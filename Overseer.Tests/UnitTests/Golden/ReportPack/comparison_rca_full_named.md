**ROOT CAUSE ANALYSIS**

# Comparison #12 — Spring model sweep

**Comparison:** 5 models · computed 2026-09-21

*INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.*

- **Date:** 2026-09-28
- **Suite:** GnollHack Core Suite
- **Questions:** 6
- **Models:** Orion Max, Vega Pro, Lyra Mini, Nova Lite and Zeta Prime
- **Pricing basis:** Catalog prices on 2026-09-21

## Summary

### What we found

> [!IMPORTANT]
> **Key finding.** Intelligence: Not established; speed: Not established; cost: Not established.

- Every model answered at least 33% of its scored answers without any tool call.
- Zeta Prime costs the least per question. Lyra Mini, Vega Pro, Orion Max and Nova Lite are established to score higher.
- Vega Pro scored higher than Nova Lite at 0.50 times its cost per question, established by 95% intervals that do not overlap.
- Nova Lite scored higher than Zeta Prime at 0.40 times its median answer time, established by 95% intervals that do not overlap.

### At a glance

| Model | Intelligence | Median time | Cost per question |
|---|---|---|---|
| Orion Max (reference) | 85 (82–88), rank 1–2 (tied) | 12.0 s | $0.050 |
| Vega Pro | 80 (77–83), rank 1–2 (tied) | 9.0 s | $0.030 |
| Lyra Mini | 70 (66–74), rank 3–4 (tied) | 15.0 s | $0.020 |
| Nova Lite | 70 (64–76), rank 3–4 (tied) | 8.0 s | $0.060 |
| Zeta Prime | 55 (50–60), rank 5 | 20.0 s | $0.010 |

Why each model scored as it did gives, for each model, its strengths, where it gained and lost points, its difference from the reference by dimension, its flags and the hypotheses about it.

### How sure are we?

> [!WARNING]
> **Caveat.** How sure each answer is. **Established (High)**: measured, still clear after the correction for many comparisons, robust in every check, not explained by another setting, and each model ran at least twice per suite. **Probable (Moderate)**: one of those is missing. **Suggestive (Low)**: a lead only; it is not measured, two or more are missing, or the difference may be chance. **Ruled out (Not supported)**: the hypothesis's own test contradicts it.
>
> Each model ran only once per suite, so this document cannot tell whether its results would repeat. No cause can be graded above Probable (Moderate) until a second run of each suite is made, apart from a price effect, which follows from the price cards.

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
| Replicate run | A second run of the same model, to see if results repeat. |
| Robustness check | A test of whether a result survives a small change. |
| Runs per suite | How many times each model answered each suite. |
| Tied rank | The span of places the model's interval allows. |

## Why each model scored as it did

**Orion Max (reference)**: Intelligence 85 (82–88), rank 1–2 (tied).

- *Strengths:* Tied for first of 5 models on Accuracy; tied for first of 5 models on Completeness.
- *Gained points on:* Answers above the other models' median by 10 or more: +15.5 index points on 5 questions (Q1, Q2, Q4 and 2 more).
- *Lost points on:* Answers without tools: 9.8 index points on 1 question (Q3).
- *Against the reference:* The reference.
- *Flags:* None.

**Vega Pro**: Intelligence 80 (77–83), rank 1–2 (tied).

- *Strengths:* Tied for first of 5 models on Accuracy; tied for first of 5 models on Completeness.
- *Gained points on:* Answers above the other models' median by 10 or more: +13.1 index points on 4 questions (Q1, Q2, Q3 and 1 more).
- *Lost points on:* Low-scoring answers, mostly Accuracy: 2.8 index points on 2 questions (Q3 and Q4).
- *Against the reference:* -5.0 index points: critical-error cap, difficulty weighting and spread -5.0.
- *Flags:* None.

**Lyra Mini**: Intelligence 70 (66–74), rank 3–4 (tied).

- *Strengths:* None ranked first or second.
- *Gained points on:* No answer 10 or more above the other models' median.
- *Lost points on:* Confirmed critical errors: 7.2 index points on 1 question (Q3).
- *Against the reference:* -15.0 index points: Accuracy -8.3, Completeness -3.8, Conciseness -1.5, Readability -1.5.
- *Flags:* 1 critical error (Q3).

**Nova Lite**: Intelligence 70 (64–76), rank 3–4 (tied).

- *Strengths:* None ranked first or second.
- *Gained points on:* No answer 10 or more above the other models' median.
- *Lost points on:* Answers without tools: 6.3 index points on 1 question (Q3).
- *Against the reference:* -15.0 index points: Accuracy -8.7, Completeness -4.0, Conciseness -1.6, Readability -1.6.
- *Flags:* None.

**Zeta Prime**: Intelligence 55 (50–60), rank 5.

- *Strengths:* None ranked first or second.
- *Gained points on:* No answer 10 or more above the other models' median.
- *Lost points on:* Low-scoring answers, mostly Accuracy: 5.6 index points on 3 questions (Q1, Q3 and Q4); last of 5 models on Accuracy.
- *Against the reference:* -30.0 index points: Accuracy -16.5, Completeness -7.5, Conciseness -3.0, Readability -3.0.
- *Flags:* 1 isolated miss (Q1).

*Each entry is a count, a share or a rank measured over the model's scored answers; none says why. Gained points on: the questions the model answered at least 10 points above the other models' median, with the index points those margins add up to over its questions. Lost points on: the largest share of the model's index points at risk (the most index points a failure signal could have cost), with its questions (Appendix A). Against the reference: the model's Intelligence Index minus the reference's, split by dimension (Appendix A). Flags: critical errors (a mistake serious enough to cap the answer's score); isolated misses (a question one model missed while the others did well). An isolated miss is a question the model scored 50 or less on while the other models' median was 75 or more.*

## Unexpected results

No unexpected results could be looked for: no two models share a provider.

## Scope and design

### Setup and method

- **Suite:** GnollHack Core Suite, 6 questions in the per-question matrix.
- **Coverage:** Every non-excluded model of Comparison #12 — Spring model sweep.
- **Chat configuration under test:** Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed
- **Grading:** Each answer is graded on accuracy, completeness, conciseness and readability, weighted Accuracy 55%, Completeness 25%, Conciseness 10%, Readability 10%. Each dimension is graded on behaviorally anchored levels (fixed grade steps, each described by the answer behavior that earns it) scored 1, 15, 35, 55, 72, 87, 100. A critical error caps the answer's quality at 25.
- **Graders:**
  - Assessor: Gemini 3.8 Flash (Google, gemini-3.8-flash), a different provider from every model
- **Formulas:** Answer quality is the weighted geometric mean (an average that one very low score pulls down sharply) of the four dimension scores, capped by a critical error; in a panel run it is the mean of both graders' scores. The Intelligence Index is the difficulty-weighted mean (an average in which harder questions count for more) of answer quality. Median answer time is the median model time per answer, with tool time excluded. Cost per question is each model's own spend divided by the questions asked.
- **Comparability:** Every model in this report was measured under one instrument condition, signature `sig-7f3a91`.
- **Pricing basis:** Catalog prices on 2026-09-21
- **Versions:** Harness 41; scoring method 12.

One run per suite on each side limits what this design can decide; the next runs add replicates.

## Evidence: intelligence

| Model | Intelligence Index | 95% interval | Rank | Critical errors |
|---|---|---|---|---|
| Orion Max (reference) | 85 | 82–88 | 1–2 (tied) | 0 of 6 answers |
| Vega Pro | 80 | 77–83 | 1–2 (tied) | 0 of 6 answers |
| Lyra Mini | 70 | 66–74 | 3–4 (tied) | 1 of 6 answers |
| Nova Lite | 70 | 64–76 | 3–4 (tied) | 0 of 6 answers |
| Zeta Prime | 55 | 50–60 | 5 | 0 of 6 answers |

*A rank shown as a span, such as 5–9 (tied), is a tied rank: the model's 95% interval (the range the true value most likely lies in) overlaps others', so its rank is the span of places the interval allows. Interval bounds are rounded to whole numbers, so two intervals printed as touching may not overlap; ranks use the unrounded bounds.*

| Model | Accuracy | Completeness | Conciseness | Readability |
|---|---|---|---|---|
| Orion Max (reference) | 80 | 80 | 80 | 80 |
| Vega Pro | 80 | 80 | 80 | 80 |
| Lyra Mini | 65 | 65 | 65 | 65 |
| Nova Lite | 64 | 64 | 64 | 64 |
| Zeta Prime | 50 | 50 | 50 | 50 |

### Paired tests against Orion Max

| Pair | Questions | Difference (95% interval) | Result |
|---|---|---|---|
| Vega Pro vs Orion Max | 6 | 0.0 points (-21.0 to +21.0) | Not established |
| Lyra Mini vs Orion Max | 6 | -15.2 points (-23.8 to -6.6) | Not established |
| Nova Lite vs Orion Max | 6 | -15.8 points (-26.1 to -5.6) | Not established |
| Zeta Prime vs Orion Max | 6 | -30.0 points (-43.9 to -16.1) | Not established |

*Each row is a paired test (a comparison of two models on the same questions). Intelligence: Holm-adjusted across 4 tests (a correction for running many comparisons at once). Difference: the mean per-question difference, first model minus the reference, on the questions both answered. Appendix A gives the tests among all pairs.*

### What the graders recorded

- The grader recorded a comment on this answer.
  - *Evidence:* Q1

The first known fact: Uncached input, price per token accounts for 45% (44–46%) of the per-question cost difference between Orion Max and Vega Pro.

## Evidence: speed

### Where the time goes

| Model | Wall time per question (median, tools included) | Wait before output | Output generation | Tool execution | Other |
|---|---|---|---|---|---|
| Orion Max | 8.9 s | 1.9 s | 5.2 s | 1.5 s | 0.3 s |
| Vega Pro | 10.4 s | 2.5 s | 6.1 s | 1.5 s | 0.3 s |
| Lyra Mini | 11.9 s | 3.1 s | 7.0 s | 1.5 s | 0.3 s |
| Nova Lite | 13.4 s | 3.8 s | 7.9 s | 1.5 s | 0.3 s |
| Zeta Prime | 14.9 s | 4.4 s | 8.7 s | 1.5 s | 0.3 s |

*Wall time per question: the median time per question, tool execution included, split into its parts in proportion to their mean times. The parts are the wait before output, output generation (the time spent writing the reply, and how fast it is written), tool execution, the model's own waits and retries, and other time. The wait before output is summed over every model call of the answer, hidden thinking included; the run report's time to first token (the wait before the model starts writing, mostly its thinking) is the first call's alone. It differs from the median model time the other tables of this document give, which leaves tool execution out. Left out because it is zero or not recorded in every row: own waits and retries.*

### Paired tests against Orion Max

| Pair | Time ratio (95% interval) | Result |
|---|---|---|
| Vega Pro vs Orion Max | 1.20 (0.76 to 1.89) | Not established |
| Lyra Mini vs Orion Max | 1.40 (1.37 to 1.43) | Not established |
| Nova Lite vs Orion Max | 1.60 (1.02 to 2.51) | Not established |
| Zeta Prime vs Orion Max | 1.80 (1.75 to 1.86) | Not established |

*Each row is a paired test. Speed: Holm-adjusted across 4 tests. A ratio is the first model's own time per question divided by the reference's, on the questions both answered. Appendix A gives the tests among all pairs.*

### Time differences between the models

Not split into parts, because the difference itself may be chance: Orion Max vs Vega Pro, Orion Max vs Lyra Mini, Orion Max vs Nova Lite, Orion Max vs Zeta Prime and Nova Lite vs Zeta Prime.

The speed tables split the time per question into its components.

## Evidence: cost

### Where the cost goes

| Model | Cost per question | Uncached input | Cache reads | Visible output | Reasoning |
|---|---|---|---|---|---|
| Orion Max | $0.014 | $0.0042 | $0.0001 | $0.0067 | $0.0030 |
| Vega Pro | $0.022 | $0.0082 | $0.0001 | $0.0089 | $0.0046 |
| Lyra Mini | $0.031 | $0.013 | $0.0002 | $0.011 | $0.0064 |
| Nova Lite | $0.041 | $0.018 | $0.0002 | $0.014 | $0.0086 |
| Zeta Prime | $0.052 | $0.024 | $0.0002 | $0.017 | $0.011 |

*Mean cost per question by component. Each component is a kind of token (the word pieces a model reads and writes, which set its price). Uncached input is text read at the full price, and cache reads are stored input read at a lower price. Cache writes store input for later calls; visible output is the answer; reasoning is the model's hidden thinking. Priced on this basis (which prices turned the token counts into dollars): Priced from the catalog as of 2026-09-20. Comparable across dates; not what was actually spent. Left out because it is zero or not recorded in every row: cache writes.*

### Paired tests against Orion Max

Cost: Not tested. No candidate price card was resolved for run #31 of Orion Max on this pricing basis, so its cost is unknown.

### Cost differences between the models

Mostly the price cards: Orion Max vs Vega Pro; Orion Max vs Lyra Mini; Orion Max vs Nova Lite; Orion Max vs Zeta Prime; Nova Lite vs Zeta Prime.

*Each difference is split into parts that add up exactly to it (a standard method, the logarithmic-mean Divisia index), using only the questions both models have figures for. A share's interval comes from resampling those questions (a bootstrap interval). The difference is the first model's minus the second's. A share can pass 100% when another component pulls the other way. Appendix B gives each pair's split.*

The cost tables split the cost per question into volume and price.

## Hypotheses

### Intelligence

| Hypothesis | Statement | Grade | Share | Settled by |
|---|---|---|---|---|
| HQ3.1 | Shared failures on specific questions point to the instrument, not the models. | Suggestive (Low) | — | Check 1 |

*Share: how much of the difference, or of the total it is part of, this cause accounts for, with its 95% interval where one was computed. Settled by: the run or check under What to run next that would confirm or rule it out.*

*How to read a hypothesis ID such as HQ1.2: H compares two models, W looks inside one model, and H0 is a null hypothesis (the starting assumption that a difference is only chance). Q, S and C stand for intelligence, speed and cost. Appendix C gives each hypothesis's evidence for and against.*

No tested difference could be shown to be real; each may be chance.

The strongest hypothesis in the table: Shared failures on specific questions point to the instrument, not the models. Its grade and the evidence against it stand beside it.

### Speed

| Hypothesis | Statement | Grade | Share | Settled by |
|---|---|---|---|---|
| WS3.1 | Tool rounds multiply Orion Max's time (where its time goes, not a comparison). | Probable (Moderate) | — | Run 1 |
| WS3.2 | Tool rounds multiply Nova Lite's time (where its time goes, not a comparison). | Probable (Moderate) | — | Run 1 |
| WS3.3 | Tool rounds multiply Zeta Prime's time (where its time goes, not a comparison). | Probable (Moderate) | — | Run 1 |
| WS2.1 | Most of Zeta Prime's time is output generation, driven by reasoning tokens (where its time goes, not a comparison). | Ruled out (Not supported) | 65% (64–66%) | — |
| WS2.2 | Most of Nova Lite's time is output generation, driven by reasoning tokens (where its time goes, not a comparison). | Ruled out (Not supported) | 66% (65–67%) | — |
| WS2.3 | Most of Orion Max's time is output generation, driven by reasoning tokens (where its time goes, not a comparison). | Ruled out (Not supported) | 70% (69–71%) | — |

*Share: how much of the difference, or of the total it is part of, this cause accounts for, with its 95% interval where one was computed. Settled by: the run or check under What to run next that would confirm or rule it out.*

No tested difference could be shown to be real; each may be chance.

The strongest hypothesis in the table: Tool rounds multiply Orion Max's time (where its time goes, not a comparison). Its grade and the evidence against it stand beside it.

### Cost

| Hypothesis | Statement | Grade | Share | Settled by |
|---|---|---|---|---|
| WC1.1 | Output (with reasoning) is Zeta Prime's largest cost component (where its cost goes, not a comparison). | Probable (Moderate) | 54% | — |
| WC1.2 | Output (with reasoning) is Nova Lite's largest cost component (where its cost goes, not a comparison). | Probable (Moderate) | 55% | — |
| WC1.3 | Output (with reasoning) is Orion Max's largest cost component (where its cost goes, not a comparison). | Probable (Moderate) | 69% | — |
| HC4.1 | Weak prompt caching raises Zeta Prime's cost against Orion Max. | Ruled out (Not supported) | 9% (9–9%) | — |
| HC4.2 | Weak prompt caching raises Nova Lite's cost against Orion Max. | Ruled out (Not supported) | 8% (8–9%) | — |
| HC4.3 | Weak prompt caching raises Zeta Prime's cost against Nova Lite. | Ruled out (Not supported) | 12% (12–13%) | — |
| HC4.4 | Weak prompt caching raises Lyra Mini's cost against Orion Max. | Ruled out (Not supported) | 8% (7–8%) | — |

*Share: how much of the difference, or of the total it is part of, this cause accounts for, with its 95% interval where one was computed. Settled by: the run or check under What to run next that would confirm or rule it out.*

5 cost differences follow mainly from the price cards (HC2.1–HC2.5); Appendix C lists them.

Every tested difference is real (H0-C.1–H0-C.5).

The strongest hypothesis in the table: Output (with reasoning) is Zeta Prime's largest cost component (where its cost goes, not a comparison). Its grade and the evidence against it stand beside it.

### The writer's hypotheses

> [!WARNING]
> **Not tested by code.** Hypotheses the report writer added from the grader comments and excerpts. Code has not tested them, so their confidence is Low. Each names its locus (where a cause lies: model, chat, grading, questions, data or provider).
>
> - **Intelligence · Low** (Orion Max; locus suite): Orion Max may have lost points to a rubric that asks for more than the question states.
>   - *For:* The grader comment on Q1 names a missing point.
>   - *Against:* Other models answered the same question well.
>   - *Refuted if:* A rubric check finds every point stated in the question.

## Conclusions

| Axis | Status | Best hypothesis | Why |
|---|---|---|---|
| Intelligence | Not established | HQ3.1: shared failures on the same questions | Suggestive (Low) because it is observed, not measured. |
| Speed | Not established | — | Only hypotheses about one model's own breakdown fired on this axis; none compares the models. |
| Cost | Not established | — | No cost mechanism beyond the price cards remains: every other hypothesis on this axis was ruled out. |

> [!TIP]
> **Conclusion.** Intelligence: Not established. Speed: Not established. Cost: Not established.

## What to run next

In the order to make them: the Overseer chat first (C), then the benchmark (B), then model selection (M). Appendix D gives each run's launcher settings.

| # | Run | Purpose | Models | Settles | Estimate |
|---|---|---|---|---|---|
| 1 | Replicate (recommended) | M, model selection | All 5 models | H0-Q.1, H0-Q.2, H0-Q.3, H0-Q.4, H0-Q.5, H0-W.1, H0-W.2, H0-S.1 and 7 more (Appendix D) | Not estimated |

### Checks

1. Check the rubric of Q3 against the source, and whether the tools' corpus holds the facts the question needs. *(rubric; settles HQ3.1)*

The runs are ordered chat first, then the benchmark, then model selection.

## Limitations and threats to validity

- The benchmark asks single-turn questions under one chat configuration: live chat carries the conversation before each question, and spoiler-free mode, web search and subagents are off, so a cause found here holds for that configuration only.
- It runs without the wiki text that live chat always gives the model before it answers, so a reading about retrieval or tool routing was measured under a condition live chat does not share.
- The hypotheses come from a fixed rule table over these runs; points at risk are upper bounds, and a decomposition rests on the questions both sides have figures for. The writer's own hypotheses are qualitative.
- Intervals: Item sampling only.
- The graders are AI models. Each grader's provider relation to the models is stated under Setup and method; a grader from a model's own provider may read that model more favorably.
- Paired tests: each measure's family is Holm-adjusted across the tests it makes over these models only.
- Costs are priced at today's catalog cards, as the comparison prices them.

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

### Compared models

| Letter | Model | Provider | Kind | Runs | Thinking level | Harness version | Run dates (UTC) | Price card |
|---|---|---|---|---|---|---|---|---|
| A | Orion Max (reference) | Northwind | Run | 1 | Not set | 41 | 2026-09-21 | — |
| B | Vega Pro | Southstar | Run | 1 | Not set | 41 | 2026-09-21 | — |
| C | Lyra Mini | Eastgate | Run | 1 | Not set | 41 | 2026-09-21 | — |
| D | Nova Lite | Westlake | Run | 1 | Not set | 41 | 2026-09-21 | — |
| E | Zeta Prime | Midland | Run | 1 | Not set | 41 | 2026-09-21 | — |

### Configuration differences

- **Configuration differences across the models:** No configuration key other than the model
- **Orion Max against Vega Pro: config differences:** Provider
- **Orion Max against Lyra Mini: config differences:** Provider
- **Orion Max against Nova Lite: config differences:** Provider
- **Orion Max against Zeta Prime: config differences:** Provider
- **Nova Lite against Zeta Prime: config differences:** Provider

### Failure profile

**Outcomes**

| Model | Provider | Answers | May mislead | Correct / partial / incorrect / not attempted | Wrong instead of abstaining | Level 2 or below: accuracy / completeness / conciseness / readability |
|---|---|---|---|---|---|---|
| Orion Max (reference) | Northwind | 6 | 0 (0 per 20) | — | — | 0 / 0 / 0 / 0 |
| Vega Pro | Southstar | 6 | 0 (0 per 20) | — | — | 0 / 0 / 0 / 0 |
| Lyra Mini | Eastgate | 6 | 1 (3.3 per 20) | — | — | 0 / 0 / 0 / 0 |
| Nova Lite | Westlake | 6 | 0 (0 per 20) | — | — | 0 / 0 / 0 / 0 |
| Zeta Prime | Midland | 6 | 0 (0 per 20) | — | — | 0 / 0 / 0 / 0 |

*Counted over each model's scored answers in all its runs; in a panel run a level is the mean of both panel members' levels. May mislead: an accuracy level below 4 or a critical error. Outcomes are recorded from scoring method 13. Low levels: answers at level 2 or below on each dimension.*

**Signals**

| Model | Provider | Truncated | Output defects | No tool call | Tool-limit hits | Median answer length | Isolated / shared misses | Lowest scorer |
|---|---|---|---|---|---|---|---|---|
| Orion Max (reference) | Northwind | 0 | 0 | 2 | 0 | 10 | 0 / 1 | 0 |
| Vega Pro | Southstar | 0 | 0 | 2 | 0 | 10 | 0 / 0 | 0 |
| Lyra Mini | Eastgate | 0 | 0 | 2 | 0 | 10 | 0 / 1 | 0 |
| Nova Lite | Westlake | 0 | 0 | 2 | 0 | 10 | 0 / 1 | 0 |
| Zeta Prime | Midland | 0 | 0 | 2 | 0 | 10 | 1 / 1 | 6 |

*Output defects: an empty, truncated or scrubbed answer, reasoning bleed, repeated fragments, or an answer that did not complete normally. Tool-limit hits: the tool budget or the iteration limit was reached. An isolated miss is a question the model scored 50 or less where the other models' median is 75 or more; a shared miss is one it scored below 50 along with at least half the others. Lowest scorer: questions where no other model scored lower. The counts say what the graders recorded, not why.*

### Grader findings by category

No run of these models recorded structured grader findings; they are recorded from harness 40.

### Panel agreement

No model was graded by an assessor panel in every run, so no panel agreement figures are stated.

- **Response-style conflict:** None

### Points at risk

| Model | Answers without tools | Budget, truncation or output defects | Confirmed critical errors | Other low-scoring answers | Total | Median answer |
|---|---|---|---|---|---|---|
| Orion Max | 9.8 | 0.0 | 0.0 | 0.8 | 10.7 | 89 |
| Vega Pro | 0.2 | 0.0 | 0.0 | 2.8 | 3.0 | 81 |
| Lyra Mini | 1.2 | 0.0 | 7.2 | 0.0 | 8.3 | 71 |
| Nova Lite | 6.3 | 0.0 | 0.0 | 2.0 | 8.3 | 70 |
| Zeta Prime | 0.0 | 0.0 | 0.0 | 5.6 | 5.6 | 54 |

*Index points at risk: the points lost on the answers showing each signal, relative to the model's own median answer, each answer counted once under its first signal. They are an upper bound, not a causal estimate.*

### Intelligence Index difference by dimension

| Pair | Difference | Accuracy | Completeness | Conciseness | Readability | Critical-error cap, difficulty weighting and spread |
|---|---|---|---|---|---|---|
| Orion Max vs Vega Pro | +5.0 | 0.0 | 0.0 | 0.0 | 0.0 | +5.0 |
| Orion Max vs Lyra Mini | +15.0 | +8.3 | +3.8 | +1.5 | +1.5 | -0.2 |
| Orion Max vs Nova Lite | +15.0 | +8.7 | +4.0 | +1.6 | +1.6 | -0.8 |
| Orion Max vs Zeta Prime | +30.0 | +16.5 | +7.5 | +3.0 | +3.0 | 0.0 |
| Nova Lite vs Zeta Prime | +15.0 | +7.8 | +3.5 | +1.4 | +1.4 | +0.8 |

*Index points, first model minus second, split by the scoring weights; the parts sum to the difference. Other is the critical-error cap and the difficulty weighting.*

### Paired tests

*Each pair is compared on the questions both models answered. Each measure is its own family of tests, Holm-adjusted across the tests it makes over these models; a result is established when its adjusted p-value is below 0.05. Another set of models gives another family and another adjustment.*

**Every pair**

Intelligence: Holm-adjusted across 10 tests.

| Pair | Questions | Difference (95% interval) | Adjusted p | Result |
|---|---|---|---|---|
| Orion Max vs Vega Pro | 6 | 0.0 points (-21.0 to +21.0) | 0.813 | Not established |
| Orion Max vs Lyra Mini | 6 | +15.2 points (+6.6 to +23.8) | 0.313 | Not established |
| Orion Max vs Nova Lite | 6 | +15.8 points (+5.6 to +26.1) | 0.313 | Not established |
| Orion Max vs Zeta Prime | 6 | +30.0 points (+16.1 to +43.9) | 0.313 | Not established |
| Vega Pro vs Lyra Mini | 6 | +15.2 points (+1.1 to +29.3) | 0.313 | Not established |
| Vega Pro vs Nova Lite | 6 | +15.8 points (+3.0 to +28.6) | 0.313 | Not established |
| Vega Pro vs Zeta Prime | 6 | +30.0 points (+21.0 to +39.0) | 0.313 | Not established |
| Lyra Mini vs Nova Lite | 6 | +0.7 points (-7.1 to +8.4) | 1.000 | Not established |
| Lyra Mini vs Zeta Prime | 6 | +14.8 points (+8.1 to +21.5) | 0.313 | Not established |
| Nova Lite vs Zeta Prime | 6 | +14.2 points (+5.8 to +22.5) | 0.313 | Not established |

*Difference: the mean per-question difference, first model minus second, on the questions both answered.*

Speed: Holm-adjusted across 10 tests. Cost: Not tested. No pair could be tested on this measure; each pair says why.

| Pair | Time ratio (95% interval) | Speed result | Spend ratio (95% interval) | Cost result |
|---|---|---|---|---|
| Orion Max vs Vega Pro | 0.83 (0.53 to 1.31) | Not established | — | Not tested |
| Orion Max vs Lyra Mini | 0.71 (0.70 to 0.73) | Not established | — | Not tested |
| Orion Max vs Nova Lite | 0.62 (0.40 to 0.98) | Not established | — | Not tested |
| Orion Max vs Zeta Prime | 0.56 (0.54 to 0.57) | Not established | — | Not tested |
| Vega Pro vs Lyra Mini | 0.86 (0.54 to 1.36) | Not established | — | Not tested |
| Vega Pro vs Nova Lite | 0.75 (0.74 to 0.76) | Not established | — | Not tested |
| Vega Pro vs Zeta Prime | 0.67 (0.42 to 1.06) | Not established | — | Not tested |
| Lyra Mini vs Nova Lite | 0.87 (0.55 to 1.38) | Not established | — | Not tested |
| Lyra Mini vs Zeta Prime | 0.78 (0.77 to 0.79) | Not established | — | Not tested |
| Nova Lite vs Zeta Prime | 0.89 (0.56 to 1.41) | Not established | — | Not tested |

*A ratio is the first model's own time or spend per question divided by the second's, on the questions both answered.*

*At least one entry has a single run. With one run a side, a paired test captures question sampling only, not run-to-run variation, so it can look more certain than it is.*

### Per-question matrix

| Q | Topic | Band | A | B | C | D | E | Spread |
|---|---|---|---|---|---|---|---|---|
| Q1 | An item question about gems | Intermediate | 92 | 85 | 70 | 75 | 50 | 42 |
| Q2 | — | Intermediate | 88 | 80 | 72 | 60 | 55 | 33 |
| Q3 | — | Intermediate | 30 | 70 | 28 CE | 32 | 25 | 45 |
| Q4 | — | Intermediate | 90 | 75 | 65 | 72 | 52 | 38 |
| Q5 | — | Intermediate | 85 | 82 | 74 | 68 | 58 | 27 |
| Q6 | — | Intermediate | 95 | 88 | 80 | 78 | 60 | 35 |

*Columns: A = Orion Max, B = Vega Pro, C = Lyra Mini, D = Nova Lite, E = Zeta Prime. CE marks a critical error; Spread is the highest score minus the lowest. — under a model marks a question it was not asked or not scored on. — under Topic marks a question not given in detail.*

## Appendix B: Speed and cost decompositions

### Calls, latency and output

| Model | Model calls per answer | Time to first token per call, median | Decode rate | Reasoning share | Output tokens per question |
|---|---|---|---|---|---|
| Orion Max | 2.5 | 0.8 s | — | 31% | 970 |
| Vega Pro | 2.5 | 1.0 s | — | 34% | 1,120 |
| Lyra Mini | 2.5 | 1.3 s | — | 36% | 1,270 |
| Nova Lite | 2.5 | 1.5 s | — | 38% | 1,420 |
| Zeta Prime | 2.5 | 1.8 s | — | 39% | 1,570 |

*Model calls per answer: the model is called again after each tool round (one cycle of calling a tool and reading its result). Decode rate: the median output tokens per second once the model has started writing. Reasoning share: the part of the output tokens spent on hidden thinking.*

### Tokens per question

| Model | Uncached input | Cache reads | Visible output | Reasoning | Cache-read share |
|---|---|---|---|---|---|
| Orion Max | 3,350 | 1,000 | 670 | 300 | 23% |
| Vega Pro | 3,650 | 1,200 | 740 | 380 | 25% |
| Lyra Mini | 3,950 | 1,400 | 810 | 460 | 26% |
| Nova Lite | 4,250 | 1,600 | 880 | 540 | 27% |
| Zeta Prime | 4,550 | 1,800 | 950 | 620 | 28% |

*Left out because it is zero or not recorded in every row: cache writes.*

### Speed and cost figures

| Model | Provider | Median answer time | Cost per question | Input tokens per question | Output tokens per question |
|---|---|---|---|---|---|
| Orion Max (reference) | Northwind | 12.0 s | $0.050 | 4,350 | 970 |
| Vega Pro | Southstar | 9.0 s | $0.030 | 4,850 | 1,120 |
| Lyra Mini | Eastgate | 15.0 s | $0.020 | 5,350 | 1,270 |
| Nova Lite | Westlake | 8.0 s | $0.060 | 5,850 | 1,420 |
| Zeta Prime | Midland | 20.0 s | $0.010 | 6,350 | 1,570 |

### Cost differences, decomposed

| Pair | Questions | Difference | Token volume | Price card | Leading component |
|---|---|---|---|---|---|
| Orion Max vs Vega Pro | 6 | -$0.0078 | 28% (27–29%) | 72% (71–73%) | Uncached input, price per token (45%) |
| Orion Max vs Lyra Mini | 6 | -$0.017 | 29% (29–30%) | 71% (70–71%) | Uncached input, price per token (44%) |
| Orion Max vs Nova Lite | 6 | -$0.027 | 30% (30–31%) | 70% (69–70%) | Uncached input, price per token (43%) |
| Orion Max vs Zeta Prime | 6 | -$0.038 | 31% (31–32%) | 69% (68–69%) | Uncached input, price per token (42%) |
| Nova Lite vs Zeta Prime | 6 | -$0.011 | 35% (35–36%) | 65% (64–65%) | Uncached input, price per token (39%) |

*Each difference is split into parts that add up exactly to it (a standard method, the logarithmic-mean Divisia index), using only the questions both models have figures for. A share's interval comes from resampling those questions (a bootstrap interval). Each share is of the difference, the first model's minus the second's: token volume, the price card and caching split the whole of it, caching only for a pair whose two sides bill different components; the leading component is the largest part. Left out because it is zero or not recorded in every row: caching.*

## Appendix C: Hypothesis details

Code raised each hypothesis from a rule fixed in advance and graded it. **Established (High)** needs all of these: it is measured; it is still clear after the correction for many comparisons; its null hypothesis is rejected; no confound (another difference between two models that could account for a result) is open; every robustness check (a test of whether a result survives a small change) passed; and each model ran at least twice per suite. **Probable (Moderate)**: exactly one of those conditions is missing. **Suggestive (Low)**: it is not measured, two or more conditions are missing, or the null hypothesis stands. **Ruled out (Not supported)**: the hypothesis's own test contradicts it. A null hypothesis reads **Rejected — a real difference** when a test found a real difference, and **Stands — may be chance** when none could be shown. Each entry gives the models, the locus and the grade, then the evidence for and against.

### Intelligence

- **HQ3.1: shared failures on the same questions** (Orion Max, Vega Pro, Lyra Mini, Nova Lite and Zeta Prime; locus suite / corpus / chat; Suggestive (Low))
  - *For:* Q3: 4 of 5 models scored below 50.
  - *Against:* None recorded.
  - *Grade reasons:* It is observed, not measured.
  - *Refuted if:* One model answered the question well.

| Hypothesis | Pair | Paired result | Grade |
|---|---|---|---|
| H0-Q.1 | Orion Max vs Vega Pro | 0.0 points, not established | Stands — may be chance (Low) |
| H0-Q.2 | Orion Max vs Lyra Mini | +15.2 points, not established | Stands — may be chance (Low) |
| H0-Q.3 | Orion Max vs Nova Lite | +15.8 points, not established | Stands — may be chance (Low) |
| H0-Q.4 | Orion Max vs Zeta Prime | +30.0 points, not established | Stands — may be chance (Low) |
| H0-Q.5 | Nova Lite vs Zeta Prime | +14.2 points, not established | Stands — may be chance (Low) |
| H0-W.1 | Within Orion Max | No contrast could be made. | Stands — may be chance (Low) |
| H0-W.2 | Within Zeta Prime | No contrast could be made. | Stands — may be chance (Low) |

### Speed

- **WS3.1: Orion Max's time spent on tool rounds** (Orion Max; locus model / chat; Probable (Moderate))
  - *For:* Across Orion Max's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95% interval 0.23 to 0.99, n 6).
  - *Against:* None recorded.
  - *Grade reasons:* See Robustness.
  - *Refuted if:* The ρ interval includes zero.
  - *Robustness:* Replicates failed (Orion Max ran only once per suite); Leave one question out passed (ρ stays at or above 0.5 without any one question).
- **WS3.2: Nova Lite's time spent on tool rounds** (Nova Lite; locus model / chat; Probable (Moderate))
  - *For:* Across Nova Lite's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95% interval 0.23 to 0.99, n 6).
  - *Against:* None recorded.
  - *Grade reasons:* See Robustness.
  - *Refuted if:* The ρ interval includes zero.
  - *Robustness:* Replicates failed (Nova Lite ran only once per suite); Leave one question out passed (ρ stays at or above 0.5 without any one question).
- **WS3.3: Zeta Prime's time spent on tool rounds** (Zeta Prime; locus model / chat; Probable (Moderate))
  - *For:* Across Zeta Prime's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95% interval 0.23 to 0.99, n 6).
  - *Against:* None recorded.
  - *Grade reasons:* See Robustness.
  - *Refuted if:* The ρ interval includes zero.
  - *Robustness:* Replicates failed (Zeta Prime ran only once per suite); Leave one question out passed (ρ stays at or above 0.5 without any one question).
- **WS2.1: Zeta Prime's time spent generating output** (Zeta Prime; locus model; Ruled out (Not supported))
  - *For:* Output generation is 65% (64–66%) of model time per question.
  - *Against:* Reasoning is 39% of Zeta Prime's output tokens.
  - *Grade reasons:* Its own test rules it out: reasoning is 39% of Zeta Prime's output tokens.
  - *Refuted if:* The reasoning share is under one half.
  - *Robustness:* Replicates failed (Zeta Prime ran only once per suite); Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most).
- **WS2.2: Nova Lite's time spent generating output** (Nova Lite; locus model; Ruled out (Not supported))
  - *For:* Output generation is 66% (65–67%) of model time per question.
  - *Against:* Reasoning is 38% of Nova Lite's output tokens.
  - *Grade reasons:* Its own test rules it out: reasoning is 38% of Nova Lite's output tokens.
  - *Refuted if:* The reasoning share is under one half.
  - *Robustness:* Replicates failed (Nova Lite ran only once per suite); Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most).
- **WS2.3: Orion Max's time spent generating output** (Orion Max; locus model; Ruled out (Not supported))
  - *For:* Output generation is 70% (69–71%) of model time per question.
  - *Against:* Reasoning is 31% of Orion Max's output tokens.
  - *Grade reasons:* Its own test rules it out: reasoning is 31% of Orion Max's output tokens.
  - *Refuted if:* The reasoning share is under one half.
  - *Robustness:* Replicates failed (Orion Max ran only once per suite); Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most).

| Hypothesis | Pair | Paired result | Grade |
|---|---|---|---|
| H0-S.1 | Orion Max vs Vega Pro | 0.83 times as long, not established | Stands — may be chance (Low) |
| H0-S.2 | Orion Max vs Lyra Mini | 0.71 times as long, not established | Stands — may be chance (Low) |
| H0-S.3 | Orion Max vs Nova Lite | 0.62 times as long, not established | Stands — may be chance (Low) |
| H0-S.4 | Orion Max vs Zeta Prime | 0.56 times as long, not established | Stands — may be chance (Low) |
| H0-S.5 | Nova Lite vs Zeta Prime | 0.89 times as long, not established | Stands — may be chance (Low) |

### Cost

- **WC1.1: Zeta Prime's output cost** (Zeta Prime; locus model; Probable (Moderate))
  - *For:* Visible output and reasoning are 54% of Zeta Prime's cost per question.
  - *Against:* None recorded.
  - *Grade reasons:* See Robustness.
  - *Refuted if:* Another component is larger.
  - *Robustness:* Replicates failed (Zeta Prime ran only once per suite).
- **WC1.2: Nova Lite's output cost** (Nova Lite; locus model; Probable (Moderate))
  - *For:* Visible output and reasoning are 55% of Nova Lite's cost per question.
  - *Against:* None recorded.
  - *Grade reasons:* See Robustness.
  - *Refuted if:* Another component is larger.
  - *Robustness:* Replicates failed (Nova Lite ran only once per suite).
- **WC1.3: Orion Max's output cost** (Orion Max; locus model; Probable (Moderate))
  - *For:* Visible output and reasoning are 69% of Orion Max's cost per question.
  - *Against:* None recorded.
  - *Grade reasons:* See Robustness.
  - *Refuted if:* Another component is larger.
  - *Robustness:* Replicates failed (Orion Max ran only once per suite).
- **HC4.1–HC4.4** (4 pairs; locus chat / provider): Weak prompt caching raises the first model's cost against the second model.
  - *Refuted if:* The cache-read share is comparable to the other models'.
  - *Robustness:* Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most); Replicates failed (Both models ran only once per suite); Configuration passed (No configuration key differs beyond the model).

| Hypothesis | Pair | Share (95% interval) | Grade | Settled by |
|---|---|---|---|---|
| HC4.1 | Zeta Prime vs Orion Max | 9% (9–9%) | Ruled out (Not supported) | — |
| HC4.2 | Nova Lite vs Orion Max | 8% (8–9%) | Ruled out (Not supported) | — |
| HC4.3 | Zeta Prime vs Nova Lite | 12% (12–13%) | Ruled out (Not supported) | — |
| HC4.4 | Lyra Mini vs Orion Max | 8% (7–8%) | Ruled out (Not supported) | — |

- **HC4.1**
  - *For:* Uncached input, token volume accounts for 9% (9–9%) of the $0.038 per-question cost difference.
  - *Against:* Zeta Prime's cache-read share is 28% against the models' median of 26%.
  - *Grade reasons:* Its own test rules it out: Zeta Prime's cache-read share is 28% against the models' median of 26%.
- **HC4.2**
  - *For:* Uncached input, token volume accounts for 8% (8–9%) of the $0.027 per-question cost difference.
  - *Against:* Nova Lite's cache-read share is 27% against the models' median of 26%.
  - *Grade reasons:* Its own test rules it out: Nova Lite's cache-read share is 27% against the models' median of 26%.
- **HC4.3**
  - *For:* Uncached input, token volume accounts for 12% (12–13%) of the $0.011 per-question cost difference.
  - *Against:* Zeta Prime's cache-read share is 28% against the models' median of 26%.
  - *Grade reasons:* Its own test rules it out: Zeta Prime's cache-read share is 28% against the models' median of 26%.
- **HC4.4**
  - *For:* Uncached input, token volume accounts for 8% (7–8%) of the $0.017 per-question cost difference.
  - *Against:* Lyra Mini's cache-read share is 26% against the models' median of 26%.
  - *Grade reasons:* Its own test rules it out: Lyra Mini's cache-read share is 26% against the models' median of 26%.

- **HC2.1–HC2.5** (5 pairs; locus model): The first model costs more than the second model mainly from its price per token.
  - *Grade reasons:* Every condition of the grade is met.
  - *Refuted if:* Not testable by a run: the share follows from the price cards and these runs' token counts.
  - *Robustness:* Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most); Configuration passed (No configuration key differs beyond the model).

| Hypothesis | Pair | Share (95% interval) | Grade | Settled by |
|---|---|---|---|---|
| HC2.1 | Zeta Prime vs Orion Max | 69% (68–69%) | Known from the price cards (High) | — |
| HC2.2 | Nova Lite vs Orion Max | 70% (69–70%) | Known from the price cards (High) | — |
| HC2.3 | Lyra Mini vs Orion Max | 71% (70–71%) | Known from the price cards (High) | — |
| HC2.4 | Zeta Prime vs Nova Lite | 65% (64–65%) | Known from the price cards (High) | — |
| HC2.5 | Vega Pro vs Orion Max | 72% (71–73%) | Known from the price cards (High) | — |

- **HC2.1**
  - *For:* Price per token, all components accounts for 69% (68–69%) of the $0.038 per-question cost difference.
  - *Against:* The volume effect is 31% (31–32%) of the difference.
- **HC2.2**
  - *For:* Price per token, all components accounts for 70% (69–70%) of the $0.027 per-question cost difference.
  - *Against:* The volume effect is 30% (30–31%) of the difference.
- **HC2.3**
  - *For:* Price per token, all components accounts for 71% (70–71%) of the $0.017 per-question cost difference.
  - *Against:* The volume effect is 29% (29–30%) of the difference.
- **HC2.4**
  - *For:* Price per token, all components accounts for 65% (64–65%) of the $0.011 per-question cost difference.
  - *Against:* The volume effect is 35% (35–36%) of the difference.
- **HC2.5**
  - *For:* Price per token, all components accounts for 72% (71–73%) of the $0.0078 per-question cost difference.
  - *Against:* The volume effect is 28% (27–29%) of the difference.

| Hypothesis | Pair | Paired result | Grade |
|---|---|---|---|
| H0-C.1 | Orion Max vs Vega Pro | No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted). | Rejected — a real difference (Not supported) |
| H0-C.2 | Orion Max vs Lyra Mini | No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted). | Rejected — a real difference (Not supported) |
| H0-C.3 | Orion Max vs Nova Lite | No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted). | Rejected — a real difference (Not supported) |
| H0-C.4 | Orion Max vs Zeta Prime | No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted). | Rejected — a real difference (Not supported) |
| H0-C.5 | Nova Lite vs Zeta Prime | No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted). | Rejected — a real difference (Not supported) |

## Appendix D: Run settings

Each run's settings are the launcher's, copied from the analyzed runs unless its kind changes them; a setting the runs did not share is left for you to choose.

### Run 1: Replicate (M, model selection, recommended)

- **Decides:**
  - Confirms H0-Q.1, H0-Q.2, H0-Q.3, H0-Q.4 and H0-Q.5 if the paired test still finds no difference with three runs per model; refutes each if a paired test of the two on the Intelligence Index is established after adjustment.
  - Confirms H0-W.1 and H0-W.2 if the contrasts still find no difference with three runs per model; refutes each if a Mann–Whitney contrast is established after the Holm adjustment.
  - Confirms H0-S.1, H0-S.2, H0-S.3, H0-S.4 and H0-S.5 if the paired test still finds no difference with three runs per model; refutes each if a paired test of the two on the model time is established after adjustment.
  - Confirms WS3.1, WS3.2 and WS3.3 if model time keeps rising with the calls per answer; refutes each if the ρ interval includes zero.
- **Predicted comparability:** Model comparison with the analyzed runs; a replicate (Tier A) of each model's own runs
- **Estimate:** Not estimated
- **Settles:** H0-Q.1, H0-Q.2, H0-Q.3, H0-Q.4, H0-Q.5, H0-W.1, H0-W.2, H0-S.1, H0-S.2, H0-S.3, H0-S.4, H0-S.5, WS3.1, WS3.2, WS3.3

| Launcher field | Setting |
|---|---|
| Models | Model batch |
| Models Under Test | Orion Max, Vega Pro, Lyra Mini, Nova Lite, Zeta Prime |
| Run Target | Single suite |
| Benchmark Suite | GnollHack Core Suite |
| Scoring Profile | Not shared by the analyzed runs: choose it |
| Response Style | Concise — production default |
| Source Code References | Allowed — answers cite source files and lines |
| Assessor | Not shared by the analyzed runs: choose it |
| Co-Assessor | None |
| Second Reader | None |
| Coverage | Never |
| Claim Verifier | None |
| Report Writer | None |
| Runs per model | 3 |
| Model order | Randomized (recommended) |

- The estimate for a model without analyzed runs is the analyzed models' mean.

## Appendix E: Known facts

The counts and the configuration facts the analysis states, each numbered as the writer cites it. Its tests and decompositions are given once, under the evidence and in Appendices A and B.

- **6.** Orion Max answered 2 of 6 scored questions without a tool call.
- **7.** Vega Pro answered 2 of 6 scored questions without a tool call.
- **8.** Lyra Mini answered 2 of 6 scored questions without a tool call.
- **9.** Lyra Mini has 1 critical error in 6 scored answers.
- **10.** Nova Lite answered 2 of 6 scored questions without a tool call.
- **11.** Zeta Prime answered 2 of 6 scored questions without a tool call.

## Appendix F: Reproducibility

- **All models:** System prompt SHA-256 prefix `e9b3e9a7c4d1`; tool guides SHA-256 prefix `f59d8b30a1c7`
- **Orion Max** (Northwind orion-max, thinking level not set): Run IDs 31; run dates 2026-09-21
- **Vega Pro** (Southstar vega-pro, thinking level not set): Run IDs 32; run dates 2026-09-21
- **Lyra Mini** (Eastgate lyra-mini, thinking level not set): Run IDs 33; run dates 2026-09-21
- **Nova Lite** (Westlake nova-lite, thinking level not set): Run IDs 34; run dates 2026-09-21
- **Zeta Prime** (Midland zeta-prime, thinking level not set): Run IDs 35; run dates 2026-09-21
- **Grader models:** Assessor: Google gemini-3.8-flash
- **Comparability signature:** `sig-7f3a91`
- **Pricing basis:** Catalog prices on 2026-09-21

## Appendix G: Question details

*Each answer shown is one the writer was given as an excerpt. The excerpts were given in rounds, one per question in each round and at most three per question, taking the suites in turn: the questions with critical errors or refuted sentences first, then the widest spread between models, then those every model scored low; within a question, a critical error or refuted sentence first, then the lowest score. Rubrics are shown without their source paragraphs. Each answer gives its graders' scores on one line and every critical error with the grader that flagged it; the panel members' comments and notes are shown only for a panel disagreement (scores more than 15 points apart, or a critical error only one member flagged), and the reference reader's comment only when its score is more than 15 points from the panel mean.*

### Q3

**Question:**

> Question 203

**Rubric:**

> - Rubric 203

#### Orion Max, run 31

**Answer:**

> Answer 203

**Graders:** Assessor 30.

**Claim verifier:**

- No claims were checked.

#### Lyra Mini, run 33

**Answer:**

> Answer 203

**Graders:** Assessor 28.

- **Critical error (assessor):** "Answer 203"

**Claim verifier:**

- No claims were checked.

#### Zeta Prime, run 35

**Answer:**

> Answer 203

**Graders:** Assessor 25.

**Claim verifier:**

- No claims were checked.

### Q1: An item question about gems

**Question:**

> Question 201

**Rubric:**

> - Rubric 201

#### Lyra Mini, run 33

**Answer:**

> Answer 201

**Graders:** Assessor 70.

**Claim verifier:**

- No claims were checked.

#### Nova Lite, run 34

**Answer:**

> Answer 201

**Graders:** Assessor 75.

**Claim verifier:**

- No claims were checked.

#### Zeta Prime, run 35

**Answer:**

> Answer 201

**Graders:** Assessor 50.

**Claim verifier:**

- No claims were checked.

## Appendix H: Glossary

- **95% interval:** A range built so that, if the questions were drawn again and again, 95 of 100 such ranges would contain the true value. When two models' ranges overlap, the ranges alone cannot say which is higher.
- **Answer quality:** The score of one answer, from 0 to 100. It combines the four dimension scores in a weighted geometric mean and is capped when the answer has a critical error.
- **Answers without tools:** Answers for which the model called no tool, such as the wiki search or the source code search, and answered from its own knowledge alone.
- **Bootstrap interval:** An interval found by redrawing the questions at random, with repeats, many times and recomputing the figure each time. The middle 95 of every 100 recomputed figures form the interval.
- **Confound:** A setting other than the model itself that differs between the two models a hypothesis compares, such as the response style. While it is open, its effect cannot be told apart from the model's.
- **Critical error:** A mistake the graders judge serious, such as a wrong game rule stated as fact. It caps the answer's quality, whatever the answer's other scores.
- **Decomposition share:** The part of a time or cost difference that one component accounts for, found by a standard method (the logarithmic-mean Divisia index). The parts always add up to the whole difference, so one part can be larger than the whole when another pulls the other way: a 2 s gap can be 3 s from one part and −1 s from another.
- **Difficulty-weighted mean:** The average of answer quality over the questions, with harder questions counting for more. The Intelligence Index is computed this way.
- **Dimension:** One of the four qualities each answer is graded on: Accuracy, Completeness, Conciseness and Readability. Each has its own weight in the answer's quality.
- **Grade words:** Established (High) means measured, still clear after the correction for many comparisons, and robust in every check, with no open confound and two or more runs per suite. Probable (Moderate) misses exactly one of those conditions. Suggestive (Low) is a lead only. Ruled out (Not supported) means the hypothesis's own test contradicts it.
- **Holm adjustment:** When many pairs are compared at once, some differences look real by chance. The Holm adjustment raises the bar for each comparison, so the whole set keeps at most a one-in-twenty chance of any false finding.
- **Hypothesis:** A possible cause of a difference, raised by a rule fixed in advance and tested by code against the data. Each hypothesis states what would refute it and receives a grade.
- **Intelligence Index:** A model's overall score for answer quality, from 0 to 100. It is the mean quality of the model's answers, with harder questions counting for more.
- **Isolated miss:** A question a model scored 50 or less on while the other models' median score was 75 or more.
- **Leave-one-out check:** A robustness check that repeats a result with each question removed in turn. If the result rests on a single question, the check fails.
- **Locus:** Where a hypothesis places the cause: the model itself; the Overseer chat prompt or its tools (chat); the grading (harness); a question or its rubric (suite); the data a tool reads (corpus); or the serving (provider).
- **Median and mean:** The median is the middle value once all values are sorted, so a few very slow or costly questions barely move it. The mean is the plain average, which they do move.
- **Null hypothesis:** The starting assumption that a difference, between two models or between two groups of questions, is only chance. A paired test, or else a bootstrap interval that excludes zero, can reject it. A null that stands does not prove that the two are equal.
- **Output generation and decode rate:** Output generation is the time the model spends writing its reply once it has started. The decode rate is how fast it writes, in tokens per second.
- **p-value:** The chance of a difference at least this large if the two models were really equal. After the Holm adjustment, a p-value below 0.05 counts as a real difference.
- **Paired test:** A comparison of two models question by question, on the questions both answered, so that how hard each question is counts the same for both.
- **Points at risk:** The index points a model lost on its answers below its own median answer, each answer counted once under its first signal, such as no tool call, a defect or a critical error. They are an upper bound on what each signal costs, not an estimate of its effect.
- **Pricing basis:** The prices per token used to turn token counts into dollars: today's catalog prices, or the prices stored with each run. Costs on different bases are not comparable.
- **Reference model:** The model each other model is paired-tested against: the one chosen for the comparison, or else the model with the highest Intelligence Index. It is also called the baseline model.
- **Replicate run:** Running the same model on the same questions again. With only one run per suite, the document cannot tell whether a result would repeat, which caps every grade at Probable.
- **Response style:** The production chat asks the model for concise answers; a run can ask for detailed ones instead. Completeness is graded under the style the run used, and only a pair of runs in both styles shows how much the style matters.
- **Robustness check:** A test of whether a hypothesis still holds when the data changes a little: one question left out, another suite, a second run, the other panel member, or another setting.
- **Runs per suite:** How many times a model answered every question of a suite. With one run per suite, the data cannot show whether a result would repeat.
- **Scoring levels:** Each dimension is graded on a fixed scale of levels, each described by the answer behavior that earns it (behaviorally anchored levels). Each level stands for a fixed score.
- **Suite:** A fixed set of benchmark questions, each with its rubric. A run answers every question of one suite once.
- **Tied rank:** When a model's 95% interval overlaps others', the data cannot fix its place, so its rank is the span of places the interval allows, such as 5–9 (tied). The span starts one place below the models whose intervals lie wholly above its own and ends one place above those whose intervals lie wholly below it.
- **Time ratio and spend ratio:** One model's own time, or spend, per question divided by the other's, on the questions both answered. A ratio of 1.50 means half as much again, and 0.50 means half.
- **Time to first token:** The seconds from sending a request to the first word of the reply. For reasoning models it is mostly the time the model spends thinking before it answers.
- **Tokens:** The pieces of text, roughly three quarters of a word each, that a model reads and writes; providers price by them. Uncached input is text read at the full price. Cache reads are input stored from an earlier call and charged less; cache writes store it. Visible output is the answer, and reasoning is the model's hidden thinking, billed as output; a provider that does not report it separately counts it inside output.
- **Tool round:** One cycle in which the model calls a tool, such as the wiki search, reads its result and continues. Each round adds a model call, with its own wait for the first token.
- **Weighted geometric mean:** The way an answer's four dimension scores are combined. Unlike a plain average, it falls sharply when any one dimension is very low, so strong dimensions cannot hide a weak one.

## Fact sheet

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
          "display": "0 of 6 answers with a critical error",
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
      "lastRunUtc": "2026-09-21T07:08:00Z",
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
          "display": "0 of 6 answers with a critical error",
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
      "lastRunUtc": "2026-09-21T08:10:00Z",
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
          "display": "1 of 6 answers with a critical error",
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
      "lastRunUtc": "2026-09-21T09:12:00Z",
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
          "display": "0 of 6 answers with a critical error",
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
      "lastRunUtc": "2026-09-21T10:14:00Z",
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
          "display": "0 of 6 answers with a critical error",
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
      "lastRunUtc": "2026-09-21T11:16:00Z",
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
      "display": "no configuration key other than the model",
      "key": "comparison.configDifferences",
      "value": 0
    },
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
      "display": "Model A, the highest Intelligence Index",
      "key": "comparison.reference",
      "value": "A"
    },
    {
      "available": true,
      "display": "1 run per suite",
      "key": "comparison.runsPerSuite",
      "value": 1
    },
    {
      "available": true,
      "display": "sig-7f3a91",
      "key": "comparison.signature",
      "value": "sig-7f3a91"
    },
    {
      "available": true,
      "display": "1 suite",
      "key": "comparison.suiteCount",
      "value": 1
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
      "display": "not set",
      "key": "model.A.config.maxOutputTokens"
    },
    {
      "available": true,
      "display": "Enabled",
      "key": "model.A.config.parallelMode",
      "value": "Enabled"
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.A.config.reasoningMode"
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.A.config.serviceTier"
    },
    {
      "available": true,
      "display": "Current",
      "key": "model.A.cost.basis",
      "value": "Current"
    },
    {
      "available": true,
      "display": "$0.0001",
      "key": "model.A.cost.component.cacheRead.perQuestion",
      "value": 0.000125
    },
    {
      "available": true,
      "display": "$0.0000",
      "key": "model.A.cost.component.cacheWrite.perQuestion",
      "value": 0
    },
    {
      "available": true,
      "display": "$0.0067",
      "key": "model.A.cost.component.output.perQuestion",
      "value": 0.0067
    },
    {
      "available": true,
      "display": "$0.0030",
      "key": "model.A.cost.component.reasoning.perQuestion",
      "value": 0.0029999999999999996
    },
    {
      "available": true,
      "display": "$0.0042",
      "key": "model.A.cost.component.uncachedInput.perQuestion",
      "value": 0.0041875
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
      "display": "0 of 6 answers with a critical error",
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
      "available": true,
      "display": "0% (95% interval 0%–39%); before scoring method 13, flagged rather than confirmed",
      "key": "model.A.outcome.confirmedCriticalRate",
      "value": 0
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
      "display": "$0.125 per million tokens",
      "key": "model.A.price.cacheRead.perMillion",
      "value": 0.125
    },
    {
      "available": true,
      "display": "$1.50 per million tokens",
      "key": "model.A.price.cacheWrite.perMillion",
      "value": 1.5
    },
    {
      "available": true,
      "display": "$10.00 per million tokens",
      "key": "model.A.price.output.perMillion",
      "value": 10
    },
    {
      "available": true,
      "display": "$10.00 per million tokens",
      "key": "model.A.price.reasoning.perMillion",
      "value": 10
    },
    {
      "available": true,
      "display": "$1.25 per million tokens",
      "key": "model.A.price.uncachedInput.perMillion",
      "value": 1.25
    },
    {
      "available": true,
      "display": "10 characters",
      "key": "model.A.profile.answerChars.median",
      "value": 10
    },
    {
      "available": true,
      "display": "6 answers",
      "key": "model.A.profile.answers",
      "value": 6
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on accuracy",
      "key": "model.A.profile.deficit.accuracy",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on completeness",
      "key": "model.A.profile.deficit.completeness",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on conciseness",
      "key": "model.A.profile.deficit.conciseness",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on readability",
      "key": "model.A.profile.deficit.readability",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.profile.findings.accuracy",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.profile.findings.completeness",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.profile.findings.conciseness",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.profile.findings.critical_error",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.profile.findings.other",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.profile.findings.readability",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": true,
      "display": "0 of 1 run with structured grader findings",
      "key": "model.A.profile.findings.runsWithSynthesis",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.profile.findings.tool_use",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": true,
      "display": "0 of 6 questions this model missed while the others scored well",
      "key": "model.A.profile.isolatedMisses",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 questions on which this model scored lowest",
      "key": "model.A.profile.lowestScorer",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers that may mislead",
      "key": "model.A.profile.mayMislead",
      "value": 0
    },
    {
      "available": true,
      "display": "0 in 20 answers",
      "key": "model.A.profile.mayMisleadPer20",
      "value": 0
    },
    {
      "available": true,
      "display": "2 of 6 answers without a tool call",
      "key": "model.A.profile.noToolAnswers",
      "value": 2
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.profile.outcome.correct",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.profile.outcome.incorrect",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.profile.outcome.noAnswer",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.profile.outcome.notAttempted",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.profile.outcome.partial",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.profile.outcome.wrongInsteadOfAbstaining",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": true,
      "display": "0 of 6 answers with output defects",
      "key": "model.A.profile.outputDefects",
      "value": 0
    },
    {
      "available": true,
      "display": "1 of 6 questions missed by this model and at least half the others",
      "key": "model.A.profile.sharedMisses",
      "value": 1
    },
    {
      "available": true,
      "display": "0 of 6 answers that hit the tool limit",
      "key": "model.A.profile.toolLimitHits",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers that were truncated",
      "key": "model.A.profile.truncated",
      "value": 0
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
      "display": "1–2 of 5 (tied: intervals overlap)",
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
      "display": "1 of 1 question with a scored answer",
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
      "display": "89 (4 answers)",
      "key": "model.A.score.withTools",
      "value": 88.75
    },
    {
      "available": true,
      "display": "63 (2 answers)",
      "key": "model.A.score.withoutTools",
      "value": 62.5
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
      "display": "Accuracy 55%, Completeness 25%, Conciseness 10%, Readability 10%",
      "key": "model.A.scoring.weights"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.servedTier",
      "unavailableReason": "No answer recorded the tier it was served at."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.speed.decodeRate",
      "unavailableReason": "No answer's final call recorded a measurable decode rate."
    },
    {
      "available": true,
      "display": "2.5",
      "key": "model.A.speed.modelCallsPerAnswer",
      "value": 2.5
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
      "display": "0% of model time",
      "key": "model.A.speed.ownWaitShare",
      "value": 0
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
      "key": "model.A.speed.retriesPerAnswer",
      "unavailableReason": "No answer recorded its retried attempts."
    },
    {
      "available": true,
      "display": "every answer served at the requested tier",
      "key": "model.A.speed.servedTierMismatch",
      "value": 0
    },
    {
      "available": true,
      "display": "1.5 s",
      "key": "model.A.speed.toolTimeP50",
      "value": 1500
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.speed.ttftP50",
      "unavailableReason": "Not recorded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.A.speed.ttftP90",
      "unavailableReason": "No answer recorded a time to first token."
    },
    {
      "available": true,
      "display": "0.8 s",
      "key": "model.A.speed.ttftPerCallP50",
      "value": 760
    },
    {
      "available": true,
      "display": "Comparable",
      "key": "model.A.state",
      "value": "Comparable"
    },
    {
      "available": true,
      "display": "no response-style conflict",
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
      "available": true,
      "display": "23%",
      "key": "model.A.tokens.cacheReadShare",
      "value": 0.22988505747126436
    },
    {
      "available": true,
      "display": "4,350",
      "key": "model.A.tokens.inputPerQuestion",
      "value": 4350
    },
    {
      "available": true,
      "display": "970",
      "key": "model.A.tokens.outputPerQuestion",
      "value": 970
    },
    {
      "available": true,
      "display": "300",
      "key": "model.A.tokens.reasoningPerQuestion",
      "value": 300
    },
    {
      "available": true,
      "display": "31% of output tokens",
      "key": "model.A.tokens.reasoningShare",
      "value": 0.30927835051546393
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
      "display": "not set",
      "key": "model.B.config.maxOutputTokens"
    },
    {
      "available": true,
      "display": "Enabled",
      "key": "model.B.config.parallelMode",
      "value": "Enabled"
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.B.config.reasoningMode"
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.B.config.serviceTier"
    },
    {
      "available": true,
      "display": "Current",
      "key": "model.B.cost.basis",
      "value": "Current"
    },
    {
      "available": true,
      "display": "$0.0001",
      "key": "model.B.cost.component.cacheRead.perQuestion",
      "value": 0.00015
    },
    {
      "available": true,
      "display": "$0.0000",
      "key": "model.B.cost.component.cacheWrite.perQuestion",
      "value": 0
    },
    {
      "available": true,
      "display": "$0.0089",
      "key": "model.B.cost.component.output.perQuestion",
      "value": 0.008879999999999999
    },
    {
      "available": true,
      "display": "$0.0046",
      "key": "model.B.cost.component.reasoning.perQuestion",
      "value": 0.004560000000000001
    },
    {
      "available": true,
      "display": "$0.0082",
      "key": "model.B.cost.component.uncachedInput.perQuestion",
      "value": 0.0082125
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
      "display": "0 of 6 answers with a critical error",
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
      "available": true,
      "display": "0% (95% interval 0%–39%); before scoring method 13, flagged rather than confirmed",
      "key": "model.B.outcome.confirmedCriticalRate",
      "value": 0
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
      "display": "$0.125 per million tokens",
      "key": "model.B.price.cacheRead.perMillion",
      "value": 0.125
    },
    {
      "available": true,
      "display": "$1.50 per million tokens",
      "key": "model.B.price.cacheWrite.perMillion",
      "value": 1.5
    },
    {
      "available": true,
      "display": "$12.00 per million tokens",
      "key": "model.B.price.output.perMillion",
      "value": 12
    },
    {
      "available": true,
      "display": "$12.00 per million tokens",
      "key": "model.B.price.reasoning.perMillion",
      "value": 12
    },
    {
      "available": true,
      "display": "$2.25 per million tokens",
      "key": "model.B.price.uncachedInput.perMillion",
      "value": 2.25
    },
    {
      "available": true,
      "display": "10 characters",
      "key": "model.B.profile.answerChars.median",
      "value": 10
    },
    {
      "available": true,
      "display": "6 answers",
      "key": "model.B.profile.answers",
      "value": 6
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on accuracy",
      "key": "model.B.profile.deficit.accuracy",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on completeness",
      "key": "model.B.profile.deficit.completeness",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on conciseness",
      "key": "model.B.profile.deficit.conciseness",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on readability",
      "key": "model.B.profile.deficit.readability",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.profile.findings.accuracy",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.profile.findings.completeness",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.profile.findings.conciseness",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.profile.findings.critical_error",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.profile.findings.other",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.profile.findings.readability",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": true,
      "display": "0 of 1 run with structured grader findings",
      "key": "model.B.profile.findings.runsWithSynthesis",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.profile.findings.tool_use",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": true,
      "display": "0 of 6 questions this model missed while the others scored well",
      "key": "model.B.profile.isolatedMisses",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 questions on which this model scored lowest",
      "key": "model.B.profile.lowestScorer",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers that may mislead",
      "key": "model.B.profile.mayMislead",
      "value": 0
    },
    {
      "available": true,
      "display": "0 in 20 answers",
      "key": "model.B.profile.mayMisleadPer20",
      "value": 0
    },
    {
      "available": true,
      "display": "2 of 6 answers without a tool call",
      "key": "model.B.profile.noToolAnswers",
      "value": 2
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.profile.outcome.correct",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.profile.outcome.incorrect",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.profile.outcome.noAnswer",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.profile.outcome.notAttempted",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.profile.outcome.partial",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.profile.outcome.wrongInsteadOfAbstaining",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": true,
      "display": "0 of 6 answers with output defects",
      "key": "model.B.profile.outputDefects",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 questions missed by this model and at least half the others",
      "key": "model.B.profile.sharedMisses",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers that hit the tool limit",
      "key": "model.B.profile.toolLimitHits",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers that were truncated",
      "key": "model.B.profile.truncated",
      "value": 0
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
      "display": "1–2 of 5 (tied: intervals overlap)",
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
      "display": "1 of 1 question with a scored answer",
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
      "display": "80 (4 answers)",
      "key": "model.B.score.withTools",
      "value": 79.5
    },
    {
      "available": true,
      "display": "81 (2 answers)",
      "key": "model.B.score.withoutTools",
      "value": 81
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
      "display": "Accuracy 55%, Completeness 25%, Conciseness 10%, Readability 10%",
      "key": "model.B.scoring.weights"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.servedTier",
      "unavailableReason": "No answer recorded the tier it was served at."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.speed.decodeRate",
      "unavailableReason": "No answer's final call recorded a measurable decode rate."
    },
    {
      "available": true,
      "display": "2.5",
      "key": "model.B.speed.modelCallsPerAnswer",
      "value": 2.5
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
      "display": "0% of model time",
      "key": "model.B.speed.ownWaitShare",
      "value": 0
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
      "key": "model.B.speed.retriesPerAnswer",
      "unavailableReason": "No answer recorded its retried attempts."
    },
    {
      "available": true,
      "display": "every answer served at the requested tier",
      "key": "model.B.speed.servedTierMismatch",
      "value": 0
    },
    {
      "available": true,
      "display": "1.5 s",
      "key": "model.B.speed.toolTimeP50",
      "value": 1500
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.speed.ttftP50",
      "unavailableReason": "Not recorded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.B.speed.ttftP90",
      "unavailableReason": "No answer recorded a time to first token."
    },
    {
      "available": true,
      "display": "1.0 s",
      "key": "model.B.speed.ttftPerCallP50",
      "value": 1010
    },
    {
      "available": true,
      "display": "Comparable",
      "key": "model.B.state",
      "value": "Comparable"
    },
    {
      "available": true,
      "display": "no response-style conflict",
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
      "available": true,
      "display": "25%",
      "key": "model.B.tokens.cacheReadShare",
      "value": 0.24742268041237114
    },
    {
      "available": true,
      "display": "4,850",
      "key": "model.B.tokens.inputPerQuestion",
      "value": 4850
    },
    {
      "available": true,
      "display": "1,120",
      "key": "model.B.tokens.outputPerQuestion",
      "value": 1120
    },
    {
      "available": true,
      "display": "380",
      "key": "model.B.tokens.reasoningPerQuestion",
      "value": 380
    },
    {
      "available": true,
      "display": "34% of output tokens",
      "key": "model.B.tokens.reasoningShare",
      "value": 0.3392857142857143
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
      "display": "1 of 6 questions scored more than 15 points above the reference",
      "key": "model.B.vsReference.better",
      "value": 1
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "model.B.vsReference.shared",
      "value": 6
    },
    {
      "available": true,
      "display": "5 of 6 questions scored within 15 points of the reference",
      "key": "model.B.vsReference.similar",
      "value": 5
    },
    {
      "available": true,
      "display": "0 of 6 questions scored more than 15 points below the reference",
      "key": "model.B.vsReference.worse",
      "value": 0
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
      "display": "not set",
      "key": "model.C.config.maxOutputTokens"
    },
    {
      "available": true,
      "display": "Enabled",
      "key": "model.C.config.parallelMode",
      "value": "Enabled"
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.C.config.reasoningMode"
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.C.config.serviceTier"
    },
    {
      "available": true,
      "display": "Current",
      "key": "model.C.cost.basis",
      "value": "Current"
    },
    {
      "available": true,
      "display": "$0.0002",
      "key": "model.C.cost.component.cacheRead.perQuestion",
      "value": 0.000175
    },
    {
      "available": true,
      "display": "$0.0000",
      "key": "model.C.cost.component.cacheWrite.perQuestion",
      "value": 0
    },
    {
      "available": true,
      "display": "$0.011",
      "key": "model.C.cost.component.output.perQuestion",
      "value": 0.011340000000000001
    },
    {
      "available": true,
      "display": "$0.0064",
      "key": "model.C.cost.component.reasoning.perQuestion",
      "value": 0.00644
    },
    {
      "available": true,
      "display": "$0.013",
      "key": "model.C.cost.component.uncachedInput.perQuestion",
      "value": 0.0128375
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
      "display": "1 of 6 answers with a critical error",
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
      "available": true,
      "display": "17% (95% interval 3%–56%); before scoring method 13, flagged rather than confirmed",
      "key": "model.C.outcome.confirmedCriticalRate",
      "value": 0.16666666666666666
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
      "display": "$0.125 per million tokens",
      "key": "model.C.price.cacheRead.perMillion",
      "value": 0.125
    },
    {
      "available": true,
      "display": "$1.50 per million tokens",
      "key": "model.C.price.cacheWrite.perMillion",
      "value": 1.5
    },
    {
      "available": true,
      "display": "$14.00 per million tokens",
      "key": "model.C.price.output.perMillion",
      "value": 14
    },
    {
      "available": true,
      "display": "$14.00 per million tokens",
      "key": "model.C.price.reasoning.perMillion",
      "value": 14
    },
    {
      "available": true,
      "display": "$3.25 per million tokens",
      "key": "model.C.price.uncachedInput.perMillion",
      "value": 3.25
    },
    {
      "available": true,
      "display": "10 characters",
      "key": "model.C.profile.answerChars.median",
      "value": 10
    },
    {
      "available": true,
      "display": "6 answers",
      "key": "model.C.profile.answers",
      "value": 6
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on accuracy",
      "key": "model.C.profile.deficit.accuracy",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on completeness",
      "key": "model.C.profile.deficit.completeness",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on conciseness",
      "key": "model.C.profile.deficit.conciseness",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on readability",
      "key": "model.C.profile.deficit.readability",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.profile.findings.accuracy",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.profile.findings.completeness",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.profile.findings.conciseness",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.profile.findings.critical_error",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.profile.findings.other",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.profile.findings.readability",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": true,
      "display": "0 of 1 run with structured grader findings",
      "key": "model.C.profile.findings.runsWithSynthesis",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.profile.findings.tool_use",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": true,
      "display": "0 of 6 questions this model missed while the others scored well",
      "key": "model.C.profile.isolatedMisses",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 questions on which this model scored lowest",
      "key": "model.C.profile.lowestScorer",
      "value": 0
    },
    {
      "available": true,
      "display": "1 of 6 answers that may mislead",
      "key": "model.C.profile.mayMislead",
      "value": 1
    },
    {
      "available": true,
      "display": "about 3.3 in 20 answers",
      "key": "model.C.profile.mayMisleadPer20",
      "value": 3.333333333333333
    },
    {
      "available": true,
      "display": "2 of 6 answers without a tool call",
      "key": "model.C.profile.noToolAnswers",
      "value": 2
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.profile.outcome.correct",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.profile.outcome.incorrect",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.profile.outcome.noAnswer",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.profile.outcome.notAttempted",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.profile.outcome.partial",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.profile.outcome.wrongInsteadOfAbstaining",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": true,
      "display": "0 of 6 answers with output defects",
      "key": "model.C.profile.outputDefects",
      "value": 0
    },
    {
      "available": true,
      "display": "1 of 6 questions missed by this model and at least half the others",
      "key": "model.C.profile.sharedMisses",
      "value": 1
    },
    {
      "available": true,
      "display": "0 of 6 answers that hit the tool limit",
      "key": "model.C.profile.toolLimitHits",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers that were truncated",
      "key": "model.C.profile.truncated",
      "value": 0
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
      "display": "3–4 of 5 (tied: intervals overlap)",
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
      "display": "1 of 1 question with a scored answer",
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
      "display": "64 (4 answers)",
      "key": "model.C.score.withTools",
      "value": 63.5
    },
    {
      "available": true,
      "display": "68 (2 answers)",
      "key": "model.C.score.withoutTools",
      "value": 67.5
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
      "display": "Accuracy 55%, Completeness 25%, Conciseness 10%, Readability 10%",
      "key": "model.C.scoring.weights"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.servedTier",
      "unavailableReason": "No answer recorded the tier it was served at."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.speed.decodeRate",
      "unavailableReason": "No answer's final call recorded a measurable decode rate."
    },
    {
      "available": true,
      "display": "2.5",
      "key": "model.C.speed.modelCallsPerAnswer",
      "value": 2.5
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
      "display": "0% of model time",
      "key": "model.C.speed.ownWaitShare",
      "value": 0
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
      "key": "model.C.speed.retriesPerAnswer",
      "unavailableReason": "No answer recorded its retried attempts."
    },
    {
      "available": true,
      "display": "every answer served at the requested tier",
      "key": "model.C.speed.servedTierMismatch",
      "value": 0
    },
    {
      "available": true,
      "display": "1.5 s",
      "key": "model.C.speed.toolTimeP50",
      "value": 1500
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.speed.ttftP50",
      "unavailableReason": "Not recorded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.C.speed.ttftP90",
      "unavailableReason": "No answer recorded a time to first token."
    },
    {
      "available": true,
      "display": "1.3 s",
      "key": "model.C.speed.ttftPerCallP50",
      "value": 1260
    },
    {
      "available": true,
      "display": "Comparable",
      "key": "model.C.state",
      "value": "Comparable"
    },
    {
      "available": true,
      "display": "no response-style conflict",
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
      "available": true,
      "display": "26%",
      "key": "model.C.tokens.cacheReadShare",
      "value": 0.2616822429906542
    },
    {
      "available": true,
      "display": "5,350",
      "key": "model.C.tokens.inputPerQuestion",
      "value": 5350
    },
    {
      "available": true,
      "display": "1,270",
      "key": "model.C.tokens.outputPerQuestion",
      "value": 1270
    },
    {
      "available": true,
      "display": "460",
      "key": "model.C.tokens.reasoningPerQuestion",
      "value": 460
    },
    {
      "available": true,
      "display": "36% of output tokens",
      "key": "model.C.tokens.reasoningShare",
      "value": 0.36220472440944884
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
      "display": "0 of 6 questions scored more than 15 points above the reference",
      "key": "model.C.vsReference.better",
      "value": 0
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "model.C.vsReference.shared",
      "value": 6
    },
    {
      "available": true,
      "display": "3 of 6 questions scored within 15 points of the reference",
      "key": "model.C.vsReference.similar",
      "value": 3
    },
    {
      "available": true,
      "display": "3 of 6 questions scored more than 15 points below the reference",
      "key": "model.C.vsReference.worse",
      "value": 3
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
      "display": "not set",
      "key": "model.D.config.maxOutputTokens"
    },
    {
      "available": true,
      "display": "Enabled",
      "key": "model.D.config.parallelMode",
      "value": "Enabled"
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.D.config.reasoningMode"
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.D.config.serviceTier"
    },
    {
      "available": true,
      "display": "Current",
      "key": "model.D.cost.basis",
      "value": "Current"
    },
    {
      "available": true,
      "display": "$0.0002",
      "key": "model.D.cost.component.cacheRead.perQuestion",
      "value": 0.0002
    },
    {
      "available": true,
      "display": "$0.0000",
      "key": "model.D.cost.component.cacheWrite.perQuestion",
      "value": 0
    },
    {
      "available": true,
      "display": "$0.014",
      "key": "model.D.cost.component.output.perQuestion",
      "value": 0.014080000000000002
    },
    {
      "available": true,
      "display": "$0.0086",
      "key": "model.D.cost.component.reasoning.perQuestion",
      "value": 0.00864
    },
    {
      "available": true,
      "display": "$0.018",
      "key": "model.D.cost.component.uncachedInput.perQuestion",
      "value": 0.0180625
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
      "display": "0 of 6 answers with a critical error",
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
      "available": true,
      "display": "0% (95% interval 0%–39%); before scoring method 13, flagged rather than confirmed",
      "key": "model.D.outcome.confirmedCriticalRate",
      "value": 0
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
      "display": "$0.125 per million tokens",
      "key": "model.D.price.cacheRead.perMillion",
      "value": 0.125
    },
    {
      "available": true,
      "display": "$1.50 per million tokens",
      "key": "model.D.price.cacheWrite.perMillion",
      "value": 1.5
    },
    {
      "available": true,
      "display": "$16.00 per million tokens",
      "key": "model.D.price.output.perMillion",
      "value": 16
    },
    {
      "available": true,
      "display": "$16.00 per million tokens",
      "key": "model.D.price.reasoning.perMillion",
      "value": 16
    },
    {
      "available": true,
      "display": "$4.25 per million tokens",
      "key": "model.D.price.uncachedInput.perMillion",
      "value": 4.25
    },
    {
      "available": true,
      "display": "10 characters",
      "key": "model.D.profile.answerChars.median",
      "value": 10
    },
    {
      "available": true,
      "display": "6 answers",
      "key": "model.D.profile.answers",
      "value": 6
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on accuracy",
      "key": "model.D.profile.deficit.accuracy",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on completeness",
      "key": "model.D.profile.deficit.completeness",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on conciseness",
      "key": "model.D.profile.deficit.conciseness",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on readability",
      "key": "model.D.profile.deficit.readability",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.profile.findings.accuracy",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.profile.findings.completeness",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.profile.findings.conciseness",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.profile.findings.critical_error",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.profile.findings.other",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.profile.findings.readability",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": true,
      "display": "0 of 1 run with structured grader findings",
      "key": "model.D.profile.findings.runsWithSynthesis",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.profile.findings.tool_use",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": true,
      "display": "0 of 6 questions this model missed while the others scored well",
      "key": "model.D.profile.isolatedMisses",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 questions on which this model scored lowest",
      "key": "model.D.profile.lowestScorer",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers that may mislead",
      "key": "model.D.profile.mayMislead",
      "value": 0
    },
    {
      "available": true,
      "display": "0 in 20 answers",
      "key": "model.D.profile.mayMisleadPer20",
      "value": 0
    },
    {
      "available": true,
      "display": "2 of 6 answers without a tool call",
      "key": "model.D.profile.noToolAnswers",
      "value": 2
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.profile.outcome.correct",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.profile.outcome.incorrect",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.profile.outcome.noAnswer",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.profile.outcome.notAttempted",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.profile.outcome.partial",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.profile.outcome.wrongInsteadOfAbstaining",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": true,
      "display": "0 of 6 answers with output defects",
      "key": "model.D.profile.outputDefects",
      "value": 0
    },
    {
      "available": true,
      "display": "1 of 6 questions missed by this model and at least half the others",
      "key": "model.D.profile.sharedMisses",
      "value": 1
    },
    {
      "available": true,
      "display": "0 of 6 answers that hit the tool limit",
      "key": "model.D.profile.toolLimitHits",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers that were truncated",
      "key": "model.D.profile.truncated",
      "value": 0
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
      "display": "3–4 of 5 (tied: intervals overlap)",
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
      "display": "1 of 1 question with a scored answer",
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
      "display": "69 (4 answers)",
      "key": "model.D.score.withTools",
      "value": 68.75
    },
    {
      "available": true,
      "display": "55 (2 answers)",
      "key": "model.D.score.withoutTools",
      "value": 55
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
      "display": "Accuracy 55%, Completeness 25%, Conciseness 10%, Readability 10%",
      "key": "model.D.scoring.weights"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.servedTier",
      "unavailableReason": "No answer recorded the tier it was served at."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.speed.decodeRate",
      "unavailableReason": "No answer's final call recorded a measurable decode rate."
    },
    {
      "available": true,
      "display": "2.5",
      "key": "model.D.speed.modelCallsPerAnswer",
      "value": 2.5
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
      "display": "0% of model time",
      "key": "model.D.speed.ownWaitShare",
      "value": 0
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
      "key": "model.D.speed.retriesPerAnswer",
      "unavailableReason": "No answer recorded its retried attempts."
    },
    {
      "available": true,
      "display": "every answer served at the requested tier",
      "key": "model.D.speed.servedTierMismatch",
      "value": 0
    },
    {
      "available": true,
      "display": "1.5 s",
      "key": "model.D.speed.toolTimeP50",
      "value": 1500
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.speed.ttftP50",
      "unavailableReason": "Not recorded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.D.speed.ttftP90",
      "unavailableReason": "No answer recorded a time to first token."
    },
    {
      "available": true,
      "display": "1.5 s",
      "key": "model.D.speed.ttftPerCallP50",
      "value": 1510
    },
    {
      "available": true,
      "display": "Comparable",
      "key": "model.D.state",
      "value": "Comparable"
    },
    {
      "available": true,
      "display": "no response-style conflict",
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
      "available": true,
      "display": "27%",
      "key": "model.D.tokens.cacheReadShare",
      "value": 0.27350427350427353
    },
    {
      "available": true,
      "display": "5,850",
      "key": "model.D.tokens.inputPerQuestion",
      "value": 5850
    },
    {
      "available": true,
      "display": "1,420",
      "key": "model.D.tokens.outputPerQuestion",
      "value": 1420
    },
    {
      "available": true,
      "display": "540",
      "key": "model.D.tokens.reasoningPerQuestion",
      "value": 540
    },
    {
      "available": true,
      "display": "38% of output tokens",
      "key": "model.D.tokens.reasoningShare",
      "value": 0.38028169014084506
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
      "display": "0 of 6 questions scored more than 15 points above the reference",
      "key": "model.D.vsReference.better",
      "value": 0
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "model.D.vsReference.shared",
      "value": 6
    },
    {
      "available": true,
      "display": "1 of 6 questions scored within 15 points of the reference",
      "key": "model.D.vsReference.similar",
      "value": 1
    },
    {
      "available": true,
      "display": "5 of 6 questions scored more than 15 points below the reference",
      "key": "model.D.vsReference.worse",
      "value": 5
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
      "display": "not set",
      "key": "model.E.config.maxOutputTokens"
    },
    {
      "available": true,
      "display": "Enabled",
      "key": "model.E.config.parallelMode",
      "value": "Enabled"
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.E.config.reasoningMode"
    },
    {
      "available": true,
      "display": "not set",
      "key": "model.E.config.serviceTier"
    },
    {
      "available": true,
      "display": "Current",
      "key": "model.E.cost.basis",
      "value": "Current"
    },
    {
      "available": true,
      "display": "$0.0002",
      "key": "model.E.cost.component.cacheRead.perQuestion",
      "value": 0.00022499999999999997
    },
    {
      "available": true,
      "display": "$0.0000",
      "key": "model.E.cost.component.cacheWrite.perQuestion",
      "value": 0
    },
    {
      "available": true,
      "display": "$0.017",
      "key": "model.E.cost.component.output.perQuestion",
      "value": 0.0171
    },
    {
      "available": true,
      "display": "$0.011",
      "key": "model.E.cost.component.reasoning.perQuestion",
      "value": 0.011159999999999998
    },
    {
      "available": true,
      "display": "$0.024",
      "key": "model.E.cost.component.uncachedInput.perQuestion",
      "value": 0.023887500000000002
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
      "display": "0 of 6 answers with a critical error",
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
      "available": true,
      "display": "0% (95% interval 0%–39%); before scoring method 13, flagged rather than confirmed",
      "key": "model.E.outcome.confirmedCriticalRate",
      "value": 0
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
      "display": "$0.125 per million tokens",
      "key": "model.E.price.cacheRead.perMillion",
      "value": 0.125
    },
    {
      "available": true,
      "display": "$1.50 per million tokens",
      "key": "model.E.price.cacheWrite.perMillion",
      "value": 1.5
    },
    {
      "available": true,
      "display": "$18.00 per million tokens",
      "key": "model.E.price.output.perMillion",
      "value": 18
    },
    {
      "available": true,
      "display": "$18.00 per million tokens",
      "key": "model.E.price.reasoning.perMillion",
      "value": 18
    },
    {
      "available": true,
      "display": "$5.25 per million tokens",
      "key": "model.E.price.uncachedInput.perMillion",
      "value": 5.25
    },
    {
      "available": true,
      "display": "10 characters",
      "key": "model.E.profile.answerChars.median",
      "value": 10
    },
    {
      "available": true,
      "display": "6 answers",
      "key": "model.E.profile.answers",
      "value": 6
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on accuracy",
      "key": "model.E.profile.deficit.accuracy",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on completeness",
      "key": "model.E.profile.deficit.completeness",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on conciseness",
      "key": "model.E.profile.deficit.conciseness",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers at level 2 or below on readability",
      "key": "model.E.profile.deficit.readability",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.profile.findings.accuracy",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.profile.findings.completeness",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.profile.findings.conciseness",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.profile.findings.critical_error",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.profile.findings.other",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.profile.findings.readability",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": true,
      "display": "0 of 1 run with structured grader findings",
      "key": "model.E.profile.findings.runsWithSynthesis",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.profile.findings.tool_use",
      "unavailableReason": "No run of this model recorded structured grader findings; they are recorded from harness 40."
    },
    {
      "available": true,
      "display": "1 of 6 questions this model missed while the others scored well",
      "key": "model.E.profile.isolatedMisses",
      "value": 1
    },
    {
      "available": true,
      "display": "6 of 6 questions on which this model scored lowest",
      "key": "model.E.profile.lowestScorer",
      "value": 6
    },
    {
      "available": true,
      "display": "0 of 6 answers that may mislead",
      "key": "model.E.profile.mayMislead",
      "value": 0
    },
    {
      "available": true,
      "display": "0 in 20 answers",
      "key": "model.E.profile.mayMisleadPer20",
      "value": 0
    },
    {
      "available": true,
      "display": "2 of 6 answers without a tool call",
      "key": "model.E.profile.noToolAnswers",
      "value": 2
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.profile.outcome.correct",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.profile.outcome.incorrect",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.profile.outcome.noAnswer",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.profile.outcome.notAttempted",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.profile.outcome.partial",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.profile.outcome.wrongInsteadOfAbstaining",
      "unavailableReason": "Outcome classes are not recorded before scoring method 13."
    },
    {
      "available": true,
      "display": "0 of 6 answers with output defects",
      "key": "model.E.profile.outputDefects",
      "value": 0
    },
    {
      "available": true,
      "display": "1 of 6 questions missed by this model and at least half the others",
      "key": "model.E.profile.sharedMisses",
      "value": 1
    },
    {
      "available": true,
      "display": "0 of 6 answers that hit the tool limit",
      "key": "model.E.profile.toolLimitHits",
      "value": 0
    },
    {
      "available": true,
      "display": "0 of 6 answers that were truncated",
      "key": "model.E.profile.truncated",
      "value": 0
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
      "display": "1 of 1 question with a scored answer",
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
      "display": "47 (4 answers)",
      "key": "model.E.score.withTools",
      "value": 46.75
    },
    {
      "available": true,
      "display": "57 (2 answers)",
      "key": "model.E.score.withoutTools",
      "value": 56.5
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
      "display": "Accuracy 55%, Completeness 25%, Conciseness 10%, Readability 10%",
      "key": "model.E.scoring.weights"
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.servedTier",
      "unavailableReason": "No answer recorded the tier it was served at."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.speed.decodeRate",
      "unavailableReason": "No answer's final call recorded a measurable decode rate."
    },
    {
      "available": true,
      "display": "2.5",
      "key": "model.E.speed.modelCallsPerAnswer",
      "value": 2.5
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
      "display": "0% of model time",
      "key": "model.E.speed.ownWaitShare",
      "value": 0
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
      "key": "model.E.speed.retriesPerAnswer",
      "unavailableReason": "No answer recorded its retried attempts."
    },
    {
      "available": true,
      "display": "every answer served at the requested tier",
      "key": "model.E.speed.servedTierMismatch",
      "value": 0
    },
    {
      "available": true,
      "display": "1.5 s",
      "key": "model.E.speed.toolTimeP50",
      "value": 1500
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.speed.ttftP50",
      "unavailableReason": "Not recorded."
    },
    {
      "available": false,
      "display": "not available",
      "key": "model.E.speed.ttftP90",
      "unavailableReason": "No answer recorded a time to first token."
    },
    {
      "available": true,
      "display": "1.8 s",
      "key": "model.E.speed.ttftPerCallP50",
      "value": 1760
    },
    {
      "available": true,
      "display": "Comparable",
      "key": "model.E.state",
      "value": "Comparable"
    },
    {
      "available": true,
      "display": "no response-style conflict",
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
      "available": true,
      "display": "28%",
      "key": "model.E.tokens.cacheReadShare",
      "value": 0.28346456692913385
    },
    {
      "available": true,
      "display": "6,350",
      "key": "model.E.tokens.inputPerQuestion",
      "value": 6350
    },
    {
      "available": true,
      "display": "1,570",
      "key": "model.E.tokens.outputPerQuestion",
      "value": 1570
    },
    {
      "available": true,
      "display": "620",
      "key": "model.E.tokens.reasoningPerQuestion",
      "value": 620
    },
    {
      "available": true,
      "display": "39% of output tokens",
      "key": "model.E.tokens.reasoningShare",
      "value": 0.39490445859872614
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
      "available": true,
      "display": "0 of 6 questions scored more than 15 points above the reference",
      "key": "model.E.vsReference.better",
      "value": 0
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "model.E.vsReference.shared",
      "value": 6
    },
    {
      "available": true,
      "display": "1 of 6 questions scored within 15 points of the reference",
      "key": "model.E.vsReference.similar",
      "value": 1
    },
    {
      "available": true,
      "display": "5 of 6 questions scored more than 15 points below the reference",
      "key": "model.E.vsReference.worse",
      "value": 5
    },
    {
      "available": true,
      "display": "cheaper and better",
      "key": "notable.1.kind",
      "value": "cheaperAndBetter"
    },
    {
      "available": true,
      "display": "Models B and D",
      "key": "notable.1.models",
      "value": 2
    },
    {
      "available": true,
      "display": "none",
      "key": "notable.1.questions",
      "value": 0
    },
    {
      "available": true,
      "display": "Model B scored higher than Model D at 0.50 times its cost per question, established by 95% intervals that do not overlap.",
      "key": "notable.1.summary",
      "value": "Model B scored higher than Model D at 0.50 times its cost per question, established by 95% intervals that do not overlap."
    },
    {
      "available": true,
      "display": "faster and better",
      "key": "notable.2.kind",
      "value": "fasterAndBetter"
    },
    {
      "available": true,
      "display": "Models D and E",
      "key": "notable.2.models",
      "value": 2
    },
    {
      "available": true,
      "display": "none",
      "key": "notable.2.questions",
      "value": 0
    },
    {
      "available": true,
      "display": "Model D scored higher than Model E at 0.40 times its median answer time, established by 95% intervals that do not overlap.",
      "key": "notable.2.summary",
      "value": "Model D scored higher than Model E at 0.40 times its median answer time, established by 95% intervals that do not overlap."
    },
    {
      "available": true,
      "display": "faster and better",
      "key": "notable.3.kind",
      "value": "fasterAndBetter"
    },
    {
      "available": true,
      "display": "Models B and E",
      "key": "notable.3.models",
      "value": 2
    },
    {
      "available": true,
      "display": "none",
      "key": "notable.3.questions",
      "value": 0
    },
    {
      "available": true,
      "display": "Model B scored higher than Model E at 0.45 times its median answer time, established by 95% intervals that do not overlap.",
      "key": "notable.3.summary",
      "value": "Model B scored higher than Model E at 0.45 times its median answer time, established by 95% intervals that do not overlap."
    },
    {
      "available": true,
      "display": "3 notable findings",
      "key": "notable.count",
      "value": 3
    },
    {
      "available": true,
      "display": "provider",
      "key": "pair.A.B.configDifferences",
      "value": 1
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
      "available": false,
      "display": "not available",
      "key": "pair.A.B.cost.short",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "no Accuracy difference established after the Holm adjustment across 10 tests (adjusted p 0.813)",
      "key": "pair.A.B.dimension.accuracy.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Accuracy difference established after the Holm adjustment across 4 tests (adjusted p 0.406)",
      "key": "pair.A.B.dimension.accuracy.reference",
      "value": false
    },
    {
      "available": true,
      "display": "no Completeness difference established after the Holm adjustment across 10 tests (adjusted p 0.813)",
      "key": "pair.A.B.dimension.completeness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Completeness difference established after the Holm adjustment across 4 tests (adjusted p 0.406)",
      "key": "pair.A.B.dimension.completeness.reference",
      "value": false
    },
    {
      "available": true,
      "display": "no Conciseness difference established after the Holm adjustment across 10 tests (adjusted p 0.813)",
      "key": "pair.A.B.dimension.conciseness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Conciseness difference established after the Holm adjustment across 4 tests (adjusted p 0.406)",
      "key": "pair.A.B.dimension.conciseness.reference",
      "value": false
    },
    {
      "available": true,
      "display": "no Readability difference established after the Holm adjustment across 10 tests (adjusted p 0.813)",
      "key": "pair.A.B.dimension.readability.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Readability difference established after the Holm adjustment across 4 tests (adjusted p 0.406)",
      "key": "pair.A.B.dimension.readability.reference",
      "value": false
    },
    {
      "available": true,
      "display": "the 95% intervals of Model A and Model B overlap",
      "key": "pair.A.B.intervalOverlap",
      "value": true
    },
    {
      "available": true,
      "display": "no difference in score established after the Holm adjustment across 10 tests (adjusted p 0.813)",
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
      "display": "no difference in score established after the Holm adjustment across 4 tests (adjusted p 0.406)",
      "key": "pair.A.B.quality.reference",
      "value": false
    },
    {
      "available": true,
      "display": "0.0 points, not established",
      "key": "pair.A.B.quality.short",
      "value": false
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.A.B.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "no speed difference established after the Holm adjustment across 10 tests (adjusted p 1.000)",
      "key": "pair.A.B.speed.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "Model A took 0.83 times as long as Model B on the same questions (0.53 to 1.31)",
      "key": "pair.A.B.speed.ratio",
      "value": 0.8330303825065561
    },
    {
      "available": true,
      "display": "no speed difference established after the Holm adjustment across 4 tests (adjusted p 0.438)",
      "key": "pair.A.B.speed.reference",
      "value": false
    },
    {
      "available": true,
      "display": "0.83 times as long, not established",
      "key": "pair.A.B.speed.short",
      "value": false
    },
    {
      "available": true,
      "display": "provider",
      "key": "pair.A.C.configDifferences",
      "value": 1
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
      "available": false,
      "display": "not available",
      "key": "pair.A.C.cost.short",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "no Accuracy difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.C.dimension.accuracy.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Accuracy difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.C.dimension.accuracy.reference",
      "value": false
    },
    {
      "available": true,
      "display": "no Completeness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.C.dimension.completeness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Completeness difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.C.dimension.completeness.reference",
      "value": false
    },
    {
      "available": true,
      "display": "no Conciseness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.C.dimension.conciseness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Conciseness difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.C.dimension.conciseness.reference",
      "value": false
    },
    {
      "available": true,
      "display": "no Readability difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.C.dimension.readability.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Readability difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.C.dimension.readability.reference",
      "value": false
    },
    {
      "available": true,
      "display": "the 95% intervals of Model A and Model C do not overlap",
      "key": "pair.A.C.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference in score established after the Holm adjustment across 10 tests (adjusted p 0.313)",
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
      "display": "no difference in score established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.C.quality.reference",
      "value": false
    },
    {
      "available": true,
      "display": "+15.2 points, not established",
      "key": "pair.A.C.quality.short",
      "value": false
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.A.C.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "no speed difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.C.speed.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "Model A took 0.71 times as long as Model C on the same questions (0.70 to 0.73)",
      "key": "pair.A.C.speed.ratio",
      "value": 0.714024714731622
    },
    {
      "available": true,
      "display": "no speed difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.C.speed.reference",
      "value": false
    },
    {
      "available": true,
      "display": "0.71 times as long, not established",
      "key": "pair.A.C.speed.short",
      "value": false
    },
    {
      "available": true,
      "display": "provider",
      "key": "pair.A.D.configDifferences",
      "value": 1
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
      "available": false,
      "display": "not available",
      "key": "pair.A.D.cost.short",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "no Accuracy difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.D.dimension.accuracy.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Accuracy difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.D.dimension.accuracy.reference",
      "value": false
    },
    {
      "available": true,
      "display": "no Completeness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.D.dimension.completeness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Completeness difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.D.dimension.completeness.reference",
      "value": false
    },
    {
      "available": true,
      "display": "no Conciseness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.D.dimension.conciseness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Conciseness difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.D.dimension.conciseness.reference",
      "value": false
    },
    {
      "available": true,
      "display": "no Readability difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.D.dimension.readability.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Readability difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.D.dimension.readability.reference",
      "value": false
    },
    {
      "available": true,
      "display": "the 95% intervals of Model A and Model D do not overlap",
      "key": "pair.A.D.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference in score established after the Holm adjustment across 10 tests (adjusted p 0.313)",
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
      "display": "no difference in score established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.D.quality.reference",
      "value": false
    },
    {
      "available": true,
      "display": "+15.8 points, not established",
      "key": "pair.A.D.quality.short",
      "value": false
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.A.D.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "no speed difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.D.speed.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "Model A took 0.62 times as long as Model D on the same questions (0.40 to 0.98)",
      "key": "pair.A.D.speed.ratio",
      "value": 0.6246956089867761
    },
    {
      "available": true,
      "display": "no speed difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.D.speed.reference",
      "value": false
    },
    {
      "available": true,
      "display": "0.62 times as long, not established",
      "key": "pair.A.D.speed.short",
      "value": false
    },
    {
      "available": true,
      "display": "provider",
      "key": "pair.A.E.configDifferences",
      "value": 1
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
      "available": false,
      "display": "not available",
      "key": "pair.A.E.cost.short",
      "unavailableReason": "No candidate price card was resolved for run #31 of Model A on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "no Accuracy difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.E.dimension.accuracy.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Accuracy difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.E.dimension.accuracy.reference",
      "value": false
    },
    {
      "available": true,
      "display": "no Completeness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.E.dimension.completeness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Completeness difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.E.dimension.completeness.reference",
      "value": false
    },
    {
      "available": true,
      "display": "no Conciseness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.E.dimension.conciseness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Conciseness difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.E.dimension.conciseness.reference",
      "value": false
    },
    {
      "available": true,
      "display": "no Readability difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.E.dimension.readability.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Readability difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.E.dimension.readability.reference",
      "value": false
    },
    {
      "available": true,
      "display": "the 95% intervals of Model A and Model E do not overlap",
      "key": "pair.A.E.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference in score established after the Holm adjustment across 10 tests (adjusted p 0.313)",
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
      "display": "no difference in score established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.E.quality.reference",
      "value": false
    },
    {
      "available": true,
      "display": "+30.0 points, not established",
      "key": "pair.A.E.quality.short",
      "value": false
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.A.E.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "no speed difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.A.E.speed.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "Model A took 0.56 times as long as Model E on the same questions (0.54 to 0.57)",
      "key": "pair.A.E.speed.ratio",
      "value": 0.5553073274569991
    },
    {
      "available": true,
      "display": "no speed difference established after the Holm adjustment across 4 tests (adjusted p 0.125)",
      "key": "pair.A.E.speed.reference",
      "value": false
    },
    {
      "available": true,
      "display": "0.56 times as long, not established",
      "key": "pair.A.E.speed.short",
      "value": false
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
      "available": false,
      "display": "not available",
      "key": "pair.B.C.cost.short",
      "unavailableReason": "No candidate price card was resolved for run #32 of Model B on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "no Accuracy difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.C.dimension.accuracy.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Completeness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.C.dimension.completeness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Conciseness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.C.dimension.conciseness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Readability difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.C.dimension.readability.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "the 95% intervals of Model B and Model C do not overlap",
      "key": "pair.B.C.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference in score established after the Holm adjustment across 10 tests (adjusted p 0.313)",
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
      "display": "+15.2 points, not established",
      "key": "pair.B.C.quality.short",
      "value": false
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.B.C.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "no speed difference established after the Holm adjustment across 10 tests (adjusted p 1.000)",
      "key": "pair.B.C.speed.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "Model B took 0.86 times as long as Model C on the same questions (0.54 to 1.36)",
      "key": "pair.B.C.speed.ratio",
      "value": 0.8571412636633363
    },
    {
      "available": true,
      "display": "0.86 times as long, not established",
      "key": "pair.B.C.speed.short",
      "value": false
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
      "available": false,
      "display": "not available",
      "key": "pair.B.D.cost.short",
      "unavailableReason": "No candidate price card was resolved for run #32 of Model B on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "no Accuracy difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.D.dimension.accuracy.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Completeness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.D.dimension.completeness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Conciseness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.D.dimension.conciseness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Readability difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.D.dimension.readability.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "the 95% intervals of Model B and Model D do not overlap",
      "key": "pair.B.D.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference in score established after the Holm adjustment across 10 tests (adjusted p 0.313)",
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
      "display": "+15.8 points, not established",
      "key": "pair.B.D.quality.short",
      "value": false
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.B.D.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "no speed difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.D.speed.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "Model B took 0.75 times as long as Model D on the same questions (0.74 to 0.76)",
      "key": "pair.B.D.speed.ratio",
      "value": 0.7499073528471929
    },
    {
      "available": true,
      "display": "0.75 times as long, not established",
      "key": "pair.B.D.speed.short",
      "value": false
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
      "available": false,
      "display": "not available",
      "key": "pair.B.E.cost.short",
      "unavailableReason": "No candidate price card was resolved for run #32 of Model B on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "no Accuracy difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.E.dimension.accuracy.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Completeness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.E.dimension.completeness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Conciseness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.E.dimension.conciseness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Readability difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.B.E.dimension.readability.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "the 95% intervals of Model B and Model E do not overlap",
      "key": "pair.B.E.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference in score established after the Holm adjustment across 10 tests (adjusted p 0.313)",
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
      "display": "+30.0 points, not established",
      "key": "pair.B.E.quality.short",
      "value": false
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.B.E.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "no speed difference established after the Holm adjustment across 10 tests (adjusted p 0.469)",
      "key": "pair.B.E.speed.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "Model B took 0.67 times as long as Model E on the same questions (0.42 to 1.06)",
      "key": "pair.B.E.speed.ratio",
      "value": 0.6666111334212095
    },
    {
      "available": true,
      "display": "0.67 times as long, not established",
      "key": "pair.B.E.speed.short",
      "value": false
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
      "available": false,
      "display": "not available",
      "key": "pair.C.D.cost.short",
      "unavailableReason": "No candidate price card was resolved for run #33 of Model C on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "no Accuracy difference established after the Holm adjustment across 10 tests (adjusted p 1.000)",
      "key": "pair.C.D.dimension.accuracy.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Completeness difference established after the Holm adjustment across 10 tests (adjusted p 1.000)",
      "key": "pair.C.D.dimension.completeness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Conciseness difference established after the Holm adjustment across 10 tests (adjusted p 1.000)",
      "key": "pair.C.D.dimension.conciseness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Readability difference established after the Holm adjustment across 10 tests (adjusted p 1.000)",
      "key": "pair.C.D.dimension.readability.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "the 95% intervals of Model C and Model D overlap",
      "key": "pair.C.D.intervalOverlap",
      "value": true
    },
    {
      "available": true,
      "display": "no difference in score established after the Holm adjustment across 10 tests (adjusted p 1.000)",
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
      "display": "+0.7 points, not established",
      "key": "pair.C.D.quality.short",
      "value": false
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.C.D.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "no speed difference established after the Holm adjustment across 10 tests (adjusted p 1.000)",
      "key": "pair.C.D.speed.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "Model C took 0.87 times as long as Model D on the same questions (0.55 to 1.38)",
      "key": "pair.C.D.speed.ratio",
      "value": 0.8748935381341502
    },
    {
      "available": true,
      "display": "0.87 times as long, not established",
      "key": "pair.C.D.speed.short",
      "value": false
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
      "available": false,
      "display": "not available",
      "key": "pair.C.E.cost.short",
      "unavailableReason": "No candidate price card was resolved for run #33 of Model C on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "no Accuracy difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.C.E.dimension.accuracy.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Completeness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.C.E.dimension.completeness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Conciseness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.C.E.dimension.conciseness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Readability difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.C.E.dimension.readability.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "the 95% intervals of Model C and Model E do not overlap",
      "key": "pair.C.E.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference in score established after the Holm adjustment across 10 tests (adjusted p 0.313)",
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
      "display": "+14.8 points, not established",
      "key": "pair.C.E.quality.short",
      "value": false
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.C.E.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "no speed difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.C.E.speed.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "Model C took 0.78 times as long as Model E on the same questions (0.77 to 0.79)",
      "key": "pair.C.E.speed.ratio",
      "value": 0.7777144348087733
    },
    {
      "available": true,
      "display": "0.78 times as long, not established",
      "key": "pair.C.E.speed.short",
      "value": false
    },
    {
      "available": true,
      "display": "provider",
      "key": "pair.D.E.configDifferences",
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
      "available": false,
      "display": "not available",
      "key": "pair.D.E.cost.short",
      "unavailableReason": "No candidate price card was resolved for run #34 of Model D on this pricing basis, so its cost is unknown."
    },
    {
      "available": true,
      "display": "no Accuracy difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.D.E.dimension.accuracy.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Completeness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.D.E.dimension.completeness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Conciseness difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.D.E.dimension.conciseness.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "no Readability difference established after the Holm adjustment across 10 tests (adjusted p 0.313)",
      "key": "pair.D.E.dimension.readability.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "the 95% intervals of Model D and Model E do not overlap",
      "key": "pair.D.E.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "no difference in score established after the Holm adjustment across 10 tests (adjusted p 0.313)",
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
      "display": "+14.2 points, not established",
      "key": "pair.D.E.quality.short",
      "value": false
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "pair.D.E.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "no speed difference established after the Holm adjustment across 10 tests (adjusted p 1.000)",
      "key": "pair.D.E.speed.allPairs",
      "value": false
    },
    {
      "available": true,
      "display": "Model D took 0.89 times as long as Model E on the same questions (0.56 to 1.41)",
      "key": "pair.D.E.speed.ratio",
      "value": 0.8889246530124949
    },
    {
      "available": true,
      "display": "0.89 times as long, not established",
      "key": "pair.D.E.speed.short",
      "value": false
    },
    {
      "available": true,
      "display": "1 of 6 questions with a critical error from any model",
      "key": "questions.anyCriticalError",
      "value": 1
    },
    {
      "available": true,
      "display": "0 of 6 questions that every model scored low",
      "key": "questions.sharedLow",
      "value": 0
    },
    {
      "available": true,
      "display": "6 of 6 questions with a wide spread between models",
      "key": "questions.wideSpread",
      "value": 6
    },
    {
      "available": true,
      "display": "none",
      "key": "rca.anomaly.count",
      "value": 0
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.anomaly.priorsChecked",
      "unavailableReason": "No two models share a provider."
    },
    {
      "available": true,
      "display": "0 pairs",
      "key": "rca.anomaly.untestedPairs",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.A.budgetOrDefect",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.A.criticalError",
      "value": 0
    },
    {
      "available": true,
      "display": "9.8 index points",
      "key": "rca.atRisk.A.noTool",
      "value": 9.833333333333334
    },
    {
      "available": true,
      "display": "0.8 index points",
      "key": "rca.atRisk.A.otherLow",
      "value": 0.8333333333333333
    },
    {
      "available": true,
      "display": "10.7 index points below a median answer of 89",
      "key": "rca.atRisk.A.total",
      "value": 10.666666666666668
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.B.budgetOrDefect",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.B.criticalError",
      "value": 0
    },
    {
      "available": true,
      "display": "0.2 index points",
      "key": "rca.atRisk.B.noTool",
      "value": 0.16666666666666666
    },
    {
      "available": true,
      "display": "2.8 index points",
      "key": "rca.atRisk.B.otherLow",
      "value": 2.833333333333333
    },
    {
      "available": true,
      "display": "3.0 index points below a median answer of 81",
      "key": "rca.atRisk.B.total",
      "value": 2.9999999999999996
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.C.budgetOrDefect",
      "value": 0
    },
    {
      "available": true,
      "display": "7.2 index points",
      "key": "rca.atRisk.C.criticalError",
      "value": 7.166666666666667
    },
    {
      "available": true,
      "display": "1.2 index points",
      "key": "rca.atRisk.C.noTool",
      "value": 1.1666666666666667
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.C.otherLow",
      "value": 0
    },
    {
      "available": true,
      "display": "8.3 index points below a median answer of 71",
      "key": "rca.atRisk.C.total",
      "value": 8.333333333333334
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.D.budgetOrDefect",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.D.criticalError",
      "value": 0
    },
    {
      "available": true,
      "display": "6.3 index points",
      "key": "rca.atRisk.D.noTool",
      "value": 6.333333333333333
    },
    {
      "available": true,
      "display": "2.0 index points",
      "key": "rca.atRisk.D.otherLow",
      "value": 2
    },
    {
      "available": true,
      "display": "8.3 index points below a median answer of 70",
      "key": "rca.atRisk.D.total",
      "value": 8.333333333333332
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.E.budgetOrDefect",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.E.criticalError",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.E.noTool",
      "value": 0
    },
    {
      "available": true,
      "display": "5.6 index points",
      "key": "rca.atRisk.E.otherLow",
      "value": 5.583333333333333
    },
    {
      "available": true,
      "display": "5.6 index points below a median answer of 54",
      "key": "rca.atRisk.E.total",
      "value": 5.583333333333333
    },
    {
      "available": true,
      "display": "HC2.1",
      "key": "rca.axis.cost.best",
      "value": "HC2.1"
    },
    {
      "available": true,
      "display": "Not established",
      "key": "rca.axis.cost.status",
      "value": "NotEstablished"
    },
    {
      "available": true,
      "display": "HQ3.1",
      "key": "rca.axis.intelligence.best",
      "value": "HQ3.1"
    },
    {
      "available": true,
      "display": "Not established",
      "key": "rca.axis.intelligence.status",
      "value": "NotEstablished"
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.axis.speed.best",
      "unavailableReason": "Only hypotheses about one model's own breakdown fired on this axis; none compares the models."
    },
    {
      "available": true,
      "display": "Not established",
      "key": "rca.axis.speed.status",
      "value": "NotEstablished"
    },
    {
      "available": true,
      "display": "Check the rubric of Q3 against the source, and whether the tools' corpus holds the facts the question needs.",
      "key": "rca.check.1"
    },
    {
      "available": true,
      "display": "Check 1, a review of the rubric of Q3",
      "key": "rca.check.1.summary"
    },
    {
      "available": true,
      "display": "71–73%",
      "key": "rca.cost.A.B.all.price.interval"
    },
    {
      "available": true,
      "display": "72%",
      "key": "rca.cost.A.B.all.price.share",
      "value": 0.71922009092776
    },
    {
      "available": true,
      "display": "27–29%",
      "key": "rca.cost.A.B.all.volume.interval"
    },
    {
      "available": true,
      "display": "28%",
      "key": "rca.cost.A.B.all.volume.share",
      "value": 0.28077990907223965
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.B.cacheRead.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.B.cacheRead.price.share",
      "value": 5.65413520631882E-18
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.B.cacheRead.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.B.cacheRead.volume.share",
      "value": 0.0032092426187419684
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.B.cacheWrite.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.B.cacheWrite.price.share",
      "value": -0
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.B.cacheWrite.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.B.cacheWrite.volume.share",
      "value": -0
    },
    {
      "available": true,
      "display": "split by token kind",
      "key": "rca.cost.A.B.grouped",
      "value": false
    },
    {
      "available": true,
      "display": "18–18%",
      "key": "rca.cost.A.B.output.price.interval"
    },
    {
      "available": true,
      "display": "18%",
      "key": "rca.cost.A.B.output.price.share",
      "value": 0.18112542291023093
    },
    {
      "available": true,
      "display": "10–10%",
      "key": "rca.cost.A.B.output.volume.interval"
    },
    {
      "available": true,
      "display": "10%",
      "key": "rca.cost.A.B.output.volume.share",
      "value": 0.09872053344406923
    },
    {
      "available": true,
      "display": "9–9%",
      "key": "rca.cost.A.B.reasoning.price.interval"
    },
    {
      "available": true,
      "display": "9%",
      "key": "rca.cost.A.B.reasoning.price.share",
      "value": 0.08719899521942158
    },
    {
      "available": true,
      "display": "11–12%",
      "key": "rca.cost.A.B.reasoning.volume.interval"
    },
    {
      "available": true,
      "display": "11%",
      "key": "rca.cost.A.B.reasoning.volume.share",
      "value": 0.11305774419007787
    },
    {
      "available": true,
      "display": "-$0.0078 per question",
      "key": "rca.cost.A.B.total",
      "value": -0.007790000000000002
    },
    {
      "available": true,
      "display": "44–46%",
      "key": "rca.cost.A.B.uncachedInput.price.interval"
    },
    {
      "available": true,
      "display": "45%",
      "key": "rca.cost.A.B.uncachedInput.price.share",
      "value": 0.45089567279810744
    },
    {
      "available": true,
      "display": "6–7%",
      "key": "rca.cost.A.B.uncachedInput.volume.interval"
    },
    {
      "available": true,
      "display": "7%",
      "key": "rca.cost.A.B.uncachedInput.volume.share",
      "value": 0.06579238881935058
    },
    {
      "available": true,
      "display": "0.9 times",
      "key": "rca.cost.A.B.volumeRatio",
      "value": 0.8832687741305474
    },
    {
      "available": true,
      "display": "70–71%",
      "key": "rca.cost.A.C.all.price.interval"
    },
    {
      "available": true,
      "display": "71%",
      "key": "rca.cost.A.C.all.price.share",
      "value": 0.7067857366013377
    },
    {
      "available": true,
      "display": "29–30%",
      "key": "rca.cost.A.C.all.volume.interval"
    },
    {
      "available": true,
      "display": "29%",
      "key": "rca.cost.A.C.all.volume.share",
      "value": 0.29321426339866247
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.C.cacheRead.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.C.cacheRead.price.share",
      "value": -7.268935900156093E-18
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.C.cacheRead.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.C.cacheRead.volume.share",
      "value": 0.0029797377830750966
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.C.cacheWrite.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.C.cacheWrite.price.share",
      "value": -0
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.C.cacheWrite.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.C.cacheWrite.volume.share",
      "value": -0
    },
    {
      "available": true,
      "display": "split by token kind",
      "key": "rca.cost.A.C.grouped",
      "value": false
    },
    {
      "available": true,
      "display": "17–18%",
      "key": "rca.cost.A.C.output.price.interval"
    },
    {
      "available": true,
      "display": "18%",
      "key": "rca.cost.A.C.output.price.share",
      "value": 0.17680749428237597
    },
    {
      "available": true,
      "display": "10–10%",
      "key": "rca.cost.A.C.output.volume.interval"
    },
    {
      "available": true,
      "display": "10%",
      "key": "rca.cost.A.C.output.volume.share",
      "value": 0.09971217198699239
    },
    {
      "available": true,
      "display": "9–9%",
      "key": "rca.cost.A.C.reasoning.price.interval"
    },
    {
      "available": true,
      "display": "9%",
      "key": "rca.cost.A.C.reasoning.price.share",
      "value": 0.09029630352104531
    },
    {
      "available": true,
      "display": "11–12%",
      "key": "rca.cost.A.C.reasoning.volume.interval"
    },
    {
      "available": true,
      "display": "11%",
      "key": "rca.cost.A.C.reasoning.volume.share",
      "value": 0.11470965595452089
    },
    {
      "available": true,
      "display": "-$0.017 per question",
      "key": "rca.cost.A.C.total",
      "value": -0.01678
    },
    {
      "available": true,
      "display": "43–45%",
      "key": "rca.cost.A.C.uncachedInput.price.interval"
    },
    {
      "available": true,
      "display": "44%",
      "key": "rca.cost.A.C.uncachedInput.price.share",
      "value": 0.4396819387979164
    },
    {
      "available": true,
      "display": "7–8%",
      "key": "rca.cost.A.C.uncachedInput.volume.interval"
    },
    {
      "available": true,
      "display": "8%",
      "key": "rca.cost.A.C.uncachedInput.volume.share",
      "value": 0.07581269767407409
    },
    {
      "available": true,
      "display": "0.8 times",
      "key": "rca.cost.A.C.volumeRatio",
      "value": 0.7938554761823443
    },
    {
      "available": true,
      "display": "69–70%",
      "key": "rca.cost.A.D.all.price.interval"
    },
    {
      "available": true,
      "display": "70%",
      "key": "rca.cost.A.D.all.price.share",
      "value": 0.6968068304832366
    },
    {
      "available": true,
      "display": "30–31%",
      "key": "rca.cost.A.D.all.volume.interval"
    },
    {
      "available": true,
      "display": "30%",
      "key": "rca.cost.A.D.all.volume.share",
      "value": 0.3031931695167636
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.D.cacheRead.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.D.cacheRead.price.share",
      "value": -4.522534089900603E-18
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.D.cacheRead.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.D.cacheRead.volume.share",
      "value": 0.0027808676307007835
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.D.cacheWrite.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.D.cacheWrite.price.share",
      "value": -0
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.D.cacheWrite.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.D.cacheWrite.volume.share",
      "value": -0
    },
    {
      "available": true,
      "display": "split by token kind",
      "key": "rca.cost.A.D.grouped",
      "value": false
    },
    {
      "available": true,
      "display": "17–18%",
      "key": "rca.cost.A.D.output.price.interval"
    },
    {
      "available": true,
      "display": "17%",
      "key": "rca.cost.A.D.output.price.share",
      "value": 0.1731783963649792
    },
    {
      "available": true,
      "display": "10–10%",
      "key": "rca.cost.A.D.output.volume.interval"
    },
    {
      "available": true,
      "display": "10%",
      "key": "rca.cost.A.D.output.volume.share",
      "value": 0.10045897849597754
    },
    {
      "available": true,
      "display": "9–9%",
      "key": "rca.cost.A.D.reasoning.price.interval"
    },
    {
      "available": true,
      "display": "9%",
      "key": "rca.cost.A.D.reasoning.price.share",
      "value": 0.09291798670837466
    },
    {
      "available": true,
      "display": "11–12%",
      "key": "rca.cost.A.D.reasoning.volume.interval"
    },
    {
      "available": true,
      "display": "12%",
      "key": "rca.cost.A.D.reasoning.volume.share",
      "value": 0.11620325912032395
    },
    {
      "available": true,
      "display": "-$0.027 per question",
      "key": "rca.cost.A.D.total",
      "value": -0.026969999999999997
    },
    {
      "available": true,
      "display": "42–44%",
      "key": "rca.cost.A.D.uncachedInput.price.interval"
    },
    {
      "available": true,
      "display": "43%",
      "key": "rca.cost.A.D.uncachedInput.price.share",
      "value": 0.4307104474098827
    },
    {
      "available": true,
      "display": "8–9%",
      "key": "rca.cost.A.D.uncachedInput.volume.interval"
    },
    {
      "available": true,
      "display": "8%",
      "key": "rca.cost.A.D.uncachedInput.volume.share",
      "value": 0.08375006426976137
    },
    {
      "available": true,
      "display": "0.7 times",
      "key": "rca.cost.A.D.volumeRatio",
      "value": 0.7222488715266346
    },
    {
      "available": true,
      "display": "68–69%",
      "key": "rca.cost.A.E.all.price.interval"
    },
    {
      "available": true,
      "display": "69%",
      "key": "rca.cost.A.E.all.price.share",
      "value": 0.6885105330251258
    },
    {
      "available": true,
      "display": "31–32%",
      "key": "rca.cost.A.E.all.volume.interval"
    },
    {
      "available": true,
      "display": "31%",
      "key": "rca.cost.A.E.all.volume.share",
      "value": 0.31148946697487445
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.E.cacheRead.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.E.cacheRead.price.share",
      "value": -1.4131936554816275E-18
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.E.cacheRead.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.E.cacheRead.volume.share",
      "value": 0.0026068821689259657
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.E.cacheWrite.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.E.cacheWrite.price.share",
      "value": -0
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.A.E.cacheWrite.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.A.E.cacheWrite.volume.share",
      "value": -0
    },
    {
      "available": true,
      "display": "split by token kind",
      "key": "rca.cost.A.E.grouped",
      "value": false
    },
    {
      "available": true,
      "display": "17–17%",
      "key": "rca.cost.A.E.output.price.interval"
    },
    {
      "available": true,
      "display": "17%",
      "key": "rca.cost.A.E.output.price.share",
      "value": 0.17007808201744268
    },
    {
      "available": true,
      "display": "10–10%",
      "key": "rca.cost.A.E.output.volume.interval"
    },
    {
      "available": true,
      "display": "10%",
      "key": "rca.cost.A.E.output.volume.share",
      "value": 0.10103766355085769
    },
    {
      "available": true,
      "display": "9–10%",
      "key": "rca.cost.A.E.reasoning.price.interval"
    },
    {
      "available": true,
      "display": "10%",
      "key": "rca.cost.A.E.reasoning.price.share",
      "value": 0.09517595976166772
    },
    {
      "available": true,
      "display": "12–12%",
      "key": "rca.cost.A.E.reasoning.volume.interval"
    },
    {
      "available": true,
      "display": "12%",
      "key": "rca.cost.A.E.reasoning.volume.share",
      "value": 0.117545625222691
    },
    {
      "available": true,
      "display": "-$0.038 per question",
      "key": "rca.cost.A.E.total",
      "value": -0.03835999999999999
    },
    {
      "available": true,
      "display": "42–43%",
      "key": "rca.cost.A.E.uncachedInput.price.interval"
    },
    {
      "available": true,
      "display": "42%",
      "key": "rca.cost.A.E.uncachedInput.price.share",
      "value": 0.4232564912460154
    },
    {
      "available": true,
      "display": "9–9%",
      "key": "rca.cost.A.E.uncachedInput.volume.interval"
    },
    {
      "available": true,
      "display": "9%",
      "key": "rca.cost.A.E.uncachedInput.volume.share",
      "value": 0.09029929603239982
    },
    {
      "available": true,
      "display": "0.7 times",
      "key": "rca.cost.A.E.volumeRatio",
      "value": 0.663200689125152
    },
    {
      "available": true,
      "display": "64–65%",
      "key": "rca.cost.D.E.all.price.interval"
    },
    {
      "available": true,
      "display": "65%",
      "key": "rca.cost.D.E.all.price.share",
      "value": 0.6491469867604399
    },
    {
      "available": true,
      "display": "35–36%",
      "key": "rca.cost.D.E.all.volume.interval"
    },
    {
      "available": true,
      "display": "35%",
      "key": "rca.cost.D.E.all.volume.share",
      "value": 0.35085301323956025
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.D.E.cacheRead.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.D.E.cacheRead.price.share",
      "value": 5.651844072987431E-18
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.D.E.cacheRead.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.D.E.cacheRead.volume.share",
      "value": 0.0021949078138718083
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.D.E.cacheWrite.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.D.E.cacheWrite.price.share",
      "value": -0
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.D.E.cacheWrite.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.D.E.cacheWrite.volume.share",
      "value": -0
    },
    {
      "available": true,
      "display": "split by token kind",
      "key": "rca.cost.D.E.grouped",
      "value": false
    },
    {
      "available": true,
      "display": "16–16%",
      "key": "rca.cost.D.E.output.price.interval"
    },
    {
      "available": true,
      "display": "16%",
      "key": "rca.cost.D.E.output.price.share",
      "value": 0.16070948285114076
    },
    {
      "available": true,
      "display": "10–11%",
      "key": "rca.cost.D.E.output.volume.interval"
    },
    {
      "available": true,
      "display": "10%",
      "key": "rca.cost.D.E.output.volume.share",
      "value": 0.10443538106457471
    },
    {
      "available": true,
      "display": "10–10%",
      "key": "rca.cost.D.E.reasoning.price.interval"
    },
    {
      "available": true,
      "display": "10%",
      "key": "rca.cost.D.E.reasoning.price.share",
      "value": 0.10181989333148497
    },
    {
      "available": true,
      "display": "12–12%",
      "key": "rca.cost.D.E.reasoning.volume.interval"
    },
    {
      "available": true,
      "display": "12%",
      "key": "rca.cost.D.E.reasoning.volume.share",
      "value": 0.11942681430679408
    },
    {
      "available": true,
      "display": "-$0.011 per question",
      "key": "rca.cost.D.E.total",
      "value": -0.011389999999999997
    },
    {
      "available": true,
      "display": "38–39%",
      "key": "rca.cost.D.E.uncachedInput.price.interval"
    },
    {
      "available": true,
      "display": "39%",
      "key": "rca.cost.D.E.uncachedInput.price.share",
      "value": 0.38661761057781424
    },
    {
      "available": true,
      "display": "12–13%",
      "key": "rca.cost.D.E.uncachedInput.volume.interval"
    },
    {
      "available": true,
      "display": "12%",
      "key": "rca.cost.D.E.uncachedInput.volume.share",
      "value": 0.12479591005431967
    },
    {
      "available": true,
      "display": "0.9 times",
      "key": "rca.cost.D.E.volumeRatio",
      "value": 0.9175557258189582
    },
    {
      "available": true,
      "display": "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted).",
      "key": "rca.hypothesis.H0-C.1.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.H0-C.1.axis"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-C.1.for"
    },
    {
      "available": true,
      "display": "Rejected — a real difference (Not supported)",
      "key": "rca.hypothesis.H0-C.1.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)",
      "key": "rca.hypothesis.H0-C.1.gradeReasons"
    },
    {
      "available": true,
      "display": "no cost difference between Model A and Model B",
      "key": "rca.hypothesis.H0-C.1.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-C.1.locus"
    },
    {
      "available": true,
      "display": "Model A and Model B",
      "key": "rca.hypothesis.H0-C.1.models"
    },
    {
      "available": true,
      "display": "H0-C.1",
      "key": "rca.hypothesis.H0-C.1.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the cost per question is established after adjustment.",
      "key": "rca.hypothesis.H0-C.1.refutedIf"
    },
    {
      "available": true,
      "display": "The cost per question difference between Model A and Model B is within sampling noise.",
      "key": "rca.hypothesis.H0-C.1.statement"
    },
    {
      "available": true,
      "display": "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted).",
      "key": "rca.hypothesis.H0-C.2.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.H0-C.2.axis"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-C.2.for"
    },
    {
      "available": true,
      "display": "Rejected — a real difference (Not supported)",
      "key": "rca.hypothesis.H0-C.2.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)",
      "key": "rca.hypothesis.H0-C.2.gradeReasons"
    },
    {
      "available": true,
      "display": "no cost difference between Model A and Model C",
      "key": "rca.hypothesis.H0-C.2.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-C.2.locus"
    },
    {
      "available": true,
      "display": "Model A and Model C",
      "key": "rca.hypothesis.H0-C.2.models"
    },
    {
      "available": true,
      "display": "H0-C.2",
      "key": "rca.hypothesis.H0-C.2.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the cost per question is established after adjustment.",
      "key": "rca.hypothesis.H0-C.2.refutedIf"
    },
    {
      "available": true,
      "display": "The cost per question difference between Model A and Model C is within sampling noise.",
      "key": "rca.hypothesis.H0-C.2.statement"
    },
    {
      "available": true,
      "display": "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted).",
      "key": "rca.hypothesis.H0-C.3.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.H0-C.3.axis"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-C.3.for"
    },
    {
      "available": true,
      "display": "Rejected — a real difference (Not supported)",
      "key": "rca.hypothesis.H0-C.3.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)",
      "key": "rca.hypothesis.H0-C.3.gradeReasons"
    },
    {
      "available": true,
      "display": "no cost difference between Model A and Model D",
      "key": "rca.hypothesis.H0-C.3.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-C.3.locus"
    },
    {
      "available": true,
      "display": "Model A and Model D",
      "key": "rca.hypothesis.H0-C.3.models"
    },
    {
      "available": true,
      "display": "H0-C.3",
      "key": "rca.hypothesis.H0-C.3.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the cost per question is established after adjustment.",
      "key": "rca.hypothesis.H0-C.3.refutedIf"
    },
    {
      "available": true,
      "display": "The cost per question difference between Model A and Model D is within sampling noise.",
      "key": "rca.hypothesis.H0-C.3.statement"
    },
    {
      "available": true,
      "display": "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted).",
      "key": "rca.hypothesis.H0-C.4.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.H0-C.4.axis"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-C.4.for"
    },
    {
      "available": true,
      "display": "Rejected — a real difference (Not supported)",
      "key": "rca.hypothesis.H0-C.4.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)",
      "key": "rca.hypothesis.H0-C.4.gradeReasons"
    },
    {
      "available": true,
      "display": "no cost difference between Model A and Model E",
      "key": "rca.hypothesis.H0-C.4.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-C.4.locus"
    },
    {
      "available": true,
      "display": "Model A and Model E",
      "key": "rca.hypothesis.H0-C.4.models"
    },
    {
      "available": true,
      "display": "H0-C.4",
      "key": "rca.hypothesis.H0-C.4.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the cost per question is established after adjustment.",
      "key": "rca.hypothesis.H0-C.4.refutedIf"
    },
    {
      "available": true,
      "display": "The cost per question difference between Model A and Model E is within sampling noise.",
      "key": "rca.hypothesis.H0-C.4.statement"
    },
    {
      "available": true,
      "display": "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted).",
      "key": "rca.hypothesis.H0-C.5.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.H0-C.5.axis"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-C.5.for"
    },
    {
      "available": true,
      "display": "Rejected — a real difference (Not supported)",
      "key": "rca.hypothesis.H0-C.5.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)",
      "key": "rca.hypothesis.H0-C.5.gradeReasons"
    },
    {
      "available": true,
      "display": "no cost difference between Model D and Model E",
      "key": "rca.hypothesis.H0-C.5.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-C.5.locus"
    },
    {
      "available": true,
      "display": "Model D and Model E",
      "key": "rca.hypothesis.H0-C.5.models"
    },
    {
      "available": true,
      "display": "H0-C.5",
      "key": "rca.hypothesis.H0-C.5.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the cost per question is established after adjustment.",
      "key": "rca.hypothesis.H0-C.5.refutedIf"
    },
    {
      "available": true,
      "display": "The cost per question difference between Model D and Model E is within sampling noise.",
      "key": "rca.hypothesis.H0-C.5.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-Q.1.against"
    },
    {
      "available": true,
      "display": "Intelligence",
      "key": "rca.hypothesis.H0-Q.1.axis"
    },
    {
      "available": true,
      "display": "The paired Intelligence Index test is not established after the Holm adjustment across 4 tests. The paired Intelligence Index test is not established after the Holm adjustment across 10 tests. The two 95% intervals overlap.",
      "key": "rca.hypothesis.H0-Q.1.for"
    },
    {
      "available": true,
      "display": "Stands — may be chance (Low)",
      "key": "rca.hypothesis.H0-Q.1.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "with one run per suite, the tests can miss a real difference (grade capped at Low)",
      "key": "rca.hypothesis.H0-Q.1.gradeReasons"
    },
    {
      "available": true,
      "display": "no intelligence difference between Model A and Model B",
      "key": "rca.hypothesis.H0-Q.1.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-Q.1.locus"
    },
    {
      "available": true,
      "display": "Model A and Model B",
      "key": "rca.hypothesis.H0-Q.1.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.H0-Q.1.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "H0-Q.1",
      "key": "rca.hypothesis.H0-Q.1.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the Intelligence Index is established after adjustment.",
      "key": "rca.hypothesis.H0-Q.1.refutedIf"
    },
    {
      "available": true,
      "display": "The Intelligence Index difference between Model A and Model B is within sampling noise.",
      "key": "rca.hypothesis.H0-Q.1.statement"
    },
    {
      "available": true,
      "display": "The two 95% intervals do not overlap.",
      "key": "rca.hypothesis.H0-Q.2.against"
    },
    {
      "available": true,
      "display": "Intelligence",
      "key": "rca.hypothesis.H0-Q.2.axis"
    },
    {
      "available": true,
      "display": "The paired Intelligence Index test is not established after the Holm adjustment across 4 tests. The paired Intelligence Index test is not established after the Holm adjustment across 10 tests.",
      "key": "rca.hypothesis.H0-Q.2.for"
    },
    {
      "available": true,
      "display": "Stands — may be chance (Low)",
      "key": "rca.hypothesis.H0-Q.2.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "with one run per suite, the tests can miss a real difference (grade capped at Low)",
      "key": "rca.hypothesis.H0-Q.2.gradeReasons"
    },
    {
      "available": true,
      "display": "no intelligence difference between Model A and Model C",
      "key": "rca.hypothesis.H0-Q.2.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-Q.2.locus"
    },
    {
      "available": true,
      "display": "Model A and Model C",
      "key": "rca.hypothesis.H0-Q.2.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.H0-Q.2.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "H0-Q.2",
      "key": "rca.hypothesis.H0-Q.2.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the Intelligence Index is established after adjustment.",
      "key": "rca.hypothesis.H0-Q.2.refutedIf"
    },
    {
      "available": true,
      "display": "The Intelligence Index difference between Model A and Model C is within sampling noise.",
      "key": "rca.hypothesis.H0-Q.2.statement"
    },
    {
      "available": true,
      "display": "The two 95% intervals do not overlap.",
      "key": "rca.hypothesis.H0-Q.3.against"
    },
    {
      "available": true,
      "display": "Intelligence",
      "key": "rca.hypothesis.H0-Q.3.axis"
    },
    {
      "available": true,
      "display": "The paired Intelligence Index test is not established after the Holm adjustment across 4 tests. The paired Intelligence Index test is not established after the Holm adjustment across 10 tests.",
      "key": "rca.hypothesis.H0-Q.3.for"
    },
    {
      "available": true,
      "display": "Stands — may be chance (Low)",
      "key": "rca.hypothesis.H0-Q.3.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "with one run per suite, the tests can miss a real difference (grade capped at Low)",
      "key": "rca.hypothesis.H0-Q.3.gradeReasons"
    },
    {
      "available": true,
      "display": "no intelligence difference between Model A and Model D",
      "key": "rca.hypothesis.H0-Q.3.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-Q.3.locus"
    },
    {
      "available": true,
      "display": "Model A and Model D",
      "key": "rca.hypothesis.H0-Q.3.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.H0-Q.3.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "H0-Q.3",
      "key": "rca.hypothesis.H0-Q.3.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the Intelligence Index is established after adjustment.",
      "key": "rca.hypothesis.H0-Q.3.refutedIf"
    },
    {
      "available": true,
      "display": "The Intelligence Index difference between Model A and Model D is within sampling noise.",
      "key": "rca.hypothesis.H0-Q.3.statement"
    },
    {
      "available": true,
      "display": "The two 95% intervals do not overlap.",
      "key": "rca.hypothesis.H0-Q.4.against"
    },
    {
      "available": true,
      "display": "Intelligence",
      "key": "rca.hypothesis.H0-Q.4.axis"
    },
    {
      "available": true,
      "display": "The paired Intelligence Index test is not established after the Holm adjustment across 4 tests. The paired Intelligence Index test is not established after the Holm adjustment across 10 tests.",
      "key": "rca.hypothesis.H0-Q.4.for"
    },
    {
      "available": true,
      "display": "Stands — may be chance (Low)",
      "key": "rca.hypothesis.H0-Q.4.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "with one run per suite, the tests can miss a real difference (grade capped at Low)",
      "key": "rca.hypothesis.H0-Q.4.gradeReasons"
    },
    {
      "available": true,
      "display": "no intelligence difference between Model A and Model E",
      "key": "rca.hypothesis.H0-Q.4.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-Q.4.locus"
    },
    {
      "available": true,
      "display": "Model A and Model E",
      "key": "rca.hypothesis.H0-Q.4.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.H0-Q.4.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "H0-Q.4",
      "key": "rca.hypothesis.H0-Q.4.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the Intelligence Index is established after adjustment.",
      "key": "rca.hypothesis.H0-Q.4.refutedIf"
    },
    {
      "available": true,
      "display": "The Intelligence Index difference between Model A and Model E is within sampling noise.",
      "key": "rca.hypothesis.H0-Q.4.statement"
    },
    {
      "available": true,
      "display": "The two 95% intervals do not overlap.",
      "key": "rca.hypothesis.H0-Q.5.against"
    },
    {
      "available": true,
      "display": "Intelligence",
      "key": "rca.hypothesis.H0-Q.5.axis"
    },
    {
      "available": true,
      "display": "The paired Intelligence Index test is not established after the Holm adjustment across 10 tests.",
      "key": "rca.hypothesis.H0-Q.5.for"
    },
    {
      "available": true,
      "display": "Stands — may be chance (Low)",
      "key": "rca.hypothesis.H0-Q.5.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "with one run per suite, the tests can miss a real difference (grade capped at Low)",
      "key": "rca.hypothesis.H0-Q.5.gradeReasons"
    },
    {
      "available": true,
      "display": "no intelligence difference between Model D and Model E",
      "key": "rca.hypothesis.H0-Q.5.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-Q.5.locus"
    },
    {
      "available": true,
      "display": "Model D and Model E",
      "key": "rca.hypothesis.H0-Q.5.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.H0-Q.5.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "H0-Q.5",
      "key": "rca.hypothesis.H0-Q.5.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the Intelligence Index is established after adjustment.",
      "key": "rca.hypothesis.H0-Q.5.refutedIf"
    },
    {
      "available": true,
      "display": "The Intelligence Index difference between Model D and Model E is within sampling noise.",
      "key": "rca.hypothesis.H0-Q.5.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-S.1.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.H0-S.1.axis"
    },
    {
      "available": true,
      "display": "The paired model time test is not established after the Holm adjustment across 4 tests. The paired model time test is not established after the Holm adjustment across 10 tests.",
      "key": "rca.hypothesis.H0-S.1.for"
    },
    {
      "available": true,
      "display": "Stands — may be chance (Low)",
      "key": "rca.hypothesis.H0-S.1.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "with one run per suite, the tests can miss a real difference (grade capped at Low)",
      "key": "rca.hypothesis.H0-S.1.gradeReasons"
    },
    {
      "available": true,
      "display": "no speed difference between Model A and Model B",
      "key": "rca.hypothesis.H0-S.1.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-S.1.locus"
    },
    {
      "available": true,
      "display": "Model A and Model B",
      "key": "rca.hypothesis.H0-S.1.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.H0-S.1.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "H0-S.1",
      "key": "rca.hypothesis.H0-S.1.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the model time is established after adjustment.",
      "key": "rca.hypothesis.H0-S.1.refutedIf"
    },
    {
      "available": true,
      "display": "The model time difference between Model A and Model B is within sampling noise.",
      "key": "rca.hypothesis.H0-S.1.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-S.2.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.H0-S.2.axis"
    },
    {
      "available": true,
      "display": "The paired model time test is not established after the Holm adjustment across 4 tests. The paired model time test is not established after the Holm adjustment across 10 tests.",
      "key": "rca.hypothesis.H0-S.2.for"
    },
    {
      "available": true,
      "display": "Stands — may be chance (Low)",
      "key": "rca.hypothesis.H0-S.2.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "with one run per suite, the tests can miss a real difference (grade capped at Low)",
      "key": "rca.hypothesis.H0-S.2.gradeReasons"
    },
    {
      "available": true,
      "display": "no speed difference between Model A and Model C",
      "key": "rca.hypothesis.H0-S.2.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-S.2.locus"
    },
    {
      "available": true,
      "display": "Model A and Model C",
      "key": "rca.hypothesis.H0-S.2.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.H0-S.2.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "H0-S.2",
      "key": "rca.hypothesis.H0-S.2.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the model time is established after adjustment.",
      "key": "rca.hypothesis.H0-S.2.refutedIf"
    },
    {
      "available": true,
      "display": "The model time difference between Model A and Model C is within sampling noise.",
      "key": "rca.hypothesis.H0-S.2.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-S.3.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.H0-S.3.axis"
    },
    {
      "available": true,
      "display": "The paired model time test is not established after the Holm adjustment across 4 tests. The paired model time test is not established after the Holm adjustment across 10 tests.",
      "key": "rca.hypothesis.H0-S.3.for"
    },
    {
      "available": true,
      "display": "Stands — may be chance (Low)",
      "key": "rca.hypothesis.H0-S.3.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "with one run per suite, the tests can miss a real difference (grade capped at Low)",
      "key": "rca.hypothesis.H0-S.3.gradeReasons"
    },
    {
      "available": true,
      "display": "no speed difference between Model A and Model D",
      "key": "rca.hypothesis.H0-S.3.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-S.3.locus"
    },
    {
      "available": true,
      "display": "Model A and Model D",
      "key": "rca.hypothesis.H0-S.3.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.H0-S.3.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "H0-S.3",
      "key": "rca.hypothesis.H0-S.3.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the model time is established after adjustment.",
      "key": "rca.hypothesis.H0-S.3.refutedIf"
    },
    {
      "available": true,
      "display": "The model time difference between Model A and Model D is within sampling noise.",
      "key": "rca.hypothesis.H0-S.3.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-S.4.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.H0-S.4.axis"
    },
    {
      "available": true,
      "display": "The paired model time test is not established after the Holm adjustment across 4 tests. The paired model time test is not established after the Holm adjustment across 10 tests.",
      "key": "rca.hypothesis.H0-S.4.for"
    },
    {
      "available": true,
      "display": "Stands — may be chance (Low)",
      "key": "rca.hypothesis.H0-S.4.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "with one run per suite, the tests can miss a real difference (grade capped at Low)",
      "key": "rca.hypothesis.H0-S.4.gradeReasons"
    },
    {
      "available": true,
      "display": "no speed difference between Model A and Model E",
      "key": "rca.hypothesis.H0-S.4.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-S.4.locus"
    },
    {
      "available": true,
      "display": "Model A and Model E",
      "key": "rca.hypothesis.H0-S.4.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.H0-S.4.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "H0-S.4",
      "key": "rca.hypothesis.H0-S.4.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the model time is established after adjustment.",
      "key": "rca.hypothesis.H0-S.4.refutedIf"
    },
    {
      "available": true,
      "display": "The model time difference between Model A and Model E is within sampling noise.",
      "key": "rca.hypothesis.H0-S.4.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-S.5.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.H0-S.5.axis"
    },
    {
      "available": true,
      "display": "The paired model time test is not established after the Holm adjustment across 10 tests.",
      "key": "rca.hypothesis.H0-S.5.for"
    },
    {
      "available": true,
      "display": "Stands — may be chance (Low)",
      "key": "rca.hypothesis.H0-S.5.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "with one run per suite, the tests can miss a real difference (grade capped at Low)",
      "key": "rca.hypothesis.H0-S.5.gradeReasons"
    },
    {
      "available": true,
      "display": "no speed difference between Model D and Model E",
      "key": "rca.hypothesis.H0-S.5.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-S.5.locus"
    },
    {
      "available": true,
      "display": "Model D and Model E",
      "key": "rca.hypothesis.H0-S.5.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.H0-S.5.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "H0-S.5",
      "key": "rca.hypothesis.H0-S.5.ref"
    },
    {
      "available": true,
      "display": "A paired test of the two on the model time is established after adjustment.",
      "key": "rca.hypothesis.H0-S.5.refutedIf"
    },
    {
      "available": true,
      "display": "The model time difference between Model D and Model E is within sampling noise.",
      "key": "rca.hypothesis.H0-S.5.statement"
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
      "display": "Stands — may be chance (Low)",
      "key": "rca.hypothesis.H0-W.1.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "with one run per suite, the tests can miss a real difference (grade capped at Low)",
      "key": "rca.hypothesis.H0-W.1.gradeReasons"
    },
    {
      "available": true,
      "display": "no difference within Model A's own questions",
      "key": "rca.hypothesis.H0-W.1.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-W.1.locus"
    },
    {
      "available": true,
      "display": "Model A",
      "key": "rca.hypothesis.H0-W.1.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.H0-W.1.nextRun",
      "value": 1
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
      "display": "The contrasts between groups of Model A's own questions are within sampling noise.",
      "key": "rca.hypothesis.H0-W.1.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-W.2.against"
    },
    {
      "available": true,
      "display": "Intelligence",
      "key": "rca.hypothesis.H0-W.2.axis"
    },
    {
      "available": true,
      "display": "No contrast could be made.",
      "key": "rca.hypothesis.H0-W.2.for"
    },
    {
      "available": true,
      "display": "Stands — may be chance (Low)",
      "key": "rca.hypothesis.H0-W.2.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "with one run per suite, the tests can miss a real difference (grade capped at Low)",
      "key": "rca.hypothesis.H0-W.2.gradeReasons"
    },
    {
      "available": true,
      "display": "no difference within Model E's own questions",
      "key": "rca.hypothesis.H0-W.2.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-W.2.locus"
    },
    {
      "available": true,
      "display": "Model E",
      "key": "rca.hypothesis.H0-W.2.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.H0-W.2.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "H0-W.2",
      "key": "rca.hypothesis.H0-W.2.ref"
    },
    {
      "available": true,
      "display": "A Mann–Whitney contrast is established after the Holm adjustment.",
      "key": "rca.hypothesis.H0-W.2.refutedIf"
    },
    {
      "available": true,
      "display": "The contrasts between groups of Model E's own questions are within sampling noise.",
      "key": "rca.hypothesis.H0-W.2.statement"
    },
    {
      "available": true,
      "display": "The volume effect is 31% (31–32%) of the difference.",
      "key": "rca.hypothesis.HC2.1.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.HC2.1.axis"
    },
    {
      "available": true,
      "display": "Price per token, all components accounts for 69% (68–69%) of the $0.038 per-question cost difference.",
      "key": "rca.hypothesis.HC2.1.for"
    },
    {
      "available": true,
      "display": "Known from the price cards (High)",
      "key": "rca.hypothesis.HC2.1.grade",
      "value": "High"
    },
    {
      "available": true,
      "display": "every condition of the grade is met",
      "key": "rca.hypothesis.HC2.1.gradeReasons"
    },
    {
      "available": true,
      "display": "68–69%",
      "key": "rca.hypothesis.HC2.1.interval"
    },
    {
      "available": true,
      "display": "Model E's higher price per token, compared with Model A",
      "key": "rca.hypothesis.HC2.1.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.HC2.1.locus"
    },
    {
      "available": true,
      "display": "Model E and Model A",
      "key": "rca.hypothesis.HC2.1.models"
    },
    {
      "available": true,
      "display": "known from the price cards, not a cause the runs found",
      "key": "rca.hypothesis.HC2.1.priceCard",
      "value": true
    },
    {
      "available": true,
      "display": "HC2.1",
      "key": "rca.hypothesis.HC2.1.ref"
    },
    {
      "available": true,
      "display": "Not testable by a run: the share follows from the price cards and these runs' token counts.",
      "key": "rca.hypothesis.HC2.1.refutedIf"
    },
    {
      "available": true,
      "display": "69%",
      "key": "rca.hypothesis.HC2.1.share",
      "value": 0.6885105330251258
    },
    {
      "available": true,
      "display": "of the gap",
      "key": "rca.hypothesis.HC2.1.shareOf"
    },
    {
      "available": true,
      "display": "Model E costs more than Model A mainly from its price per token.",
      "key": "rca.hypothesis.HC2.1.statement"
    },
    {
      "available": true,
      "display": "The volume effect is 30% (30–31%) of the difference.",
      "key": "rca.hypothesis.HC2.2.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.HC2.2.axis"
    },
    {
      "available": true,
      "display": "Price per token, all components accounts for 70% (69–70%) of the $0.027 per-question cost difference.",
      "key": "rca.hypothesis.HC2.2.for"
    },
    {
      "available": true,
      "display": "Known from the price cards (High)",
      "key": "rca.hypothesis.HC2.2.grade",
      "value": "High"
    },
    {
      "available": true,
      "display": "every condition of the grade is met",
      "key": "rca.hypothesis.HC2.2.gradeReasons"
    },
    {
      "available": true,
      "display": "69–70%",
      "key": "rca.hypothesis.HC2.2.interval"
    },
    {
      "available": true,
      "display": "Model D's higher price per token, compared with Model A",
      "key": "rca.hypothesis.HC2.2.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.HC2.2.locus"
    },
    {
      "available": true,
      "display": "Model D and Model A",
      "key": "rca.hypothesis.HC2.2.models"
    },
    {
      "available": true,
      "display": "known from the price cards, not a cause the runs found",
      "key": "rca.hypothesis.HC2.2.priceCard",
      "value": true
    },
    {
      "available": true,
      "display": "HC2.2",
      "key": "rca.hypothesis.HC2.2.ref"
    },
    {
      "available": true,
      "display": "Not testable by a run: the share follows from the price cards and these runs' token counts.",
      "key": "rca.hypothesis.HC2.2.refutedIf"
    },
    {
      "available": true,
      "display": "70%",
      "key": "rca.hypothesis.HC2.2.share",
      "value": 0.6968068304832366
    },
    {
      "available": true,
      "display": "of the gap",
      "key": "rca.hypothesis.HC2.2.shareOf"
    },
    {
      "available": true,
      "display": "Model D costs more than Model A mainly from its price per token.",
      "key": "rca.hypothesis.HC2.2.statement"
    },
    {
      "available": true,
      "display": "The volume effect is 29% (29–30%) of the difference.",
      "key": "rca.hypothesis.HC2.3.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.HC2.3.axis"
    },
    {
      "available": true,
      "display": "Price per token, all components accounts for 71% (70–71%) of the $0.017 per-question cost difference.",
      "key": "rca.hypothesis.HC2.3.for"
    },
    {
      "available": true,
      "display": "Known from the price cards (High)",
      "key": "rca.hypothesis.HC2.3.grade",
      "value": "High"
    },
    {
      "available": true,
      "display": "every condition of the grade is met",
      "key": "rca.hypothesis.HC2.3.gradeReasons"
    },
    {
      "available": true,
      "display": "70–71%",
      "key": "rca.hypothesis.HC2.3.interval"
    },
    {
      "available": true,
      "display": "Model C's higher price per token, compared with Model A",
      "key": "rca.hypothesis.HC2.3.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.HC2.3.locus"
    },
    {
      "available": true,
      "display": "Model C and Model A",
      "key": "rca.hypothesis.HC2.3.models"
    },
    {
      "available": true,
      "display": "known from the price cards, not a cause the runs found",
      "key": "rca.hypothesis.HC2.3.priceCard",
      "value": true
    },
    {
      "available": true,
      "display": "HC2.3",
      "key": "rca.hypothesis.HC2.3.ref"
    },
    {
      "available": true,
      "display": "Not testable by a run: the share follows from the price cards and these runs' token counts.",
      "key": "rca.hypothesis.HC2.3.refutedIf"
    },
    {
      "available": true,
      "display": "71%",
      "key": "rca.hypothesis.HC2.3.share",
      "value": 0.7067857366013377
    },
    {
      "available": true,
      "display": "of the gap",
      "key": "rca.hypothesis.HC2.3.shareOf"
    },
    {
      "available": true,
      "display": "Model C costs more than Model A mainly from its price per token.",
      "key": "rca.hypothesis.HC2.3.statement"
    },
    {
      "available": true,
      "display": "The volume effect is 35% (35–36%) of the difference.",
      "key": "rca.hypothesis.HC2.4.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.HC2.4.axis"
    },
    {
      "available": true,
      "display": "Price per token, all components accounts for 65% (64–65%) of the $0.011 per-question cost difference.",
      "key": "rca.hypothesis.HC2.4.for"
    },
    {
      "available": true,
      "display": "Known from the price cards (High)",
      "key": "rca.hypothesis.HC2.4.grade",
      "value": "High"
    },
    {
      "available": true,
      "display": "every condition of the grade is met",
      "key": "rca.hypothesis.HC2.4.gradeReasons"
    },
    {
      "available": true,
      "display": "64–65%",
      "key": "rca.hypothesis.HC2.4.interval"
    },
    {
      "available": true,
      "display": "Model E's higher price per token, compared with Model D",
      "key": "rca.hypothesis.HC2.4.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.HC2.4.locus"
    },
    {
      "available": true,
      "display": "Model E and Model D",
      "key": "rca.hypothesis.HC2.4.models"
    },
    {
      "available": true,
      "display": "known from the price cards, not a cause the runs found",
      "key": "rca.hypothesis.HC2.4.priceCard",
      "value": true
    },
    {
      "available": true,
      "display": "HC2.4",
      "key": "rca.hypothesis.HC2.4.ref"
    },
    {
      "available": true,
      "display": "Not testable by a run: the share follows from the price cards and these runs' token counts.",
      "key": "rca.hypothesis.HC2.4.refutedIf"
    },
    {
      "available": true,
      "display": "65%",
      "key": "rca.hypothesis.HC2.4.share",
      "value": 0.6491469867604399
    },
    {
      "available": true,
      "display": "of the gap",
      "key": "rca.hypothesis.HC2.4.shareOf"
    },
    {
      "available": true,
      "display": "Model E costs more than Model D mainly from its price per token.",
      "key": "rca.hypothesis.HC2.4.statement"
    },
    {
      "available": true,
      "display": "The volume effect is 28% (27–29%) of the difference.",
      "key": "rca.hypothesis.HC2.5.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.HC2.5.axis"
    },
    {
      "available": true,
      "display": "Price per token, all components accounts for 72% (71–73%) of the $0.0078 per-question cost difference.",
      "key": "rca.hypothesis.HC2.5.for"
    },
    {
      "available": true,
      "display": "Known from the price cards (High)",
      "key": "rca.hypothesis.HC2.5.grade",
      "value": "High"
    },
    {
      "available": true,
      "display": "every condition of the grade is met",
      "key": "rca.hypothesis.HC2.5.gradeReasons"
    },
    {
      "available": true,
      "display": "71–73%",
      "key": "rca.hypothesis.HC2.5.interval"
    },
    {
      "available": true,
      "display": "Model B's higher price per token, compared with Model A",
      "key": "rca.hypothesis.HC2.5.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.HC2.5.locus"
    },
    {
      "available": true,
      "display": "Model B and Model A",
      "key": "rca.hypothesis.HC2.5.models"
    },
    {
      "available": true,
      "display": "known from the price cards, not a cause the runs found",
      "key": "rca.hypothesis.HC2.5.priceCard",
      "value": true
    },
    {
      "available": true,
      "display": "HC2.5",
      "key": "rca.hypothesis.HC2.5.ref"
    },
    {
      "available": true,
      "display": "Not testable by a run: the share follows from the price cards and these runs' token counts.",
      "key": "rca.hypothesis.HC2.5.refutedIf"
    },
    {
      "available": true,
      "display": "72%",
      "key": "rca.hypothesis.HC2.5.share",
      "value": 0.71922009092776
    },
    {
      "available": true,
      "display": "of the gap",
      "key": "rca.hypothesis.HC2.5.shareOf"
    },
    {
      "available": true,
      "display": "Model B costs more than Model A mainly from its price per token.",
      "key": "rca.hypothesis.HC2.5.statement"
    },
    {
      "available": true,
      "display": "Model E's cache-read share is 28% against the models' median of 26%.",
      "key": "rca.hypothesis.HC4.1.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.HC4.1.axis"
    },
    {
      "available": true,
      "display": "Uncached input, token volume accounts for 9% (9–9%) of the $0.038 per-question cost difference.",
      "key": "rca.hypothesis.HC4.1.for"
    },
    {
      "available": true,
      "display": "Not supported",
      "key": "rca.hypothesis.HC4.1.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: Model E's cache-read share is 28% against the models' median of 26%",
      "key": "rca.hypothesis.HC4.1.gradeReasons"
    },
    {
      "available": true,
      "display": "9–9%",
      "key": "rca.hypothesis.HC4.1.interval"
    },
    {
      "available": true,
      "display": "Model E's weak prompt caching, compared with Model A",
      "key": "rca.hypothesis.HC4.1.label"
    },
    {
      "available": true,
      "display": "chat / provider",
      "key": "rca.hypothesis.HC4.1.locus"
    },
    {
      "available": true,
      "display": "Model E and Model A",
      "key": "rca.hypothesis.HC4.1.models"
    },
    {
      "available": true,
      "display": "HC4.1",
      "key": "rca.hypothesis.HC4.1.ref"
    },
    {
      "available": true,
      "display": "The cache-read share is comparable to the other models'.",
      "key": "rca.hypothesis.HC4.1.refutedIf"
    },
    {
      "available": true,
      "display": "9%",
      "key": "rca.hypothesis.HC4.1.share",
      "value": 0.09029929603239982
    },
    {
      "available": true,
      "display": "of the gap",
      "key": "rca.hypothesis.HC4.1.shareOf"
    },
    {
      "available": true,
      "display": "Weak prompt caching raises Model E's cost against Model A.",
      "key": "rca.hypothesis.HC4.1.statement"
    },
    {
      "available": true,
      "display": "Model D's cache-read share is 27% against the models' median of 26%.",
      "key": "rca.hypothesis.HC4.2.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.HC4.2.axis"
    },
    {
      "available": true,
      "display": "Uncached input, token volume accounts for 8% (8–9%) of the $0.027 per-question cost difference.",
      "key": "rca.hypothesis.HC4.2.for"
    },
    {
      "available": true,
      "display": "Not supported",
      "key": "rca.hypothesis.HC4.2.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: Model D's cache-read share is 27% against the models' median of 26%",
      "key": "rca.hypothesis.HC4.2.gradeReasons"
    },
    {
      "available": true,
      "display": "8–9%",
      "key": "rca.hypothesis.HC4.2.interval"
    },
    {
      "available": true,
      "display": "Model D's weak prompt caching, compared with Model A",
      "key": "rca.hypothesis.HC4.2.label"
    },
    {
      "available": true,
      "display": "chat / provider",
      "key": "rca.hypothesis.HC4.2.locus"
    },
    {
      "available": true,
      "display": "Model D and Model A",
      "key": "rca.hypothesis.HC4.2.models"
    },
    {
      "available": true,
      "display": "HC4.2",
      "key": "rca.hypothesis.HC4.2.ref"
    },
    {
      "available": true,
      "display": "The cache-read share is comparable to the other models'.",
      "key": "rca.hypothesis.HC4.2.refutedIf"
    },
    {
      "available": true,
      "display": "8%",
      "key": "rca.hypothesis.HC4.2.share",
      "value": 0.08375006426976137
    },
    {
      "available": true,
      "display": "of the gap",
      "key": "rca.hypothesis.HC4.2.shareOf"
    },
    {
      "available": true,
      "display": "Weak prompt caching raises Model D's cost against Model A.",
      "key": "rca.hypothesis.HC4.2.statement"
    },
    {
      "available": true,
      "display": "Model E's cache-read share is 28% against the models' median of 26%.",
      "key": "rca.hypothesis.HC4.3.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.HC4.3.axis"
    },
    {
      "available": true,
      "display": "Uncached input, token volume accounts for 12% (12–13%) of the $0.011 per-question cost difference.",
      "key": "rca.hypothesis.HC4.3.for"
    },
    {
      "available": true,
      "display": "Not supported",
      "key": "rca.hypothesis.HC4.3.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: Model E's cache-read share is 28% against the models' median of 26%",
      "key": "rca.hypothesis.HC4.3.gradeReasons"
    },
    {
      "available": true,
      "display": "12–13%",
      "key": "rca.hypothesis.HC4.3.interval"
    },
    {
      "available": true,
      "display": "Model E's weak prompt caching, compared with Model D",
      "key": "rca.hypothesis.HC4.3.label"
    },
    {
      "available": true,
      "display": "chat / provider",
      "key": "rca.hypothesis.HC4.3.locus"
    },
    {
      "available": true,
      "display": "Model E and Model D",
      "key": "rca.hypothesis.HC4.3.models"
    },
    {
      "available": true,
      "display": "HC4.3",
      "key": "rca.hypothesis.HC4.3.ref"
    },
    {
      "available": true,
      "display": "The cache-read share is comparable to the other models'.",
      "key": "rca.hypothesis.HC4.3.refutedIf"
    },
    {
      "available": true,
      "display": "12%",
      "key": "rca.hypothesis.HC4.3.share",
      "value": 0.12479591005431967
    },
    {
      "available": true,
      "display": "of the gap",
      "key": "rca.hypothesis.HC4.3.shareOf"
    },
    {
      "available": true,
      "display": "Weak prompt caching raises Model E's cost against Model D.",
      "key": "rca.hypothesis.HC4.3.statement"
    },
    {
      "available": true,
      "display": "Model C's cache-read share is 26% against the models' median of 26%.",
      "key": "rca.hypothesis.HC4.4.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.HC4.4.axis"
    },
    {
      "available": true,
      "display": "Uncached input, token volume accounts for 8% (7–8%) of the $0.017 per-question cost difference.",
      "key": "rca.hypothesis.HC4.4.for"
    },
    {
      "available": true,
      "display": "Not supported",
      "key": "rca.hypothesis.HC4.4.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: Model C's cache-read share is 26% against the models' median of 26%",
      "key": "rca.hypothesis.HC4.4.gradeReasons"
    },
    {
      "available": true,
      "display": "7–8%",
      "key": "rca.hypothesis.HC4.4.interval"
    },
    {
      "available": true,
      "display": "Model C's weak prompt caching, compared with Model A",
      "key": "rca.hypothesis.HC4.4.label"
    },
    {
      "available": true,
      "display": "chat / provider",
      "key": "rca.hypothesis.HC4.4.locus"
    },
    {
      "available": true,
      "display": "Model C and Model A",
      "key": "rca.hypothesis.HC4.4.models"
    },
    {
      "available": true,
      "display": "HC4.4",
      "key": "rca.hypothesis.HC4.4.ref"
    },
    {
      "available": true,
      "display": "The cache-read share is comparable to the other models'.",
      "key": "rca.hypothesis.HC4.4.refutedIf"
    },
    {
      "available": true,
      "display": "8%",
      "key": "rca.hypothesis.HC4.4.share",
      "value": 0.07581269767407409
    },
    {
      "available": true,
      "display": "of the gap",
      "key": "rca.hypothesis.HC4.4.shareOf"
    },
    {
      "available": true,
      "display": "Weak prompt caching raises Model C's cost against Model A.",
      "key": "rca.hypothesis.HC4.4.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.HQ3.1.against"
    },
    {
      "available": true,
      "display": "Intelligence",
      "key": "rca.hypothesis.HQ3.1.axis"
    },
    {
      "available": true,
      "display": "check 1",
      "key": "rca.hypothesis.HQ3.1.check",
      "value": 1
    },
    {
      "available": true,
      "display": "Q3: 4 of 5 models scored below 50.",
      "key": "rca.hypothesis.HQ3.1.for"
    },
    {
      "available": true,
      "display": "Low",
      "key": "rca.hypothesis.HQ3.1.grade",
      "value": "Low"
    },
    {
      "available": true,
      "display": "it is observed, not measured",
      "key": "rca.hypothesis.HQ3.1.gradeReasons"
    },
    {
      "available": true,
      "display": "shared failures on the same questions",
      "key": "rca.hypothesis.HQ3.1.label"
    },
    {
      "available": true,
      "display": "suite / corpus / chat",
      "key": "rca.hypothesis.HQ3.1.locus"
    },
    {
      "available": true,
      "display": "Model A, Model B, Model C, Model D and Model E",
      "key": "rca.hypothesis.HQ3.1.models"
    },
    {
      "available": true,
      "display": "HQ3.1",
      "key": "rca.hypothesis.HQ3.1.ref"
    },
    {
      "available": true,
      "display": "One model answered the question well.",
      "key": "rca.hypothesis.HQ3.1.refutedIf"
    },
    {
      "available": true,
      "display": "Shared failures on specific questions point to the instrument, not the models.",
      "key": "rca.hypothesis.HQ3.1.statement"
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
      "display": "Visible output and reasoning are 54% of Model E's cost per question.",
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
      "display": "Model E ran only once per suite",
      "key": "rca.hypothesis.WC1.1.gradeReasons"
    },
    {
      "available": true,
      "display": "Model E's output cost",
      "key": "rca.hypothesis.WC1.1.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.WC1.1.locus"
    },
    {
      "available": true,
      "display": "Model E",
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
      "display": "54%",
      "key": "rca.hypothesis.WC1.1.share",
      "value": 0.5395961621079766
    },
    {
      "available": true,
      "display": "of its cost",
      "key": "rca.hypothesis.WC1.1.shareOf"
    },
    {
      "available": true,
      "display": "Output (with reasoning) is Model E's largest cost component (where its cost goes, not a comparison).",
      "key": "rca.hypothesis.WC1.1.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.WC1.2.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.WC1.2.axis"
    },
    {
      "available": true,
      "display": "Visible output and reasoning are 55% of Model D's cost per question.",
      "key": "rca.hypothesis.WC1.2.for"
    },
    {
      "available": true,
      "display": "Moderate",
      "key": "rca.hypothesis.WC1.2.grade",
      "value": "Moderate"
    },
    {
      "available": true,
      "display": "Model D ran only once per suite",
      "key": "rca.hypothesis.WC1.2.gradeReasons"
    },
    {
      "available": true,
      "display": "Model D's output cost",
      "key": "rca.hypothesis.WC1.2.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.WC1.2.locus"
    },
    {
      "available": true,
      "display": "Model D",
      "key": "rca.hypothesis.WC1.2.models"
    },
    {
      "available": true,
      "display": "WC1.2",
      "key": "rca.hypothesis.WC1.2.ref"
    },
    {
      "available": true,
      "display": "Another component is larger.",
      "key": "rca.hypothesis.WC1.2.refutedIf"
    },
    {
      "available": true,
      "display": "55%",
      "key": "rca.hypothesis.WC1.2.share",
      "value": 0.5543829683401453
    },
    {
      "available": true,
      "display": "of its cost",
      "key": "rca.hypothesis.WC1.2.shareOf"
    },
    {
      "available": true,
      "display": "Output (with reasoning) is Model D's largest cost component (where its cost goes, not a comparison).",
      "key": "rca.hypothesis.WC1.2.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.WC1.3.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.WC1.3.axis"
    },
    {
      "available": true,
      "display": "Visible output and reasoning are 69% of Model A's cost per question.",
      "key": "rca.hypothesis.WC1.3.for"
    },
    {
      "available": true,
      "display": "Moderate",
      "key": "rca.hypothesis.WC1.3.grade",
      "value": "Moderate"
    },
    {
      "available": true,
      "display": "Model A ran only once per suite",
      "key": "rca.hypothesis.WC1.3.gradeReasons"
    },
    {
      "available": true,
      "display": "Model A's output cost",
      "key": "rca.hypothesis.WC1.3.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.WC1.3.locus"
    },
    {
      "available": true,
      "display": "Model A",
      "key": "rca.hypothesis.WC1.3.models"
    },
    {
      "available": true,
      "display": "WC1.3",
      "key": "rca.hypothesis.WC1.3.ref"
    },
    {
      "available": true,
      "display": "Another component is larger.",
      "key": "rca.hypothesis.WC1.3.refutedIf"
    },
    {
      "available": true,
      "display": "69%",
      "key": "rca.hypothesis.WC1.3.share",
      "value": 0.6922390722569134
    },
    {
      "available": true,
      "display": "of its cost",
      "key": "rca.hypothesis.WC1.3.shareOf"
    },
    {
      "available": true,
      "display": "Output (with reasoning) is Model A's largest cost component (where its cost goes, not a comparison).",
      "key": "rca.hypothesis.WC1.3.statement"
    },
    {
      "available": true,
      "display": "Reasoning is 39% of Model E's output tokens.",
      "key": "rca.hypothesis.WS2.1.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.WS2.1.axis"
    },
    {
      "available": true,
      "display": "Output generation is 65% (64–66%) of model time per question.",
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
      "display": "its own test rules it out: reasoning is 39% of Model E's output tokens",
      "key": "rca.hypothesis.WS2.1.gradeReasons"
    },
    {
      "available": true,
      "display": "64–66%",
      "key": "rca.hypothesis.WS2.1.interval"
    },
    {
      "available": true,
      "display": "Model E's time spent generating output",
      "key": "rca.hypothesis.WS2.1.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.WS2.1.locus"
    },
    {
      "available": true,
      "display": "Model E",
      "key": "rca.hypothesis.WS2.1.models"
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
      "display": "65%",
      "key": "rca.hypothesis.WS2.1.share",
      "value": 0.6502225519287834
    },
    {
      "available": true,
      "display": "of its model time",
      "key": "rca.hypothesis.WS2.1.shareOf"
    },
    {
      "available": true,
      "display": "Most of Model E's time is output generation, driven by reasoning tokens (where its time goes, not a comparison).",
      "key": "rca.hypothesis.WS2.1.statement"
    },
    {
      "available": true,
      "display": "Reasoning is 38% of Model D's output tokens.",
      "key": "rca.hypothesis.WS2.2.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.WS2.2.axis"
    },
    {
      "available": true,
      "display": "Output generation is 66% (65–67%) of model time per question.",
      "key": "rca.hypothesis.WS2.2.for"
    },
    {
      "available": true,
      "display": "Not supported",
      "key": "rca.hypothesis.WS2.2.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: reasoning is 38% of Model D's output tokens",
      "key": "rca.hypothesis.WS2.2.gradeReasons"
    },
    {
      "available": true,
      "display": "65–67%",
      "key": "rca.hypothesis.WS2.2.interval"
    },
    {
      "available": true,
      "display": "Model D's time spent generating output",
      "key": "rca.hypothesis.WS2.2.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.WS2.2.locus"
    },
    {
      "available": true,
      "display": "Model D",
      "key": "rca.hypothesis.WS2.2.models"
    },
    {
      "available": true,
      "display": "WS2.2",
      "key": "rca.hypothesis.WS2.2.ref"
    },
    {
      "available": true,
      "display": "The reasoning share is under one half.",
      "key": "rca.hypothesis.WS2.2.refutedIf"
    },
    {
      "available": true,
      "display": "66%",
      "key": "rca.hypothesis.WS2.2.share",
      "value": 0.6596009975062345
    },
    {
      "available": true,
      "display": "of its model time",
      "key": "rca.hypothesis.WS2.2.shareOf"
    },
    {
      "available": true,
      "display": "Most of Model D's time is output generation, driven by reasoning tokens (where its time goes, not a comparison).",
      "key": "rca.hypothesis.WS2.2.statement"
    },
    {
      "available": true,
      "display": "Reasoning is 31% of Model A's output tokens.",
      "key": "rca.hypothesis.WS2.3.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.WS2.3.axis"
    },
    {
      "available": true,
      "display": "Output generation is 70% (69–71%) of model time per question.",
      "key": "rca.hypothesis.WS2.3.for"
    },
    {
      "available": true,
      "display": "Not supported",
      "key": "rca.hypothesis.WS2.3.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: reasoning is 31% of Model A's output tokens",
      "key": "rca.hypothesis.WS2.3.gradeReasons"
    },
    {
      "available": true,
      "display": "69–71%",
      "key": "rca.hypothesis.WS2.3.interval"
    },
    {
      "available": true,
      "display": "Model A's time spent generating output",
      "key": "rca.hypothesis.WS2.3.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.WS2.3.locus"
    },
    {
      "available": true,
      "display": "Model A",
      "key": "rca.hypothesis.WS2.3.models"
    },
    {
      "available": true,
      "display": "WS2.3",
      "key": "rca.hypothesis.WS2.3.ref"
    },
    {
      "available": true,
      "display": "The reasoning share is under one half.",
      "key": "rca.hypothesis.WS2.3.refutedIf"
    },
    {
      "available": true,
      "display": "70%",
      "key": "rca.hypothesis.WS2.3.share",
      "value": 0.7038770053475936
    },
    {
      "available": true,
      "display": "of its model time",
      "key": "rca.hypothesis.WS2.3.shareOf"
    },
    {
      "available": true,
      "display": "Most of Model A's time is output generation, driven by reasoning tokens (where its time goes, not a comparison).",
      "key": "rca.hypothesis.WS2.3.statement"
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
      "display": "Across Model A's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95% interval 0.23 to 0.99, n 6).",
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
      "display": "Model A ran only once per suite",
      "key": "rca.hypothesis.WS3.1.gradeReasons"
    },
    {
      "available": true,
      "display": "Model A's time spent on tool rounds",
      "key": "rca.hypothesis.WS3.1.label"
    },
    {
      "available": true,
      "display": "model / chat",
      "key": "rca.hypothesis.WS3.1.locus"
    },
    {
      "available": true,
      "display": "Model A",
      "key": "rca.hypothesis.WS3.1.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.WS3.1.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "WS3.1",
      "key": "rca.hypothesis.WS3.1.ref"
    },
    {
      "available": true,
      "display": "The ρ interval includes zero.",
      "key": "rca.hypothesis.WS3.1.refutedIf"
    },
    {
      "available": true,
      "display": "Tool rounds multiply Model A's time (where its time goes, not a comparison).",
      "key": "rca.hypothesis.WS3.1.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.WS3.2.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.WS3.2.axis"
    },
    {
      "available": true,
      "display": "Across Model D's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95% interval 0.23 to 0.99, n 6).",
      "key": "rca.hypothesis.WS3.2.for"
    },
    {
      "available": true,
      "display": "Moderate",
      "key": "rca.hypothesis.WS3.2.grade",
      "value": "Moderate"
    },
    {
      "available": true,
      "display": "Model D ran only once per suite",
      "key": "rca.hypothesis.WS3.2.gradeReasons"
    },
    {
      "available": true,
      "display": "Model D's time spent on tool rounds",
      "key": "rca.hypothesis.WS3.2.label"
    },
    {
      "available": true,
      "display": "model / chat",
      "key": "rca.hypothesis.WS3.2.locus"
    },
    {
      "available": true,
      "display": "Model D",
      "key": "rca.hypothesis.WS3.2.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.WS3.2.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "WS3.2",
      "key": "rca.hypothesis.WS3.2.ref"
    },
    {
      "available": true,
      "display": "The ρ interval includes zero.",
      "key": "rca.hypothesis.WS3.2.refutedIf"
    },
    {
      "available": true,
      "display": "Tool rounds multiply Model D's time (where its time goes, not a comparison).",
      "key": "rca.hypothesis.WS3.2.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.WS3.3.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.WS3.3.axis"
    },
    {
      "available": true,
      "display": "Across Model E's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95% interval 0.23 to 0.99, n 6).",
      "key": "rca.hypothesis.WS3.3.for"
    },
    {
      "available": true,
      "display": "Moderate",
      "key": "rca.hypothesis.WS3.3.grade",
      "value": "Moderate"
    },
    {
      "available": true,
      "display": "Model E ran only once per suite",
      "key": "rca.hypothesis.WS3.3.gradeReasons"
    },
    {
      "available": true,
      "display": "Model E's time spent on tool rounds",
      "key": "rca.hypothesis.WS3.3.label"
    },
    {
      "available": true,
      "display": "model / chat",
      "key": "rca.hypothesis.WS3.3.locus"
    },
    {
      "available": true,
      "display": "Model E",
      "key": "rca.hypothesis.WS3.3.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.WS3.3.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "WS3.3",
      "key": "rca.hypothesis.WS3.3.ref"
    },
    {
      "available": true,
      "display": "The ρ interval includes zero.",
      "key": "rca.hypothesis.WS3.3.refutedIf"
    },
    {
      "available": true,
      "display": "Tool rounds multiply Model E's time (where its time goes, not a comparison).",
      "key": "rca.hypothesis.WS3.3.statement"
    },
    {
      "available": true,
      "display": "Uncached input, price per token accounts for 45% (44–46%) of the per-question cost difference between Model A and Model B.",
      "key": "rca.known.1"
    },
    {
      "available": true,
      "display": "Model D answered 2 of 6 scored questions without a tool call.",
      "key": "rca.known.10"
    },
    {
      "available": true,
      "display": "Model E answered 2 of 6 scored questions without a tool call.",
      "key": "rca.known.11"
    },
    {
      "available": true,
      "display": "Uncached input, price per token accounts for 44% (43–45%) of the per-question cost difference between Model A and Model C.",
      "key": "rca.known.2"
    },
    {
      "available": true,
      "display": "Uncached input, price per token accounts for 43% (42–44%) of the per-question cost difference between Model A and Model D.",
      "key": "rca.known.3"
    },
    {
      "available": true,
      "display": "Uncached input, price per token accounts for 42% (42–43%) of the per-question cost difference between Model A and Model E.",
      "key": "rca.known.4"
    },
    {
      "available": true,
      "display": "Uncached input, price per token accounts for 39% (38–39%) of the per-question cost difference between Model D and Model E.",
      "key": "rca.known.5"
    },
    {
      "available": true,
      "display": "Model A answered 2 of 6 scored questions without a tool call.",
      "key": "rca.known.6"
    },
    {
      "available": true,
      "display": "Model B answered 2 of 6 scored questions without a tool call.",
      "key": "rca.known.7"
    },
    {
      "available": true,
      "display": "Model C answered 2 of 6 scored questions without a tool call.",
      "key": "rca.known.8"
    },
    {
      "available": true,
      "display": "Model C has 1 critical error in 6 scored answers.",
      "key": "rca.known.9"
    },
    {
      "available": true,
      "display": "Priced from the catalog as of 2026-09-20. Comparable across dates; not what was actually spent.",
      "key": "rca.pricingBasis"
    },
    {
      "available": true,
      "display": "none",
      "key": "rca.profile.A.flags",
      "value": 0
    },
    {
      "available": true,
      "display": "answers above the other models' median by 10 or more: +15.5 index points on 5 questions (Q1, Q2, Q4 and 2 more)",
      "key": "rca.profile.A.gainedPointsOn"
    },
    {
      "available": true,
      "display": "answers without tools: 9.8 index points on 1 question (Q3)",
      "key": "rca.profile.A.lostPointsOn"
    },
    {
      "available": true,
      "display": "tied for first of 5 models on Accuracy",
      "key": "rca.profile.A.strength.1"
    },
    {
      "available": true,
      "display": "tied for first of 5 models on Completeness",
      "key": "rca.profile.A.strength.2"
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.profile.A.weakestDimension",
      "unavailableReason": "No dimension is below the model's Accuracy."
    },
    {
      "available": true,
      "display": "none",
      "key": "rca.profile.B.flags",
      "value": 0
    },
    {
      "available": true,
      "display": "answers above the other models' median by 10 or more: +13.1 index points on 4 questions (Q1, Q2, Q3 and 1 more)",
      "key": "rca.profile.B.gainedPointsOn"
    },
    {
      "available": true,
      "display": "low-scoring answers, mostly Accuracy: 2.8 index points on 2 questions (Q3 and Q4)",
      "key": "rca.profile.B.lostPointsOn"
    },
    {
      "available": true,
      "display": "tied for first of 5 models on Accuracy",
      "key": "rca.profile.B.strength.1"
    },
    {
      "available": true,
      "display": "tied for first of 5 models on Completeness",
      "key": "rca.profile.B.strength.2"
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.profile.B.weakestDimension",
      "unavailableReason": "No dimension is below the model's Accuracy."
    },
    {
      "available": true,
      "display": "1 critical error (Q3)",
      "key": "rca.profile.C.flag.1"
    },
    {
      "available": true,
      "display": "1 critical error (Q3)",
      "key": "rca.profile.C.flags",
      "value": 1
    },
    {
      "available": true,
      "display": "no answer 10 or more above the other models' median",
      "key": "rca.profile.C.gainedPointsOn"
    },
    {
      "available": true,
      "display": "confirmed critical errors: 7.2 index points on 1 question (Q3)",
      "key": "rca.profile.C.lostPointsOn"
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.profile.C.weakestDimension",
      "unavailableReason": "No dimension is below the model's Accuracy."
    },
    {
      "available": true,
      "display": "none",
      "key": "rca.profile.D.flags",
      "value": 0
    },
    {
      "available": true,
      "display": "no answer 10 or more above the other models' median",
      "key": "rca.profile.D.gainedPointsOn"
    },
    {
      "available": true,
      "display": "answers without tools: 6.3 index points on 1 question (Q3)",
      "key": "rca.profile.D.lostPointsOn"
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.profile.D.weakestDimension",
      "unavailableReason": "No dimension is below the model's Accuracy."
    },
    {
      "available": true,
      "display": "1 isolated miss (Q1)",
      "key": "rca.profile.E.flag.1"
    },
    {
      "available": true,
      "display": "1 isolated miss (Q1)",
      "key": "rca.profile.E.flags",
      "value": 1
    },
    {
      "available": true,
      "display": "no answer 10 or more above the other models' median",
      "key": "rca.profile.E.gainedPointsOn"
    },
    {
      "available": true,
      "display": "low-scoring answers, mostly Accuracy: 5.6 index points on 3 questions (Q1, Q3 and Q4)",
      "key": "rca.profile.E.lostPointsOn"
    },
    {
      "available": true,
      "display": "last of 5 models on Accuracy",
      "key": "rca.profile.E.lowestRank"
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.profile.E.weakestDimension",
      "unavailableReason": "No dimension is below the model's Accuracy."
    },
    {
      "available": true,
      "display": "0.0 points",
      "key": "rca.quality.A.B.dimension.accuracy.points",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 points",
      "key": "rca.quality.A.B.dimension.completeness.points",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 points",
      "key": "rca.quality.A.B.dimension.conciseness.points",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 points",
      "key": "rca.quality.A.B.dimension.readability.points",
      "value": 0
    },
    {
      "available": true,
      "display": "+5.0 points",
      "key": "rca.quality.A.B.other.points",
      "value": 5
    },
    {
      "available": true,
      "display": "+5.0 index points",
      "key": "rca.quality.A.B.total",
      "value": 5
    },
    {
      "available": true,
      "display": "+8.3 points",
      "key": "rca.quality.A.C.dimension.accuracy.points",
      "value": 8.341666666666661
    },
    {
      "available": true,
      "display": "+3.8 points",
      "key": "rca.quality.A.C.dimension.completeness.points",
      "value": 3.791666666666664
    },
    {
      "available": true,
      "display": "+1.5 points",
      "key": "rca.quality.A.C.dimension.conciseness.points",
      "value": 1.5166666666666657
    },
    {
      "available": true,
      "display": "+1.5 points",
      "key": "rca.quality.A.C.dimension.readability.points",
      "value": 1.5166666666666657
    },
    {
      "available": true,
      "display": "-0.2 points",
      "key": "rca.quality.A.C.other.points",
      "value": -0.1666666666666572
    },
    {
      "available": true,
      "display": "+15.0 index points",
      "key": "rca.quality.A.C.total",
      "value": 15
    },
    {
      "available": true,
      "display": "+8.7 points",
      "key": "rca.quality.A.D.dimension.accuracy.points",
      "value": 8.70833333333332
    },
    {
      "available": true,
      "display": "+4.0 points",
      "key": "rca.quality.A.D.dimension.completeness.points",
      "value": 3.958333333333327
    },
    {
      "available": true,
      "display": "+1.6 points",
      "key": "rca.quality.A.D.dimension.conciseness.points",
      "value": 1.5833333333333308
    },
    {
      "available": true,
      "display": "+1.6 points",
      "key": "rca.quality.A.D.dimension.readability.points",
      "value": 1.5833333333333308
    },
    {
      "available": true,
      "display": "-0.8 points",
      "key": "rca.quality.A.D.other.points",
      "value": -0.8333333333333073
    },
    {
      "available": true,
      "display": "+15.0 index points",
      "key": "rca.quality.A.D.total",
      "value": 15
    },
    {
      "available": true,
      "display": "+16.5 points",
      "key": "rca.quality.A.E.dimension.accuracy.points",
      "value": 16.50000000000001
    },
    {
      "available": true,
      "display": "+7.5 points",
      "key": "rca.quality.A.E.dimension.completeness.points",
      "value": 7.500000000000004
    },
    {
      "available": true,
      "display": "+3.0 points",
      "key": "rca.quality.A.E.dimension.conciseness.points",
      "value": 3.0000000000000018
    },
    {
      "available": true,
      "display": "+3.0 points",
      "key": "rca.quality.A.E.dimension.readability.points",
      "value": 3.0000000000000018
    },
    {
      "available": true,
      "display": "0.0 points",
      "key": "rca.quality.A.E.other.points",
      "value": -1.4210854715202004E-14
    },
    {
      "available": true,
      "display": "+30.0 index points",
      "key": "rca.quality.A.E.total",
      "value": 30
    },
    {
      "available": true,
      "display": "+7.8 points",
      "key": "rca.quality.D.E.dimension.accuracy.points",
      "value": 7.7916666666666865
    },
    {
      "available": true,
      "display": "+3.5 points",
      "key": "rca.quality.D.E.dimension.completeness.points",
      "value": 3.5416666666666754
    },
    {
      "available": true,
      "display": "+1.4 points",
      "key": "rca.quality.D.E.dimension.conciseness.points",
      "value": 1.4166666666666703
    },
    {
      "available": true,
      "display": "+1.4 points",
      "key": "rca.quality.D.E.dimension.readability.points",
      "value": 1.4166666666666703
    },
    {
      "available": true,
      "display": "+0.8 points",
      "key": "rca.quality.D.E.other.points",
      "value": 0.8333333333332984
    },
    {
      "available": true,
      "display": "+15.0 index points",
      "key": "rca.quality.D.E.total",
      "value": 15
    },
    {
      "available": true,
      "display": "none fired",
      "key": "rca.question.1.hypotheses",
      "value": 0
    },
    {
      "available": true,
      "display": "Model A",
      "key": "rca.question.1.model",
      "value": "A"
    },
    {
      "available": true,
      "display": "Model B",
      "key": "rca.question.1.neighbor",
      "value": "B"
    },
    {
      "available": true,
      "display": "not established",
      "key": "rca.question.1.orderEstablished",
      "value": false
    },
    {
      "available": true,
      "display": "answers above the other models' median by 10 or more: +15.5 index points on 5 questions (Q1, Q2, Q4 and 2 more); tied for first of 5 models on Accuracy; tied for first of 5 models on Completeness",
      "key": "rca.question.1.signals"
    },
    {
      "available": true,
      "display": "Unknown",
      "key": "rca.question.1.status",
      "value": "Unknown"
    },
    {
      "available": true,
      "display": "Why did Model A score highest, though its lead over Model B is not established?",
      "key": "rca.question.1.text"
    },
    {
      "available": true,
      "display": "not answered by the model's own breakdown alone",
      "key": "rca.question.1.withinOnly",
      "value": false
    },
    {
      "available": true,
      "display": "none fired",
      "key": "rca.question.2.hypotheses",
      "value": 0
    },
    {
      "available": true,
      "display": "Model E",
      "key": "rca.question.2.model",
      "value": "E"
    },
    {
      "available": true,
      "display": "Model D",
      "key": "rca.question.2.neighbor",
      "value": "D"
    },
    {
      "available": true,
      "display": "established",
      "key": "rca.question.2.orderEstablished",
      "value": true
    },
    {
      "available": true,
      "display": "lost points on low-scoring answers, mostly Accuracy: 5.6 index points on 3 questions (Q1, Q3 and Q4); 1 isolated miss (Q1)",
      "key": "rca.question.2.signals"
    },
    {
      "available": true,
      "display": "Unknown",
      "key": "rca.question.2.status",
      "value": "Unknown"
    },
    {
      "available": true,
      "display": "Why did Model E score lowest?",
      "key": "rca.question.2.text"
    },
    {
      "available": true,
      "display": "not answered by the model's own breakdown alone",
      "key": "rca.question.2.withinOnly",
      "value": false
    },
    {
      "available": true,
      "display": "WS3.3 and WS2.1",
      "key": "rca.question.3.hypotheses",
      "value": 2
    },
    {
      "available": true,
      "display": "Model E",
      "key": "rca.question.3.model",
      "value": "E"
    },
    {
      "available": true,
      "display": "median model time 20.0 s; output generation 59% of its time per question; 2.5 model calls per answer",
      "key": "rca.question.3.signals"
    },
    {
      "available": true,
      "display": "Suggestive (Low)",
      "key": "rca.question.3.status",
      "value": "Low"
    },
    {
      "available": true,
      "display": "Why is Model E slowest?",
      "key": "rca.question.3.text"
    },
    {
      "available": true,
      "display": "only the model's own breakdown answers it",
      "key": "rca.question.3.withinOnly",
      "value": true
    },
    {
      "available": true,
      "display": "HC4.2, HC2.2 and WC1.2",
      "key": "rca.question.4.hypotheses",
      "value": 3
    },
    {
      "available": true,
      "display": "Model D",
      "key": "rca.question.4.model",
      "value": "D"
    },
    {
      "available": true,
      "display": "$0.060 per question; uncached input 44% of its cost per question; 27% of input tokens read from the cache",
      "key": "rca.question.4.signals"
    },
    {
      "available": true,
      "display": "Known from the price cards",
      "key": "rca.question.4.status",
      "value": "High"
    },
    {
      "available": true,
      "display": "Why is Model D the most expensive?",
      "key": "rca.question.4.text"
    },
    {
      "available": true,
      "display": "not answered by the model's own breakdown alone",
      "key": "rca.question.4.withinOnly",
      "value": false
    },
    {
      "available": true,
      "display": "5 pairs",
      "key": "rca.rule.HC2.count",
      "value": 5
    },
    {
      "available": true,
      "display": "65% to 72% of each gap",
      "key": "rca.rule.HC2.shareSpan",
      "value": 0.07007310416732004
    },
    {
      "available": true,
      "display": "4 pairs",
      "key": "rca.rule.HC4.count",
      "value": 4
    },
    {
      "available": true,
      "display": "8% to 12% of each gap",
      "key": "rca.rule.HC4.shareSpan",
      "value": 0.04898321238024558
    },
    {
      "available": true,
      "display": "3 models",
      "key": "rca.rule.WC1.count",
      "value": 3
    },
    {
      "available": true,
      "display": "54% to 69% of each model's own cost",
      "key": "rca.rule.WC1.shareSpan",
      "value": 0.15264291014893683
    },
    {
      "available": true,
      "display": "3 models",
      "key": "rca.rule.WS2.count",
      "value": 3
    },
    {
      "available": true,
      "display": "65% to 70% of each model's own time",
      "key": "rca.rule.WS2.shareSpan",
      "value": 0.053654453418810144
    },
    {
      "available": true,
      "display": "M, model selection",
      "key": "rca.run.1.purpose"
    },
    {
      "available": true,
      "display": "Run 1, a replicate of all five models",
      "key": "rca.run.1.summary"
    },
    {
      "available": true,
      "display": "not split into parts: the difference may be chance",
      "key": "rca.speed.A.B.decomposed",
      "value": false
    },
    {
      "available": true,
      "display": "-1.6 s per question",
      "key": "rca.speed.A.B.total",
      "value": -1.55
    },
    {
      "available": true,
      "display": "not split into parts: the difference may be chance",
      "key": "rca.speed.A.C.decomposed",
      "value": false
    },
    {
      "available": true,
      "display": "-3.0 s per question",
      "key": "rca.speed.A.C.total",
      "value": -3
    },
    {
      "available": true,
      "display": "not split into parts: the difference may be chance",
      "key": "rca.speed.A.D.decomposed",
      "value": false
    },
    {
      "available": true,
      "display": "-4.6 s per question",
      "key": "rca.speed.A.D.total",
      "value": -4.55
    },
    {
      "available": true,
      "display": "not split into parts: the difference may be chance",
      "key": "rca.speed.A.E.decomposed",
      "value": false
    },
    {
      "available": true,
      "display": "-6.0 s per question",
      "key": "rca.speed.A.E.total",
      "value": -6
    },
    {
      "available": true,
      "display": "not split into parts: the difference may be chance",
      "key": "rca.speed.D.E.decomposed",
      "value": false
    },
    {
      "available": true,
      "display": "-1.5 s per question",
      "key": "rca.speed.D.E.total",
      "value": -1.45
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
      "key": "rca.within.A.cost.cacheRead.share",
      "value": 0.008920606601248885
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.A.cost.cacheWrite.share",
      "value": 0
    },
    {
      "available": true,
      "display": "48%",
      "key": "rca.within.A.cost.output.share",
      "value": 0.47814451382694023
    },
    {
      "available": true,
      "display": "21%",
      "key": "rca.within.A.cost.reasoning.share",
      "value": 0.21409455842997321
    },
    {
      "available": true,
      "display": "a mean of $0.014 per question",
      "key": "rca.within.A.cost.total",
      "value": 0.0140125
    },
    {
      "available": true,
      "display": "30%",
      "key": "rca.within.A.cost.uncachedInput.share",
      "value": 0.2988403211418377
    },
    {
      "available": true,
      "display": "a mean of 0.3 s per question",
      "key": "rca.within.A.time.other.seconds",
      "value": 0.3
    },
    {
      "available": true,
      "display": "3%",
      "key": "rca.within.A.time.other.share",
      "value": 0.03340757238307349
    },
    {
      "available": true,
      "display": "a mean of 5.3 s per question",
      "key": "rca.within.A.time.outputGeneration.seconds",
      "value": 5.265
    },
    {
      "available": true,
      "display": "59%",
      "key": "rca.within.A.time.outputGeneration.share",
      "value": 0.5863028953229398
    },
    {
      "available": true,
      "display": "a mean of 0.0 s per question",
      "key": "rca.within.A.time.ownWaits.seconds",
      "value": 0
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.A.time.ownWaits.share",
      "value": 0
    },
    {
      "available": true,
      "display": "a mean of 1.5 s per question",
      "key": "rca.within.A.time.toolExecution.seconds",
      "value": 1.5
    },
    {
      "available": true,
      "display": "17%",
      "key": "rca.within.A.time.toolExecution.share",
      "value": 0.16703786191536749
    },
    {
      "available": true,
      "display": "a mean of 9.0 s per question",
      "key": "rca.within.A.time.total",
      "value": 8.98
    },
    {
      "available": true,
      "display": "a mean of 1.9 s per question",
      "key": "rca.within.A.time.ttft.seconds",
      "value": 1.915
    },
    {
      "available": true,
      "display": "21%",
      "key": "rca.within.A.time.ttft.share",
      "value": 0.21325167037861914
    },
    {
      "available": true,
      "display": "1%",
      "key": "rca.within.B.cost.cacheRead.share",
      "value": 0.006879944960440315
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.B.cost.cacheWrite.share",
      "value": 0
    },
    {
      "available": true,
      "display": "41%",
      "key": "rca.within.B.cost.output.share",
      "value": 0.40729274165806667
    },
    {
      "available": true,
      "display": "21%",
      "key": "rca.within.B.cost.reasoning.share",
      "value": 0.20915032679738563
    },
    {
      "available": true,
      "display": "a mean of $0.022 per question",
      "key": "rca.within.B.cost.total",
      "value": 0.021802500000000002
    },
    {
      "available": true,
      "display": "38%",
      "key": "rca.within.B.cost.uncachedInput.share",
      "value": 0.37667698658410725
    },
    {
      "available": true,
      "display": "a mean of 0.3 s per question",
      "key": "rca.within.B.time.other.seconds",
      "value": 0.3
    },
    {
      "available": true,
      "display": "3%",
      "key": "rca.within.B.time.other.share",
      "value": 0.028490028490028487
    },
    {
      "available": true,
      "display": "a mean of 6.2 s per question",
      "key": "rca.within.B.time.outputGeneration.seconds",
      "value": 6.185
    },
    {
      "available": true,
      "display": "59%",
      "key": "rca.within.B.time.outputGeneration.share",
      "value": 0.5873694207027539
    },
    {
      "available": true,
      "display": "a mean of 0.0 s per question",
      "key": "rca.within.B.time.ownWaits.seconds",
      "value": 0
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.B.time.ownWaits.share",
      "value": 0
    },
    {
      "available": true,
      "display": "a mean of 1.5 s per question",
      "key": "rca.within.B.time.toolExecution.seconds",
      "value": 1.5
    },
    {
      "available": true,
      "display": "14%",
      "key": "rca.within.B.time.toolExecution.share",
      "value": 0.14245014245014243
    },
    {
      "available": true,
      "display": "a mean of 10.5 s per question",
      "key": "rca.within.B.time.total",
      "value": 10.530000000000001
    },
    {
      "available": true,
      "display": "a mean of 2.5 s per question",
      "key": "rca.within.B.time.ttft.seconds",
      "value": 2.545
    },
    {
      "available": true,
      "display": "24%",
      "key": "rca.within.B.time.ttft.share",
      "value": 0.241690408357075
    },
    {
      "available": true,
      "display": "1%",
      "key": "rca.within.C.cost.cacheRead.share",
      "value": 0.0056832020784281885
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.C.cost.cacheWrite.share",
      "value": 0
    },
    {
      "available": true,
      "display": "37%",
      "key": "rca.within.C.cost.output.share",
      "value": 0.36827149468214665
    },
    {
      "available": true,
      "display": "21%",
      "key": "rca.within.C.cost.reasoning.share",
      "value": 0.20914183648615736
    },
    {
      "available": true,
      "display": "a mean of $0.031 per question",
      "key": "rca.within.C.cost.total",
      "value": 0.0307925
    },
    {
      "available": true,
      "display": "42%",
      "key": "rca.within.C.cost.uncachedInput.share",
      "value": 0.41690346675326784
    },
    {
      "available": true,
      "display": "a mean of 0.3 s per question",
      "key": "rca.within.C.time.other.seconds",
      "value": 0.3
    },
    {
      "available": true,
      "display": "3%",
      "key": "rca.within.C.time.other.share",
      "value": 0.025041736227045072
    },
    {
      "available": true,
      "display": "a mean of 7.0 s per question",
      "key": "rca.within.C.time.outputGeneration.seconds",
      "value": 7.015
    },
    {
      "available": true,
      "display": "59%",
      "key": "rca.within.C.time.outputGeneration.share",
      "value": 0.585559265442404
    },
    {
      "available": true,
      "display": "a mean of 0.0 s per question",
      "key": "rca.within.C.time.ownWaits.seconds",
      "value": 0
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.C.time.ownWaits.share",
      "value": 0
    },
    {
      "available": true,
      "display": "a mean of 1.5 s per question",
      "key": "rca.within.C.time.toolExecution.seconds",
      "value": 1.5
    },
    {
      "available": true,
      "display": "13%",
      "key": "rca.within.C.time.toolExecution.share",
      "value": 0.12520868113522537
    },
    {
      "available": true,
      "display": "a mean of 12.0 s per question",
      "key": "rca.within.C.time.total",
      "value": 11.98
    },
    {
      "available": true,
      "display": "a mean of 3.2 s per question",
      "key": "rca.within.C.time.ttft.seconds",
      "value": 3.165
    },
    {
      "available": true,
      "display": "26%",
      "key": "rca.within.C.time.ttft.share",
      "value": 0.26419031719532554
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.D.cost.cacheRead.share",
      "value": 0.004880131763557617
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.D.cost.cacheWrite.share",
      "value": 0
    },
    {
      "available": true,
      "display": "34%",
      "key": "rca.within.D.cost.output.share",
      "value": 0.34356127615445625
    },
    {
      "available": true,
      "display": "21%",
      "key": "rca.within.D.cost.reasoning.share",
      "value": 0.21082169218568902
    },
    {
      "available": true,
      "display": "a mean of $0.041 per question",
      "key": "rca.within.D.cost.total",
      "value": 0.0409825
    },
    {
      "available": true,
      "display": "44%",
      "key": "rca.within.D.cost.uncachedInput.share",
      "value": 0.44073689989629716
    },
    {
      "available": true,
      "display": "a mean of 0.3 s per question",
      "key": "rca.within.D.time.other.seconds",
      "value": 0.3
    },
    {
      "available": true,
      "display": "2%",
      "key": "rca.within.D.time.other.share",
      "value": 0.022172949002217293
    },
    {
      "available": true,
      "display": "a mean of 7.9 s per question",
      "key": "rca.within.D.time.outputGeneration.seconds",
      "value": 7.935
    },
    {
      "available": true,
      "display": "59%",
      "key": "rca.within.D.time.outputGeneration.share",
      "value": 0.5864745011086474
    },
    {
      "available": true,
      "display": "a mean of 0.0 s per question",
      "key": "rca.within.D.time.ownWaits.seconds",
      "value": 0
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.D.time.ownWaits.share",
      "value": 0
    },
    {
      "available": true,
      "display": "a mean of 1.5 s per question",
      "key": "rca.within.D.time.toolExecution.seconds",
      "value": 1.5
    },
    {
      "available": true,
      "display": "11%",
      "key": "rca.within.D.time.toolExecution.share",
      "value": 0.11086474501108647
    },
    {
      "available": true,
      "display": "a mean of 13.5 s per question",
      "key": "rca.within.D.time.total",
      "value": 13.530000000000001
    },
    {
      "available": true,
      "display": "a mean of 3.8 s per question",
      "key": "rca.within.D.time.ttft.seconds",
      "value": 3.795
    },
    {
      "available": true,
      "display": "28%",
      "key": "rca.within.D.time.ttft.share",
      "value": 0.28048780487804875
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.E.cost.cacheRead.share",
      "value": 0.004296147787483889
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.E.cost.cacheWrite.share",
      "value": 0
    },
    {
      "available": true,
      "display": "33%",
      "key": "rca.within.E.cost.output.share",
      "value": 0.3265072318487756
    },
    {
      "available": true,
      "display": "21%",
      "key": "rca.within.E.cost.reasoning.share",
      "value": 0.2130889302592009
    },
    {
      "available": true,
      "display": "a mean of $0.052 per question",
      "key": "rca.within.E.cost.total",
      "value": 0.052372499999999995
    },
    {
      "available": true,
      "display": "46%",
      "key": "rca.within.E.cost.uncachedInput.share",
      "value": 0.45610769010453966
    },
    {
      "available": true,
      "display": "a mean of 0.3 s per question",
      "key": "rca.within.E.time.other.seconds",
      "value": 0.3
    },
    {
      "available": true,
      "display": "2%",
      "key": "rca.within.E.time.other.share",
      "value": 0.02002670226969292
    },
    {
      "available": true,
      "display": "a mean of 8.8 s per question",
      "key": "rca.within.E.time.outputGeneration.seconds",
      "value": 8.765
    },
    {
      "available": true,
      "display": "59%",
      "key": "rca.within.E.time.outputGeneration.share",
      "value": 0.585113484646195
    },
    {
      "available": true,
      "display": "a mean of 0.0 s per question",
      "key": "rca.within.E.time.ownWaits.seconds",
      "value": 0
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.E.time.ownWaits.share",
      "value": 0
    },
    {
      "available": true,
      "display": "a mean of 1.5 s per question",
      "key": "rca.within.E.time.toolExecution.seconds",
      "value": 1.5
    },
    {
      "available": true,
      "display": "10%",
      "key": "rca.within.E.time.toolExecution.share",
      "value": 0.10013351134846461
    },
    {
      "available": true,
      "display": "a mean of 15.0 s per question",
      "key": "rca.within.E.time.total",
      "value": 14.98
    },
    {
      "available": true,
      "display": "a mean of 4.4 s per question",
      "key": "rca.within.E.time.ttft.seconds",
      "value": 4.415
    },
    {
      "available": true,
      "display": "29%",
      "key": "rca.within.E.time.ttft.share",
      "value": 0.2947263017356475
    },
    {
      "available": true,
      "display": "from Model E at $0.010 to Model D at $0.060",
      "key": "spread.cost",
      "value": 0.049999999999999996
    },
    {
      "available": true,
      "display": "from Model E at 50 to Model A at 80, 30 points apart",
      "key": "spread.dimension.accuracy",
      "value": 30
    },
    {
      "available": true,
      "display": "from Model E at 50 to Model A at 80, 30 points apart",
      "key": "spread.dimension.completeness",
      "value": 30
    },
    {
      "available": true,
      "display": "from Model E at 50 to Model A at 80, 30 points apart",
      "key": "spread.dimension.conciseness",
      "value": 30
    },
    {
      "available": true,
      "display": "from Model E at 50 to Model A at 80, 30 points apart",
      "key": "spread.dimension.readability",
      "value": 30
    },
    {
      "available": true,
      "display": "from Model E at 55 to Model A at 85, 30 points apart",
      "key": "spread.quality",
      "value": 30
    },
    {
      "available": true,
      "display": "from Model D at 8.0 s to Model E at 20.0 s",
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
    "Eastgate",
    "Gemini 3.8 Flash",
    "GnollHack Core Suite",
    "Google",
    "Lyra Mini",
    "Midland",
    "Northwind",
    "Nova Lite",
    "Orion Max",
    "Southstar",
    "Vega Pro",
    "Westlake",
    "Zeta Prime",
    "gemini-3.8-flash",
    "lyra-mini",
    "nova-lite",
    "orion-max",
    "vega-pro",
    "zeta-prime"
  ],
  "models": [
    {
      "entryKey": "run:31",
      "label": "Orion Max",
      "letter": "A",
      "provider": "Northwind"
    },
    {
      "entryKey": "run:32",
      "label": "Vega Pro",
      "letter": "B",
      "provider": "Southstar"
    },
    {
      "entryKey": "run:33",
      "label": "Lyra Mini",
      "letter": "C",
      "provider": "Eastgate"
    },
    {
      "entryKey": "run:34",
      "label": "Nova Lite",
      "letter": "D",
      "provider": "Westlake"
    },
    {
      "entryKey": "run:35",
      "label": "Zeta Prime",
      "letter": "E",
      "provider": "Midland"
    }
  ],
  "noSignificanceInstead": "",
  "noSignificanceSummary": "",
  "notableFindings": [
    {
      "facts": [
        "pair.B.D.intervalOverlap",
        "model.B.quality.index",
        "model.D.quality.index",
        "model.B.cost.perQuestion",
        "model.D.cost.perQuestion"
      ],
      "kind": "cheaperAndBetter",
      "models": [
        "B",
        "D"
      ],
      "number": 1,
      "questions": [],
      "summary": "Model B scored higher than Model D at 0.50 times its cost per question, established by 95% intervals that do not overlap."
    },
    {
      "facts": [
        "pair.D.E.intervalOverlap",
        "model.D.quality.index",
        "model.E.quality.index",
        "model.D.speed.modelTimeP50",
        "model.E.speed.modelTimeP50"
      ],
      "kind": "fasterAndBetter",
      "models": [
        "D",
        "E"
      ],
      "number": 2,
      "questions": [],
      "summary": "Model D scored higher than Model E at 0.40 times its median answer time, established by 95% intervals that do not overlap."
    },
    {
      "facts": [
        "pair.B.E.intervalOverlap",
        "model.B.quality.index",
        "model.E.quality.index",
        "model.B.speed.modelTimeP50",
        "model.E.speed.modelTimeP50"
      ],
      "kind": "fasterAndBetter",
      "models": [
        "B",
        "E"
      ],
      "number": 3,
      "questions": [],
      "summary": "Model B scored higher than Model E at 0.45 times its median answer time, established by 95% intervals that do not overlap."
    }
  ],
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
          "adjustment": "Holm",
          "adjustmentNote": "Holm-adjusted across 4 tests",
          "familySize": 4,
          "measure": "Speed",
          "pairs": [
            {
              "adjustedPValue": 0.4375,
              "effect": 0.8330303825065561,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 0.529843904547703,
              "pValue": 0.4375,
              "pairedItems": 6,
              "secondLetter": "B",
              "upper": 1.3097057684779734
            },
            {
              "adjustedPValue": 0.125,
              "effect": 0.714024714731622,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 0.7003765774672244,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "C",
              "upper": 0.7279388112767561
            },
            {
              "adjustedPValue": 0.125,
              "effect": 0.6246956089867761,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 0.398251593445093,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "D",
              "upper": 0.9798946452706714
            },
            {
              "adjustedPValue": 0.125,
              "effect": 0.5553073274569991,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 0.5388951164675453,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "E",
              "upper": 0.5722193772116063
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
          "adjustment": "Holm",
          "adjustmentNote": "Holm-adjusted across 10 tests",
          "familySize": 10,
          "measure": "Speed",
          "pairs": [
            {
              "adjustedPValue": 1,
              "effect": 0.8330303825065561,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 0.529843904547703,
              "pValue": 0.4375,
              "pairedItems": 6,
              "secondLetter": "B",
              "upper": 1.3097057684779734
            },
            {
              "adjustedPValue": 0.3125,
              "effect": 0.714024714731622,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 0.7003765774672244,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "C",
              "upper": 0.7279388112767561
            },
            {
              "adjustedPValue": 0.3125,
              "effect": 0.6246956089867761,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 0.398251593445093,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "D",
              "upper": 0.9798946452706714
            },
            {
              "adjustedPValue": 0.3125,
              "effect": 0.5553073274569991,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "A",
              "lower": 0.5388951164675453,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "E",
              "upper": 0.5722193772116063
            },
            {
              "adjustedPValue": 1,
              "effect": 0.8571412636633363,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "B",
              "lower": 0.5413309485848339,
              "pValue": 0.4375,
              "pairedItems": 6,
              "secondLetter": "C",
              "upper": 1.3571940562331342
            },
            {
              "adjustedPValue": 0.3125,
              "effect": 0.7499073528471929,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "B",
              "lower": 0.7399675915508266,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "D",
              "upper": 0.7599806319567132
            },
            {
              "adjustedPValue": 0.46875,
              "effect": 0.6666111334212095,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "B",
              "lower": 0.419225537038671,
              "pValue": 0.09375,
              "pairedItems": 6,
              "secondLetter": "E",
              "upper": 1.0599793284065113
            },
            {
              "adjustedPValue": 1,
              "effect": 0.8748935381341502,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "C",
              "lower": 0.554106613762761,
              "pValue": 0.4375,
              "pairedItems": 6,
              "secondLetter": "D",
              "upper": 1.3813924686280894
            },
            {
              "adjustedPValue": 0.3125,
              "effect": 0.7777144348087733,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "C",
              "lower": 0.7694358530213178,
              "pValue": 0.03125,
              "pairedItems": 6,
              "secondLetter": "E",
              "upper": 0.7860820882402686
            },
            {
              "adjustedPValue": 1,
              "effect": 0.8889246530124949,
              "effectKind": "Ratio",
              "established": false,
              "favors": "None",
              "firstLetter": "D",
              "lower": 0.5607757339029062,
              "pValue": 0.4375,
              "pairedItems": 6,
              "secondLetter": "E",
              "upper": 1.4090963480780692
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
      "displayName": "Orion Max",
      "entryKey": "run:31",
      "explanation": "Comparable: measured under the baseline condition.",
      "label": "Orion Max",
      "letter": "A",
      "modelId": "orion-max",
      "provider": "Northwind",
      "runIds": [
        31
      ],
      "speedDegraded": false,
      "state": "Comparable"
    },
    {
      "costDegraded": false,
      "displayName": "Vega Pro",
      "entryKey": "run:32",
      "explanation": "Comparable: measured under the baseline condition.",
      "label": "Vega Pro",
      "letter": "B",
      "modelId": "vega-pro",
      "provider": "Southstar",
      "runIds": [
        32
      ],
      "speedDegraded": false,
      "state": "Comparable"
    },
    {
      "costDegraded": false,
      "displayName": "Lyra Mini",
      "entryKey": "run:33",
      "explanation": "Comparable: measured under the baseline condition.",
      "label": "Lyra Mini",
      "letter": "C",
      "modelId": "lyra-mini",
      "provider": "Eastgate",
      "runIds": [
        33
      ],
      "speedDegraded": false,
      "state": "Comparable"
    },
    {
      "costDegraded": false,
      "displayName": "Nova Lite",
      "entryKey": "run:34",
      "explanation": "Comparable: measured under the baseline condition.",
      "label": "Nova Lite",
      "letter": "D",
      "modelId": "nova-lite",
      "provider": "Westlake",
      "runIds": [
        34
      ],
      "speedDegraded": false,
      "state": "Comparable"
    },
    {
      "costDegraded": false,
      "displayName": "Zeta Prime",
      "entryKey": "run:35",
      "explanation": "Comparable: measured under the baseline condition.",
      "label": "Zeta Prime",
      "letter": "E",
      "modelId": "zeta-prime",
      "provider": "Midland",
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
        "C",
        "D",
        "E"
      ],
      "itemRevisionUsed": 1,
      "modelTimeMs": 10268,
      "models": [
        {
          "criticalError": false,
          "letter": "A",
          "modelTimeMs": 8220,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 92,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "B",
          "modelTimeMs": 6740,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 85,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "C",
          "modelTimeMs": 11820,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 70,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "D",
          "modelTimeMs": 9140,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 75,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "E",
          "modelTimeMs": 15420,
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
        "C",
        "D",
        "E"
      ],
      "itemRevisionUsed": 1,
      "modelTimeMs": 9732,
      "models": [
        {
          "criticalError": false,
          "letter": "A",
          "modelTimeMs": 5740,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 88,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "B",
          "modelTimeMs": 10320,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 80,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "C",
          "modelTimeMs": 8140,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 72,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "D",
          "modelTimeMs": 13920,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 60,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "E",
          "modelTimeMs": 10540,
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
        "C",
        "E"
      ],
      "itemRevisionUsed": 1,
      "modelTimeMs": 10788,
      "models": [
        {
          "criticalError": false,
          "letter": "A",
          "modelTimeMs": 8820,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 30,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "B",
          "modelTimeMs": 7140,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 70,
          "toolCalls": 0
        },
        {
          "criticalError": true,
          "letter": "C",
          "modelTimeMs": 12420,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 28,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "D",
          "modelTimeMs": 9540,
          "refutedAnswerSentences": 0,
          "refutedClaims": 1,
          "runCount": 1,
          "score": 32,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "E",
          "modelTimeMs": 16020,
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
        "C",
        "D",
        "E"
      ],
      "itemRevisionUsed": 1,
      "modelTimeMs": 10212,
      "models": [
        {
          "criticalError": false,
          "letter": "A",
          "modelTimeMs": 6140,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 90,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "B",
          "modelTimeMs": 10920,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 75,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "C",
          "modelTimeMs": 8540,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 65,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "D",
          "modelTimeMs": 14520,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 72,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "E",
          "modelTimeMs": 10940,
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
        "C",
        "D",
        "E"
      ],
      "itemRevisionUsed": 1,
      "modelTimeMs": 11308,
      "models": [
        {
          "criticalError": false,
          "letter": "A",
          "modelTimeMs": 9420,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 85,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "B",
          "modelTimeMs": 7540,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 82,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "C",
          "modelTimeMs": 13020,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 74,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "D",
          "modelTimeMs": 9940,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 68,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "E",
          "modelTimeMs": 16620,
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
        "C",
        "D",
        "E"
      ],
      "itemRevisionUsed": 1,
      "modelTimeMs": 10692,
      "models": [
        {
          "criticalError": false,
          "letter": "A",
          "modelTimeMs": 6540,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 95,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "B",
          "modelTimeMs": 11520,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 88,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "C",
          "modelTimeMs": 8940,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 80,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "D",
          "modelTimeMs": 15120,
          "refutedAnswerSentences": 0,
          "refutedClaims": 0,
          "runCount": 1,
          "score": 78,
          "toolCalls": 0
        },
        {
          "criticalError": false,
          "letter": "E",
          "modelTimeMs": 11340,
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
  "referenceIsBaselineModel": false,
  "referenceLetter": "A",
  "rootCause": {
    "anomalies": [],
    "axes": [
      {
        "axis": "intelligence",
        "bestHypothesis": "HQ3.1",
        "reason": "Suggestive (Low) because it is observed, not measured.",
        "status": "NotEstablished"
      },
      {
        "axis": "speed",
        "reason": "Only hypotheses about one model's own breakdown fired on this axis; none compares the models.",
        "status": "NotEstablished"
      },
      {
        "axis": "cost",
        "bestHypothesis": "HC2.1",
        "reason": "No cost mechanism beyond the price cards remains: every other hypothesis on this axis was ruled out.",
        "status": "NotEstablished"
      }
    ],
    "checks": [
      {
        "hypothesisIds": [
          "HQ3.1"
        ],
        "kind": "rubric",
        "number": 1,
        "summary": "Check 1, a review of the rubric of Q3",
        "text": "Check the rubric of Q3 against the source, and whether the tools' corpus holds the facts the question needs."
      }
    ],
    "computedAtUtc": "2026-09-21T08:00:00Z",
    "controlItems": [],
    "controls": [],
    "costPairs": [
      {
        "axis": "cost",
        "components": [
          {
            "amount": -0.0005125227089027412,
            "effect": "volume",
            "key": "uncachedInput",
            "label": "uncached input, token volume",
            "share": 0.06579238881935058,
            "shareLower": 0.06424291031175927,
            "shareUpper": 0.06741876767770795
          },
          {
            "amount": -0.003512477291097258,
            "effect": "price",
            "key": "uncachedInput",
            "label": "uncached input, price per token",
            "share": 0.45089567279810744,
            "shareLower": 0.44441814595666274,
            "shareUpper": 0.45706925015627226
          },
          {
            "amount": -2.499999999999994E-05,
            "effect": "volume",
            "key": "cacheRead",
            "label": "cache reads, token volume",
            "share": 0.0032092426187419684,
            "shareLower": 0.003134141245298779,
            "shareUpper": 0.003288031565103017
          },
          {
            "amount": -4.404571325722362E-20,
            "effect": "price",
            "key": "cacheRead",
            "label": "cache reads, price per token",
            "share": 5.65413520631882E-18,
            "shareLower": 5.5218194639227244E-18,
            "shareUpper": 5.792947819889121E-18
          },
          {
            "amount": 0,
            "effect": "volume",
            "key": "cacheWrite",
            "label": "cache writes, token volume",
            "share": -0,
            "shareLower": 0,
            "shareUpper": 0
          },
          {
            "amount": 0,
            "effect": "price",
            "key": "cacheWrite",
            "label": "cache writes, price per token",
            "share": -0,
            "shareLower": 0,
            "shareUpper": 0
          },
          {
            "amount": -0.0007690329555292995,
            "effect": "volume",
            "key": "output",
            "label": "visible output, token volume",
            "share": 0.09872053344406923,
            "shareLower": 0.09640501305699987,
            "shareUpper": 0.10115018729040175
          },
          {
            "amount": -0.0014109670444706992,
            "effect": "price",
            "key": "output",
            "label": "visible output, price per token",
            "share": 0.18112542291023093,
            "shareLower": 0.17855169784769567,
            "shareUpper": 0.18357827152302506
          },
          {
            "amount": -0.0008807198272407068,
            "effect": "volume",
            "key": "reasoning",
            "label": "reasoning, token volume",
            "share": 0.11305774419007787,
            "shareLower": 0.11041201344430085,
            "shareUpper": 0.11583338367918124
          },
          {
            "amount": -0.0006792801727592943,
            "effect": "price",
            "key": "reasoning",
            "label": "reasoning, price per token",
            "share": 0.08719899521942158,
            "shareLower": 0.08515840026234356,
            "shareUpper": 0.08933978598324782
          },
          {
            "amount": -0.0021872754916727475,
            "effect": "volume",
            "key": "all",
            "label": "token volume, all components",
            "share": 0.28077990907223965,
            "shareLower": 0.27419407805835877,
            "shareUpper": 0.2876903702123939
          },
          {
            "amount": -0.005602724508327252,
            "effect": "price",
            "key": "all",
            "label": "price per token, all components",
            "share": 0.71922009092776,
            "shareLower": 0.7123096297876061,
            "shareUpper": 0.7258059219416408
          }
        ],
        "difference": -0.007790000000000002,
        "differenceLower": -0.00797666666666667,
        "differenceUpper": -0.0076033333333333335,
        "first": "A",
        "firstTotal": 0.0140125,
        "grouped": false,
        "notEstablished": false,
        "second": "B",
        "secondTotal": 0.021802500000000002,
        "sharedQuestions": 6,
        "volumeRatio": 0.8832687741305474
      },
      {
        "axis": "cost",
        "components": [
          {
            "amount": -0.001272137066970963,
            "effect": "volume",
            "key": "uncachedInput",
            "label": "uncached input, token volume",
            "share": 0.07581269767407409,
            "shareLower": 0.07412905748228368,
            "shareUpper": 0.07757564767873716
          },
          {
            "amount": -0.007377862933029037,
            "effect": "price",
            "key": "uncachedInput",
            "label": "uncached input, price per token",
            "share": 0.4396819387979164,
            "shareLower": 0.433395502260312,
            "shareUpper": 0.4456921628830486
          },
          {
            "amount": -5.000000000000012E-05,
            "effect": "volume",
            "key": "cacheRead",
            "label": "cache reads, token volume",
            "share": 0.0029797377830750966,
            "shareLower": 0.0029148853478429917,
            "shareUpper": 0.003047541649735887
          },
          {
            "amount": 1.2197274440461925E-19,
            "effect": "price",
            "key": "cacheRead",
            "label": "cache reads, price per token",
            "share": -7.268935900156093E-18,
            "shareLower": -7.434340374113323E-18,
            "shareUpper": -7.110731310024441E-18
          },
          {
            "amount": 0,
            "effect": "volume",
            "key": "cacheWrite",
            "label": "cache writes, token volume",
            "share": -0,
            "shareLower": 0,
            "shareUpper": 0
          },
          {
            "amount": 0,
            "effect": "price",
            "key": "cacheWrite",
            "label": "cache writes, price per token",
            "share": -0,
            "shareLower": 0,
            "shareUpper": 0
          },
          {
            "amount": -0.0016731702459417323,
            "effect": "volume",
            "key": "output",
            "label": "visible output, token volume",
            "share": 0.09971217198699239,
            "shareLower": 0.09752395767150965,
            "shareUpper": 0.1020013934365996
          },
          {
            "amount": -0.0029668297540582688,
            "effect": "price",
            "key": "output",
            "label": "visible output, price per token",
            "share": 0.17680749428237597,
            "shareLower": 0.1743090494727868,
            "shareUpper": 0.17919582468371767
          },
          {
            "amount": -0.0019248280269168604,
            "effect": "volume",
            "key": "reasoning",
            "label": "reasoning, token volume",
            "share": 0.11470965595452089,
            "shareLower": 0.11221306025554958,
            "shareUpper": 0.11731987161216147
          },
          {
            "amount": -0.0015151719730831404,
            "effect": "price",
            "key": "reasoning",
            "label": "reasoning, price per token",
            "share": 0.09029630352104531,
            "shareLower": 0.0883310516760478,
            "shareUpper": 0.09235099388966723
          },
          {
            "amount": -0.004920135339829556,
            "effect": "volume",
            "key": "all",
            "label": "token volume, all components",
            "share": 0.29321426339866247,
            "shareLower": 0.2867809607571859,
            "shareUpper": 0.2999444543772341
          },
          {
            "amount": -0.011859864660170447,
            "effect": "price",
            "key": "all",
            "label": "price per token, all components",
            "share": 0.7067857366013377,
            "shareLower": 0.700055545622766,
            "shareUpper": 0.7132190392428142
          }
        ],
        "difference": -0.01678,
        "differenceLower": -0.017153333333333333,
        "differenceUpper": -0.016406666666666667,
        "first": "A",
        "firstTotal": 0.0140125,
        "grouped": false,
        "notEstablished": false,
        "second": "C",
        "secondTotal": 0.0307925,
        "sharedQuestions": 6,
        "volumeRatio": 0.7938554761823443
      },
      {
        "axis": "cost",
        "components": [
          {
            "amount": -0.002258739233355464,
            "effect": "volume",
            "key": "uncachedInput",
            "label": "uncached input, token volume",
            "share": 0.08375006426976137,
            "shareLower": 0.0819811161412116,
            "shareUpper": 0.08559906789829871
          },
          {
            "amount": -0.011616260766644534,
            "effect": "price",
            "key": "uncachedInput",
            "label": "uncached input, price per token",
            "share": 0.4307104474098827,
            "shareLower": 0.4246243323288879,
            "shareUpper": 0.43654412904585704
          },
          {
            "amount": -7.500000000000013E-05,
            "effect": "volume",
            "key": "cacheRead",
            "label": "cache reads, token volume",
            "share": 0.0027808676307007835,
            "shareLower": 0.0027243007628042176,
            "shareUpper": 0.0028398333964407482
          },
          {
            "amount": 1.2197274440461925E-19,
            "effect": "price",
            "key": "cacheRead",
            "label": "cache reads, price per token",
            "share": -4.522534089900603E-18,
            "shareLower": -4.618430306876914E-18,
            "shareUpper": -4.430539208304367E-18
          },
          {
            "amount": 0,
            "effect": "volume",
            "key": "cacheWrite",
            "label": "cache writes, token volume",
            "share": -0,
            "shareLower": 0,
            "shareUpper": 0
          },
          {
            "amount": 0,
            "effect": "price",
            "key": "cacheWrite",
            "label": "cache writes, price per token",
            "share": -0,
            "shareLower": 0,
            "shareUpper": 0
          },
          {
            "amount": -0.002709378650036514,
            "effect": "volume",
            "key": "output",
            "label": "visible output, token volume",
            "share": 0.10045897849597754,
            "shareLower": 0.09838060593944616,
            "shareUpper": 0.10262812918759866
          },
          {
            "amount": -0.0046706213499634885,
            "effect": "price",
            "key": "output",
            "label": "visible output, price per token",
            "share": 0.1731783963649792,
            "shareLower": 0.17075316577643027,
            "shareUpper": 0.1755024307478041
          },
          {
            "amount": -0.0031340018984751367,
            "effect": "volume",
            "key": "reasoning",
            "label": "reasoning, token volume",
            "share": 0.11620325912032395,
            "shareLower": 0.11383951683527559,
            "shareUpper": 0.11866724341064523
          },
          {
            "amount": -0.002505998101524864,
            "effect": "price",
            "key": "reasoning",
            "label": "reasoning, price per token",
            "share": 0.09291798670837466,
            "shareLower": 0.09102790052760129,
            "shareUpper": 0.09488822800169873
          },
          {
            "amount": -0.008177119781867115,
            "effect": "volume",
            "key": "all",
            "label": "token volume, all components",
            "share": 0.3031931695167636,
            "shareLower": 0.2969255396787376,
            "shareUpper": 0.3097342738929833
          },
          {
            "amount": -0.018792880218132888,
            "effect": "price",
            "key": "all",
            "label": "price per token, all components",
            "share": 0.6968068304832366,
            "shareLower": 0.6902657261070169,
            "shareUpper": 0.7030744603212624
          }
        ],
        "difference": -0.026969999999999997,
        "differenceLower": -0.027530000000000006,
        "differenceUpper": -0.02640999999999999,
        "first": "A",
        "firstTotal": 0.0140125,
        "grouped": false,
        "notEstablished": false,
        "second": "D",
        "secondTotal": 0.0409825,
        "sharedQuestions": 6,
        "volumeRatio": 0.7222488715266346
      },
      {
        "axis": "cost",
        "components": [
          {
            "amount": -0.0034638809958028563,
            "effect": "volume",
            "key": "uncachedInput",
            "label": "uncached input, token volume",
            "share": 0.09029929603239982,
            "shareLower": 0.08847384606868634,
            "shareUpper": 0.09220478312288537
          },
          {
            "amount": -0.016236119004197146,
            "effect": "price",
            "key": "uncachedInput",
            "label": "uncached input, price per token",
            "share": 0.4232564912460154,
            "shareLower": 0.4173662909643177,
            "shareUpper": 0.4289144935153573
          },
          {
            "amount": -0.00010000000000000002,
            "effect": "volume",
            "key": "cacheRead",
            "label": "cache reads, token volume",
            "share": 0.0026068821689259657,
            "shareLower": 0.0025571087623593594,
            "shareUpper": 0.002658631690889756
          },
          {
            "amount": 5.421010862427522E-20,
            "effect": "price",
            "key": "cacheRead",
            "label": "cache reads, price per token",
            "share": -1.4131936554816275E-18,
            "shareLower": -1.4412471275507415E-18,
            "shareUpper": -1.3862114377158683E-18
          },
          {
            "amount": 0,
            "effect": "volume",
            "key": "cacheWrite",
            "label": "cache writes, token volume",
            "share": -0,
            "shareLower": 0,
            "shareUpper": 0
          },
          {
            "amount": 0,
            "effect": "price",
            "key": "cacheWrite",
            "label": "cache writes, price per token",
            "share": -0,
            "shareLower": 0,
            "shareUpper": 0
          },
          {
            "amount": -0.0038758047738109,
            "effect": "volume",
            "key": "output",
            "label": "visible output, token volume",
            "share": 0.10103766355085769,
            "shareLower": 0.09905465164418154,
            "shareUpper": 0.10310333128241897
          },
          {
            "amount": -0.0065241952261890996,
            "effect": "price",
            "key": "output",
            "label": "visible output, price per token",
            "share": 0.17007808201744268,
            "shareLower": 0.16772261696288404,
            "shareUpper": 0.17233982500089184
          },
          {
            "amount": -0.004509050183542426,
            "effect": "volume",
            "key": "reasoning",
            "label": "reasoning, token volume",
            "share": 0.117545625222691,
            "shareLower": 0.11530131734254419,
            "shareUpper": 0.11987903713778174
          },
          {
            "amount": -0.003650949816457573,
            "effect": "price",
            "key": "reasoning",
            "label": "reasoning, price per token",
            "share": 0.09517595976166772,
            "shareLower": 0.09335875766597954,
            "shareUpper": 0.09706530883882242
          },
          {
            "amount": -0.011948735953156181,
            "effect": "volume",
            "key": "all",
            "label": "token volume, all components",
            "share": 0.31148946697487445,
            "shareLower": 0.3053869238177714,
            "shareUpper": 0.3178457832339758
          },
          {
            "amount": -0.026411264046843817,
            "effect": "price",
            "key": "all",
            "label": "price per token, all components",
            "share": 0.6885105330251258,
            "shareLower": 0.6821542167660242,
            "shareUpper": 0.6946130761822287
          }
        ],
        "difference": -0.03835999999999999,
        "differenceLower": -0.039106666666666665,
        "differenceUpper": -0.03761333333333333,
        "first": "A",
        "firstTotal": 0.0140125,
        "grouped": false,
        "notEstablished": false,
        "second": "E",
        "secondTotal": 0.052372499999999995,
        "sharedQuestions": 6,
        "volumeRatio": 0.663200689125152
      },
      {
        "axis": "cost",
        "components": [
          {
            "amount": -0.0014214254155187006,
            "effect": "volume",
            "key": "uncachedInput",
            "label": "uncached input, token volume",
            "share": 0.12479591005431967,
            "shareLower": 0.12277931904129795,
            "shareUpper": 0.12687998096366945
          },
          {
            "amount": -0.004403574584481303,
            "effect": "price",
            "key": "uncachedInput",
            "label": "uncached input, price per token",
            "share": 0.38661761057781424,
            "shareLower": 0.38115334245198046,
            "shareUpper": 0.3919053915835221
          },
          {
            "amount": -2.4999999999999893E-05,
            "effect": "volume",
            "key": "cacheRead",
            "label": "cache reads, token volume",
            "share": 0.0021949078138718083,
            "shareLower": 0.0021595162683558794,
            "shareUpper": 0.0022314787265694633
          },
          {
            "amount": -6.437450399132683E-20,
            "effect": "price",
            "key": "cacheRead",
            "label": "cache reads, price per token",
            "share": 5.651844072987431E-18,
            "shareLower": 5.560711545464454E-18,
            "shareUpper": 5.7460134476042974E-18
          },
          {
            "amount": 0,
            "effect": "volume",
            "key": "cacheWrite",
            "label": "cache writes, token volume",
            "share": -0,
            "shareLower": 0,
            "shareUpper": 0
          },
          {
            "amount": 0,
            "effect": "price",
            "key": "cacheWrite",
            "label": "cache writes, price per token",
            "share": -0,
            "shareLower": 0,
            "shareUpper": 0
          },
          {
            "amount": -0.0011895189903255057,
            "effect": "volume",
            "key": "output",
            "label": "visible output, token volume",
            "share": 0.10443538106457471,
            "shareLower": 0.10274923852427484,
            "shareUpper": 0.10617784742896794
          },
          {
            "amount": -0.0018304810096744927,
            "effect": "price",
            "key": "output",
            "label": "visible output, price per token",
            "share": 0.16070948285114076,
            "shareLower": 0.15862429479060883,
            "shareUpper": 0.1627272947322758
          },
          {
            "amount": -0.0013602714149543844,
            "effect": "volume",
            "key": "reasoning",
            "label": "reasoning, token volume",
            "share": 0.11942681430679408,
            "shareLower": 0.11750112999893912,
            "shareUpper": 0.12141666899325075
          },
          {
            "amount": -0.0011597285850456136,
            "effect": "price",
            "key": "reasoning",
            "label": "reasoning, price per token",
            "share": 0.10181989333148497,
            "shareLower": 0.1001781098513344,
            "shareUpper": 0.10351638664495273
          },
          {
            "amount": -0.0039962158207985906,
            "effect": "volume",
            "key": "all",
            "label": "token volume, all components",
            "share": 0.35085301323956025,
            "shareLower": 0.3451892038328678,
            "shareUpper": 0.3567059761124573
          },
          {
            "amount": -0.007393784179201409,
            "effect": "price",
            "key": "all",
            "label": "price per token, all components",
            "share": 0.6491469867604399,
            "shareLower": 0.643294023887542,
            "shareUpper": 0.6548107961671323
          }
        ],
        "difference": -0.011389999999999997,
        "differenceLower": -0.011576666666666666,
        "differenceUpper": -0.011203333333333336,
        "first": "D",
        "firstTotal": 0.0409825,
        "grouped": false,
        "notEstablished": false,
        "second": "E",
        "secondTotal": 0.052372499999999995,
        "sharedQuestions": 6,
        "volumeRatio": 0.9175557258189582
      }
    ],
    "costRows": [
      {
        "kind": "model",
        "label": "Orion Max",
        "letter": "A",
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
        "token": "A",
        "total": 0.0140125,
        "unit": "usd"
      },
      {
        "kind": "model",
        "label": "Vega Pro",
        "letter": "B",
        "segments": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.0082125
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.00015
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.008879999999999999
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.004560000000000001
          }
        ],
        "token": "B",
        "total": 0.021802500000000002,
        "unit": "usd"
      },
      {
        "kind": "model",
        "label": "Lyra Mini",
        "letter": "C",
        "segments": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.0128375
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.000175
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.011340000000000001
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.00644
          }
        ],
        "token": "C",
        "total": 0.0307925,
        "unit": "usd"
      },
      {
        "kind": "model",
        "label": "Nova Lite",
        "letter": "D",
        "segments": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.0180625
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.0002
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.014080000000000002
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.00864
          }
        ],
        "token": "D",
        "total": 0.0409825,
        "unit": "usd"
      },
      {
        "kind": "model",
        "label": "Zeta Prime",
        "letter": "E",
        "segments": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.023887500000000002
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.00022499999999999997
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.0171
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.011159999999999998
          }
        ],
        "token": "E",
        "total": 0.052372499999999995,
        "unit": "usd"
      }
    ],
    "engineVersion": 12,
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
            "label": "wait before output",
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
        "token": "A",
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
      },
      {
        "atRisk": [
          {
            "key": "noTool",
            "label": "answers without tools",
            "value": 0.16666666666666666
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
            "value": 2.833333333333333
          }
        ],
        "budgetOrDefectAnswers": 0,
        "cacheReadShare": 0.24742268041237114,
        "callsPerAnswer": 2.5,
        "cost": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.0082125
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.00015
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.008879999999999999
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.004560000000000001
          }
        ],
        "criticalAnswers": 0,
        "label": "Vega Pro",
        "medianQuality": 81,
        "modelTimeMs": 9030,
        "noToolAnswers": 2,
        "outputTokens": 1120,
        "reasoningShare": 0.3392857142857143,
        "reasoningTokens": 380,
        "scoredAnswers": 6,
        "telemetryAnswers": 6,
        "time": [
          {
            "key": "ttft",
            "label": "wait before output",
            "value": 2.545
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 6.185
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
        "token": "B",
        "tokens": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 3650
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 1200
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 740
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 380
          }
        ],
        "ttftPerCallP50Ms": 1010
      },
      {
        "atRisk": [
          {
            "key": "noTool",
            "label": "answers without tools",
            "value": 1.1666666666666667
          },
          {
            "key": "budgetOrDefect",
            "label": "budget, truncation or output defects",
            "value": 0
          },
          {
            "key": "criticalError",
            "label": "confirmed critical errors",
            "value": 7.166666666666667
          },
          {
            "key": "otherLow",
            "label": "other low-scoring answers",
            "value": 0
          }
        ],
        "budgetOrDefectAnswers": 0,
        "cacheReadShare": 0.2616822429906542,
        "callsPerAnswer": 2.5,
        "cost": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.0128375
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.000175
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.011340000000000001
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.00644
          }
        ],
        "criticalAnswers": 1,
        "label": "Lyra Mini",
        "medianQuality": 71,
        "modelTimeMs": 10480,
        "noToolAnswers": 2,
        "outputTokens": 1270,
        "reasoningShare": 0.36220472440944884,
        "reasoningTokens": 460,
        "scoredAnswers": 6,
        "telemetryAnswers": 6,
        "time": [
          {
            "key": "ttft",
            "label": "wait before output",
            "value": 3.165
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 7.015
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
        "token": "C",
        "tokens": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 3950
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 1400
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 810
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 460
          }
        ],
        "ttftPerCallP50Ms": 1260
      },
      {
        "atRisk": [
          {
            "key": "noTool",
            "label": "answers without tools",
            "value": 6.333333333333333
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
            "value": 2
          }
        ],
        "budgetOrDefectAnswers": 0,
        "cacheReadShare": 0.27350427350427353,
        "callsPerAnswer": 2.5,
        "cost": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.0180625
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.0002
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.014080000000000002
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.00864
          }
        ],
        "criticalAnswers": 0,
        "label": "Nova Lite",
        "medianQuality": 70,
        "modelTimeMs": 12030,
        "noToolAnswers": 2,
        "outputTokens": 1420,
        "reasoningShare": 0.38028169014084506,
        "reasoningTokens": 540,
        "scoredAnswers": 6,
        "telemetryAnswers": 6,
        "time": [
          {
            "key": "ttft",
            "label": "wait before output",
            "value": 3.795
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 7.935
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
        "token": "D",
        "tokens": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 4250
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 1600
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 880
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 540
          }
        ],
        "ttftPerCallP50Ms": 1510
      },
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
            "value": 0
          },
          {
            "key": "otherLow",
            "label": "other low-scoring answers",
            "value": 5.583333333333333
          }
        ],
        "budgetOrDefectAnswers": 0,
        "cacheReadShare": 0.28346456692913385,
        "callsPerAnswer": 2.5,
        "cost": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 0.023887500000000002
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 0.00022499999999999997
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 0.0171
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 0.011159999999999998
          }
        ],
        "criticalAnswers": 0,
        "label": "Zeta Prime",
        "medianQuality": 53.5,
        "modelTimeMs": 13480,
        "noToolAnswers": 2,
        "outputTokens": 1570,
        "reasoningShare": 0.39490445859872614,
        "reasoningTokens": 620,
        "scoredAnswers": 6,
        "telemetryAnswers": 6,
        "time": [
          {
            "key": "ttft",
            "label": "wait before output",
            "value": 4.415
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 8.765
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
        "token": "E",
        "tokens": [
          {
            "key": "uncachedInput",
            "label": "uncached input",
            "value": 4550
          },
          {
            "key": "cacheRead",
            "label": "cache reads",
            "value": 1800
          },
          {
            "key": "cacheWrite",
            "label": "cache writes",
            "value": 0
          },
          {
            "key": "output",
            "label": "visible output",
            "value": 950
          },
          {
            "key": "reasoning",
            "label": "reasoning",
            "value": 620
          }
        ],
        "ttftPerCallP50Ms": 1760
      }
    ],
    "gradeCap": "Each model ran only once per suite, so this document cannot tell whether its results would repeat. No cause can be graded above Probable (Moderate) until a second run of each suite is made, apart from a price effect, which follows from the price cards.",
    "hypotheses": [
      {
        "against": [],
        "axis": "intelligence",
        "confounds": [],
        "factKeys": [
          "pair.A.B.quality.reference",
          "pair.A.B.quality.allPairs"
        ],
        "for": [
          "The paired Intelligence Index test is not established after the Holm adjustment across 4 tests.",
          "The paired Intelligence Index test is not established after the Holm adjustment across 10 tests.",
          "The two 95% intervals overlap."
        ],
        "grade": "Low",
        "gradeReasons": [
          "with one run per suite, the tests can miss a real difference (grade capped at Low)"
        ],
        "id": "H0-Q",
        "key": "H0-Q.1",
        "kind": "null",
        "label": "no intelligence difference between Model A and Model B",
        "loci": [],
        "locus": "—",
        "models": [
          "A",
          "B"
        ],
        "modelsText": "Model A and Model B",
        "nextRunNumber": 1,
        "number": 1,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the Intelligence Index is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model A vs Model B",
        "statement": "The Intelligence Index difference between Model A and Model B is within sampling noise."
      },
      {
        "against": [
          "The two 95% intervals do not overlap."
        ],
        "axis": "intelligence",
        "confounds": [],
        "factKeys": [
          "pair.A.C.quality.reference",
          "pair.A.C.quality.allPairs"
        ],
        "for": [
          "The paired Intelligence Index test is not established after the Holm adjustment across 4 tests.",
          "The paired Intelligence Index test is not established after the Holm adjustment across 10 tests."
        ],
        "grade": "Low",
        "gradeReasons": [
          "with one run per suite, the tests can miss a real difference (grade capped at Low)"
        ],
        "id": "H0-Q",
        "key": "H0-Q.2",
        "kind": "null",
        "label": "no intelligence difference between Model A and Model C",
        "loci": [],
        "locus": "—",
        "models": [
          "A",
          "C"
        ],
        "modelsText": "Model A and Model C",
        "nextRunNumber": 1,
        "number": 2,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the Intelligence Index is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model A vs Model C",
        "statement": "The Intelligence Index difference between Model A and Model C is within sampling noise."
      },
      {
        "against": [
          "The two 95% intervals do not overlap."
        ],
        "axis": "intelligence",
        "confounds": [],
        "factKeys": [
          "pair.A.D.quality.reference",
          "pair.A.D.quality.allPairs"
        ],
        "for": [
          "The paired Intelligence Index test is not established after the Holm adjustment across 4 tests.",
          "The paired Intelligence Index test is not established after the Holm adjustment across 10 tests."
        ],
        "grade": "Low",
        "gradeReasons": [
          "with one run per suite, the tests can miss a real difference (grade capped at Low)"
        ],
        "id": "H0-Q",
        "key": "H0-Q.3",
        "kind": "null",
        "label": "no intelligence difference between Model A and Model D",
        "loci": [],
        "locus": "—",
        "models": [
          "A",
          "D"
        ],
        "modelsText": "Model A and Model D",
        "nextRunNumber": 1,
        "number": 3,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the Intelligence Index is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model A vs Model D",
        "statement": "The Intelligence Index difference between Model A and Model D is within sampling noise."
      },
      {
        "against": [
          "The two 95% intervals do not overlap."
        ],
        "axis": "intelligence",
        "confounds": [],
        "factKeys": [
          "pair.A.E.quality.reference",
          "pair.A.E.quality.allPairs"
        ],
        "for": [
          "The paired Intelligence Index test is not established after the Holm adjustment across 4 tests.",
          "The paired Intelligence Index test is not established after the Holm adjustment across 10 tests."
        ],
        "grade": "Low",
        "gradeReasons": [
          "with one run per suite, the tests can miss a real difference (grade capped at Low)"
        ],
        "id": "H0-Q",
        "key": "H0-Q.4",
        "kind": "null",
        "label": "no intelligence difference between Model A and Model E",
        "loci": [],
        "locus": "—",
        "models": [
          "A",
          "E"
        ],
        "modelsText": "Model A and Model E",
        "nextRunNumber": 1,
        "number": 4,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the Intelligence Index is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model A vs Model E",
        "statement": "The Intelligence Index difference between Model A and Model E is within sampling noise."
      },
      {
        "against": [
          "The two 95% intervals do not overlap."
        ],
        "axis": "intelligence",
        "confounds": [],
        "factKeys": [
          "pair.D.E.quality.allPairs"
        ],
        "for": [
          "The paired Intelligence Index test is not established after the Holm adjustment across 10 tests."
        ],
        "grade": "Low",
        "gradeReasons": [
          "with one run per suite, the tests can miss a real difference (grade capped at Low)"
        ],
        "id": "H0-Q",
        "key": "H0-Q.5",
        "kind": "null",
        "label": "no intelligence difference between Model D and Model E",
        "loci": [],
        "locus": "—",
        "models": [
          "D",
          "E"
        ],
        "modelsText": "Model D and Model E",
        "nextRunNumber": 1,
        "number": 5,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the Intelligence Index is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model D vs Model E",
        "statement": "The Intelligence Index difference between Model D and Model E is within sampling noise."
      },
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
          "with one run per suite, the tests can miss a real difference (grade capped at Low)"
        ],
        "id": "H0-W",
        "key": "H0-W.1",
        "kind": "null",
        "label": "no difference within Model A's own questions",
        "loci": [],
        "locus": "—",
        "models": [
          "A"
        ],
        "modelsText": "Model A",
        "nextRunNumber": 1,
        "number": 1,
        "priceEffect": false,
        "refutedIf": "A Mann–Whitney contrast is established after the Holm adjustment.",
        "robustness": [],
        "shortStatement": "Noise within Model A",
        "statement": "The contrasts between groups of Model A's own questions are within sampling noise."
      },
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
          "with one run per suite, the tests can miss a real difference (grade capped at Low)"
        ],
        "id": "H0-W",
        "key": "H0-W.2",
        "kind": "null",
        "label": "no difference within Model E's own questions",
        "loci": [],
        "locus": "—",
        "models": [
          "E"
        ],
        "modelsText": "Model E",
        "nextRunNumber": 1,
        "number": 2,
        "priceEffect": false,
        "refutedIf": "A Mann–Whitney contrast is established after the Holm adjustment.",
        "robustness": [],
        "shortStatement": "Noise within Model E",
        "statement": "The contrasts between groups of Model E's own questions are within sampling noise."
      },
      {
        "against": [],
        "axis": "intelligence",
        "checkNumber": 1,
        "confounds": [],
        "factKeys": [
          "questions.sharedLow"
        ],
        "for": [
          "Q3: 4 of 5 models scored below 50."
        ],
        "grade": "Low",
        "gradeReasons": [
          "it is observed, not measured"
        ],
        "id": "HQ3",
        "key": "HQ3.1",
        "kind": "pair",
        "label": "shared failures on the same questions",
        "loci": [
          "suite",
          "corpus",
          "chat"
        ],
        "locus": "suite / corpus / chat",
        "models": [
          "A",
          "B",
          "C",
          "D",
          "E"
        ],
        "modelsText": "Model A, Model B, Model C, Model D and Model E",
        "number": 1,
        "priceEffect": false,
        "refutedIf": "One model answered the question well.",
        "robustness": [],
        "shortStatement": "Shared failures: instrument",
        "statement": "Shared failures on specific questions point to the instrument, not the models."
      },
      {
        "against": [],
        "axis": "speed",
        "confounds": [],
        "factKeys": [
          "pair.A.B.speed.reference",
          "pair.A.B.speed.allPairs"
        ],
        "for": [
          "The paired model time test is not established after the Holm adjustment across 4 tests.",
          "The paired model time test is not established after the Holm adjustment across 10 tests."
        ],
        "grade": "Low",
        "gradeReasons": [
          "with one run per suite, the tests can miss a real difference (grade capped at Low)"
        ],
        "id": "H0-S",
        "key": "H0-S.1",
        "kind": "null",
        "label": "no speed difference between Model A and Model B",
        "loci": [],
        "locus": "—",
        "models": [
          "A",
          "B"
        ],
        "modelsText": "Model A and Model B",
        "nextRunNumber": 1,
        "number": 1,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the model time is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model A vs Model B",
        "statement": "The model time difference between Model A and Model B is within sampling noise."
      },
      {
        "against": [],
        "axis": "speed",
        "confounds": [],
        "factKeys": [
          "pair.A.C.speed.reference",
          "pair.A.C.speed.allPairs"
        ],
        "for": [
          "The paired model time test is not established after the Holm adjustment across 4 tests.",
          "The paired model time test is not established after the Holm adjustment across 10 tests."
        ],
        "grade": "Low",
        "gradeReasons": [
          "with one run per suite, the tests can miss a real difference (grade capped at Low)"
        ],
        "id": "H0-S",
        "key": "H0-S.2",
        "kind": "null",
        "label": "no speed difference between Model A and Model C",
        "loci": [],
        "locus": "—",
        "models": [
          "A",
          "C"
        ],
        "modelsText": "Model A and Model C",
        "nextRunNumber": 1,
        "number": 2,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the model time is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model A vs Model C",
        "statement": "The model time difference between Model A and Model C is within sampling noise."
      },
      {
        "against": [],
        "axis": "speed",
        "confounds": [],
        "factKeys": [
          "pair.A.D.speed.reference",
          "pair.A.D.speed.allPairs"
        ],
        "for": [
          "The paired model time test is not established after the Holm adjustment across 4 tests.",
          "The paired model time test is not established after the Holm adjustment across 10 tests."
        ],
        "grade": "Low",
        "gradeReasons": [
          "with one run per suite, the tests can miss a real difference (grade capped at Low)"
        ],
        "id": "H0-S",
        "key": "H0-S.3",
        "kind": "null",
        "label": "no speed difference between Model A and Model D",
        "loci": [],
        "locus": "—",
        "models": [
          "A",
          "D"
        ],
        "modelsText": "Model A and Model D",
        "nextRunNumber": 1,
        "number": 3,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the model time is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model A vs Model D",
        "statement": "The model time difference between Model A and Model D is within sampling noise."
      },
      {
        "against": [],
        "axis": "speed",
        "confounds": [],
        "factKeys": [
          "pair.A.E.speed.reference",
          "pair.A.E.speed.allPairs"
        ],
        "for": [
          "The paired model time test is not established after the Holm adjustment across 4 tests.",
          "The paired model time test is not established after the Holm adjustment across 10 tests."
        ],
        "grade": "Low",
        "gradeReasons": [
          "with one run per suite, the tests can miss a real difference (grade capped at Low)"
        ],
        "id": "H0-S",
        "key": "H0-S.4",
        "kind": "null",
        "label": "no speed difference between Model A and Model E",
        "loci": [],
        "locus": "—",
        "models": [
          "A",
          "E"
        ],
        "modelsText": "Model A and Model E",
        "nextRunNumber": 1,
        "number": 4,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the model time is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model A vs Model E",
        "statement": "The model time difference between Model A and Model E is within sampling noise."
      },
      {
        "against": [],
        "axis": "speed",
        "confounds": [],
        "factKeys": [
          "pair.D.E.speed.allPairs"
        ],
        "for": [
          "The paired model time test is not established after the Holm adjustment across 10 tests."
        ],
        "grade": "Low",
        "gradeReasons": [
          "with one run per suite, the tests can miss a real difference (grade capped at Low)"
        ],
        "id": "H0-S",
        "key": "H0-S.5",
        "kind": "null",
        "label": "no speed difference between Model D and Model E",
        "loci": [],
        "locus": "—",
        "models": [
          "D",
          "E"
        ],
        "modelsText": "Model D and Model E",
        "nextRunNumber": 1,
        "number": 5,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the model time is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model D vs Model E",
        "statement": "The model time difference between Model D and Model E is within sampling noise."
      },
      {
        "against": [],
        "axis": "speed",
        "confounds": [],
        "factKeys": [
          "model.A.speed.modelCallsPerAnswer"
        ],
        "for": [
          "Across Model A's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95% interval 0.23 to 0.99, n 6)."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "Model A ran only once per suite"
        ],
        "id": "WS3",
        "key": "WS3.1",
        "kind": "within",
        "label": "Model A's time spent on tool rounds",
        "loci": [
          "model",
          "chat"
        ],
        "locus": "model / chat",
        "models": [
          "A"
        ],
        "modelsText": "Model A",
        "nextRunNumber": 1,
        "number": 1,
        "priceEffect": false,
        "refutedIf": "The ρ interval includes zero.",
        "robustness": [
          {
            "detail": "Model A ran only once per suite.",
            "name": "Replicates",
            "status": "Failed"
          },
          {
            "detail": "ρ stays at or above 0.5 without any one question.",
            "name": "Leave one question out",
            "status": "Passed"
          }
        ],
        "shortStatement": "Tool rounds, time: Model A",
        "statement": "Tool rounds multiply Model A's time (where its time goes, not a comparison)."
      },
      {
        "against": [],
        "axis": "speed",
        "confounds": [],
        "factKeys": [
          "model.D.speed.modelCallsPerAnswer"
        ],
        "for": [
          "Across Model D's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95% interval 0.23 to 0.99, n 6)."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "Model D ran only once per suite"
        ],
        "id": "WS3",
        "key": "WS3.2",
        "kind": "within",
        "label": "Model D's time spent on tool rounds",
        "loci": [
          "model",
          "chat"
        ],
        "locus": "model / chat",
        "models": [
          "D"
        ],
        "modelsText": "Model D",
        "nextRunNumber": 1,
        "number": 2,
        "priceEffect": false,
        "refutedIf": "The ρ interval includes zero.",
        "robustness": [
          {
            "detail": "Model D ran only once per suite.",
            "name": "Replicates",
            "status": "Failed"
          },
          {
            "detail": "ρ stays at or above 0.5 without any one question.",
            "name": "Leave one question out",
            "status": "Passed"
          }
        ],
        "shortStatement": "Tool rounds, time: Model D",
        "statement": "Tool rounds multiply Model D's time (where its time goes, not a comparison)."
      },
      {
        "against": [],
        "axis": "speed",
        "confounds": [],
        "factKeys": [
          "model.E.speed.modelCallsPerAnswer"
        ],
        "for": [
          "Across Model E's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95% interval 0.23 to 0.99, n 6)."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "Model E ran only once per suite"
        ],
        "id": "WS3",
        "key": "WS3.3",
        "kind": "within",
        "label": "Model E's time spent on tool rounds",
        "loci": [
          "model",
          "chat"
        ],
        "locus": "model / chat",
        "models": [
          "E"
        ],
        "modelsText": "Model E",
        "nextRunNumber": 1,
        "number": 3,
        "priceEffect": false,
        "refutedIf": "The ρ interval includes zero.",
        "robustness": [
          {
            "detail": "Model E ran only once per suite.",
            "name": "Replicates",
            "status": "Failed"
          },
          {
            "detail": "ρ stays at or above 0.5 without any one question.",
            "name": "Leave one question out",
            "status": "Passed"
          }
        ],
        "shortStatement": "Tool rounds, time: Model E",
        "statement": "Tool rounds multiply Model E's time (where its time goes, not a comparison)."
      },
      {
        "against": [
          "Reasoning is 39% of Model E's output tokens."
        ],
        "axis": "speed",
        "confounds": [],
        "explains": 8765,
        "factKeys": [
          "rca.within.E.time.outputGeneration.share",
          "model.E.tokens.reasoningShare"
        ],
        "for": [
          "Output generation is 65% (64–66%) of model time per question."
        ],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: reasoning is 39% of Model E's output tokens"
        ],
        "id": "WS2",
        "key": "WS2.1",
        "kind": "within",
        "label": "Model E's time spent generating output",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "E"
        ],
        "modelsText": "Model E",
        "number": 1,
        "priceEffect": false,
        "refutedIf": "The reasoning share is under one half.",
        "robustness": [
          {
            "detail": "Model E ran only once per suite.",
            "name": "Replicates",
            "status": "Failed"
          },
          {
            "detail": "The deciding share keeps its sign and stays the largest without the question that contributes most.",
            "name": "Leave one question out",
            "status": "Passed"
          }
        ],
        "share": 0.6502225519287834,
        "shareLower": 0.6437415578568212,
        "shareUpper": 0.6561855670103092,
        "shortStatement": "Output generation: Model E",
        "statement": "Most of Model E's time is output generation, driven by reasoning tokens (where its time goes, not a comparison)."
      },
      {
        "against": [
          "Reasoning is 38% of Model D's output tokens."
        ],
        "axis": "speed",
        "confounds": [],
        "explains": 7935.000000000001,
        "factKeys": [
          "rca.within.D.time.outputGeneration.share",
          "model.D.tokens.reasoningShare"
        ],
        "for": [
          "Output generation is 66% (65–67%) of model time per question."
        ],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: reasoning is 38% of Model D's output tokens"
        ],
        "id": "WS2",
        "key": "WS2.2",
        "kind": "within",
        "label": "Model D's time spent generating output",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "D"
        ],
        "modelsText": "Model D",
        "number": 2,
        "priceEffect": false,
        "refutedIf": "The reasoning share is under one half.",
        "robustness": [
          {
            "detail": "Model D ran only once per suite.",
            "name": "Replicates",
            "status": "Failed"
          },
          {
            "detail": "The deciding share keeps its sign and stays the largest without the question that contributes most.",
            "name": "Leave one question out",
            "status": "Passed"
          }
        ],
        "share": 0.6596009975062345,
        "shareLower": 0.6519634211941904,
        "shareUpper": 0.6667064724146167,
        "shortStatement": "Output generation: Model D",
        "statement": "Most of Model D's time is output generation, driven by reasoning tokens (where its time goes, not a comparison)."
      },
      {
        "against": [
          "Reasoning is 31% of Model A's output tokens."
        ],
        "axis": "speed",
        "confounds": [],
        "explains": 5265,
        "factKeys": [
          "rca.within.A.time.outputGeneration.share",
          "model.A.tokens.reasoningShare"
        ],
        "for": [
          "Output generation is 70% (69–71%) of model time per question."
        ],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: reasoning is 31% of Model A's output tokens"
        ],
        "id": "WS2",
        "key": "WS2.3",
        "kind": "within",
        "label": "Model A's time spent generating output",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "A"
        ],
        "modelsText": "Model A",
        "number": 3,
        "priceEffect": false,
        "refutedIf": "The reasoning share is under one half.",
        "robustness": [
          {
            "detail": "Model A ran only once per suite.",
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
        "shortStatement": "Output generation: Model A",
        "statement": "Most of Model A's time is output generation, driven by reasoning tokens (where its time goes, not a comparison)."
      },
      {
        "against": [
          "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)."
        ],
        "axis": "cost",
        "confounds": [],
        "factKeys": [
          "pair.A.B.cost.reference",
          "pair.A.B.cost.allPairs"
        ],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)"
        ],
        "id": "H0-C",
        "key": "H0-C.1",
        "kind": "null",
        "label": "no cost difference between Model A and Model B",
        "loci": [],
        "locus": "—",
        "models": [
          "A",
          "B"
        ],
        "modelsText": "Model A and Model B",
        "number": 1,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the cost per question is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model A vs Model B",
        "statement": "The cost per question difference between Model A and Model B is within sampling noise."
      },
      {
        "against": [
          "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)."
        ],
        "axis": "cost",
        "confounds": [],
        "factKeys": [
          "pair.A.C.cost.reference",
          "pair.A.C.cost.allPairs"
        ],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)"
        ],
        "id": "H0-C",
        "key": "H0-C.2",
        "kind": "null",
        "label": "no cost difference between Model A and Model C",
        "loci": [],
        "locus": "—",
        "models": [
          "A",
          "C"
        ],
        "modelsText": "Model A and Model C",
        "number": 2,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the cost per question is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model A vs Model C",
        "statement": "The cost per question difference between Model A and Model C is within sampling noise."
      },
      {
        "against": [
          "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)."
        ],
        "axis": "cost",
        "confounds": [],
        "factKeys": [
          "pair.A.D.cost.reference",
          "pair.A.D.cost.allPairs"
        ],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)"
        ],
        "id": "H0-C",
        "key": "H0-C.3",
        "kind": "null",
        "label": "no cost difference between Model A and Model D",
        "loci": [],
        "locus": "—",
        "models": [
          "A",
          "D"
        ],
        "modelsText": "Model A and Model D",
        "number": 3,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the cost per question is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model A vs Model D",
        "statement": "The cost per question difference between Model A and Model D is within sampling noise."
      },
      {
        "against": [
          "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)."
        ],
        "axis": "cost",
        "confounds": [],
        "factKeys": [
          "pair.A.E.cost.reference",
          "pair.A.E.cost.allPairs"
        ],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)"
        ],
        "id": "H0-C",
        "key": "H0-C.4",
        "kind": "null",
        "label": "no cost difference between Model A and Model E",
        "loci": [],
        "locus": "—",
        "models": [
          "A",
          "E"
        ],
        "modelsText": "Model A and Model E",
        "number": 4,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the cost per question is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model A vs Model E",
        "statement": "The cost per question difference between Model A and Model E is within sampling noise."
      },
      {
        "against": [
          "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)."
        ],
        "axis": "cost",
        "confounds": [],
        "factKeys": [
          "pair.D.E.cost.allPairs"
        ],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)"
        ],
        "id": "H0-C",
        "key": "H0-C.5",
        "kind": "null",
        "label": "no cost difference between Model D and Model E",
        "loci": [],
        "locus": "—",
        "models": [
          "D",
          "E"
        ],
        "modelsText": "Model D and Model E",
        "number": 5,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the cost per question is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Model D vs Model E",
        "statement": "The cost per question difference between Model D and Model E is within sampling noise."
      },
      {
        "against": [],
        "axis": "cost",
        "confounds": [],
        "explains": 0.02826,
        "factKeys": [
          "model.E.cost.component.output.perQuestion",
          "model.E.cost.component.reasoning.perQuestion"
        ],
        "for": [
          "Visible output and reasoning are 54% of Model E's cost per question."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "Model E ran only once per suite"
        ],
        "id": "WC1",
        "key": "WC1.1",
        "kind": "within",
        "label": "Model E's output cost",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "E"
        ],
        "modelsText": "Model E",
        "number": 1,
        "priceEffect": false,
        "refutedIf": "Another component is larger.",
        "robustness": [
          {
            "detail": "Model E ran only once per suite.",
            "name": "Replicates",
            "status": "Failed"
          }
        ],
        "share": 0.5395961621079766,
        "shortStatement": "Output cost: Model E",
        "statement": "Output (with reasoning) is Model E's largest cost component (where its cost goes, not a comparison)."
      },
      {
        "against": [],
        "axis": "cost",
        "confounds": [],
        "explains": 0.022720000000000004,
        "factKeys": [
          "model.D.cost.component.output.perQuestion",
          "model.D.cost.component.reasoning.perQuestion"
        ],
        "for": [
          "Visible output and reasoning are 55% of Model D's cost per question."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "Model D ran only once per suite"
        ],
        "id": "WC1",
        "key": "WC1.2",
        "kind": "within",
        "label": "Model D's output cost",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "D"
        ],
        "modelsText": "Model D",
        "number": 2,
        "priceEffect": false,
        "refutedIf": "Another component is larger.",
        "robustness": [
          {
            "detail": "Model D ran only once per suite.",
            "name": "Replicates",
            "status": "Failed"
          }
        ],
        "share": 0.5543829683401453,
        "shortStatement": "Output cost: Model D",
        "statement": "Output (with reasoning) is Model D's largest cost component (where its cost goes, not a comparison)."
      },
      {
        "against": [],
        "axis": "cost",
        "confounds": [],
        "explains": 0.0097,
        "factKeys": [
          "model.A.cost.component.output.perQuestion",
          "model.A.cost.component.reasoning.perQuestion"
        ],
        "for": [
          "Visible output and reasoning are 69% of Model A's cost per question."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "Model A ran only once per suite"
        ],
        "id": "WC1",
        "key": "WC1.3",
        "kind": "within",
        "label": "Model A's output cost",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "A"
        ],
        "modelsText": "Model A",
        "number": 3,
        "priceEffect": false,
        "refutedIf": "Another component is larger.",
        "robustness": [
          {
            "detail": "Model A ran only once per suite.",
            "name": "Replicates",
            "status": "Failed"
          }
        ],
        "share": 0.6922390722569134,
        "shortStatement": "Output cost: Model A",
        "statement": "Output (with reasoning) is Model A's largest cost component (where its cost goes, not a comparison)."
      },
      {
        "against": [
          "Model E's cache-read share is 28% against the models' median of 26%."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.0034638809958028563,
        "factKeys": [
          "rca.cost.A.E.uncachedInput.volume.share",
          "rca.cost.A.E.total",
          "model.E.tokens.cacheReadShare"
        ],
        "for": [
          "Uncached input, token volume accounts for 9% (9–9%) of the $0.038 per-question cost difference."
        ],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: Model E's cache-read share is 28% against the models' median of 26%"
        ],
        "id": "HC4",
        "key": "HC4.1",
        "kind": "pair",
        "label": "Model E's weak prompt caching, compared with Model A",
        "loci": [
          "chat",
          "provider"
        ],
        "locus": "chat / provider",
        "models": [
          "E",
          "A"
        ],
        "modelsText": "Model E and Model A",
        "number": 1,
        "priceEffect": false,
        "refutedIf": "The cache-read share is comparable to the other models'.",
        "robustness": [
          {
            "detail": "The deciding share keeps its sign and stays the largest without the question that contributes most.",
            "name": "Leave one question out",
            "status": "Passed"
          },
          {
            "detail": "Both models ran only once per suite.",
            "name": "Replicates",
            "status": "Failed"
          },
          {
            "detail": "No configuration key differs beyond the model.",
            "name": "Configuration",
            "status": "Passed"
          }
        ],
        "share": 0.09029929603239982,
        "shareLower": 0.08847384606868634,
        "shareUpper": 0.09220478312288537,
        "shortStatement": "Weak caching: Model E",
        "statement": "Weak prompt caching raises Model E's cost against Model A."
      },
      {
        "against": [
          "Model D's cache-read share is 27% against the models' median of 26%."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.002258739233355464,
        "factKeys": [
          "rca.cost.A.D.uncachedInput.volume.share",
          "rca.cost.A.D.total",
          "model.D.tokens.cacheReadShare"
        ],
        "for": [
          "Uncached input, token volume accounts for 8% (8–9%) of the $0.027 per-question cost difference."
        ],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: Model D's cache-read share is 27% against the models' median of 26%"
        ],
        "id": "HC4",
        "key": "HC4.2",
        "kind": "pair",
        "label": "Model D's weak prompt caching, compared with Model A",
        "loci": [
          "chat",
          "provider"
        ],
        "locus": "chat / provider",
        "models": [
          "D",
          "A"
        ],
        "modelsText": "Model D and Model A",
        "number": 2,
        "priceEffect": false,
        "refutedIf": "The cache-read share is comparable to the other models'.",
        "robustness": [
          {
            "detail": "The deciding share keeps its sign and stays the largest without the question that contributes most.",
            "name": "Leave one question out",
            "status": "Passed"
          },
          {
            "detail": "Both models ran only once per suite.",
            "name": "Replicates",
            "status": "Failed"
          },
          {
            "detail": "No configuration key differs beyond the model.",
            "name": "Configuration",
            "status": "Passed"
          }
        ],
        "share": 0.08375006426976137,
        "shareLower": 0.0819811161412116,
        "shareUpper": 0.08559906789829871,
        "shortStatement": "Weak caching: Model D",
        "statement": "Weak prompt caching raises Model D's cost against Model A."
      },
      {
        "against": [
          "Model E's cache-read share is 28% against the models' median of 26%."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.0014214254155187006,
        "factKeys": [
          "rca.cost.D.E.uncachedInput.volume.share",
          "rca.cost.D.E.total",
          "model.E.tokens.cacheReadShare"
        ],
        "for": [
          "Uncached input, token volume accounts for 12% (12–13%) of the $0.011 per-question cost difference."
        ],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: Model E's cache-read share is 28% against the models' median of 26%"
        ],
        "id": "HC4",
        "key": "HC4.3",
        "kind": "pair",
        "label": "Model E's weak prompt caching, compared with Model D",
        "loci": [
          "chat",
          "provider"
        ],
        "locus": "chat / provider",
        "models": [
          "E",
          "D"
        ],
        "modelsText": "Model E and Model D",
        "number": 3,
        "priceEffect": false,
        "refutedIf": "The cache-read share is comparable to the other models'.",
        "robustness": [
          {
            "detail": "The deciding share keeps its sign and stays the largest without the question that contributes most.",
            "name": "Leave one question out",
            "status": "Passed"
          },
          {
            "detail": "Both models ran only once per suite.",
            "name": "Replicates",
            "status": "Failed"
          },
          {
            "detail": "No configuration key differs beyond the model.",
            "name": "Configuration",
            "status": "Passed"
          }
        ],
        "share": 0.12479591005431967,
        "shareLower": 0.12277931904129795,
        "shareUpper": 0.12687998096366945,
        "shortStatement": "Weak caching: Model E",
        "statement": "Weak prompt caching raises Model E's cost against Model D."
      },
      {
        "against": [
          "Model C's cache-read share is 26% against the models' median of 26%."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.001272137066970963,
        "factKeys": [
          "rca.cost.A.C.uncachedInput.volume.share",
          "rca.cost.A.C.total",
          "model.C.tokens.cacheReadShare"
        ],
        "for": [
          "Uncached input, token volume accounts for 8% (7–8%) of the $0.017 per-question cost difference."
        ],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: Model C's cache-read share is 26% against the models' median of 26%"
        ],
        "id": "HC4",
        "key": "HC4.4",
        "kind": "pair",
        "label": "Model C's weak prompt caching, compared with Model A",
        "loci": [
          "chat",
          "provider"
        ],
        "locus": "chat / provider",
        "models": [
          "C",
          "A"
        ],
        "modelsText": "Model C and Model A",
        "number": 4,
        "priceEffect": false,
        "refutedIf": "The cache-read share is comparable to the other models'.",
        "robustness": [
          {
            "detail": "The deciding share keeps its sign and stays the largest without the question that contributes most.",
            "name": "Leave one question out",
            "status": "Passed"
          },
          {
            "detail": "Both models ran only once per suite.",
            "name": "Replicates",
            "status": "Failed"
          },
          {
            "detail": "No configuration key differs beyond the model.",
            "name": "Configuration",
            "status": "Passed"
          }
        ],
        "share": 0.07581269767407409,
        "shareLower": 0.07412905748228368,
        "shareUpper": 0.07757564767873716,
        "shortStatement": "Weak caching: Model C",
        "statement": "Weak prompt caching raises Model C's cost against Model A."
      },
      {
        "against": [
          "The volume effect is 31% (31–32%) of the difference."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.026411264046843817,
        "factKeys": [
          "rca.cost.A.E.all.price.share",
          "rca.cost.A.E.total",
          "model.E.price.output.perMillion"
        ],
        "for": [
          "Price per token, all components accounts for 69% (68–69%) of the $0.038 per-question cost difference."
        ],
        "grade": "High",
        "gradeReasons": [],
        "id": "HC2",
        "key": "HC2.1",
        "kind": "pair",
        "label": "Model E's higher price per token, compared with Model A",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "E",
          "A"
        ],
        "modelsText": "Model E and Model A",
        "number": 1,
        "priceEffect": true,
        "refutedIf": "Not testable by a run: the share follows from the price cards and these runs' token counts.",
        "robustness": [
          {
            "detail": "The deciding share keeps its sign and stays the largest without the question that contributes most.",
            "name": "Leave one question out",
            "status": "Passed"
          },
          {
            "detail": "No configuration key differs beyond the model.",
            "name": "Configuration",
            "status": "Passed"
          }
        ],
        "share": 0.6885105330251258,
        "shareLower": 0.6821542167660242,
        "shareUpper": 0.6946130761822287,
        "shortStatement": "Price per token: Model E",
        "statement": "Model E costs more than Model A mainly from its price per token."
      },
      {
        "against": [
          "The volume effect is 30% (30–31%) of the difference."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.018792880218132888,
        "factKeys": [
          "rca.cost.A.D.all.price.share",
          "rca.cost.A.D.total",
          "model.D.price.output.perMillion"
        ],
        "for": [
          "Price per token, all components accounts for 70% (69–70%) of the $0.027 per-question cost difference."
        ],
        "grade": "High",
        "gradeReasons": [],
        "id": "HC2",
        "key": "HC2.2",
        "kind": "pair",
        "label": "Model D's higher price per token, compared with Model A",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "D",
          "A"
        ],
        "modelsText": "Model D and Model A",
        "number": 2,
        "priceEffect": true,
        "refutedIf": "Not testable by a run: the share follows from the price cards and these runs' token counts.",
        "robustness": [
          {
            "detail": "The deciding share keeps its sign and stays the largest without the question that contributes most.",
            "name": "Leave one question out",
            "status": "Passed"
          },
          {
            "detail": "No configuration key differs beyond the model.",
            "name": "Configuration",
            "status": "Passed"
          }
        ],
        "share": 0.6968068304832366,
        "shareLower": 0.6902657261070169,
        "shareUpper": 0.7030744603212624,
        "shortStatement": "Price per token: Model D",
        "statement": "Model D costs more than Model A mainly from its price per token."
      },
      {
        "against": [
          "The volume effect is 29% (29–30%) of the difference."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.011859864660170447,
        "factKeys": [
          "rca.cost.A.C.all.price.share",
          "rca.cost.A.C.total",
          "model.C.price.output.perMillion"
        ],
        "for": [
          "Price per token, all components accounts for 71% (70–71%) of the $0.017 per-question cost difference."
        ],
        "grade": "High",
        "gradeReasons": [],
        "id": "HC2",
        "key": "HC2.3",
        "kind": "pair",
        "label": "Model C's higher price per token, compared with Model A",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "C",
          "A"
        ],
        "modelsText": "Model C and Model A",
        "number": 3,
        "priceEffect": true,
        "refutedIf": "Not testable by a run: the share follows from the price cards and these runs' token counts.",
        "robustness": [
          {
            "detail": "The deciding share keeps its sign and stays the largest without the question that contributes most.",
            "name": "Leave one question out",
            "status": "Passed"
          },
          {
            "detail": "No configuration key differs beyond the model.",
            "name": "Configuration",
            "status": "Passed"
          }
        ],
        "share": 0.7067857366013377,
        "shareLower": 0.700055545622766,
        "shareUpper": 0.7132190392428142,
        "shortStatement": "Price per token: Model C",
        "statement": "Model C costs more than Model A mainly from its price per token."
      },
      {
        "against": [
          "The volume effect is 35% (35–36%) of the difference."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.0073937841792014085,
        "factKeys": [
          "rca.cost.D.E.all.price.share",
          "rca.cost.D.E.total",
          "model.E.price.output.perMillion"
        ],
        "for": [
          "Price per token, all components accounts for 65% (64–65%) of the $0.011 per-question cost difference."
        ],
        "grade": "High",
        "gradeReasons": [],
        "id": "HC2",
        "key": "HC2.4",
        "kind": "pair",
        "label": "Model E's higher price per token, compared with Model D",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "E",
          "D"
        ],
        "modelsText": "Model E and Model D",
        "number": 4,
        "priceEffect": true,
        "refutedIf": "Not testable by a run: the share follows from the price cards and these runs' token counts.",
        "robustness": [
          {
            "detail": "The deciding share keeps its sign and stays the largest without the question that contributes most.",
            "name": "Leave one question out",
            "status": "Passed"
          },
          {
            "detail": "No configuration key differs beyond the model.",
            "name": "Configuration",
            "status": "Passed"
          }
        ],
        "share": 0.6491469867604399,
        "shareLower": 0.643294023887542,
        "shareUpper": 0.6548107961671323,
        "shortStatement": "Price per token: Model E",
        "statement": "Model E costs more than Model D mainly from its price per token."
      },
      {
        "against": [
          "The volume effect is 28% (27–29%) of the difference."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.005602724508327252,
        "factKeys": [
          "rca.cost.A.B.all.price.share",
          "rca.cost.A.B.total",
          "model.B.price.output.perMillion"
        ],
        "for": [
          "Price per token, all components accounts for 72% (71–73%) of the $0.0078 per-question cost difference."
        ],
        "grade": "High",
        "gradeReasons": [],
        "id": "HC2",
        "key": "HC2.5",
        "kind": "pair",
        "label": "Model B's higher price per token, compared with Model A",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "B",
          "A"
        ],
        "modelsText": "Model B and Model A",
        "number": 5,
        "priceEffect": true,
        "refutedIf": "Not testable by a run: the share follows from the price cards and these runs' token counts.",
        "robustness": [
          {
            "detail": "The deciding share keeps its sign and stays the largest without the question that contributes most.",
            "name": "Leave one question out",
            "status": "Passed"
          },
          {
            "detail": "No configuration key differs beyond the model.",
            "name": "Configuration",
            "status": "Passed"
          }
        ],
        "share": 0.71922009092776,
        "shareLower": 0.7123096297876061,
        "shareUpper": 0.7258059219416408,
        "shortStatement": "Price per token: Model B",
        "statement": "Model B costs more than Model A mainly from its price per token."
      }
    ],
    "knownFacts": [
      {
        "factKeys": [
          "rca.cost.A.B.uncachedInput.price.share"
        ],
        "kind": "decomposition",
        "number": 1,
        "text": "Uncached input, price per token accounts for 45% (44–46%) of the per-question cost difference between Model A and Model B."
      },
      {
        "factKeys": [
          "rca.cost.A.C.uncachedInput.price.share"
        ],
        "kind": "decomposition",
        "number": 2,
        "text": "Uncached input, price per token accounts for 44% (43–45%) of the per-question cost difference between Model A and Model C."
      },
      {
        "factKeys": [
          "rca.cost.A.D.uncachedInput.price.share"
        ],
        "kind": "decomposition",
        "number": 3,
        "text": "Uncached input, price per token accounts for 43% (42–44%) of the per-question cost difference between Model A and Model D."
      },
      {
        "factKeys": [
          "rca.cost.A.E.uncachedInput.price.share"
        ],
        "kind": "decomposition",
        "number": 4,
        "text": "Uncached input, price per token accounts for 42% (42–43%) of the per-question cost difference between Model A and Model E."
      },
      {
        "factKeys": [
          "rca.cost.D.E.uncachedInput.price.share"
        ],
        "kind": "decomposition",
        "number": 5,
        "text": "Uncached input, price per token accounts for 39% (38–39%) of the per-question cost difference between Model D and Model E."
      },
      {
        "factKeys": [
          "model.A.profile.noToolAnswers"
        ],
        "kind": "count",
        "number": 6,
        "text": "Model A answered 2 of 6 scored questions without a tool call."
      },
      {
        "factKeys": [
          "model.B.profile.noToolAnswers"
        ],
        "kind": "count",
        "number": 7,
        "text": "Model B answered 2 of 6 scored questions without a tool call."
      },
      {
        "factKeys": [
          "model.C.profile.noToolAnswers"
        ],
        "kind": "count",
        "number": 8,
        "text": "Model C answered 2 of 6 scored questions without a tool call."
      },
      {
        "factKeys": [
          "model.C.errors.critical"
        ],
        "kind": "count",
        "number": 9,
        "text": "Model C has 1 critical error in 6 scored answers."
      },
      {
        "factKeys": [
          "model.D.profile.noToolAnswers"
        ],
        "kind": "count",
        "number": 10,
        "text": "Model D answered 2 of 6 scored questions without a tool call."
      },
      {
        "factKeys": [
          "model.E.profile.noToolAnswers"
        ],
        "kind": "count",
        "number": 11,
        "text": "Model E answered 2 of 6 scored questions without a tool call."
      }
    ],
    "models": [
      {
        "entryKey": "run:31",
        "isReference": true,
        "isSubject": false,
        "label": "Orion Max",
        "letter": "A",
        "modelId": "orion-max",
        "provider": "Northwind",
        "runIds": [
          31
        ],
        "token": "A"
      },
      {
        "entryKey": "run:32",
        "isReference": false,
        "isSubject": false,
        "label": "Vega Pro",
        "letter": "B",
        "modelId": "vega-pro",
        "provider": "Southstar",
        "runIds": [
          32
        ],
        "token": "B"
      },
      {
        "entryKey": "run:33",
        "isReference": false,
        "isSubject": false,
        "label": "Lyra Mini",
        "letter": "C",
        "modelId": "lyra-mini",
        "provider": "Eastgate",
        "runIds": [
          33
        ],
        "token": "C"
      },
      {
        "entryKey": "run:34",
        "isReference": false,
        "isSubject": false,
        "label": "Nova Lite",
        "letter": "D",
        "modelId": "nova-lite",
        "provider": "Westlake",
        "runIds": [
          34
        ],
        "token": "D"
      },
      {
        "entryKey": "run:35",
        "isReference": false,
        "isSubject": false,
        "label": "Zeta Prime",
        "letter": "E",
        "modelId": "zeta-prime",
        "provider": "Midland",
        "runIds": [
          35
        ],
        "token": "E"
      }
    ],
    "notes": [
      "Costs are priced at today's catalog cards, as the comparison prices them."
    ],
    "origin": "Comparison",
    "pointsAtRisk": [
      {
        "kind": "model",
        "label": "Orion Max",
        "letter": "A",
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
        "token": "A",
        "total": 10.666666666666668,
        "unit": "points"
      },
      {
        "kind": "model",
        "label": "Vega Pro",
        "letter": "B",
        "segments": [
          {
            "key": "noTool",
            "label": "answers without tools",
            "value": 0.16666666666666666
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
            "value": 2.833333333333333
          }
        ],
        "token": "B",
        "total": 2.9999999999999996,
        "unit": "points"
      },
      {
        "kind": "model",
        "label": "Lyra Mini",
        "letter": "C",
        "segments": [
          {
            "key": "noTool",
            "label": "answers without tools",
            "value": 1.1666666666666667
          },
          {
            "key": "budgetOrDefect",
            "label": "budget, truncation or output defects",
            "value": 0
          },
          {
            "key": "criticalError",
            "label": "confirmed critical errors",
            "value": 7.166666666666667
          },
          {
            "key": "otherLow",
            "label": "other low-scoring answers",
            "value": 0
          }
        ],
        "token": "C",
        "total": 8.333333333333334,
        "unit": "points"
      },
      {
        "kind": "model",
        "label": "Nova Lite",
        "letter": "D",
        "segments": [
          {
            "key": "noTool",
            "label": "answers without tools",
            "value": 6.333333333333333
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
            "value": 2
          }
        ],
        "token": "D",
        "total": 8.333333333333332,
        "unit": "points"
      },
      {
        "kind": "model",
        "label": "Zeta Prime",
        "letter": "E",
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
            "value": 5.583333333333333
          }
        ],
        "token": "E",
        "total": 5.583333333333333,
        "unit": "points"
      }
    ],
    "pricingBasis": "Priced from the catalog as of 2026-09-20. Comparable across dates; not what was actually spent.",
    "pricingBasisKind": "catalog",
    "priorsChecked": [],
    "profiles": [
      {
        "dimensions": [
          80,
          80,
          80,
          80
        ],
        "flags": [],
        "letter": "A",
        "qualityIndex": 85,
        "strengths": [
          {
            "kind": "gainedPointsOn",
            "questions": [
              "Q2",
              "Q4",
              "Q1",
              "Q6",
              "Q5"
            ],
            "short": "+15.5 points on 5 questions",
            "text": "answers above the other models' median by 10 or more: +15.5 index points on 5 questions (Q1, Q2, Q4 and 2 more)"
          },
          {
            "kind": "strength",
            "questions": [],
            "short": "tied for first on Accuracy",
            "text": "tied for first of 5 models on Accuracy"
          },
          {
            "kind": "strength",
            "questions": [],
            "short": "tied for first on Completeness",
            "text": "tied for first of 5 models on Completeness"
          }
        ],
        "token": "A",
        "weaknesses": [
          {
            "kind": "lostPointsOn",
            "questions": [
              "Q3"
            ],
            "short": "answers without tools, 9.8 points",
            "text": "answers without tools: 9.8 index points on 1 question (Q3)"
          }
        ]
      },
      {
        "dimensions": [
          80,
          80,
          80,
          80
        ],
        "flags": [],
        "letter": "B",
        "qualityIndex": 80,
        "strengths": [
          {
            "kind": "gainedPointsOn",
            "questions": [
              "Q3",
              "Q2",
              "Q1",
              "Q5"
            ],
            "short": "+13.1 points on 4 questions",
            "text": "answers above the other models' median by 10 or more: +13.1 index points on 4 questions (Q1, Q2, Q3 and 1 more)"
          },
          {
            "kind": "strength",
            "questions": [],
            "short": "tied for first on Accuracy",
            "text": "tied for first of 5 models on Accuracy"
          },
          {
            "kind": "strength",
            "questions": [],
            "short": "tied for first on Completeness",
            "text": "tied for first of 5 models on Completeness"
          }
        ],
        "token": "B",
        "weaknesses": [
          {
            "kind": "lostPointsOn",
            "questions": [
              "Q3",
              "Q4"
            ],
            "short": "mostly Accuracy, 2.8 points",
            "text": "low-scoring answers, mostly Accuracy: 2.8 index points on 2 questions (Q3 and Q4)"
          }
        ]
      },
      {
        "dimensions": [
          64.83333333333333,
          64.83333333333333,
          64.83333333333333,
          64.83333333333333
        ],
        "flags": [
          {
            "kind": "criticalErrors",
            "questions": [
              "Q3"
            ],
            "short": "1 critical error",
            "text": "1 critical error (Q3)"
          }
        ],
        "letter": "C",
        "qualityIndex": 70,
        "strengths": [
          {
            "kind": "gainedPointsOn",
            "questions": [],
            "short": "none",
            "text": "no answer 10 or more above the other models' median"
          }
        ],
        "token": "C",
        "weaknesses": [
          {
            "kind": "lostPointsOn",
            "questions": [
              "Q3"
            ],
            "short": "confirmed critical errors, 7.2 points",
            "text": "confirmed critical errors: 7.2 index points on 1 question (Q3)"
          }
        ]
      },
      {
        "dimensions": [
          64.16666666666667,
          64.16666666666667,
          64.16666666666667,
          64.16666666666667
        ],
        "flags": [],
        "letter": "D",
        "qualityIndex": 70,
        "strengths": [
          {
            "kind": "gainedPointsOn",
            "questions": [],
            "short": "none",
            "text": "no answer 10 or more above the other models' median"
          }
        ],
        "token": "D",
        "weaknesses": [
          {
            "kind": "lostPointsOn",
            "questions": [
              "Q3"
            ],
            "short": "answers without tools, 6.3 points",
            "text": "answers without tools: 6.3 index points on 1 question (Q3)"
          }
        ]
      },
      {
        "dimensions": [
          50,
          50,
          50,
          50
        ],
        "flags": [
          {
            "kind": "isolatedMisses",
            "questions": [
              "Q1"
            ],
            "short": "1 isolated miss",
            "text": "1 isolated miss (Q1)"
          }
        ],
        "letter": "E",
        "qualityIndex": 55,
        "strengths": [
          {
            "kind": "gainedPointsOn",
            "questions": [],
            "short": "none",
            "text": "no answer 10 or more above the other models' median"
          }
        ],
        "token": "E",
        "weaknesses": [
          {
            "kind": "lostPointsOn",
            "questions": [
              "Q3",
              "Q1",
              "Q4"
            ],
            "short": "mostly Accuracy, 5.6 points",
            "text": "low-scoring answers, mostly Accuracy: 5.6 index points on 3 questions (Q1, Q3 and Q4)"
          },
          {
            "kind": "lowestRank",
            "questions": [],
            "short": "last on Accuracy",
            "text": "last of 5 models on Accuracy"
          }
        ]
      }
    ],
    "qualitySplits": [
      {
        "components": [
          {
            "amount": 0,
            "key": "accuracy",
            "label": "Accuracy",
            "share": 0
          },
          {
            "amount": 0,
            "key": "completeness",
            "label": "Completeness",
            "share": 0
          },
          {
            "amount": 0,
            "key": "conciseness",
            "label": "Conciseness",
            "share": 0
          },
          {
            "amount": 0,
            "key": "readability",
            "label": "Readability",
            "share": 0
          },
          {
            "amount": 5,
            "key": "other",
            "label": "Critical-error cap, difficulty weighting and spread",
            "share": 1
          }
        ],
        "first": "A",
        "indexDifference": 5,
        "second": "B"
      },
      {
        "components": [
          {
            "amount": 8.341666666666661,
            "key": "accuracy",
            "label": "Accuracy",
            "share": 0.5561111111111108
          },
          {
            "amount": 3.791666666666664,
            "key": "completeness",
            "label": "Completeness",
            "share": 0.2527777777777776
          },
          {
            "amount": 1.5166666666666657,
            "key": "conciseness",
            "label": "Conciseness",
            "share": 0.10111111111111105
          },
          {
            "amount": 1.5166666666666657,
            "key": "readability",
            "label": "Readability",
            "share": 0.10111111111111105
          },
          {
            "amount": -0.1666666666666572,
            "key": "other",
            "label": "Critical-error cap, difficulty weighting and spread",
            "share": -0.01111111111111048
          }
        ],
        "first": "A",
        "indexDifference": 15,
        "second": "C"
      },
      {
        "components": [
          {
            "amount": 8.70833333333332,
            "key": "accuracy",
            "label": "Accuracy",
            "share": 0.5805555555555546
          },
          {
            "amount": 3.958333333333327,
            "key": "completeness",
            "label": "Completeness",
            "share": 0.26388888888888845
          },
          {
            "amount": 1.5833333333333308,
            "key": "conciseness",
            "label": "Conciseness",
            "share": 0.10555555555555539
          },
          {
            "amount": 1.5833333333333308,
            "key": "readability",
            "label": "Readability",
            "share": 0.10555555555555539
          },
          {
            "amount": -0.8333333333333073,
            "key": "other",
            "label": "Critical-error cap, difficulty weighting and spread",
            "share": -0.05555555555555382
          }
        ],
        "first": "A",
        "indexDifference": 15,
        "second": "D"
      },
      {
        "components": [
          {
            "amount": 16.50000000000001,
            "key": "accuracy",
            "label": "Accuracy",
            "share": 0.5500000000000004
          },
          {
            "amount": 7.500000000000004,
            "key": "completeness",
            "label": "Completeness",
            "share": 0.25000000000000017
          },
          {
            "amount": 3.0000000000000018,
            "key": "conciseness",
            "label": "Conciseness",
            "share": 0.10000000000000006
          },
          {
            "amount": 3.0000000000000018,
            "key": "readability",
            "label": "Readability",
            "share": 0.10000000000000006
          },
          {
            "amount": -1.4210854715202004E-14,
            "key": "other",
            "label": "Critical-error cap, difficulty weighting and spread",
            "share": -4.736951571734001E-16
          }
        ],
        "first": "A",
        "indexDifference": 30,
        "second": "E"
      },
      {
        "components": [
          {
            "amount": 7.7916666666666865,
            "key": "accuracy",
            "label": "Accuracy",
            "share": 0.5194444444444458
          },
          {
            "amount": 3.5416666666666754,
            "key": "completeness",
            "label": "Completeness",
            "share": 0.2361111111111117
          },
          {
            "amount": 1.4166666666666703,
            "key": "conciseness",
            "label": "Conciseness",
            "share": 0.09444444444444469
          },
          {
            "amount": 1.4166666666666703,
            "key": "readability",
            "label": "Readability",
            "share": 0.09444444444444469
          },
          {
            "amount": 0.8333333333332984,
            "key": "other",
            "label": "Critical-error cap, difficulty weighting and spread",
            "share": 0.05555555555555323
          }
        ],
        "first": "D",
        "indexDifference": 15,
        "second": "E"
      }
    ],
    "questions": [
      {
        "axis": "intelligence",
        "comparativeKeys": [],
        "hypothesisKeys": [],
        "neighbor": "B",
        "number": 1,
        "orderEstablished": false,
        "priceCard": false,
        "signals": [
          "answers above the other models' median by 10 or more: +15.5 index points on 5 questions (Q1, Q2, Q4 and 2 more)",
          "tied for first of 5 models on Accuracy",
          "tied for first of 5 models on Completeness"
        ],
        "text": "Why did Model A score highest, though its lead over Model B is not established?",
        "token": "A",
        "withinKeys": [],
        "withinOnly": false
      },
      {
        "axis": "intelligence",
        "comparativeKeys": [],
        "hypothesisKeys": [],
        "neighbor": "D",
        "number": 2,
        "orderEstablished": true,
        "priceCard": false,
        "signals": [
          "lost points on low-scoring answers, mostly Accuracy: 5.6 index points on 3 questions (Q1, Q3 and Q4)",
          "1 isolated miss (Q1)"
        ],
        "text": "Why did Model E score lowest?",
        "token": "E",
        "withinKeys": [],
        "withinOnly": false
      },
      {
        "axis": "speed",
        "comparativeKeys": [],
        "grade": "Low",
        "hypothesisKeys": [
          "WS3.3",
          "WS2.1"
        ],
        "number": 3,
        "priceCard": false,
        "signals": [
          "median model time 20.0 s",
          "output generation 59% of its time per question",
          "2.5 model calls per answer"
        ],
        "text": "Why is Model E slowest?",
        "token": "E",
        "withinKeys": [
          "WS3.3",
          "WS2.1"
        ],
        "withinOnly": true
      },
      {
        "axis": "cost",
        "comparativeKeys": [
          "HC4.2",
          "HC2.2"
        ],
        "grade": "High",
        "hypothesisKeys": [
          "HC4.2",
          "HC2.2",
          "WC1.2"
        ],
        "number": 4,
        "priceCard": true,
        "signals": [
          "$0.060 per question",
          "uncached input 44% of its cost per question",
          "27% of input tokens read from the cache"
        ],
        "text": "Why is Model D the most expensive?",
        "token": "D",
        "withinKeys": [
          "WC1.2"
        ],
        "withinOnly": false
      }
    ],
    "recommendedRuns": [
      {
        "afterSystemHypotheses": false,
        "allowSourceCodeReferences": true,
        "assessorName": "Gemini 3.8 Flash",
        "decides": "Confirms H0-Q.1, H0-Q.2, H0-Q.3, H0-Q.4 and H0-Q.5 if the paired test still finds no difference with three runs per model; refutes each if a paired test of the two on the Intelligence Index is established after adjustment. Confirms H0-W.1 and H0-W.2 if the contrasts still find no difference with three runs per model; refutes each if a Mann–Whitney contrast is established after the Holm adjustment. Confirms H0-S.1, H0-S.2, H0-S.3, H0-S.4 and H0-S.5 if the paired test still finds no difference with three runs per model; refutes each if a paired test of the two on the model time is established after adjustment. Confirms WS3.1, WS3.2 and WS3.3 if model time keeps rising with the calls per answer; refutes each if the ρ interval includes zero.",
        "decidesItems": [
          "Confirms H0-Q.1, H0-Q.2, H0-Q.3, H0-Q.4 and H0-Q.5 if the paired test still finds no difference with three runs per model; refutes each if a paired test of the two on the Intelligence Index is established after adjustment.",
          "Confirms H0-W.1 and H0-W.2 if the contrasts still find no difference with three runs per model; refutes each if a Mann–Whitney contrast is established after the Holm adjustment.",
          "Confirms H0-S.1, H0-S.2, H0-S.3, H0-S.4 and H0-S.5 if the paired test still finds no difference with three runs per model; refutes each if a paired test of the two on the model time is established after adjustment.",
          "Confirms WS3.1, WS3.2 and WS3.3 if model time keeps rising with the calls per answer; refutes each if the ρ interval includes zero."
        ],
        "estimatedMinutes": 180,
        "hypothesisIds": [
          "H0-Q.1",
          "H0-Q.2",
          "H0-Q.3",
          "H0-Q.4",
          "H0-Q.5",
          "H0-W.1",
          "H0-W.2",
          "H0-S.1",
          "H0-S.2",
          "H0-S.3",
          "H0-S.4",
          "H0-S.5",
          "WS3.1",
          "WS3.2",
          "WS3.3"
        ],
        "kind": "replicate",
        "launches": 1,
        "mark": "Recommended",
        "modelNames": [
          "Orion Max",
          "Vega Pro",
          "Lyra Mini",
          "Nova Lite",
          "Zeta Prime"
        ],
        "notes": [
          "The estimate for a model without analyzed runs is the analyzed models' mean."
        ],
        "number": 1,
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
          100,
          101,
          102,
          103,
          104
        ],
        "verboseMode": false
      }
    ],
    "referenceToken": "A",
    "scope": "Comparison",
    "speedPairs": [
      {
        "axis": "speed",
        "components": [],
        "difference": -1550,
        "differenceLower": -3803.333333333334,
        "differenceUpper": 670.0000000000009,
        "first": "A",
        "firstTotal": 7480,
        "grouped": false,
        "notEstablished": true,
        "second": "B",
        "secondTotal": 9030,
        "sharedQuestions": 6,
        "unavailableReason": "Not decomposed: the difference is not established."
      },
      {
        "axis": "speed",
        "components": [],
        "difference": -3000,
        "differenceLower": -3400,
        "differenceUpper": -2599.999999999999,
        "first": "A",
        "firstTotal": 7480,
        "grouped": false,
        "notEstablished": true,
        "second": "C",
        "secondTotal": 10480,
        "sharedQuestions": 6,
        "unavailableReason": "Not decomposed: the difference is not established."
      },
      {
        "axis": "speed",
        "components": [],
        "difference": -4550,
        "differenceLower": -7203.333333333334,
        "differenceUpper": -1930,
        "first": "A",
        "firstTotal": 7480,
        "grouped": false,
        "notEstablished": true,
        "second": "D",
        "secondTotal": 12030,
        "sharedQuestions": 6,
        "unavailableReason": "Not decomposed: the difference is not established."
      },
      {
        "axis": "speed",
        "components": [],
        "difference": -6000,
        "differenceLower": -6800,
        "differenceUpper": -5199.999999999999,
        "first": "A",
        "firstTotal": 7480,
        "grouped": false,
        "notEstablished": true,
        "second": "E",
        "secondTotal": 13480,
        "sharedQuestions": 6,
        "unavailableReason": "Not decomposed: the difference is not established."
      },
      {
        "axis": "speed",
        "components": [],
        "difference": -1450,
        "differenceLower": -4870,
        "differenceUpper": 2003.333333333334,
        "first": "D",
        "firstTotal": 12030,
        "grouped": false,
        "notEstablished": true,
        "second": "E",
        "secondTotal": 13480,
        "sharedQuestions": 6,
        "unavailableReason": "Not decomposed: the difference is not established."
      }
    ],
    "speedRows": [
      {
        "kind": "model",
        "label": "Orion Max",
        "letter": "A",
        "segments": [
          {
            "key": "ttft",
            "label": "wait before output",
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
        "token": "A",
        "total": 8.88,
        "unit": "seconds"
      },
      {
        "kind": "model",
        "label": "Vega Pro",
        "letter": "B",
        "segments": [
          {
            "key": "ttft",
            "label": "wait before output",
            "value": 2.5208309591642926
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 6.126263057929725
          },
          {
            "key": "toolExecution",
            "label": "tool execution",
            "value": 1.4857549857549857
          },
          {
            "key": "ownWaits",
            "label": "own waits and retries",
            "value": 0
          },
          {
            "key": "other",
            "label": "other",
            "value": 0.2971509971509972
          }
        ],
        "token": "B",
        "total": 10.43,
        "unit": "seconds"
      },
      {
        "kind": "model",
        "label": "Lyra Mini",
        "letter": "C",
        "segments": [
          {
            "key": "ttft",
            "label": "wait before output",
            "value": 3.1385809682804675
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 6.95644407345576
          },
          {
            "key": "toolExecution",
            "label": "tool execution",
            "value": 1.4874791318864775
          },
          {
            "key": "ownWaits",
            "label": "own waits and retries",
            "value": 0
          },
          {
            "key": "other",
            "label": "other",
            "value": 0.2974958263772955
          }
        ],
        "token": "C",
        "total": 11.88,
        "unit": "seconds"
      },
      {
        "kind": "model",
        "label": "Nova Lite",
        "letter": "D",
        "segments": [
          {
            "key": "ttft",
            "label": "wait before output",
            "value": 3.766951219512195
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 7.876352549889135
          },
          {
            "key": "toolExecution",
            "label": "tool execution",
            "value": 1.4889135254988912
          },
          {
            "key": "ownWaits",
            "label": "own waits and retries",
            "value": 0
          },
          {
            "key": "other",
            "label": "other",
            "value": 0.2977827050997782
          }
        ],
        "token": "D",
        "total": 13.429999999999998,
        "unit": "seconds"
      },
      {
        "kind": "model",
        "label": "Zeta Prime",
        "letter": "E",
        "segments": [
          {
            "key": "ttft",
            "label": "wait before output",
            "value": 4.385527369826436
          },
          {
            "key": "outputGeneration",
            "label": "output generation",
            "value": 8.70648865153538
          },
          {
            "key": "toolExecution",
            "label": "tool execution",
            "value": 1.4899866488651534
          },
          {
            "key": "ownWaits",
            "label": "own waits and retries",
            "value": 0
          },
          {
            "key": "other",
            "label": "other",
            "value": 0.29799732977303073
          }
        ],
        "token": "E",
        "total": 14.88,
        "unit": "seconds"
      }
    ],
    "systemFirst": false
  },
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

## Evaluation terms

- **Purpose statement:** Internal evaluation of candidate AI models for the Overseer assistant within GnollHack.
- **Distillation / training prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.
- **Third-party model content:** Outputs generated by **Orion Max** (Northwind), **Vega Pro** (Southstar), **Lyra Mini** (Eastgate), **Nova Lite** (Westlake) and **Zeta Prime** (Midland), graded by models from Google, and described in this document by **Claude Opus 5.5** (Anthropic) are third-party content evaluated solely for domain-specific benchmark scoring and operational model selection.

---

*Document ID 212 · format version 28 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Full · models named*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically for structure, permitted figures and references, word limits, disclosure of benchmark text, peer names, significance claims, interval-overlap wording, hype words and spelling; the checks do not verify the prose's interpretations.*
