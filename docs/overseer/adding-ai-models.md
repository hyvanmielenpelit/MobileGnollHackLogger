# Adding AI Models to Overseer

This guide provides instructions on how to add new AI models from various providers to the Overseer project.

## Google Gemini

To add new Gemini models, we utilize an Antigravity AI skill to automate the extraction and catalog updates. Follow these steps:

### 1. Get the Current Models List

First, fetch the list of available models from the Google Generative Language API.

You can use a REST client (like RestMan) or `curl`:

```bash
curl -o models.json "https://generativelanguage.googleapis.com/v1beta/models?key=YOUR_API_KEY"
```

*(Ensure you replace `YOUR_API_KEY` with a valid Google AI Studio API key.)*

### 2. Prepare the New Models

Review the generated `models.json` file and identify the new models you wish to add. 

Create a file named `new-models.json` in `C:\hmp\plans\` and copy the relevant model objects into it as a JSON array. 

**Example format (`C:\hmp\plans\new-models.json`):**

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

Once `new-models.json` is ready, run the following prompt in Google Antigravity:

> "In the MobileGnollHackLogger repository, add new Gemini models using the `adding_gemini_models` skill."

Antigravity will automatically:
- Read your `new-models.json` file.
- Check for duplicates against the existing catalog.
- Map the required fields (like `contextWindowSize` and `thinkingLevels`).
- Append the new models to the `GoogleModelCatalog.json` catalog while maintaining the correct sorting.

---

## Anthropic / Claude

*(Instructions for adding new Anthropic/Claude models will be added here in the future.)*

---

## OpenAI

*(Instructions for adding new OpenAI models will be added here in the future.)*
