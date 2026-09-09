---
name: server_tool_parameter_reference
description: >-
  Per-tool parameter and result contract for every Overseer AI chat tool -- what to check
  when asking "what parameters does source_code_search take", "why did this tool return
  nothing", "which tools can a benchmark run call", "what does wiki_search's category
  filter actually match against", or "is get_item_stats supposed to have no stats field".
  Covers, tool by tool: required and optional parameters, defaults and clamps and the
  config key each comes from, which corpus or service the tool reads, the shape of a
  successful result, per-tool failure modes, which 16 tools Benchmark:AllowedTools
  permits, and how to tell a correct empty result from a broken one (a guard-message
  failure during indexing vs. an ordinary "not found" once indexed). This is the contract
  layer the diagnostic method in server_benchmark_tool_diagnostics checks a call against;
  read it whenever a tool call's parameters or result need to be judged correct or wrong,
  not just present or absent.
---

# Overseer Tool Parameter Reference

This is a lookup document, not a narrative. Every fact in it was read out of the tool's
`ToolDefinition`/`ParameterSchema` (what the model may pass) and its handler body (what the
server actually does with it, including defaults, clamps and failure modes) — not inferred
from the tool's name or description. Where this document's account of a tool differs from
what its guide text in `Overseer/ToolGuides/<tool_name>.md` says, the guide text is what the
model was told; this document is what the code does. A mismatch between the two is itself a
finding, and belongs in `server_benchmark_to_chat_transfer` §2 (Harness Defect) or §7 rung 3
(Tool Descriptions), not silently resolved in either direction.

---

## 1. How to Use This With the Method Skill

This file states what a call **should** look like — the parameter contract, the defaults, the
result shape, the documented failure modes. [`server_benchmark_tool_diagnostics`](../server_benchmark_tool_diagnostics/SKILL.md)
states **how to test** whether a specific call did: the five verdicts for a tool's call
population, the replay-fidelity tiers, and the diagnostic table a tool-layer pass produces.
Use this document to build the "expected" column; use that one to compare it against what a
benchmark run or chat session actually recorded.

> 🛑 **The single most important distinction in this whole reference**: a tool guarded by
> `ToolGuardMessages` because its backing service is still indexing returns `Success = false`.
> Once indexing completes, an ordinary miss — no matching article, no matching monster, no
> matching file — returns `Success = true` with a "not found" message in `Content`, or (for the
> three structured stats tools) a populated `error` field **inside a successful JSON body**.
> Conflating these two is the single easiest way to misdiagnose a tool-layer finding: a
> `Success = false` count includes both "the corpus was still warming up" and "the tool itself
> is broken," and only the guard-message text distinguishes them. See §6 for the stats-tool
> variant of this trap, which inverts it one step further.

---

## 2. Every Tool at a Glance

**Real counts, established by reading `Overseer/Services/Tools/` and `Program.cs`, not by
trusting the file list in a prompt**: the directory holds 32 `.cs` files. Eleven are
infrastructure (`ToolRegistry.cs`, `ToolDefinition.cs`, `ToolExecutor.cs`, `ToolBatchRunner.cs`,
`ToolBatchResultBudget.cs`, `ToolGuardMessages.cs`, `IToolHandler.cs`, `IClientToolBridge.cs`,
`NullClientToolBridge.cs`, `SignalRClientToolBridge.cs`, `ExternalToolHosts.cs`). The remaining
21 files define tool handlers: 20 files each implement `IToolHandler` directly (one server tool
per file), and one file — `ClientToolHandlers.cs` — defines 10 client tool classes deriving from
the shared `ClientToolHandlerBase`. `Program.cs` registers exactly 30 `IToolHandler`
implementations (28 consecutive `AddSingleton<IToolHandler, …>` lines plus 2 more for the GitHub
tools), confirming 20 server + 10 client with none missing and none duplicated.

| Tool | Location | Reads | In `Benchmark:AllowedTools`? | Purpose |
|---|---|---|---|---|
| `source_code_search` | Server | `SourceCodeService` / `NetHackSourceCodeService` | Yes | Keyword/regex search across indexed C source, ranked by match count per file |
| `source_code_view` | Server | same | Yes | View a file by line range or by centering on a search term |
| `search_definitions` | Server | same | Yes | Find a symbol's definition line(s) with context |
| `get_function_definition` | Server | same | Yes | Extract a full function/macro/struct body by brace-tracking |
| `get_constants` | Server | same | Yes | Look up `#define` constants / enum members by name or wildcard |
| `list_indexed_files` | Server | same | Yes | List indexed files, optionally substring-filtered |
| `wiki_search` | Server | `WikiService` (Lucene/BM25) | Yes | Search the GnollHack wiki, return per-article snippets |
| `wiki_view` | Server | `WikiService` | Yes | Fetch one GnollHack wiki article (optionally one section) |
| `nethack_wiki_search` | Server | `NetHackWikiService` (Lucene/BM25) | Yes | Search the NetHack wiki, return full article bodies |
| `nethack_wiki_view` | Server | `NetHackWikiService` | Yes | Fetch one NetHack wiki article (optionally one section) |
| `monster_lookup` | Server | `WikiService` | Yes | Wiki lookup for a monster, category-scoped with unfiltered fallback |
| `item_lookup` | Server | `WikiService` | Yes | Wiki lookup for an item, category-scoped with unfiltered fallback |
| `get_monster_stats` | Server | `SourceCodeService` (`src/monst.c`) | Yes | Structured monster stats, Level 1 → Level 2 fallback |
| `get_item_stats` | Server | `SourceCodeService` (`src/objects.c`) | Yes | **Level 2 only** — raw macro dump, never structured stats |
| `get_artifact_stats` | Server | `SourceCodeService` (`include/artilist.h`) | Yes | Structured artifact stats, Level 1 → Level 2 fallback |
| `get_knowledge_article` | Server | `KnowledgeBaseService` | Yes | Exact topic-key lookup of a curated article |
| `search_github` | Server | `GitHubApiService` (network) | **No** | Search GitHub issues/PRs or commits |
| `get_github_repo_info` | Server | `GitHubApiService` (network) | **No** | Repo summary, commits, issues, PRs, releases, or one issue's detail |
| `search_server_dumplogs` | Server | `ApplicationDbContext` + `DumpLogPath` | **No** | Search recent players' dumplog files for a term |
| `delegate_to_subagent` | Server | `SubAgentCatalogService` / `AgentLoopRunner` | **No** | Run a specialized subagent turn and fold its findings back in |
| `get_full_message_history` | Client | GnollHack client (SignalR bridge) | **No** | Full in-session message log |
| `get_directory_listing` | Client | same | **No** | Directory listing on the client device |
| `refresh_snapshot` | Client | same | **No** | Force a fresh game-state snapshot upload |
| `get_save_info` | Client | same | **No** | Info about one save file on the device |
| `get_player_library` | Client | same | **No** | A discovered in-game manual's text |
| `get_oracle_consultations` | Client | same | **No** | A received Oracle consultation's text |
| `get_player_xlog` | Client | same | **No** | The client's local xlog (past runs) |
| `get_player_dumplogs` | Client | same | **No** | Dumplogs stored on the client device |
| `get_app_log` | Client | same | **No** | The client application log |
| `get_panic_log` | Client | same | **No** | The client panic/crash log |

> 🛑 **Client tools cannot appear in a benchmark run, and not primarily because of a network
> check.** `SignalRClientToolBridge.IsClientConnected` is hardcoded `true` ("We assume true for
> v2. The timeout will catch disconnected clients." — the class's own comment), so
> `ToolRegistry`'s connection gate never actually excludes anything at that layer. What excludes
> client tools is that `AgentRunRequest.EnableClientTools` is a plain `bool` that every benchmark
> call site in `BenchmarkService.cs` leaves unset (defaults to `false`), and separately none of
> the 10 client tool names appear in `Benchmark:AllowedTools`. In a live chat session with a real
> GnollHack client attached, `EnableClientTools` can be `true` and a call will actually round-trip
> over SignalR; without a client attached, the request will time out (`TimeoutSeconds`, 15s
> default, 30s for `refresh_snapshot`) rather than fail fast, because nothing server-side detects
> the absence of a live client. Either way — excluded from the benchmark, or attempted with no
> client attached — their absence proves nothing about tool quality.

---

## 3. `Benchmark:AllowedTools` — the 16 That Can Appear

From `Overseer/appsettings.json`:

```json
"Benchmark": {
  "AllowedTools": [
    "wiki_search", "wiki_view", "get_knowledge_article",
    "nethack_wiki_search", "nethack_wiki_view",
    "monster_lookup", "item_lookup",
    "get_monster_stats", "get_item_stats", "get_artifact_stats",
    "get_constants", "get_function_definition", "search_definitions",
    "source_code_search", "source_code_view", "list_indexed_files"
  ]
}
```

`BenchmarkService.cs` reads this list (falling back to an identical hardcoded
`_defaultAllowedTools` if the key is absent) and passes it as `AllowedToolNames` into
`ToolRegistry.BuildToolsForRequest`, which filters the function declarations the model even
sees. A tool outside this list is never offered to the model in a benchmark turn, so it never
executes — **no benchmark finding may rest on one of these 14 tools' absence**:
`search_github`, `get_github_repo_info`, `search_server_dumplogs`, `delegate_to_subagent`, and
all 10 client tools. If a report shows zero calls to any of those, that is expected behavior,
not a routing defect.

---

## 4. Source-Code Family

All six tools share a `repository` parameter: `"gnollhack"` (default) or `"nethack"`, resolved
per-call by an identical `ResolveService` helper in each handler. **A `repository: "nethack"`
call reads `NetHackSourceCodeService`, a corpus `BenchmarkRun` does not fingerprint** (only
`SourceCodeHeadSha` for the GnollHack side is recorded — see
[`server_tool_data_sources`](../server_tool_data_sources/SKILL.md)). Each handler guards on
`service.IsIndexingComplete`, returning `ToolGuardMessages.SourceCodeIndexingInProgress` or
`.NetHackSourceCodeIndexingInProgress` (`Success = false`) while warming up.

| Tool | Required | Optional | Defaults / clamps | Config key |
|---|---|---|---|---|
| `source_code_search` | `query` | `file_filter`, `max_results`, `is_regex`, `whole_word`, `case_sensitive`, `filenames_only`, `context_lines`, `repository` | `max_results` default 10, **clamped 1–100 in `SearchFiles` regardless of what's passed**; `context_lines` default 5, clamped 0–25 | `Tools:source_code_search:MaxResults`, `Tools:source_code_search:ContextLines` |
| `source_code_view` | `file`, and either `start_line` or `search_term` | `line_count`, `repository` | `line_count` default 50, clamped 1–1000 | `Tools:source_code_view:LineCount` |
| `search_definitions` | `symbol` | `kind` (`function`\|`struct`\|`macro`\|`enum`\|`type`\|`any`), `repository` | `kind` default `any`; result capped at **10 matches, hardcoded**, not configurable | none |
| `get_function_definition` | `name` | `type` (`function`\|`macro`\|`struct`\|`any`), `start_line` (continuation offset), `repository` | chunk size 150 lines | `Tools:get_function_definition:MaxLinesPerChunk` |
| `get_constants` | `name` **or** `prefix_filter` (handler requires at least one; schema only requires `name`) | `prefix_filter`, `repository` | result capped at **100 constants, hardcoded** | none |
| `list_indexed_files` | none | `path_filter`, `repository` | none | none |

**Result shape and ranking** (`SourceCodeService.SearchFiles`): matches are grouped by file,
files are **ordered by descending match-line count and then `Take(maxResults)`** — `max_results`
caps files, not individual matches, but files *are* ranked, contrary to a "no ranking" reading of
this tool. Within each file, matching lines within `contextLines * 2` of each other are grouped;
only the first 5 groups per file are shown, with `[... N additional match groups in this file
hidden ...]` for the rest.

> 🛑 **`source_code_search`'s `query` is a single literal substring, matched per line.**
> `SourceCodeService.SearchFiles` (`Overseer/Services/SourceCodeService.cs:451-584`) tests each
> indexed line with `doc.ContentLines[i].Contains(query, comparison)` (`:498`), where `comparison`
> is `OrdinalIgnoreCase` unless `case_sensitive: true`. Every consequence follows from that one
> line:
>
> - The query is **never split into terms** and never stemmed. `"layer glyph rendering"` matches
>   only a line containing that exact 21-character run — not a line about layers and another about
>   glyphs.
> - It **cannot span a newline**, so a phrase broken across two source lines is unmatchable.
> - **File and symbol names are not searched by `query` at all.** `file_filter` is the only thing
>   that looks at a path; `filenames_only: true` changes how matches are *reported*, not what is
>   matched.
> - Exact spacing and punctuation are load-bearing: `"m_shot.n ="` misses a line written
>   `m_shot.n=` or `m_shot.n  =`.
>
> **The two fallbacks do not rescue an ordinary miss.** `SourceCodeSearchTool.cs:144-163` retries
> only in two situations — case-insensitively after a `case_sensitive: true` miss, and literally
> after an `is_regex: true` miss. A plain multi-word or misspelled-identifier miss triggers
> neither.
>
> **A miss returns `Success = true` with the 30-character string `"No relevant source code found."`**
> (`SourceCodeSearchTool.cs:165-168`) and **no near-miss information** — no "did you mean", no
> partial-term hit count, nothing to tell the model that its phrasing rather than the corpus was
> the problem. So a model that guesses an identifier gets an answer indistinguishable from
> "the game does not contain this", and its cheapest recovery is to guess again. That is a
> **latency and cost** failure mode, not a correctness one; see
> [`server_benchmark_tool_diagnostics`](../server_benchmark_tool_diagnostics/SKILL.md) § 4, the
> empty-result cascade.
>
> **A 30-character result is a miss; a short result is not.** A successful `filenames_only: true`
> probe legitimately returns 25–250 characters (`src/makemon.c (29 matches)`). Counting "results
> under 100 characters" as misses conflates the two and overstates the miss rate — run 28's own
> rows show 16 `source_code_search` results under 100 characters on two questions, of which 12
> were the 30-character miss string and 4 were successful `filenames_only` probes.

The whole result is then truncated to `MaxSourceResultLength` (root config key, **100000 at
`Overseer/appsettings.json:17`**; `SourceCodeSearchTool.cs:30` reads it and falls back to the same
value in code) with an `[... output truncated ...]` marker, *before* `ToolExecutor`'s own per-tool
`MaxResultLength` cap ever applies — `source_code_search` truncates twice.

> 🛑 **`MaxSourceResultLength` is effectively dead, and its informative suffix never reaches the
> model.** `ToolExecutor` (`Overseer/Services/Tools/ToolExecutor.cs:252-282`) cuts a plain-text
> result at `ToolExecutionContext.MaxResultLength` — **10,000** by default
> (`IToolHandler.cs:55`; `appsettings.json:26` for the chat default and `:220` for the benchmark
> value, both 10000) — and `SourceCodeSearchTool` declares no `MaxResultLengthOverride`, so
> nothing raises that ceiling for it. A result long enough to hit the 100,000 cap therefore has
> `SourceCodeService.cs:578-581`'s suffix — *"[Additional matches not shown — refine your query or
> use source_code_view]"* — appended at character 100,000 and then removed by the 10,000-character
> cut, which appends `... [Result truncated for length]` instead. **The model is never told to
> refine its query**, in a benchmark run or in chat. Raising `MaxSourceResultLength` changes
> nothing; only `MaxResultLength` or a handler override would.

> 🛑 **A regex compile error returns `Success = true`.** `SearchFiles` catches an invalid regex
> and returns the string `"Error: Invalid regular expression. …"` as ordinary content; the tool
> handler wraps whatever `SearchFiles` returns into `ToolResult { Success = true, Content = … }`
> unconditionally (the only `Success = false` paths are the indexing guard and the missing-query
> check). A `ToolCallSummary` count of "successful" `source_code_search` calls can include calls
> that failed to compile their own regex.

**Fallback behavior**: if `case_sensitive: true` yields nothing, the tool silently retries
case-insensitively and prefixes a note; if that also fails and `is_regex: true`, it retries as a
literal string search with a note. `whole_word: true` (when `is_regex` is not already true)
rewrites `query` to `\b<escaped query>\b` and forces regex mode.

**`get_function_definition` extraction**: macros are read by following `\`-continuation lines;
functions and structs use `CLexer.ExtractBracedBlock` (brace-tracking, matching the tool's own
description); if brace extraction fails, it falls back to a flat 10-line window. Truncation
produces `[Output truncated at line X of Y. Call again with start_line=X to continue.]` — an
informational continuation, not a failure.

**`search_definitions` / `get_function_definition` matching is line-pattern, not a C parser.**
Function/macro/struct/type matches are anchored regexes against a single line
(`^{symbol}\s*\(`, `^\s*#define\s+{symbol}[\s(]`, etc.); an enum-member match additionally
requires an `enum ` token somewhere in the **preceding 50 lines** as a heuristic proxy for "am I
inside an enum body" — this can both false-negative (member more than 50 lines past the `enum`
keyword) and false-positive (an unrelated `enum ` string within the lookback window).

**Failure modes across the family**: missing required parameter → `Success = false` with a
`"Missing … parameter"` message; `source_code_view`'s `file` containing `..` or resolving outside
the configured repository root → `Success = false`; a disallowed extension (only `.c .h .des
.txt` normally; `.cs .xaml` only when `context.OverseerMode == 2` **and** `repository` is
`gnollhack`) → `Success = false`; `get_constants` / `get_function_definition` / `search_definitions`
finding nothing → `Success = true` with an explanatory "No … found" `Content`, **not** a failure.

---

## 5. Wiki Family

Both wiki services build an in-memory Lucene.NET 4.8 index (`RAMDirectory`, `StandardAnalyzer`,
`BM25Similarity`) at startup and hot-swap it on reindex; both guard on `IsIndexingComplete`
(`ToolGuardMessages.WikiIndexingInProgress` / `.NetHackWikiIndexingInProgress`, `Success = false`
while warming up). **An empty hit list from any of these four tools is a tokenization/analyzer
question before it is an absence question** — `StandardAnalyzer` lowercases and strips stop
words, `QueryParserBase.Escape` runs before parsing, and a `ParseException` is swallowed into an
empty result rather than surfaced as an error.

| Tool | Required | Optional | Defaults / clamps | Config key |
|---|---|---|---|---|
| `wiki_search` | `query` | `category`, `max_results` | `max_results` default 5, **no clamp/cap** — `maxResults > 0 ? maxResults : 5` is passed straight to Lucene's hit count | `Tools:wiki_search:MaxResults`, `Tools:wiki_search:PerResultChars` (2500) |
| `wiki_view` | `article` | `section` | none | none |
| `nethack_wiki_search` | `query` | `namespace_filter` (`article`\|`source`\|`category`\|`forum`\|`help`\|`nethackwiki`), `max_results` | `max_results` default **3** (tool-level, distinct from the config ceiling below), clamped `1..max(1, configured)` | `Tools:nethack_wiki_search:MaxResults` (5) |
| `nethack_wiki_view` | `article` | `section` | none | none |

> 🛑 **`wiki_search`'s `category` filter is not a stored taxonomy field.** It compiles to
> `new WildcardQuery(new Term("path", "*{categoryFilter}*"))` — a substring match against the
> indexed file's **full filesystem path**. It only narrows results if the wiki repository happens
> to keep monster/item/spell articles under a directory or filename containing that substring.
> `monster_lookup` and `item_lookup` rely on exactly this mechanism (§6) and are built to degrade
> gracefully — `nethack_wiki_search`'s `namespace_filter`, by contrast, is a real
> `TermQuery` against a `namespace` field parsed from each file's YAML-style frontmatter
> (default `"article"` when a file has no frontmatter or no `namespace:` key) — the two "category"
> concepts are not the same mechanism despite the similar name.

**`wiki_view` / `nethack_wiki_view` article resolution is a single Lucene hit, not real fuzzy
matching.** Both run a `MultiFieldQueryParser` over `title`/`filename`, take `hits.ScoreDocs[0]`
unconditionally when `TotalHits > 0`, and return that document — there is no relevance floor, so
a garbled or ambiguous `article` string can silently return the wrong article rather than a "not
found" result. `section` extraction is markdown-heading text match (`^(#+)\s+(.*)`, case-
insensitive full-title equality), capturing lines until a heading of equal or shallower depth; if
the named section isn't found, the **full article** is returned with a
`[Section 'X' not found in article. Returning full text.]` prefix — not an error.

**Result shape**: `wiki_search` returns per-hit snippets via `WikiSnippetExtractor.BuildSnippet`
(bounded to `PerResultChars`, query-term-aware); `nethack_wiki_search` returns **full article
bodies** with no per-result character cap of its own (bounded only by the generic per-tool
`MaxResultLength` truncation in `ToolExecutor`). An empty result from any of the four is
`Success = true` with a "No relevant information found" / "article … not found" `Content` —
never a failure once indexing is complete.

---

## 6. Structured Lookup Family

Two tools sit on `WikiService`; three parse the GnollHack C sources directly. Both groups take
only a bare `name` — no wildcards, no repository selector.

**`monster_lookup` / `item_lookup`** — `name` (required) only. Both call
`WikiService.GetRelevantContext(name, "monster")` / `(name, "item")` — the same path-substring
category mechanism as `wiki_search` §5 — and if that returns nothing, **silently retry
unfiltered** before finally returning `Success = true, Content = "No information found for
monster/item: {name}"`. This double fallback makes both tools resilient to the wiki repository
not actually organizing articles under a `monster`/`item` path segment.

**`get_monster_stats` / `get_item_stats` / `get_artifact_stats`** — `name` (required, exact as
written in the source: `src/monst.c`, `src/objects.c`, `include/artilist.h` respectively) only.
All three guard on `SourceCodeService.IsIndexingComplete`
(`ToolGuardMessages.SourceCodeIndexingInProgress`). All three serialize a
`StatsResponse<T>` (`Overseer/Services/SourceCodeModels.cs`):

```
stats               // T? (MonsterStats/ItemStats/ArtifactStats — a bare Dictionary<string,object> via [JsonExtensionData])
raw_definition       // string?
flag_descriptions    // Dictionary<string,string> — empty {} rather than omitted when unset
macro_definitions    // Dictionary<string,string> — likewise
struct_definitions   // Dictionary<string,string> — likewise
error                // string?
message              // string?
```

> 🛑 **A miss is reported inside a successful JSON body, not as a tool failure.** When
> `src/monst.c` isn't indexed, or the name doesn't match, the handler sets `response.Error` and
> returns it — the *tool* still returns `ToolResult { Success = true, Content = <that JSON> }`.
> This is the stats-tool variant of the §1 callout, one layer deeper: here even a fully-warmed,
> fully-working corpus reports "not found" as `error` inside a 200-shaped payload, so neither
> `Success` nor the presence of an `error` field alone tells you whether the corpus was reachable
> — you have to read the JSON.

**Truncation** (`Tools:get_monster_stats` / `get_item_stats` / `get_artifact_stats`, each with
`TruncationThreshold` 9900 and `HardLimit` 10000): if the serialized JSON exceeds the threshold,
the handler re-serializes a minified version — dropping `flag_descriptions` if only `Stats` was
populated ("Level 1" minification), or dropping `macro_definitions`/`struct_definitions` if
`RawDefinition` was populated ("Level 2" minification) — and if *that* still exceeds
`HardLimit`, the tool returns `Success = false` with an over-the-limit error instead.

**The Level 1 → Level 2 *parsing* fallback (distinct from the truncation minification above)
exists only for monsters and artifacts, not items:**

- `get_monster_stats` and `get_artifact_stats` attempt a real positional-token parse of the
  macro call (Level 1: populates `Stats.Fields` and calls `PopulateFlagDescriptions`). On any
  exception during that parse, `SourceCodeService` logs `_logger.LogWarning(ex, "Level 1 parsing
  failed for monster/artifact '{Name}', falling back to Level 2.")` and instead returns
  `RawDefinition` plus the relevant `MacroDefinitions`/`StructDefinitions` context — silently, from
  the caller's point of view, beyond the shape of the JSON.
- **`get_item_stats` has no Level 1 at all.** The code's own comment is explicit: `/* Level 2 for
  items — raw dump + context (structured parsing requires per-macro handlers) */`. Every
  successful `get_item_stats` call returns `RawDefinition` + macro/struct context and a `Message`
  telling the caller to interpret the raw macro invocation itself; `Stats` is never populated and
  no "Level 1 failed" warning is ever logged for items, because there is no Level 1 to fail. A
  diagnostic expecting `stats` on `get_item_stats` is diagnosing the wrong contract.

---

## 7. Knowledge Base, GitHub, Dumplogs, Delegation

**`get_knowledge_article`** — `topic` (required, string). `KnowledgeBaseService.GetArticle` is an
**exact, case-insensitive dictionary key lookup** (`Dictionary<string, KnowledgeArticle>(
StringComparer.OrdinalIgnoreCase)`) — not prefix, not fuzzy. A miss is a genuine tool failure:
`Success = false` with `ErrorMessage` listing every available topic key (`GetAvailableTopics()`),
unlike the stats tools' in-payload `error` pattern in §6. Guards on
`ToolGuardMessages.KnowledgeBaseIndexingInProgress` while the KB git repo is still loading.

**`search_github`** (`query`, `search_type` [`issues`\|`commits`] required; `repo_filter`,
`state_filter`, `sort`, `count` optional, count default 10/max 30 per the schema description but
**not enforced by clamping in the handler** — it is passed through to `GitHubApiService`) and
**`get_github_repo_info`** (`owner`, `repo`, `info_type` required; `info_type: "issue_detail"`
additionally requires `issue_number`) are `ToolCategory.ExternalLookup`, `TimeoutSeconds = 20`
(overriding the 15s interface default), throttled process-wide by
`ToolExecutionLimits:MaxProcessExternalLookupCalls` (default 3, shared across **both** GitHub
tools and any other `ExternalLookup`-category tool). `GitHub:PersonalAccessToken` may be empty —
`GitHubApiService` simply omits the `Authorization` header when it is, making unauthenticated
GitHub API calls that still succeed but hit GitHub's much lower unauthenticated rate limit; an
empty token is not itself a tool failure.

**`search_server_dumplogs`** — `search_term` (required); `max_results` optional, default 3,
clamped `1..Tools:search_server_dumplogs:MaxResults` (5). Requires `DumpLogPath` (User Secret) to
be configured, else `Success = false`. Scans the most recent `GameLog` rows in
`BatchSize` (100) chunks, ordered by `Id` descending, for up to `MaxBatches` (5) batches — i.e. up
to 500 most-recent games — stopping as soon as `max_results` matches are found; each excerpt is
the first case-insensitive match plus `ContextPadding` (100) characters on each side. A dumplog
file that doesn't exist on disk, or a read error on one file, is silently skipped, not a tool
failure.

**`delegate_to_subagent`** — `agent_name` (required; the schema's `enum` is generated **per
request** from `SubAgentCatalogService.GetEnabledSubAgents()`, so the allowed values are whatever
subagents are currently enabled, not a fixed list) and `task` (required); `context`,
`subagent_name` optional. Gated on `context.EnableSubAgents`, subagent recursion depth
(`AgentDepth < MaxAgentDepth`), and a budget check (`context.Budget.TryStartSubAgent`); throttled
by its own `SubAgent`-category semaphore (`SubAgentSettings:MaxProcessParallelSubAgents`, 12) with
a bounded queue wait (`SubAgentSettings:SubAgentQueueWaitSeconds`, 30s) before returning a
capacity error rather than blocking indefinitely.

> 🛑 **Nested subagent tool calls inflate the parent turn's recorded call count.** The subagent's
> own `ToolCalls` list comes back as `ToolResult.NestedToolCalls`
> (`DelegateToSubAgentTool.cs`), and `AgentLoopRunner` (`:565-578`) appends every one of those
> nested calls to the **same** `streamToolCalls` list the parent turn is building, tagging each
> with `ParentToolCallId` and an incremented `Depth`. One `delegate_to_subagent` call can therefore
> silently add N more entries to a session's tool-call history. This is harmless for benchmark
> analysis specifically because `delegate_to_subagent` is outside `Benchmark:AllowedTools` (§3)
> and `EnableSubAgents` is never set for a benchmark request — but it is real and undercounted-
> looking-normal in a live chat session where subagents are enabled.

---

## 8. Cross-Cutting Limits

These apply above and across individual tool contracts. The batching/throttle **mechanism** —
concurrency tiers, the `Channel<ToolBatchOutcome>` stream, budget allocation order — is owned by
[`tool_execution_architecture`](../tool_execution_architecture/SKILL.md); this section lists only
the config keys relevant to reading a tool's *output*, and where each is read.

| Key | Default | Read in | Applies to |
|---|---|---|---|
| `MaxSourceResultLength` (root-level) | 100000 (code fallback; key absent from `appsettings.json`, so any override is a User Secret) | `SourceCodeSearchTool` constructor | Only `source_code_search`'s own pre-truncation of its concatenated multi-file result, before the generic per-tool cap below ever runs |
| `AiPerformanceSettings:MaxResultLength:Default` | 10000 (Min 1000 / Max 100000, user-adjustable) | `ChatService.cs` when building `ToolExecutionContext` | Generic per-tool-call cap, enforced in `ToolExecutor.ExecuteAsync` step 4: a JSON-shaped result (starts with `{`/`[`) over the cap becomes a `Success = false` "Result too large" error instead of being substring-truncated (to avoid emitting invalid JSON); a plain-text result is hard-truncated with `... [Result truncated for length]` |
| `ToolExecutionLimits:MaxBatchResultLength` | 40000 | `AgentLoopRunner.cs` (`:482`) | One `ToolBatchResultBudget` per tool-call **batch** (one iteration's parallel tool calls), scaled to `Math.Max(this, ToolExecutionContext.MaxResultLength)`; a tool whose `MaxResultLengthOverride` already exceeds the context max (only `refresh_snapshot`, 60200) is exempted from this budget entirely |
| `ToolExecutionLimits:MaxTurnResultLength` | 120000 | `AgentLoopRunner.cs` (`:123`) | A cumulative ceiling across **all** tool-call batches within one model turn (multiple iterations of the tool loop), distinct from and layered above the per-batch budget |

Per-handler `MaxResultLengthOverride` (`IToolHandler`) is `null` for every tool except
`refresh_snapshot`, which floors its effective cap at 60200 chars specifically so the client
snapshot's tail sections (Discoveries, dungeon overview) aren't silently lost to an arbitrary cut
— see the comment in `ClientToolHandlers.cs`.

---

## 9. Cross-References

- [`server_benchmark_tool_diagnostics`](../server_benchmark_tool_diagnostics/SKILL.md) — the
  diagnostic method that consumes this reference: the five call-population verdicts, replay
  fidelity tiers, and the diagnostic table a tool-layer pass produces
- [`server_tool_data_sources`](../server_tool_data_sources/SKILL.md) — the corpora themselves:
  their paths, what each index silently excludes by size or absence, and which corpora a
  benchmark run fingerprints
- [`server_benchmark_to_chat_transfer`](../server_benchmark_to_chat_transfer/SKILL.md) — the
  triage a tool-layer finding feeds into, including the Corpus/Environment Defect category
- [`tool_execution_architecture`](../tool_execution_architecture/SKILL.md) — the batching,
  throttle and truncation **mechanism** referenced, not restated, in §8
- [`background_indexing_architecture`](../background_indexing_architecture/SKILL.md) — the
  tool→service→guard map and the cold/warm state matrix behind every `IsIndexingComplete` check
  in §§4–7
