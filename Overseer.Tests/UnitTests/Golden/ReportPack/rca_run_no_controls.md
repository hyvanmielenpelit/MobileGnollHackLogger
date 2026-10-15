# Orion Max on the Overseer GnollHack Assistant Benchmark — Root Cause Analysis

*INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.*

- **Date:** 2026-09-28
- **Suite:** GnollHack Core Suite
- **Questions:** 1
- **Runs:** 1 (run 31)
- **Peers:** none; this is a stand-alone report

## The root causes in one sentence

Intelligence: Not established; speed: Probable cause; cost: Probable cause.

## Scope and design

This analysis describes run 31 of Orion Max on its own. Other models enter only as controls: none qualified.

### Setup and method

- **Suite:** GnollHack Core Suite, 1 questions.
- **Chat configuration under test:** Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed
- **Model under test:** Orion Max (Northwind, orion-max), thinking level not set; 1 run.
- **Grading:** each answer is graded on accuracy, completeness, conciseness and readability, weighted Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %. Each dimension is graded on behaviorally anchored levels scored 1, 15, 35, 55, 72, 87, 100. A critical error caps the answer's quality at 25.
- **Graders:**
  - Assessor: Gemini 3.8 Flash (Google, gemini-3.8-flash), different provider from the model under test
- **Formulas:** answer quality is the weighted geometric mean of the four dimension scores, capped by a critical error; in a panel run it is the mean of both graders' scores. The Intelligence Index is the difficulty-weighted mean of answer quality. Median answer time is the median model time per answer, with tool time excluded. Cost per question is the model under test's spend divided by the questions asked.
- **Comparability:** this report describes the model on its own, measured under the instrument condition with signature `sig-7f3a91`.
- **Pricing basis:** Catalog prices on 2026-09-21
- **Versions:** harness 41, scoring method 12.

### Controls

No earlier run of another model answered these questions under the same graders, scoring and prompt options. Speed and cost are analyzed from the model's own accounting. The intelligence hypotheses that could lie in the model or in the instrument are capped at Low, and the intelligence axis stays not established until a control run exists.

One run per suite on each side limits what this design can decide; the next runs add replicates.

## Evidence: intelligence

### Key figures

- **Intelligence:** 85 / 100 (interval 82–88).
- **Speed:** median answer time 12.0 s.
- **Cost:** $0.050 per question.
- **Critical errors:** 0 of 6 answers.

### Results

### Dimensions

| Dimension | Orion Max |
|---|---|
| Accuracy | 80 |
| Completeness | 80 |
| Conciseness | 80 |
| Readability | 80 |

### Difficulty bands (assessed)

| Difficulty band | Questions | Authored questions | Orion Max |
|---|---|---|---|
| Simple | 0 | 6 | — |
| Intermediate | 6 | 0 | 80 |
| Advanced | 0 | 0 | — |

### Failure profile

| Measure | Orion Max |
|---|---|
| Answers | 6 |
| May mislead | 0 (0 per 20) |
| Correct / partial / incorrect / not attempted | — |
| Wrong instead of abstaining | — |
| Level 2 or below: accuracy / completeness / conciseness / readability | 0 / 0 / 0 / 0 |
| Truncated | 0 |
| Output defects | 0 |
| No tool call | 2 |
| Tool-limit hits | 0 |
| Median answer length | 10 |

*Counted over each model's scored answers in all its runs; in a panel run a level is the mean of both panel members' levels. May mislead: an accuracy level below 4 or a critical error. Outcomes are recorded from scoring method 13. Low levels: answers at level 2 or below on each dimension. Output defects: an empty, truncated or scrubbed answer, reasoning bleed, repeated fragments, or an answer that did not complete normally. Tool-limit hits: the tool budget or the iteration limit was reached. The counts say what the graders recorded, not why.*

### Grader findings and panel agreement

- **Panel mean absolute difference:** not available
- **Intraclass correlation, ICC(A,1):** not available
- **Panel disagreements:** not available
- **Panel member A alone:** not available
- **Panel member B alone:** not available
- **Response-style conflict:** none

No synthesis findings were recorded.

### Tool use

- **Tool calls per question:** 0.0
- **Source code share:** not available
- **Wiki share:** not available
- **Structured lookup share:** not available
- **Knowledge base share:** not available
- **Other tools share:** not available
- **Failed tool calls:** 0
- **Calls refused by the tool budget:** 0

*Recorded success or failure describes whether a tool call executed. It does not show that the query was well chosen, that the result was relevant, or that the corpus was current.*

*Per-call arguments and results are in each run's Tool-call log until the retention sweep prunes them.*

### Points at risk

| Model | Answers without tools | Budget, truncation or output defects | Confirmed critical errors | Other low-scoring answers | Total | Median answer |
|---|---|---|---|---|---|---|
| Orion Max | 9.8 | 0.0 | 0.0 | 0.8 | 10.7 | 89 |
| Intermediate questions | 9.8 | 0.0 | 0.0 | 0.8 | 10.7 | — |

*Index points at risk: the points lost on the answers showing each signal, relative to the model's own median answer, each answer counted once under its first signal. They are an upper bound, not a causal estimate.*

### Per-question results

| Q | Topic | Assessed band | Authored | Score | Critical error | Refuted answer sentences | Tool calls | Model time |
|---|---|---|---|---|---|---|---|---|
| Q1 | An item question about gems | Intermediate | Simple | 92 | no | 0 | 0 | 8.2 s |
| Q2 | — | Intermediate | Simple | 88 | no | 0 | 0 | 5.7 s |
| Q3 | — | Intermediate | Simple | 30 | no | 0 | 0 | 8.8 s |
| Q4 | — | Intermediate | Simple | 90 | no | 0 | 0 | 6.1 s |
| Q5 | — | Intermediate | Simple | 85 | no | 0 | 0 | 9.4 s |
| Q6 | — | Intermediate | Simple | 95 | no | 0 | 0 | 6.5 s |

The first known fact: No earlier run of another model answered these questions under the same instrument.

## Evidence: speed

### Where the time goes

| Model | Seconds per question | Time to first token | Output generation | Tool execution | Own waits and retries | Other |
|---|---|---|---|---|---|---|
| Orion Max | 8.9 s | 1.9 s | 5.2 s | 1.5 s | 0.0 s | 0.3 s |
| Intermediate questions | 8.9 s | 1.9 s | 5.2 s | 1.5 s | 0.0 s | 0.3 s |

*Seconds per question: the median of the per-question time, split into its parts in proportion to their means. Tool execution is excluded from the model's own time elsewhere in this document.*

### Calls, latency and output

| Model | Model calls per answer | Time to first token per call, median | Decode rate | Reasoning share | Output tokens per question |
|---|---|---|---|---|---|
| Orion Max | 2.5 | 0.8 s | — | 31 % | 970 |

### Speed and cost figures

| Measure | Orion Max |
|---|---|
| Median answer time | 12.0 s |
| Cost per question | $0.050 |
| Input tokens per question | 4,350 |
| Output tokens per question | 970 |

The speed tables split the time per question into its components.

## Evidence: cost

### Where the cost goes

| Model | Cost per question | Uncached input | Cache reads | Cache writes | Visible output | Reasoning |
|---|---|---|---|---|---|---|
| Orion Max | $0.014 | $0.0042 | $0.0001 | $0.0000 | $0.0067 | $0.0030 |
| Intermediate questions | $0.014 | $0.0042 | $0.0001 | $0.0000 | $0.0067 | $0.0030 |

*Mean cost per question by component, priced on this basis: Priced from the catalog as of 2026-09-20. Comparable across dates; not what was actually spent.*

### Tokens per question

| Model | Uncached input | Cache reads | Cache writes | Visible output | Reasoning | Cache-read share |
|---|---|---|---|---|---|---|
| Orion Max | 3,350 | 1,000 | 0 | 670 | 300 | 23 % |

The cost tables split the cost per question into volume and price.

## Known facts

1. No earlier run of another model answered these questions under the same instrument.
2. Orion Max answered 2 of 6 scored questions without a tool call.
3. Output generation is 59 % of Orion Max's time per question.
4. Visible output is 48 % of Orion Max's cost per question.

### What the graders recorded

- The grader recorded a comment on this answer.
  - *Evidence:* Q1 (92 / 100)

## Hypotheses

*Code fired each hypothesis on a pre-declared rule and graded it: High (quantified, established after adjustment, its null rejected, no open confound, every robustness check passed, two or more runs per suite on both sides), Moderate (exactly one of those conditions missed), Low (qualitative only, two or more conditions missed, or the null stands) or Not supported (refuted by the rule's own test). Each null hypothesis says that the difference is within sampling noise.*

### Intelligence

| Hypothesis | Statement | Models | Locus | Grade |
|---|---|---|---|---|
| H0-W.1 | The contrasts between groups of Orion Max's own questions are within sampling noise. | Orion Max | — | Low |

- **H0-W.1**
  - *For:* No contrast could be made.
  - *Against:* none recorded.
  - *Grade reasons:* capped at Low: a single run per suite gives the contrasts little power.
  - *Refuted if:* A Mann–Whitney contrast is established after the Holm adjustment.

No hypothesis on this axis has more support than its null.

### Speed

| Hypothesis | Statement | Models | Locus | Grade |
|---|---|---|---|---|
| WS3.1 | Tool rounds multiply Orion Max's time (where its time goes, not a comparison). | Orion Max | model / chat | Moderate |
| WS2.1 | Most of Orion Max's time is output generation, driven by reasoning tokens (where its time goes, not a comparison). | Orion Max | model | Not supported |

- **WS3.1**
  - *For:* Across Orion Max's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95 % interval 0.23 to 0.99, n 6).
  - *Against:* none recorded.
  - *Grade reasons:* replicates failed: Orion Max has a single run per suite.
  - *Refuted if:* The ρ interval includes zero.
  - *Robustness:* Replicates failed (Orion Max has a single run per suite); Leave one question out passed (ρ stays at or above 0.5 without any one question).
  - *Settled by:* next run 2.
- **WS2.1**
  - *For:* Output generation is 70 % (69–71 %) of model time per question.
  - *Against:* Reasoning is 31 % of Orion Max's output tokens.
  - *Grade reasons:* the rule's own test refutes it: reasoning is 31 % of Orion Max's output tokens.
  - *Refuted if:* The reasoning share is under one half.
  - *Share:* 70 % (95 % interval 69–71 %)
  - *Robustness:* Replicates failed (Orion Max has a single run per suite); Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most).
  - *Settled by:* next run 3.

The strongest hypothesis in the table: Tool rounds multiply Orion Max's time (where its time goes, not a comparison). Its grade and the evidence against it stand beside it.

### Cost

| Hypothesis | Statement | Models | Locus | Grade |
|---|---|---|---|---|
| WC1.1 | Output (with reasoning) is Orion Max's largest cost component (where its cost goes, not a comparison). | Orion Max | model | Moderate |

- **WC1.1**
  - *For:* Visible output and reasoning are 69 % of Orion Max's cost per question.
  - *Against:* none recorded.
  - *Grade reasons:* replicates failed: Orion Max has a single run per suite.
  - *Refuted if:* Another component is larger.
  - *Share:* 69 %
  - *Robustness:* Replicates failed (Orion Max has a single run per suite).

The strongest hypothesis in the table: Output (with reasoning) is Orion Max's largest cost component (where its cost goes, not a comparison). Its grade and the evidence against it stand beside it.

### The writer's hypotheses

*Hypotheses the report writer added from the grader comments and excerpts. Code has not tested them, so their confidence is Low.*

- **Intelligence · Low** (Orion Max; locus suite): Orion Max may have lost points to a rubric that asks for more than the question states.
  - *For:* The grader comment on Q1 names a missing point.
  - *Against:* Other models answered the same question well.
  - *Refuted if:* A rubric check finds every point stated in the question.

## Conclusions

| Axis | Status | Best hypothesis | Why |
|---|---|---|---|
| Intelligence | Not established | — | No hypothesis fired on this axis. |
| Speed | Probable cause | WS3.1 | WS3.1 is the best hypothesis, graded Moderate: replicates failed: Orion Max has a single run per suite. |
| Cost | Probable cause | WC1.1 | WC1.1 is the best hypothesis, graded Moderate: replicates failed: Orion Max has a single run per suite. |

Intelligence: Not established. Speed: Probable cause. Cost: Probable cause.

## Next runs and checks

Runs are listed in the order to make them: the Overseer chat first (C), then the benchmark (B), then model selection (M). Each one's settings are the launcher's, copied from the analyzed runs unless its kind changes them; a setting the runs did not share is left for you to choose.

### Run 1: Control run (B, the benchmark, recommended)

- **Decides:** Separates the model from the instrument: once two controls, one from another provider, have run on the same questions under the same graders, the intelligence hypotheses are graded without the control cap.
- **Predicted comparability:** Model comparison: every must-match key agrees and only the model differs
- **Estimate:** not estimated

| Launcher field | Setting |
|---|---|
| Models | Model batch |
| Models Under Test | Lyra Mini (high), Zeta Prime (high) |
| Run Target | Single suite |
| Benchmark Suite | GnollHack Core Suite |
| Scoring Profile | not shared by the analyzed runs: choose it |
| Response Style | Concise — production default |
| Source Code References | Allowed — answers cite source files and lines |
| Assessor | not shared by the analyzed runs: choose it |
| Co-Assessor | none |
| Second Reader | none |
| Coverage | Never |
| Claim Verifier | none |
| Report Writer | none |
| Runs per model | 1 |
| Model order | Randomized (recommended) |

- Without the subject: its run already exists and becomes the other side of the comparison.
- The estimate for a model without analyzed runs is the analyzed models' mean.

### Run 2: Replicate (M, model selection, recommended)

- **Decides:** Confirms WS3.1 if model time keeps rising with the calls per answer; refutes it if the ρ interval includes zero.
- **Predicted comparability:** Model comparison with the analyzed runs; a replicate (Tier A) of each model's own runs
- **Estimate:** not estimated
- **Settles:** WS3.1

| Launcher field | Setting |
|---|---|
| Models | Model batch |
| Models Under Test | Orion Max |
| Run Target | Single suite |
| Benchmark Suite | GnollHack Core Suite |
| Scoring Profile | not shared by the analyzed runs: choose it |
| Response Style | Concise — production default |
| Source Code References | Allowed — answers cite source files and lines |
| Assessor | not shared by the analyzed runs: choose it |
| Co-Assessor | none |
| Second Reader | none |
| Coverage | Never |
| Claim Verifier | none |
| Report Writer | none |
| Runs per model | 3 |
| Model order | Randomized (recommended) |

- The estimate for a model without analyzed runs is the analyzed models' mean.

### Run 3: Thinking-level pair (M, model selection, optional)

- **Decides:** Confirms WS2.1 if the decode share stays the largest and reasoning stays at least half the output; refutes it if the reasoning share is under one half.
- **Predicted comparability:** Model comparison: only the thinking level differs
- **Estimate:** not estimated
- **Settles:** WS2.1

| Launcher field | Setting |
|---|---|
| Models | Model batch |
| Models Under Test | Orion Max, Orion Max (medium) |
| Run Target | Single suite |
| Benchmark Suite | GnollHack Core Suite |
| Scoring Profile | not shared by the analyzed runs: choose it |
| Response Style | Concise — production default |
| Source Code References | Allowed — answers cite source files and lines |
| Assessor | not shared by the analyzed runs: choose it |
| Co-Assessor | none |
| Second Reader | none |
| Coverage | Never |
| Claim Verifier | none |
| Report Writer | none |
| Runs per model | 1 |
| Model order | Randomized (recommended) |

- The estimate for a model without analyzed runs is the analyzed models' mean.

### Checks

No check is proposed.

The runs are ordered chat first, then the benchmark, then model selection.

## Limitations

- The benchmark asks single-turn questions; live chat carries the conversation before each question.
- It runs without the wiki text that live chat always gives the model before it answers, so a reading about retrieval or tool routing was measured under a condition live chat does not share.
- Spoiler-free mode, web search and subagents are off, and one combination of the chat's options is graded; a cause found here holds for that configuration only.
- The hypotheses come from a fixed rule table over these runs; points at risk are upper bounds, and a decomposition rests on the questions both sides have figures for. The writer's own hypotheses are qualitative.
- No other model answered these questions, so the data cannot tell the model from the instrument on intelligence.
- No earlier run of another model answered these questions under the same graders, scoring and prompt options.
- Costs are priced at today's catalog cards, as the comparison prices them.

The benchmark asks single questions, so longer conversations are not measured.

## Threats to validity

- The benchmark asks single-turn questions under one chat configuration. It does not exercise conversation history, pre-injected wiki context, spoiler-free mode, web search or subagents.
- Interval: Item sampling only.
- The graders are AI models. Each grader's provider relation to the model under test is stated under Setup and method; a grader from the model's own provider may read it more favorably.

## Reproducibility appendix

- **Run IDs:** 31
- **Run dates:** 2026-09-21
- **Model under test:** Northwind orion-max, thinking level not set
- **Grader models:** Assessor: Google gemini-3.8-flash
- **Harness version:** 41
- **Scoring method version:** 12
- **Comparability signature:** `sig-7f3a91`
- **System prompt SHA-256 prefix:** `e9b3e9a7c4d1`
- **Tool guides SHA-256 prefix:** `f59d8b30a1c7`
- **Pricing basis:** Catalog prices on 2026-09-21

## Question details

### Q1: An item question about gems

**Question:**

> Question 201

**Rubric:**

> - Rubric 201

**Answer:**

> Answer 201

**Graders:**

- **Assessor (Gemini 3.8 Flash):** score 92.

**Claim verifier:**

- No claims were checked.

## Fact sheet

```json
{
  "entries": [
    {
      "costDegraded": false,
      "costPerQuestionUsd": 0.05,
      "costRank": 1,
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
      "isSubject": true,
      "lastRunUtc": "2026-09-21T07:08:00Z",
      "modelTimeP50Ms": 12000,
      "qualityIndex": 85,
      "qualityLower": 82,
      "qualityRank": 1,
      "qualityUpper": 88,
      "runCount": 1,
      "speedDegraded": false,
      "speedRank": 1
    }
  ],
  "facts": [
    {
      "available": true,
      "display": "6",
      "key": "answers.scored",
      "value": 6
    },
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
      "display": "0",
      "key": "band.advanced.questions",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "band.advanced.score",
      "unavailableReason": "The model has no scored answer in this band."
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
      "display": "6",
      "key": "band.intermediate.questions",
      "value": 6
    },
    {
      "available": true,
      "display": "80",
      "key": "band.intermediate.score",
      "value": 80
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
      "display": "0",
      "key": "band.simple.questions",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "band.simple.score",
      "unavailableReason": "The model has no scored answer in this band."
    },
    {
      "available": true,
      "display": "0",
      "key": "bands.authored.advanced",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "bands.authored.intermediate",
      "value": 0
    },
    {
      "available": true,
      "display": "6",
      "key": "bands.authored.simple",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "claims.indeterminate",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "claims.refuted",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "claims.supported",
      "unavailableReason": "No claim verifier ruled on these answers."
    },
    {
      "available": true,
      "display": "1",
      "key": "comparison.models",
      "value": 1
    },
    {
      "available": false,
      "display": "not available",
      "key": "comparison.peerRuns",
      "unavailableReason": "A stand-alone run report has no peers."
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
      "display": "Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed",
      "key": "config.chat",
      "value": "Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed"
    },
    {
      "available": true,
      "display": "not set",
      "key": "config.maxOutputTokens"
    },
    {
      "available": true,
      "display": "Enabled",
      "key": "config.parallelMode",
      "value": "Enabled"
    },
    {
      "available": true,
      "display": "not set",
      "key": "config.reasoningMode"
    },
    {
      "available": true,
      "display": "not set",
      "key": "config.serviceTier"
    },
    {
      "available": true,
      "display": "Current",
      "key": "cost.basis",
      "value": "Current"
    },
    {
      "available": true,
      "display": "$0.0001",
      "key": "cost.component.cacheRead.perQuestion",
      "value": 0.000125
    },
    {
      "available": true,
      "display": "$0.0000",
      "key": "cost.component.cacheWrite.perQuestion",
      "value": 0
    },
    {
      "available": true,
      "display": "$0.0067",
      "key": "cost.component.output.perQuestion",
      "value": 0.0067
    },
    {
      "available": true,
      "display": "$0.0030",
      "key": "cost.component.reasoning.perQuestion",
      "value": 0.0029999999999999996
    },
    {
      "available": true,
      "display": "$0.0042",
      "key": "cost.component.uncachedInput.perQuestion",
      "value": 0.0041875
    },
    {
      "available": true,
      "display": "$0.050",
      "key": "cost.perQuestion",
      "value": 0.05
    },
    {
      "available": false,
      "display": "not available",
      "key": "cost.perRun",
      "unavailableReason": "Not recorded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "cost.pricingAsOf",
      "unavailableReason": "The price card publishes no date."
    },
    {
      "available": false,
      "display": "not available",
      "key": "cost.rank",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "cost.totalRunPerRun",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "80",
      "key": "dimension.accuracy",
      "value": 80
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
      "display": "80",
      "key": "dimension.completeness",
      "value": 80
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
      "display": "80",
      "key": "dimension.conciseness",
      "value": 80
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
      "display": "80",
      "key": "dimension.readability",
      "value": 80
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
      "display": "0 of 6 answers",
      "key": "errors.critical",
      "value": 0
    },
    {
      "available": true,
      "display": "0 % (95 % interval 0 %–39 %); before scoring method 13, flagged rather than confirmed",
      "key": "outcome.confirmedCriticalRate",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "panel.disagreements",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "panel.icc",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "panel.judgeDependentPairs",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "panel.meanAbsDelta",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "panel.memberAAlone",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "panel.memberBAlone",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "panel.referenceReaderIndex",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": false,
      "display": "not available",
      "key": "panel.referenceReaderOffset",
      "unavailableReason": "The model was not graded by an assessor panel in every run."
    },
    {
      "available": true,
      "display": "$0.125 per million tokens (today's catalog)",
      "key": "price.cacheRead.perMillion",
      "value": 0.125
    },
    {
      "available": true,
      "display": "$1.50 per million tokens (today's catalog)",
      "key": "price.cacheWrite.perMillion",
      "value": 1.5
    },
    {
      "available": true,
      "display": "$10.00 per million tokens (today's catalog)",
      "key": "price.output.perMillion",
      "value": 10
    },
    {
      "available": true,
      "display": "$10.00 per million tokens (today's catalog)",
      "key": "price.reasoning.perMillion",
      "value": 10
    },
    {
      "available": true,
      "display": "$1.25 per million tokens (today's catalog)",
      "key": "price.uncachedInput.perMillion",
      "value": 1.25
    },
    {
      "available": true,
      "display": "10 characters",
      "key": "profile.answerChars.median",
      "value": 10
    },
    {
      "available": true,
      "display": "6 answers",
      "key": "profile.answers",
      "value": 6
    },
    {
      "available": true,
      "display": "0 of 6 answers",
      "key": "profile.deficit.accuracy",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers",
      "key": "profile.deficit.completeness",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers",
      "key": "profile.deficit.conciseness",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers",
      "key": "profile.deficit.readability",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.findings.accuracy",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.findings.completeness",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.findings.conciseness",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.findings.critical_error",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.findings.other",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.findings.readability",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": true,
      "display": "0 of 1 run",
      "key": "profile.findings.runsWithSynthesis",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.findings.tool_use",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.isolatedMisses",
      "unavailableReason": "A per-model report sets no miss against a question matrix of other models."
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.lowestScorer",
      "unavailableReason": "A per-model report sets no miss against a question matrix of other models."
    },
    {
      "available": true,
      "display": "0 of 6 answers",
      "key": "profile.mayMislead",
      "value": 0
    },
    {
      "available": true,
      "display": "0 in 20 answers",
      "key": "profile.mayMisleadPer20",
      "value": 0
    },
    {
      "available": true,
      "display": "2 of 6 answers",
      "key": "profile.noToolAnswers",
      "value": 2
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.outcome.correct",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.outcome.incorrect",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.outcome.noAnswer",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.outcome.notAttempted",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.outcome.partial",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.outcome.wrongInsteadOfAbstaining",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": true,
      "display": "0 of 6 answers",
      "key": "profile.outputDefects",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "profile.sharedMisses",
      "unavailableReason": "A per-model report sets no miss against a question matrix of other models."
    },
    {
      "available": true,
      "display": "0 of 6 answers",
      "key": "profile.toolLimitHits",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers",
      "key": "profile.truncated",
      "value": 0
    },
    {
      "available": true,
      "display": "85 / 100",
      "key": "quality.index",
      "value": 85
    },
    {
      "available": true,
      "display": "82–88",
      "key": "quality.interval"
    },
    {
      "available": true,
      "display": "Item sampling only.",
      "key": "quality.intervalBasis",
      "value": "Item sampling only."
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
      "key": "quality.peerBest",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "quality.peerMedian",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "quality.rank",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": true,
      "display": "80 / 100",
      "key": "quality.rawIndex",
      "value": 80
    },
    {
      "available": true,
      "display": "1 of 1 question",
      "key": "quality.scoredItems",
      "value": 1
    },
    {
      "available": true,
      "display": "80 / 100",
      "key": "quality.unweightedMean",
      "value": 80
    },
    {
      "available": true,
      "display": "0.0 index points at risk (upper bound, not a causal estimate)",
      "key": "rca.atRisk.subject.budgetOrDefect",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 index points at risk (upper bound, not a causal estimate)",
      "key": "rca.atRisk.subject.criticalError",
      "value": 0
    },
    {
      "available": true,
      "display": "9.8 index points at risk (upper bound, not a causal estimate)",
      "key": "rca.atRisk.subject.noTool",
      "value": 9.833333333333334
    },
    {
      "available": true,
      "display": "0.8 index points at risk (upper bound, not a causal estimate)",
      "key": "rca.atRisk.subject.otherLow",
      "value": 0.8333333333333333
    },
    {
      "available": true,
      "display": "10.7 index points at risk against the model's median answer of 89 (upper bound, not a causal estimate)",
      "key": "rca.atRisk.subject.total",
      "value": 10.666666666666668
    },
    {
      "available": true,
      "display": "WC1.1",
      "key": "rca.axis.cost.best",
      "value": "WC1.1"
    },
    {
      "available": true,
      "display": "Probable cause",
      "key": "rca.axis.cost.status",
      "value": "ProbableCause"
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.axis.intelligence.best",
      "unavailableReason": "No hypothesis fired on this axis."
    },
    {
      "available": true,
      "display": "Not established",
      "key": "rca.axis.intelligence.status",
      "value": "NotEstablished"
    },
    {
      "available": true,
      "display": "WS3.1",
      "key": "rca.axis.speed.best",
      "value": "WS3.1"
    },
    {
      "available": true,
      "display": "Probable cause",
      "key": "rca.axis.speed.status",
      "value": "ProbableCause"
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.control.deficit.interval",
      "unavailableReason": "No other model answered these questions."
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.control.deficit.mean",
      "unavailableReason": "No other model answered these questions."
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.control.instrumentSuspect",
      "unavailableReason": "No other model answered these questions."
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.control.modelSpecific",
      "unavailableReason": "No other model answered these questions."
    },
    {
      "available": true,
      "display": "0 controls",
      "key": "rca.controls.count",
      "value": 0
    },
    {
      "available": true,
      "display": "capped at Low: no other model answered these questions",
      "key": "rca.controls.limit"
    },
    {
      "available": true,
      "display": "none",
      "key": "rca.controls.providers",
      "value": 0
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-W.1.against"
    },
    {
      "available": true,
      "display": "Intelligence",
      "key": "rca.hypothesis.H0-W.1.axis"
    },
    {
      "available": true,
      "display": "No contrast could be made.",
      "key": "rca.hypothesis.H0-W.1.for"
    },
    {
      "available": true,
      "display": "Low",
      "key": "rca.hypothesis.H0-W.1.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "capped at Low: a single run per suite gives the contrasts little power",
      "key": "rca.hypothesis.H0-W.1.gradeReasons"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-W.1.locus"
    },
    {
      "available": true,
      "display": "Orion Max",
      "key": "rca.hypothesis.H0-W.1.models"
    },
    {
      "available": true,
      "display": "A Mann–Whitney contrast is established after the Holm adjustment.",
      "key": "rca.hypothesis.H0-W.1.refutedIf"
    },
    {
      "available": true,
      "display": "The contrasts between groups of Orion Max's own questions are within sampling noise.",
      "key": "rca.hypothesis.H0-W.1.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.WC1.1.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.WC1.1.axis"
    },
    {
      "available": true,
      "display": "Visible output and reasoning are 69 % of Orion Max's cost per question.",
      "key": "rca.hypothesis.WC1.1.for"
    },
    {
      "available": true,
      "display": "Moderate",
      "key": "rca.hypothesis.WC1.1.grade",
      "value": "Moderate"
    },
    {
      "available": true,
      "display": "replicates failed: Orion Max has a single run per suite.",
      "key": "rca.hypothesis.WC1.1.gradeReasons"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.WC1.1.locus"
    },
    {
      "available": true,
      "display": "Orion Max",
      "key": "rca.hypothesis.WC1.1.models"
    },
    {
      "available": true,
      "display": "Another component is larger.",
      "key": "rca.hypothesis.WC1.1.refutedIf"
    },
    {
      "available": true,
      "display": "69 %",
      "key": "rca.hypothesis.WC1.1.share",
      "value": 0.6922390722569134
    },
    {
      "available": true,
      "display": "Output (with reasoning) is Orion Max's largest cost component (where its cost goes, not a comparison).",
      "key": "rca.hypothesis.WC1.1.statement"
    },
    {
      "available": true,
      "display": "Reasoning is 31 % of Orion Max's output tokens.",
      "key": "rca.hypothesis.WS2.1.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.WS2.1.axis"
    },
    {
      "available": true,
      "display": "Output generation is 70 % (69–71 %) of model time per question.",
      "key": "rca.hypothesis.WS2.1.for"
    },
    {
      "available": true,
      "display": "Not supported",
      "key": "rca.hypothesis.WS2.1.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "the rule's own test refutes it: reasoning is 31 % of Orion Max's output tokens.",
      "key": "rca.hypothesis.WS2.1.gradeReasons"
    },
    {
      "available": true,
      "display": "69–71 %",
      "key": "rca.hypothesis.WS2.1.interval"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.WS2.1.locus"
    },
    {
      "available": true,
      "display": "Orion Max",
      "key": "rca.hypothesis.WS2.1.models"
    },
    {
      "available": true,
      "display": "next run 3",
      "key": "rca.hypothesis.WS2.1.nextRun",
      "value": 3
    },
    {
      "available": true,
      "display": "The reasoning share is under one half.",
      "key": "rca.hypothesis.WS2.1.refutedIf"
    },
    {
      "available": true,
      "display": "70 %",
      "key": "rca.hypothesis.WS2.1.share",
      "value": 0.7038770053475936
    },
    {
      "available": true,
      "display": "Most of Orion Max's time is output generation, driven by reasoning tokens (where its time goes, not a comparison).",
      "key": "rca.hypothesis.WS2.1.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.WS3.1.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.WS3.1.axis"
    },
    {
      "available": true,
      "display": "Across Orion Max's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95 % interval 0.23 to 0.99, n 6).",
      "key": "rca.hypothesis.WS3.1.for"
    },
    {
      "available": true,
      "display": "Moderate",
      "key": "rca.hypothesis.WS3.1.grade",
      "value": "Moderate"
    },
    {
      "available": true,
      "display": "replicates failed: Orion Max has a single run per suite.",
      "key": "rca.hypothesis.WS3.1.gradeReasons"
    },
    {
      "available": true,
      "display": "model / chat",
      "key": "rca.hypothesis.WS3.1.locus"
    },
    {
      "available": true,
      "display": "Orion Max",
      "key": "rca.hypothesis.WS3.1.models"
    },
    {
      "available": true,
      "display": "next run 2",
      "key": "rca.hypothesis.WS3.1.nextRun",
      "value": 2
    },
    {
      "available": true,
      "display": "The ρ interval includes zero.",
      "key": "rca.hypothesis.WS3.1.refutedIf"
    },
    {
      "available": true,
      "display": "Tool rounds multiply Orion Max's time (where its time goes, not a comparison).",
      "key": "rca.hypothesis.WS3.1.statement"
    },
    {
      "available": true,
      "display": "No earlier run of another model answered these questions under the same instrument.",
      "key": "rca.known.1"
    },
    {
      "available": true,
      "display": "Orion Max answered 2 of 6 scored questions without a tool call.",
      "key": "rca.known.2"
    },
    {
      "available": true,
      "display": "Output generation is 59 % of Orion Max's time per question.",
      "key": "rca.known.3"
    },
    {
      "available": true,
      "display": "Visible output is 48 % of Orion Max's cost per question.",
      "key": "rca.known.4"
    },
    {
      "available": true,
      "display": "Priced from the catalog as of 2026-09-20. Comparable across dates; not what was actually spent.",
      "key": "rca.pricingBasis"
    },
    {
      "available": true,
      "display": "Control run (B, recommended): Lyra Mini (high) and Zeta Prime (high) on suite GnollHack Core Suite, 1 per model. Separates the model from the instrument: once two controls, one from another provider, have run on the same questions under the same graders, the intelligence hypotheses are graded without the control cap.",
      "key": "rca.run.1.summary"
    },
    {
      "available": true,
      "display": "Replicate (M, recommended): Orion Max on suite GnollHack Core Suite, 3 per model. Confirms WS3.1 if model time keeps rising with the calls per answer; refutes it if the ρ interval includes zero.",
      "key": "rca.run.2.summary"
    },
    {
      "available": true,
      "display": "Thinking-level pair (M, optional): Orion Max and Orion Max (medium) on suite GnollHack Core Suite, 1 per model. Confirms WS2.1 if the decode share stays the largest and reasoning stays at least half the output; refutes it if the reasoning share is under one half.",
      "key": "rca.run.3.summary"
    },
    {
      "available": true,
      "display": "no harness or suite hypothesis holds model-selection runs back",
      "key": "rca.systemFirst",
      "value": false
    },
    {
      "available": true,
      "display": "1 % of the cost per question",
      "key": "rca.within.subject.cost.cacheRead.share",
      "value": 0.008920606601248885
    },
    {
      "available": true,
      "display": "0 % of the cost per question",
      "key": "rca.within.subject.cost.cacheWrite.share",
      "value": 0
    },
    {
      "available": true,
      "display": "48 % of the cost per question",
      "key": "rca.within.subject.cost.output.share",
      "value": 0.47814451382694023
    },
    {
      "available": true,
      "display": "21 % of the cost per question",
      "key": "rca.within.subject.cost.reasoning.share",
      "value": 0.21409455842997321
    },
    {
      "available": true,
      "display": "$0.014 per question, mean",
      "key": "rca.within.subject.cost.total",
      "value": 0.0140125
    },
    {
      "available": true,
      "display": "30 % of the cost per question",
      "key": "rca.within.subject.cost.uncachedInput.share",
      "value": 0.2988403211418377
    },
    {
      "available": true,
      "display": "0.3 s per question, mean",
      "key": "rca.within.subject.time.other.seconds",
      "value": 0.3
    },
    {
      "available": true,
      "display": "3 % of the time per question",
      "key": "rca.within.subject.time.other.share",
      "value": 0.03340757238307349
    },
    {
      "available": true,
      "display": "5.3 s per question, mean",
      "key": "rca.within.subject.time.outputGeneration.seconds",
      "value": 5.265
    },
    {
      "available": true,
      "display": "59 % of the time per question",
      "key": "rca.within.subject.time.outputGeneration.share",
      "value": 0.5863028953229398
    },
    {
      "available": true,
      "display": "0.0 s per question, mean",
      "key": "rca.within.subject.time.ownWaits.seconds",
      "value": 0
    },
    {
      "available": true,
      "display": "0 % of the time per question",
      "key": "rca.within.subject.time.ownWaits.share",
      "value": 0
    },
    {
      "available": true,
      "display": "1.5 s per question, mean",
      "key": "rca.within.subject.time.toolExecution.seconds",
      "value": 1.5
    },
    {
      "available": true,
      "display": "17 % of the time per question",
      "key": "rca.within.subject.time.toolExecution.share",
      "value": 0.16703786191536749
    },
    {
      "available": true,
      "display": "9.0 s per question, mean",
      "key": "rca.within.subject.time.total",
      "value": 8.98
    },
    {
      "available": true,
      "display": "1.9 s per question, mean",
      "key": "rca.within.subject.time.ttft.seconds",
      "value": 1.915
    },
    {
      "available": true,
      "display": "21 % of the time per question",
      "key": "rca.within.subject.time.ttft.share",
      "value": 0.21325167037861914
    },
    {
      "available": true,
      "display": "2026-09-21",
      "key": "run.dates"
    },
    {
      "available": true,
      "display": "41",
      "key": "run.harnessVersion"
    },
    {
      "available": true,
      "display": "31",
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
      "display": "89 (4 answers)",
      "key": "score.withTools",
      "value": 88.75
    },
    {
      "available": true,
      "display": "63 (2 answers)",
      "key": "score.withoutTools",
      "value": 62.5
    },
    {
      "available": true,
      "display": "25",
      "key": "scoring.criticalErrorCap",
      "value": 25
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
      "available": false,
      "display": "not available",
      "key": "servedTier",
      "unavailableReason": "No answer recorded the tier it was served at."
    },
    {
      "available": false,
      "display": "not available",
      "key": "speed.decodeRate",
      "unavailableReason": "No answer's final call recorded a measurable decode rate."
    },
    {
      "available": true,
      "display": "2.5",
      "key": "speed.modelCallsPerAnswer",
      "value": 2.5
    },
    {
      "available": false,
      "display": "not available",
      "key": "speed.modelTimeMean",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "12.0 s",
      "key": "speed.modelTimeP50",
      "value": 12000
    },
    {
      "available": false,
      "display": "not available",
      "key": "speed.modelTimeP90",
      "unavailableReason": "Not recorded."
    },
    {
      "available": true,
      "display": "0 % of model time",
      "key": "speed.ownWaitShare",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "speed.rank",
      "unavailableReason": "A stand-alone run report has no peers."
    },
    {
      "available": false,
      "display": "not available",
      "key": "speed.retriesPerAnswer",
      "unavailableReason": "No answer recorded its retried attempts."
    },
    {
      "available": true,
      "display": "every answer served at the requested tier",
      "key": "speed.servedTierMismatch",
      "value": 0
    },
    {
      "available": true,
      "display": "1.5 s",
      "key": "speed.toolTimeP50",
      "value": 1500
    },
    {
      "available": false,
      "display": "not available",
      "key": "speed.ttftP50",
      "unavailableReason": "Not recorded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "speed.ttftP90",
      "unavailableReason": "No answer recorded a time to first token."
    },
    {
      "available": true,
      "display": "0.8 s",
      "key": "speed.ttftPerCallP50",
      "value": 760
    },
    {
      "available": true,
      "display": "No response-style conflict",
      "key": "style.responseStyleConflict",
      "value": false
    },
    {
      "available": true,
      "display": "Orion Max",
      "key": "subject.label",
      "value": "Orion Max"
    },
    {
      "available": true,
      "display": "orion-max",
      "key": "subject.modelId",
      "value": "orion-max"
    },
    {
      "available": true,
      "display": "Northwind",
      "key": "subject.provider",
      "value": "Northwind"
    },
    {
      "available": true,
      "display": "1",
      "key": "subject.runs",
      "value": 1
    },
    {
      "available": true,
      "display": "Comparable",
      "key": "subject.state",
      "value": "Comparable"
    },
    {
      "available": true,
      "display": "not set",
      "key": "subject.thinkingLevel"
    },
    {
      "available": true,
      "display": "GnollHack Core Suite",
      "key": "suite.name",
      "value": "GnollHack Core Suite"
    },
    {
      "available": true,
      "display": "1",
      "key": "suite.questions",
      "value": 1
    },
    {
      "available": true,
      "display": "23 % of input tokens read from the cache",
      "key": "tokens.cacheReadShare",
      "value": 0.22988505747126436
    },
    {
      "available": true,
      "display": "4,350",
      "key": "tokens.inputPerQuestion",
      "value": 4350
    },
    {
      "available": true,
      "display": "970",
      "key": "tokens.outputPerQuestion",
      "value": 970
    },
    {
      "available": true,
      "display": "300",
      "key": "tokens.reasoningPerQuestion",
      "value": 300
    },
    {
      "available": true,
      "display": "31 % of output tokens",
      "key": "tokens.reasoningShare",
      "value": 0.30927835051546393
    },
    {
      "available": true,
      "display": "0.0",
      "key": "tools.callsPerQuestion",
      "value": 0
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
      "key": "tools.failed",
      "value": 0
    },
    {
      "available": true,
      "display": "0",
      "key": "tools.refusedByBudget",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "tools.share.knowledgeBase",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "tools.share.other",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "tools.share.sourceCode",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "tools.share.structuredLookup",
      "unavailableReason": "No tool call succeeded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "tools.share.wiki",
      "unavailableReason": "No tool call succeeded."
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
      "role": "Assessor",
      "sameFamilyAsSubject": false
    }
  ],
  "knownNames": [
    "Gemini 3.8 Flash",
    "GnollHack Core Suite",
    "Google",
    "Northwind",
    "Orion Max",
    "gemini-3.8-flash",
    "orion-max"
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
      "authoredBand": "Simple",
      "band": "Intermediate",
      "costUsd": 0.0132,
      "criticalError": false,
      "inputTokens": 4100,
      "itemRevisionUsed": 1,
      "modelCallCount": 3,
      "modelTimeMs": 8220,
      "number": 1,
      "orderIndex": 1,
      "outputTokens": 920,
      "peerCount": 0,
      "peersAbove": 0,
      "questionKey": "201",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 1,
      "score": 92,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "costUsd": 0.013525,
      "criticalError": false,
      "inputTokens": 4200,
      "itemRevisionUsed": 1,
      "modelCallCount": 2,
      "modelTimeMs": 5740,
      "number": 2,
      "orderIndex": 2,
      "outputTokens": 940,
      "peerCount": 0,
      "peersAbove": 0,
      "questionKey": "202",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 1,
      "score": 88,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "costUsd": 0.013849999999999998,
      "criticalError": false,
      "inputTokens": 4300,
      "itemRevisionUsed": 1,
      "modelCallCount": 3,
      "modelTimeMs": 8820,
      "number": 3,
      "orderIndex": 3,
      "outputTokens": 960,
      "peerCount": 0,
      "peersAbove": 0,
      "questionKey": "203",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 1,
      "score": 30,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "costUsd": 0.014175,
      "criticalError": false,
      "inputTokens": 4400,
      "itemRevisionUsed": 1,
      "modelCallCount": 2,
      "modelTimeMs": 6140,
      "number": 4,
      "orderIndex": 4,
      "outputTokens": 980,
      "peerCount": 0,
      "peersAbove": 0,
      "questionKey": "204",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 1,
      "score": 90,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "costUsd": 0.014499999999999999,
      "criticalError": false,
      "inputTokens": 4500,
      "itemRevisionUsed": 1,
      "modelCallCount": 3,
      "modelTimeMs": 9420,
      "number": 5,
      "orderIndex": 5,
      "outputTokens": 1000,
      "peerCount": 0,
      "peersAbove": 0,
      "questionKey": "205",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 1,
      "score": 85,
      "toolCalls": 0
    },
    {
      "authoredBand": "Simple",
      "band": "Intermediate",
      "costUsd": 0.014825000000000001,
      "criticalError": false,
      "inputTokens": 4600,
      "itemRevisionUsed": 1,
      "modelCallCount": 2,
      "modelTimeMs": 6540,
      "number": 6,
      "orderIndex": 6,
      "outputTokens": 1020,
      "peerCount": 0,
      "peersAbove": 0,
      "questionKey": "206",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 1,
      "score": 95,
      "toolCalls": 0
    }
  ],
  "rootCause": {
    "axes": [
      {
        "axis": "intelligence",
        "reason": "No hypothesis fired on this axis.",
        "status": "NotEstablished"
      },
      {
        "axis": "speed",
        "bestHypothesis": "WS3.1",
        "reason": "WS3.1 is the best hypothesis, graded Moderate: replicates failed: Orion Max has a single run per suite.",
        "status": "ProbableCause"
      },
      {
        "axis": "cost",
        "bestHypothesis": "WC1.1",
        "reason": "WC1.1 is the best hypothesis, graded Moderate: replicates failed: Orion Max has a single run per suite.",
        "status": "ProbableCause"
      }
    ],
    "checks": [],
    "computedAtUtc": "2026-09-21T08:00:00Z",
    "controlItems": [],
    "controlLimit": "Low",
    "controlLimitReason": "no other model answered these questions",
    "controls": [],
    "costPairs": [],
    "costRows": [
      {
        "kind": "model",
        "label": "Orion Max",
        "segments": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.0041875
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.000125
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.0067
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.0029999999999999996
          }
        ],
        "token": "subject",
        "total": 0.0140125,
        "unit": "usd"
      },
      {
        "kind": "band",
        "label": "Intermediate questions",
        "segments": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.0041875
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.000125
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.0067
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.0029999999999999996
          }
        ],
        "total": 0.0140125,
        "unit": "usd"
      }
    ],
    "engineVersion": 1,
    "evidence": [
      {
        "atRisk": [
          {
            "key": "noTool",
            "label": "answers without tools",
            "value": 9.833333333333334
          },
          {
            "key": "budgetOrDefect",
            "label": "budget, truncation or output defects",
            "value": 0
          },
          {
            "key": "criticalError",
            "label": "confirmed critical errors",
            "value": 0
          },
          {
            "key": "otherLow",
            "label": "other low-scoring answers",
            "value": 0.8333333333333333
          }
        ],
        "budgetOrDefectAnswers": 0,
        "cacheReadShare": 0.22988505747126436,
        "callsPerAnswer": 2.5,
        "cost": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.0041875
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.000125
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.0067
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.0029999999999999996
          }
        ],
        "criticalAnswers": 0,
        "label": "Orion Max",
        "medianQuality": 89,
        "modelTimeMs": 7480,
        "noToolAnswers": 2,
        "outputTokens": 970,
        "reasoningShare": 0.30927835051546393,
        "reasoningTokens": 300,
        "scoredAnswers": 6,
        "telemetryAnswers": 6,
        "time": [
          {
            "key": "ttft",
            "label": "time to first token",
            "value": 1.915
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 5.265
          },
          {
            "key": "toolExecution",
            "label": "tool execution",
            "value": 1.5
          },
          {
            "key": "ownWaits",
            "label": "own waits and retries",
            "value": 0
          },
          {
            "key": "other",
            "label": "other",
            "value": 0.3
          }
        ],
        "token": "subject",
        "tokens": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 3350
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 1000
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 670
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 300
          }
        ],
        "ttftPerCallP50Ms": 760
      }
    ],
    "hypotheses": [
      {
        "against": [],
        "axis": "intelligence",
        "confounds": [],
        "factKeys": [],
        "for": [
          "No contrast could be made."
        ],
        "grade": "Low",
        "gradeReasons": [
          "capped at Low: a single run per suite gives the contrasts little power"
        ],
        "id": "H0-W",
        "key": "H0-W.1",
        "kind": "null",
        "loci": [],
        "locus": "—",
        "models": [
          "subject"
        ],
        "modelsText": "Orion Max",
        "number": 1,
        "refutedIf": "A Mann–Whitney contrast is established after the Holm adjustment.",
        "robustness": [],
        "shortStatement": "Noise within Orion Max",
        "statement": "The contrasts between groups of Orion Max's own questions are within sampling noise."
      },
      {
        "against": [],
        "axis": "speed",
        "confounds": [],
        "factKeys": [
          "speed.modelCallsPerAnswer"
        ],
        "for": [
          "Across Orion Max's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95 % interval 0.23 to 0.99, n 6)."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "replicates failed: Orion Max has a single run per suite."
        ],
        "id": "WS3",
        "key": "WS3.1",
        "kind": "within",
        "loci": [
          "model",
          "chat"
        ],
        "locus": "model / chat",
        "models": [
          "subject"
        ],
        "modelsText": "Orion Max",
        "nextRunNumber": 2,
        "number": 1,
        "refutedIf": "The ρ interval includes zero.",
        "robustness": [
          {
            "detail": "Orion Max has a single run per suite.",
            "name": "Replicates",
            "status": "Failed"
          },
          {
            "detail": "ρ stays at or above 0.5 without any one question.",
            "name": "Leave one question out",
            "status": "Passed"
          }
        ],
        "shortStatement": "Tool rounds, time: Orion Max",
        "statement": "Tool rounds multiply Orion Max's time (where its time goes, not a comparison)."
      },
      {
        "against": [
          "Reasoning is 31 % of Orion Max's output tokens."
        ],
        "axis": "speed",
        "confounds": [],
        "factKeys": [
          "rca.within.subject.time.outputGeneration.share",
          "tokens.reasoningShare"
        ],
        "for": [
          "Output generation is 70 % (69–71 %) of model time per question."
        ],
        "grade": "NotSupported",
        "gradeReasons": [
          "the rule's own test refutes it: reasoning is 31 % of Orion Max's output tokens."
        ],
        "id": "WS2",
        "key": "WS2.1",
        "kind": "within",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "subject"
        ],
        "modelsText": "Orion Max",
        "nextRunNumber": 3,
        "number": 1,
        "refutedIf": "The reasoning share is under one half.",
        "robustness": [
          {
            "detail": "Orion Max has a single run per suite.",
            "name": "Replicates",
            "status": "Failed"
          },
          {
            "detail": "The deciding share keeps its sign and stays the largest without the question that contributes most.",
            "name": "Leave one question out",
            "status": "Passed"
          }
        ],
        "share": 0.7038770053475936,
        "shareLower": 0.6944414987628137,
        "shareUpper": 0.711864406779661,
        "shortStatement": "Output generation: Orion Max",
        "statement": "Most of Orion Max's time is output generation, driven by reasoning tokens (where its time goes, not a comparison)."
      },
      {
        "against": [],
        "axis": "cost",
        "confounds": [],
        "factKeys": [
          "cost.component.output.perQuestion",
          "cost.component.reasoning.perQuestion"
        ],
        "for": [
          "Visible output and reasoning are 69 % of Orion Max's cost per question."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "replicates failed: Orion Max has a single run per suite."
        ],
        "id": "WC1",
        "key": "WC1.1",
        "kind": "within",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "subject"
        ],
        "modelsText": "Orion Max",
        "number": 1,
        "refutedIf": "Another component is larger.",
        "robustness": [
          {
            "detail": "Orion Max has a single run per suite.",
            "name": "Replicates",
            "status": "Failed"
          }
        ],
        "share": 0.6922390722569134,
        "shortStatement": "Output cost: Orion Max",
        "statement": "Output (with reasoning) is Orion Max's largest cost component (where its cost goes, not a comparison)."
      }
    ],
    "knownFacts": [
      {
        "factKeys": [
          "rca.controls.count"
        ],
        "kind": "count",
        "number": 1,
        "text": "No earlier run of another model answered these questions under the same instrument."
      },
      {
        "factKeys": [
          "profile.noToolAnswers"
        ],
        "kind": "count",
        "number": 2,
        "text": "Orion Max answered 2 of 6 scored questions without a tool call."
      },
      {
        "factKeys": [
          "rca.within.subject.time.outputGeneration.share"
        ],
        "kind": "decomposition",
        "number": 3,
        "text": "Output generation is 59 % of Orion Max's time per question."
      },
      {
        "factKeys": [
          "rca.within.subject.cost.output.share"
        ],
        "kind": "decomposition",
        "number": 4,
        "text": "Visible output is 48 % of Orion Max's cost per question."
      }
    ],
    "models": [
      {
        "entryKey": "run:31",
        "isReference": false,
        "isSubject": true,
        "label": "Orion Max",
        "modelId": "orion-max",
        "provider": "Northwind",
        "runIds": [
          31
        ],
        "token": "subject"
      }
    ],
    "notes": [
      "No earlier run of another model answered these questions under the same graders, scoring and prompt options.",
      "Costs are priced at today's catalog cards, as the comparison prices them."
    ],
    "origin": "RunCompletion",
    "pointsAtRisk": [
      {
        "kind": "model",
        "label": "Orion Max",
        "segments": [
          {
            "key": "noTool",
            "label": "answers without tools",
            "value": 9.833333333333334
          },
          {
            "key": "budgetOrDefect",
            "label": "budget, truncation or output defects",
            "value": 0
          },
          {
            "key": "criticalError",
            "label": "confirmed critical errors",
            "value": 0
          },
          {
            "key": "otherLow",
            "label": "other low-scoring answers",
            "value": 0.8333333333333333
          }
        ],
        "token": "subject",
        "total": 10.666666666666668,
        "unit": "points"
      },
      {
        "kind": "band",
        "label": "Intermediate questions",
        "segments": [
          {
            "key": "noTool",
            "label": "answers without tools",
            "value": 9.833333333333334
          },
          {
            "key": "budgetOrDefect",
            "label": "budget, truncation or output defects",
            "value": 0
          },
          {
            "key": "criticalError",
            "label": "confirmed critical errors",
            "value": 0
          },
          {
            "key": "otherLow",
            "label": "other low-scoring answers",
            "value": 0.8333333333333333
          }
        ],
        "total": 10.666666666666668,
        "unit": "points"
      }
    ],
    "pricingBasis": "Priced from the catalog as of 2026-09-20. Comparable across dates; not what was actually spent.",
    "pricingBasisKind": "catalog",
    "qualitySplits": [],
    "recommendedRuns": [
      {
        "afterSystemHypotheses": false,
        "allowSourceCodeReferences": true,
        "decides": "Separates the model from the instrument: once two controls, one from another provider, have run on the same questions under the same graders, the intelligence hypotheses are graded without the control cap.",
        "estimatedMinutes": 16,
        "hypothesisIds": [],
        "kind": "control",
        "mark": "Recommended",
        "modelNames": [
          "Lyra Mini (high)",
          "Zeta Prime (high)"
        ],
        "notes": [
          "Without the subject: its run already exists and becomes the other side of the comparison.",
          "The estimate for a model without analyzed runs is the analyzed models' mean."
        ],
        "number": 1,
        "order": "Randomized",
        "predictedTier": "Model comparison: every must-match key agrees and only the model differs",
        "purpose": "B",
        "runCount": 1,
        "runMode": "Batch",
        "runsPerModel": 1,
        "secondOpinionMode": 0,
        "suiteId": 5,
        "suiteName": "GnollHack Core Suite",
        "targetKind": "Suite",
        "testedModelConfigurationIds": [
          102,
          104
        ],
        "verboseMode": false
      },
      {
        "afterSystemHypotheses": false,
        "allowSourceCodeReferences": true,
        "decides": "Confirms WS3.1 if model time keeps rising with the calls per answer; refutes it if the ρ interval includes zero.",
        "estimatedMinutes": 24,
        "hypothesisIds": [
          "WS3.1"
        ],
        "kind": "replicate",
        "mark": "Recommended",
        "modelNames": [
          "Orion Max"
        ],
        "notes": [
          "The estimate for a model without analyzed runs is the analyzed models' mean."
        ],
        "number": 2,
        "order": "Randomized",
        "predictedTier": "Model comparison with the analyzed runs; a replicate (Tier A) of each model's own runs",
        "purpose": "M",
        "runCount": 1,
        "runMode": "Batch",
        "runsPerModel": 3,
        "secondOpinionMode": 0,
        "suiteId": 5,
        "suiteName": "GnollHack Core Suite",
        "targetKind": "Suite",
        "testedModelConfigurationIds": [
          100
        ],
        "verboseMode": false
      },
      {
        "afterSystemHypotheses": false,
        "allowSourceCodeReferences": true,
        "decides": "Confirms WS2.1 if the decode share stays the largest and reasoning stays at least half the output; refutes it if the reasoning share is under one half.",
        "estimatedMinutes": 16,
        "hypothesisIds": [
          "WS2.1"
        ],
        "kind": "thinkingPair",
        "mark": "Optional",
        "modelNames": [
          "Orion Max",
          "Orion Max (medium)"
        ],
        "notes": [
          "The estimate for a model without analyzed runs is the analyzed models' mean."
        ],
        "number": 3,
        "order": "Randomized",
        "predictedTier": "Model comparison: only the thinking level differs",
        "purpose": "M",
        "runCount": 1,
        "runMode": "Batch",
        "runsPerModel": 1,
        "secondOpinionMode": 0,
        "suiteId": 5,
        "suiteName": "GnollHack Core Suite",
        "targetKind": "Suite",
        "testedModelConfigurationIds": [
          100,
          150
        ],
        "verboseMode": false
      }
    ],
    "scope": "Model",
    "speedPairs": [],
    "speedRows": [
      {
        "kind": "model",
        "label": "Orion Max",
        "segments": [
          {
            "key": "ttft",
            "label": "time to first token",
            "value": 1.893674832962138
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 5.206369710467706
          },
          {
            "key": "toolExecution",
            "label": "tool execution",
            "value": 1.483296213808463
          },
          {
            "key": "ownWaits",
            "label": "own waits and retries",
            "value": 0
          },
          {
            "key": "other",
            "label": "other",
            "value": 0.29665924276169264
          }
        ],
        "token": "subject",
        "total": 8.88,
        "unit": "seconds"
      },
      {
        "kind": "band",
        "label": "Intermediate questions",
        "segments": [
          {
            "key": "ttft",
            "label": "time to first token",
            "value": 1.893674832962138
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 5.206369710467706
          },
          {
            "key": "toolExecution",
            "label": "tool execution",
            "value": 1.483296213808463
          },
          {
            "key": "ownWaits",
            "label": "own waits and retries",
            "value": 0
          },
          {
            "key": "other",
            "label": "other",
            "value": 0.29665924276169264
          }
        ],
        "total": 8.88,
        "unit": "seconds"
      }
    ],
    "systemFirst": false
  },
  "rows": [],
  "subjectDisplayName": "Orion Max",
  "subjectExplanation": "Comparable: measured under the baseline condition.",
  "subjectKey": "run:31",
  "subjectKind": "Run",
  "subjectLabel": "Orion Max",
  "subjectModelId": "orion-max",
  "subjectProvider": "Northwind",
  "subjectRunIds": [
    31
  ],
  "subjectState": "Comparable",
  "suiteId": 5,
  "suiteName": "GnollHack Core Suite"
}
```

## Evaluation terms

- **Purpose statement:** Internal evaluation of candidate AI models for the Overseer assistant within GnollHack.
- **Distillation / training prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.
- **Third-party model content:** Outputs generated by **Orion Max** (Northwind), graded by models from Google, and described in this document by **Claude Opus 5.5** (Anthropic) are third-party content evaluated solely for domain-specific benchmark scoring and operational model selection.

---

*Document ID 131 · format version 13 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Full · peers named*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically for structure, permitted figures and references, word limits, disclosure of benchmark text, peer names, significance claims, interval-overlap wording, hype words and spelling; the checks do not verify the prose's interpretations.*
