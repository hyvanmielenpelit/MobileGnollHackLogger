# Comparison #12 — 2 of 5 models: Internal Improvement Brief

**Comparison:** Comparison #12 · 5 models · computed 2026-09-21

*INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.*

- **Date:** 2026-09-28
- **Suite:** GnollHack Core Suite
- **Questions:** 6
- **Models:** 2 models (A and B), identities withheld
- **Coverage:** 2 of 5 models of Comparison #12; the other 3 are not part of this document.
- **Pricing basis:** Catalog prices on 2026-09-21

## Models compared

| Model | Intelligence Index | Rank | Median answer time | Cost per question | Critical errors |
|---|---|---|---|---|---|
| Model A | 85 (82–88) | 1 | 12.0 s | $0.050 | 0 of 6 answers |
| Model B | 55 (50–60) | 2 | 20.0 s | $0.010 | 0 of 6 answers |

*Each model's Intelligence Index is followed by its 95 % interval; a joint rank means its interval overlaps a neighbor's, so the order between them is not established by the intervals.*

## 1. Shared gaps

Q3 is a shared gap: most models scored low on it, so check the chat and the corpus first.

## 2. Model-specific gaps

Model B gave the weakest answer on Q3.

## 3. The benchmarking system

The rubric of Q3 may need a review, since most models scored low on it.

### Paired tests

*Each pair is compared on the questions both models answered. Each measure is its own family of tests, Holm-adjusted across the tests it makes over these models; a result is established when its adjusted p-value is below 0.05. Another set of models gives another family and another adjustment.*

**Against Model A (the reference)**

Intelligence: a single test, no adjustment.

| Pair | Questions | Difference (95 % interval) | Adjusted p | Result |
|---|---|---|---|---|
| Model A vs Model B | 6 | +30.0 points (+16.1 to +43.9) | 0.031 | Model A scored higher |

*Difference: the mean per-question difference, first model minus second, on the questions both answered.*

Speed: no test could be made. Cost: not tested. No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown.

| Pair | Time ratio (95 % interval) | Speed result | Spend ratio (95 % interval) | Cost result |
|---|---|---|---|---|
| Model A vs Model B | 1.00 (1.00 to 1.00) | not tested | — | not tested |

*A ratio is the first model's own time or spend per question divided by the second's, on the questions both answered.*

*At least one entry has a single run. With one run a side, a paired test captures question sampling only, not run-to-run variation, so it can look more certain than it is.*

## 4. Leads

*Provisional and un-triaged. A lead is not a finding: it must go through the triage, evidence bar and tool-layer diagnostics of `server_benchmark_to_chat_transfer` before anything is changed.*

- **[chat]** Check how the chat routes the question most models scored low on. *(evidence: Questions any critical error: 0 of 6 questions · Q3)*

## 5. Per-question matrix

| Q | Topic | Band | A | B | Spread |
|---|---|---|---|---|---|
| Q1 | An item question about gems | Intermediate | 92 | 50 | 42 |
| Q2 | An item question about prayer | Intermediate | 88 | 55 | 33 |
| Q3 | An item question about unicorns | Intermediate | 30 | 25 | 5 |
| Q4 | An item question about wands | Intermediate | 90 | 52 | 38 |
| Q5 | An item question about altars | Intermediate | 85 | 58 | 27 |
| Q6 | An item question about shops | Intermediate | 95 | 60 | 35 |

*Each model's column is headed by its letter in the models table. CE marks a critical error; Spread is the highest score minus the lowest. — under a model marks a question it was not asked or not scored on.*

### Question details

*Each answer shown is one the writer was given as an excerpt; the excerpts were chosen for the questions with critical errors or refuted sentences first, then the widest spread between models, then those every model scored low.*

#### Q1: An item question about gems

**Question:**

> Question 201

**Rubric:**

> - Rubric 201

##### Model A, run 31

**Answer:**

> Answer 201

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 92.

**Claim verifier:**

- No claims were checked.

##### Model B, run 35

**Answer:**

> Answer 201

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 50.

**Claim verifier:**

- No claims were checked.

#### Q2: An item question about prayer

**Question:**

> Question 202

**Rubric:**

> - Rubric 202

##### Model A, run 31

**Answer:**

> Answer 202

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 88.

**Claim verifier:**

- No claims were checked.

##### Model B, run 35

**Answer:**

> Answer 202

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 55.

**Claim verifier:**

- No claims were checked.

#### Q3: An item question about unicorns

**Question:**

> Question 203

**Rubric:**

> - Rubric 203

##### Model A, run 31

**Answer:**

> Answer 203

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 30.

**Claim verifier:**

- No claims were checked.

##### Model B, run 35

**Answer:**

> Answer 203

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 25.

**Claim verifier:**

- No claims were checked.

#### Q4: An item question about wands

**Question:**

> Question 204

**Rubric:**

> - Rubric 204

##### Model A, run 31

**Answer:**

> Answer 204

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 90.

**Claim verifier:**

- No claims were checked.

##### Model B, run 35

**Answer:**

> Answer 204

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 52.

**Claim verifier:**

- No claims were checked.

#### Q5: An item question about altars

**Question:**

> Question 205

**Rubric:**

> - Rubric 205

##### Model A, run 31

**Answer:**

> Answer 205

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 85.

**Claim verifier:**

- No claims were checked.

##### Model B, run 35

**Answer:**

> Answer 205

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 58.

**Claim verifier:**

- No claims were checked.

#### Q6: An item question about shops

**Question:**

> Question 206

**Rubric:**

> - Rubric 206

##### Model A, run 31

**Answer:**

> Answer 206

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 95.

**Claim verifier:**

- No claims were checked.

##### Model B, run 35

**Answer:**

> Answer 206

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 60.

**Claim verifier:**

- No claims were checked.

## 6. Fact sheet

```json
{
  "comparisonEntryCount": 5,
  "coversAllEntries": false,
  "entries": [
    {
      "costDegraded": false,
      "costPerQuestionUsd": 0.05,
      "costRank": 2,
      "entryKey": "run:31",
      "extra": [
        {
          "available": true,
          "display": "80",
          "key": "dimension.accuracy",
          "value": 80
        },
        {
          "available": true,
          "display": "80",
          "key": "dimension.completeness",
          "value": 80
        },
        {
          "available": true,
          "display": "80",
          "key": "dimension.conciseness",
          "value": 80
        },
        {
          "available": true,
          "display": "80",
          "key": "dimension.readability",
          "value": 80
        },
        {
          "available": true,
          "display": "0 of 6 answers",
          "key": "errors.critical",
          "value": 0
        },
        {
          "available": true,
          "display": "0.0",
          "key": "tools.callsPerQuestion",
          "value": 0
        }
      ],
      "firstRunUtc": "2026-09-21T07:00:00Z",
      "harnessVersion": "41",
      "isSubject": false,
      "lastRunUtc": "2026-09-21T07:00:00Z",
      "modelTimeP50Ms": 12000,
      "peerLetter": "A",
      "qualityIndex": 85,
      "qualityLower": 82,
      "qualityRank": 1,
      "qualityUpper": 88,
      "runCount": 1,
      "speedDegraded": false,
      "speedRank": 1
    },
    {
      "costDegraded": false,
      "costPerQuestionUsd": 0.01,
      "costRank": 1,
      "entryKey": "run:35",
      "extra": [
        {
          "available": true,
          "display": "50",
          "key": "dimension.accuracy",
          "value": 50
        },
        {
          "available": true,
          "display": "50",
          "key": "dimension.completeness",
          "value": 50
        },
        {
          "available": true,
          "display": "50",
          "key": "dimension.conciseness",
          "value": 50
        },
        {
          "available": true,
          "display": "50",
          "key": "dimension.readability",
          "value": 50
        },
        {
          "available": true,
          "display": "0 of 6 answers",
          "key": "errors.critical",
          "value": 0
        },
        {
          "available": true,
          "display": "0.0",
          "key": "tools.callsPerQuestion",
          "value": 0
        }
      ],
      "firstRunUtc": "2026-09-21T11:00:00Z",
      "harnessVersion": "41",
      "isSubject": false,
      "lastRunUtc": "2026-09-21T11:00:00Z",
      "modelTimeP50Ms": 20000,
      "peerLetter": "B",
      "qualityIndex": 55,
      "qualityLower": 50,
      "qualityRank": 2,
      "qualityUpper": 60,
      "runCount": 1,
      "speedDegraded": false,
      "speedRank": 2
    }
  ],
  "facts": [
    {
      "available": true,
      "display": "2",
      "key": "comparison.models",
      "value": 2
    },
    {
      "available": true,
      "display": "2026-09-21",
      "key": "comparison.pricedOn",
      "value": "2026-09-21"
    },
    {
      "available": true,
      "display": "Priced from the catalog as of 2026-09-20. Comparable across dates; not what was actually spent.",
      "key": "comparison.pricingBasis",
      "value": "Priced from the catalog as of 2026-09-20. Comparable across dates; not what was actually spent."
    },
    {
      "available": true,
      "display": "catalog",
      "key": "comparison.pricingBasisKind",
      "value": "catalog"
    },
    {
      "available": true,
      "display": "sig-7f3a91",
      "key": "comparison.signature",
      "value": "sig-7f3a91"
    },
    {
      "available": true,
      "display": "Models A and B",
      "key": "frontier.qualityCost",
      "value": 2
    },
    {
      "available": true,
      "display": "Model A",
      "key": "frontier.qualitySpeed",
      "value": 1
    },
    {
      "available": true,
      "display": "Models A and B",
      "key": "frontier.speedCost",
      "value": 2
    },
    {
      "available": true,
      "display": "6",
      "key": "model.A.answers.scored",
      "value": 6
    },
    {
      "available": true,
      "display": "0",
      "key": "model.A.band.advanced.questions",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.band.advanced.score",
      "unavailableReason": "The model has no scored answer in this band."
    },
    {
      "available": true,
      "display": "6",
      "key": "model.A.band.intermediate.questions",
      "value": 6
    },
    {
      "available": true,
      "display": "80",
      "key": "model.A.band.intermediate.score",
      "value": 80
    },
    {
      "available": true,
      "display": "0",
      "key": "model.A.band.simple.questions",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.band.simple.score",
      "unavailableReason": "The model has no scored answer in this band."
    },
    {
      "available": true,
      "display": "0",
      "key": "model.A.bands.authored.advanced",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.A.bands.authored.intermediate",
      "value": 0
    },
    {
      "available": true,
      "display": "6",
      "key": "model.A.bands.authored.simple",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.claims.indeterminate",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.claims.refuted",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.claims.supported",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": true,
      "display": "Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed",
      "key": "model.A.config.chat",
      "value": "Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed"
    },
    {
      "available": true,
      "display": "Current",
      "key": "model.A.cost.basis",
      "value": "Current"
    },
    {
      "available": true,
      "display": "$0.050",
      "key": "model.A.cost.perQuestion",
      "value": 0.05
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.cost.perRun",
      "unavailableReason": "Not recorded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.cost.pricingAsOf",
      "unavailableReason": "The price card publishes no date."
    },
    {
      "available": true,
      "display": "2nd of 2",
      "key": "model.A.cost.rank",
      "value": 2
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.cost.totalRunPerRun",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "80",
      "key": "model.A.dimension.accuracy",
      "value": 80
    },
    {
      "available": true,
      "display": "80",
      "key": "model.A.dimension.completeness",
      "value": 80
    },
    {
      "available": true,
      "display": "80",
      "key": "model.A.dimension.conciseness",
      "value": 80
    },
    {
      "available": true,
      "display": "80",
      "key": "model.A.dimension.readability",
      "value": 80
    },
    {
      "available": true,
      "display": "0 of 6 answers",
      "key": "model.A.errors.critical",
      "value": 0
    },
    {
      "available": true,
      "display": "on the intelligence against cost, intelligence against speed and speed against cost frontiers",
      "key": "model.A.frontier",
      "value": true
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.panel.disagreements",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.panel.icc",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.panel.meanAbsDelta",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.panel.memberAAlone",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.panel.memberBAlone",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.panel.referenceReaderIndex",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.panel.referenceReaderOffset",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": true,
      "display": "85 / 100",
      "key": "model.A.quality.index",
      "value": 85
    },
    {
      "available": true,
      "display": "82–88",
      "key": "model.A.quality.interval"
    },
    {
      "available": true,
      "display": "Item sampling only.",
      "key": "model.A.quality.intervalBasis",
      "value": "Item sampling only."
    },
    {
      "available": true,
      "display": "6 points",
      "key": "model.A.quality.intervalSpan",
      "value": 6
    },
    {
      "available": true,
      "display": "1st of 2",
      "key": "model.A.quality.rank",
      "value": 1
    },
    {
      "available": true,
      "display": "80 / 100",
      "key": "model.A.quality.rawIndex",
      "value": 80
    },
    {
      "available": true,
      "display": "1 of 1 question",
      "key": "model.A.quality.scoredItems",
      "value": 1
    },
    {
      "available": true,
      "display": "80 / 100",
      "key": "model.A.quality.unweightedMean",
      "value": 80
    },
    {
      "available": true,
      "display": "2026-09-21",
      "key": "model.A.run.dates"
    },
    {
      "available": true,
      "display": "41",
      "key": "model.A.run.harnessVersion"
    },
    {
      "available": true,
      "display": "31",
      "key": "model.A.run.ids"
    },
    {
      "available": true,
      "display": "e9b3e9a7c4d1",
      "key": "model.A.run.promptSha256"
    },
    {
      "available": true,
      "display": "f59d8b30a1c7",
      "key": "model.A.run.toolGuidesSha256"
    },
    {
      "available": true,
      "display": "1",
      "key": "model.A.runs",
      "value": 1
    },
    {
      "available": true,
      "display": "25",
      "key": "model.A.scoring.criticalErrorCap",
      "value": 25
    },
    {
      "available": true,
      "display": "1, 15, 35, 55, 72, 87, 100",
      "key": "model.A.scoring.levels"
    },
    {
      "available": true,
      "display": "12",
      "key": "model.A.scoring.methodVersion"
    },
    {
      "available": true,
      "display": "Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %",
      "key": "model.A.scoring.weights"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.speed.modelTimeMean",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "12.0 s",
      "key": "model.A.speed.modelTimeP50",
      "value": 12000
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.speed.modelTimeP90",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "1st of 2",
      "key": "model.A.speed.rank",
      "value": 1
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.speed.ttftP50",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "Comparable",
      "key": "model.A.state",
      "value": "Comparable"
    },
    {
      "available": true,
      "display": "No response-style conflict",
      "key": "model.A.style.responseStyleConflict",
      "value": false
    },
    {
      "available": true,
      "display": "1",
      "key": "model.A.suite.questions",
      "value": 1
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.A.thinkingLevel"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.tokens.inputPerQuestion",
      "unavailableReason": "No answer recorded its token counts."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.tokens.outputPerQuestion",
      "unavailableReason": "No answer recorded its token counts."
    },
    {
      "available": true,
      "display": "0.0",
      "key": "model.A.tools.callsPerQuestion",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.A.tools.failed",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.A.tools.refusedByBudget",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.tools.share.knowledgeBase",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.tools.share.other",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.tools.share.sourceCode",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.tools.share.structuredLookup",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.tools.share.wiki",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.tools.zeroKnowledgeBaseAnswers",
      "unavailableReason": "No question of this suite is a knowledge-base topic; the prompt routes game mechanics past the knowledge base."
    },
    {
      "available": true,
      "display": "6",
      "key": "model.B.answers.scored",
      "value": 6
    },
    {
      "available": true,
      "display": "0",
      "key": "model.B.band.advanced.questions",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.band.advanced.score",
      "unavailableReason": "The model has no scored answer in this band."
    },
    {
      "available": true,
      "display": "6",
      "key": "model.B.band.intermediate.questions",
      "value": 6
    },
    {
      "available": true,
      "display": "50",
      "key": "model.B.band.intermediate.score",
      "value": 50
    },
    {
      "available": true,
      "display": "0",
      "key": "model.B.band.simple.questions",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.band.simple.score",
      "unavailableReason": "The model has no scored answer in this band."
    },
    {
      "available": true,
      "display": "0",
      "key": "model.B.bands.authored.advanced",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.B.bands.authored.intermediate",
      "value": 0
    },
    {
      "available": true,
      "display": "6",
      "key": "model.B.bands.authored.simple",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.claims.indeterminate",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.claims.refuted",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.claims.supported",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": true,
      "display": "Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed",
      "key": "model.B.config.chat",
      "value": "Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed"
    },
    {
      "available": true,
      "display": "Current",
      "key": "model.B.cost.basis",
      "value": "Current"
    },
    {
      "available": true,
      "display": "$0.010",
      "key": "model.B.cost.perQuestion",
      "value": 0.01
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.cost.perRun",
      "unavailableReason": "Not recorded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.cost.pricingAsOf",
      "unavailableReason": "The price card publishes no date."
    },
    {
      "available": true,
      "display": "1st of 2",
      "key": "model.B.cost.rank",
      "value": 1
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.cost.totalRunPerRun",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "50",
      "key": "model.B.dimension.accuracy",
      "value": 50
    },
    {
      "available": true,
      "display": "50",
      "key": "model.B.dimension.completeness",
      "value": 50
    },
    {
      "available": true,
      "display": "50",
      "key": "model.B.dimension.conciseness",
      "value": 50
    },
    {
      "available": true,
      "display": "50",
      "key": "model.B.dimension.readability",
      "value": 50
    },
    {
      "available": true,
      "display": "0 of 6 answers",
      "key": "model.B.errors.critical",
      "value": 0
    },
    {
      "available": true,
      "display": "on the intelligence against cost and speed against cost frontiers",
      "key": "model.B.frontier",
      "value": true
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.panel.disagreements",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.panel.icc",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.panel.meanAbsDelta",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.panel.memberAAlone",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.panel.memberBAlone",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.panel.referenceReaderIndex",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.panel.referenceReaderOffset",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": true,
      "display": "55 / 100",
      "key": "model.B.quality.index",
      "value": 55
    },
    {
      "available": true,
      "display": "50–60",
      "key": "model.B.quality.interval"
    },
    {
      "available": true,
      "display": "Item sampling only.",
      "key": "model.B.quality.intervalBasis",
      "value": "Item sampling only."
    },
    {
      "available": true,
      "display": "10 points",
      "key": "model.B.quality.intervalSpan",
      "value": 10
    },
    {
      "available": true,
      "display": "2nd of 2",
      "key": "model.B.quality.rank",
      "value": 2
    },
    {
      "available": true,
      "display": "50 / 100",
      "key": "model.B.quality.rawIndex",
      "value": 50
    },
    {
      "available": true,
      "display": "1 of 1 question",
      "key": "model.B.quality.scoredItems",
      "value": 1
    },
    {
      "available": true,
      "display": "50 / 100",
      "key": "model.B.quality.unweightedMean",
      "value": 50
    },
    {
      "available": true,
      "display": "2026-09-21",
      "key": "model.B.run.dates"
    },
    {
      "available": true,
      "display": "41",
      "key": "model.B.run.harnessVersion"
    },
    {
      "available": true,
      "display": "35",
      "key": "model.B.run.ids"
    },
    {
      "available": true,
      "display": "e9b3e9a7c4d1",
      "key": "model.B.run.promptSha256"
    },
    {
      "available": true,
      "display": "f59d8b30a1c7",
      "key": "model.B.run.toolGuidesSha256"
    },
    {
      "available": true,
      "display": "1",
      "key": "model.B.runs",
      "value": 1
    },
    {
      "available": true,
      "display": "25",
      "key": "model.B.scoring.criticalErrorCap",
      "value": 25
    },
    {
      "available": true,
      "display": "1, 15, 35, 55, 72, 87, 100",
      "key": "model.B.scoring.levels"
    },
    {
      "available": true,
      "display": "12",
      "key": "model.B.scoring.methodVersion"
    },
    {
      "available": true,
      "display": "Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %",
      "key": "model.B.scoring.weights"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.speed.modelTimeMean",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "20.0 s",
      "key": "model.B.speed.modelTimeP50",
      "value": 20000
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.speed.modelTimeP90",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "2nd of 2",
      "key": "model.B.speed.rank",
      "value": 2
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.speed.ttftP50",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "Comparable",
      "key": "model.B.state",
      "value": "Comparable"
    },
    {
      "available": true,
      "display": "No response-style conflict",
      "key": "model.B.style.responseStyleConflict",
      "value": false
    },
    {
      "available": true,
      "display": "1",
      "key": "model.B.suite.questions",
      "value": 1
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.B.thinkingLevel"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.tokens.inputPerQuestion",
      "unavailableReason": "No answer recorded its token counts."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.tokens.outputPerQuestion",
      "unavailableReason": "No answer recorded its token counts."
    },
    {
      "available": true,
      "display": "0.0",
      "key": "model.B.tools.callsPerQuestion",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.B.tools.failed",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.B.tools.refusedByBudget",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.tools.share.knowledgeBase",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.tools.share.other",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.tools.share.sourceCode",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.tools.share.structuredLookup",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.tools.share.wiki",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.tools.zeroKnowledgeBaseAnswers",
      "unavailableReason": "No question of this suite is a knowledge-base topic; the prompt routes game mechanics past the knowledge base."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.B.cost.ratio",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.B.cost.reference",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "the 95 % intervals of Model A and Model B do not overlap",
      "key": "pair.A.B.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "+30.0 points (Model A minus Model B)",
      "key": "pair.A.B.quality.difference",
      "value": 30
    },
    {
      "available": true,
      "display": "+16.1 to +43.9",
      "key": "pair.A.B.quality.interval"
    },
    {
      "available": true,
      "display": "Model A scored higher on the same questions, established as a single test (adjusted p 0.031)",
      "key": "pair.A.B.quality.reference",
      "value": true
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.A.B.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "Model A took 1.00 times as long as Model B on the same questions (1.00 to 1.00)",
      "key": "pair.A.B.speed.ratio",
      "value": 1
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.B.speed.reference",
      "unavailableReason": "The pair was not tested."
    },
    {
      "available": true,
      "display": "0 of 6 questions",
      "key": "questions.anyCriticalError",
      "value": 0
    },
    {
      "available": true,
      "display": "1 of 6 questions",
      "key": "questions.sharedLow",
      "value": 1
    },
    {
      "available": true,
      "display": "5 of 6 questions",
      "key": "questions.wideSpread",
      "value": 5
    },
    {
      "available": true,
      "display": "from $0.050 (Model A) to $0.010 (Model B)",
      "key": "spread.cost",
      "value": 0.04
    },
    {
      "available": true,
      "display": "from 50 (Model B) to 80 (Model A), 30 points apart",
      "key": "spread.dimension.accuracy",
      "value": 30
    },
    {
      "available": true,
      "display": "from 50 (Model B) to 80 (Model A), 30 points apart",
      "key": "spread.dimension.completeness",
      "value": 30
    },
    {
      "available": true,
      "display": "from 50 (Model B) to 80 (Model A), 30 points apart",
      "key": "spread.dimension.conciseness",
      "value": 30
    },
    {
      "available": true,
      "display": "from 50 (Model B) to 80 (Model A), 30 points apart",
      "key": "spread.dimension.readability",
      "value": 30
    },
    {
      "available": true,
      "display": "from 55 (Model B) to 85 (Model A), 30 points apart",
      "key": "spread.quality",
      "value": 30
    },
    {
      "available": true,
      "display": "from 20.0 s (Model B) to 12.0 s (Model A)",
      "key": "spread.speed",
      "value": 8000
    },
    {
      "available": true,
      "display": "GnollHack Core Suite",
      "key": "suite.name",
      "value": "GnollHack Core Suite"
    }
  ],
  "graders": [
    {
      "label": "Gemini 3.8 Flash",
      "modelId": "gemini-3.8-flash",
      "provider": "Google",
      "role": "Assessor",
      "sameFamilyAsSubject": false
    }
  ],
  "knownNames": [
    "Gemini 3.8 Flash",
    "GnollHack Core Suite",
    "Google",
    "gemini-3.8-flash"
  ],
  "models": [
    {
      "entryKey": "run:31",
      "label": "Model A",
      "letter": "A",
      "provider": ""
    },
    {
      "entryKey": "run:35",
      "label": "Model B",
      "letter": "B",
      "provider": ""
    }
  ],
  "noSignificanceInstead": "",
  "noSignificanceSummary": "",
  "pairedDifferences": [],
  "pairedTests": [
    {
      "measures": [
        {
          "adjustment": "None",
          "adjustmentNote": "Single comparison — no adjustment needed",
          "familySize": 1,
          "measure": "Intelligence",
          "pairs": [
            {
              "adjustedPValue": 0.03125,
              "effect": 30,
              "effectKind": "Difference",
              "established": true,
              "favors": "First",
              "firstLetter": "A",
              "lower": 16.109242522022058,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "B",
              "upper": 43.89075747797794
            }
          ]
        },
        {
          "adjustment": "None",
          "adjustmentNote": "No test was made in this family.",
          "familySize": 0,
          "measure": "Speed",
          "pairs": [
            {
              "effect": 1,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 1,
              "pairedItems": 6,
              "secondLetter": "B",
              "upper": 1
            }
          ]
        },
        {
          "adjustment": "None",
          "adjustmentNote": "No test was made in this family.",
          "familySize": 0,
          "measure": "Cost",
          "notTestedReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown.",
          "pairs": [
            {
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "notTestedReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown.",
              "pairedItems": 0,
              "secondLetter": "B"
            }
          ]
        }
      ],
      "mode": "Reference",
      "referenceLetter": "A",
      "singleRunCaveat": "At least one entry has a single run. With one run a side, a paired test captures question sampling only, not run-to-run variation, so it can look more certain than it is."
    }
  ],
  "peers": [
    {
      "costDegraded": false,
      "displayName": "Model A",
      "entryKey": "run:31",
      "explanation": "Comparable: measured under the baseline condition.",
      "label": "Model A",
      "letter": "A",
      "modelId": "",
      "provider": "",
      "runIds": [
        31
      ],
      "speedDegraded": false,
      "state": "Comparable"
    },
    {
      "costDegraded": false,
      "displayName": "Model B",
      "entryKey": "run:35",
      "explanation": "Comparable: measured under the baseline condition.",
      "label": "Model B",
      "letter": "B",
      "modelId": "",
      "provider": "",
      "runIds": [
        35
      ],
      "speedDegraded": false,
      "state": "Comparable"
    }
  ],
  "purposeStatements": [
    "Internal evaluation of candidate AI models for the Overseer assistant within GnollHack."
  ],
  "questions": [
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "criticalError": false,
      "detailed": true,
      "excerptLetters": [
        "A",
        "B"
      ],
      "itemRevisionUsed": 1,
      "modelTimeMs": 10000,
      "models": [
        {
          "criticalError": false,
          "letter": "A",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 92,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "B",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 50,
          "toolCalls": 0
        }
      ],
      "number": 1,
      "orderIndex": 1,
      "peerCount": 2,
      "peerMax": 92,
      "peerMin": 50,
      "peersAbove": 0,
      "questionKey": "201",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 2,
      "score": 71,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "criticalError": false,
      "detailed": true,
      "excerptLetters": [
        "A",
        "B"
      ],
      "itemRevisionUsed": 1,
      "modelTimeMs": 10000,
      "models": [
        {
          "criticalError": false,
          "letter": "A",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 88,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "B",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 55,
          "toolCalls": 0
        }
      ],
      "number": 2,
      "orderIndex": 2,
      "peerCount": 2,
      "peerMax": 88,
      "peerMin": 55,
      "peersAbove": 0,
      "questionKey": "202",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 2,
      "score": 71.5,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "criticalError": false,
      "detailed": true,
      "excerptLetters": [
        "A",
        "B"
      ],
      "itemRevisionUsed": 1,
      "modelTimeMs": 10000,
      "models": [
        {
          "criticalError": false,
          "letter": "A",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 30,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "B",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 25,
          "toolCalls": 0
        }
      ],
      "number": 3,
      "orderIndex": 3,
      "peerCount": 2,
      "peerMax": 30,
      "peerMin": 25,
      "peersAbove": 0,
      "questionKey": "203",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 2,
      "score": 27.5,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "criticalError": false,
      "detailed": true,
      "excerptLetters": [
        "A",
        "B"
      ],
      "itemRevisionUsed": 1,
      "modelTimeMs": 10000,
      "models": [
        {
          "criticalError": false,
          "letter": "A",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 90,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "B",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 52,
          "toolCalls": 0
        }
      ],
      "number": 4,
      "orderIndex": 4,
      "peerCount": 2,
      "peerMax": 90,
      "peerMin": 52,
      "peersAbove": 0,
      "questionKey": "204",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 2,
      "score": 71,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "criticalError": false,
      "detailed": true,
      "excerptLetters": [
        "A",
        "B"
      ],
      "itemRevisionUsed": 1,
      "modelTimeMs": 10000,
      "models": [
        {
          "criticalError": false,
          "letter": "A",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 85,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "B",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 58,
          "toolCalls": 0
        }
      ],
      "number": 5,
      "orderIndex": 5,
      "peerCount": 2,
      "peerMax": 85,
      "peerMin": 58,
      "peersAbove": 0,
      "questionKey": "205",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 2,
      "score": 71.5,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "criticalError": false,
      "detailed": true,
      "excerptLetters": [
        "A",
        "B"
      ],
      "itemRevisionUsed": 1,
      "modelTimeMs": 10000,
      "models": [
        {
          "criticalError": false,
          "letter": "A",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 95,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "B",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 60,
          "toolCalls": 0
        }
      ],
      "number": 6,
      "orderIndex": 6,
      "peerCount": 2,
      "peerMax": 95,
      "peerMin": 60,
      "peersAbove": 0,
      "questionKey": "206",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 2,
      "score": 77.5,
      "toolCalls": 0
    }
  ],
  "rows": [],
  "scope": "Comparison",
  "subjectDisplayName": "",
  "subjectExplanation": "",
  "subjectKey": "comparison:12/2ec087bf91da0268",
  "subjectKind": "Comparison",
  "subjectLabel": "Comparison #12 · 2 of 5 models",
  "subjectModelId": "",
  "subjectProvider": "",
  "subjectRunIds": [
    31,
    35
  ],
  "subjectState": "Comparable",
  "suiteId": 5,
  "suiteName": "GnollHack Core Suite"
}
```

---

*Document ID 212 · format version 12 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Full · models anonymized*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically for structure, permitted figures and references, word limits, disclosure of benchmark text, peer names, significance claims, interval-overlap wording, hype words and spelling; the checks do not verify the prose's interpretations.*
