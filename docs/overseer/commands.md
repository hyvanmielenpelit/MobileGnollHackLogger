# Overseer Build, Test, and Run Commands

This guide provides a comprehensive reference of all command-line operations used to develop, build, test, and run the Overseer application (ASP.NET Core backend + Angular frontend).

---

## 1. Quick Reference

| Action | Working Directory | Command |
| :--- | :--- | :--- |
| **Run Entire App (Backend + SPA Proxy)** | `Overseer/` | `dotnet run` |
| **Run Frontend Unit Tests (Headless)** | `Overseer/ClientApp/` | `npm run test:headless` |
| **Run Backend Unit & Integration Tests** | `Overseer.Tests/` | `dotnet test --filter "Category!=UsesExternalApi"` |
| **Build Frontend (Production)** | `Overseer/ClientApp/` | `npm run build` |
| **Build Backend** | `Overseer/` | `dotnet build` |
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

### Running Backend Tests
```bash
# Run all tests SKIPPING external AI API calls (Recommended - saves AI quota)
dotnet test --filter "Category!=UsesExternalApi"

# Run all tests INCLUDING external AI API calls - consumes API quota and spends real money.
# Ask first; on a machine with the live-test secrets configured this bills three providers.
dotnet test

# Run a specific test class
dotnet test --filter "FullyQualifiedName~ChatServiceTests"
dotnet test --filter "FullyQualifiedName~SourceCodeServiceTests"

# Run a specific test method
dotnet test --filter "FullyQualifiedName~ChatServiceTests.StripThoughts_RemovesAiThoughtTags"
```

> [!WARNING]
> **AI API Quota**: Always use `--filter "Category!=UsesExternalApi"` unless you have explicit permission to consume live AI API tokens.
>
> **The filter fails open, so check it rather than trusting it.** A typo in the trait name or its
> value matches nothing, excludes nothing, and runs the live tests without complaint —
> `Categoy!=UsesExternalApi` and `Category!=UsesExternalApis` both select all 1482 tests. Verify a
> filter with a discovery run, which executes nothing:
>
> ```bash
> dotnet test Overseer.Tests --list-tests --filter "Category=UsesExternalApi"
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

# Generate an idempotent SQL script for server deployment (covers all migrations)
dotnet ef migrations script -i -p GnollHackServer.Data -s MobileGnollHackLogger -o migration.sql

# Generate an idempotent SQL script from a specific migration to latest
dotnet ef migrations script <FromMigration> -i -p GnollHackServer.Data -s MobileGnollHackLogger -o migration.sql

# Generate an idempotent SQL script between two specific migrations
dotnet ef migrations script <FromMigration> <ToMigration> -i -p GnollHackServer.Data -s MobileGnollHackLogger -o migration.sql
```

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
