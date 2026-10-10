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
- **A new point release is missing from the picker until it has its own entry.** An ID that extends a
  catalogued one with a version number, such as `claude-sonnet-5-1` beside `claude-sonnet-5`, is treated
  as a different model: it is not offered, it borrows none of its predecessor's name, limits or price,
  and Overseer's log carries a warning containing `looks like a new version of catalog entry` that
  names it. Adding the catalog entry is what makes it appear. Dated snapshots (`-YYYYMMDD`,
  `-YYYY-MM-DD`, `-NNN`, as in `gpt-5.4-2026-03-05` or `gemini-3.7-flash-001`) still appear under
  their base model's name.

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

---

## Retiring a Model

When a provider withdraws a model, **retire** it: delete its catalog entry **and** add an entry to the
retired-models list. Like adding a model, this is a data change in two JSON files, followed by a
rebuild.

### 1. Record It in `RetiredModels.json`

`Overseer/Services/ModelCatalogs/RetiredModels.json` is a flat JSON array, embedded by the same
`Services\ModelCatalogs\*.json` glob as the catalogs and read by `ModelMetadataService`
(`GetRetiredEntries`, `GetRetiredEntry`). Write the entry **before** deleting the catalog entry, because
`lastKnown` is copied from it:

```json
{
  "provider": "Google",
  "prefixes": ["gemini-3.7-flash"],
  "displayName": "Gemini 3.7 Flash",
  "retiredOn": "2026-10-10",
  "note": "Withdrawn from the Gemini API by Google.",
  "replacement": "gemini-3.8-flash",
  "lastKnown": {
    "thinkingLevels": ["low", "medium", "high"],
    "contextWindowSize": 1048576,
    "maxOutputTokens": 65536,
    "inputPerMillion": 0.75,
    "outputPerMillion": 3.75,
    "cachedInputPerMillion": 0.075
  }
}
```

| Field | Notes |
|---|---|
| `provider` | `Anthropic`, `Google` or `OpenAI`. |
| `prefixes` | The deleted entry's prefixes. A retired prefix matches a model ID exactly or followed by a dated snapshot suffix (`gemini-3.7-flash-001`), never as a variant. |
| `displayName` | The deleted entry's display name. The admin alert and the resolution dialog name the model by it. |
| `retiredOn` | `yyyy-MM-dd`, the day the provider withdrew the model. |
| `note` | Optional. One sentence appended to the notice users see. |
| `replacement` | Optional. The model ID offered first when switching, and as **Use {replacement}** in chat. **It must be a catalogued model** (whitelisted); one that is not is never offered, and a test fails. |
| `lastKnown` | The deleted entry's **base card**: its `thinkingLevels`, `contextWindowSize`, `maxOutputTokens`, and the `inputPerMillion`, `outputPerMillion` and `cachedInputPerMillion` of its `pricing` (not `longContext` or `scheduledChange`). Used **only** to prefill *Keep as custom model*; it is never runtime metadata or pricing. |

**An active catalog entry always wins.** `GetRetiredEntry` returns nothing for an ID the catalog
describes (exact, snapshot or variant), so a retired prefix can never hide a model that is still
offered.

### 2. Delete the Catalog Entry, Then Fix the Tests

Delete the model's entry from its provider catalog and rebuild. Then update the **tests that used the
retired ID as a catalogued model** — a whitelisting, metadata or pricing assertion about it — by
moving them to the replacement. Other occurrences of the old ID in tests are opaque data (an ID stored
on a fixture row, a string in a recorded run) and stay as they are.

> **Deleting the entry without a retired entry is the wrong half.** The model then reads *Not in
> catalog* instead of *Removed*: users see a blue advisory rather than the retirement date and the
> replacement, administrators get no alert, and GnollBench does not refuse it.

### How a Row Is Classified

`UserAiModel` and `SystemAiApiConfiguration` carry `ModelCatalogMode`: `catalog`, `custom`, or null for
a legacy row, which counts as `catalog`. The server derives it on create (`catalog` when the catalog
describes the model ID, else `custom`) and on update only when the model ID or provider changes; the
client never sends it. An existing row therefore stays in catalog mode when its model is retired.

`ModelAvailabilityService.Evaluate` classifies a row; the first match wins:

1. **Custom endpoint** — the row has a Base URL. A gateway or Azure deployment name is legitimately
   uncatalogued.
2. **Custom** — the row's mode is `custom`.
3. **Available** — the catalog describes the model ID (`IsDescribedByCatalog`: exact, snapshot or variant).
4. **Retired** — a retired prefix matches.
5. **Not in catalog** — anything else.

Retired and Not in catalog **need attention**; the API returns the result as `modelAvailability` on
each row.

### What Users and Administrators See

- **Models page.** A flagged user model shows a *Removed* or *Not in catalog* badge, a notice and
  **Resolve…**, which opens the resolution dialog; a custom-mode row shows *Custom model*. A flagged
  system-provided model shows the notice and *An administrator needs to update this model. You can
  choose another model in chat.* The model form shows the notice in edit mode.
- **Chat.** The model picker shows the same chip. With a flagged model selected, a notice above the
  input offers **Resolve…** (own model), **Use {replacement}**, **Choose another model** and, for an
  administrator, **Open System Configs**. Chat never refuses to send to a flagged model, because
  providers sometimes keep serving a withdrawn model for a grace period. When the provider answers
  with a "model not found" 404 (`ModelNotFoundClassifier`: Google `error.status == NOT_FOUND`, OpenAI
  `error.code == model_not_found`, Anthropic `error.type == not_found_error`, or a JSON 404 whose
  message names the model ID), the turn ends with an `error` event whose `ErrorCode` is
  `model_unavailable` and whose text says the provider no longer serves the model, without a retry; a
  system model's failure is also written to the system AI error log. A remembered chat model that no
  longer exists is forgotten, and a one-time note says which model was selected instead.
- **Admin.** `ConfigHealthService` raises one warning per retired model used by enabled system
  configurations (id `retired-model-{provider}-{firstPrefix}`), linking to **Review in System
  Configs**. *Not in catalog* raises no alert. The System Configs tab lists every flagged configuration
  in a summary banner and gives each row the badge, the notice and **Resolve…**;
  `/admin?tab=configs&resolveConfig={id}` opens the dialog for one configuration.

### The Three Resolutions

The dialog (`app-model-resolution-dialog`) offers three choices:

- **Switch** to another catalog model of the same provider, **in place**: the row keeps its id, so chat
  selections, assignments, confidential trust and history attribution carry over. A dry-run preview
  lists every change first. The thinking level and reasoning settings are adapted to what the target
  supports, the limits are clamped to its ceilings, the display name follows the row's display-name
  mode, and a custom price is kept with a note to check it.
- **Keep as custom model**: the row switches to custom mode with its own limits and prices, prefilled
  from `lastKnown` for a retired model. Replies fail once the provider stops serving it.
- **Delete** the row.

The endpoints are `POST api/settings/usermodels/{id}/model-resolution`,
`POST api/admin/systemconfigs/{id}/model-resolution` and `GET api/settings/model-catalog/{provider}`
(the switch targets, newest first). A non-dry-run switch of a system configuration in live use is
refused with 409 and the same blockers as deletion (`SystemConfigUsageGuard`, which counts the
co-assessor role too); keeping it as custom is not guarded.

### What GnollBench Refuses

The run launcher refuses a run when **any** of its roles — tested model, assessor, co-assessor, second
or reference reader, claim verifier, report writer — uses a retired model
(`BenchmarkRunLauncher.RetiredModelRefusal`); AI report writing refuses a retired report writer the same
way. Custom-mode and Not-in-catalog models are admitted: a Not-in-catalog model gets an advisory that
its cost is reported as unknown unless it has a custom price. The client disables **Start** with a hint
and offers **Resolve in System Configs**. Other GnollBench jobs show the chip but are not refused.

### Tests That Guard the List

`ModelMetadataServiceTests` checks that the list loads, that no retired prefix equals or is shadowed
by an active prefix of the same provider, that every `replacement` is whitelisted, that every
`retiredOn` parses as `yyyy-MM-dd`, and that a retired prefix matches with and without a snapshot
suffix but never an active model, a variant or another provider's model. A new retired entry is covered
with no test change.


