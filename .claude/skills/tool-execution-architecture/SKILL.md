---
name: tool-execution-architecture
description: Architectural guide for Overseer's tool batching pipeline, concurrency throttles (ToolBatchRunner and ToolExecutor), batch output budgets (ToolBatchResultBudget), real-time Channel streaming, and testing patterns.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/tool_execution_architecture/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.