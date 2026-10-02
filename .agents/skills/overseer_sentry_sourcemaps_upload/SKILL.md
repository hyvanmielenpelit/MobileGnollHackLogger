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
Verify that `Overseer/Overseer.csproj` excludes `.map` files in **both** places: the project-level item group, and the `PublishAngular` target:
```xml
<Content Remove="wwwroot\**\*.map" />
```
```xml
<DistFiles Include="wwwroot\**" Exclude="wwwroot\**\*.map" />
```
Both are needed: the Web SDK publishes every `wwwroot` file it discovered as a static web asset when the project was evaluated, so the `DistFiles` exclusion alone does not stop maps that already existed then; the `DistFiles` exclusion covers maps that `ng build` creates during the publish.

---

## Step 2: Determine Release Version

Read the target version number:
1. If the user specified a version in their prompt (e.g., `1.0.17`), use that version.
2. Otherwise, read `<Version>` from `Overseer/Overseer.csproj` (or `"version"` from `Overseer/ClientApp/package.json`).
3. Ensure the version string is non-empty and formatted as semantic versioning (e.g., `1.0.17`).

> [!IMPORTANT]
> The `--release <version>` value passed to `sentry-cli` must match the `release` property configured in `Overseer/ClientApp/src/main.ts` (`Sentry.init({ release: packageJson.version })`). This `packageJson.version` is in fact read directly from `Overseer/ClientApp/package.json`, so you can use the `"version"` found there (which is synchronized from `Overseer.csproj` by MSBuild).

---

## Step 3: Verify That `wwwroot` Is the Published Build

The maps are uploaded from `Overseer/wwwroot`, and they are only correct if `wwwroot` still holds exactly the build in the publish output that was, or will be, deployed. The publish output contains no maps of its own.

- The publish output is `Overseer/bin/Release/net10.0/publish/` (the `PublishUrl` of `Overseer/Properties/PublishProfiles/FolderProfile.pubxml`, and also the output of `dotnet publish Overseer -c Release`). If the user published elsewhere, use their path.
- **Never run `ng build`, `npm run build` or `sentry-cli sourcemaps inject` in this skill.** A rebuild makes `wwwroot` describe a build that was not published. If the publish output is missing, **STOP** and tell the user to publish first (`docs/overseer/release-checklist.md` § 4).
- Run this check from the repository root (PowerShell 5.1):

  ```powershell
  $repo = (git rev-parse --show-toplevel) -replace '/', '\'
  $src  = Join-Path $repo 'Overseer\wwwroot'
  $pub  = Join-Path $repo 'Overseer\bin\Release\net10.0\publish\wwwroot'
  if (-not (Test-Path -LiteralPath $pub)) { throw "publish output not found: $pub" }
  $stop = @(); $warn = @()
  $maps = @(Get-ChildItem $pub -Recurse -File | Where-Object { $_.Name -match '\.map(\.br|\.gz)?$' })
  if ($maps.Count -gt 0) { $warn += "publish output contains $($maps.Count) source map file(s)" }
  foreach ($f in Get-ChildItem $pub -Recurse -File | Where-Object { $_.Extension -notin '.br', '.gz', '.map' }) {
    $rel = $f.FullName.Substring($pub.Length); $s = $src + $rel
    if (-not (Test-Path -LiteralPath $s)) { $stop += "missing from wwwroot: $rel"; continue }
    if ((Get-FileHash -LiteralPath $f.FullName).Hash -ne (Get-FileHash -LiteralPath $s).Hash) { $stop += "differs: $rel" }
    elseif ($f.Extension -eq '.js' -and (Test-Path -LiteralPath "$s.map") -and
            -not (Select-String -LiteralPath $f.FullName -Pattern '//# debugId=' -Quiet)) { $stop += "no Debug ID: $rel" }
  }
  foreach ($e in Get-ChildItem $src -Recurse -File -Include *.js) {
    if (-not (Test-Path -LiteralPath ($pub + $e.FullName.Substring($src.Length)))) { $stop += "not published: $($e.FullName.Substring($src.Length))" }
  }
  'STOP: ' + $stop.Count; $stop; 'WARN: ' + $warn.Count; $warn
  ```

  If the user published elsewhere, set `$pub` to that folder's `wwwroot` as a full long path (not an 8.3 short path such as `TOMMIG~1`, which breaks the relative-path arithmetic).
- **Any `STOP` line:** do not upload. `differs`, `missing` or `not published` mean `wwwroot` was rebuilt after the publish. Tell the user to republish and deploy that publish, then run the skill again. `no Debug ID` means the publish's `sentry-cli sourcemaps inject` step failed (it runs with `IgnoreExitCode="true"`), so the deployed bundles cannot be matched by Sentry. Tell the user to fix `sentry-cli` (usually `Overseer/.sentryclirc`) and republish.
- **A `WARN` line** (maps in the publish output) does not block the upload, but tell the user that this publish exposes the client source and must not be deployed. Point them at Step 1.3.

---

## Step 4: Upload Source Maps to Sentry

Upload the source maps and bundle assets from `wwwroot` to Sentry under the specified release version. Run it only after Step 3 reported zero `STOP` lines.

- **Working Directory:** `Overseer/ClientApp`
- **Command:**
  ```bash
  npx sentry-cli sourcemaps upload --release <version> ../wwwroot
  ```
  *(Replace `<version>` with the version determined in Step 2, e.g. `1.0.17`)*

---

## Step 5: Verify & Report Results

1. Check the output of `sentry-cli` for upload confirmation, including:
   - Organization and project name
   - Release version name
   - Number of source maps / bundle files uploaded
2. Confirm to the user that:
   - Source maps have been successfully uploaded to Sentry for release `<version>`.
   - What Step 3 actually checked: the main bundle's Debug ID (from its trailing `//# debugId=` comment), that `wwwroot` matched the publish output file for file, and the number of map files found in the publish output (expected 0).
