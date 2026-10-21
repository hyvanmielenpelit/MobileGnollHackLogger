# AI Telemetry

The Admin page's **AI Telemetry** tab shows what the Overseer chat, title generation and GnollBench cost
and consume, how available the providers were to the chat, how many people use Overseer, and how each
user uses the system AI configurations and their own keys. The server side is `AiTelemetrySummaryService`
(`Overseer/Services/Telemetry/`); the client is `admin/telemetry-tab/`.

The tab has three sub-tabs, each a place in the URL (`admin-page-urls.md`): **Summary**
(`/admin/telemetry`, the default: the summary cards and the *Rate Limit Partitions* table), **Models**
(`/admin/telemetry/models`: the breakdown, § 7) and **Users** (`/admin/telemetry/users`: the Users view,
§ 12). A panel is mounted when its sub-tab is first selected and then kept, hidden, so the Users list is
not fetched until it is first shown, and each list keeps its state across switches.

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

The **Keys** filter on the sub-tab row (§ 7) selects *System AI configurations* (the default), *User
keys* or *Both*, for every sub-tab. User-key rows show the user's current name. Calls on a user's own key are logged without content.
**An incognito turn on a user's own
key writes nothing** — no usage row and no attempt row. Incognito turns on a system configuration keep
recording as before (they spend the operator's quota), and confidential turns record on both key sources.

## 7. The breakdown: badges, search, provider filter, sort and the sticky bar

The breakdown, on the **Models** sub-tab, shows one card per key source, user, provider, model and role
group; the list is labeled *Usage by model and role* (`aria-label`), with no section heading. Each card
is titled with the model name (§ 5), with the model id and the provider badge on the line under it, then a
badge row: the role (*Chat*, *Title Generation*, *Benchmark*, *Other*, each in its role color), the
thinking level and, under *Keys: Both*, the key (*System key*, *User key: alice*). With *Show separately*
a card has one thinking badge — the level, or *None sent* / *Not recorded* in a neutral badge; with
*Group together* it has one badge per level its rows recorded, in `ThinkingLevelOrder.json` order, and
none for *None sent* or *Not recorded*.

The filter bar over the cards has two rows. The first holds the **search** (model name, model id,
provider, and a user key's user name; whitespace-separated terms must all match, matches are highlighted
in the title and the id; Escape clears it), the *Thinking levels* select and the **Sort** popover. The
second holds the *Roles* checkboxes and, from two providers up, the **provider toggles**: pressed toggles
OR together and none pressed shows every provider, while no role checked shows nothing. *Clear filters*
clears the search and the provider toggles. The search, matching and highlighting are the model pickers'
(`shared/model-picker/model-picker-view.ts`); the filtering and sorting are pure functions in
`telemetry-breakdown.ts`.

**Sort** is a field — *Requests* (the default, highest first), *Est. cost*, *Input (uncached)*, *Cache
read*, *Cache created*, *Output*, *Cache hit*, *Avg duration*, *Provider errors*, *Model name* or
*Provider* — and an order, *Highest first* / *Lowest first* or *A–Z* / *Z–A*. A card without a figure for
the field (an unpriced cost, no cache hit or duration, provider errors *In GnollBench* or without attempts)
sorts last in both orders. Ties go by title, then thinking level in the catalog order, then the card key.
*All shown* sums the shown cards and stays last. The charts dialog does not take the search, the provider
toggles or the sort.

The **Date range** and **Keys** controls sit on the sub-tab row, right-aligned after the tabs, each with a
visible inline label, in a `role="group"` *Date range and keys* that is a sibling of the tablist, not
inside it. Below about 48 rem of the row they take their own line above the tabs. Every sub-tab honors
both. A chevron button (*Date range and keys*, `aria-expanded`) at the end of the group collapses it to
one line, *Last 30 days · System AI configurations*. The tab header and that row form one sticky head at
the top of the tab; the head's height is kept in `--tl-sticky-clearance`, so a focused or scrolled-to
element stops below it.

The **Request Governor** card and the *Rate Limit Partitions* heading carry the scope tag *live*, as
*Overseer Activity* carries *all keys*: they show the governor's current state and ignore both the range
and the keys.

## 7a. Usage of a model by user

Each model card (not *All shown*) has a *pie-chart* icon button at the end of its title, *Usage by user:
{model} · {role}* (plus the level under *Show separately* and the key badge's text where there is one, so
every button's name is distinct). It opens the full-screen **Usage by user** dialog
(`model-users/telemetry-model-users-dialog.component.ts`), which lists every user of that model as one
full-width card.

- **Scope.** The card's provider, model id, role group and key source, over the tab's range: a *System*
  card lists the users of that model on system AI configurations; a *User key* card lists every user who
  used that model on **their own key**, not only the card's user. All thinking levels are fetched; the
  *Thinking level* filter (*All levels*, each recorded level, *None sent*, *Not recorded*; shown from two
  level values up) is preset to the card's level when the card was opened under *Show separately*, else
  *All levels*, so a system card's dialog totals equal the card's own figures. The filter applies to the
  rows before grouping.
- **Cards.** One per user, or per user and level with *Thinking levels: Show separately* (the dialog's own
  setting, stored apart from the main list's). Each card shows the rank, the user name (*Unknown user* when
  the account is gone), the thinking badges, the last request in UTC, a share bar and the figures:
  *Requests*, *Est. cost*, *Input (uncached)*, *Cache read*, *Cache created*, *Output*, *Total tokens*,
  *Cache hit*, *Avg duration*. Every additive figure carries *{p} of total*.
- **Shares and rank are of the scope**: the users matching the level filter, **before the search**. The
  search is a find tool, so a found user still shows its share of the whole and its rank in the current
  sort. The summary strip above the list shows that total: *Users*, *Requests*, *Est. cost*, *Total
  tokens* and *Top user* (rank 1, with its share of the sort field, or of requests when the sort is not an
  additive figure; the share bars follow the same rule). *All matching*, after the shown cards, sums every
  card that matches the search and the level filter, including those a batch has not shown yet.
- **Sort**: *Est. cost* (the default, highest first), *Requests*, *Input (uncached)*, *Cache read*, *Cache
  created*, *Output*, *Total tokens*, *Cache hit*, *Avg duration*, *Last request* (*Newest first* /
  *Oldest first*) or *User name* (*A–Z* / *Z–A*). A user without a figure for the field (an unpriced cost,
  no cache hit or duration, no name under *User name*) sorts last in both orders. Ties go by user name,
  then thinking level, then the card key.
- **Provider errors are not shown per user.** `ChatProviderAttempt.AspNetUserId` is set on user-key
  attempts only, so system-configuration attempts cannot be attributed to a user; the dialog leaves the
  metric out and, for system scope, says so under the summary.
- **Batches** of 50 cards, with *Show N more* and *Show all M*; focus moves to the first new card's
  title. The endpoint returns every group of the model, so sorting and shares are exact on the client.
- **Storage**: `overseer.admin.telemetry.modelUsers` = `{ version: 1, separateThinkingLevels, sort }`; the
  search and the level filter are not stored.

## 7b. Usage of a user by model

Each card of the Users view (§ 12) has a *pie-chart* icon button first among its actions, *Usage by
model: {name}* (tooltip *Usage by model*, `aria-haspopup="dialog"`). It opens the full-screen **Usage by
model** dialog (`user-models/telemetry-user-models-dialog.component.ts`, its logic in
`user-models/telemetry-user-models.ts`), the mirror image of § 7a: every model one user used, as one
full-width card each. The two dialogs share the stylesheet partial `_telemetry-usage-dialog.scss` and its
`tl-mu-*` classes; this dialog's element ids are `tl-um-*`.

- **Header.** *Usage by model: {userName}* (*Unknown user* when the account is gone), with *{Keys label} ·
  {range}* under it, and Close; the footer has *Done*.
- **Scope.** That user, over the tab's range and *Keys* filter. `GET api/admin/ai-telemetry/user-models`
  returns one row per key source, provider, model, role context and thinking level, for every role group;
  its *cost recorded since* is the earliest priced row of the included logs in the range.
- **Summary strip.** *Models* (distinct provider and model), *Requests*, *Est. cost*, *Total tokens* and
  *Top model* (*Claude 5 Opus · Benchmark · 41% of est. cost*). Under it, when the scope includes system
  keys, *Provider errors are not recorded per user for system AI configurations.* (as in § 7a), and *Cost
  recorded since {date}.* when the range starts earlier.
- **Filter bar.** A search (*Search model or provider*, `#tl-um-search`), a *Role* select (from two role
  groups up), a *Key* select (only under *Keys: Both*: *All keys*, *System AI configurations*, *User
  keys*), a thinking-level filter (from two level values up), the *Thinking levels* grouping (*Group
  together* / *Show separately*), the Sort popover and *Clear filters*, which clears the search and the
  role, key and level filters.
- **Shares and rank are of the scope** after the role, key and level filters, **before the search**, as
  in § 7a.
- **Cards.** The rank, the model name with the search highlights, the model id and the provider badge, the
  role and thinking badges and, under *Both*, *System key* or *Own key*, *Last request … UTC*, a share bar
  and § 7a's nine figures, each additive one with *{p} of total*.
- **Sort**: *Est. cost* (the default, highest first), *Requests*, *Input (uncached)*, *Cache read*, *Cache
  created*, *Output*, *Total tokens*, *Cache hit*, *Avg duration*, *Last request*, *Model name* or
  *Provider*. A card without a figure for the field sorts last in both orders.
- **Batches** of 50 cards, with *Show N more* and *Show all M*.
- **Storage**: `overseer.admin.telemetry.userModels` = `{ version: 1, separateThinkingLevels, sort }`; the
  search and the role, key and level filters are not stored.

## 8. Estimated cost

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

## 9. Chat provider availability

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

## 10. Retention

`ChatRetentionSettings:PruneChatProviderAttemptDays` (400; 0 disables) prunes the ledger as full-pass
phase 10, through `POST api/admin/maintenance/prune-chat-provider-attempts`, and in the Database tab
(policy, Next Pass Preview, both pass-step lists, a granular card, and a *Provider Attempts* history
column). See `chat-data-retention.md`.

## 11. Activity: users, chats and messages

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

## 12. The Users view: AI usage by user

The **Users** sub-tab lists each user's AI usage, so that an administrator can set limits on problem users;
the list is labeled *Users* (`aria-label`), with no section heading. It follows the *Keys* filter (§ 6),
and its scope line says what it counts:

| Keys | Lists | Scope line |
|---|---|---|
| *System AI configurations* (the default) | usage on system AI configurations (`SystemAiUsageLog`) | *System AI configurations. Chats, messages and refusals count every key.* |
| *User keys* | usage on users' own API keys (`UserKeyAiUsageLog`) | *Users' own API keys. Limits apply to system AI configurations only; chats, messages and refusals count every key.* |
| *Both* | both | *System AI configurations and users' own keys. Chats, messages and refusals count every key.* |

Showing user-key usage per user exposes nothing new: the breakdown already shows it per user under *Keys:
User keys* (§ 7). Refusals, chats and messages come from `SecurityEvent` and `OverseerDailyActivity`,
which carry no key source, so they ignore *Keys*.

The filter bar is the breakdown's: a search by user name or email (debounced 200 ms; Escape with text
clears it) and the **Sort** popover, over server paging. The sort fields are *Est. cost* (the
default, highest first), *Requests*, *Tokens*, *Busiest hour*, *Refusals*, *Last active* (*Newest first* /
*Oldest first*) and *User name* (*A–Z* / *Z–A*). The server sorts (`sort` = `cost`, `requests`, `tokens`,
`peakHour`, `refusals`, `lastActive` or `name`; `direction` = `asc` or `desc`, the default); a user with no
last request sorts last in both directions, and ties go by user name, then user id.

The cards are built like the breakdown's, on the same layout mixins and breakpoints
(`_telemetry-cards.scss`): a title row with the icon buttons at its end, the meta line *Last active …*, an
*Account state* badge row (*Blocked*, *Deletion hold*, *N refusals*), nine metrics, then the
configurations table, full width, in a keyboard-scrollable labeled region that stacks under 36 rem. The
metrics are requests (chat and title), tokens, estimated cost, the **busiest hour** (most requests in one
UTC clock hour) with *typical n per active hour*, active days, chats, messages, and the limit refusals and
provider refusals from `SecurityEvent` (security plan). Under *Both*, *Requests* carries the note *system N
· own key M* and *Est. cost* the note *own key $X*; the busiest hour, active days and requests per active
hour sum both logs per user, day and hour.

The table lists the user's configurations. A system line shows today's and this month's **effective**
counters against their limits (a daily counter whose last reset was before today reads 0 — computed, never
written, so the view never resets a counter), and the limit source (*user*, *group*, *system-wide*). An
own-key line is one per user and `UserAiModelId`, named by the `UserAiModel`'s `DisplayName`, else the
logged `ModelDisplayName`, else the model id, and tagged *own key*; it shows *—* under *Today* and *This
month* and the limit source *own key* (`LimitSource = "ownKey"`), since limits apply to system
configurations only.

The server's row carries `systemRequests`, `userKeyRequests`, `systemEstimatedCost` and
`userKeyEstimatedCost` beside the totals, each configuration line its `keySource` (a `configId` is unique
only within its key source), and the page its `keySource`.

**Busiest hour is information for setting limits, never a misuse signal.** Long play sessions
legitimately produce busy hours. Nothing labels, colors, flags, records or acts on a busy hour, and the
view says so in its own text. The only misuse-related figures are refusals the system or an
administrator already issued, and the *Blocked* state.

The **spend alert** is a separate, cost-based signal outside this view: when a non-admin user's
operator-funded estimated cost (roles 1–3) over the last 24 hours reaches
`AiUsageGuard:AlertUserDailyCostUsd`, the admin page shows an alert banner whose *Review* link opens this
view, `/admin/telemetry/users` (`security-monitoring.md` § 2). It never limits anyone.

Actions: *Usage by model* (the dialog of § 7b), *Usage charts* (the charts dialog for that user), *Manage
limits* (selects the Users tab and opens its *AI configuration access for {user}* dialog, where the Rate
Limits dialog edits each assignment; no limit editing is duplicated here), and *Block* / *Unblock*. The
Security tab's *Usage* action lands here, on the Users sub-tab, with that one user selected.

## 13. The charts dialog

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

## 14. API

| Method | Route | Returns |
|---|---|---|
| `GET` | `api/admin/ai-telemetry/summary?startDate&endDate&keys` | totals, breakdown rows, cost, availability, activity |
| `GET` | `api/admin/ai-telemetry/series?startDate&endDate&keys&roles&bucket&splitBy&userId` | the series (400 past 1,100 buckets) |
| `GET` | `api/admin/ai-telemetry/users?startDate&endDate&keys&search&userId&sort&direction&skip&take` | the Users view page (`take` 1–100; `direction` `asc` or `desc`, the default) |
| `GET` | `api/admin/ai-telemetry/model-users?startDate&endDate&keys&provider&modelId&roles` | one model's usage by user, role context and thinking level (`keys` system or user; 400 without `provider` or `modelId`) |
| `GET` | `api/admin/ai-telemetry/user-models?startDate&endDate&keys&userId` | one user's usage by key source, provider, model, role context and thinking level (`AiTelemetryUserModelsDto`; 400 `{ message: "userId is required." }` without `userId`) |

The breakdown's view settings are stored as `overseer.admin.telemetry.breakdown`
(`{ version: 1, roles, separateThinkingLevels, keys, sort, providers, toolbarCollapsed }`, `sort` being
`{ field, direction }`; a record without the last three reads them as their defaults); the search is not
stored, so an old search never hides cards on a later visit. The Users view's settings are stored as
`overseer.admin.telemetry.users.view` (`{ version: 2, sort: { field, direction } }`; a version 1 record,
`{ version: 1, sort: '<id>' }`, is read as that field, highest first), the Usage by user dialog's as
`overseer.admin.telemetry.modelUsers` and the Usage by model dialog's as
`overseer.admin.telemetry.userModels` (each `{ version: 1, separateThinkingLevels, sort }`).

## 15. Tests

`AdminTelemetrySummaryTests`, `AdminTelemetrySeriesTests`, `AdminTelemetryUsersTests` (key sources,
order, the name sort, own-key lines, the controller's pass-through), `AdminTelemetryModelUsersTests`,
`AdminTelemetryUserModelsTests`, `ActivityCounterTests`, `ActivityCountingCallSiteTests`, `SystemAiUsageRecordingTests`,
`ChatProviderAttemptLedgerTests`, `TitleAttemptClassificationTests`, `ProviderTitleUsageTests` and
`ChatProviderAttemptMaintenanceTests` in `Overseer.Tests/UnitTests/`, and the spend alert's link in
`ConfigHealthServiceTests`; the client specs beside the tab (sub-tabs, panels, the *live* tags, the badge
layout), the Users view (`telemetry-users-view.spec.ts`, `telemetry-users.component.spec.ts`), the charts
dialog, the Usage by user dialog (`telemetry-model-users.spec.ts`,
`telemetry-model-users-dialog.component.spec.ts`) and the Usage by model dialog
(`telemetry-user-models.spec.ts`, `telemetry-user-models-dialog.component.spec.ts`), and the sub-tab paths
in `admin-url.spec.ts`.
