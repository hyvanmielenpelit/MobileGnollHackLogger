---
name: server-benchmark-to-chat-transfer
description: >-
  Mandatory method for turning Overseer AI benchmark (GnollBench) findings into improvements
  to the production chat agent: quality, latency, cost. Covers the priority order (the chat
  first, the benchmark second, model results third); that the benchmark grades the
  production chat system prompt; triage of every finding into harness, suite, chat-transferable
  or corpus / environment defect; the configuration-parity check; what the benchmark does not
  measure; the route into the tool-layer skills; the evidence bar, version boundaries and
  panel reading; the ladder of safe changes from knowledge-base article to prompt edit;
  anti-overfitting; verification and rollback; the required Chat Transfer output and Developer
  Runbook; and the record: the recent entries, the standing cautions, and the ledgers of
  changes that reached the chat and of verifier failures in references/. Read before analyzing any
  benchmark run report, diagnostics or assessment, and before writing any implementation plan
  derived from one.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_benchmark_to_chat_transfer/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.