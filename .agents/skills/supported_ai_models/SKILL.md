---
name: supported_ai_models
description: Formal policy on the AI models supported by the Overseer project.
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
