---
name: server_benchmark_runbook
description: >-
  Mandatory closing deliverable of every Overseer AI benchmark analysis: the Developer Runbook
  (developer_runbook_v<N>.md) that tells the developer exactly what to do next and in which
  order. Covers the dependency order in which a round's fixes must be applied (plan execution,
  GnollHack plan, wiki handoff, knowledge-base push, Overseer restart, rubric repair import and
  difficulty re-assessment, grader roster or profile changes), the rule that the agent
  implements the whole plan uninterrupted and the developer's manual jobs come at the very end,
  the special-case mid-plan pause with its proof that the solution compiles and its explicit
  "not finished" message, exports at the start and imports at the end, the step-card format that makes
  every step followable with no other document open, the run cards that specify each following
  benchmark run field by field as the launcher shows it, the purposes a run may serve in
  their binding order of importance (first improving the main Overseer chat, then the
  benchmarking system, then deciding which models perform best as the Overseer AI on
  intelligence, speed and cost, with validation of the round's fixes taking the rank of what it
  validates), predicting the
  comparability tier of a run before it is made, model-selection run design, cost and wall-time
  estimates, what the developer hands back after a run, and the self-check before handoff. Read
  once a benchmark analysis has triaged its findings and before its implementation plan is
  written; read whenever a user asks "what do I do next", "which runs should I make", "in what
  order do I apply these fixes", or "which model should Overseer use".
---

# Developer Runbook: Ordered Fix Steps and the Runs That Follow

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
  very end.** The plan implements every change, the agent builds and tests, and only then does
  Part A of the runbook begin. The exceptions are narrow and are in § 3.

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

## Part A - Apply the Fixes, in This Order      <- step cards (section 3)
###   Before the agent starts                   <- S1..Sn: exports the agent needs; usually none
###   The agent implements the plan             <- one card: approve, then wait for "finished"
###   Pauses                                    <- P1..Pn: special cases only; usually none
###   Your jobs, after the agent has finished   <- A1..An: everything manual, in order
## Part B - Runs to Make                        <- run cards R1..Rn (section 5)
## Part C - After Each Run                      <- what to download, where, and the prompt (section 6)
## Part D - Do Not Change Until the Runs Are Done   <- the freeze list (section 4)
```

**The chat message that delivers the round repeats Part A and the run-card titles in short
form** — one line per step, one line per run with its mark and estimated cost — after the links
to the documents. The developer should be able to start from the chat message alone.

**A round with nothing to fix still owes a runbook.** Part A is one line — *"No fix to apply;
nothing to restart or import."* — and Part B proposes the next run on purposes C, B or M, in
that order of preference (§ 4).

**After the plan is executed**, `walkthrough.md` carries a **Runbook status** section: every
Part A step marked *done* or *remaining*, and every value that only became known during
execution — the new `HarnessVersion`, the `ToolGuidesSha256` the next run should show, a
migration name. The completion chat message lists the remaining steps again. Write a new
`_v<N>` of the set only when a step's *content* had to change.

## 3. Part A: Ordering the Fixes

### Who works when

**The agent first, without interruption; the developer last.** The implementation plan applies
every change of the round, the agent builds and tests, and the developer's manual jobs begin
only when the agent has said it is finished. A developer who has to build and start Overseer in
the middle of a plan is building a half-changed tree, and a developer who is handed a job between
two stages cannot tell whether the agent is done. Part A therefore has four blocks, and in most
rounds the first and the third are empty and say so in one line:

| Block | Cards | What goes here |
|---|---|---|
| **Before the agent starts** | `S1…` | An **export** the agent needs as input — the suite's **Download All as YAML**, a database query result. Ask for it while the analysis is still running, so the plan is written on it and never waits for it |
| **The agent implements the plan** | one card | *Approve the plan. The agent then works through all of it without asking you for anything, and tells you when it has finished.* Name what "finished" looks like: the completion message, `walkthrough.md`, the build and test results |
| **Pauses** | `P1…` | **Special cases only** (below). Usually: *"None — the agent does not need you until it has finished."* |
| **Your jobs, after the agent has finished** | `A1…` | Everything manual, in the order of the table that follows. **Imports** into Overseer belong here |

### A pause is a special case

A pause is allowed only when a later **agent** step needs something that only the running
application can produce or take in, and it cannot be moved to the start or the end. An export or
import of Overseer data **between** stages is the same thing and is accepted only in a **very big
plan**. `server_implementation_planning` § *The plan runs uninterrupted; the developer's jobs come
last* owns the rules; what the runbook and the pause message owe the developer is:

- **The solution compiles at that point, and the agent has proven it** — `dotnet build
  MobileGnollHackLogger.slnx` with 0 errors, plus `npm run build` from `Overseer/ClientApp/` when
  the client changed, and any new migration already applied — *before* the developer is asked to
  press Start. The card states the command and its result. Visual Studio builds whatever is on
  disk; a pause on a tree that does not compile costs the developer an error list they did not
  cause.
- **Step cards as clear as any other** (format below), under the pause's `P<n>` name, which is
  the same name the plan's pause step carries.
- **The words "I have not finished".** The pause message opens by saying that this is a pause,
  which stage it follows, which stages remain, what the agent needs back, and that it **will
  continue working as soon as the developer replies**. The card's last line repeats it:
  *"When this is done, tell the agent. The agent still has work to do — do not commit, do not
  launch a run."*

The wiki confirmation gate of `server_wiki_handoff` § 4a is a pause only when an agent step waits
behind it. When what waits is the developer's own restart or run, the wiki sessions are ordinary
`A` cards.

### The order of the developer's jobs

Leave out what the round does not have; never reorder without saying why in the card.

| Order | Job | Why it sits here |
|---|---|---|
| 1 | A `gnollhack_` plan, in a session on the GnollHack clone; then export the new snapshot and attach it to the suite if the round says so | A new board is a **Fundamental** key (`GameSnapshot`): it must land before any run that is meant to use it, and never between the halves of a pair |
| 2 | Wiki handoff: Step 2 (validation, a chat on the GnollHack clone), then Step 3 (execution, a chat on the GnollHackWiki clone) only on `VALIDATION: PASS`; commit and push the wiki | `server_wiki_handoff` § 4a owns the wording. Before the start of Overseer, so one start covers it |
| 3 | Knowledge-base article push | Reloads on a 10-minute HEAD poll; it changes the frozen prompt segment, so it moves `KnowledgeBaseHeadSha` and `CandidateSystemPromptSha256` |
| 4 | **In Visual Studio: stop Overseer if it is running, then start it** | Starting builds the finished tree. `Overseer/ToolGuides` and the NetHack wiki are read **only at startup**; the GnollHack wiki, the source index and the knowledge base re-check their git HEAD every 10 minutes, and a fresh start settles all of them at once. In development the Angular client is served through the SPA proxy, so no separate client build is needed. Any migration was already applied by the agent |
| 5 | Rubric repair: **Import Questions from YAML**, validate, review, confirm, **Assess question difficulty**, re-mark reviewed | An import, so it is an end job; after the start, so the import and the difficulty assessment run on the round's code. The launcher refuses the suite until every question has an assessed difficulty. **Exception:** when the import is the isolated variable of a pair, it goes *between* the two runs and the card says so |
| 6 | Grader roster, scoring-profile or System AI Config changes the round recommends | Last, and only between series: a roster or thinking-level change is an **Instrument** key move and must never fall between a motivating run and its confirming run |
| 7 | The pre-launch check (below) | The cheapest place to catch a skipped job |

**One numbering for the whole round.** `S`, `P`, `A` and `R` names are used identically in the
plan, the runbook, `task.md`, the walkthrough and every chat message. A pause is the only card
that is also a plan step. Two names for one step is a defect.

### The step card

One step is **one action in one place with one expected result**. If a step needs "and then",
it is two steps.

```text
### <S|P|A><n> - <imperative title>
- Who: You | A new chat opened on <absolute clone path>
- Needs: A<k>, A<m> finished      (or: nothing)
- Where: <exact UI path, label by label>  |  <absolute directory and shell>
- Do: <numbered clicks with the labels exactly as the UI prints them, or the exact command
       in a fenced block, or the absolute path of the file to upload or the prompt to paste>
- Expect: <what appears when it worked - a message, a badge, a count, a line of output>
- If not: <the one thing to do - usually: stop, paste what you see into this chat>
- Takes: <rough time; and cost, when the step spends money, e.g. Assess question difficulty>
```

Rules for writing a card:

- **Absolute paths** for every file, in a form that can be pasted into a file dialog.
- **UI labels verbatim, in bold**, in click order: Admin → **AI Benchmark** → **Manage Suites** →
  … . **Grep each label in the Angular template before writing it** (§ 8 names the files);
  a label recalled from an older round is how a runbook sends someone to a button that moved.
- **Commands in their own fenced block**, one per block, PowerShell unless stated.
- **Repeat, do not link.** The rubric import steps (`server_rubric_handoff` § 6) and the wiki
  sequence and gate message (`server_wiki_handoff` § 4a) are written out inside the cards. Those
  skills still own the wording; the card copies it.
- **Every `Expect` is observable.** "The import succeeded" is not; *"the review step lists exactly
  Q3 and Q7, both as replace, none as create"* is.
- **Say what waits.** A step another step depends on says so in `Needs` on the dependent card, and
  a confirmation gate is a card of its own.
- **Overseer is restarted from Visual Studio**: stop it, then start it; starting builds the tree.
  The card says exactly that, and its `Expect` names something visible — the Admin page loads,
  and the next run's manifest shows the round's `HarnessVersion` or a changed `ToolGuidesSha256`.
- **No personal path goes into a skill or the repository.** Downloads go to *"your run-artifacts
  folder"*; the card proposes a subfolder name and nothing above it.
- **A `P` card ends with**: *"When this is done, tell the agent. The agent still has work to do —
  do not commit, do not launch a run."*

### The pre-launch check (always the last card of Part A)

A short checklist the developer ticks immediately before the first run:

- [ ] The agent has said it is **finished** — not paused — and the walkthrough exists.
- [ ] Overseer was stopped and started in Visual Studio **after** the agent finished and after the last change to the NetHack wiki.
- [ ] At least 10 minutes have passed since the last wiki, source or knowledge-base push — or the start came after it.
- [ ] Admin → **AI Benchmark** → **Run Benchmark**: selecting the suite shows **no** `Difficulty n/m Assessed` warning.
- [ ] The suite card shows no `Needs review` badge the round did not expect.
- [ ] No run is in progress.
- [ ] The models, thinking levels and scoring profile match the run card exactly — including the
      settings that are **not** launcher fields (§ 5).

## 4. Part B: Deciding Which Runs to Propose

### The purposes, in order of importance

Every proposed run serves at least one purpose, and its card says which. **The table is in
priority order, and the order is binding** — it is the benchmark's own
(`server_benchmark_to_chat_transfer` § *What the Benchmark Is For*).

| Priority | Tag | Purpose | Typical shape |
|---|---|---|---|
| **1 — most important** | **C** | **Improve the main Overseer chat**: the system and its tools work correctly, the models perform at maximum efficiency, there are no bugs or other problems, and quality improves in every respect | A run that re-exercises a repaired tool, corpus, prompt section or request path; an isolated-variable pair for a chat-transferable finding that has not met the evidence bar (`server_benchmark_to_chat_transfer` § 6); a run read for latency, token volume and cache-read share after an efficiency change |
| **2** | **B** | **Improve the benchmarking system**, so that it works rigorously and correctly and its results serve both the chat and a scientific comparison of models | A run chosen to exercise a harness, grader or suite question: double grading to measure grader agreement, another suite to test a detector off the questions that motivated it, a replicate set to measure reproducibility |
| **3** | **M** | **Decide which models perform best as the Overseer AI** on intelligence, speed and cost | The same exam under the same graders, with only the candidate changed (below) |
| — | **V** | Validate this round's fixes. Not a priority of its own: a **V** run inherits the priority of the fixes it validates, so its card always carries **C** or **B** beside **V** | The motivating run's configuration, unchanged, after Part A |

What the order decides:

- **Which runs are proposed, and which are Required.** A run is **Required** when a chat fix
  (**C**) or a harness or suite fix (**B**) is unverified without it. An **M** run is never
  Required by a round whose findings were about the chat or the instrument; it is Recommended or
  Optional, and the card says what decision it would inform.
- **The sequence of the cards.** Runs that carry **C** come first, then **B**, then **M**. The
  developer can stop after any card and will have spent the money on the most important question
  first.
- **No model ranking on a system known to be broken.** An **M** run or set is proposed only when
  the round leaves **no open defect in the chat system, its tools or the harness that would
  affect the candidates** — and if one is open, the runbook says so and defers the **M** runs to
  the round that closes it. A ranking taken on a defect measures the defect, and it usually
  does not measure it equally across providers.
- **What a card's criteria look at first.** On a **C** run the first criteria are about the
  system, not the score: every tool call succeeded or failed for a documented reason, the
  repaired behaviour appears, no delivery check failed, latency and token volume did not regress.
  The Intelligence Index comes after them.
- **Spend.** When the developer's budget allows fewer runs than the runbook lists, the priority
  order is the order in which they are cut, from the bottom.

**One run can be enough, and often is**: a single confirming run is usually tagged **V C B** —
the same report verifies the chat and harness fixes and re-measures the production prompt — and
it also adds one data point to the model record at no extra cost, which the card may note without
tagging the run **M**. Propose a further run only when it answers a question the first cannot.
Mark every run **Required**, **Recommended** or **Optional**, and order the cards so that stopping
after any card leaves a coherent result.

### Rules for a validating run (V)

- **Same configuration as the motivating run** — suite, scoring profile, model under test,
  assessor, second-opinion assessor and mode, claim verifier, `Concise` response style. The card
  lists each value and marks it *same as run <R>*.
- **Predict the comparability tier before the run is made**, from what Part A moves, using the
  key categories in `BenchmarkComparabilityKey.cs`:

  | Part A moved | Key category | Pair lands in |
  |---|---|---|
  | Nothing the harness fingerprints | — | Tier A (Replicate) |
  | Only question parallelism, speed calibration or pricing | SpeedAndCost | Tier B |
  | **Exactly one** of: `HarnessVersion`, `ToolGuidesSha256`, `CandidateSystemPromptSha256`, `KnowledgeBaseHeadSha`, `ScoringMethodVersion`, scoring profile, assessor / second-opinion / claim-verifier configuration, per-question budgets | Instrument | Tier C (compare, never pool) |
  | Two or more of those | Instrument | **NotComparable** |
  | A rubric import (item revisions), a difficulty re-assessment, a new board, another suite | Fundamental | **NotComparable** |
  | Model, thinking level, service tier, parallel mode, response style | Candidate | **NotComparable** |

  **Say the predicted tier and name the keys on the card.** When the prediction is
  NotComparable — the usual case after a rubric import — say what that means in practice: score
  deltas against run `<R>` prove nothing, and the criteria are therefore written as **absolute
  observations** (a count of tool misses, a flag that must not appear, a rubric point no longer
  charged), not as index movements.
- **Offer the isolated pair only when attribution is worth a second run's cost**: R1 after the
  code change and *before* the rubric import, R2 after it. Say the cost of the extra run and what
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
  profile, one assessor, one second-opinion configuration, one claim verifier, `Concise` style —
  and **no Part A action between the first and the last run of the set**. Make the set *after*
  the round's fixes, never straddling them.
- **Take candidates from what is configured, never from memory**: the System AI Configs with the
  Benchmark role, the `RecommendedModels` entries in `Overseer/appsettings.json`, and the
  `supported_ai_models` policy. Always include the currently recommended model of the provider
  being challenged, as the baseline. The card names models as the `Model Under Test` dropdown
  prints them.
- **Choose an assessor from a provider none of the candidates shares** where the roster allows.
  Where it does not, the card warns that the `Same-Provider Assessment Warning` dialog will
  appear for that run and that `Acknowledge & Start Run` is the intended answer.
- **State the decision rule before the runs are made**, on the three axes:
  - *Intelligence* — Intelligence Index with its interval, critical errors, refuted claims. A
    candidate inside the baseline's interval is a tie, not a loss or a win.
  - *Speed* — **median model time and TTFT P50 / P90**, never the Speed Index, which saturates
    and is comparable only within one thinking level. Name the ceiling a mid-game player
    tolerates, in milliseconds, from the analysis.
  - *Cost* — **candidate cost per question** from the Model Under Test cost card, never the run
    total, which is mostly grading. Note any price that is promotional or dated.
- **Read the result in Admin → AI Benchmark → Model Comparison → `Open comparison wizard`.** An
  entry listed under *Entries excluded from every figure* was not comparable, and the card says
  in advance that this must not happen for the set.
- A model change is ladder **rung 6** and still has to clear the evidence bar: one run per
  candidate motivates a recommendation; a second comparable run, or a replicate set, justifies
  changing `RecommendedModels`.

### Cost and time

Every run card carries an **estimated cost and wall time**, taken from the analysed run's own
figures (total cost, grading share, duration) and scaled by question count and `Number of Runs`.
Say what the estimate is based on. Tell the developer to compare it with the launcher's
**Series Projection** (`Projected wall time`, `Projected cost`, `Remaining daily headroom`) and
to stop if the projection is more than about twice the estimate.

## 5. The Run Card

```text
### R<n> - <short title> - [V][C][B][M] - Required | Recommended | Optional
- Answers: <the one question this run settles, in a sentence>
- Start only after: A<k> ... A<m>, and R<j> if this run depends on its result
- Where: Admin -> AI Benchmark -> Run Benchmark -> "Configure & Execute Benchmark"

| Launcher field | Set to | Versus run <R> |
|---|---|---|
| Benchmark Suite | <name as listed> (<n> questions) | same |
| Scoring Profile | <name> | same |
| Model Under Test | <entry as the dropdown prints it, with its thinking-level badge> | same / CHANGED |
| Assessor Model | ... | same |
| Second Opinion Assessor (optional) | ... or "None - no second opinion" | same |
| Second Opinion Mode | <option label> | same |
| Claim Verifier (optional) | ... or "None - no claim verification" | same |
| Candidate Response Style | Concise - production default; comparable with previous runs | same |
| Number of Runs | 1 | |

- Not in the launcher, and must also match: thinking level, service tier, reasoning mode and
  parallel mode (from the System AI Config behind each dropdown entry: check the badges);
  Blind Second Opinion and max parallel questions (from the scoring profile: Admin ->
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

Rules for writing a card:

- **Every launcher field is listed, even the unchanged ones.** The card is a form to copy, not a
  diff. Field labels and option labels are verbatim (§ 8).
- **The `Detailed` response style is never proposed for a V or M run** — its own label says it is
  not comparable with previous runs. It appears only as the isolated variable of a C pair.
- **`Second Opinion Mode` does nothing without a second-opinion assessor**; a card that sets one
  sets the other.
- **Name each run R1, R2, …** and use those names everywhere — in Part A's `Needs`, in the chat
  message, and in the prompt of § 6 — so that "the baseline run" never has to be guessed.

## 6. Part C: After Each Run

One card, the same for every run of the round unless a run needs more:

1. In the run's detail view, **Download Markdown Report** and **Tool-call log**; save both in
   your run-artifacts folder, in a new subfolder the card names.
2. If the round's analysis used the suite's YAML export or a diagnostics capture, say so and name
   the buttons.
3. Start the next analysis with the **ready-to-paste prompt** the card provides. It carries: the
   run number and its R-name; the absolute path of this runbook; a **receipt** the developer
   fills in — *"Part A steps done: … ; skipped: … ; anything that went differently: …"*; and the
   absolute paths of the downloaded files.

**The next analysis does not trust the receipt.** It opens by reading the runbook the prompt
names — the one permitted read of an earlier round's document, because the user pointed at it —
and **verifies each Part A step from the run's own record**: item revisions and assessed
difficulties for the rubric import, `ToolGuidesSha256` and `HarnessVersion` for the restart,
`WikiHeadSha` for the wiki edit, the roster for a roster change. A step that did not land is
reported **first**, before any finding, because a run made on unrepaired rubrics re-produces the
previous round's findings and the analysis would otherwise diagnose them again and write a second
repair that conflicts with the first.

## 7. Self-Check Before Handing Over

- [ ] Every human action of the round appears in Part A **exactly once**; none lives only in
      another document.
- [ ] **Every manual job comes after the agent has finished.** The *Pauses* block is empty, or
      each pause is justified in the plan's `## Mid-Plan Pauses`, falls on a stage boundary that
      compiles, and its card and message say the agent has not finished and will continue.
- [ ] Every export the agent needs is an `S` card asked for at the start; every import is an `A`
      card at the end — or the plan is very big and says why one sits between stages.
- [ ] The `A` cards follow § 3's order, or the card that departs from it says why.
- [ ] `S`, `P`, `A` and `R` names are identical in the plan, the runbook, `task.md` and the chat
      message.
- [ ] Every card has `Who`, `Needs`, `Where`, `Do`, `Expect`, `If not`; every `Expect` is observable.
- [ ] Every file path is absolute and **exists** (checked, not assumed); every UI label was
      grepped in the current templates.
- [ ] A Visual Studio stop-and-start card exists if anything under `Overseer/ToolGuides`, the
      NetHack wiki or compiled code changed, and it sits after the last such change.
- [ ] No personal folder path appears anywhere in the runbook's reusable text or in a skill.
- [ ] A rubric import is followed by **Assess question difficulty** and the re-review, and the
      card says the assessed difficulty — a question's weight — may change.
- [ ] Every run card lists every launcher field, the off-launcher settings, the predicted tier
      with the keys that move, an estimate, and criteria with a place to read each.
- [ ] At least one run is marked Required, or the runbook says why none is.
- [ ] The runs follow the priority order — chat (**C**), then the benchmarking system (**B**),
      then model ranking (**M**): cards are in that sequence, Required marks go to unverified
      **C** and **B** fixes, and no **M** run is proposed while a defect that would affect the
      candidates is open.
- [ ] Every run names its purposes; a **V** run also carries **C** or **B**; an **M** set holds everything but the candidate fixed and
      states its decision rule on all three axes.
- [ ] No model is named from memory; every name was read from configuration or from the analysed
      report.
- [ ] Part D lists what must not be edited until the last run is analysed: the scoring profile,
      the System AI Configs in the roster, the suite's questions beyond the round's repair, and
      `Overseer/ToolGuides`.
- [ ] The chat message repeats Part A and the run titles in short form, with absolute links to
      every document of the round.

## 8. Where the UI Facts Come From

Labels drift; these are the files to grep, with the state verified on 2026-09-19.

| Fact | Source |
|---|---|
| Admin tabs; benchmark sub-tabs `Run Benchmark`, `Run History`, `Multi-Run Analysis`, `Manage Suites`, `Scoring Profiles`, `Model Comparison` | `Overseer/ClientApp/src/app/admin/admin.component.ts`, `admin/benchmark/benchmark.component.html` |
| Launcher fields and the `Start Benchmark` button; `Series Projection` | `admin/benchmark/benchmark.component.html` |
| Second Opinion Mode option labels | `Overseer/ClientApp/src/app/services/admin-benchmark.service.ts` |
| Launcher refusals and their messages | `Overseer/Services/Benchmarking/BenchmarkRunLauncher.cs` |
| Delivery-check failure messages | `Overseer/Services/Benchmarking/BenchmarkCandidateRequestProbe.cs`, `BenchmarkGradingRequestProbe.cs` |
| Comparability tiers, key categories and their members | `Overseer/Services/Benchmarking/BenchmarkComparabilityKey.cs` |
| Model comparison columns and excluded measures | `admin/benchmark/model-comparison/model-comparison.component.html`, `Overseer/Services/Benchmarking/BenchmarkModelComparisonService.cs` |
| What is read only at startup | `Overseer/Services/Tools/ToolRegistry.cs`, `Overseer/Services/NetHackWikiService.cs` |
| What re-checks git every 10 minutes | `Overseer/Services/WikiService.cs`, `SourceCodeService.cs`, `KnowledgeBaseService.cs` |
| The Angular client is served through the SPA proxy in development, so a Visual Studio start needs no client build | `Overseer/Overseer.csproj` (`SpaProxyLaunchCommand`) |

## 9. Cross-References

- [`server_benchmark_to_chat_transfer`](../server_benchmark_to_chat_transfer/SKILL.md) — § 6 the evidence bar and comparability, § 9 criteria and rollback, § 10 the required output this runbook is part of, § 11 the grader-scale caution
- [`server_implementation_planning`](../server_implementation_planning/SKILL.md) — the one-task-directory rule, § *The plan runs uninterrupted; the developer's jobs come last* (pauses, the compile guarantee), the walkthrough's Runbook status section
- [`server_rubric_handoff`](../server_rubric_handoff/SKILL.md) — § 6, the import steps a card repeats
- [`server_wiki_handoff`](../server_wiki_handoff/SKILL.md) — § 4a, the gate and messages a card repeats
- [`supported_ai_models`](../supported_ai_models/SKILL.md) — which models may be proposed as candidates
- [`docs/overseer/ai-benchmark.md`](../../../docs/overseer/ai-benchmark.md) — § *Multi-Run Replicate Sets*, § *Comparability Across Runs*
