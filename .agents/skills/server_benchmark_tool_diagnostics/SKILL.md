---
name: server_benchmark_tool_diagnostics
description: >-
  How to read an Overseer AI benchmark run as a diagnostic instrument for the production chat
  agent's tool layer, rather than as a scoreboard. Answers "did the AI have access to the wiki
  or the source at all", "why did this tool return nothing", "did the tool actually work or was
  there a technical problem". Covers what a run records about its tool calls and what it
  discards, deriving the hidden count of technically failed tool calls, the five verdicts for a
  tool's call population, the four-rung ladder that separates "no access" from "no data" from
  "a broken tool", the corpus fingerprints a run records and the two reachable corpora it does
  not, reconstructing the arguments and results the run never stored, the three replay fidelity
  tiers, the parameter and result comparison checklist, and the diagnostic table a tool-layer
  analysis must produce. Read before attributing any benchmark finding to a tool, a corpus, or
  the absence of game data.
---

# Benchmark Tool Diagnostics: Reading a Run as an Instrument, Not a Scoreboard

---

## 1. The Two Questions

Every tool-layer finding reduces to two questions, asked of one tool in one run:

1. **Did the agent have access to the underlying repository or corpus at all?**
2. **Did the tool work as intended, or was there a technical problem?**

Neither is answered by a score. Both are answered by the run's stored columns, the corpus on disk, and — where the run stored nothing — reconstruction and replay.

**"Where the run stored nothing" is version-dependent.** Every run before harness 17 stored nothing about a call's own arguments or result, which is why most of this skill is built around reconstruction (§ 6) and replay (§ 7). From harness 17, a run's `BenchmarkRunAnswerToolCall` rows may make either unnecessary — always check first whether the answer has rows (§ 2, § 7's rung 0) before reconstructing or replaying anything.

**The standing fact that makes this worth doing.** A benchmark run grades the production chat system prompt (`server_benchmark_to_chat_transfer` § 1) **with the production tool registry behind it**: `ToolRegistry` and every `IToolHandler` are the same objects a live chat turn reaches, and `Benchmark:AllowedTools` in `Overseer/appsettings.json` merely narrows which of them are offered. So every tool call a run makes is a live sample of what a real user's session does, and **a tool defect seen in a run is a defect a real user hits.** That is also the trap: a corpus defect seen in a run is *not* a model defect, and reads exactly like one.

> 🛑 **Answer question 1 before question 2, and both before touching a score.** A confident "the game does not contain this" that turns out to be an indexer size limit is the most expensive mistake this skill exists to prevent.

---

## 2. What a Run Records, and What It Throws Away

All nine fields below exist on `BenchmarkRunAnswer` (`GnollHackServer.Data/BenchmarkRunAnswer.cs`); the five fingerprints are on `BenchmarkRun` (`GnollHackServer.Data/BenchmarkRun.cs`) and are stamped by `BenchmarkService.PopulateInstrumentFingerprint`. **From harness 17 there is a tenth source**, on the answer rather than the run: `BenchmarkRunAnswer.ToolCalls`, described in its own row below.

| Field | Where written | What it proves | What it cannot prove |
|---|---|---|---|
| `ToolCallSummary` | `BenchmarkService`, from `AgentRunResult.ToolCalls` filtered to `Status == "completed" && Error empty && Name non-empty`, grouped by name as `name×count` | Which tools **succeeded**, and how many times each | Nothing about arguments, results, ordering, or failures — `ToolCallSummary` itself never carries any of the four, at any harness version. Ordering specifically is not recoverable from it at all (§ 11). **From harness 17**, an answer with rows carries all four instead, in `ToolCalls` below |
| `ToolCallCount` | `AgentLoopRunner`: `result.ToolCallCount = result.ToolCalls.Count` | How many calls were **attempted**, errors and budget refusals included | Which of them worked |
| `ToolCallsBlocked` | `AgentLoopRunner`, incremented per `ToolBatchOutcome.BudgetExhausted` | How many calls the per-question budget refused | Nothing when null — null means **not recorded** (pre-harness-11), never zero |
| `ToolBudgetExhausted` | `AgentLoopRunner`, set alongside the above | That the budget bound at least once | How much the model still wanted to do |
| `ToolCallBudgetUsed` | `BenchmarkService`, the band-resolved budget for this question | The ceiling that actually applied — it differs per difficulty band and is **not** readable from `BenchmarkRun.MaxToolCallsPerQuestionUsed` | — |
| `ToolTimeMs` | `AgentRunResult.ToolTimeMs`, summed **per batch** | Wall-clock tool I/O for the turn | Per-tool latency; concurrent tools in one batch are one measurement |
| `TerminationReason` | `AgentLoopRunner`: `canceled` \| `budget_exhausted` \| `iteration_limit` \| `completed` | What the **harness loop** did | What the model intended |
| `ProviderFinishReason` | The provider's verbatim reason for the final model call | What the **provider** said | Nothing when null — "not recorded", never "stopped normally" |
| The five fingerprints — `CandidateSystemPromptSha256`, `ToolGuidesSha256`, `KnowledgeBaseHeadSha`, `WikiHeadSha`, `SourceCodeHeadSha` | `BenchmarkService.PopulateInstrumentFingerprint`; the last three via `GitHelper.GetGitHeadSha` on `KbPath`, `WikiPath`, `SourceCodePath` | Which revision of three of the five corpora the run read | Anything about the two NetHack corpora, which have no fingerprint (§ 5, and `server_tool_data_sources` § 6). Null means **not recorded** — never "no corpus", never "unchanged" |
| `BenchmarkRunAnswer.ToolCalls` (**harness 17 onward**) | `BenchmarkToolCallRecorder.Build`, called by `BenchmarkService` once per answer's turn | Every attempted call's name, status, arguments, result, error, tool round (`IterationIndex`), emission order (`SortOrder`), timings and the result size as `ToolExecutor` handed it over (`ResultLengthChars`) — individually, per call | Nothing for a run before harness 17: no rows exist and none can be backfilled. `ArgsText`/`Result` are nulled by the retention sweep once the *run* (not the row) is older than `ChatRetentionSettings.PruneBenchmarkToolCallResultsDays` (default 90); `ResultLengthChars` and every other column survive that prune |

**Two parsing traps in `ToolCallSummary`.** It can carry a `(N blocked by budget)` parenthetical, or read `None (N blocked by budget)` — so Σ must sum only the `name×count` pairs. And that parenthetical **never appears on a benchmark answer**: `BenchmarkService` detects blocked calls by matching the error text *"Maximum tool calls per session exceeded"*, while `ToolExecutor` emits that string only when `ToolExecutionContext.ToolBudgetScopeId` is null. Every benchmark call site sets one (`bench_<runId>_q<orderIndex>`), so a benchmark refusal carries the per-question message instead and the suffix is a chat-path artifact.

> 🛑 **For any run before harness 17, arguments and results were not stored, and `ShowDebugLog` is `false` in every benchmark path.**
>
> `ChatMessageToolCall.ArgsText` and `.Result` — which the production chat *does* persist — are never written for a benchmark answer at any harness version, because a benchmark run creates **no `ChatMessage` rows at all**. `AgentRunRequest.ShowDebugLog` is hardcoded `false` at all **eight** benchmark call sites in `BenchmarkService` (candidate answer, retry, difficulty assessment, claim verification and the rest), and stays that way from harness 17 too — the change below is a separate record, not a flip of this flag. **For any run before harness 17, "compare the tool use parameters and results" therefore cannot be done from stored benchmark data**; it is done by reconstruction (§ 6) and replay (§ 7), and a finding must say which — never imply a transcript exists.
>
> **From harness 17, this is no longer a hard limit.** `BenchmarkRunAnswer.ToolCalls` (`BenchmarkRunAnswerToolCall` rows) carry the real `ArgsText`, `Result` and `Error` for every attempted call, capped per `BenchmarkToolCallRecordLimits.Resolve` and prunable by the run's age (see the table row above). An analyst reads them through `GET /api/admin/benchmark/runs/{id}/answers/{answerId}/tool-calls` (admin-authenticated), which returns every row in `SortOrder` order with its full arguments and result — this is reading the record, not reconstruction or replay, and should be tried first (§ 7, rung 0). Full detail is in [`docs/overseer/ai-benchmark.md`](../../../docs/overseer/ai-benchmark.md) § **Harness Version 17 Updates**.

**A failed second opinion keeps the head of its raw text.** From the run-32 round, an unusable second-opinion verdict's `BenchmarkRunAnswer.SecondOpinionError` carries the parser's (or the timeout's) message, then ` | raw: ` and the first 600 characters of the model's last response with newlines collapsed, under the column's 2048-character cap. A parse failure — prose before the object, text after it, malformed JSON inside it — can therefore be diagnosed from the record without a replay. Before that round only the parser's message was kept. The stage is also bounded by `Benchmark:SecondOpinion:TimeoutSeconds` (code default 900; `appsettings.json` sets 600 from the run-34 round) and re-asks once for JSON-only output (`Benchmark:SecondOpinion:ParseRetryEnabled`); a timeout reads `Second opinion timeout exceeded (N s).`

---

## 3. The Derived Failure Count

For a run before harness 17, no report section surfaces the number of tool calls that failed **technically** — neither refused by the budget nor successful. It is arithmetic:

```
technical failures = ToolCallCount − Σ(ToolCallSummary name×count) − ToolCallsBlocked
```

`ToolCallCount` counts attempts; `ToolCallSummary` counts successes; `ToolCallsBlocked` counts budget refusals. **A non-zero result is direct evidence for question 2, and it is invisible in the report.** Compute it first, for every answer, before reading anything else.

> **From harness 17, this count is available directly and need not be derived.** `BenchmarkToolCallRecorder.Outcomes` returns it as `Failed` for an answer with rows, surfaced on the report's **Tool Call Outcomes** line and on `BenchmarkRunAnswerDto.ToolCallsFailed` (nullable; null means not recorded, never zero). The formula below still applies, unchanged, to any answer without rows — every answer from a run before harness 17 — and remains the only route to the figure for one.

**Worked example.** The figures below are illustrative of the shape `BenchmarkReportBuilder.FormatToolBudgetLine` renders, not extracted from a stored run; substitute the answer's own columns.

An answer reports `ToolCallCount = 30`, `ToolCallsBlocked = 2`, `ToolCallBudgetUsed = 45`, and
`ToolCallSummary = "source_code_search×12, source_code_view×9, wiki_search×5"`.

- Σ successes = 12 + 9 + 5 = **26**
- 30 − 26 − 2 = **2 technical failures**

The report's own line reads *"28 executed, 2 blocked, budget 45"* — because `BenchmarkReportBuilder.ToolCallSplit` computes `Executed = attempted − blocked`. **"Executed" is not "succeeded"**; the two failures are inside that 28, and the report never separates them.

Three caveats:

- **A null `ToolCallsBlocked` means *not recorded*, never zero.** For a pre-harness-11 answer the formula silently folds budget refusals into "technical failures". `ToolCallSplit`'s fallback — parsing the `(N blocked by budget)` suffix — cannot rescue it either, because that suffix never appears on a benchmark answer (§ 2). For such a run the derived figure is an upper bound on technical failures, and must be reported as one.
- **`delegate_to_subagent` inflates `ToolCallCount`.** `AgentLoopRunner` appends each outcome's `NestedToolCalls` to `result.ToolCalls`, so a delegating turn counts its children. Harmless for benchmark runs — that tool is **not** in `Benchmark:AllowedTools` — but not for chat sessions, where the formula needs the nested rows removed first.
- **`ToolCallSummary` is null when nothing succeeded.** Σ is then 0, not "unknown".

---

## 4. Five Verdicts for a Tool's Call Population

Per tool, per answer. Each verdict has one decision procedure.

| Verdict | Decision procedure | Supporting evidence |
|---|---|---|
| **Never attempted** | The tool's name is absent from `ToolCallSummary` **and** the derived failure count is 0 **and** `ToolCallsBlocked` is 0 | This is a routing observation about the model, not a tool fact. Check first that the tool was even offered: `Benchmark:AllowedTools` |
| **Attempted and refused by budget** | `ToolCallsBlocked > 0` (non-null) or `ToolBudgetExhausted` is true | `ToolCallBudgetUsed` for the ceiling; `TerminationReason == "budget_exhausted"` when the loop ended on it. A harness/limits finding, not a model one |
| **Attempted and errored** | Derived failure count > 0 (§ 3) | Which tool errored is **not recorded** for a run before harness 17 — the summary lists only successes; attribute by replay (§ 7), never by assumption. **From harness 17** it *is* recorded, per call — `BenchmarkRunAnswerToolCall.Name` and `.Error` name it and its error text directly; read the row (§ 7, rung 0) instead of replaying |
| **Succeeded and returned nothing** | Present in `ToolCallSummary` **and** the answer shows no content from it, or the answer paraphrases a not-found message | A `Success = true` "not found" is a success. Whether it means "absent from the game" or "absent from the index" is § 5 |
| **Succeeded with data** | Present in `ToolCallSummary` and the answer carries content or citations traceable to it | Then any defect is in the *content* — a corpus question (§ 5) or a suite question, not a tool question |

### The empty-result cascade

A sixth pattern, and it is a *population* signal rather than a per-call verdict: **a run of `Success = true` results clustered at or near a tool's miss-payload length, across many calls on one question.** It is distinct from a failed call (which carries `Success = false` and an error) and from a truncated one (which is long and carries a marker), and it is a **latency and cost** finding rather than a correctness one — the tool worked every time, the corpus was fine, and the model simply kept paying for calls that told it nothing.

**How to recognize it.** From harness 17 the right column is `BenchmarkRunAnswerToolCall.ResultLengthChars`; comparing it against the tool's known miss payload identifies a miss exactly.

> 🛑 **`ResultLengthChars` is *not* the size the tool itself produced.** `BenchmarkToolCallRecorder.Build` sets it from `ChatMessageToolCall.Result.Length`, and `ToolExecutor` had already truncated that value at `ToolExecutionContext.MaxResultLength` — 10,000 by default. So the column is pre-*record*-cap (it survives the benchmark recorder's own `MaxResultChars` and the retention prune, which is what it is for), **not** pre-`ToolExecutor`-cap. Whatever the value, a truncated result means *at least* `MaxResultLength` characters and the real size is unrecoverable.
>
> **There is no single truncation length to match on.** `ToolExecutor.BuildTruncationSuffix` (`ToolExecutor.cs:301-304`) composes the marker per call — `... [Truncated: showing {shown} of {total} characters. Narrow the query, or ask for a specific section, to see the rest.]` — 107 characters of fixed template plus the digits of both figures, so a 10,000-character cut of a six-digit result stores ≈**10117** and the value moves with the sizes. A stored **10033** is the marker of a run recorded before that suffix existed: 10,000 characters plus the 33-character `... [Result truncated for length]`. **The reliable test is the `[Truncated:` prefix in the stored `Result`, not any length** — fall back to a length only for a run whose `Result` was pruned, and then only against the arithmetic of that run's harness version. Run 28 shows 10033 on `source_code_search`, `wiki_view`, `item_lookup`, `list_indexed_files` and `nethack_wiki_search`.
>
> The point the arithmetic serves is unchanged: the column is censored at the **large** end and reliable at the **small** end, which is exactly where a miss lives. Treat any at-cap value as censored, never as a measurement. Each tool's miss payload is in [`server_tool_parameter_reference`](../server_tool_parameter_reference/SKILL.md) § 4–§ 7 — `source_code_search`'s is a near-miss report of a few hundred characters opening `No relevant source code found for '`, and a run recorded before that builder existed carries the 30-character `"No relevant source code found."` instead.

> 🛑 **Match the exact miss payload, not "short".** A "results under N characters" proxy conflates a miss with a legitimately compact success — a successful `source_code_search` with `filenames_only: true` returns 25–250 characters. Run 28's own rows: 16 `source_code_search` results under 100 characters across two questions, of which **12 were the 30-character miss string and 4 were successful `filenames_only` probes**. A proxy would have reported 16 misses where there were 12.
>
> The method is exact-payload matching; the payload itself is per tool and per harness version. `source_code_search`'s current miss is a near-miss report of a few hundred characters, which overlaps the `filenames_only` band outright — so match its opening `No relevant source code found for '` rather than any length, and reserve the bare 30-character `"No relevant source code found."` for a run recorded before that builder existed or a call in which it threw. Get the payload for the run's own harness version from [`server_tool_parameter_reference`](../server_tool_parameter_reference/SKILL.md) § 4 before counting anything.

**Why it cascades — and what changed under it.** A tool whose miss carries no near-miss information gives the model nothing to correct with, so its cheapest recovery is another guess. That is the diagnosis of **run 28**: its `source_code_search` misses returned the bare `"No relevant source code found."`, saying only that nothing was found, and its recorded arguments show the shape directly — `"layer glyph rendering"`, `"group size"`, `"layer_glyphs MAX_LAYERS send"` alongside guessed identifiers like `"CLONE_GROUP"` and `"select_random_encounter"`, spread over 20 tool rounds on one question.

**What the tool returns now is not that.** `SourceCodeSearchTool.BuildMissContent` probes for near-neighbour identifiers and names them with match counts, states when nothing matched the query as one literal substring, reports which individual terms matched which files, names an excluding `file_filter`, and points at `search_definitions` and `list_indexed_files` (`server_tool_parameter_reference` § 4). The two unchanged halves of the mechanism still hold — the query is a single literal substring matched per line, and the two fallbacks do not fire for an ordinary miss — so the cascade shape remains possible; it is the information-free miss that no longer explains it. **A cascade found in a current run therefore needs its own explanation, and one found in run 28 or any earlier run must be dated as such.**

**Why it is worth a finding.** Run 28's evidence: the two questions carrying the cascade consumed **42.6 % of the run's input tokens** and produced its **P90 and maximum model time**, and the source-family share of a question's calls correlated ***r* = 0.92** with its model time. Nothing was factually wrong; the run was slow and expensive.

**Triage** (§ 9): a cascade is **3 Chat-Transferable** when the fix is routing or query-formulation guidance in the tool description or `_policy.md`, and **1 Harness Defect** when it is a tool-contract problem — a missing near-miss hint, or a limit that suppresses the tool's own advice. `MaxSourceResultLength` is the standing example of the latter: its *"refine your query or use source_code_view"* suffix is appended at 100,000 characters and then cut away by `ToolExecutor`'s 10,000-character cap, so **that** advice never reaches the model (`server_tool_parameter_reference` § 4). Qualify the claim to the tool-specific half: the generic `... [Truncated: showing …. Narrow the query, or ask for a specific section, to see the rest.]` suffix `ToolExecutor` appends in its place does reach the model, so a truncated plain-text result is not advice-free — what is suppressed is the pointer to `source_code_view`. It is **never** a Corpus / Environment Defect unless a § 5 rung actually fails.

**A `get_function_definition` continuation that returns no body line is the pre-2026-09-10 clamp
signature.** Until the run-34 round `SourceCodeService.GetFunctionBody` read `start_line` as a
0-based index into its own output and clamped any value past the output's end to its last line, so a
model that resumed with the absolute file line the result header prints got back the header and no
body: run 34 Q16 call 19, `start_line: 1480` against `create_encounter` (236 output lines, file lines
1330–1564), `ResultLengthChars` **65**. In a run recorded before that round this shape is a
**tool-contract defect** (§ 9 category 3), not a miss and not a model error — and whatever the
unreturned region held never reached the model. From the round, `start_line` accepts either the
truncation notice's 1-based output line or an absolute file line inside the definition, and any other
value returns an explicit `start_line N is outside this definition …` message, so a header-only
continuation in a later run is a regression of that contract (`server_tool_parameter_reference` § 4).

**A stored `start_line 0 is outside this definition …` result dates a run to before the run-36
round (2026-09-11).** From that round `0` is the model's idiom for "from the beginning" and behaves
as an omitted `start_line`, so it can no longer produce this message; a call recorded with
`start_line: 0` and this exact text is a run from before 2026-09-11 (`server_tool_parameter_reference`
§ 4).

**A `get_function_definition` miss of the bare `No definition found for '<name>' of kind '<kind>'.`
shape is a pre-run-35-round marker.** Until the round a miss carried only that sentence — 56
characters plus the two names — so a stored result of exactly that shape dates the run. From the
round the payload **still opens with** the same sentence, which is what a reader matches on, exactly
as with `source_code_search`'s opening (§ 4 above), and then carries a bounded `filenames_only`
occurrence probe naming up to three files with match counts, or a statement that the identifier does
not occur in the indexed repository, plus fixed guidance that a struct member, function pointer or
macro alias has no extractable body under that name. The whole payload is capped at 600 characters
and the builder can never itself fail — it falls back to the service's original sentence
(`server_tool_parameter_reference` § 4 states the contract).

---

**The wiki family's miss payloads changed under harness 18, and both forms must stay diagnosable.**
Up to harness 17 they were bare, exactly as `source_code_search`'s was before the run-28 round, and a
run recorded then must still be readable:

| Tool | Payload up to harness 17 | Length |
|---|---|---|
| `wiki_search` | `No relevant information found in the GnollHack wiki.` (`WikiSearchTool.cs:75`) | **52** |
| `wiki_view` | `Wiki article matching '{article}' not found.` (`WikiViewTool.cs:61`) | **35 + the article name** |
| `nethack_wiki_search` | `No relevant information found in the NetHack wiki.` | **50** |
| `monster_lookup` | `No information found for monster: {name}` | **34 + the name** |
| `item_lookup` | `No information found for item: {name}` | **31 + the name** |

**From harness 18 each is a near-miss report of a few hundred characters**, built the way
`SourceCodeSearchTool.BuildMissContent` builds its own, and matched on its opening rather than on any
length:

| Tool | Opening to match | What the payload adds |
|---|---|---|
| `wiki_search` | `No GnollHack wiki article matched '` | whether a `category` was set and — the high-value hint — whether the same query matches **without** it, because `category` compiles to a wildcard against the indexed file's filesystem **path** and not a taxonomy field; otherwise which individual terms of the query do have articles; then a next action naming `wiki_view` or `nethack_wiki_search` |
| `wiki_view` | `No wiki article matched '` for an article miss | an **article**-resolution miss only — no code path emits `Article matching '` as a section-miss payload. A `section` that matches no heading is **not a miss at all**: `WikiService.GetArticle` answers it itself with the `[Section 'X' not found in article. Returning full text.]` line, followed by the whole article, which `ToolExecutor` then truncates at its per-tool cap — `server_tool_parameter_reference` § 5 states this correctly two paragraphs below its own miss-payload table, so the two skills had been contradicting each other. Run 30's Q4 is the empirical cost: three `section` misses on one article (`Runewords`, whose headings carry emoji prefixes) at roughly 10,000 characters each — `section: "✨ Gilthoniel"` returned 853 characters once the emoji was guessed, and `Morgoth` was never reached. The run-31 round added the normalised pass and the heading list to the marker line on `wiki_view`; **from the run-33 round `nethack_wiki_view` resolves sections through the same extractor**, and both gained a unique-substring pass (a request contained in exactly one heading selects it), so on either tool a section miss now means no heading equals, normalises to, or uniquely contains the request, and the marker line names every heading (`server_tool_parameter_reference` § 5). A `nethack_wiki_view` section miss in a run recorded before that round — run 33's Q9, `Spell failure` against *Spellcasting*, 10,117 characters — carries the bare line with no heading list |
| `wiki_view` **(disambiguation, not a miss)** | `Several wiki articles are titled '` | `Success = true` — an ordinary result carrying a next action, never a miss. Fires when two or more indexed articles share an exact case-insensitive title; lists extensionless relative candidate paths in index order, capped at 6 with `…`, and names the path form (`Races/Gnoll`) to call back with. A reader counting misses by opening must not count this one |
| `monster_lookup`, `item_lookup` | `No GnollHack wiki article matched the monster '` / `… the item '` | that **both** the category path filter and the unfiltered fallback missed — both tools try the filter first and retry without it — plus which words matched, then a next action naming `get_monster_stats` / `get_item_stats` |
| `nethack_wiki_view` **(resolution line, not a miss)**, from the run-36 round | `[No NetHack wiki article titled '` | `Success = true` — an ordinary result, never a miss, prepended to the full article. Fires whenever the normalised request differs from the normalised resolved title; names the article actually shown (`Showing 'Y'.`) and up to four other candidate titles drawn from the title/filename and `summary` queries. A reader counting misses must not count it. Before the round the same request silently returned the wrong article with no marker at all — run 36 Q11: `Combat` for *"Two weapon combat"* (`server_tool_parameter_reference` § 5) |

Every probe swallows its own exceptions and any `Error:`-prefixed content into "no hit", so a miss can
never itself fail, and each payload is deliberately kept to a few hundred characters: **every tool
result is re-sent to the model on each subsequent round of the same question**, so a verbose miss is
paid once per remaining round.

**A provider transport failure now lands in the transport-defect bucket, from harness 18.** Under 17
and earlier a `Failed` or `ProviderError` answer fell through `BenchmarkRunFinalizer.Classify` into
**Clean**, so a run that lost a question to a TCP connect timeout reported itself *"Clean 18 of 18"* —
which is what run 29 did on Q8. From 18, `HasTerminalFailure` puts it in **TransportDefect** and the
provider-error count agrees with the bucket. Two consequences for reading a run:

- **A 17-stamped run's clean count is not comparable with an 18-stamped one's.** Run 29's own
  *"Clean 18 of 18"* is *"Clean 17 of 18, Transport Defects 1"* under 18. Read the harness version
  before reading a clean count as a quality signal.
- **The classification is now made from the exception type, not from its message.** Run 29's Q8 was
  missed because `BenchmarkProviderErrorClassifier` matched substrings against a Windows English
  operating-system string; `SocketException.SocketErrorCode` and the exception chain do not vary with
  the machine's display language. A run recorded before 18 on a non-English host may therefore carry
  a `Failed` answer that was in truth a provider error, and nothing repairs those rows.

**From harness 21, an OpenAI in-stream failure carries its own code and is retried when transient.**
`response.failed` and a top-level `error` object are parsed into `OpenAI stream error: [{code}]
{message}` instead of a bare, code-less sentence, and the shared `ProviderErrorRetryPolicy` retries the
condition when it is not on the deny list (`invalid_request_error`, `insufficient_quota`,
`context_length_exceeded`, `authentication`), so a transient overload no longer costs the question
outright the way it did on run 37. The resulting answer, when the retries are exhausted, is classified
`ProviderError` and carries an HTTP status. **An answer from harness 20 or earlier reading `OpenAI
stream error: response.failed` with `Status = Failed` is that harness's own defect — the code and
message were discarded before they ever reached the stored answer — and is not evidence of a model
failure.**

## 5. Telling "No Access" from "No Data"

The cold-start case is handled and visible: while a service is still indexing, each guarded tool returns `Success = false` with a `ToolGuardMessages.*` string, and `ToolBatchRunner` feeds that message back to the model as the tool result — so the model can and does paraphrase it into the answer. The tool→service→guard map and the cold/warm state matrix are **owned by [`background_indexing_architecture`](../background_indexing_architecture/SKILL.md) § 3**; read them there.

**After indexing completes, the failure modes go silent.** A miss returns an ordinary `Success = true` "not found", and the exclusions that produce a *permanent* miss log nothing. Climb all four rungs before concluding anything about the game:

1. **Is a guard message present in the answer text?** If the answer echoes a `ToolGuardMessages.*` phrase — "initialization in progress", "warming up", "Do not retry" — the corpus was cold on the machine that ran the benchmark. Stop: this is a **Corpus / Environment Defect** and says nothing about the model or the game.
2. **Does the run's corpus fingerprint match the corpus on disk now — and is one recorded at all?** Delegates to [`server_tool_data_sources`](../server_tool_data_sources/SKILL.md) § 6, including the two corpora that have none: the NetHack source (`NetHackSourceCodePath`) and the NetHack wiki (`NetHackWikiPath`). Both are reachable from a run — `nethack_wiki_search` and `nethack_wiki_view` are in `Benchmark:AllowedTools`, and `source_code_search`, `source_code_view`, `get_function_definition`, `search_definitions`, `get_constants` and `list_indexed_files` each accept `repository: "nethack"` — so **a NetHack-corpus finding has no run-recorded provenance at all** and must fall back to comparing the run's `StartedAtUtc` against the corpus on disk and stating the residual uncertainty in the finding. That is a deliberate limit, documented as such in [`docs/overseer/ai-benchmark.md`](../../../docs/overseer/ai-benchmark.md) § Harness Version 16 Updates, not an oversight.
3. **Is the target inside the indexed scope and under the size limit?** `server_tool_data_sources` § 5 owns the scope and limit table. In band, `list_indexed_files` is the cheapest probe there is — it is in `Benchmark:AllowedTools`, takes a `path_filter`, and answers "is this file in the index" directly.
4. **Does the query match under that tool's own semantics?** Parameter shapes, defaults, clamps and match behaviour are in [`server_tool_parameter_reference`](../server_tool_parameter_reference/SKILL.md) § 4–§ 7.

**Only when all four rungs pass is "the game does not contain this" a supportable claim.**

### Worked example: `src/soundset.c`

`SourceCodeService.IndexRepository` indexes a file only when `fileInfo.Length <= _maxFileSizeKB * 1024`, and skips it otherwise with **no log line and no marker**. `_maxFileSizeKB` comes from the `MaxSourceFileSizeKB` configuration key and defaults to **800** when unset. `src/soundset.c` in the GnollHack source measures **1,086,562 bytes ≈ 1061 KB** — measured on this machine on 2026-09-09.

So the file is **not in the source index at all**. Every consequence is silent:

- `source_code_view` and `source_code_search` cannot see it, and report an ordinary not-found.
- `get_function_definition` and `search_definitions` cannot find anything declared in it.
- `get_constants` is missing every `#define` and enum member it declares, because constants are parsed only from files that passed the size gate.
- `list_indexed_files` with a `soundset` filter returns nothing — which is exactly why rung 3 is worth running.

A model asked about GnollHack sound sets therefore receives a confident "not found" that is a **property of the indexer, not of the game**, and an analysis that reads it as a knowledge gap files a model-quality finding against a configuration value. The same limit governs the NetHack source corpus: `NetHackSourceCodeService` derives from `SourceCodeService` and reads the same key.

---

## 6. Reconstructing the Call

Because arguments were never stored for any run before harness 17 (§ 2), a tool-layer finding about such a run usually rests on a **reconstruction**: the most probable argument set, inferred from three sources together. **For a harness-17 run, check first whether the answer has rows** (§ 2, § 7's rung 0) — reconstruction is for when it does not, or when the row's own payload columns were pruned by the retention sweep.

- **The question text** — the entity, mechanic or file the model was asked about, which constrains `query`, `article`, `name` or `file_path`.
- **The answer's own citations** — a quoted file and line, an article title, a stat block. These are the strongest evidence, because the model can only cite what a result contained.
- **The tool's schema and defaults** — `server_tool_parameter_reference` § 4–§ 7 for what was required, what was optional, and what the handler filled in.

> 🛑 **A reconstruction is a hypothesis and must be labelled one in the finding.** Write "reconstructed arguments (hypothesis)", never "the run called `source_code_search` with …". A reconstruction that is presented as a recorded call is indistinguishable from a fabricated citation by anyone reading the finding later, and this repository has already had to withdraw a benchmark finding built on a sentence the system prompt never contained — the withdrawal of T3 in `server_benchmark_to_chat_transfer` § 2. Reconstruction earns its place only when it is visibly provisional.

A reconstruction is never the end of a finding. It is the input to § 7.

---

## 7. Replay, in Three Fidelity Tiers

**Rung zero, for a harness-17 run: read the stored record before reconstructing or replaying anything.** The **tool-call log export** — `GET /api/admin/benchmark/runs/{id}/tool-call-log` (admin-authenticated), downloadable from the run detail as **Tool-call log** — is the first route to a run's `ArgsText` and `Result`, ahead of the per-answer endpoint. It returns one Markdown document covering **every answer of the run**: the per-call table plus, per call, the full arguments, the error, and — from harness 25 — the first 600 **and last 240** characters of the result, with `(pruned by retention)` where the sweep nulled a payload. A run stamped 24 or lower carries the head alone, so an end marker such as `[Showing N of M matching articles …]` is absent from its export whatever the tool returned. A run's log is attached to an analysis the way its report and diagnostics are. `GET /api/admin/benchmark/runs/{id}/answers/{answerId}/tool-calls` (admin-authenticated) remains the route when only one answer is in question — it returns every attempted call of that answer's turn in `SortOrder` order, with its full arguments, full result, error text, status, tool round and timings. Either is neither reconstruction (§ 6) nor replay — both are the harness's own record of what happened — and they settle the § 8 parameter-and-result checklist directly wherever the row's payload was not later pruned by the retention sweep (a pruned row shows `ArgsText`/`Result` null beside a non-zero `ResultLengthChars`; § 2). **Run 30's round shipped the log export**, and the run-30 analysis had to leave several cells undetermined for want of it — settled within minutes of the export existing. The three tiers below are for when rung zero is unavailable: any run before harness 17, or a pruned row on one after it.

Pick the cheapest tier that can settle the question, and name the tier in the finding.

### (a) On-disk reproduction

PowerShell against the same corpus path: does the file exist, what is its byte length against the limit, does the term appear, is the article present. Cheapest, needs no build, and settles **reachability and content** — rungs 2 and 3 of § 5. It cannot tell you what the *tool* did with any of it.

### (b) Scratch xUnit replay

Constructs the real service and the real handler against a real path, and executes the tool with the reconstructed arguments. This is the tier that proves tool behaviour.

**The pattern to copy is `Overseer.Tests/UnitTests/NetHackWikiToolTests.cs`.** What it does:

- Builds an `IConfiguration` with `ConfigurationBuilder().AddInMemoryCollection(...)`, supplying the corpus path key and the relevant limit — e.g. `NetHackWikiPath` and `MaxNetHackWikiFileSizeKB` — so no User Secret is read and no machine path is hardcoded into the assertion logic.
- Constructs the service directly (`new NetHackWikiService(config)`) and **awaits `service.InitializationTask`** before asserting, so the assertion runs against a warm index rather than a race.
- Constructs the handler around that service (`new NetHackWikiSearchTool(service, config)`, `new NetHackWikiViewTool(service)`) and calls `ExecuteAsync(JsonDocument.Parse("{\"query\": \"…\"}").RootElement, new ToolExecutionContext { SessionId = 1, SpoilerFreeMode = false }, CancellationToken.None)` — the reconstructed arguments go in as the raw JSON the model would have emitted.
- Asserts on `result.Success`, `result.Content` and `result.ErrorMessage` separately, which is exactly the empty-versus-errored distinction § 8 requires.
- Runs two flavours of corpus: a **synthetic** one written to a temp directory in the constructor and deleted in `Dispose`, and a **real** one guarded by an existence check that returns early when the corpus is absent, so the file passes on a machine without it.
- Tests the cold guard by **not** awaiting `InitializationTask` and gating the assertions on `if (!service.IsIndexingComplete)`, then asserting `Success == false` and `ErrorMessage == ToolGuardMessages.NetHackWikiIndexingInProgress`.
- Touches **no external AI API**, so it needs no `[Trait("Category", "UsesExternalApi")]` and costs nothing to run — see [`testing_guidelines`](../testing_guidelines/SKILL.md) § 1 for the trait convention and the mandatory `--filter-not-trait "Category=UsesExternalApi"`, and § 5 for the `InitializationTask` rule this pattern implements.

**A diagnostic replay is written to the session scratch directory, never into the repository, and there is deliberately no committed fixture** — a one-off reconstruction of one run's call is evidence for one finding, not a regression test, and committing it would grow the suite with cases nobody can later interpret. If replay reveals a real defect, the test that guards the **fix** is a separate, deliberate addition.

### (c) Live chat turn with `ShowDebugLog` enabled

A real chat session persists `ChatMessageToolCall.ArgsText` and `.Result`, so this is the **only** tier that produces a genuinely recorded call with its arguments and its result. Highest fidelity, highest cost, and it measures the chat configuration rather than the benchmark one — `hasWikiContext` alone differs (`server_benchmark_to_chat_transfer` § 4). Reserve it for a finding that survives (a) and (b).

---

## 8. The Parameter and Result Comparison Checklist

Run every line, for each call under examination. Tier (a) can answer some; the rest need (b) or (c).

- **Required parameters present** — a missing required property is a model-behaviour defect, not a tool defect.
- **`repository` routed to the intended corpus** — absent or `"gnollhack"` reads the GnollHack tree; `"nethack"` reads the NetHack one, through a different service with a different guard message. A finding that names the wrong corpus is worthless.
- **Defaults and clamps applied as `server_tool_parameter_reference` § 8 states** — result counts, context lines, chunk sizes.
- **Result shape matches the schema** the tool documents.
- **Truncation markers absent or explained** — `... [Truncated: showing {shown} of {total} characters. Narrow the query, or ask for a specific section, to see the rest.]` for a per-tool cut, `... [Result truncated for length]` for the same cut in a run recorded before that suffix existed (both appear in stored runs, so check for both), `[Tool output truncated: cumulative turn limit reached]`, `[Tool output omitted: cumulative turn limit reached]`, or a `Result too large (N chars)` refusal on oversized JSON. Which budget produced which marker is owned by [`tool_execution_architecture`](../tool_execution_architecture/SKILL.md) § 2. A truncated result that the model then answered from incompletely is a limits finding, not a knowledge gap.
- **Empty distinguished from errored** — `Success = true` with a not-found string is a working tool; `Success = false` is not. `ToolBatchRunner` puts the failure's `ErrorMessage` into the outcome's `Content`, so the model sees the same field either way and the distinction must come from the replayed `ToolResult`, never from the answer text.
- **Latency inside the family's normal range** — compare against `ToolTimeMs` for the answer and the run's other answers; an outlier points at the corpus or the throttles, not the model.

---

## 9. Triage

Map each § 4 verdict onto the four categories in [`server_benchmark_to_chat_transfer`](../server_benchmark_to_chat_transfer/SKILL.md) § 2.

| § 4 verdict | Likely category | Why |
|---|---|---|
| Never attempted | **3 Chat-Transferable** — a routing observation. First rule out that the tool was not in `Benchmark:AllowedTools`, which is **1 Harness Defect** | The prompt and tool guides decide routing, and both are production artifacts |
| Attempted and refused by budget | **1 Harness Defect**, or a limits-parity item at ladder rung 4 | The budget is an instrument setting, not a model property |
| Attempted and errored | **1 Harness Defect** if the harness or a handler broke; **4 Corpus / Environment Defect** if the corpus caused it | Identify by replay before choosing |
| Succeeded and returned nothing — a § 5 rung failed | **4 Corpus / Environment Defect** | The instrument is sound and the suite is sound; the data the tool read was not what it is in production |
| Succeeded and returned nothing — all four rungs pass | **2 Suite Defect** if the rubric asserts the fact; otherwise a genuine content gap for ladder rung 1 or 2 | The corpus really lacks it |
| Succeeded with data, but the answer is wrong | **3 Chat-Transferable** | The model had the data and misused it |

> 🛑 **A corpus defect is never reported as a model-quality finding, and never produces a chat prompt change.** Its fix is to the corpus, the indexer limit, or the deployment — and a prompt edit made in its place both fails to fix anything and destroys instrument comparability for every future run (`server_benchmark_to_chat_transfer` § 6).

---

## 10. Required Output

A tool-diagnostics pass **must** produce this table, one row per tool per run (or per answer where a single question is under examination):

| Tool | Attempted | Succeeded | Derived failures | Corpus fingerprint recorded / matches | Verdict (§ 4) | Triage (§ 9) | Evidence |
|---|---|---|---|---|---|---|---|
| … | `ToolCallCount` share | from `ToolCallSummary` | § 3 formula | recorded? · matches disk? · or *none exists* | one of five | one of four | column names, replay tier, file paths |

*For a harness-17 run, read "Succeeded" and "Derived failures" directly from the answer's rows (`Outcomes()` — succeeded, failed, refused) instead of from `ToolCallSummary` and the § 3 formula; say so in the Evidence column. The formula and the summary remain the only route for a run without rows.*

**Limits of this pass** — state this, or its equivalent, in every tool-diagnostics output:

> This pass reads stored run columns only. **For any run before harness 17, arguments and results were never stored** for any benchmark tool call (`ShowDebugLog` is `false` at every benchmark call site, and a run creates no `ChatMessage` rows), so every statement about such a call's parameters or its returned content is a reconstruction or a replay, labelled as such. **From harness 17, a run's `BenchmarkRunAnswerToolCall` rows carry the real arguments, result, error, status, emission order and timings for every attempted call** — read them through the tool-calls endpoint (§ 7, rung 0) rather than reconstructing, unless the payload columns were later pruned by the retention sweep (`ChatRetentionSettings.PruneBenchmarkToolCallResultsDays`, default 90 days), in which case `ArgsText`/`Result` are null but `Name`, `Status`, `Error` and `ResultLengthChars` still are not. **Two corpora reachable from this run carry no fingerprint** — the NetHack source (`NetHackSourceCodePath`) and the NetHack wiki (`NetHackWikiPath`) — so any NetHack finding rests on `StartedAtUtc` against the corpus as it stands now. **Any run before harness 16 has no GnollHack wiki or GnollHack source provenance at all**: `WikiHeadSha` and `SourceCodeHeadSha` were added in harness 16, no historical row is backfilled and none can be, and null in either column means *not recorded*. `BenchmarkAssessmentPrompt.HarnessVersion` is now `"25"`.

---

## 11. Anti-Patterns

| Anti-pattern | Why it is wrong |
|---|---|
| Inferring call **ordering** from `ToolCallSummary` | `ToolCallSummary` itself never records ordering, at any harness version — the report's own legacy caveat says so: *"`ToolCallSummary` records aggregate call counts, not an ordered execution log (ordering is not recorded)"* — `BenchmarkReportBuilder`, in the tool usage section. Cite that line for a run without rows. **From harness 17, check for rows first**: an answer with `ToolCalls` carries the real `SortOrder` and needs no inference at all |
| Reading a null `ToolCallsBlocked` as zero | Null means **not recorded** (pre-harness-11). Treating it as zero converts budget refusals into fabricated technical failures in the § 3 formula |
| Reading a null corpus fingerprint as "no corpus" | It means the hash was not captured or could not be resolved — `BenchmarkInstrumentFingerprint` states this in its own contract. It says nothing about whether the corpus was there |
| Assuming a NetHack-corpus call has provenance because the GnollHack ones do | Three of five corpora are fingerprinted. The NetHack source is a Git tree that is simply not hashed; the NetHack wiki is generated wholesale by a script and has no revision to hash |
| Treating a `Success = true` "not found" as proof the data does not exist | It proves the index returned nothing. Climb all four rungs of § 5 first — `src/soundset.c` is the standing counter-example |
| Attributing a cold-start guard message to model behaviour | `ToolGuardMessages.*` text in an answer is the machine that ran the benchmark still indexing. It is a Corpus / Environment Defect and the model was following the guard's explicit instruction |
| Building a finding on a reconstructed argument without labelling it | Indistinguishable from a fabricated citation to a later reader; see the withdrawal of T3 (§ 6) |
| Comparing tool counts across runs whose `Benchmark:AllowedTools`, tool budget, or `parallelMode` differ | The allowed set decides what *could* be called; `ToolCallBudgetUsed` is band-resolved and decides what *could complete*; `parallelMode` changes the batching policy and therefore the counts directly. `BenchmarkComparabilityKey` keys the budgets (`PerQuestionBudgets`) and the parallel mode, and neither `WikiHeadSha` nor `SourceCodeHeadSha` is a key at all — so **a difference in either corpus hash is a fact to investigate, not an automatic tier drop**, while a difference in budgets or parallel mode really does move the tier |

---

## 12. Cross-References

- [`server_tool_data_sources`](../server_tool_data_sources/SKILL.md) — the corpora, resolving each path, secrets hygiene, § 5 reachability and size limits, § 6 provenance and its two holes, refresh semantics
- [`server_tool_parameter_reference`](../server_tool_parameter_reference/SKILL.md) — the per-tool parameter and result contract; § 3 `Benchmark:AllowedTools`, § 4–§ 7 the tool families, § 8 cross-cutting limits
- [`server_benchmark_to_chat_transfer`](../server_benchmark_to_chat_transfer/SKILL.md) — § 2 the four triage categories, § 4a the route into this skill, § 6 the evidence bar, § 7 the ladder of safe changes
- [`background_indexing_architecture`](../background_indexing_architecture/SKILL.md) — § 3 the tool→service→guard map and the cold/warm state matrix, **owned there**
- [`tool_execution_architecture`](../tool_execution_architecture/SKILL.md) — batching, throttles, budgets and truncation markers, **owned there**
- [`testing_guidelines`](../testing_guidelines/SKILL.md) — § 1 the external-API trait and the mandatory test filter, § 5 `InitializationTask` synchronization, for the § 7(b) scratch replay
- [`docs/overseer/ai-benchmark.md`](../../../docs/overseer/ai-benchmark.md) — § **Harness Version 16 Updates** for the two corpus fingerprints, why they are provenance rather than comparability keys, and the two corpora that still have none; § **Harness Version 17 Updates** for the per-call tool record, the derived result cap, the three-way outcome split, and the retention window
