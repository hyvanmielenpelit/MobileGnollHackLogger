# Overseer Release Checklist

This checklist provides a concise, step-by-step procedure for cutting a new release of the **Gnoll Overseer** application. Follow each step in order.

---

## 1. Pre-Release Testing

Run automated tests to ensure everything is working before preparing a release:

```bash
# 1. Run backend unit & integration tests (skipping external AI APIs)
dotnet test --filter "Category!=UsesExternalApi"

# 2. Run frontend unit tests in headless mode (from Overseer/ClientApp/)
cd Overseer/ClientApp
npm run test:headless
cd ../..
```

---

## 2. Version Bump

1. Open `Overseer/Overseer.csproj` and update the version tag to the new semantic version (e.g., `<Version>1.0.23</Version>`).
2. Build the project once to synchronize the version to `Overseer/ClientApp/package.json`:
   ```bash
   dotnet build Overseer/Overseer.csproj
   ```

---

## 3. Generate Changelog Entry via AI Skill

Prompt your local AI Assistant (e.g., Antigravity) to analyze the commit log and append a new entry to `Overseer/Data/release-notes.json`:

> *"Generate a new changelog entry for Overseer version `<version>`. Use the `overseer_changelog` skill."*

Review `Overseer/Data/release-notes.json` to verify the generated summary and categorized change items.

*(For manual editing rules or JSON schema details, see [changelog-guide.md](changelog-guide.md) and [ai-changelog.md](ai-changelog.md)).*

---

## 4. Publish Application (Build & Inject Sentry Debug IDs)

Publish the ASP.NET Core backend and Angular frontend for production:

```bash
dotnet publish Overseer -c Release
```

This command automatically:
- Compiles the ASP.NET Core backend.
- Builds the Angular SPA with production optimization (`ng build --configuration production`).
- Injects Sentry Debug IDs into the generated `.js` and `.map` files in `Overseer/wwwroot/`.
- Copies the deployment payload into `Overseer/bin/Release/net10.0/publish/` (with `.map` files excluded).

---

## 5. Test Server Deployment & Validation

1. **Deploy to Test Server**: Upload the published files from `Overseer/bin/Release/net10.0/publish/` to the test server.
2. **Perform Testing**: Test the deployed version thoroughly on the test server.
3. **Iterate if Changes Needed**:
   - If bugs or adjustments are found, implement the fixes in code.
   - Update `Overseer/Data/release-notes.json` manually if the new changes require additional notes (refer to [changelog-guide.md](changelog-guide.md)).
   - Re-publish the application: `dotnet publish Overseer -c Release`.
   - Re-upload to the test server and verify again.
4. **Completion**: Once testing on the test server is completed successfully and no more changes are required, proceed to the next steps.

---

## 6. Upload Sourcemaps to Sentry

Upload the source maps from `Overseer/wwwroot/` to Sentry under the new release version.

**Option A (AI Skill):**
> *"Upload Overseer source maps to Sentry. Use the `overseer_sentry_sourcemaps_upload` skill."*

**Option B (Manual CLI):**
```bash
cd Overseer/ClientApp
npx sentry-cli sourcemaps upload --release <version> ../wwwroot
cd ../..
```

*(For full details on Sentry configuration, see [sentry-sourcemaps.md](sentry-sourcemaps.md)).*

---

## 7. Deploy to Production Server

Deploy the verified changes directly from the test server to the production server. (No FTP upload from your local machine is required here).

---

## 8. Post-Release Verification

1. **Web Access**: Open the production site in your browser and verify the chat interface loads cleanly.
2. **Version String**: Check the application footer / settings modal to confirm the new version number is displayed.
3. **Sentry Dashboard**: Check the Sentry project dashboard to ensure no startup or runtime errors are reported.

---

## 9. Tag the Release

Tagging should be performed last, once the production server is verified and working.

> [!NOTE]
> All changes (version bump, release-notes.json, code adjustments) should already be committed to Git by this point. If anything remains uncommitted, commit your changes before proceeding.

### 1. Push All Commits First

Before creating and pushing tags, ensure all local commits are pushed to the remote repository:

```bash
git push
```

### 2. Create and Push the Tag

Once your commits are pushed, create the release tag and push it:

```bash
git tag overseer/v<version>
git push origin overseer/v<version>
```

> [!IMPORTANT]
> The `overseer/v<version>` tag serves as the anchor point for the AI changelog generator in subsequent releases.

### Tagging Later / Retroactive Tagging

If you forget to create the tag immediately after deploying, you can easily tag the release commit later:

1. Ensure all commits are pushed (`git push`).
2. In Visual Studio, open the **Git Changes** window and click **View all commits** (or open the Git Repository history view).
3. Right-click or select the row corresponding to the release commit, and press **Ctrl+C** to copy the commit hash.
4. Create and push the tag specifying that commit hash:

```bash
git tag overseer/v<version> <commit-hash>
git push origin overseer/v<version>
```

#### Example:

```bash
git tag overseer/v1.0.23 8a2b3ab50e0344b61c9e98c0784c804b818cfba3
git push origin overseer/v1.0.23
```

---

## Related Guides

- [commands.md](commands.md) — Comprehensive reference of all build, test, and run CLI commands.
- [sentry-sourcemaps.md](sentry-sourcemaps.md) — Sentry source map generation and upload guide.
- [changelog-guide.md](changelog-guide.md) — Manual changelog schema and guidelines.
- [ai-changelog.md](ai-changelog.md) — AI-assisted changelog generation workflow.
