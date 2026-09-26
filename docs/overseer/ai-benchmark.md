# Overseer AI Intelligence Benchmark Feature

The AI Intelligence Benchmark subsystem in Overseer provides automated, reproducible evaluation of AI models against domain-specific roguelike game knowledge, spoilers, monster/item stats, and C codebase logic for GnollHack.

**Purpose and priorities.** AI benchmarking serves, in this order of importance: **(1) improving the main AI chat of the Overseer** — the benchmark runs the production chat prompt and the production tools, so it is how the chat is debugged and fixed: that the system and its tools work correctly, that the models perform at maximum efficiency, and that there are no bugs or other problems; **(2) improving the benchmarking system itself**, so that it works rigorously and correctly and its results serve both the chat and a scientific comparison of models; and **(3) the results themselves** — how well different models perform as the Overseer assistant. It therefore has two uses — debugging and improving the chat, and deciding which models the chat should use — and both aim at the best possible AI assistant experience for regular users of the Overseer. § 9.1 *What the Benchmark Tells the Chat* describes the first use in detail.

---

## 1. Architectural Overview

The benchmark framework consists of:
- **Suites & Questions**: Configurable collections of questions stratified across difficulty tiers (`Simple`, `Intermediate`, `Advanced`), with AI-assessed difficulty scores ($1\text{--}100$).
- **Multi-Dimensional BARS Assessment**: Behaviorally Anchored Rating Scales (0–6) evaluated across four distinct dimensions:
  - **Accuracy** (Weight: 55%)
  - **Completeness** (Weight: 25%)
  - **Conciseness** (Weight: 10%)
  - **Readability** (Weight: 10%)
- **Critical Error Ceiling**: If an answer contains critical hallucinations, dangerous commands, or complete fabrications, its overall Quality Score is hard-capped at the critical error ceiling (default: 25).
- **Logarithmic Speed Decay**: Response speed is graded relative to a target latency (default: 5,000 ms) using $Speed = \text{clamp}(100 - k \cdot \log_2(\text{DurationMs} / \text{TargetMs}), 1, 100)$.
- **Pipelined Evaluation & Concurrency Control**: Evaluator assessments are pipelined concurrently with candidate answer generation when candidate and assessor models use separate API keys, while safely serializing when sharing a rate-limit semaphore permit.
- **Provider Error Isolation**: Distinguishes between genuine model errors (wrong answers, hallucinations) and transient API infrastructure failures (HTTP 429 rate limits, 503 service unavailable, 529 overload). Provider errors are excluded from scores and denominators.
- **Configurable Scoring Profiles**: Entities defining weights, level-to-score mappings, critical error ceilings, speed target latencies, decay factors, and maximum parallel questions.
- **Exportable Markdown Reports**: Generates comprehensive 7-section Markdown reports containing run manifests, results summaries with Intelligence and Speed indices, question replies, tool traces, scoring methodology, and final qualitative synthesis.

### Run Progress Dialog

Starting a run opens a modal progress dialog, reachable again at any time from the **Show Progress** button on the active-run banner.

The dialog header carries the run number, the suite and the scoring profile. The two models are **not** in the header: they appear directly below it in a badge strip, badged exactly as the model selectors in the AI Benchmark tab badge them — thinking level, reasoning mode, provider, requested service tier, and parallel tool calls — so the configuration under test is legible without opening the report.

It presents the run as **three** stages, which is how `BenchmarkService.ExecuteRunAsync` (and `RunFailedQuestionsAsync` for a re-run) actually sequences the work:

1. **Answering and grading** — every question is answered, in parallel up to `MaxParallelQuestionsUsed`, and each answer is then assessed, its unverified claims checked, and a second opinion taken on it, all before the loop moves on. Most claim checks and most second opinions of a run happen here, not later.
2. **Follow-up grading passes** — once every answer is graded, a serial tail runs: the remaining claim checks (mainly contested verdicts and critical-error splits), then the outlier sweep or the sample top-up in the modes that have one, then the remaining claim checks again for any split the sweep created.
3. **Synthesis and scoring** — the holistic report and final indices are produced.

Stage 1 is one stage and not three because the executor pipelines assessment, verification and the second opinion behind each answer inside the same loop, in both the sequential and the parallel branch, so no instant of it belongs to only one of them. (The one exception is a credential collision between the candidate and assessor configurations, which serialises the assessments behind all the answering; that still falls inside stage 1.) Stage 2 is one rail item and not two because the server marks `Verifying` **twice** — before and after the second-opinion pass — so an item per pass would step the rail backwards near the end of a run. In `Flagged` mode with no contested verdicts the tail makes no model call at all and stage 2 may be visible for a single poll or none; that is truthful, and stage 1's counters already carried the work.

The stage comes from the server's `BenchmarkRunDetailDto.Stage` whenever there is one, because nothing in the answer rows moves during the tail and no client-side derivation can see it. The derivation is the fallback for a run this process is not executing, or a detail from a server predating the field, and it can only reach stage 1 or stage 3. **Diagnostics** name the pass the rail collapses away: `Stage: 2 of 3 (verifying)` or `2 of 3 (second opinion)`, and `(server)` or `(derived)` for its source. Two determinate progress bars — Answers and Assessments — stay visible throughout, so a full Answers bar during grading does not read as a hang.

The per-question list merges the suite's questions (fetched once when the dialog opens) with the run's answers, and distinguishes five states:

- **Pending** — the question has not been dispatched.
- **Answering** — the request has been sent to the provider and no reply has arrived yet.
- **Answered / Scored** — an answer row exists.
- **Verifying** — the claim verifier is re-reading a row that already carries a score.
- **Second opinion** — the second-opinion assessor is re-grading such a row.
- **During a failed-question re-run**, a scope member reads **Pending** until its request is dispatched, **Answering** while it is in flight, and then its re-executed row's own state; its previous failure chip is not shown while the re-run is running. Under a re-run scope the Elapsed stat reads **Re-run elapsed** and measures the re-run's own span from `RerunStartedAtUtc`, because the run's `CompletedAtUtc` stays fixed across a re-run. `RerunCompletedAtUtc` is cleared when a re-run starts and is ignored while the run's status is Running, so a second re-run's own elapsed stat is never computed against the previous re-run's stale end stamp.

The last two appear **inside stage 1** as well as during the follow-up passes, because that is where most of that work happens. Both come from in-flight sets that the wrapper methods `VerifyAnswerClaimsAsync` and `RunSecondOpinionAsync` set and clear in a `finally`, so every caller marks the row and a throw or a cancel cannot leave it pulsing.

**Claims verified** and **Second opinions** are plain counts in the statistics strip, not progress bars, and each appears only when the run configured that role. Neither has an honest maximum: only an answer whose assessor listed unverified claims is a verification candidate, and only an answer whose trigger fired is a second-opinion candidate, so a bar drawn against the answered count either sits full or never fills. A `· N in progress` suffix shows the in-flight count while the role is reading a row.

The dialog has exactly **one** polling live region — the status line under the Assessments bar, which announces the stage. A second one would announce continuously for the length of the run.

`BenchmarkService` creates a `BenchmarkRunAnswer` only after the model replies, so in-flight state is not derivable from the answers alone: it comes from `BenchmarkRunManager`, which records the order indexes currently in flight (`MarkQuestionInFlight` before the provider request, cleared in a `finally`) and exposes them as `BenchmarkRunDetailDto.InFlightOrderIndexes`. The list is empty for any run that is not the current, still-running one, so a completed run or a restarted server reports nothing rather than stale state.

Diagnostics are assembled **client-side from the run detail**, not from a server-side log — `BenchmarkRunManager` keeps none, and the DTO already carries the run manifest, token counters, flags, and per-question error and HTTP status codes. Answer text, thought text, and assessor comments are deliberately excluded: they are long, model-generated, and already reachable through the run detail dialog and the Markdown report.

Reloading the admin page mid-run restores the banner via `GET /api/admin/benchmark/runs/active`. The dialog is **not** auto-opened on load, so it never steals focus from work in progress.

### Runs That Stop Early: Totals Without a Score

A run that never reached the end of its suite has a real cost and a real elapsed time, and no score. The harness keeps those two facts apart.

**`ApplyTotals` versus `Apply`.** `BenchmarkRunFinalizer.ApplyTotals` writes the measured totals — candidate, assessor and claim-verifier token sums, the long-context subsets, `TotalAnswerDurationMs`, `ToolOverheadMs`, `AnsweredQuestionCount`, `UnansweredQuestionCount`, and every integrity and advisory counter. It touches neither `Status` nor `CompletedAtUtc` nor any of the four score columns. `BenchmarkRunFinalizer.Apply` calls it and then adds the scores: `QualityIndex`, `QualityIndexStandardError`, `UnweightedQualityIndex`, `SpeedIndex`, the completion timestamp and the computed status. `Apply` remains the only writer of the score columns.

**Every abort path records totals and no score.** `ExecuteRunAsync`'s sequential loop cancellation check and its two terminal handlers call `ApplyTotals` before saving, leaving their own `Status`, `CompletedAtUtc` and `ErrorMessage` assignments intact. A live run also records the wall clock its own stopwatch measured, up to the stop. `POST .../runs/{id}/cancel` does the same for an **orphaned** row — one whose live run is gone, so its abort path will never run — deriving the elapsed figure from the two timestamps; it deliberately does not do this when a live run was cancelled, because that run measures its own wall clock.

**A cancelled retry is not an abort.** A retry never removes an answer row, so a run whose answers covered its suite before the retry still covers it after the cancel — writing `Canceled` there would be a claim the run stopped early, and would make every later re-run refuse it. All six retry handlers (`RunFailedQuestionsAsync`, `RerunSingleQuestionAsync`, `ReassessSingleQuestionAsync`'s non-trial branch, `RerunFinalSynthesisAsync`, `RetryFailedAssessmentsAsync` and `RetryFailedClaimVerificationAsync`) therefore call `RestoreTerminalStatusAsync` on cancellation, which re-finalises the run over its whole answer set and records the reason in `ErrorMessage`. Each also checks the token as the first statement of its `try`, so a retry launched with an already-cancelled token takes that path rather than failing on whatever it touched first; the load that precedes the `try` deliberately passes `CancellationToken.None`, because a throw there would escape past both the handlers and the `finally` that releases the run manager. `POST .../runs/{id}/cancel` does the same for a row whose answer rows cover its suite, writing `Canceled by operator.` as the reason. A trial re-assessment is unchanged: it restores the status the caller captured.

**An interrupted run becomes `Failed` and carries no index.** `CleanupOrphanedRunsAsync` runs at startup and finalises every row left `Running` by a restart. It records the totals, sets `Failed` with `Run interrupted by application restart.`, and publishes no index: an index over the answers that happen to exist describes a fraction of the instrument and is, once stored, indistinguishable from an index over all of it. `TotalDurationMs` is left at zero, because wall clock to the moment cleanup runs would include the outage between the crash and the restart. The Run History cell and the report both fall back to the timestamps and label the figure as elapsed.

**Re-scoring and re-running are refused on such a run.** The test is `BenchmarkRunFinalizer.IsAbortedRun`: the status is `Canceled` or `Failed` **and** the run has fewer answer rows than `TotalQuestionCount`. `RescoreRunAsync` returns an error for such a run, and the six endpoints that end in `Apply` — reassess answer, rerun answer, rerun synthesis, retry failed assessments, retry claim verification, rerun failed questions — answer 400 with the same message. The coverage half of the test is what the refusal text has always claimed ("stopped before finishing its suite"), and it is what separates a run cancelled part-way through answering from one cancelled during its grading stages or during a retry, whose answer set is whole and whose next finalisation recomputes an honest status over all of it. A `TotalQuestionCount` of zero was never recorded and counts as not covered. Reading, reporting, calibrating, cancelling and deleting stay available; only recomputing indices is refused. A `CompletedWithErrors` run is **not** refused, because it reached the end of its suite. In the admin UI the four reachable controls are disabled and a notice above the Rescore button explains why, so a disabled button is never unexplained. The client reads the verdict as the `isAborted` flag on the run summary and detail DTOs rather than reimplementing the test — it falls back to the status alone only for a response from a server that predates the flag. The server guard is the authority.

**Run History shows the duration and the shortfall.** The Duration cell shows candidate answer time for a run that finished, and wall clock up to the stop for one that did not, falling back to the two timestamps when neither figure was recorded; the column sorts by what it displays. Beside the status badge, **any** terminal run that answered fewer questions than its suite holds carries an `answered / total` badge — which is what separates an index of 74 over 16 of 18 questions from 74 over 18.

**The run detail dialog carries both figures, as two cards.** *Answer Duration* is the time the candidate spent producing answers; *Elapsed Wall Time* is start to finish with grading included. They are the same pair the report prints as **Total Candidate Answer Time** and **Total Elapsed Wall Time**, and the gap between them is the grading pipeline — on run 24, 12m 45s of answering inside a 23m 39s run. Neither card falls back to the other: a figure labelled as answer time shows answer time or a dash, because substituting the wall clock under that label would report a different measurement as though it were the labelled one. The wall-clock card *does* fall back to the two timestamps, which is the same measurement by another route rather than a different one — a run interrupted by a restart records no wall clock of its own.

**The report states what it does not have.** `### Harness Cost` fires on candidate spend as well as grading-role spend, so a run that stopped before any grading still reports what it cost; an **Answer Rate** line sits under Answered Questions. A provenance line claims a run predates a harness version only when the run's own `HarnessVersion` parses and is genuinely lower — an unknown version is not evidence of age, and a missing figure on a current run has a current cause. The band distribution is labelled as being over **answers**, which is what it counts.

**The provider's finish reason is persisted.** `BenchmarkRunAnswer.ProviderFinishReason` holds the provider's own verbatim reason for ending the response — Anthropic `stop_reason`, OpenAI `incomplete_details.reason` or status, Google `finishReason` — unmapped. It is distinct from `TerminationReason`, which describes what the harness loop did. The pair is what separates an empty answer the model produced from one a transport defect destroyed, and therefore what the harness scores 0 from scoring method 10 on. Null means "not recorded" and never "stopped normally". The three providers emit it as a `finish_reason` `ChatEvent`, which `AgentLoopRunner` consumes without yielding onward; truncation detection now prefers this typed value and keeps the older debug-text match only as a fallback for a provider that does not emit it.

**A moved instrument and a changed option are different things.** The Run History instrument badge compares a run's three hashes against the next older completed run of the same suite. `CandidateSystemPromptSha256` covers the prompt *as built*, so a run option that changes the prompt text — `verboseMode` is the usual one — moves the hash without the instrument having moved. The badge therefore consults `CandidatePromptOptionsJson` first: differing options are reported as `OPTIONS CHANGED` naming the keys that differ, and only runs whose options match can say anything about whether the instrument held still. Runs 24 and 25 of 2026-09-08 are the worked example — the two candidate hashes differ by 92 characters and by exactly one option, 25 minutes apart, on unchanged code, and `INSTRUMENT CHANGED` fired twice while the instrument never moved.

---

## 2. BARS Rating Scales & Score Mapping

Levels are scored on a 7-point scale mapped non-linearly to 100 points:

| Level | Score | Anchor Definition |
|---|---|---|
| **0** | `1` | Completely incorrect, nonsensical, irrelevant, or fabricated. |
| **1** | `15` | Major inaccuracies with isolated correct fragments; misleading. |
| **2** | `35` | Partially correct but significant errors or critical omissions. |
| **3** | `55` | Mostly sound; several minor defects of the dimension's kind. |
| **4** | `72` | Sound, with minor defects that would not mislead a player or leave the question unanswered. |
| **5** | `87` | Strong, with at most one trivial defect of the dimension's kind. |
| **6** | `100` | No defect of the dimension's kind. |

The table gives the shape of the scale only. The per-dimension anchors the assessor actually grades
by — and the scope rules beside them — are in `BenchmarkAssessmentPrompt.BuildPerQuestionPreamble`,
which is authoritative wherever the two differ.

### Speed Scoring Profiles
Response speed is graded relative to a target latency and decay factor using:
$$\text{Speed} = \text{clamp}\left(100 - k \cdot \log_2\left(\frac{\text{ModelTime}}{Target(q)}\right), 1, 100\right)$$

where $\text{ModelTime}$ is the turn duration with harness tool I/O removed, and $Target(q)$ scales with assessed difficulty. Note it is **not** `DurationMs`: on a tool-heavy question the two differ by the whole tool time.

Overseer supports configuring scoring profiles to accommodate different agent architectures:
1. **Standard Intelligence Index** (Default / Interactive Agent Profile):
   - **Target Latency (`SpeedTargetMs`):** 2,000 ms (2 s)
   - **Decay Factor (`SpeedDecayK`):** 12.0
   - **Max Parallel Questions:** 1 (Sequential, strict timing)
   - **Intended Use:** Standard conversational models and interactive agents where rapid turn completion is desired. At assessed difficulty 0 a 2-second response yields 100 points, decaying to 88 at 4 s, 76 at 8 s, 64 at 16 s and 52 at 32 s; difficulty raises the target proportionally, so a difficulty-50 question is scored against 3 s rather than 2 s. The target is model time with tool I/O already removed, which is why two seconds is a reachable figure for an agentic turn.

> These two constants are pinned by the invariants documented on `BenchmarkScoringConstants`, and `BenchmarkScoringTests` fails the build if they are changed without re-deriving the per-band timeout margins. **This section previously documented 5,000 ms / k = 25.0, and then 15,000 ms / k = 20.0.** The first pair floored at ≈ 78 s and tied slow answers together; the second saturated at the ceiling instead — run 48 scored 100 on 18 of 18 — and was replaced in the runs 47–48 round once the speed constants left the quality comparability signature (harness 28). **15,000 ms / k = 20.0 is what reproduces the published scores of every run up to 48**; a run scored before the recalibration keeps its old-scale Speed Index until an administrator uses the per-run **Rescore** action on it.
2. **Reasoning Agent Profile** (Deep Thinker Profile):
   - **Target Latency (`SpeedTargetMs`):** 30,000 ms (30 s)
   - **Decay Factor (`SpeedDecayK`):** 15.0
   - **Max Parallel Questions:** 1 (Sequential)
   - **Intended Use:** Heavy thinking models (e.g., Claude 3.7 Sonnet with extended thinking, OpenAI o-series) performing multi-step tool iterations, multi-file inspection, and extended reasoning traces. A 30-second response yields 100 points, decaying smoothly to 85 at 60s and 70 at 120s. Administrators can create this profile in the Scoring Profiles UI or API.

### Harness Version 2 & Scoring Method Version 3 Updates
With Harness Version 2 and Scoring Method Version 3:
- **Per-Question Tool Budget** *(historical — superseded by the four banded caps in Harness Version 5 below; `Benchmark:MaxToolCallsPerQuestion` no longer exists)*: Candidate models have a dedicated tool execution budget of **25 calls per question** (configured via `Benchmark:MaxToolCallsPerQuestion`, default: 25). The budget scope is uniquely keyed per question (`bench_{runId}_q{orderIndex}`) with a 1-hour cache expiration. Once exhausted, subsequent tool calls in that question are rejected with an explanatory error (`BudgetExhausted = true`), preventing runaway loops while allowing the model to summarize its findings.
- **Unbiased Assessor Prompts**: Turn duration has been completely removed from per-question assessor prompts (`BuildPerQuestionPrompt`) to eliminate evaluator bias against thorough reasoning models.
- **Harness Context Block**: Assessors receive a structured `Harness Context` detailing available tools, completed tool call count, and whether the tool budget was exhausted.
- **Harness Artifact & Tool Unavailability Guidance**: Assessor instructions explicitly direct the evaluator not to dock scores when harness-imposed tool unavailability prevents information retrieval, and to treat raw tool call JSON, control tokens, and repetition as transport artifacts rather than model authoring flaws.
- **Degradation Detection & Run Integrity**: Identifies degraded answers (`EmptyAnswer = 5`, or flags for harness artifacts, truncation, or tool starvation). Answers marked as `EmptyAnswer` are excluded from score indices. Runs containing any degraded or tool-starved answers were marked with status `CompletedWithErrors` — see the Harness Version 4 section below, which narrowed this.
- **Raw Quality Index & Assessed Difficulty Bucketing**: Markdown reports display both the canonical difficulty-weighted Intelligence Index and the Raw Quality Index (showing critical error cap impact). The Difficulty Breakdown buckets results by `AssessedDifficulty` (Simple: 1–33, Intermediate: 34–66, Advanced: 67–100) alongside authored band distributions and non-monotonicity notices. All floating-point numbers format strictly under `CultureInfo.InvariantCulture`.

### Harness Version 4 & Scoring Method Version 5 Updates

Prompted by the 2026-09-03 GPT-5.6 Luna run, which was reported as `CompletedWithErrors` while its own diagnostics said `failed 0` and `ERRORS: none`.

- **Four integrity classes, not three.** `BenchmarkAnswerIntegrity` gains `Recovered`: the provider leaked transport artifacts, the scrubber removed them, and the answer beneath was graded normally. Every answer falls in exactly one of Clean / TransportDefect / Recovered / HarnessLimit, and the four sum to the question count. `TransportDefect` now means unrecoverable only — `Empty` or `Truncated`. A run whose worst event is a recovery or a configured cap is `CompletedWithLimits`, not `CompletedWithErrors`. Historical runs keep their stored status until re-scored.
- **Executed, blocked, budget.** `ToolCallCount` counts *attempted* calls, including the ones the budget refused, so reports printed impossible lines such as "27 of 25 calls used". Reports now separate the three figures.
- **Real provenance.** The report's Overseer version comes from the running assembly instead of a hard-coded `1.0.0`, and the harness version is a code constant (`BenchmarkAssessmentPrompt.HarnessVersion`) rather than a configuration key an operator can edit without changing the harness.
- **Critical error requires a quoted claim (v5).** An omission can never be a critical error — that is what COMPLETENESS grades. The assessor must return `criticalErrorQuote`, copied verbatim from the graded answer; `BenchmarkAssessmentParser` demotes an unverifiable claim to `criticalError = false` and records why in the comment. Scores are **not** comparable with v4.
- **Deduction evidence.** Assessors state, per dimension, which rubric point a deduction rests on — or that it rests on their own knowledge instead. Stored in `AssessmentEvidenceJson` and shown in the report and the run detail.
- **Second opinion.** A run may name a **second opinion assessor**, selected in the start dialog like every other model and recorded on the run (`BenchmarkRun.SecondOpinionAssessorModelConfigurationId` plus its settings snapshot, `SecondOpinionAssessorModelSnapshotId` — see **Model Configuration Snapshots**). It is optional and off by default; when none is selected, no answer is re-graded. There is deliberately **no fallback to the run's own assessor** — a model checking its own verdict produces agreement, not a second reading. The trigger is a critical error, or a quality score below the scoring profile's `SecondOpinionQualityThreshold` (default 50; `0` disables the score trigger and leaves second opinions to critical errors alone). Both verdicts are kept. The **first stays authoritative for scoring**; a material disagreement — more than 15 quality points, or a split on `criticalError` — sets `SecondOpinionDisagreed` and surfaces as a `DISPUTED` badge and a Disputed Assessments report section, for a human to settle with the existing re-assess action.

  Neither the model nor the threshold is a configuration key. A `SystemAiApiConfiguration` id is a database identity that means nothing in a settings file and cannot be picked by an administrator; the threshold sits on the scoring profile so it is snapshotted into the run and a report can say what produced its second verdicts.
- **Assessor cost.** Per-answer `AssessmentInputTokens`, `AssessmentOutputTokens` and `AssessmentDurationMs`, aggregated onto the run, and reported in a Harness Cost block. The candidate token totals stay the candidate's alone.
- **Report clarity.** Time-to-first-token percentiles; "Question Parallelism" and "Parallel Tool Calls" as distinct names for two distinct mechanisms; and a note that Speed Index comparisons are meaningful only between runs at the same thinking level.
- **One artifact vocabulary.** `TransportArtifactRules` holds the payload-detection rules that `ReasoningTextSanitizer` (live streaming) and `BenchmarkArtifactScrubber` (benchmark grading) both apply. The streaming sanitizer previously knew only `{"tool_uses": …}`, which is why five of eighteen answers reached the benchmark carrying payloads it had let through — and why chat users saw the same leaks, with nothing downstream to scrub them.

### Harness Version 5 Updates

Prompted by a second defect the 2026-09-03 GPT-5.6 Luna run exposed: the report asserted that reasoning narration was removed before grading, but five graded answers still carried it, and the assessor docked conciseness and readability for prose the report claimed did not exist. `ScoringMethodVersion` does not move for this harness version — no scoring formula changed.

- **Pre-tool visible text is always moved to the thought channel.** A model that emits a visible preamble before a tool call had that preamble graded as answer prose whenever a reasoning summary followed it: the thought-div writer recorded where a visible span started but discarded that offset once a reasoning summary arrived, so the preamble was never wrapped in a thought `<div>` and fell straight into the graded answer. The writer now records **every** visible span of an iteration and wraps each one, regardless of what follows it.
- **The benchmark scrubber's narration rules widened**, as a second line of defence for providers that do not separate the channels cleanly: whitespace-only lines are normalised before paragraph splitting (so a blank line padded with spaces no longer defeats the paragraph boundary the narration strip relies on); a leading orphan backtick run is stripped; the narration-stripping pass now iterates, bounded at 10 passes, instead of running once; the signature vocabulary that identifies narration is wider; and narration butted directly against the answer with no separating blank line is removed sentence-by-sentence, guarded so the strip can never empty an answer outright.
- **Four banded per-question caps, replacing four flat keys.** Every cap that used to be one number for the whole suite is now one number per difficulty band:

  | Band | `ToolCallBudget` | `ToolIterations` | `TotalModelCalls` | `QuestionTimeoutSeconds` |
  |---|---:|---:|---:|---:|
  | Simple | 25 | 12 | 16 | 420 |
  | Intermediate | 35 | 16 | 22 | 600 |
  | Advanced | 45 | 22 | 28 | 720 |

  What each cap actually limits:
  - **`ToolCallBudget`** — total tool calls for the question. This is the graceful-stop budget the report already explains (`FormatToolBudgetLine`): once reached, further calls are refused with an explanatory error rather than the question failing outright.
  - **`ToolIterations`** — sequential tool *rounds*, where one round is one model call plus the batch of tool calls it emitted. It bounds investigation *depth*, not *width*: a model that batches 3 calls per round spends 3× the tool call budget for every iteration it takes, so a wide-batching model exhausts `ToolCallBudget` in far fewer iterations than a model that calls one tool at a time.
  - **`TotalModelCalls`** — total provider requests for the question, tool-triggered and otherwise. This is a runaway-loop safety net and must never be the cap that actually binds first; if it does, one of the other three is misconfigured for that band.
  - **`QuestionTimeoutSeconds`** — wall-clock ceiling for the whole question, independent of the three call-count caps above.

  Sizing rule of thumb: `ToolIterations` ≈ half of `ToolCallBudget`, and `TotalModelCalls` = `ToolIterations` + 4 to 6 (room for the answer-composing calls that don't call a tool).

  Configuration keys are `Benchmark:ToolCallBudget:{Band}`, `Benchmark:ToolIterations:{Band}`, `Benchmark:TotalModelCalls:{Band}` and `Benchmark:QuestionTimeoutSeconds:{Band}` (`{Band}` is `Simple`, `Intermediate`, or `Advanced`). The old flat keys — `Benchmark:MaxToolCallsPerQuestion`, `Benchmark:MaxToolIterations`, `Benchmark:MaxTotalModelCalls`, `Benchmark:PerQuestionTimeoutSeconds` — are **removed**, so there is exactly one place to set each cap rather than a flat default and a banded override that could disagree.
- **The timeout is coupled to the speed floor.** The speed score reaches its floor of 1 point at $\text{ModelTime} / Target(q) = 2^{99/12} \approx 304.4$, where $Target(q) = \text{SpeedTargetMs} \cdot (1 + \text{Difficulty}(q)/100)$. Inside a band, the binding case is always its **lowest** difficulty: a lower difficulty means a smaller $Target(q)$, which means the floor is reached at a smaller `ModelTime` — the floor arrives earliest for the easiest question in the band. At the Simple band's floor difficulty (1) the floor sits at ≈615 s; Intermediate (36) at ≈828 s; Advanced (71) at ≈1,041 s. Each band's `QuestionTimeoutSeconds` sits below its own floor with 195–321 s of margin, which is why the timeout is banded rather than raised to one flat value: a flat 720 s — the value an Advanced question needs in order to spend 45 tool calls over 22 rounds — would let a Simple question run some 105 s *past* its own 615 s floor without timing out, so every Simple answer slower than 615 s would score 1 and be indistinguishable from every other slow Simple answer. That is exactly the flattening the speed constants were pinned to avoid.
- **Report changes.** Answer headings are demoted (`BenchmarkReportBuilder.DemoteAnswerHeadings`) so a model's own `##`/`###` heading can never land at or above the report's own outline level; the advisory sentence for reasoning narration now distinguishes narration that was actually removed from narration that was merely detected (using `ScrubbedArtifactText` non-empty as the per-answer signal, since `NarrationBlockCount` is not a persisted column); the scrub counter reports transport payloads and reasoning narration as two separate figures instead of one that hid the narration count entirely; and a **Critical Errors** headline is printed under Results Summary — with the affected question numbers — whenever at least one answer was critical-error capped, omitted entirely when none was.

### Harness Version 6 Updates

Prompted by a review of the 2026-09-03 GPT-5.6 Luna run's report, diagnostics, and admin-UI screens. `ScoringMethodVersion` stays at **5** — no scoring formula changed — but `BenchmarkAssessmentPrompt.HarnessVersion` moves to **6**, because what a model is graded on changed for narration-carrying answers. Runs before and after are not comparable on those answers.

- **The narration strip no longer stops at the first paragraph it does not recognise.** Harness version 5 claimed to remove reasoning narration before grading, and the report said so; two answers of the reference run nevertheless reached the assessor with narration intact and were docked for it, one losing 32 points on both conciseness and readability for "transport/preamble filler". The cause was structural: `BenchmarkArtifactScrubber` stripped narration only as a *prefix* and `break`ed at the first non-matching paragraph, so anything unrecognised at the front shielded every narration paragraph behind it. Q3 was shielded by a bare decoding artifact (`tsotlhe`); Q5 by `I found the relevant implementation…`, an opener the signature regex did not cover. Three changes:
  - The strip now steps over up to **2** consecutive unrecognised leading paragraphs (`MaxNarrationShieldParagraphs`) and puts them back in front of whatever survives. A paragraph is only stepped over when it is shorter than `MaxShieldParagraphChars` and carries no Markdown block structure — otherwise the rule could reach into an answer's own body, where "Let me look at this another way" is ordinary prose.
  - A leading paragraph that is a single bare word — letters only, no punctuation, no Markdown, no digits, at most `MaxOrphanTokenChars` — is removed as a decoding artifact (`StripLeadingOrphanToken`). Punctuation, emphasis markers, or a digit disqualify it, so an authored `**Yes.**` is untouched.
  - `NarrationSignatureRegex` covers `I found the`, `I located the`, and `I confirmed the` alongside the existing `I have the` / `I need the`.
- **The removal count is persisted.** `BenchmarkRunAnswer.NarrationBlockCount` (nullable `int`, migration `AddBenchmarkNarrationBlockCount`) records how many narration blocks the scrubber actually removed. The report previously inferred removal from `ScrubbedArtifactText` being non-empty — also true when only a leaked *payload* was removed — which is why it asserted a removal that had not happened. **Null means "not recorded", never zero**: runs before this version fall back to the old proxy and the report says the figure is inferred rather than measured.
- **The speed score is annotated with the numbers it was computed from.** The per-question line printed `DurationMs` beside a score computed from `ModelTimeMs` against `Target(q)`. It now prints model time and the effective target.
- **Budget pressure and grounding.** A question that stops one call short of its budget is not "exhausted" and was flagged nowhere, though it may have been cut off mid-investigation: the reference run's Q7 spent 34 of 35 and Q2 23 of 25. The Tool Usage Profile now names any question at or above 90% of its band budget, and separately names Advanced-band questions answered with one tool call or fewer — a signal about the *suite* rather than the model, since such a question is no longer testing source retrieval.
- **Cache creation tokens read `n/a` when the provider does not report them.** OpenAI reports cache reads only, and a literal `0` beside four million cache reads reads as a cache that never warmed.
- **Profile fit.** When the candidate ran at thinking level `high` or `max` against a profile whose `SpeedTargetMs` is below 30,000 ms, the Comparability block says the Speed Index is advisory for that run, and the run start dialog says so before the run. Advisory only: no gate, no scoring effect.
- **The non-monotonicity note names the cause when it is known.** When every critical-error-capped question falls in one assessed band, that band's average is depressed by the cap rather than by difficulty, and the report says which band and which questions instead of offering the generic explanation.
- **A forgone second opinion is quantified.** When no second opinion assessor was selected, the report states how many answers *would* have been re-graded, split by trigger. The reference run produced two critical errors with no second opinion selected — exactly the trigger the feature exists for — and nothing connected the two facts.
- **Critical errors are surfaced in the admin UI.** The run-detail Run Integrity Notice now names the critical-error count and the affected question numbers, and fires even when a critical error is a run's only problem. Previously the screen showed five score tiles and the sentence "2 answer(s) carry advisory flags", with the two critical errors visible only by scrolling into the per-question list. The advisory-flag line names its questions too, and the client-side diagnostics capture labels the holistic score `holistic:` rather than `final:`.

### Harness Version 7 Updates

Prompted by the same 2026-09-03 GPT-5.6 Luna run (run 7), after a second reading of its per-question
verdicts. **`BenchmarkAssessmentPrompt.HarnessVersion` moves to 7 and `ScoringMethodVersion` to 6** —
this bump *is* a scoring change: an answer containing a claim the assessor cannot adjudicate against the
rubric no longer loses Accuracy for it. **Runs 1–7 and everything after are not comparable on any answer
carrying an out-of-rubric claim.**

What run 7 showed, in its own numbers:

- **Q1** scored 60. Its rubric named an explicit critical-error condition ("Invents racial intrinsics …
  that are not in the list above"), the answer invented two, and `criticalError` still came back `false`
  — so no cap applied. What *did* apply was an Accuracy deduction whose stated evidence was that the
  claims could not be verified against the rubric. The rubric was a *partial* list; "not in the rubric"
  and "false" are different findings, and only one of them is the assessor's to make.
- **Q10** scored 60 with a verdict reading "hallucinates 'adamantium', mischaracterizes gemstone armor,
  and omits bronze" — beside `criticalError: false`. The run-level synthesis then made that same
  hallucination the headline finding of the whole run, contradicting the per-question verdict that
  actually scored.
- The report's Intelligence Index read **94** against an unweighted mean of **92**: the two weakest
  answers were also two of the easiest questions, so difficulty weighting lifted the headline *because*
  the model failed easy questions. Nothing in the report said so.

The changes:

- **Unverified claims are recorded, not deducted for.** CRITICAL INSTRUCTION 8 tells the assessor that a
  rubric is a floor rather than an exhaustive fact base: a claim it can neither confirm nor refute goes
  into a new `unverifiedClaims` array and must not reduce Accuracy. The ACCURACY level-3 anchor no longer
  reads "slight hallucinations", which invited exactly the conflation. `BenchmarkRunAnswer.UnverifiedClaimCount`
  and `UnverifiedClaimsJson` persist the result, and each claim is **quote-verified against the graded
  answer** before it is stored — a claim the assessor paraphrased rather than quoted is dropped, because
  a claim that cannot be located cannot be reviewed.
  **`UnverifiedClaimCount` is nullable: null means "the assessor was never asked", zero means "asked and
  found none".**
- **Contested verdicts are detected and flagged.** `BenchmarkVerdictConsistency` matches fabrication
  vocabulary (`hallucinat`, `fabricat`, `invent` — with a negative lookahead for `inventory`, which
  appears throughout a roguelike benchmark, `non-existent`, `no such`, `made up`) against the assessor's
  own comment and evidence. A hit alongside `criticalError: false` sets
  `BenchmarkAnswerFlags.ContestedVerdict` (32). **Nothing about a score changes**: forcing the cap
  mechanically would be worse than leaving it, because a false 25-point cap costs far more than a missed
  advisory. The flag joins the *advisory* set in `BenchmarkRunFinalizer` and deliberately **not**
  `TransportDefectFlags` — putting it there would flip healthy runs to `CompletedWithErrors`, the exact
  regression harness version 4 was written to undo.
- **Four second-opinion modes.** `BenchmarkSecondOpinionMode` on the scoring profile, overridable per run
  from the start dialog, and snapshotted onto the run as `SecondOpinionModeUsed`:

  | Mode | Coverage | Execution stages |
  |---|---|---|
  | `Off` (0) | Nothing. Equivalent to selecting no second-opinion assessor | 2 |
  | `Flagged` (1) | Per-answer triggers only | 2 |
  | `FlaggedAndOutliers` (2) | Triggers, plus a post-scoring sweep for answers far below the run's own median | **3** |
  | `All` (3) | Every answer graded twice | 2 |

  `FlaggedAndOutliers` is the **only** mode with a third stage: the sweep needs the run's median, so it
  cannot run per-answer. It is capped at four sweep re-grades per run.
- **Four per-answer triggers**, evaluated as a first-match cascade so the counts partition the answers:
  a critical error; a **contested verdict**; **unverifiable claims alongside a docked accuracy level**
  (the Q1 shape — either alone is unremarkable, but together they suggest the deduction rested on the
  thing scoring method 6 forbids deducting for); and a quality score below the profile's threshold.
  Run 7 produced none of the two that existed at the time, which is why configuring a second-opinion
  assessor would not by itself have produced a single second verdict.
- **Grader agreement is measured and always reported with its coverage.**
  `SecondOpinionGradedAnswerCount` and `SecondOpinionMeanAbsDelta` on the run; a **disagreement** is a
  gap above **15 quality points** — roughly one BARS level on the dominant dimension — or a split on
  `criticalError`. The coverage fraction travels with the figure everywhere it appears, because the two
  are not separable: under the trigger-based modes the disagreement rate is conditioned on the first
  assessor's own uncertainty and says nothing about the instrument, while the same number over every
  answer is an inter-rater agreement rate. Only `All` produces the latter. Manual trial verdicts are
  excluded from the aggregates.
- **The unweighted quality mean is computed, stored and shown.** `BenchmarkRun.UnweightedQualityIndex`
  (nullable; null for runs before this version). The report prints it under the Intelligence Index
  whenever the two differ by a point or more, with the delta the weighting produced, and the admin
  results screen shows a matching tile. Neither number replaces the other — they answer different
  questions, and the gap between them is invisible from either alone.
- **Re-assessment provenance.** An applied re-assess now records `PreviousQualityScore`,
  `ReassessedAtUtc`, the re-assessing model (`ReassessedByModelSnapshotId`) and increments `ReassessmentCount`; a second
  re-assess leaves `PreviousQualityScore` at the *original*, so the record always says what the published
  index was before anyone touched it. `ReassessedAnswerCount` surfaces on the run, and the report says on
  the answer that moved that a published index has moved since publication.
- **Trial re-assessment.** `POST .../reassess` accepts `trial: true`, which records a prospective
  assessor's verdict in the second-opinion slot and **changes no score, level, flag or index** — the run
  row is left byte-identical, including `Status` and `CompletedAtUtc`, which the non-trial path rewrites.
  Overwriting an existing *automatic* second opinion requires `replaceExistingSecondOpinion`, because
  that verdict is run evidence and an experiment must not erase evidence by accident. Trial verdicts are
  tagged `Manual` and excluded from the agreement aggregates.
- **Calibration runs.** `BenchmarkAssessorCalibration` records one non-destructive re-grading of a
  finished run by an alternative assessor: the agreement statistics, the token cost, the duration, and
  the per-answer verdicts as JSON. **It writes no `BenchmarkRunAnswer` field at all**, and it deliberately
  never reaches the Markdown report — a calibration is an experiment about *graders*, not a property of
  the run, and printing it beside the run's own figures would invite reading a calibration verdict as a
  result. It is how a prospective assessor is compared against the one in use without spending a single
  candidate call.
- **The report explains its own aggregation.** Beyond the items above: band dispersion (`range 60–97,
  lowest Q1`) so a band average cannot hide a single outlier; a second branch of the non-monotonicity
  note that tests whether removing the depressed band's weakest answer restores the ordering, and names
  the question when it does; an Assessor Findings block; an Assessor Agreement block carrying the
  conditioning caveat under every mode but `All`; budget-constrained questions marked where they also
  scored below the run's own mean, with the configuration key to raise and re-run; a Synthesis Divergence
  entry where the run-level synthesis names a hallucination the per-question verdict declined to flag;
  the second-opinion trigger named per answer; and an assessor-pairing disclosure in the Comparability
  block.
- **Turn duration leaves the synthesis prompt.** It was removed from the per-question prompt in harness
  version 2 precisely to stop the assessor penalising deliberation; leaving it in the prompt that
  produces the Holistic Assessor Score reintroduced the same bias at run level. The synthesis now
  receives each verdict's accuracy and completeness evidence and its unverified-claim count instead.
- **Two admin-UI advisories that did not exist.** The results screen marks the Speed Index advisory for a
  deliberating candidate on an interactive-latency profile — read from the *run's own* profile snapshot,
  server-side — where previously it marked only the concurrency case, and run 7 showed a bare
  `SPEED INDEX 67 / 100`. And the start dialog warns when the assessor and the second opinion share a
  provider, and when the selected assessor differs from the one that graded the suite's last completed
  run.
- **The diagnostics capture can now explain a score.** It prints all three model roles, the scoring
  constants the run was actually scored with, an `--- INTEGRITY ---` block mirroring the report's
  four-class accounting plus the advisory and agreement figures, and per-question `band`, `assessedDiff`,
  `levels`, `critical`, `tools`, `narration`, `unverified`, `secondOpinion` and `reassessed` fields.
  Every one of those was already on the DTO. `computed:` — the superseded `ComputedScore` column that
  current runs never write — is printed only where a historical run actually has it, instead of reading
  `n/a` on every capture.
- **Report timestamps are culture-invariant.** `{run.StartedAtUtc:yyyy-MM-dd HH:mm:ss}` uses the
  *culture's* time separator for `:`, so on a `fi-FI` machine — which is what these run on — every
  generated report read `19.32.00`. A report is compared across machines; its timestamps have to look
  the same on all of them.

### Harness Version 9 & Scoring Method Version 7 Updates

Prompted by the 2026-09-03 GPT-5.6 Luna run where Q1 was docked to Accuracy 4/6 (losing 28 points on the 55%-weight dimension) with `accuracyEvidence: "Matches rubric."` — a string the prompt offered as the full-level form — and Q5 exhibited the same shape at 5/6. A deduction whose stated basis names no defect is unreviewable: neither a human operator nor the harness can discern whether the awarded level or the evidence string was the error.

- **`ScoringMethodVersion` moves to 7**: A no-fault evidence string such as "Matches rubric" may accompany **level 6 only**. If an assessor awards any level below 6, the evidence string MUST specifically name what kept it below (the rubric point, the claim, or the missing element). Scores are **not comparable** with v6 on any answer graded below level 6.
- **Unevidenced deductions are detected and flagged**: `BenchmarkVerdictConsistency` checks whether deductions below level 6 carry no-fault or unevidenced strings. Violations set `BenchmarkAnswerFlags.UnevidencedDeduction` (64), routing the answer to a second reader. Advisory only; the first verdict stands and no score is altered mechanically.
- **Reporting un-triggered second opinions**: When a second-opinion assessor is configured on a run but no answer meets an activation trigger, the report explicitly states zero coverage instead of remaining silent.
- **Complete advisory-flag breakdown**: The report itemizes the complete set of advisory flags rather than truncating to a subset.
- **Claim verifier role introduced**: Factual claims the assessor could neither confirm nor refute can now be checked against the GnollHack C source code and NetHack/GnollHack wiki by a third model role equipped with read-only exploration tools. Claim verification runs as an advisory post-grading phase and does not alter scores.

### Harness Version 10 Updates

Prompted by grader behavior where assessors continued to penalize answers for unverified claims despite the prohibition introduced in harness 7, grounding accuracy deductions in the fact that a claim could not be verified against the authored rubric alone.

- **Detection of unverified-grounded deductions**: `BenchmarkVerdictConsistency` detects accuracy deductions whose evidence describes unverified status or inability to confirm against the rubric, flagging them under `UnevidencedDeduction` and routing to a second opinion.
- **Claim verifier prompt optimization**: Claim verifier queries place the prompt in the user turn to optimize instruction adherence and tool engagement across diverse frontier model providers.
- **Harness stage failure visibility**: Failures occurring during intermediate harness stages (such as second opinions or claim verifications) are clearly surfaced in the Markdown report and UI Run Integrity Notice rather than failing silently.
- **Live mid-run progress updates**: Progress statistics update dynamically throughout execution.

### Harness Version 11 Updates

Prompted by the 2026-09-04 GPT-5.6 Luna run (run 10), which exposed critical grader fidelity and instrumentation gaps (findings F1–F8):

- **ACCURACY Omission Deduction Prohibition and Pure Detector (F1)**:
  Assessors docked the ACCURACY dimension for omitted content (such as missing racial attribute maxima or unmentioned item materials), double-penalizing the omission on both ACCURACY (55% weight) and COMPLETENESS (25% weight). The prompt (§6) now explicitly instructs: *"An omission MUST NOT reduce ACCURACY — that is what COMPLETENESS grades. Deduct from ACCURACY only for statements the answer actually makes that are false."*
  Complementing this rule, `BenchmarkVerdictConsistency.IsOmissionGroundedAccuracyDeduction` matches omission vocabulary in `accuracyEvidence` without corresponding falsehood terms, setting `BenchmarkAnswerFlags.OmissionAsAccuracy` (256). This flag joins the advisory set and routes the answer to a second reader.
- **Blind Second Opinions Eliminating Anchoring Bias (F2)**:
  Previously, `BuildSecondOpinionPrompt` revealed the first assessor's quality score, critical error flag, and qualitative comments, preceded by the biasing statement *"its verdict was severe enough that the harness asked for an independent second reading"*. This rendered `SecondOpinionMeanAbsDelta` an anchoring residual rather than an independent inter-rater metric.
  Harness 11 introduces **Blind Second Opinions** (`SecondOpinionBlind` on scoring profiles, default `true`; snapshotted on runs as `SecondOpinionBlindUsed`). In blind mode, the second assessor evaluates the answer without seeing the first assessor's scores, deductions, or comments. The prompt uses neutral trigger framing across all triggers. Agreement metrics from blind runs are **not comparable** to earlier anchored runs.
- **Prioritized Claim Verification and Trigger Cascade Integration (F3)**:
  Claim verification now executes per-answer *before* the second-opinion trigger cascade rather than at run completion.
  - New trigger: `RefutedClaim` routes an answer to a second opinion if a factual claim was refuted by the verifier.
  - New trigger: `OmissionAsAccuracy` routes answers with flagged omission-grounded accuracy deductions.
  - The `UnverifiedClaims` trigger now fires only if claims were actually refuted or indeterminate; an answer whose unverified claims were all confirmed true by the verifier no longer triggers an unnecessary second opinion.
  - Verified claim records are provided as factual context to the second reader.
- **Scope-Aware Tool Budget and Early Warning Notice (F4)**:
  Candidate models hitting tool limits were previously met with an inaccurate `"Maximum tool calls per session exceeded"` message. The refusal message is now scoped to the benchmark question: `"Tool call budget for this question is exhausted ({limit} calls). No further tool calls will run — answer now with what you have."`
  Additionally, `AgentLoopRunner` appends a proactive budget warning (`[Tool budget: N of M calls remaining for this question.]`) to tool results when remaining calls fall to $\le \max(3, 20\%)$ (emitted at most once per iteration). Blocked tool attempts are persisted as `BenchmarkRunAnswer.ToolCallsBlocked`.
- **Three Distinct Budget States (F5)**:
  Reports and analytics now distinguish between three budget conditions:
  1. **Pressured**: Answer used $\ge 90\%$ and $< 100\%$ of its allocated budget.
  2. **Budget Saturated**: Answer used exactly 100% of its budget with 0 calls refused (spent all available calls without hitting the blocking limit).
  3. **Budget Exhausted**: Answer attempted calls beyond the cap, displaying the exact number of refused calls.
- **Intelligence Index Standard Error and 95% Confidence Interval (F6)**:
  To distinguish meaningful model differences from finite item-sampling noise, the difficulty-weighted Intelligence Index now computes its standard error:
  $$\text{SE} = \frac{\sqrt{\sum w_i^2 (q_i - \hat{I})^2 \cdot \frac{n}{n-1}}}{\sum w_i}$$
  Reports, admin UI score tiles, and DTOs report $\hat{I} \pm 1.96 \cdot \text{SE}$ (95% CI).
- **Disputed Verdicts Connected to Claim Verification (F7)**:
  In the Disputed Assessments report section and the admin UI Run Integrity Notice, disputed answers display the outcome of any claim verification conducted for those questions (supported, refuted, indeterminate counts).

### Harness Version 12 Updates

Prompted by the 2026-09-04 GPT-5.6 Luna benchmark run (run 11), which revealed eight harness grader defects (**F1–F8**) and six gaps in what the benchmark feeds back to the production chat agent (**T1–T6**):

- **Blind Second Opinion Backfill Migration (F1)**:
  Although Harness 11 introduced blind second opinions, the original migration set `defaultValue: false` and performed no data backfill, leaving existing default scoring profiles anchored (causing run 11 to still run anchored). Harness 12 adds migration `AddBenchmarkAgreementDirection` which explicitly backfills `SecondOpinionBlind = 1 WHERE IsDefault = 1`.
- **Claim Verification Raw Text Capture, Bounded JSON Re-ask, and Retry Endpoint (F2)**:
  When a claim verifier produces unparseable output, the raw text is preserved in `BenchmarkRunAnswer.ClaimVerificationRawText` and surfaced in diagnostics and answer details. If JSON extraction fails, the parser issues a bounded one-turn follow-up requesting only JSON before flagging failure. A dedicated recovery endpoint (`POST /api/admin/benchmark/runs/{id}/retry-claim-verification`) allows retrying failed claim verifications without regrading the entire run.
- **Shared Robust BenchmarkJsonExtractor (F3)**:
  A shared `BenchmarkJsonExtractor` utility replaces fragmented and brittle JSON parsers across candidate answers, per-question assessments, final synthesis, and claim verification using a deterministic 4-stage bounded search (direct parse, markdown fenced code blocks, brace-matching scanner, and trimmed object boundary detection).
- **Substitution Regex Guard in Verdict Consistency (F4)**:
  `BenchmarkVerdictConsistency.IsOmissionGroundedAccuracyDeduction` was augmented with a substitution regex guard (`\binstead\s+of\b`, `\brather\s+than\b`, `\bin\s+place\s+of\b`, `\bnot\s+X\s+but\s+Y\b`) to prevent false-positive `OmissionAsAccuracy` flags when an assessor notes that the candidate stated one thing instead of the correct fact (which is a genuine accuracy finding, not an omission).
- **Assessor Agreement Signed Delta and Critical-Error Splits (F5)**:
  The direction of grader disagreement is now tracked alongside absolute magnitude. `BenchmarkRun.SecondOpinionMeanSignedDelta` records whether the second assessor was systematically harsher (negative) or more lenient (positive) than the primary grader. The Run Integrity Notice and report highlight critical-error splits (questions where one grader called a critical error and the other did not).
- **Synthesis Awareness of Refuted Claims and Second Opinions (F6)**:
  `BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt` now feeds refuted claim citations and second-opinion scores into the holistic synthesis prompt with a strict instruction: a run containing refuted claims or critical-error splits must NOT be described as free of factual errors in the synthesis prose.
- **Report §5 Formatting Repairs (F7)**:
  Repaired three formatting issues in §5 of the benchmark report: omission question lists now render properly, disputed assessment tables align correctly, and unverified claim counts format consistently.
- **Same-Model Assessor Advisory (F8)**:
  When the second-opinion assessor and claim verifier share the same model configuration, an advisory callout is rendered noting that independent validation is compromised by shared model biases.
- **Candidate System Prompt Configuration Snapshot (T1)**:
  The candidate answers under the production chat prompt (`ChatService.BuildSystemPrompt`). `BenchmarkCandidatePromptOptions` snapshots the exact prompt arguments (`verboseMode`, `hasGameSnapshot`, `spoilerFree`, etc.) into `BenchmarkRun.CandidatePromptOptionsJson`, displayed in the report manifest (*Chat Prompt Under Test*) and UI diagnostics.
- **Response Style Control in Start Dialog (T2)**:
  The benchmark start dialog now features a **Response Style (candidate prompt)** selector (Concise vs. Detailed). Selecting Detailed sets `verboseMode: true`, allowing operators to determine whether a Completeness deficit is caused by the model or the concise prompt instruction.
- **Tool Routing Analysis (T3)**:
  `BenchmarkChatTransfer` aggregates tool calls into five functional families (Source Code, Wiki, Structured Lookup, Knowledge Base, Other), analyzes family shares by difficulty band, computes Pearson correlation ($r$) of source tool share against latency and quality score, highlights knowledge base under-use, and includes caveats noting that tool execution order is not recorded. **This is true of every run through harness 16.** From harness 17 a run whose answers carry per-call rows can read ordering directly from them, and the report's own caveat becomes conditional on that — see **Harness Version 17 Updates**.
- **Response-Style Conflict Advisory (T4)**:
  A deterministic predicate detects when a concise-prompt candidate (`verboseMode: false`) scores Completeness as its lowest dimension with a gap $\ge 13$ points below Accuracy, printing a dedicated advisory explaining the prompt-rubric tension.
- **Knowledge Base Gaps in Suite Health (T5)**:
  Refuted factual claims across benchmark runs are aggregated by `BenchmarkRubricGapDetector` and surfaced in the Suite Health panel as candidate topics for new knowledge base articles, complete with source citations and question references.
- **Benchmark-to-Chat Transfer Framework and Project Skill (T6)**:
  Documentation and the mandatory project skill `server_benchmark_to_chat_transfer` codify the disciplined protocol for translating benchmark empirical findings into production chat system prompt and routing improvements.
- **Instrument Fingerprinting**:
  To guarantee exact reproducibility and detect drift in the evaluation instrument between benchmark runs, the harness computes and persists cryptographic hashes:
  - `CandidateSystemPromptSha256`: Lower-case hex SHA-256 of the exact candidate system prompt string.
  - `CandidateSystemPromptText`: Full prompt text persisted when `Benchmark:StoreSystemPromptText` is `true` (default), enabling exact diffs against historical runs.
  - `ToolGuidesSha256`: SHA-256 across sorted `(relative path, file SHA-256)` pairs in `Overseer/ToolGuides/`.
  - `KnowledgeBaseHeadSha`: Git HEAD commit SHA of the knowledge base repository.
  - `WikiHeadSha`: Git HEAD commit SHA of the GnollHack wiki repository (`WikiPath`). Recorded from harness 16.
  - `SourceCodeHeadSha`: Git HEAD commit SHA of the GnollHack source repository (`SourceCodePath`). Recorded from harness 16.
  Two runs represent an exact reproduction of the evaluation instrument only when `CandidateSystemPromptSha256`, `ToolGuidesSha256`, and `KnowledgeBaseHeadSha` match. `WikiHeadSha` and `SourceCodeHeadSha` are **provenance, not comparability keys** — see **Harness Version 16 Updates**.
- **Contested-Verdict Adjudication (H1)**:
  When the primary assessor and second-opinion assessor split on `CriticalError` or when an answer carries `BenchmarkAnswerFlags.ContestedVerdict`, the answer is automatically selected for claim verification (`RunClaimVerificationAsync`), even if `UnverifiedClaimCount == 0`. The verifier receives a disputed verdict prompt testing both the candidate's claims and the assessor's counter-claims against source code facts. Reports summarize `Critical Errors: {confirmed} confirmed, {contested} contested` and print a **Contested-Verdict Sensitivity** line showing the Intelligence Index if contested verdicts were upheld at the second grader's score. From harness 25 a second advisory figure, **Verification-cleared Accuracy Sensitivity**, is printed beside it; see § *Harness Version 25 Updates*.
- **Estimated Harness Cost Block (H7)**:
  Under `### Harness Cost`, token pricing is resolved with fallback precedence: run snapshot → custom override (Admin System AI Configs) → catalog default (`pricing` object in model catalog JSON). The report renders an **Estimated Cost** breakdown (candidate uncached input, cached input, cache creation, and output; primary assessor; and claim verifier, plus total cost). Cache creation tokens are costed at the model's `cacheWritePerMillion` rate when published. If participating models use different currencies, per-role costs are printed but the total is suppressed with an explanatory note. If any participating model lacks pricing, an advisory notice names the specific role(s) lacking pricing and indicates where to configure custom rates or catalog entries, ensuring no misleading partial totals are displayed. A provenance line distinguishes catalog rates (with `asOf` date) from custom overrides.
  > As of harness 15 the breakdown carries **five** peer roles — candidate, assessor, second opinion, claim verifier, synthesis — plus a Grading subtotal, not the three named above. See **Harness Version 15 Updates**.
- **Heading-Scoped Wiki Snippet Retrieval (T8)**:
  `wiki_search` returns heading-scoped excerpts (`WikiSnippetExtractor`) scored by distinct query-term frequency with 10× heading weighting, rather than concatenating whole articles. Each hit is bounded by `Tools:wiki_search:PerResultChars` (default 2,500) and includes omission markers (`[article: {filename} — {n} further section(s) omitted; use wiki_view for the full text]`) or complete markers, preventing large articles from evicting later search hits. Default result count is raised to 5 (`Tools:wiki_search:MaxResults`).

### Harness Version 13 Updates

Prompted by the 2026-09-05 GPT-5.6 Luna benchmark run (run 13), which produced five harness findings (**H1–H5**) and one suite finding about per-question resource caps (**S2**).

- **Model Calls in the Report and Diagnostics (H1)**:
  `BenchmarkRunAnswer.ModelCallCount` has been persisted since harness 11 but appeared in no artifact an analyst reads, so input-token growth could not be attributed: many small calls and a few large-context calls produce the same total and call for opposite responses. § 2 of the report now carries **Model Calls** (total, mean, and the question with the maximum) and **Input Tokens per Model Call**; § 3's per-question `Tool Budget` line carries `model calls: {n}`; and the copyable diagnostics carry `modelCalls=` on each `[Qn]` line plus a run-level model-call total under `--- TOKENS ---`.
- **Instrument Fingerprint in the Run History (H2)**:
  The three instrument hashes now travel on the run **summary** DTO, not only the detail. The run list shows the first eight hex characters of `CandidateSystemPromptSha256` per row, and badges a row **INSTRUMENT CHANGED** when its triple differs from the next older completed run of the same suite, with a tooltip naming which hash moved. Comparison is client-side over the already-loaded history; no new endpoint. This is the fact that tells an operator at a glance whether two runs form a reproduction — the rule the report already states but the list could not support.
- **One Tool-Family Classifier (H3)**:
  The tool-family counts and the zero-knowledge-base answer count are computed once on the server by `BenchmarkChatTransfer.ClassifyTool` and projected on the run detail DTO. The diagnostics builder reads them instead of its own hard-coded copy of the tool-name lists, which drifted silently every time a tool was added; the client-side loop survives only as a fallback for run details served before the projection existed. The knowledge-base line is also qualified to match the report: a game-mechanics suite making no knowledge-base calls is prompt compliance, not under-use.
- **Assessor Agreement Coverage Advisory (H4)**:
  The Assessor Agreement card carries the same `*` advisory marker the Speed Index card uses whenever the second-opinion mode is not `All` **or** fewer than five answers were graded twice. At n = 1 a displayed `0.0` is the arithmetic of a single point and reads as strong agreement; the tooltip states the coverage and that the figure is conditioned on the first assessor's own uncertainty rather than an unbiased agreement rate.
- **Estimated Cost Breakdown (H5)**:
  The Estimated Cost card gains an interest-triggered tooltip carrying the candidate, assessor and claim-verifier shares of the total. On run 13 this surfaces what the single figure hides: the candidate was 28 % of the cost and grading plus verification 72 %.
- **Unbanded Per-Question Resource Caps (S2)**:
  Run 13 had two Intermediate questions stop at 32 of 35 tool calls while running below the run's own mean, so the band was binding on questions a production chat session would have let run. The three resource caps are now **flat**: `ToolCallBudget` 45, `ToolIterations` 22, `TotalModelCalls` 28 — the Advanced band's figures, for every question. Their configuration keys became plain values (`Benchmark:ToolCallBudget`, not `Benchmark:ToolCallBudget:{Band}`), and the banded sections were **deleted**: a .NET configuration key cannot be both a value and a section, and `GetValue<int>` on a key that has children returns `0`, so a leftover banded key makes the flat read fall through to the compiled default and silently ignores the operator's override.
  `QuestionTimeoutSeconds` **stays banded** at 420 / 600 / 720. It is pinned to the speed-score floor by an invariant `BenchmarkScoringTests` asserts: the binding case inside a band is its lowest difficulty, which has the smallest speed target and so the earliest floor, and a flat 720 s would put the Simple band's floor about 300 s inside the timeout and flatten the Speed Index.
  **Comparability**: runs from harness 13 on are **not** strictly comparable with runs 1–13 on Completeness, because Simple and Intermediate questions now get materially larger allowances. Historical runs keep their own per-answer `ToolCallBudgetUsed` values, and the report still prints the joined `25 / 35 / 45 (per difficulty band)` form for them.
- **Long-Context, Service-Tier and Scheduled Pricing in Run Costing**:
  A model catalog entry may now carry a `longContext` rate card (a second set of absolute rates above a per-request prompt-token threshold), `serviceTierMultipliers` (a scalar per served service tier), and a `scheduledChange` (an announced future price change). Run costing is affected in three ways:
  - The candidate's long-context portion is bucketed **per model call** at answer time — never from an answer's summed tokens, which an agentic answer pushes over any threshold routinely while no single request comes close — and persisted in `BenchmarkRunAnswer.LongContext*Tokens` and `BenchmarkRun.TotalLongContext*Tokens`, because benchmark cost is recomputed from stored totals rather than snapshotted.
  - Candidate costing charges the long-context portion at its own card and the remainder at the base card, then scales by the tier the provider actually **served** (the modal non-null `ActualServiceTierUsed`), never the requested one. Assessor and claim-verifier costing stays flat: no per-call usage is recorded for either role.
    > As of harness 15 this is true of **per-call** usage only — both roles carry cache-read and cache-creation **aggregates** (`TotalAssessmentCacheReadTokens`/`CacheCreationTokens`, `TotalClaimVerificationCacheReadTokens`/`CacheCreationTokens`). See **Harness Version 15 Updates**.
  - `PricingSnapshotJson` records `longContext` and `serviceTierMultipliers` beside the already-resolved rates. It deliberately does **not** record `scheduledChange`: the snapshot's job is to record what applied when the run started, so replaying it must never re-evaluate a date.
  Under `### Harness Cost` the report prints a **Long-context surcharge** line and a **Service tier** line, each only when it applies — an absent tier is omitted rather than shown as 1.0×, so a reader never has to tell "no surcharge" from "a surcharge of zero". Runs recorded before this existed have zero long-context tokens and recost to exactly what they did before.

**Deferred, with reasons:**
- **H6 — a deliberative-latency scoring profile.** The Speed Index remains structurally advisory whenever a candidate running at a high thinking level is measured against an interactive-latency target. Recording the mismatch (harness 12) is not the same as scoring the candidate against a target that fits it.
- **T11 — a partial-retrieval clause in `Overseer/ToolGuides/_policy.md`.** Confident fabrication under *partial* retrieval failure has been observed twice (run 12 Q12, run 13 Q1), but the two runs differ on `ToolGuidesSha256` — T8 moved it — so for a tool-guide-sensitive finding they are a controlled pair, not a reproduction. Awaiting run 14.

### Harness Version 14 & Scoring Method Version 8 Updates

Prompted by the 2026-09-06 GPT-5.6 Luna benchmark run (run 14). The run reproduced T16 for the fourth time and left the harness unable to say whether a one-run result would survive a re-run at all, so the largest change here is not a fix but an instrument: **replicate sets**.

> [!IMPORTANT]
> **Scoring method version 8 breaks comparability.** Runs before this version are **not** comparable with runs after it on any answer graded below level 6, because two grading rules changed. The **default scoring profile also changed in place** — see below — which resets comparability a second time. Runs 11–14 keep their value as observations and cease to be reproduction halves.

- **Synthesis Accuracy Divergence (scoring method 8)**:
  Run 14's final synthesis described the run as free of factual errors while two per-question verdicts (Q11, Q14) carried accuracy deductions whose evidence named a concrete false assertion. Nothing in scoring method 7 caught this: the guardrail covered refuted claims and critical-error splits, and neither applied.
  Two additions close it. `BenchmarkVerdictConsistency.SynthesisClaimsNoFactualErrors` matches the no-factual-errors family **sentence-scoped, never paragraph-scoped** — the same discipline `FabricationRegex` already keeps, because a paragraph-scoped match accuses a clean verdict. `AnswersWithNamedAccuracyDefects` selects verdicts with `AccuracyLevel < 6` whose `AccuracyEvidence` is non-empty and **is not full-level boilerplate** (`"Matches rubric."` and its variants); that exclusion is what keeps it from firing on every level-5 answer of a strong run.
  The report renders a **Synthesis Accuracy Divergence** advisory beside the existing Synthesis Divergence block. Like its neighbour it names the questions, states that the per-question verdict is what scored, and **changes no score**.
  `BuildFinalSynthesisPrompt`'s CRITICAL INSTRUCTION 2 was widened to match: the prohibition now covers **any answer carrying an accuracy deduction whose evidence names a defect**, those questions must be named in `weaknesses`, and the affected question blocks carry an explicit `Accuracy defect recorded: yes` marker so the constraint is visible per question rather than only in the header.
- **Completeness scope is now a grading rule (scoring method 8)**:
  The COMPLETENESS section states the precedence rule outright — *the question defines the scope; a rubric point the question did not ask for is not an omission and must not lower the level* — and requires the assessor to record such a point in `completenessEvidence` prefixed `OUT-OF-SCOPE:`. The parser sets `BenchmarkRunAnswer.CompletenessOutOfScope` from that marker; a missing marker is the normal case and never an error, and a malformed one does not throw.
  The report prints **Out-of-scope completeness deductions: N (Qx, Qy)** under Dimensional Score Averages. This counter exists to keep the rule honest: it measures exactly how much of any Completeness movement the rule itself can explain. **If Completeness rises by more than the out-of-scope count accounts for, the rule has changed grader behaviour beyond its remit and must be revisited** — it is the instrument's measurable share of the persistent Accuracy-to-Completeness gap, not a licence to flatter the model.
- **`FlaggedPlusSample`, and the default profile's changed grading regime**:
  `BenchmarkSecondOpinionMode` gains `FlaggedPlusSample = 4`: `Flagged`, plus a **deterministic** top-up to `BenchmarkScoringProfile.SecondOpinionMinimumSample`, taking the lowest quality score first with ties broken by ascending order index, so the same data selects the same answers on every run. It runs in two stages like `Flagged`, with no post-scoring sweep. The achieved count is persisted as `BenchmarkRun.SecondOpinionSampleCountUsed`, which may fall short of the target when fewer answers exist, and is meaningless under any other mode.
  What it yields is a *conditioned-plus-sample* agreement rate. It is **still not** the unbiased rate `All` gives, because the flagged half remains conditioned on the first assessor's own uncertainty. It is a cheaper partial measurement, not a substitute.
  The **Standard Intelligence Index (Default)** profile now uses this mode with a minimum sample of 4. Every other profile keeps `SecondOpinionMinimumSample = 0`, which is behaviour-identical to before.
  > The column defaults to `0` and the default profile is set to `4` by an **explicit data step in the migration**, never by a column default. A non-zero column default would have silently changed the grading regime of every profile in the database — including ones created for unrelated experiments — with nothing in any later report saying so. That is the run-11 F1 defect, and this shape is the lesson from it.
- **Claim Verification Yield and an optional verifier token budget**:
  The Harness Cost section gains a **Claim Verification Yield** line: claims checked, supported, refuted, indeterminate, verifier cost, cost per claim, and the verifier's share of run cost. On run 14 that reads *10 claims, 0 refuted, $1.70, $0.17 per claim, 67 % of run cost* — a majority of the run's spend buying no refutation.
  `Benchmark:ClaimVerificationInputTokenBudget` (**default `0` = unlimited**) caps verifier input tokens per run deterministically. When it is exhausted the remaining answers are marked `NotChecked` and the report says so **apart from a verifier failure**: no call was made, so it is not a failure, and the report must not read as though the verifier broke. The budget is seeded from what the run has already spent, so a second pass cannot spend it twice. It is deliberately not the recommended first lever — choosing a cheaper verifier is.
- **Input-token concentration (T18)**:
  The report carries each question's input tokens as a **share of the run total**, and a run-level line naming the top three answers and their combined share (run 14: Q18, Q16, Q13 — 50.8 %). `BenchmarkRunAnswerDto.InputTokenShare` is **computed** from the stored per-answer tokens and the run total, never a stored column.
  This is measurement rather than instruction. Input-token cost is driven by **model-call count**, not tool-call count — the three concentrated answers ran 22–23 model calls each — so the existing batching policy is working, and adding to the chat prompt on the strength of it would be overfitting.
- **Narration is prompt-compliant in production chat (H5)**:
  The § 5 Issues narration line now states that narrating a lookup is what `Overseer/ToolGuides/_policy.md` **asks** the production chat agent to do (*"Briefly tell the player what you're looking up when using a tool"*), and that the benchmark strips it only so the graded text is the answer itself. Without that sentence a future reader would port the scrubber into chat and remove behaviour the prompt deliberately requires.

### Scoring Method Version 9 Updates

Prompted by the 2026-09-07 `gemini-3.5-flash-lite` run (run 22), where Readability was the weakest dimension on several answers whose only stated fault was that the rubric had suggested a different layout.

> [!IMPORTANT]
> **Scoring method version 9 breaks comparability on Readability.** Runs graded under v8 and earlier are **not** comparable with v9 runs on that dimension. Accuracy, Completeness and Conciseness are unaffected.

- **A rubric FORM criterion is no longer a Readability criterion (scoring method 9)**:
  The READABILITY section states it outright — *the level anchors are the whole of this dimension; an answer that meets an anchor meets it regardless of whether the rubric proposed a different presentation* — and requires the assessor to record such a suggestion in `readabilityEvidence` prefixed `FORM:`. The parser sets `BenchmarkRunAnswer.ReadabilityFormOnly` from that marker.
  The anchors already name bullet points and clean headings as the level-5 form, so a rubric FORM criterion was a second, unstated scale grading the same dimension. The suite grades the **production chat system prompt**, which asks for concise prose: a rubric asking for a comparison table therefore docked the candidate for obeying its own system prompt.
  The marker is matched **anchored to the start** of the evidence string, unlike `OUT-OF-SCOPE:` — `form` is an ordinary word, and `readabilityEvidence` carries the marker and nothing else. A missing marker is the normal case and never an error, and a malformed one does not throw.
  The counter this feeds serves the same purpose as the out-of-scope counter: it measures exactly how much of any Readability movement the rule itself can explain. **If Readability rises by more than the FORM count accounts for, the rule has changed grader behaviour beyond its remit and must be revisited.**
- **The synthesis accuracy-defect marker no longer fires on denials (scoring method 9)**:
  `BenchmarkVerdictConsistency.NamesAnAccuracyDefect` excluded only the anchored full-level boilerplate (`"Matches rubric."`), so a sentence-form denial such as *"No factual errors; the answer matches every rubric point"* was printed into the synthesis prompt as `Accuracy defect recorded: yes` and pulled a clean question into the run's stated weaknesses. The denial is now detected unanchored, and is itself disqualified whenever the same evidence names a falsehood, names an omission, or concedes one after a conjunction (`but`, `however`, `although`, …) — so *"no factual errors, but states the level as 40 when it is 25"* still counts as a defect.

### Scoring Method Version 10 Updates

Prompted by the 2026-09-08 `gemini-3.1-pro-preview` runs (runs 24 and 25), where a candidate that thought for 6½ minutes and returned no text contributed *nil* to the Intelligence Index rather than *zero*. Under v9 such an answer was classified `TransportDefect` — "unrecoverable; excluded or invalid" — so a model could improve its index by not answering.

> [!IMPORTANT]
> **Scoring method version 10 breaks comparability whenever either run contains an unanswered question.** Runs graded under v9 and earlier are **not** comparable with v10 runs on the Intelligence Index, the Raw Quality Index, the unweighted mean or the standard error. A v9 run excluded such a question; a v10 run scores it 0. Runs in which every question was answered are unaffected in value, though not in provenance. The **Speed Index and the run status are unaffected**. Stored indices are not recomputed — past runs keep the numbers their own reports state.

- **An unanswered question is scored 0 (scoring method 10)**:
  An answer is *unanswered* when the model ended its turn normally — the provider's own finish reason is one of `stop`, `end_turn`, `completed`, `complete`, `stop_sequence` — and produced no answer text. It is scored **0** and enters the Intelligence Index, the Raw Quality Index and the unweighted mean, weighted by its authored band's fallback difficulty, since no grader read it and it therefore has no assessed difficulty. `BenchmarkRun.DifficultyFallbackUsed` records that a fallback weight was used, which keeps the report's claim about independent assessment honest. The zero **is** the penalty: an unanswered question is not additionally flagged as a critical error.
- **It does not enter the Speed Index**:
  No `SpeedScore` is written, so the speed aggregate excludes it under both versions. An answer that does not exist has no latency worth scoring, and charging one failure on two supposedly orthogonal axes would punish it twice.
- **Attribution and severity are independent**:
  Such an answer is classified `BenchmarkAnswerIntegrity.Unanswered`, disjoint from `TransportDefect`, so the integrity breakdown attributes the *cause* correctly. The run status still reports **`CompletedWithErrors`**: an empty answer is an error whatever produced it, and the two buckets differ on cause, not on severity. The invariant the breakdown asserts is now *clean + transport defects + recovered + harness limits + unanswered = the question count*.
- **An answer destroyed in transit is still excluded**:
  That is not the candidate's fault. A truncated answer's finish reason is `MAX_TOKENS`, which is not a normal stop, so it remains a transport defect. The whole distinction rests on `BenchmarkRunAnswer.ProviderFinishReason`, so answers recorded before that column existed — where it is null — keep the old classification. A null or unrecognised reason never silently reclassifies an answer.
- **No grader is paid to read an empty string**:
  The per-question assessment step returns early on an answer with no text. Where the model produced the emptiness, the answer is marked `Scored` with a comment saying it was scored by rule rather than by grader; otherwise the assessment is marked `Failed` with a plain reason. Previously the assessor was invoked, returned nothing by construction, and its empty verdict was recorded as a harness stage failure — a guaranteed one, paid for at assessor rates. The dimensional levels are left null either way, so the report's dimensional averages stay averages over answers a grader actually read.
- **One predicate, every scoring site**:
  `BenchmarkRunFinalizer.CountsTowardQualityIndex` is the single definition of which answers contribute to a quality index, and the six sites that recompute one — the finaliser, the run detail DTO's `RawQualityIndex`, the report's own raw index, the group statistics, the per-item analysis and `RescoreRunAsync` — all use it. They have to be over the same item set, or two numbers describing one run disagree with no way to tell which is right. Sites that count answers, latency, tokens or coverage keep their `Status == Ok` filters: an answer that does not exist has none of those.
- **A rescore now writes all three quality figures**:
  `RescoreRunAsync` wrote `QualityIndex` and `SpeedIndex` and left `UnweightedQualityIndex` and `QualityIndexStandardError` at the previous profile's values, so a rescored run's report printed an Intelligence Index from one profile beside an unweighted mean and a standard error from another. Since the weighted-versus-unweighted gap is treated as meaningful, all three now move together.
- **The report says so plainly**, in five places: an **Unanswered Questions** line in § 2 naming the question numbers, an **Unanswered** bucket in Run Integrity, a per-question `0 / 100` with a NO ANSWER marker and the provider finish reason, a corrected Aggregation Formulas note in § 4, and `*(Scored 0: no answer produced)*` rather than `*(Note: Excluded from scoring)*` in § 5.

`BenchmarkAssessmentPrompt.HarnessVersion` stays `"12"`: the instrument — prompt, tool guides, knowledge base — does not move, and only the scoring rules change.

### Harness Version 15 Updates

Prompted by an audit of the Harness Cost section against the per-role token columns it reads, which turned up an Anthropic cache-creation double-charge (**H1**) and a `HarnessVersion` constant that had gone stale two versions ago (**H5**).

- **Per-role cost tracking**:
  `### Harness Cost` now tracks five peer roles instead of three: **Candidate**, **Assessor** (per-question assessments only), **Second Opinion**, **Claim Verifier**, and **Synthesis** (the final holistic pass, priced on the assessor's own card — it is the same configuration, not a separate model choice). A **Grading subtotal** line reports `Assessor + Second Opinion + Claim Verifier + Synthesis` and its share of the run's total cost, computed once by `ModelPricingService.ComputeRunRoleCosts` and consumed identically by the report and the admin cost-panel UI, so the two cannot disagree about what the subtotal includes. `BenchmarkRun` gained matching `TotalSecondOpinion*` and `TotalSynthesis*` columns (input, output, cache read, cache creation, duration), and the assessor and claim-verifier roles gained cache-read/cache-creation aggregate columns of their own. A role's cache figures print beside its in/out tokens only when non-zero, so a run with no cache activity for a role reads exactly as it did before this section grew a cache breakdown. A run recorded before harness 15 prints a notice: *"Recorded before per-role cost tracking: the second opinion's spend is inside the assessor line, and the final synthesis is not counted at all."*
- **The `EstimatedAssessorCost` semantic change**:
  Before harness 15, second-opinion spend was pooled into the same answer-level fields the primary per-question assessment used (`BenchmarkRunAnswer.AssessmentInputTokens`/`AssessmentOutputTokens`), so the Assessor cost line silently included it. From harness 15, second opinion has its own columns and its own peer line, so **`EstimatedAssessorCost` now means the per-question assessments alone** — the final synthesis was never in it and still is not; synthesis is its own peer line, priced on the assessor's card. A run's Assessor cost figure under harness 15 is therefore not directly comparable to the same field on an older run, which may have folded second-opinion spend into it.
- **H6 — pipelining vs. measured overlap**:
  The report's pipelining disclaimer ("assessment time overlaps the candidate's and the two do not sum to the wall time") only describes a run with question-level concurrency (`MaxParallelQuestionsUsed > 1`). A sequential run has no such overlap to disclaim, so it now prints a **measured overlap** instead: wall clock (`TotalDurationMs`) minus the summed candidate, assessment, second-opinion, claim-verification and synthesis durations. Synthesis was the largest missing term in that sum before this round; on one measured sequential run, counting it dropped the residual to 17 seconds.
- **H1 — Anthropic cache-creation tokens were charged twice**:
  The per-call costing path billed a call's cache-creation tokens at the cache-write rate and then billed the same tokens again at the base input rate, because it partitioned the prompt using `TokenUsageReport.UncachedInputTokens`, which still contains the cache-creation tokens. `TokenUsageReport` gained `BillableUncachedInputTokens` (`TotalPromptTokens − CacheReadTokens − CacheCreationTokens`), and every caller that bills a call now uses it instead. Only Anthropic calls carrying cache-creation tokens were affected — Google and OpenAI report no cache-creation tokens on any run on record, so their recorded costs are unchanged by this fix.
  Every past Anthropic run's recorded cost drops when its report is regenerated. On run 27 the total moves **$4.40 → $3.82** and the candidate line **$2.00 → $1.42** (precisely $1.414233). This is a correction to a costing bug, not a price change: it applies uniformly to every stored run carrying Anthropic cache-creation tokens, and it takes effect the moment a report is (re)built from the stored token totals — no migration touches the totals themselves (see "Persisted chat cost is not backfilled" below for why the same fix cannot reach chat's already-written cost figures).
- **H5 — the `HarnessVersion` constant was stale**:
  `BenchmarkAssessmentPrompt.HarnessVersion` read `"12"` from harness 12 through harness 14, while this document described harness 13 and 14 as landed, with runs recorded under each. It is now `"15"`, with the v13, v14 and v15 deltas recorded in its own XML doc comment history. **Nothing repairs the mislabelled rows**: every run started under harness 13 or 14 was stamped `HarnessVersion = "12"`, and that column is a snapshot of what the constant read at run time, not a live fact the harness can retroactively correct. A comparison keying on `HarnessVersion` should treat `"12"` as "12, 13, or 14" for any run recorded in that window; the three are told apart, where it matters, by `CandidateSystemPromptSha256`, `ToolGuidesSha256` and `KnowledgeBaseHeadSha` instead. `ScoringMethodVersion` is untouched by this round (still 10): no grading rule changed, and resetting it without one would have opened a second, unnecessary comparability break.
- **`get_item_stats` macro parsing repair**:
  `GameDataParser`, which the `get_item_stats` tool uses to read macro-defined item stat blocks out of the GnollHack source, now strips `//` and `/* */` comments and treats string and character literals as opaque before splitting a macro body into arguments — a comma or parenthesis inside a literal, or inside a comment, no longer ends an argument early or shifts every later one. Backslash-continued `#define` directives are joined into one logical line before parsing, including when the continuation falls inside the parameter list rather than only the body, which a macro spanning several source lines needs in order to parse at all.
- **Not in scope of a run's cost**:
  The one-off difficulty assessment the harness runs when a suite's questions are (re)authored is a config-level call (`BenchmarkService.RecordJobUsageAsync`, via `SystemAiConfigService.RecordUsageAsync` with `roleContext: 4`) that happens outside any `BenchmarkRun` and is billed to the assessor configuration directly. It is correctly absent from every run's Harness Cost section — "the cost of running the whole benchmark suite" invites the reading that it should be there, and it should not be: it is a property of the suite's authoring, not of any one run that grades against it.
- **Persisted chat cost is not backfilled**:
  H1 fixes cost computed **at report or dashboard render time** from stored token totals. It does nothing for `SystemAiUsageLog` rows already written for Anthropic turns with cache-creation tokens before this fix, whose stored cost figures keep the doubled-charge convention. Admin cost totals that mix rows from before and after this fix therefore mix two different costing conventions for the same token counts, and there is no reconciling them after the fact: `SystemAiConfigService.RecordUsageAsync`'s `inputTokens` parameter is `TokenUsageReport.TotalPromptTokens` — the **inclusive** total, not `BillableUncachedInputTokens` — stored as `SystemAiUsageLog.InputTokens` alongside `CacheCreationInputTokens`. A backfill would need to know, per historical turn, how many of those input tokens were cache-creation tokens *at billing time*; the value stored is not that split, and no historical record carries per-turn cache-creation counts separately once the row is written. The gap is **impossible to close, not merely deferred**. Any future costing of these stored rows must apply the same disjoint partition (`inputTokens − cacheReadTokens − cacheCreationTokens`) itself, rather than trusting the stored `InputTokens` to already exclude cache-creation tokens.

### Harness Version 16 Updates

Prompted by the observation that a benchmark run grades the production chat system prompt with the production tool registry behind it, so every tool call it makes is a live sample of what a real chat session does — and that for the two largest corpora a run could not say which revision it had read. A "not found" from a source or wiki tool was indistinguishable from a corpus that was stale, incomplete, or excluded by an indexer limit.

- **Two corpus fingerprints**:
  `BenchmarkRun` gains `WikiHeadSha` and `SourceCodeHeadSha` — the Git HEAD commit SHA of the GnollHack wiki corpus (`WikiPath`) and the GnollHack source corpus (`SourceCodePath`) at run time, stamped by `BenchmarkService.PopulateInstrumentFingerprint` beside the existing `KnowledgeBaseHeadSha` and read through `GitHelper.GetGitHeadSha`. Three of the five corpora a run can reach are now fingerprinted. A finding about a corpus can name the revision the run actually read instead of inferring it from `StartedAtUtc`.
- **They are provenance, and deliberately not comparability keys**:
  `BenchmarkComparabilityKey` treats an absent value as a real value (`NoValue = "(none)"`), and `BenchmarkComparabilityKeyKind.Instrument` holds that exactly one differing instrument key is Tier C — the deliberate single-variable experiment — while two or more is an uncontrolled comparison that drops below Tier B. Every historical run is null on both new columns. Registering them as `Instrument` keys would therefore make **every** new run differ from **every** historical run on two keys at once, silently converting the whole accumulated run history into non-comparable data, and nothing would error. The `HarnessVersion` bump to `"16"` is the one key that marks this change; a difference in either corpus hash is a fact to investigate, not an automatic tier drop.
- **The series resume guard covers them, with no backfill**:
  `BenchmarkRunSeries` gains `FirstMemberWikiHeadSha` and `FirstMemberSourceCodeHeadSha`, and the resume guard compares all five hashes rather than three. The comparison returns early on an empty recorded value, so a series created before harness 16 reports no drift on the new keys and stays resumable. **No historical row is backfilled**, and none can be: the corpus revision a past run read is not recoverable after the fact.
- **Two corpora are still unfingerprinted, and both are reachable from a run**:
  The NetHack source corpus (`NetHackSourceCodePath`) is a Git working tree but is not fingerprinted; the NetHack wiki (`NetHackWikiPath`) is not version-controlled at all — it is generated wholesale by `Overseer/Scripts/NetHackWiki/convert_nethackwiki_dump_md.py`, so there is no revision to record. Meanwhile `nethack_wiki_search` and `nethack_wiki_view` are in `Benchmark:AllowedTools`, and every source tool accepts `repository: "nethack"`. **A NetHack-corpus finding therefore has no run-recorded provenance**, and must fall back to comparing the run's `StartedAtUtc` against the corpus on disk and stating the residual uncertainty in the finding. This is a deliberate limit rather than an oversight; a future round may add a content manifest for the generated wiki, and the columns added here are independent of it.
- **A pre-16 run reads null as "not recorded"**:
  Both new columns are nullable, and null means *not recorded* — never "no corpus" and never "unchanged". Run reports render `not recorded`, identical to the existing `KnowledgeBaseHeadSha` convention, and every drift detector treats a null on either side as no drift rather than as a change.
- **The group manifest carries one stacked `Fingerprints` column**:
  Five hashes per run is too many for five Markdown columns, so `BenchmarkGroupReportBuilder`'s manifest table collapses `Prompt SHA`, `Guides SHA` and `KB SHA` into one `Fingerprints` cell holding five `<br>`-separated `LABEL hash` entries, labelled `PROMPT`, `GUIDES`, `KB`, `WIKI`, `SRC`. The run list and the multi-run picker use the same labels, so the three surfaces read identically, and every entry carries a visible label as well as a colour so the stack stays legible in greyscale.
- **The multi-run picker's fingerprint column is no longer sortable or filterable**:
  It showed the tool guides hash alone, and sorting or filtering on one hash out of five invites reading it as *the* fingerprint. The column keeps its place and now shows all five; its sort header, its filter input and both accessors are gone. The comparability tier preview remains the authoritative check when assembling a group.
- **Tool-layer diagnostics are a documented method**:
  Three project skills — `server_tool_data_sources`, `server_tool_parameter_reference` and `server_benchmark_tool_diagnostics` — cover resolving each corpus path, what each index silently excludes, and how to tell "no access" from "no data" from "a broken tool" for any tool call in any run. `server_benchmark_to_chat_transfer` gains a fourth triage category, **Corpus / Environment Defect**, for a finding whose cause is the data a tool read rather than the model or the suite.

### Harness Version 17 Updates

Prompted by the gap harness 16 left standing: fingerprinting three of the run's five corpora still could not say what any one tool call actually asked or received, so every tool-layer finding kept resting on reconstruction from the question text and the answer's own citations, or on a scratch replay — because a benchmark answer creates no `ChatMessage` rows and `AgentRunRequest.ShowDebugLog` is hardcoded `false` at every benchmark call site. **Nothing the candidate model receives changes in this round** — no system prompt edit, no tool description change, no policy change — so **harness 16 and harness 17 scores remain directly comparable**; this round only widens what the harness records about calls the candidate already made.

- **A per-call record, one row per attempt.** `BenchmarkRunAnswerToolCall` (`GnollHackServer.Data/BenchmarkRunAnswerToolCall.cs`, migration `AddBenchmarkToolCallRecords`) is a new table, one row per tool call **attempted** during an answer's turn, cascade-deleted with the answer and indexed on `(BenchmarkRunAnswerId, SortOrder)`. `SortOrder` is the call's position in emission order across the whole turn, dense and 0-based; `BenchmarkToolCallRecorder.Build` assigns it by enumeration order rather than sorting on the source `ChatMessageToolCall.SortOrder`, because a nested sub-agent call is appended to that list after its parent completes and carries no meaningful value in that field there — sorting by it would scramble the turn rather than preserve it. `IterationIndex` is the tool round the call belongs to (`ChatMessageToolCall.BatchIndex`); `Name`, `ToolCallId`, `Status`, `QueueWaitMs`, `ExecutionMs`, `Depth` and `AgentName` (which sub-agent, if any, made the call) complete the shape. `ArgsText` and `Result` are the payloads the retention rule below prunes; `Error` is a payload too but carries no truncation flag of its own, because a tool error's diagnostic content is in its first line and a cut first line is still that. This is deliberately **not** `ChatMessageToolCall`: that record's foreign key is to a `ChatMessage`, which a benchmark answer has none of, and the chat retention sweep joins through `ChatMessage.TimestampUtc` — a benchmark row parked there would be silently invisible to it. Rows exist only for runs from harness 17 onward; an answer with no rows means the run predates the record, never that the model made no tool calls, and `BenchmarkRunAnswer.ToolCallSummary` remains the only source for those.
- **`ResultLengthChars` is the true pre-cap size, and it is never pruned.** Every row records the length the tool actually produced, before any cap and before the retention sweep, even when nothing was truncated. That is what keeps a truncated or pruned record usable: a null `Result` beside a non-zero `ResultLengthChars` means *pruned*, and a zero means *the tool returned nothing* — the same shape, opposite facts, and this column is what tells them apart.
- **The result cap is derived, not written as a literal.** `BenchmarkToolCallRecordLimits.Resolve` reads `Benchmark:ToolCallRecord:MaxArgsChars` (default 4,000) and `MaxErrorChars` (default 2,000) directly, but derives `MaxResultChars` as `Benchmark:MaxResultLength + 2,000` — 12,000 at today's configuration — instead of giving it a default of its own. `ToolExecutor` already truncates every successful tool result to `MaxResultLength` before it reaches a record, so a stored result cannot exceed the derived cap at the current configuration; a hardcoded 16,000 would either be a cap that never fires, or would silently become wrong the day an operator raises `Benchmark:MaxResultLength`. The derivation also covers a ceiling the benchmark itself never sets: `Benchmark:AllowedTools` is operator-configurable, and a sub-agent tool added to it would put that tool's results under `MaxSubAgentResultLength` (30,000) instead — reading the cap from the same value the agent context was actually given is what keeps the record honest if either figure moves, rather than a copy of one that can drift out of step with it.
- **A three-way outcome split, and the failure mode it exists to catch.** `BenchmarkToolCallRecorder.Outcomes` partitions every row into `Succeeded`, `Failed`, or `RefusedByBudget` — mutually exclusive and exhaustive, so the three sum to the row count exactly. The reason this exists: `ToolCallSummary` lists only calls that completed with no error, so a call whose JSON result exceeded the result cap — which `ToolExecutor` converts into an error rather than a success — appears in **no** tool-name count anywhere in the report. A model that repeatedly over-fetched from `source_code_search` left no trace at all in the Tool Usage Profile; the profile read as sparse rather than as wasteful. `### Tool Usage Profile` now carries a **Tool Call Outcomes** line (succeeded / failed / refused by budget) stating this split explicitly, for exactly this reason.
- **The budget-refusal wording is scope-dependent, and matching only one wording was itself a defect.** `ToolExecutor` picks between two refusal messages on whether `ToolExecutionContext.ToolBudgetScopeId` is set: unset, it emits *"Maximum tool calls per session exceeded."* — the chat wording; set, it emits *"Tool call budget for this question is exhausted…"* — the benchmark wording. Every benchmark executor sets a per-question scope, so a benchmark refusal always reads the second and never the first. `BenchmarkToolCallRecorder.IsBudgetRefusal` is the single owner of matching both, so `ToolCallSummary`'s blocked-count suffix and `Outcomes`'s refused bucket cannot disagree about what "refused" means. Matching only the session wording is what left `ToolCallSummary`'s `"(N blocked by budget)"` suffix reading **zero on every benchmark run** since per-question scoping was introduced; `BenchmarkRunAnswer.ToolCallsBlocked` was the only trustworthy signal for a refused call in the meantime, and remains the signal of record for any run before harness 17.
- **A rerun replaces its rows, never appends to them.** The rerun path deletes an answer's existing `BenchmarkRunAnswerToolCall` rows before writing the new turn's, so re-running a question a second time does not double its recorded call count.
- **One counting rule, and the comparability argument for it.** `BenchmarkChatTransfer.ToolCallCountsFor` counts an answer's per-tool-name calls from its rows when it has any, and falls back to parsing `ToolCallSummary` otherwise — every reader in this class (`AggregateToolCounts`, `AnalyzeToolRouting`, and their downstream callers in `BenchmarkReportBuilder` and `BenchmarkGroupStatistics`) goes through this one function, so a run-wide total and a per-answer share can never be drawn from two different sources. The row path counts **succeeded rows only**, grouped by name. That is deliberate, not an oversight: `ToolCallSummary` only ever listed successes, so folding the failed and refused rows into the same counts — the obvious "use the richer data" move — would inflate a harness-17 run's tool-name counts above a harness-16 run's for identical model behaviour, and every family share and correlation drawn from them would be incomparable across that boundary while looking perfectly fine. The failed and refused calls are reported on their own, from `Outcomes`, never folded into a name count.
- **The tool-ordering caveat is now conditional, and the unqualified branch has to stay.** `### Tool Usage Profile`'s *"Note on tool ordering"* used to assert flatly that ordering is not recorded. For a run with rows it now says the opposite and says why: the per-question tables in § 3 list every attempted call in emission order with the tool round it belonged to, so whether wiki tools were attempted before source code tools on a given question is read directly off that table rather than inferred from the family totals above it. For a run without rows — every run before harness 17 — the caveat is exactly the sentence it always was, because that remains exactly true of those runs: deleting it would assert of every historical report that its tool ordering can be read, which it cannot, by anyone, ever.
- **Payloads are never inlined in the report; the endpoint is the only way to read one.** § 3's new per-question table (round, tool, status, exec ms, result size) prints `ResultLengthChars`, never a character of the argument or result text — a single tool result can be tens of thousands of characters of game source, and a report is written to be pasted whole into a chat or an issue. Arguments and results are served only by `GET /api/admin/benchmark/runs/{id}/answers/{answerId}/tool-calls`, admin-authenticated, returning the rows in `SortOrder` order with full arguments and results — a separate endpoint rather than an addition to `BenchmarkRunAnswerDto`, because that DTO loads every time the run dialog opens and would multiply its size for a view almost nobody opens. `BenchmarkRunAnswerDto` itself gains only `ToolCallsSucceeded`, `ToolCallsFailed` and `ToolCallsRefused` — all **nullable, where null means "not recorded" and never zero**, following the `NarrationBlockCount` precedent. The Angular run-detail card's new **Tool calls** disclosure loads against this endpoint lazily, per answer, only when an operator opens it.
- **A 90-day retention window, keyed on the run's age, not the row's.** `ChatRetentionSettings.PruneBenchmarkToolCallResultsDays` defaults to **90** — three times chat's own `PruneToolCallResultsDays` (30), because a benchmark run stays evidence for a study longer than a chat turn stays evidence of anything. `ChatRetentionService.PruneAgedBenchmarkToolCallPayloadsAsync` is a new numbered step (step 5) in `RunFullMaintenanceAsync`'s sweep, and it nulls `ArgsText` and `Result` for every call whose **run** — via `BenchmarkRunAnswerToolCall.BenchmarkRunAnswer.BenchmarkRun.StartedAtUtc` — started before the cutoff, because the row carries no timestamp of its own: a run re-analysed long after it finished should keep its evidence until the run itself is old enough, not until an unrelated clock on the row ticks over. `Name`, `Status`, `Error`, `SortOrder`, the timings and `ResultLengthChars` are never pruned — they are a few bytes each, and they are what the aggregates and the tool-layer diagnostics actually read.
- **Run-forward, with no backfill and no exception.** Rows exist only for runs from harness 17 onward, and none can be added to an older run after the fact — the arguments and results a pre-17 answer's calls carried were never captured, by construction, and no later pass can recover what was never written. Every consumer keeps its `ToolCallSummary` path for those runs permanently. `BenchmarkAssessmentPrompt.HarnessVersion` moves to `"17"`; `ScoringMethodVersion` stays at **10**, because no grading rule changed — this round only widens what the harness records about calls the candidate already made.

### Reporting and Tool-Layer Round (2026-09-09) — No Version Bump

A remediation round drawn from run 28's analysis. It changes **what the report says** and **what
three tools return**, and it changes **no scoring rule**: `BenchmarkAssessmentPrompt.HarnessVersion`
stays at `"17"` and `ScoringMethodVersion` stays at **10**.

**That non-bump is a deliberate trade, and it is the one thing in this round worth arguing about.**
Some of these changes do alter what the candidate receives — three tool guides, `_policy.md`, the
`source_code_search` miss payload, the plain-text truncation suffix, and a `get_item_stats` result
that now carries structured values. By the standard the harness-16-to-17 note states, that is
exactly what a version bump records. Bumping it here would also mark the run that *verifies* this
round non-comparable with run 28, which is the only run these changes were motivated by, and the
verification would measure nothing. So the round is recorded through the **instrument
fingerprints** instead — `ToolGuidesSha256` and `CandidateSystemPromptSha256` both move, and the
comparability machinery treats them as provenance rather than as keys, which is precisely the
distinction that makes this possible. A future reader comparing a pre-round run with a post-round
run must therefore read the fingerprints, not the harness version, and the run registry in
`server_benchmark_to_chat_transfer` § 11 is where the two values are written down.

#### Report changes

- **The cost breakdown is computed once, in the pricing service.** `ModelPricingService` gains
  `ModelCostBreakdown` (uncached input, cache read, cache write, output, plus the long-context
  portion as a *subset* of those four, not a fifth bucket) and
  `ComputeCostBreakdownFromTotals`. `ComputeCostFromTotals` returns that breakdown's `Total`, so
  the parts and the total are the same arithmetic by construction.
  `ComputeRunRoleCostBreakdowns` produces the five roles' breakdowns and `ComputeRunRoleCosts`
  reads its totals from it. The report no longer multiplies a rate card by a token count at all.
  Two defects went with the old local arithmetic: it priced each grading role's *total* prompt
  column at the uncached input rate, so a role's printed `in:` figure could exceed its own total;
  and it modelled neither the long-context split nor the service-tier multiplier, so a candidate
  with a long-context card would have misprinted its own components.
- **All five roles are itemised**, with the same conditional shape: `uncached in` and `cached in`
  are separated only where the card publishes a distinct cached rate, since otherwise cache reads
  bill at the input rate and naming them apart would imply a saving the run did not get. The
  **Long-context surcharge** line now also states how many dollars of the candidate's total were
  billed at that card.
- **The narration-removal wording is omitted when nothing was detected.** With `reasoning bleed: 0`
  the Advisory Flags note used to claim either that the text "was removed before grading" or that
  it was "removed in 0 of 0" — both describing text that never existed.
- **Band Agreement carries a signed drift summary** before the per-question list: how many
  questions were assessed harder than authored, how many easier, and the mean signed delta against
  the authored band's midpoint (the 25 / 55 / 85 map in `BenchmarkRunFinalizer.FallbackDifficulty`).
  When every mismatch shares a direction the line says so explicitly, because assessed difficulty
  is the Intelligence Index weight — a one-directional drift moves the headline, and a list of
  per-question band changes does not show that. The Angular Band Agreement panel carries the same
  line.
- **Early terminations and near-ceiling answers are reported, beside the Harness Limits line and
  never inside it.** `TerminationReason` was persisted and rendered nowhere. Run Integrity now
  carries an **Early Terminations** line with the reason breakdown and question numbers whenever
  any answer did not end on its own, a **Near a Ceiling** line for an answer that finished within
  one step of a configured cap without the cap firing, and a per-question **Termination** line.
  `BenchmarkRunFinalizer.HasHarnessLimit` is deliberately **unchanged**: it feeds `Classify`,
  `ToolStarvedAnswerCount` and the run status, so widening it for a reporting gap would move the
  clean-answer count and the run status of every future run. A terminated answer therefore still
  classifies as `Clean` with `Harness Limits: 0`, and a test asserts that.
  The round count itself is not persisted on an answer; `ModelCallCount` stands in for it, which
  is exact for a tool-using turn because the agent loop makes one model call per round.
- **A saturated Speed Index is demoted, and from harness 28 it is also re-tuned.** When at least
  half the scored answers sit at the Speed Index ceiling, or when a deliberating candidate ran
  against an interactive-latency profile, § 2 and § 7 Final Indices both lead with **median model
  time** and mark the index advisory; the Angular score card does the same, with the index on its
  sub-line. That demotion is unchanged. What changed is that re-tuning is no longer forbidden:
  re-tuning was rejected for three rounds because `speedTargetMs`, `speedDecayK` and
  `speedDifficultyScaling` sat inside `BenchmarkScoringProfileService.CanonicalSignature`, which is
  hashed into the `ScoringProfile` comparability key, so a profile differing *only* in its speed
  target marked its runs non-comparable on every **quality** dimension as well — for a reason that
  cannot touch a quality score. Harness 28 moves the three constants out of that signature into
  `SpeedCalibrationSignature`, reached by a `SpeedCalibration` comparability key of kind
  SpeedAndCost, and then recalibrates them (below). A recalibration now degrades the **speed**
  aggregates across the boundary and leaves quality comparable, which is the treatment the
  difference always deserved. Demotion still covers the case a calibration cannot: a candidate run
  far outside the profile the run was scored against.
- **The confidence interval carries a caveat when answers were capped.** A critical-error cap
  replaces a score with 25, and that deviation enters the variance weighted by the item's assessed
  difficulty *squared*, so a run with critical errors reports a wide interval for a reason that is
  not item sampling. The formula is unchanged; the reader is told which figure to read instead —
  the **Critical Errors** count.
- **The difficulty-fallback line states a guarantee instead of implying a measurement.**
  `BenchmarkRunLauncher` refuses to launch a suite carrying any question without an assessed
  difficulty, and `BenchmarkService` coalesces the authored fallback into the stored
  `AssessedDifficulty` when it writes an answer, so `DifficultyFallbackUsed` is false on every run
  that can exist. The line now says so and names the guard. `FallbackDifficulty` itself stays: it
  is still the defensive default in `BenchmarkRunFinalizer` and the report's assessed-difficulty
  label.
- **Assessor Agreement is reported by trigger, and over the verification-uninformed subset.** A
  second opinion on an answer carrying a refuted claim is handed that refutation in its own prompt
  (`BenchmarkAssessmentPrompt`'s fact-check verification block), so its disagreement is partly the
  verifier's finding rather than an independent second reading. The section now prints the trigger
  breakdown of the covered answers, and a second agreement figure over the answers that saw no
  refuted claim. The existing pooled figures are unchanged, so no historical number changes
  meaning.
- **Diagnostics capture.** `diagnosticsModeName` switches on the `BenchmarkSecondOpinionMode` enum
  rather than on bare integers, so `FlaggedPlusSample` is named instead of reported as `unknown`,
  and a mode added later cannot regress it. The `outlier delta` figure prints only under
  `FlaggedAndOutliers`, the one trigger that reads it.

#### One ordering change, which is not presentational

`RunClaimVerificationAsync` now runs **before** the outlier sweep and the second-opinion sample
top-up, and again after them. Before, it ran only after: a flagged answer's second opinion was
handed its refutation (the per-answer path verifies before its own second opinion) while an
outlier-selected or sample-selected one was not, so the pooled agreement figure mixed two
different measurements depending on which trigger had selected the answer. Running it first makes
every second opinion in the run read the same verification state. The **second** call is what keeps
an existing behaviour: a critical-error split is one of the things that makes an answer a
verification candidate, and only a second opinion can produce one. The pass filters on
`ClaimVerificationJson` and `ClaimVerificationError` both being null, so it re-checks nothing and
returns before any model call when the two stages found no split. The second opinion is advisory
and enters no index, so no score changes.

#### Tool-layer changes

- **`get_item_stats` has a Level 1.** `ObjectsMacroResolver` is loaded from
  `SourceCodeService.ParseGameData()` on every re-index, with both `src/objects.c` and
  `include/objclass.h` — the header supplies the enum constants that the ternary conditions in
  `CHARGEDRING`, `MISCELLANEOUSITEM`, `GENERAL_TOOL`, `GENERAL_SPELLTOOL`, `CONTAINER` and
  `GENERAL_ROCK` compare against, and without it roughly sixty entries return C ternary text in
  place of a value. `GetItemStats` populates `StatsResponse<ItemStats>.Stats` from the resolution
  and keeps the raw definition beside it; on a resolve failure it falls back to the raw dump plus
  macro and struct context, with the failure reason in `message`, and never throws.
  `NetHackSourceCodeService` overrides `ParseGameData()` with a no-op, so the resolver is
  GnollHack-only without a guard.
- **The item result states its own units.** `ac_bonus` is the stored `oc_armor_class`, which
  GnollHack writes as `10 - ac`; `base_ac` is that `ac` argument; and the game negates `ac_bonus`
  into the hero's AC, so a positive bonus *lowers* AC. Magic cancellation is a stored level
  adjusted further at run time, and the spellcasting penalty's player-visible form is
  `spell_casting_penalty_percent`. All three conventions are emitted as notes with the values, not
  left in source comments — a model reading `ac_bonus: 9, base_ac: 1` cannot otherwise tell which
  number is which, and that is the confusion that produced a spurious critical error on run 28.
- **A name several object classes share is no longer silently resolved to one of them.**
  `objects.c` holds 947 entries under 904 distinct names; the resolver keeps every entry, reports
  the sharing classes in `ambiguous_object_classes`, notes which one the values came from when it
  picked for the caller, and accepts an object class to select among them.
- **Flag unions are lists.** A slot whose value is an OR of symbolic flags is returned as a list of
  those flags, matching the shape `get_monster_stats` already returns for its own flag fields.
- **`source_code_search`'s miss names a next action.** The bare 30-character
  *"No relevant source code found."* carried no near-miss information, so a model that guessed an
  identifier got an answer indistinguishable from "the game does not contain this" and its cheapest
  recovery was another guess — 20 tool rounds of it on one run-28 question. The miss now probes the
  index for near neighbours of an identifier-shaped query, states explicitly that a multi-word
  query matched no line *as a literal substring*, reports which individual terms matched which
  files, and names the `file_filter` in play. It stays short on purpose: every tool result is
  re-sent on each subsequent round of the same question, so a verbose miss would undo the saving.
- **The truncation suffix is actionable, and it is no longer a fixed-length marker.**
  `ToolExecutor`'s `... [Result truncated for length]` said nothing a model could act on, and
  `SourceCodeService`'s own *"refine your query or use source_code_view"* suffix was appended at
  character 100,000 and then cut away by the 10,000-character cap, so the model had never once
  received it. The suffix now states how many characters of how many are shown and what to do
  about it. **Consequence for diagnostics:** the stored `10033` fingerprint — 10,000 characters
  plus a 33-character marker — no longer identifies a truncated result. The marker is
  `... [Truncated: showing {shown} of {total} characters. …]`, 107 characters plus the two figures'
  digits, so a truncated plain-text result now stores roughly 10,117. Match on the `[Truncated:`
  prefix rather than on a length.
  `MaxSourceResultLength` is left at 100,000 and remains dead; raising it fixes nothing, and
  raising `MaxResultLength` would raise input-token cost on every subsequent round of every
  question.
- **Tool guides.** `source_code_search.md` states the matcher contract its parameter prose used to
  contradict — one literal substring, matched per line, never split into terms, unable to span a
  line break, and blind to file and symbol names. `get_item_stats.md` describes the fields the tool
  now returns and states the AC convention. `get_monster_stats.md` states the `ac`, `mc` and `mr`
  scales and which direction is favourable for the monster, without naming any monster or value.
  `_policy.md` scopes tool narration to the moment of the lookup and keeps it out of the answer's
  opening, and adds miss-recovery guidance.

#### The rubric FORM contract

Scoring method v9 abolished the link between a rubric's FORM section and the Readability
dimension, and the assessor prompt now says so outright: *"A rubric FORM or format suggestion is
not a READABILITY criterion… do not lower the level"*. **The label a rubric must carry for that**
existed in exactly one place in the repository — the placeholder text in the question editor:

```
**FORM** (not graded — presentation note only)
```

Every one of suite 6's eighteen rubrics instead says `**FORM** (readability)`, asserting a grading
link that no longer exists. `BenchmarkGenerationPrompt` is the upstream cause and now carries the
annotated label in its numbered instruction, its worked example and both JSON examples, so a newly
generated suite cannot reproduce the defect. The heading shape is load-bearing:
`BenchmarkRubricCitationValidator` bounds the `**SOURCE**` section with a look-ahead for bold
ALL-CAPS headings at line start, and a parenthetical **after** the closing `**` still matches —
`**FORM** (…)` is safe, `**FORM (…)**` is not.

Repairing the eighteen live rubrics is a separate, deliberate step: editing `ExpectedPoints` calls
`BenchmarkQuestionAssessment.Clear`, which increments `ItemRevision` and nulls the whole
assessed-difficulty snapshot, and `SuiteItemRevisions` is a **Fundamental** comparability key — so
every run of that suite after the edit is non-comparable with every run before it, and the suite
cannot be run at all until difficulty is re-assessed for all eighteen questions.

### Harness Version 18 Updates

Prompted by an audit of what a run's own integrity counts actually meant: a transport failure was
classified from an exception's message, which on a real host is an operating-system string in the
machine's display language, so the classifier only held on an English-locale box; and a `Failed`
or `ProviderError` answer sat in the same **Clean** bucket as an answer that actually produced
graded text, so a run that outright lost a question could still report itself 100% clean.
`ScoringMethodVersion` stays at **10** — no scoring formula changed — but
`BenchmarkAssessmentPrompt.HarnessVersion` moves to **18**, because the integrity bucketing, the
gradeable population every report figure is drawn from, and the fingerprints a run keeps around a
re-run all change what a report — or a comparison across the boundary — means.

- **Provider-error classification reads the exception, not its text.** `BenchmarkProviderErrorClassifier`
  gains `Classify(Exception? exception, string? errorMessage, bool callerCanceled)`, which walks the
  exception chain (unwrapping an `AggregateException`'s branches) and classifies from
  `SocketException.SocketErrorCode`, `OperationCanceledException`, `TimeoutException`,
  `IOException`/`AuthenticationException`, and `HttpRequestException` — types and enum values, none
  of which a locale can rewrite. `callerCanceled` tells a user-initiated cancel apart from a
  cancellation the transport itself raised, since only the caller knows which happened; the new
  overload falls back to the existing message-only `Classify(string?)` when nothing in the chain
  matches. That message-only overload — the only path open to the streamed `"error"` event, which
  carries a bare string — gained its own locale-independent substrings (`"connection attempt
  failed"`, `"forcibly closed"`, `"No such host is known"`, `"Name or service not known"`,
  `"actively refused"`, `"SSL connection could not be established"`), so a transport failure
  reaching it that way is not misclassified either.
- **The bucket that error lands in changed.** `BenchmarkRunFinalizer.HasTerminalFailure` is new —
  status `ProviderError` or `Failed`, nothing else — and `HasTransportDefect` now returns true for
  it ahead of every other check, so `Classify` puts such an answer in **TransportDefect**, not
  **Clean**. Say this plainly: it moves the clean count and the provider-error count of every run
  reported from harness 18 on, and a reader comparing a run stamped 17 against one stamped 18 must
  read the difference as a bucketing change, not a regression. No index, no dimensional score and
  no run status moves — `ComputeStatus` and the scoring path are untouched — which is exactly why
  `ScoringMethodVersion` does not move with it.
- **One gradeable-answer denominator, used everywhere "answered question(s)" is printed.**
  `BenchmarkRunFinalizer.CountsTowardQualityIndex` — an `Ok` status, or a model-produced empty
  answer scored 0 under method 10 — is now the population behind every such figure in
  `BenchmarkReportBuilder`, behind `BenchmarkChatTransfer.AnalyzeToolRouting`, and behind the
  client's diagnostics capture. The second-opinion trigger cascade and the grader-agreement
  aggregates are drawn from the same population, so a transport-defect answer can no longer consume
  a second-opinion slot and then contaminate the agreement figure. This **changes a published
  advisory figure** — the agreement mean — which stays advisory, folds into no index, and is always
  printed with its coverage caveat.
- **Two advisory flags.** `BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction` (512) is set from the
  prompted `Not in rubric:` marker (`BenchmarkAssessmentParser.OutOfRubricAccuracyMarker`) that
  nothing previously consumed; it also becomes a second-opinion trigger, with
  `ResolveSecondOpinionTrigger` positioning it after `ContestedVerdict` and before
  `UnevidencedDeduction`, gated on `(answer.AccuracyLevel ?? 6) <=
  BenchmarkVerdictConsistency.UnevidencedDeductionMaxLevel`. `BenchmarkAnswerFlags.AnswerFramingOpener`
  (1024) is set from `BenchmarkArtifactScrubber.HasAnswerFramingOpener`, which matches a
  claim-of-sufficiency opener — "I now have everything I need", "Let me give you the answer" —
  anchored at the start of the answer text. **The opener is detected and counted only; the text is
  not removed.** Scrubbing it would replace the very text the assessor grades, which is a
  scoring-method change rather than artifact removal — the same reasoning that keeps
  `ContestedVerdict` and the rest of the advisory set out of `TransportDefectFlags`. The report
  states explicitly that the opener reaches production chat unmodified and violates the
  answer-opening rule in `Overseer/ToolGuides/_policy.md`. Both flags get a run-level count —
  `OutOfRubricAccuracyAnswerCount` and `AnswerFramingOpenerAnswerCount` — recomputed from the
  persisted `AnswerFlags` every time `ApplyTotals` runs. Both columns are non-nullable and default
  to 0, which is the *correct* value for every run before 18: the flags did not exist for those
  answers to carry, so a genuine zero is what recomputing them produces. That is a different
  convention from the fingerprint and timestamp columns below, where a 0-shaped value would be
  wrong and null is the only honest reading.
- **A non-gradeable answer no longer publishes a speed score.** `BenchmarkRunFinalizer.Apply` nulls
  `SpeedScore` on every answer `CountsTowardQualityIndex` excludes, and the report suppresses the
  line for it. `SpeedIndex` itself does not move: it was already computed only over `Status == Ok`
  answers, so the filter had already excluded them.
- **The re-run's own fingerprint and timestamp columns.** Migration `BenchmarkHarness18Integrity`
  adds four nullable `BenchmarkRun` columns for a re-run's own instrument — the failed-question
  re-run's, and from 2026-09-24 the single-answer re-run's as well, so they describe the most recent
  re-run of either kind (§ *Runs Are Independent of Later Suite Changes*):
  `RerunCandidateSystemPromptSha256`, `RerunToolGuidesSha256`, `RerunStartedAtUtc`,
  `RerunCompletedAtUtc`. The re-run records its own fingerprints instead of overwriting the run's
  original five, because those five are the only record that the instrument did not move between
  two runs; overwriting them on a re-run would falsify the provenance of every answer the re-run
  did not touch. The same reasoning covers the wall clock: `CompletedAtUtc` and `TotalDurationMs`
  stay the original execution's, and `BenchmarkRunFinalizer.Apply` gained an optional
  `preserveCompletedAt` parameter (default `false`, the previous behaviour) that both re-run paths
  (`RunFailedQuestionsAsync` and `RerunSingleQuestionAsync`) pass as `true` — a re-run launched hours later must
  not absorb that interval into the run's own elapsed time. **All four columns read as "not
  recorded" on a run that was never re-run, never as zero.** Harness 22 later added a fifth,
  nullable `RerunHarnessVersion` (`nvarchar(16)`) alongside these four, stamped with the re-run's
  own `BenchmarkAssessmentPrompt.HarnessVersion`; see § *Harness Version 22 Updates*. The same
  migration also adds
  `OutOfRubricAccuracyAnswerCount` and `AnswerFramingOpenerAnswerCount` from the advisory flags
  above — six new `BenchmarkRun` columns in total — but those two are ordinary non-nullable counts
  and do not follow this "not recorded" convention; see the advisory-flags item for why 0 is
  correct for them.
- **The failed-question re-run fails safe.** `RunFailedQuestionsAsync` gained the same two terminal
  handlers `ExecuteRunAsync` already had: `OperationCanceledException` sets `Canceled`, and a
  general `Exception` sets `Failed` with `ErrorMessage` and `CompletedAtUtc`. Before this, a throw
  partway through a re-run could leave the row reading `Running` with no owner — an unrecoverable
  state, because cancelling it sets `Canceled`, and a re-run then refuses an aborted run outright.
  `AdminBenchmarkController.RerunFailedQuestions` now accepts an orphaned `Running` row
  deliberately — it is the one action that repairs such a row rather than ending it — while still
  refusing `Canceled` and `Failed`.
- **Cancelling a retry no longer locks the run out of later re-runs.** The bullet above left one
  gap: cancelling a *retry* of an already-finished run wrote `Canceled` too, which every re-run
  gate then read as "the suite is incomplete". Run #37 (2026-09-11) reached exactly that state —
  all 18 answer rows present, **CANCELED**, every re-run refused. Two changes close it. Every
  retry handler now restores the answers-derived status through `RestoreTerminalStatusAsync`
  instead of writing `Canceled`, and `POST .../runs/{id}/cancel` does the same for a row whose
  answer rows cover its suite. And the gates test what the refusal text claims:
  `BenchmarkRunFinalizer.IsAbortedRun` is `Canceled` or `Failed` **and** fewer answer rows than
  `TotalQuestionCount`, which repairs every existing row in that state without a database edit and
  keeps the gate correct if some future path writes `Canceled` again. The verdict reaches the
  client as the `isAborted` DTO flag. `ExecuteRunAsync`'s own handlers are unchanged: a run
  cancelled during its original execution genuinely did stop before finishing its suite.
- **The re-run executes the full run-level stage sequence.** Where it previously ran one
  verification pass, `RunFailedQuestionsAsync` now runs the same sequence `ExecuteRunAsync` does —
  verification, outlier sweep, sample top-up, verification again, synthesis — because that ordering
  (verification ahead of second-opinion selection, and again after it) is what keeps the pooled
  agreement figure measuring the same thing regardless of which trigger selected an answer; a
  re-run that skipped the middle stages would reintroduce the mixed-measurement problem that
  ordering fix removed, for a run whose figures are then compared against a clean one's.
- **A four-stage progress model.** `BenchmarkRunManager` gains `BenchmarkRunStage` (`Answering`,
  `Verifying`, `SecondOpinion`, `Synthesizing`, `Terminal`) plus `InFlightVerification` and
  `InFlightSecondOpinion` — per-answer in-flight sets alongside the existing `InFlightQuestions` —
  all exposed on the run-detail DTO. The client prefers the server's `stage` and falls back to
  deriving one from the answer rows only when it is absent. The server's figure is needed because
  nothing else can tell the two middle stages apart: no answer row changes while verification or
  the second-opinion pass runs, which on one measured run held for nine of the run's nineteen
  minutes. The state is in-process only — `BenchmarkRunManager` keeps no server-side log of it — so
  a run whose process restarts mid-flight reports no stage, the same as every other in-flight
  signal this dialog already falls back on. The **dialog** later stopped rendering these five
  values as four rail items — see the 2026-09-10 round below — while the enum itself is unchanged.
- **Tool result-budget parity.** `WikiSearchTool.MaxResultLengthOverride` is now `13000`, because
  the tool's own budget — `Tools:wiki_search:MaxResults` × `PerResultChars`, 5 × 2,500 = 12,500 —
  exceeded the generic 10,000-character cap (`Benchmark:MaxResultLength`), so a full-yield search
  was always truncated mid-article on its last hit. `ToolExecutor.ExecuteAsync` and
  `GetEffectiveMaxResultLength` both take `Math.Max(baseMaxLen, handlerMax)`, so an override is a
  **floor** and can never lower a cap; the shared `MaxResultLength` — every other tool's cap, and
  the live chat default — is deliberately **not** raised alongside it. `AgentLoopRunner`'s
  batch-budget exemption now compares a tool's effective cap against the *batch* budget
  (`Math.Max(ToolExecutionLimits:MaxBatchResultLength, MaxResultLength)`, 40,000 by default) rather
  than against the per-tool cap alone, so an override that fits inside the batch budget stays
  accountable to it: `wiki_search`'s 13,000 does, `refresh_snapshot`'s 60,200 does not and stays
  exempt — which is what the exemption was written for. `nethack_wiki_search` gained the per-result
  cap it never had — `NetHackWikiSearchTool.CapArticle`, configured at
  `Tools:nethack_wiki_search:PerResultChars` (default 3,000) — capping each returned article
  individually rather than leaving the whole result bounded only by the generic cut, which used to
  drop every article after whichever one Lucene's ordering happened to place last. These are
  `Overseer/appsettings.json` values, and **`ToolGuidesSha256` hashes the guide files, not the
  configuration**, so a tool-limit change like this one is invisible in every run record on its
  own; only the accompanying guide edits move that hash.
- **The comparability consequence.** `HarnessVersion` **is** an `Instrument`-kind key in
  `BenchmarkComparabilityKey`, and this round also moves `ToolGuidesSha256` — the wiki-tool guide
  and result-cap changes above touch the guide tree. It does **not** move
  `CandidateSystemPromptSha256`: the candidate system prompt inlines `_policy.md` and the policy
  overrides, not every per-tool guide, so editing a per-tool guide changes the hashed `ToolGuides`
  tree without changing the built prompt text. Run 30 measured this directly, recording a
  `CandidateSystemPromptSha256` byte-identical to run 29's despite the wiki-tool guide edits between
  them (`server_benchmark_to_chat_transfer` § 11). Per `BenchmarkComparabilityKey`'s tier resolver,
  exactly one differing `Instrument` key is Tier C — the deliberate single-variable experiment —
  and **two or more drops the set to `NotComparable`, below Tier B** (`instrument > 1` in the
  resolver). The first run stamped 18 therefore differs from a run stamped 17 on **two** instrument
  keys at once (`HarnessVersion`, `ToolGuidesSha256`) and is **not** a Tier-B reproduction of it:
  compare the two on counts and per-question thresholds, not on the index.
- **Gemini usage accounting, 2026-09-10.** `GoogleProvider.ParseStreamAsync` emits exactly **one**
  `usage` event per model call, after the stream ends, carrying the last `usageMetadata` the stream
  delivered. Gemini attaches `usageMetadata` to every streamed chunk — `promptTokenCount` constant,
  the output and thought counts cumulative — and `AgentLoopRunner` sums every `usage` event it
  receives into the run result's token totals, into `ModelCallUsages` (the per-call cost basis) and
  into `AgentRunBudget.AddActualTokens`. Until this date the provider emitted one event per chunk,
  so every Gemini model call was counted roughly **once per chunk**. **Every run recorded before the
  fix therefore carries inflated Gemini-role token and cost figures**: the primary assessor and the
  synthesis on every run, and the candidate on run 22 (Gemini 3.5 Flash-Lite) — its cache-read share
  possibly included. Run 33 shows the scale: the assessor reported ≈ 39,600 input tokens per
  assessment against ≈ 3,500 per call for the second opinion's prompt of the same shape, and its
  reported $1.97 total corresponds to roughly $1.2. Anthropic and OpenAI roles were never affected;
  each of those providers emits a single usage report per response. Live Gemini chat sessions were
  inflated the same way — per-message token and cost figures, and the budget's actual-token tally.
  Token totals and costs are **not** comparability keys and no record shape changed, so
  `HarnessVersion` stays at `"18"`; but a cost or token comparison **across this date** that involves
  any Gemini role compares an inflated figure with a corrected one, and the stored pre-fix figures
  are not rewritten.
- **Run 34 round, 2026-09-10 — reporting, detector and one default; no version bump.**
  `HarnessVersion` stays at `"18"` and `ScoringMethodVersion` at **10**: nothing a run records
  changes shape, and the only stored figure that can move is the advisory
  `AnswerFramingOpenerAnswerCount`. The round's chat-side edits (`_policy.md`, the
  `get_function_definition` continuation contract, `get_monster_stats` attack dice) move
  `CandidateSystemPromptSha256` and `ToolGuidesSha256`, recorded in `server_benchmark_to_chat_transfer`
  § 11; the four changes below move neither.
  - **The Run Integrity Notice counts the second opinions that completed.** When some second-opinion
    calls failed and others completed, the admin notice said grader agreement *"is not measured for
    this run"* directly under an agreement card reporting a coverage of 3 of 18. It now says agreement
    is measured over the answers whose second opinion completed, and keeps *"not measured"* for
    coverage 0 only — the distinction `BenchmarkReportBuilder` already drew.
  - **`AnswerFramingRegex` gains two alternatives**: a pronoun subject claiming sufficiency without
    naming a source (*"This has full detail"*, *"This covers it well"*), and the *"— here's the
    rundown / summary / breakdown / short version / answer"* tail within an answer's first 80
    characters, which announces that the substance follows instead of starting it. Run 34's hand
    count was 4 against a detector count of 2, the sixth consecutive under-count. Still
    **detect-and-count only**.
  - **The Claim Verification Yield line prints per-claim cost to three decimals below $0.01**, so a
    non-zero cost ($0.04 over 15 claims) no longer reads `$0.00/claim`.
  - **`Overseer/appsettings.json` sets `Benchmark:SecondOpinion:TimeoutSeconds` to 600** (it was
    900; the code fallback in `BenchmarkService` for a missing key stays 900). On run 34 one second
    opinion at `xhigh` ran the full 900 s and doubled the run's wall time; a healthy call at `high`
    took about 84 s on run 33, so 600 s still stops only a hung call. A timeout is recorded in
    `SecondOpinionError` with the raw head, as before. No run record fingerprints this setting, so the
    registry entry records it per run.

### Run Progress Dialog Round (2026-09-10) — No Version Bump

The four-stage rail introduced with harness 18 described a pipeline the executor does not run.
`BenchmarkAssessmentPrompt.HarnessVersion` stays at `"18"` and `ScoringMethodVersion` stays at
**10**: nothing a run records, sends or scores changed here. What changed is when one transient
in-process mark is set, and what the dialog draws.

Three defects, all from the same mismatch:

- **A scored row went on reading as finished while the verifier re-read it.** `RunSecondOpinionAsync`
  had always set its in-flight mark for every caller, but the verification mark was set only inside
  the run-level loop in `RunClaimVerificationAsync`; the per-answer call in
  `ExecutePerQuestionAssessmentAsync` reached `VerifyAnswerClaimsAsync` directly and marked nothing.
  With `MaxParallelQuestions = 1` the next question is not dispatched until the current one's
  assessment, verification and second opinion have all returned, so the visible result was a scored
  row, a **Pending** row, and no explanation for the gap between them.
  `VerifyAnswerClaimsAsync` is now a wrapper that sets and clears the mark in a `finally` around
  `VerifyAnswerClaimsCoreAsync`, exactly as the second-opinion path does, and the run-level loop no
  longer marks anything of its own.
- **The rail moved backwards.** It mapped `BenchmarkRunStage` one-to-one, and the server marks
  `Verifying` → `SecondOpinion` → `Verifying` → `Synthesizing`, so stage 3 lost its done state late
  in every run. The two middle values now share one rail item, "Follow-up grading passes", and the
  status line and diagnostics name which pass is running. The enum is untouched — it still names the
  pass precisely, which is what the status line and the diagnostics need.
- **Two progress bars had no honest maximum.** "Claims verified" and "Second opinions" both drew
  against the answered count, though only a candidate answer is ever verified or re-graded, so they
  sat full or empty rather than measuring anything. They are counts in the statistics strip now,
  each shown only when its role is configured.

The dialog's single live region moved into the Assessments block along with the bars it used to
follow. Not changed, and deliberately: the stage stays in-process only, the executor's ordering is
untouched (sequential mode still waits for the full grade of question *n* before dispatching *n*+1 —
this round makes that wait legible, it does not remove it), and the multi-run progress dialog's rail
describes series stages rather than run stages and keeps its own.

### Run 30 Round (2026-09-10) — No Version Bump

The round implementing run 30's analysis. `BenchmarkAssessmentPrompt.HarnessVersion` stays at
`"18"` and `ScoringMethodVersion` stays at **10**: what changed is report and UI rendering, a
read-only export, an advisory detector widened, one tool payload added, and two tool
implementations changed — none of it touches an index, a dimensional score, an integrity bucket or
a run status.

The round moves **`ToolGuidesSha256` only**, through `wiki_view.md`. Per-tool guide edits do not
move `CandidateSystemPromptSha256`, because the candidate system prompt inlines `_policy.md` and
the policy overrides rather than every per-tool guide — see the corrected bullet above, which run
30 is the measurement behind. With the claim verifier held at GPT-5.6 Luna for the confirming run
(§ 6 of `server_benchmark_to_chat_transfer`), the confirming run is **Tier C** against run 30: one
differing instrument key, not two or more.

#### Report and UI fixes (H1–H4, H7)

- **The report's three P50 lines — model time, turn duration, TTFT — now use the statistical
  median**, the mean of the two middle values for an even count, matching what the Angular score
  card already computed; P90 and maximum stay nearest-rank. **This changes a published figure for
  every run re-rendered from now on**: run 30 itself re-reads median model time as 22,283 ms where
  the report previously rendered 19,406 ms for the same stored data. It is report-only and folds
  into no index and no comparability key.
- **The per-question tool table gained an `Args` column**, a bounded 80-character single-line
  preview of the call's arguments, with `(pruned)` where the retention sweep had already nulled the
  payload.
- **Disputed Assessments now renders the second reader's stored `comment`**, previously captured
  but not displayed.
- **Band Agreement now opens by stating what the section actually describes**: assessed difficulty
  is a suite-item snapshot, so the section is describing the suite, not the run being read.
- **The admin advisory-flag mask gained `OutOfRubricAccuracyDeduction` (512) and
  `AnswerFramingOpener` (1024)**, which `BenchmarkRunFinalizer.AdvisoryFlags` already carried on the
  stored answer — the mask that renders the Run Integrity Notice had not been widened to match, so
  the notice had been counting three flagged answers while naming only two questions.

#### The tool-call log export

A new admin-only `GET runs/{id}/tool-call-log`, returning `text/markdown` and downloadable from the
run detail page as **Tool-call log**, covers every answer of the run: the per-call table, plus per
call the full arguments, the error, and the first 600 characters of the result (from harness 25, its last 240 as well), with
`(pruned by retention)` markers wherever the sweep nulled a payload. Payloads render inside fenced
code blocks rather than table cells, so no escaping can corrupt them. It loads the run's answers
with their tool calls included, which the run-detail endpoint deliberately does not.

**Why it exists**: the run-30 analysis had to leave several questions undetermined for want of the
stored arguments and results. The export settled every one of them within minutes of shipping —
including a `wiki_view` resolution defect that no amount of report-reading across six prior runs
had found.

#### The detector widening (H3)

`BenchmarkArtifactScrubber.AnswerFramingRegex` now admits `both` and `every` as objects. It remains
**detect-and-count only** — the matched text is never removed, because scrubbing would replace the
very text the assessor grades and would move `ScoringMethodVersion`.

#### The tool changes

- **`SourceCodeService.FindDefinition` builds its patterns once per call** and gates each line on a
  literal `Ordinal` `Contains` before any pattern runs against it; the output is byte-identical for
  every `kind`, and a rendered-output test pins that. On run 30 this method cost 1,338–2,265 ms per
  call — **8,806 ms of the run's 10,544 ms of tool time from 6 % of its calls** — and a live chat
  turn pays the identical cost.
- **`WikiService.GetArticle` gained the ordered resolution branches**: request normalization
  (including the `.md`/`.txt`/`.html` extension tolerance), an exact path-form match against a
  stored `relpath`, then title-equality collection with a disambiguation payload for two or more
  hits, the resolved article for exactly one, and the prior top-scoring-hit behaviour — now scoped
  to that last branch only — for none. The result header and `wiki_search`'s snippet headers now
  show the article's repository-relative path. The wiki indexer now skips any file whose path
  relative to the wiki root has a segment beginning with `.`.

The tool-limit and index-scope halves of this move **no fingerprint at all**, exactly as the
harness-18 section above already says of `Overseer/appsettings.json` tool limits.

#### What was deliberately not done

No `ChatService.BuildSystemPrompt` prose was edited, and no rung 5, 6 or 7 action was taken.

**H6 — the strictly serial advisory grading stages (9m 19s of run 30's 22m 25s) — is deferred, with
its reason.** Both the verification loop and the second-opinion loop share one
`ApplicationDbContext`, which is not thread-safe, and the verifier's token budget is accounted
sequentially; parallelising either is a scoped refactor and gets its own round.

**The suite-6 rubric repair and the band-drift repair stay deferred to one single deliberate
comparability break**, which must not land in the same round as a verification run: the `#if 0`
group-size divisor in Q16's rubric, the out-of-scope Completeness points, the FORM `(readability)`
labels on all 18 rubrics, and the `BenchmarkDifficultyPrompt` band-drift repair. **N3** — a `section`
that matches no heading returning the whole truncated article instead of its heading list — is
deferred for the reason given above: it changes a contract `wiki_view.md` and
`server_tool_parameter_reference` § 5 both document, so it needs its own pre-declared criterion and
rollback.

### Run 31 Round (2026-09-10) — No Version Bump

The round implementing run 31's analysis. `BenchmarkAssessmentPrompt.HarnessVersion` stays at
`"18"` and `ScoringMethodVersion` stays at **10**: what changed is one tool's section matcher, three
tool guides, one source-indexer allow-list, and two harness scheduling changes. The round moves
**`ToolGuidesSha256` only** — through `wiki_view.md`, `monster_lookup.md` and `wiki_search.md` —
and `CandidateSystemPromptSha256` **must not** move, which a test in `BenchmarkServiceTests`
pins. With the grader roster held at run 31's for the confirming run, that pair is **Tier C**: one
differing instrument key, not two or more.

#### Grading is pipelined with the candidate (H1)

Per-question grading — assessment, claim verification and any per-answer second opinion — used to be
awaited between two candidate turns in the sequential branch. One critical-error second opinion at
`max` idled run 31 for **5 m 13 s between Q5 and Q6** and evicted the Anthropic prompt cache while it
ran (Q6 cache creation 23,034 tokens against ~11,000 typical); second opinion took 57 % of the run's
wall time for four opinions.

Grading now starts as a task and is **not** awaited before the next candidate turn. Each grading task
owns the DI scope its candidate ran in, so no `DbContext` is touched from two tasks at once, and the
pipeline is bounded by **`Benchmark:MaxConcurrentGrading`** (default **2**) so the assessor provider
is not hammered. The candidate never waits on that semaphore. The pipeline drains with a single
`Task.WhenAll` before the run-level stages, every one of which needs the full set of scores. The
`FlaggedPlusSample` sample top-up likewise runs its selected opinions concurrently under the same
bound, each in its own scope with its answer re-loaded by id; "lowest quality score first" remains
the *selection* rule and never governed execution order. The credential-collision path is unchanged
— it already deferred all assessment to after the loop.

**The candidate is still strictly sequential**, and `DurationMs`, `TtftMs` and `ToolTimeMs` are still
measured inside the candidate's own turn, so speed comparability across runs is untouched.

**What a run's timings now mean.** *Assessment Time* and *Second Opinion Time* remain **sums of
stage durations**, and those stages now overlap each other and the candidate, so each sum will
exceed its own wall-clock span and the report's *measured overlap* line will report a large
**negative** overlap. That is the arithmetic reporting a pipelined run honestly, not a defect. No
score, index, integrity bucket, run status or comparability key reads either figure. A run's
`TotalDurationMs` still measures the wall clock, which is the figure that should fall.

#### The candidate request carries the segmented prompt (H2)

`ChatService` has built the system prompt in three cache segments — frozen prefix, session prefix,
volatile suffix — since the prompt-cache work, but the benchmark passed only the flat string, so the
25,682-character prompt was written to the Anthropic cache on **every question**: $0.79 of run 31's
$1.35 candidate cost was cache write. The candidate `AgentRunRequest` now carries `FrozenPrefix`,
`SessionPrefix`, `VolatileSuffix` and `SegmentedPrompt`, built through
`BenchmarkCandidatePromptOptions.BuildSegmentedSystemPrompt` under the same
`PromptCacheSettings:EnableSegmentedPrompt` gate `ChatService.BuildSystemPrompt` uses.

`SystemPrompt` stays the flat string and `CandidateSystemPromptSha256` still hashes it, so **the
instrument does not move**: the segments concatenate to that string byte for byte, and a test asserts
that concatenation for the default options and for `verboseMode: true`. Only the Anthropic provider
reads the segments today; Google's stable-prefix caching and OpenAI's automatic caching key on the
byte prefix, which this does not change. In a benchmark the volatile suffix is empty — there is no
wiki context — and the provider already skips an empty block.

**This was a benchmark-side defect only.** Live chat has always sent the segmented prompt, so a
run's cost and cache-read share were never representative of chat on this axis; a comparison between
a pre-round and a post-round run's candidate cost is not a model result.

#### `wiki_view` section matching (T1)

`WikiService.ExtractMarkdownSection` matched a `section` by case-insensitive equality on the whole
heading text, and 12.7 % of the GnollHack wiki's headings (1,192 of 9,367) carry an emoji prefix — so
`section: "Elbereth"` could not reach `## 🔮 Elbereth` and the tool returned the whole article
truncated at 10,000 characters instead. Seven occurrences across runs 30–31; Q4 of run 31 alone cost
277K input tokens and 8 model calls to it.

Matching is now two passes: exact case-insensitive equality first, then — only if no heading matched
exactly — equality after normalising both sides, stripping every *leading* character that is not a
Unicode letter or digit and collapsing internal whitespace runs. Stripping is leading-only and the
exact pass runs first, so an article with both `## Notes` and `## 📝 Notes` still answers `Notes`
with the exact one.

The section-miss line now carries the article's headings, in document order and as written so one
can be copied straight back, `; `-separated and capped at 600 characters:

```
[Section 'X' not found in article. Headings: ℹ️ Overview; 📝 Engraving Mechanics; 🔮 Elbereth; …. Returning full text.]
```

The whole article still follows it, so the documented contract — a section that matches no heading is
not a miss — is kept; the line only adds information. This is run 30's deferred **N3** in its
non-breaking form. `nethack_wiki_view` is **not** changed and still matches exact titles only.

#### The monster-page template in the guides (T2)

`monster_lookup.md` warned about "the difficulty number" without naming the label the model actually
sees on the page, and on run 31 Q14 reported Master Kaen as level 40 — his difficulty rating — where
he is level 25. Measured on 539 of 624 wiki monster pages, the template is a `## Level N …` header
followed by a `Hit dice: M` line. Both `monster_lookup.md` and `wiki_search.md` now say so: the
header number is the difficulty rating, `Hit dice` is the level, and `get_monster_stats` is the
fallback when the level is needed and only the header is present. Guide text only — no code change,
and `ToolGuidesSha256` moves.

#### `winprocs.h` reaches the source index (C1)

`SourceCodeService`'s platform-header heuristic excludes any `win*.h`, sparing only `wintype.h`. That
also excluded `include/winprocs.h`, so `search_definitions("window_procs")` returned nothing for a
struct that exists. The heuristic now spares `winprocs.h` as well, with both comparisons
case-insensitive. This moves **no fingerprint** — `SourceCodeHeadSha` is the repository's Git HEAD,
not a description of what the indexer kept — so a run before and after this change reports the same
`SourceCodeHeadSha` over different index contents.

#### The detector widening (H3)

`BenchmarkArtifactScrubber.AnswerFramingRegex` reported 1 answer-framing opener on run 31 where a
hand count found 4. The three it missed all made the claim **about the sources** rather than about
the model, so the pattern gained two alternatives: one with a source noun as the sentence's subject
(*"The wiki has this well documented."*), one with it as the object after the sufficiency word
(*"This is well covered by the wiki."*), the second insisting on that noun precisely so it does not
claim ordinary prose. It remains **detect-and-count only** — the matched text is never removed,
because scrubbing would replace the very text the assessor grades and would move
`ScoringMethodVersion`.

#### What was deliberately not done

No `ChatService` prose was edited and no rung 5, 6 or 7 action was taken. **T4 — the answer-framing
prompt half — is a triggered rung-7 candidate that is deliberately deferred**: the rule's threshold
(3 or more openers on two consecutive runs) is met, but the undetected openers scored 100 on
Conciseness, `_policy.md` already forbids the form, and no measurable quality cost motivates the
edit. The detector is widened so the count stays honest, and the trigger is recorded in
`server_benchmark_to_chat_transfer` § 11.

**The grader roster is frozen at run 31's for the confirming run** — assessor Gemini 3.7 Flash @
`high`, second opinion GPT-5.6 Luna @ `max` blind on `FlaggedPlusSample`, verifier GPT-5.6 Luna @
`max` — so that only `ToolGuidesSha256` moves across the pair.

**S1 (Q18) and S2 (Q3) join the deferred single suite-6 rubric repair**, which must not land in the
same round as a verification run.

### Run 32 Round (2026-09-10) — No Version Bump

The round implementing run 32's analysis. `BenchmarkAssessmentPrompt.HarnessVersion` stays at
`"18"` and `ScoringMethodVersion` stays at **10**: nothing a run records changes shape, and no
candidate-side input moves — `CandidateSystemPromptSha256` and `ToolGuidesSha256` both stand still.
The one expected effect on a run's figures is that fewer second opinions are lost, so agreement
coverage may rise; that is the grading stage succeeding more often, not a change in what it measures.

#### Second-opinion parse recovery (H1)

Run 32 lost 3 of 5 GPT-5.6 Luna second opinions at `max` to JSON parsing, and the stage ran
26 m 33 s for two usable verdicts while the run looked stuck. Three changes bound that:

- **Balanced extraction.** `BenchmarkJsonExtractor.Extract` still prefers a fenced JSON block, but
  outside a fence it now returns the first *complete* JSON object or array in document order, found
  by running a `Utf8JsonReader` (trailing commas allowed, comments skipped) from each `{` or `[`. A
  bracketed word in prose before the object (`[Rubric 2]`) no longer wins, and text after the object
  no longer rides along. The old first-to-last bracket span is the last resort, so a malformed object
  still reaches the parser and its error is the one recorded.
- **Timeout and one re-ask.** `RunSecondOpinionCoreAsync` mirrors the claim-verification path: a
  linked cancellation after **`Benchmark:SecondOpinion:TimeoutSeconds`** (default **900**; set to
  600 in `appsettings.json` from the run-34 round), and on a
  parse failure one JSON-only re-ask (**`Benchmark:SecondOpinion:ParseRetryEnabled`**, default
  `true`) inside the request's existing two-model-call budget. The re-ask's tokens and duration are
  added to the answer's `SecondOpinion*` figures and to usage. A timeout is recorded in
  `SecondOpinionError` exactly like a parse failure (`Second opinion timeout exceeded (N s).`); a
  cancel of the run itself still cancels. 900 s rather than the verifier's 300 s because run 32's
  healthy `max` calls took about 5 minutes each; the ceiling is there to stop a hung call, not a slow
  one.
- **The raw head is kept.** An unusable verdict's `SecondOpinionError` carries the failure message,
  then ` | raw: ` and the first 600 characters of the model's last response, newlines collapsed,
  under the column's 2048-character cap — so the next parse failure can be read from the record. No
  new column.

#### The measured-overlap line (H2)

With grading pipelined behind the candidate (run-31 round, H1), the summed stage durations can
exceed the wall clock, and `FormatDuration` rendered the negative residual field by field as
`-1.-1s`. It is now sign-aware, and a negative residual gets its own sentence: *"the summed stage
durations … exceed the wall clock by 16m 1s (961,135 ms) — grading stages ran concurrently with
candidate answering."* A non-negative residual keeps the original wording.

#### The detector widening (H3)

`AnswerFramingRegex` reported 1 answer-framing opener on run 32 where a hand count found 3. Its
"clear answer" alternative now admits *gives / provides / offers* beside *has / is* (*"This gives a
clear answer already."*), and a new interjection-led alternative claims *"Good — I now have a clear
picture of …"*. Both carry non-matching controls in the tests (*"This gives the player a clear
advantage."*, *"Good — the sword is cursed."*). Still **detect-and-count only**.

#### Dot-directories leave the source index (H4)

`SourceCodeService.IndexRepository` now skips any file whose path relative to the repository root
has a segment beginning with `.`, the same test `WikiService` applies to the wiki, and logs the
skipped count once per pass. Run 32's Q16 surfaced a Visual Studio cache file
(`win/win32/xpl/…/.vs/…/HierarchyCache.v1.txt`) in a `filenames_only` search; it was the only such
file on disk. The indexer is shared, so the NetHack source skips dot-directories too. This moves
**no fingerprint**: `SourceCodeHeadSha` is the repository's Git HEAD, not a description of what the
indexer kept.

> **Extended in the Harness Version 20 round**: the same method (renamed
> `IsUnderExcludedDirectory`) also skips any segment equal to `bin` or `obj`, case-insensitively —
> see § *Harness Version 20 Updates* for the 483-file measurement this caught.

#### The run-progress dialog question list (H5)

The dialog's question list (`.run-question-list`) no longer has its own height cap and scroller; the
dialog body is the only scroller. `.job-item-list` keeps its `20rem` scroller everywhere else.

#### What was deliberately not done

No `ChatService` prose was edited and no tool guide was touched. **The `_policy.md` opener wording
(run-32 T2) is deferred one round**, so the prompt hash and tool guides stay still while the grader
roster moves; the detector is widened now so the next count is honest. The `Praying.md` precision
paragraph (T1) is made in the GnollHack wiki repository's own session.

**The grader roster does move before run 33, by operator decision**: the second-opinion and
claim-verifier configurations (both GPT-5.6 Luna) go from `max` to one chosen thinking level (`high`
recommended). Those are **two** Instrument keys, so run 33 is below Tier B against run 32 and verifies
this round's countable criteria only; the first agreement and verifier figures at the new level are
a baseline, not a comparison.

### Multi-Run Replicate Sets (Harness Version 14)

Four consecutive runs reproduced the same Completeness gap, and the harness still could not say whether any single figure would survive a re-run. A one-run result mixes the thing being measured with the noise of measuring it once, and no amount of care in the report separates them. Replicate sets do.

The statistical method is documented in full in **[`ai-benchmark-multi-run.md`](ai-benchmark-multi-run.md)**; this section covers the machinery.

#### Series: N runs of one identical request

`BenchmarkRunSeries` is a launched series — *N* executions of one validated `StartBenchmarkRunRequest`, run **strictly one at a time**. `StartBenchmarkRunRequest` gains `RunCount` (default `1`) and `AllowCapWait`; a `RunCount` of 1 is the pre-multi-run path exactly, creating no series row and no group.

- **Sequential is not merely a constraint.** `BenchmarkRunManager` permits one run at a time, so a series could not overlap members even if it wanted to. It is also correct: the report already calls this timing mode *"Sequential (comparable speed)"*, and replicate speed measurement is only meaningful when every member ran under the same contention.
- **One launch path.** `BenchmarkRunLauncher.CreateAndLaunchRunAsync` owns every validation the single-run endpoint used to perform inline, and both `POST runs` and the orchestrator call it. Two copies of that validation would drift, and the drift would show up as a series whose members were admitted under different rules — precisely what a replicate set may not be.
- **`RunCount` is bounded by the live configured `MaxRunsPerDay`, never by a constant.** Raising the series ceiling and raising the daily spend cap are then the same action. A series may never be a way around the cap.
- **The cap is a rolling 24-hour window**, not a calendar day, and `GET runs/limits` reports it the same way `CanSpendAsync` counts it. `BenchmarkComplianceGuard.GetLimitsAsync` is the single owner of that arithmetic; no caller re-derives it, because a client that computed "today" as midnight-to-now would disagree with the guard that actually refuses the run. The arithmetic works out exactly: a series of *N* = `MaxRunsPerDay` launched from an empty window passes, because the guard tests the count *before* creating each run and the last member sees *N* − 1.

**Status, stop reasons and resume.** Status is `Pending | Running | WaitingForCap | Stopped | Completed | CompletedWithErrors | Cancelled | Failed`, with `StopReason` ∈ `MemberFailed | RunCapReached | SpendDenied` set whenever the status is `Stopped`. A cap denial either parks the series in `WaitingForCap` with a bounded, cancellable retry (`AllowCapWait`) or stops it resumably. A member failure stops the series and **keeps the completed runs**. Cancellation cancels the in-flight member through the run manager — so the run's own finalisation path runs rather than being bypassed — and `Cancelled` is terminal and **not** resumable: the operator said stop, which is a different statement from a series that halted on its own.

`POST runs/series/{id}/resume` continues from `CompletedRunCount + 1`, and:

- **It works after a process restart.** The orchestrator is transient in-memory state; the series is a row. Resume reconstructs everything from the row, and `ReconcileOrphanedSeriesAsync` moves any series left `Running` by an unclean shutdown to `Stopped` at startup, so the Continue button appears instead of the row sitting in a state nothing is advancing.
- **The instrument guard refuses by default.** The series records member 1's `CandidateSystemPromptSha256`, `ToolGuidesSha256` and `KnowledgeBaseHeadSha`. Resume recomputes all three and **refuses when any has moved, naming which** — a replicate set whose members straddle a deployment is not a replicate set, and the failure is silent: every downstream statistic would still compute, confidently, over incomparable runs. An explicit `acknowledgeInstrumentChange` override continues and marks the series, which forces the auto-created group to **Tier C**, making pooling impossible by construction rather than by discipline.
- Resume is refused for `Cancelled`, `Completed` and `Failed`, goes through `BenchmarkRunManager.TryStart` like every other launch (so pressing Continue twice cannot double-start a member), and re-checks `CanSpendAsync()` first: a resume is a new run and is capped like one.

On completion with two or more successful members, a `BenchmarkRunGroup` is created automatically, named `<Suite> · <Model> · <date> · R=<n>`, with its tier **resolved from the runs and asserted** rather than assumed. A series is Tier A by construction — but "by construction" is an argument, and the whole point of the tier machinery is that the argument is checked. Any disagreement that is not an acknowledged instrument change is logged as a harness defect.

#### Comparability tiers: what may be computed over which set

`BenchmarkComparabilityKey` extracts the keys from a run and resolves a set's tier. This is the guard against this feature's worst failure — a confident pooled index computed over runs that were never comparable.

| Tier | Meaning | Pooling |
|---|---|---|
| **A — Replicate** | Every key matches: suite **and every question's item revision**; the full candidate specification and prompt options; all three instrument hashes; harness and scoring-method versions; scoring profile **and its canonical scoring-semantics signature**; assessor, second-opinion and claim-verifier configurations; per-question budgets **(tool-call budget, iteration cap, model-call cap and question timeout, all four bands)**; question parallelism | **Yes** — the only tier at which a pooled index is sound |
| **B — Quality-comparable** | Tier A relaxed on question parallelism and the pricing snapshot, which affect speed and cost only | Quality yes; **speed and cost aggregates carry a degraded flag** |
| **C — Cross-condition** | Candidate identical, exactly one instrument key deliberately moved | **Never.** Such a set is two groups, and the tool's job is to *compare* them |
| Below B | The runs measure different things | No aggregate over them means anything |

The scoring profile is keyed by its **canonical scoring-semantics signature** (see § 9), not its
raw snapshot and not its id alone, because scoring method 8 edits the default profile in place and
because a non-semantic field inside that snapshot — the profile's name, its default flag, or its
timestamps — must not be able to end a comparable series by itself. Item revisions are keyed
because a rubric edit changes the answer key — which is exactly what the Rubric Gap Author does on
purpose.

The resolver returns, for a set that fails a tier, **which keys differ and on which runs**. A boolean verdict with no reason is unusable in a dialog or a bug report, so the reasons travel to the UI, into the group's `TierReasonsJson`, and into the copyable diagnostics.

**Enforcement**: a group below Tier B **cannot be persisted**, and Tier C requires an explicit `crossCondition` flag. These rules live in the controller rather than the UI, because the UI is not the thing that must not be bypassed.

#### Groups, analysis and the report

`BenchmarkRunGroup` ↔ `BenchmarkRunGroupMember` is many-to-many: a run may sit in several analysis groups — a baseline run belongs both to its own replicate set and to the cross-condition pair it anchors — while belonging to at most one series. `BenchmarkGroupAnalysis` persists a computed result with its member run ids and versions, so a group report stays reproducible after a run is deleted or the membership changes. A group whose membership moved since its last analysis is **badged stale rather than discarded**: a stale analysis is not wrong, it is a correct statement about a different set of runs.

`BenchmarkGroupStatistics` is pure arithmetic — no I/O, no AI. `BenchmarkGroupReportBuilder` renders it as Markdown with **no AI-written synthesis**: every figure is reproducible arithmetic, which is what makes the report usable as an instrument rather than as another opinion. `GET runs/groups/{id}/report` mirrors `GET runs/{id}/report` exactly, and is built from the **persisted analysis** rather than recomputed.

Two points the report and the UI both state explicitly, because both are misread otherwise:

- **The item-sampling interval does not shrink with *R*.** The two variance components answer different questions and are rendered separately before being combined. Reproducibility SE = SD(run indices)/√*R* answers *"would a re-run move this?"* and **does** shrink with *R*. Item-sampling SE answers *"would a different set of questions move this?"* and **does not**, because every run uses the same items. A reader who expects it to shrink will report the code as broken.
- **Multi-run cannot separate candidate noise from grader noise.** Run-to-run variance mixes the two, because each run produces a new answer graded once. Separating them requires re-grading identical answers (`SecondOpinionMode = All`, or a re-assessment pass); `FlaggedPlusSample` is the partial answer. Without this stated, an operator will attribute item instability to the model when it may be the grader.

Per-item differences in a group comparison are **exploratory, under Benjamini–Hochberg FDR control**, and labelled as such everywhere they appear. Eighteen simultaneous item tests without correction would manufacture findings.

#### What the group report adds: per-run scores, TTFT, model calls, the reproducibility interval

`BenchmarkGroupReportBuilder` numbers its sections with a running counter, so the references below
(§2.1, §4, §5, §7) are **that report's own headings**, not this document's:

- **§2.1 — the reproducibility SD's own interval.** Beside the point estimate, the report states the
  reproducibility standard deviation's own 95 % confidence interval on σ itself, from the
  χ²(*R*−1) distribution, tabulated for *R*−1 = 2 through 19 (*R* = 3 through 20). Null below
  *R* = 3, where there is no SD to bound, and null above *R* = 20, where the warning it carries no
  longer applies. At *R* = 3 the upper bound is roughly **6.3×** the point estimate, so **two
  groups' SDs are not comparable point estimates**: a set reporting an SD ten times another's can
  have an interval that overlaps it completely, and a reader who compares the bare numbers will
  conclude the instrument became ten times less reproducible when nothing measurable did.
- **§4 — per-run scores, and two kinds of unstable item.** The Per-Item Statistics table gains a
  **Per-run scores** column rendering each item's scores in run-id order (e.g. `25 / 97 / 97`),
  with that order stated once under the table; an item whose score and run-id vectors disagree in
  length renders `—` rather than a mispaired list. Unstable items split into two kinds: **unstable
  because the critical-error ceiling tripped in some runs and not others** (critical-error rate
  strictly between 0 and 1 — the SD is the ceiling moving rather than a score earned differently,
  and the item's Accuracy mean sits well above its total mean), rendered `unstable — ceiling`; and
  **unstable with a critical-error rate of 0**, earned on the rubric itself. The two look identical
  in the table without this split and are entirely different problems — a rubric that cannot
  decide a borderline case, versus an answer that genuinely varies.
- **§5 — pooled time to first token.** A pooled TTFT line (P50 / P90 / max) sits beside the pooled
  model-time line, over its own answer count — kept separate from the model-time count because TTFT
  is nullable where model time is not. This is the latency a chat user actually waits through; a
  production speed claim rests on this line, not on model time, which a thinking-heavy
  configuration can dominate without moving TTFT at all.
- **§7 — model calls, and the claim verifier's yield as arithmetic.** Beside the tool-call block,
  the report carries total and per-run **model calls**, with the point that **input cost tracks
  model calls, not tool calls**: every model call resends the whole conversation, so two tools
  batched into one call pay the input once and the same two tools in two calls pay it twice. Where
  claim verification ran, a Claim Verification Yield block reports cost per claim checked, cost per
  refutation (stated as "this ratio does not exist" when there were no refutations, rather than as
  a zero or an infinity), and the indeterminate share.
- **§1.1 correction — the *Chat Prompt Under Test* batching line is now three-way.** `Disabled` and
  `OnRequest` each name the override file they select (`_policy_parallel_disabled.md` /
  `_policy_parallel_on_request.md`); **`Enabled` selects no override file, and the batching
  guidance already in `Overseer/ToolGuides/_policy.md` applies unchanged.** The line previously
  claimed `Enabled` selected `_policy_parallel_on_request.md`, which is prompt text the prompt
  never contained.

Every field above is computed from data the run already stored — no migration, no backfill.
Because the statistics live in the persisted `BenchmarkGroupAnalysis.ResultJson` rather than being
recomputed on every read, **a group analysis computed before these fields existed still renders
the new lines as `—`, not as a missing section, until Run Analysis is pressed again.** An absent
value renders as an em dash rather than as a zero deliberately: a stale analysis reporting "0
model calls" would be a false statement about a run that made calls nobody has counted yet.

### Harness Version 19 Updates

*2026-09-10.*

Prompted by a count nobody had audited: of the five critical errors ever published on the Claude 5
Sonnet series, **three were the grader's mistake rather than the model's** — run 28 Q3 (the rubric
quoted `objects.c` macro *arguments* as player-visible values), run 31 Q18 (erosion on attack, which
is the game's own code, proven against `src/engrave.c`), and run 35 Q1 (*"Immune to lycanthropy"*,
which is `src/attrib.c:117` and `Races/Gnoll.md:25`). A critical error caps quality at 25 regardless
of the four levels, so it is the single most consequential judgement in the assessment prompt, and
on run 35 the *blind* second opinion agreed with the false verdict — meaning the existing
second-reader safeguard does not catch this class at all. The harness already owns a model whose
whole job is checking a claim against the game's source and wiki, and it was never pointed at the
one claim that costs the most to get wrong.

`ScoringMethodVersion` stays at **10** — nothing here changes a score, a cap, an index or a run
status — but `BenchmarkAssessmentPrompt.HarnessVersion` moves to **19**, because the assessment
prompt's own text changes, a run records a new count, and two tool contracts move
`ToolGuidesSha256`. A run stamped 19 therefore differs from a run stamped 18 on **two** instrument
keys (`HarnessVersion`, `ToolGuidesSha256`) and not on `CandidateSystemPromptSha256`: `_policy.md`
is untouched and per-tool guides are not inlined into the candidate prompt.

- **Critical-error quotes are adjudicated on the claim-verifier pipeline.** The per-answer
  verification dispatch, and the post-run pass that backfills it, now also fire when an answer
  carries `CriticalError` with a non-empty `CriticalErrorQuote` — not only when it has unverified
  or disputed claims. The quote is inserted at the head of the claim list, the verifier prompt
  gains a `CRITICAL ERROR ADJUDICATION:` preamble telling it that the first claim was marked a
  confidently asserted material falsehood and that *a claim absent from the rubric is not thereby
  false*, and the assessor's own accuracy evidence is handed over as the counter-claim (previously
  emitted only for a disputed verdict). The quote is deliberately **not** written to
  `UnverifiedClaimsJson` or `UnverifiedClaimCount`: that column means "claims the assessor could
  not adjudicate", and the quote is the opposite. It is carried in the prompt only and matched back
  by the verifier's verbatim echo, exactly as an unverified claim is. Cost is roughly one verifier
  call per critical error — measured well under $0.10 per run at the current roster.
- **`BenchmarkAnswerFlags.ContestedCriticalError` (2048) and `BenchmarkRun.ContestedCriticalErrorAnswerCount`.**
  Set when the quote's own verdict comes back `Supported` — the parser having already demoted a
  citation-less Supported to Indeterminate, so a supported verdict always carries a citation. A
  `Refuted` or `Indeterminate` verdict sets nothing. The flag joins `BenchmarkRunFinalizer`'s
  `AdvisoryFlags`, so it counts under `AdvisoryFlagAnswerCount` and **never** moves the clean count,
  the run status, the cap or any index. `ClaimVerificationJson` already stores every verification
  including this one, so no new column records the verdict itself. The count reads as *not recorded*
  rather than zero on any run before 19.
- **The flag says *contested*, never *overturned*, and every surface that prints it says so.**
  Report § Run Integrity carries a `Contested Critical Errors:` line naming the questions, the
  Advisory Flags breakdown gains `contested critical errors: N`, § 5 Issues reads
  *"Contested critical error (advisory, changed no score)"*, and the synthesis footer counts them
  beside refuted claims and disputed verdicts. The run detail's Run Integrity Notice and the
  diagnostics `--- INTEGRITY ---` line agree with all of it, and the per-answer badge carries the
  same wording. The reason for the insistence is symmetrical to the standing verifier caution,
  which by run 35 stood at seven recorded instances of the verifier being wrong — in both
  directions, a confident refutation of a true claim and a confident support of a false one — so
  **both verdicts are advisory evidence, and a human reads the cited code path before anything
  rests on either.** The synthesis prompt is additionally told, for each such answer, *"do not
  describe this answer as fabricating it"* — the one place the flag changes what a model is told,
  because the synthesis had previously narrated a spurious critical error as a fabrication.
- **Assessment prompt § 5 CRITICAL ERROR gains one sentence.** *"A claim the rubric does not mention
  is not thereby invented. Mark criticalError only for a claim the rubric's ground truth or your own
  verified knowledge contradicts; a claim the rubric merely omits belongs in `unverifiedClaims`
  (section 7), where the harness checks it against the source."* This is the failure mode all three
  spurious errors shared, and § 7 is the route that already existed for it.
- **Two source-tool contract fixes.** `source_code_view` **defaults `start_line` to 1** when neither
  it nor `search_term` is given, instead of failing the call: its schema had always listed only
  `file` as required and described `start_line` as optional, while the handler returned
  *"Missing start_line or search_term parameter"* and the tool guide documented `start_line` as
  **required** — three statements, two of them wrong, and a model that followed the schema paid a
  failed tool call for it (run 35's only failed call). Schema and guide now agree with the code.
  `get_function_definition`'s **miss payload** stops being information-free: it still opens with the
  service's own `No definition found for '<name>' of kind '<kind>'.` — which is what a reader and
  the diagnostics skill match on — and then names, from one bounded `filenames_only` probe (max 3
  files, 1,000 characters, non-regex, case-insensitive, exceptions and `Error:`-prefixed content
  swallowed into "no hit"), where the identifier occurs with match counts, or states that it does
  not occur in the indexed repository; then always the guidance that a struct member, function
  pointer or macro alias has no extractable body under that name and is read with
  `source_code_search` (with `context_lines`) or `search_definitions` instead. The whole payload is
  capped at **600 characters** — every tool result is re-sent to the model on each subsequent round
  of the same question, so a verbose miss is paid once per remaining round — and the builder sits
  inside a `catch` returning the bare sentence, which is therefore the resolver-defect payload
  rather than the ordinary-miss one. Both changes move `ToolGuidesSha256` and nothing else a run
  records.
- **Two instrument lines the run record already supported and no surface printed.** Each
  per-question tool-budget line gains `, tool rounds: R (X.X calls/round)`, counted as the distinct
  `IterationIndex` values across the answer's per-call rows, with a matching run-level **Tool
  Rounds** line in the Tool Usage Profile; and the run detail's Tool Usage Profile gains the
  three-way **outcome split** (`N succeeded, N failed, N refused by budget`) summed over the
  answers. Both obey the same null rule as every harness-17 column: **omitted, never zero**, when
  the answer has no per-call rows — and the outcome split is withheld entirely unless *every* answer
  that made a tool call has recorded outcomes, since a partial sum would read as a run-wide figure
  while silently omitting the answers it could not see. Rounds are what separate "many calls" from
  "many serial round-trips" as the cause of a question's latency, which is the measurement the
  standing serial-rounds observation has been waiting on.
- **The answer-framing opener detector widened again**, after under-counting against a hand count on
  seven consecutive runs: `fully` joins the source-as-subject sufficiency alternation,
  and the *"here's the …"* tail gains a second form — a sufficiency word inside the first sentence,
  then a sentence boundary, then the tail — which catches run 35's Q2 (*"covers it fully. Here's the
  breakdown"*). The tail branch still **requires** a sufficiency clause, because `_policy.md:11`
  forbids opening with the act of finding rather than with a lead-in to substance; a negative test
  guards the distinction. Detection only, as since harness 18: the text is not removed, because it
  is exactly what production chat sends.
- **Grader roster of record**, unchanged by this round and to be held still through the confirming
  run: assessor **Gemini 3.7 Flash @ `high`**, second opinion **GPT-5.6 Luna @ `high`**, claim
  verifier **GPT-5.6 Luna @ `high`**. Run 35 closed the Claude 5 Sonnet candidate series, so the
  confirming run opens a **new candidate series** and is a baseline rather than a comparison — the
  instrument criteria above are properties of the harness and the tools and verify whatever the
  candidate is, while every quality, latency and cost reading is the new model's first figure. Which
  is precisely why the graders must not move with it.

`ContestedCriticalErrorAnswerCount` is one added `int` column with a default of 0
(`AddContestedCriticalErrorAnswerCount`); nothing else needed a migration, and no figure is
backfilled.

### Harness Version 20 Updates

*2026-09-11.*

Prompted by run 36 (GPT-5.6 Sol, the first candidate on the new series): two of the run's two
refuted claims and its one out-of-rubric Accuracy deduction were **all three wrong**, and each was
checkable on disk. The claim verifier refuted a fear-spell saving-throw claim citing the wiki's
general skill-modifier table, when `src/zap.c:949` (plus the shared code at `:772`, `:815`, `:850`)
applies an *extra* per-skill-level penalty on top of that table that the page never states. It
refuted a −4 magic-cancellation claim about the touch of death citing a monster data table's `mcadj`
field, when the code that actually applies the penalty is `src/mcastu.c:793`. And the assessor's own
out-of-rubric deduction — that an experience-level-dependent prayer-timeout claim was wrong — was
itself wrong: `src/rnd.c:200` shows `rne`'s cap rising with `u.ulevel` past level 15. In every case
the grader cited a table or a secondary text instead of the code that computes the effect, and
nothing in the harness had ever told it not to. Run 36 also measured, at rung zero, that 483 of 781
candidate files under the GnollHack source's four target directories are git-ignored `bin`/`obj`
build output, indexed as game source since at least 2026-09-09.

`ScoringMethodVersion` stays at **10** — nothing here changes a score, a cap or an index —
but `BenchmarkAssessmentPrompt.HarnessVersion` moves to **20**, because the claim-verification
prompt's own text changes, a run records a new advisory count, and three tool guides move
`ToolGuidesSha256`. A run stamped 20 therefore differs from a run stamped 19 on **two** instrument
keys (`HarnessVersion`, `ToolGuidesSha256`) and not on `CandidateSystemPromptSha256`: `_policy.md` is
untouched and per-tool guides are not inlined into the candidate prompt.

- **The claim-verification prompt gains instruction 3a.** *"A claim about how a spell, attack or
  effect is computed is checked in the code that implements it — the case or function that applies
  the effect — not only in a data table (`src/monst.c`, `src/objects.c`) or a wiki page. A table or
  page that omits a term does not refute a claim that names the term; a Refuted verdict needs code,
  or a wiki statement, that contradicts the claim."* This is the shape both run-36 refutations
  shared, and the seventh, sixth and earlier standing verifier-caution instances before them.
- **The out-of-rubric Accuracy deduction is adjudicated on the same pipeline.** When an answer
  carries `OutOfRubricAccuracyDeduction` (512), `BenchmarkService.ExtractOutOfRubricBasis` reads the
  assessor's own accuracy evidence for the text after the `Not in rubric:` marker, up to the first
  sentence end or line break and capped at 600 characters, and submits it to the claim verifier as
  an adjudication claim — after the critical-error quote when an answer carries both (quote first,
  basis second), alone otherwise — under a new `OUT-OF-RUBRIC DEDUCTION ADJUDICATION:` preamble. The
  basis is the **assessor's** statement, not the answer's, so its verdict is excluded from the
  answer's Supported/Refuted/Indeterminate claim counts, from `RefutedClaim`, from the claims handed
  to the second reader, from the synthesis's refuted-claims list and from the report's Refuted Claims
  section — it stays only in `ClaimVerificationJson`, where its citation is the record, and is never
  written to `UnverifiedClaimsJson` or `UnverifiedClaimCount`. `NeedsClaimVerification` covers the
  new case, so the post-run backfill pass reaches it too.
- **`BenchmarkAnswerFlags.ContestedAccuracyDeduction` (4096) and
  `BenchmarkRun.ContestedAccuracyDeductionAnswerCount`.** Set when the basis's own verdict comes back
  `Refuted` — the statement the deduction rests on is false. `Supported` or `Indeterminate` clears
  it. The flag joins `BenchmarkRunFinalizer.AdvisoryFlags`, so it counts under
  `AdvisoryFlagAnswerCount` and **never** moves the clean count, the run status, the deduction, the
  cap or any index — advisory in the same sense as `ContestedCriticalError`, and the same caution
  applies: the flag says *contested*, never *overturned*. `ContestedAccuracyDeductionAnswerCount` is
  `int?`; it reads as **not recorded**, never zero, on every run before harness 20, which never
  adjudicated an out-of-rubric basis at all.
- **Every surface that prints `ContestedCriticalError` gains the matching line for this flag.**
  Report § Run Integrity carries a `Contested Accuracy Deductions:` line naming the questions when
  the count is non-zero; the Advisory Flags breakdown gains `contested accuracy deductions: N` or
  `not recorded`; § 5 Issues reads *"Contested out-of-rubric accuracy deduction (advisory, changed no
  score)"*; the synthesis footer counts it beside refuted claims, disputed verdicts and contested
  critical errors, and the synthesis prompt is told, per such answer, not to describe the deduction
  as an error of the answer. The admin API's run-detail DTO, the Angular Run Integrity Notice, the
  diagnostics `--- INTEGRITY ---` line (`contested accuracy deductions: N` / `not recorded`) and the
  per-answer badge (dashed red, labelled "contested deduction") all agree with it.
- **Three tool-contract fixes**, all found on run 36 and all moving `ToolGuidesSha256`:
  - `get_function_definition`'s `start_line: 0` now behaves as omitted — starts the body at the
    beginning — instead of returning the explicit out-of-range message it returned from the
    run-34 round on. `0` is never printed by any truncation notice, so it cannot be a stale value; it
    is the 0-based idiom for "from the start," and four of run 36's calls meant exactly that.
  - `nethack_wiki_search` declares its own `MaxResultLengthOverride` —
    `Tools:nethack_wiki_search:MaxResults × (PerResultChars + 128) + 500`, **16,140** at the current
    settings — so a full five-article yield of capped articles is no longer cut mid-article by the
    generic per-tool cap the way run 36 recorded four times at exactly 10,117 characters. The 128 is
    headroom for `CapArticle`'s own per-article truncation note, appended after the 3,000-character
    cut.
  - `nethack_wiki_view` now announces a non-exact resolution instead of silently returning the
    wrong article. `NetHackWikiService.GetArticleResolved` still takes the top hit of the
    title/filename query with no relevance floor — the article chosen has not changed — but when
    the normalised request and the normalised resolved title differ, `NetHackWikiViewTool` prepends
    `[No NetHack wiki article titled 'X'. Showing 'Y'. Other candidates: A; B; C; D.]`, built from
    the top five hits of the title/filename query and the top five of a `summary`-field query,
    distinct, capped at 600 characters. Run 36 Q11 asked for *"Two weapon combat"* and silently
    received `--- Combat ---` twice; the corpus's actual article is `Twoweapon`, reachable only
    through its summary text. An exact-title hit still carries no line — this is an ordinary
    `Success = true` result, never a miss.
- **The GnollHack source indexer skips `bin` and `obj` directories, alongside dot-directories.**
  `SourceCodeService.IsUnderExcludedDirectory` (renamed from `IsUnderDotDirectory`, see § *Harness
  Version 18 Updates*, H4) now also skips any repository-relative path segment equal to `bin` or
  `obj`, case-insensitively, logged in the same *"Skipped {Count} source file(s) under dot-, bin or
  obj directories."* line. `NetHackSourceCodeService` inherits the fix, since the indexer is shared.
  Measured 2026-09-11: 483 of 781 candidate files under the four target directories were git-ignored
  build output under `win\win32\xpl\**\(bin|obj)` — 480 `.txt` and 3 `.h` — indexed since at least
  2026-09-09; the index now holds about 298 files under those directories. This is a fix to the
  indexer, not to a prompt, and moves **no fingerprint**: `SourceCodeHeadSha` is the repository's Git
  HEAD, not a description of what the indexer kept.
- **Grader roster and everything else about the round.** No `ChatService` prose or `_policy.md` text
  was touched, so `CandidateSystemPromptSha256` does not move. The round's tool-contract and
  indexer fixes reach live chat identically to a benchmark run, since both read the production tool
  registry and the production source index.

`ContestedAccuracyDeductionAnswerCount` is one added nullable `int` column
(`AddContestedAccuracyDeductionAnswerCount`); it is left `NULL` on every existing row, read as *not
recorded* rather than zero, and no figure is backfilled.

### Harness Version 21 Updates

*2026-09-11.*

Prompted by run 37 (GPT-5.6 Sol, the confirming run for run 36's round): an OpenAI in-stream overload
took out 12 of the run's 18 questions, and the harness graded the 12 error strings as if they were
answers, reported `Provider Errors: 0`, and still published an Intelligence Index computed over the
6 surviving questions — a number over a sixth of the suite, carrying the same confidence as a
complete run. Nothing before this round distinguished a provider failure from an ordinary short
answer once the failure had already been turned into text and handed to the assessor.

`ScoringMethodVersion` stays at **10** — the scoring formula itself is unchanged, only whether a
partial run is allowed to publish a number over it — but `BenchmarkAssessmentPrompt.HarnessVersion`
moves to **21**. No `ChatService` prose and no `Overseer/ToolGuides/` file changed in this round, so
unlike harness 19 and 20 this is a **single-instrument-key** move: a run stamped 21 differs from a run
stamped 20 on `HarnessVersion` alone, not on `ToolGuidesSha256` or `CandidateSystemPromptSha256`.

- **A shared retry vocabulary now covers every provider.** New `ProviderErrorRetryPolicy.IsRetryable(string?)`
  is the one place the agent loop asks whether a provider error code is worth retrying, replacing the
  per-provider logic that had let an Anthropic or Google overload retry while an OpenAI one failed
  outright. An explicit deny list (`invalid_request_error`, `insufficient_quota`,
  `context_length_exceeded`, `authentication`) wins over the allow tokens, so a request that is wrong
  rather than merely rejected never retries.
- **`OpenAiResponsesProvider` stops discarding the failure it already has.** It now parses
  `response.failed` and a top-level `error` object into `OpenAI stream error: [{code}] {message}`,
  instead of the previous bare `OpenAI stream error: response.failed` that dropped both the code and
  the message. `ChatEvent.Detail` is new: a bounded raw provider failure payload, set only on `error`
  events, and `OpenAiResponsesProvider` fills it alongside the formatted message.
- **`AgentLoopRunner` logs a non-2xx status and a bounded response body at warning level
  unconditionally** — no longer gated behind the debug-log flag every benchmark call site leaves off
  — so a transport failure now leaves a trace in the server log even for a run that never set
  `ShowDebugLog`.
- **`BenchmarkProviderErrorClassifier` recognises four more shapes**: `server_error`,
  `[server_error]`, `Our servers are currently overloaded`, `rate_limit_exceeded`, and a bracketed
  numeric HTTP status — exactly the strings the `[code]` formatting above now puts in front of it.
- **`BenchmarkService` captures the first error and keeps later, distinct ones.** An answer that
  fails more than once records the first error text; a later attempt whose text differs is appended
  after ` | ` rather than discarded or overwritten. A **terminal-failure answer** — one classified
  `ProviderError` or `Failed` — now stores `AnswerText = null`, `ThoughtText = null`, and `0` output
  tokens whenever the provider reported no usage, instead of persisting the error text as if it were a
  graded response. The new `BenchmarkRunAnswer.ProviderErrorDetail` (`nvarchar(4000)`, nullable)
  carries the raw detail from `ChatEvent.Detail` when one was captured. The assessor is **skipped** on
  such an answer, with `"Not assessed: the provider failed the request; excluded from scoring."` in
  place of a score.
- **`BenchmarkRunFinalizer` withholds the indexes rather than publishing a partial number.** New
  `BenchmarkRun.TerminalFailureAnswerCount` (`int?` — `null` means *not recorded*, i.e. the run was
  finalised before harness 21). When **any** answer in the run carries a terminal failure (status
  `ProviderError` or `Failed`), `QualityIndex`, `QualityIndexStandardError`, `UnweightedQualityIndex`
  and `SpeedIndex` are all stored as `null` rather than computed over the survivors. A run that is
  `CompletedWithErrors` only because of assessment failures or model-produced empty answers is
  unaffected and keeps its indexes — the suppression is specific to a terminal *provider* failure.
  Re-running the failed questions re-finalises the run, so a fully repaired run regains its indexes.
  A minimum-item threshold (publish an index once fewer than some fraction of questions failed) was
  considered and rejected: it would still publish a number over an item set that no longer matches the
  suite's, and that number is comparable with no complete run.
- **The report says so instead of showing a number.** § 2 and § 7 print
  `Not computed — N of M questions failed at the provider` in place of an index whenever the
  suppression above applies; `Provider Errors` now counts terminal failures directly, so run 37's own
  report would have read 12 rather than 0; § 5 Issues prints the answer's full `ErrorMessage` and the
  HTTP status rather than a truncated form; and the Run Integrity block gains a
  `Terminal provider failures: N` line.
- **The tool-call log names the real reason for an empty answer.** An answer with no `ToolCalls` rows
  now reads `*No tool calls attempted — the answer failed before its first tool round.*` when the
  answer failed terminally, and the existing `*No tool calls attempted on this answer.*` otherwise; a
  run recorded before harness 17 keeps its own existing sentence, since it has no rows to reason about
  either way.
- **Admin UI.** The run history table's Cost column now shows the model-under-test's own cost with the
  whole run's catalog total (grading included) beneath it, rather than one blended figure. The run
  detail dialog gained a status badge and, when the run carries any terminal failure, a top failure
  alert carrying a **Re-run Failed Questions** button. Every index cell reads **"not computed"** rather
  than a blank or a stale figure whenever the suppression above applies.
- **The failed-question re-run selects empty answers too.** `BenchmarkRunFinalizer.NeedsReExecution`
  is now the single predicate behind both the controller gate and `RunFailedQuestionsAsync`, and it
  covers `EmptyAnswer` alongside `ProviderError` and `Failed`. Two cases made the old pair wrong: a
  cancel that lands while a question is in flight leaves the in-flight answer as `EmptyAnswer` with no
  finish reason, so cancelling a re-run created a row the re-run could never repair; and an answer with
  no text is an unanswered question whatever produced it, including a model that ended its turn
  normally and is scored 0. The client already agreed — `isAnswerFailed` counts `EmptyAnswer`, the
  button is enabled for one and the scope chip lists it — so this closes a client/server scope
  disagreement in which the server either refused with a 400 or silently skipped the row. The refusal
  text is now `This run has no failed, provider-error or empty answers to re-run.` Repairing a
  scored-0 empty answer replaces that 0 with a graded score and therefore moves the run's Intelligence
  Index; the re-run's own instrument fingerprints and scope are recorded as before.
- **A user cancel mid-answer records `Failed`, not `EmptyAnswer`.** Both answer status blocks in
  `BenchmarkService` now classify a run-level cancel that produced no text as `Failed` with
  `Canceled before the answer completed.`, ahead of the empty-flag branch. A per-question timeout
  cancels only its own linked token and is still reported as `Per-question timeout exceeded`.
- **`HarnessVersion` stays at `21` and `ScoringMethodVersion` stays at `10`** for the two bullets
  above: nothing changes about how a graded answer is scored or how the candidate is prompted — what
  changes is which rows an operator action repairs.
- **`BenchmarkAssessmentPrompt.HarnessVersion` is now `"21"`.**
- **2026-09-11: a cancel mid-answer now records `Canceled` (value 6), not `Failed`.** New
  `BenchmarkAnswerStatus.Canceled = 6` records that the operator canceled the run while a question's
  request was in flight; `BenchmarkService` writes `Status = Canceled`,
  `ErrorMessage = "Canceled by the operator before the answer completed."` and `HttpStatusCode = null`
  for such an answer. `BenchmarkRunFinalizer.HasTerminalFailure` and `HasUnresolvedWork` both cover the
  new status, and `NeedsReExecution` and `HasTransportDefect` cover it through them, so a canceled
  answer keeps the transport-defect treatment a `Failed` one already had. The classifier's caller-cancel
  check — `BenchmarkProviderErrorClassifier.Classify(Exception, string, bool callerCanceled)` — now runs
  ahead of its socket, I/O and HTTP rules rather than after them, so a caller-initiated cancel is never
  misclassified as a provider error regardless of which transport exception wraps it. `HarnessVersion`
  stays at `21` for the same reason as the bullet above: nothing about scoring or prompting moved.
- **2026-09-11: the run-detail DTO gains three re-run progress fields.**
  `BenchmarkRunDetailDto.rerunScopeOrderIndexes`, `rerunAnsweredOrderIndexes` and
  `rerunScoredOrderIndexes` report a server-tracked re-run's scope and progress, beside the existing
  `inFlightOrderIndexes`. The Angular client's `effectiveRerunScope` prefers the server-reported scope
  over its own client-captured `rerunScopeOrderIndexes` field whenever the server reports a non-empty
  one, and the new `runMeterTotal`, `runMeterAnswered`, `runMeterScored` and `runMeterFailed` getters
  read against that scope instead of the whole run once one is active, so the run progress dialog's
  meters and `runStageLabel` describe the re-run's own population rather than the suite's.
  `runProgressRows` checks `inFlightOrderIndexes` ahead of an answer row's own status, so a question
  re-executed in place reads `Answering`, and while the re-run is running a scope member not yet in
  `rerunAnsweredOrderIndexes` reads `Pending` rather than its previous failure. `runElapsedLabel`
  measures from `rerunStartedAtUtc` under a re-run scope, since `CompletedAtUtc` is preserved.
  `rerunCompletedAtUtc` is ignored by this label while the run's status is `Running`, since harness
  22 clears that column when a re-run starts rather than leaving it at a stale value.

**The motivating case.** Run 37 on 2026-09-11 lost 12 of its 18 questions to an OpenAI in-stream
overload. The harness graded the 12 error strings as if they were answers, reported
`Provider Errors: 0`, and still published an Intelligence Index computed over the 6 surviving
questions. Every change in this round traces back to closing one piece of that: the error is now
retried when transient (the shared retry policy), captured with its code and detail instead of
discarded (`OpenAiResponsesProvider`, `ChatEvent.Detail`), excluded from scoring rather than graded
(`BenchmarkService`), and the run's indexes are withheld rather than published over the remainder
(`BenchmarkRunFinalizer`).

Migration adds `BenchmarkRunAnswer.ProviderErrorDetail` (`nvarchar(4000)`, nullable) and
`BenchmarkRun.TerminalFailureAnswerCount` (`int`, nullable). Both are `NULL` on every existing row,
read as *not recorded* rather than as "no failure" or zero, and no figure is backfilled.

### Harness Version 22 Updates

*2026-09-11.*

Prompted by run 37's completed (repaired) report, once the failed-question re-run round finished: a
second re-run's own "Re-run elapsed" stat read 0s, because `RerunCompletedAtUtc` still carried an
earlier re-run's end stamp and that stamp fell before the new re-run's own start; and run 37 Q1 came
back Accuracy 5/6 with evidence reading "Matches rubric; …" — naming no defect — which went
unflagged while the synthesis went on to assert an accuracy defect nobody had named.

`ScoringMethodVersion` stays at **10** — nothing here changes how an answer is scored — and
`BenchmarkAssessmentPrompt.HarnessVersion` moves to **22**. No `ChatService` prose, no
`Overseer/ToolGuides/` file, and no knowledge-base article changed in this round, so — as with
harness 21 — a run stamped 22 differs from a run stamped 21 on `HarnessVersion` alone, not on
`ToolGuidesSha256` or `CandidateSystemPromptSha256`.

- **`RerunCompletedAtUtc` is cleared, not merely left stale, when a re-run starts.** Both
  `AdminBenchmarkController`'s re-run start and `BenchmarkService.RunFailedQuestionsAsync` now null
  the column at the same point they stamp `RerunStartedAtUtc`, and the Angular run-progress dialog
  ignores `rerunCompletedAtUtc` while the run's status is `Running`. Previously a second re-run's own
  "Re-run elapsed" timer read 0s, because the first re-run's end stamp predated the second re-run's
  start and the dialog subtracted the wrong pair.
- **`BenchmarkRun.RerunHarnessVersion`** (nullable, max 16 — migration
  `AddBenchmarkRerunHarnessVersion`) records the harness version the re-run itself ran under,
  stamped by `PopulateRerunInstrumentFingerprint` from `BenchmarkAssessmentPrompt.HarnessVersion`,
  and mapped through to the admin DTO. The exported report now renders a **"Repaired by a
  re-run"** manifest block (worded *failed-question re-run* before 2026-09-24, when the single-answer
  re-run began recording into the same columns) whenever `RerunStartedAtUtc` is set — not only when the
  re-run's prompt or ToolGuides hash differs from the original run's — naming the re-run's own span
  and its harness version, printing *"not recorded (re-run predates harness 22)"* for a re-run
  stamped before this round. The End Time line is now labelled **"original execution"**, followed by
  a **"Re-run span (UTC)"** line; Total Candidate Answer Time carries a note that it includes
  re-executed answers while the wall time stays the original execution's; and the report's "Measured
  overlap" line is not computed for a repaired run — replaced by a sentence that stage durations
  include the re-run, which lies outside the original wall clock. The run detail dialog's Elapsed
  Wall Time and Answer Duration cards carry the matching notes. **Caveat**: an earlier repaired run
  whose `CompletedAtUtc` already moved keeps its stored value, so the "original execution" label may
  be inaccurate on those legacy rows.
- **`BenchmarkVerdictConsistency.UnevidencedDeductionMaxLevel` moves from 4 to 5.** A level-5
  Accuracy or Completeness verdict whose evidence names no defect — the shape run 37 Q1 hit,
  "Matches rubric; …", unflagged, ahead of a synthesis that asserted an accuracy defect nobody had
  named — is now flagged `UnevidencedDeduction`, routed to the blind second reader, and, where the
  evidence reads `Not in rubric:`, submitted to the claim verifier as an out-of-rubric deduction
  through the same pipeline as § *Harness Version 20 Updates*. Expected cost: roughly one extra
  second opinion per run.
- **`wiki_search`'s `max_results` is clamped to `1..max(1, configured)`**, where *configured* is
  `Tools:wiki_search:MaxResults` (5) — mirroring the clamp `nethack_wiki_search` already had. Its
  tool-schema description now reads *"default and maximum 5; larger values are clamped"*. The clamp
  is what keeps a full yield inside the 13,000-character per-tool result override; run 37 Q4 asked
  `max_results: 10` and got 13,117 stored characters, cut mid-result at the override.
- **The source definition matcher gains two more shapes, in `SourceCodeService`.**
  `search_definitions` and `get_function_definition` now try a second function alternative after the
  original NetHack-style `^name\s*\(` line — a same-line return type followed by `[*]name(`, where
  the first token is neither `extern` nor a control keyword (`return`, `else`, `if`, `while`, `for`,
  `switch`, `case`, `goto`, `sizeof`) and the line does not end in `;` — and, for kinds `type` or
  `any`, a closing-brace typedef alternative `^\s*\}\s*name\s*;` whose body locator walks back,
  bounded at 400 lines, to the nearest `typedef struct|union|enum` opener and returns the whole
  block, falling back to the closing line and its usual 10-line window when no opener is found. Run
  37 Q15 missed both shapes: `void lib_print_glyph(...)` at `win/win32/xpl/libshare/libproc.c:470`,
  and `} gbuf_entry;` at `src/display.c:161`.
- **Admin UI.** The run-progress dialog is now full viewport height (`calc(100dvh - 16px)`, 8px on
  phones), and the run detail dialog prints "n/a" for cache-creation tokens when the provider —
  OpenAI — reports none, rather than a stray zero.

Migration `AddBenchmarkRerunHarnessVersion` adds `BenchmarkRun.RerunHarnessVersion` (`nvarchar(16)`,
nullable). It reads `NULL` as *not recorded* on every row from before this round, never as an empty
harness version, and no figure is backfilled.

### Harness Version 23 Updates

*2026-09-11.*

Prompted by run 38 (GPT-5.6 Sol @ `medium`, harness 22, Intelligence Index 95 ± 4, 0 critical
errors, 190 tool calls with 0 failures). The run confirmed the harness-22 round on every countable
criterion except one: the level-5 unevidenced-deduction detector did not fire on the sentence shape
it was added for. Q1 came back Accuracy 5/6 with evidence reading *"Matches rubric; accurately
describes Gnoll alignment options, available roles, and core racial traits without error."* — a
denial written as a sentence rather than as the boilerplate `IsNoFaultEvidence` matches — and Q6
came back Completeness 5/6 with nothing in its evidence but an `OUT-OF-SCOPE:` clause, which is a
point the instruction says not to deduct for.

`ScoringMethodVersion` stays at **10** — nothing here changes how an answer is scored — and
`BenchmarkAssessmentPrompt.HarnessVersion` moves to **23**. No `ChatService` prose and no
knowledge-base article changed, so `CandidateSystemPromptSha256` does not move; **two
`Overseer/ToolGuides/` files do change**, so `ToolGuidesSha256` does. A run stamped 23 therefore
differs from a run stamped 22 on `HarnessVersion` **and** `ToolGuidesSha256`, which is below Tier B:
compare the two on counts and per-question thresholds, not as a reproduction pair.

- **The unevidenced-deduction detector reads for a named defect, not for a boilerplate string.**
  `BenchmarkVerdictConsistency.HasUnevidencedDeduction`'s Accuracy clause now asks
  `!NamesAnAccuracyDefect(...)` instead of `IsNoFaultEvidence(...)`, so the denial vocabulary
  `DefectDenialRegex` already recognised — *"… without error."*, *"… with no factual errors."* —
  flags when it carries no `FalsehoodRegex`, `OmissionRegex` or `ConcessionRegex` word. Evidence
  naming a rubric point and an incorrect value is unaffected.
- **A Completeness deduction whose only evidence is an `OUT-OF-SCOPE:` clause is flagged too.**
  `NamesACompletenessDefect` strips the marker's own sentences from the evidence and applies the
  same two tests to what is left, so the marker no longer stands in for a defect nobody named. The
  companion predicates `IsOutOfScopeOnlyDeduction` and `IsFormOnlyDeduction` are what the report and
  the admin DTO count: how many of the recorded `OUT-OF-SCOPE:` and `FORM:` points sit beside a
  level below 6 with no in-scope defect named. The Completeness case raises `UnevidencedDeduction`
  and routes to the blind second reader; the Readability case is **counted only** — Readability is
  not a flagged dimension. Expected cost: roughly one extra second opinion per flagged answer
  (run 38 would have added two).
- **The exported report and the run-detail card say what the instruction was, not that it was
  followed.** Both blocks now read *"recorded under the `OUT-OF-SCOPE:` marker; the instruction is
  not to deduct for them"*, followed — only when the count is non-zero — by a line naming how many
  of them sit beside a sub-6 level with no in-scope defect named, and which questions they are.
- **`readabilityEvidence` is persisted.** `BenchmarkService.BuildEvidenceJson` gains a `readability`
  key beside `accuracy` and `completeness`. On a run graded before this round the key is absent and
  the per-answer `ReadabilityFormOnly` column is the only marker signal, which is what the
  `FORM:` count falls back to; it cannot distinguish a marker-only evidence string from one that
  also named a real defect, and a run from before this round is read on that basis.
- **The tool-call log export prints the size the tool returned.** `BenchmarkToolCallLogBuilder`'s
  truncated-result label read *"Result (first 600 of 12,897 chars, stored 12,000)"* from this round until harness 25 added the tail —
  `ResultLengthChars` is the length `ToolExecutor` handed over, and the stored note appears only when
  the recorder kept less than that. Previously the label named the stored length as if it were the
  result length, which reads as a tool returning less than it did.
- **The run-detail summary cards carry the model-under-test cost.** A **Model Under Test** card
  showing `EstimatedCandidateCost` and its share of the catalog total sits immediately before the
  **Estimated Cost** card, which keeps its whole-run catalog figure — the pair the Run History cost
  cell already shows. The card is rendered only when the candidate cost is priced.
- **Two chat-visible tool contracts** (they move `ToolGuidesSha256`; both were found at rung zero in
  run 38's tool-call log):
  - **`source_code_view` stops at a whole line.** `SourceCodeService.GetFileExcerpt` takes a
    character budget, which `SourceCodeViewTool` fills from `ToolExecutionContext.MaxResultLength` —
    the same cap `ToolExecutor` applies afterwards — and appends
    `[Output truncated at line X of Y requested (file line N). Call again with start_line=N+1 to
    continue.]` when the requested range does not fit. A budget of 0 keeps the unbudgeted behaviour
    for every other caller. Three of run 38's 12 `source_code_view` calls were cut mid-line by the
    generic cap, with nothing saying where to resume.
  - **`get_function_definition` falls back to any kind.** When the requested `type` has zero matches
    and a definition of another kind exists, the tool returns it behind
    `[No {kind} named '{name}' in the indexed {repository} source; showing the {found} definition
    instead.]`. The fallback fires only on a zero-match requested kind, so a name with both a
    function and a macro still returns the function when `function` is asked for. Three of run 38's
    23 calls spent a tool round each missing a macro under `type: "function"`.

No EF Core migration: the two new counts are computed at DTO build time from the run's answers, as
`CompletenessOutOfScopeCount` already is.

### Harness Version 24 Updates

*2026-09-12.*

Prompted by run 39 (Gemini 3.7 Flash @ `medium`, harness 23, Intelligence Index 68 ± 8, both of its
applied critical errors traced to rubric defects) and its tool-diagnostics pass: `wiki_search` missed
an article whose title differed from the query only by an inflection, costing 22% of the run's input
tokens recovering from it; every Anthropic grading call paid the 1.25× cache-write price and never
read a cache back; the Critical Errors line could read "0 confirmed" while two caps were in fact
applied and disputed; and five suite items were re-assessed between runs 38 and 39 with no
comparability key moving, so the report could not show why the weights had changed.

`ScoringMethodVersion` stays at **10** — nothing here changes how an answer is scored — and
`BenchmarkAssessmentPrompt.HarnessVersion` moves to **24**. `Overseer/ToolGuides/wiki_search.md`
gains a sentence on query matching, so **`ToolGuidesSha256` moves**; no `ChatService` prose and no
knowledge-base article changed, so `CandidateSystemPromptSha256` does not move. A run stamped 24
therefore differs from a run stamped 23 on `HarnessVersion` **and** `ToolGuidesSha256`, which is
below Tier B: compare the two on counts and per-question thresholds, not as a reproduction pair.

- **`wiki_search` stems both wiki indexes (T1, T1b).** `WikiService` and `NetHackWikiService` build
  their Lucene analyzer as `EnglishAnalyzer` (Porter stemming) rather than `StandardAnalyzer`, so a
  query differing from an indexed title only by inflection (`material` / `materials`) still engages
  the title's ×5 boost. `GetRelevantSnippets` gains an overload that also returns the query's total
  hit count, and `WikiSearchTool` appends *"[Showing N of M matching articles — narrow the query, or
  add a distinctive word from the article's title, to see others.]"* whenever a query matched more
  articles than were returned.
- **Grading prompts carry their shared preamble as a frozen, cacheable segment (H1).**
  `BenchmarkAssessmentPrompt.BuildPerQuestionPrompt` splits into `BuildPerQuestionPreamble` (the text
  identical across every question of a suite) and `BuildPerQuestionBody` (the per-question
  remainder). `AgentRunRequest` gains `CacheConversationTail` (default `true`); the assessor, second
  opinion, claim verifier and synthesis grading call sites in `BenchmarkService` set it `false` and
  send the preamble as a `SegmentedPrompt` frozen prefix, so a single-shot request whose only message
  is never re-sent gets no conversation-tail cache breakpoint — the preamble is written to cache once
  per suite and read on every later question instead of being rewritten, uncached, on each one.
- **The Critical Errors line states the applied count before any split (H2).** It now reads *"N
  applied (question(s) …)"*, followed — only when the count is non-zero — by how many of those the
  second reader disputed, how many critical errors the second reader raised on its own that the
  assessor never applied, and which applied quotes the claim verifier checked against the source or
  wiki and found supported. The Contested-Verdict Sensitivity line is recomputed over the disputed
  set alone and reworded *"… with each split resolved at the second reader's score (raises and lowers
  both)"*.
- **A Fundamental `SuiteAssessedDifficulties` key, and both revision axes are shown (H5).** Assess
  Difficulty rewrites `BenchmarkQuestion.AssessedDifficulty` without bumping `ItemRevision`, so two
  runs could previously agree on `SuiteItemRevisions` and still have been weighted by two different
  exams with nothing to say so. Each question header now prints `Item rev N`, the Comparability
  block gains a *Suite item revisions* line and an *Assessed difficulties* line (both Q-by-Q), and the
  Band Agreement sentence is qualified *"… until the item is re-assessed or edited"* rather than
  claiming unconditional byte-identity.
- **Default suites move from one hardcoded file to a discovered catalog, and runs record their
  origin.** See *Default Suites* under § 5.

Migration `AddBenchmarkDefaultSuiteKey` adds `BenchmarkSuite.DefaultSuiteKey` (`nvarchar(64)`,
nullable), `BenchmarkSuite.DefaultSuiteVersion` (`int`, nullable) and `BenchmarkRun.DefaultSuiteKeyUsed`
(`nvarchar(64)`, nullable); no data is backfilled, so a suite imported before this round reads
`DefaultSuiteKey` as `null` — "unknown, match by name" — rather than as "custom".

### Harness Version 25 Updates

*2026-09-12.*

Prompted by run 40 (Gemini 3.7 Flash @ `medium`, harness 24, Intelligence Index 68 ± 8, unchanged
from run 39): on seven of eighteen questions the assessor deducted Accuracy for statements the
candidate had taken verbatim from the wiki or the source its tools returned, and the claim verifier
later supported every such claim it checked. The unevidenced-deduction detector caught one of the
nine, because it matches only `verif*` vocabulary and this assessor wrote "not corroborated by the
rubric", "asserted without source support" and "could not be adjudicated"; the final synthesis, which
sees refuted claims but not supported ones, called four verifier-supported facts invented. Two tool
records were also unreadable rather than wrong: the tool-call log export cut every result to its
first 600 characters, hiding the end markers a `wiki_search` result carries, and a
`search_definitions` miss returned a bare sentence with nothing to act on.

`ScoringMethodVersion` stays at **10** — nothing here changes how an answer is scored, and the new
index is advisory — and `BenchmarkAssessmentPrompt.HarnessVersion` moves to **25**.
`Overseer/ToolGuides/wiki_search.md` and `Overseer/ToolGuides/search_definitions.md` each gain a
sentence, so **`ToolGuidesSha256` moves**; no `ChatService` prose and no knowledge-base article
changed, so `CandidateSystemPromptSha256` does not move. A run stamped 25 therefore differs from a
run stamped 24 on `HarnessVersion` **and** `ToolGuidesSha256`, which is below Tier B: compare the two
on counts and per-question thresholds, not as a reproduction pair.

- **The unevidenced-deduction detector matches the vocabulary assessors actually use (H1).**
  `BenchmarkVerdictConsistency.UnverifiabilityRegex` also matches *corroborat*, *unsupported*,
  *without basis / support / source support / corroboration*, *adjudicat*, *beyond the verifiable
  rubric*, *outside the rubric*, *not in / from / covered by / supported by / given in the rubric*,
  *the rubric does not support / cover / mention / corroborate / state / include*, and *withheld /
  kept / held below level 5 or 6*. The `DefectRegex` guard is unchanged, so evidence that also names
  a real fault still counts as a legitimate deduction. The `not in the rubric` alternative carries a
  `(?!\s*:)` lookahead so the mandatory `Not in rubric:` marker below is read on its own path rather
  than here.
- **Two sentences in the assessment prompt (H2).** After preamble instruction 8: *"A claim outside
  the rubric does not lower the ACCURACY level either — do not withhold level 5 or 6 because the
  answer states something the rubric does not cover. Levels 5 and 6 are withheld only for a named
  defect."* (From scoring method 11 the last sentence reads *"Award an ACCURACY level below 6 only for
  a named statement in the answer that is wrong or imprecise."* — see Harness Version 31.) And the
  evidence rule now makes the marker mandatory rather than exemplary: an
  out-of-rubric deduction's evidence sentence **MUST** begin with `Not in rubric:`, because that
  basis is what the harness sends to the claim verifier, and a deduction written without the marker
  is never checked.
- **The final synthesis receives the verifier-supported claims (H3).** Each per-question verdict
  summary now carries up to six supported claims (300 characters each), with the out-of-rubric basis
  and the critical-error quote excluded as they already were from the refuted list, and the synthesis
  prompt names them: *"Verifier-supported claims (checked against source/wiki; do not describe any of
  these as invented, fabricated, unsupported, uncorroborated or inflated)"*. Instruction 2 adds that
  a claim listed as verifier-supported is a fact of the game whatever the rubric omitted, so naming
  it as embellishment is a grading error rather than a finding. Costs roughly 2k input tokens per
  synthesis.
- **Verification-cleared Accuracy deductions, and a sensitivity index for them (H4).** An answer is
  *verification-cleared* when it carries `UnevidencedDeduction`, has unverified claims, has no
  refuted and no indeterminate claim, and sits at Accuracy level 5 or below — that is, Accuracy was
  docked citing only claims the rubric did not cover, and every such claim the verifier checked was
  supported. *Assessor Findings* names them after the *Unverified Claims* line, and **§ 2** and
  **§ 7 Final Indices** print a **Verification-cleared Accuracy Sensitivity**: the Intelligence Index
  recomputed with Accuracy one level higher (capped at 6) on exactly those answers, every other level
  and every other answer unchanged, the critical-error cap and the unanswered-at-0 rule applied as in
  the real index, and quality recomputed from the run's own scoring profile snapshot. It is the
  instrument's share of the Accuracy shortfall, as `OUT-OF-SCOPE:` is of Completeness. Advisory: it
  changes no score, has no DTO, no column and no client surface, and is omitted when the population
  is empty — exactly like *Contested-Verdict Sensitivity*.
- **Claim counts exclude the critical-error quote (H5).** The quote is the assessor's statement about
  the answer, not a claim the answer makes, and it was already excluded from the refuted list and the
  `RefutedClaim` flag by way of `WithoutOutOfRubricBasis`'s sibling treatment of the basis. A
  `WithoutCriticalErrorQuote` filter now removes it from `ClaimsSupportedCount`,
  `ClaimsRefutedCount` and `ClaimsIndeterminateCount` as well, so *Unverified Claims* and *Claim
  Verification Yield* total the unverified-claim count. `ClaimVerificationJson` still stores every
  verification, and the `ContestedCriticalError` decision still reads the unfiltered set.
- **The tool-call log export writes the tail of a cut result (H6).** A result longer than 600 + 240
  characters is written as its first 600 characters, the marker `… [head of 600 chars; tail of 240
  chars follows]`, and its last 240; the label reads *Result (first 600 and last 240 of N chars…)*.
  A shorter result is written whole. The end of a tool result is where `wiki_search` puts its
  `[Showing N of M matching articles …]` line and `source_code_search` its truncation notice, and
  head-only export made both invisible to a diagnostics pass.
- **A `search_definitions` miss says where the symbol does occur (H7).** The occurrence probe
  `get_function_definition` has carried since the run-35 round moves into a shared
  `SourceMissContentBuilder`, and `search_definitions` uses it with its own guidance: on a hit, that
  the symbol occurs but no definition line matched this kind, so try `kind: "any"` or
  `source_code_search` with `context_lines` on the named file; on no hit, to check the spelling or
  use `list_indexed_files` / `source_code_search` with `filenames_only: true`. Bounded and capped at
  600 characters as before, and inside a `catch` that returns the service's bare sentence, which is
  therefore the resolver-defect payload. `get_function_definition`'s own payload is byte-identical to
  what it produced before the lift. `search_definitions.md` gains one sentence saying a miss is to be
  followed rather than retried.
- **`wiki_search.md` gains a stop rule (T1).** *"When a returned article answers the question as
  asked, answer from it — including its examples and lists — and go to the source only for a part the
  article does not cover or when the question asks for the implementation."* Run 40's Q2 spent 17
  rounds and 21 % of the run's input tokens exploring the source after the wiki article had already
  answered; this is the fourth observation of serial over-exploration and the second candidate to
  show it.

### Harness Version 26 Updates

*2026-09-12.*

Prompted by run 41 (Gemini 3.7 Flash @ `medium`, harness 25, Intelligence Index 78 ± 3): on ten of
eighteen questions the assessor docked Accuracy citing only claims the rubric did not cover, and the
claim verifier later supported every such claim it checked on eight of them. The detector caught
three, because its defect guard fired on words inside clauses that *denied* a defect — "no
adjudicable falsehood found", "nothing stated contradicts the rubric" — and its vocabulary lacked
*unconfirmed* and *beyond what can be confirmed*; and the `Not in rubric:` marker harness 25 made
mandatory was written **zero** times, so no such deduction reached the verifier. Seventeen of
seventeen `FORM:` markers again sat beside a sub-6 Readability with nothing else named. At rung zero
the run also showed `monster_lookup` returning neighbouring articles beside the exact hit, and the
tool-call record cutting a `wiki_search` result that had reached the model whole.

`ScoringMethodVersion` stays at **10** — the new index is advisory and no formula changed — and
`BenchmarkAssessmentPrompt.HarnessVersion` moves to **26**. `Overseer/ToolGuides/monster_lookup.md`
and `Overseer/ToolGuides/item_lookup.md` each gain a sentence, so **`ToolGuidesSha256` moves**; no
`ChatService` prose and no knowledge-base article changed, so `CandidateSystemPromptSha256` does not
move. The candidate message, which no key fingerprints, now carries the no-greet instruction (H5).
The difficulty prompt is re-anchored (§ D below), so a suite re-assessed under it moves
`SuiteAssessedDifficulties` for every item and `SuiteItemRevisions` for any item edited alongside:
**a run of suite 6 stamped 26 is not comparable with any earlier run of suite 6** on
`HarnessVersion`, `ToolGuidesSha256`, `SuiteAssessedDifficulties` and `SuiteItemRevisions`.

- **The unverifiability detector reads denials as denials (H1).**
  `BenchmarkVerdictConsistency.IsUnverifiabilityGroundedDeduction` strips each clause that denies a
  defect — *no / nothing / none / neither … false, falsehood, contradicts, error, inaccurate,
  misstated*, and *the rubric does not state / mention / include / cover / list* — before applying
  `DefectRegex`, so only the remainder can veto the flag. A concession leading into a real charge
  ("no error in the table, but the level is wrong") keeps its charge: only the denial clause is
  removed. `UnverifiabilityRegex` also matches *unconfirmed*, *beyond (the) verifiable*, *beyond what
  can be verified / confirmed*, *without (any) rubric support*, *adjudicable* and *not established /
  supported by the source / rubric*. The ten run-41 and seven run-40 evidence strings are unit tests.
- **A flagged deduction is adjudicated even without the marker (H2b).** When the detector flags an
  Accuracy deduction whose evidence carries no `Not in rubric:` marker, `BenchmarkAssessmentParser`
  also sets `OutOfRubricAccuracyDeduction`, and `BenchmarkService.OutOfRubricBasisOf` falls back from
  the marker's sentence to `BenchmarkVerdictConsistency.UnverifiabilityBasisOf` — the first evidence
  sentence carrying the unverifiability wording, capped at 600 characters. The verifier checks it
  exactly as it checks a marked basis, and a Refuted verdict sets `ContestedAccuracyDeduction`;
  an Indeterminate one sets nothing. The flag's meaning is unchanged: the assessor docked Accuracy
  from its own knowledge.
- **FORM-cleared Readability Sensitivity (H3).** Beside *Verification-cleared Accuracy
  Sensitivity*, § 2 and § 7 Final Indices print the Intelligence Index recomputed with Readability one
  level higher (capped at 6) on every answer whose only Readability basis was a rubric `FORM:`
  suggestion, through the same recompute as the Accuracy line. Advisory, no score, no DTO, omitted
  when no answer qualifies.
- **The tool-call record stores what the model received (H4).** When
  `Benchmark:ToolCallRecord:MaxResultChars` is unset, `BenchmarkToolCallRecordLimits.Resolve` derives
  it from the larger of `Benchmark:MaxResultLength` and the largest `MaxResultLengthOverride` among
  the run's allowed tools (`ToolRegistry.LargestResultLengthOverride`), plus 2,000 — 18,140 at current
  settings, from `nethack_wiki_search`'s 16,140. A stored cut ends
  `... [Record truncated: stored N of M characters]`, so it can no longer be mistaken for
  `ToolExecutor`'s pre-run-28 `... [Result truncated for length]`, which it had reused.
- **The candidate message carries chat's no-greet instruction (H5).** Chat appends
  `[System instruction: Do not greet me, unless I greet you first.]` to every non-first user turn;
  the benchmark message carried neither that nor the greeting instruction, so on run 41 Q8 the model
  introduced itself as the frozen prompt's greeting rule asks. The string is one constant,
  `ChatService.NoGreetInstruction`, used at both sites; the chat text is byte-identical.
- **`monster_lookup` and `item_lookup` return an exact-title article alone (T1).**
  `WikiService.GetLookupContext` runs the category query for the top 8 hits; when exactly one title
  equals the normalised request it returns that article and one `[Other matches: …]` line naming up
  to four other titles, two or more exact titles return the `Several wiki articles are titled '…'`
  disambiguation, and anything else returns the previous top-5 join. The unfiltered fallback is
  unchanged. Each guide gains: *"When the name matches an article title exactly, only that article is
  returned, with other matching titles listed on one line; pass one of those titles to get a
  different article."*
- **The difficulty prompt anchors bands on the work an answer needs (§ D).** The Advanced band had
  named *"subtle patch-specific GnollHack changes"* and the Simple band *"widely known NetHack lore"*,
  so on a suite that is GnollHack-specific by design every item drifted upward (+25 to +29 mean
  signed delta on every run since 29, 11 of 18 above the authored band on run 41). The bands now read:
  Simple — a single fact, value or short list from one wiki article or one structured stats lookup,
  whichever variant it belongs to; Intermediate — two or more sources or mechanics, a formula whose
  terms, conditions and exceptions must all be stated, or a NetHack-versus-GnollHack contrast on one
  mechanic; Advanced — reasoning over the C implementation, multi-function control flow, probability
  or scaling derivations, or interactions across several systems. A new critical instruction says
  that a fact being GnollHack-specific does not by itself raise the band. The authored bands are
  untouched. Suite 6 was re-assessed as a whole after this change.

### Harness Version 27 Updates

*2026-09-12.*

Prompted by run 42 (GPT-5.6 Luna @ `high`, harness 26, Intelligence Index 76 ± 5): at rung zero the
run showed two tool contracts that had never worked as written. Four `wiki_search` calls whose
unfiltered query had hits returned nothing at all, solely because a `category` of `monster` or `item`
was set; and `item_lookup` on an exact article title returned a guide article rather than the article
whose title it had been given, because the exact-title branch harness 26 added sits behind the same
filter and was therefore unreachable. Separately, `nethack_wiki_view("Spellcasting")` resolved twice
to *Spellcaster* while *Spellcasting* itself was listed as a candidate. On the grading side one answer
was recorded at Readability **0** beside Accuracy 5, Completeness 5 and Conciseness 3, with nothing in
the assessor's comment naming a readability defect — worth about 2.6 index points, unflagged and
ungated by a second reader — and the assessor's own text is stored nowhere, so whether the field was
omitted or deliberately zero cannot be settled from the record. The detectors again caught fewer
instances than a hand count (9 of 14).

`ScoringMethodVersion` stays at **10** — no formula changed and no flag here moves a score — and
`BenchmarkAssessmentPrompt.HarnessVersion` moves to **27**. `Overseer/ToolGuides/wiki_search.md` gains
one sentence, so **`ToolGuidesSha256` moves**; no `ChatService` prose changed, so
`CandidateSystemPromptSha256` does not move. Two columns are added by the
`AddDimensionOutlierAndAssessmentRawText` migration: `BenchmarkRuns.DimensionOutlierAnswerCount` and
`BenchmarkRunAnswers.AssessmentRawText`.

- **The `category` filter is case-insensitive and matches inside the wiki (T1).** `WikiService`
  indexes a `pathlower` `StringField` — the wiki-relative path with its extension, lower-cased and
  forward-slashed — and `GetRelevantContext`, `GetRelevantSnippets` and `GetLookupContext` build their
  category clause against it, folding the supplied value the same way. A `StringField` is one exact,
  case-sensitive term, so the previous clause over the raw absolute `path` could never match a
  capitalised directory from a lower-case category, and a category value excluded every hit rather
  than narrowing them; because `monster_lookup` and `item_lookup` run the category query first, the
  harness-26 exact-title contract was reached only through their unfiltered retry, which does not
  narrow. Indexing against the wiki-relative path also means a category can never match a directory
  above the wiki root. The `wiki_search` schema and guide now name the wiki's real top-level
  directories — Artifacts, Conducts, Development, Difficulties, Dungeon, Guides, Items, Monsters,
  Races, Roles, Rooms, Skills, Spells — and say the match is case-insensitive.
- **`nethack_wiki_view` prefers an exact title (T2).** `NetHackWikiService.GetArticleResolved` takes
  the first title/filename hit whose normalised title equals the normalised request — trimmed,
  internal whitespace collapsed, lower-cased, the rule `NetHackWikiViewTool` already applies when it
  decides whether to prepend a resolution line — and falls back to the top hit when none does. The
  title query widens from 5 hits to 8 so a stem-equal title cannot crowd the exact one out of the
  window; the candidate list keeps its first-5 semantics. Under Lucene's English stemmer
  *Spellcasting* and *Spellcaster* reduce to the same token, which is how the top hit came to be the
  wrong article.
- **The four assessor levels are required, and the assessor's text is kept (H1).**
  `BenchmarkAssessmentParser` reads `accuracyLevel`, `completenessLevel`, `concisenessLevel` and
  `readabilityLevel` through `TryGetIntProperty`; a missing or non-numeric one fails the parse with a
  message naming the field, which the existing per-question retry feeds back to the assessor, and a
  second failure follows the existing parse-failure path to `AssessmentStatus.Failed`. A level of 0 is
  now something the assessor stated rather than something the parser supplied. Every graded answer
  stores the assessor's final text in `BenchmarkRunAnswer.AssessmentRawText`, capped at 8,000
  characters, whether or not the parse succeeded.
- **A collapsed dimension is flagged and read twice (H2).**
  `BenchmarkVerdictConsistency.IsDimensionOutlier` is true when exactly one of the four levels is 1 or
  below while the other three are 3 or above, and neither the assessor's comment nor that dimension's
  evidence names a defect of that kind — the class's own defect and omission vocabulary for Accuracy
  and Completeness, and *filler / rambling / verbose / padding / tangent / repetition* and *format /
  markdown / heading / table / garbled / unreadable / wall of text / incoherent / disjointed* for
  Conciseness and Readability, which carry no evidence string of their own. Answers that do not count
  toward the quality index are excluded at the call site, so a provider error graded 0/0/0/0 never
  qualifies. The verdict carries `BenchmarkAnswerFlags.DimensionOutlier`, is counted into
  `DimensionOutlierAnswerCount`, appears in the Run Integrity breakdown, in an Assessor Findings line
  and as a per-question harness note, and triggers a second opinion — placed after `OmissionAsAccuracy`
  and before `UnverifiedClaims`, so the stronger triggers keep their priority. Advisory: no index moves.
- **The detectors read more of the assessor's vocabulary (H3).** `UnverifiabilityRegex` also matches
  *not supported* (outside the *by the rubric* form it already carried), *not established*, *does not
  define*, *lacking / lacks / without / no source-level or implementation detail or precision*, and
  *below level 6 beyond*; `UnverifiabilityDenialRegex` reads *incorrect* and *wrong* as denied defects,
  so *"no incorrect claim"* and *"nothing wrong"* no longer veto the flag; and `OmissionRegex` matches
  *never stated / states / mentioned* and *is not stated*. The five run-42 evidence strings are unit
  tests, alongside four strings from the same run that name a real defect and must stay unflagged.

### Cross-Model Comparison Round (2026-09-12) — No Version Bump

*Prompted by runs 43–46.* Nothing here grades anything: `BenchmarkAssessmentPrompt.HarnessVersion`
stays at **27**, `ScoringMethodVersion` at **10**, and `CandidateSystemPromptSha256` and
`ToolGuidesSha256` do not move. The round repairs the Admin → AI Benchmark → Run History →
*Cross-model comparison* wizard, which the four-run set had exercised harder than any earlier round.

**The wizard is four steps, not three.**

| Step | Title | Reachable when |
|---|---|---|
| 1 | Sources | always |
| 2 | Comparability & filters | a comparison has been computed |
| 3 | Table | a comparison has been computed |
| 4 | Figures | something in it can actually be charted |

The comparison table used to sit at the bottom of step 2, below the filters, the caveats and the
exclusions, and could not leave the screen. It is now step 3 of its own, and step 4 is the figures.
A set that no figure can draw therefore still opens its table: step 3 is reachable on a comparison
alone, and only step 4 is gated on a chartable entry.

- **The table step carries the provenance line the figures already carried** — suite, pricing basis,
  the reference condition's must-match signature abbreviated to twelve hex characters, and the time
  the comparison was computed — so an exported table is matchable against the wizard's methods block
  and against an exported figure from the same comparison.
- **Eight export formats**, chosen from the *Table export format* select and written by **Download
  table**: **Excel (`.xlsx`)**, CSV, TSV, Markdown, JSON, HTML, PNG and WebP. Excel is first and is
  the default, because it is the format that opens correctly on a Windows admin's machine whatever
  their list separator; the CSV and TSV writers carry a UTF-8 BOM and CRLF rows for the same reason.
  The `.xlsx` file has a *Comparison* sheet with a frozen bold header, typed numeric columns with
  per-kind formats, and a second *Provenance* sheet carrying the fields above and one row per set
  notice. Machine formats (XLSX, CSV, TSV, JSON) carry raw values; human formats (Markdown, HTML,
  PNG, WebP) carry the text exactly as the on-screen table prints it, the same formatters serving
  both so the screen and the file cannot drift. CSV and TSV prefix a text cell beginning with `=`,
  `+`, `-`, `@`, tab or CR with an apostrophe (OWASP formula injection); a typed XLSX string cell is
  never evaluated and needs no guard.
- **The export is the whole matching set, not the visible page.** Every entry passing the current
  column filters, in the current sort, across all pages — and the status line says so, naming the
  file, the row count and the scope.
- **Clipboard copy beside every download.** The table offers **Copy as Markdown**; each figure card
  offers an icon-only **Copy figure** that writes the composed image — caption, notices and all — to
  the clipboard. Copies are always PNG, because browsers reject `image/webp` in a `ClipboardItem`.
  Every clipboard path feature-detects `navigator.clipboard` and `ClipboardItem`, reports *copied*,
  *unsupported* or *refused* inline, and never throws. There is deliberately no *Copy all figures*:
  an operating-system clipboard holds one image, so a batch would silently keep only the last.
- **The condition badge opens a dialog, not a tooltip.** The info button beside a source's
  *Condition X* badge in step 1 used to show a hint popover holding every differing key's full
  serialised configuration as one unbroken string — about 1,200 characters on run 46.
  `#conditionDetailDialog` now lays the same information out one row per differing key: the key's
  label and machine name, its kind as a text badge (*Fundamental*, *Candidate*, *Instrument*,
  *Speed and cost* — text plus a hue, never hue alone), the one line saying what a difference on it
  costs a comparison, and this source's value beside the reference condition's. A
  `key=value;key=value` configuration is split into its fields with the changed ones marked; a
  digest is abbreviated with the full value on its copy control; anything longer than about 600
  characters collapses behind an *Expand*. A source whose own runs disagree gets the
  self-inconsistent explanation and its keys instead. The whole thing copies as Markdown.
  The button is offered only where the source actually differs from the reference condition.
- **The *Model profiles* figure draws again.** `app.config.ts` registered `ScatterController`,
  `BarController`, `LineElement` and `PointElement` but **not `LineController`**, while
  `buildProfilePlot` builds a `type: 'line'` chart. Chart.js keys controllers by type id, and
  `ScatterController` being a `LineController` subclass does not register `'line'` — so the
  directive threw *"line" is not a registered controller* and the canvas stayed blank, on screen and
  in the offscreen export path alike. The registrable list now lives in one module,
  `model-comparison/chart-registrables.ts`, imported by both `app.config.ts` and the component spec
  so the two cannot drift apart again, and a spec asserts the profile card renders three line
  datasets rather than asserting a configuration object.

`write-excel-file` is the one new client dependency. It is reached through a dynamic import inside
the XLSX encoder alone, so it builds into its own lazy chunk and neither the initial bundle nor the
admin chunk carries it until someone exports a spreadsheet.

### Harness Version 28 Updates

Prompted by benchmark runs 47 and 48 (2026-09-12; GPT-5.6 Luna @ `max` and Gemini 3.7 Flash @
`medium`, a Tier A pair on one roster). `ScoringMethodVersion` stays **10** — nothing here changes
what a quality score is — and `CandidateSystemPromptSha256` does not move. `ToolGuidesSha256` does
not move either, which is precisely why the harness stamp matters this round: the tool-data change
below is carried by a file no fingerprint covers.

- **`AD_SAMU` is described by what the attack steals.** `Overseer/Data/flag_descriptions.json` read
  *"hits, may steal Amulet (Wizard)"*, and every candidate that called `get_monster_stats` for Master
  Kaen framed his theft attack around the Amulet of Yendor. `stealamulet` (`src/steal.c:655-701`)
  takes the hero's **quest artifact** first and reaches the Amulet branch only for a monster carrying
  `M3_WANTSAMUL`; Kaen carries `M3_WANTSARTI`. The entry now reads *"hits, steals the hero's quest
  artifact (monsters that want it); steals the Amulet of Yendor only for a monster that wants the
  Amulet, such as the Wizard"*. The file is loaded at startup by
  `SourceCodeService.LoadFlagDescriptions` from the published `Data/` directory and is **not** part
  of `ToolGuidesSha256`, so `HarnessVersion` 27 → 28 is the only record that the tool's output
  changed. This is a production tool result, so the fix reaches the chat assistant and the benchmark
  candidate identically.
- **The claim verifier checks a magnitude where the property is applied.** Instruction **3b** follows
  3a in `BenchmarkClaimVerificationPrompt`: a claim about the magnitude or tier of a resistance or
  property is checked against the code that applies the property — the enlightenment strings in
  `src/cmd.c` and the resistance rolls in `src/zap.c` — not against the code that grants it, because
  how an intrinsic is acquired says nothing about how much it protects. Run 48's Q13 refutation of a
  50 % fire-resistance claim was wrong in exactly that way (verifier-caution instance 11). The
  verdict was advisory and entered no index; the clause is the response, and deleting it is the
  rollback.
- **The Speed Index is recalibrated: `SpeedTargetMs` 15,000 → 2,000, `SpeedDecayK` 20 → 12**
  (`SpeedDifficultyScaling` stays 1.0). The old pair saturated — run 48 sat at the ceiling on 18 of
  18 answers, and three consecutive rounds read the median model time instead of the index because
  the index had run out of resolution at the top. The floor stays unreachable inside every band's
  timeout, which is invariant 1 of `BenchmarkScoringConstants`:

  | Band | Lowest difficulty | Target(q) | Floor at | Timeout | Margin |
  |---|---|---|---|---|---|
  | Simple | 1 | 2,020 ms | ~615 s | 420 s | ~195 s |
  | Intermediate | 36 | 2,720 ms | ~828 s | 600 s | ~228 s |
  | Advanced | 71 | 3,420 ms | ~1,041 s | 720 s | ~321 s |

  The floor ratio is $2^{99/12} \approx 304.4$. Invariant 2 still binds at the top: a 3.4 s answer at
  difficulty 61 scores 99, not 100. Under the new constants run 48 recomputes to ≈ 86 with none at
  the ceiling, run 47 to ≈ 53, and a 15 s median run to ≈ 70.
- **The speed constants left the quality comparability signature.** The three of them are no longer
  in `BenchmarkScoringProfileService.CanonicalSignature`, which the `ScoringProfile` **Instrument**
  key hashes; they are rendered by a new `SpeedCalibrationSignature` and reach the ladder through a
  new `SpeedCalibration` key of kind **SpeedAndCost**, `DegradesSpeed` true and `DegradesCost` false.
  Two runs on opposite sides of the recalibration are therefore **QualityComparable**, with the speed
  aggregates flagged degraded — rather than NotComparable on every quality dimension for a reason no
  quality score can read. The key is also in `BenchmarkCrossModelComparability.DegradingKeys`, which
  is enumerated rather than derived from the kind: a speed-and-cost key left out of that list is
  treated as must-match and would exclude every point across the boundary from every chart.
  Consequences for existing data: stored **group analyses** hash the old key set and should be
  re-analysed from the Multi-Run tab (the standing caution on the scoring-profile key already says
  so); runs at or below 48 keep their old-scale Speed Index until an administrator uses the per-run
  **Rescore** action, which recomputes quality from stored levels under identical weights and so
  leaves the Intelligence Index untouched by construction; and a run with no profile snapshot reads
  `(none)` on the new key exactly as it does on the profile key.
- **Data-only migration `RecalibrateBenchmarkSpeedIndex`.** The columns already exist, so the C#
  defaults alone would leave every stored profile on the old scale. `Up` moves profiles still holding
  exactly 15,000 / 20.0 to 2,000 / 12.0; `Down` reverses it under the mirrored guard. A profile an
  administrator has tuned is left alone in both directions.
- **The model-comparison speed axis is the candidate's own model time.** `SpeedMeasure` gains
  `meanModelTime` (the default) and `totalModelTime` beside `ttftP50` and `speedIndex`; both speed
  scatters and the profile figure read the selected measure, and the comparison table and every
  export format carry *Model time / question* and *Suite total*. The server side of it is
  `BenchmarkGroupSpeedStatistics.ModelTimeMeanMs`, `TotalModelTimePerRunMeanMs`,
  `PerRunTotalModelTimeMs` and `TotalModelTimeStandardDeviationMs`, computed over the same pooled
  answers and item-set restriction as the existing TTFT figures, and surfaced on
  `BenchmarkModelComparisonSpeedDto` and in the group report. TTFT stays available and stays the
  perceived-latency figure; model time is the turn duration less tool I/O, which is what the two
  runs actually differ on.
- **A run or a series that finishes plays a sound.** The Run Benchmark tab carries a persisted
  *completion sound* setting, on by default, with a *Test sound* button that also satisfies the
  browsers that require one user gesture before programmatic playback. A single run chimes once; a
  series chimes once at the end and never per member; a run opened from history already terminal
  never chimes. When the sound is enabled the run and series polls keep going in a hidden tab at a
  15-second cadence instead of pausing — browsers throttle background timers to roughly once a minute
  after five minutes, so the chime can lag by up to a minute — and the document title carries a
  "✓ " prefix until the tab is revisited. Blocked playback is reported in the dialog rather than
  swallowed. From harness 32 the sound is armed on Start, an optional desktop notification sits beside
  it, and the tab holds a Web Lock while it watches live work; see *Harness Version 32 Updates* below.

### Cross-Model Comparison UI Polish (2026-09-13) — No Version Bump

*Prompted by the wizard's own use on runs 46–48.* Nothing here grades anything:
`BenchmarkAssessmentPrompt.HarnessVersion` stays at **28**, `ScoringMethodVersion` at **10**, and
`CandidateSystemPromptSha256` and `ToolGuidesSha256` do not move. Six presentation changes to the
*Cross-model comparison* wizard and, for the cost rule, to the admin benchmark views around it.

- **The condition-detail dialog is full screen.** `#conditionDetailDialog` carries
  `.gh-dialog-fullscreen` rather than sizing itself, which is what the wizard and the Suite Health
  dialog already use, so the key table's *meaning* column widens from 40ch to 52ch and a source that
  differs on many keys needs less scrolling. Light dismiss goes with it: a full-screen dialog's
  backdrop shows only as an 8px ring, which is too thin to be an honest click target, so `closedby`
  is absent and Escape and the dialog's own controls are the ways out.
- **The dialog saves as well as copies.** *Download as Markdown* sits beside *Copy as Markdown* and
  writes the same text to `comparability_<source>_<yyyyMMdd_HHmmss>.md`, for a reader who wants the
  comparability record in the same folder as the exported table and figures rather than on a
  clipboard that the next copy overwrites.
- **The condition badge and its info button are one flex unit.** `.csp-condition-cell` centres them
  on each other on one line and, where the column is too narrow for both, gives the wrapped button a
  vertical gap instead of the horizontal offset the old `margin-left` left behind.
- **Costs print four decimals everywhere in the admin benchmark area** — run cards, the cost panel,
  multi-run cost statistics, the comparison table, the figures' axis ticks and their end labels. The
  three formatters that split at a dollar (`formatCostAmount`, `formatAmount`, `formatCost`) now pass
  a fixed `'1.4-4'`, and `formatUsd`/`formatUsdText` a fixed `toFixed(4)`. Zero prints `$0.0000`;
  thousands separators stay where a `DecimalPipe` already produced them. A sub-cent candidate and a
  multi-dollar one line up on the decimal point, which is the comparison the whole area exists for.
  In the comparison figures four is now only the default: *Style › Number format* sets the decimals
  of bar value labels, scatter value plates, tooltips and the profile's range labels, and the chart
  module no longer has a `formatUsd` of its own. Run cards, axis ticks and the comparison table and
  its exports keep the formatting described here (see *Comparison Figure Number Format and Axis
  Titles (2026-09-24)*).
- **The cost panel's bars carry value labels.** Only the cost panel: the label is drawn past the SD
  whisker by a scriptable `offset` that measures the whisker in pixels from the value scale, and the
  scale takes `grace: '12%'` so the tallest bar's label is not clipped. `grace` is ignored where a
  scale has an explicit `max`, which the quality and speed panels do have — they keep
  `datalabels.display: false`. `renderPlotOffscreen` forwards the same plugins and shallow-copies the
  options, so an exported figure carries the labels the screen does.
- **Every axis title is two lines**: what the axis measures, then *lower is better*, *higher is
  better*, or — on the profile figure — *up is better on every axis*. Chart.js draws one line per
  element of a `string[]` title, so the direction is no longer an em-dash suffix that a narrow figure
  truncates first. The direction line appears only while the figure's Better badge is hidden, and a
  bar value-axis title can also move its final parenthetical to a line of its own (see *Comparison
  Figure Number Format and Axis Titles (2026-09-24)*), so a title is one to three lines.
- **WebP exports choose a quality.** The format option reads exactly *WebP*, and a quality select
  (75, 80, 85, 90, 95 or 100, default **100**) appears beside it only while WebP is chosen. The
  figures and the table hold the setting separately, as they already hold their formats separately.
  `canvas.toBlob` has no lossless flag; 100 passes `1.0`, which Chromium encodes losslessly and other
  engines encode at their highest lossy quality, so the label claims neither. The repository's
  quality-85 convention governs **shipped image assets**, not an admin's one-off export of a figure
  they are about to paste into a report.
- **Download table asks which columns to write.** A native `<dialog>` lists all 26 columns with every
  column that holds no value on any exported row pre-unticked and marked *empty* — for a fully
  comparable, unscheduled set that is *Scheduled price change*, *Differs on*, *Speed degraded by* and
  *Cost degraded by*. *All columns* and *Only columns with values* set the whole grid; the Download
  button refuses an empty selection; the choice is remembered for the wizard's lifetime. The file
  keeps `COMPARISON_TABLE_COLUMNS` order whatever order the boxes were ticked in, and the status line
  ends `…, N of 26 columns.` The chooser stops `cancel` and `close` from propagating, or the wizard
  dialog containing it would close too.
- **The comparison table stops wrapping mid-value.** Eight columns rather than ten: *Model · R ·
  State · Intelligence Index · Speed Index · Timings · Candidate $ / question · Notes*, where
  *Timings* is one `<dl>` stacking model time per question, the suite total and TTFT P50/P90. Every
  multi-part cell is a flex column, so a badge that wraps below its value gets a 4px gap and no left
  offset; numbers carry `nowrap`; the interval basis is clipped to one line with its full text in the
  tooltip and, unchanged, in the export. Sorting on screen by suite total and by TTFT P50 is gone
  with their columns — sorting by *Timings* sorts on model time per question. **The exported table
  still carries all 26 columns**, so nothing is lost from the artefact, only from the screen.

### Model Comparison Question Count (2026-09-23) — No Version Bump

*Prompted by a figure preview whose badge read "16 questions" for an 18-question suite.* Nothing
here grades anything: `HarnessVersion`, `ScoringMethodVersion`, `CandidateSystemPromptSha256` and
`ToolGuidesSha256` do not move. The comparison is computed on request from stored data.

- **The questions badge counts scored questions against the suite.** It reads `18 questions` when
  every charted entry scored every suite question, `16 of 18 questions` when fewer are scored, and
  `15–16 of 18 questions` when the entries differ; the accessible label spells it out as *16 of the
  suite's 18 questions scored*. The scored count is the Intelligence Index's real denominator: an
  answer counts only when it is `Ok` (or a model-produced empty answer), graded, and graded against
  the question's **current** rubric revision. Before, the badge showed the first charted entry's
  scored count as though it were the suite size. The *Number of questions* style toggle still hides
  it.
- **Three new fields on `BenchmarkModelComparisonQualityDto`.** `SuiteItemCount` is the suite's
  question count now; `RevisedItemCount` counts questions whose every otherwise-scored answer was
  graded under an older rubric revision (a rubric edit or rubric-repair import after the run);
  `UnscoredItemCount` counts questions with no scored answer at all — failed, skipped, canceled,
  ungraded or never asked. `ItemCount + RevisedItemCount + UnscoredItemCount == SuiteItemCount`
  always, because both new counts are taken over questions missing from the statistics.
  `BenchmarkGroupStatistics` and Multi-Run Analysis are unchanged.
- **Coverage notes on every figure.** When questions are left out, the set-level notes — shown above
  the figures, in the preview dialog and in every export — say why: one *info* note for revised
  rubrics (*2 of the suite's 18 questions are left out: their rubrics were revised after these
  runs…*), and one *warning* per plotted entry with unscored questions, naming it, since two indices
  over different item sets are not strictly the same exam.
- **The whole-suite cost is each entry's own mean run cost** (`cost.candidateCostPerRunUsd`), not
  `$ / question` multiplied by the first entry's count. It no longer depends on any item count, so
  entries with different scored counts are no longer rescaled by the wrong one. Where the counts
  agree the values are unchanged.
- **The cost axis title** reads *Candidate cost of one suite run (USD)*, without a question count:
  the run paid for every question it asked, not only for the scored ones.

### Cost per Question Denominator (2026-09-23) — No Version Bump

*Prompted by the Model Comparison question-count fix, which found that `$ / question` divided by
scored items.* Nothing here grades anything: `HarnessVersion`, `ScoringMethodVersion`,
`CandidateSystemPromptSha256` and `ToolGuidesSha256` do not move. Both views compute on request
from stored data.

- **The defect.** `BenchmarkGroupStatistics.ComputeCost` divided the mean cost per run by the number
  of items with at least one scored answer. The numerator is a different population: run token
  totals are summed over **every** answer row, whatever its status, so the spend on a
  `ProviderError`, a `Canceled` or an ungraded answer, and on an answer graded under an older rubric
  revision, was in the numerator while its question was out of the denominator. 18 asked and 16
  scored overstated `$ / question` by 18/16 = 12.5 %. It reached the Model Comparison table, detail
  row, scatters and table exports, the Multi-Run *Per question* stat, and the Multi-Run report's
  *Cost per question* line.
- **The denominator is questions asked.** `CostPerQuestion = MeanCostPerRun / QuestionsAskedPerRun`,
  where `QuestionsAskedPerRun` is the answer rows of the costed runs (the runs with a
  `BenchmarkGroupRunCost` row, whose spend is the numerator) divided by their number. A run has one
  row per question it asked: a failed-question re-run overwrites its row in place, `Skipped` is
  never assigned, a deleted question's rows keep their tokens with a null question id, and a run
  cancelled part-way has rows only for the questions it reached. The ratio of totals, rather than a
  mean of per-run ratios, keeps `CostPerQuestion × QuestionsAskedPerRun == MeanCostPerRun` exact.
  The value is null when a cost row names no member run, or when the costed runs have no rows.
  Rejected: the suite's question count (wrong for a cancelled run and for a suite that changed after
  its runs), and answers that count toward the index (today's defect over answers instead of items).
  A separate *cost per usable answer* would be a new metric, and is out of scope.
- **New fields.** `BenchmarkGroupCostStatistics.QuestionsAskedPerRun` and
  `BenchmarkModelComparisonCostDto.QuestionsAskedPerRun`; the latter is null whenever pricing did not
  resolve, as the other cost fields are.
- **Labels.** The report line reads *Cost per question: $0.0183 — $0.3300 mean per run over 18
  questions asked per run.* The Multi-Run *Per question* stat adds *over 18 questions asked per
  run*; the Model Comparison single-entry cost tile adds *— over 18 questions asked per run*. P1's
  cost axis reads *Candidate cost of one suite run (USD, 18 questions asked)* when every charted
  entry asked the same number of questions, and *(USD)* otherwise.
- **Stored analyses keep their old figure and say so.** A Multi-Run analysis stored before this
  change has no `QuestionsAskedPerRun`, so its `CostPerQuestion` is still over scored items. The
  report says *divided by items with a scored answer, not by questions asked. Recompute the analysis
  for the per-question-asked figure.* and the view says *over scored items; recompute for per
  question asked*. Stored blobs are not rewritten: they are a record of what was computed, and
  recomputing is one click.
- **What moves.** `$ / question` drops wherever answers were lost or rubrics revised, by the ratio of
  scored to asked. Whole-suite cost does not change: it is each entry's own mean run cost.

### Runs Are Independent of Later Suite Changes (2026-09-24) — No Version Bump

*Prompted by a Model Comparison of two old runs whose step 4 left questions out because their
rubrics had been edited since.* A finished run is a closed record: editing, adding, deleting or
reordering questions or rubrics, replacing or editing the board, and renaming or deleting the suite
change nothing an old run shows, how it is re-graded, or how it compares. No suite operation is
refused because old runs exist. `HarnessVersion` and `ScoringMethodVersion` do not move: a new run's
grading input is byte-identical — the same rubric and board, now read from the run's own copy.

- **The exam record** (migration `AddBenchmarkRunExamRecord`). Each answer stores the rubric it is
  graded against (`ExpectedPointsUsed`, with `ExpectedPointsRecorded` separating "no rubric" from
  "not known"), written when the answer row is created. Each run points at the board it was asked
  and graded with (`BoardSnapshotId`) in the new append-only, content-addressed
  `BenchmarkRunBoardSnapshots` table, written at launch beside the `GameSnapshot*Used` fields, and
  records `DefaultSuiteVersionUsed`. See § 5 for the fields.
- **The backfill, and its limits.** The migration records an answer's rubric only where its question
  still exists at the revision the answer was made against — every text, band or rubric write bumps
  `ItemRevision`, so an equal revision means an equal rubric — and links every run to a board record
  where a live board still has the hash the run recorded; a `THROW` self-check fails the migration
  if a run whose board still exists is left unlinked. What it cannot recover:
  - an answer whose question was edited or deleted before this change has no recoverable rubric;
  - an answer from before `AddBenchmarkQuestionIdentity` (2026-09-03) has no `ItemRevisionUsed`, so
    its rubric is not provable and stays unrecorded even when the question is unchanged;
  - a run whose board was replaced or edited before this change has no recoverable board;
  - a backfilled board carries today's digest, which matters only to final synthesis;
  - `DefaultSuiteVersionUsed` stays null for old runs, and the report prints the key alone, as it
    effectively did before.
- **Statistics read the runs, not the suite.** `BenchmarkRunExam.Build(runs)` builds detached
  questions from the answer rows (§ 4, *Stable item identity*). `ItemRevision` is the highest graded
  revision among a question's counted answers, `AssessedDifficulty` the rounded mean of the answers'
  weights, and text, order and rubric come from the newest run. Group statistics, Multi-Run analysis
  and Model Comparison compute over it; a group whose suite was deleted now analyses rather than
  refusing, and a comparison entry of a deleted suite is measured rather than excluded. Mixed
  revisions across runs already differ on the Fundamental key `SuiteItemRevisions`, so such a set is
  *not comparable* and never pooled.
- **Re-grading an old run reads its record.** `BenchmarkRunExamRecord.Rubric(answer)` and
  `.Board(run)` replace every live-suite lookup on the grading paths — re-run failed questions,
  re-run one answer, re-assess (applied and trial), retry failed assessments, retry claim
  verification, rerun synthesis, the outlier sweep, the sample top-up and assessor calibration — and
  `BenchmarkBoardGuard` checks the run's board record. Where the record is missing, the operation
  refuses the whole run before anything changes: the controller returns `400` with a message starting
  *Refused:* that names the questions by stored order (the *Action Error* box shows it), and the service
  guards again, restoring the run's terminal status with the message in `ErrorMessage`. Assessor
  calibration instead skips answers without a recorded rubric and logs the count. Today these
  re-grades would have used the wrong rubric or none, silently.
- **DTO changes.** `BenchmarkModelComparisonQualityDto.SuiteItemCount` is renamed `ExamItemCount` (the
  questions these runs were asked) and `RevisedItemCount` is removed: nothing is left out for a later
  rubric edit any more, so the revised-rubric coverage note is gone and the questions badge reads
  *18 questions* for a fully scored 18-question exam, whatever the suite holds now.
  `ItemCount + UnscoredItemCount == ExamItemCount`. The run detail DTO gains `GameSnapshotSha256Used`
  and `HasBoardRecord`, and `GET api/admin/benchmark/runs/{id}/board` returns the run's board record
  (`404` without a board, `409` when it is unknown). *View Game Snapshot* on the run detail opens that
  board read-only.
- **The `Compare` guard.** `BenchmarkGroupStatistics.Compare` pairs an item only when both sides'
  `ItemRevision` agree; the rest go to the new `BenchmarkGroupComparison.RevisionMismatchedItemCount`,
  and the report and Multi-Run note say so. `UnpairedItemCount` keeps meaning "present on one side
  only".
- **Names.** Group DTOs, group report file names and the comparability index show the name the runs
  recorded (`SuiteName`), not the live suite's.
- **Suite-operation hygiene.** `ReorderQuestions` returns `400` unless its list is a permutation of
  the suite's question ids, and a refused *Delete Suite* (a run of it in progress) now shows its
  message instead of failing silently.
- **Stored Multi-Run analyses keep their figures until recomputed.** Sets graded under since-edited
  rubrics move when recomputed: their questions are no longer left out.
- **Model Comparison, step 3.** *Download table* and *Copy as Markdown* are icon-only `.action-btn`s
  (file-with-arrow and copy glyphs) with `interestfor` tooltips and `aria-disabled` when there is
  nothing to export, vertically centred beside the format select with a 12 px gap. The download
  button's name follows the selected format (*Download the table as Excel (.xlsx)…*).

#### The single-answer re-run keeps the run's provenance

**The defect.** Re-running one answer called `PopulateInstrumentFingerprint`, overwriting the run's
`CandidateSystemPromptSha256`, `CandidateSystemPromptText`, `ToolGuidesSha256` and the three corpus
heads with the values current at re-run time, and cleared `CompletedAtUtc`, so the run's end moved to
the re-run's end and its elapsed wall time absorbed the whole gap (a re-run that threw left no end
time at all). Those fingerprints are what the comparability keys and the report's reproduction line
read, so one re-run under a changed prompt relabelled every untouched answer and could move the run
into or out of a comparison group; and the report's *Re-run under a different instrument* warning could
never fire for it.

**The fix.** Both re-run paths start through `BenchmarkService.BeginRerun`, which records the
re-run's own prompt hash, ToolGuides hash and harness version in the `Rerun*` columns and opens its
span; the single-answer re-run keeps `CompletedAtUtc`, closes `RerunCompletedAtUtc` on success,
error and cancellation, and finalizes with `preserveCompletedAt: true`. Its endpoint sets
`RerunStartedAtUtc` and clears `ErrorMessage` when it flips the run to Running, as the failed-question
endpoint does. The `Rerun*` columns now describe the most recent re-run of either kind; the report's
block reads *Repaired by a re-run*, or *Re-run under a different instrument* when the prompt or
guides moved.

**Known limit.** A run already re-run this way keeps the overwritten values: the originals were not
kept anywhere.

### Comparison Figure Number Format and Axis Titles (2026-09-24) — No Version Bump

*Prompted by a cost panel whose value-axis title, `Candidate cost of one suite run (USD, 18 questions
asked)`, was longer than its axis.* Display only: nothing here grades anything, and
`HarnessVersion`, `ScoringMethodVersion`, `CandidateSystemPromptSha256` and `ToolGuidesSha256` do
not move. Both settings live in step 4's sidebar, **Style** tab, and are stored with the rest of the
figure style in the browser (`overseer.modelComparison.figureStyle`, still `version: 1`); a style
stored before them reads both at their defaults.

- **Axis title line break** (Bar panels › *Values and axes*): *Automatic* (default), *Always* or
  *Never*. A title that ends in a parenthetical after a nonempty head can move that parenthetical to
  a second line, `Candidate cost of one suite run` over `(USD, 18 questions asked)`; nothing else is
  split, and `Model` never is. The *lower/higher is better* line, shown only while the Better badge
  is hidden, stays last. The tooltip keeps the one-line title. *Automatic* decides at every layout,
  in the value scale's `afterFit`: it measures the unbroken lines in the title's resolved font against
  the fitted axis length less 8 px, and where they do not fit it breaks the title and refits the
  scale once. The page, the preview and every export therefore decide independently, by their own
  axis length; pixel density alone does not change the decision, and a longer axis restores the
  unbroken title. The decision is written into each chart's own merged options, never into the
  figure spec, so two charts built from one spec can decide differently. Only bar value axes break;
  scatter titles are unchanged.
- **The limit.** One break is all there is. Where the head or the parenthetical alone is longer than
  the axis, or the title font is very large, the title still does not fit; it is drawn whole, with no
  ellipsis, no smaller font and no further wrapping. A longer axis (a taller preview, a wider
  container) or a smaller *Axis titles* size is the remedy.
- **Two Chart.js layout facts the break works around or lives with.** A second `fit()` recomputes
  the scale's tick-label padding from pixel state the first fit has already moved, and the layout
  sizes the axis from that padding, so the refit keeps the first fit's `paddingLeft/Right/Top/
  Bottom`. And in horizontal bars Chart.js fits the category axis at the full plot height, fits the
  value axis beside it, then refits only the category axis at the shorter final height: where that
  refit autoskips ticks the category axis narrows and the value axis ends up wider than the width
  its title was decided on. That can leave a break the final width did not need (seen with eight
  models and large axis text on a short plot); it never leaves a one-line title too long for its
  axis. Correcting it would take a second layout pass, which the break deliberately never starts.
- **Number format** — a section in every family (after bar *Values and axes*, trade-off *Labels and
  legend* and profile *Heading and badges*) with one select per measure the family shows: Intelligence
  Index, the selected speed measure, and the selected cost (the trade-offs always show *Cost per
  question*). Each option reads its decimal count and the family's first plotted value written with
  it, `1 (22.5 s)`, or a fixed example where nothing is plotted. Defaults: Intelligence Index **0**,
  Speed Index **0**, mean time per question **1**, total time for the suite **1**, TTFT P50 **2**, and
  **4** for every cost. The setting is per measure and shared by every family that shows it; switching
  the speed or cost measure shows that measure's own setting.
- **What it formats.** Bar value labels and bar tooltips, scatter value plates and scatter tooltips,
  the profile's tooltip and its real-value range labels. Values are written fixed-point with their
  trailing zeros; a value that rounds to zero never keeps a minus sign. **Not** axis ticks, which
  follow their own step; not run cards; not the comparison table or its CSV/XLSX exports. No value,
  domain, sort order, frontier or normalized coordinate is rounded.
- **Units.** Time decimals count in seconds. A millisecond display shows `decimals − 3` of them and
  never fewer than whole milliseconds, so settings 0–3 read alike there and can keep more precision
  than the seconds display would. Each family keeps its own unit rule — bars the largest plotted
  speed, scatters the resolved axis domain, the profile the largest plotted time — so the value text
  always agrees with its axis title, and a sub-second model on a seconds axis reads `0.9 s` rather
  than the `870 ms` it used to.
- **Resets.** The section's reset (*Reset visible number formats to defaults*) resets only the three
  measures on show; a hidden measure keeps its setting. The family resets (*Reset bar style* and the
  others) leave every number format alone.
- **Visible defaults changed.** The scatter and profile indices lose the fractional digit they used to
  print, mean and total times show one decimal instead of two, and every time value follows its
  figure's unit.

### Model Comparison: Three Steps, Theme and Fonts, View-Aware Sidebar (2026-09-25) — No Version Bump

*Prompted by exporting the comparison into slide decks and web pages, where a dark-only figure and a
content-sized table image rarely fit.* Display only: nothing here grades anything, and
`HarnessVersion`, `ScoringMethodVersion`, `CandidateSystemPromptSha256` and `ToolGuidesSha256` do
not move. With no stored settings the charts and the table image look exactly as before.

**Steps.** The wizard has three steps instead of four; the separate *Table* step is gone and its
table is a view of the last step.

| Step | Title | Holds |
|------|-------|-------|
| 1 | Sources | The runs and analysis groups to compare |
| 2 | Comparability & filters | *Scope* (pricing basis, comparability) and *Entries in the charts* |
| 3 | Charts & table | Every chart and the comparison table, with their settings and exports |

Step 3 is reachable as soon as a comparison exists. When nothing in the set can be charted, the two
chart views are `aria-disabled` with the reason shown, and the step opens on the table.

**Views.** Step 3 has four view tabs: **All charts** (grid), **Single chart** (eye), **Interactive
table** (table) and **Table preview** (image). The Interactive table is today's sortable, filterable
table in the application's own dark UI; the Table preview shows the table image on the Single chart
stage — same zoom, fit, pan and keys, with its own view state opening at Fit to screen — and always
previews the image export, with a note when another Table format is chosen. Leaving a chart view
releases the chart bitmaps; leaving the Table preview releases its bitmap.

**The sidebar follows the view.** Chart views show **Data · Theme · Charts · Download**; both table
views show **Data · Theme · Table · Download**. A tab shared by both sets stays selected across a view
change; only *Charts* and *Table* swap. A stored tab from before is migrated: `emphasis` → `data`,
`style` → `charts`, `export` → `download`.

| Setting | Charts | Table image | Tab |
|---------|:------:|:-----------:|-----|
| Speed measure, cost measure | ✓ | — | Data → Measures |
| Model order (incl. *Custom*), direction | ✓ | ✓ (rows) | Data → Model order |
| Emphasised models | ✓ | — | Data → Emphasis |
| Pricing basis, comparability, entries in the charts | ✓ | pricing basis only | step 2 |
| Theme, background, preview backdrop, font, weights, text colours, border | ✓ | ✓ | Theme |
| Per-family chart settings, axis title weight, plot frame | ✓ | — | Charts |
| Columns | — | ✓ (every output) | Table → Columns |
| Alternate-row shading, lines between rows | — | ✓ | Table → Row style |
| Image format and WebP quality | ✓ | ✓ | Download (shared) |
| Chart size | ✓ | — | Download → Chart size |
| Table format, table image size | — | ✓ | Download (table views) |

*Speed measure*, *Cost measure*, *Model order* and *Order direction* moved from step 2 to the Data
tab as radio groups, beside the charts they change; step 2 keeps what decides *which* entries can be
compared, and its note says where the others went. In the table views the Data tab shows only
*Model order*.

**Model order.** *Order by* offers Intelligence Index, Speed, Cost, Name and **Custom**. Custom shows
every entry in a list that is rearranged by dragging its handle, or with the handle's Move menu
(*Move to top*, *Move up*, *Move down*, *Move to bottom*), which is the keyboard and single-pointer
path, announcing each move. The list is seeded from the
order in effect, marks where the charts stop plotting (an entry dragged above the line enters the
charts), tags entries that are never charted *table only*, and *Reset custom order* returns it to
Intelligence Index, descending. *Direction* is disabled while Custom is chosen. The custom order lives
for the wizard session and is not stored. **One model order drives the charts and the table**: the
Interactive table, the Table preview and every table export follow it — so the table's default row
order is now the model order, and *State* is one header click away — until a column header is
clicked; *Use model order* (above the table and in the Data tab), and any change to the model order,
return to it. The charts and the table share one comparator, so they cannot disagree.

**Columns.** The Table tab's *Columns* section lists all 28 display columns — the eight on-screen
columns, some of which combine several values (*Model*, *R*, *State*, *Intelligence Index*, *Speed
Index*, *Timings*, *Candidate $ / question*, *Notes*), and the other twenty single values as columns
of their own — to show or hide with a checkbox and to reorder by drag and drop or with a row handle's
Move menu.
*Model* is always shown but can move. Quick actions: *Default columns* (today's eight, in today's
order), *Only columns with values*, *All columns*. One configuration drives the Interactive table,
the Table preview and every download and copy, and is stored per browser
(`overseer.modelComparison.tableColumns`). Reading formats (the on-screen table, the image, Markdown,
HTML) keep a combined column combined, worded as on screen; data formats (Excel, CSV, TSV, JSON)
write its parts as typed columns in its place. **No value is written twice**: a part shown as its own
column is left out of its combined cell, and in a data format a part already written is skipped.
Hiding a column that has an active filter clears that filter, with a status line. **The default
download columns changed**: a download used to write every populated column of the 26; it now writes
the eight on-screen columns (split into their parts in data formats). *All columns* restores the full
set in one click.

**Row style.** The Table tab's *Row style* section sets the table image's alternate-row shading
(*None*, *Light*, *Medium*, *Strong*, made from the text color at 5, 10 and 16 % opacity; *Medium* by
default, and a stored *Row bands* setting becomes *Medium* when it was on and *None* when it was off)
and its *Lines between rows*. It applies to the Table preview, the PNG and WebP downloads and Copy as
image; the Interactive table is not affected.

**Table export.** The step's separate table export controls and the download-time column dialog are
gone. The Download tab in the table views holds the **Table format** (Excel, CSV, TSV, Markdown,
JSON, HTML, or *Image (PNG or WebP)*), the **Table image size** and the shared **Image format**, a
scope line, and *Download table* / *Copy table*, which the view bar repeats. *Copy table* follows the
format: Image copies a PNG (also when WebP is chosen), Excel copies cells with an HTML fallback, HTML
copies a formatted table with a plain-text fallback, and Markdown, CSV, TSV and JSON copy their text
without the byte-order mark the files carry. Format, image format and WebP quality are stored per
browser (`overseer.modelComparison.download`); the charts' and the table's WebP quality are one
setting.

**Table image size.** Separate from the chart size, because a table and a chart are very different
shapes, and stored on its own (`overseer.modelComparison.tableImageSize`). Two modes:

- **Fit the table** (default): the image is as large as the table, at the chosen density — at the
  default 200 % exactly the content-sized 2× image the table was always written as.
- **A preset or a custom size**, in **plain pixels**: the table is laid out in `W / T × H / T`
  layout px for text size `T` and written at exactly `W·D × H·D` for density `D`, spanning the
  width. A size too narrow or too short is refused with the minimum named, and download and copy are
  disabled with that reason. In custom mode the section shows the *Fit the table* size at the current
  text size — `W × H px` for the shown columns and the rows passing the filters — as information,
  not a default; typed in, it fits the table with no spare space.

This deliberately differs from the charts' rule, which composes in a box of at least 960 × 540
layout px that density maps onto the bitmap, so a chart's content never changes with the resolution.
Under that rule whether a table fits would depend only on the aspect ratio and the text size, and no
pixel size could be named as "the size the table fits in". A table's content is fixed by its rows and
columns, so the useful question is how many pixels it needs, and plain pixels answer it.

**Theme and fonts.** The Theme tab reaches every chart and the table image, on the page, in the
previews and in every export, through one resolved theme:

- **Theme**: *Dark* (today's palette) or *Light* (measured on white, PowerPoint's default slide,
  with its own validated series hues `#2a78d6`, `#eb6834`, `#18a070`).
- **Background**: the theme's, **transparent**, or a custom colour. A transparent export carries
  alpha in PNG, WebP and the clipboard PNG. On the page a transparent figure is shown over a
  checkerboard or a chosen **preview backdrop** colour, which is never exported.
- **Font**: *Overseer default* or one of six self-hosted families (Inter, Roboto, Geist, IBM Plex
  Sans, Source Sans 3, Open Sans; see the data privacy framework § 3.2), loaded before anything is
  measured, with a status line when loading fails. Heading and label weights, 400–700.
- **Text colour**: heading and text colours, each following the theme unless set; warnings name
  headings below 3:1, text below 4.5:1 and series below 3:1 against the background, the backdrop
  colour, or the theme's own ground under the checkerboard. Warning notes, pricing badges, the accent
  and the series hues stay theme-owned.
- **Borders**: an optional figure border with width, corner radius (which also rounds the
  background, leaving transparent corners) and colour.

The Charts tab adds *Axis title weight* and *Frame the plot area* to the bar and trade-off panels.
All of it is stored with the figure style (`overseer.modelComparison.figureStyle`, still
`version: 1`, with new `appearance` and `table` records); a style stored before reads them at their
defaults.

### Model Comparison: Total Run Cost Measure and Column (2026-09-25) — No Version Bump

*Prompted by run cards and cost estimates, which need what one benchmark run of a model costs.*
Display only: nothing here grades anything, and `HarnessVersion`, `ScoringMethodVersion`,
`CandidateSystemPromptSha256` and `ToolGuidesSha256` do not move. No database change.

**What the total is.** The mean cost of one run behind an entry with every role included —
candidate, assessor, second opinion, claim verifier and final synthesis — and its sample SD across
the entry's runs (null at R = 1). Each run is costed by `ModelPricingService.ComputeRunRoleCosts`,
the function the run report's `### Harness Cost` prints, so on the *As-run* basis an R = 1 entry's
total equals that report's total. The endpoint carries it on the cost object as
`totalRunCostPerRunUsd`, `totalRunCostSdUsd` and `totalRunCostUnavailableReason`. It is computed on
its own path: grading roles never enter the per-run costs handed to `BenchmarkGroupStatistics`,
which sums every role it is given and would otherwise turn every candidate figure into a total.

**When there is none.** The total is null, with a one-sentence reason, when any run behind the
entry:

- **predates harness 15** (`BenchmarkModelComparison.PerRoleCostTrackingHarnessVersion`), which
  first recorded second opinion and final synthesis tokens as roles of their own. An older run's
  total would be understated rather than unknown, so it is refused;
- has no resolvable candidate price card; or
- has a grading role that spent tokens and has no price card (`BenchmarkRoleCosts.Incomplete`).

An excluded entry carries no cost object at all, so no total either.

**Pricing bases.** *As-run* reads each run's stored `PricingSnapshotJson` for every role, as the run
report does. *Current* prices every role from today's catalog for that role's stored provider and
model id, the same rule it already applies to the candidate; custom per-configuration overrides are
not consulted for any role on this basis, which keeps the grader and candidate figures consistent with each
other.

**The cost measure.** Step 3 → Data → Measures → *Cost measure* offers *Total run cost including
grading roles* beside the default *Candidate cost for the whole suite*. Candidate cost stays the
default, because it is what a model would cost as the chat assistant; grading spend is most of a
run and none of it transfers. The option is enabled only when at least two entries can be charted
and **every** one of them carries a total — an axis missing some bars would rank models on a figure
that is absent for some of them. Otherwise it is shown disabled, labelled *— not available*, and
the hint names the first entry without a total and why. A comparison that arrives without totals
while the total is selected falls back to candidate cost.

**The table column.** The step 3 table has a new default column, **Total $ / run, with grading**,
directly after *Candidate $ / question*. It prints the total, with `± SD` at R ≥ 2, and an
*Unavailable* badge whose tooltip carries the reason when the total is null. Like the other
combined columns it has three parts, each also offered as a column of its own in the Columns picker
(hidden by default): *Total $/run incl. grading* and *Total $/run SD* (money) and *Total $/run
unavailable because* (text). The data formats (XLSX, CSV, TSV, JSON) write the three parts as typed
columns; the reading formats (Markdown, HTML, the image) write the combined text, such as
`$1.2345 ± $0.0567`, or `— · <reason>`. The table now has 29 parts and 31 display columns, of which
9 are shown by default.

**Saved layouts.** The column layout in `overseer.modelComparison.tableColumns` is now written as
`version: 2`. A version-1 layout (or one with no version) is migrated once on load: the new column
is inserted right after *Candidate $ / question*, and shown when that column is shown. A version-2
layout is never migrated again, so an admin who hides the column keeps it hidden. Without the
migration the general repair rule would have appended the new column hidden, and it would have
looked missing to exactly the admins using the table.

### Model Comparison: Two Steps, Models and Prices in the Data Tab, About Dialog (2026-09-26)

*The separate Comparability & filters step was mostly a stopover: its explanation repeated the
wizard header, and its controls belonged beside the charts they change.* Display only: nothing here grades anything, and
`HarnessVersion`, `ScoringMethodVersion`, `CandidateSystemPromptSha256` and `ToolGuidesSha256` do
not move.

**Steps.** The wizard has two steps instead of three.

| Step | Title | Holds |
|------|-------|-------|
| 1 | Sources | The runs and analysis groups to compare |
| 2 | Charts & table | Every chart and the comparison table, with their settings and exports |

Step 2 is reachable as soon as a comparison exists, even one no chart can draw — it opens on the
table views in that case, as before. The footer button on step 2 is *Close*.

**Data tab groups.** The separate Comparability & filters step is gone; what it held now lives in
the Data tab, in this order:

| Group | Chart views | Table views | What it holds |
|-------|:------:|:-----------:|----------------|
| Models | ✓ | ✓ | One row per model with a **Show** checkbox (plot this model, up to the chart cap) and a **Highlight** checkbox (chart views only: draw it in gold and grey the rest) |
| Measures | ✓ | — | Speed measure, cost measure |
| Prices | ✓ | ✓ | Pricing basis |
| Model order | ✓ | ✓ | Order by, direction, custom order |

**Models.** The old step-2 *Entries in the charts* checklist and the Data tab's old *Emphasis* list
are one table now. Every model in the comparison gets one row with Show and Highlight checkboxes;
the Highlight column is shown only in the chart views, since a table view has no gold series to
draw. "Emphasise" is renamed *Highlight* in the UI only — the internal names (`emphasisKeys`,
`toggleEmphasis`, `isEmphasised`, `clearEmphasis`) are unchanged. Unticking Show takes a model out of
the charts only; it stays in the table. A model the server excluded has its Show box disabled and
an information tip with the reason.

**Strictness is gone.** The Comparability strictness filter (*Comparable and degraded* /
*Comparable only*) is removed; it duplicated Show, which already excludes a model from every
figure with the same effect, one row at a time and visibly. The *Strict comparability is on*
figure note that used to appear in exports can no longer appear, since there is no longer a
setting for it to report.

**Prices.** The pricing basis select — *Today's prices — fair across dates* (Current) and *Prices
at run time — what was spent* (AsRun) — moved from the removed step to its own Data tab group,
shown in both view groups since the table reads it too. Changing it recomputes the comparison
server-side, the same round trip *Recompute* makes.

**Recompute.** The view bar's *Recompute* control is now icon-only: a `.action-btn` with the
rotate glyph, `aria-label="Recompute the comparison"` and the tooltip *Recompute this comparison*.

**About.** The view bar also gains an *About* button (`#mc-about-trigger`, a `.btn-ghost` with the
help-circle glyph the source picker's *About conditions* button already uses, with visible text
*About*), carrying a count badge equal to the number of caveat notes while any exist. It opens an
**About this comparison** dialog (`#aboutDialog`) holding: a one-line summary; *Read these first*
(thinking levels differ across candidates; every model here has only one run); *Not in the
charts* (models measured differently, the keys they differ on, behind a Details disclosure) —
this is where the removed *Entries excluded from every figure* section's content now lives; *How
to read the charts*; and *Not shown as charts* (the server's refused measures, each with its
summary, an *Instead: …* line and a *Why* disclosure). The dialog's body renders only while it is
open, and it stops propagation of its own close and cancel events so they never reach, and close,
the wizard's own dialog.

**Refetch and selection.** A refetch that returns the same set of entry keys — from changing
Prices or from Recompute — keeps the admin's Show and Highlight choices exactly as set. A refetch
whose entries differ reseeds both from scratch.

**Unavailable charts, by reason.** The chart views' unavailable line now names why, per shape:

- No models in the comparison: *"There are no models in this comparison."*
- Fewer than two models were measured the same way (nothing chartable, but the table still has
  everything): *"Fewer than two models were measured the same way, so there is nothing to chart.
  The table lists every model and why."*
- The admin unticked models under Show, down to fewer than two: *"Charts need at least two models.
  Tick more under Data → Models."*

### Model Comparison: Resizable Sidebar and Compact Toolbars (2026-09-26)

*Every view's actions now sit on that view's own toolbar row, and the view bar is reduced to the
sidebar toggle and the view tabs.* Display only: nothing here grades anything, and
`HarnessVersion`, `ScoringMethodVersion`, `CandidateSystemPromptSha256` and `ToolGuidesSha256` do
not move.

**About and Recompute** moved from the view bar to the right end of the step tab row
(*1. Sources · 2. Charts & table*), and show on step 2 only. Both are otherwise unchanged.

**Resizable sidebar.** The settings sidebar opens 26 rem wide, enough for its four tabs, and a
handle between the sidebar and the views resizes it between 18 rem and 40 rem (never more than half the
workspace). The handle is a WAI-ARIA window splitter (`app-pane-resizer`): drag it; or focus it
and use ArrowLeft / ArrowRight (16 px), Shift with them (64 px), Home and End; double-click resets
it. The width is remembered per browser as `sidebarWidth` in
`overseer.modelComparison.figureSidebar`. The handle disappears while the sidebar is collapsed, and
where the sidebar stacks above the views on a narrow window.

**One toolbar row per view**, each ending in a right-aligned group of icon-only buttons with
tooltips:

| View | Row |
|------|-----|
| All charts | Zoom · **Download all charts** |
| Single chart | Figure picker · zoom · **Copy** · **Download** |
| Table preview | *Comparison table* · zoom · **Copy table** · **Download table** |
| Interactive table | *Comparison table* (i) · **Copy table** · **Download table** |

- **Download all charts** is only on the All charts row. Its glyph is two download arrows into one
  tray — the one-chart Download glyph doubled — and its tooltip names the archive's size, density
  and format.
- **Single chart's Download** is icon-only; its tooltip reads *Download this chart — <size ·
  density · format>*, or why it will not.
- **Download table** is icon-only, named and tooltipped after the chosen format (*Download the
  table as Excel (.xlsx)*), beside Copy table.
- The rows stay on one line until the panel is under 44 rem wide; the zoom slider shrinks first.

**All-charts tiles** each carry Copy, Download and Open in Single view in their top-right corner,
shown while the tile is hovered or holds keyboard focus, and always on touch screens. Copy and
Download are tab stops and do not open the chart in Single; Open stays out of the tab order,
because Enter on the tile already opens it.

**The sidebar's Download tab holds settings only.** Its *Download all charts*, *Download table*
and *Copy table* buttons are gone; the chart views' tab says where the chart downloads are, and the
table views' tab keeps its scope line.

**A compact Interactive table header.** The heading, two paragraphs, the *Computed* line and the
order line became one row and one line: *Comparison table* with an information tip (*Every entry
is listed here, charted or not. Point at a State badge to see why.*, also the table's
description) and the table's Copy and Download at the row's end, sticky while the table scrolls;
then *Computed … · Rows follow the model order: …*, with a link-style *Use model order* once a
header sorts the table. The paragraph on what a download holds was removed: the Download tab's
scope line says it, with the actual counts.

**Number format and weights.** In the Charts tab, each decimal-places choice is a compact select
of the digits 0–6 with the example value shown beside it rather than inside every option; in
Chromium browsers the open list is styled in the dark theme. Font weight choices read
*Regular (400)*, *Medium (500)*, *Semibold (600)* and *Bold (700)*.

### Harness Version 29 Updates

Prompted by the analysis of runs 50 and 51, the first two runs of a game-snapshot suite, which showed that
`BenchmarkCandidatePromptOptions` had been recording the prompt the harness *built*, never the request
body a provider actually *sent* — so the defect below could hide behind a `HasGameSnapshot: true` line in
the report. `ScoringMethodVersion` stays **10**, `CandidateSystemPromptSha256` does not move, and
`ToolGuidesSha256` does not move either — no guide file and no prompt text changed; only what reached the
wire.

- **The board silently dropped out for two of three providers, and the system prompt for the third.**
  `BenchmarkService.BuildCandidateSeedHistory(run, systemPrompt, questionText)` now emits, in order, a
  `{role="system", content=systemPrompt}` message, then the board message
  (`ChatService.GameSnapshotPrefix + "\n" + board.SanitizedText`) when the suite carries one, then the
  user message. Before this round the board **was** the first system message — and the first system
  message is exactly what `GoogleProvider.OrderSystemParts` and `AnthropicProvider.BuildChatRequestBody`
  replace with the prompt's own segments, so a Google or Anthropic candidate on a snapshot suite received
  **no board at all**. `OpenAiResponsesProvider` builds `instructions` from the history's system messages
  and never from `SegmentedPrompt`, so it took that same first system message — the board — as its only
  system text, and an OpenAI candidate received **no system prompt at all**.

  | Provider | What actually reached the wire | What was silently missing |
  |---|---|---|
  | Google (`GoogleProvider.OrderSystemParts`) | The system prompt's own segments | The board |
  | Anthropic (`AnthropicProvider.BuildChatRequestBody`) | The system prompt's own segments | The board |
  | OpenAI (`OpenAiResponsesProvider`, builds `instructions`) | The board, as the entire system text | The system prompt |

  `OpenAiResponsesProvider.BuildChatRequestBody` now falls back to `segmentedPrompt.FullPrompt` for
  `instructions` when the history carries no system message; `ChatService` always supplies one on the live
  chat path, so that path is unaffected. **Affected runs: 36, 37, 38, 42, 46, 47, 51** — prompt-less on
  OpenAI since 2026-09-10. Runs **50 and 51** are the snapshot runs that exposed the defect; neither is
  usable for model selection.

- **The wire check.** New `Overseer/Services/Benchmarking/BenchmarkCandidateRequestProbe.cs`:
  `Verify(IAiProvider provider, List<object> seedHistory, SegmentedPrompt? segmentedPrompt, string
  systemPrompt, string? boardText, int? questionNumber = null)` runs the same `provider.PrepareMessageHistory`
  → `BuildChatRequestBody(modelId: "probe", …)` → `JsonSerializer.Serialize` pipeline a real candidate call
  would use, and requires the serialised body to contain the first 120 characters of each thing that must
  be there, escaped through the same serializer. With a segmented prompt it checks each non-empty segment
  separately, because the providers emit the segments as separate JSON strings — separate Gemini parts,
  separate Anthropic system blocks — and a needle spanning a segment boundary would be found nowhere
  although every character of the prompt is present; it falls back to the flat prompt when the segments do
  not concatenate to it. No network call, no tokens spent. It runs at two levels:
  - **Per run, fail closed** — once, immediately after the instrument fingerprint is populated, on all
    three candidate entry points (`RunAsync`, `RunFailedQuestionsAsync`, the single-answer re-run), seeded
    from the run's first question. A throw reaches the existing terminal handler: the run row is marked
    `Failed` with the message, no answers are created and nothing is spent.
  - **Per question, fail the answer** — the first statement inside the `try` that wraps
    `_agentLoopRunner.RunAsync`, in both `ExecuteSingleQuestionAsync` and `ReExecuteSingleAnswerAsync`. The
    message reads *"Harness delivery check failed: the {provider} request for question {n} did not contain
    the {board | system prompt}."* (both parts missing reads "the system prompt and the board"). It lands
    in the existing `catch (Exception ex)`, becomes `terminalError`, and since
    `BenchmarkProviderErrorClassifier` does not classify an `InvalidOperationException` with that text as a
    provider error, the answer is stored `BenchmarkAnswerStatus.Failed`: cleared text, that error message,
    no assessor call, counted under Transport Defects, out of the Intelligence Index, and repeatable with
    re-run failed questions. A test pins the message to `Failed` rather than `ProviderError`. No new flag,
    status or column.

- **The claim verifier now sees the board too.** `BenchmarkClaimVerificationPrompt.BuildPrompt` takes
  `boardName` / `boardText`. When there is a board it emits, after the rubric block, a
  `--- GAME BOARD (the snapshot the question is about; authoritative for the hero's current state) ---` …
  `--- END GAME BOARD ---` block, adds instruction **3c** — a claim about the hero's current state is
  checked against the GAME BOARD, not against the rubric's BOARD FACTS, which quote only part of it, cited
  as `board: "<the quoted line>"`, with absence from the rubric not meaning absence from the board — and
  extends instruction 5's citation examples with the `board:` form. `BenchmarkService` passes
  `run.BenchmarkSuite?.GameSnapshot?.Name` / `?.SanitizedText`, and `RetryFailedClaimVerificationAsync`
  gained the `Include(r => r.BenchmarkSuite).ThenInclude(s => s!.GameSnapshot)` it was missing.
  `BenchmarkClaimVerificationParser` needed no change: it validates only that a citation is non-blank,
  never its shape. Cost: roughly 3,000 input tokens per verification conversation on a snapshot suite,
  cached within it.

- **The report says what was sent, not just what was built.** *Chat Prompt Under Test* gains
  `· **Game snapshot:** yes|no` from `promptOpts.HasGameSnapshot`, and a `- **Delivery:**` line: for
  `HarnessVersion` ≥ 29, *"prompt and board delivery verified against the provider request body before the
  first question."*; for an older run with a snapshot, *"not verified — before harness 29 a Google or
  Anthropic candidate did not receive the board and an OpenAI candidate did not receive this prompt."*; for
  an older OpenAI run with no snapshot, *"not verified — before harness 29 an OpenAI candidate did not
  receive this prompt."*; and no line at all for a pre-29 run on another provider with no board, where
  nothing was wrong. The Angular diagnostics `options:` line gained `snapshot=<bool>` and the run-detail
  prompt summary gained `· snapshot`.

- **T1–T3, the tool contracts.** `MarkdownSectionExtractor.Extract`: a `section` containing `›` is tried on
  the full string first (unchanged), then on the trimmed text after the **last** `›`, through the same
  three-pass helper, so the ambiguity refusal still holds. `NetHackWikiViewTool`: an article miss now
  returns a payload opening `No NetHack wiki article matched '` that lists up to four candidate titles (the
  resolver's own, else the top titles of one wiki-search-style query, exceptions swallowed) and ends with
  `Try nethack_wiki_search.`, capped at 600 characters, `Success = true`, with a `catch` that returns the
  old bare sentence. `SourceCodeService.GetItemStats`: a miss lists up to five distinct near names from
  `_itemResolver.FindItemNames` — called with the whole name and then with each word of ≥ 4 characters — as
  *"Did you mean: …"*, keeps the existing `item_lookup` / `wiki_search` pointer, and always appends *"A
  name that is an unidentified appearance — 'hooded cloak', 'orange potion', 'red mushroom' — has no entry:
  appearances are randomized per game; see the snapshot's Discoveries section."*, capped at 600 characters.

`BenchmarkAssessmentPrompt.HarnessVersion` is now **"29"**, with a v29 paragraph in its version history. A
run stamped 29 differs from one stamped 28 on `HarnessVersion` alone, but the candidate receives materially
different input on a snapshot suite or on OpenAI, so it is a **Fundamental** comparability break against
every earlier run of either kind, not Tier C.

### Harness Version 30 Updates

Prompted by the analysis of run 52, the first harness-29 run of a snapshot suite. It confirmed that the
candidate, the primary assessor, the second reader and the claim verifier all receive the board, and found
three secondary grading paths that still graded a snapshot suite without it. It also found a report line
asserting a verification nothing recorded, a grader calling tool-grounded facts "fabricated", and a
`wiki_search` snippet rule that hid an article's stat block. `ScoringMethodVersion` stays **10** and
`CandidateSystemPromptSha256` does not move; `ToolGuidesSha256` **moves**, because `wiki_search.md` gained
one sentence.

- **Every grading path loads the board, and none grades without it (H1).** `ReassessSingleQuestionAsync`,
  `RetryFailedAssessmentsAsync` and `RunAssessorCalibrationAsync` loaded the suite's questions but not its
  `GameSnapshot`; lazy loading is off, so `run.BenchmarkSuite?.GameSnapshot?.SanitizedText` was `null` and
  the assessor prompt was built rubric-only, silently. Their loads are now the internal query builders
  `BenchmarkService.ReassessmentAnswerQuery`, `RetryAssessmentsRunQuery` and `CalibrationRunQuery`, each
  including the board. New `BenchmarkBoardGuard.RequireBoardLoaded(run)` throws when the suite references
  a snapshot that was not loaded, and runs before every board read. What a refusal does depends on the
  path. On the primary assessment the answer is stored `AssessmentStatus = Failed` with the message, and
  can be re-run. On the second opinion it becomes `SecondOpinionError`. On the claim verifier it becomes
  `ClaimVerificationError`, which *retry failed claim verification* picks up. Calibration skips the answer,
  and a trial re-assessment fails the trial.

- **Delivery is recorded, per role (H2).** `BenchmarkRun.CandidateDeliveryVerifiedAtUtc` is stamped when
  the pre-run probe (`VerifyCandidateDeliveryBeforeRun`) passes. `BenchmarkRunAnswer.AssessorBoardChars`,
  `SecondOpinionBoardChars` and `VerifierBoardChars` record the board characters each role's prompt
  carried: the length of `SanitizedText`, or `0` when the suite has a board that did not reach the
  prompt, or `null` when the suite has no board. A trial re-assessment writes `SecondOpinionBoardChars`,
  because its verdict is stored in the second-opinion columns. From harness 30 the report's
  `- **Delivery:**` sentence reads *"verified … before the first question (<time> UTC)"* only when the
  stamp exists; otherwise it reads *"not recorded"*. A run stamped 29 keeps the sentence it always had.
  On a board suite the Delivery line is followed by
  `Board delivered — assessor N of M graded, second opinion N of M, claim verifier N of M; synthesis: yes; difficulty assessment: digest (no map).`
  A role with any answer at `0` gets a `- **Board Not Delivered (<role>):**` line under Run Integrity.
  The run-detail DTO carries `CandidateDeliveryVerifiedAtUtc` and `BoardDelivery`, and the Angular run
  detail and diagnostics text print the same figures.

- **Evidence-informed re-grade (H3), advisory.** The assessor never sees tool results, and on run 52 it
  called several tool-grounded facts invented. After an answer's claim verification is persisted, the
  answer qualifies for a re-grade when it carries `ContestedAccuracyDeduction` or
  `ContestedCriticalError`, or is a verification-cleared Accuracy deduction
  (`BenchmarkService.IsVerificationClearedAccuracyDeduction`, the predicate the report's figure of that
  name also uses). A qualifying answer is re-graded once by the run's **primary** assessor, whose prompt
  is `BenchmarkAssessmentPrompt.BuildEvidenceInformedBody`. That prompt is the unchanged per-question
  body, then a `--- VERIFIER FINDINGS ---` block (claim, verdict, citation and basis, with the
  critical-error quote and the out-of-rubric basis labelled as the assessor's own statements), then
  `EvidenceInformedInstruction` and a `withdrawn` list in the schema. The first verdict's levels, score
  and comment are withheld, as they are from the blind second opinion. The result is stored in
  `EvidenceInformedQualityScore`, `EvidenceInformedCriticalError` and `EvidenceInformedJson` (the
  `SecondOpinionJson` shape plus `withdrawn`). **No stored level, score, cap, flag or index changes, and
  the second-opinion columns are untouched.** A new primary verdict clears the three columns. Cost is
  pooled into the answer's `Assessment*` fields and recorded under role context 4. The call is bounded by
  `Benchmark:SecondOpinion:TimeoutSeconds`, the only grading-call timeout the harness has. A failure is
  logged and leaves the columns null. `Benchmark:EvidenceInformedRegrade:Enabled` (default `true`, also
  written into `appsettings.json`) switches the stage off. The report prints
  `- **Evidence-informed Sensitivity:** N / 100` beside the other sensitivity figures and in § 7,
  computed the way *Contested-Verdict Sensitivity* is. It prints a per-question
  `Evidence-informed re-grade (<assessor>): S / 100, critical error yes|no — withdrew: …` line, and the
  synthesis prompt gets one line per re-graded answer.

- **The synthesis and the rubric gap author see the board (H4).** `BuildFinalSynthesisPrompt` takes
  `boardName` / `boardDigest` and prints the digest (≤ 6,000 characters, no map) under a
  `GAME CONTEXT BOARD (DIGEST …)` block. `ExecuteFinalSynthesisAsync` queries it directly, because
  `RerunFinalSynthesisAsync` loads no suite. `BenchmarkRubricGapAuthorPrompt.BuildPrompt` takes the
  suite's `BenchmarkGameSnapshot` and prints `SanitizedText` whole, in the untrusted-data delimiter form
  the Suite Wizard uses, with rule 8: a point about the hero's situation must be quotable from the board.

- **A critical-error quote is verified in its context (H6).**
  `BenchmarkClaimVerificationPrompt.CriticalErrorQuoteContext(answerText, quote)` returns the nearest
  preceding heading or non-list line, stripped of heading and bold markup, when the quote is a list item
  or a fragment (no sentence-ending punctuation, under 80 characters). The prompt shows it as a
  `Context (not part of the claim): Under "<line>":` line inside claim 0's block, and adds the
  instruction to judge the text as placed under that heading. The claim text itself stays the verbatim
  quote. `BenchmarkClaimVerificationParser` stores the submitted text and every reader matches the
  critical-error quote against it verbatim, so folding the context into the claim would break that
  matching. Run 52's Q5 (`Safe to Eat (Vegan)` over a mushroom line) is the case this fixes.

- **H7, H8.** On a board suite the Grounding note reads *"answered from the board with one tool call or
  fewer — … Expected on a snapshot suite when the board settles the question."* and no longer says the
  items may not test source retrieval. A failed-question re-run or a single-answer re-run of a run whose
  `CandidatePromptOptionsJson` is blank is now refused with a message naming the missing record. It used
  to build default options, silently resetting `VerboseMode`. The refusal comes before any status flip
  or verdict clearing.

- **`wiki_search` lead block (T1, tool layer).** `WikiSnippetExtractor.BuildSnippet`, when some section
  scores above zero, returns the whole article (footer `— complete`) if it formats to
  ≤ `perResultChars / 2`. Otherwise it always keeps the article's first section with a body when that
  section formats to ≤ `LeadBlockMaxChars` (600), ahead of the ranked sections. The first ranked section
  is still always kept. On run 52, `Spells/Cure petrification.md` came back as its one-line *Description*
  because the level / mana / components block scored 0.

`BenchmarkAssessmentPrompt.HarnessVersion` moved to **"30"**, with a v30 paragraph in its version history.
A run stamped 30 differs from one stamped 29 on `HarnessVersion` and `ToolGuidesSha256`, which is below
Tier B. Migration: `AddBoardDeliveryAndEvidenceInformedVerdict` (seven nullable columns, none backfilled;
null means not recorded).

### Harness Version 31 & Scoring Method Version 11 Updates

Prompted by the analysis of run 53 (Gemini 3.7 Flash @ `medium`, snapshot suite 8, harness 30, method
10). No grader sees what the candidate's tools returned, so the assessor called true, tool-grounded facts
"fabricated" on seven of 18 answers and the harness caught four. The harness-30 re-grade withdrew
deductions that no verifier finding bore on, on two answers. And the Accuracy anchors for levels 4–6
rewarded source-level depth that the production concise prompt tells the candidate not to produce.
`HarnessVersion` moves to **"31"** and `ScoringMethodVersion` to **11**, together. `ToolGuidesSha256`
**moves** (`wiki_search.md`, `get_artifact_stats.md`). `CandidateSystemPromptSha256` does not move
because of this round: per-tool guides are not inlined into the candidate prompt. No EF Core migration:
everything new is stored inside existing JSON columns.

- **Grading operations are refused on a run graded under another scoring method (H0).**
  `BenchmarkService.IsCurrentScoringMethod(run)` is true only when the run's stored
  `ScoringMethodVersion` equals the constant. When it is not, these are refused with
  `ScoringMethodRefusal(run)` — *"Refused: run N was graded under scoring method S, and this build grades
  under C. … Start a new run instead."*:
  - re-assess, including a trial;
  - calibrate, which also gains the aborted-run check it lacked;
  - re-run one answer;
  - re-run failed questions;
  - retry failed assessments;
  - retry claim verification.

  The controller refuses synchronously, right after the aborted-run check. That comes before the row is
  set to `Running`, before any timestamp, run-manager registration, background scheduling or billing.
  The service repeats the test at the top of each operation, for a direct or background entry. It
  restores the terminal status and records the refusal in `ErrorMessage`; a trial restores the captured
  status instead. The harness version is deliberately not part of the test, because a re-run under a
  newer harness is a recorded case (`RerunHarnessVersion`).

  **Series resume** is refused, with outcome `Invalid`, when any member run of the series carries
  another scoring method. Unlike a moved instrument hash this cannot be overridden: a member launched
  now would be graded under this build's method. **Still available on every historical run:** reports,
  exports, the tool-call log, diagnostics, comparisons, group analysis, cancel, Rescore and re-running
  the final synthesis. The synthesis grades nothing: `BuildFinalSynthesisPrompt` does not build on
  `BuildPerQuestionPreamble` and carries no anchor text. The accepted cost is that a historical run
  with a failed assessment can no longer be repaired in place; start a new run.

- **Rescore never stamps a method onto levels it did not grade.** `RescoreRunAsync` remaps stored
  levels to points and recomputes the indices; it cannot apply new anchors, because it grades nothing.
  It stamps `LastMethodRescoreCanApply` (**10**) when the stored value is 10 or lower, and leaves 11 or
  higher untouched. Methods up to 10 changed how stored levels become scores, which a rescore applies.
  Method 11 changed only the anchors, which it cannot apply. The documented use — moving a
  pre-recalibration run onto the current Speed Index scale — keeps working for runs 11–53.

- **The sentences the assessor charged as false are checked (H1).**
  `BenchmarkService.ExtractAccusedQuotes(answerText, accuracyEvidence)` takes each span between paired
  double quotes, straight or typographic, in the stored accuracy evidence. A span must be 15–400
  characters and occur in the answer once Markdown emphasis markers and whitespace runs are ignored,
  the normalisation the parser applies to a critical-error quote. The **answer's** own span is kept,
  not the assessor's copy. A span that is not in the answer is a rubric or board quotation and is
  dropped. At most three are kept per answer, longest first, ties in evidence order.

  An answer is eligible at Accuracy ≤ 4 or when it carries `ContestedVerdict`, whatever its
  unverified-claim count. `Benchmark:ClaimVerification:AccusedQuotesEnabled` (default `true`) switches
  the feature off. Each accused sentence carries a context excerpt: its list heading when it is a list
  item or fragment, and the text immediately before it. A literally true fragment under a heading that
  makes it false advice is judged in that context. This is a bounded heuristic: it finds only the
  accusations the assessor quoted.

- **Roles are server-owned (H1).** Every item submitted to the verifier comes from one ordered
  manifest (`BuildClaimManifest`), and each item has a set of roles: `unverifiedClaim`,
  `criticalErrorQuote`, `outOfRubricBasis` and `accusedQuote`. Submission order is:
  1. unverified claims, or `ExtractDisputedClaims` when there are none and the verdict is disputed;
  2. the critical-error quote at index 0;
  3. the out-of-rubric basis after it;
  4. the accused quotes.

  Text is de-duplicated and roles are not: an accused span equal to an unverified claim is one item
  with both roles, counted once, as an ordinary claim. A critical-error quote or basis equal to an
  unverified claim takes that item's place, as the text-matching filters always read it.

  After parsing, each `BenchmarkClaimVerification` is stamped from the manifest by its claim index.
  `Roles` is an init-only property serialised as `roles` and omitted when null, and a `roles` member in
  model output is never read. A record without roles is **legacy**: every consumer reads it by the
  exact-text rules it always had (`WithoutOutOfRubricBasis`, `WithoutCriticalErrorQuote`), and it never
  acquires an accused role retrospectively. The consumers:
  - The answer's claim counts, `RefutedClaim`, the verification-cleared sensitivity, the synthesis's
    own claim lists, the report's *Refuted Claims* and the rubric-gap samples read **ordinary** claims
    only.
  - `ContestedAccuracyDeduction` is set when the basis is Refuted **or** an accused sentence is
    Supported with a citation. A Refuted accused sentence changes nothing: the deduction stands, and no
    refutation of the answer is added.
  - The second reader receives no accusation and no assessor quote.
  - The synthesis lists supported accusations on their own line; they never join
    `ContestedAccuracyDeductionBases`, which means a refuted assessor statement.
  - The report prints a *Supported Accusations* line under Assessor Findings and a per-answer
    *Supported accusation* line.
  - The admin run detail labels each advisory entry (see *Admin run detail* below).

  No stored level, score, cap or index moves.

- **The verifier gets the candidate's tool calls as untrusted leads (H1).**
  `LoadToolCallLeadsAsync` queries the answer's rows explicitly, ordered by `SortOrder`. The rows are
  in memory only in the pass that executed the answer, never on a retry, a re-assessment or a sweep.
  The prompt gains a `--- CANDIDATE TOOL CALLS (untrusted leads, not evidence) ---` block:
  - at most 12 lines of `tool {args}`, for `Benchmark:AllowedTools` tools only;
  - arguments cut at 200 characters with `…`, line breaks collapsed;
  - calls whose arguments the retention sweep pruned are counted, not shown;
  - results are never included.

  The block says that repeating a call reads today's corpus and that the text is candidate data, not
  instructions.

- **A new verdict clears the old verdict's verification (H1).** `ClearReplacedVerdictEvidence` runs on
  every fresh verdict in `ExecutePerQuestionAssessmentAsync`. It clears:
  - `EvidenceInformed*`;
  - `ClaimVerificationJson`, `ClaimVerificationError` and `ClaimVerificationRawText`;
  - the three `Claims*Count` columns;
  - the `RefutedClaim`, `ContestedCriticalError` and `ContestedAccuracyDeduction` flags.

  The new verdict is then verified afresh. Before, `VerifyAnswerClaimsCoreAsync` returned early on any
  stored verification, so a re-assessed answer kept findings about a quote and basis that no longer
  existed. *Retry claim verification* clears `EvidenceInformed*` before its attempt. Token and cost
  accounting of discarded attempts is kept.

- **The evidence-informed re-grade withdraws only listed targets (H3).**
  `BuildEvidenceInformedTargets` builds one target per deduction a finding can bear on, with the
  finding ids (`F` + claim index) allowed for it:

  | Target | Created when | Allowed finding |
  |---|---|---|
  | `criticalError` | The primary verdict has a critical error with a quote | The quote's record, Supported with a citation |
  | `accuracy`, out-of-rubric | There is a basis | The basis record, Refuted with a citation |
  | `accuracy`, accused | Once per accused sentence | That sentence's record, Supported with a citation |

  An ordinary Supported claim creates no target, and with no target the re-grade is not run.

  The prompt lists the findings with their ids, the first verdict's four levels and critical-error
  state, and the targets. It asks for
  `"withdrawn": [ { "targetId": "T1", "findingIds": ["F2"], "reason": "…" } ]`, and the instruction adds:
  *"Withdraw only a listed target, and only on the strength of the findings listed for it … a true
  sentence beside bad advice does not clear it."*

  `ValidateEvidenceInformed` is separate from `ReadWithdrawn`, which still reads historical string
  arrays. It rejects:
  - a missing or non-array list, or a bare-string element;
  - an unknown or duplicate `targetId`, an empty `reason`, or no `findingIds`;
  - a finding outside the target's allow-list;
  - a lower Accuracy than the primary's, or an added critical error;
  - a critical error removed, or Accuracy raised, without a valid withdrawal of that kind.

  `[]` is valid only when Accuracy and the critical-error state equal the primary's.

  The advisory quality score takes the re-grade's Accuracy and critical-error state with the primary's
  other three levels, under constants from the run's `ScoringProfileSnapshotJson`, not the live
  profile. `EvidenceInformedJson` keeps its fields and gains `validationVersion: 1`, `targets`,
  `accepted`, `withdrawnDropped`, `validationErrors` and `eligibleForSensitivity`; `withdrawn` holds
  the accepted items.

- **One eligibility predicate (H3).** `IsEligibleEvidenceInformedRegrade(run, answer)` decides the
  eligible count, the substitution in both printed sensitivity figures, and the synthesis input:
  - A validated record counts when it passed.
  - A record with no validation provenance on a run stamped 31 or later is ineligible, even if its
    JSON parses.
  - On an earlier run the existing calculation is kept exactly, over the graded answers, labelled
    *legacy, unvalidated*. No old record is restamped.

  From harness 31 the figure (*Evidence-informed Sensitivity (validated re-grades only)*) is taken over
  the Intelligence Index's own item set. An excluded answer keeps its primary score in the figure, and
  the excluded count is printed even when every re-grade is excluded. The per-answer re-grade line
  prints the four stored levels, the accepted withdrawals, the dropped ones with reasons, and its
  status: validated, rejected, legacy, or no provenance. A person reads every accepted critical-error
  withdrawal against the rubric clause before the advisory figure is used; the scored critical error
  never moves.

- **Detectors (H2, H4), advisory.** `BenchmarkVerdictConsistency.IsPrecisionGroundedAccuracyDeduction`
  sets `UnevidencedDeduction`, at any unverified-claim count, when Accuracy is below 6 and the
  evidence withholds the level for missing precision, nuance or depth with no undenied falsehood.
  Examples: *held at 4 rather than*, *lacks the source-level precision*, *does not reach source-level
  precision*. That was run 53's Q9 and Q14 shape. The report prints an *Accuracy Withheld for
  Precision* count on its own.

  `FactualErrorsAdmittedRegex` accepts *not factually clean*, *cannot be characterised / described as
  factually clean*, and *not confined to omissions*, which must have omissions as its object.
  `DefectDenialRegex` accepts *no false claims (identified / found)*. `NamesNoDefect` strips only the
  denial clause, sentence by sentence (`HasUndeniedFalsehood`), before testing for an affirmative
  falsehood. `DefectRegex` is unchanged. These are heuristics, not proof that a deduction is invalid.

- **Tool-result diagnostics (H6).** `BenchmarkToolResultClassifier.Classify(row)` returns independent
  facets: outcome, payload available or unavailable, not-found, model-visible cut, record cut, partial
  result, content error. A `null` `Result` beside a non-zero `ResultLengthChars` is the retention sweep:
  it is neither a hit nor a miss. Misses are matched at the start of the result against each tool's
  own openings. A `get_knowledge_article` miss is a failed call and is reported apart. Near-miss
  openings (`Several wiki articles are titled '`, `[No function|macro|struct named '`, `[Note: …`) are
  not misses.

  The Tool Usage Profile gains *Not-found results: N of K inspectable successful payloads (…); U
  payloads unavailable* and *Cut before the model saw it: N by the per-tool cap; batch budget and
  turn limit not recorded. Cut in the stored record only: M*. `AgentLoopRunner` stores a result
  before the batch budget and the turn limit apply, so those two cuts never reach a stored row, and
  the report says *not recorded* rather than print a zero. The report's per-question tool table and the
  tool-call log gain a `Note` column with a fixed vocabulary: `miss`, `cut`, `record cut`, `partial`,
  `content error`, `unavailable`.

- **Grading prompt (H3b, H5).** Beside the critical-error quote rule: *"Quote the sentence that commits
  the error the clause names. When the clause is about advice or an implication, quote the advice, not
  a true statement beside it."* After the Readability anchors: valid Markdown and LaTeX math
  (`$…$`, `$$…$$`, `\(…\)`, `\[…\]`) is judged as the player sees it typeset.

- **Admin run detail.** Each claim-verification entry carrying an advisory role shows a label:
  *critical-error quote*, *out-of-rubric basis*, or *sentence the assessor charged as false*. One note
  says these were sent to check the assessor and are not claims the answer was marked down for. A
  legacy record shows no label.

- **Tool layer (T1–T4).**
  - **`source_code_search`:** a filtered literal miss runs one bounded unfiltered probe of the whole
    query and says where it matches (*"(top 3 files shown)"* when three are named) or that it matches
    nowhere unfiltered. It never claims corpus-wide absence. The probe is skipped for `is_regex` and
    `whole_word`. The enriched miss is kept to 600 characters: the query echo is cut at 80 with `…`,
    and the near-neighbour hints come last, in whole sentences. An unfiltered miss is byte-identical
    to before.
  - **`wiki_search` ranking:** `WikiService.GetRelevantSnippets` ranks articles matching more of the
    query's distinct analyzed terms first, then by the original BM25 order. It falls back to the
    legacy order for under 2 or over 16 terms, or an uppercase `AND` / `OR` / `NOT`. `totalHits` — the
    `M` of `Showing N of M` — is unchanged. Three callers get the new order: `wiki_search`, its miss
    probe, and the `wiki_view` miss probe.
  - **Flag fields:** `SourceCodeService.ParseFlagField` unwraps a balanced outer parenthesis before
    splitting, so a flag union such as The Holy Grail's `aflags` no longer comes back as
    `["(AF_RESTR", "AF_NAME_KNOWN_WHEN_INVOKED)"]`. An expression that still contains a parenthesis is
    returned verbatim.
  - **`get_artifact_stats.md`:** the guide says where a base item's effect is found: `get_item_stats`
    under its exact name.

- **Rollback keys.** `Benchmark:ClaimVerification:AccusedQuotesEnabled` turns accused-sentence
  checking off. `Benchmark:EvidenceInformedRegrade:Enabled` turns the re-grade off. The anchors have no
  switch: a follow-up wording change is a **new** method version, never a silent edit of method 11.

#### Scoring Method 11: the Accuracy anchors

Levels 0–3, Completeness, Conciseness, Readability, the critical-error definition, the weights and the
level-to-points mapping are unchanged. Levels 4–6:

| Level | Method 10 | Method 11 |
|---|---|---|
| 4 | Fully accurate; all factual claims align with GnollHack mechanics with no meaningful errors. | Accurate in substance, with minor imprecisions that are not errors a player would act on: a loosely stated figure, an imprecise term, a rule stated without a condition that does not apply here. |
| 5 | Highly accurate and precise; demonstrates nuanced understanding of mechanics and interactions. | Accurate, with a single trivial imprecision and nothing a player could act on wrongly. |
| 6 | Flawless, authoritative precision matching C core source code implementation details exactly. | No false or imprecise statement: every claim the answer makes that you can adjudicate is correct as stated. |

Directly beneath them, a scope rule: *"ACCURACY grades only what the answer states. Depth, length,
source-level detail and how many mechanics are covered are not ACCURACY criteria. A two-sentence answer
in which you find no false or imprecise statement is level 6; what it leaves out is graded under
COMPLETENESS. … A claim you cannot adjudicate goes to `unverifiedClaims` (instruction 8): it does not
lower the level, and level 6 does not certify it. Every level below 6 must name a statement the answer
makes and say what is wrong or imprecise about it."*

Instruction 8's last sentence became *"Award an ACCURACY level below 6 only for a named statement in the
answer that is wrong or imprecise."* The evidence rule became *"An accuracy deduction must name
something the answer **states** that is wrong or imprecise."*

**Why.** The method-10 anchors for 5 and 6 rewarded source-level depth, and the production chat
prompt's concise mode tells the candidate not to produce it. On run 53, short answers containing no
false statement were held at 4 or 5 "rather than 6" for lacking source-level precision. Accuracy
measured compliance with the chat prompt instead of truthfulness. Completeness anchors 5 and 6 also
reward breadth that concise mode limits. That is a recorded product trade-off (run 12 tested and
refuted verbose mode) and is not changed. An empty, failed, cancelled or provider-error answer never
reaches the grader, so it cannot receive a vacuous level 6.

**Comparability.** Scores are not comparable with method 10 on Accuracy, and therefore on the quality
score and every index. The break is not confined to answers graded 4 or 5, because the index is a
weighted mean over all of them. On the default profile the largest single-answer lift, Accuracy 4 → 6,
is 28 points × 0.55 = 15.4 quality points before caps and rounding. Scores rise, and a rise across this
boundary is not evidence of anything. The comparison view refuses to rank a method-10 run against a
method-11 run because `HarnessVersion` and `ScoringMethodVersion` are both `Instrument` keys and both
move here: two differing instrument keys are `NotComparable`. A later round that bumped only one of
them would produce Tier C instead.

### Harness Version 32 Updates

Prompted by the analysis of run 54 (Gemini 3.7 Flash @ `medium`, snapshot suite 8, harness 31, method
11). The suite's board had been replaced with a newer export, and two rubrics still quoted lines the new
board no longer carried. The contested-deduction flag named only one of its two causes. The
accused-sentence extractor missed single-quoted sentences, took quotations the assessor approved of as
accusations, and printed no outcome for the sentences it did send. Half the evidence-informed
re-grades were rejected on form. The verifier cited unreachable code once and supported a false number
from a wiki page once. And every grading call paid full price for the board, because it sat after the
question. `HarnessVersion` moves to **"32"**; `ScoringMethodVersion` stays **11**, and
`FirstValidatedRegradeHarness` stays **31**. No tool guide moves, so `ToolGuidesSha256` and
`CandidateSystemPromptSha256` do not move because of this round. One EF Core migration
(`AddBenchmarkRunBoardFactsCheck`) adds the nullable `BenchmarkRuns.BoardFactsCheckJson`.

**A 31-stamped snapshot-suite run is not grade-comparable with a 32-stamped one.** The graders read the
same board text in a different place (A6), a different set of accused sentences reaches the verifier
(A3), and the verifier works under two more instructions (A5). `HarnessVersion` is an `Instrument` key,
so the comparison view already refuses to pool across the boundary.

- **Rubric board quotes are checked against the board (A1).** `BenchmarkBoardFactsChecker` reads every
  `**BOARD FACTS**` section of a suite's rubrics — from the line that is exactly that heading to the
  next line starting with `**` — and every bullet in it (a line starting `- `, with any following
  non-empty line that is neither a bullet nor a heading). Inside a bullet, straight double quotes pair
  sequentially (first with second, third with fourth; an unpaired last quote is ignored) and typographic
  `“…”` pairs are read the same way. Each non-empty span is looked up in the stored board
  (`BenchmarkGameSnapshot.SanitizedText`, the text every grader receives) ordinally, after converting
  CRLF and lone CR to LF, with no other normalisation: the authoring contract is verbatim. Sequential
  pairing is what makes a quoted board line containing quote characters safe, because the fragments on
  either side are each still substrings of the board.

  The result (`BoardFactsCheckDto`) counts bullets, checked literals and bullets with **no** quoted
  literal, and lists the **missing literals** — the only issues. A bullet with no quoted literal is a
  count, never a warning: an absence fact such as *"The status line shows no hunger state."* cannot be
  quoted, and the harness's own rubric generator writes unquoted prose. Reporting those as issues would
  warn on every write of every generated suite. The count is there so that "none missing" is not read
  as "every fact was checked".

  The check is shown after uploading or replacing a suite's board (`POST suites/{id}/snapshot`),
  after saving the board text in the snapshot viewer (`PUT snapshots/{id}/text`, which now returns
  `{ snapshot, boardFactsCheck }`; the check is null when no suite owns the snapshot), and after
  importing questions. The question import panel also calls the new
  `GET suites/{id}/board-facts-check` after a whole-suite import, because that is the path the offline
  authoring skill feeds. The upload dialog stays open when a literal is missing, so the list can be
  read before it closes. `CreateQuestion`, `UpdateQuestion`, `AcceptRubricAddition`, question
  generation, default-suite import and suite duplication are not instrumented; the on-demand endpoint
  and the launch-time stamp cover them. `RunAsync` stamps the check into
  `BenchmarkRun.BoardFactsCheckJson` beside the other board fields (null for a suite with no board), so
  a run records what applied when it started and is never recomputed against later edits. The report
  manifest prints, under *Game Snapshot*, *"Rubric board quotes: N checked, none missing."* or the
  missing count by question — *"these rubrics quote text this board does not contain; grades on them
  rest on stale facts"* — followed by the unquoted count when it is not zero. The run detail shows a
  notice only when something is missing. **Everything here is advisory**: no write, question or launch
  is ever refused.

- **The board is a Fundamental comparability key (A1).** `GameSnapshot`, valued from
  `GameSnapshotSha256Used`, joins the `Fundamental` keys: two runs of one snapshot suite on different
  boards sat different exams and are not pooled or ranked. Runs 53 and 54 read different boards, and no
  key recorded it. A suite with no board has no value for this key, and that is **not** an absent
  identity: `BenchmarkComparabilityKey.IsAbsentIdentity` exempts it, so two non-board runs still match on
  it and are not excluded from the comparison view. `ComputeKeyHash` hashes every `Name=Value`, so
  **every run's key hash changes**, and every stored `BenchmarkRunGroup.ComparabilityKeyHash` is stale.
  Nothing compares that column for equality; re-analyse a stored group analysis to refresh it, as
  already advised for the narrowed scoring-profile key. From comparability key definition version 2
  (2026-09-24) the version that produced a stored hash is recorded beside it
  (`BenchmarkRunGroup.ComparabilityKeyVersion`), and a hash of another version is flagged stale in the
  multi-run view; see **Model Configuration Snapshots**.

- **`ContestedAccuracyDeduction` names its cause (A2).** The flag is raised when the verifier refutes
  the own-knowledge basis of an out-of-rubric Accuracy deduction, **or** supports a sentence the
  assessor quoted as false. The report now splits the run-level list by cause, read from each answer's
  stored verification items by role — *"own-knowledge basis refuted: …; a sentence the assessor quoted
  as false was supported: …"* — and lists a legacy record without roles under *"cause not recorded"*.
  The per-answer line names its cause, and the run detail notice and tooltip describe both. The Advisory
  Flags parenthetical is unchanged.

- **Accused-sentence extraction reads single quotes and approval (A3).** `ExtractAccusedQuotes` also
  takes straight and typographic single-quoted spans. For those only, the opening quote must follow the
  start of the text, whitespace, `(` or an em dash, and the closing one must precede the end of the
  text, whitespace or punctuation; the existing guard that the span occurs in the answer is what keeps
  contractions from pairing. A span is then skipped when its clause approves of it and nothing in the
  clause charges it. The clause runs back to the previous `.`, `;` or `:` followed by a space and
  forward to the next. An enclosing parenthesis decides on its own only when it holds a marker of
  either kind. Otherwise the clause around it decides, which is what keeps *Correct on the core
  mechanic ("…")* approved. The approval and charge vocabularies are
  `BenchmarkVerdictConsistency.AccusationApprovalRegex` and `AccusationChargeRegex`; a charge wins
  when both occur. The report prints, per answer, *"Accused sentences checked: N — supported a, refuted
  b, indeterminate c"* with the refuted and indeterminate ones listed; at run level the same totals
  under Assessor Findings; and the Claim Verification Yield line states the two populations apart
  (*"X unverified claim(s) + Y accused sentence(s) checked"*). All of it is computed at render time from
  `ClaimVerificationJson` by role. The run-level `Claims*Count` columns keep counting ordinary claims
  only, and no column is added.

- **An evidence-informed re-grade gets one repair turn (A4).** The re-grade body's schema block now
  carries `withdrawn` itself, with *"`withdrawn` is required; use `[]` when you withdraw nothing."*;
  the trailing prose that mentioned it is gone. Each withdrawable target lists only the verifier
  findings that bear on it, and the rest are printed once as context that must never be cited. When the
  first reply parses but fails only because `withdrawn` is missing or not an array, the harness sends
  one repair turn, gated by `Benchmark:EvidenceInformedRegrade:ParseRetryEnabled` (default `true`). It
  reuses the same request and budget and appends the first reply and a repair message quoting the
  error. Tokens and duration are summed over both turns, and `EvidenceInformedJson` records
  `repairTurns` and the repair's outcome. Nothing else is retried — no provider error, timeout,
  unparseable reply, unknown id or dropped withdrawal — and the all-or-nothing eligibility guard is
  unchanged. Calibration and trial reassessment, which share the assessor call, are unaffected. The
  primary, second-opinion, calibration and trial bodies are byte-identical apart from A6's board line.

- **Verifier instructions 3d and 3e (A5).** 3d: before citing a function as the code that implements
  something, confirm it has a live call site — GnollHack keeps superseded NetHack code, and a function
  whose only callers are commented out decides nothing (run 54 Q12, `priest_talk`). 3e: a wiki page
  alone does not settle a claim about a number while the source is searchable; the code that applies
  it decides, and without it the verdict is Indeterminate (run 54 Q11). They follow 3a–3c, and 3c is
  still emitted only on a board suite.

- **The board sits ahead of the question for every grading role, and the wire is checked (A6).**
  `BuildGradingPrompt` returns a history of the grading system prompt, **a second system message with
  the delimited board** (same heading and delimiters as before), then the per-question user message.
  The per-question body prints *"The game snapshot for this suite is given above, before this
  question."* where the board used to be. A second system message is the one form all three providers
  carry: Anthropic emits it as the second cached system block, Google as the next `systemInstruction`
  part, and OpenAI joins every history system message into `instructions` (a session segment would not
  reach an OpenAI grader at all). This covers the primary assessment, second opinion, re-grade and its
  repair turn, calibration and trial. The final synthesis passes no board; it reads the digest. The
  verifier keeps its one-sentence system prompt and single user message, reordered to instructions,
  board, question, rubric, evidence, harness context, tool-call leads, claims. Instructions and board
  are therefore a byte-identical prefix across a run's verifier calls. The Anthropic assessor's cache
  should cover preamble plus board on repeated calls; read the real cache telemetry before claiming a
  saving.

  `BenchmarkGradingRequestProbe` runs before every grading call. It builds the body through the real
  provider's `PrepareMessageHistory` and `BuildChatRequestBody`, serialises it and makes no network
  call. It checks 120-character needles, JSON-encoded the way the body is, for the instructions, the
  head of the board block (required **exactly once**), the head of the board text, and the question
  marker. They must appear in that order, read the way the model reads the request: system text first,
  then conversation, since the OpenAI body serialises `input` before `instructions`. The board text
  itself is not required once, because BOARD FACTS quote board lines. A failure sends nothing. It
  fails a primary assessment, sets the second opinion's or verifier's error, and leaves a re-grade or
  calibration without a verdict. **`AssessorBoardChars`, `SecondOpinionBoardChars` and
  `VerifierBoardChars` are stamped only after that call's probe passed**: `BenchmarkBoardGuard.BoardCharsSent`
  reads the loaded entity, so without this the *Board delivered* line would stay green with no board
  on the wire, which is the harness-29 failure.

- **Report § 2 order (A7).** The Critical Errors line and the four sensitivity lines (Contested-Verdict,
  Evidence-informed, Verification-cleared Accuracy, FORM-cleared Readability) sit directly after the
  Intelligence Index block, before the Speed Index or Median Model Time heading, as § 7 already
  prints them. In § 2 the Verification-cleared line refers to *"answer(s) listed under Assessor
  Findings"*, which follow it; § 7 keeps *"answer(s) above"*, where they precede it.

- **Completion signals in a background tab (no version effect).** A run watched from another tab could
  chime only when the operator returned. The likely cause, not reproduced: the one `HTMLAudioElement`
  was created lazily on the first `play()`, at completion, in a hidden tab, and Chromium defers loading
  such an element until the tab is shown. The run diagnostics now record enough to confirm or refute
  this on the first real run.
  - **Arming on Start.** `BenchmarkCompletionSoundService.arm()` is called synchronously from the click
    handlers that start or resume watched work: Start (which also covers series launch), Resume series,
    both failed-question re-run entry points, *Test sound*, and the series dialog's *Continue series*.
    Before its first `await` it creates an `AudioContext`, calls `resume()` and starts a one-sample
    silent buffer, so the gesture's activation is not spent waiting on the network. It then creates and
    loads the fallback element, and fetches and decodes the Opus chime (the M4A on a failed fetch,
    non-OK response or failed decode). Concurrent calls share one attempt; a failed arm may retry.
  - **Playback.** The path order follows visibility. In a **visible** tab the element plays first, since
    it opens a fresh output stream on every play, and the buffer is the fallback when the element is
    blocked or unsupported. In a **hidden** tab, where an element's `play()` can be deferred, a decoded
    buffer plays first through the `AudioContext` at gain 0.6 and the element is the fallback. *Test
    sound* follows the same order. On the buffer path `resume()` is raced against 1,000 ms, and a
    timeout falls through to the element. After `start(0)` the context clock must advance within 250 ms;
    if it does not, the stalled source is stopped, the context is closed and rebuilt once with the same
    decoded buffer, and a second stall stops that source and falls through to the element. A stalled
    source is always stopped before another path plays, so one completion never chimes twice.
    `'played'` means the browser accepted the playback, not that anyone heard it. A new outcome, **`'deferred'`**,
    means the element's `play()` had not settled 2 seconds into a hidden tab. The status then reads
    *"The browser held the sound until this tab was shown."* A late fulfilment still marks the key
    played, so a retry cannot chime twice.
  - **Desktop notification.** A second checkbox, *Show a desktop notification*, is independent of the
    sound: either, both or neither may be on. Permission is requested when the box is ticked, while
    the operator is at the screen. It is requested again under the Start gesture (and the other
    arming actions) only when the box is ticked and this browser has not decided yet — a setting
    restored in another browser or profile, or after site data was cleared — so the run does not end
    in a notification that silently never fires. It is never requested on page load, and never again
    once the browser has decided. A refused, dismissed or unsupported result unticks the box and says
    why in a status line under the two checkboxes; that line is always in the DOM for screen readers,
    and taken out of the layout while it is empty. A ticked box raises a notification whatever the
    focus — with the tab in front too — once per run or series, and clicking it focuses the tab.
    Whether it makes a sound is up to the browser and the operating system, so it is not a guaranteed
    audio fallback. A platform whose `Notification` constructor requires a service worker gets none;
    the application has no service worker and does not gain one for this.
  - **Hidden-tab polling** continues at the 15-second cadence when **either** signal is on; with both
    off a hidden tab pauses as before. The run and series pollers take their tick from a dedicated
    worker (`public/workers/benchmark-poll-ticker.js`, one worker per poller), whose timers are not
    subject to the main-thread throttling that slows a hidden, occluded or minimized window's
    `setInterval` to once a minute. Where a worker cannot be created — no `Worker`, a construction
    failure, an `onerror` — the poller falls back to `setInterval` and the signal can arrive up to a
    minute late. The worker is a same-origin file, which the CSP's `default-src 'self'` admits; a
    `blob:` worker would be refused.
  - **Poll errors.** A poller stops only after **5 consecutive** failed polls, and every successful poll
    resets the count: 10 seconds of outage at the visible cadence, over a minute at the hidden one, so a
    development server restart is survived and a dead server still stops the poller. The error text is
    shown from the first failure.
  - **Web Lock.** While a run or series poller is live the tab holds a Web Lock named
    `overseer-benchmark-live:run:<id>` or `…:series:<id>` (unique per operation, so two tabs on
    different runs never queue), acquired when polling starts and released when it stops — terminal
    status, poll error, cancellation, leaving the page — including a run already live when the page
    loads and a series resumed from the dialog. While a series is watched, the series owns the lock
    across all its members; the run poller that follows each member neither takes nor releases it. Chrome lists a held Web Lock among the conditions that
    exempt a tab from Energy Saver freezing, but Web Locks are a coordination API: this is **no
    guarantee** against throttling, suspension, discard or operating-system sleep, on any browser.
  - **A series resumed from the series dialog now chimes.** The dialog's *Continue series* arms the
    signals and emits `seriesResumed`, and the page restarts its series poll, which had stopped at
    *Stopped*.
  - **A cancelled run or series signals nothing.** An operator cancellation plays no chime, raises no
    notification and leaves the tab title alone: a series that ends `Cancelled`, a run that ends
    `Canceled`, a member of a cancelled series, and a run this page asked to cancel — even when the
    server returns a cancelled retry of a complete run to `Completed`. `Failed`, `Stopped` and every
    `Completed…` end still signal. Client-only; no harness bump.
  - Both choices are remembered in the run-settings blob, which is written when a run is started, so a
    changed checkbox is remembered from the next Start, exactly as the sound's has always been.
  - The run diagnostics' *Completion sound* line adds arming and armed state, the `AudioContext` state,
    the path taken and its outcome, visibility and focus at capture time, the milliseconds a deferred
    play took to settle, notification support, permission and outcome, and the lock state. Beneath it an
    `attempts:` list prints the last 10 play and test attempts, each with its time, key, visibility and
    focus **at the attempt**, path, context state before and after, whether the clock advanced, whether
    the context was rebuilt, and the outcome; a `context states:` line lists the context's recorded
    state changes. The *Completion notification* line names the last notification error, if any, and
    is followed by the last 10 notification attempts (time, key, visibility, focus, outcome: `shown`,
    `duplicate`, `not-granted`, `unsupported` or `error`). The
    *Run poll* line names the ticker mode (`worker` or `timer`), and the *Last poll error* line the count
    of consecutive failed polls.
  - No CSP change: the fetch is same-origin (`connect-src 'self'`), and `Permissions-Policy` restricts
    neither autoplay nor notifications. References: [Web Audio best practices](https://developer.mozilla.org/en-US/docs/Web/API/Web_Audio_API/Best_practices),
    [Notification.requestPermission](https://developer.mozilla.org/docs/Web/API/Notification/requestPermission_static),
    [Web Locks API](https://developer.mozilla.org/en-US/docs/Web/API/Web_Locks_API),
    [Chrome Energy Saver freezing](https://developer.chrome.com/blog/freezing-on-energy-saver).

### Harness Version 33 & Scoring Method Version 12 Updates

Prompted by the analysis of run 55 (Gemini 3.7 Flash @ `medium`, snapshot suite 8, harness 32, method
11). Three problems with the instrument explained most of that run's shortfall:

- The claim verifier's verdicts on the assessor's own sentences were booked against the candidate.
- Accused quotes were checked as bare fragments.
- The assessor's own-knowledge Accuracy deductions were wrong on 6 of 8 answers, and the docked level
  was what scored.

The run also exposed four record-keeping gaps. A re-run erased what it repaired. A re-run overwrote
the delivery-probe stamp. A replaced board kept stale metadata. And missing board quotes were counted
but never named. `HarnessVersion` moves to **"33"** and `ScoringMethodVersion` to **12**. No tool guide
moves, so `ToolGuidesSha256` and `CandidateSystemPromptSha256` do not move because of this round. One
EF Core migration, `AddBenchmarkRerunAndBoardProvenance`, adds seven nullable columns:

- `BenchmarkRuns.RerunCandidateDeliveryVerifiedAtUtc` and `GameSnapshotFormatVersionUsed`;
- `BenchmarkRunAnswers.RerunAtUtc`, `RerunOfStatus` and `RerunOfErrorMessage` (512);
- `BenchmarkGameSnapshots.SnapshotFormatVersion` and `BoardHeaderTimestamp` (32).

**Method 12 is a Fundamental break on every suite, not only on suite 8.** No method-12 Accuracy,
quality score or index is comparable with a method-11 one, and scores will generally rise across the
boundary, which is not evidence of anything about a candidate. `HarnessVersion` and
`ScoringMethodVersion` are both comparability keys, so the comparison view already refuses to rank
across the boundary. The H0 guard from harness 31 still refuses re-assess, re-grade and
evidence-informed re-grade on a method-11 run. **Rescore** stays allowed and never stamps 12 onto
levels it did not grade (`LastMethodRescoreCanApply` stays 10).

- **Own-knowledge deductions no longer lower Accuracy (scoring method 12).** The ACCURACY anchors and
  § 6 of the assessor preamble now say that **ACCURACY is graded against the rubric and the GAME BOARD
  only.** A statement the assessor believes false from its own knowledge, which neither settles, does
  not lower the level. The assessor reports it instead as an `unverifiedClaims` entry:
  `Suspected false: <the sentence, verbatim from the answer> — <reason>`. The sentence *"If a
  deduction does not come from the rubric, the evidence sentence MUST begin with `Not in rubric:`"* is
  replaced by *"A deduction that does not come from the rubric or the GAME BOARD is not made."*
  - **Parsing.** `BenchmarkSuspectedFalseClaim` recognises the prefix and splits the entry into the
    answer's sentence and the reason. The split takes the longest head that occurs in the answer, so a
    dash inside the sentence is safe. `BenchmarkAssessmentParser` checks the sentence, not the whole
    entry, against the answer, and stores the entry verbatim in `UnverifiedClaimsJson`.
  - **Verification.** The verifier receives the sentence alone, as an ordinary `unverifiedClaim`, and
    the verification item carries `suspectedFalse: true`, `suspicion` and `recordedClaim` (the stored
    entry). A refutation raises `RefutedClaim` and the second opinion, as for any claim.
  - **Report.** It counts these apart, per answer and in Assessor Findings: *"Suspected false by the
    assessor: N — refuted a (the verifier sided with the assessor), supported b, indeterminate c"*
    (harness 36 wording).
  - **The old marker.** `OutOfRubricAccuracyMarker` still parses, so stored method-11 evidence renders
    unchanged. A method-12 assessor that writes it anyway beside a sub-6 level still raises
    `OutOfRubricAccuracyDeduction`. Under method 12 that flag means *the instruction was not followed*,
    and `BenchmarkVerdictConsistency.OutOfRubricAccuracyDeductionDescription` words it by method
    version.
  - **The trade.** A true out-of-rubric error no longer costs Accuracy until the verifier refutes it.
    On run 55 that gave up 1 correct deduction against 6 wrong ones removed.

- **The assessor's own sentences are checked under their own role (B1).** `ExtractDisputedClaims`
  now returns two lists: `AnswerClaims` and `AssessorStatements`.
  - **Answer claims** are the critical-error quote and up to four digit-bearing answer sentences,
    split on sentence ends only (not on a bare newline). Each needs at least four words and one run of
    three letters, which drops fragments such as `To use it:\n1.`. Only these are persisted to
    `UnverifiedClaimsJson`.
  - **Assessor statements** are sentences of the accuracy evidence, or the review comment as a last
    resort. Two kinds of sentence are skipped: one that only reports what the answer says and quotes
    it (`^(The answer|It|The response) (states|says|claims|asserts|gives|lists|tells)`), which the
    accused-quote path already covers; and a `Suspected false:` entry, which is an answer sentence.
  - **The role.** Assessor statements are submitted with the new role `assessorStatement`.
    `IsOrdinaryClaim` excludes them, so they reach no claim count, no `RefutedClaim`, no *Refuted
    Claims* entry, no second-opinion trigger and nothing in the second reader's context.
  - **Flags.** A Refuted assessor statement with a citation raises `ContestedAccuracyDeduction`, its
    third cause. It is also an evidence-informed re-grade target and a synthesis contested basis.
  - **Verifier.** It gets an *ASSESSOR STATEMENT ADJUDICATION* paragraph and a per-claim line *"Stated
    by the first assessor (not part of the answer)."*
  - **Report.** It prints *"Assessor statements checked: N — supported a, refuted b, indeterminate
    c"* per answer and at run level, lists each refuted one as the assessor's error, and adds them to
    the Claim Verification Yield as a third population.
  - **Earlier runs.** Before harness 33 these sentences were stored as `unverifiedClaim`, and they
    cannot be told apart by text.

- **An accused quote is checked as the sentence it came from (B2).** `ExtractAccusedQuotes` widens
  each quoted span to the enclosing sentence or list item. The span ends at `.`, `!` or `?` followed
  by whitespace, or at a line break, and a leading list marker is dropped.
  - **Limits.** A widening beyond `AccusedQuoteMaxLength` (400) keeps the bare span. Spans whose
    widened ranges overlap become **one** submission, so two fragments of one sentence are checked
    once: run 55 Q17 is the fixture.
  - **The charge.** The assessor's evidence sentence that holds each quotation is attached as a
    separate line, *"Charge (the assessor's words; untrusted, not part of the claim): …"*, capped at
    400 characters.
  - **The record.** The item keeps `quotedFragments` and `charge`, and the report prints the fragments
    beside the widened sentence, so the quotation itself stays visible.

- **A cited function with no live call site cannot refute or support (B3).**
  `BenchmarkCitationLivenessCheck` runs on every verification whose citation is made only of
  `src/<file>.c:<lines>` references. For each reference it finds the enclosing function, scanning up
  from the cited line to the column-0 definition the indexer recognises. It then counts that name's
  `name(` occurrences in the GnollHack source that are not the definition, not a prototype, and not
  inside a comment or string. It reads the same directories, extensions, size limit and exclusions as
  the source indexer, from disk, and caches until HEAD moves.
  - **Effect.** When every cited function has zero live call sites, the item gains `citationNote`
    (*"cited function <name> has no live call site"*). `BenchmarkClaimVerification.EffectiveVerdict`,
    which every count and flag now reads, is then Indeterminate.
  - **The record.** The stored verdict is untouched, and the report and the grading prompts print
    both.
  - **Failure.** Any exception, or indexing still in progress, writes no note.
  - **Limits.** A function reached only through a function pointer or a macro has no `name(` call site,
    which is why the note demotes to Indeterminate and never to the opposite verdict. A wrong verdict
    that cites live code (run 55 Q5) is not caught.

- **Re-run provenance (D1, D2).**
  - **Delivery stamp.** `VerifyCandidateDeliveryBeforeRun` takes the target. The run path writes
    `CandidateDeliveryVerifiedAtUtc`, and both re-run paths write
    `RerunCandidateDeliveryVerifiedAtUtc`. The report keeps *"before the first question (…)"* and adds
    *"; re-verified before the re-run (…)"*.
  - **Earlier runs.** On a run before harness 33 whose stamp is later than its re-run's start, the
    report says the stamp is the re-run's and that the pre-run stamp was overwritten.
  - **Replaced status.** `ReExecuteSingleAnswerAsync` copies the answer's `Status` and truncated
    `ErrorMessage` into `RerunOfStatus` / `RerunOfErrorMessage` and stamps `RerunAtUtc` before it
    overwrites the row.
  - **Report.** The re-run block lists *"Q15 re-executed: was ProviderError — …"* per answer, and each
    such question gets a *Re-executed* line.
  - **Limit.** The failed attempt's tool-call rows are still deleted; the report says so.

- **Board provenance (D3).** `BenchmarkSnapshotHeaderParser` reads, from the first 12 lines only, the
  `Snapshot format: N` line, the version from `… GnollHack Version 4.3.0 (Build 20) …`, and the
  `snapshot at yyyy-MM-dd HH:mm:ss` time. Any miss is null; a board is operator-supplied text.
  - **Writes.** Board creation (`BenchmarkSnapshotImporter.BuildBoardAsync`) and text replacement
    (`UpdateSnapshotText`) set `SnapshotFormatVersion` and `BoardHeaderTimestamp`. They set
    `SourceGnollHackVersion` from the header when the caller gave none or the text was replaced.
  - **Captured time.** `CapturedAtUtc` keeps its meaning, when Overseer first received the board. The
    header time has no zone and is stored as printed.
  - **Runs.** `StampBoardProvenance` records the format on the run, parsing the text when the board
    predates the column. The manifest's *Game Snapshot* line prints *"format N"* or *"format not
    stated"* from harness 33.
  - **Viewer and export.** The snapshot viewer's metadata strip shows *Format* and *Board time*. The
    YAML export writes `suite.snapshot.snapshot_format`, which an import accepts and ignores, because
    the format is derived from the text.

- **Board quotes, named (C).**
  - **Report.** Each missing literal is listed under the manifest's *Rubric board quotes* line as
    `  - Q1: "…"`, capped at 20 with *"and N more"*.
  - **Run detail.** The notice lists them the same way, and it now appears even when no other
    integrity cause does.
  - **Question numbers.** The snapshot viewer, the question-import panel and the upload dialog printed
    `orderIndex + 1` although `OrderIndex` is stored 1-based, so they named the next question; they
    print it as stored.
  - **Start.** On a suite with a board, **Start Benchmark** first fetches
    `GET suites/{id}/board-facts-check`. When quotes are missing, a modal lists them with *"These
    rubrics quote text the board does not contain; grades on them will rest on stale facts."* and
    offers *Acknowledge & Start Run*. It is advisory: a failed check starts the run as if it were clean,
    and nothing is refused.

- **Run diagnostics (no version effect).**
  - **Download.** A download button beside the copy button saves the same text as
    `<suite>_<model>_run<id>_diagnostics.txt`.
  - **Elapsed.** The capture prints `Elapsed (run):` and `Re-run elapsed:` separately, because
    `CompletedAtUtc` stays fixed across a re-run.
  - **Scope.** It prints `Failed-question re-run covered:` once the run is terminal, and `… in progress
    over:` before.
  - **Counts.** `Verified N, second-graded M` is run-wide, followed by the re-run-scoped pair.
  - **Board and re-runs.** It prints the board format, the re-verification stamp, and each re-executed
    answer's replaced status.

### Harness Version 34 Updates

Prompted by the analysis of run 56 (Gemini 3.7 Flash @ `medium`, snapshot suite 8, harness 33, method
12). It found one defect in the chat system's cost accounting, one expensive tool-use habit that a tool
result can correct, and four defects in the grading instrument. `HarnessVersion` moves to **"34"**;
`ScoringMethodVersion` stays **12**. No tool guide changes, so `ToolGuidesSha256` and
`CandidateSystemPromptSha256` do not move. One EF Core migration, `AddBenchmarkAnswerReasoningTokens`,
adds the nullable column `BenchmarkRunAnswers.ReasoningTokens`.

- **Gemini thinking tokens are output tokens (C1).** Google's `usageMetadata.candidatesTokenCount`
  excludes `thoughtsTokenCount`, and Google bills both at the output rate. `GoogleProvider` now reports
  `OutputTokens` = candidates + thoughts and `ReasoningTokens` = thoughts, which is the contract
  `TokenUsageReport` states and `OpenAiResponsesProvider` already met (`output_tokens` includes
  reasoning).
  - **Comparability.** Gemini output-token and cost figures before harness 34, in chat and in the
    benchmark alike, **exclude thinking tokens** and are not comparable on cost with later ones. Stored
    figures are not rewritten.
  - **Context occupancy.** The chat's context-occupancy figure reads the last call's output tokens, so
    for Gemini it now over-states by that call's thoughts, as it already did for OpenAI.
  - **Record.** Each candidate answer stores `ReasoningTokens` (null on answers recorded before the
    migration). The report adds `, Reasoning=N` to each question's *Tokens* line and a
    *Of Which Reasoning Tokens* line under *Total Output Tokens*, with *"(not recorded on k
    answer(s))"* when some are null.

- **A definition pointer in source tool results (T1).** `source_code_search` (on a hit, not with
  `filenames_only`) and `source_code_view` append one line when the result shows a column-0 C function
  definition in a `.c` file — a `>>>`-marked line for the search, one of the first three lines of a
  view that does not end in a column-0 `}`:
  *"[Definition: learn() at src/spell.c:398. get_function_definition {"name": "learn"} returns the
  whole body in one call; paging it with source_code_view costs one model round per page.]"*
  - **Shape.** At most two names and 300 characters; a `nethack` result names the `repository`
    argument. A prototype (`;`), a data-table row (`,`) and a macro are no definition.
  - **Budget.** `source_code_view` holds 308 characters back from its whole-line budget and puts the
    pointer ahead of its truncation notice. A search result that would push the pointer past
    `ToolExecutor`'s cap carries it first instead of last.
  - **Scope.** The pointer reaches chat and benchmark alike. It is tool output, not a tool guide, so
    `ToolGuidesSha256` does not fingerprint it. Run 56's Q16 read `learn()` in `src/spell.c` with
    repeated `source_code_view` pages; that is what the pointer is for.

- **Citation liveness, two blind spots closed (H1).** `BenchmarkCitationLivenessCheck` accepts a
  column-0 definition line only when its parameter list closes within 40 lines and is followed by `{`,
  directly or after K&R parameter declarations; a macro row in a data table (`SCROLL("mail", …),`) is
  no function, and the scan walks on upward. A **value reference** — the whole token `name` not
  followed by `(`, outside comments and strings, on a line that is not a `#define` of it — now counts as
  live, so a function reached through a function pointer (`set_occupation(learn, "studying")`) gets no
  note. The note still only ever demotes to Indeterminate, and it now errs toward silence: a dead
  function whose name is also a variable gets none.

- **The approval scan reaches the enclosing parenthesis (H2).** `IsApprovedInEvidence` used to stop at
  the first clause boundary even when that boundary lay inside a parenthetical enclosing the span, so
  *"Core claims match the rubric (items lie on the floor, not buried; … the exact '…' message; …)"*
  accused the approved quote. When the boundary lies inside such a parenthetical the scan reads, in
  order and the first with a marker deciding: the span's own clause; each further enclosing
  parenthetical outwards; then the clause holding the outermost opener, without the parenthetical.
  The innermost parenthetical is not read whole, since its other clauses judge other statements. A
  charge marker in the span's own clause still wins.

- **A docked `Suspected false:` sentence is flagged (H3).** Under method 12 an own-knowledge suspicion
  goes to `unverifiedClaims` and must not lower Accuracy; run 56 Q18 did both, unflagged, because its
  quoted span (*"Flame Burst"*) was under `AccusedQuoteMinLength`.
  - **Detection.** `DocksSuspectedFalse` takes every quoted span of the accuracy evidence of at least
    4 characters that the evidence does not approve of, and reports true when one occurs in the
    sentence of a `Suspected false:` entry, at an Accuracy level below 6. It raises
    `OutOfRubricAccuracyDeduction`, so the answer goes to the second reader as a marked out-of-rubric
    deduction does.
  - **Contest.** Such a sentence that the verifier **supports** with a citation raises
    `ContestedAccuracyDeduction`. The report names the cause *"docked suspected-false sentence
    supported"*, and the synthesis lists the sentence among the supported accusations. Advisory: no
    score moves.

- **Claim-verification instructions 3f–3h (H4).** After 3e the verifier reads: **3f** facts, not
  advice — a recommendation is refuted only through a false mechanic it states, otherwise
  Indeterminate; **3g** before refuting a formula, a table or a number, check for the same quantity in
  another notation and recompute the claim's own worked example; **3h** read the function the cited one
  hands the effect to before concluding that an effect is absent. Verification counts are not
  comparable across 33 → 34.

### Harness Version 35 Updates

Prompted by the analysis of runs 57 (Gemini 3.7 Flash @ `medium`) and 59 (GPT-5.6 Luna @ `high`),
snapshot suite 8, harness 34, method 12. All tool calls of both runs succeeded and no corpus defect was
found; the round corrects two tool-layer habits, adds one policy sentence, and repairs the grading
instrument. `HarnessVersion` moves to **"35"**; `ScoringMethodVersion` stays **12**. Four tool guides
and `_policy.md` change, so `ToolGuidesSha256` and `CandidateSystemPromptSha256` both move. No EF Core
migration.

- **`wiki_search` category guidance (T1).** `wiki_search.md` now says that `Guides` holds only the
  articles about the Gnoll Overseer app, and that the game-mechanics articles — spell casting, saving
  throws, praying, skills in general, eating — sit at the wiki root, where any `category` excludes
  them, so a mechanics question omits it. Run 59 set `category: Guides` on game questions and about 20
  of its 45 `wiki_search` calls returned topically irrelevant articles.

- **A compiled-out note in the source readers (T2).** `get_function_definition` and
  `source_code_view` end a result that shows lines inside a literal `#if 0` region with
  *"[Not compiled: file lines A-B are inside #if 0 ... #endif. They show removed or disabled code, not
  what the game does; the live code is outside them.]"* (up to three ranges, at most 300 characters,
  ahead of any truncation notice; `source_code_view` holds 308 characters back from its whole-line
  budget for it, and `get_function_definition` puts the note first when its body already fills
  `ToolExecutor`'s cap).
  `source_code_search` names up to two `>>>` matches in such a region. `SourceCompiledOutNote` tracks
  nesting across `#if`/`#ifdef`/`#ifndef`, ends a region at its matching `#endif`, `#else` or `#elif`,
  scans from the top of the file, and never guesses at any other condition. Run 59 Q16 read
  `study_book`'s `#if 0` body (`src/spell.c:933-960`) as live and earned a critical error for it. The
  note is tool output and an ordinary success; the three guides gained one sentence saying what it
  means.

- **One `_policy.md` sentence (T3, prompt-visible).** After the rule against looking up what is
  already in context: the snapshot says what the hero has and where things are, not how an item, a
  monster or a mechanic works, and a fact the advice depends on that neither the snapshot nor an earlier
  result states is looked up first. Run 57 stated five such mechanics wrongly without a lookup, the
  third observation after runs 52 and 56. It reaches every chat user and may add tool calls; its
  criterion and rollback trigger are pre-declared in the round's analysis (unlooked-up false mechanics
  at most 2; median model time above 13,200 ms, or mean tool calls per question above 4.5 without the
  count improving, or Conciseness below 90 → remove the sentence).

- **Flag-detector vocabulary (H1).** `BenchmarkVerdictConsistency.DefectRegex` recognises
  *imprecise*, *understates*, *overstates*, *inaccurate* and *the rubric's point* as a stated defect,
  so *"everything adjudicable … is correct, but it understates the rubric's point"* no longer raises
  `UnevidencedDeduction`; `FalsehoodRegex` recognises *opposite*, *contrary*, *denies* and *inverts*,
  so a stated falsehood is not read as an omission (`OmissionAsAccuracy`); and the out-of-rubric
  vocabulary accepts *beyond the verified* beside *beyond the verifiable*. Each misfire had cost a
  wasted second opinion and a wrong *Verification-cleared* entry on runs 57 and 59.

- **A cited file that is not in the index (H2).** `BenchmarkCitationLivenessCheck` adds the note
  *"cited file <path> is not in the indexed source"* when a verdict cites a `src/<file>` the indexed
  GnollHack source does not hold, and counts the verdict as Indeterminate, as the liveness note does.
  A file over `MaxSourceFileSizeKB` is also not in the index, and the wording is true for both. Run
  57 Q1 cited `src/mattackm.c:40`, a file that does not exist.

- **Duplicate claims removed before verification (H2).** An unverified claim whose text, without a
  leading `Suspected false:` prefix and with whitespace trimmed and collapsed, equals an earlier one's
  (ordinal, ignoring case) is verified once; the prefixed form is kept, since its role carries more.
  The report's unverified-claim count uses the same list, so it agrees with the *Claim Verification
  Yield* line. Run 57 Q9 listed one sentence twice.

- **Report wording (H3, H4).** The out-of-rubric line says *"which the scoring method does not permit
  as an accuracy deduction"* instead of naming method v7. The accused-sentence summary reads
  *"refuted n (the verifier sided with the assessor), supported m (the verifier sided with the
  answer), indeterminate k"* (harness 36 wording; see below). The
  *Claim Verification Yield* line follows the *Synthesis* cost line, so the five role cost lines are
  contiguous. When the second reader disputed a critical error, *Contested critical errors* adds that it
  counts quotes the claim verifier supported, while second-reader disputes are counted on the *Critical
  Errors* line. A `ContestedCriticalError` is described as supported *"as a standalone sentence; the
  error may lie in its context, so read the verdict's basis before treating the critical error as
  overturned"* — run 59 Q11's quoted sentence was true in isolation and the error was its context. The
  client's run diagnostics print *"Answers with verified claims: n"* instead of *"Verified n"*.

- **The synthesis gets its counts as data (H5).** `BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt`
  receives, per dimension, the number of answers at each level and, per answer, the verifier's outcome
  counts, and is told that a refuted claim and a critical error contested by the second reader or the
  verifier are advisory and reported as such, and that every count it states comes from that data.
  Run 57's synthesis repeated two wrong verdicts as defects and run 59's wrote *"Two questions (Q4, Q8,
  Q10, Q14, Q16)"*. The synthesis feeds no score.

- **Client-only, no version effect.** The completion sound and desktop notification were made robust
  in a visible, hidden, unfocused or minimized tab — see *Completion signals in a background tab*
  under Harness Version 32.

- **Comparability.** With `HarnessVersion`, `ToolGuidesSha256` and `CandidateSystemPromptSha256` all
  moving, and the round's rubric repair imported, the next suite-8 run is NotComparable with runs 57
  and 59.

### Harness Version 36 Updates

Prompted by the analysis of runs 60 (Gemini 3.7 Flash @ `medium`) and 61 (GPT-5.6 Luna @ `high`),
snapshot suite 8, harness 35, method 12, both graded with Gemini 3.8 Flash @ `medium` as second reader
and claim verifier. All 115 tool calls of the two runs succeeded. The claim verifier gave seven wrong
verdicts, four of them resting on a citation that could not have been evidence, and `wiki_search`
stayed silent when a `category` hid the article the model needed. `HarnessVersion` moves to
**"36"**; `ScoringMethodVersion` stays **12**. One tool guide changes, so `ToolGuidesSha256` moves;
`CandidateSystemPromptSha256` does not. No EF Core migration.

- **Two more citation notes (H1).** `BenchmarkCitationLivenessCheck` adds, beside the liveness note
  and the not-in-the-index note:
  - *"cited file <path> without a line"* when a verdict's only source citation names a `src/` or
    `include/` `.c` or `.h` file with no line, and nothing else in the citation names a wiki page
    (`wiki:`) or a board line (`board:`). A NetHack source path (`nethack/…`) is exempt, as for the
    other notes.
  - *"cited line <path>:<line> is only the definition line of <function>"* when a single cited line
    (not a range) in an indexed file is itself a function's definition line — it matches the
    definition patterns, names no keyword, and is followed by a body. A range is never noted this
    way, even one that starts on the definition line, and a citation with any reference inside a live
    function carries no note.

  A note reads as Indeterminate for every flag and count, exactly as the liveness note does
  (`BenchmarkCitationLivenessCheck.EffectiveVerdict`); the verdict is never turned into its opposite.
  A right verdict with a bad citation therefore loses its count — the accepted cost. The note is
  written when a verdict is stored and never recomputed, so **stored runs are not re-annotated**.
  Runs 60 and 61 had three verdicts citing a bare `src/shk.c` or `src/spell.c` and one citing
  `src/spell.c:5186`, the definition line of `percent_success`; one of them, a wrong *Supported*,
  drove a validated evidence-informed re-grade (run 60 Q7).

- **Instruction 5 says what a citation is (H1).** With a board it reads:

  > 5. CITATION REQUIREMENT: Every Supported or Refuted verdict MUST include a citation: a source file
  > with the line or line range that shows it, such as 'src/weapon.c:450' or 'src/spell.c:640-646'; a
  > wiki page title such as 'wiki:Yeenoghu'; or a game board line such as 'board: "a - a blessed +1
  > quarterstaff (weapon in hands)"'. A source file without a line is not a citation, and neither is
  > the line on which a function merely begins: cite the lines inside it that decide the claim. A
  > verdict without a citation, or with only such a citation, is read by the harness as Indeterminate.

  Without a board, the same text without the board clause. The old example of a bare file name
  (`'src/mon.c'`) is gone.

- **Instruction 3i, absence needs more than one place (H1).** After 3h:

  > 3i. Absence needs more than one place. Before you refute a claim that something has an effect, or
  > support a claim that it has none, search for every place the item, monster or function is handled:
  > an effect is often applied in a function that runs earlier or later than the one you found first —
  > a pre-effect and a post-effect, a caller, a shared check at the top of the attack routine. One
  > function that lacks the effect does not show that the effect is absent; if you cannot rule the
  > other places out, the verdict is Indeterminate.

  Run 61 Q11 refuted a lizard corpse's 13 turns of resistance from the post-effect, and magic
  cancellation against a cockatrice's touch from one function of the attack path.

- **Report wording (H3).** Both *Suspected false by the assessor* lines — per answer and in Assessor
  Findings — read *"refuted n (the verifier sided with the assessor), supported m (the verifier sided
  with the answer), indeterminate k"*. The earlier *"(the assessor was right)"* stated a verifier
  verdict as fact; run 61 Q11 printed it over two wrong refutations.

- **`wiki_search` names what a category hid (T1, chat and benchmark alike).** When `category` is set,
  the result is not empty, and the single best match for the query without the category lies outside
  it and is not among the results, the result carries

  > [Without category, the best match for this query is <path>, which is outside '<category>'. Omit
  > category to see it.]

  with the path cut at 120 characters, the category at 40 and the line at 240. It sits after the
  snippets, separated by a blank line and ahead of the *Showing N of M* line, unless the result is
  within 400 characters of its 13,000-character cap, in which case it goes first. Any failure of the
  probe yields no line. The empty-result miss payload (`BuildMissContent`) is unchanged; it already
  named the unfiltered match. `wiki_search.md` gains one sentence describing the line, which moves
  `ToolGuidesSha256`; per-tool guides are not inlined into the system prompt, so
  `CandidateSystemPromptSha256` stays. Run 61 set a category on about 20 of 37 searches, and five of
  them excluded the root-level mechanics article the question needed.

- **What does not move.** `ScoringMethodVersion` (12), `CandidateSystemPromptSha256`, the chat system
  prompt and `_policy.md`, and every `BenchmarkAnswerFlags` member. The grader roster is the user's
  choice: Gemini 3.8 Flash @ `medium` as second reader and claim verifier, because it is a different
  model from the candidates.

- **Comparability.** `HarnessVersion` and `ToolGuidesSha256` move, and the round's rubric repair
  (Q7, Q11, Q16, Q18) is imported, so the next suite-8 run is NotComparable with runs 60 and 61.
  Verification counts are not comparable across 35 and 36: a verdict the new notes demote was counted
  under 35.

### Harness Version 37 Updates

Prompted by the analysis of runs 62 (Gemini 3.7 Flash @ `medium`) and 63 (GPT-5.6 Luna @ `high`),
snapshot suite 8 on the format 8 board, harness 36, method 12, graded with Gemini 3.8 Flash @ `medium`
as second reader and claim verifier. All 106 tool calls of the two runs succeeded. The harness-36
*Without category* line was followed once in seven chances and its pre-declared rollback tripped, and
`wiki_search` returned three short articles as their stat blocks alone when only their titles matched.
`HarnessVersion` moves to **"37"**; `ScoringMethodVersion` stays **12**. One tool guide changes, so
`ToolGuidesSha256` moves; `CandidateSystemPromptSha256` does not. No EF Core migration.

- **A short article comes back whole whatever scored (T4, chat and benchmark alike).**
  `WikiSnippetExtractor.BuildSnippet` tests the short-article rule first: an article whose formatted
  length, header included, is at or under `PerResultChars / 2` (1,250 characters at the default) is
  returned whole, ending *— complete*, whether or not any section shares a term with the query. The
  lead-sections fallback for a query that scores no section now applies to longer articles only, and
  the ranked selection is unchanged. Run 63 Q18 searched *"garlic sulfurous ash ginseng sporal
  powder"*, got three articles of under 600 characters as their stat blocks only, and spent its next
  round on four `wiki_view` calls.

- **The *Without category* line is removed (T5, chat and benchmark alike).** A categorised non-empty
  `wiki_search` result is again the joined snippets followed by the *Showing N of M* line, as under
  harness 35; `BuildOutsideCategoryHint` is gone. The empty-result miss payload (`BuildMissContent`)
  still names the unfiltered match. `wiki_search.md` loses the sentence that described the line, which
  moves `ToolGuidesSha256` back to `f0c53b98…`, the value harness 35 recorded, so that fingerprint
  alone does not tell a 37 run from a 35 one; `HarnessVersion` does. Per-tool guides are not inlined
  into the system prompt, so `CandidateSystemPromptSha256` stays. A *[Without category …]* line in a
  result therefore dates a run to harness 36 exactly.

- **`get_artifact_stats` is Structured Lookup (H1).** `BenchmarkChatTransfer.ClassifyTool` puts it
  beside `get_monster_stats` and `get_item_stats`, so no tool in `Benchmark:AllowedTools` falls into
  *Other*. Runs 62 and 63 counted four such calls as *Other*; re-rendering their reports moves them.

- **Synthesis Divergence reads a denial (H2).** `QuestionsNamedWithFabrication` counts a sentence only
  when it holds a fabrication word that is not denied: a match is denied when the text before it in
  the same sentence ends with *rather than*, *instead of*, *not*, *never*, *no* or *without*, followed
  by at most two words and no punctuation. *"supported rather than invented"* and *"not an invented
  item"* name no question; *"inventing an automatic regeneration"* does. `FabricationRegex` and
  `MentionsFabrication`, which other detectors use, are unchanged. Run 63 listed Q13 and Q18 from a
  sentence that praised them.

- **Every second-opinion trigger prints in words (H3).** The report's `TriggerLabel` gains
  *out-of-rubric accuracy deduction* and *dimension outlier*; the client's
  `secondOpinionTriggerLabel` gains those two and *sample top-up*.

- **Instruction 3j, passed values are settled where they are assigned (H4).** After 3i:

  > 3j. Values passed are settled where they are assigned. When the code you cite only passes
  > variables on (for example a `case` block that calls a function with `duration` or `cures_sick`),
  > read where those variables are computed, and the object's data entry they come from, before you
  > refute a number, a die roll or a cure.

  Run 63 Q6 refuted a potion's 12d6 and its cures from the `case POT_HEALING:` block, which only
  passes values computed at the top of the function.

- **Verifier spend and a failed verification's raw text in the report (H5, H6).** Each answer's
  *Claim Verification* line ends with *"— N tool call(s), X input tokens, Y s"* from
  `ClaimVerificationToolCallCount`, `ClaimVerificationInputTokens` and `ClaimVerificationDurationMs`,
  naming only the columns present. *Harness Cost* adds *Verifier spend by answer*: the three answers
  with the most verifier input tokens, the first with its count, and the mean input tokens and tool
  calls per verified answer; it prints under *Claim Verification Yield* when the verifier is priced,
  and after the cost block otherwise. A failed verification's line adds the first 600 characters of
  `ClaimVerificationRawText`, on one line and with backticks replaced by apostrophes. All of it is
  computed at render time from stored columns, so a re-rendered report of an earlier run shows it too.

- **What does not move.** `ScoringMethodVersion` (12), `CandidateSystemPromptSha256`, the chat system
  prompt and `_policy.md`, and every `BenchmarkAnswerFlags` member.

- **Comparability.** `HarnessVersion` and `ToolGuidesSha256` move, and the round's rubric repair (Q1,
  Q5, Q6, Q11, Q16, Q17) is imported, so the next suite-8 run is NotComparable with runs 62 and 63.
  `wiki_search` result sizes and `wiki_view` counts are not comparable across 36 and 37 on questions
  whose searches return short articles.

### Harness Version 38 Updates

Prompted by the analysis of runs 64 (GPT-6 Luna @ `high`) and 65 (GPT-5.6 Luna @ `high`), snapshot
suite 8 on the format 8 board, harness 37, method 12, graded with Claude 5 Opus @ `medium` and Gemini
3.8 Flash @ `medium` as second reader and claim verifier. All 103 tool calls of the two runs
succeeded. The claim verifier returned Supported on three items for a true clause beside the one the
assessor charged, and two of the four validated evidence-informed re-grades then withdrew true
charges; two true facts docked to Accuracy 5 reached no check; and the synthesis summed the claim
counts itself and got them wrong. `HarnessVersion` moves to **"38"**; `ScoringMethodVersion` stays
**12**. One tool guide changes, so `ToolGuidesSha256` moves from `cc4cf83a…`;
`CandidateSystemPromptSha256` does not. No EF Core migration.

- **The claim verifier judges the charged part (H1).** An accused item whose quoted fragments are
  shorter than its sentence prints, after its *Charge* line:

  ```text
  Charged part (the words the assessor quoted, not part of the claim): "<part>"
  ```

  with several parts joined by `"; "`. `BenchmarkService` passes each submission's existing
  `QuotedFragments` as `claimChargedParts`; no column, flag or verdict field is added. The accused
  preamble now says to *judge the charged part*: Supported means the charged part is true as the
  answer states it, Refuted that it is false, and a true clause elsewhere in the sentence does not
  make a false charged part Supported. The critical-error block adds that the assessor's evidence
  names the part of the claim it holds false, and that a true clause elsewhere does not make the
  verdict Supported. After 3j:

  > 3k. When a claim joins several statements, a verdict about it is a verdict about the statement at
  > issue: the charged part of an accused sentence, or the part the assessor's evidence names for a
  > critical-error quote. Say in your basis which statement you checked.

  Run 65 Q1 was Supported from `src/mon.c:4935-4953`, the general peaceful-anger code, while the charge
  was the dwarf rule the sentence stated; run 65 Q8 was Supported from the board's *Controlled
  teleport* line while the hero carried a wand of digging; run 64 Q11's critical quote was Supported
  on the read-ability clause beside the charged "keep a casting ready". The first two became wrong
  evidence-informed withdrawals.

- **Accused sentences are checked at Accuracy 5 (H2).** `AccusedQuoteEligibleMaxAccuracyLevel` is 5,
  so an answer at Accuracy 5 or below, or with a contested verdict, submits the sentences its accuracy
  evidence quotes. Run 64 Q7 was docked for "while you're not confused" and run 65 Q10 for "can
  identify two items", both true (`src/read.c:2762-2786`), and neither reached the verifier. The cost
  is about four to six further checked items per run, US$0.15–0.30 at US$0.04–0.05 an item; the
  rollback is the constant back to 4 above US$0.06 an item or a run above US$3.80.

- **The synthesis gets harness-counted claim totals (H3).** Directly after the LEVEL DISTRIBUTION
  block, when any answer has a verification count:

  ```text
  --- CLAIM VERIFICATION TOTALS (counted by the harness; copy these figures rather than adding up the per-question lines) ---
  Unverified claims: <U> across <A> answer(s); verified: <S> supported, <R> refuted, <I> indeterminate
  --- END CLAIM VERIFICATION TOTALS ---
  ```

  It sums the per-answer `UnverifiedClaimCount`, `ClaimsSupportedCount`, `ClaimsRefutedCount` and
  `ClaimsIndeterminateCount` (null as 0), the same columns the report's *Assessor Findings* line sums,
  and `<A>` counts the answers with at least one unverified claim. Instruction 6 already required every
  count to be copied from a data block; until now no block carried these. Run 64's synthesis wrote
  "14 checked, 13 supported" against the report's 16 and 15.

- **`item_lookup` finds artifacts (T5, a tool change that live chat shares).** `WikiService` gains
  `GetLookupContextInCategories` and `GetRelevantContextInCategories`, whose category clause is a
  `BooleanQuery` of `SHOULD` `pathlower` wildcards, one per category; the single-category methods
  delegate to them. `ItemLookupTool` searches `item` **and** `artifact`, so `Artifacts/The Holy
  Grail.md` is a category hit and its exact title wins. The miss payload now says *"Both the 'item' and
  'artifact' path filters and an unfiltered search of the whole wiki missed"*; its opening is
  unchanged, so `BenchmarkToolResultClassifier` still reads it. `item_lookup.md` says artifacts are
  covered, which moves `ToolGuidesSha256`. `monster_lookup` is unchanged. Three calls in run 65 asked
  for the Grail and got *Grail of healing*, *Holy symbol* and holy water.

- **A minified `get_item_stats` payload keeps what the model needs (T6, a tool change that live chat
  shares).** When a result exceeds `TruncationThreshold`, `GetItemStatsTool.Minify` keeps `Stats`,
  `RawDefinition` and `Error`; its `Message` opens with the unchanged *"Response truncated due to size
  limits."* and, when the Level-2 fallback named why Level 1 failed, appends the original text from
  *"Structured values were not available:"* onwards; and it keeps the definition of the macro the raw
  definition invokes (the identifier before its first `(`, such as `SPELL`) when the result still fits
  the threshold with it. Run 64 Q11 received 572 characters: a positional `SPELL(...)` call with no
  legend and no reason.

- **What does not move.** `ScoringMethodVersion` (12), `CandidateSystemPromptSha256`, the chat system
  prompt and `_policy.md`, and every `BenchmarkAnswerFlags` member. The verifier and the synthesis feed
  no score.

- **Comparability.** `HarnessVersion` and `ToolGuidesSha256` move, and the round's rubric repair (Q6,
  Q7, Q10) is imported, so the next suite-8 run is NotComparable with runs 64 and 65. `item_lookup`
  results and counts are not comparable across 37 and 38 on questions about an artifact, and
  claim-verification counts are not comparable on answers at Accuracy 5.

### Harness Version 39 Updates

Prompted by the analysis of runs 66 (GPT-6 Sol @ `medium`) and 67 (GPT-5.6 Luna @ `max`), snapshot
suite 8 on the format 8 board, harness 38, method 12, graded with Claude 5.5 Opus @ `medium` and
Gemini 3.8 Flash @ `medium` as second reader and claim verifier. All 143 tool calls of the two runs
succeeded. A game word raised a false contested verdict; the disputed-answer summaries ignored the
accused sentences; and the claim verifier produced four more wrong verdicts (instances 40–43), two
of which a validated evidence-informed re-grade then relied on. `HarnessVersion` moves to **"39"**;
`ScoringMethodVersion` stays **12**. No tool guide changes, so `ToolGuidesSha256` stays `671d4949…`;
`CandidateSystemPromptSha256` does not move. No EF Core migration.

- **A contested verdict ignores the game senses of "hallucination" (H1).**
  `BenchmarkVerdictConsistency` masks the game vocabulary that shares the fabrication stem — *potion
  of hallucination*, *hallucination gamble / risk / effect / status / resistance*, *cures / causes /
  induces / from / or / and / to hallucination*, *hallucinating hero / player / character*, *while
  hallucinating* — with spaces of equal length before matching. The contested-verdict check reads the
  assessor's **comment and accuracy evidence only**, through the denial-aware
  `MentionsUndeniedFabrication` (sentence by sentence, as the synthesis path already did); the
  completeness evidence lists omissions and is no longer read. Run 67 Q5's *"a poison or
  hallucination gamble"* in completeness evidence raised a contested verdict and a second opinion.
  `MentionsFabrication` keeps its signature for its other caller and masks the same vocabulary.

- **Disputed-answer lines count accused sentences (H2).** A *Disputed Assessments* line whose answer
  has accused sentences adds `; accused sentences: S supported, R refuted, I indeterminate` inside its
  bracket (the bracket opens `[Accused sentences: …]` when the answer has no claim counts), and the
  answer DTO carries `accusedSupportedCount`, `accusedRefutedCount` and `accusedIndeterminateCount`
  (null without a verification record), all computed at render time from `ClaimVerificationJson` by
  the report's existing `AccusedSentencesOf` / `VerdictCounts`. The Run Integrity Notice prints them
  the same way. Run 67 Q3 read "0 supported, 0 refuted, 0 indeterminate" while its two accused
  sentences came back one Supported, one Refuted.

- **A macro's definition line is not evidence (H3a).** `BenchmarkCitationLivenessCheck` notes a
  verdict whose only source evidence is a single `src/*.c` or `include/*.h` line inside a `#define`
  header — the `#define` line or one of its backslash-continued lines, through the first line that
  does not end in `\`:

  ```text
  cited line <path>:<line> is only the definition of macro <NAME>
  ```

  and counts it Indeterminate, by the same path and storage as the harness-36 function-definition
  note. A range is never noted. Run 66 Q13 was refuted citing `src/objects.c:3549`, a parameter line of
  `#define SPELL(…)`.

- **The charged part gets its own verdict (H3b).** For an accused item that prints a *Charged part*
  line, the claim-verification schema asks for `chargedPartVerdict` (`Supported` | `Refuted` |
  `Indeterminate`) and `chargedPartBasis`, and instruction 3k adds: *"For an item that names a charged
  part, chargedPartVerdict is your verdict on that part alone, with its own citation in
  chargedPartBasis; the item's verdict field is ignored for such an item."* The parser receives which
  items carried the line (`BenchmarkClaimVerificationPrompt.ChargedPartItems`, the same rule the
  prompt uses) and stores the charged-part verdict as the item's verdict, with `chargedPartBasis` as
  its citation and basis, so every existing reader and every citation note works on it; the item's
  own verdict, citation and basis are kept as `itemVerdict`, `itemCitation` and `itemBasis` for
  display, and `chargedPart` is `true`. A missing or unparseable `chargedPartVerdict` keeps the item's
  own fields and counts Indeterminate with the note *"the charged part was not judged separately"*; a
  Supported or Refuted charged-part verdict whose basis names no `src/` or `include/` file, `wiki:` or
  `board:` is demoted to Indeterminate as a missing citation. The new stored fields are omitted when
  null, so earlier stored JSON reads unchanged. Run 67 Q7 was Supported on the uncharged clause
  although harness 38 printed the charged part.

- **Instruction 3l.** *"When a GnollHack wiki page states the property a claim is about — a spell's
  casting time, an item's effect, a stat block value — and your reading of the source seems to
  contradict it, name the wiki statement in your basis and cite the code that overrides it; the
  wiki's stat blocks are printed from the same game data. Without both, the verdict is
  Indeterminate."* Run 67 Q6 was refuted from `docast` while the wiki page the candidate read said
  "Casting time: 0 rounds", generated from the same flag.

- **One cost share, one number (H4).** The cost panel's largest-remainder apportioning is the exported
  pure function `apportionWholePercentShares`, and the Model Under Test card prints the candidate's
  apportioned share among the same roles, so card and panel print the same whole percent (run 66:
  25 % on the card, 26 % in the panel, for $0.6926 of $2.7247).

- **The diagnostics capture names the verifier (H5).** The `--- MODELS ---` block gains a `Verifier:`
  line in the form of its neighbours, or `Verifier: none selected`, and prints an unset service tier
  as `default (none requested)` in that block only.

- **No *Endpoint: official* row in the run progress dialog (H6).** The Model Under Test endpoint row
  appears only for a non-official endpoint, as the three grader rows already did. The report's
  manifest keeps its `Endpoint:` line.

- **`wiki_view` names what it cuts (T2, a tool change that live chat shares).** A `wiki_view` without
  a `section` whose rendered article is longer than the result cap returns

  ```text
  [Article is N characters; the first M are shown. Headings: H1; H2; …. Call wiki_view again with section set to one of them to read the rest.]
  ```

  followed by the article's first `M` characters, where the line, its newline and the shown text
  land at exactly the cap (less the spoiler-free suffix when that mode is on); the headings are every
  heading of the whole article from `MarkdownSectionExtractor.Headings`, the list the section-miss
  marker also uses, and the line is capped at 600 characters with a trailing `…`. Such a result no
  longer carries `ToolExecutor`'s `[Truncated:` suffix, so `BenchmarkToolResultClassifier` recognises
  the line at the start of a `wiki_view` result and marks the row **`partial`** (a tool's own
  partial-result notice), not `cut`. *Sacrifice Offering* (17,840 characters) was cut blind at 10,000
  on both runs.

- **Lookup headers name the path (T1, a tool change that live chat shares).** `monster_lookup`,
  `item_lookup` and the pre-injected wiki context print `--- Items/Torch.md ---` rather than
  `--- Torch.md ---`, as `wiki_view` and `wiki_search` already did.

- **What does not move.** `ScoringMethodVersion` (12), `ToolGuidesSha256`,
  `CandidateSystemPromptSha256`, the chat system prompt and `_policy.md`, and every
  `BenchmarkAnswerFlags` member. No stored score, level or cap changes.

- **Comparability.** Only the Instrument key `HarnessVersion` moves, so a harness-39 run of an
  otherwise identical configuration is one key away from runs 66 and 67. Contested-verdict counts,
  claim-verification counts on accused items with a charged part, and citation-note counts are not
  comparable across 38 and 39; a run stamped 38 or earlier carries no charged-part verdicts, and its
  stored verdicts are unchanged. `wiki_view` result lengths are not comparable on over-cap articles.

### Aggregation Formulas:
- **Quality Score**: $\text{Quality} = A^{0.55} \cdot C^{0.25} \cdot Cn^{0.10} \cdot R^{0.10}$ (capped at 25 if `criticalError` is true).
- **Model Time**: $\text{ModelTime} = \max(0, \text{DurationMs} - \text{ToolTimeMs})$ — the turn duration with harness tool I/O removed. This, not `DurationMs`, is what speed is scored on.
- **Speed Target**: $Target(q) = T \cdot (1 + s \cdot \text{Difficulty}(q) / 100)$, where $T$ is `SpeedTargetMs` and $s$ is `SpeedDifficultyScaling`.
- **Speed Score**: $\text{Speed} = \text{clamp}(100 - k \cdot \log_2(\text{ModelTime} / Target(q)), 1, 100)$, where $k$ is `SpeedDecayK`.
- **Intelligence Index**: $\sum(\text{Difficulty}(q) \cdot \text{Quality}(q)) / \sum(\text{Difficulty}(q))$.
- **Intelligence Index Standard Error**:
  $$\text{SE} = \frac{\sqrt{\sum w_i^2 (q_i - \hat{I})^2 \cdot \frac{n}{n-1}}}{\sum w_i}$$
  with $95\%\text{ CI} = \hat{I} \pm 1.96 \cdot \text{SE}$ (where $n \ge 3$). Reflected as `QualityIndexStandardError` on `BenchmarkRun`. Represents item-sampling uncertainty; two runs whose intervals overlap cannot be distinguished with statistical confidence.
- **Unweighted Quality Mean**: the **equal-weight mean** of $\text{Quality}(q)$ over answered questions,
  stored as `UnweightedQualityIndex` from harness version 7. Not a rival to the Intelligence Index but a
  companion to it: the difference between the two is how far difficulty weighting moved the headline, and
  a run whose weak answers are its easy ones reads *higher* weighted than unweighted. Both are reported.
- **Assessor Agreement**: the mean of $|\text{first}(q) - \text{second}(q)|$ over the answers graded
  twice, stored as `SecondOpinionMeanAbsDelta` beside `SecondOpinionGradedAnswerCount`. A **disagreement**
  is a gap above **15** quality points, or a split on `criticalError`. Interpretable only together with
  its coverage: an unbiased inter-rater rate requires `SecondOpinionMode = All`.
- **Speed Index**: the **equal-weight mean** of $\text{Speed}(q)$ over answered questions. Difficulty enters through $Target(q)$, not through the weight — weighting here as well would count difficulty twice and drag the index toward the floor by construction. (This line previously claimed a difficulty-weighted mean, which neither the code nor the generated report has ever produced.)

---

## 3. Assessor Strategy

Cross-model comparison is valid only when every candidate was graded by the **same** assessor —
otherwise the models are measured with different instruments and the indices are not comparable. The
benchmark's own recorded purpose is *operational model selection*, which is a cross-family choice, so
this constraint is binding rather than academic.

Runs 43–46 (2026-09-12) are the worked example. Four runs of suite 6 at identical item revisions,
assessed difficulties, prompt options and instrument SHAs scored 70 / 68 / 72 under GPT-5.6 Sol @
`medium` and **97** under Gemini 3.7 Flash @ `high`, with the blind second reader's signed delta
flipping from about +30 on the three Sol-graded runs to −13.3 on the Gemini-graded one — a grader
effect five to ten times the spread between the candidates themselves. The comparison view's
comparability index enforced the rule automatically, putting run 46 in its own condition and
excluding it from the figures, because `AssessorConfiguration`, `SecondOpinionConfiguration` and
`ClaimVerifierConfiguration` are `Instrument` keys. From key definition version 2 each of the three
carries the grader's full recorded settings, the output cap its calls actually sent and its endpoint
(see **Model Configuration Snapshots**).

### The roster today, and why Anthropic is primary

Status as of 2026-09-26, the roster of runs 66 and 67:

| Role | Model | Since |
|---|---|---|
| **Primary assessor** (scores) | Claude 5.5 Opus @ `medium` | Run 66. Claude 5 Opus @ `medium` graded runs 52–65, and Claude 5 Opus @ `low` runs 39–42 |
| **Second reader** (advisory, blind) | Gemini 3.8 Flash @ `medium` | Runs 57 and 59; the user's deliberate roster from runs 60 and 61 on |
| **Claim verifier** (advisory) | Gemini 3.8 Flash @ `medium` | The same |
| **Candidates** | GPT-5.6 Luna, GPT-6 Luna, GPT-6 Sol, Gemini 3.7 Flash | — |

By provider:

| Provider | Role today | Earlier roles |
|---|---|---|
| **Anthropic** | Primary assessor; also other benchmark work such as suite authoring | Candidate: Claude 5 Sonnet @ `high` on runs 28–35 and 43, Claude 5 Opus @ `low` on run 44. Second opinion and claim verifier in stage 1 (runs 36–38) |
| **Google** | Candidate (Gemini 3.7 Flash); second reader and claim verifier (Gemini 3.8 Flash) | Primary assessor (Gemini 3.7 Flash) until the promotion at run 39, and again on run 46 |
| **OpenAI** | Candidate | Second opinion and claim verifier: GPT-5.6 Luna on runs 28–35 and 52–56, GPT-5.6 Sol on run 39. Primary assessor: GPT-5.6 Sol @ `medium` on runs 43–45. The GPT-5.6 Luna grader roster was retired at runs 60 and 61 and is not to be re-proposed |

**Why Anthropic scores.** The constraint that chose it still holds:

- **OpenAI and Google** are both candidate providers, so neither can be the permanent primary: same-family
  self-preference bias would land on the grader whose verdict *scores*.
- **Anthropic** has not been a candidate since run 44 (2026-09-12), so it is the only provider eligible
  for every current candidate.

**What the 2026-09-03 position got wrong.** It recorded Anthropic as *"not planned as a model under
test"*, and therefore assessor-eligible in every configuration. Anthropic was a candidate on ten runs:
runs 28–35, graded by Gemini 3.7 Flash @ `high` with GPT-5.6 Luna as second opinion and verifier, and
runs 43–44, graded by GPT-5.6 Sol @ `medium`. So **no provider is assessor-eligible for all three
candidate families.** An Anthropic candidate under today's roster trips the same-provider gate — the
launch returns 409 until the administrator acknowledges the notice, and the acknowledgment is stored in
`SameProviderAcknowledged` and disclosed in the report — while grading it with any other assessor puts
it on another grader's scale, which the comparison view keeps out of the figures and which runs 43–46
show can be larger than the spread between candidates. No decision is recorded on which of the two an
Anthropic candidate run takes; make it before launching one, not after.

### What the staged migration completed

The 2026-09-03 plan had two stages: stage 1 kept Google as the primary through the remaining
OpenAI-candidate runs, with Anthropic as a blind second reader to measure it before it scored anything;
stage 2 promoted Anthropic to primary from the first Google-candidate run and rotated the second opinion
to whichever of OpenAI and Google was not the candidate, so that every row held three distinct
providers. What happened:

1. **Stage 1 ran on runs 36–38** — GPT-5.6 Sol candidate, Gemini 3.7 Flash assessor, Claude as second
   opinion and claim verifier: the first three-provider roster of the plan.
2. **The promotion happened on schedule.** Run 39 (2026-09-12), the first Google candidate, was graded
   by Claude 5 Opus @ `low` with GPT-5.6 Sol @ `medium` as second reader and verifier. Claude 5 Opus
   stayed primary at `low` through run 42.
3. **Runs 43–46 interrupted it deliberately.** The cross-model set included Anthropic candidates, so it
   was graded by GPT-5.6 Sol (runs 43–45) and Gemini 3.7 Flash @ `high` (run 46) — the grader-effect
   example above.
4. **The rotating second opinion was followed and then replaced.** Runs 52–56 had GPT-5.6 Luna @
   `xhigh` or `high` as second reader and verifier for Gemini candidates, as stage 2 prescribed. From
   runs 57 and 59 a single fixed second reader and verifier, Gemini 3.8 Flash @ `medium`, grades every
   candidate; from runs 60 and 61 it is the user's deliberate roster, chosen because it is a different
   model from every candidate, the Google one included.
5. **The primary was strengthened twice**: Claude 5 Opus from `low` to `medium` by run 52, and Claude 5
   Opus to Claude 5.5 Opus at run 66. `AssessorConfiguration` is an `Instrument` key, so each move
   separates the runs after it from the runs before it in the comparison view.

**What the fixed second reader changes.**

- **It removes the stated cost of rotation.** While it stays unchanged, the agreement metric is
  comparable across candidate providers, not only within one.
- **On a Google candidate, the second reader and the verifier share the candidate's provider.** The
  report prints this for the verifier as *"the weakest available pairing"*. Both roles are advisory and
  nothing they produce is read by a scoring path, so the bias reaches the agreement metric and the claim
  counts, not the Intelligence Index.
- **The second reader and the verifier are one model.** The report says so too: under blind mode the
  second reader receives the verification findings, so a refuted claim and a harsh second opinion on
  the same answer are one finding, not two independent ones.

**What stage 1 bought.** Claude graded answers on this suite beside Gemini before it scored any, so the
promotion was from a model whose behavior on the suite was already measured — per answer, under the
same rubrics, with `SecondOpinionMeanAbsDelta` and the disagreement list accumulating run by run. The
method still applies to any future assessor change: measure the prospective grader before promoting it,
and use a calibration run, which previews the comparison against any stored run at the cost of one
assessor pass and no candidate calls.

**What stage 1 cost.** Gemini 3.7 Flash — the grader that produced the Q1 "unverified" deduction and the
Q10 "hallucinates 'adamantium'" verdict that harness version 7 exists to fix — kept scoring through
stage 1. The exposure was smaller than run 7's, because the prompt fix applies to whichever model grades
and the new triggers fire on exactly those two shapes, but it was not zero. It ended at run 39.

**The staging rationale was the agreement data, not continuity.** An earlier draft argued for keeping
Gemini partly to preserve comparability with the seven existing Gemini-graded runs. That argument did
not hold: the `ScoringMethodVersion` bump to 6 already separated runs 1–7 from everything after on any
answer containing an out-of-rubric claim, and the item-analysis `ScoringMethodMixed` flag says so.
Continuity with older runs is not a reason to keep a grader.

### Use the strongest grader in the slot that scores

The stage-1 rule *"do not hop to a stronger Gemini in the interim"* kept the migration at one
comparability break instead of two, and is spent. Its general half — put the strongest grader available
in the primary slot, not in an advisory one — is what the moves to Claude 5 Opus @ `medium` and to
Claude 5.5 Opus applied.

Its cost argument has not survived. The 2026-09-03 figures — assessment at roughly 2% of a run's token
cost, a few seconds per answer, fully hidden inside the pipeline — were measured with a Flash-tier
assessor and no longer hold: **grading is now most of a run's cost.** On run 66 the candidate was 25%
of US$2.7247 and the grading roles took the rest, and on runs 62–67 the claim verifier alone took 38–56%
of each run's cost. Grading has been pipelined per question since the run-31 round, but a slow grader
can still be the critical path: run 52's second opinion, GPT-5.6 Luna @ `xhigh`, took 21 m 17 s of the
run's 23 m 18 s. The advice stands; weigh it against these figures rather than the old ones.

### The second opinion is an independent reader, not an adjudicator

| Role | Model choice | Why it does or does not work |
|---|---|---|
| **Adjudicator** — meant to be *more right* | A stronger model | **Does not work as designed.** The first verdict stays authoritative for scoring, so the better model's verdict is recorded and then ignored. If you trust a model more, make it the primary assessor |
| **Independent reader** — meant to detect *fragile verdicts* and measure agreement | Comparable tier, **different provider** | **This is what the feature is for.** Disagreement means two competent, independently-biased readers reached different conclusions |

Stage 1 deliberately placed the *stronger* model in the second-opinion slot, which the table warns
against as a permanent arrangement. That was acceptable because it was temporary and its purpose was
measurement rather than adjudication — observing the prospective primary before promoting it — and it
ended with the promotion at run 39.

Today's arrangement is the other way round: a Flash-tier second reader beside an Opus-tier primary, from
a different provider on every OpenAI candidate but not on a Google one. It is not the *comparable tier*
the table describes, and it is not an adjudicator either — its verdict does not score. Read a
disagreement as a reason to open the answer, not as evidence that the primary was wrong.

The Intelligence Index comes from the primary assessor alone, so neither the rotation of stage 2 nor
its replacement affected it. Changing the second reader moves the `SecondOpinionConfiguration`
`Instrument` key and starts a new agreement population.

### Blind vs. Anchored Second Opinions

From Harness Version 11, the second opinion is **blind by default** (`SecondOpinionBlind = true`). In earlier versions (Harness 4–10), the second reader received the first assessor's score, critical error flag, and full commentary, preceded by the notice that the first verdict was "severe enough".

> **Harness 11 Migration Note & Harness 12 Backfill:** While Harness 11 added the `SecondOpinionBlind` property defaulting to `true` in code and the seeder, the original database migration set `defaultValue: false` without backfilling existing rows. Consequently, the existing default scoring profile in production remained anchored, and benchmark run 11 executed anchored. Harness 12 resolved this via migration `20260904214142_AddBenchmarkAgreementDirection`, which explicitly backfilled `SecondOpinionBlind = 1 WHERE IsDefault = 1`. Runs starting from Harness 12 truly grade blind under the default profile.

Anchored second opinions suffer from anchoring bias: models instructed to review an existing score systematically regress toward the anchor rather than evaluating independently. While anchored evaluations can be useful for human-style appeals or error reviews, they do not produce an authentic inter-rater agreement metric. Under blind mode, the second assessor receives only the question, rubric, answer, and any objective claim verification findings, ensuring `SecondOpinionMeanAbsDelta` and `SecondOpinionMeanSignedDelta` represent true inter-rater variation and direction. Agreement statistics between blind and anchored runs are **not comparable**.

### Grade everything twice

Best practice for rated evaluation — in ML evaluation and in the psychometrics it borrows from — is **two
independent raters over the whole set, with inter-rater agreement reported**. Selective re-grading is the
compromise for when that is unaffordable, and it is not unaffordable here.

Full double grading buys three things selective re-grading cannot:

- **An unbiased agreement rate.** Under selective re-grading the disagreement rate is conditioned on the
  first grader's own uncertainty, so it measures nothing about the instrument. Stage 1's value rested on
  this, and every agreement figure since does too.
- **Symmetric coverage of the failure mode that matters most.** A first grader that is *confidently
  wrong* produces no trigger at all — no critical error, no fabrication vocabulary, no low score. That
  answer is invisible to every trigger, and it is exactly the one a second reader catches.
- **Less machinery.** No median, so no post-scoring pass, no third execution stage, no cap.

**Recommendation: `SecondOpinionMode = All`**, with `Flagged` and `FlaggedAndOutliers` held in reserve for
a large suite where assessor cost becomes binding.

### Two gaps the gating does not close

1. **Nothing checks whether the assessor and the second opinion share a provider.** The same-provider
   gate covers *candidate versus assessor* only. The start dialog carries an advisory and the report a
   disclosure. **Advisory, not a block.**
2. **A suite's assessor changing between runs is advisory too.** The start dialog warns when the selected
   assessor differs from the most recent completed run of the same suite. That is meant for exactly such
   a moment — the stage-2 promotion at run 39, or a change of assessor model such as the one at run 66.

### Keep suite authoring separate from grading

Anthropic is used for other benchmark work, including suite authoring, as well as being the primary
assessor. Keep the authoring configuration distinct from the grading one: a model that wrote a rubric is
not a neutral reader of answers against it, and the two roles drifting onto one System AI Configuration
would make that impossible to see.

---

## 4. Suite Health and Item Analysis

A benchmark measures models, and after a while it also needs measuring. This section is about the
second thing: which items no longer discriminate, which carry a difficulty weight that does not
match how they behave, whether a rubric's own citations still resolve, and where the suite is
silent about the game.

**Every finding here is read-only.** The Suite Health panel's only outward action is "open this
question for editing", and there is deliberately no endpoint behind any of these reports that
writes a question, a rubric, or a difficulty rating.

### Stable item identity, and the reorder bug it fixes

Before harness version 7, a stored answer was tied to its question by `OrderIndex` alone.
`ReorderQuestions` rewrites `BenchmarkQuestion.OrderIndex` and touches no stored answer, so after
any reorder every earlier run displayed its answers **against the wrong questions** — silently,
with no error and no flag. That was a correctness bug in the existing screens, independent of any
analysis built on top.

- **`BenchmarkRunAnswer.BenchmarkQuestionId`** (nullable FK, `DeleteBehavior.SetNull`) is the
  stable link. `SetNull` rather than `Cascade` because a run is a historical record: deleting a
  question from a suite is suite maintenance, not history revision, and a null FK renders as
  "question deleted".
- **`BenchmarkQuestion.ItemRevision`** (int, default 1) is bumped at exactly the point that
  already clears the difficulty snapshot — a change to the question text, its band, or its rubric.
  An edited question is a **different item**, and its statistics must not straddle the rewrite.
- **`BenchmarkRunAnswer.ItemRevisionUsed`** records the revision an answer was produced against.

The `AddBenchmarkQuestionIdentity` migration backfills the link where it is unambiguous: the
answer's run belongs to the suite, and the suite holds exactly one question at that order index
with that exact text. Everything else is left null, and **everything downstream excludes an
unlinked answer rather than guessing** — a wrong link would corrupt every figure built on it, and
unlike a missing one, invisibly. `ItemRevisionUsed` is deliberately *not* backfilled: a historical
answer was produced against whatever the question said at the time, which is unknowable from the
migration. Null means "unknown revision", is reported per item as `UnknownRevisionCount`, and is
included in the statistics — dropping it would empty the table for every suite that already has
runs, and assuming it matches the current revision would be a claim the data does not support.

Anything that merges questions with answers now prefers the FK and falls back to the order index
only where there is none.

**Suite Health reads the live questions; everything about runs reads the runs** (2026-09-24). Suite
Health's subject is today's question, so it keeps passing the live suite. Group statistics, Multi-Run
analysis and Model Comparison compute over `BenchmarkRunExam.Build(runs)`: one question per
`BenchmarkItemAnalysis.QuestionKey` (`BenchmarkQuestionIdUsed ?? BenchmarkQuestionId`), with the text,
order, rubric, revision and weight the runs recorded. A question deleted since stays in that exam
through `BenchmarkQuestionIdUsed`; one added since is not part of it. The run progress dialog lists a
run's own answers under their stored order index and text. Re-grading reads each answer's recorded
rubric and the run's recorded board. See § *Runs Are Independent of Later Suite Changes*.

### Item statistics

`BenchmarkItemAnalysis` — pure computation over stored runs, no AI calls, no writes.

| Statistic | Definition |
|---|---|
| `RunCount`, `DistinctModelCount`, `DistinctAssessorCount`, `DistinctScoringMethodVersionCount` | Sample size and its confounds. All four are shown everywhere the statistics are, because a mean over three runs by one model says something quite different from the same mean over twelve runs by four models, and neither is visible from the mean |
| `MeanQuality`, `MinQuality`, `MaxQuality`, `StdDev` | Over `Ok`, scored answers |
| `EmpiricalDifficulty` | `100 − MeanQuality` |
| `AssessedDifficulty`, `DifficultyDelta` | The a priori rating and its gap from the empirical one |
| `Discrimination` | Mean quality among runs in the top half by Intelligence Index, minus the bottom half. **Suppressed below 4 runs**, where the split is one run against one run |
| `MeanToolCalls`, `BudgetBoundFraction` | Fraction of runs at or above 90% of the question's tool budget |

Flags, all advisory:

- **`Saturated`** — mean ≥ 97 with a spread ≤ 3. The item carries little information and inflates
  every index equally.
- **`Miscalibrated`** — `|DifficultyDelta| ≥ 25`. The weight this item contributes to the
  Intelligence Index does not match what models actually score on it.
- **`Unstable`** — spread ≥ 30. Either a genuinely discriminating item or an ambiguous one; a
  human decides which.
- **`BudgetBound`** — at least half the runs were at or above 90% of the budget. The cap, not the
  model, may be setting the score.
- **`AssessorConfounded`** — more than one assessor graded the item's runs, so its spread mixes
  candidate ability with grader severity. Fires on suite 5 from the stage-2 assessor promotion
  onward.
- **`ScoringMethodMixed`** — more than one scoring method version. A run graded under method 5 and
  one under method 6 grade accuracy by different rules — method 6 forbids the unverified-claim
  deduction method 5 permitted — so their scores are not the same measurement. Fires on suite 5 as
  soon as harness version 7 ships, which is precisely when a reader needs to be told.

When either confound flag fires, **every other statistic on the row is confounded** rather than a
measurement, and the row is presented that way. Below 4 runs the whole row is marked
`InsufficientData`; the row is still shown, because seeing it is how an operator learns the suite
needs more runs.

> **The non-writeback rule.** `EmpiricalDifficulty` is **never** written into
> `BenchmarkQuestion.AssessedDifficulty` — not automatically, and not by a one-click action,
> because no such action exists anywhere in the API or the UI. `AssessedDifficulty` weights the
> Intelligence Index, so deriving it from the scores it weights is circular: a model that does
> badly on an item would retroactively reduce that item's weight, flattering the very run that
> produced the number. The delta is *reported* so a human can re-author the question or re-rate it
> deliberately.

### Rubric gaps from recurring unverified claims

`BenchmarkRubricGapDetector` consumes the `UnverifiedClaimsJson` that harness version 7 records,
grouped by question **and revision**. No AI calls, no embedding service.

- **Clustering**: lowercase alphanumeric tokens, a short stopword list, Jaccard similarity ≥ 0.6.
  Deliberately simple and explainable — a human reads every cluster anyway, so the cost of a
  slightly loose cluster is a moment's reading, while the cost of an opaque similarity model is
  that nobody can say why two claims were grouped.
- **Model family**: `provider` plus the first two hyphen-separated segments of the model id, so
  `gpt-5.6-luna` and `gpt-5.6` are one family and `gpt-5.6` and `gemini-3.7-flash` are two. The
  provider is part of the key because the verdict rests on the families being *independent*.
- **Verdict**: a cluster raised by **two or more distinct families** is `LikelyRubricGap` and is
  surfaced for a human to fold into the rubric. A cluster from one family is `LikelyHallucination`
  and is **not** presented as a suite issue — it is a finding about that model, already visible on
  its own run.

This is the defensible form of "let the models under evaluation improve the benchmark". The
indefensible form — asking a candidate what the answer key should say — lets a model argue its own
score up. What happens here is narrow: a claim becomes evidence about the *rubric* only when
independent families raise the same one, and even then it is surfaced rather than applied.

### Rubric source-citation validation

`BenchmarkRubricCitationValidator` parses the `**SOURCE**` convention and resolves what it finds
against the running indexes. No AI calls.

- **File paths** (`src/o_init.c`, `include/objclass.h`) — resolved through `SourceCodeService`.
- **Backticked symbols** (`` `MH_GNOLL` ``) — resolved through `FindDefinition`. A backticked span
  that is prose rather than an identifier is skipped, so it does not become unresolvable noise.
- **Line numbers are parsed and reported but never validated.** They drift with every commit, and
  permanent false alarms train an operator to ignore the whole panel.
- **Wiki titles** — resolved where a title lookup exists; otherwise reported as `NotValidated`,
  explicitly. "We did not check" and "we checked and it is fine" are different facts, and a panel
  that conflates them is worse than one that omits the row. The report also states whether the
  source index had finished building, because an unresolved citation means little if it had not.

### Coverage gap analysis

The **only** AI-using part of this section: an explicit admin action with an explicitly selected
model, exactly like the existing difficulty-rating action, and gated by the same spend caps.

Guardrails, all requirements rather than guidance:

- The model receives the suite's **question texts only** — no rubrics, no answers, no scores, no
  item statistics. Withholding the scores is the point: a model shown which questions models did
  badly on would report gaps that flatter or punish particular runs, and the resulting suite would
  encode last run's outcome rather than the domain.
- The result is a **read-only report**. Nothing is written into the suite, and no endpoint exists
  that would write one.
- No generated question or rubric may be inserted without human editing and approval.
- **A gap with no source location is discarded by the parser.** A draft rubric that cannot cite a
  source is not usable as an answer key.
- The analysing model is **disclosed on the report** — display name, provider, model id, thinking
  level, and its token cost — as a difficulty rating discloses its assessor. It is not snapshotted
  onto the suite, because the report is not persisted either.
- **Keep the authoring configuration distinct from both graders'.** Under the staged assessor
  migration Anthropic occupies the second-opinion slot from the next run onward, so an Anthropic
  authoring configuration must not be the same one used to grade: a model that helped author a
  suite is not a neutral reader of answers against it.

`MaxQuestionsPerSuite` (50) and the compliance section's growth-cap argument are unaffected,
because nothing is auto-inserted and the endpoint adds one bounded call per invocation.

### The Suite Health panel

The **Suite Health** button on a suite card opens a **full-screen modal dialog** — dismissed with
Escape, the header close button, or the footer Close button. It is full-screen because it has to be:
the item analysis is an eleven-column table, and the suite cards it used to render inside are 560px
wide, which forced the question text to truncate and every other column to stop wrapping. There is
one dialog for all suite cards, re-created per opening so the reports always reload rather than
showing the previous suite's figures. It carries four tabs following the shared `.gh-tabs` /
`.gh-tab` widget conventions: **Items**, **Rubric gaps**, **Citations** and **Coverage**. Each tab is
its own scroll container, with the item table's header row and its question column pinned.

The Items tab also shows a row of summary tiles below the banner: questions, items with runs, items
below the run floor, flagged items, confounded items. These are **counts only, never a mean or any
other derived measurement** — a large legible average would be read before the caveat that says it is
not a measurement, whereas a count of items in a state is true at any sample size. The **Refresh**
control beside the tabs re-fetches the Items and Rubric gaps reports only; Citations and Coverage
keep their own explicit buttons, because one scans an index and the other spends AI tokens.

Statistical honesty is a UI requirement here, not a nicety. The Items tab's banner comes *before*
the table and states the suite-level sample size, the assessor mix, the scoring-method mix, how
many answers were excluded for having no question link, and the non-writeback rule. Every row
carries its own `n runs / n models / n assessors / n scoring methods`; `Discrimination` reads
"insufficient data" below 4 runs; a confounded row is marked as such. Every action in the panel is
"open this question for editing".

---

## 5. Data Model & Relationships

```
BenchmarkScoringProfile (1)
       │
       └───< (N) BenchmarkRun (1) ───< (N) BenchmarkRunAnswer
                       ▲
                       │
BenchmarkSuite (1) ────┴───< (N) BenchmarkQuestion
```

- **`BenchmarkScoringProfile`**: Name, `IsDefault`, dimensional weights, `LevelScoresJson`, `CriticalErrorCeiling`, `SpeedTargetMs`, `SpeedDecayK`, `MaxParallelQuestions`, `SecondOpinionQualityThreshold`, `SecondOpinionMode`, `SecondOpinionOutlierDeltaPoints`.
- **`BenchmarkSuite`**: Unique suite name, description (accepts Markdown, rendered as sanitized HTML), timestamps, and questions.
- **`BenchmarkQuestion`**: Order index, `ItemRevision` (bumped whenever the question text, band or rubric changes — an edited question is a different item), question text, difficulty tier, `AssessedDifficulty` ($1\text{--}100$), `AssessedDifficultyAtUtc`, expected rubric points, and the assessing model: `AssessedDifficultyModelSnapshotId` (its recorded settings, display name included) and `AssessedDifficultyModelConfigurationId` (attribution only).
- **`BenchmarkRun`**: The four model roles, each as a settings snapshot (`TestedModelSnapshotId` and `AssessorModelSnapshotId`, required; `SecondOpinionAssessorModelSnapshotId` and `ClaimVerifierModelSnapshotId`, null for an absent role) beside an attribution-only `…ModelConfigurationId`, the effective grader output caps (`AssessorEffectiveMaxOutputTokens`, `SecondOpinionEffectiveMaxOutputTokens`, `ClaimVerifierEffectiveMaxOutputTokens`), run status, `QualityIndex`, `UnweightedQualityIndex`, `SpeedIndex`, `TotalAnswerDurationMs`, `ScoringProfileId`, `ScoringProfileSnapshotJson`, `ScoringMethodVersion`, `HarnessVersion`, the instrument fingerprint (`CandidateSystemPromptSha256`, `CandidateSystemPromptText`, `ToolGuidesSha256`) and the corpus fingerprints (`KnowledgeBaseHeadSha`, and from harness 16 `WikiHeadSha` and `SourceCodeHeadSha` — Git HEAD SHAs of the GnollHack wiki and source corpora, where null means "not recorded", never "no corpus"), `DifficultyFallbackUsed` (set when a scored answer carries no assessed difficulty and is therefore weighted by its authored band's fallback — from scoring method 10 that is how an unanswered question is weighted), `SpeedMeasurementDegraded`, `MaxParallelQuestionsUsed`, `AnsweredQuestionCount` and `UnansweredQuestionCount` (questions the model failed to answer — not the complement of the former, since a provider error and a question that never ran are neither), the integrity counts (`TransportDefectAnswerCount`, `RecoveredAnswerCount`, `AdvisoryFlagAnswerCount`, `ContestedVerdictAnswerCount`, `ReassessedAnswerCount`), the second-opinion record (`SecondOpinionModeUsed`, `SecondOpinionGradedAnswerCount`, `SecondOpinionMeanAbsDelta`), token accounting, and assessment synthesis.
- **`BenchmarkRunAnswer`**: Order index, question text, sanitized visible answer text, thought text (reasoning), dimensional levels (0–6), dimensional scores, `QualityScore`, `SpeedScore`, `CriticalError`, `AssessedDifficulty`, `AssessmentStatus`, assessor comment, token/duration metrics, the assessor's evidence (`AssessmentEvidenceJson`, `CriticalErrorQuote`, `UnverifiedClaimCount`, `UnverifiedClaimsJson`), the second-opinion verdict and its `SecondOpinionTrigger`, re-assessment provenance (`PreviousQualityScore`, `ReassessedAtUtc`, `ReassessmentCount`), and the snapshot of every model that graded it (`AssessedByModelSnapshotId`, `SecondOpinionByModelSnapshotId`, `ClaimVerificationByModelSnapshotId`, `ReassessedByModelSnapshotId`, `EvidenceInformedByModelSnapshotId`).
- **`BenchmarkRunAnswer` termination provenance**: `TerminationReason` describes what the harness loop did (canceled, budget exhausted, iteration limit, completed); `ProviderFinishReason` is the provider's own verbatim reason for ending the response, unmapped. Read together they separate an empty answer the model produced — a normal stop with no text, scored 0 from scoring method 10 — from one a transport defect destroyed, which stays unscored. Null means "not recorded" and never "stopped normally".
- **`BenchmarkRunAnswer` item identity**: `BenchmarkQuestionId` (nullable FK, `DeleteBehavior.SetNull`) and `ItemRevisionUsed`. The stable link between an answer and the question it answers; before it existed, `OrderIndex` was the only link and a suite reorder silently re-attached every earlier run's answers to the wrong questions. Null means "unlinked" and is excluded from item analysis rather than guessed at.
- **`BenchmarkRunAnswer` rubric record**: `ExpectedPointsUsed` (nvarchar(max), null) and `ExpectedPointsRecorded` (bit). The rubric the answer is graded against, copied when the answer row is created; every re-grade reads it, never the live question. `ExpectedPointsRecorded = false` means the rubric is not known, and any re-grade of that answer is refused; with it true, a null `ExpectedPointsUsed` means the question had no rubric points.
- **`BenchmarkRun` exam record**: `BoardSnapshotId` (nullable FK to `BenchmarkRunBoardSnapshot`, `Restrict`) — the board text and digest the run was asked and graded with, null when the run had no board; a non-null `GameSnapshotSha256Used` with a null `BoardSnapshotId` means the board is unknown. `DefaultSuiteVersionUsed` (int, null) — the default-suite version at launch, which the report prints after the key.
- **`BenchmarkRunBoardSnapshot`**: `Sha256` (`char(64)`, unique), `SanitizedText`, `DigestText`, `CharCount`, `CreatedAtUtc`. Append-only and content-addressed: the key is the lower-case hex SHA-256 of the UTF-16LE `SanitizedText + "\0" + (DigestText ?? "")`, so a digest change is a new row. No suite operation edits or deletes a row; written through `BenchmarkRunBoardSnapshotStore.GetOrCreateAsync` at launch.
- **`BenchmarkAssessorCalibration`**: One non-destructive re-grading of a run by an alternative assessor — the assessor's settings snapshot (`AssessorModelSnapshotId`), `AnswerCount`, `SkippedAnswerCount`, `MeanAbsDelta`, `DisagreementCount`, token and duration cost, and `VerdictsJson`. Admin-UI only: it never appears in the Markdown report, because a calibration is an experiment about graders rather than a property of the run.
- **`BenchmarkRunAnswerToolCall`** (from harness 17): One row per tool call **attempted** during an answer's turn — `SortOrder`, `IterationIndex`, `Name`, `ToolCallId`, `Status`, `ArgsText`, `Result`, `Error`, `QueueWaitMs`, `ExecutionMs`, `Depth`, `AgentName`, `ArgsTruncated`, `ResultTruncated`, `ResultLengthChars` — cascade-deleted with `BenchmarkRunAnswer` and indexed on `(BenchmarkRunAnswerId, SortOrder)`. `ArgsText` and `Result` are pruned by age; every other field survives the prune. See **Harness Version 17 Updates**.

### Model Configuration Snapshots

Benchmark history never depends on a `SystemAiApiConfiguration` row. Every model a benchmark used is
recorded as a row of **`SystemAiConfigurationSnapshot`**, an append-only, content-addressed table: one
row per distinct combination of settings ever used, keyed by `Sha256`, never updated and never deleted.

- **Fields.** `Provider`, `ModelId`, `DisplayName`, `ThinkingLevel`, `ReasoningMode`,
  `ReasoningSummary`, `ServiceTier`, `MaxOutputTokens` (the configuration's own value),
  `ParallelExecutionMode`, and the endpoint — `BaseUrl`, `ApiVersion`, `CustomHeadersJson`, where null
  means the provider's official endpoint. The API key, the chat-only `MaxInputTokens`, pricing (frozen
  per role in `BenchmarkRun.PricingSnapshotJson`), posture, rate limits and notes are deliberately not
  stored.
- **Hash.** The SHA-256 of the UTF-16LE canonical text `SystemAiConfigurationSnapshot/1\n` followed by
  one `Name=value\n` line per **non-null** field in ordinal name order, as lower-case hex
  (`SystemAiConfigurationSnapshotStore.ComputeSha256`). Null fields are omitted, so adding a nullable
  field later moves no existing hash.
- **`IsComplete`.** False only for rows backfilled from the legacy inline columns
  (`DecoupleHistoryFromSystemAiConfigurations`), which did not record every setting. On such a row a
  null field means **not recorded**; the comparability keys render it `(not recorded)`, never `(none)`.
  Legacy candidate rows recorded everything and are complete. Every legacy call went to the official
  endpoint, so their null endpoint fields are true.
- **References.** Twelve foreign keys point here, all `Restrict` and all auto-included: the four run
  roles, the five per-answer graders, the difficulty assessor, the calibration assessor and the rubric
  author. The `…ModelConfigurationId` columns beside them are plain attribution ids, not foreign keys:
  they keep "the run had a second opinion" and "graded by a different assessor" answerable, and default
  the re-run dialogs, but deleting a configuration never touches them.

**A run executes with the settings it recorded.** Launch captures a snapshot for each role and the
output cap each grader's calls will send (`AssessorEffectiveMaxOutputTokens` and its two siblings,
harness fallbacks included). Every later model call of the run — candidate, assessor, second opinion,
claim verifier, re-runs and re-assessments — takes its settings **and endpoint** from the role's
snapshot and only its key from the live configuration (`SystemAiConfigurationSnapshotStore.Bind`). A
mid-run edit therefore changes nothing. If the live configuration's provider or endpoint no longer
matches the snapshot, the key cannot call the recorded model and the call fails with *"The
configuration's provider or endpoint changed since this run was launched; its key cannot call the
recorded model."* A retry, re-assess or calibration with an **override** configuration captures that
configuration's snapshot at that moment and records it on the answer or run. Difficulty and rubric-gap
jobs capture at job start.

**Endpoints.** Benchmark calls use the configuration's custom endpoint, resolved strictly through
`EndpointPolicy.TryResolveStrict`: a stored endpoint the policy no longer accepts fails the call and is
never replaced by the official endpoint, because a benchmark that silently called somewhere other than
where it records would be worse than one that fails. Launch validates each role's endpoint in full,
DNS included. Reports and the run view show the endpoint as *official* or *custom (…; fingerprint …)*,
never its hostname; the comparability keys carry it as `official` or `custom-<12 hex>`.

**Deleting a configuration.** Always possible unless something is calling its model right now — a
running run, an active series, or a difficulty, question-generation, rubric-check or rubric-gap job.
`GET /api/admin/systemconfigs/{id}/deletion-check` reports the blockers and what a delete removes (user
and group assignments, users' confidentiality decisions) and keeps (benchmark history, usage and error
logs); `DELETE` re-checks and answers `409` with the blockers. A stopped series is not a blocker, but
resuming it after the delete is refused because its configuration no longer exists.

**Comparability key definition version 2.** `BenchmarkComparabilityKey.DefinitionVersion` is stored
beside every persisted group key hash in `BenchmarkRunGroup.ComparabilityKeyVersion`; a group whose
stored version differs is flagged *Comparability definition changed* until re-analysed. Version 2 reads
the candidate keys from the tested snapshot, adds `CandidateEndpoint` (a model-axis key), and gives all
three grader signatures one shape:
`provider;model;thinking;reasoningMode;reasoningSummary;serviceTier;maxOutputTokens=<effective cap>;endpoint`,
the second opinion adding its `mode`, `blind` and `minimumSample`. `parallelMode` left the grader
signature: it shapes only the candidate's system prompt. Runs recorded before version 2 read as a
different grader instrument from later runs, which is the truth.

### Difficulty Assessment Lifecycle
1. **Explicit Assessor Selection**: Question and suite difficulty ratings are explicit actions where the administrator chooses any benchmark-capable System AI Configuration via a modal selector dialog.
2. **Clear on Edit**: When a question's text, author difficulty tier, or expected criteria rubric is modified, any existing assessed difficulty and assessor snapshot are automatically cleared.
3. **Suite Completion Tracking**: Each suite card displays a completion badge indicating progress (`Difficulty n/total Assessed`), styled green at full completion, amber when partial, and neutral at zero.
4. **Run Gating**: Benchmark runs require every question in the suite to be assessed before execution. Starting a run with unassessed questions is rejected with HTTP 400 BadRequest. The legacy pre-run silent auto-rating step has been removed.

### Content Rendering & Security Principles
Rendering policy strictly depends on content author:
- **Administrator-Authored Content** (Suite descriptions, names, and question expected answer criteria/rubrics): Authored as Markdown and rendered as sanitized HTML via `MarkdownPipe` (`marked` + `DOMPurify`) inside `CollapsibleMarkdownComponent`. Note that `MarkdownPipe` also defangs images from other origins, replacing them with a `.blocked-external-image` badge — so an administrator embedding a remote image in a suite description will see the badge rather than the image. See `docs/overseer/data-privacy-framework.md` § 3.11.
- **Rubric Authoring Conventions**: Rubrics should use bold section labels (`**REQUIRED**`, `**CRITICAL ERROR**`, `**SCOPE**`, `**FORM**`, `**SOURCE**`), bulleted lists (`- `), and inline code backticks (`` `symbol` ``). ATX headings (`#`, `##`, `###`) are avoided to prevent collisions with the prompt's outer sectioning hierarchy. In prompts, rubrics are safely fenced between `--- BEGIN RUBRIC ---` and `--- END RUBRIC ---` delimiters on separate lines.
  - **A `**FORM**` section must not name a presentation the graded response style cannot produce.** The suite grades the production chat system prompt, which asks for concise prose, so a rubric demanding a comparison table or a multi-section layout asks the candidate to disobey the very prompt under test. Since scoring method version 9 the assessor records such a suggestion under a `FORM:` marker and does not deduct Readability for it, so the criterion no longer costs the candidate points — but it also no longer means anything, and a `**FORM**` section is worth writing only when the requested shape is one a concise answer could plausibly take.
- **AI-Generated Content** (Candidate model answers, thought reasoning text, assessor evaluations): Untrusted external completions rendered strictly as **plain text** within `<pre>` containers, never through `[innerHTML]`.

### Default Suites

From harness 24, default suites are discovered rather than hardcoded. Each is one file under
`Overseer/Data/DefaultSuites/<key>.json` (configurable via `Benchmark:DefaultSuitesPath`; empty
resolves to `<AppBase>/Data/DefaultSuites`), carrying top-level `key` (a lowercase slug, 1–64
characters) and `version` (an integer) fields alongside the existing `name`, `description` and
`questions` array. `DefaultSuiteCatalogService` parses every `*.json` file in the directory into a
catalog entry; an entry whose file fails validation (missing `key`, a `key` that collides with
another file, missing `name`, or a question with no `questionText`) is listed with its `error` and
cannot be imported, never thrown. A missing directory yields an empty catalog.

Importing a key copies its questions into a new `BenchmarkSuite` — unassessed, `ItemRevision` 1 per
question — and stamps `BenchmarkSuite.DefaultSuiteKey` and `BenchmarkSuite.DefaultSuiteVersion` from
the file. **Import never overwrites an existing row**: re-importing a key already in the database
creates a second suite, with a name-collision suffix (`"<name> (2)"`, `(3)`, …) exactly as
`POST suites/{id}/duplicate` already does. Updating a default-suite file therefore does **not**
retroactively change previously imported rows — to reflect an edited default suite, re-import it or
edit the existing suite manually. This is also why the seed-mirror rule in `server_rubric_handoff`
§ 3a exists: a rubric repair applied only to the database is undone the next time someone re-imports
that key.

A run stamps `BenchmarkRun.DefaultSuiteKeyUsed` from the suite it ran against at launch, so a run
records which default suite (if any) produced its questions independently of whether that suite row
still exists. The exported report's Run Manifest carries this as a **Suite origin** line: `default
suite `<key>` (v<N>)` when the suite row is loaded and its `DefaultSuiteKey` still matches the run's
own, `default suite `<key>`` alone when the version cannot be read back, `custom suite` when no
default-suite key was recorded, and `not recorded (run before harness 24)` for a run from before this
column existed. A suite imported before harness 24 carries `DefaultSuiteKey = null` and is not
backfilled — its runs read as *not recorded*, and the catalog dialog can only offer a *possibly
imported earlier (matched by name)* hint for it, never a definite count.

### YAML Import and Export

Questions and whole suites can be exported to, and imported from, a YAML document that a human or an
AI can edit. YAML is used because a rubric is arbitrary Markdown: a `|` block scalar holds any
content verbatim — headings, code fences, `---` lines — delimited only by indentation.

**The source of truth for the format is
`Overseer/ClientApp/src/app/admin/benchmark/question-yaml/question-yaml-format.ts`**; the rules
below mirror it. Parsing and serializing both happen in the client: the parser is `js-yaml`,
loaded lazily on the first Validate, followed by a schema pass; the serializer is hand-written so
that every multi-line value is a block scalar. The server receives structured requests.

```yaml
# Overseer benchmark questions. Edit freely; keep every `id` you were given.
format: overseer-benchmark-questions
version: 1

suite:
  name: "GnollHack Player Assistance Benchmark Suite"
  description: |
    Eighteen questions across three tiers.
  snapshot:
    name: "Valkyrie dlvl 12"            # optional; defaults to the suite name
    gnollhack_version: "4.2.0 Build 47" # optional, at most 64 characters
    captured_at: "2026-09-16T18:04:11Z" # optional, ISO 8601 UTC
    notes: |                            # optional
      Exported from the developer menu.
    sha256: "9f2c…"                     # optional; written by every export, checked on import
    text: |                             # required inside `snapshot`
      GnollHack 4.2.0 …

questions:
  - id: 42
    difficulty: Simple
    question: |
      What is the Gnoll race?
    rubric: |
      **REQUIRED** (accuracy + completeness)
      - The Gnoll is a GnollHack-original playable race.

  - difficulty: Advanced
    question: |
      Which source file implements ...?
```

| Rule | Requirement |
|---|---|
| H1 | A mapping with `format: overseer-benchmark-questions` and `version: 1`; only the keys `format`, `version`, `suite`, `questions`. |
| H2 | `suite` is optional: `name` (1–128 characters), `description`, `suggested_description`, `snapshot`; no other keys. `suggested_description` is Markdown read only by the Snapshot Suite Wizard's add-questions route; no export writes it. `snapshot` is a **mapping**, whose keys are only `name` (1–128), `gnollhack_version` (≤ 64), `captured_at` (a parseable date, normalized to ISO 8601), `notes`, `sha256` (64 hexadecimal characters) and `text` (required, non-blank). A **string** `snapshot`, or a `snapshot_text` key, gets the old-shape error: *"`suite.snapshot` is now a mapping with `name` and `text`; `suite.snapshot_text` is no longer a key."* |
| Q1 | `questions` is a non-empty list of mappings. |
| Q2 | Question keys are only `id`, `difficulty`, `question`, `rubric`. |
| Q3 | `id`, when present, is a positive integer, unique in the document. |
| Q4 | `difficulty` is Simple, Intermediate or Advanced (case-insensitive) or 1–3. |
| Q5 | `question` and `rubric` are text. An absent key keeps the current value on a replace. |
| Q6 | A blank `question` on a replace is an error; `rubric: \|` with nothing under it clears the rubric. |
| M1 | Single-question import (a question's *Import from YAML*): exactly one question, whose `id`, if present, is the target's. |
| M2 | Questions import (Manage Questions toolbar): every `id` belongs to the open suite; a question without `id` is created and needs `question`; `suite` is ignored with a notice. When the open suite's name is passed and the file's `suite.name` differs, a second notice says *"This file names suite "X", but you are importing into "Y"."* — the Snapshot Suite Wizard passes no name and reports the difference as a route check instead. |
| M3 | Suite import (Manage Suites toolbar): `suite.name` is required; every question is created and needs `question`; ids are ignored with a notice, and so is a `suite.suggested_description` (*"`suite.suggested_description` is ignored: a suite import uses `suite.description`."*). The snapshot gets no notice here — what happens to it is reported on the review step, from the server's preflight. In `single` and `questions` mode a document that carries a snapshot gets the notice *"The game snapshot in the file is ignored: this import changes questions only."* |
| L1 | Created questions must not push the suite past `Benchmark:Compliance:MaxQuestionsPerSuite`; the server enforces it and the dialog shows its message. |

Syntax errors are reported as *Line N, column M: reason*; schema errors name the location, as in
*questions[3] (id 42): unknown key `tier`*. Block scalars lose their trailing newline and trailing
whitespace on parse; a BOM and CRLF are accepted. A file written for the Snapshot Suite Wizard
follows the export — UTF-8 without a BOM, LF line endings, never mixed. Export writes UTF-8 without a BOM, LF line endings,
the `|2` indentation indicator where a value's first line starts with whitespace, a `# Question N`
comment (the question's `OrderIndex`, as a run report prints it) before each item, and omits
`rubric` for an empty rubric. Every multi-question export of a snapshot suite (*Download All as
YAML* and its copy button on the Manage Questions toolbar, *Download Suite as YAML* and *Copy Suite
as YAML to Clipboard*) also carries the board as the `suite.snapshot` mapping — text, hash and
metadata from one `GET snapshots/{id}?includeText=true` — so the file alone is enough to check a
BOARD FACT **and** to recreate the suite together with its board. If that fetch fails the export is
written with **no `snapshot` key at all** (an incomplete mapping would not import) and the status
line says so. A single-question export never carries it. File names are `overseer-questions-export-<suite-slug>.yaml`,
`overseer-question-export-<orderIndex>-id-<id>.yaml` and `overseer-suite-export-<suite-slug>.yaml`.

**Which suites export.** A suite card's *Download Suite as YAML* and *Copy Suite as YAML to
Clipboard* are live whenever `canExportSuite` holds — the suite has questions **or** a game
snapshot — and `aria-disabled` otherwise. A snapshot suite with no questions skips the questions
request and writes `questions: []`, because a bare `questions:` would read back as YAML null. That
file is the input of the Snapshot Suite Wizard's add-to-suite route; it does **not** re-import as
is, since Q1 requires a non-empty list.

**Replace and create.** A replace changes only the keys present. When the question text, difficulty
or rubric differs after trimming, the question gets exactly what `PUT questions/{id}` does: a new
`ItemRevision` and a cleared AI-assessed difficulty; an identical re-import changes nothing. A
create is appended after the highest `OrderIndex`, `IsGenerated = false`, difficulty Simple unless
given.

**What an import never does**: delete or reorder questions, touch runs, assessments or reviews,
change an **existing** game snapshot, or write anything before the whole document has validated. The dialog's steps are
*Provide YAML* (paste or upload a file of at most 2 MB, then Validate), *Review* (one card per
question — *Replace #N* or *Create new question* — with *Side by side* and *Diff* views), and
*Done*. The server validates the whole batch again before its single save.

**Panel and shell.** The import itself is `app-question-yaml-import-panel`
(`question-yaml-import-panel.component.ts`): the source radios, paste and file inputs, validation,
the review cards, the suite card with the snapshot preflight, `apply()` and the done summary. It
renders **no stepper and no footer**. Two hosts own those: `QuestionYamlImportDialogComponent`, the
standalone dialog, which keeps its three modes, its `.gh-steps` stepper and its footer bound to the
panel through a `ViewChild`; and the Snapshot Suite Wizard. Every element id and the radio group
name derive from the panel's `idPrefix` (`questionYamlImport` in the dialog, `snapshot-wizard-import`
in the wizard), so two panels share one document. `stateChange` tells an `OnPush` host to redraw its
footer.

**Route checks with an expectation.** The standalone dialog passes no `expectation`, and the panel
behaves exactly as described above. The wizard passes an `ImportExpectation` (`import-expectation.ts`),
and the review step then opens with an **outcome statement** in a bordered callout, a **Checks** list
grouped *Blocking* / *Warning* / *Confirmed* (each with a text label and a glyph), and a required
confirmation checkbox whose label counts the warnings. The apply button reads *Add N Questions to
{suite}* or *Create Suite {name}*, and stays inert while a blocking check exists or the box is
unticked. Route A calls `POST snapshots/match` once per validated document to compare the file's
board with the suite's; a failed call is a warning, never a block. Route B does not offer the
*Attach the game snapshot* checkbox and always attaches.

| Route | Level | Code | Trigger |
|---|---|---|---|
| A (add) | blocking | `replaces-questions` | An item carries an `id`: it would replace an existing question. The way out is *Import Questions from YAML* in Manage Questions. |
| A | warning | `other-suite-name` | The file's `suite.name` differs from the target suite's. |
| A | warning | `board-other-suite`, `board-not-stored` | The file's board is stored on another suite, or nowhere: its BOARD FACTS will fail *Snapshot facts*. |
| A | warning | `board-none-in-file`, `board-unchecked` | The file carries no board, or the preflight failed. |
| A | confirmed | `board-matches` | The file's board is the one stored on the suite. |
| A | warning | `over-cap` | Existing plus new questions exceed `MaxQuestionsPerSuite`. |
| A | confirmed | `all-new` | No item carries an id. |
| B (create) | blocking | `no-board` | No `suite.snapshot`. A board-less suite still imports with *Import Suite from YAML*. |
| B | warning | `name-differs`, `name-taken`, `ids-ignored` | The name differs from the one the prompt asked for; a suite of that name exists (*(Imported)*); items carry ids. |
| both | warning / confirmed | `counts-differ` / `counts-match` | The prompt set per-band counts and the file's differ, or match. |
| both | warning | `no-difficulty`, `no-rubric`, `rubric-lint` | Items without a difficulty or rubric; `lintRubric` codes aggregated with counts. |

A route-A document whose `questions` list is empty but which carries a board adds the hint *"This
looks like the file you downloaded for the agent, not the file the agent wrote."* After apply the
result is **checked against the intent** — route A expects `createdCount === N` and
`replacedCount === 0`, route B a suite with `N` questions and a snapshot — and the done step shows
the match as a confirmation or the mismatch, with the numbers, as a warning.

**Suite import always creates a new suite**, named `<name> (Imported)`, `(Imported 2)`, … when the
name is taken, with no `DefaultSuiteKey`.

**The snapshot the document carries is attached.** `ImportSuite` takes an optional
`snapshot: { name?, text, sourceGnollHackVersion?, capturedAtUtc?, notes? }`. Text or HTML is
decided by `LooksLikeHtml`, the same rule an upload uses; HTML is flattened by `DumpHtmlSanitizer`.
The board is capped at 60,000 characters and the request's text at
`MaxSuiteSnapshotUploadChars` (4,000,000). A blank text, an over-long text, a snapshot name over
128 characters or a version over 64 is `400`, checked before anything is added to the context. The
capture method is **`YamlImport`**; a blank snapshot name defaults to the *requested* suite name,
before any "(Imported)" suffix; `notes` defaults to *"Imported with suite YAML."*.

**Matching is by the SHA-256 of the stored form** — after flattening, `NormalizeFlattenedText` and
the cap — so the server, never the client, decides whether two boards are the same.
`BenchmarkSnapshotImporter.StoredForm` computes it, and `CreateForSuiteAsync` normalizes through
the same private helper, so the preflight and the import cannot disagree.

| Identical snapshot found… | What the import does |
|---|---|
| …attached to **no** suite | **Reuses it.** No new row; its name, version, notes and capture method are left as they are, so the file's are dropped. |
| …attached to **another** suite | **Creates a copy**, with the `(2)` name suffix, and the review step says so. |

Why a copy: `BenchmarkSuite.GameSnapshotId` has a unique index and the rest of the system is built
on one board having one owner — the Snapshot Viewer edits the text in place, *Upload Snapshot* with
replace **deletes** the old board, the snapshot DTO names one owning suite. De-duplication suits
immutable content; a snapshot is a mutable, suite-owned record. A run records the snapshot's
SHA-256 **by value**, so two identical copies stay fully comparable, and a copy costs at most
~60 KB.

The suite, its questions and the board land in **one save**. An `ArgumentException` from the
importer — text that flattens to nothing — is `400`; the race in which two imports grab the same
free board at once trips the unique index and returns `409` *"The snapshot was attached to another
suite a moment ago. Import again."* In both cases nothing is persisted.

**The preflight.** `POST snapshots/match` takes `{ text }` and returns
`{ sha256, charCount, truncated, isHtml, match }`, where `match` is `null` or
`{ id, name, suiteId, suiteName }` for a stored snapshot with that hash — an unattached one
preferred when several exist. It writes nothing. The import dialog calls it once per validated
document when the review step opens in suite mode, and the review step reports which of the three
outcomes applies, plus *"cut at 60,000 characters"* when `truncated`. A failed preflight says the
check could not be made and the import still works, because the server applies the same rule
itself. Unticking the checkbox imports the suite without a snapshot. A *"different board"* result
for a file whose `suite` block is the export verbatim means the stored board predates line-ending
normalization and still holds carriage returns; repair it with the Snapshot Viewer's **Normalize
line endings** (see Sanitizer Convergence), and the file needs no change.

**The `sha256` warning.** Every export writes the stored board's hash. On import the server
recomputes it from `text`, and a difference is a **warning** on the review step — *"the text
differs from the snapshot this file was exported from — it was edited, or damaged in transit"* —
never an error, because editing the board in the file is legitimate. An agent-authored file simply
omits the key.

**Difficulty travels.** `difficulty` is parsed per Q4, carried through `toImportItems` in every
mode, and stored as `item.Difficulty ?? BenchmarkDifficulty.Simple` — so an omitted `difficulty`
becomes **Simple**, silently. That is the **authored** band only: the AI-assessed difficulty
(1–100) is what weights the Intelligence Index, the assessor is never shown the authored band, and
the launcher refuses the suite until **Assess Difficulty** has rated every question.

**Rubric notices.** For every question whose imported rubric is present and changed, the review
card lists advisory *Rubric:* notices from `lintRubric`; they never block the import:

| Code | Trigger |
|---|---|
| `no-required` | No line starting `**REQUIRED**`. |
| `form-readability` | A line starting `**FORM** (readability)`, a FORM label that asserts a grading link. |
| `bold-parenthetical` | A heading with its parenthetical inside the bold markers, as `**FORM (…)**`, which breaks `BenchmarkRubricCitationValidator`'s bold-heading look-ahead. |
| `no-board-facts` | The open suite has a snapshot and the rubric has no `**BOARD FACTS**` line. |

Endpoints: `POST suites/{suiteId}/questions/import`, `POST suites/import` and
`POST snapshots/match` (§ 6).

**Two help dialogs, one component.** `QuestionYamlHelpDialogComponent` takes a
`variant` input and is instantiated twice on the benchmark page: `#questionYamlHelpDialog`
(`variant` defaults to `questions`) and `#suiteYamlHelpDialog` (`variant="suite"`). Because both
live in one document, every element id carries the variant's `idPrefix` — `yaml-help` and
`suite-yaml-help` — since a duplicated id silently breaks `aria-labelledby`, `aria-controls`, the
tooltip anchors and the exclusive accordion. The import dialog's *Open the format help* link routes
to whichever matches the open import's mode. The **suite** variant is titled *Suite YAML Import and
Export*, opens from the help icon on the Manage Suites toolbar, and has the tabs *Workflow*,
*Format*, *From a Snapshot*, *Examples* and *AI Prompt*. It makes **no server call**: its guide
text is static, in `suite-yaml-guide.ts`. Its *AI Prompt* tab holds an explanation, an **Open the
Snapshot Suite Wizard** button (Feather `compass`) and the step-by-step instructions of both routes
(`buildSuiteWorkflowInstructions('both')`, downloadable as `overseer-snapshot-suite-steps.md`); the
*From a Snapshot* tab carries the same button (`GuideTab.action: 'wizard'`). The button emits
`(wizardRequested)`, and the page closes the help before it opens the wizard, so the two never
stack. The prompt builder lives only in the wizard. Every tab of both variants opens with a
plain-text ingress saying what the tab is for (`GuideTab.ingress`).

**Code samples.** Every code sample in both variants is an `app-code-block`
(`shared/code-block/code-block.component.ts`): the exact text in `<pre><code>`, never injected as
HTML, with a Copy icon button in its top-right corner — a Feather check for two seconds and a
polite *Copied* announcement on success, a visible line under the block on failure. With a
`caption` it draws a header bar holding the file name and the buttons **Download, Copy**. A guide
tab's Markdown is split by `splitGuideMarkdown` (`guide-segments.ts`) into prose, rendered through
the Markdown pipe as before, and column-0 fenced code, rendered as caption-less code blocks — the
pipe's sanitized `innerHTML` cannot hold a component. On the *Examples* tab each disclosure reads
title, divider, description, then a captioned code card; the questions variant's AI instructions
are a captioned code block too.

The **questions** variant (*Import/Export Help* on the Manage Questions toolbar, also reachable
from the import dialog) is full height with five tabs: *Workflow*, *Replace or Create*, *Format*, *Examples* (six copyable,
downloadable YAML documents in an exclusive `<details name>` accordion, each spec-checked to parse
and validate), and *AI Prompt*, the last holding a copyable, downloadable set of instructions for an
AI. The guide text lives in `HUMAN_GUIDE_TABS` in
`question-yaml-format.ts`. The *AI Prompt* text is assembled by `buildAiInstructions`: the YAML
shape comes from the client, and the *Writing a rubric* section (section rules, grading semantics,
worked example) and the *Difficulty bands* section come from `BenchmarkRubricAuthoringGuidance`
through `GET rubric-authoring-guidance` — the same class `BenchmarkGenerationPrompt` reads, so the
instructions and the generation prompt cannot drift apart. The dialog fetches it on first open; if
the fetch fails, the tab says so and the instructions carry a line telling the AI to ask for the
rubric guidance before editing rubrics.

---

## 6. API Endpoints

All benchmark endpoints require the `AdminOnly` authorization policy:

### Scoring Profiles
- `GET /api/admin/benchmark/scoring-profiles`: List all scoring profiles.
- `POST /api/admin/benchmark/scoring-profiles`: Create a new profile.
- `PUT /api/admin/benchmark/scoring-profiles/{id}`: Update profile configuration.
- `POST /api/admin/benchmark/scoring-profiles/{id}/default`: Mark profile as system default.
- `DELETE /api/admin/benchmark/scoring-profiles/{id}`: Delete profile (default cannot be deleted).

### Rubric Authoring Guidance
- `GET /api/admin/benchmark/rubric-authoring-guidance`: The rubric section rules, grading semantics, worked example, FORM label and the three difficulty bands (`name`, `range`, `description`) from `BenchmarkRubricAuthoringGuidance`, for the YAML help dialog's *AI Prompt* instructions. No database access.

### Difficulty Rating
- `POST /api/admin/benchmark/suites/{id}/rate-difficulty`: Auto-rate difficulty for all questions in a suite with an explicitly selected assessor model; returns `{ ratedCount, suite }`.
- `POST /api/admin/benchmark/questions/{id}/rate-difficulty`: Auto-rate difficulty for a single question with an explicitly selected assessor model; returns `{ difficulty }`.

### Suites & Questions
- `GET /api/admin/benchmark/suites`: List all suites with question counts and assessed question progress. No longer seeds a default suite when the list is empty — an empty database returns an empty list; see *Default Suites* below.
- `POST /api/admin/benchmark/suites`: Create a new suite.
- `PUT /api/admin/benchmark/suites/{id}`: Update suite name and description.
- `DELETE /api/admin/benchmark/suites/{id}`: Delete suite.
- `POST /api/admin/benchmark/suites/{id}/duplicate`: Clone a suite, its questions, and their assessment snapshots.
- `GET /api/admin/benchmark/suites/default-catalog`: List every default suite file found on disk (`DefaultSuiteCatalogEntryDto[]`: key, version, name, description, question count, per-band counts, file name, `error` when the file does not parse, `alreadyImportedCount` and `alreadyImportedNames`). No AI calls; cached per file `LastWriteTimeUtc`.
- `POST /api/admin/benchmark/suites/import-default`: Import one or more default suites by key (body `{ keys: string[] }`, 1–20; 400 when empty). Returns `{ imported: BenchmarkSuiteDto[], skipped: { key, reason }[] }` — imported suites arrive unassessed; import never overwrites an existing row, and a name collision is imported as `"<name> (2)"`, `(3)`, etc.
- `GET /api/admin/benchmark/suites/{id}/item-analysis`: Per-item statistics over the suite's stored runs, with the suite-level sample size, assessor mix and scoring-method mix. Pure arithmetic; no AI calls, no spend gate.
- `GET /api/admin/benchmark/suites/{id}/rubric-gaps`: Clustered unverified claims with a `LikelyRubricGap` / `LikelyHallucination` verdict per cluster. No AI calls.
- `POST /api/admin/benchmark/suites/{id}/validate-citations`: Resolves the rubrics' `**SOURCE**` citations against the running source and wiki indexes. A POST rather than a GET because it walks the whole index. No AI calls.
- `POST /api/admin/benchmark/suites/{id}/coverage-analysis`: Asks an explicitly selected model which subsystems the suite does not test (gated by spend caps). Returns a **read-only report**; nothing is written into the suite, and no endpoint exists that would.
- `POST /api/admin/benchmark/suites/{id}/description-generation`: Drafts a suite description with an explicitly selected model (body `{ generatorModelConfigurationId, instructions?, includeSnapshot, includeDebugText }`; gated by spend caps). 400 for a missing or unusable configuration, 404 for an unknown suite. Returns `SuiteDescriptionGenerationResultDto` with the text, timing, tokens, cost and log; nothing is saved. See *AI-Generated Suite Descriptions*.
- `GET /api/admin/benchmark/suites/{id}/questions`: List questions by order index with assessor snapshot properties.
- `POST /api/admin/benchmark/suites/{id}/questions`: Add a question to a suite.
- `PUT /api/admin/benchmark/questions/{id}`: Update a question (clears assessment snapshot if content changed).
- `DELETE /api/admin/benchmark/questions/{id}`: Delete a question.
- `PUT /api/admin/benchmark/suites/{id}/questions/reorder`: Reorder questions via ID array.
- `POST /api/admin/benchmark/suites/{suiteId}/questions/import`: YAML questions import (body `{ items: [{ questionId?, questionText?, difficulty?, expectedPoints?, replaceExpectedPoints }] }`). Items with `questionId` replace, the rest are created; 404 for an unknown suite, 400 for a foreign or duplicate id, a create without text, or the question cap — all before any write. Returns `{ createdCount, replacedCount, unchangedCount, questions }`. See *YAML Import and Export*.
- `POST /api/admin/benchmark/suites/import`: Create a new suite from a YAML import (body `{ name, description?, questions: [...], snapshot? }`). 400 for a blank or over-128-character name, no questions, a question without text, or the question cap; a taken name becomes `"<name> (Imported)"`. The optional `snapshot` is `{ name?, text, sourceGnollHackVersion?, capturedAtUtc?, notes? }`: 400 for blank text, text over 4,000,000 characters, a snapshot name over 128 or a version over 64; 409 when the board this import meant to reuse was attached elsewhere a moment earlier. The suite, its questions and the board are one save. Returns `BenchmarkSuiteDto`. See *YAML Import and Export*.
- `POST /api/admin/benchmark/snapshots/match`: Read-only preflight for a suite import (body `{ text }`). Returns `{ sha256, charCount, truncated, isHtml, match }`, where `match` is `null` or `{ id, name, suiteId, suiteName }` for a stored snapshot with the same stored-form hash, preferring one no suite owns. 400 for blank text, text over 4,000,000 characters, or text that flattens to nothing. Writes nothing.
- `POST /api/admin/benchmark/suites/{id}/snapshot`: Attach a board built from uploaded snapshot text or an HTML dump to this suite (body `{ name, content, contentKind, notes?, sourceGnollHackVersion?, replaceExisting }`). 409 when the suite already has a snapshot and `replaceExisting` is false. Returns `{ board, suite }`. See *Game Snapshots*.

### Runs & Scoring
- `POST /api/admin/benchmark/runs`: Start a benchmark run (gated by hourly/daily caps and same-provider acknowledgement).
- `GET /api/admin/benchmark/runs`: List historical runs with filtering.
- `GET /api/admin/benchmark/runs/{id}`: Full run detail with question answers, compliance purpose statement, and assessment.
- `GET /api/admin/benchmark/runs/active`: Return `{ runId }` for the run currently executing, or 204 when idle. Lets a client that reloaded mid-run reattach to it; the client then calls `GET .../runs/{id}` for the detail.
- `POST /api/admin/benchmark/runs/{id}/rescore`: Recompute indices for an existing run against a scoring profile (ungated arithmetic).
- `POST /api/admin/benchmark/runs/{id}/answers/{answerId}/reassess`: Re-assess a single question's answer (gated by spend caps). `trial: true` records the verdict in the second-opinion slot and changes **no** score, level, flag or index — including the run's `Status` and `CompletedAtUtc`; overwriting an existing automatic second opinion additionally requires `replaceExistingSecondOpinion: true`.
- `POST /api/admin/benchmark/runs/{id}/calibrate`: Re-grade every answer of a finished run with another assessor and store the agreement statistics only (gated by spend caps). Writes no `BenchmarkRunAnswer` field.
- `GET /api/admin/benchmark/runs/{id}/calibrations`: List prior calibrations for a run, newest first.
- `GET /api/admin/benchmark/suites/{id}/last-assessor`: The assessor of the suite's most recent completed run, for the start dialog's assessor-change advisory. Returns an empty object for a suite with no completed run.
- `POST /api/admin/benchmark/runs/{id}/cancel`: Cancel an active run. The live run's own abort path records what it consumed — the totals over the answers that completed, and the wall clock up to the stop — and publishes no index. When there is no live run to cancel the row is orphaned and its abort path will never run, so the endpoint records those totals itself, deriving the elapsed figure from the two timestamps. When the row's answer rows already cover its suite — a cancelled retry of a finished run — it is restored to the status those answers describe rather than set to `Canceled`, with `Canceled by operator.` as the reason, so the cancel does not lock the run out of later re-runs.
- `POST /api/admin/benchmark/runs/{id}/rerun-failed`: Re-run every question whose answer failed, hit a provider error, or came back empty (`BenchmarkRunFinalizer.NeedsReExecution`) (gated by spend caps). Cancelling it restores the run to the status its answers describe.

> A run that stopped before finishing its suite is refused by **rescore**, **rerun-failed**, **reassess**, **rerun answer**, **rerun synthesis**, **retry failed assessments** and **retry claim verification**: each ends in a full finalisation, which would publish an Intelligence Index and a Speed Index computed over only the questions that completed, into the same columns a complete run uses. The test is `BenchmarkRunFinalizer.IsAbortedRun` — `Canceled` or `Failed` **and** fewer answer rows than `TotalQuestionCount` — so a `Canceled` run whose answers cover its suite is accepted, and its re-run is finalised over the whole suite. Reading, reporting, calibrating, cancelling and deleting such a run are unaffected, and a `CompletedWithErrors` run — which did reach the end of its suite — is not refused. The run summary and detail DTOs carry the verdict as `isAborted`.
- `GET /api/admin/benchmark/runs/{id}/report`: Download server-rendered Markdown report with compliance manifest.
- `DELETE /api/admin/benchmark/runs/{id}`: Delete a single run.
- `GET /api/admin/benchmark/suites/{id}/runs/footprint`: Return stored run count and total answer character footprint for a suite.
- `DELETE /api/admin/benchmark/suites/{id}/runs`: Bulk delete all stored benchmark runs for a suite.

#### Multi-Run: Limits, Series, Groups and Analysis
- `GET /api/admin/benchmark/runs/limits`: The caps and the live **rolling-window** counts — `maxRunsPerHour`, `maxRunsPerDay`, `runsInLastHour`, `runsInLast24Hours`, `remainingDailyHeadroom` (never negative) and `maxRunCountPerSeries`. The Number of runs field binds its `max` to this rather than to a literal, so raising the configured cap raises the field with it.
- `POST /api/admin/benchmark/runs/series`: Start a series of `RunCount` identical runs. `POST .../runs` with `runCount > 1` routes here too, so the two cannot diverge.
- `GET /api/admin/benchmark/runs/series/{id}`: Series status, stop reason, per-member rows, and **both** the member-1 and current instrument hashes — which is what makes a refused resume self-explaining.
- `GET /api/admin/benchmark/runs/series/active`: The series being driven, or the most recent resumable one; 204 when there is none.
- `POST /api/admin/benchmark/runs/series/{id}/cancel`: Cancel the in-flight member and the series. Terminal, and not resumable.
- `POST /api/admin/benchmark/runs/series/{id}/resume`: Continue from `CompletedRunCount + 1`. Answers **409** naming the moved hash when the instrument changed since member 1; `acknowledgeInstrumentChange` proceeds and forces the resulting group to Tier C.
- `GET /api/admin/benchmark/runs/groups`, `GET .../groups/{id}`: Analysis groups, with tier, member rows, latest-analysis id and a staleness flag, and the tested model's identity (display name, provider, model id, thinking level, reasoning mode) taken from the newest member run.
- `POST /api/admin/benchmark/runs/groups/preview`: The tier a set of runs *would* resolve to, without creating anything. This is what the group builder shows while runs are still being selected.
- `POST /api/admin/benchmark/runs/groups`, `PUT .../groups/{id}`, `DELETE .../groups/{id}`: Group CRUD. Creation and membership edits **refuse below Tier B** with the differing keys named, and permit Tier C only with an explicit `crossCondition`.
- `POST /api/admin/benchmark/runs/groups/{id}/analysis`: Compute and persist the statistics; an optional `compareWithGroupId` adds the paired comparison. Refuses a Tier C set: such a set is two conditions, and a pooled index over it would describe neither.
- `GET /api/admin/benchmark/runs/groups/{id}/analysis`: The most recent stored analysis, or 204.
- `GET /api/admin/benchmark/runs/groups/{id}/report`: Download the multi-run Markdown report, built from the **persisted** analysis so it stays reproducible after a run is deleted.

#### Rubric Gap Author
- `POST /api/admin/benchmark/rubric-gap-author`, `GET .../{jobId}`, `GET .../active`, `POST .../{jobId}/cancel`: An AI job that **drafts** proposed rubric additions from verified claim clusters, using read-only tools to confirm each citation. It writes nothing.
- `POST /api/admin/benchmark/questions/{id}/rubric-additions/accept`: Apply **one** operator-approved draft and bump that question's item revision. The request carries the **final text the human submits**, which may be the draft edited or replaced outright; the acceptance record stores the drafting model, the cluster, the citation, and whether the text was taken **verbatim or edited**.

  > There is deliberately **no accept-all endpoint**. § 7 rung 1 of `server_benchmark_to_chat_transfer` requires human authorship of curated knowledge, and `BenchmarkRubricGapDetector`'s own documentation says a gap is *"surfaced for a human to fold into the rubric — never applied automatically"*. One endpoint, one draft, one click, one item-revision bump. Editing before accepting strengthens the authorship claim rather than weakening it.

---

## 7. AI Provider Terms Compliance Controls

The AI Intelligence Benchmark subsystem incorporates technical controls and auditable intent records to ensure operations represent internal model evaluation rather than data extraction, distillation, or training dataset harvesting.

### Summary of Applicable Provider Terms (Snapshot: 2026-09-01)
- **Anthropic** (Commercial Terms §D.4): Prohibits accessing services to train competing AI models or reverse engineer services. Prohibits Anthropic from training on Customer Content.
- **Google** (Gemini API Terms): Prohibits using services to develop competing models or reverse engineering/replicating components or parameter weights.
- **OpenAI** (Terms of Use): Prohibits using model output to develop competing models.

### Evaluation vs. Distillation Analysis
The benchmark performs domain evaluation:
- Outputs are not used to train, fine-tune, distill, or develop any AI model.
- Model completions are scored using an assessor model and formatted as Markdown reports for human administrator review to configure model selection in Overseer.
- Queries are ordinary single-turn requests evaluating domain understanding (NetHack/GnollHack mechanics and C source code).

### Technical Controls Enforced in Code
1. **Structural Growth Caps**:
   - `MaxQuestionsPerSuite` (Default: 50): Prevents question suites from expanding into scraping/harvesting pipelines. Enforced on manual question creation, suite duplication, and default suite import.
   - `MaxRunsPerHour` (Default: 5) & `MaxRunsPerDay` (Default: 20): Evaluated across all suites and models. Reaching either cap returns `HTTP 429 Too Many Requests` with a clear explanation naming the hit cap and configured limit.
   - Bounded store: Daily maximum data ingestion cannot exceed $20 \text{ runs} \times 50 \text{ questions} = 1,000 \text{ answers/day}$ even under continuous utilization.
2. **Same-Provider Evaluation Gate**:
   - When the tested model and assessor model share an AI provider (e.g., Google assessing Google, or Anthropic assessing Anthropic), `POST /api/admin/benchmark/runs` returns `HTTP 409 Conflict` with provider details.
   - The administrator must review and explicitly acknowledge the same-provider methodological notice (`acknowledgeSameProvider: true`).
   - Acknowledged status is persisted on `BenchmarkRun.SameProviderAcknowledged` and disclosed in generated reports.
3. **Auditable Purpose Statement**:
   - `Benchmark:Compliance:PurposeStatement` is recorded at execution time on `BenchmarkRun.PurposeStatementUsed` and included in every exportable Markdown report.
4. **Visible Footprint & Manual Bulk Deletion**:
   - Retention is indefinite by design to maintain historical evaluation records without automated purges.
   - To keep storage visible and actionable, each suite displays its stored footprint (run count and total answer character count) alongside a bulk deletion control (`DELETE /api/admin/benchmark/suites/{id}/runs`).

### Limits of Assessment
- Snapshot date: 2026-09-01. Click-through terms change over time.
- Enterprise agreements: Negotiated enterprise agreements override standard click-through terms.
- Engineering risk reduction: Automated abuse detection systems trigger on traffic shapes; rate caps and low concurrency protect API accounts from false-positive flags.

### Re-evaluation Triggers
Compliance review must be revisited if:
1. Benchmark results or comparisons are published externally as competitive claims.
2. Benchmark completions are used for automated ingestion, distillation, or downstream model tuning.
3. Daily run volumes or suite question sizes are increased by orders of magnitude.
4. AI providers update commercial use or evaluation terms.

---

## 8. Thinking Level Configuration & Output Limits

- **Pin Explicit Thinking Levels**: Benchmark and assessor System AI Configurations should pin an explicit **Thinking Level** (e.g. `high`, `medium`, or `none`). Leaving it on `Default` makes a run's reasoning behavior depend on the model and on `AnthropicSettings:ExplicitDefaultEffort`, which can compromise run-to-run comparability over time.
- **Assessor Token Limits (`AssessorMaxOutputTokens`)**: Evaluator and assessor completions share their `max_tokens` budget with internal reasoning/thinking output. The default fallback limit (`Benchmark:AssessorMaxOutputTokens`) is set to `32000` to prevent assessor evaluation JSON completions from being prematurely truncated when thinking is enabled. Individual assessor configurations can override this fallback using their per-configuration `MaxOutputTokens` setting.

---

## 9. Keeping Chat Limits in Step with the Benchmark

The benchmark measures what the Overseer chat can do, so the chat's own defaults must not be tighter than the caps the harness grants its hardest questions. They were: before harness version 6 the chat allowed 15 tool iterations against the Advanced band's 22, and 50 tool calls against the Advanced band's 45.

**The scope difference is the part that is easy to misread.** `ToolExecutor` keys its counter on `ToolBudgetScopeId ?? SessionId`. The benchmark sets a per-question scope (`bench_{runId}_q{orderIndex}`), so `Benchmark:ToolCallBudget` is a **per-question** allowance. Chat sets none, so `AiPerformanceSettings:MaxCallsPerSession` is the allowance for an **entire chat session** across a four-hour window. Comparing the two numbers directly makes the chat look generous when it is not.

Two invariants, to be re-checked whenever the benchmark caps are retuned. Since harness 13 the three resource caps are flat, so the first invariant holds by construction rather than by coincidence — it applies to **every** question, not only to the Advanced band's:

| Invariant | Before harness 13 | Today |
|---|---|---|
| `AiPerformanceSettings:MaxToolIterations:Default` **equals** `Benchmark:ToolIterations` | 22 = 22 (Advanced band only) | 22 = 22 (every question) |
| `AiPerformanceSettings:MaxCallsPerSession:Default` is **at least 3x** `Benchmark:ToolCallBudget` | 150 ≥ 3 × 45 | 150 ≥ 3 × 45 |

The cap keys are **flat values** since harness 13 (`Benchmark:ToolCallBudget`, `Benchmark:ToolIterations`, `Benchmark:TotalModelCalls`); only `Benchmark:QuestionTimeoutSeconds` is still a per-band section. A leftover banded key from an older deployment fails silently rather than loudly — `GetValue<int>` on a key that has children returns `0` — so an upgrade must delete it, and the Run Manifest is the place to confirm the flat key is being read.

The 3x factor is empirical, not arbitrary: on the 2026-09-03 run a single Advanced question executed up to **39** tool calls (Q13 and Q18, each 39 of 45), so a session budget has to cover several such questions rather than one. At the old default of 50, the second hard question in a session was refused mid-investigation with "Maximum tool calls per session exceeded."

Both chat values remain user-adjustable in `/settings`; these are the defaults for a user who has never changed them. `MaxResultLength` already agreed at 10,000 on both sides, and chat's `ChatRequestTimeout` (1,800 s) already exceeds the Advanced band's per-question timeout (720 s).

### Comparability Across Runs: The Budget Key Widened, the Scoring-Profile Key Narrowed

`BenchmarkComparabilityKey` resolves whether a set of runs may be pooled (§ 2's Multi-Run
Replicate Sets). Two of its keys changed, in opposite directions, and both bear directly on the
caps this section documents.

**The budget key widened.** `BenchmarkComparabilityKey.BudgetSignature` previously covered only
`MaxToolCallsPerQuestionUsed`, so two runs straddling a change to any of the other three caps —
the tool-iteration cap, the total model-call cap, or the per-question timeout — still resolved
Tier A, even though these very caps had already changed once (banded → flat, after run 13, as
above). `BenchmarkRun` now carries three nullable snapshot columns, written at run start from the
same configuration read the per-question path uses: `ToolIterationCapsJson`,
`TotalModelCallCapsJson`, `QuestionTimeoutSecondsJson`. Each is canonical per-band JSON with a
fixed key order and invariant-culture numbers — `{"Simple":22,"Intermediate":22,"Advanced":22}` —
so the comparability text is byte-stable; a flat cap renders its single figure under all three
band keys, which is literally what applied to every band, and the same shape survives a future
re-banding with no schema change. `BudgetSignature` now covers all four caps. Runs recorded before
the three columns existed render `(none)` for them, so they match each other and differ from newer
runs — correctly, because for those runs the harness genuinely does not know what caps applied.
**Operator-facing consequence: a group mixing runs from before and after a cap change now
correctly drops below Tier A.** That is the fix working, not a regression.

**The scoring-profile key narrowed.** `BenchmarkRun.ScoringProfileSnapshotJson` stores the whole
serialised profile entity — including `Name`, `IsDefault`, `CreatedAtUtc` and `ModifiedAtUtc` —
and the comparability key used to hash that entire blob. None of those four fields can move a
score, and all four moved the hash: renaming a profile, promoting a different profile to default,
or editing a field and reverting it each silently ended a comparable series with nothing in the
UI saying so. The key now hashes `BenchmarkScoringProfileService.CanonicalSignature`, computed
only from the profile's *scoring semantics* — the four dimension weights, a normalised
`LevelScoresJson`, `CriticalErrorCeiling`, the five second-opinion fields, the three speed
constants, and `MaxParallelQuestions` — deserialised from the stored snapshot, falling back to a
hash of the raw blob when the snapshot will not deserialise. The profile id still travels alongside
this signature in the key, because two profiles with identical scoring semantics are still two
profiles. `ScoringProfileSnapshotJson` itself is unchanged: it remains the historical record of
what the profile actually was.

Two consequences worth stating plainly: **nothing that matched before stops matching** — the key
is recomputed from each run's stored snapshot at comparison time, with no key hash stored on the
run itself, so narrowing what it reads applies uniformly to every run, past and future. And
because the key's rendered value changed, **any already-stored `BenchmarkGroupAnalysis` reads as
stale and should be re-analysed**, so its recorded comparability-key hash reflects the narrowed
definition.

This is the second time a non-semantic field inside a hashed snapshot has made a tier spuriously
unreachable — the first was `PricingSnapshot`'s `capturedAtUtc` (Sentry OVERSEER-8, § 2). When a
field is added to `BenchmarkScoringProfile` in the future, `CanonicalSignature` must be updated
with it, or the same defect recurs a third time.

**The default profile was also renamed**, from `Standard Intelligence Index (Default)` to
`Standard Intelligence Index`, by a migration data step matching that exact name (the seed itself
had always written the clean name — the suffix was a stored anomaly rather than the intended
value). The badge beside the profile heading already states that it is the default, so the suffix
said so twice. Historical runs display the *live* profile name, so they now show the clean name
too — correctly, since it is the same profile — while each run's own `ScoringProfileSnapshotJson`
keeps the old, suffixed name verbatim, as history requires. This rename was only safe to make
**after** the comparability key stopped hashing `Name`: renaming the row before this fix would
itself have ended every comparable series that used the default profile.

---

## 9.1 What the Benchmark Tells the Chat

While § 9 governs the **chat → benchmark** direction (ensuring benchmark resource limits never exceed chat defaults), this section establishes the **benchmark → chat** return path (translating benchmark empirical findings into chat quality, latency, and cost improvements).

### The Benchmark Grades the Production Chat Prompt

A foundational architectural reality in Overseer is that the benchmark does not evaluate models in a vacuum or under an artificial test prompt: **the candidate model is evaluated under the verbatim production Overseer chat system prompt**.

The candidate prompt is built from a snapshotted configuration record rather than from literals at the call site, in `BenchmarkService.cs:262-270`:
```csharp
var promptOptions = new BenchmarkCandidatePromptOptions
{
    VerboseMode = verboseMode,
    HasGameSnapshot = suiteHasBoard
};
run.CandidatePromptOptionsJson = promptOptions.ToCanonicalJson();
run.CandidatePromptSourceUsed = "ChatService.BuildSystemPrompt";

string systemPrompt = promptOptions.BuildSystemPrompt(_chatService, testedConfig.ParallelExecutionMode);
```

`BenchmarkCandidatePromptOptions.BuildSystemPrompt` (`BenchmarkCandidatePromptOptions.cs:93-107`) forwards those options to the production builder, `ChatService.cs:1395`:
```csharp
internal string BuildSystemPrompt(
    IEnumerable<string> wikiContext,
    bool spoilerFreeMode,
    bool verboseMode,
    bool isGameOn,
    bool developerMode,
    int overseerMode,
    bool hasGameSnapshot,
    bool hasMessageHistory,
    string? clientSettings,
    bool enableToolUse,
    bool enableWebSearch,
    bool allowSourceCodeReferences,
    bool enableSubAgents = false,
    ParallelExecutionMode parallelMode = ParallelExecutionMode.Enabled)
```

There are three such call sites — `BenchmarkService.cs:270`, `:497` and `:3562` — all routed through `BenchmarkCandidatePromptOptions`.
Every quality score, completeness deduction, hallucination finding, and latency measurement in a benchmark run is a direct empirical test of the exact instructions, formatting guidelines, and tool preferences delivered to real users.

### The Graded Configuration and Run Comparability

Because the prompt is parameterised, a benchmark run only measures the specific configuration passed at start time. From Harness Version 12, `BenchmarkCandidatePromptOptions` records this configuration into `BenchmarkRun.CandidatePromptOptionsJson` and prints it as the *Chat Prompt Under Test* report manifest.

Run 11 (and all prior runs through 11) answered under:
- **Mode:** Gameplay Help (`overseerMode: 0`)
- **Response Style:** Concise (`verboseMode: false` — *"Default to 2–5 sentences per response"*)
- **Tools:** Enabled; **Source code references:** Allowed
- **Web search:** Disabled; **Subagents:** Disabled
- **Spoiler-free mode:** Off; **Active game:** No; **Message history:** No
- **Pre-injected wiki context:** None

> ⚠️ **Comparability Invariant**: Configurations differ in what they measure. Two benchmark runs are strictly comparable on **Completeness**, **Conciseness**, and **Readability** only if their candidate prompt options match.

### Three Empirical Signals Flowing Back to Chat

Harness Version 12 surfaces three concrete analytical signals to guide chat system design:

1. **Tool Routing Analysis (`BenchmarkChatTransfer`)**:
   Aggregates tool calls into five functional families (Source Code, Wiki, Structured Lookup, Knowledge Base, Other) across difficulty bands.
   - *Correlations*: Computes Pearson correlation ($r$) of source tool share against model latency and quality score. A high positive correlation with time indicates tool proliferation latency penalties.
   - *Knowledge Base Under-use*: Highlights questions where the model made zero `get_knowledge_article` calls despite available documentation.
   - *Limit*: `ToolCallSummary` records aggregate tool counts, not execution traces. Whether wiki tools were attempted before source tools is not derivable.
2. **Response-Style Conflict Detection**:
   When `verboseMode: false`, the model is instructed to answer in 2–5 sentences. If Completeness is the lowest scoring dimension by $\ge 13$ points below Accuracy, the harness flags a response-style conflict. This alerts operators that low Completeness may stem from prompt obedience rather than model inability. Running the suite under `verboseMode: true` isolates the model capability.
3. **Knowledge Base Gaps from Refuted Claims**:
   Claims refuted by the claim verifier (`src/...` or wiki citations) are aggregated across runs and projected in the Suite Health panel. These represent verified misconceptions held by frontier models, providing authoritative candidate topics for new knowledge base articles.

### The Standing Rule: Motivation vs. Justification

> 🛑 **Prompt Modification Bar**: A benchmark finding may **motivate** a chat prompt change, but it can never **justify** one on a single run.

The prompt is the measuring instrument. Altering the prompt based on an isolated observation from a single run invalidates the baseline and risks overfitting to idiosyncratic grader or model behaviors. The standing bar before altering the production chat prompt is:
- A finding reproduced across at least **two independent benchmark runs**, OR
- A deliberate controlled run under an explicitly varied configuration (e.g. comparing `verboseMode: false` vs. `verboseMode: true`).

See the project skill `server_benchmark_to_chat_transfer` for the detailed protocol.

### What Is Measured vs. What Is Unmeasured

The benchmark today covers only a specific slice of Overseer chat capabilities:
- **Measured**: Concise gameplay questions without game context (or with static board context in Harness 8+).
- **Unmeasured**:
  - Conversational multi-turn context (chat history)
  - Pre-injected wiki context (which live chat provides automatically)
  - Spoiler-free mode
  - Web search tool routing
  - Subagent delegation and parallel tasks

Each unmeasured configuration represents an active chat capability operating outside benchmark verification.

---

## 10. Game Snapshots & AI-Generated Questions (Harness Version 8)

Harness version 8 introduces **Game Snapshots** and **AI-Generated Questions Grounded in Live Game State**.

### Terminology

The admin UI, the API and this section say **game snapshot** / **snapshot**. A few places in the
implementation still say "board" on purpose, called out here rather than left silently inconsistent:

- **Identifiers and CSS classes** are unchanged: the snapshot badge's `badge-board` class, the Suite
  Health tab id `board-facts` (`sh-tab-board-facts` / `sh-panel-board-facts`), and method/parameter
  names such as `BoardMetadata`, `PrepareBoardText`, `boardText` and `boardName` in
  `BenchmarkSnapshotImporter` and `AdminBenchmarkController`. None of these reach an administrator.
- **The rubric-format literals `` `**BOARD FACTS**` `` and `` `**SOURCE** — board` ``** are stored
  verbatim inside every AI-generated rubric (`BenchmarkQuestion.ExpectedPoints`), required by
  `BenchmarkGenerationParser` (a generated rubric without `**BOARD FACTS**` is rejected), checked by
  `BenchmarkRubricCheckPrompt`, and parsed by `BenchmarkRubricCitationValidator` under the `**SOURCE**`
  convention. They cannot be renamed without administrators meeting both spellings side by side
  forever: every rubric already stored keeps the old heading, and rewriting stored rubrics to match
  would bump `SuiteItemRevisions` — a **Fundamental** key in `BenchmarkComparabilityKey` — for every
  question on every snapshot-bound suite, severing future runs from past ones. The Generate Questions
  dialog's Authoring Instructions hint and the Suite Health panel's Snapshot facts introduction
  (both below) explain the literal at the point an administrator meets it; this section is the third.
- **The model-facing prompts** (`BenchmarkAssessmentPrompt`, the question-generation prompt, the
  rubric-check prompt, the difficulty prompt) still say "GAME CONTEXT BOARD" and teach the model the
  two literals above. This text never reaches an administrator, and the grader prompt
  (`BenchmarkAssessmentPrompt`) is pinned by `BenchmarkAssessmentPromptTests`; changing its wording is
  a `HarnessVersion` bump with its own comparability round, not a UI terminology fix.
  `CandidateSystemPromptSha256` — the candidate system prompt actually hashed for comparability —
  does not contain the word at all. The one prompt string that *is* admin-visible,
  `BenchmarkGenerationPrompt.DefaultInstructions` (the server-side default shown in the Authoring
  Instructions textarea), does say "snapshot".

Everything else below — capture, the viewer, generation, rubric verification — uses "snapshot"
throughout, matching the UI. Renamed suite names and report/log lines apply only going forward:
existing stored suite names, run reports and job logs already written are not migrated.

### Game Snapshots
A snapshot (`BenchmarkGameSnapshot`) captures the complete, authentic text dump of a live GnollHack
game session.
- **Capture Pipeline**:
  - **Live Game Capture (`ClientRefresh`)**: Retained for the snapshots already captured this way and
    for `BenchmarkSnapshotImporter.FromClientTextAsync`, which `BenchmarkSnapshotImporterTests`
    exercises directly. The chat UI offers no live capture: there is no dedicated capture button and
    no `snapshots/capture` endpoint. A live snapshot now reaches a benchmark by being attached to the
    chat and then saved through the path below.
  - **Session Attachment Capture (`SessionAttachment`)**: Captured from an existing game snapshot
    attached to the chat session (the **Save Attached Game Snapshot** button in the chat header). This
    extracts the newest attached snapshot text directly from the session's system messages without
    requiring a 45-second round trip to the native game client.
  - **Server Upload (`ServerUpload`)**: A raw HTML game dump, sanitized by `DumpHtmlSanitizer.Sanitize`.
    `POST snapshots` creates a new `Snapshot: <name>` suite bound to it and has no UI; the suite card's
    **Upload Snapshot** (below) stores an HTML dump under this method too, attached to that suite.
  - **Text Upload (`TextUpload`)**: A `.snapshot.txt` as the Snapshot Viewer downloads it, or any
    already-flattened snapshot text, uploaded from the suite card's **Upload Snapshot**. Line endings
    are unified and the text runs through `NormalizeFlattenedText`, exactly as a text edit does, so
    re-uploading a downloaded snapshot reproduces its SHA-256.
  - **YAML Import (`YamlImport`)**: The board carried by a suite YAML's `suite.snapshot` mapping,
    attached by `POST suites/import` (see *YAML Import and Export*). Text or HTML is decided by the
    same `LooksLikeHtml` rule as an upload. An identical stored snapshot that belongs to no suite is
    reused rather than copied; one that belongs to another suite is copied, because a board has one
    owner.
- **Upload Snapshot** (suite card, `SnapshotUploadDialogComponent`): `POST suites/{id}/snapshot`
  attaches a board built from the uploaded file to **that** suite and creates no suite. The body is
  `{ name, content, contentKind, notes?, sourceGnollHackVersion?, replaceExisting }`. `contentKind`
  is `Auto` (default), `Text` or `Html`; `Auto` treats content as HTML when, after trimming, it
  starts with `<` and contains `<html`, `<body` or `<pre` (case-insensitive). Content over 4,000,000
  characters is refused; the stored text is capped at 60,000 as for every capture. A suite that
  already has a snapshot gets `409` unless `replaceExisting` is true: the dialog shows a warning
  block naming the current snapshot, and *Upload Snapshot* opens a confirmation naming the old and new
  snapshot before it sends `replaceExisting: true`. A confirmed replacement deletes the old board in
  the same save that attaches the new one. Name disambiguation runs before the old board is removed,
  so re-uploading under the old board's exact name stores `<name> (2)`. The card's button is
  `aria-disabled` while a question generation job runs on that suite, because the job re-reads the
  board by id.
- **Sanitizer Convergence**: All capture paths pass snapshot text through
  `DumpHtmlSanitizer.NormalizeFlattenedText` before persistence. This normalizes line endings
  (`\r\n` / `\r` to `\n`), strips trailing whitespace per line, strips terminal backticks/triple
  backticks, rejects empty text, computes a canonical SHA-256 digest, and enforces the 60,000
  character hard cap with an explicit truncation marker (`[SNAPSHOT TRUNCATED at 60000 characters.]`).
  `PrepareBoardText` is **idempotent**: text that is already exactly the cut plus that marker is
  left alone rather than cut and marked a second time, which is what lets a truncated snapshot
  survive an export/import or a download/re-upload with its hash intact — and therefore be matched
  at all. The editable-text path below (Immutability & Safety) runs the same normalization, so a
  round-trip of unchanged text yields the same hash.
  Boards saved from a chat session before `NormalizeFlattenedText` unified line endings went through
  a normalizer that kept CRLF, so they may still hold carriage returns and a hash that differs from
  a YAML export or a `.snapshot.txt` re-upload of the same text. For such a board the Snapshot
  Viewer's **Game Snapshot** tab shows a notice with a **Normalize line endings** button, which saves
  the stored text unchanged through `PUT snapshots/{id}/text`; the server unifies the line endings,
  re-hashes the board and rebuilds its digest. The button is unavailable while the text editor holds
  unsaved changes.
- **Provenance Tracking**: Each snapshot records `CaptureMethod`, `SourceChatSessionId`,
  `SourceGnollHackVersion`, `Notes`, `DigestText`, and `CapturedAtUtc`.
- **Digest**: `DigestText` is a **deterministic extract** of the snapshot, built by
  `BenchmarkSnapshotDigestBuilder` from the snapshot's own labelled sections — the preamble (version
  banner, dates, character line), `Status:`, `Background:`, `Current Status:`, the last few
  `Latest messages:`, `Pets:`, `Notable locations:` and `Inventory:`, each with a line cap that
  reports how many lines it dropped. The map grid, the symbol legend, the skills, spells,
  discoveries, game log, conducts and dungeon overview are deliberately omitted, and the whole
  extract is capped at **6,000 characters**. A dump carrying none of those headers falls back to its
  leading characters cut at a line boundary, so no capture path yields an empty digest.
  - **Who reads it**: the **difficulty assessor only** (`BenchmarkDifficultyPrompt`), which runs in
    batches of four questions and would otherwise carry a 60,000-character snapshot in every call.
    The prompt tells the assessor what the digest omits. Every other reader — the question
    generator, the grading assessor, the claim verifier, the rubric checker and the candidate model —
    receives the full `SanitizedText`.
  - **Editing and regeneration**: an administrator can hand-edit the digest field in **Edit Snapshot
    Metadata**, or rebuild it from the current snapshot text with **Regenerate from snapshot**
    (`POST snapshots/{id}/regenerate-digest`), which never touches the snapshot text. Saving a text
    edit through the **Editor** tab (below) always rebuilds the digest too — overwriting a hand-edited one
    — because the digest must describe the text that is actually stored.
  - **Snapshots captured before this change keep their old prefix digest** — the first 2,000
    characters of the snapshot — until an administrator regenerates or edits them. There is no
    automatic backfill.
- **Automatic Name Disambiguation**: Duplicate snapshot names are automatically disambiguated with an
  incrementing numeric counter suffix (e.g. `Name (2)`, `Name (3)`) rather than rejected with an
  error, keeping the bound question suite name synchronized with the snapshot. A snapshot-bound suite
  created from a fresh capture from now on is named `Snapshot: <name>`; suites created before this
  change keep their stored name — nothing is migrated.
- **Chat UI Controls & Gating**: Two icon buttons, both in the chat header immediately to the right
  of the rename (pen) button. One requires an attached snapshot and the other requires that there be
  none, so they can never appear together.
  - **Attach Game Snapshot**: Displayed whenever the chat runs embedded inside the GnollHack game
    client (`clientBridge.isEmbedded()`) and no snapshot is attached yet (`!hasGameSnapshot`) —
    including in a brand-new chat, where attaching creates the session. It fetches the snapshot over
    the client bridge and attaches it to the chat; it does **not** create a stored snapshot or a
    suite.
  - **Save Attached Game Snapshot**: Displayed when an active, non-ephemeral session has an attached
    snapshot (`user.isAdmin && currentSessionId && hasGameSnapshot && !isEphemeralSession`), including
    in an ordinary browser session, because it reads the snapshot text from the session's own stored
    messages. This is the only route from the chat to a benchmark snapshot; saving again creates
    another stored snapshot rather than updating the first.
  - Only **Save Attached Game Snapshot** is gated by administrator privileges (`isAdmin`);
    **Attach Game Snapshot** is available to any embedded user. Neither depends on `ShowDebugLog` or
    any build configuration flags.
- **Immutability & Safety**: No run or answer holds a foreign key to a snapshot — a run copies the
  snapshot's name, SHA-256, size and capture method by value — so deleting a snapshot
  (`DELETE snapshots/{id}`, the viewer's **Delete Snapshot**) detaches it from its suite and keeps
  every question, run and recorded snapshot fact. Snapshot text is no longer immutable after creation: an administrator can edit it directly
  through the **Game Snapshot** tab (described under Snapshot Viewer UI below), which calls
  `PUT snapshots/{id}/text`. That endpoint runs the text through the same
  `DumpHtmlSanitizer.NormalizeFlattenedText` every capture path uses, which unifies CRLF/CR line
  endings to LF, applies the
  60,000-character cap and truncation marker through `BenchmarkSnapshotImporter.PrepareBoardText`,
  and returns `400` for text that normalizes to nothing. It takes the `ExpectedSha256` the client
  loaded and returns `409` when the stored hash has since changed, so two administrators editing the
  same snapshot get a conflict message instead of a silent overwrite. A successful save rebuilds
  `Sha256`, `CharCount` and `DigestText` from the new text — **overwriting a hand-edited digest** —
  and stamps `ModifiedAtUtc`. It leaves `CaptureMethod` and the bound suite's own description
  unchanged, so the suite description's provenance text (character count, hash prefix) goes stale
  after a text edit and is not refreshed automatically. Other fields (`Name`, `SourceGnollHackVersion`,
  `Notes`, the digest text) are still edited separately, on the **Metadata** tab.

### Authoring a Suite from an Exported AI Snapshot

A suite YAML is a **complete, one-file description of a snapshot suite** (see *YAML Import and
Export*), so a whole snapshot suite can be authored offline, with Overseer not running, and
imported in one step.

**The Snapshot Suite Wizard** (`app-snapshot-suite-wizard`, `snapshot-suite-wizard.component.ts`),
a `.btn-ghost` with the `compass` glyph on the Manage Suites toolbar after *Import Suite from YAML*,
walks both routes and imports the agent's file itself.

| Route | The board is… | The agent reads / writes | The import |
|---|---|---|---|
| **A** — *Attached to a suite in Overseer* | stored on a suite that has a snapshot | the suite YAML the wizard downloads, `overseer-suite-export-<slug>.yaml` / `agent-new-questions-<slug>.yaml` | the **questions import**: adds the questions; the description changes only if the admin applies the suggested one. No server change was needed. |
| **B** — *A snapshot file from GnollHack* | an `.ai.html` or `.snapshot.txt` on disk | that file / `agent-new-suite-<slug>.yaml` | the **suite import**: creates a new suite with the snapshot attached |

The names follow one rule: `overseer-…-export` was written by Overseer, `agent-new-…` was written by
the agent and is the one to upload. The first token differs, so the two files sort apart in a folder
and stay distinguishable when a file dialog truncates the name. Nothing parses a file name on
import, so a file written under an older name still imports; only the upload step's advisory —
shown when the attached file is named like the suite download — depends on it.

Its steps are *Source* (the route, with no default, and for A a list of the suites with a
snapshot, empty ones first), *File*, *Prompt* (`app-suite-prompt-builder`, which takes the route
and path as inputs), *Upload*, *Review and confirm* (the import panel with an `ImportExpectation`,
see *YAML Import and Export*), *Assess* (**Assess Difficulty**) and, for route A only, *Describe*.
*Assess* reports the remaining unassessed count, and once the reloaded suite list shows every
question assessed it says **Done.**, marks the step with a check in the indicator and announces the
completion once through a live region; the step indicator marks step 6 and 7 by their outcome, not
by having been passed, so a skipped assessment stays visibly unfinished. *Describe* shows the file's
`suite.suggested_description` in the shared Markdown editor, opened in Split, beside the suite's
current description, and **Apply Suggested Description** writes it with `PUT suites/{id}` (the
fresh suite name is sent, as the endpoint requires one). When the file carried no suggestion, the
step says *"No suggested description was included in the YAML file"* and offers an empty editor for
the text the agent repeated in its handoff; the review step already warned about it. A disclosure above the steps holds the whole workflow
as a copyable, downloadable checklist (`suite-workflow-instructions.ts`), generic until the details
are known. Every forward button is `aria-disabled` while its step is incomplete, and pressing it
then moves focus to what is missing; Escape and Close are refused while an import is applying.
Only the difficulty assessor and the suite help open above the wizard.

**The description rules.** Both prompts carry one paragraph on how to write the description
(`descriptionAuthoringLines` in `suite-agent-prompt.ts`): route A's `suite.suggested_description`,
route B's `suite.description`. It follows `BenchmarkDescriptionPrompt.DefaultInstructions` in
`Overseer/Services/Benchmarking/BenchmarkDescriptionPrompt.cs` — 120 to 300 words, a lead paragraph
with the per-band counts, a `### Covered Domains` list, no quoted question and no answer — so an
agent-written description has the shape of one **Generate description** drafts. Route A adds that
the text replaces the description and so describes the whole suite after the import, that
`suite.description` stays unchanged, and that the handoff repeats it. The two copies cross-reference
each other in comments; no test links them. The `server_snapshot_suite_authoring` skill (§ 6a)
states the same rules.

**The path constraint.** A web page never learns where a file is on disk: a file picker gives it
a name and contents, and a download's destination is invisible. So route A downloads the suite YAML
and then asks where it was saved, route B's file picker is only a local **checker**
(`snapshot-file-check.ts`: HTML or text by the server's `LooksLikeHtml` rule, a GnollHack banner,
the 60,000-character cap as an advisory — nothing is uploaded, nothing blocks), and in both the
admin pastes the full path. Uploading the agent's *output* has no such problem.

**Persistence.** `{ v, route, suiteId, sourcePath, suiteName, counts, waitForGoAhead, step,
importedSuiteId, suggestedDescription, agentFileName }` is kept under `localStorage['overseer.snapshotSuiteWizard']`, written on every
step change and removed on **Done** and **Start over**; every access is wrapped in `try/catch`.
Opening with saved state offers **Resume** / **Start over**. The uploaded YAML is never stored, so
a resume inside the import lands on *Upload*, and a resume after a successful import on *Assess*,
or on *Describe* when that was the saved step. The last two fields are optional, so a state saved
before they existed still resumes.
A saved suite that no longer exists or no longer has a snapshot discards the state with a one-line
explanation, as does a different `v`.

The workflow, route B:

1. **In GnollHack**: game menu → **Developer** → **Export AI Snapshot** (visible with Developer
   Mode and debug log messages on). It writes `gnollhack.<name>.<…>.ai.html` — despite the
   extension, plain monospace text in an HTML wrapper — and opens the OS share sheet. Overseer is
   not involved, and the standalone export is **not** truncated, so it can exceed 60,000
   characters. A `.snapshot.txt` downloaded from the Snapshot Viewer works just as well.
2. **In the wizard**, choose the file route, optionally check the file, and paste its path.
3. **In an agent session** with the repositories on disk — Claude Code, Antigravity or similar —
   paste the prompt the wizard generates (`Mode: create a new suite`, `Snapshot file:`).
4. The agent reports its **count table**, then writes **one file**, `agent-new-suite-<slug>.yaml`,
   beside the snapshot.
5. **Back in the wizard**: upload it → the review shows the outcome, the checks, the suite, what
   happens to the snapshot and every question → tick the confirmation → **Create Suite {name}** →
   **Assess Difficulty**. Optionally Suite Health *Snapshot facts* and the citation check.

Route A is the same with the suite chosen on the first step, **Download Suite YAML** in place of
the export, a prompt with `Mode: add questions to an existing suite` and `Suite file:`, a
`agent-new-questions-<slug>.yaml` from the agent, and **Add N Questions to {suite}**. The agent
keeps the `suite` block verbatim, gives no question an `id`, and suggests a description, which the
import does not change.

**How many questions.** The board decides, not a target. The agent surveys the board's distinct
*decisions* — one candidate question each — classifies them with
`BenchmarkRubricAuthoringGuidance.BandDescription`, and caps the total at
`Benchmark:Compliance:MaxQuestionsPerSuite`. The default target is 18, the sensible range 12–24,
and the split moves toward equal thirds (6 / 6 / 6) by trimming the largest band rather than
promoting a question. Padding is never free: every question costs a candidate call plus one or two
assessor calls on every run.

**Why rubrics are grounded in the flattened text.** Suite Health's *Snapshot facts* check demands a
verbatim quote from the **stored** snapshot for every BOARD FACTS claim, and the stored snapshot is
the flattened, normalized, capped text — not the HTML the game exported. An agent that grounds in
the raw HTML writes facts the check cannot find.

**The canonical procedure is the skill**, `.agents/skills/server_snapshot_suite_authoring/SKILL.md`
(invocable as `server-snapshot-suite-authoring`). It owns the flattening steps, the count rule, the
rubric rules and the self-check; the generated prompt names it **by name, not by path** — under the
invocable name and the canonical underscore name both — and `suite-agent-prompt.spec.ts` pins
those two names, so renaming the skill must change `SKILL_NAME` and `SKILL_CANONICAL_NAME` in
`suite-agent-prompt.ts`.

### Suite Card Actions
Each bound suite's card in Manage Suites shows one primary action and a row of secondary ones: a
gold `.btn-gh` **Manage Questions** button, followed by a `.btn-ghost` outlined row — **Upload
Snapshot** (on every card; see Game Snapshots above), **Generate
Questions** (only while the suite has a bound snapshot), **Assess Difficulty**, **Suite Health**,
and, once the suite has generated questions, **Check Rubrics** and, while any question is still
unreviewed, **Verify All**. A `.btn-ghost-danger` **Delete Runs** sits at the end of the row once the
suite has recorded runs. The snapshot badge — a map-icon button naming the snapshot and its
character count, with an interest-triggered tooltip — is the entry point into the Snapshot Viewer; it
replaces the previous dedicated "View Board" row button.

### Snapshot Viewer UI
`SnapshotViewerComponent` (`Overseer/ClientApp/src/app/shared/snapshot-viewer/`) is a full-screen
dialog for reading a 60,000-character fixed-width dump, checking claims about it — the
administrator's own, the AI's in a chat, and the rubric checker's verbatim quotes — and editing it
and its metadata directly. It has three tabs: two editor pages and a page for deleting the snapshot.
The DOM-free text logic (map detection, sections, line-number formatting) lives in `reader-text.ts`
beside it.
- **Layout**: A `.gh-dialog-fullscreen` dialog whose body sits under a tab row — **Game Snapshot**,
  **Metadata** and **Delete** — built on the shared `.gh-tabs` widget with its full ARIA contract
  (`tablist` / `tab` / `tabpanel`, roving `tabindex`) and keyboard model (arrow keys wrap, Home and End
  jump). All three panels stay in the DOM and the inactive ones are `hidden`, so an edit on either
  editing tab survives switching to another, and switching never asks. The dialog body itself never
  scrolls: only the CodeMirror scrollers do, so the dialog footer stays in view. Each editor frame is
  size-contained (`contain: size`) with a minimum height, so a page's minimum height is its fixed parts
  plus that minimum, never the document's length; only a viewport shorter than that makes the panel
  scroll as a whole. The dialog opens on **Game Snapshot** every time, and closing it destroys both
  editors, so a reopened snapshot always gets fresh ones.
- **Footer**: One footer under the body, shown on the two editing tabs and absent on **Delete**,
  serves both editing tabs. On the left a single polite live region (`role="status"`) shows, in
  priority order, the last error, a success message for two seconds, or what is unsaved (*Unsaved
  changes: snapshot text*, *…: metadata*, or *…: snapshot text and metadata*). On the right,
  **Revert** and **Save Changes** act on everything unsaved on both tabs, whichever tab is showing;
  both are `aria-disabled` until something is unsaved, and while a save is in flight. The header close
  button and Escape are the ways out of the dialog.
- **Game Snapshot page**: `SnapshotTextEditorComponent` (`snapshot-text-editor.component.*`), a
  lazily-loaded **CodeMirror 6** editor. `codemirror-setup.ts` and `codemirror-map-tools.ts`, which
  only it imports, are the only modules that import `@codemirror/*`; the editor components
  `import()` the first on mount, so CodeMirror ships as its own chunk and never reaches the initial
  bundle. CodeMirror is created outside the Angular zone, so pointer tracking does not run change
  detection; only its callbacks re-enter the zone.
  - **Monospace rendering**: Snapshot text is **never rendered as Markdown**. Game dumps contain
    NetHack map symbols (`#`, `|`, `-`, `*`) that Markdown parsers mangle. The editor uses a monospace
    font stack with `font-variant-ligatures: none` (a ligating coding font would draw two map cells
    such as `--` as one glyph), a dark theme, line numbers and an active-line highlight.
  - **Toolbar**: the line count, a live `{{ length }} / 60,000 chars` counter that warns once the
    buffer exceeds the server's cap, and the loaded snapshot's SHA-256 (first 12 characters); then
    **Jump to section** (a `<select>` of the dump's labelled sections — `Map:`, `Map grid:`,
    `Inventory:` and so on — with their line numbers, which scrolls to and places the cursor at the
    chosen line), **Find** (CodeMirror's own find/replace panel, also on **Ctrl+F**, restyled to the
    `.gh-input` / `.btn-ghost` look), **Copy Text**, **Copy with line numbers** and **Download
    .snapshot.txt**. A second row lists the shortcuts — **Alt+G** go to line, **Ctrl+S** save, and
    **Ctrl+M**, which toggles Tab between indenting and moving keyboard focus out of the editor — and
    holds the two view toggles.
  - **View toggles**: *Line numbers* (default on) and *Wrap long lines (prose only)* (default off)
    reconfigure CodeMirror compartments, and are remembered in `localStorage` under
    `overseer.snapshotReader.lineNumbers` and `overseer.snapshotReader.wrap`; if storage is
    unavailable the defaults apply. Wrapping is CodeMirror's own `EditorView.lineWrapping`, so its
    height measurement knows lines wrap; the map rows and rulers carry line classes that keep them
    `white-space: pre`, because a wrapped map row misstates the map.
  - **Map tools**: Active when the text contains the `Map grid:` block `dump_map_ai()` writes (see the
    board format in `GnollHack/src/detect.c`). The map rows carry `.cm-map-row`, the two column-ruler
    lines `.cm-map-ruler` (dimmed), and the hero's cell, taken from the legend's "The hero is at
    <x,y>" sentence, is highlighted gold. `y` is read from each row's gutter, never from line order,
    so a trimmed or missing row cannot shift it. The map is re-detected at once when an edit touches
    the map block, shifted when lines are added or removed above it, and otherwise re-detected 400 ms
    after the last edit. A status bar under the editor shows `<x,y>` and the symbol of the map cell
    under the pointer, or, when the pointer is not over the map, of the cell at the caret; blank cells
    are described as unseen or rock. While a coordinate is shown, an icon button beside it copies
    `<x,y>` — the way to copy a coordinate is to click the cell, which places the caret there, and
    then that button.
  - **Copy**: *Copy Text* copies the editor buffer, unsaved edits included, since that is what is on
    screen. *Copy with line numbers* copies the lines of the selection, or every line when nothing is
    selected, as `L{n}: text` with the numbers padded so the colons align; a selection ending at the
    very start of a line does not include that line. Both announce the result in the status bar.
  - **Saving**: **Save Changes** in the footer, or **Ctrl+S** in the editor (which acts only while
    the buffer is dirty, and then saves the metadata too), saves everything unsaved on both tabs. The
    metadata is validated first — a name is required, and the digest must fit 6,000 characters — and
    when that fails nothing is sent: the Metadata tab opens with focus on the offending field and the
    footer names the problem (*Metadata: Snapshot name is required.*). Then, in this order, a dirty
    buffer is sent to `PUT snapshots/{id}/text` with the `sha256` the dialog loaded as
    `expectedSha256`, and dirty metadata to `PUT snapshots/{id}`. The order matters: the text save
    rebuilds the digest server-side, and the metadata save writes the digest the form holds, so a
    hand-edited digest overwrites the rebuilt one instead of being lost to it; with no digest edit the
    form already holds the rebuilt digest and sends it back unchanged. After a text save the editor
    stays on the tab, the server's normalized text becomes the new saved document (replacing the
    buffer, unless it was edited again while the save was in flight), and the Metadata page's SHA-256
    and digest field follow — the digest field only if it holds no unsaved edit of its own. The footer
    then shows *Saved. SHA-256 and digest updated.*, or *Saved.* when only metadata was saved. The
    two requests are not atomic, and there are three ways the save can stop part-way: a failed text
    save (a `409` when someone else changed the text, say) sends no metadata request and shows
    *Snapshot text: <message>*, leaving both edits in place; a metadata save that fails after the text
    save succeeded shows *Snapshot text saved. Metadata: <message>* — a name already taken by another
    snapshot is the usual case — and leaves the metadata edit in place, so Save Changes retries only
    that; and a failed metadata-only save shows *Metadata: <message>*.
  - **Download**: *Download .snapshot.txt* always serves the text saved on the server, through a
    same-origin link with a `download` attribute (`<name>.snapshot.txt`, the name reduced to
    `[A-Za-z0-9._-]`), so no tab opens. When the editor holds unsaved changes it first asks in a nested
    `.gh-dialog` confirmation — **Cancel**, **Download saved text**, or **Save and download**, which
    saves the buffer and downloads once the save succeeds; on a failed save nothing is downloaded and
    the footer shows the error. The link click, unlike `window.open`, is not treated as a popup when it
    runs after the save's response.
- **Metadata page**: One form. At the top, a compact provenance strip: capture
  method, size, capture time, **Modified** (once the snapshot has actually been edited, not merely
  stamped at creation), source chat, and the SHA-256 with its copy button. Below it the editable
  fields — **Snapshot Name**, **GnollHack Version**, **Notes** (an autosizing textarea capped at six
  lines) — and the **Snapshot digest** CodeMirror field, which takes the remaining height and scrolls
  internally. The digest field's **Regenerate from snapshot** button rebuilds the extract
  server-side and puts it straight into the field; it is disabled while the request is in flight and
  the snapshot text is never touched. A field that differs from the saved snapshot makes the
  metadata unsaved; the dialog footer saves and reverts it.
- **Delete page**: A *Delete snapshot* card holding a static `.alert-danger` warning (no
  `role="alert"`: it is content, not an event) — *This cannot be undone.* — and a list of what
  deleting does: the snapshot, by name, is deleted permanently; it is detached from its suite, when it
  has one; questions and runs are kept, and runs keep the snapshot facts they recorded; Generate
  Questions and Check Rubrics are unavailable for the suite until a new snapshot is uploaded. While
  either editing tab holds unsaved changes a line says they will be lost (*Unsaved changes to the
  snapshot text and metadata will be lost.*). Below it, **Delete Snapshot** (a full `.btn-gh-delete`,
  the page's only action) is `aria-disabled` while a save is in flight, so it stays discoverable. It
  opens a nested confirmation, with focus on **Cancel**, restating the consequences in one paragraph.
  A confirmed delete calls `DELETE snapshots/{id}`, emits `snapshotDeleted`, and closes the viewer
  without asking about unsaved edits; a failure keeps the confirmation open with the server's
  message. While a question generation job runs on the suite the button is replaced by that
  explanation. The page has no footer.
- **Unsaved changes**: A tab holding unsaved changes shows a small gold dot after its label, and
  *(unsaved changes)* in visually hidden text, so the state is part of the tab's accessible name and
  does not rest on colour and shape alone. Closing the dialog — the header close button or Escape —
  with unsaved changes on either tab shows an inline strip under the tab row in place of a
  `confirm()` popup, naming what is unsaved (*Discard unsaved changes to the snapshot text?*, *…to
  the metadata?*, or *…to the snapshot text and metadata?*), with **Keep editing**, which returns
  focus to the active tab's editor, and **Discard**, which closes the dialog. The footer's **Revert**
  acts at once when only the tab in view is unsaved; when it would also discard something on another
  tab, the same strip asks first (*Revert unsaved changes to the snapshot text and metadata?*), with
  **Keep editing** and **Revert**, which restores both tabs and returns focus to the active tab's
  editor. One strip serves both questions, so opening one replaces the other, and any save clears it.
  A save in flight on either tab blocks closing.
- **Truncation Notice**: If the snapshot contains the truncation marker, a prominent alert above the
  editor informs the administrator that tail sections of the dump were omitted — at capture, or at a
  later text edit that itself exceeded the cap. Leaving the marker line in place when editing keeps
  the notice showing, correctly: the tail is still missing.

#### Pointing at the Snapshot
The administrator and the AI see the same text, so what they need is a shared way to point at it. In
a rubric's `` `**BOARD FACTS**` `` section, prefer `<x,y>` coordinates — the snapshot's own notation,
which it instructs the AI to use — and verbatim quotes, the form the rubric checker already returns:
both survive a re-capture, or a text edit, of the snapshot. Line numbers depend on the exact capture
(or the last edit), so keep them for administrator-to-AI chat and notes; the AI receives the text
without numbers but can count lines, and *Copy with line numbers* produces a passage whose numbers
match the reader's.

### AI-Generated Benchmark Questions
Administrators can generate benchmark questions tailored to a specific game snapshot using any
benchmark-capable AI configuration.
- **3 Difficulty Bands**: Questions are generated in 3 separate prompts:
  - **Simple** (Default: 6 questions, authored difficulty: `Simple`)
  - **Intermediate** (Default: 6 questions, authored difficulty: `Intermediate`)
  - **Advanced** (Default: 6 questions, authored difficulty: `Advanced`)
- **Strict Grounding & Rubric Structure**: Every generated question must be unanswerable without the
  snapshot. Every generated rubric must contain:
  1. `` `**BOARD FACTS**` ``: Point-by-point factual claims verified against the snapshot text. The
     section keeps this fixed name in every stored rubric — see Terminology above.
  2. `**REQUIRED**`: Essential points an answer must make to receive credit.
  3. `**ACCEPTABLE**`: Valid variations, alternative phrasings, or equivalent actions.
  4. `**UNACCEPTABLE**`: Incorrect assertions, lethal actions, or contradictions of snapshot facts.
- **Human Review Discipline**:
  - All AI-generated questions are flagged `IsGenerated = true` and initially `IsReviewed = false`.
  - Content revisions automatically increment `ItemRevision` via `BenchmarkQuestionAssessment.Clear`,
    resetting reviewed status if `ReviewedAtRevision != ItemRevision`.
  - Unreviewed questions display warning badges in the Admin UI.
  - Benchmark reports display a prominent warning banner whenever a run includes unreviewed generated
    questions, disclosing the number of unverified items.
  - A **Verify All** button (in the suite card's action row — see Suite Card Actions above) lets an
    administrator attest that all questions have been reviewed against the game snapshot.

### AI Rubric Verification (Snapshot Facts Tab in Suite Health)
To assist human review, the Suite Health dialog provides a dedicated **Snapshot facts** tab (the tab
id `board-facts` is unchanged — see Terminology above). Its introduction explains the
`` `**BOARD FACTS**` `` literal at the point the administrator meets it: the checker verifies that
section of each question's rubric against the bound snapshot, and the section keeps its fixed name in
every stored rubric.
- **Verifiable Quote Discipline**: The checker model evaluates every factual claim in a question's
  rubric against the game snapshot. Every claim assessed as `supported` must be accompanied by a
  verbatim quote from the snapshot text.
- **Findings & Verdicts**: If any claim is `contradicted` or `not-in-board` (verdict identifiers,
  unchanged), the question verdict is flagged as `unsupported`. The findings table displays the
  rubric claim, the assessment badge, the exact **Snapshot Evidence** quote, and explanatory
  reasoning.
- **One-Click Corrections**: Administrators can jump directly from a finding card into the question
  editor to refine the rubric or verify the question.

### AI-Generated Suite Descriptions
The **Edit Benchmark Suite** dialog has a **Generate with AI** button at the end of the description
editor's label row.
It is disabled while creating a suite: a suite without an id has no questions to describe, so save it
first.
- **The dialog**: pick a benchmark-capable model (the picker remembers the last model chosen in this
  browser via `localStorage`, falling back to the run panel's assessor when that model is no longer
  benchmark-capable), optionally edit the instructions (prefilled with
  the default brief: a 120–300 word Markdown description with a lead paragraph and a
  `### Covered Domains` list), and choose whether to include the game snapshot (shown only when the
  suite has one; default on). **Generate** runs one model call and shows an indeterminate progress bar
  with an elapsed timer; **Cancel** aborts the request. The result shows the description in a
  **Preview** tab rendered as HTML, with a **Markdown** tab showing the source, and a stat
  strip: elapsed time, time to first token, prompt, cached, output and reasoning tokens, and the USD
  cost (`—` when no price card resolves, never `$0.0000`).
- **What the model sees**: the suite name, every question's text and authored difficulty band in
  order, the per-band counts, and — when included — the snapshot's full sanitized text. All of it is
  fenced as untrusted reference data. **Rubrics are never sent**, so the description cannot leak
  answer keys.
- **Nothing is saved by the server.** **Use this description** writes the text into the suite's
  description editor; it is persisted only by **Save Suite**. Closing the generation dialog with an
  unapplied result asks first, and closing the Edit Suite dialog (Cancel, header close or Escape) with
  an edited name or description asks before discarding the changes.
- **Endpoint**: `POST /api/admin/benchmark/suites/{id}/description-generation`, synchronous like
  coverage analysis, and gated by the same spend caps (429 when the compliance guard refuses).
- **Output limit**: `Benchmark:DescriptionMaxOutputTokens` in configuration, default 4096.
- **Thinking blocks**: a model that emits its reasoning as `<div class="ai-thought">` markup has
  that block removed before the fence is unwrapped, so neither the preview nor **Use this
  description** carries it. The raw response in the diagnostics keeps it.
- **Usage accounting**: each completed call writes one `SystemAiUsageLog` row with `RoleContext = 7`
  (Suite Description). A request cancelled before the provider reported usage is not recorded.
- **Diagnostics**: the collapsible diagnostics panel has a copy button, and its text carries the
  suite, generator, instructions, usage and a structured log with raw-response excerpts. The full
  prompt and raw response are included only when **Include prompt and raw response in diagnostics**
  is ticked before generating. No API key is ever included.

