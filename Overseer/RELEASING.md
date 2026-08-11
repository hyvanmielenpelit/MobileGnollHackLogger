# Release Process

This repository contains two independently versioned projects (`Overseer` and `MobileGnollHackLogger`) that share the `GnollHackServer.Data` library. 

There are two supported ways to automatically generate release notes for the Overseer project based on path-filtered Git commits:
1. **Local AI Agent (Recommended)**: Uses a local AI agent (like Antigravity) with the `overseer_changelog` skill.
2. **GitHub Actions**: Uses an external AI model via a cloud-based workflow.

## 1. Cutting a Release via Local AI Agent (Recommended)

Using a local AI agent is the fastest and recommended approach. The agent utilizes the dedicated `overseer_changelog` AI skill to analyze local commits, classify changes, and format the JSON.

1. **Push Changes**: Ensure all code changes for the new version are pushed to the `main` branch.
2. **Prompt the AI**: In your local AI chat (e.g., Antigravity), use a prompt like:
   `Generate a new changelog entry for Overseer version 1.0.3. Use the overseer_changelog skill.`
3. **Review the JSON**: The AI will update `Overseer/Data/release-notes.json`. Verify the changes look correct.
4. **Commit the Changes**: Commit the updated `release-notes.json` file.
   `git commit -am "Add changelog entry for v1.0.3"`
5. **Create Tag**: You MUST create a prefixed Git tag so the AI knows where to start for the *next* release.
   `git tag overseer/v1.0.3`
6. **Push Everything**: 
   `git push`
   `git push origin overseer/v1.0.3`

*For detailed instructions on the JSON schema and rules, refer to [CHANGELOG_GUIDE.md](CHANGELOG_GUIDE.md).*

## 2. Cutting a Release via GitHub Actions (Alternative)

If you prefer to use the cloud pipeline, follow these steps:

1. **Push Changes**: Ensure all code changes for the new version are pushed to the `main` branch on GitHub.
2. **Trigger AI Notes**: 
   - Go to the **Actions** tab on GitHub.
   - Select the **Draft Release Notes** workflow.
   - Click **Run workflow**.
   - Select the project `overseer` and enter the **new version number** (e.g., `1.0.3`).
3. **Review PR**: The AI will analyze the commits and open a Pull Request with the updated release notes (e.g., `Overseer/Data/release-notes.json`). Review the notes, edit if necessary, and merge the PR into `main`.
4. **Pull to Local**: Open your local terminal and run `git pull` to fetch the merge commit.
5. **Create Tag**: You MUST create a prefixed Git tag so the system knows where to start for the *next* release.
   `git tag overseer/v1.0.3`
6. **Push Tag**: `git push origin overseer/v1.0.3`
