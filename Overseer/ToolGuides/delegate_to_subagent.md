# delegate_to_subagent

Delegates a specialized multi-step inquiry to an autonomous subagent.

## When to Use Subagents
- Use `delegate_to_subagent` when a complex task requires extensive iterative investigation that benefits from dedicated specialization:
  - `wiki_researcher`: Thorough multi-query wiki lookups across GnollHack and NetHack wikis.
  - `source_investigator`: Multi-file C codebase searches, function tracing, and formula verification.
  - `game_data_analyst`: Multi-attribute monster/item analysis, server dumplog patterns, and dataset extraction.
- Subagents run their own tool iterations and return a synthesized response.
- Do NOT invoke subagents for simple, single-turn lookups where direct tool calls (`get_monster_stats`, `get_knowledge_article`, `wiki_view`) suffice.
