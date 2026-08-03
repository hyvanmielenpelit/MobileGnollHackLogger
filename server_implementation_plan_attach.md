# Overseer Server: Add Log Tools & Dumplog max_length Parameter

## Context

The GnollHack client is removing its manual "Attach" button UI (dumplog, screenshot, app log, panic log uploads via the defunct `/api/session/attach` endpoint). Instead, the Overseer AI will access this data on-demand via client tools through the existing JS bridge → SignalR pipeline.

Two new **client-side tools** are needed: `get_app_log` and `get_panic_log`. The actual tool execution happens on the client device (the server only needs the tool handler definition and tool guide). Additionally, the existing `get_player_dumplogs` tool needs a `max_length` parameter so the AI can control content truncation.

**Screenshots** are already fully handled by the existing `POST /api/chat/send` attachment pipeline — no changes needed.

---

## Changes Required

### 1. Add `GetAppLogTool` to ClientToolHandlers.cs

**File:** [`Overseer/Services/Tools/ClientToolHandlers.cs`](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/Tools/ClientToolHandlers.cs)

Add a new class following the existing pattern (e.g., `GetPlayerDumplogsTool`):

```csharp
public class GetAppLogTool : ClientToolHandlerBase
{
    public override string ToolName => "get_app_log";
    public override ToolCategory Category => ToolCategory.ClientPersistentDataQuery;

    public override JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""last_n"": { ""type"": ""integer"", ""description"": ""Return only the last N lines of the log. If omitted, returns the entire log (subject to server-side truncation)."" },
            ""search_term"": { ""type"": ""string"", ""description"": ""Optional substring to filter log lines (case-insensitive). Only lines containing this term are returned."" }
        }
    }").RootElement;
}
```

**Behavior notes for the client implementation (FYI only):**
- Reads `GHApp.GHPath/log/ghlog.txt` on the device
- The client will implement `last_n` filtering (tail of file) and `search_term` filtering (case-insensitive substring match on each line)
- Server-side `ToolExecutor` truncation at `MaxResultLength` provides the safety net for very large logs

---

### 2. Add `GetPanicLogTool` to ClientToolHandlers.cs

**File:** [`Overseer/Services/Tools/ClientToolHandlers.cs`](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/Tools/ClientToolHandlers.cs)

```csharp
public class GetPanicLogTool : ClientToolHandlerBase
{
    public override string ToolName => "get_panic_log";
    public override ToolCategory Category => ToolCategory.ClientPersistentDataQuery;
}
```

No parameters needed — panic logs are short (usually just a few lines per crash event). The default empty `ParameterSchema` from `ClientToolHandlerBase` is fine.

**Behavior notes for the client implementation (FYI only):**
- Reads `GHApp.GHPath/paniclog` on the device
- Returns the full file content, or a "No panic log found" message if the file doesn't exist
- The C core engine writes to this file on panic/crash events

---

### 3. Add `max_length` parameter to `GetPlayerDumplogsTool`

**File:** [`Overseer/Services/Tools/ClientToolHandlers.cs`](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/Tools/ClientToolHandlers.cs)

The existing `GetPlayerDumplogsTool` currently has only a `filename` parameter. Add `max_length`:

```csharp
public class GetPlayerDumplogsTool : ClientToolHandlerBase
{
    public override string ToolName => "get_player_dumplogs";
    public override ToolCategory Category => ToolCategory.ClientPersistentDataQuery;

    public override JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""filename"": { ""type"": ""string"", ""description"": ""Filename of a specific dumplog to read (e.g. gnollhack.Gandalf.20260801100000.txt). Get filenames from the list mode or from get_player_xlog's dumplog_filename field. If omitted, returns a list of all existing dumplog files on the device."" },
            ""max_length"": { ""type"": ""integer"", ""description"": ""Maximum number of characters to return from the dumplog content. Defaults to 4000. Use a higher value (e.g. 16000) to get more complete dumplogs when needed."" }
        }
    }").RootElement;
}
```

**Client-side behavior:** The client currently hardcodes `MaxDumplogChars = 4000`. It will read `max_length` from the tool parameters and use it instead of the hardcoded default. The server-side `ToolExecutor` truncation at `MaxResultLength` still provides the upper bound.

---

### 4. Register New Tools in Program.cs

**File:** [`Overseer/Program.cs`](file:///c:/hmp/MobileGnollHackLogger/Overseer/Program.cs)

Tool handlers are **manually registered** as singletons (there is no reflection-based auto-discovery). Add the two new registrations alongside the existing tool registrations (after the `GetPlayerDumplogsTool` line):

```csharp
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetAppLogTool>();
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetPanicLogTool>();
```

---

### 5. Add Tool Display Names in chat.component.ts

**File:** [`Overseer/ClientApp/src/app/chat/chat.component.ts`](file:///c:/hmp/MobileGnollHackLogger/Overseer/ClientApp/src/app/chat/chat.component.ts)

The Angular `ChatComponent` has a `TOOL_DISPLAY_NAMES` map that provides human-readable status text shown to the user while a tool is executing. Add entries for the two new tools:

```typescript
'get_app_log': 'Reading application log',
'get_panic_log': 'Reading panic log',
```

---

### 6. Add Tool Guide: `get_app_log.md`

**File:** [`Overseer/ToolGuides/get_app_log.md`](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/get_app_log.md)

```markdown
Reads the GnollHack application log (ghlog.txt) from the player's device.

Contains timestamped entries for app startup, connection events, UI actions, errors, and debug information. Useful for diagnosing connection failures, UI issues, performance problems, and unexpected app behavior.

Use `last_n` to retrieve only the most recent log entries (e.g. `last_n: 100` for the last 100 lines). Use `search_term` to filter for specific events (e.g. `search_term: "Overseer"` to find Overseer-related log entries).

The log file may be large. If you only need recent events, always specify `last_n` to avoid unnecessarily large responses.
```

---

### 7. Add Tool Guide: `get_panic_log.md`

**File:** [`Overseer/ToolGuides/get_panic_log.md`](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/get_panic_log.md)

```markdown
Reads the GnollHack panic log (paniclog) from the player's device.

Contains entries written by the C game engine when a fatal error (panic) occurs. Each entry includes a timestamp and the panic message describing the crash cause.

The file may not exist if no panics have occurred — in that case the tool returns a message indicating no panic log was found. Panic logs are typically short.
```

---

### 8. Update Tool Guide: `get_player_dumplogs.md`

**File:** [`Overseer/ToolGuides/get_player_dumplogs.md`](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/get_player_dumplogs.md)

Add a note about `max_length` to the existing guide:

```markdown
(add to existing content)

When reading a specific dumplog, content is truncated to `max_length` characters (default 4000). If you need more complete dumplog content (e.g. for detailed post-mortem analysis), pass a higher `max_length` value such as 16000.
```

---

## What the Client Side Will Do (FYI — no server action needed)

For reference, the client-side changes being done in parallel:

1. Add `"get_app_log"` and `"get_panic_log"` to the `AllowedClientTools` set
2. Implement the tool execution logic in `DispatchToolCallAsync`
3. Read `max_length` parameter in `get_player_dumplogs` handler
4. Remove the entire Attach button/grid UI (dumplogs, screenshots, app log, panic log attach buttons)
5. Regenerate MAUI XAML via `makedefsdroid`

The client changes depend on the server having the tool definitions registered, so please deploy the server changes first or coordinate timing.
