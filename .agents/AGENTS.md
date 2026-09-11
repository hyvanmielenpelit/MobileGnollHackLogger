# MobileGnollHackLogger Project Rules

These rules apply to all AI-assisted development on the MobileGnollHackLogger codebase.

## Project Overview

MobileGnollHackLogger is an ASP.NET Core web application that logs, processes, and displays game logs, leaderboards, and user accounts for GnollHack.

## SCSS and CSS Conventions

### Rules for Style Sheets
- **Do NOT modify CSS files directly** if there is a corresponding SCSS file (e.g., `wwwroot/css/site2.scss` generates `wwwroot/css/site2.css` and `wwwroot/css/site2.min.css`).
- **Modify only the SCSS file** (`.scss`) for styling updates.
- **Compile SCSS files** using `npx sass` to regenerate the corresponding CSS and minified CSS files.

### Compilation Commands
To compile SCSS files:
- Standard CSS:
  ```bash
  npx sass wwwroot/css/site2.scss wwwroot/css/site2.css
  ```
- Minified CSS:
  ```bash
  npx sass wwwroot/css/site2.scss wwwroot/css/site2.min.css --style compressed
  ```

## Image Conventions

### Rules for Image Files
- **WebP format**: Convert JPG and PNG images to WebP to optimize web asset performance.
- **Conversion quality**: When converting images to WebP, always use a compression quality of **85** (e.g., `quality=85` in Pillow or `-q 85` in cwebp).

## Temporary and Guidance Files

- **NEVER** store temporary files, scratch scripts, or guidance files in the repository root, in a scratch directory under the repository root, or anywhere else within the repository.
- Use your harness's own scratch directory; the global rules name the exact path for each. The sole in-tree exception is `.plans/`, and only as the fallback when the shared `plans` repository cannot be reached.

## Entity Framework Core Migrations

When making changes to database models that require EF Core migrations, you MUST observe the following rules:
- **Correct Project**: Migrations MUST be targeted to the `GnollHackServer.Data` project. Use the `-p GnollHackServer.Data -s MobileGnollHackLogger` flags.
- **Add Migration**: `dotnet ef migrations add <MigrationName> -p GnollHackServer.Data -s MobileGnollHackLogger -o Migrations`
- **Update Database**: After generating a migration, you MUST run a separate command to apply it to the database: `dotnet ef database update -p GnollHackServer.Data -s MobileGnollHackLogger`

## Testing and Verification Commands

These are the **only** correct ways to run this repository's tests. Use them verbatim in every implementation plan's Verification Plan and in every verification run.

- **.NET tests** (from the repository root):
  ```bash
  dotnet test Overseer.Tests --filter-not-trait "Category=UsesExternalApi"
  ```
  The `--filter-not-trait` is **not optional**. Without it the run calls live OpenAI, Anthropic and Google APIs, spending quota and money. For the whole solution, substitute `MobileGnollHackLogger.slnx` for `Overseer.Tests`. Note the project directory is `Overseer.Tests`, plural.

  To run **only** the live-API tests, which needs the secrets and your explicit permission, the counterpart is `--filter-trait "Category=UsesExternalApi"` — the same argument, without the `not`.

  **This command depends on the repository-root `global.json`.** `Overseer.Tests` is a Microsoft.Testing.Platform application, and from the .NET 10 SDK onward `dotnet test` refuses to run one through the old VSTest path — *"Testing with VSTest target is no longer supported…"*. The `"test": { "runner": "Microsoft.Testing.Platform" }` entry in `global.json` selects the new runner, and without it the command above fails outright. **Do not delete or "modernise" that file** — see the `testing-guidelines` skill for why `dotnet.config` is not the mechanism here.

  > ⚠️ **The filter fails open, so verify it rather than trusting it.** A typo in the trait name or its value — `Categoy=UsesExternalApi`, `Category=UsesExternalApis` — matches nothing, therefore excludes nothing, and runs the live-API tests without a word of complaint. So does writing `!=` inside `--filter-not-trait`, which is the old VSTest habit. If you change the argument, confirm it with a **discovery** count first, which runs no test and spends nothing:
  > ```bash
  > dotnet test Overseer.Tests --list-tests --filter-trait "Category=UsesExternalApi"
  > ```
  > That must report exactly the live-API tests (4 as of 2026-09-09). Never check a filter by running the suite unfiltered.

  > **If you meet `--filter "Category!=UsesExternalApi"` in an older plan or commit, it is the same thing.** That is VSTest filter syntax, which Microsoft.Testing.Platform accepts as a compatibility shim; this repository moved to the native `--filter-not-trait` on 2026-09-09 so the documented command does not rest on a compatibility layer in a toolchain that has already dropped one. Both still work today.

- **Angular tests** (from `Overseer/ClientApp/`):
  ```bash
  npm run test:headless
  ```
  Never plain `npm test` or `ng test`: without `--no-watch` Karma stays in watch mode and the command never returns, and without `--browsers=ChromeHeadless` it opens a browser window on the user's desktop.

Rationale, the `[Trait("Category", "UsesExternalApi")]` convention, live-model policy, and the Angular test configuration rules are in the `testing-guidelines` skill.

## File Organization

| Area | Location |
|------|----------|
| Repository Documentation | `docs/` |
| Overseer Developer Documentation | `docs/overseer/` |
| Razor Pages | `MobileGnollHackLogger/Pages/` |
| Stylesheets (SCSS) | `MobileGnollHackLogger/wwwroot/css/site2.scss` |
| Generated Stylesheets (CSS) | `MobileGnollHackLogger/wwwroot/css/site2.css` & `site2.min.css` |
| Images | `MobileGnollHackLogger/wwwroot/img/` |
| Program Entry & Startup | `MobileGnollHackLogger/Program.cs` |
| Visual Studio Solution | `MobileGnollHackLogger.slnx` |

### Documentation & Solution File (MobileGnollHackLogger.slnx)
- Whenever you add, move, or rename files under `docs/` (e.g. `docs/` or `docs/overseer/`), you **MUST** also add them to the Visual Studio solution file (`MobileGnollHackLogger.slnx`) under the corresponding `<Folder Name="/docs/...">` solution items folder.
- **Rationale**: Visual Studio Solution Explorer only displays repository documentation if the files are registered in the `.slnx` solution file. Adding them ensures human developers can access, browse, and edit all documentation directly inside Visual Studio.

## NetHack Wiki Re-Indexing Policy

- **Startup-Only Indexing**: `NetHackWikiService` only indexes files once during application startup and does NOT run periodic background re-indexing timers.
- **Rationale**: NetHackWiki consists of thousands of static files (in `C:\hmp\nethackwiki`) that are updated very seldomly via manual file uploads. Running periodic scans on thousands of files introduces unnecessary CPU and disk I/O load.
- **Restart Required**: If NetHackWiki markdown files are updated or uploaded, the Overseer site/service must be restarted for the new content to be indexed.

## Environment & Shell Conventions

- **Operating System:** Development and tool execution take place on Windows. For PowerShell commands, syntax rules, quoting, and file I/O best practices, follow the global `agent-powershell-guidelines` skill.

## Implementation Plans

**Non-trivial tasks require a written implementation plan, approved by the user before any file is modified.** Read the repository overlay skill `server_implementation_planning` (`.agents/skills/server_implementation_planning/SKILL.md`) for project-specific build boundaries, and the global `agent-implementation-planning` skill for the universal lifecycle.

A plan is **required** when a task meets any of these:
- It touches **more than one file**, or more than one project (`MobileGnollHackLogger`, `Overseer`, `GnollHackServer.Data`, `Overseer.Tests`)
- It requires an **EF Core migration**
- It requires **SCSS-to-CSS recompilation** or an **Angular client rebuild**
- It is a refactor, a new feature, or anything the user describes as large or non-trivial

A plan is **not** required for single-file fixes, typo and comment corrections, answering questions, or read-only investigation. When in doubt, write one — a rejected plan is cheap, a wrong cross-project change is not.

### Plan and Document Delivery

- Deliver plans, reviews, analyses, reports, and other structured documents as **Markdown files** saved in the shared `plans` repository: `<plans-root>/hyvanmielenpelit/MobileGnollHackLogger/YYYY-MM-DD/task_name/<document_name>_v<N>.md` (where N=1 for the first version). Resolve `<plans-root>` as `AGENT_PLANS_ROOT`, else `C:\hmp\plans`, else a `plans` directory beside this repository; never create it yourself.
- **Fallback**: if no root resolves, write to the **main** repository's `.plans/YYYY-MM-DD/task_name/` — this repository's when MobileGnollHackLogger is the main repository, which it usually is here — and **say so in chat**, naming the reason and the scope. Never fall back silently. This is available only because `.plans/` is gitignored — confirm with `git check-ignore -q .plans` against that repository. Where it is not ignored, or the main repository is another one this session cannot write to, the plan stays in the chat and no file is written.
- **Document versioning**: the first version always gets a `_v1` suffix. Never overwrite an existing version — to revise, create a new file with the next version number (`_v2`, `_v3`, etc.). `task.md` and `walkthrough.md` are singular (no version suffix). Follow-up rounds use lettered variants (`task_A.md`, `walkthrough_A.md`, etc.).
- **Version harmonization**: when a task directory holds several versioned documents describing one coherent piece of work, revising any of them bumps **all** of them to the same `_v<N>` — including unchanged ones, which are copied verbatim to the new number. Mixed versions inside a set are a defect.
- **Commit policy**: the `plans` repository is the **only** repository an agent may commit or push to, and there it commits once per round without being asked. Committing or pushing **in this repository is forbidden** unless the user explicitly asks — including in the `.plans/` fallback.
- **Wait for explicit user approval before editing any file.** Do not begin implementation alongside the plan. Always print the plan's file path.
- **Harness rules take precedence**: the plans repository is the source of truth across all AI agents. If a harness keeps a private plan file or artifact, copy the finished plan there immediately before requesting user approval.
- **Research Isolation**: Do NOT browse or read the plans repository or `.plans/` during Phase 1 (Research), and never read another repository's scope, to prevent stale or superseded designs from corrupting analysis.

## AI Benchmark Findings

Any analysis of an AI benchmark run — its report, diagnostics, or assessments — and any implementation plan derived from one **MUST** read **all five** of these skills, in this order, **before the first finding is written**:

1. `server-benchmark-to-chat-transfer`
2. `server-benchmark-tool-diagnostics`
3. `server-tool-data-sources`
4. `server-tool-parameter-reference`
5. `server-wiki-handoff`

This is **unconditional**. There is no finding-shaped condition to evaluate first, and none of the five is reached through any of the others. The earlier form of this rule made the last three conditional on *"any finding [that] turns on what a tool returned"* — a test an agent can only apply **after** the research those skills were meant to inform — and nested two of them inside the third, so a requirement lived inside a skill nobody had loaded. The run-28 analysis on 2026-09-09 read only the first and shipped with its tool layer un-audited, spending roughly 138,000 subagent tokens rediscovering a contract `server-tool-parameter-reference` already documented verbatim, and still filing the finding against the wrong contract.

The analysis **MUST** produce the **Chat Transfer** section `server-benchmark-to-chat-transfer` § 10 specifies, including the tool-diagnostics table and "Limits of this pass" statement that section requires. The benchmark grades the production chat system prompt, so a benchmark analysis that yields no conclusion about the chat assistant is incomplete, not merely brief. Both skills are living documents: every analysis appends its run to the model behaviour notes.

Why the last three earn their place: what a run stores about its tool calls **depends on its harness version** — before harness 17 it stored tool *counts* and discarded arguments and results; from harness 17 it stores every attempted call's arguments, result, error and timings — so "the tool returned nothing" is several different verdicts and only one of them is about the model. A corpus that was missing, stale, outside the indexed scope or excluded by a size limit is a **Corpus / Environment Defect** and never produces a chat prompt change.

**The cost is accepted deliberately.** Reading all five is roughly 1,350 lines of context up front. That is a considered trade against a session that spent far more than that on subagents rediscovering a subset of the same material and still got a finding wrong. Do not "optimise" this rule back into a conditional one. The fifth, `server-wiki-handoff`, was added on 2026-09-10 after the run-34 wiki handoff shipped with an unverified target page and a presumed generator; it is short by design, and its body binds only when a finding lands on ladder rung 2.

A `UserPromptSubmit` hook in `.claude/settings.json` backs this rule up by injecting the five skill names on benchmark-shaped prompts. It is a reminder, not the rule — if you edit either half, check the other. Two properties of it are deliberate and should not be "fixed": it matches by grepping the hook's **raw stdin** (neither `jq` nor `pwsh` is installed on this machine, so every JSON-parsing variant of the pattern is unusable here), which means a session whose `cwd` or id happens to contain "benchmark" also matches — an accepted false positive costing one injected line; and it ends in `|| true`, so a non-match, a missing `grep` or any error exits 0 and never blocks the prompt.

## Publishing

- **Do NOT publish anything** (e.g., via `dotnet publish` or similar commands) unless explicitly requested by the user.

## Skill Naming

Skills in this repository use the **`server_`** prefix. Canonical bodies live in
`.agents/skills/<underscore_name>/SKILL.md`; the `.claude/skills/<kebab-name>/` stubs are
**generated** by `SharedAgentSkills\tools\sync_stubs.ps1` and must never be hand-edited.
Notable project skills include `server_implementation_planning`, `server_benchmark_to_chat_transfer`, `server_wiki_handoff`,
`server_rubric_handoff` (the human-pasted rubric edit a Suite Defect finding hands off),
`server_data_privacy_framework`, and the tool-layer trio `server_tool_data_sources`,
`server_tool_parameter_reference` and `server_benchmark_tool_diagnostics`.

> [!IMPORTANT]
> **Never use the `client_` prefix here.** It is reserved for **GnollHack**, which is the
> real game client of this server -- the two prefixes name the tiers of one system. A
> `client_` skill in this repository would read as "the GnollHack client" to anyone who
> knows the convention, and skill names are what a triggering agent matches on.
>
> For browser-side concerns use **`frontend_`**, as in `frontend_packages_management`.

## Shared Skills

Global skills and baseline rules are supplied by the `hyvanmielenpelit/SharedAgentSkills`
repository and installed with its `setup.ps1`. **This is a prerequisite, not an option** --
clone it and run the script once per machine. See its `docs/ai-skill-management.md`.

- `agent-implementation-planning` -- planning lifecycle, plan format, plans repository conventions, versioning and harmonization, the commit protocol, the `.plans/` fallback
- `agent-subagent-guidelines` -- the mandatory **Subagent Use** plan section, model tiers
  and how to resolve them, file-level exclusivity, protecting uncommitted changes
- `agent-powershell-guidelines` -- Windows and PowerShell 5.1 rules

Each project overlay skill also carries a short self-contained fallback, so a machine
without the shared skills degrades to a thin baseline rather than to nothing.
