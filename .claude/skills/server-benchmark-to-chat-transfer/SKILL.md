---
name: server-benchmark-to-chat-transfer
description: >-
  Mandatory method for turning Overseer AI benchmark findings into improvements to the
  production chat agent — better answer quality, lower latency, lower cost. Covers the fact
  that the benchmark grades the production chat system prompt, the required triage of every
  finding into harness defect / suite defect / chat-transferable, the evidence bar a finding
  must clear before any chat prompt is changed, the configuration-parity check, the ordered
  ladder of safe changes from knowledge-base article up to prompt edit, the anti-overfitting
  rules, and the per-run model behaviour notes this skill accumulates. Read before analysing
  any benchmark run report, diagnostics or assessment, and before writing any implementation
  plan derived from one.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_benchmark_to_chat_transfer/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.