# Overseer Server Update Request

The GnollHack MAUI client has been updated to standardize the environment, settings, and debug data sent to the Gnoll Overseer. Please implement the corresponding changes on the ASP.NET Core server (`MobileGnollHackLogger`).

## 1. Unified `EnvironmentData` Payload
Previously, the client sent `OverseerSettings` (a small JSON with `boolSettings` and `intSettings`) and a separate `DebugData` JSON string.

We have now consolidated all this information into a single unified JSON payload that is sent via the `OverseerSettings` form field. This payload now contains comprehensive device information, memory stats, and game settings, organized into strongly typed dictionaries. 

The client serializes the `OverseerEnvironmentData` class using `Newtonsoft.Json.JsonConvert.SerializeObject()` with **default settings** (no `CamelCasePropertyNamesContractResolver`), so the top-level JSON property names are **PascalCase**:

```json
{
  "BoolData": { "allowSpoilers": true, "isGameOn": false, "DeveloperMode": true, "DebugLogMessages": false, ... },
  "IntData": { "overseerMode": 0, "GPUCacheUsageResources": 150, ... },
  "LongData": { "TotalMemoryMB": 8192, "UsedMemoryMB": 2048, "TotalPlayTimeSeconds": 3600, ... },
  "DoubleData": { },
  "StringData": { "Platform": "Windows", "OSVersion": "10.0.19045", "GHVersion": "4.2.0", "GPUBackend": "Direct3D11", ... }
}
```

> **⚠️ Mixed Dictionary Key Casing**: The dictionary keys within each section use **mixed casing** because they are populated in two places:
> - Keys added by `GHApp.GetEnvironmentData()` use **PascalCase**: `"DeveloperMode"`, `"DebugLogMessages"`, `"LowLevelLogging"`, `"ScreenLogging"`, `"IsBeta"`, `"IsPlaytest"`, `"GPUCacheUsageResources"`, `"PendingResponses"`, `"TotalMemoryMB"`, etc.
> - Keys added by `OverseerPage.xaml.cs` at session creation time use **camelCase**: `"allowSpoilers"`, `"verboseResponses"`, `"sendGameContext"`, `"isGameOn"`, `"overseerMode"`.
>
> The server parsing code must use the **exact key strings** as listed above.

## 2. API Contract Changes (`SessionController.cs`)
- **Remove `DebugData`**: The `DebugData` field is no longer sent by the client. You can safely remove `public string? DebugData { get; set; }` from `CreateSessionRequest`.
- **Remove DebugData file writing**: In `SessionController.Create()`, remove the entire `if (!string.IsNullOrWhiteSpace(request.DebugData))` block (lines 143–165) that writes `debug_data.json` to disk and adds it as a system message with a `ChatMessageAttachment`. All relevant diagnostic data is now included in `OverseerSettings`.

## 3. Context Injection (`ChatService.cs`)

### 3a. Update Property Extraction in `StreamMessageAsync`
The existing JSON property extraction (lines 181–203) currently looks for `"boolSettings"` and `"intSettings"`. These must be updated to match the new payload:

| Old Lookup | New Lookup | Notes |
|---|---|---|
| `root.TryGetProperty("boolSettings", ...)` | `root.TryGetProperty("BoolData", ...)` | PascalCase (C# property name) |
| `root.TryGetProperty("intSettings", ...)` | `root.TryGetProperty("IntData", ...)` | PascalCase (C# property name) |
| `boolSettings.TryGetProperty("developerMode", ...)` | `boolData.TryGetProperty("DeveloperMode", ...)` | Key changed to PascalCase |
| `boolSettings.TryGetProperty("debugLogMessages", ...)` | `boolData.TryGetProperty("DebugLogMessages", ...)` | Key changed to PascalCase |
| `boolSettings.TryGetProperty("allowSpoilers", ...)` | `boolData.TryGetProperty("allowSpoilers", ...)` | Key is still camelCase |
| `boolSettings.TryGetProperty("verboseResponses", ...)` | `boolData.TryGetProperty("verboseResponses", ...)` | Key is still camelCase |
| `boolSettings.TryGetProperty("isGameOn", ...)` | `boolData.TryGetProperty("isGameOn", ...)` | Key is still camelCase |
| `intSettings.TryGetProperty("overseerMode", ...)` | `intData.TryGetProperty("overseerMode", ...)` | Key is still camelCase |

### 3b. Remove `hasDebugData`
Since `DebugData` is no longer sent as a separate system message, the `hasDebugData` boolean (declared at line 91 and detected at line 144 by matching `"Developer Debug Data:"` prefixed system messages) will always be `false` for new sessions.

- Remove `bool hasDebugData = false;` from `StreamMessageAsync` (line 91).
- Remove the detection line: `if (pm.Content.StartsWith("Developer Debug Data:")) hasDebugData = true;` (line 144).
- Remove `hasDebugData` from the `BuildSystemPrompt()` call (line 214) and from the method signature (line 796).
- In `BuildSystemPrompt`, replace the three places that check `hasDebugData` with checks against `developerMode` instead:
  - **Line 838** (`case 2` — Developer mode message): Replace `if (hasDebugData)` with `if (developerMode)`. Since mode 2 already requires `developerMode && debugLogMessages`, this is equivalent.
  - **Line 909** (Capabilities section): Replace `if (hasDebugData)` with `if (developerMode)` or `if (overseerMode == 2)`.
  - **Line 922** (Available Context section): Replace `if (hasDebugData)` with `if (developerMode)`, and update the message to note that environment/debug data is included via `ClientSettings` rather than as a separate system message.
  - **Line 923** (no-context fallback condition): Remove `&& !hasDebugData` from the compound condition; replace with `&& !developerMode` or equivalent.

### 3c. Inject `ClientSettings` into the System Prompt
The LLM currently has no visibility into the full environment data. Update `BuildSystemPrompt()` to accept `string? clientSettings` as a new parameter and append a formatted summary of the unified payload.

- In `StreamMessageAsync`, pass `session?.ClientSettings` when calling `BuildSystemPrompt()`.
- In `BuildSystemPrompt()`, parse `clientSettings` with `JsonDocument.Parse()` and iterate through the `BoolData`, `IntData`, `LongData`, `DoubleData`, and `StringData` dictionaries. Append their key-value pairs as a formatted block (e.g., `## Client Environment`) at the end of the system prompt, before the wiki context section, so the AI is aware of the user's platform, app version, memory constraints, and runtime flags.

## 4. Prompt Update for Technical Assistant Mode
Currently, the Technical Assistant mode (`overseerMode == 1`) focuses strictly on app problems (installation, save files, etc.). The community wants this mode to also focus on **game mechanics**.

In `ChatService.BuildSystemPrompt()` under `case 1: // Technical Help`:
- **Current**: `"Focus on actionable troubleshooting steps rather than gameplay advice."`
- **Requested Change**: Modify the prompt to instruct the AI to focus on **game mechanics, strategy, and game-related troubleshooting** in addition to app problems. The AI should feel empowered to explain complex NetHack/GnollHack mechanics (e.g., armor class, spellcasting penalties, weapon skills) when the user asks for technical help regarding how the game works.

---
Thank you!
