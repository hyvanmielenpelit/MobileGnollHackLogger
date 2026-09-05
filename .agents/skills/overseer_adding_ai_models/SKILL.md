---
name: overseer_adding_ai_models
description: How to add a single new AI model (Anthropic Claude, Google Gemini, or OpenAI GPT) to the Overseer project by adding an entry to the provider model catalog. Covers the ModelCatalogEntry field reference, per-provider thinking level and reasoning summary conventions, the prefix-matching contract in ModelMetadataService and its two traps, where to source each specification, and the mandatory rebuild. Triggered when requested to "add a new AI model", "add Claude/Gemini/GPT model to Overseer", "register a new model", "update the model catalog", or similar.
---

# Adding a New AI Model to Overseer

Adding a model to Overseer is a **data change, not a code change**. You add one JSON object to
one catalog file and rebuild. If you find yourself editing `ModelMetadataService.cs`, stop —
you have misread the problem.

## Scope

| Use this skill | Use something else |
|----------------|--------------------|
| Adding one or a few models by hand, any provider | Bulk-importing many Gemini models from a Google API dump — use **`adding_gemini_models`** |
| Correcting a model's display name, limits, or release date | Deciding *whether* a model may be supported at all — see **`supported_ai_models`** |

## The Catalogs

One file per provider, all under `Overseer/Services/ModelCatalogs/`:

| Provider | File |
|----------|------|
| Anthropic | `AnthropicModelCatalog.json` |
| Google | `GoogleModelCatalog.json` |
| OpenAI | `OpenAiModelCatalog.json` |

Each file is a **flat JSON array** of catalog entries, and each entry is deserialized into
`ModelCatalogEntry` (`Overseer/Services/ModelCatalogEntry.cs`) by `ModelMetadataService`.

> [!IMPORTANT]
> **These files are embedded resources**, pulled in by the wildcard
> `<EmbeddedResource Include="Services\ModelCatalogs\*.json" />` in `Overseer/Overseer.csproj`
> and read through `Assembly.GetManifestResourceStream`. Editing the JSON changes nothing at
> runtime until the **Overseer project is rebuilt** and the service restarted. A new file needs
> no `.csproj` change; the wildcard already covers it.

## Field Reference

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `prefixes` | string array | **yes** | — | The provider's exact model IDs. Almost always a single-element array. |
| `displayName` | string | **yes** | `""` | Shown in the model picker. Also used as `Description`. |
| `releaseDate` | string `YYYY-MM-DD` | **yes** | `""` | Drives the `createdAt` sort key sent to the Angular client (`SettingsController`), so the picker sorts newest-first by this value. |
| `thinkingLevels` | string array | yes, if the model reasons | `[]` | Passed through verbatim as the provider's effort/thinking parameter. Provider-specific — see below. |
| `reasoningModes` | string array | no | `[]` | OpenAI only, and only for models with a "pro" mode. |
| `reasoningSummaries` | string array | no | `[]` | Anthropic and OpenAI. Provider-specific — see below. |
| `contextWindowSize` | int | **yes** | `0` | Total context window in tokens. |
| `maxOutputTokens` | int | **yes** | `0` | Max output tokens. `MaxInputTokens` is **derived** as `contextWindowSize - maxOutputTokens`; never store it. |
| `supportsSubAgentCoordination` | bool | no | **`true`** | Set `false` only for small/cheap models that must not orchestrate sub-agents. |
| `supportsSubAgentExecution` | bool | no | **`true`** | Set `false` only for small/cheap models that must not run as a sub-agent. |
| `pricing` | object | no | *none* | Published list pricing per million tokens. Omit entirely when the provider publishes no price. See schema below. |

Because both sub-agent flags default to `true`, **omit them for capable models** and add them
only to opt a weak model out. That is what the existing catalogs do — only the Gemini Flash-Lite
and GPT Nano entries carry them.

### The `pricing` Object

Published list rates are declared per 1,000,000 tokens. When a model has no published price, omit the `pricing` block entirely; Overseer treats absent pricing as "price not available", never as zero.

```json
"pricing": {
  "inputPerMillion": 5.00,
  "outputPerMillion": 22.50,
  "cachedInputPerMillion": 2.50,
  "cacheWritePerMillion": null,
  "currency": "USD",
  "asOf": "2026-09-05"
}
```

- `inputPerMillion` (decimal, required): Price per 1M uncached input tokens.
- `outputPerMillion` (decimal, required): Price per 1M output tokens.
- `cachedInputPerMillion` (decimal, optional): Price per 1M cached prompt tokens. If omitted, prompt cache reads are costed at `inputPerMillion`.
- `cacheWritePerMillion` (decimal, optional): Price per 1M cache creation/write tokens (e.g. Anthropic). If omitted or null, cache write cost is omitted.
- `currency` (string, optional): ISO currency code (defaults to `"USD"` if omitted).
- `asOf` (string, required if pricing is present): ISO date (`YYYY-MM-DD`) when published list pricing was verified. Ensures operators know how fresh the pricing metadata is.

## Per-Provider Conventions

Copy these verbatim from a sibling entry rather than inventing values. They reflect what each
provider's API actually accepts.

| Provider | `thinkingLevels` | `reasoningSummaries` | `reasoningModes` |
|----------|------------------|----------------------|------------------|
| Anthropic | `["low", "medium", "high", "xhigh", "max"]` — but **drop `xhigh`** for Claude 4.6 Opus and 4.6 Sonnet, which support `max` without it | `["summarized", "omitted"]` | *(omit)* |
| Google | Per model — see below; commonly `["low", "medium", "high"]` | *(omit)* | *(omit)* |
| OpenAI | `["none", "low", "medium", "high", "xhigh", "max"]` | `["auto", "concise", "detailed"]` | `[]`, or `["standard", "pro"]` for pro-capable models |

For Anthropic, `AnthropicProvider` sends the chosen level as `output_config.effort` next to
`thinking: { type: "adaptive" }`. All supported Claude models use adaptive thinking; there is no
legacy fixed-budget path to consider.

> [!IMPORTANT]
> **Effort support is per model — check it, do not copy it blindly.** `xhigh` in particular is not
> universal: it is supported on Fable 5.1, Fable 5, Opus 5, Opus 4.8, Opus 4.7 and Sonnet 5, but
> **not** on Opus 4.6 or Sonnet 4.6, which support `max` without it. The authoritative per-level,
> per-model list is the "Effort levels" table at
> `https://platform.claude.com/docs/en/build-with-claude/effort` — read it for every new Claude
> model rather than copying the previous entry's array.
>
> **Google is per model too.** `minimal` is accepted by Gemini 3.5 Flash, 3.5 Flash-Lite and
> 3.6 Flash, but **rejected by 3.7 Flash and 3.8 Flash**, whose documentation states it is not
> supported and returns an error. Read the levels from
> `https://ai.google.dev/gemini-api/docs/models/<model-id>` or the per-model table at
> `https://ai.google.dev/gemini-api/docs/thinking` for every new Gemini model. The
> `/v1beta/models` dump does not carry them — its `thinking` flag is a boolean.

### Display name conventions

Read the convention off the catalog, not off the vendor's marketing page:

| Provider | Form | Examples |
|----------|------|----------|
| Anthropic | `Claude <version> <tier>` — **version before tier**, reversing Anthropic's own word order | `Claude 5 Opus`, `Claude 5.1 Fable` |
| Google | `Gemini <version> <tier>` | `Gemini 3.5 Flash`, `Gemini 3.5 Flash-Lite` |
| OpenAI | `GPT-<version> <tier>` | `GPT-5.5 Pro`, `GPT-5.6 Luna` |

## The Prefix-Matching Contract, and Its Two Traps

`ModelMetadataService` matches a model ID against catalog `prefixes` in two different ways.
Understanding both prevents the two mistakes this skill exists to stop.

**`IsWhitelisted`** — decides whether the model is offered at all. It accepts a prefix match
where the remaining suffix is empty, or is a hyphen followed only by digits, dots, and hyphens
(`IsVersionSuffix`).

**`GetMetadata`** — decides which entry describes the model. It scans every prefix and keeps the
**longest** match.

### Trap 1 — a point release is silently already whitelisted, under the wrong name

`claude-fable-5-1` leaves the suffix `-1` against the `claude-fable-5` prefix. That is all
digits, so `IsVersionSuffix` returns `true` and the model **passes the whitelist with no entry
of its own** — then `GetMetadata` labels it with its predecessor's name and release date.

> **Never conclude from "it already appears in the model picker" that no catalog entry is
> needed.** Check the *display name and release date*, not mere presence. A point release that
> shows up under the previous version's name is exactly the bug this trap produces.

### Trap 2 — order does not matter, but strict prefixes do

Array position is irrelevant, because `GetMetadata` takes the longest match. `gpt-5.4` and
`gpt-5.4-pro` coexist correctly wherever they sit in the file, and so do `gemini-3.5-flash` and
`gemini-3.5-flash-lite`.

What does matter: an entry whose prefix is a **strict prefix of a different model's ID** will
capture that model whenever the more specific entry is missing. Add the specific entry and the
capture stops.

Note the asymmetry between the two methods: `gpt-5.4-pro` is *not* whitelisted by the `gpt-5.4`
entry, because `-pro` is not a version suffix. Only numeric suffixes leak.

## Workflow

1. **Confirm the model is eligible.** `supported_ai_models` restricts Overseer to models released
   on or after 2026-01-01. Do not add older models.
2. **Get the exact model ID and specs** from the provider (see below). The ID must be the one the
   provider's API accepts — for Anthropic that is the bare ID with **no date suffix**
   (`claude-sonnet-5`, never `claude-sonnet-5-20260630`, which returns `404`).
3. **Check for an existing entry**, including one that would capture the new ID through Trap 1.
4. **Append the entry** to the end of the provider's catalog array, matching the file's existing
   grouping and indentation (two spaces).
5. **Preserve the file's encoding**: UTF-8 **without BOM**. Match the line endings the file
   already uses — the catalogs are **LF**, not CRLF. Verify by byte count, never with `grep`.
6. **Rebuild** — `dotnet build Overseer\Overseer.csproj`. Without this the change is invisible.
7. **Verify** — see below.

### Where to source the specifications

| Provider | Source | Maps to |
|----------|--------|---------|
| Anthropic | The model's overview page, `https://platform.claude.com/docs/en/models/<model>/overview` — the "Specifications" tables | Model ID, context window, max output, released date |
| Google | `GET https://generativelanguage.googleapis.com/v1beta/models?key=<API_KEY>` for the ID and the token limits, **plus the model's own page**, `https://ai.google.dev/gemini-api/docs/models/<model-id>`, for the thinking levels and the launch date | `name` minus `models/` → `prefixes`; `displayName`; `inputTokenLimit` → `contextWindowSize`; `outputTokenLimit` → `maxOutputTokens`; the levels listed on the model page → `thinkingLevels`; a stated launch date → `releaseDate` |
| OpenAI | The model's page in the OpenAI docs, plus `GET https://api.openai.com/v1/models` for the exact ID | Model ID, context window, max output |

Google's API does not report a release date. Prefer a launch date stated on the model's own page
or model card; fall back to the current UTC date only when none is published. Anthropic and OpenAI
publish one — use the published date, not today's.

## Verification

```powershell
Get-Content Overseer\Services\ModelCatalogs\<Provider>ModelCatalog.json -Raw | ConvertFrom-Json | Out-Null
```

A malformed catalog does **not** fail loudly: `LoadCatalogs` deserializes it to `null` and
silently skips the whole provider, so every model from that provider disappears from the picker.
Always parse the file after editing.

```powershell
dotnet build Overseer\Overseer.csproj
dotnet test Overseer.Tests\Overseer.Tests.csproj
```

`SubAgentCatalogTests` and `DelegateToSubAgentToolTests` exercise `ModelMetadataService` against
the real catalogs.

Then, in the running app: open the **Models** page, pick the provider, and open the model picker.
Confirm the new model shows the intended display name, sorts by its release date, and reports the
expected context window, max output, and thinking levels. Confirm the *previous* version still
appears separately under its own name — proof that the new entry did not capture it.

> The picker lists models fetched live from the provider's own `/models` API and filtered through
> the catalog. A model absent from the provider's API will not appear no matter what the catalog
> says, and no frontend file needs changing to add a model.

## Optional: Token Pricing for Benchmark Cost Estimation

Pricing lives in configuration (`Overseer/appsettings.json` or user secrets), **never in the catalog JSON files** — prices change without a release, and a hardcoded roster in source drifts silently.

To enable estimated cost reporting in the AI Benchmark for the new model, add an entry under `ModelPricing`:

```json
"ModelPricing": {
  "<model-id-prefix>": {
    "InputPerMillion": 2.50,
    "OutputPerMillion": 10.00,
    "CachedInputPerMillion": 0.25
  }
}
```

Prefixes match by longest prefix against model IDs (e.g. `gpt-5.6` matches `gpt-5.6-luna`). `CachedInputPerMillion` is optional (omit if the model does not support prompt caching). If pricing is not configured, benchmark reports display that pricing is not configured and continue normally.

## What NOT to Do

- **Do not edit `ModelMetadataService.cs`** to special-case a model. There are no per-model
  branches and none may be added; `supported_ai_models` forbids legacy parsers and regexes.
- **Do not add a hardcoded model list to the Angular client.** There is none, by design.
- **Do not change `RecommendedModels` in `Overseer/appsettings.json`** unless asked. It is a
  curated default per provider, not a "newest model" pointer.
- **Do not touch `MaxOutputTokensPerProvider`.** It is an independent request cap, unrelated to a
  model's ceiling.
- **Do not add beta-only capabilities** to `reasoningSummaries` or `thinkingLevels` unless the
  provider integration actually implements them.
- **Do not add models released before 2026-01-01.**

## Related Skills

- **`adding_gemini_models`** — bulk import of many Gemini models from a Google API dump.
- **`supported_ai_models`** — which models are eligible, plus measured provider latency and
  service-tier behaviour.
- **`server_implementation_planning`** — a catalog change plus documentation touches several
  files and needs a plan.
