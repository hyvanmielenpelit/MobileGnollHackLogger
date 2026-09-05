---
name: overseer_chat_response_timing
description: Architecture and guidelines for Overseer AI chat response duration and time-to-first-token (TTFT) timing indicators, database persistence, streaming events, and UI lifecycle.
---

# Overseer Chat Response Timing Architecture

This document defines the architectural specification, database persistence model, streaming event lifecycle, and UI display standards for response timing metrics (Time-to-First-Token and Total Duration) in Overseer AI chat messages.

---

## 1. Overview & UI State Lifecycle

In the Overseer chat interface, assistant messages display timing indicators in the header (under or alongside the model name and thinking badge). The timing indicator follows a **three-phase lifecycle**:

```
Phase 1: [Initial Call]               Phase 2: [Streaming / Tools]            Phase 3: [Complete]
┌───────────────────────────┐         ┌───────────────────────────┐          ┌───────────────────────────┐
│ Overseer      just now    │         │ Overseer      just now    │          │ Overseer      just now    │
│ Gemini 2.5 Flash          │   ───►  │ Gemini 2.5 Flash          │   ───►   │ Gemini 2.5 Flash          │
│ [ 🔄 Spinner ]            │         │ 9s → [ 🔄 Spinner ]       │          │ 9s → 30s                  │
└───────────────────────────┘         └───────────────────────────┘          └───────────────────────────┘
```

### Three-Phase Progression:
1. **Phase 1 (Initial Request Sent)**:
   - Overseer starts calling the AI provider API.
   - `timeToFirstTokenMs === null` and generation is in progress.
   - UI displays a small spinner (`.gh-spinner-small`).
2. **Phase 2 (First Token / Content Received)**:
   - The first token, thinking chunk, tool start, or error arrives from the AI provider.
   - TTFT is captured (e.g. 9,120 ms -> formatted as `9s`).
   - `timeToFirstTokenMs !== null` and generation is still streaming.
   - UI displays `9s→` followed immediately by the small spinner (`.gh-spinner-small`).
3. **Phase 3 (Generation & Tool Execution Complete)**:
   - The stream completes, all tool iterations finish, and the final response is saved.
   - Total duration from the initial API call start to the very end of the reply is captured (e.g. 30,450 ms -> formatted as `30s`).
   - The second spinner disappears and is replaced by the total duration: `9s→30s`.

---

## 2. Database Persistence Model & Best Practices

### Why Both Metrics Must Be Stored in the Database:
1. **Persistence Across Reloads & Session Switching**:
   - When a user refreshes the page or loads an existing chat session via `GET /api/chat/sessions/{id}`, completed messages must retain and render the full `9s→30s` timing. Without storing `TotalDurationMs` in the database, historical messages would lose the total duration metric or revert to displaying only `9s`.
2. **Data Consistency**:
   - `TimeToFirstTokenMs` is already stored in the `ChatMessage` table. Storing `TotalDurationMs` (or `DurationMs`) provides parity between the latency phase (time to start) and the total execution phase (time to completion).
3. **Analytics & Performance Monitoring**:
   - Storing both values allows telemetry on provider latency, token generation speeds, and tool execution overhead across different models, providers, and prompt lengths.

### Data Schema (`GnollHackServer.Data`):
In `ChatMessage.cs`:
```csharp
public class ChatMessage
{
    // ... other properties ...

    public int? TimeToFirstTokenMs { get; set; }
    
    public int? TotalDurationMs { get; set; }
}
```

### EF Core Migration Rules:
- Migrations MUST be targeted to `GnollHackServer.Data` using startup project `MobileGnollHackLogger`:
  ```bash
  dotnet ef migrations add AddChatTotalDurationMs -p GnollHackServer.Data -s MobileGnollHackLogger -o Migrations
  dotnet ef database update -p GnollHackServer.Data -s MobileGnollHackLogger
  ```
- Update the covering index in `ApplicationDbContext.cs` on `(ChatSessionId, TimestampUtc)`:
  ```csharp
  SqlServerIndexBuilderExtensions.IncludeProperties(
      b.HasIndex("ChatSessionId", "TimestampUtc"), 
      new[] { "Role", "IsHidden", "ModelUsed", "ProviderUsed", "TimeToFirstTokenMs", "TotalDurationMs" });
  ```

---

## 3. Backend Timing Measurement & Event Flow

### Measurement Anchors in `ChatService.cs`:
- **Start Timestamp**: `apiCallStartTime = System.Diagnostics.Stopwatch.GetTimestamp()` is recorded immediately before the first HTTP POST to the AI provider. Even across multiple tool iterations within the generation loop, `apiCallStartTime` retains the initial start timestamp.
- **TTFT Capture**: Upon receiving the first stream event of type `chunk`, `thinking_chunk`, `tool_call_complete`, or `error`:
  ```csharp
  if (!timeToFirstTokenMs.HasValue && (evt.Type == "chunk" || evt.Type == "thinking_chunk" || evt.Type == "tool_call_complete" || evt.Type == "error"))
  {
      timeToFirstTokenMs = (int)System.Diagnostics.Stopwatch.GetElapsedTime(apiCallStartTime!.Value).TotalMilliseconds;
      yield return new ChatEvent { Type = "ttft", Data = timeToFirstTokenMs.Value.ToString() };
  }
  ```
- **Total Duration Capture**: When the loop terminates and the full response is built (after thinking boundaries are wrapped and right before saving to DB):
  ```csharp
  int? totalDurationMs = apiCallStartTime.HasValue 
      ? (int)System.Diagnostics.Stopwatch.GetElapsedTime(apiCallStartTime.Value).TotalMilliseconds 
      : null;
  ```
- **Broadcast**:
  - `ChatService` yields a `duration` event: `yield return new ChatEvent { Type = "duration", Data = totalDurationMs.Value.ToString() };`
  - When saving `asstMsg` to `dbContext.ChatMessage`, persist `asstMsg.TimeToFirstTokenMs = timeToFirstTokenMs;` and `asstMsg.TotalDurationMs = totalDurationMs;`.

---

## 4. API & Controller DTOs

### Session Loading (`ChatController.cs`):
In `GetSession(long id)`:
```csharp
var messages = await _dbContext.ChatMessage
    .Where(m => m.ChatSessionId == id && m.Role != "system" && !m.IsHidden)
    .OrderBy(m => m.TimestampUtc)
    .Select(m => new { 
        m.Id, 
        m.Role, 
        m.Content, 
        m.TimestampUtc,
        m.TimeToFirstTokenMs,
        m.TotalDurationMs,
        m.ProviderUsed,
        m.ModelUsed
    })
    .ToListAsync();
```

---

## 5. Frontend Architecture & Formatting

### Data Types (`chat.service.ts`):
```typescript
export interface ChatMessage {
  id?: number;
  role: string;
  content: string;
  timestampUtc?: string;
  attachments?: ChatAttachment[];
  toolCalls?: ToolCall[];
  modelDisplayName?: string;
  thinkingLevel?: string;
  timeToFirstTokenMs?: number;
  totalDurationMs?: number;
}
```

### Component State & Formatting (`chat.component.ts`):
```typescript
timeToFirstTokenMs: number | null = null;
totalDurationMs: number | null = null;

// Formatting helper (formatTtft already exists for single values)
formatDuration(ttftMs: number | null | undefined, totalMs: number | null | undefined): string {
  const ttftStr = this.formatTtft(ttftMs);
  const totalStr = this.formatTtft(totalMs);
  if (ttftStr && totalStr) {
    return `${ttftStr}→${totalStr}`;
  }
  return ttftStr || totalStr || '';
}
```

### Event Handling in `setupSignalR()` / `processChatEvent()`:
```typescript
else if (evt.type === 'ttft') {
  this.timeToFirstTokenMs = parseInt(evt.data, 10);
  this.cdr.detectChanges();
} else if (evt.type === 'duration') {
  this.totalDurationMs = parseInt(evt.data, 10);
  this.cdr.detectChanges();
} else if (evt.type === 'done') {
  // ...
  this.messages.push({
    role: 'assistant',
    content: this.streamingMessage,
    timestampUtc: new Date().toISOString(),
    toolCalls: [...this.streamingToolCalls],
    modelDisplayName: this.selectedModel?.displayName || this.selectedModel?.modelId || this.singleModelInfo?.modelId,
    thinkingLevel: this.selectedModel?.thinkingLevel || this.singleModelInfo?.thinkingLevel,
    timeToFirstTokenMs: this.timeToFirstTokenMs ?? undefined,
    totalDurationMs: this.totalDurationMs ?? undefined
  });
  
  this.timeToFirstTokenMs = null;
  this.totalDurationMs = null;
  // ...
}
```

### Template Rendering (`chat.component.html`):

#### 1. Persisted Messages:
```html
@if (msg.role === 'assistant' && (msg.timeToFirstTokenMs != null || msg.totalDurationMs != null)) {
  <div class="ttft-container" style="text-align: right; margin-top: 2px;">
    <span class="model-name-subtext">{{ formatDuration(msg.timeToFirstTokenMs, msg.totalDurationMs) }}</span>
  </div>
}
```

#### 2. Streaming Bubble:
```html
<div class="ttft-container" style="text-align: right; margin-top: 2px;">
  @if (timeToFirstTokenMs === null) {
    <span class="model-name-subtext">
      <span class="gh-spinner-small" style="display:inline-block; width: 10px; height: 10px; border-width: 1.5px;"></span>
    </span>
  }
  @if (timeToFirstTokenMs !== null && totalDurationMs === null) {
    <span class="model-name-subtext">
      {{ formatTtft(timeToFirstTokenMs) }}→<span class="gh-spinner-small" style="display:inline-block; width: 10px; height: 10px; border-width: 1.5px; margin-left: 2px;"></span>
    </span>
  }
  @if (timeToFirstTokenMs !== null && totalDurationMs !== null) {
    <span class="model-name-subtext">
      {{ formatDuration(timeToFirstTokenMs, totalDurationMs) }}
    </span>
  }
</div>
```

---

## 6. Backward Compatibility & Edge Cases

1. **Legacy Messages**: Older messages saved in the database before the migration will have `TimeToFirstTokenMs` populated but `TotalDurationMs == null`. `formatDuration` displays `9s` cleanly without trailing arrows.
2. **Immediate Errors**: If an API call fails before returning tokens, TTFT is recorded upon receiving the error event, and the total duration reflects the failure time.
3. **Multi-Iteration Tool Calls**: When tools execute across multiple iterations, `apiCallStartTime` remains anchored at the very beginning of the first API call, ensuring the total time correctly reflects the entire end-to-end user wait time.
4. **Reconnection & Silent State Sync**: When a client reconnects to an ongoing stream, replaying buffered `ttft` events seamlessly initializes `timeToFirstTokenMs`, putting the client in Phase 2 (`9s→[spinner]`) without jumping or resetting.

---

## 7. Context Window & Whole-Turn Cost Accounting

For the full specification, see [docs/overseer/chat-response-telemetry.md](../../docs/overseer/chat-response-telemetry.md).

### Whole-Turn Costing Basis
Chat turn costs are computed from the **summed token counts across all tool iterations**:
- `runResult.UncachedInputTokens`
- `runResult.OutputTokens`
- `runResult.CacheReadTokens`
- `runResult.CacheCreationTokens`

> [!WARNING]
> Never cost from `LastPromptTokens` or `LastOutputTokens`. Those represent the final call in the tool loop and exist solely to measure remaining context window capacity. In multi-turn tool sessions, final-call costing would dramatically understate actual API usage.

### Streaming `cost` Event
Emitted by `ChatService` when response generation finishes:
```csharp
yield return new ChatEvent
{
    Type = "cost",
    Data = JsonSerializer.Serialize(new
    {
        estimatedCost,
        currency = pricing.Currency ?? "USD",
        source = pricing.Source, // "custom" | "catalog"
        inputTokens,
        outputTokens,
        cacheReadTokens,
        cacheCreationTokens,
        isOperatorCost // true when executing on a system configuration
    })
};
```

### Operator Cost Distinction (`isOperatorCost`)
When a user chats using an operator-provided system model, `isOperatorCost` is `true`. The UI displays `(operator)` next to the cost figure so users clearly understand that server operator credits were used, not personal user billing.

### Persistence & UI
- Persisted on `ChatMessage`: `EstimatedCost` (precision 18, 8), `CostCurrency`, `PricingSource`, and the four token counts. Snapshotted on completion; never recomputed on read.
- Gated in UI by user preference `showChatCost` (under Settings).
- Rendered in `.ttft-container` in message headers and in the streaming footer.
- Running conversation total in footer is explicitly labelled as `Total cost (loaded)` to prevent confusion when older messages have been pruned by chat retention policies.
