# Comparison #12 — Spring model sweep: Executive Summary

**Comparison:** Comparison #12 — Spring model sweep · 5 models · computed 2026-09-21

*INTERNAL — unpublished benchmark results. Do not share outside the Overseer team.*

- **Date:** 2026-09-28
- **Suite:** GnollHack Core Suite
- **Questions:** 6
- **Models:** Orion Max, Vega Pro, Lyra Mini, Nova Lite and Zeta Prime
- **Pricing basis:** Catalog prices on 2026-09-21

## The result in one sentence

Orion Max and Vega Pro head the comparison; their intervals overlap, so the order between them is not established.

## The comparison in one paragraph

Orion Max has the highest Intelligence Index of the 5 models compared.

## Which model to use

For the best answers, choose Orion Max or Vega Pro; their intervals overlap, so the order between them is not established.

## How they compare

| Model | Provider | Intelligence Index | Rank | Median answer time | Cost per question | Critical errors |
|---|---|---|---|---|---|---|
| Orion Max | Northwind | 85 (82–88) | joint 1 | 12.0 s | $0.050 | 0 of 6 answers |
| Vega Pro | Southstar | 80 (77–83) | joint 1 | 9.0 s | $0.030 | 0 of 6 answers |
| Lyra Mini | Eastgate | 70 (66–74) | joint 3 | 15.0 s | $0.020 | 1 of 6 answers |
| Nova Lite | Westlake | 70 (64–76) | joint 3 | 8.0 s | $0.060 | 0 of 6 answers |
| Zeta Prime | Midland | 55 (50–60) | 5 | 20.0 s | $0.010 | 0 of 6 answers |

*Each model's Intelligence Index is followed by its 95 % interval; a joint rank means its interval overlaps a neighbor's, so the order between them is not established by the intervals.*

| Model | Provider | Accuracy | Completeness | Conciseness | Readability |
|---|---|---|---|---|---|
| Orion Max | Northwind | 80 | 80 | 80 | 80 |
| Vega Pro | Southstar | 80 | 80 | 80 | 80 |
| Lyra Mini | Eastgate | 65 | 65 | 65 | 65 |
| Nova Lite | Westlake | 64 | 64 | 64 | 64 |
| Zeta Prime | Midland | 50 | 50 | 50 | 50 |

- **Accuracy:** from 50 (Zeta Prime) to 80 (Orion Max), 30 points apart
- **Completeness:** from 50 (Zeta Prime) to 80 (Orion Max), 30 points apart
- **Conciseness:** from 50 (Zeta Prime) to 80 (Orion Max), 30 points apart
- **Readability:** from 50 (Zeta Prime) to 80 (Orion Max), 30 points apart

## Model by model

**Orion Max**

- Orion Max scored 85 / 100 on intelligence.

**Vega Pro**

- Vega Pro scored 80 / 100 on intelligence.

**Lyra Mini**

- Lyra Mini scored 70 / 100 on intelligence.

**Nova Lite**

- Nova Lite scored 70 / 100 on intelligence.

**Zeta Prime**

- Zeta Prime scored 55 / 100 on intelligence.

## Trade-offs

The intelligence against cost frontier holds Orion Max, Vega Pro, Lyra Mini and Zeta Prime.

On the Pareto frontier (no other model is at least as good on both measures and better on one):

- **Intelligence against cost:** Orion Max, Vega Pro, Lyra Mini and Zeta Prime
- **Intelligence against speed:** Orion Max, Vega Pro and Nova Lite
- **Speed against cost:** Vega Pro, Lyra Mini, Nova Lite and Zeta Prime

## How reliable this is

The graders agreed on most answers.

Of the 10 pairs of models, 2 have overlapping 95 % intervals; the intervals alone do not establish the order within such a pair.

Orion Max, the highest Intelligence Index, was compared with each other model on the questions both answered (Holm-adjusted across 4 tests); 0 of 4 comparisons establish which scored higher.

Every pair of models was compared on the questions both answered (Holm-adjusted across 10 tests); 0 of 10 comparisons establish which scored higher.

## About this benchmark

GnollHack is a roguelike game descended from NetHack. The Overseer is its AI assistant: players ask it questions about the game, and it answers with the help of tools that search the game's source code, its wiki and a knowledge base.

This benchmark gives the production assistant prompt and tools, unchanged, a fixed set of single-turn questions about the game, and asks for the concise answer style the live assistant uses. Every model compared here answered the same questions under the same configuration.

An AI grader scores every answer for accuracy, completeness, conciseness and readability, weighted Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %. Speed is each model's own time per answer, with time spent in tools excluded. Cost is each model's price per question at the catalog prices of 2026-09-21, which is comparable across dates but is not what was actually spent.

What it does not measure: conversations longer than one question, the wiki text the live assistant is given before it answers, spoiler-free mode, web search, and delegation to subagents. A result here describes the assistant as configured for this benchmark; it may not carry over to those situations.

## Evaluation terms

- **Purpose statement:** Internal evaluation of candidate AI models for the Overseer assistant within GnollHack.
- **Distillation / training prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.
- **Third-party model content:** Outputs generated by **Orion Max** (Northwind), **Vega Pro** (Southstar), **Lyra Mini** (Eastgate), **Nova Lite** (Westlake) and **Zeta Prime** (Midland), graded by models from Google, and described in this document by **Claude Opus 5.5** (Anthropic) are third-party content evaluated solely for domain-specific benchmark scoring and operational model selection.

---

*Document ID 212 · format version 12 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Full · models named*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically for structure, permitted figures and references, word limits, disclosure of benchmark text, peer names, significance claims, interval-overlap wording, hype words and spelling; the checks do not verify the prose's interpretations.*
