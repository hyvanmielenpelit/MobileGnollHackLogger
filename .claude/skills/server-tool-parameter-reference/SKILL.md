---
name: server-tool-parameter-reference
description: >-
  Per-tool parameter and result contract for every Overseer AI chat tool -- what to check
  when asking "what parameters does source_code_search take", "why did this tool return
  nothing", "which tools can a benchmark run call", "what does wiki_search's category
  filter actually match against", or "is get_item_stats supposed to have no stats field".
  Covers, tool by tool: required and optional parameters, defaults and clamps and the
  config key each comes from, which corpus or service the tool reads, the shape of a
  successful result, per-tool failure modes, which 16 tools Benchmark:AllowedTools
  permits, and how to tell a correct empty result from a broken one (a guard-message
  failure during indexing vs. an ordinary "not found" once indexed). This is the contract
  layer the diagnostic method in server_benchmark_tool_diagnostics checks a call against;
  read it whenever a tool call's parameters or result need to be judged correct or wrong,
  not just present or absent.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/server_tool_parameter_reference/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.