---
name: server-benchmark-conditions-and-variants
description: >-
  GnollBench (Overseer AI benchmark) information conditions, scarce corpora, gold evidence and
  instruction variants. Read when a task involves scarce information, a source code only run, a
  closed book run, comparing scarce with plentiful information, how far a model relies on its
  training data instead of its tools, scrubbing the wiki for a benchmark, a scarce or scrubbed
  wiki branch and its .gnollbench-corpus.json marker, gold evidence or an evidence manifest for a
  suite, evidence reach (grounded correct, retrieval failure, navigation failure, read but wrong),
  held-out questions, an instruction variant, an experimental tool such as get_character_info or
  referenced_by, testing a prompt or tool-guide change before production, promoting or retiring a
  variant, or improving the AI without adding wiki data. Covers writing the evidence YAML from a
  rubric's SOURCE section and the wiki, building and auditing a scrubbed branch, the held-out rule,
  reading Evidence Reach in an analysis, and writing and testing a variant.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_benchmark_conditions_and_variants/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.