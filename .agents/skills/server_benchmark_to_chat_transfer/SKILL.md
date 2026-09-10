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
- **Version currency.** The bullets above were last checked against harness **18**, during the run-32 analysis (2026-09-10). `BenchmarkAssessmentPrompt.HarnessVersion` is the source of truth for the current value; if it now reads higher, treat this section as possibly aged and verify every claim against `server_benchmark_tool_diagnostics` § 2 before relying on it. This section silently aged out at harness 17 once already, and cost a run-28 analysis its tool-layer evidence — that is why this line exists.

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

> 🛑 **The assessor, second-opinion and claim-verifier model configurations are `Instrument`
> comparability keys too** — `ClaimVerifierConfiguration` among them, in
> `BenchmarkComparabilityKey.cs` — so a grader-roster change must happen **between series, never
> between the two halves of a verification pair**, and must be recorded in § 11 like any other
> instrument change. Run 30 is the worked example: the claim verifier moved from GPT-5.6 Sol to
> GPT-5.6 Luna between run 29 and run 30, to cut cost, which made run 30 differ from run 29 on
> **three** instrument keys rather than the two the harness-18 round alone would have moved. At no
> extra cost that time — two already dropped the pair to `NotComparable` — but the general case is a
> verification that measures nothing: move the roster on the wrong side of a pair and the pair stops
> verifying anything. The change itself was sound ($0.06 for 19 claims), and the new roster (Luna)
> is kept **unchanged for the confirming run**, so that only `ToolGuidesSha256` moves next. A
> change to a grader's **thinking level** is the same kind of key move as a change of model: the
> configuration is the key, not only the model it names.

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
   - A wiki edit may be **AI-authored when every claim it adds carries a citation to the source it
     was verified against** — this project's practice, and the one the `Races/Gnoll.md` rewrite
     followed. What stays forbidden, unchanged from rung 1, is ingesting a model's own *answer* as
     fact; a citation to the game source or another primary text is not that, and this does not
     loosen rung 1's human-authorship requirement for knowledge-base articles.
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

### Runs 11–25 — 2026-09-04 to 2026-09-08 (pruned per the rule above)

Full entries were collapsed on 2026-09-08 when this registry passed the pruning threshold, and again on 2026-09-10 when runs 28 and 29 became the most recent pair. What each run established is preserved below; the reports themselves remain the primary source.

| Run(s) | Date | Candidate | Intelligence Index | Transfer action, and what it settled |
|---|---|---|---|---|
| 11 | 2026-09-04 | GPT-5.6 Luna (`max`) | 91.5 ± 5.9 | Seeded the rung-1 knowledge base worklist (Q9, Q16) and promoted `verboseMode: true` for testing at run 12. **T3 and T4 were later withdrawn** — both cited prompt rules that do not exist, and were reclassified as harness defects. Graded **anchored** through a missing blind-second-opinion backfill (F1), and its instrument SHAs were never recorded, so **run 11 cannot serve as half of any reproduction.** |
| 12 | 2026-09-05 | GPT-5.6 Luna (`max`) | 91 ± 7 | **T7: `verboseMode: true` refuted.** Completeness moved 83.0 → 83.8 — inside noise — while Accuracy fell 4.2 and mean model time rose 28 %. The concise production default was kept, and has been kept at every re-test since. **T8** (heading-scoped wiki snippets) promoted to rung 3 with a six-measure pre-declared criterion. |
| 13 | 2026-09-05 | GPT-5.6 Luna (`max`) | 94 ± 4 | T11 (fabrication under *partial* retrieval failure) deferred pending a second observation. |
| 14 | 2026-09-06 | GPT-5.6 Luna (`max`) | 94 ± 3 | **T8 verified and kept**: four of six measures met against run 13's one, and both remaining misses moved the intended way. **T15** promoted at rung 3 (`get_monster_stats.md` contradicted `_policy.md`'s exact-stats routing). **T16** — Completeness lowest for a fourth consecutive run — deferred as unattributed, which is what motivated replicate sets. **Comparability reset: `ScoringMethodVersion` 7 → 8**, plus an in-place scoring-profile edit; runs 11–14 keep their value as observations and cease to be reproduction halves. |
| 16–18 | 2026-09-07 | GPT-5.6 Luna (`max`) | **94.42** (multi-run, [91.74, 97.11]) | The project's first R = 3 replicate set. **Reproducibility SD 0.30** — a re-run of this configuration moves the index by well under a point, so a later difference above ~1 point is signal rather than noise. **T19** (the Gnoll-race item) raised as a human-authored article; **T21** ruled claim-verifier spend not chat-transferable. |
| 19–21 | 2026-09-07 | GPT-5.6 Luna (`max`) | **92.63** (multi-run, ± 9.01) | Second R = 3 set, Tier A. **Runs 16–21 share all three instrument SHAs**, making the two sets the project's first replicate-grade reproduction pair; the apparent tenfold jump in reproducibility SD (0.30 → 3.34) is **not** an instrument change, since the two χ²(2) intervals overlap. **T-B** (Q1) again raised at rung 1 — now reclassified to rung 2, see § 7. Budget-key note: H6 widened `BudgetSignature` afterwards, so **runs 19–21 do not share a budget key with runs started later**; extending this series needs a fresh replicate set, not appended members. |
| 22 | 2026-09-07 | Gemini 3.5 Flash-Lite (`high`) | 58 ± 10 | **T23: Google prompt caching implemented at rung 5** — `BuildChatRequestBody` now gives the request a stable byte prefix; explicit `cachedContents` was deliberately not implemented, and **that confirming run is still outstanding**, so the run's 21.1 % cache-read share — against 90 %+ on every Anthropic and OpenAI run — has no established cause. **T25: `RecommendedModels:Google` demoted from this model to `gemini-3.8-flash` @ `high`** at rung 6; that confirming run is **also outstanding**, and the replacement's price advantage is promotional and **doubles on 2027-01-01**. **T24** (unscoped greeting instruction) deferred at rung 7 on one observation. Shares all three instrument SHAs with runs 16–21. **Comparability reset in its round: `ScoringMethodVersion` 8 → 9** (rubric FORM criteria stopped being charged against Readability), so a v8 run is not comparable with a v9 run on Readability. Its **11 refuted claims across 7 answers (24 % rate)** still deserve the re-check the verifier caution below motivates. |
| 24 | 2026-09-08 | Gemini 3.1 Pro (`high`), **`verboseMode: true`** | none — cancelled at 3 of 18 | No transfer action; every finding recorded as motivated-only. **T6** (fabricated Gnoll racial intrinsic and Yeenaghu wish) reclassified rung 1 → **rung 2**, see § 7. **Do not promote `gemini-3.1-pro-preview` to `RecommendedModels:Google`**, which names `gemini-3.8-flash`. Its `CandidateSystemPromptSha256 e9b3e9a752…` is the **`verboseMode: true` twin of `bb19dc24…` on unchanged code** — a known isolated pair, which is what makes a controlled verbose-vs-concise run cheap to set up. **Not a reproduction half:** the suite did not complete. |
| 25 | 2026-09-08 | Gemini 3.1 Pro (`high`), `verboseMode: false` | none — cancelled at 0 of 18 | No transfer action. The second of the two observations behind **T1** — 383 s to first token and 6½ minutes of work for no text at all. `CandidateSystemPromptSha256` byte-identical to runs 16–22. **Comparability reset in its round: `ScoringMethodVersion` 9 → 10** — an unanswered question the model ended normally is now **scored 0** and enters the Intelligence Index, the raw index and the unweighted mean instead of being excused as a transport defect, so a v9-or-earlier run is not comparable with a v10 run on any of those whenever either contains one. The **Speed Index and the run status are unaffected**. **Not a reproduction half.** |

Four standing cautions from these entries, kept because they still bind:

- **The scoring-profile comparability key was widened, then narrowed (H9, resolved 2026-09-07).** It now hashes `BenchmarkScoringProfileService.CanonicalSignature` — the profile's scoring semantics only — recomputed from each run's stored snapshot, so nothing that matched before stopped matching. Every stored **group analysis** should still be re-analysed so its recorded `ComparabilityKeyHash` reflects the narrowed definition.
- **Runs 11–14 and runs 16–21 sit on opposite sides of the v7 → v8 reset**, and everything before run 22 sits on the far side of v8 → v9 (Readability) and v9 → v10 (unanswered questions). Three resets now separate run 11 from the present.
- **A refutation is advisory evidence, and a human reads the cited line before any change rests on it.** On run 24 Q1 the claim verifier produced **two** refutations and **both were wrong**, each with a confident citation, and both are recorded permanently in that report and flagged as `BenchmarkAnswerFlags.RefutedClaim`.
  - It refuted *"Gnolls can be played by the following six roles"*, citing `src/role.c:1228` — a line inside the **`races[]`** array, the Gnoll *race* entry's alignment mask, while `MH_GNOLL` appears in exactly **six `roles[]` entries**: Barbarian (`:141`), Caveman/Cavewoman (`:220`), Healer (`:299`), Priest/Priestess (`:540`), Rogue (`:621`), Ranger (`:713`).
  - It refuted the claimed Yeenaghu peace and wish, citing `M2_HOSTILE` on `src/monst.c:5669`. **That verdict was recorded here as standing until 2026-09-10, and it is false.** `peace_minded()` (`src/makemon.c:4451`) returns `TRUE` for Yeenaghu **and** for `PM_HYENA` against a gnoll player *unconditionally*, ahead of `always_peaceful()` and ahead of every alignment test — `M2_HOSTILE` is the default flag that check overrides. And `src/minion.c:799-830` grants a real wish through `mongrantswish()`, gated on chaotic with an alignment record ≥ 14, or a luck roll for a chaotic or neutral gnoll, or carrying the Howling Flail, with `context.yeenaghu_wishes` making repeats progressively rarer.
  - **The shared failure mode is worth more than either instance:** the verifier cites a *default flag or the wrong array element* and misses the special-case code that overrides it. Both errors are on one answer, about one race, and both survived into a published report. **No code fix applies** — the verifier is a model, its output is advisory and folded into no index, and that containment is what limited the damage — but a refutation about GnollHack-specific racial behaviour should be treated as **unverified until a human reads the overriding code path**, not as evidence.
- **Two controlled runs remain deferred, and both must run under v10 rather than across the boundary**: (a) one model, one suite, `verboseMode` false vs. true, to settle whether verbosity buys Completeness — cheap now that `e9b3e9a752…`/`bb19dc24…` is a known isolated pair; and (b) a tool-policy variant run, to test whether the source-family-share-versus-latency correlation is causal.

### Run 28 — 2026-09-09: Claude 5 Sonnet
- **Candidate**: Claude 5 Sonnet (`claude-sonnet-5`), thinking level `high`. 18 questions, suite
  *GnollHack Player Assistance Benchmark Suite* (suite 6). **The first Anthropic candidate ever
  benchmarked here.**
- **Prompt options**: `overseerMode` 0 (Gameplay Help); `verboseMode` **false**;
  `spoilerFreeMode` false; `enableToolUse` true; `enableWebSearch` false; `enableSubAgents` false;
  `allowSourceCodeReferences` true; `isGameOn`, `hasGameSnapshot`, `hasMessageHistory` and
  `hasWikiContext` all false; `parallelMode` Enabled.
- **Grading regime**: harness **17**, scoring method **10**; second-opinion mode
  `FlaggedPlusSample` (4), **blind**. Assessor **Gemini 3.7 Flash** (`gemini-3.7-flash`, `high`);
  second opinion and claim verifier **GPT-5.6 Sol** (`gpt-5.6-sol`, `high`, reasoning standard),
  blind `FlaggedPlusSample` — read from the run's own diagnostics capture, identical to run 29's, so
  the roster is no bar to using run 28 as half of a reproduction.
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
  - `CandidateSystemPromptSha256` after the round:
    `851af9406949b176e72442f26ed6f7f77aa27dcd85eeb52162be0405c1942a00`, read from run 29. It is
    hashed over the prompt the builder produces at run time, and `_policy.md` is injected into the
    frozen segment, so the round did move it.
- **A diagnostic marker moved with this round.** `ToolExecutor`'s truncation suffix changed from the
  fixed 33-character `... [Result truncated for length]` to `... [Truncated: showing {shown} of
  {total} characters. …]`, so the stored `ResultLengthChars` fingerprint of a truncated plain-text
  result is no longer **10033**. Run 28's own 14 truncated results carry 10033 and always will;
  a post-round run carries roughly 10117. Match on the `[Truncated:` prefix, never on a length.
  `server_benchmark_tool_diagnostics` § 4 carries this.
- **Verification Outcome**: **run 29 is the confirming run — see its entry below.** It passed on
  every countable criterion except one: `_policy.md`'s answer-opening rule, which three of 17
  answers broke and which the harness could not measure at all. Every other rung-3 change above is
  verified and kept. Two of the run-28 criteria remain **undetermined** rather than met or missed —
  Q3's per-question call count, and Q15/Q16 per-question quality — because the run-29 analysis does
  not carry per-question figures for them; they need run 29's own report.

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

### Run 29 — 2026-09-10: Claude 5 Sonnet (the confirming run for run 28's round)
- **Candidate**: Claude 5 Sonnet (`claude-sonnet-5`), thinking level `high`, parallel tool calls
  on, max output 128000. Suite 6, 18 questions, sequential (1 at a time).
- **Prompt options**: `overseerMode` 0; `verboseMode` false; `spoilerFreeMode` false;
  `enableToolUse` true; `enableWebSearch` false; `enableSubAgents` false;
  `allowSourceCodeReferences` true; `isGameOn`, `hasGameSnapshot`, `hasMessageHistory` and
  `hasWikiContext` all false. `parallelMode` Enabled.
- **Grading regime**: harness 17, scoring method 10, profile *Standard Intelligence Index* (1),
  budget 45 flat. Assessor **Gemini 3.7 Flash** (`high`); second opinion **GPT-5.6 Sol**
  (`high`, reasoning standard), **blind**, `FlaggedPlusSample`; claim verifier **GPT-5.6 Sol**.
  Three distinct providers. **Identical roster to run 28** — see the correction to that entry.
- **Instrument SHAs**:
  - `CandidateSystemPromptSha256 = 851af9406949b176e72442f26ed6f7f77aa27dcd85eeb52162be0405c1942a00`
    — **this is the post-round value the run-28 entry left blank.**
  - `ToolGuidesSha256 = ed93e73d475c07b853957715bfa5e06aef307010769d09f965f0338fd5620480`
    — the post-round value, confirmed from the run record.
  - `KnowledgeBaseHeadSha = 576ca5741d1bd79ef1cb2f7db575709cf0bb0db8` (= runs 16–22, 24, 25, 28)
  - `WikiHeadSha = d4aa0e19790caa7e97e21cc27a4501fbe8c2e1f9` (= run 28)
  - `SourceCodeHeadSha = 3861281ec5bd39a6de5e75c1f91c5ddc4db19a42` (= run 28)
- **Quality**: Intelligence Index **92 ± 3** (95 % CI over **17** items — Q8 was lost to a
  transport failure); raw quality index 92; unweighted mean 92; holistic 84. Accuracy 94.5 / L 5.6;
  Completeness 87.7 / L 5.1; Conciseness 89.2 / L 5.2; Readability 91.6 / L 5.4.
  **0 confirmed critical errors, 1 contested (Q3)**; Contested-Verdict Sensitivity 89.
  3 refuted claims across 3 answers of 13 unverified; verifier 10 supported / 3 refuted / 0
  indeterminate. Agreement 26.6 mean absolute / −26.6 signed over 5 of 17, 4 disagreements, 1
  critical-error split — **all four disagreements in the same direction, and the figure is
  contaminated by Q8: excluding it, 33.3 over 4 of 4.** From harness 18 the harness excludes such
  an answer itself, so a post-18 run's agreement figure is already over the gradeable population.
- **Speed**: median model time **18,807 ms**; P90 58,215; max 65,191. Median TTFT 3,683 ms.
  Speed Index 96, **saturated** — 12 of 17 at the ceiling, so it does not discriminate; and the
  profile targets 15,000 ms interactive latency while the candidate ran at `high`, so it is
  advisory twice over.
- **Cost**: $3.46 — candidate $1.39 (40 %), grading $2.08 (60 %). 85 tool calls (4.7/question,
  **0 failed, 0 refused**) — Wiki 50.6 %, Source 40.0 %, Structured 7.1 %, KB 2.4 %.
  81 model calls; 2,155,450 in / 26,396 out; cache read 1,854,675 (**86.0 %**), cache creation
  300,615 (**cache write is $0.75, 54 % of candidate cost**). Wall 19m 02s, candidate answering
  7m 32s, advisory grading stages 9m 00s (47 %) strictly serial.
- **Comparability**: **run 28 is a valid predecessor** — every key of § 6 matches except the two
  fingerprints the round moved. This is the first true reproduction pair in this registry for an
  Anthropic candidate.
- **Verification Outcome — run 28's round: PASSED on the countable criteria, with one exception.**
  - **Verified**: bare 30-char `source_code_search` misses 12 → **0**; tool calls 114 → **85**;
    source-family 54 → **34**; input tokens −19 %; candidate answering 10m 39s → **7m 32s**; median
    model time 22,064 → **18,807 ms**; top-two token concentration 42.6 % → **31.8 %**; source-share
    vs. model-time *r* 0.92 → **0.85**; critical errors 2 → **0**, with Q3 moving from a capped 25
    to 83/critical-false while quoting `get_item_stats`'s new unit conventions verbatim; refuted
    claims 5 → 3; cost $3.73 → $3.46. The `[Truncated:` marker length moved 10033 → **10117**
    exactly as § 4 of `server_benchmark_tool_diagnostics` predicted (8 occurrences).
  - **Not verified**: the `_policy.md` answer-opening rule. Three of 17 answers (Q1, Q2, Q15) open
    with a claim of sufficiency, which `_policy.md:11-13` forbids outright, and **none was
    flagged** — `BenchmarkArtifactScrubber`'s regexes miss all three (adverb between `I` and the
    verb; `give` absent from the `Let me` verb list; `This has` outside the finding pattern).
    The instruction is kept unchanged; a **flag-and-count-only** detector was added instead, which
    is the path the run-28 entry deferred as "its own round".
  - **Undetermined, not missed**: two of run 28's own criteria could not be settled from the
    run-29 analysis — Q3's per-question tool-call count (target ≤ 3) and Q15/Q16 per-question
    quality (must not fall from 100 and 99). Neither figure is carried in the analysis; both need
    run 29's own report. **Conciseness landed at 89.2 against a stated floor of 89.7** — breached
    as written, by 0.5 points, which is far inside the ±11 reference interval and is recorded as a
    breach of the criterion rather than as a result.
  - The Intelligence Index moved +8, **inside run 28's own ±11 interval**, so the index movement is
    not itself a result. The countable events are.
- **Transfer Action**: **T2** — wiki-family miss payloads given near-miss information and a next
  action, mirroring `SourceCodeSearchTool.BuildMissContent`, at **rung 3**, with the criterion and
  rollback trigger recorded in the run-29 analysis § 7. **T3** — `WikiSearchTool` given a
  `MaxResultLengthOverride` of 13000 so its own 5 × 2500 budget fits, with `AgentLoopRunner`'s
  batch-budget exemption tightened to compare against
  `Math.Max(MaxBatchResultLength, MaxResultLength)` so the override does not silently exempt the
  tool from the 40,000-char batch budget, and `nethack_wiki_search` given the per-result cap it
  lacked, at **rung 4**. The global cap was **not** raised and `Tools:wiki_search:PerResultChars`
  was **not** lowered: the cap binds on only 2 of 24 `wiki_search` calls, so both alternatives cost
  far more than they repair.
  **T4** — the Gnoll wiki content gap, **rung 2, sixth raising and second on suite 6**. ⚠️ **This
  triage was substantially wrong; see the correction below.** Run 30's C1 narrows it further: run
  29's two 706-character Q1 `wiki_view` results were the **monster** article `Monsters/Gnoll.md`,
  not a duplicate fetch of the race article — the race article was always reachable by
  `wiki_search` and unreachable by `wiki_view` on the bare title, so that part of run 29's C1 is
  reclassified as a **retrieval defect**, not a content gap. **T1** measured, not re-edited.
  **T5** prompt-compliant, no action. **T6** and **T7** ruled not chat-transferable.
- **Suite defects still open**: all 18 suite-6 rubrics label FORM `(readability)` (14 of 17 answers
  tripped it); 3 out-of-scope Completeness deductions; band drift **+28.9 mean signed, 11 harder
  and 0 easier of 18**; Q4 and Q12 assessed Advanced yet answerable from one wiki hit. The rubric
  repair and the band re-authoring are **deliberately deferred to one single comparability break**,
  which must not land in the same round as a verification run.
- **A configuration surface with no fingerprint at all**: `Overseer/appsettings.json` tool limits
  (`Tools:*:MaxResults`, `PerResultChars`, `MaxResultLength`). `ToolGuidesSha256` hashes the guide
  files, not the configuration, so T3's change is invisible in every run record. Recorded, not
  closed.

**Correction to T4, 2026-09-10: the Gnoll gap was never an access problem, and it was a third the
size recorded.** T4 had been raised six times across this registry as a rung-2 content gap. It was
checked on disk for the first time on 2026-09-10, and three of its premises do not hold.

- **The article exists and always has.** `Races/Gnoll.md` in the GnollHack wiki repository dates
  from that repository's **initial commit (2025-07-15)** and is present at
  `WikiHeadSha d4aa0e1`, the exact revision runs 28 and 29 recorded. It was in the corpus for
  **every run in this registry**.
- **It was reachable by the tools, on every one of those runs.** `WikiService` indexes `.md` under
  `SearchOption.AllDirectories`, so `Races/` is in scope; the file is 1.16 KB and clears
  `MaxWikiFileSizeKB`; and its `title` field is `"Gnoll"` with a 5× boost, so both `wiki_search`
  and `wiki_view` reach it on the obvious query. There was no exclusion, no truncation and no
  cold-start guard. **"The AI could not see it" was never true and was never checked.**
- **Two of the six facts recorded as missing were in the article the whole time**: the per-level
  hit-point and mana progression, and the Yeenaghu wish. Three were genuinely absent — corpse-
  property recognition, triple nutrition from tripe rations, and bone-eating — and all three are
  declared by the game itself in the `races[]` gnoll entry (`src/role.c`), alongside the two the
  article did carry. The sixth, bone-eating, is real: it exempts a gnoll from the unfamiliar-food
  confirmation on bone and leather (`src/eat.c:3660,3673`).
- **The article's Yeenaghu line was imprecise in a way that could itself draw a deduction**: it said
  "when met", where the code requires *chatting*, requires a chaotic gnoll to hold an alignment
  record ≥ 14 or win a luck roll, admits a neutral gnoll on a luck roll, lets the Howling Flail
  bypass the alignment test outright, and makes repeat wishes progressively rarer.

**What this reclassifies.** Q1's poor score on runs 24 and 29 is **substantially a grading defect,
not a knowledge gap**: both of its refuted claims were verifier errors (see the standing verifier
caution above), and the answer was closer to the source than the report says. The remaining content
work was three facts and one precision fix, not six missing facts — a rung-2 item roughly a third
the size this registry had been carrying.

**Two method lessons, and they are the reason this entry is this long.**

1. **A "content gap" is a Corpus / Environment claim, and § 2's fourth triage category requires it
   to be *checked on disk* before it is filed.** Six raisings inherited the premise from the
   assessor's prose instead. The check is the reachability procedure in
   [`server_tool_data_sources`](../server_tool_data_sources/SKILL.md) § 5 and costs minutes.
2. **An assessor's list of "missing" facts is a hypothesis about the corpus, not an observation of
   it.** Two of six were wrong here. Diff the list against the article before recording it, and
   record which facts the article already carried.

`Races/Gnoll.md` was rewritten on 2026-09-10 against the game source — the three missing traits
added, the Yeenaghu conditions corrected, and a NetHack-veterans note added for gnolls taking the
gnome slot in `races[]`. **T4 is closed pending the wiki commit**, and a later run must not re-raise
it without first re-running the reachability check and diffing the assessor's list against the page.

**The implementation round for this entry shipped on 2026-09-10, and it moves the harness.**
That is the difference from the run-28 round, which deliberately moved nothing, and it must not be
discovered later by someone comparing the two.

- **`HarnessVersion` moves to `"18"`; `ScoringMethodVersion` stays at 10.** The harness bump records
  six new columns and a new per-answer record shape; no index, no dimensional score and no run
  status changes.
- **The round moves two instrument keys, not one**: `HarnessVersion` and `ToolGuidesSha256` (T2's
  tool guides and miss payloads). It does **not** move `CandidateSystemPromptSha256`, which this
  entry originally claimed — run 30 recorded
  `851af9406949b176e72442f26ed6f7f77aa27dcd85eeb52162be0405c1942a00`, byte-identical to run 29's,
  because the candidate system prompt inlines `_policy.md` and the policy overrides rather than
  every per-tool guide. Per `BenchmarkComparabilityKeyKind.Instrument`, **two or more differing
  instrument keys drops a comparison below Tier B**, so the conclusion is unchanged: the confirming
  run for *this* round verifies its **countable criteria** — the run-29 analysis § 7 thresholds, all
  of them counts and per-question figures — and is **not** a Tier-B reproduction of run 29. That was
  the accepted cost of admitting the tool changes into the same round as the harness fixes rather
  than the next one. **A per-tool guide edit alone therefore moves `ToolGuidesSha256` only**, which
  is what makes the run-30 round a single-key, Tier-C change.
- **Harness 18 changes what a run reports about itself, by design.** A `Failed` or `ProviderError`
  answer now lands in the transport-defect bucket rather than in Clean, so run 29's own
  *"Clean 18 of 18"* would read *"Clean 17 of 18, Transport Defects 1, Provider Errors 1"* under 18.
  The grader-agreement figure is now over the gradeable population, so run 29's 26.6 would read
  **33.3**. Neither is a regression and neither is an index change: **compare a 17-stamped run with
  an 18-stamped one on those figures only after reading `docs/overseer/ai-benchmark.md` § Harness
  Version 18 Updates.**
- **Two new advisory flags are now measured**, both folded into no index:
  `OutOfRubricAccuracyDeduction`, from the prompted `Not in rubric:` marker that nothing previously
  consumed and which is now also a second-opinion trigger; and `AnswerFramingOpener`, which is
  **detected and counted only — the text is not removed**, because scrubbing would replace what the
  assessor grades and would move `ScoringMethodVersion`, making the run that verifies this round
  non-comparable with run 29. T1's criterion for the next run is that the `AnswerFramingOpener`
  count is **reported and ≤ 3**; at 3 or more on **two consecutive** runs the prompt half becomes a
  rung-7 candidate with its own round and its own approval.
- **The band drift was diagnosed, and the diagnosis changes what a later repair should do.**
  `BenchmarkDifficultyPrompt` was read against the items whose authored and assessed bands disagree.
  Its task line asks for the question's intrinsic difficulty, but its three band anchors name
  **classes of subject matter** — the Advanced anchor names the material's location, *"deep C source
  code mechanics"* — it supplies the full rubric, which is an enumeration of what a complete
  *answer* must contain, its one anti-anchoring instruction only ever rates **up**, and it names no
  corpus and no tool hierarchy, so the assessor cannot rate retrieval difficulty because it is given
  nothing to rate it against. **The prompt is at fault, not the authored bands.** Zero of eighteen
  items assessed *easier* is the fingerprint of a one-sided rule, not of per-item noise. So the
  deferred suite repair must **fix the prompt and leave the bands alone**: moving the authored bands
  up to meet the assessed values would encode the assessor's calibration defect into the suite,
  destroy the drift signal that revealed it, and spend a **Fundamental** comparability break doing
  it. A prompt repair is its own round with its own re-rating pass, because `AssessedDifficulty` is
  the Intelligence Index weight.

### Run 30 — 2026-09-10: Claude 5 Sonnet (the confirming run for run 29's round; first harness-18 run)
- **Candidate**: Claude 5 Sonnet (`claude-sonnet-5`), thinking `high`, parallel tool calls on, max
  output 128000. Suite 6, 18 questions, sequential.
- **Prompt options**: `overseerMode` 0; `verboseMode` false; `spoilerFreeMode` false;
  `enableToolUse` true; `enableWebSearch` false; `enableSubAgents` false;
  `allowSourceCodeReferences` true; `isGameOn`, `hasGameSnapshot`, `hasMessageHistory`,
  `hasWikiContext` all false; `parallelMode` Enabled.
- **Grading regime**: harness **18**, scoring method 10, profile *Standard Intelligence Index* (1),
  budget 45 flat. Assessor Gemini 3.7 Flash @ `high`; second opinion GPT-5.6 Sol @ `high`, blind,
  FlaggedPlusSample; **claim verifier GPT-5.6 Luna @ `max`** — changed by the operator from
  GPT-5.6 Sol to cut cost; `ClaimVerifierConfiguration` is an Instrument comparability key. See the
  roster-timing rule in § 6.
- **Instrument SHAs**: `CandidateSystemPromptSha256 =
  851af9406949b176e72442f26ed6f7f77aa27dcd85eeb52162be0405c1942a00` (**= run 29**: per-tool guide
  edits do not move the prompt hash); `ToolGuidesSha256 =
  3feaff3657121d675dd971acb307050654ca98631e08139f77c6126f2e000a10` (moved, the run-29 round);
  `KnowledgeBaseHeadSha = 576ca5741d1bd79ef1cb2f7db575709cf0bb0db8` (= runs 16–29); `WikiHeadSha =
  ade24a4544b43272dfb58809c791d55968629ac2` (moved: the `Races/Gnoll.md` rewrite); `SourceCodeHeadSha
  = 3861281ec5bd39a6de5e75c1f91c5ddc4db19a42` (= runs 28–29).
- **Quality**: Intelligence Index **90 ± 4** (18 items); raw 90; unweighted 91; holistic 90.
  Accuracy 94.8 / L 5.6; Completeness 83.5 / L 4.8; Conciseness 89.1 / L 5.2; Readability 91.3 /
  L 5.3. **0 confirmed critical errors, 1 contested (Q16)**; sensitivity 87. 2 refuted claims of 19
  unverified across 7 answers; verifier 17 / 2 / 0. Agreement −23.8 signed over 4 of 18, 3
  disagreements, 1 critical split (Q16). Out-of-scope 2; FORM 16 of 18; band drift +28.9 (a suite
  constant, not a run measurement).
- **Speed**: median model time **19,406 ms** as the report rendered it, against the UI's
  **22,283 ms** — two medians for one run (H1), fixed in this round by moving the report's P50
  lines to the statistical median, which the UI already used. P90 103,766; max 105,694 (Q18). TTFT
  median 4,445. Speed Index 91, saturated 10 of 18, advisory twice over.
- **Cost**: $2.97 — candidate $1.57 (53 %; cache write $0.86 = 55 % of it), grading $1.40 (47 %).
  84 tool calls (4.7/q, **0 failed, 0 refused**) — Wiki 57.1 %, Source 36.9 %, Structured 3.6 %, KB
  2.4 %. 76 model calls; 2,165,700 in / 34,783 out; cache read 84.2 %. Wall 22m 25s; answering
  10m 30s; advisory grading stages 9m 19s (42 %) strictly serial. **`search_definitions` accounted
  for 8,806 ms of 10,544 ms of tool time in 5 calls.**
- **Comparability**: three instrument keys differ from run 29 (`HarnessVersion`, `ToolGuidesSha256`,
  `ClaimVerifierConfiguration`) → **NotComparable, below Tier B**; it verifies countable criteria
  only.
- **Verification Outcome — run 29's round**: **T3 verified** (no `wiki_search` truncation; the
  `nethack_wiki_search` per-article cap visible). **T2: criterion (1) missed as written**
  (wiki-family 43 → 48; source 34 → 31 ✓), **(2) met** (Q1 10 → 3 calls, 284,321 → 98,643 tokens),
  **(3) met** — settled at rung 0 from the run's tool-call log, which shows two `No wiki article
  matched '` payloads (Q5 310 chars, Q18 294 chars) — **(4) met** (2 refuted, 0 critical), **(5)
  met** on the report's median. **T2 is kept, not reverted.** The model recovered from both misses
  within two rounds and neither became a guessing cascade, which is the failure mode T2 exists to
  prevent; it did not, however, take the `wiki_search` next action the payload named — it corrected
  its own argument instead. Reverting would have substituted a bare 35-character "not found" for
  the payload whose contract sentence is the plausible cause of that correction. **T1 met**: 1
  flagged, 2 by hand (Q1, Q9), ≤ 3; no rung-7 candidate; the detector was widened (H3) so the next
  count is honest.
- **Transfer Action**: **T1** `wiki_view` title-collision disambiguation at **rung 3, tool
  contract** — 26 genuine colliding pairs measured on disk, and Q1's monster-for-race substitution
  confirmed at rung 0 — with the criterion and rollback in the run-30 analysis § 7. **T2**
  `FindDefinition` given a literal pre-filter and per-call compiled patterns, output-identical,
  guarded by a rendered-output test. **T3** the wiki indexer now skips dot-directory segments (20
  non-article documents removed from the index). **T6** the wiki `Melee Weapons` note 1 correction
  at **rung 2, AI-authored from cited source** in the wiki repository's own session (the fork
  cannot be applied at range; reach is 2 / √5 / √6 / √7 / √8 for Basic through Grand Master).
  Harness fixes H1–H4 are report and UI only, and **neither `HarnessVersion` nor
  `ScoringMethodVersion` moves**; the round moves `ToolGuidesSha256` (via `wiki_view.md`) and
  nothing else a run records. `FindDefinition` and the indexer exclusion move **no fingerprint at
  all** and are recorded here for that reason.
- **A second `wiki_view` resolution defect, found only because the round shipped the tool-call log
  (N1/N2)**. The tool could not resolve a filename carrying its extension, although its own
  parameter schema calls `article` an *"Article filename or title"* and `wiki_search` prints that
  filename in its snippet header — so the model copies it straight in. **6 of the run's 20
  `wiki_view` calls did exactly that**: a single-word title plus `.md` missed outright (`Praying.md`,
  `Runewords.md`, both of which resolve on the bare title), while a multi-word one survived on its
  remaining terms and, with no relevance floor, returned the **wrong article** (`Guide to
  Praying.md` → `Guide.md`; `Sacrifice Offering.md` → `Sacrifice Gifts.md`). The near-miss probe
  then asserted *"No article with a similar title is indexed either"* about an article that exists,
  because it re-queried with the same unresolvable string. Both are the same root cause as T1 and
  both were folded into this round's Step 5. **The method lesson: a rung-0 read of a run's own
  arguments found in minutes a defect that six runs of report-reading had not, and it was one the
  analysis could not have inferred from result lengths.**
- **Deferred with its reason (N3)**: a `section` matching no heading returns the whole article,
  which is then truncated, so the headings needed to correct the call may be past the cut. Q4 paid
  roughly 30,000 characters across three such calls on one article whose headings carry emoji
  prefixes — `section: "✨ Gilthoniel"` returned 853 characters once the model guessed the emoji,
  and `Morgoth` was never reached. Returning the heading list plus a next action would cost
  hundreds of characters. It changes a contract documented in `wiki_view.md` and
  `server_tool_parameter_reference` § 5, so it is its own round with its own criterion and
  rollback.
- **Two skill defects this round corrected**: `server_benchmark_tool_diagnostics` § 4 and
  `server_tool_parameter_reference` § 5 both documented a `wiki_view` **section-miss payload**
  opening `Article matching '`. **No code emits it** — a section miss returns the full article with
  the `[Section '…' not found in article. Returning full text.]` line, which the parameter
  reference's own prose stated correctly two paragraphs below its table. Q4 is the proof: three
  section misses at roughly 10,000 characters each, not a few-hundred-character payload.
- **Verifier caution, fourth instance**: Q11's refutation cited the `Trident` and `Fork` stub
  pages; `src/apply.c:5233` applies tridents at spear range. A refutation about weapon *behaviour*
  needs the `apply.c` / `uhitm.c` path read, not the item page.

### Run 31 — 2026-09-10: Claude 5 Sonnet (the confirming run for run 30's round)
- **Candidate**: Claude 5 Sonnet (`claude-sonnet-5`), thinking `high`, parallel tool calls on, max
  output 128000. Suite 6, 18 questions, sequential.
- **Prompt options**: `overseerMode` 0; `verboseMode` false; `spoilerFreeMode` false;
  `enableToolUse` true; `enableWebSearch` false; `enableSubAgents` false;
  `allowSourceCodeReferences` true; `isGameOn`, `hasGameSnapshot`, `hasMessageHistory`,
  `hasWikiContext` all false; `parallelMode` Enabled.
- **Grading regime**: harness 18, scoring method 10, profile *Standard Intelligence Index* (1),
  budget 45 flat. Assessor Gemini 3.7 Flash @ `high`; **second opinion GPT-5.6 Luna @ `max`**
  (changed from run 30's GPT-5.6 Sol @ `high` — an Instrument key, moved on the wrong side of a
  verification pair for the second time in three runs), blind, FlaggedPlusSample; claim verifier
  GPT-5.6 Luna @ `max` (= run 30).
- **Instrument SHAs**: `CandidateSystemPromptSha256 = 851af9406949b176e72442f26ed6f7f77aa27dcd85eeb52162be0405c1942a00`
  (= runs 29–30); `ToolGuidesSha256 = 847937baa94da2e990d68f63d05d4438fbb5b463cc51273e49375d50c8a16704`
  (moved: run-30 round, `wiki_view.md`); `KnowledgeBaseHeadSha = 576ca5741d1bd79ef1cb2f7db575709cf0bb0db8`
  (= runs 16–30); `WikiHeadSha = a31dfc2f9461a33e0607ebe5bf49c336d40e942d` (moved: run-30 T6);
  `SourceCodeHeadSha = 3861281ec5bd39a6de5e75c1f91c5ddc4db19a42` (= runs 28–30).
- **Quality**: Intelligence Index **86 ± 12** (18 items; the interval is widened by two capped answers);
  raw 89; unweighted 87; holistic 82. Accuracy 90.6 / L 5.4; Completeness 85.2 / L 4.9; Conciseness
  90.5 / L 5.3; Readability 92.8 / L 5.4. **2 critical errors as published (Q5, Q18) — of which only Q5
  is genuine**; Q18 is a rubric defect proven against `src/engrave.c` (erosion-on-attack is GnollHack's
  code, unchanged from NetHack), and the synthesis's "NetHack contamination" narrative is wrong for it.
  Q5's error is an *omission* of the 200/100/0 trouble thresholds (`src/pray.c:3675`), which are also
  NetHack's; the assessor's "NetHack 1000-turn figure" is GnollHack's own `Guide to Praying` article.
  0 refuted claims of 15 verified; verifier 15 / 0 / 0. Agreement −6.0 signed over 4 of 18, 1
  disagreement (Q3, the third raising of its rubric defect). Out-of-scope 1; FORM 13; band drift +28.9.
- **Speed**: median model time **16,085 ms**; P90 45,014; max 59,996 (Q16). TTFT median 4,120. Speed
  Index 97, saturated 14 of 18, advisory twice over.
- **Cost**: $2.33 — candidate $1.35 (58 %; **cache write $0.79 = 59 % of it**), grading $0.98 (42 %).
  68 tool calls (3.8/q, **0 failed, 0 refused**) — Wiki 51.5 %, Source 38.2 %, Structured 7.4 %, KB
  2.9 %. 68 model calls; 1,897,497 in / 24,605 out; cache read 83.3 %; uncached $0.00. Wall 27m 06s;
  candidate answering 6m 36s (24 %); **second opinion 15m 23s (57 %) for 4 opinions**, serial and
  inline — Q5's ran between Q5 and Q6 and evicted the Anthropic prompt cache (Q6 cache creation
  23,034 vs ~11,000 typical).
- **Comparability**: two Instrument keys differ from run 30 (`ToolGuidesSha256`, second-opinion
  configuration) → **NotComparable, below Tier B**; verifies countable criteria only.
- **Verification Outcome — run 30's round**: **T1 (disambiguation)**: criteria (1) and (3) **not
  exercised** — no colliding title was requested on any of 12 `wiki_view` rows; (2) **met** (Q1 header
  `Races/Gnoll.md`, Completeness 5/6); (4) **breached as written** on "confirmed critical errors 0" (2
  published, 1 genuine) — **kept, not reverted**: neither critical error's question exercised the
  changed branch, so the criterion's side-effect clause was too broad, and the *unplanned* criterion —
  the `.md`-suffix trap — is **verified**: 8 of 12 `wiki_view` calls carried `.md` and every one
  resolved (run 30: 2 misses, 2 wrong articles). **T2 (`FindDefinition`)**: 79 ms ≤ 200 — verified.
  **T3 (dot-directory exclusion)**: consistent. **T6 (*Melee Weapons* note)**: corrected text reached
  the model in Q11's snippet. **Run-29 T1 (openers)**: reported 1, by hand **4** (Q2, Q4, Q12, Q18) —
  **≥ 3 on two consecutive runs; the rung-7 candidate is triggered** and deferred with reasons (no
  measurable quality cost; the rule is already in `_policy.md`; the detector is widened instead).
- **Transfer Action**: **T1** — `wiki_view` section matching normalised (leading non-letter symbols
  and whitespace ignored), heading list appended to the section-miss line, at **rung 3, tool
  contract**; criterion and rollback in the run-31 analysis § 4. **T2** — `monster_lookup.md` and
  `wiki_search.md` state that a monster page's `Level N` header is its difficulty and `Hit dice` its
  level, at **rung 3**. **C1** — `winprocs.h` allow-listed in `SourceCodeService` (moves no fingerprint).
  **H1** — per-question grading pipelined concurrently with the next candidate turn; sample top-up
  opinions run concurrently. **H2** — the benchmark candidate request now carries the segmented
  prompt, so the frozen and session segments are cached across questions; `CandidateSystemPromptSha256`
  is asserted unchanged. **H3** — `AnswerFramingRegex` widened for source-as-subject openers. No
  `HarnessVersion` or `ScoringMethodVersion` bump: the round moves `ToolGuidesSha256` and nothing
  else a run records. **S1, S2** join the deferred single suite-6 rubric repair.
- **Verification Outcome (for this round)**: the next run at run 31's roster settles § 4's T1, T2 and
  the H1/H2 counts. **Do not change the grader roster before it.**

### Run 32 — 2026-09-10: Claude 5 Sonnet (the confirming run for run 31's round)
- **Candidate**: Claude 5 Sonnet (`claude-sonnet-5`), thinking `high`, parallel tool calls on, max
  output 128000. Suite 6, 18 questions, sequential.
- **Prompt options**: `overseerMode` 0; `verboseMode` false; `spoilerFreeMode` false;
  `enableToolUse` true; `enableWebSearch` false; `enableSubAgents` false;
  `allowSourceCodeReferences` true; `isGameOn`, `hasGameSnapshot`, `hasMessageHistory`,
  `hasWikiContext` all false; `parallelMode` Enabled.
- **Grading regime**: harness 18, scoring method 10, profile *Standard Intelligence Index* (1),
  budget 45 flat. Assessor Gemini 3.7 Flash @ `high`; second opinion GPT-5.6 Luna @ `max`, blind,
  FlaggedPlusSample; claim verifier GPT-5.6 Luna @ `max`. **Identical roster to run 31.**
- **Instrument SHAs**: `CandidateSystemPromptSha256 = 851af9406949b176e72442f26ed6f7f77aa27dcd85eeb52162be0405c1942a00`
  (= runs 29–31); `ToolGuidesSha256 = 485ea404ad558c7dc62b0463b037d51d231f44ebe5a2c8b41f6d6ecdb5ad99f3`
  (moved: run-31 round); `KnowledgeBaseHeadSha = 576ca5741d1bd79ef1cb2f7db575709cf0bb0db8` (= runs
  16–31); `WikiHeadSha = a31dfc2f9461a33e0607ebe5bf49c336d40e942d` (= run 31);
  `SourceCodeHeadSha = 3861281ec5bd39a6de5e75c1f91c5ddc4db19a42` (= runs 28–31).
- **Quality**: Intelligence Index **91 ± 7**; raw 92; unweighted 91; holistic 90. Accuracy 94.2 /
  L 5.6; Completeness 87.4 / L 5.1; Conciseness 91.3 / L 5.3; Readability 91.3 / L 5.3. **1 critical
  error (Q5, genuine, repeat of run 31 — an omission the wiki itself carries in the adjacent
  section)**; 1 refuted claim (Q3, verifier correct: `DRGN_ARMR` passes `mgc = 1`); 1 disputed (Q9).
  Agreement −11.5 signed over **2 of 18** — 3 of 5 second opinions failed to parse, so the figure is
  near-meaningless for this run. Out-of-scope 2; FORM 16; band drift +28.9 (suite constant).
- **Speed**: median model time **16,369 ms**; P90 75,782; max 79,124 (Q18). TTFT median **3,185**.
  Speed Index 96, saturated 14 of 18, advisory twice over.
- **Cost**: $2.12 — candidate $1.03 (48 %; cache write $0.40), grading $1.09 (52 %; second opinion
  $0.25 for 2 usable verdicts). 72 tool calls (4.0/q, **0 failed, 0 refused**) — Wiki 48.6 %, Source
  40.3 %, Structured 6.9 %, KB 4.2 %. 69 model calls; 1,895,902 in / 27,847 out; cache read
  **91.5 %**. Wall 22m 36s; answering 7m 19s; **second opinion 26m 33s** (≈ 41k output tokens and
  ≈ 5.3 min per call at `max`), the critical path.
- **Comparability**: **one Instrument key differs from run 31 (`ToolGuidesSha256`) → Tier C.** The
  first single-key confirming run since 28→29.
- **Verification Outcome — run 31's round**: T1 (`wiki_view` section normalisation + heading list)
  **verified** (Q4 `Gilthoniel` 853 chars, `Morgoth` 650; Q7 section miss returned the heading list);
  T2 (`Level N` = difficulty guide text) **partially met** — Q13 right, Q14 wrong in its opener; C1
  (`winprocs.h`) **verified**; H1 (pipelining) **verified** — stage sum exceeds wall by 961 s; H2
  (segmented candidate prompt) **verified** — cache read 83.3 → 91.5 %, candidate cost $1.35 → $1.03
  at identical input volume, TTFT 4,120 → 3,185 ms; H3 (opener detector) **under-counts** — 1
  flagged, 3 by hand (Q2, Q7, Q12); third consecutive run ≥ 3.
- **Transfer Action**: **T1** `Praying.md` § *95 % Chance Safe Thresholds* cross-references the
  trouble thresholds, at **rung 2**, AI-authored from `src/pray.c:3675`, in the wiki repository's
  own session. **T2** the opener wording change is **deferred one round** so that the prompt hash
  and tool guides stay still while the grader roster moves (see below). **T3** upstream monster-page
  header recorded. **T5** no chat change; segmentation exhausted as a cost lever. Harness: **H1**
  second-opinion parse robustness (balanced-value extraction, JSON-only re-ask, raw head preserved,
  timeout), **H2** negative-overlap report line, **H3** detector widened, **H4** source indexer skips
  dot-directory segments (moves no fingerprint), **H5** run-dialog question list scrolls with the
  dialog. No `HarnessVersion` or `ScoringMethodVersion` bump: the code in this round changes nothing
  a run records. **By operator decision the grader roster moves before run 33**: both
  `SecondOpinionConfiguration` and `ClaimVerifierConfiguration` (GPT-5.6 Luna) go from `max` to one
  chosen thinking level (`high` recommended; the run-33 analysis records the exact levels). That is
  **two Instrument keys from run 32 → below Tier B**, so run 33 verifies this round's countable
  criteria only and is not a Tier-C reproduction of run 32; the first agreement and verifier figures
  at the new level are a baseline, not a comparison. `_policy.md` is untouched.
- **Verification Outcome (for this round)**: run 33 settles the plan's § Verification criteria —
  second-opinion parse failures 0, stage time under 10 minutes, no second opinion reaching the
  timeout, overlap line well-formed, detector count = hand count, `.vs` absent from
  `list_indexed_files`, Q5 Completeness ≥ 4 with no critical error once the wiki commit has been
  polled in. **Do not edit `_policy.md`, and change no grader beyond the two recorded thinking-level
  moves, before it.**

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
