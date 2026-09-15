# Overseer Build, Test, and Run Commands

This guide provides a comprehensive reference of all command-line operations used to develop, build, test, and run the Overseer application (ASP.NET Core backend + Angular frontend).

---

## 1. Quick Reference

| Action | Working Directory | Command |
| :--- | :--- | :--- |
| **Run Entire App (Backend + SPA Proxy)** | `Overseer/` | `dotnet run` |
| **Run Frontend Unit Tests (Headless)** | `Overseer/ClientApp/` | `npm run test:headless` |
| **Run Backend Unit & Integration Tests** | `Overseer.Tests/` | `dotnet test --filter-not-trait "Category=UsesExternalApi"` |
| **Build Frontend (Production)** | `Overseer/ClientApp/` | `npm run build` |
| **Build Backend** | `Overseer/` | `dotnet build` |
| **Apply Migrations to a Server Database** | Root, then the server's MobileGnollHackLogger site folder | *(See § 5.1 — migration bundle)* |
| **Release Application** | Root | *(See [release-checklist.md](release-checklist.md))* |

---

## 2. Frontend Development (`Overseer/ClientApp/`)

All frontend commands must be run from the `Overseer/ClientApp/` directory.

### Dependencies
```bash
# Install npm dependencies
npm install

# Clean install from package-lock.json
npm ci
```

### Running the Frontend
```bash
# Start standalone Angular development server on port 44447
npm start
# or:
npx ng serve --port 44447
```

### Building the Frontend
```bash
# Production build (compiles into ../wwwroot with hidden sourcemaps)
npm run build
# or:
npx ng build --configuration production

# Development build with live watch
npm run watch
# or:
npx ng build --watch --configuration development
```

> [!IMPORTANT]
> **Static Assets Rule**: `Overseer/wwwroot/` is a build output directory wiped on every build. Never edit files in `wwwroot/` directly. Place static assets and images in `Overseer/ClientApp/public/` instead.

### Frontend Unit Testing (Karma & Jasmine)
```bash
# Run entire test suite once in Headless Chrome (Recommended for CI & AI agents)
npm run test:headless
# or:
npx ng test --no-watch --browsers=ChromeHeadless
# or:
npm test -- --no-watch --browsers=ChromeHeadless

# Run a specific test file (Headless)
npx ng test --include="src/app/chat/chat.component.spec.ts" --no-watch --browsers=ChromeHeadless
npx ng test --include="src/app/chat/markdown.pipe.spec.ts" --no-watch --browsers=ChromeHeadless

# Interactive watch mode with browser GUI (for local browser debugging)
npm test
# or:
npx ng test
```

> [!TIP]
> **Headless Execution**: Always use `npm run test:headless` or `--browsers=ChromeHeadless` during automated runs to prevent popup browser windows from disrupting your workflow.

### Sentry Sourcemaps & Debug IDs
```bash
# Inject Debug IDs into generated wwwroot assets (run after production build)
npx sentry-cli sourcemaps inject ../wwwroot

# Upload sourcemaps to Sentry for a specific release version
npx sentry-cli sourcemaps upload --release <version> ../wwwroot
```
*(Refer to [sentry-sourcemaps.md](sentry-sourcemaps.md) for full instructions).*

---

## 3. Backend Development (`Overseer/`)

All backend commands must be run from the `Overseer/` directory.

### Building
```bash
# Build the ASP.NET Core project (automatically synchronizes version to ClientApp/package.json)
dotnet build

# Clean build artifacts
dotnet clean
```

### Running
```bash
# Run Overseer (launches ASP.NET Core and SPA proxy via npm start)
dotnet run

# Run with specific launch profile
dotnet run --launch-profile https
```

### Publishing
```bash
# Publish for production release (triggers Angular build and excludes .map files from package)
dotnet publish -c Release
```
*(Published output will be in `bin/Release/net10.0/publish/`).*

---

## 4. Backend Testing (`Overseer.Tests/`)

All test commands must be run from the `Overseer.Tests/` (or repository root) directory.

> [!IMPORTANT]
> **`dotnet test` depends on the repository-root `global.json`.** `Overseer.Tests` is a
> Microsoft.Testing.Platform application, and from the .NET 10 SDK onward `dotnet test` will not
> run one through the old VSTest path. `global.json` carries the opt-in:
>
> ```json
> { "test": { "runner": "Microsoft.Testing.Platform" } }
> ```
>
> Without it every command in this section fails with *"Testing with VSTest target is no longer
> supported by Microsoft.Testing.Platform on .NET 10 SDK and later."* On this toolchain
> (SDK 10.0.401) a `dotnet.config` `[dotnet.test.runner]` entry does **not** work as a substitute,
> despite current Microsoft documentation; see the `testing-guidelines` skill § 1a.
>
> The filter arguments below are the **native xunit v3 options** (`--filter-not-trait`,
> `--filter-trait`, `--filter-class`, `--filter-method`). `--filter "Category!=UsesExternalApi"` and
> `--filter "FullyQualifiedName~X"` are the VSTest-syntax equivalents, which MTP still accepts as a
> compatibility shim — they work, but this repository does not document them, for the reason in
> `testing-guidelines` § 1a.

### Running Backend Tests
```bash
# Run all tests SKIPPING external AI API calls (Recommended - saves AI quota)
dotnet test --filter-not-trait "Category=UsesExternalApi"

# Run all tests INCLUDING external AI API calls - consumes API quota and spends real money.
# Ask first; on a machine with the live-test secrets configured this bills three providers.
dotnet test

# Run a specific test class (fully qualified, or with a leading/trailing wildcard)
dotnet test --filter-class "Overseer.Tests.UnitTests.BenchmarkScoringTests"
dotnet test --filter-class "*BenchmarkScoringTests"

# Run a specific test method
dotnet test --filter-method "Overseer.Tests.ChatServiceTests.EmptyResponseNotice_NamesTheProviderFinishReason"
dotnet test --filter-method "*EmptyResponseNotice_*"
```

> [!WARNING]
> **AI API Quota**: Always use `--filter-not-trait "Category=UsesExternalApi"` unless you have explicit permission to consume live AI API tokens.
>
> **The filter fails open, so check it rather than trusting it.** A typo in the trait name or its
> value matches nothing, therefore excludes nothing, and runs the live tests without complaint —
> `Categoy=UsesExternalApi` and `Category=UsesExternalApis` both select all 1482 tests, and so does
> writing `!=` inside `--filter-not-trait`, which is the old VSTest habit. Verify a filter with a
> discovery run, which executes nothing:
>
> ```bash
> dotnet test Overseer.Tests --list-tests --filter-trait "Category=UsesExternalApi"
> ```
>
> It must list exactly the live-API tests (4 as of 2026-09-09). Never verify a filter by running
> the suite unfiltered.

---

## 5. Database Migrations (Entity Framework Core)

Entity Framework Core migrations are stored in `GnollHackServer.Data` and startup from `MobileGnollHackLogger`.

Run these commands from the repository root (`MobileGnollHackLogger/`):

```bash
# Add a new migration
dotnet ef migrations add <MigrationName> -p GnollHackServer.Data -s MobileGnollHackLogger -o Migrations

# Apply migrations directly to the local database
dotnet ef database update -p GnollHackServer.Data -s MobileGnollHackLogger
```

### 5.1 Applying Migrations to a Server Database (Migration Bundle)

Neither `Overseer/Program.cs` nor `MobileGnollHackLogger/Program.cs` calls `Migrate()`, so starting the site never upgrades its database. Server migrations are applied explicitly, with an EF Core **migration bundle**, **before the new build is started**. Take a database backup first (see [release-checklist.md](release-checklist.md) § 5 and § 7).

**1. Build the bundle** on the development machine, from the repository root, from the same commit as the build being deployed:

```bash
dotnet ef migrations bundle -p GnollHackServer.Data -s MobileGnollHackLogger -r win-x64 --self-contained -o <path>\efbundle.exe
```

`--self-contained` includes the .NET runtime, so the server needs neither the SDK nor a runtime for it. Add `--force` to overwrite an existing file. A versioned name such as `efbundle_overseer_v<version>.exe` makes it clear which release a bundle belongs to.

**2. Run it on the server from the MobileGnollHackLogger site folder:**

```powershell
Set-Location '<MobileGnollHackLogger site folder>'
$conn = Read-Host 'Connection string'
& '<path>\efbundle.exe' --connection $conn
```

- **Working directory**: there is no `IDesignTimeDbContextFactory`, so the bundle starts the `MobileGnollHackLogger` host to obtain the `DbContext`. That host's `Program.cs` throws unless `ConnectionStrings:SqlDatabaseConnection` and `ConnectionStrings:EmailConnection` are configured and `Content\ConfirmAccountEmail.html` and `Content\ForgotPasswordEmail.html` exist under the content root, which is the working directory. The site folder has all of them. A failure while the host starts happens before the database is touched.
- **Connection string**: pass the *value* of `ConnectionStrings:SqlDatabaseConnection` for the database being upgraded. If you type it inline instead of using `Read-Host`, use PowerShell single quotes and **single** backslashes: copied from `appsettings.json`, the server name carries the JSON-escaped `\\`, which fails with *"Instance failure."* before connecting. `Read-Host` also keeps the value out of the PowerShell history.
- **Rights**: no Windows elevation is needed. The SQL login needs `db_owner`, or `db_ddladmin` plus `db_datareader` and `db_datawriter`, on the database.
- **What it does**: applies every migration missing from `__EFMigrationsHistory`, in order, one transaction per migration, printing `Applying migration '<id>'` for each and `Done.` at the end. It stops at the first failure; the migrations before it stay applied and recorded. Against an up-to-date database it applies nothing.
- **Check**: once the new build is running, the Overseer admin Storage tab's *Schema* section should show *Pending: None*.

### 5.2 Why Not an Idempotent SQL Script

Do **not** apply this repository's migrations with `dotnet ef migrations script --idempotent` (`-i`):

- EF Core 10 wraps each migration of an idempotent script in a single `GO` batch. SQL Server compiles a whole batch before executing any of it, so a migration that adds a column and then updates it in the same migration fails compilation with Msg 207 *"Invalid column name"* and is skipped entirely. Its `IF NOT EXISTS` guards do not help, because the batch never runs. Examples: `20260903075124_AddBenchmarkIntegrityAndTiming`, `20260903111808_AddBenchmarkSecondOpinionAssessor`, `20260909160427_AddChatMessageSnapshotFlags`.
- SSMS continues past a failed batch. A later migration that then fails at run time after its `BEGIN TRANSACTION` leaves a transaction open; the following `COMMIT`s only close nested levels, and closing the window rolls everything after that point back.
- On 2026-09-15 this happened on the test server (upgrade from `overseer/v1.0.28` to 1.1.0, 57 migrations): eight migrations failed compilation, `20260909093810_AddBenchmarkCorpusFingerprints` left a transaction open, and 29 of the 57 ended up applied. The remaining 28 were then applied with a bundle.
- `dotnet ef database update` and bundles send each statement as its own command, which is why development machines never hit this.

`dotnet ef migrations script` is still useful for **reading** the SQL a migration will run; just do not use its output to apply the migrations.

---

## 6. SCSS Compilation (Host Styles)

For the main MobileGnollHackLogger ASP.NET Core host pages:

Run from the `MobileGnollHackLogger/` directory:

```bash
# Standard CSS
npx sass wwwroot/css/site2.scss wwwroot/css/site2.css

# Minified CSS (Compressed)
npx sass wwwroot/css/site2.scss wwwroot/css/site2.min.css --style compressed
```
*(Note: Overseer's Angular styles in `Overseer/ClientApp/src/styles.scss` are compiled automatically by Angular CLI during `ng build` / `ng test`).*
