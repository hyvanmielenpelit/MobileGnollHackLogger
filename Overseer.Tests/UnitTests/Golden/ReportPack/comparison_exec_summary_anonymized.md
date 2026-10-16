**EXECUTIVE SUMMARY**

# Comparison #12

**Comparison:** 5 models · computed 2026-09-21

*Confidential. Prepared for the model's provider. Questions are described, not quoted.*

- **Date:** 2026-09-28
- **Suite:** GnollHack Core Suite
- **Questions:** 6
- **Models:** 5 models (A to E), identities withheld
- **Pricing basis:** Catalog prices on 2026-09-21

## The result in one sentence

Model A and Model B head the comparison; their intervals overlap, so the order between them is not established.

## The comparison in one paragraph

Model A has the highest Intelligence Index of the 5 models compared.

## Which model to use

For the best answers, choose Model A or Model B; their intervals overlap, so the order between them is not established.

## How they compare

| Model | Intelligence Index | Rank | Median answer time | Cost per question | Critical errors |
|---|---|---|---|---|---|
| Model A (reference) | 85 (82–88) | 1–2 (tied) | 12.0 s | $0.050 | 0 of 6 answers |
| Model B | 80 (77–83) | 1–2 (tied) | 9.0 s | $0.030 | 0 of 6 answers |
| Model C | 70 (66–74) | 3–4 (tied) | 15.0 s | $0.020 | 1 of 6 answers |
| Model D | 70 (64–76) | 3–4 (tied) | 8.0 s | $0.060 | 0 of 6 answers |
| Model E | 55 (50–60) | 5 | 20.0 s | $0.010 | 0 of 6 answers |

*Each model's Intelligence Index is followed by its 95% interval. A rank shown as a span, such as 5–9 (tied), means the model's interval overlaps others', so its rank is the span of places the interval allows. Interval bounds are rounded to whole numbers, so two intervals printed as touching may not overlap; ranks use the unrounded bounds.*

| Model | Accuracy | Completeness | Conciseness | Readability |
|---|---|---|---|---|
| Model A (reference) | 80 | 80 | 80 | 80 |
| Model B | 80 | 80 | 80 | 80 |
| Model C | 65 | 65 | 65 | 65 |
| Model D | 64 | 64 | 64 | 64 |
| Model E | 50 | 50 | 50 | 50 |

- **Accuracy:** From Model E at 50 to Model A at 80, 30 points apart
- **Completeness:** From Model E at 50 to Model A at 80, 30 points apart
- **Conciseness:** From Model E at 50 to Model A at 80, 30 points apart
- **Readability:** From Model E at 50 to Model A at 80, 30 points apart

## Compared with the reference model

**Reference:** Model A, the highest Intelligence Index.

| Model | Paired difference (95% interval) | Better / similar / worse | May mislead per 20 | Time ratio | Cost ratio |
|---|---|---|---|---|---|
| Model A (reference) | — | — | 0 | 1.00× | 1.00× |
| Model B | 0.0 (-21.0 to +21.0) | 1 / 5 / 0 | 0 | 0.75× | 0.60× |
| Model C | -15.2 (-23.8 to -6.6) | 0 / 3 / 3 | 3.3 | 1.25× | 0.40× |
| Model D | -15.8 (-26.1 to -5.6) | 0 / 1 / 5 | 0 | 0.67× | 1.20× |
| Model E | -30.0 (-43.9 to -16.1) | 0 / 1 / 5 | 0 | 1.67× | 0.20× |

*Paired difference: the mean per-question difference, the model minus the reference, on the questions both answered, with its 95% interval. Better, similar and worse count the questions both scored on the same item revision on which the model scored more than 15 points above the reference, within 15 points of it, or more than 15 points below it; they are not tests. May mislead per 20: answers with an accuracy level below 4 or a critical error, per 20 answers. Time and cost ratios: the model's median answer time and cost per question divided by the reference's.*

The table sets every other model against Model A; only the paired tests and the intervals decide where an order is established.

## Also worth knowing

- Model B scored higher than Model D at 0.50 times its cost per question, established by 95% intervals that do not overlap.
- Model D scored higher than Model E at 0.40 times its median answer time, established by 95% intervals that do not overlap.
- Model B scored higher than Model E at 0.45 times its median answer time, established by 95% intervals that do not overlap.

## Model by model

**Model A**

- Model A scored 85 / 100 on intelligence.

**Model B**

- Model B scored 80 / 100 on intelligence.

**Model C**

- Model C scored 70 / 100 on intelligence.

**Model D**

- Model D scored 70 / 100 on intelligence.

**Model E**

- Model E scored 55 / 100 on intelligence.

## Trade-offs

The intelligence against cost frontier holds Models A, B, C and E.

On the Pareto frontier (no other model is at least as good on both measures and better on one):

- **Intelligence against cost:** Models A, B, C and E
- **Intelligence against speed:** Models A, B and D
- **Speed against cost:** Models B, C, D and E

## How reliable this is

The graders agreed on most answers.

Of the 10 pairs of models, 2 have overlapping 95% intervals; the intervals alone do not establish the order within such a pair.

Model A, the highest Intelligence Index, was compared with each other model on the questions both answered (Holm-adjusted across 4 tests); 0 of 4 comparisons establish which scored higher.

Every pair of models was compared on the questions both answered (Holm-adjusted across 10 tests); 0 of 10 comparisons establish which scored higher.

## About this benchmark

GnollHack is a roguelike game descended from NetHack. The Overseer is its AI assistant: players ask it questions about the game, and it answers with the help of tools that search the game's source code, its wiki and a knowledge base.

This benchmark gives the production assistant prompt and tools, unchanged, a fixed set of single-turn questions about the game, and asks for the concise answer style the live assistant uses. Every model compared here answered the same questions under the same configuration.

An AI grader scores every answer for accuracy, completeness, conciseness and readability, weighted Accuracy 55%, Completeness 25%, Conciseness 10%, Readability 10%. Speed is each model's own time per answer, with time spent in tools excluded. Cost is each model's price per question at the catalog prices of 2026-09-21, which is comparable across dates but is not what was actually spent.

What it does not measure: conversations longer than one question, the wiki text the live assistant is given before it answers, spoiler-free mode, web search, and delegation to subagents. A result here describes the assistant as configured for this benchmark; it may not carry over to those situations.

## Evaluation terms

- **Purpose statement:** Internal evaluation of candidate AI models for the Overseer assistant within GnollHack.
- **Distillation / training prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.
- **Third-party model content:** Outputs generated by the compared models (identities withheld), graded by models from Google, and described in this document by **Claude Opus 5.5** (Anthropic) are third-party content evaluated solely for domain-specific benchmark scoring and operational model selection.

---

*Document ID 212 · format version 23 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Summary · models anonymized*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically for structure, permitted figures and references, word limits, disclosure of benchmark text, peer names, significance claims, interval-overlap wording, hype words and spelling; the checks do not verify the prose's interpretations.*
