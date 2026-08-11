# Changelog Guide

This document explains how to manually add or edit entries in `Data/release-notes.json`, the data file that powers the Overseer's in-app changelog.

## File Location

```
Overseer/Data/release-notes.json
```

## JSON Schema

The file is a JSON array of release objects. **Entries must be in descending version order** (newest first), because the UI reads `[0]` to determine the latest version.

```json
[
  {
    "version": "1.1.0",
    "date": "2026-08-11",
    "summary": "A brief 1–3 sentence overview of the release.",
    "changes": [
      {
        "type": "feature",
        "text": "User-friendly description of the change."
      }
    ]
  }
]
```

## Field Reference

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `version` | string | Yes | Semantic version number (e.g., `"1.2.0"`). |
| `date` | string | Yes | Release date in `YYYY-MM-DD` format. |
| `summary` | string | Yes | A brief 1–3 sentence summary of the overall release. |
| `changes` | array | Yes | One or more change items (see below). |

### Change Items

Each object in the `changes` array describes a single user-facing change.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | string | Yes | One of `"feature"`, `"improvement"`, `"fix"`, or `"security"`. |
| `text` | string | Yes | A clear, user-friendly description of the change. |

## Change Types

| Type | Icon | When to Use |
|------|------|-------------|
| `feature` | ⭐ Star | **Entirely new functionality** that did not exist before. |
| `improvement` | 📄 Document | **Enhancement to an existing feature** — performance gains, UX polish, visual refinements. |
| `fix` | 🔧 Wrench | **Bug correction** — something was broken and is now fixed. |
| `security` | 🛡️ Shield | **Security-related fix or hardening** — patching a vulnerability, adding input validation, etc. |

> **Tip:** When in doubt between `feature` and `improvement`, ask: "Could a user have done this before?" If yes → `improvement`. If no → `feature`.

## What NOT to Include

Do **not** create changelog items for:

- Version number bumps (e.g., "bumped version to 1.0.3")
- Dependency or NuGet/npm package updates
- CI/CD pipeline or build system changes
- Merge commits or branch housekeeping
- Code formatting, linting, or whitespace-only changes
- Internal refactoring that has no user-visible effect

These are implementation details that don't affect the end user.

## Sidebar Star Badge

The changelog link in the chat sidebar displays a yellow star animation when a new **major or minor** version is detected (e.g., 1.0.x → 1.1.0 or 1.x → 2.0.0). **Patch-only releases** (e.g., 1.0.2 → 1.0.3) do **not** trigger the star. The star disappears once the user opens the changelog page.

## Adding a New Release

1. Open `Overseer/Data/release-notes.json`.
2. Add a new object at the **beginning** of the array (index 0).
3. Fill in `version`, `date`, `summary`, and `changes`.
4. Each item in `changes` must have a `type` and `text`.
5. A single release can mix different types (e.g., one feature and two fixes).
6. Validate that the file is valid JSON before committing.
7. After committing, create the release tag: `git tag overseer/vX.Y.Z` and push both the commit and tag: `git push` followed by `git push origin overseer/vX.Y.Z`.

### Example

```json
[
  {
    "version": "1.1.0",
    "date": "2026-08-15",
    "summary": "Improved changelog display and fixed authentication issues.",
    "changes": [
      {
        "type": "improvement",
        "text": "Changelog items are now grouped by category (Features, Improvements, Bug Fixes, Security) for easier reading."
      },
      {
        "type": "fix",
        "text": "Fixed an issue where users were occasionally logged out when switching between pages."
      },
      {
        "type": "fix",
        "text": "Corrected a layout glitch in the settings panel on narrow screens."
      }
    ]
  },
  {
    "version": "1.0.2",
    "date": "2026-08-10",
    "summary": "Added Changelog Feature",
    "changes": [
      {
        "type": "feature",
        "text": "Gnoll Overseer's changelog is now available through a dedicated Changelog link in the main chat window's sidebar."
      }
    ]
  }
]
```

## AI-Generated Changelogs

For the full release process, including instructions on how to use AI (both Local AI Agents and GitHub Actions) to generate changelog entries automatically, please see the [Release Process Guide](RELEASING.md).

## Configuration

The number of release entries shown per page in the changelog UI is configured in `appsettings.json` via the `ChangelogPageSize` key. If the key is not present, it defaults to `10`.
