---
name: server_benchmark_to_chat_transfer
description: >-
  Mandatory method for turning Overseer AI benchmark findings into improvements to the
  production chat agent — better answer quality, lower latency, lower cost. Covers the fact
  that the benchmark grades the production chat system prompt, the required triage of every
  finding into harness defect / suite defect / chat-transferable, the evidence bar a finding
  must clear before any chat prompt is changed, the configuration-parity check, what the
  benchmark does not measure, the ordered ladder of safe changes from knowledge-base article
  up to prompt edit, the mandatory post-change verification and rollback rule, the
  anti-overfitting rules, and the per-run model behaviour notes this skill accumulates. Read
  before analysing any benchmark run report, diagnostics or assessment, and before writing
  any implementation plan derived from one.
---

# Benchmark to Chat Transfer: Turning Benchmark Findings into Chat Improvements

This skill defines the mandatory protocol for translating empirical findings from the Overseer AI Intelligence Benchmark into concrete improvements to the production chat agent — higher answer quality, reduced latency, and lower token costs — without overfitting the assistant to the benchmark suite.

---

## 1. The Structural Fact

The benchmark does not use a bespoke question-answering prompt. It builds the candidate's system prompt from the production chat builder, through a snapshotted configuration record:

```csharp
// Overseer/Services/Benchmarking/BenchmarkService.cs:262-270
var promptOptions = new BenchmarkCandidatePromptOptions
{
    VerboseMode = verboseMode,
    HasGameSnapshot = suiteHasBoard
};
run.CandidatePromptOptionsJson = promptOptions.ToCanonicalJson();
run.CandidatePromptSourceUsed = "ChatService.BuildSystemPrompt";

string systemPrompt = promptOptions.BuildSystemPrompt(_chatService, testedConfig.ParallelExecutionMode);
```

```csharp
// Overseer/Services/ChatService.cs:1395 — the production builder, reached from
// BenchmarkCandidatePromptOptions.cs:93-107
internal string BuildSystemPrompt(
    IEnumerable<string> wikiContext,
    bool spoilerFreeMode,
    bool verboseMode,
    bool isGameOn,
    bool developerMode,
    int overseerMode,
    bool hasGameSnapshot,
    bool hasMessageHistory,
    string? clientSettings,
    bool enableToolUse,
    bool enableWebSearch,
    bool allowSourceCodeReferences,
    bool enableSubAgents = false,
    ParallelExecutionMode parallelMode = ParallelExecutionMode.Enabled)
```

**The benchmark grades the production chat system prompt**, not an artificial or simplified test prompt. Every quality score, completeness deduction, hallucination finding, tool routing observation, and latency measurement in a benchmark report is an empirical measurement of what a real human player receives when asking assistance from the Overseer.

Transferring findings back to chat is therefore not an analogy or an extrapolation; it is the direct analysis of production prompt performance under test conditions.

Two qualifications that the phrase "verbatim production prompt" hides, and that every analysis must hold in view:

- **There are three call sites**, not one — `BenchmarkService.cs:270`, `:497` and `:3562` — and all of them route through `BenchmarkCandidatePromptOptions`. That record is the authoritative statement of what was graded; read it, not this section, when attributing a result.
- **"Verbatim" is true of the builder, not of every input.** `wikiContext` is always empty in a benchmark run, while live chat pre-injects wiki articles. The prompt-building *code* is identical; the prompt *text* a benchmark candidate sees is not the text a live user's model sees. See § 4.

---

## 2. The Mandatory Triage

Every finding produced by a benchmark analysis must be triaged into exactly one of three categories:

1. **Harness Defect**: The testing instrument itself is broken or flawed (e.g. grading biases, parser failures, broken reporting formulas, unhandled timeouts, missing backfills, report prose that misstates what the prompt says).
2. **Suite Defect**: The benchmark question, rubric ground truth, or difficulty rating is incorrect, ambiguous, or outdated.
3. **Chat-Transferable**: The defect or observation reflects authentic behavior that real users experience in chat (e.g. tool routing inefficiencies, prompt-induced brevity vs. completeness conflicts, knowledge gaps, latency inflation).

An analysis that fails to classify its findings into this taxonomy is incomplete.

> 🛑 **Verify every quotation before building a finding on it.**
>
> Benchmark reports contain hardcoded interpretive prose written by `BenchmarkReportBuilder`, not extracted from the prompt at run time. **Any prompt text a report quotes must be checked against `Overseer/Services/ChatService.cs` or `Overseer/ToolGuides/` before a finding rests on it.** A mismatch is a **Harness Defect**, not a chat finding.
>
> This rule exists because a finding was once built on a sentence the prompt had never contained — see the withdrawal of T3 below.

### Worked Example: Benchmark Run 11 (2026-09-04, GPT-5.6 Luna)
- **Harness Defects (F1–F8)**:
  - F1: Blind second opinion migration lacked default backfill (`SecondOpinionBlind = 0`), causing run 11 to run anchored.
  - F2: Claim verifier output parse error caused silent loss of verification without retry or raw text preservation.
  - F3: Brittle JSON parsing dropped valid model outputs.
  - F4: Substitution vocabulary ("instead of") triggered false-positive `OmissionAsAccuracy` detections.
  - F5: Grader disagreement direction was unrecorded (only absolute delta was tracked).
  - F6: Final synthesis prompt claimed the run was free of factual errors despite verified refuted claims.
  - F7: Report §5 formatting defects on omission lists and disputed tables.
  - F8: Second-opinion assessor and claim verifier shared the same model configuration without advisory notice.
- **Chat-Transferable Findings (T1–T6)**:
  - T1/T2: Completeness was lowest dimension (83.0 vs 97.7 Accuracy) because candidate was instructed to *"Default to 2–5 sentences per response"* (`verboseMode: false`).
  - **T3 — WITHDRAWN.** Source code tools accounted for 71% of tool calls, and the report described this as occurring despite a prompt rule *"Prefer wiki tools over source code tools"*. **No such rule exists.** That sentence was hardcoded in `BenchmarkReportBuilder.cs`, not read from the prompt. What `Overseer/ToolGuides/_policy.md` actually says is narrower and conditional: it prefers GnollHack tools over *web search*; it routes strategy and general "what is X" questions to wiki tools first; it routes **specific mechanics questions** (exact AC, damage dice, MR, resistances, speed, material, artifact flags) to the structured stats tools; and it places source code tools at rung 4 of a seven-rung hierarchy, for questions that require reading game logic. On a suite weighted toward mechanics, a high source share may be **prompt-compliant**. The routing-inefficiency reading is withdrawn; the correlation with turn latency ($r = 0.53$) survives as a **cost** observation. Reclassified: **Harness Defect**.
  - **T4 — WITHDRAWN.** Knowledge base was called only once in 18 questions (`get_knowledge_article` under-use), and the report described this as under-use. **The prompt explicitly directs the model to skip the knowledge base for game mechanics** (`Overseer/Services/ChatService.cs:1103` § "Information Routing", scoping it to app navigation, settings, controls, replay, vault, and troubleshooting). On an all-mechanics suite, zero calls is **prompt-compliant**. Reclassified: **Harness Defect** and fixed in `BenchmarkReportBuilder` by guarding the line on question topics.
  - T5: Refuted claims on Q9 (spell-skill saving throws) and Q16 (simultaneous attacker cap) revealed specific game mechanics misconceptions.
  - T6: Systematic framework required to guide chat prompt updates.

---

## 3. Configuration Parity — Check Before Attributing Anything

Before attributing any score, weakness, or behavior to a model's underlying intelligence or reasoning capabilities, **always inspect the Chat Prompt Under Test block in the report manifest**. It renders `BenchmarkRun.CandidatePromptOptionsJson`, which snapshots every field below.

| Option | Why it changes what was measured |
|---|---|
| `verboseMode` | Concise (`false`) instructs 2–5 sentences; directly caps Completeness |
| `spoilerFreeMode` | Filters what may be revealed, independent of what was retrieved |
| `overseerMode` | Selects the persona and task framing (`0` = Gameplay Help) |
| `enableToolUse` | With tools off, every answer is closed-book |
| `enableWebSearch` | Adds a provider-side retrieval path outside the tool runner |
| `allowSourceCodeReferences` | Gates whether source citations are permitted at all |
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
- Assessor roster, second-opinion mode, and blind vs. anchored

**The Golden Rule of Attribution**: A dimension may be depressed because the prompt instructed the model to answer that way. In run 11, Completeness (83.0) lagged Accuracy (97.7) by 14.7 points because the model obeyed the concise instruction *"Default to 2–5 sentences per response"*. Blaming the model for low completeness without checking `verboseMode` is an attribution error.

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
| Tools enabled, source references allowed, concise style | Any other combination of § 3's options |

> 🛑 **Tool-routing findings are the most exposed.** Every routing measurement was taken with
> `wikiContext` empty. Live chat hands the model wiki text before it decides anything —
> precisely the condition most likely to change which tool it reaches for. A routing finding
> is evidence about the benchmark configuration until a run with wiki context says otherwise.

The same caution applies in the other direction: a chat problem observed in a live session with history, wiki context or spoiler-free mode active is **not** contradicted by a benchmark run that scored well, because the benchmark never exercised that path.

---

## 5. The Three Transfer Axes

Benchmark reports provide quantitative diagnostic data across three distinct performance axes:

### Axis 1: Quality
- **Metrics to Read**: Dimensional averages, BARS levels (0–6), evidence strings (`accuracyEvidence`, `completenessEvidence`), critical errors, refuted claims, and disputed verdicts.
- **Dimension weights are per-run, not fixed.** Accuracy 55% / Completeness 25% / Conciseness 10% / Readability 10% are the **defaults** on `BenchmarkScoringConstants` (`BenchmarkScoring.cs:11-14`). The weights that actually applied come from the run's `BenchmarkScoringProfile` row, resolved by `BenchmarkScoringProfileService` and editable per profile. Comparing two runs under different profiles while assuming fixed weights mis-attributes a score shift to the model.
- **Interpretation**:
  - Low Accuracy indicates factual hallucination or outdated knowledge. Check if the claim was verified against source code.
  - Low Completeness under `verboseMode: false` indicates prompt adherence, not failure. Under `verboseMode: true`, it indicates an inability to retrieve or explain edge cases.
  - A Critical Error indicates dangerous misinformation (e.g. fatal tactical advice).

### Axis 2: Speed and Latency
- **Metrics to Read**: Model Time ($\text{DurationMs} - \text{ToolTimeMs}$), TTFT percentiles (P50, P90, max), and tool call counts.
- **Interpretation**:
  - Distinguish model deliberation time from tool network I/O.
  - Heavy tool usage (e.g. 5+ source searches per turn) adds seconds of I/O latency and degrades user experience.
  - The Speed Index is an advisory psychometric score across difficulty bands; compare raw Model Time, not Speed Index, when evaluating chat responsiveness.

### Axis 3: Cost and Token Efficiency
- **Metrics to Read**: Input:Output token ratio, prompt cache-read share, and tool call breakdown by family.
- **Interpretation**:
  - Run 11 demonstrated a 44:1 input:output ratio with a 90% prompt cache-read hit rate, confirming that Overseer's segmented system prompt (`ChatService.BuildSegmentedSystemPrompt` — frozen, session-stable, volatile) delivers massive cost savings.
  - Proliferation of source code tools significantly increases input token consumption due to large C code payloads.
  - Anything that changes the **frozen** segment invalidates that cache for every session. See § 7 rung 1 and rung 5.

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
- Scoring profile — weights, level scores, critical-error ceiling
- Assessor roster, second-opinion mode, and **blind vs. anchored**

> **Worked example — why the last one is not a footnote.** Run 11's F1 records that the blind
> second-opinion migration lacked a default backfill, so the run graded **anchored** while a
> later run under the same nominal settings would grade **blind**. That is a different
> grading regime producing different scores from an identical candidate, and nothing in the
> score itself reveals it. "Two runs" that differ here are two measurements, not a
> reproduction.

**Comparability Invariant** (from `docs/overseer/ai-benchmark.md` § 9.1): two runs are strictly comparable on **Completeness**, **Conciseness** and **Readability** only if their candidate prompt options match.

> ⚠️ **The harness does not fingerprint the prompt.** `BenchmarkRun` records
> `CandidatePromptOptionsJson` and `CandidatePromptSourceUsed` — but no hash of the prompt
> text, of `_policy.md`, or of the knowledge base topic list. A rung-1 or rung-3 change made
> between two runs is therefore **invisible in the run record**. Until that is fixed, the
> two-run bar is met only when the analyst has manually confirmed no intervening change to
> `Overseer/Services/ChatService.cs`, `Overseer/ToolGuides/`, or the KB repository. This is
> what the instrument-SHA field in § 11 exists to record.

Below this threshold, findings are logged in the **Model Behaviour Notes** (§ 11) and the prompt remains untouched.

---

## 7. The Ladder of Safe Changes

When an empirical chat-transferable finding clears the evidence bar, resolve it using the **lowest possible rung** on the ladder of safe changes:

1. **Knowledge Base Article (Lowest Cost, Safest)**:
   - When a model hallucinates a mechanic or has a claim refuted by the verifier (e.g. Q9 spell-skill mechanics, Q16 attacker cap).
   - In `ChatService.cs:1102`, the prompt explicitly instructs the agent that knowledge base articles take precedence over the wiki.
   - **Ships without a deploy.** The knowledge base is a **separate git repository** at the configured `KbPath`. `KnowledgeBaseService` polls its HEAD SHA every 10 minutes and reloads on change (`KnowledgeBaseService.cs:37-59`). Pushing an article is the whole deployment. This is the main reason this rung is first.
   - ⚠️ **It is not prompt-neutral.** The KB topic list is injected into the **frozen** prompt segment (`ChatService.cs:1093-1099`), so a new article changes the graded prompt and invalidates the frozen segment's cache for every session. It is a *smaller* instrument change, not *no* instrument change — record it between runs like any other (§ 11).
   - *Requirement*: Human authorship only; model outputs must never be ingested automatically as authoritative knowledge.
2. **Wiki Content Update**:
   - For factual omissions or ambiguities that belong in public NetHack/GnollHack documentation rather than specialized Overseer tips.
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

Rungs 1 and 2 — knowledge base and wiki content — are exempt from the re-run requirement, because they add facts rather than change instructions. They are still recorded in § 11, since rung 1 alters the frozen prompt segment (§ 7).

---

## 10. Required Output

Any formal analysis of an AI benchmark run report or diagnostics **MUST** include a dedicated **Chat Transfer** section containing:
- Table of triaged chat-transferable findings.
- Evidence from the report (dimensions, tool counts, Pearson $r$, or citations) — with every quoted prompt sentence verified against source per § 2.
- The proposed ladder rung (1 to 7).
- Evidence bar assessment (Single run / Motivated vs. Multi-run / Justified), **including the comparability assessment** from § 6.
- For any proposed change at rung 3 or above, the **pre-declared acceptance criterion** and **rollback trigger** required by § 9.

Any implementation plan derived from a benchmark run must replicate this section or explicitly state: *"No chat-transferable changes proposed in this plan."*

---

## 11. Model Behaviour Notes (Accumulated Knowledge)

*This section is an accumulating registry. Every benchmark analysis appends its run findings below, using the schema that follows.*

### Entry Schema

```markdown
### Run <N> — YYYY-MM-DD: <Candidate model>
- **Candidate**: model version, thinking level
- **Prompt options**: the full BenchmarkCandidatePromptOptions record, plus parallelMode
- **Grading regime**: harness version, scoring method version, scoring profile,
  assessor roster, second-opinion mode, blind/anchored
- **Instrument SHAs**: commit SHA of Overseer/Services/ChatService.cs; of the
  Overseer/ToolGuides/ tree; HEAD of the knowledge base repository
- **Quality**: dimensional scores and levels, Intelligence Index with CI, refuted claims
- **Speed**: mean and max model time, speed score, notable correlations
- **Cost**: tool call counts by family, token ratio, cache-read share
- **Transfer Action**: what was done, at which ladder rung, or what was deferred
- **Verification Outcome**: for a prior run's change — criterion, result, kept or reverted
```

**Instrument SHAs are mandatory.** They are the only record that the prompt did not move between two runs, because the harness does not hash it (§ 6). An entry without them cannot serve as half of a two-run reproduction.

**Pruning.** Once this section exceeds roughly ten entries, collapse everything older than the last three into a single summary table (run, date, model, Intelligence Index, transfer action) and keep full entries only for the most recent three. An unbounded registry pushes the method sections above it out of an agent's effective reading window, which defeats the purpose of the skill.

### Run 11 — 2026-09-04: GPT-5.6 Luna
- **Candidate**: GPT-5.6 Luna, thinking level `max`. 18 questions (Default Suite).
- **Prompt options**: `overseerMode: 0` (Gameplay Help), `verboseMode: false`, `spoilerFreeMode: false`, `enableToolUse: true`, `enableWebSearch: false`, `allowSourceCodeReferences: true`, `enableSubAgents: false`, `isGameOn: false`, `developerMode: false`, `hasMessageHistory: false`, `hasWikiContext: false`, `hasGameSnapshot: false`. `parallelMode`: not recorded.
- **Grading regime**: harness version 11; **anchored** second opinion (unintentionally — see F1, the missing `SecondOpinionBlind` backfill). Scoring profile, assessor roster and second-opinion mode: not recorded.
- **Instrument SHAs**: not recorded — this entry predates the requirement, and the run executed on another machine whose deployed commit cannot be recovered. **Run 11 therefore cannot serve as half of a two-run reproduction** for any finding sensitive to prompt text.
- **Quality**: Accuracy 97.7 / Level 5.8; Completeness 83.0 / Level 4.8; Conciseness 95.0; Readability 95.0. Intelligence Index: 91.5 $\pm$ 5.9 (95% CI). Response-style conflict confirmed (14.7 pt gap). Refuted claims on Q9 (`src/zap.c:359-364`, spell skill) and Q16 (`src/makemon.c:110-129`, simultaneous attackers).
- **Speed**: Mean model time 68.3s (max 179.9s on Q18). Speed score 56.6. Correlation between source tool share and model latency ($r = 0.53$).
- **Cost**: 293 total tool calls. Source Code: 208 calls (71.0%), Wiki: 77 calls (26.3%), Structured Lookup: 7 calls (2.4%), Knowledge Base: 1 call (0.3%). Zero-KB answers: 17 of 18 questions. Token ratio: 44:1 input:output with 90% cache-read share.
- **Transfer Action**: Seeded Knowledge Base Gap worklist with Q9 and Q16 (rung 1). Promoted Response Style control (T2) to allow testing `verboseMode: true` in Run 12. **T3 withdrawn** on 2026-09-05 — the prompt rule it cited does not exist (§ 2); reclassified as a Harness Defect and fixed in `BenchmarkReportBuilder`. **T4 withdrawn** on 2026-09-05 — the prompt explicitly scopes the knowledge base away from game mechanics (`ChatService.cs:1103`); reclassified as a Harness Defect and line qualified in report.
- **Verification Outcome**: n/a — no prior change under test.

### Run 12 — 2026-09-05: GPT-5.6 Luna
- **Candidate**: GPT-5.6 Luna, thinking level `max`. 18 questions (Default Suite 5).
- **Prompt options**: `overseerMode: 0` (Gameplay Help), `verboseMode: true` (Detailed), `spoilerFreeMode: false`, `enableToolUse: true`, `enableWebSearch: false`, `allowSourceCodeReferences: true`, `enableSubAgents: false`, `isGameOn: false`, `developerMode: false`, `hasMessageHistory: false`, `hasWikiContext: false`, `hasGameSnapshot: false`. `parallelMode`: Enabled (`ParallelExecutionMode.Enabled`).
- **Grading regime**: harness version 12; scoring method version 7; scoring profile Standard Intelligence Index (Default); assessor Gemini 3.7 Flash (`high`); second opinion Claude 5 Opus, blind, mode Flagged.
- **Instrument SHAs**:
  - `CandidateSystemPromptSha256`: recorded via `BenchmarkRun.CandidateSystemPromptSha256`
  - `ToolGuidesSha256`: recorded via `BenchmarkRun.ToolGuidesSha256`
  - `KnowledgeBaseHeadSha`: recorded via `BenchmarkRun.KnowledgeBaseHeadSha`
- **Quality**: Accuracy 93.5 / Level 5.6; Completeness 83.8 / Level 4.9; Conciseness 89.9; Readability 97.1. Intelligence Index: 91 $\pm$ 7 (95% CI). Refuted claims: 0. Contested verdicts: 1 (Q12, split on critical error; sensitivity index ≈ 90).
- **Speed**: Mean model time 87.2 s (max 183.2 s on Q18). Speed score 46. Median TTFT: 2,236 ms. Pearson $r = 0.65$ between source tool share and model time; $r = -0.19$ with quality score.
- **Cost**: 334 total tool calls. Source Code: 237 calls (71.0%), Wiki: 91 calls (27.2%), Structured Lookup: 6 calls (1.8%), Knowledge Base: 0 calls (0.0%). Zero-KB answers: 18 of 18 questions (prompt-compliant per `ChatService.cs:1103`). Token ratio: 28.4:1 input:output (4,301,234 in / 151,373 out) with 90.1% cache-read share (3,873,723 cached).
- **Transfer Action**:
  - **T7**: `verboseMode: true` bought no Completeness (83.0 → 83.8) and cost 28% latency and 14% tool calls. Refuted T1/T2 hypothesis. Concise production default kept, change no prompt text.
  - **T8**: Promoted to implementation (rung 3, heading-scoped wiki snippets in `WikiSnippetExtractor` and `WikiSearchTool`, default 5 results, 2,500 chars/snippet). Pre-declared acceptance criterion: Wiki share ≥ 35%, Source share ≤ 60%, total tool calls ≤ 300, total input tokens ≤ 3.4M, Q12 quality score ≥ 70 with no fabricated claims, budget-pressured questions ≤ 1.
  - **T9**: Confident fabrication under retrieval failure (Q12). Deferred pending T8 verification (rung 7 prose change deferred).
  - **T10**: Input token amplification (28.4:1) attacked via T8 and measured via H7 cost reporting.
- **Verification Outcome**: Run 11 promoted T2 (`verboseMode: true`): criterion — Completeness rises materially under `verboseMode: true`; result — 83.0 → 83.8, inside noise, while Accuracy fell 4.2 and mean model time rose 28 %; hypothesis refuted, the concise default is kept.

### Run 13 — 2026-09-05: GPT-5.6 Luna
- **Candidate**: GPT-5.6 Luna, thinking level `max`. 18 questions (Default Suite 5).
- **Prompt options**: `overseerMode: 0`, `verboseMode: true`, `spoilerFreeMode: false`,
  `enableToolUse: true`, `enableWebSearch: false`, `allowSourceCodeReferences: true`,
  `enableSubAgents: false`, `isGameOn: false`, `developerMode: false`, `hasMessageHistory: false`,
  `hasWikiContext: false`, `hasGameSnapshot: false`. `parallelMode`: Enabled (2).
- **Grading regime**: harness version 12; scoring method version 7; profile Standard Intelligence
  Index (Default); assessor Gemini 3.7 Flash (`high`); second opinion Claude 5 Opus, blind, mode
  Flagged, threshold 50, outlier delta 25; claim verifier Claude 5 Opus (`high`).
- **Instrument SHAs**:
  - `CandidateSystemPromptSha256`: `e9b3e9a75278a5cbd09fbe3afb270806a3be318863c9c3e1623897428a1c16c6`
  - `ToolGuidesSha256`: `f59d8b30d2c2855931275fb0965f434db8ceb20feba84b4a5ac86eb65734f4a9`
  - `KnowledgeBaseHeadSha`: `576ca5741d1bd79ef1cb2f7db575709cf0bb0db8`
- **Quality**: Accuracy 95.7 / L 5.7; Completeness 84.8 / L 4.9; Conciseness 91.2; Readability
  96.4. Intelligence Index 94 ± 4 (95 % CI); unweighted mean 91; holistic 91. Critical errors: 1
  (Q1, fabricated lycanthropy immunity). Refuted claims 0; unverified claims 3 across Q1 and Q7,
  all 3 supported by the verifier. Contested verdicts 0. Advisory flags 3 (reasoning bleed 3,
  repeated fragments 1) on Q3, Q5, Q13.
- **Speed**: mean model time 118.9 s (max 279.7 s, Q13); Speed Index 61 (advisory — profile targets
  15 000 ms, candidate ran at `max`); median TTFT 2 931 ms. *r* = 0.68 source share vs. model time;
  *r* = +0.11 vs. quality.
- **Cost**: 305 tool calls — Source 198 (64.9 %), Wiki 98 (32.1 %), Structured Lookup 8 (2.6 %),
  Knowledge Base 1 (0.3 %). Zero-KB answers 17 of 18 (prompt-compliant). Token ratio 33.5 : 1
  (5 182 903 in / 154 555 out), cache-read share 90.5 %. Estimated run cost $1.38 — candidate
  $0.38 (28 %), assessor $0.51, verifier $0.49.
- **Limits change made after this run**: in response to S2 (Q7 and Q11 both at 32/35 and both below
  the run mean), the per-question resource caps were **unbanded** — `ToolCallBudget` 25/35/45 → **45
  flat**, `ToolIterations` 12/16/22 → **22 flat**, `TotalModelCalls` 16/22/28 → **28 flat**, matching
  production. `QuestionTimeoutSeconds` stays banded at 420/600/720 because it is pinned to the
  speed-score floor. **Runs after this point are not strictly comparable with runs 1–13 on
  Completeness.**
- **Transfer Action**: T11 (fabrication under *partial* retrieval failure) deferred — second
  observation, but the pair is not a reproduction (`ToolGuidesSha256` moved with T8); prompt
  already forbids it at `ChatService.cs:1220/1230/1241`, so no rung-7 edit. T12 handed to the wiki
  repository (rung 2, exempt). T13/T14 answered with measurement (H1, H5) rather than instruction.
- **Verification Outcome**: **T8 (heading-scoped wiki snippets, rung 3) — criterion met on 1 of 6
  measures.** Wiki share 32.1 % (target ≥ 35 %), source share 64.9 % (≤ 60 %), tool calls 305
  (≤ 300), input tokens 5.18 M (≤ 3.4 M, *wrong direction*), budget-pressured questions 2 (≤ 1);
  Q12 100 with no fabrication ✔. Side-effect check clean in the candidate's favour (Accuracy +2.2,
  Index +3). Rollback trigger fires as written; **kept** by explicit decision — four of five misses
  moved the intended way and the input-token miss is on an axis T8 does not control. Criterion
  re-baselined; input-token attribution moved to harness finding H1.

### Run 14 — 2026-09-06: GPT-5.6 Luna
- **Candidate**: GPT-5.6 Luna (`gpt-5.6-luna`), thinking level `max`, reasoning `standard`, service
  tier Default, parallel tool calls on. 18 questions (Default Suite 5).
- **Prompt options**: `overseerMode: 0`, `verboseMode: true`, `spoilerFreeMode: false`,
  `enableToolUse: true`, `enableWebSearch: false`, `allowSourceCodeReferences: true`,
  `enableSubAgents: false`, `isGameOn: false`, `developerMode: false`, `hasMessageHistory: false`,
  `hasWikiContext: false`, `hasGameSnapshot: false`. `parallelMode`: Enabled (2).
- **Grading regime**: harness version 12; scoring method version 7; profile Standard Intelligence
  Index (Default); assessor Gemini 3.7 Flash (`high`); second opinion Claude 5 Opus, blind, mode
  Flagged, threshold 50, outlier delta 25; claim verifier Claude 5 Opus (`high`). Per-question caps
  unbanded (ToolCallBudget 45, ToolIterations 22, TotalModelCalls 28 flat) as of the post-run-13
  change.
- **Instrument SHAs** — identical to run 13 on all three:
  - `CandidateSystemPromptSha256`: `e9b3e9a75278a5cbd09fbe3afb270806a3be318863c9c3e1623897428a1c16c6`
  - `ToolGuidesSha256`: `f59d8b30d2c2855931275fb0965f434db8ceb20feba84b4a5ac86eb65734f4a9`
  - `KnowledgeBaseHeadSha`: `576ca5741d1bd79ef1cb2f7db575709cf0bb0db8`
- **Quality**: Accuracy 96.3; Completeness 86.2; Conciseness 90.5; Readability 96.4. Intelligence
  Index **94 ± 3** (95 % CI); unweighted mean 93; holistic 93. Critical errors 0; refuted claims 0;
  10 unverified claims across Q1, Q4 and Q5, **all 10 returned supported** by the verifier.
  Accuracy deductions with named defects on **Q11** (0-turn weapon swap) and **Q14** (difficulty 40
  reported as level, against `LVL(25, 16, -10, 15, 10, -20)`). Completeness was again the lowest
  dimension — a fourth consecutive run — trailing Accuracy by 10.1 points.
- **Speed**: mean model time 82.6 s (run 13: 118.9 s, −30 % on an unchanged instrument); Speed
  Index 72 (advisory). *r* = **0.86** source-family share vs. model time (run 13: 0.68);
  *r* = **−0.05** vs. quality — more source calls buy time, not accuracy.
- **Cost**: 281 tool calls — Source 56.2 %, Wiki 40.9 %, Structured Lookup 2.5 %, Knowledge Base
  0.4 %. Input 4.21 M (−18.8 %), output 117.6 k (−24 %), ratio 35.8 : 1, cache-read share 90.4 %.
  Q18, Q16 and Q13 alone are **50.8 %** of input tokens at 22–23 model calls each. Estimated run
  cost **$2.53** — candidate $0.30 (12 %), assessor $0.54 (21 %), **claim verifier $1.70 (67 %)**
  for 10 claims checked and 0 refuted.
- **Transfer Action**:
  - **T15**: `get_monster_stats.md`'s *"only fall back to this tool if the wiki lacks data"* clause
    contradicts `_policy.md`'s exact-stats routing rule; Q14 routed to `monster_lookup` alone and
    reported Master Kaen's difficulty as his level. **Promoted at rung 3** (tool guide text), with
    the four § 9 obligations discharged against **3-run replicate sets on both sides** rather than
    one run per side.
  - **T16**: Completeness lowest in all four runs. **Deferred, no chat prompt change** — the cause
    is unattributed and `verboseMode: true` was already refuted at run 12 (T7). The instrument side
    is attacked by the completeness-scope grading rule and its out-of-scope counter; the model side
    stays unaddressed until multi-run says the gap survives with the instrument corrected.
  - **T18**: chat input-token cost is driven by model-call count, not tool-call count. **Answered
    with measurement, not instruction** — the batching policy is working and adding to it would be
    overfitting.
- **Verification Outcome**: **T8 (heading-scoped wiki snippets, rung 3) — kept.** Criterion now met
  on **four of six** measures where run 13 met one: wiki share 40.9 % (≥ 35 % ✔), source share
  56.2 % (≤ 60 % ✔), tool calls 281 (≤ 300 ✔), Q12 97 with no fabrication (✔); input tokens 4.21 M
  (≤ 3.4 M ✘, −18.8 %, right direction) and budget-pressured questions 2 (≤ 1 ✘, both now well
  inside a 45-call flat budget). Side-effect check clean and in the candidate's favour: Accuracy
  +0.6, Completeness +1.4, Index unchanged at 94 with a tighter interval, mean model time −30 %,
  cache-read share flat. **Both remaining misses moved the intended way.**
- **Comparability reset.** The plan derived from this run bumps `ScoringMethodVersion` **7 → 8** and
  edits the *Standard Intelligence Index (Default)* profile in place. Runs 11–14 therefore keep
  their value as **observations** and cease to be usable as **reproduction halves**: the two-run bar
  in § 6 resets, and the runs 12–14 comparable series ends here.

### Runs 16–18 — 2026-09-07: GPT-5.6 Luna (first R=3 replicate set)
- **Candidate**: GPT-5.6 Luna. 18 questions (Suite 5). Series 2, sequential, 1 h 38 m 06 s.
- **Prompt options**: **not recorded in this analysis** — the multi-run report rendered no Chat
  Prompt Under Test block (finding M5, fixed by the plan derived from this run).
  `CandidatePromptOptions` matched across all three members, so they agree with each other; the
  values must be read from the run rows before any dimensional claim rests on this entry. **This
  entry cannot serve as half of a two-run reproduction on any dimension-sensitive finding until that
  is done.**
- **Grading regime**: harness version 12; scoring method version 8; assessor, second-opinion and
  claim-verifier configurations all matched across members but not rendered by value.
- **Instrument SHAs** — identical across all three members:
  - `CandidateSystemPromptSha256`: `bb19dc24e28755228647960efc5c6aa70cfed64262da29a4a5079181acf5753b`
  - `ToolGuidesSha256`: `9c79137965e4fe19e5cb2faea71ed29598ff032a7f3a653d8504ffd8a91ea168`
  - `KnowledgeBaseHeadSha`: `576ca5741d1bd79ef1cb2f7db575709cf0bb0db8`
- **Quality**: Multi-Run Intelligence Index **94.42**, combined 95 % interval [91.74, 97.11]
  (± 2.69). Per-run 94.59 / 94.60 / 94.07. Reproducibility SD **0.30** (half-width 0.75);
  item-sampling half-width 2.58 — the dominant term, and invariant in *R*. Critical errors 0 on every
  item in every run. Per-dimension scores **unavailable** (M4). Unstable items: Q1 only
  (mean 71.0, SD 23.3, min 45, max 90).
- **Speed**: mean Speed Index 72.3 ± 2.1 (70 / 74 / 73). Pooled over 54 answers: P50 72.2 s,
  P90 168.2 s, max 220.8 s. Slowest by median model time: Q18 206.1 s, Q13 123.6 s, Q11 121.6 s.
- **Cost**: total $12.37 over three runs; mean $4.12 ± **$1.57** (CV 38 %, against the index's
  0.3 %) — $3.4582 / $2.9969 / $5.9154. Per role: claim verifier $9.83 (79 %), assessor $1.63
  (13 %), candidate $0.9073 (7 %). $0.2291 per question, $0.0437 per index point. Token and
  tool-family aggregates **unavailable** (M6).
- **Comparability**: recorded as Tier B — Quality-comparable, 22 of 23 keys matched, the sole
  difference being `PricingSnapshot`. **That difference was a harness defect, not a condition of the
  runs:** `capturedAtUtc = DateTime.UtcNow` inside the hashed snapshot JSON made Tier A structurally
  unreachable for every series ever run and falsely degraded every multi-run cost aggregate. The
  harness reported it about itself as **Sentry OVERSEER-8** at the moment the series completed. Fixed
  on 2026-09-07; re-analysing this group resolves Tier A. **The runs are a genuine replicate set and
  their quality aggregates were always sound.**
- **Transfer Action**: **T19** (Q1, the Gnoll-race item) — rubric check first, then a
  human-authored knowledge base article, rung 1; **no prompt change**, and the article is *not* an
  agent task: rung 1 requires human authorship. **T20** (pooled P90 latency 168 s) deferred, its
  parity check blocked by M5. **T21** — the claim verifier's 79 % of spend is explicitly ruled **not
  chat-transferable**: it is a grading role with no counterpart in the chat request path, and the
  chat-relevant figure here is the candidate's $0.3024 per run. **T22**: run 14's deferred T16
  (Completeness lowest four runs running) was still unmeasurable, which is what motivated M4.
- **Verification Outcome**: n/a — no prior chat change was under test. What the set did verify is the
  instrument: reproducibility SD 0.30 means a re-run of this configuration moves the index by well
  under a point, so a future difference above ~1 point is signal rather than noise.

---

## 12. Cross-References

- [`docs/overseer/ai-benchmark.md`](../../../docs/overseer/ai-benchmark.md) (§ 9 Limits Parity & § 9.1 What the Benchmark Tells the Chat)
- [`server_implementation_planning`](../server_implementation_planning/SKILL.md)
- [`overseer_chat_message_handling`](../overseer_chat_message_handling/SKILL.md)
- [`overseer_chat_response_timing`](../overseer_chat_response_timing/SKILL.md)
- [`tool_execution_architecture`](../tool_execution_architecture/SKILL.md)
