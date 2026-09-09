---
name: server-tool-data-sources
description: >-
  The corpora behind Overseer's information-retrieval tools, and how to prove on disk what a
  tool could and could not have seen. Answers "did the AI have access to the wiki", "why did
  this tool return nothing", "where does Overseer read the source code from", "is the source
  index stale" and "how do I regenerate the NetHack wiki". Covers resolving every tool corpus
  path from User Secrets and Overseer/appsettings.json — the GnollHack wiki and source, the
  NetHack wiki and source, the knowledge base, the dumplog store and Overseer/ToolGuides —
  what each index includes and what it silently excludes (target directories, file
  extensions, filename exclusions, per-file size limits), the PowerShell that verifies a
  repository was reachable and which files a size limit dropped, refresh modes and what needs
  an Overseer restart, corpus staleness and the five fingerprints a benchmark run records
  plus the two corpora it does not, the secrets-hygiene rule that only keys are ever
  reported, and regenerating the NetHack wiki with its mandatory backup.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_tool_data_sources/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.