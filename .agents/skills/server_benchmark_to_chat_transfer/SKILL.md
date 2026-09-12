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
2. **Suite Defect**: The benchmark question, rubric ground truth, or difficulty rating is incorrect, ambiguous, or outdated. A suite repair to a default suite is complete only when its file under `Overseer/Data/DefaultSuites/` carries the same text — see [`server_rubric_handoff`](../server_rubric_handoff/SKILL.md) § 3a; a custom suite is repaired in the database only.
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
- **Provider-side request settings** — prompt caching mode, service tier, thinking level, parallel tool calls. These are part of the measured configuration. The benchmark and live chat run under the same provider settings, always: a setting enabled for benchmark runs only makes every cost, latency and cache figure in a report a measurement of a configuration chat does not have, and a finding that does not transfer. Decided 2026-09-12 (run-40 round) when a benchmark-only Gemini explicit cache was considered and rejected on this ground.

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
- **Version currency.** The bullets above were last checked against harness **25**, during the run-40 round (2026-09-12). `BenchmarkAssessmentPrompt.HarnessVersion` is the source of truth for the current value; if it now reads higher, treat this section as possibly aged and verify every claim against `server_benchmark_tool_diagnostics` § 2 before relying on it. This section silently aged out at harness 17 once already, and cost a run-28 analysis its tool-layer evidence — that is why this line exists.

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
- **Two advisory sensitivity indices sit beside the Intelligence Index, and neither is a score.** *Contested-Verdict Sensitivity* resolves every critical-error split at the second reader's figure. From harness 25, *Verification-cleared Accuracy Sensitivity* recomputes the index with Accuracy one level higher on every answer whose Accuracy was docked citing only claims the rubric did not cover, where the verifier later supported every such claim it checked — the instrument's share of the Accuracy shortfall, as `OUT-OF-SCOPE:` is of Completeness. Both are omitted when their population is empty, and neither moves a stored number. Read them as the width of the grading's own uncertainty, never as a corrected result.
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
   - **A wiki edit leaves this repository as a handoff prompt for a separate session in the
     `WikiPath` clone. Before writing one, complete the pre-flight checklist in
     [`server_wiki_handoff`](../server_wiki_handoff/SKILL.md) and use its template.** The
     run-34 handoff (2026-09-10) presumed a page generator that does not exist and named
     `Resistances and Saving Throws.md` as the formula's home when the formula is in
     `Saving Throws.md`; its prescribed verification grep would have returned zero. Both were
     checkable read-only on disk. The same rule that binds content gaps (§ 2 category 4: check
     on disk before filing) binds handoff prompts.
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

### Runs 11–35 — 2026-09-04 to 2026-09-10 (pruned per the rule above)

Full entries were collapsed on 2026-09-08 when this registry passed the pruning threshold, again on 2026-09-10 when runs 28 and 29 became the most recent pair, once more on 2026-09-10 in the run-34 round, which folded runs 28–30 into this table and kept runs 31–34 in full, again on 2026-09-10 in the run-35 round, which folded runs 31–32 in and kept runs 33–35 in full, again on 2026-09-11 in the run-36 round, which folded run 33 in and kept runs 34–36 in full, again on 2026-09-11 in the run-37 round, which folded run 34 in and kept runs 35–37 in full, again on 2026-09-11 in the run-37 re-run round, which folded run 35 in, kept runs 36–37 in full and replaced the partial run-37 entry with the completed run's, again on 2026-09-11 in the run-38 round, which removed the run-35 full entry the previous round's fold had left behind and keeps runs 36–38 in full, again on 2026-09-12 in the run-39 round, which folded run 36 in and kept runs 37–39 in full, and again on 2026-09-12 in the run-40 round, which folded run 37 in and keeps runs 38–40 in full. What each run established is preserved below; the reports themselves remain the primary source.

| Run(s) | Date | Candidate | Intelligence Index | Transfer action, and what it settled |
|---|---|---|---|---|
| 11 | 2026-09-04 | GPT-5.6 Luna (`max`) | 91.5 ± 5.9 | Seeded the rung-1 knowledge base worklist (Q9, Q16) and promoted `verboseMode: true` for testing at run 12. **T3 and T4 were later withdrawn** — both cited prompt rules that do not exist, and were reclassified as harness defects. Graded **anchored** through a missing blind-second-opinion backfill (F1), and its instrument SHAs were never recorded, so **run 11 cannot serve as half of any reproduction.** |
| 12 | 2026-09-05 | GPT-5.6 Luna (`max`) | 91 ± 7 | **T7: `verboseMode: true` refuted.** Completeness moved 83.0 → 83.8 — inside noise — while Accuracy fell 4.2 and mean model time rose 28 %. The concise production default was kept, and has been kept at every re-test since. **T8** (heading-scoped wiki snippets) promoted to rung 3 with a six-measure pre-declared criterion. |
| 13 | 2026-09-05 | GPT-5.6 Luna (`max`) | 94 ± 4 | T11 (fabrication under *partial* retrieval failure) deferred pending a second observation. |
| 14 | 2026-09-06 | GPT-5.6 Luna (`max`) | 94 ± 3 | **T8 verified and kept**: four of six measures met against run 13's one, and both remaining misses moved the intended way. **T15** promoted at rung 3 (`get_monster_stats.md` contradicted `_policy.md`'s exact-stats routing). **T16** — Completeness lowest for a fourth consecutive run — deferred as unattributed, which is what motivated replicate sets. **Comparability reset: `ScoringMethodVersion` 7 → 8**, plus an in-place scoring-profile edit; runs 11–14 keep their value as observations and cease to be reproduction halves. |
| 16–18 | 2026-09-07 | GPT-5.6 Luna (`max`) | **94.42** (multi-run, [91.74, 97.11]) | The project's first R = 3 replicate set. **Reproducibility SD 0.30** — a re-run of this configuration moves the index by well under a point, so a later difference above ~1 point is signal rather than noise. **T19** (the Gnoll-race item) raised as a human-authored article; **T21** ruled claim-verifier spend not chat-transferable. |
| 19–21 | 2026-09-07 | GPT-5.6 Luna (`max`) | **92.63** (multi-run, ± 9.01) | Second R = 3 set, Tier A. **Runs 16–21 share all three instrument SHAs**, making the two sets the project's first replicate-grade reproduction pair; the apparent tenfold jump in reproducibility SD (0.30 → 3.34) is **not** an instrument change, since the two χ²(2) intervals overlap. **T-B** (Q1) again raised at rung 1 — now reclassified to rung 2, see § 7. Budget-key note: H6 widened `BudgetSignature` afterwards, so **runs 19–21 do not share a budget key with runs started later**; extending this series needs a fresh replicate set, not appended members. |
| 22 | 2026-09-07 | Gemini 3.5 Flash-Lite (`high`) | 58 ± 10 | **T23: Google prompt caching implemented at rung 5** — `BuildChatRequestBody` now gives the request a stable byte prefix; explicit `cachedContents` was deliberately not implemented, and **that confirming run is still outstanding** — measured on runs 39—40 (54.3 % / 51.9 %, implicit only) and closed by the 2026-09-12 decision against explicit caching, so the run's 21.1 % cache-read share — against 90 %+ on every Anthropic and OpenAI run — has no established cause. **T25: `RecommendedModels:Google` demoted from this model to `gemini-3.8-flash` @ `high`** at rung 6; that confirming run is **also outstanding**, and the replacement's price advantage is promotional and **doubles on 2027-01-01**. **T24** (unscoped greeting instruction) deferred at rung 7 on one observation. Shares all three instrument SHAs with runs 16–21. **Comparability reset in its round: `ScoringMethodVersion` 8 → 9** (rubric FORM criteria stopped being charged against Readability), so a v8 run is not comparable with a v9 run on Readability. Its **11 refuted claims across 7 answers (24 % rate)** still deserve the re-check the verifier caution below motivates. |
| 24 | 2026-09-08 | Gemini 3.1 Pro (`high`), **`verboseMode: true`** | none — cancelled at 3 of 18 | No transfer action; every finding recorded as motivated-only. **T6** (fabricated Gnoll racial intrinsic and Yeenaghu wish) reclassified rung 1 → **rung 2**, see § 7. **Do not promote `gemini-3.1-pro-preview` to `RecommendedModels:Google`**, which names `gemini-3.8-flash`. Its `CandidateSystemPromptSha256 e9b3e9a752…` is the **`verboseMode: true` twin of `bb19dc24…` on unchanged code** — a known isolated pair, which is what makes a controlled verbose-vs-concise run cheap to set up. **Not a reproduction half:** the suite did not complete. |
| 25 | 2026-09-08 | Gemini 3.1 Pro (`high`), `verboseMode: false` | none — cancelled at 0 of 18 | No transfer action. The second of the two observations behind **T1** — 383 s to first token and 6½ minutes of work for no text at all. `CandidateSystemPromptSha256` byte-identical to runs 16–22. **Comparability reset in its round: `ScoringMethodVersion` 9 → 10** — an unanswered question the model ended normally is now **scored 0** and enters the Intelligence Index, the raw index and the unweighted mean instead of being excused as a transport defect, so a v9-or-earlier run is not comparable with a v10 run on any of those whenever either contains one. The **Speed Index and the run status are unaffected**. **Not a reproduction half.** |
| 28 | 2026-09-09 | Claude 5 Sonnet (`high`) | 84 ± 11 | The first Anthropic candidate; harness 17 with **no comparable predecessor** (three method resets separate it from every earlier completed run), so every finding was motivating only. A rung-3 round: `get_item_stats` Level 1 with its unit notes, the monster `ac`/`mc`/`mr` scales in `get_monster_stats.md`, the `source_code_search` matcher contract and near-miss payload, `_policy.md` answer-opening and miss-recovery text, and `ToolExecutor`'s truncation suffix `... [Truncated: showing …]` — a stored **10033** is the pre-round marker length, ≈ 10117 after it, so match the `[Truncated:` prefix, never a length. **No version bump by design: compare across this round by the instrument fingerprints, not by the harness version** (post-round `ToolGuidesSha256 ed93e73d…`, `CandidateSystemPromptSha256 851af940…`). Two suite defects proven here and still open: **Q3's critical error is spurious** — its rubric states `objects.c` macro *arguments* (base AC 1, spellcasting penalty 5), not player-visible values, and the repair is a units rule across every numeric rubric point — and **all 18 FORM sections are labelled `(readability)`**. Repairing the live rubrics is a **Fundamental** comparability break and must be recorded in this registry when it lands. |
| 29 | 2026-09-10 | Claude 5 Sonnet (`high`) | 92 ± 3 (17 items) | Confirming run for 28's round: **passed on every countable criterion except the answer-opening rule** (bare `source_code_search` misses 12 → 0, tool calls 114 → 85, critical errors 2 → 0). Rung 3: wiki-family near-miss payloads; rung 4: `wiki_search`'s 13,000 result-cap floor and `nethack_wiki_search`'s per-article cap. The **harness-18 round** followed it and moved two instrument keys (`HarnessVersion`, `ToolGuidesSha256`) but **not** `CandidateSystemPromptSha256` — per-tool guides are not inlined into the prompt. **T4 correction**: the Gnoll "content gap", raised six times, was never an access problem — `Races/Gnoll.md` was present and indexed on every run, and two of the six "missing" facts were in it. Its two method lessons bind: **check a content gap on disk before filing it** (§ 2 category 4, `server_tool_data_sources` § 5), and **diff an assessor's "missing" list against the page**. **Band drift diagnosed**: `BenchmarkDifficultyPrompt` is at fault, not the authored bands, so the deferred repair fixes the prompt and leaves the bands alone. `Overseer/appsettings.json` tool limits carry no fingerprint. |
| 30 | 2026-09-10 | Claude 5 Sonnet (`high`) | 90 ± 4 | The first harness-18 run; below Tier B against 29 (three keys, including a claim-verifier change made on the wrong side of the pair). It measured that **a per-tool guide edit moves `ToolGuidesSha256` only** (`CandidateSystemPromptSha256` = run 29). Rung 3: `wiki_view` title-collision disambiguation; `FindDefinition` given a literal pre-filter (it had cost 8.8 s of the run's 10.5 s of tool time); the wiki indexer skips dot-directories. The newly shipped tool-call log found at rung 0 the **`.md`-suffix trap** — 6 of 20 `wiki_view` calls passed the filename, single-word ones missed and multi-word ones returned the wrong article — folded into the same round. Method lesson: **read the run's own arguments before inferring anything from result lengths**. Two skill errors corrected (no code emits a `wiki_view` section-miss payload opening `Article matching '`). Verifier caution, fourth instance (Q11 trident range, `src/apply.c:5233`). |
| 31 | 2026-09-10 | Claude 5 Sonnet (`high`) | 86 ± 12 | The confirming run for run 30's round: **verified** the `.md`-suffix fix (8 of 12 `wiki_view` calls carried `.md`, all resolved, against run 30's 2 misses and 2 wrong articles), `FindDefinition`'s literal pre-filter at 79 ms, H1 per-question grading pipelined, and H2 the segmented candidate prompt. Of its two published critical errors (Q5, Q18), only Q5 was genuine — Q18 was a rubric defect proven against `src/engrave.c`. Second opinion ran serial and inline and evicted the Anthropic prompt cache. |
| 32 | 2026-09-10 | Claude 5 Sonnet (`high`) | 91 ± 7 | The confirming run for run 31's round: **verified** the segmented candidate prompt (cache read 83.3 → 91.5 %, candidate cost $1.35 → $1.03 at identical input volume, TTFT 4,120 → 3,185 ms), `wiki_view` section normalisation and heading list, and second-opinion parse robustness; applied the `Praying.md` rung-2 wiki edit. **The first single-key confirming run since 28→29 (Tier C).** Its second opinion at `max` cost 26m 33s and was the critical path, which is what moved the grader roster to a lower thinking level. |
| 33 | 2026-09-10 | Claude 5 Sonnet (`high`) | 92 ± 5 | run-32 round verified; T1 opener sentence (rung 3, later reverted); T2 shared section extractor; S2 Gnoll wish odds; H1 Gemini usage once per call; H4 deduction-verification proposal (implemented as the harness-20 H2 out-of-rubric adjudication, `ContestedAccuracyDeduction`). |
| 34 | 2026-09-10 | Claude 5 Sonnet (`high`) | 93 ± 4 | run-33 round verified: T1 opener sentence rollback fired (hand count 4 vs. detector 2), H1 and H2 met; T2 `get_function_definition` continuation given an explicit out-of-range contract (rung 3); T3 `get_monster_stats` `mattk[].dice` string fix (rung 3); T4 `Saving Throws.md` reachable but unlinked from spell pages (rung 2, second observation); T5 Q3 fabricated-acquisition second observation recorded; H6 `_policy.md` truncated-results marker updated. No `HarnessVersion` or `ScoringMethodVersion` bump. |
| 35 | 2026-09-10 | Claude 5 Sonnet (`high`) | 90 ± 7 | Closed the Sonnet series: run-34 round verified except the opener detector (seventh consecutive under-count), a spurious Q1 critical error (`src/attrib.c:117`, counterfactual ≈ 92.6) led to the harness-19 `ContestedCriticalError` adjudication, `source_code_view` defaults `start_line` to 1 and the `get_function_definition` miss payload names where the identifier occurs (rung 3), and the saving-throw article was linked from the spell pages (rung 2) — `HarnessVersion` 18 → 19. |
| 36 | 2026-09-10 | GPT-5.6 Sol (`medium`) | 96 ± 2 | New candidate baseline, first three-provider roster (OpenAI candidate, Gemini assessor, Claude second opinion/verifier). Found at rung zero: **C1** — 483 of 781 indexed source candidates are git-ignored `bin`/`obj` build output (corpus defect). Rung 3: **T1** `get_function_definition` `start_line: 0` now means from the beginning (reverses the run-34 test); **T2** `nethack_wiki_search` cap raised to 16,140; **T3** `nethack_wiki_view` gains a title/filename/summary resolution line. Rung 2 **T4** handoff (`Saving Throws.md` slow/hold/fear table). Harness: **H1** claim-verification prompt prefers the implementing code over a data table that may omit a term — `HarnessVersion` 19 → 20; **H2** out-of-rubric-deduction adjudication built (`ContestedAccuracyDeduction`). Verifier-caution instances 8–9 (Q7, Q14 — data table instead of implementing code, at Opus `low`). |
| 37 | 2026-09-11 | GPT-5.6 Sol (`medium`) | 97 ± 2 | Completed by a failed-question re-run: 6 answers from the original execution and 12 re-run under harness-21 code on a row stamped 20. run-36 round **verified** (T1 `start_line: 0` 42/42; T2 the 16,140 `nethack_wiki_search` ceiling; H1 on Q7), and the re-run repaired all 12 with 0 provider errors. Rung 3: **H5** `wiki_search` `max_results` clamped after a 13,117-char cut on Q4; **H6** same-line return types and closing-brace typedefs reachable by `search_definitions` / `get_function_definition` (`libproc.c:470`, `display.c:161`). Harness: re-run timer, dialog height, repaired-run manifest with `RerunHarnessVersion`, **H4** level-5 unevidenced detection — `HarnessVersion` 21 → 22, `ToolGuidesSha256` `3bdaf81f…`. Its Q1 "Matches rubric" beside Accuracy 5 went unflagged, which is the miss run 38 turned into the sentence-form detector. |

The full entries for runs 28–30 — the run-28 Q3 units proof with its source lines, the run-29 T4
correction in full, the run-30 N1–N3 detail and the harness-18 comparability reasoning — are in
`benchmark_run_28_analysis/` (2026-09-09) and `benchmark_run_29_analysis/`, `benchmark_run_30_analysis/`
(2026-09-10) under `hyvanmielenpelit/MobileGnollHackLogger/` in the plans repository, and verbatim in
this file's Git history before the run-34 round. The harness-18 changes themselves are documented in
`docs/overseer/ai-benchmark.md` § *Harness Version 18 Updates*. The full entries for runs 31–32 —
the run-31 `.md`-suffix and `FindDefinition` verification detail and the run-32 segmented-prompt and
grader-roster detail — are likewise in `benchmark_run_31_analysis/` and `benchmark_run_32_analysis/`
(2026-09-10) under `hyvanmielenpelit/MobileGnollHackLogger/` in the plans repository, and verbatim in
this file's Git history before the run-35 round. The full entry for run 33 — the T1 opener-sentence
rung-3 change and its reversion, T2's shared section extractor, S2's Gnoll wish-odds fix and the H4
deduction-verification proposal — is likewise in `benchmark_run_33_analysis/` (2026-09-10) under
`hyvanmielenpelit/MobileGnollHackLogger/` in the plans repository, and verbatim in this file's Git
history before the run-36 round. The full entry for run 34 — the T1 opener-sentence rollback detail,
the T2 `get_function_definition` out-of-range contract and the T4 `Saving Throws.md` link
observation — is likewise in `benchmark_run_34_analysis/` (2026-09-10) under
`hyvanmielenpelit/MobileGnollHackLogger/` in the plans repository, and verbatim in this file's Git
history before the run-37 round. The full entry for run 35 — the spurious Q1 critical error with its
source lines, the verifier-caution sixth and seventh instances and the run-34 round's verification
detail — and the partial run-37 entry written when 12 of 18 had failed, which carries the harness-21
round's transfer action, are likewise in `benchmark_run_35_analysis/` (2026-09-10) and
`benchmark_run_37_analysis/` (2026-09-11) under `hyvanmielenpelit/MobileGnollHackLogger/` in the plans
repository, and verbatim in this file's Git history before the run-37 re-run round. The full entry
for run 36 — the C1 corpus-defect discovery, the `nethack_wiki_search` cap derivation and the
`ContestedAccuracyDeduction` build — is likewise in `benchmark_run_36_analysis/` (2026-09-10) under
`hyvanmielenpelit/MobileGnollHackLogger/` in the plans repository, and verbatim in this file's Git
history before the run-39 round. The full entry for run 37 — the mixed harness 20/21 re-run
detail, the H5 `wiki_search` clamp derivation and the H6 matcher shapes — is likewise in
`benchmark_run_37_rerun_analysis/` (2026-09-11) under `hyvanmielenpelit/MobileGnollHackLogger/` in
the plans repository, and verbatim in this file's Git history before the run-40 round.

Five standing cautions from these entries, kept because they still bind:

- **The scoring-profile comparability key was widened, then narrowed (H9, resolved 2026-09-07).** It now hashes `BenchmarkScoringProfileService.CanonicalSignature` — the profile's scoring semantics only — recomputed from each run's stored snapshot, so nothing that matched before stopped matching. Every stored **group analysis** should still be re-analysed so its recorded `ComparabilityKeyHash` reflects the narrowed definition.
- **Runs 11–14 and runs 16–21 sit on opposite sides of the v7 → v8 reset**, and everything before run 22 sits on the far side of v8 → v9 (Readability) and v9 → v10 (unanswered questions). Three resets now separate run 11 from the present.
- **A refutation is advisory evidence, and a human reads the cited line before any change rests on it.** On run 24 Q1 the claim verifier produced **two** refutations and **both were wrong**, each with a confident citation, and both are recorded permanently in that report and flagged as `BenchmarkAnswerFlags.RefutedClaim`.
  - It refuted *"Gnolls can be played by the following six roles"*, citing `src/role.c:1228` — a line inside the **`races[]`** array, the Gnoll *race* entry's alignment mask, while `MH_GNOLL` appears in exactly **six `roles[]` entries**: Barbarian (`:141`), Caveman/Cavewoman (`:220`), Healer (`:299`), Priest/Priestess (`:540`), Rogue (`:621`), Ranger (`:713`).
  - It refuted the claimed Yeenaghu peace and wish, citing `M2_HOSTILE` on `src/monst.c:5669`. **That verdict was recorded here as standing until 2026-09-10, and it is false.** `peace_minded()` (`src/makemon.c:4451`) returns `TRUE` for Yeenaghu **and** for `PM_HYENA` against a gnoll player *unconditionally*, ahead of `always_peaceful()` and ahead of every alignment test — `M2_HOSTILE` is the default flag that check overrides. And `src/minion.c:799-830` grants a real wish through `mongrantswish()`, gated on chaotic with an alignment record ≥ 14, or a luck roll for a chaotic or neutral gnoll, or carrying the Howling Flail, with `context.yeenaghu_wishes` making repeats progressively rarer.
  - **The shared failure mode is worth more than either instance:** the verifier cites a *default flag or the wrong array element* and misses the special-case code that overrides it. Both errors are on one answer, about one race, and both survived into a published report. **No code fix applies** — the verifier is a model, its output is advisory and folded into no index, and that containment is what limited the damage — but a refutation about GnollHack-specific racial behaviour should be treated as **unverified until a human reads the overriding code path**, not as evidence.
  - **Now nine recorded instances.** Fourth (run 30, Q11 trident range, `src/apply.c:5233`), fifth (run 33, Q1 wish-repeat odds), sixth and seventh (run 35, Q16 group-size `#if 0` citation and Q7's "d20-style save" wrongly marked Supported) were followed by two more on run 36, both at claim-verifier `low`: eighth, Q7's fear-skill save table refuted against a wiki table that omits the spell's extra per-skill-level term (`src/zap.c:949`); ninth, Q14's −4 magic-cancellation penalty refuted against Master Kaen's `mcadj` data field instead of the code that applies it, `src/mcastu.c:793`. From harness 20 the claim-verification prompt's instruction 3a is the response: a claim about how a spell, attack or effect is computed is checked in the code that implements it, not only a data table or a wiki page that may simply omit the term.
- **Two controlled runs remain deferred, and both must run under v10 rather than across the boundary**: (a) one model, one suite, `verboseMode` false vs. true, to settle whether verbosity buys Completeness — cheap now that `e9b3e9a752…`/`bb19dc24…` is a known isolated pair; and (b) a tool-policy variant run, to test whether the source-family-share-versus-latency correlation is causal.
- **A critical error is a grader judgement, not a fact.** Three of the five critical errors ever published on the Claude 5 Sonnet series were spurious — run 28 Q3 (rubric units), run 31 Q18 (`src/engrave.c`), run 35 Q1 (`src/attrib.c:117`, `Races/Gnoll.md:25`) — and on run 35 the blind second opinion agreed with the false verdict. A critical error caps quality at 25, so it is the single most consequential judgement in the assessment prompt and the one most worth re-reading on disk. From harness 19 the harness itself sends the assessor's `criticalErrorQuote` to the claim verifier and flags a supported quote as `ContestedCriticalError`; that flag is **advisory and says *contested*, never *overturned*** — read it, and the cited code path, before the count. From harness 20 the same treatment reaches an out-of-rubric Accuracy deduction: its own-knowledge basis (the text after `Not in rubric:`) is sent to the verifier as a claim, and a Refuted verdict flags `ContestedAccuracyDeduction` — advisory in the same sense, no score, cap or index change, run 36 Q5 being the motivating case (`src/rnd.c:200`).

**Correction, 2026-09-10 (run-33 H1): every Gemini-role token and cost figure in this registry —
the table above and every entry below — is inflated.** Until the run-33 round `GoogleProvider`
emitted one usage report per streamed chunk and `AgentLoopRunner` summed them, so each Gemini model
call was counted roughly once per chunk (≈ 11× on run 33). That covers the primary assessor and the
synthesis on every run, and the candidate on run 22 — its tokens, its cost and possibly its 21.1 %
cache-read share. Anthropic and OpenAI figures are unaffected. Every "grading share" below overstates
the assessor, and a cost comparison across the fix is not like-for-like. The stored figures are not
rewritten; see `docs/overseer/ai-benchmark.md` § *Harness Version 18 Updates*.

### Run 38 — 2026-09-11: GPT-5.6 Sol (clean confirming run for the run-37 re-run round; harness 22)
- **Candidate**: GPT-5.6 Sol (`gpt-5.6-sol`), thinking `medium`, reasoning `standard`, parallel tool calls Enabled, max output 128000. Suite 6, 18 questions, sequential, no re-run.
- **Prompt options**: identical to runs 29–37. **Grading regime**: harness 22, scoring method 10, profile Standard Intelligence Index (1), budget 45 flat; assessor Gemini 3.7 Flash @ `high`; second opinion Claude 5 Opus @ `low`, blind, FlaggedPlusSample; claim verifier Claude 5 Opus @ `low` — roster = runs 36–37.
- **Instrument SHAs**: `CandidateSystemPromptSha256 = 715c0dcb…` (= runs 35–37); `ToolGuidesSha256 = 3bdaf81f…` (= run 37); `KnowledgeBaseHeadSha = 576ca574…`; `WikiHeadSha = e8a167c0…`; `SourceCodeHeadSha = 3861281e…`. All re-read from disk 2026-09-11 and matching; `ChatService.cs` at `d5c2b28c`. **Tier C against run 37** (HarnessVersion only), as pre-declared.
- **Quality**: II **95 ± 4** (raw 95, unweighted 95, holistic 94). Accuracy 96.8 / L 5.8; Completeness 92.6 / 5.4; Conciseness 91.9 / 5.4; Readability 96.4 / 5.7. 0 critical errors; 11 of 12 claims Supported, **1 refuted** (Q9 castings paraphrase — defensible, the first standing refutation on the Sol series). **Q7 Accuracy 3/6 (quality 64) is S9, fourth observation, and the first time it scored**: the rubric's skill table is the general one; `src/zap.c:361-364` + `:949` × 5 % (`src/mhitu.c:1517`) give the candidate's +15/−10/−35/−60/−85/−110 % exactly, and `Saving Throws.md` (wiki `e8a167c`, the run-36 T4 fix) carries the same table — **run-36 T4 verified in use**. Counterfactual II ≈ 96.5. Agreement −21.8 over 4 (Opus `low`, third run; −22.0 / −11.5 / −21.8); 3 disagreements (Q1, Q9, Q11). Out-of-scope 6 — **two of them (Q1, Q6) beside Completeness 5 with no other evidence (H2)**; FORM 5; band drift +28.9. **Q1 Accuracy 5 with "Matches rubric; … without error." unflagged — the harness-22 H4 criterion missed (H1).**
- **Speed**: median model time **38,958 ms** (runs 36–37: 58,101 / 55,524); P90 81,916; max 83,714 (Q18). TTFT median 1,888. Speed Index 87. 117 model calls (6.5/q, max 15 on Q2).
- **Cost**: $3.03 — candidate $2.30 (76 %; output $0.74), grading $0.73 (24 %; verifier $0.40 = $0.033/claim; second opinion $0.26 of which $0.17 cache write, no reads). 1,898,529 in / 36,778 out; cache read 88.2 %. 190 tool calls (10.6/q, 0 failed, 0 refused); Source 63.7 %, Wiki 31.6 %, Structured 4.7 %, KB 0. Q15 + Q2 + Q18 = 41.5 % of input; Q2 (authored Simple) spent 12 source rounds after the wiki had answered (T3, third observation of serial exploration).
- **Tool layer**: clean; 0 corpus defects; 13 results at the generic cap (8 `source_code_search`, 3 `source_code_view`, 1 `wiki_view`, 1 `nethack_wiki_view`). Found at rung zero: `source_code_view` cut mid-line with no continuation contract (T1); `get_function_definition` kind `function` misses on three macros, a round each (T2); log export labels the recorder-capped length as the result length (H3).
- **Verification Outcome — run-37 round**: H5 met (26/26 ≤ 8,697; clamp not exercised); H6 met on the run half (3/3 misses are macros); **H4 missed** (sentence-form no-fault unflagged); H7 met on the report; H1–H3 not exercised; side effects met except input tokens +0.95 %.
- **Transfer Action**: rung 3 tool contracts **T1** (`source_code_view` whole-line budget + continuation notice) and **T2** (`get_function_definition` kind fallback with note), each with one guide sentence; harness **H1** (sentence-form no-fault → `UnevidencedDeduction`), **H2** (`OUT-OF-SCOPE:`/`FORM:`-only evidence beside a sub-6 level detected, counted, routed; report and card reworded), **H3** (log label), **H5** (run-detail Model Under Test cost card, added at the user's request); **S9** rubric repair (human, suite editor) — a suite comparability break on Q7 once it lands. **`HarnessVersion` 22 → 23**; `ToolGuidesSha256` moves `3bdaf81f…` → **`aa0937559ad8f42eba28aba230084a5997c7c7c2af6f502a7eb26dfa2da8cc20`** (two guides; recomputed over the built `ToolGuides` output on 2026-09-11, the replication first validated against run 38's own `3bdaf81f…`); no `ScoringMethodVersion`, `_policy.md`, `ChatService` or knowledge-base change; `CandidateSystemPromptSha256` asserted unchanged. T3–T5 recorded.
- **Verification Outcome (for this round)**: run 39 — same candidate, same roster, harness 23; two instrument keys move (HarnessVersion, ToolGuidesSha256) → **below Tier B**, verifies countable criteria only (`benchmark_run_38_analysis_v2.md` § 6). Deferred controlled runs (Sol @ `low`; tool-policy variant) still pending.

### Run 39 — 2026-09-12: Gemini 3.7 Flash (new candidate; baseline)
- **Candidate**: Gemini 3.7 Flash (`gemini-3.7-flash`), thinking `medium`, parallel tool calls Enabled,
  max output 65,536. Suite 6, 18 questions, sequential.
- **Prompt options**: identical to runs 29–38 (`overseerMode` 0; `verboseMode` false; tools on; web
  search, subagents, spoiler-free, game, snapshot, history, wiki context all off/false; `parallelMode`
  Enabled).
- **Grading regime**: harness 23, scoring method 10, profile Standard Intelligence Index (1), budget 45
  flat. Assessor **Claude 5 Opus @ `low`**; second opinion **GPT-5.6 Sol @ `medium`**, blind,
  FlaggedPlusSample; claim verifier **GPT-5.6 Sol @ `medium`** — roster rotated so candidate, assessor
  and second reader are three providers. **Not comparable with any earlier run**: candidate, three
  roster keys, harness, `ToolGuidesSha256`, and Q1–Q4 and Q7 **re-assessed** after run 38 at unchanged
  `ItemRevision` (H5) all moved without a key to say so before this round.
- **Instrument SHAs**: `CandidateSystemPromptSha256 = 715c0dcb86820a316057bf584dbef5b16a3f74aa1de2f3f202d8442d9c276b3a`
  (= runs 35–38); `ToolGuidesSha256 = aa0937559ad8f42eba28aba230084a5997c7c7c2af6f502a7eb26dfa2da8cc20`
  (= the run-38 round's recorded prediction); `KnowledgeBaseHeadSha = 576ca5741d1bd79ef1cb2f7db575709cf0bb0db8`;
  `WikiHeadSha = e8a167c0be046ebbcc70eeb9ca37eecb7582e598`; `SourceCodeHeadSha = 3861281ec5bd39a6de5e75c1f91c5ddc4db19a42`.
  All re-read from disk 2026-09-12 and matching; `ChatService.cs` at `d5c2b28`.
- **Quality**: II **68 ± 8** (raw 71, unweighted 68, holistic 62). Accuracy 71.3 / L 4.0; Completeness
  62.4 / 3.5; Conciseness 88.2 / 5.1; Readability 84.4 / 4.8. **2 critical errors applied, both
  spurious** — Q1 lycanthropy resistance is a Gnoll intrinsic (`src/attrib.c:117`; fourth Q1
  observation) and Q18's erosion-on-attack and hypocrite penalty are GnollHack code (`src/uhitm.c:602`,
  `src/engrave.c:304-323`, `src/mon.c:4819-4838`; run 31 proved it) — the verifier supported both quotes
  and the blind second reader agreed with neither; instrument-corrected II ≈ 71–72. Q3 docked again on
  the run-28 units defect (S2, still open). 56 claims: 45 supported, 9 refuted, 2 indeterminate; 5
  refutations sound, 2 pedantic, 2 not read on disk. Agreement −4.7 signed over 9; 6 disagreements; 3
  critical-error splits (Q1, Q11, Q18). Out-of-scope 1; **FORM 17 of 18 under Opus @ `low`** (H4); band
  drift +26.3 (run 38: +28.9 — five items re-assessed, see H5).
- **Speed**: median model time **8,284 ms** (Sol 39–58 s; Sonnet 15.7 s); P90 50,844; max 64,476 (Q16,
  16 rounds). TTFT median 1,688. Speed Index 98, saturated. 96 model calls, 1.0 calls per round (17 of
  18 questions strictly serial).
- **Cost**: **$4.65** — candidate **$0.84** (18 %), grading $3.82 (verifier $2.19 = $0.04/claim, 47 %;
  assessor $0.97 of which **$0.60 cache write with 0 reads**, H1; second opinion $0.46; synthesis
  $0.21). 2,074,465 in / 10,809 out; cache read **54.3 %** (first Gemini measurement since the
  usage-count fix; implicit cache only). 81 tool calls (4.5/q, 0 failed, 0 refused); Source 60.5 %,
  Wiki 30.9 %, Structured 6.2 %, KB 2.5 %.
- **Tool layer**: clean; 0 corpus defects; 5 ordinary misses, each recovered next round; 5 results at
  the generic cap. Found at rung zero: **`wiki_search` has no stemming** (T1 — `Object Materials.md`
  unreachable by the query `material`; 271 spell pages with a "Material components" heading outrank it)
  and no hit-count line (T1b).
- **Verification Outcome — run-38 round**: T1 whole-line budget in use (Q10 resumes at 47, 127); T2 not
  exercised; H1 met (Q12); H2 met (17 counted); H3 met; H5 met. The pre-declared same-candidate
  confirming run did not happen — run 39 is a different candidate under a rotated roster.
- **Transfer Action**: rung 3 **T1** `EnglishAnalyzer` (Porter stemming) in `WikiService` and
  `NetHackWikiService` plus a guide sentence, **T1b** `[Showing N of M matching articles …]` hit-count
  line; rung 2 **T2** handoff written (three wiki pages, four edits: Morgoth exceptions, melee-range
  penalty vs. prohibition, blessed-missile breakage, sacrifice-prayer timeout). Harness: **H1** grading
  prompts carry the shared preamble as `SegmentedPrompt.FrozenPrefix` and single-shot grading requests
  emit no conversation-tail breakpoint (`AgentRunRequest.CacheConversationTail`); **H2** Critical Errors
  line rewritten to state the applied count before any split, and Contested-Verdict Sensitivity resolves
  splits in both directions; **H5** `Item rev N` on every question
  header, Comparability block gains item-revision and assessed-difficulty lines, Fundamental
  `SuiteAssessedDifficulties` key registered — **`HarnessVersion` 23 → 24**; `ToolGuidesSha256` moves to
  `62195cdf03e7d8239a9889b2c049e8bfcf6d0b59751e08fb1ff83948789a0773` (`wiki_search.md`; replication
  validated against run 39's `aa093755…` first); no `ScoringMethodVersion`, `_policy.md`, `ChatService` or knowledge-base change;
  `CandidateSystemPromptSha256` asserted unchanged. **S1–S3** rubric handoff written
  (`rubric_handoff_v5.md`; live text = seed at `ItemRevision` 1; Q1 lycanthropy, Q3 units, Q18 erosion)
  — mirrored into `Overseer/Data/DefaultSuites/gnollhack_player_assistance.json`; **the human paste, the
  re-assessment, and the resulting new `ItemRevision` / `AssessedDifficulty` values for Q1/Q3/Q18 are
  pending human steps and not yet reported.** § G: default suites move from one hardcoded file to a
  discovered catalog (`Overseer/Data/DefaultSuites/*.json`), an accessible import dialog, no auto-seed,
  and runs record their suite origin (`DefaultSuiteKeyUsed`). T3–T6, H3, H4 recorded (no code change).
- **Verification Outcome (for this round)**: run 40 — same candidate (Gemini 3.7 Flash @ `medium`);
  assessor and second opinion unchanged; **claim verifier changed — configuration not yet reported**;
  harness 24. `HarnessVersion`, `ToolGuidesSha256` and `ClaimVerifierConfiguration` all move (three
  instrument keys) → **below Tier B**, verifies countable criteria only
  (`benchmark_run_39_analysis_v5.md` § 6). Deferred controlled runs (Sol @ `low`; tool-policy variant)
  still pending.

### Run 40 — 2026-09-12: Gemini 3.7 Flash (confirming run for the run-39 round; harness 24)
- **Candidate**: Gemini 3.7 Flash (`gemini-3.7-flash`), thinking `medium`, parallel tool calls
  Enabled, max output 65,536. Suite 6, 18 questions, sequential.
- **Prompt options**: identical to runs 29–39. **Grading regime**: harness 24, scoring method 10,
  profile Standard Intelligence Index (1), budget 45 flat. Assessor Claude 5 Opus @ `low`; second
  opinion GPT-5.6 Sol @ `medium`, blind, FlaggedPlusSample; claim verifier **GPT-5.6 Luna @ `max`**
  (changed from Sol @ `medium` — an `Instrument` key). **Below Tier B against run 39**: three
  instrument keys plus `SuiteItemRevisions` (Q1, Q3, Q18 r2) and `SuiteAssessedDifficulties`.
- **Instrument SHAs**: `CandidateSystemPromptSha256 = 715c0dcb86820a316057bf584dbef5b16a3f74aa1de2f3f202d8442d9c276b3a`
  (= runs 35–39); `ToolGuidesSha256 = 62195cdf03e7d8239a9889b2c049e8bfcf6d0b59751e08fb1ff83948789a0773`
  (= the run-39 round's recorded prediction; replicated over the built output, 34 files);
  `KnowledgeBaseHeadSha = 576ca5741d1bd79ef1cb2f7db575709cf0bb0db8`;
  `WikiHeadSha = 90f6f6a5dfef77f868a98362ce11ac7a7c2188e5` (run 39 + the T2 handoff commit);
  `SourceCodeHeadSha = 3861281ec5bd39a6de5e75c1f91c5ddc4db19a42`. All re-read from disk 2026-09-12
  and matching; `ChatService.cs` at `d5c2b28`.
- **Quality**: II **68 ± 8** (raw 69, unweighted 68, holistic 68). Accuracy 67.7 / L 3.8;
  Completeness 62.3 / 3.4; Conciseness 88.9 / 5.2; Readability 84.1 / 4.8. **1 critical error
  applied (Q16), spurious** — `adj_lev()` is GnollHack's own integer formula (`src/makemon.c:3976-4014`,
  cap 127) and the rubric's `cntdiv` point is `#if 0` dead code (`:88-158`); verifier supported the
  quote, blind second reader 54 / no CE. **Seven rubric defects** (S1–S7: Q3, Q5, Q6, Q7, Q15, Q16,
  Q18) charged correct, tool-grounded facts; instrument-corrected II ≈ 74 (estimate). 47 claims:
  45 supported, 2 refuted (Q4 Astral — wiki wording; Q11 runewords — sound), 1 indeterminate.
  Agreement −3.3 signed over 6; 2 disagreements, 2 critical-error splits (Q7 second reader CE on a
  wiki-documented mechanic; Q16). Out-of-scope 1; **FORM 15, 14 beside Readability < 6 under Opus @
  `low`** (second run); **unverifiability-grounded Accuracy deductions 9, detector 1** (H1).
- **Speed**: median model time **7,092 ms** (run 39: 8,284); P90 25,248; max 26,463 (Q18, 13
  rounds); Q2 26,567 ms over 17 rounds. TTFT median 1,321. Speed Index 100, saturated. 98 model
  calls, 1.1 per round (T2).
- **Cost**: **$2.19** — candidate $0.87 (40 %; uncached in $0.75), grading **$1.32** (assessor
  $0.54 with 59,585 cache read / 3,505 creation — run-39 H1 verified; second opinion $0.29; verifier
  $0.15 = $0.003/claim, 7 %, but 8m 47s critical path; synthesis $0.35, 16 %). 2,081,226 in / 9,981
  out; cache read **51.9 %**, zero on every first call and on 5 whole questions (T3). 86 tool calls
  (4.8/q, 0 failed, 0 refused); Source 58.1 %, Wiki 33.7 %, Structured 5.8 %, KB 2.3 %. Q2 + Q18 +
  Q16 = 54.1 % of input.
- **Tool layer**: clean; 0 corpus defects; 5 ordinary misses (4 `source_code_search` near-miss
  payloads, 1 bare `search_definitions` — H7), all recovered; 6 results at the generic cap. Found at
  rung zero: `search_definitions` miss carries no probe (H7); the log export cuts every result to
  its first 600 characters, so end markers are invisible (H6).
- **Verification Outcome — run-39 round**: T1 **verified** (Q10 `material` → `Object Materials.md`
  first; side effects met); T1b not observable in the export; H1 **met**; H2/H5 **met** on the
  report; S1–S3 **met** (no Q1/Q18 CE; no Q3 units deduction); T2 partly exercised (Q17 in use).
- **Transfer Action**: rung 3 **T1** one sentence in `wiki_search.md` (stop when the article
  answers); harness **H1** detector vocabulary, **H2** prompt sentence + mandatory `Not in rubric:`
  marker, **H3** supported claims to the synthesis, **H4** report line **and the advisory
  *Verification-cleared Accuracy Sensitivity* index**, **H5** claim-count fix, **H6** log tail,
  **H7** `search_definitions` miss probe + guide sentence — **`HarnessVersion` 24 → 25**;
  `ToolGuidesSha256` moves `62195cdf…` → **`b3920ee678c8a9832ad5d5c170b091179fec6e263b93751251bdbff34d652e60`**
  (`wiki_search.md`, `search_definitions.md`; recomputed over the built `ToolGuides` output on
  2026-09-12, 34 files, the replication first validated against run 40's own `62195cdf…`); no
  `ScoringMethodVersion`, `_policy.md`, `ChatService` or knowledge-base change;
  `CandidateSystemPromptSha256` asserted unchanged. **S1–S7** rubric handoff
  (`rubric_handoff_v3.md`) — the seven blocks **mirrored into
  `Overseer/Data/DefaultSuites/gnollhack_player_assistance.json`** in this round; the human paste,
  the re-assessment and the resulting new `ItemRevision` / `AssessedDifficulty` values are pending
  human steps and not yet reported. **W1–W2** wiki handoff (`wiki_handoff_prompt_v3.md`). T2, T4,
  T6, H8–H10 recorded. **T3 closed — decided against** (§ 3, § 7 rung 5): chat and benchmark run
  under the same provider settings, so a benchmark-only Gemini explicit cache is not available, and
  a chat-wide one costs more in token-hour storage than the discounted reads save at this traffic.
- **Verification Outcome (for this round)**: run 41 **or the next same-candidate run** — same
  candidate, same roster, harness 25, after the rubric paste; below Tier B; countable criteria in
  `benchmark_run_40_analysis_v3.md` § 6. Deferred: T5 assessor calibration (re-assess a Sol run
  under Opus @ `low`, or run `gemini-3.8-flash` @ `high`); band-drift prompt repair (since run 29);
  controlled runs (Sol @ `low`; tool-policy variant) still pending.
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
- [`server_wiki_handoff`](../server_wiki_handoff/SKILL.md) — pre-flight checklist and template for rung-2 wiki handoff prompts
