---
name: server-benchmark-conditions-and-variants
description: >-
  GnollBench (Overseer AI benchmark) information conditions, scarce corpora, gold evidence and
  experimental changes. Read when a task involves scarce information, a source code only run, a
  closed book run, comparing scarce with plentiful information, how far a model relies on its
  training data instead of its tools, scrubbing the wiki for a benchmark, a scarce or scrubbed
  wiki branch and its .gnollbench-corpus.json marker, gold evidence or an evidence manifest for a
  suite, evidence reach (grounded correct, retrieval failure, navigation failure, read but wrong),
  held-out questions, an experimental change (instruction variant), a built-in change folder under
  Overseer/BenchmarkChanges or a change written in Overseer's editor, an experimental tool such as
  get_character_info or referenced_by, testing a prompt or tool-guide change before production,
  combining changes in one run, promoting or retiring a change, or improving the AI without adding
  wiki data. Covers writing the evidence YAML from a rubric's SOURCE section and the wiki, building
  and auditing a scrubbed branch, the held-out rule, reading Evidence Reach in an analysis, and
  writing and testing an experimental change, including asking the developer for Download as folder
  when a run used a change written in Overseer.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_benchmark_conditions_and_variants/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.