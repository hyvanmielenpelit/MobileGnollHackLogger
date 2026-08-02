# Plan A: Source Code Access for the Overseer

## Background

The Gnoll Overseer currently has access to the **GnollHack Wiki** and the **NetHack Wiki** for answering player questions. However, when a question involves undocumented mechanics, potential bugs, or edge-case behavior, the authoritative source is the **C source code** itself.

This plan adds two new tools (`source_code_search` and `source_code_view`) that let the LLM grep through and read relevant source files from a local git clone of the public GnollHack repository.

> [!NOTE]
> Spoiler-free mode behaviour for these tools is defined in the separate **Plan B: Spoiler Management System** plan.

---

## Resolved Design Decisions

| Decision | Resolution |
|----------|-----------|
| **Server path** | `c:\gnollhack-repository` (configurable via `SourceCodePath`) |
| **Repo visibility** | Public (`https://github.com/hyvanmielenpelit/GnollHack.git`) — no auth needed |
| **Branch** | `master` — always the newest source code |
| **Clone depth** | Shallow clone (`--depth 1`) — only latest source, no git history |
| **Index scope — normal modes** | `src/`, `include/`, `dat/` (gameplay-relevant C code, headers, level descriptions, and text databases) |
| **Index scope — Debug Mode (mode 2)** | Also indexes `win/win32/xpl/` (.NET MAUI frontend, native bridge, C# code) |
| **Token cost** | Max result length is configurable via `IConfiguration` (in-code default, overridable by env vars or `appsettings.Development.json`) |
| **Pull mechanism** | External scheduled service, similar to the existing wiki pull |

---

## Proposed Changes

### Component 1: Source Code Repository Service

#### [NEW] [SourceCodeService.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/SourceCodeService.cs)

A singleton service that:
- Constructor takes `IConfiguration configuration` (matching the `WikiService` pattern)
- Reads `SourceCodePath` from configuration (default: `c:\gnollhack-repository`) — note: follows the same pattern as `WikiPath` and `DumpLogPath`, which use in-code defaults and are **not** in `appsettings.json`
- Reads `MaxSourceFileSizeKB` from configuration (default: `800`)
- On startup, indexes the directory tree of `src/`, `include/`, and `dat/` subdirectories
- Accepts an `includeNetCode` flag (set when Debug Mode is active) to additionally index `win/win32/xpl/` (`.cs`, `.xaml` files)
- Provides two search methods:
  - `SearchFiles(string query, string? fileFilter, int maxResults, bool includeNetCode)` — keyword search across file contents with surrounding line context (±5 lines around each match)
  - `GetFileExcerpt(string filePath, int startLine, int lineCount = 50)` — returns a specific range of lines from a file
- Filters to `.c`, `.h`, `.des`, `.txt` in normal mode; also `.cs`, `.xaml` in Debug Mode (`.txt` files in `dat/` contain important game data: text databases, quest dialogues, rumors, etc.)
- Excludes auto-generated files (`vis_tab.c`, `vis_tab.h`, `onames.h`, `pm.h`, `date.h`, `animoff.h`, `animtotals.h`)
- Excludes platform-specific headers (`*conf.h`, `win*.h`, `mac*.h`, `qt*.h`, etc.)
- Case-insensitive search by default
- Multiple match consolidation: groups nearby matches, limits to 3 match groups per file
- When results are truncated, appends `[N additional matches not shown — refine your query or use source_code_view]` so the LLM knows there is more
- **Lazy re-indexing**: implement as `IHostedService` with a `Timer` that checks if `.git/refs/heads/master` has changed every 10 minutes. When a change is detected, re-scans the directory tree without requiring a server restart. Note: `WikiService` does not re-index — this is a new pattern

---

### Component 2: Source Code Search Tool

#### [NEW] [SourceCodeSearchTool.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/Tools/SourceCodeSearchTool.cs)

Implements `IToolHandler` with:
- **Constructor**: takes `SourceCodeService` and `IConfiguration` (reads `MaxSourceResultLength` from config, default `3000`)
- **Tool name**: `source_code_search`
- **Category**: `ToolCategory.InformationRetrieval`
- **Execution location**: `ToolExecutionLocation.Server`
- **Parameters**:
  - `query` (required, string) — the search term
  - `file_filter` (optional, string) — restrict to a specific file
  - `max_results` (optional, integer, default 5)
- Checks `context.OverseerMode == 2` to decide `includeNetCode`
- Formats results as: `--- filename:L42 ---\n<context lines>`
- Respects configurable max result length
- **Spoiler-free mode**: when `context.SpoilerFreeMode` is true, appends the spoiler-free reminder (same pattern as `WikiSearchTool` and `MonsterLookupTool`)

---

### Component 3: Source Code File View Tool

#### [NEW] [SourceCodeViewTool.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/Tools/SourceCodeViewTool.cs)

Implements `IToolHandler` with:
- **Constructor**: takes `SourceCodeService`
- **Tool name**: `source_code_view`
- **Category**: `ToolCategory.InformationRetrieval`
- **Execution location**: `ToolExecutionLocation.Server`
- **Parameters**:
  - `file` (required, string) — path relative to repo root
  - `start_line` (required, integer)
  - `line_count` (optional, integer, default 50, max 100)
- Checks `context.OverseerMode == 2` to decide whether `.cs`/`.xaml` files are accessible
- **Path traversal validation**: normalize the requested path with `Path.GetFullPath`, then verify it starts with the configured `SourceCodePath` and ends with an allowed extension. Reject paths containing `..` segments or symbolic links outside the repo
- **Spoiler-free mode**: when `context.SpoilerFreeMode` is true, appends the spoiler-free reminder

---

### Component 4: Tool Guide Files

> [!NOTE]
> Tool guide files are plain text `.md` files (no wrapping code fences). The `ToolGuides\**` glob in `Overseer.csproj` already copies them to the output directory via `<Content Include="ToolGuides\**" CopyToOutputDirectory="PreserveNewest" />`, so new files are picked up automatically.

#### [NEW] [source_code_search.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/source_code_search.md)

```
Search the GnollHack C source code for functions, macros, constants, or game mechanic implementations.
Use this tool to verify undocumented game mechanics, check exact formulas or probabilities,
investigate potential bugs, or find how specific features are implemented in the codebase.

The codebase is organized as:
- src/*.c — C source files (game logic, combat, spells, items, monsters, dungeon generation, etc.)
- include/*.h — Header files (data structures, macros, constants, monster/object definitions)
- dat/*.des — Level description files (special level layouts)
- dat/*.txt — Text databases (quest dialogues, rumors, encyclopedia entries)
- win/win32/xpl/ — .NET MAUI frontend (C#/XAML) — only available in Debug Mode

Key files for common mechanic lookups:
- src/potion.c, src/read.c, src/zap.c — Item usage (potions, scrolls, wands)
- src/uhitm.c, src/mhitu.c, src/mhitm.c — Combat (player-vs-monster, monster-vs-player, monster-vs-monster)
- src/mon.c, src/mondata.c, src/makemon.c — Monster behavior, data, creation
- src/spell.c — Spellcasting mechanics
- src/artifact.c — Artifact properties and effects
- src/trap.c — Trap mechanics
- src/pray.c — Prayer mechanics
- src/eat.c — Eating/nutrition
- src/weapon.c — Weapon skills and damage
- src/shk.c — Shopkeeper interactions
- src/fountain.c — Fountain effects
- include/monst.h, include/obj.h — Core data structures
- include/mondata.h — Monster property macros (resistances, flags)
- include/youprop.h — Player property macros
- src/objects.c — Object definitions (all items with stats)
- src/monst.c — Monster definitions (all monsters with stats)

Search tips:
- Search for function names (e.g., "potionhit", "hitmu", "rn2")
- Search for constants (e.g., "PM_GNOLL", "SPE_FIREBALL", "EXPL_FIERY")
- Search for game messages to find the code that produces them (e.g., "You feel a numbness")
- Use file_filter to narrow results to a specific file when you know where to look

After finding relevant code, use source_code_view to see more context around the match.
```

#### [NEW] [source_code_view.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/source_code_view.md)

```
View a section of a GnollHack source code file by line range.
Use this after source_code_search to see more context around a match,
or when you already know which file and approximate location to examine.
Specify the file path relative to the repository root (e.g., "src/potion.c").
```

---

### Component 5: Registration & Configuration

#### [MODIFY] [Program.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Program.cs)

Add `SourceCodeService` singleton after the existing `WikiService` registration (line 66), and register the two tool handlers after the existing tool handler block (after line 93):

```diff
 builder.Services.AddSingleton<WikiService>();
+builder.Services.AddSingleton<SourceCodeService>();
 ...
 builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.SearchServerDumplogsTool>();
+builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.SourceCodeSearchTool>();
+builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, Overseer.Services.Tools.SourceCodeViewTool>();
```

If the `SourceCodeService` implements `IHostedService` for lazy re-indexing, also register:

```diff
+builder.Services.AddHostedService(sp => sp.GetRequiredService<SourceCodeService>());
```

#### Configuration keys

> [!IMPORTANT]
> The existing `WikiPath` and `DumpLogPath` config keys are **not** in `appsettings.json` — they are read from `IConfiguration` with in-code defaults (set via environment variables or `appsettings.Development.json` on the server). Follow the same pattern: read these keys from `IConfiguration` in the service constructor with defaults, and do **not** add them to `appsettings.json` (to avoid committing server-specific paths to the repository).

Config keys to read (with in-code defaults):
- `SourceCodePath` — default: `c:\gnollhack-repository`
- `MaxSourceSearchResults` — default: `5`
- `MaxSourceFileSizeKB` — default: `800`
- `MaxSourceResultLength` — default: `3000`

---

### Component 6: Tool Policy & System Prompt Updates

#### [MODIFY] [_policy.md](file:///c:/hmp/MobileGnollHackLogger/Overseer/ToolGuides/_policy.md)

```diff
+- When answering questions about specific game mechanics, probabilities, or formulas,
+  use source_code_search to verify the exact implementation before stating numbers.
+- Prefer wiki_search for general information and source_code_search for precise mechanics.
+- When citing source code, always mention the file name and approximate line number.
+- Use source_code_view to get more context when a source_code_search result is incomplete.
```

#### [MODIFY] [ChatService.cs](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/ChatService.cs)

Add a new section between Section 6 ("Available Context", ending around line 1345) and Section 7 ("Session Context", line 1350). The section should be conditioned on `enableToolUse`:

```csharp
// ──────────────────────────────────────────────
// SECTION 6b: Source Code Access
// ──────────────────────────────────────────────
if (enableToolUse)
{
    sb.AppendLine("## Source Code Access");
    sb.AppendLine("You have access to the GnollHack C source code via the source_code_search and source_code_view tools.");
    sb.AppendLine("Use these tools to verify undocumented mechanics, check exact formulas or probabilities, and investigate potential bugs.");
    sb.AppendLine("When a player asks about a specific mechanic that is not covered in the wiki, search the source code to find the authoritative answer.");
    sb.AppendLine("When citing source code findings, mention the file and line number, and translate the C code into player-friendly language.");
    if (overseerMode == 2)
        sb.AppendLine("In Debug Mode, you also have access to the .NET MAUI frontend code (C#/XAML) under win/win32/xpl/.");
    sb.AppendLine();
}
```

Update Section 11 ("Important Rules", line 1396) — add after the existing wiki authority line:

```diff
 sb.AppendLine("- The GnollHack Wiki at wiki.gnollhack.com is the authoritative source for GnollHack-specific information.");
+sb.AppendLine("- The GnollHack source code (accessible via source_code_search) is the definitive authority for exact mechanics, formulas, and probabilities. Prefer source code over wiki when they disagree.");
 sb.AppendLine("- For inherited NetHack mechanics not yet documented on the GnollHack Wiki, the NetHack Wiki (nethackwiki.com) can be referenced as a secondary source, but always caveat that mechanics may differ.");
```

---

### Component 7: Git Pull — External Scheduled Service

> [!NOTE]
> The source code repository is kept fresh by an external scheduled service, following the same pattern as the existing wiki pull service.

Reference script:

```powershell
# pull-gnollhack-repository.ps1
$repoPath = "c:\gnollhack-repository"
$repoUrl = "https://github.com/hyvanmielenpelit/GnollHack.git"
if (-not (Test-Path $repoPath)) {
    git clone --depth 1 --branch master $repoUrl $repoPath
} else {
    Set-Location $repoPath
    # Use fetch+reset instead of pull — safer for shallow clones
    git fetch --depth 1 origin master
    git reset --hard origin/master
}
```

---

## Summary of All Files

| # | File | Action | Description |
|---|------|--------|-------------|
| 1 | `Overseer/Services/SourceCodeService.cs` | **NEW** | Core service: indexes and searches source files (also `IHostedService` for re-indexing) |
| 2 | `Overseer/Services/Tools/SourceCodeSearchTool.cs` | **NEW** | LLM tool: grep-like search with spoiler-free support |
| 3 | `Overseer/Services/Tools/SourceCodeViewTool.cs` | **NEW** | LLM tool: view file line ranges with path traversal protection |
| 4 | `Overseer/ToolGuides/source_code_search.md` | **NEW** | Tool description with file map |
| 5 | `Overseer/ToolGuides/source_code_view.md` | **NEW** | Tool description for file viewer |
| 6 | `Overseer/Program.cs` | **MODIFY** | Register new services, tool handlers, and hosted service |
| 7 | `Overseer/ToolGuides/_policy.md` | **MODIFY** | Add source code policy |
| 8 | `Overseer/Services/ChatService.cs` | **MODIFY** | System prompt: source code access section + authority update |
| 9 | Pull script (external) | **NEW** | Reference script for scheduled pull |

---

## Subagent Use

- **Subagents**: Not needed — single agent can handle all changes
- **Model**: Inherit (current model)
- **User tasks**: Set up git clone on server, configure `SourceCodePath`, add pull script to scheduler

---

## Verification Plan

### Automated Tests
```bash
dotnet build Overseer/Overseer.csproj
```

### Manual Verification
- Point `SourceCodePath` to `c:\hmp\GnollHack`, start Overseer dev server
- Test: "How does the rn2 function work?" → should invoke `source_code_search` and find `rnd.c`
- Test `source_code_view` follow-up
- Test Debug Mode → `.NET` code becomes searchable
- Test nonexistent search term → graceful "no results"
- Test `file_filter` parameter
