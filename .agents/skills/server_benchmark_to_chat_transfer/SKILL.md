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
  anti-overfitting rules, the fourth triage category for a corpus or environment defect, the
  route into tool-layer diagnostics when a finding turns on what a tool returned, and the
  per-run model behaviour notes this skill accumulates. Read before analysing any benchmark
  run report, diagnostics or assessment, and before writing any implementation plan derived
  from one.
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

Every finding produced by a benchmark analysis must be triaged into exactly one of four categories:

1. **Harness Defect**: The testing instrument itself is broken or flawed (e.g. grading biases, parser failures, broken reporting formulas, unhandled timeouts, missing backfills, report prose that misstates what the prompt says).
2. **Suite Defect**: The benchmark question, rubric ground truth, or difficulty rating is incorrect, ambiguous, or outdated.
3. **Chat-Transferable**: The defect or observation reflects authentic behavior that real users experience in chat (e.g. tool routing inefficiencies, prompt-induced brevity vs. completeness conflicts, knowledge gaps, latency inflation).
4. **Corpus / Environment Defect**: the corpus a tool reads was missing, stale, outside the indexed scope, excluded by a size limit, or still indexing on the machine that ran the benchmark. The instrument is sound and the suite is sound; the *data the tool reads* was not what it is in production. Diagnose with [`server_benchmark_tool_diagnostics`](../server_benchmark_tool_diagnostics/SKILL.md) before filing a finding in any other category.

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

## 4a. Tool-Layer Diagnostics

A benchmark run executes the **production tool registry**, so a tool defect seen in a run is a defect a real user hits. What a run records about its tool calls **depends on its harness version**, and four facts govern every tool finding:

- **Whether arguments and results were stored is a version boundary — check the run's harness version before anything else.** For a run **before harness 17** they were not stored: `BenchmarkRunAnswer.ToolCallSummary` is a `name×count` string over *successful* calls only, `AgentRunRequest.ShowDebugLog` is hardcoded `false` at every benchmark call site, and a run creates no `ChatMessage` rows, so nothing equivalent to `ChatMessageToolCall.ArgsText` / `.Result` exists. For such a run, *"compare the parameters and results"* is **reconstruction and replay**, never transcript reading. **From harness 17** a run's `BenchmarkRunAnswer.ToolCalls` rows carry the real `ArgsText`, `Result`, `Error`, `Status`, emission order (`SortOrder`), tool round (`IterationIndex`), timings and the result size as `ToolExecutor` handed it over (`ResultLengthChars`) for **every attempted call**, read through `GET /api/admin/benchmark/runs/{id}/answers/{answerId}/tool-calls` (admin-authenticated). Reading those rows is **rung zero** — attempted before any reconstruction or replay (`server_benchmark_tool_diagnostics` § 2, § 7). A row whose payload the retention sweep pruned shows `ArgsText`/`Result` null beside a non-zero `ResultLengthChars`; that is the sweep, not an absent record.
- **The count of failed tool calls is derived for a run before harness 17.** `ToolCallCount − Σ(ToolCallSummary counts) − ToolCallsBlocked` is the number of calls that errored technically. No report section surfaces it, and a non-zero value is direct evidence of a tool problem. Compute it first. A null `ToolCallsBlocked` means *not recorded*, never zero. **From harness 17 the figure is reported** as the answer's three-way outcome split, so read it rather than deriving what the record already states.
- **From harness 16 a run fingerprints three of the five corpora** — knowledge base, GnollHack wiki, GnollHack source. The NetHack wiki and NetHack source are **not** fingerprinted and are both reachable from a run. These fingerprints are **provenance, not comparability keys**: a difference is a fact to investigate, not an automatic tier drop.
- **Version currency.** The bullets above were last checked against harness **17**. `BenchmarkAssessmentPrompt.HarnessVersion` is the source of truth for the current value; if it now reads higher, treat this section as possibly aged and verify every claim against `server_benchmark_tool_diagnostics` § 2 before relying on it. This section silently aged out at harness 17 once already, and cost a run-28 analysis its tool-layer evidence — that is why this line exists.

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
> The useful corollary: **`e9b3e9a752…` and `bb19dc24…` are a known isolated `verboseMode` pair on
> unchanged code**, which is exactly the controlled pair bar 2 above accepts in place of two
> comparable runs — and what makes D5(a) cheap to run.

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
   - ⚠️ **Game mechanics do not belong on this rung.** The frozen segment's Information Routing section (`ChatService.cs:1193`) tells the model: *"For game mechanics, monsters, items, spells, or other topics NOT listed above: skip the knowledge base entirely and go directly to `wiki_search`, `monster_lookup`, or `item_lookup`."* An article about a race, a monster or a mechanic therefore sits in a store the model is instructed not to consult, and making it reachable means widening the topic list — which is in the **frozen** segment, so it invalidates the prompt cache for **every** session. Overseer's cache-read share is 90–91 % on Anthropic and OpenAI runs, so that is a real recurring cost. **The correct rung for game-mechanics content is 2**, and the standing rung-1 Gnoll-race article recorded across runs 16–22 is reclassified accordingly.
2. **Wiki Content Update**:
   - For factual omissions or ambiguities that belong in public NetHack/GnollHack documentation rather than specialized Overseer tips.
   - **This is the rung with the awkward deploy path, and it is still the right one.** The knowledge base at `C:\hmp\overseer_knowledgebase` is a git repository (`hyvanmielenpelit/OverseerKnowledgeBase`) that reloads on a 10-minute HEAD poll; the wiki mirror at `C:\hmp\nethackwiki` is **not** a git repository, and per `.agents/AGENTS.md` it needs a manual file upload plus an Overseer restart to re-index. So the rung with the clean deploy path is the one the prompt tells the model to skip, and the rung the prompt actually routes to is an unversioned directory. Content still goes here; prefer authoring upstream on the GnollHack wiki rather than only in the local mirror, since a hand-added section in a mirror of a third-party wiki is one re-sync away from being erased.
   - The Gnoll-race gap has now been raised on runs 16–18 (T19), 19–21 (T-B), 22 (S2/Q1) and 24 (T6). Before counting those as four confirmations, check whether they are the same suite item: runs 16–22 ran "Suite 5" and runs 24–25 ran the "GnollHack Player Assistance Benchmark Suite". The verified source facts are in the run 24 plan's Appendix A, so nobody re-derives them from C — see the verifier caution in § 11.
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

It **MUST** also include the tool-layer output that [`server_benchmark_tool_diagnostics`](../server_benchmark_tool_diagnostics/SKILL.md) § 10 mandates:

- **The diagnostic table**, one row per tool per run: Tool, Attempted, Succeeded, Derived failures, corpus fingerprint recorded / matches disk, Verdict (§ 4 there), Triage (§ 9 there), Evidence. For a harness-17 run, read Succeeded and Failed from the answer's `ToolCalls` rows rather than from `ToolCallSummary` and the derived-failure formula, and **say so in the Evidence column**.
- **The "Limits of this pass" statement** that skill also mandates — which run columns were read, which claims are reconstruction or replay and at which fidelity tier, and which corpora carry no fingerprint.

The columns of that table are defined in the diagnostics skill, so this item **cannot be satisfied without loading it** — which is the point. A prose cross-reference can be read and set aside; a required section of the deliverable cannot. Deliverable-shaped requirements survive context pressure and cross-references do not, which is exactly how the run-28 analysis shipped with its tool layer un-audited.

**The only escape** is an analysis that makes no tool, corpus or retrieval claim whatsoever. Note that a run report always contains a Tool Usage Profile, so this escape is close to theoretical; taking it requires saying so explicitly rather than omitting the section.

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

### Runs 11–21 — 2026-09-04 to 2026-09-07 (pruned per the rule above)

Full entries were collapsed on 2026-09-08 when this registry passed the pruning threshold. What each run established is preserved below; the reports themselves remain the primary source.

| Run(s) | Date | Candidate | Intelligence Index | Transfer action, and what it settled |
|---|---|---|---|---|
| 11 | 2026-09-04 | GPT-5.6 Luna (`max`) | 91.5 ± 5.9 | Seeded the rung-1 knowledge base worklist (Q9, Q16) and promoted `verboseMode: true` for testing at run 12. **T3 and T4 were later withdrawn** — both cited prompt rules that do not exist, and were reclassified as harness defects. Graded **anchored** through a missing blind-second-opinion backfill (F1), and its instrument SHAs were never recorded, so **run 11 cannot serve as half of any reproduction.** |
| 12 | 2026-09-05 | GPT-5.6 Luna (`max`) | 91 ± 7 | **T7: `verboseMode: true` refuted.** Completeness moved 83.0 → 83.8 — inside noise — while Accuracy fell 4.2 and mean model time rose 28 %. The concise production default was kept, and has been kept at every re-test since. **T8** (heading-scoped wiki snippets) promoted to rung 3 with a six-measure pre-declared criterion. |
| 13 | 2026-09-05 | GPT-5.6 Luna (`max`) | 94 ± 4 | T11 (fabrication under *partial* retrieval failure) deferred pending a second observation. |
| 14 | 2026-09-06 | GPT-5.6 Luna (`max`) | 94 ± 3 | **T8 verified and kept**: four of six measures met against run 13's one, and both remaining misses moved the intended way. **T15** promoted at rung 3 (`get_monster_stats.md` contradicted `_policy.md`'s exact-stats routing). **T16** — Completeness lowest for a fourth consecutive run — deferred as unattributed, which is what motivated replicate sets. **Comparability reset: `ScoringMethodVersion` 7 → 8**, plus an in-place scoring-profile edit; runs 11–14 keep their value as observations and cease to be reproduction halves. |
| 16–18 | 2026-09-07 | GPT-5.6 Luna (`max`) | **94.42** (multi-run, [91.74, 97.11]) | The project's first R = 3 replicate set. **Reproducibility SD 0.30** — a re-run of this configuration moves the index by well under a point, so a later difference above ~1 point is signal rather than noise. **T19** (the Gnoll-race item) raised as a human-authored article; **T21** ruled claim-verifier spend not chat-transferable. |
| 19–21 | 2026-09-07 | GPT-5.6 Luna (`max`) | **92.63** (multi-run, ± 9.01) | Second R = 3 set, Tier A. **Runs 16–21 share all three instrument SHAs**, making the two sets the project's first replicate-grade reproduction pair; the apparent tenfold jump in reproducibility SD (0.30 → 3.34) is **not** an instrument change, since the two χ²(2) intervals overlap. **T-B** (Q1) again raised at rung 1 — now reclassified to rung 2, see § 7. Budget-key note: H6 widened `BudgetSignature` afterwards, so **runs 19–21 do not share a budget key with runs started later**; extending this series needs a fresh replicate set, not appended members. |

Two standing cautions from these entries, kept because they still bind:

- **The scoring-profile comparability key was widened, then narrowed (H9, resolved 2026-09-07).** It now hashes `BenchmarkScoringProfileService.CanonicalSignature` — the profile's scoring semantics only — recomputed from each run's stored snapshot, so nothing that matched before stopped matching. Every stored **group analysis** should still be re-analysed so its recorded `ComparabilityKeyHash` reflects the narrowed definition.
- **Runs 11–14 and runs 16–21 sit on opposite sides of the v7 → v8 reset**, and everything before run 22 sits on the far side of v8 → v9 (Readability) and v9 → v10 (unanswered questions). Three resets now separate run 11 from the present.

### Run 22 — 2026-09-07: Gemini 3.5 Flash-Lite
- **Candidate**: Gemini 3.5 Flash-Lite (`gemini-3.5-flash-lite`), thinking level `high`, reasoning
  Default, service tier Default (served `standard`), parallel tool calls on. 18 questions (Suite 5).
  **This was the model `RecommendedModels:Google` named for production chat** — see the Transfer
  Action below.
- **Prompt options**: `overseerMode: 0`, `verboseMode: false`, `spoilerFreeMode: false`,
  `enableToolUse: true`, `enableWebSearch: false`, `allowSourceCodeReferences: true`,
  `enableSubAgents: false`, `isGameOn: false`, `developerMode: false`, `hasMessageHistory: false`,
  `hasWikiContext: false`, `hasGameSnapshot: false`. `parallelMode`: Enabled.
- **Grading regime**: harness 12; scoring method 8; profile *Standard Intelligence Index*; assessor
  Claude 5 Sonnet (`high`); second opinion GPT-5.6 Terra, **blind, FlaggedPlusSample** — the report
  misrendered this as `Off`/"Manual only" (H1, since fixed); claim verifier GPT-5.6 Terra (`high`).
  ToolCallBudget 45 flat. Three distinct providers across the three roles.
- **Instrument SHAs** — **identical to runs 16–21 on all three**:
  - `CandidateSystemPromptSha256`: `bb19dc24e28755228647960efc5c6aa70cfed64262da29a4a5079181acf5753b`
  - `ToolGuidesSha256`: `9c79137965e4fe19e5cb2faea71ed29598ff032a7f3a653d8504ffd8a91ea168`
  - `KnowledgeBaseHeadSha`: `576ca5741d1bd79ef1cb2f7db575709cf0bb0db8`
- **Quality**: Intelligence Index **58 ± 10**; raw 59; unweighted mean 59; holistic 58.
  Accuracy 62.8 / L 3.6; Completeness 48.6 / L 2.7; Conciseness 85.0 / L 4.9; Readability 75.2 / L 4.2.
  Critical-error flags 4 (Q1, Q10, Q11, Q16), of which the cap bound on 2 (Q10, Q11). Contested-Verdict
  Sensitivity 62. **11 refuted claims across 7 answers** (Q2 ×3, Q5, Q6, Q7 ×2, Q10, Q16 ×2, Q18) out
  of 46 checked — a 24 % refutation rate against Luna's 0 %. Assessor mean absolute difference
  13.0 pts, signed +7.2 over 10 of 18; 4 disagreements; **2 critical-error splits** (Q11 25↔68,
  Q16 21↔48). 1 out-of-scope completeness deduction (Q12); 1 omission-as-accuracy (Q2).
- **Speed**: median model time **7,192 ms**, P90 19,742 ms, max 20,080 ms; median TTFT 1,887 ms.
  Speed Index 100 — **saturated**, 17 of 18 answers at the ceiling (H3, since fixed). Tool overhead
  14.9 s total, 8 % of turn time. *r* = 0.90 source share vs. model time; *r* = −0.20 vs. quality.
- **Cost**: 95 tool calls (5.3/question) — Source 67.4 %, Wiki 24.2 %, Structured 5.3 %, KB 3.2 %
  (3 KB calls; 15 of 18 answers zero-KB, prompt-compliant per `ChatService.cs:1103`). 108 model calls,
  83,437 input tokens per call. Input 9,011,289 / output 40,102 = 224.7 : 1; **cache-read share
  21.1 %, cache-creation 0** — against 90–91 % on every Anthropic and OpenAI run. Run cost $5.50:
  **candidate $2.29 (42 %)**, verifier $2.12 (38 %), assessor $1.09 (20 %).
- **Transfer Action**: **T23** — Google prompt caching absent in `GoogleProvider`; implemented at
  **rung 5**. `BuildChatRequestBody` now emits `systemInstruction`, `tools`, `toolConfig`,
  `safetySettings`, `generationConfig` and `service_tier` **before** `contents`, giving the request a
  stable byte prefix, with `safetySettings` ordered by ordinal key. Explicit `cachedContents` (4b) was
  **deliberately not implemented**: Google documents implicit caching as on by default for Gemini
  2.5-and-newer, keyed on the *prompt* prefix rather than the JSON byte prefix, so the token prefix
  was probably already stable and 4a's measured effect is unknown. **The confirming run is
  outstanding**, and until it lands the 21.1 % gap has no established cause.
  **T24** (unscoped greeting instruction at `ChatService.cs:1125`, contradicting `:1373`) deferred,
  rung 7, single observation, criterion recorded. **T25** — `RecommendedModels:Google` demoted from
  this model to **`gemini-3.8-flash` @ `high`** at rung 6; that model also omits
  `supportsSubAgentCoordination` / `supportsSubAgentExecution`, which default to `true` where
  Flash-Lite set both `false`, so the recommended Google model became sub-agent eligible as a side
  effect. Its price advantage is promotional and **doubles on 2027-01-01** ($0.75/$3.75/$0.075 →
  $1.50/$7.50/$0.15); revisit before then. The confirming `gemini-3.8-flash` benchmark run is
  outstanding. **T26** grading-role spend ruled not transferable. **S2/Q1** third confirmation of
  the outstanding rung-1 knowledge base article — still human-authored, still not an agent task.
- **Verification Outcome**: no prior chat change was under test. What the run verifies about the
  instrument is that four report defects (H1–H5) and one grading defect (H7) are only observable in
  the low-score, high-disagreement regime, and had survived eleven runs of frontier candidates
  unnoticed. All five were fixed in the same round as this entry, together with **H6**: rubric FORM
  criteria were being charged against Readability, which has no format anchor.
- **Comparability reset in this round**: `ScoringMethodVersion` moved **8 → 9** with the H6 and H7
  fixes. **Runs graded under v8 — this one included — are not comparable with v9 runs on
  Readability**; Accuracy, Completeness and Conciseness are unaffected. This is the same kind of
  deliberate break run 14's entry records, and it is stated here so that anyone extending the
  series sees the reset rather than inferring an improvement from it.

### Run 24 — 2026-09-08: Gemini 3.1 Pro (partial, cancelled at 3 of 18)
- **Candidate**: Gemini 3.1 Pro (`gemini-3.1-pro-preview`), thinking level `high`, service tier
  Default (served `standard`), parallel tool calls on. GnollHack Player Assistance Benchmark Suite,
  18 questions. **Cancelled** after 4 answer rows (Q1–Q3 `Ok`, Q4 `EmptyAnswer`).
- **Prompt options**: `overseerMode: 0`, **`verboseMode: true`** — the first verbose run in this
  registry — `spoilerFreeMode: false`, `enableToolUse: true`, `enableWebSearch: false`,
  `allowSourceCodeReferences: true`, `enableSubAgents: false`, `isGameOn: false`,
  `developerMode: false`, `hasMessageHistory: false`, `hasWikiContext: false`,
  `hasGameSnapshot: false`. `parallelMode`: Enabled.
- **Grading regime**: harness 12; **scoring method 9**; profile *Standard Intelligence Index*;
  assessor Claude 5 Sonnet (`high`); second opinion GPT-5.6 Terra, blind, `FlaggedPlusSample`; claim
  verifier GPT-5.6 Terra.
- **Instrument SHAs**:
  - `CandidateSystemPromptSha256`: `e9b3e9a75278a5cbd09fbe3afb270806a3be318863c9c3e1623897428a1c16c6`
  - `ToolGuidesSha256`: `9c79137965e4fe19e5cb2faea71ed29598ff032a7f3a653d8504ffd8a91ea168`
  - `KnowledgeBaseHeadSha`: `576ca5741d1bd79ef1cb2f7db575709cf0bb0db8`
  - The latter two are **identical to runs 16–22**, and the candidate hash is the
    **`verboseMode: true` twin of runs 16–22's `bb19dc24…` on unchanged code** (§ 6).
- **Quality**: **No Intelligence Index** — the run was cancelled, and under the change this round
  ships an aborted run publishes none. Raw quality index 60 over 3 answers. Accuracy 63.0 / L 3.7;
  Completeness 48.3 / L 2.7; Conciseness 54.0 / L 3.0; Readability 87.0 / L 5.0. 1 critical error
  (Q1). 2 refuted claims of 12 unverified across 3 answers, **one of which is false — see the
  verifier caution below**. 1 claim-verification timeout (Q2, 300 s).
- **Speed**: median turn 155,351 ms, P90 231,490 ms; median TTFT 18,641 ms, P90 74,735 ms; tool
  overhead 3,271 ms over `Ok` answers.
- **Cost**: 38 tool calls (9.5/question) — Source 71.4 %, Wiki 22.9 %, Structured 5.7 %, KB 0 %.
  40 model calls over 4 answer rows. 2,856,966 input / 25,387 output tokens; 837,637 cache-read.
  **The run reported all of this as `$0.00` and `0s`** — the harness defect this round fixes.
- **Transfer Action**: none taken. **T1** (empty answers after minutes of work), **T3** (median turn
  155 s), **T4** (`verboseMode` and Completeness) and **T5** (source-family share vs. latency)
  recorded as **motivated-only**. **T6** (fabricated Gnoll racial intrinsic and Yeenaghu wish)
  reclassified from rung 1 to **rung 2** — see § 7. **T7** (zero knowledge-base calls) is
  prompt-compliant per `ChatService.cs:1193`, no action. **Do not promote `gemini-3.1-pro-preview`
  to `RecommendedModels:Google`**, which names `gemini-3.8-flash` per run 22's T25.
- **Verification Outcome**: n/a — no prior chat change was under test.

### Run 25 — 2026-09-08: Gemini 3.1 Pro (partial, cancelled at 0 of 18)
- **Candidate**: same model and thinking level, same suite. **Cancelled** after one answer row,
  status `EmptyAnswer`.
- **Prompt options**: identical to run 24 except **`verboseMode: false`**.
- **Grading regime**: harness 12; scoring method 9; assessor GPT-5.6 Luna (`max`); second opinion and
  claim verifier Claude 5 Opus (`high`).
- **Instrument SHAs**: `CandidateSystemPromptSha256 = bb19dc24e28755228647960efc5c6aa70cfed64262da29a4a5079181acf5753b`
  — **byte-identical to runs 16–22** — with the same `ToolGuidesSha256` and `KnowledgeBaseHeadSha`
  as run 24.
- **Quality**: no index, no dimensional scores. The single answer produced no text.
- **Speed**: TTFT 383,114 ms, total 387,112 ms. **6½ minutes of work for nothing.**
- **Cost**: 51,534 input / 68 output tokens; 16,556 cache-read; 2 tool calls; 3 model calls. Also
  reported as `$0.00` and `0s`.
- **Transfer Action**: none. This is the second of the two observations behind **T1**.
- **Verification Outcome**: n/a.

**Both entries carry the same two cautions.** Runs 24 and 25 are **not comparable with each other** —
their prompt options, assessor and verifier rosters all differ — and **neither can serve as half of a
two-run reproduction**, because neither completed its suite.

**Verifier caution — a refutation is advisory evidence, and a human reads the cited line before any
change rests on it.** On run 24 Q1 the claim verifier refuted *"Gnolls can be played by the following
six roles"*, citing `src/role.c:1228`. That line is inside the **`races[]`** array — it is the Gnoll
*race* entry's alignment mask — while `MH_GNOLL` appears in exactly **six `roles[]` entries**:
Barbarian (`:141`), Caveman/Cavewoman (`:220`), Healer (`:299`), Priest/Priestess (`:540`), Rogue
(`:621`), Ranger (`:713`). The answer was right and the verifier was wrong, with a confident citation,
and the refutation is now recorded permanently in that report and flagged on the answer as
`BenchmarkAnswerFlags.RefutedClaim`. Its companion refutation on the same answer **stands** — Yeenaghu
carries `M2_HOSTILE` (`src/monst.c:5669`), so the claimed peace and wish are a genuine fabrication.
One right, one wrong, on one answer. **Run 22's 11 refuted claims across 7 answers — a 24 %
refutation rate — deserve the same re-check.** No code fix applies: the verifier is a model, its
output is advisory and folded into no index, and that containment is what limited the damage here.

**Comparability reset in this round**: `ScoringMethodVersion` moves **9 → 10**. An unanswered question
— the model ended its turn normally and produced no text — is now **scored 0** and enters the
Intelligence Index, the raw index and the unweighted mean, instead of being excluded as a transport
defect. **Runs graded under v9 or earlier are not comparable with v10 runs on the Intelligence Index,
the raw index, the unweighted mean or the standard error whenever either run contains an unanswered
question.** The **Speed Index and the run status are unaffected** — such a run reported
`CompletedWithErrors` under v9 and still does, because an empty answer is an error whatever produced
it. This is the third such deliberate break the registry records, after run 14's 7 → 8 and run 22's
8 → 9, and it is stated here so nobody extending the series infers a regression from a candidate that
simply stopped being excused. **The two deferred controlled runs must therefore run after this
change, not across it**: (a) one model, one suite, `verboseMode` false vs. true, to settle T4 — cheap
now that `e9b3e9a752…`/`bb19dc24…` is a known isolated pair; and (b) a tool-policy variant run to test
whether T5's latency correlation is causal.

### Run 28 — 2026-09-09: Claude 5 Sonnet
- **Candidate**: Claude 5 Sonnet (`claude-sonnet-5`), thinking level `high`. 18 questions, suite
  *GnollHack Player Assistance Benchmark Suite* (suite 6). **The first Anthropic candidate ever
  benchmarked here.**
- **Prompt options**: `overseerMode` 0 (Gameplay Help); `verboseMode` **false**;
  `spoilerFreeMode` false; `enableToolUse` true; `enableWebSearch` false; `enableSubAgents` false;
  `allowSourceCodeReferences` true; `isGameOn`, `hasGameSnapshot`, `hasMessageHistory` and
  `hasWikiContext` all false; `parallelMode` Enabled.
- **Grading regime**: harness **17**, scoring method **10**; second-opinion mode
  `FlaggedPlusSample` (4), **blind**; claim verifier `gpt-5.6-sol`. ⚠️ **The primary assessor and
  second-opinion model ids are not recorded in the run-28 analysis.** Read them off the run's own
  report before this entry is used as half of a reproduction — § 6 makes the roster part of what
  two runs must share, and this entry cannot discharge that as it stands.
- **Instrument SHAs**:
  - `CandidateSystemPromptSha256 = bb19dc24e28755228647960efc5c6aa70cfed64262da29a4a5079181acf5753b`
  - `ToolGuidesSha256 = 9c79137965e4fe19e5cb2faea71ed29598ff032a7f3a653d8504ffd8a91ea168`
  - `KnowledgeBaseHeadSha = 576ca5741d1bd79ef1cb2f7db575709cf0bb0db8`
  - `SourceCodeHeadSha = 3861281`

  **All three of the first are byte-identical to runs 16–22 and run 25.** The chat instrument had
  not moved when run 28 was measured.
- **Quality**: Intelligence Index **84 ± 11** (95 % CI over 18 items); raw quality index 88;
  unweighted mean 83 (difficulty weighting moved the index +1); holistic assessor score 83.
  Accuracy 89.6 / Completeness 83.0 / Conciseness 89.7 / Readability 92.1. **2 critical errors as
  published (Q3, Q14) — of which only Q14 is genuine.** 5 refuted claims across 4 answers.
- **Speed**: Speed Index 92, **saturated** — 11 of 18 answers at the ceiling, so the index does not
  discriminate for this candidate. Median model time 22,064 ms; P90 and maximum both produced by
  the two source-heavy questions.
- **Cost**: $3.73 — candidate $1.67 (45 %), grading $2.06 (55 %). 114 tool calls, **0 failed, 0
  refused**. 87.1 % cache-read share against a prompt measured 99 % cacheable, so prompt
  segmentation is exhausted as a cost lever. Wall time 23 m 42 s of which candidate answering was
  10 m 39 s. Two questions (Q15, Q16) consumed **42.6 % of the run's input tokens**, and the
  source-family share of a question's calls correlated **r = 0.92** with its model time and r = 0.14
  with its quality.
- **Comparability**: **run 28 has no comparable predecessor.** Runs 16–22 ran harness 12 / method 8,
  run 24 method 9, run 25 method 9; three deliberate scoring-method resets (7→8, 8→9, 9→10) separate
  run 28 from every completed run above. Every finding from it is therefore **single-run and
  motivating only**, never justifying.
- **Transfer Action**: a remediation round shipped on **2026-09-09**, all at **rung 3** — tool
  descriptions and tool policy text — plus report and harness fixes that are not chat changes at all.
  - **Rung 3, chat-transferable**: `get_item_stats` gained a Level 1 that returns named object class
    values and states its own unit conventions (`ac_bonus` is the stored `oc_armor_class` = `10 - ac`,
    `base_ac` is that argument, and the game negates the bonus into the hero's AC); `get_item_stats.md`
    and `get_monster_stats.md` now state those conventions and the monster `ac`/`mc`/`mr` scales;
    `source_code_search.md` states the matcher contract its own parameter prose had contradicted;
    `_policy.md` scopes tool narration to the moment of the lookup, keeps it out of the answer's
    opening, and adds miss-recovery guidance; `source_code_search`'s miss payload now names a next
    action; and `ToolExecutor`'s plain-text truncation suffix does too.
  - **Not chat changes**: the report's cost breakdown moved into `ModelPricingService` (its
    parentheticals were arithmetically wrong in two ways, one of which would have misprinted any
    OpenAI candidate); signed band drift, early terminations, near-ceiling answers, a capped-answer
    caveat on the confidence interval, agreement by trigger and a verification-uninformed agreement
    subset were added to the report; the saturated Speed Index is presented as advisory behind median
    model time; and run-level claim verification now runs before the two run-level second-opinion
    stages as well as after them, so every second opinion reads the same verification state.
  - **Deliberately not done**: no `ChatService.cs` prose was edited, and no rung 5, 6 or 7 action was
    taken. Widening `BenchmarkArtifactScrubber`'s narration regex was excluded because scrubbing is
    destructive — it replaces the text the assessor grades — so a wider regex would change what is
    graded; a flag-and-count-only path is its own round. `BenchmarkRunFinalizer.HasHarnessLimit` was
    deliberately left alone: it feeds `Classify`, the clean count and the run status, so widening it
    to cover a termination reason would change those for every future run.
- **No harness or method bump, and why.** `HarnessVersion` stays `"17"` and `ScoringMethodVersion`
  stays **10**, even though the tool-guide, `_policy.md` and tool-result changes do alter what the
  candidate receives. Bumping either would mark the run that *verifies* this round non-comparable
  with run 28 — the only run that motivated it — and the verification would measure nothing. The
  round is recorded through the **instrument fingerprints** instead, which the comparability
  machinery treats as provenance rather than as keys. **A reader comparing a pre-round run with a
  post-round run must read the fingerprints, not the harness version.**
  - `ToolGuidesSha256` after the round: `ed93e73d475c07b853957715bfa5e06aef307010769d09f965f0338fd5620480`
    (computed over the built `ToolGuides` output directory, 34 files, by the same manifest algorithm
    `BenchmarkService.ComputeToolGuidesSha256` uses).
  - `CandidateSystemPromptSha256` after the round: **not yet known.** It is hashed over the prompt
    the builder produces at run time, and `_policy.md` is injected into the frozen segment, so it has
    certainly moved — but the new value can only be read off the first run made after the round. Fill
    it in from the confirming run.
- **A diagnostic marker moved with this round.** `ToolExecutor`'s truncation suffix changed from the
  fixed 33-character `... [Result truncated for length]` to `... [Truncated: showing {shown} of
  {total} characters. …]`, so the stored `ResultLengthChars` fingerprint of a truncated plain-text
  result is no longer **10033**. Run 28's own 14 truncated results carry 10033 and always will;
  a post-round run carries roughly 10117. Match on the `[Truncated:` prefix, never on a length.
  `server_benchmark_tool_diagnostics` § 4 carries this.
- **Verification Outcome**: ⚠️ **pending.** The confirming run had not been launched when this entry
  was written. It must be **one run** (the criteria in the run-28 analysis are all countable events,
  which is why one suffices), suite 6, candidate `claude-sonnet-5` at thinking level `high`, the same
  assessor roster, `verboseMode: false`, harness 17, scoring method 10, after an Overseer restart —
  the tool guides are read once at startup. Until it has completed and been evaluated against the
  analysis's pre-declared criteria, **every rung-3 change above is unverified**, and § 9's rollback
  rule applies to each of them. Record the outcome here.

**Two suite defects run 28 exposed, which are not chat findings and must not be read as any.**

- **Q3's critical error is spurious.** Its rubric asserts *"Base AC is 1 in GnollHack"* and
  *"Spellcasting penalty is 5"* — both are `src/objects.c` macro **arguments**, not values a player
  ever sees. `GENERAL_ARMOR` stores `10 - ac` (`src/objects.c:1005`), `ARM_AC_BONUS`
  (`include/hack.h:681`) reads it and `src/do.c:5273` negates it into the hero's AC; the spellcasting
  penalty is multiplied by 30 (`include/general.h:1145`, applied at `src/do.c:2733`). The answer's
  *"−9 (i.e., improves your AC by 9)"* and *"−150 %"* were **both correct**. Scoring Q3 at its own raw
  64 instead of the cap of 25 moves the index 84 → 86 and the critical-error count **2 → 1**. The
  index barely moves — two points inside a ± 11 interval — but the count halves, and the count is the
  figure the report itself directs a reader to for this failure mode. **The repair is a units rule
  across every numeric rubric point in the suite, not a one-line edit**, and no harness check can
  see this class of defect: the second opinion agreed at 25/25 because it read the same rubric.
- **All 18 rubrics label their FORM section `**FORM** (readability)`**, asserting a grading link that
  scoring method v9 abolished and that the assessor prompt now contradicts outright. The canonical
  label — `**FORM** (not graded — presentation note only)` — existed in exactly one place in the
  repository, the question editor's placeholder text. `BenchmarkGenerationPrompt` was the upstream
  cause and was fixed in this round, so a newly generated suite cannot reproduce it; repairing the
  18 live rubrics is a separate step, because editing `ExpectedPoints` increments `ItemRevision` and
  nulls the assessed-difficulty snapshot, and `SuiteItemRevisions` is a **Fundamental** comparability
  key. **When that repair happens it will be a fourth deliberate comparability break**, this one
  suite-scoped rather than method-scoped: every run of suite 6 after the edit is non-comparable with
  every run before it, `GET suites/6/item-analysis` will report every item as `InsufficientData`
  until new runs accumulate (`BenchmarkItemAnalysis` admits an answer only at the question's current
  revision), and stored `BenchmarkGroupAnalysis` rows covering suite-6 runs go stale. **It must be
  written down here when it lands, or a later reader will read the revision bump as an improvement.**

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
