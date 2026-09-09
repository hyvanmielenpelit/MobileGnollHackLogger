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

**The standing fact that makes this worth doing.** A benchmark run grades the production chat system prompt (`server_benchmark_to_chat_transfer` § 1) **with the production tool registry behind it**: `ToolRegistry` and every `IToolHandler` are the same objects a live chat turn reaches, and `Benchmark:AllowedTools` in `Overseer/appsettings.json` merely narrows which of them are offered. So every tool call a run makes is a live sample of what a real user's session does, and **a tool defect seen in a run is a defect a real user hits.** That is also the trap: a corpus defect seen in a run is *not* a model defect, and reads exactly like one.

> 🛑 **Answer question 1 before question 2, and both before touching a score.** A confident "the game does not contain this" that turns out to be an indexer size limit is the most expensive mistake this skill exists to prevent.

---

## 2. What a Run Records, and What It Throws Away

All nine fields below exist on `BenchmarkRunAnswer` (`GnollHackServer.Data/BenchmarkRunAnswer.cs`); the five fingerprints are on `BenchmarkRun` (`GnollHackServer.Data/BenchmarkRun.cs`) and are stamped by `BenchmarkService.PopulateInstrumentFingerprint`.

| Field | Where written | What it proves | What it cannot prove |
|---|---|---|---|
| `ToolCallSummary` | `BenchmarkService`, from `AgentRunResult.ToolCalls` filtered to `Status == "completed" && Error empty && Name non-empty`, grouped by name as `name×count` | Which tools **succeeded**, and how many times each | Nothing about arguments, results, ordering, or failures. Ordering is not recorded at all (§ 11) |
| `ToolCallCount` | `AgentLoopRunner`: `result.ToolCallCount = result.ToolCalls.Count` | How many calls were **attempted**, errors and budget refusals included | Which of them worked |
| `ToolCallsBlocked` | `AgentLoopRunner`, incremented per `ToolBatchOutcome.BudgetExhausted` | How many calls the per-question budget refused | Nothing when null — null means **not recorded** (pre-harness-11), never zero |
| `ToolBudgetExhausted` | `AgentLoopRunner`, set alongside the above | That the budget bound at least once | How much the model still wanted to do |
| `ToolCallBudgetUsed` | `BenchmarkService`, the band-resolved budget for this question | The ceiling that actually applied — it differs per difficulty band and is **not** readable from `BenchmarkRun.MaxToolCallsPerQuestionUsed` | — |
| `ToolTimeMs` | `AgentRunResult.ToolTimeMs`, summed **per batch** | Wall-clock tool I/O for the turn | Per-tool latency; concurrent tools in one batch are one measurement |
| `TerminationReason` | `AgentLoopRunner`: `canceled` \| `budget_exhausted` \| `iteration_limit` \| `completed` | What the **harness loop** did | What the model intended |
| `ProviderFinishReason` | The provider's verbatim reason for the final model call | What the **provider** said | Nothing when null — "not recorded", never "stopped normally" |
| The five fingerprints — `CandidateSystemPromptSha256`, `ToolGuidesSha256`, `KnowledgeBaseHeadSha`, `WikiHeadSha`, `SourceCodeHeadSha` | `BenchmarkService.PopulateInstrumentFingerprint`; the last three via `GitHelper.GetGitHeadSha` on `KbPath`, `WikiPath`, `SourceCodePath` | Which revision of three of the five corpora the run read | Anything about the two NetHack corpora, which have no fingerprint (§ 5, and `server_tool_data_sources` § 6). Null means **not recorded** — never "no corpus", never "unchanged" |

**Two parsing traps in `ToolCallSummary`.** It can carry a `(N blocked by budget)` parenthetical, or read `None (N blocked by budget)` — so Σ must sum only the `name×count` pairs. And that parenthetical **never appears on a benchmark answer**: `BenchmarkService` detects blocked calls by matching the error text *"Maximum tool calls per session exceeded"*, while `ToolExecutor` emits that string only when `ToolExecutionContext.ToolBudgetScopeId` is null. Every benchmark call site sets one (`bench_<runId>_q<orderIndex>`), so a benchmark refusal carries the per-question message instead and the suffix is a chat-path artifact.

> 🛑 **Arguments and results are not stored, and `ShowDebugLog` is `false` in every benchmark path.**
>
> `ChatMessageToolCall.ArgsText` and `.Result` — which the production chat *does* persist — are never written for a benchmark answer, because a benchmark run creates **no `ChatMessage` rows at all**. `AgentRunRequest.ShowDebugLog` is hardcoded `false` at all **eight** benchmark call sites in `BenchmarkService` (candidate answer, retry, difficulty assessment, claim verification and the rest). **"Compare the tool use parameters and results" therefore cannot be done from stored benchmark data.** It is done by reconstruction (§ 6) and replay (§ 7), and a finding must say which — never imply a transcript exists.

---

## 3. The Derived Failure Count

No report section surfaces the number of tool calls that failed **technically** — neither refused by the budget nor successful. It is arithmetic:

```
technical failures = ToolCallCount − Σ(ToolCallSummary name×count) − ToolCallsBlocked
```

`ToolCallCount` counts attempts; `ToolCallSummary` counts successes; `ToolCallsBlocked` counts budget refusals. **A non-zero result is direct evidence for question 2, and it is invisible in the report.** Compute it first, for every answer, before reading anything else.

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
| **Attempted and errored** | Derived failure count > 0 (§ 3) | Which tool errored is **not recorded** — the summary lists only successes. Attribute by replay (§ 7), never by assumption |
| **Succeeded and returned nothing** | Present in `ToolCallSummary` **and** the answer shows no content from it, or the answer paraphrases a not-found message | A `Success = true` "not found" is a success. Whether it means "absent from the game" or "absent from the index" is § 5 |
| **Succeeded with data** | Present in `ToolCallSummary` and the answer carries content or citations traceable to it | Then any defect is in the *content* — a corpus question (§ 5) or a suite question, not a tool question |

---

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

Because arguments were never stored (§ 2), a tool-layer finding usually rests on a **reconstruction**: the most probable argument set, inferred from three sources together.

- **The question text** — the entity, mechanic or file the model was asked about, which constrains `query`, `article`, `name` or `file_path`.
- **The answer's own citations** — a quoted file and line, an article title, a stat block. These are the strongest evidence, because the model can only cite what a result contained.
- **The tool's schema and defaults** — `server_tool_parameter_reference` § 4–§ 7 for what was required, what was optional, and what the handler filled in.

> 🛑 **A reconstruction is a hypothesis and must be labelled one in the finding.** Write "reconstructed arguments (hypothesis)", never "the run called `source_code_search` with …". A reconstruction that is presented as a recorded call is indistinguishable from a fabricated citation by anyone reading the finding later, and this repository has already had to withdraw a benchmark finding built on a sentence the system prompt never contained — the withdrawal of T3 in `server_benchmark_to_chat_transfer` § 2. Reconstruction earns its place only when it is visibly provisional.

A reconstruction is never the end of a finding. It is the input to § 7.

---

## 7. Replay, in Three Fidelity Tiers

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
- Touches **no external AI API**, so it needs no `[Trait("Category", "UsesExternalApi")]` and costs nothing to run — see [`testing_guidelines`](../testing_guidelines/SKILL.md) § 1 for the trait convention and the mandatory `--filter "Category!=UsesExternalApi"`, and § 5 for the `InitializationTask` rule this pattern implements.

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
- **Truncation markers absent or explained** — `... [Result truncated for length]`, `[Tool output truncated: cumulative turn limit reached]`, `[Tool output omitted: cumulative turn limit reached]`, or a `Result too large (N chars)` refusal on oversized JSON. Which budget produced which marker is owned by [`tool_execution_architecture`](../tool_execution_architecture/SKILL.md) § 2. A truncated result that the model then answered from incompletely is a limits finding, not a knowledge gap.
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

**Limits of this pass** — state this, or its equivalent, in every tool-diagnostics output:

> This pass reads stored run columns only. **Arguments and results were never stored** for any benchmark tool call (`ShowDebugLog` is `false` at every benchmark call site, and a run creates no `ChatMessage` rows), so every statement about a call's parameters or its returned content is a reconstruction or a replay, labelled as such. **Two corpora reachable from this run carry no fingerprint** — the NetHack source (`NetHackSourceCodePath`) and the NetHack wiki (`NetHackWikiPath`) — so any NetHack finding rests on `StartedAtUtc` against the corpus as it stands now. **Any run before harness 16 has no GnollHack wiki or GnollHack source provenance at all**: `BenchmarkAssessmentPrompt.HarnessVersion` is `"16"`, `WikiHeadSha` and `SourceCodeHeadSha` were added in that version, no historical row is backfilled and none can be, and null in either column means *not recorded*.

---

## 11. Anti-Patterns

| Anti-pattern | Why it is wrong |
|---|---|
| Inferring call **ordering** from `ToolCallSummary` | Ordering is not recorded. The report says so itself: *"`ToolCallSummary` records aggregate call counts, not an ordered execution log (ordering is not recorded)"* — `BenchmarkReportBuilder`, in the tool usage section. Cite that line rather than re-deriving the point |
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
- [`docs/overseer/ai-benchmark.md`](../../../docs/overseer/ai-benchmark.md) — § **Harness Version 16 Updates** for the two corpus fingerprints, why they are provenance rather than comparability keys, and the two corpora that still have none
