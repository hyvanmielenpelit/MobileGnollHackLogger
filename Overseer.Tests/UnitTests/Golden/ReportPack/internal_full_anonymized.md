# GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Internal Improvement Brief

*INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.*

- **Date:** 2026-09-28
- **Suite:** GnollHack Core Suite
- **Questions:** 4
- **Runs:** 1 (run 12)
- **Peers:** Models A and B, identities withheld

## 1. The Overseer chat and its tools

Most tool calls went to the source code (57 %).

### Recommendations for the Overseer chat

- Send item-destruction questions to the source code first. *(Computed)*

## 2. The benchmarking system

Both panel members flagged the Q3 error (R1).

### Recommendations for the benchmarking system

- Clarify the conciseness anchor that split the graders on Q2. *(Graders disagree)*

## 3. The model's result

GPT-5.6 Luna ranks 2nd of 3 at $0.036 per question.

### Key figures

- **Intelligence:** 80 / 100 (interval 77–83), 2nd of 3; its 95 % interval overlaps those of Models A and B.
- **Speed:** median answer time 12.3 s, 2nd of 2.
- **Cost:** $0.036 per question, 2nd of 3.
- **Critical errors:** 1 of 4 answers.

### Strengths

- Answers simple questions precisely and briefly. *(One grader — different family)*

### Weaknesses

- Asserted a false outcome on Q3, where Model A scored well. *(Both graders)*
- Scored lowest on intermediate questions (49). *(Computed)*

### Recommendations for model developers

- Verify object-destruction rules before asserting them. *(Both graders)*

## 4. Leads

*Provisional and un-triaged. A lead is not a finding: it must go through the triage, evidence bar and tool-layer diagnostics of `server_benchmark_to_chat_transfer` before anything is changed.*

- **[suite]** The Q3 rubric may understate how often a thrown gem survives. *(Both graders)*

## 5. Per-question results

| Q | Topic | Band | Score | Peer mean | Difference | Critical error | Refuted answer sentences | Tool calls | Model time |
|---|---|---|---|---|---|---|---|---|---|
| Q1 | Throwing gems at unicorns | Simple | 90 | 85 | +5 | no | 0 | 2 | 8.1 s |
| Q2 | Prayer timeout | Intermediate | 72 | 70 | +2 | no | 0 | 3 | 11.0 s |
| Q3 | Breaking a thrown gem | Intermediate | 25 | 68 | -43 | yes | 1 | 5 | 15.2 s |
| Q4 | Wand of wishing charges | Advanced | 87 | 91 | -4 | no | 0 | 4 | 13.4 s |

### Questions below the peer mean or with a critical error

**Q3** (Breaking a thrown gem): Claimed a thrown gem always shatters; the rubric says it can survive.

### Question details

#### Q1: Throwing gems at unicorns

**Question:**

> What happens if I throw a gem at a co-aligned unicorn?

**Rubric:**

> - The unicorn catches the gem.
> - A valuable gem raises Luck; worthless glass does not.

**Answer excerpt:**

> The unicorn catches it. A real gem of your alignment raises your Luck; glass does nothing.

**Graders:**

- **Panel member A (Gemini 3.8 Flash):** score 92. Accurate and brief.
  - Accuracy: Matches rubric.
- **Panel member B (Claude Haiku 5):** score 88. Correct.

**Claim verifier:**

- **Grader's statement — refuted (the answer was right):** "Worthless glass angers the unicorn." — Glass is caught and returned without anger (dothrow.c).

#### Q2: Prayer timeout

**Question:**

> How long is the prayer timeout after a successful prayer?

**Rubric:**

> - The timeout is reset to a random value around 350.

**Answer excerpt:**

> About 50 to 1000 turns, typically near 350.

**Graders:**

- **Panel member A (Gemini 3.8 Flash):** score 75. Terse.
  - Completeness: Omits the Luck adjustment.
- **Panel member B (Claude Haiku 5):** score 69.

**Claim verifier:**

- **Answer sentence — supported:** "The timeout is typically near 350." — Matches the prayer code (pray.c).

#### Q3: Breaking a thrown gem

**Question:**

> Will my gem break if I throw it at a unicorn?

**Rubric:**

*Rubric not recorded for this answer.*

**Answer excerpt:**

> Yes. A thrown gem always shatters on impact, so never throw your valuable gems at a unicorn; keep them for…

**Graders:**

- **Panel member A (Gemini 3.8 Flash):** score 25. Critical error.
  - Accuracy: The gem does not always shatter.
  - Critical error: "A thrown gem always shatters on impact"
- **Panel member B (Claude Haiku 5):** score 25. Fabricated breakage.

**Claim verifier:**

- **Answer sentence accused by a grader — refuted (the grader was right):** "A thrown gem always shatters on impact." — Gems are caught, not broken (dothrow.c).

#### Q4: Wand of wishing charges

**Question:**

> How many wishes can I get from a wand of wishing?

**Rubric:**

> - 1 to 3 charges.
> - Wresting gives one more.

**Answer excerpt:**

> A new wand has 1 to 3 charges, and you can wrest one more.

**Graders:**

- **Panel member A (Gemini 3.8 Flash):** score 88. Good.
- **Panel member B (Claude Haiku 5):** score 86.

**Claim verifier:**

- No claims were checked.

## 6. Fact sheet

```json
{
  "entries": [
    {
      "costDegraded": false,
      "costPerQuestionUsd": 0.036,
      "costRank": 2,
      "entryKey": "run:12",
      "extra": [],
      "isSubject": true,
      "modelTimeP50Ms": 12300,
      "qualityIndex": 80.4,
      "qualityLower": 77.1,
      "qualityRank": 2,
      "qualityUpper": 83.2,
      "runCount": 1,
      "speedDegraded": false,
      "speedRank": 2
    },
    {
      "costDegraded": false,
      "costPerQuestionUsd": 0.052,
      "costRank": 3,
      "entryKey": "run:14",
      "extra": [],
      "isSubject": false,
      "modelTimeP50Ms": 9800,
      "peerLetter": "A",
      "qualityIndex": 85.2,
      "qualityLower": 81,
      "qualityRank": 1,
      "qualityUpper": 89.4,
      "runCount": 1,
      "speedDegraded": false,
      "speedRank": 1
    },
    {
      "costDegraded": false,
      "costPerQuestionUsd": 0.021,
      "costRank": 1,
      "entryKey": "run:13",
      "extra": [],
      "isSubject": false,
      "peerLetter": "B",
      "qualityIndex": 78.3,
      "qualityLower": 74.1,
      "qualityRank": 3,
      "qualityUpper": 82.5,
      "runCount": 1,
      "speedDegraded": true
    }
  ],
  "facts": [
    {
      "available": true,
      "display": "-4",
      "key": "band.advanced.difference"
    },
    {
      "available": true,
      "display": "91",
      "key": "band.advanced.peerMean"
    },
    {
      "available": true,
      "display": "1",
      "key": "band.advanced.questions"
    },
    {
      "available": true,
      "display": "87",
      "key": "band.advanced.score"
    },
    {
      "available": true,
      "display": "-21",
      "key": "band.intermediate.difference"
    },
    {
      "available": true,
      "display": "69",
      "key": "band.intermediate.peerMean"
    },
    {
      "available": true,
      "display": "2",
      "key": "band.intermediate.questions"
    },
    {
      "available": true,
      "display": "49",
      "key": "band.intermediate.score"
    },
    {
      "available": true,
      "display": "+5",
      "key": "band.simple.difference"
    },
    {
      "available": true,
      "display": "85",
      "key": "band.simple.peerMean"
    },
    {
      "available": true,
      "display": "1",
      "key": "band.simple.questions"
    },
    {
      "available": true,
      "display": "90",
      "key": "band.simple.score"
    },
    {
      "available": true,
      "display": "2026-09-20",
      "key": "comparison.pricedOn"
    },
    {
      "available": true,
      "display": "Priced from the catalog as of 2026-09-20. Comparable across dates; not what was actually spent.",
      "key": "comparison.pricingBasis"
    },
    {
      "available": true,
      "display": "catalog",
      "key": "comparison.pricingBasisKind"
    },
    {
      "available": true,
      "display": "sig-7f3a91",
      "key": "comparison.signature"
    },
    {
      "available": true,
      "display": "Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed",
      "key": "config.chat"
    },
    {
      "available": true,
      "display": "$0.036",
      "key": "cost.perQuestion"
    },
    {
      "available": true,
      "display": "$0.144",
      "key": "cost.perRun"
    },
    {
      "available": true,
      "display": "2026-09-01",
      "key": "cost.pricingAsOf"
    },
    {
      "available": true,
      "display": "2nd of 3",
      "key": "cost.rank"
    },
    {
      "available": true,
      "display": "84",
      "key": "dimension.accuracy"
    },
    {
      "available": true,
      "display": "+2",
      "key": "dimension.accuracy.difference"
    },
    {
      "available": true,
      "display": "82",
      "key": "dimension.accuracy.peerMean"
    },
    {
      "available": true,
      "display": "70",
      "key": "dimension.completeness"
    },
    {
      "available": true,
      "display": "-8",
      "key": "dimension.completeness.difference"
    },
    {
      "available": true,
      "display": "78",
      "key": "dimension.completeness.peerMean"
    },
    {
      "available": true,
      "display": "88",
      "key": "dimension.conciseness"
    },
    {
      "available": true,
      "display": "+3",
      "key": "dimension.conciseness.difference"
    },
    {
      "available": true,
      "display": "85",
      "key": "dimension.conciseness.peerMean"
    },
    {
      "available": true,
      "display": "90",
      "key": "dimension.readability"
    },
    {
      "available": true,
      "display": "+1",
      "key": "dimension.readability.difference"
    },
    {
      "available": true,
      "display": "89",
      "key": "dimension.readability.peerMean"
    },
    {
      "available": true,
      "display": "1 of 4 answers",
      "key": "errors.critical",
      "value": 1
    },
    {
      "available": true,
      "display": "1 of 4 answers",
      "key": "panel.disagreements"
    },
    {
      "available": true,
      "display": "0.82",
      "key": "panel.icc"
    },
    {
      "available": false,
      "display": "not available",
      "key": "panel.judgeDependentPairs",
      "unavailableReason": "The compared runs were not all graded by the same panel."
    },
    {
      "available": true,
      "display": "6.5 points",
      "key": "panel.meanAbsDelta"
    },
    {
      "available": true,
      "display": "81 / 100",
      "key": "panel.memberAAlone"
    },
    {
      "available": true,
      "display": "79 / 100",
      "key": "panel.memberBAlone"
    },
    {
      "available": true,
      "display": "80 / 100",
      "key": "quality.index",
      "value": 80.4
    },
    {
      "available": true,
      "display": "77–83",
      "key": "quality.interval"
    },
    {
      "available": true,
      "display": "Item sampling only. Below 3 runs there is no reproducibility estimate, so this interval covers one source of variation rather than two.",
      "key": "quality.intervalBasis"
    },
    {
      "available": true,
      "display": "its 95 % interval overlaps those of Models A and B",
      "key": "quality.intervalOverlap"
    },
    {
      "available": true,
      "display": "2nd of 3",
      "key": "quality.rank"
    },
    {
      "available": true,
      "display": "2026-09-20",
      "key": "run.dates"
    },
    {
      "available": true,
      "display": "41",
      "key": "run.harnessVersion"
    },
    {
      "available": true,
      "display": "12",
      "key": "run.ids"
    },
    {
      "available": true,
      "display": "e9b3e9a7c4d1",
      "key": "run.promptSha256"
    },
    {
      "available": true,
      "display": "f59d8b30a1c7",
      "key": "run.toolGuidesSha256"
    },
    {
      "available": true,
      "display": "25",
      "key": "scoring.criticalErrorCap"
    },
    {
      "available": true,
      "display": "1, 15, 35, 55, 72, 87, 100",
      "key": "scoring.levels"
    },
    {
      "available": true,
      "display": "12",
      "key": "scoring.methodVersion"
    },
    {
      "available": true,
      "display": "Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %",
      "key": "scoring.weights"
    },
    {
      "available": true,
      "display": "12.3 s",
      "key": "speed.modelTimeP50"
    },
    {
      "available": true,
      "display": "15.0 s",
      "key": "speed.modelTimeP90"
    },
    {
      "available": true,
      "display": "2nd of 2",
      "key": "speed.rank"
    },
    {
      "available": true,
      "display": "no",
      "key": "style.responseStyleConflict"
    },
    {
      "available": true,
      "display": "4",
      "key": "suite.questions"
    },
    {
      "available": true,
      "display": "18,250",
      "key": "tokens.inputPerQuestion"
    },
    {
      "available": true,
      "display": "1,140",
      "key": "tokens.outputPerQuestion"
    },
    {
      "available": true,
      "display": "3.5",
      "key": "tools.callsPerQuestion"
    },
    {
      "available": true,
      "display": "2.8",
      "key": "tools.callsPerQuestion.peerMean"
    },
    {
      "available": true,
      "display": "0",
      "key": "tools.failed"
    },
    {
      "available": true,
      "display": "0",
      "key": "tools.refusedByBudget"
    },
    {
      "available": true,
      "display": "0 %",
      "key": "tools.share.knowledgeBase"
    },
    {
      "available": true,
      "display": "0 %",
      "key": "tools.share.other"
    },
    {
      "available": true,
      "display": "57 %",
      "key": "tools.share.sourceCode"
    },
    {
      "available": true,
      "display": "14 %",
      "key": "tools.share.structuredLookup"
    },
    {
      "available": true,
      "display": "29 %",
      "key": "tools.share.wiki"
    },
    {
      "available": true,
      "display": "4 of 4",
      "key": "tools.zeroKnowledgeBaseAnswers"
    }
  ],
  "graders": [
    {
      "label": "Gemini 3.8 Flash",
      "modelId": "gemini-3.8-flash",
      "provider": "Google",
      "role": "Panel member A",
      "sameFamilyAsSubject": false,
      "thinkingLevel": "medium"
    },
    {
      "label": "Claude Haiku 5",
      "modelId": "claude-haiku-5",
      "provider": "Anthropic",
      "role": "Panel member B",
      "sameFamilyAsSubject": false
    },
    {
      "label": "Gemini 3.8 Flash",
      "modelId": "gemini-3.8-flash",
      "provider": "Google",
      "role": "Claim verifier",
      "sameFamilyAsSubject": false,
      "thinkingLevel": "medium"
    }
  ],
  "knownNames": [
    "Anthropic",
    "Claude Haiku 5",
    "GPT-5.6 Luna",
    "Gemini 3.8 Flash",
    "GnollHack Core Suite",
    "Google",
    "OpenAI",
    "claude-haiku-5",
    "gemini-3.8-flash",
    "gpt-5.6-luna"
  ],
  "noSignificanceInstead": "Put each model's runs in an analysis group, open one in the Multi-Run Analysis tab and choose the other under Compare with group.",
  "noSignificanceSummary": "Testing every pair among these 3 models at once would flag chance differences as significant, so this view tests none.",
  "peers": [
    {
      "costDegraded": false,
      "displayName": "Model A",
      "entryKey": "run:14",
      "explanation": "Comparable: measured under the baseline condition.",
      "label": "Model A",
      "letter": "A",
      "modelId": "",
      "provider": "",
      "runIds": [
        14
      ],
      "speedDegraded": false,
      "state": "Comparable",
      "thinkingLevel": "high"
    },
    {
      "costDegraded": false,
      "displayName": "Model B",
      "entryKey": "run:13",
      "explanation": "Degraded: speed was measured with parallel execution disabled.",
      "label": "Model B",
      "letter": "B",
      "modelId": "",
      "provider": "",
      "runIds": [
        13
      ],
      "speedDegraded": true,
      "state": "Degraded"
    }
  ],
  "purposeStatements": [
    "Internal evaluation of candidate AI models for the Overseer assistant within GnollHack."
  ],
  "questions": [
    {
      "band": "Simple",
      "criticalError": false,
      "difference": 5,
      "itemRevisionUsed": 2,
      "modelTimeMs": 8100,
      "number": 1,
      "orderIndex": 1,
      "peerCount": 2,
      "peerMean": 85,
      "questionKey": "101",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 1,
      "score": 90,
      "toolCalls": 2
    },
    {
      "band": "Intermediate",
      "criticalError": false,
      "difference": 2,
      "itemRevisionUsed": 1,
      "modelTimeMs": 11000,
      "number": 2,
      "orderIndex": 2,
      "peerCount": 2,
      "peerMean": 70,
      "questionKey": "102",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 1,
      "score": 72,
      "toolCalls": 3
    },
    {
      "band": "Intermediate",
      "criticalError": true,
      "difference": -43,
      "itemRevisionUsed": 1,
      "modelTimeMs": 15200,
      "number": 3,
      "orderIndex": 3,
      "peerCount": 2,
      "peerMean": 68,
      "questionKey": "103",
      "refutedAnswerSentences": 1,
      "refutedClaims": 1,
      "runCount": 1,
      "score": 25,
      "toolCalls": 5
    },
    {
      "band": "Advanced",
      "criticalError": false,
      "difference": -4,
      "itemRevisionUsed": 3,
      "modelTimeMs": 13400,
      "number": 4,
      "orderIndex": 4,
      "peerCount": 2,
      "peerMean": 91,
      "questionKey": "104",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 1,
      "score": 87,
      "toolCalls": 4
    }
  ],
  "rows": [
    {
      "category": "critical_error",
      "id": "R1",
      "kind": "weakness",
      "memberAText": "States that a thrown gem always shatters.",
      "memberBText": "Claims the gem is always destroyed.",
      "questions": [
        3
      ],
      "recurrence": 1,
      "status": "Convergent",
      "supportLabel": "Both graders"
    },
    {
      "category": "accuracy",
      "id": "R2",
      "kind": "strength",
      "memberAText": "Precise on the unicorn throwing rules.",
      "questions": [
        1
      ],
      "recurrence": 1,
      "status": "MemberAOnly",
      "supportLabel": "One grader — different family"
    },
    {
      "category": "conciseness",
      "id": "R3",
      "kind": "strength",
      "memberAText": "Admirably brief.",
      "memberBText": "Too terse to be useful.",
      "questions": [
        2
      ],
      "recurrence": 1,
      "status": "Conflicting",
      "supportLabel": "Graders disagree"
    }
  ],
  "subjectDisplayName": "GPT-5.6 Luna",
  "subjectExplanation": "Comparable: measured under the baseline condition.",
  "subjectKey": "run:12",
  "subjectKind": "Run",
  "subjectLabel": "GPT-5.6 Luna",
  "subjectModelId": "gpt-5.6-luna",
  "subjectProvider": "OpenAI",
  "subjectRunIds": [
    12
  ],
  "subjectState": "Comparable",
  "subjectThinkingLevel": "high",
  "suiteId": 5,
  "suiteName": "GnollHack Core Suite"
}
```

## Removed content

Automatic validation removed these items from the writer's output before it was stored:

- `weaknesses[2]` (rule 4): Cited R9, which does not exist.

---

*Document ID 101 · format version 4 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Full · peers anonymized*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically.*
