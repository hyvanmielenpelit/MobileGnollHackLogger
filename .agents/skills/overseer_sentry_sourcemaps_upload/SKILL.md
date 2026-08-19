---
name: overseer_sentry_sourcemaps_upload
description: Instructions and guidelines for building, injecting Debug IDs, and uploading Overseer's Angular source maps and debug files to Sentry using sentry-cli. Triggered when requested to "upload source maps to Sentry", "upload Overseer debug files", "upload sourcemaps", or similar for the Overseer project.
---

# Overseer Sentry Source Maps & Debug Files Upload Guide

When instructed to upload Overseer's debug files or source maps to Sentry, follow this step-by-step procedure strictly.

## Trigger Phrases
This skill is triggered by prompts such as:
- *"Upload Overseer's debug files to Sentry. Use the overseer_sentry_sourcemaps_upload skill."*
- *"Upload Overseer's debug files to Sentry"*
- *"Upload Overseer source maps to Sentry"*
- *"Upload sourcemaps for Overseer to Sentry"*
- *"Build and upload Sentry sourcemaps for Overseer"*

---

## Step 1: Verify Prerequisites & Configuration

Before running build or upload commands, verify the required configurations:

### 1. Sentry CLI Configuration (`Overseer/.sentryclirc`)
Check if `Overseer/.sentryclirc` exists. It must contain:
```ini
[defaults]
org = your-sentry-org-slug
project = overseer

[auth]
token = sntrys_eyJ...
```
- **If missing or incomplete:** **STOP** and inform the developer to create `Overseer/.sentryclirc` with a valid Sentry auth token (having `project:releases` scope) and verify it is ignored in `.gitignore`. Do not proceed without authentication.

### 2. Angular Source Map Settings (`Overseer/ClientApp/angular.json`)
Check `Overseer/ClientApp/angular.json` under `projects.ClientApp.architect.build.configurations.production.sourceMap`:
- `"scripts": true`
- `"styles": false`
- `"hidden": true` (ensures `//# sourceMappingURL=` is omitted so DevTools will not publicly fetch source maps)

### 3. MSBuild Exclusion (`Overseer/Overseer.csproj`)
Verify that the `PublishAngular` target in `Overseer/Overseer.csproj` excludes `.map` files:
```xml
<DistFiles Include="wwwroot\**" Exclude="wwwroot\**\*.map" />
```

---

## Step 2: Determine Release Version

Read the target version number:
1. If the user specified a version in their prompt (e.g., `1.0.17`), use that version.
2. Otherwise, read `<Version>` from `Overseer/Overseer.csproj` (or `"version"` from `Overseer/ClientApp/package.json`).
3. Ensure the version string is non-empty and formatted as semantic versioning (e.g., `1.0.17`).

> [!IMPORTANT]
> The `--release <version>` value passed to `sentry-cli` must match the `release` property configured in `Overseer/ClientApp/src/main.ts` (`Sentry.init({ release: packageJson.version })`). This `packageJson.version` is in fact read directly from `Overseer/ClientApp/package.json`, so you can use the `"version"` found there (which is synchronized from `Overseer.csproj` by MSBuild).

---

## Step 3: Build Angular Production Application

Execute the production build to generate minified bundles and hidden `.map` files in `Overseer/wwwroot`:

- **Working Directory:** `Overseer/ClientApp`
- **Command:**
  ```bash
  npx ng build --configuration production
  ```
  *(Alternatively: `npm run build -- --configuration=production`)*

Verify that the build completes successfully (exit code 0) and that `.js` and `.map` files are generated in `Overseer/wwwroot`.

---

## Step 4: Inject Debug IDs

Inject unique Debug IDs into the generated `.js` and `.map` files in `wwwroot`. This ensures Sentry can reliably match stack traces to source maps regardless of file hashing:

- **Working Directory:** `Overseer/ClientApp`
- **Command:**
  ```bash
  npx sentry-cli sourcemaps inject ../wwwroot
  ```

Verify that `sentry-cli` reports debug IDs successfully injected.

---

## Step 5: Upload Source Maps to Sentry

Upload the source maps and bundle assets from `wwwroot` to Sentry under the specified release version:

- **Working Directory:** `Overseer/ClientApp`
- **Command:**
  ```bash
  npx sentry-cli sourcemaps upload --release <version> ../wwwroot
  ```
  *(Replace `<version>` with the version determined in Step 2, e.g. `1.0.17`)*

---

## Step 6: Verify & Report Results

1. Check the output of `sentry-cli` for upload confirmation, including:
   - Organization and project name
   - Release version name
   - Number of source maps / bundle files uploaded
2. Confirm to the user that:
   - Source maps have been successfully uploaded to Sentry for release `<version>`.
   - Production source maps remain hidden (`hidden: true`) and are excluded from the `dotnet publish` deployment payload by MSBuild.
