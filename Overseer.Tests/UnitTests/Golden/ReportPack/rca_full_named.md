# Orion Max on the Overseer GnollHack Assistant Benchmark — Root Cause Analysis

*INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.*

- **Date:** 2026-09-28
- **Suite:** GnollHack Core Suite
- **Questions:** 1
- **Runs:** 1 (run 31)
- **Compared with:** Model A = Vega Pro (Southstar, vega-pro); Model B = Lyra Mini (Eastgate, lyra-mini); Model C = Nova Lite (Westlake, nova-lite); Model D = Zeta Prime (Midland, zeta-prime)
- **Pricing basis:** Catalog prices on 2026-09-21

## Summary

### What we found

> [!IMPORTANT]
> **Key finding.** Intelligence: Not established; speed: Probable cause; cost: Not established.

- Zeta Prime is the cheapest model, at $0.010 per question, and its Intelligence Index interval overlaps no other model's.
- Answers without any tool call: Orion Max, 2 of 6 scored answers (33%); Vega Pro, 2 of 6 scored answers (33%); Lyra Mini, 2 of 6 scored answers (33%); Nova Lite, 2 of 6 scored answers (33%); Zeta Prime, 2 of 6 scored answers (33%).

### The questions

> [!NOTE]
> **Question.** **Why did Orion Max score highest, though its lead over Vega Pro is not established?**
>
> *Answer strength: Measured lead over Vega Pro: not established — see its split; no cause compared.*
>
> For Orion Max the measured signals are tied for first of 5 models on Accuracy; tied for first of 5 models on Completeness. The answer strength is Unknown.

> [!NOTE]
> **Question.** **Why did Zeta Prime score lowest?**
>
> *Answer strength: No hypothesis about this model fired — see its profile.*
>
> For Model D the measured signals are lost points on low-scoring answers, mostly Accuracy: 5.6 index points on 3 questions (Q1, Q3 and Q4); 1 isolated miss (Q1). The answer strength is Unknown.

> [!NOTE]
> **Question.** **Why is Zeta Prime slowest?**
>
> *Answer strength: Probable (Moderate) — against the other models, see HS3.1.*
>
> For Model D the measured signals are median model time 20.0 s; against Orion Max: output tokens carries 55% of the 6.0 s difference; output generation 59% of its time per question; 2.5 model calls per answer. The answer strength is Probable (Moderate). The leading hypothesis about its time per question is HS3.1.

> [!NOTE]
> **Question.** **Why is Nova Lite the most expensive?**
>
> *Answer strength: Known from the price cards — see HC2.2.*
>
> For Model C the measured signals are $0.060 per question; uncached input 44% of its cost per question; 27% of input tokens read from the cache. The answer strength is Known from the price cards. The leading hypothesis about its cost per question is HC4.2.

### At a glance

| Model | Intelligence | Median time | Cost per question | Where it lost most points | Flags |
|---|---|---|---|---|---|
| **Orion Max** | 85 (82–88), rank 1–2 (tied) | 12.0 s | $0.050 | Answers without tools, 9.8 points | None |
| Vega Pro | 80 (77–83), rank 1–2 (tied) | 9.0 s | $0.030 | Mostly Accuracy, 2.8 points | None |
| Lyra Mini | 70 (66–74), rank 3–4 (tied) | 15.0 s | $0.020 | Confirmed critical errors, 7.2 points | 1 critical error |
| Nova Lite | 70 (64–76), rank 3–4 (tied) | 8.0 s | $0.060 | Answers without tools, 6.3 points | None |
| Zeta Prime | 55 (50–60), rank 5 | 20.0 s | $0.010 | Mostly Accuracy, 5.6 points | 1 isolated miss |

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
| Critical error | A mistake serious enough to cap the answer's score. |
| Grade words | How sure the document is of a cause, strongest to weakest. |
| Holm adjustment | A correction for running many comparisons at once. |
| Hypothesis | A possible cause that code tested against the data. |
| Intelligence Index | The overall answer-quality score, out of a hundred. |
| Isolated miss | A question one model missed while the others did well. |
| Median and mean | The middle value, and the plain average. |
| Points at risk | The most index points a failure signal could have cost. |
| Replicate run | A second run of the same model, to see if results repeat. |
| Robustness check | A test of whether a result survives a small change. |
| Runs per suite | How many times each model answered each suite. |
| Tied rank | The span of places the model's interval allows. |

## Why each model scored as it did

| Model | Index | Strengths | Lost points on | Flags |
|---|---|---|---|---|
| **Orion Max** | 85 | Tied for first of 5 models on Accuracy; tied for first of 5 models on Completeness | Answers without tools: 9.8 index points on 1 question (Q3) | None |
| Vega Pro | 80 | Tied for first of 5 models on Accuracy; tied for first of 5 models on Completeness | Low-scoring answers, mostly Accuracy: 2.8 index points on 2 questions (Q3 and Q4) | None |
| Lyra Mini | 70 | None ranked first or second | Confirmed critical errors: 7.2 index points on 1 question (Q3) | 1 critical error (Q3) |
| Nova Lite | 70 | None ranked first or second | Answers without tools: 6.3 index points on 1 question (Q3) | None |
| Zeta Prime | 55 | None ranked first or second | Low-scoring answers, mostly Accuracy: 5.6 index points on 3 questions (Q1, Q3 and Q4); last of 5 models on Accuracy | 1 isolated miss (Q1) |

*Each entry is a count, a share or a rank measured over the model's scored answers; none says why. Lost points on: the largest share of the model's index points at risk (the most index points a failure signal could have cost), with its questions (Appendix A). Flags: critical errors (a mistake serious enough to cap the answer's score); isolated misses (a question one model missed while the others did well). An isolated miss is a question the model scored 50 or less on while the other models' median was 75 or more.*

## Scope and design

This analysis sets Orion Max against 4 peers of the same comparison.

### Setup and method

- **Suite:** GnollHack Core Suite, 1 questions.
- **Chat configuration under test:** Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed
- **Model under test:** Orion Max (Northwind, orion-max), thinking level not set; 1 run.
- **Grading:** Each answer is graded on accuracy, completeness, conciseness and readability, weighted Accuracy 55%, Completeness 25%, Conciseness 10%, Readability 10%. Each dimension is graded on behaviorally anchored levels (fixed grade steps, each described by the answer behavior that earns it) scored 1, 15, 35, 55, 72, 87, 100. A critical error caps the answer's quality at 25.
- **Graders:**
  - Assessor: Gemini 3.8 Flash (Google, gemini-3.8-flash), different provider from the model under test
- **Formulas:** Answer quality is the weighted geometric mean (an average that one very low score pulls down sharply) of the four dimension scores, capped by a critical error; in a panel run it is the mean of both graders' scores. The Intelligence Index is the difficulty-weighted mean (an average in which harder questions count for more) of answer quality. Median answer time is the median model time per answer, with tool time excluded. Cost per question is the model under test's spend divided by the questions asked.
- **Comparability:** Every model in this report was measured under one instrument condition, signature `sig-7f3a91`.
- **Pricing basis:** Catalog prices on 2026-09-21
- **Versions:** Harness 41, scoring method 12.

One run per suite on each side limits what this design can decide; the next runs add replicates.

## Evidence: intelligence

| Model | Intelligence Index | 95% interval | Rank | Critical errors |
|---|---|---|---|---|
| **Orion Max** | 85 | 82–88 | 1–2 (tied) | 0 of 6 answers |
| Vega Pro | 80 | 77–83 | 1–2 (tied) | 0 of 6 answers |
| Lyra Mini | 70 | 66–74 | 3–4 (tied) | 1 of 6 answers |
| Nova Lite | 70 | 64–76 | 3–4 (tied) | 0 of 6 answers |
| Zeta Prime | 55 | 50–60 | 5 | 0 of 6 answers |

*A rank shown as a span, such as 5–9 (tied), is a tied rank: the model's 95% interval (the range the true value most likely lies in) overlaps others', so its rank is the span of places the interval allows. Interval bounds are rounded to whole numbers, so two intervals printed as touching may not overlap; ranks use the unrounded bounds.*

| Model | Accuracy | Completeness | Conciseness | Readability |
|---|---|---|---|---|
| **Orion Max** | 80 | 80 | 80 | 80 |
| Vega Pro | 80 | 80 | 80 | 80 |
| Lyra Mini | 65 | 65 | 65 | 65 |
| Nova Lite | 64 | 64 | 64 | 64 |
| Zeta Prime | 50 | 50 | 50 | 50 |

### Paired differences against the peers

| Peer | Paired difference (95% interval) |
|---|---|
| Vega Pro | 0.0 points (-16.5 to +10.5) |
| Lyra Mini | +15.2 points (+9.0 to +20.8) |
| Nova Lite | +15.8 points (+7.7 to +22.5) |
| Zeta Prime | +30.0 points (+19.5 to +38.0) |

*Paired difference: mean per-question difference, subject minus peer, over the questions both answered; 95% paired-bootstrap interval. It reflects question sampling only, is not adjusted for comparing several models, and is not a significance test.*

### What the graders recorded

- The grader recorded a comment on this answer.
  - *Evidence:* Q1 (92 / 100)

The first known fact: Output tokens accounts for 55% (53–56%) of the per-question time difference between Orion Max and Model B.

## Evidence: speed

### Where the time goes

| Model | Wall time per question (median, tools included) | Time to first token | Output generation | Tool execution | Other |
|---|---|---|---|---|---|
| Orion Max | 8.9 s | 1.9 s | 5.2 s | 1.5 s | 0.3 s |
| Vega Pro | 10.4 s | 2.5 s | 6.1 s | 1.5 s | 0.3 s |
| Lyra Mini | 11.9 s | 3.1 s | 7.0 s | 1.5 s | 0.3 s |
| Nova Lite | 13.4 s | 3.8 s | 7.9 s | 1.5 s | 0.3 s |
| Zeta Prime | 14.9 s | 4.4 s | 8.7 s | 1.5 s | 0.3 s |

*Wall time per question: the median time per question, tool execution included, split into its parts in proportion to their mean times. The parts are time to first token (the wait before the model starts writing, mostly its thinking), output generation (the time spent writing the reply, and how fast it is written), tool execution, the model's own waits and retries, and other time. It differs from the median model time the other tables of this document give, which leaves tool execution out. Left out because it is zero or not recorded in every row: own waits and retries.*

### Time differences between the models

- **Orion Max vs Lyra Mini:** -3.0 s per question; leading component output tokens, -1.6 s, 55% (53–56%) of the difference.
- **Orion Max vs Nova Lite:** -4.6 s per question; leading component output tokens, -2.5 s, 55% (35–127%) of the difference.
- **Orion Max vs Zeta Prime:** -6.0 s per question; leading component output tokens, -3.3 s, 55% (54–56%) of the difference.

Not split into parts, because the difference itself may be chance: Orion Max vs Vega Pro.

*Each difference is split into parts that add up exactly to it (a standard method, the logarithmic-mean Divisia index), using only the questions both models have figures for. A share's interval comes from resampling those questions (a bootstrap interval). The difference is the first model's minus the second's. A share can pass 100% when another component pulls the other way. Appendix B gives every component.*

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

### Cost differences between the models

- **Orion Max vs Vega Pro:** -$0.0078 per question; leading component uncached input, price per token, -$0.0035, 45% (44–46%) of the difference.
- **Orion Max vs Lyra Mini:** -$0.017 per question; leading component uncached input, price per token, -$0.0074, 44% (43–45%) of the difference.
- **Orion Max vs Nova Lite:** -$0.027 per question; leading component uncached input, price per token, -$0.012, 43% (42–44%) of the difference.
- **Orion Max vs Zeta Prime:** -$0.038 per question; leading component uncached input, price per token, -$0.016, 42% (42–43%) of the difference.

*Each difference is split into parts that add up exactly to it (a standard method, the logarithmic-mean Divisia index), using only the questions both models have figures for. A share's interval comes from resampling those questions (a bootstrap interval). The difference is the first model's minus the second's. A share can pass 100% when another component pulls the other way. Appendix B gives every component.*

The cost tables split the cost per question into volume and price.

## Hypotheses

### Intelligence

| Hypothesis | Statement | Grade | Share | Settled by |
|---|---|---|---|---|
| HQ3.1 | Shared failures on specific questions point to the instrument, not the models. | Suggestive (Low) | — | Check 1 |

*Share: how much of the difference, or of the total it is part of, this cause accounts for, with its 95% interval where one was computed. Settled by: the run or check under What to run next that would confirm or rule it out.*

*How to read a hypothesis ID such as HQ1.2: H compares two models, W looks inside one model, and H0 is a null hypothesis (the starting assumption that a difference is only chance). Q, S and C stand for intelligence, speed and cost. Appendix C gives each hypothesis's evidence for and against.*

No real difference could be shown (the gap may be chance) for 2 comparisons: Orion Max vs. Vega Pro; within Orion Max's own questions.

A real difference was found for 3 pairs: Orion Max vs. Lyra Mini; Orion Max vs. Nova Lite; Orion Max vs. Zeta Prime.

The strongest hypothesis in the table: Shared failures on specific questions point to the instrument, not the models. Its grade and the evidence against it stand beside it.

### Speed

| Hypothesis | Statement | Grade | Share | Settled by |
|---|---|---|---|---|
| HS3.1 | Zeta Prime is slower than Orion Max mainly from more output (reasoning) tokens. | Probable (Moderate) | 55% (54–56%) | Run 1 |
| HS3.2 | Nova Lite is slower than Orion Max mainly from more output (reasoning) tokens. | Probable (Moderate) | 55% (35–127%) | Run 1 |
| HS3.3 | Lyra Mini is slower than Orion Max mainly from more output (reasoning) tokens. | Probable (Moderate) | 55% (53–56%) | Run 1 |
| WS3.1 | Tool rounds multiply Orion Max's time (where its time goes, not a comparison). | Probable (Moderate) | — | Run 1 |
| WS2.1 | Most of Orion Max's time is output generation, driven by reasoning tokens (where its time goes, not a comparison). | Ruled out (Not supported) | 70% (69–71%) | — |

*Share: how much of the difference, or of the total it is part of, this cause accounts for, with its 95% interval where one was computed. Settled by: the run or check under What to run next that would confirm or rule it out.*

No real difference could be shown (the gap may be chance) for 1 pair: Orion Max vs. Vega Pro.

A real difference was found for 3 pairs: Orion Max vs. Lyra Mini; Orion Max vs. Nova Lite; Orion Max vs. Zeta Prime.

The strongest hypothesis in the table: Model D is slower than Orion Max mainly from more output (reasoning) tokens. Its grade and the evidence against it stand beside it.

### Cost

| Hypothesis | Statement | Grade | Share | Settled by |
|---|---|---|---|---|
| WC1.1 | Output (with reasoning) is Orion Max's largest cost component (where its cost goes, not a comparison). | Probable (Moderate) | 69% | — |
| HC4.1 | Weak prompt caching raises Zeta Prime's cost against Orion Max. | Ruled out (Not supported) | 9% (9–9%) | — |
| HC4.2 | Weak prompt caching raises Nova Lite's cost against Orion Max. | Ruled out (Not supported) | 8% (8–9%) | — |
| HC4.3 | Weak prompt caching raises Lyra Mini's cost against Orion Max. | Ruled out (Not supported) | 8% (7–8%) | — |
| HC4.4 | Weak prompt caching raises Vega Pro's cost against Orion Max. | Ruled out (Not supported) | 7% (6–7%) | — |
| HC2.1 | Zeta Prime costs more than Orion Max mainly from its price per token. | Known from the price cards (High) | 69% (68–69%) | — |
| HC2.2 | Nova Lite costs more than Orion Max mainly from its price per token. | Known from the price cards (High) | 70% (69–70%) | — |
| HC2.3 | Lyra Mini costs more than Orion Max mainly from its price per token. | Known from the price cards (High) | 71% (70–71%) | — |
| HC2.4 | Vega Pro costs more than Orion Max mainly from its price per token. | Known from the price cards (High) | 72% (71–73%) | — |

*Share: how much of the difference, or of the total it is part of, this cause accounts for, with its 95% interval where one was computed. Settled by: the run or check under What to run next that would confirm or rule it out.*

A real difference was found for 4 pairs: Orion Max vs. Vega Pro; Orion Max vs. Lyra Mini; Orion Max vs. Nova Lite; Orion Max vs. Zeta Prime.

The strongest hypothesis in the table: Output (with reasoning) is Orion Max's largest cost component (where its cost goes, not a comparison). Its grade and the evidence against it stand beside it.

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
| Speed | Probable cause | HS3.1: Zeta Prime's more output tokens, compared with Orion Max | Probable (Moderate) because both models ran only once per suite. |
| Cost | Not established | HC2.1: Zeta Prime's higher price per token, compared with Orion Max | Known from the price cards: the differences in price per token account for the gap, which the price cards show rather than a cause the runs found; every other hypothesis on this axis was ruled out. |

> [!TIP]
> **Conclusion.** Intelligence: Not established. Speed: Probable cause. Cost: Not established.

## What to run next

In the order to make them: the Overseer chat first (C), then the benchmark (B), then model selection (M). Appendix D gives each run's launcher settings.

| # | Run | Purpose | Models | Settles | Estimate |
|---|---|---|---|---|---|
| 1 | Replicate (recommended) | M, model selection | All 5 models | H0-Q.1, H0-W.1, H0-S.1, HS3.1, HS3.2, HS3.3, WS3.1 | Not estimated |

### Checks

1. Check the rubric of Q3 against the source, and whether the tools' corpus holds the facts the question needs. *(rubric; settles HQ3.1)*

The runs are ordered chat first, then the benchmark, then model selection.

## Limitations and threats to validity

- The benchmark asks single-turn questions under one chat configuration: live chat carries the conversation before each question, and spoiler-free mode, web search and subagents are off, so a cause found here holds for that configuration only.
- It runs without the wiki text that live chat always gives the model before it answers, so a reading about retrieval or tool routing was measured under a condition live chat does not share.
- The hypotheses come from a fixed rule table over these runs; points at risk are upper bounds, and a decomposition rests on the questions both sides have figures for. The writer's own hypotheses are qualitative.
- Interval: Item sampling only.
- The graders are AI models. Each grader's provider relation to the model under test is stated under Setup and method; a grader from the model's own provider may read it more favorably.
- Costs are priced at today's catalog cards, as the comparison prices them.

The benchmark asks single questions, so longer conversations are not measured.

## Appendices

- **A. Intelligence detail:** The compared models, configuration differences, failure profiles, grader findings, panel agreement, points at risk, the index difference by dimension, the paired tests the body does not give and the per-question matrix.
- **B. Speed and cost decompositions:** Calls, latency, tokens and every component of each decomposed difference.
- **C. Hypothesis details:** Each hypothesis's evidence for and against, grade reasons and robustness checks, and the null hypotheses.
- **D. Run settings:** Every launcher setting of each run to make next.
- **E. Known facts:** The counts and the configuration facts the analysis states; its tests and decompositions are in the body and in Appendices A and B.
- **F. Reproducibility:** Run ids, dates, hashes and the pricing basis.
- **G. Question details:** The questions the writer cites, those with a critical error, confirmed or overturned, and the control items, each with its answer excerpts.
- **H. Glossary:** Every term this document uses, in plain words.

## Appendix A: Intelligence detail

### Compared models

| Model | Provider | Letter | Kind | Runs | Thinking level | Harness version | Run dates (UTC) |
|---|---|---|---|---|---|---|---|
| **Orion Max** | Northwind | — | Run | 1 | Not set | 41 | 2026-09-21 |
| Vega Pro | Southstar | A | Run | 1 | Not set | 41 | 2026-09-21 |
| Lyra Mini | Eastgate | B | Run | 1 | Not set | 41 | 2026-09-21 |
| Nova Lite | Westlake | C | Run | 1 | Not set | 41 | 2026-09-21 |
| Zeta Prime | Midland | D | Run | 1 | Not set | 41 | 2026-09-21 |

### Configuration differences

The analyzed models share every configuration key other than the model.

### Key figures

- **Intelligence:** 85 / 100 (interval 82–88), 1–2 of 5 (tied: intervals overlap); its 95% interval overlaps that of Vega Pro.
- **Speed:** Median answer time 12.0 s, 3rd of 5.
- **Cost:** $0.050 per question, 4th of 5.
- **Critical errors:** 0 of 6 answers.

### Results against peers

### Quality

| Model | Provider | Intelligence Index | 95% interval | Rank | Paired difference |
|---|---|---|---|---|---|
| **Orion Max** | Northwind | 85 | 82–88 | 1–2 (tied) | — |
| Vega Pro | Southstar | 80 | 77–83 | 1–2 (tied) | 0.0 points (-16.5 to +10.5) |
| Lyra Mini | Eastgate | 70 | 66–74 | 3–4 (tied) | +15.2 points (+9.0 to +20.8) |
| Nova Lite | Westlake | 70 | 64–76 | 3–4 (tied) | +15.8 points (+7.7 to +22.5) |
| Zeta Prime | Midland | 55 | 50–60 | 5 | +30.0 points (+19.5 to +38.0) |

*Paired difference: mean per-question difference, subject minus peer, over the questions both answered; 95% paired-bootstrap interval. It reflects question sampling only, is not adjusted for comparing several models, and is not a significance test.*

*Orion Max: Its 95% interval overlaps that of Vega Pro. This describes where the intervals overlap; it is not a significance test.*

On the same questions the paired difference with Lyra Mini excludes zero (not adjusted for several comparisons).

On the same questions the paired difference with Nova Lite excludes zero (not adjusted for several comparisons).

On the same questions the paired difference with Zeta Prime excludes zero (not adjusted for several comparisons).

No pair of models is tested for significance: with 5 models, testing every pair would flag chance differences.

### Speed

| Model | Provider | Median answer time | Rank |
|---|---|---|---|
| **Orion Max** | Northwind | 12.0 s | 3 |
| Vega Pro | Southstar | 9.0 s | 2 |
| Lyra Mini | Eastgate | 15.0 s | 4 |
| Nova Lite | Westlake | 8.0 s | 1 |
| Zeta Prime | Midland | 20.0 s | 5 |

### Cost

| Model | Provider | Cost per question | Rank |
|---|---|---|---|
| **Orion Max** | Northwind | $0.050 | 4 |
| Vega Pro | Southstar | $0.030 | 3 |
| Lyra Mini | Eastgate | $0.020 | 2 |
| Nova Lite | Westlake | $0.060 | 5 |
| Zeta Prime | Midland | $0.010 | 1 |

### Dimensions

| Dimension | Orion Max | Peer mean | Difference |
|---|---|---|---|
| Accuracy | 80 | 65 | +15 |
| Completeness | 80 | 65 | +15 |
| Conciseness | 80 | 65 | +15 |
| Readability | 80 | 65 | +15 |

### Difficulty bands (assessed)

| Difficulty band | Questions | Authored questions | Orion Max | Peer mean | Difference |
|---|---|---|---|---|---|
| Simple | 0 | 6 | — | — | — |
| Intermediate | 6 | 0 | 80 | 65 | +15 |
| Advanced | 0 | 0 | — | — | — |

### Judge-dependent pairs

Judge-dependent pairs: Not available. No run in the comparison was graded by a panel.

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

- **Panel mean absolute difference:** Not available
- **Intraclass correlation, ICC(A,1):** Not available
- **Panel disagreements:** Not available
- **Panel member A alone:** Not available
- **Panel member B alone:** Not available
- **Response-style conflict:** None

No synthesis findings were recorded.

### Tool use

- **Tool calls per question:** 0.0 (peer mean 0.0)
- **Source code share:** Not available
- **Wiki share:** Not available
- **Structured lookup share:** Not available
- **Knowledge base share:** Not available
- **Other tools share:** Not available
- **Failed tool calls:** 0
- **Calls refused by the tool budget:** 0

*Recorded success or failure describes whether a tool call executed. It does not show that the query was well chosen, that the result was relevant, or that the corpus was current.*

*Per-call arguments and results are in each run's Tool-call log until the retention sweep prunes them.*

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

*Index points, first model minus second, split by the scoring weights; the parts sum to the difference. Other is the critical-error cap and the difficulty weighting.*

### Per-question results

| Q | Topic | Assessed band | Authored | Score | Peer mean | Difference | Critical error | Refuted answer sentences | Tool calls | Model time |
|---|---|---|---|---|---|---|---|---|---|---|
| Q1 | An item question about gems | Intermediate | Simple | 92 | 70 | +22 | No | 0 | 0 | 8.2 s |
| Q2 | — | Intermediate | Simple | 88 | 67 | +21 | No | 0 | 0 | 5.7 s |
| Q3 | — | Intermediate | Simple | 30 | 39 | -9 | No | 0 | 0 | 8.8 s |
| Q4 | — | Intermediate | Simple | 90 | 66 | +24 | No | 0 | 0 | 6.1 s |
| Q5 | — | Intermediate | Simple | 85 | 71 | +14 | No | 0 | 0 | 9.4 s |
| Q6 | — | Intermediate | Simple | 95 | 77 | +18 | No | 0 | 0 | 6.5 s |

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

| Measure | Orion Max |
|---|---|
| Median answer time | 12.0 s |
| Cost per question | $0.050 |
| Input tokens per question | 4,350 |
| Output tokens per question | 970 |

### Speed differences, decomposed

| Pair | Questions | Difference per question | Component | Part | Share (95% interval) |
|---|---|---|---|---|---|
| Orion Max vs Lyra Mini | 6 | -3.0 s | Latency per call (time to first token) | -1.3 s | 42% (42–42%) |
| Orion Max vs Lyra Mini | 6 | -3.0 s | Output tokens | -1.6 s | 55% (53–56%) |
| Orion Max vs Lyra Mini | 6 | -3.0 s | Decode rate | -0.1 s | 4% (2–5%) |
| Orion Max vs Nova Lite | 6 | -4.6 s | Latency per call (time to first token) | -1.9 s | 41% (27–94%) |
| Orion Max vs Nova Lite | 6 | -4.6 s | Output tokens | -2.5 s | 55% (35–127%) |
| Orion Max vs Nova Lite | 6 | -4.6 s | Decode rate | -0.2 s | 4% (-83–28%) |
| Orion Max vs Zeta Prime | 6 | -6.0 s | Latency per call (time to first token) | -2.5 s | 42% (42–42%) |
| Orion Max vs Zeta Prime | 6 | -6.0 s | Output tokens | -3.3 s | 55% (54–56%) |
| Orion Max vs Zeta Prime | 6 | -6.0 s | Decode rate | -0.2 s | 3% (2–5%) |

*Each difference is split into parts that add up exactly to it (a standard method, the logarithmic-mean Divisia index), using only the questions both models have figures for. A share's interval comes from resampling those questions (a bootstrap interval). The difference is the first model's minus the second's. A component whose part and share are both zero is left out.*

### Cost differences, decomposed

| Pair | Questions | Difference per question | Component | Effect | Part | Share (95% interval) |
|---|---|---|---|---|---|---|
| Orion Max vs Vega Pro | 6 | -$0.0078 | Uncached input, token volume | Volume | -$0.0005 | 7% (6–7%) |
| Orion Max vs Vega Pro | 6 | -$0.0078 | Uncached input, price per token | Price | -$0.0035 | 45% (44–46%) |
| Orion Max vs Vega Pro | 6 | -$0.0078 | Visible output, token volume | Volume | -$0.0008 | 10% (10–10%) |
| Orion Max vs Vega Pro | 6 | -$0.0078 | Visible output, price per token | Price | -$0.0014 | 18% (18–18%) |
| Orion Max vs Vega Pro | 6 | -$0.0078 | Reasoning, token volume | Volume | -$0.0009 | 11% (11–12%) |
| Orion Max vs Vega Pro | 6 | -$0.0078 | Reasoning, price per token | Price | -$0.0007 | 9% (9–9%) |
| Orion Max vs Vega Pro | 6 | -$0.0078 | Token volume, all components | Volume | -$0.0022 | 28% (27–29%) |
| Orion Max vs Vega Pro | 6 | -$0.0078 | Price per token, all components | Price | -$0.0056 | 72% (71–73%) |
| Orion Max vs Lyra Mini | 6 | -$0.017 | Uncached input, token volume | Volume | -$0.0013 | 8% (7–8%) |
| Orion Max vs Lyra Mini | 6 | -$0.017 | Uncached input, price per token | Price | -$0.0074 | 44% (43–45%) |
| Orion Max vs Lyra Mini | 6 | -$0.017 | Cache reads, token volume | Volume | -$0.0001 | 0% (0–0%) |
| Orion Max vs Lyra Mini | 6 | -$0.017 | Visible output, token volume | Volume | -$0.0017 | 10% (10–10%) |
| Orion Max vs Lyra Mini | 6 | -$0.017 | Visible output, price per token | Price | -$0.0030 | 18% (17–18%) |
| Orion Max vs Lyra Mini | 6 | -$0.017 | Reasoning, token volume | Volume | -$0.0019 | 11% (11–12%) |
| Orion Max vs Lyra Mini | 6 | -$0.017 | Reasoning, price per token | Price | -$0.0015 | 9% (9–9%) |
| Orion Max vs Lyra Mini | 6 | -$0.017 | Token volume, all components | Volume | -$0.0049 | 29% (29–30%) |
| Orion Max vs Lyra Mini | 6 | -$0.017 | Price per token, all components | Price | -$0.012 | 71% (70–71%) |
| Orion Max vs Nova Lite | 6 | -$0.027 | Uncached input, token volume | Volume | -$0.0023 | 8% (8–9%) |
| Orion Max vs Nova Lite | 6 | -$0.027 | Uncached input, price per token | Price | -$0.012 | 43% (42–44%) |
| Orion Max vs Nova Lite | 6 | -$0.027 | Cache reads, token volume | Volume | -$0.0001 | 0% (0–0%) |
| Orion Max vs Nova Lite | 6 | -$0.027 | Visible output, token volume | Volume | -$0.0027 | 10% (10–10%) |
| Orion Max vs Nova Lite | 6 | -$0.027 | Visible output, price per token | Price | -$0.0047 | 17% (17–18%) |
| Orion Max vs Nova Lite | 6 | -$0.027 | Reasoning, token volume | Volume | -$0.0031 | 12% (11–12%) |
| Orion Max vs Nova Lite | 6 | -$0.027 | Reasoning, price per token | Price | -$0.0025 | 9% (9–9%) |
| Orion Max vs Nova Lite | 6 | -$0.027 | Token volume, all components | Volume | -$0.0082 | 30% (30–31%) |
| Orion Max vs Nova Lite | 6 | -$0.027 | Price per token, all components | Price | -$0.019 | 70% (69–70%) |
| Orion Max vs Zeta Prime | 6 | -$0.038 | Uncached input, token volume | Volume | -$0.0035 | 9% (9–9%) |
| Orion Max vs Zeta Prime | 6 | -$0.038 | Uncached input, price per token | Price | -$0.016 | 42% (42–43%) |
| Orion Max vs Zeta Prime | 6 | -$0.038 | Cache reads, token volume | Volume | -$0.0001 | 0% (0–0%) |
| Orion Max vs Zeta Prime | 6 | -$0.038 | Visible output, token volume | Volume | -$0.0039 | 10% (10–10%) |
| Orion Max vs Zeta Prime | 6 | -$0.038 | Visible output, price per token | Price | -$0.0065 | 17% (17–17%) |
| Orion Max vs Zeta Prime | 6 | -$0.038 | Reasoning, token volume | Volume | -$0.0045 | 12% (12–12%) |
| Orion Max vs Zeta Prime | 6 | -$0.038 | Reasoning, price per token | Price | -$0.0037 | 10% (9–10%) |
| Orion Max vs Zeta Prime | 6 | -$0.038 | Token volume, all components | Volume | -$0.012 | 31% (31–32%) |
| Orion Max vs Zeta Prime | 6 | -$0.038 | Price per token, all components | Price | -$0.026 | 69% (68–69%) |

*Each difference is split into parts that add up exactly to it (a standard method, the logarithmic-mean Divisia index), using only the questions both models have figures for. A share's interval comes from resampling those questions (a bootstrap interval). The difference is the first model's minus the second's. A component whose part and share are both zero is left out.*

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
| H0-Q.1 | Orion Max vs Vega Pro | The subject's paired-bootstrap interval against this peer includes zero. | Stands — may be chance (Low) |
| H0-W.1 | Within Orion Max | No contrast could be made. | Stands — may be chance (Low) |
| H0-Q.2 | Orion Max vs Lyra Mini | The subject's paired-bootstrap interval against this peer excludes zero (unadjusted). | Rejected — a real difference (Not supported) |
| H0-Q.3 | Orion Max vs Nova Lite | The subject's paired-bootstrap interval against this peer excludes zero (unadjusted). | Rejected — a real difference (Not supported) |
| H0-Q.4 | Orion Max vs Zeta Prime | The subject's paired-bootstrap interval against this peer excludes zero (unadjusted). | Rejected — a real difference (Not supported) |

### Speed

- **HS3.1: Zeta Prime's more output tokens, compared with Orion Max** (Zeta Prime and Orion Max; locus model; Probable (Moderate))
  - *For:* Output tokens accounts for 55% (54–56%) of the 6.0 s per-question difference in model time. Zeta Prime's reasoning share of output is 39%, Orion Max's 31%.
  - *Against:* None recorded.
  - *Grade reasons:* See Robustness.
  - *Refuted if:* The decode-rate share is larger.
  - *Robustness:* Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most); Replicates failed (Both models ran only once per suite); Configuration passed (No configuration key differs beyond the model).
- **HS3.2: Nova Lite's more output tokens, compared with Orion Max** (Nova Lite and Orion Max; locus model; Probable (Moderate))
  - *For:* Output tokens accounts for 55% (35–127%) of the 4.6 s per-question difference in model time. Nova Lite's reasoning share of output is 38%, Orion Max's 31%.
  - *Against:* None recorded.
  - *Grade reasons:* See Robustness.
  - *Refuted if:* The decode-rate share is larger.
  - *Robustness:* Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most); Replicates failed (Both models ran only once per suite); Configuration passed (No configuration key differs beyond the model).
- **HS3.3: Lyra Mini's more output tokens, compared with Orion Max** (Lyra Mini and Orion Max; locus model; Probable (Moderate))
  - *For:* Output tokens accounts for 55% (53–56%) of the 3.0 s per-question difference in model time. Lyra Mini's reasoning share of output is 36%, Orion Max's 31%.
  - *Against:* None recorded.
  - *Grade reasons:* See Robustness.
  - *Refuted if:* The decode-rate share is larger.
  - *Robustness:* Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most); Replicates failed (Both models ran only once per suite); Configuration passed (No configuration key differs beyond the model).
- **WS3.1: Orion Max's time spent on tool rounds** (Orion Max; locus model / chat; Probable (Moderate))
  - *For:* Across Orion Max's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95% interval 0.23 to 0.99, n 6).
  - *Against:* None recorded.
  - *Grade reasons:* See Robustness.
  - *Refuted if:* The ρ interval includes zero.
  - *Robustness:* Replicates failed (Orion Max ran only once per suite); Leave one question out passed (ρ stays at or above 0.5 without any one question).
- **WS2.1: Orion Max's time spent generating output** (Orion Max; locus model; Ruled out (Not supported))
  - *For:* Output generation is 70% (69–71%) of model time per question.
  - *Against:* Reasoning is 31% of Orion Max's output tokens.
  - *Grade reasons:* Its own test rules it out: reasoning is 31% of Orion Max's output tokens.
  - *Refuted if:* The reasoning share is under one half.
  - *Robustness:* Replicates failed (Orion Max ran only once per suite); Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most).

| Hypothesis | Pair | Paired result | Grade |
|---|---|---|---|
| H0-S.1 | Orion Max vs Vega Pro | No family test exists; the 95% bootstrap interval of the per-question difference includes zero. | Stands — may be chance (Low) |
| H0-S.2 | Orion Max vs Lyra Mini | No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted). | Rejected — a real difference (Not supported) |
| H0-S.3 | Orion Max vs Nova Lite | No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted). | Rejected — a real difference (Not supported) |
| H0-S.4 | Orion Max vs Zeta Prime | No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted). | Rejected — a real difference (Not supported) |

### Cost

- **WC1.1: Orion Max's output cost** (Orion Max; locus model; Probable (Moderate))
  - *For:* Visible output and reasoning are 69% of Orion Max's cost per question.
  - *Against:* None recorded.
  - *Grade reasons:* See Robustness.
  - *Refuted if:* Another component is larger.
  - *Robustness:* Replicates failed (Orion Max ran only once per suite).
- **HC4.1: Zeta Prime's weak prompt caching, compared with Orion Max** (Zeta Prime and Orion Max; locus chat / provider; Ruled out (Not supported))
  - *For:* Uncached input, token volume accounts for 9% (9–9%) of the $0.038 per-question cost difference.
  - *Against:* Zeta Prime's cache-read share is 28% against the models' median of 26%.
  - *Grade reasons:* Its own test rules it out: Zeta Prime's cache-read share is 28% against the models' median of 26%.
  - *Refuted if:* The cache-read share is comparable to the other models'.
  - *Robustness:* Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most); Replicates failed (Both models ran only once per suite); Configuration passed (No configuration key differs beyond the model).
- **HC4.2: Nova Lite's weak prompt caching, compared with Orion Max** (Nova Lite and Orion Max; locus chat / provider; Ruled out (Not supported))
  - *For:* Uncached input, token volume accounts for 8% (8–9%) of the $0.027 per-question cost difference.
  - *Against:* Nova Lite's cache-read share is 27% against the models' median of 26%.
  - *Grade reasons:* Its own test rules it out: Nova Lite's cache-read share is 27% against the models' median of 26%.
  - *Refuted if:* The cache-read share is comparable to the other models'.
  - *Robustness:* Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most); Replicates failed (Both models ran only once per suite); Configuration passed (No configuration key differs beyond the model).
- **HC4.3: Lyra Mini's weak prompt caching, compared with Orion Max** (Lyra Mini and Orion Max; locus chat / provider; Ruled out (Not supported))
  - *For:* Uncached input, token volume accounts for 8% (7–8%) of the $0.017 per-question cost difference.
  - *Against:* Lyra Mini's cache-read share is 26% against the models' median of 26%.
  - *Grade reasons:* Its own test rules it out: Lyra Mini's cache-read share is 26% against the models' median of 26%.
  - *Refuted if:* The cache-read share is comparable to the other models'.
  - *Robustness:* Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most); Replicates failed (Both models ran only once per suite); Configuration passed (No configuration key differs beyond the model).
- **HC4.4: Vega Pro's weak prompt caching, compared with Orion Max** (Vega Pro and Orion Max; locus chat / provider; Ruled out (Not supported))
  - *For:* Uncached input, token volume accounts for 7% (6–7%) of the $0.0078 per-question cost difference.
  - *Against:* Vega Pro's cache-read share is 25% against the models' median of 26%.
  - *Grade reasons:* Its own test rules it out: Vega Pro's cache-read share is 25% against the models' median of 26%.
  - *Refuted if:* The cache-read share is comparable to the other models'.
  - *Robustness:* Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most); Replicates failed (Both models ran only once per suite); Configuration passed (No configuration key differs beyond the model).
- **HC2.1: Zeta Prime's higher price per token, compared with Orion Max** (Zeta Prime and Orion Max; locus model; Known from the price cards (High))
  - *For:* Price per token, all components accounts for 69% (68–69%) of the $0.038 per-question cost difference.
  - *Against:* The volume effect is 31% (31–32%) of the difference.
  - *Grade reasons:* Every condition of the grade is met.
  - *Refuted if:* Not testable by a run: the share follows from the price cards and these runs' token counts.
  - *Robustness:* Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most); Configuration passed (No configuration key differs beyond the model).
- **HC2.2: Nova Lite's higher price per token, compared with Orion Max** (Nova Lite and Orion Max; locus model; Known from the price cards (High))
  - *For:* Price per token, all components accounts for 70% (69–70%) of the $0.027 per-question cost difference.
  - *Against:* The volume effect is 30% (30–31%) of the difference.
  - *Grade reasons:* Every condition of the grade is met.
  - *Refuted if:* Not testable by a run: the share follows from the price cards and these runs' token counts.
  - *Robustness:* Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most); Configuration passed (No configuration key differs beyond the model).
- **HC2.3: Lyra Mini's higher price per token, compared with Orion Max** (Lyra Mini and Orion Max; locus model; Known from the price cards (High))
  - *For:* Price per token, all components accounts for 71% (70–71%) of the $0.017 per-question cost difference.
  - *Against:* The volume effect is 29% (29–30%) of the difference.
  - *Grade reasons:* Every condition of the grade is met.
  - *Refuted if:* Not testable by a run: the share follows from the price cards and these runs' token counts.
  - *Robustness:* Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most); Configuration passed (No configuration key differs beyond the model).
- **HC2.4: Vega Pro's higher price per token, compared with Orion Max** (Vega Pro and Orion Max; locus model; Known from the price cards (High))
  - *For:* Price per token, all components accounts for 72% (71–73%) of the $0.0078 per-question cost difference.
  - *Against:* The volume effect is 28% (27–29%) of the difference.
  - *Grade reasons:* Every condition of the grade is met.
  - *Refuted if:* Not testable by a run: the share follows from the price cards and these runs' token counts.
  - *Robustness:* Leave one question out passed (The deciding share keeps its sign and stays the largest without the question that contributes most); Configuration passed (No configuration key differs beyond the model).

| Hypothesis | Pair | Paired result | Grade |
|---|---|---|---|
| H0-C.1 | Orion Max vs Vega Pro | No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted). | Rejected — a real difference (Not supported) |
| H0-C.2 | Orion Max vs Lyra Mini | No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted). | Rejected — a real difference (Not supported) |
| H0-C.3 | Orion Max vs Nova Lite | No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted). | Rejected — a real difference (Not supported) |
| H0-C.4 | Orion Max vs Zeta Prime | No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted). | Rejected — a real difference (Not supported) |

## Appendix D: Run settings

Each run's settings are the launcher's, copied from the analyzed runs unless its kind changes them; a setting the runs did not share is left for you to choose.

### Run 1: Replicate (M, model selection, recommended)

- **Decides:**
  - Confirms H0-Q.1 if the paired test still finds no difference with three runs per model; refutes it if a paired test of the two on the Intelligence Index is established after adjustment.
  - Confirms H0-W.1 if the contrasts still find no difference with three runs per model; refutes it if a Mann–Whitney contrast is established after the Holm adjustment.
  - Confirms H0-S.1 if the paired test still finds no difference with three runs per model; refutes it if a paired test of the two on the model time is established after adjustment.
  - Confirms HS3.1, HS3.2 and HS3.3 if the output-token share stays the largest; refutes each if the decode-rate share is larger.
  - Confirms WS3.1 if model time keeps rising with the calls per answer; refutes it if the ρ interval includes zero.
- **Predicted comparability:** Model comparison with the analyzed runs; a replicate (Tier A) of each model's own runs
- **Estimate:** Not estimated
- **Settles:** H0-Q.1, H0-W.1, H0-S.1, HS3.1, HS3.2, HS3.3, WS3.1

| Launcher field | Setting |
|---|---|
| Models | Model batch |
| Models Under Test | Orion Max, Vega Pro, Zeta Prime, Nova Lite, Lyra Mini |
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

- **8.** Orion Max answered 2 of 6 scored questions without a tool call.
- **9.** Vega Pro answered 2 of 6 scored questions without a tool call.
- **10.** Lyra Mini answered 2 of 6 scored questions without a tool call.
- **11.** Lyra Mini has 1 critical error in 6 scored answers.
- **12.** Nova Lite answered 2 of 6 scored questions without a tool call.
- **13.** Zeta Prime answered 2 of 6 scored questions without a tool call.

## Appendix F: Reproducibility

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

## Appendix G: Question details

### Q1: An item question about gems

**Question:**

> Question 201

**Rubric:**

> - Rubric 201

**Answer:**

> Answer 201

**Graders:**

- **Assessor (Gemini 3.8 Flash):** Score 92.

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
- **Points at risk:** The index points a model lost on its answers below its own median answer, each answer counted once under its first signal, such as no tool call, a defect or a critical error. They are an upper bound on what each signal costs, not an estimate of its effect.
- **Pricing basis:** The prices per token used to turn token counts into dollars: today's catalog prices, or the prices stored with each run. Costs on different bases are not comparable.
- **Replicate run:** Running the same model on the same questions again. With only one run per suite, the document cannot tell whether a result would repeat, which caps every grade at Probable.
- **Response style:** The production chat asks the model for concise answers; a run can ask for detailed ones instead. Completeness is graded under the style the run used, and only a pair of runs in both styles shows how much the style matters.
- **Robustness check:** A test of whether a hypothesis still holds when the data changes a little: one question left out, another suite, a second run, the other panel member, or another setting.
- **Runs per suite:** How many times a model answered every question of a suite. With one run per suite, the data cannot show whether a result would repeat.
- **Scoring levels:** Each dimension is graded on a fixed scale of levels, each described by the answer behavior that earns it (behaviorally anchored levels). Each level stands for a fixed score.
- **Suite:** A fixed set of benchmark questions, each with its rubric. A run answers every question of one suite once.
- **Tied rank:** When a model's 95% interval overlaps others', the data cannot fix its place, so its rank is the span of places the interval allows, such as 5–9 (tied). The span starts one place below the models whose intervals lie wholly above its own and ends one place above those whose intervals lie wholly below it.
- **Time to first token:** The seconds from sending a request to the first word of the reply. For reasoning models it is mostly the time the model spends thinking before it answers.
- **Tokens:** The pieces of text, roughly three quarters of a word each, that a model reads and writes; providers price by them. Uncached input is text read at the full price. Cache reads are input stored from an earlier call and charged less; cache writes store it. Visible output is the answer, and reasoning is the model's hidden thinking, billed as output; a provider that does not report it separately counts it inside output.
- **Tool round:** One cycle in which the model calls a tool, such as the wiki search, reads its result and continues. Each round adds a model call, with its own wait for the first token.
- **Weighted geometric mean:** The way an answer's four dimension scores are combined. Unlike a plain average, it falls sharply when any one dimension is very low, so strong dimensions cannot hide a weak one.

## Fact sheet

```json
{
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
      "isSubject": true,
      "lastRunUtc": "2026-09-21T07:08:00Z",
      "modelTimeP50Ms": 12000,
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
      "lastRunUtc": "2026-09-21T08:10:00Z",
      "modelTimeP50Ms": 9000,
      "peerLetter": "A",
      "qualityIndex": 80,
      "qualityLower": 77,
      "qualityRank": 2,
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
      "lastRunUtc": "2026-09-21T09:12:00Z",
      "modelTimeP50Ms": 15000,
      "peerLetter": "B",
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
      "lastRunUtc": "2026-09-21T10:14:00Z",
      "modelTimeP50Ms": 8000,
      "peerLetter": "C",
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
      "lastRunUtc": "2026-09-21T11:16:00Z",
      "modelTimeP50Ms": 20000,
      "peerLetter": "D",
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
      "display": "6",
      "key": "answers.scored",
      "value": 6
    },
    {
      "available": false,
      "display": "not available",
      "key": "band.advanced.difference",
      "unavailableReason": "The model or its peers lack this figure."
    },
    {
      "available": false,
      "display": "not available",
      "key": "band.advanced.peerMean",
      "unavailableReason": "No peer has this figure."
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
      "available": true,
      "display": "+15",
      "key": "band.intermediate.difference",
      "value": 15.25
    },
    {
      "available": true,
      "display": "65",
      "key": "band.intermediate.peerMean",
      "value": 64.75
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
      "unavailableReason": "The model or its peers lack this figure."
    },
    {
      "available": false,
      "display": "not available",
      "key": "band.simple.peerMean",
      "unavailableReason": "No peer has this figure."
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
      "display": "5",
      "key": "comparison.models",
      "value": 5
    },
    {
      "available": true,
      "display": "every peer has 1 run",
      "key": "comparison.peerRuns",
      "value": 1
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
      "available": true,
      "display": "4th of 5",
      "key": "cost.rank",
      "value": 4
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
      "available": true,
      "display": "+15",
      "key": "dimension.accuracy.difference",
      "value": 15.25
    },
    {
      "available": true,
      "display": "65",
      "key": "dimension.accuracy.peerMean",
      "value": 64.75
    },
    {
      "available": true,
      "display": "80",
      "key": "dimension.completeness",
      "value": 80
    },
    {
      "available": true,
      "display": "+15",
      "key": "dimension.completeness.difference",
      "value": 15.25
    },
    {
      "available": true,
      "display": "65",
      "key": "dimension.completeness.peerMean",
      "value": 64.75
    },
    {
      "available": true,
      "display": "80",
      "key": "dimension.conciseness",
      "value": 80
    },
    {
      "available": true,
      "display": "+15",
      "key": "dimension.conciseness.difference",
      "value": 15.25
    },
    {
      "available": true,
      "display": "65",
      "key": "dimension.conciseness.peerMean",
      "value": 64.75
    },
    {
      "available": true,
      "display": "80",
      "key": "dimension.readability",
      "value": 80
    },
    {
      "available": true,
      "display": "+15",
      "key": "dimension.readability.difference",
      "value": 15.25
    },
    {
      "available": true,
      "display": "65",
      "key": "dimension.readability.peerMean",
      "value": 64.75
    },
    {
      "available": true,
      "display": "0 of 6 answers",
      "key": "errors.critical",
      "value": 0
    },
    {
      "available": true,
      "display": "0% (95% interval 0%–39%); before scoring method 13, flagged rather than confirmed",
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
      "unavailableReason": "No run in the comparison was graded by a panel."
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
      "display": "$0.030",
      "key": "peer.A.cost.perQuestion",
      "value": 0.03
    },
    {
      "available": true,
      "display": "its 95% interval overlaps the subject's",
      "key": "peer.A.intervalOverlap",
      "value": true
    },
    {
      "available": true,
      "display": "0.0 points",
      "key": "peer.A.pairedDifference",
      "value": 0
    },
    {
      "available": true,
      "display": "the paired interval includes zero",
      "key": "peer.A.pairedExcludesZero",
      "value": false
    },
    {
      "available": true,
      "display": "-16.5 to +10.5",
      "key": "peer.A.pairedInterval"
    },
    {
      "available": true,
      "display": "80 / 100",
      "key": "peer.A.quality.index",
      "value": 80
    },
    {
      "available": true,
      "display": "77–83",
      "key": "peer.A.quality.interval"
    },
    {
      "available": true,
      "display": "2nd of 5",
      "key": "peer.A.quality.rank",
      "value": 2
    },
    {
      "available": true,
      "display": "1 run",
      "key": "peer.A.runs",
      "value": 1
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "peer.A.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "9.0 s",
      "key": "peer.A.speed.medianSeconds",
      "value": 9000
    },
    {
      "available": true,
      "display": "$0.020",
      "key": "peer.B.cost.perQuestion",
      "value": 0.02
    },
    {
      "available": true,
      "display": "its 95% interval does not overlap the subject's",
      "key": "peer.B.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "+15.2 points",
      "key": "peer.B.pairedDifference",
      "value": 15.166666666666666
    },
    {
      "available": true,
      "display": "the paired interval excludes zero",
      "key": "peer.B.pairedExcludesZero",
      "value": true
    },
    {
      "available": true,
      "display": "+9.0 to +20.8",
      "key": "peer.B.pairedInterval"
    },
    {
      "available": true,
      "display": "70 / 100",
      "key": "peer.B.quality.index",
      "value": 70
    },
    {
      "available": true,
      "display": "66–74",
      "key": "peer.B.quality.interval"
    },
    {
      "available": true,
      "display": "3rd of 5",
      "key": "peer.B.quality.rank",
      "value": 3
    },
    {
      "available": true,
      "display": "1 run",
      "key": "peer.B.runs",
      "value": 1
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "peer.B.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "15.0 s",
      "key": "peer.B.speed.medianSeconds",
      "value": 15000
    },
    {
      "available": true,
      "display": "$0.060",
      "key": "peer.C.cost.perQuestion",
      "value": 0.06
    },
    {
      "available": true,
      "display": "its 95% interval does not overlap the subject's",
      "key": "peer.C.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "+15.8 points",
      "key": "peer.C.pairedDifference",
      "value": 15.833333333333334
    },
    {
      "available": true,
      "display": "the paired interval excludes zero",
      "key": "peer.C.pairedExcludesZero",
      "value": true
    },
    {
      "available": true,
      "display": "+7.7 to +22.5",
      "key": "peer.C.pairedInterval"
    },
    {
      "available": true,
      "display": "70 / 100",
      "key": "peer.C.quality.index",
      "value": 70
    },
    {
      "available": true,
      "display": "64–76",
      "key": "peer.C.quality.interval"
    },
    {
      "available": true,
      "display": "3rd of 5",
      "key": "peer.C.quality.rank",
      "value": 3
    },
    {
      "available": true,
      "display": "1 run",
      "key": "peer.C.runs",
      "value": 1
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "peer.C.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "8.0 s",
      "key": "peer.C.speed.medianSeconds",
      "value": 8000
    },
    {
      "available": true,
      "display": "$0.010",
      "key": "peer.D.cost.perQuestion",
      "value": 0.01
    },
    {
      "available": true,
      "display": "its 95% interval does not overlap the subject's",
      "key": "peer.D.intervalOverlap",
      "value": false
    },
    {
      "available": true,
      "display": "+30.0 points",
      "key": "peer.D.pairedDifference",
      "value": 30
    },
    {
      "available": true,
      "display": "the paired interval excludes zero",
      "key": "peer.D.pairedExcludesZero",
      "value": true
    },
    {
      "available": true,
      "display": "+19.5 to +38.0",
      "key": "peer.D.pairedInterval"
    },
    {
      "available": true,
      "display": "55 / 100",
      "key": "peer.D.quality.index",
      "value": 55
    },
    {
      "available": true,
      "display": "50–60",
      "key": "peer.D.quality.interval"
    },
    {
      "available": true,
      "display": "5th of 5",
      "key": "peer.D.quality.rank",
      "value": 5
    },
    {
      "available": true,
      "display": "1 run",
      "key": "peer.D.runs",
      "value": 1
    },
    {
      "available": true,
      "display": "6 questions",
      "key": "peer.D.sharedQuestions",
      "value": 6
    },
    {
      "available": true,
      "display": "20.0 s",
      "key": "peer.D.speed.medianSeconds",
      "value": 20000
    },
    {
      "available": true,
      "display": "$0.125 per million tokens",
      "key": "price.cacheRead.perMillion",
      "value": 0.125
    },
    {
      "available": true,
      "display": "$1.50 per million tokens",
      "key": "price.cacheWrite.perMillion",
      "value": 1.5
    },
    {
      "available": true,
      "display": "$10.00 per million tokens",
      "key": "price.output.perMillion",
      "value": 10
    },
    {
      "available": true,
      "display": "$10.00 per million tokens",
      "key": "price.reasoning.perMillion",
      "value": 10
    },
    {
      "available": true,
      "display": "$1.25 per million tokens",
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
      "available": true,
      "display": "its 95% interval overlaps that of Model A",
      "key": "quality.intervalOverlap",
      "value": 1
    },
    {
      "available": true,
      "display": "6 points",
      "key": "quality.intervalSpan",
      "value": 6
    },
    {
      "available": true,
      "display": "80",
      "key": "quality.peerBest",
      "value": 80
    },
    {
      "available": true,
      "display": "70",
      "key": "quality.peerMedian",
      "value": 70
    },
    {
      "available": true,
      "display": "1st of 5",
      "key": "quality.rank",
      "value": 1
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
      "display": "0.2 index points",
      "key": "rca.atRisk.A.noTool",
      "value": 0.16666666666666666
    },
    {
      "available": true,
      "display": "2.8 index points",
      "key": "rca.atRisk.A.otherLow",
      "value": 2.833333333333333
    },
    {
      "available": true,
      "display": "3.0 index points below a median answer of 81",
      "key": "rca.atRisk.A.total",
      "value": 2.9999999999999996
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.B.budgetOrDefect",
      "value": 0
    },
    {
      "available": true,
      "display": "7.2 index points",
      "key": "rca.atRisk.B.criticalError",
      "value": 7.166666666666667
    },
    {
      "available": true,
      "display": "1.2 index points",
      "key": "rca.atRisk.B.noTool",
      "value": 1.1666666666666667
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.B.otherLow",
      "value": 0
    },
    {
      "available": true,
      "display": "8.3 index points below a median answer of 71",
      "key": "rca.atRisk.B.total",
      "value": 8.333333333333334
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.C.budgetOrDefect",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.C.criticalError",
      "value": 0
    },
    {
      "available": true,
      "display": "6.3 index points",
      "key": "rca.atRisk.C.noTool",
      "value": 6.333333333333333
    },
    {
      "available": true,
      "display": "2.0 index points",
      "key": "rca.atRisk.C.otherLow",
      "value": 2
    },
    {
      "available": true,
      "display": "8.3 index points below a median answer of 70",
      "key": "rca.atRisk.C.total",
      "value": 8.333333333333332
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
      "display": "0.0 index points",
      "key": "rca.atRisk.D.noTool",
      "value": 0
    },
    {
      "available": true,
      "display": "5.6 index points",
      "key": "rca.atRisk.D.otherLow",
      "value": 5.583333333333333
    },
    {
      "available": true,
      "display": "5.6 index points below a median answer of 54",
      "key": "rca.atRisk.D.total",
      "value": 5.583333333333333
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.subject.budgetOrDefect",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 index points",
      "key": "rca.atRisk.subject.criticalError",
      "value": 0
    },
    {
      "available": true,
      "display": "9.8 index points",
      "key": "rca.atRisk.subject.noTool",
      "value": 9.833333333333334
    },
    {
      "available": true,
      "display": "0.8 index points",
      "key": "rca.atRisk.subject.otherLow",
      "value": 0.8333333333333333
    },
    {
      "available": true,
      "display": "10.7 index points below a median answer of 89",
      "key": "rca.atRisk.subject.total",
      "value": 10.666666666666668
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
      "available": true,
      "display": "HS3.1",
      "key": "rca.axis.speed.best",
      "value": "HS3.1"
    },
    {
      "available": true,
      "display": "Probable cause",
      "key": "rca.axis.speed.status",
      "value": "ProbableCause"
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
      "key": "rca.cost.subject.A.all.price.interval"
    },
    {
      "available": true,
      "display": "72%",
      "key": "rca.cost.subject.A.all.price.share",
      "value": 0.71922009092776
    },
    {
      "available": true,
      "display": "27–29%",
      "key": "rca.cost.subject.A.all.volume.interval"
    },
    {
      "available": true,
      "display": "28%",
      "key": "rca.cost.subject.A.all.volume.share",
      "value": 0.28077990907223965
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.A.cacheRead.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.A.cacheRead.price.share",
      "value": 5.65413520631882E-18
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.A.cacheRead.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.A.cacheRead.volume.share",
      "value": 0.0032092426187419684
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.A.cacheWrite.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.A.cacheWrite.price.share",
      "value": -0
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.A.cacheWrite.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.A.cacheWrite.volume.share",
      "value": -0
    },
    {
      "available": true,
      "display": "18–18%",
      "key": "rca.cost.subject.A.output.price.interval"
    },
    {
      "available": true,
      "display": "18%",
      "key": "rca.cost.subject.A.output.price.share",
      "value": 0.18112542291023093
    },
    {
      "available": true,
      "display": "10–10%",
      "key": "rca.cost.subject.A.output.volume.interval"
    },
    {
      "available": true,
      "display": "10%",
      "key": "rca.cost.subject.A.output.volume.share",
      "value": 0.09872053344406923
    },
    {
      "available": true,
      "display": "9–9%",
      "key": "rca.cost.subject.A.reasoning.price.interval"
    },
    {
      "available": true,
      "display": "9%",
      "key": "rca.cost.subject.A.reasoning.price.share",
      "value": 0.08719899521942158
    },
    {
      "available": true,
      "display": "11–12%",
      "key": "rca.cost.subject.A.reasoning.volume.interval"
    },
    {
      "available": true,
      "display": "11%",
      "key": "rca.cost.subject.A.reasoning.volume.share",
      "value": 0.11305774419007787
    },
    {
      "available": true,
      "display": "-$0.0078 per question",
      "key": "rca.cost.subject.A.total",
      "value": -0.007790000000000002
    },
    {
      "available": true,
      "display": "44–46%",
      "key": "rca.cost.subject.A.uncachedInput.price.interval"
    },
    {
      "available": true,
      "display": "45%",
      "key": "rca.cost.subject.A.uncachedInput.price.share",
      "value": 0.45089567279810744
    },
    {
      "available": true,
      "display": "6–7%",
      "key": "rca.cost.subject.A.uncachedInput.volume.interval"
    },
    {
      "available": true,
      "display": "7%",
      "key": "rca.cost.subject.A.uncachedInput.volume.share",
      "value": 0.06579238881935058
    },
    {
      "available": true,
      "display": "70–71%",
      "key": "rca.cost.subject.B.all.price.interval"
    },
    {
      "available": true,
      "display": "71%",
      "key": "rca.cost.subject.B.all.price.share",
      "value": 0.7067857366013377
    },
    {
      "available": true,
      "display": "29–30%",
      "key": "rca.cost.subject.B.all.volume.interval"
    },
    {
      "available": true,
      "display": "29%",
      "key": "rca.cost.subject.B.all.volume.share",
      "value": 0.29321426339866247
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.B.cacheRead.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.B.cacheRead.price.share",
      "value": -7.268935900156093E-18
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.B.cacheRead.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.B.cacheRead.volume.share",
      "value": 0.0029797377830750966
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.B.cacheWrite.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.B.cacheWrite.price.share",
      "value": -0
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.B.cacheWrite.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.B.cacheWrite.volume.share",
      "value": -0
    },
    {
      "available": true,
      "display": "17–18%",
      "key": "rca.cost.subject.B.output.price.interval"
    },
    {
      "available": true,
      "display": "18%",
      "key": "rca.cost.subject.B.output.price.share",
      "value": 0.17680749428237597
    },
    {
      "available": true,
      "display": "10–10%",
      "key": "rca.cost.subject.B.output.volume.interval"
    },
    {
      "available": true,
      "display": "10%",
      "key": "rca.cost.subject.B.output.volume.share",
      "value": 0.09971217198699239
    },
    {
      "available": true,
      "display": "9–9%",
      "key": "rca.cost.subject.B.reasoning.price.interval"
    },
    {
      "available": true,
      "display": "9%",
      "key": "rca.cost.subject.B.reasoning.price.share",
      "value": 0.09029630352104531
    },
    {
      "available": true,
      "display": "11–12%",
      "key": "rca.cost.subject.B.reasoning.volume.interval"
    },
    {
      "available": true,
      "display": "11%",
      "key": "rca.cost.subject.B.reasoning.volume.share",
      "value": 0.11470965595452089
    },
    {
      "available": true,
      "display": "-$0.017 per question",
      "key": "rca.cost.subject.B.total",
      "value": -0.01678
    },
    {
      "available": true,
      "display": "43–45%",
      "key": "rca.cost.subject.B.uncachedInput.price.interval"
    },
    {
      "available": true,
      "display": "44%",
      "key": "rca.cost.subject.B.uncachedInput.price.share",
      "value": 0.4396819387979164
    },
    {
      "available": true,
      "display": "7–8%",
      "key": "rca.cost.subject.B.uncachedInput.volume.interval"
    },
    {
      "available": true,
      "display": "8%",
      "key": "rca.cost.subject.B.uncachedInput.volume.share",
      "value": 0.07581269767407409
    },
    {
      "available": true,
      "display": "69–70%",
      "key": "rca.cost.subject.C.all.price.interval"
    },
    {
      "available": true,
      "display": "70%",
      "key": "rca.cost.subject.C.all.price.share",
      "value": 0.6968068304832366
    },
    {
      "available": true,
      "display": "30–31%",
      "key": "rca.cost.subject.C.all.volume.interval"
    },
    {
      "available": true,
      "display": "30%",
      "key": "rca.cost.subject.C.all.volume.share",
      "value": 0.3031931695167636
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.C.cacheRead.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.C.cacheRead.price.share",
      "value": -4.522534089900603E-18
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.C.cacheRead.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.C.cacheRead.volume.share",
      "value": 0.0027808676307007835
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.C.cacheWrite.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.C.cacheWrite.price.share",
      "value": -0
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.C.cacheWrite.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.C.cacheWrite.volume.share",
      "value": -0
    },
    {
      "available": true,
      "display": "17–18%",
      "key": "rca.cost.subject.C.output.price.interval"
    },
    {
      "available": true,
      "display": "17%",
      "key": "rca.cost.subject.C.output.price.share",
      "value": 0.1731783963649792
    },
    {
      "available": true,
      "display": "10–10%",
      "key": "rca.cost.subject.C.output.volume.interval"
    },
    {
      "available": true,
      "display": "10%",
      "key": "rca.cost.subject.C.output.volume.share",
      "value": 0.10045897849597754
    },
    {
      "available": true,
      "display": "9–9%",
      "key": "rca.cost.subject.C.reasoning.price.interval"
    },
    {
      "available": true,
      "display": "9%",
      "key": "rca.cost.subject.C.reasoning.price.share",
      "value": 0.09291798670837466
    },
    {
      "available": true,
      "display": "11–12%",
      "key": "rca.cost.subject.C.reasoning.volume.interval"
    },
    {
      "available": true,
      "display": "12%",
      "key": "rca.cost.subject.C.reasoning.volume.share",
      "value": 0.11620325912032395
    },
    {
      "available": true,
      "display": "-$0.027 per question",
      "key": "rca.cost.subject.C.total",
      "value": -0.026969999999999997
    },
    {
      "available": true,
      "display": "42–44%",
      "key": "rca.cost.subject.C.uncachedInput.price.interval"
    },
    {
      "available": true,
      "display": "43%",
      "key": "rca.cost.subject.C.uncachedInput.price.share",
      "value": 0.4307104474098827
    },
    {
      "available": true,
      "display": "8–9%",
      "key": "rca.cost.subject.C.uncachedInput.volume.interval"
    },
    {
      "available": true,
      "display": "8%",
      "key": "rca.cost.subject.C.uncachedInput.volume.share",
      "value": 0.08375006426976137
    },
    {
      "available": true,
      "display": "68–69%",
      "key": "rca.cost.subject.D.all.price.interval"
    },
    {
      "available": true,
      "display": "69%",
      "key": "rca.cost.subject.D.all.price.share",
      "value": 0.6885105330251258
    },
    {
      "available": true,
      "display": "31–32%",
      "key": "rca.cost.subject.D.all.volume.interval"
    },
    {
      "available": true,
      "display": "31%",
      "key": "rca.cost.subject.D.all.volume.share",
      "value": 0.31148946697487445
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.D.cacheRead.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.D.cacheRead.price.share",
      "value": -1.4131936554816275E-18
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.D.cacheRead.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.D.cacheRead.volume.share",
      "value": 0.0026068821689259657
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.D.cacheWrite.price.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.D.cacheWrite.price.share",
      "value": -0
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.cost.subject.D.cacheWrite.volume.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.cost.subject.D.cacheWrite.volume.share",
      "value": -0
    },
    {
      "available": true,
      "display": "17–17%",
      "key": "rca.cost.subject.D.output.price.interval"
    },
    {
      "available": true,
      "display": "17%",
      "key": "rca.cost.subject.D.output.price.share",
      "value": 0.17007808201744268
    },
    {
      "available": true,
      "display": "10–10%",
      "key": "rca.cost.subject.D.output.volume.interval"
    },
    {
      "available": true,
      "display": "10%",
      "key": "rca.cost.subject.D.output.volume.share",
      "value": 0.10103766355085769
    },
    {
      "available": true,
      "display": "9–10%",
      "key": "rca.cost.subject.D.reasoning.price.interval"
    },
    {
      "available": true,
      "display": "10%",
      "key": "rca.cost.subject.D.reasoning.price.share",
      "value": 0.09517595976166772
    },
    {
      "available": true,
      "display": "12–12%",
      "key": "rca.cost.subject.D.reasoning.volume.interval"
    },
    {
      "available": true,
      "display": "12%",
      "key": "rca.cost.subject.D.reasoning.volume.share",
      "value": 0.117545625222691
    },
    {
      "available": true,
      "display": "-$0.038 per question",
      "key": "rca.cost.subject.D.total",
      "value": -0.03835999999999999
    },
    {
      "available": true,
      "display": "42–43%",
      "key": "rca.cost.subject.D.uncachedInput.price.interval"
    },
    {
      "available": true,
      "display": "42%",
      "key": "rca.cost.subject.D.uncachedInput.price.share",
      "value": 0.4232564912460154
    },
    {
      "available": true,
      "display": "9–9%",
      "key": "rca.cost.subject.D.uncachedInput.volume.interval"
    },
    {
      "available": true,
      "display": "9%",
      "key": "rca.cost.subject.D.uncachedInput.volume.share",
      "value": 0.09029929603239982
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
      "display": "no cost difference between Orion Max and Model A",
      "key": "rca.hypothesis.H0-C.1.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-C.1.locus"
    },
    {
      "available": true,
      "display": "Orion Max and Model A",
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
      "display": "The cost per question difference between Orion Max and Model A is within sampling noise.",
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
      "display": "no cost difference between Orion Max and Model B",
      "key": "rca.hypothesis.H0-C.2.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-C.2.locus"
    },
    {
      "available": true,
      "display": "Orion Max and Model B",
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
      "display": "The cost per question difference between Orion Max and Model B is within sampling noise.",
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
      "display": "no cost difference between Orion Max and Model C",
      "key": "rca.hypothesis.H0-C.3.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-C.3.locus"
    },
    {
      "available": true,
      "display": "Orion Max and Model C",
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
      "display": "The cost per question difference between Orion Max and Model C is within sampling noise.",
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
      "display": "no cost difference between Orion Max and Model D",
      "key": "rca.hypothesis.H0-C.4.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-C.4.locus"
    },
    {
      "available": true,
      "display": "Orion Max and Model D",
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
      "display": "The cost per question difference between Orion Max and Model D is within sampling noise.",
      "key": "rca.hypothesis.H0-C.4.statement"
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
      "display": "The subject's paired-bootstrap interval against this peer includes zero. The two 95% intervals overlap.",
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
      "display": "no intelligence difference between Orion Max and Model A",
      "key": "rca.hypothesis.H0-Q.1.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-Q.1.locus"
    },
    {
      "available": true,
      "display": "Orion Max and Model A",
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
      "display": "The Intelligence Index difference between Orion Max and Model A is within sampling noise.",
      "key": "rca.hypothesis.H0-Q.1.statement"
    },
    {
      "available": true,
      "display": "The subject's paired-bootstrap interval against this peer excludes zero (unadjusted). The two 95% intervals do not overlap.",
      "key": "rca.hypothesis.H0-Q.2.against"
    },
    {
      "available": true,
      "display": "Intelligence",
      "key": "rca.hypothesis.H0-Q.2.axis"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-Q.2.for"
    },
    {
      "available": true,
      "display": "Rejected — a real difference (Not supported)",
      "key": "rca.hypothesis.H0-Q.2.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: the subject's paired-bootstrap interval against this peer excludes zero (unadjusted)",
      "key": "rca.hypothesis.H0-Q.2.gradeReasons"
    },
    {
      "available": true,
      "display": "no intelligence difference between Orion Max and Model B",
      "key": "rca.hypothesis.H0-Q.2.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-Q.2.locus"
    },
    {
      "available": true,
      "display": "Orion Max and Model B",
      "key": "rca.hypothesis.H0-Q.2.models"
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
      "display": "The Intelligence Index difference between Orion Max and Model B is within sampling noise.",
      "key": "rca.hypothesis.H0-Q.2.statement"
    },
    {
      "available": true,
      "display": "The subject's paired-bootstrap interval against this peer excludes zero (unadjusted). The two 95% intervals do not overlap.",
      "key": "rca.hypothesis.H0-Q.3.against"
    },
    {
      "available": true,
      "display": "Intelligence",
      "key": "rca.hypothesis.H0-Q.3.axis"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-Q.3.for"
    },
    {
      "available": true,
      "display": "Rejected — a real difference (Not supported)",
      "key": "rca.hypothesis.H0-Q.3.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: the subject's paired-bootstrap interval against this peer excludes zero (unadjusted)",
      "key": "rca.hypothesis.H0-Q.3.gradeReasons"
    },
    {
      "available": true,
      "display": "no intelligence difference between Orion Max and Model C",
      "key": "rca.hypothesis.H0-Q.3.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-Q.3.locus"
    },
    {
      "available": true,
      "display": "Orion Max and Model C",
      "key": "rca.hypothesis.H0-Q.3.models"
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
      "display": "The Intelligence Index difference between Orion Max and Model C is within sampling noise.",
      "key": "rca.hypothesis.H0-Q.3.statement"
    },
    {
      "available": true,
      "display": "The subject's paired-bootstrap interval against this peer excludes zero (unadjusted). The two 95% intervals do not overlap.",
      "key": "rca.hypothesis.H0-Q.4.against"
    },
    {
      "available": true,
      "display": "Intelligence",
      "key": "rca.hypothesis.H0-Q.4.axis"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-Q.4.for"
    },
    {
      "available": true,
      "display": "Rejected — a real difference (Not supported)",
      "key": "rca.hypothesis.H0-Q.4.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: the subject's paired-bootstrap interval against this peer excludes zero (unadjusted)",
      "key": "rca.hypothesis.H0-Q.4.gradeReasons"
    },
    {
      "available": true,
      "display": "no intelligence difference between Orion Max and Model D",
      "key": "rca.hypothesis.H0-Q.4.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-Q.4.locus"
    },
    {
      "available": true,
      "display": "Orion Max and Model D",
      "key": "rca.hypothesis.H0-Q.4.models"
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
      "display": "The Intelligence Index difference between Orion Max and Model D is within sampling noise.",
      "key": "rca.hypothesis.H0-Q.4.statement"
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
      "display": "No family test exists; the 95% bootstrap interval of the per-question difference includes zero.",
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
      "display": "no speed difference between Orion Max and Model A",
      "key": "rca.hypothesis.H0-S.1.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-S.1.locus"
    },
    {
      "available": true,
      "display": "Orion Max and Model A",
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
      "display": "The model time difference between Orion Max and Model A is within sampling noise.",
      "key": "rca.hypothesis.H0-S.1.statement"
    },
    {
      "available": true,
      "display": "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted).",
      "key": "rca.hypothesis.H0-S.2.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.H0-S.2.axis"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-S.2.for"
    },
    {
      "available": true,
      "display": "Rejected — a real difference (Not supported)",
      "key": "rca.hypothesis.H0-S.2.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)",
      "key": "rca.hypothesis.H0-S.2.gradeReasons"
    },
    {
      "available": true,
      "display": "no speed difference between Orion Max and Model B",
      "key": "rca.hypothesis.H0-S.2.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-S.2.locus"
    },
    {
      "available": true,
      "display": "Orion Max and Model B",
      "key": "rca.hypothesis.H0-S.2.models"
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
      "display": "The model time difference between Orion Max and Model B is within sampling noise.",
      "key": "rca.hypothesis.H0-S.2.statement"
    },
    {
      "available": true,
      "display": "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted).",
      "key": "rca.hypothesis.H0-S.3.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.H0-S.3.axis"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-S.3.for"
    },
    {
      "available": true,
      "display": "Rejected — a real difference (Not supported)",
      "key": "rca.hypothesis.H0-S.3.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)",
      "key": "rca.hypothesis.H0-S.3.gradeReasons"
    },
    {
      "available": true,
      "display": "no speed difference between Orion Max and Model C",
      "key": "rca.hypothesis.H0-S.3.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-S.3.locus"
    },
    {
      "available": true,
      "display": "Orion Max and Model C",
      "key": "rca.hypothesis.H0-S.3.models"
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
      "display": "The model time difference between Orion Max and Model C is within sampling noise.",
      "key": "rca.hypothesis.H0-S.3.statement"
    },
    {
      "available": true,
      "display": "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted).",
      "key": "rca.hypothesis.H0-S.4.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.H0-S.4.axis"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.H0-S.4.for"
    },
    {
      "available": true,
      "display": "Rejected — a real difference (Not supported)",
      "key": "rca.hypothesis.H0-S.4.grade",
      "value": "NotSupported"
    },
    {
      "available": true,
      "display": "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)",
      "key": "rca.hypothesis.H0-S.4.gradeReasons"
    },
    {
      "available": true,
      "display": "no speed difference between Orion Max and Model D",
      "key": "rca.hypothesis.H0-S.4.label"
    },
    {
      "available": true,
      "display": "—",
      "key": "rca.hypothesis.H0-S.4.locus"
    },
    {
      "available": true,
      "display": "Orion Max and Model D",
      "key": "rca.hypothesis.H0-S.4.models"
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
      "display": "The model time difference between Orion Max and Model D is within sampling noise.",
      "key": "rca.hypothesis.H0-S.4.statement"
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
      "display": "no difference within Orion Max's own questions",
      "key": "rca.hypothesis.H0-W.1.label"
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
      "display": "The contrasts between groups of Orion Max's own questions are within sampling noise.",
      "key": "rca.hypothesis.H0-W.1.statement"
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
      "display": "Model D's higher price per token, compared with Orion Max",
      "key": "rca.hypothesis.HC2.1.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.HC2.1.locus"
    },
    {
      "available": true,
      "display": "Model D and Orion Max",
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
      "display": "Model D costs more than Orion Max mainly from its price per token.",
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
      "display": "Model C's higher price per token, compared with Orion Max",
      "key": "rca.hypothesis.HC2.2.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.HC2.2.locus"
    },
    {
      "available": true,
      "display": "Model C and Orion Max",
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
      "display": "Model C costs more than Orion Max mainly from its price per token.",
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
      "display": "Model B's higher price per token, compared with Orion Max",
      "key": "rca.hypothesis.HC2.3.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.HC2.3.locus"
    },
    {
      "available": true,
      "display": "Model B and Orion Max",
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
      "display": "Model B costs more than Orion Max mainly from its price per token.",
      "key": "rca.hypothesis.HC2.3.statement"
    },
    {
      "available": true,
      "display": "The volume effect is 28% (27–29%) of the difference.",
      "key": "rca.hypothesis.HC2.4.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.HC2.4.axis"
    },
    {
      "available": true,
      "display": "Price per token, all components accounts for 72% (71–73%) of the $0.0078 per-question cost difference.",
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
      "display": "71–73%",
      "key": "rca.hypothesis.HC2.4.interval"
    },
    {
      "available": true,
      "display": "Model A's higher price per token, compared with Orion Max",
      "key": "rca.hypothesis.HC2.4.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.HC2.4.locus"
    },
    {
      "available": true,
      "display": "Model A and Orion Max",
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
      "display": "72%",
      "key": "rca.hypothesis.HC2.4.share",
      "value": 0.71922009092776
    },
    {
      "available": true,
      "display": "Model A costs more than Orion Max mainly from its price per token.",
      "key": "rca.hypothesis.HC2.4.statement"
    },
    {
      "available": true,
      "display": "Model D's cache-read share is 28% against the models' median of 26%.",
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
      "display": "its own test rules it out: Model D's cache-read share is 28% against the models' median of 26%",
      "key": "rca.hypothesis.HC4.1.gradeReasons"
    },
    {
      "available": true,
      "display": "9–9%",
      "key": "rca.hypothesis.HC4.1.interval"
    },
    {
      "available": true,
      "display": "Model D's weak prompt caching, compared with Orion Max",
      "key": "rca.hypothesis.HC4.1.label"
    },
    {
      "available": true,
      "display": "chat / provider",
      "key": "rca.hypothesis.HC4.1.locus"
    },
    {
      "available": true,
      "display": "Model D and Orion Max",
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
      "display": "Weak prompt caching raises Model D's cost against Orion Max.",
      "key": "rca.hypothesis.HC4.1.statement"
    },
    {
      "available": true,
      "display": "Model C's cache-read share is 27% against the models' median of 26%.",
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
      "display": "its own test rules it out: Model C's cache-read share is 27% against the models' median of 26%",
      "key": "rca.hypothesis.HC4.2.gradeReasons"
    },
    {
      "available": true,
      "display": "8–9%",
      "key": "rca.hypothesis.HC4.2.interval"
    },
    {
      "available": true,
      "display": "Model C's weak prompt caching, compared with Orion Max",
      "key": "rca.hypothesis.HC4.2.label"
    },
    {
      "available": true,
      "display": "chat / provider",
      "key": "rca.hypothesis.HC4.2.locus"
    },
    {
      "available": true,
      "display": "Model C and Orion Max",
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
      "display": "Weak prompt caching raises Model C's cost against Orion Max.",
      "key": "rca.hypothesis.HC4.2.statement"
    },
    {
      "available": true,
      "display": "Model B's cache-read share is 26% against the models' median of 26%.",
      "key": "rca.hypothesis.HC4.3.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.HC4.3.axis"
    },
    {
      "available": true,
      "display": "Uncached input, token volume accounts for 8% (7–8%) of the $0.017 per-question cost difference.",
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
      "display": "its own test rules it out: Model B's cache-read share is 26% against the models' median of 26%",
      "key": "rca.hypothesis.HC4.3.gradeReasons"
    },
    {
      "available": true,
      "display": "7–8%",
      "key": "rca.hypothesis.HC4.3.interval"
    },
    {
      "available": true,
      "display": "Model B's weak prompt caching, compared with Orion Max",
      "key": "rca.hypothesis.HC4.3.label"
    },
    {
      "available": true,
      "display": "chat / provider",
      "key": "rca.hypothesis.HC4.3.locus"
    },
    {
      "available": true,
      "display": "Model B and Orion Max",
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
      "display": "8%",
      "key": "rca.hypothesis.HC4.3.share",
      "value": 0.07581269767407409
    },
    {
      "available": true,
      "display": "Weak prompt caching raises Model B's cost against Orion Max.",
      "key": "rca.hypothesis.HC4.3.statement"
    },
    {
      "available": true,
      "display": "Model A's cache-read share is 25% against the models' median of 26%.",
      "key": "rca.hypothesis.HC4.4.against"
    },
    {
      "available": true,
      "display": "Cost",
      "key": "rca.hypothesis.HC4.4.axis"
    },
    {
      "available": true,
      "display": "Uncached input, token volume accounts for 7% (6–7%) of the $0.0078 per-question cost difference.",
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
      "display": "its own test rules it out: Model A's cache-read share is 25% against the models' median of 26%",
      "key": "rca.hypothesis.HC4.4.gradeReasons"
    },
    {
      "available": true,
      "display": "6–7%",
      "key": "rca.hypothesis.HC4.4.interval"
    },
    {
      "available": true,
      "display": "Model A's weak prompt caching, compared with Orion Max",
      "key": "rca.hypothesis.HC4.4.label"
    },
    {
      "available": true,
      "display": "chat / provider",
      "key": "rca.hypothesis.HC4.4.locus"
    },
    {
      "available": true,
      "display": "Model A and Orion Max",
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
      "display": "7%",
      "key": "rca.hypothesis.HC4.4.share",
      "value": 0.06579238881935058
    },
    {
      "available": true,
      "display": "Weak prompt caching raises Model A's cost against Orion Max.",
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
      "display": "Orion Max, Model A, Model B, Model C and Model D",
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
      "key": "rca.hypothesis.HS3.1.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.HS3.1.axis"
    },
    {
      "available": true,
      "display": "Output tokens accounts for 55% (54–56%) of the 6.0 s per-question difference in model time. Model D's reasoning share of output is 39%, Orion Max's 31%.",
      "key": "rca.hypothesis.HS3.1.for"
    },
    {
      "available": true,
      "display": "Moderate",
      "key": "rca.hypothesis.HS3.1.grade",
      "value": "Moderate"
    },
    {
      "available": true,
      "display": "both models ran only once per suite",
      "key": "rca.hypothesis.HS3.1.gradeReasons"
    },
    {
      "available": true,
      "display": "54–56%",
      "key": "rca.hypothesis.HS3.1.interval"
    },
    {
      "available": true,
      "display": "Model D's more output tokens, compared with Orion Max",
      "key": "rca.hypothesis.HS3.1.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.HS3.1.locus"
    },
    {
      "available": true,
      "display": "Model D and Orion Max",
      "key": "rca.hypothesis.HS3.1.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.HS3.1.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "HS3.1",
      "key": "rca.hypothesis.HS3.1.ref"
    },
    {
      "available": true,
      "display": "The decode-rate share is larger.",
      "key": "rca.hypothesis.HS3.1.refutedIf"
    },
    {
      "available": true,
      "display": "55%",
      "key": "rca.hypothesis.HS3.1.share",
      "value": 0.5511151205368906
    },
    {
      "available": true,
      "display": "Model D is slower than Orion Max mainly from more output (reasoning) tokens.",
      "key": "rca.hypothesis.HS3.1.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.HS3.2.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.HS3.2.axis"
    },
    {
      "available": true,
      "display": "Output tokens accounts for 55% (35–127%) of the 4.6 s per-question difference in model time. Model C's reasoning share of output is 38%, Orion Max's 31%.",
      "key": "rca.hypothesis.HS3.2.for"
    },
    {
      "available": true,
      "display": "Moderate",
      "key": "rca.hypothesis.HS3.2.grade",
      "value": "Moderate"
    },
    {
      "available": true,
      "display": "both models ran only once per suite",
      "key": "rca.hypothesis.HS3.2.gradeReasons"
    },
    {
      "available": true,
      "display": "35–127%",
      "key": "rca.hypothesis.HS3.2.interval"
    },
    {
      "available": true,
      "display": "Model C's more output tokens, compared with Orion Max",
      "key": "rca.hypothesis.HS3.2.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.HS3.2.locus"
    },
    {
      "available": true,
      "display": "Model C and Orion Max",
      "key": "rca.hypothesis.HS3.2.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.HS3.2.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "HS3.2",
      "key": "rca.hypothesis.HS3.2.ref"
    },
    {
      "available": true,
      "display": "The decode-rate share is larger.",
      "key": "rca.hypothesis.HS3.2.refutedIf"
    },
    {
      "available": true,
      "display": "55%",
      "key": "rca.hypothesis.HS3.2.share",
      "value": 0.5452041367539779
    },
    {
      "available": true,
      "display": "Model C is slower than Orion Max mainly from more output (reasoning) tokens.",
      "key": "rca.hypothesis.HS3.2.statement"
    },
    {
      "available": true,
      "display": "none recorded",
      "key": "rca.hypothesis.HS3.3.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.HS3.3.axis"
    },
    {
      "available": true,
      "display": "Output tokens accounts for 55% (53–56%) of the 3.0 s per-question difference in model time. Model B's reasoning share of output is 36%, Orion Max's 31%.",
      "key": "rca.hypothesis.HS3.3.for"
    },
    {
      "available": true,
      "display": "Moderate",
      "key": "rca.hypothesis.HS3.3.grade",
      "value": "Moderate"
    },
    {
      "available": true,
      "display": "both models ran only once per suite",
      "key": "rca.hypothesis.HS3.3.gradeReasons"
    },
    {
      "available": true,
      "display": "53–56%",
      "key": "rca.hypothesis.HS3.3.interval"
    },
    {
      "available": true,
      "display": "Model B's more output tokens, compared with Orion Max",
      "key": "rca.hypothesis.HS3.3.label"
    },
    {
      "available": true,
      "display": "model",
      "key": "rca.hypothesis.HS3.3.locus"
    },
    {
      "available": true,
      "display": "Model B and Orion Max",
      "key": "rca.hypothesis.HS3.3.models"
    },
    {
      "available": true,
      "display": "next run 1",
      "key": "rca.hypothesis.HS3.3.nextRun",
      "value": 1
    },
    {
      "available": true,
      "display": "HS3.3",
      "key": "rca.hypothesis.HS3.3.ref"
    },
    {
      "available": true,
      "display": "The decode-rate share is larger.",
      "key": "rca.hypothesis.HS3.3.refutedIf"
    },
    {
      "available": true,
      "display": "55%",
      "key": "rca.hypothesis.HS3.3.share",
      "value": 0.5477737497394417
    },
    {
      "available": true,
      "display": "Model B is slower than Orion Max mainly from more output (reasoning) tokens.",
      "key": "rca.hypothesis.HS3.3.statement"
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
      "display": "Visible output and reasoning are 69% of Orion Max's cost per question.",
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
      "display": "Orion Max ran only once per suite",
      "key": "rca.hypothesis.WC1.1.gradeReasons"
    },
    {
      "available": true,
      "display": "Orion Max's output cost",
      "key": "rca.hypothesis.WC1.1.label"
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
      "display": "69%",
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
      "display": "Reasoning is 31% of Orion Max's output tokens.",
      "key": "rca.hypothesis.WS2.1.against"
    },
    {
      "available": true,
      "display": "Speed",
      "key": "rca.hypothesis.WS2.1.axis"
    },
    {
      "available": true,
      "display": "Output generation is 70% (69–71%) of model time per question.",
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
      "display": "its own test rules it out: reasoning is 31% of Orion Max's output tokens",
      "key": "rca.hypothesis.WS2.1.gradeReasons"
    },
    {
      "available": true,
      "display": "69–71%",
      "key": "rca.hypothesis.WS2.1.interval"
    },
    {
      "available": true,
      "display": "Orion Max's time spent generating output",
      "key": "rca.hypothesis.WS2.1.label"
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
      "display": "70%",
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
      "display": "Across Orion Max's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95% interval 0.23 to 0.99, n 6).",
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
      "display": "Orion Max ran only once per suite",
      "key": "rca.hypothesis.WS3.1.gradeReasons"
    },
    {
      "available": true,
      "display": "Orion Max's time spent on tool rounds",
      "key": "rca.hypothesis.WS3.1.label"
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
      "display": "Tool rounds multiply Orion Max's time (where its time goes, not a comparison).",
      "key": "rca.hypothesis.WS3.1.statement"
    },
    {
      "available": true,
      "display": "Output tokens accounts for 55% (53–56%) of the per-question time difference between Orion Max and Model B.",
      "key": "rca.known.1"
    },
    {
      "available": true,
      "display": "Model B answered 2 of 6 scored questions without a tool call.",
      "key": "rca.known.10"
    },
    {
      "available": true,
      "display": "Model B has 1 critical error in 6 scored answers.",
      "key": "rca.known.11"
    },
    {
      "available": true,
      "display": "Model C answered 2 of 6 scored questions without a tool call.",
      "key": "rca.known.12"
    },
    {
      "available": true,
      "display": "Model D answered 2 of 6 scored questions without a tool call.",
      "key": "rca.known.13"
    },
    {
      "available": true,
      "display": "Output tokens accounts for 55% (35–127%) of the per-question time difference between Orion Max and Model C.",
      "key": "rca.known.2"
    },
    {
      "available": true,
      "display": "Output tokens accounts for 55% (54–56%) of the per-question time difference between Orion Max and Model D.",
      "key": "rca.known.3"
    },
    {
      "available": true,
      "display": "Uncached input, price per token accounts for 45% (44–46%) of the per-question cost difference between Orion Max and Model A.",
      "key": "rca.known.4"
    },
    {
      "available": true,
      "display": "Uncached input, price per token accounts for 44% (43–45%) of the per-question cost difference between Orion Max and Model B.",
      "key": "rca.known.5"
    },
    {
      "available": true,
      "display": "Uncached input, price per token accounts for 43% (42–44%) of the per-question cost difference between Orion Max and Model C.",
      "key": "rca.known.6"
    },
    {
      "available": true,
      "display": "Uncached input, price per token accounts for 42% (42–43%) of the per-question cost difference between Orion Max and Model D.",
      "key": "rca.known.7"
    },
    {
      "available": true,
      "display": "Orion Max answered 2 of 6 scored questions without a tool call.",
      "key": "rca.known.8"
    },
    {
      "available": true,
      "display": "Model A answered 2 of 6 scored questions without a tool call.",
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
      "display": "low-scoring answers, mostly Accuracy: 2.8 index points on 2 questions (Q3 and Q4)",
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
      "display": "1 critical error (Q3)",
      "key": "rca.profile.B.flag.1"
    },
    {
      "available": true,
      "display": "1 critical error (Q3)",
      "key": "rca.profile.B.flags",
      "value": 1
    },
    {
      "available": true,
      "display": "confirmed critical errors: 7.2 index points on 1 question (Q3)",
      "key": "rca.profile.B.lostPointsOn"
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.profile.B.weakestDimension",
      "unavailableReason": "No dimension is below the model's Accuracy."
    },
    {
      "available": true,
      "display": "none",
      "key": "rca.profile.C.flags",
      "value": 0
    },
    {
      "available": true,
      "display": "answers without tools: 6.3 index points on 1 question (Q3)",
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
      "display": "1 isolated miss (Q1)",
      "key": "rca.profile.D.flag.1"
    },
    {
      "available": true,
      "display": "1 isolated miss (Q1)",
      "key": "rca.profile.D.flags",
      "value": 1
    },
    {
      "available": true,
      "display": "low-scoring answers, mostly Accuracy: 5.6 index points on 3 questions (Q1, Q3 and Q4)",
      "key": "rca.profile.D.lostPointsOn"
    },
    {
      "available": true,
      "display": "last of 5 models on Accuracy",
      "key": "rca.profile.D.lowestRank"
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.profile.D.weakestDimension",
      "unavailableReason": "No dimension is below the model's Accuracy."
    },
    {
      "available": true,
      "display": "none",
      "key": "rca.profile.subject.flags",
      "value": 0
    },
    {
      "available": true,
      "display": "answers without tools: 9.8 index points on 1 question (Q3)",
      "key": "rca.profile.subject.lostPointsOn"
    },
    {
      "available": true,
      "display": "tied for first of 5 models on Accuracy",
      "key": "rca.profile.subject.strength.1"
    },
    {
      "available": true,
      "display": "tied for first of 5 models on Completeness",
      "key": "rca.profile.subject.strength.2"
    },
    {
      "available": false,
      "display": "not available",
      "key": "rca.profile.subject.weakestDimension",
      "unavailableReason": "No dimension is below the model's Accuracy."
    },
    {
      "available": true,
      "display": "0.0 points",
      "key": "rca.quality.subject.A.dimension.accuracy.points",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 points",
      "key": "rca.quality.subject.A.dimension.completeness.points",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 points",
      "key": "rca.quality.subject.A.dimension.conciseness.points",
      "value": 0
    },
    {
      "available": true,
      "display": "0.0 points",
      "key": "rca.quality.subject.A.dimension.readability.points",
      "value": 0
    },
    {
      "available": true,
      "display": "+5.0 points",
      "key": "rca.quality.subject.A.other.points",
      "value": 5
    },
    {
      "available": true,
      "display": "+5.0 index points",
      "key": "rca.quality.subject.A.total",
      "value": 5
    },
    {
      "available": true,
      "display": "+8.3 points",
      "key": "rca.quality.subject.B.dimension.accuracy.points",
      "value": 8.341666666666661
    },
    {
      "available": true,
      "display": "+3.8 points",
      "key": "rca.quality.subject.B.dimension.completeness.points",
      "value": 3.791666666666664
    },
    {
      "available": true,
      "display": "+1.5 points",
      "key": "rca.quality.subject.B.dimension.conciseness.points",
      "value": 1.5166666666666657
    },
    {
      "available": true,
      "display": "+1.5 points",
      "key": "rca.quality.subject.B.dimension.readability.points",
      "value": 1.5166666666666657
    },
    {
      "available": true,
      "display": "-0.2 points",
      "key": "rca.quality.subject.B.other.points",
      "value": -0.1666666666666572
    },
    {
      "available": true,
      "display": "+15.0 index points",
      "key": "rca.quality.subject.B.total",
      "value": 15
    },
    {
      "available": true,
      "display": "+8.7 points",
      "key": "rca.quality.subject.C.dimension.accuracy.points",
      "value": 8.70833333333332
    },
    {
      "available": true,
      "display": "+4.0 points",
      "key": "rca.quality.subject.C.dimension.completeness.points",
      "value": 3.958333333333327
    },
    {
      "available": true,
      "display": "+1.6 points",
      "key": "rca.quality.subject.C.dimension.conciseness.points",
      "value": 1.5833333333333308
    },
    {
      "available": true,
      "display": "+1.6 points",
      "key": "rca.quality.subject.C.dimension.readability.points",
      "value": 1.5833333333333308
    },
    {
      "available": true,
      "display": "-0.8 points",
      "key": "rca.quality.subject.C.other.points",
      "value": -0.8333333333333073
    },
    {
      "available": true,
      "display": "+15.0 index points",
      "key": "rca.quality.subject.C.total",
      "value": 15
    },
    {
      "available": true,
      "display": "+16.5 points",
      "key": "rca.quality.subject.D.dimension.accuracy.points",
      "value": 16.50000000000001
    },
    {
      "available": true,
      "display": "+7.5 points",
      "key": "rca.quality.subject.D.dimension.completeness.points",
      "value": 7.500000000000004
    },
    {
      "available": true,
      "display": "+3.0 points",
      "key": "rca.quality.subject.D.dimension.conciseness.points",
      "value": 3.0000000000000018
    },
    {
      "available": true,
      "display": "+3.0 points",
      "key": "rca.quality.subject.D.dimension.readability.points",
      "value": 3.0000000000000018
    },
    {
      "available": true,
      "display": "0.0 points",
      "key": "rca.quality.subject.D.other.points",
      "value": -1.4210854715202004E-14
    },
    {
      "available": true,
      "display": "+30.0 index points",
      "key": "rca.quality.subject.D.total",
      "value": 30
    },
    {
      "available": true,
      "display": "none fired",
      "key": "rca.question.1.hypotheses",
      "value": 0
    },
    {
      "available": true,
      "display": "Orion Max",
      "key": "rca.question.1.model",
      "value": "subject"
    },
    {
      "available": true,
      "display": "Model A",
      "key": "rca.question.1.neighbor",
      "value": "A"
    },
    {
      "available": true,
      "display": "not established",
      "key": "rca.question.1.orderEstablished",
      "value": false
    },
    {
      "available": true,
      "display": "tied for first of 5 models on Accuracy; tied for first of 5 models on Completeness",
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
      "display": "Why did Orion Max score highest, though its lead over Model A is not established?",
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
      "display": "Model D",
      "key": "rca.question.2.model",
      "value": "D"
    },
    {
      "available": true,
      "display": "Model C",
      "key": "rca.question.2.neighbor",
      "value": "C"
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
      "display": "Why did Model D score lowest?",
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
      "display": "HS3.1",
      "key": "rca.question.3.hypotheses",
      "value": 1
    },
    {
      "available": true,
      "display": "Model D",
      "key": "rca.question.3.model",
      "value": "D"
    },
    {
      "available": true,
      "display": "median model time 20.0 s; against Orion Max: output tokens carries 55% of the 6.0 s difference; output generation 59% of its time per question; 2.5 model calls per answer",
      "key": "rca.question.3.signals"
    },
    {
      "available": true,
      "display": "Probable (Moderate)",
      "key": "rca.question.3.status",
      "value": "Moderate"
    },
    {
      "available": true,
      "display": "Why is Model D slowest?",
      "key": "rca.question.3.text"
    },
    {
      "available": true,
      "display": "not answered by the model's own breakdown alone",
      "key": "rca.question.3.withinOnly",
      "value": false
    },
    {
      "available": true,
      "display": "HC4.2 and HC2.2",
      "key": "rca.question.4.hypotheses",
      "value": 2
    },
    {
      "available": true,
      "display": "Model C",
      "key": "rca.question.4.model",
      "value": "C"
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
      "display": "Why is Model C the most expensive?",
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
      "display": "Run 1, a replicate of all five models",
      "key": "rca.run.1.summary"
    },
    {
      "available": true,
      "display": "not split into parts: the difference may be chance",
      "key": "rca.speed.subject.A.decomposed",
      "value": false
    },
    {
      "available": true,
      "display": "-1.6 s per question",
      "key": "rca.speed.subject.A.total",
      "value": -1.55
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.speed.subject.B.callCount.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.speed.subject.B.callCount.share",
      "value": -0
    },
    {
      "available": true,
      "display": "2–5%",
      "key": "rca.speed.subject.B.decodeRate.interval"
    },
    {
      "available": true,
      "display": "4%",
      "key": "rca.speed.subject.B.decodeRate.share",
      "value": 0.0355595835938916
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.speed.subject.B.gap.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.speed.subject.B.gap.share",
      "value": -0
    },
    {
      "available": true,
      "display": "53–56%",
      "key": "rca.speed.subject.B.outputTokens.interval"
    },
    {
      "available": true,
      "display": "55%",
      "key": "rca.speed.subject.B.outputTokens.share",
      "value": 0.5477737497394417
    },
    {
      "available": true,
      "display": "42–42%",
      "key": "rca.speed.subject.B.perCallLatency.interval"
    },
    {
      "available": true,
      "display": "42%",
      "key": "rca.speed.subject.B.perCallLatency.share",
      "value": 0.4166666666666667
    },
    {
      "available": true,
      "display": "-3.0 s per question",
      "key": "rca.speed.subject.B.total",
      "value": -3
    },
    {
      "available": true,
      "display": "-38–11%",
      "key": "rca.speed.subject.C.callCount.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.speed.subject.C.callCount.share",
      "value": -0
    },
    {
      "available": true,
      "display": "-83–28%",
      "key": "rca.speed.subject.C.decodeRate.interval"
    },
    {
      "available": true,
      "display": "4%",
      "key": "rca.speed.subject.C.decodeRate.share",
      "value": 0.041609050059208896
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.speed.subject.C.gap.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.speed.subject.C.gap.share",
      "value": -0
    },
    {
      "available": true,
      "display": "35–127%",
      "key": "rca.speed.subject.C.outputTokens.interval"
    },
    {
      "available": true,
      "display": "55%",
      "key": "rca.speed.subject.C.outputTokens.share",
      "value": 0.5452041367539779
    },
    {
      "available": true,
      "display": "27–94%",
      "key": "rca.speed.subject.C.perCallLatency.interval"
    },
    {
      "available": true,
      "display": "41%",
      "key": "rca.speed.subject.C.perCallLatency.share",
      "value": 0.41318681318681316
    },
    {
      "available": true,
      "display": "-4.6 s per question",
      "key": "rca.speed.subject.C.total",
      "value": -4.55
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.speed.subject.D.callCount.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.speed.subject.D.callCount.share",
      "value": -0
    },
    {
      "available": true,
      "display": "2–5%",
      "key": "rca.speed.subject.D.decodeRate.interval"
    },
    {
      "available": true,
      "display": "3%",
      "key": "rca.speed.subject.D.decodeRate.share",
      "value": 0.03221821279644276
    },
    {
      "available": true,
      "display": "0–0%",
      "key": "rca.speed.subject.D.gap.interval"
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.speed.subject.D.gap.share",
      "value": -0
    },
    {
      "available": true,
      "display": "54–56%",
      "key": "rca.speed.subject.D.outputTokens.interval"
    },
    {
      "available": true,
      "display": "55%",
      "key": "rca.speed.subject.D.outputTokens.share",
      "value": 0.5511151205368906
    },
    {
      "available": true,
      "display": "42–42%",
      "key": "rca.speed.subject.D.perCallLatency.interval"
    },
    {
      "available": true,
      "display": "42%",
      "key": "rca.speed.subject.D.perCallLatency.share",
      "value": 0.4166666666666667
    },
    {
      "available": true,
      "display": "-6.0 s per question",
      "key": "rca.speed.subject.D.total",
      "value": -6
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
      "value": 0.006879944960440315
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.A.cost.cacheWrite.share",
      "value": 0
    },
    {
      "available": true,
      "display": "41%",
      "key": "rca.within.A.cost.output.share",
      "value": 0.40729274165806667
    },
    {
      "available": true,
      "display": "21%",
      "key": "rca.within.A.cost.reasoning.share",
      "value": 0.20915032679738563
    },
    {
      "available": true,
      "display": "$0.022 per question, mean",
      "key": "rca.within.A.cost.total",
      "value": 0.021802500000000002
    },
    {
      "available": true,
      "display": "38%",
      "key": "rca.within.A.cost.uncachedInput.share",
      "value": 0.37667698658410725
    },
    {
      "available": true,
      "display": "0.3 s per question, mean",
      "key": "rca.within.A.time.other.seconds",
      "value": 0.3
    },
    {
      "available": true,
      "display": "3%",
      "key": "rca.within.A.time.other.share",
      "value": 0.028490028490028487
    },
    {
      "available": true,
      "display": "6.2 s per question, mean",
      "key": "rca.within.A.time.outputGeneration.seconds",
      "value": 6.185
    },
    {
      "available": true,
      "display": "59%",
      "key": "rca.within.A.time.outputGeneration.share",
      "value": 0.5873694207027539
    },
    {
      "available": true,
      "display": "0.0 s per question, mean",
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
      "display": "1.5 s per question, mean",
      "key": "rca.within.A.time.toolExecution.seconds",
      "value": 1.5
    },
    {
      "available": true,
      "display": "14%",
      "key": "rca.within.A.time.toolExecution.share",
      "value": 0.14245014245014243
    },
    {
      "available": true,
      "display": "10.5 s per question, mean",
      "key": "rca.within.A.time.total",
      "value": 10.530000000000001
    },
    {
      "available": true,
      "display": "2.5 s per question, mean",
      "key": "rca.within.A.time.ttft.seconds",
      "value": 2.545
    },
    {
      "available": true,
      "display": "24%",
      "key": "rca.within.A.time.ttft.share",
      "value": 0.241690408357075
    },
    {
      "available": true,
      "display": "1%",
      "key": "rca.within.B.cost.cacheRead.share",
      "value": 0.0056832020784281885
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.B.cost.cacheWrite.share",
      "value": 0
    },
    {
      "available": true,
      "display": "37%",
      "key": "rca.within.B.cost.output.share",
      "value": 0.36827149468214665
    },
    {
      "available": true,
      "display": "21%",
      "key": "rca.within.B.cost.reasoning.share",
      "value": 0.20914183648615736
    },
    {
      "available": true,
      "display": "$0.031 per question, mean",
      "key": "rca.within.B.cost.total",
      "value": 0.0307925
    },
    {
      "available": true,
      "display": "42%",
      "key": "rca.within.B.cost.uncachedInput.share",
      "value": 0.41690346675326784
    },
    {
      "available": true,
      "display": "0.3 s per question, mean",
      "key": "rca.within.B.time.other.seconds",
      "value": 0.3
    },
    {
      "available": true,
      "display": "3%",
      "key": "rca.within.B.time.other.share",
      "value": 0.025041736227045072
    },
    {
      "available": true,
      "display": "7.0 s per question, mean",
      "key": "rca.within.B.time.outputGeneration.seconds",
      "value": 7.015
    },
    {
      "available": true,
      "display": "59%",
      "key": "rca.within.B.time.outputGeneration.share",
      "value": 0.585559265442404
    },
    {
      "available": true,
      "display": "0.0 s per question, mean",
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
      "display": "1.5 s per question, mean",
      "key": "rca.within.B.time.toolExecution.seconds",
      "value": 1.5
    },
    {
      "available": true,
      "display": "13%",
      "key": "rca.within.B.time.toolExecution.share",
      "value": 0.12520868113522537
    },
    {
      "available": true,
      "display": "12.0 s per question, mean",
      "key": "rca.within.B.time.total",
      "value": 11.98
    },
    {
      "available": true,
      "display": "3.2 s per question, mean",
      "key": "rca.within.B.time.ttft.seconds",
      "value": 3.165
    },
    {
      "available": true,
      "display": "26%",
      "key": "rca.within.B.time.ttft.share",
      "value": 0.26419031719532554
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.C.cost.cacheRead.share",
      "value": 0.004880131763557617
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.C.cost.cacheWrite.share",
      "value": 0
    },
    {
      "available": true,
      "display": "34%",
      "key": "rca.within.C.cost.output.share",
      "value": 0.34356127615445625
    },
    {
      "available": true,
      "display": "21%",
      "key": "rca.within.C.cost.reasoning.share",
      "value": 0.21082169218568902
    },
    {
      "available": true,
      "display": "$0.041 per question, mean",
      "key": "rca.within.C.cost.total",
      "value": 0.0409825
    },
    {
      "available": true,
      "display": "44%",
      "key": "rca.within.C.cost.uncachedInput.share",
      "value": 0.44073689989629716
    },
    {
      "available": true,
      "display": "0.3 s per question, mean",
      "key": "rca.within.C.time.other.seconds",
      "value": 0.3
    },
    {
      "available": true,
      "display": "2%",
      "key": "rca.within.C.time.other.share",
      "value": 0.022172949002217293
    },
    {
      "available": true,
      "display": "7.9 s per question, mean",
      "key": "rca.within.C.time.outputGeneration.seconds",
      "value": 7.935
    },
    {
      "available": true,
      "display": "59%",
      "key": "rca.within.C.time.outputGeneration.share",
      "value": 0.5864745011086474
    },
    {
      "available": true,
      "display": "0.0 s per question, mean",
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
      "display": "1.5 s per question, mean",
      "key": "rca.within.C.time.toolExecution.seconds",
      "value": 1.5
    },
    {
      "available": true,
      "display": "11%",
      "key": "rca.within.C.time.toolExecution.share",
      "value": 0.11086474501108647
    },
    {
      "available": true,
      "display": "13.5 s per question, mean",
      "key": "rca.within.C.time.total",
      "value": 13.530000000000001
    },
    {
      "available": true,
      "display": "3.8 s per question, mean",
      "key": "rca.within.C.time.ttft.seconds",
      "value": 3.795
    },
    {
      "available": true,
      "display": "28%",
      "key": "rca.within.C.time.ttft.share",
      "value": 0.28048780487804875
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.D.cost.cacheRead.share",
      "value": 0.004296147787483889
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.D.cost.cacheWrite.share",
      "value": 0
    },
    {
      "available": true,
      "display": "33%",
      "key": "rca.within.D.cost.output.share",
      "value": 0.3265072318487756
    },
    {
      "available": true,
      "display": "21%",
      "key": "rca.within.D.cost.reasoning.share",
      "value": 0.2130889302592009
    },
    {
      "available": true,
      "display": "$0.052 per question, mean",
      "key": "rca.within.D.cost.total",
      "value": 0.052372499999999995
    },
    {
      "available": true,
      "display": "46%",
      "key": "rca.within.D.cost.uncachedInput.share",
      "value": 0.45610769010453966
    },
    {
      "available": true,
      "display": "0.3 s per question, mean",
      "key": "rca.within.D.time.other.seconds",
      "value": 0.3
    },
    {
      "available": true,
      "display": "2%",
      "key": "rca.within.D.time.other.share",
      "value": 0.02002670226969292
    },
    {
      "available": true,
      "display": "8.8 s per question, mean",
      "key": "rca.within.D.time.outputGeneration.seconds",
      "value": 8.765
    },
    {
      "available": true,
      "display": "59%",
      "key": "rca.within.D.time.outputGeneration.share",
      "value": 0.585113484646195
    },
    {
      "available": true,
      "display": "0.0 s per question, mean",
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
      "display": "1.5 s per question, mean",
      "key": "rca.within.D.time.toolExecution.seconds",
      "value": 1.5
    },
    {
      "available": true,
      "display": "10%",
      "key": "rca.within.D.time.toolExecution.share",
      "value": 0.10013351134846461
    },
    {
      "available": true,
      "display": "15.0 s per question, mean",
      "key": "rca.within.D.time.total",
      "value": 14.98
    },
    {
      "available": true,
      "display": "4.4 s per question, mean",
      "key": "rca.within.D.time.ttft.seconds",
      "value": 4.415
    },
    {
      "available": true,
      "display": "29%",
      "key": "rca.within.D.time.ttft.share",
      "value": 0.2947263017356475
    },
    {
      "available": true,
      "display": "1%",
      "key": "rca.within.subject.cost.cacheRead.share",
      "value": 0.008920606601248885
    },
    {
      "available": true,
      "display": "0%",
      "key": "rca.within.subject.cost.cacheWrite.share",
      "value": 0
    },
    {
      "available": true,
      "display": "48%",
      "key": "rca.within.subject.cost.output.share",
      "value": 0.47814451382694023
    },
    {
      "available": true,
      "display": "21%",
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
      "display": "30%",
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
      "display": "3%",
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
      "display": "59%",
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
      "display": "0%",
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
      "display": "17%",
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
      "display": "21%",
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
      "display": "Accuracy 55%, Completeness 25%, Conciseness 10%, Readability 10%",
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
      "display": "0% of model time",
      "key": "speed.ownWaitShare",
      "value": 0
    },
    {
      "available": true,
      "display": "3rd of 5",
      "key": "speed.rank",
      "value": 3
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
      "display": "no response-style conflict",
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
      "display": "23%",
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
      "display": "31% of output tokens",
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
      "available": true,
      "display": "0.0",
      "key": "tools.callsPerQuestion.peerMean",
      "value": 0
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
  "noSignificanceInstead": "Put each model's runs in an analysis group, open one in the Multi-Run Analysis tab and choose the other under Compare with group.",
  "noSignificanceSummary": "Testing every pair among these 5 models at once would flag chance differences as significant, so this view tests none.",
  "pairedDifferences": [
    {
      "lower": -16.5,
      "meanDifference": 0,
      "peerLetter": "A",
      "sharedQuestions": 6,
      "upper": 10.5
    },
    {
      "lower": 9,
      "meanDifference": 15.166666666666666,
      "peerLetter": "B",
      "sharedQuestions": 6,
      "upper": 20.833333333333332
    },
    {
      "lower": 7.666666666666667,
      "meanDifference": 15.833333333333334,
      "peerLetter": "C",
      "sharedQuestions": 6,
      "upper": 22.5
    },
    {
      "lower": 19.5,
      "meanDifference": 30,
      "peerLetter": "D",
      "sharedQuestions": 6,
      "upper": 38
    }
  ],
  "peers": [
    {
      "costDegraded": false,
      "displayName": "Vega Pro",
      "entryKey": "run:32",
      "explanation": "Comparable: measured under the baseline condition.",
      "label": "Vega Pro",
      "letter": "A",
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
      "letter": "B",
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
      "letter": "C",
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
      "letter": "D",
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
      "costUsd": 0.0132,
      "criticalError": false,
      "difference": 22,
      "inputTokens": 4100,
      "itemRevisionUsed": 1,
      "modelCallCount": 3,
      "modelTimeMs": 8220,
      "number": 1,
      "orderIndex": 1,
      "outputTokens": 920,
      "peerCount": 4,
      "peerMax": 85,
      "peerMean": 70,
      "peerMin": 50,
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
      "difference": 21.25,
      "inputTokens": 4200,
      "itemRevisionUsed": 1,
      "modelCallCount": 2,
      "modelTimeMs": 5740,
      "number": 2,
      "orderIndex": 2,
      "outputTokens": 940,
      "peerCount": 4,
      "peerMax": 80,
      "peerMean": 66.75,
      "peerMin": 55,
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
      "difference": -8.75,
      "inputTokens": 4300,
      "itemRevisionUsed": 1,
      "modelCallCount": 3,
      "modelTimeMs": 8820,
      "number": 3,
      "orderIndex": 3,
      "outputTokens": 960,
      "peerCount": 4,
      "peerMax": 70,
      "peerMean": 38.75,
      "peerMin": 25,
      "peersAbove": 1,
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
      "difference": 24,
      "inputTokens": 4400,
      "itemRevisionUsed": 1,
      "modelCallCount": 2,
      "modelTimeMs": 6140,
      "number": 4,
      "orderIndex": 4,
      "outputTokens": 980,
      "peerCount": 4,
      "peerMax": 75,
      "peerMean": 66,
      "peerMin": 52,
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
      "difference": 14.5,
      "inputTokens": 4500,
      "itemRevisionUsed": 1,
      "modelCallCount": 3,
      "modelTimeMs": 9420,
      "number": 5,
      "orderIndex": 5,
      "outputTokens": 1000,
      "peerCount": 4,
      "peerMax": 82,
      "peerMean": 70.5,
      "peerMin": 58,
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
      "difference": 18.5,
      "inputTokens": 4600,
      "itemRevisionUsed": 1,
      "modelCallCount": 2,
      "modelTimeMs": 6540,
      "number": 6,
      "orderIndex": 6,
      "outputTokens": 1020,
      "peerCount": 4,
      "peerMax": 88,
      "peerMean": 76.5,
      "peerMin": 60,
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
        "bestHypothesis": "HQ3.1",
        "reason": "Suggestive (Low) because it is observed, not measured.",
        "status": "NotEstablished"
      },
      {
        "axis": "speed",
        "bestHypothesis": "HS3.1",
        "reason": "Probable (Moderate) because both models ran only once per suite.",
        "status": "ProbableCause"
      },
      {
        "axis": "cost",
        "bestHypothesis": "HC2.1",
        "reason": "Known from the price cards: the differences in price per token account for the gap, which the price cards show rather than a cause the runs found; every other hypothesis on this axis was ruled out.",
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
        "first": "subject",
        "firstTotal": 0.0140125,
        "notEstablished": false,
        "second": "A",
        "secondTotal": 0.021802500000000002,
        "sharedQuestions": 6
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
        "first": "subject",
        "firstTotal": 0.0140125,
        "notEstablished": false,
        "second": "B",
        "secondTotal": 0.0307925,
        "sharedQuestions": 6
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
        "first": "subject",
        "firstTotal": 0.0140125,
        "notEstablished": false,
        "second": "C",
        "secondTotal": 0.0409825,
        "sharedQuestions": 6
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
        "first": "subject",
        "firstTotal": 0.0140125,
        "notEstablished": false,
        "second": "D",
        "secondTotal": 0.052372499999999995,
        "sharedQuestions": 6
      }
    ],
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
        "kind": "model",
        "label": "Vega Pro",
        "letter": "A",
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
        "token": "A",
        "total": 0.021802500000000002,
        "unit": "usd"
      },
      {
        "kind": "model",
        "label": "Lyra Mini",
        "letter": "B",
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
        "token": "B",
        "total": 0.0307925,
        "unit": "usd"
      },
      {
        "kind": "model",
        "label": "Nova Lite",
        "letter": "C",
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
        "token": "C",
        "total": 0.0409825,
        "unit": "usd"
      },
      {
        "kind": "model",
        "label": "Zeta Prime",
        "letter": "D",
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
        "token": "D",
        "total": 0.052372499999999995,
        "unit": "usd"
      }
    ],
    "engineVersion": 8,
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
            "label": "time to first token",
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
        "token": "A",
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
            "label": "time to first token",
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
        "token": "B",
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
            "label": "time to first token",
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
        "token": "C",
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
            "label": "time to first token",
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
        "token": "D",
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
        "factKeys": [],
        "for": [
          "The subject's paired-bootstrap interval against this peer includes zero.",
          "The two 95% intervals overlap."
        ],
        "grade": "Low",
        "gradeReasons": [
          "with one run per suite, the tests can miss a real difference (grade capped at Low)"
        ],
        "id": "H0-Q",
        "key": "H0-Q.1",
        "kind": "null",
        "label": "no intelligence difference between Orion Max and Model A",
        "loci": [],
        "locus": "—",
        "models": [
          "subject",
          "A"
        ],
        "modelsText": "Orion Max and Model A",
        "nextRunNumber": 1,
        "number": 1,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the Intelligence Index is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Orion Max vs Model A",
        "statement": "The Intelligence Index difference between Orion Max and Model A is within sampling noise."
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
        "label": "no difference within Orion Max's own questions",
        "loci": [],
        "locus": "—",
        "models": [
          "subject"
        ],
        "modelsText": "Orion Max",
        "nextRunNumber": 1,
        "number": 1,
        "priceEffect": false,
        "refutedIf": "A Mann–Whitney contrast is established after the Holm adjustment.",
        "robustness": [],
        "shortStatement": "Noise within Orion Max",
        "statement": "The contrasts between groups of Orion Max's own questions are within sampling noise."
      },
      {
        "against": [
          "The subject's paired-bootstrap interval against this peer excludes zero (unadjusted).",
          "The two 95% intervals do not overlap."
        ],
        "axis": "intelligence",
        "confounds": [],
        "factKeys": [],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: the subject's paired-bootstrap interval against this peer excludes zero (unadjusted)"
        ],
        "id": "H0-Q",
        "key": "H0-Q.2",
        "kind": "null",
        "label": "no intelligence difference between Orion Max and Model B",
        "loci": [],
        "locus": "—",
        "models": [
          "subject",
          "B"
        ],
        "modelsText": "Orion Max and Model B",
        "number": 2,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the Intelligence Index is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Orion Max vs Model B",
        "statement": "The Intelligence Index difference between Orion Max and Model B is within sampling noise."
      },
      {
        "against": [
          "The subject's paired-bootstrap interval against this peer excludes zero (unadjusted).",
          "The two 95% intervals do not overlap."
        ],
        "axis": "intelligence",
        "confounds": [],
        "factKeys": [],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: the subject's paired-bootstrap interval against this peer excludes zero (unadjusted)"
        ],
        "id": "H0-Q",
        "key": "H0-Q.3",
        "kind": "null",
        "label": "no intelligence difference between Orion Max and Model C",
        "loci": [],
        "locus": "—",
        "models": [
          "subject",
          "C"
        ],
        "modelsText": "Orion Max and Model C",
        "number": 3,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the Intelligence Index is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Orion Max vs Model C",
        "statement": "The Intelligence Index difference between Orion Max and Model C is within sampling noise."
      },
      {
        "against": [
          "The subject's paired-bootstrap interval against this peer excludes zero (unadjusted).",
          "The two 95% intervals do not overlap."
        ],
        "axis": "intelligence",
        "confounds": [],
        "factKeys": [],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: the subject's paired-bootstrap interval against this peer excludes zero (unadjusted)"
        ],
        "id": "H0-Q",
        "key": "H0-Q.4",
        "kind": "null",
        "label": "no intelligence difference between Orion Max and Model D",
        "loci": [],
        "locus": "—",
        "models": [
          "subject",
          "D"
        ],
        "modelsText": "Orion Max and Model D",
        "number": 4,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the Intelligence Index is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Orion Max vs Model D",
        "statement": "The Intelligence Index difference between Orion Max and Model D is within sampling noise."
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
          "subject",
          "A",
          "B",
          "C",
          "D"
        ],
        "modelsText": "Orion Max, Model A, Model B, Model C and Model D",
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
        "factKeys": [],
        "for": [
          "No family test exists; the 95% bootstrap interval of the per-question difference includes zero."
        ],
        "grade": "Low",
        "gradeReasons": [
          "with one run per suite, the tests can miss a real difference (grade capped at Low)"
        ],
        "id": "H0-S",
        "key": "H0-S.1",
        "kind": "null",
        "label": "no speed difference between Orion Max and Model A",
        "loci": [],
        "locus": "—",
        "models": [
          "subject",
          "A"
        ],
        "modelsText": "Orion Max and Model A",
        "nextRunNumber": 1,
        "number": 1,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the model time is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Orion Max vs Model A",
        "statement": "The model time difference between Orion Max and Model A is within sampling noise."
      },
      {
        "against": [
          "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)."
        ],
        "axis": "speed",
        "confounds": [],
        "factKeys": [],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)"
        ],
        "id": "H0-S",
        "key": "H0-S.2",
        "kind": "null",
        "label": "no speed difference between Orion Max and Model B",
        "loci": [],
        "locus": "—",
        "models": [
          "subject",
          "B"
        ],
        "modelsText": "Orion Max and Model B",
        "number": 2,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the model time is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Orion Max vs Model B",
        "statement": "The model time difference between Orion Max and Model B is within sampling noise."
      },
      {
        "against": [
          "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)."
        ],
        "axis": "speed",
        "confounds": [],
        "factKeys": [],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)"
        ],
        "id": "H0-S",
        "key": "H0-S.3",
        "kind": "null",
        "label": "no speed difference between Orion Max and Model C",
        "loci": [],
        "locus": "—",
        "models": [
          "subject",
          "C"
        ],
        "modelsText": "Orion Max and Model C",
        "number": 3,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the model time is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Orion Max vs Model C",
        "statement": "The model time difference between Orion Max and Model C is within sampling noise."
      },
      {
        "against": [
          "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)."
        ],
        "axis": "speed",
        "confounds": [],
        "factKeys": [],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)"
        ],
        "id": "H0-S",
        "key": "H0-S.4",
        "kind": "null",
        "label": "no speed difference between Orion Max and Model D",
        "loci": [],
        "locus": "—",
        "models": [
          "subject",
          "D"
        ],
        "modelsText": "Orion Max and Model D",
        "number": 4,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the model time is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Orion Max vs Model D",
        "statement": "The model time difference between Orion Max and Model D is within sampling noise."
      },
      {
        "against": [],
        "axis": "speed",
        "confounds": [],
        "explains": 3306.6907232213434,
        "factKeys": [
          "rca.speed.subject.D.outputTokens.share",
          "rca.speed.subject.D.total"
        ],
        "for": [
          "Output tokens accounts for 55% (54–56%) of the 6.0 s per-question difference in model time.",
          "Model D's reasoning share of output is 39%, Orion Max's 31%."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "both models ran only once per suite"
        ],
        "id": "HS3",
        "key": "HS3.1",
        "kind": "pair",
        "label": "Model D's more output tokens, compared with Orion Max",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "D",
          "subject"
        ],
        "modelsText": "Model D and Orion Max",
        "nextRunNumber": 1,
        "number": 1,
        "priceEffect": false,
        "refutedIf": "The decode-rate share is larger.",
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
        "share": 0.5511151205368906,
        "shareLower": 0.5378380657822239,
        "shareUpper": 0.5641345737176965,
        "shortStatement": "More output tokens: Model D",
        "statement": "Model D is slower than Orion Max mainly from more output (reasoning) tokens."
      },
      {
        "against": [],
        "axis": "speed",
        "confounds": [],
        "explains": 2480.6788222305995,
        "factKeys": [
          "rca.speed.subject.C.outputTokens.share",
          "rca.speed.subject.C.total"
        ],
        "for": [
          "Output tokens accounts for 55% (35–127%) of the 4.6 s per-question difference in model time.",
          "Model C's reasoning share of output is 38%, Orion Max's 31%."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "both models ran only once per suite"
        ],
        "id": "HS3",
        "key": "HS3.2",
        "kind": "pair",
        "label": "Model C's more output tokens, compared with Orion Max",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "C",
          "subject"
        ],
        "modelsText": "Model C and Orion Max",
        "nextRunNumber": 1,
        "number": 2,
        "priceEffect": false,
        "refutedIf": "The decode-rate share is larger.",
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
        "share": 0.5452041367539779,
        "shareLower": 0.3475635820013107,
        "shareUpper": 1.2744376428533912,
        "shortStatement": "More output tokens: Model C",
        "statement": "Model C is slower than Orion Max mainly from more output (reasoning) tokens."
      },
      {
        "against": [],
        "axis": "speed",
        "confounds": [],
        "explains": 1643.3212492183252,
        "factKeys": [
          "rca.speed.subject.B.outputTokens.share",
          "rca.speed.subject.B.total"
        ],
        "for": [
          "Output tokens accounts for 55% (53–56%) of the 3.0 s per-question difference in model time.",
          "Model B's reasoning share of output is 36%, Orion Max's 31%."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "both models ran only once per suite"
        ],
        "id": "HS3",
        "key": "HS3.3",
        "kind": "pair",
        "label": "Model B's more output tokens, compared with Orion Max",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "B",
          "subject"
        ],
        "modelsText": "Model B and Orion Max",
        "nextRunNumber": 1,
        "number": 3,
        "priceEffect": false,
        "refutedIf": "The decode-rate share is larger.",
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
        "share": 0.5477737497394417,
        "shareLower": 0.5330607172279814,
        "shareUpper": 0.5621770710553743,
        "shortStatement": "More output tokens: Model B",
        "statement": "Model B is slower than Orion Max mainly from more output (reasoning) tokens."
      },
      {
        "against": [],
        "axis": "speed",
        "confounds": [],
        "factKeys": [
          "speed.modelCallsPerAnswer"
        ],
        "for": [
          "Across Orion Max's questions, model time rises with model calls per answer: Spearman ρ 0.88 (95% interval 0.23 to 0.99, n 6)."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "Orion Max ran only once per suite"
        ],
        "id": "WS3",
        "key": "WS3.1",
        "kind": "within",
        "label": "Orion Max's time spent on tool rounds",
        "loci": [
          "model",
          "chat"
        ],
        "locus": "model / chat",
        "models": [
          "subject"
        ],
        "modelsText": "Orion Max",
        "nextRunNumber": 1,
        "number": 1,
        "priceEffect": false,
        "refutedIf": "The ρ interval includes zero.",
        "robustness": [
          {
            "detail": "Orion Max ran only once per suite.",
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
          "Reasoning is 31% of Orion Max's output tokens."
        ],
        "axis": "speed",
        "confounds": [],
        "explains": 5265,
        "factKeys": [
          "rca.within.subject.time.outputGeneration.share",
          "tokens.reasoningShare"
        ],
        "for": [
          "Output generation is 70% (69–71%) of model time per question."
        ],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: reasoning is 31% of Orion Max's output tokens"
        ],
        "id": "WS2",
        "key": "WS2.1",
        "kind": "within",
        "label": "Orion Max's time spent generating output",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "subject"
        ],
        "modelsText": "Orion Max",
        "number": 1,
        "priceEffect": false,
        "refutedIf": "The reasoning share is under one half.",
        "robustness": [
          {
            "detail": "Orion Max ran only once per suite.",
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
        "against": [
          "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)."
        ],
        "axis": "cost",
        "confounds": [],
        "factKeys": [],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)"
        ],
        "id": "H0-C",
        "key": "H0-C.1",
        "kind": "null",
        "label": "no cost difference between Orion Max and Model A",
        "loci": [],
        "locus": "—",
        "models": [
          "subject",
          "A"
        ],
        "modelsText": "Orion Max and Model A",
        "number": 1,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the cost per question is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Orion Max vs Model A",
        "statement": "The cost per question difference between Orion Max and Model A is within sampling noise."
      },
      {
        "against": [
          "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)."
        ],
        "axis": "cost",
        "confounds": [],
        "factKeys": [],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)"
        ],
        "id": "H0-C",
        "key": "H0-C.2",
        "kind": "null",
        "label": "no cost difference between Orion Max and Model B",
        "loci": [],
        "locus": "—",
        "models": [
          "subject",
          "B"
        ],
        "modelsText": "Orion Max and Model B",
        "number": 2,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the cost per question is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Orion Max vs Model B",
        "statement": "The cost per question difference between Orion Max and Model B is within sampling noise."
      },
      {
        "against": [
          "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)."
        ],
        "axis": "cost",
        "confounds": [],
        "factKeys": [],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)"
        ],
        "id": "H0-C",
        "key": "H0-C.3",
        "kind": "null",
        "label": "no cost difference between Orion Max and Model C",
        "loci": [],
        "locus": "—",
        "models": [
          "subject",
          "C"
        ],
        "modelsText": "Orion Max and Model C",
        "number": 3,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the cost per question is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Orion Max vs Model C",
        "statement": "The cost per question difference between Orion Max and Model C is within sampling noise."
      },
      {
        "against": [
          "No family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)."
        ],
        "axis": "cost",
        "confounds": [],
        "factKeys": [],
        "for": [],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: no family test exists; the 95% bootstrap interval of the per-question difference excludes zero (unadjusted)"
        ],
        "id": "H0-C",
        "key": "H0-C.4",
        "kind": "null",
        "label": "no cost difference between Orion Max and Model D",
        "loci": [],
        "locus": "—",
        "models": [
          "subject",
          "D"
        ],
        "modelsText": "Orion Max and Model D",
        "number": 4,
        "priceEffect": false,
        "refutedIf": "A paired test of the two on the cost per question is established after adjustment.",
        "robustness": [],
        "shortStatement": "Noise: Orion Max vs Model D",
        "statement": "The cost per question difference between Orion Max and Model D is within sampling noise."
      },
      {
        "against": [],
        "axis": "cost",
        "confounds": [],
        "explains": 0.0097,
        "factKeys": [
          "cost.component.output.perQuestion",
          "cost.component.reasoning.perQuestion"
        ],
        "for": [
          "Visible output and reasoning are 69% of Orion Max's cost per question."
        ],
        "grade": "Moderate",
        "gradeReasons": [
          "Orion Max ran only once per suite"
        ],
        "id": "WC1",
        "key": "WC1.1",
        "kind": "within",
        "label": "Orion Max's output cost",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "subject"
        ],
        "modelsText": "Orion Max",
        "number": 1,
        "priceEffect": false,
        "refutedIf": "Another component is larger.",
        "robustness": [
          {
            "detail": "Orion Max ran only once per suite.",
            "name": "Replicates",
            "status": "Failed"
          }
        ],
        "share": 0.6922390722569134,
        "shortStatement": "Output cost: Orion Max",
        "statement": "Output (with reasoning) is Orion Max's largest cost component (where its cost goes, not a comparison)."
      },
      {
        "against": [
          "Model D's cache-read share is 28% against the models' median of 26%."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.0034638809958028563,
        "factKeys": [
          "rca.cost.subject.D.uncachedInput.volume.share",
          "rca.cost.subject.D.total"
        ],
        "for": [
          "Uncached input, token volume accounts for 9% (9–9%) of the $0.038 per-question cost difference."
        ],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: Model D's cache-read share is 28% against the models' median of 26%"
        ],
        "id": "HC4",
        "key": "HC4.1",
        "kind": "pair",
        "label": "Model D's weak prompt caching, compared with Orion Max",
        "loci": [
          "chat",
          "provider"
        ],
        "locus": "chat / provider",
        "models": [
          "D",
          "subject"
        ],
        "modelsText": "Model D and Orion Max",
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
        "shortStatement": "Weak caching: Model D",
        "statement": "Weak prompt caching raises Model D's cost against Orion Max."
      },
      {
        "against": [
          "Model C's cache-read share is 27% against the models' median of 26%."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.002258739233355464,
        "factKeys": [
          "rca.cost.subject.C.uncachedInput.volume.share",
          "rca.cost.subject.C.total"
        ],
        "for": [
          "Uncached input, token volume accounts for 8% (8–9%) of the $0.027 per-question cost difference."
        ],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: Model C's cache-read share is 27% against the models' median of 26%"
        ],
        "id": "HC4",
        "key": "HC4.2",
        "kind": "pair",
        "label": "Model C's weak prompt caching, compared with Orion Max",
        "loci": [
          "chat",
          "provider"
        ],
        "locus": "chat / provider",
        "models": [
          "C",
          "subject"
        ],
        "modelsText": "Model C and Orion Max",
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
        "shortStatement": "Weak caching: Model C",
        "statement": "Weak prompt caching raises Model C's cost against Orion Max."
      },
      {
        "against": [
          "Model B's cache-read share is 26% against the models' median of 26%."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.001272137066970963,
        "factKeys": [
          "rca.cost.subject.B.uncachedInput.volume.share",
          "rca.cost.subject.B.total"
        ],
        "for": [
          "Uncached input, token volume accounts for 8% (7–8%) of the $0.017 per-question cost difference."
        ],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: Model B's cache-read share is 26% against the models' median of 26%"
        ],
        "id": "HC4",
        "key": "HC4.3",
        "kind": "pair",
        "label": "Model B's weak prompt caching, compared with Orion Max",
        "loci": [
          "chat",
          "provider"
        ],
        "locus": "chat / provider",
        "models": [
          "B",
          "subject"
        ],
        "modelsText": "Model B and Orion Max",
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
        "share": 0.07581269767407409,
        "shareLower": 0.07412905748228368,
        "shareUpper": 0.07757564767873716,
        "shortStatement": "Weak caching: Model B",
        "statement": "Weak prompt caching raises Model B's cost against Orion Max."
      },
      {
        "against": [
          "Model A's cache-read share is 25% against the models' median of 26%."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.0005125227089027412,
        "factKeys": [
          "rca.cost.subject.A.uncachedInput.volume.share",
          "rca.cost.subject.A.total"
        ],
        "for": [
          "Uncached input, token volume accounts for 7% (6–7%) of the $0.0078 per-question cost difference."
        ],
        "grade": "NotSupported",
        "gradeReasons": [
          "its own test rules it out: Model A's cache-read share is 25% against the models' median of 26%"
        ],
        "id": "HC4",
        "key": "HC4.4",
        "kind": "pair",
        "label": "Model A's weak prompt caching, compared with Orion Max",
        "loci": [
          "chat",
          "provider"
        ],
        "locus": "chat / provider",
        "models": [
          "A",
          "subject"
        ],
        "modelsText": "Model A and Orion Max",
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
        "share": 0.06579238881935058,
        "shareLower": 0.06424291031175927,
        "shareUpper": 0.06741876767770795,
        "shortStatement": "Weak caching: Model A",
        "statement": "Weak prompt caching raises Model A's cost against Orion Max."
      },
      {
        "against": [
          "The volume effect is 31% (31–32%) of the difference."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.026411264046843817,
        "factKeys": [
          "rca.cost.subject.D.all.price.share",
          "rca.cost.subject.D.total"
        ],
        "for": [
          "Price per token, all components accounts for 69% (68–69%) of the $0.038 per-question cost difference."
        ],
        "grade": "High",
        "gradeReasons": [],
        "id": "HC2",
        "key": "HC2.1",
        "kind": "pair",
        "label": "Model D's higher price per token, compared with Orion Max",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "D",
          "subject"
        ],
        "modelsText": "Model D and Orion Max",
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
        "shortStatement": "Price per token: Model D",
        "statement": "Model D costs more than Orion Max mainly from its price per token."
      },
      {
        "against": [
          "The volume effect is 30% (30–31%) of the difference."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.018792880218132888,
        "factKeys": [
          "rca.cost.subject.C.all.price.share",
          "rca.cost.subject.C.total"
        ],
        "for": [
          "Price per token, all components accounts for 70% (69–70%) of the $0.027 per-question cost difference."
        ],
        "grade": "High",
        "gradeReasons": [],
        "id": "HC2",
        "key": "HC2.2",
        "kind": "pair",
        "label": "Model C's higher price per token, compared with Orion Max",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "C",
          "subject"
        ],
        "modelsText": "Model C and Orion Max",
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
        "shortStatement": "Price per token: Model C",
        "statement": "Model C costs more than Orion Max mainly from its price per token."
      },
      {
        "against": [
          "The volume effect is 29% (29–30%) of the difference."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.011859864660170447,
        "factKeys": [
          "rca.cost.subject.B.all.price.share",
          "rca.cost.subject.B.total"
        ],
        "for": [
          "Price per token, all components accounts for 71% (70–71%) of the $0.017 per-question cost difference."
        ],
        "grade": "High",
        "gradeReasons": [],
        "id": "HC2",
        "key": "HC2.3",
        "kind": "pair",
        "label": "Model B's higher price per token, compared with Orion Max",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "B",
          "subject"
        ],
        "modelsText": "Model B and Orion Max",
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
        "shortStatement": "Price per token: Model B",
        "statement": "Model B costs more than Orion Max mainly from its price per token."
      },
      {
        "against": [
          "The volume effect is 28% (27–29%) of the difference."
        ],
        "axis": "cost",
        "confounds": [],
        "explains": 0.005602724508327252,
        "factKeys": [
          "rca.cost.subject.A.all.price.share",
          "rca.cost.subject.A.total"
        ],
        "for": [
          "Price per token, all components accounts for 72% (71–73%) of the $0.0078 per-question cost difference."
        ],
        "grade": "High",
        "gradeReasons": [],
        "id": "HC2",
        "key": "HC2.4",
        "kind": "pair",
        "label": "Model A's higher price per token, compared with Orion Max",
        "loci": [
          "model"
        ],
        "locus": "model",
        "models": [
          "A",
          "subject"
        ],
        "modelsText": "Model A and Orion Max",
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
        "share": 0.71922009092776,
        "shareLower": 0.7123096297876061,
        "shareUpper": 0.7258059219416408,
        "shortStatement": "Price per token: Model A",
        "statement": "Model A costs more than Orion Max mainly from its price per token."
      }
    ],
    "knownFacts": [
      {
        "factKeys": [
          "rca.speed.subject.B.outputTokens.share"
        ],
        "kind": "decomposition",
        "number": 1,
        "text": "Output tokens accounts for 55% (53–56%) of the per-question time difference between Orion Max and Model B."
      },
      {
        "factKeys": [
          "rca.speed.subject.C.outputTokens.share"
        ],
        "kind": "decomposition",
        "number": 2,
        "text": "Output tokens accounts for 55% (35–127%) of the per-question time difference between Orion Max and Model C."
      },
      {
        "factKeys": [
          "rca.speed.subject.D.outputTokens.share"
        ],
        "kind": "decomposition",
        "number": 3,
        "text": "Output tokens accounts for 55% (54–56%) of the per-question time difference between Orion Max and Model D."
      },
      {
        "factKeys": [
          "rca.cost.subject.A.uncachedInput.price.share"
        ],
        "kind": "decomposition",
        "number": 4,
        "text": "Uncached input, price per token accounts for 45% (44–46%) of the per-question cost difference between Orion Max and Model A."
      },
      {
        "factKeys": [
          "rca.cost.subject.B.uncachedInput.price.share"
        ],
        "kind": "decomposition",
        "number": 5,
        "text": "Uncached input, price per token accounts for 44% (43–45%) of the per-question cost difference between Orion Max and Model B."
      },
      {
        "factKeys": [
          "rca.cost.subject.C.uncachedInput.price.share"
        ],
        "kind": "decomposition",
        "number": 6,
        "text": "Uncached input, price per token accounts for 43% (42–44%) of the per-question cost difference between Orion Max and Model C."
      },
      {
        "factKeys": [
          "rca.cost.subject.D.uncachedInput.price.share"
        ],
        "kind": "decomposition",
        "number": 7,
        "text": "Uncached input, price per token accounts for 42% (42–43%) of the per-question cost difference between Orion Max and Model D."
      },
      {
        "factKeys": [
          "profile.noToolAnswers"
        ],
        "kind": "count",
        "number": 8,
        "text": "Orion Max answered 2 of 6 scored questions without a tool call."
      },
      {
        "factKeys": [],
        "kind": "count",
        "number": 9,
        "text": "Model A answered 2 of 6 scored questions without a tool call."
      },
      {
        "factKeys": [],
        "kind": "count",
        "number": 10,
        "text": "Model B answered 2 of 6 scored questions without a tool call."
      },
      {
        "factKeys": [],
        "kind": "count",
        "number": 11,
        "text": "Model B has 1 critical error in 6 scored answers."
      },
      {
        "factKeys": [],
        "kind": "count",
        "number": 12,
        "text": "Model C answered 2 of 6 scored questions without a tool call."
      },
      {
        "factKeys": [],
        "kind": "count",
        "number": 13,
        "text": "Model D answered 2 of 6 scored questions without a tool call."
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
      },
      {
        "entryKey": "run:32",
        "isReference": false,
        "isSubject": false,
        "label": "Vega Pro",
        "letter": "A",
        "modelId": "vega-pro",
        "provider": "Southstar",
        "runIds": [
          32
        ],
        "token": "A"
      },
      {
        "entryKey": "run:33",
        "isReference": false,
        "isSubject": false,
        "label": "Lyra Mini",
        "letter": "B",
        "modelId": "lyra-mini",
        "provider": "Eastgate",
        "runIds": [
          33
        ],
        "token": "B"
      },
      {
        "entryKey": "run:34",
        "isReference": false,
        "isSubject": false,
        "label": "Nova Lite",
        "letter": "C",
        "modelId": "nova-lite",
        "provider": "Westlake",
        "runIds": [
          34
        ],
        "token": "C"
      },
      {
        "entryKey": "run:35",
        "isReference": false,
        "isSubject": false,
        "label": "Zeta Prime",
        "letter": "D",
        "modelId": "zeta-prime",
        "provider": "Midland",
        "runIds": [
          35
        ],
        "token": "D"
      }
    ],
    "notes": [
      "Costs are priced at today's catalog cards, as the comparison prices them."
    ],
    "origin": "ModelPack",
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
        "kind": "model",
        "label": "Vega Pro",
        "letter": "A",
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
        "token": "A",
        "total": 2.9999999999999996,
        "unit": "points"
      },
      {
        "kind": "model",
        "label": "Lyra Mini",
        "letter": "B",
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
        "token": "B",
        "total": 8.333333333333334,
        "unit": "points"
      },
      {
        "kind": "model",
        "label": "Nova Lite",
        "letter": "C",
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
        "token": "C",
        "total": 8.333333333333332,
        "unit": "points"
      },
      {
        "kind": "model",
        "label": "Zeta Prime",
        "letter": "D",
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
        "token": "D",
        "total": 5.583333333333333,
        "unit": "points"
      }
    ],
    "pricingBasis": "Priced from the catalog as of 2026-09-20. Comparable across dates; not what was actually spent.",
    "pricingBasisKind": "catalog",
    "profiles": [
      {
        "dimensions": [
          80,
          80,
          80,
          80
        ],
        "flags": [],
        "qualityIndex": 85,
        "strengths": [
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
        "token": "subject",
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
        "letter": "A",
        "qualityIndex": 80,
        "strengths": [
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
        "letter": "B",
        "qualityIndex": 70,
        "strengths": [],
        "token": "B",
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
        "letter": "C",
        "qualityIndex": 70,
        "strengths": [],
        "token": "C",
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
        "letter": "D",
        "qualityIndex": 55,
        "strengths": [],
        "token": "D",
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
        "first": "subject",
        "indexDifference": 5,
        "second": "A"
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
        "first": "subject",
        "indexDifference": 15,
        "second": "B"
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
        "first": "subject",
        "indexDifference": 15,
        "second": "C"
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
        "first": "subject",
        "indexDifference": 30,
        "second": "D"
      }
    ],
    "questions": [
      {
        "axis": "intelligence",
        "comparativeKeys": [],
        "hypothesisKeys": [],
        "neighbor": "A",
        "number": 1,
        "orderEstablished": false,
        "priceCard": false,
        "signals": [
          "tied for first of 5 models on Accuracy",
          "tied for first of 5 models on Completeness"
        ],
        "text": "Why did Orion Max score highest, though its lead over Model A is not established?",
        "token": "subject",
        "withinKeys": [],
        "withinOnly": false
      },
      {
        "axis": "intelligence",
        "comparativeKeys": [],
        "hypothesisKeys": [],
        "neighbor": "C",
        "number": 2,
        "orderEstablished": true,
        "priceCard": false,
        "signals": [
          "lost points on low-scoring answers, mostly Accuracy: 5.6 index points on 3 questions (Q1, Q3 and Q4)",
          "1 isolated miss (Q1)"
        ],
        "text": "Why did Model D score lowest?",
        "token": "D",
        "withinKeys": [],
        "withinOnly": false
      },
      {
        "axis": "speed",
        "comparativeKeys": [
          "HS3.1"
        ],
        "grade": "Moderate",
        "hypothesisKeys": [
          "HS3.1"
        ],
        "number": 3,
        "priceCard": false,
        "signals": [
          "median model time 20.0 s",
          "against Orion Max: output tokens carries 55% of the 6.0 s difference",
          "output generation 59% of its time per question",
          "2.5 model calls per answer"
        ],
        "text": "Why is Model D slowest?",
        "token": "D",
        "withinKeys": [],
        "withinOnly": false
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
          "HC2.2"
        ],
        "number": 4,
        "priceCard": true,
        "signals": [
          "$0.060 per question",
          "uncached input 44% of its cost per question",
          "27% of input tokens read from the cache"
        ],
        "text": "Why is Model C the most expensive?",
        "token": "C",
        "withinKeys": [],
        "withinOnly": false
      }
    ],
    "recommendedRuns": [
      {
        "afterSystemHypotheses": false,
        "allowSourceCodeReferences": true,
        "assessorName": "Gemini 3.8 Flash",
        "decides": "Confirms H0-Q.1 if the paired test still finds no difference with three runs per model; refutes it if a paired test of the two on the Intelligence Index is established after adjustment. Confirms H0-W.1 if the contrasts still find no difference with three runs per model; refutes it if a Mann–Whitney contrast is established after the Holm adjustment. Confirms H0-S.1 if the paired test still finds no difference with three runs per model; refutes it if a paired test of the two on the model time is established after adjustment. Confirms HS3.1, HS3.2 and HS3.3 if the output-token share stays the largest; refutes each if the decode-rate share is larger. Confirms WS3.1 if model time keeps rising with the calls per answer; refutes it if the ρ interval includes zero.",
        "decidesItems": [
          "Confirms H0-Q.1 if the paired test still finds no difference with three runs per model; refutes it if a paired test of the two on the Intelligence Index is established after adjustment.",
          "Confirms H0-W.1 if the contrasts still find no difference with three runs per model; refutes it if a Mann–Whitney contrast is established after the Holm adjustment.",
          "Confirms H0-S.1 if the paired test still finds no difference with three runs per model; refutes it if a paired test of the two on the model time is established after adjustment.",
          "Confirms HS3.1, HS3.2 and HS3.3 if the output-token share stays the largest; refutes each if the decode-rate share is larger.",
          "Confirms WS3.1 if model time keeps rising with the calls per answer; refutes it if the ρ interval includes zero."
        ],
        "estimatedMinutes": 180,
        "hypothesisIds": [
          "H0-Q.1",
          "H0-W.1",
          "H0-S.1",
          "HS3.1",
          "HS3.2",
          "HS3.3",
          "WS3.1"
        ],
        "kind": "replicate",
        "mark": "Recommended",
        "modelNames": [
          "Orion Max",
          "Vega Pro",
          "Zeta Prime",
          "Nova Lite",
          "Lyra Mini"
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
          104,
          103,
          102
        ],
        "verboseMode": false
      }
    ],
    "scope": "Model",
    "speedPairs": [
      {
        "axis": "speed",
        "components": [],
        "difference": -1550,
        "differenceLower": -3803.333333333334,
        "differenceUpper": 670.0000000000009,
        "first": "subject",
        "firstTotal": 7480,
        "notEstablished": true,
        "second": "A",
        "secondTotal": 9030,
        "sharedQuestions": 6,
        "unavailableReason": "Not decomposed: the difference is not established."
      },
      {
        "axis": "speed",
        "components": [
          {
            "amount": 0,
            "key": "callCount",
            "label": "number of model calls",
            "share": -0,
            "shareLower": 0,
            "shareUpper": 0
          },
          {
            "amount": -1250,
            "key": "perCallLatency",
            "label": "latency per call (time to first token)",
            "share": 0.4166666666666667,
            "shareLower": 0.41666666666666646,
            "shareUpper": 0.41666666666666685
          },
          {
            "amount": -1643.3212492183252,
            "key": "outputTokens",
            "label": "output tokens",
            "share": 0.5477737497394417,
            "shareLower": 0.5330607172279814,
            "shareUpper": 0.5621770710553743
          },
          {
            "amount": -106.67875078167481,
            "key": "decodeRate",
            "label": "decode rate",
            "share": 0.0355595835938916,
            "shareLower": 0.02115626227795871,
            "shareUpper": 0.05027261610535221
          },
          {
            "amount": 0,
            "key": "gap",
            "label": "time outside the calls",
            "share": -0,
            "shareLower": -4.3725706816006183E-16,
            "shareUpper": 4.0602442043434285E-16
          }
        ],
        "difference": -3000,
        "differenceLower": -3400,
        "differenceUpper": -2599.999999999999,
        "first": "subject",
        "firstTotal": 7480,
        "notEstablished": false,
        "second": "B",
        "secondTotal": 10480,
        "sharedQuestions": 6
      },
      {
        "axis": "speed",
        "components": [
          {
            "amount": 0,
            "key": "callCount",
            "label": "number of model calls",
            "share": -0,
            "shareLower": -0.37563372811907053,
            "shareUpper": 0.10591689197476477
          },
          {
            "amount": -1880,
            "key": "perCallLatency",
            "label": "latency per call (time to first token)",
            "share": 0.41318681318681316,
            "shareLower": 0.2685704363368654,
            "shareUpper": 0.9352192203470497
          },
          {
            "amount": -2480.6788222305995,
            "key": "outputTokens",
            "label": "output tokens",
            "share": 0.5452041367539779,
            "shareLower": 0.3475635820013107,
            "shareUpper": 1.2744376428533912
          },
          {
            "amount": -189.3211777694005,
            "key": "decodeRate",
            "label": "decode rate",
            "share": 0.041609050059208896,
            "shareLower": -0.8340231350813705,
            "shareUpper": 0.27753861063630864
          },
          {
            "amount": 0,
            "key": "gap",
            "label": "time outside the calls",
            "share": -0,
            "shareLower": -2.807082412879409E-16,
            "shareUpper": 2.3280581103402595E-16
          }
        ],
        "difference": -4550,
        "differenceLower": -7203.333333333334,
        "differenceUpper": -1930,
        "first": "subject",
        "firstTotal": 7480,
        "notEstablished": false,
        "second": "C",
        "secondTotal": 12030,
        "sharedQuestions": 6
      },
      {
        "axis": "speed",
        "components": [
          {
            "amount": 0,
            "key": "callCount",
            "label": "number of model calls",
            "share": -0,
            "shareLower": 0,
            "shareUpper": 0
          },
          {
            "amount": -2500,
            "key": "perCallLatency",
            "label": "latency per call (time to first token)",
            "share": 0.4166666666666667,
            "shareLower": 0.4166666666666666,
            "shareUpper": 0.4166666666666668
          },
          {
            "amount": -3306.6907232213434,
            "key": "outputTokens",
            "label": "output tokens",
            "share": 0.5511151205368906,
            "shareLower": 0.5378380657822239,
            "shareUpper": 0.5641345737176965
          },
          {
            "amount": -193.30927677865657,
            "key": "decodeRate",
            "label": "decode rate",
            "share": 0.03221821279644276,
            "shareLower": 0.019198759615636614,
            "shareUpper": 0.04549526755110954
          },
          {
            "amount": 0,
            "key": "gap",
            "label": "time outside the calls",
            "share": -0,
            "shareLower": -1.900701818158268E-16,
            "shareUpper": 1.7490282726402464E-16
          }
        ],
        "difference": -6000,
        "differenceLower": -6800,
        "differenceUpper": -5199.999999999999,
        "first": "subject",
        "firstTotal": 7480,
        "notEstablished": false,
        "second": "D",
        "secondTotal": 13480,
        "sharedQuestions": 6
      }
    ],
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
        "kind": "model",
        "label": "Vega Pro",
        "letter": "A",
        "segments": [
          {
            "key": "ttft",
            "label": "time to first token",
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
        "token": "A",
        "total": 10.43,
        "unit": "seconds"
      },
      {
        "kind": "model",
        "label": "Lyra Mini",
        "letter": "B",
        "segments": [
          {
            "key": "ttft",
            "label": "time to first token",
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
        "token": "B",
        "total": 11.88,
        "unit": "seconds"
      },
      {
        "kind": "model",
        "label": "Nova Lite",
        "letter": "C",
        "segments": [
          {
            "key": "ttft",
            "label": "time to first token",
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
        "token": "C",
        "total": 13.429999999999998,
        "unit": "seconds"
      },
      {
        "kind": "model",
        "label": "Zeta Prime",
        "letter": "D",
        "segments": [
          {
            "key": "ttft",
            "label": "time to first token",
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
        "token": "D",
        "total": 14.88,
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

*Document ID 131 · format version 18 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Full · peers named*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically for structure, permitted figures and references, word limits, disclosure of benchmark text, peer names, significance claims, interval-overlap wording, hype words and spelling; the checks do not verify the prose's interpretations.*
