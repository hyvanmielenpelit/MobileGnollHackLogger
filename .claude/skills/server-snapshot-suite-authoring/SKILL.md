---
name: server-snapshot-suite-authoring
description: >-
  Creating an Overseer AI benchmark question suite YAML from a GnollHack AI snapshot — a
  `.ai.html` produced by the game's Export AI Snapshot, or a `.snapshot.txt` downloaded from the
  Snapshot Viewer — in an agent session with the repositories on disk and Overseer not running.
  Covers flattening the snapshot exactly as the server would, choosing how many questions the
  board supports and how many per difficulty band, writing questions that are unanswerable
  without the board, writing rubrics whose BOARD FACTS are quotable from it verbatim and whose
  mechanics points are verified against the GnollHack source, and producing the single file that
  Import Suite from YAML turns into a suite with its game snapshot attached. Also covers adding
  questions to an existing snapshot suite from its exported YAML, where the board is taken from the
  file's `suite.snapshot.text` and the output is a questions file the Snapshot Suite Wizard imports
  into that suite. Read it whenever the task is "make a benchmark suite from this snapshot", "turn
  this .ai.html into questions", "add questions to this suite YAML", or an Overseer suite YAML with
  a `suite.snapshot` block has to be written by hand.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_snapshot_suite_authoring/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.