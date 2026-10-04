# Report Packs — AI-Written Documents about One Model

A **report pack** is a set of up to three documents about one model of a Model Comparison, written in
the context of the other models compared with it. The documents are for three different readers: the
model's provider or a manager, AI researchers and model developers, and the Overseer team itself.

The same machinery also writes a run's own **run-completion documents**: the Executive Summary and the
Report for AI Researchers and Developers about one run on its own, with no peers, written once after the
run is scored by the report writer the run names (§ 11).

A **battery result** — one model's composite over the suites of a battery — can be a subject too: of a
pack from a comparison of battery results, and of its own **battery-completion documents**, written once
after the battery run finishes and is analyzed (§ 14).

This document describes the feature for developers: what each document holds, how the figures and the
prose are kept apart, how the prose is validated, what is stored, how a stored document is rendered
at download, how its PDF and Word copies carry charts (§ 13), and how a battery result is a subject
(§ 14). It is the companion to [`ai-benchmark.md`](ai-benchmark.md), which describes the harness,
the run report and Model Comparison. Read that first if you have not.

The one design rule behind everything below: **numbers come from code, words come from the writer, and
rendering involves no AI.** The only model call in the feature is the writer call at generation.

Implementation:

| Concern | Type |
|---|---|
| The stored document and its run fingerprints | `BenchmarkReportDocument`, `BenchmarkReportDocumentRun` (`GnollHackServer.Data/BenchmarkReportDocument.cs`) |
| Fact sheet, content snapshot, writer output, validation notes and DTOs | `Overseer/Models/BenchmarkReportPackModels.cs` |
| The writer's prompts and the repair message | `BenchmarkReportPackPrompt` |
| Parsing the writer's JSON | `BenchmarkReportPackParser` |
| The validation rules and the drop policy | `BenchmarkReportPackValidator` |
| A comparison's identity, and its startup backfill | `BenchmarkReportComparisonKey`, `BenchmarkReportDocumentBackfill` |
| Generation: preparation, the writer call, repair, storage | `BenchmarkReportPackService` |
| The background job and its progress | `BenchmarkReportPackJob`, `BenchmarkReportPackJobManager` |
| A run's run-completion documents: scheduling, the queued job, the run's status | `BenchmarkRunReportDocumentService` (singleton) |
| A battery subject's fact sheet and content | `BenchmarkBatteryReportFacts` |
| A battery run's battery-completion documents | `BenchmarkBatteryReportDocumentService` (singleton), `AdminBenchmarkBatteryReportsController` |
| Deterministic Markdown rendering | `BenchmarkReportPackRenderer` (pure, static), behind `BenchmarkReportRenderService` |
| Chart images on disk: storage, validation, manifest, loading | `BenchmarkReportChartStore` (singleton), `Overseer/Models/BenchmarkReportChartModels.cs` |
| Which figure goes where, and the figure markers | `BenchmarkReportChartPlacement` |
| Endpoints | `AdminBenchmarkReportPacksController` (the write-now endpoint included), `AdminBenchmarkReportDocumentsController` |
| Golden files | `Overseer.Tests/UnitTests/Golden/ReportPack/` |

---

## 1. Purpose and the Three Documents

Step 3 of the Model Comparison wizard, **Reports**, starts a report pack (§ 12). The admin
picks one comparison entry as the **subject** — a run, an analysis group or a battery result (§ 14) —
and a separate **report
writer** model, and chooses which documents to write. The other entries of the comparison are the
subject's **peers**, lettered A, B, C… in quality-rank order.

Any entry that is not **Excluded** can be the subject. A **Degraded** entry is allowed: its degraded axis
is omitted from ranking, and the fact sheet marks the affected facts unavailable with the reason.

| Document | Reader | Disclosure | Skeleton |
|---|---|---|---|
| **Executive Summary** | A non-specialist at the model's provider, or a manager. Plain language, short sentences, no jargon | Summary or Full (§ 6) | Headline (one sentence) · key figures (with peers, the *Intelligence* line carries the rank) · *How it compares* (peers only: a code-rendered table of every entry's Intelligence Index with its interval, median answer time, cost per question and critical errors, a compact table of the subject's dimensions against the peer mean, any charts (§ 13), then a paragraph of at most 70 words on where the model stands and whether that position is established) · *What it did well* (at most 3, each at most 30 words) · *Where it fell short* (at most 3, each at most 30 words) · *What this means for use as a game assistant* (at most 90 words) · *How reliable this result is* (at most 60 words, then the code-rendered interval sentence and, with peers, the one sentence on why no pair is tested for significance) · *Evaluation terms* |
| **Report for AI Researchers and Developers** (stored as `TechnicalReport`) | AI researchers and model developers. Precise and neutral | Any level | Headline · abstract (at most 150 words) · *Setup and method*, with a *Compared models* table when there are peers (model, provider, kind, runs, thinking level, harness version, run dates) · figures against the peers, with each peer's *Paired difference* · *Speed and cost* · *Why it scored this way* (the patterns and causes behind the weaknesses, by category, at most 300 words) then *Weaknesses* · *What worked well* (the patterns behind the strengths, at most 150 words) then *Strengths* (at most 8 of each) · question topics for every question · a note for each question more than 15 points below the peer mean or with a critical error (with no peers: scoring below 50 or with a critical error) · at most 6 recommendations for the model's next iteration — only what a model developer can change in the model — each naming the change proposed and, in a few words, the weakness it answers, with its evidence · *Threats to validity*, ending in the writer's *limitations* paragraph (at most 120 words) · *Evaluation terms* |
| **Internal Improvement Brief** | The Overseer team and its AI agents. Direct and practical | **Full only**; internal | Headline · three parts in the order of *What the Benchmark Is For*: the Overseer chat and its tools (at most 200 words), the benchmarking system (at most 150), the model's result (at most 150, then its key figures, the code-rendered interval sentence and any charts) · strengths and weaknesses (at most 8 of each) · question topics and notes · at most 8 recommendations for the chat, the benchmark or model developers · at most 6 **leads** · the fact sheet as JSON (in the Markdown and HTML copies; the PDF and Word copies say *"The fact sheet is in the Markdown copy of this document."*) |

The writer fills named **slots** and **lists** only; the renderer supplies every heading, table and
figure. The slot ids are in `BenchmarkReportSlots`: `comparison`, `meaning` and `confidence` (Executive
Summary); `abstract`, `whyItScored`, `whatWorked` and `limitations` (Report for AI Researchers and
Developers); `overseerChat`, `benchmarkSystem` and `modelResult` (Internal Brief). The slots are
**peer-aware**: `comparison` is required only when the fact sheet has peers
(`BenchmarkReportAudienceSpec.PeerOnlySlots`, `SlotsFor(hasPeers)`), and a stand-alone reply that
supplies it has the extra slot dropped with a rule 1 note. `limitations` is required in both forms: it
names limitations of this data that the code-rendered *Threats to validity* lines do not already state —
a degraded peer, a single-run subject or peer, heavy grader disagreement on particular questions, a band
with few questions — and the prompt lists those lines so the writer does not restate them. The *Why it scored this way* categories are domain knowledge, reading
the game state, tool use, instruction following, completeness under the concise answer style, and
calibration; a category the data does not support is left out. The renderer prints the weaknesses list
right after the `whyItScored` text under `### Weaknesses`, and the strengths list after `whatWorked`
under `### Strengths`, so the writer is told to explain patterns and causes there and not to restate the
list items.

The prose is written once and must be safe at every disclosure level: the same stored output renders
for the provider, with questions described rather than quoted and peers anonymized, and for the team at
Full. The writer is told to write in US English (*color, behavior, analyze, center, gray, labeled,
canceled*), and rule 12 (§ 3) checks it.

**The name of audience 2.** The document for AI researchers and model developers is shown everywhere —
dialogs, the Download Center, the rendered title, the PDF and Word metadata — as **Report for AI
Researchers and Developers**. Its stored type is still `BenchmarkReportAudience.TechnicalReport` (2), and
a document stored under the earlier name *Technical Report* is relabeled on display
(`BenchmarkReportRenderService.CurrentTitle`); its file names use the label `Researcher_Report` (§ 9).

**The stand-alone (peerless) form.** A comparison with only the subject — every run-completion document
(§ 11) and every battery-completion document (§ 14) — has no peers. The fact sheet then marks every fact that compares the subject with peers
unavailable with the reason *"A stand-alone run report has no peers."*
(`BenchmarkReportFacts.StandaloneReason`); the writer prompt says there are no peers, that `{{peer:X}}`
tokens are unavailable and that the subject is never compared with other models; and a question needs a
note when it scored below 50 (`BenchmarkReportPackPrompt.StandaloneNoteScore`) or carried a critical
error, in place of the peer gap. The renderer prints *Peers: none; this is a stand-alone report* in the
header, a results table of the subject's own dimensions and bands with no peer column, question tables
without *Peer mean* and *Difference*, a comparability line that describes the model on its own, and
no pairwise-significance statement.

**Evidence lines.** Under every strength, weakness and recommendation of the Report for AI Researchers
and Developers at **Detailed and Full**, the renderer — never the writer — prints **one** compact,
indented *Evidence:* line, its parts joined by ` · ` and each present only where it applies (at Summary
the item keeps its support label and prints no evidence line):

- the **finding rows** (`R3`) the item cites: each row's support label (§ 2) and category, and, for a
  group subject, in how many of its runs it recurred;
- the **facts** the item cites, by their human label (`BenchmarkReportFactLabels`) and display value, or
  *not available*;
- the **questions** the item is about — its own question list and the `Q` ids it cites, or, when it
  names none itself, the questions of the rows it cites — each with its score, *Q6 (59 / 100)*; at most
  six, the lowest scores first for a weakness or a recommendation and the highest first for a
  strength, then *"and N more"*;
- the **claim verifier**: *"the claim verifier refuted an answer sentence on Q6"* when a ruling on the
  answer's own text among those questions was refuted (a document stored without ruling roles reads
  *a claim*).

For example *"Both graders — accuracy · Q6 (59 / 100), Q10 (57 / 100) · the claim verifier refuted an
answer sentence on Q6"*. No question text, answer or grader note is expanded under an item at any
disclosure: the grader notes appear once, under *Question details*, at Full. The Executive Summary and
the Internal Improvement Brief print no evidence lines; the Executive Summary prints each item's support
label in plain words instead (§ 2). Rule 9 (§ 3) still binds the writer's prose; the quoted evidence is
content, not prose.

**Evaluation terms.** The Executive Summary and the Report for AI Researchers and Developers end, after
*Removed content* and before the footer, with an *Evaluation terms* block: the subject runs' purpose
statements (`PurposeStatementUsed`, deduplicated, carried in the fact sheet's `purposeStatements`); the
distillation and training prohibition sentence as the run report prints it; and a third-party model
content sentence naming the subject and its provider, the graders' providers and the writer and its
provider. The subject is always named; under anonymized peer naming, a grader provider that is also a
peer's provider (and not the subject's) reads *"a withheld provider"*. The Internal Improvement Brief has
no such block.

**Leads** (Internal Brief only) are things worth checking, each tagged `harness`, `suite`, `chat` or
`corpus`. They are provisional, un-triaged inputs written by an AI from computed figures, never
findings: each goes through the triage, evidence bar and tool-layer diagnostics of the
`server_benchmark_to_chat_transfer` skill before anything is changed.

---

## 2. The Fact Sheet and the Token Rules

Every figure in a document comes from the **fact sheet** (`BenchmarkReportFactSheet`, stored as
`FactsJson`), computed by code from the comparison: the subject's and peers' indices and intervals,
speed and cost, grader agreement, per-question scores and differences from the peer mean, the graders,
and the **finding rows** of the runs' final syntheses (ids `R1`, `R2`…), each with its kind, category,
questions and agreement state. An unavailable fact keeps its key, so the writer and the renderer can say
why it is missing.

The writer never writes a figure. It references:

| Token | Meaning |
|---|---|
| `{{key}}` | A fact, printed as the fact's display value |
| `{{subject}}` | The model under report |
| `{{peer:B}}` | A peer, by its letter; printed as the peer's name or *Model B* depending on peer naming |
| `Q7` | A question, by its number — the only place a digit may appear in prose |
| `R3` | A finding row, in an item's `evidence` list only; the prose describes the finding instead |

Counts in prose are number words. Interval overlap is stated descriptively — *"its 95 % interval
overlaps those of Models B and C"*. The comparison runs **no significance test**. A document with peers
says so once, in its own words, where the comparison left pairwise significance out: *"No pair of models
is tested for significance: with N models, testing every pair would flag chance differences."* (*"The two
models are not tested for significance, so a gap between them may be noise."* for two models), in the
Executive Summary's *How reliable this result is* and in the researcher report's *Quality* block. The
writer prompt carries the statement as its *NO SIGNIFICANCE TEST* block.

**The documents own this statement.** The sheet's `NoSignificanceSummary` and `NoSignificanceInstead`
come from `BenchmarkReportFacts.NoSignificanceStatement`, whose wording is frozen in
`BenchmarkReportFacts` (`NoSignificanceSummaryOfTwo`, `NoSignificanceInsteadText`). They no longer
copy the comparison's *Pairwise significance* excluded measure, whose text now tells the wizard's
operator that the charts and table carry no test and points to the wizard's **Paired tests** view
(`ai-benchmark.md`, *Paired Tests*). Decoupling them kept every golden render and stored sheet where
it was. The *Instead* sentence is kept on the sheet and is never printed or shown to the writer.
**Documents do not yet cite the paired tests**: a Report Pack or run-completion document still states
that no pair is tested, even where the wizard has tested one. Having documents cite the
family-adjusted tests is a follow-up.

**Per-peer facts.** Each peer `X` has its own facts, so the writer can say how the subject compares with
it: `peer.X.quality.index`, `peer.X.quality.interval`, `peer.X.quality.rank`,
`peer.X.speed.medianSeconds`, `peer.X.cost.perQuestion`, `peer.X.runs` (*"3 runs"*) and the boolean
`peer.X.intervalOverlap` (the peer's 95 % interval overlaps the subject's). A fact on an axis the peer is
degraded on is unavailable with the entry's explanation. The prompt's PEERS block lists each peer's fact
keys and its explanation as data, and SUBJECT carries the subject's own explanation. Their labels read
*Model X's Intelligence Index* and so on; the label never names the peer, and the renderer puts the
name in when peers are named.

**Paired differences.** For each peer, the fact sheet's `pairedDifferences` list
(`BenchmarkReportPairedDifference`: `peerLetter`, `sharedQuestions`, `meanDifference`, `lower`, `upper`, in
letter order) holds the mean per-question difference, subject minus peer, over the questions both scored
on the same item revision — the subject's per-question score (a mean over runs for a group) against the
peer's own per-question mean. Its 95 % interval is a paired bootstrap of 10,000 resamples of that
question list (`BenchmarkReportFacts.PairedBootstrap`), seeded from the first eight hex characters of the
comparison's key (§ 5), each peer from a fresh generator, so the same comparison always prints the same
interval. The facts are `peer.X.pairedDifference` (*"+4.2 points"*), `peer.X.pairedInterval` (*"-1.3 to
+9.8"*) and `peer.X.sharedQuestions`; below five shared questions
(`BenchmarkReportFacts.PairedMinimumQuestions`) all three are unavailable with the reason. The figure is
an estimate: it reflects question sampling only, is not adjusted for comparing several models, and is
**not a significance test** — rule 11 still bans *significant* and its kin, and the renderer prints that
caveat under the table that shows it (§ 7).

`peer.X.pairedExcludesZero` (format 8) is a boolean: true when both bounds of the paired interval, **as
printed** to one decimal, lie on the same side of zero. It displays *the paired interval excludes zero*
or *the paired interval includes zero*, and is unavailable wherever the paired difference is. Where it is
true the Report for AI Researchers and Developers prints, under its *Quality* table, *"On the same
questions the paired difference with {peer} excludes zero (not adjusted for several comparisons)."*, and
the writer may state the paired result for that pair instead of *not established* (§ 3, rule 16).

**Comparison and per-question facts (format 8).** `comparison.peerRuns` states the peers' run counts in
one clause — *"every peer has 1 run"*, or *"peers have 1 to 3 runs"* — and is withheld on a stand-alone
sheet. Each question of the sheet also carries `peerMin`, `peerMax` and `peersAbove`: the lowest and
highest peer score on it, and how many peers scored more than 5 points above the subject on the same item
and revision (`BenchmarkReportFacts.PeerAboveMarginPoints`). The writer prompt shows them on each question
line as *"peers: min 60, max 90, 2 of 4 scored clearly higher"*. The `dimension.<d>.difference` and
`band.<b>.difference` facts keep their unrounded value but display the difference of the two printed
whole numbers, and so does a question's *Difference* column, so every printed row adds up.

**Support labels.** Each strength and weakness is printed with a support label computed by code from the
finding rows it cites, never written by the model:

| Label | When |
|---|---|
| *Both graders* | The cited rows are Convergent between the two panel members |
| *One grader — different provider* | One member found it, from a provider other than the subject's |
| *One grader — same provider as the model* | One member found it, from the subject's own provider |
| *Graders disagree* | The cited rows are Conflicting |
| *Single assessor* | A single-assessor run |
| *Computed*, printed *From per-question results* | The item cites facts or questions only, no finding row |

The Executive Summary prints the same label in plain words (`BenchmarkReportPackRenderer.PlainSupportLabel`):
*raised by one grader*, *raised by one grader, from the model's own company*, *the graders disagree*,
*raised by the grader* and *from per-question results*. An item both graders agreed on, the ordinary
case, carries no label there (from format 8; the label is chosen at render, so older documents lose
their *both graders agreed* too).

**Fact labels.** A rendered document never prints a raw fact key: every key has a human label in
`BenchmarkReportFactLabels` (*Intelligence Index*, *95 % interval*, *Critical errors*…), used by the
tables, the labeled lines and the evidence lines alike, so one figure never has two names. A key without
an explicit label gets a readable fallback built from its dotted parts.

**Grader vocabulary.** The writer calls the graders by one set of role names, in lower case: *panel
member A*, *panel member B*, *the reference reader* and *the claim verifier* (in a single-assessor run,
*the assessor* and *the second reader*); the Executive Summary says *one grader* or *both graders*.

The writer prompt's row list finds the two panel members by the role names the fact sheet writes —
`Panel member A` and `Panel member B` — as well as the legacy `Assessor` and `Co-assessor`, so in a panel
run a row raised by one member only carries the note that it was *raised only by* that member and
whether that member shares the subject's provider.

---

## 3. Validation, Repair and Drops

`BenchmarkReportPackValidator` checks the writer's JSON against nineteen rules. Each failure is a
`BenchmarkReportValidationNote` with its rule number, location (`headline`, `sections.abstract`,
`weaknesses[1]`…) and message. Rule 20 is not a check on the writer: it is the note a battery
subject's preparation records when it left question detail out of the prompt (§ 14).

| # | Rule |
|---|---|
| 1 | The output is JSON of the expected shape, and every required slot is present and non-empty |
| 2 | Every `{{…}}` token exists: a fact key, `{{subject}}` or a known peer letter |
| 3 | No bare digits in prose after masking tokens, question references and known names |
| 4 | Every question number exists, and the question topics cover every question where the document requires them |
| 5 | Every evidence id exists, and every strength, weakness and lead cites at least one — and so does every recommendation of the Report for AI Researchers and Developers |
| 6 | A strength may not cite a weakness row and a weakness may not cite a strength row; a finding citing only Conflicting rows must say the graders disagree |
| 7 | Length limits: headline at most 35 words; abstract at most 150 words; a question topic at most 12 words; in the Executive Summary at most 3 strengths and 3 weaknesses, each at most 30 words, *What this means for use as a game assistant* (`meaning`) at most 90 words, *How reliable this result is* (`confidence`) at most 60 words and the *How it compares* paragraph (`comparison`) at most 70 words; in the Report for AI Researchers and Developers *Why it scored this way* (`whyItScored`) at most 300 words, *What worked well* (`whatWorked`) at most 150 words, `limitations` at most 120 words and at most 6 recommendations; in the Internal Improvement Brief `overseerChat` at most 200 words, `benchmarkSystem` and `modelResult` at most 150 each, at most 8 recommendations and at most 6 leads |
| 8 | No headings, tables or HTML inside any text |
| 9 | No run of 8 or more words shared with any question, rubric, answer or grader-evidence text |
| 10 | No peer names, model ids or providers in prose |
| 11 | No significance language: *significant*, *significantly*, *statistically*, *reliably better* or *worse*, *clearly outperforms* |
| 12 | US English: no word of the fixed British-spelling list (`BenchmarkReportPackValidator.BritishSpellings`: *colour, behaviour, analyse, analysed, organise, recognise, favour, honour, centre, defence, catalogue, programme, grey, travelled, modelling, labelled, cancelled, judgement*), matched as whole words ignoring case, in any prose |
| 13 | In the Executive Summary's `confidence` slot only: the quality interval is not called *narrow*, *narrower*, *wide*, *wider*, *tight*, *tighter* or *broad* (`BenchmarkReportPackValidator.IntervalWidthWords`, whole words ignoring case); the code-rendered sentence after the slot states the interval and its span |
| 14 | The headline and the abstract do not mention the claim verifier: *verifier* or *verifiers*, as a whole word ignoring case (`VerifierInSummaryRule`). A claim-verifier ruling is advisory and belongs, attributed, among the weaknesses |
| 15 | Every question the prompt lists under *QUESTIONS NEEDING A NOTE* has a note in `questionNotes` (`MissingQuestionNoteRule`) |
| 16 | A sentence that holds `{{subject}}` and a `{{peer:X}}` token together with a comparative word (`ComparativeWords`: *higher, lower, better, worse, ahead, behind, outperform(s/ed), beat(s), leads, trails*), where `peer.X.intervalOverlap` says the two intervals overlap, also says *overlap* or *not established* (`OverlapHedgeRule`); where that peer's `peer.X.pairedExcludesZero` is true, stating the paired result — *paired* and *excludes zero* in the sentence, or the fact's token — satisfies it as well. Tokens are set aside before the text is split into sentences, so a fact key's dots never end one |
| 17 | No hype or filler words (`HypeWords`: *impressive, remarkable, outstanding, stellar, exceptional, robust, seamless, leverage, delve, game-changing, cutting-edge*), as whole words ignoring case, in any prose (`HypeWordRule`) |
| 18 | A `model_developers` recommendation mentions nothing a model developer cannot change (`OverseerOnlyTerms`: *rubric, retrieval, index, corpus, regression test, system prompt, tool guide, prompt the model, the assistant's prompt, GnollHack*, with their plural and inflected forms, as whole words ignoring case; `ModelDeveloperScopeRule`) |
| 19 | No negation — *no, none, never, without, zero*, ignoring case — among the four words before a token whose display value starts with `0` in the same sentence, as in *"no critical errors across {{errors.critical}}"* reading *"0 of 18 answers"* (format 9). Tokens are set aside before the text is split into sentences |
| 20 | *Not a writer check.* A battery subject's prompt exceeded `Benchmark:ReportPack:BatteryMaxPromptChars`, so the full detail of the questions it names was left out and only their rows were given (`BenchmarkBatteryReportFacts.PromptBudgetRule`, location `prompt`, § 14). Recorded before the writer call, stored with the document and logged as a warning; it neither drops anything nor marks the document *Completed with warnings* |

Rules 2, 3, 8, 9, 10, 11, 12 and 17 apply to every prose string: the headline, each paragraph of each
slot, and the text of every item, topic and note. Rule 13 applies to each paragraph of the Executive
Summary's `confidence` slot, rule 14 to the headline and each paragraph of the abstract, rules 16 and 19
to each sentence of the prose, and rule 18 to the text of each recommendation for `model_developers`.

**Readability.** The system prompt of every audience adds: one idea per sentence; sentences of at most
about 25 words; active voice; a count from the facts rather than *many* or *several*; the category of a
finding named rather than *"issues across many topics"*; and no hype words (the rule 17 list). The
Executive Summary keeps its plain-language rule, and the Internal Brief adds *"Lead with the action,
then the evidence."*

**Repair and drop policy.**

1. When any rule fails, the writer gets **one repair turn**: every issue, then the output rules in brief
   (`BenchmarkReportPackPrompt.BuildRepairMessage`).
2. What still fails after the repair is **dropped** — the item or paragraph is removed from the stored
   output, the note records `Dropped`, and the document is stored as **CompletedWithWarnings**. An
   over-cap slot with a word limit (the abstract, `meaning`, `confidence`, `comparison`, `whyItScored`,
   `whatWorked`, `limitations`, `overseerChat`, `benchmarkSystem`, `modelResult`) loses its last
   paragraphs until it fits.
3. **Rules 12 to 19 never drop** (`BenchmarkReportPackValidator.IsWarningRule`); a zero-count token
   after a negation (rule 19) is kept after the repair turn like the others. A missing question
   note (rule 15) has nothing to drop. A spelling slip, an interval adjective, a mention of the claim
   verifier, an unhedged comparison across overlapping intervals, a hype word or a model-developer
   recommendation that strays into the Overseer is not worth losing a finding or the one paragraph of a
   required slot: after the repair turn, text that still uses a British spelling, calls the interval
   narrow or wide, names the verifier in the headline or the abstract, ranks the subject against an
   overlapping peer without saying so, uses a hype word or recommends to model developers a change to a
   rubric, the retrieval or the corpus is
   **kept**, its note is recorded without `Dropped`, and the document is stored as
   **CompletedWithWarnings**. The
   Executive Summary states the interval's span in a code-rendered sentence whatever the writer wrote
   (§ 6).
4. A missing headline or an empty required slot cannot be dropped around: the **document fails** and no
   row is stored. A headline whose only fault is rule 12 is kept.

The wizard's Reports step marks such a document *Completed with warnings*, and the notes are stored with
it (`ValidationNotesJson`, returned by the detail endpoint, § 9), so a dropped item is never silent.

**Writer rules added in format 8** (the R numbers of § 7), in the system prompt of every audience:

- **Peer-aware triage (R13).** Each question line carries the peers' spread (§ 2). Where most peers
  answered a question well and the subject missed it, that is evidence about the model; where every
  model missed it, the writer suspects the chat, its tools, the corpus or the rubric first. The slots
  `overseerChat`, `benchmarkSystem` and `whyItScored` and the leads are told to apply it, and strengths
  and weaknesses prefer points where the subject differs from its peers.
- **Model-developer recommendations (R12)** concern only the model — its knowledge, calibration,
  instruction following, verbosity and tool-use habits — never the Overseer's prompts, tools, retrieval,
  corpus, rubrics or tests. Rule 18 checks it.
- **Paired results (R14).** Where a peer's `pairedExcludesZero` is true, the writer says that on the
  same questions the higher-scoring model scored higher on average and that the paired interval excludes
  zero, not adjusted for comparing several models, and never *not established* for that pair; *significant*
  and its kin stay banned.
- **Unavailable figures and fact keys (R15).** An unavailable figure is mentioned only where leaving it
  out would mislead, in plain words and never estimated; a fact key never appears outside its token, and
  the prose never describes the facts list or the fact sheet.
- **Shared values (R16).** A value several peers share is stated once, for all of them.
- The `modelResult` slot is told that code appends the interval sentence after it (§ 1) and not to
  restate it (R11).

---

## 4. The Writer Model

**Rules enforced by the server.**

- **The model under report is refused as its own writer** — the same provider and model id (400).
- **A writer from the subject's provider** triggers a warning that must be acknowledged: the Reports
  step shows it in amber, and **Generate** then asks a nested *Same-Provider Report Writer* confirmation
  (**Write Anyway**) on every write (§ 12) before sending `acknowledgeSameProvider`; without it the start
  answers 409 with the warning. The acknowledgment is stored on each document as
  `SameProviderAcknowledged` and is never remembered.
- The writer must be an enabled Benchmark-role configuration with a key, and allowed by the endpoint
  policy.
- **Run-completion documents follow the same rule** (§ 11): the model under test itself is refused
  (400, *"The model under test cannot write its own reports."*), and a writer from the candidate's
  provider is allowed after a warning and an explicit confirmation — in the launcher and in the run
  report's **AI Reports** tab. Without the acknowledgment the server answers 409 with a
  `SameProviderWarningDto` of role `reportWriter`. The acknowledgment is recorded on each document it
  produced as `SameProviderAcknowledged` and is never kept as a preference.

**Recommended setting.** A **strong model — the tier recommended for scoring roles, Claude Opus or GPT
Sol, not the provider's top tier (Claude Fable, GPT Astra)** — **from a family other than the subject's**,
at **medium** thinking or reasoning effort. Use **high** only if its documents often need the repair turn
or lose items to validation; never **low**, because the writer must follow a strict schema and token
rules. Not an economy tier (Flash, Flash-Lite): the documents go to people outside the team, and one call
per document keeps the cost small. The same recommendation is in `ai-benchmark.md` § 3 *Choosing grader
models and effort*, the *How the graders work* guide, the Reports step's writer info tip and the
report-writer info tip of the run report's **AI Reports** tab.

**Per document.** The two documents a run or a pack writes most often ask different things of the
writer, and each can be written by a different model:

- **Executive Summary** — short (about 2,400 output tokens) and plain-language, for decision-makers;
  clear, careful wording matters more than depth. A strong writing model — Claude Opus or GPT Sol — at
  medium effort; it is cheap even with a strong model.
- **Report for AI Researchers and Developers** — long (about 7,400 output tokens) and number-dense, and it
  must keep every figure exact and follow a strict schema. The strongest scoring-tier reasoning model you
  trust with numbers — Claude Opus or GPT Sol — at medium effort, high if its documents often need the
  repair turn. It costs roughly three to four times the summary.
- **Both** — prefer a writer from another provider than the model under test (a same-provider writer is
  allowed after a warning), and, where the roster allows, one that shares a family with neither panel
  member either; avoid the economy tiers (Flash, Flash-Lite) and the top tiers (Claude Fable, GPT Astra).
  For a run, two documents with two writers are two rounds: write one, then choose another writer for the
  other.

**Usage and guards.** Each writer call is recorded in `SystemAiUsageLog` with `RoleContext = 8`
(Report Pack), run-completion documents included. While a job runs, the writer configuration cannot be
deleted: the usage guard reports a blocker of kind `reportPackJob`, and of kind `runReportWriter` for a
configuration named as the report writer of a run whose documents are Pending or Writing. The start is
refused with 429 when `BenchmarkComplianceGuard.CanSpendAsync` denies it, and with 409 while another
report-pack job runs or a run-completion job waits for the slot — one job at a time.

---

## 5. Storage and the Scoring Fingerprint

Each document is one **immutable** `BenchmarkReportDocument` row; there is no update endpoint. It holds:

- audience, subject key (`run:<id>`, `group:<id>` or `battery:<id>`) and label, the subject's run ids
  (for a battery, its usable member runs), the comparison request, and the suite;
- `Origin` (`BenchmarkReportDocumentOrigin`): **ReportPack** (1, the default, and the value every row
  written before the column existed carries) for a document of a Report Pack job, **RunCompletion** (2)
  for a run's own run-completion document (§ 11), **BatteryCompletion** (3) for a battery run's own
  battery-completion document (§ 14; the column is an `int`, so the value needed no schema change). An
  index on `(SubjectKey, Origin)` finds a run's or battery run's own documents, and the list DTO carries
  `origin`;
- the writer's identity and its configuration snapshot, and `SameProviderAcknowledged`;
- `ReportFormatVersion`, the writer prompt's SHA-256 and `AnswerExcerptChars`;
- `FactsJson` — the fact sheet;
- `ContentJson` — the verbatim content the renderer may print: question text as asked, rubric as
  graded, answer excerpts cut at generation, the complete answer (`answerText`) wherever the excerpt was
  cut (from format version 5, § 7), grader evidence and verifier rulings (each with its role from format
  version 3), all taken from the subject's **answer rows**, never from the live suite, so a later suite
  edit cannot change a document. The writer sees only the excerpts;
- `WriterOutputJson` — the final validated writer output, with dropped items already removed;
- `ValidationNotesJson`, status, tokens, duration and cost;
- `ComparisonKey` — the identity of the comparison the document was written for: the lower-case hex
  SHA-256 of `runs=<ids>;groups=<ids>`, each list sorted, distinct and comma-joined, over the comparison
  request's `RunIds` and `GroupIds`, with `;batteries=<ids>` appended only when the request names
  battery results, so every earlier key is unchanged (`BenchmarkReportComparisonKey`, the only
  implementation). Documents
  of the same **set of entries** share it whatever their subject; the pricing basis is not part of it, so
  changing *Prices* in the wizard keeps the same documents listed. A run-completion document carries its
  one run's key. An index on `(ComparisonKey, Origin, CreatedAtUtc)` serves the list.

The child table **`BenchmarkReportDocumentRuns (DocumentId, RunId)`** stores each subject run's
**scoring fingerprint at generation**: `FinalScore`, `QualityIndex`, `SpeedIndex`,
`ScoringMethodVersion`, `RerunCompletedAtUtc`, and the first 16 hex characters of SHA-256 over
`AssessmentJson + "\n" + CoAssessorSynthesisJson`. List responses compare it with the run as it is now
and flag *Run changed since this document was written* (`runChangedSinceGeneration`) when a run was
re-scored or re-run afterwards.

Each **peer** run gets a row too, with `IsPeer = true` and the same fingerprint fields. A run is never
both subject and peer of one document; if it were, the subject row would win. List responses compare
the peer rows the same way and flag *Comparison changed* (`peersChangedSinceGeneration`) when a peer run
was re-scored, re-run or deleted. `runChangedSinceGeneration` and `missingRunIds` look at subject rows
only, and so does the list's `runId` filter (§ 9), so a run's report lists exactly its own documents.

**Rows written before these columns existed.** At startup, `BenchmarkReportDocumentBackfill` gives every
row with no `ComparisonKey` one derived from its stored `ComparisonRequestJson`, in batches of 200,
beside the run-completion settlement in `Program.cs`; a row it cannot read is logged and stays null, and
lists only where no comparison filter applies. The backfill is idempotent. Those rows have **no peer
rows** — their peers' fingerprints were never stored — so they are never flagged *Comparison changed*.

There is **no foreign key to runs**: deleting a run keeps its documents. Deleting a document cascades to
its child rows and removes its chart folder (§ 13).

A document's **chart images are not in the row**. They are PNG files in a folder of their own on disk
(§ 13), the only part of a document that is not immutable: they can be added, replaced or removed at
any time without touching the row.

---

## 6. Disclosure and Peer Naming

Both are chosen **at download, per document**; the stored row is the same whatever is chosen.

| Level | Question text | Rubric | Model's answer | Grader evidence | Stamp |
|---|---|---|---|---|---|
| **Summary** | Topic only | Never | Never | Never | *Confidential. Prepared for the model's provider. Questions are described, not quoted.* |
| **Detailed** | Verbatim, every question | Never | Excerpts, every question | Never | *Confidential. Prepared for the model's provider. Contains benchmark questions — do not publish.* |
| **Full** | Verbatim, every question | Verbatim | Complete (the stored excerpt on a document written before format version 5) | Verbatim | *INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.* |

The stamps are audience-aware (`BenchmarkReportPackRenderer.Stamp(audience, disclosure)`). The Executive
Summary quotes no question or rubric at any level, so its Detailed stamp reads *Confidential. Prepared for
the model's provider. Review before sharing.* and its Full stamp *INTERNAL — unpublished benchmark
results. Do not share outside the Overseer team.*; its Summary stamp is the one above. The PDF and Word
classification banners use the same text.

In the Report for AI Researchers and Developers, Summary prints each finding's support label but no
evidence line (§ 1). Detailed adds the evidence lines and prints every question and each run's answer
excerpt in one *Questions and answers* section; Full prints the same section as *Question details*, with
each run's **complete answer** under *Answer:*, each question's rubric, the graders' scores and notes and
the claim verifier's rulings added. An answer short enough to be stored whole prints as its excerpt,
which is the whole answer; a cut excerpt on a document stored before complete answers were kept prints
under *Answer excerpt:* with the note *"This document predates complete-answer capture; the answer is
shown as the excerpt stored when it was written."* The notes on individual questions never quote them at
any level: the section does. The size guard (`BenchmarkPdfRenderer.MaxSourceCharacters`, shared by Word)
still applies to the rendered Markdown.

At every level the *Tool-use behavior* section says that a recorded success or failure describes only
whether a tool call executed — not whether the query was well chosen, the result relevant or the corpus
current. At Full it adds where the per-call detail is: each run's Tool-call log, until the retention sweep
prunes it, when the runs recorded per-call rows (`tools.failed` is available, harness 17 and later), or
that the runs did not record per-call arguments or results.

**Writer independence.** When the stored writer provider equals the subject's provider (trimmed,
case-insensitive, as `BenchmarkComplianceGuard.IsSameProvider` compares), the Executive Summary's *How
reliable this result is* and the technical report's *Threats to validity* print a code-rendered caveat
naming the writer and the shared provider, whatever the writer returned. The Internal Improvement Brief
does not print it. The Executive Summary's reliability section also prints *"The 95 % interval is 73–82,
a span of 9 points, and rests on 4 of 4 questions with a scored answer."* from the `quality.interval`,
`quality.intervalSpan` and `quality.scoredItems` facts; a document stored without the span fact, or whose
reliability paragraph already places `{{quality.interval}}`, prints nothing there.

**Peer naming** is *Named* or *Anonymized*. Anonymized prints peers as *Model A*, *Model B*…, removes the
provider column, and replaces the peers' model ids and providers in every table. A grader whose provider
is withheld (a peer's provider that is not the subject's, as in *Evaluation terms*, § 1) reads *a model
from a withheld provider* in place of its name and model id, in *Setup and method*, the *Reproducibility
appendix* and the full question details. Named prints the peers' names wherever a fact states them by
letter — the interval-overlap figure (*"overlaps every peer's"*) and *Judge-dependent pairs*. The subject
is always named.

The Report for AI Researchers and Developers renders at any level with either naming. The Executive
Summary is **offered** at Summary and Full only (`BenchmarkReportPackRenderer.AllowedDisclosures`, which
fills each list item's `allowedDisclosures`), since its Detailed text is its Summary text; it still
**renders** at Detailed (`IsAllowed`), with its own stamp, so older links keep working. The Internal Brief
renders at **Full only**; any other combination answers 400.

Why provider copies default to Summary with anonymized peers is recorded in `ai-benchmark.md` § 7
*Sharing Reports with AI Providers*.

---

## 7. Deterministic Rendering

Every download renders the stored row at the chosen disclosure and peer naming with
`BenchmarkReportPackRenderer`, a pure static renderer. `BenchmarkReportRenderService` loads the row and
calls it; its constructor takes only the DbContext, the chart store (§ 13) and a logger, and the
documents controller's constructor takes only that service. Architecture tests pin both, so no model
client, clock or configuration can reach the render path; the chart store reads its one setting,
`ChartsDataLocation`, when it is created, and holds no model client.

The guarantees:

- no clock: only the stored creation time prints;
- invariant culture for every number and date;
- explicit ordering everywhere: slots in the audience's slot order, never dictionary order;
- `\n` line endings and UTF-8 without a BOM.

**Golden files.** `Overseer.Tests/UnitTests/Golden/ReportPack/` holds the expected output for fixed
inputs; they are LF, pinned by the `.gitattributes` rule `Overseer.Tests/UnitTests/Golden/** text eol=lf`.
A golden test fails on any change to the renderer's output.

> **Any renderer output change must bump `BenchmarkReportPackRenderer.ReportFormatVersion` in the same
> commit as the updated golden files.** A stored document records the version it was generated under;
> the shapes stored as JSON are part of the format too, so renaming one of their properties is a format
> change as well.

**Format version 2** added the *Evidence* lines, the *Evaluation terms* block and the stand-alone form
(§ 1), and the label *Report for AI Researchers and Developers*; the fact sheet gained
`purposeStatements`. The Internal Improvement Brief changes only its format version and the embedded
JSON. Format 1 had none of these.

**Format version 10** (the current one, 2026-10-03, with harness 46 in `ai-benchmark.md`) comes from the
battery run 1 analysis (runs 76 and 77). It changes no score, index, grading prompt, comparability key or
`HarnessVersion`; stored documents re-render with the new renderer on their next download.

- **Battery tool outcomes (H2)**: a battery fact sheet's `tools.failed` and `tools.refusedByBudget` are
  counted over the usable members' index-counting answers' per-call tool rows, classified by the run-level
  rule (`BenchmarkToolCallRecorder.Outcomes`) through a projection that never loads `ArgsText` or
  `Result` (`BenchmarkBatteryAnswerOutcomes.LoadAsync`). They stay unavailable, with the run-level reason,
  when a member predates per-call tool records (harness 17). Format 9 withheld both on every battery.
- **Empty band scores (H3)**: both difficulty-band tables, with peers and stand-alone, leave out the model,
  peer-mean and difference columns when no `band.*.score` is available, instead of a column of dashes.
- **Refuted sentences (H4)**: the battery fact `claims.refutedAnswerSentences`, *Refuted answer sentences,
  accused sentences included*, sums the per-question refuted answer sentences; `claims.refuted` is
  labelled *Claims the verifier refuted (the answers' own claims)*, and the battery prompt tells the writer
  how the two differ.
- **Client-aborted preparation**: an estimate or preview the client abandons answers `499` instead of
  raising an unhandled `TaskCanceledException`, and the run report dialog asks for an estimate only while
  its AI Reports tab is shown.

**Format version 9** (2026-09-30, with harness 44 in `ai-benchmark.md`) comes from a
review of run 75's run-completion documents. It changes no score, index, grading prompt, comparability
key or `HarnessVersion`. Stored documents re-render with the new renderer on their next download
(*generated under 8 · rendered with 9*); the prompt changes reach only documents written from now on.

- **Assessed and authored bands (N1, R4)**: *Difficulty bands* is *Difficulty bands (assessed)* with an
  *Authored questions* column, and the per-question table's *Band* column is *Assessed band*, followed by
  *Authored*. The facts `bands.authored.simple|intermediate|advanced` (*Questions authored as …*) carry
  the authored counts, and the FACTS header tells the writer the bands are assessed difficulty, so it no
  longer writes "no simple-band questions" about a suite with authored Simple items.
- **Question details last (N2)**: in the Report for AI Researchers and Developers, `## Question details`
  (one `### Q<n>` per question, `#### Run` per run) follows the *Reproducibility appendix* and precedes
  *Removed content* and *Evaluation terms*; *Per-question results* (the table and its note) stays where
  it was, and the table of contents follows. The Internal Improvement Brief keeps its details under
  section 5.
- **Mean time (N4)**: the fact `speed.modelTimeMean` (*Mean answer time*, from
  `BenchmarkModelComparisonSpeedDto.ModelTimeMeanMs`, formatted like the median); *Key figures* reads
  *"median answer time 14.7 s, mean 17.2 s"* and *Speed and cost* adds *Mean answer time*.
- **Zero-count tokens (R1)**: the fact `answers.scored` (*Scored answers*, the denominator of
  `errors.critical`), a writer rule that an *N of M* token is a noun phrase — never after *no* or the
  object of *made*; to say none occurred, *"no critical errors across all {{answers.scored}} answers"* —
  and validator rule 19 (§ 3).
- **Model-developer recommendations (R2)** name a general capability a model developer can train or
  tune — stating the decisive mechanic behind a verdict, committing to a conclusion the inputs settle —
  never a GnollHack fact, a change to the assistant's prompt or tools, or a rubric point; game-specific
  gaps are Internal Brief leads (`corpus` or `chat`). It replaces format 8's R12 wording, and rule 18's
  vocabulary grows (§ 3).
- **Support labels (H3)**: a finding row carries `questionsA`, `questionsB` and `sharedQuestions` (the
  FINDING ROWS print *shared* / *A only* / *B only*). A strength or weakness citing a Convergent row
  reads *Both graders* only when its questions meet the row's shared questions; otherwise *One grader*
  with that member's provider relation. A run-wide Convergent row, and a row stored before format 9
  without the lists, keep the row's label.
- **PDF layout 4 (N3)**: the closing section is kept on one page (§ 8).

A document stored under format 8 or earlier renders every change above that needs no new fact: the
authored columns and the mean print *—* or nothing without their facts, and its support labels are the
row's.

**Format version 8** (2026-09-30) comes from a review of the first comparison
documents, and adds charts to the PDF and Word copies (§ 13). It changes no score, index, grading
prompt, comparability key or `HarnessVersion`: the report writer is not part of the graded instrument,
and no comparability code reads `WriterPromptSha256` or the format and layout versions. Stored documents
re-render with the new renderer on their next download; the prompt changes reach only documents written
from now on.

- **Significance, once (R1)**: one document-worded sentence (§ 2), in the Executive Summary's *How
  reliable this result is* and the researcher report's *Quality* block; the *Significance* line of
  *Threats to validity* is gone, and the comparison's *Instead* instruction is never printed.
- **Named peers (R2)**: a Named copy names the peers in the interval-overlap figure and in *Judge-dependent
  pairs* instead of giving their letters (§ 6).
- **Whole-number differences (R3)**: every *Difference* — dimension, band and question — is the
  difference of the two whole numbers printed beside it (§ 2).
- **Note heading (R4)**: *"Questions more than 15 points below the peer mean, or with a critical error"*.
- **Evidence lines (R5)**: at most six questions each, weakest or strongest first, then *"and N more"*
  (§ 1).
- **Cover (R6)**: with peers, the PDF and Word cover's subject line reads *"{Suite} · run #68 · compared
  with 4 models"* (*group #N* for a group subject), and its facts table replaces the *Peers* row with
  *Compared with* — the peers' labels and *(4 models)* in a Named copy, *"4 models (A to D), identities
  withheld"* in an Anonymized one — and *Pricing basis*. The Markdown front matter lists *Compared with*
  and *Pricing basis* in place of *Peers*. A stand-alone document's cover is unchanged (§ 8).
- **File names (R7)**: `vs-<N>-models_` after `run-<id>_`, or first for a group subject, when the
  document has peers (§ 8).
- **Fact sheet in Markdown only (R8)**: the PDF and Word copies of the Internal Improvement Brief end
  section 6 with *"The fact sheet is in the Markdown copy of this document."*
  (`BenchmarkReportRenderOptions.IncludeFactSheet = false` for native renders); the Markdown copy keeps
  the JSON.
- **No Rank line (R9)**: the Executive Summary's separate *Rank* line of format 7 is gone; the rank is
  on the *Intelligence* line.
- **How it compares (R10)**: the table gains a *Critical errors* column and is followed by a compact
  *Dimensions* table (subject, peer mean, difference); the Executive Summary's items no longer print
  *(both graders agreed)* (§ 2).
- **Internal Brief figures (R11)**: section 3 prints the interval sentence after *Key figures*.
- **Paired results (R14)**: the `peer.X.pairedExcludesZero` fact and the researcher report's sentence for
  each peer whose paired interval excludes zero (§ 2); validator rule 16 accepts the paired result for
  that peer (§ 3).
- **Withheld graders (R18)**: in an Anonymized copy a grader whose provider is withheld reads *a model
  from a withheld provider* (§ 6).
- **New facts**: `peer.X.pairedExcludesZero`, `comparison.peerRuns`, and the per-question `peerMin`,
  `peerMax` and `peersAbove` (§ 2).
- **Writer prompt**: the per-question peer spread, the peer-aware triage rule and rules R12 to R16 (§ 3);
  validator rule 18, a warning, and the parser's `KnownSlots` gain `comparison` and `limitations`.
- **Charts**: a `[[figure:<key>]]` marker at each anchor where the render is given a chart (§ 13), drawn
  by *PDF layout 3* and *Word layout 2* (§ 8).

A document stored under format 7 or earlier renders with every change above that needs no new fact;
without `pairedExcludesZero` it prints no paired-result sentence, and its prose stays as it was written.

**Format version 7** (2026-09-29) makes a comparison document say how the model compares
with its peers, and gives the writer the peers' own figures to say it with. It changes no score, index,
grading prompt, comparability key or `HarnessVersion`.

- **Per-peer facts and paired differences** (§ 2): the `peer.X.*` facts and the fact sheet's
  `pairedDifferences` list; each entry of `entries` gains `harnessVersion` (*mixed* when its runs differ),
  `firstRunUtc` and `lastRunUtc` (each run's completion time, or its start when none was recorded).
- **Executive Summary**: *Key figures* gains a *Rank* line, *"2 of 3 on intelligence"*, followed by
  *"(the order is not established where intervals overlap)"* when the subject's interval overlaps any
  peer's. A code-rendered section **How it compares** follows *Key figures*: one row per entry — Model,
  Intelligence Index with its interval, Median answer time, Cost per question — the subject in bold, then
  the peers by letter, and under it the writer's `comparison` paragraph. Peer mode only.
- **Report for AI Researchers and Developers**:
  - *Setup and method* gains **Compared models** (peer mode only): Model, Provider, Kind (run or group),
    Runs, Thinking level, Harness version and Run dates (UTC). The stand-alone form keeps its single-run
    setup lines;
  - the *Quality* table gains a **Paired difference** column (*"-4.6 points (-9.8 to +0.7)"*, *not
    available* below the minimum, with the reasons listed under the table) and the code-rendered note
    *"Paired difference: mean per-question difference, subject minus peer, over the questions both
    answered; 95 % paired-bootstrap interval. It reflects question sampling only, is not adjusted for
    comparing several models, and is not a significance test."*;
  - *Threats to validity* ends with the writer's `limitations` paragraph, in both forms.
- **Evidence lines** print a `peer.X.*` fact under its label with the peer's name put in when peers are
  named (`FactLabel`).
- **Writer prompt**: the PEERS block, the subject's explanation, the *NO SIGNIFICANCE TEST* block, the
  readability rules (§ 3), the `comparison` and `limitations` slots and the Internal Brief's word caps.
  Validator rules 15–17 (§ 3). The output-token estimates are 2,400 (Executive Summary), 7,400 (Report for
  AI Researchers and Developers) and 7,000 (Internal Brief).
- **Footer**: from format 7 the checks line names *interval-overlap wording* and *hype words* among the
  automatic checks; an older document keeps the wording of the checks it received.

A document stored under format 6 renders as before: *How it compares* and the *Rank* line appear only when
its sheet has `peer.*` facts, the *Paired difference* column only when a `peer.X.pairedDifference` fact
exists, *Compared models* without the *Harness version* and *Run dates* columns it has no data for, and
the `comparison` and `limitations` paragraphs only when the slot is present.

**Format version 6** (2026-09-29, with harness 43 in `ai-benchmark.md`) makes the
documents read the same figures as the run report and tightens what the writer may say about the claim
verifier:

- **Panel dimensions (R1)**: in a panel entry (every run a panel run) the `dimension.<d>` facts are the
  panel row — the mean of both members' dimension averages, from `BenchmarkPanelDimensions.Averages`, the
  function the run report's *Dimensional Score Averages* table uses — instead of member A's figures, and
  the response-style fact reads the panel averages.
- **One P90 (R2)**: the run report now uses the same interpolated percentile as the `speed.modelTimeP90`
  fact (`BenchmarkGroupStatistics.Percentile`), so the two print the same P90.
- **Refuted answer sentences (R3)** count distinct sentences per answer: rulings on one sentence count
  once under the union manifest's key (`BenchmarkService.ItemKey`, ignoring case), so a sentence both
  accused and quoted as a critical error is one sentence.
- **Ruling labels (R4)** name the side the verifier took: *(the verifier sided with the grader)* or *(the
  verifier sided with the answer)*, instead of *(the grader was wrong)* and its counterpart.
- **Displays (R5)**: the response-style fact reads *"Completeness is the lowest dimension, X points below
  Accuracy"* or *"No response-style conflict"*; its value stays the boolean, and a display stored before
  format 6 loses its leading *yes: * when rendered. `quality.scoredItems` reads *"N of M questions"*.
- **Computed support (R6)**: the stored label *Computed* is printed *From per-question results* (*from
  per-question results* in the Executive Summary's plain words); the stored value is unchanged.
- **Knowledge-base answers (R7)**: `tools.zeroKnowledgeBaseAnswers` is stated only when a question of the
  suite is a knowledge-base topic (`BenchmarkChatTransfer.HasKnowledgeBaseRoutingQuestion`); otherwise it
  is unavailable with the reason *"No question of this suite is a knowledge-base topic; the prompt routes
  game mechanics past the knowledge base."*, and the *Tool-use behavior* section omits the line.
- **Interval sentence (R8)**: the writer prompt's confidence slot says that code appends one sentence
  after the paragraph stating the interval, its span and the questions it rests on, and that none of them
  is to be restated. The renderer skips its computed sentence when a stored document's confidence text
  already places `{{quality.interval}}`, as writers before format 6 were asked to. Rule 13's note no
  longer suggests writing the span.
- **Strength evidence (R9)**: a strength's evidence line lists no refutation; only a weakness's does.
- **PDF keep-with-next (R10)**: a run of consecutive headings is grouped and kept with what follows. Before
  a paragraph the group and the paragraph are one unbreakable block (`PreventPageBreak`); before anything
  else the group gets `EnsureSpace` for its own height plus three body lines
  (`BenchmarkPdfStyle.KeepWithNextHeight`) or, before a table, its header and first row
  (`KeepWithTableHeight`, 72 pt).
- **Findings table (R11)**: at Summary disclosure the *Grader reliability* findings table is replaced by
  the note *"The graders' findings are listed at Detailed and Full disclosure only, since their wording
  may quote the benchmark's questions and answers."* — the finding texts are grader prose that no
  disclosure check scans. At Detailed each row's *Finding* cell reads *"kind · category: text"*, the text
  cut to 160 characters (a Conflicting row gives both members' texts, *A: … B: …*); at Full the cell
  keeps *kind · category* and the whole texts follow the table. The *Recurrence* column is omitted when
  the subject has one run.
- **Reference reader (R12)**: in a panel entry, the facts `panel.referenceReaderIndex` (*Reference reader (advisory, third
  provider)*, *"N / 100"*) and `panel.referenceReaderOffset` (*Reference reader's mean offset from the
  panel*, *"+x.x points"*), each the mean over the subject's runs with a reference reader, print under
  *Grader reliability*, the last of them followed by *"It never scores; its neutrality between the two
  panel families is an assumption."*
- **Claim-verifier rulings (A1)**: a new writer rule — a claim-verifier ruling is an advisory judgment by
  an AI model that is sometimes wrong; attribute it (*"the claim verifier judged …"*), never state it as a
  fact about the game, and never list refuted claims in the abstract or the one-sentence result. The
  abstract's slot job says the same. Validator rule 14 (`VerifierInSummaryRule`, § 3) flags *verifier* in
  the headline or the abstract. The warning rules — 12, 13 and 14 (`IsWarningRule`) — keep their text,
  ask for the repair turn and mark the document CompletedWithWarnings.
- **Response-style note (A2)**: a new writer rule — the response-style note is Overseer's own
  observation, never attributed to a grader.

A document stored under format 5 renders with the new labels and displays; where it lacks a fact (the
reference reader's, say) the line is omitted, and where its confidence paragraph already places the
interval the computed sentence is not added.

**Format version 5** (2026-09-29) comes from a review of the run-73 Executive Summary
and technical report PDFs:

- **Provider wording**: the single-grader support labels read *One grader — different provider* and *One
  grader — same provider as the model* (§ 2); *Setup and method* and *Threats to validity* speak of a
  grader's provider. A sheet stored under format 2–4 is normalized when it is read
  (`BenchmarkReportFacts.NormalizeSupportLabels`, in both the render and the anonymized appendix copy), so
  its items keep their single-grader label rather than falling to *Computed*. The stored property
  `sameFamilyAsSubject` keeps its name, and the Internal Improvement Brief's raw fact-sheet appendix still
  prints it.
- **Writer independence**: a writer sharing the subject's provider gets a code-rendered caveat (§ 6),
  also on re-rendered format 2–4 documents.
- **Interval span**: the new fact `quality.intervalSpan` (the printed upper bound minus the printed lower
  bound, *"9 points"*); the writer prompt's confidence slot names `{{quality.interval}}` and
  `{{quality.intervalSpan}}` instead of asking for the interval's width; validator rule 13 (§ 3); the
  Executive Summary's code-rendered span sentence (§ 6).
- **Disclosure density**: the Report for AI Researchers and Developers prints evidence lines at Detailed
  and Full only.
- **Complete answers**: `ContentJson` keeps `answerText` for every cut excerpt, and Full prints it (§ 6).
- **Tool use**: the execution-only note at every level and the Tool-call log pointer at Full (§ 6).
- **Native framing**: the PDF and Word downloads render with `IncludeDocumentFooter = false` as well as
  `IncludeFrontMatter = false`; their cover gains the writer's provider and model id, a *Generated
  format* row (formerly *Report format*) and a *Provenance* row (§ 8). The Markdown footer reads *format
  version 5* for a current document and *generated under format version N · rendered with format version
  5* for an older one, and its provenance line names the automatic checks and says they do not verify the
  prose's interpretations.

A format 4 document renders without the span sentence and with its excerpts at Full, under the note.
Version-5 rows stay readable by version-4 code, which ignores `answerText`.

**Format version 4** (2026-09-29) makes the disclosure levels differ visibly (§ 6): at
Detailed the Report for AI Researchers and Developers prints every question and answer excerpt in a
*Questions and answers* section, and the quote under each noted question is gone at Detailed and Full,
where the section carries it. Nothing stored changed, so a format 3 document renders the same way.

**Format version 3** (2026-09-29) came from a review of the run-completion documents
written for run 73, which found the evidence repeated under every item, raw fact keys in the text, the
quality index printed as *±* a half-width, refuted grader statements read as errors of the answer, rubric
source notes and cut-off tables leaking into the writer's input, and the slots restating their lists:

- **Evidence**: one compact line per item in the Report for AI Researchers and Developers, none in the
  Executive Summary, which prints plain support labels (§§ 1–2); no question expansion under an item.
- **Labels**: a human label for every fact key (`BenchmarkReportFactLabels`); *Serious errors* became
  *Critical errors*.
- **Researcher report**: *Why it scored this way* then `### Weaknesses`, *What worked well* then
  `### Strengths`, the recommendations as a list only; a **Speed and cost** table (median and
  90th-percentile answer time, cost per question, cost per run, the total cost per run with the graders
  included, input and output tokens per question,
  from the new facts `tokens.inputPerQuestion` and `tokens.outputPerQuestion`), each row only when its
  fact is available.
- **Figures**: the index reads *77 / 100 (interval 73–82)*, never *±* (the `quality.index` fact is the
  point alone, and its interval is rounded as the point is); *How confident are we* became *How reliable
  this result is*; the *About this benchmark* paragraph names a grader from the model under test's
  company; the cost sentence and the appendix's pricing basis follow the new
  `comparison.pricingBasisKind` and `comparison.pricedOn` facts — *Catalog prices on <date> (price card
  dated <date>)* for the catalog basis, the prices stored with each run for the snapshot basis; table
  integers print without *.0*; the Markdown footer begins *Document ID N*.
- **Claim rulings** carry a role (an answer sentence, an answer sentence accused by a grader, a
  critical-error quote, a grader's statement, the basis of an out-of-rubric deduction) and print in words
  with who was right — *Answer sentence accused by a grader — supported (the grader was wrong)*. The
  per-question column *Refuted answer sentences* counts only refuted rulings on the answer's own text
  (the fact sheet's `RefutedAnswerSentences`); a sheet stored without it keeps *Refuted claims*.
- **Writer input**: the rubric's `SOURCE` paragraphs are removed from the prompt; answer excerpts are cut
  at a sentence end or line break and never inside a Markdown table (*"… (a table follows in the
  answer)"*).
- **Writer prompt**: the one grader vocabulary (§ 2); a job for each slot; the reliability slot says when
  a grader shares the subject's provider; a Conflicting row whose two texts are about different things is
  left out; a refuted grader's statement means the grader was wrong; the abstract names every error the
  claim verifier refuted.
- **Front matter**: the PDF and Word downloads render with `IncludeFrontMatter = false`, since their
  cover prints the stamp and the facts; Markdown and HTML keep it (§ 8).

A document stored under format 2 keeps rendering: where it lacks a role, a count or a fact, the renderer
falls back to what format 2 printed. Two observations of that review are deliberately left for a later
benchmark round: on one run-73 question the claim verifier's verdict contradicted its own rationale, and
the synthesis can merge unrelated items into one Conflicting row.

The goldens include two stand-alone ones, `exec_standalone.md` and `technical_standalone.md`, rendered
from the fixture's single-run subject. To regenerate every golden after an intended change, set
`$env:OVERSEER_UPDATE_GOLDENS = '1'` and run the normal test command (`ai-benchmark.md` and the
`testing-guidelines` skill give it), then review each diff before keeping it.

**Configuration**, read only at generation:

| Key | Default | Meaning |
|---|---|---|
| `Benchmark:ReportPack:MaxOutputTokens` | 16000 | The writer call's output limit |
| `Benchmark:ReportPack:AnswerExcerptChars` | 600 | The length answer excerpts are cut to in `ContentJson` |

The excerpt length used is stored on each row, so changing the setting never changes a re-render. The
third setting of the section, `Benchmark:ReportPack:ChartsDataLocation`, is not a generation setting: it
says where chart images are stored (§ 13).

---

## 8. The Download Center

One panel, `app-download-center-panel` (`download-center/download-center-panel.component.*`), packages
documents and run files for download. It is shown in two places: as a dialog —
`app-benchmark-download-center`, a thin wrapper around the panel, reached from the run report dialog's
**Downloads** button and from the Model Comparison launcher's **Open Download Center** (§ 12) — and,
placed directly, as step 4 of the Model Comparison wizard, *Documents*, where it also manages the
comparison's charts (§ 13).

| Package | Contents | Disclosure | Peers | Formats |
|---|---|---|---|---|
| **Internal** | Every available document: pack documents, the run report, the tool-call log, run diagnostics | Full | Named | PDF, Word and Markdown (PDF, Word and Text for the diagnostics) |
| **External** | Executive Summary and Report for AI Researchers and Developers only; internal-only rows are listed but unselectable, with their reason | Summary (Detailed as an option) | Anonymized (Named as an option, with a warning) | PDF |
| **Custom** | Any selection | Per document | Per document | Any, Word included |

The summary line, the ZIP's `MANIFEST.md` and its file name call them *Internal package* and *External package*.
Opened on a run, the dialog lists every document whose subject includes the run, so a run's
run-completion documents (§ 11) appear under both packages beside any Report Pack documents about it;
while they are still being written, a notice says so and the list reloads when they are done (§ 11).

Opened on a list of documents (`DownloadCenterDocumentsContext`, by id) or on a library
(`DownloadCenterLibraryContext`), the panel may take a `title` and a `subtitle` in place of its own. A
library context lists with one request: `scope` is `{ kind: 'comparison', entryKeys }` (this
comparison's Report Pack documents, `comparison=<entry keys>&origin=reportPack`, § 9) or `{ kind: 'all' }`
(every Report Pack document, `origin=reportPack&take=500`), with the reports of their subjects' runs, and
`preselect` is `'all'` (every row starts as the package chooses it) or `'none'` (nothing starts chosen).
The launcher opens it on `all` with nothing preselected and the title *Comparison reports*; the wizard's
step 4 shows `comparison` with every row preselected and the title *Documents of this comparison*.
Packages, disclosure levels, naming, formats and the ZIP are the same in every context.

**The documents list** shows each document as a full-width card (the `frontend_ui_controls` skill
§ 8h). The list's header holds the *Documents* heading, an (i) button **About document options** that
opens one dialog explaining *Sharing*, *Disclosure* (what each level contains, per document type),
*Peer names*, *Formats* and, in the wizard, *Charts*, and a status line such as *Showing 10 of 23
documents* (*· filtered from 40* while a filter is active), which screen readers announce.

Above the cards, a filter bar:

- **Search** matches the title, the subject, the suite and the writer once typing pauses. Escape clears
  the text without closing the dialog; with the field empty, Escape closes it as usual.
- **Sort by** offers *Newest first* (the default), *Oldest first*, *Document type*, *Title*, *Subject*,
  *Suite*, *Writer*, *Writing cost, highest first* and *Changed since written first*. The choice is
  remembered in the browser, separately from the download settings.
- **Filters** — *Document*, *Subject*, *Suite*, *Written by*, *Changes*, *Charts* (in the wizard only,
  § 13) and *Created* (the last 24 hours, 7 days or 30 days) — each open a list of options with the
  number of documents each would leave, counted with the other filters applied. Several options of one
  filter widen the list; several filters narrow it. A filter is offered only while its documents differ
  in it. There is no Sharing filter: a document's sharing follows the disclosure chosen on its own card.
- **Chips** show each active filter and the search; each removes itself, and **Clear all** removes them
  all but *Show selected only*.
- **The selection line** reads *N selected — M not shown*, with **Show selected only**, **Clear
  selection** and **Select all N** (*Select all N matching* while a filter is active), and, in the
  wizard, **Update charts…** (§ 13). There is no select-all checkbox.

The bar stays at the top of the panel while the list scrolls, where the panel is wide enough.

Each card has the *Include* checkbox top left (a click on the title selects the card too, and a selected
card turns gold), a line with the document type, the *Shareable* or *Internal only* tag and the *Run
changed since this document was written* and *Comparison changed* tags in words, the title, a line with
the date, the subject, the suite and the writer (a run file's suite and model), the actions top right,
and under a rule the options, each labeled: *Disclosure*, *Peer names*, *Formats* and, in the wizard,
*Charts*. An internal-only card in the External package is dimmed, with its checkbox disabled and no
options. The list shows 10 cards, then **Show N more** and **Show all M**; after either, focus moves to
the first new card. A new filter, search or sort shows the first 10 again. Card actions are icon-only:

- **View** opens the in-app PDF viewer: a Report Pack or run-completion document at the highest
  disclosure it allows, the others offered as disclosure tabs with the per-document disclosure guide,
  and, when it has peers, a second *Peer names* row (*Named*, *Anonymized*) opening at *Named*; a run
  report as its PDF.
- **Delete** (Report Pack documents only; a run's own documents are deleted from its run report) asks a
  nested confirmation, then moves focus to the next row, else the previous, else the *Documents* heading.
- **More actions**, in the wizard only, holds *Update charts* and *Remove charts* (§ 13).

The panel is an inline-size container: the package column sits beside the list from 48rem of its own
width, and the filter bar sticks from 36rem. The card list lays itself out by its own width: below 30rem a
card puts its actions on their own line and its options one per line, and the filters scroll sideways in
one row.

Each row offers its formats PDF first, then Word: pack documents and the run report PDF, Word, Markdown
and HTML; the tool-call log PDF, Word and Markdown; diagnostics PDF, Word and Text. The choices are
remembered per browser in settings **version 3**. A stored version 2 is migrated: the package, the paper
and every remembered choice are kept except the Internal package's remembered formats, which are dropped
so every admin meets the Internal package's Word default once. Any older version is ignored. The
dialog's explanations — each package's description, the paper size, a row's note and the full reason a
row is internal-only — sit behind click-mode info buttons, and the option meanings in the one *About
document options* dialog; the red *Internal only*
tag, the *Peers are named* warning and the failure list stay on screen.

**Paper size.** *A4* by default, *US Letter* as an option, remembered with the other settings and sent
with every PDF and Word request.

**Progress.** While a package is prepared, an overlay dims the dialog body (leaving Close and Cancel
usable) and shows a ring spinner, the current step — *Preparing 2 of 5 — …*, *Building the ZIP…*,
*Saving…* — and a progress bar. The footer's status line announces the same steps to screen readers;
with reduced motion the ring stands still.

**File names.** A pack document at Full, and every internal-only file (run report, tool-call log,
diagnostics), gets an `_INTERNAL` file-name suffix. A document whose subject is one run (`run:<digits>`,
every run-completion document) is named from the run number first, with a `run-<digits>_` prefix, in its
PDF, Word and Download Center names alike — `run-73_…_full_named_INTERNAL.pdf`. A document with peers
adds `vs-<N>-models_`, N being its peer count, after the run prefix, or first for a group subject —
`run-68_vs-4-models_…_summary_named.pdf`. The client's `reportDocumentFileStem` and the server's
`BenchmarkPdfFileNames.ForReportDocument` build the same name. A group subject has no run prefix. The run report and tool-call log are fetched from the
existing run endpoints and keep the server's file name; their PDFs and Word files are named by the server,
with `_INTERNAL.pdf` and `_INTERNAL.docx`. Run diagnostics are a point-in-time capture, taken **once per
download**: the `.txt`, the `.pdf` and the `.docx` of one download hold the same text and the same capture
time.

**ZIP and manifest.** Several files download as one ZIP, `<model>_<package>_<yyyyMMdd_HHmmss>.zip`, with a
`MANIFEST.md` listing each file's name, document id, audience, disclosure, naming, renderer version,
creation time, writer, format and SHA-256 — for a PDF, of its exact bytes, and with a
`PDF: PDF/UA-1, PDF/A-3A, A4` (or `US Letter`) line, or for a Word file a
`Word: Office Open XML (.docx), A4` (or `US Letter`) line. PDFs and Word files are stored in the ZIP
uncompressed, since their streams already are. The packaging time appears only in the manifest and the ZIP name, so the Markdown
documents stay byte-identical across downloads.

**HTML.** HTML is built client-side from the rendered Markdown by a converter with its own private
`marked` and DOMPurify instances — never the chat pipe's global ones, whose options and hooks belong to
the chat. The output is self-contained, with print CSS.

### PDF

PDFs are rendered **server-side** from the same Markdown every other format starts from, by
`BenchmarkPdfRenderer` (`Overseer/Services/Benchmarking/Pdf/`): QuestPDF under its free Community license
lays out the pages, and Markdig parses the Markdown with raw HTML disabled, so a tag in a model's answer
prints as text. The renderer is a static class like `BenchmarkReportPackRenderer`, so the download path
still reaches no model client, clock or configuration, and the architecture pins of § 7 stand.

- **Conformance**: PDF/UA-1 (tagged: headings, tables with header cells, the logo's alternative text,
  the language `en-US`) and PDF/A-3A; metadata title, author *GnollBench (Overseer)*, subject, keywords
  and creator. The creation date is the stored one — the document's creation, the run's completion (else
  its start), or the diagnostics capture — never the time of the request. PDF/UA makes QuestPDF write a
  new document id each time, so a PDF is reproducible in content, not in bytes.
- **Fonts**: Source Sans 3 and Source Code Pro (SIL OFL 1.1), with DejaVu Sans for arrows, math and box
  drawing, embedded from `Overseer/Resources/Pdf/Fonts/` beside their license texts. The host's fonts are
  never used; a glyph none of them has (an emoji) prints as a replacement mark rather than failing.
- **Page 1**: the wide GnollBench logo, the document kind, the title, the subject line, a facts table,
  *Source {first 16 hex of the source hash} · PDF layout 4*, and a classification banner — amber
  *Confidential …* for a provider copy (the audience-aware stamp of § 6), red *INTERNAL …* for everything
  else, the text saying what the color says. A report document with peers has the subject line
  *"{Suite} · run #68 · compared with 4 models"* (*group #N* for a group subject); a stand-alone one
  keeps the suite and its runs. A table of contents follows when a document other than the
  Executive Summary has four or more `##` sections. A report document's facts table reads *Document ID*,
  *Disclosure*, then, with peers, *Compared with* (Named: the peers' labels in letter order and the count,
  *"… and … (2 models)"*; Anonymized: *4 models (A to D), identities withheld*) and *Pricing basis*,
  or, stand-alone, *Peers* (*none (stand-alone report)*), then *Suite*, *Questions*, *Run* or *Runs*, the
  creation time, *Generated format* (the format version the
  document was generated under), *Writer* (display name, then provider, model id and thinking level, each
  empty part left out) and *Provenance* (the figures computed by Overseer, the prose by the writer and
  checked automatically for structure, permitted figures and references, word limits and disclosure,
  which does not verify its interpretations); it has no *Audience* row, since the document kind says it.
  The Markdown's front matter — the stamp and the *Date*, *Suite*, *Questions*, *Runs* and *Peers* list
  under the title, *Compared with* and *Pricing basis* in place of *Peers* when there are peers — and its
  closing footer — the document ID, version, writer and provenance lines — are
  left out of the PDF and Word files (`BenchmarkReportRenderOptions.IncludeFrontMatter = false` and
  `IncludeDocumentFooter = false`), because the cover prints the same.
- **Every page**: from page 2 a running header with the emblem, *GnollBench · {kind}* and the subject;
  a footer with the short classification, the source hash and layout version, and *Page X of Y*; for
  internal documents a diagonal *INTERNAL* watermark. Header, footer and watermark are artifacts, skipped
  by screen readers.
- **Content**: `#`–`###` headings are bookmarks; tables repeat their header row on every page, never
  split a row across pages when it fits on one (the body is a column of one-row tables sharing the
  header's columns; only a row taller than a page breaks), right-align numeric columns — *not available*
  counts as a placeholder, like *—*, and does not decide a column's alignment — and size columns by their
  content. A narrow table, at most three columns whose preferred widths fill less than 60 % of the text
  width, keeps those widths against the left margin instead of stretching across the page. Code blocks
  and the diagnostics text wrap anywhere. *PDF layout 2* (2026-09-29) brought the unsplit rows, the narrow
  tables, the placeholder and the new cover table.
- **Figures** (*PDF layout 3*, 2026-09-30): a paragraph that is exactly a figure marker,
  `[[figure:<key>]]`, is drawn as the chart given for that key (§ 13), and prints nothing when there is
  none. The image is centered, as wide as the text column unless its height would pass 60 % of the page's
  content height, in which case it is scaled down to that height. It is tagged `SemanticFigure` with the
  chart's alternative text (its title, else *Chart*, when the text is empty), and the caption below it,
  tagged `SemanticCaption`, reads **Figure N.** *Title* — caption, in the table text size. Image and
  caption are kept on one page, and figures are numbered in order of appearance.
- **Closing section** (*PDF layout 4*, 2026-09-30): the last `##` section of a document with at least two
  (*Evaluation terms*) is kept on one page when its estimated height fits a page
  (`BenchmarkPdfMarkdownComposer.KeptTogetherSectionStart`, an estimate that errs high), so no document
  ends on a page holding one bullet; a taller section flows as before.
- **Source hash**: SHA-256 over the UTF-8 Markdown followed by each drawn chart's SHA-256 (lowercase hex)
  in figure order, so a changed chart changes the hash; with no chart drawn it equals the Markdown's own
  hash, as before layout 3. The cover and the footer print its first 16 hex characters.
- **Limits**: a source over 6,000,000 characters is refused with 413 and a message to download the
  Markdown instead; a render stops when the request is canceled. The diagnostics text is posted for
  rendering and is **never stored or logged**.

### Word

Word files (`.docx`) are for editing. `BenchmarkWordRenderer` (`Overseer/Services/Benchmarking/Word/`)
walks the same prepared Markdig tree as the PDF renderer and writes the package with the Open XML SDK,
never by string templating. It is static and stateless like the PDF renderer, so the architecture pins of
§ 7 stand. The colors come from the palette the PDF uses (`BenchmarkDocumentPalette`).

- **Styles**: every piece of formatting is a named style; direct formatting appears only where the
  Markdown carries it (bold, italic, strike, underline, mark, a cell's alignment). Built-in styles keep
  Word's own ids and names — *Normal*, *Title*, *Subtitle*, *heading 1–6*, *List Paragraph*, *Quote*,
  *TOC Heading*, *toc 2*, *header*, *footer*, *Hyperlink*, *Table Grid* — so the Styles pane, the
  Navigation pane, the table of contents and the accessibility checker recognize them. GnollBench adds
  *Document Kind*, *Code Block*, *Inline Code*, *Horizontal Rule*, *Source Line*, *Classification Internal*
  / *Provider*, *GnollBench Table* and *GnollBench Facts*. Headings are Bold where the PDF uses Semibold,
  because Word selects weights only as regular and bold within a family.
- **Lists** are real Word lists: one bullet definition shared by every bulleted list, and a new list
  instance for each ordered list, starting at its own number.
- **Tables** use *GnollBench Table*: a shaded header row that repeats on every page, zebra banding, rows
  that do not split across pages, numeric columns right-aligned, and column widths from the
  same weights the PDF uses.
- **Page 1** mirrors the PDF: the wide logo, the document kind, the title, the subject line, a facts table,
  *Source {first 16 hex} · Word layout 2* and the classification banner; the subject line, the facts table
  and the banner text come from the same document information as the PDF's, so they change with the
  PDF's cover. The source hash follows the PDF's rule, charts included. The table of contents follows
  under the PDF's rule, as a real `TOC` field pre-filled with links to the `##` sections and marked for
  Word to refresh (page numbers included) when the file is opened; Word may ask once to update fields.
- **Every page**: from page 2 a header with the emblem, *GnollBench · {kind}* and the subject; a footer
  with the short classification, the source hash and layout version, and *Page X of Y* as `PAGE` and
  `NUMPAGES` fields; for internal documents Word's own *INTERNAL* text watermark, which *Design →
  Watermark → Remove Watermark* removes.
- **Fonts**: Source Sans 3 and Source Code Pro are embedded the way Word's *Embed fonts in the file* does
  (ECMA-376 obfuscated font parts, not subset), so the document looks and edits the same without them
  installed; about 1 MB per file. A reader whose Word blocks embedded fonts sees Calibri and Consolas.
- **Images**: the two logos are PNG (`Overseer/Resources/Word/`), because WebP pictures do not open in
  Word 2019, Word 2021 or LibreOffice. A Markdown image, `![alt](url)`, still prints as `[alt]`.
- **Figures** (*Word layout 2*, 2026-09-30): a figure marker with a chart (§ 13) becomes an inline picture
  in a paragraph of its own, the PNG in its own image part, as wide as the text column with its height
  capped as in the PDF. Its `DocProperties` carry an id unique in the document from 3 (1 and 2 are the
  logos), the name *Figure N* and the chart's alternative text as the description, which Word shows as
  the picture's alt text. The picture is centered and kept with the caption paragraph below it, which
  reads, as in the PDF, **Figure N.** *Title* — caption.
- **Properties**: title, author *GnollBench (Overseer)*, subject, keywords, language `en-US` and the
  stored creation date (never the request time), plus the custom properties *GnollBench Classification*
  and *GnollBench Source SHA-256*. The file opens without *Compatibility Mode*.
- **Limits**: the PDF's — 413 over 6,000,000 characters, canceled with the request, and the diagnostics
  text is never stored or logged.

---

## 9. Endpoints

All endpoints require the `AdminOnly` policy and sit under `api/admin/benchmark`.

### Report packs (`AdminBenchmarkReportPacksController`)

- `POST /api/admin/benchmark/report-packs/preview`: The fact sheet and prompts without a model call —
  subject, peers, estimated tokens and cost per document, the same-provider warning and any refusal.
- `POST /api/admin/benchmark/report-packs`: Start a job. Body
  `{ runIds, groupIds, batteryRunIds, pricingBasis, subjectKey, audiences[], writerModelConfigurationId, acknowledgeSameProvider }`,
  with audiences as numbers (1 Executive Summary, 2 Report for AI Researchers and Developers, 3 Internal
  Brief). `batteryRunIds` names battery results, and a request naming any may name no run or group.
  Returns 202 `{ jobId }`.
- `GET /api/admin/benchmark/report-packs/jobs/{jobId}`: Job progress, per document.
- `GET /api/admin/benchmark/report-packs/jobs/active`: The running job, or 204.
- `POST /api/admin/benchmark/report-packs/jobs/{jobId}/cancel`: Cancel the job.

The start's refusals, in the order they are checked:

1. Battery results mixed with runs or groups — 400, *"A comparison holds either battery results or runs
   and analysis groups."* The preview refuses the mix the same way.
2. An unknown entry, or an Excluded subject — 400.
3. A writer that is invalid, disabled, keyless, not of the Benchmark role, or refused by the endpoint
   policy — 400.
4. A writer that is the subject's own model — 400.
5. No audience — 400.
6. The spend cap — 429.
7. A same-provider writer without `acknowledgeSameProvider` — 409, with the warning.
8. A job already running, or a run-completion or battery-completion job waiting for the slot — 409,
   with that job.

- `POST /api/admin/benchmark/runs/{runId}/report-documents`: Write a finished run's missing
  run-completion documents now (§ 11). Body `{ writerModelConfigurationId, audiences?, acknowledgeSameProvider }`:
  `audiences` names the documents to write (1 Executive Summary, 2 Report for AI Researchers and
  Developers); null or empty writes every missing one. The writer is recorded on the run as its report
  writer, replacing an earlier one. Returns 202 `{ runId, status, audiences }` with the status Pending
  and the documents the job will write. Refusals, in order: no body — 400; an unknown run — 404; a run
  that has not finished (Completed, CompletedWithErrors or CompletedWithLimits) with a final synthesis —
  400; a job for the run Pending or Writing — 409; a requested audience that is not a run-completion
  document — 400; a requested document already written — 409 (*"The <name> is already written. Delete it
  first to write it again."*), or, with none requested, both written — 409; a writer that is unusable or
  the model under test — 400; a writer of the candidate's provider without `acknowledgeSameProvider` —
  409 with a `SameProviderWarningDto` of role `reportWriter`; a writer refused by the endpoint policy —
  400; the spend cap — 429.
- `GET /api/admin/benchmark/runs/{runId}/report-documents/job`: The run's current or last run-completion
  job (`BenchmarkRunReportJobDto`): the phase, the queue position and the blocking job while queued, the
  writer, per-document start and end times, model calls, tokens and cost, the log, the run's persisted
  status and message, and the server's time. 200 with the view; 204 when this process knows no job for
  the run (none since the last restart, or its finished job has expired); 404 for an unknown run; 503
  when the run-completion service is not available.
- `POST /api/admin/benchmark/runs/{runId}/report-documents/cancel`: Cancel the run's job. 202 with the
  job view once asked; 409 when no job for the run is in progress; 404 for an unknown run. Documents
  written before the cancellation are kept.
- `POST /api/admin/benchmark/runs/{runId}/report-documents/estimate`: Body
  `{ writerModelConfigurationId, audiences? }`. What writing the documents would cost, by the preview's
  arithmetic — per-document estimates and the total (null when the writer has no price) — with the
  writer's refusal or same-provider warning. Builds the fact sheet and the prompts and makes **no** model
  call. 404 for an unknown run; 400 for an audience that is not a run-completion document; a run without
  a final synthesis answers 200 with the refusal.
- `DELETE /api/admin/benchmark/runs/{runId}/report-documents/{documentId}`: Delete one of the run's own
  run-completion documents and settle the run's status (§ 11). 204; 404 when the run or the document is
  unknown, or the document is not this run's run-completion document; 409 while the run's documents are
  being written.

### Battery-completion documents (`AdminBenchmarkBatteryReportsController`)

Under `api/admin/benchmark/batteries/runs/{batteryRunId}/report-documents`, with the request and
response shapes of the run endpoints above (§ 14):

- `POST …/report-documents`: Write the finished battery run's missing battery-completion documents with
  the writer in the body, which becomes the battery run's writer. 202 with the battery run's Pending
  status (its id in `runId`) and the documents to write. Refusals, in order: no body — 400; an unknown
  battery run — 404; a battery run that has not finished, or whose latest analysis is missing, stale or
  incomplete — 400; a job for it Pending or Writing — 409; an audience other than the two — 400; a
  requested document already written, or with none requested both written — 409; a writer that is
  unusable or the model under test — 400; a writer of the candidate's provider without
  `acknowledgeSameProvider` — 409 with the warning; a writer refused by the endpoint policy — 400; the
  spend cap — 429.
- `POST …/report-documents/estimate`: The cost of writing them with a writer, by the preview's
  arithmetic over the battery prompt, with the writer's refusal or warning; no model call.
- `GET …/report-documents/job`: The battery run's current or last job, labeled *Battery run #N*; 204 when
  this process knows none; 404 for an unknown battery run.
- `POST …/report-documents/cancel`: Cancel the job; documents already written are kept. 202; 409 when
  nothing is in progress.
- `DELETE …/report-documents/{documentId}`: Delete one of the battery run's own battery-completion
  documents and settle its status. 204; 404 when it is not one; 409 while its documents are being
  written.

### Report documents (`AdminBenchmarkReportDocumentsController`)

- `GET /api/admin/benchmark/report-documents?suiteId=&runId=&comparison=&origin=&subject=&take=`: List documents,
  newest first, without rendered text, each with `runChangedSinceGeneration`,
  `peersChangedSinceGeneration`, `comparisonKey`, `comparisonEntryCount` (the subject and its peers; a
  group counts once), `peerCount`, `pricingBasis` (`AsRun` or `Current`), `peerLetters` (each peer's entry
  key and its letter, from the fact sheet) and, from the chart manifest only (§ 13), `chartCount`,
  `chartFigureKeys` and `chartSettingsHash`. Every filter is optional:
  - `runId` matches a run of the **subject** only, never a peer's run (§ 5);
  - `comparison=run:1,run:2,group:4` (or `battery:7,battery:9`) takes the comparison's entry keys, in
    any order, and matches the documents whose `ComparisonKey` they hash to; any other form answers 400
    *The comparison must be a comma-separated list of run:&lt;id&gt; and group:&lt;id&gt; keys, or of
    battery:&lt;id&gt; keys.*, and a key that matches nothing lists nothing. The client sends entry keys
    and never hashes;
  - `origin=reportPack|runCompletion|batteryCompletion` filters on `Origin`; absent lists every origin,
    and any other value is a 400;
  - `subject=` takes **one** entry key (`run:<id>`, `group:<id>` or `battery:<id>`, a positive id,
    exactly as written) and matches it exactly against each document's subject key; any other form is a
    400. The Download Center's `battery` context lists a battery run's documents this way, its
    battery-completion and Report Pack documents alike;
  - `take` defaults to 200 and is capped at 500.
- `GET /api/admin/benchmark/report-documents/{id}`: Detail: metadata, validation notes and the facts JSON.
- `GET /api/admin/benchmark/report-documents/{id}/render?disclosure=summary|detailed|full&peers=named|anonymized`:
  The rendered Markdown (`text/markdown; charset=utf-8`), deterministic, with no model call; 400 for a
  refused combination.
- `GET /api/admin/benchmark/report-documents/{id}/render/pdf?disclosure=&peers=&paper=a4|letter&inline=`: The same
  document as a PDF (`application/pdf`), named
  `[run-<id>_][vs-<N>-models_]<title>_<disclosure>_<peers>[_INTERNAL].pdf` (the prefixes for a `run:<id>`
  subject and for a document with peers, § 8; a `battery:<id>` subject takes `battery-run-<id>_` in place
  of `run-<id>_`, § 14), with the document's charts of the requested naming drawn
  in it (§ 13); the same refusals as `render`, 400 for another `paper`, 413 over the size limit. A Report
  for AI Researchers and Developers is named
  `[run-<id>_][vs-<N>-models_]<title without its "— <document name>" ending>_Researcher_Report_<disclosure>_<peers>[_INTERNAL].pdf`,
  whether the stored title ends in the current name or the legacy *Technical Report*. With
  `inline=true` the response carries `Content-Disposition: inline` with the same file name, so a
  browser tab shows the PDF rather than saving it; the PDF viewer's *Open in new tab* uses it (§ 11).
- `GET /api/admin/benchmark/report-documents/{id}/render/docx?disclosure=&peers=&paper=a4|letter`: The
  same document as Word
  (`application/vnd.openxmlformats-officedocument.wordprocessingml.document`), named
  `[run-<id>_][vs-<N>-models_]<title>_<disclosure>_<peers>[_INTERNAL].docx` (with `_Researcher_Report` as
  for the PDF), with its charts drawn as for the PDF and the PDF endpoint's refusals.
- `DELETE /api/admin/benchmark/report-documents/{id}`: Delete a document; its run rows cascade, and its
  chart folder is removed (a folder that cannot be removed is logged and never fails the delete).
  Deleting a run-completion document also settles its run's status (§ 11), and a battery-completion
  document its battery run's (§ 14); unlike the run and battery endpoints, this one does not refuse
  while the documents are being written.
- `PUT /api/admin/benchmark/report-documents/{id}/charts`: Replace the document's whole chart set (§ 13).
  Body `{ charts: [{ figureKey, naming, title, caption, altText, settingsHash, pngBase64 }] }`, at most
  40,000,000 bytes (`[RequestSizeLimit]`). 200 `{ documentId, chartCount, figureKeys, settingsHash }`;
  400 `{ error }` for a stand-alone document (*"This document has no peers; charts are drawn only for
  documents that compare models."*), when chart storage is not configured (*"Chart storage is not
  configured. Set Benchmark:ReportPack:ChartsDataLocation to an absolute folder."*) and for any chart the
  validation refuses; 404 for an unknown document.
- `DELETE /api/admin/benchmark/report-documents/{id}/charts`: Remove the document's charts. 204, also
  when there were none or chart storage is not configured; 404 for an unknown document.

### Run files as PDF and Word (`AdminBenchmarkController`)

- `GET /api/admin/benchmark/runs/{id}/report/pdf?paper=`: The run report as a PDF, named after the
  Markdown with `_INTERNAL.pdf`.
- `GET /api/admin/benchmark/runs/{id}/tool-call-log/pdf?paper=`: The tool-call log as a PDF, named the same
  way.
- `POST /api/admin/benchmark/runs/{id}/diagnostics/pdf?paper=`: Body `{ text, capturedAtUtc }`, at most
  4 MB. Renders the diagnostics the client captured, dated at the capture; 404 for an unknown run, 400 for
  empty text or an unreadable time. The text is not stored or logged.
- `GET /api/admin/benchmark/runs/{id}/report/docx?paper=`,
  `GET /api/admin/benchmark/runs/{id}/tool-call-log/docx?paper=` and
  `POST /api/admin/benchmark/runs/{id}/diagnostics/docx?paper=`: The same three files as Word, with the
  PDF endpoints' validation and limits, named `…_INTERNAL.docx` and `…_diagnostics_INTERNAL.docx`.

---

## 10. What a Report Pack Does Not Do

- It changes no score, index, grading prompt or comparability key, and moves neither `HarnessVersion`
  nor `ScoringMethodVersion`.
- It runs no significance test and states none.
- It never reads the live suite: question and rubric text come from the subject's answer rows.
- It never re-writes a stored document. A changed run is flagged, not re-generated; generate a new pack
  if the old one is out of date. A run's run-completion documents are written again only after they are
  deleted (§ 11). A document's charts can be replaced or removed at any time (§ 13), which changes its
  PDF and Word copies but not the stored row, and involves no model call.

---

## 11. Run-Completion Documents

A run can name a **report writer** when it is launched. Once the run is scored, that writer writes the
run's **Executive Summary** and **Report for AI Researchers and Developers** once, in the stand-alone form
(§ 1), and stores them as ordinary `BenchmarkReportDocument` rows with `Origin = RunCompletion` and the
subject key `run:<id>`. From then on they behave like any other document: every download renders the
stored row, with no model call.

**Choosing the writer.** The launcher's *Grading* fieldset has an optional **Report Writer** field after
*Claim Verifier*, with the empty choice *None — no AI-written reports*.

- `StartBenchmarkRunRequest.ReportWriterModelConfigurationId` is stored on the run as
  `BenchmarkRun.ReportWriterModelConfigurationId` (no foreign key) and stamped on every member run of a
  series.
- The launcher refuses (400) a configuration that is not an enabled Benchmark-role configuration with a
  key or that the endpoint policy refuses, and the model under test (*"The model under test cannot write
  its own reports."*). The launcher form shows that refusal in red under the field and holds Start back.
- A writer from the candidate's provider is **allowed after a warning**. The form shows an amber
  advisory and Start stays available; without `StartBenchmarkRunRequest.AcknowledgeSameProviderReportWriter`
  the start answers 409 with a `SameProviderWarningDto` of role `reportWriter`
  (`BenchmarkRunLaunchOutcome.ReportWriterSameProviderNotAcknowledged`), checked after the assessor's
  same-provider check, so the operator answers one confirmation at a time. The launcher's
  *Same-Provider Report Writer* dialog re-sends the start with the flag, keeping any assessor
  acknowledgment given in the same attempt; neither flag is remembered. A series start maps the outcome
  to the same 409. The automatic job derives its acknowledgment from the writer and the candidate, since
  only an acknowledged launch can put a same-provider writer on a run, and records it on each document as
  `SameProviderAcknowledged`.
- The writer is **not a comparability key**: two runs that differ only in their writer compare as Tier A.
  It grades nothing and writes after scoring.

**What the run records.** `BenchmarkRun.ReportDocumentsStatus` (`BenchmarkRunReportDocumentsStatus`) and
`ReportDocumentsMessage` (at most 1,000 characters):

| Status | Meaning |
|---|---|
| NotRequested (0) | The default: no job has been asked for; also where a run returns after one of its documents is deleted |
| Pending (1) | A job is queued for the report-pack slot |
| Writing (2) | The writer is writing |
| Completed (3) | Every document the job wrote is stored |
| CompletedWithWarnings (4) | They are stored, and one carries validation warnings (§ 3) |
| Failed (5) | The message says why: *"<document name>: <error>"*, *"The report writer configuration is no longer available."*, *"The reports could not be written: <error>"* or *"Overseer restarted before the reports were written."*; a document already stored is kept |
| Skipped (6) | The compliance guard refused the spend; the message is its reason |
| Canceled (7) | An administrator canceled the job: *"Canceled before the writing began."*, or *"Canceled while writing. The <document name> was written and is kept."* (*"… Nothing was written."* when none was); never Failed |

The column is an `int`, so `Canceled` needed no migration.

**When they are written.** `BenchmarkRunReportDocumentService.ScheduleIfDue(runId)` is called from the
`finally` of `BenchmarkService.RunAsync` and of `RunFailedQuestionsAsync`, after the run has completed, and
returns at once, so the next run of a series is never held up by the writer. It writes only when all of
these hold: the run's status is exactly **Completed**; it names a report writer; its final synthesis
exists; it has no run-completion document yet; and its documents are not already Pending or Writing.
`RerunFinalSynthesisAsync` never calls it.

**The job.** One job has one writer and writes the requested documents the run is missing, in list order
(the Executive Summary first); an automatic job requests both. Two documents with two writers are two
jobs, one after the other. `BenchmarkRunReportDocumentService` keeps a per-run, in-memory registry of
jobs, and its **phases** — *Queued*, *Preparing*, *Writing*, *Finished* — are what the job endpoint
(§ 9) reports:

1. **Queued.** The run becomes **Pending**, and the job waits in a first-in, first-out queue for the
   report-pack slot (`BenchmarkReportPackJobManager.WaitForSlotAsync`). The job view gives the number of
   jobs ahead and the job holding the slot (*Run #N: …* or *Report Pack: …*). While it waits, a manual
   Report Pack start answers 409 (§ 9).
2. **Preparing.** `BenchmarkComplianceGuard.CanSpendAsync` is checked; a refusal makes the run
   **Skipped** with the guard's reason. The writer configuration must still exist, be enabled and have a
   key; otherwise the run is **Failed** with *"The report writer configuration is no longer available."*
3. **Writing.** The run becomes **Writing**. A single-run comparison is prepared and the documents are
   written through the Report Pack's own path: parsing, validation, one repair turn, drops, storage and a
   `SystemAiUsageLog` row with `RoleContext = 8`, charged to the user who launched the run (or who
   pressed **Write Reports**). The job records each document's start and end times, model calls, tokens
   and cost.
4. **Finished.** The run ends **Completed**, **CompletedWithWarnings**, **Canceled**, or **Failed** with
   the first failed document's name and error. A document that was stored is kept, so a later **Write
   Reports** writes only the missing one.

Every job view carries the server's time, so a client can tick an elapsed time without trusting its own
clock. A finished job stays visible for **6 hours** (`FinishedJobRetention`), unless the run's next job
replaces it first; after that the job endpoint answers 204 and only the run's persisted status and
message remain.

**Canceling.** `POST …/report-documents/cancel` (§ 9) stops the job in either phase. A queued job leaves
the queue and the run becomes **Canceled** with *"Canceled before the writing began."* A job that is
writing stops at once: the document in hand is discarded, the documents already written are kept, and
the message names them (*"Canceled while writing. The Executive Summary was written and is kept."*, or
*"… Nothing was written."*). Tokens already used are still charged. A canceled job is always Canceled,
never Failed.

**Restart.** No job survives a restart, and the in-memory job views go with it: after a restart only the
persisted status, the message and the stored documents remain, and the job endpoint answers 204. At
startup, in the cleanup block of `Program.cs` that settles interrupted benchmark work, every run left
Pending or Writing becomes **Failed** with *"Overseer restarted before the reports were written."*
(`SettleInterruptedAsync`).

**Writing on request.** `POST /api/admin/benchmark/runs/{runId}/report-documents` (§ 9) writes the
chosen missing documents of any finished run with a final synthesis — a run launched with *None*, a run
from before this feature, or a run whose job failed or was canceled — with the writer it is given, which
it records on the run. The run report dialog's **AI Reports** tab (`app-run-ai-reports`) is where an
administrator does it:

- **The documents.** Both are listed, written or not: a written one with *Written* or *Written with
  warnings*, a line of meta — writer, date, duration, cost and *same provider, acknowledged* when the
  document records the acknowledgment — the *Run changed since this document was written* tag when
  flagged, **View** and a **Delete** icon button; a missing one marked *Not written*. A status line above
  says the job state (*Waiting for the report writer*, *Writing…*, *Failed: …*, *Skipped: …*, the
  cancellation message), with **Show Progress** while the run's documents are Pending or Writing, the
  automatic job's included. A *Downloads* notice with **Open Download Center** opens the Download Center
  on the run, and focus returns to the button when it closes.
- **The write panel**, while a finished run lacks a document: one checkbox per missing document (the
  button reads **Write Report** for one and **Write Reports** for two; a written one is named as already
  written), the report writer picker with an (i) button that opens the per-document advice (§ 4) as a
  modal *Choosing a report writer* dialog, and a live cost estimate from the estimate endpoint, shown as
  an *Estimated cost* panel with the total and, for two documents, the cost of each. The picker starts on the run's own writer, else on the launcher's
  *Report Writer* when it needs neither a refusal nor a warning for this run. A refusal (the model under
  test, an unusable configuration, a run without a final synthesis) shows in red and disables the button;
  a same-provider writer shows an amber warning and, on **Write Reports**, a *Same-Provider Report
  Writer* confirmation (**Write Anyway**), asked on every write and never remembered. **Write Reports** is
  disabled while a job is Pending or Writing.
- **The progress dialog** (`app-run-report-writing-dialog`) opens after a write, and on **Show
  Progress**: a status line that changes with the phase only, a stage rail (*Queued*, *Preparing*, one
  stage per document, *Done*), an activity bar, a stat strip (elapsed time, writer, model calls, tokens,
  cost so far or total, and the estimate), a documents table with **View** once the job has finished,
  and a *Diagnostics* disclosure with **Copy** and **Download**
  (`run-<id>_ai-report-writing-diagnostics_<yyyyMMdd-HHmmss>.txt`). It polls the job every 2 s and backs
  off on failures (2, 4, 8, 16, then 30 s). **Run in Background** and the close button only stop
  following the job, never cancel it; **Cancel Writing** asks first. From the moment a write is accepted
  the job endpoint answers with at least a *Queued* starting view, and a run's earlier finished job never
  answers a new job's poll. When the endpoint answers 204 while the run's stored status is Pending or
  Writing, the dialog keeps polling for up to 30 s after it opened; otherwise it shows the run's stored
  status. The finished dialog sums up what was written, in
  what time and at what cost, with **Open Download Center** and **Done**.
- **The PDF viewer** (`app-pdf-viewer-dialog`): **View** opens the stored document in a full-screen
  in-app viewer, rendered by pdf.js from the server's PDF with peers named, at the fullest disclosure the
  document allows, and the others offered as *Summary* / *Detailed* / *Full* tabs (*Summary* / *Full*
  for the Executive Summary). An (i) button after the tabs opens a modal that explains what each offered
  level contains in that document — Summary and Full for the Executive Summary; Summary, Detailed and
  Full for the Report for AI Researchers and Developers (`report-disclosure-guide.ts`, whose
  per-document texts the Download Center's *Disclosure* column also shows, one section per document).
  It has page
  navigation, zoom, a selectable text layer, **Download PDF** (under the server's file name) and **Open
  in new tab**, a real same-origin URL with `inline=true`. No PDF is framed or embedded, so the CSP is
  unchanged.

**In the run progress dialog.** A run launched with a report writer shows the automatic job as its
**stage 4**, *Writing reports*: the dialog's labels read *Stage n of 4*, the stage is current while the
Completed run's documents are Pending or Writing (and for up to 30 s while they are still NotRequested),
the status line follows the job's phase (*"Stage 4 of 4 — Writing reports: waiting for the report writer
(1 job ahead)"*) and then appends *"Reports written: 2 documents, 1m 12s."*, and the completion chime
waits for the stage to end. The run itself ends Completed when scoring ends. The full description —
roster row, stat cell, cost panel rows, diagnostics block — is `ai-benchmark.md` § 1, *Run Progress
Dialog*.

**In the Download Center.** Opened on a run, the dialog asks for the run's report job
(`GET …/runs/{runId}/report-documents/job`) beside the run's documents. While the job's phase is not
*Finished* it shows, above the table, *"The AI-written reports of this run are being written (<phase>).
They appear here when they are done."* — the phase reads *waiting for the report writer*, *preparing the
fact sheet*, *writing* or *finishing* — and asks again every 5 s (`DOWNLOAD_CENTER_REPORT_JOB_POLL_MS`);
a failed poll is skipped. When the job answers *Finished*, or 204 once it is gone, the dialog reloads the
run's documents and drops the notice. A job already finished, no job, or a failed first request shows no
notice. Closing the dialog, or opening it on another subject, stops the poll.

**Written once, rewritten after a delete.** A run-completion document is immutable like every other:
downloads only render it. A later re-synthesis or re-score marks it *Run changed since this document was
written* and does not rewrite it. To write it again, delete it — the tab's **Delete**, after a
confirmation, or `DELETE /api/admin/benchmark/runs/{runId}/report-documents/{documentId}`, which is
refused while the run's documents are being written — and use **Write Reports**. Deleting a run's
run-completion document through either delete endpoint returns the run's status to **NotRequested** with
no message, unless a job is in progress, so the status never describes a document that is gone; the run
keeps its report writer. After a delete the tab clears the writer picker when it held the model that
wrote the deleted document, so a rewrite starts from a deliberate choice.

---

## 12. The Comparison Wizard's Reports and Documents Steps, and the Comparison Reports Launcher

The Model Comparison wizard has four steps: *1. Sources*, *2. Charts & table*, *3. Reports* (*"Write AI
reports on one model of this comparison"*) and *4. Documents* (*"View, chart, download and delete this
comparison's documents"*). Steps 3 and 4 are reachable once a comparison exists; step 3 also needs an
entry that is not Excluded, and otherwise is `aria-disabled` with a visually hidden reason, and **Next**
skips it. Next runs 2 → 3 → 4, and closes the wizard on step 4. Step 2's former **Reports** button and
the Report Pack dialog it opened are gone; **About** and **Recompute** stay on step 2.

Steps 3 and 4 are mounted on their first visit and afterwards hidden, never destroyed, when another step
is active, so step 3's form, its running job and its polling, and step 4's table page, filters and
selection survive a trip back to step 2. That trip is the loop the steps are built for: set the charts
on step 2, generate on step 3, view on step 4, go back to step 2 to change them, then **Update charts…**
on step 4 and view again (§ 13). While charts are being drawn and uploaded, the wizard's close controls
are disabled and Escape is refused, as during an export.

**Step 3, Reports** (`app-report-pack-panel`, `report-pack/report-pack-panel.component.*`), headed
*Reports*, uses `app-run-report-frame` in its **sidebar** layout: a resizable sidebar with the form, then
the main area with the job, in reading and tab order. From 60rem of body width the sidebar is 20–32rem
wide, at most 40 % of the body, 24rem by default, set by the `app-pane-resizer` between the two (drag, or
Left and Right on it); the width is kept under `sidebarWidth` in
`localStorage['overseer.benchmark.reportPack']`. Below 60rem the two stack, the sidebar first. Each
column scrolls on its own.

- **Sidebar**, *New report pack*: *Subject*; *Documents* (the three checkboxes); *Charts in PDF and
  Word*, the chart picker (§ 13) with, on screen, the print advisory when step 2's theme would print
  badly and, while the `report-charts-location-missing` alert is present, *"Chart storage is not
  configured; documents will be written without charts."*; *Report writer*, with the dialog-mode info tip
  *Choosing a report writer* (`run-ai-reports/report-writer-advice.ts`, shared with the run report's **AI
  Reports** tab, with an Internal Brief entry) and the *How the graders work* link; the refusal or the
  amber same-provider warning; the *Estimated cost* panel (`.gh-estimate-panel`, shared with the AI
  Reports tab); **Generate**.
- **Same-provider writer**: Generate opens a nested *Same-Provider Report Writer* confirmation, **Write
  Anyway**, on every write, and only then sends `acknowledgeSameProvider: true`. Nothing is remembered.
- **Main area**, *Report pack progress*: a status line that changes with the job's phase; while a job
  runs, the stage rail (*Queued*, *Preparing*, one stage per document, *Done*); a stat strip (elapsed,
  writer, model calls, tokens, cost, estimate) that stays after the job finishes; one row per document
  with its status chip, a live duration, its model calls and a charts cell — *Charts: attaching…*,
  *Charts: 3*, *Charts failed — retry* (a button that tries again) or *Charts: none*; and *Log and
  diagnostics*, a disclosure with the job log and icon-only **Copy diagnostics** and **Download
  diagnostics** (`report-pack_<subject>_diagnostics_<yyyyMMdd-HHmmss>.txt`, LF line endings, never naming
  the user who started the job). Elapsed time ticks every second on the server's clock
  (`serverTimeUtc` of the job view, the browser's clock as a fallback). Once the job finishes, a summary
  with **See the documents**, which switches to step 4, and **Dismiss** sits above the stat strip.
- Polling continues while the step is hidden and stops when the wizard is destroyed. There is no
  Markdown preview: documents are read in the PDF viewer, from step 4.

The panel is given a `ReportPackContext` whose `entryKeys` are every entry of the comparison, Excluded
ones included, because the Report Pack request sends every entry's run and group ids and the stored key
hashes those.

**Step 4, Documents** is the Download Center panel (§ 8) placed directly in the wizard, on a library
context of this comparison's Report Pack documents (`comparison=<entry keys>&origin=reportPack`, § 9),
titled *Documents of this comparison*, with **every document preselected**. Run-completion documents stay
in their run's report. A document belongs to a comparison when the comparison has the same set of
entries (§ 5), so changing *Prices* keeps the list, and adding or removing a model empties it. The wizard
lends the panel its chart actions (§ 13): the *Charts* option and filter, **Update charts…** and each
card's **More actions**. The list reloads when a job finishes and when a document is charted.

**The Model Comparison launcher** (Admin → AI Benchmark → Model Comparison) leads with the action: a hero
card with *Cross-model comparison*, its lead and **Open Comparison Wizard** (the page's only `.btn-gh`,
*compass* glyph), then the *Last comparison* read-out, then *How the comparison works* — the four wizard
steps and the like-for-like note — in a disclosure that is open on the first visit and afterwards as the
operator left it (`localStorage['overseer.benchmark.modelComparison.launcher']`). Below it, **Comparison
reports** (`app-report-documents-launcher`, `report-pack/report-documents-launcher.component.*`) sums up
every Report Pack document in one line — *"5 report documents from 2 comparisons · the latest written
…"*, or *"No reports yet. Reports written on the comparison wizard's Reports step appear here."* — with an
*N changed since written* tag when a subject's or a peer's run changed, and a click-mode info tip. Its one
`.btn-ghost` **Open Download Center** (*file-with-arrow*) is `aria-disabled` while there is nothing to
open, the summary line saying why, and opens the Download Center dialog on every Report Pack document
(`{ kind: 'all' }`) with **nothing preselected** and no chart actions. The summary loads when the tab is
shown, not with the page, and again each time the tab is shown, the wizard closes (it may have written
documents), a document is deleted in the Download Center or the Download Center closes; focus then
returns to the button. The launcher still duplicates none of the wizard's controls.

The run report's **Downloads** also opens the Download Center dialog, on the run, without chart actions.
Both places, like step 4, show and download the charts a document already has in its PDF and Word
copies; only the wizard can change them.

---

## 13. Charts in PDF and Word

A comparison document's PDF and Word copies can carry the Model Comparison's own charts — the figures of
step 2, drawn for print. **Charts are drawn in the browser, from step 2's settings, and stored on the
server as PNG files beside the document; rendering only places them.** No model call is involved, the
stored document row never changes, and the Markdown and HTML copies never carry a chart. Only a document
with peers can have charts; a stand-alone document (every run-completion document) has none.

### The figures and where they go

Seven figures can be chosen per document type (`BenchmarkReportChartPlacement`; the client's
`REPORT_CHART_FIGURES`), in this order:

| Key | Figure | Needs |
|---|---|---|
| `p1a-quality` | Intelligence | Two plotted models |
| `p1b-speed` | Speed | Two plotted models |
| `p1c-cost` | Cost | Two plotted models |
| `p2-profile` | Model profiles | Three plotted models |
| `s1-quality-speed` | Intelligence against speed | Two plotted models |
| `s2-quality-cost` | Intelligence against cost | Two plotted models |
| `s3-speed-cost` | Speed against cost | Two plotted models |

| Document | Where the figures go |
|---|---|
| Executive Summary | All in *How it compares*, after the comparison and dimensions tables and before the writer's paragraph |
| Report for AI Researchers and Developers | Intelligence at the end of *Results against peers → Quality*; Speed and Cost at the end of their blocks; Model profiles at the very end of *Results against peers*; the three trade-off charts after the *Speed and cost* table (and not at all when that section is absent) |
| Internal Improvement Brief | All in section 3, after *Key figures* and the interval sentence |

Several figures at one anchor appear in the order of the first table.

**Markers.** The renderer writes a line `[[figure:<key>]]`, with a blank line before and after, at each
anchor for each chart it is given (`BenchmarkReportRenderOptions.Charts`), and only for a document with
peers. The PDF and Word renderers draw the chart there (§ 8: `SemanticFigure` with the alternative text
and a `SemanticCaption` in the PDF, an inline picture with its description in Word, both captioned
**Figure N.** *Title* — caption and numbered in order of appearance); a marker with no chart prints
nothing. The Markdown and HTML downloads pass no charts, so they never carry a marker. The source hash of
a PDF or Word copy covers its drawn charts (§ 8), and adding charts moved the layouts to **PDF layout 3**
and **Word layout 2**.

### Choosing charts: the picker and its defaults

`app-report-chart-picker` is a group captioned *Charts in PDF and Word*: a segmented tab row of the
document types (*Executive*, *Researchers*, *Internal*), each with the number of charts selected for it,
or `—` for a document type not checked under *Documents*; beneath it the selected document's full name
(*Executive Summary*, *Report for AI Researchers and Developers*, *Internal Improvement Brief*),
**All** / **None**, and one checkbox per figure named *"Include Intelligence in the Executive Summary"*
with its target section. With one document type (the Download Center's *Update charts* dialog on
documents of one type) there is no tab row, only that document's list. A document type not being
written, and a figure the comparison cannot draw, stay listed with `aria-disabled` checkboxes and their
reason (*Not checked under Documents*, *needs three or more models*).

The defaults are the Executive Summary's Intelligence and Intelligence against cost; all seven for the
Report for AI Researchers and Developers; and Intelligence, Speed and Cost for the Internal Improvement
Brief. The last selection is remembered per browser in `localStorage['overseer.benchmark.reportCharts']`
(version 1).

### How a chart is drawn

`composeReportChart` in the wizard composes one figure off-screen through the same export pipeline as
step 2's downloads, at the document layout (`DOCUMENT_CHART_LAYOUT` in `report-pack/report-charts.ts`):
1800 px wide, 1800 × 1125 for bars and scatters and 1800 × 1350 for the profile, text at 175 %, PNG —
about 9 pt text and about 270 dpi at column width. Everything else is **step 2's active setting, used as
is**: theme, background, font, weights, colors, border, logo, the per-family styles, Show, Highlight, the
model order and the measures. A bar orientation of *Automatic* is resolved from the document layout's
width, never from the chart on screen. Documents print on white paper, so when step 2 uses the dark
theme, or a transparent background with light text, an **on-screen advisory** says so, beside the picker
on step 3 and in the Update charts dialog; it changes nothing.

Each chart carries a title (the figure's), a caption (its detail line, then *"Drawn from the comparison
computed {time}."*) and alternative text (the title, then one clause per plotted model with its value and
interval). `chartSettingsHash` is the SHA-256 of the canonical JSON of the figure style, the layout, Show,
Highlight, the order, the measures, the pricing basis and the comparison's `computedAtUtc`; it is stored
with the charts, so step 4 can tell charts drawn with the settings on screen from older ones.

**Anonymized variants.** Every figure is drawn twice: **named**, as step 2 shows it, and, when the
document has peer letters, **anonymized** (`anonymizeComparisonForSubject`,
`model-comparison/report-chart-anonymize.ts`): the peers relabeled *Model A*… with the letters of **that
document's** fact sheet (`peerLetters` in the document list), their provider and model id removed and
drawn in the neutral gray, other free text naming a peer rewritten or dropped, entries without a letter
dropped, and the subject unchanged and highlighted. An anonymized render draws only anonymized images;
a missing variant is left out, never replaced by the other.

**The publisher.** `ReportChartPublisher` composes and uploads one document at a time: every selected
figure of the document's type, both variants, converted to base64 and sent as the whole set in one
`PUT`. It records a failure per document and carries on, can be canceled after the document in flight,
and stops once — reported once, not per document — when the server says chart storage is not configured.
The wizard owns one publisher and queues step 3's and step 4's work through it.

### When charts are attached

- **While the wizard is open**, each document of a step-3 job is charted as soon as its row reaches
  *Completed*, with step 3's selection for its type (none when that type's selection is empty). Its row
  shows the progress (§ 12).
- **Otherwise** — the wizard closed during the job, a document written before charts existed, or charts
  to redraw after step 2 changed — step 4 shows *None* or *differs from step 2* and **Update charts…**
  adds or redraws them.

### Managing charts on step 4

In the wizard only, the Download Center panel gains:

- a **Charts** option on each Report Pack card: *None*, *3 · current* (drawn with the settings step 2
  shows now, by `chartSettingsHash`), or *3 · differs from step 2*, tagged `.gh-tag-changed`; and a
  **Charts** filter with the same three states;
- **Update charts…** in the selection line, for the selected Report Pack documents, which opens a
  nested dialog with the picker limited to their document types and prefilled from the figures they already have (else from
  the remembered selection), the print advisory, and **Update** / **Cancel**. Progress shows in the
  preparing overlay. A document without peers, written on another pricing basis than step 2 shows, or
  written for another comparison is skipped and listed under *Not charted* with its reason. When chart
  storage is not configured the overlay closes and a visible warning says so;
- a per-card **More actions** popover (`.gh-action-popover`) with *Update charts* for that document and
  *Remove charts*, each unavailable with its reason on a second line.

Step 4 is opened from the wizard with all of the comparison's documents preselected, so **Update
charts…** applies to all of them at once; the launcher's Download Center opens with nothing preselected
and without chart actions (§ 12).

### Storage on the server

`Benchmark:ReportPack:ChartsDataLocation` names the folder, which must be an **absolute path**; it is
read once, when the singleton `BenchmarkReportChartStore` is created, so a change needs a restart. There
is no fallback. Empty, whitespace or a relative path means *not configured*: uploads are refused with
*"Chart storage is not configured. Set Benchmark:ReportPack:ChartsDataLocation to an absolute folder."*,
renders draw no charts, and `ConfigHealthService` raises the warning alert
`report-charts-location-missing`, which the wizard's Reports step reads. The folder is created on the
first write, never at startup.

**Layout.** One folder per document, `<ChartsDataLocation>/<documentId>/`, holding `manifest.json` and one
`<figureKey>.<named|anonymized>.png` per image; `<ChartsDataLocation>/.staging/<documentId>-<guid>/`
exists only while a set is being written. The manifest is camelCase UTF-8 JSON without a BOM: `version`
(1), `documentId`, `settingsHash`, `createdAtUtc` and `charts`, one entry per image with `figureKey`,
`naming`, `file`, `sha256`, `widthPx`, `heightPx`, `title`, `caption` and `altText`. Every path is built
from the numeric document id, a known figure key and a fixed naming word, and is checked to lie inside
the folder.

**Atomic replace.** A `PUT` replaces the document's whole set, all or nothing, under a per-document lock:
the files and the manifest are written to a staging folder, the old folder is deleted, and the staging
folder is moved into place in one `Directory.Move`. A render reads the manifest and skips, with a logged
warning, an image that is missing or whose SHA-256 differs from the manifest; **a render never fails
because of its charts**. The list and detail DTOs read `chartCount`, `chartFigureKeys` and
`chartSettingsHash` from the manifest alone.

**Upload limits** (`BenchmarkReportChartStore.ValidateCharts`; every chart is checked before anything is
written): a known figure key; naming `named` or `anonymized`; no figure and naming twice; at least one
and at most 16 charts; one settings hash of 64 hex characters for the whole set; alternative text
present and at most 1,000 characters, a title of at most 200 and a caption of at most 500; valid base64
(a `data:image/png;base64,` prefix is accepted) that decodes to a PNG — by its signature — of at most
4 MB, whose IHDR width and height are each 320 to 4,096 pixels; and a request body of at most
40,000,000 bytes. The endpoints are in § 9.

**Deleting.** Deleting a Report Pack document deletes its chart folder; a failure is logged and never
fails the delete. `DELETE …/charts` removes one document's charts. The Admin **Database** tab shows the
folder's size (*Report Chart Files*) and has a **Clear Report Chart Files** action that deletes every
chart image, keeping the documents (`POST /api/admin/maintenance/clear-report-charts`; see
[`chat-data-retention.md`](chat-data-retention.md)).

**Not in the database backup.** The chart folder is outside the database, so a database backup does not
contain it. Losing it loses only images: the documents and their text are intact, their PDF and Word
copies simply have no charts, and **Update charts…** on step 4 draws them again from the comparison, with
no AI call.

---

## 14. Battery Subjects and Battery-Completion Documents

A **battery result** — a battery run's persisted composite over the suites of a battery
(`ai-benchmark-multi-suite.md`) — is a subject like a run or a group, under the same rule: *numbers come
from code, words come from the writer, rendering involves no AI*. Its subject key is `battery:<id>`. It
is the subject of a Report Pack written from a comparison of battery results (the wizard's step 3), whose
peers are the comparison's other battery results, and of the battery run's own **battery-completion
documents**. Why batteries have AI-written documents at all, and why the members write none, are
`ai-benchmark-multi-suite.md` § 7.2 (decisions D2 and D3).

**The fact sheet** is `BenchmarkBatteryReportFacts.Build`, a `BenchmarkReportFactSheet` with
`SubjectKind = "Battery"`. Every analysis figure is read from the battery's persisted
`BenchmarkBatteryStatisticsResult` and never recomputed; the per-answer figures — tokens, tool shares,
refuted sentences, the response-style conflict — come from the usable member runs' answer rows, as a
run document's do. It supplies **every generic key** the renderer, the validator and the prompt read, so
a battery document renders through the same code as a run's:

- **Supplied:** `quality.*` (the Overall Index and its interval, from the comparison entry), the
  count-weighted `dimension.*` means, `band.<b>.questions`, `speed.*` and `cost.*` (`cost.perRun` and
  `cost.totalRunPerRun` per battery pass), `tokens.*`, the tool shares and calls per question, `style.*`,
  `answers.scored` and `errors.critical` summed over the item rows, `claims.*`, `panel.*`, `scoring.*`,
  `run.*`, and per peer the index, interval, rank, overlap, speed, cost and runs.
- **Unavailable, each with its reason:** `suite.name` (a battery spans several suites);
  `quality.rawIndex` and `quality.unweightedMean` (the Overall Index is a weighted composite);
  `band.<b>.score`, `.peerMean` and `.difference` (a battery weights suites, not bands);
  `tools.failed` and `tools.refusedByBudget` (per-call tool rows are not loaded);
  `tools.callsPerQuestion.peerMean` with peers (their answers are not loaded);
  `panel.judgeDependentPairs`; and, on a stand-alone sheet, every peer fact (*"A stand-alone battery
  report has no peers."*). A peer has **no paired difference**: the `peer.X.paired*` facts are absent.
- **The battery's own keys:** `battery.*` (name, revision, scheme, suite count, runs per suite, member
  runs, rounds, definition and class hashes, pooled identity, critical-error rate, speed index, the
  suites' SD and range, excluded members, caveats), `suite.<n>.*` (name, weight, index, contribution,
  interval, scored items, runs, speed index, cost per run, critical-error rate; *n* from 1),
  `sensitivity.<scheme>`, `sensitivity.panelVerificationCleared` (harness 48, only when a member is a
  panel run: the advisory panel grading sensitivity, not a weighting scheme; the writer is told never to
  present it as a corrected result) and `loo.<n>`.

A battery sheet has **no finding rows**: its items cite fact keys and question references as evidence.

**Questions.** They are numbered across the battery, suite by suite, and referred to as **`S<n>-Q<m>`**
— `S2-Q7` is question seven of suite two — in the prose, in `evidence` and in the rendered document;
that form replaces `Q7` and is the only place a digit may appear in the prose. The validator applies
rules 3, 4 and 5 to that form: a plain `Q7`, or a reference to no question of the battery, is an issue.
Every question gets a **one-line row** — band, mean score over the runs that scored it, critical errors,
refuted claims, tool calls. At most `Benchmark:ReportPack:BatteryDetailQuestionsPerSuite` (default
**6**) questions per suite also get **full detail**: the question as asked, its rubric without its
`SOURCE` paragraphs, one answer excerpt from the run whose score was the median of the question's rounds,
and the graders' comments on it. They are chosen critical errors first, then the lowest and the highest
mean scores, alternately. Question topics and notes are asked for the questions in detail only, and a
question needs a note when it scored below 50 or carried a critical error.

**The prompt budget.** When the largest writer prompt exceeds `Benchmark:ReportPack:BatteryMaxPromptChars`
(default **360,000** characters, about 90,000 tokens), detail is left out in reverse priority until it
fits, and a **rule 20** note (§ 3) names the questions that kept only their rows. The note is stored with
the document and logged with the job; it drops nothing and does not mark the document *Completed with
warnings*.

**The prompt's BATTERY block** explains the composite (the weights, each suite's contribution, the
interval's two components), forbids comparing it with a single suite's, run's or group's index, asks the
writer to use the suite profile, the sensitivity and leave-one-out facts only to say how far the result
depends on the weights or on one suite, and states that cost is per battery pass. The audience system
prompts are not edited, and run and group documents render exactly as before.

**No significance test.** A battery document carries the documents' own frozen statement
(`BenchmarkReportFacts.NoSignificanceStatement`, § 2), independent of the Model Comparison wizard's
*Pairwise significance* excluded-measure text, and peers carry no paired difference. It does not cite
the battery's M7 comparison or the wizard's paired tests; that is a follow-up.

**Cover and file names.** The PDF and Word subject line reads *Battery run #N — {battery} (K suites, R
runs per suite)* (`BenchmarkPdfDocumentInfo.BatterySubjectLine`), and the facts table names the battery,
the battery run and the member-run count. Files are named with the prefix `battery-run-<id>_` in place of
`run-<id>_` (`BenchmarkPdfFileNames.ForReportDocument`, and the Download Center's
`reportDocumentFileStem`).

### Battery-completion documents

A battery run can carry a **report writer** (`BenchmarkBatteryRun.ReportWriterModelConfigurationId`),
chosen with the launcher's *Report Writer* field when the battery is started and checked then against the
tested model, with the same refusal and same-provider warning as a run's (§ 4). The battery run records
`ReportDocumentsStatus` and `ReportDocumentsMessage` with the statuses of § 11 (migration
`AddBatteryReportDocuments`). Its members are launched with no writer.

**When they are written.** `BenchmarkBatteryReportDocumentService.ScheduleIfDue(batteryRunId)` is called
right after the battery run's automatic analysis succeeds, and returns at once. It writes when all of
these hold: the battery run has finished; its latest analysis is complete and not stale; it names a
writer; it has no battery-completion document yet; and no job for it is Pending or Writing. Each document
is a one-entry comparison of the battery result, stored with `Origin = BatteryCompletion` (3) and the
subject key `battery:<id>`; the writer call goes through `WriteBatteryCompletionDocumentsAsync` and the
Report Pack's own path — parsing, validation, one repair turn, drops, storage.

**The job** is the run-completion job's twin, keyed by battery run id: the same phases, the single
report-pack slot (`BenchmarkReportPackJobManager.WaitForSlotAsync`), the compliance guard, the
`BenchmarkRunReportJobDto` view labeled *Battery run #N*, cancellation and the 6-hour retention. At
startup `SettleInterruptedAsync` marks a battery run left Pending or Writing **Failed** with *"Overseer
restarted before the battery reports were written."* Deleting the battery run cancels its job and
deletes its battery-completion documents with their chart files (`SettleAfterDeleteAsync`); deleting one
document returns the battery run to NotRequested unless a job is in progress.

**Writing on request.** The endpoints are in § 9. The Battery Run Report's **AI Reports** tab
(`app-battery-ai-reports`) is the counterpart of a run's: both documents listed, **View** in the PDF
viewer, **Delete** behind a confirmation, *Write missing reports* with the writer picker (starting on the
battery run's own writer), the cost estimate, the same-provider confirmation and **Show Progress**. The
Download Center, opened on a battery run (its `battery` context), lists the battery's Markdown analysis
report and every document whose subject is `battery:<id>` — battery-completion and Report Pack alike,
through `subject=` — with no member-run files, and shows the *being written* notice while the battery
run's job runs, asking every 5 s.
