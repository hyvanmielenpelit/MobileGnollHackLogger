---
name: server-benchmark-tool-diagnostics
description: >-
  How to read an Overseer AI benchmark run as a diagnostic instrument for the production chat
  agent's tool layer, rather than as a scoreboard. Answers "did the AI have access to the wiki
  or the source at all", "why did this tool return nothing", "did the tool actually work or was
  there a technical problem". Covers what a run records about its tool calls and what it
  discards, deriving the hidden count of technically failed tool calls, the five verdicts for a
  tool's call population, the four-rung ladder that separates "no access" from "no data" from
  "a broken tool", the corpus fingerprints a run records and the two reachable corpora it does
  not, reconstructing the arguments and results the run never stored, the three replay fidelity
  tiers, the parameter and result comparison checklist, and the diagnostic table a tool-layer
  analysis must produce. Read before attributing any benchmark finding to a tool, a corpus, or
  the absence of game data.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_benchmark_tool_diagnostics/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.