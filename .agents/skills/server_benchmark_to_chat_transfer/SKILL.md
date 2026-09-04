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
  - T4: Knowledge base was called only once in 18 questions (`get_knowledge_article` under-use).
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
- **Transfer Action**: Seeded Knowledge Base Gap worklist with Q9 and Q16 (rung 1). Promoted Response Style control (T2) to allow testing `verboseMode: true` in Run 12. **T3 withdrawn** on 2026-09-05 — the prompt rule it cited does not exist (§ 2); reclassified as a Harness Defect and fixed in `BenchmarkReportBuilder`.
- **Verification Outcome**: n/a — no prior change under test.

---

## 12. Cross-References

- [`docs/overseer/ai-benchmark.md`](../../../docs/overseer/ai-benchmark.md) (§ 9 Limits Parity & § 9.1 What the Benchmark Tells the Chat)
- [`server_implementation_planning`](../server_implementation_planning/SKILL.md)
- [`overseer_chat_message_handling`](../overseer_chat_message_handling/SKILL.md)
- [`overseer_chat_response_timing`](../overseer_chat_response_timing/SKILL.md)
- [`tool_execution_architecture`](../tool_execution_architecture/SKILL.md)
