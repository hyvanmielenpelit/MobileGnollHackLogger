---
name: server_benchmark_runbook
description: >-
  Mandatory closing deliverable of every Overseer AI benchmark analysis: the Developer Runbook
  (developer_runbook_v<N>.md) that tells the developer exactly what to do next and in which
  order. Read once a benchmark analysis has triaged its findings and before its implementation
  plan is written; read whenever a user asks "what do I do next", "which runs should I make",
  "in what order do I apply these fixes", or "which model should Overseer use". Covers the
  single numbered step list in the order things are done, each step marked EXPORT, AGENT,
  CHANGE, CHECK, RUN or SAVE, with every run a step specified field by field as the launcher
  shows it; the dependency order of a round's fixes (plan, GnollHack plan, wiki handoff,
  knowledge base, restart, rubric import and re-assessment, roster or profile) and placing a
  change a run must not see after that run; the rule that the
  agent implements the whole plan uninterrupted and the developer's manual jobs come at the
  very end; the special-case mid-plan pause with its proof that the solution compiles and its
  explicit "not finished" message; exports at the start and imports at the end; the purposes a
  run may serve in their binding order of importance (first improving the main Overseer chat,
  then the benchmarking system, then deciding which models perform best as the Overseer AI on
  intelligence, speed and cost, with validation of the round's fixes taking the rank of what it
  validates); predicting the comparability tier of a run before it is made; model-selection run
  design; cost and wall-time estimates; the go / stop line and the hand-back after each run;
  and the self-check before handoff.
---

# Developer Runbook: One Numbered Step List, Runs Included

> *Naming: the Second Reader (single-assessor runs) and Reference Reader (panel runs) were called
> the "second opinion" before 2026-09-27. Database columns, API fields and code identifiers still
> use `SecondOpinion*`, as do reports exported before that date and the history sections below.*
>
> A run card or report written before that date reads *Second Opinion* and *Second Opinion Mode*
> where the launcher now shows *Second Reader* or *Reference Reader* and *Coverage*; the settings
> are the same.

## 1. Purpose and When It Binds

A benchmark analysis round produces several documents, and each one that needs a human says so
in its own place: the plan waits for approval, the rubric repair waits for an import, the wiki
handoff waits for two chat sessions, a `gnollhack_` plan waits for another developer. **Nothing
else tells the developer in which order to do all of it, or how to make the next run.** This
skill defines the document that does: the **Developer Runbook**.

**The runbook serves the benchmark's purposes in their order of importance**
(`server_benchmark_to_chat_transfer` § *What the Benchmark Is For*): first the main AI chat of the
Overseer — that the system and its tools work correctly, that the models perform at maximum
efficiency, that there are no bugs — then the benchmarking system itself, then the ranking of
models. Both uses, debugging the chat and choosing its models, aim at the best possible assistant
for regular users. § 4 turns that order into which runs are proposed, in which sequence, and
which are Required.

It binds on **every** benchmark analysis, including one that proposes no fix (§ 2). It is **not**
one of the five skills `.agents/AGENTS.md` § *AI Benchmark Findings* requires before the first
finding: nothing in it can be applied until the findings are triaged. Read it then, **before the
implementation plan is written**, because the plan and the runbook divide one round's work
between them (§ 3).

Three rules hold throughout:

- **The agent's work comes first and runs uninterrupted; the developer's manual jobs come at the
  very end.** The plan implements every change, the agent builds and tests, and only then do
  the developer's `CHANGE` steps begin. The exceptions are narrow and are in § 3.

- **The agent never launches a benchmark run, imports a rubric, or changes a model
  configuration.** Runs spend real money on three providers and are the developer's decision.
  The agent's job is to make that decision and those clicks trivial.
- **The runbook is written for a reader who has opened no other document of the round.** No
  "as described in the analysis", no "see the handoff". A step that needs the content of another
  document repeats it.

## 2. The Deliverable

`developer_runbook_v<N>.md`, in the round's task directory, a member of its document set and
versioned with it (`server_implementation_planning` § *One task directory per analysis*). It
carries the `Skills consulted:` line like every document of the round.

```text
# Developer Runbook: Run <R> Round

Skills consulted: ...
Analysed run: <R> (<suite name>, <candidate as the report names it>), <date>
This round in three lines: what was found, what changes, what the next run is for.

## At a Glance                    <- one row per step: Step | Kind | What | Needs | Mark | Time / cost
## Steps                          <- Step 1 .. Step N, in the order they are done; no action lives outside it
## Throughout: Do Not Change      <- the freeze list (section 4), also repeated inside every CHECK step
```

**Everything the developer does is one step of one list, in the order it is done.** The runs are
steps of that list, not a part of their own: the position of a step *is* its dependency order, so
a change that must wait for a run sits after that run, and no prose such as *"between R1 and R2"*
is ever needed to say so.

### Step kinds

Every step title names its kind, in capitals, from this fixed vocabulary:

| Kind | Meaning | Examples |
|---|---|---|
| `EXPORT` | Take something out of Overseer for the agent | **Download All as YAML**; a database query result |
| `AGENT` | The agent works; you wait for its *finished* or *paused* message | Approve the plan and wait; continue after a pause; the analysis of a run |
| `CHANGE` | You change the system or its data | Restart Overseer; import a rubric repair; **Assess question difficulty**; the wiki validation and execution sessions; knowledge-base push; roster or profile change; a `gnollhack_` plan and a new board |
| `CHECK` | A checklist that must pass before the next step | The pre-launch check before every `RUN` step (§ 3) |
| `RUN` | Launch one benchmark run or battery | R1, R2 … (§ 5) |
| `SAVE` | Download a run's files, read its criteria, go on or hand back | After every `RUN` step (§ 6) |

### Numbering and naming

- **Inside the runbook**, steps are `Step 1 … Step N`, titled
  `### Step 7 — RUN R1: <title> — [V][B] — Required`.
- **Everywhere else** — the plan, `task.md`, the walkthrough, every chat message — a step is
  written **`runbook Step 7`**, because a plan has Proposed Changes steps of its own.
- **A `RUN` step keeps its run name** `R1, R2, …`: reports, analyses, § 11 of
  `server_benchmark_to_chat_transfer` and the next analysis prompt refer to runs by it.
- **Another document's own steps are named by what they are**, with that document's number in
  parentheses once: *"the validation prompt (Step 2 of the handoff document)"*. A runbook step
  title never reads *"Step 5 — CHANGE: Step 2"*.
- Step numbers and run names are identical in the plan, the runbook, `task.md`, the walkthrough
  and every chat message. **Two names for one step is a defect.**

### At a Glance

The table at the top has **one row per step**, in the same order and with the same numbers as
`## Steps`: `Step | Kind | What | Needs | Mark | Time / cost`. `Mark` is filled on `RUN` rows
only (Required, Recommended or Optional). A worked example, for a round shaped like one whose
rubric repair is the isolated variable between two runs:

| Step | Kind | What | Needs | Mark |
|---|---|---|---|---|
| 1 | EXPORT | Run report of the analysed run (done) | — | |
| 2 | AGENT | Approve the plan; wait for *finished* | 1 | |
| 3 | CHANGE | Restart Overseer | 2 | |
| 4 | CHECK | Pre-launch check for R1; Step 7 must not be done yet | 3 | |
| 5 | RUN | R1: the suite before the rubric import | 4 | Required |
| 6 | SAVE | R1's files; go / stop | 5 | |
| 7 | CHANGE | Import the rubric repair (only after Step 6) | 6 | |
| 8 | CHANGE | Assess question difficulty; re-mark reviewed | 7 | |
| 9 | CHECK | Pre-launch check for R2 | 8 | |
| 10 | RUN | R2: the battery on the repaired rubrics | 9 | Required |
| 11 | SAVE | R2's files; the analysis prompt | 10 | |

**The chat message that delivers the round repeats the *At a Glance* table in short form** —
one line per step, `RUN` steps with their mark and estimated cost — after the links to the
documents. The developer should be able to start from the chat message alone.

**A round with nothing to fix still owes a runbook.** Its list has no `CHANGE` step and opens
with one line — *"No fix to apply; nothing to restart or import."* — followed by the `CHECK`,
`RUN` and `SAVE` steps of the next run on purposes C, B or M, in that order of preference (§ 4).

**After the plan is executed**, `walkthrough.md` carries a **Runbook status** section: every
step of the list marked *done* or *remaining*, and every value that only became known during
execution — the new `HarnessVersion`, the `ToolGuidesSha256` the next run should show, a
migration name. The completion chat message lists the remaining steps again. Write a new
`_v<N>` of the set only when a step's *content* had to change.

> **Legacy format (before 2026-10-03).** A runbook written before this date has Part A (`S`
> exports, `P` pauses, `A` jobs, ending in the pre-launch check), Part B (run cards `R1…`),
> Part C (after each run) and Part D (the freeze list). An analysis that opens such a runbook
> reads `S` as `EXPORT`, `P` as an `AGENT` pause with its developer steps, `A` as `CHANGE` or
> `CHECK`, a run card as `RUN` and Part C as `SAVE`. The format changed because a rubric import
> listed in Part A was done before the run it was meant to follow.

## 3. Ordering the Steps

### Who works when

**The agent first, without interruption; the developer last.** The implementation plan applies
every change of the round, the agent builds and tests, and the developer's manual jobs begin
only when the agent has said it is finished. A developer who has to build and start Overseer in
the middle of a plan is building a half-changed tree, and a developer who is handed a job between
two stages cannot tell whether the agent is done. The list therefore has a default shape, and in
most rounds it has no `EXPORT` step and no pause:

| Position | Steps | What goes here |
|---|---|---|
| **First** | `EXPORT` | An **export** the agent needs as input — the suite's **Download All as YAML**, a database query result. Ask for it while the analysis is still running, so the plan is written on it and never waits for it. Usually none |
| **Then** | one `AGENT` | *Approve the plan. The agent then works through all of it without asking you for anything, and tells you when it has finished.* Name what "finished" looks like: the completion message, `walkthrough.md`, the build and test results |
| **Inside it, special cases only** | a pause (below) | Usually none: the agent does not need you until it has finished |
| **Last** | `CHANGE`, `CHECK`, `RUN`, `SAVE` | Everything manual, in the order of the table that follows, with each run at its place. **Imports** into Overseer belong here |

### A pause is a special case

A pause is allowed only when a later **agent** step needs something that only the running
application can produce or take in, and it cannot be moved to the start or the end. An export or
import of Overseer data **between** stages is the same thing and is accepted only in a **very big
plan**. `server_implementation_planning` § *The plan runs uninterrupted; the developer's jobs come
last* owns the rules; what the runbook and the pause message owe the developer is:

- **The solution compiles at that point, and the agent has proven it** — `dotnet build
  MobileGnollHackLogger.slnx` with 0 errors, plus `npm run build` from `Overseer/ClientApp/` when
  the client changed, and any new migration already applied — *before* the developer is asked to
  press Start. The step states the command and its result. Visual Studio builds whatever is on
  disk; a pause on a tree that does not compile costs the developer an error list they did not
  cause.
- **Three consecutive entries in the list.** An `AGENT` step (*the agent stops after stage k and
  says so*), the developer's `EXPORT` / `CHANGE` steps, then an `AGENT` step (*the agent
  continues with stages k+1…*). The plan's `## Mid-Plan Pauses` names the pause by those steps,
  as *"runbook Steps 4–6"*. Each developer step is as clear as any other (format below).
- **The words "I have not finished".** The pause message opens by saying that this is a pause,
  which stage it follows, which stages remain, what the agent needs back, and that it **will
  continue working as soon as the developer replies**. Each developer step inside the pause ends
  with it: *"When this is done, tell the agent. The agent still has work to do — do not commit,
  do not launch a run."*

The wiki confirmation gate of `server_wiki_handoff` § 4a is a pause only when an agent step waits
behind it. When what waits is the developer's own restart or run, the wiki sessions are ordinary
`CHANGE` steps.

### The order of the developer's jobs

Leave out what the round does not have; never reorder without saying why in the step.

**A `CHANGE` step that a `RUN` step must not see is placed after that run's `SAVE` step**, and
its title says so: *"Step 7 — CHANGE: Import the rubric repair (only after Step 6, R1 saved)"*.
That run's `CHECK` and `RUN` steps name it under `Must not be done yet:`. A dependency is
expressed by position and `Needs`, never by prose such as *"between R1 and R2"*. The table below
orders the jobs that come before the first run, and every one of them can move behind a run this
way.

| Order | Job | Why it sits here |
|---|---|---|
| 1 | A `gnollhack_` plan, in a session on the GnollHack clone; then export the new snapshot and attach it to the suite if the round says so | A new board is a **Fundamental** key (`GameSnapshot`): it must land before any run that is meant to use it, and never between the halves of a pair |
| 2 | Wiki handoff: the validation session (Step 2 of the handoff document, a chat on the GnollHack clone), then the execution session (its Step 3, a chat on the GnollHackWiki clone) only on `VALIDATION: PASS`; commit and push the wiki | `server_wiki_handoff` § 4a owns the wording. Before the start of Overseer, so one start covers it |
| 3 | Knowledge-base article push | Reloads on a 10-minute HEAD poll; it changes the frozen prompt segment, so it moves `KnowledgeBaseHeadSha` and `CandidateSystemPromptSha256` |
| 4 | **In Visual Studio: stop Overseer if it is running, then start it** | Starting builds the finished tree. `Overseer/ToolGuides` and the NetHack wiki are read **only at startup**; the GnollHack wiki, the source index and the knowledge base re-check their git HEAD every 10 minutes, and a fresh start settles all of them at once. The host serves the Angular client from `Overseer/wwwroot` as last built by `npm run build`, which Visual Studio does not run, so a round that changed the client has the agent build it first. Any migration was already applied by the agent |
| 5 | Rubric repair: **Import Questions from YAML**, validate, review, confirm, **Assess question difficulty**, re-mark reviewed | An import, so it is an end job; after the start, so the import and the difficulty assessment run on the round's code. The launcher refuses the suite until every question has an assessed difficulty. When the import is the isolated variable of a pair, it is placed after the first run's `SAVE` step, by the rule above |
| 6 | Grader roster, scoring-profile or System AI Config changes the round recommends | Last, and only between series: a roster or thinking-level change is an **Instrument** key move and must never fall between a motivating run and its confirming run |
| 7 | A `CHECK` step before every `RUN` step (below) | The cheapest place to catch a skipped job |

Step numbers and run names follow § 2 *Numbering and naming*.

### The step card

One step is **one action in one place with one expected result**. If a step needs "and then",
it is two steps.

```text
### Step <n> — <KIND>: <imperative title>
- Who: You | A new chat opened on <absolute clone path>
- Needs: Step <k>, Step <m> finished      (or: nothing)
- Must not be done yet: Step <j> (<what>)  (CHECK and RUN steps only, when a later change must not have happened)
- Where: <exact UI path, label by label>  |  <absolute directory and shell>
- Do: <numbered clicks with the labels exactly as the UI prints them, or the exact command
       in a fenced block, or the absolute path of the file to upload or the prompt to paste>
- Expect: <what appears when it worked - a message, a badge, a count, a line of output>
- If not: <the one thing to do - usually: stop, paste what you see into this chat>
- Takes: <rough time; and cost, when the step spends money, e.g. Assess question difficulty>
```

Rules for writing a step:

- **`Needs` names step numbers** (*"Step 6 finished"*), never a description of the order.
- **Absolute paths** for every file, in a form that can be pasted into a file dialog.
- **UI labels verbatim, in bold**, in click order: Admin → **AI Benchmark** → **Manage Suites** →
  … . **Grep each label in the Angular template before writing it** (§ 8 names the files);
  a label recalled from an older round is how a runbook sends someone to a button that moved.
- **Commands in their own fenced block**, one per block, PowerShell unless stated.
- **Repeat, do not link.** The rubric import steps (`server_rubric_handoff` § 6) and the wiki
  sequence and gate message (`server_wiki_handoff` § 4a) are written out inside the steps. Those
  skills still own the wording; the step copies it. A file to upload or a prompt to paste is
  named by absolute path.
- **Every `Expect` is observable.** "The import succeeded" is not; *"the review step lists exactly
  Q3 and Q7, both as replace, none as create"* is.
- **Say what waits.** A step another step depends on says so in `Needs` on the dependent step, and
  a confirmation gate is a step of its own.
- **Overseer is restarted from Visual Studio**: stop it, then start it; starting builds the tree.
  The step says exactly that, and its `Expect` names something visible — the Admin page loads,
  and the next run's manifest shows the round's `HarnessVersion` or a changed `ToolGuidesSha256`.
- **No personal path goes into a skill or the repository.** Downloads go to *"your run-artifacts
  folder"*; the step proposes a subfolder name and nothing above it.
- **A developer step inside a pause ends with**: *"When this is done, tell the agent. The agent
  still has work to do — do not commit, do not launch a run."*

### The `CHECK` step (its own step immediately before every `RUN` step)

A short checklist the developer ticks immediately before the run that follows. It is **written
out in full each time**, never as *"as Step 4"*:

- [ ] The agent has said it is **finished** — not paused — and the walkthrough exists.
- [ ] Overseer was stopped and started in Visual Studio **after** the agent finished and after the last change to the NetHack wiki.
- [ ] At least 10 minutes have passed since the last wiki, source or knowledge-base push — or the start came after it.
- [ ] Admin → **AI Benchmark** → **Run Benchmark**: selecting the suite shows **no** `Difficulty n/m Assessed` warning.
- [ ] The suite card shows no `Needs review` badge the round did not expect.
- [ ] No run is in progress.
- [ ] Where it applies: Step <n> (<the change this run must not see>) has **NOT** been done.
- [ ] Nothing on the *Throughout: Do Not Change* list has been edited: <the list, written out>.
- [ ] The models, thinking levels and scoring profile match the `RUN` step that follows exactly —
      including the settings that are **not** launcher fields (§ 5).

## 4. Deciding Which Runs to Propose

### The purposes, in order of importance

Every proposed run serves at least one purpose, and its `RUN` step says which. **The table is in
priority order, and the order is binding** — it is the benchmark's own
(`server_benchmark_to_chat_transfer` § *What the Benchmark Is For*).

| Priority | Tag | Purpose | Typical shape |
|---|---|---|---|
| **1 — most important** | **C** | **Improve the main Overseer chat**: the system and its tools work correctly, the models perform at maximum efficiency, there are no bugs or other problems, and quality improves in every respect | A run that re-exercises a repaired tool, corpus, prompt section or request path; an isolated-variable pair for a chat-transferable finding that has not met the evidence bar (`server_benchmark_to_chat_transfer` § 6); a run read for latency, token volume and cache-read share after an efficiency change |
| **2** | **B** | **Improve the benchmarking system**, so that it works rigorously and correctly and its results serve both the chat and a scientific comparison of models | A run chosen to exercise a harness, grader or suite question: double grading to measure grader agreement, another suite to test a detector off the questions that motivated it, a replicate set to measure reproducibility |
| **3** | **M** | **Decide which models perform best as the Overseer AI** on intelligence, speed and cost | The same exam under the same graders, with only the candidate changed (below) |
| — | **V** | Validate this round's fixes. Not a priority of its own: a **V** run inherits the priority of the fixes it validates, so its `RUN` step always carries **C** or **B** beside **V** | The motivating run's configuration, unchanged, after the round's `CHANGE` steps |

What the order decides:

- **Which runs are proposed, and which are Required.** A run is **Required** when a chat fix
  (**C**) or a harness or suite fix (**B**) is unverified without it. An **M** run is never
  Required by a round whose findings were about the chat or the instrument; it is Recommended or
  Optional, and the `RUN` step says what decision it would inform.
- **The sequence of the runs.** The list is in the order of doing, so a run that another step
  depends on comes first whatever its tag. Among runs that do not depend on each other, **C**
  comes first, then **B**, then **M**. The developer can stop after any `SAVE` step and will have
  a coherent result, with the money spent on the most important question first.
- **No model ranking on a system known to be broken.** An **M** run or set is proposed only when
  the round leaves **no open defect in the chat system, its tools or the harness that would
  affect the candidates** — and if one is open, the runbook says so and defers the **M** runs to
  the round that closes it. A ranking taken on a defect measures the defect, and it usually
  does not measure it equally across providers.
- **What a `RUN` step's criteria look at first.** On a **C** run the first criteria are about the
  system, not the score: every tool call succeeded or failed for a documented reason, the
  repaired behaviour appears, no delivery check failed, latency and token volume did not regress.
  The Intelligence Index comes after them.
- **Spend.** When the developer's budget allows fewer runs than the runbook lists, the priority
  order is the order in which they are cut, from the bottom.

**One run can be enough, and often is**: a single confirming run is usually tagged **V C B** —
the same report verifies the chat and harness fixes and re-measures the production prompt — and
it also adds one data point to the model record at no extra cost, which the `RUN` step may note
without tagging the run **M**. Propose a further run only when it answers a question the first
cannot. Mark every run **Required**, **Recommended** or **Optional**, and order the runs so that
stopping after any `SAVE` step leaves a coherent result.

**Skipping a run.** A run marked Recommended or Optional may be skipped; its `CHECK`, `RUN` and
`SAVE` steps are skipped together, and a `CHANGE` step whose `Needs` names a skipped run says in
that step what to do then. The *At a Glance* `Mark` column shows the mark on the `RUN` row.

### Rules for a validating run (V)

- **Same configuration as the motivating run** — suite, scoring profile, model under test,
  assessor, co-assessor (or none), second reader (or reference reader) and its coverage, claim verifier, `Concise`
  response style. The `RUN` step
  lists each value and marks it *same as run <R>*.
- **Predict the comparability tier before the run is made**, from what the `AGENT` and `CHANGE`
  steps before the run move, using the key categories in `BenchmarkComparabilityKey.cs`:

  | Moved before the run | Key category | Pair lands in |
  |---|---|---|
  | Nothing the harness fingerprints | — | Tier A (Replicate) |
  | Only question parallelism, speed calibration or pricing | SpeedAndCost | Tier B |
  | **Exactly one** of: `HarnessVersion`, `ToolGuidesSha256`, `CandidateSystemPromptSha256`, `KnowledgeBaseHeadSha`, `ScoringMethodVersion`, scoring profile, assessor / second-reader / claim-verifier configuration, per-question budgets | Instrument | Tier C (compare, never pool) |
  | Two or more of those | Instrument | **NotComparable** |
  | A rubric import (item revisions), a difficulty re-assessment, a new board, another suite | Fundamental | **NotComparable** |
  | Model, thinking level, service tier, parallel mode, response style | Candidate | **NotComparable** |

  A co-assessor is part of `AssessorConfiguration`, not a key of its own: adding one, removing one
  or changing either panel member moves that one key. But a panel run forces its reference reader
  to `All` and blind, so against a single-assessor run whose second reader was not already the same
  reader in `All` mode, blind, `SecondOpinionConfiguration` moves too, and the pair is
  **NotComparable**.

  **Say the predicted tier and name the keys in the `RUN` step.** When the prediction is
  NotComparable — the usual case after a rubric import — say what that means in practice: score
  deltas against run `<R>` prove nothing, and the criteria are therefore written as **absolute
  observations** (a count of tool misses, a flag that must not appear, a rubric point no longer
  charged), not as index movements.
- **Offer the isolated pair only when attribution is worth a second run's cost**: R1 after the
  code change and *before* the rubric import, R2 after it; the import is a `CHANGE` step placed
  after R1's `SAVE` step. Say the cost of the extra run and what
  it buys. Never let a roster change fall between the two.
- **Criteria come from the analysis, verbatim** — the pre-declared acceptance criterion and
  rollback trigger of every rung-3-or-higher change (`server_benchmark_to_chat_transfer` § 9) —
  each with *where in the report it is read*. At least one criterion must be read on questions
  **not** implicated in the finding (§ 8 rule 5 there).
- **Use `Number of Runs` = 3 only when the expected movement is smaller than the run's own
  confidence interval**, and say so; a replicate set triples the cost.

### Rules for model-selection runs (M)

The grader effect recorded in `server_benchmark_to_chat_transfer` § 11 is larger than the
difference between candidates, so a ranking is sound only when **everything but the candidate is
identical**:

- One suite at one set of item revisions and assessed difficulties, one board, one scoring
  profile, one assessor or one panel, one second-reader configuration, one claim verifier, `Concise` style —
  and **no `CHANGE` step between the first and the last run of the set**. Make the set *after*
  the round's fixes, never straddling them.
- **Take candidates from what is configured, never from memory**: the System AI Configs with the
  Benchmark role, the `RecommendedModels` entries in `Overseer/appsettings.json`, and the
  `supported_ai_models` policy. Always include the currently recommended model of the provider
  being challenged, as the baseline. The `RUN` step names models as the `Model Under Test`
  dropdown prints them.
- **Choose an assessor from a provider none of the candidates shares** where the roster allows.
  Where it does not, the `RUN` step warns that the `Same-Provider Assessment Warning` dialog will
  appear for that run and that `Acknowledge & Start Run` is the intended answer. **When the set
  spans the providers the scoring roles could come from, grade it with a panel instead** (below):
  a panel run never shows that dialog.
- **State the decision rule before the runs are made**, on the three axes:
  - *Intelligence* — Intelligence Index with its interval, critical errors, refuted claims. A
    candidate inside the baseline's interval is a tie, not a loss or a win. From scoring method 13
    (harness 45) also compare, from report § 2 or the Critical Errors key-figure card:
    - the **confirmed critical-error rate** with its 95 % Wilson interval — confirmed only, not
      unresolved or overturned splits; on a small suite the interval is wide, so overlapping
      intervals are a tie here too;
    - **correct when attempted** — correct ÷ (correct + partial + incorrect);
    - **wrong instead of abstaining** — incorrect ÷ (incorrect + not attempted), lower is better: it
      shows a model that guesses where the production prompt tells it to say it does not know.

    State in advance which of these can veto a candidate the index favors. A method-13 set is never
    compared with a method-12 run.
  - *Speed* — **median model time and TTFT P50 / P90**, never the Speed Index, which saturates
    and is comparable only within one thinking level. Name the ceiling a mid-game player
    tolerates, in milliseconds, from the analysis.
  - *Cost* — **candidate cost per question** from the Model Under Test cost card, never the run
    total, which is mostly grading. Note any price that is promotional or dated.
- **Read the result in Admin → AI Benchmark → Model Comparison → `Open comparison wizard`.** Open
  **About** in the wizard's view bar: a model listed under *Not in the charts* was not comparable,
  and the Interactive table shows it with State *Excluded*. The `RUN` step says in advance that this
  must not happen for the set.
- A model change is ladder **rung 6** and still has to clear the evidence bar: one run per
  candidate motivates a recommendation; a second comparable run, or a replicate set, justifies
  changing `RecommendedModels`.

### Panel roster (two-family assessor panel)

A run with a **Co-Assessor** is a panel run: the assessor (member A) and the co-assessor (member
B) both grade every answer, and the published score is their mean. The rules and their reasons
are in `docs/overseer/ai-benchmark.md` § 3 *The Two-Family Assessor Panel*; a `RUN` step that proposes
a panel follows them:

- **Members from two providers, neither under test.** The launcher refuses a panel whose members
  share a provider, and one where the model under test (provider and model id) is either member.
  Take an older, or otherwise non-candidate, model of each family — read from the configured
  System AI Configs, never from memory.
- **Fixed across every run the set compares.** The panel is part of `AssessorConfiguration`, and
  the judge-family diagnostics in Model Comparison refuse runs graded by different panels. A
  roster change between the runs of a set, or between the halves of a verification pair, spends
  the set.
- **Reference reader and claim verifier from a third family that scores nothing** — neither a
  candidate nor a member. They are the neutral anchors the diagnostics compare the panel with, so
  a `RUN` step that cannot meet this says which assumption is weakened. The launcher shows an advisory,
  not a refusal, when either shares a provider with the candidate or a member.
- **Which families may score is a roster decision the analysis records, not a code rule.** Take
  it from the analysed report's *Panel Disclosure* and the round's roster, and name any change in
  the `RUN` step.
- **Two panel runs, one per candidate family, under one panel** give the whole judge × candidate
  matrix; propose them as a pair when the question is family bias, and tag them **B**.
- **In a panel run the grader overrides are refused** (retry, re-assess, re-run synthesis and
  single-answer re-run with another configuration), so a `RUN` step never tells the developer to retry
  with a substitute grader; `Retry Failed Assessments` without an override is fine.
- **Cost**: two grader calls per answer and a second synthesis. Scale the estimate from a panel
  run's own figures when one exists, and say so when it does not.

### Cost and time

Every `RUN` step carries an **estimated cost and wall time**, taken from the analysed run's own
figures (total cost, grading share, duration) and scaled by question count and `Number of Runs`.
Say what the estimate is based on. Tell the developer to compare it with the launcher's
**Series Projection** (`Projected wall time`, `Projected cost`, `Remaining daily headroom`) and
to stop if the projection is more than about twice the estimate.

## 5. The `RUN` Step

```text
### Step <n> — RUN R<k>: <short title> — [V][C][B][M] — Required | Recommended | Optional
- Answers: <the one question this run settles, in a sentence>
- Needs: Step <n-1> (the CHECK before it)
- Must not be done yet: Step <j> (<the change this run must not see>)   (or: nothing)
- Where: Admin -> AI Benchmark -> Run Benchmark -> "New Benchmark Run"

| Launcher field | Set to | Versus run <R> |
|---|---|---|
| Model Under Test | <entry as the dropdown prints it, with its thinking-level badge> | same / CHANGED |
| Run Target | Single suite / Battery | same |
| Benchmark Suite | <name as listed> (<n> questions) — Single suite only | same |
| Battery | <name as listed> (<K> suites), revision <r>: <suite 1>, <suite 2>, … in run order — Battery only | same / CHANGED |
| Scoring Profile | <name> | same |
| Response Style | Concise — production default | same |
| Source Code References | Allowed / Disallowed — production default | same |
| Assessor | ... | same |
| Co-Assessor (optional) | <entry as the dropdown prints it> or "None - single assessor" | same / CHANGED |
| Second Reader (optional) | ... or "None — no second reader" | same |
| Reference Reader (optional, panel run) | ... or "None — no reference reader" | same |
| Coverage | <option label>; in a panel run the fixed line "Every answer, blind — a third reading. Fixed in a panel run." | same |
| Claim Verifier (optional) | ... or "None - no claim verification" | same |
| Report Writer (optional) | <entry as the dropdown prints it> or "None — no AI-written reports" | same / CHANGED |
| Number of Runs | 1 — Single suite only | |
| Runs per Suite | 1 — Battery only | |
| Wait when the run cap blocks the next run | checked / unchecked — Battery, or Number of Runs ≥ 2 | |
| Reuse earlier runs | checked / unchecked — Battery only; name the runs the projection should list | |

- Not in the launcher, and must also match: thinking level, service tier, reasoning mode and
  parallel mode (from the System AI Config behind each dropdown entry: check the badges);
  Blind Second Reader (forced on in a panel run) and max parallel questions (from the scoring profile: Admin ->
  AI Benchmark -> Scoring Profiles). Do not edit either between the runs of this round.
- Predicted comparability with run <R>: <tier>, because <keys> move. <What that means for
  reading the result.>
- In the first minute, check: the run's manifest shows <HarnessVersion n>, a ToolGuidesSha256
  that <differs from / equals> <first 8 characters>, item revisions <Qx rN>. If it shows
  "Harness delivery check failed", stop and paste the message here.
- Estimate: about <cost>, about <duration>, based on run <R> (<its cost>, <its duration>).
- Pass / fail, read from the report:

| # | Criterion (pre-declared) | Where to read it | Pass | If it fails |
|---|---|---|---|---|

- If questions fail on a provider error: Re-run Failed Questions once, then hand back as is.
```

Rules for writing a `RUN` step:

- **Every launcher field is listed, even the unchanged ones.** The `RUN` step is a form to copy,
  not a diff. Field labels and option labels are verbatim (§ 8).
- **The `Detailed` response style is never proposed for a V or M run** — its own label says it is
  not comparable with previous runs. It appears only as the isolated variable of a C pair.
- **`Coverage` does nothing without a second reader**; a `RUN` step that sets one sets the other.
- **The reader field's label follows the Co-Assessor.** The launcher shows one field, labeled
  `Second Reader` in a single-assessor run and `Reference Reader` in a panel run; a `RUN` step
  lists the row that matches its run, not both.
- **A `RUN` step that sets a `Co-Assessor` follows § 4 *Panel roster***, says the run is a panel
  run, and expects no Coverage selector, only the fixed line "Every answer, blind — a third
  reading. Fixed in a panel run.": the server runs the reference reader over every answer, blind.
  A `RUN` step for a single-assessor run still lists the Co-Assessor field, as "None - single
  assessor".
- **Choosing the grader models and effort** follows `docs/overseer/ai-benchmark.md` § 3 *Choosing
  grader models and effort*; a `RUN` step names the configured entries as the dropdowns print them.
- **`Report Writer` is not a comparability key.** It writes the run's two AI-written reports once
  the run is scored and grades nothing, so a `RUN` step may set or change it without moving the
  predicted tier. The launcher refuses the model under test and any writer from its provider; the recommended
  writer is in the same § 3 table.
- **A battery `RUN` step** sets `Run Target` to *Battery* and fills the `Battery` row with the battery's
  name, its revision (the editor's subtitle and the progress dialog's heading show it) and its suites
  in run order, so the developer can tell a revised battery from the one the step meant; it lists
  `Benchmark Suite` and `Number of Runs` as not applicable. `Runs per Suite` is the field
  `Number of Runs` becomes, bounded by `floor(maxMembersPerBattery / K)`; one round already gives a
  result, and a reproducibility figure needs three. A battery that plans more launches than the daily
  cap needs *Wait when the run cap blocks the next run*. Battery mechanics:
  `docs/overseer/ai-benchmark-multi-suite.md`.
- **`Reuse earlier runs` is per start and never remembered.** A `RUN` step that checks it names the earlier
  runs the projection should report reusing (*Reusing n earlier runs (#ids); launching m.*) and says
  what to do when it reports fewer: reuse needs all five instrument hashes unchanged since those runs,
  so a moved wiki, source or knowledge-base clone disqualifies them. A `RUN` step that wants fresh runs
  leaves it unchecked.
- **A battery `RUN` step's estimate is summed over its suites**: for each suite, its analysed or recent
  run's cost and duration, summed and multiplied by `Runs per Suite`, less the runs reused. Compare it
  with the launcher's **Battery Projection**, which sums each suite's recent mean run duration and
  cost the same way, and use the same stop rule as for the Series Projection (§ 4 *Cost and time*).
- **Name each run R1, R2, …** and use those names everywhere — in the `Needs` of other steps, in
  the chat message, and in the prompt of § 6 — so that "the baseline run" never has to be guessed.
- **A time criterion carries a TTFT control.** A criterion on model time or wall clock is decided
  only when the run's TTFT P50 is within ±30 % of the baseline's; otherwise it reads *not decidable
  (provider latency)* and the token and tool-call criteria decide. Battery run 7's run 89 measured a
  TTFT P50 of 5.3 s against 1.8 s on identical work, so its time figures said nothing about the change.
- **A criterion read from the UI names the capture.** The criterion says which screenshot, capture
  or downloaded image it is read from, the `RUN` step says when to take it (*while the second suite
  runs*, *after the report is written*), and the `SAVE` step saves it into the run's `UI\` folder
  (§ 6 *One folder per runbook*). A UI criterion with no saved capture cannot be verified by the next
  analysis.

## 6. The `SAVE` Step and the Next Analysis

**One `SAVE` step immediately after every `RUN` step**, written out in full each time, never as
*"as Step 6"*:

1. **The downloads.** In the run's detail view, **Download Markdown Report** and **Tool-call
   log**; save both in the run's folder (**One folder per runbook**, below). If the
   round's analysis used the suite's YAML export or a diagnostics capture, say so and name the
   buttons. When the `RUN` step asks for them, also download the two AI-written reports — the
   Executive Summary and the Report for AI Researchers and Developers — from the run's
   **Downloads** (the Download Center, or **View** in the run report's *AI-Written Reports*
   section), after that section shows them written.
2. **The go / stop line**, built from the `RUN` step's criteria table: *"If criteria 1–3 passed,
   go on with Step <n+1>. If any failed, stop and start the analysis with the prompt below."*
   When the next step needs the analysis whatever the result, the line says so and an `AGENT`
   step *"Wait for the analysis of R1"* follows; that analysis writes its own runbook, which says
   whether the remaining steps of this one still hold.
3. **The ready-to-paste analysis prompt**, always on the last `SAVE` step and on every one whose
   go / stop line can stop. It carries: the run number and its R-name; the absolute path of this
   runbook; a **receipt** the developer fills in — *"Steps done: … ; skipped: … ; done in a
   different order: … ; anything that went differently: …"*; and the absolute paths of the
   downloaded files.

**The next analysis does not trust the receipt.** It opens by reading the runbook the prompt
names — the one permitted read of an earlier round's document, because the user pointed at it —
and **verifies each `CHANGE` step from the run's own record, and that each was done on the side
of each run the list put it on** (an import the list placed after R1 must not show in R1's item
revisions): item revisions and assessed
difficulties for the rubric import, `ToolGuidesSha256` and `HarnessVersion` for the restart,
`WikiHeadSha` for the wiki edit, the roster for a roster change. A step that did not land is
reported **first**, before any finding, because a run made on unrepaired rubrics re-produces the
previous round's findings and the analysis would otherwise diagnose them again and write a second
repair that conflicts with the first.

### One folder per runbook

A runbook's artifacts go in **one version folder** of the run-artifacts folder:

- The runbook's first `RUN` step opens the next free version folder, `v<N>`.
- Every `SAVE` step of that runbook writes into `v<N>\R<k>\`, where `R<k>` is the run's name:
  `<model folder>\Battery` and `<model folder>\Run <id>` for the reports and tool-call logs, `UI\`
  for every screenshot, capture and downloaded image a criterion is read from, and, when the step
  asks for them, the suite YAML exports.
- A rubric repair file stays beside the export it was made from.
- The next round's runbook starts `v<N+1>`.
- Each `SAVE` step names the exact subfolder, so the developer never chooses one.

Set on 2026-10-05 by the user's instruction, after one round's R1–R3 landed in three version folders
(`v5`, `v6` and `v7`) and the next analysis had to look for each run in a different one.

## 7. Self-Check Before Handing Over

- [ ] Every human action of the round is **exactly one step** of the list; none lives only in
      another document.
- [ ] **Every manual job comes after the agent has finished.** There is no pause, or each pause
      is justified in the plan's `## Mid-Plan Pauses`, falls on a stage boundary that compiles,
      and its steps and message say the agent has not finished and will continue.
- [ ] Every export the agent needs is an `EXPORT` step at the start; every import is a `CHANGE`
      step after the `AGENT` step — or the plan is very big and says why one sits between stages.
- [ ] The `CHANGE` steps follow § 3's order, or the step that departs from it says why.
- [ ] Step numbers and run names are identical in the plan, the runbook, `task.md` and the chat
      message, and outside the runbook a step is written *"runbook Step n"*.
- [ ] Every step has `Who`, `Needs`, `Where`, `Do`, `Expect`, `If not`; every `Expect` is observable.
- [ ] Every file path is absolute and **exists** (checked, not assumed); every UI label was
      grepped in the current templates.
- [ ] A Visual Studio stop-and-start step exists if anything under `Overseer/ToolGuides`, the
      NetHack wiki or compiled code changed, and it sits after the last such change.
- [ ] No personal folder path appears anywhere in the runbook's reusable text or in a skill.
- [ ] A rubric import is followed by **Assess question difficulty** and the re-review, and the
      step says the assessed difficulty — a question's weight — may change.
- [ ] Every `RUN` step lists every launcher field, the off-launcher settings, the predicted tier
      with the keys that move, an estimate, and criteria with a place to read each.
- [ ] At least one run is marked Required, or the runbook says why none is.
- [ ] The runs follow the priority order — chat (**C**), then the benchmarking system (**B**),
      then model ranking (**M**) — among runs that do not depend on each other: Required marks
      go to unverified **C** and **B** fixes, and no **M** run is proposed while a defect that
      would affect the candidates is open.
- [ ] Every run names its purposes; a **V** run also carries **C** or **B**; an **M** set holds everything but the candidate fixed and
      states its decision rule on all three axes.
- [ ] No model is named from memory; every name was read from configuration or from the analysed
      report.
- [ ] *Throughout: Do Not Change* lists what must not be edited until the last run is analysed:
      the scoring profile, the System AI Configs in the roster, the suite's questions beyond the
      round's repair, and `Overseer/ToolGuides`; every `CHECK` step repeats it.
- [ ] The chat message repeats the *At a Glance* table in short form, with absolute links to
      every document of the round.
- [ ] Every step has a kind and a number; no action sits outside `## Steps`.
- [ ] Every `RUN` step has its own `CHECK` step immediately before it and its own `SAVE` step
      immediately after it.
- [ ] Every change a run must not see is placed after that run's `SAVE` step, says so in its
      title, and is named in that run's `Must not be done yet`.
- [ ] No step needs another document's *content* to be followed; a file to upload or a prompt to
      paste is named by absolute path.
- [ ] The *At a Glance* table matches the steps row for row.
- [ ] Every `SAVE` step has a go / stop line; the last one carries the analysis prompt.
- [ ] Every `SAVE` step names its `v<N>\R<k>` subfolder, and all of the runbook's runs share one
      `v<N>` (§ 6 *One folder per runbook*).
- [ ] Every time criterion carries its TTFT control (± 30 % of the baseline's TTFT P50).
- [ ] Every UI criterion names its capture and the moment to take it, and a `SAVE` step saves it
      into `UI\`.

## 8. Where the UI Facts Come From

Labels drift; these are the files to grep, with the state verified on 2026-09-19.

| Fact | Source |
|---|---|
| Admin tabs; benchmark sub-tabs `Run Benchmark`, `Run History`, `Multi-Run Analysis`, `Manage Suites`, `Scoring Profiles`, `Model Comparison` | `Overseer/ClientApp/src/app/admin/admin.component.ts`, `admin/benchmark/benchmark.component.html` (the sub-tab row); each sub-tab's panel is in `admin/benchmark/<name>-tab/` |
| Launcher fields and the `Start Benchmark` button; `Series Projection`; the `Co-Assessor` field and its panel warnings (field order verified on 2026-09-27: Model Under Test first, above the three fieldsets) | `admin/benchmark/run-tab/benchmark-run-tab.component.html` |
| The `Source Code References` field and its two option labels (from 2026-09-30, harness 44; a run before it always used *Allowed*, so a card repeating an earlier run's configuration sets *Allowed* explicitly) | `admin/benchmark/run-tab/benchmark-run-tab.component.html` |
| Coverage option labels | `Overseer/ClientApp/src/app/services/admin-benchmark.service.ts` |
| Launcher refusals and their messages | `Overseer/Services/Benchmarking/BenchmarkRunLauncher.cs` |
| Delivery-check failure messages | `Overseer/Services/Benchmarking/BenchmarkCandidateRequestProbe.cs`, `BenchmarkGradingRequestProbe.cs` |
| Comparability tiers, key categories and their members | `Overseer/Services/Benchmarking/BenchmarkComparabilityKey.cs` |
| Model comparison columns and excluded measures | `admin/benchmark/model-comparison/model-comparison.component.html`, `Overseer/Services/Benchmarking/BenchmarkModelComparisonService.cs` |
| What is read only at startup | `Overseer/Services/Tools/ToolRegistry.cs`, `Overseer/Services/NetHackWikiService.cs` |
| What re-checks git every 10 minutes | `Overseer/Services/WikiService.cs`, `SourceCodeService.cs`, `KnowledgeBaseService.cs` |
| The host serves the Angular client from `Overseer/wwwroot`, so a Visual Studio start does not rebuild it; `npm run build` in `Overseer/ClientApp/` does | `Overseer/Properties/launchSettings.json` (no SPA proxy), `Overseer/ClientApp/angular.json` (`outputPath`) |

## 9. Cross-References

- [`server_benchmark_to_chat_transfer`](../server_benchmark_to_chat_transfer/SKILL.md) — § 6 the evidence bar and comparability, § 9 criteria and rollback, § 10 the required output this runbook is part of, § 11 the grader-scale caution
- [`server_implementation_planning`](../server_implementation_planning/SKILL.md) — the one-task-directory rule, § *The plan runs uninterrupted; the developer's jobs come last* (pauses, the compile guarantee), the walkthrough's Runbook status section
- [`server_rubric_handoff`](../server_rubric_handoff/SKILL.md) — § 6, the import steps a card repeats
- [`server_wiki_handoff`](../server_wiki_handoff/SKILL.md) — § 4a, the gate and messages a card repeats
- [`supported_ai_models`](../supported_ai_models/SKILL.md) — which models may be proposed as candidates
- [`docs/overseer/ai-benchmark.md`](../../../docs/overseer/ai-benchmark.md) — § *Multi-Run Replicate Sets*, § *Comparability Across Runs*
