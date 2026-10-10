# GnollBench Model Batches — Several Models Under One Settings Set

A **model batch** runs several models under test **one after another under one start request**: the
same suite or battery, the same graders, scoring profile, prompt options and report writer, and the
same Overseer build. It is the third way to start GnollBench runs, beside a single run (or a replicate
series) and a battery run, and it exists for one purpose: **comparing candidate models soundly**, where
a comparison is sound only when everything but the candidate is identical
(`server_benchmark_runbook` § 4 *Rules for model-selection runs*).

The batch removes most sources of error **by construction** — one stored settings template, one
target, one build — and a set of **guardrails** covers what construction cannot: the server evaluates
every rule in one place and the launcher shows the result as blockers, warnings to acknowledge and
advice before Start, and run-time guards stop the batch when the instrument moves under it.

This document assumes [`ai-benchmark.md`](ai-benchmark.md) (single runs and the grader roster),
[`ai-benchmark-multi-run.md`](ai-benchmark-multi-run.md) (series and run groups) and
[`ai-benchmark-multi-suite.md`](ai-benchmark-multi-suite.md) (batteries): every member of a batch is one
of those, unchanged.

Implementation:

| Concern | File |
|---|---|
| Settings (`Benchmark:ModelBatch`) | `Overseer/Services/Benchmarking/BenchmarkModelBatchOptions.cs`, `Overseer/appsettings.json` |
| The guardrail rules and the context they read | `Overseer/Services/Benchmarking/BenchmarkModelBatchGuardrails.cs` (`BenchmarkModelBatchGuardrails`, `BenchmarkModelBatchGuardrailService`) |
| Planned runs, projected cost and wall time, the whole-batch run-limit calculation | `Overseer/Services/Benchmarking/BenchmarkModelBatchProjection.cs` |
| Start, the drive loop, the run-time guards, cancel, skip, resume, restart reconciliation | `Overseer/Services/Benchmarking/BenchmarkModelBatchOrchestrator.cs` |
| The batch claim on the run gate | `Overseer/Services/Benchmarking/BenchmarkRunManager.cs` (`ModelBatchOwner`, `TryClaimBatch`, `ReleaseBatch`, `ClaimHolder`) |
| The plain-text diagnostics | `Overseer/Services/Benchmarking/BenchmarkModelBatchDiagnostics.cs` |
| The API and each member's result | `Overseer/Controllers/AdminBenchmarkModelBatchesController.cs` |
| Requests and DTOs | `Overseer/Models/BenchmarkModelBatchModels.cs` |
| Data model and migration | `GnollHackServer.Data/BenchmarkModelBatchRun.cs`, migration `AddModelBatches` |
| Client: launcher batch mode and *Batch Projection* | `admin/benchmark/run-tab/`, `state/benchmark-launcher.state.ts` |
| Client: the Batch Readiness card | `admin/benchmark/run-tab/model-batch-readiness/` |
| Client: the progress dialog and its pure helpers | `admin/benchmark/model-batch/` |
| Client: the banner, polling and the end signals | `state/benchmark-active-run.monitor.ts`, `state/benchmark-end-signal.ts`, `services/benchmark-completion-sound.service.ts` |

(Client paths are under `Overseer/ClientApp/src/app/`.)

---

## 1. What a Batch Runs

A batch has a **target** and a list of **models under test** (2 to `MaxModels`), and it runs one
**member** per model, strictly one at a time, in the batch's run order:

| Target | Runs per model (R) | Each member is | Planned launches per member |
|---|---|---|---|
| One suite | 1 | a single run | 1 |
| One suite | 2 or more | a replicate series of R runs (`BenchmarkSeriesOrchestrator`), which creates its run group | R |
| A battery of K suites | *Runs per Suite* R | a battery run (`BenchmarkBatteryOrchestrator`), with its analysis and, with a writer, its documents | K × R |

The members are launched from **one stored template**, `StartRequestJson`: the ordinary
`StartBenchmarkRunRequest` the launcher builds, stored with its tested configuration id 0, `RunCount` 1,
the batch's *Wait when the run cap blocks the next run*, both same-provider acknowledgments false and
`AllowSourceCodeReferences` defaulting to false. A member's request (`MemberRequest`) differs from it
only in the tested configuration, the suite (a suite target), the run count (R on a suite) and the two
same-provider acknowledgments, which come from the warnings acknowledged at start (§ 3.2, MB-W10 and
MB-W08).

**Run order.** *Randomized* (the default) shuffles the models with a Fisher–Yates shuffle
(`BenchmarkModelBatchOrchestrator.Shuffle`) under a new seed drawn at start and stored as `OrderSeed`,
so the order is reproducible from the record; *As listed* runs them in the order the launcher's
reorderable list sets. The order is fixed at start, and the members are stored in it (`OrderIndex`).

**What a batch does not do.** Members never run concurrently: the server admits one benchmark run at a
time, and concurrent runs would share a provider's load and corrupt each other's speed figures. A batch
never reuses earlier runs (the battery launcher's *Reuse earlier runs* is not offered in batch mode). It
does not interleave replicate rounds across models (A, B, C, then A, B, C again): a battery run or a
series cannot be suspended between its members, so each model's member runs to its end before the next
model starts. Randomized order and the per-member start times and time-of-day strata in the diagnostics
cover the reporting side.

## 2. Settings

`Benchmark:ModelBatch` in `Overseer/appsettings.json`, read live (`BenchmarkModelBatchOptions.From`); the
code fallbacks equal the shipped values, and a User Secrets key overrides both.

| Key | Default | Used by |
|---|---|---|
| `MaxModels` | 12 | MB-B02; the launcher's picker maximum (`GET runs/limits` → `maxModelsPerBatch`, and the preflight's `maxModels`) |
| `WarnCostUsd` | 40 | MB-W09: projected cost above it |
| `WarnWallHours` | 12 | MB-W09: projected wall time above it |
| `LongWallHours` | 4 | MB-T02: projected wall time above it |
| `StallMinutes` | 15 | MB-R5: minutes without progress before the dialog flags a stall |

**Why 12 models.** Nothing else in the system caps lower. *Open in Model Comparison* takes up to
`MAX_COMPARISON_SOURCES` = 24 sources and a batch adds one per model; the daily cap of 120 launches holds
12 models on a two-suite battery at up to 5 runs per suite (12 × 2 × 5 = 120), or on a single suite at up
to 10 runs per model. What grows with the model count is wall time and spend, which MB-W09 and MB-T02
state before Start; the multi-model picker shows all 12 cards and the progress dialog's member list
scrolls.

## 3. The Guardrails

**Held fixed by construction** — no finding is needed:

| Code | What | How |
|---|---|---|
| MB-S1 | One grader roster, scoring profile, response style, source-references option and report writer for every member | One stored template; members differ only in the tested configuration |
| MB-S2 | One suite (its item revisions and assessed difficulties) or one battery revision | Fixed at start; the run-time guard compares the battery's definition hash and the members' exam keys (§ 5.2) |
| MB-S3 | One build | The run-time guard MB-R1 (§ 5.2) |
| MB-S4 | One run at a time | `BenchmarkRunManager` admits one run, and the batch claim keeps every other launch out (§ 5.1) |

**The findings.** `BenchmarkModelBatchGuardrails.Evaluate` is a pure function over a
`BenchmarkModelBatchGuardContext` that `BenchmarkModelBatchGuardrailService.BuildContextAsync` loads from
the database and the running services: the candidates and graders, the target and what keeps it from
running, the launcher's own refusal of each candidate (`BenchmarkRunLauncher.ValidateRequestAsync` on a
probe request with the same-provider acknowledgments set, asked only once the target itself can run),
the projection and run limits, the `RecommendedModels` entries, the price cards, the scoring profile (the
chosen one, else the default), and the graders and start times of the target's last 500 completed runs.

Each finding (`BenchmarkModelBatchFindingDto`) has a stable `code` (`MB-W01`), a `name`
(`MixedFamiliesSingleAssessor`), a `severity` (`Blocker`, `Warning` or `Advice`), the launcher `field` it
is about (`models`, `assessor`, `coAssessor`, `reader`, `verifier`, `reportWriter`, `profile`,
`responseStyle`, `sourceReferences`, `runsPerModel`, `order`, `capWait`, `target`, or none), a `title`
clipped to 60 characters, a one-sentence `detail` clipped to 140 characters (except where a launcher or
spend-guard refusal is quoted whole), the affected `modelConfigurationIds` (ascending), and for a warning
an `acknowledgmentKey`. The list comes back blockers first, then warnings, then advice, each group in
evaluation order. Model names are the configurations' display names (else their model ids); no family or
version is written into a rule's text.

**Severity decides behavior.**

- **Blocker** — *Start Model Batch* is `aria-disabled` with the reason shown; a start the server
  refuses for it is **400** with the blockers, except MB-B09, which is **409**.
- **Warning** — the batch starts only after the operator checks *I understand* on that line. The
  acknowledged keys travel with the start request (`acknowledgedFindingKeys`); a start with a warning
  whose key is missing is **409** listing the warnings still to acknowledge. Every warning of the start is
  stored on the batch (`AcknowledgedFindingsJson`) and printed in its diagnostics, so a later reader sees
  what was knowingly accepted.
- **Advice** — shown collapsed under *N tips*; never blocks and needs no acknowledgment. The advice at
  start is stored too (`AdviceAtStartJson`).

**The acknowledgment key** is the code, an optional discriminator and the sorted affected configuration
ids: `MB-W03`, `MB-W10:7`, `MB-W05/ServiceTier:3,7` (`BenchmarkModelBatchGuardrails.AcknowledgmentKey`).
The discriminator separates the findings of one code that differ in kind — MB-W05 has one per differing
attribute (`ParallelMode`, `ServiceTier`, `Endpoint`, `MaxOutputTokens`). Because the key carries the
affected models, an acknowledgment stops matching the moment the condition changes (another set of
affected models), and the launcher drops it (`applyFindings`), so an old check never covers a new problem.

### 3.1 Blockers

| Code | Condition | Title / sentence |
|---|---|---|
| MB-B01 TooFewModels | Fewer than 2 distinct models | *Choose at least two models* / *A batch compares models; for one model, choose One model.* |
| MB-B02 TooManyModels | More than `MaxModels` | *At most {n} models in one batch* / *Split the comparison into two batches under the same graders.* |
| MB-B03 GraderIsCandidate | The assessor or the co-assessor is the same model as a candidate (`BenchmarkComplianceGuard.IsSameModel`); one finding per candidate and role | *{model} cannot grade itself* / *Choose another {assessor \| co-assessor}: a scoring grader must not be a model under test.* |
| MB-B04 ReportWriterIsCandidate | The report writer is the same model as a candidate | *{model} cannot write its own report* / *Choose another report writer, or None.* |
| MB-B05 PanelInvalid | The co-assessor is the assessor's configuration, or shares its provider | *The co-assessor is the assessor* / *The co-assessor must be a different configuration from the assessor.*; or *The panel needs two providers* / *A panel needs members from two providers. The assessor and the co-assessor both belong to {provider}.* |
| MB-B06 MemberRefused | A requested configuration no longer exists, or the launcher refuses the request for a candidate (endpoint policy, unusable configuration …). A candidate already refused by MB-B03, MB-B04 or an invalid panel is not refused twice; a spend-guard refusal is MB-B08's | *{model} is refused* / *{model}: {the launcher's refusal}* |
| MB-B07 TargetNotReady | No suite chosen; the suite or battery is missing, archived or invalid; a suite has no questions or questions without an assessed difficulty | *{target} is not ready* / the launcher's or battery start's own words |
| MB-B08 CapExceeded | One of the run-limit blockers of § 4: the planned launches above the daily cap without *Wait when the run cap blocks the next run*; one member's plan above its own launcher's limit; the spend guard refusing the first launch now | *{L} runs exceed the daily cap of {m}* / *Check Wait when the run cap blocks the next run, or choose fewer models.* — the member-plan variant names `Benchmark:Battery:MaxMembers` (battery) or the daily cap (series), the spend variant reads *The run cap refuses the first launch* or *The spend guard refuses the first launch* with the guard's reason |
| MB-B09 RunActive | A run, a claim, a series, a battery run or another batch is active | *A benchmark is already running* / *Wait for it to finish or cancel it.* |

### 3.2 Warnings (acknowledge to start)

| Code | Condition | Title / sentence |
|---|---|---|
| MB-W01 MixedFamiliesSingleAssessor | Candidates from two or more providers and no co-assessor | *Use a two-family panel for this batch* / *A single assessor favors its own provider's models over the others.* |
| MB-W02 AnchorIsCandidate | The second or reference reader, or the claim verifier, is the same model as a candidate | *{model} checks its own answers* / *Its {reader / claim verifier} findings on its own run are not independent.* |
| MB-W03 NoClaimVerifier | No claim verifier | *No claim verifier* / *Refuted claims go unchecked, and a panel split on a critical error stays unresolved.* |
| MB-W04 GraderEffortTooHigh | A grader role at `xhigh` or `max`; one finding per configuration, naming every role it holds | *{Role} at {level} effort* / *Very high effort slows graders and breaks their JSON; medium is recommended.* |
| MB-W05 CandidateSettingsDiffer | Candidates differ in parallel mode, service tier, endpoint (official or a custom endpoint's fingerprint) or max output tokens; one finding per attribute, naming the models outside the largest group (every model when no two agree) | *Models differ in more than the model* / *{Attribute} differs: {models}; the comparison mixes it with the model.* |
| MB-W06 DuplicateConfiguration | Two candidates match on provider, model id, thinking level, service tier, parallel mode and endpoint | *{model} is selected twice* / *For repeats, use Runs per model instead.* |
| MB-W07 DetailedStyle | Response style *Detailed* | *Detailed style is not the production chat* / *Results will not compare with Concise runs of these models.* |
| MB-W08 ReportWriterSharesFamily | The report writer shares a provider with one or more candidates (the per-run same-provider writer warning, asked once for the batch) | *{writer} reports on its own provider* / *{models}' documents come from a same-provider writer.* |
| MB-W09 LargeBatch | Projected cost above `WarnCostUsd` or projected wall time above `WarnWallHours` | *Large batch: about US${cost}, {hours} h* / *Check the projection before starting.* |
| MB-W10 SameProviderAssessor | A single assessor shares a provider with one or more candidates (the per-run same-provider assessor gate, asked once for the batch) | *{assessor} grades its own provider's models* / *{models} are graded by a same-provider assessor.* |
| MB-W11 ExceedsDailyHeadroom | Planned launches above the rolling 24-hour window's remaining headroom, but not above the daily cap | *{L} runs, {h} left in the 24-hour window* / *With Wait checked it pauses at the cap; without, it stops there.* |
| MB-W12 HourlyCapRisk | The projected launch rate above the hourly headroom, and more planned launches than that headroom | *Runs may outpace the hourly cap of {m}* / *Short runs launch faster than {m} per hour allows; the batch will pause.* |

MB-W10's acknowledgment is sent to each affected member as `acknowledgeSameProvider`, and MB-W08's as
`acknowledgeSameProviderReportWriter`, so the members' launchers do not ask again.

### 3.3 Advice

| Code | Condition | Title / sentence |
|---|---|---|
| MB-A01 SingleRunPerModel | Runs per model (suite) or runs per suite (battery) is 1 | *One run per model* / *Differences under about 2 index points are noise; use 2–3 runs to rank.* |
| MB-A02 UniformFamilyBias | A single assessor shares a provider with every candidate | *All models share the assessor's provider* / *The ranking is fair, but absolute scores may run high.* |
| MB-A03 AnchorSharesFamily | The reader or the verifier shares a provider with a candidate (other than itself) or a panel member | *{Role} shares {provider} with {names}* / *A third family keeps the reference checks neutral.* |
| MB-A04 NoReferenceReader | No second or reference reader | *No reference reader* / *Model Comparison cannot estimate the panel's family bias without one.* |
| MB-A05 CoAssessorNotPeer | The co-assessor's thinking level differs from the assessor's | *Panel members at different effort* / *A panel assumes peers; use the same effort for both.* |
| MB-A06 SourceReferencesAllowed | Source Code References *Allowed* | *Not the production default* / *Players get Disallowed; results will differ from what they see.* |
| MB-A07 PricingIncomplete | A candidate's price card does not resolve | *No price for {model}* / *Its cost per question will be missing from the comparison.* |
| MB-A08 ProfileFit | A candidate at `high` or `max` and a scoring-profile speed target under 30 s | *This profile targets interactive latency* / *{model} at {level} will score low on Speed Index; read it as advisory.* |
| MB-A09 NoBaseline | No candidate is a `RecommendedModels` entry (model and, where the entry names one, thinking level) of a provider present in the batch | *No current production model in the batch* / *Add {recommended} as the baseline the others are judged against.* |
| MB-A10 RosterDiffersFromRecent | The assessor, co-assessor, reader and verifier differ from those of the most recent completed run on the target | *Graders differ from recent runs* / *This batch will not compare with earlier runs of these models.* |
| MB-A11 ReportWriterInBatch | A report writer is set | *{n} AI documents will be written* / *For one document on the whole batch, use Model Comparison after it.* |
| MB-A12 AnchoredSecondReader | A single assessor and a second reader the scoring profile does not blind | *Second reader sees the first verdict* / *A blind reader gives an independent check.* |

MB-A11's count is the number of AI documents one report writes (`BenchmarkRunReportDocumentService.Audiences`)
per member run on a suite target (models × R of them), and per battery run on a battery target (models of them).

### 3.4 Time and Order

| Code | Severity | Condition | Title / sentence |
|---|---|---|---|
| MB-T01 OrderAsListed | Advice | *Model order* is *As listed* | *Models run in a fixed order* / *Randomize so no model always gets the same hours.* |
| MB-T02 LongWallTime | Advice | Projected wall time above `LongWallHours` | *Models run hours apart* / *Their speed figures include time-of-day effects.* |
| MB-T03 StratumDiffersFromRecent | Advice | A candidate's most recent completed run on the target started in another 4-hour UTC block, weekday or weekend, than now | *{model} last ran on a weekday, 12–16 UTC* (or *weekend*, and its block) / *Starting now puts it in another time block for Chat Consistency speed.* |
| MB-T04 CorpusQuiet | Shown in the progress dialog, always | — | *Do not push the wiki, source or knowledge base until the batch ends; a change stops it.* |

### 3.5 Why These Rules

- **The grader effect is larger than the candidates.** Runs 43–46 scored 70 / 68 / 72 under one
  assessor and 97 under another on the same suite, revisions and prompt, so every member is graded by the
  same roster (MB-S1), a candidate never grades itself (MB-B03) and the grader effort stays moderate
  (MB-W04, after runs 32 and 52).
- **Judges prefer their own family.** No single provider is a neutral judge for candidates of several
  providers, so a mixed batch with one assessor is a warning (MB-W01) and the two-family panel is the
  answer (`ai-benchmark.md` § *The Two-Family Assessor Panel*). A single assessor over one provider's
  models is legitimate, so MB-W01 is a warning, not a blocker, and MB-A02 says the ranking is fair.
- **One run is one sample.** The R = 3 replicate sets measured a reproducibility SD of 0.30 and 3.34 index
  points, so a single-run difference of a point or two is noise (MB-A01).
- **Speed depends on the hour.** Battery run 7 measured a TTFT P50 of 5.3 s against 1.8 s on identical
  work, so randomized order (MB-T01), long wall times (MB-T02) and strata (MB-T03) are called out.
- **The instrument must not move mid-set.** A harness bump, a tool-guide edit, a corpus push or a grader
  configuration edit between two members makes them a different measurement (MB-R1, MB-R2).

## 4. Projection and Run Limits

`BenchmarkModelBatchProjection.Compute` makes, before Start and over **every member**, the calculation the
battery launcher makes for one battery run:

| Quantity | Formula | Used by |
|---|---|---|
| Planned launches *L* | models × R (suite), or models × K × R (battery) | every row below |
| Member plan | R per series, K × R per battery run | MB-B08 when above the member's own limit: `Benchmark:Battery:MaxMembers` (battery), the daily cap (series) |
| Daily cap | *L* > `MaxRunsPerDay` | MB-B08 without *Wait when the run cap blocks the next run*. With it, the projection says the batch spans at least ⌈*L* / `MaxRunsPerDay`⌉ rolling 24-hour windows (`daySpan`) and lasts at least (that number − 1) × 24 h (`minimumWallMs`) |
| Daily headroom | `MaxRunsPerDay` ≥ *L* > `RemainingDailyHeadroom` | MB-W11 |
| Hourly rate | 60 min ÷ the shortest recent mean run duration among the target's suites (`projectedRunsPerHour`), against `MaxRunsPerHour` − `RunsInLastHour` | MB-W12, when the rate is above that headroom and *L* exceeds it |
| Spend guard now | `BenchmarkComplianceGuard.CheckSpendAsync` | MB-B08 when it refuses and either *Wait* is unchecked or the refusal is not a run cap |
| Cap-wait budget | each cap wait is bounded by `CapWaitBudget` (26 h), as for series and batteries | a wait that exhausts it stops the batch (`RunCapReached`) |

The caps come from `BenchmarkComplianceGuard.GetLimitsAsync` (`MaxRunsPerDay`, default 120;
`MaxRunsPerHour`, default 30; `RunsInLast24Hours`, `RunsInLastHour`, `RemainingDailyHeadroom`) and
`MaxBatteryMembers` (default 120).

**Cost and wall time.** The basis is the battery launcher's: the mean of a suite's last 5 completed runs
(`BasisRunCount`), wall time being a run's total duration, else its summed answer time, and cost its
estimate on its own pricing snapshot. A model with completed runs of its own on a suite is projected from
its own mean wall time, and its own mean candidate cost replaces the candidate share of the suite mean
(the graders cost what they cost on that suite); a model without is projected from the suite mean. Each
member line names its basis: `OwnRuns`, `Mixed` (a battery whose suites are partly the model's own),
`TargetMean`, or `None` when a suite has no completed run, in which case that member's cost and wall time
are null — and so are the batch totals, which are summed only when every member has one. Each member's
figures are its suite sums × R.

## 5. Execution

### 5.1 Start and the batch claim

`POST runs` (`BenchmarkModelBatchOrchestrator.StartAsync`), serialized with resume behind one gate:

1. The guardrails are evaluated again on the server. Blockers refuse the start: **409** when MB-B09 is
   among them, **400** otherwise, each with the blockers. A warning whose key was not acknowledged refuses
   it with **409** and the warnings to acknowledge.
2. The run order is drawn (§ 1), the target is read (**404** for an unknown suite or battery), and the
   batch row is written with its members in run order, each with a snapshot of its configuration
   (`TestedModelSnapshotJson`: display name, provider, model id, thinking level, reasoning mode, service
   tier, parallel mode, endpoint, max output tokens), the battery's revision and definition hash, the
   template, the warnings and the advice. The status is `Pending`.
3. Only then is the **batch claim** taken, under the owner token `modelbatch:<id>`; a claim lost to a
   race (a run, an orchestrator claim or another batch) deletes the row again and refuses with **409**.
4. The drive loop starts, and the response is **201** with the batch.

The batch claim is a third owner on `BenchmarkRunManager`, **above** the series and battery orchestrator
claim. While it is held, only launches and orchestrator claims made for that batch are admitted: the
launcher (`CreateAndLaunchRunAsync`), `TryStart`, `TryClaimOrchestrator` and the series and battery starts
and resumes take a `batchOwner` and refuse every other caller with **409**, *"A model batch is running;
wait for it or cancel it."* (`ClaimConflictMessage`). The series and battery orchestrators remember the
batch owner of a child they drive and launch its member runs under it. Refusals that name a claim read
`ClaimHolder`, the batch claim when one is held, else the orchestrator claim — so a single run, the
in-place re-run endpoints and a common-grader re-grade are refused while a batch runs.

**The claim is released whenever the drive loop ends** — the batch completes, stops, fails or is
canceled — in the drive task's `finally`, and by Cancel even when canceling the child throws. A
**stopped batch holds no claim**, so repairs such as *Re-run failed questions* work on its members' runs;
but its members belong to it until it is final: a manual resume of a series or battery run that is a
member of a batch not yet `Completed`, `CompletedWithErrors`, `Cancelled` or `Failed` is refused with
**409**, *"Part of model batch #N; continue it from the batch's progress dialog."*
(`AdminBenchmarkBatteriesController.ModelBatchOwnershipRefusalAsync`, used by both resume endpoints).

### 5.2 The drive loop and the run-time guards

The loop takes the first member that is `Running`, else the first `Pending` one. For a pending member it
runs, in order:

1. **MB-R1 InstrumentChanged** (unless the operator has accepted a change, below). For a battery target
   the battery's current definition hash is compared with the one recorded at start. Once the first
   member's instrument is recorded — the five instrument hashes, `HarnessVersion` and
   `ScoringMethodVersion` of the earliest run any member produced — the running build's harness and
   scoring method are compared with it, and so are the five hashes a run would carry now
   (`ComputeCurrentInstrumentFingerprintAsync`, for the first member's configuration on the target's first
   suite, so a prompt that differs only by a candidate's parallel mode is not a change; when that
   configuration is gone the next pending member's is used and the prompt hash is not compared). Any
   difference stops the batch with `InstrumentChanged`, naming the keys.
2. **MB-R2 GraderConfigChanged, and the exam keys.** Over the members finished so far, each member's
   first completed run per suite is compared with the first finished member's on the same suite through
   `BenchmarkComparabilityKey`: the exam keys (item revisions, assessed difficulties, scoring profile) stop
   the batch with `InstrumentChanged`, the grader keys (assessor, second-opinion and claim-verifier
   configurations) with `GraderConfigChanged`. An edit of a grader's System AI Config mid-batch is caught
   here.
3. **The run cap.** `CheckSpendAsync`: a refusal that is not a run cap stops the batch with
   `SpendDenied`; a run-cap refusal stops it with `RunCapReached` unless *Wait when the run cap blocks the
   next run* is set, in which case the batch waits in `WaitingForCap`, re-checking every 2 minutes, for
   at most `CapWaitBudget` (26 h), then stops with `RunCapReached`.
4. **The launch** — a single run, a series or a battery run, under the batch claim. A launch the cap or
   the spend guard refuses leaves the member `Pending` and stops the batch with `RunCapReached` or
   `SpendDenied`; any other refusal marks the member `Failed` and stops the batch with `MemberStopped`.

The member is then polled every 5 seconds until its child is terminal; each advance of the child (its
status, completed count, last progress or answered questions) moves the batch's `LastProgressAtUtc`. A
battery member is terminal only once its post-run work is done — not driven, not analyzing, its documents
neither `Pending` nor `Writing` — so the next model never starts while a battery run is still analyzed or
documented.

**MB-R3 MemberStopped.** A member that ends `Completed` (a run's `Completed` or `CompletedWithLimits`) or
`CompletedWithErrors` lets the batch go on. Any other end — failed, canceled, or a series or battery run
that stopped — stops the batch: with `RunCapReached` or `SpendDenied` when the child stopped for that
reason, otherwise `MemberStopped`, naming the model and its position (*"\<model\> (2 of 4) ended
Stopped: …"*). The remaining members are not launched into whatever condition caused it.

When no member is pending the batch ends **`Completed`** when every member is `Completed`, otherwise
**`CompletedWithErrors`**, its stop detail listing the members without a clean result (*Without a clean
result: \<model\> (Skipped); …*). A member that ended `CompletedWithErrors` or was skipped therefore makes
the batch `CompletedWithErrors`. An unhandled error marks the batch `Failed`.

The batch's status vocabulary is the series' (`Pending`, `Running`, `WaitingForCap`, `Stopped`,
`Completed`, `CompletedWithErrors`, `Cancelled`, `Failed`); a member's is `Pending`, `Running`,
`Completed`, `CompletedWithErrors`, `Stopped`, `Failed`, `Skipped` or `Canceled`. The counts on the batch
are recomputed from the member rows, never incremented: *completed* counts `Completed` and
`CompletedWithErrors`, *failed* counts `Failed` and `Stopped`.

### 5.3 Stop reasons and resuming

| Stop reason | Words (`StopReasonText`) | Resume options, in this order |
|---|---|---|
| `InstrumentChanged`, `GraderConfigChanged` | *The instrument changed*, *A grader's configuration changed* | **Re-run under current instrument**, **Continue — accept the change** |
| `RunCapReached`, `SpendDenied` | *Run cap reached*, *Spend guard denied the next run* | **Continue** |
| `MemberStopped`, `RestartReconciled` | *A member did not finish*, *The service restarted* | **Continue** and **Skip this model** while a member is left unfinished; **Continue** alone when the stop came between members |

A batch offers resume options only while it is `Stopped` and not driven (`ResumeOptionsFor`); `POST
runs/{id}/resume` refuses any other mode with **400**, and refuses with **409** while this or another
batch is driven, a run is in flight or another claim is held.

- **Continue** first re-checks MB-R1 and MB-R2 (unless a change was accepted). When the instrument moved,
  the batch stays `Stopped` with the new stop reason and the resume is refused with **409** `{
  instrumentChanged: true, batchId, changedKeys, message }`, which offers the two instrument options.
  Otherwise the member the stop left unfinished is taken up again: a child still live is awaited, a
  stopped series or battery run its own Continue would resume is resumed under the batch claim (a series
  with the batch's instrument-change acknowledgment), and anything else — a failed single run, a
  canceled member — is launched afresh, its earlier run, series or battery run recorded as superseded.
  After a cap or spend stop the next member is launched once the spend guard allows it.
- **Skip this model** marks the unfinished member `Skipped` and goes on with the next.
- **Continue — accept the change** records `InstrumentChangeAcknowledged`, skips MB-R1 and MB-R2 from then
  on, and goes on with the remaining members. The batch is then **not comparable across the change**: the
  dialog tags it so and the diagnostics say so.
- **Re-run under current instrument** re-runs every member at full cost: each member's links are appended
  to `SupersededMembersJson` (member, order, configuration, status, run, series and battery run ids, time),
  every member is reset to `Pending`, the recorded first-member instrument is cleared and the acceptance
  dropped. The earlier runs stay in Run History but leave the batch. The dialog asks for confirmation
  first, stating the number of completed models and their last total cost.

**Skip a pending model.** `POST runs/{id}/members/{memberId}/skip` marks a `Pending` member `Skipped`
while the batch is live or stopped; it never runs. **400** for a member in any other state or a final
batch.

**Cancel** (`POST runs/{id}/cancel`) stops the drive loop, cancels the member's run, series or battery run,
marks every running and pending member `Canceled` and the batch `Cancelled`, which is terminal; completed
members keep their results. **400** for a final batch.

**MB-R4 RestartReconciled.** At startup, after the series and battery reconciliations, every batch left
`Running`, `Pending` or `WaitingForCap` is set `Stopped` with `RestartReconciled` and its running member
`Stopped` (*"The Overseer service restarted while this member was running."*); finished members are
intact, and **Continue** re-checks the instrument and the graders, then resumes the stopped member.

**Delete** (`DELETE runs/{id}`) removes a final batch's record (`Completed`, `CompletedWithErrors`,
`Cancelled` or `Failed`); its member runs, series and battery runs are kept. **409** while it is driven,
live or stopped.

### 5.4 MB-R5 Stall

The batch DTO carries `stalled` while the batch is live and `LastProgressAtUtc` is older than
`StallMinutes`. The progress dialog then shows *No progress for {n} min*. It is informational: nothing is
canceled.

## 6. Results of a Member

`GET runs/{id}` reads each finished member's result (`BenchmarkModelBatchMemberResultDto`), cached per
member until the runs it covers, their completion or the member's status change:

- **Index** — a battery member's **Overall Index** and its combined half-width from its battery run's
  latest analysis (`batteryAnalysis`); a series member's pooled **Intelligence Index** from its run group's
  latest analysis (`groupAnalysis`), else the mean of its runs' indices (`runMean`); a single run's index
  with 1.96 × its standard error (`run`).
- **Speed** — the median model time over the Ok answers of the member's completed runs, and the median
  time to first answer text over those with call telemetry (`TtftP50Ms`).
- **Cost** — candidate and total cost summed over the completed runs, each on its own pricing snapshot,
  and the candidate cost per answered question; null when any run cannot be priced.
- **Errors** — refuted claims, confirmed critical errors, the own-wait share, failed answers, provider
  errors and retries.

The batch carries the live candidate and total cost of every member run so far (a running run's from its
answers), null when any is unknown, and per member its run ids, the run in flight with its stage, its
1-based step (*Run k of R*, or a battery's slot), the questions answered, the instrument its first run
recorded, and the instrument keys on which that differs from the batch's first member
(`InstrumentDriftKeys`; the prompt hash only between members of one parallel mode).

## 7. The Launcher

Admin → GnollBench → **Run Benchmark**. The card opens with **Models**, a radio group: *One model* (the
default) or *Model batch*. *Model batch* replaces *Model Under Test* with **Models Under Test**, the
multi-model picker (`app-model-multi-picker`, with price and parallel badges, at most `MaxModels`, 12
models): a model that is currently a scoring grader or the report writer is an unavailable option (*Grades
this batch*, *Writes its reports*, MB-B03 and MB-B04 derived in the browser from the same configuration
ids). Each chosen model is a glass card under the trigger, as wide as its name and badges, in a row that wraps: its
name, a remove button in the corner, the same badges the list shows (thinking level, reasoning mode,
provider, price, parallel mode) and a start edge in the provider's color. A chosen model a warning names
carries an amber note on its card, led by a warning glyph (*Same provider as the assessor*, *Same
provider as the report writer*, *Checks its own answers*, *Settings differ*, *Selected twice*). Beside
the trigger, after *All* and *None*, the count reads *2 selected · max 12*. The rest of the launcher is
the one a run uses, with these differences:

- **Run Target** works as before: a suite, or a battery. On a suite, **Runs per model** (R; 1 runs each
  model once, 2 or more a replicate series per model); on a battery, *Runs per Suite*.
- **Model order** — *Randomized (recommended)* or *As listed*, the latter with a reorderable list of the
  chosen models (*Models in run order*).
- **Report Writer** starts at *None* when batch mode is chosen: every member would get its own
  documents, and one document over the whole batch comes from Model Comparison once it ends.
- **Batch Options** — *Wait when the run cap blocks the next run* (required when the batch plans more
  launches than the daily cap), and on a battery the note *Reuse earlier runs: not available for a model
  batch.*
- **Batch Projection** (*n models × R = L runs*, or *n models × K suites × R = L runs*) — projected wall
  time and cost (*each model at its recent mean run duration / cost on this target*, or why it is not
  projected), the remaining daily headroom with the runs in the last 24 hours and the last hour, the day
  span when the batch needs more than one window, a *Per-model basis* disclosure, and the alerts for the
  daily cap, the daily headroom and the hourly cap.
- **Batch Readiness** — the card above **Start Model Batch** (§ 7.1).

The batch settings are remembered with the launcher's (`runMode`, `batchModelIds`, `batchOrder`,
`batchOrderIds`, `batchRunsPerModel`); the acknowledgments are kept for one start only and never stored.

### 7.1 Readiness, inline lines and the confirmation

**Authority.** The server evaluates every rule (`POST preflight`); the launcher asks it 300 ms after the
settings settle, one request at a time (`debounceTime` and `switchMap`), retries a failed check after 2 s,
doubling to 30 s, and renders what it returns. It duplicates no rule except the picker's MB-B03 and
MB-B04 options.

The guidance comes in four layers, each carrying less text than the one before:

1. **At the source — the picker.** A blocker cannot be selected in the first place, and a warned model
   carries its note on its card.
2. **Inline, one line per field.** Under a field, its first blocker as a `.gh-field-error` line, else its
   first warning as a `.gh-field-warning` line, linked by the field's `aria-describedby`. Advice is never
   inline.
3. **The Batch Readiness card** (`app-model-batch-readiness`), shown once a model is chosen (or at once
   for a blocker that exists from the start, such as a busy server): a summary row with a status word and
   glyph — *Ready*, *{n} to review*, *{n} blocking* — and count chips (*Blocking n*, *To review n*,
   *Acknowledged n*, *Tips n*), then a *Findings (n)* disclosure, open while anything blocks or awaits
   acknowledgment. Each line has its severity word and glyph, title, sentence, a click-mode info tip with
   the rationale, **Go to field**, and for a warning an **I understand** checkbox; the advice sits in a
   nested, closed *{n} tips* disclosure. A visually hidden `role="status"` line announces only the counts,
   once per settled check.
4. **The confirmation dialog** — *Start model batch?* with the plan in one table: the models in run order
   (with the order and seed rule), the target, runs per model or per suite, planned runs, projected cost
   and wall time, the graders, and the acknowledged warnings by title; **Cancel** and **Start Model
   Batch**. Nothing is sent before it is confirmed. A refusal that carries findings refreshes the card.

*Start Model Batch* is `aria-disabled`, with its reason under it, until two models are chosen, a target
and an assessor are set, the check has answered, no blocker remains and every warning is acknowledged.

## 8. Following a Batch

**The banner.** While a batch is live or stopped, a banner reads *Model batch #N (target)* and *Model k of
M: \<model\>*, *Waiting for run cap — …* or *Stopped — \<reason\>. k of M models completed. Continue from
the progress dialog.*, with **Show Batch Progress** and **Cancel Batch**, which asks for confirmation
first. While it stands, the run, series and battery banners of its members stay hidden. On page load the
banner reattaches to the batch this process drives, else the newest live or stopped one (`GET
runs/active`), without opening the dialog. The banner's poller reads `GET runs/{id}` every 5 seconds, every
15 seconds in a hidden tab while an alert is on, and once when the tab is shown again; a failing poll backs
off and raises the shared *Lost contact* notice (*Model batch #N …*). While a member's run is in flight the
run poller follows it.

**The Model Batch Progress dialog** (`app-model-batch-progress-dialog`), full-screen, opened on Start, from
the banner and from a Run History batch card. Its header is *Model Batch #N* with the GnollBench emblem, the
subtitle *\<target\> · \<n models × K suites × R runs\> · \<order and seed\>* and the scoring graders. The
body holds:

- a stage rail, *Model runs* (*k of M models*) then *Ready to compare*;
- the *Models finished* progress bar and the dialog's one `role="status"` line (*Running model k of M:
  \<model\>*, *Waiting for the run cap*, *Stopped: \<reason\>*, *Completed: k of M models* …);
- for a stopped or waiting batch, a state block with the stop reason, its detail and each resume option
  with what it does;
- a stat strip: *Status*, *Elapsed*, *Models done*, *Runs* (launched of planned), *Failed*, *Candidate
  cost* and *Total cost* (*so far* while live);
- *All members share one instrument*, or *Instrument changed before \<model\>: \<keys\>*, tagged *Not
  comparable across the change* once a change was accepted;
- the MB-T04 line, and the stall line (MB-R5);
- **Models in run order**: one card per member with its position, name and badges and its status chip.
  A running member shows its step (*Run k of R*, or *Suite s of K · round r: \<suite\>*), its run and stage,
  the questions answered and its elapsed time; a pending one *Waiting*, with **Skip model**; a finished
  one its index badge (*Intelligence Index* or *Overall Index*), median model time, TTFT P50 and cost per
  question; a skipped, failed, stopped or canceled one its reason. Each card links to the member's
  **run progress** per run, or its **battery progress**, and to its report;
- **Batch diagnostics**, a disclosure with **Copy** and **Download** (§ 9).

The footer has **Run in Background** (or **Close**), **Cancel Batch** (confirmed first) while the batch is
live or stopped, one button per resume option, and **Open in Model Comparison**, available once two members
have a result: it closes the dialog and opens the comparison wizard with the members' battery runs, runs
or (for a series) run groups selected, at most 24 sources. While a request is in flight the other actions
are `aria-disabled` with the reason beside them. The dialog polls `GET runs/{id}` every 2 seconds while the
batch is live or resumable, one request at a time, backing off 4, 8, 16 and 30 seconds on failures and
pausing while the tab is hidden.

**Members' own dialogs.** A member's run or battery progress dialog **replaces** the batch dialog (two
modal dialogs would trap focus between them); its dismissal then reads **Back to Batch** and returns to
the batch dialog. While the batch is not final, the battery and series progress dialogs of its members
say *Part of model batch #N.* with **Open model batch #N**, and their Continue, Re-run under current
instrument and Cancel are `aria-disabled` with *Use the model batch's progress dialog.*, as the server
refuses those resumes (§ 5.1).

**Run History.** The newest 200 batches (`GET runs?take=200`) are listed beside runs and battery runs as
*Model batch* cards (the Kind facet's third value): *Model batch #N · status · k of M models*, the target,
the runs per model or suite, the start time and the user, *Models*, *Runs*, *Duration* and *Cost*, the
models in run order with their badges, and **View progress**, **Open in Model Comparison** and **Delete
model batch** (final batches only). A run or battery run card whose run belongs to a loaded batch carries
a *Batch #N* kicker, read from the batches' member lists, and from a battery run's own `modelBatchRunId`.

## 9. Completion and Failure Alerts

A batch alerts **once, at its end**, under the launcher's *Completion Alerts* settings (the sound and the
desktop notification), by the same end rule as every other run kind
(`benchmarkEndSignal('modelBatch', status)` in `state/benchmark-end-signal.ts`):

| End | Signal |
|---|---|
| `Completed` | the completion chime |
| `Stopped` (any stop reason, a restart included), `Failed`, `CompletedWithErrors` | the *AI Benchmarking Failed* sound |
| `Cancelled` | nothing |

The failure sound means *this end needs your attention*: a stopped batch waits for the operator, and a
`CompletedWithErrors` batch has a member without a clean result. The desktop notification reads *Model
batch #N — finished: k of M models*, *— stopped: \<reason\> at \<model\> (k of M)*, *— failed: \<error\>
at \<model\> (k of M)* or *— completed with errors: k of M models*. A batch signals only after this page
has seen it live (`batchesSeenLive`), once per end: the signal key `modelbatch:{id}:{status}:{stopReason}:
{lastProgressAtUtc}` lets a batch that stops, is continued and stops again signal each stop, while
repeated polls of one end do not. **Its members signal nothing of their own** while the page follows the
batch; a member run the operator re-ran by hand signals as any run does. Start and every resume arm both
sounds under the click.

## 10. Diagnostics

`GET runs/{id}/diagnostics` returns plain text, which the dialog shows and copies or downloads as
`model-batch-<id>-diagnostics.txt`: **BATCH** (status, stop reason and detail, target with its battery
revision and definition hash, suites, created, started and completed, member counts, cost so far, whether
this process drives it, resume options), **SETTINGS** (runs per model, cap wait, every grader and the
report writer by name, second-opinion mode, scoring profile, response style, source code references),
**GUARDRAILS** (every warning acknowledged at start and every piece of advice, with its affected
configurations, and an accepted instrument change), **ORDER** (mode, seed, run order), **MEMBERS** (each
model's configuration snapshot, status, links, runs, start with its time-of-day stratum, completion and
duration, the run in flight, and its result), **INSTRUMENT** (the first member's, then each member's with
its drift), **TIMING** (median model time, TTFT P50, own-wait share), **ERRORS** (failed answers, provider
errors, retries, the member's error), **CAPS** and **PROGRESS** (last progress, the stall threshold and
flag, the current member).

## 11. API

All routes are under `api/admin/benchmark/model-batches` (`AdminOnly`). Enums travel as strings.

| Method | Route | Purpose |
|---|---|---|
| POST | `preflight` | Body `StartBenchmarkModelBatchRequest`. Every finding, the projection with its limits, and `maxModels`. Creates and spends nothing |
| POST | `runs` | Start. Body: `targetKind` (`Suite` or `Battery`), `suiteId` or `batteryId`, `testedModelConfigurationIds` in listed order, `runsPerModel`, `order` (`Randomized` or `AsListed`), `allowCapWait`, `run` (the template), `acknowledgedFindingKeys`. **201** with the batch; **400** `{ message, findings }` with the blockers; **409** with the busy blocker, or with the warnings still to acknowledge; **404** for an unknown target |
| GET | `runs/active` | The batch this process drives, else the newest live or stopped one; **204** when none |
| GET | `runs?skip=&take=` | Batches newest first, without member results (`take` default 50, at most 500); Run History asks for 200 |
| GET | `runs/{id}` | One batch with its members' progress and results: what the banner and the dialog poll |
| GET | `runs/{id}/diagnostics` | The diagnostics as `text/plain` |
| POST | `runs/{id}/cancel` | Cancel. **400** for a final batch |
| POST | `runs/{id}/resume` | Body `{ mode: "Continue" \| "SkipCurrent" \| "AcceptInstrumentChange" \| "RerunUnderCurrentInstrument" }`. **200** with the batch; **409** `{ instrumentChanged, batchId, changedKeys, message }` when Continue finds the instrument moved; **409** while a batch, a run or another claim is active; **400** for a mode not offered now or a batch not `Stopped`; a resumed child the server refuses is **400** with its reason |
| POST | `runs/{id}/members/{memberId}/skip` | Mark a pending member Skipped. **400** otherwise |
| DELETE | `runs/{id}` | Delete a final batch's record; its member runs, series and battery runs are kept. **204**; **409** while live or stopped |

Elsewhere: `GET /api/admin/benchmark/runs/limits` carries `maxModelsPerBatch`; a run's detail, a series and
a battery run carry `modelBatchRunId`, the newest batch they belong to; and the series and battery resume
endpoints answer **409** for a member of a batch that is not final (§ 5.1).

## 12. Limits

- **Sequential only.** Members never overlap, so a batch of many battery passes takes days; MB-W09 and
  MB-T02 say so before Start. Wall time includes each battery member's analysis and documents.
- **No reuse.** Every member is a fresh run, series or battery run.
- **No interleaving.** Replicate rounds are not interleaved across models; randomized order spreads the
  hours between models, not within one model's runs.
- **One build.** A batch that spans a deliberate change (a harness bump, a tool-guide edit, a corpus push)
  is not one comparison: re-run under the current instrument, or accept the change and read the two sides
  separately.
- **The guardrails advise; they do not choose.** They name configured models only; which families to use
  as graders is in [`ai-benchmark.md`](ai-benchmark.md) § 3 *Choosing grader models and effort*.
