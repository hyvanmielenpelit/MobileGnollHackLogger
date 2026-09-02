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

Starting a run opens a modal progress dialog, reachable again at any time from the **Show Progress** button on the active-run banner. It presents the run as the three sequential stages the executor actually performs, rather than as a single loop:

1. **Collecting answers** — every question is answered, in parallel up to `MaxParallelQuestionsUsed`.
2. **Assessing answers** — every answer is scored by the assessor model.
3. **Synthesis and scoring** — the holistic report and final indices are produced.

The stage is derived client-side from `BenchmarkRunDetailDto`: answers present versus `TotalQuestionCount` separates stage 1 from stage 2, and the answers' `AssessmentStatus` values separate stage 2 from stage 3. Two determinate progress bars — Answers and Assessments — stay visible in every stage, so a full Answers bar during assessment does not read as a hang.

The per-question list merges the suite's questions (fetched once when the dialog opens) with the run's answers. A question with no answer row shows `Pending`. There is deliberately no inferred "in progress" state: `BenchmarkService` creates a `BenchmarkRunAnswer` only after the model replies, so no per-question in-flight state is knowable from the client. Reporting one would require server-side run state in `BenchmarkRunManager`, which this dialog does not add.

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

### Aggregation Formulas:
- **Quality Score**: $\text{Quality} = A^{0.55} \cdot C^{0.25} \cdot Cn^{0.10} \cdot R^{0.10}$ (capped at 25 if `criticalError` is true).
- **Speed Score**: $\text{Speed} = \text{clamp}(100 - 25 \cdot \log_2(\text{DurationMs} / 5000), 1, 100)$.
- **Intelligence Index**: $\sum(\text{Difficulty}(q) \cdot \text{Quality}(q)) / \sum(\text{Difficulty}(q))$.
- **Speed Index**: $\sum(\text{Difficulty}(q) \cdot \text{Speed}(q)) / \sum(\text{Difficulty}(q))$.

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
