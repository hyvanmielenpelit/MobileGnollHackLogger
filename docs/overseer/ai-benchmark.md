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

### Aggregation Formulas:
- **Quality Score**: $\text{Quality} = A^{0.55} \cdot C^{0.25} \cdot Cn^{0.10} \cdot R^{0.10}$ (capped at 25 if `criticalError` is true).
- **Model Time**: $\text{ModelTime} = \max(0, \text{DurationMs} - \text{ToolTimeMs})$ — the turn duration with harness tool I/O removed. This, not `DurationMs`, is what speed is scored on.
- **Speed Target**: $Target(q) = T \cdot (1 + s \cdot \text{Difficulty}(q) / 100)$, where $T$ is `SpeedTargetMs` and $s$ is `SpeedDifficultyScaling`.
- **Speed Score**: $\text{Speed} = \text{clamp}(100 - k \cdot \log_2(\text{ModelTime} / Target(q)), 1, 100)$, where $k$ is `SpeedDecayK`.
- **Intelligence Index**: $\sum(\text{Difficulty}(q) \cdot \text{Quality}(q)) / \sum(\text{Difficulty}(q))$.
- **Speed Index**: the **equal-weight mean** of $\text{Speed}(q)$ over answered questions. Difficulty enters through $Target(q)$, not through the weight — weighting here as well would count difficulty twice and drag the index toward the floor by construction. (This line previously claimed a difficulty-weighted mean, which neither the code nor the generated report has ever produced.)

---

## 3. Data Model & Relationships

```
BenchmarkScoringProfile (1)
       │
       └───< (N) BenchmarkRun (1) ───< (N) BenchmarkRunAnswer
                       ▲
                       │
BenchmarkSuite (1) ────┴───< (N) BenchmarkQuestion
```

- **`BenchmarkScoringProfile`**: Name, `IsDefault`, dimensional weights, `LevelScoresJson`, `CriticalErrorCeiling`, `SpeedTargetMs`, `SpeedDecayK`, `MaxParallelQuestions`.
- **`BenchmarkSuite`**: Unique suite name, description (accepts Markdown, rendered as sanitized HTML), timestamps, and questions.
- **`BenchmarkQuestion`**: Order index, question text, difficulty tier, `AssessedDifficulty` ($1\text{--}100$), `AssessedDifficultyModel` (display name of assessing model), `AssessedDifficultyAtUtc`, expected rubric points, and assessor configuration snapshot (`AssessedDifficultyModelConfigurationId`, `AssessedDifficultyProviderUsed`, `AssessedDifficultyModelIdUsed`, `AssessedDifficultyThinkingLevelUsed`, `AssessedDifficultyReasoningModeUsed`, `AssessedDifficultyReasoningSummaryUsed`, `AssessedDifficultyServiceTierUsed`, `AssessedDifficultyMaxOutputTokensUsed`).
- **`BenchmarkRun`**: Tested and assessor snapshot fields, run status, `QualityIndex`, `SpeedIndex`, `TotalAnswerDurationMs`, `ScoringProfileId`, `ScoringProfileSnapshotJson`, `ScoringMethodVersion`, `DifficultyFallbackUsed` (retained for historical runs; not set by new runs), `SpeedMeasurementDegraded`, `MaxParallelQuestionsUsed`, token accounting, and assessment synthesis.
- **`BenchmarkRunAnswer`**: Order index, question text, sanitized visible answer text, thought text (reasoning), dimensional levels (0–6), dimensional scores, `QualityScore`, `SpeedScore`, `CriticalError`, `AssessedDifficulty`, `AssessmentStatus`, assessor comment, and token/duration metrics.

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

## 4. API Endpoints

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
- `POST /api/admin/benchmark/runs/{id}/answers/{answerId}/reassess`: Re-assess a single question's answer (gated by spend caps).
- `POST /api/admin/benchmark/runs/{id}/cancel`: Cancel an active run.
- `POST /api/admin/benchmark/runs/{id}/rerun-failed`: Re-run only questions that encountered provider errors (gated by spend caps).
- `GET /api/admin/benchmark/runs/{id}/report`: Download server-rendered Markdown report with compliance manifest.
- `DELETE /api/admin/benchmark/runs/{id}`: Delete a single run.
- `GET /api/admin/benchmark/suites/{id}/runs/footprint`: Return stored run count and total answer character footprint for a suite.
- `DELETE /api/admin/benchmark/suites/{id}/runs`: Bulk delete all stored benchmark runs for a suite.

---

## 5. AI Provider Terms Compliance Controls

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

## 6. Thinking Level Configuration & Output Limits

- **Pin Explicit Thinking Levels**: Benchmark and assessor System AI Configurations should pin an explicit **Thinking Level** (e.g. `high`, `medium`, or `none`). Leaving it on `Default` makes a run's reasoning behavior depend on the model and on `AnthropicSettings:ExplicitDefaultEffort`, which can compromise run-to-run comparability over time.
- **Assessor Token Limits (`AssessorMaxOutputTokens`)**: Evaluator and assessor completions share their `max_tokens` budget with internal reasoning/thinking output. The default fallback limit (`Benchmark:AssessorMaxOutputTokens`) is set to `32000` to prevent assessor evaluation JSON completions from being prematurely truncated when thinking is enabled. Individual assessor configurations can override this fallback using their per-configuration `MaxOutputTokens` setting.

---

## 7. Keeping Chat Limits in Step with the Benchmark

The benchmark measures what the Overseer chat can do, so the chat's own defaults must not be tighter than the caps the harness grants its hardest questions. They were: before harness version 6 the chat allowed 15 tool iterations against the Advanced band's 22, and 50 tool calls against the Advanced band's 45.

**The scope difference is the part that is easy to misread.** `ToolExecutor` keys its counter on `ToolBudgetScopeId ?? SessionId`. The benchmark sets a per-question scope (`bench_{runId}_q{orderIndex}`), so `Benchmark:ToolCallBudget:Advanced` is a **per-question** allowance. Chat sets none, so `AiPerformanceSettings:MaxCallsPerSession` is the allowance for an **entire chat session** across a four-hour window. Comparing the two numbers directly makes the chat look generous when it is not.

Two invariants, to be re-checked whenever the benchmark bands are retuned:

| Invariant | Today |
|---|---|
| `AiPerformanceSettings:MaxToolIterations:Default` **equals** `Benchmark:ToolIterations:Advanced` | 22 = 22 |
| `AiPerformanceSettings:MaxCallsPerSession:Default` is **at least 3x** `Benchmark:ToolCallBudget:Advanced` | 150 ≥ 3 × 45 |

The 3x factor is empirical, not arbitrary: on the 2026-09-03 run a single Advanced question executed up to **39** tool calls (Q13 and Q18, each 39 of 45), so a session budget has to cover several such questions rather than one. At the old default of 50, the second hard question in a session was refused mid-investigation with "Maximum tool calls per session exceeded."

Both chat values remain user-adjustable in `/settings`; these are the defaults for a user who has never changed them. `MaxResultLength` already agreed at 10,000 on both sides, and chat's `ChatRequestTimeout` (1,800 s) already exceeds the Advanced band's per-question timeout (720 s).
