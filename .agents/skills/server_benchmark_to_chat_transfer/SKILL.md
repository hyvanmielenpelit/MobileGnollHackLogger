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
- **Version currency.** The bullets above were last checked against harness **18**, during the run-34 analysis (2026-09-10). `BenchmarkAssessmentPrompt.HarnessVersion` is the source of truth for the current value; if it now reads higher, treat this section as possibly aged and verify every claim against `server_benchmark_tool_diagnostics` § 2 before relying on it. This section silently aged out at harness 17 once already, and cost a run-28 analysis its tool-layer evidence — that is why this line exists.

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

### Runs 11–30 — 2026-09-04 to 2026-09-10 (pruned per the rule above)

Full entries were collapsed on 2026-09-08 when this registry passed the pruning threshold, again on 2026-09-10 when runs 28 and 29 became the most recent pair, and once more on 2026-09-10 in the run-34 round, which folded runs 28–30 into this table and kept runs 31–34 in full. What each run established is preserved below; the reports themselves remain the primary source.

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
| 28 | 2026-09-09 | Claude 5 Sonnet (`high`) | 84 ± 11 | The first Anthropic candidate; harness 17 with **no comparable predecessor** (three method resets separate it from every earlier completed run), so every finding was motivating only. A rung-3 round: `get_item_stats` Level 1 with its unit notes, the monster `ac`/`mc`/`mr` scales in `get_monster_stats.md`, the `source_code_search` matcher contract and near-miss payload, `_policy.md` answer-opening and miss-recovery text, and `ToolExecutor`'s truncation suffix `... [Truncated: showing …]` — a stored **10033** is the pre-round marker length, ≈ 10117 after it, so match the `[Truncated:` prefix, never a length. **No version bump by design: compare across this round by the instrument fingerprints, not by the harness version** (post-round `ToolGuidesSha256 ed93e73d…`, `CandidateSystemPromptSha256 851af940…`). Two suite defects proven here and still open: **Q3's critical error is spurious** — its rubric states `objects.c` macro *arguments* (base AC 1, spellcasting penalty 5), not player-visible values, and the repair is a units rule across every numeric rubric point — and **all 18 FORM sections are labelled `(readability)`**. Repairing the live rubrics is a **Fundamental** comparability break and must be recorded in this registry when it lands. |
| 29 | 2026-09-10 | Claude 5 Sonnet (`high`) | 92 ± 3 (17 items) | Confirming run for 28's round: **passed on every countable criterion except the answer-opening rule** (bare `source_code_search` misses 12 → 0, tool calls 114 → 85, critical errors 2 → 0). Rung 3: wiki-family near-miss payloads; rung 4: `wiki_search`'s 13,000 result-cap floor and `nethack_wiki_search`'s per-article cap. The **harness-18 round** followed it and moved two instrument keys (`HarnessVersion`, `ToolGuidesSha256`) but **not** `CandidateSystemPromptSha256` — per-tool guides are not inlined into the prompt. **T4 correction**: the Gnoll "content gap", raised six times, was never an access problem — `Races/Gnoll.md` was present and indexed on every run, and two of the six "missing" facts were in it. Its two method lessons bind: **check a content gap on disk before filing it** (§ 2 category 4, `server_tool_data_sources` § 5), and **diff an assessor's "missing" list against the page**. **Band drift diagnosed**: `BenchmarkDifficultyPrompt` is at fault, not the authored bands, so the deferred repair fixes the prompt and leaves the bands alone. `Overseer/appsettings.json` tool limits carry no fingerprint. |
| 30 | 2026-09-10 | Claude 5 Sonnet (`high`) | 90 ± 4 | The first harness-18 run; below Tier B against 29 (three keys, including a claim-verifier change made on the wrong side of the pair). It measured that **a per-tool guide edit moves `ToolGuidesSha256` only** (`CandidateSystemPromptSha256` = run 29). Rung 3: `wiki_view` title-collision disambiguation; `FindDefinition` given a literal pre-filter (it had cost 8.8 s of the run's 10.5 s of tool time); the wiki indexer skips dot-directories. The newly shipped tool-call log found at rung 0 the **`.md`-suffix trap** — 6 of 20 `wiki_view` calls passed the filename, single-word ones missed and multi-word ones returned the wrong article — folded into the same round. Method lesson: **read the run's own arguments before inferring anything from result lengths**. Two skill errors corrected (no code emits a `wiki_view` section-miss payload opening `Article matching '`). Verifier caution, fourth instance (Q11 trident range, `src/apply.c:5233`). |

The full entries for runs 28–30 — the run-28 Q3 units proof with its source lines, the run-29 T4
correction in full, the run-30 N1–N3 detail and the harness-18 comparability reasoning — are in
`benchmark_run_28_analysis/` (2026-09-09) and `benchmark_run_29_analysis/`, `benchmark_run_30_analysis/`
(2026-09-10) under `hyvanmielenpelit/MobileGnollHackLogger/` in the plans repository, and verbatim in
this file's Git history before the run-34 round. The harness-18 changes themselves are documented in
`docs/overseer/ai-benchmark.md` § *Harness Version 18 Updates*.

Four standing cautions from these entries, kept because they still bind:

- **The scoring-profile comparability key was widened, then narrowed (H9, resolved 2026-09-07).** It now hashes `BenchmarkScoringProfileService.CanonicalSignature` — the profile's scoring semantics only — recomputed from each run's stored snapshot, so nothing that matched before stopped matching. Every stored **group analysis** should still be re-analysed so its recorded `ComparabilityKeyHash` reflects the narrowed definition.
- **Runs 11–14 and runs 16–21 sit on opposite sides of the v7 → v8 reset**, and everything before run 22 sits on the far side of v8 → v9 (Readability) and v9 → v10 (unanswered questions). Three resets now separate run 11 from the present.
- **A refutation is advisory evidence, and a human reads the cited line before any change rests on it.** On run 24 Q1 the claim verifier produced **two** refutations and **both were wrong**, each with a confident citation, and both are recorded permanently in that report and flagged as `BenchmarkAnswerFlags.RefutedClaim`.
  - It refuted *"Gnolls can be played by the following six roles"*, citing `src/role.c:1228` — a line inside the **`races[]`** array, the Gnoll *race* entry's alignment mask, while `MH_GNOLL` appears in exactly **six `roles[]` entries**: Barbarian (`:141`), Caveman/Cavewoman (`:220`), Healer (`:299`), Priest/Priestess (`:540`), Rogue (`:621`), Ranger (`:713`).
  - It refuted the claimed Yeenaghu peace and wish, citing `M2_HOSTILE` on `src/monst.c:5669`. **That verdict was recorded here as standing until 2026-09-10, and it is false.** `peace_minded()` (`src/makemon.c:4451`) returns `TRUE` for Yeenaghu **and** for `PM_HYENA` against a gnoll player *unconditionally*, ahead of `always_peaceful()` and ahead of every alignment test — `M2_HOSTILE` is the default flag that check overrides. And `src/minion.c:799-830` grants a real wish through `mongrantswish()`, gated on chaotic with an alignment record ≥ 14, or a luck roll for a chaotic or neutral gnoll, or carrying the Howling Flail, with `context.yeenaghu_wishes` making repeats progressively rarer.
  - **The shared failure mode is worth more than either instance:** the verifier cites a *default flag or the wrong array element* and misses the special-case code that overrides it. Both errors are on one answer, about one race, and both survived into a published report. **No code fix applies** — the verifier is a model, its output is advisory and folded into no index, and that containment is what limited the damage — but a refutation about GnollHack-specific racial behaviour should be treated as **unverified until a human reads the overriding code path**, not as evidence.
- **Two controlled runs remain deferred, and both must run under v10 rather than across the boundary**: (a) one model, one suite, `verboseMode` false vs. true, to settle whether verbosity buys Completeness — cheap now that `e9b3e9a752…`/`bb19dc24…` is a known isolated pair; and (b) a tool-policy variant run, to test whether the source-family-share-versus-latency correlation is causal.

**Correction, 2026-09-10 (run-33 H1): every Gemini-role token and cost figure in this registry —
the table above and every entry below — is inflated.** Until the run-33 round `GoogleProvider`
emitted one usage report per streamed chunk and `AgentLoopRunner` summed them, so each Gemini model
call was counted roughly once per chunk (≈ 11× on run 33). That covers the primary assessor and the
synthesis on every run, and the candidate on run 22 — its tokens, its cost and possibly its 21.1 %
cache-read share. Anthropic and OpenAI figures are unaffected. Every "grading share" below overstates
the assessor, and a cost comparison across the fix is not like-for-like. The stored figures are not
rewritten; see `docs/overseer/ai-benchmark.md` § *Harness Version 18 Updates*.

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

### Run 33 — 2026-09-10: Claude 5 Sonnet (the confirming run for run 32's round)
- **Candidate**: Claude 5 Sonnet (`claude-sonnet-5`), thinking `high`, parallel tool calls on. Suite
  6, 18 questions.
- **Prompt options**: `overseerMode` 0; `verboseMode` false; `spoilerFreeMode` false;
  `enableToolUse` true; `enableWebSearch` false; `enableSubAgents` false;
  `allowSourceCodeReferences` true; `isGameOn`, `hasGameSnapshot`, `hasMessageHistory`,
  `hasWikiContext` all false; `parallelMode` Enabled. Identical to run 32.
- **Grading regime**: harness 18, scoring method 10. Assessor Gemini 3.7 Flash @ `high`; **second
  opinion GPT-5.6 Luna @ `xhigh`** (reasoning `standard`), blind, FlaggedPlusSample; **claim verifier
  GPT-5.6 Luna @ `xhigh`**. Both moved from run 32's `max` by operator decision (the run-32 entry
  recommended `high`).
- **Instrument SHAs**: `CandidateSystemPromptSha256 = 851af9406949b176e72442f26ed6f7f77aa27dcd85eeb52162be0405c1942a00`
  (= runs 29–32); `ToolGuidesSha256 = 485ea404ad558c7dc62b0463b037d51d231f44ebe5a2c8b41f6d6ecdb5ad99f3`
  (= run 32); `KnowledgeBaseHeadSha = 576ca5741d1bd79ef1cb2f7db575709cf0bb0db8` (= runs 16–32);
  `WikiHeadSha = 9768d965…` (moved: the run-32 T1 `Praying.md` commit; provenance, not a key — the
  analysis recorded the prefix only); `SourceCodeHeadSha = 3861281ec5bd39a6de5e75c1f91c5ddc4db19a42`
  (= runs 28–32).
- **Quality**: Intelligence Index **92 ± 5**. Accuracy 94.6; Completeness 88.5; Conciseness 89.8;
  Readability 92.1. **0 critical errors; 0 refuted claims of 20 verified** (verifier 20 supported).
  Agreement stored −0.25 signed (printed −0.3 on the card and in diagnostics, −0.2 in the report —
  H2) / 14.8 absolute over 4 of 18; two disagreements in **opposite** directions (Q1 −30, Q16 +16),
  unlike runs 29–31. Q16 (60) is the lowest answer and a rubric defect (S1). Three Accuracy
  deductions are grader errors the instrument cannot see (H4): Q10 (the assessor inverted mithril and
  hard crystal against `Object Materials.md`), Q6 (docked for not citing C), Q16 (inherited from the
  rubric).
- **Speed**: median model time **16,111 ms**; P90 71,853; max 93,082 (Q18). TTFT median 3,549. Flat
  against runs 29–32 within every interval.
- **Cost**: **$1.97 as reported, inflated by H1** — the assessor's $0.66 is roughly $0.06 and the
  synthesis's $0.21 roughly $0.02 once each Gemini call is counted once, so the run is roughly $1.2
  and grading roughly 15 % of it, not 49 %. 69 tool calls (3.8/q, **0 failed, 0 refused**); candidate
  cache read **92.0 %**; cache write $0.37 = 37 % of candidate cost (multi-round tool results, not the
  prompt). Wall **11m 29s** (run 32: 22m 36s); answering 7m 27s; second opinion **5m 35s** for 4
  opinions (run 32: 26m 33s at `max`).
- **Comparability**: two Instrument keys differ from run 32 (`SecondOpinionConfiguration`,
  `ClaimVerifierConfiguration`) → **NotComparable, below Tier B**. Agreement and verifier figures are
  the **baseline at `xhigh`**, not a comparison.
- **Verification Outcome — run 32's round**: second-opinion parse failures 0 **met** (4 of 4);
  stage under 10 minutes **met**; no second opinion at the timeout **met**; overlap line well-formed
  **met** (7m 21s, arithmetic checks); `.vs` absent from `list_indexed_files` **not exercised**; Q5
  Completeness ≥ 4 with no critical error once the wiki commit was polled in **met — run-32 T1
  verified**; detector count = hand count **missed** — 2 flagged (Q2, Q17), 3 by hand (Q7 *"This
  gives a clear, well-documented answer straight from the wiki…"*); by hand 3, 3, 4, 3, 3 on runs
  29–33. The run-31 `.md`-suffix and section normalisation still hold.
- **Transfer Action**: **T1** — `_policy.md`'s answer-opening rule gains one sentence naming the
  *has this documented / covers this well / gives a clear answer* shape, at **rung 3**. It moves
  `ToolGuidesSha256` **and** `CandidateSystemPromptSha256` (`_policy.md` is inlined into the frozen
  segment) and invalidates the frozen prompt cache once. **T2** — one shared
  `MarkdownSectionExtractor`: `nethack_wiki_view` gains `wiki_view`'s normalised pass and heading
  list, and both gain a unique-substring pass; `nethack_wiki_view.md` states it; **rung 3, tool
  contract**. **S2** — `Races/Gnoll.md`'s "not a repeatable source of wishes" replaced with the
  source's odds (1 in 3·*n* after *n* wishes, `src/minion.c:816-827`), **rung 2**, applied by hand in
  the wiki repository. Harness: **H1** Gemini usage emitted once per model call; **H2** agreement
  deltas rounded once at storage (1 decimal, away from zero); **H3** detector widened
  (adjective-separated "clear … answer"; `gives|provides|offers` in the source-as-object form). No
  `HarnessVersion` or `ScoringMethodVersion` bump; the H1 correction is dated in
  `docs/overseer/ai-benchmark.md` and beneath this registry's pruned-runs table. **H4** (deduction
  verification: send an evidenced Accuracy deduction's claim to the claim verifier, ≈ $0.03 per run)
  is proposed for its own plan, not built. **T3–T5** recorded, no change. **S1** joins the deferred
  suite-6 rubric repair.
- **Verifier caution, fifth instance** (S2): Q1's *"not repeatable"* was marked **supported**
  against `Races/Gnoll.md`'s old sentence, while `src/minion.c` grants repeats at
  `!rn2(3 * max(1, context.yeenaghu_wishes))`. A "supported" verdict against a secondary text is not
  a verdict against the source.
- **Verification Outcome (for this round)**: run 34 at run 33's roster, unchanged. Criteria: opener
  hand count ≤ 1 and detector = hand count, with Accuracy and Completeness within run 33's ± 5 —
  **revert the T1 sentence** if the count is ≥ 3 again or either dimension falls by more than 5;
  every `nethack_wiki_view` section request either hits or returns the heading list — **revert the
  substring pass** if any call returns a section an exact heading would not have given; assessor
  input per assessment within 2× the second opinion's per-call figure and assessor cost < $0.10; one
  signed-delta value on the card, in the report and in diagnostics. Run 34 differs from run 33 by the
  two prompt fingerprints, so it verifies by these countable criteria, not by a Tier-B comparison.
  **Do not change the grader roster before run 34.**

### Run 34 — 2026-09-10: Claude 5 Sonnet (the confirming run for run 33's round)
- **Candidate**: Claude 5 Sonnet (`claude-sonnet-5`), thinking `high`, parallel tool calls on. Suite
  6, 18 questions, sequential.
- **Prompt options**: `overseerMode` 0; `verboseMode` false; `spoilerFreeMode` false;
  `enableToolUse` true; `enableWebSearch` false; `enableSubAgents` false;
  `allowSourceCodeReferences` true; `isGameOn`, `hasGameSnapshot`, `hasMessageHistory`,
  `hasWikiContext` all false; `parallelMode` Enabled. Identical to runs 29–33.
- **Grading regime**: harness 18, scoring method 10, profile *Standard Intelligence Index* (1),
  budget 45 flat. Assessor Gemini 3.7 Flash @ `high`; second opinion GPT-5.6 Luna @ `xhigh`, blind,
  FlaggedPlusSample; claim verifier GPT-5.6 Luna @ `xhigh`. **Identical roster to run 33.**
- **Instrument SHAs**: `CandidateSystemPromptSha256 = 22006c75994e4f1b5fb89d3b2f31e6bed4ba1807d6e56970ee54622ad439fba9`
  (moved: run-33 T1); `ToolGuidesSha256 = a5f8a25f6e96058342663179b8367254d0e4ced0d7f62bf8e5adad5ff1b543d0`
  (moved: run-33 T1 + T2); `KnowledgeBaseHeadSha = 576ca5741d1bd79ef1cb2f7db575709cf0bb0db8` (= runs
  16–33); `WikiHeadSha = 1ecb24d99d836402b04f9cde09d64e9723a36ae6` (moved: run-33 S2; provenance);
  `SourceCodeHeadSha = 3861281ec5bd39a6de5e75c1f91c5ddc4db19a42` (= runs 28–33). All three corpus
  fingerprints re-read from disk on 2026-09-10 and matching.
- **Quality**: Intelligence Index **93 ± 4**; raw 93; unweighted 93; holistic 93. Accuracy 96.3 /
  L 5.7; Completeness 89.3 / L 5.2; Conciseness 91.3 / L 5.3; Readability 91.3 / L 5.3. **0 critical
  errors; 0 refuted of 15 verified** (13 supported, 2 indeterminate — both Q16 encounter-system
  claims, supported by an on-disk read of `src/encounter.c`). Agreement −20.7 signed / 20.7 abs over
  3 of 18 (a fourth, Q3, timed out); 2 disagreements (Q14 87 → 60, Q16 83 → 60), both lower.
  Out-of-scope 4; FORM 14; band drift +28.9 (suite constant). `OutOfRubricAccuracyDeduction` 1 (Q3 —
  the assessor was right: `src/mon.c:425-473` drops scales on death).
- **Speed**: median model time **16,396 ms**; P90 41,777; max 91,486 (Q16, 15 model calls). TTFT
  median 3,399. Flat against runs 31–33.
- **Cost**: **$1.25** — candidate $0.93 (74 %; cache write $0.33), grading $0.32 (26 %; second
  opinion $0.21). 74 tool calls (4.1/q, **0 failed, 0 refused**) — Source 50.0 %, Wiki 39.2 %,
  Structured 6.8 %, KB 4.1 %. 69 model calls; 1,921,601 in / 23,626 out; cache read **93.0 %**. Wall
  **23m 44s** (run 33: 11m 29s) — answering 6m 32s, **second opinion 21m 40s, of which one 900-s
  timeout on Q3** at `xhigh`. Assessor $0.06 (run-33 H1 verified: 3,412 tokens per assessment).
- **Comparability**: two Instrument keys differ from run 33 (the two prompt fingerprints) → **below
  Tier B**; verifies run 33's countable criteria only.
- **Verification Outcome — run 33's round**: **T1 (opener sentence) MISSED — rollback trigger
  fired**: hand count 4 (Q2, Q5, Q16, Q17), detector 2; Accuracy +1.7 and Completeness +0.8 inside
  ± 5. Hand counts runs 29–34: 3, 3, 4, 3, 3, 4. **T2 (`nethack_wiki_view` substring pass) not
  exercised** — zero calls; `wiki_view`'s normalised pass re-verified 2 of 2 (Q11 `Combat and
  Items`, Q18 `Elbereth`). **H1 met** (assessor input per assessment 0.99× the second opinion's
  per-call figure; assessor $0.06 < $0.10). **H2 met** (−20.7 in card, report and diagnostics).
  **H3 (detector = hand count) missed**, sixth consecutive under-count — Q5 *"This has full detail —
  here's the rundown"* and Q17 *"This covers it well — here's the summary:"* matched no alternative.
- **Transfer Action**: **T1** the run-33 `_policy.md` sentence **reverted** (rung 3), per the
  pre-declared trigger; no further prose attempt — the rung-7 candidate stays deferred, a positively
  framed variant in a controlled pair being the only untried shape. **T2** `get_function_definition`
  continuation: `start_line` accepts the truncation notice's 1-based output line or an absolute file
  line inside the definition, and returns an explicit out-of-range message instead of clamping to the
  last line; the truncation notice names the next unseen line in both numberings (*"… Call again with
  start_line=151 (file line 1480) to continue."* for `create_encounter`), and the header's line count
  no longer includes a phantom trailing line (235, not 236, for L1330–L1564); schema, guide and `server_tool_parameter_reference` § 4 state it —
  **rung 3, tool contract** (Q16 call 19 returned 65 characters). **T3** `get_monster_stats`
  `mattk[].dice` string and a guide line — **rung 3** (Q14 transposed 16d2). **T4** `Saving
  Throws.md` reachable but unlinked from the spell pages — **rung 2**, recorded for the wiki
  repository's session. **T5** Q3 fabricated acquisition — second observation of the run-13 T11
  shape, recorded. **T6** cache lever exhausted (93.0 %); a rung-6 thinking-`medium` series recorded
  as the next cost/speed experiment. **T7** `search_definitions.md` bare-symbol line. **H6**
  `_policy.md` truncated-results marker updated to `... [Truncated: showing …]`, the retired marker
  kept in one clause for older conversations. Harness: **H1** Run Integrity Notice wording when some
  second opinions completed; **H2** detector widened (pronoun-subject `has/covers … full detail / it
  well`, and the *"— here's the rundown/summary"* tail); **H4** yield-line precision. No
  `HarnessVersion` or `ScoringMethodVersion` bump: the round moves `ToolGuidesSha256` and
  `CandidateSystemPromptSha256` (the `_policy.md` edit) and nothing else a run records.
  `ToolGuidesSha256` after the round: `2c0a8acae3887dca86a54d326c01f89df010e5cedea6ec4fbd6caeb3445c2559`
  (computed over the built `ToolGuides` output directory, 34 files, by the manifest algorithm
  `BenchmarkService.ComputeToolGuidesSha256` uses; the same computation over the pre-round build
  reproduced run 34's `a5f8a25f…`); `CandidateSystemPromptSha256` after the round is read from run 35.
- **Grader roster and timeout, decided 2026-09-10**: the operator moves the second opinion (GPT-5.6
  Luna) from `xhigh` to **`high`** in the admin UI before run 35; the claim verifier stays at `xhigh`.
  `SecondOpinionConfiguration` therefore moves too — at no extra comparability cost, since run 35 is
  already below Tier B from the two prompt fingerprints and no scored criterion reads the second
  opinion — and **run 35's agreement figure is the baseline at `high`, not a comparison** with run
  34's. `Benchmark:SecondOpinion:TimeoutSeconds` goes 900 → **600** in `Overseer/appsettings.json`;
  no run record fingerprints it, so it is recorded here: runs up to 34 ran at 900 s, runs from 35 at
  600 s.
- **Suite defects still open**: S1–S5 as before; **S6 (new)** Q13's REQUIRED
  nutrition/sound/speaking/diet points for a combat-profile question drew a Completeness charge
  without the out-of-scope marker. All deferred to the single suite-6 break.
- **Verification Outcome (for this round)**: run 35 with the second opinion at `high` (if the move
  was not made, run 35 is recorded at `xhigh` and the move waits for the next series). Three
  Instrument keys differ from run 34 (two prompt fingerprints and `SecondOpinionConfiguration`), so it
  verifies by these countable criteria: opener hand count reported (no target — the sentence is gone)
  with Accuracy and Completeness within run 34's ± 5; detector count = hand count; **every
  `get_function_definition` call with `start_line` returns ≥ 2 body lines or the explicit
  out-of-range message — revert T2 if any continuation returns a region other than the one
  requested**; **if Q14 reaches `get_monster_stats`, its claw dice read 16d2 and no
  `get_monster_stats` result exceeds the cap — revert T3 on any "Result too large"**; the Run
  Integrity Notice reads "measured over N" whenever coverage > 0; `$/claim` prints three decimals; no
  second opinion reaches 600 s. **Do not change the assessor or the claim verifier before run 35.**

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
