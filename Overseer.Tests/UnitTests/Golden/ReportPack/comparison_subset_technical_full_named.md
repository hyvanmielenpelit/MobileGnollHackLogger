# Comparison #12 — Orion Max vs Zeta Prime: Report for AI Researchers and Developers

**Comparison:** Comparison #12 — Spring model sweep · 5 models · computed 2026-09-21

*INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.*

- **Date:** 2026-09-28
- **Suite:** GnollHack Core Suite
- **Questions:** 6
- **Models:** Orion Max and Zeta Prime
- **Coverage:** 2 of 5 models of Comparison #12; the other 3 are not part of this document.
- **Pricing basis:** Catalog prices on 2026-09-21

## Abstract

The models answered the same questions, and Orion Max scored 85 / 100.

## Setup and method

- **Suite:** GnollHack Core Suite, 6 questions in the per-question matrix.
- **Coverage:** 2 of 5 models of Comparison #12; the other 3 are not part of this document.
- **Chat configuration under test:** Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed
- **Grading:** each answer is graded on accuracy, completeness, conciseness and readability, weighted Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %. Each dimension is graded on behaviorally anchored levels scored 1, 15, 35, 55, 72, 87, 100. A critical error caps the answer's quality at 25.
- **Graders:**
  - Assessor: Gemini 3.8 Flash (Google, gemini-3.8-flash), a different provider from every model
- **Formulas:** answer quality is the weighted geometric mean of the four dimension scores, capped by a critical error; in a panel run it is the mean of both graders' scores. The Intelligence Index is the difficulty-weighted mean of answer quality. Median answer time is the median model time per answer, with tool time excluded. Cost per question is each model's own spend divided by the questions asked.
- **Comparability:** every model in this report was measured under one instrument condition, signature `sig-7f3a91`.
- **Pricing basis:** Catalog prices on 2026-09-21
- **Versions:** harness 41; scoring method 12.

### Compared models

| Letter | Model | Provider | Kind | Runs | Thinking level | Harness version | Run dates (UTC) | Price card |
|---|---|---|---|---|---|---|---|---|
| A | Orion Max (reference) | Northwind | run | 1 | not set | 41 | 2026-09-21 | — |
| B | Zeta Prime | Midland | run | 1 | not set | 41 | 2026-09-21 | — |

## Results

| Model | Provider | Intelligence Index | 95 % interval | Rank | Median answer time | Speed rank | Cost per question | Cost rank | Critical errors |
|---|---|---|---|---|---|---|---|---|---|
| Orion Max (reference) | Northwind | 85 | 82–88 | 1 | 12.0 s | 1 | $0.050 | 2 | 0 of 6 answers |
| Zeta Prime | Midland | 55 | 50–60 | 2 | 20.0 s | 2 | $0.010 | 1 | 0 of 6 answers |

Orion Max and Zeta Prime share a rank because their intervals overlap.

### Paired tests

*Each pair is compared on the questions both models answered. Each measure is its own family of tests, Holm-adjusted across the tests it makes over these models; a result is established when its adjusted p-value is below 0.05. Another set of models gives another family and another adjustment.*

**Against Orion Max (the reference)**

Intelligence: a single test, no adjustment.

| Pair | Questions | Difference (95 % interval) | Adjusted p | Result |
|---|---|---|---|---|
| Orion Max vs Zeta Prime | 6 | +30.0 points (+16.1 to +43.9) | 0.031 | Orion Max scored higher |

*Difference: the mean per-question difference, first model minus second, on the questions both answered.*

Speed: no test could be made. Cost: not tested. No candidate price card was resolved for run #31 of Orion Max on this pricing basis, so its cost is unknown.

| Pair | Time ratio (95 % interval) | Speed result | Spend ratio (95 % interval) | Cost result |
|---|---|---|---|---|
| Orion Max vs Zeta Prime | 1.00 (1.00 to 1.00) | not tested | — | not tested |

*A ratio is the first model's own time or spend per question divided by the second's, on the questions both answered.*

*At least one entry has a single run. With one run a side, a paired test captures question sampling only, not run-to-run variation, so it can look more certain than it is.*

### Compared with the reference model

**Reference:** Orion Max, the highest Intelligence Index.

| Model | Provider | Questions | Paired difference (95 % interval) | Result | Better / similar / worse | May mislead per 20 | Time ratio | Cost ratio |
|---|---|---|---|---|---|---|---|---|
| Orion Max (reference) | Northwind | — | — | — | — | 0 | 1.00× | 1.00× |
| Zeta Prime | Midland | 6 | -30.0 (-43.9 to -16.1) | Orion Max scored higher | 0 / 1 / 5 | 0 | 1.67× | 0.20× |

*Paired difference: the mean per-question difference, the model minus the reference, on the questions both answered, with its 95 % interval. Better, similar and worse count the questions both scored on the same item revision on which the model scored more than 15 points above the reference, within 15 points of it, or more than 15 points below it; they are not tests. May mislead per 20: answers with an accuracy level below 4 or a critical error, per 20 answers. Time and cost ratios: the model's median answer time and cost per question divided by the reference's.*

The table sets every other model against Orion Max; only the paired tests and the intervals decide where an order is established.

## Notable findings

- Every model scored below 50 on Q3: a likely chat, corpus or question problem rather than one model's.

*Detected by code from the figures, in this order, at most two of a kind and ten in all: a model established as scoring higher than another (by a paired test or by 95 % intervals that do not overlap) at no more than half its cost per question, or half its median answer time; one model at two thinking levels where the lower level's Intelligence Index is at least the higher's; the highest-Index or reference model scoring 50 or less where the other models' median is 75 or more; the lowest-Index model scoring at least 15 points above every other model on a question; every model scoring below 50 on a question; an advanced-band score at least 10 points above the simple-band score, each band with at least three questions; at least two critical errors, or three panel disagreements, at twice the other models' median rate or more; a pair whose order depends on which panel member grades; truncated answers and tool-limit hits on at least 10 % of a model's answers; tool calls per question or median answer length at most half or at least twice the other models' median; and a provider error rate of at least 2 % and five times the other models' median.*

Each finding follows from the tables above; the data does not explain them further.

## Dimension profiles

| Model | Provider | Accuracy | Completeness | Conciseness | Readability |
|---|---|---|---|---|---|
| Orion Max (reference) | Northwind | 80 | 80 | 80 | 80 |
| Zeta Prime | Midland | 50 | 50 | 50 | 50 |

- **Accuracy:** from 50 (Zeta Prime) to 80 (Orion Max), 30 points apart
- **Completeness:** from 50 (Zeta Prime) to 80 (Orion Max), 30 points apart
- **Conciseness:** from 50 (Zeta Prime) to 80 (Orion Max), 30 points apart
- **Readability:** from 50 (Zeta Prime) to 80 (Orion Max), 30 points apart

Accuracy ranges from 50 (Zeta Prime) to 80 (Orion Max), 30 points apart.

## Speed and cost frontier

| Model | Provider | Median answer time | Cost per question |
|---|---|---|---|
| Orion Max (reference) | Northwind | 12.0 s | $0.050 |
| Zeta Prime | Midland | 20.0 s | $0.010 |

On the Pareto frontier (no other model is at least as good on both measures and better on one):

- **Intelligence against cost:** Orion Max and Zeta Prime
- **Intelligence against speed:** Orion Max
- **Speed against cost:** Orion Max and Zeta Prime

The intelligence against speed frontier holds Orion Max.

## Per-model analysis

### Orion Max

- Orion Max scored 85 / 100 on intelligence.
  - *Evidence:* Orion Max's intelligence Index: 85 / 100 · Q1

### Zeta Prime

- Zeta Prime scored 55 / 100 on intelligence.
  - *Evidence:* Zeta Prime's intelligence Index: 55 / 100 · Q1

## Why the models differ

### Failure profile

| Model | Provider | Answers | May mislead | Correct / partial / incorrect / not attempted | Wrong instead of abstaining | Level 2 or below: accuracy / completeness / conciseness / readability | Truncated | Output defects | No tool call | Tool-limit hits | Median answer length | Isolated / shared misses | Lowest scorer |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Orion Max (reference) | Northwind | 6 | 0 (0 per 20) | — | — | 0 / 0 / 0 / 0 | 0 | 0 | 6 | 0 | 10 | — | — |
| Zeta Prime | Midland | 6 | 0 (0 per 20) | — | — | 0 / 0 / 0 / 0 | 0 | 0 | 6 | 0 | 10 | — | — |

*Counted over each model's scored answers in all its runs; in a panel run a level is the mean of both panel members' levels. May mislead: an accuracy level below 4 or a critical error. Outcomes are recorded from scoring method 13. Low levels: answers at level 2 or below on each dimension. Output defects: an empty, truncated or scrubbed answer, reasoning bleed, repeated fragments, or an answer that did not complete normally. Tool-limit hits: the tool budget or the iteration limit was reached. An isolated miss is a question the model scored 50 or less where the other models' median is 75 or more; a shared miss is one it scored below 50 along with at least half the others. Lowest scorer: questions where no other model scored lower. The counts say what the graders recorded, not why.*

### Grader findings by category

No run of these models recorded structured grader findings; they are recorded from harness 40.

The failure profiles count what the graders recorded for each model; they do not say why.

## Cross-model question patterns

Most models scored low on Q3, which points to the question or the chat first.

### Per-question matrix

| Q | Topic | Band | A | B | Spread |
|---|---|---|---|---|---|
| Q1 | An item question about gems | Intermediate | 92 | 50 | 42 |
| Q2 | An item question about prayer | Intermediate | 88 | 55 | 33 |
| Q3 | An item question about unicorns | Intermediate | 30 | 25 | 5 |
| Q4 | An item question about wands | Intermediate | 90 | 52 | 38 |
| Q5 | An item question about altars | Intermediate | 85 | 58 | 27 |
| Q6 | An item question about shops | Intermediate | 95 | 60 | 35 |

*Columns: A = Orion Max, B = Zeta Prime. CE marks a critical error; Spread is the highest score minus the lowest. — under a model marks a question it was not asked or not scored on.*

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

- **Orion Max** (Northwind orion-max, thinking level not set): run ids 31; run dates 2026-09-21; System prompt SHA-256 prefix `e9b3e9a7c4d1`; Tool guides SHA-256 prefix `f59d8b30a1c7`
- **Zeta Prime** (Midland zeta-prime, thinking level not set): run ids 35; run dates 2026-09-21; System prompt SHA-256 prefix `e9b3e9a7c4d1`; Tool guides SHA-256 prefix `f59d8b30a1c7`
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

#### Orion Max, run 31

**Answer:**

> Answer 201

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 92.

**Claim verifier:**

- No claims were checked.

#### Zeta Prime, run 35

**Answer:**

> Answer 201

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 50. Misses the alignment condition.

**Claim verifier:**

- No claims were checked.

### Q2: An item question about prayer

**Question:**

> Question 202

**Rubric:**

> - Rubric 202

#### Orion Max, run 31

**Answer:**

> Answer 202

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 88.

**Claim verifier:**

- No claims were checked.

#### Zeta Prime, run 35

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

#### Orion Max, run 31

**Answer:**

> Answer 203

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 30.

**Claim verifier:**

- No claims were checked.

#### Zeta Prime, run 35

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

#### Orion Max, run 31

**Answer:**

> Answer 204

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 90.

**Claim verifier:**

- No claims were checked.

#### Zeta Prime, run 35

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

#### Orion Max, run 31

**Answer:**

> Answer 205

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 85.

**Claim verifier:**

- No claims were checked.

#### Zeta Prime, run 35

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

#### Orion Max, run 31

**Answer:**

> Answer 206

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 95.

**Claim verifier:**

- No claims were checked.

#### Zeta Prime, run 35

**Answer:**

> Answer 206

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 60.

**Claim verifier:**

- No claims were checked.

## Evaluation terms

- **Purpose statement:** Internal evaluation of candidate AI models for the Overseer assistant within GnollHack.
- **Distillation / training prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.
- **Third-party model content:** Outputs generated by **Orion Max** (Northwind) and **Zeta Prime** (Midland), graded by models from Google, and described in this document by **Claude Opus 5.5** (Anthropic) are third-party content evaluated solely for domain-specific benchmark scoring and operational model selection.

---

*Document ID 212 · format version 14 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Full · models named*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically for structure, permitted figures and references, word limits, disclosure of benchmark text, peer names, significance claims, interval-overlap wording, hype words and spelling; the checks do not verify the prose's interpretations.*
