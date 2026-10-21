---
name: server_benchmark_conditions_and_variants
description: >-
  GnollBench (Overseer AI benchmark) information conditions, scarce corpora, gold evidence and
  instruction variants. Read when a task involves scarce information, a source code only run, a
  closed book run, comparing scarce with plentiful information, how far a model relies on its
  training data instead of its tools, scrubbing the wiki for a benchmark, a scarce or scrubbed
  wiki branch and its .gnollbench-corpus.json marker, gold evidence or an evidence manifest for a
  suite, evidence reach (grounded correct, retrieval failure, navigation failure, read but wrong),
  held-out questions, an instruction variant, an experimental tool such as get_character_info or
  referenced_by, testing a prompt or tool-guide change before production, promoting or retiring a
  variant, or improving the AI without adding wiki data. Covers writing the evidence YAML from a
  rubric's SOURCE section and the wiki, building and auditing a scrubbed branch, the held-out rule,
  reading Evidence Reach in an analysis, and writing and testing a variant.
---

# GnollBench Conditions, Scarce Corpora, Gold Evidence and Variants

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
   no re-assessment.

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

## 6. Writing and Testing a Variant

An instruction variant tries a change to the candidate's instructions or tools as a benchmark-only
bundle that production never loads. The format is the doc's § 6.

- **Only generic procedures and source-derived tools, never answers.** No rubric text, no
  question-specific hint, no mechanic's value, no *"if asked about X, answer Y"*. Before handoff, read
  every file once against the question *"would this sentence answer a benchmark question by itself?"*.
  A source-derived tool extracts facts mechanically from the game source and is unit-tested against
  it. The anti-overfitting rules of `server_benchmark_to_chat_transfer` § 8 apply in full.
- **A variant used by a run is never edited.** Its hash would no longer match the run's stamp and the
  run could no longer be re-run. Copy it to `<name>-v2` and change the copy; when a production guide a
  variant copied changes, the next version re-bases on it.
- **Every variant pair carries cost criteria.** Pre-declare acceptance and rollback criteria before
  the runs (`server_benchmark_to_chat_transfer` § 9), and always include input tokens and model time:
  an instruction that adds tool rounds can win on quality and lose on cost.
- **Test in *Source code only* first, then in Production.** The wiki cannot mask the effect in Source
  code only; the Production pair shows the change does not hurt where the wiki helps. A tool-guide
  replacement for a tool the condition withholds never reaches the model, so name what each pair can
  and cannot test.
- **Check beyond the motivating questions.** A variant motivated by some questions is checked on the
  others, and before promotion on a held-out second suite.
- **Promotion** of a variant that clears its criteria:
  1. its tool-guide texts move into `Overseer/ToolGuides/`, and its appendix into `_policy.md` or the
     prompt;
  2. an experimental tool's `IsExperimental` becomes false and the tool is added to
     `Benchmark:AllowedTools`; an experiment switch becomes the default;
  3. a row in the chat change ledger (`server_benchmark_to_chat_transfer`
     `references/chat_change_ledger.md`);
  4. one confirming production run, because the appendix sat in the volatile segment and a
     `_policy.md` sentence sits in the frozen one, which can matter to the model and does matter to
     the prompt cache.
- **A family-specific instruction is an interaction claim.** One base prompt stays; a variant scoped
  to a provider or model family needs a factorial (variant × family) and far more data than a main
  effect (research report § 11). Variants cannot be scoped to a family today.
- A chat trial of a variant is impossible by construction and would be a smoke test, never evidence.

## 7. References

- [`docs/overseer/ai-benchmark-conditions-and-variants.md`](../../../docs/overseer/ai-benchmark-conditions-and-variants.md)
  — what exists in the code.
- `research_report_v5.md`, plans repository task directory
  `hyvanmielenpelit/MobileGnollHackLogger/2026-10-21/benchmark_run_125_analysis/` — the design, the
  literature, the statistics and the roadmap.
- `server_benchmark_to_chat_transfer` (triage, evidence bar, anti-overfitting, verification and
  rollback), `server_benchmark_runbook` (runs as runbook steps), `server_rubric_handoff` (the YAML
  handoff pattern), `server_wiki_handoff` (rung-2 wiki changes, never on a held-out item).
