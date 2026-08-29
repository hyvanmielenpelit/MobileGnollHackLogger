# delegate_to_subagent

Delegates a specialized multi-step inquiry to an autonomous subagent.

## When to Use Subagents
- Use `delegate_to_subagent` when a complex task requires extensive iterative investigation across multiple files or records.
- Subagents run their own tool iterations and return a synthesized response.
- Do NOT invoke subagents for simple, single-turn lookups where direct tool calls (`get_monster_stats`, `get_knowledge_article`, `wiki_view`) suffice.
- **Cost & Latency Warning**: Subagent execution consumes multiple autonomous LLM turns and tool executions. Prefer direct tool calls whenever possible.

## Handling Partial Results
- If a subagent terminates early due to cancellation, iteration limits, or budget exhaustion, its returned output begins with a `[PARTIAL RESULT — subagent '<agent_name>' terminated early: <status>...]` banner.
- When this banner is present, you MUST inform the user that the subagent's investigation was incomplete or interrupted.
- Do NOT hallucinate missing findings or present partial findings as a comprehensive answer. Clearly state what was found versus what was left unverified.
