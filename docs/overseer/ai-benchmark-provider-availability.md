# GnollBench Provider Availability — Provider Errors, the Error Rate and Re-runs Needed

GnollBench, the Overseer AI benchmark, measures **how available a candidate model's provider was while
we benchmarked it**: how many of our API attempts failed because of the provider, as a count and as a
rate, and how many re-runs a model needed before its benchmarking was complete. Every figure is a
statement about **provider errors observed in GnollBench runs**, made by hand on the development
computer at the hours those runs ran. It never states why a provider failed, and it is never a
statement about hours, traffic or users no run covered.

An error that is our own fault never counts against the provider: exhausted credits or billing, a
rejected key, our own request governor, our tier's rate limits, our own time budgets, our
cancellations and our own network.

This document is the **definitive method**. The other documents link here and do not restate it:
[`ai-benchmark.md`](ai-benchmark.md) (the harness, § 5 *Data Model & Relationships* and *Harness Version
56 Updates*), [`ai-benchmark-report-pack.md`](ai-benchmark-report-pack.md) (the documents),
[`ai-benchmark-chat-consistency.md`](ai-benchmark-chat-consistency.md) (endpoint P6 and Protocol V2),
[`ai-benchmark-model-batches.md`](ai-benchmark-model-batches.md) and
[`ai-benchmark-multi-suite.md`](ai-benchmark-multi-suite.md) (member figures and lineage).

Implementation:

| Concern | File |
|---|---|
| The ledger row and its enums (`ProviderAttemptOutcome`, `BenchmarkFailureOrigin`, `BenchmarkReplacementCause`, `BenchmarkEndpointKind`, `ProviderAttemptOutcomes`) | `GnollHackServer.Data/BenchmarkProviderAttempt.cs`, migration `ProviderAvailabilityLedger` |
| The run and answer columns | `GnollHackServer.Data/BenchmarkRun.cs`, `GnollHackServer.Data/BenchmarkRunAnswer.cs` |
| One record per attempt, in memory | `Overseer/Services/Agents/ProviderAttemptRecord.cs`, `ModelCallRecord.Attempts`, `AgentLoopRunner.ExecuteApiWithRetriesAsync` |
| The outcome classes (§ 3) | `Overseer/Services/Agents/ProviderAttemptClassifier.cs` |
| The message fallback (§ 3.6) | `Overseer/Services/Benchmarking/BenchmarkProviderErrorClassifier.cs` |
| Answer attribution (§ 4), the ledger writes, the repair counters and the rollups | `Overseer/Services/Benchmarking/BenchmarkService.cs` (`AttributeCandidateOutcome`), `Overseer/Services/Telemetry/ModelCallTelemetryWriter.cs`, `Overseer/Services/Benchmarking/BenchmarkRunFinalizer.cs` |
| The cause of a replacement (§ 7.3) | `Overseer/Services/Benchmarking/BenchmarkRunLauncher.cs` (`BenchmarkReplacementCauses`) |
| Lineage at every relaunch (§ 7.2) | `BenchmarkBatteryOrchestrator.cs`, `BenchmarkModelBatchOrchestrator.cs`, `BenchmarkSeriesOrchestrator.cs` |
| The summary, the interval, the bins and the formatting (§§ 5, 6) | `Overseer/Services/Benchmarking/BenchmarkProviderAvailability.cs`, `BenchmarkProportionInterval.Wilson95(double, double)` |
| Loading a run, a set of runs, a battery run or a comparison entry with its lineage (§ 8) | `Overseer/Services/Benchmarking/BenchmarkProviderAvailabilityService.cs` |
| The client's formatting, class labels and components | `Overseer/ClientApp/src/app/admin/benchmark/shared/provider-availability/` |

---

## 1. What Is Measured, and Why

Two things, both candidate-only:

1. **Provider errors**: the attempts that failed because of the provider — mainly overload and
   unavailability (HTTP 503 and 529), server errors, provider timeouts and broken streams — as a count
   *k* over the valid attempts *n*, and as the rate *k* / *n*.
2. **Re-runs needed**: the repair passes on a run and the runs that replaced it, and the part of them
   caused by provider errors.

Before harness 56 neither could be read from a run. Retries happened inside one model call and
survived only as counters on the call (`AttemptCount`, `Http429Count`, `Http5xxCount`); the answer
status `ProviderError` also absorbed failures that were ours, above all our own per-question time
budget; most HTTP 429 responses, which the providers document as our own limit, were counted as the
provider's; and a re-run deleted the replaced attempt's telemetry, so the evidence of the failure went
with it.

## 2. The Unit of Observation: One Provider Attempt

An **attempt** is one HTTP request to the provider, or one refusal by Overseer's request governor
before a request was sent. It is the right unit because it is what a provider's availability is
(request-based availability, *good events ÷ valid events*), because it counts retried errors that still
ended in a good answer, and because it can be placed in a time-of-day stratum by its own start time,
which chat consistency needs. Per-call, per-answer and per-run figures are derived from it.

**The attempt ledger.** `AgentLoopRunner` keeps one `ProviderAttemptRecord` per loop iteration of every
model call — the governor refusal, the request exception, every HTTP status branch, the stream-error
retry, a mid-stream failure and the success — on `ModelCallRecord.Attempts`. GnollBench persists them as
**`BenchmarkProviderAttempt`** rows, one per attempt, written in the same `SaveChanges` as the call's
telemetry:

| Group | Columns |
|---|---|
| Whose attempt | `BenchmarkRunId` (cascades with the run), `BenchmarkRunAnswerId` (a plain, indexed id with no foreign key; a synthesis call has none), `QuestionId`, `Source` (`BenchmarkCandidate` or `BenchmarkGrader`), `GraderRole` |
| What was requested | `Provider`, `RequestedModelId`, `SystemAiApiConfigurationId` (plain, no navigation), `EndpointKind` (`Official` or `Custom`) |
| Where in the run | `Pass` (0 for the initial execution, *n* for the *n*-th repair pass), `CallIndex`, `AttemptIndex` (0 for the call's first attempt) |
| When | `StartedAtUtc` (the attempt's send), `DurationMs` (the attempt's span) |
| What happened | `Outcome` (§ 3), `HttpStatus`, `ProviderErrorCode`, `Retried` (the loop went on to another attempt), `RetryAfterMs` |

`ProviderErrorCode` is the provider's own code, sanitized to `[A-Za-z0-9_.-]` and at most 64 characters:
Anthropic's `error.type`, OpenAI's `error.code` (else `error.type`), Google's `error.status`. **Nothing
else is kept**: no message text, no headers and no secrets. The error body is parsed for its
`type`/`code`/`status` and its Google `details[].@type`, at most 2 KB of it, and discarded.

**The ledger is append-only.** A re-run of failed questions, a single-answer re-run, a re-assessment
and a re-synthesis delete and rewrite the affected `ModelCallTelemetry` rows as they always have; they
never delete a ledger row. Ledger rows are deleted only with their run. Rows written during a repair pass
carry `Pass = RepairPassCount` at that time; grader rows carry the run's `RepairPassCount` when they are
written, and grader repairs (*Retry failed assessments*, *Rerun synthesis*, *Retry claim verification*,
a re-assessment) do not start a pass.

**Candidate and grader.** The ledger covers both, but **every reported figure is candidate-only**.
Grader errors hurt the run and say nothing about the model under test, so grader availability appears
only as one information line in the Integrity tab.

**Recorded runs.** A run is *recorded* when it was started under harness 56 or later: it carries
`BenchmarkRun.ProviderLedgerVersion = 1` from its start, and only a recorded run writes ledger rows,
rollups and answer attribution. A run started before harness 56 is a **legacy run** and **never gets a
ledger, even when it is repaired** after the deployment: a partial ledger covering only the repaired
answers would read as the whole run. `CallTelemetryVersion` was deliberately not bumped for this, because
a new telemetry version would split the chat consistency speed axis across the deployment; recorded
means `ProviderLedgerVersion = 1`, nothing else.

**The run's rollups.** `CandidateValidAttemptCount`, `CandidateProviderErrorCount`,
`CandidateUnattributedErrorCount` and `CandidateOurSideFailureCount` (all null on a legacy run) are
incremented atomically in SQL as each answer is saved, so a running run's cards show provider errors
live, and they are recomputed from the ledger when the run is finalized and after every repair. Card
lists read the rollups only; every interval, breakdown and strip is computed from the ledger.

**The production chat** runs the same `AgentLoopRunner`, so it builds the same records in memory, but
only GnollBench persists them. A record holds only the class, the sanitized code, the status, the
timings and the retry flag.

## 3. Outcome Classes: Whose Fault Is It

Every attempt gets exactly one `ProviderAttemptOutcome`, decided by `ProviderAttemptClassifier` on
structured evidence: the HTTP status; the provider's error `type`, `code` and `status`, parsed from the
JSON error body or the stream `error` event; Google's `error.details[].@type`; the rate-limit headers of
the attempt's **own** response (`RateLimitJson` keeps only the last attempt's); the endpoint kind; the
exception type; and the stage the attempt reached (before the send, waiting for headers, streaming).

| Value | Class | Side | Valid? | Examples |
|------:|-------|------|--------|----------|
| 0 | `Ok` | — | yes | a 2xx response whose stream ended with its completion event and carried no error event |
| 10 | `ProviderOverloaded` | **provider** | yes | HTTP 529; Anthropic `overloaded_error` (HTTP or stream); HTTP 503 other than OpenAI's "Slow Down"; Gemini 503 `UNAVAILABLE`; a 5xx whose body says *currently overloaded* |
| 11 | `ProviderServerError` | **provider** | yes | HTTP 500, 502, 504 and other 5xx; stream `api_error` / `server_error` / `internal` / `deadline_exceeded`; a bracketed 5xx edge code such as `[520]` |
| 12 | `ProviderTimeout` | **provider** | yes | the `AiProvider` HttpClient timed out before any response headers (§ 3.4); a provider-sent HTTP 408 |
| 13 | `ProviderStreamFailure` | **provider** | yes | the stream broke after it started: a stream error event after chunks with no recognized code, a mid-stream `OperationCanceledException` we did not cause, the HttpClient timeout after the headers, or a 2xx stream that ended without its completion event |
| 20 | `RateLimitUnattributed` | **unattributed** | yes | a 429 that no rule of § 3.1 attributes to us: a Gemini 429 without `QuotaFailure` details, an OpenAI 429 with neither the code nor a spent header, any 429 on a custom endpoint |
| 21 | `TransportUnattributed` | **unattributed** | yes | connection reset, aborted or refused; a TLS failure; an `IOException` mid-request; a transport cancel before any response; a stream error event before any chunk with no recognized code; an unexpected 1xx or 3xx |
| 30 | `OurRateLimit` | ours | **no** | § 3.1 |
| 31 | `OurBilling` | ours | **no** | out of balance: Anthropic `billing_error` or a credit-balance 400, OpenAI `insufficient_quota` (any status, 429 included) or `billing_hard_limit_reached`, Google `BILLING_DISABLED`, HTTP 402 |
| 32 | `OurKey` | ours | **no** | a rejected key: HTTP 401 or 403, `authentication_error`, `invalid_api_key`, `API_KEY_INVALID` |
| 33 | `OurRequest` | ours | **no** | HTTP 400, 404, 409, 413, 422 and every other 4xx not named above; `invalid_request_error`, a context-length error, a model that does not exist (`ModelNotFoundClassifier`); an exception thrown while we built or sent the request |
| 34 | `OurThrottle` | ours | **no** | the governor refused or timed out a permit (a slot timeout, an active cooldown); no request was sent |
| 35 | `OurCanceled` | ours | **no** | an operator cancel, or our per-question time budget cut an in-flight attempt; HTTP 499 or Google `CANCELLED` |
| 36 | `OurNetwork` | ours | **no** | a DNS failure on our machine (`HostNotFound`, `NoData`, `TryAgain`, a name-resolution error) or `NetworkUnreachable` |

**Valid attempts** are classes 0 to 21. The our-side classes 30 to 36 are left out of the denominator,
following the *valid events* convention of request-based availability, and are reported separately, as
proof that they were excluded. The *unattributed* classes stay in the denominator and out of the
headline numerator: they form the upper bound (§ 5).

**The order of the rules.** The classifier tries, in this order: our governor; our cancels; the key and
billing verdicts of `ApiKeyFailureClassifier` (the same rules as the alert emails, see
[`api-key-alerts.md`](api-key-alerts.md) § *What triggers an alert*, whose table is the reference for
*out of balance* and *rejected key*); a model that does not exist; the HTTP status; a stream `error`
event; an exception; and last a 2xx stream, which is `Ok` only when its completion event was seen. An
error shape no rule recognizes lands in an *unattributed* class, never in a provider one, so a provider
that changes its error bodies can only move attempts into the upper bound, where the breakdown shows
them.

### 3.1 The 429 rule

Per the providers' own documentation a 429 is **usually our limit**, not the provider's capacity.
Anthropic defines its 429 `rate_limit_error` as the account's limit, its acceleration limits included,
and signals capacity with 529 `overloaded_error`; OpenAI's 429 is a rate limit or `insufficient_quota`,
and its 503 *Slow Down* is caused by our own sudden spike; a Gemini 429 `RESOURCE_EXHAUSTED` is a project
quota when its body carries `QuotaFailure` details. A 429 is classified **`OurRateLimit`** when any of
these holds, and **`RateLimitUnattributed`** otherwise:

- **Anthropic**, official endpoint: any 429.
- **OpenAI**, official endpoint: a 429 whose body `code` or `type` is `rate_limit_exceeded`, or whose
  own `x-ratelimit-remaining-requests` or `x-ratelimit-remaining-tokens` header is `0`.
- **OpenAI**: a 503 whose body says *Slow Down* or *sudden increase in your request rate*.
- **Google**, official endpoint: a 429 whose `error.details` holds a
  `type.googleapis.com/google.rpc.QuotaFailure`. Gemini sends no `x-ratelimit-*` headers, so the header
  rule never applies to it.
- **Any provider**, official endpoint: a 429 whose captured rate-limit headers show a remaining count of
  `0` (`x-ratelimit-remaining-*`, `anthropic-ratelimit-*-remaining`).
- A billing or quota body is `OurBilling`, which is checked first.

**There is no "provider rate limit" class.** A 429 that a provider documents as shared-capacity
exhaustion — Vertex AI's dynamic shared quota documents one — cannot be told from a quota 429 on the
wire, and this repository has seen long runs of Gemini 429s on healthy keys
([`api-key-alerts.md`](api-key-alerts.md) § *Known gap: Google*). An unexplained 429 is therefore honestly
*unattributed*: never silently counted against the provider, and never silently dropped.

### 3.2 Custom endpoints

A custom endpoint (`AiEndpointDescriptor` other than `Official`: a Vertex or gateway bearer token, an
Azure key, a local server) is keyed on its provider name, but its 429 semantics differ: Vertex has shared
capacity, Azure a per-deployment quota. **Every 429 on a custom endpoint is `RateLimitUnattributed`**
unless a billing or key rule claims it. The overload, server-error, timeout and stream classes apply
unchanged, because the endpoint is the provider being benchmarked. The ledger row's `EndpointKind`
records which rule applied. Per-endpoint 429 rules are deferred until a custom endpoint is benchmarked
regularly.

### 3.3 Two accepted attribution doubts

Both are conservative choices, stated here so that no reader mistakes them for certainties:

1. **Gemini's 504 `DEADLINE_EXCEEDED` and some 500 `INTERNAL`** are documented as possibly caused by an
   oversized prompt. They are still counted as the provider's (`ProviderServerError`).
2. **A provider overload that sets a governor cooldown.** A provider 503 or 529 sets a cooldown in
   `AiRequestGovernor`, and a later permit timeout during that cooldown is `OurThrottle`. The provider's
   overload is counted once, on the attempt that saw it, and the answer the cooldown eventually fails
   has origin *Ours*.

### 3.4 Timeouts

- **The HttpClient timeout** is 600 s (`AiRateLimitSettings:RequestTimeoutSeconds`, on the `AiProvider`
  client). A provider that sends no response headers in that time is unavailable: `ProviderTimeout`.
  The same timeout after the headers is a broken stream, `ProviderStreamFailure`.
- **Our per-question time budget** (`Benchmark:QuestionTimeoutSeconds`: Simple 420 s, Intermediate
  600 s, Advanced 720 s) spans the whole agent loop. When it cuts an attempt, the attempt is
  `OurCanceled` and the answer is **Failed, our side: question time budget**, never a provider error.
- **What that means in practice.** The budget starts with the question and the HttpClient timeout with
  the attempt, so on a Simple question the budget always fires first, and on an Intermediate question it
  does in practice too. **`ProviderTimeout` is reachable only on Advanced questions, under the 420 / 600
  / 720 s question budgets**, and only for a stall that begins within the question's first 120 s.
  **Provider stalls everywhere else count as ours.** This is the conservative side of the line; the
  our-side counts show how many attempts the budget cut. Gemini has been measured holding a connection
  open for 45–60 s with no response headers before answering 503
  ([`gemini-service-tier-measurements.md`](gemini-service-tier-measurements.md) § *`gemini-3.7-flash` —
  saturated on both tiers and both endpoints*); such a hang ends in a 503 and is counted as
  `ProviderOverloaded`, not as a timeout.
- **Telling the two cancels apart.** `AgentRunRequest.OperatorCancellation` carries the operator's
  cancel token beside the linked token the loop runs on. When the linked token fires and the operator's
  has not, the attempt was cut by the caller's budget (`QuestionBudgetCanceled`); when the operator's
  has, by the operator. Both are `OurCanceled`; the distinction feeds the answer's `FailureKind`.
- **A budget cut without an exception.** When the question budget ends the agent loop without an
  exception — during the send, during a backoff, or between attempts — the answer is recorded as the
  same question-budget failure (**Failed**, our side, `FailureKind` `question_budget`), never as an
  empty answer.

### 3.5 Overseer's own refusals

The request governor's refusals — the permit timeout (*"Request throttled: …"*) and the cooldown
(*"Rate limit cooldown active for …"*) — are **never provider errors**: no request was sent, and they
are `OurThrottle`. The difficulty-assessment job still treats them as **transient**
(`BenchmarkDifficultyFailurePolicy`): it fails that batch and continues with the next, as it does for a
provider error, because the next batch may get a permit; only a non-transient error aborts the job.

### 3.6 The message fallback

`BenchmarkProviderErrorClassifier` remains the fallback for paths that carry only text, and for an
answer whose failure left no attempt record. Three rules make it safe:

- **Our own messages first.** A text that opens with `Request throttled:`, `Rate limit cooldown active`
  or `Per-question timeout exceeded (` is never a provider error, whatever status-like digits it
  carries; the governor's messages embed the credential key `{provider}:system:{systemModelId}`, which
  can contain them.
- **Anchored statuses only.** A status is read only from `API Error: NNN`, `HTTP NNN`,
  `status code NNN`, `Status: NNN`, `[NNN]`, or a **leading** `NNN ` — the form the runner's own terminal
  texts take (*"429 Rate Limited. Max retries …"*, *"503 Unavailable. Max retries exceeded."*). A bare
  number elsewhere, such as Anthropic's *"prompt is too long: 205000 tokens > 200000"*, is not a status.
- **A question-budget cancel is not a 408.** The typed overload `Classify(Exception, string?, bool
  callerCanceled, bool questionBudgetCanceled)` returns *not a provider error* for an
  `OperationCanceledException` raised by the caller's cancel or by the question budget.

When the fallback is the only evidence, its provider verdict is mapped onto a class by the status it
found: 503 and 529 `ProviderOverloaded`, 408 `ProviderTimeout`, any other 5xx `ProviderServerError`,
429 `RateLimitUnattributed`, anything else `TransportUnattributed`.

## 4. Answer-Level Attribution

A recorded run's answers carry:

- **`FailureOrigin`**: `None`, `Provider`, `Unattributed` or `Ours`;
- **`FailureKind`**: the name of the outcome class of the attempt that ended the answer, or
  `question_budget`;
- **`ReExecutionCount`**: how many times the answer was re-executed.

The status follows the origin:

| The answer ended on | Status | Origin |
|---|---|---|
| no failure | `Ok` or `EmptyAnswer` | `None` |
| a provider class | `ProviderError` | `Provider` |
| an unattributed class | `ProviderError` | `Unattributed` |
| an our-side class | **`Failed`** | `Ours` |
| our question budget | **`Failed`** | `Ours`, kind `question_budget` |
| a failure with no attempt record and no provider verdict from the fallback | `Failed` | `Ours`, no kind |
| an operator cancel with no earlier error | `Canceled` | `Ours`, kind `OurCanceled` |

An operator cancel that follows an error keeps that error's attribution, so the attempt the cancel cut
does not stand in for it. `Failed` was already terminal and re-executable (`HasTerminalFailure`,
`NeedsReExecution`), so no run, re-run or index-withholding behavior changes.

**Where `Failed` and `ProviderError` are treated alike.** The assessor prompt already listed both as
excluded from scoring. The **synthesis prompt** now prints its *excluded from scoring* line for
`ProviderError` **or** `Failed` as well. The only answers this changes are `Failed` answers, which never
carry text to grade, so harness 56 is not a grading change (it is classified `ProviderErrorAccounting`
alone, see [`ai-benchmark-chat-consistency.md`](ai-benchmark-chat-consistency.md) § 14). The assessor
prompt's wording is unchanged, because its words reach the grader. The run report prints the per-answer
error line for both statuses, labeled by origin: *Provider error*, *Unattributed error* or *Failed: our
side*. The Questions tab reads *Provider error: overloaded (HTTP 529)*, *Unattributed error: rate
limited (HTTP 429)* or *Failed: our side (question time budget)*.

**Legacy answers** keep their stored values; their `FailureOrigin` is null, shown as *not recorded*, and
they keep the old wording.

## 5. The Metrics

All figures are candidate-only.

| Name | Definition | Shown as |
|---|---|---|
| **Provider error rate** (the headline) | provider-class attempts ÷ valid attempts | `0.81%`, always beside its count |
| **Provider errors** | the provider-class attempt count *k*, with *n* valid attempts | `12 of 1,480 attempts` |
| **Upper bound including unattributed** | (provider + unattributed) ÷ valid | `≤ 1.0% incl. 3 unattributed` |
| **By class** | counts of overloaded (503/529), server error (5xx), timeout, stream failure, 429 ours, 429 unattributed and transport unattributed | breakdown table |
| **Our-side failures (excluded)** | counts of classes 30 to 36 | breakdown table and the Integrity tab only |
| **Questions lost to the provider** | answers whose terminal status, after every pass, is `ProviderError` with origin `Provider` | count |
| **First-attempt success** | candidate calls whose first attempt was `Ok` ÷ candidate calls with a valid attempt | the Integrity tab only |
| **Repair passes** | *Re-run failed questions* passes plus single-answer re-runs (`RepairPassCount`); provider-caused when a re-executed answer's prior origin was `Provider` (`ProviderRepairPassCount`) | `2 (1 for provider errors)` |
| **Replacement runs** | runs in the lineage chain that this run replaced (§ 7); provider-caused by `ReplacementCause` | count |
| **Re-runs needed** | repair passes plus replacement runs, each with its own passes, and the provider-caused part | `3 re-runs (2 for provider errors)` |

The summary of one run or a pool of runs is `BenchmarkProviderAvailability.Summarize`, a pure function
over the ledger rows and the runs' lineage records (`ProviderAvailabilitySummary`).

## 6. Statistics

### 6.1 The interval: cluster-adjusted Wilson, 95 %

The Wilson score interval is the recommended interval for proportions near 0 and for small *n*
(Brown, Cai and DasGupta, 2001), and it is already the repository's proportion interval. Attempts are
not independent — provider errors come in bursts — so the effective sample size is reduced by a design
effect, the approach of Korn and Graubard (1998) for survey proportions with few events
(`BenchmarkProviderAvailability.ClusterAdjustedWilson95`):

1. Each cluster *c* carries (*k*<sub>c</sub>, *n*<sub>c</sub>): provider-class and valid attempts.
   p̂ = Σ*k* ÷ Σ*n*, *N* = Σ*n*, and *C* is the number of clusters with a valid attempt.
2. V<sub>cl</sub> = *C* / (*C* − 1) · Σ<sub>c</sub> (*k*<sub>c</sub> − p̂ · *n*<sub>c</sub>)² ÷ *N*², and
   V<sub>srs</sub> = p̂ (1 − p̂) ÷ *N*.
3. **DEFF** = max(1, V<sub>cl</sub> ÷ V<sub>srs</sub>) when 0 < p̂ < 1 and *C* ≥ 2; otherwise 1.
4. *n*<sub>eff</sub> = *N* ÷ DEFF and *k*<sub>eff</sub> = p̂ · *n*<sub>eff</sub>.
5. The interval is Wilson on (*k*<sub>eff</sub>, *n*<sub>eff</sub>)
   (`BenchmarkProportionInterval.Wilson95(double, double)`), whose exact-bound branches follow the
   integer version: *k*<sub>eff</sub> = 0 gives a lower bound of 0, *k*<sub>eff</sub> = *n*<sub>eff</sub>
   an upper bound of 1. With a design effect of 1 it is the plain Wilson interval on (Σ*k*, Σ*n*). With
   *k* = 0 the design effect is 1, so the upper bound is the plain Wilson one, about 3.84 ÷ (*n* + 3.84),
   consistent with the rule of three.

The upper bound including unattributed errors has its own interval, computed the same way with the
unattributed attempts added to *k*.

### 6.2 The clusters

- **(run, 10-minute UTC block of the attempt's start)**, always. Time blocks capture provider bursts,
  which cross the parallel questions of one run.
- **Runs**, as well, when two or more recorded runs are pooled. Run clusters capture day-to-day
  variation.
- The **larger** of the two design effects is used, so the interval is never narrower than either
  structure supports.

Within one run the interval is **descriptive**: a burst longer than 10 minutes spans several blocks and
is only partly captured. Across runs, the run clusters cap that effect.

### 6.3 Few attempts

With **fewer than 100 valid attempts** (`FewAttemptsThreshold`) the figure carries the tag *few
attempts* (OpenRouter shows no uptime figure below 100 requests). The figure is still shown: a count is
never hidden.

### 6.4 Comparing models, and comparing periods

- **Models (Model Comparison)**: descriptive only — each model's rate with its interval, on one common
  scale. No significance ranking is added: the comparison already carries its own pre-declared tests,
  and one more family invites cherry-picking.
- **Periods (Chat Consistency)**: a pre-declared primary endpoint, **P6**, with a margin of ±1.0
  percentage point, stratified by time of week, under Protocol V2
  ([`ai-benchmark-chat-consistency.md`](ai-benchmark-chat-consistency.md) § 9).

### 6.5 Formatting

The server (`BenchmarkProviderAvailability`) and the client (`provider-availability.ts`) format alike,
byte for byte, in the invariant culture; `BenchmarkProviderAvailabilityTests` holds the parity table the
client spec copies. Since 2026-10-15 both write the percent sign in the US style, right after the number
(`0.81%`); before, both wrote a space (`0.81 %`).

- **The rate** (`FormatPercent`) has **2 significant digits** and no space before the sign. With
  *v* = the proportion, clamped to [0, 1], × 100:
  1. *v* = 0 gives `0`.
  2. The decimals *d* are 0 when *v* ≥ 10; otherwise the smallest *d* ≥ 1 with *v* × 10<sup>d</sup> ≥ 10
     (beyond 15 decimals the value gives `0`).
  3. *r* = floor(*v* × 10<sup>d</sup> + 0.5), rounding half away from zero.
  4. When *d* > 0 and *r* reaches 100 (9.96 → 10), *d* drops by one and *r* is rounded again.
  5. *r* is written with the decimal point *d* places from the right, with one digit before the point;
     trailing zeros are kept.

  So `0%`, `0.81%`, `3.0%`, `3.1%`, `12%`, `100%`. Per-mille is never used.
- **The count** always comes with its denominator (`FormatCount`): `12 of 1,480 attempts`, `1 of 1
  attempt`, with a comma thousands separator.
- **The interval** (`FormatInterval`): `95% CI 0.46–1.4%`, each bound formatted as the rate, joined by
  an en dash, one percent sign at the end.
- **The headline** (`FormatRate`): the rate, then in parentheses the count, the interval and *few
  attempts* below 100, separated by `; ` — `0.81% (12 of 1,480 attempts; 95% CI 0.46–1.4%)`,
  `3.0% (3 of 99 attempts; 95% CI 1.0–8.5%; few attempts)`, `0% (0 of 1,480 attempts; 95% CI
  0–0.26%)`. With no valid attempt it reads `no valid attempts`; a pool with no recorded run reads *Not
  recorded*.
- **In prose only**, a rate under 1 % with *k* > 0 (tested in integers as *k* × 100 < *n*) gets a natural
  frequency (`FormatOneIn`), *about 1 in N* with **N = round(*n* / *k*)** (floor(*n* / *k* + 0.5)): 12 of
  1,480 is *about 1 in 123*. Natural frequencies aid reasoning (Hoffrage et al., *Science* 2000).

## 7. Re-runs and Lineage

### 7.1 The fields

- **`BenchmarkRun.RepairPassCount`** and **`ProviderRepairPassCount`**: each *Re-run failed questions*
  pass and each single-answer re-run is one repair pass; it is provider-caused when any answer it
  re-executed had origin `Provider` before.
- **`BenchmarkRun.ReplacesRunId`**: the run this run replaced, a plain indexed `long?` with **no foreign
  key**; a reader tolerates a predecessor that has since been deleted.
- **`BenchmarkRun.ReplacementCause`**: `Unknown`, `ProviderFailure`, `OurFailure`, `InstrumentChange` or
  `OperatorRequest` (§ 7.3).
- **`BenchmarkRunAnswer.ReExecutionCount`**: how many times the answer was re-executed.

### 7.2 Which launches link which runs

Every launch goes through `BenchmarkRunLauncher.CreateAndLaunchRunAsync`, whose optional
`replacesRunId` and `replacementCause` are stamped on the new run. The launches that replace an earlier
run are:

- **A battery run's resume.** A failed member is superseded and relaunched under the same
  `(SuiteIndex, Round)`; the new run replaces the slot's newest superseded member. **Attaching** an
  existing run to a slot links the attached run to the slot's previous run — its live occupant, else its
  newest superseded member — with cause `OperatorRequest`, unless the attached run already names a
  predecessor or the link would close a cycle.
- **A model batch's resume** (*Continue*, *Re-run under current instrument*, *Continue — accept the
  change*), which relaunches a superseded child:
  - a **single-run child**: the new run replaces the superseded child's run;
  - a **series child**: the whole series is replaced, and the link is recorded **member to member** —
    each member run of the new series replaces the newest run at the same index of the superseded series;
  - a **battery child**: the whole battery run is replaced, **member to member** — each member of the
    new battery run replaces the same `(SuiteIndex, Round)` member of the superseded one (its live
    member, else its newest superseded), where that member exists.
- **A series resume replaces nothing.** `ResumeSeriesAsync` continues at the next index: a failed member
  keeps its index and stays failed. Series members therefore never carry `ReplacesRunId`; a failed
  series member still enters chat consistency as an availability-only unit (§ 8).

The other launches, a fresh start from the launcher among them, replace nothing.

### 7.3 The cause

Decided from the replaced run by `BenchmarkReplacementCauses.Decide`, the first that applies:

| Cause | When |
|---|---|
| `InstrumentChange` | The run was replaced under the current instrument (*Re-run under current instrument*). |
| `OperatorRequest` | The replacement came from an attach, or the replaced run was canceled by the operator. |
| `Unknown` | The replaced run is no longer stored. |
| `ProviderFailure` | A terminal answer of the replaced run failed with origin `Provider`. |
| `OurFailure` | Otherwise, for a recorded run. |
| `Unknown` | Otherwise: the replaced run is a legacy run (started before harness 56). |

### 7.4 Re-runs needed

**Re-runs needed** for a run, or a pool of runs, is the sum of the repair passes of every run in the
pool plus the number of runs in the pool that another run of the pool replaced
(`BenchmarkProviderAvailability.CountReruns`); a replaced run counts once, and is provider-caused when a
run replacing it carries `ProviderFailure`. The pool of a run is the run and its predecessors, walked
through `ReplacesRunId`, so the count covers each predecessor's own passes. The re-run figures cover
every run of the pool, recorded or legacy.

## 8. Survivorship: Which Attempts a Pooled Figure Includes

Runs that ended `Failed` or `Canceled`, unusable battery members and superseded members are exactly the
runs an outage produces, and none of them enters Model Comparison or chat consistency as a scored unit.
A pooled availability figure must not lose them:

- **Battery run, Model Comparison and model batch figures** pool every selected run, **its lineage
  predecessors** (loaded separately by walking `ReplacesRunId`, because Model Comparison never admits a
  `Failed` or `Canceled` run to an entry), and **every repair pass** of each. A failed run that was later
  replaced therefore still counts. A standalone failed run with no lineage link is not pulled in,
  because nothing says it belonged to that comparison. The figure says *"includes N replaced runs"*.
- **Chat consistency P6** includes, beside the period's units, every `Failed` or `Canceled` run of the
  same model and the same suite or battery whose start lies **inside the period's run range** — at or
  after the first unit's start and at or before the last unit's, by start then id — and the lineage
  predecessors of the period's units. They are listed in the run selection with the unanalyzed reason
  `availabilityOnly`. In a battery comparison they are the failed or superseded **member runs** of the
  battery runs in the set; a whole battery run that is incomplete or failed is not pulled in.

## 9. Presentation

**The rules.** Position on a common scale is read more precisely than any other encoding (Cleveland and
McGill, 1984), so the error rate gets its **own aligned axis** rather than a hue on another chart. There
are **no red, amber or green traffic lights**: where a threshold cue is used it is redundant, a dashed
ring plus a text label, in color-blind-safe hues. **The count always appears with the rate.** The figure
stays visible without being prominent: card lists show nothing while all is well. Every chart option is
optional and remembered per browser in `localStorage`, read and written behind `try/catch`.

| Where | What | Default |
|---|---|---|
| Run and battery run result dialogs, *Key figures* | the tile **Provider errors** (`provider-errors`): the rate, *12 of 1,480 attempts*, then *95% CI … · 3 unattributed · 2 re-runs (1 provider)*; *few attempts* below 100; *Not recorded* on a legacy run; neutral tone. It can be hidden with **Choose figures** like every tile | shown |
| Both dialogs, *Integrity* tab | the **Provider availability** section (`app-provider-availability`): a one-sentence summary, the breakdown table (class, count, share of valid attempts, typical statuses) with our-side rows under *Excluded: our side*, the outcome view, the re-run line (repair passes, replaced runs as links, first-attempt success), the grader line, and in the battery dialog a per-member table (member, attempts, provider errors, rate, re-runs) | view `strip`, stored under `overseer.benchmark.providerAvailability.view` |
| The outcome view | `strip`: 5-minute UTC bins from the first to the last attempt, each shaded by its provider-error share on a color-blind-safe sequential ramp, gray with no attempts, hatched for bins with our-side failures only, with a visually hidden table of the same data; `bar`: a 100 %-stacked bar of attempts by class with counts labeled | `strip` |
| Run dialog, *Questions* tab | the failed answer's line by origin and kind (§ 4) | — |
| Run History run and battery run cards; Model Comparison source cards | the outlined badge `app-provider-error-badge`, **only when** provider errors or re-runs are above 0: *Provider errors 0.81%* and/or *2 re-runs*, its tooltip *12 of 1,480 attempts; 3 unattributed; 2 re-runs, 1 for provider errors*. Run History adds the sort *Most provider errors* (legacy runs last) and the Flags facet value *Provider errors* | hidden at zero |
| Chat Consistency step-1 run cards and step-3 period cards | a *Provider errors* fact | — |
| Model batch member figures; multi-run progress table and its `--- MEMBERS ---` block; battery leaderboard | *Provider errors* and *Re-runs* (model batch); a *Provider errors* column (multi-run, leaderboard, sortable there) | — |
| Model Comparison, figure style group `availability` | `panel` (`on` · `off`): a fourth P1 panel, a dot per model with its interval, labeled *k/n*, hollow below 100 attempts; `measure` (`strict` · `inclusive`); `scale` (`sqrt` · `linear`); `markers` on the S1–S3 scatters (`off` · `on`): a dashed ring and *3.1% provider errors* on a model whose interval's **lower** bound exceeds 2 % | panel `on`, measure `strict`, scale `sqrt`, markers `off` |
| Model Comparison, figure **A1 Provider errors** | the same dot plot as its own figure card, always present, which documents can carry (`a1-availability`) | — |
| Model Comparison table | **Provider errors**, `0.81% (12/1,480)`; **Re-runs needed**, `3 (2 provider)` | Provider errors shown, Re-runs needed hidden |
| Chat Consistency, figure **Provider availability** | `points`: a dot per unit with its interval, area by valid attempts, availability-only units hollow, ▲ above a unit that needed a provider-caused re-run; `controlChart`: a Laney p′ chart labeled *control limits*; `strip`: one row per measure, one cell per UTC day | `points` |
| Documents | the `availability.*` facts, a Key Figures bullet, a *Provider availability* subsection, threats lines, figure A1 and figure `cc5-availability` ([`ai-benchmark-report-pack.md`](ai-benchmark-report-pack.md)) | — |

The scatter markers use the interval's lower bound so that a model is marked only when the data show
more than 1 in 50 attempts failing at the provider, not because of noise. The words are always those of
an observation: *provider errors observed in GnollBench runs*, never *the provider throttled*, never a
cause or an intent.

## 10. Legacy Runs

There is **no backfill**. The old counters lump our 429s with the provider's, and re-runs deleted their
attempts, so any reconstruction would be wrong in a direction no reader could see. A legacy run —
started before harness 56 — stays unrecorded even when it is repaired later (§ 2). Every surface shows
*Not recorded* for it; its answers keep their stored statuses and a null `FailureOrigin`; its documents
mark every `availability.*` fact unavailable, *not recorded before harness 56*. Pooled figures say
*"recorded for M of N runs"* and are computed on the recorded runs only, while their re-run figures
cover every run.

## 11. Known Limitations

- **The retry policy is measured, not changed.** HTTP-level 504, 529 and 408 are not retried; only 429
  (up to `AiRateLimitSettings:Max429RetriesPerCall`, 4) and 500, 502 and 503 are. Request exceptions —
  transport failures and the HttpClient timeout — are never retried. A stream `error` event **before the
  first chunk** is retried when `ProviderErrorRetryPolicy.IsRetryable` matches it, and that list does
  include 529, 504 and *overloaded*; an error after the stream has produced output is not retried. Retrying
  more would change what the candidate experiences, a candidate-input and timing change with its own
  harness bump and comparability cost, so it is left to a separate decision, which this ledger is the
  evidence for.
- **`ProviderTimeout` is rarely reachable** under the question budgets (§ 3.4), so provider stalls are
  mostly counted as ours.
- **Unattributed errors** cannot be resolved from the wire; they are shown as an upper bound, never in
  the headline.
- **Within one run the interval is descriptive**, and a burst longer than 10 minutes is only partly
  captured by the time-block clusters (§ 6.2).
- **The hours sampled.** Every figure covers only the hours the runs ran. GnollBench runs are made by
  hand on the development computer; a figure is only as current as the last run someone made.
- **Custom endpoints** follow one conservative 429 rule (§ 3.2), not per-endpoint semantics.

## 12. Related Measurements

- [`api-key-alerts.md`](api-key-alerts.md) — § *What triggers an alert* holds the classifier table for
  *out of balance* and *rejected key* that `OurBilling` and `OurKey` reuse; § *Known gap: Google* explains
  why a Google 429 is never treated as a balance failure, and records the long runs of Gemini 429s on
  healthy keys behind the *unattributed* class.
- [`gemini-service-tier-measurements.md`](gemini-service-tier-measurements.md) — § *`gemini-3.7-flash` —
  saturated on both tiers and both endpoints*: HTTP 503 `UNAVAILABLE` and 45–60 s hangs with no response
  headers on a saturated Gemini model, the shapes behind `ProviderOverloaded`.
- [`anthropic-model-latency-measurements.md`](anthropic-model-latency-measurements.md) — § *Error
  behaviour* and § *What to conclude*: a sample with no `429` and no `529 overloaded_error`, and the
  advice to code defensively against both.
