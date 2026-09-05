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

> [!IMPORTANT]
> **USD-only policy.** Overseer prices exclusively in **USD**. The provider catalogs carry no
> `currency` field, custom prices entered on a model are always USD, and `ModelPricingService`
> supplies the literal `"USD"` at every resolution path. `ModelPricing.Currency` and
> `ChatMessage.CostCurrency` are retained as constants — they keep historical rows readable and
> feed benchmark reporting, but no code path can produce any other value.

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
- The chat UI displays an `(operator)` suffix next to the cost figure (e.g. `4.00¢ (operator)`).
- This explicitly signals that the hosting operator absorbed the API charges, preventing end users from misunderstanding the figure as a personal charge or billing deduction.

When executing via a user's personal API key (`UserAiModel`), `isOperatorCost` is `false`, displaying the personal cost (e.g. `4.00¢`).

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

### Session-Level Cost Persistence

`ChatSession.TotalEstimatedCost` (`decimal(18, 8)`, nullable) holds the running total of the
session's assistant turns in USD. `ChatSessionCostAccumulator.Apply` folds each completed turn's
cost into it inside the **same** `SaveChangesAsync` that persists the assistant message, so the
message and the total cannot drift apart. An unpriced turn is skipped entirely: the total is
never nudged to zero by a model with no configured pricing, and a session with no priced turn at
all stays `NULL`.

Like per-message costs, the total is **snapshotted and never recomputed**. It is deliberately
**not** backfilled — every chat predating the column reads `NULL`, and the client falls back to
summing the per-message costs it has loaded. The column is denormalized on purpose: it must
survive independently of which messages a client happens to load, and it is removed together with
the session by the existing retention purge path.

---

## 3. UI Display & Settings Lifecycle

### Settings Toggles

Under **Settings → Preferences**:
- **Show context window usage**: Controls display of the context usage progress bar in the chat footer.
- **Show chat cost**: Controls visibility of every cost indicator at once — the per-reply figure on each assistant message, the live streaming cost, and the chat total above the prompt box.

### Cost Formatting Rule

All three surfaces share **one** formatter, `ChatComponent.formatCost`:

| Cost | Rendered as |
|------|-------------|
| At or above $1 | `$1.23` |
| Below $1 | `0.85¢` — cents, two decimals |
| Above zero but below a hundredth of a cent | `<0.01¢` |
| Unknown (`null`) | nothing at all — the surface is hidden |

A single reply, and often a whole short chat, costs a fraction of a cent; rendering that as
`$0.00` would read as free. The dollar form takes over at $1, where the distinction starts to
matter. A legacy row carrying a non-USD `CostCurrency` falls through to `0.0500 EUR` form; no
current code path can produce one.

### UI Presentation Surfaces

1. **Per-Reply Cost**:
   The bottom-left corner of each assistant message box, in `.msg-cost-footer`, with an
   `(operator)` suffix when the hosting operator paid. It is **no longer** in the top-right
   `.ttft-container`, which now carries timing only:
   ```html
   @if (msg.role === 'assistant' && showChatCost && msg.estimatedCost != null) {
     <div class="msg-cost-footer" title="Estimated cost of this response">
       {{ formatCost(msg.estimatedCost, msg.costCurrency) }}@if (msg.isOperatorCost) {<span class="msg-cost-operator">(operator)</span>}
     </div>
   }
   ```
2. **Live Streaming Cost**:
   The same `.msg-cost-footer`, on the streaming message box, driven by `liveCost` /
   `liveCostCurrency` / `liveIsOperatorCost` once the `cost` event arrives.
3. **Chat Total**:
   `Chat cost 1.85¢` in the telemetry bar above the prompt box, sharing the row with the
   context window indicator. The value is `ChatSession.TotalEstimatedCost` plus the in-flight
   turn, falling back to the sum of loaded per-message costs for a chat saved before that column
   existed. A **PARTIAL** badge marks a chat in which some assistant turn ran unpriced, with a
   tooltip saying so. When no cost is known anywhere the whole indicator is hidden — there is no
   "not available" text, and the context indicator stays right-aligned.

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
