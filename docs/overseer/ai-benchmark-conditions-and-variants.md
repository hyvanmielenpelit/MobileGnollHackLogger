# GnollBench Information Conditions, Scarce Corpora, Gold Evidence and Instruction Variants

This document describes the parts of GnollBench (the Overseer AI benchmark) that change **what the
candidate can reach** and **what it is told**, and the measurement that reads how far an answer got
to the truth:

- **Information conditions** remove tools from one run: *Production*, *Source code only*, *Closed book*.
- **Corpus conditions** let a scrubbed GnollHack wiki declare itself, so a run on it is never taken
  for a run on the live wiki.
- **Gold evidence** records, per question, the wiki pages, source spans and knowledge-base topics a
  grounded answer reads.
- **Evidence Reach** classifies every answer from its stored tool calls against that evidence.
- **Instruction variants** are benchmark-only bundles of instruction text, tool-guide text,
  experimental tools and tool-output switches. Production never loads one.

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
| Variant catalog and runtime context | `Overseer/Services/Benchmarking/BenchmarkInstructionVariant.cs` |
| Variant folders | `Overseer/BenchmarkVariants/<name>/` |
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

A variant's experimental tools are added to the list under Production and Source code only, never
under Closed book (§ 6.4).

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
closed-book or variant answer is told what the candidate had. The graders' and the claim verifier's
own requests keep reading `Benchmark:AllowedTools`: the verifier keeps the wiki.

### 1.2 The launcher

The launcher labels the field **Tool Access**; its info tip ends *"Reports call this the information
condition."* It sits in the *Test Setup* fieldset of Run Benchmark, after **Source Code References**
and an *Experiment settings* caption (*"Leave these at their defaults for a normal run. Change one only
to test what the AI's tools or instructions contribute."*), above **Experimental Changes** (§ 6.4). The
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
`closed book`), `variant: <name:hash12>` and `corpus: <condition:label>`.

### 1.4 Where it is printed

The run report's *Chat Prompt Under Test* block, after the *Tools* line, prints four lines that
`BenchmarkReportBuilder.ConditionManifestLines` builds:

- **Information condition:** the display name;
- **Candidate tools:** the resolved list, comma-separated, or *none*;
- **Corpus condition:** *live*, or *scarce — <label>* (§ 2);
- **Instruction variant:** *none*, or *<name> (<hash12>) — benchmark-only instruction text and tools
  that production does not have* (§ 6).

The tool-call log header prints the same four lines, and the copied diagnostics' `options:` line ends
with `condition=…, variant=…, corpus=…`. The *Candidate tools* line is rebuilt when the report is
rendered, from the recorded condition, the recorded variant and the current `Benchmark:AllowedTools`;
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
| `InstructionVariant` | `BenchmarkRun.InstructionVariant` (`name:hash12`) | `none` |

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
- **Variant contrast** (`VariantContrast`): every Candidate key matches, `InstructionVariant` differs,
  and the only other Instrument key that may differ is `CandidateSystemPromptSha256` (moved by an
  appendix).

**Ablation pairs are variant contrasts too** (§ 6.10). An ablation's stamp is its own `InstructionVariant`
value, so the classification needs no new kind: a `<name>/minus/<feature>` run against a run of the whole
`<name>`, and a `<name>/only/<feature>` run against a run without a variant, each differ in
`InstructionVariant` and at most in `CandidateSystemPromptSha256`, and are `VariantContrast` pairs. An
ablation is never pooled with its base, with no variant, or with another ablation.

Both explanations end *"compare, never pool"*. A pair that differs in condition **and** variant is
*NotComparable*. Neither kind is ever a Root Cause control (only Model comparison pairs are), and a
condition or variant run never joins production runs in Model Comparison, because the keys it moves
are must-match keys there.

A pair that differs **only in the corpus condition** moves one Instrument key, so it is a
*Verification of a change* pair and, in Multi-Run Analysis, a Tier C cross-condition set: two
conditions to compare, never one group to pool. So is a variant that adds no appendix, such as
`source-tools-v1`, against a run without a variant, at group level.

### 5.3 Chat consistency

Chat consistency measures the production chat, so `ChatConsistencyComparability.NonProductionReason`
excludes a run with a non-production information condition, a corpus condition other than live, or an
instruction variant, each with its own reason (*"the information condition …"*, *"the corpus condition
…"*, *"the instruction variant …"*).

---

## 6. Instruction Variants

### 6.1 What a variant is

A variant is a named, hashed, benchmark-only bundle that changes how the candidate is instructed and
equipped without touching production. It is the way to try a change — an instruction sentence, a tool
description, a source-derived tool, a tool-output switch — as a controlled pair against the unchanged
instructions before it is promoted. A run records the variant's name and content hash.

### 6.2 The folder

A variant is a folder `Overseer/BenchmarkVariants/<name>/`, copied to the build output by a
`Content` item in `Overseer.csproj`, holding `variant.json` and the files it names:

```json
{
  "name": "source-tools-v1",
  "description": "One sentence shown in the launcher.",
  "extends": null,
  "promptAppendix": null,
  "toolGuides": { "get_character_info": "get_character_info.md" },
  "experimentalTools": ["get_character_info"],
  "experiments": ["stats_referenced_by"]
}
```

| Field | Meaning |
|---|---|
| `name` | Equal to the folder name; lower-case kebab-case, at most 64 characters |
| `description` | The *Technical note* of the launcher's summary box (§ 6.4); one sentence |
| `extends` | Another variant whose content this one starts from, or null |
| `promptAppendix` | A Markdown file of the folder whose text ends the system prompt, or null |
| `toolGuides` | Tool name → a file of the folder whose text replaces that tool's description for the candidate's requests |
| `experimentalTools` | Experimental tools the candidate is offered |
| `experiments` | Tool-output switches; the only one is `stats_referenced_by` |

**Validation.** `BenchmarkInstructionVariantCatalog` loads every folder once at startup. A variant is
invalid when `name` is not the folder name or not kebab-case; it extends itself, a missing or invalid
variant, or a variant that itself extends one; `promptAppendix` is not a `.md` file of the folder; a
`toolGuides` key is not a registered tool or its file is not in the folder; an `experimentalTools`
entry is not a registered tool or not experimental; or an `experiments` entry is unknown. Files are
named without directories or `..`. An invalid variant is logged, listed with its reason, and refused
by the launcher; *Instruction Variant* offers the valid ones only.

**`extends`** is one level deep. The child's tool guides override the parent's for the same tool;
experimental tools and experiments are the union; the appendix is the parent's text, a blank line,
then the child's.

**The content hash** is `BenchmarkService.ComputeToolGuidesSha256(folder)`, the same manifest that
`ToolGuidesSha256` uses (the SHA-256 of the sorted `relative path:file SHA-256` lines of every file in
the folder, `variant.json` included). A variant that extends another hashes `parent:<parent hash>` and
`self:<own hash>` together. The run stamp, `BenchmarkRun.InstructionVariant`, is `<name>:<first 12 hex
of the hash>`. The variants live outside `Overseer/ToolGuides`, so they never move `ToolGuidesSha256`.

### 6.3 What a variant may and may not contain

A variant may contain **generic procedures** (how to search, what to read, when to read the source),
**tool descriptions**, and **source-derived tools** whose facts are extracted mechanically from the
game source and checked by unit tests. It never contains a game fact we authored: no answer, no rubric
text, no question-specific hint, no *"if asked about X, answer Y"*. A sentence that would answer a
benchmark question by itself is a wiki edit in disguise. The anti-overfitting rules of the
`server_benchmark_to_chat_transfer` skill (§ 8) apply in full.

### 6.4 How a variant reaches the candidate

- **The request.** `StartBenchmarkRunRequest.InstructionVariant` names it; the launcher refuses an
  unknown name (*"Unknown instruction variant '<name>'."*) or an invalid one (*"The instruction variant
  '<name>' is invalid: <reason>"*). `GET api/admin/benchmark/instruction-variants` lists every variant
  with its name, description, parent, hash prefix, validity and reason.
- **The prompt.** The resolved appendix is passed to the two prompt wrappers and ends the flat prompt
  and the volatile suffix, after the source-only notice when both apply. It moves
  `CandidateSystemPromptSha256`. It is not part of the stored options record.
- **The tools.** The candidate's allowlist is the condition's list plus the variant's experimental
  tools (none under Closed book). A variant's tool-guide text is used only for a tool the request
  declares: under Source code only, a replacement `wiki_search` guide never reaches the model.
- **The context.** `BenchmarkService` puts the variant on the `ToolExecutionContext` of the
  candidate's own requests only — the answer, the failed-question re-run and the single-answer re-run —
  never on a grader, verifier, difficulty or synthesis call. `ToolExecutionContext.CloneFor` copies it
  to each tool call.
- **Re-runs.** A re-run resolves the variant by the stamped name and refuses, *"The instruction variant
  '<name>' has changed since this run; re-run is not possible."*, when the variant is gone, invalid,
  or its hash no longer matches the stamp.
- **The launcher.** The launcher labels the field **Experimental Changes**; its info tip ends *"Reports
  call this the instruction variant."* It sits under **Tool Access** (§ 1.2) and is a read-out line of the
  choice with a **Choose…** button (`#candidateInstructionVariant`, `.btn-gh btn-gh-small`) that opens the
  full-screen Experimental Changes dialog (§ 6.10). The read-out is *None — same as the live chat*, a
  whole variant as *<name> — <what it adds> · <hash prefix>*, or an ablation in words (*asker-profile-v1
  without the reworded wiki_search description*). The suffix and the summary
  are derived from what the variant contains (`run-tab/instruction-variant-summary.ts`), never written
  for it, so a new variant needs no launcher text and `variant.json` never has to change for the
  launcher's sake (editing it would move the variant's hash):

  | The variant | Suffix |
  |---|---|
  | adds a tool or an experiment, and has an appendix | *new tools and extra instructions* |
  | adds a tool or an experiment only | *new tools* |
  | has an appendix only | *extra instructions* |
  | only replaces tool guides | *reworded tool descriptions* |
  | none of these | the name alone |

  With a variant chosen, a summary box (`#candidateInstructionVariantSummary`) is headed *What
  <name> changes* and lists, in plain words, each experimental tool (*Adds a tool the live chat does not
  have: race and role lookup.*), each experiment, the appendix, the reworded tool guides and the parent
  (*Builds on source-tools-v1.*); a tool without a plain name shows its identifier as code. Then the
  variant's `description` as a *Technical note*, and the warning *"Test only: the live chat never uses
  it. Compare this run only with a run that is identical except for Experimental Changes, on the Paired
  Test tab of its report."* When the Tool Access choice voids part of the variant, one more warning
  (`#candidateInstructionVariantConditionWarning`) says so: under Closed book only the appendix applies,
  or nothing does; under Source code only a reworded guide of a withheld tool does not apply. For an
  ablation the summary lists what the run changes — the base's lines without the left-out feature, or the
  one kept feature — and names its reference run (`#candidateInstructionVariantReference`). *Repeat
  this run's setup* strips the hash suffix (splitting the stamp at its first `:`, so an ablation spec is
  kept whole) and, when the catalog no longer lists the name as valid, or an ablation's base is invalid
  or no longer has the feature, chooses None and says so. The launcher keeps the spec in
  `instructionVariant` and remembers it under `overseer_admin_benchmark_run_settings`.
- **The catalog endpoint.** Besides the name, description, parent, hash prefix, validity and reason,
  each entry carries `experimentalTools` and `experiments` (a parent's included), `rewordedTools` (the
  `toolGuides` keys that are not experimental tools), `hasPromptAppendix`, and
  `rewordingsWithheldUnderSourceOnly` (the reworded tools Source code only withholds), all from the
  resolved variant (`BenchmarkInstructionVariantDto.From`); the lists are empty for an invalid one. Each
  entry also carries `features` (§ 6.10): `{ id, kind, target, fromParent, appliesUnder: { production,
  sourceOnly, closedBook }, mentions }`, so the launcher can validate and label a remembered ablation
  without another call. `GET api/admin/benchmark/instruction-variants/{name}/details` returns one folder
  variant for the dialog: `name`, `description`, `parent`, `stamp`, `isValid`, `invalidReason`, the
  features with their `text` (an experimental tool's or a reworded description's variant guide, an
  appendix part's text), `productionText` (a reworded description's startup production text, from
  `Overseer/ToolGuides/`) and `parameterSchemaJson` (an experimental tool's parameter schema), and
  `runs`: the ten newest runs whose stamp starts with `<name>:` or `<name>/` (the whole variant and its
  ablations), each with its spec and whether its hash equals the current one, plus `runCount` and
  `olderHashRunCount`. An invalid variant has no features and no stamp; an unknown name, or an ablation
  spec, is 404.

**Production never loads a variant.** `ToolExecutionContext.Variant` is null on every live-chat
request and on every benchmark run whose variant is None, and such a request declares the same tools,
with the same descriptions, and receives the same prompt bytes as under harness 58.

### 6.5 Experimental tools and experiments

- **`IToolHandler.IsExperimental`** (default false). `ToolRegistry.BuildToolsForRequest` skips an
  experimental handler unless the request's variant lists it in `experimentalTools`, before the
  allowlist is consulted. `ToolExecutor` refuses a call to an experimental tool the request's variant
  does not enable — *"Tool '<name>' is not available in this session."* — before the handler runs, and
  the agent loop refuses a call to a tool the request did not declare.
- **Per-request guide text.** The description a request declares is the variant's guide when it has
  one for the tool, otherwise the handler's own. The shared handler's `Description` is set once at
  startup and never changed per request, so concurrent requests cannot see each other's variant. An
  experimental tool has no file in `Overseer/ToolGuides/`, and startup does not log a missing guide for
  it.
- **Experiments.** `stats_referenced_by` adds a `referenced_by` list to the results of
  `get_artifact_stats`, `get_item_stats` and `get_monster_stats` when the request's variant enables it.

### 6.6 `get_character_info`

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

### 6.7 `referenced_by`

The functions whose code uses the artifact's `ART_`, the monster's `PM_` or the item's object
constant: whole-word occurrences in the GnollHack `src/*.c` files, outside comments, string and
character literals and `#if 0` regions, each mapped to its enclosing function and rendered
`<function> (src/<file>.c:<line>)`, distinct, in file then line order, at most 12 and then *"… and N
more"*. The constant is derived as `util/makedefs.c` derives it, and the field is omitted when the
generated `include/onames.h` or `include/pm.h` does not define it, while the source index is not
ready, or on any failure. It is the first field dropped when a result is too long. The index
(`SourceReferenceIndex`) is built lazily on first use over the same corpus view as
`SourceLivenessIndex`.

### 6.8 The shipped variants

| Variant | Contents |
|---|---|
| `source-tools-v1` | `get_character_info` with its guide; `stats_referenced_by`; the three stats-tool guides as in production plus one sentence describing `referenced_by`. No appendix and no new instruction prose. |
| `asker-profile-v1` | Extends `source-tools-v1`. An appendix with one generic procedure: when a question states or implies the player's race, role or alignment, put those words in the searches, read that race's or role's page, and when no page states the rule, read the game's race and role tables (`get_character_info`) or the source rather than general NetHack knowledge. The production `wiki_search` guide plus one sentence to the same effect. |

Measured against each other, `asker-profile-v1` isolates the instruction from the tools.

### 6.9 Testing, promotion and retirement

- **Test in pairs.** A run with the variant against a run without it, nothing else changed, is a
  variant contrast. Run the pair in *Source code only* first, where the wiki cannot mask the effect,
  then in *Production*, to see that it does not hurt where the wiki helps. Every pair carries
  pre-declared acceptance and rollback criteria, cost among them (input tokens and model time), and a
  variant motivated by some questions is checked on questions that did not motivate it.
- **Promote** a variant that clears its criteria, and a held-out check on a second suite: move its
  tool-guide texts into `Overseer/ToolGuides/` and its appendix into `_policy.md` or the prompt; set
  the tool's `IsExperimental` to false and add it to `Benchmark:AllowedTools`; make a switch the
  default; record the change in the chat change ledger; and make one confirming production run, since
  the appendix sat in the volatile segment and a `_policy.md` sentence sits in the frozen one.
- **Retire, never edit.** A variant a run has used is never edited: its hash would no longer match the
  stamp and the run could not be re-run. Copy it to a new folder with the next suffix (`-v2`) and change
  that. When a production guide that a variant copied changes, the copy is stale; the hash records what
  was used, and the next version re-bases.

### 6.10 Ablations

A run can use a folder variant whole, the variant **with one feature left out**, or **only one** of its
features. Both are single-factor ablations: they say which part of a variant made its effect. Free
composition of several hand-picked features is deliberately not offered: with about 11 questions a
suite, many ad-hoc combinations produce false discoveries and lose the attribution.

**Features** are derived from the resolved variant (`BenchmarkInstructionVariantCatalog`, `DeriveFeatures`),
never written in `variant.json`, so no folder and no hash changes:

| Feature id | Kind (`kind` on the wire) | What it is |
|---|---|---|
| `tool.<name>` | experimental tool (`experimentalTool`) | The tool together with its variant guide |
| `experiment.<name>` | tool-output switch (`experiment`) | For example `experiment.stats_referenced_by` |
| `guide.<tool>` | reworded description (`rewordedGuide`) | A `toolGuides` entry for a tool that is not one of the variant's experimental tools |
| `appendix.<variant>` | instruction paragraph (`appendix`) | The appendix of that variant folder; a child and its parent contribute one each, so they can be ablated separately |

The order is experimental tools, experiments and reworded descriptions in ordinal order, then the
appendices, parent first. A feature inherited from the parent has `fromParent`. The context keeps the
appendices per folder in `BenchmarkVariantContext.AppendixParts`; `Appendix` stays their concatenation,
which the prompt and the series fingerprint read. Feature ids contain no `:`.

- `source-tools-v1`: `tool.get_character_info`, `experiment.stats_referenced_by`,
  `guide.get_artifact_stats`, `guide.get_item_stats`, `guide.get_monster_stats`.
- `asker-profile-v1`: those five, inherited, plus `guide.wiki_search` and `appendix.asker-profile-v1`.

**The spec** is the string the launch request sends in `StartBenchmarkRunRequest.InstructionVariant`:

| Spec | Meaning | Compare it with |
|---|---|---|
| `<name>` | The whole variant | A run without a variant |
| `<name>/minus/<feature>` | The variant with that feature left out | A run of the whole `<name>` |
| `<name>/only/<feature>` | Just that feature of the variant | A run without a variant |

**Resolution.** `BenchmarkInstructionVariantCatalog.Find(spec)` resolves a plain name exactly as before. A
spec containing `/` that is not a folder name is parsed (`TryParseSpec`) and resolved to a synthesized
entry whose context is the base's with the feature removed (`minus`) or with only that feature kept
(`only`):

- a removed tool also removes its guide;
- a removed appendix part leaves the other part;
- a removed reworded guide falls back to the production description, as for any tool without a variant
  guide.

Its `Name` is the spec, and its hash is the SHA-256 of `base:<base hash>\n<mode>:<feature id>\n`, so the
stamp `<spec>:<hash12>` changes whenever the base variant's files change. Valid synthesized entries are
cached per spec; `All` lists folder variants only. Every consumer that resolves a variant goes through
`Find`, so the launch, the stamp, the re-run check, the grader's tool list and the series resume guard
handle an ablation unchanged. The sites that read the stamp as a string split it at the first `:`, which
yields the spec. A plain variant records, receives and prints exactly what it did before ablations
existed, so there is no `HarnessVersion` bump and `BenchmarkComparabilityKey.DefinitionVersion` stays 3.

**Refusals.** `Find` returns an invalid entry, which the launcher reports as *"The instruction variant
'<spec>' is invalid: <reason>"*, when:

- the spec is not `<variant>/<mode>/<feature>`;
- the base is not a variant, or is invalid;
- the mode is not `minus` or `only`;
- the feature is not one of the base's (the reason lists the base's features);
- the base has one feature only: `minus` *leaves nothing; choose None*, and `only` *is the whole variant;
  choose it*;
- the stamp would be longer than the 160 characters `BenchmarkRun.InstructionVariant` holds.

The launcher (`BenchmarkRunLauncher.AblationConditionRefusal`) also refuses an ablation whose feature
**does not apply under the chosen Tool Access**, because the run would be identical to its reference and
the contrast empty: under Closed book only an appendix applies, and under Source code only a reworded
description of a withheld tool does not apply. The message names the feature, its id, the condition
and the run it would equal.

**Dangling references** are a warning, never a refusal. Each feature carries `mentions`, the other
features its text names, found mechanically: an experimental tool's name, and `referenced_by` for
`experiment.stats_referenced_by`. Each of the three reworded stats guides mentions the experiment, and
`asker-profile-v1`'s appendix mentions `tool.get_character_info`. When an ablation keeps a text that
mentions a feature it removes — `asker-profile-v1/minus/tool.get_character_info` keeps an appendix that
names `get_character_info` — the dialog says the model will be told about a tool it does not have, and
requires a checkbox before **Use this**. Such a run can be a deliberate test.

**The report** prints an ablation in words in the condition manifest
(`BenchmarkInstructionVariantCatalog.DescribeAblation`): *asker-profile-v1, leaving out the reworded
wiki_search description (3fa2c91b0d1e) — benchmark-only instruction text and tools that production does
not have*, or *…, only the get_character_info tool (…)*. A plain variant prints as before.

**The dialog** (`run-tab/instruction-variant-dialog/`, opened by the launcher's **Choose…**) is a native
full-screen `<dialog>`:

- an **About** disclosure, open on first use and remembered in
  `overseer.benchmark.instructionVariantDialog.view`, which explains variants, the pairing rule, *Source
  code only first, then Production*, pre-declared criteria including cost, the one-at-a-time rule below,
  and never comparing with production runs in Model Comparison;
- on the left, a radio group of *None — same as the live chat*, each valid variant (name, suffix, hash,
  parent, the number of runs that used it), and invalid variants disabled with their reason;
- on the right, for the checked variant: *How to run it* (*The whole variant*, *Leave one feature out*,
  *Only one feature*, shown when the variant has two or more features); *What it changes*, one section
  per feature, marked *From <parent>* when inherited — an experimental tool's guide and its parameters, a
  reworded description's diff against production with a *Side by side* / *Diff* toggle, an appendix's
  text; in the two ablation modes a *Leave this out* or *Run only this* radio per feature, unavailable
  with its reason when the feature does not apply under the current Tool Access; what applies under that
  Tool Access; the result (the spec, its words, the run to compare it with, any dangling-reference
  warning); the technical stamp; and the runs that used the variant or its ablations;
- **Use this**, `aria-disabled` with a stated reason until the choice is complete and every warning is
  acknowledged, and **Cancel** (Escape cancels).

A reworded description shows *The live description has changed since this variant copied it; a -v2
should re-base on it.* when a non-blank line of the live description is missing from the variant's copy.
A variant that deliberately deletes a production line also triggers it.

**Method.**

- **One ablation at a time, and confirm a winner on a second suite.** Running every ablation on one
  11-question suite and keeping the best is a multiple-comparisons trap.
- Pre-declare the criteria per ablation, as for a variant (§ 6.9).
- **Promotion** of an ablation result promotes the corresponding subset of the variant as a new `-vN`
  folder, never the spec, so a promoted change is again an ordinary variant with its own hash.

**A limit of the series resume guard.** The guard compares the instrument hashes, which see a variant
only through its appendix. A base edit that touches only a guide file therefore does not block resuming
a series, for a plain variant and an ablation alike; only a re-run's stamp check catches it. This
predates ablations and is unchanged.

---

## 7. Not Built Yet

The research report's roadmap (§ 7 there) lists the later phases. Not in the code today:

- the **oracle condition** (the gold page handed to the model at a fixed position);
- **divergence tags** (whether GnollHack equals or differs from NetHack on the item) and the
  **held-out flag** (items whose facts never enter the wiki or the knowledge base), as fields on a
  question;
- **knowledge-base scrubbing** and a knowledge-base corpus marker;
- **family-scoped variants** (`appliesTo` on `variant.json`), a **prompt-profile** comparability key,
  and an **admin-only variant preview** in the chat.

The roadmap is in `research_report_v5.md`, task directory
`hyvanmielenpelit/MobileGnollHackLogger/2026-10-21/benchmark_run_125_analysis/` in the plans
repository.
