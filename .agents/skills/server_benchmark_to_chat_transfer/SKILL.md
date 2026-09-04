---
name: server_benchmark_to_chat_transfer
description: >-
  Mandatory method for turning Overseer AI benchmark findings into improvements to the
  production chat agent — better answer quality, lower latency, lower cost. Covers the fact
  that the benchmark grades the production chat system prompt, the required triage of every
  finding into harness defect / suite defect / chat-transferable, the evidence bar a finding
  must clear before any chat prompt is changed, the configuration-parity check, the ordered
  ladder of safe changes from knowledge-base article up to prompt edit, the anti-overfitting
  rules, and the per-run model behaviour notes this skill accumulates. Read before analysing
  any benchmark run report, diagnostics or assessment, and before writing any implementation
  plan derived from one.
---

# Benchmark to Chat Transfer: Turning Benchmark Findings into Chat Improvements

This skill defines the mandatory protocol for translating empirical findings from the Overseer AI Intelligence Benchmark into concrete improvements to the production chat agent — higher answer quality, reduced latency, and lower token costs — without overfitting the assistant to the benchmark suite.

---

## 1. The Structural Fact

In `BenchmarkService.cs:262`, the candidate model's system prompt is generated directly via:
```csharp
var systemPrompt = await _chatService.BuildSystemPrompt(
    userId: null,
    gameplayHelpMode: true,
    hasGameSnapshot: suiteHasBoard,
    gameplayRole: null,
    verboseMode: promptOptions.VerboseMode,
    spoilerFree: false,
    webSearchEnabled: false,
    subAgentsEnabled: false,
    allowSourceReferences: true,
    preferredLanguage: null,
    cancellationToken: cancellationToken);
```

**The benchmark grades the verbatim production chat system prompt**, not an artificial or simplified test prompt. Every quality score, completeness deduction, hallucination finding, tool routing observation, and latency measurement in a benchmark report is an empirical measurement of what a real human player receives when asking assistance from the Overseer.

Transferring findings back to chat is therefore not an analogy or an extrapolation; it is the direct analysis of production prompt performance under test conditions.

---

## 2. The Mandatory Triage

Every finding produced by a benchmark analysis must be triaged into exactly one of three categories:

1. **Harness Defect**: The testing instrument itself is broken or flawed (e.g. grading biases, parser failures, broken reporting formulas, unhandled timeouts, missing backfills).
2. **Suite Defect**: The benchmark question, rubric ground truth, or difficulty rating is incorrect, ambiguous, or outdated.
3. **Chat-Transferable**: The defect or observation reflects authentic behavior that real users experience in chat (e.g. tool routing inefficiencies, prompt-induced brevity vs. completeness conflicts, knowledge gaps, latency inflation).

An analysis that fails to classify its findings into this taxonomy is incomplete.

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
  - T3: Source code tools accounted for 71% of tool calls despite prompt rule *"Prefer wiki tools over source code tools"*, heavily driving turn latency ($r = 0.53$).
  - T4: Knowledge base was called only once in 18 questions (`get_knowledge_article` under-use).
  - T5: Refuted claims on Q9 (spell-skill saving throws) and Q16 (simultaneous attacker cap) revealed specific game mechanics misconceptions.
  - T6: Systematic framework required to guide chat prompt updates.

---

## 3. Configuration Parity — Check Before Attributing Anything

Before attributing any score, weakness, or behavior to a model's underlying intelligence or reasoning capabilities, **always inspect the Chat Prompt Under Test block in the report manifest**:

- What was `verboseMode`? (Concise `false` vs. Detailed `true`)
- Was game context present? (`hasGameSnapshot`)
- Was spoiler-free mode enabled?
- Were subagents or web search enabled?

**The Golden Rule of Attribution**: A dimension may be depressed because the prompt instructed the model to answer that way. In run 11, Completeness (83.0) lagged Accuracy (97.7) by 14.7 points because the model obeyed the concise instruction *"Default to 2–5 sentences per response"*. Blaming the model for low completeness without checking `verboseMode` is an attribution error.

---

## 4. The Three Transfer Axes

Benchmark reports provide quantitative diagnostic data across three distinct performance axes:

### Axis 1: Quality
- **Metrics to Read**: Dimensional averages (Accuracy 55%, Completeness 25%, Conciseness 10%, Readability 10%), BARS levels (0–6), evidence strings (`accuracyEvidence`, `completenessEvidence`), critical errors, refuted claims, and disputed verdicts.
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

---

## 5. The Evidence Bar

> 🛑 **A single benchmark run MOTIVATES a chat change; it NEVER JUSTIFIES one.**

The production chat prompt is the scientific instrument. Editing the prompt in response to a single benchmark run destroys comparability for future runs and risks overfitting to idiosyncratic grader or candidate quirks.

### The Minimum Bar for Touching `ChatService.BuildSystemPrompt`:
1. The finding has reproduced across at least **two independent benchmark runs**, OR
2. A controlled pair of runs was executed where a single variable was isolated (e.g. identical model run under `verboseMode: false` vs. `verboseMode: true`).

Below this threshold, findings are logged in the **Model Behaviour Notes** (Section 9) and the prompt remains untouched.

---

## 6. The Ladder of Safe Changes

When an empirical chat-transferable finding clears the evidence bar, resolve it using the **lowest possible rung** on the ladder of safe changes:

1. **Knowledge Base Article (Lowest Cost, Safest)**:
   - When a model hallucinates a mechanic or has a claim refuted by the verifier (e.g. Q9 spell-skill mechanics, Q16 attacker cap).
   - In `ChatService.cs:1102`, the prompt explicitly instructs the agent that knowledge base articles take precedence over the wiki.
   - *Requirement*: Human authorship only; model outputs must never be ingested automatically as authoritative knowledge.
2. **Wiki Content Update**:
   - For factual omissions or ambiguities that belong in public NetHack/GnollHack documentation rather than specialized Overseer tips.
3. **Tool Descriptions and Tool Policy Text (`_toolRegistry.GetPolicyText()`)**:
   - For tool routing inefficiencies (e.g. urging wiki use before source code search for general mechanics). Changing tool descriptions guides the model without altering core persona prompt sections.
4. **Limits Parity**:
   - Aligning session and iteration budgets between chat and benchmark bands per `docs/overseer/ai-benchmark.md` § 9.
5. **Model or Thinking Level Selection**:
   - Adjusting default models or reasoning effort in `/settings` rather than hacking prompt prose.
6. **Chat System Prompt Modification (Highest Risk, Last Resort)**:
   - Modifying `ChatService.BuildSystemPrompt` prose directly. Reserved exclusively for systemic, cross-model deficiencies backed by multiple runs.

---

## 7. Anti-Overfitting Rules

To preserve Overseer chat quality for real human players, agents and developers are **strictly prohibited** from:

1. **Never copy benchmark rubric points into the chat system prompt**:
   - Rubrics exist to score specific questions. Baking rubric answers into the system prompt is Goodhart's Law; it inflates benchmark scores while bloating the prompt for real users.
2. **Never force verbose mode on production chat to inflate Completeness**:
   - A player asking a question while playing needs a crisp 2–5 sentence answer, not a 1,000-word encyclopedic dump. Terse chat defaults are an intentional product decision.
3. **Never tune prompt instructions to optimize the Speed Index**:
   - Speed Index measures adherence to difficulty-scaled latency targets. Chat responsiveness is optimized through streaming and prompt caching, not by suppressing necessary model reasoning.
4. **Never weaken anti-fabrication, uncertainty, or spoiler-free constraints**:
   - Under no circumstances should an agent loosen uncertainty warnings or anti-hallucination guardrails to score points on questions where the model lacked confidence.

---

## 8. Required Output

Any formal analysis of an AI benchmark run report or diagnostics **MUST** include a dedicated **Chat Transfer** section containing:
- Table of triaged chat-transferable findings.
- Evidence from the report (dimensions, tool counts, Pearson $r$, or citations).
- The proposed ladder rung (1 to 6).
- Evidence bar assessment (Single run / Motivated vs. Multi-run / Justified).

Any implementation plan derived from a benchmark run must replicate this section or explicitly state: *"No chat-transferable changes proposed in this plan."*

---

## 9. Model Behaviour Notes (Accumulated Knowledge)

*This section is an accumulating registry. Every benchmark analysis appends its run findings below.*

### Run 11 — 2026-09-04: GPT-5.6 Luna
- **Configuration**: `gameplayHelpMode: true`, `verboseMode: false`, thinking level `max`, 18 questions (Default Suite).
- **Quality**: Accuracy 97.7 / Level 5.8; Completeness 83.0 / Level 4.8; Conciseness 95.0; Readability 95.0. Intelligence Index: 91.5 $\pm$ 5.9 (95% CI). Response-style conflict confirmed (14.7 pt gap). Refuted claims on Q9 (`src/zap.c:359-364`, spell skill) and Q16 (`src/makemon.c:110-129`, simultaneous attackers).
- **Speed**: Mean model time 68.3s (max 179.9s on Q18). Speed score 56.6. High correlation between source tool share and model latency ($r = 0.53$).
- **Cost**: 293 total tool calls. Source Code: 208 calls (71.0%), Wiki: 77 calls (26.3%), Structured Lookup: 7 calls (2.4%), Knowledge Base: 1 call (0.3%). Zero-KB answers: 17 of 18 questions. Token ratio: 44:1 input:output with 90% cache-read share.
- **Transfer Action**: Seeded Knowledge Base Gap worklist with Q9 and Q16. Promoted Response Style control (T2) to allow testing `verboseMode: true` in Run 12.

---

## 10. Cross-References

- [ai-benchmark.md](file:///c:/hmp/MobileGnollHackLogger/docs/overseer/ai-benchmark.md) (§ 9 Limits Parity & § 9.1 What the Benchmark Tells the Chat)
- [server_implementation_planning](file:///c:/hmp/MobileGnollHackLogger/.agents/skills/server_implementation_planning/SKILL.md)
- [overseer_chat_message_handling](file:///c:/hmp/MobileGnollHackLogger/.agents/skills/overseer_chat_message_handling/SKILL.md)
- [overseer_chat_response_timing](file:///c:/hmp/MobileGnollHackLogger/.agents/skills/overseer_chat_response_timing/SKILL.md)
- [tool_execution_architecture](file:///c:/hmp/MobileGnollHackLogger/.agents/skills/tool_execution_architecture/SKILL.md)
