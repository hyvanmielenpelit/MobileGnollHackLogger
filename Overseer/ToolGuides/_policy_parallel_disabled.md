### Parallel Execution Policy: Sequential Only
This policy **overrides** the "Tool Batching and Parallel Execution" section above.

This API key is configured for sequential tool execution to prevent rate limiting. Issue tool calls one at a time, across successive turns, even when the lookups are independent. The server executes tool calls one at a time on this key, so issuing several in a single turn will not make them finish any sooner and may exhaust the shared batch output budget. Do not delegate to more than one subagent at a time.
