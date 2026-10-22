# AI Feedback Reports

The Overseer chat assistant can send a short report to the development team when something in a
conversation went wrong in a way the developers can fix. The tool is `report_feedback`; administrators
read and triage the reports on **Admin → AI Telemetry → Feedback** (`/admin/telemetry/feedback`), where
they also turn the tool on and off, separately for the Overseer chat and for GnollBench. GnollBench offers
the same tool to its candidates and stores their calls in a table of its own (§ 11).

The pieces are `ReportFeedbackTool` (`Overseer/Services/Tools/`), the services in
`Overseer/Services/Feedback/` (`ReportFeedbackArguments`, `FeedbackFingerprint`, `FeedbackRedaction`,
`FeedbackSettings`, `FeedbackSwitchService`), `AdminFeedbackController`, and the client panel
`admin/telemetry-tab/feedback/`. The model's description of the tool is
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
the model named differently, or splitting one it conflated, is a later phase (§ 14).

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

An administrator turns the tool on and off for the production chat at runtime, from the card at the top of
the Feedback sub-tab, without a restart or a configuration edit. The card holds one switch, **Enabled in
Overseer chat**, and a pointer: *GnollBench runs choose this per run: GnollBench → Run Benchmark → Feedback
Tool.*

- **Storage.** `OverseerFeedbackSetting` is append-only, like `BenchmarkBaselineModelSetting`: rows are
  never updated or deleted, and each click appends one (`Target`, `Enabled`, `SetAtUtc`, `SetByUserId`),
  even when it sets the value the switch already has. `Target` is an `OverseerFeedbackSwitchTarget`
  (`Chat = 0`, `GnollBench = 1`; the column defaults to 0, so a row without one is a chat row), and the
  switch's state is the newest chat row, read through the `(Target, Id)` index. **The table is the
  switch's history**, and the card shows its newest entry (*Off since 21 Oct 2026 14:02 by {user name}.
  …*); no admin-log entry is written.
- **The retired GnollBench target.** Until 2026-10-22 the card also held an *Enabled in GnollBench* switch,
  stored as rows of `Target = GnollBench`. Those rows are kept as history and are never read again; the
  enum value stays so they still load. GnollBench now takes the value from each run's launcher setting
  (below).
- **The seed.** Until an administrator has set the switch once, its state is `Feedback:Enabled` from
  `Overseer/appsettings.json` (the row reads *Off by configuration default* in that state). Once a chat row
  exists, the seed is not consulted again.
- **The cache.** `FeedbackSwitchService.IsEnabled()` caches the state for 30 seconds; `SetAsync` writes the
  cache through, so a change is immediate in the Overseer process. A read failure returns `false` and
  caches nothing.
- **The switch governs the production chat.** `ChatService` reads it when it builds the turn's request, so
  the next turn after a change declares or withholds the tool, and the handler reads it again before
  storing, so a turn that was already running when the tool was turned off records nothing.
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
| `ChatService` (production chat) | the chat switch is on, and the session is neither ephemeral nor confidential |
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
default session reference (the Developer Tools handler probe), and when the chat switch is off; each of those
returns *Feedback reports are not available in this conversation.*

**The closed-book case.** A closed-book GnollBench candidate runs with `EnableToolUse = false` and an empty
tool list, so the `enableToolUse` term withholds the feedback tool too, whatever the run's Feedback Tool
setting recorded. That is intended: a closed-book run
has no tool loop, and giving it one tool would make it a different condition. The run report says so
(§ 11).

**Order of the handler's checks.** Sub-agent; argument parse; the GnollBench candidate branch (the per-turn
cap, then an acknowledgment without a write); the session and the chat switch; scrub and fingerprint; the
daily cap; the same-conversation duplicate; the per-turn cap; store and acknowledge. A refused, invalid or
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

Four tables, all in migration `AddOverseerFeedback`; `SplitOverseerFeedbackSwitch` adds the settings
table's `Target` column and its `(Target, Id)` index.

| Table | One row per | Linked to | Removed by |
|---|---|---|---|
| `OverseerFeedbackIssues` | distinct problem (fingerprint, unique) | nothing outside the feature | an administrator's delete, which cascades to its occurrences |
| `OverseerFeedbackReports` | chat occurrence | its issue (`IssueId`, cascade); the user (`AspNetUserId`, cascade); `ChatSessionId` and `TurnUserMessageId` as plain columns | the conversation's permanent purge; the account's erasure; the issue's delete |
| `OverseerFeedbackSettings` | change of the chat switch (`Target` chat; GnollBench rows are retired history) | `SetByUserId` as a plain column | never: kept like any admin setting |
| `BenchmarkRunAnswerSelfReports` | GnollBench candidate call | its answer (cascade) | the answer, through run → answer → self-report |

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
  issue by an administrator's delete. An occurrence age prune is the named next step (§ 14).

The privacy side, including the notice's data-map row, is in
[`data-privacy-framework.md`](data-privacy-framework.md) § 7 and § 8.6. Report fields are model output a
user can steer: they are rendered as plain text only, and no model reads them back.

## 10. The admin view

**Admin → AI Telemetry → Feedback**, `/admin/telemetry/feedback`. The panel's contract with the tab's
header (the date range applies to an issue's active span, *Keys* does not apply, the sub-tab label's counts
are not date-limited) is in [`ai-telemetry.md`](ai-telemetry.md) § 12b; the query parameters (`status`,
`category`, `model`, `q`) are in [`admin-page-urls.md`](admin-page-urls.md) § 2.

- **The switch card** comes first: the title *Feedback tool* names the section, and one row follows. The
  row has its label (*Enabled in Overseer chat*), which its switch references through `aria-labelledby`; a
  sentence of state with when and by whom it was last set; one large switch (`gh-switch gh-switch-lg`); a
  `role="status"` line; and an error line. The sentence of state reads *On by configuration default. The
  assistant may file reports in ordinary chats.*, *On since {time} by {name}. The assistant may file
  reports in ordinary chats.*, *Off by configuration default. The assistant is not offered the tool.*, or
  *Off since {time} by {name}. The assistant is not offered the tool.* Under the row, a note points to the
  launcher: *GnollBench runs choose this per run: GnollBench → Run Benchmark → Feedback Tool.*

  The switch commits at once, is disabled while its request runs, reverts on an error and refetches. The
  status line reads *Overseer chat: the feedback tool is on.* (or *off*).
- **The filter bar**: a search over the issue's subject and summary; *Sort by* *Last seen* (the default),
  *First seen* or *Most occurrences*; the *Status* facet (defaulting to **New and Regressed**, the open
  work), the *Category* facet and the *Model* facet (the models that filed any occurrence); and removable
  chips. Every change refetches.
- **The cards** are a server-paged list, in batches of 25. Each issue card shows its category and status
  badges (*Regressed 2×* when it regressed more than once) and the models that reported it; the subject as
  its title and the summary under it; four aligned metrics — *Occurrences* (with *N retained* when fewer
  remain), *Users*, *First seen*, *Last seen*; the latest expected location, related tools, *Model's
  suspected cause (hypothesis)* and version; an *Occurrences* disclosure that loads the occurrences newest
  first, ten at a time; and the triage form — a *Status* select without *Regressed*, whose help text reads
  *A resolved issue that recurs becomes Regressed; a dismissed one stays dismissed*, a *Note*, *Save*, and
  *Delete issue*, whose confirmation says the occurrences go with it.
- **An occurrence** shows its time, user, provider and model, version, the session and message ids as text,
  what was searched for, the expected location, how the mistake was noticed, the cause note, the details,
  and the *Truncated* and *Redacted* marks. The view does not show the conversation: reading a user's
  conversation from the admin side is chat access, which belongs behind the access journal (§ 14).

**The API** (`AdminFeedbackController`, `api/admin/feedback`, `AdminOnly`):

| Method and path | Purpose |
|---|---|
| `GET switch` · `POST switch/{target}` (`{ enabled }`) | The switch's state, `{ chat }`, as `{ enabled, setAtUtc, setByUserName, seeded }`; appending a change to it, `target` `chat` (case-insensitive; anything else, `gnollbench` included, 400 *Unknown switch.*), which returns it |
| `GET issues?startDate&endDate&status&category&modelId&search&sort&skip&take` | A page of issues (`status` comma-separated; `sort` `lastSeen`, `firstSeen` or `count`; `take` 1–100; unknown values 400) |
| `GET models` | Distinct model ids over all occurrences |
| `GET issues/{id}/occurrences?skip&take` | A page of one issue's occurrences, newest first |
| `GET counts` | Issue counts per status over all dates |
| `POST issues/{id}/status` (`{ status, note? }`) | Triage (404 unknown id; 400 unknown status or `regressed`) |
| `DELETE issues/{id}` | The issue and its occurrences |

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
  their answer, and the tool-call age prune does not touch them. The `Fingerprint` index is what a later
  cross-run view and a join to `OverseerFeedbackIssue.Fingerprint` will use.
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

The harness change is described in [`ai-benchmark.md`](ai-benchmark.md), *Harness Version 60 Updates*, and
the diagnostic consequence in the `server_benchmark_tool_diagnostics` skill.

## 12. The chat

The chat shows the call like any other tool call: its display name is *Feedback to developers*, and its
argument summary is the category in words followed by the subject (*Missing information: Dilithium
crystal*). The guide allows the model one sentence saying that it reported the issue.

## 13. Tests

`ReportFeedbackToolTests`, `ReportFeedbackArgumentsTests`, `FeedbackFingerprintTests`,
`FeedbackRedactionTests`, `FeedbackSettingsTests`, `FeedbackSwitchServiceTests`,
`ToolRegistryFeedbackGateTests`, `AdminFeedbackControllerTests` and `BenchmarkSelfReportsTests` in
`Overseer.Tests/UnitTests/`, with additions to `ToolExecutorRateLimitTests` (the exemption, the null
`RemainingBudget`, no parameter dump), `ChatRetentionServiceTests` and `AccountErasureTests` (occurrences
removed and counted, issues kept), `BenchmarkToolCallRecorderTests`, `BenchmarkReportBuilderTests` and
`BenchmarkServiceTests`. On the client: `admin-feedback.service.spec.ts`,
`telemetry-feedback.component.spec.ts`, and additions to the telemetry tab's and the chat's specs.

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
- **The client.** The switch card and its pointer in `telemetry-feedback.component.spec.ts`, the route in
  `admin-feedback.service.spec.ts`, the Feedback Tool field in `benchmark.component.launcher.spec.ts`, its
  request, remembered value, repeated setup and recommended-run reset in `benchmark-launcher.state.spec.ts`,
  and the *feedback tool withheld* tag in `run-facts.spec.ts`.

## 14. Deferred

Not in this version, and named so nobody assumes otherwise:

- **Merging and splitting issues**, for problems the model named differently or conflated.
- **A triage agent**: semantic grouping where exact grouping proves too fine, and reading the reporting
  turn's conversation, which is chat access and belongs behind `ChatAccessAuditLog`.
- **An age prune for occurrences.**
- **A notification** on a new or regressed issue; a `ConfigHealthService` alert was rejected, because
  issues are records to triage, not configuration problems.
- **The GnollBench cross-run view** ("the same gap, hit by four models"), a calibration metric, and a join
  from self-reports to chat issues by fingerprint.
- **Self-reports in report packs** and in the AI-written report documents.
- **Feeding report text back to a model.** No model reads it in this version.
