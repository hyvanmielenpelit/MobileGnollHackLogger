# GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Root Cause Analysis

*INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.*

- **Date:** 2026-09-28
- **Battery:** Core knowledge, revision 2: 2 suites, 2 runs per suite, weighting scheme Questions and difficulty
- **Questions:** 4
- **Member runs:** 4 (battery run 9)
- **Peers:** None; this is a stand-alone report

## Summary

### What we found

> [!IMPORTANT]
> **Key finding.** Intelligence: Not established; speed: Probable cause; cost: Root cause established.

- GPT-5.6 Luna answered 2 of 8 scored answers (25%) without any tool call.

### At a glance

| Model | Intelligence | Median time | Cost per question |
|---|---|---|---|
| **GPT-5.6 Luna** | 80 (77–83) | 12.3 s | $0.036 |

Why each model scored as it did gives, for each model, its strengths, where it gained and lost points, its flags and the hypotheses about it.

### How sure are we?

> [!WARNING]
> **Caveat.** How sure each answer is. **Established (High)**: measured, still clear after the correction for many comparisons, robust in every check, not explained by another setting, and each model ran at least twice per suite. **Probable (Moderate)**: one of those is missing. **Suggestive (Low)**: a lead only; it is not measured, two or more are missing, or the difference may be chance. **Ruled out (Not supported)**: the hypothesis's own test contradicts it.
>
> Every model has two or more runs per suite, so a cause can be graded up to Established (High).
>
> The intelligence hypotheses that could lie in the model or in the instrument are capped at Suggestive (Low): no other model answered these questions.

## Key terms

The terms the summary and the conclusions use, each in plain words. A term is also explained where it first appears, and Appendix H defines every term this document uses.

| Term | What it means |
|---|---|
| 95% interval | The range the true value most likely lies in. |
| Answers without tools | Answers the model wrote without looking anything up. |
| Confound | Another difference between two models that could account for a result. |
| Grade words | How sure the document is of a cause, strongest to weakest. |
| Holm adjustment | A correction for running many comparisons at once. |
| Hypothesis | A possible cause that code tested against the data. |
| Intelligence Index | The overall answer-quality score, out of a hundred. |
| Median and mean | The middle value, and the plain average. |
| Robustness check | A test of whether a result survives a small change. |
| Runs per suite | How many times each model answered each suite. |

## Why each model scored as it did

**GPT-5.6 Luna**: Intelligence 80 (77–83).

- *Strengths:* None ranked first or second.
- *Gained points on:* Not measured with fewer than 3 models.
- *Lost points on:* Confirmed critical errors: 5.6 index points on 1 question (S2-Q3); weakest dimension Completeness, 2.0 points below Accuracy.
- *Flags:* 1 critical error (S2-Q3).
- *Hypotheses:* WQ2.1: GPT-5.6 Luna's points lost in suite 2; WQ3.1: GPT-5.6 Luna's critical errors.

*Each entry is a count, a share or a rank measured over the model's scored answers; none says why. Lost points on: the largest share of the model's index points at risk (the most index points a failure signal could have cost), with its questions (Appendix A). Flags: critical errors (a mistake serious enough to cap the answer's score).*

## Scope and design

This analysis describes battery run 9 of GPT-5.6 Luna on its own. Other models enter only as controls: none qualified.

### Setup and method

- **Battery:** Core knowledge, revision 2: 2 suites, 2 runs per suite, weighting scheme Questions and difficulty; 4 questions in all.
- **Chat configuration under test:** Gameplay Help; concise response style, verboseMode false; tools enabled; web search disabled; subagents disabled; source code references allowed
- **Model under test:** GPT-5.6 Luna (OpenAI, gpt-5.6-luna), thinking level high; 4 member runs.
- **Grading:** Each answer is graded on accuracy, completeness, conciseness and readability, weighted Accuracy 55%, Completeness 25%, Conciseness 10%, Readability 10%. Each dimension is graded on behaviorally anchored levels (fixed grade steps, each described by the answer behavior that earns it) scored 1, 15, 35, 55, 72, 87, 100. A critical error caps the answer's quality at 25.
- **Graders:**
  - Panel member A: Gemini 3.8 Flash (Google, gemini-3.8-flash), different provider from the model under test
  - Panel member B: Claude Haiku 5 (Anthropic, claude-haiku-5), different provider from the model under test
  - Reference reader: DeepSeek V4 (DeepSeek, deepseek-v4), different provider from the model under test
  - Claim verifier: Gemini 3.8 Flash (Google, gemini-3.8-flash), different provider from the model under test
- **Formulas:** Answer quality is the weighted geometric mean (an average that one very low score pulls down sharply) of the four dimension scores, capped by a critical error; in a panel run it is the mean of both graders' scores. The Intelligence Index is the difficulty-weighted mean (an average in which harder questions count for more) of answer quality. Median answer time is the median model time per answer, with tool time excluded. Cost per question is the model under test's spend divided by the questions asked.
- **Battery composite:** The Overall Index is the sum over the suites of each suite's weight times its Intelligence Index, under the battery's weighting scheme; it is not comparable with a single suite's index. Speed and cost are pooled over the member runs, and cost per run is per battery pass, one run of every suite.
- **Comparability:** This report describes the battery result on its own, measured in the comparability class `5e1d0c7b3a91f08c`.
- **Pricing basis:** Catalog prices on 2026-09-20 (price card dated 2026-09-01)
- **Versions:** Harness 41, scoring method 12.

### Controls

No earlier run of another model answered these questions under the same graders, scoring and prompt options. Speed and cost are analyzed from the model's own accounting. The intelligence hypotheses that could lie in the model or in the instrument are capped at Low, and the intelligence axis stays not established until a control run exists.

One run per suite on each side limits what this design can decide; the next runs add replicates.

## Evidence: intelligence

| Model | Intelligence Index | 95% interval | Critical errors |
|---|---|---|---|
| **GPT-5.6 Luna** | 80 | 77–83 | 1 of 4 answers |

| Model | Accuracy | Completeness | Conciseness | Readability |
|---|---|---|---|---|
| **GPT-5.6 Luna** | 80 | 78 | 82 | 84 |

### What the graders recorded

- The grader recorded a comment on this answer.
  - *Evidence:* Q1 · S1-Q1 (90 / 100)

The first known fact: No earlier run of another model answered these questions under the same instrument.

## Evidence: speed

### Where the time goes

| Model | Wall time per question (median, tools included) | Wait before output | Output generation | Tool execution | Own waits and retries | Other |
|---|---|---|---|---|---|---|
| GPT-5.6 Luna | 22.5 s | 2.6 s | 14.6 s | 2.0 s | 0.1 s | 3.2 s |
| Suite 1: Item lore | 20.5 s | 2.6 s | 13.0 s | 2.0 s | 0.1 s | 2.9 s |
| Suite 2: Hazards | 24.5 s | 2.6 s | 16.2 s | 2.0 s | 0.1 s | 3.6 s |

*Wall time per question: the median time per question, tool execution included, split into its parts in proportion to their mean times. The parts are the wait before output (every model call's wait for its first token on one question, summed), output generation (the time spent writing the reply, and how fast it is written), tool execution, the model's own waits and retries, and other time. The wait before output is summed over every model call of the answer, hidden thinking included; the run report's time to first token (the wait before the model starts writing, mostly its thinking) is the first call's alone. It differs from the median model time the other tables of this document give, which leaves tool execution out.*

The speed tables split the time per question into its components.

## Evidence: cost

### Where the cost goes

| Model | Cost per question | Uncached input | Cache reads | Visible output | Reasoning |
|---|---|---|---|---|---|
| GPT-5.6 Luna | $0.019 | $0.0050 | $0.0001 | $0.0090 | $0.0050 |
| Suite 1: Item lore | $0.019 | $0.0050 | $0.0001 | $0.0090 | $0.0050 |
| Suite 2: Hazards | $0.019 | $0.0050 | $0.0001 | $0.0090 | $0.0050 |

*Mean cost per question by component. Each component is a kind of token (the word pieces a model reads and writes, which set its price). Uncached input is text read at the full price, and cache reads are stored input read at a lower price. Cache writes store input for later calls; visible output is the answer; reasoning is the model's hidden thinking. Priced on this basis (which prices turned the token counts into dollars): As run. Left out because it is zero or not recorded in every row: cache writes.*

The cost tables split the cost per question into volume and price.

## Hypotheses

### Intelligence

| Hypothesis | Statement | Grade | Share | Settled by |
|---|---|---|---|---|
| WQ2.1 | GPT-5.6 Luna's points are lost mostly in suite 2 (where its points go, not a comparison). | Suggestive (Low) | 100% | Run 1 |
| WQ3.1 | Confirmed critical errors cost GPT-5.6 Luna most of its points (where its points go, not a comparison). | Suggestive (Low) | 66% | — |

*Share: how much of the difference, or of the total it is part of, this cause accounts for, with its 95% interval where one was computed. Settled by: the run or check under What to run next that would confirm or rule it out.*

*How to read a hypothesis ID such as HQ1.2: H compares two models, W looks inside one model, and H0 is a null hypothesis (the starting assumption that a difference is only chance). Q, S and C stand for intelligence, speed and cost. Appendix C gives each hypothesis's evidence for and against.*

Every tested difference is real (H0-W.1).

The strongest hypothesis in the table: GPT-5.6 Luna's points are lost mostly in suite 2 (where its points go, not a comparison). Its grade and the evidence against it stand beside it.

### Speed

| Hypothesis | Statement | Grade | Share | Settled by |
|---|---|---|---|---|
| WS2.1 | Most of GPT-5.6 Luna's time is output generation, driven by reasoning tokens (where its time goes, not a comparison). | Probable (Moderate) | 83% (82–84%) | Run 2 |

*Share: how much of the difference, or of the total it is part of, this cause accounts for, with its 95% interval where one was computed. Settled by: the run or check under What to run next that would confirm or rule it out.*

The strongest hypothesis in the table: Most of GPT-5.6 Luna's time is output generation, driven by reasoning tokens (where its time goes, not a comparison). Its grade and the evidence against it stand beside it.

### Cost

| Hypothesis | Statement | Grade | Share | Settled by |
|---|---|---|---|---|
| WC1.1 | Output (with reasoning) is GPT-5.6 Luna's largest cost component (where its cost goes, not a comparison). | Established (High) | 73% | — |

*Share: how much of the difference, or of the total it is part of, this cause accounts for, with its 95% interval where one was computed. Settled by: the run or check under What to run next that would confirm or rule it out.*

The strongest hypothesis in the table: Output (with reasoning) is GPT-5.6 Luna's largest cost component (where its cost goes, not a comparison). Its grade and the evidence against it stand beside it.

### The writer's hypotheses

> [!WARNING]
> **Not tested by code.** Hypotheses the report writer added from the grader comments and excerpts. Code has not tested them, so their confidence is Low. Each names its locus (where a cause lies: model, chat, grading, questions, data or provider).
>
> - **Intelligence · Low** (GPT-5.6 Luna; locus suite): GPT-5.6 Luna may have lost points to a rubric that asks for more than the question states.
>   - *For:* The grader comment on Q1 names a missing point.
>   - *Against:* Other models answered the same question well.
>   - *Refuted if:* A rubric check finds every point stated in the question.

## Conclusions

| Axis | Status | Best hypothesis | Why |
|---|---|---|---|
| Intelligence | Not established | WQ2.1: GPT-5.6 Luna's points lost in suite 2 | Suggestive (Low) because leaving the suite out moves the Overall Index by less than the interval half-width, and it is unknown whether both panel members agree (the model was not graded by a panel). |
| Speed | Probable cause | WS2.1: GPT-5.6 Luna's time spent generating output | Probable (Moderate) because the reasoning tokens are not recorded separately. |
| Cost | Root cause established | WC1.1: GPT-5.6 Luna's output cost | Established (High): it passed every check. |

> [!TIP]
> **Conclusion.** Intelligence: Not established. Speed: Probable cause. Cost: Root cause established.

## What to run next

In the order to make them: the Overseer chat first (C), then the benchmark (B), then model selection (M). Appendix D gives each run's launcher settings.

| # | Run | Purpose | Models | Settles | Estimate |
|---|---|---|---|---|---|
| 1 | Control run (recommended) | B, the benchmark | Lyra Mini (high) and Zeta Prime (high) | WQ2.1 | About $0.48 and 28 minutes |
| 2 | Thinking-level pair (recommended) | M, model selection | GPT-5.6 Luna and Orion Max (medium) | WS2.1 | About $0.48 and 28 minutes |

### Checks

No check is proposed.

The runs are ordered chat first, then the benchmark, then model selection.

## Limitations and threats to validity

- The benchmark asks single-turn questions under one chat configuration: live chat carries the conversation before each question, and spoiler-free mode, web search and subagents are off, so a cause found here holds for that configuration only.
- It runs without the wiki text that live chat always gives the model before it answers, so a reading about retrieval or tool routing was measured under a condition live chat does not share.
- The hypotheses come from a fixed rule table over these runs; points at risk are upper bounds, and a decomposition rests on the questions both sides have figures for. The writer's own hypotheses are qualitative.
- Interval: Battery composite: item sampling only. Below three complete rounds there is no reproducibility estimate.
- The graders are AI models. Each grader's provider relation to the model under test is stated under Setup and method; a grader from the model's own provider may read it more favorably.
- Composite: the Overall Index weights the suites under the battery's scheme; another scheme or another set of suites gives another figure, and it is not comparable with a single suite's index.
- Detail: the writer was given the full text of at most a few questions per suite, each with one answer, and the other questions as one-line rows.
- Battery analysis: Fewer than three complete rounds: the interval covers item sampling only.
- No other model answered these questions, so the data cannot tell the model from the instrument on intelligence.
- No earlier battery run of another model ran this battery revision with a current, complete analysis in the same comparability class.

The benchmark asks single questions, so longer conversations are not measured.

## Appendices

- **A. Intelligence detail:** The compared models, configuration differences, failure profiles, grader findings, panel agreement, points at risk, the index difference by dimension, the paired tests the body does not give and the per-question matrix.
- **B. Speed and cost decompositions:** Calls, latency, tokens and the share of each component in each decomposed difference.
- **C. Hypothesis details:** Each hypothesis's evidence for and against, grade reasons and robustness checks, and the null hypotheses.
- **D. Run settings:** Every launcher setting of each run to make next.
- **E. Known facts:** The counts and the configuration facts the analysis states; its tests and decompositions are in the body and in Appendices A and B.
- **F. Reproducibility:** Run ids, dates, hashes and the pricing basis.
- **G. Question details:** The questions the writer cites, those with a critical error, confirmed or overturned, and the control items, each with its answer excerpts.
- **H. Glossary:** Every term this document uses, in plain words.

## Appendix A: Intelligence detail

### Key figures

- **Intelligence:** 80 / 100 (interval 77–83).
- **Speed:** Median answer time 12.3 s.
- **Cost:** $0.036 per question.
- **Critical errors:** 1 of 4 answers.

### Battery profile

The Overall Index is the sum over the suites of each suite's weight times its Intelligence Index, under the Questions and difficulty scheme. It is not comparable with a single suite's Intelligence Index.

| Suite | Weight | Intelligence Index | 95% interval | Contribution | Scored questions | Runs | Speed Index | Cost per run, graders included | Critical-error rate |
|---|---|---|---|---|---|---|---|---|---|
| S1 · Item lore | 60.0% | 84 / 100 | 78–90 | 50.4 points | 2 of 2 questions | 2 runs | 84 / 100 | $0.081 | 0% |
| S2 · Hazards | 40.0% | 74 / 100 | — | 29.6 points | 2 of 2 questions | 2 runs | 80 / 100 | $0.063 | 25% |

*A suite's cost per run is its mean over its member runs, graders included, at the prices stored with each run.*

- **Complete rounds:** 2 complete rounds
- **Standard deviation of the suite indices:** 7.1 points
- **Range of the suite indices:** 10.0 points
- **Pooled identity:** The Overall Index equals one difficulty-weighted index over every question of every suite
- **Critical-error rate:** 13%
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
- **Response-style conflict:** None

A battery report lists no synthesis findings; each member run's report has its own.

### Tool use

- **Tool calls per question:** 3.5
- **Source code share:** 57%
- **Wiki share:** 29%
- **Structured lookup share:** 14%
- **Knowledge base share:** 0%
- **Other tools share:** 0%
- **Failed tool calls:** Not available
- **Calls refused by the tool budget:** Not available

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

*Model calls per answer: the model is called again after each tool round (one cycle of calling a tool and reading its result). Decode rate: the median output tokens per second once the model has started writing. Reasoning share: the part of the output tokens spent on hidden thinking.*

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

Code raised each hypothesis from a rule fixed in advance and graded it. **Established (High)** needs all of these: it is measured; it is still clear after the correction for many comparisons; its null hypothesis is rejected; no confound (another difference between two models that could account for a result) is open; every robustness check (a test of whether a result survives a small change) passed; and each model ran at least twice per suite. **Probable (Moderate)**: exactly one of those conditions is missing. **Suggestive (Low)**: it is not measured, two or more conditions are missing, or the null hypothesis stands. **Ruled out (Not supported)**: the hypothesis's own test contradicts it. A null hypothesis reads **Rejected — a real difference** when a test found a real difference, and **Stands — may be chance** when none could be shown. Each entry gives the models, the locus and the grade, then the evidence for and against.

### Intelligence

- **WQ2.1: GPT-5.6 Luna's points lost in suite 2** (GPT-5.6 Luna; locus model / suite; Suggestive (Low))
  - *For:* Suite 2 holds 100% of GPT-5.6 Luna's points at risk.
  - *Against:* None recorded.
  - *Grade reasons:* See Robustness.
  - *Refuted if:* The contrast stands within noise, or the shares are even.
  - *Robustness:* Replicates passed (Every model ran at least twice per suite); Across suites failed (Leaving the suite out moves the Overall Index by less than the interval half-width); Panel agreement could not be assessed (The model was not graded by a panel).
- **WQ3.1: GPT-5.6 Luna's critical errors** (GPT-5.6 Luna; locus model; Suggestive (Low))
  - *For:* Confirmed critical errors hold 66% of the points at risk (5.6 index points, upper bound).
  - *Against:* None recorded.
  - *Grade reasons:* Critical errors may not clearly hold more of the lost points than the next cause.
  - *Refuted if:* The caps are unconfirmed.
  - *Robustness:* Replicates passed (Every model ran at least twice per suite); Leave one question out could not be assessed (Fewer than two questions put points at risk in this segment); Panel agreement could not be assessed (The model was not graded by a panel).

| Hypothesis | Pair | Paired result | Grade |
|---|---|---|---|
| H0-W.1 | Within GPT-5.6 Luna | Suite 2 against the rest: adjusted p 0.030. | Rejected — a real difference (Not supported) |

### Speed

- **WS2.1: GPT-5.6 Luna's time spent generating output** (GPT-5.6 Luna; locus model; Probable (Moderate))
  - *For:* Output generation is 83% (82–84%) of model time per question.
  - *Against:* The reasoning tokens are not recorded separately.
  - *Grade reasons:* The reasoning tokens are not recorded separately.
  - *Refuted if:* The reasoning share is under one half.
  - *Robustness:* Replicates passed (Every model ran at least twice per suite); Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most).

### Cost

- **WC1.1: GPT-5.6 Luna's output cost** (GPT-5.6 Luna; locus model; Established (High))
  - *For:* Visible output and reasoning are 73% of GPT-5.6 Luna's cost per question.
  - *Against:* None recorded.
  - *Grade reasons:* Every condition of the grade is met.
  - *Refuted if:* Another component is larger.
  - *Robustness:* Replicates passed (Every model ran at least twice per suite).

## Appendix D: Run settings

Each run's settings are the launcher's, copied from the analyzed runs unless its kind changes them; a setting the runs did not share is left for you to choose.

### Run 1: Control run (B, the benchmark, recommended)

- **Decides:** Separates the model from the instrument: once two controls, one from another provider, have run on the same questions under the same graders, the intelligence hypotheses are graded without the control cap.
- **Predicted comparability:** Model comparison: every must-match key agrees and only the model differs
- **Estimate:** About $0.48 and 28 minutes
- **Settles:** WQ2.1

| Launcher field | Setting |
|---|---|
| Models | Model batch |
| Models Under Test | Lyra Mini (high), Zeta Prime (high) |
| Run Target | Battery |
| Battery | Core knowledge |
| Scoring Profile | Profile 1 |
| Response Style | Concise — production default |
| Source Code References | Allowed — answers cite source files and lines |
| Assessor | Configuration 21 |
| Co-Assessor | None |
| Second Reader | None |
| Coverage | Not shared by the analyzed runs: choose it |
| Claim Verifier | None |
| Report Writer | None |
| Runs per Suite | 2 |
| Model order | Randomized (recommended) |

- Without the subject: its run already exists and becomes the other side of the comparison.
- The estimate for a model without analyzed runs is the analyzed models' mean.

### Run 2: Thinking-level pair (M, model selection, recommended)

- **Decides:** Confirms WS2.1 if the decode share stays the largest and reasoning stays at least half the output; refutes it if the reasoning share is under one half.
- **Predicted comparability:** Model comparison: only the thinking level differs
- **Estimate:** About $0.48 and 28 minutes
- **Settles:** WS2.1

| Launcher field | Setting |
|---|---|
| Models | Model batch |
| Models Under Test | GPT-5.6 Luna, Orion Max (medium) |
| Run Target | Battery |
| Battery | Core knowledge |
| Scoring Profile | Profile 1 |
| Response Style | Concise — production default |
| Source Code References | Allowed — answers cite source files and lines |
| Assessor | Configuration 21 |
| Co-Assessor | None |
| Second Reader | None |
| Coverage | Not shared by the analyzed runs: choose it |
| Claim Verifier | None |
| Report Writer | None |
| Runs per Suite | 2 |
| Model order | Randomized (recommended) |

- The estimate for a model without analyzed runs is the analyzed models' mean.

## Appendix E: Known facts

The counts and the configuration facts the analysis states, each numbered as the writer cites it. Its tests and decompositions are given once, under the evidence and in Appendices A and B.

- **1.** No earlier run of another model answered these questions under the same instrument.
- **2.** GPT-5.6 Luna answered 2 of 8 scored questions without a tool call.
- **3.** GPT-5.6 Luna has 1 critical error in 8 scored answers.

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

- **Panel member A (Gemini 3.8 Flash):** Score 92. Accurate and brief.
  - Accuracy: Matches rubric.
- **Panel member B (Claude Haiku 5):** Score 88. Correct.

**Claim verifier:**

- **Grader's statement — refuted (the verifier sided with the answer):** "Worthless glass angers the unicorn." — Glass is caught and returned without anger (dothrow.c).

### S2-Q1

**Question:**

> Will my gem break if I throw it at a unicorn?

**Rubric:**

*Rubric not recorded for this answer.*

#### Run 21

**Answer excerpt:**

> Yes. A thrown gem always shatters on impact, so never throw your valuable gems at a unicorn; keep them for…

**Graders:**

- **Panel member A (Gemini 3.8 Flash):** Score 25. Critical error.
  - Accuracy: The gem does not always shatter.
  - Critical error: "A thrown gem always shatters on impact"
- **Panel member B (Claude Haiku 5):** Score 25. Fabricated breakage.

**Claim verifier:**

- **Answer sentence accused by a grader — refuted (the verifier sided with the grader):** "A thrown gem always shatters on impact." — Gems are caught, not broken (dothrow.c).

## Appendix H: Glossary

- **95% interval:** A range built so that, if the questions were drawn again and again, 95 of 100 such ranges would contain the true value. When two models' ranges overlap, the ranges alone cannot say which is higher.
- **Answer quality:** The score of one answer, from 0 to 100. It combines the four dimension scores in a weighted geometric mean and is capped when the answer has a critical error.
- **Answers without tools:** Answers for which the model called no tool, such as the wiki search or the source code search, and answered from its own knowledge alone.
- **Battery:** A set of suites run together. Each suite is scored on its own, and the battery's Overall Index combines them under the battery's weights.
- **Claim verifier:** An AI model that checks single statements, in answers or in grader comments, against the game's source code and wiki. Its rulings are advisory and can be wrong.
- **Confound:** A setting other than the model itself that differs between the two models a hypothesis compares, such as the response style. While it is open, its effect cannot be told apart from the model's.
- **Control:** An earlier run of another model that answered the same questions under the same graders and settings. Controls show how hard each question is, which helps tell the model from the instrument.
- **Critical error:** A mistake the graders judge serious, such as a wrong game rule stated as fact. It caps the answer's quality, whatever the answer's other scores.
- **Decomposition share:** The part of a time or cost difference that one component accounts for, found by a standard method (the logarithmic-mean Divisia index). The parts always add up to the whole difference, so one part can be larger than the whole when another pulls the other way: a 2 s gap can be 3 s from one part and −1 s from another.
- **Difficulty-weighted mean:** The average of answer quality over the questions, with harder questions counting for more. The Intelligence Index is computed this way.
- **Dimension:** One of the four qualities each answer is graded on: Accuracy, Completeness, Conciseness and Readability. Each has its own weight in the answer's quality.
- **Grade words:** Established (High) means measured, still clear after the correction for many comparisons, and robust in every check, with no open confound and two or more runs per suite. Probable (Moderate) misses exactly one of those conditions. Suggestive (Low) is a lead only. Ruled out (Not supported) means the hypothesis's own test contradicts it.
- **Holm adjustment:** When many pairs are compared at once, some differences look real by chance. The Holm adjustment raises the bar for each comparison, so the whole set keeps at most a one-in-twenty chance of any false finding.
- **Hypothesis:** A possible cause of a difference, raised by a rule fixed in advance and tested by code against the data. Each hypothesis states what would refute it and receives a grade.
- **Intelligence Index:** A model's overall score for answer quality, from 0 to 100. It is the mean quality of the model's answers, with harder questions counting for more.
- **Leave-one-out check:** A robustness check that repeats a result with each question removed in turn. If the result rests on a single question, the check fails.
- **Locus:** Where a hypothesis places the cause: the model itself; the Overseer chat prompt or its tools (chat); the grading (harness); a question or its rubric (suite); the data a tool reads (corpus); or the serving (provider).
- **Median and mean:** The median is the middle value once all values are sorted, so a few very slow or costly questions barely move it. The mean is the plain average, which they do move.
- **Null hypothesis:** The starting assumption that a difference, between two models or between two groups of questions, is only chance. A paired test, or else a bootstrap interval that excludes zero, can reject it. A null that stands does not prove that the two are equal.
- **Output generation and decode rate:** Output generation is the time the model spends writing its reply once it has started. The decode rate is how fast it writes, in tokens per second.
- **Overall Index:** A battery's combined score: each suite's Intelligence Index times the suite's weight, summed over the suites. It is not comparable with a single suite's index.
- **Panel member:** In a panel run, two AI graders, Panel member A and Panel member B, score each answer. The answer's score is the mean of their two scores.
- **Points at risk:** The index points a model lost on its answers below its own median answer, each answer counted once under its first signal, such as no tool call, a defect or a critical error. They are an upper bound on what each signal costs, not an estimate of its effect.
- **Pricing basis:** The prices per token used to turn token counts into dollars: today's catalog prices, or the prices stored with each run. Costs on different bases are not comparable.
- **Reference reader:** A third AI grader that scores the same answers as a check on the panel. Its scores do not count toward any result.
- **Replicate run:** Running the same model on the same questions again. With only one run per suite, the document cannot tell whether a result would repeat, which caps every grade at Probable.
- **Response style:** The production chat asks the model for concise answers; a run can ask for detailed ones instead. Completeness is graded under the style the run used, and only a pair of runs in both styles shows how much the style matters.
- **Robustness check:** A test of whether a hypothesis still holds when the data changes a little: one question left out, another suite, a second run, the other panel member, or another setting.
- **Runs per suite:** How many times a model answered every question of a suite. With one run per suite, the data cannot show whether a result would repeat.
- **Scoring levels:** Each dimension is graded on a fixed scale of levels, each described by the answer behavior that earns it (behaviorally anchored levels). Each level stands for a fixed score.
- **Suite:** A fixed set of benchmark questions, each with its rubric. A run answers every question of one suite once.
- **Time to first token:** The seconds from sending a request to the first word of the reply. For reasoning models it is mostly the time the model spends thinking before it answers.
- **Tokens:** The pieces of text, roughly three quarters of a word each, that a model reads and writes; providers price by them. Uncached input is text read at the full price. Cache reads are input stored from an earlier call and charged less; cache writes store it. Visible output is the answer, and reasoning is the model's hidden thinking, billed as output; a provider that does not report it separately counts it inside output.
- **Tool round:** One cycle in which the model calls a tool, such as the wiki search, reads its result and continues. Each round adds a model call, with its own wait for the first token.
- **Wait before output:** The time to first token of every model call of one question, added up: each tool round adds one more wait. It is the first part of the time per question, not the run's time to first token, which is the first call's alone.
- **Weighted geometric mean:** The way an answer's four dimension scores are combined. Unlike a plain average, it falls sharply when any one dimension is very low, so strong dimensions cannot hide a weak one.

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
      "display": "13%",
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
      "display": "Gameplay Help; concise response style, verboseMode false; tools enabled; web search disabled; subagents disabled; source code references allowed",
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
      "display": "Battery composite: item sampling only. Below three complete rounds there is no reproducibility estimate",
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
      "display": "0.0 index points",
      "key": "rca.atRisk.subject.budgetOrDefect",
      "value": 0
    },
    {
      "available": true,
      "display": "5.6 index points",
      "key": "rca.atRisk.subject.criticalError",
      "value": 5.625
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.subject.noTool",
      "value": 0
    },
    {
      "available": true,
      "display": "2.9 index points",
      "key": "rca.atRisk.subject.otherLow",
      "value": 2.875
    },
    {
      "available": true,
      "display": "8.5 index points below a median answer of 75",
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
      "display": "Rejected — a real difference (Not supported)",
      "key": "rca.hypothesis.H0-W.1.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: suite 2 against the rest: adjusted p 0.030",
      "key": "rca.hypothesis.H0-W.1.gradeReasons"
    },
    {
      "available": true,
      "display": "no difference within GPT-5.6 Luna's own questions",
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
      "display": "H0-W.1",
      "key": "rca.hypothesis.H0-W.1.ref"
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
      "display": "Visible output and reasoning are 73% of GPT-5.6 Luna's cost per question.",
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
      "display": "GPT-5.6 Luna's output cost",
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
      "display": "WC1.1",
      "key": "rca.hypothesis.WC1.1.ref"
    },
    {
      "available": true,
      "display": "Another component is larger.",
      "key": "rca.hypothesis.WC1.1.refutedIf"
    },
    {
      "available": true,
      "display": "73%",
      "key": "rca.hypothesis.WC1.1.share",
      "value": 0.7329842931937172
    },
    {
      "available": true,
      "display": "of its cost",
      "key": "rca.hypothesis.WC1.1.shareOf"
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
      "display": "suite 2 holds 100% of GPT-5.6 Luna's points at risk.",
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
      "display": "leaving the suite out moves the Overall Index by less than the interval half-width; it is unknown whether both panel members agree (the model was not graded by a panel)",
      "key": "rca.hypothesis.WQ2.1.gradeReasons"
    },
    {
      "available": true,
      "display": "GPT-5.6 Luna's points lost in suite 2",
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
      "display": "WQ2.1",
      "key": "rca.hypothesis.WQ2.1.ref"
    },
    {
      "available": true,
      "display": "The contrast stands within noise, or the shares are even.",
      "key": "rca.hypothesis.WQ2.1.refutedIf"
    },
    {
      "available": true,
      "display": "100%",
      "key": "rca.hypothesis.WQ2.1.share",
      "value": 1
    },
    {
      "available": true,
      "display": "of its points at risk",
      "key": "rca.hypothesis.WQ2.1.shareOf"
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
      "display": "Confirmed critical errors hold 66% of the points at risk (5.6 index points, upper bound).",
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
      "display": "it is unknown whether the result depends on one question (fewer than two questions put points at risk in this segment); it is unknown whether both panel members agree (the model was not graded by a panel); critical errors may not clearly hold more of the lost points than the next cause",
      "key": "rca.hypothesis.WQ3.1.gradeReasons"
    },
    {
      "available": true,
      "display": "GPT-5.6 Luna's critical errors",
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
      "display": "WQ3.1",
      "key": "rca.hypothesis.WQ3.1.ref"
    },
    {
      "available": true,
      "display": "The caps are unconfirmed.",
      "key": "rca.hypothesis.WQ3.1.refutedIf"
    },
    {
      "available": true,
      "display": "66%",
      "key": "rca.hypothesis.WQ3.1.share",
      "value": 0.6617647058823529
    },
    {
      "available": true,
      "display": "of its points at risk",
      "key": "rca.hypothesis.WQ3.1.shareOf"
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
      "display": "Output generation is 83% (82–84%) of model time per question.",
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
      "display": "the reasoning tokens are not recorded separately",
      "key": "rca.hypothesis.WS2.1.gradeReasons"
    },
    {
      "available": true,
      "display": "82–84%",
      "key": "rca.hypothesis.WS2.1.interval"
    },
    {
      "available": true,
      "display": "GPT-5.6 Luna's time spent generating output",
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
      "display": "WS2.1",
      "key": "rca.hypothesis.WS2.1.ref"
    },
    {
      "available": true,
      "display": "The reasoning share is under one half.",
      "key": "rca.hypothesis.WS2.1.refutedIf"
    },
    {
      "available": true,
      "display": "83%",
      "key": "rca.hypothesis.WS2.1.share",
      "value": 0.8295454545454546
    },
    {
      "available": true,
      "display": "of its model time",
      "key": "rca.hypothesis.WS2.1.shareOf"
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
      "display": "Output generation is 65% of GPT-5.6 Luna's time per question.",
      "key": "rca.known.4"
    },
    {
      "available": true,
      "display": "Visible output is 47% of GPT-5.6 Luna's cost per question.",
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
      "display": "B, the benchmark",
      "key": "rca.run.1.purpose"
    },
    {
      "available": true,
      "display": "Run 1, a control run of Lyra Mini (high) and Zeta Prime (high) (about $0.48 and 28 minutes)",
      "key": "rca.run.1.summary"
    },
    {
      "available": true,
      "display": "M, model selection",
      "key": "rca.run.2.purpose"
    },
    {
      "available": true,
      "display": "Run 2, a thinking-level pair of GPT-5.6 Luna and Orion Max (medium) (about $0.48 and 28 minutes)",
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
      "display": "1%",
      "key": "rca.within.subject.cost.cacheRead.share",
      "value": 0.005235602094240839
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.subject.cost.cacheWrite.share",
      "value": 0
    },
    {
      "available": true,
      "display": "47%",
      "key": "rca.within.subject.cost.output.share",
      "value": 0.4712041884816754
    },
    {
      "available": true,
      "display": "26%",
      "key": "rca.within.subject.cost.reasoning.share",
      "value": 0.2617801047120419
    },
    {
      "available": true,
      "display": "a mean of $0.019 per question",
      "key": "rca.within.subject.cost.total",
      "value": 0.0191
    },
    {
      "available": true,
      "display": "26%",
      "key": "rca.within.subject.cost.uncachedInput.share",
      "value": 0.2617801047120419
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.within.subject.speed.burstAnswers",
      "unavailableReason": "No answer recorded whether its visible text arrived in one burst."
    },
    {
      "available": true,
      "display": "a mean of 3.2 s per question",
      "key": "rca.within.subject.time.other.seconds",
      "value": 3.225
    },
    {
      "available": true,
      "display": "14%",
      "key": "rca.within.subject.time.other.share",
      "value": 0.1433333333333333
    },
    {
      "available": true,
      "display": "a mean of 14.6 s per question",
      "key": "rca.within.subject.time.outputGeneration.seconds",
      "value": 14.6
    },
    {
      "available": true,
      "display": "65%",
      "key": "rca.within.subject.time.outputGeneration.share",
      "value": 0.6488888888888887
    },
    {
      "available": true,
      "display": "a mean of 0.1 s per question",
      "key": "rca.within.subject.time.ownWaits.seconds",
      "value": 0.1
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.subject.time.ownWaits.share",
      "value": 0.004444444444444444
    },
    {
      "available": true,
      "display": "a mean of 2.0 s per question",
      "key": "rca.within.subject.time.toolExecution.seconds",
      "value": 2
    },
    {
      "available": true,
      "display": "9%",
      "key": "rca.within.subject.time.toolExecution.share",
      "value": 0.08888888888888888
    },
    {
      "available": true,
      "display": "a mean of 22.5 s per question",
      "key": "rca.within.subject.time.total",
      "value": 22.500000000000004
    },
    {
      "available": true,
      "display": "a mean of 2.6 s per question, summed over its model calls",
      "key": "rca.within.subject.time.ttft.seconds",
      "value": 2.575
    },
    {
      "available": true,
      "display": "11%",
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
      "display": "Accuracy 55%, Completeness 25%, Conciseness 10%, Readability 10%",
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
      "display": "0%",
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
      "display": "60.0%",
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
      "display": "25%",
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
      "display": "40.0%",
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
      "display": "0%",
      "key": "tools.share.knowledgeBase"
    },
    {
      "available": true,
      "display": "0%",
      "key": "tools.share.other"
    },
    {
      "available": true,
      "display": "57%",
      "key": "tools.share.sourceCode"
    },
    {
      "available": true,
      "display": "14%",
      "key": "tools.share.structuredLookup"
    },
    {
      "available": true,
      "display": "29%",
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
        "reason": "Suggestive (Low) because leaving the suite out moves the Overall Index by less than the interval half-width, and it is unknown whether both panel members agree (the model was not graded by a panel).",
        "status": "NotEstablished"
      },
      {
        "axis": "speed",
        "bestHypothesis": "WS2.1",
        "reason": "Probable (Moderate) because the reasoning tokens are not recorded separately.",
        "status": "ProbableCause"
      },
      {
        "axis": "cost",
        "bestHypothesis": "WC1.1",
        "reason": "Established (High): it passed every check.",
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
    "engineVersion": 14,
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
        "burstAnswers": 0,
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
            "label": "wait before output",
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
        "timedAnswers": 0,
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
          "its own test rules it out: suite 2 against the rest: adjusted p 0.030"
        ],
        "id": "H0-W",
        "key": "H0-W.1",
        "kind": "null",
        "label": "no difference within GPT-5.6 Luna's own questions",
        "loci": [],
        "locus": "—",
        "models": [
          "subject"
        ],
        "modelsText": "GPT-5.6 Luna",
        "number": 1,
        "priceEffect": false,
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
          "suite 2 holds 100% of GPT-5.6 Luna's points at risk."
        ],
        "grade": "Low",
        "gradeReasons": [
          "leaving the suite out moves the Overall Index by less than the interval half-width",
          "it is unknown whether both panel members agree (the model was not graded by a panel)"
        ],
        "id": "WQ2",
        "key": "WQ2.1",
        "kind": "within",
        "label": "GPT-5.6 Luna's points lost in suite 2",
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
        "priceEffect": false,
        "refutedIf": "The contrast stands within noise, or the shares are even.",
        "robustness": [
          {
            "detail": "Every model ran at least twice per suite.",
            "name": "Replicates",
            "status": "Passed"
          },
          {
            "detail": "Leaving the suite out moves the Overall Index by less than the interval half-width.",
            "name": "Across suites",
            "status": "Failed"
          },
          {
            "detail": "The model was not graded by a panel.",
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
          "Confirmed critical errors hold 66% of the points at risk (5.6 index points, upper bound)."
        ],
        "grade": "Low",
        "gradeReasons": [
          "it is unknown whether the result depends on one question (fewer than two questions put points at risk in this segment)",
          "it is unknown whether both panel members agree (the model was not graded by a panel)",
          "critical errors may not clearly hold more of the lost points than the next cause"
        ],
        "id": "WQ3",
        "key": "WQ3.1",
        "kind": "within",
        "label": "GPT-5.6 Luna's critical errors",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "subject"
        ],
        "modelsText": "GPT-5.6 Luna",
        "number": 1,
        "priceEffect": false,
        "refutedIf": "The caps are unconfirmed.",
        "robustness": [
          {
            "detail": "Every model ran at least twice per suite.",
            "name": "Replicates",
            "status": "Passed"
          },
          {
            "detail": "Fewer than two questions put points at risk in this segment.",
            "name": "Leave one question out",
            "status": "NotAssessable"
          },
          {
            "detail": "The model was not graded by a panel.",
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
          "Output generation is 83% (82–84%) of model time per question."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "the reasoning tokens are not recorded separately"
        ],
        "id": "WS2",
        "key": "WS2.1",
        "kind": "within",
        "label": "GPT-5.6 Luna's time spent generating output",
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
        "priceEffect": false,
        "refutedIf": "The reasoning share is under one half.",
        "robustness": [
          {
            "detail": "Every model ran at least twice per suite.",
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
          "Visible output and reasoning are 73% of GPT-5.6 Luna's cost per question."
        ],
        "grade": "High",
        "gradeReasons": [],
        "id": "WC1",
        "key": "WC1.1",
        "kind": "within",
        "label": "GPT-5.6 Luna's output cost",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "subject"
        ],
        "modelsText": "GPT-5.6 Luna",
        "number": 1,
        "priceEffect": false,
        "refutedIf": "Another component is larger.",
        "robustness": [
          {
            "detail": "Every model ran at least twice per suite.",
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
        "text": "Output generation is 65% of GPT-5.6 Luna's time per question."
      },
      {
        "factKeys": [
          "rca.within.subject.cost.output.share"
        ],
        "kind": "decomposition",
        "number": 5,
        "text": "Visible output is 47% of GPT-5.6 Luna's cost per question."
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
        "decidesItems": [
          "Separates the model from the instrument: once two controls, one from another provider, have run on the same questions under the same graders, the intelligence hypotheses are graded without the control cap."
        ],
        "estimatedCostUsd": 0.48,
        "estimatedMinutes": 28,
        "hypothesisIds": [
          "WQ2.1"
        ],
        "kind": "control",
        "launches": 1,
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
        "decidesItems": [
          "Confirms WS2.1 if the decode share stays the largest and reasoning stays at least half the output; refutes it if the reasoning share is under one half."
        ],
        "estimatedCostUsd": 0.48,
        "estimatedMinutes": 28,
        "hypothesisIds": [
          "WS2.1"
        ],
        "kind": "thinkingPair",
        "launches": 1,
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
            "label": "wait before output",
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
            "label": "wait before output",
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
            "label": "wait before output",
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

*Document ID 202 · format version 26 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Full · peers named*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically for structure, permitted figures and references, word limits, disclosure of benchmark text, peer names, significance claims, interval-overlap wording, hype words and spelling; the checks do not verify the prose's interpretations.*
