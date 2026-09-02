---
name: supported_ai_models
description: Formal policy on the AI models supported by the Overseer project, plus measured provider behaviour - which Gemini service_tier values Google honours and where the served tier is reported, and measured per-model Anthropic Claude latency, time-to-first-token, availability, and the per-model thinking and display defaults.
---
# Supported AI Models Policy

When working on AI integration within the Overseer project (e.g., `ChatService.cs`, `ModelMetadataService.cs`), adhere to the following policy:

1. **Only Support Models Released in 2026 or Later**:
   - We do not support legacy models released before January 1st, 2026.
   - Specifically, models like GPT-4o, Claude 3.5 Sonnet, and Gemini 2.5 are **NOT** supported.
   - We only support newer models such as GPT-5.x, Claude 4.6+, Claude 5+, and Gemini 3.x.

2. **API Payload Simplification**:
   - Because we no longer support older models, we can assume modern API features are available.
   - For example, Anthropic models uniformly use the modern `"adaptive"` thinking type (via `output_config.effort`) rather than the legacy `"enabled"` type with a fixed token budget.

3. **Model Metadata Registration**:
   - Do not add regexes or metadata parsers for legacy models into `ModelMetadataService.cs`. Keep the codebase clean and focused on modern capabilities.

4. **OpenAI Provider**:
   - Always use `OpenAiResponsesProvider`. The legacy `OpenAiProvider` has been removed.

5. **Gemini Service Tiers — Measured Behaviour**:
   - Before reasoning about Google `service_tier` values (`priority`, `flex`, `standard`), read
     **[`docs/overseer/gemini-service-tier-measurements.md`](../../../docs/overseer/gemini-service-tier-measurements.md)**.
     It records real measurements against a paid Tier 2 project, not documentation claims.
   - Key findings, so you do not have to guess:
     - `service_tier` **is honoured** — Google echoes back exactly the tier requested.
     - `priority` **does not buy availability**. It does not prevent HTTP 503 on a saturated model,
       and no latency benefit was measurable on any model.
     - The served tier is reported in the response **body** (`usageMetadata.serviceTier`). The
       `x-gemini-service-tier` **header is absent on `:streamGenerateContent`** — the endpoint
       Overseer chat uses. Never rely on the header for streaming.
     - Availability varied enormously **by model**, not by tier or by billing tier.
   > [!IMPORTANT]
   > **Availability findings expire.** The newest Gemini model is always the most heavily used,
   > so it is usually the saturated one. When a new Gemini generation ships, expect the
   > congestion to move to it and today’s congested model to become reliable. Re-measure
   > rather than quoting that document’s availability table after a model release; its
   > structural findings (tier honouring, where the tier is reported) are the durable part.

6. **Anthropic Claude Latency - Measured Behaviour**:
   - Before reasoning about Anthropic model speed, availability, or thinking configuration, read
     **[`docs/overseer/anthropic-model-latency-measurements.md`](../../../docs/overseer/anthropic-model-latency-measurements.md)**.
     It records 168 live calls across all seven supported Claude models, not documentation claims.
   - Key findings, so you do not have to guess:
     - **Availability was perfect** - 168/168 HTTP 200, no 429, no `529 overloaded_error`, no timeouts.
       Unlike Gemini, Anthropic scarcity was not a factor in this window.
     - **Release order does not predict speed.** `claude-opus-4-7` was the fastest model measured,
       beating Opus 4.8, Opus 5, and Opus 4.6. Measure; do not extrapolate.
     - **Spread is about 2x**: ~2.3 s median total for Opus 4.7 and Sonnet 5, ~4.6 s for Fable 5.
     - **`output_config.effort` is not a thinking-token dial.** At `high` on an easy prompt, every
       model spent only tens of output tokens - adaptive thinking scales to task difficulty.
     - **`claude-opus-4-6` ignores output-format instructions** far more than its siblings (it
       returned markdown-bolded answers on 20 of 24 calls despite being told to reply with the bare value).
   > [!IMPORTANT]
   > **`thinking` and `display` defaults differ per model at the API level, and this silently corrupts direct comparisons.**
   > At the raw Anthropic API level, omitting `thinking` runs **adaptive** on Fable 5, Opus 5, and Sonnet 5, but runs **no thinking at
   > all** on Opus 4.8, 4.7, 4.6, and Sonnet 4.6. `display` defaults to `omitted` everywhere except
   > Opus 4.6 and Sonnet 4.6, where it is `summarized`.
   >
   > **Application guarantee**: Within Overseer, `AnthropicProvider` always sends `thinking: {type:"adaptive"}` and an explicit effort (controlled by `AnthropicSettings:ExplicitDefaultEffort`, defaulting to `high`) even when Thinking Level is left on `Default`. However, `display` is still omitted when no reasoning summary is chosen, so the underlying `display` difference remains in effect for reasoning summaries. Always set both explicitly when making raw API comparisons or measuring latency.
   > **The latency figures expire** - they are a 12-minute snapshot. The structural findings above
   > are the durable part.
