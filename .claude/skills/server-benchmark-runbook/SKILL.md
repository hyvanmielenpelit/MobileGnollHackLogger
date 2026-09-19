---
name: server-benchmark-runbook
description: >-
  Mandatory closing deliverable of every Overseer AI benchmark analysis: the Developer Runbook
  (developer_runbook_v<N>.md) that tells the developer exactly what to do next and in which
  order. Covers the dependency order in which a round's fixes must be applied (plan execution,
  GnollHack plan, wiki handoff, knowledge-base push, Overseer restart, rubric repair import and
  difficulty re-assessment, grader roster or profile changes), the rule that the agent
  implements the whole plan uninterrupted and the developer's manual jobs come at the very end,
  the special-case mid-plan pause with its proof that the solution compiles and its explicit
  "not finished" message, exports at the start and imports at the end, the step-card format that makes
  every step followable with no other document open, the run cards that specify each following
  benchmark run field by field as the launcher shows it, the purposes a run may serve in
  their binding order of importance (first improving the main Overseer chat, then the
  benchmarking system, then deciding which models perform best as the Overseer AI on
  intelligence, speed and cost, with validation of the round's fixes taking the rank of what it
  validates), predicting the
  comparability tier of a run before it is made, model-selection run design, cost and wall-time
  estimates, what the developer hands back after a run, and the self-check before handoff. Read
  once a benchmark analysis has triaged its findings and before its implementation plan is
  written; read whenever a user asks "what do I do next", "which runs should I make", "in what
  order do I apply these fixes", or "which model should Overseer use".
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_benchmark_runbook/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.