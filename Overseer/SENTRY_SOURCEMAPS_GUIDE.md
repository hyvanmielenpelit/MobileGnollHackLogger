# Sentry Source Maps Upload Guide for Angular

This guide explains how to properly generate, upload, and exclude Angular source maps for Sentry, ensuring that your production stack traces are readable without exposing your source code to the public.

## AI Skill Automation

You can ask the AI assistant to automate the production build, Debug ID injection, and source map upload process using the `overseer_sentry_sourcemaps_upload` skill.

To invoke the skill, use any of the following prompts:
- *"Upload Overseer's debug files to Sentry. Use the overseer_sentry_sourcemaps_upload skill."*
- *"Upload Overseer source maps to Sentry"*
- *"Upload sourcemaps for Overseer to Sentry"*
- *"Build and upload Sentry sourcemaps for Overseer"*

The skill verifies prerequisites (including `.sentryclirc` and `angular.json`), determines the release version from `Overseer.csproj` or `package.json`, runs the production build, injects Debug IDs via `sentry-cli`, and uploads the sourcemaps to Sentry.

## 1. The Problem

By default, the Overseer Angular application is built using production settings (`ng build --configuration production`). Standard production settings strip out all source maps to optimize file size and hide the original source code. 

Without source maps, Sentry will report errors using minified, obfuscated filenames and line numbers (e.g., `main.a1b2c3d4.js:1:250`), making debugging nearly impossible.

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

## 3. Sentry CLI Configuration (`.sentryclirc`)

To allow `sentry-cli` to authenticate and upload files to the correct project, you need to create a configuration file named `.sentryclirc` in the **root of the `Overseer/` directory** (the same folder as `Overseer.csproj` and `wwwroot/`). `sentry-cli` will automatically traverse up from `ClientApp/` during the build and find this file.

Create the file `Overseer/.sentryclirc` with your full project-specific configuration:

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

## 4. The Upload & Exclusion Process (Foolproof Method)

Relying on manual deletion scripts (like `rm` or PowerShell commands) is brittle. If a script fails or a developer publishes locally, the `.map` files might still end up in the publish payload, bloating your FTP upload size and exposing your source code.

The **best practice** in an ASP.NET Core SPA is to use MSBuild to explicitly exclude `.map` files from the final publish directory. This guarantees they are never packaged for deployment, regardless of how the build is triggered.

### Step-by-Step Execution

#### A. Build, Inject, and Upload

Run the following commands from the `ClientApp` directory. This is typically done in your CI/CD pipeline or a publish script.

```bash
# 1. Build the Application (Generates hidden .map files in ../wwwroot)
npm run build -- --configuration production

# 2. Inject Debug IDs into the .js and .map files
#    This adds a unique identifier that Sentry uses to match errors to source maps.
#    Must run AFTER the build completes.
npx sentry-cli sourcemaps inject ../wwwroot

# 3. Upload source maps to Sentry
#    --release must match the 'release' property set in Sentry.init() in main.ts
npx sentry-cli sourcemaps upload --release <version> ../wwwroot
```

> **Important:** The `--release <version>` value (e.g., `1.0.17`) must be **identical** to the `release` property configured in your Angular app's `Sentry.init()` call in `main.ts` (which dynamically imports the `version` from `package.json` synchronized from `Overseer.csproj`). If they don't match, Sentry will not be able to link errors to their source maps.

#### B. The Foolproof MSBuild Exclusion

To ensure the `.map` files never make it to your FTP server, update the `<PublishAngular>` target in `Overseer.csproj`.

Change the file inclusion rule from:
```xml
<DistFiles Include="wwwroot\**" />
```
To:
```xml
<DistFiles Include="wwwroot\**" Exclude="wwwroot\**\*.map" />
```

### Why this is foolproof:
When you run `dotnet publish`, MSBuild will execute the Angular build (creating `.map` files in the local `wwwroot` folder). However, when MSBuild gathers files to copy into the final `bin/Release/net10.0/publish/` folder (which is what you upload via FTP), it will explicitly ignore every `.map` file. 

You get the best of both worlds:
1. Sentry gets the source maps (uploaded from the local intermediate folder).
2. Your FTP upload size remains tiny.
3. It is impossible to accidentally deploy the source maps, even if the upload scripts fail.

## 5. Why `sentry-cli` Instead of the esbuild Plugin

Angular 19+ uses the esbuild-based `@angular/build:application` builder. The `@sentry/esbuild-plugin` has known integration issues with Angular's application builder — it often runs before the build finishes or fails to locate output files correctly. Sentry's maintainers recommend using `sentry-cli` as the most reliable method for Angular applications using the esbuild-based builder.
