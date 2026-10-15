# GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Root Cause Analysis

*INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.*

- **Date:** 2026-09-28
- **Battery:** Core knowledge, revision 2: 2 suites, 2 runs per suite, weighting scheme Questions and difficulty
- **Questions:** 4
- **Member runs:** 4 (battery run 9)
- **Peers:** none; this is a stand-alone report

## Summary

### What we found

> [!IMPORTANT]
> **Key finding.** Intelligence: Not established; speed: Probable cause; cost: Root cause established.

### At a glance

| Model | Intelligence | Median time | Cost per question | Where it lost most points | Flags |
|---|---|---|---|---|---|
| **GPT-5.6 Luna** | 80 (77–83), rank 2 | 12.3 s | $0.036 | confirmed critical errors, 5.6 points | 1 critical error |

### How sure are we?

> [!WARNING]
> **Caveat.** Established (High): measured, established after adjustment, robust, with two or more runs per suite on both sides. Probable (Moderate): one of those conditions is missed. Suggestive (Low): qualitative only, two or more conditions missed, or the difference may be noise. Ruled out (Not supported): the rule's own test refutes it.
>
> Every model has two or more runs per suite, so a cause can be graded up to Established (High).
>
> The intelligence hypotheses that could lie in the model or in the instrument are capped at Suggestive (Low): no other model answered these questions.

## Why each model scored as it did

| Model | Index | Strengths | Lost points on | Flags |
|---|---|---|---|---|
| **GPT-5.6 Luna** | 80 | none ranked first or second | confirmed critical errors: 5.6 index points on 1 question (S2-Q3); weakest dimension Completeness, 2.0 points below Accuracy | 1 critical error (S2-Q3) |

*Each entry is a count, a share or a rank that was measured over the model's scored answers; none says why. Lost points on: the largest share of the model's index points at risk (Appendix A), with its questions.*

## Scope and design

This analysis describes battery run 9 of GPT-5.6 Luna on its own. Other models enter only as controls: none qualified.

### Setup and method

- **Battery:** Core knowledge, revision 2: 2 suites, 2 runs per suite, weighting scheme Questions and difficulty; 4 questions in all.
- **Chat configuration under test:** Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed
- **Model under test:** GPT-5.6 Luna (OpenAI, gpt-5.6-luna), thinking level high; 4 member runs.
- **Grading:** each answer is graded on accuracy, completeness, conciseness and readability, weighted Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %. Each dimension is graded on behaviorally anchored levels scored 1, 15, 35, 55, 72, 87, 100. A critical error caps the answer's quality at 25.
- **Graders:**
  - Panel member A: Gemini 3.8 Flash (Google, gemini-3.8-flash), different provider from the model under test
  - Panel member B: Claude Haiku 5 (Anthropic, claude-haiku-5), different provider from the model under test
  - Reference reader: DeepSeek V4 (DeepSeek, deepseek-v4), different provider from the model under test
  - Claim verifier: Gemini 3.8 Flash (Google, gemini-3.8-flash), different provider from the model under test
- **Formulas:** answer quality is the weighted geometric mean of the four dimension scores, capped by a critical error; in a panel run it is the mean of both graders' scores. The Intelligence Index is the difficulty-weighted mean of answer quality. Median answer time is the median model time per answer, with tool time excluded. Cost per question is the model under test's spend divided by the questions asked.
- **Battery composite:** the Overall Index is the sum over the suites of each suite's weight times its Intelligence Index, under the battery's weighting scheme; it is not comparable with a single suite's index. Speed and cost are pooled over the member runs, and cost per run is per battery pass, one run of every suite.
- **Comparability:** this report describes the battery result on its own, measured in the comparability class `5e1d0c7b3a91f08c`.
- **Pricing basis:** Catalog prices on 2026-09-20 (price card dated 2026-09-01)
- **Versions:** harness 41, scoring method 12.

### Controls

No earlier run of another model answered these questions under the same graders, scoring and prompt options. Speed and cost are analyzed from the model's own accounting. The intelligence hypotheses that could lie in the model or in the instrument are capped at Low, and the intelligence axis stays not established until a control run exists.

One run per suite on each side limits what this design can decide; the next runs add replicates.

## Evidence: intelligence

| Model | Intelligence Index | 95 % interval | Rank | Critical errors |
|---|---|---|---|---|
| **GPT-5.6 Luna** | 80 | 77–83 | 2 | 1 of 4 answers |

*A joint rank means the model's 95 % interval overlaps a neighbor's, so the intervals do not establish the order between them.*

| Model | Accuracy | Completeness | Conciseness | Readability |
|---|---|---|---|---|
| **GPT-5.6 Luna** | 80 | 78 | 82 | 84 |

### What the graders recorded

- The grader recorded a comment on this answer.
  - *Evidence:* Q1 · S1-Q1 (90 / 100)

The first known fact: No earlier run of another model answered these questions under the same instrument.

## Evidence: speed

### Where the time goes

| Model | Seconds per question | Time to first token | Output generation | Tool execution | Own waits and retries | Other |
|---|---|---|---|---|---|---|
| GPT-5.6 Luna | 22.5 s | 2.6 s | 14.6 s | 2.0 s | 0.1 s | 3.2 s |
| Suite 1: Item lore | 20.5 s | 2.6 s | 13.0 s | 2.0 s | 0.1 s | 2.9 s |
| Suite 2: Hazards | 24.5 s | 2.6 s | 16.2 s | 2.0 s | 0.1 s | 3.6 s |

*Seconds per question: the median of the per-question time, split into its parts in proportion to their means. Tool execution is excluded from the model's own time elsewhere in this document.*

The speed tables split the time per question into its components.

## Evidence: cost

### Where the cost goes

| Model | Cost per question | Uncached input | Cache reads | Cache writes | Visible output | Reasoning |
|---|---|---|---|---|---|---|
| GPT-5.6 Luna | $0.019 | $0.0050 | $0.0001 | $0.0000 | $0.0090 | $0.0050 |
| Suite 1: Item lore | $0.019 | $0.0050 | $0.0001 | $0.0000 | $0.0090 | $0.0050 |
| Suite 2: Hazards | $0.019 | $0.0050 | $0.0001 | $0.0000 | $0.0090 | $0.0050 |

*Mean cost per question by component, priced on this basis: As run.*

The cost tables split the cost per question into volume and price.

## Hypotheses

### Intelligence

| Hypothesis | Statement | Grade | Share | Settled by |
|---|---|---|---|---|
| WQ2.1: points lost in suite 2 by GPT-5.6 Luna | GPT-5.6 Luna's points are lost mostly in suite 2 (where its points go, not a comparison). | Suggestive (Low) | 100 % | run 1 |
| WQ3.1: critical errors of GPT-5.6 Luna | Confirmed critical errors cost GPT-5.6 Luna most of its points (where its points go, not a comparison). | Suggestive (Low) | 66 % | — |

*Ids: H0 is a null hypothesis, H one between two models, W one within a single model; Q, S and C mark intelligence, speed and cost. Appendix C gives each hypothesis' evidence for and against.*

*Null hypotheses (the difference is within sampling noise):* a paired result rejects it for within GPT-5.6 Luna's own questions.

The strongest hypothesis in the table: GPT-5.6 Luna's points are lost mostly in suite 2 (where its points go, not a comparison). Its grade and the evidence against it stand beside it.

### Speed

| Hypothesis | Statement | Grade | Share | Settled by |
|---|---|---|---|---|
| WS2.1: output generation of GPT-5.6 Luna | Most of GPT-5.6 Luna's time is output generation, driven by reasoning tokens (where its time goes, not a comparison). | Probable (Moderate) | 83 % (82–84 %) | run 2 |

The strongest hypothesis in the table: Most of GPT-5.6 Luna's time is output generation, driven by reasoning tokens (where its time goes, not a comparison). Its grade and the evidence against it stand beside it.

### Cost

| Hypothesis | Statement | Grade | Share | Settled by |
|---|---|---|---|---|
| WC1.1: output cost of GPT-5.6 Luna | Output (with reasoning) is GPT-5.6 Luna's largest cost component (where its cost goes, not a comparison). | Established (High) | 73 % | — |

The strongest hypothesis in the table: Output (with reasoning) is GPT-5.6 Luna's largest cost component (where its cost goes, not a comparison). Its grade and the evidence against it stand beside it.

### The writer's hypotheses

> [!WARNING]
> **Not tested by code.** Hypotheses the report writer added from the grader comments and excerpts. Code has not tested them, so their confidence is Low.
>
> - **Intelligence · Low** (GPT-5.6 Luna; locus suite): GPT-5.6 Luna may have lost points to a rubric that asks for more than the question states.
>   - *For:* The grader comment on Q1 names a missing point.
>   - *Against:* Other models answered the same question well.
>   - *Refuted if:* A rubric check finds every point stated in the question.

## Conclusions

| Axis | Status | Best hypothesis | Why |
|---|---|---|---|
| Intelligence | Not established | WQ2.1: points lost in suite 2 by GPT-5.6 Luna | The best hypothesis, WQ2.1, is graded Low: across suites failed: leaving the suite out moves the Overall Index by less than the interval half-width; panel agreement could not be assessed: the model was not graded by a panel, or a group has no score from both members. |
| Speed | Probable cause | WS2.1: output generation of GPT-5.6 Luna | WS2.1 is the best hypothesis, graded Moderate: its lead is not established, or the reasoning share is unknown. |
| Cost | Root cause established | WC1.1: output cost of GPT-5.6 Luna | WC1.1 is graded High and its null hypothesis is rejected. |

> [!TIP]
> **Conclusion.** Intelligence: Not established. Speed: Probable cause. Cost: Root cause established.

## What to run next

In the order to make them: the Overseer chat first (C), then the benchmark (B), then model selection (M). Appendix D gives each run's launcher settings.

| # | Run | Purpose | Models | Settles | Estimate |
|---|---|---|---|---|---|
| 1 | Control run (recommended) | B, the benchmark | Lyra Mini (high) and Zeta Prime (high) | WQ2.1 | about $0.480 and 28 minutes |
| 2 | Thinking-level pair (recommended) | M, model selection | GPT-5.6 Luna and Orion Max (medium) | WS2.1 | about $0.480 and 28 minutes |

### Checks

No check is proposed.

The runs are ordered chat first, then the benchmark, then model selection.

## Limitations and threats to validity

- The benchmark asks single-turn questions under one chat configuration: live chat carries the conversation before each question, and spoiler-free mode, web search and subagents are off, so a cause found here holds for that configuration only.
- It runs without the wiki text that live chat always gives the model before it answers, so a reading about retrieval or tool routing was measured under a condition live chat does not share.
- The hypotheses come from a fixed rule table over these runs; points at risk are upper bounds, and a decomposition rests on the questions both sides have figures for. The writer's own hypotheses are qualitative.
- Interval: Battery composite: item sampling (t) only. Below three complete rounds there is no reproducibility estimate
- The graders are AI models. Each grader's provider relation to the model under test is stated under Setup and method; a grader from the model's own provider may read it more favorably.
- Composite: the Overall Index weights the suites under the battery's scheme; another scheme or another set of suites gives another figure, and it is not comparable with a single suite's index.
- Detail: the writer was given the full text of at most a few questions per suite, each with one answer, and the other questions as one-line rows.
- Battery analysis: Fewer than three complete rounds: the interval covers item sampling only.
- No other model answered these questions, so the data cannot tell the model from the instrument on intelligence.
- No earlier battery run of another model ran this battery revision with a current, complete analysis in the same comparability class.

The benchmark asks single questions, so longer conversations are not measured.

## Appendices

- **A. Intelligence detail:** the compared models, configuration differences, failure profiles, grader findings, panel agreement, points at risk, the index difference by dimension, every paired test and the per-question matrix.
- **B. Speed and cost decompositions:** calls, latency, tokens and every component of each decomposed difference.
- **C. Hypothesis details:** each hypothesis' evidence for and against, grade reasons and robustness checks, and the null hypotheses.
- **D. Run settings:** every launcher setting of each run to make next.
- **E. Known facts:** what a test, a count, a decomposition or the configuration proves.
- **F. Reproducibility:** run ids, dates, hashes and the pricing basis.
- **G. Question details:** the questions this document cites, with their answers.
- **H. Glossary:** the statistical terms in plain words.

## Appendix A: Intelligence detail

### Key figures

- **Intelligence:** 80 / 100 (interval 77–83).
- **Speed:** median answer time 12.3 s.
- **Cost:** $0.036 per question.
- **Critical errors:** 1 of 4 answers.

### Battery profile

The Overall Index is the sum over the suites of each suite's weight times its Intelligence Index, under the Questions and difficulty scheme. It is not comparable with a single suite's Intelligence Index.

| Suite | Weight | Intelligence Index | 95 % interval | Contribution | Scored questions | Runs | Speed Index | Cost per run, graders included | Critical-error rate |
|---|---|---|---|---|---|---|---|---|---|
| S1 · Item lore | 60.0 % | 84 / 100 | 78–90 | 50.4 points | 2 of 2 questions | 2 runs | 84 / 100 | $0.081 | 0 % |
| S2 · Hazards | 40.0 % | 74 / 100 | — | 29.6 points | 2 of 2 questions | 2 runs | 80 / 100 | $0.063 | 25 % |

*A suite's cost per run is its mean over its member runs, graders included, at the prices stored with each run.*

- **Complete rounds:** 2 complete rounds
- **Standard deviation of the suite indices:** 7.1 points
- **Range of the suite indices:** 10.0 points
- **Pooled identity:** the Overall Index equals one difficulty-weighted index over every question of every suite
- **Critical-error rate:** 13 %
- **Speed Index:** 82 / 100
- **Member runs left out of the analysis:** 0 member runs

### Weighting sensitivity

- 80 / 100 under the Questions and difficulty weights (the declared scheme)
- 79 / 100 under the Equal per suite weights
- 79 / 100 under the Questions only weights

### Leave one suite out

- S1: 74 / 100 without Item lore, a change of -6.0 points
- S2: 84 / 100 without Hazards, a change of +4.0 points

### Results

### Dimensions

| Dimension | GPT-5.6 Luna |
|---|---|
| Accuracy | 84 |
| Completeness | 70 |
| Conciseness | 88 |
| Readability | 90 |

### Difficulty bands (assessed)

| Difficulty band | Questions | Authored questions |
|---|---|---|
| Simple | 1 | — |
| Intermediate | 2 | — |
| Advanced | 1 | — |

### Grader findings and panel agreement

- **Panel mean absolute difference:** 6.5 points
- **Intraclass correlation, ICC(A,1):** 0.82
- **Panel disagreements:** 1 of 4 answers
- **Panel member A alone:** 81 / 100
- **Panel member B alone:** 79 / 100
- **Reference reader (advisory, third provider):** 90 / 100
- **Reference reader's mean offset from the panel:** +16.8 points. Its scores do not count toward the score; its neutrality between the two panel families is an assumption.
- **Response-style conflict:** none

A battery report lists no synthesis findings; each member run's report has its own.

### Tool use

- **Tool calls per question:** 3.5
- **Source code share:** 57 %
- **Wiki share:** 29 %
- **Structured lookup share:** 14 %
- **Knowledge base share:** 0 %
- **Other tools share:** 0 %
- **Failed tool calls:** not available
- **Calls refused by the tool budget:** not available

*Recorded success or failure describes whether a tool call executed. It does not show that the query was well chosen, that the result was relevant, or that the corpus was current.*

*Per-call arguments and results are in each member run's Tool-call log until the retention sweep prunes them.*

### Points at risk

| Model | Answers without tools | Budget, truncation or output defects | Confirmed critical errors | Other low-scoring answers | Total | Median answer |
|---|---|---|---|---|---|---|
| GPT-5.6 Luna | 0.0 | 0.0 | 5.6 | 2.9 | 8.5 | 75 |
| Suite 1: Item lore | 0.0 | 0.0 | 0.0 | 0.0 | 0.0 | — |
| Suite 2: Hazards | 0.0 | 0.0 | 11.3 | 5.8 | 17.0 | — |

*Index points at risk: the points lost on the answers showing each signal, relative to the model's own median answer, each answer counted once under its first signal. They are an upper bound, not a causal estimate.*

### Per-question results

| Question | Topic | Assessed band | Authored | Mean score | Runs scored | Critical errors | Refuted answer sentences | Tool calls | Model time |
|---|---|---|---|---|---|---|---|---|---|
| S1-Q1 | An item question about gems | Simple | — | 90 | 2 | 0 | 0 | 2 | 8.1 s |
| S1-Q2 | — | Intermediate | — | 72 | 2 | 0 | 0 | 3 | 11.0 s |
| S2-Q1 | — | Intermediate | — | 25 | 2 | 1 | 1 | 5 | 15.2 s |
| S2-Q2 | — | Advanced | — | 87 | 2 | 0 | 0 | 4 | 13.4 s |

*Each question's mean score is over the member runs that scored it; its critical errors count those runs with a critical error.*

## Appendix B: Speed and cost decompositions

### Calls, latency and output

| Model | Model calls per answer | Time to first token per call, median | Decode rate | Reasoning share | Output tokens per question |
|---|---|---|---|---|---|
| GPT-5.6 Luna | 2 | — | — | — | 1,400 |

### Tokens per question

| Model | Uncached input | Cache reads | Cache writes | Visible output | Reasoning | Cache-read share |
|---|---|---|---|---|---|---|
| GPT-5.6 Luna | 4,000 | 1,000 | 0 | 900 | 500 | — |

### Speed and cost figures

| Measure | GPT-5.6 Luna |
|---|---|
| Median answer time | 12.3 s |
| 90th-percentile answer time | 15.0 s |
| Candidate cost per question | $0.036 |
| Candidate cost per battery pass | $0.144 |
| Input tokens per question | 18,250 |
| Output tokens per question | 1,140 |

## Appendix C: Hypothesis details

*Code fired each hypothesis on a pre-declared rule and graded it: High (quantified, established after adjustment, its null rejected, no open confound, every robustness check passed, two or more runs per suite on both sides), Moderate (exactly one of those conditions missed), Low (qualitative only, two or more conditions missed, or the null stands) or Not supported (refuted by the rule's own test). Each null hypothesis says that the difference is within sampling noise.*

### Intelligence

- **WQ2.1: points lost in suite 2 by GPT-5.6 Luna** (GPT-5.6 Luna; locus model / suite; Suggestive (Low))
  - *For:* suite 2 holds 100 % of GPT-5.6 Luna's points at risk.
  - *Against:* none recorded.
  - *Grade reasons:* see Robustness.
  - *Refuted if:* The contrast stands within noise, or the shares are even.
  - *Robustness:* Replicates passed (Every model has at least two runs per suite); Across suites failed (Leaving the suite out moves the Overall Index by less than the interval half-width); Panel agreement could not be assessed (The model was not graded by a panel, or a group has no score from both members).
- **WQ3.1: critical errors of GPT-5.6 Luna** (GPT-5.6 Luna; locus model; Suggestive (Low))
  - *For:* Confirmed critical errors hold 66 % of the points at risk (5.6 index points, upper bound).
  - *Against:* none recorded.
  - *Grade reasons:* the lead of the critical-error share over the next is not established.
  - *Refuted if:* The caps are unconfirmed.
  - *Robustness:* Replicates passed (Every model has at least two runs per suite); Leave one question out could not be assessed (Fewer than two questions put points at risk in this segment); Panel agreement could not be assessed (The model was not graded by a panel, or a group has no score from both members).

| Hypothesis | Pair | Paired result | Grade |
|---|---|---|---|
| H0-W.1 | within GPT-5.6 Luna | suite 2 against the rest: adjusted p 0.030. | Ruled out (Not supported) |

### Speed

- **WS2.1: output generation of GPT-5.6 Luna** (GPT-5.6 Luna; locus model; Probable (Moderate))
  - *For:* Output generation is 83 % (82–84 %) of model time per question.
  - *Against:* The reasoning tokens are not recorded separately.
  - *Grade reasons:* its lead is not established, or the reasoning share is unknown.
  - *Refuted if:* The reasoning share is under one half.
  - *Robustness:* Replicates passed (Every model has at least two runs per suite); Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most).

### Cost

- **WC1.1: output cost of GPT-5.6 Luna** (GPT-5.6 Luna; locus model; Established (High))
  - *For:* Visible output and reasoning are 73 % of GPT-5.6 Luna's cost per question.
  - *Against:* none recorded.
  - *Grade reasons:* every condition of the grade is met.
  - *Refuted if:* Another component is larger.
  - *Robustness:* Replicates passed (Every model has at least two runs per suite).

## Appendix D: Run settings

Each run's settings are the launcher's, copied from the analyzed runs unless its kind changes them; a setting the runs did not share is left for you to choose.

### Run 1: Control run (B, the benchmark, recommended)

- **Decides:** Separates the model from the instrument: once two controls, one from another provider, have run on the same questions under the same graders, the intelligence hypotheses are graded without the control cap.
- **Predicted comparability:** Model comparison: every must-match key agrees and only the model differs
- **Estimate:** about $0.480 and 28 minutes
- **Settles:** WQ2.1

| Launcher field | Setting |
|---|---|
| Models | Model batch |
| Models Under Test | Lyra Mini (high), Zeta Prime (high) |
| Run Target | Battery |
| Battery | Core knowledge |
| Scoring Profile | profile 1 |
| Response Style | Concise — production default |
| Source Code References | Allowed — answers cite source files and lines |
| Assessor | configuration 21 |
| Co-Assessor | none |
| Second Reader | none |
| Coverage | not shared by the analyzed runs: choose it |
| Claim Verifier | none |
| Report Writer | none |
| Runs per Suite | 2 |
| Model order | Randomized (recommended) |

- Without the subject: its run already exists and becomes the other side of the comparison.
- The estimate for a model without analyzed runs is the analyzed models' mean.

### Run 2: Thinking-level pair (M, model selection, recommended)

- **Decides:** Confirms WS2.1 if the decode share stays the largest and reasoning stays at least half the output; refutes it if the reasoning share is under one half.
- **Predicted comparability:** Model comparison: only the thinking level differs
- **Estimate:** about $0.480 and 28 minutes
- **Settles:** WS2.1

| Launcher field | Setting |
|---|---|
| Models | Model batch |
| Models Under Test | GPT-5.6 Luna, Orion Max (medium) |
| Run Target | Battery |
| Battery | Core knowledge |
| Scoring Profile | profile 1 |
| Response Style | Concise — production default |
| Source Code References | Allowed — answers cite source files and lines |
| Assessor | configuration 21 |
| Co-Assessor | none |
| Second Reader | none |
| Coverage | not shared by the analyzed runs: choose it |
| Claim Verifier | none |
| Report Writer | none |
| Runs per Suite | 2 |
| Model order | Randomized (recommended) |

- The estimate for a model without analyzed runs is the analyzed models' mean.

## Appendix E: Known facts

1. No earlier run of another model answered these questions under the same instrument.
2. GPT-5.6 Luna answered 2 of 8 scored questions without a tool call.
3. GPT-5.6 Luna has 1 critical error in 8 scored answers.
4. Output generation is 65 % of GPT-5.6 Luna's time per question.
5. Visible output is 47 % of GPT-5.6 Luna's cost per question.

## Appendix F: Reproducibility

- **Run IDs:** 12, 15, 21, 24
- **Run dates:** 2026-09-20
- **Model under test:** OpenAI gpt-5.6-luna, thinking level high
- **Grader models:** Panel member A: Google gemini-3.8-flash, thinking level medium; Panel member B: Anthropic claude-haiku-5; Reference reader: DeepSeek deepseek-v4; Claim verifier: Google gemini-3.8-flash, thinking level medium
- **Harness version:** 41
- **Scoring method version:** 12
- **Battery definition SHA-256 prefix:** `d41f0c9e2b7a`
- **Battery comparability class SHA-256 prefix:** `5e1d0c7b3a91`
- **Comparability signature:** `5e1d0c7b3a91f08c`
- **System prompt SHA-256 prefix:** `e9b3e9a7c4d1`
- **Tool guides SHA-256 prefix:** `f59d8b30a1c7`
- **Pricing basis:** Catalog prices on 2026-09-20 (price card dated 2026-09-01)

## Appendix G: Question details

*Only the questions given in detail are shown, each with one answer: from the run whose score was the median of its rounds. Every question is listed under Per-question results.*

### S1-Q1: An item question about gems

**Question:**

> What happens if I throw a gem at a co-aligned unicorn?

**Rubric:**

> - The unicorn catches the gem.
> - A valuable gem raises Luck; worthless glass does not.

#### Run 12

**Answer:**

> The unicorn catches it. A real gem of your alignment raises your Luck; glass does nothing.

**Graders:**

- **Panel member A (Gemini 3.8 Flash):** score 92. Accurate and brief.
  - Accuracy: Matches rubric.
- **Panel member B (Claude Haiku 5):** score 88. Correct.

**Claim verifier:**

- **Grader's statement — refuted (the verifier sided with the answer):** "Worthless glass angers the unicorn." — Glass is caught and returned without anger (dothrow.c).

## Appendix H: Glossary

- **95 % interval:** a range built so that, if the questions were sampled again and again, 95 of 100 such ranges would hold the true value. When two models' intervals overlap, the intervals alone do not say which is higher.
- **Paired test:** a comparison of two models question by question, on the questions both answered, so that how hard each question is counts the same for both.
- **Holm adjustment:** testing many pairs at once turns up some differences by chance. The Holm adjustment raises each p-value so that the whole family of tests keeps a 5 % chance of a false finding; a result is established when its adjusted p-value is below 0.05.
- **Null hypothesis:** the statement that a difference is within sampling noise. It stands until a paired test, or without one a bootstrap interval that excludes zero, rejects it; a null that stands is not proof that the two are equal.
- **Grade words:** Established (High) is measured, established after adjustment, robust and replicated; Probable (Moderate) misses one of those conditions; Suggestive (Low) is qualitative, misses two or more, or may be noise; Ruled out (Not supported) is refuted by its own rule's test.
- **Points at risk:** the index points a model lost on its answers below its own median answer, each answer counted once under its first signal (no tool call, a defect, a critical error). An upper bound of what each signal costs, not an estimate of its effect.
- **Decomposition share:** the part of a speed or cost difference one component accounts for; the parts sum exactly to the difference. A share can pass 100 % when two components pull in opposite directions: if the difference is 2 s, one component adding 3 s and another taking 1 s back are 150 % and −50 %. Over few questions or a small difference, a share's interval can be very wide.
- **Isolated miss:** a question a model scored 50 or less on while the other models' median was 75 or more.
- **Joint rank:** two models share a rank when their 95 % intervals overlap.

## Fact sheet

```json
{
  "battery": {
    "batteryRunId": 9,
    "memberRunCount": 4,
    "name": "Core knowledge",
    "revision": 2,
    "runsPerSuite": 2,
    "scheme": "Questions and difficulty",
    "suiteCount": 2,
    "suites": [
      {
        "name": "Item lore",
        "number": 1,
        "questionCount": 2,
        "suiteId": 21
      },
      {
        "name": "Hazards",
        "number": 2,
        "questionCount": 2,
        "suiteId": 22
      }
    ]
  },
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
      "available": false,
      "display": "not available",
      "key": "band.advanced.score",
      "unavailableReason": "A battery weights its suites, not its difficulty bands, so it states no pooled band score; each question's row carries its band."
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
      "available": false,
      "display": "not available",
      "key": "band.intermediate.score",
      "unavailableReason": "A battery weights its suites, not its difficulty bands, so it states no pooled band score; each question's row carries its band."
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
      "available": false,
      "display": "not available",
      "key": "band.simple.score",
      "unavailableReason": "A battery weights its suites, not its difficulty bands, so it states no pooled band score; each question's row carries its band."
    },
    {
      "available": true,
      "display": "Fewer than three complete rounds: the interval covers item sampling only.",
      "key": "battery.caveat.1"
    },
    {
      "available": true,
      "display": "5e1d0c7b3a91",
      "key": "battery.classSha256"
    },
    {
      "available": true,
      "display": "13 %",
      "key": "battery.criticalErrorRate",
      "value": 0.125
    },
    {
      "available": true,
      "display": "d41f0c9e2b7a",
      "key": "battery.definitionSha256"
    },
    {
      "available": true,
      "display": "0 member runs",
      "key": "battery.excludedMembers",
      "value": 0
    },
    {
      "available": true,
      "display": "4 runs",
      "key": "battery.memberRuns",
      "value": 4
    },
    {
      "available": true,
      "display": "Core knowledge",
      "key": "battery.name"
    },
    {
      "available": true,
      "display": "the Overall Index equals one difficulty-weighted index over every question of every suite",
      "key": "battery.pooledIdentity",
      "value": true
    },
    {
      "available": true,
      "display": "2",
      "key": "battery.revision",
      "value": 2
    },
    {
      "available": true,
      "display": "2 complete rounds",
      "key": "battery.rounds",
      "value": 2
    },
    {
      "available": true,
      "display": "2 runs",
      "key": "battery.runsPerSuite",
      "value": 2
    },
    {
      "available": true,
      "display": "Questions and difficulty",
      "key": "battery.scheme"
    },
    {
      "available": true,
      "display": "82 / 100",
      "key": "battery.speedIndex",
      "value": 82
    },
    {
      "available": true,
      "display": "2 suites",
      "key": "battery.suiteCount",
      "value": 2
    },
    {
      "available": true,
      "display": "10.0 points",
      "key": "battery.suiteIndexRange",
      "value": 10
    },
    {
      "available": true,
      "display": "7.1 points",
      "key": "battery.suiteIndexSd",
      "value": 7.07
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
      "display": "5e1d0c7b3a91f08c",
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
      "display": "74 / 100 without Item lore, a change of -6.0 points",
      "key": "loo.1",
      "value": 74
    },
    {
      "available": true,
      "display": "84 / 100 without Hazards, a change of +4.0 points",
      "key": "loo.2",
      "value": 84
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
      "display": "Battery composite: item sampling (t) only. Below three complete rounds there is no reproducibility estimate",
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
      "display": "0.0 index points at risk (upper bound, not a causal estimate)",
      "key": "rca.atRisk.subject.budgetOrDefect",
      "value": 0
    },
    {
      "available": true,
      "display": "5.6 index points at risk (upper bound, not a causal estimate)",
      "key": "rca.atRisk.subject.criticalError",
      "value": 5.625
    },
    {
      "available": true,
      "display": "0.0 index points at risk (upper bound, not a causal estimate)",
      "key": "rca.atRisk.subject.noTool",
      "value": 0
    },
    {
      "available": true,
      "display": "2.9 index points at risk (upper bound, not a causal estimate)",
      "key": "rca.atRisk.subject.otherLow",
      "value": 2.875
    },
    {
      "available": true,
      "display": "8.5 index points at risk against the model's median answer of 75 (upper bound, not a causal estimate)",
      "key": "rca.atRisk.subject.total",
      "value": 8.5
    },
    {
      "available": true,
      "display": "WC1.1",
      "key": "rca.axis.cost.best",
      "value": "WC1.1"
    },
    {
      "available": true,
      "display": "Root cause established",
      "key": "rca.axis.cost.status",
      "value": "RootCauseEstablished"
    },
    {
      "available": true,
      "display": "WQ2.1",
      "key": "rca.axis.intelligence.best",
      "value": "WQ2.1"
    },
    {
      "available": true,
      "display": "Not established",
      "key": "rca.axis.intelligence.status",
      "value": "NotEstablished"
    },
    {
      "available": true,
      "display": "WS2.1",
      "key": "rca.axis.speed.best",
      "value": "WS2.1"
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
      "display": "suite 2 against the rest: adjusted p 0.030.",
      "key": "rca.hypothesis.H0-W.1.against"
    },
    {
      "available": true,
      "display": "Intelligence",
      "key": "rca.hypothesis.H0-W.1.axis"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-W.1.for"
    },
    {
      "available": true,
      "display": "Not supported",
      "key": "rca.hypothesis.H0-W.1.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "the rule's own test refutes it: suite 2 against the rest: adjusted p 0.030.",
      "key": "rca.hypothesis.H0-W.1.gradeReasons"
    },
    {
      "available": true,
      "display": "no contrast within GPT-5.6 Luna's own questions",
      "key": "rca.hypothesis.H0-W.1.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-W.1.locus"
    },
    {
      "available": true,
      "display": "GPT-5.6 Luna",
      "key": "rca.hypothesis.H0-W.1.models"
    },
    {
      "available": true,
      "display": "A Mann–Whitney contrast is established after the Holm adjustment.",
      "key": "rca.hypothesis.H0-W.1.refutedIf"
    },
    {
      "available": true,
      "display": "The contrasts between groups of GPT-5.6 Luna's own questions are within sampling noise.",
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
      "display": "Visible output and reasoning are 73 % of GPT-5.6 Luna's cost per question.",
      "key": "rca.hypothesis.WC1.1.for"
    },
    {
      "available": true,
      "display": "High",
      "key": "rca.hypothesis.WC1.1.grade",
      "value": "High"
    },
    {
      "available": true,
      "display": "every condition of the grade is met",
      "key": "rca.hypothesis.WC1.1.gradeReasons"
    },
    {
      "available": true,
      "display": "output cost of GPT-5.6 Luna",
      "key": "rca.hypothesis.WC1.1.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.WC1.1.locus"
    },
    {
      "available": true,
      "display": "GPT-5.6 Luna",
      "key": "rca.hypothesis.WC1.1.models"
    },
    {
      "available": true,
      "display": "Another component is larger.",
      "key": "rca.hypothesis.WC1.1.refutedIf"
    },
    {
      "available": true,
      "display": "73 %",
      "key": "rca.hypothesis.WC1.1.share",
      "value": 0.7329842931937172
    },
    {
      "available": true,
      "display": "Output (with reasoning) is GPT-5.6 Luna's largest cost component (where its cost goes, not a comparison).",
      "key": "rca.hypothesis.WC1.1.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.WQ2.1.against"
    },
    {
      "available": true,
      "display": "Intelligence",
      "key": "rca.hypothesis.WQ2.1.axis"
    },
    {
      "available": true,
      "display": "suite 2 holds 100 % of GPT-5.6 Luna's points at risk.",
      "key": "rca.hypothesis.WQ2.1.for"
    },
    {
      "available": true,
      "display": "Low",
      "key": "rca.hypothesis.WQ2.1.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "across suites failed: leaving the suite out moves the Overall Index by less than the interval half-width.; panel agreement could not be assessed: the model was not graded by a panel, or a group has no score from both members.",
      "key": "rca.hypothesis.WQ2.1.gradeReasons"
    },
    {
      "available": true,
      "display": "points lost in suite 2 by GPT-5.6 Luna",
      "key": "rca.hypothesis.WQ2.1.label"
    },
    {
      "available": true,
      "display": "model / suite",
      "key": "rca.hypothesis.WQ2.1.locus"
    },
    {
      "available": true,
      "display": "GPT-5.6 Luna",
      "key": "rca.hypothesis.WQ2.1.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.WQ2.1.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "The contrast stands within noise, or the shares are even.",
      "key": "rca.hypothesis.WQ2.1.refutedIf"
    },
    {
      "available": true,
      "display": "100 %",
      "key": "rca.hypothesis.WQ2.1.share",
      "value": 1
    },
    {
      "available": true,
      "display": "GPT-5.6 Luna's points are lost mostly in suite 2 (where its points go, not a comparison).",
      "key": "rca.hypothesis.WQ2.1.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.WQ3.1.against"
    },
    {
      "available": true,
      "display": "Intelligence",
      "key": "rca.hypothesis.WQ3.1.axis"
    },
    {
      "available": true,
      "display": "Confirmed critical errors hold 66 % of the points at risk (5.6 index points, upper bound).",
      "key": "rca.hypothesis.WQ3.1.for"
    },
    {
      "available": true,
      "display": "Low",
      "key": "rca.hypothesis.WQ3.1.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "leave one question out could not be assessed: fewer than two questions put points at risk in this segment.; panel agreement could not be assessed: the model was not graded by a panel, or a group has no score from both members.; the lead of the critical-error share over the next is not established",
      "key": "rca.hypothesis.WQ3.1.gradeReasons"
    },
    {
      "available": true,
      "display": "critical errors of GPT-5.6 Luna",
      "key": "rca.hypothesis.WQ3.1.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.WQ3.1.locus"
    },
    {
      "available": true,
      "display": "GPT-5.6 Luna",
      "key": "rca.hypothesis.WQ3.1.models"
    },
    {
      "available": true,
      "display": "The caps are unconfirmed.",
      "key": "rca.hypothesis.WQ3.1.refutedIf"
    },
    {
      "available": true,
      "display": "66 %",
      "key": "rca.hypothesis.WQ3.1.share",
      "value": 0.6617647058823529
    },
    {
      "available": true,
      "display": "Confirmed critical errors cost GPT-5.6 Luna most of its points (where its points go, not a comparison).",
      "key": "rca.hypothesis.WQ3.1.statement"
    },
    {
      "available": true,
      "display": "The reasoning tokens are not recorded separately.",
      "key": "rca.hypothesis.WS2.1.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.WS2.1.axis"
    },
    {
      "available": true,
      "display": "Output generation is 83 % (82–84 %) of model time per question.",
      "key": "rca.hypothesis.WS2.1.for"
    },
    {
      "available": true,
      "display": "Moderate",
      "key": "rca.hypothesis.WS2.1.grade",
      "value": "Moderate"
    },
    {
      "available": true,
      "display": "its lead is not established, or the reasoning share is unknown",
      "key": "rca.hypothesis.WS2.1.gradeReasons"
    },
    {
      "available": true,
      "display": "82–84 %",
      "key": "rca.hypothesis.WS2.1.interval"
    },
    {
      "available": true,
      "display": "output generation of GPT-5.6 Luna",
      "key": "rca.hypothesis.WS2.1.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.WS2.1.locus"
    },
    {
      "available": true,
      "display": "GPT-5.6 Luna",
      "key": "rca.hypothesis.WS2.1.models"
    },
    {
      "available": true,
      "display": "next run 2",
      "key": "rca.hypothesis.WS2.1.nextRun",
      "value": 2
    },
    {
      "available": true,
      "display": "The reasoning share is under one half.",
      "key": "rca.hypothesis.WS2.1.refutedIf"
    },
    {
      "available": true,
      "display": "83 %",
      "key": "rca.hypothesis.WS2.1.share",
      "value": 0.8295454545454546
    },
    {
      "available": true,
      "display": "Most of GPT-5.6 Luna's time is output generation, driven by reasoning tokens (where its time goes, not a comparison).",
      "key": "rca.hypothesis.WS2.1.statement"
    },
    {
      "available": true,
      "display": "No earlier run of another model answered these questions under the same instrument.",
      "key": "rca.known.1"
    },
    {
      "available": true,
      "display": "GPT-5.6 Luna answered 2 of 8 scored questions without a tool call.",
      "key": "rca.known.2"
    },
    {
      "available": true,
      "display": "GPT-5.6 Luna has 1 critical error in 8 scored answers.",
      "key": "rca.known.3"
    },
    {
      "available": true,
      "display": "Output generation is 65 % of GPT-5.6 Luna's time per question.",
      "key": "rca.known.4"
    },
    {
      "available": true,
      "display": "Visible output is 47 % of GPT-5.6 Luna's cost per question.",
      "key": "rca.known.5"
    },
    {
      "available": true,
      "display": "As run",
      "key": "rca.pricingBasis"
    },
    {
      "available": true,
      "display": "1 critical error (S2-Q3)",
      "key": "rca.profile.subject.flag.1"
    },
    {
      "available": true,
      "display": "1 critical error (S2-Q3)",
      "key": "rca.profile.subject.flags",
      "value": 1
    },
    {
      "available": true,
      "display": "confirmed critical errors: 5.6 index points on 1 question (S2-Q3)",
      "key": "rca.profile.subject.lostPointsOn"
    },
    {
      "available": true,
      "display": "Completeness, 2.0 points below Accuracy",
      "key": "rca.profile.subject.weakestDimension"
    },
    {
      "available": true,
      "display": "Run 1, a control run of Lyra Mini (high) and Zeta Prime (high) (about $0.480 and 28 minutes)",
      "key": "rca.run.1.summary"
    },
    {
      "available": true,
      "display": "Run 2, a thinking-level pair of GPT-5.6 Luna and Orion Max (medium) (about $0.480 and 28 minutes)",
      "key": "rca.run.2.summary"
    },
    {
      "available": true,
      "display": "no harness or suite hypothesis holds model-selection runs back",
      "key": "rca.systemFirst",
      "value": false
    },
    {
      "available": true,
      "display": "1 %",
      "key": "rca.within.subject.cost.cacheRead.share",
      "value": 0.005235602094240839
    },
    {
      "available": true,
      "display": "0 %",
      "key": "rca.within.subject.cost.cacheWrite.share",
      "value": 0
    },
    {
      "available": true,
      "display": "47 %",
      "key": "rca.within.subject.cost.output.share",
      "value": 0.4712041884816754
    },
    {
      "available": true,
      "display": "26 %",
      "key": "rca.within.subject.cost.reasoning.share",
      "value": 0.2617801047120419
    },
    {
      "available": true,
      "display": "$0.019 per question, mean",
      "key": "rca.within.subject.cost.total",
      "value": 0.0191
    },
    {
      "available": true,
      "display": "26 %",
      "key": "rca.within.subject.cost.uncachedInput.share",
      "value": 0.2617801047120419
    },
    {
      "available": true,
      "display": "3.2 s per question, mean",
      "key": "rca.within.subject.time.other.seconds",
      "value": 3.225
    },
    {
      "available": true,
      "display": "14 %",
      "key": "rca.within.subject.time.other.share",
      "value": 0.1433333333333333
    },
    {
      "available": true,
      "display": "14.6 s per question, mean",
      "key": "rca.within.subject.time.outputGeneration.seconds",
      "value": 14.6
    },
    {
      "available": true,
      "display": "65 %",
      "key": "rca.within.subject.time.outputGeneration.share",
      "value": 0.6488888888888887
    },
    {
      "available": true,
      "display": "0.1 s per question, mean",
      "key": "rca.within.subject.time.ownWaits.seconds",
      "value": 0.1
    },
    {
      "available": true,
      "display": "0 %",
      "key": "rca.within.subject.time.ownWaits.share",
      "value": 0.004444444444444444
    },
    {
      "available": true,
      "display": "2.0 s per question, mean",
      "key": "rca.within.subject.time.toolExecution.seconds",
      "value": 2
    },
    {
      "available": true,
      "display": "9 %",
      "key": "rca.within.subject.time.toolExecution.share",
      "value": 0.08888888888888888
    },
    {
      "available": true,
      "display": "22.5 s per question, mean",
      "key": "rca.within.subject.time.total",
      "value": 22.500000000000004
    },
    {
      "available": true,
      "display": "2.6 s per question, mean",
      "key": "rca.within.subject.time.ttft.seconds",
      "value": 2.575
    },
    {
      "available": true,
      "display": "11 %",
      "key": "rca.within.subject.time.ttft.share",
      "value": 0.11444444444444443
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
      "display": "12, 15, 21, 24",
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
      "display": "80 / 100 under the Questions and difficulty weights (the declared scheme)",
      "key": "sensitivity.difficultyMass",
      "value": 80
    },
    {
      "available": true,
      "display": "79 / 100 under the Equal per suite weights",
      "key": "sensitivity.equal",
      "value": 79
    },
    {
      "available": true,
      "display": "79 / 100 under the Questions only weights",
      "key": "sensitivity.itemCount",
      "value": 79
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
      "display": "50.4 points",
      "key": "suite.1.contribution",
      "value": 50.4
    },
    {
      "available": true,
      "display": "$0.081",
      "key": "suite.1.costPerRun",
      "value": 0.081
    },
    {
      "available": true,
      "display": "0 %",
      "key": "suite.1.criticalErrorRate",
      "value": 0
    },
    {
      "available": true,
      "display": "84 / 100",
      "key": "suite.1.index",
      "value": 84
    },
    {
      "available": true,
      "display": "78–90",
      "key": "suite.1.interval"
    },
    {
      "available": true,
      "display": "Item lore",
      "key": "suite.1.name"
    },
    {
      "available": true,
      "display": "2 runs",
      "key": "suite.1.runs",
      "value": 2
    },
    {
      "available": true,
      "display": "2 of 2 questions",
      "key": "suite.1.scoredItems",
      "value": 2
    },
    {
      "available": true,
      "display": "84 / 100",
      "key": "suite.1.speedIndex",
      "value": 84
    },
    {
      "available": true,
      "display": "60.0 %",
      "key": "suite.1.weight",
      "value": 0.6
    },
    {
      "available": true,
      "display": "29.6 points",
      "key": "suite.2.contribution",
      "value": 29.6
    },
    {
      "available": true,
      "display": "$0.063",
      "key": "suite.2.costPerRun",
      "value": 0.063
    },
    {
      "available": true,
      "display": "25 %",
      "key": "suite.2.criticalErrorRate",
      "value": 0.25
    },
    {
      "available": true,
      "display": "74 / 100",
      "key": "suite.2.index",
      "value": 74
    },
    {
      "available": false,
      "display": "not available",
      "key": "suite.2.interval",
      "unavailableReason": "No interval could be computed for this suite."
    },
    {
      "available": true,
      "display": "Hazards",
      "key": "suite.2.name"
    },
    {
      "available": true,
      "display": "2 runs",
      "key": "suite.2.runs",
      "value": 2
    },
    {
      "available": true,
      "display": "2 of 2 questions",
      "key": "suite.2.scoredItems",
      "value": 2
    },
    {
      "available": true,
      "display": "80 / 100",
      "key": "suite.2.speedIndex",
      "value": 80
    },
    {
      "available": true,
      "display": "40.0 %",
      "key": "suite.2.weight",
      "value": 0.4
    },
    {
      "available": false,
      "display": "not available",
      "key": "suite.name",
      "unavailableReason": "A battery result spans several suites; each suite's name is its suite.<n>.name fact."
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
      "available": false,
      "display": "not available",
      "key": "tools.failed",
      "unavailableReason": "A battery report does not load per-call tool rows; each member run's Tool-call log has them."
    },
    {
      "available": false,
      "display": "not available",
      "key": "tools.refusedByBudget",
      "unavailableReason": "A battery report does not load per-call tool rows; each member run's Tool-call log has them."
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
    "Core knowledge",
    "DeepSeek",
    "DeepSeek V4",
    "GPT-5.6 Luna",
    "Gemini 3.8 Flash",
    "Google",
    "Hazards",
    "Item lore",
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
      "criticalErrorCount": 0,
      "detailed": true,
      "itemRevisionUsed": 2,
      "modelTimeMs": 8100,
      "number": 1,
      "orderIndex": 1,
      "peerCount": 0,
      "peersAbove": 0,
      "questionKey": "101",
      "reference": "S1-Q1",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 2,
      "score": 90,
      "suite": 1,
      "toolCalls": 2
    },
    {
      "band": "Intermediate",
      "criticalError": false,
      "criticalErrorCount": 0,
      "detailed": false,
      "itemRevisionUsed": 1,
      "modelTimeMs": 11000,
      "number": 2,
      "orderIndex": 2,
      "peerCount": 0,
      "peersAbove": 0,
      "questionKey": "102",
      "reference": "S1-Q2",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 2,
      "score": 72,
      "suite": 1,
      "toolCalls": 3
    },
    {
      "band": "Intermediate",
      "criticalError": true,
      "criticalErrorCount": 1,
      "detailed": true,
      "itemRevisionUsed": 1,
      "modelTimeMs": 15200,
      "number": 3,
      "orderIndex": 3,
      "peerCount": 0,
      "peersAbove": 0,
      "questionKey": "103",
      "reference": "S2-Q1",
      "refutedAnswerSentences": 1,
      "refutedClaims": 1,
      "runCount": 2,
      "score": 25,
      "suite": 2,
      "toolCalls": 5
    },
    {
      "band": "Advanced",
      "criticalError": false,
      "criticalErrorCount": 0,
      "detailed": false,
      "itemRevisionUsed": 3,
      "modelTimeMs": 13400,
      "number": 4,
      "orderIndex": 4,
      "peerCount": 0,
      "peersAbove": 0,
      "questionKey": "104",
      "reference": "S2-Q2",
      "refutedAnswerSentences": 0,
      "refutedClaims": 0,
      "runCount": 2,
      "score": 87,
      "suite": 2,
      "toolCalls": 4
    }
  ],
  "rootCause": {
    "axes": [
      {
        "axis": "intelligence",
        "bestHypothesis": "WQ2.1",
        "reason": "The best hypothesis, WQ2.1, is graded Low: across suites failed: leaving the suite out moves the Overall Index by less than the interval half-width; panel agreement could not be assessed: the model was not graded by a panel, or a group has no score from both members.",
        "status": "NotEstablished"
      },
      {
        "axis": "speed",
        "bestHypothesis": "WS2.1",
        "reason": "WS2.1 is the best hypothesis, graded Moderate: its lead is not established, or the reasoning share is unknown.",
        "status": "ProbableCause"
      },
      {
        "axis": "cost",
        "bestHypothesis": "WC1.1",
        "reason": "WC1.1 is graded High and its null hypothesis is rejected.",
        "status": "RootCauseEstablished"
      }
    ],
    "checks": [],
    "computedAtUtc": "2026-09-28T10:42:00Z",
    "controlItems": [],
    "controlLimit": "Low",
    "controlLimitReason": "no other model answered these questions",
    "controls": [],
    "costPairs": [],
    "costRows": [
      {
        "kind": "model",
        "label": "GPT-5.6 Luna",
        "segments": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.005
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.00010000000000000002
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.009
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.005
          }
        ],
        "token": "subject",
        "total": 0.0191,
        "unit": "usd"
      },
      {
        "kind": "suite",
        "label": "Suite 1: Item lore",
        "segments": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.005
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.0001
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.009
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.005
          }
        ],
        "total": 0.0191,
        "unit": "usd"
      },
      {
        "kind": "suite",
        "label": "Suite 2: Hazards",
        "segments": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.005
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.0001
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.009
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.005
          }
        ],
        "total": 0.0191,
        "unit": "usd"
      }
    ],
    "engineVersion": 3,
    "evidence": [
      {
        "atRisk": [
          {
            "key": "noTool",
            "label": "answers without tools",
            "value": 0
          },
          {
            "key": "budgetOrDefect",
            "label": "budget, truncation or output defects",
            "value": 0
          },
          {
            "key": "criticalError",
            "label": "confirmed critical errors",
            "value": 5.625
          },
          {
            "key": "otherLow",
            "label": "other low-scoring answers",
            "value": 2.875
          }
        ],
        "budgetOrDefectAnswers": 0,
        "callsPerAnswer": 2,
        "cost": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.005
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.00010000000000000002
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.009
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.005
          }
        ],
        "criticalAnswers": 1,
        "label": "GPT-5.6 Luna",
        "medianQuality": 75,
        "modelTimeMs": 17600,
        "noToolAnswers": 2,
        "outputTokens": 1400,
        "scoredAnswers": 8,
        "telemetryAnswers": 0,
        "time": [
          {
            "key": "ttft",
            "label": "time to first token",
            "value": 2.575
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 14.6
          },
          {
            "key": "toolExecution",
            "label": "tool execution",
            "value": 2
          },
          {
            "key": "ownWaits",
            "label": "own waits and retries",
            "value": 0.1
          },
          {
            "key": "other",
            "label": "other",
            "value": 3.225
          }
        ],
        "token": "subject",
        "tokens": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 4000
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
            "value": 900
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 500
          }
        ]
      }
    ],
    "hypotheses": [
      {
        "against": [
          "suite 2 against the rest: adjusted p 0.030."
        ],
        "axis": "intelligence",
        "confounds": [],
        "factKeys": [],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "the rule's own test refutes it: suite 2 against the rest: adjusted p 0.030."
        ],
        "id": "H0-W",
        "key": "H0-W.1",
        "kind": "null",
        "label": "no contrast within GPT-5.6 Luna's own questions",
        "loci": [],
        "locus": "—",
        "models": [
          "subject"
        ],
        "modelsText": "GPT-5.6 Luna",
        "number": 1,
        "refutedIf": "A Mann–Whitney contrast is established after the Holm adjustment.",
        "robustness": [],
        "shortStatement": "Noise within GPT-5.6 Luna",
        "statement": "The contrasts between groups of GPT-5.6 Luna's own questions are within sampling noise."
      },
      {
        "against": [],
        "axis": "intelligence",
        "confounds": [],
        "explains": 8.5,
        "factKeys": [],
        "for": [
          "suite 2 holds 100 % of GPT-5.6 Luna's points at risk."
        ],
        "grade": "Low",
        "gradeReasons": [
          "across suites failed: leaving the suite out moves the Overall Index by less than the interval half-width.",
          "panel agreement could not be assessed: the model was not graded by a panel, or a group has no score from both members."
        ],
        "id": "WQ2",
        "key": "WQ2.1",
        "kind": "within",
        "label": "points lost in suite 2 by GPT-5.6 Luna",
        "loci": [
          "model",
          "suite"
        ],
        "locus": "model / suite",
        "models": [
          "subject"
        ],
        "modelsText": "GPT-5.6 Luna",
        "nextRunNumber": 1,
        "number": 1,
        "refutedIf": "The contrast stands within noise, or the shares are even.",
        "robustness": [
          {
            "detail": "Every model has at least two runs per suite.",
            "name": "Replicates",
            "status": "Passed"
          },
          {
            "detail": "Leaving the suite out moves the Overall Index by less than the interval half-width.",
            "name": "Across suites",
            "status": "Failed"
          },
          {
            "detail": "The model was not graded by a panel, or a group has no score from both members.",
            "name": "Panel agreement",
            "status": "NotAssessable"
          }
        ],
        "share": 1,
        "shortStatement": "Lost in suite 2",
        "statement": "GPT-5.6 Luna's points are lost mostly in suite 2 (where its points go, not a comparison)."
      },
      {
        "against": [],
        "axis": "intelligence",
        "confounds": [],
        "explains": 5.625,
        "factKeys": [
          "outcome.confirmedCriticalRate"
        ],
        "for": [
          "Confirmed critical errors hold 66 % of the points at risk (5.6 index points, upper bound)."
        ],
        "grade": "Low",
        "gradeReasons": [
          "leave one question out could not be assessed: fewer than two questions put points at risk in this segment.",
          "panel agreement could not be assessed: the model was not graded by a panel, or a group has no score from both members.",
          "the lead of the critical-error share over the next is not established"
        ],
        "id": "WQ3",
        "key": "WQ3.1",
        "kind": "within",
        "label": "critical errors of GPT-5.6 Luna",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "subject"
        ],
        "modelsText": "GPT-5.6 Luna",
        "number": 1,
        "refutedIf": "The caps are unconfirmed.",
        "robustness": [
          {
            "detail": "Every model has at least two runs per suite.",
            "name": "Replicates",
            "status": "Passed"
          },
          {
            "detail": "Fewer than two questions put points at risk in this segment.",
            "name": "Leave one question out",
            "status": "NotAssessable"
          },
          {
            "detail": "The model was not graded by a panel, or a group has no score from both members.",
            "name": "Panel agreement",
            "status": "NotAssessable"
          }
        ],
        "share": 0.6617647058823529,
        "shortStatement": "Critical errors: GPT-5.6 Luna",
        "statement": "Confirmed critical errors cost GPT-5.6 Luna most of its points (where its points go, not a comparison)."
      },
      {
        "against": [
          "The reasoning tokens are not recorded separately."
        ],
        "axis": "speed",
        "confounds": [],
        "explains": 14600,
        "factKeys": [
          "rca.within.subject.time.outputGeneration.share",
          "tokens.reasoningShare"
        ],
        "for": [
          "Output generation is 83 % (82–84 %) of model time per question."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "its lead is not established, or the reasoning share is unknown"
        ],
        "id": "WS2",
        "key": "WS2.1",
        "kind": "within",
        "label": "output generation of GPT-5.6 Luna",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "subject"
        ],
        "modelsText": "GPT-5.6 Luna",
        "nextRunNumber": 2,
        "number": 1,
        "refutedIf": "The reasoning share is under one half.",
        "robustness": [
          {
            "detail": "Every model has at least two runs per suite.",
            "name": "Replicates",
            "status": "Passed"
          },
          {
            "detail": "The deciding share keeps its sign and stays the largest without the question that contributes most.",
            "name": "Leave one question out",
            "status": "Passed"
          }
        ],
        "share": 0.8295454545454546,
        "shareLower": 0.8159509202453987,
        "shareUpper": 0.8404255319148937,
        "shortStatement": "Output generation: GPT-5.6 Luna",
        "statement": "Most of GPT-5.6 Luna's time is output generation, driven by reasoning tokens (where its time goes, not a comparison)."
      },
      {
        "against": [],
        "axis": "cost",
        "confounds": [],
        "explains": 0.014,
        "factKeys": [
          "cost.component.output.perQuestion",
          "cost.component.reasoning.perQuestion"
        ],
        "for": [
          "Visible output and reasoning are 73 % of GPT-5.6 Luna's cost per question."
        ],
        "grade": "High",
        "gradeReasons": [],
        "id": "WC1",
        "key": "WC1.1",
        "kind": "within",
        "label": "output cost of GPT-5.6 Luna",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "subject"
        ],
        "modelsText": "GPT-5.6 Luna",
        "number": 1,
        "refutedIf": "Another component is larger.",
        "robustness": [
          {
            "detail": "Every model has at least two runs per suite.",
            "name": "Replicates",
            "status": "Passed"
          }
        ],
        "share": 0.7329842931937172,
        "shortStatement": "Output cost: GPT-5.6 Luna",
        "statement": "Output (with reasoning) is GPT-5.6 Luna's largest cost component (where its cost goes, not a comparison)."
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
        "text": "GPT-5.6 Luna answered 2 of 8 scored questions without a tool call."
      },
      {
        "factKeys": [
          "errors.critical"
        ],
        "kind": "count",
        "number": 3,
        "text": "GPT-5.6 Luna has 1 critical error in 8 scored answers."
      },
      {
        "factKeys": [
          "rca.within.subject.time.outputGeneration.share"
        ],
        "kind": "decomposition",
        "number": 4,
        "text": "Output generation is 65 % of GPT-5.6 Luna's time per question."
      },
      {
        "factKeys": [
          "rca.within.subject.cost.output.share"
        ],
        "kind": "decomposition",
        "number": 5,
        "text": "Visible output is 47 % of GPT-5.6 Luna's cost per question."
      }
    ],
    "models": [
      {
        "entryKey": "battery:9",
        "isReference": false,
        "isSubject": true,
        "label": "GPT-5.6 Luna",
        "modelId": "orion-max",
        "provider": "Northwind",
        "runIds": [
          12,
          15,
          21,
          24
        ],
        "thinkingLevel": "high",
        "token": "subject"
      }
    ],
    "notes": [
      "No earlier battery run of another model ran this battery revision with a current, complete analysis in the same comparability class."
    ],
    "origin": "BatteryCompletion",
    "pointsAtRisk": [
      {
        "kind": "model",
        "label": "GPT-5.6 Luna",
        "segments": [
          {
            "key": "noTool",
            "label": "answers without tools",
            "value": 0
          },
          {
            "key": "budgetOrDefect",
            "label": "budget, truncation or output defects",
            "value": 0
          },
          {
            "key": "criticalError",
            "label": "confirmed critical errors",
            "value": 5.625
          },
          {
            "key": "otherLow",
            "label": "other low-scoring answers",
            "value": 2.875
          }
        ],
        "token": "subject",
        "total": 8.5,
        "unit": "points"
      },
      {
        "kind": "suite",
        "label": "Suite 1: Item lore",
        "segments": [
          {
            "key": "noTool",
            "label": "answers without tools",
            "value": 0
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
            "value": 0
          }
        ],
        "total": 0,
        "unit": "points"
      },
      {
        "kind": "suite",
        "label": "Suite 2: Hazards",
        "segments": [
          {
            "key": "noTool",
            "label": "answers without tools",
            "value": 0
          },
          {
            "key": "budgetOrDefect",
            "label": "budget, truncation or output defects",
            "value": 0
          },
          {
            "key": "criticalError",
            "label": "confirmed critical errors",
            "value": 11.25
          },
          {
            "key": "otherLow",
            "label": "other low-scoring answers",
            "value": 5.75
          }
        ],
        "total": 17,
        "unit": "points"
      }
    ],
    "pricingBasis": "As run",
    "pricingBasisKind": "AsRun",
    "profiles": [
      {
        "dimensions": [
          80,
          78,
          82,
          84
        ],
        "flags": [
          {
            "kind": "criticalErrors",
            "questions": [
              "S2-Q3"
            ],
            "short": "1 critical error",
            "text": "1 critical error (S2-Q3)"
          }
        ],
        "qualityIndex": 80,
        "strengths": [],
        "token": "subject",
        "weaknesses": [
          {
            "kind": "lostPointsOn",
            "questions": [
              "S2-Q3"
            ],
            "short": "confirmed critical errors, 5.6 points",
            "text": "confirmed critical errors: 5.6 index points on 1 question (S2-Q3)"
          },
          {
            "kind": "weakestDimension",
            "questions": [],
            "short": "Completeness 2.0 below Accuracy",
            "text": "Completeness, 2.0 points below Accuracy"
          }
        ]
      }
    ],
    "qualitySplits": [],
    "questions": [],
    "recommendedRuns": [
      {
        "afterSystemHypotheses": false,
        "allowSourceCodeReferences": true,
        "assessorModelConfigurationId": 21,
        "batteryId": 3,
        "batteryName": "Core knowledge",
        "decides": "Separates the model from the instrument: once two controls, one from another provider, have run on the same questions under the same graders, the intelligence hypotheses are graded without the control cap.",
        "estimatedCostUsd": 0.48,
        "estimatedMinutes": 28,
        "hypothesisIds": [
          "WQ2.1"
        ],
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
        "runsPerModel": 2,
        "scoringProfileId": 1,
        "targetKind": "Battery",
        "testedModelConfigurationIds": [
          102,
          104
        ],
        "verboseMode": false
      },
      {
        "afterSystemHypotheses": false,
        "allowSourceCodeReferences": true,
        "assessorModelConfigurationId": 21,
        "batteryId": 3,
        "batteryName": "Core knowledge",
        "decides": "Confirms WS2.1 if the decode share stays the largest and reasoning stays at least half the output; refutes it if the reasoning share is under one half.",
        "estimatedCostUsd": 0.48,
        "estimatedMinutes": 28,
        "hypothesisIds": [
          "WS2.1"
        ],
        "kind": "thinkingPair",
        "mark": "Recommended",
        "modelNames": [
          "GPT-5.6 Luna",
          "Orion Max (medium)"
        ],
        "notes": [
          "The estimate for a model without analyzed runs is the analyzed models' mean."
        ],
        "number": 2,
        "order": "Randomized",
        "predictedTier": "Model comparison: only the thinking level differs",
        "purpose": "M",
        "runCount": 1,
        "runMode": "Batch",
        "runsPerModel": 2,
        "scoringProfileId": 1,
        "targetKind": "Battery",
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
        "label": "GPT-5.6 Luna",
        "segments": [
          {
            "key": "ttft",
            "label": "time to first token",
            "value": 2.575
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 14.6
          },
          {
            "key": "toolExecution",
            "label": "tool execution",
            "value": 2
          },
          {
            "key": "ownWaits",
            "label": "own waits and retries",
            "value": 0.1
          },
          {
            "key": "other",
            "label": "other",
            "value": 3.225
          }
        ],
        "token": "subject",
        "total": 22.500000000000004,
        "unit": "seconds"
      },
      {
        "kind": "suite",
        "label": "Suite 1: Item lore",
        "segments": [
          {
            "key": "ttft",
            "label": "time to first token",
            "value": 2.55
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 13
          },
          {
            "key": "toolExecution",
            "label": "tool execution",
            "value": 2
          },
          {
            "key": "ownWaits",
            "label": "own waits and retries",
            "value": 0.1
          },
          {
            "key": "other",
            "label": "other",
            "value": 2.85
          }
        ],
        "total": 20.500000000000004,
        "unit": "seconds"
      },
      {
        "kind": "suite",
        "label": "Suite 2: Hazards",
        "segments": [
          {
            "key": "ttft",
            "label": "time to first token",
            "value": 2.6
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 16.2
          },
          {
            "key": "toolExecution",
            "label": "tool execution",
            "value": 2
          },
          {
            "key": "ownWaits",
            "label": "own waits and retries",
            "value": 0.1
          },
          {
            "key": "other",
            "label": "other",
            "value": 3.6
          }
        ],
        "total": 24.500000000000004,
        "unit": "seconds"
      }
    ],
    "systemFirst": false
  },
  "rows": [],
  "subjectDisplayName": "GPT-5.6 Luna",
  "subjectExplanation": "Comparable: the battery definition and comparability class match the baseline.",
  "subjectKey": "battery:9",
  "subjectKind": "Battery",
  "subjectLabel": "GPT-5.6 Luna",
  "subjectModelId": "gpt-5.6-luna",
  "subjectProvider": "OpenAI",
  "subjectRunIds": [
    12,
    15,
    21,
    24
  ],
  "subjectState": "Comparable",
  "subjectThinkingLevel": "high",
  "suiteName": "Core knowledge"
}
```

## Evaluation terms

- **Purpose statement:** Internal evaluation of candidate AI models for the Overseer assistant within GnollHack.
- **Distillation / training prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.
- **Third-party model content:** Outputs generated by **GPT-5.6 Luna** (OpenAI), graded by models from Google, Anthropic and DeepSeek, and described in this document by **Claude Opus 5.5** (Anthropic) are third-party content evaluated solely for domain-specific benchmark scoring and operational model selection.

---

*Document ID 202 · format version 13 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Full · peers named*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically for structure, permitted figures and references, word limits, disclosure of benchmark text, peer names, significance claims, interval-overlap wording, hype words and spelling; the checks do not verify the prose's interpretations.*
