---
name: server_implementation_planning
description: >-
  Full workflow for writing, delivering, and executing implementation plans for
  non-trivial MobileGnollHackLogger and Overseer tasks. Covers when a plan is
  required, the mandatory research-plan-approve-execute-verify lifecycle, plan document
  structure, subagent use, artifact delivery to the shared plans repository, the
  .plans/ fallback, and research isolation. Read this skill before starting any
  multi-file or cross-project change.
---

# Server Implementation Planning Workflow (Project Overlay)

## Overview & Baseline Delegation

This skill defines the repository-specific planning requirements and build boundaries for **MobileGnollHackLogger** and **Overseer**.

> **If the shared skills are not installed**, this is the whole baseline:
> a written plan is required for any change touching more than one file or crossing a
> build boundary; save it in the shared plans repository as
> `<plans-root>/hyvanmielenpelit/MobileGnollHackLogger/YYYY-MM-DD/task_name/implementation_plan_v1.md`,
> resolving `<plans-root>` as `AGENT_PLANS_ROOT`, else `C:\hmp\plans`, else a `plans`
> directory beside this one -- and if none resolves, fall back to the **main**
> repository's `.plans/YYYY-MM-DD/task_name/` (this repository's when
> MobileGnollHackLogger is the main one), usable only because `.plans/` is gitignored there
> (`git check-ignore -q .plans`), **and say so in chat**. If it is not ignored, or that
> repository is not writable from this session, keep the plan in the chat instead. Never overwrite a
> version -- increment. Print the path; **wait for explicit approval before editing any
> file**; track progress in `task.md`; finish with `walkthrough.md`. **Never commit or
> push in this repository.** Clone the plans repository from
> `https://github.com/hyvanmielenpelit/plans` and install the full guidance from
> `https://github.com/hyvanmielenpelit/SharedAgentSkills` (`.\setup.ps1`).

> [!IMPORTANT]
> **Global Baseline Delegation**: the 5-phase lifecycle, plan document structure, the
> Execution Target line, the plans repository layout and scope directories, `_v<N>`
> versioning and harmonization, the commit protocol, the `.plans/` fallback, follow-up
> rounds, and research isolation are defined in the global
> **`agent-implementation-planning`** skill. Subagent tiers, how to resolve them, and file-level exclusivity are in
> **`agent-subagent-guidelines`**. Harness mechanics -- plan mode under Claude Code,
> artifact delivery under Antigravity -- are in `claude-plan-mode` or
> `gemini-antigravity-conventions`, whichever is installed for your application.
>
> The sections below are what is specific to **this repository**.

The sections below specify the repository-specific build boundaries, multi-project triggers, and execution constraints that apply to this codebase.

---

## Project Structure

This repository spans four projects and several generated-artifact boundaries:
- `MobileGnollHackLogger` (ASP.NET Core host and Razor Pages)
- `Overseer` (Angular client SPA frontend)
- `GnollHackServer.Data` (Entity Framework Core database models and migrations)
- `Overseer.Tests` (xUnit test suite)

A change that looks local often is not: a model added to a catalog file affects the Angular client, a database model change requires an EF Core migration in a different project than the one being edited, and a style tweak is worthless unless the generated CSS is recompiled. Planning is what catches that before any source file is edited.

---

## When a Plan Is Required

A written implementation plan is **required** when the task meets **any** of these criteria:

- It touches **more than one file**, or more than one project (`MobileGnollHackLogger`, `Overseer`, `GnollHackServer.Data`, `Overseer.Tests`)
- It requires an **EF Core migration**
- It requires **SCSS-to-CSS recompilation** or an **Angular client rebuild**
- It is a refactor, a new feature, or anything the user describes as large or non-trivial

A plan is **not** required for:
- Single-file bug fixes
- Typo and comment corrections
- Answering questions or read-only investigation
- Minor follow-ups while executing an already-approved plan

**When in doubt, write a plan.** A rejected plan is cheap; a wrong cross-project change is not.

---

## Build Impact for This Repository

These are the generated-artifact boundaries a plan must account for. A plan step must never straddle one — finish the source change, cross the boundary, then continue.

### SCSS to CSS

`MobileGnollHackLogger/wwwroot/css/site2.scss` generates **both** `site2.css` and `site2.min.css`. **Never edit the generated CSS directly** when an SCSS source exists. Both outputs are regenerated:

```bash
npx sass wwwroot/css/site2.scss wwwroot/css/site2.css
```

```bash
npx sass wwwroot/css/site2.scss wwwroot/css/site2.min.css --style compressed
```

A plan that changes styling must list the two generated CSS files in Affected Files and name SCSS compilation under Build Impact.

### EF Core Migrations

Migrations target the **`GnollHackServer.Data`** project, not the project being edited, and applying a migration is a **separate second command** — generating it does not touch the database:

```bash
dotnet ef migrations add <MigrationName> -p GnollHackServer.Data -s MobileGnollHackLogger -o Migrations
```

```bash
dotnet ef database update -p GnollHackServer.Data -s MobileGnollHackLogger
```

A plan that changes a database model must list the generated migration files, name the migration under Build Impact, and sequence dependent code **after** the migration step.

The AI agent can and should run both `dotnet ef migrations add` and `dotnet ef database update` directly during the execution phase once the plan is approved. No human assignment is needed for running database updates.

### Angular Client

The `Overseer` SPA is built separately from the ASP.NET Core host. Changes to TypeScript, component templates, or component styles require a client rebuild before they are visible, and shipping a release build also involves uploading source maps to Sentry as its own step.

### Images

New or replaced raster assets are converted to **WebP at quality 85**.

### Documentation and Solution Items (`docs/` and `MobileGnollHackLogger.slnx`)

When adding, moving, or renaming documentation files under `docs/` (e.g., `docs/`, `docs/overseer/`, or any new documentation subdirectories):
- You MUST also add or update `<File Path="..." />` entries inside the corresponding `<Folder Name="/docs/...">` element in `MobileGnollHackLogger.slnx`.
- Rationale: Visual Studio does not show arbitrary repository files in the Solution Explorer unless they are registered in the `.slnx` solution file. Adding them ensures human developers can navigate and open all documentation directly in Visual Studio.
- Example entry in `MobileGnollHackLogger.slnx`:
  ```xml
  <Folder Name="/docs/overseer/">
    ...
    <File Path="docs/overseer/my-new-guide.md" />
  </Folder>
  ```
- Any plan that adds or moves documentation files under `docs/` must list `MobileGnollHackLogger.slnx` under Affected Files.

### Publishing

**Never publish** (`dotnet publish` or equivalent) unless the user explicitly asks. A plan must not include a publish step on its own initiative.

---

## Build Dependency Chains

These are sequencing constraints, not just boundaries. A plan must not parallelize across
one, and no two agents may touch both ends of a chain concurrently.

| Chain | Constraint |
|-------|-----------|
| `site2.scss` -> `npx sass` -> `site2.css` **and** `site2.min.css` | Both outputs are regenerated together. **No agent may edit the generated CSS**, and none may touch it in parallel with the SCSS edit. |
| Model change in `GnollHackServer.Data` -> `dotnet ef migrations add` -> `dotnet ef database update` -> dependent code | Do not parallelize across the migration step. The agent runs **both** commands itself; no human assignment is needed. |
| Overseer TypeScript / templates -> Angular client rebuild -> (release only) Sentry source-map upload | Changes are not visible until the client is rebuilt. The source-map upload is its own step and only on release. |
| New/moved files under `docs/` -> add to `MobileGnollHackLogger.slnx` | Solution Explorer accessibility. Visual Studio does not show documentation files unless they are declared in `.slnx`. |

## Verification Plan

Every plan must include the **Verification Plan** section, with **Automated** and
**Manual** subsections.

### Automated

The Automated subsection uses these commands **verbatim**. They are also stated in
`AGENTS.md`, which is loaded into every context window, so there is no excuse for a plan
inventing its own:

```bash
dotnet build MobileGnollHackLogger.slnx
```

```bash
dotnet test Overseer.Tests --filter "Category!=UsesExternalApi"
```

From `Overseer/ClientApp/`, whenever the Angular client changed:

```bash
npm run test:headless
```

```bash
npm run build
```

Three ways a plan gets this wrong, all of which have happened:

- **Omitting `--filter "Category!=UsesExternalApi"`.** The run then calls live OpenAI,
  Anthropic and Google APIs and spends real quota.
- **Writing `npm test`, `ng test`, or `npm test -- --watch=false`** instead of
  `npm run test:headless`. Karma stays in watch mode and the command never returns.
- **Writing `.sln`.** This repository uses `MobileGnollHackLogger.slnx`.

**Read `testing_guidelines` before writing this section.** It also governs whether any test
the plan adds needs the `[Trait("Category", "UsesExternalApi")]` decoration -- a plan-time
decision, not an execution-time one. A plan that adds a live-API test must say so
explicitly and must state that the user's permission is required before running it.

### Manual

List the steps that prove the change works in the running application: which page, which
action, and what the user should see. Automated coverage does not substitute for this on
UI or report-output changes.

## Subagent Use

Every plan must include the **Subagent Use** section. Tiers, the selection rule, the
spawn boundary, and file-level exclusivity are in **`agent-subagent-guidelines`**; the
chains above are what constrains sequencing here.

## AI Benchmark Plans and Chat Transfer

Any plan derived from an AI benchmark run analysis, report, or diagnostic review **MUST** include a dedicated **Chat Transfer** section and must consult the **`server_benchmark_to_chat_transfer`** skill before drafting.

The benchmark evaluates the production chat system prompt (`ChatService.BuildSystemPrompt`), so benchmark observations directly measure live assistant behavior. The plan's Chat Transfer section must:
1. Triage findings into harness defects, suite defects, or chat-transferable findings.
2. Check configuration parity (e.g. `verboseMode` concise vs. detailed).
3. Identify the proposed ladder rung (knowledge base article, wiki update, tool policy/description, limits parity, model selection, or prompt prose modification).
4. Evaluate whether the evidence bar is met (minimum two independent runs or an isolated variable pair) before any chat prompt change is proposed.

If the plan addresses only harness or suite infrastructure, it must explicitly state: *"No chat-transferable changes proposed in this plan."*

## Cross-References

- `agent-implementation-planning` (global lifecycle baseline)
- `agent-subagent-guidelines` (subagent tiers and exclusivity)
- `server_benchmark_to_chat_transfer` (mandatory method for benchmark-to-chat translation)
- `testing_guidelines` (test classification and execution)
