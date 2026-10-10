# Overseer Chat Consistency Report: Test Model

*Confidential. Unpublished chat consistency results. Review before sharing.*

- **Document:** Executive Summary
- **Date:** 2026-09-28
- **Analysis:** #7
- **Model:** Test Model (TestProvider, test-model-1, thinking level high)
- **Baseline period:** 2026-09-01 00:00 UTC to 2026-09-08 00:00 UTC, 2 runs
- **Comparison period:** from 2026-09-15 00:00 UTC to 2026-09-22 00:00 UTC, 2 runs
- **Hours:** weekdays 04–12 UTC
- **Suites:** Core suite
- **Control models:** 1 control model (A), identity withheld
- **Protocol:** V1

## Overall verdict

- **Verdict:** The chat changed
- **Answer streaming rate not computable:** No telemetry run in the baseline.
- **Sample:** Met: at least 2 runs on 2 days per period and 20 paired items
- **Hours:** every result holds for weekdays 04–12 UTC only, the hours both periods share
- **Controls:** 1 control model (A), identity withheld, run on the same suites in both periods; 1 missing-control note

## The result in one sentence

The Overseer chat with Test Model is Degraded: quality degraded (indicated); time to first answer text equivalent (established); answer streaming rate not computable; cost per answer inconclusive (not established) within weekdays 04–12 UTC.

## Verdicts by endpoint

| Measure | Result | What it means |
|---|---|---|
| Quality | Degraded: −4.2 index points | Worse than before, beyond the margin of ±3 index points; graded Indicated |
| Time to first answer text | Equivalent: +2.0 % | The same as before, within the margin of ±15 %; graded Established |
| Cost per answer | Inconclusive: +3.1 % | Undecided: these runs could detect only a change of ±12.7 % or more |

*Not computable: Answer streaming rate; the overall verdict says why.*

**Where the change came from.** The analysis attributes Quality to the provider's side, graded Indicated. The analysis attributes Cost per answer to our change, graded Not established.

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

## Is the Overseer chat with this model as good as before?

Answer quality degraded by −4.2 index points within weekdays 04–12 UTC. Waiting time is equivalent.

## What changed for players

Players got answers of lower quality, by −4.2 index points. Cost per answer is inconclusive at +3.1 %, and the runs could only detect a change of ±12.7 %.

## Our changes and their effect

Between the periods, Overseer was updated once: E1, 2026-09-11 00:00 UTC — game snapshot: off → on. The control models' runs show one more update.

The Overseer edited its tool guides on 2026-09-11 00:00 UTC. Their effect is not established, graded Not established.

## Provider-side changes

The analysis places the quality change on the provider's side, graded Indicated.

## Confidence and scope

The result rests on 4 runs and 80 answers, and time of day is not assessable: the common hours do not include both US business hours and other hours.

## Next runs

The analysis suggests one more run: Repeat the comparison runs during US business hours.

## How to read this

- **What was measured.** The Overseer chat with Test Model: the model together with the chat system prompt, the tools, the knowledge corpora and the agent loop, run on the same benchmark suites in a baseline period and a later comparison period.
- **The five measures.** *Quality* is the Intelligence Index of the graded answers; *time to first answer text* is how long a player waits before the answer starts; *answer streaming rate* is how fast it then appears; *work per turn* is the output tokens per answer; *cost per question* is US dollars per question at one price card.
- **Grades.** *Established* means fit to publish; *Indicated*, a decisive result with a caveat; *Not established*, no finding.
- **Verdicts.** *Equivalent* means the same as before within the endpoint's margin; *degraded* or *improved* a change beyond it; *inconclusive* that these runs cannot tell a change from no change, and the smallest detectable change says how large a change they could have missed. Only an Established result is fit to publish.
- **Hours.** Every result holds for weekdays 04–12 UTC only, the hours both periods share. It says nothing about the hours outside them.

## Evaluation terms

- **Distillation / training prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.
- **Third-party model content:** Outputs generated by **Test Model** (TestProvider) through the Overseer chat, measured by the Overseer benchmark and described in this document by **Writer One** (Anthropic), are third-party content evaluated solely to check whether the Overseer chat with this model stays consistent over time, and for operational model selection.

---

*Document ID 41 · format version 2 · created 2026-09-28 10:42 UTC · writer Writer One (Anthropic, writer-1) · disclosure Detailed · controls anonymized*

*Figures and tables were computed by Overseer from the saved chat consistency analysis. The prose was written by Writer One from those figures and checked automatically for structure, permitted figures, word limits, control-model names, hype words, spelling, readable text and the claim rules on change, cause, intent, mechanism, public claims and hours; the checks do not verify the prose's interpretations.*
