# GnollHack Account Server Software

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Angular](https://img.shields.io/badge/Angular-22-DD0031)](https://angular.io/)

This repository contains the server-side software that powers the online services for [GnollHack](https://github.com/hyvanmielenpelit/GnollHack), a graphical roguelike game derived from NetHack. It provides three things: the **account and score services** the game clients connect to (account management, score tracking, game log recording and bones file sharing), **Gnoll Overseer**, an AI-powered assistant for players, and **GnollBench**, the AI benchmark that evaluates the models behind Overseer.

## What Is GnollHack?

[GnollHack](https://github.com/hyvanmielenpelit/GnollHack) is a turn-based roguelike game with a graphical tile-based interface, available on Android, iOS, and Windows. It features a rich world of monsters, items, and dungeons. Players create accounts on the **GnollHack Account** server (this repository) to track their scores, share bones files with other players, and participate in the global community.

## Projects in This Repository

This solution contains two web applications — **GnollHack Account** and **Gnoll Overseer** — with **GnollBench**, the AI benchmark, as a component inside Overseer. A shared data library and a test project complete it. Together they form the complete backend infrastructure for GnollHack's online services.

### 🏰 GnollHack Account (`MobileGnollHackLogger/`)

The original and primary project in this repository, **GnollHack Account** (historically called *MobileGnollHackLogger*) is an ASP.NET Core web application built with Razor Pages. It is the backend that the GnollHack game clients connect to directly. Its responsibilities include:

- **Top Score Recording** — Receives and stores game results from players, maintaining global leaderboards.
- **Recent Games** — Displays a feed of the latest completed games across all players.
- **Bones Sharing** — Manages the exchange of "bones files" between players. In roguelike tradition, when a player dies, their ghost and equipment can appear in another player's dungeon.
- **Statistics** — Provides detailed win-rate statistics broken down by role, difficulty, and game mode.
- **User Accounts** — Handles player registration, authentication, and profile management via ASP.NET Identity.
- **xlogfile API** — Exposes game log data in the standard `xlogfile` format used by [NetHack Scoreboard](https://nethackscoreboard.org/) and [Junethack](https://junethack.net/) tournament tracking.

### 🤖 Gnoll Overseer (`Overseer/`)

**Gnoll Overseer** is GnollHack's web-based AI assistant. It is a separate ASP.NET Core application with an Angular single-page application (SPA) frontend. Overseer helps players navigate the complex world of GnollHack by providing intelligent, context-aware answers about game mechanics, monsters, items, strategies, and more.

Key capabilities:

- **Multi-Provider AI Chat** — Supports multiple LLM backends (OpenAI, Anthropic/Claude, Google Gemini) so administrators can configure the best model for their needs.
- **Tool-Augmented Responses** — The AI is equipped with a rich set of tools that let it look up real data instead of hallucinating:
  - **Source Code Search & View** — Searches and reads the GnollHack C source code directly for precise, authoritative answers.
  - **Monster, Item & Artifact Lookup** — Retrieves exact stats from the game's data structures.
  - **Wiki & Knowledge Base Search** — Queries the [GnollHack Wiki](https://github.com/hyvanmielenpelit/GnollHackWiki) and a curated knowledge base.
  - **NetHack Wiki Search** — Searches the community NetHack Wiki for broader context.
  - **GitHub Integration** — Fetches repository info, searches issues, and browses code on GitHub.
  - **Player Data Tools** — Looks up player game logs, dump logs, and save file info.
- **Sub-agents** — The main agent can delegate scoped, multi-step tasks to specialist sub-agents that run with their own instructions, tools and budget (`Overseer/Services/Agents/`).
- **Document Attachments and RAG** — Users can attach PDF and Office documents; Overseer parses them and retrieves the relevant passages with local ONNX embeddings.
- **Data Privacy Framework** — Confidential and ephemeral (incognito) sessions, envelope encryption with crypto-shredding, outbound DLP masking of secrets, and malware scanning of attachments.
- **Real-Time Streaming** — Uses SignalR for real-time, token-by-token response streaming to the browser.
- **Spoiler Policy** — Enforces a configurable spoiler policy so the AI can respect players who want to discover things on their own.
- **Telemetry, Cost and Retention** — Response timing, whole-turn cost accounting, and automated chat data retention and database maintenance.
- **API Key Alerts** — E-mails the developers when a system API key runs out of credit, fails, or is about to expire.
- **Admin Dashboard** — Administrative tools on the tabs *Users, Groups, API Keys, System Configs, Database, AI Telemetry, AI Benchmark (GnollBench)* and *Developer Tools*.

### 📊 GnollBench (inside `Overseer/`)

**GnollBench** is the AI benchmarking component of Gnoll Overseer, on the **AI Benchmark** tab of the Admin page (admin-only). It runs candidate models through the **production chat system prompt and the production tools**, so a benchmark run measures the assistant players actually use.

Its purpose, in order of importance:

1. **Improving the Overseer AI chat** — finding bugs in the system prompt, the tools and the data behind them.
2. **Improving the benchmark itself**, so that its results are rigorous.
3. **The results** — which models to use in the chat, judged on intelligence, speed and cost.

Features:

- **Question suites** in three difficulty tiers, with YAML import and export, AI-generated questions, and suites built from GnollHack AI game snapshots.
- **Blind AI assessment** on BARS rating scales (accuracy, completeness, conciseness, readability) with a critical-error ceiling, assessor panels, and a second or reference reader.
- **Scoring profiles**, configurable, including speed scoring.
- **Provider-error isolation**, so a provider outage does not count against a model.
- **Tool-call capture** for diagnosing the chat's tool layer.
- **Multi-run replicate series** with statistical analysis.
- **Multi-suite batteries** combined into an Overall Intelligence Index.
- **Model Comparison** wizard with charts.
- **Report packs** — AI-written documents rendered to PDF and Word — and the **Download Center**.

The tab's sub-tabs are *Run Benchmark, Run History, Multi-Run Analysis, Multi-Suite, Manage Suites, Scoring Profiles* and *Model Comparison*.

Code: `Overseer/Services/Benchmarking/`, `Overseer/Controllers/AdminBenchmark*.cs`, `Overseer/ClientApp/src/app/admin/benchmark/`, `GnollHackServer.Data/Benchmark*.cs`, and the default suites in `Overseer/Data/DefaultSuites/`. Its four documents are listed under [GnollBench](#gnollbench) in the Documentation section.

### 📦 GnollHack Server Data (`GnollHackServer.Data/`)

A shared .NET class library that provides the **data access layer** used by both GnollHack Account and Gnoll Overseer. All three components share one database. It contains:

- Entity Framework Core database context and entity models (game logs, bones transactions, user accounts, AI chat sessions, AI configuration, AI benchmark suites, runs and reports, etc.)
- The EF Core migrations, in `GnollHackServer.Data/Migrations/`
- ASP.NET Identity integration for user authentication
- Shared utilities (email sending, game data helpers, logging)

### 🧪 Overseer Tests (`Overseer.Tests/`)

The automated test suite for the Overseer project, containing unit tests and integration tests for the AI chat service, tool execution, API endpoints and GnollBench. It uses xUnit v3 on Microsoft.Testing.Platform. The Angular client has its own Vitest specs, run in browser mode with Playwright.

## 📏 Codebase Size

> **These figures are an estimate, and they change quickly with new commits.** Measured **2026-10-06** at commit **`7f48dec`**.

### Lines of code

| Component | Production code | Tests | Total |
|---|---:|---:|---:|
| GnollHack Account (`MobileGnollHackLogger/` and its game-data entities) | ~11,000 | — | ~11,000 |
| Gnoll Overseer — AI chat and platform | ~84,000 | ~48,000 | ~132,000 |
| GnollBench — AI benchmark | ~188,000 | ~126,000 | ~313,000 |
| Shared data plumbing (database context, user, email, loggers) | ~1,000 | — | ~1,000 |
| **Total** | **~284,000** | **~174,000** | **~458,000** |

Share of production code: GnollBench ~66%, Gnoll Overseer ~30%, GnollHack Account ~4%.

Production code by language:

| Component | C# | TypeScript | HTML templates | SCSS | Razor (`.cshtml`) |
|---|---:|---:|---:|---:|---:|
| GnollHack Account | ~6,900 | — | — | ~1,100 | ~3,200 |
| Gnoll Overseer | ~42,400 | ~20,400 | ~7,700 | ~13,500 | — |
| GnollBench | ~89,500 | ~64,800 | ~19,600 | ~13,900 | — |

Documentation is not counted above: `docs/` holds ~18,600 lines, of which ~13,900 are the four GnollBench documents.

### Database tables

The shared SQL Server database has **54 tables**. Each is assigned to the component whose code reads and writes it.

| Component | Tables | Share | What they hold |
|---|---:|---:|---|
| GnollBench | 23 | ~43% | suites and questions, runs and answers, tool calls, series, groups and batteries, comparisons, report documents, scoring profiles, game snapshots, and the model-configuration snapshots runs are recorded against |
| Gnoll Overseer | 19 | ~35% | chat sessions, messages, attachments and tool calls, AI configurations and API keys, user AI settings and models, usage and error logs, groups, audit and maintenance logs |
| GnollHack Account | 5 | ~9% | game logs, bones and bones transactions, save file tracking, request logs |
| Shared (ASP.NET Identity) | 7 | ~13% | users, roles, claims, logins and tokens |

The table count measures schema breadth, not data volume: row counts and sizes are not measured, and they vary by installation.

### How it was counted

- **Unit**: non-blank lines, comments included, of tracked source files (`.cs`, `.cshtml`, `.ts`, `.html`, `.scss`, `.css`, `.js`). Tables are the `ToTable` entries of the EF Core model snapshot, `GnollHackServer.Data/Migrations/ApplicationDbContextModelSnapshot.cs`.
- **GnollBench** is every source file whose path contains `benchmark`, case-insensitive. Tables are assigned by which code uses them: the 22 `Benchmark*` tables plus `SystemAiConfigurationSnapshots`.
- **Tests** are `Overseer.Tests/`, `*.spec.ts` and `*.testing.ts`.
- **Excluded**: the generated EF Core migrations, vendored libraries in `MobileGnollHackLogger/wwwroot/lib/`, the CSS compiled from SCSS, minified files, and all non-code files (JSON data and model catalogs, tool guides, documentation, AI agent skills).
- **Known biases**: GnollBench code is slightly under-counted, because shared Angular components it relies on heavily (the model picker, data table, PDF viewer and snapshot viewer) and benchmark hooks inside chat files count as Gnoll Overseer; `SystemAiConfigurationSnapshot.cs` counts as Gnoll Overseer code although its table counts as GnollBench. GnollHack Account includes ~74 scaffolded ASP.NET Identity UI files.

## Technology Stack

| Layer | Technology |
|---|---|
| **Runtime** | .NET 10.0 |
| **Web Framework** | ASP.NET Core (Razor Pages + Web API) |
| **Frontend SPA** | Angular 22 with TypeScript |
| **Database** | SQL Server via Entity Framework Core |
| **Authentication** | ASP.NET Identity |
| **Real-Time** | SignalR |
| **AI Providers** | OpenAI, Anthropic (Claude), Google (Gemini) |
| **Search Index** | Lucene.NET (for source code indexing in Overseer) |
| **Embeddings** | ONNX Runtime (local embeddings for document RAG) |
| **Documents** | QuestPDF (PDF), Open XML SDK (Word), PdfPig (PDF parsing), Markdig |
| **Charts** | Chart.js with ng2-charts |
| **Email** | Azure Communication Services |
| **Styling** | SCSS (compiled to CSS) with Bootstrap |
| **Testing** | xUnit v3 (Microsoft.Testing.Platform); Vitest with Playwright for the Angular client |
| **Error Monitoring** | Sentry (ASP.NET Core and Angular) |

## 📚 Documentation

Developer guides, command references, release checklists, and architectural documentation are centralized in the [`docs/`](docs/) directory. This section is the complete index.

### Gnoll Overseer — operations

| Document | Description |
|---|---|
| [**Release Checklist**](docs/overseer/release-checklist.md) | Step-by-step checklist with exact terminal commands for cutting an Overseer release, from tests and version bump to Sentry source maps and production deployment. |
| [**Commands Reference**](docs/overseer/commands.md) | CLI commands to develop, build, test, and run the backend and frontend, and to apply database migrations. |
| [**Changelog Guide**](docs/overseer/changelog-guide.md) | JSON schema, change classification types, and editing rules for `Overseer/Data/release-notes.json`. |
| [**AI Changelog Generation**](docs/overseer/ai-changelog.md) | Generating release notes from Git commits with a local AI agent. |
| [**Sentry Source Maps Guide**](docs/overseer/sentry-sourcemaps.md) | Generating, injecting Debug IDs into, and uploading Angular source maps to Sentry while keeping them out of the public deployment. |
| [**Sentry Logging Architecture**](docs/overseer/sentry-logging-architecture.md) | Sentry crash logging, server event processing, proxy tunneling, and frontend network error suppression. |
| [**Test Configuration & Secrets**](docs/overseer/test-configuration.md) | User Secrets schema, AI credentials for live API tests, and setup troubleshooting. |

### Gnoll Overseer — architecture

| Document | Description |
|---|---|
| [**Tool Batching & Execution**](docs/overseer/tool-batching-and-execution.md) | Multi-turn tool execution, concurrency throttles, output budgets, and real-time streaming. |
| [**Subagents**](docs/overseer/subagents.md) | The coordinator-specialist multi-agent architecture: delegation, specialist sub-agents, and cancellation. |
| [**Chat & Data Retention**](docs/overseer/chat-data-retention.md) | Session quotas, the soft-delete lifecycle, tool call payload pruning, attachment cleanup, and automated database maintenance. |
| [**Chat Response Telemetry & Cost**](docs/overseer/chat-response-telemetry.md) | Response timing (TTFT, duration), context window usage, whole-turn token costing, and operator cost attribution. |
| [**Data Privacy Framework**](docs/overseer/data-privacy-framework.md) | How Overseer protects user content — confidential and ephemeral sessions, encryption, provider trust, secret masking, document ingestion — and what it does not claim. |
| [**API Key Alerts**](docs/overseer/api-key-alerts.md) | E-mail alerts when a system API key runs out of credit or fails, and their throttling. |

### Gnoll Overseer — AI models

| Document | Description |
|---|---|
| [**Adding AI Models**](docs/overseer/adding-ai-models.md) | Adding new LLM models to the Overseer model catalogs. |
| [**Gemini Service Tier Measurements**](docs/overseer/gemini-service-tier-measurements.md) | Measured availability, latency, and `service_tier` honoring for each supported Gemini model. A dated snapshot. |
| [**Anthropic Model Latency Measurements**](docs/overseer/anthropic-model-latency-measurements.md) | Measured response time, time-to-first-token, availability, and error behavior of the supported Claude models with adaptive thinking. A dated snapshot. |

### GnollBench

| Document | Description |
|---|---|
| [**AI Intelligence Benchmark**](docs/overseer/ai-benchmark.md) | The single-run harness: purpose, difficulty tiers, blind assessment, scoring, provider error isolation, and reporting. |
| [**Multi-Run Analysis**](docs/overseer/ai-benchmark-multi-run.md) | The statistical method behind series and groups of runs, and what it cannot tell you. |
| [**Multi-Suite Batteries**](docs/overseer/ai-benchmark-multi-suite.md) | Batteries of weighted suites, their execution, and the Overall Intelligence Index. |
| [**Report Packs**](docs/overseer/ai-benchmark-report-pack.md) | AI-written documents about the models of a comparison or a run, rendered to PDF and Word. |

### GnollHack Account

| Document | Description |
|---|---|
| [**Database Migrations**](docs/overseer/commands.md#5-database-migrations-entity-framework-core) | Entity Framework Core migrations, in the Commands Reference. |
| [**Styles & SCSS**](docs/overseer/commands.md#6-scss-compilation-host-styles) | SCSS compilation of the host styles, in the Commands Reference. |

AI agent rules are in [`.agents/AGENTS.md`](.agents/AGENTS.md), and project skills in [`.agents/skills/`](.agents/skills/).

## Repository Structure

```
MobileGnollHackLogger/          # Solution root
├── .agents/                    # AI agent rules (AGENTS.md) and project skills
├── .claude/                    # Claude Code adapter over .agents/
├── docs/                       # Central repository documentation
│   └── overseer/               #   Overseer and GnollBench guides and specifications
├── MobileGnollHackLogger/      # GnollHack Account web app (Razor Pages)
│   ├── Pages/                  #   Razor pages (Index, TopScores, RecentGames, Statistics, etc.)
│   ├── Areas/                  #   Razor areas
│   │   ├── API/                #     Game client API (logs, bones, dumplogs, replays, save tracking, Junethack)
│   │   └── Identity/           #     ASP.NET Identity UI
│   ├── Content/                #   Email templates
│   └── wwwroot/                #   Static assets (CSS, JS, images)
├── Overseer/                   # Gnoll Overseer AI assistant web app
│   ├── ClientApp/              #   Angular SPA source
│   ├── Controllers/            #   API controllers (Chat, Auth, Admin, AdminBenchmark, Settings, Sessions)
│   ├── Models/                 #   API request and response models
│   ├── Middleware/             #   HTTP middleware (security headers)
│   ├── Security/               #   Authorization requirements and rate-limit policies
│   ├── Services/               #   AI chat service, source code indexing, providers
│   │   ├── Agents/             #     Sub-agents
│   │   ├── ApiKeyAlerts/       #     API key failure and expiration alerts
│   │   ├── Benchmarking/       #     GnollBench, the AI benchmark
│   │   ├── Documents/          #     Attachment document parsing
│   │   ├── ModelCatalogs/      #     Per-provider AI model catalogs
│   │   ├── Privacy/            #     Data privacy framework
│   │   ├── Providers/          #     LLM provider implementations (OpenAI, Anthropic, Google)
│   │   ├── Rag/                #     Document retrieval with local embeddings
│   │   └── Tools/              #     AI tool definitions and handlers
│   ├── Hubs/                   #   SignalR hub for real-time chat streaming
│   ├── Resources/              #   Embedded resources (PDF fonts)
│   ├── ToolGuides/             #   Markdown guides that shape AI tool behavior
│   └── Data/                   #   Static data files (flag descriptions, release notes, etc.)
│       └── DefaultSuites/      #     Default GnollBench question suites
├── GnollHackServer.Data/       # Shared data access library
│   └── Migrations/             #   EF Core migrations
├── Overseer.Tests/             # Test project
├── global.json                 # .NET SDK settings; selects the Microsoft.Testing.Platform test runner
├── MobileGnollHackLogger.slnx  # Visual Studio solution file
└── LICENSE                     # MIT License
```

## Getting Started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org/) (LTS) and npm — required for the Overseer Angular frontend
- [SQL Server Express](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) — free edition of SQL Server for local development
- [SQL Server Management Studio (SSMS)](https://learn.microsoft.com/en-us/ssms/download-sql-server-management-studio-ssms) — for creating and managing the database
- Visual Studio 2026 (recommended) or any compatible .NET IDE

### Database Setup

1. **Install SQL Server Express** if you haven't already. During installation, note the instance name (the default is `SQLEXPRESS`).

2. **Create the database** using SQL Server Management Studio (SSMS):
   - Open SSMS and connect to your SQL Server Express instance (the server name is typically `.\SQLEXPRESS` or `localhost\SQLEXPRESS`).
   - Right-click on **Databases** in the Object Explorer and select **New Database...**.
   - Enter `GnollHackDb` as the database name and click **OK**.

3. **Apply Entity Framework migrations** to create the schema (after building the solution — see [Building](#building) below):
   ```bash
   dotnet ef database update -p GnollHackServer.Data -s MobileGnollHackLogger
   ```

### Configuration

Sensitive configuration data should not be stored in `appsettings.json`. Instead, this project uses [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) for local development.

To configure secrets in Visual Studio, right-click on the project in the Solution Explorer, select **Manage User Secrets**, and paste the following JSON templates. Replace the placeholder values with your actual data.

**For MobileGnollHackLogger:**
Right-click on the `MobileGnollHackLogger` project → **Manage User Secrets**, and paste:
```json
{
  "ConnectionStrings": {
    "SqlDatabaseConnection": "Server=.\\SQLEXPRESS;Database=GnollHackDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True",
    "EmailConnection": "endpoint=https://<your-communication-service>.communication.azure.com/;accesskey=<your-access-key>"
  },
  "ReplayPath": "C:\\path\\to\\replays",
  "LogFile": "C:\\path\\to\\logs\\gnollhack_account.log",
  "GoogleTagManagerID": "",
  "EncryptionKeyString": "<32-character-key>",
  "EncryptionIVString": "<16-character-iv>",
  "DumpLogPath": "C:\\path\\to\\dumplogs",
  "BonesPath": "C:\\path\\to\\bones",
  "AntiForgeryToken": "<anti-forgery-token>",
  "BonesVersionCompatibilityInfo": [
    {
      "Version": 0,
      "Label": "Older"
    },
    {
      "Version": 67239937,
      "Label": "4.2.0"
    },
    {
      "Version": 67305473,
      "Label": "4.3.0"
    }
  ]
}
```

**For Overseer:**
Right-click on the `Overseer` project → **Manage User Secrets**, and paste:
```json
{
  "ConnectionStrings": {
    "SqlDatabaseConnection": "Server=.\\SQLEXPRESS;Database=GnollHackDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True",
    "EmailConnection": "endpoint=https://<your-communication-service>.communication.azure.com/;accesskey=<your-access-key>"
  },
  "GitHub": {
    "PersonalAccessToken": "ghp_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
  },
  "WikiPath": "C:\\path\\to\\GnollHackWiki",
  "SourceCodePath": "C:\\path\\to\\GnollHack",
  "MaxWikiFileSizeKB": 100,
  "KbPath": "C:\\path\\to\\overseer_knowledgebase",
  "DumpLogPath": "C:\\path\\to\\dumplogs",
  "ConversationsDataLocation": "C:\\path\\to\\overseer_data\\conversations",
  "Benchmark": {
    "ReportPack": {
      "ChartsDataLocation": "C:\\path\\to\\overseer_data\\charts"
    }
  },
  "AntiForgeryToken": "<anti-forgery-token>",
  "AesEncryptionKey": "<base64-encoded-key>",
  "Admins": "AdminUser1,AdminUser2",
  "AdminNotificationEmail": "admin@example.com"
}
```

> **Note:** If your SQL Server Express instance uses a different name, replace `.\SQLEXPRESS` with the correct server and instance name (e.g., `localhost\MYINSTANCE`).

> **Note:** `Benchmark:ReportPack:ChartsDataLocation` is the absolute folder where the chart images of AI Benchmark report documents are stored. It is created on first use and is not part of the database backup; see [`docs/overseer/ai-benchmark-report-pack.md`](docs/overseer/ai-benchmark-report-pack.md) § 13. Without it, report documents download without charts.

### Building

1. **Clone the repository:**
   ```bash
   git clone https://github.com/hyvanmielenpelit/MobileGnollHackLogger.git
   cd MobileGnollHackLogger
   ```

2. **Build the ASP.NET Core backends:**
   The Overseer and GnollHack Account ASP.NET Core backends are built with:
   ```bash
   dotnet build
   ```

3. **Apply database migrations** (see [Database Setup](#database-setup) above):
   ```bash
   dotnet ef database update -p GnollHackServer.Data -s MobileGnollHackLogger
   ```

4. **Build the Overseer Angular frontend:**
   The frontend's Angular application is built with `npm run build` in the `Overseer/ClientApp` directory:
   ```bash
   cd Overseer/ClientApp
   npm ci
   npm run build
   ```

### Running Locally

- **GnollHack Account:**
  ```bash
  dotnet run --project MobileGnollHackLogger
  ```

- **Gnoll Overseer:**
  ```bash
  dotnet run --project Overseer
  ```
  The host serves the Angular client from `Overseer/wwwroot` as last built by `npm run build` in
  `Overseer/ClientApp`; run that build after client changes.

### Publishing

To publish the web applications (GnollHack Account or Overseer) to a production environment, use Visual Studio:
1. Right-click on the respective project (`MobileGnollHackLogger` or `Overseer`) in the Solution Explorer.
2. Select **Publish...** from the context menu.
3. Follow the wizard to configure your publish profile (e.g., to a local folder, Azure, IIS) and click **Publish**.

## Related Repositories

| Repository | Description |
|---|---|
| [GnollHack](https://github.com/hyvanmielenpelit/GnollHack) | The game itself — C core engine and .NET MAUI frontend |
| [GnollHackWiki](https://github.com/hyvanmielenpelit/GnollHackWiki) | Community wiki for GnollHack game content |

## Contributing

Contributions are welcome! To get started:

1. Check the [Issues](https://github.com/hyvanmielenpelit/MobileGnollHackLogger/issues) for open tasks or bug reports.
2. Fork the repository and create a feature branch.
3. Make your changes, ensuring they follow the existing code style.
4. Run the test suites: `dotnet test Overseer.Tests --filter-not-trait "Category=UsesExternalApi"` from the repository root, and `npm run test:headless` in `Overseer/ClientApp/`. Without the filter, the .NET run calls live OpenAI, Anthropic and Google APIs and spends money.
5. Submit a Pull Request with a clear description of what you changed and why.

## License

This project is licensed under the **MIT License**. See [LICENSE](LICENSE) for details.

Copyright © 2026 Hyvän mielen pelit ry
