# MobileGnollHackLogger — Agent Rules

## Project Identity

This is **MobileGnollHackLogger**, the GnollHack Account Server. It is a server-side ASP.NET Core web application that serves as the backend for the GnollHack mobile game (a NetHack variant). It records game results, shares bones files between players, tracks save files for anti-cheat, serves character dump logs, provides leaderboards and statistics pages, and generates xlogfile output for external scoreboards (NetHack Scoreboard and JunetHack).

- **Repository:** `hyvanmielenpelit/MobileGnollHackLogger`
- **License:** MIT (Copyright © 2023 Tommi Gustafsson)
- **Framework:** ASP.NET Core (.NET 9.0) with Razor Pages and Web API controllers
- **Database:** SQL Server via Entity Framework Core 9 (Code-First)
- **Email:** Azure Communication Services (sender: `DoNotReply@gnollhack.com`)

## Coding Standards

### Language and Framework
- C# with .NET 9.0, nullable reference types enabled, implicit usings enabled.
- Follow existing code style: minimal API pattern in `Program.cs`, standard ASP.NET conventions.
- Use `var` for local variables when the type is obvious from the right-hand side.
- Prefer `string.IsNullOrEmpty()` for null/empty checks (this is the pattern used throughout).

### Naming Conventions
- PascalCase for public properties, methods, classes, and enums.
- camelCase for local variables and method parameters.
- Private fields use underscore prefix: `_dbContext`, `_signInManager`, `_dbLogger`.
- Database entity classes use PascalCase property names.
- Enum values use PascalCase (e.g., `LogType.GameLog`, `RequestLogSubType.MainFunctionality`).

### Entity Framework
- Use Code-First migrations. The migration tool is `dotnet-ef` (version 8.0.0, configured in `MobileGnollHackLogger/.config/dotnet-tools.json`).
- Add new migrations with: `dotnet ef migrations add <MigrationName> --project MobileGnollHackLogger`.
- All entity string properties should have `[MaxLength(N)]` attributes. Follow existing size conventions (see the Data Models skill for specific sizes per entity).
- Primary keys use `long Id` with `[DatabaseGenerated(DatabaseGeneratedOption.Identity)]` and `[PrimaryKey(nameof(Id))]`.
- Default values for date columns use `HasDefaultValueSql("getutcdate()")` in `OnModelCreating`.
- Foreign keys to ASP.NET Identity users use `[ForeignKey("AspNetUser")] string? AspNetUserId` with a companion `ApplicationUser? AspNetUser` navigation property.
- Indexes use either `[Index]` attribute on the class or fluent API in `OnModelCreating`.

### API Controllers
- All API controllers live in `Areas/API/` (flat structure, not nested subdirectories).
- Controllers use attribute routing with `[Route]` and `[HttpGet]`/`[HttpPost]`.
- Routes are NOT prefixed with `api/` uniformly — some use `api/[controller]` (SaveFileTracking, Replay), some use bare names (`xlogfile`, `bones`, `dumplog`, `junethack/{username}`). Check the actual `[Route]` attribute.
- All mutating endpoints authenticate via `LoginInfoModel` (or a subclass) containing `UserName`, `Password`, and `AntiForgeryToken`. Authentication is per-request via `SignInManager.PasswordSignInAsync` — this is NOT token-based or cookie-based.
- The `AntiForgeryToken` is validated against `_configuration["AntiForgeryToken"]` before password verification.
- After login succeeds, check `user.IsBanned`, `user.IsGameLogBanned` (for GameLog), and `user.IsBonesBanned` (for Bones) before processing.
- Log all API requests using `DbLogger` to the `RequestInfo` table. Each controller instantiates its own `DbLogger` with the appropriate `LogType`.

### Razor Pages
- All pages live in `Pages/`. The shared layout is `Pages/Shared/_Layout.cshtml`.
- Pages use Bootstrap 5 for styling (included via `wwwroot/lib/bootstrap/`).
- Test pages (prefixed `Test*`) are debug/development tools for exercising API endpoints — do not remove them.

### Error Handling
- Throw exceptions for critical startup configuration failures (missing connection strings, missing email templates, missing paths).
- For API endpoints, return appropriate HTTP status codes with plain text error messages via `Content()` or `StatusCode()`.
- Always log errors to the database via `DbLogger.LogRequestAsync()` before returning error responses.
- Common status code patterns: 400=Bad Request, 401=Invalid AntiForgeryToken, 403=Login Failed, 409=Duplicate/Conflict, 410=Gone, 412=2FA Required, 423=Banned/Locked, 500=Server Error.

## Architecture Constraints

### Do Not Change
- The xlogfile format output — `XLogFileLine.ToString(OutputMode)` and its 47 header fields. Changes will break NetHack Scoreboard and JunetHack integrations.
- The `LoginInfoModel`-based per-request authentication pattern with `AntiForgeryToken`. The mobile game client sends credentials with each API call.
- The bones file storage path structure: `{BonesPath}/{UserName}/{OriginalFileName}_{index}`.
- The dump log file storage structure: `{DumpLogPath}/{UserName}/gnollhack.{Name}.{StartTimeUTC}.{ext}`.
- The `SaveFileTracking` AES-256-CBC encryption/decryption of database IDs (using `EncryptionKeyString` and `EncryptionIVString` from configuration).
- The `DbLogger` deduplication logic that merges identical log entries by incrementing `Count` and updating `LastDate`.

### Security Rules
- Connection strings (`SqlDatabaseConnection`, `EmailConnection`), `AntiForgeryToken`, `EncryptionKeyString`, `EncryptionIVString`, `DumpLogPath`, `BonesPath`, and `LogFile` are all stored in User Secrets or environment-specific configuration — never commit them to source control.
- User Secrets ID: `aspnet-MobileGnollHackLogger-e0f17738-c6d6-4a69-9228-432663eb945b`.
- Never log passwords to the database or files.
- Always validate that the authenticated user owns the data they are submitting (e.g., `xLogFileLine.Name = model.UserName` in LogController).
- The `SaveFileTrackingController.Use` endpoint cross-validates `UserName`, `TimeStamp`, `Sha256`, and `FileLength` between the model and database to prevent tampering.

### Configuration Keys (from User Secrets / appsettings)
| Key | Used By | Purpose |
|-----|---------|---------|
| `ConnectionStrings:SqlDatabaseConnection` | Program.cs | SQL Server connection |
| `ConnectionStrings:EmailConnection` | Program.cs → EmailSender | Azure Communication Services |
| `AntiForgeryToken` | All POST controllers | Shared secret for API auth |
| `DumpLogPath` | LogController, DumpLogController | Base path for dump log files |
| `BonesPath` | BonesController | Base path for bones files |
| `EncryptionKeyString` | SaveFileTrackingController | AES-256 key (32 bytes UTF-8) |
| `EncryptionIVString` | SaveFileTrackingController | AES-128 IV (16 bytes UTF-8) |
| `LogFile` | LogFileLogger | File logging path |
| `GoogleTagManagerID` | _Layout.cshtml | Google Tag Manager container ID |

## Build and Run

### Prerequisites
- .NET 9.0 SDK
- SQL Server instance
- Azure Communication Services resource (for email)
- User Secrets configured with all required keys above

### Commands
```bash
# Restore tools (dotnet-ef)
dotnet tool restore

# Build
dotnet build

# Run locally
dotnet run --project MobileGnollHackLogger   # HTTP on localhost:5148
dotnet run --project MobileGnollHackLogger --launch-profile https  # HTTPS on localhost:7097

# Entity Framework migrations
dotnet ef migrations add <Name> --project MobileGnollHackLogger
dotnet ef database update --project MobileGnollHackLogger
```

### Development Configuration
- Launch profiles: `http` (port 5148), `https` (port 7097), `IIS Express` (port 31165/44344).
- `appsettings.Development.json` enables `DetailedErrors`.
- No CI/CD pipeline is configured (`.github/workflows/` is empty).

## Testing
- There are no automated unit tests or integration tests in this repository.
- Test pages (`Test.cshtml`, `TestBones.cshtml`, `TestSFTCreate.cshtml`, `TestSFTUse.cshtml`) provide browser-based forms for manual API testing.

## Domain Terminology

| Term | Meaning |
|------|---------|
| **GnollHack** | A variant of the classic roguelike game NetHack |
| **Bones file** | A saved dungeon level from a dead player, shared with other players to encounter |
| **xlogfile** | A standardized tab-separated log format used by NetHack variants to record game results |
| **Dump log** | A character summary file generated at game end (available in plain text and HTML) |
| **Ascension** | Winning the game (the death text is `"ascended"`) |
| **Mode** | Game rule variant: `normal`=Classic, `debug`=Wizard, `explore`=Explore, `casual`=Casual, `reloadable`=Reloadable, `modern`=Modern |
| **Scoring** | Whether the game counts for scoring: `"yes"` means it counts |
| **NetHack Scoreboard** | External service that aggregates scores from NetHack variants |
| **JunetHack** | Annual NetHack tournament (June) that uses xlogfile data |
| **Port** | The platform-specific build of GnollHack (e.g., Android, iOS, Windows) |
| **Conduct** | Self-imposed challenges tracked as bit flags |
| **Difficulty** | Game difficulty level: -4=Standard through 2=Grand Master |
| **Role** | Character class: Arc, Bar, Cav, Hea, Kni, Mon, Pri, Ran, Rog, Sam, Tou, Val, Wiz |
| **Race** | Character race: Hum, Dwa, Elf, Gnl, Orc |
