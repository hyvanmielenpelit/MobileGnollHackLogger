# GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Report for AI Researchers and Developers

*INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.*

- **Date:** 2026-09-28
- **Suite:** GnollHack Core Suite
- **Questions:** 4
- **Runs:** 1 (run 12)
- **Peers:** none; this is a stand-alone report

## Abstract

GPT-5.6 Luna scored 80 / 100 on 4 questions, with one critical error on Q3.

## Key figures

- **Intelligence:** 80 / 100 (interval 77–83).
- **Speed:** median answer time 12.3 s.
- **Cost:** $0.036 per question.
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
- **Comparability:** this report describes the model on its own, measured under the instrument condition with signature `sig-7f3a91`.
- **Pricing basis:** Catalog prices on 2026-09-20 (price card dated 2026-09-01)
- **Versions:** harness 41, scoring method 12.

## Results

### Dimensions

| Dimension | GPT-5.6 Luna |
|---|---|
| Accuracy | 84 |
| Completeness | 70 |
| Conciseness | 88 |
| Readability | 90 |

### Difficulty bands

| Difficulty band | Questions | GPT-5.6 Luna |
|---|---|---|
| Simple | 1 | 90 |
| Intermediate | 2 | 49 |
| Advanced | 1 | 87 |

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

Completeness was its lowest dimension at 70.

### Weaknesses

- Asserted a false outcome on Q3. *(Both graders)*
  - *Evidence:* Both graders — critical error · Q3 (25 / 100) · the claim verifier refuted an answer sentence on Q3
- Scored lowest on intermediate questions (49). *(From per-question results)*
  - *Evidence:* Intermediate band score: 49 · Q3 (25 / 100), Q2 (72 / 100) · the claim verifier refuted an answer sentence on Q3

## What worked well

Short, accurate answers on simple questions (R2).

### Strengths

- Answers simple questions precisely and briefly. *(One grader — different provider)*
  - *Evidence:* One grader — different provider — accuracy · Q1 (90 / 100)

## Recommendations for model developers

- Asserting a destruction rule without checking it cost Q3; verify object-destruction rules before stating them. *(Both graders)*
  - *Evidence:* Both graders — critical error · Q3 (25 / 100) · the claim verifier refuted an answer sentence on Q3

## Per-question results

| Q | Topic | Band | Score | Critical error | Refuted answer sentences | Tool calls | Model time |
|---|---|---|---|---|---|---|---|
| Q1 | Throwing gems at unicorns | Simple | 90 | no | 0 | 2 | 8.1 s |
| Q2 | Prayer timeout | Intermediate | 72 | no | 0 | 3 | 11.0 s |
| Q3 | Breaking a thrown gem | Intermediate | 25 | yes | 1 | 5 | 15.2 s |
| Q4 | Wand of wishing charges | Advanced | 87 | no | 0 | 4 | 13.4 s |

### Questions scoring below 50 or with a critical error

**Q3** (Breaking a thrown gem): Claimed a thrown gem always shatters; the rubric says it can survive.

### Question details

#### Q1: Throwing gems at unicorns

**Question:**

> What happens if I throw a gem at a co-aligned unicorn?

**Rubric:**

> - The unicorn catches the gem.
> - A valuable gem raises Luck; worthless glass does not.

**Answer:**

> The unicorn catches it. A real gem of your alignment raises your Luck; glass does nothing.

**Graders:**

- **Panel member A (Gemini 3.8 Flash):** score 92. Accurate and brief.
  - Accuracy: Matches rubric.
- **Panel member B (Claude Haiku 5):** score 88. Correct.

**Claim verifier:**

- **Grader's statement — refuted (the verifier sided with the answer):** "Worthless glass angers the unicorn." — Glass is caught and returned without anger (dothrow.c).

#### Q2: Prayer timeout

**Question:**

> How long is the prayer timeout after a successful prayer?

**Rubric:**

> - The timeout is reset to a random value around 350.

**Answer:**

> About 50 to 1000 turns, typically near 350.

**Graders:**

- **Panel member A (Gemini 3.8 Flash):** score 75. Terse.
  - Completeness: Omits the Luck adjustment.
- **Panel member B (Claude Haiku 5):** score 69.

**Claim verifier:**

- **Answer sentence — supported:** "The timeout is typically near 350." — Matches the prayer code (pray.c).

#### Q3: Breaking a thrown gem

**Question:**

> Will my gem break if I throw it at a unicorn?

**Rubric:**

*Rubric not recorded for this answer.*

**Answer:**

> Yes. A thrown gem always shatters on impact, so never throw your valuable gems at a unicorn; keep them for selling or for wishing instead.
>
> If you must throw something, throw worthless glass.

**Graders:**

- **Panel member A (Gemini 3.8 Flash):** score 25. Critical error.
  - Accuracy: The gem does not always shatter.
  - Critical error: "A thrown gem always shatters on impact"
- **Panel member B (Claude Haiku 5):** score 25. Fabricated breakage.

**Claim verifier:**

- **Answer sentence accused by a grader — refuted (the verifier sided with the grader):** "A thrown gem always shatters on impact." — Gems are caught, not broken (dothrow.c).

#### Q4: Wand of wishing charges

**Question:**

> How many wishes can I get from a wand of wishing?

**Rubric:**

> - 1 to 3 charges.
> - Wresting gives one more.

**Answer:**

> A new wand has 1 to 3 charges, and you can wrest one more.

**Graders:**

- **Panel member A (Gemini 3.8 Flash):** score 88. Good.
- **Panel member B (Claude Haiku 5):** score 86.

**Claim verifier:**

- No claims were checked.

## Tool-use behavior

- **Tool calls per question:** 3.5
- **Source code share:** 57 %
- **Wiki share:** 29 %
- **Structured lookup share:** 14 %
- **Knowledge base share:** 0 %
- **Other tools share:** 0 %
- **Failed tool calls:** 0
- **Calls refused by the tool budget:** 0

*Recorded success or failure describes whether a tool call executed. It does not show that the query was well chosen, that the result was relevant, or that the corpus was current.*

*Per-call arguments and results are in each run's Tool-call log until the retention sweep prunes them.*

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
| R1 | weakness · critical error | Q3 | Both graders |
| R2 | strength · accuracy | Q1 | One grader — different provider |
| R3 | strength (A) vs weakness (B) · conciseness | Q2 | Graders disagree |

- **R1:** Panel member A: States that a thrown gem always shatters. · Panel member B: Claims the gem is always destroyed.
- **R2:** Panel member A: Precise on the unicorn throwing rules.
- **R3:** Panel member A: Admirably brief. · Panel member B: Too terse to be useful.

## Threats to validity

- The benchmark asks single-turn questions under one chat configuration. It does not exercise conversation history, pre-injected wiki context, spoiler-free mode, web search or subagents.
- Interval: Item sampling only. Below 3 runs there is no reproducibility estimate, so this interval covers one source of variation rather than two.
- The graders are AI models. Each grader's provider relation to the model under test is stated under Setup and method; a grader from the model's own provider may read it more favorably.

The result rests on a single run, so its interval covers question sampling alone.

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

## Evaluation terms

- **Purpose statement:** Internal evaluation of candidate AI models for the Overseer assistant within GnollHack.
- **Distillation / training prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.
- **Third-party model content:** Outputs generated by **GPT-5.6 Luna** (OpenAI), graded by models from Google, Anthropic and DeepSeek, and described in this document by **Claude Opus 5.5** (Anthropic) are third-party content evaluated solely for domain-specific benchmark scoring and operational model selection.

---

*Document ID 101 · format version 8 · created 2026-09-28 10:42 UTC · writer Claude Opus 5.5 (Anthropic, claude-opus-5-5) · disclosure Full · peers named*

*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and checked automatically for structure, permitted figures and references, word limits, disclosure of benchmark text, peer names, significance claims, interval-overlap wording, hype words and spelling; the checks do not verify the prose's interpretations.*
