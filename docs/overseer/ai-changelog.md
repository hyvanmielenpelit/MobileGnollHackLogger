# AI Changelog Generation

This repository contains two independently versioned projects (`Overseer` and `MobileGnollHackLogger`) that share the `GnollHackServer.Data` library. 

This guide explains how to automatically generate release notes for the Overseer project based on path-filtered Git commits using a Local AI Agent.

## Cutting a Release via Local AI Agent

The agent utilizes the dedicated `overseer_changelog` AI skill to analyze local commits, classify changes, and format the JSON.

1. **Prompt the AI**: In your local AI chat (e.g., Antigravity), use a prompt like:
   `Generate a new changelog entry for Overseer version 1.0.3. Use the overseer_changelog skill.`
2. **Review the JSON**: The AI will update `Overseer/Data/release-notes.json`. Verify the changes look correct.
3. **Commit the Changes**: Commit the updated `release-notes.json` file.
   `git commit -am "Add changelog entry for v1.0.3"`
4. **Create Tag**: You MUST create a prefixed Git tag so the AI knows where to start for the *next* release.
   `git tag overseer/v1.0.3`
5. **Push Everything**: 
   - `git push`
   - `git push origin overseer/v1.0.3`

## Related Guides

- [changelog-guide.md](changelog-guide.md) — Detailed instructions on the JSON schema, change types, and manual editing rules.
- [release-checklist.md](release-checklist.md) — Complete release workflow checklist.
