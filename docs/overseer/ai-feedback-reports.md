# AI Feedback Reports

The Overseer chat assistant can send a short report to the development team when something in a
conversation went wrong in a way the developers can fix. The tool is `report_feedback`; administrators
read and triage the reports on **Admin → Feedback** (`/admin/feedback`), where they also turn the tool on
and off for the Overseer chat. GnollBench offers the same tool to its candidates, chosen per run in the
launcher, and stores their calls in a table of its own (§ 11); the tab's *GnollBench* sub-tab groups and
triages them. Selected issues of either sub-tab can be exported as Markdown for an AI coding assistant,
and the assistant's resolutions imported back as YAML (§ 12).

The pieces are `ReportFeedbackTool` (`Overseer/Services/Tools/`), the services in
`Overseer/Services/Feedback/` (`ReportFeedbackArguments`, `FeedbackFingerprint`, `FeedbackRedaction`,
`FeedbackSettings`, `FeedbackSwitchService`, `OverseerFeedbackQuery`, `GnollBenchFeedbackQuery`,
`FeedbackMarkdownExport` with `FeedbackExportService`, `FeedbackResolutionService`),
`AdminFeedbackController`, `AdminGnollBenchFeedbackController`, `AdminFeedbackTransferController`, and
the client tab `admin/feedback-tab/`. The model's description of the tool is
`Overseer/ToolGuides/report_feedback.md`.

## 1. Purpose and categories

A report is a note from the assistant to the developers. It never changes the answer: the guide tells
the model to correct a mistake in its answer first, and the result text tells it to continue with the
user's request.

| Category | When the model files it |
|---|---|
| `missing_information` | It searched for a fact the GnollHack wiki, the knowledge base or the source should contain, made at least one reasonable search, and did not find it |
| `tool_problem` | A tool returned an error, malformed output, or a result that contradicts its own description; that tool is named first in `related_tools` |
| `own_mistake` | Something it told the user earlier in the conversation was wrong, noticed through a user correction, a tool result or its own review (`noticed_via`) |

The categories are a fixed enum so they can be counted. The parameters, caps and result texts are in the
`server_tool_parameter_reference` skill, § 7a. In short: `category`, `subject` and `summary` are required;
`details`, `searched_for`, `expected_location`, `related_tools`, `noticed_via`, `suspected_cause` and
`suspected_cause_note` are optional. Over-long strings are cut without an error and the row records
`Truncated`; `related_tools` keeps registered tool names only.

## 2. Issues and occurrences

Every report is an **occurrence** (`OverseerFeedbackReport`) of an **issue** (`OverseerFeedbackIssue`).
The issue is the problem; the occurrences are the times it was reported.

**The fingerprint** groups them. `FeedbackFingerprint.Compute`:

1. normalizes the subject (`NormalizeSubject`): Unicode NFKC, lowercase invariant, every character that
   is not a letter, a digit or a space becomes a space, whitespace collapsed, trimmed;
2. builds the key `category + "\n" + normalizedSubject`, and for `tool_problem` also
   `+ "\n" + firstRelatedTool` (lowercase; empty when none was given);
3. takes the SHA-256 of the key's UTF-8 bytes as 64 lowercase hex characters.

**The subject is the grouping key; the summary is not.** The same problem is worded differently every
time, so the guide asks the model to name the subject in two to six words, the way its wiki page or
source file names it, and to use the same subject for the same problem. The grouping is therefore exact
after normalization: it groups what the model names the same way, and nothing else. Merging two issues
the model named differently, or splitting one it conflated, is a later phase (§ 15).

**Recording an occurrence.** When no issue has the fingerprint, the handler creates one (`New`, count 1)
together with the occurrence. When one exists, it adds the occurrence and then advances the issue with
set-based updates, so two concurrent reports never lose a count: `OccurrenceCount + 1` and
`LastSeenUtc = now`, and, only when the issue is `Resolved`, `Status = Regressed`,
`RegressionCount + 1`, `LastRegressionUtc = now`. A race on the issue's unique fingerprint reloads the
issue and continues; a race on the occurrence's unique `(ChatSessionId, Fingerprint)` index means a
parallel call in the same batch won, and the call gets the already-recorded result.

**One occurrence per issue per conversation.** A second report with the same fingerprint in the same
conversation is not stored and is answered *This feedback was already recorded in this conversation.*

**The five statuses:**

| Status | Set by | Meaning | On a new occurrence |
|---|---|---|---|
| `New` | the system | Not yet looked at | stays `New`; the count advances |
| `Acknowledged` | an administrator | Seen; being investigated or waiting on a fix | stays; the count advances |
| `Resolved` | an administrator | Fixed | **becomes `Regressed`**: `RegressionCount + 1`, `LastRegressionUtc = now` |
| `Regressed` | the system | Was resolved and came back | stays `Regressed`; the count advances |
| `Dismissed` | an administrator | Will not fix, or noise | stays `Dismissed`; the count advances |

This is the resolve-reopens, dismiss-stays rule of an error tracker, with the reopened state named so it
can be filtered and counted. An administrator leaves `Regressed` at the next triage, by choosing
*Resolved*, *Acknowledged* or *Dismissed*; the status endpoint refuses `regressed` as an input, because it
is a fact about recurrence, not a decision. The count advancing under `Dismissed` is deliberate: an issue
dismissed at three occurrences can be seen reaching three hundred.

**What a purge and an erasure leave behind.** Occurrences go with their conversation's permanent purge
and with their user's account erasure (§ 9). **Issues stay**, holding nothing that points at a
conversation or a user. Their `OccurrenceCount` is historical and is never decremented; the admin view
shows the retained count beside it (*12 occurrences, 9 retained*). Deleting an issue in the admin view
removes the issue and, by cascade, its occurrences.

## 3. The feedback switch and the GnollBench setting

An administrator turns the tool on and off for the production chat at runtime, from the switch in the
Feedback tab's header, without a restart or a configuration edit. The header holds one switch, **Feedback
tool**, for the Overseer chat, and an (i) that says what the tool does, that it applies to the Overseer chat
only and that GnollBench runs choose it per run: *GnollBench → Run Benchmark → Feedback Tool* (§ 10).

- **Storage.** `OverseerFeedbackSetting` is append-only, like `BenchmarkBaselineModelSetting`: rows are
  never updated or deleted, and each click appends one (`Target`, `Enabled`, `SetAtUtc`, `SetByUserId`),
  even when it sets the value the switch already has. `Target` is an `OverseerFeedbackSwitchTarget`
  (`Chat = 0`, `GnollBench = 1`; the column defaults to 0, so a row without one is a chat row), and the
  switch's state is the newest chat row, read through the `(Target, Id)` index. **The table is the
  switch's history**, and the (i) shows its newest entry (*Changed 21 Oct 2026 14:02 by {user name}.*);
  no admin-log entry is written.
- **The retired GnollBench target.** Until 2026-10-22 the card also held an *Enabled in GnollBench* switch,
  stored as rows of `Target = GnollBench`. Those rows are kept as history and are never read again; the
  enum value stays so they still load. GnollBench now takes the value from each run's launcher setting
  (below).
- **The seed.** Until an administrator has set the switch once, its state is `Feedback:Enabled` from
  `Overseer/appsettings.json` (the (i) reads *Configuration default.* in that state). Once a chat row
  exists, the seed is not consulted again.
- **The cache.** `FeedbackSwitchService.IsEnabled()` caches the state for 30 seconds; `SetAsync` writes the
  cache through, so a change is immediate in the Overseer process. A read failure returns `false` and
  caches nothing.
- **The switch governs the production chat.** `ChatService` reads it when it builds the turn's request, so
  the next turn after a change declares or withholds the tool, and the handler reads it again before
  storing, so a turn that was already running when the tool was turned off records nothing.
- **The user's setting.** `UserAiSettings.EnableFeedbackTool` (default on; the migration sets existing rows
  on), under Settings → AI Permissions → *Feedback to developers*, can only withhold the tool: the chat
  offers it when the admin switch and the user's setting are both on (`ChatFeedbackToolGate`). The page
  leads with whether the tool is available in the user's chats and why not, shows the admin switch's state
  (`feedbackToolAdminEnabled` in `GET /api/settings`) as a secondary line under it, and its (i) dialog
  explains the tool and names the admin switch's state again. GnollBench does not read it.
- **No confirmation.** Turning the tool off is reversible in one click and destroys nothing, so the switch
  commits at once, as the System Configs access switches do, and announces the result in a status line.

**GnollBench.** A run's value comes from the launcher's **Feedback Tool** field (GnollBench → Run Benchmark,
*Experiment settings*, after Experimental Changes): *Offered — default* or *Withheld — the AI cannot report
problems*. The launch request carries it as `withholdFeedbackTool` (null and false both offer the tool), and
the run records it in its candidate prompt options (`CandidatePromptOptionsJson`) as `feedbackToolWithheld`.

- **Recorded once, at launch.** The field is written only when the tool was withheld, and under every
  information condition (production, source only, closed book). Both candidate paths read the run's
  recorded value, so a re-run of failed questions or of a single answer offers or withholds the tool as the
  run did. A run recorded before the field existed has none and is offered the tool.
- **Withholding it is a recorded instrument change, never a silent one.** An offered run records exactly
  what a harness-60 run did, byte for byte, and stays fully comparable with one. A withheld run differs on
  the *CandidatePromptOptions* comparability key, so it is not comparable with an offered one: Multi-Run
  grouping reports the mixed set below Tier B, the battery comparability guard refuses a mixed composite,
  and the cross-model comparability signature refuses a mixed comparison. Withheld runs are comparable with
  each other, and two runs launched with the same setting still pair across information conditions. Chat
  Consistency shows a change as a *Prompt options* Overseer event.
- **Orchestrated members inherit it.** A battery, series or model batch stores its start request as
  `StartRequestJson` and launches every member from it, so all members carry the same setting. A request
  stored before the field existed offers the tool.
- **Repeat this run's setup** sets the field from the run's recorded `feedbackToolWithheld`; a run
  recommended by Root Cause Analysis or the control-run suggestion resets it to *Offered*, as it resets
  Tool Access and Experimental Changes. The launcher remembers the choice with its other settings.

## 4. The suspected cause is a hypothesis

A model's account of why it erred is a story told afterwards, not a reading of its own process. The
schema therefore keeps the checkable facts (`searched_for`, `related_tools`, `noticed_via`) apart from the
optional `suspected_cause`, whose enum leads with the causes that can be checked against the turn:
`did_not_verify` and `misread_tool_result`, then `training_knowledge_conflict`, `search_strategy`,
`corpus_or_tool_limit`, `ambiguous_question`, `answered_too_quickly` and `other` (an unknown value becomes
`other`). The field is never required, because a required cause would force a made-up one. The admin view
and the GnollBench run report label it *Model's suspected cause (hypothesis)*.

## 5. Scrubbing and the version tag

The guide tells the model to leave out personal information, credentials and long quotes; the server
enforces it before storage. `FeedbackRedaction.Apply` runs `DlpScannerService.Scan` over `subject`,
`summary`, `details`, `searched_for`, `expected_location` and `suspected_cause_note` with
`DlpPolicy.Defaults` (credentials, card numbers, IBANs, SSNs) plus e-mail addresses and phone numbers,
replaces each finding with `[redacted]`, and sets `Redacted` on the row. The scrub runs **before** the
fingerprint is computed, so a subject that was itself a secret groups by its redacted form.

Contact details are off by default for outbound masking because masking them breaks a pasted address book;
that reason does not apply to a record only developers read. The copy of the same text in the turn's
`ChatMessageToolCall` row is the model's original arguments and is not scrubbed (§ 9).

Each occurrence also stores **`AppVersion`**, the entry assembly's `AssemblyInformationalVersion` (as the
GnollBench run manifest reads it), cut to 64 characters, null when absent. It is what lets an
administrator see that an issue stopped recurring after a release; the card shows the latest one.

GnollBench self-reports are **not** scrubbed (§ 11).

## 6. Where the tool is offered

`ToolRegistry.BuildToolsForRequest` declares a `ToolCategory.Feedback` handler only when the request's
`EnableFeedbackTool` and `enableToolUse` are both true. That check **replaces the allow-list check** for
this category; it comes after the experimental-tool check.

| Caller | `EnableFeedbackTool` |
|---|---|
| `ChatService` (production chat) | the chat switch and the user's setting are on, and the session is neither ephemeral nor confidential |
| `DelegateToSubAgentTool` | not set: sub-agents are never offered it |
| GnollBench candidate paths (`ExecuteSingleQuestionAsync`, `ReExecuteSingleAnswerAsync`) | `!FeedbackToolWithheld` of the run's recorded candidate prompt options (§ 3) |
| Graders, claim verifier, rubric-gap author, report writers | not set |

**Why the allow-list does not apply.** `Benchmark:AllowedTools` also feeds the grader's *Available tools*
line, the claim verifier's tool set and lead filter, and the rubric-gap author; listing the tool there
would hand it to all three. Exempting the category keeps that key, the per-condition candidate tool lists
and the sub-agent catalog unchanged. `SubAgentCatalogService.Validate` would accept the name, so the
registry gate is what keeps it away from sub-agents.

**Second guard.** `AgentLoopRunner.IsDeclared` refuses any call whose name was not declared for the
request, so a grader, the verifier or a sub-agent cannot execute `report_feedback` even if a model names
it. The handler itself refuses at `AgentDepth > 0`, in an ephemeral or a confidential session, with a
default session reference (the Developer Tools handler probe), when the chat switch is off, and when the
user's setting is off; each of those
returns *Feedback reports are not available in this conversation.*

**The closed-book case.** A closed-book GnollBench candidate runs with `EnableToolUse = false` and an empty
tool list, so the `enableToolUse` term withholds the feedback tool too, whatever the run's Feedback Tool
setting recorded. That is intended: a closed-book run
has no tool loop, and giving it one tool would make it a different condition. The run report says so
(§ 11).

**Order of the handler's checks.** Sub-agent; argument parse; the GnollBench candidate branch (the per-turn
cap, then an acknowledgment without a write); the session and the chat switch; scrub and fingerprint; the
user's setting; the daily cap; the same-conversation duplicate; the per-turn cap; store and acknowledge. A refused, invalid or
duplicate call does not use one of the turn's three reports. **Storage never fails the turn**: any
exception in the database steps is logged at Warning with the category, never the text, and the model gets
the not-available result, which it treats like any other tool failure.

## 7. The budget exemption and the log rule

- **Outside the call budget.** `IToolHandler.CountsTowardCallBudget` defaults to `true`;
  `ReportFeedbackTool` returns `false`. `ToolExecutor` looks the handler up before its session-quota block
  and skips the block for an exempt handler, so a report counts against neither the chat's 150-call session
  budget nor GnollBench's 45-call question budget. `RemainingBudget` stays null on that path, so the result
  carries no `[Tool budget: …]` notice; a `0` there would tell the model its budget is gone. Counting the
  tool would make it compete with real research and move *Budget Pressure* in every run.
- **Its own caps.** Three recorded reports per agent run (`ReportFeedbackTool.MaxReportsPerTurn`, a
  constant so the guide's "three per turn" cannot drift), counted by `AgentRunBudget.TryIncrementFeedbackReport`;
  `Feedback:MaxReportsPerUserPerDay` per user per UTC day in the chat; one occurrence per issue per
  conversation.
- **The log rule.** `ToolExecutor`'s *Tool Execution Audit* line normally dumps the call's raw parameters;
  it is skipped for `ToolCategory.Feedback`. The handler logs only the category, the issue id and the new
  row id. Report text reaches the application log nowhere. See
  [`tool-batching-and-execution.md`](tool-batching-and-execution.md).

## 8. Configuration

```json
"Feedback": {
  "Enabled": true,
  "MaxReportsPerUserPerDay": 20
}
```

`FeedbackSettings.Read` parses each value from its trimmed string, never with `GetValue<T>`. An unparseable
`Enabled` resolves to `false`; an unparseable or out-of-range daily cap resolves to 20, clamped to 1–200.
`Enabled` is only the chat switch's seed (§ 3); the run's Feedback Tool setting has no configuration key.
GnollBench ignores both values.

## 9. Storage and lifetimes

Five tables. The first four are in migration `AddOverseerFeedback`; `SplitOverseerFeedbackSwitch` adds the
settings table's `Target` column and its `(Target, Id)` index, and `AddFeedbackSourcesAndResolutions` adds
the columns below and `BenchmarkFeedbackTriages`.

| Table | One row per | Linked to | Removed by |
|---|---|---|---|
| `OverseerFeedbackIssues` | distinct problem (fingerprint, unique) | nothing outside the feature | an administrator's delete, which cascades to its occurrences |
| `OverseerFeedbackReports` | chat occurrence | its issue (`IssueId`, cascade); the user (`AspNetUserId`, cascade); `ChatSessionId`, `TurnUserMessageId` and `SystemAiConfigurationId` as plain columns | the conversation's permanent purge; the account's erasure; the issue's delete |
| `OverseerFeedbackSettings` | change of the chat switch (`Target` chat; GnollBench rows are retired history) | `SetByUserId` as a plain column | never: kept like any admin setting |
| `BenchmarkRunAnswerSelfReports` | GnollBench candidate call | its answer (cascade) | the answer, through run → answer → self-report |
| `BenchmarkFeedbackTriages` | triaged GnollBench issue (`Fingerprint`, unique) | nothing: joined to self-reports by the indexed `Fingerprint`; `TriagedByUserId` as a plain column | never: kept like the chat issues' triage |

**The columns `AddFeedbackSourcesAndResolutions` adds.**

- `OverseerFeedbackReports.ModelSource` (`nvarchar(16)`, null): `system` when the turn ran on a system AI
  configuration, `user` when it ran on the user's own model, null when unknown. `SystemAiConfigurationId`
  (`bigint`, null) names the configuration. It is a plain column with no foreign key, like
  `ChatMessage.SystemAiConfigurationIdUsed`, because the attribution must survive the configuration's
  deletion; the admin view names a deleted one *Configuration #id (deleted)*. `ReportFeedbackTool`
  records both from the `ToolExecutionContext` (`ActiveSystemModelId`, else `ActiveUserModelId`), the only
  source at write time, because the assistant message and its tool-call rows are saved after the turn.
- `OverseerFeedbackIssues.Resolution` (`nvarchar(4000)`, null), what was done, and `FixedInVersion`
  (`nvarchar(64)`, null), the Overseer version that has the fix.
- `BenchmarkFeedbackTriages`: `Fingerprint` (64, unique), `Status` stored as a string (`New`,
  `Acknowledged`, `Resolved`, `Dismissed`; never `Regressed`, which is derived, § 11), `TriageNote` (1000),
  `Resolution` (4000), `FixedInVersion` (64), `TriagedByUserId`, `TriagedUtc`, and `ResolvedUtc`, set when
  the status becomes Resolved and cleared by any other status.

**The backfill.** The migration's `Up` fills `ModelSource` for the reports that existed before it, in SQL
Server SQL: for each report it takes the `ChatMessage` that owns the `ChatMessageToolCall` row with the
report's `ToolCallId`, matched on `ChatMessage.ChatSessionId` = the report's `ChatSessionId` (the tool-call
row carries no session id), newest message first. A non-null `SystemAiConfigurationIdUsed` gives `system`
and the configuration id; a null one gives `user`. A report whose tool-call row cannot be found stays
null, *Unknown*, and is never guessed. The tests build their schema with `EnsureCreated` and never run a
migration, so no test covers the backfill; it is verified by hand against the database.

**Why the links are what they are.**

- `ChatSessionId` is a plain column, like `ChatAccessAuditLog.ChatSessionId`, because a cascading key to
  the session beside the user cascade would give SQL Server two cascade paths from `AspNetUsers`. It is
  `long`, not nullable: every stored occurrence has a persistent session, which is also why the unique
  `(ChatSessionId, Fingerprint)` index needs no filter.
- `TriagedByUserId` on the issue and `SetByUserId` on the setting are plain columns, like
  `ChatAccessAuditLog.ActorUserId`, so deleting an administrator never fails on them and erasure need not
  know these tables.
- The issue's `Status` is stored as a string (`New`, `Acknowledged`, `Resolved`, `Regressed`, `Dismissed`).

**Lifetimes of a chat report.**

- **Ephemeral and confidential sessions write nothing**: the tool is not offered, and the handler refuses.
  The ephemeral promise of no rows in the four chat tables is unchanged. Encrypting report text under the
  session key was rejected: administrators could not read it, and crypto-shredding would destroy it.
- **The text exists twice in an ordinary chat**: the occurrence row (scrubbed) and the call's arguments and
  result in the turn's `ChatMessageToolCall` row (not scrubbed), which lives, is pruned and dies with the
  conversation like every other tool call.
- **Soft delete (trash)** keeps the rows, as it keeps the session's messages.
- **The permanent purge** (`ChatRetentionService.PermanentlyPurgeSessionsAsync`) deletes the sessions'
  occurrences explicitly before the session rows, counts them first into
  `MaintenanceResultDto.PurgedFeedbackReportCount` (dry runs included), and names the count in its log line.
  Every deletion path reaches it. **Issues stay.**
- **Account erasure** removes the user's occurrences through the user cascade. Issues stay, with nothing
  that points at the user.
- **No age prune.** Every occurrence is bounded by its conversation's or its account's lifetime, and every
  issue by an administrator's delete. An occurrence age prune is the named next step (§ 15).

The privacy side, including the notice's data-map row, is in
[`data-privacy-framework.md`](data-privacy-framework.md) § 7 and § 8.6. Report fields are model output a
user can steer: they are rendered as plain text only, and no model in Overseer reads them back. An
administrator's export hands them to an AI coding assistant, fenced as untrusted data and without any
user identity (§ 12).

## 10. The admin view

**Admin → Feedback**, `/admin/feedback` (`admin/feedback-tab/`, `app-admin-feedback-tab`), the last Admin
tab, after *Services*. Until 2026-10-22 it was the AI Telemetry tab's fifth sub-tab, and
`/admin/telemetry/feedback` still opens it, keeping the query ([`admin-page-urls.md`](admin-page-urls.md)
§ 3). It has two sub-tabs, a `.gh-tabs` row named *Feedback sources* under the header:

- **Overseer** (`/admin/feedback`, the default; `app-feedback-overseer-panel`, `overseer/`): the issues the
  chat assistant reported, as described below.
- **GnollBench** (`/admin/feedback/gnollbench`; `app-feedback-gnollbench-panel`, `gnollbench/`): the
  candidates' self-reports grouped into issues by fingerprint (§ 11).

The shell holds the header and the sub-tab row and mounts only the active panel. The page store remembers
the chosen sub-tab (`AdminPageStore.feedbackSubTab`). The query parameters of both sub-tabs are in
`admin-page-urls.md` § 2.

- **The tab's pill.** The *Feedback* tab in the Admin tab row shows the open issues, New plus Regressed, as
  a small gold pill (*Feedback 4*); its accessible name is *Feedback: 3 new, 1 regressed*. The pill is
  absent at zero and when the counts fail. The counts come from `GET api/admin/feedback/counts`, over **all
  dates**: the tab's date range does not limit them. `AdminPageStore.loadFeedbackCounts()` fetches them when
  the Admin page loads and again after a status change, a delete, an import or a Refresh in the tab; the
  tab row and the Overseer *Status* facet both read `store.feedbackCounts`. The pill counts Overseer
  issues only; the GnollBench sub-tab shows its own counts in its *Status* facet.
- **The header** is sticky, like the other Admin tabs', and holds the sub-tab row: the title *Feedback*,
  the **feedback tool switch**, the **date range of the active sub-tab** and a **Refresh** icon button
  (*Refresh feedback*, tooltip *Refresh*). Each sub-tab has its own range, remembered with its view in
  `localStorage` (below): *All time* (the default),
  *Last 7 days*, *Last 30 days*, *Last 90 days*, *Last year* or *Custom date range* (two date inputs, up to
  today; an end before the start is moved to the start). Its click (i) reads *Shows the issues reported at
  least once in this range. Each sub-tab keeps its own range. The counts on the tab are over all dates.* An
  issue is listed when its **active span** overlaps the range (`FirstSeenUtc <= end` and
  `LastSeenUtc >= start`), so an issue first seen in
  August and last seen today is listed for an August range, and the answer does not depend on occurrence
  rows a purge may have removed; *All time* sends no dates. Issues have no key source, so there is no
  *Keys* filter. Refresh reloads the switch, the active panel's facets, its first page of issues and the
  counts, and is `aria-disabled` while the first page loads.
- **The switch** comes first among the header's controls: the label **Feedback tool**, the state word *On*
  (gold) or *Off* (gray, `aria-hidden`), one large switch (`gh-switch gh-switch-lg`) and a click (i),
  *About the feedback tool*. The switch is a checkbox with `role="switch"`, so assistive technology
  announces a switch, on or off; its accessible name is *Feedback tool in the Overseer chat* (the visible
  label with a visually hidden *in the Overseer chat*), and it is described by the (i). The (i) reads *Lets
  the chat assistant report missing information, tool problems and its own mistakes to administrators.*,
  then *Applies to the Overseer chat only. GnollBench runs choose it per run: GnollBench → Run Benchmark →
  Feedback Tool.*, then the switch's newest entry: *Changed {time} by {name}.*, *Changed {time}.* without a
  name, or *Configuration default.* while the switch is seeded; nothing while loading or after a load error.
  Until the state arrives the switch is disabled and the state word absent.

  The switch commits at once, is disabled while its request runs, reverts on an error and refetches. A
  visually hidden `role="status"` line announces *Overseer chat: the feedback tool is on.* (or *off*);
  the state word shows the same on screen. A load or toggle error is a red line across the header under
  its controls. A 1 px hairline separates the switch from the range and Refresh; it is hidden when the
  header is too narrow to hold them on one line, and while the custom range's date inputs are shown.
- **Reported issues**: the section title, with the list's count (*Showing 25 of 40 issues*, a polite
  live region) at the end of its row.
- **The filter bar**: a search over the issue's subject and summary and its occurrences' details,
  *searched for* and expected location (at most 200 characters); *Sort by* *Last seen* (the default),
  *First seen*, *Most occurrences*, *Most users*, *Most regressions*, *Recently triaged* or *Subject (A–Z)*;
  the facets *Status* (defaulting to **New and Regressed**, the open work), *Category*, *Model*,
  *Provider*, *User*, *Group* (with *No group*, which also matches occurrences with no user), *Model
  source* (*System configuration*, *User's own model*, *Unknown*), *Configuration*, *Version*, *Tool* (a
  related tool) and *Has resolution* (*Any*, with or without); and removable chips, with *Issue: #42* for
  an `ids` address. Facet options come from `GET facets` for the range, each with its count of issues.
  **Occurrence-level facets** (model, provider, user, group, model source, configuration, version, tool)
  match an issue when **one** retained occurrence matches all of them together. Every change refetches.
- **What the sub-tab remembers.** Its filters, search, sort, date range and export options are one record
  in `localStorage`, `overseer.admin.feedback.overseer.view` (`{ version: 1, sort, filters, search, range,
  exportOptions }`; `…gnollbench.view` for the other sub-tab), read and written behind `try/catch`
  (`shared/feedback-view-storage.ts`). **The address wins**: when it carries any of the sub-tab's filter
  parameters (a shared link), those apply and are remembered; otherwise the remembered view applies and is
  written to the address, replacing the history entry. A sub-tab switch removes every Feedback filter
  parameter, so the shared names never leak from one sub-tab into the other. The sort key used before the
  sub-tabs, `overseer.admin.telemetry.feedback.view`, is read once as the Overseer sort when the new record
  is absent, and is left in place.
- **The selection.** Each card has a checkbox, top left, whose label is the card's title. The selection is
  held by id (§ 8f of the `frontend_ui_controls` skill), at most 500, and is remembered across paging,
  filtering and reloads in `overseer.admin.feedback.overseer.selection` (`{ version: 1, ids }`; the
  GnollBench sub-tab keeps `fingerprints` in `…gnollbench.selection`). The selection bar
  (`app-feedback-selection-bar`, `shared/`) under the filters shows the count, *Show selected only* (which
  sends the ids to the server and prunes those it no longer returns), *Clear selection*, *Select all N
  matching* (`GET issues/ids`; it keeps at most 500 and says *Selected the first 500 of N* when a cap cuts
  the list), **Export** and **Import resolutions** (§ 12). An export drops the ids the server reports
  missing.
- **The empty states** are one dashed card each, with a glyph that assistive technology skips: *No
  feedback issues* — *The assistant has not reported an issue in this date range.* when the range holds
  none, *No issues match these filters* with a *Clear filters* button when the filters leave none, and *No
  selected issue* under *Show selected only*.
- **The cards** are a server-paged list, in batches of 25. Each issue card shows its category and status
  badges (*Regressed 2×* when it regressed more than once) and the models that reported it; the subject as
  its title and the summary under it, and, when an accepted GnollBench self-report has the same
  fingerprint, how often and its status with an *Open in GnollBench* link (it opens the GnollBench sub-tab
  searched by the subject, because the Overseer issue DTO carries no fingerprint); four aligned metrics —
  *Occurrences* (with *N retained* when fewer remain), *Users*, *First seen*, *Last seen*; the latest
  expected location, related tools, *Model's suspected cause (hypothesis)* and version, the providers, the
  model sources, and the fixed-in version and resolution when set; an *Occurrences* disclosure that loads
  the occurrences newest first, ten at a time, filtered by the occurrence-level facets with a *Show all
  occurrences* toggle; and the triage form — a *Status* select without *Regressed*, whose (i) beside the
  label, which also describes the select, reads *A resolved issue that recurs becomes Regressed; a
  dismissed one stays dismissed*, a *Note*, a *Fixed in version* (with an (i)), a *Resolution* (*What was
  changed, where, and how it was verified*), *Save*, and
  *Delete issue*, whose confirmation says the occurrences go with it.
- **An occurrence** shows its time, user, groups, provider and model, model source (with the system
  configuration's name), version, the session and message ids as text, the summary,
  what was searched for, the expected location, how the mistake was noticed, the cause note, the details,
  and the *Truncated* and *Redacted* marks. The view does not show the conversation: reading a user's
  conversation from the admin side is chat access, which belongs behind the access journal (§ 15).

**The API** (`AdminFeedbackController`, `api/admin/feedback`, `AdminOnly`). The filter building is
`OverseerFeedbackQuery` (scoped), shared by `issues`, `issues/ids` and the export. The filters, all
optional, every list comma-separated: `startDate`, `endDate`, `status`, `category`, `modelId`,
`provider`, `user` (Identity ids), `group` (group ids, plus `none`), `source` (`system`, `user`,
`unknown`), `config` (configuration ids), `version`, `tool` (matched against the occurrence's related
tools), `resolution` (`with` or `without`), `ids` (at most 500) and `search` (at most 200 characters).
Unknown values are 400.

| Method and path | Purpose |
|---|---|
| `GET switch` · `POST switch/{target}` (`{ enabled }`) | The switch's state, `{ chat }`, as `{ enabled, setAtUtc, setByUserName, seeded }`; appending a change to it, `target` `chat` (case-insensitive; anything else, `gnollbench` included, 400 *Unknown switch.*), which returns it |
| `GET issues?<filters>&sort&skip&take` | A page of issues (`sort` `lastSeen`, `firstSeen`, `count`, `users`, `regressions`, `triaged` or `subject`; `take` 1–100). Each issue also carries `providers`, `modelSources` (`{ system, user, unknown }` over retained occurrences), `resolution`, `fixedInVersion` and `alsoInGnollBench` (`{ selfReportCount, status }` or null) |
| `GET issues/ids?<filters>&sort` | Every matching id, at most 1,000, with `totalCount` and `truncated` |
| `GET facets?startDate&endDate` | The options present in the range, each with its count of issues: categories, models, providers, sources, versions and tools as `{ value, count }`; users, groups (with `none`, *No group*) and configurations as `{ id, name, count }`, a deleted configuration named *Configuration #id (deleted)*. It replaced `GET models` |
| `GET issues/{id}/occurrences?<filters>&all&skip&take` | A page of one issue's occurrences, newest first, filtered by the occurrence-level filters unless `all=true`; each also carries `modelSource`, `systemConfigurationId`, `systemConfigurationName` and `groupNames` |
| `GET counts` | Issue counts per status over all dates |
| `POST issues/{id}/status` (`{ status, note?, resolution?, fixedInVersion? }`) | Triage (404 unknown id; 400 unknown status or `regressed`). For the note, the resolution and the fixed-in version an absent or null value keeps the stored one and an empty one clears it, so a status change without a note keeps the note. A note over 1,000 characters, a resolution over 4,000 or a fixed-in version that does not match `^[0-9A-Za-z.+\-]{1,64}$` is 400 |
| `DELETE issues/{id}` | The issue and its occurrences |

**The cross-link limit.** `alsoInGnollBench` and the GnollBench sub-tab's `alsoInOverseer` join the two
stores by fingerprint. The chat fingerprints the **redacted** arguments (§ 5) and the benchmark the
**raw** ones (§ 11), so a subject the DLP scanner changed hashes differently in the two stores and is not
cross-linked.

## 11. GnollBench

From **harness 60** every GnollBench candidate with a tool loop is offered `report_feedback`, unless the run
recorded `feedbackToolWithheld` because it was launched with the Feedback Tool setting *Withheld* (§ 3); the closed-book
candidate is not (§ 6). The chat switch and `Feedback:Enabled` do not apply, and there is no daily cap. The
handler acknowledges a candidate's call without writing anything, after the parse and the per-turn cap,
because the benchmark stores the call itself:

- **A separate table.** `BenchmarkToolCallRecorder.Build` skips self-report calls, and
  `BenchmarkSelfReports.Extract` writes them as `BenchmarkRunAnswerSelfReport` rows: parsed and
  fingerprinted like a chat report, with `Accepted` and the refusal or validation `Error`, and **not
  scrubbed** (no user is in the loop, the text derives from suite questions, and scrubbing would make a
  self-report differ from what the candidate wrote). No user id is stored. The rows live and die with
  their answer, and the tool-call age prune does not touch them. The `Fingerprint` index is what the
  GnollBench sub-tab groups by and what the join to `OverseerFeedbackIssue.Fingerprint` uses (below).
- **Every tool metric excludes them.** `ToolCallCount` and `ToolCallSummary` are filtered in both candidate
  paths, and the tool-call rows never hold a self-report, so *Budget Pressure*, tool routing, Chat
  Transfer, Chat Consistency, evidence reach and the graders' *Completed tool calls* are unchanged by
  construction. The tool is not in `CandidateAllowedTools`; the harness version, together with the absence
  of `feedbackToolWithheld` in the run's candidate prompt options, is the record that it was offered.
- **The Self-Reports section.** The run report's `### Self-Reports` follows *Tool Routing*: totals by
  category, the count not accepted and the number of distinct issues; the note that the suspected cause is
  the candidate's hypothesis; a table by question; and *Repeated issues*, one line per fingerprint that
  appears in more than one question. A run without any, a run before harness 60 and a closed-book run each
  say why the section is empty; a run with a tool loop that recorded `feedbackToolWithheld` says *The
  feedback tool was not offered (withheld by the run's Feedback Tool setting).* (a closed-book run keeps its
  own sentence, which takes precedence). Each question in § 3 of the report lists its self-reports under its call
  table. Report packs and the AI-written report documents do not show them.

**The GnollBench sub-tab** (Admin → Feedback → *GnollBench*, `/admin/feedback/gnollbench`) is the
cross-run view. Nothing is written on the benchmark path: issues are computed when the list is read, and
only an administrator's triage is stored.

- **Issues are computed by fingerprint.** `GnollBenchFeedbackQuery` (scoped) groups self-reports with a
  non-null `Fingerprint`, joined to their answer and run, into one issue per fingerprint, by default over
  the **accepted** calls only; the *Self-reports* select widens it to *Accepted and refused* or *Refused
  only* (`accepted=all|refused`). A self-report's time is `BenchmarkRunAnswer.StartedAtUtc ??
  BenchmarkRun.StartedAtUtc`; its model and provider are the run's `TestedModelSnapshot`; its question text
  is the answer's own `QuestionText` copy, because the question link is nullable. An issue shows the latest
  subject and summary. Grouping and the occurrence filters run in SQL; the triage join, the derived status,
  the date overlap, the status filter and the sorts run in memory over one row per fingerprint, which the
  small volume allows; the information condition, the variant and the model batch resolve to run-id lists
  first, the condition parsed from `CandidatePromptOptionsJson` because it has no column.
- **Triage by fingerprint.** `BenchmarkFeedbackTriages` (§ 9) holds the status, note, resolution and
  fixed-in version of a fingerprint. No row reads **New**. **Regressed is derived**: a triage row that is
  Resolved reads Regressed when the newest accepted self-report is newer than its `ResolvedUtc`. It
  therefore cannot be chosen, and becoming Resolved again sets `ResolvedUtc` to now.
- **A re-run** deletes and recreates an answer's self-reports, timed at the re-run, so nothing keys on a
  self-report's row id. The grouping and the triage row, both keyed by fingerprint, survive it, and a
  re-run that reports a Resolved issue again makes it read **Regressed**. That is intended: the model
  reported it again.
- **The filter bar**: a search over the subject, summary, details and question text; *Sort by* *Last
  seen* (the default), *First seen*, *Most self-reports*, *Most runs*, *Most models* or *Subject (A–Z)*; the
  *Self-reports* select; and the facets *Status* (default New and Regressed, with the sub-tab's own counts
  from `GET counts`), *Category*, *Model*, *Provider*, *Run*, *Series*, *Analysis group* (Multi-Run
  Analysis), *Battery run*, *Model batch* (a batch member that names the run, its series or a battery run
  it belongs to), *Suite*, *Question*, *Information condition*, *Corpus* (`live` for none), *Experimental
  Changes* (the variant name or ablation spec without its hash, or *None*) and *Harness* (sorted
  numerically). The facet options count self-reports whose own time is in the range. The view, the
  selection and the address follow the Overseer sub-tab's rules (§ 10).
- **The cards** show the category and derived status, the models, the subject and summary, four metrics
  (*Self-reports* with the run count, *Models*, *First seen*, *Last seen*), the providers, suites and the
  fingerprint's first 12 characters, and, when an `OverseerFeedbackIssue` has the fingerprint, *Also
  reported in the Overseer chat* with a link to that issue (`/admin/feedback?ids=<id>&status=all`) and its
  status. A *Self-reports* disclosure lists them newest first: run (a link that opens the run report
  download, since no route opens one run), suite, model and provider, information condition, Experimental
  Changes, corpus, harness, *Refused because* for a refused call, the question, and the report's text
  fields. The triage form is the Overseer sub-tab's, without *Delete*: self-reports are run records and
  leave with their run.
- **The API** (`AdminGnollBenchFeedbackController`, `api/admin/feedback/gnollbench`, `AdminOnly`). The
  filters, every list comma-separated: `startDate`, `endDate`, `status`, `accepted`, `category`, `model`,
  `provider`, `run`, `series`, `group`, `battery`, `batch`, `suite`, `question`, `condition`
  (`production`, `sourceOnly`, `closedBook`), `corpus`, `variant`, `harness`, `fingerprints` (at most 500)
  and `search` (at most 200 characters).

  | Method and path | Purpose |
  |---|---|
  | `GET issues?<filters>&sort&skip&take` | A page of issues: `fingerprint`, `category`, `subject`, `summary`, first and last seen, `selfReportCount`, `runCount`, `modelCount`, `models` and `suites` (at most five, then *+N*), `providers`, the derived `status`, the triage fields, `resolvedUtc` and `alsoInOverseer` (`{ issueId, status }` or null; the cross-link limit of § 10 applies) |
  | `GET issues/ids?<filters>` | Every matching fingerprint, at most 1,000, with `totalCount` and `truncated` |
  | `GET issues/{fingerprint}/occurrences?<filters>&all&skip&take` | A page of one issue's self-reports, newest first |
  | `GET facets?startDate&endDate&accepted` | The facet options with their counts |
  | `GET counts` | Issue counts per derived status over all dates |
  | `POST issues/{fingerprint}/status` (`{ status, note?, resolution?, fixedInVersion? }`) | Upserts the triage row with the Overseer sub-tab's keep and clear and length rules; `regressed` 400; a fingerprint with no self-report 404 |

The harness change is described in [`ai-benchmark.md`](ai-benchmark.md), *Harness Version 60 Updates*, and
the diagnostic consequence in the `server_benchmark_tool_diagnostics` skill.

## 12. Export and import

The selection bar of either sub-tab hands selected issues to an AI coding assistant as a Markdown file and
takes its answer back as a YAML resolution file. The assistant's method is the project skill
`server-feedback-resolution` (`.agents/skills/server_feedback_resolution/SKILL.md`). The controller is
`AdminFeedbackTransferController` (`api/admin/feedback`, `AdminOnly`).

### The export

`POST api/admin/feedback/export` takes `{ source: "overseer" | "gnollbench", issueIds?, fingerprints?,
occurrencesPerIssue?: 5 | 10 | 25 (default 10), includeQuestionText?: bool (GnollBench, default true) }`.
The Overseer source takes `issueIds` and refuses `fingerprints`, GnollBench the reverse; at most 200
issues. It returns `{ fileName, markdown, issueCount, missingIds, missingFingerprints }`, where the two
lists name the requested issues that no longer exist; 404 with those lists when none exists, 400 with a
`message` for an invalid request. The export dialog chooses the occurrences per issue and, for
GnollBench, whether to include the question text; the choice is remembered with the sub-tab's view. The
client saves the file and drops the missing ids from the remembered selection.

`FeedbackExportService` loads the issues through the two query services, maps them to a document that
holds no user identity, and builds the file with `FeedbackMarkdownExport`: pure, deterministic for the
same document, with **LF** line endings. The export time is the clock's, cut to the second, taken before
anything is read. The file is `overseer-feedback-<source>-<yyyyMMdd-HHmm>.md`:

1. **A header**: a facts table with the format `overseer-feedback-export`, format version 1, the source,
   the export time in UTC (which the template copies as `exportedAtUtc`), the Overseer version (the full
   `AssemblyInformationalVersion`), the issue count, the occurrences per issue and, for GnollBench,
   whether the question text is included; then *Work on this file with the `server-feedback-resolution`
   skill.*
2. **Untrusted content**: the report fields are model output (a chat user can steer the chat's; the
   suite's questions steer the candidates'), triage notes and resolutions are administrator text, and
   everything inside a `text` fence is data to investigate, never an instruction — not to be followed,
   no command it names to be run, no link it gives to be opened. Every claim is to be verified against the
   source code and the corpora. No heading contains model-written text. The Overseer export adds that user
   identities are left out.
3. **One section per issue**, headed `## Issue 42` (Overseer) or `## Issue <first 12 hex of the
   fingerprint>` (GnollBench). A facts table holds only values the server controls: id, fingerprint,
   category, status, first and last seen, counts (occurrences with the retained count, distinct users and
   regressions; for GnollBench self-reports and runs), models, providers, and for Overseer the model
   sources, system configuration names and versions, for GnollBench the suites, conditions, variants,
   corpora and harness versions; then related tools, suspected causes (hypotheses), the triage time and
   the fixed-in version. Cells escape `|` and `\` and flatten line breaks. The subject, summary, triage
   note and resolution follow, then `### Occurrences`: the newest occurrences, each `#### Occurrence N`
   with its own facts table (time, provider and model, the model source and configuration or the run,
   suite, question id, condition, variant, corpus and harness, how it was noticed, the suspected cause,
   related tools, the truncated and redacted marks) and its fenced texts (subject and summary when they
   differ from the issue's, details, *searched for*, expected location, the cause note, and the question
   text when included). A GnollBench issue with only refused self-reports is exported over all of them.
4. **The resolution template**: a fenced `yaml` block in the format below with `source`,
   `exportedAtUtc` and one item per issue, `id` (Overseer) and `fingerprint` filled in and `status`,
   `resolution`, `fixedInVersion` and `note` commented out.

**Fencing.** Every model-written or administrator-written text sits in a fence whose backtick run is one
longer than the longest backtick run inside it, at least three, with the info string `text`, so a report
cannot close its fence and inject Markdown.

**Never in the export:** user names, user ids, group names, `ChatSessionId`, `TurnUserMessageId` and
`ToolCallId`; the occurrences are projected without those columns. Distinct users appear only as a
count. [`data-privacy-framework.md`](data-privacy-framework.md) § 8.6 and the privacy notice say so.

### The resolution file

```yaml
format: overseer-feedback-resolutions
version: 1
source: overseer              # overseer | gnollbench; must match the sub-tab importing it
exportedAtUtc: "2026-10-23T12:34:56Z"   # copied from the export; optional, enables change detection
issues:
  - id: 42                    # Overseer only; required there, forbidden for gnollbench
    fingerprint: "<64 lowercase hex>"   # required; must belong to the id
    status: resolved          # new | acknowledged | resolved | dismissed; absent keeps
    resolution: |             # absent keeps; empty clears; at most 4000 characters
      What changed, where, and how it was verified.
    fixedInVersion: "1.4.1"   # absent keeps; empty clears; ^[0-9A-Za-z.+\-]{1,64}$
    note: |                   # the triage note; absent keeps; empty clears; at most 1000
      The triage note.
```

The rules. Errors block the import; notices do not. The client checks them all
(`shared/feedback-resolution-yaml.ts`, lazy `js-yaml`, path-bearing messages such as *issues[3] (id 42):
unknown key `state`*); the server checks R1–R9 again (`FeedbackResolutionService.TryValidate`), all but
R2's match with the sub-tab.

| Rule | Kind |
|---|---|
| R1 `format` is `overseer-feedback-resolutions` and `version` is 1 (the server accepts them absent, but exact when sent) | error |
| R2 `source` is `overseer` or `gnollbench` and matches the importing sub-tab (*This file is for the GnollBench sub-tab.*); the server cannot know the sub-tab, so the match is checked in the client only | error |
| R3 `issues` is a non-empty list of at most 500 | error |
| R4 No unknown key, at the top level or in an item | error |
| R5 Overseer: `id` is a positive integer; GnollBench: no `id` | error |
| R6 `fingerprint` is 64 lowercase hex characters | error |
| R7 No id or fingerprint appears twice | error |
| R8 `status` is one of the four values; `regressed` has its own message, *recurrence is recorded by the system* | error |
| R9 The lengths (note 1,000, resolution 4,000) and the `fixedInVersion` pattern | error |
| R10 An item that changes nothing | notice |
| R11 `resolved` with no resolution, stored or given (*a resolved issue should say what was done*) | notice |
| R12 No `exportedAtUtc` (*changes since the export cannot be detected*) | notice |

### Preview and apply

The *Import resolutions* dialog has the questions importer's three steps: the file, the review, and the
result.

- **Preview.** `POST api/admin/feedback/resolutions/preview` takes `{ format?, version?, source,
  exportedAtUtc?, items }` and writes nothing. It returns, per item, whether the issue exists and whether
  the fingerprint belongs to the id (either failing is an error), the category and subject, the
  **current** status, note, resolution, fixed-in version, `triagedUtc` and `lastSeenUtc`, the values
  after the item, which fields change, and, when the file has `exportedAtUtc`, the number of occurrences
  since the export, *triaged since export* (the stored `triagedUtc` is later) and *regressed since
  export*. An invalid file is 400 with `{ message, errors: [{ index, message }] }`.
- **Review.** One card per item with badges for *no changes* and each change (*status X → Y*, note,
  resolution, fixed in version), and a *Side by side* / *Diff* view of each changed text through the
  shared `app-text-diff`. *Triaged since export* needs acknowledgment: Apply stays unavailable until *I
  have reviewed the issues changed since the export* is checked. *N new occurrences since export* and
  *Regressed since export* are notices.
- **Apply.** `POST api/admin/feedback/resolutions/apply` takes the same body, each item with the
  `expectedStatus` and `expectedTriagedUtc` the preview showed (`expectedStatus` may be `regressed`; the
  time is null for an issue never triaged). In one transaction the server re-validates, compares every
  expected pair with the stored one, and writes each changed issue with a conditional update on the
  values it read. Any mismatch, an issue deleted in between, or a race that the conditional update or the
  unique fingerprint index catches rolls the whole file back and returns **409** with `{ message,
  conflicts: [{ index, id, fingerprint, reason: changed | missing, expectedStatus, currentStatus,
  expectedTriagedUtc, currentTriagedUtc }] }`; the dialog shows it and offers *Review again*. Only items
  that change something are written, each with the importing administrator as `TriagedByUserId` and
  `TriagedUtc` now; unchanged items keep their triage time. A GnollBench item upserts its triage row, and
  becoming Resolved sets `ResolvedUtc`. It returns `{ changed, unchanged }`; the client refreshes the list
  and the tab's pill.

## 13. The chat

The chat shows the call like any other tool call: its display name is *Feedback to developers*, and its
argument summary is the category in words followed by the subject (*Missing information: Dilithium
crystal*). The guide allows the model one sentence saying that it reported the issue.

## 14. Tests

`ReportFeedbackToolTests`, `ReportFeedbackArgumentsTests`, `FeedbackFingerprintTests`,
`FeedbackRedactionTests`, `FeedbackSettingsTests`, `FeedbackSwitchServiceTests`,
`ToolRegistryFeedbackGateTests`, `AdminFeedbackControllerTests` and `BenchmarkSelfReportsTests` in
`Overseer.Tests/UnitTests/`, with additions to `ToolExecutorRateLimitTests` (the exemption, the null
`RemainingBudget`, no parameter dump), `ChatRetentionServiceTests` and `AccountErasureTests` (occurrences
removed and counted, issues kept), `BenchmarkToolCallRecorderTests`, `BenchmarkReportBuilderTests` and
`BenchmarkServiceTests`. On the client: `admin-feedback.service.spec.ts`,
`admin-feedback-tab.component.spec.ts`, the Feedback tab and its pill in `admin.component.spec.ts`, the
paths and the retired sub-tab in `admin-url.spec.ts`, and additions to the chat's specs.

The switch and the GnollBench setting have their own cases:

- **The switch.** `FeedbackSwitchServiceTests`: a row without a target reads as the chat switch, and a
  retired GnollBench row does not affect it (`ARetiredGnollBenchRow_DoesNotAffectTheChatSwitch`).
  `AdminFeedbackControllerTests`: the chat switch is set and attributed, and an unknown switch, `gnollbench`
  included, is rejected.
- **The launch.** `BenchmarkRunLauncherTests`: `WithholdFeedbackTool` true resolves withheld, false and null
  offered, and a stored start request keeps the setting, or offers the tool when it predates the field.
- **The recording.** `BenchmarkCandidatePromptOptionsTests`: `FeedbackToolWithheld` false is omitted from
  the canonical JSON, true serializes and round-trips, and `Describe()` names a withheld tool.
  `BenchmarkServiceTests`: the setting is recorded under every condition, a candidate path with a withheld
  record does not offer the tool, and a re-execution keeps the run's recorded value. No test runs
  `RunAsync` to completion, so the path from the launch request to the record is covered at these two
  levels.
- **The consequences.** `BenchmarkPairedTestsTests`: the condition contrast holds when both runs record the
  withheld setting and not when only one does. `BenchmarkReportBuilderTests`: the Self-Reports section of a
  withheld run says the run's Feedback Tool setting withheld the tool.
  `BenchmarkChatConsistencyReportFactsTests`: the change reads *feedback tool withheld: not set → on*.
- **The client.** The header switch and its (i) in `admin-feedback-tab.component.spec.ts`, the route in
  `admin-feedback.service.spec.ts`, the Feedback Tool field in `benchmark.component.launcher.spec.ts`, its
  request, remembered value, repeated setup and recommended-run reset in `benchmark-launcher.state.spec.ts`,
  and the *feedback tool withheld* tag in `run-facts.spec.ts`.
- **The user's setting.** `ChatFeedbackToolGateTests`: all sixteen combinations of the admin switch, the
  user's setting and the two session kinds, offered only for an ordinary session with both on.
  `ReportFeedbackToolTests`: a user who turned the tool off gets the not-available result, and nothing is
  stored (`AUserWhoTurnedTheToolOff_GetsNotAvailableAndNothingIsStored`). `SettingsControllerFeedbackToolTests`:
  GET defaults the setting to on and reports the admin switch (null without the switch service), and PUT
  stores the setting and keeps it when the field is absent. On the client, `settings.component.spec.ts`: the
  checkbox and its nineteenth `saveSettings` argument, the result and sentence of every state, the
  administrator line outside the live region, the checkbox kept operable while the admin switch is off, and
  the (i) dialog naming the admin switch's state.

The sub-tabs, the export and the import have their own:

- **The Overseer sub-tab.** `AdminFeedbackControllerTests` (EF InMemory, as before): the new filters, the
  occurrence-level filters matching one occurrence together, the sorts, `GET facets`, `GET issues/ids`
  and its cap, the occurrence fields, and the status body's keep and clear rules (a status change without
  a note keeps it) and length refusals. `ReportFeedbackToolTests`: the model source and configuration are
  recorded from the context.
- **The GnollBench sub-tab.** `AdminGnollBenchFeedbackControllerTests`, on the SQLite schema template
  (`SqliteSchemaTemplate.OpenCopy()`), because InMemory evaluates every query in memory and would hide a
  translation failure SQL Server would throw: the grouping by fingerprint, the time fallback, the accepted
  scope, every filter, the search, the sorts, the facets, the derived status (Regressed after a newer
  accepted self-report, refused ones not counting), the triage upsert with its `ResolvedUtc` rules and
  refusals, and the cross-link.
- **The export.** `FeedbackMarkdownExportTests`: the header, the notice, the headings without model text,
  the facts tables, the template, LF line endings, no user identity, and the fence lengthening, pinned with
  a report that holds triple and quadruple backticks and a fake `## Issue` heading.
- **The import.** `FeedbackResolutionServiceTests`, on the SQLite schema template because InMemory throws
  on `BeginTransactionAsync`: rules R1–R9 as the server checks them, the preview's current values and
  since-export flags, apply writing only changed items, `ResolvedUtc`, and the 409 rollback, including a
  change between the check and the write and a triage row another writer created first.
- **The client.** `admin-feedback-tab.component.spec.ts` covers the shell (the header and the sub-tab
  row); `feedback-overseer-panel.component.spec.ts` and `feedback-gnollbench-panel.component.spec.ts` the
  panels; `feedback-view-storage.spec.ts` the remembered records; `admin-gnollbench-feedback.service.spec.ts`
  and `admin-feedback.service.spec.ts` the routes; the selection bar, the export and import dialogs,
  `feedback-resolution-yaml.spec.ts`, `feedback-transfer.service.spec.ts` and `text-diff.component.spec.ts`
  the transfer UI; and `admin-url.spec.ts` the sub-tab paths.

## 15. Deferred

Not in this version, and named so nobody assumes otherwise:

- **Merging and splitting issues**, for problems the model named differently or conflated.
- **A triage agent**: semantic grouping where exact grouping proves too fine, and reading the reporting
  turn's conversation, which is chat access and belongs behind `ChatAccessAuditLog`.
- **An age prune for occurrences.**
- **A notification** on a new or regressed issue; a `ConfigHealthService` alert was rejected, because
  issues are records to triage, not configuration problems.
- **A calibration metric** for self-reports. The GnollBench cross-run view and the join to chat issues by
  fingerprint exist (§ 11).
- **Self-reports in report packs** and in the AI-written report documents.
- **Feeding report text back to a model inside Overseer.** No model in Overseer reads it; only an
  administrator's export hands it to an AI coding assistant (§ 12).
- **Unifying the two fingerprints.** The chat hashes the redacted arguments and the benchmark the raw
  ones, so a subject the DLP scanner changed is not cross-linked (§ 10). Unifying them would move the
  benchmark's stored fingerprints, so it is a decision of its own.
- **Moving the questions importer to `app-text-diff`.** The import review and the Experimental Changes
  dialog use the shared component; the questions YAML importer still renders the same diff classes itself.
- **The series resume guard and guide-only variant edits.** The guard compares instrument hashes, which see
  a variant only through its appendix, so an edit to a variant's guide file alone does not stop a series
  from resuming; only a re-run's stamp check catches it
  ([`ai-benchmark-conditions-and-variants.md`](ai-benchmark-conditions-and-variants.md) § 6.10).
