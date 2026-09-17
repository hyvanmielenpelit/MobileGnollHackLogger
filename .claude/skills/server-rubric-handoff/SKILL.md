---
name: server-rubric-handoff
description: >-
  Mandatory method for turning a Suite Defect finding from an Overseer AI benchmark analysis
  into a YAML repair file a human imports in one action with Import Questions from YAML. Covers
  why the agent never edits a rubric itself, how to obtain the current text (Download All as
  YAML from the suite's Manage Questions toolbar, with a database query kept as a fallback for
  the revision and assessed-difficulty fields alone), the pre-flight checks before a replacement
  is written (verify every fact on disk with a citation, the units rule, scope, no rubric point
  copied from a candidate answer), the deliverable itself (one file carrying only the changed
  questions, each keyed by id, each rubric whole and verbatim, self-checked with a real YAML
  parser before handoff), what the harness does on import (item revision bump, assessed
  difficulty cleared, review mark invalidated, launcher refusal until re-assessed), the human
  steps in the Admin UI, and what to record in the registry afterwards. Read whenever an
  analysis files a Suite Defect that needs a rubric change, or a user asks for a rubric edit
  they can hand to the importer.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_rubric_handoff/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.