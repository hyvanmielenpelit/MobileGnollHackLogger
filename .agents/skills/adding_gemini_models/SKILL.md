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

            > [!IMPORTANT]
            > A prefix extracted this way whitelists the **exact** ID and numerically suffixed
            > variants of it — nothing else. `IsWhitelisted` in `ModelMetadataService` accepts only
            > an empty remainder or a hyphen followed by digits, dots and hyphens, so
            > `gemini-3.1-pro` does **not** whitelist `gemini-3.1-pro-preview`, and a model served
            > under a `-preview` or dated ID is dropped from the picker with no error anywhere. If
            > the dump gives such an ID, that exact string must be in `prefixes`. See **Trap 2** in
            > `overseer_adding_ai_models` for the full contract.

        *   `displayName`: Copy directly from the source `"displayName"`.
        *   `releaseDate`: Formatted as `"YYYY-MM-DD"`. Google's `/models` API reports no release
            date, so the **current UTC date is a fallback, not the rule** — prefer a launch date
            when the model's own page or model card states one. This field is the model picker's
            sort key, and nothing else.
        *   `thinkingLevels`: **Only if** the `"thinking"` property is `true` in the source JSON.
            **The value is per model — never a constant.** The `/v1beta/models` dump carries only
            the boolean `thinking` flag; it does not report which levels a model accepts. Read the
            levels from the model's own page,
            `https://ai.google.dev/gemini-api/docs/models/<model-id>`, or from the per-model table
            in `https://ai.google.dev/gemini-api/docs/thinking`, and copy exactly what is listed
            there. If `"thinking"` is missing or `false`, do NOT include the field at all.

            > [!WARNING]
            > **`minimal` is not universal, and guessing it breaks the user's request.** Gemini 3.5
            > Flash, 3.5 Flash-Lite and 3.6 Flash accept `minimal`; **Gemini 3.7 Flash and 3.8
            > Flash do not** — their documentation states that `minimal` is not supported and
            > returns an error. `GoogleProvider` sends the selected level straight through as
            > `thinkingConfig.thinkingLevel`, so a level the model rejects reaches the API and
            > fails. Earlier versions of this skill hardcoded
            > `["minimal", "low", "medium", "high"]` for every Gemini model; that was wrong and
            > shipped a broken `gemini-3.7-flash` entry. **Do not restore the constant** — and if a
            > model's page cannot be reached, omit the level you cannot confirm rather than
            > including it.

        *   `contextWindowSize`: Copy from the source `"inputTokenLimit"`.
        *   `maxOutputTokens`: Copy from the source `"outputTokenLimit"`.
        *   `supportsSubAgentCoordination` / `supportsSubAgentExecution`: both default to `true`, so
            **omit them for capable models**. Add both as `false` only for the cheap tier — in
            practice the `-flash-lite` models, which is what the existing catalog does. A
            `-flash-lite` model in the dump gets both flags; a plain `-flash` or `-pro` model gets
            neither.

4.  **Insertion & Save**:
    *   Append the newly constructed model objects to the **end** (bottom) of the JSON array in `GoogleModelCatalog.json`.
    *   Save the updated `GoogleModelCatalog.json`. Do **NOT** modify or delete the user's input file.

## Verification

Every one of these steps is mandatory; the first two fail silently if skipped.

1.  **Parse the file.** A malformed catalog does not fail loudly — `LoadCatalogs` deserializes it
    to `null` and skips the whole provider, so *every* Gemini model vanishes from the picker.

    ```powershell
    Get-Content Overseer\Services\ModelCatalogs\GoogleModelCatalog.json -Raw | ConvertFrom-Json | Out-Null
    ```

2.  **Rebuild.** The catalogs are **embedded resources**
    (`<EmbeddedResource Include="Services\ModelCatalogs\*.json" />` in `Overseer/Overseer.csproj`),
    read through `Assembly.GetManifestResourceStream`. Until the project is rebuilt and the service
    restarted, the edit has no runtime effect whatsoever.

    ```powershell
    dotnet build Overseer\Overseer.csproj
    dotnet test Overseer.Tests\Overseer.Tests.csproj
    ```

3.  **Check the picker.** Open the **Models** page, choose Google, and confirm each added model
    shows its intended display name, its context window and max output, and **exactly** the
    thinking levels its documentation lists — no more. Confirm the previous version still appears
    separately under its own name.

4.  **Preserve encoding.** `GoogleModelCatalog.json` is UTF-8 **without BOM** with **LF** line
    endings. Verify by counting CR and LF bytes, never with `grep` or `file`:

    ```powershell
    $b=[System.IO.File]::ReadAllBytes('Overseer\Services\ModelCatalogs\GoogleModelCatalog.json'); $cr=0;$lf=0; foreach($x in $b){ if($x -eq 13){$cr++}; if($x -eq 10){$lf++} }; "bytes=$($b.Length) CR=$cr LF=$lf"
    ```

    Expect `CR=0` and a non-zero `LF`.

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

`thinking: true` in the dump only says the model reasons. The levels come from
`https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash`, which lists `low`, `medium` and
`high` — and states that `minimal` returns an error. That is why the array below is not the
four-value constant an earlier version of this skill prescribed.

```json
{
  "prefixes": ["gemini-3.7-flash"],
  "displayName": "Gemini 3.7 Flash",
  "releaseDate": "2026-08-15",
  "thinkingLevels": ["low", "medium", "high"],
  "contextWindowSize": 1048576,
  "maxOutputTokens": 65536
}
```
