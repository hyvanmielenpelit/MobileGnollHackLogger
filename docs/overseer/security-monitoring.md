# Security Monitoring, Limits and Account-Deletion Holds

How Overseer and the GnollHack website keep per-user AI limits enforceable, block a user from the AI
chat, attribute provider safety actions to a user, record security events, harden sign-in on the
website and the game APIs, and hold an account deletion for administrator review. This is not legal
advice: the owners should check their obligations as an operator of an AI service with legal counsel,
in particular for account-deletion holds, which delay an erasure request. The Users tab's per-user
actions (lock out, ban, game upload restrictions, delete), the Bulk Actions wizard and the admin log are
in [`admin-user-management.md`](admin-user-management.md).

## 1. Do the AI providers already monitor for misuse?

Partly, and in a way that makes it the operator's concern rather than removing it.

- **Anthropic** runs safeguards on API traffic and recommends a hashed per-user id
  (`metadata.user_id`); persistent policy violations can lead to suspension of the account's access.
- **OpenAI** recommends moderation, sign-in, input limits, user reporting and a hashed per-user
  `safety_identifier`, so that it can block one end user instead of the whole organization.
- **Google** scans Gemini API traffic for Prohibited Use Policy violations and may flag a project for
  review. The Gemini API has no per-request end-user field.

The providers enforce against the **API key's owner — the operator**. Without a user identifier one
abusive user on a system configuration exposes every user of that key. The providers also do nothing
for attacks on the web application itself, for cost abuse (OWASP LLM10 *Unbounded Consumption*), for
prompt injection through tool content, or for knowing which of our users did something.

Overseer's measures are therefore proportionate ones for a game assistant: enforceable limits and a
block (§§ 2–3), provider attribution through safety identifiers (§ 4), a security event log with an
admin view (§§ 5–7), acceptable-use terms in the privacy notice, sign-in hardening (§§ 8–9) and a
review step before deleting an account with evidence of illegal use (§ 10).

**Moderation was considered and left out by the owners' decision.** A NetHack assistant discusses
killing monsters all day; a general-purpose classifier's *violence* categories would bury real signals
in false positives, and the providers' own safeguards — now attributable to a user — remain.

## 2. Per-user limits

`SystemAiConfigService.GetAndCheckSystemConfigAsync` decides whether a user may use a system AI
configuration now:

1. The configuration-wide limits are checked first.
2. A **disabled** user assignment denies outright (*"Your access to this model has been turned off by
   an administrator."*) — no fall-through to group or system-wide access. `SettingsService.GetResolvedSystemModelsAsync`
   leaves such a configuration out of the model list, so the picker never offers it.
3. An **enabled** user assignment must be under its own limits **and**, when the user also has enabled
   group assignments for the configuration, at least one of them must be under its limits.
4. Without a user assignment, an enabled group assignment under its limits grants access.
5. Otherwise only a system-wide configuration grants access.

A limit of **null or 0 means no limit**. A refusal names the limit and when it resets, for example
*"You have reached your daily limit for this model (50 requests). It resets at 00:00 UTC."*; monthly
limits reset on the 1st of the month (UTC), lifetime limits never.

Every place that uses a system configuration checks it: the chat turn's chosen model, the fallback
model a turn without a model id takes (the GnollHack handoff's initial prompt arrives that way; the
next resolved model is tried after a refusal), the title fallback (checked against the role the
fallback list was built with — 2, or 1 when no title configuration exists), and the sub-agent branches
1.1 and 2D (a configuration refused in 2A is not retried in 2D). Sub-agent runs on an `AI:*` server key
(branches 1.2 and 2E) have no configuration; they are checked by the usage guard below and recorded as a
`SystemAiUsageLogs` row with configuration id 0, role 3 and the model name `Server key: <model>`. On a
turn that runs on a user's own model, only an administrator's sub-agent may fall back to a server key:
for anyone else 2B and 2C do not stand the server key in for a missing user key, and 2E refuses with
*"No API key is available for the sub-agent's model. …"*.

### Daily usage guard

`AiUsageGuard` caps what one account can spend on operator-funded models per UTC day. It runs on the
chat-role check (`checkRole == 1`) of a configuration reached through **group or system-wide access**,
and on sub-agent runs on a server key. It steps aside for an **enabled per-user assignment** (an
administrator's explicit grant) and, while `AiUsageGuard:ExemptAdmins` is true, for administrators. A
dedicated title configuration (role 2) is never guarded; the title fallback is, when no title
configuration exists, because it is then checked as role 1.

It sums the user's `SystemAiUsageLogs` rows since 00:00 UTC, roles 1–3 (GnollBench roles 4–8 are
excluded):

| Setting (`AiUsageGuard:*`) | Default | Refuses when | Event code |
|---|---|---|---|
| `MaxUserDailyCostUsd` | 50.00 | the sum of `EstimatedCost` reaches it | `guard-cost` |
| `MaxUserDailyRuns` | 2,000 | the count of role 1 and 3 rows reaches it (bounds unpriced runs) | `guard-runs` |

`0` turns either off. A refusal reads *"You have reached today's limit for AI use in the Overseer. It
resets at 00:00 UTC."* and is recorded as `QuotaRefused`. To lift the limit for one heavy player, give
them an explicit per-user assignment, or raise the value.

`AiUsageGuard:AlertUserDailyCostUsd` (20.00) raises an admin alert banner, linking to the AI Telemetry
tab, when one or more non-admin users' operator-funded spend over the last 24 hours reaches it. It never
limits anyone.

The request-level controls on operator-funded turns — the message-length cap, the per-user concurrency
gate and the `session/create` turn limiter — are in `data-privacy-framework.md` § 3.7.

**Ordering never grants access.** A user's model order lives in `UserSystemModelOrder`, separate from
assignments; `PUT api/settings/systemmodels/reorder` accepts only ids the user can see, the reset
endpoint deletes the order rows, and the admin's reorder writes the same rows. Before this, reordering
created unlimited user assignments for any configuration id the client sent.

**GnollBench usage (roles 4–8) is logged and priced but consumes no quota counter.** Role 2 counts on
the title counters, roles 1 and 3 on the chat counters.

### Review query for existing self-created assignments

Rows created by the old reorder behavior are not deleted automatically. This read-only query lists
assignments to non-system-wide configurations with no per-user limit and no enabled group assignment
behind them; review each listed user in the Users tab:

```sql
SELECT u.UserName, c.DisplayName, a.Id AS AssignmentId, a.IsEnabled
FROM UserSystemAiApiConfigurations a
JOIN SystemAiApiConfigurations c ON c.Id = a.SystemAiApiConfigurationId
JOIN AspNetUsers u ON u.Id = a.AspNetUserId
WHERE c.IsSystemWide = 0
  AND ISNULL(a.MaxDailyChatRequests, 0) = 0 AND ISNULL(a.MaxMonthlyChatRequests, 0) = 0 AND ISNULL(a.MaxTotalChatRequests, 0) = 0
  AND ISNULL(a.MaxDailyChatTokens, 0) = 0 AND ISNULL(a.MaxMonthlyChatTokens, 0) = 0 AND ISNULL(a.MaxTotalChatTokens, 0) = 0
  AND NOT EXISTS (SELECT 1 FROM UserGroups ug
                  JOIN GroupSystemAiApiConfigurations g ON g.GroupId = ug.GroupId
                  WHERE ug.AspNetUserId = a.AspNetUserId AND g.SystemAiApiConfigurationId = c.Id
                    AND g.IsEnabled = 1);
```

## 3. Blocking a user from the AI chat

An administrator blocks a user from the Users tab (or the Security tab) with a written reason of 1–256
characters, which is shown to the user; an administrator cannot block themselves or another
administrator. The fields live on
`ApplicationUser` (`OverseerBlockedUtc`, `OverseerBlockReason`, `OverseerBlockedByUserId`).
`OverseerAccessService` reads the block with a 30-second cache (entries declare a size, as the cache has
a size limit) evicted on block and unblock, and writes `AdminBlocked` / `AdminUnblocked` events.

A blocked user cannot start AI work on any key but keeps sign-in, reading, export, deletion and settings:

| Where | Result |
|---|---|
| `POST api/chat/send` (`[RequireOverseerAccess]`) | 403 `{ code: "overseer_blocked", message, reason }` |
| `POST api/session/create` (anonymous; checked after the password passes) | the same 403 |
| `ChatService.GenerateAndBroadcastMessageAsync` (both ways a turn starts) | an `error` chat event with `ErrorCode = "overseer_blocked"` and the reason in `Detail` |
| Title generation, `delegate_to_subagent` | skipped / refused |

The chat shows a persistent banner and disables sending on either refusal.

**The snapshot guard's automatic block** (§ 3a) sets the same three fields, with
`OverseerBlockedByUserId = system:snapshot-guard`, so the Users tab's and the Security tab's **Unblock**
lift it and every row of the table above behaves as for an administrator's block. It writes an
`AiChatBlocked` admin-log entry by the actor *Snapshot guard* and an `AutoBlocked` event, never
`AdminBlocked`, so the two counts stay apart.

## 3a. The snapshot guard

The GnollHack client sends texts a player or another player can write: the AI snapshot (names, labels,
`#call` names, annotations; and, through a shared bones level, named monsters, the ghost and engravings
read), the in-game message history, the directory manifest, and the results of its client tools. Any HTTP
client with an account can send the same fields, so the game's own input limits bound nothing. The guard
(`Overseer/Services/Security/`) scans every such text **before it is stored or reaches the model**, and
refuses a text that addresses the AI, **never the session**: the game client shows any refusal of
`session/create` as a credentials or connection problem, so the player is told in the chat that opens.

**Detection is heuristic: a paraphrased instruction without a marker word passes.** The guard is one
layer; the model-side defenses GnollBench measures (the experimental changes `own-findings-v1`,
`game-text-data-v1` and `board-as-data-v1`) are the others.

**What is scanned, and where.**

| Entry point | Texts | On a refusal |
|---|---|---|
| `SessionController.Create` | the flattened `SnapshotText` (or the sanitized `SnapshotHtml`), the full `MessageHistory`, the `DirectoryManifest` (channels `create`, `history`, `manifest`) | the session is still created and the handoff proceeds; the refused text is not stored with the session, neither row nor file (it is kept only in the quarantine, below). A refused snapshot is replaced by a notice row for the model (`Role = system`, not a snapshot, so `hasGameSnapshot` stays false), and the session's `SnapshotRefusedUtc` / `SnapshotRefusalCode` are set |
| `ChatController.AttachSnapshot` | the flattened snapshot (`attach`) | `422 { code: "snapshot_refused", error }`; nothing is written or superseded. An accepted snapshot clears the session's refusal |
| `ToolExecutor`, every client tool | the result (`tool-<name>`); `refresh_snapshot` as a snapshot, `get_full_message_history` as a history, `get_directory_listing` as a manifest, the rest as client data | the result fails with the model's notice as its error and the refusal code on `ToolResult.SnapshotRefusalCode`; the agent loop yields a `snapshot_refused` chat event (`{ code, message }`); the session's refusal is set. An accepted `refresh_snapshot` clears it |
| GnollBench suite import, the Snapshot Viewer | boards | **not scanned**: administrator-only and deliberate, and the injection-resistance suite depends on its planted board reaching the candidates |

**The scanner** (`SnapshotGuardScanner`, pure, every pattern bounded to a line or a short window, a 2-second
match timeout read as a finding `scan-timeout`) reports fixed codes only, the one thing stored about a
finding on its event:

| Code | Strength | Matches |
|---|---|---|
| `address` | weak | an AI word (`AI`, assistant, Overseer, chatbot, language model, LLM, GPT, ChatGPT, Claude, Gemini) within 60 characters of an imperative, or `AI ASSISTANT:` (not a pet's status line `Assistant: HP:…`) |
| `override` | strong | ignore / disregard / forget … previous instructions or prompts, ignore *your* rules (not "ignore the rules"), new instructions, system prompt, "you are now an … AI / unrestricted …", "from now on, you …" (not the quest's "… from now on."), "act as an AI / system …", your new role, developer mode, jailbreak, DAN mode |
| `authority` | strong | from / by / a note from the (Overseer / GnollHack) team, developers, devs, admins, operators; "Overseer devs"; `[INST]`, `<<SYS>>`, `<|im_start|>`, `### System`, turn markers at a line's start, `SYSTEM:`; a header of the prompt's own structure (`Game Context Snapshot`, `Full Message History`, `Game Directory Manifest`, `Snapshot guard notice`); `<untrusted_document_context` / `<untrusted_game_snapshot` |
| `tool` | weak | a registered tool name (read from `IToolHandler` at first use, so a new tool is covered) within 40 characters of call, use, run, invoke, execute or trigger |
| `exfil-image` | strong | Markdown image syntax to a URL that is not the game's own site |
| `exfil-url` | weak | a URL other than the game's own sites (gnollhack.com, nethack.org, nethackwiki.com, hyvanmielenpelit.fi, github.com/hyvanmielenpelit), or "send / post / upload / forward … to" an address |
| `hidden-tag` | strong | a tag character, U+E0000–U+E007F |
| `hidden` | weak | any other format or control character (tab, line feed and carriage return excepted), U+2028, U+2029, a lone surrogate |
| `directive` | weak | end every answer with, always say / reply … (at a clause's start), respond only with, repeat after me, say the word, tell the player that |

**Verdict:** *Injection* for a strong code or two codes, *Suspicious* for one weak code, *Clean* otherwise.
Calibrated on two real exports of one game (a clean board, which yields no code, and one with three planted
texts) and on every line of GnollHack's message corpora and C string literals (79,055 lines: rumors,
oracles, quest texts, epitaphs, the encyclopedia, the tribute novels): no strong code, and seven weak ones,
in a comment, an encyclopedia URL and font URLs of the HTML dump code.

**The client's own headers are not impersonated prompt headers.** The client's directory manifest
begins with the line `=== Game Directory Manifest ===` (`GHGame.GenerateDirectoryManifest`) and its full
message history with `=== Full Message History ===` (`GHGame.ExportFullMessageHistory`), and both phrases
are in the `authority` family. Before the scan, `SnapshotGuardScanner.Scan` drops the first line of a
`Manifest` text when it is exactly `ClientManifestHeader` (a trailing carriage return aside), and of a
`MessageHistory` text when it is exactly `ClientHistoryHeader`. Each header is stripped only from its own
kind and only as the first line, so the same phrase anywhere else, spelled otherwise, or at the start of
another kind of text is still found. Without the strip every `get_directory_listing` result was an
`injection-own` refusal (`tool-get_directory_listing:authority`), which counts toward the automatic block,
and every `get_full_message_history` result that still began with its header an `injection-foreign` one.

**Attribution.** For a snapshot the scanner reads the text section by section, by the headings of
`AI_SNAPSHOT_FORMAT_VERSION` 16, and decides whether the parts **only the current player can write** reach
the verdict on their own:

| Section | Who can write there |
|---|---|
| the lines before `Map:` (the character's line), `Inventory:` (labels are stripped from bones), `Pets:` (a bones pet is no longer tame), `Discoveries:`, `Logged events:`, `Dungeon overview:` (annotations) | the player |
| `Notable locations:` (named monsters, the ghost), `Latest messages:` (engravings read, anything printed), and every other section | possibly another player |

A message history is possibly another player's; a manifest and other client data are the player's. A heading
the table does not know continues the section before it. The code is `injection-own`, `injection-foreign`,
`suspicious-own` or `suspicious-foreign`. A player who walks into a bones level with a hostile ghost name is a
victim: the snapshot is refused, the player is told it may not be theirs and that leaving the level removes
it, and nothing counts toward a block.

**Modes**, a runtime setting (below):

| Verdict | `Enforce` | `Observe` | `Off` |
|---|---|---|---|
| Clean | delivered | delivered | not scanned |
| Suspicious | refused, banner *may contain*, model notice, event; never counts toward a block | delivered; event with `Detail` `observed;…` | not scanned |
| Injection | refused, banner *contains*, model notice, event; `injection-own` counts toward the automatic block | delivered; event with `Detail` `observed;…`; no block | not scanned |

One `SnapshotRejected` event per request, `Code` the strongest verdict, `Detail` (≤ 256, fixed vocabulary)
`<channel>:<codes>` per channel joined by `;`, for example `create:address,tool;history:directive`, prefixed
`observed;` in Observe. No text, no excerpt on the event; the text itself goes to the quarantine.

**The quarantine.** The guard keeps each text it found something in, so an administrator can check
whether the finding was right. The scanner is heuristic and has been wrong (the client's own headers,
above), and a block that rests on its codes alone cannot be re-verified; the text is what makes it
checkable.

- **What is kept.** One `SnapshotGuardQuarantine` row per text with a finding, linked to the request's
  `SnapshotRejected` event (a foreign key with a database cascade; an event that folds a run keeps every
  refusal's texts as rows of its own). Beside the encrypted text, in clear: the channel, subject and text
  kind, the level and codes, whether the text was the player's own, whether it was withheld (false in
  Observe), the chat mode, the session reference when the caller knew one, the full and kept lengths,
  whether it was cut, the SHA-256 of the full text's UTF-8, the key version and the user id. Built only
  when `SnapshotGuard:QuarantineEnabled` is on, the request has a user id and the scan found something;
  **Observe keeps the text too**, although it was delivered, because calibrating a rule needs it. Off
  scans and keeps nothing.
- **Cut, hashed whole.** A text over `QuarantineMaxChars` (100,000) is cut, one character earlier rather
  than split a surrogate pair, and marked truncated; the hash is of the full text, so it matches the
  client's original either way.
- **Encrypted under the content keyring.** `SnapshotQuarantineCipher` seals the text with AES-GCM under
  the keyring's active version (`PrivacySettings`, `data-privacy-framework.md` § 5.7), with the
  associated data `snapshot-quarantine:v1:` plus the user id, so a row re-pointed at another user does
  not decrypt. Not `AesEncryptionKey`, which is API-key material only, and no per-row data key: a row
  lives at most the retention window, so a key rotation waits it out. `Open` never throws: a retired
  version, another user's associated data or a corrupt value reads as *"[This content could not be
  decrypted.]"*. With no usable keyring the event is recorded without its texts and a warning is logged
  once per process; the guard's decision is unchanged, and `ConfigHealthService`'s
  `content-keyring-unusable` alert reports the ring itself.
- **In every chat mode.** Normal, confidential and incognito alike, as the chat's mode descriptions and
  the privacy notice state (`data-privacy-framework.md` § 8.7). The mode is stored, not consulted
  (`SnapshotGuardScope`): session create passes `normal` or `confidential` from the request and no
  reference, since the session does not exist yet; an attach to an existing session takes the mode from
  the reference (an `eph_` reference is `incognito`, a numeric one reads the caller's own row), and an
  attach that creates a session takes it from the request's flags with no reference; a client tool
  result takes it from the turn's session and its confidential scope. Enforcement already rests on
  content-free records written in every mode, and the text is game data the client uploaded, never a
  chat message or an attachment, and only what the guard flagged.
- **The queue holds ciphertext only.** The guard seals before it calls the recorder. The recorder holds at
  most `SecurityEventRecorder.QuarantineQueueBudgetBytes` (64 MB, counted as UTF-16) of quarantine
  ciphertext at once; an event over the budget is queued without its texts, with a warning at most once a
  minute (*"The security event queue's quarantine budget is full; refused game text is not being
  kept."*). An entry releases its bytes when it is dequeued, before the write, and when the full channel
  drops it.
- **Retention.** `ChatRetentionSettings:PruneSnapshotQuarantineDays` (30; `0` disables it) deletes rows by
  the time they were received, first thing in full-pass phase 9 and in the *Prune security events*
  action. **Rows of held users are not exempt**: the event stays for the review, the text does not need
  to. Deleting an event deletes its rows, so the event prune and an account's erasure take them too. The
  quarantine is not in the user's personal-data export.
- **Reading it.** `GET api/admin/security/events/{id}` (§ 7) decrypts the newest 50 texts of the event
  and reports how many there are in all. When it returns any, it writes one access journal row
  (`ChatAccessAuditLog`, action `SecurityContentRead`): the administrator, the subject user, the session
  reference when every returned text shares one, whether any text came from a confidential or incognito
  chat, the address, and the detail `security-event:{id}; texts:{n}`. A failed journal write is logged and
  does not fail the read. Downloads are built in the browser from that response and are not journaled
  again.

**The automatic block** (`SnapshotGuard:AutoBlockEnabled`, on), decided right after an `injection-own` refusal
in Enforce: the user's earlier `SnapshotRejected` events with `Code = injection-own` and no `observed;` prefix,
within the last `AutoBlockWindowDays` (30) days today included, are counted by **distinct UTC day**, today is
added, and the user is blocked at `AutoBlockDistinctDays` (3). Distinct days, not counts: the recorder folds
identical events within ten minutes into one run, and three reconnects with the same pet name are one
warning. Never for an administrator, never in Observe or Off, never for a `suspicious-*` or
`injection-foreign` finding, never twice. The block is § 3's, with the reason *"Suspended automatically: game
snapshots containing instructions addressed to the AI assistant were uploaded on 3 separate days. An
administrator has been notified and can lift the suspension."* **Never an Identity ban**: `IsBanned` stops
every sign-in including the game, and stays an administrator's decision from the Users tab.

**Reported** by the admin alerts below and, when `SnapshotGuard:NotificationEmail` is set
(`gnollhack@hyvanmielenpelit.fi`; empty disables it), one e-mail per block: the user name, the time and where
to review it (Admin → Security → Events, `/admin/security/events`); never the text or the codes.

**What the player and the model are told.** Fixed texts, by code and by what was refused
(`SnapshotGuardService.UserMessageFor`, `ModelNoticeFor`); the server writes the banner's text, which the
session DTO carries as `snapshotRefusal { code, occurredUtc, message }` and the chat event as `{ code,
message }`. The chat's `alert-warning` banner sits under the blocked banner and is cleared by an accepted
snapshot. For an injection in the player's own text: *"The game snapshot was not loaded. It contains text
written as instructions to an AI assistant, in a part of the board you write yourself (a name, a label or a
note). Rename or remove it and reconnect. The chat works without the snapshot. An administrator has been
notified; uploading such text on separate days suspends AI chat access automatically."* The model's notice
row begins `Snapshot guard notice:` — not *Game snapshot …*, which `ChatService.IsGameSnapshotMessage` would
read as a board — and says the snapshot was withheld, no board is available, and not to guess at it.

**The mode is a runtime setting.** The append-only `SnapshotGuardSettings` table (`Mode`, `SetAtUtc`,
`SetByUserId`) holds the history; the newest row is the mode, and with no row `SnapshotGuard:Mode` seeds it
(`Observe` in `appsettings.json`; missing or unreadable means `Enforce`, the closed end, and an unreadable stored
value reads as `Enforce` too). `SnapshotGuardModeService` caches it for 30 seconds and writes through on a set,
so every entry point follows a change in the Security tab within that window, without a restart.

| Method | Route | |
|---|---|---|
| `GET` | `api/admin/security/snapshot-guard` | `{ mode, setAtUtc, setByUserName, seeded, autoBlockEnabled, autoBlockDistinctDays, autoBlockWindowDays, notificationEmailConfigured, quarantineEnabled, quarantineMaxChars, quarantineRetentionDays }` |
| `POST` | `api/admin/security/snapshot-guard` | `{ mode }`: appends a row even when unchanged and returns the state; 400 for an unknown mode |

**Admin alerts** (`ConfigHealthService`): *warning* `snapshot-guard-observe` while the mode is Observe and
`snapshot-guard-off` while it is Off, each with *Change* → `/admin/security/settings`; *warning*
`snapshot-refusals:{yyyyMMdd}:{count}` while `SnapshotRejected` events fall in the last 7 days (*"N game
snapshots were refused for text addressed to the AI in the last 7 days (M users)."*); *error*
`snapshot-autoblock:{newest event id}` while `AutoBlocked` events do; these two with *Review* →
`/admin/security/events`. The changing part of each id makes a
dismissed alert reappear on new events.

**Settings** (`SnapshotGuard`, read on each call by `SnapshotGuardOptions.Read`, which never throws):
`Mode` (the seed), `AutoBlockEnabled` (true; an unreadable value is true), `AutoBlockDistinctDays` (3; 1–30),
`AutoBlockWindowDays` (30; 1–365, at least the day count), `NotificationEmail`, `QuarantineEnabled` (true; an
unreadable value is true), `QuarantineMaxChars` (100,000; 1,000–1,000,000, a value outside the range is the
default). The quarantine's retention is `ChatRetentionSettings:PruneSnapshotQuarantineDays` (30).

**What it does not do.** It does not scan bones files, which the website receives in the game's save format:
a hostile name in a bones level is caught where it does harm, in a victim's snapshot, and its uploader is
found through the ghost's name and the Users tab (`IsBonesBanned`). It changes nothing the model is sent for a
clean text, so `CandidateSystemPromptSha256` and GnollBench comparability are untouched, and it does not scan
GnollBench boards.

## 4. Provider safety identifiers

`SafetyIdentifierService` derives a 32-byte key once by HKDF-SHA256 from `AesEncryptionKey` (info
`overseer:provider-safety-identifier:v1`; no new secret) and computes `"u"` plus the first 32 hex digits
of HMAC-SHA256 over the user id: 33 characters, deterministic, not reversible without the key.

It is sent with every **operator-funded** call to an **official** endpoint — chat turns on a system
configuration, title generation on a system configuration, sub-agent runs on a system configuration or
an `AI:*` server key — incognito and confidential turns included; never on a user's own key, never to a
custom endpoint (which may reject an unknown field), never for GnollBench. OpenAI receives
`safety_identifier`, Anthropic `metadata.user_id`; Google has no such field and receives nothing.

**Lookup:** when a provider reports abuse for an identifier, the Security tab's *Find user by safety
identifier* (`GET api/admin/security/safety-identifier/{identifier}`) computes the identifier of every
user in turn and returns the match — O(users), fine for the account counts here.

## 5. Security events

`SecurityEvent` rows hold identifiers and codes only — never message text, attempted user names,
passwords or headers. The IP address is stored on authentication and rate-limit events only. The one
exception sits beside the event, never on it: the game text the snapshot guard found something in, kept
encrypted in the quarantine (§ 3a).

`SecurityEventRecorder` (Overseer) queues events to a bounded channel (1,000, oldest dropped) drained by
a background loop in its own scope; callers never wait for the database and never see a failure.
Identical events — same kind, user (or IP when there is no user), configuration and code — within ten
minutes of a run's first occurrence extend that run (`Count`, `LastOccurredUtc`). The website writes
through `GnollHackServer.Data.Security.SecurityEventWriter`: the same run aggregation, directly through
the request's context, at most 30 writes per IP address a minute.

| Kind | Written by | User | IP |
|---|---|---|---|
| `LoginFailed` (`Detail` `unknown-user` when no account matched), `LoginLockedOut`, `TwoFactorFailed` | Overseer `AuthController` login and 2FA; `SessionController.Create`; website `Login`; game APIs | when matched | yes |
| `RateLimitRejected` (`Code` = policy) | Overseer and website rate limiters, Overseer's with `Detail` = the endpoint's route template (the path without its query string when no route matched; never the raw path, which carries session ids and `eph_` references); the game API sign-in throttle (`game-api-signin`) and the session-create throttle (`session-create`), `Detail` = the limit (`pair`, `account` or `ip`); the `session/create` initial-turn limiter (`session-create-turn`) | when signed in | yes |
| `QuotaRefused` (`Code` user / group / system), `AccessDenied` (`Code` disabled / not-assigned) | `SystemAiConfigService` | yes | no |
| `QuotaRefused` (`Code` guard-cost / guard-runs) | `AiUsageGuard`, through `SystemAiConfigService` and `delegate_to_subagent` | yes | no |
| `ProviderRefusal`, `ProviderSafetyBlock` (`Code` = finish / block reason), `ProviderPolicyRejection` (`Code` = sanitized error code of a final 4xx attempt naming a policy); `Detail` = `req:<provider request id>` when the call reported one, the id an operator quotes to the provider (`[A-Za-z0-9_.-]`, at most 64) | after every operator-funded chat turn and sub-agent run (`ProviderRefusalEvents`), incognito included; never for titles | yes | no |
| `AttachmentRejected` (`Code` count / validation / content / malware / scan-failed, `Detail` = `{content type};{byte size}`, a side empty when the check had not reached it: no `Detail` for `count`, the declared type only for `validation` (`image/png;`), both for the rest; the type keeps only letters, digits, `/`, `.`, `+` and `-`, at most 64; never the file name or the scanner's text) | `ChatService` attachment checks | yes | no |
| `ChatReported` (`Code` = `msg:<message id>`) | `ChatController.ReportMessage`, after the email is sent; the report itself is not stored, and the event names the reporter and the reported message's id, never its content. The id is deliberate: the same action emails the whole conversation to the operator's mailbox, so the id reveals nothing the report did not send, and it is what lets the event be checked later; it also makes each reported message its own run | yes | no |
| `SnapshotRejected` (`Code` injection-own / injection-foreign / suspicious-own / suspicious-foreign, `Detail` = the channels' codes, `observed;` first in Observe) | `SnapshotGuardService`, from session create, snapshot attach and client tool results (§ 3a); the texts themselves are in the quarantine (§ 3a) when it is on, linked to the event | yes | no |
| `AutoBlocked` (`Code` snapshot-injection, `Detail` = the reason) | `OverseerAccessService.BlockAutomaticallyAsync`, for the snapshot guard (§ 3a) | the blocked user | no |
| `AdminBlocked`, `AdminUnblocked` | `OverseerAccessService` | the blocked user | no |
| `AccountHoldPlaced`, `AccountHoldReleased` (`Detail` = reason), `AccountDeletionHeld` (`Code` manual / automatic) | holds and the website deletion page | the account's user | no |
| `AdminLockedOut`, `AdminUnlocked`, `AdminBanned`, `AdminUnbanned`, `AdminSignedOut`, `AdminEmailConfirmed`, `AdminTwoFactorReset`, `AdminEmailChanged` (`Detail` = reason) | the Users tab's per-user actions and the Bulk Actions runner (`AdminUserManagementService`); the durable record naming the administrator is the admin log | the account's user | no |

**Retention:** `ChatRetentionSettings:PruneSecurityEventDays` (180) prunes runs last seen before the
window (full-pass phase 9, `POST api/admin/maintenance/prune-security-events`, the Database tab), except
the events of a user with an active account hold or a pending deletion request, which are kept until it
is decided. Events cascade with the account. The quarantine's texts are pruned sooner, after
`PruneSnapshotQuarantineDays` (30), held users included, and go with their event (§ 3a).

## 6. Sign-in rate limit and Identity lockout

Overseer's `Login` rate-limit policy is a fixed window per remote IP,
`PrivacySettings:RateLimits:LoginPermitsPerMinute` (10), on `/api/auth/login`, `/api/auth/login/2fa` and
`/api/session/create`. A rejection answers 429 with *"Too many sign-in attempts. Please wait a minute and
try again."* — except on `/api/session/create`, which answers **401**: the GnollHack handoff retries
every status but 404, 400 and 401 seven times, and stops on 401.

**Identity lockout: 10 consecutive failed passwords lock the account for 15 minutes**, set identically
in both apps from an `IdentityLockout` section (`MaxFailedAccessAttempts`, `LockoutMinutes`) in
**both** `appsettings.json` files — keep them identical, because the website and Overseer read and write
the same lockout fields on the same accounts. Identity counts consecutive failures and resets on a
successful sign-in, so ordinary typing mistakes never accumulate; combined with the per-IP limits an
attacker gets at most ten guesses per account per 15 minutes, while NIST SP 800-63B only requires
capping consecutive failures at 100. The website's password page counts failures toward lockout; its
2FA and recovery-code pages always did. Its Lockout page names the duration.

**Only the website's and Overseer's own sign-in pages count toward Identity lockout.** The game APIs
and `/api/session/create`, which GnollHack calls with its stored credentials and retries, check the
password with `lockoutOnFailure: false` and count failures toward the game sign-in throttle of § 8
instead, so a game client retrying a mistyped password can never lock the account. A locked-out account
is still refused there.

**A banned or unconfirmed account** is refused by Identity before the password is checked
(`GnollHackSignInManager` makes a ban refuse every sign-in path; both apps require a confirmed account).
Both sign-in pages then check, in order: a locked-out account is refused first, before any password
check; a wrong password counts toward lockout like any other wrong password; only the correct password
reveals *"This account has been banned."* (on the website followed by a contact line) or, in Overseer
only, that the email is not confirmed. The ban reason is never shown.

**Signed-in sessions end within `IdentityLockout:SecurityStampValidationMinutes` (5)** of a new security
stamp — a ban, an admin lockout, *Sign out everywhere* or a two-factor reset: both apps set
`SecurityStampValidatorOptions.ValidationInterval` from it, instead of the 30-minute default. Overseer's
chat hub applies the same interval to an open chat connection, which ends no later than its first hub
call once that interval has passed.

**Accounts created with lockout disabled** keep `LockoutEnabled = 0` and are never locked. Count them:

```sql
SELECT COUNT(*) FROM AspNetUsers WHERE LockoutEnabled = 0;
```

If any exist, enabling it for them is a one-line `UPDATE AspNetUsers SET LockoutEnabled = 1 WHERE
LockoutEnabled = 0;` that the developer runs after review; no migration does it.

## 7. The Security tab

Admin → **Security** has three sub-tabs, `/admin/security` (*Summary*), `/admin/security/events`
(*Events*) and `/admin/security/settings` (*Settings*), under a sticky header that holds the date range
shared with the AI Telemetry tab (an info tip says it applies to *Summary* and *Events*) and an icon
*Refresh*:

- **Summary**: for the date range, summary cards (sign-in failures, lockouts, limit refusals, provider
  refusals and policy hits, rejected attachments, reports, snapshot refusals, blocks with an *Automatic*
  line, deletion holds with a link to the Users tab's *Deletion requests*).
- **Events**: the *Find user by safety identifier* lookup, whose result offers *Usage* (the user's AI usage
  in the AI Telemetry tab) and *Events* (narrows the list below to that user), then the events card list
  (search by user name, a *Kind* facet, server paging) whose cards offer *Details* (every card, with or
  without a user), and *Block* / *Unblock* and *Usage* when the event has a user. A card whose event has
  kept game text shows a *Text kept* tag. The Users tab's and the deletion requests' *Security events*
  actions open this sub-tab narrowed to the user.
- **The event detail dialog**, opened by *Details*, is the evidence pack of one event. It shows the kind
  and user, the time and run, *What this means* (a plain-language explanation of the kind), *Stored
  fields* (every field, ids included, and the configuration's name), *Refused game text* when the
  quarantine holds any (§ 3a: per text its tags, *Injection* / *Suspicious*, the player's own or possibly
  another player's, *Withheld* or delivered in Observe, and the chat mode; the channel and text kind; the
  codes with a one-line meaning each; the length, *truncated to N* when cut; the SHA-256 with a copy
  button; the session reference; and *Open in viewer*, which opens the text read-only in the dialog's own
  Snapshot Viewer, and *Download .txt*; *Showing the newest 50 of N.* when there are more), and *Related
  records*. Those are linked to the event by user, address and time, over the run widened by ten
  minutes each side, at most 20 rows a list, and a list appears only for the kinds it applies to and when
  it has rows:
  - *Terms accepted* (every kind with a user, not windowed): the document, version, time and source of
    each Terms of Service acceptance, so the pack says what the user agreed to and when;
  - the user's other events in the window (every kind);
  - for the account kinds (`Admin*`, `AutoBlocked`, `AccountHold*`, `AccountDeletionHeld`): the admin-log
    entries with the user as subject, the active hold, and the newest deletion request;
  - for `SnapshotRejected`: the user's sessions whose snapshot was refused in the window (never the title,
    which can be the user's own text and is enveloped in a confidential session);
  - for `ChatReported`: the reported message's id, session, time and role, and whether it still exists
    (never its content);
  - for the `Provider*` kinds and `QuotaRefused`: the provider attempts of the same user on the same
    configuration, and the user's usage rows. The user filter is deliberate, so another user's calls on a
    shared configuration are never listed; but provider attempts on an operator-funded (system)
    configuration are written without a user id (`ai-telemetry.md`), so for those events the attempts list
    is usually empty and the usage rows are what remains;
  - for `LoginFailed`, `LoginLockedOut`, `TwoFactorFailed` and `RateLimitRejected`: other events from the
    same address, and the website's `RequestLogs` rows of that address whose span overlaps the window (path,
    method, response code, count, the attempted user name; never `Message` or `RequestData`).

  For an event with a user, links lead on and close the dialog first: *Usage*, *Admin log* (the Admin log
  narrowed to the user), *Events of this user*, and *Deletion requests* when there is a hold or a
  request. A text that cannot be decrypted shows the unreadable notice in place of its two actions.
  **Download event (.zip)** saves
  `security-event-{id}-{yyyyMMdd-HHmm}.zip`, built in the browser from the same response:
  `security-event-{id}.json` (the detail, each text replaced by its file name), `security-event-{id}.md`
  (a readable summary with each related list as a table) and `texts/{n}-{channel}.txt` per kept text, as
  received. An event without kept text has no `texts/` folder. Reading the detail is what the access
  journal records (§ 3a); the downloads are not journaled again.
- **Settings**: the *Snapshot guard* fieldset (§ 3a: a *Mode* radio group, Off / Observe / Enforce, each
  with its sentence; a choice applies at once, the group is disabled while it is saved and restored on a
  failure; a status line *Changed 24 Oct 2026 14:02 by …* or *Not set yet: seeded from the
  configuration.*; the configured block rule in one sentence; and the quarantine in another, *Refused game
  text is kept encrypted for 30 days for review, in every chat mode.* or *Refused game text is not kept
  (QuarantineEnabled is off).*).

Every count is a word plus a number.

| Method | Route | Returns |
|---|---|---|
| `GET` | `api/admin/security/summary?startDate&endDate` | counts per kind (sums of `Count`), distinct users and IPs per kind, pending deletion requests, active holds |
| `GET` | `api/admin/security/events?startDate&endDate&kinds&userId&search&skip&take` | events newest first, `take` 1–100, each with `quarantineCount` |
| `GET` | `api/admin/security/events/{id}` | one event: its fields, `explanation`, `systemConfigurationName`, the newest 50 kept texts decrypted with `quarantineTotalCount`, and `related`; journals the read of any text (§ 3a); 404 `{ message }` for an unknown id |
| `GET` | `api/admin/security/safety-identifier/{identifier}` | `{ userId, userName }` or 404 |
| `GET` / `POST` | `api/admin/security/snapshot-guard` | the snapshot guard's state / set the mode (`{ mode }`), § 3a |
| `POST` / `DELETE` | `api/admin/users/{userId}/overseer-block` | block (`{ reason }`) / unblock |

## 8. Game API sign-in hardening

The GnollHack game APIs (`POST /xlogfile`, `POST /bones`, `POST /api/savefiletracking/create` and `/use`,
`POST /api/replay`) check a user name and password on every request. The clients in the wild stop
retrying only on **403**; every other status and every timeout is retried by the main-menu timer every
two seconds without backoff. So every refusal a player can trigger is a 403:

| Outcome | Status | Body (`text/plain; charset=utf-8`) | Headers |
|---|---|---|---|
| Wrong password, unknown user (and a banned or unconfirmed account with a wrong password) | 403 | *"Sign-in failed."* | — |
| Throttled (pair, account or IP) | 403 | *"Too many failed sign-in attempts. Try again in {n} minutes."* | `Retry-After` |
| Identity lockout | 403 (was 423) | *"This account is temporarily locked. Try again in {n} minutes."*; beyond 24 hours *"This account is locked until {yyyy-MM-dd HH:mm} UTC."* | `Retry-After` from `LockoutEnd`, at most 366 days |
| Banned, correct password | 403 | *"This account has been banned."* | — |
| Unconfirmed email, correct password | 403 | *"Confirm your email address before signing in to GnollHack online features."* | — |
| 2FA account | 412 | *"This account requires two-factor authentication."* | — |
| Antiforgery mismatch | 401 | *"AntiForgeryToken is invalid."* | — |
| Game log or bones upload restriction | 423 (unchanged) | unchanged — these checks stay in the controllers, after sign-in | — |

`GameApiAuthenticator` does what the four controllers repeated: the antiforgery check, the in-memory
`GameApiSignInThrottle`, and the password check through `CheckPasswordSignInAsync` without issuing a
cookie, with 2FA detected exactly as `PasswordSignInAsync` decides it. Failures count toward the
throttle, never toward Identity lockout. The throttle has three sliding limits:

| Limit | Key | Failures | Window | Block |
|---|---|---|---|---|
| Pair | account and address | 10 | 15 minutes | 15 minutes |
| Account | account, across addresses | 50 | 60 minutes | 30 minutes |
| Address | address, across accounts | 100 | 15 minutes | 15 minutes |

The account key is the upper-cased user name, whether or not the account exists. The throttle is
checked before the account is looked up, so a refused request costs no lookup and no password hash,
writes no request log row and extends nothing. Only a wrong password or an unknown user counts
(`GameApiSignInClassifier`); a banned or unconfirmed account with the correct password is told so and
is not counted. A correct password clears the pair's and the account's failures, not the address's.
Because the tight limit is per pair, guessing from another address cannot keep a player out of the
game: that address meets the account limit only after 50 failures an hour.

Overseer's `/api/session/create` uses the same throttle, classifier and limits (its own `GameApiSecurity`
section) and answers every refusal with 401 `{ code, message }`: `throttled`, `locked_out`, `banned`,
`email_not_confirmed` or `invalid_credentials`. Its refusals are the `session-create` events of § 5.

The throttle is per process: the website and Overseer keep separate windows, so do several instances
(the deployment is a single instance), and a restart clears them. **An administrator's Unlock in
Overseer does not clear it**, since the website's windows are out of Overseer's reach; a pair block lasts
at most 15 minutes and an account block 30. `ReplayController` is not implemented after sign-in and
answers a correct password with the same 403. Details and the full response tables are in
[`admin-user-management.md`](admin-user-management.md) § 7.

The website's rate limiter has two policies per remote IP: `GameApiFlood`
(`GameApiSecurity:FloodRequestsPerMinute`, 600 — far above one client's worst loop) on the five POST
actions, and `AccountPages` (`GameApiSecurity:AccountPagePermitsPerMinute`, 20) on `Login`,
`LoginWith2fa`, `LoginWithRecoveryCode`, `Register`, `ForgotPassword`, `ResendEmailConfirmation` and
`ResetPassword`. A rejection is a 429 with `Retry-After`, a plain-text body (never empty — the bones
client reads the media type) and a `RateLimitRejected` event. The thresholds are in the website's
`GameApiSecurity` section.

**The antiforgery secret is no longer stored in `RequestLogs`.** Every request row used to store the
client's token in `RequestAntiForgeryToken` — on a valid request, the server's secret. `DbLogger` now
stores `match` or `mismatch`, and mismatch log messages carry no token values. Scrub the existing rows
once (count first, then update):

```sql
SELECT COUNT(*) FROM RequestLogs WHERE LEN(RequestAntiForgeryToken) > 8;
UPDATE RequestLogs SET RequestAntiForgeryToken = NULL WHERE LEN(RequestAntiForgeryToken) > 8;
```

### Deployment check before relying on per-IP limits

Per-IP limits need real client addresses. Query the most recent `RequestLogs` rows whose `Type` is
GameLog (1) and confirm `UserIPAddress` varies across users rather than being one proxy or
load-balancer address — remembering that a row keeps the IP of its first request, because identical
requests are folded into one row (`Count`, `LastDate`):

```sql
SELECT TOP 50 RequestUserName, UserIPAddress, LastDate FROM RequestLogs WHERE Type = 1 ORDER BY LastDate DESC;
```

If it is one address, set `GameApiSecurity:PerIpLimitsEnabled` to `false` until forwarded headers are
configured for that proxy; the account limits keep working either way. Neither app calls
`UseForwardedHeaders`, so behind a proxy every client also shares one pair key per account: the pair
limit then behaves like a per-account limit of 10 failures, and the address limit could refuse every
player at once. The same applies to Overseer's `Login` policy and to `/api/session/create`'s throttle,
which partition by remote IP.

## 9. Acceptable use

The privacy notice has an *Acceptable use* section: Overseer is for GnollHack and NetHack questions,
the providers' own policies apply to every message, and misuse can lead to limits or a block of AI
access. It also says that game snapshots containing text addressed to the AI assistant are not delivered to
it and are recorded as security events, that the flagged text is kept encrypted for 30 days for an
administrator's review in incognito and confidential chats too, and that repeated cases can suspend AI chat
access automatically (§ 3a). Its data map lists *Security events* (snapshot guard refusals and the kept text
included) and *Provider safety identifier*.

## 10. Account-deletion holds and administrator review

Overseer cannot judge legality; it can see what the providers' safety systems reported and what an
administrator decided. A deletion is **held** when either:

- an administrator **placed a deletion hold** on the account (Users tab, a written reason of 1–512
  characters, never shown to the user) — for a report, a complaint or a provider's abuse notice traced
  through the safety identifier; or
- the account has **at least 3 serious provider safety events within 30 days**: `ProviderSafetyBlock` or
  `ProviderPolicyRejection` events whose code is on `AccountHold:SeriousProviderCodes` (by default Gemini
  `PROHIBITED_CONTENT` and `BLOCKLIST`, OpenAI `invalid_prompt`), summing each run's count. Refusals,
  usage volume, rate-limit and quota events never count. The rule only holds a deletion for review; it
  never blocks, limits or flags the user anywhere else.

`AccountHoldEvaluator` decides it (pure, shared by both apps) over what `AccountHoldQueries` loads;
the options are the `AccountHold` section, identical in both `appsettings.json` files.

**On the website** (`DeletePersonalData`), after the password check: a pending request shows *"Your
request to delete your account is being reviewed by an administrator (since {date}). You will receive an
email when it is done."*; a held deletion inserts an `AccountDeletionRequest` (the reasons, user id, name
and email snapshots), writes `AccountDeletionHeld` and shows *"Your request to delete your account has
been received. It needs to be reviewed by an administrator before it can be completed. You will receive
an email when it is done."* — the user is told it needs review, not why. Nothing is deleted and the user
stays signed in. Otherwise the account is erased at once, as always.

**The review email** goes to `AccountHold:ReviewNotificationEmail` (default
`gnollhack@hyvanmielenpelit.fi`; empty disables it) at most once per user account per
`ReviewEmailMinIntervalHours` (6), decided over every request of the user and stored on the request
(`LastReviewEmailUtc`), so it survives restarts and a user retrying the page cannot flood the mailbox. It
carries the user name, the request time, the hold type and a link to `OverseerAdminUrl` with
`/users/deletions` appended to its path, before any query string, and a trailing `/` trimmed
(`https://overseer.example/admin` → `https://overseer.example/admin/users/deletions`;
`AccountDeletionReviewEmail.ReviewLink`; website only; empty means no link) — **never the reasons**.
A link in an older email, `?tab=users&subtab=deletions`, still opens the sub-tab: the Admin page
replaces it with the path. The website
sends it; the shared `EmailSender` starts sends with `WaitUntil.Started`, so only a failure to start is
seen, and a failed start leaves the stamp unchanged so the next attempt retries.

**In Overseer**, the Users tab's *Deletion requests* sub-tab lists requests (status facet, default
*Pending*), each with *Why it was held* (stored) and *Now:* (evaluated now), *Overdue* at 25 days
(`ReviewHighlightDays`) — data-protection law generally expects an answer to an erasure request within a
month, and a refusal needs a lawful ground. **This system does not decide when a refusal is lawful**;
that is for the owners and their counsel. An administrator decides every request explicitly:

- **Approve and delete** (type the user name to confirm) runs `AccountErasure.EraseAsync` — the same
  code as self-service deletion — marks the request *Completed*, clears its user id, name and email, and
  emails *"Your account has been deleted."*. Refused while Overseer's `DumpLogPath` or
  `ConversationsDataLocation` is missing (they must equal the website's values; a configuration-health
  alert names a missing one). The bones step deletes absolute paths built from the website's `BonesPath`,
  so the two apps are expected to run on one host; the result reports bones files and folders it did not
  find.
- **Decline** with a note (for administrators; not emailed): the account and any hold remain, and the
  user is emailed that the request could not be completed and whom to contact.

Releasing a hold does not approve a pending request. While any request is pending, the admin alert
banner shows *"N account deletion(s) await review (M overdue)."* with a *Review* link; its id carries the
newest request id, so a dismissed alert reappears for a new request. Security events of a held user are
kept past retention until the request is decided; an approved deletion removes them with the user.

| Method | Route | Action |
|---|---|---|
| `GET` | `api/admin/account-deletion-requests?status&search&skip&take` | requests, counts, whether the erasure paths are configured |
| `POST` | `api/admin/account-deletion-requests/{id}/approve` | `{ confirmUserName }`; 400 on a wrong name, 409 while paths are missing |
| `POST` | `api/admin/account-deletion-requests/{id}/decline` | `{ note }` |
| `POST` / `DELETE` | `api/admin/users/{userId}/account-hold` | place (`{ reason }`) / release |

**What a contested block or ban rests on.** Proof in a dispute is, in practice, a record kept before the
dispute, and the system keeps these: the Terms of Service the user accepted and when
(`UserTermsAcceptances`); the stated rule (the privacy notice says repeated snapshot refusals can suspend
AI chat access automatically, § 9, and the automatic block's reason names the rule, § 3a); the facts relied
on, as security events with their dates, codes and counts, attributed to the account, and for a snapshot
guard refusal the refused text itself, in the quarantine for 30 days (§ 3a); who decided and why (the admin
log, `AdminUserAction`); and what the user was told (a block's reason is shown to them, § 3; a ban's reason
never is, § 6). One event's *Download event (.zip)* (§ 7) gathers what the system holds about that event: its fields, the
terms acceptances, the related records its kind has (the admin-log entries for the account kinds) and the
kept text. Enforcement
rests on content-free records in every chat mode; the snapshot guard's own case is the one that needs the
text, because its only other evidence is a heuristic scanner's codes. Whether this meets a particular legal
standard, and whether a statement-of-reasons duty applies, is for the owners and their counsel.

## 11. Deferred

- **GnollHack client fixes** (a separate plan in the GnollHack repository): honor `Retry-After`;
  exponential backoff in the main-menu timer; treat 401 / 423 / 429 as "stop until the next trigger";
  drop a queued item after a 4xx other than 408 / 429; null-check `Content-Type` in `SendBonesFile`; send
  an app version with the credential check and save tracking; read a `{ code, message }` body on a refused
  `session/create` and show the message, which the snapshot guard (§ 3a) could then use to tell the player in
  the game.
- **Scanning bones files** for text addressed to the AI: they arrive in the game's save format; the guard
  catches such text in the victim's snapshot instead (§ 3a).
- **A model classifier for the snapshot guard's *Suspicious* tier**: a later decision on the Security tab's
  numbers; a classifier is itself injectable and costs a call per session.
- **Answering looping banned clients more cheaply**: revisit if `RequestLogs` shows it matters.
- **Wrapping tool results in the untrusted-content boundary**: changes what the production chat prompt
  sees, so it belongs in a GnollBench-validated round.
- **CSP `report-to`** and **`ForwardedHeaders`**: deployment decisions.
- **Keeping a blocked or banned account's evidence through a deletion request.** A block leaves
  self-service deletion open, and deletion cascades the user's security events and with them the
  quarantine; only the admin log survives. The account-deletion hold (§ 10) keeps evidence while a request
  is reviewed, but it is placed by hand or by the provider-event rule, not by a block or a ban. Whether a
  block or ban should place a hold, and for how long evidence may be kept against an erasure request, is a
  legal question for counsel; the change itself is small (`AccountHoldEvaluator` and one rule).
- **A statement of reasons for a ban.** A ban's reason is never shown to the user (§ 6), while a block's
  is. If counsel wants the user told the ground and how to contest it, that is a wording and a page, not a
  data change.
- **The quarantine in the user's own data export** (`data-privacy-framework.md` § 8.2): whether an access
  or portability request must include it is for counsel; including it is one section of the export,
  decrypted under the user's own request.
- **Saying so at the moment of refusal.** The player's refusal message (`SnapshotGuardService.UserMessageFor`)
  could add that the text is kept for 30 days for review. It is disclosed where the mode is chosen and in
  the privacy notice; adding it there changes texts the guard tests pin.
- **Marking an event reviewed or a false positive**, which would also stop a reviewed `injection-own` run
  counting toward the automatic block. It needs a column on `SecurityEvent` and a change to the block's
  query.
- **A server-side file endpoint for the event ZIP**, for scripted retrieval: one GET from the same detail.
- **Exporting the filtered event list** (JSON or CSV of a date range) from the Events header.
- **Passing the created session's id back to the quarantine rows** at session create and at a new attach.
  The row is written before the session exists; the detail's sessions list covers session create by
  `SnapshotRefusedUtc`, and a refused new attach creates no session at all.

## 12. Tests

`LimitEnforcementTests`, `OverseerBlockTests`, `SafetyIdentifierTests`, `SecurityEventRecorderTests`,
`SecurityEventMaintenanceTests`, `GameApiSignInThrottleTests`, `GameApiSignInResponsesTests`,
`GameApiSignInClassifierTests`, `GnollHackSignInManagerTests`, `SessionCreateLockoutTests`,
`AccountHoldEvaluatorTests`, `AccountErasureTests`, `AccountDeletionReviewTests` and
`AccountDeletionReviewEmailTests` in `Overseer.Tests/UnitTests/`. No test project hosts the website,
so the game API controllers, the account pages and the deletion page are checked manually.

The snapshot guard: `SnapshotGuardScannerTests` (against the two real exports in
`Overseer.Tests/Fixtures/Snapshots/`, each family, a negative corpus and attribution, and the client's own
manifest and history headers: realistic texts are clean, planted lines are still found, and a header is not
stripped from another kind), `SnapshotGuardServiceTests` (modes, the distinct-day rule and its exemptions,
the e-mail, and the quarantine: Enforce and Observe, the three chat modes, off, truncation and the full-text
SHA-256, ciphertext that never holds the plaintext, no keyring), `SnapshotGuardModeServiceTests` (the
quarantine settings' defaults and fallbacks), the guard cases of `SessionControllerCreateTests`,
`ChatAttachSnapshotTests` (the quarantine's mode and reference for a new incognito attach, for an attach
to an existing incognito session and for an attach to the user's existing confidential session), `OverseerBlockTests` and `ConfigHealthServiceTests`,
`ToolExecutorClientResultGuardTests`, and the integration tests `AdminSnapshotGuardEndpointTests`.

The quarantine and the event detail: `SnapshotQuarantineCipherTests` (a round trip; a fresh nonce
per seal; another user's associated data, a version no longer in the ring, a tampered tag and malformed
columns read as the notice; no active version throws from `Seal`), `SecurityEventRecorderTests` (quarantine rows on a new event and on an extended run, the queue
budget and its release on dequeue and on a drop, the provider request id detail),
`SecurityQueryServiceDetailTests` (decryption, the related lists by kind, another user's provider attempts
never listed, the terms acceptances, a reported message without content, the journal row only when texts
are returned, the 50-text cap, an unknown id, `QuarantineCount` on the page) and
`SecurityEventMaintenanceTests` (the quarantine prune, its dry run, zero days, held users not exempt, the
manual action's log line, the cascade from a deleted event). The client: `security-event-dialog.component.spec.ts`,
`security-event-archive.spec.ts` and `admin-security-tab.component.spec.ts`.
Every `api/admin` endpoint's authorization is pinned by `AdminEndpointAuthorizationSweepTests` and CSRF
enforcement by `AntiforgeryEnforcementTests` (`Overseer.Tests/IntegrationTests/`).
