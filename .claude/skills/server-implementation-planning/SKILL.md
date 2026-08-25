---
name: server-implementation-planning
description: >-
  Full workflow for writing, delivering, and executing implementation plans for
  non-trivial MobileGnollHackLogger and Overseer tasks. Covers when a plan is
  required, the mandatory research-plan-approve-execute-verify lifecycle, plan document
  structure, subagent use, artifact delivery to .plans/, and .plans/ research
  isolation. Read this skill before starting any multi-file or cross-project change.
---

# Implementation Planning Workflow (Claude Code)

The canonical, tool-neutral planning workflow lives in this repository's shared agent directory:
`.agents/skills/server_implementation_planning/SKILL.md` (path relative to the repository root).

**Read `.agents/skills/server_implementation_planning/SKILL.md` in full before proceeding, and follow it.**
The sections below provide mandatory Claude Code-specific harness mechanics and integration instructions.

## Claude Code Plan Mode

Plan mode restricts editing to its own plan file (`~/.claude/plans/<slug>.md`), which
conflicts with the repository's `.plans/` delivery rule. Resolve it like this:

1. **Write** the plan to the harness plan file (`~/.claude/plans/<slug>.md`). In-place
   editing of that file is expected and does not violate the `_v<N>` rule — versioning
   binds to the `.plans/` copy only.
2. **Copy** the finished plan to
   `.plans/YYYY-MM-DD/task_name/implementation_plan_v1.md` (or the next revision
   `implementation_plan_v<N>.md`) — **before** asking for approval, not after. Creating
   the task directory is part of this step.
3. **Print** both paths in chat, plus a brief summary.
4. **Request approval** with the `ExitPlanMode` tool. Do not ask "is this plan okay?" in
   chat text — that is what the tool is for. **Approval is never skipped.**
5. **On approval**, create `task.md`, execute, and finish with `walkthrough.md`.

> [!NOTE]
> **Why step 2 does not violate the "no other file edits" restriction.** That restriction
> exists to stop the agent changing the *project* before approval. Copying the plan to its
> canonical location touches no source file, project file, migration, or generated asset —
> it is the delivery of the planning artifact itself. Make the copy; do not defer it.
> Everything else — `task.md`, source edits, build steps — still waits for approval.

`.plans/` is the **source of truth**; the harness plan file is a working copy. This matters
because sessions hand work to each other: another session reads the latest `_v<N>` from
`.plans/` and writes its next revision there, never looking inside `~/.claude/`.

| Location | Naming | Revising |
|----------|--------|----------|
| Harness plan file | Whatever the harness assigns | Edit **in place** |
| `.plans/` | `<document_name>_v<N>.md` | **Never overwrite** — increment `_v<N>` |

## Claude Code Model Tier Mapping

When specifying subagent models in the Subagent Use section:

- **`inherit` (default)**: Assign to almost all subagent tasks. Gives the subagent the
  orchestrator's full reasoning capability (Claude Sonnet or Opus) for multi-file changes,
  refactoring, reviews, and complex debugging.
- **`haiku` (cheapest tier)**: Reserved strictly for zero-judgment mechanical tasks, such
  as applying an identical, pre-specified string replacement across many files. Any
  context-sensitivity or decision-making requires `inherit`.

## File Writing Tools and Line Endings

**Always use the native `Write` tool.** **Do NOT use shell commands** (`cat << EOF`,
heredoc, `echo`) to author Markdown — the content contains backticks, dollar signs, and
angle brackets that cause shell quoting failures and corrupted output, and shell
redirection produces LF line endings where this Windows working tree needs CRLF.

## Scratch Files

`.agents/AGENTS.md` names an Antigravity-specific scratch path. Claude Code has no such
directory — use the session scratchpad directory Claude Code reports in its own
environment instead. The binding part of that rule still holds: **never** write temporary
files, scratch scripts, or guidance files anywhere inside the repository, including the
repository root. The sole exception is `.plans/`, which is gitignored and is the
intended home for AI-produced documents.
