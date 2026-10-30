---
name: tool_execution_architecture
description: Architectural guide for Overseer's tool batching pipeline, concurrency throttles (ToolBatchRunner and ToolExecutor), batch output budgets (ToolBatchResultBudget), real-time Channel streaming, and testing patterns.
---

# Tool Execution & Batching Architecture for Overseer

This skill guides AI agents and developers working on Overseer's tool execution engine, adding new tool definitions, tuning performance limits, debugging result truncation, or writing automated tests for parallel execution.

---

## 1. Architectural Overview & Context

Overseer uses a unified multi-provider tool execution engine that allows AI models to call multiple tools in a single response turn. 

### Key Execution Lifecycle Steps:
1. **Provider Tool Extraction**: The AI provider (`GoogleProvider`, `AnthropicProvider`, `OpenAiResponsesProvider`) extracts 1..N tool calls from the model turn into `ToolBatchItem` records.
2. **Session Batch Dispatch**: `ChatService` dispatches items to `ToolBatchRunner.RunAsync`.
3. **Throttling & Execution**:
   - `ToolBatchRunner` applies per-session concurrency limits (`maxParallelTools`, `maxParallelClientTools`).
   - `ToolExecutor.ExecuteAsync` applies session rate limits, process-level concurrency limits, and category throttles (`_externalLookupThrottler`).
4. **Real-Time Stream**: As each tool finishes, a `ToolBatchOutcome` record is pushed to `Channel<ToolBatchOutcome>`, streaming `tool_result`, `tool_error`, or `debug` SSE events to the frontend.
5. **Output Budget Allocation**: Completed results are evaluated against `ToolBatchResultBudget` (default: 40,000 characters) before appending to LLM message history.

---

## 2. Throttling Hierarchy Reference

When modifying limits or investigating latency bottlenecks, refer to the multi-tier throttling hierarchy:

| Component | Scope | Config Key / Constant | Default | Notes |
|---|---|---|---|---|
| **`ToolBatchRunner`** | Session Batch | `AiPerformanceSettings:MaxParallelToolCalls:Default` | `6` (Min 1, Max 10) | Global concurrency for standard server-side tools in a single turn. |
| **`ToolBatchRunner`** | Client Tools | `AiPerformanceSettings:MaxParallelClientToolCalls:Default` | `1` (Min 1, Max 4) | Serializes interactive client-bridge tools (`IsClientTool`). |
| **`ToolExecutor`** | Process-Wide | `ToolExecutionLimits:MaxProcessParallelToolCalls` | `30` | Semaphore across all active user sessions on the server. |
| **`ToolExecutor`** | External APIs | `ToolExecutionLimits:MaxProcessExternalLookupCalls` | `3` | Throttler for `ToolCategory.ExternalLookup` tools. |
| **`ToolExecutor`** | Session Quota | `AiPerformanceSettings:MaxCallsPerSession:Default` | `150` (Min 5 / Max 500; `Benchmark:MaxCallsPerSession` is 50) | Atomic rate limiter per session. Skipped for a handler whose `CountsTowardCallBudget` is `false` (§ 3). |
| **`ToolExecutor`** | Per-Tool Timeout | `handler.TimeoutSeconds` | Configured per tool | Enforced via linked `CancellationTokenSource`. |
| **`ToolExecutor`** | Per-Tool Size | `ToolExecutionContext.MaxResultLength` | `10,000` chars | Clamps a single plain-text tool result with `ToolExecutor.BuildTruncationSuffix`: `... [Truncated: showing {shown} of {total} characters. Narrow the query, or ask for a specific section, to see the rest.]`. |
| **`ToolBatchResultBudget`** | Batch Budget | `ToolExecutionLimits:MaxBatchResultLength` | `40,000` chars | Dynamically scales to `Math.Max(budgetChars, MaxResultLength)`. |

---

## 2a. Turn Limits, Warnings and Notices

What can end or cut a turn in `AgentLoopRunner`, what the model is told first, and the code a chat turn records (`TurnLimitCodes`):

| Limit | Config Key | Default | Warned Before It Hits | At the Limit | Code |
|---|---|---|---|---|---|
| Tool rounds | `AiPerformanceSettings:MaxToolIterations:Default` | `22` | `[Limit notice: …]` at 3 or fewer rounds left, and a last-chance notice at 1 | Forced final call: `ForcedFinalInstruction`, no tools | `research_rounds` |
| Model calls, shared with sub-agents | `SubAgentSettings:MaxTotalModelCalls` | `48` | Counts toward the rounds left, `min(iterations left, model calls left)` | Forced final call | `model_calls` |
| Time | `AiPerformanceSettings:ChatRequestTimeout:Default` | `1,800` s | At 75 % and at 90 % of the time to `TurnDeadlineUtc` | The turn's token is canceled | `time_limit` |
| Cumulative tool output | `ToolExecutionLimits:MaxTurnResultLength` | `120,000` chars | At 80 % | Later results are cut; the stored row keeps the full text | `tool_output` (administrators only) |
| Tool-call budget | `AiPerformanceSettings:MaxCallsPerSession:Default` | `150` per chat, per question in GnollBench | `[Tool budget: …]` at `max(3, 20 %)` remaining | Further calls are refused | `tool_budget` |
| Output tokens | the model's catalog maximum | | Not warned: nothing can be said in the middle of a response | The answer is cut | `answer_length` |

Rate limits and provider failures are not warned, because the model cannot act on them, and neither are per-result and per-batch sizes, whose markers `_policy.md` explains. Provider failures record `provider_busy`, `provider_overloaded`, `provider_timeout`, `provider_error`, `connection_lost`, `api_key_rejected` or `api_key_balance`. A turn that ends with no answer records `no_final_answer`.

**Three request flags, all off by default, so graders, generators, report writers and title generation are unchanged:**

| `AgentRunRequest` Member | Set By | Effect |
|---|---|---|
| `ApplyTurnGuidance` | `ChatService`, both GnollBench candidate sites, `DelegateToSubAgentTool` | Limit warnings and recovery: what the model experiences. A change to `TurnGuidance` needs a harness bump |
| `TurnDeadlineUtc` | `ChatService` from `OngoingGenerationState.DeadlineUtc`, the candidate sites | The time warning's clock. Sub-agents leave it null and get no time notice |
| `ReportTurnLimits` | `ChatService` only | Hits become `AgentRunResult.LimitHits`, notices and event rows; a timeout or a broken stream ends the loop normally; provider error text stays out of the conversation |

**Warnings (`TurnGuidance`).** At most one notice block per batch, combining whatever is due, on the **last** tool result of the batch. Each fires once per turn. The block goes into `providerResults` only: the stored row and the `tool_result` event hold `outcome.Content`, so the user never sees it, later turns do not replay it, and the cached prompt prefix is not disturbed. It does not count against the output ceilings. The `[Tool budget: …]` note keeps its existing once-per-iteration path, and says "in this chat" when there is no budget scope, with a sub-agent's real total.

**Recovery for an unfinished answer.** After a tool-free iteration, under `ApplyTurnGuidance`, when the turn has used tools, no error event occurred, the token is not canceled, `toolIterations < maxToolIterations`, no forced final instruction was sent and the model-call budget has room, one of three triggers earns **one** more call with tools off and `ForcedFinalInstruction`:

- the stream closed early: the provider reported `call_meta` without a completion marker (every production provider reports it), which with `ReportTurnLimits` also records `connection_lost`;
- the iteration has no visible text;
- the last OpenAI message phase is `commentary` (`assistant_phase`, an internal event the runner consumes).

The iteration's visible text moves into a thought and joins the history through `IAiProvider.AppendAssistantTextToHistory`, not `AppendAssistantToolCallsToHistory`, whose empty tool list Anthropic and Gemini reject. With empty text and no items that method appends nothing, and a reasoning-only OpenAI item list is not replayed. If the recovery call answers, a `connection_lost` hit is marked `Recovered`: it still writes an event row, and the user sees no notice for it. If the final text is still empty, or the turn ends on `commentary` after tools, `EndedWithoutFinalAnswer` is set and a chat turn records `no_final_answer`. `TerminationReason` keeps its four values, and a recovered turn stays `completed`.

**Provider errors under `ReportTurnLimits`.** Every `error` event from the provider path becomes a hit, without being appended to `Content` or yielded; `CaptureStreamError` and `CaptureErrorResponse` read it first, so the attempt ledger's evidence is unchanged. An `error` carrying an `ErrorCode` (`model_unavailable`) stays, because its text is safe and the composer notice reads it. No event carries `Detail`. The sanitizing sits here, never in the providers, because GnollBench parses the same error text for its answer status. Text and audiences: `ChatTurnNoticeCatalog`, `ChatTurnNoticeDisclosure`, `docs/overseer/chat-response-telemetry.md` § 6.

---

## 3. Tool Classification & Execution Locations

Tools are defined via `IToolHandler` implementations registered in `ToolRegistry`.

- **Standard Server Tools**: Execute directly within the ASP.NET Core backend (e.g. `WikiSearchTool`, `SourceCodeSearchTool`, `MonsterLookupTool`).
- **Client Tools (`IsClientTool = true`)**: Execute on the player's client via the interactive SignalR / WebSockets client bridge (`ToolExecutionLocation.Client`). Throttled to 1 concurrent call by default to avoid race conditions in client UI.
- **External Lookup Tools (`ToolCategory.ExternalLookup`)**: Outbound network requests to external APIs (e.g. GitHub API tools). Regulated by `_externalLookupThrottler` to prevent IP rate-limiting.
- **Feedback Tools (`ToolCategory.Feedback`)**: `report_feedback` (`ReportFeedbackTool`), the assistant's report to the developers (`docs/overseer/ai-feedback-reports.md`). Three rules attach to the category or its handler:
  - **Declaration.** `ToolRegistry.BuildToolsForRequest` declares a Feedback handler only when `enableFeedbackTool` (from `AgentRunRequest.EnableFeedbackTool`) and `enableToolUse` are both true, **in place of** the allow-list check, so `Benchmark:AllowedTools`, the condition tool lists and the sub-agent catalog never name it. Sub-agents, graders, the claim verifier and the rubric-gap author leave the flag false.
  - **Budget exemption.** `IToolHandler.CountsTowardCallBudget` defaults to `true`; `ReportFeedbackTool` returns `false`. `ToolExecutor.ExecuteAsync` looks the handler up **before** the session-quota block and skips the block for an exempt handler (an unknown tool still fails as before, and the experimental guard still runs after the lookup). `RemainingBudget` stays **null** on that path, so `AgentLoopRunner` appends no `[Tool budget: …]` notice; never set it to `0`, which tells the model its budget is gone. The tool has its own per-run cap, `AgentRunBudget.TryIncrementFeedbackReport(ReportFeedbackTool.MaxReportsPerTurn)`.
  - **Log rule.** `ToolExecutor`'s *Tool Execution Audit* line dumps `parameters.GetRawText()` for every other tool; for `ToolCategory.Feedback` the dump is skipped, because the arguments are conversation-derived text. The handler logs only the category, the issue id and the row id. A new tool whose arguments carry user or conversation text should join this rule rather than log its parameters.

---

## 4. Batch Result Budget & Truncation Handling

When building or debugging tool handlers, remember that `ToolBatchResultBudget` processes results in **call order**:

```csharp
var budget = new ToolBatchResultBudget(Math.Max(budgetChars, execContext.MaxResultLength));
foreach (var outcome in outcomes)
{
    var budgetedContent = budget.Apply(outcome.Content);
}
```

### Truncation Markers:
- `... (truncated: batch output budget reached)` — Result partially fit into remaining budget.
- `(skipped: batch output budget reached)` — Result arrived after budget was completely exhausted.
- `... [Truncated: showing {shown} of {total} characters. Narrow the query, or ask for a specific section, to see the rest.]` — Result exceeded per-tool `MaxResultLength`. Built by `ToolExecutor.BuildTruncationSuffix(shownLength, totalLength)` and shared by both truncation call sites, so the marker is **not** a fixed length: 107 characters of template plus the digits of the two figures.
- `... [Result truncated for length]` — The same per-tool cut in a run recorded before 2026-09-09. Stored benchmark and chat rows from then still carry it, so a diagnostic that matches truncation markers has to accept both. Match the `[Truncated:` prefix rather than a length.

---

## 5. Testing & Synchronization Patterns

When writing or modifying unit tests for tool execution, follow the existing test fixtures in `Overseer.Tests/UnitTests/`:

### 1. `ParallelToolExecutionTests.cs`
- Use `TaskCompletionSource<ToolResult>` to simulate long-running tool operations and assert concurrency boundaries:
  ```csharp
  var outcomeChannel = Channel.CreateUnbounded<ToolBatchOutcome>();
  var task = ToolBatchRunner.RunAsync(items, executor, maxParallelTools: 2, maxParallelClientTools: 1, outcomeChannel.Writer, CancellationToken.None);
  ```

### 2. `ToolBatchResultBudgetTests.cs`
- Test exact character allocations and truncation boundaries:
  ```csharp
  var budget = new ToolBatchResultBudget(100);
  var r1 = budget.Apply(new string('a', 60));
  var r2 = budget.Apply(new string('b', 60));
  Assert.Contains("(truncated: batch output budget reached)", r2);
  ```

### 3. `ToolExecutorRateLimitTests.cs`
- Test process throttles and session quotas using mocked `IConfiguration` and `IToolHandler` lists, including the `CountsTowardCallBudget` exemption (null `RemainingBudget`) and the skipped parameter dump for `ToolCategory.Feedback`.

### 4. `ToolResultHandlingTests.cs`
- Test single-tool result clamping, JSON payload protection, and error payload creation.

### 5. `SystemPromptPolicyTests.cs`
- Assert that `ToolGuides/_policy.md` exists and contains mandatory batching and accuracy directives.

### 6. `AgentLoopRunnerTurnLimitTests.cs`, `AgentLoopRunnerTurnGuidanceTests.cs`, `ChatProviderErrorSanitizingTests.cs`
- Fake-provider tests over `TurnLimitTestHarness`: one hit per limit, the notice schedule, recovery, and a marker string in a provider body that reaches only a live administrator's facts. With the flags off, the events stay byte-identical.
