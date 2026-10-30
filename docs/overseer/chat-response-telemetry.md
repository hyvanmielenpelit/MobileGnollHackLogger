# Chat Response Telemetry & Cost Accounting Specification

This document details the architectural specification, database persistence model, streaming event lifecycle, and UI display standards for response telemetry (Time-to-First-Token, Total Duration, Context Window usage, and Token Cost Accounting) in Overseer AI chat messages.

---

## 1. Overview & Operational Telemetry

In the Overseer chat interface, assistant messages display operational telemetry in the message header (under the model name and thinking badge) and during active generation:

1. **Response Timing**: TTFT (Time-to-First-Token) and Total Duration (e.g. `9s → 30s`).
2. **Context Window Usage**: Real-time gauge and token usage of context window consumed.
3. **Turn & Conversation Cost**: Per-message estimated cost, live streaming cost indicator, conversation total for loaded messages, and operator vs. user cost attribution.
4. **Turn Notices**: Under the message, a notice when a limit or a provider failure ended or cut the turn (§ 6).

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
> **USD-only policy, and the currency fields are gone.** Overseer prices exclusively in **USD**.
> The provider catalogs carry no `currency` field, and custom prices entered on a model are
> always USD. The currency concept has therefore been **removed end to end**: `ModelPricing`
> no longer carries a `Currency` property, the `cost` SSE event no longer carries a `currency`
> field, and `ChatMessage.CostCurrency` was dropped by migration
> `RemoveChatMessageCostCurrency`. Every amount is rendered with a leading `$`.

### Sub-Agent Runs

A turn that calls `delegate_to_subagent` runs a second agent loop, on its own `AgentRunResult`, and
possibly on a different model funded by a different payer. Its spend is part of the turn:

- `DelegateToSubAgentTool` prices each delegated run on **its own** model's card, per call
  (`ModelPricingService.ComputeRunCost`), so long-context cards and service-tier multipliers apply as
  for the main loop. The card follows the payer: a system configuration's own card; the user's
  `UserAiModel` card only when the user's own key funded the run; otherwise the catalog default, since
  a user's custom override describes the user's account and the operator's server key bills at list
  price.
- The result travels back as a `DelegatedRunCost` on the tool result, through `ToolBatchOutcome`, into
  `AgentRunResult.DelegatedRunCosts`. It survives the success, canceled, error and "Result too large"
  paths.
- `ChatTurnCost.Combine` adds the delegated runs to the main loop's cost. A run that made no model
  call is ignored. **A turn is either fully priced or unpriced**: if the main model or any delegated
  run that made a model call has no price card, the whole turn is unpriced (`null`), because keeping
  the priced parts would quietly understate it.

The four token fields of the turn (`InputTokens`, `OutputTokens`, `CacheReadTokens`,
`CacheCreationTokens`) stay **main model only**: they are the main model's context accounting, and
summing tokens of different models prices nothing. Never price a turn from
`runResult.ModelCallUsages` alone.

GnollBench is unaffected: every benchmark path sets `EnableSubAgents = false`.

### Streaming `cost` Event

During stream completion (in `ChatService.cs`), Overseer broadcasts a dedicated streaming event alongside the existing `context` event:

```csharp
yield return new ChatEvent
{
    Type = "cost",
    Data = JsonSerializer.Serialize(new
    {
        estimatedCost = disclosed.EstimatedCost,   // the whole turn, sub-agent runs included, as this viewer may see it
        source = disclosed.PricingSource,          // "custom" | "catalog" | "mixed"
        inputTokens,                               // main model only, like the three below
        outputTokens,
        cacheReadTokens,
        cacheCreationTokens,
        isOperatorCost = disclosed.IsOperatorCost,
        operatorCost = disclosed.OperatorCost,     // the operator-funded share; admins only
        subAgentCost = disclosed.SubAgentCost,     // the delegated runs' share
        subAgentRuns = turnCost.SubAgentRuns       // delegated runs that made a model call, priced or not
    })
};
```

The event is emitted whenever the main model has a price card, with `estimatedCost: null` when a
delegated run's model has none and the turn is therefore unpriced. `source` is `"mixed"` when the
main loop and a delegated run were priced from different sources.

The `cost` event is separated from the `context` event because context window measurement requires known model limits and prompt token counts, whereas cost estimation requires resolved pricing.

### Operator vs. User Cost Attribution (`isOperatorCost`)

When an assistant turn executes using an operator-provided system model (`systemModelId.HasValue`):
- `isOperatorCost` is set to `true`.
- The chat UI displays an `(operator)` suffix next to the cost figure (e.g. `4.00¢ (operator)`).
- This explicitly signals that the hosting operator absorbed the API charges, preventing end users from misunderstanding the figure as a personal charge or billing deduction.

When executing via a user's personal API key (`UserAiModel`), `isOperatorCost` is `false`, displaying the personal cost (e.g. `4.00¢`).

#### Funding is per run

The main loop is operator-funded exactly when it ran on a system configuration. A delegated run is
decided per resolution branch (`ResolvedSubAgentModel.IsOperatorFunded`): a system configuration, and
any `AI:*` server key — including a user model whose provider has no user key and fell back to it —
count as operator-funded; a run on the user's own key does not. The turn's **operator share**
(`OperatorEstimatedCost`) is the main loop when it ran on a system configuration plus every
operator-funded delegated run, and `null` when the operator paid nothing.

#### What each viewer is shown

`ChatCostDisclosure.For` is the single rule, used by the `cost` event and by both history loads
(persistent and ephemeral sessions). A row's operator share is `OperatorEstimatedCost` when set;
otherwise — every row saved before the column existed — the whole cost when
`SystemAiConfigurationIdUsed` is set.

| Viewer | `estimatedCost` | `operatorCost` | `subAgentCost` |
|--------|-----------------|----------------|----------------|
| Admin | the whole turn | the operator share | the delegated share |
| Regular user | the user-funded share (whole − operator share); `null` when that is not above 0 | always `null` | the delegated share only when the operator paid nothing |

`isOperatorCost`, for both viewers, is `SystemAiConfigurationIdUsed` set **or** an operator share
above 0. The first term keeps an unpriced turn on a system configuration from raising the PARTIAL
glyph; the second covers a user-model turn whose sub-agent the operator funded. A regular user's
`estimatedCost` is by construction what `ChatSessionCostAccumulator` adds to
`TotalUserEstimatedCost`, so the client's fold of the live cost into its header total agrees with
the total served on reload. Known limit: a user share priced at exactly 0 beside an operator share
is shown as withheld rather than as `0.00¢`.

### Database Persistence Model

In `ChatMessage` (`GnollHackServer.Data`):
```csharp
public decimal? EstimatedCost { get; set; }          // precision: (18, 8); the whole turn, sub-agent runs included
public string? PricingSource { get; set; }          // "custom" | "catalog" | "mixed"
public long? InputTokens { get; set; }              // this and the three below: main model only
public long? OutputTokens { get; set; }
public long? CacheReadTokens { get; set; }
public long? CacheCreationTokens { get; set; }
public decimal? SubAgentEstimatedCost { get; set; }  // precision: (18, 8); the delegated runs' share of EstimatedCost
public decimal? OperatorEstimatedCost { get; set; }  // precision: (18, 8); the operator-funded share of EstimatedCost
public string? TurnNoticeCode { get; set; }          // the notice under this message, § 6
public string? TurnNoticeFactsJson { get; set; }     // the numbers and sanitized codes behind it
```

`SubAgentEstimatedCost` and `OperatorEstimatedCost` are **shares** of `EstimatedCost`, never
additions to it. `SubAgentEstimatedCost` is `null` when the turn delegated nothing that reached a
provider; `OperatorEstimatedCost` is `null` when the operator paid nothing, and on every row saved
before migration `AddChatMessageSubAgentAndOperatorCost` — those rows are read through
`SystemAiConfigurationIdUsed`, and no backfill is attempted. `SystemAiConfigurationIdUsed` names the
configuration that funded the **main** model.

Costs and token counts are **snapshotted at turn completion and never recomputed on read**. If catalog pricing or configuration overrides change in the future, historical message costs remain accurate to the time of generation.

### Session-Level Cost Persistence

`ChatSession.TotalEstimatedCost` (`decimal(18, 8)`, nullable) holds the running total of the
session's assistant turns in USD. `ChatSessionCostAccumulator.Apply` folds each completed turn's
cost into it inside the **same** `SaveChangesAsync` that persists the assistant message, so the
message and the total cannot drift apart. `TotalUserEstimatedCost` receives only the user-funded
share — the turn less its `OperatorEstimatedCost`. An unpriced turn is skipped entirely: the total is
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
matter.

### UI Presentation Surfaces

1. **Per-Reply Cost**:
   The bottom-left corner of each assistant message box, in `.msg-cost-footer`. It is **no
   longer** in the top-right `.ttft-container`, which now carries timing only. The first figure
   is the whole cost the viewer may see, sub-agent runs included; the delegated share follows it,
   inside the reply's cost rather than beside it — `1.25¢ · sub-agents 0.40¢`:
   ```html
   @if (msg.role === 'assistant' && showChatCost && msg.estimatedCost != null) {
     <div class="msg-cost-footer" title="Estimated cost of this response, sub-agent runs included">
       {{ formatCost(msg.estimatedCost) }}@if (operatorCostLabel(msg.estimatedCost, msg.operatorCost); as op) {<span class="msg-cost-operator">{{ op }}</span>}@if (msg.subAgentCost) {<span class="msg-cost-subagents">· sub-agents {{ formatCost(msg.subAgentCost) }}</span>}
     </div>
   }
   ```
   The operator tag is driven by `operatorCost`, not `isOperatorCost`: `(operator)` when the
   operator paid the whole figure shown, `(0.40¢ operator)` when it paid part of it, nothing when
   it paid none. `operatorCost` reaches admins only, so a regular user never sees an operator tag
   beside their own share.
2. **Live Streaming Cost**:
   The same `.msg-cost-footer`, on the streaming message box, driven by `liveCost`,
   `liveOperatorCost` and `liveSubAgentCost` once the `cost` event arrives; all three are copied
   onto the finished message.
3. **Chat Total**:
   `Total 1.85¢` in the chat header, right-aligned beside the title and sharing that space with
   the context window indicator. The value is `ChatSession.TotalEstimatedCost` plus the in-flight
   turn, falling back to the sum of loaded per-message costs for a chat saved before that column
   existed. When some assistant turn ran unpriced, a small amber warning glyph follows the figure;
   its tooltip and accessible name say the total is incomplete because responses on a model with
   no configured pricing are not included. An operator-funded turn whose price is withheld from
   the viewer does not raise the glyph. When no cost is known anywhere the whole indicator is
   hidden — there is no "not available" text, and the context indicator stays right-aligned.

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

---

## 6. Turn Notices

When a limit or a provider failure ends or cuts a chat turn, the user sees a notice under the assistant message: what happened and what to do, with no numbers, provider names, model output or raw errors. `ChatTurnNoticeCatalog` is the only place with this text, and it renders from the code at read time, so a wording fix also corrects old messages.

| Code | Title | Action |
|---|---|---|
| `no_final_answer` | The Overseer stopped before answering | Try again |
| `research_rounds`, `model_calls` | Research limit reached | Continue |
| `tool_budget` | Research limit reached for this chat | New chat |
| `answer_length` | Answer cut short | Continue |
| `time_limit` | Time limit reached | Try again |
| `provider_busy` | The AI service is busy | Try again |
| `provider_overloaded` | The AI service is unavailable | Try again |
| `provider_timeout` | The AI service did not respond | Try again |
| `provider_error` | The AI service returned an error | Try again |
| `connection_lost` | The connection to the AI service was lost | Try again |
| `api_key_rejected` | The API key was rejected | None |
| `api_key_balance` | The API key has no credit | None |

`tool_output` has no notice: it is for administrators, on the Limits sub-tab. The two API key notices word themselves differently for the user's own key and a system model.

**One notice per message.** All hits become event rows (`ai-telemetry.md` § 12c); the message shows the first of `api_key_*`, `time_limit`, `provider_*`, `connection_lost`, `no_final_answer`, `research_rounds`, `model_calls`, `tool_budget`, `answer_length`. Severity is `error` when the turn has no visible answer, otherwise `warning`. A `connection_lost` hit whose recovery call then produced an answer is marked `Recovered`: it still writes its event row, and the user sees no notice for it.

**Persistence.** `ChatMessage.TurnNoticeCode` and `TurnNoticeFactsJson` hold the code and the numbers and sanitized codes behind it. They are plain text in every mode, like token counts, because they contain nothing the user typed or the model wrote, and the portability export carries them. `EphemeralSessionStore` keeps both for an incognito session. A turn with no answer stores empty `Content`; an assistant row with empty `Content` is not replayed to the model.

**Streaming.** `ChatService` emits `turn_notice` last, after the message and the event rows are saved, with the JSON `{ code, severity, title, message, advice, action, reference, adminFacts }` in `Data`, the one field reconnect replay carries. The message projections in `ChatController` carry the same shape as `turnNotice`, so the notice survives a reload.

**What each audience sees** (`ChatTurnNoticeDisclosure`):

- **Everyone:** the catalog text, and the reference `L-<event id>` where a row exists.
- **On their own API key:** for a provider failure, also the HTTP status and the sanitized provider code. The account is theirs, and neither is the provider's message.
- **Administrators:** every stored fact, and, on a live turn only, the bounded provider error body. The body is never stored in `TurnNoticeFactsJson` or the event row, because a provider can echo prompt text; after a reload it is in the server log.

**Provider errors do not reach the conversation.** In a chat turn (`AgentRunRequest.ReportTurnLimits`) a provider `error` event becomes a hit: its text goes into neither `Content`, the client nor later turns, and no event carries `Detail`. The exception is an `error` with an `ErrorCode` such as `model_unavailable`, whose text is already safe and which the composer notice reads. A timeout or a broken stream ends the loop normally, so the partial text, tool calls and costs are saved. A user's Stop adds no notice. Refusals before a turn runs (system-model quotas, the one-answer gate, the request rate limiter) stay as they are and are not limit events.

**The notice card** (`app-turn-notice`) sits outside the message's Markdown, in every `showThoughtsAndTools` mode. Its one action shows only on the conversation's last assistant message and not while a turn runs: *Try again* resends the turn's user message and is left out when that message had attachments, *Continue* sends "Please continue.", *New chat* starts a new chat. Administrator facts are in a `<details>`. A live notice is announced through a status or alert region that is always on the page; the card has no live role, so loading a chat's history reads nothing aloud.
