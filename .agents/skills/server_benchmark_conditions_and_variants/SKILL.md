---
name: server_benchmark_conditions_and_variants
description: >-
  GnollBench (Overseer AI benchmark) information conditions, scarce corpora, gold evidence and
  experimental changes. Read when a task involves scarce information, a source code only run, a
  closed book run, comparing scarce with plentiful information, how far a model relies on its
  training data instead of its tools, scrubbing the wiki for a benchmark, a scarce or scrubbed
  wiki branch and its .gnollbench-corpus.json marker, gold evidence or an evidence manifest for a
  suite, evidence reach (grounded correct, retrieval failure, navigation failure, read but wrong),
  held-out questions, an experimental change (instruction variant), a built-in change folder under
  Overseer/BenchmarkChanges or a change written in Overseer's editor, an experimental tool such as
  get_character_info or referenced_by, testing a prompt or tool-guide change before production,
  combining changes in one run, promoting or retiring a change, or improving the AI without adding
  wiki data. Covers writing the evidence YAML from a rubric's SOURCE section and the wiki, building
  and auditing a scrubbed branch, the held-out rule, reading Evidence Reach in an analysis, and
  writing and testing an experimental change, including asking the developer for Download as folder
  when a run used a change written in Overseer.
---

# GnollBench Conditions, Scarce Corpora, Gold Evidence and Experimental Changes

GnollBench is the user's name for the Overseer AI benchmark. What the code does is described in
[`docs/overseer/ai-benchmark-conditions-and-variants.md`](../../../docs/overseer/ai-benchmark-conditions-and-variants.md);
read it first. This skill is the method: how to prepare the inputs, how to read the outputs, and the
rules that keep the measurement honest. The design and its literature are in `research_report_v5.md`
in the plans repository, task directory
`hyvanmielenpelit/MobileGnollHackLogger/2026-10-21/benchmark_run_125_analysis/`.

A benchmark analysis still reads the five mandatory benchmark skills first
(`.agents/AGENTS.md` § *AI Benchmark Findings*); this skill adds to them and replaces none.

## 1. Why the Conditions Exist

A production GnollBench score measures a model and a corpus together: once the wiki states a fact,
the score moves toward "did the model open the page that says it". The conditions separate the two:

| Condition | Built with | Measures |
|---|---|---|
| Production | the launcher default | What a user gets |
| Scarce | a scrubbed wiki branch (§ 3), all tools | Whether the model reaches the truth from the source when the documentation is silent |
| Source code only | the launcher field | A cheap bound on Scarce; it also changes the action space, so read it as a sensitivity check |
| Closed book | the launcher field | The parametric floor and the contamination probe |

The feedback tool `report_feedback` (harness 60) is neither an experimental tool nor part of any
condition's candidate tool list: it is declared outside the allow-list, under every condition that has
a tool loop, and a closed-book candidate, which has none, is not offered it. The one exception is a
run that recorded `feedbackToolWithheld` in its candidate prompt options, because it was launched with
the run's Feedback Tool setting *Withheld*: its candidates are not offered the tool. The field is
recorded under every condition, closed book included, so two runs launched with the same setting
still form a condition contrast and two launched with different settings do not.

Read every contrast **paired per item**, per model, and compare, never pool. With 11 items only
effects of roughly 15–25 points are detectable (the research report's power table); say so in any
conclusion drawn from one suite.

## 2. Writing an Evidence Manifest

The evidence of a suite is a questions YAML that carries only `id` and `evidence` for each question.

1. **Get the current text.** *Download All as YAML* on the suite's Manage Questions toolbar, or the
   file the user gives you. Never invent an id.
2. **One unit per gold page or source range.** For each question:
   - a `source` unit for each file and line range or function in the rubric's `**SOURCE**` section;
   - a `wiki` unit for each GnollHack wiki page that states one of the rubric's key facts;
   - a `kb` unit for each knowledge-base topic that states one, by the topic `get_knowledge_article`
     takes;
   - `personal: true` on the asker's own race or role page when the answer depends on the asker's
     character. Not opening it is a navigation failure even when a generic answer happens to be
     right.
3. **Verify every path on disk.** Wiki paths against `C:\hmp\GnollHackWiki`, source paths and line
   ranges against `C:\hmp\GnollHack`, at the HEAD Overseer indexes (the run's recorded
   `SourceCodeHeadSha` when the manifest is for a recorded run: read with `git show <sha>:<path>`).
   Line numbers drift with commits; a range that no longer covers the function matches nothing.
   Paths are repository-relative with forward slashes; `lines` reads `a-b`.
4. **Keep it narrow.** A unit is gold only when reading it settles a key fact. A broad unit (a whole
   file with no `lines` or `function`) counts any view of the file as opened and inflates *gold
   opened*.
5. **Write the file** as `agent-question-evidence-<slug>_v<N>.yaml` in the plan's task directory,
   UTF-8 without a BOM, **LF** line endings, header `format: overseer-benchmark-questions` and
   `version: 1`, then `questions:` with `id` and `evidence` only. Self-check it with a real YAML
   parser: every id belongs to the suite, every list is non-empty, every `kind` is `wiki`, `source` or
   `kb`.
6. **Hand it off.** The developer imports it with *Import Questions from YAML*; the review step shows
   an **evidence** badge on each card. An evidence-only import makes no new item revision and needs
   no re-assessment. Afterwards each question card in Manage Questions lists its evidence; a single
   unit can be fixed in Edit Question without another import.

Evidence is read from the suite when a report is rendered, so the manifest applies to earlier runs
too; the report's suite evidence SHA prefix tells which version it was read against.

## 3. Building and Auditing a Scrubbed Branch

A scrubbed branch writes to the GnollHackWiki repository: do it only when the user asks, and never on
the wiki's default branch.

1. Start from the wiki HEAD in a separate worktree, on `bench/scarce-<suite>-<date>`.
2. Take the key values from the evidence manifest and the rubrics: one to three atomic values per
   question (a yes or no, a number, an item or monster name in its context).
3. **Grep every key value across the whole wiki**, not only the gold pages (`git grep -n -i`),
   including tables, lists and restatements on other pages. Remove or neutralize every passage that
   states the fact; keep the pages and their structure, so the tools still find a page that says
   nothing.
4. Write `.gnollbench-corpus.json` at the root: `condition` (`scarce`), `label`, `suite`,
   `baseWikiSha`, `createdUtc`, `scrubbedPages`. Record every residual leak you could not or chose
   not to remove in an extra `residualLeaks` list (the reader uses only `condition` and `label`).
   Also list the leaks the scrub cannot reach: knowledge-base articles that state a key value (the
   knowledge base is not scrubbed in Phase 1) and NetHack wiki pages that happen to state the
   GnollHack value.
5. **Never merge the branch into the wiki's default branch**, and never base a wiki handoff on it.
6. The session itself — worktree, `WikiPath`, restart, the *Corpus condition* manifest check, and
   pointing `WikiPath` back afterwards — is a runbook step for the developer (the doc's § 2.2). A run
   whose manifest reads *live* or *invalid* where *scarce* was intended is not a scarce run.

## 4. The Held-Out Rule

Keep a set of questions whose facts are **never** added to the wiki or the knowledge base. They keep
the scarce condition honest after the documentation improves.

- Until a held-out field exists on questions, list the held-out ids in the round's analysis and in a
  comment at the top of the suite's evidence file.
- A wiki or knowledge-base finding on a held-out item is recorded and not fixed: no rung-2 handoff
  targets it.
- Judge a wiki edit as a treatment: the change on the items whose gold pages it touched minus the
  change on control items. A score gain from a wiki edit is a corpus improvement, never a model
  improvement.

## 5. Reading Evidence Reach in an Analysis

Before reading it, check that the run has tool-call rows (harness 17 or later), outcome classes
(scoring method 13 or later), and the evidence version you expect (the suite evidence SHA prefix).

| Class | First reading | What to do |
|---|---|---|
| `GroundedCorrect` | Right from reading | Nothing |
| `UngroundedCorrect` | Right without opening the gold | A latent risk, sharpest where GnollHack differs from NetHack; count it beside accuracy |
| `RetrievalFailure` | The gold never surfaced | Routing: the queries, the category filter, the tool contract, the asker's race or role missing from the query — a tool-layer or instruction finding, not a knowledge one |
| `NavigationFailure` | Surfaced, never opened | Routing again: what the result showed, and why the model stopped |
| `ReadButWrong` | Opened, still wrong | **Open the answer and the page it read before calling it a model trait** |

- **`ReadButWrong` is triaged, never assumed.** It covers answering against the opened evidence,
  misreading it, and another page contradicting it. Run 125's Q9 read a race page carrying a false
  sentence: a corpus defect, which goes to the corpus under the chat-transfer triage and is excluded
  from the model's score.
- **`Unavailable`** means pruned rows; say *not measurable*, not *not reached*. **Record cut** means
  the recorder cut a relevant result; an absent header proves nothing.
- The graders never see tool results, so only these trajectory figures can tell "right from memory"
  from "right from reading".
- Under *Source code only* the wiki units cannot be reached by construction; read the source units.

## 6. Writing and Testing an Experimental Change

An experimental change tries one idea for the candidate's instructions or tools as a benchmark-only
change that production never loads. The model, the two sources, the selection and the dialogs are the
doc's § 6.

- **Built in, or written in Overseer.**
  - A new tool or an experiment switch is C# code, so its change is **built in**. The experiments are
    `stats_referenced_by` (a `referenced_by` field on the stats tools) and `board_as_data` (the board
    delivered inside `<untrusted_game_snapshot>` at the start of the user turn instead of as a system
    message; the structural prompt-injection treatment). A built-in change is a folder
    `Overseer/BenchmarkChanges/<id>/` holding `change.json` and the `.md` files it names (the doc's
    § 6.2). **An agent writes built-in folders**, as files of an implementation plan, UTF-8 without a
    BOM, CRLF; it never writes a change into the database.
  - A text-only change (a description addition or replacement, instructions) can be either. A built-in
    folder is reviewed in Git with the round. The developer can instead write it in the picker's editor
    (**New change**), where it is stored in the database, screened against the benchmark questions and
    locked by its first run; that is a runbook `CHANGE` step that spells out every field
    (`server_benchmark_runbook` § 3 *A change written in Overseer*).
  - **A run that used a change written in Overseer is read through *Download as folder*.** Its text is
    not in the repository, so it cannot be read from disk. Ask the developer for the folder (Admin →
    **GnollBench** → **Run Benchmark** → **Experimental Changes** → **Choose…**, then **More actions** →
    **Download as folder** on the change's card, a `<id>.zip`) before writing any finding about it, and
    name it as an `EXPORT` step when the analysis needs it. The first run locked the change, so the folder
    holds the text that run used. Never rely on a change written in Overseer that you have not been given
    as a folder.
  - A built-in folder and a database change with the same id: equal text, the built-in one wins; other
    text, the built-in folder is invalid (the doc's § 6.5). Pick an id no database change holds.
- **One idea per change.** A change is one titled idea, so a selection can isolate it: split a bundle
  into changes rather than putting several ideas into one. The id is kebab-case ending `-v<N>`, at most
  32 characters; the stamp of a whole selection must fit 160 characters, and the seven built-in changes
  together take 155. The title, summary and *Should improve* are not hashed, so they can be corrected
  later; every text is.
- **Only generic procedures and source-derived tools, never answers.** No rubric text, no
  question-specific hint, no mechanic's value, no *"if asked about X, answer Y"*. Before handoff, read
  every text once against the question *"would this sentence answer a benchmark question by itself?"*.
  A source-derived tool extracts facts mechanically from the game source and is unit-tested against
  it. The anti-overfitting rules of `server_benchmark_to_chat_transfer` § 8 apply in full, to both
  sources. The editor's overlap check finds copied wording only, so a change written in Overseer is read
  the same way once it is handed over as a folder.
- **A change used by a run is never edited.** Its hash would no longer match the run's stamp and the
  run could no longer be re-run. A change written in Overseer is locked for this by its first run; a
  built-in folder is protected only by this rule. Copy it to `<id>-v2` (a new folder, or *Copy as new
  version* in the editor) and change the copy. When a production description that a replacement copied
  changes, the next version re-bases on it.
- **An addition follows production.** It is appended to the live description when the run starts, so a
  production guide edit between the two runs of a pair changes what the treatment run reads without
  changing its stamp. `ToolGuidesSha256` shows it; keep the guides unchanged across a pair.
- **Every pair carries cost criteria.** Pre-declare acceptance and rollback criteria before
  the runs (`server_benchmark_to_chat_transfer` § 9), and always include input tokens and model time:
  an instruction that adds tool rounds can win on quality and lose on cost.
- **Test in *Source code only* first, then in Production.** The wiki cannot mask the effect in Source
  code only; the Production pair shows the change does not hurt where the wiki helps. A description edit
  of a tool the condition withholds never reaches the model, and the launcher refuses a change with no
  effect under the chosen Tool Access, so name what each pair can and cannot test.
- **Check beyond the motivating questions.** A change motivated by some questions is checked on the
  others, and before promotion on a held-out second suite.
- **Combinations are free; attribution is not.** Changes are ticked in any combination (the doc's § 6.13
  records why the earlier single-factor rule was reversed).
  - A selection of several changes is one treatment, and its effect belongs to them together. To learn
    which change did it, test each alone against None, or the selection without one change against the
    whole selection. Both pairs are experimental-changes contrasts on the Paired Test tab, whose
    explanation names the exact difference.
  - Do not try many combinations on one suite and keep the best: with about 11 questions that is a
    multiple-comparisons trap. Pre-declare the criteria per selection, cost included, and confirm a
    winner on a second suite.
  - A mention warning means the model is told about a tool or a field it does not have (*Answer from the
    player's own character* without *Race and role lookup tool* names `get_character_info`). That is a
    deliberate test or a mistake, so say which in the analysis.
- **The run-125 runbook's R3 and R4** named the two removed variants: `source-tools-v1` is *Race and
  role lookup tool* + *Where the game code uses it* (`race-role-tool-v1+stats-referenced-by-v1`), and
  `asker-profile-v1` is all four built-in changes. A runbook step written for a variant is set up with
  those ticks.
- **Promotion** of a change that clears its criteria:
  1. its description additions are appended to the matching `Overseer/ToolGuides/<tool>.md`, its
     replacements put in their place, and its instructions moved into `_policy.md` or the prompt; a
     change written in Overseer is first handed over as a folder, so the promoted text is the reviewed
     one;
  2. an experimental tool's `IsExperimental` becomes false and the tool is added to
     `Benchmark:AllowedTools`; an experiment switch becomes the default;
  3. a row in the chat change ledger (`server_benchmark_to_chat_transfer`
     `references/chat_change_ledger.md`);
  4. one confirming production run, because the instructions sat in the volatile segment and a
     `_policy.md` sentence sits in the frozen one, which can matter to the model and does matter to
     the prompt cache.
  Promoting part of a selection promotes those changes, each a reviewed folder with its own hash.
- **Retirement.** A retired change is hidden from the picker and refused in a new launch, but its runs
  still re-run and report. A built-in change is retired by adding its id to
  `Overseer/BenchmarkChanges/retired.json` in a plan; a change written in Overseer by the developer's
  **Retire**.
- **A family-specific instruction is an interaction claim.** One base prompt stays; a change scoped
  to a provider or model family needs a factorial (change × family) and far more data than a main
  effect (research report § 11). Changes cannot be scoped to a family today.
- A chat trial of an experimental change is impossible by construction and would be a smoke test, never
  evidence.

## 7. References

- [`docs/overseer/ai-benchmark-conditions-and-variants.md`](../../../docs/overseer/ai-benchmark-conditions-and-variants.md)
  — what exists in the code.
- `research_report_v5.md`, plans repository task directory
  `hyvanmielenpelit/MobileGnollHackLogger/2026-10-21/benchmark_run_125_analysis/` — the design, the
  literature, the statistics and the roadmap.
- `server_benchmark_to_chat_transfer` (triage, evidence bar, anti-overfitting, verification and
  rollback), `server_benchmark_runbook` (runs as runbook steps), `server_rubric_handoff` (the YAML
  handoff pattern), `server_wiki_handoff` (rung-2 wiki changes, never on a held-out item).
