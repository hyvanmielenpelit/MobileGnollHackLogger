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
- **`SourceCodeService`**: Background hosted service (`IHostedService`) that generates the GnollHack headers with makedefs and parses C source code, macros, constants, and function definitions. `StartAsync` loads the flag descriptions, starts the first pass on the thread pool and returns at once, so Kestrel starts listening without waiting for makedefs or the index. `NetHackSourceCodeService` derives from it with `SupportsMakedefs => false`. The full pipeline is in `docs/overseer/gnollhack-source-indexing.md`.

### The Corpus Job Runner (`WikiService`, `NetHackWikiService`, `KnowledgeBaseService`)
- Each of the three corpus services owns one `CorpusIndexJobRunner` (`Overseer/Services/CorpusIndexing/`), composed rather than inherited. Every pass of that corpus — startup, the 10-minute Git check, and the Services tab's **Re-index** / **Reload articles** — goes through it.
- **The gate** is a flag guarded by a lock, not a semaphore. While a pass runs, a `Manual` request is refused (`CorpusIndexStartResult.AlreadyRunning`, HTTP 409 `alreadyRunning`); an automatic request (`Startup`, `RepositoryChange`) queues at most **one** `FollowUp` pass, which runs the most recent request's delegate, and gets back a task that completes when that follow-up ends.
- **No pass task ever faults.** An exception is recorded as a `Failed` job (first line of the message, at most 500 characters) and logged once; cancellation on `Dispose` is `Canceled`; a pass may also call `CorpusPassContext.Fail(message)`. The runner keeps the running job with its live phase and file counts (`ReadingFiles`, `BuildingIndex`, `Publishing`) and the last 20 finished jobs, newest first.
- Each service publishes its statistics (`CorpusIndexInfoDto`: generation, HEAD and branch read **before** the files, file count, bytes, size-skipped and unreadable files, fingerprint prefix) in the **same swap** as the searcher or article set and `ContentFingerprint`, and reports them with `GetStatus()` to the Services tab dashboards (`docs/overseer/corpus-dashboards.md`).

### Asynchronous Startup Principle
- **No Blocking I/O in Constructors**: Services must **never** perform blocking disk scans, file parsing, or index construction directly inside their constructors.
- **Instant Activation**: Constructors start the first pass on the thread pool — the three corpus services through `CorpusIndexJobRunner.TryStart(Startup, …)` — and return in **< 1ms**. This prevents ASP.NET Core dependency injection from blocking request threads (e.g., when `ChatController` activates on `GET /api/chat/sessions`).
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
- In the three corpus services it is the job runner's **startup pass**: it completes when that pass has ended and its job is recorded in the history, it does **not** wait for a follow-up pass the startup pass queues, and it **never faults** — a failing startup pass (for example *"The WikiPath folder does not exist."*) still completes it, and the failure is the job's outcome.
- `NetHackWikiService`'s `startIndexing: false` test constructor starts no pass and records no job: `InitializationTask` is an already completed task and `IsIndexingComplete` stays `false` for the service's whole life.

### 2. `IsIndexingComplete` (For Tool Pre-Flight Guards)
```csharp
// WikiService, KnowledgeBaseService:
public bool IsIndexingComplete => InitializationTask?.IsCompleted ?? false;

// NetHackWikiService (false for a service built with startIndexing: false):
public bool IsIndexingComplete => _startIndexing && (InitializationTask?.IsCompleted ?? false);

// SourceCodeService (IHostedService):
private volatile bool _isIndexingComplete = false;
public bool IsIndexingComplete => _isIndexingComplete;
// Set to true when the first pass that StartAsync() starts in the background ends
```
- Returns `false` while the initial indexing pass is running.
- Returns `true` once the initial indexing pass has finished (whether successful or failed due to missing directory).
- **Does NOT flicker**: Periodic 10-minute re-indexing cycles and manual re-index passes do not reset `IsIndexingComplete` to `false`; the previous index keeps serving until the new one is published.
- **`SourceCodeService.InitializationTask` never faults**: the pass catches every exception and records it as the job's outcome, so a test that awaits it after a failing pass gets a completed task and `IsIndexingComplete == true`. Its first pass includes the makedefs check, so on a machine whose generated headers are stale the guard holds until makedefs has been built and run.

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
| `wiki_overview` | `WikiOverviewTool` | `WikiService` or `NetHackWikiService`, by its `wiki` argument | That service's `IsIndexingComplete` | That corpus's guard message, because `CorpusToolUsageService` counts guards by exact string |
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
- Each compares the HEAD SHA with the `HeadSha` of its **published** index, read **before** the pass read the files. A pull that lands during a pass therefore gets its own follow-up pass, and a pass that failed publishes nothing, so the next check retries it instead of waiting for the next commit.
- If the HEAD SHA matches the published one, the re-indexing pass is completely skipped (zero CPU/disk overhead).
- When a new commit SHA is detected (e.g. after a `git pull`), the service requests a `RepositoryChange` pass. In `WikiService` and `KnowledgeBaseService` that request goes **through the job runner's gate**, so a timer pass never overlaps a running one: it queues the single follow-up instead.
- A manual **Re-index** (wiki) or **Reload articles** (knowledge base) reads the working tree as it is, uncommitted edits included; the timer itself only follows new commits.
- **Atomic Hot-Swap**:
  - `WikiService`: Builds a new `RAMDirectory` and `IndexSearcher` in isolation, then atomically swaps them, with the analyzer, the fingerprint and the published statistics, inside `lock (_swapLock)`. Superseded readers are disposed outside the lock. A file that cannot be read is listed under the unreadable files and skipped; the pass goes on.
  - `KnowledgeBaseService`: Parses articles into a new dictionary and swaps it (`Interlocked.Exchange`) together with the published statistics and `ContentFingerprint` inside `lock (_swapLock)`.
  - `SourceCodeService`: Each pass reads every file and parses the game data into a new immutable `SourceIndexSnapshot` (`Services/SourceIndexing/`: documents, constants, a new `GameDataParser` and `ObjectsMacroResolver`, fingerprint, HEAD SHA, branch, `Generation`) and publishes it with one `Volatile.Write`. Every query method reads the reference once and uses that snapshot to the end, so the chat keeps answering from the previous index during a rebuild and never sees a partial one. A failed or canceled pass publishes nothing. `IndexGeneration` (0 before the first publish) is the snapshot's generation; `SourceLivenessIndex` keys its cache on it rather than on HEAD.

### 1a. One Gate for Source Passes (`SourceCodeService`)
- Start-up, the 10-minute poll and the Services tab buttons (`RequestReindex`, `RequestMakedefsBuild`, `CancelCurrentJob`) all run through one `SemaphoreSlim(1,1)`. An automatic trigger that finds it taken sets a pending flag, so at most one follow-up pass is queued; a manual request is refused (*already running*, HTTP 409).
- Before reading, a pass waits for a quiet repository: no `.git/index.lock`, and HEAD reading the same twice 3 s apart, for at most 2 minutes.
- **makedefs runs only when its inputs changed.** In GnollHack (`SupportsMakedefs`), `MakedefsHeaderGenerator` hashes makedefs' inputs (`MakedefsInputSet`) and compares them, and the four generated headers, with the stamp `overseer-makedefs-stamp.json` beside the executable. Unchanged → no process is started; changed → `MakedefsBuildCommand` (timeout `MakedefsBuildTimeoutSeconds`, default 300) and three makedefs runs (`MakedefsRunTimeoutSeconds`, default 60) through `IExternalProcessRunner`, which kills the whole process tree on a timeout. A makedefs failure never stops the pass: the headers on disk are indexed as they are.
- `GetStatus()` reports the published index, the makedefs state, the running job and the last 20 jobs for the GnollHack Source dashboard (`api/admin/devtools/source/status`).

### 2. No Automatic Re-Indexing (`NetHackWikiService`)
- `NetHackWikiService` indexes the NetHack community wiki files (located in `C:\hmp\nethackwiki`) **once at application startup**, and again only when an administrator clicks **Re-index** on Admin → Services → NetHack Wiki (`RequestReindex(Manual)`, `POST api/admin/devtools/nethack-wiki/reindex`). Its status reports `RefreshMode = StartupAndManual` and no next check.
- **No Background Timer**: It does NOT create or run periodic 10-minute timers, file watchers or Git checks.
- **Rationale**: NetHackWiki consists of thousands of static markdown files that change very seldomly via manual file uploads. Running periodic scans on thousands of files introduces unnecessary CPU and disk I/O load.
- **Restart or Re-index**: Whenever NetHackWiki files are updated on disk, the Overseer site/service must be restarted, or a Re-index run, for the changes to take effect. The manual pass keeps the parallel chunked build and swaps the new index in when it is complete.

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
   `SourceCodeService` and `NetHackSourceCodeService` start their first pass in `StartAsync`, so await `InitializationTask` **after** `await service.StartAsync(...)`; a synchronous call site uses `.GetAwaiter().GetResult()`.

3. **Testing the Unindexed Guard**:
   To test that a tool returns `Success = false` with the directive guard error message, construct the service and immediately invoke the tool **without** awaiting `InitializationTask`. For the NetHack wiki, the `startIndexing: false` constructor holds the guard deterministically (§ 2).

---

## 6. Checklist for New Services or Tools

When creating a new background-indexed service or tool:
- [ ] Do NOT perform disk I/O in the service constructor; use `Task.Run()`, or a `CorpusIndexJobRunner` when the corpus needs re-index requests and a job history.
- [ ] Expose `public Task InitializationTask { get; private set; }`.
- [ ] Expose `public bool IsIndexingComplete => InitializationTask?.IsCompleted ?? false;`.
- [ ] Add the `if (!_service.IsIndexingComplete)` guard in the tool's `ExecuteAsync`.
- [ ] Implement thread-safe query null checks (e.g. `_searcher == null`) as defense-in-depth.
- [ ] Register proactive warm-up in `Program.cs` under `app.Lifetime.ApplicationStarted`.
- [ ] In unit tests, await `service.InitializationTask`.
