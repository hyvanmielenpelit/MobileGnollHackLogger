---
name: server-wiki-handoff
description: >-
  Mandatory method for turning a rung-2 wiki finding from an Overseer AI benchmark analysis into
  a handoff prompt for a separate session in the GnollHackWiki clone at WikiPath. Covers the
  read-only pre-flight checks against WikiPath the analyst must do before writing the prompt
  (target page exists, which of two similarly named pages carries the fact, exact match count,
  existing links, clean tree, line endings, no presumed generator), what the prompt must not
  restate or prescribe, the prompt template, and what to record after the wiki change lands.
  Read with the other four benchmark skills before the first finding is written; act on it
  whenever a finding lands on ladder rung 2.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_wiki_handoff/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.