---
name: server_implementation_planning
description: >-
  Full workflow for writing, delivering, and executing implementation plans for
  non-trivial MobileGnollHackLogger and Overseer tasks. Covers when a plan is
  required, the mandatory research-plan-approve-execute-verify lifecycle, plan document
  structure, subagent use, artifact delivery to .plans/, and .plans/ research
  isolation. Read this skill before starting any multi-file or cross-project change.
---

# Server Implementation Planning Workflow

## Purpose

This skill defines the **mandatory planning workflow** for non-trivial changes to this
repository. It ensures that complex work is researched, documented, user-approved, and
verified — preventing wasted effort from wrong-direction changes.

The repository spans four projects and several generated-artifact boundaries:
- `MobileGnollHackLogger` (ASP.NET Core host and Razor Pages)
- `Overseer` (Angular client SPA frontend)
- `GnollHackServer.Data` (Entity Framework Core database models and migrations)
- `Overseer.Tests` (xUnit test suite)

A change that looks local often is not: a model added to a catalog file affects the Angular
client, a database model change requires an EF Core migration in a different project than the
one being edited, and a style tweak is worthless unless the generated CSS is recompiled.
Planning is what catches that before any source file is edited.

## When a Plan Is Required

A written implementation plan is **required** when the task meets **any** of these
criteria:

- It touches **more than one file**, or more than one project (`MobileGnollHackLogger`,
  `Overseer`, `GnollHackServer.Data`, `Overseer.Tests`)
- It requires an **EF Core migration**
- It requires **SCSS-to-CSS recompilation** or an **Angular client rebuild**
- It is a refactor, a new feature, or anything the user describes as large or non-trivial

A plan is **not** required for:

- Single-file bug fixes
- Typo and comment corrections
- Answering questions or read-only investigation
- Minor follow-ups **while executing** an already-approved plan

**When in doubt, write a plan.** A rejected plan is cheap; a wrong cross-project change
is not.

## The Five-Phase Lifecycle

Every planned task follows these phases in strict order:

### Phase 1 — Research

- Use search and file-reading tools to understand the affected code, its dependencies,
  and the implications of changing it.
- **Do NOT modify any file during this phase.** Read-only operations only.
- **Do NOT read documents from the `.plans/` directory during research** — see
  "`.plans/` Isolation During Research" below.
- Take notes as you go. Those notes become the input to the plan.

### Phase 2 — Write the Implementation Plan

- Create the plan as a **Markdown file** saved inside the repository under
  `.plans/` (which is gitignored). See "Where to Save Plans and Other Documents" below.
- Put any open questions or design decisions the user needs to weigh in on **directly in
  the plan document**, so they are answered before execution rather than during it.
- If the harness confines you to its own plan file or artifact directory while planning,
  write the plan there and then **copy it to `.plans/` before requesting approval** — see
  "Harness Rules Take Precedence" below.
- Present the plan to the user for review.

### Phase 3 — Obtain User Approval

- **STOP and wait for the user's explicit approval before editing any file.** This is a
  hard gate.
- **Always print the plan's `.plans/` path in chat** so the user can open it — plus the
  harness file's path, if the harness keeps one.
- **How to request approval**: if the harness provides an explicit approval mechanism
  (e.g., Claude Code `ExitPlanMode` or Antigravity review prompt), use it. Otherwise,
  print a brief summary of the plan — not the full document — and wait. **Approval is
  never skipped.**
- Do not begin implementation alongside the plan, do not "get a head start", and do not
  pre-create files "to save time."
- If the user requests changes, revise the plan and re-present it.

### Phase 4 — Execute

- Once approved, implement the plan step by step.
- Track progress with a task checklist (see "Progress Tracking" below).
- **If you discover something that requires significant deviation from the approved
  plan**, stop and update the plan document, then present the revision for re-approval
  before continuing.
- Do not silently diverge from what the user approved.

### Phase 5 — Verify

- Confirm the changes have the intended effect:
  - Build the affected projects (`dotnet build`), and the Angular client if it was
    touched
  - Run the test suite if it covers the change (`dotnet test` against `Overseer.Tests`)
  - Recompile SCSS and confirm the generated CSS changed as expected
  - Apply and check any new migration
- Create a **walkthrough document** summarizing what changed, what was tested, and the
  results.

## Plan Document Structure

Use this Markdown template. Omit sections that are not relevant, but always include the
ones marked **mandatory**.

```markdown
# [Goal Description]

The problem, the background context, and what the change accomplishes.

## User Review Required                          ← if applicable
Decisions the user must make: breaking changes, design trade-offs, data-loss risk.
Use callouts (e.g. `> [!WARNING]`) for critical items.

## Open Questions                                ← if applicable
Design or clarifying questions that affect the plan.

## Affected Files                                ← mandatory
| File | Project | Change |
|------|---------|--------|
| `Overseer/Services/FooService.cs` | Overseer | Add `BarAsync()` |
| `GnollHackServer.Data/Models/Foo.cs` | GnollHackServer.Data | Add `Bar` column |

## Build Impact                                  ← mandatory
Which regeneration steps this triggers — SCSS compilation, EF Core migration,
Angular build — or "None".

## Proposed Changes                              ← mandatory
Grouped by component, ordered by dependency (regeneration prerequisites first).
Mark each file `[NEW]`, `[MODIFY]`, or `[DELETE]` and say what changes and why.

## Subagent Use                                  ← mandatory
### Subagents Needed
[Yes / No — if no, explain why]

### Subagent Assignments
| Task | Model | Files | Rationale |
|------|-------|-------|-----------|

### Human Assignments (if any)
| Task | Rationale | Fallback if Not Approved |
|------|-----------|--------------------------|

## Risks                                         ← mandatory
What could break, and how it would be noticed.

## Verification Plan                             ← mandatory
### Automated
- Commands to run.

### Manual Verification
- Steps the user should take.
```

### Key Structural Rules

1. **Affected Files table** — every file the plan touches must be listed.
2. **Build Impact** — explicitly state which regeneration steps are triggered, or
   "None". See "Build Impact for This Repository" below.
3. **Subagent Use** — mandatory even when no subagents are needed. State "No" and
   explain why.
4. **Risks** — do not skip this. Even a low-risk change should say what to watch for.
5. **Proposed Changes** — order by dependency. A regeneration boundary must fall
   **between** steps, never inside one.

### Scaling the Format

The full format applies to **non-trivial** work. If the harness asks for a "concise"
plan, that means *no excessive verbosity within this format* — keep every mandatory
section, but keep each one tight. Never trim the Affected Files table.

For **truly trivial** work, the harness's own concise format (or no plan at all) is
acceptable. Say in chat that the shortcut was taken because the task was trivial. If the
user then asks for the full format, produce it as the next revision (`_v<N+1>`).

## Build Impact for This Repository

These are the generated-artifact boundaries a plan must account for. A plan step must
never straddle one — finish the source change, cross the boundary, then continue.

### SCSS to CSS

`MobileGnollHackLogger/wwwroot/css/site2.scss` generates **both**
`site2.css` and `site2.min.css`. **Never edit the generated CSS directly** when an SCSS
source exists. Both outputs are regenerated:

```bash
npx sass wwwroot/css/site2.scss wwwroot/css/site2.css
```

```bash
npx sass wwwroot/css/site2.scss wwwroot/css/site2.min.css --style compressed
```

A plan that changes styling must list the two generated CSS files in Affected Files and
name SCSS compilation under Build Impact.

### EF Core Migrations

Migrations target the **`GnollHackServer.Data`** project, not the project being edited,
and applying a migration is a **separate second command** — generating it does not touch
the database:

```bash
dotnet ef migrations add <MigrationName> -p GnollHackServer.Data -s MobileGnollHackLogger -o Migrations
```

```bash
dotnet ef database update -p GnollHackServer.Data -s MobileGnollHackLogger
```

A plan that changes a database model must list the generated migration files, name the
migration under Build Impact, and sequence dependent code **after** the migration step.

### Angular Client

The `Overseer` SPA is built separately from the ASP.NET Core host. Changes to TypeScript,
component templates, or component styles require a client rebuild before they are
visible, and shipping a release build also involves uploading source maps to Sentry as
its own step.

### Images

New or replaced raster assets are converted to **WebP at quality 85**.

### Publishing

**Never publish** (`dotnet publish` or equivalent) unless the user explicitly asks. A
plan must not include a publish step on its own initiative.

## Subagent Use

Every implementation plan **must** contain a Subagent Use section, even when the answer
is "No" — in which case say why (for example, "single-file change, not worth the
overhead").

### Model Tier

**Default to the orchestrator's own tier** (`inherit`) for essentially every subagent
task. If a task is worth spawning a subagent for, it is worth giving that subagent the
orchestrator's full reasoning capability. This covers multi-file changes, implementing
features from existing patterns, researching subsystems, reviewing code, debugging, and
refactoring.

**Reserve the cheapest tier** (`flash` / `haiku`) for work that is
trivially mechanical with **zero judgment**: applying one identical, pre-specified text
replacement across many files, or inserting the same boilerplate line repeatedly. The
criterion is that the subagent does not decide *what* to change, only *where* to paste an
already-specified string. Any ambiguity or context-sensitivity means `inherit`.

### Agent Type

Type and tier are independent axes — the tier is how capable the subagent is, the type is
what it is allowed to do.

| Agent type | Use for | Can edit files? |
|------------|---------|-----------------|
| `Explore` / `Research` | Read-only fan-out search across many files | No |
| `Plan` | Design work from research already gathered | No |
| `general-purpose` / `Coder` | Execution — the actual changes an approved plan calls for | Yes |

Read-only research agents that the harness prescribes **during planning** are not what
the Subagent Use section governs. They run before the plan exists and need no approval.
The section governs **execution-phase** subagents: the ones that edit files after
approval.

### Planning Constraints

- **File-level exclusivity (strict).** No two agents — including the orchestrator — may
  edit the same file concurrently. Assign each agent a non-overlapping set of files; if
  two tasks touch the same file, **sequence** them instead of parallelizing.
- **Respect regeneration boundaries.** Do not parallelize across an EF Core migration, an
  SCSS compilation, or an Angular rebuild. Work that depends on a generated artifact must
  wait for the step that generates it.
- **Communication overhead.** For work that takes under 30 seconds to do directly,
  spawning a subagent is slower than doing it yourself.
- **Never overwrite uncommitted changes** without explicit user permission. This includes
  restoring a file to an earlier version or regenerating its contents wholesale. If the
  planned work risks losing uncommitted edits — the user's or a previous agent's, which
  cannot always be distinguished — ask the user to **commit first** (preferred) or to
  explicitly accept the risk, and only then proceed.

### Human Assignments

Assigning work to the user is the **rare exception**. The threshold is high: the task
must be one where AI failure is likely and the token cost of retries substantial. The
main case is a **very extensive cut-and-paste move** — relocating a large block (50+
lines), especially across files — which a human does atomically in the editor and an AI
frequently botches. For small moves or straightforward find-and-replace, do it yourself.

If the user declines a human-assigned task, the orchestrator handles it directly.

## Where to Save Plans and Other Documents

All AI-produced documents — implementation plans, reviews, analyses (including bug
analyses), reports, and any other structured artifacts — are saved **inside the
repository** under the `.plans/` directory, which is gitignored (see `.gitignore`, under
`# AI Agent Plans`). Note that this is the **only** permitted in-repository location for
AI-produced files: `.agents/AGENTS.md` otherwise prohibits writing temporary or guidance
files anywhere in the repository.

```
.plans/
  YYYY-MM-DD/
    task_name/
      implementation_plan_v<N>.md       ← N=1 for the first version
      code_review_v<N>.md               ← example: a review document
      bug_analysis_v<N>.md              ← example: an analysis document
      task.md                           ← single file, based on the approved plan
      walkthrough.md                    ← single file, post-completion summary
      implementation_review_A_v<N>.md   ← follow-up round A
      task_A.md                         ← follow-up A task checklist
      walkthrough_A.md                  ← follow-up A walkthrough
```

### Directory Naming Rules

- **Date directory** (`YYYY-MM-DD`): the creation date of the **first document** for the
  topic. Follow-up documents created on later dates go in the **same** date directory as
  the original.
- **Task directory**: a short, descriptive `snake_case` name (e.g., `chat_page_update`,
  `add_gemini_model`, `retention_policy_change`).
- **Create subdirectories** as needed — they will not exist the first time.
- **Conflict resolution**: every new task (not a follow-up round) gets its **own new
  folder**. Before creating one, check whether the desired name already exists under the
  same date. If it does, find the next free name in the sequence `task_name`,
  `task_name_2`, `task_name_3`, ... — always building from the **base name**, never
  appending a suffix to an already-suffixed name (`task_name_2_3` is wrong; the name
  after `task_name_2` is `task_name_3`). Never rename the existing folder, and do not
  read or modify it.

> [!IMPORTANT]
> **Three distinct suffix types — do not confuse them:**
>
> | Suffix | Applies to | Meaning | Example |
> |--------|-----------|---------|---------|
> | `_2`, `_3`, ... | **Folder** names | Conflict resolution for separate tasks with the same name | `chat_page_update_2/` |
> | `_A`, `_B`, ... | **File** names | Follow-up round within the same task folder | `implementation_review_A_v1.md` |
> | `_v1`, `_v2`, ... | **File** names | Document revision (never overwrite, always increment) | `implementation_plan_v2.md` |

### Document Versioning (STRICT)

This applies to **all** document types saved in `.plans/`.

1. **First version**: always use the `_v1` suffix.
2. **Never overwrite an existing version.** To revise, create a **new file** with the
   next version number (read `_v1` → write `_v2`).
3. **Determine the next version** by checking which files already exist. The new file
   gets the highest existing number plus one.
4. **Do not delete or modify** older versions. They form a revision history that lets the
   user compare approaches across different sessions or different AIs.

**Exception — `task.md` and `walkthrough.md`**: these are **singular** files with no
version suffix. There is one of each per task, based on whichever plan version was
ultimately approved and implemented. Follow-up rounds get lettered variants.

### How to Write Files

**Always use the agent's native file-writing tool** (e.g., `Write`, `write_to_file`,
`create_file`). **Do NOT use shell commands** (`cat << EOF`, heredoc, `echo`) to author
Markdown — the content contains backticks, dollar signs, and angle brackets that cause
shell quoting failures and corrupted output, and shell redirection produces LF line endings
where this Windows working tree needs CRLF.

## Harness Rules Take Precedence

Agent harnesses impose their own planning workflows, and several of them restrict where you
may write. **The harness rules always win** — this skill is guidance layered inside whatever
the harness permits, never an override of a harness restriction.

### `.plans/` Is the Source of Truth

A harness may keep its own private plan file (Claude Code plan mode writes to
`~/.claude/plans/<slug>.md`; Antigravity uses an artifact directory). Treat that file as a
**working/backup copy**. The canonical document is always the one in
`.plans/YYYY-MM-DD/task_name/`.

This matters because agents hand work to each other. A different AI picking up the task
reads the **latest `_v<N>` from `.plans/`** and writes its next revision **to `.plans/`** —
it never looks inside a harness-private directory.

### When to Make the `.plans/` Copy

**As soon as the plan is finished, and immediately before requesting approval.** The copy
is part of delivering the plan, not part of executing it.

Order of operations: finish writing the plan → **copy to `.plans/`** → print the path →
request approval via the harness mechanism (or a chat summary).

> [!NOTE]
> **This copy does not violate a harness "no other file edits" restriction.** Such
> restrictions exist to stop the agent changing the *project* before approval. Copying
> the plan to its canonical location touches no source file, project file, migration, or
> generated asset — it is the delivery of the planning artifact itself. Make the copy;
> do not defer it to execution.

Everything else still waits for approval: `task.md`, source edits, build steps.

### Versioning Is Per-Location

| Location | Naming | Revising |
|----------|--------|----------|
| Harness-private plan file / artifact | Whatever the harness assigns | Edit **in place** |
| `.plans/` | `<document_name>_v<N>.md` | **Never overwrite** — increment `_v<N>` |

### Agent-Specific Notes

- **Antigravity / Gemini Agents**: First create the artifact in the artifact directory
  (`<appDataDir>/brain/<conversation-id>/`) so the UI presents it, then **also copy it**
  to `.plans/YYYY-MM-DD/task_name/<document_name>_v<N>.md`. Both locations must receive the
  file. Request approval via the artifact review mechanism and wait for the user to
  approve before proceeding.
- **Claude Code**: Write to the harness plan file (`~/.claude/plans/<slug>.md`), then copy
  to `.plans/YYYY-MM-DD/task_name/implementation_plan_v<N>.md` before requesting approval via
  `ExitPlanMode`.
- **Other agents**: Same path convention. The `.plans/` directory is gitignored, so
  documents will not be committed unless the user explicitly adds them.

## Progress Tracking

After approval, create a **task checklist** (`task.md`) and update it as you work through
each step. There is one `task.md` per task, based on the approved plan version.

```markdown
# Task Checklist

Based on: implementation_plan_v2.md

- [ ] Uncompleted task
- [/] In-progress task
- [x] Completed task
  - [x] Sub-task A
  - [ ] Sub-task B
```

## Walkthrough Document

After completing all work, create `walkthrough.md` summarizing:

- **Which plan version was implemented** (e.g., "Implemented `implementation_plan_v2.md`")
- What was changed, with file links
- What was tested
- Validation results (build output, test results, migration applied)
- Any remaining follow-up items

There is one `walkthrough.md` per task's original implementation. Follow-up rounds get
lettered variants.

## Follow-Up Rounds

Work that follows a completed plan — reviews, corrections, further analyses,
supplementary changes — is tracked as a **follow-up round** in the same task directory.
Each round gets a **letter suffix** (`_A`, `_B`, ...) assigned in creation order, embedded
between the document name and the version suffix: `<document_name>_<round>_v<N>.md`.

| File type | Original | Round A | Round B |
|-----------|----------|---------|---------|
| Plan / review / analysis | `implementation_plan_v1.md` | `implementation_review_A_v1.md` | `performance_analysis_B_v1.md` |
| Task checklist | `task.md` | `task_A.md` | `task_B.md` |
| Walkthrough | `walkthrough.md` | `walkthrough_A.md` | `walkthrough_B.md` |

- The **document name** describes the content (`implementation_review`,
  `correction_plan`, `performance_analysis`), in `snake_case`.
- The **version suffix** follows the same strict rules within the round: first is `_v1`,
  never overwrite, increment to revise. Task checklists and walkthroughs are **singular
  per round**.
- Each round follows the **same five-phase lifecycle**; its plan document takes the role
  of the implementation plan and needs approval before execution.
- Check which letters already exist; the new round takes the next one.

Use a **follow-up round** when the work directly relates to the original task (reviewing
the implementation, fixing issues found during verification, adding something deferred).
Create a **new task directory** when the work is substantially independent — even if it
touches the same files — or when the scope has outgrown the original task.

## Quick Decision Guide

```
Is it a minor follow-up while executing an already-approved plan?
  → YES: Skip a new plan. Continue executing the existing one.
  → NO: Continue ↓

Is the task trivial (single file, typo, comment, question)?
  → YES: Skip the plan. Just do it.
  → NO: Continue ↓

Does it touch multiple files, cross project boundaries, or need a migration,
SCSS recompilation, or an Angular rebuild?
  → YES: Write a full plan. Follow the five-phase lifecycle.
  → NO: Use judgment. When in doubt, write the plan.
```

## `.plans/` Isolation During Research

The `.plans/` directory accumulates implementation plans, analyses, reviews, and other
structured documents from past and current tasks — including **superseded drafts** (`_v1`
when `_v2` was approved), **rejected approaches**, and **stale analyses** whose assumptions
may no longer hold.

> [!CAUTION]
> **Do NOT browse or read `.plans/` during Phase 1 (Research).** Old plan content can
> corrupt your research by injecting outdated design decisions, incorrect assumptions,
> or rejected approaches into your analysis. Base your research exclusively on the
> **actual source code, project files, migration files, stylesheets, tests, and skill
> documentation** — these are the ground truth.

### Rules for Orchestrating Agents

| Situation | Rule |
|-----------|------|
| **Phase 1 — Research** | Do NOT read any files under `.plans/`. Research the actual codebase. |
| **Phase 2 — Writing a plan** | Do NOT read other tasks' plans. You may read your own task's prior plan versions (e.g., `_v1` before writing `_v2`) if the user asked you to revise. |
| **Phase 4 — Execution** | Read **only** the approved plan for the current task. Do not browse other task directories. |
| **Follow-up rounds** | You may read the walkthrough and plan from the **same task directory** that the follow-up relates to. Do not read other tasks' plans. |
| **Picking up another agent's work** | Read the **latest `_v<N>`** plan for the specific task you are continuing. Do not read other tasks' plans or earlier superseded versions unless the user explicitly asks. |

### Rules for Subagents

Subagents operate on a **strict need-to-know basis**:

- **Do NOT read any files in `.plans/`** unless the orchestrator explicitly provides the
  path to a specific document and instructs the subagent to read it.
- The orchestrator should pass the relevant context (from the approved plan) **in the
  subagent's prompt**, not by directing the subagent to read the `.plans/` directory
  itself.
- If a subagent needs to understand a design decision, the orchestrator includes that
  decision in the task description — the subagent does not go looking for it in old plans.

### Rationale

1. **Stale data corruption**: A `_v1` plan may contain an approach that was explicitly
   rejected. An agent reading it may unconsciously adopt the rejected design.
2. **Cross-task contamination**: Plans for unrelated tasks may describe changes to the same
   files with different intent, confusing the agent about what the current task should do.
3. **Token waste**: `.plans/` can grow large. Reading irrelevant plans wastes context window
   tokens that should be spent on actual source code.
4. **Subagent scope creep**: Subagents that browse `.plans/` may discover context beyond
   their assigned task, leading to unauthorized or out-of-scope changes.
