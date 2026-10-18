# AI Telemetry

The Admin page's **AI Telemetry** tab shows what the Overseer chat, title generation and GnollBench cost
and consume, how available the providers were to the chat, how many people use Overseer, and how each
user uses the system AI configurations. The server side is `AiTelemetrySummaryService`
(`Overseer/Services/Telemetry/`); the client is `admin/telemetry-tab/`.

## 1. Sources

| Table | Written by | Holds |
|---|---|---|
| `SystemAiUsageLog` | `SystemAiConfigService.RecordUsageAsync` / `RecordCallsUsageAsync` | one row per recorded turn, title or GnollBench job on a **system AI configuration**: user, configuration, model, role context, thinking level, tokens, duration, estimated cost |
| `UserKeyAiUsageLog` | `UserKeyUsageRecorder` | the same for turns, sub-agent runs and titles on a **user's own key**; no session, message or content; deleted with the user |
| `ChatProviderAttempt` | `ChatProviderAttemptRecorder` | one row per **provider attempt** of a chat or title call (one HTTP request, or a governor refusal before one was sent); no content, headers or secrets; a user id only on user-key rows; pruned after 400 days |
| `OverseerDailyActivity` | `ActivityCounter` | chats and messages created per user per UTC day, and one anonymous row per day for incognito chats and messages; no content; deleted with the user |
| `SecurityEvent` | see `security-monitoring.md` | the refusal counts of the Users view |

## 2. Role groups

`AiUsageRoles.GroupOf(roleContext)`:

| Group | RoleContext values |
|---|---|
| Chat | 1 (chat turn), 3 (sub-agent) |
| Title Generation | 2 |
| Benchmark | 4 benchmark, 5 question generation, 6 rubric check, 7 suite description, 8 report pack |
| Other | any other value |

The Request Volume card and the breakdown use the same groups. Config Analytics is unchanged.

## 3. Token figures and their normalization

`InputTokens` has different meanings by role, and the stored meaning is left alone (the quota counters
and Config Analytics read it):

- Chat rows (1, 3): the prompt **minus cache reads** (cache writes included).
- GnollBench rows (4–8): the **whole prompt**.
- Title rows (2): the whole prompt — real usage when the provider reported it, else a chars/4 estimate.

The tab normalizes on read, per group of one role context, so the result is exact:
`PromptTokens = input + cacheRead` for roles 1 and 3, `input` otherwise, and
`UncachedInputTokens = max(0, prompt − cacheRead − cacheWrite)`. The cache hit ratio is
cache read ÷ prompt. Before this, the tab counted every `InputTokens` as uncached, which double-counted
cache writes for chat and cache reads and writes for GnollBench and made the hit ratio too low.

## 4. Thinking level

`SystemAiUsageLog.ThinkingLevel` and `UserKeyAiUsageLog.ThinkingLevel` store the level a call sent, `""`
when it sent none, and (on the system log) `null` on rows written before the column existed. `""` and
`null` differ on purpose: OpenAI has a real reasoning effort `none`, so no word can mean "not sent". The
breakdown shows *None sent*, *Not recorded* or the level, and can group or separate levels.

## 5. Model names

A breakdown row shows the catalog model name (`ModelMetadataService.GetMetadata(...).DisplayName`), else
a retired entry's name, else the model id.

## 6. Key sources

The toolbar's **Keys** filter selects *System AI configurations* (the default), *User keys* or *Both*.
User-key rows show the user's current name. Calls on a user's own key are logged without content.
**An incognito turn on a user's own
key writes nothing** — no usage row and no attempt row. Incognito turns on a system configuration keep
recording as before (they spend the operator's quota), and confidential turns record on both key sources.

## 7. Estimated cost

Cost is priced **per call at record time**, because long-context rates (Claude Haiku 5.5 above 100,000
prompt tokens, GPT-5.x above 272,000) apply per request and a sum of a turn's tokens cannot be repriced.
`EstimatedCost` (`decimal(18,8)`, USD) and `PricingSource` (`catalog`, `custom`) are set when the model
has a price card; a null cost means **unpriced**, never $0.

- Chat turns record the main model's own cost; sub-agent runs record their own rows.
- Sub-agent runs record their cost on their own row, even when canceled or failed.
- GnollBench rows record the cost of **every call the job paid for**, through
  `RecordCallsUsageAsync`, which derives tokens and cost from the same call list (several GnollBench rows
  used to record less than was paid for: a repair turn, last-attempt cache counts, missing cache tokens).
- Titles record real usage from the response (`IAiProvider.ParseTitleUsage`) and its cost, whether or not
  the title came back empty; without reported usage, the chars/4 estimate and a single-request estimate.

Rows written before this existed stay unpriced (no backfill); the Estimated Cost card says *n of N
requests unpriced* and *Cost recorded since {date}* when the range starts earlier.

## 8. Chat provider availability

`AgentLoopRunner` classifies every attempt of every agent run into GnollBench's `ProviderAttemptOutcome`
classes (`ai-benchmark-provider-availability.md`); the chat turn, each sub-agent run and title generation
now save them as `ChatProviderAttempt` rows, in a `finally`, so completed, errored, thrown and abandoned
turns all record. Title generation builds its records with `TitleAttemptEvidence` from the same evidence
(governor refusals, cancels, exceptions, error responses with their bodies and rate-limit headers, and
the 2xx response once read).

Two summary cards show the chat's availability. The Provider Availability card shows the provider error
rate (provider errors ÷ valid attempts, the denominator GnollBench uses: `Ok`, the provider classes and
the unattributed classes), under it the count and its cluster-adjusted Wilson 95% interval over
10-minute blocks (`BenchmarkProviderAvailability`), marked *few attempts* below 100, then first-attempt
success, chat turns ended by a provider error, title generation (*k of n attempts*), and *Recorded since*
when the range starts earlier. *Not recorded* until the ledger has a row. Once it has one, the Provider
Errors by Class card shows the provider error count, the counts per class, the unattributed classes with
the inclusive rate, and our own failures (excluded from the rate). Benchmark-role cards show *In
GnollBench*: GnollBench reports its own availability.

**Server-key sub-agent runs** (`AI:*` keys) are operator-funded but belong to no configuration: they write
a usage row through `SystemAiConfigService.RecordServerKeyUsageAsync` with configuration id 0, role 3,
the run's tokens and its own estimated cost, and the model name `Server key: <model>`; their attempt rows
are *System* with no configuration id. Configuration id 0 is attribution only, not a foreign key. The
model-level aggregates group by provider and model and include these rows; the Users view shows them as a
*Server key: …* configuration line, and no counter changes for them.

## 9. Retention

`ChatRetentionSettings:PruneChatProviderAttemptDays` (400; 0 disables) prunes the ledger as full-pass
phase 10, through `POST api/admin/maintenance/prune-chat-provider-attempts`, and in the Database tab
(policy, Next Pass Preview, both pass-step lists, a granular card, and a *Provider Attempts* history
column). See `chat-data-retention.md`.

## 10. Activity: users, chats and messages

Chats and messages are purged by retention, so counting them would give a history that falls away into
the past. `OverseerDailyActivity` counts them **when they are created**, so purging never changes it:

| Counted | Where |
|---|---|
| A chat (`ChatsCreated`; `ConfidentialChatsCreated` when created confidential) | `ChatController.Send` creating a chat, `ChatController.AttachSnapshot` (also `GnollHackChatsCreated`), `SessionController.Create` (`GnollHackChatsCreated` when it is a GnollHack session) |
| A visible message the user sent (`UserMessages`) | `ChatService`, after the message is saved; hidden messages (the GnollHack client's automatic initial prompt) are not counted |
| A saved assistant reply (`AssistantMessages`) | `ChatService`, after the reply is saved; an incognito reply is not counted |
| Incognito chats and user messages | **only on the day's anonymous row** (no user id) |

`ActivityCounter` adds with one atomic `UPDATE` in its own scope, inserts the day's row when none
existed, and repeats the update once when two first events of a day race on the unique
`(DateUtc, AspNetUserId)` index (unfiltered, so the one anonymous row per day is unique too). A failure is
logged and never fails a chat. Upgrading an existing chat to confidential is not a creation.

- **Active users** are distinct users with a chat or a visible message in the range; a user who only
  chats incognito does not count as active, and the card says so.
- **New users** are active users whose first activity day ever falls in the range — in the first weeks
  after deployment every active user is new.
- **No backfill:** counting starts on the deployment day, and the card and charts say *Counted since
  {date}* for a range that starts earlier.
- **Account deletion** removes the user's rows by cascade, when the deletion goes ahead; a deletion held
  for review (`security-monitoring.md` § 10) keeps them until an administrator approves it.

The privacy framework's incognito promise covers the four chat tables; this counter is outside them and is
disclosed in the privacy notice (*Activity counts*).

## 11. The Users view: system AI usage by user

Below the breakdown, *System AI usage by user* lists each user's usage of **system AI configurations only**
(user keys are the user's own business), so that an administrator can set limits on problem users. Search
by user name or email, sort by cost (the default), requests, tokens, busiest hour, refusals or last
activity, server paging.

Each card shows requests (chat and title), tokens, estimated cost, the **busiest hour** (most requests in
one UTC clock hour) with *typical n per active hour*, active days, chats, messages, the limit refusals and
provider refusals from `SecurityEvent` (security plan), and the *Blocked* and *Deletion hold* states; then
a table of the user's configurations with today's and this month's **effective** counters against their
limits (a daily counter whose last reset was before today reads 0 — computed, never written, so the view
never resets a counter), and the limit source (*user*, *group*, *system-wide*).

**Busiest hour is information for setting limits, never a misuse signal.** Long play sessions
legitimately produce busy hours. Nothing labels, colors, flags, records or acts on a busy hour, and the
view says so in its own text. The only misuse-related figures are refusals the system or an
administrator already issued, and the *Blocked* state.

The **spend alert** is a separate, cost-based signal outside this view: when a non-admin user's
operator-funded estimated cost (roles 1–3) over the last 24 hours reaches
`AiUsageGuard:AlertUserDailyCostUsd`, the admin page shows an alert banner linking to this tab
(`security-monitoring.md` § 2). It never limits anyone.

Actions: *Usage charts* (the charts dialog for that user), *Manage limits* (selects the Users tab and
opens its *AI configuration access for {user}* dialog, where the Rate Limits dialog edits each assignment;
no limit editing is duplicated here), and *Block* / *Unblock*. The Security tab's *Usage* action lands
here with that one user selected.

## 12. The charts dialog

**Charts** opens a full-screen dialog on the shared GnollBench figure layer (`overseer_frontend` skill):
nine composed bitmap figures — *Requests*, *Estimated cost (USD)* (optionally cumulative), *Tokens*,
*Cache hit ratio*, *Mean duration*, *Provider error rate* (Benchmark lines dropped: GnollBench reports its
own), *Active users*, *Chats created*, *Messages* — shown, copied and downloaded identically, singly or as
a ZIP (`ai-telemetry_<figure>_<yyyyMMdd_HHmmss>.<ext>`, `ai-telemetry_charts_<stamp>.zip`).

The settings sidebar has **Data** (keys, roles, bucket, split by, cumulative cost, the user filter chip),
**Theme** and **Charts** (the figure style panel's appearance and timeline families; Chat-Consistency-only
controls hidden through `hiddenKeys`), and **Download** (size and format).

- `GET api/admin/ai-telemetry/series?startDate&endDate&keys&roles&bucket&splitBy&userId`.
- **Buckets:** `auto` = day up to 45 days, week (Monday 00:00 UTC) up to 180, else month (the 1st, UTC);
  empty buckets are included. A range with more than 1,100 buckets is refused with 400.
- **Split by:** none, role, provider, model, key source, or user (only with user keys; system rows form
  one *System AI configurations* line under *Both*). The 8 lines with the most requests are kept; the
  rest merge into one *Other* line.
- **Activity figures** ignore *Split by* and *Keys*; buckets before the counting start are gaps, not zero;
  under a user filter the incognito lines are omitted.
- **User filter:** only that user's usage rows, user-key attempt rows (system attempt rows carry no user)
  and activity rows.
- **Storage:** `overseer.admin.telemetry.charts` and its `.chartSize`, `.figureStyle`,
  `.figureStylePanel.open` and `.download` keys, each `{ version: 1, … }`; the user filter is not stored.

## 13. API

| Method | Route | Returns |
|---|---|---|
| `GET` | `api/admin/ai-telemetry/summary?startDate&endDate&keys` | totals, breakdown rows, cost, availability, activity |
| `GET` | `api/admin/ai-telemetry/series?startDate&endDate&keys&roles&bucket&splitBy&userId` | the series (400 past 1,100 buckets) |
| `GET` | `api/admin/ai-telemetry/users?startDate&endDate&search&userId&sort&skip&take` | the Users view page (`take` 1–100) |

The breakdown's view settings are stored as `overseer.admin.telemetry.breakdown`
(`{ version: 1, roles, separateThinkingLevels, keys }`); the Users view's as
`overseer.admin.telemetry.users.view`.

## 14. Tests

`AdminTelemetrySummaryTests`, `AdminTelemetrySeriesTests`, `AdminTelemetryUsersTests`,
`ActivityCounterTests`, `ActivityCountingCallSiteTests`, `SystemAiUsageRecordingTests`,
`ChatProviderAttemptLedgerTests`, `TitleAttemptClassificationTests`, `ProviderTitleUsageTests` and
`ChatProviderAttemptMaintenanceTests` in `Overseer.Tests/UnitTests/`; the client specs beside the tab,
the Users view and the charts dialog.
