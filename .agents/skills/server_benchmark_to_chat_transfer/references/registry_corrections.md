# Registry Corrections

Corrections to figures recorded in the registry of `../SKILL.md` § 11 (its earlier registry rows,
retired entries and the task directories they point to). Linked from `../SKILL.md` § 11 *Standing
Cautions*. A correction stays here for as long as any recorded figure it qualifies can be read.

**Correction, 2026-09-10 (run-33 H1): every Gemini-role token and cost figure recorded before the
fix is inflated**, ≈ 11× on run 33: `GoogleProvider` emitted one usage report per streamed chunk and
`AgentLoopRunner` summed them. That covers the primary assessor and the synthesis on every run, and
the candidate on run 22 (its tokens, its cost and possibly its 21.1 % cache-read share); Anthropic and
OpenAI figures are unaffected. A "grading share" overstates the assessor, and a cost comparison
across the fix is not like-for-like. Stored figures are not rewritten (`docs/overseer/ai-benchmark.md`
§ *Harness Version 18 Updates*).

**Correction, 2026-09-17 (harness-29 round, H1): runs 36, 37, 38, 42, 46, 47 and 51 are prompt-less
OpenAI runs.** From 2026-09-10 `BuildCandidateSeedHistory` put the board (when present) ahead of the
system prompt, and `OpenAiResponsesProvider` builds `instructions` from the history's own system
messages, not from `SegmentedPrompt`: with no board `instructions` was omitted, and on a snapshot
suite the board became the entire system text. So run 47's Intelligence Index (83 ± 5) is not an
OpenAI-candidate measurement under the production prompt, and **the 47–48 pair is not a clean Tier A
pair on the model axis**: only 48 (Gemini) reliably received the prompt the harness recorded. See
`docs/overseer/ai-benchmark.md` § *Harness Version 29 Updates*.
