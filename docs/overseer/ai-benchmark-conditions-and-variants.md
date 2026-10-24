# GnollBench Information Conditions, Scarce Corpora, Gold Evidence and Experimental Changes

This document describes the parts of GnollBench (the Overseer AI benchmark) that change **what the
candidate can reach** and **what it is told**, and the measurement that reads how far an answer got
to the truth:

- **Information conditions** remove tools from one run: *Production*, *Source code only*, *Closed book*.
- **Corpus conditions** let a scrubbed GnollHack wiki declare itself, so a run on it is never taken
  for a run on the live wiki.
- **Gold evidence** records, per question, the wiki pages, source spans and knowledge-base topics a
  grounded answer reads.
- **Evidence Reach** classifies every answer from its stored tool calls against that evidence.
- **Experimental changes** are benchmark-only, titled changes to the candidate's instructions, tool
  descriptions, experimental tools and tool-output switches, ticked one by one for a run, built in the
  repository or written in Overseer. Production never loads one.

It is a companion to [`ai-benchmark.md`](ai-benchmark.md) (the single-run harness; the features
arrived with harness 59) and [`ai-benchmark-multi-run.md`](ai-benchmark-multi-run.md) (comparability
tiers). The design rationale, the literature behind it and the roadmap are in the research report
`research_report_v5.md` in the plans repository, task directory
`hyvanmielenpelit/MobileGnollHackLogger/2026-10-21/benchmark_run_125_analysis/`.

Implementation:

| Concern | File |
|---|---|
| Information conditions, the source-only notice | `Overseer/Services/Benchmarking/BenchmarkInformationCondition.cs` |
| The recorded prompt options and the prompt wrappers | `Overseer/Services/Benchmarking/BenchmarkCandidatePromptOptions.cs` |
| The corpus marker | `Overseer/Services/Benchmarking/BenchmarkCorpusConditionMarker.cs`, read by `Overseer/Services/WikiService.cs` |
| Gold evidence units and the suite evidence SHA | `Overseer/Services/Benchmarking/BenchmarkEvidence.cs` |
| Evidence Reach | `Overseer/Services/Benchmarking/BenchmarkEvidenceReach.cs` |
| Experimental changes: the catalog, validation, hashes, selection and runtime context | `Overseer/Services/Benchmarking/BenchmarkExperimentalChanges.cs` |
| Changes written in Overseer: the store, ids, locking, folder download | `Overseer/Services/Benchmarking/BenchmarkExperimentalChangeStore.cs`, `GnollHackServer.Data/BenchmarkExperimentalChange.cs` |
| The overlap check against the benchmark questions | `Overseer/Services/Benchmarking/BenchmarkChangeTextScreen.cs` |
| Built-in change folders | `Overseer/BenchmarkChanges/<id>/`, `Overseer/BenchmarkChanges/retired.json` |
| The API | `Overseer/Controllers/AdminBenchmarkController.cs` (`experimental-changes`) |
| The picker and the editor | `Overseer/ClientApp/src/app/admin/benchmark/run-tab/experimental-changes-dialog/`, `experimental-change-editor-dialog/`, `experimental-changes.ts` |
| Experimental tools | `Overseer/Services/Tools/IToolHandler.cs`, `ToolRegistry.cs`, `ToolExecutor.cs`, `GetCharacterInfoTool.cs`, `CharacterTables.cs` |
| `referenced_by` | `Overseer/Services/SourceReferenceIndex.cs` and the three stats tools |
| Pair kinds | `Overseer/Services/Benchmarking/BenchmarkPairedTests.cs` (`ClassifyRunPair`) |
| Comparability keys | `Overseer/Services/Benchmarking/BenchmarkComparabilityKey.cs` |

---

## 1. Information Conditions

### 1.1 The three conditions

| Condition (wire name) | Launcher label | What changes |
|---|---|---|
| Production (`production`) | *Production — every tool, as in the live chat (default)* | Nothing: the candidate gets `Benchmark:AllowedTools` and the production prompt. |
| Source code only (`sourceOnly`) | *Source code only — no wikis, no knowledge base* | The tools that read a wiki or the knowledge base are withheld, and the source-only notice ends the prompt. |
| Closed book (`closedBook`) | *Closed book — no tools, memory only* | No tools at all; the prompt is built with `EnableToolUse = false`. |

The report's display names (`BenchmarkInformationConditions.DisplayName()`, § 1.4) are the earlier,
longer texts — *Production — all tools (production default)*, *Source code only — no wiki, knowledge
base or lookup tools*, *Closed book — no tools* — and the first words name the same condition.

The candidate's tool list is `BenchmarkInformationConditions.ResolveAllowedTools(configured)`:

- **Production** returns `Benchmark:AllowedTools` unchanged.
- **Source code only** returns it without `wiki_search`, `wiki_view`, `get_knowledge_article`,
  `nethack_wiki_search`, `nethack_wiki_view`, `monster_lookup` and `item_lookup` (the lookup tools read
  the GnollHack wiki), in the configured order. The source-code and stats tools stay.
- **Closed book** returns an empty list.

The experimental tools of a run's experimental changes are added to the list under Production and
Source code only, never under Closed book (§ 6.8).

**The source-only notice.** Under Source code only, `BuildSystemPrompt` and
`BuildSegmentedSystemPrompt` append `BenchmarkInformationConditions.SourceOnlyNotice` as a final
paragraph: at the end of the flat prompt, and at the end of the volatile suffix in the segmented form
(the benchmark's volatile suffix is otherwise empty). The notice says the wikis, the knowledge base
and the lookup tools are not available, that *"instructions elsewhere in this prompt that tell you to
consult them first do not apply"*, and that the GnollHack source code, read with the source-code and
stats tools, is the source of truth. The second clause matters: the frozen prefix and `_policy.md`
keep their instructions to call `get_knowledge_article` and `wiki_search` first, and the notice
overrides them. `ChatService` itself is not changed by any condition.

**Closed book.** `CandidatePromptOptionsFor` records `EnableToolUse = false`, so `ChatService` drops
the knowledge-base, source-access and tool-policy sections as it does for any tools-off prompt, and
`ToolRegistry.BuildToolsForRequest` declares nothing (it returns early when tool use is off, and an
empty allowlist matches no tool).

**Graders see the candidate's list.** The per-question grading prompt prints *"- Available tools:
…"*; it names the candidate's resolved list (*None* under Closed book), so a grader of a source-only,
closed-book or experimental-changes answer is told what the candidate had. The graders' and the claim verifier's
own requests keep reading `Benchmark:AllowedTools`: the verifier keeps the wiki.

### 1.2 The launcher

The launcher labels the field **Tool Access**; its info tip ends *"Reports call this the information
condition."* It sits in the *Test Setup* fieldset of Run Benchmark, after **Source Code References**
and an *Experiment settings* caption (*"Leave these at their defaults for a normal run. Change one only
to test what the AI's tools or instructions contribute."*), above **Experimental Changes** (§ 6.14). The
*Experiment settings* part ends with **Feedback Tool**, after Experimental Changes, which offers or
withholds `report_feedback` for the run ([`ai-feedback-reports.md`](ai-feedback-reports.md) § 3).
Production is first and the default. The info tip lists the three conditions in plain words, Production
with a *Default* badge (`BENCHMARK_INFORMATION_CONDITIONS` in `admin-benchmark.service.ts` holds the
labels and the sentences). For the other two the condition's sentence shows under the select
(`#candidateInformationConditionNote`), then a warning (`#candidateInformationConditionWarning`):
*"Test run: its scores are not comparable with normal runs. Compare it only with a run that is
identical except for Tool Access, on the Paired Test tab of its report."* The value travels in
`StartBenchmarkRunRequest.InformationCondition`, so a model batch, a series and a battery template
carry it; the launcher refuses an unknown value with *"Unknown information condition '<v>'."* *Repeat
this run's setup* reads it back from the run's prompt options. A Root Cause Analysis recommendation
always sets Production.

### 1.3 The record

The condition is stored in the run's prompt options record (`CandidatePromptOptionsJson`) as the
field `informationCondition`. **Production writes no field**: the property is null for Production and
omitted from the JSON, so every production record, old and new, serializes and hashes exactly as
before. `BenchmarkCandidatePromptOptions.Describe()` names the condition when it is not Production.
Run History reports a changed condition as a changed run option against the previous run, and the
history card and the run detail's *Prompt* fact show a tag for each setting that differs from
production (`runConditionTags` in `run-report-frame/run-facts.ts`): the condition (`source code only`,
`closed book`), `changes: <spec>` (the experimental changes' spec, without the hash) and
`corpus: <condition:label>`.

### 1.4 Where it is printed

The run report's *Chat Prompt Under Test* block, after the *Tools* line, prints four lines that
`BenchmarkReportBuilder.ConditionManifestLines` builds:

- **Information condition:** the display name;
- **Candidate tools:** the resolved list, comma-separated, or *none*;
- **Corpus condition:** *live*, or *scarce — <label>* (§ 2);
- **Experimental changes:** *none*, or *<titles> (<spec>:<hash12>) — benchmark-only instruction text
  and tools that production does not have*, the titles joined by commas, an id standing in for a change
  the catalog no longer knows (§ 6).

The tool-call log header prints the same four lines, and the copied diagnostics' `options:` line ends
with `condition=…, variant=…, corpus=…`, where `variant` is the stamp or `none`. The *Candidate tools*
line is rebuilt when the report is rendered, from the recorded condition, the recorded experimental
changes and the current `Benchmark:AllowedTools`;
it is the list the run had as long as that setting has not changed since.

---

## 2. Corpus Conditions

### 2.1 The marker

A scrubbed wiki declares itself in `.gnollbench-corpus.json` at the wiki root:

```json
{
  "condition": "scarce",
  "label": "trap-questions-2026-10-28",
  "suite": "GnollHack Trap Questions",
  "baseWikiSha": "<full commit SHA the scrub started from>",
  "createdUtc": "2026-10-28T09:00:00Z",
  "scrubbedPages": ["Races/Human.md", "Roles/Healer.md"]
}
```

`WikiService` reads it at every index pass (`BenchmarkCorpusConditionMarker.Read`) and publishes
`CorpusCondition` together with the index's content fingerprint, so the two always describe the same
index. The recorded value is `condition:label` (at most 128 characters); no file means the live wiki
(null); a file that does not parse, or has no `condition`, records `invalid` and logs a warning. Only
`condition` and `label` reach the run; `suite`, `baseWikiSha`, `createdUtc` and `scrubbedPages` are
for the people and agents who build and audit the branch. The live wiki has no marker.

**The marker is never searchable.** The wiki indexer keeps only `.md`, `.txt` and `.html` files and
skips every path segment that begins with a dot, the file name included; `.gnollbench-corpus.json`
fails both tests.

Every run stamps the value in `BenchmarkRun.CorpusCondition` when its instrument fingerprint is
populated; the report prints it as § 1.4 shows.

### 2.2 Running a scarce session

1. Build the scrubbed branch, `bench/scarce-<suite>-<date>`, in the GnollHackWiki repository, with
   the marker at its root (the `server_benchmark_conditions_and_variants` skill has the method and the
   audit). **The branch is never merged into the wiki's default branch.**
2. Check it out in a **separate worktree** (`git worktree add <path> bench/scarce-<suite>-<date>`), so
   the developer's own wiki clone stays on the live wiki.
3. Point `WikiPath` (User Secrets) at the worktree for the session, and restart Overseer. `WikiService`
   reads `WikiPath` once at startup. When the branch is instead checked out in the configured clone
   itself, the wiki's 10-minute HEAD poll re-indexes on the changed HEAD without a restart, and the
   pass reads the marker.
4. Before the first run, check the manifest line of a short run or the first run's report: it must
   read *Corpus condition: scarce — <label>*. *live* means the old index is still loaded; *invalid*
   means the marker does not parse.
5. Afterwards point `WikiPath` back to the live clone and restart Overseer (or check the default
   branch out again and let the poll re-index).

A forgotten scrubbed corpus degrades the developer's own chat, because live chat reads the same
`WikiService`. The *Corpus condition* line makes it visible on the next run.

### 2.3 The wiki only, not the knowledge base

Phase 1 scrubs the GnollHack wiki only. The knowledge base gets no marker, for two reasons: a
knowledge-base change already moves the `KnowledgeBaseHeadSha` Instrument key, and scrubbing both
would move two Instrument keys at once, which makes the pair *NotComparable* rather than a controlled
contrast. A scarce wiki session therefore runs against the live knowledge base, and the gold facts a
knowledge-base article states are still reachable through `get_knowledge_article` under Production.
Withholding the knowledge base is what *Source code only* does.

---

## 3. Gold Evidence

### 3.1 The `evidence` key

A question may carry a list of evidence units in the questions YAML (`question-yaml-format.ts`), stored
as canonical JSON in `BenchmarkQuestion.EvidenceJson`:

| Key | Required | Meaning |
|---|---|---|
| `kind` | yes | `wiki`, `source` or `kb` |
| `path` | yes | A wiki page or source file, repository-relative with forward slashes (`Races/Human.md`, `src/role.c`); for `kb`, the topic `get_knowledge_article` takes |
| `section` | no | A wiki section, wiki units only; recorded, not matched |
| `lines` | no | `a-b`, 1-based and inclusive, with 1 ≤ a ≤ b; source units only |
| `function` | no | A function name; source units only |
| `personal` | no | `true` when the unit is about the asker's own race or role |

```yaml
format: overseer-benchmark-questions
version: 1
questions:
  - id: 150
    evidence:
      - kind: wiki
        path: Races/Human.md
        personal: true
      - kind: source
        path: src/role.c
        lines: 1200-1260
      - kind: source
        path: src/makemon.c
        function: peace_minded
```

The client rejects an unknown key or kind; the server validates units again (`BenchmarkEvidence.Validate`).
A missing `evidence` key keeps the stored evidence; `evidence: []` clears it. *Download All as YAML*
writes it, and *Import Questions from YAML* and a suite import read it.

**Evidence is metadata.** An import that changes only evidence bumps no `ItemRevision`, keeps the
assessed difficulty and keeps the review mark; the review step shows an **evidence** change badge,
and *Apply N changes* counts it. A change to the question text, difficulty or rubric still makes a new
revision, with or without evidence beside it.

**In Manage Questions.** Each question card lists its gold evidence in a **Gold Evidence** box under the
rubric, with an *Evidence: N* badge beside the difficulty badges, and the dialog subtitle counts the
questions that have any (*11 Questions Total · 3 with Gold Evidence*, or *No Gold Evidence*). *Edit
Question* and *Add Question* edit it as rows: kind, path, then section for a wiki unit or lines and
function for a source unit, and the *personal* flag. The rules are the YAML's: the form validates as the
server does, sends `evidence` only when its rows changed (so a form opened before an evidence import
does not overwrite that import), an emptied list clears the stored evidence, and an evidence-only save
makes no new revision. The create and update endpoints (`POST suites/{suiteId}/questions`,
`PUT questions/{id}`) accept `evidence` with the import's semantics: null keeps, an empty list clears
(`BenchmarkEvidence.Resolve`).

### 3.2 The suite evidence SHA

`BenchmarkEvidence.SuiteEvidenceSha` is the SHA-256 of the ordinal-sorted `questionId:canonicalJson`
lines of every question with evidence; the report prints its first 12 characters. Evidence is read
from the suite **when the report is rendered**, not stamped on the run, so importing evidence after a
run gives that run an Evidence Reach section on its next render, and the prefix tells which manifest
it was read against.

---

## 4. Evidence Reach

### 4.1 Matching

`BenchmarkEvidenceReach.Compute` reads the answer's stored tool-call rows (`BenchmarkRunAnswerToolCall`)
against its question's units. Matching is on the header lines the tools print, case-insensitive, with
`\` read as `/`.

| Unit | Surfaced | Opened |
|---|---|---|
| wiki `P` (with or without `.md`) | a `wiki_search`, `item_lookup`, `monster_lookup` or `wiki_view` result contains the line `--- P.md ---` | the first header of a `wiki_view` result is P, or an `item_lookup` / `monster_lookup` result has exactly one article header and it is P |
| source `F` | a `source_code_search`, `source_code_view` or `get_function_definition` header names F (`--- F:Ln ---`, `--- F:La-Lb ---`, `--- F:La-Lb (name, N lines) ---`) | a `source_code_view` or `get_function_definition` header names F and its range overlaps `lines`; or a `get_function_definition` header names `function`; or, when the unit has neither, any view or definition of F |
| kb `T` | as opened | a `get_knowledge_article` call's `topic` argument equals T |

*Gold surfaced* and *gold opened* mean any unit of the question; *personal context opened* means any
`personal` unit was opened, and is not applicable when the question has none.

Two record states are reported rather than guessed:

- **Unavailable.** A row the retention sweep pruned (`Result` null beside a non-zero
  `ResultLengthChars`; its `ArgsText` is gone with it) hides whether the gold was reached. When a
  relevant row is pruned and no unit was opened, the answer is *Unavailable* and its class
  *NotApplicable*.
- **Record cut.** A relevant result the recorder cut (`ResultTruncated`) without a matching header
  proves nothing; the per-question line adds *"not seen in the stored result (record cut)"*.

Evidence Reach needs the per-call rows, which exist from harness 17, and the outcome class, which
exists from scoring method 13. Read it only on runs that have both.

### 4.2 Classes

Each answer with evidence gets exactly one class, from its outcome (`BenchmarkOutcomeSummary.Classify`):

| Class | Rule | Reads as |
|---|---|---|
| `GroundedCorrect` | Correct, gold opened | Right from reading |
| `UngroundedCorrect` | Correct, gold not opened | Right without reading the gold: from memory, a snippet or another page |
| `RetrievalFailure` | Not correct, no unit ever surfaced | The searches never reached the gold |
| `NavigationFailure` | Not correct, a unit surfaced, none opened | The gold was in a result and never opened |
| `ReadButWrong` | Not correct, gold opened | Answering against the opened evidence, misreading it, or another page contradicting it |
| `NotApplicable` | No evidence, *Unavailable*, no answer, or a run before scoring method 13 | — |

### 4.3 In the report

§ 2 of the run report gets an **Evidence Reach** section, before *Tool Usage Profile*, when any
question has evidence:

- the condition, the corpus condition and the suite evidence SHA prefix;
- the count of each class, of the answers with evidence;
- *gold opened k of n*, *personal context opened k of m*, the zero-call answers, and the
  *Unavailable* count when there is one;
- one line: *"Read but wrong covers answering against the opened evidence, misreading it, and another
  page contradicting it; open the answer before calling it a model trait."*

Each question in § 3 gets one line, for example *"Evidence: surfaced yes (Races/Orc.md), opened no;
personal page Races/Human.md not opened; Retrieval of gold: navigation failure"*, or *"Evidence: not
available (the tool-call rows were pruned)"*. The run detail API carries the class name on each
answer (`BenchmarkRunAnswerDto.EvidenceReach`).

**Triage.** A `ReadButWrong` answer is opened before it is called a model trait: run 125's Q9 read a
race page that stated a falsehood, which is a corpus defect, not a reading failure. `NavigationFailure`
and `RetrievalFailure` point at routing — the queries, the tool contract, the category filter — not at
what the model knows.

---

## 5. Comparability

### 5.1 Keys

Two Instrument keys join the 27 of key definition 2, for 29:

| Key | Value | Absent reads as |
|---|---|---|
| `CorpusCondition` | `BenchmarkRun.CorpusCondition` | `live` |
| `InstructionVariant` (labeled *Experimental changes*) | `BenchmarkRun.InstructionVariant`, the experimental changes' stamp (`<spec>:<hash12>`) | `none` |

Every run recorded before them reads `live` and `none`, so no historical pair changes tier. The
information condition is not a key of its own: it is part of `CandidatePromptOptions`, a Candidate key.

`BenchmarkComparabilityKey.DefinitionVersion` is **3**. Every Multi-Run group persisted under
definition 2 carries a stale `ComparabilityKeyHash` and shows *Comparability definition changed —
re-analyze to refresh the key hash* until it is re-analyzed.

### 5.2 Pair kinds

`BenchmarkPairedTests.ClassifyRunPair` has two pair kinds for these contrasts, checked before the
others:

- **Condition contrast** (`ConditionContrast`): the information conditions differ, every Fundamental
  and Candidate key matches except `CandidatePromptOptions`, the two records are equal once the
  condition is cleared and tools are enabled (`ComparabilitySignatureWithoutCondition`), and the only
  Instrument keys that differ are at most `CandidateSystemPromptSha256` (moved by the notice or the
  dropped tool sections) and `CorpusCondition`.
- **Experimental-changes contrast** (`VariantContrast`): every Candidate key matches,
  `InstructionVariant` differs, and the only other Instrument key that may differ is
  `CandidateSystemPromptSha256` (moved by a change's instructions).

**Every two selections of experimental changes pair this way** (§ 6.13). A run with one change against
a run with none, a run with a set without one change against a run with the whole set, and any two
selections at all differ in `InstructionVariant` and at most in `CandidateSystemPromptSha256`, so the
classification needs no other kind. The explanation computes the difference from the two stamps, by
title, else by id: *"Experimental-changes contrast: run #412 has none; run #415 adds Race and role lookup
tool. Everything else is the same; compare, never pool."* When the runs differ by more than one change it
adds *"The runs differ by 2 changes, so a difference belongs to them together."*; when they have the same
changes with different text, it says so (`BenchmarkPairedTests.ExperimentalChangesContrast`). Two
different selections are never pooled.

Both explanations end *"compare, never pool"*. A pair that differs in condition **and** experimental
changes is *NotComparable*. Neither kind is ever a Root Cause control (only Model comparison pairs are),
and a condition or experimental-changes run never joins production runs in Model Comparison, because the
keys it moves are must-match keys there.

A pair that differs **only in the corpus condition** moves one Instrument key, so it is a
*Verification of a change* pair and, in Multi-Run Analysis, a Tier C cross-condition set: two
conditions to compare, never one group to pool. So is a selection without instructions, such as *Race
and role lookup tool*, against a run without experimental changes, at group level.

### 5.3 Chat consistency

Chat consistency measures the production chat, so `ChatConsistencyComparability.NonProductionReason`
excludes a run with a non-production information condition, a corpus condition other than live, or
experimental changes, each with its own reason (*"the information condition …"*, *"the corpus condition
…"*, *"the experimental changes …"*).

---

## 6. Experimental Changes

### 6.1 What an experimental change is

An experimental change is one titled, self-contained, benchmark-only idea — an instruction paragraph, a
tool-description sentence, a source-derived tool, a tool-output switch — that changes how the candidate
is instructed or equipped without touching production. The launcher's **Experimental Changes** field
ticks any set of them for a run, and the run records the set and its hash. It is the way to try a change
as a controlled pair against the unchanged instructions before it is promoted (§ 6.13).

Changes come from two sources and are described by the same fields:

| Field | Meaning | Built in | Written in Overseer | Hashed |
|---|---|---|---|---|
| `id` | Kebab-case ending `-v<N>`, at most 32 characters; unique across both sources | the folder name | generated from the title, never edited | yes |
| `title` | The name everyone sees; at most 60 characters | yes | yes | no |
| `summary` | One sentence: what the AI gets; at most 220 characters | yes | yes | no |
| `expectedEffect` | Optional, shown as *Should improve*; at most 220 characters | yes | yes | no |
| experimental tool | A registered `IsExperimental` tool offered to the candidate, with an optional description of its own | yes | no (code) | yes |
| experiment | A tool-output switch; the only one is `stats_referenced_by` | yes | no (code) | yes |
| description additions | Production tool → text **appended** to its live description | yes | yes | yes |
| description replacements | Production tool → text that **replaces** its description | yes | yes | yes |
| instructions | Text appended to the end of the system prompt | yes | yes | yes |

A new tool and a tool-output switch are C# code, so only a built-in change can have one; the texts can
be written in either place.

**Text normalization.** Every text is normalized before it is used and before it is hashed
(`BenchmarkExperimentalChangeHash.Normalize`): CRLF and CR become LF, and the text is trimmed. A built-in
`.md` file with CRLF endings and the same text typed into the editor give the same hash and the same
request bytes. A blank text, `instructions` or `expectedEffect` included, counts as none.

**The change hash** (`BenchmarkExperimentalChangeHash.Of`) is the SHA-256 of the change's canonical
lines, sorted ordinally, each ending `\n`: `id:<id>`, `tool:<name>`, `tooldesc:<SHA-256 of text>`,
`experiment:<name>`, `add:<tool>:<SHA-256 of text>`, `replace:<tool>:<SHA-256 of text>` and
`instructions:<SHA-256 of text>`. The source is not in it, and neither are `title`, `summary` and
`expectedEffect`: the wording people see can be corrected at any time without making a run
un-re-runnable, and a change moved from the database into the repository keeps its hash (§ 6.5). A
database change's hash is recomputed from its stored text whenever the catalog reads it.

**Validation** (`BenchmarkExperimentalChangeValidator`, shared by both sources): the id rules; a title
and a summary, each within its length; at least one effect; an experimental tool that is registered and
`IsExperimental`, and an experimental-tool description only beside one; a known experiment; a
description edit's tool registered, **not** experimental and in the benchmark allowlist
(`Benchmark:AllowedTools`, else `BenchmarkService`'s default list); no tool both added to and replaced,
or edited twice, within one change; every text non-empty after normalization and at most 4,000
characters; at most 6 description edits per change. A built-in change's files are named without `/`,
`\` or `..`, end in `.md` and exist in its folder.

**Conflicts.** Two changes cannot run together when both offer the same experimental tool, both turn on
the same experiment, both replace the same tool's description, or one replaces a description the other
adds to (`BenchmarkExperimentalChangeCatalog.ConflictBetween`). Additions to the same tool compose. The
catalog computes each change's `conflictsWith`, the picker disables a conflicting card, and the launcher
refuses the selection.

**Mentions** are a warning, never a refusal. A change mentions another when one of its texts names, as a
whole word, the other's experimental tool, or `referenced_by` for the `stats_referenced_by` experiment.
*Answer from the player's own character* names `get_character_info`, so it mentions *Race and role
lookup tool*. When a ticked change mentions one that is not ticked, the picker and the launcher's summary
box say *"Answer from the player's own character mentions get_character_info, which this run does not
offer."* with an **Add Race and role lookup tool** button. Such a run can be a deliberate test.

### 6.2 Built-in changes

A built-in change is a folder `Overseer/BenchmarkChanges/<id>/`, copied to the build output by a
`Content` item in `Overseer.csproj`, holding `change.json` and the `.md` files it names:

```json
{
  "id": "race-role-tool-v1",
  "title": "Race and role lookup tool",
  "summary": "Adds get_character_info, which reads a race's or role's rules — allowed combinations, intrinsics by level, racial attitudes — straight from the game tables.",
  "expectedEffect": "Questions that depend on the player's race or role are answered from the tables, not from memory.",
  "experimentalTool": "get_character_info",
  "experimentalToolDescription": "get_character_info.md",
  "experiment": null,
  "descriptionAdditions": {},
  "descriptionReplacements": {},
  "instructions": null
}
```

`id` equals the folder name. `experimentalToolDescription` and `instructions` name a `.md` file of the
folder; `descriptionAdditions` and `descriptionReplacements` map a production tool to one.
`BenchmarkExperimentalChangeCatalog` loads every folder once at startup; an invalid folder is logged,
listed with its reason, and never selectable. `Overseer/BenchmarkChanges/retired.json` is a JSON array
of retired built-in ids, outside every hash. The folders live outside `Overseer/ToolGuides`, so they
never move `ToolGuidesSha256`.

Only a built-in change can carry a new tool or a tool-output switch. An agent writes a built-in folder
as part of an implementation plan, and it is reviewed in Git like any other file.

### 6.3 Changes written in Overseer

A text-only change — description additions, description replacements and instructions — can be written
in the picker's editor (§ 6.15) and is stored as a row of `BenchmarkExperimentalChanges`
(`GnollHackServer.Data/BenchmarkExperimentalChange.cs`, migration `AddBenchmarkExperimentalChanges`):
`ChangeId` (unique), `Title`, `Summary`, `ExpectedEffect`, `DescriptionEditsJson`
(`[{ "tool", "mode": "add" | "replace", "text" }]`, normalized text), `Instructions`, `ContentHash`,
`CopiedFromChangeId`, `CreatedAtUtc`, `UpdatedAtUtc`, `LockedAtUtc` and `RetiredAtUtc`. The table has no
foreign keys and stores no user id: authorship of a handful of admin-written changes is not worth adding
personal data that the account-erasure flow would then have to know about, so the picker shows dates
only. `BenchmarkExperimentalChangeStore` creates, edits, retires, restores and deletes the rows, and
every write refreshes the catalog.

- **Ids** are generated from the title on creation: lower-case ASCII, every run of other characters a
  `-`, trimmed to 26 characters at a word boundary, then `-v1`; when either source already holds the id,
  `-2`, `-3` … go before `-v1`. Ids never change.
- **Copy as new version** is offered for any valid change without a tool or a switch, built-in ones
  included. The copy is a database change whose id is the next free `x-v<M>` after `x-v<N>`, with
  `CopiedFromChangeId` set.
- **Edits are conditional.** A save carries the `UpdatedAtUtc` the editor opened with and is an update
  `where ChangeId = <id> and UpdatedAtUtc = <expected>` (and, when the text changes, `and LockedAtUtc is
  null`), so an edit made elsewhere in the meantime is refused: *"It was changed elsewhere since you
  opened it. Reload it and make your edit again."*
- **Delete** is allowed only while the change is unlocked; otherwise *"A run has used it, so it cannot be
  deleted. Retire it instead."* **Retire** and **Restore** are always allowed.

**The catalog** (`BenchmarkExperimentalChangeCatalog`, a singleton) holds the built-in changes and an
immutable snapshot that merges them with the database rows, swapped atomically. The snapshot is rebuilt
on first use, after every store write, and by `RefreshAsync()` at the start of every launch, re-run and
catalog call (the table is tiny), so a change written by another request or process is seen where it
matters. A failed database read keeps the previous snapshot and logs a warning. Built-in changes are
listed first, then the database's, each by title.

### 6.4 Locking and screening

**Locking.** Once a run has used a change, its hashed text must never change, or the run could not be
re-run. After every other check has passed and immediately before the run row is saved, the launcher
(`BenchmarkRunLauncher.LockAndStampAsync`) sets `LockedAtUtc` on each database change of the selection
in one conditional update (`where LockedAtUtc is null`), refreshes the catalog, and resolves and stamps
the selection again. An edit racing the launch either lands before the lock, and the launch stamps the
edited text, or is refused: *"Its text is locked: a run has used it, and that run stays re-runnable only
while the text is unchanged. Copy it as a new version to change the text."* A locked change keeps its
title, summary and *Should improve* editable. A lock is never removed, even when the run is deleted. A
built-in change needs no lock: its text changes only through Git.

**Screening.** The rule of § 6.6 is enforced by the person who writes the change, helped by two checks:

- **A required confirmation** on every save that creates or changes text: *"This text is a general
  procedure or wording. It states no game fact, answer or hint about a particular question."* The server
  refuses such a save without `genericConfirmed: true`. A save that changes only the title, the summary
  or *Should improve* does not need it.
- **An overlap check** (`BenchmarkChangeTextScreen`): every text is compared with every benchmark
  question's `QuestionText` and `ExpectedPoints`, in all suites, as lower-cased word sequences (letters
  and digits; punctuation and spacing ignored). A run of **8 or more consecutive words** shared with a
  question or a rubric is reported, once per question field and at most 10 times: *"Shares "the quest
  artifact of the healer is the staff …" with the rubric of question 3 of Gnoll Basics."* It warns and
  never blocks, because a generic procedure can legitimately echo a question's wording. It runs as the
  person types (`POST …/screen`, 600 ms after typing stops) and again on save.

The overlap check catches copied wording, not a paraphrased answer; the confirmation and the rule of
§ 6.6 remain the guard.

### 6.5 Moving a change into the repository

**Download as folder** (`GET api/admin/benchmark/experimental-changes/{id}/folder`) returns `<id>.zip`,
holding `<id>/change.json` and its `.md` files — `<tool>.md` per description edit and for an
experimental tool's description, `instructions.md` — UTF-8 without a BOM, CRLF. It works for any change,
built in or not. Put the folder in `Overseer/BenchmarkChanges/` and rebuild.

When a built-in folder and a database change share an id:

- **Equal hashes**: the built-in change is the one listed. The database row is shown nowhere, its runs
  still resolve (the hash is the same), and the editor refuses it: *"This change is now built in; change
  it in the repository."*
- **Different hashes**: the database change is the one listed and resolvable. The built-in folder is
  logged as invalid, *"Its id is taken by a change written in Overseer with different text."*, and not
  listed.

This is how a change written in Overseer reaches Git review; promotion to production then follows
§ 6.13.

### 6.6 What a change may and may not contain

A change may contain **generic procedures** (how to search, what to read, when to read the source),
**tool descriptions**, and **source-derived tools** whose facts are extracted mechanically from the
game source and checked by unit tests. It never contains a game fact we authored: no answer, no rubric
text, no question-specific hint, no *"if asked about X, answer Y"*. A sentence that would answer a
benchmark question by itself is a wiki edit in disguise. The anti-overfitting rules of the
`server_benchmark_to_chat_transfer` skill (§ 8) apply in full.

This applies alike to a built-in change and to a change written in Overseer.

### 6.7 A selection, its spec and its stamp

- **Spec.** The ticked ids, sorted ordinally, joined by `+`: `race-role-tool-v1+stats-referenced-by-v1`.
  Null is no changes. `StartBenchmarkRunRequest.InstructionVariant` carries it; the server canonicalizes
  the order and refuses a duplicate.
- **`+` in a query string.** ASP.NET Core decodes `+` in a query string to a space, and Angular's
  `HttpParams` leaves `+` unencoded. Every server-side spec parser turns spaces back into `+` before
  splitting (ids contain no spaces): `BenchmarkExperimentalChangeCatalog.NormalizeSpec`, used by the
  launcher, the runs endpoint and the Feedback tab's `variant` filter.
- **Stamp.** `<spec>:<hash12>`, where the selection hash is the SHA-256 of `<id>:<change hash>\n` per
  change in spec order (`BenchmarkExperimentalChangeHash.OfSelection`). It is stored in
  `BenchmarkRun.InstructionVariant` (160 characters). A longer stamp is refused: *"Too many changes for
  one run: the run record holds 160 characters. Choose fewer."* The four built-in changes together make
  a 91-character spec and a 104-character stamp. Every site that splits a stamp at its first `:` gets the
  spec.
- **Composition** (`BenchmarkExperimentalChangeCatalog.Resolve`) builds a `BenchmarkVariantContext`, the
  runtime context that `ToolExecutionContext.Variant`, the prompt wrappers and the stats tools read, in
  spec order: experimental tools and experiments are the union; a tool's description is its replacement
  when a change replaces it, else the live description (`IToolHandler.Description`, read at resolve time
  and normalized) followed by `"\n\n" + text` for each change that adds to it; an experimental tool's
  description is the change's own text when it gives one, else the tool's declared one; instructions are
  joined by a blank line into `AppendixParts` (`Variant` is the change id) and `Appendix`. Resolved
  contexts are cached per spec and selection hash.
- **Applicability under Tool Access**, per change, `full`, `partial` or `none`
  (`BenchmarkExperimentalChange.AppliesUnder`): under Closed book only instructions reach the model;
  under Source code only a description edit of a tool in
  `BenchmarkInformationConditions.SourceOnlyExcludedTools` does not. A selection with a change that is
  `none` under the chosen Tool Access is refused (*"Search with the player's race and role has no effect
  under Source code only: wiki_search is not offered there. Uncheck it, or choose another Tool Access."*),
  because the stamp would claim a change the model never saw. `partial` is allowed, with a note.

**An addition follows production.** It is appended to the live description, so a change to a production
guide alters what the next run with that addition sends while the stamp stays the same.
`ToolGuidesSha256` then differs between the two runs, so they are not a clean pair. A replacement is a
fixed text and does not follow production.

**Launch refusals**, in this order: a malformed spec (*"'<spec>' is not a list of experimental change
ids joined by '+'."*, which is also what a spec in the earlier `<variant>/minus/<feature>` form gets); an
unknown id (*"Unknown experimental change '<id>'."*); an invalid change (*"<title> is invalid:
<reason>"*); a retired change (*"<title> is retired."*); a duplicate (*"<title> is selected more than
once."*); a conflict (*"<title> and <title> both replace the wiki_search description."*); a stamp too
long; a change with no effect under the Tool Access.

### 6.8 How a selection reaches the candidate

- **The prompt.** The resolved appendix, the changes' instructions, is passed to the two prompt wrappers
  and ends the flat prompt and the volatile suffix, after the source-only notice when both apply. It
  moves `CandidateSystemPromptSha256`. It is not part of the stored options record.
- **The tools.** The candidate's allowlist is the condition's list plus the selection's experimental
  tools (none under Closed book). A changed description is used only for a tool the request declares:
  under Source code only, an addition to `wiki_search` never reaches the model.
- **The context.** `BenchmarkService` puts the context on the `ToolExecutionContext` of the candidate's
  own requests only — the answer, the failed-question re-run and the single-answer re-run — never on a
  grader, verifier, difficulty or synthesis call. `ToolExecutionContext.CloneFor` copies it to each tool
  call.
- **Re-runs.** A re-run resolves the stamped spec, retired changes included, and refuses, *"The
  experimental changes '<spec>' have changed since this run; re-run is not possible."*, when a change is
  gone or invalid, or the selection's hash no longer matches the stamp.
- **The record.** The run report's manifest line reads *Experimental changes: <titles> (<stamp>) —
  benchmark-only instruction text and tools that production does not have* (§ 1.4); the comparability
  key is labeled *Experimental changes* (§ 5.1); the run's tag is `changes: <spec>` (§ 1.3).

**Production never loads a change.** `ToolExecutionContext.Variant` is null on every live-chat request
and on every benchmark run whose Experimental Changes is None, and such a request declares the same
tools, with the same descriptions, and receives the same prompt bytes as under harness 58.

### 6.9 Experimental tools and experiments

- **`IToolHandler.IsExperimental`** (default false). `ToolRegistry.BuildToolsForRequest` skips an
  experimental handler unless the request's experimental changes offer it, before the allowlist is
  consulted. `ToolExecutor` refuses a call to an experimental tool the request's changes do not offer —
  *"Tool '<name>' is not available in this session."* — before the handler runs, and the agent loop
  refuses a call to a tool the request did not declare.
- **Per-request descriptions.** The description a request declares is the composed one when the
  selection changes the tool, otherwise the handler's own. The shared handler's `Description` is set once
  at startup and never changed per request, so concurrent requests cannot see each other's changes. An
  experimental tool has no file in `Overseer/ToolGuides/`, and startup does not log a missing guide for
  it.
- **Experiments.** `stats_referenced_by` adds a `referenced_by` list to the results of
  `get_artifact_stats`, `get_item_stats` and `get_monster_stats` when the request's changes turn it on
  (`BenchmarkExperimentalChangeCatalog.StatsReferencedByExperiment`).

### 6.10 `get_character_info`

An experimental tool that reads the GnollHack race and role tables from the indexed source
(`src/role.c`, `src/attrib.c`, `src/prop.c`, with quest artifact names from `include/artilist.h`),
parsed when the source is indexed.

- **Parameters:** `race` and `role`, both optional strings, at least one required (*"Missing race or
  role parameter"* otherwise). Matching is case-insensitive on the noun, adjective or plural, or the
  male or female form (`gnoll`, `gnollish`, `Cavewoman`, `healers`).
- **Result** (JSON, at most 4,000 characters):
  - a race: `name`, `allowed_roles`, `allowed_alignments`, `always_peaceful_races`,
    `always_hostile_races`, `abilities_by_level` (`[{ "level": n, "ability": "<property name>" }]`)
    and `traits`;
  - a role: `name`, `allowed_races`, `allowed_alignments`, `quest_artifact`, `abilities_by_level` and
    `traits`;
  - `note`: a monster's own flags (always peaceful or hostile, quest leader, guardian or nemesis)
    come before race attitudes, with a pointer to `get_monster_stats`;
  - `raw_definition` and `message` when a table row could not be parsed.
- A name that matches nothing returns an `error` listing the valid races and roles; while the source
  is indexing, the stats tools' guard message.

### 6.11 `referenced_by`

The functions whose code uses the artifact's `ART_`, the monster's `PM_` or the item's object
constant: whole-word occurrences in the GnollHack `src/*.c` files, outside comments, string and
character literals and `#if 0` regions, each mapped to its enclosing function and rendered
`<function> (src/<file>.c:<line>)`, distinct, in file then line order, at most 12 and then *"… and N
more"*. The constant is derived as `util/makedefs.c` derives it, and the field is omitted when the
generated `include/onames.h` or `include/pm.h` does not define it, while the source index is not
ready, or on any failure. It is the first field dropped when a result is too long. The index
(`SourceReferenceIndex`) is built lazily on first use over the same corpus view as
`SourceLivenessIndex`.

### 6.12 The built-in changes

| Id | Title | Contents |
|---|---|---|
| `race-role-tool-v1` | Race and role lookup tool | The experimental tool `get_character_info` with its own description (`get_character_info.md`) |
| `stats-referenced-by-v1` | Where the game code uses it | The experiment `stats_referenced_by`, and one sentence describing `referenced_by` added to the `get_artifact_stats`, `get_item_stats` and `get_monster_stats` descriptions |
| `asker-search-words-v1` | Search with the player's race and role | One sentence added to the `wiki_search` description: when the question states the player's race, role or alignment, include those words in the query |
| `asker-character-procedure-v1` | Answer from the player's own character | Instructions with one generic procedure: when a question states or implies the player's race, role or alignment, put those words in the searches, read that race's or role's page, and when no page states the rule, read the game's race and role tables (`get_character_info`) or the source rather than general NetHack knowledge |

They replace the two instruction variants of the run-125 round, whose texts they hold unchanged. Each
variant guide was the production text plus one blank line and one sentence, so each addition is that
sentence. `source-tools-v1` is *Race and role lookup tool* + *Where the game code uses it*;
`asker-profile-v1`, which extended it, is all four. Composed, the changes give exactly the tools, tool
descriptions and appendix the variants gave, which a unit test checks. No run used either variant, so no
stamp of the earlier form needs mapping.

Under Closed book only *Answer from the player's own character* applies; under Source code only *Search
with the player's race and role* does not apply, since `wiki_search` is withheld there.

### 6.13 Testing, promotion and retirement

**Free combination, by decision.** Changes are ticked freely, in any combination. This reverses the
earlier single-factor rule, under which a variant ran whole, with one feature left out, or with only one
feature, and free composition was deliberately not offered, because with about 11 questions a suite
many ad-hoc combinations produce false discoveries and lose the attribution. The rule was reversed on
2026-10-24 because it made every idea part of a bundle that had to be picked by its slug and then taken
apart in modes, and a run could record nothing but such a bundle. The risk it guarded against is
mitigated instead by three things:

- **the multi-change callout** in the picker and the launcher, *"The effect will belong to these 2
  changes together. To learn which one did it, test each alone later."*;
- **the pair explanation** of the Paired Test tab, which names the exact difference between the two
  stamps — *"Experimental-changes contrast: run #412 has none; run #415 adds Race and role lookup tool.
  Everything else is the same; compare, never pool."* — and adds *"The runs differ by 2 changes, so a
  difference belongs to them together."* when they differ by more than one;
- **the *Confirm a winner on a second suite* tip** of the picker.

One tick is *only one*; the same set without one tick is *leave one out*. Both are ordinary selections
and ordinary experimental-changes contrasts.

**Method.**

- **Test in pairs.** A run with the selection against a run identical except for Experimental Changes,
  nothing else changed, is an experimental-changes contrast (§ 5.2). Run the pair in *Source code only*
  first, where the wiki cannot mask the effect, then in *Production*, to see that it does not hurt where
  the wiki helps. Every pair carries pre-declared acceptance and rollback criteria, cost among them
  (input tokens and model time), and a change motivated by some questions is checked on questions that
  did not motivate it.
- **Learn which change did it.** When a selection of several changes won or lost, test each change alone
  against None, or the selection without one change against the whole selection.
- **Do not fish.** Trying many combinations on one 11-question suite and keeping the best is a
  multiple-comparisons trap. Pre-declare the criteria per selection, and confirm a winner on a second
  suite.
- **Promote** a change that clears its criteria, and a held-out check on a second suite: append its
  description additions to the matching `Overseer/ToolGuides/<tool>.md`, put its replacements in their
  place, and move its instructions into `_policy.md` or the prompt; set an experimental tool's
  `IsExperimental` to false and add it to `Benchmark:AllowedTools`; make a switch the default; record the
  change in the chat change ledger; and make one confirming production run, since the instructions sat in
  the volatile segment and a `_policy.md` sentence sits in the frozen one. A change written in Overseer is
  downloaded as a folder (§ 6.5) first, so the promoted text is the one that was reviewed in Git.
- **Retire, never edit.** A change a run has used is never edited: its hash would no longer match the
  stamp and the run could not be re-run. A change written in Overseer is locked for this automatically
  (§ 6.4); a built-in folder is protected only by this rule. Copy the change as a new version (`-v2`)
  and change the copy. When a production description that a replacement copied changes, the copy is
  stale; the hash records what was used, and the next version re-bases.
- **Retirement.** A retired change is hidden from the picker (listed under *Show retired*) and refused
  in a new launch, but still resolves for re-runs and reports. A built-in change is retired by adding its
  id to `Overseer/BenchmarkChanges/retired.json`; a change written in Overseer with **Retire**, and
  brought back with **Restore**.

**A limit of the series resume guard.** The guard compares the instrument hashes, which see a selection
only through its instructions. An edit of a built-in change's description file alone therefore does not
block resuming a series; only a re-run's stamp check catches it. A change written in Overseer cannot be
edited once a series member has used it, because the launch locked it.

### 6.14 The launcher

The launcher labels the field **Experimental Changes**. It sits under **Tool Access** (§ 1.2) and is a
read-out line of the choice (`#candidateInstructionVariantReadout`) with a **Choose…** button
(`#candidateInstructionVariant`, `.btn-gh btn-gh-small`) that opens the picker (§ 6.15). The read-out is
*None — same as the live chat*, the one change's title (*Race and role lookup tool*), or *2 changes: Race
and role lookup tool, Where the game code uses it* (`changeReadout` in `run-tab/experimental-changes.ts`).
Its info tip says what the changes are, that the live chat never uses them, and to choose None for a
normal run.

With changes chosen, a summary box (`#candidateInstructionVariantSummary`, *What this run changes*) lists
each change as **title** — summary, then the mention warning with its **Add …** button, the multi-change
warning (`#candidateInstructionVariantTogether`), a line for each change that does not fully apply under
the Tool Access choice, and the warning (`#candidateInstructionVariantWarning`) *"Test only: the live chat
never uses it. Compare this run only with a run that is identical except for Experimental Changes, on the
Paired Test tab of its report."*

The launcher keeps the spec in `instructionVariant` and remembers it under
`overseer_admin_benchmark_run_settings`. A remembered or repeated spec (*Repeat this run's setup* splits
the stamp at its first `:`) is kept, in canonical order, only when every id is valid and unretired;
otherwise it resets with *"Experimental Changes reset to None: <id> is no longer offered."* When the
catalog cannot be loaded at all on first load, a remembered spec resets with *"Experimental Changes reset
to None: the list of changes could not be loaded."* An editor save, retire, restore or delete reloads the
catalog.

### 6.15 The dialogs

**The picker** (`run-tab/experimental-changes-dialog/`, `app-experimental-changes-dialog`) is a native
`<dialog>` opened with `showModal()`, `min(76rem, 100vw - 2rem)` wide and a fixed `90dvh` tall, so it
does not grow or shrink as cards are checked and disclosures opened. Escape cancels; there is no light
dismiss. Under the title *Experimental Changes* the subtitle reads *"Select the test-only changes this run
should try. The live chat never uses them."* From a dialog body width of 52 rem the list and **This run**
sit side by side as two panes that scroll independently; below it they form one column, **This run** after
the list, with one scroll. Headings inside the dialog use the header font (Cinzel); body text stays Lato.

- **Used before** (only when there are any): up to six buttons, one per distinct change set of recent
  runs whose changes are all selectable (`recentSelections`), labeled by the titles joined by ` + `
  (*All four* when the set is every selectable built-in change) and the run count, and `aria-pressed`
  when equal to the current selection. One click sets the selection. An (i) beside the label says what
  the buttons are.
- **Changes to test**: a section whose heading row — the title, the number of listed changes, **New
  change**, which opens the editor, and **Clear all** while anything is selected — stays at the top of the
  list pane as the cards scroll beneath it, with a filter field under it when more than eight changes are
  selectable. Built-in changes come first, then the ones written in Overseer, each by title, and invalid
  changes last, disabled with their reason. Each card has:
  - a checkbox labeled by the **title**, set in the header font, and **More actions** at the top right;
  - a meta line under the title: the kind tags as outlined pills derived from the contents — *New tool*,
    *Tool output*, *Tool description*, *Instructions*; for a change written in Overseer, *Written in
    Overseer · edited 24 Oct 2026*; *Used in N runs* or *Not used by a run yet*; and, when locked, a
    **Locked** badge whose (i) explains that a run used this version and **Copy as new version…** makes an
    editable copy;
  - the summary and *Should improve: …* as a gold-ruled highlighted line;
  - a line saying what does not reach the model under the current Tool Access, or why the checkbox is
    disabled (the change has no effect there, conflicts with a selected change, or is invalid), linked to
    the checkbox by `aria-describedby`; when it blocks, it is amber with a warning icon;
  - **Show exactly what the AI gets** as the card's footer under a hairline, one labeled block per effect:
    *New tool* with its description and parameter table; *Tool output* with the switch in plain words; an
    addition with the added text and a nested **Show the whole description**; a replacement with
    `app-text-diff` against the live description, *Side by side* or *Diff*; instructions with their text;
  - the **More actions** popover with, as they apply: *Edit…* (written in Overseer), *Copy as new
    version…* (valid, no tool or switch), *Download as folder* (any change), *Retire* (written in
    Overseer) and *Delete…* (written in Overseer and unlocked), which asks in an inline row on the card,
    *Delete "<title>"?* with **Delete** and **Keep**. An action that does not apply is absent.

  A checked card has a gold border, a gold bar on its left and a gold title; while its checkbox has
  keyboard focus, the whole card is outlined.
- **Show retired (N)**: the retired changes, with **Restore** for one written in Overseer and *Retired in
  the repository* for a built-in one. They cannot be selected.
- **This run**, the second pane: its heading carries the **Technical details** (i), which opens a dialog
  with the spec, the stamp, and each change's id, source, hash prefix and folder or *database*. Then, in
  order:
  - the count, large and gold, with the selected titles, or *"No changes — this run uses exactly what the
    live chat uses."*;
  - the mention, multi-change and Tool Access warnings;
  - **Compare it with** — *This is a normal run.* with none selected, else *A run identical except for
    Experimental Changes: None.* and *Runs with exactly this selection: #415, #420* as links that open the
    reports — always ending in the rule *Never compare these runs with production runs in Model
    Comparison.*;
  - *Tool Access: <name> — everything applies.* when it does, as a fact row with a check. Tool Access has an
    (i) in both states that says it decides which tools the AI gets and is chosen in the launcher;
  - **Testing fairly (4 tips)**, open on first use and then remembered in
    `overseer.benchmark.experimentalChangesDialog.view`, a numbered guide: *Compare in pairs*, *Source code
    only first, then Production*, *Declare the criteria first, cost included*, *Confirm a winner on a
    second suite*. This document's path is behind the (i) of its intro.
- **The footer**: a polite status (*2 changes selected*, *No changes — same as the live chat*),
  **Cancel** and **Use this**. **Use this** is `aria-disabled` with its reason in a footer note while the
  catalog is loading or could not be loaded, while the stamp would be too long, and while a selected
  change has no effect under the Tool Access or conflicts with another selected one.

**The editor** (`run-tab/experimental-change-editor-dialog/`, `app-experimental-change-editor-dialog`) is
a sibling `<dialog>` of the picker in the run tab, never its child: its `close` and `cancel` events stop
there, so closing it never closes the picker. It is `min(64rem, 100vw - 2rem)` wide and opens in three
modes, headed *New experimental change*, *Edit <title>* and *New version of <title>* (prefilled from the
copied change); its subtitle says that new tools and output switches are built in the repository.

- **Name it**: *Title* and *Summary* (required) and *Should improve*, each with a live character count;
  errors show under the field after it is left or on a refused save, and focus moves to the first invalid
  field.
- **What it changes**: one fieldset per effect, each with **Remove** — *Added to the end of a tool's
  description* (a tool select, a textarea, and **What the AI will read**, the live description with the
  addition after it), *Replaces a tool's description* (prefilled with the live description, the change
  shown with `app-text-diff`) and *Added to the end of the AI's instructions* (at most one). The tool
  select offers the production tools a change may edit, marks the ones Source code only withholds, and
  does not offer a tool another block of the change already uses; up to 6 description blocks. The **Add
  …** buttons below the blocks are absent once their limit is reached.
- **Check**: the overlap findings as warnings, or *No overlap with any benchmark question or rubric.*;
  then the required confirmation, unticked again whenever the texts change, and asked only when the save
  creates or changes text.
- **Locked**: editing a locked change shows a banner — *"Used by a run, so its text is locked: that run
  stays re-runnable only while the text is unchanged. You can still edit the title, summary and Should
  improve."* — with **Copy as new version**, and the effect blocks read-only.
- **The footer**: **Cancel** and **Save**. **Save** is `aria-disabled` with its reason while a required
  field or a block's tool or text is empty, there is no effect, or the confirmation is unticked. A 409
  (locked meanwhile, edited elsewhere, or built in) is shown in the footer note, with **Reload** unless the
  change is now built in. Cancel, the close button or Escape with unsaved edits asks *Discard your edits?*
  with **Keep editing** and **Discard** instead of closing.
- **After saving**, the editor closes, the picker reloads the catalog, a new change is ticked and its
  checkbox focused; after an edit, focus returns to the card's **More actions**.

### 6.16 The API

All under `api/admin/benchmark/` in `AdminBenchmarkController`, `AdminOnly`:

| Method and route | Body / query | Result |
|---|---|---|
| `GET experimental-changes` | | `{ changes, recentSelections, productionTools, limits, maxStampLength }` |
| `GET experimental-changes/runs` | `spec` | `{ runCount, olderHashRunCount, runs }`: the ten newest runs whose stamp has exactly this spec, and how many no longer match its current hash; 400 for an invalid spec |
| `POST experimental-changes` | the edit request | 201 `{ change, overlaps }`; 400 with field errors |
| `PUT experimental-changes/{id}` | the edit request and `expectedUpdatedAtUtc` | 200 `{ change, overlaps }`; 400; 404; 409 locked, edited elsewhere, or built in |
| `POST experimental-changes/{id}/retire`, `…/restore` | | 204; 404; 409 built in |
| `DELETE experimental-changes/{id}` | | 204; 404; 409 locked or built in |
| `POST experimental-changes/screen` | `{ texts }` | `{ overlaps }` |
| `GET experimental-changes/{id}/folder` | | `application/zip`, `<id>.zip` |

- **A change**: `id`, `title`, `summary`, `expectedEffect`, `source` (`builtIn` / `database`), `kinds`
  (`newTool`, `toolOutput`, `toolDescription`, `instructions`), `hashPrefix`, `isValid`,
  `invalidReason`, `retired`, `locked`, `createdAtUtc`, `updatedAtUtc`, `copiedFrom`, `experimentalTool`
  (`name`, `description`, `parameterSchemaJson`) or null, `experiment` or null, `descriptionEdits`
  (`tool`, `mode`, `text`, `productionText`), `instructions`, `appliesUnder` (`production`,
  `sourceOnly`, `closedBook`: `full` / `partial` / `none`), `notApplyingUnderSourceOnly`, `mentions`,
  `conflictsWith`, `runCount`, and `canEdit`, `canEditText`, `canDelete`, `canCopy` (`canCopy` is a
  valid change with no tool or switch).
- **`recentSelections`**: up to six distinct specs of the newest runs whose changes are all valid and
  unretired: `spec`, `changeIds`, `runCount`, `lastRunId`, `lastStartedAtUtc`.
- **`productionTools`**: `name`, `description`, `withheldUnderSourceOnly` — the allowlisted
  non-experimental tools, in the allowlist's order.
- **`limits`**: `titleMax` 60, `summaryMax` 220, `textMax` 4000, `descriptionEditsMax` 6.
- **The edit request**: `title`, `summary`, `expectedEffect`, `descriptionEdits` (`tool`, `mode`,
  `text`), `instructions`, `copiedFrom`, `genericConfirmed`.
- **An overlap**: `text` (the shared words), `suiteId`, `suiteName`, `questionId`, `questionNumber`
  (`OrderIndex + 1`), `field` (`question` / `rubric`).

The texts make the catalog one call of a few kilobytes, so the picker has no per-change loading state.

---

## 7. Not Built Yet

The research report's roadmap (§ 7 there) lists the later phases. Not in the code today:

- the **oracle condition** (the gold page handed to the model at a fixed position);
- **divergence tags** (whether GnollHack equals or differs from NetHack on the item) and the
  **held-out flag** (items whose facts never enter the wiki or the knowledge base), as fields on a
  question;
- **knowledge-base scrubbing** and a knowledge-base corpus marker;
- **family-scoped experimental changes** (an `appliesTo` field on a change), a **prompt-profile**
  comparability key, and an **admin-only preview** of a change in the chat.

The roadmap is in `research_report_v5.md`, task directory
`hyvanmielenpelit/MobileGnollHackLogger/2026-10-21/benchmark_run_125_analysis/` in the plans
repository.
