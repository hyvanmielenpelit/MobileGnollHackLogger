# Sentry Source Maps Upload Guide for Angular

This guide explains how to properly generate, upload, and exclude Angular source maps for Sentry, ensuring that your production stack traces are readable without exposing your source code to the public.

## AI Skill Automation

You can ask the AI assistant to automate the source map upload process using the `overseer_sentry_sourcemaps_upload` skill.

To invoke the skill, use any of the following prompts:
- *"Upload Overseer's debug files to Sentry. Use the overseer_sentry_sourcemaps_upload skill."*
- *"Upload Overseer source maps to Sentry"*
- *"Upload sourcemaps for Overseer to Sentry"*
- *"Upload Sentry sourcemaps for Overseer"*

The skill verifies prerequisites (including `.sentryclirc` and `angular.json`), determines the release version from `Overseer.csproj` or `package.json`, ensures Debug IDs are injected via `sentry-cli`, and uploads the sourcemaps to Sentry.

---

## 1. The Problem

By default, the Overseer Angular application is built using production settings (`ng build --configuration production`). Standard production settings strip out all source maps to optimize file size and hide the original source code. 

Without source maps, Sentry will report errors using minified, obfuscated filenames and line numbers (e.g., `main.a1b2c3d4.js:1:250`), making debugging nearly impossible.

---

## 2. The Fix: `angular.json` Configuration

We need to instruct Angular to generate source maps during a production build, but keep them **hidden** from the browser.

Open `Overseer/ClientApp/angular.json` and locate the `architect.build.configurations.production` section. Update the `sourceMap` property:

```json
"configurations": {
  "production": {
    "sourceMap": {
      "scripts": true,
      "styles": false,
      "hidden": true
    },
    // ... other production settings
  }
}
```

### Why `hidden: true`?
This setting creates the `.map` files on disk but **omits** the `//# sourceMappingURL=` comment at the bottom of the generated `.js` files. Consequently, when a regular user visits the site and opens browser DevTools, the browser will not attempt to download the source maps.

---

## 3. Sentry CLI Configuration (`.sentryclirc`)

To allow `sentry-cli` to authenticate and upload files to the correct project, you need to create a configuration file named `.sentryclirc` in the **root of the `Overseer/` directory** (the same folder as `Overseer.csproj` and `wwwroot/`). `sentry-cli` will automatically traverse up from `ClientApp/` during the build and find this file.

Create the file `Overseer/.sentryclirc` with your project-specific configuration:

```ini
[defaults]
org = your-sentry-org-slug
project = overseer

[auth]
token = sntrys_eyJ...
```

### Auth Token Permissions
When generating your Auth Token in the Sentry dashboard (Settings → Auth Tokens or Developer Settings → Internal Integrations), ensure the token has the following scope:
- **`project:releases`** (Admin or Write access to manage releases and upload source maps)

> [!CAUTION]
> **CRITICAL SECURITY STEP:** Because this file contains your secret Auth Token, you **MUST** add `.sentryclirc` to your repository's `.gitignore` file immediately. Never commit this file to version control.

---

## 4. The Upload & Exclusion Process (Foolproof Method)

Relying on manual deletion scripts (like `rm` or PowerShell commands) is brittle. If a script fails or a developer publishes locally, the `.map` files might still end up in the publish payload, bloating your FTP upload size and exposing your source code.

The **best practice** in an ASP.NET Core SPA is to use MSBuild to inject Debug IDs during the build and explicitly exclude `.map` files from the final publish directory.

### MSBuild Target Integration in `Overseer.csproj`

In `Overseer/Overseer.csproj`, the `<PublishAngular>` target is configured as follows:

```xml
<Target Name="PublishAngular" BeforeTargets="ComputeFilesToPublish">
  <Exec WorkingDirectory="ClientApp" Command="npm ci" />
  <Exec WorkingDirectory="ClientApp" Command="npx ng build --configuration production" />
  <Exec WorkingDirectory="ClientApp" Command="npx sentry-cli sourcemaps inject ../wwwroot" IgnoreExitCode="true" />
  <ItemGroup>
    <DistFiles Include="wwwroot\**" Exclude="wwwroot\**\*.map" />
    <ResolvedFileToPublish Include="@(DistFiles->'%(FullPath)')" Exclude="@(ResolvedFileToPublish)">
      <RelativePath>wwwroot\%(RecursiveDir)%(FileName)%(Extension)</RelativePath>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
      <ExcludeFromSingleFile>true</ExcludeFromSingleFile>
    </ResolvedFileToPublish>
  </ItemGroup>
</Target>
```

### Why this is foolproof:
When you run `dotnet publish Overseer -c Release`:
1. MSBuild runs `ng build --configuration production` (generating minified `.js` and hidden `.map` files in `Overseer/wwwroot/`).
2. MSBuild runs `sentry-cli sourcemaps inject ../wwwroot`, injecting unique Debug IDs into both the `.js` bundles and `.map` files.
3. MSBuild copies all files to `bin/Release/net10.0/publish/` **except** `.map` files (`Exclude="wwwroot\**\*.map"`).
4. The deployed `.js` files contain the Debug IDs needed by Sentry, while the `.map` files are never exposed publicly.
5. You upload sourcemaps from the local `Overseer/wwwroot/` directory to Sentry using `overseer_sentry_sourcemaps_upload` or `sentry-cli`.

---

## 5. Manual CLI Upload

To upload sourcemaps manually without using the AI skill:

```bash
cd Overseer/ClientApp
npx sentry-cli sourcemaps upload --release <version> ../wwwroot
cd ../..
```

> **Important:** The `--release <version>` value (e.g., `1.0.23`) must be **identical** to the `release` property configured in `Overseer/ClientApp/src/main.ts` (`Sentry.init({ release: packageJson.version })`).

---

## 6. Why `sentry-cli` Instead of the esbuild Plugin

Angular 19+ uses the esbuild-based `@angular/build:application` builder. The `@sentry/esbuild-plugin` has known integration issues with Angular's application builder — it often runs before the build finishes or fails to locate output files correctly. Sentry's maintainers recommend using `sentry-cli` as the most reliable method for Angular applications using the esbuild-based builder.

---

## Related Guides

- [release-checklist.md](release-checklist.md) — Step-by-step Overseer release checklist.
- [commands.md](commands.md) — Comprehensive reference of all CLI commands.
