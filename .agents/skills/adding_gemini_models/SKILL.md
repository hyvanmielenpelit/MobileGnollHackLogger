---
name: adding_gemini_models
description: Instructions and guidelines for autonomously parsing new Gemini models from a JSON file and inserting them into the Overseer GoogleModelCatalog.json without duplicates. Triggered when requested to "add new Gemini models", "update Gemini models list", or similar for the Overseer project.
---

# Adding Gemini Models to Overseer

These rules apply when you are tasked with adding new Gemini models to the Overseer project.

> [!NOTE]
> **This skill is the bulk-import path**: it parses a Google API dump from `new-models.json` and
> appends many models at once. To add a **single** model by hand, or a model from **Anthropic or
> OpenAI**, use **`overseer_adding_ai_models`** instead — it documents the full
> `ModelCatalogEntry` field reference, the per-provider conventions, and the prefix-matching
> traps in `ModelMetadataService`.

## Workflow

1.  **Read Inputs**:
    *   Read the new models from the **input file the user names in the prompt** (conventionally called `new-models.json`). If the user pasted the model JSON directly into the prompt instead, use that and skip the file entirely.
    *   If the user gave neither, **ask for the path — do not guess.** If you fetched the dump from the Google API yourself, write it into your harness's own scratch directory first and read it back from there.
    *   Read the existing catalog from `C:\hmp\MobileGnollHackLogger\Overseer\Services\ModelCatalogs\GoogleModelCatalog.json`.

    > [!IMPORTANT]
    > **The input file must never live in the `plans` repository (`C:\hmp\plans`) or anywhere inside a project repository.** `plans` is a committed, pushed store for implementation plans and walkthroughs; a transient model dump left there becomes a stray untracked file in a repository agents commit to. Transient files belong in your harness's scratch directory, per the global agent rules. Earlier versions of this skill named a path under `C:\hmp\plans\` — that was wrong and is no longer used.

2.  **Duplicate Checking**:
    *   For each model in `new-models.json`, check if its intended prefix (extracted by removing `"models/"` from its `"name"`) already exists in the `"prefixes"` array of any existing model in `GoogleModelCatalog.json`.
    *   If the model already exists in the catalog, **warn the user with an error message** for that specific model and **skip inserting it** to avoid duplicates.

3.  **Data Mapping**:
    *   For each new model that is *not* a duplicate, construct a new JSON object with the following fields:
        *   `prefixes`: An array containing a single string. Extract this string by removing `"models/"` from the source `"name"` property.
        *   `displayName`: Copy directly from the source `"displayName"`.
        *   `releaseDate`: Retrieve the current UTC date and format it as `"YYYY-MM-DD"`.
        *   `thinkingLevels`: **Only if** the `"thinking"` property is `true` in the source JSON, add this field and set it to `["minimal", "low", "medium", "high"]`. If `"thinking"` is missing or `false`, do NOT include the `thinkingLevels` field.
        *   `contextWindowSize`: Copy from the source `"inputTokenLimit"`.
        *   `maxOutputTokens`: Copy from the source `"outputTokenLimit"`.

4.  **Insertion & Save**:
    *   Append the newly constructed model objects to the **end** (bottom) of the JSON array in `GoogleModelCatalog.json`.
    *   Save the updated `GoogleModelCatalog.json`. Do **NOT** modify or delete the user's input file.

## Example Mapping

### Source Data (the input file, e.g. `new-models.json`)
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

### Target Object (`C:\hmp\MobileGnollHackLogger\Overseer\Services\ModelCatalogs\GoogleModelCatalog.json`)
```json
{
  "prefixes": ["gemini-3.7-flash"],
  "displayName": "Gemini 3.7 Flash",
  "releaseDate": "2026-08-15",
  "thinkingLevels": ["minimal", "low", "medium", "high"],
  "contextWindowSize": 1048576,
  "maxOutputTokens": 65536
}
```
