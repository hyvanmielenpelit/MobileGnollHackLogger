### Parallel Execution Policy: On Request Only
This policy **overrides** the "Tool Batching and Parallel Execution" section above.

This API key is configured to execute tool calls sequentially by default to conserve API quota. Issue tool calls one at a time unless the player has explicitly asked for parallel, concurrent, or batched lookups in their message (for example "run these in parallel" or "look these up at the same time"). Do not infer such a request from urgency or tone alone.
