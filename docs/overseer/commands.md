# Overseer Build, Test, and Run Commands

This guide provides a comprehensive reference of all command-line operations used to develop, build, test, and run the Overseer application (ASP.NET Core backend + Angular frontend).

---

## 1. Quick Reference

| Action | Working Directory | Command |
| :--- | :--- | :--- |
| **Run Entire App (serves the last client build)** | `Overseer/` | `dotnet run` |
| **Run Frontend Unit Tests (Headless)** | `Overseer/ClientApp/` | `npm run test:headless` |
| **Run Frontend Specs for One Area (watch)** | `Overseer/ClientApp/` | `npx ng test --include=src/app/<area>` |
| **Find Slow Frontend Specs** | `Overseer/ClientApp/` | `npm run test:profile` |
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
# Serve the development build over HTTPS on port 44447 with the ASP.NET Core development
# certificate, proxying /api and /chathub (WebSockets included) to the Overseer host
npm start
```

This is optional. The launch profiles do not start it: the host serves the last `npm run build`
from `Overseer/wwwroot` (see § 3 *Running*). Start the Overseer host first, then `npm start`, and
open `https://localhost:44447` for source maps and hot module replacement. The first run exports
the development certificate as PEM to `%APPDATA%\ASP.NET\https\` (outside the repository);
`serve-https.js` does that and passes any extra arguments on to `ng serve`.

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

### Frontend Unit Testing (Vitest)

The specs run with [Vitest](https://vitest.dev/) in browser mode, in headless Chromium driven by
Playwright, through Angular's `@angular/build:unit-test` builder. `src/test-setup.ts` loads
zone.js's Vitest patch (for `fakeAsync`) and restores spies after every test, and
`vitest-base.config.mts` holds what the builder does not configure itself: the browser, its
headless mode, launch options and 800×600 viewport included. `angular.json` names no browser,
because the builder's `browsers` option would replace the provider and drop its launch options;
for the same reason `CHROME_BIN`, which only that option honors, has no effect.

The `test` target lives in its own `angular.json` project, `ClientAppSpecs`, because the Angular
CLI keys the persistent build cache, and the `.tsbuildinfo` inside it, on the project name. With a
single project, `ng test` and `ng build` / `ng serve` overwrote each other's `.tsbuildinfo` and
re-checked the whole program on every switch between them (5–27 s, measured on 2026-10-03).
Keep that project's `sourceRoot`, `prefix` and `schematics` equal to `ClientApp`'s: `ng generate`
run from inside `src/` resolves to `ClientAppSpecs`. `"incremental": false` in
`tsconfig.spec.json` was rejected as the alternative, because it makes every test build pay the
full re-check.

The browser is Playwright's full Chromium build in its new headless mode
(`launchOptions: { channel: 'chromium' }`), not the default headless shell. Under the headless
shell, parallel runs intermittently delayed `canvas.toBlob` callbacks by about 7 s, enough for a
spec that encodes several images to exceed the 15 s test timeout; measured on 2026-10-03, the
headless shell stalled in 9 of 15 full runs (two of them failed on that timeout) and the full
build in none of 6, without being slower.

Once per machine, download Playwright's Chromium (into `%LOCALAPPDATA%\ms-playwright`, outside the
repository):

```bash
npx playwright install chromium
```

```bash
# Run the entire suite once (Recommended for CI & AI agents)
npm run test:headless
# or:
npx ng test --no-watch

# Run one file, a directory or a glob once
npx ng test --include="src/app/chat/chat.component.spec.ts" --no-watch
npx ng test --include="src/app/admin/benchmark" --no-watch
```

**While iterating (humans only)**, run one area in watch mode: Vitest re-runs the affected tests
on every save, and Ctrl+C stops it. A single file takes seconds rather than a full run.

```bash
npx ng test --include=src/app/admin/benchmark
# narrowed to test names matching a regular expression:
npx ng test --include=src/app/admin/benchmark --filter="diagnostics"
```

> [!IMPORTANT]
> **Agents use `npm run test:headless` or `--no-watch`.** Plain `npm test` or `ng test` in an
> interactive terminal starts watch mode, and the command never returns.

### Profiling the Specs
```bash
# List every test with its duration; tests above 200 ms are marked as slow
npm run test:profile
```

The 200 ms threshold is `slowTestThreshold` in `vitest-base.config.mts`. Files run in parallel, one
per browser page (up to 12 on a 32-thread machine), but the tests of one file run one after another
on one page, so the largest file sets the floor of a full run. Large component specs are therefore
split by area into `<name>.<area>.spec.ts` files that share a `<name>.testing.ts` setup module, and
keep the outer `describe` name so every full test name stays the same: `benchmark.component.*.spec.ts`
(12 files, sharing `benchmark.component.testing.ts`) and `model-comparison.component.*.spec.ts`
(6 files, sharing `model-comparison.component.testing.ts`). As measured on 2026-10-02, after that
split, the largest file sums to about 19 s of a 36 s Vitest run (3,915 specs, 129 files), and no
file stands out: the slowest are the `AdminBenchmarkComponent` parts, whose every spec creates the
whole component. Once the largest file ends well before the run does, splitting it further does not
shorten the run, so check the file finish order (`startTime` and `endTime` per file in the JSON
report) before splitting: on 2026-10-03 the largest file, `benchmark.component.run-report.spec.ts`
(about 18–28 s), ended 6–11 s before every run's end, behind a tail of smaller files.

> [!NOTE]
> **Do not turn chart animation off to speed specs up.** Forcing `prefers-reduced-motion` made the
> model-comparison specs slower (49 s → 75 s): with animation on, Chart.js draws in a later
> animation frame that usually never runs before the spec ends; with it off, every chart is drawn
> synchronously inside the spec.

### Why the Same Run Is Sometimes 2–3× Slower

On the development laptop the same spec file, with the same test order, took 116–120 s in one
hour and 46–48 s an hour later. That swing is the laptop's power and scheduling state (which cores
Windows gives the browser), not the code. **Compare timings only back to back**, with nothing else
running. For the fastest runs, plug the laptop in and set the Windows power mode to *Best
performance* while the suite runs — a setting for the developer to choose, not one an agent
changes.

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
# Run Overseer with the first launch profile (http)
dotnet run

# Run with specific launch profile
dotnet run --launch-profile https
```

Both profiles (`http` on 5277, `https` on 7214) serve the Angular client from `Overseer/wwwroot`
as last built by `npm run build`; starting the host never rebuilds it. The SPA proxy package is
referenced in `Overseer.csproj` but not activated by any profile. If the browser does not trust
the development certificate, run `dotnet dev-certs https --trust` once.

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
