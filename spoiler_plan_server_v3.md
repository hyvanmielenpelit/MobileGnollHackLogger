# Plan B (Server): Spoiler Management — Overseer Server Changes

## Background

The Overseer currently has a basic spoiler-free mode that truncates wiki/lookup results (500 chars for wiki search, 250 chars with newline-seeking for item/monster lookups). This plan replaces it with a **nuanced, context-aware spoiler management system** where the LLM exercises judgement guided by a detailed policy.

> **Core Principle**: Spoilers are about UNREVEALED CONTENT, not about MECHANICS.
> Explaining *how* something works = not a spoiler. Revealing *what* lies ahead = spoiler.

> [!IMPORTANT]
> This plan covers only the **Overseer server** changes. The client-side MAUI handlers for `get_player_library`, `get_oracle_consultations`, and `get_player_dumplogs` are in a separate companion plan.

---

## Component 1: Spoiler Policy Documents

The policy is delivered via a **two-tier** approach:
1. **Short summary in `_policy.md`** (always loaded via `ToolRegistry`): brief reminder, low token cost.
2. **Full policy in `spoiler_policy.md`** (cached by `ToolRegistry`, injected only when `spoilerFreeMode` is active).

#### [MODIFY] [_policy.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/_policy.md)

Replace the outdated spoiler-free mode bullet and append the new section:

```diff
 - Do NOT use tools for information already in your context (game snapshot,
   recent messages in the snapshot, wiki articles already provided).
-- When spoiler-free mode is active, tools automatically return limited information.
-  Do not try to work around this.
+- When spoiler-free mode is active, tools return full information but you MUST filter it according to the spoiler policy.
 - Briefly tell the player what you're looking up when using a tool.
 - If a tool returns no results, say so honestly — do not fabricate information.
+
+## Spoiler-Free Mode
+- When spoiler-free mode is active, explaining HOW mechanics work is always safe.
+- Revealing WHAT the player has not yet encountered is a spoiler.
+- Use get_player_library and get_oracle_consultations to check what the player already knows.
+- Do NOT scan dumplogs for spoiler checking. The full spoiler policy is provided in the system prompt when this mode is active.
```

#### [NEW] [spoiler_policy.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/spoiler_policy.md)

Cached by `ToolRegistry` at startup, exposed via `GetSpoilerPolicyText()`. Full content:

```markdown
# Spoiler-Free Mode: Detailed Policy

When spoiler-free mode is active, you must carefully evaluate every piece of information
before sharing it with the player. The fundamental distinction is:

- **NOT a spoiler**: Explaining HOW game mechanics work — formulas, probabilities,
  damage calculations, skill effects, item properties the player already knows about.
- **IS a spoiler**: Revealing WHAT the player has not yet encountered or discovered —
  future dungeon branches, boss monsters, quest outcomes, hidden levels, undiscovered
  artifact powers, item identities they haven't learned yet.

## Category Reference

### ✅ ALWAYS SAFE (Never a spoiler)

- **Combat formulas**: To-hit calculations, damage dice, AC effects, DR mechanics
- **General mechanics**: How hunger works, how prayer timing works, how skill training works,
  how encumbrance is calculated, how regeneration rates are determined
- **Probability tables**: rn2() outcomes, percentage chances for effects, save thresholds
- **Status effect mechanics**: How poison works, how paralysis duration is calculated,
  how stoning timers function
- **UI and controls**: How to use the interface, keyboard shortcuts, settings explanations
- **Technical issues**: Crashes, bugs, performance problems, installation help
- **Character stats**: What attributes do, how level drain works, how XP calculations work
- **Magic system**: How spell success is calculated, memory retention, energy regeneration
- **Item categories**: General explanations of item types (potions heal, scrolls do things)
- **Visible threats**: Warning about dangers that are currently visible in the game snapshot
- **Game history context**: The player's own current stats, inventory, map — they can already see these

### ⚠️ CONDITIONAL (May or may not be a spoiler — requires checking)

- **Specific item identities**: Is this "milky potion" actually a potion of healing?
  → Check: Has the player identified this potion type? (visible in snapshot discoveries)
  → If identified: safe to discuss. If not: say "try it and see" or give hints.
- **Specific monster abilities**: Does a cockatrice's touch petrify?
  → Check: Has the player encountered this monster? (visible on current map, or mentioned
  in message history, or in dumplog from past games)
  → If encountered: safe to discuss. If not: give vague warnings ("that creature is dangerous").
- **Artifact properties**: What does Excalibur do?
  → Check: Does the player possess or have they previously wielded this artifact?
  → If known: safe. If not: "you'll discover its properties when you find it."
- **Specific level features**: Is there a shop on this level?
  → Check: Is it visible in the current snapshot?
  → If visible: safe. If not: don't reveal.
- **Oracle consultations**: The player's received Delphi consultations are fair game — they
  already received this information in-game. Use get_oracle_consultations to check.
- **Library manuals**: Content from manuals the player has found and read is known to them.
  Use get_player_library to check what they've read.

### 🚫 ALWAYS A SPOILER (Never reveal in spoiler-free mode)

- **Future dungeon branches**: Names, depths, or existence of branches the player hasn't visited
- **Hidden or secret levels**: The existence of levels the player hasn't encountered
- **Boss encounters**: Identity, location, or abilities of unencountered bosses/unique monsters
- **Quest details**: Quest objectives, quest nemesis identity, quest artifact powers (if not yet received)
- **Optimal strategies**: "You should get X artifact, then do Y, then Z" meta-game strategies
- **Ascension kits**: Lists of ideal items/equipment for winning the game
- **Endgame content**: What happens in the endgame, endgame level layouts, final challenges
- **Puzzle solutions**: How to solve specific puzzles the player hasn't attempted
- **Altar/fountain outcomes**: Complete tables of what can happen (give hints instead)
- **Wish lists**: What the "best" wishes are (let the player discover wish mechanics themselves)

## How to Handle Borderline Cases

1. **Check the game snapshot**: If the information is visible on the player's current map,
   in their inventory, or in their recent messages, it is NOT a spoiler.
2. **Check the player's library**: Use `get_player_library` to see what manuals/catalogues the player has read.
3. **Check Oracle consultations**: Use `get_oracle_consultations` to see what hints the player has received.
4. **When still uncertain**: Err on the side of caution. Give vague hints rather than direct answers.

## Dumplogs and Spoiler Checking

Do NOT routinely scan the player's dumplogs for spoiler-checking purposes.
Assume by default that the player has not been exposed to extra game content
through past games. Dumplogs should ONLY be read when the **player explicitly asks**
about a past game. When a dumplog IS read, you may update your understanding
of what the player has seen and adjust spoiler filtering accordingly.

## Debug Mode Exception

When the Overseer is in Debug Mode (mode 2), spoiler-free mode is ALWAYS disabled.
```

---

## Component 2: ToolRegistry Caching

#### [MODIFY] [ToolRegistry.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/Tools/ToolRegistry.cs)

Cache `spoiler_policy.md` alongside `_policy.md`:

```diff
 private readonly string _guidesPath;
 private string _policyText = string.Empty;
+private string _spoilerPolicyText = string.Empty;
 ...
 private void LoadGuides()
 {
     if (!Directory.Exists(_guidesPath))
     {
         return;
     }

     var policyFile = Path.Combine(_guidesPath, "_policy.md");
     if (File.Exists(policyFile))
     {
         _policyText = File.ReadAllText(policyFile);
     }
+
+    var spoilerPolicyFile = Path.Combine(_guidesPath, "spoiler_policy.md");
+    if (File.Exists(spoilerPolicyFile))
+    {
+        _spoilerPolicyText = File.ReadAllText(spoilerPolicyFile);
+    }

     foreach (var handler in _handlers)
     {
         ...
     }
 }

+public string GetSpoilerPolicyText()
+{
+    return _spoilerPolicyText;
+}
```

---

## Component 3: Client Tool Declarations (Server-Side Stubs)

These are the server-side class declarations for the client-executed tools. They define the tool schema that gets sent to the LLM. The actual execution happens on the MAUI client.

#### [MODIFY] [ClientToolHandlers.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/Tools/ClientToolHandlers.cs)

Add four new tool handler classes:

```csharp
public class GetPlayerLibraryTool : ClientToolHandlerBase
{
    public override string ToolName => "get_player_library";

    public override JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""item_id"": { ""type"": ""integer"", ""description"": ""ID of a specific manual to read in full. If omitted, returns a list of all discovered manuals with just their names and IDs (no text content)."" }
        }
    }").RootElement;
}

public class GetOracleConsultationsTool : ClientToolHandlerBase
{
    public override string ToolName => "get_oracle_consultations";

    public override JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""item_id"": { ""type"": ""integer"", ""description"": ""ID of a specific consultation to read in full. If omitted, returns a list of all received consultations with just their names and IDs (no text content)."" }
        }
    }").RootElement;
}

public class GetPlayerXlogTool : ClientToolHandlerBase
{
    public override string ToolName => "get_player_xlog";

    public override JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""limit"": { ""type"": ""integer"", ""description"": ""Maximum number of entries to return. Defaults to 50."" },
            ""offset"": { ""type"": ""integer"", ""description"": ""Number of newest entries to skip. Defaults to 0."" }
        }
    }").RootElement;
}

public class GetPlayerDumplogsTool : ClientToolHandlerBase
{
    public override string ToolName => "get_player_dumplogs";

    public override JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""filename"": { ""type"": ""string"", ""description"": ""Filename of a specific dumplog to read (e.g. gnollhack.Gandalf.20260801100000.txt). Get filenames from the list mode or from get_player_xlog's dumplog_filename field. If omitted, returns a list of all existing dumplog files on the device."" }
        }
    }").RootElement;
}
```

---

## Component 4: Tool Guide Files

#### [NEW] [get_player_library.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/get_player_library.md)

```
Access the player's library of manuals and catalogues discovered in their current game.

This tool has two modes:

1. LIST MODE (no item_id): Returns a lightweight listing of all discovered manuals
   with just their names and IDs. Use this first to see what's available.

2. READ MODE (item_id specified): Returns the full text content of a specific manual.
   Use the ID from list mode to read a particular manual.

In spoiler-free mode, use this tool to check whether the player already knows about
a specific topic before revealing details about it. If a topic is covered in a manual
the player has read, it is safe to discuss freely.

This tool requires an active game with client data enabled.
```

#### [NEW] [get_oracle_consultations.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/get_oracle_consultations.md)

```
Access the Oracle of Delphi major consultations the player has received in their current game.

This tool has two modes:

1. LIST MODE (no item_id): Returns a lightweight listing of all received consultations
   with just their names and IDs. Use this first to see what's available.

2. READ MODE (item_id specified): Returns the full text content of a specific consultation.
   Use the ID from list mode to read a particular consultation.

In spoiler-free mode, use this to verify what hints the Oracle has already given
the player. Information from received consultations is safe to discuss and expand upon.

This tool requires an active game with client data enabled.
```

#### [NEW] [get_player_xlog.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/get_player_xlog.md)

```
Retrieve entries from the player's local game log (xlogfile).
Returns a listing of past games with rich metadata: character name, role,
race, gender, alignment, XP level, HP, game mode, turns, score, outcome,
death date, real time played, and whether a dumplog file exists.

By default, returns the 50 newest games. Use the 'limit' and 'offset'
parameters to paginate through older games if needed.

Each entry includes a dumplog_filename field if a dumplog exists for that game.
Use this filename with get_player_dumplogs to read the full dumplog.

This tool requires client data to be enabled.
```

#### [NEW] [get_player_dumplogs.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/get_player_dumplogs.md)

```
Access dumplog files stored on the player's device.

This tool has two modes:

1. LIST MODE (no filename): Returns a list of all dumplog files that actually
   exist on the device. Each entry includes the filename, linked game info
   (if an xlog entry matches), file size, and format (txt, html, or both).
   Files are deduplicated so .txt/.html pairs for the same game appear as one
   entry. Some dumplogs may be "orphaned" (no matching xlog entry) if the
   xlog file was deleted or corrupted.

2. READ MODE (filename specified): Reads the full text of a specific dumplog.
   Get the filename from list mode or from get_player_xlog's dumplog_filename
   field. HTML files are automatically stripped of tags for readability.

⚠️ DO NOT use this tool for routine spoiler checking. Assume by default that past
games have not revealed spoiler content. Only read a dumplog when the player
explicitly asks about a past game (e.g., "why did I die?", "what happened in my
last run?"). When you do read a dumplog, you may then update your understanding
of what the player has seen and adjust spoiler filtering accordingly.

This tool requires client data to be enabled.
```

---

## Component 5: Server Dumplog Search Tool (Optional, Lower Priority)

#### [NEW] [SearchServerDumplogsTool.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/Tools/SearchServerDumplogsTool.cs)

Implements `IToolHandler` with:
- **Tool name**: `search_server_dumplogs`
- **Category**: `InformationRetrieval`
- **Execution location**: `Server`
- **Parameters**:
  - `search_term` (required, string)
  - `max_results` (optional, integer, default 3, max 5)
- Queries `GameLog` table, reads dumplog files, returns matching excerpts with game metadata.

> [!IMPORTANT]
> Needs read access to the main `MobileGnollHackLogger` database and `DumpLogPath`.

#### [NEW] [search_server_dumplogs.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/search_server_dumplogs.md)

```
Search dumplogs from all players on the GnollHack server for a specific term.
Returns matching excerpts from game summaries, providing general game knowledge.

⚠️ IMPORTANT: These are NOT the current player's dumplogs. They are from all
players who have posted top scores. Use this for general game knowledge (e.g.,
"is it possible to ascend as a gnoll priest?"), NOT for determining what
this specific player has encountered (use get_player_dumplogs for that).

⚠️ USE SPARINGLY — dumplogs are long and there can be many of them.
Only use when you need general game knowledge that isn't available in the wiki.
```

---

## Component 6: System Prompt Enhancement

#### [MODIFY] [ChatService.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/ChatService.cs)

Replace the current SECTION 12 (lines 1398–1413 in current code). The existing block uses `spoilerFreeMode && overseerMode != 2` and contains basic bullet-point spoiler rules. Replace the entire section with the comprehensive version:

```csharp
        // ──────────────────────────────────────────────
        // SECTION 12: Spoiler Control (comprehensive)
        // ──────────────────────────────────────────────
        if (spoilerFreeMode && overseerMode != 2)
        {
            sb.AppendLine();
            sb.AppendLine("## ⚠️ SPOILER-FREE MODE IS ACTIVE");
            sb.AppendLine();
            sb.AppendLine("The player has enabled spoiler-free mode. You must carefully evaluate");
            sb.AppendLine("every piece of information before sharing it.");
            sb.AppendLine();
            sb.AppendLine("### The Core Rule");
            sb.AppendLine("- **NOT a spoiler**: Explaining HOW game mechanics work — formulas, probabilities, damage calculations, skill effects.");
            sb.AppendLine("- **IS a spoiler**: Revealing WHAT the player has not yet encountered — future dungeon branches, unmet bosses, undiscovered item identities.");
            sb.AppendLine();
            sb.AppendLine("### Tools for Spoiler Checking");
            sb.AppendLine("Before revealing conditional information, use these tools to check what the player already knows:");
            sb.AppendLine("- The game snapshot — check what's visible on the current map, in inventory, and in messages");
            sb.AppendLine("- `get_player_library` — check what manuals/catalogues the player has read");
            sb.AppendLine("- `get_oracle_consultations` — check what Oracle hints the player has received");
            sb.AppendLine("Do NOT scan dumplogs for spoiler checking. Only use `get_player_dumplogs` when the player explicitly asks about a past game.");
            sb.AppendLine();
            sb.AppendLine("### Quick Reference");
            sb.AppendLine("✅ SAFE: Combat formulas, probability tables, general mechanics, status effects, UI help, visible threats");
            sb.AppendLine("⚠️ CHECK FIRST: Specific item identities, monster abilities, artifact powers, level features");
            sb.AppendLine("🚫 NEVER: Future branches, hidden levels, boss encounters, quest details, optimal strategies, endgame content");
            sb.AppendLine();

            // Append cached spoiler policy from ToolRegistry (loaded at startup)
            var spoilerPolicy = _toolRegistry.GetSpoilerPolicyText();
            if (!string.IsNullOrWhiteSpace(spoilerPolicy))
            {
                sb.AppendLine("### Detailed Spoiler Policy");
                sb.AppendLine(spoilerPolicy);
            }

            sb.AppendLine("When uncertain, err on the side of caution — give hints rather than direct answers.");
        }
```

---

## Component 7: Existing Tool Improvements

#### [MODIFY] [WikiSearchTool.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/Tools/WikiSearchTool.cs)

Replace the 500-char truncation:

```diff
-        private string ApplySpoilerFreeMode(string content)
-        {
-            if (content.Length > 500)
-            {
-                return content.Substring(0, 500) + "...\n\n[SPOILER FREE MODE: Remainder of wiki article has been redacted.]";
-            }
-            return content;
-        }
+        private string ApplySpoilerFreeMode(string content)
+        {
+            // In spoiler-free mode, return full content but add a reminder.
+            // The LLM's spoiler policy (from spoiler_policy.md) handles what to share.
+            return content + "\n\n[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";
+        }
```

#### [MODIFY] [ItemLookupTool.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/Tools/ItemLookupTool.cs)

Replace the 250-char truncation with newline-seeking:

```diff
-        private string ApplySpoilerFreeMode(string content)
-        {
-            if (content.Length > 250)
-            {
-                var summary = content.Substring(0, 250);
-                var nextNewline = content.IndexOf('\n', 250);
-                if (nextNewline > 250 && nextNewline < 500)
-                {
-                    summary = content.Substring(0, nextNewline);
-                }
-                
-                return summary + "\n\n[SPOILER FREE MODE: Detailed stats, weight, price, and magical properties have been redacted. The player must discover these in-game.]";
-            }
-            return content;
-        }
+        private string ApplySpoilerFreeMode(string content)
+        {
+            return content + "\n\n[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";
+        }
```

#### [MODIFY] [MonsterLookupTool.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/Tools/MonsterLookupTool.cs)

Replace the 250-char truncation with newline-seeking:

```diff
-        private string ApplySpoilerFreeMode(string content)
-        {
-            if (content.Length > 250)
-            {
-                var summary = content.Substring(0, 250);
-                var nextNewline = content.IndexOf('\n', 250);
-                if (nextNewline > 250 && nextNewline < 500)
-                {
-                    summary = content.Substring(0, nextNewline);
-                }
-                
-                return summary + "\n\n[SPOILER FREE MODE: Detailed stats, resistances, and drop tables have been redacted. The player must discover these in-game.]";
-            }
-            return content;
-        }
+        private string ApplySpoilerFreeMode(string content)
+        {
+            return content + "\n\n[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";
+        }
```

> [!NOTE]
> This shifts spoiler filtering from hard content truncation to LLM-level judgement. The LLM reads the full article to reason about it, but only shares safe information.

#### [MODIFY] [ToolRegistry.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/Tools/ToolRegistry.cs)

Update `GetAdjustedDescription` — after removing hard truncation from tools, the annotation claiming "limited information" becomes inaccurate:

```diff
         private string GetAdjustedDescription(IToolHandler handler, ToolExecutionContext context)
         {
             var desc = handler.Description;
             if (context.SpoilerFreeMode)
             {
-                desc += "\n[Spoiler Free Mode Active: This tool will return limited information.]";
+                desc += "\n[Spoiler Free Mode Active: Evaluate returned information against the spoiler policy before sharing with the player.]";
             }
             return desc;
         }
```

---

## Component 8: ToolExecutionContext Assignment

> [!NOTE]
> `OverseerMode` already exists in `ToolExecutionContext` (line 36 of [IToolHandler.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/Tools/IToolHandler.cs)), but is never assigned. `ToolExecutionContext` also contains `UserId` and `DataDirectory` properties that are populated elsewhere. Only this `ChatService.cs` change is needed.

#### [MODIFY] [ChatService.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/ChatService.cs)

At line 406, add `OverseerMode` assignment:

```diff
-var execContext = new ToolExecutionContext { SessionId = currentSessionId, IsGameOn = isGameOn, SpoilerFreeMode = spoilerFreeMode };
+var execContext = new ToolExecutionContext { SessionId = currentSessionId, IsGameOn = isGameOn, SpoilerFreeMode = spoilerFreeMode, OverseerMode = overseerMode };
```

---

## Component 9: Registration

#### [MODIFY] [Program.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Program.cs)

```diff
+builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetPlayerLibraryTool>();
+builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetOracleConsultationsTool>();
+builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetPlayerXlogTool>();
+builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.GetPlayerDumplogsTool>();
+// Optional, lower priority:
+builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.SearchServerDumplogsTool>();
```

---

## Summary of Server Files

| # | File | Action | Description |
|---|------|--------|-------------|
| 1 | `Overseer/ToolGuides/spoiler_policy.md` | **NEW** | Full spoiler classification policy |
| 2 | `Overseer/ToolGuides/_policy.md` | **MODIFY** | Short spoiler summary (always loaded) |
| 3 | `Overseer/Services/Tools/ToolRegistry.cs` | **MODIFY** | Cache spoiler policy, expose `GetSpoilerPolicyText()` |
| 4 | `Overseer/Services/Tools/ClientToolHandlers.cs` | **MODIFY** | Add 4 client tool stubs |
| 5 | `Overseer/ToolGuides/get_player_library.md` | **NEW** | Library tool guide |
| 6 | `Overseer/ToolGuides/get_oracle_consultations.md` | **NEW** | Oracle tool guide |
| 7 | `Overseer/ToolGuides/get_player_xlog.md` | **NEW** | Xlog tool guide |
| 8 | `Overseer/ToolGuides/get_player_dumplogs.md` | **NEW** | Player dumplog tool guide |
| 9 | `Overseer/Services/Tools/SearchServerDumplogsTool.cs` | **NEW** | Server dumplog search (optional) |
| 10 | `Overseer/ToolGuides/search_server_dumplogs.md` | **NEW** | Server dumplog tool guide (optional) |
| 11 | `Overseer/Services/ChatService.cs` | **MODIFY** | Spoiler prompt + cached policy + OverseerMode |
| 12 | `Overseer/Services/Tools/WikiSearchTool.cs` | **MODIFY** | Nuanced spoiler filtering |
| 13 | `Overseer/Services/Tools/ItemLookupTool.cs` | **MODIFY** | Nuanced spoiler filtering |
| 14 | `Overseer/Services/Tools/MonsterLookupTool.cs` | **MODIFY** | Nuanced spoiler filtering |
| 15 | `Overseer/Program.cs` | **MODIFY** | Register new tool handlers |
| 16 | `Overseer/Services/Tools/ToolRegistry.cs` | **MODIFY** | Update `GetAdjustedDescription` spoiler annotation |

## Verification Plan

### Automated
```bash
dotnet build Overseer/Overseer.csproj
```

### Manual
1. Ask "How does prayer work?" in spoiler-free mode → full mechanical explanation (NOT truncated)
2. Ask "What's in the Gnomish Mines?" → vague hints, not layouts
3. Debug mode (mode 2) → spoiler restrictions fully off
4. Wiki/Item/Monster lookups → no hard truncation (was 500-char for wiki, 250-char for item/monster), spoiler reminder annotation appended instead
