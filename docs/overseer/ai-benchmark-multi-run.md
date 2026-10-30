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
| The crossed (pigeonhole) bootstrap of statistics method 2 | `Overseer/Services/Benchmarking/BenchmarkCrossedBootstrap.cs` |
| Per-item reuse | `Overseer/Services/Benchmarking/BenchmarkItemAnalysis.cs` |
| Loading, persisting, refusing to pool, and analyzing a new group | `Overseer/Services/Benchmarking/BenchmarkGroupAnalysisService.cs` |
| The generated group name | `Overseer/Services/Benchmarking/BenchmarkRunGroupNames.cs` |
| Suggested groups | `Overseer/Services/Benchmarking/BenchmarkRunGroupSuggestionService.cs` |
| The Markdown report | `Overseer/Services/Benchmarking/BenchmarkGroupReportBuilder.cs` |
| Sequential execution of a series | `Overseer/Services/Benchmarking/BenchmarkSeriesOrchestrator.cs` |
| The page, the New group dialog and the analysis dialog | `Overseer/ClientApp/src/app/admin/benchmark/multi-run/` |

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

A **group** (`BenchmarkRunGroup`) is an *analysis* concept: a set of runs to compute statistics
over. Membership is many-to-many, so one run may sit in several groups — a baseline run belongs both
to its own replicate set and to the cross-condition pair it anchors — while belonging to at most one
series.

A completed series with two or more successful members automatically creates a group containing them,
and analyzes it (§ 2.1). The code **asserts** that this group resolves Tier A rather than assuming it,
logging an error when it does not.

> **Every series before 2026-09-07 failed that assertion, and the assertion was right.** The pricing
> snapshot embedded `capturedAtUtc` in the JSON the comparability key hashed, so the
> `PricingSnapshot` key could never agree between two runs launched minutes apart: Tier A was
> unreachable by construction and every multi-run cost aggregate was flagged degraded on a difference
> that was a timestamp. The key now hashes the snapshot's prices alone
> (`BenchmarkComparabilityKey.PricingSignature`). A report generated before the fix says Tier B for
> this reason and its cost figures are sound; re-running the analysis moves the group to Tier A. The
> assertion's error reached Sentry as **OVERSEER-8**.

**Groups stay single-suite.** The suite is a Fundamental key, so a group never mixes suites, and
nothing in this document combines them. The cross-suite layer is the **battery**
([`ai-benchmark-multi-suite.md`](ai-benchmark-multi-suite.md)): a fixed set of suites with declared
weights, run for one model one suite after another, whose per-suite figures are this document's
statistics, reused unchanged, and whose Overall Index is their weighted composite. A battery run with
two or more rounds also creates one ordinary group per suite, and analyzes each before the battery
composite, so each suite's replicate analysis stays available here.

### 2.1 Groups are analyzed on creation

Every group that can be pooled is analyzed on the server as soon as it exists, through
`BenchmarkGroupAnalysisService.TryAnalyseNewGroupAsync`:

- a group created by hand, at Tier A or Tier B (New group, or *Create group* on a suggestion);
- the group a completed series creates;
- each per-suite group a battery with *Runs per Suite* ≥ 2 creates, before the battery composite is
  computed.

A Tier C group is not analyzed — it is compared (§ 8) — and neither is a group recorded below Tier B.
An analysis that fails or is refused (an unsettled member, say) is logged and changes nothing else:
the group is still created, and the series or battery run keeps its own outcome. The group then shows
*Not analyzed*, and **Run analysis** in its analysis dialog computes it later.

Each analysis also writes its headline figures — index, combined half-width, mean Speed Index and mean
cost per run — to columns of its `BenchmarkGroupAnalysis` row (`HeadlineIndex`, `HeadlineHalfWidth`,
`HeadlineSpeedIndex`, `HeadlineCostPerRun`), which is what the group cards read; migration
`GroupAnalysisHeadlines` backfilled them from the stored result JSON. A stored analysis without them
shows *Re-analyze to show figures*.

An analysis is **stale** when the group's membership changed after it, or when a member was re-run in
place after it (the member's `RerunCompletedAtUtc` is later than the analysis). A stale analysis is
badged, not discarded: it is a correct statement about the answers it was computed over.

### 2.2 Group names

A group needs no name. When none is given, the server stores the generated
`Suite · Model · yyyy-MM-dd · R=n` (`BenchmarkRunGroupNames.Default`, cut to 256 characters): the
newest member's suite and model, the earliest member's start date in UTC, and the run count. The series,
the battery and a manual create all name a group through that one function. A name sent with a create
is kept, trimmed; one over 256 characters is refused.

The UI asks for no name. A group card's title is the model, and its prominent line is the member runs
(`#41 #42 #43`), each a link to its run report. A name someone typed in the past — one that does not
have the generated shape for its suite, model and run count (`NameIsGenerated` false) — is shown as a
quoted caption under the title. The Markdown report's heading still uses the stored name.

### 2.3 Suggested groups, and no duplicates

`GET api/admin/benchmark/runs/groups/suggestions?suiteId=` clusters the suite's newest 300 settled runs
(`Completed`, `CompletedWithLimits`, `CompletedWithErrors`) by their **full comparability key hash**,
keeps the clusters of two or more runs that resolve Tier A together, drops every cluster that an
existing group's members equal or contain, and returns the rest newest first, at most 20. It is
read-only.

The page shows them above the groups as *Suggested groups* cards: the model, the runs, the tier, *R*,
the time span, the series and the mean of the runs' stored indices. **Create group** creates the group
and analyzes it in one click; **Dismiss** hides the suggestion in this browser (`localStorage`, keyed by
the key hash and the run ids, so a cluster that gains a run shows again). Suggestions are never turned
into groups automatically: clusters overlap as runs accumulate, and creating each would multiply groups.

A group whose member set **equals** an existing group of the same suite is refused, by the create and
by the preview alike: *Group #12 already holds exactly these runs.*, with `duplicateOfGroupId`. The New
group dialog shows the message with a link that opens that group.

### 2.4 The page

GnollBench → **Multi-Run Analysis** has a header card — the title, a **Suite** select remembered per
browser and independent of Run Benchmark's suite (the launcher's suite is only the fallback for a
browser with none remembered), an info tip on groups and tiers, **New group** and Refresh — then the
*Suggested groups* cards and the *Analysis groups* card list, with a search (`#id`, model, run, note or
name), *Sort by*, and *Tier*, *Analysis*, *Origin* and *Model* facets, ten cards at a time. Each card
shows the index with its half-width, speed, cost per run and the run count, and offers *Open analysis*,
the Markdown report download and *Delete group*.

**New group** opens a dialog listing the suite's runs as cards (model, id, status, start, index, speed,
and the five instrument hashes), with its own search, sort and facets. Selecting two or more runs
previews their tier with the reasons (§ 3.2); an unsettled run cannot be selected. A Tier C selection
offers the *Persist as a cross-condition group* acknowledgment. An optional note is the only free text.

The analysis opens in a dialog with section tabs — **Summary**, **Dimensions**, **Items**,
**Speed & cost**, **Tokens & tools** and **Comparison** — and names the statistics method beside the
provenance line, so every interval is read by the method that produced it (§ 12).

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
| **Instrument** | `CandidateSystemPromptSha256`, `ToolGuidesSha256`, `KnowledgeBaseHeadSha`, harness version, scoring method version, the scoring profile *and its snapshot*, the assessor / second-reader / claim-verifier configurations, the per-question budgets | The measuring apparatus moved |
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

Whatever its tier, a group is **not analyzed while a member is unsettled**: a member whose status is
`Running` (a re-run in progress), `Failed` or `Canceled` refuses the analysis, naming the lowest-id
such member (`BenchmarkRunFinalizer.IsPoolable` admits `Completed`, `CompletedWithLimits` and
`CompletedWithErrors`). The error reads *Member run #{id} is still in progress. Analyze the group once
it has finished.* or *Member run #{id} ended {Status} and produced no measurement. Remove it from the
group to analyze the rest.*, and no analysis is stored. The group a series creates never admits such a
member; a user-made group can hold one, for example while a member's failed questions are re-run.

A member whose own Intelligence Index is **withheld** (a question failed at the provider, so its run
detail reads *Not computed*) is still pooled over its scored answers, and is marked rather than
hidden: `BenchmarkRunGroupMemberDto.IndexWithheld` is set, the group's member list reads *Index not
computed* for it, the analysis shows a warning under the point estimate — *Run #12 withholds its own
index (a question failed at the provider); its per-run index here is over its scored answers.* or
*Runs #12, #15 withhold their own index (a question failed at the provider); their per-run indices
here are over their scored answers.* — and the Markdown report's *Group Manifest* reads `withheld` in
the member's Index cell, followed by *Run #12 withholds its own Intelligence Index (a question failed
at the provider); the figures below are over its scored answers.* (or *Runs #12, #15 withhold their
own Intelligence Index …; the figures below are over their scored answers.*). The member flag reads
the run's stored terminal-failure count, or its answers' statuses when none was stored; the Markdown
report reads the stored count alone, which every run finalized at harness 21 or later has.

### 3.2 A tier verdict always carries its reasons

`BenchmarkComparabilityResult` returns not just the tier but **which keys differed, what each value
was, and which runs carried it**. A boolean verdict with no reason is unusable in a dialog or a bug
report, so the group builder, the report manifest and the diagnostics capture all render the reasons.

The result also carries `ComparabilityKeyHash` — an order-independent hash of the set's per-run key
hashes, defined at every tier. Equal sets hash equally regardless of the order they were passed in,
and a set whose membership changes hashes differently, which is what lets a stored analysis be
recognised as stale.

The hash is only as stable as the key definitions that produced it. `BenchmarkComparabilityKey.DefinitionVersion`
is stored beside it in `BenchmarkRunGroup.ComparabilityKeyVersion` whenever a group's hash is written
(creation, membership edit, analysis, and the group a series creates). A group whose stored version is
not the current one carries `comparabilityKeyStale` and shows a *Comparability definition changed*
badge beside *Stale analysis*; re-analysing it refreshes the hash and the version. The next key
definition change only has to bump the constant.

### 3.3 Composite keys

The assessor, second-reader and claim-verifier configurations, the scoring profile (id **and**
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

**The items come from the member runs, not the live suite.** `BenchmarkRunExam.Build` builds one
item per question the members answered (keyed on `BenchmarkQuestionIdUsed ?? BenchmarkQuestionId`),
carrying the revision the members were graded under, so the predicate's *answered against the
item's revision* is the graded revision. Editing, deleting, adding or reordering questions, renaming
the suite or deleting it changes none of a group's figures; a group whose suite was deleted still
analyses. Paired comparison pairs an item only when both groups were graded under the same revision
of it; the rest are counted as `RevisionMismatchedItemCount` and excluded.

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
| **Critical-error rate** | *k* / *R*, where *k* is the runs with a critical error on the item: a **confirmed** one on a scoring-method-13 run, member A's flag on an earlier run | — |
| Median model time | Median of the item's model-attributable times | no timings |

> **Sample SD here, population SD in the suite-health table.** `BenchmarkItemStatistics.StdDev` uses
> the population denominator over the same numbers. Both are correct for what they claim: the
> suite-health table *describes the runs it has*, while a replicate set *estimates the spread of the
> process that produced them*.

**Unstable items.** An item whose sample SD reaches the configured threshold (default 15.0 quality
points) is flagged **unstable**. This is where a single run's verdict is least trustworthy — and it is
a suite-health signal at least as much as a model one: an item whose rubric cannot decide a borderline
answer will swing between runs no matter which model answers it.

Under statistics method 2 an item is flagged unstable **only from three runs**, the same *R* < 3 rule
the reproducibility figure follows (§ 5.3): a sample SD from two scores is arithmetic rather than evidence, and an item flag
built on it invites a reader to act on noise. At *R* = 2 the item reads
*Too few runs* instead (`InsufficientRuns`). Method 1 analyses flagged an item from two runs, and are
shown as stored.

**A critical-error rate strictly between 0 and 1** is the signal worth acting on. It means the same
question sometimes does and sometimes does not trip the score ceiling — either a genuinely borderline
answer or, more often, a rubric that does not decide the case.

**What counts as a critical error depends on the scoring method.**

- **Method 13 and later:** only a **confirmed** one — resolved *Agreed*, *UpheldByVerifier* or
  *SingleAssessor* (`BenchmarkCriticalErrorResolver.IsConfirmed`). A split the claim verifier
  overturned, or left unresolved, does not count.
- **Method 12 and earlier:** member A's flag, the assessor's, as before. Those runs record no
  resolution.

A set spanning the boundary differs on at least two Instrument keys (the scoring method version and
the harness version), so it is below Tier B and is never one group: no rate mixes the two definitions.

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

### 5.2 Two sources of uncertainty, and the combined interval

Two sources of uncertainty sit on the index, and they are always **reported separately**, because they
answer different questions and behave differently as runs are added. How the combined 95 % interval is
formed from the data depends on the analysis's statistics method (§ 12): method 2 bootstraps it, and
keeps the two components as diagnostics; method 1 added the components in quadrature.

**Reproducibility standard error** — *"would a re-run move this?"*

```
SE_repro = SD(per-run indices) / √R
half-width = t(R−1, 0.975) · SE_repro
```

**It shrinks with *R*,** as 1/√*R*. This is the component multi-run exists to measure, and the reason
a three-run set says something a one-run set cannot.

**Item-sampling standard error** — *"would a different set of questions move this?"*

The weighted standard error of the difficulty-weighted mean of the **per-item cross-run mean**
qualities. Under method 2 it is `BenchmarkScoring.QualityIndexStandardErrorUnrounded`: the unrounded
item means and weights, with deviations taken around the unrounded weighted mean. Under method 1 it was
`BenchmarkScoring.QualityIndexStandardError` — the function the single-run report uses — which rounds the
means and weights to integers and centers on the rounded index.

```
half-width = 1.96 · SE_item
```

> **It does not shrink with *R*, and that is correct.** Every run answers the same items. Adding runs
> sharpens the estimate of each item's mean and does nothing whatever about the fact that the suite
> drew those particular questions. A reader who expects the whole interval to fall as √*R* will
> conclude the code is broken. It is not — shrinking that component would require *more questions*,
> not more runs.

#### The combined interval: a crossed bootstrap (method 2)

A group's scores form a crossed array: every run answers every question, so a score carries a run
effect, a question effect and a residual. The combined interval is the **percentile interval of a
crossed (pigeonhole) bootstrap** over that array (Owen 2007, *The pigeonhole bootstrap*, Ann. Appl.
Stat. 1(2); Owen & Eckles 2012), computed by `BenchmarkCrossedBootstrap.IndexDistribution`:

- Each replicate draws the questions with replacement **once** and the runs with replacement **once**.
- Its statistic is the index itself, recomputed on the resampled array: the mean, over the drawn runs,
  of each run's weighted index `Σ w·score / Σ w` over the drawn questions it answered, with each
  question's fixed weight from § 5.1. A drawn run that answered none of the drawn questions is left out
  of that replicate's mean.
- 10,000 replicates (`DefaultReplicates`) with the fixed seed 20261025 (`DefaultSeed`), so a fixed group
  gives a fixed interval, reproducible from the stored answers. The question draw and the run draw use
  separate random streams.
- The bounds are the 2.5th and 97.5th percentiles of the replicates (`CombinedLower`, `CombinedUpper`),
  which lie inside [0, 100] by construction. `CombinedHalfWidth` is half their distance; a percentile
  interval need not be symmetric about the point estimate. `CombinedIntervalMethod` records
  `crossedBootstrap`, and `BootstrapReplicates` and `BootstrapSeed` the draw.

In such a replicate the run effect, the question effect and the residual are each represented once, so
the bootstrap variance is **mildly conservative, never anti-conservative**. With three runs the run draw
has only ten distinct multisets, which is one reason it is conservative rather than tight.

#### Why the components are no longer added

**Under a crossed item × run model, both standard errors contain the residual term.** The
reproducibility SE is computed from per-run indices, each of which carries the residual averaged over
the questions; the item-sampling SE is computed from per-item means, each of which carries it averaged
over the runs. Adding the two in quadrature counted the residual twice, and it also mixed a *t*
quantile with a *z* quantile. Method 2 therefore reports both as **diagnostics** of where the
interval's width comes from — the reproducibility SE with its *t* half-width, the item-sampling SE with
its 1.96 half-width — and adds neither into the interval. The report heads them *What the interval is
made of*.

#### Method 1's combined interval

Analyses stored before 2026-10-25, and the surfaces that still compute method 1 (§ 12.1), combine the two
half-widths in quadrature, treating item selection and run-to-run variation as independent sources:

```
half-width = √( (t · SE_repro)² + (1.96 · SE_item)² )
```

### 5.3 The *R* < 3 rule

Below **three** runs, no reproducibility figure is reported at all — not the SD, not the SE, not the
half-width. A run-to-run standard deviation from two points is not a measurement, and reporting one
invites a reader to act on noise.

This mirrors conventions already in the codebase: the `n < 3 → null` rule in
`BenchmarkScoring.QualityIndexStandardError` and `BenchmarkItemAnalysis.MinRunsForMeasurement`.

Under method 2, below three runs the bootstrap redraws **only the questions** (a *question bootstrap*,
`CombinedIntervalMethod` = `questionBootstrap`). Run-to-run variation is then not in the interval at
all: a spread between two runs is no more evidence here than it is in the reproducibility figure, so it
is not resampled. The dialog and the report say the interval covers the choice of questions only.

Under method 1, at *R* < 3 the combined interval is the item-sampling half-width alone, and the report
says the interval covers one source rather than two.

### 5.4 Intervals are clamped to the score range

A quality score is bounded [0, 100], so a reported interval bound outside that range is not a wider
claim but an impossible one — an item with a mean of 71 and a half-width of 58 does not have an upper
bound of 129. Reported bounds are clamped and the interval is **marked truncated**, per item and, under
method 1, for the combined index interval. A method 2 combined interval is a set of percentiles of
indices that are themselves inside the range, so it is never clamped and `CombinedIntervalTruncated` is
always false.

The **half-widths are never clamped**. They are the quantities that carry the spread and, under method
1, combine in quadrature, and clamping one would corrupt the other. A truncated interval is therefore
narrower than its own half-width implies, which is exactly why it is marked: read the SD or the
half-width, not the pinned bound.

### 5.5 Per-dimension statistics

The index is a composite and cannot say which dimension moved. Each of Accuracy, Completeness,
Conciseness and Readability is therefore pooled across the runs in its own right: per-run means, their
mean, sample SD, a 95 % interval, min, max, and the weakest items on that dimension.

Under method 2 the interval is the **same bootstrap over the dimension scores**, with every question
weighted 1, so the statistic on the original data is the mean of the per-run means:
`BootstrapLower` and `BootstrapUpper`, with `IntervalMethod` `crossedBootstrap` from three runs that
scored the dimension and `questionBootstrap` below that. `ConfidenceHalfWidth` is half the interval's
width, and it covers question sampling as well as run-to-run variation. Under method 1 the interval was
a *t*-based half-width over the per-run means, run-to-run variation alone, under the *R* < 3 rule
above.

Two properties matter when reading them:

- **They are unweighted**, while the index is difficulty-weighted. There are no per-dimension
  difficulty weights to apply, so the two are not expected to agree, and a discrepancy is not a defect.
- **A dimension can be low because the prompt said so.** A concise response instruction caps
  Completeness by design. The report puts that warning next to the table and the configuration next to
  the manifest, because the alternative is an attribution error — see § 2 of the
  `server_benchmark_to_chat_transfer` skill.

A dimension no member scored is reported present and empty rather than omitted, so a consumer never
has to distinguish "absent" from "unscored".

### 5.6 Runs or questions, and when the runs were made

Two method 2 figures say what the next round of measurement should buy.

**The planning figure.** `RunsWhereRunComponentMatchesQuestions` is the smallest *R* ≥ 3 at which the
run component's half-width is no larger than the question component's:

```
1.96 · SD_repro / √R  ≤  1.96 · SE_item      ⇔      R = max(3, ⌈(SD_repro / SE_item)²⌉)
```

Past it, more questions narrow the interval more than more runs do. It is null below three runs and
when the item-sampling SE is zero or unavailable. The dialog and the report state it as *From about
{n} runs, more questions narrow the interval more than more runs.*

**The time span.** `RunSpanHours` is the time between the earliest and the latest member's start. A
series runs its members back to back, so its run component measures *re-running now*, not *re-running
on another day*: a provider-side change between days is not in it. When the span is under 24 hours
with *R* ≥ 3, the dialog and the report warn: *All runs were made within {n} hours; the run component
measures re-running now, not on another day.*

---

## 6. Speed

- **Mean and sample SD of the *R* per-run Speed Indices.**
- **Pooled per-answer model-time percentiles** — P50, P90 and max over all *R* × *Q* answers, by
  linear interpolation between order statistics. Pooled rather than averaged per run: the question is
  what a single slow turn looks like, and a mean of per-run medians cannot answer it.
- **Per-item median model time.**

The standing single-run caveat carries over: the Speed Index is comparable only within one thinking
level and one timing mode, and the comparability keys covering both are what enforce it.

**A tier is not a timing verdict.** A group sits at Tier B whenever exactly one instrument or
speed-and-cost key differs, and several of those — the pricing snapshot most obviously — cannot move
a timing measurement at all. The caveat printed with the speed figures is therefore built from the
group's **own** degraded flags (`BenchmarkGroupStatistics.BuildSpeedCaveat`): it names the keys that
actually moved, or states positively that none affecting timing did. Reading "Tier B" as "the speed
numbers are degraded" is wrong in both directions.

Question parallelism degrades **both** speed and cost, not speed alone — concurrency changes cache and
token behaviour as well as wall time.

---

## 7. Cost

Total across the runs; mean ± sample SD per run; the per-role split (candidate / assessor / claim
verifier); cost per question; and cost per index point.

The second reader's spend is a role of its own, named *Second reader*, or *Reference reader* for a
panel run; a set that mixes panel and single-assessor runs shows both. Analyses stored before
2026-09-27 name it *Second opinion*, which the view maps to *Second reader* for display. The
comparability key that signs the reader's settings is displayed as *Second reader configuration*;
its stored name, `SecondOpinionConfiguration`, does not change.

**Cost per question** is the mean cost per run divided by the questions the costed runs asked: their
answer rows, averaged over the runs whose pricing resolved. Failed, canceled and ungraded answers are
included, and so are answers graded under an older rubric revision, because run token totals are
summed over every answer row. It is therefore *not* divided by the item count the index uses.
Analyses stored before 2026-09-23 carry the old scored-items figure and say so; to update one,
recompute it.

Per-run costs are resolved from each run's **own pricing snapshot**, with the same arithmetic the
single-run report uses — including the long-context buckets and the service tier the provider
**actually served** rather than the one requested — so a group total is the sum of the figures an
operator already read on the individual runs.

A run whose pricing cannot be resolved is **omitted**, not zeroed. An unknown cost is reported as
unknown.

A pricing-snapshot mismatch degrades **cost only**. Prices cannot move a quality score.

**Per-role dispersion is reported beside the per-role totals** — SD, min and max per role, plus the
individual per-run totals. Cost is the least reproducible quantity a replicate set measures: a set
whose 22 comparability keys match and whose Intelligence Index reproduces to a third of a point can
still spend twice as much on one member as another, and a mean with one standard deviation on the
total does not say which role did that. The report names the role carrying the spread.

## 7.1 Token and Tool Usage

Rendered only when a member recorded any of it. Pooled candidate input, output and prompt-cache-read
tokens with the input:output ratio and cache-read share; the per-run input totals and their SD;
assessor and claim-verifier tokens **kept separate** from the candidate's, as the run row keeps them;
tool calls by family with each family's share; and the claim-verification outcome — claims checked,
supported, refuted, indeterminate.

The last of those exists to be read beside the verifier's share of the cost table. A verifier can be
most of a set's spend, and a cost figure that names it without saying how many claims it checked
leaves a reader unable to judge whether the spend bought anything.

A family's share is prompt-compliant or not only against the tool preference hierarchy in
`Overseer/ToolGuides/_policy.md`. A high source-code share on a suite weighted toward exact mechanics
is what that policy asks for, not a routing defect.

---

## 8. Comparing Two Groups

This is the verification use case: a baseline replicate set and a treatment replicate set that differ
on exactly one instrument key. In the analysis dialog's **Comparison** tab the group being analyzed is
the treatment, and the group chosen under *Compare with* — another Tier A or Tier B group of the same
suite — is the baseline.

Items are **paired by question**. Differences are **treatment minus baseline**, so a positive value
means the treatment scored higher. Only items answered on both sides, under the same rubric revision,
are paired; the rest are counted and excluded, because a pair needs two halves and imputing one would
invent the finding.

**The guard.** A comparison pairs items of one suite, so the analysis refuses a baseline in another
suite (*Group #{id} is in another suite; a comparison pairs items of one suite.*) and the group itself
(*A group cannot be compared with itself.*). Both halves are computed under the same statistics method.

**What differs between the groups.** A method 2 comparison records `PairKeyDifferences`: the
comparability keys whose values split **exactly along the two groups** — one value carried by every
baseline run, another by every treatment run. A key that also varies inside a group is left out, as is
any key on which a run sits in both groups. The dialog and the report list each key with its kind,
description and both values. More than one such key means the difference cannot be attributed to any
one change, and both say so; none means no recorded change separates the groups.

#### The primary estimand: the weighted index difference (method 2)

The primary result is the **difficulty-weighted index difference**, treatment minus baseline
(`IndexDifference`): each side's multi-run Intelligence Index (§ 5.1) recomputed over the **paired
items only**, with the **treatment group's item weights on both sides**. It is the same quantity as the
headline index, which the unweighted question-level tests below are not. One weighting has to serve
both sides, because difficulty is re-assessed in every run and the two groups' weights differ;
using the treatment side's is an arbitrary but fixed choice.

Its 95 % interval (`IndexDifferenceLower`, `IndexDifferenceUpper`) is the percentile interval of a
**paired crossed bootstrap** (`BenchmarkCrossedBootstrap.DifferenceDistribution`, 10,000 replicates,
seed 20261025): each replicate draws the questions once and uses that draw for both groups, which keeps
the pairing, then redraws each group's runs independently. Run-to-run variation is included only when
**both sides have three runs or more** (`IndexDifferenceMethod` `crossedBootstrap`,
`RunVarianceIncluded` true); otherwise only the questions are redrawn (`questionBootstrap`), and the
interval covers the choice of questions only, so it is narrower than a re-run would justify.

Beside it:

- **Bootstrap p** (`IndexDifferenceBootstrapP`) — `2 · min(share of replicates ≤ 0, share ≥ 0)`, capped
  at 1. It is the inversion of the percentile interval, not an exact test, and its resolution is
  2 / 10,000 = 0.0002; a value of 0 means below that and is shown as *< 0.001*.
- **Standard error** (`IndexDifferenceStandardError`) — the sample SD of the replicates.
- **Minimum detectable difference** (`MinimumDetectableIndexDifference`) —
  `(z₀.₉₇₅ + z₀.₈₀) · SE ≈ 2.80 · SE`: the smallest true difference this design detects with 80 % power at
  a two-sided α of 0.05. A true difference smaller than this would be missed more than one time in five,
  so a non-significant result says only that any effect is probably below it, not that there is none.

#### Secondary, question-level tests

Paired on per-item cross-run mean quality, **unweighted**, one observation per question. They ignore
run-to-run variation, which is why the index difference is primary under method 2.

| Statistic | Definition | Why this one |
|---|---|---|
| Unweighted mean difference, with 95 % *t* interval | mean ± *t*(*n*−1) · SD / √*n* | The effect, on the scale the reader already understands |
| **Wilcoxon signed-rank** | Zero differences discarded (Wilcoxon's own reduction, and the count is reported); tie-corrected average ranks; **exact** conditional-permutation p-value for *n* ≤ 25, tie- and continuity-corrected normal approximation above that | Non-parametric, which is what a bounded 0–100 scale over eighteen items warrants |
| **Hodges–Lehmann shift** (method 2) | The Hodges–Lehmann estimate of location over the per-item differences | The location estimate that matches Wilcoxon's hypothesis, which a mean does not |
| Paired *t* | Standard | Reported for readers who expect it — never *instead* of Wilcoxon, because it assumes a normality nobody has checked |
| **Cohen's *d*av** (method 2) | mean item difference ÷ the mean of the two groups' **population** SDs over all run × question scores of the paired items (Lakens 2013) | An effect size that does not grow with *R*: duplicating every run leaves it unchanged |

**Why *d*z is no longer shown.** Cohen's *d*z — mean difference ÷ SD of the differences — is computed on
cross-run item means, which tighten as runs are added, so the same shift reads as a larger effect at
larger *R*. It stays in the JSON (`CohensDz`) and is shown for method 1 comparisons only.

#### Method 1's comparison

A method 1 comparison has no index difference: its result is the unweighted question-level table above
with **Wilcoxon as the primary test**, the paired *t* as secondary and *d*z as the effect size, and no
Hodges–Lehmann shift, *d*av, `PairKeyDifferences` or prior-comparison count.

### 8.1 Per-item differences are exploratory

Per-item comparisons use Welch's *t* over the two sides' per-run scores for that item, and are
reported under **Benjamini–Hochberg** step-up false-discovery-rate control (default *q* = 0.05). The
adjusted column is a **q-value**, not a p-value.

Every per-item comparison carries an `Exploratory` flag that is always true, present precisely so that
no surface can render one without the label. Eighteen simultaneous item tests without correction would
manufacture findings, reliably, on data with no effect in it at all.

**Untestable items (method 2).** An item with no variance estimate — zero spread on both sides, or fewer
than two runs on a side — has no Welch test. It is marked **Untestable** (`Untestable`, counted in
`UntestableItemCount`), carries no p-value, and is left out of the Benjamini–Hochberg step-up and of its
*m*. Method 1 gave zero spread on both sides with different means p = 0, which on discrete rubric scores
at small *R* was a guaranteed false rejection.

### 8.2 The comparison never pools

`BenchmarkGroupStatistics.Compare` compares two already-computed group results. It never averages them
together. The refusal to pool a Tier C set lives at the persistence boundary
(`BenchmarkGroupAnalysisService`), which is where a caller could otherwise do it by accident.

### 8.3 Siblings: the wizard's Paired tests and the run report's Paired Test tab

*Compare with group* is no longer the only paired test, but it is still the reference for the others.
Since 2026-10-03 two siblings ask the same question elsewhere, and all three are one method family,
`BenchmarkPairedTests`, built on the primitives of this section (`BenchmarkGroupStatistics.Compare`,
`WilcoxonSignedRank`, `CohensDz`, `StudentTCritical95`) and re-implementing none:

- The **Model Comparison wizard's Paired tests view** tests runs, groups or battery results of one
  condition against a reference or across all pairs. For run and group entries its Intelligence row
  **is** `Compare` over each entry's `Compute` result, at statistics method 1, which a unit test pins.
  For the same two groups its mean difference, Wilcoxon and paired *t* therefore equal the
  question-level tests of *Compare with group*; it has no weighted index difference. Each measure is a
  family, Holm-adjusted when it holds more than one test.
- The **run report's Paired Test tab** tests one run against another finished run on the same suite,
  without building groups, and names the pair a *model comparison*, a *verification of a change* or a
  *replicate*.

Both add the quality dimensions, and speed and cost as Wilcoxon on log ratios, which *Compare with
group* does not report. *Compare with group* keeps what they lack: the weighted index difference with
its bootstrap interval, the per-item exploratory comparisons of § 8.1 and a persisted comparison inside
the group analysis. See `ai-benchmark.md`, *Paired Tests*.

### 8.4 Repeated comparisons

Each analysis that carries a comparison counts the earlier analyses of the same group that carried one
(`PriorComparisonCount`). From **two** earlier comparisons, the dialog adds a note — *This group has been
compared {n} times before. A difference found after several looks is weaker evidence; decide what
difference matters before looking.* — and the report a *Compared {n} times before* subsection. Every further comparison is another chance for one of
them to look significant by luck, and nothing in a single p-value accounts for the looks before it.

The remedy is procedural, not arithmetic: **decide the difference that matters before looking** — state
the acceptance criterion before the treatment set is run (§ 10), and judge the index difference and its
interval against it, alongside the minimum detectable difference.

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

1. **Check the tier first, and what made it.** Tier A is the only tier at which the pooled index
   means what it appears to mean. At Tier B, read the *differing key* rather than assuming what is
   degraded: the report's degraded flags name which of speed and cost the difference actually
   affects, and a Tier B group can have both intact. At Tier C, do not read the aggregates at all —
   read the comparison.
2. **Read the Chat Prompt Under Test block, before any dimensional figure.** It is in the manifest,
   and it shows what the candidate was told to do, which any attribution of a dimension to the model
   must check first. A group whose configuration was
   not recorded cannot support an attribution at all, and the block says so instead of printing
   defaults. It also states whether wiki context was pre-injected — live chat always does, and a run
   does from harness 64 under the Production condition; for any other run every retrieval and
   routing figure is measured under a condition production does not share.
3. **Read both interval components before the combined one.** If the reproducibility half-width is
   large relative to the effect you care about, more runs will help. If the item-sampling half-width
   dominates, more runs will not — you need more questions; under method 2 the planning figure (§ 5.6)
   says from how many runs that is so. Check which statistics method the analysis carries and, under
   method 2, whether the interval is a crossed or a question bootstrap and whether the runs were made
   within one day. An interval marked truncated (method 1) is narrower than its half-width implies;
   read the half-width.
4. **Read the dimensions.** Which dimension is lowest, and whether its gap survives across runs, is
   the question a single run cannot answer and this one can.
5. **Read the unstable items.** They tell you which parts of the suite cannot support a single-run
   conclusion.
6. **Read cost per role, not just in total.** The spread almost always sits in one role, and which
   one is the actionable half.
7. **Only then read the point estimate.**

For a verification, the acceptance criterion should be stated **before** the treatment set is run, and
compared against the paired difference and its interval — under method 2 the weighted index difference,
and its minimum detectable difference — not against the two point estimates.

---

## 11. The Report Carries No AI-Written Prose

The multi-run report is entirely arithmetic. This is a deliberate non-goal rather than an omission: a
cross-run narrative would add cost, a new AI path, and a second place for a synthesis to contradict
its own evidence — exactly the defect the single-run report's **Synthesis Accuracy Divergence**
advisory exists to catch.

Every figure in the report is reproducible from the member runs' stored answers, which is what makes
it usable as an instrument for deciding whether a change is kept.

This non-goal still holds for the multi-run report. It was reversed for **batteries** on 2026-10-03: a
battery run's Executive Summary and Root Cause Analysis are written by a report writer under the Report Pack rule — numbers from code, words from the writer, rendering with no AI —
while the battery's Markdown report stays arithmetic only. The decision and its reasons are in
[`ai-benchmark-multi-suite.md`](ai-benchmark-multi-suite.md) § 7.2, and the mechanics in
[`ai-benchmark-report-pack.md`](ai-benchmark-report-pack.md) § 14. A single run already had its own
AI-written documents (`ai-benchmark-report-pack.md` § 11).

---

## 12. Statistics Method Versions

Every group analysis and comparison records the statistics method it was computed under, as
`StatisticsMethodVersion` on the result and on the comparison. An absent or null value is method 1. A
stored analysis is **never rewritten**: the dialog, the Markdown report and every other reader render
each analysis by its own version, and recomputing an analysis stores a new one under the current
method.

- **Method 1** — until 2026-10-25. The combined index interval adds the reproducibility and
  item-sampling half-widths in quadrature (§ 5.2); dimension intervals are *t*-based over the per-run
  means; an item is flagged unstable from two runs; the comparison is the unweighted question-level one,
  with Wilcoxon primary and *d*z as the effect size; per-item Welch tests give zero spread with different
  means p = 0.
- **Method 2** — from 2026-10-25, on every new Multi-Run Analysis analysis. The crossed bootstrap
  interval with the components as diagnostics (§ 5.2), the question bootstrap below three runs (§ 5.3),
  bootstrap dimension intervals (§ 5.5), the planning figure and time span (§ 5.6), *Unstable* only from
  three runs (§ 4), and the comparison of § 8: the weighted index difference as primary estimand,
  Hodges–Lehmann and *d*av, untestable items outside Benjamini–Hochberg, *What differs between the
  groups* and the prior-comparisons note.

The bootstrap draws from a fixed seed, so a method 2 figure is as reproducible from the stored answers
as a method 1 one (§ 11). The analysis dialog names the method in its provenance line, and the Markdown
report in its header (*Statistics method: 2*) and its closing line.

### 12.1 Boundary of Method 2

Method 2 is used by **Multi-Run Analysis**: the group analysis, the comparison, the Markdown group
report, the analysis dialog, and the model batch result of a series member, which reads the group's
stored analysis ([`ai-benchmark-model-batches.md`](ai-benchmark-model-batches.md) § 6).

Everything else that calls `BenchmarkGroupStatistics` still computes **method 1**, and its figures and
JSON are unchanged:

- the battery composite and its comparisons, and the battery analysis service
  ([`ai-benchmark-multi-suite.md`](ai-benchmark-multi-suite.md));
- Model Comparison, including its Paired tests view (§ 8.3);
- the run report's Paired Test tab.

One group can therefore show a method 1 interval in Model Comparison and a method 2 interval in its own
analysis. The two are different estimates of the same uncertainty, not a contradiction, which is why the
dialog and the report name the method beside every interval.
