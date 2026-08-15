---
name: adding_gemini_models
description: Instructions and guidelines for autonomously parsing new Gemini models from a JSON file and inserting them into the Overseer GoogleModelCatalog.json without duplicates. Triggered when requested to "add new Gemini models", "update Gemini models list", or similar for the Overseer project.
---

# Adding Gemini Models to Overseer

These rules apply when you are tasked with adding new Gemini models to the Overseer project.

## Workflow

1.  **Read Inputs**:
    *   Read the new models from `C:\hmp\plans\new-models.json`.
    *   Read the existing catalog from `C:\hmp\MobileGnollHackLogger\Overseer\Services\ModelCatalogs\GoogleModelCatalog.json`.

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
    *   Save the updated `GoogleModelCatalog.json`. Do **NOT** modify or delete `C:\hmp\plans\new-models.json`.

## Example Mapping

### Source Data (`C:\hmp\plans\new-models.json`)
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
