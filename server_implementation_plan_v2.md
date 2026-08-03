# Overseer AI Tools — Server Implementation Plan

This document describes all remaining server-side work for improving the Overseer AI's source code search, wiki search, and game data analysis tools. All changes are in the **MobileGnollHackLogger/Overseer** project. No changes are needed on the GnollHack app (client) side.

---

## Background

The Overseer AI assists GnollHack players with game mechanics questions, debugging, and source code analysis. It uses a set of server-side tools (registered in `ToolRegistry`) to search indexed source code, query the GnollHack wiki, look up monsters/items, and search the NetHack wiki. A review of these tools found several issues and opportunities for improvement.

### What's Already Done
- `ToolExecutor` truncation is now configurable via `AiPerformanceSettings.MaxResultLength` (default 8,000 chars)
- `MaxSourceResultLength` in `appsettings.json` is 100,000 (source search internal limit)
- `MaxCallsPerSession` and `MaxToolIterations` are configurable
- Tool guides have been improved with preference hierarchy and PREFERRED TOOL labels
- New client tools `get_app_log` and `get_panic_log` added

### What This Plan Covers
The remaining work, organized into 4 phases from quick fixes through advanced features.

---

## Phase 0: Quick Fixes (Effort: Trivial)

### 0.1 Fix Misleading Tool Guides

**Problem:** The tool guides for `monster_lookup` and `item_lookup` claim "**PREFERRED TOOL** — Returns authoritative monster/item data directly from the game database" and "No source_code_search verification is needed for stats returned by this tool" — but the actual implementations are just wiki keyword searches. Additionally, `_policy.md` (line 25) reinforces this by claiming "The wiki and lookup tools draw from the same game data," which is not true. This causes the AI to give the player incorrect or incomplete information without cross-checking.

**Fix:** Update the tool guides to accurately describe what the tools do *today*:

**File:** `Overseer/ToolGuides/monster_lookup.md`
```markdown
Search the GnollHack wiki for monster information.

Uses keyword search across wiki articles in the "monster" category.
Results are wiki articles, not structured game data — they may be
incomplete or not available for all monsters. If you need exact stats
(HP, AC, attacks, MR, flags), verify against src/monst.c using
source_code_search.
```

**File:** `Overseer/ToolGuides/item_lookup.md`
```markdown
Search the GnollHack wiki for item information.

Uses keyword search across wiki articles in the "item" category.
Results are wiki articles, not structured game data — they may be
incomplete or not available for all items. If you need exact stats
(damage, AC, weight, properties), verify against src/objects.c using
source_code_search.
```

**File:** `Overseer/ToolGuides/_policy.md` — Update line 25 to remove the claim that lookup tools don't need source verification. The current text says:
> "Do NOT routinely use source_code_search to verify information that wiki/lookup tools already provide clearly (e.g., monster stats, item properties, class descriptions). The wiki and lookup tools draw from the same game data."

The last sentence ("The wiki and lookup tools draw from the same game data") is misleading — these tools do wiki keyword searches, not database queries. Rewrite to:
> "Do NOT routinely use source_code_search to double-check wiki articles for well-documented topics — but do verify when you need exact formulas or stats that the wiki might not cover."

### 0.2 Bump Default Truncation to 10,000

**File:** `Overseer/appsettings.json`

Change the `AiPerformanceSettings.MaxResultLength.Default` from `8000` to `10000`:

```json
"AiPerformanceSettings": {
    "MaxResultLength": {
        "Min": 1000,
        "Max": 100000,
        "Default": 10000
    }
}
```

**Rationale:** 8,000 is good but still truncates many multi-file source search results. 10,000 chars (~2,500 tokens) is a good balance between cost and completeness. With smarter tools (this plan), the AI will make fewer calls, so giving each call more room is cost-neutral.

---

## Phase 1: Core Infrastructure (Effort: Medium-High)

### 1.1 Server-Side makedefs Pipeline

**Goal:** Make auto-generated header files (`onames.h`, `pm.h`, `animoff.h`, `animtotals.h`) available to the AI by building and running `makedefs` on the server.

**Why it matters:** These files contain critical game constants:
- `pm.h`: `#define PM_GIANT_ANT 0`, `#define PM_HUMAN_WEREWOLF 31`, `NUM_MONSTERS` — maps monster names to indices
- `onames.h`: `#define ARROW 1`, `#define WAN_WISH 320`, `#define ART_EXCALIBUR 1`, `NUM_OBJECTS` — maps item names and artifact names to indices

Without them, the AI cannot resolve symbolic references like `PM_GNOLL` or `WAN_DEATH` when reading source code.

> [!CAUTION]
> **CRITICAL:** The `makedefs` utility compiles `src/monst.c`, `src/objects.c`, and `src/animdef.c` directly into its own executable. When those files change in the repository, you **must recompile** `makedefs` before running it — otherwise it generates stale headers from the old C source baked into the binary.

> [!NOTE]
> **Security consideration:** The `makedefs` pipeline compiles and executes code from the tracked repository. This is safe as long as only trusted branches are tracked. As an additional safeguard, add a `MakedefsBranch` config key (default: the main branch) so only the configured branch triggers header regeneration. If the repository HEAD points to a different branch, skip regeneration.

#### Implementation Steps

**Step 1: Add configuration to `appsettings.json`:**
```json
{
    "MakedefsBuildCommand": "",
    "MakedefsExecutablePath": "",
    "MakedefsBranch": ""  // Optional: restrict regeneration to this branch (e.g., "main")
}
```

On the Windows server, `MakedefsBuildCommand` would be the MSBuild invocation:
```
"C:\path\to\MSBuild.exe" win/win32/vs/makedefs.vcxproj /t:Build /p:Configuration=Release /p:Platform=x64
```
This compiles `makedefs.exe` to `tools\Release\x64\makedefs.exe` (defined by `ToolsDir` in `win/win32/vs/dirs.props`). Alternatively, the `aftermakedefs.proj` file handles both building and running makedefs with the correct working directories, but it generates *all* output files (not just the headers we need).

If build tools are not available on the server, a pre-compiled `makedefs.exe` can be deployed and its path set in `MakedefsExecutablePath`. In this case, the binary must be manually updated whenever `monst.c`, `objects.c`, or `animdef.c` change (all three are compiled directly into `makedefs.exe`).

**Step 2: Add regeneration logic to `SourceCodeService.cs`:**

> [!NOTE]
> **Architecture note:** The current `SourceCodeService` does NOT run git commands — it detects repository updates by reading the SHA directly from `.git/refs/heads/master` (falling back to `.git/FETCH_HEAD`). The `RegenerateHeaders()` method should follow the same pattern: it does not need git integration, just makedefs execution. The configuration keys `MakedefsBuildCommand` and `MakedefsExecutablePath` provide the server operator with flexibility.
>
> The branch restriction feature (`MakedefsBranch`) can read the branch name from the same `.git/refs/heads/` directory by checking which ref file was used, or by reading `.git/HEAD` to get the symbolic ref.

```csharp
// Track modification timestamps of files compiled into makedefs.
// If none changed since last run, skip recompilation + regeneration.
private readonly string[] _makedefsSourceFiles = new[]
{
    "src/monst.c", "src/objects.c", "src/animdef.c", "util/makedefs.c"
};
private Dictionary<string, DateTime> _lastMakedefsSourceTimestamps = new();

/// <summary>
/// Regenerate onames.h, pm.h, animoff.h, animtotals.h by building and running makedefs.
/// Only runs when the source files compiled into makedefs have changed (or on startup when force=true).
/// </summary>
private void RegenerateHeaders(bool force = false)
{
    try
    {
        var currentTimestamps = new Dictionary<string, DateTime>();
        // 0a. Check if any makedefs source files actually changed
        if (!force)
        {
            bool anyChanged = false;
            foreach (var relPath in _makedefsSourceFiles)
            {
                string fullPath = Path.Combine(_sourceCodePath, relPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath))
                {
                    var lastWrite = File.GetLastWriteTimeUtc(fullPath);
                    currentTimestamps[relPath] = lastWrite;
                    if (!_lastMakedefsSourceTimestamps.TryGetValue(relPath, out var prev) || prev != lastWrite)
                    {
                        anyChanged = true;
                    }
                }
            }
            if (!anyChanged)
            {
                _logger.LogDebug("makedefs source files unchanged, skipping header regeneration.");
                return;
            }
        }

        // 0b. Optional branch restriction
        string? allowedBranch = _configuration["MakedefsBranch"];
        if (!string.IsNullOrEmpty(allowedBranch))
        {
            // Read the current branch from .git/HEAD (e.g., "ref: refs/heads/master")
            string gitHeadFile = Path.Combine(_sourceCodePath, ".git", "HEAD");
            if (File.Exists(gitHeadFile))
            {
                string headContent = File.ReadAllText(gitHeadFile).Trim();
                string currentBranch = headContent.StartsWith("ref: refs/heads/")
                    ? headContent.Substring("ref: refs/heads/".Length)
                    : "";
                if (!string.Equals(currentBranch, allowedBranch, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Skipping makedefs: current branch '{Current}' != allowed '{Allowed}'",
                        currentBranch, allowedBranch);
                    return;
                }
            }
        }

        // 1. Rebuild makedefs if a build command is configured
        string? buildCmd = _configuration["MakedefsBuildCommand"];
        if (!string.IsNullOrEmpty(buildCmd))
        {
            if (!RunProcess(buildCmd, _sourceCodePath))
            {
                _logger.LogWarning("makedefs build failed — aborting header generation to avoid running a stale binary.");
                return;  // Do NOT continue to run a potentially stale makedefs.exe
            }
        }

        // 2. Locate the executable
        string? makedefsPath = _configuration["MakedefsExecutablePath"];
        if (string.IsNullOrEmpty(makedefsPath))
        {
            // The GnollHack build system outputs makedefs.exe to tools\$(Configuration)\$(Platform)\
            // (defined in win/win32/vs/dirs.props as ToolsDir), NOT bin\.
            makedefsPath = Path.Combine(_sourceCodePath, "tools", "Release", "x64", "makedefs.exe");
        }
        
        if (!File.Exists(makedefsPath))
        {
            _logger.LogWarning("makedefs executable not found at {Path}, skipping header generation", makedefsPath);
            return;
        }

        // 3. Generate headers
        // Working directories match aftermakedefs.proj: -o/-p from util/, -a from dat/.
        // All use INCLUDE_TEMPLATE ("../include/%s") so output goes to include/ either way.
        var utilDir = Path.Combine(_sourceCodePath, "util");
        bool allSucceeded = true;
        allSucceeded &= RunProcess($"\"{makedefsPath}\" -o", utilDir);  // onames.h
        allSucceeded &= RunProcess($"\"{makedefsPath}\" -p", utilDir);  // pm.h
        allSucceeded &= RunProcess($"\"{makedefsPath}\" -a", Path.Combine(_sourceCodePath, "dat"));  // animoff.h, animtotals.h
        
        // Only commit timestamps if ALL steps succeeded.
        // If any failed, we want to retry on the next cycle.
        if (allSucceeded && !force)
        {
            _lastMakedefsSourceTimestamps = currentTimestamps;
        }
        
        _logger.LogInformation(allSucceeded
            ? "makedefs header regeneration completed successfully."
            : "makedefs header regeneration partially failed — will retry next cycle.");
    }
    catch (Exception ex)
    {
        // Graceful degradation: if makedefs fails entirely, log the error
        // and continue with indexing. Previously generated headers (if any)
        // will still be on disk and will be indexed. If no headers exist,
        // they simply won't be indexed — the rest of the source code still works.
        _logger.LogError(ex, "makedefs header regeneration failed. Continuing with existing headers (if any).");
    }
}

/// <summary>
/// Runs a shell command. Cross-platform: uses cmd.exe on Windows, /bin/bash on Linux/macOS.
/// Returns true if the process ran and exited with code 0, false otherwise.
/// </summary>
private bool RunProcess(string command, string workingDir)
{
    try
    {
        bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows);

        var psi = new ProcessStartInfo(
            isWindows ? "cmd.exe" : "/bin/bash",
            isWindows ? $"/c {command}" : $"-c \"{command}\"")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process == null) return false;
        
        // IMPORTANT: Read streams asynchronously to avoid deadlock.
        // If the child process fills the OS pipe buffer for one stream while
        // we're synchronously reading the other, both processes deadlock.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        
        bool exited = process.WaitForExit(30000); // 30 second timeout
        
        if (!exited)
        {
            _logger.LogWarning("makedefs command timed out: {Command}", command);
            try { process.Kill(); } catch { /* best effort */ }
            return false;
        }
        
        // Process has exited — drain any remaining buffered output.
        // GetAwaiter().GetResult() is safe here because the pipes are closed.
        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        
        if (process.ExitCode != 0)
        {
            _logger.LogWarning("makedefs command failed (exit {Code}): {Command}, stderr: {StdErr}",
                process.ExitCode, command, stderr);
            return false;
        }
        return true;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error running makedefs command: {Command}", command);
        return false;
    }
}
```

**Step 3: Call `RegenerateHeaders()` before `IndexRepository()` in `CheckForUpdates`:**

The current `CheckForUpdates` reads the SHA from `.git/refs/heads/master` (falling back to `.git/FETCH_HEAD`) and calls `IndexRepository()` on change. Insert `RegenerateHeaders()` before the `IndexRepository()` call:

```csharp
if (!string.IsNullOrEmpty(currentSha) && currentSha != _lastHeadSha)
{
    _logger.LogInformation("Repository update detected ({OldSha} -> {NewSha}). Re-indexing.",
        _lastHeadSha, currentSha);
    RegenerateHeaders();   // Only rebuilds if monst.c/objects.c/animdef.c/makedefs.c changed
    IndexRepository();      // Then index including the new headers (or stale ones if regen failed/skipped)
}
```

Also call `RegenerateHeaders(force: true)` once during `StartAsync` before the initial `IndexRepository()`. The `force: true` ensures headers are generated on startup regardless of stored timestamps (since the server may have been down when files changed). On subsequent `CheckForUpdates` calls, the timestamp check avoids unnecessary MSBuild recompilations when only unrelated files changed.

> [!WARNING]
> **Prerequisite:** The current `SourceCodeService` constructor takes `IConfiguration configuration` but does **not** store it as a field — it only reads individual values in the constructor. The `RegenerateHeaders()` method needs access to `_configuration` at runtime, so you must add a private field:
> ```csharp
> private readonly IConfiguration _configuration;
> ```
> and assign it in the constructor: `_configuration = configuration;`

**Step 4: Remove generated files from the exclusion set:**

```csharp
// Before:
private readonly HashSet<string> _excludedFiles = new(StringComparer.OrdinalIgnoreCase)
{
    "vis_tab.c", "vis_tab.h", "onames.h", "pm.h", "date.h", "animoff.h", "animtotals.h"
};

// After:
private readonly HashSet<string> _excludedFiles = new(StringComparer.OrdinalIgnoreCase)
{
    "vis_tab.c", "vis_tab.h", "date.h"
};
```

Keep `date.h` excluded (build metadata only) and `vis_tab.*` (pure lookup tables, no semantic value).

---

### 1.2 BM25 Wiki Search (Lucene.NET)

**Goal:** Replace the naive keyword-counting wiki search with proper BM25 ranking using Lucene.NET.

**Problem with current implementation (`WikiService.cs`):** The current scoring counts how many distinct query words appear *anywhere* in a document — binary presence per word, not frequency-weighted. A document mentioning "potion" once scores the same as one mentioning it 50 times. There is no TF-IDF weighting, no title/heading boost, no proximity scoring, and no stemming.

**Recommended approach:** Use [Lucene.NET](https://github.com/apache/lucenenet) (NuGet: `Lucene.Net`, `Lucene.Net.Analysis.Common`, `Lucene.Net.QueryParser`). This provides BM25 scoring, tokenization, stemming, and fuzzy matching out of the box.

#### Implementation Outline

**Step 1: Add NuGet packages to `Overseer.csproj`:**
```xml
<PackageReference Include="Lucene.Net" Version="4.8.0-beta00016" />
<PackageReference Include="Lucene.Net.Analysis.Common" Version="4.8.0-beta00016" />
<PackageReference Include="Lucene.Net.QueryParser" Version="4.8.0-beta00016" />
```

**Step 2: Rewrite `WikiService.cs` to use a Lucene in-memory index:**

```csharp
public class WikiService : IDisposable
{
    private readonly object _swapLock = new();
    private RAMDirectory? _directory;
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;
    private StandardAnalyzer _analyzer;
    
    private void IndexWikiFiles()
    {
        _analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        
        // Build the new index into a fresh directory
        var newDirectory = new RAMDirectory();
        var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, _analyzer)
        {
            Similarity = new BM25Similarity()  // BM25 scoring
        };
        
        using (var writer = new IndexWriter(newDirectory, config))
        {
            foreach (var file in wikiFiles)
            {
                var doc = new Document();
                doc.Add(new TextField("title", Path.GetFileNameWithoutExtension(file), Field.Store.YES));
                doc.Add(new TextField("content", File.ReadAllText(file), Field.Store.YES));
                doc.Add(new StringField("path", file, Field.Store.YES));
                doc.Add(new StringField("filename", Path.GetFileName(file), Field.Store.YES));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }
        
        var newReader = DirectoryReader.Open(newDirectory);
        var newSearcher = new IndexSearcher(newReader);
        newSearcher.Similarity = new BM25Similarity();
        
        // Hot-swap: atomically replace the old index, then dispose of the old one
        RAMDirectory? oldDirectory;
        DirectoryReader? oldReader;
        lock (_swapLock)
        {
            oldDirectory = _directory;
            oldReader = _reader;
            _directory = newDirectory;
            _reader = newReader;
            _searcher = newSearcher;
        }
        
        // Dispose old resources OUTSIDE the lock to avoid blocking queries
        oldReader?.Dispose();
        oldDirectory?.Dispose();
    }
    
    public IEnumerable<string> GetRelevantContext(string query, string? categoryFilter, int? maxResults)
    {
        IndexSearcher? searcher;
        lock (_swapLock)
        {
            searcher = _searcher;
        }
        if (searcher == null) return Enumerable.Empty<string>();
        
        // Build a BooleanQuery that searches both title (boosted) and content
        var parser = new MultiFieldQueryParser(
            LuceneVersion.LUCENE_48,
            new[] { "title", "content" },
            _analyzer,
            new Dictionary<string, float> { { "title", 5.0f }, { "content", 1.0f } }
        );
        
        var luceneQuery = parser.Parse(QueryParserBase.Escape(query));
        
        // Apply category filter if provided
        if (!string.IsNullOrEmpty(categoryFilter))
        {
            var boolQuery = new BooleanQuery();
            boolQuery.Add(luceneQuery, Occur.MUST);
            boolQuery.Add(new WildcardQuery(new Term("path", $"*{categoryFilter}*")), Occur.MUST);
            luceneQuery = boolQuery;
        }
        
        var hits = searcher.Search(luceneQuery, maxResults ?? 5);
        // ... format and return results
    }
    
    public void Dispose()
    {
        _reader?.Dispose();
        _directory?.Dispose();
        _reindexTimer?.Dispose();
    }
}
```

> [!IMPORTANT]
> **Memory management:** The code above shows the correct hot-swap pattern. When re-indexing, a *new* `RAMDirectory` + `IndexSearcher` is built first, then atomically swapped in. The *old* searcher and directory are `Dispose()`d after the swap. This avoids both memory leaks and query failures during re-indexing. Alternatively, `FSDirectory` (disk-backed with OS memory mapping) can be used instead of `RAMDirectory` to reduce managed memory pressure on the GC.

**Key improvements this gives:**
- **BM25 scoring** — proper term frequency + inverse document frequency + document length normalization
- **Title boost** (5×) — a file named `potion.md` ranks much higher for query "potion"
- **Tokenization + stemming** — "potions" matches "potion", "scrolls" matches "scroll"
- **Phrase matching** — "scroll of identify" can be searched as a phrase
- **Category filtering** integrated into the query

**Step 3: Add wiki re-indexing.** Unlike `SourceCodeService`, the current `WikiService` indexes only once at startup. Add a periodic re-index using the hot-swap pattern from Step 2:

```csharp
private Timer? _reindexTimer;

public void StartReindexTimer()
{
    // Re-index every 10 minutes; IndexWikiFiles() handles the hot-swap safely
    _reindexTimer = new Timer(_ => IndexWikiFiles(), null, 
        TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
}
```

Or implement `IHostedService` like `SourceCodeService` does.

**Step 4: Expose `category` parameter on `WikiSearchTool.cs`:**

The `WikiService.GetRelevantContext` already accepts a `categoryFilter` parameter, but `WikiSearchTool` doesn't expose it. Add it:

```json
{
    "type": "object",
    "properties": {
        "query": { "type": "string", "description": "The search terms" },
        "category": { "type": "string", "description": "Optional. Filter by category (e.g., 'monster', 'item', 'spell', 'class')" },
        "max_results": { "type": "integer", "description": "Maximum articles to return (default 3)" }
    },
    "required": ["query"]
}
```

---

## Phase 2: New Tools (Effort: Medium)

### 2.1 `get_constants` — Game Constant & Define Lookup

**Purpose:** Instant lookup of `#define` constants and enum values from the GnollHack source, including the generated headers (`pm.h`, `onames.h`).

**Why needed:** The AI constantly needs to resolve `PM_GNOLL`, `WAN_DEATH`, `AD_FIRE`, `MR_FIRE`, `MAXULEV`, etc. Currently it must do a source code search and hope the result isn't truncated. A dedicated pre-indexed lookup would be instant.

**New file:** `Overseer/Services/Tools/GetConstantsTool.cs`

**Parameter schema:**
```json
{
    "type": "object",
    "properties": {
        "name": { "type": "string", "description": "Constant name or wildcard pattern (e.g., 'PM_GNOLL', 'AD_*', 'WAN_*')" },
        "prefix_filter": { "type": "string", "description": "Optional. Filter by prefix (e.g., 'PM_', 'AD_', 'WAN_', 'ART_')" }
    },
    "required": ["name"]
}
```

**Implementation approach:**
- During `SourceCodeService.IndexRepository()`, parse all `.h` files for `#define NAME value` lines and `enum { NAME = value, ... }` blocks
- Store in a `ConcurrentDictionary<string, ConstantInfo>` where `ConstantInfo` has: name, value, file, line number, and any inline comment
- The tool does a simple dictionary lookup or prefix scan
- Wildcard patterns (`AD_*`) scan by prefix

**Tool guide (`Overseer/ToolGuides/get_constants.md`):**
```markdown
Look up #define constants and enum values from the GnollHack source code.

Fast O(1) lookup. Use this instead of source_code_search when you need to
resolve a specific constant name (e.g., PM_GNOLL, WAN_DEATH, AD_FIRE).

Supports wildcard patterns: "AD_*" returns all attack damage type constants.
Use prefix_filter for broader category browsing: prefix_filter="PM_" lists
all monster indices.

Constants from generated headers (pm.h, onames.h) are included when the
server-side makedefs pipeline is configured.
```

---

### 2.2 `wiki_view` — Direct Wiki Article Retrieval

**Purpose:** Retrieve a specific wiki article by filename or title, without going through keyword search scoring.

**Why needed:** When the AI already knows which article it wants (from a previous search or from its training knowledge), forcing it through the keyword search is wasteful and unreliable. A direct retrieval avoids scoring failures.

**New file:** `Overseer/Services/Tools/WikiViewTool.cs`

**Parameter schema:**
```json
{
    "type": "object",
    "properties": {
        "article": { "type": "string", "description": "Article filename or title (fuzzy matched, e.g., 'potion', 'gnoll', 'valkyrie')" },
        "section": { "type": "string", "description": "Optional. Specific section heading to extract (e.g., 'Strategy', 'Stats')" }
    },
    "required": ["article"]
}
```

**Implementation:**
- Fuzzy-match the article name against the wiki index (case-insensitive substring match on filename)
- If `section` is provided, parse the article for markdown headings (`#`, `##`, etc.) and extract only that section
- Return the full article or section content

**Tool guide (`Overseer/ToolGuides/wiki_view.md`):**
```markdown
View a specific wiki article by name. Use when you already know which
article you want. Faster and more reliable than wiki_search for known articles.

Use section parameter to extract a specific section (e.g., section: "Strategy").
```

---

### 2.3 `search_definitions` — Find Symbol Definitions

**Purpose:** Find where a function, macro, type, or struct is *defined* (not just used). Returns the definition site with context.

**Why needed:** The current `source_code_search` returns all occurrences. When the AI searches for `hitmu`, it gets 50+ results — but it usually wants the *definition*. This tool uses heuristics to find and return only the definition.

**New file:** `Overseer/Services/Tools/SearchDefinitionsTool.cs`

**Parameter schema:**
```json
{
    "type": "object",
    "properties": {
        "symbol": { "type": "string", "description": "Symbol name to find the definition of" },
        "kind": { "type": "string", "enum": ["function", "macro", "type", "struct", "enum", "any"], "description": "Optional. Type of symbol (default 'any')" }
    },
    "required": ["symbol"]
}
```

**Implementation approach:**
- Search indexed files using regex patterns:
  - **Function definition:** `^<name>\s*\(` at the start of a line (in `.c` files — not `.h`, to avoid prototypes). GnollHack uses the classic C style where the return type is on a **separate line** above the function name (e.g., `void\ndo_makedefs(char *options)`), so a single-line `<return_type>\s+<name>` pattern won't work. Match the function name at line start instead, then include the preceding line (return type) in the context output.
  - **Macro:** `#define\s+<name>[\s(]`
  - **Struct:** `struct\s+<name>\s*\{`
  - **Typedef:** `typedef\s+.*\s+<name>\s*;`
  - **Enum:** `enum\s+<name>\s*\{` or `<name>\s*=\s*\d+` inside an enum block
- Return the definition with surrounding context (enough to see the full function signature or macro body)
- If multiple matches, prioritize `.c` file definitions over `.h` prototypes

**Tool guide (`Overseer/ToolGuides/search_definitions.md`):**
```markdown
Find where a function, macro, struct, or type is defined in the source code.

Unlike source_code_search (which finds ALL occurrences), this tool finds
only the definition site. Use it when you need to understand what a function
does, what a macro expands to, or what fields a struct has.

Defaults to kind="any" which tries all symbol types. Specify kind for precision.
```

---

## Phase 3: Source Search Enhancements (Effort: Low-Medium)

### 3.1 Add `case_sensitive` Parameter to Source Code Search

**File:** `Overseer/Services/Tools/SourceCodeSearchTool.cs` and `Overseer/Services/SourceCodeService.cs`

Add a `case_sensitive` boolean parameter (default `false` for backwards compatibility). When `true`, use `StringComparison.Ordinal` instead of `StringComparison.OrdinalIgnoreCase`, and for regex use `RegexOptions.None` instead of `RegexOptions.IgnoreCase`.

**Why:** Searching for `PM_GNOLL` (a C #define) should not also match `pm_gnoll` (which doesn't exist). Case-sensitive search improves precision for constant lookups.

### 3.2 Add `whole_word` Parameter to Source Code Search

Add a `whole_word` boolean parameter (default `false`). When `true` and `is_regex` is `false`, wrap the query in word-boundary markers: internally convert to regex `\b<query>\b`. **Important:** Use `Regex.Escape(query)` before wrapping, to prevent regex injection from special characters in the search term (e.g., searching for `C++` or `sizeof(int)`).

**Why:** Searching for `eat` currently also matches `death`, `create`, `feature`, `meat`, etc. Word-boundary matching fixes this.

### 3.3 Add Match Highlighting

In `SourceCodeService.SearchFiles()`, prefix lines that contain the actual match with a marker (e.g., `>>>`) so the AI can quickly distinguish matched lines from context lines in dense results.

```csharp
// Instead of (current code at line ~285 in SourceCodeService.cs):
resultSb.AppendLine($"{i + 1}: {result.Document.ContentLines[i]}");

// Use a prefix marker when line i is in the match set (group is the current List<int> of match line indices):
string prefix = group.Contains(i) ? ">>> " : "    ";
resultSb.AppendLine($"{prefix}{i + 1}: {result.Document.ContentLines[i]}");
```

### 3.4 Add `search_in_file` to Source Code View

**File:** `Overseer/Services/Tools/SourceCodeViewTool.cs`

Add an optional `search_term` parameter. When provided (and `start_line` is omitted), find the first occurrence of the search term in the file and return context around it.

**Why:** The AI frequently wants to say "show me the `potionhit` function in `src/potion.c`" without knowing the line number. Currently this requires a search-then-view round-trip (2 tool calls).

```json
{
    "type": "object",
    "properties": {
        "file": { "type": "string", "description": "File path relative to repo root" },
        "start_line": { "type": "integer", "description": "Starting line number" },
        "line_count": { "type": "integer", "description": "Lines to view (default 50, max 1000)" },
        "search_term": { "type": "string", "description": "Optional. Find this term and show context around it (alternative to start_line)" }
    },
    "required": ["file"]
}
```

Make `start_line` no longer required — if `search_term` is provided, `start_line` is derived from the first match.

### 3.5 Return Top 2–3 Results from NetHack Wiki

**File:** `Overseer/Services/Tools/NetHackWikiSearchTool.cs`

Currently returns only the #1 MediaWiki search result. Sometimes this is a disambiguation page or a less relevant article. Change to return the top 2–3 results (with titles as headers) so the AI can choose the most relevant one.

Add a `max_results` parameter (default 1 for backwards compatibility, max 3).

---

## Phase 4: Advanced Tools (Effort: High)

> [!WARNING]
> **C Parsing Complexity:** Several tools in this phase rely on "brace tracking" to extract function bodies or macro blocks. **Naive brace counting will fail** when encountering braces inside string literals (`"{}"`), character literals (`'{'`), single-line comments (`// {`), or block comments (`/* { */`). The implementation needs a rudimentary **lexer** that skips over strings, character literals, and comments before counting braces. This is not a full C parser — just a state machine with states: `Normal`, `InString`, `InChar`, `InLineComment`, `InBlockComment`. Without this, the tools will produce incorrect results on real GnollHack code.

### 4.1 `get_function_definition` — Full Function Extractor

**Purpose:** Given a function or macro name, find its complete definition body.

**Difference from `search_definitions` (Phase 2):** `search_definitions` returns the definition line + context. `get_function_definition` extracts the *entire function body* by tracking brace depth, which can be many lines.

**Parameter schema:**
```json
{
    "type": "object",
    "properties": {
        "name": { "type": "string", "description": "Function or macro name" },
        "type": { "type": "string", "enum": ["function", "macro", "struct", "any"], "description": "Optional (default 'any')" }
    },
    "required": ["name"]
}
```

**Implementation:** Find the definition line (same as `search_definitions`), then:
- For functions: track `{` and `}` brace depth starting from the opening `{` until depth returns to 0 — **using the lexer described above** to skip braces in strings/comments
- For macros: follow `\` line continuations until a line without `\`
- For structs: track braces like functions

---

### 4.2 `get_monster_stats` — Structured Monster Data

**Purpose:** Parse `src/monst.c` and return structured monster statistics directly.

**Why:** The current `monster_lookup` tool just does wiki keyword search. The AI needs precise stats (base level, speed, AC, MR, attacks, resistances, flags) that are definitively defined via `MON()` macros in `src/monst.c`.

**Implementation:** Parse `MON()` macro blocks from the indexed `src/monst.c` content during indexing. The fields are well-defined:
- Monster name, symbol, difficulty, level, speed, AC, MR
- Attack tuples (AT_xxx, AD_xxx, damage)
- Weight, nutrition, sound
- Resistances, conveyed resistances
- Monster flags (M1_*, M2_*, M3_*)

All parsing is done in Overseer C# code — **no changes to makedefs or any build utility**.

> [!NOTE]
> This is complex due to multi-line `MON()` macros with nested macro references (e.g., `LVL(5, 12, 2, 20, -3)`, `ATTK(AT_CLAW, AD_PHYS, 1, 6)`). Use a **balanced-parenthesis extraction** algorithm (with the lexer to handle strings/comments) to extract each `MON(...)` block, then parse the comma-separated fields positionally. Consider building the parser incrementally: start with name + basic stats, add attack tuples and flags later.

---

### 4.3 `get_item_stats` — Structured Item Data

Same concept as `get_monster_stats` but parsing `WEAPON()`, `ARMOR()`, `POTION()`, `SCROLL()`, `WAND()`, `RING()`, etc. macro blocks from `src/objects.c`. All parsing in Overseer C# code. Same lexer-aware parenthesis extraction applies.

---

### 4.4 `get_call_graph` — Simple Call Graph Extraction

**Purpose:** Given a function name, identify which functions it calls (callees) and which functions call it (callers).

**Parameter schema:**
```json
{
    "type": "object",
    "properties": {
        "function_name": { "type": "string", "description": "Function to analyze" },
        "direction": { "type": "string", "enum": ["callers", "callees", "both"], "description": "Optional (default 'both')" },
        "depth": { "type": "integer", "description": "Optional. Trace depth (default 1)" }
    },
    "required": ["function_name"]
}
```

**Implementation:** Regex-based approximation:
- **Callees:** Find the function body (using the lexer-aware brace tracking from 4.1), extract `\b\w+\s*\(` patterns from within it, filtering out keywords (`if`, `for`, `while`, `switch`, `return`, `sizeof`)
- **Callers:** Search all files for references to the function name in a call context

This is imperfect without a real C parser, but a regex-based approximation is still useful for understanding code flow.

**Lowest priority** — implement only if the other phases are complete.

---

## Summary: Files to Create/Modify

### New Files
| File | Phase | Description |
|---|---|---|
| `Overseer/Services/Tools/GetConstantsTool.cs` | 2 | Constant/define lookup tool |
| `Overseer/Services/Tools/WikiViewTool.cs` | 2 | Direct wiki article retrieval |
| `Overseer/Services/Tools/SearchDefinitionsTool.cs` | 2 | Symbol definition finder |
| `Overseer/Services/Tools/GetFunctionDefinitionTool.cs` | 4 | Full function body extractor |
| `Overseer/Services/Tools/GetMonsterStatsTool.cs` | 4 | Structured monster data parser |
| `Overseer/Services/Tools/GetItemStatsTool.cs` | 4 | Structured item data parser |
| `Overseer/Services/Tools/GetCallGraphTool.cs` | 4 | Call graph extractor |
| `Overseer/ToolGuides/get_constants.md` | 2 | Tool guide |
| `Overseer/ToolGuides/wiki_view.md` | 2 | Tool guide |
| `Overseer/ToolGuides/search_definitions.md` | 2 | Tool guide |
| `Overseer/ToolGuides/get_function_definition.md` | 4 | Tool guide |
| `Overseer/ToolGuides/get_monster_stats.md` | 4 | Tool guide |
| `Overseer/ToolGuides/get_item_stats.md` | 4 | Tool guide |
| `Overseer/ToolGuides/get_call_graph.md` | 4 | Tool guide |

### Modified Files
| File | Phase | Changes |
|---|---|---|
| `Overseer/ToolGuides/monster_lookup.md` | 0 | Fix misleading description |
| `Overseer/ToolGuides/item_lookup.md` | 0 | Fix misleading description |
| `Overseer/ToolGuides/_policy.md` | 0 | Reword line 25 — remove false claim that lookup tools "draw from the same game data" |
| `Overseer/appsettings.json` | 0, 1 | Bump truncation default; add makedefs config keys |
| `Overseer/Program.cs` | 2, 4 | Add `AddSingleton<IToolHandler, ...>()` lines for each new tool |
| `Overseer/Services/SourceCodeService.cs` | 1, 2, 3 | makedefs pipeline; constant indexing; match highlighting; case/word params |
| `Overseer/Services/WikiService.cs` | 1 | Replace with Lucene.NET BM25 implementation; add re-indexing |
| `Overseer/Overseer.csproj` | 1 | Add Lucene.NET NuGet packages (currently only has `Microsoft.AspNetCore.SpaProxy`; `Google.Cloud.AIPlatform.V1` is in the main `MobileGnollHackLogger.csproj`) |
| `Overseer/Services/Tools/SourceCodeSearchTool.cs` | 3 | Add case_sensitive, whole_word parameters |
| `Overseer/Services/Tools/SourceCodeViewTool.cs` | 3 | Add search_in_file parameter |
| `Overseer/Services/Tools/WikiSearchTool.cs` | 1 | Add category parameter |
| `Overseer/Services/Tools/NetHackWikiSearchTool.cs` | 3 | Add max_results, return top 2–3 |

### Tool Registration
All new tools must implement the `IToolHandler` interface and be **explicitly registered** in `Program.cs` as:
```csharp
builder.Services.AddSingleton<Overseer.Services.Tools.IToolHandler, NewToolClass>();
```
The `ToolRegistry` receives all registered `IToolHandler` implementations via constructor injection (`IEnumerable<IToolHandler>`) and automatically loads the corresponding tool guide from `ToolGuides/{ToolName}.md`. No manual `Register()` calls are needed — but the DI registration line in `Program.cs` **is** required.

> [!IMPORTANT]
> **`IToolHandler` interface (actual):** The plan's Phase 2 code samples show a simplified interface. The actual interface in [`IToolHandler.cs`](file:///c:/hmp/MobileGnollHackLogger/Overseer/Services/Tools/IToolHandler.cs) uses:
> - `string ToolName` (not `Name`)
> - `string Description { get; set; }` (set by `ToolRegistry` from the `.md` guide file)
> - `ToolExecutionLocation ExecutionLocation` (Server or Client)
> - `ToolCategory Category`
> - `JsonElement ParameterSchema` (JSON schema, not a `GetParameters()` method)
> - `Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)`
> - `bool RequiresConfirmation => false;` (default interface member — override if the tool needs user confirmation)
> - `int TimeoutSeconds => 15;` (default interface member — override for long-running tools)
>
> New tool implementations must match this interface, not the simplified signatures shown in the Phase 2 parameter schema examples. The parameter schemas shown in this plan are correct as JSON but should be provided via the `ParameterSchema` property as a `JsonElement`.

---

## Recommended Implementation Order

1. **Phase 0** — Quick fixes (< 1 hour)
2. **Phase 1.1** — makedefs pipeline (standalone, no dependencies)
3. **Phase 1.2** — BM25 wiki search (standalone, no dependencies)
4. **Phase 2.1** — `get_constants` (benefits from Phase 1.1 — generated headers make this much more useful)
5. **Phase 2.2** — `wiki_view` (benefits from Phase 1.2 — Lucene index)
6. **Phase 2.3** — `search_definitions`
7. **Phase 3** — Source search enhancements (all independent)
8. **Phase 4** — Advanced tools (if time permits)
