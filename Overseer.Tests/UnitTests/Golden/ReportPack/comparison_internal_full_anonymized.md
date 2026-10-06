# Comparison #12: Internal Improvement Brief

**Comparison:** Comparison #12 · 5 models · computed 2026-09-21

*INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.*

- **Date:** 2026-09-28
- **Suite:** GnollHack Core Suite
- **Questions:** 6
- **Models:** 5 models (A to E), identities withheld
- **Pricing basis:** Catalog prices on 2026-09-21

## Models compared

| Model | Intelligence Index | Rank | Median answer time | Cost per question | Critical errors |
|---|---|---|---|---|---|
| Model A | 85 (82–88) | joint 1 | 12.0 s | $0.050 | 0 of 6 answers |
| Model B | 80 (77–83) | joint 1 | 9.0 s | $0.030 | 0 of 6 answers |
| Model C | 70 (66–74) | joint 3 | 15.0 s | $0.020 | 1 of 6 answers |
| Model D | 70 (64–76) | joint 3 | 8.0 s | $0.060 | 0 of 6 answers |
| Model E | 55 (50–60) | 5 | 20.0 s | $0.010 | 0 of 6 answers |

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

## 4. Leads

*Provisional and un-triaged. A lead is not a finding: it must go through the triage, evidence bar and tool-layer diagnostics of `server_benchmark_to_chat_transfer` before anything is changed.*

- **[chat]** Check how the chat routes the question most models scored low on. *(evidence: Questions any critical error: 1 of 6 questions · Q3)*

## 5. Per-question matrix

| Q | Topic | Band | A | B | C | D | E | Spread |
|---|---|---|---|---|---|---|---|---|
| Q1 | An item question about gems | Intermediate | 92 | 85 | 70 | 75 | 50 | 42 |
| Q2 | An item question about prayer | Intermediate | 88 | 80 | 72 | 60 | 55 | 33 |
| Q3 | An item question about unicorns | Intermediate | 30 | 70 | 28 CE | 32 | 25 | 45 |
| Q4 | An item question about wands | Intermediate | 90 | 75 | 65 | 72 | 52 | 38 |
| Q5 | An item question about altars | Intermediate | 85 | 82 | 74 | 68 | 58 | 27 |
| Q6 | An item question about shops | Intermediate | 95 | 88 | 80 | 78 | 60 | 35 |

*Each model's column is headed by its letter in the models table. CE marks a critical error; Spread is the highest score minus the lowest; — marks a question the model was not asked or not scored on.*

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

##### Model B, run 32

**Answer:**

> Answer 201

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 85.

**Claim verifier:**

- No claims were checked.

##### Model C, run 33

**Answer:**

> Answer 201

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 70.

**Claim verifier:**

- No claims were checked.

##### Model D, run 34

**Answer:**

> Answer 201

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 75.

**Claim verifier:**

- No claims were checked.

##### Model E, run 35

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

##### Model B, run 32

**Answer:**

> Answer 202

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 80.

**Claim verifier:**

- No claims were checked.

##### Model C, run 33

**Answer:**

> Answer 202

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 72.

**Claim verifier:**

- No claims were checked.

##### Model D, run 34

**Answer:**

> Answer 202

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 60.

**Claim verifier:**

- No claims were checked.

##### Model E, run 35

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

##### Model B, run 32

**Answer:**

> Answer 203

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 70.

**Claim verifier:**

- No claims were checked.

##### Model C, run 33

**Answer:**

> Answer 203

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 28.
  - Critical error: "Answer 203"

**Claim verifier:**

- No claims were checked.

##### Model D, run 34

**Answer:**

> Answer 203

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 32.

**Claim verifier:**

- No claims were checked.

##### Model E, run 35

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

##### Model B, run 32

**Answer:**

> Answer 204

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 75.

**Claim verifier:**

- No claims were checked.

##### Model C, run 33

**Answer:**

> Answer 204

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 65.

**Claim verifier:**

- No claims were checked.

##### Model D, run 34

**Answer:**

> Answer 204

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 72.

**Claim verifier:**

- No claims were checked.

##### Model E, run 35

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

##### Model B, run 32

**Answer:**

> Answer 205

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 82.

**Claim verifier:**

- No claims were checked.

##### Model C, run 33

**Answer:**

> Answer 205

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 74.

**Claim verifier:**

- No claims were checked.

##### Model D, run 34

**Answer:**

> Answer 205

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 68.

**Claim verifier:**

- No claims were checked.

##### Model E, run 35

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

##### Model B, run 32

**Answer:**

> Answer 206

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 88.

**Claim verifier:**

- No claims were checked.

##### Model C, run 33

**Answer:**

> Answer 206

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 80.

**Claim verifier:**

- No claims were checked.

##### Model D, run 34

**Answer:**

> Answer 206

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 78.

**Claim verifier:**

- No claims were checked.

##### Model E, run 35

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
  "coversAllEntries": true,
  "entries": [
    {
      "costDegraded": false,
      "costPerQuestionUsd": 0.05,
      "costRank": 4,
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
      "speedRank": 3
    },
    {
      "costDegraded": false,
      "costPerQuestionUsd": 0.03,
      "costRank": 3,
      "entryKey": "run:32",
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
      "firstRunUtc": "2026-09-21T08:00:00Z",
      "harnessVersion": "41",
      "isSubject": false,
      "lastRunUtc": "2026-09-21T08:00:00Z",
      "modelTimeP50Ms": 9000,
      "peerLetter": "B",
      "qualityIndex": 80,
      "qualityLower": 77,
      "qualityRank": 1,
      "qualityUpper": 83,
      "runCount": 1,
      "speedDegraded": false,
      "speedRank": 2
    },
    {
      "costDegraded": false,
      "costPerQuestionUsd": 0.02,
      "costRank": 2,
      "entryKey": "run:33",
      "extra": [
        {
          "available": true,
          "display": "65",
          "key": "dimension.accuracy",
          "value": 64.83333333333333
        },
        {
          "available": true,
          "display": "65",
          "key": "dimension.completeness",
          "value": 64.83333333333333
        },
        {
          "available": true,
          "display": "65",
          "key": "dimension.conciseness",
          "value": 64.83333333333333
        },
        {
          "available": true,
          "display": "65",
          "key": "dimension.readability",
          "value": 64.83333333333333
        },
        {
          "available": true,
          "display": "1 of 6 answers",
          "key": "errors.critical",
          "value": 1
        },
        {
          "available": true,
          "display": "0.0",
          "key": "tools.callsPerQuestion",
          "value": 0
        }
      ],
      "firstRunUtc": "2026-09-21T09:00:00Z",
      "harnessVersion": "41",
      "isSubject": false,
      "lastRunUtc": "2026-09-21T09:00:00Z",
      "modelTimeP50Ms": 15000,
      "peerLetter": "C",
      "qualityIndex": 70,
      "qualityLower": 66,
      "qualityRank": 3,
      "qualityUpper": 74,
      "runCount": 1,
      "speedDegraded": false,
      "speedRank": 4
    },
    {
      "costDegraded": false,
      "costPerQuestionUsd": 0.06,
      "costRank": 5,
      "entryKey": "run:34",
      "extra": [
        {
          "available": true,
          "display": "64",
          "key": "dimension.accuracy",
          "value": 64.16666666666667
        },
        {
          "available": true,
          "display": "64",
          "key": "dimension.completeness",
          "value": 64.16666666666667
        },
        {
          "available": true,
          "display": "64",
          "key": "dimension.conciseness",
          "value": 64.16666666666667
        },
        {
          "available": true,
          "display": "64",
          "key": "dimension.readability",
          "value": 64.16666666666667
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
      "firstRunUtc": "2026-09-21T10:00:00Z",
      "harnessVersion": "41",
      "isSubject": false,
      "lastRunUtc": "2026-09-21T10:00:00Z",
      "modelTimeP50Ms": 8000,
      "peerLetter": "D",
      "qualityIndex": 70,
      "qualityLower": 64,
      "qualityRank": 3,
      "qualityUpper": 76,
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
      "peerLetter": "E",
      "qualityIndex": 55,
      "qualityLower": 50,
      "qualityRank": 5,
      "qualityUpper": 60,
      "runCount": 1,
      "speedDegraded": false,
      "speedRank": 5
    }
  ],
  "facts": [
    {
      "available": true,
      "display": "5",
      "key": "comparison.models",
      "value": 5
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
      "display": "Models A, B, C and E",
      "key": "frontier.qualityCost",
      "value": 4
    },
    {
      "available": true,
      "display": "Models A, B and D",
      "key": "frontier.qualitySpeed",
      "value": 3
    },
    {
      "available": true,
      "display": "Models B, C, D and E",
      "key": "frontier.speedCost",
      "value": 4
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
      "display": "4th of 5",
      "key": "model.A.cost.rank",
      "value": 4
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
      "display": "on the intelligence against cost and intelligence against speed frontiers",
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
      "display": "joint 1st of 5 (intervals overlap)",
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
      "display": "3rd of 5",
      "key": "model.A.speed.rank",
      "value": 3
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
      "display": "80",
      "key": "model.B.band.intermediate.score",
      "value": 80
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
      "display": "$0.030",
      "key": "model.B.cost.perQuestion",
      "value": 0.03
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
      "display": "3rd of 5",
      "key": "model.B.cost.rank",
      "value": 3
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.cost.totalRunPerRun",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "80",
      "key": "model.B.dimension.accuracy",
      "value": 80
    },
    {
      "available": true,
      "display": "80",
      "key": "model.B.dimension.completeness",
      "value": 80
    },
    {
      "available": true,
      "display": "80",
      "key": "model.B.dimension.conciseness",
      "value": 80
    },
    {
      "available": true,
      "display": "80",
      "key": "model.B.dimension.readability",
      "value": 80
    },
    {
      "available": true,
      "display": "0 of 6 answers",
      "key": "model.B.errors.critical",
      "value": 0
    },
    {
      "available": true,
      "display": "on the intelligence against cost, intelligence against speed and speed against cost frontiers",
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
      "display": "80 / 100",
      "key": "model.B.quality.index",
      "value": 80
    },
    {
      "available": true,
      "display": "77–83",
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
      "display": "6 points",
      "key": "model.B.quality.intervalSpan",
      "value": 6
    },
    {
      "available": true,
      "display": "joint 1st of 5 (intervals overlap)",
      "key": "model.B.quality.rank",
      "value": 1
    },
    {
      "available": true,
      "display": "80 / 100",
      "key": "model.B.quality.rawIndex",
      "value": 80
    },
    {
      "available": true,
      "display": "1 of 1 question",
      "key": "model.B.quality.scoredItems",
      "value": 1
    },
    {
      "available": true,
      "display": "80 / 100",
      "key": "model.B.quality.unweightedMean",
      "value": 80
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
      "display": "32",
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
      "display": "9.0 s",
      "key": "model.B.speed.modelTimeP50",
      "value": 9000
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.speed.modelTimeP90",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "2nd of 5",
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
      "available": true,
      "display": "6",
      "key": "model.C.answers.scored",
      "value": 6
    },
    {
      "available": true,
      "display": "0",
      "key": "model.C.band.advanced.questions",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.band.advanced.score",
      "unavailableReason": "The model has no scored answer in this band."
    },
    {
      "available": true,
      "display": "6",
      "key": "model.C.band.intermediate.questions",
      "value": 6
    },
    {
      "available": true,
      "display": "65",
      "key": "model.C.band.intermediate.score",
      "value": 64.83333333333333
    },
    {
      "available": true,
      "display": "0",
      "key": "model.C.band.simple.questions",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.band.simple.score",
      "unavailableReason": "The model has no scored answer in this band."
    },
    {
      "available": true,
      "display": "0",
      "key": "model.C.bands.authored.advanced",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.C.bands.authored.intermediate",
      "value": 0
    },
    {
      "available": true,
      "display": "6",
      "key": "model.C.bands.authored.simple",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.claims.indeterminate",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.claims.refuted",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.claims.supported",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": true,
      "display": "Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed",
      "key": "model.C.config.chat",
      "value": "Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed"
    },
    {
      "available": true,
      "display": "Current",
      "key": "model.C.cost.basis",
      "value": "Current"
    },
    {
      "available": true,
      "display": "$0.020",
      "key": "model.C.cost.perQuestion",
      "value": 0.02
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.cost.perRun",
      "unavailableReason": "Not recorded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.cost.pricingAsOf",
      "unavailableReason": "The price card publishes no date."
    },
    {
      "available": true,
      "display": "2nd of 5",
      "key": "model.C.cost.rank",
      "value": 2
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.cost.totalRunPerRun",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "65",
      "key": "model.C.dimension.accuracy",
      "value": 64.83333333333333
    },
    {
      "available": true,
      "display": "65",
      "key": "model.C.dimension.completeness",
      "value": 64.83333333333333
    },
    {
      "available": true,
      "display": "65",
      "key": "model.C.dimension.conciseness",
      "value": 64.83333333333333
    },
    {
      "available": true,
      "display": "65",
      "key": "model.C.dimension.readability",
      "value": 64.83333333333333
    },
    {
      "available": true,
      "display": "1 of 6 answers",
      "key": "model.C.errors.critical",
      "value": 1
    },
    {
      "available": true,
      "display": "on the intelligence against cost and speed against cost frontiers",
      "key": "model.C.frontier",
      "value": true
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.panel.disagreements",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.panel.icc",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.panel.meanAbsDelta",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.panel.memberAAlone",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.panel.memberBAlone",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.panel.referenceReaderIndex",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.panel.referenceReaderOffset",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": true,
      "display": "70 / 100",
      "key": "model.C.quality.index",
      "value": 70
    },
    {
      "available": true,
      "display": "66–74",
      "key": "model.C.quality.interval"
    },
    {
      "available": true,
      "display": "Item sampling only.",
      "key": "model.C.quality.intervalBasis",
      "value": "Item sampling only."
    },
    {
      "available": true,
      "display": "8 points",
      "key": "model.C.quality.intervalSpan",
      "value": 8
    },
    {
      "available": true,
      "display": "joint 3rd of 5 (intervals overlap)",
      "key": "model.C.quality.rank",
      "value": 3
    },
    {
      "available": true,
      "display": "65 / 100",
      "key": "model.C.quality.rawIndex",
      "value": 65
    },
    {
      "available": true,
      "display": "1 of 1 question",
      "key": "model.C.quality.scoredItems",
      "value": 1
    },
    {
      "available": true,
      "display": "65 / 100",
      "key": "model.C.quality.unweightedMean",
      "value": 65
    },
    {
      "available": true,
      "display": "2026-09-21",
      "key": "model.C.run.dates"
    },
    {
      "available": true,
      "display": "41",
      "key": "model.C.run.harnessVersion"
    },
    {
      "available": true,
      "display": "33",
      "key": "model.C.run.ids"
    },
    {
      "available": true,
      "display": "e9b3e9a7c4d1",
      "key": "model.C.run.promptSha256"
    },
    {
      "available": true,
      "display": "f59d8b30a1c7",
      "key": "model.C.run.toolGuidesSha256"
    },
    {
      "available": true,
      "display": "1",
      "key": "model.C.runs",
      "value": 1
    },
    {
      "available": true,
      "display": "25",
      "key": "model.C.scoring.criticalErrorCap",
      "value": 25
    },
    {
      "available": true,
      "display": "1, 15, 35, 55, 72, 87, 100",
      "key": "model.C.scoring.levels"
    },
    {
      "available": true,
      "display": "12",
      "key": "model.C.scoring.methodVersion"
    },
    {
      "available": true,
      "display": "Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %",
      "key": "model.C.scoring.weights"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.speed.modelTimeMean",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "15.0 s",
      "key": "model.C.speed.modelTimeP50",
      "value": 15000
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.speed.modelTimeP90",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "4th of 5",
      "key": "model.C.speed.rank",
      "value": 4
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.speed.ttftP50",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "Comparable",
      "key": "model.C.state",
      "value": "Comparable"
    },
    {
      "available": true,
      "display": "No response-style conflict",
      "key": "model.C.style.responseStyleConflict",
      "value": false
    },
    {
      "available": true,
      "display": "1",
      "key": "model.C.suite.questions",
      "value": 1
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.C.thinkingLevel"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.tokens.inputPerQuestion",
      "unavailableReason": "No answer recorded its token counts."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.tokens.outputPerQuestion",
      "unavailableReason": "No answer recorded its token counts."
    },
    {
      "available": true,
      "display": "0.0",
      "key": "model.C.tools.callsPerQuestion",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.C.tools.failed",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.C.tools.refusedByBudget",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.tools.share.knowledgeBase",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.tools.share.other",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.tools.share.sourceCode",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.tools.share.structuredLookup",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.tools.share.wiki",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.tools.zeroKnowledgeBaseAnswers",
      "unavailableReason": "No question of this suite is a knowledge-base topic; the prompt routes game mechanics past the knowledge base."
    },
    {
      "available": true,
      "display": "6",
      "key": "model.D.answers.scored",
      "value": 6
    },
    {
      "available": true,
      "display": "0",
      "key": "model.D.band.advanced.questions",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.band.advanced.score",
      "unavailableReason": "The model has no scored answer in this band."
    },
    {
      "available": true,
      "display": "6",
      "key": "model.D.band.intermediate.questions",
      "value": 6
    },
    {
      "available": true,
      "display": "64",
      "key": "model.D.band.intermediate.score",
      "value": 64.16666666666667
    },
    {
      "available": true,
      "display": "0",
      "key": "model.D.band.simple.questions",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.band.simple.score",
      "unavailableReason": "The model has no scored answer in this band."
    },
    {
      "available": true,
      "display": "0",
      "key": "model.D.bands.authored.advanced",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.D.bands.authored.intermediate",
      "value": 0
    },
    {
      "available": true,
      "display": "6",
      "key": "model.D.bands.authored.simple",
      "value": 6
    },
    {
      "available": true,
      "display": "0",
      "key": "model.D.claims.indeterminate",
      "value": 0
    },
    {
      "available": true,
      "display": "1",
      "key": "model.D.claims.refuted",
      "value": 1
    },
    {
      "available": true,
      "display": "0",
      "key": "model.D.claims.supported",
      "value": 0
    },
    {
      "available": true,
      "display": "Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed",
      "key": "model.D.config.chat",
      "value": "Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed"
    },
    {
      "available": true,
      "display": "Current",
      "key": "model.D.cost.basis",
      "value": "Current"
    },
    {
      "available": true,
      "display": "$0.060",
      "key": "model.D.cost.perQuestion",
      "value": 0.06
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.cost.perRun",
      "unavailableReason": "Not recorded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.cost.pricingAsOf",
      "unavailableReason": "The price card publishes no date."
    },
    {
      "available": true,
      "display": "5th of 5",
      "key": "model.D.cost.rank",
      "value": 5
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.cost.totalRunPerRun",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "64",
      "key": "model.D.dimension.accuracy",
      "value": 64.16666666666667
    },
    {
      "available": true,
      "display": "64",
      "key": "model.D.dimension.completeness",
      "value": 64.16666666666667
    },
    {
      "available": true,
      "display": "64",
      "key": "model.D.dimension.conciseness",
      "value": 64.16666666666667
    },
    {
      "available": true,
      "display": "64",
      "key": "model.D.dimension.readability",
      "value": 64.16666666666667
    },
    {
      "available": true,
      "display": "0 of 6 answers",
      "key": "model.D.errors.critical",
      "value": 0
    },
    {
      "available": true,
      "display": "on the intelligence against speed and speed against cost frontiers",
      "key": "model.D.frontier",
      "value": true
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.panel.disagreements",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.panel.icc",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.panel.meanAbsDelta",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.panel.memberAAlone",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.panel.memberBAlone",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.panel.referenceReaderIndex",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.panel.referenceReaderOffset",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": true,
      "display": "70 / 100",
      "key": "model.D.quality.index",
      "value": 70
    },
    {
      "available": true,
      "display": "64–76",
      "key": "model.D.quality.interval"
    },
    {
      "available": true,
      "display": "Item sampling only.",
      "key": "model.D.quality.intervalBasis",
      "value": "Item sampling only."
    },
    {
      "available": true,
      "display": "12 points",
      "key": "model.D.quality.intervalSpan",
      "value": 12
    },
    {
      "available": true,
      "display": "joint 3rd of 5 (intervals overlap)",
      "key": "model.D.quality.rank",
      "value": 3
    },
    {
      "available": true,
      "display": "64 / 100",
      "key": "model.D.quality.rawIndex",
      "value": 64
    },
    {
      "available": true,
      "display": "1 of 1 question",
      "key": "model.D.quality.scoredItems",
      "value": 1
    },
    {
      "available": true,
      "display": "64 / 100",
      "key": "model.D.quality.unweightedMean",
      "value": 64
    },
    {
      "available": true,
      "display": "2026-09-21",
      "key": "model.D.run.dates"
    },
    {
      "available": true,
      "display": "41",
      "key": "model.D.run.harnessVersion"
    },
    {
      "available": true,
      "display": "34",
      "key": "model.D.run.ids"
    },
    {
      "available": true,
      "display": "e9b3e9a7c4d1",
      "key": "model.D.run.promptSha256"
    },
    {
      "available": true,
      "display": "f59d8b30a1c7",
      "key": "model.D.run.toolGuidesSha256"
    },
    {
      "available": true,
      "display": "1",
      "key": "model.D.runs",
      "value": 1
    },
    {
      "available": true,
      "display": "25",
      "key": "model.D.scoring.criticalErrorCap",
      "value": 25
    },
    {
      "available": true,
      "display": "1, 15, 35, 55, 72, 87, 100",
      "key": "model.D.scoring.levels"
    },
    {
      "available": true,
      "display": "12",
      "key": "model.D.scoring.methodVersion"
    },
    {
      "available": true,
      "display": "Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %",
      "key": "model.D.scoring.weights"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.speed.modelTimeMean",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "8.0 s",
      "key": "model.D.speed.modelTimeP50",
      "value": 8000
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.speed.modelTimeP90",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "1st of 5",
      "key": "model.D.speed.rank",
      "value": 1
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.speed.ttftP50",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "Comparable",
      "key": "model.D.state",
      "value": "Comparable"
    },
    {
      "available": true,
      "display": "No response-style conflict",
      "key": "model.D.style.responseStyleConflict",
      "value": false
    },
    {
      "available": true,
      "display": "1",
      "key": "model.D.suite.questions",
      "value": 1
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.D.thinkingLevel"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.tokens.inputPerQuestion",
      "unavailableReason": "No answer recorded its token counts."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.tokens.outputPerQuestion",
      "unavailableReason": "No answer recorded its token counts."
    },
    {
      "available": true,
      "display": "0.0",
      "key": "model.D.tools.callsPerQuestion",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.D.tools.failed",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.D.tools.refusedByBudget",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.tools.share.knowledgeBase",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.tools.share.other",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.tools.share.sourceCode",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.tools.share.structuredLookup",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.tools.share.wiki",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.tools.zeroKnowledgeBaseAnswers",
      "unavailableReason": "No question of this suite is a knowledge-base topic; the prompt routes game mechanics past the knowledge base."
    },
    {
      "available": true,
      "display": "6",
      "key": "model.E.answers.scored",
      "value": 6
    },
    {
      "available": true,
      "display": "0",
      "key": "model.E.band.advanced.questions",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.band.advanced.score",
      "unavailableReason": "The model has no scored answer in this band."
    },
    {
      "available": true,
      "display": "6",
      "key": "model.E.band.intermediate.questions",
      "value": 6
    },
    {
      "available": true,
      "display": "50",
      "key": "model.E.band.intermediate.score",
      "value": 50
    },
    {
      "available": true,
      "display": "0",
      "key": "model.E.band.simple.questions",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.band.simple.score",
      "unavailableReason": "The model has no scored answer in this band."
    },
    {
      "available": true,
      "display": "0",
      "key": "model.E.bands.authored.advanced",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.E.bands.authored.intermediate",
      "value": 0
    },
    {
      "available": true,
      "display": "6",
      "key": "model.E.bands.authored.simple",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.claims.indeterminate",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.claims.refuted",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.claims.supported",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": true,
      "display": "Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed",
      "key": "model.E.config.chat",
      "value": "Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed"
    },
    {
      "available": true,
      "display": "Current",
      "key": "model.E.cost.basis",
      "value": "Current"
    },
    {
      "available": true,
      "display": "$0.010",
      "key": "model.E.cost.perQuestion",
      "value": 0.01
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.cost.perRun",
      "unavailableReason": "Not recorded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.cost.pricingAsOf",
      "unavailableReason": "The price card publishes no date."
    },
    {
      "available": true,
      "display": "1st of 5",
      "key": "model.E.cost.rank",
      "value": 1
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.cost.totalRunPerRun",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "50",
      "key": "model.E.dimension.accuracy",
      "value": 50
    },
    {
      "available": true,
      "display": "50",
      "key": "model.E.dimension.completeness",
      "value": 50
    },
    {
      "available": true,
      "display": "50",
      "key": "model.E.dimension.conciseness",
      "value": 50
    },
    {
      "available": true,
      "display": "50",
      "key": "model.E.dimension.readability",
      "value": 50
    },
    {
      "available": true,
      "display": "0 of 6 answers",
      "key": "model.E.errors.critical",
      "value": 0
    },
    {
      "available": true,
      "display": "on the intelligence against cost and speed against cost frontiers",
      "key": "model.E.frontier",
      "value": true
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.panel.disagreements",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.panel.icc",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.panel.meanAbsDelta",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.panel.memberAAlone",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.panel.memberBAlone",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.panel.referenceReaderIndex",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.panel.referenceReaderOffset",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": true,
      "display": "55 / 100",
      "key": "model.E.quality.index",
      "value": 55
    },
    {
      "available": true,
      "display": "50–60",
      "key": "model.E.quality.interval"
    },
    {
      "available": true,
      "display": "Item sampling only.",
      "key": "model.E.quality.intervalBasis",
      "value": "Item sampling only."
    },
    {
      "available": true,
      "display": "10 points",
      "key": "model.E.quality.intervalSpan",
      "value": 10
    },
    {
      "available": true,
      "display": "5th of 5",
      "key": "model.E.quality.rank",
      "value": 5
    },
    {
      "available": true,
      "display": "50 / 100",
      "key": "model.E.quality.rawIndex",
      "value": 50
    },
    {
      "available": true,
      "display": "1 of 1 question",
      "key": "model.E.quality.scoredItems",
      "value": 1
    },
    {
      "available": true,
      "display": "50 / 100",
      "key": "model.E.quality.unweightedMean",
      "value": 50
    },
    {
      "available": true,
      "display": "2026-09-21",
      "key": "model.E.run.dates"
    },
    {
      "available": true,
      "display": "41",
      "key": "model.E.run.harnessVersion"
    },
    {
      "available": true,
      "display": "35",
      "key": "model.E.run.ids"
    },
    {
      "available": true,
      "display": "e9b3e9a7c4d1",
      "key": "model.E.run.promptSha256"
    },
    {
      "available": true,
      "display": "f59d8b30a1c7",
      "key": "model.E.run.toolGuidesSha256"
    },
    {
      "available": true,
      "display": "1",
      "key": "model.E.runs",
      "value": 1
    },
    {
      "available": true,
      "display": "25",
      "key": "model.E.scoring.criticalErrorCap",
      "value": 25
    },
    {
      "available": true,
      "display": "1, 15, 35, 55, 72, 87, 100",
      "key": "model.E.scoring.levels"
    },
    {
      "available": true,
      "display": "12",
      "key": "model.E.scoring.methodVersion"
    },
    {
      "available": true,
      "display": "Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %",
      "key": "model.E.scoring.weights"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.speed.modelTimeMean",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "20.0 s",
      "key": "model.E.speed.modelTimeP50",
      "value": 20000
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.speed.modelTimeP90",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "5th of 5",
      "key": "model.E.speed.rank",
      "value": 5
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.speed.ttftP50",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "Comparable",
      "key": "model.E.state",
      "value": "Comparable"
    },
    {
      "available": true,
      "display": "No response-style conflict",
      "key": "model.E.style.responseStyleConflict",
      "value": false
    },
    {
      "available": true,
      "display": "1",
      "key": "model.E.suite.questions",
      "value": 1
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.E.thinkingLevel"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.tokens.inputPerQuestion",
      "unavailableReason": "No answer recorded its token counts."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.tokens.outputPerQuestion",
      "unavailableReason": "No answer recorded its token counts."
    },
    {
      "available": true,
      "display": "0.0",
      "key": "model.E.tools.callsPerQuestion",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.E.tools.failed",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "model.E.tools.refusedByBudget",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.tools.share.knowledgeBase",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.tools.share.other",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.tools.share.sourceCode",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.tools.share.structuredLookup",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.tools.share.wiki",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.tools.zeroKnowledgeBaseAnswers",
      "unavailableReason": "No question of this suite is a knowledge-base topic; the prompt routes game mechanics past the knowledge base."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.B.cost.allPairs",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
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
      "display": "the 95 % intervals of Model A and Model B overlap",
      "key": "pair.A.B.intervalOverlap",
      "value": true
    },
    {
      "available": true,
      "display": "no difference established after the Holm adjustment across 10 tests (adjusted p 0.813)",
      "key": "pair.A.B.quality.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "0.0 points (Model A minus Model B)",
      "key": "pair.A.B.quality.difference",
      "value": -0
    },
    {
      "available": true,
      "display": "-21.0 to +21.0",
      "key": "pair.A.B.quality.interval"
    },
    {
      "available": true,
      "display": "no difference established after the Holm adjustment across 4 tests (adjusted p 0.406)",
      "key": "pair.A.B.quality.reference",
      "value": false
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.A.B.sharedQuestions",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.B.speed.allPairs",
      "unavailableReason": "The pair was not tested."
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
      "available": false,
      "display": "not available",
      "key": "pair.A.C.cost.allPairs",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.C.cost.ratio",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.C.cost.reference",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "the 95 % intervals of Model A and Model C do not overlap",
      "key": "pair.A.C.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.C.quality.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "+15.2 points (Model A minus Model C)",
      "key": "pair.A.C.quality.difference",
      "value": 15.166666666666666
    },
    {
      "available": true,
      "display": "+6.6 to +23.8",
      "key": "pair.A.C.quality.interval"
    },
    {
      "available": true,
      "display": "no difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.C.quality.reference",
      "value": false
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.A.C.sharedQuestions",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.C.speed.allPairs",
      "unavailableReason": "The pair was not tested."
    },
    {
      "available": true,
      "display": "Model A took 1.00 times as long as Model C on the same questions (1.00 to 1.00)",
      "key": "pair.A.C.speed.ratio",
      "value": 1
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.C.speed.reference",
      "unavailableReason": "The pair was not tested."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.D.cost.allPairs",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.D.cost.ratio",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.D.cost.reference",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "the 95 % intervals of Model A and Model D do not overlap",
      "key": "pair.A.D.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.D.quality.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "+15.8 points (Model A minus Model D)",
      "key": "pair.A.D.quality.difference",
      "value": 15.833333333333334
    },
    {
      "available": true,
      "display": "+5.6 to +26.1",
      "key": "pair.A.D.quality.interval"
    },
    {
      "available": true,
      "display": "no difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.D.quality.reference",
      "value": false
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.A.D.sharedQuestions",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.D.speed.allPairs",
      "unavailableReason": "The pair was not tested."
    },
    {
      "available": true,
      "display": "Model A took 1.00 times as long as Model D on the same questions (1.00 to 1.00)",
      "key": "pair.A.D.speed.ratio",
      "value": 1
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.D.speed.reference",
      "unavailableReason": "The pair was not tested."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.E.cost.allPairs",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.E.cost.ratio",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.E.cost.reference",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "the 95 % intervals of Model A and Model E do not overlap",
      "key": "pair.A.E.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.E.quality.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "+30.0 points (Model A minus Model E)",
      "key": "pair.A.E.quality.difference",
      "value": 30
    },
    {
      "available": true,
      "display": "+16.1 to +43.9",
      "key": "pair.A.E.quality.interval"
    },
    {
      "available": true,
      "display": "no difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.E.quality.reference",
      "value": false
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.A.E.sharedQuestions",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.E.speed.allPairs",
      "unavailableReason": "The pair was not tested."
    },
    {
      "available": true,
      "display": "Model A took 1.00 times as long as Model E on the same questions (1.00 to 1.00)",
      "key": "pair.A.E.speed.ratio",
      "value": 1
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.A.E.speed.reference",
      "unavailableReason": "The pair was not tested."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.B.C.cost.allPairs",
      "unavailableReason": "No candidate price card was resolved for run #32 of Model B on this pricing basis, so its cost is unknown."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.B.C.cost.ratio",
      "unavailableReason": "No candidate price card was resolved for run #32 of Model B on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "the 95 % intervals of Model B and Model C do not overlap",
      "key": "pair.B.C.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.C.quality.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "+15.2 points (Model B minus Model C)",
      "key": "pair.B.C.quality.difference",
      "value": 15.166666666666666
    },
    {
      "available": true,
      "display": "+1.1 to +29.3",
      "key": "pair.B.C.quality.interval"
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.B.C.sharedQuestions",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.B.C.speed.allPairs",
      "unavailableReason": "The pair was not tested."
    },
    {
      "available": true,
      "display": "Model B took 1.00 times as long as Model C on the same questions (1.00 to 1.00)",
      "key": "pair.B.C.speed.ratio",
      "value": 1
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.B.D.cost.allPairs",
      "unavailableReason": "No candidate price card was resolved for run #32 of Model B on this pricing basis, so its cost is unknown."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.B.D.cost.ratio",
      "unavailableReason": "No candidate price card was resolved for run #32 of Model B on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "the 95 % intervals of Model B and Model D do not overlap",
      "key": "pair.B.D.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.D.quality.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "+15.8 points (Model B minus Model D)",
      "key": "pair.B.D.quality.difference",
      "value": 15.833333333333334
    },
    {
      "available": true,
      "display": "+3.0 to +28.6",
      "key": "pair.B.D.quality.interval"
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.B.D.sharedQuestions",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.B.D.speed.allPairs",
      "unavailableReason": "The pair was not tested."
    },
    {
      "available": true,
      "display": "Model B took 1.00 times as long as Model D on the same questions (1.00 to 1.00)",
      "key": "pair.B.D.speed.ratio",
      "value": 1
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.B.E.cost.allPairs",
      "unavailableReason": "No candidate price card was resolved for run #32 of Model B on this pricing basis, so its cost is unknown."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.B.E.cost.ratio",
      "unavailableReason": "No candidate price card was resolved for run #32 of Model B on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "the 95 % intervals of Model B and Model E do not overlap",
      "key": "pair.B.E.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.E.quality.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "+30.0 points (Model B minus Model E)",
      "key": "pair.B.E.quality.difference",
      "value": 30
    },
    {
      "available": true,
      "display": "+21.0 to +39.0",
      "key": "pair.B.E.quality.interval"
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.B.E.sharedQuestions",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.B.E.speed.allPairs",
      "unavailableReason": "The pair was not tested."
    },
    {
      "available": true,
      "display": "Model B took 1.00 times as long as Model E on the same questions (1.00 to 1.00)",
      "key": "pair.B.E.speed.ratio",
      "value": 1
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.C.D.cost.allPairs",
      "unavailableReason": "No candidate price card was resolved for run #33 of Model C on this pricing basis, so its cost is unknown."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.C.D.cost.ratio",
      "unavailableReason": "No candidate price card was resolved for run #33 of Model C on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "the 95 % intervals of Model C and Model D overlap",
      "key": "pair.C.D.intervalOverlap",
      "value": true
    },
    {
      "available": true,
      "display": "no difference established after the Holm adjustment across 10 tests (adjusted p 1.000)",
      "key": "pair.C.D.quality.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "+0.7 points (Model C minus Model D)",
      "key": "pair.C.D.quality.difference",
      "value": 0.6666666666666666
    },
    {
      "available": true,
      "display": "-7.1 to +8.4",
      "key": "pair.C.D.quality.interval"
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.C.D.sharedQuestions",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.C.D.speed.allPairs",
      "unavailableReason": "The pair was not tested."
    },
    {
      "available": true,
      "display": "Model C took 1.00 times as long as Model D on the same questions (1.00 to 1.00)",
      "key": "pair.C.D.speed.ratio",
      "value": 1
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.C.E.cost.allPairs",
      "unavailableReason": "No candidate price card was resolved for run #33 of Model C on this pricing basis, so its cost is unknown."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.C.E.cost.ratio",
      "unavailableReason": "No candidate price card was resolved for run #33 of Model C on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "the 95 % intervals of Model C and Model E do not overlap",
      "key": "pair.C.E.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.C.E.quality.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "+14.8 points (Model C minus Model E)",
      "key": "pair.C.E.quality.difference",
      "value": 14.833333333333334
    },
    {
      "available": true,
      "display": "+8.1 to +21.5",
      "key": "pair.C.E.quality.interval"
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.C.E.sharedQuestions",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.C.E.speed.allPairs",
      "unavailableReason": "The pair was not tested."
    },
    {
      "available": true,
      "display": "Model C took 1.00 times as long as Model E on the same questions (1.00 to 1.00)",
      "key": "pair.C.E.speed.ratio",
      "value": 1
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.D.E.cost.allPairs",
      "unavailableReason": "No candidate price card was resolved for run #34 of Model D on this pricing basis, so its cost is unknown."
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.D.E.cost.ratio",
      "unavailableReason": "No candidate price card was resolved for run #34 of Model D on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "the 95 % intervals of Model D and Model E do not overlap",
      "key": "pair.D.E.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.D.E.quality.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "+14.2 points (Model D minus Model E)",
      "key": "pair.D.E.quality.difference",
      "value": 14.166666666666666
    },
    {
      "available": true,
      "display": "+5.8 to +22.5",
      "key": "pair.D.E.quality.interval"
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.D.E.sharedQuestions",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "pair.D.E.speed.allPairs",
      "unavailableReason": "The pair was not tested."
    },
    {
      "available": true,
      "display": "Model D took 1.00 times as long as Model E on the same questions (1.00 to 1.00)",
      "key": "pair.D.E.speed.ratio",
      "value": 1
    },
    {
      "available": true,
      "display": "1 of 6 questions",
      "key": "questions.anyCriticalError",
      "value": 1
    },
    {
      "available": true,
      "display": "0 of 6 questions",
      "key": "questions.sharedLow",
      "value": 0
    },
    {
      "available": true,
      "display": "6 of 6 questions",
      "key": "questions.wideSpread",
      "value": 6
    },
    {
      "available": true,
      "display": "from $0.060 (Model D) to $0.010 (Model E)",
      "key": "spread.cost",
      "value": 0.049999999999999996
    },
    {
      "available": true,
      "display": "from 50 (Model E) to 80 (Model A), 30 points apart",
      "key": "spread.dimension.accuracy",
      "value": 30
    },
    {
      "available": true,
      "display": "from 50 (Model E) to 80 (Model A), 30 points apart",
      "key": "spread.dimension.completeness",
      "value": 30
    },
    {
      "available": true,
      "display": "from 50 (Model E) to 80 (Model A), 30 points apart",
      "key": "spread.dimension.conciseness",
      "value": 30
    },
    {
      "available": true,
      "display": "from 50 (Model E) to 80 (Model A), 30 points apart",
      "key": "spread.dimension.readability",
      "value": 30
    },
    {
      "available": true,
      "display": "from 55 (Model E) to 85 (Model A), 30 points apart",
      "key": "spread.quality",
      "value": 30
    },
    {
      "available": true,
      "display": "from 20.0 s (Model E) to 8.0 s (Model D)",
      "key": "spread.speed",
      "value": 12000
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
      "entryKey": "run:32",
      "label": "Model B",
      "letter": "B",
      "provider": ""
    },
    {
      "entryKey": "run:33",
      "label": "Model C",
      "letter": "C",
      "provider": ""
    },
    {
      "entryKey": "run:34",
      "label": "Model D",
      "letter": "D",
      "provider": ""
    },
    {
      "entryKey": "run:35",
      "label": "Model E",
      "letter": "E",
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
          "adjustment": "Holm",
          "adjustmentNote": "Holm-adjusted across 4 tests",
          "familySize": 4,
          "measure": "Intelligence",
          "pairs": [
            {
              "adjustedPValue": 0.40625,
              "effect": -0,
              "effectKind": "Difference",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": -20.967861743280682,
              "pValue": 0.40625,
              "pairedItems": 6,
              "secondLetter": "B",
              "upper": 20.967861743280682
            },
            {
              "adjustedPValue": 0.125,
              "effect": 15.166666666666666,
              "effectKind": "Difference",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 6.578742140773086,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "C",
              "upper": 23.754591192560248
            },
            {
              "adjustedPValue": 0.125,
              "effect": 15.833333333333334,
              "effectKind": "Difference",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 5.606422362316174,
              "pValue": 0.0625,
              "pairedItems": 6,
              "secondLetter": "D",
              "upper": 26.060244304350494
            },
            {
              "adjustedPValue": 0.125,
              "effect": 30,
              "effectKind": "Difference",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 16.109242522022058,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "E",
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
            },
            {
              "effect": 1,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 1,
              "pairedItems": 6,
              "secondLetter": "C",
              "upper": 1
            },
            {
              "effect": 1,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 1,
              "pairedItems": 6,
              "secondLetter": "D",
              "upper": 1
            },
            {
              "effect": 1,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 1,
              "pairedItems": 6,
              "secondLetter": "E",
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
            },
            {
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "notTestedReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown.",
              "pairedItems": 0,
              "secondLetter": "C"
            },
            {
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "notTestedReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown.",
              "pairedItems": 0,
              "secondLetter": "D"
            },
            {
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "notTestedReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown.",
              "pairedItems": 0,
              "secondLetter": "E"
            }
          ]
        }
      ],
      "mode": "Reference",
      "referenceLetter": "A",
      "singleRunCaveat": "At least one entry has a single run. With one run a side, a paired test captures question sampling only, not run-to-run variation, so it can look more certain than it is."
    },
    {
      "measures": [
        {
          "adjustment": "Holm",
          "adjustmentNote": "Holm-adjusted across 10 tests",
          "familySize": 10,
          "measure": "Intelligence",
          "pairs": [
            {
              "adjustedPValue": 0.8125,
              "effect": -0,
              "effectKind": "Difference",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": -20.967861743280682,
              "pValue": 0.40625,
              "pairedItems": 6,
              "secondLetter": "B",
              "upper": 20.967861743280682
            },
            {
              "adjustedPValue": 0.3125,
              "effect": 15.166666666666666,
              "effectKind": "Difference",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 6.578742140773086,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "C",
              "upper": 23.754591192560248
            },
            {
              "adjustedPValue": 0.3125,
              "effect": 15.833333333333334,
              "effectKind": "Difference",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 5.606422362316174,
              "pValue": 0.0625,
              "pairedItems": 6,
              "secondLetter": "D",
              "upper": 26.060244304350494
            },
            {
              "adjustedPValue": 0.3125,
              "effect": 30,
              "effectKind": "Difference",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 16.109242522022058,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "E",
              "upper": 43.89075747797794
            },
            {
              "adjustedPValue": 0.3125,
              "effect": 15.166666666666666,
              "effectKind": "Difference",
              "established": false,
              "favors": "None",
              "firstLetter": "B",
              "lower": 1.0803937060460598,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "C",
              "upper": 29.252939627287272
            },
            {
              "adjustedPValue": 0.3125,
              "effect": 15.833333333333334,
              "effectKind": "Difference",
              "established": false,
              "favors": "None",
              "firstLetter": "B",
              "lower": 3.0246812868020054,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "D",
              "upper": 28.641985379864664
            },
            {
              "adjustedPValue": 0.3125,
              "effect": 30,
              "effectKind": "Difference",
              "established": false,
              "favors": "None",
              "firstLetter": "B",
              "lower": 21.045845829932716,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "E",
              "upper": 38.954154170067284
            },
            {
              "adjustedPValue": 1,
              "effect": 0.6666666666666666,
              "effectKind": "Difference",
              "established": false,
              "favors": "None",
              "firstLetter": "C",
              "lower": -7.064151364678903,
              "pValue": 1,
              "pairedItems": 6,
              "secondLetter": "D",
              "upper": 8.397484698012237
            },
            {
              "adjustedPValue": 0.3125,
              "effect": 14.833333333333334,
              "effectKind": "Difference",
              "established": false,
              "favors": "None",
              "firstLetter": "C",
              "lower": 8.149224082397614,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "E",
              "upper": 21.517442584269055
            },
            {
              "adjustedPValue": 0.3125,
              "effect": 14.166666666666666,
              "effectKind": "Difference",
              "established": false,
              "favors": "None",
              "firstLetter": "D",
              "lower": 5.786440575616906,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "E",
              "upper": 22.546892757716428
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
            },
            {
              "effect": 1,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 1,
              "pairedItems": 6,
              "secondLetter": "C",
              "upper": 1
            },
            {
              "effect": 1,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 1,
              "pairedItems": 6,
              "secondLetter": "D",
              "upper": 1
            },
            {
              "effect": 1,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 1,
              "pairedItems": 6,
              "secondLetter": "E",
              "upper": 1
            },
            {
              "effect": 1,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "B",
              "lower": 1,
              "pairedItems": 6,
              "secondLetter": "C",
              "upper": 1
            },
            {
              "effect": 1,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "B",
              "lower": 1,
              "pairedItems": 6,
              "secondLetter": "D",
              "upper": 1
            },
            {
              "effect": 1,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "B",
              "lower": 1,
              "pairedItems": 6,
              "secondLetter": "E",
              "upper": 1
            },
            {
              "effect": 1,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "C",
              "lower": 1,
              "pairedItems": 6,
              "secondLetter": "D",
              "upper": 1
            },
            {
              "effect": 1,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "C",
              "lower": 1,
              "pairedItems": 6,
              "secondLetter": "E",
              "upper": 1
            },
            {
              "effect": 1,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "D",
              "lower": 1,
              "pairedItems": 6,
              "secondLetter": "E",
              "upper": 1
            }
          ]
        },
        {
          "adjustment": "None",
          "adjustmentNote": "No test was made in this family.",
          "familySize": 0,
          "measure": "Cost",
          "notTestedReason": "No pair could be tested on this measure; each pair says why.",
          "pairs": [
            {
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "notTestedReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown.",
              "pairedItems": 0,
              "secondLetter": "B"
            },
            {
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "notTestedReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown.",
              "pairedItems": 0,
              "secondLetter": "C"
            },
            {
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "notTestedReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown.",
              "pairedItems": 0,
              "secondLetter": "D"
            },
            {
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "notTestedReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown.",
              "pairedItems": 0,
              "secondLetter": "E"
            },
            {
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "B",
              "notTestedReason": "No candidate price card was resolved for run #32 of Model B on this pricing basis, so its cost is unknown.",
              "pairedItems": 0,
              "secondLetter": "C"
            },
            {
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "B",
              "notTestedReason": "No candidate price card was resolved for run #32 of Model B on this pricing basis, so its cost is unknown.",
              "pairedItems": 0,
              "secondLetter": "D"
            },
            {
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "B",
              "notTestedReason": "No candidate price card was resolved for run #32 of Model B on this pricing basis, so its cost is unknown.",
              "pairedItems": 0,
              "secondLetter": "E"
            },
            {
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "C",
              "notTestedReason": "No candidate price card was resolved for run #33 of Model C on this pricing basis, so its cost is unknown.",
              "pairedItems": 0,
              "secondLetter": "D"
            },
            {
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "C",
              "notTestedReason": "No candidate price card was resolved for run #33 of Model C on this pricing basis, so its cost is unknown.",
              "pairedItems": 0,
              "secondLetter": "E"
            },
            {
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "D",
              "notTestedReason": "No candidate price card was resolved for run #34 of Model D on this pricing basis, so its cost is unknown.",
              "pairedItems": 0,
              "secondLetter": "E"
            }
          ]
        }
      ],
      "mode": "AllPairs",
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
      "entryKey": "run:32",
      "explanation": "Comparable: measured under the baseline condition.",
      "label": "Model B",
      "letter": "B",
      "modelId": "",
      "provider": "",
      "runIds": [
        32
      ],
      "speedDegraded": false,
      "state": "Comparable"
    },
    {
      "costDegraded": false,
      "displayName": "Model C",
      "entryKey": "run:33",
      "explanation": "Comparable: measured under the baseline condition.",
      "label": "Model C",
      "letter": "C",
      "modelId": "",
      "provider": "",
      "runIds": [
        33
      ],
      "speedDegraded": false,
      "state": "Comparable"
    },
    {
      "costDegraded": false,
      "displayName": "Model D",
      "entryKey": "run:34",
      "explanation": "Comparable: measured under the baseline condition.",
      "label": "Model D",
      "letter": "D",
      "modelId": "",
      "provider": "",
      "runIds": [
        34
      ],
      "speedDegraded": false,
      "state": "Comparable"
    },
    {
      "costDegraded": false,
      "displayName": "Model E",
      "entryKey": "run:35",
      "explanation": "Comparable: measured under the baseline condition.",
      "label": "Model E",
      "letter": "E",
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
        "B",
        "C",
        "D",
        "E"
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
          "score": 85,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "C",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 70,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "D",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 75,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "E",
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
      "peerCount": 5,
      "peerMax": 92,
      "peerMin": 50,
      "peersAbove": 0,
      "questionKey": "201",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 5,
      "score": 74.4,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "criticalError": false,
      "detailed": true,
      "excerptLetters": [
        "A",
        "B",
        "C",
        "D",
        "E"
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
          "score": 80,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "C",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 72,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "D",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 60,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "E",
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
      "peerCount": 5,
      "peerMax": 88,
      "peerMin": 55,
      "peersAbove": 0,
      "questionKey": "202",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 5,
      "score": 71,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "criticalError": true,
      "detailed": true,
      "excerptLetters": [
        "A",
        "B",
        "C",
        "D",
        "E"
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
          "score": 70,
          "toolCalls": 0
        },
        {
          "criticalError": true,
          "letter": "C",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 28,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "D",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 1,
          "runCount": 1,
          "score": 32,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "E",
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
      "peerCount": 5,
      "peerMax": 70,
      "peerMin": 25,
      "peersAbove": 0,
      "questionKey": "203",
      "refutedAnswerSentences": 0,
      "refutedClaims": 1,
      "runCount": 5,
      "score": 37,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "criticalError": false,
      "detailed": true,
      "excerptLetters": [
        "A",
        "B",
        "C",
        "D",
        "E"
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
          "score": 75,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "C",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 65,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "D",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 72,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "E",
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
      "peerCount": 5,
      "peerMax": 90,
      "peerMin": 52,
      "peersAbove": 0,
      "questionKey": "204",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 5,
      "score": 70.8,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "criticalError": false,
      "detailed": true,
      "excerptLetters": [
        "A",
        "B",
        "C",
        "D",
        "E"
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
          "score": 82,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "C",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 74,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "D",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 68,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "E",
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
      "peerCount": 5,
      "peerMax": 85,
      "peerMin": 58,
      "peersAbove": 0,
      "questionKey": "205",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 5,
      "score": 73.4,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "criticalError": false,
      "detailed": true,
      "excerptLetters": [
        "A",
        "B",
        "C",
        "D",
        "E"
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
          "score": 88,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "C",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 80,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "D",
          "modelTimeMs": 10000,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 78,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "E",
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
      "peerCount": 5,
      "peerMax": 95,
      "peerMin": 60,
      "peersAbove": 0,
      "questionKey": "206",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 5,
      "score": 80.2,
      "toolCalls": 0
    }
  ],
  "rows": [],
  "scope": "Comparison",
  "subjectDisplayName": "",
  "subjectExplanation": "",
  "subjectKey": "comparison:12",
  "subjectKind": "Comparison",
  "subjectLabel": "Comparison #12",
  "subjectModelId": "",
  "subjectProvider": "",
  "subjectRunIds": [
    31,
    32,
    33,
    34,
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
