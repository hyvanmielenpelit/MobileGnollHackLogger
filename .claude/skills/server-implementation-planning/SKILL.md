---
name: server-implementation-planning
description: >-
  Full workflow for writing, delivering, and executing implementation plans for
  non-trivial MobileGnollHackLogger and Overseer tasks. Covers when a plan is
  required, the mandatory research-plan-approve-execute-verify lifecycle, plan document
  structure, subagent use, artifact delivery to .plans/, and .plans/ research
  isolation. Read this skill before starting any multi-file or cross-project change.
---

# Server Implementation Planning Workflow (Claude Code Pointer)

The canonical repository-specific planning overlay lives in:
`.agents/skills/server_implementation_planning/SKILL.md` (path relative to repository root).

**Read `.agents/skills/server_implementation_planning/SKILL.md` in full before proceeding, and follow it.**

## General Planning Baseline & Claude Code Plan Mode

The general 5-phase lifecycle, Subagent Use rules, and Claude Code plan-mode mechanics (e.g. `~/.claude/plans/<slug>.md` copy to `.plans/`, `ExitPlanMode` workflow, and `inherit`/`haiku` model tier mappings) are provided by the global **`agent-implementation-planning`** skill.
