# Release Process

This repository contains two independently versioned projects (`Overseer` and `MobileGnollHackLogger`) that share the `GnollHackServer.Data` library. 

We use an AI-powered GitHub Action to automatically generate release notes based on path-filtered Git commits.

## Cutting a New Release

To release a new version of either project, follow these exact steps:

1. **Push Changes**: Ensure all code changes for the new version are pushed to the `main` branch on GitHub.
2. **Trigger AI Notes**: 
   - Go to the **Actions** tab on GitHub.
   - Select the **Draft Release Notes** workflow.
   - Click **Run workflow**.
   - Select the **project** (`overseer` or `account`) and enter the **new version number** (e.g., `1.0.3`).
3. **Review PR**: The AI will analyze the commits and open a Pull Request with the updated release notes (e.g. `Overseer/Data/release-notes.json`). Review the notes, edit if necessary, and merge the PR into `main`.
4. **Pull to Local**: Open your local terminal and run `git pull` to fetch the merge commit.
5. **Create Tag**: You MUST create a prefixed Git tag so the AI knows where to start for the *next* release.
   - For Overseer: `git tag overseer/v1.0.3`
   - For Account: `git tag account/v1.0.3`
6. **Push Tag**: `git push origin <tag-name>`
