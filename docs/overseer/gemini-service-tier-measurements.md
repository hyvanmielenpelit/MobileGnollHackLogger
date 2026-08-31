# Gemini Service Tier Measurements

Measured behaviour of the Google Gemini API's `service_tier` parameter across the Gemini
models Overseer supports: whether the requested tier is honoured, whether it is reported
back, and what availability and latency to expect.

**Measurement date: 2026-08-31, 10:29–11:20 UTC.**
Account: a paid **Tier 2 (Pay-As-You-Go)** Google AI Studio project.
Endpoint under test: `POST /v1beta/models/{model}:streamGenerateContent?alt=sse`, the
endpoint Overseer's main chat uses (`GoogleProvider.GetChatStreamUrl`), plus
`:generateContent` where noted.

> [!WARNING]
> **These numbers have a short shelf life.** Google's capacity is allocated dynamically,
> and the newest Gemini model is always the most heavily used one — so the model that is
> saturated today is usually the one released most recently. When Google ships a new
> Gemini generation, expect the availability picture below to **shift down the list**: the
> new model becomes the congested one, and today's congested model becomes reliable.
>
> Treat the *structural* findings (which tier gets honoured, where the served tier is
> reported) as durable, and the *availability and latency* findings as a snapshot. Re-run
> the measurements (see [Reproducing](#reproducing) below) rather than trusting this table
> after a new model release.

---

## Summary

| Model | Tier honoured & reported? | Availability (streaming) | Latency |
|-------|---------------------------|--------------------------|---------|
| `gemini-3.5-flash-lite` | ✅ Yes, exactly as requested | **16/16 succeeded** | Fast and tight: median ~0.7 s, max 0.9 s |
| `gemini-3.6-flash` | ✅ Yes, exactly as requested | **24/24 succeeded** | Median ~2.4–2.8 s, but a heavy tail up to 59 s |
| `gemini-3.7-flash` | ✅ Yes (visible on error responses) | **0/24 succeeded** — fully saturated | n/a — every call returned 503 or hung |

**The headline: `service_tier` works, but it does not buy availability.** Requesting
`priority` was honoured on every single call, and it did **not** prevent 503s, reduce
median latency, or protect against saturation.

---

## Methodology

- Two request bodies, byte-identical except for `"service_tier"` (`priority` vs
  `standard`), both with `maxOutputTokens: 256` and the prompt *"Reply with the single
  word OK."*
- The two arms were **interleaved** within each round (priority, then standard, then next
  round), so Google's minute-to-minute capacity fluctuation affects both arms equally. A
  sequential block design would have confounded tier with time.
- Same API key, same endpoint, same host, one call at a time (no concurrency).
- `curl.exe` with a hard `--max-time`, so a hung request is recorded rather than blocking
  the run.
- Latency is wall-clock for the **complete** response, not time-to-first-token.
- Sample sizes are small (8–12 rounds per arm). They are sufficient to establish
  *presence or absence* of a signal, and **not** sufficient for a statistical claim about
  latency differences. Where the data does not support a conclusion, this document says
  so.

---

## Results

### `gemini-3.5-flash-lite` — streaming, 8 rounds per arm

| Requested | HTTP 200 | min | median | mean | max | Reported `serviceTier` |
|-----------|----------|-----|--------|------|-----|------------------------|
| `priority` | 8/8 | 593 ms | 688 ms | 748 ms | 931 ms | `priority` |
| `standard` | 8/8 | 604 ms | 723 ms | 743 ms | 912 ms | `standard` |

Completely reliable and fast. The tier is echoed back exactly as requested on all 16
calls. Means are within 1 % of each other — **no latency difference**, as expected for a
model with plenty of headroom.

### `gemini-3.6-flash` — streaming, 12 rounds per arm

| Requested | HTTP 200 | min | median | mean | max | Reported `serviceTier` |
|-----------|----------|-----|--------|------|-----|------------------------|
| `priority` | 12/12 | 1 577 ms | 2 770 ms | 9 338 ms | 30 368 ms | `priority` |
| `standard` | 12/12 | 1 448 ms | 2 356 ms | 14 668 ms | 58 725 ms | `standard` |

An earlier 5-round pilot on the same model gave the same picture (5/5 on both arms, tiers
echoed correctly).

Fully available, but with a **heavy latency tail on both tiers**. Calls slower than 20 s:
3 of 12 on `priority`, 4 of 12 on `standard`.

Read this carefully:

- The **medians are indistinguishable**, and `standard`'s median is marginally *faster*.
- The **means diverge** only because the tail outliers differ (`standard`'s worst call was
  59 s versus `priority`'s 30 s).
- At n=12 with 3–4 outliers per arm, **this is not evidence of a priority benefit.** It is
  equally consistent with random variation. Do not cite the mean difference as a
  performance argument.

### `gemini-3.7-flash` — saturated on both tiers and both endpoints

Streaming, 12 rounds per arm:

| Requested | HTTP 200 | Observed outcomes |
|-----------|----------|-------------------|
| `priority` | **0/12** | HTTP 503 `UNAVAILABLE`, or a 45–60 s hang with no response headers |
| `standard` | **0/12** | HTTP 503 `UNAVAILABLE`, or a 45–60 s hang with no response headers |

Non-streaming (`:generateContent`), 4 rounds per arm: **0/4 on both arms**, same outcomes.
A single isolated success was observed on `:generateContent` at 10:31 UTC, early in the
session; eight subsequent attempts across the following 50 minutes all failed. **That one
success was luck, not an endpoint advantage** — there is no evidence that
`:generateContent` is more available than `:streamGenerateContent` for this model.

Three things worth knowing:

1. **Google returned `x-gemini-service-tier: priority` on 503 responses.** The request was
   accepted onto the priority tier and then failed anyway.
2. **This contradicts Google's documented behaviour.** The Priority inference guide states
   that traffic exceeding dynamic priority limits is "automatically and gracefully
   downgraded to Standard processing instead of failing". For `gemini-3.7-flash` during
   this window, it failed instead.
3. **Roughly half the failures were 45–60 s hangs**, with Google holding the connection
   open and sending no response headers at all. This is the source of the long
   pre-503 waits (42–85 s) seen in Overseer debug logs — Overseer is not stalling, it is
   waiting on Google.

---

## How the served tier is reported

Google reports the tier that actually served a request in two different places, and
**which ones are available depends on the endpoint**:

| Endpoint | `x-gemini-service-tier` response header | `usageMetadata.serviceTier` in body |
|----------|------------------------------------------|--------------------------------------|
| `:generateContent` (non-streaming) | ✅ Present — **including on 503 error responses** | ✅ Present on 200 |
| `:streamGenerateContent?alt=sse` | ❌ **Always absent** | ✅ Present on 200, repeated in **every** SSE chunk |

Consequences for anyone writing code against this API:

- **Do not rely on the response header.** It is missing entirely on the streaming
  endpoint, which is the one Overseer's chat uses. This is a known Google defect, reported
  publicly on their developer forum in April 2026 and unacknowledged as of this
  measurement.
- **Read `usageMetadata.serviceTier` from the response body.** It works on both endpoints
  and is the only signal available while streaming.
- **Deduplicate.** Google repeats `serviceTier` in every streaming chunk, so a naive
  implementation that logs on each occurrence will flood the log.
- The value is **plain lowercase** (`priority`, `standard`, `flex`) — not the proto-style
  `SERVICE_TIER_PRIORITY` that the public API reference's truncated enum table leaves
  ambiguous. Normalising defensively is still cheap insurance.

---

## What to conclude

1. **`service_tier` is honoured, not ignored.** 40 of 40 successful calls echoed back
   exactly the tier requested. The parameter is a real routing decision.
2. **`priority` does not buy availability.** It neither prevented 503s nor improved the
   success rate on a saturated model. When a model has no capacity, priority has no
   headroom to allocate from — and the `standard` control arm failing identically means
   the 503s say nothing about whether the tier flag works.
3. **`priority` showed no measurable latency benefit** in this sample, on any model.
4. **Billing tier is not the lever.** These results come from a paid Tier 2 project.
   Moving a Tier 1 key to Tier 2 should not be expected to fix 503 storms.
5. **Model choice is the lever that mattered.** The spread was total: 24/24 success on
   `gemini-3.6-flash` versus 0/24 on `gemini-3.7-flash` in the same session, on the same
   key. If a workload is being crushed by 503s, the productive question is which model to
   route to — subject to the volatility warning at the top of this document.

---

## Reproducing

Requires a Google API key in the `Overseer.Tests` User Secrets store under
`AI:ServiceTier:APIKey`, plus `AI:ServiceTier:Provider` and `AI:ServiceTier:Model`. See
the [`configuration_management`](../../.agents/skills/configuration_management/SKILL.md)
skill for how User Secrets are managed in this repository.

> [!CAUTION]
> **Never commit an API key.** These keys belong in User Secrets only — never in
> `appsettings.json`, never in a test fixture, never in a document in this repository.

The automated equivalent lives in the `Overseer.Tests` project's live contract tests,
categorised `UsesExternalApi`. Per the
[`testing_guidelines`](../../.agents/skills/testing_guidelines/SKILL.md) skill, these are
**excluded by default** and require explicit permission to run, because they cost money:

```powershell
dotnet test MobileGnollHackLogger.slnx --filter "Category!=UsesExternalApi"
```

```powershell
dotnet test Overseer.Tests\Overseer.Tests.csproj --filter "Category=UsesExternalApi"
```

Any live test touching these models **must** tolerate HTTP 429 and 503 by logging a
warning and passing, as `testing_guidelines` §2 requires. A test that asserts the absence
of 503s on the newest Gemini model will be permanently red through no fault of this
codebase — as the `gemini-3.7-flash` results above demonstrate.
