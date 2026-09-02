---
name: overseer-adding-ai-models
description: How to add a single new AI model (Anthropic Claude, Google Gemini, or OpenAI GPT) to the Overseer project by adding an entry to the provider model catalog. Covers the ModelCatalogEntry field reference, per-provider thinking level and reasoning summary conventions, the prefix-matching contract in ModelMetadataService and its two traps, where to source each specification, and the mandatory rebuild. Triggered when requested to "add a new AI model", "add Claude/Gemini/GPT model to Overseer", "register a new model", "update the model catalog", or similar.
---

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/overseer_adding_ai_models/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.