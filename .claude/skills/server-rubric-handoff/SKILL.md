---
name: server-rubric-handoff
description: >-
  Mandatory method for turning a Suite Defect finding from an Overseer AI benchmark analysis
  into a rubric edit a human can paste into the suite editor. Covers why the agent never edits a
  rubric itself, how to obtain the current rubric text (it lives only in the database, so the
  user runs a query), the pre-flight checks before a replacement is written (verify every fact
  on disk with a citation, the units rule, scope, no rubric point copied from a candidate
  answer), the copy-paste deliverable (the whole rubric verbatim, never a diff or a paraphrase),
  what the harness does on save (item revision bump, assessed difficulty cleared, review mark
  invalidated, launcher refusal until re-assessed), the human steps in the Admin UI, and what to
  record in the registry afterwards. Read whenever an analysis files a Suite Defect that needs a
  rubric change, or a user asks for a rubric edit they can copy-paste.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_rubric_handoff/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.