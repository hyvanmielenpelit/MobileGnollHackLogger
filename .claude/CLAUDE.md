@../.agents/AGENTS.md

## Claude Code

This project keeps its AI configuration in the tool-neutral `.agents/` directory so
that agents other than Claude can be pointed at the same files. `.claude/` is only a
thin adapter layer over it.

- **Project rules**: `.agents/AGENTS.md`, imported at the top of this file. Edit the
  rules there, not here.
- **Skills**: the full bodies of project-specific skills live in
  `.agents/skills/<name>/SKILL.md`, each with a matching pointer stub in
  `.claude/skills/`. The stub contract, and why stubs are regenerated rather than
  hand-edited, are in the global `claude-code-conventions` skill.
- **Naming**: canonical folders use underscores (`scss_compilation`); the invocable skill
  name is the hyphenated form (`scss-compilation`). Skill bodies cross-reference each
  other by the underscore folder name — that is correct and refers to the canonical file.
- **The `server_` prefix**: skill names share one flat global namespace across projects,
  user-level skills, and plugins. A generic `implementation-planning` would collide with
  the GnollHack repository's skill of that name, and the loser of such a collision is
  silently never loaded. Project skills here take `server_` (or `overseer_` where they are
  specific to that client); GnollHack uses `client_`.
- **Global skills** are installed at the user level from `hyvanmielenpelit/SharedAgentSkills`
  via its `setup.ps1`, and have no in-repository bodies:
  `agent-implementation-planning`, `agent-subagent-guidelines`,
  `agent-powershell-guidelines`, plus the Claude-only `claude-plan-mode` and
  `claude-code-conventions`. See that repository's `docs/ai-skill-management.md`.

## Shell and Commands (Windows)

Development happens on **Windows**. Claude Code's primary shell here is PowerShell; a Git
Bash `Bash` tool may also be available, and they are not interchangeable. **Default to
PowerShell.**

The full rules — PowerShell 5.1 parser limits, quoting, encoding, BOM prevention,
line-ending detection, non-interactive execution, and Unix-command substitutions — are in
the global **`agent-powershell-guidelines`** skill. Read it rather than a summary.

Repository-specific note: `.gitattributes` sets `* text=auto`, so the repository stores
**LF** and a Windows working tree holds **CRLF**. Write CRLF and let Git normalize on
commit. When modifying an existing file, match what it already uses.

## Implementation Plans and AI Documents

**Non-trivial tasks require a written implementation plan, approved by the user before
any source file is modified.** Read the `server-implementation-planning` skill for this
repository's build boundaries, and the global `agent-implementation-planning` skill for
the lifecycle, plan format, `.plans/` naming, and versioning.

### When to Plan

A plan is **required** when any of these are true:

- The task touches **more than one file**, or more than one project
  (`MobileGnollHackLogger`, `Overseer`, `GnollHackServer.Data`, `Overseer.Tests`)
- It requires an **EF Core migration**
- It requires **SCSS-to-CSS recompilation** or an **Angular rebuild**
- It is a refactor, a new feature, or anything the user describes as non-trivial

A plan is **not** required for single-file fixes, typos, comment edits, answering
questions, or read-only investigation.

### Claude Code Plan Mode

Plan mode restricts editing to its own plan file, which conflicts with the `.plans/`
rule. The reconciliation — write to the harness plan file, copy to `.plans/` **before**
requesting approval, then `ExitPlanMode` — is in the global **`claude-plan-mode`** skill.
