# Comparison #12: Report for AI Researchers and Developers

**Comparison:** Comparison #12 · 5 models · computed 2026-09-21

*INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.*

- **Date:** 2026-09-28
- **Suite:** GnollHack Core Suite
- **Questions:** 6
- **Models:** 5 models (A to E), identities withheld
- **Pricing basis:** Catalog prices on 2026-09-21

## Abstract

The models answered the same questions, and Model A scored 85 / 100.

## Setup and method

- **Suite:** GnollHack Core Suite, 6 questions in the per-question matrix.
- **Coverage:** every model of Comparison #12 that is not excluded.
- **Chat configuration under test:** Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed
- **Grading:** each answer is graded on accuracy, completeness, conciseness and readability, weighted Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %. Each dimension is graded on behaviorally anchored levels scored 1, 15, 35, 55, 72, 87, 100. A critical error caps the answer's quality at 25.
- **Graders:**
  - Assessor: Gemini 3.8 Flash (Google, gemini-3.8-flash), a different provider from every model
- **Formulas:** answer quality is the weighted geometric mean of the four dimension scores, capped by a critical error; in a panel run it is the mean of both graders' scores. The Intelligence Index is the difficulty-weighted mean of answer quality. Median answer time is the median model time per answer, with tool time excluded. Cost per question is each model's own spend divided by the questions asked.
- **Comparability:** every model in this report was measured under one instrument condition, signature `sig-7f3a91`.
- **Pricing basis:** Catalog prices on 2026-09-21
- **Versions:** harness 41; scoring method 12.

### Compared models

| Letter | Model | Kind | Runs | Thinking level | Harness version | Run dates (UTC) | Price card |
|---|---|---|---|---|---|---|---|
| A | Model A | run | 1 | not set | 41 | 2026-09-21 | — |
| B | Model B | run | 1 | not set | 41 | 2026-09-21 | — |
| C | Model C | run | 1 | not set | 41 | 2026-09-21 | — |
| D | Model D | run | 1 | not set | 41 | 2026-09-21 | — |
| E | Model E | run | 1 | not set | 41 | 2026-09-21 | — |

## Results

| Model | Intelligence Index | 95 % interval | Rank | Median answer time | Speed rank | Cost per question | Cost rank | Critical errors |
|---|---|---|---|---|---|---|---|---|
| Model A | 85 | 82–88 | joint 1 | 12.0 s | 3 | $0.050 | 4 | 0 of 6 answers |
| Model B | 80 | 77–83 | joint 1 | 9.0 s | 2 | $0.030 | 3 | 0 of 6 answers |
| Model C | 70 | 66–74 | joint 3 | 15.0 s | 4 | $0.020 | 2 | 1 of 6 answers |
| Model D | 70 | 64–76 | joint 3 | 8.0 s | 1 | $0.060 | 5 | 0 of 6 answers |
| Model E | 55 | 50–60 | 5 | 20.0 s | 5 | $0.010 | 1 | 0 of 6 answers |

Model A and Model B share a rank because their intervals overlap.

### Paired tests

*Each pair is compared on the questions both models answered. Each measure is its own family of tests, Holm-adjusted across the tests it makes over these models; a result is established when its adjusted p-value is below 0.05. Another set of models gives another family and another adjustment.*

**Against Model A (the reference)**

Intelligence: Holm-adjusted across 4 tests.

| Pair | Questions | Difference (95 % interval) | Adjusted p | Result |
|---|---|---|---|---|
| Model A vs Model B | 6 | 0.0 points (-21.0 to +21.0) | 0.406 | not established |
| Model A vs Model C | 6 | +15.2 points (+6.6 to +23.8) | 0.125 | not established |
| Model A vs Model D | 6 | +15.8 points (+5.6 to +26.1) | 0.125 | not established |
| Model A vs Model E | 6 | +30.0 points (+16.1 to +43.9) | 0.125 | not established |

*Difference: the mean per-question difference, first model minus second, on the questions both answered.*

Speed: no test could be made. Cost: not tested. No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown.

| Pair | Time ratio (95 % interval) | Speed result | Spend ratio (95 % interval) | Cost result |
|---|---|---|---|---|
| Model A vs Model B | 1.00 (1.00 to 1.00) | not tested | — | not tested |
| Model A vs Model C | 1.00 (1.00 to 1.00) | not tested | — | not tested |
| Model A vs Model D | 1.00 (1.00 to 1.00) | not tested | — | not tested |
| Model A vs Model E | 1.00 (1.00 to 1.00) | not tested | — | not tested |

*A ratio is the first model's own time or spend per question divided by the second's, on the questions both answered.*

*At least one entry has a single run. With one run a side, a paired test captures question sampling only, not run-to-run variation, so it can look more certain than it is.*

**Every pair**

Intelligence: Holm-adjusted across 10 tests.

| Pair | Questions | Difference (95 % interval) | Adjusted p | Result |
|---|---|---|---|---|
| Model A vs Model B | 6 | 0.0 points (-21.0 to +21.0) | 0.813 | not established |
| Model A vs Model C | 6 | +15.2 points (+6.6 to +23.8) | 0.313 | not established |
| Model A vs Model D | 6 | +15.8 points (+5.6 to +26.1) | 0.313 | not established |
| Model A vs Model E | 6 | +30.0 points (+16.1 to +43.9) | 0.313 | not established |
| Model B vs Model C | 6 | +15.2 points (+1.1 to +29.3) | 0.313 | not established |
| Model B vs Model D | 6 | +15.8 points (+3.0 to +28.6) | 0.313 | not established |
| Model B vs Model E | 6 | +30.0 points (+21.0 to +39.0) | 0.313 | not established |
| Model C vs Model D | 6 | +0.7 points (-7.1 to +8.4) | 1.000 | not established |
| Model C vs Model E | 6 | +14.8 points (+8.1 to +21.5) | 0.313 | not established |
| Model D vs Model E | 6 | +14.2 points (+5.8 to +22.5) | 0.313 | not established |

*Difference: the mean per-question difference, first model minus second, on the questions both answered.*

Speed: no test could be made. Cost: not tested. No pair could be tested on this measure; each pair says why.

| Pair | Time ratio (95 % interval) | Speed result | Spend ratio (95 % interval) | Cost result |
|---|---|---|---|---|
| Model A vs Model B | 1.00 (1.00 to 1.00) | not tested | — | not tested |
| Model A vs Model C | 1.00 (1.00 to 1.00) | not tested | — | not tested |
| Model A vs Model D | 1.00 (1.00 to 1.00) | not tested | — | not tested |
| Model A vs Model E | 1.00 (1.00 to 1.00) | not tested | — | not tested |
| Model B vs Model C | 1.00 (1.00 to 1.00) | not tested | — | not tested |
| Model B vs Model D | 1.00 (1.00 to 1.00) | not tested | — | not tested |
| Model B vs Model E | 1.00 (1.00 to 1.00) | not tested | — | not tested |
| Model C vs Model D | 1.00 (1.00 to 1.00) | not tested | — | not tested |
| Model C vs Model E | 1.00 (1.00 to 1.00) | not tested | — | not tested |
| Model D vs Model E | 1.00 (1.00 to 1.00) | not tested | — | not tested |

*A ratio is the first model's own time or spend per question divided by the second's, on the questions both answered.*

*At least one entry has a single run. With one run a side, a paired test captures question sampling only, not run-to-run variation, so it can look more certain than it is.*

## Dimension profiles

| Model | Accuracy | Completeness | Conciseness | Readability |
|---|---|---|---|---|
| Model A | 80 | 80 | 80 | 80 |
| Model B | 80 | 80 | 80 | 80 |
| Model C | 65 | 65 | 65 | 65 |
| Model D | 64 | 64 | 64 | 64 |
| Model E | 50 | 50 | 50 | 50 |

- **Accuracy:** from 50 (Model E) to 80 (Model A), 30 points apart
- **Completeness:** from 50 (Model E) to 80 (Model A), 30 points apart
- **Conciseness:** from 50 (Model E) to 80 (Model A), 30 points apart
- **Readability:** from 50 (Model E) to 80 (Model A), 30 points apart

Accuracy ranges from 50 (Model E) to 80 (Model A), 30 points apart.

## Speed and cost frontier

| Model | Median answer time | Cost per question |
|---|---|---|
| Model A | 12.0 s | $0.050 |
| Model B | 9.0 s | $0.030 |
| Model C | 15.0 s | $0.020 |
| Model D | 8.0 s | $0.060 |
| Model E | 20.0 s | $0.010 |

On the Pareto frontier (no other model is at least as good on both measures and better on one):

- **Intelligence against cost:** Models A, B, C and E
- **Intelligence against speed:** Models A, B and D
- **Speed against cost:** Models B, C, D and E

The intelligence against speed frontier holds Models A, B and D.

## Per-model analysis

### Model A

- Model A scored 85 / 100 on intelligence.
  - *Evidence:* Model A's intelligence Index: 85 / 100 · Q1

### Model B

- Model B scored 80 / 100 on intelligence.
  - *Evidence:* Model B's intelligence Index: 80 / 100 · Q1

### Model C

- Model C scored 70 / 100 on intelligence.
  - *Evidence:* Model C's intelligence Index: 70 / 100 · Q1

### Model D

- Model D scored 70 / 100 on intelligence.
  - *Evidence:* Model D's intelligence Index: 70 / 100 · Q1

### Model E

- Model E scored 55 / 100 on intelligence.
  - *Evidence:* Model E's intelligence Index: 55 / 100 · Q1

## Cross-model question patterns

Most models scored low on Q3, which points to the question or the chat first.

### Per-question matrix

| Q | Topic | Band | A | B | C | D | E | Spread |
|---|---|---|---|---|---|---|---|---|
| Q1 | An item question about gems | Intermediate | 92 | 85 | 70 | 75 | 50 | 42 |
| Q2 | An item question about prayer | Intermediate | 88 | 80 | 72 | 60 | 55 | 33 |
| Q3 | An item question about unicorns | Intermediate | 30 | 70 | 28 CE | 32 | 25 | 45 |
| Q4 | An item question about wands | Intermediate | 90 | 75 | 65 | 72 | 52 | 38 |
| Q5 | An item question about altars | Intermediate | 85 | 82 | 74 | 68 | 58 | 27 |
| Q6 | An item question about shops | Intermediate | 95 | 88 | 80 | 78 | 60 | 35 |

*Each model's column is headed by its letter in the models table. CE marks a critical error; Spread is the highest score minus the lowest; — marks a question the model was not asked or not scored on.*

## Grader reliability

No model was graded by an assessor panel in every run, so no panel agreement figures are stated.

- **Response-style conflict:** none

Panel member A graded every answer.

## Threats to validity

- The benchmark asks single-turn questions under one chat configuration. It does not exercise conversation history, pre-injected wiki context, spoiler-free mode, web search or subagents.
- Intervals: Item sampling only.
- The graders are AI models. Each grader's provider relation to the models is stated under Setup and method; a grader from a model's own provider may read that model more favorably.
- Paired tests: each measure's family is Holm-adjusted across the tests it makes over these models only.

Every model rests on a single run, so no interval covers run-to-run variation.

## Reproducibility appendix

- **Model A**: run ids 31; run dates 2026-09-21; System prompt SHA-256 prefix `e9b3e9a7c4d1`; Tool guides SHA-256 prefix `f59d8b30a1c7`
- **Model B**: run ids 32; run dates 2026-09-21; System prompt SHA-256 prefix `e9b3e9a7c4d1`; Tool guides SHA-256 prefix `f59d8b30a1c7`
- **Model C**: run ids 33; run dates 2026-09-21; System prompt SHA-256 prefix `e9b3e9a7c4d1`; Tool guides SHA-256 prefix `f59d8b30a1c7`
- **Model D**: run ids 34; run dates 2026-09-21; System prompt SHA-256 prefix `e9b3e9a7c4d1`; Tool guides SHA-256 prefix `f59d8b30a1c7`
- **Model E**: run ids 35; run dates 2026-09-21; System prompt SHA-256 prefix `e9b3e9a7c4d1`; Tool guides SHA-256 prefix `f59d8b30a1c7`
- **Grader models:** Assessor: Google gemini-3.8-flash
- **Comparability signature:** `sig-7f3a91`
- **Pricing basis:** Catalog prices on 2026-09-21

## Question details

*Each answer shown is one the writer was given as an excerpt; the excerpts were chosen for the questions with critical errors or refuted sentences first, then the widest spread between models, then those every model scored low.*

### Q1: An item question about gems

**Question:**

> Question 201

**Rubric:**

> - Rubric 201

#### Model A, run 31

**Answer:**

> Answer 201

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 92.

**Claim verifier:**

- No claims were checked.

#### Model B, run 32

**Answer:**

> Answer 201

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 85.

**Claim verifier:**

- No claims were checked.

#### Model C, run 33

**Answer:**

> Answer 201

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 70.

**Claim verifier:**

- No claims were checked.

#### Model D, run 34

**Answer:**

> Answer 201

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 75.

**Claim verifier:**

- No claims were checked.

#### Model E, run 35

**Answer:**

> Answer 201

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 50.

**Claim verifier:**

- No claims were checked.

### Q2: An item question about prayer

**Question:**

> Question 202

**Rubric:**

> - Rubric 202

#### Model A, run 31

**Answer:**

> Answer 202

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 88.

**Claim verifier:**

- No claims were checked.

#### Model B, run 32

**Answer:**

> Answer 202

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 80.

**Claim verifier:**

- No claims were checked.

#### Model C, run 33

**Answer:**

> Answer 202

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 72.

**Claim verifier:**

- No claims were checked.

#### Model D, run 34

**Answer:**

> Answer 202

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 60.

**Claim verifier:**

- No claims were checked.

#### Model E, run 35

**Answer:**

> Answer 202

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 55.

**Claim verifier:**

- No claims were checked.

### Q3: An item question about unicorns

**Question:**

> Question 203

**Rubric:**

> - Rubric 203

#### Model A, run 31

**Answer:**

> Answer 203

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 30.

**Claim verifier:**

- No claims were checked.

#### Model B, run 32

**Answer:**

> Answer 203

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 70.

**Claim verifier:**

- No claims were checked.

#### Model C, run 33

**Answer:**

> Answer 203

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 28.
  - Critical error: "Answer 203"

**Claim verifier:**

- No claims were checked.

#### Model D, run 34

**Answer:**

> Answer 203

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 32.

**Claim verifier:**

- No claims were checked.

#### Model E, run 35

**Answer:**

> Answer 203

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 25.

**Claim verifier:**

- No claims were checked.

### Q4: An item question about wands

**Question:**

> Question 204

**Rubric:**

> - Rubric 204

#### Model A, run 31

**Answer:**

> Answer 204

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 90.

**Claim verifier:**

- No claims were checked.

#### Model B, run 32

**Answer:**

> Answer 204

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 75.

**Claim verifier:**

- No claims were checked.

#### Model C, run 33

**Answer:**

> Answer 204

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 65.

**Claim verifier:**

- No claims were checked.

#### Model D, run 34

**Answer:**

> Answer 204

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 72.

**Claim verifier:**

- No claims were checked.

#### Model E, run 35

**Answer:**

> Answer 204

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 52.

**Claim verifier:**

- No claims were checked.

### Q5: An item question about altars

**Question:**

> Question 205

**Rubric:**

> - Rubric 205

#### Model A, run 31

**Answer:**

> Answer 205

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 85.

**Claim verifier:**

- No claims were checked.

#### Model B, run 32

**Answer:**

> Answer 205

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 82.

**Claim verifier:**

- No claims were checked.

#### Model C, run 33

**Answer:**

> Answer 205

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 74.

**Claim verifier:**

- No claims were checked.

#### Model D, run 34

**Answer:**

> Answer 205

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 68.

**Claim verifier:**

- No claims were checked.

#### Model E, run 35

**Answer:**

> Answer 205

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 58.

**Claim verifier:**

- No claims were checked.

### Q6: An item question about shops

**Question:**

> Question 206

**Rubric:**

> - Rubric 206

#### Model A, run 31

**Answer:**

> Answer 206

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 95.

**Claim verifier:**

- No claims were checked.

#### Model B, run 32

**Answer:**

> Answer 206

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 88.

**Claim verifier:**

- No claims were checked.

#### Model C, run 33

**Answer:**

> Answer 206

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 80.

**Claim verifier:**

- No claims were checked.

#### Model D, run 34

**Answer:**

> Answer 206

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 78.

**Claim verifier:**

- No claims were checked.

#### Model E, run 35

**Answer:**

> Answer 206

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 60.

**Claim verifier:**

- No claims were checked.

## Evaluation terms

- **Purpose statement:** Internal evaluation of candidate AI models for the Overseer assistant within GnollHack.
- **Distillation / training prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.
- **Third-party model content:** Outputs generated by the compared models (identities withheld), graded by models from Google, and described in this document by **Claude Opus 5.5** (Anthropic) are third-party content evaluated solely for domain-specific benchmark scoring and operational model selection.

---

*Document ID 212 · format version 12 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Full · models anonymized*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically for structure, permitted figures and references, word limits, disclosure of benchmark text, peer names, significance claims, interval-overlap wording, hype words and spelling; the checks do not verify the prose's interpretations.*
