# Overseer Chat Consistency Report: Test Model

*Confidential. Unpublished chat consistency results. Review before sharing.*

- **Document:** Report for AI Researchers and Developers
- **Date:** 2026-09-28
- **Analysis:** #7 — Weekly check
- **Model:** Test Model (TestProvider, test-model-1, thinking level high)
- **Baseline period:** 2026-09-01 00:00 UTC to 2026-09-08 00:00 UTC, 2 runs
- **Comparison period:** 2026-09-15 00:00 UTC to 2026-09-22 00:00 UTC, 2 runs
- **Hours:** weekdays 04–12 UTC
- **Suites:** Core suite
- **Control models:** Control Model Two
- **Protocol:** V1

## Overall verdict

- **Verdict:** The chat changed
- **Answer streaming rate (P3) not computable:** No telemetry run in the baseline.
- **Sample:** Met: at least 2 runs on 2 days per period and 20 paired items
- **Hours:** every result holds for weekdays 04–12 UTC only, the hours both periods share
- **Controls:** Control Model Two, run on the same suites in both periods; 1 missing-control note
- **Time strata:** 2 strata (weekdays 04–08 UTC, weekdays 08–12 UTC); time of day not assessable: the common hours do not include both US business hours and other hours
- **The analysis's headline:** Overseer chat with Test Model: quality degraded; speed equivalent within weekdays 04–12 UTC

## The result in one sentence

Quality of the Overseer chat with Test Model degraded by −4.2 index points within weekdays 04–12 UTC.

## Question and design

The analysis asks whether the Overseer chat with Test Model changed between a baseline and a comparison period. It compares matched runs of the same suites under V1.

## Runs, coverage and scope

The baseline holds 2 runs and the comparison 2 runs, within weekdays 04–12 UTC.

## Overseer events

| When (UTC) | Run | What changed |
|---|---|---|
| 2026-09-11 00:00 | run #20 | Game snapshot: off → on |
| 2026-09-12 00:00 | run #31 | Re-indexed: GnollHack wiki (812 → 815 files) (in the runs of Control Model Two) |

Two Overseer events fall between the periods: candidate prompt options changed on 2026-09-11 (run #20) and corpus index changed on 2026-09-12 (run #31).

## Results by endpoint

| Endpoint | Change (95 % interval) | Margin | Verdict and grade | Smallest detectable |
|---|---|---|---|---|
| Quality (P1) | −4.2 index points (−6.0 to −3.1 index points) | ±3 index points | Degraded, Indicated | ±2.5 index points |
| Time to first answer text (P2) | +2.0 % (−4.9 to +9.4 %) | ±15 % | Equivalent, Established | ±8.3 % |
| Cost per answer (P5) | +3.1 % (−3.9 to +10.5 %) | ±10 % | Inconclusive, Not established | ±12.7 % |

- **Quality (P1):** 90 % interval −5.6 to −3.4 index points; Holm-adjusted p 0.016; a comparison run lacks call telemetry
- **Time to first answer text (P2):** 90 % interval −3.9 to +8.3 %; Holm-adjusted p 1.000
- **Cost per answer (P5):** 90 % interval −3.0 to +9.4 %; Holm-adjusted p 0.800

- **Answer streaming rate (P3), not computable:** No telemetry run in the baseline.

*Each change is the comparison period against the baseline period: a difference in the endpoint's unit, or a change in percent for a ratio, read against the endpoint's equivalence margin.*

Quality degraded by −4.2 index points, with an interval of −6.0 to −3.1 index points, graded Indicated: a comparison run lacks call telemetry. Time to first answer text is equivalent, graded Established.

## Attribution

| Endpoints | Side | Grade |
|---|---|---|
| Quality (P1) | the provider's side | Indicated |
| Cost per answer (P5) | our change | Not established |

- **Undeclared change of the model:** Quality degraded for the model under test while Control Model Two held steady.

- **Control Model Two (OtherProvider):** 2 runs in the baseline and comparison; difference in differences Quality (P1) −4.0 index points (−6.1 to −2.0 index points)

*A control model's chat was run on the same suites in both periods. The difference in differences is the change of the model under test minus the control's own change.*

- **Comparison period, no control:** Core suite: Run Control Model Two on Core suite under the build of run #21.

The analysis places the quality change on the provider's side, graded Indicated under R9 undeclared-change.

## Robustness

The analysis ran no robustness check on a computed endpoint.

The analysis ran 0 checks, and 0 of 0 checks failed.

## Limitations

- Only weekday mornings were sampled.

The analysis records one limitation: Only weekday mornings were sampled.

## Reproducibility

- **Analysis:** #7 — Weekly check
- **Analysis code version:** 3
- **Protocol:** V1; the published protocol, without overrides; alpha 0.05
- **Common grader:** none. No common grader covered every compared run, so quality compares each run's own grades.
- **Price card:** not available
- **Baseline:** runs #10 and #11
- **Comparison:** runs #20 and #21
- **Control runs:** #30, #31
- **Report format version:** 1

Analysis #7 used code version 3 under V1.

## How to read this

- **What was measured.** The Overseer chat with Test Model: the model together with the chat system prompt, the tools, the knowledge corpora and the agent loop. The same benchmark suites were run in a baseline period and a later comparison period, and each endpoint compares the two.
- **The endpoints.** Quality (P1), Time to first answer text (P2), Answer streaming rate (P3) and Cost per answer (P5). Each estimate comes with its 95 % interval.
- **Verdicts.** Each verdict is read against the endpoint's equivalence margin, set by the protocol: *equivalent* when the 90 % interval lies inside the margin; *degraded* or *improved* (*more work* or *less work* for an endpoint that counts work) when the 95 % interval lies wholly beyond the margin on one side; *changed, negligible* when the 95 % interval excludes zero but lies inside the margin; *inconclusive* otherwise, when the runs cannot tell a change from no change. The smallest detectable change, ± the minimum detectable effect, is the smallest change these runs could have detected.
- **Evidence grades.** *Established*: a decisive verdict, every robustness check passed, the minimum sample met, telemetry-grade data and no pooling across a measurement change. *Indicated*: a decisive verdict with a failed robustness check, legacy data, pooling across a measurement change or a sample below the minimum. *Not established*: inconclusive, or nothing to support a claim. Only an Established result is fit to publish.
- **Hours.** Every result holds for weekdays 04–12 UTC only, the hours both periods share. It says nothing about the hours outside them.
- **Attribution.** The total change is reported first; the attribution then grades where it came from: our change, our infrastructure, the provider's side, or undetermined. An attribution never goes beyond its grade.
- **Control models.** A change the model under test shares with a control model is told apart from a change of the model under test alone by the difference in differences.
- **What is never inferred.** The analysis measures what changed. It never infers anyone's intent, and it names no mechanism unless the provider has confirmed one.

## Evaluation terms

- **Distillation / training prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.
- **Third-party model content:** Outputs generated by **Test Model** (TestProvider) through the Overseer chat, measured by the Overseer benchmark and described in this document by **Writer One** (Anthropic), are third-party content evaluated solely to check whether the Overseer chat with this model stays consistent over time, and for operational model selection.

---

*Document ID 41 · format version 1 · created 2026-09-28 10:42 UTC · writer Writer One (Anthropic, writer-1) · disclosure Detailed · controls named*

*Figures and tables were computed by Overseer from the saved chat consistency analysis. The prose was written by Writer One from those figures and checked automatically for structure, permitted figures, word limits, control-model names, hype words, spelling, readable text and the claim rules on change, cause, intent, mechanism, public claims and hours; the checks do not verify the prose's interpretations.*
