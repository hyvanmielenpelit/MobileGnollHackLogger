---
name: server-wiki-handoff
description: >-
  Mandatory method for turning a rung-2 wiki finding from an Overseer AI benchmark analysis into
  a three-step handoff document: proposed changes in plain text and as diffs, an AI validation prompt
  for a read-only session on the GnollHack source clone at C:\hmp\GnollHack, and an execution prompt
  for a session on the GnollHackWiki clone at C:\hmp\GnollHackWiki run only after validation passes.
  Covers pre-flight checks against the wiki clone (target page exists, which page carries the fact,
  match count, existing links, clean tree, line endings, no presumed generator), what prompts must
  not prescribe, templates, the confirmation gate pausing a round until validation passes and the
  wiki session finishes, and post-session records. Read with the other four benchmark skills before
  the first finding is written; act on it whenever a finding lands on ladder rung 2.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_wiki_handoff/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.