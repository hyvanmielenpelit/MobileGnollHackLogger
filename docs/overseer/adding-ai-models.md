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
