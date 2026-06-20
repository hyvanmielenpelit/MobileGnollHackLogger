# MobileGnollHackLogger — Agent Rules

**Identity:** MobileGnollHackLogger is the backend ASP.NET Core server for the GnollHack mobile game. It handles game results, bones files, save file tracking, dump logs, and xlogfile output for NetHack Scoreboard/JunetHack.
**Repo:** `hyvanmielenpelit/MobileGnollHackLogger` | **Framework:** .NET 9.0 (C#) | **DB:** SQL Server (EF Core 9)

## 🚨 Critical Constraints
*   **DO NOT** change the `XLogFileLine` output format (47 header fields). External integrations depend on it exactly.
*   **DO NOT** change the per-request auth pattern (`LoginInfoModel` with `UserName`, `Password`, `AntiForgeryToken`). The mobile client relies on it.
*   **DO NOT** log passwords.
*   **NEVER** commit connection strings, secrets, or `AntiForgeryToken` to source control. They live in User Secrets.
*   **ALWAYS** validate that the authenticated user owns the data they submit (e.g., `xLogFileLine.Name == model.UserName`).

## 🛠️ Coding Standards
*   **Language:** C# 9.0+, nullable enabled, implicit usings enabled. Use `var` when type is obvious.
*   **Names:** `PascalCase` for public members/enums, `camelCase` for locals, `_camelCase` for private fields.
*   **Null Checks:** Prefer `string.IsNullOrEmpty()`.

## 🗄️ Database (Entity Framework)
*   **Migrations:** Code-First. Tool config at `MobileGnollHackLogger/MobileGnollHackLogger/.config/dotnet-tools.json`. Run: `dotnet ef migrations add <Name> --project MobileGnollHackLogger`
*   **Keys:** Primary keys are `long Id` with `[DatabaseGenerated(DatabaseGeneratedOption.Identity)]`.
*   **Dates:** Default date columns to `getutcdate()` via `HasDefaultValueSql` in `OnModelCreating`.
*   **Strings:** All entity string properties MUST have `[MaxLength(N)]`.

## 🌐 API Controllers & Auth
*   **Location:** `Areas/API/`
*   **Auth Flow:** Validate `AntiForgeryToken` -> `SignInManager.PasswordSignInAsync` -> Check `user.IsBanned` flags -> Process.
*   **Logging:** All endpoints MUST use `DbLogger` to write to the `RequestInfo` table.

## ⚙️ Configuration Secrets
| Key | Purpose |
|-----|---------|
| `ConnectionStrings:SqlDatabaseConnection` | SQL DB |
| `ConnectionStrings:EmailConnection` | Azure Email |
| `AntiForgeryToken` | API Auth Secret |
| `DumpLogPath` & `BonesPath` | File storage paths |
| `EncryptionKeyString` & `EncryptionIVString` | AES keys for SaveFileTracking |

## 🏃 Build & Run
```bash
dotnet tool restore
dotnet build
dotnet run --project MobileGnollHackLogger # http:5148
```
