---
name: server-wiki-handoff
description: >-
  Mandatory method for turning a rung-2 wiki finding from an Overseer AI benchmark (GnollBench)
  analysis, a rung-1 knowledge-base finding, or a wiki or knowledge-base fact from a feedback issue,
  into a two-step handoff document: proposed changes, one at a time, as text and diffs, and one
  orchestration prompt for a session on the GnollHackWiki clone at C:\hmp\GnollHackWiki (or the
  knowledge-base clone) that validates each change against the GnollHack source at C:\hmp\GnollHack
  with a fresh read-only validator subagent, applies it on a pass, revises and re-validates it on a
  fail, up to five rounds, and reports what was not applied. Covers pre-flight checks on the wiki
  clone (target page, match count, links, clean tree, line endings, no presumed generator), what
  prompts must not prescribe, the validation loop, the confirmation gate that waits for
  the session's report, and post-session records. Read with the other four benchmark skills before
  the first finding; act on it when a finding lands on rung 1 or 2.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_wiki_handoff/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.
