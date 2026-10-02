# Multi-Suite Benchmark Batteries — Execution and the Statistical Method

This document describes Overseer's **battery**: a named, fixed set of two or more benchmark suites
with declared weights, run for one model one suite after another and combined into one **Overall
Intelligence Index** with an honest uncertainty interval. It covers what a battery is, how a battery
run executes, the admin UI, the API, and the full statistical method.

It is the third document of a set. [`ai-benchmark.md`](ai-benchmark.md) describes the single-run
harness, and [`ai-benchmark-multi-run.md`](ai-benchmark-multi-run.md) describes series, groups,
the comparability tiers and the multi-run Intelligence Index of **one** suite. This document assumes
the multi-run one: a battery reuses its per-suite statistics unchanged and adds the layer that combines
suites.

Implementation:

| Concern | File |
|---|---|
| Battery definitions, weights per scheme, the definition hash | `Overseer/Services/Benchmarking/BenchmarkBatteryDefinition.cs` |
| Key classification, the battery-run verdict, comparison eligibility | `Overseer/Services/Benchmarking/BenchmarkBatteryComparability.cs` |
| Every statistic below | `Overseer/Services/Benchmarking/BenchmarkBatteryStatistics.cs` |
| Round-robin slot order and the *usable member* rule | `Overseer/Services/Benchmarking/BenchmarkBatteryPlanner.cs` |
| Sequential execution, stop, resume, cancel, restart reconciliation | `Overseer/Services/Benchmarking/BenchmarkBatteryOrchestrator.cs` |
| Loading members, per-suite statistics, persisting the analysis | `Overseer/Services/Benchmarking/BenchmarkBatteryAnalysisService.cs` |
| The Markdown report | `Overseer/Services/Benchmarking/BenchmarkBatteryReportBuilder.cs` |
| The API | `Overseer/Controllers/AdminBenchmarkBatteriesController.cs` |
| Data model | `GnollHackServer.Data/BenchmarkBattery.cs`, `BenchmarkBatteryRun.cs`, `BenchmarkBatteryAnalysis.cs` |
| Client | `Overseer/ClientApp/src/app/admin/benchmark/batteries/` and the launcher in `benchmark.component.*` |

---

## 1. What a Battery Is

A suite measures one area — gameplay help, item identification, source-code questions, one game
board. A model that is strong in one area can be weak in another, and choosing a model for the chat
needs one figure over all of them. Before batteries, the suite id was a *Fundamental* comparability key,
so runs of different suites were never pooled, grouped or compared.

Four objects, and the distinctions matter:

- A **battery** (`BenchmarkBattery`) is a *definition*: a name, an optional description, an ordered
  list of suites (the order is the run order), a **weighting scheme**, custom weights when the scheme
  is *Custom*, a `Revision` and a `DefinitionSha256`. The term is the psychometric one: a set of tests
  given together and scored as a composite.
- A **battery run** (`BenchmarkBatteryRun`) is an *execution*: one model configuration run on every
  suite of a battery, *R* times each (*Runs per Suite*). It stores its own snapshot of the definition
  (`DefinitionJson`), so editing or deleting the battery later never changes an existing result.
- A **member** (`BenchmarkBatteryRunMember`) links one ordinary benchmark run to one **slot** of a
  battery run: a pair (suite index, round). A member was either *launched* by the battery run or
  *attached* from an earlier run (§ 3.6). Membership is many-to-many, so one run may serve several
  battery runs.
- A **battery analysis** (`BenchmarkBatteryAnalysis`) is the persisted composite: the member run ids
  it used, the result, the definition hash and the comparability class (M9). Like a group analysis,
  it stays reproducible after a member run is deleted, and is marked stale rather than discarded
  when the usable member set changes.

**Groups stay single-suite.** A battery does not widen the run group: a group is still a set of runs of
one suite, judged by the tiers of `ai-benchmark-multi-run.md` § 3. The battery is the layer above it.
When *Runs per Suite* is 2 or more, a finished battery run creates one ordinary run group per suite
that has at least two usable members, so per-suite replicate analysis keeps working in the Multi-Run
Analysis tab.

**Revision and hash.** Changing the scheme, the suites, their order or a custom weight increments
`Revision`. The definition hash covers the scheme and the set of suites with their custom weights,
**not** their order or the name (M9), so reordering the suites bumps the revision and leaves the hash
unchanged: the run order changed, the estimand did not. A name or description edit changes neither.

**A battery whose suite was deleted** keeps the suite's row with a null suite id and its name. It is
*broken*: validation fails on it, the launcher does not offer it, and its DTO lists the missing suite
in `BrokenSuiteNames`. Editing it to remove or replace the suite repairs it. A battery can also be
**archived** — hidden from the launcher without being deleted — and restored again.

---

## 2. Why *Questions and Difficulty* Is the Default

Suites differ in how many questions they hold and in how hard those questions are. A weight per suite
that ignored either would let a short suite, or an easy one, move the headline as much as a long, hard
one. The default scheme, **Questions and difficulty** (`DifficultyMass`), weighs suite *s* by its
**difficulty mass**

```
D_s = Σ_{q ∈ s} d_q          w_s = D_s / D,   D = Σ_s D_s
```

where `d_q` is the question's difficulty weight — the same fixed item weight the single-suite index
already uses (M2). `D_s` grows with both: one more question adds its difficulty, and a harder question
adds more.

It is also the only scheme under which the Overall Index means exactly what a single suite's
Intelligence Index means. A suite's index is a difficulty-weighted mean of its question qualities
`x_q`, so

```
Σ_s (D_s / D) · ( Σ_{q∈s} d_q x_q / D_s )  =  Σ_q d_q x_q / D
```

> **The Overall Index under the default equals one difficulty-weighted Intelligence Index computed
> over every question of every suite, pooled.** The battery extends the existing index; it does not
> define a new one.

The identity is exact for *R* = 1 and a complete question set, and is asserted in a unit test. For
*R* ≥ 2 it holds wherever the group layer's own identity (`IdentityHolds`,
`ai-benchmark-multi-run.md` § 5.1) holds in every suite. When it does not hold exactly, the result's
`PooledIdentityHolds` flag is false and the report says so (M2).

The default has a price, and it is intended: a long suite of advanced questions can dominate the
Overall Index, and a short easy suite barely moves it. A battery assembled to cover several areas
evenly should use **Equal per suite** or **Custom**. The editor previews every suite's share under
every scheme before the battery is saved, and every analysis reports the Overall Index under the other
automatic schemes as a sensitivity figure (M5), so a ranking that depends on the weighting is visible.

---

## 3. Execution

### 3.1 Round-robin, one member at a time

A battery run of *K* suites and *R* runs per suite has *K* × *R* slots, filled **round-major,
suite-minor**: every suite once in definition order (round 1), then every suite again (round 2), and
so on. The battery has a complete result after the first round, and time-of-day drift at the provider
is spread across suites rather than concentrated in one. Members run strictly one at a time, through
the same launcher and run gate as a single run (`BenchmarkRunLauncher.CreateAndLaunchRunAsync`,
`BenchmarkRunManager.TryStart`); a member is an ordinary benchmark run of one suite with
`RunCount` 1.

The planner (`BenchmarkBatteryPlanner.NextMember`) picks the first **unoccupied** slot. A slot is
occupied by any non-superseded member, usable or not.

### 3.2 Usable members

A member is **usable** when it is not superseded, carries no guard failure (§ 3.4), its run ended
`Completed`, `CompletedWithLimits` or `CompletedWithErrors`, **and the run's `QualityIndex` is not
null**. The last condition is not implied by the status: the finalizer withholds the index of a run
that lost a question at the provider and still reports it `CompletedWithErrors`. Only usable members
enter any statistic; the others are listed on the analysis as *excluded*, each with its reason. The
rule lives in one place (`BenchmarkBatteryPlanner.IsUsable` / `UnusableReason`) and the orchestrator,
the analysis service and the API all read it.

`CompletedMemberCount` on the battery run is the number of slots holding a usable member, recomputed
from the member rows whenever they change rather than incremented.

### 3.3 Starting a battery run

The start checks, in order, and stops at the first refusal:

1. No benchmark run is in flight, no battery run is being driven, and the orchestrator claim (§ 3.7)
   is not held — **409**. Nothing is taken.
2. The run settings are present and *Runs per Suite* is at least 1 — **400**.
3. The battery exists (**404**), is not archived and passes validation: at least two suites, every
   suite still present, no suite twice, and under *Custom* a finite positive weight for every suite
   (**400**).
4. The planned launches — suites × runs per suite, less any attached slots — do not exceed
   `Benchmark:Battery:MaxMembers` (default 60), and do not exceed the daily run cap `MaxRunsPerDay`
   unless **Allow cap wait** is set — **400**, naming the bound. Unlike a series, a battery larger
   than the daily cap is accepted with *Allow cap wait*: the cap is never bypassed, every launch still
   passes the spend guard, and such a battery simply runs for more than a day.
5. The spend guard (`BenchmarkComplianceGuard.CanSpendAsync`) — **429**.
6. The launcher's own request validation **for every suite**, on a copy of the request with that
   suite's id. A suite with a question that has no assessed difficulty fails here, before any spend,
   and the message names the suite. Same-provider grader warnings come back as **409** with the
   `SameProviderWarningDto`, exactly as for a series start, and the launcher's acknowledgment dialogs
   resend.

Then the stored request is resolved (`RunCount` forced to 1, `AllowCapWait` copied from the battery
request, `AllowSourceCodeReferences` defaulting to false), the definition is snapshotted, and the five
instrument hashes a run of each suite would carry now (`CandidateSystemPromptSha256`,
`ToolGuidesSha256`, `KnowledgeBaseHeadSha`, `WikiHeadSha`, `SourceCodeHeadSha`) are recorded per
suite in `SuiteFingerprintsJson`. Attached members are validated and inserted (§ 3.6), the row is
saved, and only then is the orchestrator claim taken under the owner token `battery:<id>`; a claim
lost to a race deletes the row again and refuses the start with **409**. The response is **202**
`{ batteryRunId }`.

### 3.4 Driving, and the two guards after every member

For each slot the drive loop launches a member from a freshly deserialized copy of the stored request
with the slot's suite id. A cap denial parks the battery run in `WaitingForCap` with a bounded retry
when *Allow cap wait* is set, and otherwise stops it with `RunCapReached`. A launch that does not
start stops it with `SpendDenied` or `MemberFailed` and leaves the slot free.

When the member finishes:

- **`Failed` or `Canceled`** (and the battery run was not canceled): the member is marked
  superseded, `FailedMemberCount` grows, and the battery run stops with `MemberFailed`.
- **Successful, index withheld** (not usable, § 3.2): the member stays in its slot and the battery run
  **continues** with the remaining slots. It ends *Completed with errors*, and the suite has no usable
  result until the operator repairs that run with **Re-run Failed Questions** and recomputes, or
  presses **Continue**, which replaces the member with a fresh run. This matches how a series treats
  such a member. The slot stays occupied for the rest of that drive, so a provider that keeps failing
  cannot loop.
- **Successful and usable**: two guards, in this order.
  1. *Fingerprint.* The run's five hashes are compared with those recorded for its suite at start; a
     null on either side does not count as a difference. This is what catches a moved wiki or source
     clone, and a prompt edit between a board suite and a non-board suite.
  2. *Comparability.* `BenchmarkBatteryComparability.Resolve` (M8) over every usable member so far.
     This is the question the analysis asks at the end, asked while it costs one run instead of all of
     them: it catches a harness version change, an in-place edit of the scoring profile or a grader
     configuration, a changed budget, and a rubric or difficulty change between two rounds of one
     suite.

  A guard that fails writes its explanation to the member's `GuardFailure` and stops the battery run
  with **`InstrumentChanged`** (*A member is not comparable with the others*). The run stays a member,
  so the stop is explainable, but it is no longer usable. There is no "continue anyway" (§ 3.5).

When every slot is occupied the battery run ends `Completed` if every slot holds a usable member,
otherwise `CompletedWithErrors`. The analysis is then computed and persisted; a refusal there is logged
and stored in `ErrorMessage` and does not fail the battery run. With *Runs per Suite* ≥ 2, one run group
per suite with at least two usable members is created (*"Auto-created from battery run #id, suite
name."*) and recorded in `AutoCreatedGroupIdsJson`, so finishing twice never creates a second group for
a suite.

The status vocabulary is the series' (`Pending`, `Running`, `WaitingForCap`, `Stopped`, `Completed`,
`CompletedWithErrors`, `Cancelled`, `Failed`), and so are the stop reasons, plus
`InstrumentChanged`, which only batteries set.

### 3.5 Continue, Re-run under the current instrument, Cancel

A battery run is **resumable** when it is `Stopped`, or `CompletedWithErrors` with a slot that holds no
usable member. Resume has two modes:

- **Continue** keeps every usable member, supersedes every other non-superseded member (a member whose
  run the operator has meanwhile repaired in place is usable, and is kept) and launches the free
  slots. It is refused with **409** and `instrumentChanged: true` when any member carries a guard
  failure, when the usable members already refuse the composite, when this build's `HarnessVersion`
  differs from theirs, or when a fingerprint hash of a suite with a free or unusable slot moved since
  start (naming the suites and hashes) — in each case a member launched now could only trip the guard
  again.
- **Re-run under the current instrument** supersedes **every** member, attached ones included, forgets
  the auto-created groups (they stay, as ordinary groups), re-records every suite's fingerprint and
  starts over.

Both are refused with **400** when the status is not resumable or the stored request is no longer
valid for a suite still to be launched, and with **429** at the spend guard. *Continue* is also refused
with **400** when a member it would keep was graded under a scoring method version other than this
build's; *Re-run under the current instrument* is the way forward then.

**Cancel** cancels the in-flight member through the run manager, so its own finalization runs, and
marks the battery run `Cancelled`, which is terminal. It is refused with **400** for a battery run that
is already `Completed`, `Cancelled` or `Failed`.

**A service restart** moves every battery run left `Running`, `WaitingForCap` or `Pending` to
`Stopped` (`MemberFailed`, *"Service restarted."*) at startup, after the series reconciliation. The
member that was in flight is finalized by the ordinary orphaned-run cleanup, and *Continue* supersedes
it if that leaves it unusable.

### 3.6 Attaching existing runs

A new battery revision should not re-run suites it already has results for. A run can be **attached**
to a slot instead of launched — at start through the launcher's **Reuse earlier runs**, or later from
the progress dialog's **Attach existing run** — while the battery run is `Pending` (at start),
`Stopped`, `Completed` or `CompletedWithErrors`, never while it is `Running` or `WaitingForCap`.

One eligibility rule (`BenchmarkBatteryComparability.CheckAttach`) serves the attach, the candidate
list and the reuse preview, so the three cannot disagree. A run is accepted when:

- its suite is the definition's suite at that index;
- it is usable (§ 3.2);
- its scoring method version is current;
- its tested configuration is the battery run's;
- its five instrument hashes equal the suite's recorded fingerprint, a null on either side not
  counting. This is deliberately strict: `WikiHeadSha` and `SourceCodeHeadSha` are provenance rather
  than comparability keys, but an earlier run can be reused only while the wiki, source and
  knowledge-base clones have not moved, as the series resume guard requires;
- the composite verdict (M8) over the current usable members plus this run still permits the
  composite.

The slot must be free, or held by a member that is not usable, which the attach supersedes. Every
refusal names its reason. Attaching to a finished battery run changes its usable member set, so its
latest analysis reads stale and the UI offers *Recompute*.

**Reuse at start** needs a preview, because a battery run starts launching the moment it is created.
`POST runs/reuse-preview` takes the same body as the start and returns, per slot in planner order, the
newest eligible run, or none with the reason the newest candidate was refused, judged against the
fingerprints a start would record now. It creates nothing and spends nothing. The start sends the
previewed run ids as `Attach`; the server checks them again and refuses the whole start if any no
longer qualifies, rather than silently launching what it was told to reuse.

### 3.7 The orchestrator claim

Series and batteries share one **orchestrator claim** on `BenchmarkRunManager`, beside the run gate.
Its owner is `series:<id>` or `battery:<id>`. It is taken last in a start or resume — after the row is
saved, so every validation refusal returns with nothing held — and released only when the drive task
ends. While it is held:

- another series or battery start, or a resume, is refused with **409**;
- a single run (`POST runs`) is refused by the launcher with **409**, *"A battery is running; wait for
  it or cancel it."* or *"A benchmark series is running; wait for it or cancel it."*, and the series and
  battery starts use the same two messages;
- the seven in-place re-run endpoints (`reassess`, `calibrate`, `answers/{answerId}/rerun`,
  `rerun-synthesis`, `retry-failed-assessments`, `retry-claim-verification`, `rerun-failed`) are
  refused with **409** and the same message, before any row is touched;
- `TryStart` itself refuses a run that does not belong to the owner, as the backstop.

A stopped series or battery holds no claim, so repairs such as *Re-run Failed Questions* work then.

### 3.8 Deleting things a battery uses

- **A member run** may be deleted; its membership row goes with it and its slot becomes free. On a
  finished battery run the analysis reads stale and shows *Incomplete* after *Recompute*; on a stopped
  one *Continue* launches the slot again.
- **A suite** cannot be deleted while an active battery run contains it. Otherwise the battery's
  reference is cleared and the battery becomes broken (§ 1).
- **A battery** cannot be deleted while one of its battery runs is `Pending`, `Running` or
  `WaitingForCap` (**409**). Otherwise its battery runs are kept with their own snapshots.
- **A system AI configuration** cannot be deleted while an active battery run's start request names
  it; stopped battery runs that name it are counted in the delete dialog (*N stopped battery runs
  name this configuration and can no longer be resumed.*).

---

## 4. The Admin UI

### 4.1 The launcher: Run Target

Admin → AI Benchmark → **Run Benchmark**, *Test Setup* fieldset:

- **Run Target** — a radio group, *Single suite* (the default) or *Battery*. *Battery* replaces the
  *Benchmark Suite* select with a **Battery** select (`#batterySelect`), which lists only runnable
  batteries — not archived, no deleted suite, no validation error — as *Name (K suites)*. Its info tip
  lists the suites in run order with their normalized weights under the battery's scheme. A warning
  names the suites whose questions are not all difficulty-assessed, since the start would refuse them.
  *Manage batteries →* opens the Multi-Suite tab.
- **Runs per Suite** — in *Battery* mode the *Number of Runs* field (*Execution* fieldset) is relabeled
  and bounded by `floor(maxMembersPerBattery / K)` instead of the series limit.
- **Wait when the run cap blocks the next run** — shown for every battery; required when the planned
  launches exceed the daily cap.
- **Battery Projection** (*K suites × R = n runs*) — projected wall time and cost as *R* × the sum of
  each suite's recent mean run duration and cost, the remaining daily headroom, and a warning when the
  launches exceed the daily cap (*… so this battery spans at least d days*) or the current headroom.
  A projection row says so when some suite has no completed or priced run to project from.
- **Reuse earlier runs** — a checkbox, off by default and never remembered (reuse is a per-start
  decision). When checked, the launcher calls the reuse preview on every change of battery, model or
  grader field, and the projection reads *Reusing n earlier runs (#ids); launching m.*, or says that
  nothing qualifies and why.

*Start* sends the battery start with the same grader fields a run request carries. The launcher
remembers *Run Target* and the battery across reloads; a remembered battery that is gone, archived or
broken falls back to *Single suite*.

### 4.2 The battery banner

While a battery run is live, stopped or continuable, a banner above the launcher reads *Battery Run #id
(name)* and *Suite s of K (suite name) · round r of R.* (or *Waiting for run cap — …*, *Stopped — reason.
k of K suites completed.*), with **Show Battery Progress**, **Continue** (naming its reason when
there is one), **Re-run under current instrument** after an `InstrumentChanged` stop, and **Cancel
Battery**. It polls through the shared poll ticker and holds the best-effort Web Lock
`overseer-benchmark-live:battery:<id>`, as a series does. The completion chime and desktop
notification fire **once, when the battery run finishes**, never per member.

### 4.3 The battery progress dialog

Full-screen. The title is *Battery Run #id*, the heading *name — revision n*, a progress bar counts
*Slots with a usable result*, and one polite live region carries the stage line. The body is a suite ×
round grid of status chips — *Pending*, *Running*, *Completed with index*, *Index withheld*,
*Instrument changed*, *Failed*, *Superseded* — with a link to each member's own run progress and a
marker on attached members. An *Index withheld* cell says what to do: *Re-run Failed Questions on this
run, then Recompute — or Continue to replace it.* Empty, superseded and index-withheld cells offer
**Attach existing run**, which lists the candidate runs for that slot, newest first, each eligible or
with the reason it is not. The footer holds **Run in Background** (or **Close**), **Cancel Battery**,
**Re-run under Current Instrument**, **Continue** and, once finished, **Open Analysis**.

### 4.4 The Multi-Suite tab

The fourth benchmark tab, after *Multi-Run Analysis*:

1. **Batteries** — one card per battery (name, suites, scheme, revision, runs) with **Edit**,
   **Leaderboard**, **Archive** / **Restore** and **Delete**, and **New Battery**. *Show archived (n)*
   reveals archived batteries.
2. **Battery Runs** — a sortable, filterable table: *Battery*, *Model*, *Status*, *Suites* (k/K),
   *Overall Index* ± half-width, *Overall Speed*, *Total Cost*, *Started*, with actions to view the
   analysis and show the progress dialog.
3. **Analysis of Battery Run #id** — the headline (or *Incomplete (k of K suites)*), *Uncertainty* (both
   components and ν), *Suite profile*, *Weighting sensitivity*, *Leave one suite out*, *Dimensions*,
   *Speed*, *Cost*, *Caveats*, *Excluded members* with their reasons, **Download Report** and
   **Compute** / **Recompute**. *Recompute* is always available, and is called out when the analysis is
   stale **or** lists an excluded member: a run repaired in place keeps its id, so staleness alone
   cannot show the repair.
4. **Leaderboard** — the battery runs sharing one definition hash, **one ranked table per
   comparability class** (M9), each headed *Harness h · scoring method m* and, when several classes
   exist, *· differs in …* naming the keys that separate them. A note says that overlapping intervals
   are not a ranking. Battery runs whose latest analysis is incomplete are listed below, unranked.
   Results of different classes are never placed in one order.
5. **Compare two results** — a *Baseline* and a *Treatment* chosen from the leaderboard: two results of
   one table compare two models, the same model in two tables verifies a change. It shows the
   difference *D* (treatment − baseline), its interval and degrees of freedom, the randomization p and
   method, and per-suite rows with Holm-adjusted p — or *Not comparable.* with the refusal text.

### 4.5 The battery editor

**New Battery** / **Edit**: name, optional description, a reorderable list of every suite with a
checkbox each (checked suites, in list order, are the battery and its run order), and **Weighting**,
a radio group defaulting to *Questions and difficulty*. Under *Custom*, a weight input per suite. A
live preview shows one weight card per checked suite, in run order: *Suite n of N*, the suite's name,
its questions and difficulty mass, its weight under the chosen scheme with a share bar, and, under a
hairline, its weight under the other automatic schemes. The preview is computed in the browser from
the per-suite masses the server sends, so it needs no round trip. A suite whose difficulties are not all assessed is warned
about: its preview uses the fallback weight 50 per unassessed question, and the launcher will refuse
it. Editing an existing battery creates a new revision; existing results keep theirs.

### 4.6 Elsewhere

- **Run History** — a member run's card carries *Battery #id · suite s/K* in its kicker.
- **Delete configuration** (Admin → System Configs) — an active battery run naming the configuration
  blocks the delete, and the impact list counts the stopped battery runs that name it.

---

## 5. API

All routes are under `api/admin/benchmark/batteries` and require the `AdminOnly` policy.

| Method | Route | Purpose |
|---|---|---|
| GET | `` | List batteries, with per-suite question counts, assessment readiness, difficulty mass, a weight preview under every scheme, `BrokenSuiteNames`, `ValidationErrors`, the battery-run count and whether one is active |
| POST | `` | Create (body `{ name, description?, weightingScheme, suiteIds, customWeights? }`). 400 for a blank, over-128-character or taken name, a missing suite, or an invalid definition |
| GET | `{id}` | One battery |
| PUT | `{id}` | Edit. A change to the scheme, suites, order or weights increments `Revision`; the hash is recomputed and moves only when the scheme, the suite set or a weight changed |
| DELETE | `{id}` | Delete. 409 while one of its battery runs is active; otherwise its battery runs keep their snapshots |
| POST | `{id}/archive` | Archive, or restore with the body `{ "archived": false }` |
| GET | `runs` | Battery runs, newest first; optional `?batteryId=` and `?take=` (default 50, at most 200) |
| POST | `runs` | Start (body `StartBenchmarkBatteryRunRequest`: `batteryId`, `runsPerSuite`, `allowCapWait`, `run` — the ordinary run request — and optional `attach`). 202 `{ batteryRunId }`; 409 conflict or unacknowledged same-provider grader; 404 unknown battery; 429 spend guard; 400 invalid request or too many launches (§ 3.3) |
| GET | `runs/active` | The battery run being driven, else the newest live or stopped one; 204 when none |
| GET | `runs/{id}` | Detail: status, stop reason and its text, `Resumable`, the suite × round slot grid with each member's run status, index, origin, usability and reason, the current position, and the latest analysis' headline and staleness |
| POST | `runs/{id}/cancel` | Cancel. 400 for a battery run already `Completed`, `Cancelled` or `Failed` |
| POST | `runs/{id}/resume` | Body `{ mode: "Continue" \| "RerunUnderCurrentInstrument" }`. 202; 409 with `{ instrumentChanged, batteryRunId, changedHashes, message }` when the instrument moved; otherwise mapped as the start |
| POST | `runs/reuse-preview` | For a start request not yet sent: per slot, the run that would be reused, or the reason none qualifies. Writes nothing |
| POST | `runs/{id}/members` | Attach a run to a slot (body `{ suiteIndex, round, runId }`), refused with its reason |
| GET | `runs/{id}/members/candidates?suiteIndex=&round=` | The runs that could fill one slot, newest first, each eligible or with its reason |
| POST | `runs/{id}/analysis` | Compute and persist the analysis; body `{ compareWithBatteryRunId }` adds the paired comparison against that baseline (M7). 400 with the explanation when the members do not form one composite or the comparison is not eligible |
| GET | `runs/{id}/analysis` | The latest analysis, or 204 |
| GET | `runs/{id}/report` | The Markdown report from the latest persisted analysis, `{battery}_{model}_battery_R{n}_{yyyyMMdd_HHmmss}.md`. 400 when there is no analysis yet |
| GET | `leaderboard?definitionSha256=` | The latest analysis of every battery run with that hash, ranked within each comparability class, incomplete ones apart. 400 without a hash |

Elsewhere: `GET /api/admin/benchmark/runs/limits` carries `maxMembersPerBattery`, the run summary DTO
carries `batteryRunId`, `batteryName`, `batterySuitePosition` and `batterySuiteCount`, and the
configuration delete check carries `stoppedBatteryRunCount`.

---

## 6. The Statistical Method

Notation: *K* ≥ 2 suites, indexed *s*; `w_s` the declared weights, `w_s ≥ 0`, `Σ w_s = 1`; `I_s` the
suite's multi-run Intelligence Index; `d_q` a question's difficulty weight; *R* runs per suite.

### M1. Why suites can be combined on one scale

Every suite is graded on the same BARS anchors, mapped to the same 0–100 quality scale, and every
question's difficulty is rated on the same anchored 1–100 bands. The per-suite indices are therefore on
a common, **absolute** scale, and a weighted mean of them is meaningful without normalizing against
other models. That rules out z-scores and rank-based normalization: they would make a model's score
depend on which other models happened to be run.

### M2. The estimand and the weights

The suites are **strata** in the survey-sampling sense (Cochran 1977, ch. 5): every suite is always
included, by design, so the suites are fixed strata rather than a sample of possible suites. The Overall
Index is

```
I = Σ_s w_s · I_s
```

where `I_s` is the multi-run index of suite *s* over its **usable** members
(`BenchmarkGroupStatistics.Compute`). At *R* = 1 it is that one run's index recomputed with fixed
weights and unrounded, so it can differ from the run's stored integer `QualityIndex` by up to 0.5.

The weights are **declared before any result exists** — by the battery definition — and never chosen
afterwards:

| Scheme (UI label) | `w_s` | What the Overall Index estimates |
|---|---|---|
| **Questions and difficulty** (`DifficultyMass`, the default) | `D_s / D`, `D_s = Σ_{q∈s} d_q` | One difficulty-weighted index over every question of every suite, pooled (§ 2) |
| **Questions only** (`ItemCount`) | `n_s / Σ n`, over the suites' exam question counts | Every question counts the same, whatever its difficulty |
| **Equal per suite** (`Equal`) | `1 / K` | Average performance across the declared task areas (a macro average) |
| **Custom** | Declared positive numbers, normalized | For example the production chat's real question mix — the estimand closest to the benchmark's first purpose |

**The difficulty weights are fixed per exam, not per run.** `d_q` is exactly the fixed item weight the
group layer already uses for the suite's index (`BenchmarkGroupItemStatistics.Weight`: the mean of the
answers' assessed difficulty, else the question's `AssessedDifficulty`, else 50, never below 1), which is
what makes the identity of § 2 exact. A question of the suite's **exam** (`BenchmarkRunExam.Build` over
the suite's member runs) that has no scored answer, and so no item row, contributes
`max(1, AssessedDifficulty ?? 50)`. `D_s` therefore counts every exam question, scored or not, and never
depends on how well the model answered.

**When the identity holds only approximately.** When some question has no scored answer — an
assessment that failed, or a co-assessment still unresolved — the suite's index averages over its scored
questions while `D_s` still counts every exam question. `PooledIdentityHolds` is true only when every
suite's `IdentityHolds` is true and every exam question of every suite has an item row; otherwise the
result carries a caveat. A *provider* failure is a different case: it withholds the run's index and
makes the member unusable (M4).

**The exam is checked, not assumed.** The exam is built from the members' answer rows, so it is the whole
suite only when the members have a row for every question. A usable member does — a run that stopped
part-way is `Canceled` or `Failed` and never usable — but when a suite's exam holds fewer questions than
its members' `TotalQuestionCount`, the result carries *exam incomplete for suite s* and
`PooledIdentityHolds` is false. A re-assessment of a suite's difficulties changes the Fundamental key
`SuiteAssessedDifficulties`, so the weights cannot drift between two results that are compared.

**Declared by rule.** Under *Questions and difficulty* and *Questions only* the weights are derived from
the suites rather than written down, so they are declared by **rule**: the rule is part of the
definition and of its hash (M9), and the numbers follow from the exams. The editor previews them from
the suites' current questions; every analysis records the weights it actually used.

**Rejected alternatives.** *Inverse-variance (fixed-effect meta-analytic) weights* move the estimand
towards whichever suites a model answered most consistently, so two models would be scored against
different weightings. *Random-effects meta-analysis* (DerSimonian–Laird, REML, Hartung–Knapp) estimates
performance on a hypothetical population of suites the battery does not sample, and with two to six
suites the between-suite variance cannot be estimated with useful precision.

### M3. Uncertainty: two components, combined in quadrature

As in `ai-benchmark-multi-run.md` § 5.2, the two sources are kept apart and only then combined.

**Item sampling** — *would different questions move this?* The suites are independent strata, so

```
SE_item = √( Σ_s w_s² · SE_item,s² )
```

with `SE_item,s` the suite's `Index.ItemSamplingStandardError`, reused exactly as the group layer
computes it and not recomputed. Its critical value is Student's *t* on the **Welch–Satterthwaite**
effective degrees of freedom (Satterthwaite 1946; Welch 1947):

```
ν = ( Σ_s w_s² SE_s² )²  /  Σ_s ( w_s⁴ SE_s⁴ / (n_s − 1) )
```

with `n_s` the scored items of suite *s*. `ν` is floored to an integer, never below 1, before the
*t* lookup. When every `w_s · SE_s` is zero the formula is 0/0: `SE_item` is then 0, the half-width 0,
and `ν` is reported as `Σ_s (n_s − 1)`. If any suite's `SE_item,s` is null (fewer than three scored
items), `SE_item` is null and the result names the suite that withholds it.

> **A deliberate difference from the single-suite method.** The group layer multiplies its item-sampling
> SE by the normal 1.96; the composite uses `t(ν)`, which is wider at the item counts a suite has. Each
> suite's own interval in the profile therefore stays on 1.96 while the headline is on `t(ν)`, and the
> report says so. This is also why a battery needs at least two suites: with one suite the composite
> would use `t(n − 1)` where the group method uses 1.96, and the two would disagree on the same data.

**Reproducibility** — *would re-running the battery move this?* With round-robin execution every round
*r* is a complete pass of the battery, so a per-round composite exists:

```
I^(r) = Σ_s w_s · I_s^(r)
SE_repro = SD_r( I^(r) ) / √R,   half-width = t(R − 1) · SE_repro
```

This captures correlation **between** suites within one round — a slow provider day hits every suite of
that round — which per-suite standard errors combined in quadrature would miss. The mean of the
`I^(r)` equals `I`, by linearity; a test asserts it.

`I_s^(r)` is computed from the suite's **item rows** for the round's run — `Σ_q d_q · score_q / Σ_q d_q`
over the items that run scored — and not read from the group result's list of per-run indices, which
carries no run ids and omits a run with no scored item, so it cannot be matched to rounds.

- **R < 3:** no reproducibility figure, as in `ai-benchmark-multi-run.md` § 5.3. The interval then
  covers item sampling only, and the report says so.
- **Ragged rounds** — suites with different numbers of usable runs, for example after attaching runs:
  the fallback `SE_repro = √( Σ_s w_s² SE_repro,s² )`, available only when every suite has at least
  three runs. The result names the fallback and that it assumes run-to-run variation is independent
  between suites.

**Combined:**

```
half-width = √( (t(R − 1) · SE_repro)² + (t(ν) · SE_item)² )
```

Bounds are clamped to [0, 100] and marked truncated; half-widths are never clamped
(`ai-benchmark-multi-run.md` § 5.4).

### M4. Completeness: no partial headline

Only usable members (§ 3.2) enter any statistic, and the excluded ones are listed with their reasons,
so a figure never silently rests on a partial run.

The Overall Index is reported **only when every suite has at least one usable member**. Otherwise the
battery run is *Incomplete (k of K suites)*: the per-suite figures are shown, the headline is not.

*Rejected: renormalizing the weights over the finished suites.* That silently changes the estimand and
makes a partial battery look complete. It is the same principle as *an interrupted run carries no
index* (`ai-benchmark.md` § 1, *Runs That Stop Early*).

### M5. Diagnostics beside the headline, never instead of it

- **Suite profile:** per suite `I_s` with its own combined interval, `w_s`, the contribution
  `w_s · I_s`, items and runs; the between-suite SD and range, read as unevenness of the profile.
- **Weighting sensitivity:** `I` under every automatic scheme other than the declared one — for the
  default, *Questions only* and *Equal per suite* — labelled as sensitivity. A reader sees at once
  whether the order of two models depends on the weighting.
- **Leave one suite out:** `I` with each suite omitted and the remaining declared weights renormalized,
  showing how much any one suite drives the result.
- **Dimensions:** `Σ_s v_s · dim_s` for Accuracy, Completeness, Conciseness and Readability, from the
  group layer's per-dimension means. A dimension that is null in any suite is null in the composite,
  naming the suite.
- **Critical-error rate:** `Σ_s v_s · rate_s`, with `rate_s` the mean of the suite's item
  critical-error rates.

Both of the last two use the **count weights** `v_s` of M6, not the declared `w_s`. Each is an
unweighted mean within a suite, so question-count weights make the composite the pooled unweighted mean
over all questions; difficulty-mass weights would mix an unweighted within-suite mean with a
difficulty-weighted between-suite one and estimate nothing nameable.

### M6. Speed and cost

**Count weights.** `v_s` equals the declared `w_s` under *Equal per suite* and *Custom*, and equals
the **question-count** weights `n_s / Σ n` under *Questions and difficulty* and *Questions only* (where
the two coincide).

**Overall Speed Index** `= Σ_s v_s · MeanSpeedIndex_s`. A suite's Speed Index is an equal-weight mean
over answers, because difficulty already enters each answer's speed score through its target latency
(`ai-benchmark.md`, *Aggregation Formulas*); weighting by difficulty again would count it twice. With
question-count weights the Overall Speed Index is the single-suite definition extended — the equal-weight
mean of every answer's speed score — **up to rounding**:

> **The rounding bound.** `MeanSpeedIndex_s` is the mean of the member runs' stored `SpeedIndex` values,
> and each stored value is an integer, the run's own answer-level mean rounded. Each therefore differs
> from the unrounded run mean by at most 0.5, and a weighted mean of such differences is bounded by the
> same 0.5. When every answer carries a speed score and every suite has the same number of usable runs,
> the Overall Speed Index is within **0.5** of the pooled answer-level mean. When some answer has no
> speed score, or the suites have different numbers of runs, the two can differ by more.

The composite is built from the stored per-suite figures on purpose, so that the suite profile adds up
to the headline. Beside it, the analysis reports the pooled answer-level model time and time to first
token (P50, P90, max) over every answer of every suite. The Overall Speed Index is null, naming the
suite, when any suite's `MeanSpeedIndex` is null, and degraded when any suite's speed is degraded or a
speed-affecting key differs between suites (M8).

**Cost:** the total cost of every usable member run; the cost of one battery pass,
`Σ_s MeanCostPerRun_s`; per-role totals; cost per question asked (total ÷ answer rows); and cost per
Overall Index point (pass cost ÷ `I`, null at a non-positive `I`). **An unresolved price makes the
figure unknown, not zero.** The group layer omits a run whose pricing does not resolve and sums the
rest, so a suite whose cost is null, or whose priced run count is below its usable member count, makes
every battery cost figure null, with the suite named. Cost is degraded on a cost-affecting key
difference.

**Elapsed:** the battery's wall clock from start to finish, and the sum of candidate answer time.

### M7. Comparing two battery results

A comparison is allowed only between two battery runs with the **same definition hash** (M9) whose suites
match, suite for suite, on their Fundamental keys (suite, item revisions, assessed difficulties), and
that are exactly one of:

- **(a) A model comparison.** The rule the Model Comparison view already enforces, reused: in every
  suite the two sides agree on every **must-match key** — both Fundamental keys, every Instrument key
  and the candidate prompt options — and differ on at least one **model-axis key** (provider, model,
  thinking level, reasoning mode and summary, service tier, output cap, batching mode, endpoint). A
  differing response style alone is therefore not a model comparison. Differing speed-and-cost keys
  degrade the speed and cost comparison and do not refuse the quality one.
- **(b) A verification of a change.** Every Candidate key matches in every suite, and across the whole
  battery **exactly one Instrument key name** differs, in one suite or in all of them. That key may be
  `CandidateSystemPromptSha256`: a prompt edit moves it in every suite at once, and that is one change,
  not *K*. Two differing key names — a prompt edit shipped together with a harness change, say — are
  refused, as Tier C refuses them.

Anything else is refused, with the differing keys and suites named.

**Per suite**, the existing paired comparison (`BenchmarkGroupStatistics.Compare`) pairs items by
question id and equal item revision; its unweighted mean difference and Wilcoxon test stay the
secondary per-suite figures.

**The composite difference** is built from difficulty-weighted per-suite differences:

```
Δ_q    = treatment item mean − baseline item mean          (paired items only)
ΔI_s   = Σ_q d_q Δ_q / Σ_q d_q
D      = Σ_s w_s · ΔI_s
```

with `w_s` and `d_q` from the baseline's exams. The Fundamental-key requirement makes the two exams
identical, so the weights are too. Because each `I_s` is difficulty-weighted, `D` is exactly the
difference of the two Overall Indices over the paired items, and under the default scheme it equals
`Σ_q d_q Δ_q / D` over all paired questions of the battery. A suite with no paired item makes `D` null,
naming the suite; nothing is renormalized (M4).

**Its standard error and interval:**

```
SE_D = √( Σ_s w_s² · SE_Δ,s² )
SE_Δ,s = √( Σ_q d_q² (Δ_q − ΔI_s)² · m_s / (m_s − 1) ) / Σ_q d_q     (null below m_s = 3)
```

with `m_s` the paired items of suite *s*, and a `t(ν_D)` interval whose Satterthwaite `ν_D` is that of
M3 with `m_s − 1`. `SE_Δ,s` is the weighted estimator of `BenchmarkScoring.QualityIndexStandardError`
applied to the pairs (`Δ_q`, `d_q`); the original centres on a rounded integer index, so the battery
uses an unrounded copy that suits differences, which are small and may be negative. A test pins the two
together on inputs whose weighted mean is a whole number.

**The primary test is a stratified paired randomization (sign-flip) test** on `D` itself (Pitman 1937).
Under the null hypothesis each paired difference is equally likely to have either sign. A resample flips
signs independently within every suite and recomputes

```
D* = Σ_q c_q ε_q Δ_q,     c_q = w_s · d_q / Σ_{q'∈s} d_q',     ε_q = ±1
```

so each difference keeps its weight. With 20 paired items or fewer in total, every assignment is
enumerated and the p-value is exact. Above that, B = 100,000 Monte Carlo resamples are drawn from a
**fixed seed** (20261001), so the result is deterministic and reproducible; then
`p = (1 + #{|D*| ≥ |D|}) / (B + 1)` (Phipson & Smyth 2010), and the Monte Carlo standard error of p is
reported with it. `|D*| ≥ |D|` is evaluated as `|D*| ≥ |D| − 10⁻¹²`, so the observed assignment and its
mirror image always count and a floating-point tie cannot move an exact p. The test examines the
declared weighted estimand directly, which a Wilcoxon test pooled across suites cannot, and needs no
normality assumption.

**Secondary:** each suite's own Wilcoxon signed-rank p, adjusted family-wise with **Holm** (1979) across
the suites that have one. A suite whose differences are all zero has no p and is left out of the family
rather than counted as 1. Per-item comparisons stay exploratory under Benjamini–Hochberg (1995) inside
each suite, as in `ai-benchmark-multi-run.md` § 8.1.

**No all-pairs significance matrix.** Comparisons are pairwise and on demand, so a leaderboard never
manufactures findings by multiplicity.

*Rejected for comparison:* Bradley–Terry scores and mean win rate (as in HELM) depend on the set of
models compared; a two-parameter IRT model needs far more respondents — models — than Overseer will ever
run on one battery to calibrate its item parameters.

### M8. Comparability inside one battery run

The 27 keys of `BenchmarkComparabilityKey.Extract` are reused unchanged and split into three classes:

| Class | Keys | Rule |
|---|---|---|
| **Suite-intrinsic** | Suite id, item revisions, assessed difficulties, game snapshot, `CandidateSystemPromptSha256`, and the board flag inside the candidate prompt options | Differ between suites by design. Must match **within** a suite, and suite for suite between two battery runs being compared |
| **Battery-wide** | Every other Candidate key; the candidate prompt options with the board flag cleared; every other Instrument key (`ToolGuidesSha256`, `KnowledgeBaseHeadSha`, harness version, scoring method version, scoring profile, the three grader configurations, per-question budgets) | Must be identical across **every** member. Any difference refuses the composite, naming the keys and runs |
| **Speed and cost** | Question parallelism, speed calibration, pricing snapshot | A difference degrades the speed or cost composite, as at Tier B |

`CandidateSystemPromptSha256` is suite-intrinsic because the prompt text depends on whether the suite
has a game board. A test enumerates every key `Extract` emits and fails on one that is not classified,
so a key added later cannot fall outside all three classes unnoticed.

That leaves a gap — a prompt edit deployed between two suites of one battery run would pass the
battery-wide check — which one more rule closes for the most part: the prompt depends on the suite only
through the board flag, so **every suite with the same board flag must carry the same
`CandidateSystemPromptSha256`**, and a difference refuses the composite, naming the suites. With exactly
one board suite and one non-board suite there is nothing to compare, and the fingerprint guard of
§ 3.4 covers that case at execution time.

Within each suite, its usable member runs must resolve to Tier A or B
(`ai-benchmark-multi-run.md` § 3.1); a suite below that refuses the composite, naming the suite.

`WikiHeadSha` and `SourceCodeHeadSha` are provenance, not comparability keys (`ai-benchmark.md`,
harness 16), and are not part of this verdict. When they differ across members the result carries a
caveat naming them. (Attaching is stricter about them; see § 3.6.)

### M9. The definition hash and the comparability class

`DefinitionSha256` is SHA-256, as lower-case hex, over a canonical UTF-8 text: the line
`scheme=<int>`, then one line `suiteId:weight` per suite in ascending suite id — the weight rendered
round-trip in the invariant culture, or `-` when there is none — joined by `\n`. The order of the
suites and the battery's name are excluded: reordering changes the run order, not the estimand. Custom
weights are hashed as declared, not normalized, so `2 : 1` and `4 : 2` hash differently — which refuses
a comparison that would have been valid, and never permits an invalid one. Two battery runs are
comparable only on equal hashes, and each battery run keeps its own definition snapshot.

**The definition hash says nothing about the exam or the instrument.** Two battery runs of one
definition may have sat different item revisions, or been graded under different harness versions. Each
complete analysis therefore also records a **comparability class**, `ComparabilityClassSha256`: SHA-256
over, per suite in ascending suite id, the suite id and the must-match signature
(`BenchmarkCrossModelComparability.MustMatchSignature`) its usable members share — both Fundamental keys,
every Instrument key and the candidate prompt options, without the model axis. Two results may stand in
one ranked list only when the definition hash **and** the class agree, which is exactly the precondition
of comparison (a) in M7. The class is null for an incomplete battery run.

### M10. What this method cannot do

Everything in `ai-benchmark-multi-run.md` § 9 still applies: run-to-run variance mixes candidate and
grader stochasticity, and a battery cannot separate them any better than a group can. In addition:

- **Questions that share one game board are not independent**, so a board suite's item-sampling
  standard error is optimistic. That is an inherited single-suite limit the battery cannot correct.
- **The Overall Index generalizes to this battery's suites under these weights**, not to GnollHack
  knowledge at large. With two suites it is a two-area average, not a general intelligence score.
- **The battery measures the chat only as well as its suites resemble the chat's questions.** A
  *Custom*-weighted battery mirroring the production question mix is the closest benchmark estimate of
  chat quality, and it is only as close as that mix is accurate.

---

## 7. The Report

`GET runs/{id}/report` renders the latest **persisted** analysis as Markdown, so it stays reproducible
after a member run is deleted. It is arithmetic only, with no AI-written prose
(`ai-benchmark-multi-run.md` § 11). Its numbered sections are *Battery Manifest* (definition, weights,
hash, conditions, and the chat prompt under test from the first member), *Overall Intelligence Index*
(with both components, ν and the *R* < 3 sentence), *Suite Profile*, *Weighting Sensitivity*,
*Leave-One-Suite-Out*, *Quality Dimensions*, *Speed*, *Cost*, *Token and Tool Usage*, *Paired Battery
Comparison* when the analysis carries one, and *Method and Limits*.

A practical reading order:

1. **Is it complete?** An incomplete battery run has no headline; read which suite is missing and why
   in *Excluded members*.
2. **Read the caveats.** A refused or degraded composite, an approximate pooled identity, the per-suite
   reproducibility fallback and a provenance difference are all stated there.
3. **Read the weighting sensitivity and leave-one-suite-out tables.** If the order of two models flips
   under another scheme, or one suite carries the difference, say so before quoting the headline.
4. **Read both interval components.** If the item-sampling half-width dominates, more rounds will not
   help — more questions will.
5. **Compare with the paired comparison, not with two point estimates**, and state the acceptance
   criterion for a verification before the treatment battery run is made.

---

## 8. References

- Benjamini, Y., & Hochberg, Y. (1995). Controlling the false discovery rate: a practical and powerful
  approach to multiple testing. *Journal of the Royal Statistical Society, Series B*, 57(1).
- Cochran, W. G. (1977). *Sampling Techniques* (3rd ed.), ch. 5, Stratified random sampling. Wiley.
- Holm, S. (1979). A simple sequentially rejective multiple test procedure. *Scandinavian Journal of
  Statistics*, 6(2).
- Miller, E. (2024). *Adding Error Bars to Evals: A Statistical Approach to Language Model Evaluations*.
  arXiv:2411.00640. Its recommendations — paired differences, clustered or stratified standard errors,
  and separating question sampling from run-to-run noise — are the ones this method follows.
- Phipson, B., & Smyth, G. K. (2010). Permutation p-values should never be zero: calculating exact
  p-values when permutations are randomly drawn. *Statistical Applications in Genetics and Molecular
  Biology*, 9(1).
- Pitman, E. J. G. (1937). Significance tests which may be applied to samples from any populations.
  *Supplement to the Journal of the Royal Statistical Society*, 4(1).
- Satterthwaite, F. E. (1946). An approximate distribution of estimates of variance components.
  *Biometrics Bulletin*, 2(6).
- Welch, B. L. (1947). The generalization of "Student's" problem when several different population
  variances are involved. *Biometrika*, 34(1–2).
