---
name: server_benchmark_to_chat_transfer
description: >-
  Mandatory method for turning Overseer AI benchmark (GnollBench) findings into improvements
  to the production chat agent: quality, latency, cost. Covers the priority order (the chat
  first, the benchmark second, model results third); that the benchmark grades the
  production chat system prompt; triage of every finding into harness, suite, chat-transferable
  or corpus / environment defect; the configuration-parity check; what the benchmark does not
  measure; the route into the tool-layer skills; the evidence bar, version boundaries and
  panel reading; the ladder of safe changes from knowledge-base article to prompt edit;
  anti-overfitting; verification and rollback; the required Chat Transfer output and Developer
  Runbook; and the record: the recent entries, the standing cautions, and the ledgers of
  changes that reached the chat and of verifier failures in references/. Read before analyzing any
  benchmark run report, diagnostics or assessment, and before writing any implementation plan
  derived from one.
---

# Benchmark to Chat Transfer: Turning Benchmark Findings into Chat Improvements

GnollBench is the user's name for the Overseer AI benchmark.

> *Naming: the Second Reader (single-assessor runs) and Reference Reader (panel runs) were called
> the "second opinion" before 2026-09-27. Code identifiers, database columns, API fields, the
> `SecondOpinionConfiguration` key and reports exported before that date keep `SecondOpinion*` and
> the old report headings; re-exporting a run's report produces the new ones. Which models and
> effort each grading role should use is in `docs/overseer/ai-benchmark.md` § 3 *Choosing grader
> models and effort*.*

This skill defines the mandatory protocol for translating empirical findings from the Overseer AI Intelligence Benchmark into concrete improvements to the production chat agent — higher answer quality, reduced latency, and lower token costs — without overfitting the assistant to the benchmark suite.

---

## What the Benchmark Is For

AI benchmarking in Overseer has a fixed order of importance. Every analysis, plan and proposed run follows it.

1. **Improving the main AI chat of the Overseer — most important.** The benchmark runs the production chat prompt (§ 1) and the production tool registry (§ 4a), which makes it the instrument for **debugging and fixing the chat**: that the system and its tools work correctly, that the models perform at maximum efficiency, and that there are no bugs or other problems — and for improving the chat's quality in every respect. A tool that returns the wrong thing, a prompt instruction that misleads, a corpus that was not indexed, a request that reached the provider without its system prompt: these are what a run is read for first.
2. **Improving the AI benchmarking system itself — second.** The harness, the graders, the suites and the reports must work rigorously and correctly, because everything learned about the chat and about the models is only as sound as the instrument it was read from.
3. **The benchmark results themselves — third.** How well different models perform as the Overseer assistant, on intelligence, speed and cost.

**Two uses follow from this:** (1) debugging and fixing the main AI chat and improving its quality in all aspects, and (2) benchmarking AI models so that we know which models to use in the chat. **Both serve one aim: the best possible AI assistant experience for regular users of the Overseer.**

What the order decides in practice:

- **An analysis leads with the chat.** Its summary states, in this order: what the run shows about the chat system and its tools; what it shows about the benchmarking system; and only then how the candidate scored. A run whose score is unusable — cancelled, prompt-less, graded on a broken rubric — can still be a first-rate run for purposes 1 and 2, and is analysed as one.
- **A defect in the chat system or its tools outranks everything else in the round**, whatever the score was. It is fixed by the round's plan, or the plan says why it cannot be.
- **A model ranking is taken only on a system believed to be sound.** A ranking made while a known chat or harness defect is open measures the defect. The registry's prompt-less OpenAI runs (§ 11, the 2026-09-17 correction) are the standing example: seven runs whose indexes described a wire defect rather than a model.
- **A higher score is never the goal.** A score is evidence about the assistant a user gets; § 8's anti-overfitting rules exist because the two can be pulled apart.
- **The Developer Runbook's runs follow the same order** ([`server_benchmark_runbook`](../server_benchmark_runbook/SKILL.md) § 4).

Set on 2026-09-19 by the user's instruction.

---

## 1. The Structural Fact

The benchmark does not use a bespoke question-answering prompt. It builds the candidate's system
prompt from the production chat builder, through a snapshotted configuration record. Cite code by
symbol in this skill; line numbers drift.

1. `BenchmarkService.CandidatePromptOptionsFor` builds a `BenchmarkCandidatePromptOptions` record
   from the run's `verboseMode`, its Source Code References setting and whether the suite has a
   board.
2. The launch stores it as `BenchmarkRun.CandidatePromptOptionsJson`, with
   `CandidatePromptSourceUsed` set to `"ChatService.BuildSystemPrompt"`.
3. `BenchmarkCandidatePromptOptions.BuildSystemPrompt` passes the record, with the tested
   configuration's `ParallelExecutionMode`, to `ChatService.BuildSystemPrompt`, the production
   builder, which delegates to `ChatService.BuildSegmentedSystemPrompt` (frozen, session-stable and
   volatile segments).
4. `BenchmarkService.PopulateInstrumentFingerprint` hashes the built prompt and records the
   instrument fingerprints that § 6 describes.

`ChatService.BuildSystemPrompt` takes these parameters:

- `wikiContext`
- `spoilerFreeMode`
- `verboseMode`
- `isGameOn`
- `developerMode`
- `overseerMode`
- `hasGameSnapshot`
- `hasMessageHistory`
- `clientSettings`
- `enableToolUse`
- `enableWebSearch`
- `allowSourceCodeReferences`
- `enableSubAgents` (default `false`)
- `parallelMode` (default `ParallelExecutionMode.Enabled`)

**The benchmark grades the production chat system prompt**, not an artificial or simplified test prompt. Every quality score, completeness deduction, hallucination finding, tool routing observation, and latency measurement in a benchmark report is an empirical measurement of what a real human player receives when asking assistance from the Overseer.

Transferring findings back to chat is therefore not an analogy or an extrapolation; it is the direct analysis of production prompt performance under test conditions.

Three qualifications that the phrase "verbatim production prompt" hides, and that every analysis must hold in view:

- **Every call site routes through `BenchmarkCandidatePromptOptions`**: a new run, the
  failed-question re-run and the single-answer re-run (both rebuild the record with
  `BenchmarkCandidatePromptOptions.FromJson` from the stored `CandidatePromptOptionsJson`), and the
  prompt preview, which builds it as a launch does. That record is the authoritative statement of
  what was graded; read it, not this section, when attributing a result.
- **"Verbatim" is true of the builder, not of every input.** `wikiContext` is always empty in a benchmark run, while live chat pre-injects wiki articles. The prompt-building *code* is identical; the prompt *text* a benchmark candidate sees is not the text a live user's model sees. See § 4.
- **"Grades the production prompt" was true of the builder, not of the wire, before harness 29.** `CandidatePromptOptionsJson` records the prompt the harness *built*; nothing recorded what a provider's request body actually *contained*, until `BenchmarkCandidateRequestProbe` began checking the serialized request itself (§ 4a below, and `docs/overseer/ai-benchmark.md` § *Harness Version 29 Updates*). Before that round a Google or Anthropic candidate on a snapshot suite was silently graded with no board, and an OpenAI candidate with no system prompt at all, behind a report that read `HasGameSnapshot: true` as if nothing were wrong.

---

## 2. The Mandatory Triage

Every finding produced by a benchmark analysis must be triaged into exactly one of four categories:

1. **Harness Defect**: The testing instrument itself is broken or flawed (e.g. grading biases, parser failures, broken reporting formulas, unhandled timeouts, missing backfills, report prose that misstates what the prompt says).
2. **Suite Defect**: The benchmark question, rubric ground truth, or difficulty rating is incorrect, ambiguous, or outdated. A suite repair to a default suite is complete only when its file under `Overseer/Data/DefaultSuites/` carries the same text — see [`server_rubric_handoff`](../server_rubric_handoff/SKILL.md) § 3a; a custom suite is repaired in the database only.
3. **Chat-Transferable**: The defect or observation reflects authentic behavior that real users experience in chat (e.g. tool routing inefficiencies, prompt-induced brevity vs. completeness conflicts, knowledge gaps, latency inflation).
4. **Corpus / Environment Defect**: the corpus a tool reads was missing, stale, outside the indexed scope, excluded by a size limit, or still indexing on the machine that ran the benchmark. The instrument is sound and the suite is sound; the *data the tool reads* was not what it is in production. Diagnose with [`server_benchmark_tool_diagnostics`](../server_benchmark_tool_diagnostics/SKILL.md) before filing a finding in any other category.
   Before filing a content gap, check the page on disk and diff the assessor's "missing" list
   against it: two of the six "missing" Gnoll facts were in `Races/Gnoll.md` all along (run 29).

An analysis that fails to classify its findings into this taxonomy is incomplete.

> 🛑 **Verify every quotation before building a finding on it.**
>
> Benchmark reports contain hardcoded interpretive prose written by `BenchmarkReportBuilder`, not extracted from the prompt at run time. **Any prompt text a report quotes must be checked against `Overseer/Services/ChatService.cs` or `Overseer/ToolGuides/` before a finding rests on it.** A mismatch is a **Harness Defect**, not a chat finding.
>
> This rule exists because a finding was once built on a sentence the prompt had never contained — see the withdrawal of T3 below.

> **A Root Cause Analysis' hypotheses and checks are inputs to this triage, never findings.**
>
> The Root Cause Analysis (`docs/overseer/ai-benchmark-report-pack.md` § 17) lists **hypotheses**, each with a **locus** — `model`, `chat`, `harness`, `suite`, `corpus` or `provider`, this section's categories plus model and provider — a confidence grade computed by code, and **checks** for what a run cannot settle. They are provisional and un-triaged: the grade rests on computed figures and grader text only, the locus names where the cause *could* be, and neither the engine nor the writer read the source, the tool-call log or the corpora. Even a *High* hypothesis or a *Root cause established* axis is a statement about the benchmark's figures, not a verified finding. Treat each hypothesis and check as a pointer to where to look. Before anything is changed because of one, it must go through this section's triage, the evidence bar of § 6 and, whenever it turns on what a tool returned, the tool-layer diagnostics of § 4a — exactly as a finding you found yourself. One that does not survive that is dropped, whatever its grade or locus said. (The Internal Improvement Brief's *leads*, which this callout used to govern, were retired with that document on 2026-10-15.)

### Worked Example: Benchmark Run 11 (2026-09-04, GPT-5.6 Luna)
- **Harness Defects (F1–F8)**: eight instrument defects, among them F1, the blind second-opinion
  migration that lacked a default backfill (`SecondOpinionBlind = 0`), so run 11 graded anchored
  (§ 6).
- **Chat-Transferable Findings (T1–T6)**:
  - T1/T2: Completeness lowest (83.0 vs 97.7 Accuracy) under the concise instruction, a reading the
    run-12 controlled pair did not bear out (T7, § 3).
  - **T3 — WITHDRAWN.** Source code tools accounted for 71% of tool calls, and the report described this as occurring despite a prompt rule *"Prefer wiki tools over source code tools"*. **No such rule exists.** That sentence was hardcoded in `BenchmarkReportBuilder.cs`, not read from the prompt. What `Overseer/ToolGuides/_policy.md` actually says is narrower and conditional: it prefers GnollHack tools over *web search*; it routes strategy and general "what is X" questions to wiki tools first; it routes **specific mechanics questions** (exact AC, damage dice, MR, resistances, speed, material, artifact flags) to the structured stats tools; and it places source code tools at rung 4 of a seven-rung hierarchy, for questions that require reading game logic. On a suite weighted toward mechanics, a high source share may be **prompt-compliant**. The routing-inefficiency reading is withdrawn; the correlation with turn latency ($r = 0.53$) survives as a **cost** observation. Reclassified: **Harness Defect**.
  - **T4 — WITHDRAWN.** Knowledge base was called only once in 18 questions (`get_knowledge_article` under-use), and the report described this as under-use. **The prompt explicitly directs the model to skip the knowledge base for game mechanics** (the frozen segment's "Information Routing" section in `ChatService.BuildSegmentedSystemPrompt`, scoping it to app navigation, settings, controls, replay, vault, and troubleshooting). On an all-mechanics suite, zero calls is **prompt-compliant**. Reclassified: **Harness Defect** and fixed in `BenchmarkReportBuilder` by guarding the line on question topics.
  - T5: refuted claims on Q9 (spell-skill saving throws) and Q16 (simultaneous attacker cap).
  - T6: a systematic framework required to guide chat prompt updates.

---

## 3. Configuration Parity — Check Before Attributing Anything

Before attributing any score, weakness, or behavior to a model's underlying intelligence or reasoning capabilities, **always inspect the Chat Prompt Under Test block in the report manifest**. It renders `BenchmarkRun.CandidatePromptOptionsJson`, which snapshots every field below.

| Option | Why it changes what was measured |
|---|---|
| `verboseMode` | Concise (`false`) instructs 2–5 sentences. Completeness is graded against rubrics that ask for more, so the two must be read together; the one controlled pair (runs 11 and 12, T7) did not show the style lowering Completeness |
| `spoilerFreeMode` | Filters what may be revealed, independent of what was retrieved |
| `overseerMode` | Selects the persona and task framing (`0` = Gameplay Help) |
| `enableToolUse` | With tools off, every answer is closed-book |
| `enableWebSearch` | Adds a provider-side retrieval path outside the tool runner |
| `allowSourceCodeReferences` | Gates whether source citations are permitted at all, and selects one of two opposite prompt branches (cite file and line / never cite them). Set per run by the launcher's **Source Code References** field from harness 44, default *Disallowed — production default*, which is what a user with *Show source code references* off receives; every run before harness 44 graded *Allowed* |
| `enableSubAgents` | Adds `delegate_to_subagent` and its prompt section |
| `isGameOn` | Changes the framing from reference lookup to live advice |
| `developerMode` | Injects runtime debug data and its prompt section |
| `hasMessageHistory` | Adds the history section and the "reference earlier events" instruction |
| `hasWikiContext` | See the callout below |
| `hasGameSnapshot` | Adds the board, inventory, Discoveries and Pets sections |
| `parallelMode` | **Passed separately**, from `testedConfig.ParallelExecutionMode`. Overrides the batching policy, and therefore directly moves the tool-call counts and latency that § 5 Axis 2 and Axis 3 read |

> ⚠️ **`hasWikiContext` is not a per-run setting — it is a permanent divergence.**
> Its declaration comment (`BenchmarkCandidatePromptOptions.cs:52`) reads
> *"false — chat pre-injects; the benchmark does not."* No benchmark run has ever graded the
> prompt a live user actually receives on this axis. Treat any finding about retrieval or
> tool routing as measured under a condition live chat does not share.

**Non-prompt confounds** must be checked in the same pass, because they change the result without changing the prompt:

- Candidate thinking level and candidate model version
- Per-question tool-call budget and tool-iteration budget (see § 7 rung 4)
- Harness version and scoring method version
- Scoring profile (weights, level scores, critical-error ceiling)
- Assessor roster, second-reader coverage, and blind vs. anchored
- **Provider-side request settings** — prompt caching mode, service tier, thinking level, parallel tool calls. These are part of the measured configuration. The benchmark and live chat run under the same provider settings, always: a setting enabled for benchmark runs only makes every cost, latency and cache figure in a report a measurement of a configuration chat does not have, and a finding that does not transfer. Decided 2026-09-12 (run-40 round) when a benchmark-only Gemini explicit cache was considered and rejected on this ground.
- **Provider availability.** From harness 56 every provider attempt is recorded, and a provider
  failure is told apart from an Overseer-side cause such as a cancellation, a time budget or our
  own rate limits (`docs/overseer/ai-benchmark-provider-availability.md`). Read the attempt ledger
  before charging a failed or slow answer to the model.

**The Golden Rule of Attribution**: read a dimension against the instruction the candidate answered under before attributing it to the model. In run 11, Completeness (83.0) trailed Accuracy (97.7) by 14.7 points under the concise instruction *"Default to 2–5 sentences per response"*, and that run's analysis read the gap as the instruction's effect. The controlled pair that followed did not bear this out: in run 12, `verboseMode: true` moved Completeness from 83.0 to 83.8, inside noise, while Accuracy fell 4.2 and mean model time rose 28 % (T7, § 11). A low Completeness under the concise style is therefore a gap *measured under* that style, of undetermined cause: neither the model's failing alone nor the style's effect. Blaming the model without checking `verboseMode` is an attribution error; so is crediting the style without a controlled pair. The report writer's prompt follows the same rule (`BenchmarkReportPackPrompt`, the response-style instruction).

---

## 4. What the Benchmark Does Not Measure

The benchmark covers one slice of Overseer chat. A finding transfers **only to the configuration that was measured**; extending it to any surface below requires its own evidence.

| Measured | Unmeasured |
|---|---|
| Single-turn questions | Multi-turn conversational context |
| No pre-injected wiki context | **Pre-injected wiki context — which live chat always provides** |
| Spoiler-free off | Spoiler-free mode |
| Web search disabled | Web search tool routing |
| Subagents disabled | Subagent delegation and parallel tasks |
| Tools enabled, concise style, source references as the launcher's **Source Code References** field set them (always allowed before harness 44) | Any other combination of § 3's options |

> 🛑 **Tool-routing findings are the most exposed.** Every routing measurement was taken with
> `wikiContext` empty. Live chat hands the model wiki text before it decides anything —
> precisely the condition most likely to change which tool it reaches for. A routing finding
> is evidence about the benchmark configuration until a run with wiki context says otherwise.

The same caution applies in the other direction: a chat problem observed in a live session with history, wiki context or spoiler-free mode active is **not** contradicted by a benchmark run that scored well, because the benchmark never exercised that path.

---

## 4a. Tool-Layer Diagnostics

A benchmark run executes the **production tool registry**, so a tool defect seen in a run is a defect a real user hits. What a run records about its tool calls **depends on its harness version**, and four facts govern every tool finding:

- **Whether arguments and results were stored is a version boundary — check the run's harness version before anything else.** For a run **before harness 17** they were not stored: `BenchmarkRunAnswer.ToolCallSummary` is a `name×count` string over *successful* calls only, `AgentRunRequest.ShowDebugLog` is hardcoded `false` at every benchmark call site, and a run creates no `ChatMessage` rows, so nothing equivalent to `ChatMessageToolCall.ArgsText` / `.Result` exists. For such a run, *"compare the parameters and results"* is **reconstruction and replay**, never transcript reading. **From harness 17** a run's `BenchmarkRunAnswer.ToolCalls` rows carry the real `ArgsText`, `Result`, `Error`, `Status`, emission order (`SortOrder`), tool round (`IterationIndex`), timings and the result size as `ToolExecutor` handed it over (`ResultLengthChars`) for **every attempted call**, read through `GET /api/admin/benchmark/runs/{id}/answers/{answerId}/tool-calls` (admin-authenticated). Reading those rows is **rung zero** — attempted before any reconstruction or replay (`server_benchmark_tool_diagnostics` § 2, § 7). A row whose payload the retention sweep pruned shows `ArgsText`/`Result` null beside a non-zero `ResultLengthChars`; that is the sweep, not an absent record.
- **The count of failed tool calls is derived for a run before harness 17.** `ToolCallCount − Σ(ToolCallSummary counts) − ToolCallsBlocked` is the number of calls that errored technically. No report section surfaces it, and a non-zero value is direct evidence of a tool problem. Compute it first. A null `ToolCallsBlocked` means *not recorded*, never zero. **From harness 17 the figure is reported** as the answer's three-way outcome split, so read it rather than deriving what the record already states.
- **From harness 16 a run fingerprints three of the five corpora** — knowledge base, GnollHack wiki, GnollHack source — by their Git HEAD, which says nothing about an uncommitted working tree. **From harness 48 it also records what each of the five indexes held** (`CorpusIndexFingerprintsJson`: a SHA-256 over the indexed text of every file, with a file count), the NetHack wiki and NetHack source included. All of these are **provenance, not comparability keys**: a difference is a fact to investigate, not an automatic tier drop.
- **Version currency.** The bullets above were last checked against harness **56**, on 2026-10-19. `BenchmarkAssessmentPrompt.HarnessVersion` is the source of truth for the current value; if it now reads higher, treat this section as possibly aged and verify every claim against `server_benchmark_tool_diagnostics` § 2 before relying on it. This section silently aged out at harness 17 once already, and cost a run-28 analysis its tool-layer evidence — that is why this line exists.

> 🛑 **Stop here.** Read the three tool-layer skills **now**, before dispatching any research about a tool, a corpus, or a tool count, and before writing the first finding. All three are read at this point; none is reached through the others.
>
> 1. [`server_benchmark_tool_diagnostics`](../server_benchmark_tool_diagnostics/SKILL.md) — the five verdicts for a tool's call population, the ladder that separates "no access" from "no data" from "a broken tool", the three replay fidelity tiers, and the diagnostic table § 10 below requires
> 2. [`server_tool_data_sources`](../server_tool_data_sources/SKILL.md) — the corpora behind each tool, and what every index silently excludes
> 3. [`server_tool_parameter_reference`](../server_tool_parameter_reference/SKILL.md) — the per-tool parameter and result contract, and how to tell a correct empty result from a broken one
>
> § 4a is a **pointer, and deliberately incomplete**: nothing above tells you what verdict a tool's calls earned, and being a readable summary is precisely what has made it feel sufficient before. Do not re-derive the method here, and do not dispatch a subagent to rediscover from source what these three already document.

---

## 5. The Three Transfer Axes

Benchmark reports provide quantitative diagnostic data across three distinct performance axes:

### Axis 1: Quality
- **Metrics to Read**: Dimensional averages, BARS levels (0–6), evidence strings (`accuracyEvidence`, `completenessEvidence`), critical errors, refuted claims, and disputed verdicts.
- **Dimension weights are per-run, not fixed.** Accuracy 55% / Completeness 25% / Conciseness 10% / Readability 10% are the **defaults** on `BenchmarkScoringConstants`. The weights that actually applied come from the run's `BenchmarkScoringProfile` row, resolved by `BenchmarkScoringProfileService` and editable per profile. Comparing two runs under different profiles while assuming fixed weights mis-attributes a score shift to the model.
- **Advisory sensitivity indices sit beside the Intelligence Index, and none is a score.** Each is
  omitted when its population is empty and moves no stored number; read them as the width of the
  grading's own uncertainty, never as a corrected result.
  - *Contested-Verdict Sensitivity* resolves every critical-error split at the second reader's
    figure.
  - *Verification-cleared Accuracy Sensitivity* (harness 25) raises Accuracy one level on every
    answer docked citing only claims the rubric did not cover, where the verifier supported every
    such claim it checked: the instrument's share of the Accuracy shortfall, as `OUT-OF-SCOPE:` is
    of Completeness.
  - *Evidence-informed Sensitivity* (harness 30) uses the primary assessor's re-grade of a
    contested answer with the claim verifier's findings in hand. From harness 31 it counts
    validated re-grades only and prints how many it excluded; on an earlier run it is labeled
    *legacy, unvalidated*. A human reads each re-graded answer's `withdrawn` list against the
    verifier verdicts it cites before anything rests on it (§ 11, the verifier caution), and the
    stage's rollback is `Benchmark:EvidenceInformedRegrade:Enabled` = `false`.
- **Read the outcome figures, not only the index** (from scoring method 13): the **confirmed
  critical-error rate** with its 95 % Wilson interval, the correct / partial / incorrect / not
  attempted / no answer counts, **correct when attempted** and **wrong instead of abstaining**
  (incorrect ÷ (incorrect + not attempted)). The last is the direct measure of whether a model
  guesses where the production prompt tells it to say it does not know. Before reading a
  Not-attempted answer as a model trait, check through § 4a whether its tools could have found the
  fact at all.
- **What a level means depends on the scoring method and the harness**: read every dimension
  against the version boundaries below.
- **Interpretation**:
  - Low Accuracy indicates factual hallucination or outdated knowledge. Check if the claim was verified against source code.
  - Low Completeness under `verboseMode: false` is a gap measured under the concise style, of undetermined cause (§ 3): read it as neither a model failure nor prompt adherence until a controlled pair separates them. Under `verboseMode: true`, it indicates an inability to retrieve or explain edge cases.
  - A Critical Error indicates dangerous misinformation (e.g. fatal tactical advice).

#### Version Boundaries

A figure is compared only with figures on the same side of every boundary that touches it. The
last column names the heading in `docs/overseer/ai-benchmark.md` that holds the full change.

| Boundary | What it changes when reading a dimension | Comparability consequence | Doc section |
|---|---|---|---|
| Scoring method 8 (run-14 round) | Method 7 → 8, with an in-place scoring-profile edit | Runs 11–14 are observations, not reproduction halves, against later runs | *Harness Version 14 & Scoring Method Version 8 Updates* |
| Scoring method 9 (run-22 round) | Rubric FORM criteria are no longer charged against Readability | No comparison across it on Readability | *Scoring Method Version 9 Updates* |
| Scoring method 10 (run-25 round) | An unanswered question the model ended normally scores 0 and enters the Intelligence Index, the raw index and the unweighted mean; Speed Index and run status are unaffected | No comparison across it on those figures when either run has such a question | *Scoring Method Version 10 Updates* |
| Scoring method 11 (harness 31, run-53 round) | Accuracy grades only what the answer states; depth, length and source-level detail are Completeness matters, where levels 5–6 rewarded source-level depth before | Accuracy, quality score and index not comparable across it; every run up to run 53 is before it | *Harness Version 31 & Scoring Method Version 11 Updates* |
| Scoring method 12 (harness 33, run-55 round) | Accuracy is graded against the rubric and the board only; an own-knowledge suspicion is a `Suspected false: ` claim and costs Accuracy only through a refutation. An `OutOfRubricAccuracyDeduction` flag means the assessor docked on own knowledge against its instructions | Scores generally rise; none comparable with method 11; every run up to run 55 is before it | *Harness Version 33 & Scoring Method Version 12 Updates* |
| Scoring method 13 (harness 45, battery run 1) | A critical error is confirmed before it caps (*Agreed*, *UpheldByVerifier* or *SingleAssessor*, `BenchmarkCriticalErrorResolver`) and needs a claim the rubric or board contradicts, so dangerous values go into the rubric's CRITICAL ERROR clause; an honest `notAttempted` answer scores at least the profile's Not-attempted score (default 50) | None comparable with method 12; `NotAttemptedScore` joins the profile key. A method-12 panel run's *Critical-error resolution sensitivity* line shows the method-13 outcome, advisory only | *Harness Version 45 & Scoring Method Version 13 Updates* |
| Scoring method 14 (harness 49, battery run 4 round) | A true statement is never an Accuracy imprecision: one less specific than the rubric, or a true condition it omits, is a Completeness matter, for both members and the reference reader | Scores generally rise; none comparable with method 13 | *Harness Version 49 & Scoring Method Version 14 Updates* |
| Harness 17 | `BenchmarkRunAnswer.ToolCalls` stores every attempted call, and the outcome split is reported | Before 17, arguments and results are reconstruction or replay (§ 4a) | *Harness Version 17 Updates* |
| Harness 18 (the run-33 round's H1) | Gemini usage counted once per call, not once per streamed chunk (≈ 11× on run 33) | Every earlier Gemini-role token and cost figure is inflated and not rewritten: assessor and synthesis on every run, the candidate on run 22 | *Harness Version 18 Updates* |
| Harness 26 (run-41 round) | Difficulty prompt re-anchored; suite 6 re-assessed (mean 70.4 → 61.8) | No later suite-6 run comparable with runs 29–41 on the Intelligence Index | *Harness Version 26 Updates* |
| Harness 29 (runs 50–51 round) | The system prompt goes first; `BenchmarkCandidateRequestProbe` checks the serialized request | Runs 36, 37, 38, 42, 46, 47 and 51 are prompt-less OpenAI runs (§ 11, the 2026-09-17 correction); earlier Google and Anthropic snapshot-suite runs had no board | *Harness Version 29 Updates* |
| Harness 32 | The board read (`GameSnapshotSha256Used`) is the Fundamental key `GameSnapshot`, and has its own system message for every grading role | Runs on different boards are not pooled or ranked; 31- and 32-stamped snapshot runs are not grade-comparable; every `ComparabilityKeyHash` changed. Suite 8 boards: `8f8c4778d449` (runs 52–53), `a21dc90405b6` (54–61), `957f1f38c0a7` (62 on) | *Harness Version 32 Updates* |
| Harness 34 | Gemini thinking tokens (`thoughtsTokenCount`) counted as output | Earlier Gemini candidate output and cost figures under-state cost | *Harness Version 34 Updates* |
| Harness 40 | The two-family panel (§ 6, *Reading a panel run*) | A different grader instrument from every single-assessor run | *Harness Version 40 Updates*; *The Two-Family Assessor Panel* |
| Harness 44 | The **Source Code References** launcher field, default *Disallowed — production default* | Every earlier run graded *Allowed*, the other prompt branch | *Harness Version 44 Updates* |
| Harness 48 | `CorpusIndexFingerprintsJson` for all five indexes | Provenance, not a key: investigate a difference, never drop a tier for it | *Harness Version 48 Updates* |
| Comparability key definition 2 (2026-09-24) | Candidate and grader settings read from recorded model snapshots; `CandidateEndpoint` joins the candidate keys; grader signatures gain the output cap and endpoint and lose `parallelMode` | Earlier runs read as a different grader instrument (`(not recorded)`); a group analyzed before shows *Comparability definition changed* until re-analyzed | *Model Configuration Snapshots* |

### Axis 2: Speed and Latency
- **Metrics to Read**: Model Time ($\text{DurationMs} - \text{ToolTimeMs}$), TTFT percentiles (P50, P90, max), and tool call counts.
- **Interpretation**:
  - Distinguish model deliberation time from tool network I/O.
  - Heavy tool usage (e.g. 5+ source searches per turn) adds seconds of I/O latency and degrades user experience.
  - The Speed Index is an advisory psychometric score across difficulty bands; compare raw Model Time, not Speed Index, when evaluating chat responsiveness.
  - Its calibration (`SpeedTargetMs`, `SpeedDecayK`) is a comparability key of its own,
    `SpeedCalibration`, apart from the quality signature (the runs 47–48 round).

### Axis 3: Cost and Token Efficiency
- **Metrics to Read**: Input:Output token ratio, prompt cache-read share, and tool call breakdown by family.
- **Interpretation**:
  - Run 11 demonstrated a 44:1 input:output ratio with a 90% prompt cache-read hit rate, confirming that Overseer's segmented system prompt (`ChatService.BuildSegmentedSystemPrompt` — frozen, session-stable, volatile) delivers massive cost savings.
  - Proliferation of source code tools significantly increases input token consumption due to large C code payloads.
  - Anything that changes the **frozen** segment invalidates that cache for every session. See § 7 rung 1 and rung 5.
  - Gemini token and cost figures cross two boundaries: harness 18 and harness 34 in the § 5
    table.

---

## 6. The Evidence Bar

> 🛑 **A single benchmark run MOTIVATES a chat change; it NEVER JUSTIFIES one.**

The production chat prompt is the scientific instrument. Editing the prompt in response to a single benchmark run destroys comparability for future runs and risks overfitting to idiosyncratic grader or candidate quirks.

### The Minimum Bar for Touching `ChatService.BuildSystemPrompt`:
1. The finding has reproduced across at least **two comparable benchmark runs**, OR
2. A controlled pair of runs was executed where a single variable was isolated (e.g. identical model run under `verboseMode: false` vs. `verboseMode: true`).

### What makes two runs comparable

Two runs count as reproduction **only** when they match on all of:

- The full `BenchmarkCandidatePromptOptions` record, and `parallelMode`
- Harness version and scoring method version
- Scoring profile — weights, level scores, critical-error ceiling, and from scoring method 13 the
  not-attempted score (`NotAttemptedScore`, the floor of an honest abstention)
- Assessor roster, second-reader coverage, and **blind vs. anchored**
- Suite item revisions **and** suite assessed difficulties (`SuiteItemRevisions`,
  `SuiteAssessedDifficulties`) — a rubric edit bumps the former without necessarily touching the
  latter, and Assess Difficulty moves the latter with **no** revision bump (H5, run 39): two runs can
  match on one of these Fundamental keys and differ on the other, and either difference alone means
  the runs were not scored against the same weighted exam.

> **Worked example — why the last one is not a footnote.** Run 11's F1 records that the blind
> second-opinion migration lacked a default backfill, so the run graded **anchored** while a
> later run under the same nominal settings would grade **blind**. That is a different
> grading regime producing different scores from an identical candidate, and nothing in the
> score itself reveals it. "Two runs" that differ here are two measurements, not a
> reproduction.

**Comparability Invariant** (from `docs/overseer/ai-benchmark.md` § 9.1): two runs are strictly comparable on **Completeness**, **Conciseness** and **Readability** only if their candidate prompt options match.

> ⚠️ **What the run record fingerprints, and what it does not.**
> `BenchmarkService.PopulateInstrumentFingerprint` records on every run:
>
> - `CandidateSystemPromptSha256`: the prompt text as built, which includes the knowledge-base
>   topic list of the frozen segment;
> - `ToolGuidesSha256`: one manifest hash over every file of the deployed `ToolGuides` directory,
>   `_policy*.md` and `spoiler_policy.md` included. Per-tool guides are not inlined into the
>   prompt, so a per-tool guide edit moves this hash only;
> - `KnowledgeBaseHeadSha`, `WikiHeadSha` and `SourceCodeHeadSha`: the Git HEAD of the knowledge
>   base, the GnollHack wiki and the GnollHack source;
> - from harness 48, `CorpusIndexFingerprintsJson`: what each of the five indexes held.
>
> What still has no fingerprint: the `ChatService.cs` commit itself (the prompt **text** is hashed,
> not the code that built it), the `Overseer/appsettings.json` tool limits, and, before harness 48,
> the working tree of a HEAD-fingerprinted corpus (from harness 48 the index fingerprint covers
> it). The Instrument SHAs field of § 11 records the first two. The two-run bar is met when the
> recorded hashes match **and** the analyst has confirmed that none of these moved in between.

> ⚠️ **The fingerprint moves on a prompt *option* change, not only on an instrument change.**
> `CandidateSystemPromptSha256` hashes the prompt **as built**, so a run option that changes the
> prompt text — `verboseMode` is the usual one — moves the hash while `ChatService.cs`, the tool
> guides and the knowledge base all stand still. A hash difference is therefore evidence of *a*
> difference, not evidence that the instrument moved: compare `CandidatePromptOptionsJson` first,
> and only runs whose options match can testify about the instrument at all. Runs 24 and 25 of
> 2026-09-08 are the worked example — 25 minutes apart, identical `ToolGuidesSha256` and
> `KnowledgeBaseHeadSha`, prompts differing by 92 characters and by exactly one option, on unchanged
> code. The Run History badge now distinguishes the two cases.
>
> The useful corollary: two runs whose `CandidatePromptOptionsJson` differs in exactly one field,
> with one `ToolGuidesSha256`, one knowledge-base HEAD and no intervening change to the
> unfingerprinted pieces above, are an **isolated pair on that option** — the controlled pair bar 2
> above accepts in place of two comparable runs.

> 🛑 **The assessor, second-reader and claim-verifier model configurations are `Instrument`
> comparability keys too** (`BenchmarkComparabilityKey`), and a change of a grader's **thinking
> level** is the same key move as a change of model. A grader-roster change therefore happens
> **between series, never between the two halves of a verification pair**, and is recorded in § 11
> like any other instrument change. Run 30 is the worked example: the claim verifier moved from
> GPT-5.6 Sol to GPT-5.6 Luna between runs 29 and 30, so run 30 differed from run 29 on three
> instrument keys instead of two, and a roster moved on the wrong side of a pair leaves the pair
> verifying nothing.

Below this threshold, findings are logged in the **Model Behaviour Notes** (§ 11) and the prompt remains untouched.

### Reading a panel run

A run with a co-assessor (harness 40 on) is graded by a **two-family panel**: member A (the
assessor) and member B (the co-assessor), from two providers, both grade every answer blind to each
other, and the published score is their mean. The second reader's slot holds the **reference reader** — it
grades every answer, blind, and never scores. The mechanics and the assumptions are in
`docs/overseer/ai-benchmark.md` § 3 *The Two-Family Assessor Panel*. Weigh its findings by how many
independent readers carry them:

- **Convergent findings and panel-agreed verdicts clear the bar more easily.** A synthesis finding the
  run view's Agreement tab marks *Both members* (`Convergent`; report § 6.3), or a verdict where the members did not disagree (no
  *MEMBERS DISAGREE* badge), was reached by two judges from different families. That is stronger
  evidence within one run than any single-assessor finding; it still motivates rather than justifies
  a prompt change, and the two-run or controlled-pair bar above stands.
- **A single-reader, same-family strength is the weakest evidence there is.** A strength only one
  member names, when that member shares the candidate's provider, is exactly what same-family
  preference would produce. Do not credit it without the other member or a tool-grounded check.
- **A single-reader weakness needs its evidence opened.** Read the member's evidence and the claim
  verifier's items it raised (`raisedBy`) before filing it; the verifier checks both members' charges.
- **A judge-dependent ranking must not by itself move the chat's model choice.** When Model
  Comparison's judge-family diagnostics list a pair of entries as judge-dependent — member A and member
  B rank them in opposite order — the panel's order for that pair rests on the balance between the
  members, not on their agreement. It is a tie for rung 6 until another comparable run or a replicate
  set separates them.
- **The bias figures are estimates with stated assumptions.** The asymmetry estimate assumes the
  reference reader is neutral between the members' two families; the accusation audit assumes the
  verifier is. Quote them with their intervals and the diagnostics' caveats, never as a verdict on a
  provider.
- **The reference reader's disagreement is with the panel score**, not with member A: read it as a
  reason to open the answer, as with any second reader.

---

## 7. The Ladder of Safe Changes

When an empirical chat-transferable finding clears the evidence bar, resolve it using the **lowest possible rung** on the ladder of safe changes:

1. **Knowledge Base Article (Lowest Cost, Safest)**:
   - When a model hallucinates a mechanic or has a claim refuted by the verifier (e.g. Q9 spell-skill mechanics, Q16 attacker cap).
   - The frozen segment's *Information Routing* section (`ChatService.BuildSegmentedSystemPrompt`)
     tells the model, for the topics the knowledge base lists, to call `get_knowledge_article`
     first, because the knowledge base is the authoritative source for them. A wrong article is
     therefore a wrong answer: `item_identification_and_appearances` taught NetHack identify, altar
     and engraving rules, and battery run 9's one confirmed critical error came from it.
   - **Ships without a deploy.** The knowledge base is a **separate git repository** at the
     configured `KbPath`. `KnowledgeBaseService` compares its HEAD SHA with the published set's on
     a 10-minute `CheckInterval` and reloads on a change. Pushing an article is the whole
     deployment. This is the main reason this rung is first.
   - ⚠️ **It is not prompt-neutral.** The KB topic list (`KnowledgeBaseService.GetTopicList`) is
     injected into the **frozen** prompt segment, so a new article changes the graded prompt and
     invalidates the frozen segment's cache for every session. It is a *smaller* instrument change,
     not *no* instrument change — record it between runs like any other (§ 11).
   - *Requirement*: Human authorship only; model outputs must never be ingested automatically as authoritative knowledge.
   - ⚠️ **Game mechanics do not belong on this rung.** The same *Information Routing* section
     tells the model: *"For game mechanics, monsters, items, spells, or other topics NOT listed
     above: skip the knowledge base entirely and go directly to wiki_search, monster_lookup, or
     item_lookup."* An article about a race, a monster or a mechanic therefore sits in a store the
     model is instructed not to consult, and making it reachable means widening the topic list —
     which is in the **frozen** segment, so it invalidates the prompt cache for **every** session.
     Overseer's cache-read share is 90–91 % on Anthropic and OpenAI runs, so that is a real
     recurring cost. **The correct rung for game-mechanics content is 2**; the Gnoll-race article
     once proposed at rung 1 belonged there, and the run-29 T4 correction showed that
     `Races/Gnoll.md` had been indexed all along.
2. **Wiki Content Update**:
   - For factual omissions or ambiguities that belong in public NetHack/GnollHack documentation rather than specialized Overseer tips.
   - **The deploy path.** `wiki_search` and the other GnollHack wiki tools read the GnollHack wiki
     configured as `WikiPath`, a Git repository. `WikiService` re-indexes when its HEAD moves, on a
     ~10-minute poll, or when an administrator clicks **Re-index** on Admin → Services → GnollHack
     Wiki; the poll compares SHAs, so an uncommitted edit is never picked up
     (`server_tool_data_sources` § 7). The rung-2 handoff below targets the GnollHackWiki clone at
     `C:\hmp\GnollHackWiki`, whose HEAD an analysis compares with the run's `WikiHeadSha`. The
     NetHack wiki mirror at `C:\hmp\nethackwiki`, which the `nethack_wiki_*` tools read, is not a
     Git repository and re-indexes only at Overseer startup or on its own manual Re-index
     (`.agents/AGENTS.md`). GnollHack content goes to the GnollHack wiki, never only into the
     NetHack mirror, where a hand-added section is one re-sync away from being erased.
   - A wiki edit may be **AI-authored when every claim it adds has been verified against the game
     source or another primary text** — this project's practice, and the one the `Races/Gnoll.md`
     rewrite followed. What stays forbidden, unchanged from rung 1, is ingesting a model's own
     *answer* as fact; a claim checked against the source is not that, and this does not loosen
     rung 1's human-authorship requirement for knowledge-base articles.
   - **The citation goes in the handoff document, never on the page.** The wiki is player
     documentation: a page states the mechanic in the game's own vocabulary and carries no file
     paths, line numbers or C identifiers, and it must stay readable rather than drifting into
     specification or AI-skill style when a benchmark finding improves it. Those conventions belong
     to the wiki repository — `wiki_editing` § 4 and § 17 — and are cited, not restated here. What
     this side owes them is a prompt that does not ask for text they forbid:
     [`server_wiki_handoff`](../server_wiki_handoff/SKILL.md) § 2 for separating the fact from its
     evidence, § 3 for what a prompt must not prescribe, § 6 for the run-42 case where a prompt
     prescribed inline source citations and the wiki session had to override it.
   - **A wiki edit leaves this repository as a three-step handoff document — the proposed changes,
     a prompt that validates them against the GnollHack source, and a prompt that executes them in
     the GnollHackWiki clone. Before writing one, complete the pre-flight checklist in
     [`server_wiki_handoff`](../server_wiki_handoff/SKILL.md) and use its templates.** The
     run-34 handoff (2026-09-10) presumed a page generator that does not exist and named
     `Resistances and Saving Throws.md` as the formula's home when the formula is in
     `Saving Throws.md`; its prescribed verification grep would have returned zero. Both were
     checkable read-only on disk. The same rule that binds content gaps (§ 2 category 4: check
     on disk before filing) binds handoff documents.
   - **The handoff document carries the proposed changes in plain text and as diffs, a validation
     prompt for a read-only session on the GnollHack source clone, and an execution prompt the human
     runs only after validation passes** ([`server_wiki_handoff`](../server_wiki_handoff/SKILL.md)
     § 3a); anything else that has to be done is done by the analyst, or stated inside one of the two
     prompts. When another
     step of the round depends on the wiki change having landed, the round stops at the
     confirmation gate in § 4a there instead of proceeding on the assumption that it did.
3. **Tool Descriptions and Tool Policy Text**:
   - For tool routing inefficiencies. Changing tool descriptions guides the model without altering core persona prompt sections.
   - `_toolRegistry.GetPolicyText()` only returns a cached string. The editable sources, loaded by `ToolRegistry.LoadGuides()` from `<AppBase>/ToolGuides`, are:

     | File | Content |
     |---|---|
     | `Overseer/ToolGuides/_policy.md` | Tool Use Policy, Tool Preference Hierarchy, batching, Accuracy About Tool Use |
     | `Overseer/ToolGuides/spoiler_policy.md` | Spoiler-free policy text |
     | `Overseer/ToolGuides/_policy_parallel_disabled.md`, `_policy_parallel_on_request.md` | Parallel-mode overrides |
     | `Overseer/ToolGuides/<tool_name>.md` | Per-tool description, overriding the handler default |
4. **Limits Parity**:
   - Aligning session and iteration budgets between chat and benchmark bands per `docs/overseer/ai-benchmark.md` § 9.
5. **Prompt Segmentation**:
   - Moving content between the frozen, session-stable and volatile segments of `BuildSegmentedSystemPrompt`. This changes **no instruction text** — only what is cacheable — so it is a genuine cost and latency lever with no behavioural risk. § 5 Axis 3 measures the effect.
   - **Explicit Gemini `cachedContents` is not used, by decision of 2026-09-12.** Chat and benchmark must share provider settings (§ 3), and a chat-wide explicit cache is billed per token-hour of storage, which at the site's traffic costs more than the discounted reads save — implicit prefix caching only, which measured 54.3 % and 51.9 % on runs 39 and 40. Do not re-propose without new traffic data and a parity-preserving design.
6. **Model or Thinking Level Selection**:
   - Adjusting default models or reasoning effort in `/settings` rather than hacking prompt prose.
7. **Chat System Prompt Modification (Highest Risk, Last Resort)**:
   - Modifying `ChatService.BuildSystemPrompt` prose directly. Reserved exclusively for systemic, cross-model deficiencies backed by multiple comparable runs.

---

## 8. Anti-Overfitting Rules

To preserve Overseer chat quality for real human players, agents and developers are **strictly prohibited** from:

1. **Never copy benchmark rubric points into the chat system prompt**:
   - Rubrics exist to score specific questions. Baking rubric answers into the system prompt is Goodhart's Law; it inflates benchmark scores while bloating the prompt for real users.
2. **Never force verbose mode on production chat to inflate Completeness**:
   - A player asking a question while playing needs a crisp 2–5 sentence answer, not a 1,000-word encyclopedic dump. Terse chat defaults are an intentional product decision.
3. **Never tune prompt instructions to optimize the Speed Index**:
   - Speed Index measures adherence to difficulty-scaled latency targets. Chat responsiveness is optimized through streaming and prompt caching, not by suppressing necessary model reasoning.
4. **Never weaken anti-fabrication, uncertainty, or spoiler-free constraints**:
   - Under no circumstances should an agent loosen uncertainty warnings or anti-hallucination guardrails to score points on questions where the model lacked confidence.
5. **Never validate a change only on the questions that produced the finding**:
   - A change motivated by Q9 and Q16 and then confirmed by improvement on Q9 and Q16 has demonstrated nothing about chat — it has demonstrated that the change addressed two questions. Improvement must appear on questions **not implicated** in the original finding, or on a distinct suite.

---

## 9. Verification and Rollback

A protocol that authorises production prompt edits but specifies no way to detect that an edit made things worse is incomplete in the direction that matters most. Every change that reached **rung 3 or above** carries these four obligations.

1. **Re-run.** The change is followed by a benchmark run under the **same** configuration as the run that motivated it — same prompt options, harness version, scoring profile and assessor regime, per § 6. A change verified against a differently-configured run is unverified.
2. **Pre-declared acceptance criterion.** *Before* the change is made, write down which dimension is expected to move, in which direction, and by how much — **referenced to the run's own confidence interval.** Run 11's Intelligence Index carried 91.5 ± 5.9 at 95% CI; a 3-point movement is not a result. Declaring the criterion afterwards is choosing the target after seeing the arrow land.
3. **Side-effect check.** Improvement on the intended axis is not sufficient. Check the specific opposing pairs from § 5:

   | Change made for | Must also be checked against |
   |---|---|
   | Quality (Accuracy, Completeness) | Mean model time; input token volume and cache-read share |
   | Latency (tool routing, iteration budgets) | Accuracy and refuted-claim count |
   | Cost (segmentation, tool proliferation) | Accuracy, and TTFT if the frozen segment changed |

4. **Rollback trigger.** A change that fails to meet its pre-declared criterion, or that degrades another dimension by more than the run's CI, is **reverted**. Record the attempt and its outcome in § 11 so the same change is not re-proposed a year later by someone reading only the finding that motivated it.

Rungs 1 and 2 — knowledge base and wiki content — are exempt from the re-run requirement, because they add facts rather than change instructions. They are still recorded in § 11, since rung 1 alters the frozen prompt segment (§ 7). **Exemption from re-running is not exemption from sequencing**: whenever another step of the round reads the edited pages, see [`server_wiki_handoff`](../server_wiki_handoff/SKILL.md) § 4a for the gate that holds it until the user confirms that the change passed validation against the GnollHack source and the wiki session has finished.

**The re-run is specified, not just required.** Obligation 1 above is discharged by a `RUN` step in the round's Developer Runbook ([`server_benchmark_runbook`](../server_benchmark_runbook/SKILL.md) § 5): the exact launcher values, what must stay unchanged from the motivating run, the comparability tier the pair will land in, and each pre-declared criterion with the place in the report where it is read. "Re-run under the same configuration" without that step has been followed wrongly before.

---

## 10. Required Output

Any formal analysis of an AI benchmark run report or diagnostics **MUST** include a dedicated **Chat Transfer** section containing:
- **A summary in priority order** (§ *What the Benchmark Is For*): first what the run shows about the chat system and its tools, then what it shows about the benchmarking system, then the candidate's result.
- Table of triaged chat-transferable findings.
- Evidence from the report (dimensions, tool counts, Pearson $r$, or citations) — with every quoted prompt sentence verified against source per § 2.
- The proposed ladder rung (1 to 7).
- Evidence bar assessment (Single run / Motivated vs. Multi-run / Justified), **including the comparability assessment** from § 6.
- For any proposed change at rung 3 or above, the **pre-declared acceptance criterion** and **rollback trigger** required by § 9.
- For any rung-2 finding, **whether the round has a confirmation gate** ([`server_wiki_handoff`](../server_wiki_handoff/SKILL.md) § 4a) and which steps sit behind it, or the explicit statement that nothing in the round depends on the wiki change.

It **MUST** also include the tool-layer output that [`server_benchmark_tool_diagnostics`](../server_benchmark_tool_diagnostics/SKILL.md) § 10 mandates:

- **The diagnostic table**, one row per tool per run: Tool, Attempted, Succeeded, Derived failures, corpus fingerprint recorded / matches disk, Verdict (§ 4 there), Triage (§ 9 there), Evidence. For a harness-17 run, read Succeeded and Failed from the answer's `ToolCalls` rows rather than from `ToolCallSummary` and the derived-failure formula, and **say so in the Evidence column**.
- **The "Limits of this pass" statement** that skill also mandates — which run columns were read, which claims are reconstruction or replay and at which fidelity tier, and which corpora carry no fingerprint.

The columns of that table are defined in the diagnostics skill, so this item **cannot be satisfied without loading it** — which is the point. A prose cross-reference can be read and set aside; a required section of the deliverable cannot. Deliverable-shaped requirements survive context pressure and cross-references do not, which is exactly how the run-28 analysis shipped with its tool layer un-audited.

**The only escape** is an analysis that makes no tool, corpus or retrieval claim whatsoever. Note that a run report always contains a Tool Usage Profile, so this escape is close to theoretical; taking it requires saying so explicitly rather than omitting the section.

It **MUST** end in a **Developer Runbook** — `developer_runbook_v<N>.md`, a member of the round's document set, in the format [`server_benchmark_runbook`](../server_benchmark_runbook/SKILL.md) defines — and the chat message that delivers the round repeats its step list in short form. The runbook carries:

- **every action the round leaves to a human, in one dependency-ordered numbered list** — plan approval, the rubric repair import, the wiki handoff's validation and execution sessions, a `gnollhack_` plan, a knowledge-base push, the Overseer restart, roster or profile changes — each as a step of one numbered list in the order of doing;
- **one or more `RUN` steps in that same list**, each preceded by its pre-launch `CHECK` step and followed by its `SAVE` step — one run can be enough — each tagged with the purposes it serves, which are ranked (§ *What the Benchmark Is For*) and proposed, ordered and marked Required in that rank: **C** improve the main chat, **B** improve the benchmarking system, **M** decide which models perform best as the Overseer AI on intelligence, speed and cost — and **V**, validate this round's fixes, which takes the rank of the fixes it validates; and
- **a `SAVE` step after each run**: what to download, whether to go on, and a ready-to-paste prompt for the next analysis.

The step kinds and the fields of each are defined in that skill, so this item cannot be satisfied without loading it. Read it **once the findings are triaged and before the plan is written** — it is not one of the five up-front reads, because nothing in it can be applied before the findings exist. **An analysis with no fix to apply still owes a runbook**: its list says so in one line and consists of the next run's `CHECK`, `RUN` and `SAVE` steps, on purposes C, B or M, in that order of preference.

Any implementation plan derived from a benchmark run must replicate this section or explicitly state: *"No chat-transferable changes proposed in this plan."*

**All of it goes in one task directory.** The analysis, the server plan, a wiki handoff document, and any plan whose work is in another repository — named with that repository's prefix, e.g. `gnollhack_implementation_plan_v<N>.md` — are one document set under `hyvanmielenpelit/MobileGnollHackLogger/YYYY-MM-DD/<task_name>/`, never split across scopes. [`server_implementation_planning`](../server_implementation_planning/SKILL.md) § *One task directory per analysis* owns the rule.

**A finding that lands in GnollHack — a game snapshot improvement above all — never proposes a change to GnollHack's save-file layout.** That layout changes about once a year, in a major upgrade, so a new saved field is not a plan item, not even an optional one. Derive the text at export time, or record the fact as an `LL_AI` game-log entry and read it back; if only a layout change would serve, list it as deferred to the next major upgrade and schedule nothing. [`server_implementation_planning`](../server_implementation_planning/SKILL.md) § *A GnollHack plan never changes the save-file layout* owns the rule and says what counts.

---

## 11. Model Behaviour Notes (Accumulated Knowledge)

*This section records what reached the chat, the most recent rounds in full, and the cautions that
still bind; every benchmark analysis adds its round to it, using the schema that follows.*

### Changes That Reached the Chat

[`references/chat_change_ledger.md`](references/chat_change_ledger.md) lists every change that
reached the chat, at which rung, with its criterion, its outcome (kept, reverted, outstanding,
superseded, decided against) and its task directory. **Search it before proposing any change**
(§ 9 rule 4); a round adds its rows there.

### Entry Schema

```markdown
### Run <N> — YYYY-MM-DD: <Candidate model>
- **Candidate**: model version, thinking level
- **Prompt options**: the full BenchmarkCandidatePromptOptions record, plus parallelMode
- **Grading regime**: harness version, scoring method version, scoring profile,
  assessor roster, second-reader coverage, blind/anchored
- **Instrument SHAs**: commit SHA of Overseer/Services/ChatService.cs; of the
  Overseer/ToolGuides/ tree; of Overseer/appsettings.json; HEAD of the knowledge base repository
- **Quality**: dimensional scores and levels, Intelligence Index with CI, refuted claims
- **Speed**: mean and max model time, speed score, notable correlations
- **Cost**: tool call counts by family, token ratio, cache-read share
- **Transfer Action**: what was done, at which ladder rung, or what was deferred
- **Chat-side outcome**: each change that reached the chat, as a row of the chat change ledger, or
  "no chat change"
- **Verification Outcome**: for a prior run's change — criterion, result, kept or reverted
- **Analysis directory**: the round's task directory in the plans repository
```

**Instrument SHAs are mandatory.** The harness hashes the prompt text, not the commit that built
it, and fingerprints no tool limit (§ 6), so an entry without them cannot serve as half of a
two-run reproduction.

### Keeping This Section Small

- At most three full entries, each at most about 3 KB.
- When a fourth is added, the oldest is **retired, not folded**: its chat changes go into the
  chat change ledger, a boundary it crossed into the § 5 table, a lesson that binds into the method
  section or a standing caution, and a new verifier instance into the verifier ledger. Then the
  entry is deleted.
- Its full text stays in its task directory and in Git history.
- No fold log and no archive table are kept, here or in `references/`.
- If this file exceeds 80 KB, the round compresses before it appends.

### Earlier Runs

Runs 11 to 97 (battery run 11) were distilled into this skill on 2026-10-19: their chat changes
into the chat change ledger, their version boundaries into § 5, their lessons into the method sections
and the standing cautions below, and their verifier instances into the verifier ledger. Their
registry rows are at
`git show 94018d76:.agents/skills/server_benchmark_to_chat_transfer/SKILL.md`, § 11; a full entry
folded before then is in this file's Git history before the round that folded it; and each
round's own record is in its task directory under `hyvanmielenpelit/MobileGnollHackLogger/` in the
plans repository.

### Standing Cautions

- **Comparability keys move with their definition.** The scoring-profile key hashes
  `BenchmarkScoringProfileService.CanonicalSignature`, the profile's scoring semantics only (H9,
  2026-09-07), and comparability key definition version 2 (2026-09-24) moved the candidate and
  grader keys (§ 5). A group analysis stored before a definition change shows *Comparability
  definition changed* until it is re-analyzed, and its recorded `ComparabilityKeyHash` reflects
  the old definition until then. The budget key (`BudgetSignature`) was widened after runs 19–21,
  so a replicate series is extended with a fresh set, never with appended members.
- **A critical error is a grader judgment, not a fact.** It caps quality at 25, so it is the
  judgment most worth re-reading on disk. Spurious caps were published on runs 28 (Q3, rubric
  units), 31 (Q18, `src/engrave.c`), 35 (Q1, where the blind second opinion agreed with the false
  verdict), 39, 40, 43, 44, 45 and 57. `ContestedCriticalError` (harness 19: the assessor's
  `criticalErrorQuote` sent to the claim verifier) and `ContestedAccuracyDeduction` (harness 20:
  an out-of-rubric deduction's basis sent to it) are **advisory and say *contested*, never
  *overturned***; they move no score, cap or index. Read the flag, and the cited code path, before
  the count. From scoring method 13 a cap is confirmed before it applies (§ 5).
- **A claim-verifier verdict is advisory evidence, and a human reads the cited line before any
  change rests on it.** Sixty-nine wrong verdicts are recorded, from run 24 to battery run 11; the
  recurring shape is a citation of a default flag, a data table, a wiki page, a declaration or the
  wrong function that misses the code applying or overriding it. Instructions 3a–3m and the
  deterministic citation notes (no live call site, not in the indexed source, a file without a
  line, a definition line, a macro definition), which demote a verdict to Indeterminate and never
  to its opposite, answer most shapes. **Still caution-only**: a default flag or the wrong array
  element; a verdict citing the very lines that implement the claim; absence concluded from one
  place inside the wrong live function; a constant's name read as the claim's meaning; a function
  read without its callers; a descriptive adjective read as a species name and variable names read
  as semantics (one more instance of either would justify a new instruction); a recomputation from
  the wrong baseline despite 3g; a hedge refuted as false. Treat a refutation about
  GnollHack-specific behavior as unverified until a human reads the overriding code path, read
  what the verifier says it refuted before its citation, and distrust a **Supported** accused
  sentence most, because a wrong support clears a genuine error. Every instance, shape and guard
  is in [`references/verifier_caution_ledger.md`](references/verifier_caution_ledger.md): open it
  when a verdict is in doubt, when numbering a new instance (appended there) or before proposing a
  verifier instruction.
  - **The assessor-side class: a true, tool-grounded fact charged as fabricated.** The assessor
    grades without tools and never sees the candidate's tool results; on run 53 it called true
    GnollHack facts "invented" on seven of 18 answers. From harness 31 an accused sentence (an
    Accuracy deduction at 4 or below, or a contested verdict; from harness 38 at 5 too) goes to the
    verifier, widened to its whole sentence from harness 33, and a Supported verdict raises the
    advisory `ContestedAccuracyDeduction`. The check finds only accusations the assessor quoted, so
    an "invented" charge in evidence is unverified until someone reads the tool-call log and the
    source.
  - **Read a verdict's polarity from its role.** Before harness 33 the harness could send
    sentences of the assessor's *own* evidence to the verifier as candidate claims, so a Refuted
    verdict there meant the *candidate* was right yet counted as a refuted claim (run 55 Q15). From
    harness 33 those items carry the role `assessorStatement`. A report of an earlier run lists
    them as ordinary claims: read each refuted claim against the answer before counting it.
- **One deferred controlled run remains.** The verbose pair (one model, one suite, `verboseMode`
  false vs. true) was declined on 2026-09-27. The tool-policy variant run, to test whether the
  source-family-share-versus-latency correlation is causal, is open; when it runs, both halves sit
  on one side of every § 5 boundary.
- **Band drift was the difficulty prompt's in part, and its repair is a comparability break
  (harness 26).** From run 29 to run 41 every suite-6 run assessed its items 25–29 points above
  the authored bands. The run-41 round re-anchored `BenchmarkDifficultyPrompt` on the work an
  answer needs and re-assessed the whole suite, so no later suite-6 run is comparable with runs
  29–41 on the Intelligence Index (§ 5). The criterion was missed (8 of 18 outside the band, mean
  signed +12.8, against ≤ 6 and ± 10) and the residual is two-sided: Q13 and Q14 are single
  stats lookups authored Advanced, Q3 and Q5 need unit conversions and an `rnz` derivation and are
  authored Simple. Revising an authored band is a Fundamental change for a round of its own.
  **Do not re-edit the difficulty prompt to chase the residual.**
- **An Intelligence Index is a number on its assessor's scale, and the grader effect dwarfs the
  candidates (runs 43–46).** Four suite-6 runs at identical item revisions, assessed difficulties,
  prompt options and instrument SHAs gave 70 / 68 / 72 under GPT-5.6 Sol @ `medium` and **97**
  under Gemini 3.7 Flash @ `high`, and the blind second reader's sign flipped with the roster
  (higher on every re-graded answer under Sol, −13.3 under Gemini). Gemini 3.7 Flash @ `high` has
  produced 95 and 97 where Sol @ `medium` and Claude 5 Opus @ `low` produce 68–78 on the same
  suite. **No run graded by one may be ranked against a run graded by the other until the T5
  calibration re-assessment is done**; its pre-declared measurement is in
  `benchmark_runs_43_46_analysis/` (2026-09-12). The comparison view enforces this through the `Instrument`
  grader keys; the run history, the run report and the Run Detail dialog do not, and print a 97 in
  the same typography as a 70.

**Correction, 2026-09-10 (run-33 H1): every Gemini-role token and cost figure in this registry —
the earlier registry rows (§ *Earlier Runs*) and every entry recorded before the fix — is inflated.** Until the run-33 round `GoogleProvider`
emitted one usage report per streamed chunk and `AgentLoopRunner` summed them, so each Gemini model
call was counted roughly once per chunk (≈ 11× on run 33). That covers the primary assessor and the
synthesis on every run, and the candidate on run 22 — its tokens, its cost and possibly its 21.1 %
cache-read share. Anthropic and OpenAI figures are unaffected. Every "grading share" below overstates
the assessor, and a cost comparison across the fix is not like-for-like. The stored figures are not
rewritten; see `docs/overseer/ai-benchmark.md` § *Harness Version 18 Updates*.

**Correction, 2026-09-17 (harness-29 round, H1): runs 36, 37, 38, 42, 46, 47 and 51 are prompt-less
OpenAI runs.** `BuildCandidateSeedHistory` put the board (when present) ahead of the system prompt in
the history it built for a candidate call, and `OpenAiResponsesProvider` builds `instructions` from
the history's own system messages rather than from `SegmentedPrompt` — so from 2026-09-10 until the fix
an OpenAI candidate received **no system prompt at all**: on a suite with no board the seed carried no
system message, so `instructions` was omitted outright; on a snapshot suite the board was the only
system message, so the board became the entire system text. **Run 47 is one of them.** Its registry row (§ *Earlier Runs*)
records what the run measured, but its Intelligence Index (83 ± 5) is not an OpenAI-candidate
measurement under the production prompt, and **the 47–48 pair is not usable as a clean Tier A pair on
the model axis** — the two runs differ on more than the candidate model, and of the two only 48
(Gemini) reliably received the prompt the harness recorded as having been sent. See
`docs/overseer/ai-benchmark.md` § *Harness Version 29 Updates* for the full defect, the three-provider
breakdown, and the wire-level check now in place.

### Battery run 12 (runs 98, 99): 2026-10-08, Claude 5.5 Haiku (`xhigh`), suites 6 and 8
- **Candidate**: Claude 5.5 Haiku (`claude-haiku-5-5`) @ `xhigh`, default tier, parallel mode 2, max output 128,000; R1 of the battery run 11 round.
- **Prompt options**: mode 0, concise, tools, no web search, no subagents, source refs **Disallowed**, spoiler-free off, snapshot per suite, no history, no pre-injected wiki context.
- **Grading regime**: harness 54, scoring 14, Standard profile; panel A Claude 5 Opus @ `medium` (the candidate's family), B GPT-5.6 Sol @ `medium`; reference reader and claim verifier Gemini 3.8 Flash @ `medium`, every answer, blind; report writer GPT-6.1 Sol @ `medium`.
- **Instrument SHAs**: `ChatService.cs` `24710d18`, `Overseer/ToolGuides` `5ff6a192`, KB `d2af7241`; prompt `ce36c4b442c3` / `9f25393be387`; ToolGuides `733b710f7524`; wiki `4bb80dc5bfa1` (the battery run 11 W1–W4 commit), source `9df8a6993d7b`; WIKI-IDX `346bfbab651f`, SRC-IDX `caf9a8b06625`, KB-IDX `3d6d96512061`, NHW-IDX `e5dc755ab200`, NHS-IDX `c3929071a8d9`; suite 8 board 22,993 characters, format 8. NotComparable with battery run 11 (`HarnessVersion`; suite 8 Q5, Q14, Q18 and suite 6 Q18 item revisions and assessed difficulties).
- **Quality**: Overall Index 81.67 [79.17, 84.16]; run 98 83 (A 86, B 81, reference 92, ICC 0.48), run 99 80 (A 84, B 76, reference 90, ICC 0.25); pooled ICC 0.38, mean B − A −5.86; panel verification-cleared 82.37; critical errors 0 raised; claims 173: 139 supported, 11 refuted, 23 indeterminate; 10 supported accusations (H6); Completeness 67.8 lowest.
- **Speed**: pooled median model time 38.7 s, P90 85.4 s, max 147.9 s (run 98 Q18); TTFT P50 2.2 s; 1.5–1.6 calls per tool round; run 98 Q2 `iteration_limit` after 22 one-call rounds.
- **Cost**: US$9.23 per pass; candidate US$0.32 (US$0.0088 per question); claim verifier 40.3 %. 270 tool calls, 0 failed, 0 refused.
- **Verification Outcome (battery run 11 round)**: harness 54, the wiki W1–W4 commit and the four rubric repairs landed; K1, K7 vacuous; K2, K4–K6, K8, W-K1–W-K3, S-K1–S-K4 passed; **K3 failed** on run 98 Q16 (member B named source files as omissions).
- **Verifier caution**: run 99 Q15 again refuted the hedge "about 1500 turns" as false (instance 69's shape); run 98 Q8, Q9 lost to a Google 503 with no retry.
- **Transfer Action**: see `battery_run_12_analysis/implementation_plan_v2.md` — wiki W1 (wrong-alignment wield, two pages) and W2 (peaceful-kill Luck, two pages); harness 54 → 55: B1 source-location rule and its report-time count, B2 verifier stage retry, B3 the "inverts" word lists; Chat Consistency battery mode (analysis code version 4). No change to `ChatService.BuildSystemPrompt`, `Overseer/ToolGuides/` or `RecommendedModels`.
- **Still outstanding**: H6 (kept as recorded, decision of 2026-10-08); T6 *steals amulet* on 14 monster pages and in `src/do.c:5523` (deferred, decision of 2026-10-08); the suite 8 board refresh (format 8 while GnollHack writes 16), which also carries the run-73 rubric repairs S1–S2 deferred to it; the controlled verbose pair (declined 2026-09-27); NetHack wiki converter E1; T4 `get_monster_stats` near names; B8 verifier coverage on long answers; T3 stall watchdog; R2 (Haiku at a lower thinking level).

### Comparison #4 (battery runs 100–119): 2026-10-11, nine models, battery *Two initial suites*
- **Candidates**: GPT-6 Astra @ `medium`; GPT-6 Luna @ `max` and `high` (the reference); GPT-5.6 Luna @ `max` and `high`; Claude 5.5 Haiku @ `xhigh`; Claude 5.5 Sonnet @ `high` and `medium`; Claude 5.5 Opus @ `medium`. One run per suite each (18 member runs, 36 questions).
- **Prompt options**: concise, tools on, web search and subagents off, source references **Disallowed**; identical on every analyzed run.
- **Grading regime**: harness 55, scoring method 14, scoring profile 1; grader configurations 19 (member A, Anthropic) / 14 (member B, OpenAI) / 16 (reference reader) / 16 (claim verifier), blind. Root Cause Analysis document 63, format 18, engine 3, writer Gemini 4 Argon @ `medium`.
- **Instrument SHAs**: prompt `ce36c4b442c3` / `9f25393be387`; `ToolGuidesSha256` `733b710f7524` as the document prints it. The `ChatService.cs` commit and the knowledge-base HEAD were **not read in this round**: the document does not print them, and no corpus fingerprint either.
- **Quality**: GPT-6 Astra (medium) leads at 87 (85–89), the only model established above the reference GPT-6 Luna (high) at 83 (80–86); Claude 5.5 Sonnet (medium) last at 76 (72–80). Completeness the lowest dimension for every model. **Grader-family gap** (member B − member A, from the panel table): OpenAI candidates −1.2 on average, Anthropic candidates −6.1, so the OpenAI-over-Anthropic order depends on the panel's balance and no configured panel can test it today.
- **Speed**: median model time from 8.9 s (Sonnet `medium`) to 60.3 s (GPT-6 Luna `max`, 2.47× its own `high` setting).
- **Cost**: per question from US$0.0029 (GPT-6 Luna `high`, the cheapest) to US$0.194 (GPT-6 Astra, about 66.5 times the reference); Claude thinking was printed as zero reasoning (E5).
- **Transfer Action**: generator fixes in `comparison_4_rca_review/implementation_plan_v2.md` (Root Cause Analysis engine 4 → 5, comparison format 19 → 20, per-model format 14 → 15); no change to `ChatService.BuildSystemPrompt`, `Overseer/ToolGuides` or `RecommendedModels`. Claude 5.5 Sonnet (medium) answered 9 of 36 questions without any tool call (T1, routing, rung 6 then 3 after replication): logged, not acted on, one run per suite being motivating only.
- **Verification Outcome**: pending the runbook's R1 (the replicate of all nine models).
- **Round A** (2026-10-16, `comparison_4_rca_review/implementation_plan_A_v3.md`): battery panel diagnostics in the report path; price effects known from the cards; Appendix G limited to cited and critical-error questions. No chat change.
- **Round C** (2026-10-16, `comparison_4_rca_review/implementation_plan_C_v3.md`): sentence trimming and a shortening turn, honest added-by-Overseer notes, check summaries and the cost price note, sentence case in every document; progress cards. No chat change.

### Bones quest-artifact trap (runs not identified): 2026-10-18, cheaper models (not identified)
- **Candidates**: not read. The round started from the user's observation that every cheaper model answers "What quest artifacts can you find in bones files, if any?" wrongly. No run report, tool-call log or suite export was exported, so the runs, the models and their thinking levels are unidentified, and the plan's Stage 1 diagnosis (classes D1–D6) was not done.
- **Prompt options**: not read (no report).
- **Grading regime**: not read (no report).
- **Instrument SHAs**: none from a report, so **this entry cannot serve as half of a reproduction**. Repository state during the round, not of any run: `ChatService.cs` `53373897`, `Overseer/ToolGuides` `a3de3901`; GnollHack source HEAD `eb2f2a9e4d4f` (every cited file unchanged since `5db1d790e7d2`); GnollHack wiki HEAD `bc4ed286cd87` (`Bones Files.md` and `Quest Artifacts.md` unchanged since `33fb5526`). `ToolGuidesSha256`, the knowledge-base HEAD and the index fingerprints were not read.
- **Quality, Speed, Cost**: not measured; no run was read.
- **Ground truth** (verified at `eb2f2a9e4d4f`): on loading bones, an artifact reverts to an ordinary item when it already exists in the game or is the **loading** hero's quest artifact (`src/bones.c:140-203`, `src/questpgr.c:196-200`); a non-generable base becomes the artifact's counterpart item or a random item of its class (`src/bones.c:146-183`); another role's quest artifact stays real, blasts the hero on touch and evades a hero whose alignment also differs or whose record is negative (`src/artifact.c:863-964`). The deciding predicate reads as absolute and is relative to the current hero, the two-hop trap T1 names.
- **Transfer Action**: see `bones_quest_artifact_trap/implementation_plan_v2.md` — W1 at rung 2, `wiki_handoff_prompt_v2.md` written, validation pending (runbook Step 6); B1 the new suite *GnollHack Trap Questions* (`agent-new-suite-gnollhack-trap-questions.yaml`: bones A1–A4, control A5, held-out B1–B3; B2 narrowed to a monster that vanishes from the game, the one route the source settles); S1 open, the original rubric not checked (no export); T1, the rung-3 `_policy.md` sentence, deferred with its precondition, criterion and rollback; M1 deferred to R2. No change to `ChatService.BuildSystemPrompt`, `Overseer/ToolGuides/` or `RecommendedModels`.
- **Verification Outcome**: pending R0 and R1 (runbook Steps 8 and 14). W1 closes when R1 answers at least max(3, R0 + 2) of A1–A4 correctly in the report's own outcome class and reaches `Bones Files` or `Quest Artifacts` on at least 3 of them.
- **Still outstanding**: the Stage 1 diagnosis and the Stage 2 check of the original rubric, both waiting for the runbook Step 1 exports; the items battery run 12 lists.
---

## 12. Cross-References

- [`docs/overseer/ai-benchmark.md`](../../../docs/overseer/ai-benchmark.md) (§ 9 Limits Parity & § 9.1 What the Benchmark Tells the Chat)
- [`server_implementation_planning`](../server_implementation_planning/SKILL.md)
- [`overseer_chat_message_handling`](../overseer_chat_message_handling/SKILL.md)
- [`overseer_chat_response_timing`](../overseer_chat_response_timing/SKILL.md)
- [`tool_execution_architecture`](../tool_execution_architecture/SKILL.md)
- [`server_benchmark_tool_diagnostics`](../server_benchmark_tool_diagnostics/SKILL.md) — the tool-layer diagnostic method
- [`server_tool_data_sources`](../server_tool_data_sources/SKILL.md) — the corpora, their paths and what each index excludes
- [`server_tool_parameter_reference`](../server_tool_parameter_reference/SKILL.md) — the per-tool parameter and result contract
- [`server_wiki_handoff`](../server_wiki_handoff/SKILL.md) — pre-flight checklist, the three-step handoff document (proposed changes, source validation, execution) and its templates
- [`server_benchmark_runbook`](../server_benchmark_runbook/SKILL.md) — the Developer Runbook every analysis ends in: one numbered step list, the following runs included as steps
- [`references/chat_change_ledger.md`](references/chat_change_ledger.md) — every change that reached the chat and every decision not to make one, with rung, criterion, outcome and task directory (§ 9 rule 4, § 11)
- [`references/verifier_caution_ledger.md`](references/verifier_caution_ledger.md) — every recorded claim-verifier failure instance and shape, with the guard each has today (§ 11, Standing Cautions)
