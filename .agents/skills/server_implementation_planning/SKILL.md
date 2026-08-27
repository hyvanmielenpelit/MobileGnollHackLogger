---
name: server_implementation_planning
description: >-
  Full workflow for writing, delivering, and executing implementation plans for
  non-trivial MobileGnollHackLogger and Overseer tasks. Covers when a plan is
  required, the mandatory research-plan-approve-execute-verify lifecycle, plan document
  structure, subagent use, artifact delivery to .plans/, and .plans/ research
  isolation. Read this skill before starting any multi-file or cross-project change.
---

# Server Implementation Planning Workflow (Project Overlay)

## Overview & Baseline Delegation

This skill defines the repository-specific planning requirements and build boundaries for **MobileGnollHackLogger** and **Overseer**.

> [!IMPORTANT]
> **Global Baseline Delegation**: The general 5-phase planning lifecycle, plan document structure, subagent coordination rules, `.plans/` artifact versioning, and `.plans/` research isolation are defined in the global **`agent-implementation-planning`** skill. Refer to that skill for universal planning conventions.

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

### Publishing

**Never publish** (`dotnet publish` or equivalent) unless the user explicitly asks. A plan must not include a publish step on its own initiative.
