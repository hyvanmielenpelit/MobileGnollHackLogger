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
- Use your harness's own scratch directory; the global rules name the exact path for each. The sole in-tree exception is `.plans/`.

## Entity Framework Core Migrations

When making changes to database models that require EF Core migrations, you MUST observe the following rules:
- **Correct Project**: Migrations MUST be targeted to the `GnollHackServer.Data` project. Use the `-p GnollHackServer.Data -s MobileGnollHackLogger` flags.
- **Add Migration**: `dotnet ef migrations add <MigrationName> -p GnollHackServer.Data -s MobileGnollHackLogger -o Migrations`
- **Update Database**: After generating a migration, you MUST run a separate command to apply it to the database: `dotnet ef database update -p GnollHackServer.Data -s MobileGnollHackLogger`

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

- Deliver plans, reviews, analyses, reports, and other structured documents as **Markdown files** saved under the repository's gitignored `.plans/` directory: `.plans/YYYY-MM-DD/task_name/<document_name>_v<N>.md` (where N=1 for the first version).
- **Document versioning**: the first version always gets a `_v1` suffix. Never overwrite an existing version — to revise, create a new file with the next version number (`_v2`, `_v3`, etc.). `task.md` and `walkthrough.md` are singular (no version suffix). Follow-up rounds use lettered variants (`task_A.md`, `walkthrough_A.md`, etc.).
- **Wait for explicit user approval before editing any file.** Do not begin implementation alongside the plan. Always print the plan's file path.
- **Harness rules take precedence**: `.plans/` is the source of truth across all AI agents. If a harness keeps a private plan file or artifact, copy the finished plan to `.plans/` immediately before requesting user approval.
- **`.plans/` Research Isolation**: Do NOT browse or read `.plans/` during Phase 1 (Research) to prevent stale or superseded designs from corrupting analysis.

## Publishing

- **Do NOT publish anything** (e.g., via `dotnet publish` or similar commands) unless explicitly requested by the user.

## Shared Skills

Global skills and baseline rules are supplied by the `hyvanmielenpelit/SharedAgentSkills`
repository and installed with its `setup.ps1`. **This is a prerequisite, not an option** --
clone it and run the script once per machine. See its `docs/ai-skill-management.md`.

- `agent-implementation-planning` -- planning lifecycle, plan format, `.plans/` conventions
- `agent-subagent-guidelines` -- the mandatory **Subagent Use** plan section, model tiers
  and how to resolve them, file-level exclusivity, protecting uncommitted changes
- `agent-powershell-guidelines` -- Windows and PowerShell 5.1 rules

Each project overlay skill also carries a short self-contained fallback, so a machine
without the shared skills degrades to a thin baseline rather than to nothing.
