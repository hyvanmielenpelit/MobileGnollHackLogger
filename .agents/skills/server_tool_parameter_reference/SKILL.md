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
| `get_item_stats` | Server | `SourceCodeService` (`src/objects.c` + `include/objclass.h`) | Yes | Structured item stats, Level 1 → Level 2 fallback |
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
| `source_code_view` | `file` | `start_line`, `search_term`, `line_count`, `repository` | `start_line` defaults to 1 when neither it nor `search_term` is given; `line_count` default 50, clamped 1–1000; the excerpt stops at the last whole line that fits under `context.MaxResultLength` (10,000 by default — the same cap `ToolExecutor` applies) and appends `[Output truncated at line X of Y requested (file line N). Call again with start_line=N+1 to continue.]`, from the run-38 round (2026-09-11); about 150–200 lines of C fit under the cap | `Tools:source_code_view:LineCount` |
| `search_definitions` | `symbol` | `kind` (`function`\|`struct`\|`macro`\|`enum`\|`type`\|`any`), `repository` | `kind` default `any`; result capped at **10 matches, hardcoded**, not configurable | none |
| `get_function_definition` | `name` | `type` (`function`\|`macro`\|`struct`\|`any`), `start_line` (1-based; **omit, or pass 0, to start at the beginning** — from the run-36 round, 2026-09-11; otherwise where to resume: the 1-based output line the truncation notice names, **or** an absolute file line inside the header's L-range; any other out-of-range value returns an explicit message, never a clamp), `repository` | chunk size 150 lines; a requested `type` with **zero** matches falls back to `any` and returns the other kind's definition behind `[No {kind} named '{name}' in the indexed {repository} source; showing the {found} definition instead.]`, from the run-38 round (2026-09-11) — a name with both a function and a macro still returns the function when `function` is asked for | `Tools:get_function_definition:MaxLinesPerChunk` |
| `get_constants` | `name` **or** `prefix_filter` (handler requires at least one; schema only requires `name`) | `prefix_filter`, `repository` | result capped at **100 constants, hardcoded** | none |
| `list_indexed_files` | none | `path_filter`, `repository` | none | none |

**Result shape and ranking** (`SourceCodeService.SearchFiles`): matches are grouped by file,
files are **ordered by descending match-line count and then `Take(maxResults)`** — `max_results`
caps files, not individual matches, but files *are* ranked, contrary to a "no ranking" reading of
this tool. Within each file, matching lines within `contextLines * 2` of each other are grouped;
only the first 5 groups per file are shown, with `[... N additional match groups in this file
hidden ...]` for the rest.

> 🛑 **`source_code_search`'s `query` is a single literal substring, matched per line.**
> `SourceCodeService.SearchFiles` (`Overseer/Services/SourceCodeService.cs:470-603`) tests each
> indexed line with `doc.ContentLines[i].Contains(query, comparison)` (`:517`), where `comparison`
> is `OrdinalIgnoreCase` unless `case_sensitive: true`. The tool schema says as much in the
> parameter's own description — *"One literal substring, matched against each source line on its
> own. Not split into terms, cannot span a line break, and does not match file or symbol names.
> Prefer a short distinctive identifier over a phrase."* — so a model that passes a term list is
> going against the schema, not following it. Every consequence follows from that one line:
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
> **The two fallbacks do not rescue an ordinary miss.** `SourceCodeSearchTool.cs:146-165` retries
> only in two situations — case-insensitively after a `case_sensitive: true` miss, and literally
> after an `is_regex: true` miss. A plain multi-word or misspelled-identifier miss triggers
> neither.
>
> **A miss returns `Success = true` and carries near-miss information and a next action.**
> `SourceCodeSearchTool.BuildMissContent` (`SourceCodeSearchTool.cs:188-225`, reached from the
> miss return at `:167-170`) assembles the content from up to four parts, in this order:
>
> 1. `No relevant source code found for '<query>'.` — and, when a `file_filter` was passed,
>    `(file_filter='<filter>' may be excluding the match)` before the period, so the model is
>    pointed at one of its own arguments as a candidate cause.
> 2. For an **identifier-shaped** query (not regex, no whitespace, matching
>    `^[A-Za-z0-9_][A-Za-z0-9_.>\-]*$`): up to two near-neighbour probes, reported as
>    `'<candidate>' matches N lines across <up to 3 paths>.` The candidates are the two longest
>    alphanumeric tokens of at least 3 characters when the identifier has more than one, otherwise
>    — for a query longer than 6 characters — the query minus its last two characters and a
>    half-length prefix. When neither hits: `No shorter form of this identifier matched either.`
> 3. For a **whitespace-bearing** query (not regex): the explicit
>    `No line contains it as one literal substring — spacing matters (e.g. 'a =' and 'a=' do not
>    match).`, then a whitespace-collapsed retry (`With whitespace removed, '<collapsed>' matches
>    …`), then per-term probes of the first three whitespace-separated terms of 2+ characters, run
>    only when at least two qualify and reported as `'<term>' matches …` joined by `; ` — or
>    `None of the individual terms matched either.`
> 4. `Try search_definitions for a known symbol, or list_indexed_files to see what is indexed.` —
>    unconditionally, on every miss.
>
> Every probe goes through `SafeProbe`, which calls `SearchFiles` with `filenames_only`,
> `ProbeMaxResults` **3**, `ProbeMaxResultLength` **1000**, `context_lines` 0, case-insensitive and
> non-regex, and swallows both exceptions and `Error:`-prefixed content into "no hit" — so a miss
> costs at most two extra bounded index scans on the identifier path and four on the whitespace
> path (one collapsed retry plus three terms), and can never itself fail. The whole builder sits
> inside a `catch` returning the bare `"No relevant source code found."`, which is therefore the
> **resolver-defect** payload rather than the ordinary-miss one.
>
> The payload is deliberately short — a few hundred characters, not a report — because **every
> tool result is re-sent to the model on each subsequent round of the same question**, so a
> verbose miss is paid for once per remaining round.
>
> Read a run's misses against the payload of the harness version that produced them. An
> information-free miss leaves a model no way to tell "my phrasing was wrong" from "the game does
> not contain this", and its cheapest recovery is another guess — the **latency and cost** failure
> mode diagnosed as the empty-result cascade in
> [`server_benchmark_tool_diagnostics`](../server_benchmark_tool_diagnostics/SKILL.md) § 4.
>
> **Match the miss payload's prefix, not its length.** The *method* — compare against the tool's
> known miss payload rather than against "short" — stands, but the constant it matches on does
> not. A successful `filenames_only: true` probe legitimately returns 25–250 characters
> (`src/makemon.c (29 matches)`), and the current miss payload runs to a few hundred, so the two
> bands overlap outright and no length threshold separates them. The reliable test is the literal
> opening `No relevant source code found for '`. A stored result that is exactly the
> 30-character `"No relevant source code found."` is either a run recorded before the near-miss
> builder existed or one where the builder threw. Run 28's figures are facts about run 28: 16
> `source_code_search` results under 100 characters on two questions, of which 12 were the
> 30-character miss string and 4 were successful `filenames_only` probes — a "results under 100
> characters" proxy would have reported 16 misses where there were 12.

The whole result is then truncated to `MaxSourceResultLength` (root config key, **100000 at
`Overseer/appsettings.json:17`**; `SourceCodeSearchTool.cs:32` reads it and falls back to the same
value in code) with an `[... output truncated ...]` marker, *before* `ToolExecutor`'s own per-tool
`MaxResultLength` cap ever applies — `source_code_search` truncates twice.

> 🛑 **`MaxSourceResultLength` is effectively dead, and the tool-specific advice it carries never
> reaches the model — but a generic actionable suffix does.** `ToolExecutor`
> (`Overseer/Services/Tools/ToolExecutor.cs:245-285`) cuts a plain-text result at
> `ToolExecutionContext.MaxResultLength` — **10,000** by default (`IToolHandler.cs:55`;
> `appsettings.json:26` for the chat default and `:220` for the benchmark value, both 10000) — and
> `SourceCodeSearchTool` declares no `MaxResultLengthOverride`, so nothing raises that ceiling for
> it. A result long enough to hit the 100,000 cap therefore has `SourceCodeService.cs:599`'s
> suffix — *"[Additional matches not shown — refine your query or use source_code_view]"* —
> appended at character 100,000 and then removed by the 10,000-character cut. **That suffix is
> unreachable: the model is never told to use `source_code_view` by this route, in a benchmark run
> or in chat, and raising `MaxSourceResultLength` changes nothing; only `MaxResultLength` or a
> handler override would.** What arrives in its place is `ToolExecutor.BuildTruncationSuffix`
> (`:301-304`), which names a next action of its own —
> `... [Truncated: showing {shown} of {total} characters. Narrow the query, or ask for a specific
> section, to see the rest.]`. So the two halves of this limit differ in consequence: the
> *tool-specific* recovery is suppressed, while a *generic* "narrow the query" instruction does
> reach the model on every truncated plain-text result.

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
description); if brace extraction fails, it falls back to a flat 10-line window. Output lines map
1:1 onto the file lines in the header's `L{A}-L{B}` range, and the header's line count is `B − A + 1`.
Truncation produces `[Output truncated at line X of Y. Call again with start_line=X+1 (file line
A+X) to continue.]` — an informational continuation, not a failure; both numbers name the next unseen
line, and either can be passed back. `start_line` is resolved as output-relative when it is between 1
and Y, as an absolute file line when it is above Y and inside A–B, and otherwise answered with the
header plus `start_line N is outside this definition: output lines 1–Y, file lines A–B. Call again
with a value in either range.` and **no body line**. A definition near the top of a file, whose file
lines overlap 1–Y, is read as output-relative. Before the run-34 round (2026-09-10) the value was a
0-based output index clamped to the last line, so an absolute file line returned the header alone
(`server_benchmark_tool_diagnostics` § 4).

**`start_line: 0` starts at the beginning, from the run-36 round (2026-09-11).** `0` is never printed
by any truncation notice — the two numbers a notice names are always 1-based — so it cannot be a
stale or mistaken value; it is the 0-based idiom for "from the start," and `GetFunctionBody` now
treats it exactly as an omitted `start_line`. Before the run-36 round, `0` returned the explicit
`start_line 0 is outside this definition: output lines 1–Y, file lines A–B. Call again with a value
in either range.` message, the same as any other out-of-range value; before the run-34 round it was
clamped instead (previous paragraph). A stored `start_line: 0` call that returned that explicit
message therefore dates the run to before 2026-09-11.

> 🛑 **A `get_function_definition` miss returns `Success = true` and, from the run-35 round, carries
> a bounded occurrence probe.** The payload **opens with** `No definition found for '` — which is
> what a reader matches on, never a length; before the round this opening sentence was the whole
> payload, the bare `No definition found for '<name>' of kind '<kind>'.`. From the round it then
> names, from one bounded `filenames_only` probe (max 3 files, 1000 characters, non-regex,
> case-insensitive, exceptions and `Error:`-prefixed content swallowed into "no hit"), where the
> identifier occurs with match counts, or states that it does not occur in the indexed repository;
> then always the guidance that this tool extracts only a body declared under that exact name, so a
> struct member, function pointer or macro alias must be read with `source_code_search` (with
> `context_lines`) on the named file, or `search_definitions` for the symbol it is assigned from.
> The whole payload is capped at **600 characters**, and the builder sits inside a `catch` returning
> the service's bare sentence — which is therefore the **resolver-defect** payload rather than the
> ordinary-miss one, exactly as with `source_code_search`. A stored result that is the bare sentence
> alone is a run recorded **before** the run-35 round, or a call in which the builder threw.

> 🛑 **A `search_definitions` miss returns `Success = true` and, from the run-40 round
> (2026-09-12), carries the same bounded occurrence probe.** The payload **opens with**
> `No definition found for '` — the same opening sentence `SourceCodeService.FindDefinition`
> produces — and is then extended by the shared `SourceMissContentBuilder` with where the symbol
> does occur (one bounded `filenames_only` probe: max 3 files, 1000 characters, non-regex,
> case-insensitive, exceptions and `Error:`-prefixed content swallowed into "no hit") or a
> statement that it does not occur in the indexed repository, followed by this tool's own
> guidance: on a hit, that the symbol occurs but no definition line matched this kind, so try
> `kind: "any"` or `source_code_search` with `context_lines` on the named file; on no hit, to
> check the spelling or use `list_indexed_files` / `source_code_search` with
> `filenames_only: true`. Capped at **600 characters**, builder inside a `catch`. A stored result
> that is the bare sentence alone is a run recorded **before** the run-40 round, or a call in
> which the builder threw — the **resolver-defect** payload, as with `get_function_definition`.

**`search_definitions` / `get_function_definition` matching is line-pattern, not a C parser.**
Function/macro/struct/type matches are anchored regexes against a single line
(`^{symbol}\s*\(`, `^\s*#define\s+{symbol}[\s(]`, etc.); an enum-member match additionally
requires an `enum ` token somewhere in the **preceding 50 lines** as a heuristic proxy for "am I
inside an enum body" — this can both false-negative (member more than 50 lines past the `enum`
keyword) and false-positive (an unrelated `enum ` string within the lookback window).

**The function matcher gained a second alternative, and kind `type`/`any` a closing-brace typedef
alternative, from the run-37 re-run round (2026-09-11).** `SourceCodeService`'s matcher — shared by
`search_definitions` and `get_function_definition` — tries the original NetHack-style `^name\s*\(`
line first; when that misses, it now also tries a same-line-return-type alternative,
`<type tokens> [*]name(`, accepted only when the line's first token is neither `extern` nor a
control keyword (`return`, `else`, `if`, `while`, `for`, `switch`, `case`, `goto`, `sizeof`) and the
line does not end in `;` — the shape that had missed
`void lib_print_glyph(...)` at `win/win32/xpl/libshare/libproc.c:470`. For kinds `type` and `any`,
the matcher also tries a closing-brace typedef alternative, `^\s*\}\s*name\s*;` — the shape that
had missed `} gbuf_entry;` at `src/display.c:161` — whose body locator walks back, bounded at 400
lines, to the nearest `typedef struct|union|enum` opener and returns the whole block; when no
opener is found within the bound, it falls back to the closing line and the usual 10-line window.

**Failure modes across the family**: missing required parameter → `Success = false` with a
`"Missing … parameter"` message (this no longer applies to `source_code_view`'s `start_line` /
`search_term` — `file` is now its only required parameter, and `start_line` defaults to 1 when
neither is given, per the table above); `source_code_view`'s `file` containing `..` or resolving
outside the configured repository root → `Success = false`; a disallowed extension (only `.c .h .des
.txt` normally; `.cs .xaml` only when `context.OverseerMode == 2` **and** `repository` is
`gnollhack`) → `Success = false`; `get_constants` / `get_function_definition` / `search_definitions`
finding nothing → `Success = true` with an explanatory "No … found" `Content`, **not** a failure.

---

## 5. Wiki Family

Both wiki services build an in-memory Lucene.NET 4.8 index (`RAMDirectory`, `EnglishAnalyzer`,
`BM25Similarity`) at startup and hot-swap it on reindex; both guard on `IsIndexingComplete`
(`ToolGuardMessages.WikiIndexingInProgress` / `.NetHackWikiIndexingInProgress`, `Success = false`
while warming up). **An empty hit list from any of these four tools is a tokenization/analyzer
question before it is an absence question** — from harness 24 `EnglishAnalyzer` (Porter stemming)
lowercases, strips stop words **and** stems both the query and the indexed title/body tokens, so
`material` and `materials` match alike (`WikiService.cs`, `NetHackWikiService.cs`; before harness 24
it was `StandardAnalyzer`, which does neither and cost `wiki_search` a title's ×5 boost whenever the
query differed from the title only by inflection — run 39 T1). `QueryParserBase.Escape` runs before
parsing, and a `ParseException` is swallowed into an empty result rather than surfaced as an error.

**`wiki_search` reports how many articles matched, not only how many it returned, from harness 24.**
When the query's total hit count exceeds `max_results`, the tool appends *"[Showing N of M matching
articles — narrow the query, or add a distinctive word from the article's title, to see others.]"*
after the snippets (`WikiSearchTool.cs`, `WikiService.GetRelevantSnippets`'s `totalHits` overload).
Absent on a result whose hit count did not exceed what was returned — never assume M from N alone on
a run before this line existed.

| Tool | Required | Optional | Defaults / clamps | Config key |
|---|---|---|---|---|
| `wiki_search` | `query` | `category`, `max_results` | `max_results` default 5, **clamped `1..max(1, configured)`, from the run-37 re-run round (2026-09-11)** — mirroring `nethack_wiki_search`'s existing clamp; before this round it was `maxResults > 0 ? maxResults : 5` passed straight to Lucene's hit count with no upper bound | `Tools:wiki_search:MaxResults`, `Tools:wiki_search:PerResultChars` (2500) |
| `wiki_view` | `article` | `section` | none | none |
| `nethack_wiki_search` | `query` | `namespace_filter` (`article`\|`source`\|`category`\|`forum`\|`help`\|`nethackwiki`), `max_results` | `max_results` default **3** (tool-level, distinct from the config ceiling below), clamped `1..max(1, configured)` | `Tools:nethack_wiki_search:MaxResults` (5), `Tools:nethack_wiki_search:PerResultChars` (3000, **from harness 18**) |
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

**`nethack_wiki_view` article resolution is still the top Lucene hit over `title`/`filename`, with no
relevance floor — but from the run-36 round (2026-09-11) a non-exact resolution is announced, not
silent.** `NetHackWikiService.GetArticleResolved` still takes `ScoreDocs[0]` of the title/filename
query unconditionally when it has a hit, so the article chosen has not changed and a garbled or
ambiguous `article` string can still resolve to the wrong article — that has not moved. What changed
is the tool layer: `NetHackWikiViewTool` normalises the request and the resolved title alike (trim,
collapse internal whitespace, lowercase) and, when they differ, prepends a resolution line before the
content:

```
[No NetHack wiki article titled 'X'. Showing 'Y'. Other candidates: A; B; C; D.]
```

`Y` is the resolved title; the candidates are up to 4 *other* titles (the resolved title excluded,
duplicates collapsed by the same normalisation), drawn from the top 5 hits of the title/filename
query followed by the top 5 hits of a separate `summary`-field query, in that order, distinct — so a
title only its summary mentions (e.g. `Twoweapon`, whose summary reads *"twoweaponing or two-weapon
combat…"*) can surface as a candidate for a request like *"Two weapon combat"* even though the
article shown is still whatever `ScoreDocs[0]` of the title/filename query names. The whole line is
capped at 600 characters, and the "Other candidates" clause is omitted when none remain. An exact
title hit (after normalisation) carries no line at all — this is an ordinary `Success = true` result
with the article attached, never a miss; see the miss-payload table below.

**`wiki_view` no longer works that way, from the run-30 round.** `WikiService.GetArticle`
normalizes the request first — trims it, converts `\` to `/`, and strips one trailing `.md`,
`.txt` or `.html` extension — then resolves it through three ordered branches:

1. **A name containing `/` is matched exactly, case-insensitively, against a stored `relpath`
   field** (the article's repository-relative path, without extension) — the path form, e.g.
   `Races/Gnoll`. It either hits exactly or falls through to branch 2; it is never a fuzzy match.
2. Otherwise the title/filename query runs as before and takes its top 8 hits, and the handler
   collects those whose `title` equals the normalized request case-insensitively. **Two or more**
   such hits return a disambiguation payload (the miss-payload table below) naming the candidate
   paths — `Success = true`, an ordinary result carrying a next action, not a miss. **Exactly one**
   returns that article.
3. **None** falls through to the old behaviour, unchanged: the top-scoring Lucene hit,
   unconditionally, with **no relevance floor**. This last branch, and only this branch, is where a
   garbled or invented `article` string still silently returns the best-scoring article rather than
   a "not found" result — the caveat that used to cover all of `wiki_view` now covers only this
   branch of it.

So `article` accepts a title, a filename with or without its extension, or a repository-relative
path — the normalization step exists specifically so a name copied from a prior result's header or
a `wiki_search` snippet resolves. Both `wiki_view`'s own result header and `wiki_search`'s snippet
headers now print `--- <repository-relative path with extension> ---` (e.g. `--- Races/Gnoll.md
---`) instead of the bare filename, so that exact path can be copied straight back into `article`
for branch 1.

`section` extraction is a markdown-heading text match (`^(#+)\s+(.*)`), capturing lines until a
heading of equal or shallower depth. **Both tools resolve a `section` through one shared
extractor, `MarkdownSectionExtractor.Extract`** (from the run-33 round; before it,
`nethack_wiki_view` matched full-title equality only and its miss line carried no heading list), in
three passes over the article's headings:

1. **Exact** — case-insensitive equality on the trimmed heading text.
2. **Normalised** (from the run-31 round on `wiki_view`) — equality after normalising both sides,
   where normalising strips every *leading* character that is not a Unicode letter or digit and
   collapses internal whitespace runs to one space. So `section: "Elbereth"` reaches
   `## 🔮 Elbereth`, and so does `section: "🔮 Elbereth"`. 12.7 % of the GnollHack wiki's headings
   carry such a prefix, which is why the pass exists. Stripping is leading-only and the exact pass
   runs first, so an article carrying both `## Notes` and `## 📝 Notes` still answers `Notes` with
   the exact one.
3. **Unique substring** (from the run-33 round) — the normalised request contained,
   case-insensitively, in the normalised heading, and selected **only when exactly one heading
   contains it**. So `section: "Spell failure"` reaches `## Minimum spell failure rates` in the
   NetHack wiki's *Spellcasting*; `section: "spell"`, contained in five of its headings, falls
   through to the miss line.

An earlier pass always beats a later one, so a heading that matches literally is never displaced by
a longer one containing it (`strategy` answers `## Strategy`, not `## Armor strategy`). The first two
passes are first-wins on a tie.

A `section` no pass resolves still returns the **full article** and is not an error, behind a marker
line that **lists the article's headings** — every heading in document order, as written so one can
be copied straight back, `; `-separated and capped at 600 characters with a trailing `…`:

```
[Section 'X' not found in article. Headings: ℹ️ Overview; 📝 Engraving Mechanics; 🔮 Elbereth; …. Returning full text.]
```

An article with no headings at all keeps the original
`[Section 'X' not found in article. Returning full text.]`. A run recorded before the run-33 round
carries that original line on every `nethack_wiki_view` section miss, whatever the article.

**Result shape**: `wiki_search` returns per-hit snippets via `WikiSnippetExtractor.BuildSnippet`
(bounded to `PerResultChars`, query-term-aware). **`nethack_wiki_search` returned full article
bodies with no per-result cap of its own up to harness 17**, bounded only by the generic per-tool
`MaxResultLength` truncation in `ToolExecutor` — which cuts the *last* article mid-sentence and
drops the ones after it, so which articles the model saw depended on Lucene's ordering. **From
harness 18 it caps each article** at `Tools:nethack_wiki_search:PerResultChars` (3000) and marks a
shortened one with `... [Article truncated: showing N of M characters. Use nethack_wiki_view for the
full article.]`, so every hit stays present in principle — but a full five-article yield of capped
articles (5 × 3000 chars plus separators) still exceeded the generic 10,000-character per-tool cap,
so the harness-18 fix did not by itself stop the *last* article, and everything after it, from being
cut mid-article on a full-yield call; run 36 recorded this at exactly 10,117 characters (the generic
cap plus its truncation suffix) on 4 of 6 calls. **From the run-36 round (2026-09-11)
`nethack_wiki_search` also declares its own `MaxResultLengthOverride`** (§8), sized so a full yield
of capped articles is never cut again by the generic cap.

An empty result from any of the four is `Success = true` — never a failure once indexing is
complete — but **the payload changed under harness 18** and both forms must stay matchable when
reading an older run:

| Tool | Miss payload up to harness 17 | Length | Opening to match from harness 18 |
|---|---|---|---|
| `wiki_search` | `No relevant information found in the GnollHack wiki.` | **52** | `No GnollHack wiki article matched '` |
| `wiki_view` | `Wiki article matching '{article}' not found.` | **35 + the article name** | `No wiki article matched '` |
| `nethack_wiki_search` | `No relevant information found in the NetHack wiki.` | **50** | unchanged |
| `nethack_wiki_view` | `article … not found` | — | unchanged |

The harness-18 payloads are near-miss reports of a few hundred characters, built the way
`SourceCodeSearchTool.BuildMissContent` builds its own (§ 4): they name the query, probe the same
index once or twice for a near neighbour and name what it hit, say when a `category` may be
excluding the match — the high-value hint, since `category` is a path substring and not a taxonomy
field — and end with a next action naming a specific alternative tool. Every probe swallows its own
exceptions and any `Error:`-prefixed content into "no hit", so a miss can never itself fail.
**Match the opening, never a length**, and reserve the bare strings above for a run recorded before
harness 18 or a call in which the builder threw.

`wiki_view`'s miss payload also states, when a `section` was requested, that it was the **article**
that missed so the section was never reached. A section that matches no heading is **not** a miss at
all and does not reach the builder: `WikiService.GetArticle` answers that case itself with the
section-miss marker line above — which carries the article's heading list — followed by the whole
article.

**`wiki_view` also has a fourth outcome, from the run-30 round, and it is not a miss.** `Several
wiki articles are titled '` opens a disambiguation payload — `Success = true`, capped at a few
hundred characters, listing extensionless candidate relative paths in index order (capped at 6 with
`…`) and naming the path form to call back with. It fires from branch 2 of the resolution order
above, when two or more indexed articles share an exact case-insensitive title. A miss count read
off the table above must not include it.

**`nethack_wiki_view` has an analogous non-miss outcome, from the run-36 round**: the resolution line
described above (opening `[No NetHack wiki article titled '`) prepends a `Success = true` result
that carries the full requested (or best-matching) article, not a miss — a miss count read off the
table above must not include it either.

**A dated measured fact: the `.md`-suffix trap.** On run 30 (2026-09-10), 6 of its 20 `wiki_view`
calls passed a filename carrying its own extension — the parameter schema calls `article` an
*"Article filename or title"* and `wiki_search` prints that filename in its own snippet header, so
the model copies it straight back in. A single-word title plus `.md` tokenized to one term matching
nothing and **missed outright** (`Praying.md`, `Runewords.md` — both resolve fine on the bare
title); a multi-word one survived on its remaining terms and, with no relevance floor in the
fallback branch, landed on the **wrong article** (`Guide to Praying.md` → `Guide.md`; `Sacrifice
Offering.md` → `Sacrifice Gifts.md`). Only `Red dragon.md` and `Sacrifice Gifts.md` resolved
correctly despite carrying the extension. The repair is the normalization step in the resolution
order above — trim, `\`→`/`, strip one trailing `.md`/`.txt`/`.html` — not a change to the fallback
branch's missing relevance floor, which this round deliberately left in place, scoped to that last
branch only. Re-measure rather than citing this figure once a later round changes the resolver
again.

---

## 6. Structured Lookup Family

Two tools sit on `WikiService`; three parse the GnollHack C sources directly. Both groups take
only a bare `name` — no wildcards, no repository selector.

**`monster_lookup` / `item_lookup`** — `name` (required) only. Both call
`WikiService.GetLookupContext(name, "monster")` / `(name, "item")` — the same path-substring
category mechanism as `wiki_search` §5, run for the top 8 hits — and if that returns nothing,
**silently retry unfiltered** before finally returning `Success = true, Content = "No information
found for monster/item: {name}"`. This double fallback makes both tools resilient to the wiki
repository not actually organizing articles under a `monster`/`item` path segment.

**The exact-title contract, from harness 26.** When exactly one category hit's title equals the
normalised request (`NormalizeArticleName` — trimmed, one trailing `.md`/`.txt`/`.html`
stripped — then compared case-insensitively, `GetArticle`'s title-equality rule; internal
whitespace is not collapsed), the result is **that article alone**, followed by one line
`[Other matches: A; B; C]` naming up to four other hit titles (absent when there are none); the
model passes one of those titles to get a different article. Two or more exact titles return the
`Several wiki articles are titled '…'` disambiguation. Anything else — including a name that is
only a prefix of a title — returns the top-5 join `GetRelevantContext` returns, and the unfiltered
retry is `GetRelevantContext` unchanged, so it never narrows. **Up to harness 25** the first call
was `GetRelevantContext` itself and an exact hit came back with its neighbours: run 41 Q13
`red dragon` carried *Red dragon scale mail* (4,498 characters) and Q14 `Master Kaen` carried
*Master lich* (4,001). In a run stamped 26 or later, an exact-title request whose result carries a
second `--- <file> ---` header is a regression of this contract.

**`get_monster_stats` / `get_item_stats` / `get_artifact_stats`** — `name` (required, exact as
written in the source: `src/monst.c`, `src/objects.c`, `include/artilist.h` respectively). For
`get_item_stats` that is the bare `oc_name` — `digging`, not `wand of digging` — and it also takes
an optional **`object_class`** (`WAND_CLASS`, `SCROLL_CLASS`, …) which selects among the object
classes that hold an entry of that name; the other two take `name` only.
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

**`get_monster_stats` attacks** (`SourceCodeService.ParseAttacks`): `stats.mattk` is a list with
one object per attack, carrying the `ATTK` slots by name — `aatyp`, `adtyp`, `damn`, `damd`, `damp`,
`mcadj`, `mlevel`, `range`, `aflags`, `action_tile`. **`damn` is the number of dice and `damd` the die
size**, so Master Kaen's claws (`damn: 16, damd: 2`) are **16d2**, not 2d16 — run 34's Q14 transposed
exactly that. Each attack whose `damn` and `damd` parse as integers with `damn > 0` also carries
**`dice`**, the pair already written as `"16d2"`; a 0/0 attack (damage computed from level, not rolled
from dice) carries no `dice` key, so its absence is not a parse failure. The key adds about 15
characters per attack and leaves every monster far under the truncation threshold below.

**Truncation** (`Tools:get_monster_stats` / `get_item_stats` / `get_artifact_stats`, each with
`TruncationThreshold` 9900 and `HardLimit` 10000): if the serialized JSON exceeds the threshold,
the handler re-serializes a minified version — dropping `flag_descriptions` if only `Stats` was
populated ("Level 1" minification), or dropping `macro_definitions`/`struct_definitions` if
`RawDefinition` was populated ("Level 2" minification) — and if *that* still exceeds
`HardLimit`, the tool returns `Success = false` with an over-the-limit error instead.
`GetItemStatsTool` implements only the `RawDefinition`-populated branch, which is also the only
one an item can reach: an item's Level 1 success populates `Stats` and `RawDefinition` together,
so a minified item response keeps both and drops `flag_descriptions`,
`macro_definitions` and `struct_definitions`.

**The Level 1 → Level 2 *parsing* fallback (distinct from the truncation minification above)
exists for all three tools, by two different mechanisms:**

- `get_monster_stats` and `get_artifact_stats` attempt a real positional-token parse of the
  macro call (Level 1: populates `Stats.Fields` and calls `PopulateFlagDescriptions`). On any
  exception during that parse, `SourceCodeService` logs `_logger.LogWarning(ex, "Level 1 parsing
  failed for monster/artifact '{Name}', falling back to Level 2.")` and instead returns
  `RawDefinition` plus the relevant `MacroDefinitions`/`StructDefinitions` context — silently, from
  the caller's point of view, beyond the shape of the JSON.
- **`get_item_stats`'s Level 1 is a macro resolver, not a positional parse.**
  `SourceCodeService.GetItemStats` (`:1073-1152`) calls `ObjectsMacroResolver.Resolve(name)`, which
  expands the `objects.c` macro chain — `DRGN_ARMR`, `BITS`, `OBJ` and the rest — down to the
  pass-2 `OBJECT` slots by repeated symbolic substitution, and runs a second structural expansion
  with every parameter bound to its own name so derived values can be told from raw ones. On
  success the response carries `Stats` from `ItemResolution.Fields`, `Message` from
  `ItemResolution.Message` (the class-versus-instance caveat: these are object-class values,
  further modified at run time by material, enchantment, exceptionality and erosion),
  **`RawDefinition` beside them** rather than instead of them, and
  `PopulateFlagDescriptions(response, resolution.FlagTokens)`. So `stats` and `raw_definition` are
  populated together on an item, which they never are on a monster or an artifact.
  - **The lookup key is the bare `oc_name`.** `digging`, not `wand of digging`; `dwarvish mattock`,
    not `mattock`. It is the same string the entry-matching regex matches in `objects.c`, so one
    `name` argument serves both the Level 1 resolver and the Level 2 raw dump.
  - **A name several object classes share is reported, not resolved away.** `objects.c` holds 947
    entries under 904 distinct names (the exact figures move with the game source;
    `Overseer.Tests/UnitTests/ObjectsMacroResolverTests.cs` asserts only the invariant that entries
    outnumber names). For a shared name the resolver takes the **first entry in file order**, puts
    every sharing class into `Fields["ambiguous_object_classes"]` as a list, and adds a note naming
    the class the values came from and the classes that also use the name. `Resolve` accepts an
    optional object class to pick a different one, and that is reachable from the tool:
    `get_item_stats`'s schema carries `object_class` and `GetItemStats(name, objectClass)` passes it
    through, so the note's "Pass object_class to get one of those instead" is an instruction the
    model can actually follow. A class the name does not appear in returns `Success = true` with an
    `error` naming the classes it does appear in, not an empty result.
  - **A slot whose value is an OR of symbolic flags comes back as a list**, not as a `|`-joined
    string (`ObjectsMacroResolver.SplitFlagUnions`) — the same shape `get_monster_stats` already
    returns for `mflags1` and its siblings via `SourceCodeService.ParseFlagField`. A value with no
    `|` is left as it stands.
  - **The AC, MC and spell-casting unit conventions travel with the values as `notes`**, because
    all three are stored in units the player never sees. `ac_bonus` is the stored `oc_armor_class`,
    which armour writes as `10 - ac` (`src/objects.c:1005`), and `base_ac` is that `ac` argument —
    emitted only where the expansion actually contains the `10 - x` form, since
    `GENERAL_CHARGED_WEAPON`, `WEAPONSHIELD`, `WEAPONBOOTS` and `WEAPONGLOVES` store their `acbon`
    raw. `src/do.c:5273` negates `ac_bonus` into the hero's AC, so a **positive** bonus lowers AC,
    which is an improvement. `magic_cancellation` is the stored MC level, further adjusted by
    `ARM_MC_BONUS`. `spell_casting_penalty` is the stored value; the percentage a player
    experiences is emitted alongside it as `spell_casting_penalty_percent`, that value times
    `ARMOR_SPELL_CASTING_PENALTY_MULTIPLIER` (30).
  - **A Level 1 failure is logged and named in `message`, not left to the JSON's shape alone.**
    `ItemResolution.Failed` always carries a reason; `GetItemStats` logs it at *information* level
    (`"Level 1 parsing did not resolve item '{Name}' ({Reason}); returning the raw definition."`),
    or at warning level when the resolver threw, and then falls back to `RawDefinition` plus the
    macro list for every armour wrapper and the `objclass` struct — with the failure reason
    appended to `message` as *"Structured values were not available: …"*. The tool never throws.
    Level 1 is **GnollHack-only**: the resolver is loaded in `ParseGameData()` from `src/objects.c`
    together with `include/objclass.h` (which supplies the enum constants the ternary conditions in
    `CHARGEDRING`, `MISCELLANEOUSITEM`, `GENERAL_TOOL`, `GENERAL_SPELLTOOL`, `CONTAINER` and
    `GENERAL_ROCK` compare against), on every re-index, and `NetHackSourceCodeService` overrides
    `ParseGameData()` with a no-op.

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
| `MaxSourceResultLength` (root-level) | 100000 at `Overseer/appsettings.json:17`; `SourceCodeSearchTool.cs` falls back to the same figure in code | `SourceCodeSearchTool` constructor | Only `source_code_search`'s own pre-truncation of its concatenated multi-file result, before the generic per-tool cap below ever runs |
| `AiPerformanceSettings:MaxResultLength:Default` | 10000 (Min 1000 / Max 100000, user-adjustable) | `ChatService.cs` when building `ToolExecutionContext` | Generic per-tool-call cap, enforced in `ToolExecutor.ExecuteAsync` step 4: a JSON-shaped result (starts with `{`/`[`) over the cap becomes a `Success = false` "Result too large" error instead of being substring-truncated (to avoid emitting invalid JSON); a plain-text result is hard-truncated with `ToolExecutor.BuildTruncationSuffix`'s `... [Truncated: showing {shown} of {total} characters. Narrow the query, or ask for a specific section, to see the rest.]` — 107 characters of fixed template plus the digits of both figures, so ≈117 for the common 10,000-of-N case, and **not a fixed length**. A run recorded before this suffix existed carries the 33-character `... [Result truncated for length]` instead |
| `ToolExecutionLimits:MaxBatchResultLength` | 40000 | `AgentLoopRunner.cs` (`:482`) | One `ToolBatchResultBudget` per tool-call **batch** (one iteration's parallel tool calls), scaled to `Math.Max(this, ToolExecutionContext.MaxResultLength)`. A tool whose effective cap exceeds **that scaled batch budget** is exempt from it — which is `refresh_snapshot` (60200) and nothing else. **Up to harness 17 the comparison was against `ToolExecutionContext.MaxResultLength` instead**, so *any* tool with an override was exempted outright; from harness 18 an override that fits inside the batch budget stays accountable to it. `MaxTurnResultLength` applies unconditionally either way |
| `ToolExecutionLimits:MaxTurnResultLength` | 120000 | `AgentLoopRunner.cs` (`:123`) | A cumulative ceiling across **all** tool-call batches within one model turn (multiple iterations of the tool loop), distinct from and layered above the per-batch budget |

Per-handler `MaxResultLengthOverride` (`IToolHandler`) is a **floor**, never a ceiling:
`ToolExecutor` takes `Math.Max(baseMaxLen, handlerMax)`, so an override raises the cap for its own
tool and cannot lower one, and it touches no other tool and not the shared
`AiPerformanceSettings:MaxResultLength:Default` that doubles as the live chat setting. Three tools
declare one:

| Tool | Override | Why |
|---|---|---|
| `refresh_snapshot` | **60200** | Floors the cap so the client snapshot's tail sections (Discoveries, dungeon overview) are not silently lost to an arbitrary cut — plus headroom for the client's own `[SNAPSHOT TRUNCATED …]` marker. See the comment in `ClientToolHandlers.cs` |
| `wiki_search` | **13000**, from harness 18 | The tool's own budget is `Tools:wiki_search:MaxResults` × `PerResultChars` = 5 × 2500 = **12500**, which exceeded the generic 10000 cap, so a full-yield search was **always** truncated mid-article on its last hit. 13000 is that 12500 plus headroom for the per-hit separators. The `max_results` clamp added in the run-37 re-run round (§5) is what keeps a full yield inside this override — an unclamped `max_results: 10` call had stored 13,117 characters, cut mid-result at this override, on run 37 Q4 |
| `nethack_wiki_search` | **16140**, from the run-36 round (2026-09-11) | `MaxResultLengthOverride` = `Tools:nethack_wiki_search:MaxResults` × (`PerResultChars` + 128) + 500 = 5 × (3000 + 128) + 500 = **16140** at current settings. The 128 is a per-article reserve for `CapArticle`'s own truncation note (`... [Article truncated: showing {shown} of {total} characters. Use nethack_wiki_view for the full article.]`, ≈ 105 characters), which is appended *after* the 3000-character cut — so a full five-article yield of capped articles plus their notes, the separators and the spoiler-free suffix runs to about 15,523 characters, not 15,000, and a floor set at `MaxResults × PerResultChars + 500` (15500) would still have been cut by the generic 10,000→override cap. See `server_benchmark_to_chat_transfer` run-36 entry (T2) for the arithmetic error this corrects |

**Neither was fixed by raising the generic cap, deliberately.** `MaxResultLength` is the cap for
all 30 tools *and* the live chat default (user-adjustable 1000–100000) *and* `Benchmark:MaxResultLength`,
so raising it enlarges every truncated result on every live turn; it would not fix a tool with no
per-result cap of its own; and every tool result is re-sent to the model on each subsequent round of
the same question (§ 4), so the extra characters are paid once per remaining round. Lowering
`Tools:wiki_search:PerResultChars` was rejected on the same arithmetic in reverse — on run 29 the
cap bound on only **2 of 24** `wiki_search` calls, so lowering it would have shrunk 22 healthy
results to repair 2.

> ⚠️ **These are `Overseer/appsettings.json` values, and no run record fingerprints them.**
> `ToolGuidesSha256` hashes the guide *files*, not the configuration, so a change to any
> `Tools:*:MaxResults`, `Tools:*:PerResultChars` or `MaxResultLength` is invisible in every run
> record — including a change that moves what a tool returns. Read the configuration alongside the
> run when a result size is part of a finding.

**The benchmark's stored copy has its own cap.** `BenchmarkToolCallRecorder` stores each result
under `Benchmark:ToolCallRecord:MaxResultChars`. When that key is unset, `BenchmarkToolCallRecordLimits.Resolve`
derives it from harness 26 as `Math.Max(Benchmark:MaxResultLength, largest MaxResultLengthOverride
among the run's allowed tools) + 2000` — 16,140 + 2,000 = **18,140** at current settings, from
`nethack_wiki_search` — so a result `ToolExecutor` handed over whole is stored whole. A stored cut
ends `... [Record truncated: stored N of M characters]`. Up to harness 25 the derivation ignored the
overrides (10,000 + 2,000 = 12,000), so a full-yield `wiki_search` or `nethack_wiki_search` result
was stored cut behind the old `... [Result truncated for length]` string —
`server_benchmark_tool_diagnostics` § 4 says how to tell that from a `ToolExecutor` cut.

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
