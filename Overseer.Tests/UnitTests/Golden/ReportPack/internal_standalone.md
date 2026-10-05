# GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Internal Improvement Brief

*INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.*

- **Date:** 2026-09-28
- **Suite:** GnollHack Core Suite
- **Questions:** 4
- **Runs:** 1 (run 12)
- **Peers:** none; this is a stand-alone report

## 1. The Overseer chat and its tools

Most tool calls went to the source code (57 %).

### Recommendations for the Overseer chat

- Send item-destruction questions to the source code first. *(From per-question results)*

## 2. The benchmarking system

Both panel members flagged the Q3 error (R1).

### Recommendations for the benchmarking system

- Clarify the conciseness anchor that split the graders on Q2. *(Graders disagree)*

## 3. The model's result

GPT-5.6 Luna scored 80 / 100 at $0.036 per question.

### Key figures

- **Intelligence:** 80 / 100 (interval 77–83).
- **Speed:** median answer time 12.3 s.
- **Cost:** $0.036 per question.
- **Critical errors:** 1 of 4 answers.

The 95 % interval is 77–83, a span of 6 points, and rests on 4 of 4 questions with a scored answer.

### Strengths

- Answers simple questions precisely and briefly. *(One grader — different provider)*

### Weaknesses

- Asserted a false outcome on Q3. *(Both graders)*
- Scored lowest on intermediate questions (49). *(From per-question results)*

### Recommendations for model developers

- Asserting a destruction rule without checking it cost Q3; verify object-destruction rules before stating them. *(Both graders)*

## 4. Leads

*Provisional and un-triaged. A lead is not a finding: it must go through the triage, evidence bar and tool-layer diagnostics of `server_benchmark_to_chat_transfer` before anything is changed.*

- **[suite]** The Q3 rubric may understate how often a thrown gem survives. *(Both graders)*

## 5. Per-question results

| Q | Topic | Assessed band | Authored | Score | Critical error | Refuted answer sentences | Tool calls | Model time |
|---|---|---|---|---|---|---|---|---|
| Q1 | Throwing gems at unicorns | Simple | — | 90 | no | 0 | 2 | 8.1 s |
| Q2 | Prayer timeout | Intermediate | — | 72 | no | 0 | 3 | 11.0 s |
| Q3 | Breaking a thrown gem | Intermediate | — | 25 | yes | 1 | 5 | 15.2 s |
| Q4 | Wand of wishing charges | Advanced | — | 87 | no | 0 | 4 | 13.4 s |

### Questions scoring below 50 or with a critical error

**Q3** (Breaking a thrown gem): Claimed a thrown gem always shatters; the rubric says it can survive.

### Question details

#### Q1: Throwing gems at unicorns

**Question:**

> What happens if I throw a gem at a co-aligned unicorn?

**Rubric:**

> - The unicorn catches the gem.
> - A valuable gem raises Luck; worthless glass does not.

**Answer:**

> The unicorn catches it. A real gem of your alignment raises your Luck; glass does nothing.

**Graders:**

- **Panel member A (Gemini 3.8 Flash):** score 92. Accurate and brief.
  - Accuracy: Matches rubric.
- **Panel member B (Claude Haiku 5):** score 88. Correct.

**Claim verifier:**

- **Grader's statement — refuted (the verifier sided with the answer):** "Worthless glass angers the unicorn." — Glass is caught and returned without anger (dothrow.c).

#### Q2: Prayer timeout

**Question:**

> How long is the prayer timeout after a successful prayer?

**Rubric:**

> - The timeout is reset to a random value around 350.

**Answer:**

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

**Answer:**

> Yes. A thrown gem always shatters on impact, so never throw your valuable gems at a unicorn; keep them for selling or for wishing instead.
>
> If you must throw something, throw worthless glass.

**Graders:**

- **Panel member A (Gemini 3.8 Flash):** score 25. Critical error.
  - Accuracy: The gem does not always shatter.
  - Critical error: "A thrown gem always shatters on impact"
- **Panel member B (Claude Haiku 5):** score 25. Fabricated breakage.

**Claim verifier:**

- **Answer sentence accused by a grader — refuted (the verifier sided with the grader):** "A thrown gem always shatters on impact." — Gems are caught, not broken (dothrow.c).

#### Q4: Wand of wishing charges

**Question:**

> How many wishes can I get from a wand of wishing?

**Rubric:**

> - 1 to 3 charges.
> - Wresting gives one more.

**Answer:**

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
      "firstRunUtc": "2026-09-20T14:05:00Z",
      "harnessVersion": "41",
      "isSubject": true,
      "lastRunUtc": "2026-09-20T14:05:00Z",
      "modelTimeP50Ms": 12300,
      "qualityIndex": 80.4,
      "qualityLower": 77.1,
      "qualityRank": 2,
      "qualityUpper": 83.2,
      "runCount": 1,
      "speedDegraded": false,
      "speedRank": 2
    }
  ],
  "facts": [
    {
      "available": false,
      "display": "not available",
      "key": "band.advanced.difference",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "band.advanced.peerMean",
      "unavailableReason": "A stand-alone run report has no peers."
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
      "available": false,
      "display": "not available",
      "key": "band.intermediate.difference",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "band.intermediate.peerMean",
      "unavailableReason": "A stand-alone run report has no peers."
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
      "available": false,
      "display": "not available",
      "key": "band.simple.difference",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "band.simple.peerMean",
      "unavailableReason": "A stand-alone run report has no peers."
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
      "available": false,
      "display": "not available",
      "key": "cost.rank",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": true,
      "display": "84",
      "key": "dimension.accuracy"
    },
    {
      "available": false,
      "display": "not available",
      "key": "dimension.accuracy.difference",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "dimension.accuracy.peerMean",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": true,
      "display": "70",
      "key": "dimension.completeness"
    },
    {
      "available": false,
      "display": "not available",
      "key": "dimension.completeness.difference",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "dimension.completeness.peerMean",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": true,
      "display": "88",
      "key": "dimension.conciseness"
    },
    {
      "available": false,
      "display": "not available",
      "key": "dimension.conciseness.difference",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "dimension.conciseness.peerMean",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": true,
      "display": "90",
      "key": "dimension.readability"
    },
    {
      "available": false,
      "display": "not available",
      "key": "dimension.readability.difference",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "dimension.readability.peerMean",
      "unavailableReason": "A stand-alone run report has no peers."
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
      "unavailableReason": "A stand-alone run report has no peers."
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
      "display": "90 / 100",
      "key": "panel.referenceReaderIndex",
      "value": 90
    },
    {
      "available": true,
      "display": "+16.8 points",
      "key": "panel.referenceReaderOffset",
      "value": 16.8
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
      "available": false,
      "display": "not available",
      "key": "quality.intervalOverlap",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": true,
      "display": "6 points",
      "key": "quality.intervalSpan",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "quality.rank",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": true,
      "display": "4 of 4 questions",
      "key": "quality.scoredItems",
      "value": 4
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
      "available": false,
      "display": "not available",
      "key": "speed.rank",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": true,
      "display": "No response-style conflict",
      "key": "style.responseStyleConflict",
      "value": false
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
      "available": false,
      "display": "not available",
      "key": "tools.callsPerQuestion.peerMean",
      "unavailableReason": "A stand-alone run report has no peers."
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
      "available": false,
      "display": "not available",
      "key": "tools.zeroKnowledgeBaseAnswers",
      "unavailableReason": "No question of this suite is a knowledge-base topic; the prompt routes game mechanics past the knowledge base."
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
      "label": "DeepSeek V4",
      "modelId": "deepseek-v4",
      "provider": "DeepSeek",
      "role": "Reference reader",
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
    "DeepSeek",
    "DeepSeek V4",
    "GPT-5.6 Luna",
    "Gemini 3.8 Flash",
    "GnollHack Core Suite",
    "Google",
    "OpenAI",
    "claude-haiku-5",
    "deepseek-v4",
    "gemini-3.8-flash",
    "gpt-5.6-luna"
  ],
  "noSignificanceInstead": "",
  "noSignificanceSummary": "",
  "pairedDifferences": [],
  "peers": [],
  "purposeStatements": [
    "Internal evaluation of candidate AI models for the Overseer assistant within GnollHack."
  ],
  "questions": [
    {
      "band": "Simple",
      "criticalError": false,
      "itemRevisionUsed": 2,
      "modelTimeMs": 8100,
      "number": 1,
      "orderIndex": 1,
      "peerCount": 0,
      "peersAbove": 0,
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
      "itemRevisionUsed": 1,
      "modelTimeMs": 11000,
      "number": 2,
      "orderIndex": 2,
      "peerCount": 0,
      "peersAbove": 0,
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
      "itemRevisionUsed": 1,
      "modelTimeMs": 15200,
      "number": 3,
      "orderIndex": 3,
      "peerCount": 0,
      "peersAbove": 0,
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
      "itemRevisionUsed": 3,
      "modelTimeMs": 13400,
      "number": 4,
      "orderIndex": 4,
      "peerCount": 0,
      "peersAbove": 0,
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
      "supportLabel": "One grader — different provider"
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

---

*Document ID 101 · format version 11 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Full · peers named*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically for structure, permitted figures and references, word limits, disclosure of benchmark text, peer names, significance claims, interval-overlap wording, hype words and spelling; the checks do not verify the prose's interpretations.*
