---
name: overseer_changelog
description: Instructions and guidelines for autonomously generating and appending a changelog entry to the Overseer/Data/release-notes.json file. Triggered when requested to "update the changelog", "add release notes", "generate changelog entry", or similar for the Overseer project.
---

# Overseer Changelog Generation Guide

When instructed to add a new release notes entry to `Overseer/Data/release-notes.json`, follow this step-by-step procedure strictly to generate and insert the entry autonomously. This process bypasses the GitHub Actions script by utilizing your contextual reasoning to classify commits.

## Step 1: Determine the Version Number

You must know the new semantic version number (e.g., `1.1.0` or `1.0.4`) to generate the release notes. 
If the user did not specify the version in their prompt, **STOP** and ask the user for the version number before proceeding.

## Step 2: Find the Latest Anchor Tag

To determine what changed since the last release, you need the most recent release tag.
Run the following Git command **from the root of the `MobileGnollHackLogger` repository** (ensure your working directory is the repository root):

```bash
git describe --tags --match "overseer/v*" --abbrev=0
```

This returns the most recent `overseer/v*` tag (e.g., `overseer/v1.0.2`). 
If no tag is found or an error occurs, **STOP** and ask the user to ensure an initial anchor tag is created.

## Step 3: Fetch the Commit Log

Retrieve the commits made since that latest tag. You must filter the commits to only include paths relevant to the Overseer project.
Run the following Git command **from the root of the `MobileGnollHackLogger` repository** (this is critical so that the path filters match correctly):

```bash
git log <latest_tag>..HEAD --oneline -- Overseer/ Overseer.Tests/ GnollHackServer.Data/
```

*(Replace `<latest_tag>` with the tag you found in Step 2.)*

## Step 4: Validate Existing Entries

Open `Overseer/Data/release-notes.json` and check the `version` field of the first object in the array (index 0).
- If the new version you are about to add **already exists** at index 0, **ABORT** the operation and inform the user that this version is already present in the changelog.

## Step 5: Classify the Commits

Review the commit log you fetched. Based on the commit messages and your knowledge of the repository, classify the user-facing changes. 

**Classification Rules:**
- `feature` — Entirely new functionality or capability that did not exist before.
- `improvement` — Enhancement to an existing feature (e.g., performance gains, UX polish, visual refinements, or user-visible refactoring).
- `fix` — A bug correction or defect resolution.
- `security` — Security-related fix or hardening.

**Exclusion Rules (What NOT to include):**
Do **not** create changelog items for:
- Version number bumps (e.g., "bump version to 1.0.3")
- Dependency or NuGet/npm package updates
- CI/CD pipeline, GitHub Actions, or build system changes
- Merge commits or branch housekeeping
- Code formatting, linting, whitespace-only changes, or minor typos
- Internal refactoring with no user-visible effect
- Changes to AI skills, agent rules, or documentation files (e.g., files in `.agents/`, `SKILL.md`, `AGENTS.md`)

*Note: A single release can have multiple items of mixed types. Ensure each item is independently classified.*

## Step 6: Generate the JSON Entry

Format your compiled changes into a JSON object strictly following this schema:

```json
{
  "version": "<version>",
  "date": "<YYYY-MM-DD>",
  "summary": "A brief 1-3 sentence summary of the overall release.",
  "changes": [
    {
      "type": "<type>",
      "text": "User-friendly description of the change."
    }
  ]
}
```

- **Date:** Use today's date in `YYYY-MM-DD` format.
- **Summary:** Must be a brief, high-level overview of the release, not a repetition of the individual change items.
- **Text:** Must be written for end users, not developers. Make it clear and easy to understand.
- **Type:** Must be exactly one of: `"feature"`, `"improvement"`, `"fix"`, or `"security"`.

## Step 7: Prepend to the File

Modify `Overseer/Data/release-notes.json` to insert your newly generated JSON object at the **beginning** of the JSON array (index 0).

**Formatting Rules for Writing:**
- Maintain 2-space indentation to match the rest of the file.
- Ensure the resulting file is valid JSON (don't forget the comma after your new object).
- **CRITICAL:** Do NOT wrap the JSON content in Markdown code blocks (e.g., do not output ` ```json ` tags into the actual file).

## Step 8: Validate the File

After modifying the file, always re-read or parse it to ensure it is valid, well-formed JSON. If you introduced a syntax error, fix it immediately before notifying the user of success.
