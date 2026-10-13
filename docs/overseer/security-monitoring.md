# Security Monitoring, Limits and Account-Deletion Holds

How Overseer and the GnollHack website keep per-user AI limits enforceable, block a user from the AI
chat, attribute provider safety actions to a user, record security events, harden sign-in on the
website and the game APIs, and hold an account deletion for administrator review. This is not legal
advice: the owners should check their obligations as an operator of an AI service with legal counsel,
in particular for account-deletion holds, which delay an erasure request.

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
have no configuration and stay unchecked and uncounted.

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
characters, which is shown to the user; an administrator cannot block themselves. The fields live on
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
passwords or headers. The IP address is stored on authentication and rate-limit events only.

`SecurityEventRecorder` (Overseer) queues events to a bounded channel (1,000, oldest dropped) drained by
a background loop in its own scope; callers never wait for the database and never see a failure.
Identical events — same kind, user (or IP when there is no user), configuration and code — within ten
minutes of a run's first occurrence extend that run (`Count`, `LastOccurredUtc`). The website writes
through `GnollHackServer.Data.Security.SecurityEventWriter`: the same run aggregation, directly through
the request's context, at most 30 writes per IP address a minute.

| Kind | Written by | User | IP |
|---|---|---|---|
| `LoginFailed` (`Detail` `unknown-user` when no account matched), `LoginLockedOut`, `TwoFactorFailed` | Overseer `AuthController` login and 2FA; `SessionController.Create`; website `Login`; game APIs | when matched | yes |
| `RateLimitRejected` (`Code` = policy) | Overseer and website rate limiters; the game API sign-in throttle (`game-api-signin`) | when signed in | yes |
| `QuotaRefused` (`Code` user / group / system), `AccessDenied` (`Code` disabled / not-assigned) | `SystemAiConfigService` | yes | no |
| `ProviderRefusal`, `ProviderSafetyBlock` (`Code` = finish / block reason), `ProviderPolicyRejection` (`Code` = sanitized error code of a final 4xx attempt naming a policy) | after every operator-funded chat turn and sub-agent run (`ProviderRefusalEvents`), incognito included; never for titles | yes | no |
| `AttachmentRejected` (`Code` count / validation / content / malware / scan-failed) | `ChatService` attachment checks | yes | no |
| `ChatReported` | `ChatController.ReportMessage`, after the email is sent; the report itself is not stored | yes | no |
| `AdminBlocked`, `AdminUnblocked` | `OverseerAccessService` | the blocked user | no |
| `AccountHoldPlaced`, `AccountHoldReleased` (`Detail` = reason), `AccountDeletionHeld` (`Code` manual / automatic) | holds and the website deletion page | the account's user | no |

**Retention:** `ChatRetentionSettings:PruneSecurityEventDays` (180) prunes runs last seen before the
window (full-pass phase 9, `POST api/admin/maintenance/prune-security-events`, the Database tab), except
the events of a user with an active account hold or a pending deletion request, which are kept until it
is decided. Events cascade with the account.

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

**Accounts created with lockout disabled** keep `LockoutEnabled = 0` and are never locked. Count them:

```sql
SELECT COUNT(*) FROM AspNetUsers WHERE LockoutEnabled = 0;
```

If any exist, enabling it for them is a one-line `UPDATE AspNetUsers SET LockoutEnabled = 1 WHERE
LockoutEnabled = 0;` that the developer runs after review; no migration does it.

## 7. The Security tab

Admin → **Security** shows, for the shared date range, summary cards (sign-in failures, lockouts, limit
refusals, provider refusals and policy hits, rejected attachments, reports, blocks, deletion holds with
a link to the Users tab's *Deletion requests*), an events card list (search by user name, a *Kind*
facet, server paging) whose cards offer *Block* / *Unblock* and *Usage* (the user's AI usage in the AI
Telemetry tab), and the safety-identifier lookup. Every count is a word plus a number.

| Method | Route | Returns |
|---|---|---|
| `GET` | `api/admin/security/summary?startDate&endDate` | counts per kind (sums of `Count`), distinct users and IPs per kind, pending deletion requests, active holds |
| `GET` | `api/admin/security/events?startDate&endDate&kinds&userId&search&skip&take` | events newest first, `take` 1–100 |
| `GET` | `api/admin/security/safety-identifier/{identifier}` | `{ userId, userName }` or 404 |
| `POST` / `DELETE` | `api/admin/users/{userId}/overseer-block` | block (`{ reason }`) / unblock |

## 8. Game API sign-in hardening

The GnollHack game APIs (`POST /xlogfile`, `POST /bones`, `POST /api/savefiletracking/create` and `/use`,
`POST /api/replay`) check a user name and password on every request. The clients in the wild stop
retrying only on **403**; every other status and every timeout is retried by the main-menu timer every
two seconds without backoff. So every refusal a player can trigger is a 403:

| Outcome | Status | Body (`text/plain; charset=utf-8`) | Headers |
|---|---|---|---|
| Wrong password, unknown user, unconfirmed account | 403 | *"Sign-in failed."* | — |
| Throttled (account or IP) | 403 | *"Too many failed sign-in attempts. Try again in {n} minutes."* | `Retry-After` |
| Identity lockout | 403 (was 423) | *"This account is temporarily locked. Try again in {n} minutes."* | `Retry-After` from `LockoutEnd` |
| 2FA account | 412 | *"This account requires two-factor authentication."* | — |
| Antiforgery mismatch | 401 | *"AntiForgeryToken is invalid."* | — |
| Banned | 423 (unchanged) | unchanged — the ban checks stay in the controllers | — |

`GameApiAuthenticator` does what the four controllers repeated: the antiforgery check, the in-memory
`GameApiSignInThrottle` (10 failed passwords per account in 15 minutes → 15 minutes refused; 100 failures
per IP in 15 minutes → 15 minutes refused; a refused request costs no password hash, writes no request
log row and extends nothing; a correct password clears the account's failures), and the password check
through `CheckPasswordSignInAsync` without issuing a cookie, with 2FA detected exactly as
`PasswordSignInAsync` decides it. Failures count toward the throttle, never toward Identity lockout. The
throttle is per process: with more than one instance each keeps its own windows (the deployment is a
single instance). `ReplayController` is not implemented after sign-in and answers a correct password
with the same 403.

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
configured for that proxy; the account limits keep working either way. The same applies to Overseer's
`Login` policy, which partitions by remote IP.

## 9. Acceptable use

The privacy notice has an *Acceptable use* section: Overseer is for GnollHack and NetHack questions,
the providers' own policies apply to every message, and misuse can lead to limits or a block of AI
access. Its data map lists *Security events* and *Provider safety identifier*.

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
carries the user name, the request time, the hold type and a link to `OverseerAdminUrl` +
`?tab=users&subtab=deletions` (website only; empty means no link) — **never the reasons**. The website
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

## 11. Deferred

- **GnollHack client fixes** (a separate plan in the GnollHack repository): honor `Retry-After`;
  exponential backoff in the main-menu timer; treat 401 / 423 / 429 as "stop until the next trigger";
  drop a queued item after a 4xx other than 408 / 429; null-check `Content-Type` in `SendBonesFile`; send
  an app version with the credential check and save tracking.
- **Answering looping banned clients more cheaply**: revisit if `RequestLogs` shows it matters.
- **Wrapping tool results in the untrusted-content boundary**: changes what the production chat prompt
  sees, so it belongs in a GnollBench-validated round.
- **CSP `report-to`** and **`ForwardedHeaders`**: deployment decisions.

## 12. Tests

`LimitEnforcementTests`, `OverseerBlockTests`, `SafetyIdentifierTests`, `SecurityEventRecorderTests`,
`SecurityEventMaintenanceTests`, `GameApiSignInThrottleTests`, `GameApiSignInResponsesTests`,
`AccountHoldEvaluatorTests`, `AccountErasureTests`, `AccountDeletionReviewTests` and
`AccountDeletionReviewEmailTests` in `Overseer.Tests/UnitTests/`. No test project hosts the website,
so the game API controllers, the account pages and the deletion page are checked manually.
