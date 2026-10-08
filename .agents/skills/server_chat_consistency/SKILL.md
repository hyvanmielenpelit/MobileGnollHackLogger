---
name: server_chat_consistency
description: >-
  GnollBench Chat Consistency in Overseer: whether the Overseer chat stays as good, fast and cheap over
  time with a given model, judged from GnollBench runs. Read before running or interpreting a chat
  consistency analysis, writing about the chat or a model getting slower, faster, worse or better,
  planning checkpoint or control runs, or classifying a new HarnessVersion in HarnessImpactLedger.
---

# Chat Consistency: Is the Overseer Chat Still as Good, Fast and Cheap?

GnollBench is the user's name for the Overseer AI benchmark.

The method, in full, is `docs/overseer/ai-benchmark-chat-consistency.md`. This skill is what an agent
must not get wrong when it runs, reads or writes about an analysis.

## 1. What It Answers

Whether the **Overseer chat with one model** — model, system prompt, tools and guides, corpora and
agent loop together — changed between a baseline and a comparison period, on five pre-declared
endpoints (Protocol V1):

| Id | Endpoint | Margin |
|----|----------|--------|
| P1 | Quality | ±3 index points |
| P2 | Time to first answer text (net of our own waits) | ±15 % |
| P3 | Answer streaming rate | ±10 % |
| P4 | Work per turn (output tokens; *more* / *less work*, never better or worse) | ±15 % |
| P5 | Cost per question (one price card for every run) | ±10 % |

α 0.05 with Holm across P1–P5; Benjamini–Hochberg within each secondary family. The evidence is
**GnollBench runs only**: only a rubric-graded run can show a change of quality.

P3 reads `CallTelemetryMeasures.AnswerStreamingRate`, which from harness 54 has no value for an answer
whose final call's decode span is under 500 ms or whose rate is over 1,000 tokens/s — visible text that
arrived in one burst after thinking, so the figure would measure delivery, not decoding. Analysis code
version **3** (`ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion`) applies those bounds, and the
streaming-rate caveat counts the delivered answers left without a rate; an analysis saved under version
2 keeps its stored result, so re-analyze before comparing a P3 verdict across the two.

## 2. Verdicts and Grades

- **Verdicts** (Lakens): *degraded* / *improved* (Holm p < α and the 95 % interval wholly beyond the
  margin), *changed, negligible* (95 % interval excludes 0 inside the margin), *equivalent* (90 %
  interval inside the margin), *inconclusive*; or *not computable* with its reason.
- **Grades**: **Established** needs a decisive verdict, every robustness check passed, the minimum
  sample, telemetry data and no relaxed pooling; **Indicated** is a decisive verdict missing any of
  those; **Not established** is inconclusive or not computable.
- **Minimum sample**: P1, P4, P5 — at least 2 runs per period on at least 2 UTC days and at least 20
  paired items; P2, P3 — at least 3 runs per period in one common time stratum. Fewer: at most
  Indicated.
- An **inconclusive** endpoint is never "no change": cite its minimum detectable effect.
- P1 on native grades with neither a common grader nor an anchor fails *Grader stability*, so it is at
  most Indicated.

## 3. Measurement Changes Against Overseer Changes

- A **measurement change** (graders, scoring, how time or tokens are counted, prices) is bridged or
  **segmented**: runs across it are not compared on the axis it breaks. A common grader bridges grading;
  one price card bridges pricing; candidate timing, call telemetry and candidate accounting are never
  bridged. Relaxed pooling pools across a boundary and caps grades at Indicated.
- An **Overseer change** (system prompt, tool guides, corpora, prompt options, budgets, a harness
  version with `CandidateInput`) **never excludes data**. It is a dated **event** that control runs
  attribute.

## 4. Sides, and Why Control Runs Matter

Attribution (rules R1–R11) names a **side**: *ours*, *provider*, *infrastructure* or *undetermined*.
A control run is another model's run under the **same Overseer build** (equal instrument fingerprint)
on the same suite. With an event in the span:

- the control moved the same way and the DiD includes 0 → R1, **ours**;
- no control qualifies → R2, **not attributable** — the change could be ours or the provider's.

A provider-side row needs the endpoint isolated: no event in the span, or a DiD separating the target.
So **after any Overseer change worth attributing, one control run of another provider under the new
build** is what turns R2 into an answer.

## 5. The Scope: Hours

Runs sample the chat only at the hours they ran. Every verdict holds **within the common time strata**
(4-hour UTC blocks, weekday or weekend) both periods sampled, and every headline ends *"within
\<scope\>"*. *Load-independent* needs a common block inside US business hours (weekdays 14–22 UTC) and
one outside them. Never write "slower" without the scope, and never generalize to hours no run covered.

## 6. Never Claim Intent

The analysis names the side a change fits, **never a mechanism or an intent**. No finding can say a
provider made a model slower or worse *deliberately*, or *why*. The strongest speed finding is R7, a
persistent serving change within the sampled hours. A mechanism appears only as a verbatim
*ProviderConfirmedCause* annotation with its date and source. The report validator (C2) drops intent
and mechanism words without one.

## 7. GnollBench Is Not a Monitoring Service

Runs are made by hand on the development computer from a Visual Studio session. There is no scheduler,
no background monitoring and no production probe; the analysis reads stored runs and spends nothing. A
verdict is only as current as the last run someone made — never describe it as live, continuous or
production-wide. Runs and common-grader re-grades spend; each shows its estimate first and starts only
when the operator confirms.

## 8. Detection and Confirmation

Choosing periods after looking at the timeline is **detection**. Confirm a change on data that did not
exist when it was found: the wizard's **Confirm on later data** preset takes the last saved analysis's
baseline against the runs after it was saved. Change-point detection (`ChatConsistencyStatistics.Pelt`)
exists but is not wired into the analysis or the timeline.

**Leaving runs out after looking at the timeline is detection too.** Step 1 of the wizard lets the
operator mark a first and a last run and leave runs out; nothing stops dropping a run *because* it is an
outlier. So every analysis saved since `AnalysisCodeVersion` 2 records its **run selection** (the step-1
dates, the marks, the left-out runs) and every usable run of the model inside the periods that was not
analyzed, with why: *left out in step 1*, *outside the step-1 dates*, *before the first run*, *after the
last run* or *not selected in step 4*. When any run is unanalyzed, the result carries a `runSelection`
data-quality note naming them and a limitation (*"The operator chose the runs: … leaving runs out after
looking at the timeline can bias them."*), and the report documents state both. **A reader citing a
verdict must not drop that note or limitation**: a verdict on a hand-picked subset holds for the
analyzed runs only, and is confirmed only on later data. Analyses saved under code version 1 have no
selection record (`recorded: false`).

## 9. Classifying a Harness Bump

`HarnessImpactLedgerTests` fails until **every** version up to `BenchmarkAssessmentPrompt.HarnessVersion`
has an entry in `Overseer/Services/ChatConsistency/HarnessImpactLedger.cs` and the last entry is the
current version. An unclassified version resolves to `Unclassified` — every flag except ReportingOnly —
so until it is entered it segments everything.

Classify the new version from its changelog on `HarnessVersion` and `docs/overseer/ai-benchmark.md`:

| Flag | Set it when the version changed |
|------|---------------------------------|
| `CandidateInput` | anything the candidate is sent or allowed: system prompt delivery, tool output or a tool guide, budgets, timeouts, request parameters |
| `CandidateTiming` | how candidate time is measured |
| `Grading` | any grader prompt, rule, role, routing or detector |
| `Scoring` | how grades become scores (a scoring method, a score rule) |
| `CandidateAccounting` | how candidate tokens or cost are counted from what the provider reported |
| `ReportingOnly` | reports, UI or storage alone — never combined with another flag |

- **Conservatively**: a version that mixes kinds carries every flag that applies; a tool-output change
  is `CandidateInput` even when no guide moved; when the changelog cannot tell, add the measurement
  flags and set `IsConservative = true`. A missing flag makes runs comparable that are not; an extra one
  only costs a segment.
- **Same-stamp cases**: when the harness changed without the stamp moving, so runs carrying one stamp
  differ, set `SameStampImpact` on that entry (harness 12 and 18 have one). Equal stamps then still
  segment on those flags.
- Write the one-line `Summary` naming the changes the classification rests on, in the style of the
  existing entries.
- `CallTelemetryVersion` is not a harness version; a telemetry change does not bump the ledger.

## 10. Pointers

- Method: `docs/overseer/ai-benchmark-chat-consistency.md`; per-call telemetry and the run report's
  Timing Decomposition: `docs/overseer/ai-benchmark.md` § 5; report documents: `ai-benchmark-report-pack.md` § 16.
- Code: `Overseer/Services/ChatConsistency/` — `ChatConsistencyProtocol`, `ChatConsistencyAnalysisService`,
  `ChatConsistencyStatistics`, `ChatConsistencyComparability`, `ChatConsistencyAttribution`,
  `ChatConsistencyEvidenceBuilder`, `ChatConsistencyRegradeService`, `HarnessImpactLedger`;
  `Overseer/Services/Telemetry/`; `Overseer/Controllers/AdminChatConsistencyController.cs`; the client
  in `Overseer/ClientApp/src/app/admin/benchmark/chat-consistency-tab/`.
- Planning the runs: `server_benchmark_runbook` § *Checkpoint and control runs for chat consistency*.
