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
- **`BenchmarkSuite`**: Unique suite name, description, timestamps, and questions.
- **`BenchmarkQuestion`**: Order index, question text, difficulty tier, `AssessedDifficulty` ($1\text{--}100$), `AssessedDifficultyModel`, `AssessedDifficultyAtUtc`, expected rubric points.
- **`BenchmarkRun`**: Tested and assessor snapshot fields, run status, `QualityIndex`, `SpeedIndex`, `TotalAnswerDurationMs`, `ScoringProfileId`, `ScoringProfileSnapshotJson`, `ScoringMethodVersion`, `DifficultyFallbackUsed`, `SpeedMeasurementDegraded`, `MaxParallelQuestionsUsed`, token accounting, and assessment synthesis.
- **`BenchmarkRunAnswer`**: Order index, question text, sanitized visible answer text, thought text (reasoning), dimensional levels (0–6), dimensional scores, `QualityScore`, `SpeedScore`, `CriticalError`, `AssessedDifficulty`, `AssessmentStatus`, assessor comment, and token/duration metrics.

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
- `POST /api/admin/benchmark/suites/{id}/rate-difficulty`: Auto-rate difficulty for all questions in a suite.
- `POST /api/admin/benchmark/questions/{id}/rate-difficulty`: Auto-rate difficulty for a single question.

### Suites & Questions
- `GET /api/admin/benchmark/suites`: List all suites with question counts.
- `POST /api/admin/benchmark/suites`: Create a new suite.
- `PUT /api/admin/benchmark/suites/{id}`: Update suite name and description.
- `DELETE /api/admin/benchmark/suites/{id}`: Delete suite.
- `POST /api/admin/benchmark/suites/{id}/duplicate`: Clone a suite and its questions.
- `POST /api/admin/benchmark/suites/import-default`: Import the 15-question default suite.
- `GET /api/admin/benchmark/suites/{id}/questions`: List questions by order index.
- `POST /api/admin/benchmark/suites/{id}/questions`: Add a question to a suite.
- `PUT /api/admin/benchmark/questions/{id}`: Update a question.
- `DELETE /api/admin/benchmark/questions/{id}`: Delete a question.
- `PUT /api/admin/benchmark/suites/{id}/questions/reorder`: Reorder questions via ID array.

### Runs & Scoring
- `POST /api/admin/benchmark/runs`: Start a benchmark run.
- `GET /api/admin/benchmark/runs`: List historical runs with filtering.
- `GET /api/admin/benchmark/runs/{id}`: Full run detail with question answers and assessment.
- `POST /api/admin/benchmark/runs/{id}/rescore`: Recompute indices for an existing run against a scoring profile.
- `POST /api/admin/benchmark/runs/{id}/answers/{answerId}/reassess`: Re-assess a single question's answer.
- `POST /api/admin/benchmark/runs/{id}/cancel`: Cancel an active run.
- `POST /api/admin/benchmark/runs/{id}/rerun-failed`: Re-run only questions that encountered provider errors.
- `GET /api/admin/benchmark/runs/{id}/report`: Download server-rendered Markdown report.
- `DELETE /api/admin/benchmark/runs/{id}`: Delete a run.
