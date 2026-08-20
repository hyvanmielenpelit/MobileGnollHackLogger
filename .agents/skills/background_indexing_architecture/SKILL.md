---
name: background_indexing_architecture
description: Documentation of Overseer's background indexing architecture, asynchronous initialization lifecycle, directive tool error degradation messaging (ToolGuardMessages), and test synchronization patterns using InitializationTask.
---

# Background Indexing Architecture for Overseer

This document details the background indexing architecture implemented in the Overseer ASP.NET Core & Angular application, including asynchronous service warm-up, runtime directive tool error degradation messaging, thread-safety mechanisms, and test synchronization patterns.

---

## 1. Architectural Overview & Context

Overseer uses several indexing services to provide semantic search, documentation lookup, and source code navigation to the AI assistant:
- **`WikiService`**: Indexes the GnollHack-specific wiki (`WikiPath`, default `c:\wiki`) into an in-memory Lucene.NET 4.8 BM25 index.
- **`NetHackWikiService`**: Indexes the NetHack community wiki (`NetHackWikiPath`) with frontmatter metadata into an in-memory Lucene.NET 4.8 BM25 index.
- **`KnowledgeBaseService`**: Parses curated Markdown guides in `KbPath/Content` into an in-memory topic dictionary.
- **`SourceCodeService`**: Background hosted service (`IHostedService`) that parses C source code, macros, constants, and function definitions.

### Asynchronous Startup Principle
- **No Blocking I/O in Constructors**: Services must **never** perform blocking disk scans, file parsing, or index construction directly inside their constructors.
- **Instant Activation**: Constructors kick off parsing work via `Task.Run()` and return in **< 1ms**. This prevents ASP.NET Core dependency injection from blocking request threads (e.g., when `ChatController` activates on `GET /api/chat/sessions`).
- **Proactive Warm-up**: In `Program.cs`, singletons are resolved during `app.Lifetime.ApplicationStarted` so background indexing begins immediately upon server boot:
  ```csharp
  app.Lifetime.ApplicationStarted.Register(() =>
  {
      _ = app.Services.GetService<WikiService>();
      _ = app.Services.GetService<NetHackWikiService>();
      _ = app.Services.GetService<KnowledgeBaseService>();
      _ = app.Services.GetService<Overseer.Services.Tools.ToolRegistry>();
  });
  ```

---

## 2. Service Status Flags & Properties

Every indexed service exposes two key properties:

### 1. `InitializationTask` (For Test Synchronization)
```csharp
public Task InitializationTask { get; private set; }
```
- Holds the initial background indexing `Task`.
- Used in unit and integration tests to deterministically `await service.InitializationTask;` before executing assertions.

### 2. `IsIndexingComplete` (For Tool Pre-Flight Guards)
```csharp
// WikiService, NetHackWikiService, KnowledgeBaseService:
public bool IsIndexingComplete => InitializationTask?.IsCompleted ?? false;

// SourceCodeService (IHostedService):
private volatile bool _isIndexingComplete = false;
public bool IsIndexingComplete => _isIndexingComplete;
// Set to true after IndexRepository() finishes in StartAsync()
```
- Returns `false` while the initial indexing pass is running.
- Returns `true` once the initial indexing pass has finished (whether successful or failed due to missing directory).
- **Does NOT flicker**: Periodic 10-minute re-indexing cycles do not reset `IsIndexingComplete` to `false`.

---

## 3. Tool Degradation Messaging: Cold vs Not-Found

All 16 information-retrieval tools implement an early-return guard at the top of `ExecuteAsync` returning a directive error (`Success = false`) using standardized messages in `ToolGuardMessages`:

```csharp
if (!_service.IsIndexingComplete)
{
    return Task.FromResult(new ToolResult 
    { 
        Success = false, 
        ErrorMessage = ToolGuardMessages.<Category>IndexingInProgress 
    });
}
```

### Directive Guidance for LLMs
The error message explicitly informs the LLM that the service is initializing in the background, directs it **not to retry** the tool within the current turn (preventing infinite retry loops), and guides it to answer from general knowledge or notify the user that data is warming up.

### State Separation Matrix

| System State | Condition | Tool Result Status | Error Message / Content | Meaning to LLM / Client |
|---|---|---|---|---|
| **Cold Startup / Warm-up** | `!service.IsIndexingComplete` | `Success = false` | `ToolGuardMessages.*` ("...service initialization in progress... Do not retry this tool in this turn...") | Service is initializing. LLM is directed not to retry and to answer from general knowledge or inform user. |
| **Indexing Complete, Unmatched** | `service.IsIndexingComplete && results.Empty` | `Success = true` | `"No relevant information found..."`, `"Article not found..."`, etc. | Data was fully searched and genuinely does not exist in the repository. |

### Complete Tool-to-Service Dependency Map

| Tool Name | Tool Class | Underlying Service | Guard Property | Guard Error Message |
|---|---|---|---|---|
| `wiki_search` | `WikiSearchTool` | `WikiService` | `_wikiService.IsIndexingComplete` | `ToolGuardMessages.WikiIndexingInProgress` |
| `wiki_view` | `WikiViewTool` | `WikiService` | `_wikiService.IsIndexingComplete` | `ToolGuardMessages.WikiIndexingInProgress` |
| `monster_lookup` | `MonsterLookupTool` | `WikiService` | `_wikiService.IsIndexingComplete` | `ToolGuardMessages.WikiIndexingInProgress` |
| `item_lookup` | `ItemLookupTool` | `WikiService` | `_wikiService.IsIndexingComplete` | `ToolGuardMessages.WikiIndexingInProgress` |
| `nethack_wiki_search` | `NetHackWikiSearchTool` | `NetHackWikiService` | `_netHackWikiService.IsIndexingComplete` | `ToolGuardMessages.NetHackWikiIndexingInProgress` |
| `nethack_wiki_view` | `NetHackWikiViewTool` | `NetHackWikiService` | `_netHackWikiService.IsIndexingComplete` | `ToolGuardMessages.NetHackWikiIndexingInProgress` |
| `get_knowledge_article` | `KnowledgeBaseTool` | `KnowledgeBaseService` | `_knowledgeBaseService.IsIndexingComplete` | `ToolGuardMessages.KnowledgeBaseIndexingInProgress` |
| `source_code_search` | `SourceCodeSearchTool` | `SourceCodeService` | `_sourceCodeService.IsIndexingComplete` | `ToolGuardMessages.SourceCodeIndexingInProgress` |
| `source_code_view` | `SourceCodeViewTool` | `SourceCodeService` | `_sourceCodeService.IsIndexingComplete` | `ToolGuardMessages.SourceCodeIndexingInProgress` |
| `get_function_definition` | `GetFunctionDefinitionTool` | `SourceCodeService` | `_sourceCodeService.IsIndexingComplete` | `ToolGuardMessages.SourceCodeIndexingInProgress` |
| `search_definitions` | `SearchDefinitionsTool` | `SourceCodeService` | `_sourceCodeService.IsIndexingComplete` | `ToolGuardMessages.SourceCodeIndexingInProgress` |
| `get_constants` | `GetConstantsTool` | `SourceCodeService` | `_sourceCodeService.IsIndexingComplete` | `ToolGuardMessages.SourceCodeIndexingInProgress` |
| `get_monster_stats` | `GetMonsterStatsTool` | `SourceCodeService` | `_sourceCodeService.IsIndexingComplete` | `ToolGuardMessages.SourceCodeIndexingInProgress` |
| `get_item_stats` | `GetItemStatsTool` | `SourceCodeService` | `_sourceCodeService.IsIndexingComplete` | `ToolGuardMessages.SourceCodeIndexingInProgress` |
| `get_artifact_stats` | `GetArtifactStatsTool` | `SourceCodeService` | `_sourceCodeService.IsIndexingComplete` | `ToolGuardMessages.SourceCodeIndexingInProgress` |
| `list_indexed_files` | `ListIndexedFilesTool` | `SourceCodeService` | `_sourceCodeService.IsIndexingComplete` | `ToolGuardMessages.SourceCodeIndexingInProgress` |

---

## 4. Periodic Re-Indexing & Change Detection

To avoid heavy periodic CPU and disk I/O load, services do NOT blindly rebuild indices on timers:

### 1. Git Repository Fingerprinting (`WikiService`, `KnowledgeBaseService`, `SourceCodeService`)
- Services that map to Git repositories poll the local repository `.git/HEAD` commit SHA every 10 minutes via `GitHelper.GetGitHeadSha()`.
- If the HEAD commit SHA matches `_lastGitSha`, the re-indexing pass is completely skipped (zero CPU/disk overhead).
- When a new commit SHA is detected (e.g. after a `git pull`), the service triggers re-indexing in the background and updates its SHA fingerprint.
- **Atomic Hot-Swap**:
  - `WikiService`: Builds a new `RAMDirectory` and `IndexSearcher` in isolation, then atomically swaps references inside `lock (_swapLock)`. Superseded readers are disposed outside the lock.
  - `KnowledgeBaseService`: Parses articles into a new dictionary and swaps the reference via `Interlocked.Exchange`.

### 2. Startup-Only Indexing (`NetHackWikiService`)
- `NetHackWikiService` indexes the NetHack community wiki files (located in `C:\hmp\nethackwiki`) **only once at application startup**.
- **No Background Timer**: It does NOT create or run periodic 10-minute timers.
- **Rationale**: NetHackWiki consists of thousands of static markdown files that change very seldomly via manual file uploads. Running periodic scans on thousands of files introduces unnecessary CPU and disk I/O load.
- **Restart Requirement**: Whenever NetHackWiki files are updated on disk, the Overseer site/service must be restarted for the changes to take effect.

---

## 5. Test Suite Guidelines & Synchronization Rules

When writing unit or integration tests for any indexed service or tool:

1. **Always use `async Task` test methods**:
   ```csharp
   [Fact]
   public async Task Tool_Executes_ReturnsData()
   ```

2. **Always `await service.InitializationTask;`** before calling query methods or tools:
   ```csharp
   using var service = new NetHackWikiService(config);
   await service.InitializationTask; // Ensures background indexing is complete
   ```

3. **Testing the Unindexed Guard**:
   To test that a tool returns `Success = false` with the directive guard error message, construct the service and immediately invoke the tool **without** awaiting `InitializationTask`.

---

## 6. Checklist for New Services or Tools

When creating a new background-indexed service or tool:
- [ ] Do NOT perform disk I/O in the service constructor; use `Task.Run()`.
- [ ] Expose `public Task InitializationTask { get; private set; }`.
- [ ] Expose `public bool IsIndexingComplete => InitializationTask?.IsCompleted ?? false;`.
- [ ] Add the `if (!_service.IsIndexingComplete)` guard in the tool's `ExecuteAsync`.
- [ ] Implement thread-safe query null checks (e.g. `_searcher == null`) as defense-in-depth.
- [ ] Register proactive warm-up in `Program.cs` under `app.Lifetime.ApplicationStarted`.
- [ ] In unit tests, await `service.InitializationTask`.
