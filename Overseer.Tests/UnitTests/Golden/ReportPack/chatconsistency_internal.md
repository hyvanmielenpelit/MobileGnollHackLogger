# Overseer Chat Consistency Report: Test Model

*INTERNAL — unpublished chat consistency results. Do not share outside the Overseer team.*

- **Document:** Internal Improvement Brief
- **Date:** 2026-09-28
- **Analysis:** #7
- **Model:** Test Model (TestProvider, test-model-1, thinking level high)
- **Baseline period:** 2026-09-01 00:00 UTC to 2026-09-08 00:00 UTC, 2 runs
- **Comparison period:** 2026-09-15 00:00 UTC to 2026-09-22 00:00 UTC, 2 runs
- **Hours:** weekdays 04–12 UTC
- **Suites:** Core suite
- **Control models:** 1 control model (A), identity withheld
- **Protocol:** V1

## Overall verdict

- **Verdict:** The chat changed
- **Answer streaming rate (P3) not computable:** No telemetry run in the baseline.
- **Sample:** Met: at least 2 runs on 2 days per period and 20 paired items
- **Hours:** every result holds for weekdays 04–12 UTC only, the hours both periods share
- **Controls:** 1 control model (A), identity withheld, run on the same suites in both periods; 1 missing-control note
- **Time strata:** 2 strata (weekdays 04–08 UTC, weekdays 08–12 UTC); time of day not assessable: the common hours do not include both US business hours and other hours

## The result in one sentence

Quality of the Overseer chat with Test Model degraded by −4.2 index points within weekdays 04–12 UTC.

## Verdicts by endpoint

| Endpoint | Change (95 % interval) | Margin | Verdict and grade | Smallest detectable |
|---|---|---|---|---|
| Quality (P1) | −4.2 index points (−6.0 to −3.1 index points) | ±3 index points | Degraded, Indicated | ±2.5 index points |
| Time to first answer text (P2) | +2.0 % (−4.9 to +9.4 %) | ±15 % | Equivalent, Established | ±8.3 % |
| Cost per answer (P5) | +3.1 % (−3.9 to +10.5 %) | ±10 % | Inconclusive, Not established | ±12.7 % |

- **Quality (P1):** 90 % interval −5.6 to −3.4 index points; a comparison run lacks call telemetry
- **Time to first answer text (P2):** 90 % interval −3.9 to +8.3 %
- **Cost per answer (P5):** 90 % interval −3.0 to +9.4 %

- **Answer streaming rate (P3), not computable:** No telemetry run in the baseline.

*Each change is the comparison period against the baseline period: a difference in the endpoint's unit, or a change in percent for a ratio, read against the endpoint's equivalence margin.*

**Where the chat stands**

| Measure | Baseline | Comparison |
|---|---|---|
| Quality | mean score 82.4 points | mean score 78.1 points |
| Time to first answer text | median 39.3 s | median 40.1 s |
| Answer streaming rate | median 61.2 tokens/s | median 59.8 tokens/s |
| Work per turn (output tokens per answer) | mean 9,044 output tokens per answer | mean 8,233 output tokens per answer |
| Cost per question | mean $0.0071 per question | mean $0.0066 per question |
| Failed answers | 0 of 40 answers | 1 of 40 answers |

*Descriptive levels of each period, not a comparison: the verdicts above say what changed.*

## Control models

- **Model A:** 2 runs in the baseline and comparison; difference in differences Quality (P1) −4.0 index points (−6.1 to −2.0 index points)

*A control model's chat was run on the same suites in both periods. The difference in differences is the change of the model under test minus the control's own change.*

| Period | Run (suite) | What to do |
|---|---|---|
| Comparison | Run #21 (Core suite) | Run Model A on Core suite under the build of run #21. |

## Where the change came from

| Endpoints | Side | Grade |
|---|---|---|
| Quality (P1) | the provider's side | Indicated |
| Cost per answer (P5) | our change | Not established |

- **Undeclared change of the model:** Quality degraded for the model under test while Model A held steady.

## Findings for the chat

Check the chat tools first: quality degraded by −4.2 index points within weekdays 04–12 UTC.

## Our changes that helped or hurt

| Event | When (UTC) | Run | What changed |
|---|---|---|---|
| E1 | 2026-09-11 00:00 | run #20 | Game snapshot: off → on |
| E2 | 2026-09-12 00:00 | run #31 | Re-indexed: GnollHack wiki (812 → 815 files) (in the runs of Model A) |

The effect of candidate prompt options changed on 2026-09-11 (run #20) is not established, graded Not established.

## Infrastructure issues

Our own waits took 3.9 % of model time in the comparison period.

## Next runs

Run the suggested follow-up: Repeat the comparison runs during US business hours.

## Actions

Prepare a Provider Issue Report on Undeclared change of the model, graded Indicated.

## Reproducibility

- **Analysis:** #7
- **Analysis code version:** 6
- **Protocol:** V1; the published protocol, without overrides; alpha 0.05
- **Common grader:** none. No common grader covered every compared run, so quality compares each run's own grades.
- **Price card:** not available
- **Baseline:** runs #10 and #11
- **Comparison:** runs #20 and #21
- **Control runs:** #30, #31
- **Report format version:** 2

---

*Document ID 41 · format version 2 · created 2026-09-28 10:42 UTC · writer Writer One (Anthropic, writer-1) · disclosure Full · controls anonymized*

*Figures and tables were computed by Overseer from the saved chat consistency analysis. The prose was written by Writer One from those figures and checked automatically for structure, permitted figures, word limits, control-model names, hype words, spelling, readable text and the claim rules on change, cause, intent, mechanism, public claims and hours; the checks do not verify the prose's interpretations.*
