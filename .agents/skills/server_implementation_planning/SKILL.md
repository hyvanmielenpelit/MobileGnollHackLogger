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
dotnet test Overseer.Tests --filter-not-trait "Category=UsesExternalApi"
```

From `Overseer/ClientApp/`, whenever the Angular client changed:

```bash
npm run test:headless
```

```bash
npm run build
```

Five ways a plan gets this wrong, all of which have happened:

- **Omitting `--filter-not-trait "Category=UsesExternalApi"`, or mistyping it.** The run then calls live
  OpenAI, Anthropic and Google APIs and spends real quota. **The filter fails open**: a typo in the
  trait name or its value selects the whole suite instead of erroring, so a plan that changes the
  argument must verify it by discovery (`--list-tests --filter-trait "Category=UsesExternalApi"`),
  never by running the suite. `testing_guidelines` § 1a has the measured table.
- **Assuming `dotnet test` works on a fresh clone with `global.json` missing or edited.** That file
  carries the Microsoft.Testing.Platform opt-in the .NET 10 SDK requires; without it every
  `dotnet test` command above fails before running a single test.
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

Any plan derived from an AI benchmark run analysis, report, or diagnostic review **MUST** include a dedicated **Chat Transfer** section, and **MUST** consult all five skills `.agents/AGENTS.md` § *AI Benchmark Findings* names — `server_benchmark_to_chat_transfer`, `server_benchmark_tool_diagnostics`, `server_tool_data_sources`, `server_tool_parameter_reference` and `server_wiki_handoff` — before drafting. That requirement is unconditional; do not treat the last four as conditional on the shape of a finding.

### One task directory per analysis

**Every document a benchmark analysis produces goes in the same task directory**, under this repository's scope:

```text
<plans-root>/hyvanmielenpelit/MobileGnollHackLogger/YYYY-MM-DD/<task_name>/
  benchmark_run_<N>_analysis_v<N>.md
  implementation_plan_v<N>.md               <- the Overseer / server plan
  developer_runbook_v<N>.md                 <- ordered fix steps and the next runs, for the human
  wiki_handoff_prompt_v<N>.md               <- the three-step handoff, when a finding lands on rung 2
  gnollhack_implementation_plan_v<N>.md     <- a plan whose work is in another repository
  task.md, walkthrough.md                   <- the server plan's
  gnollhack_task.md, gnollhack_walkthrough.md
```

- **A plan for another repository stays here**, named with that repository's lower-case name as a prefix — `gnollhack_` for GnollHack — and is **not** filed under that repository's own scope. This is the global skill's *"several repositories, one clearly main"* case, decided once: for a benchmark analysis the main repository is always this one, because the analysis is what a reader looks for and the run, the report and the registry entry all live here.
- **It is a member of the round's document set**: listed in the main plan's `## Document Set`, bumped with every revision, and copied verbatim with the one-line harmonization note when unchanged.
- **The Developer Runbook is a set member too** — `developer_runbook_v<N>.md`, in the format `server_benchmark_runbook` defines. It is the one document of the round written for a reader who has opened none of the others, and it is versioned with them.
- **It still has to stand on its own.** It is handed to another developer, possibly in another application, so it says in its own header that the work is in the other repository, that its paths are relative to that repository's root, and that none of its sibling documents is needed to implement it. Its checklist and walkthrough take the same prefix so they do not collide with the server plan's.
- **The rubric repair YAML and the Snapshot Suite Wizard files are not plans** and stay outside every repository, as `server_rubric_handoff` and `server_snapshot_suite_authoring` say.

Set on 2026-09-18 by the user's instruction, in the run-52 round, after a GnollHack plan was first filed under `hyvanmielenpelit/GnollHack/` and had to be moved: split across two scopes, the round's four documents could not be found, versioned or handed over as one thing.

### A GnollHack plan never changes the save-file layout

> 🛑 **Do not propose a change that alters GnollHack's save-file layout — not as a plan item, not as an "optional half", not as an alternative.** GnollHack's save format changes about **once a year, in a major upgrade**, and a change that needs it can take up to a year to land. A benchmark finding is never a reason to break players' saves.

What this rules out in a `gnollhack_` plan: a new field in `struct you` (`u.*`), `struct context_info` (`context.*`), `struct flag` (`flags.*`), `struct obj`, `struct monst`, a level structure, or any other structure that `src/save.c` and `src/restore.c` write and read whole; reordering or resizing an existing one; a new saved list or a changed record in one.

What stays available, and is where a snapshot improvement goes instead:

- **Text the snapshot writer derives at export time** from state that already exists — the usual case.
- **A new entry in an existing variable-length saved list whose record shape does not change.** The game log is the standing example: `struct gamelog_line` (`include/hack.h`) is a turn, flags and a text, and the `LL_AI` category exists so an event can be recorded for the AI snapshot alone. A later export can then read the fact back out of the log instead of out of a new field.
- **`iflags.*`**, which is not saved.

If a finding can only be served by a layout change, **say so and defer it**: list it in the plan under a heading *Deferred to the next major GnollHack upgrade (needs a save-file layout change)*, with the finding and the smallest field that would serve it, and schedule nothing. Do not present it for a decision in *User Review Required* — the answer is already known.

Set on 2026-09-18 by the user's instruction, in the run-54 round, after a snapshot plan offered a new turn-number field as an optional item.

### The plan runs uninterrupted; the developer's jobs come last

**A plan derived from a benchmark analysis implements every one of its changes in one uninterrupted pass, and hands the developer's manual jobs over at the very end.** Building and starting Overseer, importing a rubric repair, running the wiki sessions, changing a grader roster and launching the runs are not plan steps: they are Part A of the round's Developer Runbook (`server_benchmark_runbook`), and they begin when the plan's last step and its verification are done. Do not write a plan that stops after a stage so the developer can "try it in Overseer" — verify with the build and the tests instead, which the agent runs itself.

**Exports from and imports into Overseer go at the start or at the end.** An export the agent needs as input — the suite's **Download All as YAML**, a database query result — is asked for *before* the plan is written, normally while the analysis is still in progress. An import — the rubric repair YAML, a questions file — is an end-of-plan job. Only a **very big plan**, where a later stage genuinely consumes what an earlier stage makes Overseer produce, may place one between stages, and then it is a pause under the rules below.

**A mid-plan pause is a special case, and the plan has to earn it.** It is allowed only when a later *agent* step needs something that only the running application can produce or take in, and that cannot be moved to the start or the end. The plan says so under its own heading, `## Mid-Plan Pauses`, naming for each pause (`P1`, `P2`, …) the stage boundary it falls on, what the developer does, what the agent needs back, and why it cannot wait. A plan without that heading has no pause, and the agent does not invent one during execution; a need discovered while executing is a plan revision.

At a pause the agent owes four things, in this order:

1. **The tree compiles, proven, not assumed.** The developer starts Overseer from Visual Studio, which builds the working tree as it stands — with only part of the plan applied. So the pause falls on a **stage boundary** the plan designed to be compilable: no renamed member with callers still to be updated, no DTO changed on one side only, no migration generated and not applied. Every subagent of the stage has returned. Then the orchestrator runs, from the repository root,
   ```bash
   dotnet build MobileGnollHackLogger.slnx
   ```
   and, from `Overseer/ClientApp/` when the client changed,
   ```bash
   npm run build
   ```
   and both must finish with **0 errors** before the developer is asked for anything. If the stage added a migration, `dotnet ef database update -p GnollHackServer.Data -s MobileGnollHackLogger` has been run too. A build that fails **only** on locked output files (`MSB3021` / `MSB3027`) means Overseer is still running in Visual Studio: ask the developer to stop it, and build again — that is not a compile error, and it is not a pass either.
2. **Step cards**, in the runbook's card format (`server_benchmark_runbook` § 3), under the pause's `P<n>` name: start Overseer in Visual Studio, the exact clicks, what to expect, what to bring back.
3. **A plain statement that the work is not finished.** The pause message opens with it:

   > **I have not finished. This is pause P1, after stage 2 of 5.** The solution builds at this point (`dotnet build MobileGnollHackLogger.slnx`: 0 errors). Please do the steps below and reply with `<what is needed>`. When you do, I will continue with stages 3–5: `<one line each>`. Until then nothing else in this plan can proceed.

   A pause message that reads like a handoff is the defect this rule exists to prevent: the developer commits half a plan and launches a run on it.
4. **`task.md` shows it.** The pause step is marked `[/]` with *waiting for the developer*, and is ticked only after the agent has checked what came back — never on the developer's word alone.

**The wiki confirmation gate (`server_wiki_handoff` § 4a) is a pause under these rules only when an agent step waits behind it.** When what waits is the developer's own work — the restart, the next run — there is no pause: the wiki sessions are simply end-of-plan jobs in the runbook, in their place in the order.

Set on 2026-09-19 by the user's instruction.

### The `Skills consulted:` line

Every plan and every analysis document derived from a benchmark run **MUST** carry a one-line `Skills consulted:` field, near the top with the other metadata, listing the skills actually read — and stating explicitly when a mandatory one was **not** read, and why.

```
Skills consulted: server_benchmark_to_chat_transfer, server_benchmark_tool_diagnostics,
server_tool_data_sources, server_tool_parameter_reference, server_wiki_handoff
```

One line, checkable at a glance. Its purpose is to make the next gap visible to the user without their having to ask: the run-28 analysis on 2026-09-09 shipped having read one of the four, and it took a direct question from the user to surface that. A document that claims a skill it did not read is a worse defect than one that admits the omission, so write what actually happened.

The field belongs to **this repository's** plan format only, deliberately — the global `agent-implementation-planning` template lives in another repository and is out of scope here.

The benchmark evaluates the production chat system prompt (`ChatService.BuildSystemPrompt`), so benchmark observations directly measure live assistant behavior. The plan's Chat Transfer section must:
1. Triage findings into harness defects, suite defects, chat-transferable findings, or corpus / environment defects.
2. Check configuration parity (e.g. `verboseMode` concise vs. detailed).
3. Identify the proposed ladder rung (knowledge base article, wiki update, tool policy/description, limits parity, model selection, or prompt prose modification).
4. Evaluate whether the evidence bar is met (minimum two comparable runs or an isolated variable pair) before any chat prompt change is proposed.
5. State the pre-declared acceptance criterion and the rollback trigger for any proposed change, per the skill's Verification and Rollback section.
6. Carry the **tool-diagnostics table** and the **"Limits of this pass"** statement that `server_benchmark_to_chat_transfer` § 10 requires, with columns as `server_benchmark_tool_diagnostics` § 10 defines them.
7. For any rung-2 wiki finding, name the wiki handoff document, state its validation verdict if validation has been run, and state whether the plan has a **confirmation gate** before the steps that depend on the wiki change, per `server_wiki_handoff` § 4a — or that nothing in the plan depends on it.
8. Name the round's **Developer Runbook** (`developer_runbook_v<N>.md`, `server_benchmark_runbook`). The developer's jobs are **not** plan steps: they follow the plan's last step and live in the runbook (§ *The plan runs uninterrupted* below). The one thing the two documents share is a **pause** — when the plan has one, it is a step of its own in Proposed Changes and carries the same `P<n>` name as its runbook card. After execution, `walkthrough.md` carries a **Runbook status** section: each runbook step marked done or remaining, plus every value that only became known during execution (the new `HarnessVersion`, the `ToolGuidesSha256` the next run should show).

If the plan addresses only harness or suite infrastructure, it must explicitly state: *"No chat-transferable changes proposed in this plan."*

## Cross-References

- `agent-implementation-planning` (global lifecycle baseline)
- `agent-subagent-guidelines` (subagent tiers and exclusivity)
- `server_benchmark_to_chat_transfer` (mandatory method for benchmark-to-chat translation)
- `server_benchmark_tool_diagnostics` (reading a run as a tool-layer instrument; the § 10 table)
- `server_tool_data_sources` (the corpora behind each tool, and what each index excludes)
- `server_tool_parameter_reference` (the per-tool parameter and result contract)
- `server_wiki_handoff` (the rung-2 three-step handoff document — proposed changes, source validation, execution — and the confirmation gate)
- `server_benchmark_runbook` (the Developer Runbook: ordered fix steps and run cards for the following runs)
- `testing_guidelines` (test classification and execution)
