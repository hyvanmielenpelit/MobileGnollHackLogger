# GnollHack Account Server Software

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Angular](https://img.shields.io/badge/Angular-22-DD0031)](https://angular.io/)

This repository contains the server-side software that powers the online services for [GnollHack](https://github.com/hyvanmielenpelit/GnollHack), a graphical roguelike game derived from NetHack. It provides account management, score tracking, game log recording, bones file sharing, and an AI-powered assistant for players.

## What Is GnollHack?

[GnollHack](https://github.com/hyvanmielenpelit/GnollHack) is a turn-based roguelike game with a graphical tile-based interface, available on Android, iOS, and Windows. It features a rich world of monsters, items, and dungeons. Players create accounts on the **GnollHack Account** server (this repository) to track their scores, share bones files with other players, and participate in the global community.

## Projects in This Repository

This solution contains three main applications and a shared data library. Together they form the complete backend infrastructure for GnollHack's online services.

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
- **Real-Time Streaming** — Uses SignalR for real-time, token-by-token response streaming to the browser.
- **Spoiler Policy** — Enforces a configurable spoiler policy so the AI can respect players who want to discover things on their own.
- **Admin Dashboard** — Provides administrative tools for managing users, AI configurations, API keys, and system settings.

### 📦 GnollHack Server Data (`GnollHackServer.Data/`)

A shared .NET class library that provides the **data access layer** used by both GnollHack Account and Gnoll Overseer. It contains:

- Entity Framework Core database context and entity models (game logs, bones transactions, user accounts, AI chat sessions, AI configuration, etc.)
- ASP.NET Identity integration for user authentication
- Shared utilities (email sending, game data helpers, logging)

### 🧪 Overseer Tests (`Overseer.Tests/`)

The automated test suite for the Overseer project, containing unit tests and integration tests for the AI chat service, tool execution, and API endpoints.

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
| **Charts** | Chart.js with ng2-charts |
| **Email** | Azure Communication Services |
| **Styling** | SCSS (compiled to CSS) with Bootstrap |

## Repository Structure

```
MobileGnollHackLogger/          # Solution root
├── MobileGnollHackLogger/      # GnollHack Account web app (Razor Pages)
│   ├── Pages/                  #   Razor pages (Index, TopScores, RecentGames, Statistics, etc.)
│   ├── Areas/                  #   ASP.NET Identity UI area
│   ├── Content/                #   Email templates
│   ├── Data/                   #   EF Core migrations
│   └── wwwroot/                #   Static assets (CSS, JS, images)
├── Overseer/                   # Gnoll Overseer AI assistant web app
│   ├── ClientApp/              #   Angular SPA source
│   ├── Controllers/            #   API controllers (Chat, Auth, Admin, Settings, Sessions)
│   ├── Services/               #   AI chat service, source code indexing, providers
│   │   ├── Providers/          #     LLM provider implementations (OpenAI, Anthropic, Google)
│   │   └── Tools/              #     AI tool definitions and handlers
│   ├── Hubs/                   #   SignalR hub for real-time chat streaming
│   ├── ToolGuides/             #   Markdown guides that shape AI tool behavior
│   └── Data/                   #   Static data files (flag descriptions, etc.)
├── GnollHackServer.Data/       # Shared data access library
├── Overseer.Tests/             # Test project
├── MobileGnollHackLogger.sln   # Visual Studio solution file
└── LICENSE                     # MIT License
```

## Getting Started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org/) (LTS) and npm — required for the Overseer Angular frontend
- SQL Server (or SQL Server LocalDB for development)
- Visual Studio 2026 (recommended) or any compatible .NET IDE

### Building

1. **Clone the repository:**
   ```bash
   git clone https://github.com/hyvanmielenpelit/MobileGnollHackLogger.git
   cd MobileGnollHackLogger
   ```

2. **Restore and build:**
   ```bash
   dotnet build
   ```

3. **Set up the database** — configure your SQL Server connection string in User Secrets or `appsettings.json`, then run migrations:
   ```bash
   dotnet ef database update -p MobileGnollHackLogger -s MobileGnollHackLogger
   ```

4. **Install Overseer frontend dependencies:**
   ```bash
   cd Overseer/ClientApp
   npm ci
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
  The Angular dev server will start automatically via the SPA proxy.

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
4. Run the test suite: `dotnet test`
5. Submit a Pull Request with a clear description of what you changed and why.

## License

This project is licensed under the **MIT License**. See [LICENSE](LICENSE) for details.

Copyright © 2026 Hyvän mielen pelit ry