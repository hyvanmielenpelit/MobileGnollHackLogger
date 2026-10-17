---
name: overseer_changelog
description: Instructions and guidelines for autonomously generating and appending a changelog entry to the Overseer/Data/release-notes.json file. Triggered when requested to "update the changelog", "add release notes", "generate changelog entry", or similar for the Overseer project.
---

# Overseer Changelog Generation Guide

> [!WARNING]
> **CRITICAL PROHIBITION:** You must **never** create Git tags (`git tag`) or push to the repository (`git push`) yourself. Your job is only to generate the `release-notes.json` entry and remind the developer to perform the tagging and pushing (as described in Step 9).

When instructed to add a new release notes entry to `Overseer/Data/release-notes.json`, follow this step-by-step procedure strictly to generate and insert the entry autonomously. This process utilizes your contextual reasoning to classify commits.

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

## Step 5: Select and Classify the Commits

Review the commit log you fetched. Based on the commit messages and your knowledge of the repository, first select the changes a regular user can notice, then classify them.

**Audience: the regular Overseer chat user.** The changelog is read by people who use the
Overseer AI chat, not by administrators. List a change only if a regular user can notice it
while using the chat. In scope:
- The chat itself (`/chat`): answers, tools, models a user can pick, attachments, message
  formatting, sessions, streaming, speed and reliability.
- The pages a regular user reaches from the chat: Settings (including API keys and models),
  Privacy, Login, Debug Log and the Changelog itself.
- **Usage limits a user can run into**: rate limits, request or message size limits, quota or
  usage-guard messages, and changes to how such a limit is enforced or explained. Describe
  what the user experiences (for example, "a clearer message when you reach the hourly
  message limit"), never the internal mechanism or the threshold logic an abuser could use.
- Security and privacy fixes that protect regular users' data or accounts.

Out of scope, even when the change is large:
- **The Admin page and everything under it** (`/admin`, `Overseer/ClientApp/src/app/admin/`,
  the `Admin*Controller` endpoints): user management, default API keys, system
  configuration, admin alerts, and every admin tab and sub-tab.
- **GnollBench** — the AI benchmark and all its parts: runs, suites, batteries, scoring
  profiles, model comparisons, reports and report packs, root cause analyses, Chat
  Consistency, `Overseer/Services/Benchmarking/` and `Overseer/Services/ChatConsistency/`.
  A commit whose title starts with `[GnollBench]` is out of scope.

A commit that mixes both is listed only for its user-visible part. For example, an abuse
hardening commit that adds a usage guard and new admin settings gets one item about the limit
users may meet, and nothing about the admin settings. A GnollBench finding that led to a
change in the production chat (a system prompt, a tool, a knowledge base article) is in scope,
described by its effect on chat answers, without mentioning the benchmark. If no change in
the range is in scope, **STOP** and tell the user, rather than writing an entry with
admin-only or GnollBench-only items.

**Classification Rules:**
- `feature` — Entirely new functionality or capability that did not exist before.
- `improvement` — Enhancement to an existing feature (e.g., performance gains, UX polish, visual refinements, or user-visible refactoring).
- `fix` — A bug correction or defect resolution.
- `security` — Security-related fix or hardening.

**Exclusion Rules (What NOT to include):**
Do **not** create changelog items for:
- Version number bumps (e.g., "bump version to 1.0.3")
- Dependency or NuGet/npm package updates
- CI/CD pipeline or build system changes
- Merge commits or branch housekeeping
- Code formatting, linting, whitespace-only changes, or minor typos
- Internal refactoring with no user-visible effect
- Changes to AI skills, agent rules, or documentation files (e.g., files in `.agents/`, `SKILL.md`, `AGENTS.md`)
- Admin page and GnollBench changes (see **Audience** above)

*Note: A single release can have multiple items of mixed types. Ensure each item is independently classified.*

## Step 6: Generate the JSON Entry

Format your compiled changes into a JSON object strictly following this schema:

```json
{
  "version": "<version>",
  "date": "<YYYY-MM-DDTHH:mm:ssZ>",
  "summary": "A brief 1-3 sentence summary of the overall release.",
  "changes": [
    {
      "type": "<type>",
      "text": "User-friendly description of the change."
    }
  ]
}
```

- **Date:** Use the current date and time in exact UTC ISO-8601 format (e.g., `2026-08-13T15:30:00Z`).
- **Summary:** Must be a brief, high-level overview of the release, not a repetition of the individual change items.
- **Text:** Must be written for end users, not developers. Make it clear and easy to understand. Do not mention the Admin page, administrators' tools or GnollBench.
- **Type:** Must be exactly one of: `"feature"`, `"improvement"`, `"fix"`, or `"security"`.

## Step 7: Prepend to the File

Modify `Overseer/Data/release-notes.json` to insert your newly generated JSON object at the **beginning** of the JSON array (index 0).

**Formatting Rules for Writing:**
- Maintain 2-space indentation to match the rest of the file.
- Ensure the resulting file is valid JSON (don't forget the comma after your new object).
- **CRITICAL:** Do NOT wrap the JSON content in Markdown code blocks (e.g., do not output ` ```json ` tags into the actual file).

## Step 8: Validate the File

After modifying the file, always re-read or parse it to ensure it is valid, well-formed JSON. If you introduced a syntax error, fix it immediately before notifying the user of success.

## Step 9: Remind the Developer to Tag

After successfully generating and validating the entry, output a final message reminding the developer that they must manually commit, tag, and push the release. Include the following explicit commands in your message:

1. Commit the changes: `git commit -am "Add changelog entry for vX.Y.Z"`
2. Create the release tag: `git tag overseer/vX.Y.Z` (Make sure to replace `X.Y.Z` with the actual version).
3. Push both the commit and the tag:
   `git push`
   `git push origin overseer/vX.Y.Z`

Do not execute these commands yourself. Only output them as instructions for the developer.
