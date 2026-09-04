# Overseer AI Intelligence Benchmark Feature

The AI Intelligence Benchmark subsystem in Overseer provides automated, reproducible evaluation of AI models against domain-specific roguelike game knowledge, spoilers, monster/item stats, and C codebase logic for GnollHack.

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

It presents the run as the **two** sequential stages the executor actually performs:

1. **Collecting and assessing answers** — every question is answered, in parallel up to `MaxParallelQuestionsUsed`, and each answer is assessed immediately after it is produced.
2. **Synthesis and scoring** — the holistic report and final indices are produced.

This is one stage, not two, because `BenchmarkService.RunAsync` pipelines the per-question assessment behind each answer inside the same loop, in both the sequential and the parallel branch. (The one exception is a credential collision between the candidate and assessor configurations, which serialises the assessments behind all the answering; that still falls inside stage 1.) The stage is derived client-side from `BenchmarkRunDetailDto`: stage 1 holds while any answer is missing **or** any `AssessmentStatus` is not terminal. Two determinate progress bars — Answers and Assessments — stay visible in both stages, so a full Answers bar during assessment does not read as a hang.

The per-question list merges the suite's questions (fetched once when the dialog opens) with the run's answers, and distinguishes three pre-answer states:

- **Pending** — the question has not been dispatched.
- **Answering** — the request has been sent to the provider and no reply has arrived yet.
- **Answered / Scored** — an answer row exists.

`BenchmarkService` creates a `BenchmarkRunAnswer` only after the model replies, so in-flight state is not derivable from the answers alone: it comes from `BenchmarkRunManager`, which records the order indexes currently in flight (`MarkQuestionInFlight` before the provider request, cleared in a `finally`) and exposes them as `BenchmarkRunDetailDto.InFlightOrderIndexes`. The list is empty for any run that is not the current, still-running one, so a completed run or a restarted server reports nothing rather than stale state.

Diagnostics are assembled **client-side from the run detail**, not from a server-side log — `BenchmarkRunManager` keeps none, and the DTO already carries the run manifest, token counters, flags, and per-question error and HTTP status codes. Answer text, thought text, and assessor comments are deliberately excluded: they are long, model-generated, and already reachable through the run detail dialog and the Markdown report.

Reloading the admin page mid-run restores the banner via `GET /api/admin/benchmark/runs/active`. The dialog is **not** auto-opened on load, so it never steals focus from work in progress.

---

## 2. BARS Rating Scales & Score Mapping

Levels are scored on a 7-point scale mapped non-linearly to 100 points:

| Level | Score | Anchor Definition |
|---|---|---|
| **0** | `1` | Completely incorrect, nonsensical, irrelevant, or fabricated. |
| **1** | `15` | Major inaccuracies with isolated correct fragments; misleading. |
| **2** | `35` | Partially correct but significant errors or critical omissions. |
| **3** | `55` | Mostly correct; minor inaccuracies, omissions, or slight hallucination. |
| **4** | `72` | Fully correct and clear; covers standard gameplay/code accurately. |
| **5** | `87` | Comprehensive and insightful; accurate C macro/logic understanding. |
| **6** | `100` | Flawless, authoritative, concise, and perfectly formatted. |

### Speed Scoring Profiles
Response speed is graded relative to a target latency and decay factor using:
$$\text{Speed} = \text{clamp}\left(100 - k \cdot \log_2\left(\frac{\text{ModelTime}}{Target(q)}\right), 1, 100\right)$$

where $\text{ModelTime}$ is the turn duration with harness tool I/O removed, and $Target(q)$ scales with assessed difficulty. Note it is **not** `DurationMs`: on a tool-heavy question the two differ by the whole tool time.

Overseer supports configuring scoring profiles to accommodate different agent architectures:
1. **Standard Intelligence Index** (Default / Interactive Agent Profile):
   - **Target Latency (`SpeedTargetMs`):** 15,000 ms (15 s)
   - **Decay Factor (`SpeedDecayK`):** 20.0
   - **Max Parallel Questions:** 1 (Sequential, strict timing)
   - **Intended Use:** Standard conversational models and interactive agents where rapid turn completion is desired. At assessed difficulty 0 a 15-second response yields 100 points, decaying to 80 at 30s, 60 at 60s, and 40 at 120s; difficulty raises the target proportionally, so a difficulty-50 question is scored against 22.5 s rather than 15 s.

> These two constants are pinned by the invariants documented on `BenchmarkScoringConstants`, and `BenchmarkScoringTests` fails the build if they are changed without re-deriving the per-band timeout margins. **This section previously documented 5,000 ms and k = 25.0**, which is what the constants were before the floor-versus-timeout analysis; the seeded default has been 15,000 ms and k = 20.0 since, and 15,000 is what reproduces published run scores.
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
- **Second opinion.** A run may name a **second opinion assessor**, selected in the start dialog like every other model and recorded on the run (`BenchmarkRun.SecondOpinionAssessorModelConfigurationId` plus the usual snapshot columns). It is optional and off by default; when none is selected, no answer is re-graded. There is deliberately **no fallback to the run's own assessor** — a model checking its own verdict produces agreement, not a second reading. The trigger is a critical error, or a quality score below the scoring profile's `SecondOpinionQualityThreshold` (default 50; `0` disables the score trigger and leaves second opinions to critical errors alone). Both verdicts are kept. The **first stays authoritative for scoring**; a material disagreement — more than 15 quality points, or a split on `criticalError` — sets `SecondOpinionDisagreed` and surfaces as a `DISPUTED` badge and a Disputed Assessments report section, for a human to settle with the existing re-assess action.

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
- **The timeout is coupled to the speed floor.** The speed score reaches its floor of 1 point at $\text{ModelTime} / Target(q) = 2^{99/20} \approx 30.91$, where $Target(q) = \text{SpeedTargetMs} \cdot (1 + \text{Difficulty}(q)/100)$. Inside a band, the binding case is always its **lowest** difficulty: a lower difficulty means a smaller $Target(q)$, which means the floor is reached at a smaller `ModelTime` — the floor arrives earliest for the easiest question in the band. At the Simple band's floor difficulty (1) the floor sits at ≈468 s; Intermediate (36) at ≈631 s; Advanced (71) at ≈793 s. Each band's `QuestionTimeoutSeconds` sits below its own floor with 60-70 s of margin, which is why the timeout is banded rather than raised to one flat value: a flat 720 s — the value an Advanced question needs in order to spend 45 tool calls over 22 rounds — would let a Simple question run some 250 s *past* its own 468 s floor without timing out, so every Simple answer slower than 468 s would score 1 and be indistinguishable from every other slow Simple answer. That is exactly the flattening the speed constants were pinned to avoid.
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
  `ReassessedAtUtc`, `ReassessedByModelDisplayNameUsed` and increments `ReassessmentCount`; a second
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

### Aggregation Formulas:
- **Quality Score**: $\text{Quality} = A^{0.55} \cdot C^{0.25} \cdot Cn^{0.10} \cdot R^{0.10}$ (capped at 25 if `criticalError` is true).
- **Model Time**: $\text{ModelTime} = \max(0, \text{DurationMs} - \text{ToolTimeMs})$ — the turn duration with harness tool I/O removed. This, not `DurationMs`, is what speed is scored on.
- **Speed Target**: $Target(q) = T \cdot (1 + s \cdot \text{Difficulty}(q) / 100)$, where $T$ is `SpeedTargetMs` and $s$ is `SpeedDifficultyScaling`.
- **Speed Score**: $\text{Speed} = \text{clamp}(100 - k \cdot \log_2(\text{ModelTime} / Target(q)), 1, 100)$, where $k$ is `SpeedDecayK`.
- **Intelligence Index**: $\sum(\text{Difficulty}(q) \cdot \text{Quality}(q)) / \sum(\text{Difficulty}(q))$.
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

### The roster, and why the destination is Anthropic

Stated position as of 2026-09-03:

| Provider | Role today | Planned role |
|---|---|---|
| **OpenAI** | Model under test (GPT-5.6 Luna) | Model under test; assessor-eligible only once it is not a candidate |
| **Google** | Assessor (Gemini 3.7 Flash) — chosen for cost | **Model under test** later; not assessor-eligible then |
| **Anthropic** | Unused in benchmarking | Not planned as a model under test; used for other tasks such as suite authoring |

Applying the constraint eliminates two of the three:

- **OpenAI** cannot be the permanent assessor: it is a model under test today.
- **Google** cannot be: it becomes a model under test later, and same-family self-preference bias would
  land on the grader whose verdict *scores*.
- **Anthropic** is assessor-eligible in both configurations.

**Anthropic is the destination.** The only open question is *when*.

Gemini grading OpenAI candidates is sound in the meantime — the same-provider gate covers
candidate-versus-assessor only, this pairing never trips it, and it is an independent provider grading an
independent candidate. The configuration simply has an expiry date, and that date is **the first Google
candidate run**.

### The staged migration

**Stage 1 — now, through every remaining OpenAI-candidate run:**

| Role | Provider |
|---|---|
| Candidate | OpenAI |
| Primary assessor (scores) | **Google** — unchanged |
| Second opinion (advisory) | **Anthropic** |
| Mode | `All` |

**Stage 2 — from the first Google-candidate run onward:**

| Candidate | Primary assessor | Second opinion |
|---|---|---|
| Google | **Anthropic** | OpenAI |
| OpenAI | **Anthropic** | Google |

Three distinct providers in every row of both stages; the same-provider gate never fires in either.

**What stage 1 buys.** Anthropic grades every answer alongside Gemini, so the stage-2 promotion is from a
model whose behaviour on this exact suite is already measured — per answer, under the same rubrics, with
`SecondOpinionMeanAbsDelta` and the disagreement list accumulating run by run. Close agreement makes the
switch low-risk and lets the older runs be reasoned about; divergence is something to discover before the
switch rather than after it. A calibration run previews the same comparison against any stored run at the
cost of one assessor pass and no candidate calls.

**What stage 1 costs, stated plainly.** Gemini 3.7 Flash — the grader that produced the Q1 "unverified"
deduction and the Q10 "hallucinates 'adamantium'" verdict that harness version 7 exists to fix — keeps
scoring through stage 1. The exposure is much smaller than run 7's: the prompt fix applies to whichever
model grades, the new triggers fire on exactly those two shapes, and under `All` mode the second reader
sees every answer with disagreement surfaced. It is not zero. **A large mean absolute delta on the first
stage-1 run is grounds to promote Anthropic early rather than wait for the trigger.**

**The staging rationale is the agreement data, not continuity.** An earlier draft argued for keeping
Gemini partly to preserve comparability with the seven existing Gemini-graded runs. That argument does
not hold: the `ScoringMethodVersion` bump to 6 already separates runs 1–7 from everything after on any
answer containing an out-of-rubric claim. Runs 1–7 are becoming a distinct population regardless of who
grades next, and the item-analysis `ScoringMethodMixed` flag says so.

**Do not hop to a stronger Gemini in the interim.** Assessment is roughly 2% of a run's token cost and,
at a few seconds per answer against a ~91-second median answer, is fully hidden inside the pipeline — so
"use the strongest grader available" is sound advice in general. It is not a reason to move from Gemini
3.7 Flash to a stronger Gemini during stage 1: that breaks comparability now *and* still requires the
Anthropic switch at the trigger, producing two breaks where the staged plan has one. Apply the
strongest-grader advice to the model that ends up primary.

### The second opinion is an independent reader, not an adjudicator

| Role | Model choice | Why it does or does not work |
|---|---|---|
| **Adjudicator** — meant to be *more right* | A stronger model | **Does not work as designed.** The first verdict stays authoritative for scoring, so the better model's verdict is recorded and then ignored. If you trust a model more, make it the primary assessor |
| **Independent reader** — meant to detect *fragile verdicts* and measure agreement | Comparable tier, **different provider** | **This is what the feature is for.** Disagreement means two competent, independently-biased readers reached different conclusions |

Stage 1 deliberately places the *stronger* model in the second-opinion slot, which the table warns
against as a permanent arrangement. That is acceptable here precisely because it is temporary and because
its purpose is measurement rather than adjudication — observing the prospective primary before promoting
it. Were it to become permanent, it would be the adjudicator anti-pattern and the switch should happen
instead.

In stage 2 the second opinion rotates to whichever of OpenAI and Google is not the candidate. The cost of
rotation, stated plainly: **the agreement metric is comparable only within a candidate-provider family.**
The Intelligence Index is unaffected, because it comes from the primary assessor, which does not rotate
once stage 2 begins.

### Grade everything twice

Best practice for rated evaluation — in ML evaluation and in the psychometrics it borrows from — is **two
independent raters over the whole set, with inter-rater agreement reported**. Selective re-grading is the
compromise for when that is unaffordable, and it is not unaffordable here.

Full double grading buys three things selective re-grading cannot:

- **An unbiased agreement rate.** Under selective re-grading the disagreement rate is conditioned on the
  first grader's own uncertainty, so it measures nothing about the instrument. Stage 1's entire value
  rests on this.
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
   assessor differs from the most recent completed run of the same suite. It fires on the stage-2
   promotion, correctly — that is exactly the moment to be told.

### Keep suite authoring separate from grading

Anthropic is used for other benchmark work, including suite authoring. Keep that configuration distinct
from the grading one: a model that wrote a rubric is not a neutral reader of answers against it, and the
two roles drifting onto one System AI Configuration would make that impossible to see.

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
- **`BenchmarkQuestion`**: Order index, `ItemRevision` (bumped whenever the question text, band or rubric changes — an edited question is a different item), question text, difficulty tier, `AssessedDifficulty` ($1\text{--}100$), `AssessedDifficultyModel` (display name of assessing model), `AssessedDifficultyAtUtc`, expected rubric points, and assessor configuration snapshot (`AssessedDifficultyModelConfigurationId`, `AssessedDifficultyProviderUsed`, `AssessedDifficultyModelIdUsed`, `AssessedDifficultyThinkingLevelUsed`, `AssessedDifficultyReasoningModeUsed`, `AssessedDifficultyReasoningSummaryUsed`, `AssessedDifficultyServiceTierUsed`, `AssessedDifficultyMaxOutputTokensUsed`).
- **`BenchmarkRun`**: Tested and assessor snapshot fields, run status, `QualityIndex`, `UnweightedQualityIndex`, `SpeedIndex`, `TotalAnswerDurationMs`, `ScoringProfileId`, `ScoringProfileSnapshotJson`, `ScoringMethodVersion`, `HarnessVersion`, `DifficultyFallbackUsed` (retained for historical runs; not set by new runs), `SpeedMeasurementDegraded`, `MaxParallelQuestionsUsed`, the integrity counts (`TransportDefectAnswerCount`, `RecoveredAnswerCount`, `AdvisoryFlagAnswerCount`, `ContestedVerdictAnswerCount`, `ReassessedAnswerCount`), the second-opinion record (`SecondOpinionModeUsed`, `SecondOpinionGradedAnswerCount`, `SecondOpinionMeanAbsDelta`), token accounting, and assessment synthesis.
- **`BenchmarkRunAnswer`**: Order index, question text, sanitized visible answer text, thought text (reasoning), dimensional levels (0–6), dimensional scores, `QualityScore`, `SpeedScore`, `CriticalError`, `AssessedDifficulty`, `AssessmentStatus`, assessor comment, token/duration metrics, the assessor's evidence (`AssessmentEvidenceJson`, `CriticalErrorQuote`, `UnverifiedClaimCount`, `UnverifiedClaimsJson`), the second-opinion verdict and its `SecondOpinionTrigger`, and re-assessment provenance (`PreviousQualityScore`, `ReassessedAtUtc`, `ReassessedByModelDisplayNameUsed`, `ReassessmentCount`).
- **`BenchmarkRunAnswer` item identity**: `BenchmarkQuestionId` (nullable FK, `DeleteBehavior.SetNull`) and `ItemRevisionUsed`. The stable link between an answer and the question it answers; before it existed, `OrderIndex` was the only link and a suite reorder silently re-attached every earlier run's answers to the wrong questions. Null means "unlinked" and is excluded from item analysis rather than guessed at.
- **`BenchmarkAssessorCalibration`**: One non-destructive re-grading of a run by an alternative assessor — the assessor snapshot, `AnswerCount`, `SkippedAnswerCount`, `MeanAbsDelta`, `DisagreementCount`, token and duration cost, and `VerdictsJson`. Admin-UI only: it never appears in the Markdown report, because a calibration is an experiment about graders rather than a property of the run.

### Difficulty Assessment Lifecycle
1. **Explicit Assessor Selection**: Question and suite difficulty ratings are explicit actions where the administrator chooses any benchmark-capable System AI Configuration via a modal selector dialog.
2. **Clear on Edit**: When a question's text, author difficulty tier, or expected criteria rubric is modified, any existing assessed difficulty and assessor snapshot are automatically cleared.
3. **Suite Completion Tracking**: Each suite card displays a completion badge indicating progress (`Difficulty n/total Assessed`), styled green at full completion, amber when partial, and neutral at zero.
4. **Run Gating**: Benchmark runs require every question in the suite to be assessed before execution. Starting a run with unassessed questions is rejected with HTTP 400 BadRequest. The legacy pre-run silent auto-rating step has been removed.

### Content Rendering & Security Principles
Rendering policy strictly depends on content author:
- **Administrator-Authored Content** (Suite descriptions, names, and question expected answer criteria/rubrics): Authored as Markdown and rendered as sanitized HTML via `MarkdownPipe` (`marked` + `DOMPurify`) inside `CollapsibleMarkdownComponent`.
- **Rubric Authoring Conventions**: Rubrics should use bold section labels (`**REQUIRED**`, `**CRITICAL ERROR**`, `**SCOPE**`, `**FORM**`, `**SOURCE**`), bulleted lists (`- `), and inline code backticks (`` `symbol` ``). ATX headings (`#`, `##`, `###`) are avoided to prevent collisions with the prompt's outer sectioning hierarchy. In prompts, rubrics are safely fenced between `--- BEGIN RUBRIC ---` and `--- END RUBRIC ---` delimiters on separate lines.
- **AI-Generated Content** (Candidate model answers, thought reasoning text, assessor evaluations): Untrusted external completions rendered strictly as **plain text** within `<pre>` containers, never through `[innerHTML]`.

> **Note on Default Suite Re-Import:** Updating `BenchmarkDefaultSuite.json` does not automatically modify previously imported database rows. To reflect updated default suite descriptions or questions, re-import the default suite or edit existing suites manually.

---

## 6. API Endpoints

All benchmark endpoints require the `AdminOnly` authorization policy:

### Scoring Profiles
- `GET /api/admin/benchmark/scoring-profiles`: List all scoring profiles.
- `POST /api/admin/benchmark/scoring-profiles`: Create a new profile.
- `PUT /api/admin/benchmark/scoring-profiles/{id}`: Update profile configuration.
- `POST /api/admin/benchmark/scoring-profiles/{id}/default`: Mark profile as system default.
- `DELETE /api/admin/benchmark/scoring-profiles/{id}`: Delete profile (default cannot be deleted).

### Difficulty Rating
- `POST /api/admin/benchmark/suites/{id}/rate-difficulty`: Auto-rate difficulty for all questions in a suite with an explicitly selected assessor model; returns `{ ratedCount, suite }`.
- `POST /api/admin/benchmark/questions/{id}/rate-difficulty`: Auto-rate difficulty for a single question with an explicitly selected assessor model; returns `{ difficulty }`.

### Suites & Questions
- `GET /api/admin/benchmark/suites`: List all suites with question counts and assessed question progress.
- `POST /api/admin/benchmark/suites`: Create a new suite.
- `PUT /api/admin/benchmark/suites/{id}`: Update suite name and description.
- `DELETE /api/admin/benchmark/suites/{id}`: Delete suite.
- `POST /api/admin/benchmark/suites/{id}/duplicate`: Clone a suite, its questions, and their assessment snapshots.
- `POST /api/admin/benchmark/suites/import-default`: Import the default suite (arrives unassessed).
- `GET /api/admin/benchmark/suites/{id}/item-analysis`: Per-item statistics over the suite's stored runs, with the suite-level sample size, assessor mix and scoring-method mix. Pure arithmetic; no AI calls, no spend gate.
- `GET /api/admin/benchmark/suites/{id}/rubric-gaps`: Clustered unverified claims with a `LikelyRubricGap` / `LikelyHallucination` verdict per cluster. No AI calls.
- `POST /api/admin/benchmark/suites/{id}/validate-citations`: Resolves the rubrics' `**SOURCE**` citations against the running source and wiki indexes. A POST rather than a GET because it walks the whole index. No AI calls.
- `POST /api/admin/benchmark/suites/{id}/coverage-analysis`: Asks an explicitly selected model which subsystems the suite does not test (gated by spend caps). Returns a **read-only report**; nothing is written into the suite, and no endpoint exists that would.
- `GET /api/admin/benchmark/suites/{id}/questions`: List questions by order index with assessor snapshot properties.
- `POST /api/admin/benchmark/suites/{id}/questions`: Add a question to a suite.
- `PUT /api/admin/benchmark/questions/{id}`: Update a question (clears assessment snapshot if content changed).
- `DELETE /api/admin/benchmark/questions/{id}`: Delete a question.
- `PUT /api/admin/benchmark/suites/{id}/questions/reorder`: Reorder questions via ID array.

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
- `POST /api/admin/benchmark/runs/{id}/cancel`: Cancel an active run.
- `POST /api/admin/benchmark/runs/{id}/rerun-failed`: Re-run only questions that encountered provider errors (gated by spend caps).
- `GET /api/admin/benchmark/runs/{id}/report`: Download server-rendered Markdown report with compliance manifest.
- `DELETE /api/admin/benchmark/runs/{id}`: Delete a single run.
- `GET /api/admin/benchmark/suites/{id}/runs/footprint`: Return stored run count and total answer character footprint for a suite.
- `DELETE /api/admin/benchmark/suites/{id}/runs`: Bulk delete all stored benchmark runs for a suite.

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

**The scope difference is the part that is easy to misread.** `ToolExecutor` keys its counter on `ToolBudgetScopeId ?? SessionId`. The benchmark sets a per-question scope (`bench_{runId}_q{orderIndex}`), so `Benchmark:ToolCallBudget:Advanced` is a **per-question** allowance. Chat sets none, so `AiPerformanceSettings:MaxCallsPerSession` is the allowance for an **entire chat session** across a four-hour window. Comparing the two numbers directly makes the chat look generous when it is not.

Two invariants, to be re-checked whenever the benchmark bands are retuned:

| Invariant | Today |
|---|---|
| `AiPerformanceSettings:MaxToolIterations:Default` **equals** `Benchmark:ToolIterations:Advanced` | 22 = 22 |
| `AiPerformanceSettings:MaxCallsPerSession:Default` is **at least 3x** `Benchmark:ToolCallBudget:Advanced` | 150 ≥ 3 × 45 |

The 3x factor is empirical, not arbitrary: on the 2026-09-03 run a single Advanced question executed up to **39** tool calls (Q13 and Q18, each 39 of 45), so a session budget has to cover several such questions rather than one. At the old default of 50, the second hard question in a session was refused mid-investigation with "Maximum tool calls per session exceeded."

Both chat values remain user-adjustable in `/settings`; these are the defaults for a user who has never changed them. `MaxResultLength` already agreed at 10,000 on both sides, and chat's `ChatRequestTimeout` (1,800 s) already exceeds the Advanced band's per-question timeout (720 s).

---

## 10. Game Context Board Snapshots & AI-Generated Questions (Harness Version 8)

Harness version 8 introduces **Game Context Board Snapshots** and **AI-Generated Questions Grounded in Live Game State**.

### Game Context Board Snapshots
A board snapshot (`BenchmarkGameSnapshot`) captures the complete, authentic text dump of a live GnollHack game session board.
- **Capture Pipeline**:
  - **Live Game Capture (`ClientRefresh`)**: Captured directly from the running game client over the client bridge via SignalR from the Chat UI ("Capture Live Board" button). It creates both the snapshot and an empty question suite bound to it in a single transaction.
  - **Session Attachment Capture (`SessionAttachment`)**: Captured from an existing game snapshot attached to the chat session ("Save Attached Game Snapshot" button in the chat header). This extracts the newest attached snapshot text directly from the session's system messages without requiring a 45-second round trip to the native game client.
  - **Server Upload (`ServerUpload`)**: Captured via programmatic API or file import (`file_upload` and `manual_entry` currently have no dedicated UI).
- **Sanitizer Convergence**: All capture paths pass board text through `DumpHtmlSanitizer.NormalizeFlattenedText` before persistence. This normalizes line endings (`\r\n` / `\r` to `\n`), strips trailing whitespace per line, strips terminal backticks/triple backticks, rejects empty text, computes a canonical SHA-256 digest, and enforces the 60,000 character hard cap with an explicit truncation marker (`[SNAPSHOT TRUNCATED at 60000 chars]`).
- **Provenance Tracking**: Each snapshot records `CaptureMethod`, `SourceChatSessionId`, `SourceGnollHackVersion`, `Notes`, `DigestText`, and `CapturedAtUtc`.
- **Automatic Name Disambiguation**: Duplicate board names are automatically disambiguated with an incrementing numeric counter suffix (e.g. `Board Name (2)`, `Board Name (3)`) rather than rejected with an error, keeping the bound question suite name synchronized with the board.
- **Chat UI Controls & Gating**:
  - **Capture Live Board**: Displayed in the chat header actions and sidebar whenever an active session is open.
  - **Save Attached Game Snapshot**: Displayed in the chat header actions whenever an active session has an attached snapshot (`hasGameSnapshot`).
  - **Attach Game Snapshot to Chat**: Displayed in the composer input container whenever running embedded inside the GnollHack game client (`clientBridge.isEmbedded()`) and no snapshot is attached yet (`!hasGameSnapshot`).
  - Both capture buttons are strictly gated by administrator privileges (`isAdmin`) and do **not** depend on `ShowDebugLog` or any build configuration flags.
- **Immutability & Safety**: Board text is immutable after creation. Only metadata (`Name`, `SourceGnollHackVersion`, `Notes`, `DigestText`) can be edited. A board snapshot cannot be deleted if any benchmark suites or runs reference it.

### Snapshot Viewer UI
- **Monospace Rendering**: Preformatted game board snapshot text is **never rendered as Markdown**. Board layouts contain NetHack map symbols (`#`, `|`, `-`, `*`) that Markdown parsers mangle. The text is always displayed inside `<pre class="board-pre">` with `white-space: pre` and horizontal scrolling.
- **Interactive Tools**: Displays provenance grid, SHA-256 hash copy button, full text copy button, `.snapshot.txt` download button, and metadata editing form.
- **Truncation Notice**: If the snapshot contains the truncation marker, a prominent alert informs the administrator that tail sections of the dump were omitted at capture limit.

### AI-Generated Benchmark Questions
Administrators can generate benchmark questions tailored to a specific board snapshot using any benchmark-capable AI configuration.
- **3 Difficulty Bands**: Questions are generated in 3 separate prompts:
  - **Simple** (Default: 6 questions, authored difficulty: `Simple`)
  - **Intermediate** (Default: 6 questions, authored difficulty: `Intermediate`)
  - **Advanced** (Default: 6 questions, authored difficulty: `Advanced`)
- **Strict Grounding & Rubric Structure**: Every generated question must be unanswerable without the board. Every generated rubric must contain:
  1. `**BOARD FACTS**`: Point-by-point factual claims verified against the snapshot text.
  2. `**REQUIRED**`: Essential points an answer must make to receive credit.
  3. `**ACCEPTABLE**`: Valid variations, alternative phrasings, or equivalent actions.
  4. `**UNACCEPTABLE**`: Incorrect assertions, lethal actions, or contradictions of board facts.
- **Human Review Discipline**:
  - All AI-generated questions are flagged `IsGenerated = true` and initially `IsReviewed = false`.
  - Content revisions automatically increment `ItemRevision` via `BenchmarkQuestionAssessment.Clear`, resetting reviewed status if `ReviewedAtRevision != ItemRevision`.
  - Unreviewed questions display warning badges in the Admin UI.
  - Benchmark reports display a prominent warning banner whenever a run includes unreviewed generated questions, disclosing the number of unverified items.
  - A "Verify All" button allows an administrator to attest that all questions have been reviewed against the board snapshot.

### AI Rubric Verification (Board Facts Tab in Suite Health)
To assist human review, the Suite Health dialog provides a dedicated **Board facts** tab.
- **Verifiable Quote Discipline**: The checker model evaluates every factual claim in a question's rubric against the game board snapshot. Every claim assessed as `supported` must be accompanied by a verbatim quote from the snapshot text.
- **Findings & Verdicts**: If any claim is `contradicted` or `not-in-board`, the question verdict is flagged as `unsupported`. The findings table displays the rubric claim, the assessment badge, the exact board evidence quote, and explanatory reasoning.
- **One-Click Corrections**: Administrators can jump directly from a finding card into the question editor to refine the rubric or verify the question.

