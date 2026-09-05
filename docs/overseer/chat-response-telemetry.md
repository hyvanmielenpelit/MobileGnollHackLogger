# Chat Response Telemetry & Cost Accounting Specification

This document details the architectural specification, database persistence model, streaming event lifecycle, and UI display standards for response telemetry (Time-to-First-Token, Total Duration, Context Window usage, and Token Cost Accounting) in Overseer AI chat messages.

---

## 1. Overview & Operational Telemetry

In the Overseer chat interface, assistant messages display operational telemetry in the message header (under the model name and thinking badge) and during active generation:

1. **Response Timing**: TTFT (Time-to-First-Token) and Total Duration (e.g. `9s → 30s`).
2. **Context Window Usage**: Real-time gauge and token usage of context window consumed.
3. **Turn & Conversation Cost**: Per-message estimated cost, live streaming cost indicator, conversation total for loaded messages, and operator vs. user cost attribution.

```
┌────────────────────────────────────────────────────────────┐
│ Overseer                                          just now │
│ GPT-5.6 Luna                                               │
│                                           9s → 14s · $0.03 │
│ ... assistant message content ...                          │
└────────────────────────────────────────────────────────────┘
```

---

## 2. Chat Cost Accounting Architecture

### Whole-Turn Token Basis

Overseer calculates the cost of an assistant response on a **whole-turn basis**, summing token counts across all intermediate tool iterations:
- `runResult.UncachedInputTokens`
- `runResult.OutputTokens`
- `runResult.CacheReadTokens`
- `runResult.CacheCreationTokens`

> [!IMPORTANT]
> **Whole-Turn vs. Final-Call**: Overseer deliberately does **not** cost from `runResult.LastPromptTokens` or `runResult.LastOutputTokens`. Those metrics reflect only the final model call in a multi-turn tool loop and exist solely to measure the remaining context window. In a turn with 5–10 tool iterations, costing only the final call would understate the actual compute cost by a large factor. The whole-turn token counts match the figures passed to `SystemAiConfigService.RecordUsageAsync`.

### Pricing Resolution Precedence

When a chat turn completes, `ModelPricingService` resolves pricing in priority order:
1. **Custom Override**: Configured on the active `SystemAiApiConfiguration` (for system models) or `UserAiModel` (for personal models) with `pricingMode = "custom"` and valid input/output prices.
2. **Provider Catalog Default**: The `pricing` object declared in `OpenAiModelCatalog.json`, `AnthropicModelCatalog.json`, or `GoogleModelCatalog.json`.
3. **null (Not Available)**: If neither exists, cost estimation is omitted (never treated as zero).

### Streaming `cost` Event

During stream completion (in `ChatService.cs`), Overseer broadcasts a dedicated streaming event alongside the existing `context` event:

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

The `cost` event is separated from the `context` event because context window measurement requires known model limits and prompt token counts, whereas cost estimation requires resolved pricing.

### Operator vs. User Cost Attribution (`isOperatorCost`)

When an assistant turn executes using an operator-provided system model (`systemModelId.HasValue`):
- `isOperatorCost` is set to `true`.
- The chat UI displays an `(operator)` badge next to the cost figure (e.g. `$0.04 (operator)`).
- This explicitly signals that the hosting operator absorbed the API charges, preventing end users from misunderstanding the figure as a personal charge or billing deduction.

When executing via a user's personal API key (`UserAiModel`), `isOperatorCost` is `false`, displaying the personal cost (e.g. `$0.04`).

### Database Persistence Model

In `ChatMessage` (`GnollHackServer.Data`):
```csharp
public decimal? EstimatedCost { get; set; }       // precision: (18, 8)
public string? CostCurrency { get; set; }
public string? PricingSource { get; set; }       // "custom" | "catalog"
public long? InputTokens { get; set; }
public long? OutputTokens { get; set; }
public long? CacheReadTokens { get; set; }
public long? CacheCreationTokens { get; set; }
```

Costs and token counts are **snapshotted at turn completion and never recomputed on read**. If catalog pricing or configuration overrides change in the future, historical message costs remain accurate to the time of generation.

---

## 3. UI Display & Settings Lifecycle

### Settings Toggles

Under **Settings → Preferences**:
- **Show context window usage**: Controls display of the context usage progress bar in the chat footer.
- **Show estimated cost**: Controls visibility of all cost indicators (per-message header, live streaming cost, and conversation total).

### UI Presentation Surfaces

1. **Per-Message Subtext**:
   In `chat.component.html` within `.ttft-container`:
   ```html
   @if (showChatCost && msg.estimatedCost != null) {
     <span class="cost-subtext"> · {{ formatCost(msg.estimatedCost, msg.costCurrency) }}</span>
     @if (msg.isOperatorCost) {
       <span class="operator-cost-badge" title="Cost was paid by the system operator">(operator)</span>
     }
   }
   ```
2. **Live Streaming Footer**:
   While the response streams, the streaming footer shows the live estimated cost once the `cost` event arrives, next to the live TTFT and elapsed duration.
3. **Loaded Conversation Total**:
   Next to the context window indicator in the chat footer, Overseer renders:
   `Total cost (loaded): $0.12`
   The total is explicitly labelled **(loaded)** because chat retention policies or soft-deletion may prune earlier messages in long sessions, and the displayed total accurately reflects the sum of currently loaded messages.

---

## 4. Response Timing (TTFT & Duration)

Timing follows a three-phase lifecycle:
1. **Initial Request**: Spinner displayed while waiting for first token.
2. **First Token Received**: TTFT captured and emitted via `ttft` event (e.g. `9s→[spinner]`).
3. **Generation Complete**: Total duration captured and emitted via `duration` event (e.g. `9s→30s`). Both metrics are persisted to `ChatMessage.TimeToFirstTokenMs` and `ChatMessage.TotalDurationMs`.

---

## 5. Context Window Usage

When `showContextWindowUsage` is enabled:
- `ChatService` emits a `context` event containing `lastPromptTokens` and `contextWindowTokens`.
- The chat footer renders a visual progress bar indicating percentage of the window consumed.
