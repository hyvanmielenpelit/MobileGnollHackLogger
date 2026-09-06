# Adding AI Models to Overseer

This guide provides instructions on how to add new AI models from various providers to the Overseer project.

## How It Works

Adding a model is a **data change, not a code change**. Each provider has one catalog file under
`Overseer/Services/ModelCatalogs/` — `AnthropicModelCatalog.json`, `GoogleModelCatalog.json`, and
`OpenAiModelCatalog.json` — and adding a model means appending one JSON object to the relevant array.

The model picker lists models fetched live from each provider's own `/models` API and filtered through
these catalogs, so no frontend file and no C# file needs to change to add a model.

Two things catch people out:

- **The catalogs are embedded resources.** Editing the JSON changes nothing until the Overseer project
  is rebuilt (`dotnet build Overseer\Overseer.csproj`) and the service restarted.
- **A point release is often already whitelisted under the previous version's name.** The whitelist
  accepts a numeric version suffix, so `claude-fable-5-1` passes through the `claude-fable-5` entry and
  appears in the picker labelled "Claude 5 Fable". Presence in the picker does **not** mean an entry is
  unnecessary — check the display name and release date.

The AI skill **`overseer_adding_ai_models`** documents the full field reference, the per-provider
conventions for thinking levels and reasoning summaries, and the prefix-matching rules. Use it for any
provider by asking an agent:

> "In the MobileGnollHackLogger repository, add the model `<model-id>` to Overseer using the
> `overseer_adding_ai_models` skill."

Only models released on or after 2026-01-01 are supported; see the `supported_ai_models` skill.

## Google Gemini

Gemini has two paths. For **one model**, use `overseer_adding_ai_models` as described above. For **many
models at once**, the `adding_gemini_models` skill parses a Google API dump and appends them in bulk —
that is the workflow below.

To add new Gemini models in bulk, we utilize an Antigravity AI skill to automate the extraction and catalog updates. Follow these steps:

### 1. Get the Current Models List

First, fetch the list of available models from the Google Generative Language API.

You can use a REST client (like RestMan) or `curl`:

```bash
curl -o models.json "https://generativelanguage.googleapis.com/v1beta/models?key=YOUR_API_KEY"
```

*(Ensure you replace `YOUR_API_KEY` with a valid Google AI Studio API key.)*

### 2. Prepare the New Models

Review the generated `models.json` file and identify the new models you wish to add. 

Create a file named `new-models.json` and copy the relevant model objects into it as a JSON array. Put it
anywhere convenient that is **outside both this repository and the shared `plans` repository** — your
`Downloads` folder or a temp directory is fine. You will pass its path to the agent in the next step.

> Do **not** place it in `C:\hmp\plans\`. That is the shared, committed plans repository for
> implementation plans and walkthroughs, not a scratch area; a transient dump left there becomes a stray
> untracked file in a repository agents commit to.

**Example format (`new-models.json`):**

```json
[
  {
    "name": "models/gemini-3.7-flash",
    "version": "3.7-flash-08-2026",
    "displayName": "Gemini 3.7 Flash",
    "description": "Gemini 3.7 Flash",
    "inputTokenLimit": 1048576,
    "outputTokenLimit": 65536,
    "supportedGenerationMethods": [
      "generateContent",
      "countTokens",
      "createCachedContent",
      "batchGenerateContent"
    ],
    "temperature": 1,
    "topP": 0.95,
    "topK": 64,
    "maxTemperature": 2,
    "thinking": true
  }
]
```

### 3. Run the Antigravity Prompt

Once `new-models.json` is ready, run the following prompt in Google Antigravity, **giving the full path to
your file**:

> "In the MobileGnollHackLogger repository, add new Gemini models from `<path-to-your-new-models.json>`
> using the `adding_gemini_models` skill."

(You can also paste the model JSON straight into the prompt and skip the file altogether.)

Antigravity will automatically:
- Read your `new-models.json` file.
- Check for duplicates against the existing catalog.
- Map the required fields (like `contextWindowSize` and `thinkingLevels`).
- Append the new models to the end of the `GoogleModelCatalog.json` array. Order does not matter:
  `GetMetadata` resolves a model by longest prefix match, not by position.

> **`thinkingLevels` is per model for Gemini too — it is not a constant.** `minimal` is
> supported on Gemini 3.5 Flash, 3.5 Flash-Lite and 3.6 Flash, but **not** on 3.7 Flash or
> 3.8 Flash, where it returns an error. The `/v1beta/models` dump reports only a boolean
> `thinking` flag, so the levels have to come from the model's page at
> [ai.google.dev/gemini-api/docs/models](https://ai.google.dev/gemini-api/docs/models) or the
> per-model table in the [thinking guide](https://ai.google.dev/gemini-api/docs/thinking).

---

## Anthropic / Claude

### 1. Find the Model's Specifications

Anthropic publishes a per-model overview page:

```
https://platform.claude.com/docs/en/models/<model>/overview
```

For example, `https://platform.claude.com/docs/en/models/fable-5-1/overview`. Read the
**Specifications** tables for the Claude API model ID, context window, max output, and release date.

> Use the bare model ID with **no date suffix** — `claude-sonnet-5`, never
> `claude-sonnet-5-20260630`. A date-suffixed ID returns `404 not_found_error`.

You can also list the IDs your key can reach:

```bash
curl -H "x-api-key: YOUR_API_KEY" -H "anthropic-version: 2023-06-01" https://api.anthropic.com/v1/models
```

### 2. Add the Catalog Entry

Append to `Overseer/Services/ModelCatalogs/AnthropicModelCatalog.json`. All supported Claude models use
adaptive thinking, so the reasoning-summary values are the same for every entry:

```json
{
  "prefixes": ["claude-fable-5-1"],
  "displayName": "Claude 5.1 Fable",
  "releaseDate": "2026-09-01",
  "thinkingLevels": ["low", "medium", "high", "xhigh", "max"],
  "reasoningSummaries": ["summarized", "omitted"],
  "contextWindowSize": 1000000,
  "maxOutputTokens": 128000
}
```

> **`thinkingLevels` is per model — do not copy it blindly.** `xhigh` is supported on Fable 5.1,
> Fable 5, Opus 5, Opus 4.8, Opus 4.7 and Sonnet 5, but **not** on Opus 4.6 or Sonnet 4.6, which
> support `max` without it. Check the "Effort levels" table at
> [platform.claude.com/docs/en/build-with-claude/effort](https://platform.claude.com/docs/en/build-with-claude/effort)
> for each new model.

Display names follow `Claude <version> <tier>` — version before tier, reversing Anthropic's own word
order (`Claude 5 Opus`, `Claude 5.1 Fable`).

### 3. Rebuild and Verify

```bash
dotnet build Overseer/Overseer.csproj
```

Open the **Models** page, choose **Anthropic**, and confirm the new model appears under the intended
name, sorted by its release date — and that the previous version still appears separately under its own
name.

---

## OpenAI

### 1. Find the Model's Specifications

Use the model's page in the OpenAI documentation for the context window and max output, and list the
exact IDs your key can reach:

```bash
curl -H "Authorization: Bearer YOUR_API_KEY" https://api.openai.com/v1/models
```

### 2. Add the Catalog Entry

Append to `Overseer/Services/ModelCatalogs/OpenAiModelCatalog.json`:

```json
{
  "prefixes": ["gpt-5.6-luna"],
  "displayName": "GPT-5.6 Luna",
  "releaseDate": "2026-06-26",
  "thinkingLevels": ["none", "low", "medium", "high", "xhigh", "max"],
  "reasoningModes": ["standard", "pro"],
  "reasoningSummaries": ["auto", "concise", "detailed"],
  "contextWindowSize": 1050000,
  "maxOutputTokens": 128000
}
```

Set `reasoningModes` to `["standard", "pro"]` only for models that offer a pro mode; otherwise use `[]`.
For a small model that should not orchestrate or run as a sub-agent (the Nano tier), also add
`"supportsSubAgentCoordination": false` and `"supportsSubAgentExecution": false` — both default to
`true` when omitted.

### 3. Rebuild and Verify

```bash
dotnet build Overseer/Overseer.csproj
```

Open the **Models** page, choose **OpenAI**, and confirm the new model appears as expected.

### 4. Optional: Configure Token Pricing

Token pricing is declared directly in the catalog JSON files under an optional `pricing` object. Published list prices are model facts rather than deployment configuration, and no provider offers an API to fetch them dynamically:

```json
"pricing": {
  "inputPerMillion": 5.00,
  "outputPerMillion": 22.50,
  "cachedInputPerMillion": 2.50,
  "cacheWritePerMillion": null,
  "asOf": "2026-09-05"
}
```

- `inputPerMillion` and `outputPerMillion`: Required when `pricing` is specified. Rates are in USD per 1,000,000 tokens.
- `cachedInputPerMillion`: Optional rate for prompt cache read hits. If omitted, cache reads are costed at `inputPerMillion`.
- `cacheWritePerMillion`: Optional rate for prompt cache writes/creation (e.g. Anthropic). If omitted or null, cache creation cost is omitted.
- `asOf`: The date (`"YYYY-MM-DD"`) when the published list price was verified. Bump it **only when the figures beside it were actually read from the page that day** — an `asOf` on a figure nobody re-checked is worse than a stale one, because it stops the next reader looking.

The catalog has no `currency` field, because Overseer prices exclusively in USD.

When a model publishes no pricing, omit the `pricing` block entirely. Overseer treats absent pricing as "price not available", never as zero.

#### Conditional Rate Cards

Three optional blocks inside `pricing` describe rates that are not flat. Each is genuinely optional and is **never inferred from a sibling model** — write one only where the model's own provider page states it.

```json
"pricing": {
  "inputPerMillion": 10.00,
  "outputPerMillion": 50.00,
  "cachedInputPerMillion": 1.00,
  "cacheWritePerMillion": 12.50,
  "asOf": "2026-09-06",

  "longContext": {
    "thresholdInputTokens": 272000,
    "inputPerMillion": 20.00,
    "outputPerMillion": 75.00,
    "cachedInputPerMillion": 2.00,
    "cacheWritePerMillion": 25.00
  },

  "serviceTierMultipliers": { "batch": 0.5, "flex": 0.5, "priority": 2.0, "fast": 2.0 },

  "scheduledChange": {
    "effectiveFrom": "2027-01-01",
    "inputPerMillion": 1.50,
    "outputPerMillion": 7.50,
    "cachedInputPerMillion": 0.15,
    "note": "Promotional pricing through 2026-12-31"
  }
}
```

**`longContext`** — the rate card a provider applies to a request whose prompt exceeds a threshold.

- `thresholdInputTokens` is compared against **a single request's** prompt tokens, cache reads included, and **never** against a turn's summed tokens. An agentic turn makes tens of calls and its sum crosses any published threshold routinely while no individual request comes close; costing the sum would surcharge nearly every turn Overseer makes.
- The rates are **absolute**, not multipliers, because providers surcharge input and output by different factors.
- `cachedInputPerMillion` and `cacheWritePerMillion` are optional and fall back to the base rates when omitted — which is the conservative reading of a page that mentions only input and output.
- The surcharge applies to the **full request**, not only to the tokens above the threshold.

**`serviceTierMultipliers`** — a scalar per served service tier, applied to all four rates.

- Keys are the **normalized served** tier strings, as `ProviderHelper.NormalizeServiceTier` produces them (lower-case, no `SERVICE_TIER_` prefix): `batch`, `flex`, `priority`, `fast`. `default` and `standard` are never listed; an unlisted tier costs 1.0×, never zero.
- Costing reads `ActualServiceTierUsed` — the tier the provider **served** — and falls back to the requested tier only when the provider reported none. A tier the provider reported but the catalog does not list is 1.0×: OpenAI requests `auto`/`fast` and serves `default`/`priority`, so a priority request served as `default` must bill as `default`.
- A mistyped key produces no error at all, just quiet 1.0× mispricing, so a unit test asserts that every key the shipped catalogs declare round-trips through `NormalizeServiceTier`.

**`scheduledChange`** — a price change the provider has already announced for a future date.

- **The base card is always the rate in force today.** `scheduledChange` is the card that takes over on `effectiveFrom`, compared against UTC today. It exists because `asOf` records when a price was *verified*, not when it *expires*: without it, a campaign's end date passes and every turn is costed at the promotional rate until somebody happens to re-read a pricing page.
- One change per entry, not a queue. It replaces the four base rates only — not the `longContext` card and not the tier multipliers.
- An unparseable `effectiveFrom` resolves to **no** schedule, leaving the base card unchanged: a malformed date must never silently move a price.
- Once the date has passed, the Models page and the Admin system-config list show an advisory to fold the change into the base rates and re-verify. The cost is correct either way; the advisory keeps the catalog from becoming a changelog of elapsed schedules.

Composition order is: **base card → scheduled card if its date has passed → long-context card if this request crossed the threshold → × the served tier's multiplier.**

**Per-provider findings, verified 2026-09-06:**

| Provider | Long context | Service tiers | Scheduled changes |
|---|---|---|---|
| **OpenAI** | >272 K at 2× input and 1.5× output for the full request, on `gpt-5.4`, `gpt-5.4-pro`, `gpt-5.5`, the three GPT-5.6 models and `gpt-6-astra`. **Not** on `gpt-5.4-mini` or `gpt-5.4-nano`, whose 400 K windows cap input at 272 K so the threshold is unreachable, and **not** on `gpt-5.5-pro`, whose page states none. `gpt-6-astra` alone surcharges cache rates too ("2x input **and cache rates**"); everywhere else the long-context card omits the cache rates and they fall back to the base card. | Batch and Flex 0.5×, Fast mode (formerly Priority) 2.0× | none |
| **Google** | Pro models only, >200 K at $4.00 / $18.00 / $0.40. Of the Flash models the page says "No threshold pricing tiers." | Batch and Flex 0.5×, Priority 1.8× | Gemini 3.6 / 3.7 / 3.8 Flash run at half price through 2026-12-31, returning to $1.50 / $7.50 on 2027-01-01 |
| **Anthropic** | none — "Claude 4.6 and later models … include the full 1M token context window at standard pricing" | **none declared, deliberately.** Overseer sends neither `speed: "fast"` nor Batch API requests, and Anthropic's served `service_tier: priority` is the Priority Tier *capacity* product, not Fast mode and not a price change; declaring `"priority": 2.0` there would double every Claude cost | none |

#### Custom Deployment Overrides

Operators and users can override default catalog rates through the UI:
- **Admin → System AI Configs**: Set **Pricing** mode to **Custom** on any system configuration to define custom input, output, and cached input rates.
- **Settings → Models**: Set **Pricing** mode to **Custom** on any user model.

Overseer resolves pricing with the following precedence:
1. **Snapshotted Run Pricing**: For benchmark runs, the rates captured at run start.
2. **Custom Override**: The configuration or user model's custom rates.
3. **Catalog Default**: The provider catalog default for the model ID.
4. **null (Not Available)**: When neither an override nor catalog pricing exists.

**A custom override is a single flat rate.** It carries no long-context card, no service-tier multipliers and no schedule, and it replaces all of them: supporting the three would need a threshold, four more rates, a multiplier map and a date on both override entities, for a case nobody has. An operator who needs any of them sets the rate they want instead. The model form says so beneath the custom-pricing fields.


