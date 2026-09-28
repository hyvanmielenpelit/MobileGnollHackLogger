# GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Executive Summary

*INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.*

- **Date:** 2026-09-28
- **Suite:** GnollHack Core Suite
- **Questions:** 4
- **Runs:** 1 (run 12)
- **Peers:** Model A = Grok 5 (xAI, grok-5); Model B = Mistral Large 4 (Mistral, mistral-large-4)

## The result in one sentence

GPT-5.6 Luna answers everyday GnollHack questions well but made one confident false claim about item destruction.

## Key figures

- **Intelligence:** 80 ± 3 / 100, 2nd of 3; its 95 % interval overlaps those of Models A and B.
- **Speed:** median answer time 12.3 s, 2nd of 2.
- **Cost:** $0.036 per question, 2nd of 3.
- **Serious errors:** 1 of 4 answers.

## What it did well

- Answers simple questions precisely and briefly. *(One grader — different family)*

## Where it fell short

- Asserted a false outcome on Q3, where Grok 5 scored well. *(Both graders)*
- Scored lowest on intermediate questions (49). *(Computed)*

## What this means for use as a game assistant

GPT-5.6 Luna is a capable assistant for everyday play, but its answers on item destruction need a source check.

## How confident are we

The result rests on 4 questions; its 95 % interval overlaps those of Models A and B.

Testing every pair among these 3 models at once would flag chance differences as significant, so this view tests none. Put each model's runs in an analysis group, open one in the Multi-Run Analysis tab and choose the other under Compare with group.

## About this benchmark

GnollHack is a roguelike game descended from NetHack. The Overseer is its AI assistant: players ask it questions about the game, and it answers with the help of tools that search the game's source code, its wiki and a knowledge base.

This benchmark gives the production assistant prompt and tools, unchanged, a fixed set of single-turn questions about the game, and asks for the concise answer style the live assistant uses.

Two AI graders from two different companies score every answer for accuracy, completeness, conciseness and readability, weighted Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %. A separate verifier checks disputed claims against the game's source code. Speed is the model's own time per answer, with time spent in tools excluded. Cost is list price for the model under test, on this basis: Priced from the catalog as of 2026-09-20. Comparable across dates; not what was actually spent.

What it does not measure: conversations longer than one question, the wiki text the live assistant is given before it answers, spoiler-free mode, web search, and delegation to subagents. A result here describes the assistant as configured for this benchmark; it may not carry over to those situations.

## Removed content

Automatic validation removed these items from the writer's output before it was stored:

- `weaknesses[2]` (rule 4): Cited R9, which does not exist.

---

*Report 101 · format version 1 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Full · peers named*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically.*
