# GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Report for AI Researchers and Developers

*Confidential. Prepared for the model's provider. Contains benchmark questions — do not publish.*

- **Date:** 2026-09-28
- **Suite:** GnollHack Core Suite
- **Questions:** 4
- **Runs:** 1 (run 12)
- **Compared with:** Model A = Grok 5 (xAI, grok-5); Model B = Mistral Large 4 (Mistral, mistral-large-4)
- **Pricing basis:** Catalog prices on 2026-09-20 (price card dated 2026-09-01)

## Abstract

GPT-5.6 Luna scored 80 / 100 on 4 questions, ranking joint 1st of 3 (intervals overlap) against Grok 5 and Mistral Large 4.

## Key figures

- **Intelligence:** 80 / 100 (interval 77–83), joint 1st of 3 (intervals overlap); its 95 % interval overlaps every peer's.
- **Speed:** median answer time 12.3 s, 2nd of 2.
- **Cost:** $0.036 per question, 2nd of 3.
- **Critical errors:** 1 of 4 answers.

## Setup and method

- **Suite:** GnollHack Core Suite, 4 questions.
- **Chat configuration under test:** Gameplay Help, concise (verboseMode: false), tools: enabled, web search: disabled, subagents: disabled, source code references: allowed
- **Model under test:** GPT-5.6 Luna (OpenAI, gpt-5.6-luna), thinking level high; 1 run.
- **Grading:** each answer is graded on accuracy, completeness, conciseness and readability, weighted Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %. Each dimension is graded on behaviorally anchored levels scored 1, 15, 35, 55, 72, 87, 100. A critical error caps the answer's quality at 25.
- **Graders:**
  - Panel member A: Gemini 3.8 Flash (Google, gemini-3.8-flash), different provider from the model under test
  - Panel member B: Claude Haiku 5 (Anthropic, claude-haiku-5), different provider from the model under test
  - Reference reader: DeepSeek V4 (DeepSeek, deepseek-v4), different provider from the model under test
  - Claim verifier: Gemini 3.8 Flash (Google, gemini-3.8-flash), different provider from the model under test
- **Formulas:** answer quality is the weighted geometric mean of the four dimension scores, capped by a critical error; in a panel run it is the mean of both graders' scores. The Intelligence Index is the difficulty-weighted mean of answer quality. Median answer time is the median model time per answer, with tool time excluded. Cost per question is the model under test's spend divided by the questions asked.
- **Comparability:** every model in this report was measured under one instrument condition, signature `sig-7f3a91`.
- **Pricing basis:** Catalog prices on 2026-09-20 (price card dated 2026-09-01)
- **Versions:** harness 41, scoring method 12.

### Compared models

| Model | Provider | Letter | Kind | Runs | Thinking level | Harness version | Run dates (UTC) |
|---|---|---|---|---|---|---|---|
| **GPT-5.6 Luna** | OpenAI | — | run | 1 | high | 41 | 2026-09-20 |
| Grok 5 | xAI | A | run | 1 | high | 41 | 2026-09-19 |
| Mistral Large 4 | Mistral | B | run | 1 | not set | 41 | 2026-09-12 |

## Results against peers

### Quality

| Model | Provider | Intelligence Index | 95 % interval | Rank | Paired difference |
|---|---|---|---|---|---|
| **GPT-5.6 Luna** | OpenAI | 80 | 77–83 | joint 1 | — |
| Grok 5 | xAI | 85 | 81–89 | joint 1 | -4.6 points (-9.8 to +0.7) |
| Mistral Large 4 | Mistral | 78 | 74–83 | joint 1 | not available |

*Paired difference: mean per-question difference, subject minus peer, over the questions both answered; 95 % paired-bootstrap interval. It reflects question sampling only, is not adjusted for comparing several models, and is not a significance test.*

No paired difference:

- Mistral Large 4: Fewer than five questions were scored for both this model and the subject on the same item revision, too few for a paired difference.

*GPT-5.6 Luna: its 95 % interval overlaps every peer's. This describes where the intervals overlap; it is not a significance test.*

No pair of models is tested for significance: with 3 models, testing every pair would flag chance differences.

### Speed

| Model | Provider | Median answer time | Rank |
|---|---|---|---|
| **GPT-5.6 Luna** | OpenAI | 12.3 s | 2 |
| Grok 5 | xAI | 9.8 s | 1 |
| Mistral Large 4 | Mistral | not available | — |

Not ranked on speed:

- Mistral Large 4: Degraded: speed was measured with parallel execution disabled.

### Cost

| Model | Provider | Cost per question | Rank |
|---|---|---|---|
| **GPT-5.6 Luna** | OpenAI | $0.036 | 2 |
| Grok 5 | xAI | $0.052 | 3 |
| Mistral Large 4 | Mistral | $0.021 | 1 |

### Dimensions

| Dimension | GPT-5.6 Luna | Peer mean | Difference |
|---|---|---|---|
| Accuracy | 84 | 82 | +2 |
| Completeness | 70 | 78 | -8 |
| Conciseness | 88 | 85 | +3 |
| Readability | 90 | 89 | +1 |

### Difficulty bands (assessed)

| Difficulty band | Questions | Authored questions | GPT-5.6 Luna | Peer mean | Difference |
|---|---|---|---|---|---|
| Simple | 1 | — | 90 | 85 | +5 |
| Intermediate | 2 | — | 49 | 69 | -21 |
| Advanced | 1 | — | 87 | 91 | -4 |

### Judge-dependent pairs

Judge-dependent pairs: not available. The compared runs were not all graded by the same panel.

## Speed and cost

| Measure | GPT-5.6 Luna |
|---|---|
| Median answer time | 12.3 s |
| 90th-percentile answer time | 15.0 s |
| Cost per question | $0.036 |
| Cost per run | $0.144 |
| Input tokens per question | 18,250 |
| Output tokens per question | 1,140 |

## Why it scored this way

One critical error on Q3 capped that answer at 25.

Completeness was 70 against a peer mean of 78.

### Weaknesses

- Asserted a false outcome on Q3, where Grok 5 scored well. *(Both graders)*
  - *Evidence:* Both graders — critical error · Q3 (25 / 100) · the claim verifier refuted an answer sentence on Q3
- Scored lowest on intermediate questions (49). *(From per-question results)*
  - *Evidence:* Intermediate band score: 49 · Q3 (25 / 100), Q2 (72 / 100) · the claim verifier refuted an answer sentence on Q3

## What worked well

Short, accurate answers on simple questions (R2).

### Strengths

- Answers simple questions precisely and briefly. *(One grader — different provider)*
  - *Evidence:* One grader — different provider — accuracy · Q1 (90 / 100)

## Recommendations for model developers

- Verify object-destruction rules before asserting them. *(Both graders)*
  - *Evidence:* Both graders — critical error · Q3 (25 / 100) · the claim verifier refuted an answer sentence on Q3

## Per-question results

| Q | Topic | Assessed band | Authored | Score | Peer mean | Difference | Critical error | Refuted answer sentences | Tool calls | Model time |
|---|---|---|---|---|---|---|---|---|---|---|
| Q1 | Throwing gems at unicorns | Simple | — | 90 | 85 | +5 | no | 0 | 2 | 8.1 s |
| Q2 | Prayer timeout | Intermediate | — | 72 | 70 | +2 | no | 0 | 3 | 11.0 s |
| Q3 | Breaking a thrown gem | Intermediate | — | 25 | 68 | -43 | yes | 1 | 5 | 15.2 s |
| Q4 | Wand of wishing charges | Advanced | — | 87 | 91 | -4 | no | 0 | 4 | 13.4 s |

### Questions more than 15 points below the peer mean, or with a critical error

**Q3** (Breaking a thrown gem): Claimed a thrown gem always shatters; the rubric says it can survive.

## Tool-use behavior

- **Tool calls per question:** 3.5 (peer mean 2.8)
- **Source code share:** 57 %
- **Wiki share:** 29 %
- **Structured lookup share:** 14 %
- **Knowledge base share:** 0 %
- **Other tools share:** 0 %
- **Failed tool calls:** 0
- **Calls refused by the tool budget:** 0

*Recorded success or failure describes whether a tool call executed. It does not show that the query was well chosen, that the result was relevant, or that the corpus was current.*

## Grader reliability

- **Panel mean absolute difference:** 6.5 points
- **Intraclass correlation, ICC(A,1):** 0.82
- **Panel disagreements:** 1 of 4 answers
- **Panel member A alone:** 81 / 100
- **Panel member B alone:** 79 / 100
- **Reference reader (advisory, third provider):** 90 / 100
- **Reference reader's mean offset from the panel:** +16.8 points. It never scores; its neutrality between the two panel families is an assumption.
- **Response-style conflict:** none

| Row | Finding | Questions | Support |
|---|---|---|---|
| R1 | weakness · critical error: States that a thrown gem always shatters. | Q3 | Both graders |
| R2 | strength · accuracy: Precise on the unicorn throwing rules. | Q1 | One grader — different provider |
| R3 | strength (A) vs weakness (B) · conciseness: A: Admirably brief. B: Too terse to be useful. | Q2 | Graders disagree |

## Threats to validity

- The benchmark asks single-turn questions under one chat configuration. It does not exercise conversation history, pre-injected wiki context, spoiler-free mode, web search or subagents.
- Interval: Item sampling only. Below 3 runs there is no reproducibility estimate, so this interval covers one source of variation rather than two.
- The graders are AI models. Each grader's provider relation to the model under test is stated under Setup and method; a grader from the model's own provider may read it more favorably.

Mistral Large 4 is degraded on speed, and every model rests on a single run, so no interval covers run-to-run variation.

## Reproducibility appendix

- **Run IDs:** 12
- **Run dates:** 2026-09-20
- **Model under test:** OpenAI gpt-5.6-luna, thinking level high
- **Grader models:** Panel member A: Google gemini-3.8-flash, thinking level medium; Panel member B: Anthropic claude-haiku-5; Reference reader: DeepSeek deepseek-v4; Claim verifier: Google gemini-3.8-flash, thinking level medium
- **Harness version:** 41
- **Scoring method version:** 12
- **Comparability signature:** `sig-7f3a91`
- **System prompt SHA-256 prefix:** `e9b3e9a7c4d1`
- **Tool guides SHA-256 prefix:** `f59d8b30a1c7`
- **Pricing basis:** Catalog prices on 2026-09-20 (price card dated 2026-09-01)

## Questions and answers

### Q1: Throwing gems at unicorns

**Question:**

> What happens if I throw a gem at a co-aligned unicorn?

**Answer excerpt:**

> The unicorn catches it. A real gem of your alignment raises your Luck; glass does nothing.

### Q2: Prayer timeout

**Question:**

> How long is the prayer timeout after a successful prayer?

**Answer excerpt:**

> About 50 to 1000 turns, typically near 350.

### Q3: Breaking a thrown gem

**Question:**

> Will my gem break if I throw it at a unicorn?

**Answer excerpt:**

> Yes. A thrown gem always shatters on impact, so never throw your valuable gems at a unicorn; keep them for…

### Q4: Wand of wishing charges

**Question:**

> How many wishes can I get from a wand of wishing?

**Answer excerpt:**

> A new wand has 1 to 3 charges, and you can wrest one more.

## Removed content

Automatic validation removed these items from the writer's output before it was stored:

- `weaknesses[2]` (rule 4)

## Evaluation terms

- **Purpose statement:** Internal evaluation of candidate AI models for the Overseer assistant within GnollHack.
- **Distillation / training prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.
- **Third-party model content:** Outputs generated by **GPT-5.6 Luna** (OpenAI), graded by models from Google, Anthropic and DeepSeek, and described in this document by **Claude Opus 5.5** (Anthropic) are third-party content evaluated solely for domain-specific benchmark scoring and operational model selection.

---

*Document ID 101 · format version 11 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Detailed · peers named*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically for structure, permitted figures and references, word limits, disclosure of benchmark text, peer names, significance claims, interval-overlap wording, hype words and spelling; the checks do not verify the prose's interpretations.*
