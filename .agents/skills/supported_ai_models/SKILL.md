---
name: supported_ai_models
description: Formal policy on the AI models supported by the Overseer project, plus measured Gemini service tier behaviour - which service_tier values Google honours, what availability and latency to expect per Gemini model, and where the served tier is reported.
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
