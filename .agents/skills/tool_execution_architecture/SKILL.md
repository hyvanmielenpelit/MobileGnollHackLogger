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
| **`ToolExecutor`** | Session Quota | `AiPerformanceSettings:MaxCallsPerSession:Default` | `30` | Atomic rate limiter per session. |
| **`ToolExecutor`** | Per-Tool Timeout | `handler.TimeoutSeconds` | Configured per tool | Enforced via linked `CancellationTokenSource`. |
| **`ToolExecutor`** | Per-Tool Size | `ToolExecutionContext.MaxResultLength` | `3,000` chars | Clamps single tool result with `... [Result truncated for length]`. |
| **`ToolBatchResultBudget`** | Batch Budget | `ToolExecutionLimits:MaxBatchResultLength` | `40,000` chars | Dynamically scales to `Math.Max(budgetChars, MaxResultLength)`. |

---

## 3. Tool Classification & Execution Locations

Tools are defined via `IToolHandler` implementations registered in `ToolRegistry`.

- **Standard Server Tools**: Execute directly within the ASP.NET Core backend (e.g. `WikiSearchTool`, `SourceCodeSearchTool`, `MonsterLookupTool`).
- **Client Tools (`IsClientTool = true`)**: Execute on the player's client via the interactive SignalR / WebSockets client bridge (`ToolExecutionLocation.Client`). Throttled to 1 concurrent call by default to avoid race conditions in client UI.
- **External Lookup Tools (`ToolCategory.ExternalLookup`)**: Outbound network requests to external APIs (e.g. GitHub API tools). Regulated by `_externalLookupThrottler` to prevent IP rate-limiting.

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
- `... [Result truncated for length]` — Result exceeded per-tool `MaxResultLength`.

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
- Test process throttles and session quotas using mocked `IConfiguration` and `IToolHandler` lists.

### 4. `ToolResultHandlingTests.cs`
- Test single-tool result clamping, JSON payload protection, and error payload creation.

### 5. `SystemPromptPolicyTests.cs`
- Assert that `ToolGuides/_policy.md` exists and contains mandatory batching and accuracy directives.
