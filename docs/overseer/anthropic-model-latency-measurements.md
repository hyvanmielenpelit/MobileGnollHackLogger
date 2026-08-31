# Anthropic Model Latency Measurements

Measured response time, time-to-first-token, availability, and error behaviour of the seven
Anthropic Claude models Overseer supports, **with adaptive thinking enabled at effort
`high`** — the request shape Overseer's own configuration produces.

**Measurement date: 2026-08-31, 21:12–21:24 UTC.** 168 calls, 24 per model, one call in
flight at a time. Total cost $0.34.
Endpoint under test: `POST https://api.anthropic.com/v1/messages` with `"stream": true`,
the endpoint Overseer's main chat uses (`AnthropicProvider.GetChatStreamUrl`).

> [!WARNING]
> **The latency numbers have a short shelf life.** They are a 12-minute snapshot of one
> evening on one account. Provider capacity moves, and a model that is fast today can be
> congested next month.
>
> Treat the *structural* findings — how `thinking` and `display` defaults differ per model,
> what the streaming timestamps actually mean, that effort does not dictate thinking depth —
> as durable. Treat the *table of milliseconds* as a snapshot, and re-run
> ([Reproducing](#reproducing)) rather than trusting it after a new model release.

---

## Summary

| Model | Availability | Median TTFT | Median to answer | Median total | Worst call | Stability (max ÷ median) |
|-------|--------------|-------------|------------------|--------------|------------|--------------------------|
| `claude-opus-4-7` | 24/24 | 1 813 ms | 2 316 ms | **2 339 ms** | 3 062 ms | 1.31 |
| `claude-sonnet-5` | 24/24 | 2 272 ms | 2 356 ms | **2 408 ms** | 3 110 ms | **1.29** |
| `claude-sonnet-4-6` | 24/24 | 1 837 ms | 2 664 ms | 2 673 ms | 9 254 ms | 3.46 |
| `claude-opus-4-6` | 24/24 | 2 024 ms | 3 094 ms | 3 108 ms | 6 668 ms | 2.15 |
| `claude-opus-4-8` | 24/24 | 2 818 ms | 3 563 ms | 3 572 ms | 10 262 ms | 2.87 |
| `claude-opus-5` | 24/24 | 2 862 ms | 3 732 ms | 3 738 ms | 4 320 ms | 1.16 |
| `claude-fable-5` | 24/24 | 3 837 ms | 4 546 ms | 4 562 ms | 12 083 ms | 2.65 |

**Three headlines.**

1. **Availability was perfect: 168 of 168 calls returned HTTP 200.** No `429`, no
   `529 overloaded_error`, no timeouts, no truncation, and `retry-after` never appeared. This
   is the opposite of what the Gemini measurement found, where `gemini-3.7-flash` failed
   **24 out of 24** attempts in the same kind of window. On this evidence, Anthropic
   availability is not the risk Google availability is.
2. **Newer is not slower, and it is not faster either — the ordering is not monotonic.**
   `claude-opus-4-7` was the fastest model measured, beating both its successors (4.8 and
   Opus 5) *and* its predecessor (4.6). Any rule of thumb like "the newest model is the
   congested one" — which held for Gemini — does not transfer here.
3. **Every model got the answer right, 168/168.** Latency is the only axis that separates
   them on this task, with one formatting exception noted below.

---

## Methodology

- **One arm, seven models.** Every request byte-identical except `"model"`:

  ```json
  {
    "model": "<id>",
    "stream": true,
    "max_tokens": 2048,
    "thinking": { "type": "adaptive", "display": "summarized" },
    "output_config": { "effort": "high" },
    "messages": [{ "role": "user", "content": "A train departs at 09:47 and travels for 3 hours 26 minutes, then waits 48 minutes, then travels a further 1 hour 39 minutes. At what time does it arrive? Reply with only the arrival time in 24-hour HH:MM format." }]
  }
  ```

- **Round-robin interleaving, 24 rounds**, with the within-round order rotated each round so
  no model permanently occupied the first or last slot. Minute-to-minute capacity therefore
  affects every model equally. A per-model block design would have confounded model with time
  of day.
- **One call at a time**, no concurrency, 1-second pause between calls, 120-second timeout.
- **No retries.** A `429` or `529` would have been recorded as data, not routed around.
- The prompt is a three-step time addition crossing an hour boundary, with a single
  deterministic answer (`15:40`) and a five-character response — enough to make the model
  reason, cheap enough to measure 168 times, and self-checking.
- Harness: a .NET console program using `HttpClient` with
  `HttpCompletionOption.ResponseHeadersRead`, reading the SSE stream line by line. Same HTTP
  stack as Overseer, so the numbers transfer to production rather than describing `curl`'s
  behaviour.

### Why the request sets `thinking` and `display` explicitly

**This is the single most important thing to copy from this methodology.** Omitting the
`thinking` field does not mean the same thing on every model:

| Model | `thinking` omitted → | `display` default |
|-------|----------------------|-------------------|
| `claude-fable-5` | **adaptive** (always on; cannot be disabled) | `omitted` |
| `claude-opus-5` | **adaptive** | `omitted` |
| `claude-sonnet-5` | **adaptive** | `omitted` |
| `claude-opus-4-8` | **no thinking** | `omitted` |
| `claude-opus-4-7` | **no thinking** | `omitted` |
| `claude-opus-4-6` | **no thinking** | `summarized` |
| `claude-sonnet-4-6` | **no thinking** | `summarized` |

A comparison that left `thinking` out would have timed Opus 5 *thinking* against Opus 4.7
*not thinking* and reported the gap as a model-speed difference — with nothing in the results
to reveal it. Setting `{"type": "adaptive"}` explicitly is what makes a cross-model
comparison mean anything. `display` matters for the same reason: under `"omitted"` the
thinking blocks still stream but carry empty text, which moves when the first content-bearing
delta arrives and so distorts TTFT.

### What the three timestamps mean

| Metric | Definition |
|--------|------------|
| `ttft_ms` | First `content_block_delta` of any kind — when Overseer's chat header would stop showing a bare spinner |
| `first_text_ms` | First `text_delta` — when the answer itself starts appearing |
| `total_ms` | Stream closed |

> [!IMPORTANT]
> **The `ttft` → `first_text` gap is *not* thinking time.** It is tempting to read it that
> way, and it is wrong. The model's reasoning happens largely before the first delta is
> emitted, so it is inside `ttft`. The gap measures how long the *thinking summary* takes to
> stream, which depends on how the provider chunks it. `claude-sonnet-5` makes this obvious:
> a diagnostic confirmed it emits a full `thinking` block on every call, yet in 14 of 24 calls
> its entire summary and the first text arrived within 100 ms of each other — it buffers the
> summary into a burst, where the Opus models drip it out over 650–1 050 ms.
>
> If you want to attribute latency to thinking versus queueing, you need a non-thinking
> control arm. This study does not have one (see [Limitations](#limitations)).

---

## Results

### Latency, milliseconds, n=24 per model

**Time to first token** — when the UI stops showing a bare spinner:

| Model | min | median | mean | p90 | max |
|-------|-----|--------|------|-----|-----|
| `claude-sonnet-4-6` | **1 006** | 1 837 | 2 479 | 5 790 | 8 676 |
| `claude-opus-4-7` | 1 474 | **1 813** | 1 865 | 2 287 | 2 309 |
| `claude-sonnet-5` | 1 690 | 2 272 | 2 222 | 2 556 | 2 824 |
| `claude-opus-4-6` | 1 830 | 2 024 | 2 285 | 3 060 | 5 494 |
| `claude-opus-4-8` | 2 296 | 2 818 | 3 324 | 3 770 | 9 649 |
| `claude-opus-5` | 2 504 | 2 862 | 2 928 | 3 210 | 3 390 |
| `claude-fable-5` | 3 486 | 3 837 | 4 185 | 4 356 | 11 394 |

**Total response time** — request sent to stream closed:

| Model | min | median | mean | p90 | max | Calls > 5 s | Calls > 8 s |
|-------|-----|--------|------|-----|-----|-------------|-------------|
| `claude-opus-4-7` | 2 195 | **2 339** | 2 474 | 3 018 | 3 062 | 0 | 0 |
| `claude-sonnet-5` | 2 177 | 2 408 | 2 474 | 2 785 | 3 110 | 0 | 0 |
| `claude-sonnet-4-6` | **1 562** | 2 673 | 3 340 | 7 683 | 9 254 | 3 | 2 |
| `claude-opus-4-6` | 2 903 | 3 108 | 3 350 | 4 141 | 6 668 | 1 | 0 |
| `claude-opus-4-8` | 3 166 | 3 572 | 3 886 | 4 374 | 10 262 | 1 | 1 |
| `claude-opus-5` | 3 321 | 3 738 | 3 775 | 4 166 | 4 320 | 0 | 0 |
| `claude-fable-5` | 4 109 | 4 562 | 4 891 | 5 160 | 12 083 | 4 | 1 |

**Read the medians and the maxima, not the means.** With n=24, a single 12-second outlier
moves a mean by hundreds of milliseconds. `claude-fable-5`'s mean is 4 891 ms against a
4 562 ms median entirely because of one 12-second call.

### Consistency matters as much as speed

`claude-sonnet-5` and `claude-opus-5` never produced a call slower than 5 seconds, and their
worst calls were 1.29× and 1.16× their medians. `claude-sonnet-4-6` had both the **fastest
single call of the entire measurement** (1 562 ms) and a 3.46× spread, with a p90 of 7 683 ms
— it is quick when it is quick and unpredictable otherwise. For anything user-facing, that
spread is worse than a slower, tighter model.

### Capacity was stable across the session

Median total, by third of the run:

| Model | Rounds 1–8 | Rounds 9–16 | Rounds 17–24 |
|-------|-----------|-------------|--------------|
| `claude-fable-5` | 4 439 | 4 585 | 4 542 |
| `claude-opus-5` | 3 745 | 3 710 | 3 651 |
| `claude-opus-4-8` | 3 611 | 3 317 | 3 525 |
| `claude-opus-4-7` | 2 311 | 2 411 | 2 313 |
| `claude-opus-4-6` | 3 004 | 3 122 | 3 045 |
| `claude-sonnet-5` | 2 363 | 2 492 | 2 374 |
| `claude-sonnet-4-6` | 2 187 | 3 149 | 2 673 |

Six of seven models are flat to within a few per cent across the 12 minutes, which is the
evidence that the interleaved design worked and that these medians are not an artefact of
when each model happened to be called. Only `claude-sonnet-4-6` drifts, consistent with its
wide tail.

### `effort: "high"` did not buy much thinking

Output tokens per call, averaged: 42–107 across the seven models. On a problem this size,
**adaptive thinking scaled itself to the difficulty of the task, not to the effort setting**.
Effort is a ceiling and a disposition, not an instruction to spend tokens.

The practical consequence: these numbers characterise Overseer's configured request shape on
an *easy* prompt. They are a floor, not a profile of these models under heavy reasoning load.

### Correctness, and one formatting outlier

All seven models computed `15:40` correctly on all 24 calls — **168/168**.

**`claude-opus-4-6` was the only model that ignored the output format instruction**, returning
`**15:40**` in markdown bold on 20 of 24 calls despite "Reply with only the arrival time".
The other six complied 24/24. This is worth knowing beyond this measurement: a prompt that
relies on exact output formatting needs stricter handling on Opus 4.6 than on its siblings.

### Error behaviour

Nothing to report, which is itself the finding: 168/168 HTTP 200, every call
`stop_reason: "end_turn"`, no `max_tokens` truncation, no refusals, no `429`, no
`529 overloaded_error`, no timeouts. `anthropic-ratelimit-requests-remaining` was present on
responses (values from 999 down to 49 were observed as the run consumed the minute bucket) and
`retry-after` never appeared.

`claude-fable-5` was reachable on this account, so the zero-data-retention restriction that
blocks Fable 5 for some organisations does not apply here.

### Footnote: tokenizers differ

The byte-identical prompt was counted as **96** input tokens by `claude-opus-4-7`, **91** by
Fable 5 / Opus 5 / Opus 4.8 / Sonnet 5, and **74** by Opus 4.6 and Sonnet 4.6. Budget
estimates that assume one token count across the family will be off by up to 30 %.

---

## What to conclude

1. **Anthropic availability was a non-issue.** 168/168. Code defensively against `429` and
   `529` anyway — the sample is one 12-minute window — but do not design around scarcity the
   way the Gemini results require.
2. **Model choice buys about 2× on latency.** 2 339 ms (Opus 4.7) to 4 562 ms (Fable 5)
   median, on identical work.
3. **The version ordering does not predict speed.** Opus 4.7 beat Opus 4.8, Opus 5, and
   Opus 4.6. Measure; do not extrapolate from release order.
4. **Consistency and speed are separate properties.** Sonnet 5 and Opus 5 were tight; Sonnet
   4.6 and Fable 5 had long tails. A median alone will mislead you.
5. **`effort` is not a thinking-token dial.** At `high`, on an easy problem, every model spent
   tens of tokens. Do not assume a high effort setting is expensive per se.
6. **`thinking` and `display` defaults vary by model.** This is the trap most likely to
   silently corrupt someone else's comparison — see the table above.

### What this sample does *not* support

- **Fine ranking between neighbours.** Opus 4.7 at 2 339 ms and Sonnet 5 at 2 408 ms are 3 %
  apart at n=24. Do not call one faster than the other.
- **Any claim about heavy reasoning workloads.** The prompt was easy by design.
- **Attribution of latency to thinking versus queueing.** That needs a non-thinking control
  arm, which this run does not have.
- **Anything about tool use, long context, or multi-turn conversations.** Single-turn, ~90
  input tokens.

---

## Limitations

- **One arm only.** A `thinking: {"type": "disabled"}` control arm was designed and then
  dropped from scope. Without it, `ttft` mixes queueing with reasoning.
- **Effort `high` only.** No sweep across `low` / `medium` / `xhigh` / `max`.
- **n=24, one 12-minute window, one account, one region.**
- **One prompt.** Easy, single-turn, tiny output.

---

## Which model to use in tests

**Prefer `claude-sonnet-5` for live Anthropic tests.** It was the second-fastest model
measured (2 408 ms median), the *most consistent* of all seven (max only 1.29× its median, no
call over 3 110 ms), and the cheapest per call at $2/$10 per MTok. `claude-opus-4-7` was
marginally faster but costs 2.5× as much on output, and the difference is inside the noise at
this sample size.

**Avoid `claude-fable-5` and `claude-sonnet-4-6` in a test suite** — the first for its 4.5 s
median, the second for a p90 of 7.7 s that will make a suite's runtime unpredictable.

> [!NOTE]
> **`Overseer.Tests/LiveApiModelPolicy.cs` is currently Gemini-only.** Its `DefaultModel` is
> `gemini-3.5-flash-lite` and its allow-list messages talk about "Gemini flash models", so any
> Claude id would be rejected. Making that policy provider-aware is prerequisite work before
> these recommendations can be enforced in the test suite.

Per the [`testing_guidelines`](../../.agents/skills/testing_guidelines/SKILL.md) skill, live
tests are excluded by default and require explicit permission to run:

```powershell
dotnet test MobileGnollHackLogger.slnx --filter "Category!=UsesExternalApi"
```

---

## Reproducing

Requires an Anthropic API key in the `Overseer.Tests` User Secrets store under
`AI:AnthropicLatency:APIKey`. See
[`docs/overseer/test-configuration.md`](test-configuration.md).

> [!CAUTION]
> **Never commit an API key.** These keys belong in User Secrets only — never in
> `appsettings.json`, never in a test fixture, never in a document in this repository.

Verify the model ids first — they are exact strings and carry **no date suffix**, so a
`claude-sonnet-5-20260630`-style id will 404:

```powershell
$k = ((dotnet user-secrets list --project Overseer.Tests | Select-String "AI:AnthropicLatency:APIKey") -split '=', 2)[1].Trim()
((curl.exe -sS "https://api.anthropic.com/v1/models?limit=100" -H "x-api-key: $k" -H "anthropic-version: 2023-06-01") | ConvertFrom-Json).data | Select-Object id, display_name, created_at
```

The harness itself is a scratch .NET console program, deliberately not committed to this
repository. To rebuild it, the design is fully specified above and in the implementation plan;
the essentials are: `HttpClient` with `ResponseHeadersRead`, SSE parsed line by line,
timestamps taken at response-headers-received, first `content_block_delta`, first
`text_delta`, and stream close; round-robin over the model list with per-round rotation; one
call in flight; costs accumulated from each response's `usage` against the published rates,
with a hard spend guard.

**Cost of a full re-run: about $0.34** for 168 calls — $0.075 of that on Fable 5 alone, which
at $10/$50 per MTok is the most expensive model in the catalog.

---

## Related

- [`gemini-service-tier-measurements.md`](gemini-service-tier-measurements.md) — the
  equivalent measurement for Google Gemini, including `service_tier` behaviour. Read both
  before choosing a provider or a test model.
- [`test-configuration.md`](test-configuration.md) — User Secrets schema and troubleshooting.
- [`supported_ai_models`](../../.agents/skills/supported_ai_models/SKILL.md) — the supported
  model policy.
