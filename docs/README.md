# GnollHack Account Server & Overseer Documentation

Welcome to the central documentation directory for the `MobileGnollHackLogger` repository. This directory contains developer guides, release checklists, operational manuals, and architecture documentation for all projects in this solution.

---

## 📚 Documentation Index

### 🤖 Gnoll Overseer (`Overseer/`)

Documentation and developer references for the Gnoll Overseer AI assistant web application:

| Document | Description |
|---|---|
| [**Release Checklist**](overseer/release-checklist.md) | Step-by-step checklist with exact terminal commands for cutting an Overseer release (testing, version bump, AI changelog, publish, test server validation, Sentry sourcemaps, and production deployment). |
| [**Commands Reference**](overseer/commands.md) | Comprehensive reference of all CLI commands used to develop, build, test, and run the backend and frontend. |
| [**Changelog Guide**](overseer/changelog-guide.md) | JSON schema reference, change classification types, and manual editing rules for `Overseer/Data/release-notes.json`. |
| [**AI Changelog Generation**](overseer/ai-changelog.md) | Instructions on how to use a local AI agent (`overseer_changelog` skill) to autonomously generate release notes from Git commits. |
| [**Adding AI Models**](overseer/adding-ai-models.md) | Instructions and schemas for adding new AI models (e.g. Google Gemini) to the Overseer model catalogs. |
| [**Chat & Data Retention**](overseer/chat-data-retention.md) | Specification and architecture for user session quotas, soft-delete lifecycle, tool call payload pruning, disk attachment cleanup, and automated database maintenance. |
| [**Sentry Logging Architecture**](overseer/sentry-logging-architecture.md) | Specification and architecture for Sentry crash logging, server event processing, proxy tunneling, and frontend network error suppression. |
| [**Sentry Source Maps Guide**](overseer/sentry-sourcemaps.md) | Guide to generating, injecting Debug IDs, and uploading Angular source maps to Sentry while excluding them from public deployment. |

---

### 🏰 GnollHack Account (`MobileGnollHackLogger/`)

*Documentation and developer guides for the primary GnollHack Account web application (Razor Pages):*

- **Database Migrations**: See [Entity Framework Core Migrations in commands.md](overseer/commands.md#5-database-migrations-entity-framework-core).
- **Styles & SCSS**: See [SCSS Compilation in commands.md](overseer/commands.md#6-scss-compilation-host-styles).

---

### 🤖 AI Agent Skills & Customizations

This repository contains specialized AI skills located in [`.agents/skills/`](../.agents/skills/):

- `sentry_logging_architecture` — Architectural design and suppression rules for Sentry crash logging and proxy tunneling.
- `overseer_sentry_issue_fixing` — Systematic diagnosis and fix planning for Sentry error reports.
- `overseer_sentry_sourcemaps_upload` — Injects Debug IDs and uploads Angular sourcemaps to Sentry.
- `chat_retention_architecture` — Specification and architecture for chat message retention, soft-delete, and DB maintenance.
- `background_indexing_architecture` — Background indexing lifecycle, search services, and degradation messaging.
- `overseer_changelog` — Autonomously generates release notes from path-filtered Git commits.
- `adding_gemini_models` — Parses new Gemini model definitions into Overseer catalog.
- `testing_guidelines` — Running backend and frontend tests safely.
- `scss_compilation` — Compiling host SCSS styles.
- `configuration_management` — Managing application settings and User Secrets.

---

## 📁 Directory Structure

```
docs/
├── README.md                      # Central Documentation Index (this file)
├── overseer/                      # Gnoll Overseer documentation
│   ├── release-checklist.md       # Concise step-by-step release checklist
│   ├── commands.md                # Build, test, run, and DB migrations CLI reference
│   ├── changelog-guide.md         # In-app changelog schema & maintenance
│   ├── ai-changelog.md            # AI-assisted release notes generation
│   ├── adding-ai-models.md        # Adding LLM models to catalog
│   ├── chat-data-retention.md     # Chat & data retention architectural specification
│   ├── sentry-logging-architecture.md # Sentry crash logging architecture & error filtering
│   └── sentry-sourcemaps.md       # Sentry source map injection & upload guide
└── account/                       # GnollHack Account documentation (reserved)
```
