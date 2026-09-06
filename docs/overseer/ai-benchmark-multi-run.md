# Multi-Run Benchmark Analysis — The Statistical Method

This document is the full statement of the method behind Overseer's multi-run benchmark feature:
what a *series* is, what a *group* is, which sets of runs may be averaged together, exactly how each
reported figure is computed, and — the part that matters most — **what the method cannot tell you.**

It is the companion to [`ai-benchmark.md`](ai-benchmark.md), which describes the single-run harness.
Read that first if you have not.

Implementation:

| Concern | File |
|---|---|
| Which runs may be compared, and at what tier | `Overseer/Services/Benchmarking/BenchmarkComparabilityKey.cs` |
| Every statistic below | `Overseer/Services/Benchmarking/BenchmarkGroupStatistics.cs` |
| Per-item reuse | `Overseer/Services/Benchmarking/BenchmarkItemAnalysis.cs` |
| Loading, persisting, and refusing to pool | `Overseer/Services/Benchmarking/BenchmarkGroupAnalysisService.cs` |
| The Markdown report | `Overseer/Services/Benchmarking/BenchmarkGroupReportBuilder.cs` |
| Sequential execution of a series | `Overseer/Services/Benchmarking/BenchmarkSeriesOrchestrator.cs` |

---

## 1. Why Multi-Run Exists

A single benchmark run produces one number per question and one Intelligence Index for the run. That
index carries a confidence interval, but the interval describes **item sampling only** — how much the
figure would move if the suite had drawn a different set of questions. It says nothing about how much
the figure would move if you simply ran the same suite again with the same model.

That second quantity is the one that decides whether a change is real. When a tool guide is edited
and the Index moves by two points, the only honest question is *"is two points more than this
instrument's own noise?"* — and a single run on each side cannot answer it, because a single run has
no measured noise.

A **replicate set** is *R* runs of one identical configuration. It measures that noise directly, and
everything else in this document follows from having it.

---

## 2. Series and Groups

They are different objects and the distinction is load-bearing.

A **series** (`BenchmarkRunSeries`) is an *execution* concept: a request to run the same benchmark
*N* times. It carries the validated start request as JSON so every member is launched from one
snapshot, runs its members **strictly one at a time** through the same single-run gate
(`BenchmarkRunManager.TryStart`) that a manual run uses, and can be stopped and resumed. Sequential
execution is not merely a constraint imposed by that gate — it is also the timing mode the report
already calls *"Sequential (comparable speed)"*, which is the correct shape for replicate speed
measurement.

A **group** (`BenchmarkRunGroup`) is an *analysis* concept: a named set of runs to compute statistics
over. Membership is many-to-many, so one run may sit in several groups — a baseline run belongs both
to its own replicate set and to the cross-condition pair it anchors — while belonging to at most one
series.

A completed series with two or more successful members automatically creates a group containing them.
The code **asserts** that this group resolves Tier A rather than assuming it.

---

## 3. Comparability: Which Runs May Be Averaged

This is the guard against the worst failure the feature can have: **a confident pooled index computed
over runs that were never comparable.** Once such a number exists, nothing about it looks wrong.

`BenchmarkComparabilityKey.Extract` projects a run into a list of named keys. Every key is one of
four kinds, and the kind decides what a difference on it costs.

| Kind | Meaning | A difference means |
|---|---|---|
| **Fundamental** | Suite identity and the answer key — the suite id, and the item revision of every question | The runs answered different questions, or the same questions against a different rubric. Nothing can be paired. **Not comparable** |
| **Candidate** | Provider, model id, thinking level, reasoning mode and summary, service tier, max output tokens, parallel execution mode, the prompt-options signature | A different subject was measured. **Not comparable** |
| **Instrument** | `CandidateSystemPromptSha256`, `ToolGuidesSha256`, `KnowledgeBaseHeadSha`, harness version, scoring method version, the scoring profile *and its snapshot*, the assessor / second-opinion / claim-verifier configurations, the per-question budgets | The measuring apparatus moved |
| **Speed and cost** | Question parallelism (the timing mode) and the pricing snapshot | Speed and cost mix conditions; quality does not |

### 3.1 The tiers

| Tier | Rule | What may be computed |
|---|---|---|
| **A — Replicate** | Every key matches | Everything. The only tier at which a pooled index, its reproducibility component, and its speed and cost aggregates are all sound |
| **B — Quality-comparable** | Only *speed-and-cost* keys differ | Quality aggregates valid. Speed and cost aggregates rendered with a **degraded** flag naming what moved |
| **C — Cross-condition** | Fundamental and Candidate keys all match; **exactly one** Instrument key differs | **Never pooled.** Such a set is two groups, and the tool's job is to *compare* them |
| *below B* | A Fundamental or Candidate key differs, or two or more Instrument keys differ | Nothing. Group creation is refused, with the differing keys named |

Two or more instrument differences drop the set below Tier B rather than to Tier C, deliberately: Tier
C's whole claim is that exactly one thing was varied on purpose, and a set with two varied keys cannot
attribute a difference to either.

### 3.2 A tier verdict always carries its reasons

`BenchmarkComparabilityResult` returns not just the tier but **which keys differed, what each value
was, and which runs carried it**. A boolean verdict with no reason is unusable in a dialog or a bug
report, so the group builder, the report manifest and the diagnostics capture all render the reasons.

The result also carries `ComparabilityKeyHash` — an order-independent hash of the set's per-run key
hashes, defined at every tier. Equal sets hash equally regardless of the order they were passed in,
and a set whose membership changes hashes differently, which is what lets a stored analysis be
recognised as stale.

### 3.3 Composite keys

The assessor, second-opinion and claim-verifier configurations, the scoring profile (id **and**
snapshot hash), and the per-question budgets are each emitted as **one** composite key rather than as
several. A single deliberate assessor swap has to count as one instrument difference; emitted
separately, changing one model would look like four differences and drop a legitimate Tier C set
below Tier B.

The scoring profile is keyed on its **snapshot**, not only its id, because the *Standard Intelligence
Index (Default)* profile is edited in place.

---

## 4. Per-Item Statistics

For each item *q*, over the *R* runs that produced a scored answer to it. `BenchmarkItemAnalysis`
already computes discrimination, the difficulty delta, the budget-bound fraction and the confound
flags over an explicit run set; those are **reused, never reimplemented**, and the sample predicate
itself (`BenchmarkItemAnalysis.Samples`) is shared, so a group's denominators and the suite-health
panel's denominators cannot drift apart.

The group layer adds only what a replicate set makes meaningful:

| Figure | Definition | Undefined when |
|---|---|---|
| Mean | Arithmetic mean of the cross-run quality scores | — |
| Median | Central value at odd *R*; mean of the two central values at even *R* | — |
| **Sample** SD | *n*−1 denominator | *R* < 2 |
| Min, Max | — | — |
| IQR | P75 − P25, linear interpolation between order statistics | *R* < 2 |
| Coefficient of variation | SD / mean, reported as a percentage | *R* < 2, or mean = 0 |
| 95 % CI on the item mean | mean ± *t*(*R*−1) · SD / √*R* | *R* < 2 |
| **Critical-error rate** | *k* / *R*, where *k* is the runs in which the assessor flagged a critical error | — |
| Median model time | Median of the item's model-attributable times | no timings |

> **Sample SD here, population SD in the suite-health table.** `BenchmarkItemStatistics.StdDev` uses
> the population denominator over the same numbers. Both are correct for what they claim: the
> suite-health table *describes the runs it has*, while a replicate set *estimates the spread of the
> process that produced them*.

**Unstable items.** An item whose sample SD reaches the configured threshold (default 15.0 quality
points) is flagged **unstable**. This is where a single run's verdict is least trustworthy — and it is
a suite-health signal at least as much as a model one: an item whose rubric cannot decide a borderline
answer will swing between runs no matter which model answers it.

**A critical-error rate strictly between 0 and 1** is the signal worth acting on. It means the same
question sometimes does and sometimes does not trip the score ceiling — either a genuinely borderline
answer or, more often, a rubric that does not decide the case.

---

## 5. The Multi-Run Intelligence Index

### 5.1 The point estimate, and an identity

The multi-run Index is the **mean of the *R* per-run difficulty-weighted indices**.

Those per-run indices are recomputed here from a **fixed** per-item weight — the mean of the
assessors' per-run difficulty ratings, falling back to the question's stored `AssessedDifficulty`,
then to 50, never below 1 — rather than read off the stored `BenchmarkRun.QualityIndex`. The stored
value is integer-rounded and uses each run's own weights, and either of those alone breaks the
identity below. The mean of the stored values is still reported, labelled as a cross-check, and
nothing is computed from it.

For a fixed item set answered by every run:

> **mean of the per-run indices  ≡  difficulty-weighted mean of the per-item cross-run means**

This identity is asserted in a unit test, because it is the property that makes the two variance
decompositions in the next section coherent. When the item set is **ragged** — some run failed to
produce a scored answer to some item — the two routes weight the missing cells differently, the
`IdentityHolds` flag goes false, and the report says which number it is showing.

### 5.2 Two independent sources of uncertainty

They are reported **separately and only then combined**, because they answer different questions and
behave differently as runs are added.

**Reproducibility standard error** — *"would a re-run move this?"*

```
SE_repro = SD(per-run indices) / √R
half-width = t(R−1, 0.975) · SE_repro
```

**It shrinks with *R*,** as 1/√*R*. This is the component multi-run exists to measure, and the reason
a three-run set says something a one-run set cannot.

**Item-sampling standard error** — *"would a different set of questions move this?"*

This is the existing `BenchmarkScoring.QualityIndexStandardError` — the same function the single-run
report uses — applied to the **per-item cross-run mean** qualities.

```
half-width = 1.96 · SE_item
```

> **It does not shrink with *R*, and that is correct.** Every run answers the same items. Adding runs
> sharpens the estimate of each item's mean and does nothing whatever about the fact that the suite
> drew those particular questions. A reader who expects the whole interval to fall as √*R* will
> conclude the code is broken. It is not — shrinking that component would require *more questions*,
> not more runs.

**Combined interval:**

```
half-width = √( (t · SE_repro)² + (1.96 · SE_item)² )
```

Added in quadrature because item selection and run-to-run variation are independent sources.

### 5.3 The *R* < 3 rule

Below **three** runs, no reproducibility figure is reported at all — not the SD, not the SE, not the
half-width. A run-to-run standard deviation from two points is not a measurement, and reporting one
invites a reader to act on noise.

This mirrors conventions already in the codebase: the `n < 3 → null` rule in
`BenchmarkScoring.QualityIndexStandardError` and `BenchmarkItemAnalysis.MinRunsForMeasurement`.

At *R* < 3 the combined interval is the item-sampling half-width alone, and the report says the
interval covers one source rather than two.

---

## 6. Speed

- **Mean and sample SD of the *R* per-run Speed Indices.**
- **Pooled per-answer model-time percentiles** — P50, P90 and max over all *R* × *Q* answers, by
  linear interpolation between order statistics. Pooled rather than averaged per run: the question is
  what a single slow turn looks like, and a mean of per-run medians cannot answer it.
- **Per-item median model time.**

The standing single-run caveat carries over: the Speed Index is comparable only within one thinking
level and one timing mode. At Tier A the comparability keys enforce exactly that; at Tier B the
figures are rendered with a degraded flag naming what moved.

Question parallelism degrades **both** speed and cost, not speed alone — concurrency changes cache and
token behaviour as well as wall time.

---

## 7. Cost

Total across the runs; mean ± sample SD per run; the per-role split (candidate / assessor / claim
verifier); cost per question; and cost per index point.

Per-run costs are resolved from each run's **own pricing snapshot**, with the same arithmetic the
single-run report uses — including the long-context buckets and the service tier the provider
**actually served** rather than the one requested — so a group total is the sum of the figures an
operator already read on the individual runs.

A run whose pricing cannot be resolved is **omitted**, not zeroed. An unknown cost is reported as
unknown.

A pricing-snapshot mismatch degrades **cost only**. Prices cannot move a quality score.

---

## 8. Comparing Two Groups

This is the verification use case: a baseline replicate set and a treatment replicate set that differ
on exactly one instrument key.

Items are **paired by question** on per-item cross-run mean quality. Differences are **treatment minus
baseline**, so a positive value means the treatment scored higher. Only items answered on both sides
are paired; the rest are counted and excluded, because a pair needs two halves and imputing one would
invent the finding.

| Statistic | Definition | Why this one |
|---|---|---|
| Mean paired difference, with 95 % CI | mean ± *t*(*n*−1) · SD / √*n* | The effect, on the scale the reader already understands |
| **Wilcoxon signed-rank** (primary) | Zero differences discarded (Wilcoxon's own reduction, and the count is reported); tie-corrected average ranks; **exact** conditional-permutation p-value for *n* ≤ 25, tie- and continuity-corrected normal approximation above that | Non-parametric, which is what a bounded 0–100 scale over eighteen items warrants |
| Paired *t* (secondary) | Standard | Reported for readers who expect it — never *instead* of Wilcoxon, because it assumes a normality nobody has checked |
| **Cohen's *d*z** | mean difference / SD of the differences | The paired effect size. Not *d*, which uses a pooled between-group SD and would describe a comparison nobody made |

### 8.1 Per-item differences are exploratory

Per-item comparisons use Welch's *t* over the two sides' per-run scores for that item, and are
reported under **Benjamini–Hochberg** step-up false-discovery-rate control (default *q* = 0.05). The
adjusted column is a **q-value**, not a p-value.

Every per-item comparison carries an `Exploratory` flag that is always true, present precisely so that
no surface can render one without the label. Eighteen simultaneous item tests without correction would
manufacture findings, reliably, on data with no effect in it at all.

### 8.2 The comparison never pools

`BenchmarkGroupStatistics.Compare` compares two already-computed group results. It never averages them
together. The refusal to pool a Tier C set lives at the persistence boundary
(`BenchmarkGroupAnalysisService`), which is where a caller could otherwise do it by accident.

---

## 9. What This Method Cannot Decompose

**Run-to-run variance mixes candidate stochasticity with grader stochasticity.**

Each run produces a *new answer*, which is then graded *once*. So when an item is flagged unstable,
there are two live explanations and this method cannot separate them:

1. the candidate genuinely answers that question differently each time, or
2. the grader scores answers of the same quality differently each time.

Separating them requires **re-grading identical answers** — `SecondOpinionMode = All`, or a
re-assessment pass over stored answers. The run's sampled assessor agreement (see
`SecondOpinionMinimumSample` and the `FlaggedPlusSample` mode in [`ai-benchmark.md`](ai-benchmark.md))
is the partial measurement available today, and it is *conditioned* — measured disproportionately on
answers the first assessor already doubted — so it is not an unbiased agreement rate either.

This sentence is carried on the statistics result itself
(`BenchmarkGroupStatisticsResult.VarianceDecompositionCaveat`) rather than left to each renderer, so
that every surface showing these numbers has it available. Without it, an operator will attribute item
instability to the model when it may be the grader.

**Other limits, inherited from the single-run harness:** everything in
[`ai-benchmark.md`](ai-benchmark.md) § 9.1 *"What Is Measured vs. What Is Unmeasured"* still applies.
A replicate set measures the same thing more precisely; it does not measure anything new.

---

## 10. Reading a Multi-Run Report

A practical order:

1. **Check the tier first.** Tier A is the only tier at which the pooled index means what it appears
   to mean. At Tier B, read quality and ignore speed and cost. At Tier C, do not read the aggregates
   at all — read the comparison.
2. **Read both interval components before the combined one.** If the reproducibility half-width is
   large relative to the effect you care about, more runs will help. If the item-sampling half-width
   dominates, more runs will not — you need more questions.
3. **Read the unstable items.** They tell you which parts of the suite cannot support a single-run
   conclusion.
4. **Only then read the point estimate.**

For a verification, the acceptance criterion should be stated **before** the treatment set is run, and
compared against the paired difference and its interval — not against the two point estimates.

---

## 11. The Report Carries No AI-Written Prose

The multi-run report is entirely arithmetic. This is a deliberate non-goal rather than an omission: a
cross-run narrative would add cost, a new AI path, and a second place for a synthesis to contradict
its own evidence — exactly the defect the single-run report's **Synthesis Accuracy Divergence**
advisory exists to catch.

Every figure in the report is reproducible from the member runs' stored answers, which is what makes
it usable as an instrument for deciding whether a change is kept.
