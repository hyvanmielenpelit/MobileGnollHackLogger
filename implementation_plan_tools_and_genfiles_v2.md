# Overseer AI Search Tools Review & Generated Files Plan

Comprehensive review of the source code and wiki search tools available to the Overseer AI, assessment of their effectiveness, recommendations for improvements, and a plan for making auto-generated header files (onames.h, pm.h, etc.) available to the AI.

---

## Part 1: Existing Tools Assessment

### 1.1 Source Code Search (`source_code_search`)

**Current capabilities:**
- Case-insensitive substring and regex search across indexed C/H/DES/TXT files
- File path filtering, configurable context lines (0–25), filenames-only mode
- Results ranked by match count, grouped into ≤5 match groups per file
- Good tool guide with file map and search strategy

**Assessment: ⭐⭐⭐⭐ Strong**

This is the most well-designed tool. The parameter set is rich (regex, file filter, filenames-only, context lines), and the tool guide provides excellent guidance on search strategy (discover → survey → locate → deep dive). The main issues are output-related:

> [!WARNING]
> **Two-layer result truncation severely limits output.** `SourceCodeSearchTool` reads `MaxSourceResultLength` from `appsettings.json` (currently 100,000) with a fallback default of 3,000 — so the tool itself does build results up to 100K chars. However, `ToolExecutor` then hard-truncates **all** tool results to 3,000 characters (`private const int MaxResultLength = 3000`), making the generous appsettings value ineffective. A single match with 5 context lines on either side is ~11 lines × ~60 chars = ~660 chars including the header. That means only ~4 match groups fit before the ToolExecutor truncation kicks in. For broad queries like "rn2" or "PM_GNOLL", the AI gets barely a glimpse of the results.

**Recommended improvements:**
1. **Increase default `MaxSourceResultLength`** to 10,000 characters (the `ToolExecutor` truncation at 3,000 is a second layer that also applies). Expose both as configurable settings in the Overseer server's `appsettings.json`.
2. **Add a `case_sensitive` parameter** — some searches (e.g., `PM_GNOLL` vs `pm_gnoll`) benefit from exact case matching. Currently always case-insensitive.
3. **Add a `whole_word` parameter** — searching for `eat` also matches `death`, `create`, `feature`, etc. A word-boundary mode (`\b` regex wrapper) would help precision.
4. **Add match highlighting** — prefix matched lines with `>>>` or similar marker so the AI can quickly distinguish match lines from context lines in a dense result.

---

### 1.2 Source Code View (`source_code_view`)

**Current capabilities:**
- View file excerpt by relative path + start line + line count (max 1000)
- Path traversal protection, mode-based extension gating

**Assessment: ⭐⭐⭐⭐ Good**

Works well as a companion to search. The main gap:

**Recommended improvements:**
1. **Add a `search_in_file` parameter** — allow viewing context around a specific string/function within a single file without needing to first search-then-view. This avoids a round-trip tool call. The AI frequently wants to say "show me the `potionhit` function in `src/potion.c`" without knowing the line number.
2. **Return total line count** in the output header so the AI knows how much more of the file exists beyond the requested range.

---

### 1.3 List Indexed Files (`list_indexed_files`)

**Current capabilities:**
- Lists all indexed files with line counts, optional path substring filter

**Assessment: ⭐⭐⭐⭐ Good**

Simple and effective for discovery. Minor improvements:

**Recommended improvements:**
1. **Add file size** to the output (bytes or KB) alongside line count — helps the AI gauge whether a file is small enough to view entirely.
2. **Group by directory** optionally — when listing all files, a flat alphabetical list of 100+ files is hard to scan. A `group_by_directory` boolean would produce a tree-like output.

---

### 1.4 Wiki Search (`wiki_search`)

**Current capabilities:**
- Keyword frequency scoring with stop-word removal
- Returns top N articles (tool schema default 3, `WikiService` default 5 via `MaxWikiFilesToInclude` config) as full text

**Assessment: ⭐⭐ Weak — Needs significant improvement**

> [!IMPORTANT]
> The scoring algorithm is extremely naive: it counts how many distinct query words appear *anywhere* in the document content (binary presence per word, not frequency-weighted). A document mentioning "potion" once scores the same as one mentioning it 50 times. There is no TF-IDF weighting, no title/heading boost, no proximity scoring, and no fuzzy/stemming matching.

**Recommended improvements:**
1. **Implement proper TF-IDF or BM25 scoring** — count actual word frequency, normalize by document length, and weight rare query terms higher than common ones. BM25 is the standard for keyword search and would be a significant quality improvement. (Recommendation: Use a library like **Lucene.NET** which provides robust tokenization, stemming, and BM25 scoring out-of-the-box, rather than rolling a custom implementation).
2. **Boost title/filename matches** — if the query word appears in the filename or document title (first `#` heading), that document should score much higher. Currently a file named `potion.md` has no advantage over `combat.md` if both mention "potion" once.
3. **Add category filter parameter** — the `WikiService.GetRelevantContext` already supports a `categoryFilter` parameter, but the `WikiSearchTool` does not expose it to the AI. Adding a `category` parameter (e.g., `"monster"`, `"item"`, `"spell"`) would improve precision.
4. **Add excerpt/snippet mode** — currently returns the entire article content. For long articles, this wastes context. An option to return just the most relevant paragraph or excerpt around the match would be more efficient.
5. **Support multi-word phrase matching** — searching for "scroll of identify" should prefer documents containing that exact phrase over documents that merely contain "scroll" and "identify" separately.
6. **Implement wiki re-indexing** — unlike `SourceCodeService`, the `WikiService` indexes only once at startup. If wiki files are updated, a server restart is required. Add a periodic re-index or file watcher.

---

### 1.5 Monster Lookup (`monster_lookup`) & Item Lookup (`item_lookup`)

**Current capabilities:**
- Thin wrappers around `WikiService.GetRelevantContext` with a category filter of `"monster"` or `"item"`
- Falls back to unfiltered wiki search if category-filtered search returns nothing

**Assessment: ⭐⭐ Weak**

> [!WARNING]
> These tools are misleadingly named. Their tool guides say "Look up exact base stats and flags" but they actually just do a wiki keyword search — the same naive algorithm as `wiki_search` but filtered by path. They don't query any structured game data at all. If the wiki doesn't have a file with the monster/item name in a path containing "monster"/"item", these tools are no better than a generic wiki search.

**Recommended improvements:**
1. **Implement actual game data lookup** — parse `src/monst.c` and `src/objects.c` (which are already indexed in `SourceCodeService`) to extract structured monster/item definitions. Return actual stats (HP, AC, attacks, MR, flags, etc.) in a formatted table rather than relying on wiki articles.
2. **As an alternative or complement**, use the generated `pm.h` and `onames.h` files (see Part 3) to resolve names to indices, then extract the relevant `MON`/`OBJECT` macro block from the source.

---

### 1.6 NetHack Wiki Search (`nethack_wiki_search`)

**Current capabilities:**
- External MediaWiki API query to nethackwiki.com
- Returns HTML-stripped text of the #1 search result
- Rate limited (10/min/session), cached for 60 minutes

**Assessment: ⭐⭐⭐ Adequate**

**Recommended improvements:**
1. **Return top 2–3 results** instead of only the first — sometimes the first result is a disambiguation page or a less relevant article.
2. **Add a `max_length` parameter** — NetHack wiki articles can be very long. Allow the AI to request a condensed version (first N characters or first N sections).
3. **Extract structured sections** — instead of dumping the entire page as flat text, preserve section headings so the AI can orient itself in the article.

---

### 1.7 Server Dumplog Search (`search_server_dumplogs`)

**Current capabilities:**
- Substring search across up to 500 recent game records
- Returns ±100 character excerpts around matches

**Assessment: ⭐⭐⭐ Adequate for its purpose**

**Recommended improvements:**
1. **Add regex support** — simple substring matching is limiting for structured dumplog content.
2. **Increase the scanning window** or add filtering by game outcome, role, race, etc.

---

### 1.8 Global Tool Execution Constraints

| Constraint | Current Value | Location | Assessment |
|---|---|---|---|
| Max tool calls per session | 50 / 4 hours (sliding) | `ToolExecutor.cs` hardcoded const | Reasonable |
| Tool execution timeout | 15 seconds | `IToolHandler.TimeoutSeconds` default | Fine for local tools |
| Global result truncation | 3,000 characters | `ToolExecutor.cs` hardcoded const | **Too aggressive** |
| Max tool iterations | 32 (configurable) | `appsettings.json` `MaxToolIterations` | Generous |
| Source result length | 100,000 (configurable) | `appsettings.json` `MaxSourceResultLength` | Ineffective (overridden by ToolExecutor) |

> [!IMPORTANT]
> **The 3,000-character global truncation in `ToolExecutor`** is the single biggest bottleneck limiting the AI's effectiveness. This is a **hardcoded constant** (`private const int MaxResultLength = 3000`), not configurable via `appsettings.json`. With models supporting 1M+ token context windows, capping tool results at ~750 tokens means the AI frequently gets truncated, incomplete information and must make additional tool calls (burning the 50-call budget) to piece together what it needs.
>
> Note: For JSON results, `ToolExecutor` returns an **error** ("Result too large... please use a narrower search query") instead of truncating, which is even more disruptive — the AI gets no partial data at all.
>
> **Decision: Raise default to 10,000 characters** and make it configurable via `appsettings.json` (currently hardcoded). The cost concern is mitigated by making tools more powerful so fewer calls are needed overall.

---

### 1.9 User-Configurable AI Performance Settings

To allow users to balance AI capability against API costs and performance, key performance constraints will be configurable on a per-user basis, so that each user can have different performance settings.

> [!WARNING]
> **Obsolete fields in `UserAiSettings`:** The following fields are obsolete now that the system uses a multi-model architecture (`UserAiModel` for per-model settings, `UserAiApiKey` for per-provider API keys) and must be **removed** from `UserAiSettings` via a new EF Core migration:
>
> | Obsolete Field | Replaced By |
> |---|---|
> | `DefaultProvider` | `UserAiModel.Provider` |
> | `DefaultModel` | `UserAiModel.ModelId` |
> | `ThinkingLevel` | `UserAiModel.ThinkingLevel` |
> | `MaxInputTokens` | `UserAiModel.MaxInputTokens` |
> | `MaxOutputTokens` | `UserAiModel.MaxOutputTokens` |
> | `EncryptedApiKey` | `UserAiApiKey.EncryptedApiKey` |
> | `ApiKeyNonce` | `UserAiApiKey.ApiKeyNonce` |
> | `ApiKeyTag` | `UserAiApiKey.ApiKeyTag` |
>
> **Code locations requiring cleanup:**
> - `SettingsService.SaveSettingsAsync` — remove obsolete parameters (`defaultProvider`, `defaultModel`, `apiKey`, `thinkingLevel`, `maxInputTokens`, `maxOutputTokens`) and legacy API key encryption logic. Keep `allowMultipleModels`.
> - `SettingsService.GetDecryptedApiKeyAsync` / `DeleteApiKeyAsync` — remove entirely (replaced by per-provider methods)
> - `SettingsService.GetDecryptedApiKeyForProviderAsync` — remove fallback to legacy `UserAiSettings.EncryptedApiKey`
> - `SettingsController.GetSettings` — stop returning `provider`, `model`, `thinkingLevel`, `hasApiKey`, `hasModel`, `maxInputTokens`, `maxOutputTokens`
> - `SettingsController.UpdateSettings` — stop accepting `Provider`, `Model`, `ApiKey`, `ThinkingLevel`, `MaxInputTokens`, `MaxOutputTokens` in `UpdateSettingsRequest` DTO
> - `SettingsController.DeleteApiKey` (legacy single-key endpoint) — remove entirely
> - `ChatService` — remove fallback reads of `settings.DefaultProvider`, `settings.DefaultModel`, `settings.ThinkingLevel`, `settings.MaxInputTokens`, `settings.MaxOutputTokens` and legacy API key decryption; require at least one `UserAiModel` record
> - `AuthController.Me` — check `UserAiApiKeys` table instead of `UserAiSettings.EncryptedApiKey` for `hasApiKey`
> - Angular `settings.service.ts` — remove obsolete fields from `UserAiSettings` interface and `saveSettings()` parameters
> - Angular `settings.component.ts` — remove dummy empty-string passing for legacy fields; keep `allowMultipleModels` checkbox
> - `Overseer.Tests/ChatServiceTests.cs` — update test data to use `UserAiModel` instead of obsolete `UserAiSettings` fields

**Settings to make configurable (per user):**
- **Max result length** — replaces the hardcoded 3,000-char `ToolExecutor.MaxResultLength`
- **Max tool calls per session** — replaces the hardcoded 50 in `ToolExecutor.MaxCallsPerSession`
- **Max tool iterations** — already configurable in `appsettings.json`, but should also be per-user

**Backend & Database:**
- Sensible **minimum and maximum limits** for each setting, as well as the **default values**, will be defined in `appsettings.json`.
- The values of these new user settings will be stored in the database (new nullable columns on `UserAiSettings`, requiring a new EF Core migration that also drops the obsolete columns listed above).
- If the user hasn't set any values, defaults defined in the appsettings will be used.
- Both the frontend and the backend will check that the values are within the configured limits.
- `ToolExecutor` must be refactored to accept per-user limits instead of using hardcoded constants. This likely means passing a settings/context object through the tool execution pipeline.

**Frontend UI:**
- The Settings page is an **Angular SPA component** (`Overseer/ClientApp/src/app/settings/settings.component.*`), not a Razor page. The new UI will be added to this component.
- A new section with the heading **"AI Performance Settings"** will be added below the existing settings.
- Each setting will feature a dropdown menu with predefined values that can be selected (such as Minimal, Low, Medium, High, Very High).
- The default option for each performance setting must come first in its list. It will read "Default" and then the default value after an ndash, such as "Default – 20000 tokens".
- For all other items in the dropdown, it will have first the label and then the value after an ndash, such as "Medium – 10000 tokens".
- Each setting will also have a **Custom** option, which will allow the user to set a custom value.
- The `SettingsController` API and `SettingsService` will need updated DTOs to handle the new performance fields (and remove obsolete ones).

---

## Part 2: Recommended New Tools

### 2.1 `get_function_definition` — Function/Macro Definition Extractor

**Purpose:** Given a function or macro name, find its definition and return the complete function body or macro definition with full context.

**Why needed:** The AI frequently needs to understand a specific function's implementation. Currently this requires: (1) `source_code_search` to find the function name, (2) examining the line numbers, (3) `source_code_view` to read the full body. This 2–3 call chain is the most common search pattern but is wasteful. A dedicated tool could do it in one call.

**Parameters:**
- `name` (string, required): Function or macro name
- `type` (enum, optional): `"function"`, `"macro"`, `"struct"`, `"any"` (default `"any"`)

**Implementation approach:** Use regex patterns to detect function definitions (`<return_type>\s+<name>\s*\(` for functions, `#define\s+<name>` for macros, `struct\s+<name>` for structs), then extract the complete body by tracking brace depth or line continuation.

**Estimated effort:** Medium — regex-based extraction from already-indexed content.

---

### 2.2 `get_constants` — Game Constant & Define Lookup

**Purpose:** Look up the value and definition of specific `#define` constants, enum values, or configuration macros used in the GnollHack source code.

**Why needed:** The AI constantly needs to know what `PM_GNOLL`, `WAN_DEATH`, `AD_FIRE`, `MR_FIRE`, `MAXULEV`, etc. mean. Currently it must do a source code search, hope the result isn't truncated, and parse the `#define` line. A dedicated tool that indexes all `#define` and `enum` values would be much faster.

**Parameters:**
- `name` (string, required): Constant name or pattern (e.g., `"PM_GNOLL"`, `"AD_*"`)
- `prefix_filter` (string, optional): Filter by prefix (e.g., `"PM_"`, `"AD_"`, `"WAN_"`)

**Implementation approach:** During indexing, parse all `.h` files for `#define` lines and enum values, store in a dictionary. This tool becomes a simple dictionary lookup.

**Estimated effort:** Medium — parsing `#define` lines is straightforward.

> [!IMPORTANT]
> This tool is closely related to Part 3 (generated files). Constants like `PM_*` and `ART_*` are defined in generated headers (`pm.h`, `onames.h`) that are currently **excluded from indexing**. Making these files available would make this tool much more powerful.

---

### 2.3 `get_monster_stats` — Structured Monster Data Extractor

**Purpose:** Extract and return structured monster statistics directly from `src/monst.c`, formatted as a readable stat block.

**Why needed:** The current `monster_lookup` tool just does a wiki keyword search. The AI frequently needs precise monster stats (base level, speed, AC, MR, attacks, resistances, flags) that are definitively defined in `src/monst.c` via `MON()` macros.

**Parameters:**
- `name` (string, required): Monster name (partial match supported)
- `include_attacks` (boolean, optional, default true): Include the attack tuple details

**Implementation approach:** Parse `src/monst.c` during indexing to extract `MON()` macro blocks. The structured fields are well-defined by the macro format. Return a formatted stat block. Parsing is done entirely within the Overseer C# code — no changes to `makedefs` or other build utilities.

**Estimated effort:** High — `MON()` macro parsing is complex due to multi-line format and nested macros, but extremely valuable.

---

### 2.4 `get_item_stats` — Structured Item Data Extractor

**Purpose:** Same concept as `get_monster_stats` but for `src/objects.c`.

**Parameters:**
- `name` (string, required): Item name (partial match supported)
- `category` (string, optional): Filter by category (weapon, armor, potion, scroll, wand, ring, etc.)

**Implementation approach:** Parse `WEAPON()`, `ARMOR()`, `POTION()`, `SCROLL()`, `WAND()`, `RING()`, etc. macro blocks from `src/objects.c`. All parsing done in Overseer C# code — no changes to `makedefs`.

**Estimated effort:** High — similar complexity to monster parsing.

---

### 2.5 `wiki_view` — View Specific Wiki Article by Name

**Purpose:** Retrieve a specific wiki article by its filename or title, without going through the scoring/ranking search.

**Why needed:** When the AI already knows which article it wants (e.g., from a previous search or from its training data), forcing it through the keyword search is wasteful and unreliable. A direct retrieval tool avoids this.

**Parameters:**
- `article` (string, required): Article filename or title (fuzzy matched)
- `section` (string, optional): Specific section heading to extract

**Implementation approach:** Simple filename/title matching in the wiki index, with optional section extraction by heading parsing.

**Estimated effort:** Low.

---

### 2.6 `search_definitions` — Cross-Reference Definition Search

**Purpose:** Search for where a symbol (function, macro, type, struct) is **defined** vs. where it is **used**. Return definition site with context.

**Why needed:** The current `source_code_search` finds all occurrences of a term. When the AI searches for `hitmu`, it gets 50+ results across multiple files — but it usually wants the *definition*, not every call site. This tool would use heuristics (function definition patterns, `#define`, `typedef`, `struct`) to find and prioritize the definition.

> [!NOTE]
> **Overlap with `get_function_definition` (2.1):** These two tools share significant functionality. Consider merging them into a single `find_definition` tool that returns the full body when type is function/macro/struct, and just the definition line when type is enum/constant. This avoids having two similar tools that confuse the AI's tool selection.

**Parameters:**
- `symbol` (string, required): Symbol name to find the definition of
- `kind` (enum, optional): `"function"`, `"macro"`, `"type"`, `"struct"`, `"enum"`, `"any"`

**Estimated effort:** Medium.

---

### 2.7 `get_call_graph` — Simple Call Graph Extraction

**Purpose:** Given a function name, identify which other functions it calls and which functions call it.

**Why needed:** Understanding code flow is critical for the AI when debugging or explaining mechanics. "What calls `potionhit`?" and "What does `potionhit` call?" are common questions.

**Parameters:**
- `function_name` (string, required): Function to analyze
- `direction` (enum, optional): `"callers"`, `"callees"`, `"both"` (default `"both"`)
- `depth` (integer, optional, default 1): How many levels deep to trace

**Implementation approach:** Regex-based: find the function body, extract function call patterns (`\b\w+\s*\(`) from within it (callees). For callers, search all files for references to the function name that appear in a function call context.

**Estimated effort:** Medium-High — imperfect without a real parser, but regex-based approximation is useful.

---

## Part 3: Generated Files Availability Plan

### 3.1 The Problem

Six header files generated by `makedefs` contain critical information for AI analysis:

| File | Content | AI Value |
|---|---|---|
| `onames.h` | `#define ARROW 1`, `#define WAN_WISH 320`, `#define ART_EXCALIBUR 1`, `NUM_OBJECTS` | **Critical** — maps item names to indices, defines category boundaries |
| `pm.h` | `#define PM_GIANT_ANT 0`, `#define PM_HUMAN_WEREWOLF 31`, `NUM_MONSTERS` | **Critical** — maps monster names to indices |
| `animoff.h` | `animation_offsets[]`, `enlargement_offsets[]`, `replacement_offsets[]` | Moderate — tile animation data |
| `animtotals.h` | `TOTAL_NUM_ANIMATION_FRAMES`, etc. | Moderate — tile count totals |
| `date.h` | Build date, version, git SHA | Low — build metadata |
| `vis_tab.h` | Vision table header | Low — precomputed LOS data |

The AI currently **cannot search or view** these files because:
1. They are `.gitignore`d (confirmed in `.gitignore` lines 39–44) and not tracked in the repository
2. `SourceCodeService` explicitly excludes them in its `_excludedFiles` set (lines 26–29)

> [!NOTE]
> These files may already exist on disk in `include/` from a local developer build (e.g., `onames.h` ~30 KB, `pm.h` ~14 KB). However, the Overseer server cannot rely on this — they won't exist on a fresh clone or CI deployment. The server must be able to generate them.

Without `pm.h` and `onames.h`, the AI cannot resolve symbolic references like `PM_GNOLL` → monster index 42, or `WAN_DEATH` → wand object index 305. This makes source code analysis significantly harder.

### 3.2 Proposed Solution: Server-Side makedefs Generation Pipeline

**Approach:** Build `makedefs` on the Overseer server and run it to generate the header files into the repository's `include/` directory. Then remove these files from the `_excludedFiles` set in `SourceCodeService` so the AI can search and view them.

#### Step 1: Build makedefs on the Server

> [!CAUTION]
> **CRITICAL REBUILD REQUIREMENT:** The `makedefs` utility compiles `src/monst.c` and `src/objects.c` directly into its own executable. Therefore, when those files are updated in the repository, **you cannot simply run an existing `makedefs.exe`**. You must **recompile** `makedefs` first, otherwise it will generate stale headers based on the old C files.

The server already has the GnollHack repository cloned (configured via `SourceCodePath`). To support generating headers, Overseer needs a compilation pipeline:

1. **Install build tools** on the server:
   - **Windows**: Visual Studio Build Tools with C++ Desktop workload (v143).
   - **Linux**: Standard build essentials (`gcc`, `make`).

2. **Automate the build** via Overseer:
   When a repo update is detected, Overseer should programmatically invoke the compiler before running `makedefs`.
   - **Windows**:
     ```powershell
     & $msbuild win/win32/vs/makedefs.vcxproj /t:Build /p:Configuration=Release /p:Platform=x64
     ```
   - **Linux**:
     If a Makefile exists, invoke `make makedefs` (or equivalent).

3. **Configuration**: Add `MakedefsBuildCommand` and `MakedefsExecutablePath` to `appsettings.json` so the server can adapt to the OS it's deployed on without hardcoded paths.

#### Step 2: Integrate into SourceCodeService Re-indexing

Add a step to the `CheckForUpdates` / `IndexRepository` flow:

```csharp
private void RegenerateHeaders()
{
    // 1. Rebuild makedefs first (monst.c/objects.c might have changed)
    string buildCmd = _configuration["MakedefsBuildCommand"];
    if (!string.IsNullOrEmpty(buildCmd))
    {
        RunCommand(buildCmd, _sourceCodePath);
    }

    // 2. Locate the freshly built executable
    var makedefsPath = _configuration["MakedefsExecutablePath"] ?? 
                       Path.Combine(_sourceCodePath, "bin", "Release", "x64", "makedefs.exe");
    
    if (!File.Exists(makedefsPath)) 
    {
        _logger.LogWarning("makedefs executable not found at {Path}", makedefsPath);
        return;
    }

    // 3. Run makedefs with appropriate working directories
    //    (per aftermakedefs.proj: -v/-o/-p/-z run from util/, -a runs from dat/)
    var utilDir = Path.Combine(_sourceCodePath, "util");
    RunCommand($"{makedefsPath} -o", utilDir);  // generates include/onames.h
    RunCommand($"{makedefsPath} -p", utilDir);  // generates include/pm.h
    RunCommand($"{makedefsPath} -a", Path.Combine(_sourceCodePath, "dat"));  // generates include/animoff.h & animtotals.h
}

private void RunCommand(string command, string workingDir)
{
    // Implementation of process execution with timeout...
}
```

Then remove the generated files from the exclusion set:

```diff
 private readonly HashSet<string> _excludedFiles = new(StringComparer.OrdinalIgnoreCase)
 {
-    "vis_tab.c", "vis_tab.h", "onames.h", "pm.h", "date.h", "animoff.h", "animtotals.h"
+    "vis_tab.c", "vis_tab.h", "date.h"
 };
```

(`date.h` can remain excluded since it only contains build metadata; `vis_tab.*` is pure lookup tables with no semantic value.)

#### Step 3: Re-generate on Git Updates

The existing 10-minute re-index timer in `SourceCodeService.CheckForUpdates` already detects git HEAD changes. Add the `RegenerateHeaders()` call before `IndexRepository()`:

```csharp
if (currentSha != _lastHeadSha)
{
    RegenerateHeaders();  // Re-run makedefs when source changes
    IndexRepository();     // Then re-index including the new headers
}
```

This ensures that when the repository is updated (e.g., new monsters or objects added to `monst.c` / `objects.c`), the generated headers are regenerated and the AI always has current data.

> [!WARNING]
> **Fragile branch detection:** The current `CheckForUpdates` reads `.git/refs/heads/master` (with fallback to `.git/FETCH_HEAD`). This will fail if the repository uses a different default branch (e.g., `main`). Consider making the branch name configurable or using `git rev-parse HEAD` instead.

### 3.3 Chosen Approach: Server-Side makedefs Pipeline

> [!IMPORTANT]
> **Decision: Server-side makedefs generation.** Generated files are not part of the repository by design, so the Overseer server must build `makedefs` and generate the headers itself. Steps 1–3 above define the complete pipeline.
>
> **Key requirements:**
> - The server needs Visual Studio Build Tools with C++ Desktop workload (v143), or a pre-compiled `makedefs.exe` binary deployed alongside the application
> - The `SourceCodeService` re-index flow calls `RegenerateHeaders()` before `IndexRepository()` whenever a git HEAD change is detected
> - `onames.h`, `pm.h`, `animoff.h`, and `animtotals.h` are removed from the `_excludedFiles` set so the AI can search and view them
> - `date.h` and `vis_tab.*` remain excluded (no semantic value for AI)

---

## Part 4: Priority & Effort Summary

### Improvements to Existing Tools (by priority)

| Priority | Change | Effort | Impact |
|---|---|---|---|
| 🔴 P0 | Make `ToolExecutor.MaxResultLength` configurable and raise default to 10,000 chars | Low (refactor hardcoded const to config) | **High** — unlocks much richer results |
| 🔴 P0 | Remove 8 obsolete legacy fields from `UserAiSettings` and clean up all fallback code (Section 1.9) | Medium (DB migration, code cleanup across 6+ files, Angular + tests) | **High** — eliminates tech debt, prerequisite for performance settings |
| 🔴 P0 | User-configurable AI performance settings (Section 1.9) | Medium (DB migration, Angular UI, API, validation) | **High** — per-user control over cost vs. capability |
| 🟡 P1 | Improve wiki search scoring (BM25 + title boost) | Medium | **High** — wiki search becomes reliable |
| 🟡 P1 | Add wiki re-indexing (periodic or file watcher) | Low | Medium — live wiki updates |
| 🟡 P1 | Expose `category` parameter on `wiki_search` | Trivial | Medium — better precision |
| 🟢 P2 | Add `case_sensitive` and `whole_word` to source search | Low | Medium — precision improvement |
| 🟢 P2 | Add match highlighting to source search results | Low | Low-Medium — readability |
| 🟢 P2 | Add `search_in_file` to `source_code_view` | Low | Medium — saves round-trips |
| 🟢 P2 | Return top 2–3 results from NetHack wiki | Low | Low-Medium |
| 🟢 P2 | Fix fragile branch detection in `CheckForUpdates` (hardcoded `master`) | Trivial | Low — prevents re-index failures on repos with `main` branch |

### New Tools (by priority)

| Priority | Tool | Effort | Impact |
|---|---|---|---|
| 🔴 P0 | Make `onames.h` / `pm.h` available (Part 3) | Low-Medium | **Critical** — enables constant resolution |
| 🟡 P1 | `get_constants` — define/enum lookup | Medium | **High** — instant constant resolution |
| 🟡 P1 | `wiki_view` — direct article retrieval | Low | Medium — avoids unreliable search |
| 🟡 P1 | `search_definitions` — find definitions vs. usages | Medium | **High** — most common AI need |
| 🟢 P2 | `get_function_definition` — full function extractor (consider merging with `search_definitions`) | Medium | Medium — convenience |
| 🟢 P2 | `get_monster_stats` — structured monster data | High | Medium — complements wiki |
| 🟢 P2 | `get_item_stats` — structured item data | High | Medium — complements wiki |
| 🟢 P3 | `get_call_graph` — call graph extraction | Medium-High | Low-Medium — advanced analysis |

---

## Resolved Decisions

| Question | Decision |
|---|---|
| **Truncation limit** | Raise `ToolExecutor.MaxResultLength` to **10,000 characters** default. Currently a hardcoded `private const` — must be refactored to read from `appsettings.json` and/or per-user settings. Also fix the JSON-result error behavior to return partial data instead of an error. |
| **User Performance Settings** | Key performance constraints will be configurable per user (new columns on existing `UserAiSettings` entity, saved in DB). Angular Settings page gets new "AI Performance Settings" section with dropdowns for presets (e.g. Minimal, Low, Medium, High, Very High) + Custom option. Min/max limits and defaults configured in `appsettings.json`. `ToolExecutor` refactored to accept per-user limits. |
| **Legacy field cleanup** | Remove 8 obsolete fields from `UserAiSettings` (`DefaultProvider`, `DefaultModel`, `ThinkingLevel`, `MaxInputTokens`, `MaxOutputTokens`, `EncryptedApiKey`, `ApiKeyNonce`, `ApiKeyTag`). Clean up all fallback/legacy code paths in `SettingsService`, `SettingsController`, `ChatService`, `AuthController`, Angular frontend, and unit tests. Single EF Core migration to both drop obsolete columns and add new performance setting columns. |
| **Generated files approach** | **Server-side makedefs pipeline.** Generated files are not part of the repository by design. |
| **Wiki search overhaul** | **Full BM25 implementation.** The wiki is very large and warrants proper ranking. Recommended to use **Lucene.NET** for robust tokenization and BM25 out-of-the-box. |
| **New tool priorities** | Priority order from the table above is confirmed (P0 → P1 → P2 → P3). |
| **Structured data extraction** | **No changes to makedefs.** Parse C macros directly in Overseer C# code instead. |
