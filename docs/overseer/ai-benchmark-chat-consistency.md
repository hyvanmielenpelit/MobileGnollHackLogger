# GnollBench Chat Consistency

GnollBench is the Overseer AI benchmark (Admin → GnollBench). **Chat Consistency** is its eighth
sub-tab and the method behind it: from GnollBench runs made over time, it judges whether the Overseer
chat with one model has stayed as good, as fast and as cheap as it was, and which side a change fits —
ours, the provider's, or our infrastructure.

This document specifies the method as the code implements it. The code is in
`Overseer/Services/ChatConsistency/` (analysis, statistics, comparability, attribution, ledger),
`Overseer/Services/Telemetry/` (per-call telemetry), `Overseer/Controllers/AdminChatConsistencyController.cs`
and the client under `Overseer/ClientApp/src/app/admin/benchmark/chat-consistency-tab/`. The
agent-facing summary is the `server_chat_consistency` skill.

---

## 1. Aim

The question is always about the **Overseer chat with a given model**, not about the model alone. The
subject is the whole system a player talks to:

- the model and its configuration (provider, model id, thinking level, reasoning mode and summary,
  service tier, output cap, parallel execution mode, endpoint);
- the production chat system prompt;
- the tools and their guides;
- the corpora the tools read (knowledge base, wiki, source, their indexes);
- the agent loop, its budgets and timeouts.

A run measures that system, so a change can come from any of its parts. The analysis first reports
the **total change** of each measure, then — separately — which side the change fits.

The subject is one **model axis**: the nine candidate keys of
`BenchmarkCrossModelComparability.ModelAxisKeys`, rendered as `name=value` pairs
(`ChatConsistencyComparability.ModelAxisKey`). Two runs with different keys are different subjects;
runs of other subjects serve as controls, never as neighbors in a series.

## 2. The Evidence: GnollBench Runs Only

The analysis reads **stored GnollBench runs and nothing else** — runs with status `Completed`,
`CompletedWithLimits` or `CompletedWithErrors` (`ChatConsistencyMeasures.UsableStatuses`). It makes no
model call and spends nothing.

Only a rubric-graded run can show a change of **quality**: production chat traffic carries no rubric
and no grader, so it can show that a chat got slower or more expensive, but never that it got worse at
answering. A GnollBench run answers the same items under the production system prompt and tools and
grades every answer, so quality, speed, work and cost are measured on the same turns.

## 3. Operating Mode

GnollBench runs on the **development computer**: Overseer is started from Visual Studio for a session,
a run is launched by hand, and the run ends with the session. There is:

- **no background monitoring** and no scheduler — no run is ever started by the system;
- **no production probes** — the production chat is never sampled or measured by this method.

The consequence is a **scope**. A run samples the chat only at the hours it ran, so every verdict holds
within the time-of-week strata both periods sampled (§ 8) and says nothing about hours no run covered.
Every headline ends in that scope, for example *"… within weekdays 08–16 UTC"*.

## 4. Per-Call Telemetry

From `BenchmarkRun.CallTelemetryVersion` 1 (`BenchmarkService.CurrentCallTelemetryVersion`) every
candidate and grader model call of a run writes one `ModelCallTelemetry` row. `CallTelemetryVersion`
is **not a harness version**: nothing any model is sent depends on it.

### 4.1 Per call (`ModelCallTelemetry`)

| Group | Fields |
|-------|--------|
| Whose call | `Source` (`BenchmarkCandidate`, `BenchmarkGrader`), `GraderRole` (`Assessor`, `CoAssessor`, `SecondOpinion`, `ClaimVerifier`, `Synthesis`), `BenchmarkRunAnswerId` (cascades with the answer), `BenchmarkRunId` (synthesis only), `SystemAiApiConfigurationId`, `CallIndex` |
| Requested | `Provider`, `RequestedModelId`, `ThinkingLevelSent`, `ReasoningSummarySent`, `ServiceTierRequested`, `MaxOutputTokensSent`, `EndpointKind` (`official` or `custom`) |
| Served | `ServedModelId`, `ResponseId`, `RequestId`, `ServedServiceTier`, `ServedSpeed`, `FinishReason`, `IsRefusal`, `FallbackModelId`, `HttpVersion` |
| Before the successful send | `PermitWaitMs`, `BackoffWaitMs`, `FailedAttemptMs`, `AttemptCount`, `Http429Count`, `Http5xxCount`, `StreamErrorRetryCount`, `FinalHttpStatus` |
| Marks after the send | `HeadersMs`, `ServerProcessingMs`, `FirstEventMs`, `FirstReasoningMs`, `FirstOutputMs`, `FirstToolCallMs`, `LastDeltaMs`, `CompletedMs`, `StreamEndMs` |
| Visible output | `OutputDeltaCount`, `VisibleOutputChars`, `Last80DecodeSpanMs`, `Last80VisibleChars` |
| Usage | `InputTokens`, `CachedInputTokens`, `CacheWriteTokens`, `OutputTokens`, `ReasoningTokens` |
| Other | `RateLimitJson` (the successful attempt's rate-limit headers), `ErrorKind` (`canceled`, `permit_timeout`, `timeout`, `exception`, `stream_error`, `http_429`, `http_5xx`, `http_<status>`) |

Every mark is milliseconds from the send of the **successful** attempt (the last attempt when none
succeeded); the waits are the time before it. A call that failed for good still writes a row, with
`ErrorKind` set. Null means "not recorded", never zero.

### 4.2 Per answer and per run

- **`BenchmarkRunAnswer`**: `StartedAtUtc`, `CompletedAtUtc` (the candidate turn's wall-clock bounds),
  `PermitWaitMs`, `BackoffWaitMs` (Overseer's own waits inside `DurationMs`), `RetryAttemptCount`,
  `ServedModelId` (the id every reporting call agrees on; null when they disagree) and `ModelCalls`.
- **`BenchmarkRun`**: `CallTelemetryVersion`, `ServedModelIdsJson` (served model id → candidate call
  count) and `IsConsistencyAnchor` (§ 7).

### 4.3 Where each mark comes from: ours and the provider's

| Ours — measured by Overseer | The provider's — reported by it |
|------------------------------|---------------------------------|
| `StartedAtUtc` of the call and the turn (wall clock) | `ServedModelId`, `ResponseId`, `RequestId` |
| Permit wait (the request governor, its cooldown included), retry backoff, failed attempts | `ServedServiceTier`, `ServedSpeed` (Anthropic `usage.speed`) |
| Attempt, HTTP 429, HTTP 5xx and stream-error retry counts; final status and HTTP version | `FinishReason`, `IsRefusal`, `FallbackModelId` |
| `HeadersMs`, `StreamEndMs`, and the stream milestones stamped by `ProviderCallMeta` | `ServerProcessingMs` (`openai-processing-ms`), `RateLimitJson` |
| `OutputDeltaCount`, `VisibleOutputChars`, the last-80 % decode span | All token counts |

The milestones (`FirstEventMs` … `CompletedMs`, and the visible-text deltas behind the decode span) are
Overseer's `Stopwatch` stamps, taken right after the provider adapter reads each stream line and before
any sanitizer; the events they mark are the provider's. `ProviderResponseHeaders` reads only the request
id (`x-request-id`, `request-id`, `x-goog-request-id`), `openai-processing-ms`, and the
`x-ratelimit-*`, `anthropic-ratelimit-*` and `retry-after` headers.

### 4.4 Derived measures

`CallTelemetryMeasures` defines them once for the run report and the analysis:

- **Time to first answer text** — from the turn's start to the final candidate call's first visible
  output, minus every permit and backoff wait of the turn; failed attempts are provider time and stay
  in.
- **Answer streaming rate** — the final candidate call's visible tokens per second over the last 80 %
  of its visible text deltas; for Anthropic, which counts thinking inside its output tokens, estimated
  at 4 characters per token and marked estimated. From harness 54 an answer has **no measurable rate**,
  for every provider, when that window's decode span is under 500 ms (`MinMeasurableDecodeSpanMs`) or the
  rate is over 1,000 tokens/s (`MaxPlausibleTokensPerSecond`): its visible text arrived in one burst
  after thinking, and the figure would measure delivery, not decoding (`IsStreamingRateUnmeasurable`).
  Such an answer leaves the streaming-rate axis.
- **Net model time** — model time minus the turn's own waits.
- **Own-wait share** — own waits ÷ model time, over answers with candidate telemetry.

### 4.5 The run report's Timing Decomposition

A run with call telemetry adds a **Timing Decomposition** item to the timing block of the run report's
*§ 2 Results Summary*: the telemetry version and coverage, time to first answer text (P50, P90), the
answer streaming rate, Overseer's own waits and retries with the HTTP 429 and 5xx counts, the served
model ids and tiers, and the own-wait share, which is called dominating above 25 %. The full layout is
in `ai-benchmark.md` § 5 *Per-Call Telemetry and the Timing Decomposition*.

## 5. Measurement Changes and Overseer Changes

Two runs of one subject can differ in two opposite ways, and the analysis treats them oppositely
(`ChatConsistencyComparability`):

- A **measurement change** alters how the chat is *measured* — the graders, the scoring, how time or
  tokens are counted, the prices. It is **bridged or segmented**: runs on either side of it are not
  compared on the measure it breaks unless something removes the difference.
- An **Overseer change** alters what the chat *is* — the system prompt, the tool guides, the corpora,
  the prompt options, the budgets, a harness version that changed candidate input. It **never excludes
  data**; it becomes a dated **event** that control runs later attribute (§ 6).

### 5.1 Axes and segments

Each measure is segmented on its own **axis**: `Quality`, `SpeedTelemetry`, `SpeedLegacy`, `Work`,
`Cost`. Walking a subject's runs in start order, every unbridged boundary starts a new segment on the
axes it breaks; two runs are comparable on an axis when they share its segment.

| Change (`MeasurementChangeKind`) | Detected when | Breaks | Bridged by |
|---------------------|---------------|--------|------------|
| `Grading` | an assessor (or panel), co-assessor, second-reader or claim-verifier snapshot or configuration key differs, or the ledger between the two harness versions carries `Grading` | Quality | both runs covered by one common grader (§ 7) |
| `Scoring` | the scoring method version or the scoring-profile key differs, or the ledger carries `Scoring` | Quality, legacy speed | a rescore under one profile |
| `Scoring` (speed calibration) | the `SpeedCalibration` key differs | legacy speed | a rescore under one profile |
| `CandidateTiming` | the ledger carries `CandidateTiming` | legacy speed | never |
| `CallTelemetry` | two telemetry runs differ in `CallTelemetryVersion` | telemetry speed | never |
| `CandidateAccounting` | the ledger carries `CandidateAccounting` | work, cost | never |
| `Pricing` | the pricing snapshot key differs | cost | always: every run is costed at one price card |

A run's harness is its `HarnessVersion` together with a differing `RerunHarnessVersion`; the ledger
impact between two runs is the union over every pair of their versions, and an unknown version counts
as every change (§ 14).

**Speed exclusions** never move a boundary. A run that answered questions in parallel
(`MaxParallelQuestionsUsed` > 1) is left out of both speed axes; a run without call telemetry is left
out of telemetry speed. Both stay on every other axis.

**Choosing runs.** For each endpoint the analysis keeps the latest segment both periods share and lists
the runs it left out as a data-quality note. When the periods share no segment, the endpoint is not
computed and the refusal names the change — for quality, with the advice to re-grade every compared run
with one assessor. **Relaxed pooling** (a request option) pools across the boundary instead and caps the
affected grades at Indicated.

### 5.2 Overseer events

`DetectOverseerEvents` walks each series and records an event when one of these run fields changes
against the subject's latest earlier run that recorded it (a null field is "not recorded" and neither
starts nor ends an event):

`CandidateSystemPromptSha256`, `ToolGuidesSha256`, `KnowledgeBaseHeadSha`, `WikiHeadSha`,
`SourceCodeHeadSha`, `CorpusIndexFingerprintsJson`, `CandidatePromptOptionsJson` (compared in canonical
form), `ToolIterationCapsJson`, `TotalModelCallCapsJson`, `QuestionTimeoutSecondsJson`,
`MaxToolCallsPerQuestionUsed`, and `HarnessVersion` when the ledger impact between the two runs includes
`CandidateInput`.

An analysis takes the events between its first baseline and last comparison run, from the target series
and every control series, one per kind and change.

**For display, the tab groups them.** The timeline, the Results step and the *Before vs after an
Overseer change* preset show **composite events** (`groupOverseerEvents`, client-side): every Overseer
event of one UTC day under one harness version is one composite, and a harness change starts a new
one. Each composite has one chart marker, `E<n>`, numbered in time order, and lists the kinds that
changed with the number of runs that showed each. The grouping is presentation only: the analysis still
takes the events one per kind and change, as above. Report charts are drawn the same way, and
`CC_REPORT_CHART_VERSION` (4, in `chat-consistency-report-charts.ts`) enters their settings hash, so newly
drawn report charts are told apart from those drawn before the grouping, the timeline numbering below,
or the battery-run points, the validated palette and the GnollBench logo of version 4;
documents already written keep their charts. An `E` number in an older report therefore need not match
the one the tab shows today.

Results and newly drawn report charts take each composite's `E` number from the timeline loaded in
step 1, so a change keeps its number across the steps. A composite the timeline lacks (a control-series
change, or a span outside the step-1 dates) is numbered after the timeline's last, so it can carry a
higher number than a later composite.

## 6. Events and Control Runs

An event in the compared span means the subject's change could be ours. A **control run** separates the
two: a run of **another subject** made under the **same Overseer build**. If the control moved the same
way, the change fits our side; if the target moved and the control did not, it fits the target's side.

- **Instrument fingerprint** (`OverseerInstrumentFingerprint`): the lower-case hex SHA-256 over exactly
  the event fields of § 5.2 and the run's harness identity, a null field rendered as `(none)`. Equal
  fingerprints mean the same Overseer build as far as the candidate is concerned.
- **Matching** (`MatchControlRuns`): for every target run, the candidate controls of another subject
  with an identical fingerprint, the same suite (`BenchmarkSuiteIdUsed`, else `BenchmarkSuiteId`, else
  the suite name) and at least one item in common. Candidate controls are the request's explicit
  `controlRunIds`, or every other subject's usable run on a target suite in either period. A control
  enters the period its own start falls in (or the nearer one).
- **Missing-control notes**: a period without any qualifying control gets one note per suite and
  build, naming the run that would close the gap — *"No control run for period comparison: make a run of
  \<model\> (a provider other than \<provider\>) on suite \<suite\> under the same Overseer build as run
  #N (instrument \<12 hex\>)."* The model is the first of the request's `availableOtherProviderModels`
  from another provider.
- **Difference in differences** (`ItemDifferenceInDifferences`): per endpoint and control subject,
  item-paired (target change) − (control change) with a run-cluster bootstrap (items resampled too,
  except for the speed endpoints). Each effect records whether the DiD interval includes 0, whether it
  **separates the target** (excludes 0 on the side of the target's change), whether the control **moved
  the same way**, and whether it is the **same provider**.

## 7. Common-Grader Re-Grading and Anchors

Graders drift and grader rosters change, so native grades from different dates are a weak basis for a
quality comparison. Two mechanisms remove that doubt:

- **Common grader.** One assessor re-grades every compared run through the existing assessor
  calibration (`BenchmarkService.RunAssessorCalibrationAsync`), writing `BenchmarkAssessorCalibration`
  rows. The analysis uses the calibrations of one assessor snapshot that cover **every target run**
  without error — the requested `commonGraderSnapshotId`, or automatically the snapshot covering most
  control runs, then the latest — and the latest calibration per run. P1 then reads the calibration's
  per-answer quality, and the `Grading` boundary between covered runs is bridged. Control runs it does
  not cover are left out of the quality DiD. A requested snapshot that does not cover every target run
  is reported, and native grades are compared.
- **The re-grade** (`ChatConsistencyRegradeService`) is the method's only spending path. A run is
  eligible when it is usable, finished its suite, was graded under **scoring method 14**
  (`BenchmarkAssessmentPrompt.ScoringMethodVersion`) and has its board recorded. The estimate comes
  first — each run's recorded assessor tokens priced at the chosen assessor's current price, always an
  estimate — and the job starts only on a request with `confirmed: true`, not while a benchmark run or
  another re-grade is in progress, and only when the spending guard allows it. One job at a time, at
  most 200 runs, run by run.
- **Anchor and grader drift.** A run marked `IsConsistencyAnchor` (several may be) is re-graded by one
  assessor snapshot on different dates. Its **grader drift** is the latest re-grade's mean quality minus
  the earliest one's over the answers both graded, from calibrations on at least two distinct UTC days;
  within ±3 points it passes.

The P1 **Grader stability** check passes when a common grader covers every compared run; otherwise
it reads the anchor's drift; with neither, it fails — so a quality verdict on native grades without an
anchor is at most **Indicated**, and the next-run list asks for a re-grade or an anchor.

## 8. Common-Support Time Strata

Provider latency depends on load, and load on the hour. The analysis therefore compares speed only
**where both periods were sampled**:

- **Twelve strata** (`AssignStratum`): six 4-hour UTC blocks (00–04 … 20–24) on weekdays (indexes 0–5)
  and the same blocks on Saturday and Sunday UTC (6–11).
- An answer's stratum comes from its recorded `StartedAtUtc`; without one, a sequential run's answer is
  estimated at the run start plus the preceding answers' durations, a parallel run's at the run start.
  Estimated starts are used only for strata, and their count is a data-quality note.
- **Common support**: only strata present in both periods are used; each contributes its
  within-stratum shift with **equal weight**, so a change in when the runs were made cannot pose as a
  change of speed. Observations in strata one period alone sampled are excluded and their share
  reported.
- **Scope**: the common strata as text — *weekdays 04–12 UTC; weekends 16–20 UTC*, *(one time stratum)*
  when only one is shared, *no common time stratum* when none is.
- **US business hours** are weekdays 14–22 UTC. A common stratum counts as inside them when it is a
  weekday block overlapping that window (12–16, 16–20 or 20–24 UTC), and outside them otherwise. A
  change is called **load-independent** only when the common strata include at least one block of each
  kind.

## 9. Protocol V1

The method is pre-declared (`ChatConsistencyProtocol.V1`) and immutable: any change of a default is a
new protocol version. The protocol is stored with every analysis (`ProtocolJson`).

| Id | Endpoint | Scale | Margin | Direction | Pairing and statistic |
|----|----------|-------|--------|-----------|-----------------------|
| P1 | Quality | difference, index points | ±3 points | higher is better | item-paired quality under the common grader (native grades when none covers every run); mean of per-item differences; two-level bootstrap, runs then items |
| P2 | Time to first answer text | log ratio | ±15 % | lower is better | net of Overseer's own waits; item-centered log times; Hodges–Lehmann shift per common stratum, equal-weight mean; bootstrap over runs only |
| P3 | Answer streaming rate | log ratio | ±10 % | higher is better | final candidate call's visible decode rate; as P2 |
| P4 | Work per turn | log ratio | ±15 % | reported as *more work* / *less work*, never better or worse | total candidate output tokens per item; Hodges–Lehmann of per-item log differences; two-level bootstrap |
| P5 | Cost per question | log ratio | ±10 % | lower is better | candidate cost per item at **one price card** for every compared run; as P4 |

- **Multiplicity**: α = 0.05 with **Holm's** adjustment across the primary endpoints that produced a
  p-value; **Benjamini–Hochberg** at a false discovery rate of 0.05 within each secondary family.
- **p-values**: P1, P4 and P5 use the Wilcoxon signed-rank test on the per-item differences of the
  period means; P2 and P3 a run-cluster bootstrap p (twice the smaller tail share, each with one added
  to numerator and denominator, capped at 1). Intervals are bootstrap percentile intervals: 10,000
  replicates, seed 20261007, every resampling seeded from the protocol.
- **Items** pair only on an identical question and item revision; a revised item drops out on both
  sides, and an unrecorded revision pairs only with another unrecorded one. Both are data-quality notes.
- **Minimum replication** counts **units**: a run, or, in a battery comparison (§ 17.2), a **battery
  run**, whose usable members are merged into one unit:
  - P1, P4, P5 — **at least 2 units per period, on at least 2 distinct UTC days of the unit start, and
    at least 20 paired items**;
  - P2, P3 — **at least 3 units per period in at least one common stratum**;
  - fewer, and a decisive verdict is at most **Indicated**.
- **Battery runs as units** (analysis code version 4). A battery run's members answer different suites,
  so their item keys are disjoint; the unit's item map merges them (a key two members share, as with
  several rounds of one suite, is averaged). The bootstrap, leave-one-out, the retry-free and tier
  sensitivities and the minimum detectable effect resample or drop whole battery runs, and the speed
  strata's observation lists are per battery run. A battery run with a member outside the measurement
  segment, or not usable on an endpoint's axis, is left out whole with a `segment` note (*"Quality:
  battery run #12 was left out: its run #98 is outside the measurement segment; …"*). One battery run
  of two suites is therefore one unit, never the two runs the minimum would otherwise count. In a suite
  comparison, or with no compared set, every unit is one run and every number equals code version 3's.
- **Other constants**: power 0.8 for the minimum detectable effect; a common stratum enters the
  across-strata check with at least 2 runs per period; an own-wait share moving by 0.05 is material;
  an answer passes for the flip rate at quality ≥ 50 without a critical error; grader drift passes
  within ±3 points.
- **Price card**: the subject's current configuration pricing, else the latest run's pricing snapshot
  (or its catalog pricing), applied to every compared run, so a price change never registers as a cost
  change. Without one, P5 is not computed.
- **Overrides**: a request may override the margins, α, the bootstrap replicates (200–100,000) and
  seed, and the minimum paired items, runs, days and speed runs. Each override is recorded and labels
  the result *V1 with overrides: \<field\> \<from\> → \<to\>*. The wizard offers the margins and α.

## 10. Verdicts and Grades

Each endpoint gets one of **Lakens' four outcomes** against its margin, checked in this order
(`ChatConsistencyStatistics.Verdict`):

| Verdict | Condition | Label |
|---------|-----------|-------|
| Changed | Holm-adjusted p < α and the 95 % interval lies wholly beyond the margin | *degraded* / *improved* (P4: *more work* / *less work*) |
| Changed, negligible | the 95 % interval excludes 0 but lies inside the margin | *changed, negligible* |
| Equivalent | the 90 % interval lies inside the margin (TOST) | *equivalent* |
| Inconclusive | none of the above | *inconclusive* |

An endpoint the data cannot compute is *not computable*, with its reason.

Each decisive verdict carries an **evidence grade**:

- **Established** — publishable: a decisive verdict, every robustness check passed, the minimum sample
  met, telemetry-grade data, no relaxed pooling.
- **Indicated** — a decisive verdict with any of: a failed robustness check, legacy data (a run without
  call telemetry, or P2 on the legacy proxy), a sample below the minimum, relaxed pooling. The reasons
  are listed.
- **Not established** — inconclusive or not computable.

Every endpoint also reports its **minimum detectable effect** at α and power 0.8 (from the run-to-run
spread; *one run per period: run-to-run noise not estimable* when a period has one run) and the runs
per period that would bring it down to the margin. The **headline** leads every result:
*"Overseer chat with \<model\>: quality \<verdict\> (\<grade\>); speed …; work …; cost … within
\<scope\>"*, with any established reliability increase appended.

## 11. Robustness Checks

A check that cannot run is *not assessable* and does not lower the grade; a failed one makes the grade
Indicated.

| Check | Endpoints | Passes when |
|-------|-----------|-------------|
| Leave-one-run-out stability | all | the verdict holds without any one run (Equivalent: inside the margin; a change: the same sign) |
| Across sampled strata | P2, P3 | the verdict holds in every common stratum with at least 2 runs per period (needs two such strata) |
| No unexamined own-side explanation | all | restricted to answers without retries the verdict holds; for speed, the own-wait share did not move materially (or the measure is net of it) and parallel runs are excluded |
| Grader stability | P1 | a common grader covers every run, or the anchor's drift is within ±3 points (§ 7) |
| Runs on separate days | all | each period has the minimum runs on the minimum distinct days |
| Served-tier match | all | every answer was served at the requested tier, or the verdict holds restricted to those that were |

## 12. Secondary Families and Reliability

Secondary results are descriptive and adjusted within their family (Benjamini–Hochberg):

- **Quality detail** — the four dimension levels, the critical-error rate (Fisher), and per-item flips
  against the **null flip rate** between baseline replicates.
- **Reliability** — terminal failures, timeouts, empty answers, refusals, tool-budget exhaustion
  (answers), and HTTP 429 and 5xx responses (calls), each with Wilson intervals and Fisher's exact test.
  An increase is **established** when it is rejected, higher in the comparison and the run minimum is
  met in both periods; it is added to the headline.
- **Tool use** — tool calls and model calls per answer, and the share of the eight most used tools.
- **Reasoning tokens**, **answer length**, **net model time** (with P2 measured gross, own waits left
  in), the **shift function** (Harrell–Davis deciles, pointwise intervals), the **time-of-day contrast**
  (per-stratum shifts and US business hours minus other hours), the **difference in differences per
  control**, and the **implied generation rate** (Theil–Sen slope of decode time on output tokens).

Every result also lists the data-quality notes, the limitations (§ 21), and the **next runs** that
would resolve an open question — kinds *checkpoint*, *control*, *stratum* and *regrade*, each naming the
run whose setup to repeat.

## 13. Attribution

Attribution (`ChatConsistencyAttribution`) is a pre-declared decision table. It reports every
endpoint's **total change first**, then the rows that fire. A row names the **side** a change fits and
the events and controls behind it; it **never names a mechanism or an intent**. Only an annotation of
kind *ProviderConfirmedCause* is cited, verbatim with its date and source, as the provider's own
statement.

| Rule | Side | Fires when |
|------|------|------------|
| R1 overseer-change | ours | an endpoint changed, an Overseer event lies in the span, and a matched control moved the same way with a DiD interval including 0 |
| R2 not-attributable | undetermined | an endpoint changed, an Overseer event lies in the span, and no control qualifies; lists the candidate causes and the missing-control note |
| R3 declared-snapshot-change | provider | the served model ids differ between the periods |
| R4 served-configuration | provider | calls were served at another tier than requested, or by a fallback model |
| R5 model-behavior | provider | work, reasoning tokens, model calls or the tool mix changed, the streaming rate is Equivalent, and the change is isolated |
| R6 load-related | provider | P2 or P3 changed and the effect differs between strata, or a same-provider control moved with it, or the run-to-run variance exceeds the margin, or 429 / 5xx rates rose |
| R7 persistent-serving | provider | P3 changed at every decile and in every common stratum with one sign; *provider-wide* when a same-provider control moved too, else *model-specific*; *load-independent* only when the strata cover US business hours and outside them |
| R8 infrastructure | infrastructure | Overseer's own waits or retries account for the speed change (gross P2 changed and net did not, the own-wait share moved materially, or the change vanishes without retries) |
| R9 undeclared-change | provider | quality degraded or work changed for the target alone (a DiD separates it), the served model id did not change, and neither our infrastructure nor an Overseer change explains it — *flagged for review* |
| R10 improvement | provider | quality improved, Established: with a new snapshot, or undeclared |
| R11 undetermined | undetermined | every endpoint is Inconclusive, a changed endpoint fits no row, or rows of several sides fit one endpoint (*Multiple causes*) |

A provider-side row (R5–R7, R9, R10) needs its endpoint **isolated**: no Overseer event in the span,
or a control DiD separating the target. Without control runs, a change in a span holding an Overseer
event stays R2 — which is why control runs matter.

**What "deliberately slower" cannot be.** No row, and no report, can say that a provider slowed a model
on purpose, or why it got slower. The strongest speed finding is R7: a persistent serving change
**within the sampled hours**, provider-wide or model-specific, and load-independent only when both
kinds of hours were sampled. A mechanism appears only as a quoted ProviderConfirmedCause annotation.

## 14. The Harness Impact Ledger

`HarnessImpactLedger` classifies every harness version from 1 to the current
`BenchmarkAssessmentPrompt.HarnessVersion` by what it changed relative to the version before it:

| Flag | Meaning | Treatment |
|------|---------|-----------|
| `CandidateInput` (CI) | what the candidate is sent or allowed: system prompt, tool output, tool guides, budgets, timeouts, request parameters | an Overseer **event**, never a measurement change |
| `CandidateTiming` (CT) | how candidate time is measured | breaks legacy speed |
| `Grading` (G) | how answers are graded | breaks native quality; a common grader bridges it |
| `Scoring` (S) | how grades become scores | breaks native scores; a rescore under one profile bridges it |
| `CandidateAccounting` (CA) | how candidate tokens or cost are counted from what the provider reported | breaks work and cost; repricing does not resolve it |
| `ReportingOnly` (R) | reports, UI and storage only | breaks nothing |

`ImpactBetween(a, b)` is the union of the impacts of every version after the lower up to the higher,
together with the **same-stamp impact** of both ends — what may differ between two runs carrying one
stamp because the stamp was not moved when the harness was. An unknown, unparseable or unclassified
version resolves to `Unclassified` (every flag except ReportingOnly), so an unclassified harness bump is
treated as changing everything until it is entered. The classification is conservative: a version that
mixes kinds carries every flag that applies, a tool-output change counts as CandidateInput, and a
version the changelog cannot tell apart is marked conservative.

| Version | Flags | Summary |
|---------|-------|---------|
| 1 | Unclassified (conservative) | Baseline harness with no recorded changelog and no predecessor. |
| 2 | CI, CT, G, S | Per-question tool budget of 25 calls; model-attributable timing; artifacts scrubbed before grading; turn duration removed from the assessor prompt; scoring method 3. |
| 3 | CI, G, S (conservative) | Per-difficulty-band tool call budgets; recovered artifacts classified apart from transport defects; executed and blocked tool calls reported apart. |
| 4 | G, S | Critical error needs a verbatim quote (scoring method 5); deduction evidence; optional second-opinion pass; per-question assessor usage recorded. |
| 5 | CI, G | Four banded per-question caps including the timeout; pre-tool visible text moved to the thought channel; wider narration scrubbing of the graded answer. |
| 6 | G | Narration strip steps over unrecognized openers and orphan tokens before grading; removal count persisted; report annotations. |
| 7 | G, S | Unadjudicable claims recorded instead of deducted (scoring method 6); contested verdicts routed to a second reader; second-opinion modes; calibration runs. |
| 8 | CI, G | Game snapshots: the board reaches the candidate and the graders; AI-generated questions grounded in the board. |
| 9 | G, S | A deduction below level 6 must name its defect (scoring method 7); unevidenced deductions flagged; claim verifier role introduced. |
| 10 | G | Unverified-grounded deductions flagged; claim verifier prompt moved to the user turn; stage failures and live progress reported. |
| 11 | CI, G | Scope-aware budget refusal and remaining-budget warning in tool results; omission is never an Accuracy deduction; blind second opinions; verification before the trigger cascade. |
| 12 | CI, G; same stamp: CI, CA, G, S | Heading-scoped wiki_search snippets; candidate prompt options and Response Style recorded; blind backfill, shared JSON extractor and substitution guard in grading. |
| 13 | CI, CA | Per-question tool, iteration and model-call caps flattened to the Advanced figures; long-context, service-tier and scheduled pricing in candidate costing; model calls reported. |
| 14 | G, S | Completeness scope becomes a grading rule (scoring method 8); synthesis divergence detection; FlaggedPlusSample second opinions; replicate sets. |
| 15 | CI | get_item_stats macro parsing repaired; per-role grader cost tracking and an Anthropic cache-creation costing fix; HarnessVersion constant re-synchronized. |
| 16 | R | Run records the GnollHack wiki and source Git HEADs as provenance. |
| 17 | R | Every tool call persisted with arguments, result and timings; nothing the candidate sees changed. |
| 18 | CI, G, S, CA; same stamp: CA | wiki_search and nethack_wiki_search result caps, miss payloads and two guides; advisory grading limited to gradeable answers; no speed score for non-gradeable answers; Gemini usage counted once per call. |
| 19 | CI, G | Two source-tool contracts and guides changed; critical-error quote dispatched to the claim verifier; grading rule that a rubric omission is not an invention. |
| 20 | CI, G | Three tool guides changed; out-of-rubric Accuracy deduction checked by the claim verifier; verifier rule 3a. |
| 21 | CI, CT, G, S (conservative) | Terminal provider failures withhold the indices and skip grading; one provider retry policy for every provider, with an unrecorded effect on candidate timing. |
| 22 | CI, G | wiki_search clamps max_results and the definition matcher finds more definitions; unevidenced-deduction detection reaches level 5; re-run harness recorded. |
| 23 | CI, G | source_code_view stops at a whole line with a resume hint; get_function_definition falls back to any kind; unevidenced-deduction detector reads named defects. |
| 24 | CI, G | wiki_search stems English and reports match counts; grading preamble moved to a cacheable system segment; assessed difficulties become a comparability key. |
| 25 | CI, G | search_definitions miss carries an occurrence probe; assessor preamble rule on out-of-rubric claims; detector vocabulary; synthesis receives supported claims. |
| 26 | CI, G | Candidate message carries the no-greet instruction; monster_lookup and item_lookup return an exact-title article alone; detectors, verifier basis and difficulty prompt changed. |
| 27 | CI, G | wiki_search category filter and nethack_wiki_view resolution fixed; the four assessor levels are required; DimensionOutlier routed to a second reader. |
| 28 | CI, G, S | AD_SAMU flag description changed outside ToolGuidesSha256; verifier rule on resistance magnitude; speed model recalibrated into its own SpeedCalibration key. |
| 29 | CI, G | Production system prompt delivered as the first system message, so OpenAI candidates receive the prompt and Google and Anthropic candidates the board; delivery probes; verifier receives the board. |
| 30 | CI, G | wiki_search always returns an article's lead block; every grading path receives the board; contested answers re-graded with the verifier's findings. |
| 31 | CI, G, S | Scoring method 11; source_code_search filtered-miss hint, wiki_search ranking, flag unions and two guides; accused sentences sent to the claim verifier. |
| 32 | G | Every grading role reads the whole board ahead of the question; verifier rules 3d and 3e; accused-sentence extraction widened. |
| 33 | G, S | Scoring method 12; the claim verifier tests the assessor's own sentences; re-run and board-format provenance. |
| 34 | CI, G, CA | Source tools append a get_function_definition pointer; Gemini output tokens include thinking tokens; citation-liveness and flag rules; verifier rules 3f to 3h. |
| 35 | CI, G | Source tools mark lines inside #if 0, wiki_search guide and _policy.md changed; flag detector vocabulary; citation notes for unindexed files. |
| 36 | CI, G | A categorized wiki_search names its best match outside the category; citation notes for file-only and definition-line citations; verifier rule 3i. |
| 37 | CI, G | wiki_search returns a short article whole and drops the outside-category line; synthesis divergence check; verifier rule 3j. |
| 38 | CI, G | item_lookup searches the item and artifact paths; minified get_item_stats keeps the failure reason; the claim verifier judges the charged part. |
| 39 | CI, G | wiki_view names what it cuts and lookup headers name the path; contested verdicts read from the comment and evidence; macro citation notes; verifier rule 3l. |
| 40 | G, S | Two-family assessor panel whose published score is the panel mean; the second opinion becomes a reference reader; structured synthesis findings. |
| 41 | CI, G | wiki_view section-miss headings and [Not reachable] notes with five guides; omission detector and citation notes; panel union manifest; verifier budget per item. |
| 42 | G | Assessor prompt: the rubric's SOURCE line is provenance; definition-line citations; fabrication qualifiers; synthesis list attribution; knowledge-base topic guard. |
| 43 | CI, G | get_item_stats resolves the unique item named '... of \<name\>' and its guide changed; pronoun context for claims; verifier rule 3m; union manifest de-duplication. |
| 44 | CI, G | Source-code-reference prompt option recorded and disallowed by default; stats tools trim their inputs; accusedBy and suspectedBy attribution. |
| 45 | G, S | Scoring method 13: a critical error comes only from the rubric or the board; notAttempted defined; synthesis states how critical errors were resolved. |
| 46 | G | Rubric-charged Accuracy deductions sent to the claim verifier and RubricContradictedBySource raised; battery report fixes. |
| 47 | CI, G | Source tool output: DLLEXPORT functions live, search_definitions miss text and definition pointer wording; table-header quotes never anchor; verifier rules 3n and 3o. |
| 48 | R | Panel verification-cleared sensitivity reported; corpus index fingerprints recorded as provenance. |
| 49 | CI, G, S | Scoring method 14; _policy.md knowledge-base scope and get_knowledge_article guide changed; claim verifier batches lookups and records per-call usage. |
| 50 | CI, G | get_item_stats drops trailing words after a unique item name; the claim verifier's parse retry is a separate request with its own budget. |
| 51 | CI, G | _policy.md retrieved-figures sentence removed; get_item_stats name cleanup; claim verifier retry recorded and asked for every item. |
| 52 | CI, G | nethack_wiki_view headings notice for an over-cap article; forced-final instruction when the tools run out; one-line macro bodies count in citation liveness. |
| 53 | G | A member whose charge the verifier upheld is never verification-cleared; the synthesis is told the verdicts on its charges; report fixes. |
| 54 | CI, G | Graders told when source references are disallowed; get_constants reads brace-on-next-line enums and the source miss probe is whole-word; streaming-rate bounds and report fixes. |

**Same-stamp cases.** Harness 12 carries a same-stamp impact of CI, CA, G and S, and harness 18 of
CA: runs stamped with one of those versions may still differ in those respects, so even two runs with
equal stamps are segmented there.

**The CandidateAccounting flag** marks versions that changed how the candidate's tokens or cost are
counted from what the provider reported (13, 18, 34). Work and cost are segmented across them, and no
repricing removes the break, because the counts themselves differ.

**Classifying a new version.** `HarnessImpactLedgerTests` fails until every version up to the current
`HarnessVersion` has an entry and the last entry is the current version. The `server_chat_consistency`
skill says how to classify one.

## 15. Legacy Data

A run without call telemetry (`CallTelemetryVersion` null) is **legacy**. It stays on the quality, work
and cost axes and is left out of telemetry speed. When any compared run without parallel questions is
legacy, P2 falls back to the **legacy proxy** — model time per item on the legacy speed axis — and says
so; P3 needs telemetry and is not computed without it. Any legacy run, or the proxy, makes a decisive
verdict at most Indicated, and the result counts the legacy runs per period. The timeline draws legacy
runs as their own series, and the time figure draws the legacy proxy hollow.

## 16. Reproducibility

An analysis is computed once and saved as one immutable `ChatConsistencyAnalysis` row: the request
periods, the target and control run ids (by id, without foreign keys, so deleting a run keeps the
analysis), `ProtocolVersion` and `ProtocolJson`, `RelaxedPooling`, `CommonGraderSnapshotId`, the
`ResultJson`, `InputSha256` and `AnalysisCodeVersion` (`ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion`,
currently 4; version 2 records the run selection, § 17.2; version 3, from harness 54, reads the
streaming rate with its measurability bounds (§ 4.4), and the streaming-rate caveat counts the delivered
answers with no measurable rate: *"k delivered answer(s) have no rate: the visible text arrived in one
burst after thinking (a decode span under 500 ms or a rate over 1,000 tokens/s)."*; version 4 compares
within a battery or a suite and records the compared set as `comparisonSet` (`kind`, `key`, `label`),
`unitKind` (`run` or `batteryRun`) and `units` (each analyzed unit's id, kind, period, start and member
run ids), with battery runs as the unit of a battery comparison, § 9). A stored analysis keeps the
version it was computed under; its fingerprint and result do not change. **Run-mode invariance:** in a
suite comparison, or with no compared set, every number of a version-4 analysis equals version 3's on
the same runs; only the code version, the fingerprint and the new set and unit fields differ, and a test
pins it.

- **`InputSha256`** is the SHA-256 of a canonical serialization of every input: the code version, the
  request (its run selection included: the date label, the UTC bounds, the first and last run and the
  sorted left-out run ids, and the battery fields), the compared set and the battery run ids, the
  protocol, the unit kind and each unit's member run ids, each run with the fields and per-answer values the analysis
  reads (its call telemetry included), the unanalyzed runs (id, period, reason), the calibrations and
  anchor calibrations, the annotations and the price card, all in id order. Two analyses that differ
  only in the recorded selection have different fingerprints. Analyses saved under code version 1 keep
  their fingerprint and open with no selection record.
- Fixed inputs give fixed results: every resampling is seeded from the protocol, every collection is
  ordered, and the result JSON excludes the row's id and creation time.

An analysis cannot be deleted while report documents written from it exist.

## 17. The Chat Consistency Tab

Admin → GnollBench → **Chat Consistency** is a launcher page; the work is done in the **Chat
Consistency wizard**, six steps in a full-screen dialog.

### 17.1 The launcher

- **Open Chat Consistency Wizard** opens the wizard where it was left, on step 1 the first time.
- **How chat consistency works**, a disclosure listing the six steps under the wizard's own titles,
  open on the first visit and afterwards as the operator left it.
- **The *Current model* card** (`chat-consistency-tab/current-model-card/`), while a model is chosen: a
  full-width summary card between the hero and the saved analyses, on the shared `.bm-summary-card`
  styles. Its title is the model's name with its thinking-level, provider and service-tier badges, its
  meta line *Chosen in the wizard · remembered in this browser*, and its facts:
  - **Runs** — *6 runs · 4 with call telemetry*;
  - **Dates** — the dates as step 1 names them (*All dates*, *Last 30 days*, *2026-09-01 to
    2026-10-05*), then *· N runs in these dates* unless they are *All dates*; a small spinner stands in
    for the count while the runs load (shown after 0.3 s);
  - **In the analysis** — the runs the analysis uses, only while step 1 narrows them;
  - **First run** and **Latest run** (*#106 · 2026-10-01*);
  - **Suites** — the suites the model's runs answered;
  - **Compared** — the battery or suite step 1 compares within (*Two initial suites (revision 1)*);
  - **Latest analysis** — *#7 · <name> · saved 2026-10-02* with its headline, or *None yet*.

  Its actions are **Open latest run report** and, when an analysis exists, **Open analysis #N**. It is a
  read-out; the wizard makes the choice. Its content is read from the database every time the tab loads.
- **The remembered model.** The chosen model, its dates and the compared set are kept in this browser,
  in `localStorage['overseer.benchmark.chatConsistency.subject']` (`{ version: 2, modelKey, range,
  compare: { kind, key } }`; a version-1 record, `{ version: 1, modelKey, range }`, is read as one with
  no compared set), written whenever the model, the dates or **Compare** change. A stored set is used
  while the model's sets still offer it; otherwise the server's default applies. Once the model list loads with no model chosen, the
  tab restores them: a rolling preset (*Last 30 days*) is moved to now, and a record whose model is no
  longer listed, or has no runs, is removed. The run selection, the step and the analysis are not
  remembered. Another browser, private browsing or cleared storage starts without a model.
- **Saved analyses** — every analysis, newest first, each with its *Compared* set when it has one
  (code version 4), with *Open* and *Delete* (refused while report documents exist). *Open* switches to the analysis's model when it is another and opens the wizard on
  **Results**.

### 17.2 The wizard

A header names the model, the compared set with its unit count, and the dates (*Claude 5.5 Haiku
(xhigh) · Two initial suites (revision 1) · 2 battery runs · Last 30 days*; with no compared set the
model's run count, *GPT-6.1 Sol (medium) · 19 runs · Last 30 days*), with *· 15 in the analysis* while
step 1 narrows the units, with *Reload runs* on steps 1 and 2
and a close button. Under it, the step tabs; at the bottom, *Previous*, the step position with the
reason the next step is unavailable, and *Next* — *Analyze* on step 4, *Close* on step 6.

1. **Model** — the model (*Models with at least one usable benchmark run*), the dates, then the runs as
   cards with the **run selection** the analysis uses:
   - **Dates**: *All dates*, *Last 1 day*, *Last 3 days*, *Last 7 days*, *Last 14 days*, *Last 28
     days*, *Last 30 days*, *Last 90 days*, *Last 180 days*, *Last year* or *Custom*. A rolling preset
     counts back N × 24 hours (*Last year*: one calendar year) from the moment it was chosen, with an
     open end; the hint under the select names its start (*Since 2026-09-30 14:05 UTC · Reload runs
     moves it to now*), and *Reload runs* moves it to now. *Custom* shows **From (UTC)** and **To
     (UTC)**, inclusive UTC calendar days prefilled from the preset it replaces. Each is a `YYYY-MM-DD`
     text field (`2026-9-5`, `2026/9/5` and `2026.9.5` become `2026-09-05`) with a calendar button that
     opens a glass calendar of UTC days.
   - **Compare**: the battery or suite the analysis compares within, options grouped *Batteries* and
     *Suites* (*Two initial suites (revision 1) · 2 battery runs*, *GnollHack Player Assistance Benchmark
     Suite · 3 runs*), with the hint *Results are comparable only within one battery or one suite.* A
     battery set is one battery definition, every revision with the same definition hash; its label
     names the revisions. The default is the battery of the model's newest battery run in the dates,
     else the suite of its newest run; a remembered choice wins while it is offered. Items pair only
     within one exam, so a comparison across suites would rest on whichever suite both periods happened
     to answer. Changing the set clears the selection and says so (*The selection in step 1 was cleared
     because another battery or suite is compared.*).
   - **With a suite compared**, the run cards below show that suite's runs, battery members included:
     a member's card carries *Battery run #12 · suite 1 of 2*, and the *Suite* facet is replaced by
     *Origin* (*Standalone*, *Battery member*).
   - **With a battery compared**, the cards are **battery runs** (*Battery runs of <model>*), newest
     first: an *Include battery run #12 in the analysis* checkbox labeled by the battery name; the
     battery run id, status, harness (*Harness 54*, or *Harnesses 53, 54*) and the *First run*, *Last
     run*, *Left out* and *Incomplete* tags; the start time and *2 of 2 suites*; **First run** and
     **Last run** toggles; *Open battery run report*; the eligibility per axis (eligible when every
     member is, otherwise the members' reasons, each prefixed with its run id); and the member runs
     (*#98 · GnollHack Player Assistance Benchmark Suite · Completed · harness 54*, each with its run
     report). **A battery run with a suite that has no usable member** (failed, superseded or
     guard-failed) is listed with its checkbox disabled and the reason *Incomplete: 1 of 2 suites
     usable*, and is never analyzed: it would pair on fewer items than the other units of its period.
     The filter bar searches (`#id`, battery, harness, status, member run), sorts (*Newest first*,
     *Oldest first*, *Harness*) and filters by *Harness*, *In the analysis* and *Eligibility*. The
     selection band reads *Battery runs in the analysis — 2 of 3 battery runs in these dates · from #11
     (2026-10-08) to #12 (2026-10-08) · 1 left out · 1 incomplete*, and the selection below works on
     battery runs as it does on runs.
   - **Runs of the model**, one card per run, newest first: an *Include run #N in the analysis*
     checkbox labeled by the suite name; the run id, status, harness, *Legacy* or *Recorded*, and the
     *Anchor*, *First run*, *Last run* and *Left out* tags; the start time and served model; **First
     run** and **Last run** toggles, *Open run report* and *More actions* (*Repeat this run's setup*,
     *Mark as anchor* / *Unmark anchor*); the per-axis eligibility with the reasons; and the segment,
     telemetry, re-grade coverage and matched controls. A filter bar searches (`#id`, suite, harness,
     status, served model), sorts (*Newest first*, *Oldest first*, *Suite (A–Z)*, *Harness*) and
     filters by *Suite*, *Harness*, *Telemetry*, *In the analysis* and *Eligibility*; ten cards show at
     a time, with *Show 10 more* and *Show all N*.
   - **Loading.** While the chosen model's runs load for the first time, the list's place holds the
     shared ring (`.dc-ring`) and *Loading the runs of <model>…*, both hidden from assistive technology
     because the step's status line announces the same text; they appear only after 0.3 s, so a fast
     load shows nothing. On a reload the run cards stay in place, dimmed (`is-refreshing`, `aria-busy`),
     with a small *Updating…* spinner beside the *Runs of <model>* heading, shown after 0.3 s as well.
   - **The selection.** Without a mark or a left-out run, every run in the dates is analyzed. *First
     run* and *Last run* bound the runs the analysis uses, in start order (then run id); a first run
     later than the last clears the last, and the reverse, each saying so; pressing a pressed toggle
     clears its mark. Clearing a card's checkbox leaves the run out. A run before the first or after
     the last reads *Before the first run (#21)* / *After the last run (#93)* with its checkbox
     disabled, also when it was left out before the mark was set; its left-out id stays in the
     selection. The **Runs in the analysis** band above the filter bar sums it up (*Runs in the
     analysis — 15 of 19 runs in these dates · from #21 (2026-09-20) to #93 (2026-10-05) · 2 left
     out*), shows the marks and up to six left-out runs as removable chips (*+N more* filters the list
     to *Left out*), offers *Clear selection (N)*, and warns *No run is left in the analysis. Check at
     least one run.* **The search and the filters change what is shown, never what is analyzed.**
   - The selection is cleared when another model is chosen, and when a saved analysis is opened (*The
     run selection in step 1 was cleared to show the saved analysis.*); a reload that no longer lists a
     marked or left-out run drops it and says so. The dates stay.
2. **Timeline** — the chart workspace (§ 17.3). It draws every run in the dates, not only the runs in
   the analysis, so the composite events keep their numbers; with a battery compared, one point per
   battery run of the set (*Plot by: Member runs* shows the runs).
3. **Periods** — the periods from a preset (*Launch vs last 14 days*, *Before vs after an annotation*,
   *Before vs after an Overseer change*, which offers the composite events of § 5.2, *Confirm on later
   data*, *Custom dates*) or by hand, as inclusive UTC dates in the same date fields as step 1; Protocol
   V1 with its margins, and *Override the protocol* for the margins and α. **The presets span the units
   in the analysis**, from the first to the last of them, as the note under the presets says: *Presets
   use the runs chosen in step 1: #21 (2026-09-20) to #93 (2026-10-05), 15 runs.*, or *Presets use
   every run in the dates: …* while step 1 chooses nothing; with a battery compared, *Presets use the
   battery runs chosen in step 1: #11 (2026-10-08) to #12 (2026-10-08), 2 battery runs.* A changed
   selection re-applies the chosen preset; dates typed by hand stay. *Confirm on later data* takes the
   last saved analysis of the same model and the same compared set.
4. **Runs and controls** — the baseline and comparison runs from the runs in the analysis (eligible
   runs preselected, again whenever the step-1 selection changes), the matched control runs, the
   common-grader **re-grade** (estimate dialog first; nothing spends until *Re-grade* is pressed), *Pool
   across measurement segment boundaries*, and a preview of the common strata, the composite Overseer
   events in the span, the missing controls and *Left out in step 1: #45, #51* for left-out runs inside
   either period. With a battery compared, each period lists battery runs (*Use*, *Battery run*,
   *Started (UTC)*, *Suites*, *Eligible*, *Matched controls*), eligible complete ones preselected; the
   controls stay per run. *Analyze* runs and saves the analysis, with the compared set and the step-1
   selection (§ 19), and moves to Results; *Stop Analysis* stops it.
5. **Results** — the headline, the verdict table with estimates, intervals, grades and detectable
   effects, the attribution grouped by side after the total changes, the next runs (each with *Repeat
   this run's setup*), the charts, the events in the analyzed span as an event list (§ 17.3), the **Run
   selection**, the limitations and data-quality notes, and the identity (analysis id, `InputSha256`,
   analysis code version). The *Run selection* section lists *Dates* (the step-1 label with its UTC
   bounds, *Last 30 days · 2026-09-07 09:00 UTC to the last run*), *First run*, *Last run* and *Left out
   in step 1* (a run id or *none*), then *Not analyzed*: one line per reason with its runs and period
   (*Left out in step 1: #45 (baseline), #51 (comparison)*; *Not selected in step 4: #60
   (comparison)*), or *Every usable run of the model in the periods was analyzed.* An analysis saved
   under code version 1 has no such section. A version-4 analysis adds a *Compared* line under the
   headline; in a battery analysis the marks and the unanalyzed entries name battery runs (*battery run
   #11*; *#305 (comparison, battery run #13)*), and runs of another battery or suite are listed as
   *Outside the compared set*.
6. **Reports** — the Chat Consistency Report documents (§ 20).

Step 1 is always open; steps 2 and 3 need a model, step 4 valid periods and overrides, and steps 5 and 6
a result, analyzed or opened from the saved analyses. A step that cannot be opened stays in the tab row,
marked unavailable, with its reason. Each step is kept once shown, so closing and reopening the wizard,
or changing step, keeps a table's sort and page, the chart zoom and an analysis in progress. **Escape
and the close buttons are refused while a chart export runs or while the Reports step attaches report
charts**, since closing would strand a half-written batch. For the same duration the model and the dates
in step 1 are locked — the *Dates* select keeps its value, the date fields are read-only and their
calendar buttons open nothing, all still focusable — with the reason shown under them, and a repeated
Escape cannot close the wizard either. The run selection, the step and the analysis live as long as the
tab: switching to another GnollBench sub-tab loses them. The model and the dates are also remembered in
this browser and restored when the tab loads again (§ 17.1).

### 17.3 The Timeline step

A settings sidebar — resizable from 18 to 40 rem (at most half the workspace, 26 rem by default) and
collapsible from the view bar — beside two views of the live charts. Its tab row wraps onto a second
line when the sidebar is too narrow for *Data · Events · Annotations · Download*, so no tab is cut off;
Left and Right still move through the four tabs in order. There is one chart per measure:
**Intelligence per run** (native and common-grader), time to first answer text (legacy proxy points
hollow), answer streaming rate, **output tokens per answer**, **tool calls per answer**, cost per
question, reliability, and *Runs and events*. Output tokens and tool calls are two charts rather than one with two value axes,
which would suggest a relation between two arbitrary scales. Each chart draws its markers — composite
Overseer events `E<n>`, annotations `A<n>` and served-model changes `S<n>`, their tags staggered in a
band above the plot — names them under the chart, and has a *Show data* list of data cards.

**Battery runs.** With a battery compared in step 1, the charts draw **one point per battery run** of
the set, as the analysis counts it: the timeline's `batteryPoints`
(`ChatConsistencyTimeline.BatteryPoints`) pool each battery run's usable members — medians and means
over the union of their answers, not the mean of the members' medians, and a common grader's mean
weighted by items, kept only when every member was calibrated by it. A battery run's Intelligence is
its battery analysis's **Overall Intelligence Index**, the number the battery report, the leaderboard
and Model Comparison show: the latest stored analysis, used only while it covers the battery run's
current members. Without one, with a stale one, or for an incomplete battery run there is no
Intelligence point, and the takeaway and the data card's *Note* field say why (*No battery analysis.
Compute it from the battery report.*). Every data card of a battery chart adds *Suites* and *Member
runs*, the members named with their suites. **Plot by** on the Data tab switches to *Member runs*, the
per-run charts, for looking inside a battery; it returns to *Battery runs* when another set is chosen.

**The styling.** The series colors come from a palette validated for color-vision deficiencies and
contrast on the chart surface, and the color follows the measure, not the rank: Intelligence gold, time
to first answer text blue, streaming rate aqua, output tokens violet, tool calls orange, cost magenta.
Lines are 2 px with ringed points; each reliability rate has its own point shape as well as color. A
chart with one series has no legend box (its title names the series) and an area wash under the line.
On a chart drawing **at most two series, every point carries its value**, in the data cards' precision
(*82.0*, *39.3 s*), or the chart's **Decimal places**, above the point (the second series below it, either flipping where the plot's edge
would cut it), over a halo of the background so it reads across lines and fills; a point not in the
analysis has its value muted. Where labels would collide, the lower-priority one is dropped: the latest
point, the highest and the lowest are placed first, then the rest left to right, so those three always
remain on a dense timeline and zooming in shows more. *Reliability* and *Runs and events* carry none.

**The value axes** never mislead. The ratio measures — time, streaming rate, output tokens, tool calls,
cost and the reliability shares — **start at zero**, so a point's height is proportional to its value;
the reliability axis ends at 100 % at most and shows at least 0–10 %, so all-zero rates do not fill the
plot. The **Intelligence axis** keeps its 0–100 scale's context and shows **at least 20 points** on
nice bounds (two battery runs at 82.0 and 82.4 draw on 70–90, ticks every 5): Artificial Analysis
states its Intelligence Index's 95 % confidence interval as under ±1 point after more than ten repeats,
and a GnollBench battery run is one repeat of far fewer questions, so differences of a few points are
within noise, and a 20-point window keeps a ±2-point wobble at about a tenth of the plot height instead
of filling it. *Show the full 0–100 Intelligence scale* on the Data tab draws 0–100. Each axis pads the
data by 15 % of its span, snaps both bounds to a step of 1, 2, 2.5 or 5 × 10ᵏ chosen for about five
intervals, and widens step by step to its least span (downward first for Intelligence); the tick labels
take the decimals their step needs, so no two read alike. The bounds come from the drawn series, as the
time range does. The time axis ticks fit the span (hours with the
date on each day's first tick under a day's step, *Oct 8* under a year, *2026-10* beyond), there are no
vertical grid lines, the period bands are named at their top left, and the markers are drawn in neutral
inks — told apart by dash, tag letter and a filled or outlined tag — so color stays with the data.

**The header band.** Each chart draws its title and a subject line — the model, the compared set and
what a point is (*Claude 5.5 Haiku (xhigh) · Two initial suites (revision 1) · battery runs*) — with
the **GnollBench logo** on the right. It is the same on screen, in **Copy**, **Download**, **Download
all** and, logo only, in the report charts, whose documents print their own captions. On screen the
header band is the top of the figure: the figure's own caption is visually hidden, still naming the
figure for assistive technology, and **the takeaway sentence follows the chart**, then a footer row
with the marker line and *Show events* on the left and, on an All charts tile, its **Copy**,
**Download** and **Open in Single view** on the right.

**The tooltip** appears only over a point, not anywhere over the plot, and is small: the point and its
start as the title (*#11 · 2026-10-08 07:14 UTC*) and one line per series with its short name and value
(*Estimated: 422.2 tok/s*, *Overall Index: 82.4*), the series color only on a chart drawing more than
one. A battery run's members are on its data card, not in the tooltip.

**Show data** opens the chart's numbers as one bordered list of **data cards**, one row per run or
battery run, separated by hairlines and spanning the figure's width with nothing scrolling sideways;
its summary counts them (*Show data · 2 battery runs*). Each row has the unit as its heading (*Battery
run #11*), its start at the row's right, and every other column as a labeled field under them;
*Member runs* lists one member per line, its id and then its suite (*#96 GnollHack Player Assistance
Benchmark Suite*); *Member runs*, *Note*, *In the analysis*, *Served model* and any long value take a
whole row, an empty value reads *—*, and an empty *Note* is left out. On a narrow chart the start moves
under the heading and the fields stack in one column. The list scrolls on its own beyond 24 rem.

**Runs not in the analysis** — left out in step 1, or before its first or after its last run — are
drawn as **gray crosses**, and every line segment touching one is gray and dotted, so shape and dash,
not only color, mark them; the legend keeps each series' own symbol. The caption counts them (*2 runs
not in the analysis are drawn as gray crosses.*, counting runs with a value in any of the chart's
series), the tooltip adds the reason on a second line (*Not in the analysis: left out in step 1*), and
every data card gains an *In the analysis* field (*Yes*, *No — before the first run*). Without a
step-1 selection the charts are drawn as before. The values, scales and gaps do not change.

The sidebar has four tabs:

- **Data** — with a battery compared, **Plot by** (*Battery runs* or *Member runs*); which charts are
  shown, which series of the charts that draw more than one, *Show the full 0–100 Intelligence scale*,
  **Decimal places** per chart — *Automatic* (the precision above, named in the choice, such as
  *Automatic (2–4)* for cost) or 0–3, cost 0–4 — for the point labels, tooltips, takeaways and *Show
  data* on the Timeline and in its downloads, while the axis ticks keep the decimals their step needs and
  times under a second stay whole milliseconds (the Results step and the report charts keep the automatic
  precision), with *All automatic* to reset them, and **Mark runs not in the analysis** (on by default;
  off draws every run alike).
- **Events** — which markers the charts show (*Overseer changes*, *Annotations*, *Served-model
  changes*), which Overseer change kinds (each with the number of composite events holding it), and the
  **event list**: one section per UTC day, oldest first. A composite event shows its title (*Harness 26 →
  27*, *Changes under harness 27*), its runs and UTC times, the kinds that changed, each with its run
  count, and a *Details* disclosure with every field change, from → to; an annotation shows its text,
  scope and source; a served-model change shows the old and new model and the run. The filters hide
  items from the charts and the list together and never renumber them. A chart's *Show events* opens
  this tab.
- **Annotations** — dated notes on the timeline: *Model release*, *Provider statement*, *Provider
  confirmed a cause*, *Price change*, *Change on our side*, *Other*, for every provider, one provider or
  one model, with an optional http(s) source. Annotations are added and deleted; they are not edited.
- **Download** — *Chart size*, *Image format*, the theme, *As shown (dark)* or *Light, for print*, and
  **Show the GnollBench logo** (on by default), which turns the logo off on the charts and in every
  image.

The two views are **All charts**, every shown chart in one column, each tile with **Copy**, **Download**
and **Open in Single view** under its chart, and a toolbar with the zoom, *Fit width*, *Fit to screen*
and **Download all**; and **Single chart**, one chart with *Previous chart*, a chart select and *Next
chart*, the zoom, *Fit to screen*, *Actual size* (100 %), **Copy** and **Download**. Both views open at
**Fit to screen**: one whole chart, its *Show data* summary included, fits the view.

**Zoom resizes the live charts; it does not scale a picture.** Zooming in gives a chart more room — a
longer time axis and a taller value axis — at the same text size, and every run keeps its hover tooltip.
At 100 % a chart's box is the download's layout box for the chosen chart size, so the page and the file
agree there. The chart size's aspect ratio and text size therefore shape the charts on screen too; its
pixel density changes only the file. The zoom reaches from 25 % (lower where a fit needs it) to 400 %.
In a view, outside a form field and without Ctrl, ⌘ or Alt, `+` or `=` zooms in, `-` zooms out, `0`
fits one whole chart to the screen in both views and, in Single chart, `1` is 100 %.

**The image is the chart as shown** — its title, the model and the GnollBench logo in the header band,
without the takeaway or the marker list — in the chosen theme, with the series and markers the sidebar
shows, and the gray crosses while runs not in the analysis are marked. **Download** writes PNG or WebP (quality 75–100) at a size
preset, grouped by aspect ratio (16:9, 16:10, 4:3, 3:2, 1:1, 21:9 and print), or at a custom width and
height, with a pixel density and a text size: the controls of Model Comparison. A browser that cannot
encode WebP writes a PNG and says so. **Copy** always writes a PNG, the image type clipboards take.
**Download all** writes every shown chart — one ZIP when there is more than one — and names the charts
it skipped for having nothing to draw. The files are
`chat-consistency_<model key>_<chart>_<yyyyMMdd_HHmmss>.<png|webp>` and
`chat-consistency_<model key>_charts_<yyyyMMdd_HHmmss>.zip`. The workspace layout, the chart choices
(the decimal places, *Mark runs not in the analysis* and *Show the GnollBench logo* included) and the download settings are
kept per browser; a layout stored before the *Work per answer* chart was split shows both of its charts.

### 17.4 Repeat this run's setup

**"Repeat this run's setup"** — in a run card's *More actions*, on the next-run suggestions and in the
run report —
opens Run Benchmark with the run's suite, scoring profile, models and prompt options filled in, and
notes anything that no longer exists. **It never starts a run**: the operator checks the settings and
presses Start. From the wizard it switches sub-tab, so it closes the wizard first. While Chat
Consistency exports charts or attaches report charts, the run report refuses *Repeat this run's setup*,
*Re-run failed questions* and *Open run progress*, which would switch sub-tab too, and names the reason
in its status line; the report stays open.

## 18. Detection and Confirmation

Choosing the periods after looking at the timeline is detection: the analysis that found a change was
pointed at it. The **Confirm on later data** preset re-tests it on data that did not exist then — the
last saved analysis's baseline against the subject's runs from the day after that analysis was saved —
and only a change that holds there is confirmed.

**Leaving runs out after looking is detection too.** Step 1's first and last run and its *Include in the
analysis* checkboxes let the operator drop runs, and nothing stops dropping one *because* the timeline
shows it as an outlier. The analysis therefore records the choice instead of preventing it: every
analysis saved since code version 2 keeps its **run selection** (the step-1 dates, the marks and the
left-out runs) and every usable run of the subject inside the periods that it did not analyze, with the
first reason that applies, in this order — *left out in step 1*, *outside the step-1 dates*, *before
the first run*, *after the last run* (by start, then run id) or *not selected in step 4*. A run left out
and also outside the marks is recorded as left out. When any run is unanalyzed, the result carries:

- a data-quality note of kind `runSelection`: *"3 usable runs of the model inside the periods were not
  analyzed — left out in step 1: #45 (baseline), #51 (comparison); not selected in step 4: #60
  (comparison)."*, naming at most 20 runs and then *"and N more"*;
- the limitation *"The operator chose the runs: N usable runs of the model inside the periods were not
  analyzed (see the run selection). The verdicts hold for the analyzed runs; leaving runs out after
  looking at the timeline can bias them."*

**A compared set (code version 4)** adds a last reason, *outside the compared set*
(`outsideComparisonSet`): every usable run of the subject inside the periods that is not part of the
compared battery or suite. In a battery comparison the other reasons are decided by battery run, from
the battery selection (the first and last battery run and the left-out battery runs), and each member
run of an unanalyzed battery run is listed with its battery run (*#98 of battery run #12 (baseline)*).
Battery runs the analysis could not use are left out with an `excludedBatteryRun` note: *"Battery run
#N is incomplete (1 of 2 suites usable) and was left out."*, *"… belongs to another battery definition
and was left out."*, *"… measured another model axis and was left out."*, *"Battery run #N was not
found."*, and, since a run may serve several battery runs, *"Battery run #N shares run #M with battery
run #K and was left out."* In a suite comparison a run of another suite is left out with the
`excludedRun` note *"Run #N answered another suite and was left out."* An analysis with no compared set
whose runs span several suites carries a `mixedSuites` note naming them.

A first or last run of the selection that no longer exists is ignored, with the `runSelection` note
*"The first run of the selection, #21, was not found."* (for a battery run, *"The first battery run of
the selection, #N, was not found."*) The runs are classified only when the request
names its baseline and comparison runs, as the wizard always does; without them the server takes every
usable run in the periods, so none is unanalyzed. The selection never adds or removes a run from the
analysis. The Timeline marks the runs step 1 keeps out (§ 17.3), and the report documents state the
note and the limitation as they state every data-quality note and limitation.

## 19. API

`AdminChatConsistencyController`, route `api/admin/benchmark/chat-consistency`, policy `AdminOnly`.
Refusals are 400 with `{ error }`; JSON is camelCase with enums as strings, except the report-document
routes, which use the run report-documents contract.

| Method | Route | What it does |
|--------|-------|--------------|
| GET | `models` | every model axis with usable runs, run counts and first and last run dates |
| GET | `timeline?modelKey&from&to` | one point per usable run in the inclusive UTC range, with events and annotations; 400 without `modelKey` or when `from` > `to` |
| GET | `runs?modelKey&from&to` | the runs of step 1's run cards, validated alike; each with its suite id and key and its newest non-superseded battery membership (`batteryRunId`, `batteryName`, `batterySuitePosition`, `batterySuiteCount`) |
| GET | `battery-runs?modelKey&from&to` | the battery runs with a non-superseded member on the model axis in the range, newest first, each with its definition hash and revision, `setKey`, `complete` and `incompleteReason`, harness versions, usable members and per-axis eligibility; validated alike |
| GET | `comparison-sets?modelKey&from&to` | the batteries and suites the model can be compared within, batteries first, each group newest first (`kind`, `key`, `label`, `unitCount`, `memberRunCount`, `latestStartedAtUtc`), and `defaultKey`; validated alike |
| POST | `analyses` | runs and saves an analysis; 200 with the result and `analysisId`; 400 for malformed or overlapping periods, a period without a usable run, or a malformed or contradictory `runSelection` (below); 499 when the client aborts |
| GET | `analyses` | every saved analysis, newest first, without the results |
| GET | `analyses/{id}` | one saved analysis; 404 |
| DELETE | `analyses/{id}` | 204; 404; 409 while report documents written from it exist |
| POST | `analyses/{id}/report-documents/estimate` | the report cost estimate with `providerIssueReportAvailable` and `providerIssueReportReason`; no model call |
| POST | `analyses/{id}/report-documents` | writes the documents; 202 (§ 20) |
| GET | `analyses/{id}/report-documents/job` | the report job; 200, or 204 when this process knows none |
| POST | `analyses/{id}/report-documents/cancel` | cancels the report job; 202; 409 when none runs |
| POST | `regrade/estimate` | the re-grade estimate per run with eligibility; no model call; 400 without runs or over 200 |
| POST | `regrade` | starts a re-grade; 202 with the job; 400 without `confirmed: true`, for an invalid assessor or ineligible run, while a run or re-grade is in progress, or when the spending guard denies it |
| GET | `regrade/job` | the current or last re-grade job; 204 when none ran since start-up |
| POST | `regrade/cancel` | cancels the re-grade; 202; 409 when none runs |
| PUT | `runs/{id}/anchor` | `{ isAnchor }`; 200 `{ runId, isAnchor }`; 404 |
| GET | `annotations?provider&modelId` | annotations, oldest first, filtered when a provider is given |
| POST | `annotations` | adds an annotation; 400 for empty text or text over 1,000 characters, a provider over 64 or model id over 128 characters, an unknown kind, or a source that is not an absolute http(s) URL of at most 512 characters |
| DELETE | `annotations/{id}` | 204; 404 |

**The run selection.** The analysis request's optional `runSelection` records how step 1 chose the
runs; it is recorded, never used to pick runs. Its fields are `rangeLabel` (the dates as step 1 names
them, at most 64 characters), `rangeFromUtc` and `rangeToUtc` (the UTC bounds, null when open; taken as
UTC), `firstRunId`, `lastRunId` and `leftOutRunIds` (at most 5,000). The wizard always sends it, the
default selection included. It is refused with 400 and:

- *"The run selection's date label is at most 64 characters."*
- *"The run selection's dates end before they start."*
- *"The run selection leaves out at most 5,000 runs."*
- *"Run #45 is left out in step 1 but selected for the baseline."* (or *comparison*), for a left-out run
  that is also in `baselineRunIds` or `comparisonRunIds`.

The result's `runSelection` carries `recorded` (false for an analysis saved before code version 2,
whose JSON has none), the same fields with the left-out ids distinct and ascending, and
`unanalyzedRuns`: `{ runId, period, startedAtUtc, reason }`, ordered by start, then id, with `period`
`baseline` or `comparison` and `reason` one of `leftOut`, `outsideDateRange`, `beforeFirstRun`,
`afterLastRun`, `notSelected` and `outsideComparisonSet` (`ChatConsistencyUnanalyzedReasons`, § 18),
plus `batteryRunId` in a battery comparison.

**The compared set.** The request's optional `comparisonSet` (`{ kind, key }`, kind `battery` with key
`battery:<definition SHA-256>` or kind `suite` with key `suite:<suite identity>`) names the battery or
suite the analysis compares within; without it the analysis takes the runs one by one, as code
version 3 did. With a battery set the request names `baselineBatteryRunIds` and
`comparisonBatteryRunIds` instead of run ids, and `runSelection` carries `firstBatteryRunId`,
`lastBatteryRunId` and `leftOutBatteryRunIds` (at most 5,000). Refused with 400 and:

- *"The comparison set's kind must be "battery" or "suite"."*, or a key that does not start with its
  kind's prefix;
- *"A battery comparison takes battery run ids."* (run ids with a battery set);
- *"Battery run ids need a battery comparison set."*;
- *"A battery run cannot be in both periods."*;
- *"The run selection names battery runs, which only a battery comparison set takes."*;
- *"Battery run #12 is left out in step 1 but selected for the baseline."* (or *comparison*).

The result carries `comparisonSet` (`kind`, `key`, `label`), `unitKind` and `units`, and `GET analyses`
each summary's `comparisonSetKey` and `comparisonSetLabel` (null for a run-by-run analysis and before
code version 4). The report documents state the set as the *Compared* fact (*"Battery Two initial
suites (revision 1), 4 battery runs"*, *"Suite <name>, N runs"*).

The client calls them through `AdminChatConsistencyService`
(`Overseer/ClientApp/src/app/services/admin-chat-consistency.service.ts`).

## 20. Chat Consistency Report Documents

A saved analysis can be written up as AI-written report documents, through the report-pack machinery
(origin `ChatConsistencyReport`, scope `ChatConsistency`, subject key `chat-consistency:<id>`, column
`ChatConsistencyAnalysisId`). The details — slots, facts, validator rules, figures and file names — are
in `ai-benchmark-report-pack.md` § 16.

| Audience | For |
|----------|-----|
| Executive Summary | is the chat with this model as good as before, for players, with confidence and scope |
| Report for AI Researchers and Developers | design, coverage, events, results, attribution, robustness, limitations, reproducibility |
| Internal Improvement Brief | findings for the chat, our changes that helped or hurt, infrastructure, next runs, actions |
| Provider Issue Report | for the model's provider: the finding, its measurements, the hours observed, what we ruled out, sample request ids and the request |

The **Provider Issue Report** is available only when at least one attribution is **provider-side and
graded Established or Indicated**; otherwise it is refused with the reason *"No provider-side finding
graded Established or Indicated in this analysis."* It never names a control model — controls are
lettered peers — and carries at most 10 candidate-call request ids from the comparison period.

The validator's chat consistency rules **C1–C7** (report-pack rules 22–28) hold the prose to the
method: a change claim needs the fact that shows it, an intent or mechanism claim needs a
provider-confirmed cause, a causal claim needs an attribution, *established* needs an Established
grade, an inconclusive endpoint needs its detectable effect, the document must cite its hours and may
not claim all hours, and a Provider Issue Report may assert a model or serving change only from a
provider-side attribution and must list every Overseer event it ruled out. Files are named
`chat-consistency-<id>_<model>_<kind>_<disclosure>_<peers>`, for example
`chat-consistency-12_<model>_provider-issue-report_summary_anonymized.pdf`.

## 21. Limits

The analysis records these in every result:

- Runs sample the chat only at the hours they ran; every verdict holds within the stated scope.
- Attribution names a side, never a mechanism or an intent.
- Graders never see the candidate's tool results; unless a common grader covers every run, a quality
  change can reflect grading as well as answers.
- Control runs are not segmented for measurement changes; a DiD assumes each control was measured alike
  in both periods.
- Speed endpoints pair items by centering each answer on its item's mean; that removes item levels but
  not a change in which items each period sampled at which hour.
- Cost is computed at one price card, so a price change does not register as a cost change.
- On the legacy proxy, P2 spans the provider's whole turn, not the wait for the first answer text.
- When usable runs of the model inside the periods were not analyzed, the operator chose the runs:
  the verdicts hold for the analyzed runs, and leaving runs out after looking at the timeline can bias
  them (§ 18).

And of the implementation:

- Change-point detection (`ChatConsistencyStatistics.Pelt`) is implemented but not yet wired into the
  analysis or the timeline; detection is the operator's choice of periods, and confirmation is the
  *Confirm on later data* preset (§ 18).
- The analysis passes no rescored-run set to the comparability layer, so a scoring-profile or speed
  calibration change between the periods always segments.
- Calibrations, and so the common-grader re-grade, write no call telemetry.
- GnollBench is not a monitoring service: a verdict is only as current as the last run someone made.

## 22. How to Cite a Finding

Cite a finding with **its hour scope, its grade and its protocol version**, and its analysis id:

> Overseer chat with \<model\>: time to first answer text degraded by 22 % (95 % CI 14 % to 31 %),
> **Indicated**, within weekdays 08–16 UTC; Protocol V1; analysis #12.

Never drop the scope (*"slower"* without *"within the sampled hours"*), never upgrade an Indicated
grade to *established*, never cite an Inconclusive endpoint as *no change* — cite its minimum
detectable effect instead — and never state a cause the attribution table did not name.
