# Report Packs — AI-Written Documents about the Models of a Comparison

A **report pack** is a set of up to three documents about the models of a Model Comparison. By default
they are **comparison-wide**: one document of each type covering every model of the comparison, or a
chosen subset of two to twelve of them, each model described as an equal (§ 15). The secondary choice is
a **per-model** set: documents about one model, written in the context of the other models compared with
it — at least one other, since a pack compares models (§ 1a). Every comparison is numbered, *Comparison
#N*, and its documents carry the number (§ 15). The documents are for three different readers: a model's
provider or a manager, AI researchers and model developers, and the Overseer team itself.

The same machinery also writes a run's own **run-completion documents**: the Executive Summary, the
Report for AI Researchers and Developers and the Internal Improvement Brief about one run on its own,
with no peers, written once after the run is scored by the report writer the run names (§ 11).

A **battery result** — one model's composite over the suites of a battery — can be a subject too: of a
pack from a comparison of battery results, and of its own **battery-completion documents**, written once
after the battery run finishes and is analyzed (§ 14).

A saved **chat consistency analysis** — one model's Overseer chat across two periods of runs — is
written up as **Chat Consistency Report** documents, among them a fourth audience, the **Provider Issue
Report** (§ 16).

This document describes the feature for developers: what each document holds, how the figures and the
prose are kept apart, how the prose is validated, what is stored, how a stored document is rendered
at download, how its PDF and Word copies carry charts (§ 13), how a battery result is a subject
(§ 14), and how comparisons are numbered and written about as a whole (§ 15). §§ 1–14 describe the
per-model documents unless they say otherwise. It is the companion to [`ai-benchmark.md`](ai-benchmark.md), which describes the harness,
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
| The numbered comparison (*Comparison #N*), its name and its endpoints | `BenchmarkComparison`, `BenchmarkComparisonIdentityService`, `AdminBenchmarkComparisonsController` (§ 15) |
| A comparison-wide document's fact sheet and content | `BenchmarkComparisonReportFacts`, `BenchmarkReportContent.BuildComparison` (§ 15) |
| A document laid out with placeholder text, for step 3's Preview layout | `BenchmarkReportLayoutPreview` (§ 15) |
| Generation: preparation, the writer call, repair, storage | `BenchmarkReportPackService` |
| The background job and its progress | `BenchmarkReportPackJob`, `BenchmarkReportPackJobManager` |
| A run's run-completion documents: scheduling, the queued job, the run's status | `BenchmarkRunReportDocumentService` (singleton) |
| A battery subject's fact sheet and content | `BenchmarkBatteryReportFacts` |
| A battery run's battery-completion documents | `BenchmarkBatteryReportDocumentService` (singleton), `AdminBenchmarkBatteryReportsController` |
| Deterministic Markdown rendering | `BenchmarkReportPackRenderer` (pure, static), behind `BenchmarkReportRenderService` |
| Chart images on disk: storage, validation, manifest, loading | `BenchmarkReportChartStore` (singleton), `Overseer/Models/BenchmarkReportChartModels.cs` |
| Which figure goes where, and the figure markers | `BenchmarkReportChartPlacement` |
| Endpoints | `AdminBenchmarkReportPacksController` (the write-now endpoint included), `AdminBenchmarkReportDocumentsController` |
| A chat consistency analysis's fact sheet, prompt, rendering and endpoints | `BenchmarkChatConsistencyReportFacts`, `BenchmarkReportPackPrompt.ChatConsistency.cs`, `BenchmarkReportPackRenderer.ChatConsistency.cs`, `AdminChatConsistencyController` (§ 16) |
| Golden files | `Overseer.Tests/UnitTests/Golden/ReportPack/` |

---

## 1. Purpose and the Three Documents

Step 3 of the Model Comparison wizard, **Reports**, starts a report pack (§ 12). Under *Whole
comparison*, the default, it writes the comparison-wide documents of § 15. Under *One model at a time*
the admin picks one or more comparison entries as **subjects** — each a run, an analysis group or a
battery result (§ 14) — and each subject gets its own per-model documents, the subject of this section.
The admin also picks a separate **report writer** model and which documents to write. The other entries
of the comparison are a subject's **peers**, lettered A, B, C… in quality-rank order.

Any entry that is not **Excluded** can be the subject, as long as at least one other entry is not
Excluded either: a subject with no peer is refused (§ 1a). A **Degraded** entry is allowed: its degraded
axis is omitted from ranking, and the fact sheet marks the affected facts unavailable with the reason.

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

**The Internal Improvement Brief is also a completion document.** From 2026-10-06 it is the third
run-completion document of a run (§ 11) and the third battery-completion document of a battery run
(§ 14), written in the stand-alone form beside the other two. Its `modelResult` slot asks how the
subject performed, *"against its peers when it has them"*, so one prompt serves both forms; the
renderer golden files `internal_standalone.md` (a run subject) and `internal_battery_standalone.md` (a
battery subject) pin the stand-alone brief.

**The stand-alone (peerless) form.** A comparison with only the subject — every run-completion document
(§ 11) and every battery-completion document (§ 14) — has no peers. The Report Pack no longer writes
this form (§ 1a); Report Pack documents stored before 2026-10-06 may still have it. The fact sheet then marks every fact that compares the subject with peers
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
`server_benchmark_to_chat_transfer` skill before anything is changed. Each names the most specific
target the data shows: the question and its topic, and what to look at there (the rubric point a
grader charged, the knowledge source an answer excerpt relied on, the grading role that disagreed,
or the kind of tool call the matrix shows), and never a file, setting or tool the data does not show.

---

## 1a. Document Homes

**One rule: a document has one home, decided by whether it has peers** (2026-10-06; the decision and
the alternatives it rejected are `ai-benchmark-multi-suite.md` § 7.2, decision D8).

| Kind | Home: written in | Listed in | Never listed in |
|---|---|---|---|
| A run's own documents (no peers) | Run report → **AI Reports** tab, and automatically at the end of the run (§ 11) | The run's Download Center | Model Comparison |
| A battery run's own documents (no peers) | Battery Run Report → **AI Reports** tab, and automatically at stage 3 of the battery progress dialog (§ 14) | The battery run's Download Center | Model Comparison |
| Comparison documents (comparison-wide over two to twelve models, § 15; or per-model, one subject with at least one peer) | Model Comparison → step 3 (§ 12) | Step 4 (this comparison), and the launcher's **Comparison reports** (all comparisons) | A run's or battery run's Download Center. These show a pointer instead (§ 8) |

What keeps each document in its home:

- **A comparison report needs a peer.** `BenchmarkReportPackPreparation.HasPeers(comparison, subject)` is
  true when another entry of the comparison is not Excluded and has a different key, the entries the fact
  sheet takes as peers. The Report Pack's preview and start refuse a subject without one with
  `BenchmarkReportPackPreparation.PeerlessReportRefusal`: *"A comparison report compares one model with at
  least one other. To write a run's or a battery run's own reports, use the AI Reports tab of its
  report."* (§ 9). The rule is checked by the Report Pack endpoints only, never inside `CompareAsync` or
  `PrepareAsync`, because the run- and battery-completion paths prepare a one-entry comparison. The
  wizard's step 3 is unavailable until the comparison holds two entries that are not Excluded (§ 12).
- **One comparison document per comparison, subject (or covered set) and document type** (§§ 5, 15):
  step 3 lists a document already written as *Written*, its checkbox *Rewrite — replaces the current
  document* unchecked, and the start refuses a duplicate with 409 unless the request names it under
  `replaceDocumentIds`. Nothing is overwritten or duplicated silently: a rewrite replaces the old
  document only once the new one is stored (§ 15), and a document can also be deleted, on step 3 or 4.
- **The run and battery Download Centers list their own documents only**, by `Origin`, and point to the
  comparison documents about their subject with **Open comparison documents** (§ 8).
- **The Internal Improvement Brief is a completion document** (§ 1), so a run's or battery run's brief
  has a home without a one-entry comparison.
- **Comparison file names carry the comparison** (§ 8): a Report Pack document of a numbered comparison
  is named `comparison-<N>_…`, and an older one without a number has a `vs-` part when it has peers;
  a completion document has neither, so the two kinds never share a file name.

**What stays as it was.** `ReportPack` (1) keeps meaning a document of a Report Pack job; no `Origin` was
added. Report Pack documents stored before this rule — peerless ones included — are not re-homed: the
launcher's **Comparison reports** still lists them and deletes them. A group can still be the subject of
a comparison document *with* peers; a group's stand-alone documents have no home any more, which was
accepted on 2026-10-06.

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
**Per-model documents do not cite the paired tests**: a per-model Report Pack document, a
run-completion and a battery-completion document state that no pair is tested, even where the wizard
has tested one. Comparison-wide documents cite the family-adjusted paired tests over their covered
models instead, and carry no such statement (§ 15).

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

`BenchmarkReportPackValidator` checks the writer's JSON against nineteen rules, and a comparison-wide
document against rule 21 too, with rules 2, 10 and 16 read for its tokens (§ 15); a chat consistency
document is checked against rules 22 to 28 (C1 to C7, § 16) as well. Each failure is a
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
| 21 | A comparison-wide document's `models` list has an entry for every covered model (`ModelCoverageRule`, § 15). A warning, kept after the repair turn like rules 12 to 19 |
| 22 | **C1**, chat consistency only (§ 16): a sentence with a change word (`ChatChangeWords`) holds a token that supports a change — a changed endpoint, verdict or attribution, a control DiD whose interval excludes zero, an established reliability increase, or a rejected secondary estimate (`ChatChangeClaimRule`). An error: the paragraph is dropped |
| 23 | **C2**, chat consistency only: an intent word (`ChatIntentWords`) or mechanism word (`ChatMechanismWords`) needs a *ProviderConfirmedCause* annotation token in the sentence (`ChatIntentMechanismRule`). An error |
| 24 | **C3**, chat consistency only: a causal connective (`ChatCausalConnectives`) needs an attribution token in the sentence (`ChatCausalClaimRule`). An error |
| 25 | **C4**, chat consistency only: a public-claim word (`ChatPublicClaimWords`: *publishable, established, confirmed, proven, definitively, conclusively, we can state*) needs an Established grade token; not counted after *not*, *never*, *not yet* or a hyphen, and *confirmed* is allowed with a provider-confirmed cause token (`ChatPublicClaimRule`). An error |
| 26 | **C5**, chat consistency only: a slot that cites an inconclusive endpoint also cites its `{{endpoint.<P>.mde}}` (`ChatInconclusiveMdeRule`). A **warning**, checked over the slot's kept text |
| 27 | **C6**, chat consistency only: an all-hours word (`ChatAllHoursWords`) needs `{{serving.timeOfDayAssessable}}` in the sentence and that fact true; and the document cites `{{scope.hours}}` somewhere (`ChatHoursRule`). An error; the second half cannot drop text and marks the document *Completed with warnings* |
| 28 | **C7**, Provider Issue Report only: a model or serving term (`ChatModelServingTerms`) needs a provider-side attribution token, except in `affectedModel` and `sampleRequestIds`; and `ruledOut` cites a token of every `events.<n>` (`ChatProviderReportRule`). An error; the second half cannot drop text and marks the document *Completed with warnings* |

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

**Writer rules added with harness 54** (2026-10-08, no format version change;
`BenchmarkReportPackPrompt.SharedWritingRules`), at the end of WEIGHING THE EVIDENCE in the system prompt
of every audience for a run, group or battery subject — run- and battery-completion documents included,
the comparison-wide (§ 15) and chat consistency (§ 16) prompts not:

- **Levers, not training.** *"Outside a recommendation for model developers, never recommend training,
  fine-tuning or using outputs as training targets; recommend a lever the facts name — a tool, a tool
  guide, the knowledge base, a wiki page, a rubric, the grading, or the model and its settings."* The
  model-developer recommendations (R12) keep their own rule.
- **Disagreement means a panel disagreement.** *Disagreed* and *disagreement* are written only for an
  answer the facts mark as a panel disagreement; any other gap gives both members' scores.
- **A wrong tool result is a source lead.** When an answer repeats a tool result the facts show to be
  wrong, the lead is about that result's source (tag `corpus`), not about the model's knowledge.

The rules reach documents written from now on, which record the new `WriterPromptSha256` (§ 7).

---

## 4. The Writer Model

**Rules enforced by the server.**

- **The model under report is refused as its own writer** — the same provider and model id (400). A
  comparison-wide document refuses a writer that is a covered model's configuration — the same provider,
  model id and thinking level — and warns about a writer sharing any covered model's provider (§ 15).
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

**Per document.** The documents ask different things of the writer, and each can be written by a
different model:

- **Executive Summary** — short (about 2,400 output tokens) and plain-language, for decision-makers;
  clear, careful wording matters more than depth. A strong writing model — Claude Opus or GPT Sol — at
  medium effort; it is cheap even with a strong model.
- **Report for AI Researchers and Developers** — long (about 7,400 output tokens) and number-dense, and it
  must keep every figure exact and follow a strict schema. The strongest scoring-tier reasoning model you
  trust with numbers — Claude Opus or GPT Sol — at medium effort, high if its documents often need the
  repair turn. It costs roughly three to four times the summary.
- **Internal Improvement Brief** — for the Overseer team; it weighs the whole fact sheet, rubrics and
  grader notes included, and says what to improve in the chat, the benchmark and the model. The
  strongest scoring-tier model — Claude Opus or GPT Sol — at medium effort.
- **Every document** — prefer a writer from another provider than the model under test (a same-provider
  writer is allowed after a warning), and, where the roster allows, one that shares a family with neither
  panel member either; avoid the economy tiers (Flash, Flash-Lite) and the top tiers (Claude Fable, GPT
  Astra). For a run or a battery run, documents with different writers are separate rounds: write one,
  then choose another writer for the next.

**Usage and guards.** Each writer call is recorded in `SystemAiUsageLog` with `RoleContext = 8`
(Report Pack), run-completion documents included. While a job runs, the writer configuration cannot be
deleted: the usage guard reports a blocker of kind `reportPackJob`, and of kind `runReportWriter` for a
configuration named as the report writer of a run whose documents are Pending or Writing. The start is
refused with 429 when `BenchmarkComplianceGuard.CanSpendAsync` denies it, and with 409 while another
report-pack job runs or a run-completion job waits for the slot — one job at a time.

---

## 5. Storage and the Scoring Fingerprint

Each document is one **immutable** `BenchmarkReportDocument` row; there is no update endpoint. It holds:

- audience, subject key (`run:<id>`, `group:<id>`, `battery:<id>` or `chat-consistency:<id>`) and
  label, the subject's run ids (for a battery, its usable member runs), the comparison request, and the
  suite;
- `Origin` (`BenchmarkReportDocumentOrigin`): **ReportPack** (1, the default, and the value every row
  written before the column existed carries) for a document of a Report Pack job, **RunCompletion** (2)
  for a run's own run-completion document (§ 11), **BatteryCompletion** (3) for a battery run's own
  battery-completion document (§ 14; the column is an `int`, so the value needed no schema change),
  **ChatConsistencyReport** (4) for a document written from a saved chat consistency analysis (§ 16). An
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
  one run's key. An index on `(ComparisonKey, Origin, CreatedAtUtc)` serves the list;
- `Scope` (`Model` or `Comparison`), `ComparisonId` (the numbered comparison, a `Restrict` foreign key,
  never auto-included), `CoveredEntryKeysJson` and `CoveredSetKey` — the comparison a Report Pack
  document belongs to and the entries it covers (§ 15). Run- and battery-completion documents keep
  `Scope = Model` and null in the other three. A chat consistency document has `Scope = ChatConsistency`
  (3), null in those three and in `ComparisonKey`, and its analysis in `ChatConsistencyAnalysisId` (a
  `Restrict` foreign key to `ChatConsistencyAnalysis`, never auto-included, so an analysis with documents
  cannot be deleted — § 16).

**One Report Pack document per `(ComparisonKey, SubjectKey, Audience)`.** From 2026-10-06 a comparison
holds at most one per-model document of each type about each subject (§ 1a), and one comparison-wide
document of each type per covered set (§ 15). The start endpoint enforces it, not a
database constraint: it hashes the **request's** sources with `BenchmarkReportComparisonKey.From`, exactly
as the stored key was hashed (Excluded entries included, because the request carries them), looks for a
`ReportPack` row with that key, the subject's key and a requested audience, and refuses 409 when one
exists and the request does not name it under `replaceDocumentIds` (§§ 9, 15). The preview reports the
same rows as `writtenDocuments`, the newest per audience. Rows
stored before the rule may hold two documents of one type for one comparison and subject; the check then
reports the newest. Completion documents are not counted: their `Origin` differs.

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
only, and so does the list's `runId` filter (§ 9): it matches every document with a subject row for the
run — a group's or a battery result's documents whose subject includes it among others too — and never
one where the run is only a peer. A run's own documents are those whose subject key **is** the run,
`run:<id>`, with `Origin = RunCompletion`, which is what the run's Download Center lists
(`subject=run:<id>&origin=runCompletion`, § 8).

**Rows written before these columns existed.** At startup, `BenchmarkReportDocumentBackfill` gives every
row with no `ComparisonKey` one derived from its stored `ComparisonRequestJson`, in batches of 200,
beside the run-completion settlement in `Program.cs`; a row it cannot read is logged and stays null, and
lists only where no comparison filter applies. The backfill is idempotent. Those rows have **no peer
rows** — their peers' fingerprints were never stored — so they are never flagged *Comparison changed*.
A second startup backfill then numbers their comparisons and fills their covered entries (§ 15).

There is **no foreign key to runs**: deleting a run keeps its documents. Deleting a document cascades to
its child rows and removes its chart folder (§ 13); its numbered comparison stays.

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
letter — the interval-overlap figure (*"overlaps every peer's"*) and *Judge-dependent pairs* — and prints a
peer's label with no letter after it (*Grok 5*, not *Grok 5 (A)*). The *Compared models* table carries a
*Letter* column under both namings, so a named and an anonymized copy can be matched. The subject is
always named.

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

**Format version 12** (2026-10-06) is the format of the comparison-wide documents
(`ComparisonReportFormatVersion`, § 15); `BenchmarkReportPackRenderer.CurrentFormatVersion(scope)` gives
each scope's version. Per-model documents are written under format 11. Every Report Pack document of a
numbered comparison prints its comparison (§ 15), and every per-model document prints a named peer
without its letter, a *Letter* column in *Compared models* and joint ranks (*"joint 1st of 2 (intervals
overlap)"*), whatever format it was written under.

**Chat consistency format version 1** is the format of the chat consistency documents
(`ChatConsistencyReportFormatVersion = 1`, § 16), numbered on its own: `CurrentFormatVersion(scope)`
returns it for `ChatConsistency`, the document stores it, its reproducibility section and footer print
it, and the PDF cover shows it as *Generated format*.

**Writer prompt revision (2026-10-06, no format version change)** comes from the review of the
Comparison #2 report pack. It changes the writer prompt of every scope, and so reaches documents
written from now on; every document records the hash of the prompt it was written with
(`WriterPromptSha256`), which tells a document written before the revision from one written after.
- **P1, response style**: the conflict states a condition, not a cause; the writer says that
  completeness was graded under the concise style and never that the style caused the gap or a part of
  it (*Response style (H4)* under format version 11, below).
- **P2, what a cited question supports**: a question given with its excerpts and grader comments
  supports a claim about what an answer said or left out; one seen only as a matrix row, or by its
  scores, supports only a claim about its scores, critical errors, tool calls or time.
- **P3, generalizing, ordering and suites**: a statement about several models must hold for each in
  their excerpts and grader comments (one grader's charge is attributed to that grader); a model's
  lowest or highest questions are taken in order without skipping one; a difference between suites or
  difficulty bands is never explained by what a suite or question contains.
- **P4, one run a side**: a paired-test speed or cost result carrying the single-run caveat is said to
  rest on one run a side.
- **P5, overclaiming words**: *settled*, *proven*, *definitive*, *definitively*, *conclusive* and
  *conclusively* join the hype list of rule 17 (a warning that asks for a repair turn); READABILITY
  reads *"No hype, filler or overclaiming words"*.
- **P6, a default choice**: the Executive Summary's `whichModel` ends with one sentence naming a default
  choice for a typical Overseer player and the case for another model, based only on established results
  and the frontier facts; its cap is 150 words.
- **P7, leads name a target**: each lead names the most specific target the data shows (§ 1, *Leads*).

The same round changed the comparison renderer, which reaches stored documents on their next download:
the matrix legend names each column's model in a named copy (*"Columns: A = …, B = …."*), says that *—*
under a model marks a question it was not asked or not scored on and, when a topic is missing, that *—*
under Topic marks a question not given in detail; a two-model comparison's interval and paired-test
sentences speak of *"the two models"*; and a battery comparison's *Threats to validity* counts the
questions the writer was given in full, per suite. The reference reader's caveat reads *"Its scores do
not count toward the score; its neutrality between the two panel families is an assumption."*, and an
item the claim verifier left out reads *"The verifier gave no ruling on this item."*. A spread whose
range rounds to 1 reads *"1 point apart"*, and one whose two displayed values are equal *"97 for every
model"*; that fact text reaches new documents only.

**Format version 11** (the current per-model one, 2026-10-04, with harness 49 in `ai-benchmark.md`) comes from the
battery run 4 analysis (runs 82 and 83). It changes no score or index of its own; stored documents
re-render with the new renderer on their next download, and the prompt changes reach only documents
written from now on.

- **Battery panel facts (H2)**: when the battery analysis carries its panel agreement block
  ([`ai-benchmark-multi-suite.md`](ai-benchmark-multi-suite.md) M5), `panel.icc` is the ICC(A,1) pooled
  over every answer both members scored (*"0.50 (pooled over 36 answers both members scored)"*), not
  the mean of the runs' ICCs, and `panel.memberAAlone`, `panel.memberBAlone`,
  `panel.referenceReaderIndex` (one decimal, *"86.9 / 100"*), `panel.referenceReaderOffset` and
  `panel.meanAbsDelta` are taken from it (`FactList.Replace`). An analysis stored before the block keeps
  the per-run means.
- **Response style (H4)**: the response-style conflict states a condition, not a cause. The writer is
  told that the model's completeness is its lowest dimension, well below its accuracy, under *the
  production chat's concise response style — the default every Overseer user receives, which the
  benchmark grades as it is*; to say, where completeness is discussed, that it was graded under that
  style; and never to say that the style caused the gap or a part of it, never to present the gap as
  the model's failing alone, and never to call the style the benchmark's instruction. In a comparison,
  where the fact is true for several models it is said once for all of them and never used to explain
  a difference in completeness between models. The one controlled pair (runs 11 and 12) did not show
  the style lowering Completeness.
- **Cost labels (H4)**: a battery's `cost.perQuestion` is *Candidate cost per question*, `cost.perRun`
  *Candidate cost per battery pass*, and `cost.totalRunPerRun` *Total cost per battery pass (every
  grading and synthesis role; report writer excluded)* (`BenchmarkReportPackRenderer.SpeedAndCostLabel`);
  a run's `cost.totalRunPerRun` is *Total cost per run (every grading and synthesis role; report writer
  excluded)*, since `ModelPricingService.ComputeRunRoleCosts(...).Total` sums the candidate and every
  grading and synthesis role and the report writer is no run role.
- **Runs scored (H5)**: a battery's per-question table leaves out the *Runs scored* column when one
  member run scored every question, and its note then reads *"One member run scored each question, so
  each mean score is that run's score; its critical errors count whether that run had a critical
  error."*
- **PDF layout 5 (H5)**: table columns keep their header words whole (§ 8), and a short table stays on
  one page.

**Format version 10** (2026-10-03, with harness 46 in `ai-benchmark.md`) comes from the
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
  document has peers (§ 8; since 2026-10-06 the comparison part names the peers or the comparison key).
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
from the fixture's single-run subject; the comparison-wide documents' `comparison_*.md` and
`comparison_subset_*.md`; and the comparison-scope prompts' `prompt_*_comparison.txt`. To regenerate every golden after an intended change, set
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
**Downloads** button, the Battery Run Report's **Downloads** (§ 14), the **Open Download Center** of
either report's **AI Reports** tab and the Model Comparison launcher's **Open Download Center** (§ 12) — and,
placed directly, as step 4 of the Model Comparison wizard, *Documents*, where it also manages the
comparison's charts (§ 13).

| Package | Contents | Disclosure | Peers | Formats |
|---|---|---|---|---|
| **Internal** | Every available document: pack documents, the run report, the tool-call log, run diagnostics | Full | Named | PDF, Word and Markdown (PDF, Word and Text for the diagnostics) |
| **External** | Executive Summary and Report for AI Researchers and Developers only; internal-only rows are listed but unselectable, with their reason | Summary (Detailed as an option) | Anonymized (Named as an option, with a warning) | PDF |
| **Custom** | Any selection | Per document | Per document | Any, Word included |

The summary line, the ZIP's `MANIFEST.md` and its file name call them *Internal package* and *External package*.
Each home lists its own documents (§ 1a). Opened on a run, the dialog lists the run's files and the
run's own run-completion documents (§ 11), `report-documents?subject=run:<id>&origin=runCompletion`
(§ 9), under both packages; while they are still being written, a notice says so and the list reloads
when they are done (§ 11). A Report Pack document about the run is not listed there, nor is a document
whose subject is a group or a battery result that includes the run: a group's Report Pack documents are
reached from the comparison launcher, and a battery result's from the battery run. **A battery member**
gets an info line above the list, *"This run is a member of battery run #<id>. Its AI-written documents
are in the battery run's downloads."*, with a `.btn-ghost` **Open battery run downloads** that switches
the same Download Center to the battery run's context (§ 14).

**The comparison pointer.** In a run or a battery context the panel makes one more request,
`subject=<run:<id> | battery:<id>>&origin=reportPack`, and counts the comparison documents about the
subject. When there are any, an info line sits above the list — *"<N> comparison documents compare this
<run | battery run> with other models. They are kept with their comparisons."*, or *"1 comparison
document compares this <run | battery run> with other models. It is kept with its comparison."* — with a
`.btn-ghost` **Open comparison documents**, built like **Open battery run downloads**, which switches the
same dialog to a library context of `subject` scope with nothing preselected. No count, and a failed
count request, show nothing. A battery member with comparison documents shows both pointers.

Opened on a list of documents (`DownloadCenterDocumentsContext`, by id) or on a library
(`DownloadCenterLibraryContext`), the panel may take a `title` and a `subtitle` in place of its own. A
library context lists **only Report Pack documents**, with one request, and never a run report of a
subject run. Its `scope` is one of:

- `{ kind: 'comparison', comparisonId, name, entryKeys }` — this comparison's Report Pack documents:
  those of its number, `comparisonId=<N>&origin=reportPack`, and beside them those listed by its entry
  keys, `comparison=<entry keys>&origin=reportPack` (§ 9), that carry no number, each once, newest
  first (a failure of the second request lists the numbered documents alone); before the comparison
  has a number, the entry-key request alone. Only the comparison's name changing keeps the rows and
  choices;
- `{ kind: 'all' }` — every Report Pack document, `origin=reportPack&take=500`;
- `{ kind: 'subject', subjectKey, label }` (`DownloadCenterSubjectScope`) — every comparison document
  about one run or battery run, whichever comparison wrote it, `subject=<key>&origin=reportPack`. The
  dialog titles it *Comparison documents*, with the subtitle *About <label>* (*About run #42 · <suite> ·
  <model>*, *About battery run #9 · <battery> · <model>*); with none it says *"No comparison documents
  have been written about it."*

`preselect` (`DownloadCenterPreselect`) is `'all'` (every row starts as the package chooses it),
`'none'` (nothing starts chosen) or `{ ids }` (a row starts chosen, as the package chooses it, only when
its document id is listed), in `replaceRows` and `addRows` alike. The launcher opens `all` with nothing
preselected and the title *Comparison reports*; the pointer opens `subject` with nothing preselected; the
wizard's step 4 shows `comparison` with the documents of the last job that finished in this wizard
session preselected, else every row (§ 12). Packages, disclosure levels, naming, formats and the ZIP are
the same in every context.

**Include member runs** (2026-10-06). A battery context (§ 14) lists the battery's Markdown analysis
report and its battery-completion documents, `subject=battery:<id>&origin=batteryCompletion`, and offers
in the list header, beside the selection line, a checkbox **Include member runs** with a click-mode info
tip: *"Lists every member run's report, tool-call log and diagnostics beside the battery's own
documents, so the whole battery downloads at once."*

- **Its default.** It is checked every time a battery context opens, and never remembered; unchecked, it
  holds for that opening only.
- **The members.** On opening, the panel asks for the battery run (`GET …/batteries/runs/{id}`) and takes
  its members that are not superseded and whose run is not Deleted, ordered by suite index, then round.
  A member that is not usable is listed too, each of its rows noted *"Not used in the battery's
  statistics."*, because a failed member's files are what an analysis of the failure needs. While the
  members are being listed the panel shows *"Listing the member runs…"* and **Download** is
  unavailable.
- **The rows.** Each member gets the rows a run's own Download Center lists, built by the same function:
  *Run report* (PDF, Word, Markdown, HTML), *Tool-call log* (PDF, Word, Markdown) and *Run diagnostics*
  (PDF, Word, Text), keyed `report:<runId>`, `log:<runId>` and `diag:<runId>`. A row's suite is the
  member's suite, its subject the battery's model, and its detail line *"<suite> · round <r> · run
  #<id>"*. The *Suite* and *Document* filters and the card list's paging apply to them as to any row.
- **Packages.** The rows are added like any run file, so the Internal package selects them, the External
  package lists them internal-only and unselectable, and Custom applies its remembered per-category
  choices.
- **Diagnostics.** The *Run diagnostics* rows are listed only when the context carries
  `memberDiagnosticsText`, a callback that builds a member's diagnostics text from its run detail exactly
  as that run's own Download Center captures it (`BenchmarkComponent.runDiagnosticsTextFor`). The
  Overseer shell supplies it to every battery Download Center it opens: the Battery Run Report's
  **Downloads**, its **AI Reports** tab and **Open battery run downloads**. At preparation, each selected
  member's run detail is fetched and its text captured once per download; nothing is fetched for a row
  that is not selected. Without the callback a member gets the report and tool-call log rows only.
- **Unchecking** removes the member rows and their choices; checking again re-adds them, preset again,
  from the members already listed. A failed member request shows *"The member runs could not be listed;
  the battery's own documents are."*; unchecking and checking again retries it.

A large battery's member rows add up: each tool-call log can run to several megabytes, and the
preparation overlay counts *Preparing k of n*. Unchecking the option, or narrowing by the *Suite* or
*Document* filter, keeps a download quick.

**The documents list** shows each document as a full-width card (the `frontend_ui_controls` skill
§ 8h). The list's header holds the *Documents* heading — *Documents of Comparison #12 — <name>* while it
lists one numbered comparison's documents (*Documents of Comparison #12* while the name is unknown) —
an (i) button **About document options** that
opens one dialog explaining *Sharing*, *Disclosure* (what each level contains, per document type),
*Peer names*, *Formats* and, in the wizard, *Charts*, and a status line such as *Showing 10 of 23
documents* (*· filtered from 40* while a filter is active), which screen readers announce.

Above the cards, a filter bar:

- **Search** matches the title, the subject, the models, the comparison (*Comparison #12*, *#12* and its
  name), the suite and the writer once typing pauses. Escape clears the text without closing the dialog;
  with the field empty, Escape closes it as usual.
- **Sort by** offers *Newest first* (the default), *Oldest first*, *Document type*, *Title*, *Model
  (A–Z)* (by the first model a document covers; its stored id is still `subject`), *Suite*, *Writer*,
  *Writing cost, highest first* and *Changed since written first*. The choice is remembered in the
  browser, separately from the download settings.
- **Filters** — *Document*; *Scope* (*Whole comparison*, *Model subset*, *One model*); *Comparison*
  (*#12 — <name>*, newest first; never while one comparison's documents are listed, as on step 4);
  *Model* (every model a document covers: a comparison-wide or subset document counts under each of its
  covered models, a per-model document under its subject); *Suite*; *Written by*; *Changes*; *Charts*
  (in the wizard only, § 13); and *Created* (the last 24 hours, 7 days or 30 days) — each open a list of
  options with the number of documents each would leave, counted with the other filters applied. Several
  options of one filter widen the list; several filters narrow it. A filter is offered only while its
  documents hold two values or more of it, or it has a selection. There is no Sharing filter: a
  document's sharing follows the disclosure chosen on its own card.
- **Chips** show each active filter and the search; each removes itself, and **Clear all** removes them
  all but *Show selected only*.
- **The selection line** reads *N selected — M not shown*, with **Show selected only**, **Clear
  selection** and **Select all N** (*Select all N matching* while a filter is active), and, in the
  wizard, **Update charts…** (§ 13). There is no select-all checkbox.

The bar stays at the top of the panel while the list scrolls, where the panel is wide enough.

Each card has the *Include* checkbox top left (a click on the title selects the card too, and a selected
card turns gold), a line with the document type, the *Shareable* or *Internal only* tag and the *Run
changed since this document was written* and *Comparison changed* tags in words, the title, a meta line
— for a document of a numbered comparison first *Comparison #12* and its name, then *2 of 5 models* for
a subset; the date; the subject (per-model documents only), the suite and the writer (a run file's suite
and model) — the actions top right,
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
with reduced motion the ring turns slowly (6 s) with a still arc.

**File names.** A pack document at Full, and every internal-only file (run report, tool-call log,
diagnostics), gets an `_INTERNAL` file-name suffix. Every name ends
`_<summary|detailed|full>_<named|anonymized>[_INTERNAL].<pdf|docx|md|html>`.

**A Report Pack document of a numbered comparison** (§ 15) is named by its comparison
(`BenchmarkPdfFileNames.ComparisonStem` on the server, `comparisonFilePart` in the client), with the kind
slug `executive-summary`, `researcher-report` or `internal-brief`:

| Document | Stem |
|---|---|
| Per-model | `comparison-12_<model slug>_<kind>`, in both namings, since the subject is always named — `comparison-12_gpt-5.6-luna-max_executive-summary_full_named_INTERNAL.pdf` |
| Comparison-wide, named | `comparison-12_<name slug>_<kind>`, the name slug cut at the last hyphen at or before 40 characters |
| Comparison-wide, anonymized, or the name unknown | `comparison-12_<kind>` |
| Subset, named, up to three models | `comparison-12_subset-<covered slug>_<kind>`, the models' slugs joined by `-vs-` and cut as the name slug is (no dangling `-vs`) |
| Subset, anonymized or more than three models | `comparison-12_subset-2-of-5-models-<first 6 hex of CoveredSetKey>_<kind>` |

**Any other document** — a Report Pack row without a comparison number, a run-completion or a
battery-completion document — keeps these names. A document whose subject is one run (`run:<digits>`,
every run-completion document) is named from the run number first, with a `run-<digits>_` prefix, in its
PDF, Word and Download Center names alike — `run-73_…_full_named_INTERNAL.pdf`. A document with peers
adds a **comparison part** after the run prefix, or first for a group subject, so documents of two
comparisons never share a name:

- **1 to 3 peers whose entry keys all parse:** `vs-` and one token per peer in letter order (letter
  length, then ordinal), joined by `-`, then `_` — a token being `run-<id>`, `group-<id>` or
  `battery-run-<id>`. `battery-run-9_vs-battery-run-10_…`, `run-92_vs-run-94-run-95_…`. This form needs
  no comparison key.
- **Otherwise:** `vs-<N>-models-<first 8 characters of ComparisonKey>_`, N being the peer count —
  `run-68_vs-4-models-3f9a0c21_…_summary_named.pdf` — or `vs-<N>-models_` for a legacy row stored without
  a comparison key.
- **No peers:** no comparison part, so a run's or battery run's own documents are named as before.

The peers are read from the stored fact sheet's `peers` array (each `letter` and `entryKey`) on the
server and from the list DTO's `peerLetters` in the client. The client's `reportDocumentFileStem` and the
server's `BenchmarkPdfFileNames.ForReportDocument` build the same name. A group subject has no run prefix. The run report and tool-call log are fetched from the
existing run endpoints and keep the server's file name; their PDFs and Word files are named by the server,
with `_INTERNAL.pdf` and `_INTERNAL.docx`. Run diagnostics are a point-in-time capture, taken **once per
download**: the `.txt`, the `.pdf` and the `.docx` of one download hold the same text and the same capture
time.

**ZIP and manifest.** Several files download as one ZIP, `<stem>_<package>_<yyyyMMdd_HHmmss>.zip`, its
stem built from the **chosen** rows (`downloadZipStem`):

- documents of one numbered comparison: `comparison-12_<name slug>`, or `comparison-12` when any of them
  is chosen anonymized or the name is unknown — the comparison's name only when every chosen document of
  it is named;
- documents of several comparisons, or of one beside documents without a number: `comparison-reports`;
- a run context: the one model the chosen rows are about, else the run's model; a battery context: its
  label, else `battery-run-<id>`;
- other documents without a number: the one subject they are about, else `comparison-reports` for
  Report Pack documents and `reports` for any others.

The ZIP holds a `MANIFEST.md`. Its header lists the package, the packaging time, the file count and
*Comparison* (or *Comparisons*, separated by semicolons): each comparison of the chosen documents as
*Comparison #12 — <name>*, without the name when any chosen document of it is anonymized. Each file's
block lists its name, document id, audience, *Comparison* (*Comparison #12 — <name>*, *Comparison #12* in
an anonymized copy, a dash without one), *Model* (a per-model document's subject, a run file's model) or
*Models* (*all 5* for a comparison-wide document; a subset's names in a named copy and letters, *Model A,
Model B*, in an anonymized one), disclosure, naming, renderer version, creation time, writer, format and
SHA-256 — for a PDF, of its exact bytes, and with a
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
  *Source {first 16 hex of the source hash} · PDF layout 6*, and a classification banner — amber
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
  A document of a numbered comparison opens its facts table with a *Comparison* row and adds
  *· Comparison #12* to a per-model subject line; a comparison-wide document's subject line, title and
  facts are in § 15.
  The Markdown's front matter — the stamp and the *Date*, *Suite*, *Questions*, *Runs* and *Peers* list
  under the title, *Compared with* and *Pricing basis* in place of *Peers* when there are peers — and its
  closing footer — the document ID, version, writer and provenance lines — are
  left out of the PDF and Word files (`BenchmarkReportRenderOptions.IncludeFrontMatter = false` and
  `IncludeDocumentFooter = false`), because the cover prints the same.
- **Every page**: from page 2 a running header with the emblem, *GnollBench · {kind}* and, at its right,
  *Comparison #12 — <name>* (*Comparison #12* in an anonymized copy) kept to one line with an ellipsis,
  or the subject line for a document without a numbered comparison;
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
  none. The image is centered in a frame of its **width share** of the text column (the whole column
  without a chart layout, § 13) unless its height would pass the layout's maximum height share of the
  page's content height (60 % without a layout; 20 % to 90 % with one), in which case it is scaled down to
  that height. It is tagged `SemanticFigure` with the
  chart's alternative text (its title, else *Chart*, when the text is empty), and the caption below it,
  tagged `SemanticCaption`, reads **Figure N.** *Title* — caption, in the table text size. Image and
  caption are kept on one page, and figures are numbered in order of appearance. **Figure rows**
  (*PDF layout 6*): two consecutive figures of one row group, with nothing printed between them, print
  side by side as one `Row` of two `RelativeItem`s sharing the column less a 12 pt gap by their width
  shares, each with its own image and caption, kept on one page. A figure directly after a heading is
  kept with it: a run of headings moves to the next page with its first paragraph or figure when the
  group does not fit.
- **Closing section** (*PDF layout 4*, 2026-09-30): the last `##` section of a document with at least two
  (*Evaluation terms*) is kept on one page when its estimated height fits a page
  (`BenchmarkPdfMarkdownComposer.KeptTogetherSectionStart`, an estimate that errs high), so no document
  ends on a page holding one bullet; a taller section flows as before.
- **Header words and short tables** (*PDF layout 5*, 2026-10-04): every column has a minimum width equal
  to the longest whitespace-delimited word of its header (`PdfColumnWeights`, used by
  `PdfColumnLayout`). When the content-sized widths do not fit the text width, right-aligned numeric
  columns keep their width and the text columns shrink toward their header minimums in proportion to
  their excess; only when the header words alone do not fit do they shrink further, so a header word
  breaks mid-word only then. A table of at most 10 body rows (`ShortTableMaxBodyRows`) is kept on one page
  when it fits on one (`PreventPageBreak`), so a four-row table is no longer split.
- **Wide tables** (*PDF layout 6*, 2026-10-06; `BenchmarkPdfMarkdownComposer.TableLayout`, shared with
  Word as `BenchmarkTableLayout`): every column's minimum is its longest word, header and body together,
  and when those do not fit across the text width these measures are taken in order until they do —
  the table text steps down from 9.5 pt by half a point to 8 pt; then the deterministic tables' known
  long headers print short, one at a time, the largest saving first, with a legend line under the table
  (*Assessed band* → *Band*, *Refuted answer sentences* → *Refuted*, *Critical errors* → *Crit.*,
  *Contribution* → *Contrib.*, *Cost per run, graders included* → *Cost/run*, *Critical-error rate* →
  *Crit. rate*; *"Band: Assessed band · Refuted: Refuted answer sentences"*); then a per-question table's
  *Topic* column leaves the grid for a full-width second line of each row (*Topic: …*). Each measure is
  tried at every text size before the next is taken, and the largest size that fits wins; when nothing
  fits, every measure is taken at the smallest size and `PdfColumnWeights` shares out the shortfall, so
  only a token longer than its whole column breaks inside itself. Widths are **estimated** per
  character: an average glyph advance of a little over half an em, scaled by each character's width
  class (narrow glyphs 0.65, round capitals 1.25, *m*, *w*, *M*, *W*, *%*, *@* and the em dash 1.55, a
  full-width glyph 1.85, any other 1), and monospace code by its length.
- **Inline code** keeps the space before and after it: a test measures the gaps around a code span in
  QuestPDF's own layout (review W4), and the composer passes literal text through unchanged, with no
  workaround.
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
  same table layout the PDF uses, with `Autofit` letting Word widen a column to its longest word. A wide
  table takes the PDF's measures: the smaller text size on every run, the short headers with their legend
  in a *Source Line* paragraph below the table, and the moved *Topic* column as a merged second row under
  each body row, kept with it and striped with it.
- **Page 1** mirrors the PDF: the wide logo, the document kind, the title, the subject line, a facts table,
  *Source {first 16 hex} · Word layout 2* and the classification banner; the subject line, the facts table
  and the banner text come from the same document information as the PDF's, so they change with the
  PDF's cover. The source hash follows the PDF's rule, charts included. The table of contents follows
  under the PDF's rule, as a real `TOC` field pre-filled with links to the `##` sections and marked for
  Word to refresh (page numbers included) when the file is opened; Word may ask once to update fields.
- **Every page**: from page 2 a header with the emblem, *GnollBench · {kind}* and, at the right tab, the
  PDF header's text (a comparison heading cut with an ellipsis to the characters half the text width
  holds); a footer
  with the short classification, the source hash and layout version, and *Page X of Y* as `PAGE` and
  `NUMPAGES` fields; for internal documents Word's own *INTERNAL* text watermark, which *Design →
  Watermark → Remove Watermark* removes.
- **Fonts**: Source Sans 3 and Source Code Pro are embedded the way Word's *Embed fonts in the file* does
  (ECMA-376 obfuscated font parts, not subset), so the document looks and edits the same without them
  installed; about 1 MB per file. A reader whose Word blocks embedded fonts sees Calibri and Consolas.
- **Images**: the two logos are PNG (`Overseer/Resources/Word/`), because WebP pictures do not open in
  Word 2019, Word 2021 or LibreOffice. A Markdown image, `![alt](url)`, still prints as `[alt]`.
- **Figures** (*Word layout 2*, 2026-09-30): a figure marker with a chart (§ 13) becomes an inline picture
  in a paragraph of its own, the PNG in its own image part, as wide as its width share of the text
  column with its height capped as in the PDF. Its `DocProperties` carry an id unique in the document
  from 3 (1 and 2 are the logos), the name *Figure N* and the chart's alternative text as the
  description, which Word shows as the picture's alt text. The picture is centered and kept with the
  caption paragraph below it, which reads, as in the PDF, **Figure N.** *Title* — caption, indented to
  the figure's width. A figure row is a borderless table of one row that is not split across pages, the
  column shared between its two cells by the figures' width shares, each cell holding one figure. The
  Word layout version stays 2.
- **Properties**: title, author *GnollBench (Overseer)*, subject, keywords, language `en-US` and the
  stored creation date (never the request time), plus the custom properties *GnollBench Classification*
  and *GnollBench Source SHA-256*. The file opens without *Compatibility Mode*.
- **Limits**: the PDF's — 413 over 6,000,000 characters, canceled with the request, and the diagnostics
  text is never stored or logged.

---

## 9. Endpoints

All endpoints require the `AdminOnly` policy and sit under `api/admin/benchmark`.

### Comparisons (`AdminBenchmarkComparisonsController`)

- `POST /api/admin/benchmark/model-comparisons/identify`: Body `{ runIds, groupIds, batteryRunIds }`. The
  numbered comparison of the selection, created and named when it is new (§ 15); the same selection in any
  order is always the same comparison. 200 with `{ id, name, customName, defaultName, entryCount,
  subjectKind, entryKeys, createdAtUtc, renamedAtUtc }` (`BenchmarkComparisonDto`; `name` is the display
  name, `customName` null while it carries its default). 400 for no body, and for a selection the
  comparison refuses: empty, battery results mixed with runs or groups, or an entry that does not exist.
- `PATCH /api/admin/benchmark/model-comparisons/{id}`: Body `{ name }`. Renames the comparison, trimmed;
  an empty or null name resets it to its default name. 200 with the comparison; 400 for no body or a name
  over 160 characters; 404 for an unknown comparison.
- `GET /api/admin/benchmark/model-comparisons`: Every comparison, newest first, each `{ id, name,
  customName, defaultName, entryCount, subjectKind, documentCount, lastDocumentAtUtc, createdAtUtc }`.

### Report packs (`AdminBenchmarkReportPacksController`)

The request of the preview, the start and the layout preview is
`{ runIds, groupIds, batteryRunIds, pricingBasis, scope, subjectKey, subjectKeys, coveredEntryKeys, audiences[], writerModelConfigurationId, acknowledgeSameProvider, replaceDocumentIds }`:

- `scope` is 1 (`Model`, the default) or 2 (`Comparison`);
- model scope reads `subjectKeys`, the subjects written one after another sharing one computation of the
  comparison, or `subjectKey` alone when that list is empty;
- comparison scope reads `coveredEntryKeys`, empty or absent meaning every entry that is not Excluded;
- `audiences` are numbers (1 Executive Summary, 2 Report for AI Researchers and Developers, 3 Internal
  Brief); `batteryRunIds` names battery results, and a request naming any may name no run or group;
- `replaceDocumentIds` names the written documents the job replaces (§ 15).

- `POST /api/admin/benchmark/report-packs/preview`: The fact sheets and prompts without a model call.
  Model scope: the first subject, its peers, the estimated tokens, cost and `contextWindowShare` per
  document and subject, the same-provider warning and any refusal. A subject with no peer answers 200
  with the subject's fields, `refusal` set to `PeerlessReportRefusal` (§ 1a) and no estimates. Otherwise
  the preview also carries `writtenDocuments`: the Report Pack documents already stored for this
  comparison and its first subject (§ 5), the newest per audience, in audience order, each
  `{ audience, documentId, createdAtUtc, writerDisplayName, subjectKey, status, writerProvider,
  writerModelId, writerThinkingLevel, durationMs, costUsd }` (`BenchmarkReportPackWrittenDocumentDto`).
  Comparison scope: the covered models with their letters, this covered set's `writtenDocuments`, and
  `otherModelSets` (every other covered set of the comparison with documents, the comparison-wide set
  first, then the newest first). Both scopes add `scope`, `comparisonId` and `comparisonName` (null before
  the comparison is identified; the preview never creates one), `comparisonEntryCount`,
  `coversAllEntries`, `coveredSetKey`, `coveredModels`, `subjectDocuments` (each subject's or covered
  model's per-model documents) and `writerContextWindowTokens`. A refusal of comparison scope —
  a covered entry outside the comparison or Excluded, fewer than 2 or more than 12 covered models, a
  writer that is a covered model, a prompt above 90 % of the writer's context window — is answered 200
  in `refusal`.
- `POST /api/admin/benchmark/report-packs`: Start a job. Returns 202 `{ jobId }`. The job's view carries
  `scope` and `comparisonId`, and each document row its `subjectKey` and `subjectLabel` (a model's label,
  or *Comparison #12*, with *· 2 of 5 models* for a subset).
- `POST /api/admin/benchmark/report-packs/layout-preview`: One document laid out with placeholder text,
  as a PDF; multipart, no model call, nothing stored (§ 15).
- `GET /api/admin/benchmark/report-packs/jobs/{jobId}`: Job progress, per document.
- `GET /api/admin/benchmark/report-packs/jobs/active`: The running job, or 204.
- `POST /api/admin/benchmark/report-packs/jobs/{jobId}/cancel`: Cancel the job.

The start's refusals in model scope, each subject checked in turn, in the order they are checked:

1. Battery results mixed with runs or groups — 400, *"A comparison holds either battery results or runs
   and analysis groups."* The preview refuses the mix the same way.
2. An unknown entry, or an Excluded subject — 400.
3. A subject with no peer (`HasPeers`, § 1a) — 400, *"A comparison report compares one model with at
   least one other. To write a run's or a battery run's own reports, use the AI Reports tab of its
   report."*
4. A writer that is invalid, disabled, keyless, not of the Benchmark role, or refused by the endpoint
   policy — 400.
5. A writer that is a subject's own model — 400.
6. No audience — 400.
7. A document whose prompt is above 90 % of the writer's context window — 400 (§ 15).
8. An id in `replaceDocumentIds` that does not exist, or is not a requested Report Pack document of this
   comparison and of these subjects — 400.
9. A requested document already written for this comparison and subject (§ 5) and not named in
   `replaceDocumentIds` — 409 `{ error }`, *"The <document name> about <subject label> is already written
   for this comparison. Delete it in step 4 to write it again."*
10. The spend cap — 429.
11. A same-provider writer without `acknowledgeSameProvider` — 409, with the warning.
12. A job already running, or a run-completion or battery-completion job waiting for the slot — 409,
    with that job.

In comparison scope: battery results mixed with runs or groups — 400; a comparison that cannot be
computed — 400; a covered entry that is not in the comparison or is Excluded — 400; fewer than 2 or more
than 12 covered models — 409; an unusable writer — 400; a writer that is a covered model's
configuration (provider, model id and thinking level) — 400; no audience — 400; a prompt above 90 % of
the writer's context window — 400; an id in `replaceDocumentIds` this job does not write again — 400; a
requested document already written for this covered set and not named in `replaceDocumentIds` — 409
(*"The <document name> of these models is already written for this comparison. Delete it, or rewrite it
to replace it."*); the spend cap — 429; a writer sharing a covered model's provider without
`acknowledgeSameProvider` — 409 with the warning naming those models; a job already running — 409.
Either scope numbers the comparison before the job starts (§ 15).

- `POST /api/admin/benchmark/runs/{runId}/report-documents`: Write a finished run's missing
  run-completion documents now (§ 11). Body `{ writerModelConfigurationId, audiences?, acknowledgeSameProvider }`:
  `audiences` names the documents to write (1 Executive Summary, 2 Report for AI Researchers and
  Developers, 3 Internal Improvement Brief); null or empty writes every missing one. The writer is recorded on the run as its report
  writer, replacing an earlier one. Returns 202 `{ runId, status, audiences }` with the status Pending
  and the documents the job will write. Refusals, in order: no body — 400; an unknown run — 404; a run
  that has not finished (Completed, CompletedWithErrors or CompletedWithLimits) with a final synthesis —
  400; a job for the run Pending or Writing — 409; a requested audience that is not a run-completion
  document — 400 (`InvalidAudienceMessage`, *"Only the Executive Summary, the Report for AI Researchers
  and Developers and the Internal Improvement Brief are written for a run."*); a requested document
  already written — 409 (*"The <name> is already written. Delete it first to write it again."*), or, with
  none requested, every one written — 409 (`AllWrittenMessage`, *"This run already has every AI-written
  report. Delete one first to write it again."*); a writer that is unusable or
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
  incomplete — 400; a job for it Pending or Writing — 409; an audience other than the three — 400
  (*"Only the Executive Summary, the Report for AI Researchers and Developers and the Internal
  Improvement Brief are written for a battery run."*); a requested document already written, or with
  none requested every one written — 409 (`AllWrittenMessage`, *"This battery run already has every
  AI-written report. Delete one first to write it again."*); a writer that is
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

- `GET /api/admin/benchmark/report-documents?suiteId=&runId=&comparison=&comparisonId=&origin=&subject=&take=`: List documents,
  newest first, without rendered text, each with `runChangedSinceGeneration`,
  `peersChangedSinceGeneration`, `comparisonKey`, `comparisonEntryCount` (the subject and its peers; a
  group counts once), `peerCount`, `pricingBasis` (`AsRun` or `Current`), `peerLetters` (each peer's entry
  key and its letter, from the fact sheet; for a comparison-wide document every covered model's), the
  numbered comparison's `comparisonId` and `comparisonName` (its display name now), `scope`,
  `coversAllEntries`, `coveredSetKey`, `comparisonModelCount` (comparison scope: the comparison's
  non-excluded entries when the document was written), `coveredModels` (`{ entryKey, label, provider,
  letter }`: the subject for model scope, every covered model for comparison scope) and, from the chart
  manifest only (§ 13), `chartCount`, `chartFigureKeys` and `chartSettingsHash`. Every filter is optional:
  - `runId` matches a run of the **subject** only, never a peer's run (§ 5): every document with a
    subject row for the run, a group's or a battery result's included. For the documents about the run
    itself use `subject=run:<id>` (below), as the run's Download Center does with
    `origin=runCompletion` (§ 8);
  - `comparison=run:1,run:2,group:4` (or `battery:7,battery:9`) takes the comparison's entry keys, in
    any order, and matches the documents whose `ComparisonKey` they hash to; any other form answers 400
    *The comparison must be a comma-separated list of run:&lt;id&gt; and group:&lt;id&gt; keys, or of
    battery:&lt;id&gt; keys.*, and a key that matches nothing lists nothing. The client sends entry keys
    and never hashes;
  - `comparisonId=12` matches the documents of numbered comparison #12 (§ 15); `comparison` stays for the
    documents written before comparisons were numbered;
  - `origin=reportPack|runCompletion|batteryCompletion|chatConsistencyReport` filters on `Origin`; absent lists every origin,
    and any other value is a 400;
  - `subject=` takes **one** entry key (`run:<id>`, `group:<id>`, `battery:<id>` or
    `chat-consistency:<id>`, a positive id,
    exactly as written) and matches it exactly against each document's subject key; any other form is a
    400. `subject` and `origin` combine. The Download Center lists each home's own documents this way
    (§ 1a, § 8): a run's with `subject=run:<id>&origin=runCompletion`, a battery run's with
    `subject=battery:<id>&origin=batteryCompletion`, a chat consistency analysis's with
    `subject=chat-consistency:<id>&origin=chatConsistencyReport` (§ 16), and the comparison documents about either — the
    pointer's count and the `subject` library scope — with `origin=reportPack`;
  - `take` defaults to 200 and is capped at 500.
- `GET /api/admin/benchmark/report-documents/{id}`: Detail: metadata, validation notes and the facts JSON.
- `GET /api/admin/benchmark/report-documents/{id}/render?disclosure=summary|detailed|full&peers=named|anonymized`:
  The rendered Markdown (`text/markdown; charset=utf-8`), deterministic, with no model call; 400 for a
  refused combination.
- `GET /api/admin/benchmark/report-documents/{id}/render/pdf?disclosure=&peers=&paper=a4|letter&inline=`: The same
  document as a PDF (`application/pdf`). A Report Pack document of a numbered comparison is named
  `comparison-<N>_…_<disclosure>_<peers>[_INTERNAL].pdf` (§ 8); any other
  `[run-<id>_][vs-<comparison>_]<title>_<disclosure>_<peers>[_INTERNAL].pdf` (the prefix for a `run:<id>`
  subject and the comparison part for a document with peers, § 8; a `battery:<id>` subject takes
  `battery-run-<id>_` in place of `run-<id>_`, § 14). The document's charts of the requested naming are
  drawn in it, placed by its chart layout (§ 13); the same refusals as `render`, 400 for another `paper`,
  413 over the size limit. Without a comparison number, a Report for AI Researchers and Developers is named
  `[run-<id>_][vs-<comparison>_]<title without its "— <document name>" ending>_Researcher_Report_<disclosure>_<peers>[_INTERNAL].pdf`,
  whether the stored title ends in the current name or the legacy *Technical Report*. With
  `inline=true` the response carries `Content-Disposition: inline` with the same file name, so a
  browser tab shows the PDF rather than saving it; the PDF viewer's *Open in new tab* uses it (§ 11).
- `GET /api/admin/benchmark/report-documents/{id}/render/docx?disclosure=&peers=&paper=a4|letter`: The
  same document as Word
  (`application/vnd.openxmlformats-officedocument.wordprocessingml.document`), named
  as the PDF is with `.docx`, with its charts drawn and placed as for the PDF and the PDF endpoint's
  refusals.
- `DELETE /api/admin/benchmark/report-documents/{id}`: Delete a document; its run rows cascade, and its
  chart folder is removed (a folder that cannot be removed is logged and never fails the delete).
  Deleting a run-completion document also settles its run's status (§ 11), and a battery-completion
  document its battery run's (§ 14); unlike the run and battery endpoints, this one does not refuse
  while the documents are being written.
- `PUT /api/admin/benchmark/report-documents/{id}/charts`: Replace the document's whole chart set and its
  layout (§ 13). Body `{ charts: [{ figureKey, naming, title, caption, altText, settingsHash, pngBase64 }],
  layout? }`, at most 40,000,000 bytes (`[RequestSizeLimit]`); a body without `layout` stores none.
  200 `{ documentId, chartCount, figureKeys, settingsHash }`;
  400 `{ error }` for a stand-alone document (*"This document has no peers; charts are drawn only for
  documents that compare models."*; never a comparison-wide one), when chart storage is not configured
  (*"Chart storage is not configured. Set Benchmark:ReportPack:ChartsDataLocation to an absolute folder."*),
  for any chart the validation refuses and for an invalid layout; 404 for an unknown document.
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

### Chat consistency report documents (`AdminChatConsistencyController`)

The estimate, write, job and cancel routes of a chat consistency analysis's documents are under
`/api/admin/benchmark/chat-consistency/analyses/{id}/report-documents` (§ 16).

---

## 10. What a Report Pack Does Not Do

- It changes no score, index, grading prompt or comparability key, and moves neither `HarnessVersion`
  nor `ScoringMethodVersion`.
- A per-model document runs no significance test and states none. A comparison-wide document cites the
  paired tests the wizard's own paired-test service computes for its covered models (§ 15); no other
  test is run.
- It never reads the live suite: question and rubric text come from the subject's answer rows.
- It never re-writes a stored document. A changed run is flagged, not re-generated; generate a new pack
  if the old one is out of date — step 3's *Rewrite* replaces the old document of that type once the new
  one is stored, since a comparison holds one per subject (or covered set) and type (§§ 5, 15). A run's
  run-completion documents are written again only after they are
  deleted (§ 11). A document's charts can be replaced or removed at any time (§ 13), which changes its
  PDF and Word copies but not the stored row, and involves no model call.

---

## 11. Run-Completion Documents

A run can name a **report writer** when it is launched. Once the run is scored, that writer writes the
run's **Executive Summary**, **Report for AI Researchers and Developers** and **Internal Improvement
Brief** once, in the stand-alone form (§ 1), and stores them as ordinary `BenchmarkReportDocument` rows
with `Origin = RunCompletion` and the
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
`RerunFinalSynthesisAsync` never calls it. Because of the last-but-one condition, a run that already has
some run-completion documents — a run written before the Internal Improvement Brief became the third,
for instance — never has its missing one written automatically; its **AI Reports** tab offers it.

**The job.** One job has one writer and writes the requested documents the run is missing, in
`BenchmarkRunReportDocumentService.Audiences` order — the Executive Summary, the Report for AI
Researchers and Developers, the Internal Improvement Brief; an automatic job requests every missing one
(`audiences: null`), so it writes all three. Documents with different writers are separate jobs, one
after the other. `BenchmarkRunReportDocumentService` keeps a per-run, in-memory registry of
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
the message names them (*"Canceled while writing. The Executive Summary was written and is kept."*,
*"… The Executive Summary and the Report for AI Researchers and Developers were written and are kept."*,
with three as *"The A, the B and the C were written and are kept."*, or *"… Nothing was written."*).
Tokens already used are still charged. A canceled job is always Canceled,
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

- **The documents.** All three are listed, written or not: a written one with *Written* or *Written with
  warnings*, a line of meta — writer, date, duration, cost and *same provider, acknowledged* when the
  document records the acknowledgment — the *Run changed since this document was written* tag when
  flagged, **View** and a **Delete** icon button; a missing one marked *Not written*. A status line above
  says the job state (*Waiting for the report writer*, *Writing…*, *Failed: …*, *Skipped: …*, the
  cancellation message), with **Show Progress** while the run's documents are Pending or Writing, the
  automatic job's included. A *Downloads* notice with **Open Download Center** opens the Download Center
  on the run, and focus returns to the button when it closes.
- **The write panel**, while a finished run lacks a document: one checkbox per missing document (the
  button reads **Write Report** for one and **Write Reports** for two or more; a written one is named as
  already written), the report writer picker with an (i) button that opens the per-document advice (§ 4)
  — each of the three documents, then *Every document* — as a modal *Choosing a report writer* dialog,
  and a live cost estimate from the estimate endpoint, shown as an *Estimated cost* panel with the total
  and, for two or more documents, the cost of each. The picker starts on the run's own writer, else on the launcher's
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
  for the Executive Summary; the Internal Improvement Brief opens at *Full*, its only level). An (i) button after the tabs opens a modal that explains what each offered
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
(1 job ahead)"*) and then appends *"Reports written: 3 documents, 1m 48s."*, and the completion chime
waits for the stage to end. The run itself ends Completed when scoring ends. The full description —
roster row, stat cell, cost panel rows, diagnostics block — is `ai-benchmark.md` § 1, *Run Progress
Dialog*.

**In the Download Center.** Opened on a run, the dialog asks for the run's report job
(`GET …/runs/{runId}/report-documents/job`) beside the run's own documents
(`subject=run:<id>&origin=runCompletion`, § 8).
While the job's phase is not *Finished* it shows, above the table, *"The AI-written reports of this run
are being written (<phase>). They appear here when they are done."* — the phase reads *waiting for the
report writer*, *preparing the fact sheet*, *writing* or *finishing* — and asks again every 5 s
(`DOWNLOAD_CENTER_REPORT_JOB_POLL_MS`); a failed poll is skipped. When the job answers *Finished*, or
204 once it is gone, the dialog reloads the run's documents and drops the notice. A job already
finished, no job, or a failed first request shows no notice. Closing the dialog, or opening it on
another subject, stops the poll. A battery member is launched without a writer, so its Download Center
shows the battery pointer of § 8 instead, and **Open battery run downloads** opens the battery run's
documents (§ 14).

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
reports that compare the models of this comparison."*) and *4. Documents* (*"View, chart,
download and delete this comparison's documents"*). Steps 3 and 4 are reachable once a comparison
exists; step 3 also needs **two** entries that are not Excluded (`hasComparisonPeers`), because a
comparison report needs a subject and a peer (§ 1a). Otherwise it is `aria-disabled` with a visually
hidden reason — with one comparable entry *"A comparison report compares one model with at least one
other. Add another model on step 1, or write a run's or battery run's own reports in the AI Reports tab
of its report."*, with none *"Every model in this comparison was measured differently, so none of them
can be the subject of a report."* — and **Next** skips it. Next runs 2 → 3 → 4, and closes the wizard on step 4. Step 2's former **Reports** button and
the Report Pack dialog it opened are gone; **About** and **Recompute** stay on step 2.

**The comparison's number in the header.** After a successful Compare, and whenever the entry set
changes, the wizard calls `identify` (§ 15). The header then shows *Comparison #12 — <name>* under the
wizard's title, followed by an icon-only **Rename comparison** (`.action-btn`, *edit-3*, named *Rename
comparison #12*, with a tooltip). It opens a nested *Rename comparison #12* dialog: a *Name* field of at
most 160 characters with the hint *"Up to 160 characters. Leave it empty to use the default name: <default
name>."*, a **Reset to default** link, **Cancel** and **Save**. The new name reaches the header and steps 3
and 4 at once. A failed identify leaves the header without the number and blocks nothing.

Steps 3 and 4 are mounted on their first visit and afterwards hidden, never destroyed, when another step
is active, so step 3's form, its running job and its polling, and step 4's table page, filters and
selection survive a trip back to step 2. That trip is the loop the steps are built for: set the charts
on step 2, generate on step 3, view on step 4, go back to step 2 to change them, then **Update charts…**
on step 4 and view again (§ 13). While charts are being drawn and uploaded, the wizard's close controls
are disabled and Escape is refused, as during an export, even a repeated Escape, which the browser would
otherwise let through.

**Step 3, Reports** (`app-report-pack-panel`, `report-pack/report-pack-panel.component.*`), headed
*Reports*, uses `app-run-report-frame` in its **sidebar** layout: a resizable sidebar with the form, then
the main area with the job, in reading and tab order. From 60rem of body width the sidebar is 20–32rem
wide, at most 40 % of the body, 24rem by default, set by the `app-pane-resizer` between the two (drag, or
Left and Right on it); the width is kept under `sidebarWidth` in
`localStorage['overseer.benchmark.reportPack']`. Below 60rem the two stack, the sidebar first. Each
column scrolls on its own.

Under the step heading, the subtitle names the suite, the number of models and how many are Excluded,
and a lead paragraph says where the documents live: *"These reports are kept with the comparison: step 4
lists them, and so does Comparison reports on the Model Comparison tab. A run's or battery run's own
reports are written in the AI Reports tab of its report."*

- **Sidebar**, *New report pack*, in order:
  - **Document scope**: a `.gh-tabs-segmented` pair, *Whole comparison* (with the visible note
    *Recommended*) and *One model at a time*, remembered as `documentScope` in `localStorage['overseer.benchmark.reportPack']`, with an info
    tip: *"Per-model documents restate the comparison from one model's side. Use them only when one
    model's document must be shared on its own."*
  - **Models**: `app-model-multi-picker` (`frontend_ui_controls` § 4e-3) over the comparison's entries
    that are not Excluded, with no price or parallel badge and no chips; each option is the entry's model
    name with its thinking-level, reasoning-mode and provider badges, keyed by entry key, and two options
    that would look the same each name their source (*Battery run #10*). Under *Whole comparison* every
    model starts chosen (2 to 12; with more than 12, the 12 of highest Intelligence Index, and a note says
    why); under *One model at a time* the highest-Index model starts chosen (at least one). The choice is
    component state, reset when the comparison changes. One line under the picker says what Generate
    writes: *"Writes the comparison-wide documents."*, *"Writes documents for 3 of 5 models — leaves out
    Claude 5 Opus (high), Gemini 3.8 Pro (high)."* (naming only what is left out, with its source where
    two models share a name), or the per-model documents of the chosen models. Once the preview answers,
    under *Whole comparison* a legend follows: a boxed group titled *Letters in the anonymized copies*
    whose `<dl>` pairs each letter (a small gold tile, read as *Letter A*) with the model's label, in a
    grid of 13 rem columns. It is not added to the picker's description, which it would make long.
  - **Documents of this comparison** (`app-comparison-documents-status`): under *Whole comparison*,
    three rows, one per document type, for the chosen set — the comparison-wide documents while every
    model is chosen, else that subset's — and below them a closed *Other model sets (n)* disclosure with
    every other covered set that has documents (*"GPT-5.6 Luna (max), GPT-6.1 Sol (medium) — 2 of 5
    models"*), each with its rows and a **Choose these models** link that sets the picker to that set
    (`aria-disabled` with its reason when a model of the set is Excluded now, or the set is outside 2 to
    12). Under *One model at a time*, one group per chosen model, one row per document type. Each row
    shows a *Written* / *Written with warnings* / *Not written* tag, *Comparison changed since written*
    when flagged, and for a written document *by <writer> (<provider>; <thinking level>)*, the time, the
    duration, the cost and *Charts: 3* (figures) or *No charts*, with icon-only **View** (*eye*, the PDF viewer) and
    **Delete** (*trash*, `.action-btn-danger`, a nested confirmation). Its checkbox reads *Write* for a
    document not written and *Rewrite — replaces the current document* for a written one; not-written
    rows start checked, written ones unchecked. The other sets' rows have no checkbox. The list follows
    the preview, asked 300 ms after the last change of scope, models, checks or writer, an answer for an
    older choice dropped.
  - **Charts in PDF and Word**: the chart picker with its per-figure width select (no visible caption;
    beside each figure, and under its title below a 20 rem picker width) and its *Layout* disclosure
    (§ 13), 1 rem below the figures; on screen,
    the print advisory when a document type's theme *As in step 2* would print badly and, while the
    `report-charts-location-missing` alert is present, *"Chart storage is not configured; documents will
    be written without charts."*
  - **Report writer**, with the dialog-mode info tip *Choosing a report writer*
    (`run-ai-reports/report-writer-advice.ts`, shared with the run report's **AI Reports** tab, with an
    Internal Brief entry) and the *How the graders work* link; the refusal or the amber same-provider
    warning (*Writer from a chosen model's provider*).
  - The *Estimated cost* panel (`.gh-estimate-panel`, shared with the AI Reports tab), summing the
    checked rows, with an amber warning when a checked document's prompt would fill 70 % or more of the
    writer's context window (refused from 90 %, § 15).
  - **Preview layout** and **Generate**, two gold `.btn-gh` buttons in one action row at the bottom of
    the sidebar, Generate last. At the default sidebar width they stack, Preview layout above Generate,
    both full width; from a sidebar about 34 rem wide they sit side by side.
  - **Generate** (*zap*), which sends `scope`, `coveredEntryKeys` (whole comparison) or `subjectKeys`
    (the chosen models with a checked row), the checked document types and `replaceDocumentIds` (the
    checked written rows). It is unavailable, with its reason under it, while a job runs, with fewer than
    two (or more than twelve) models under *Whole comparison* or none under *One model at a time*, with
    no row checked, when the checked document types differ between the chosen models (*"One job writes
    the same documents for every model it covers. …"*), without a writer, while the preview is pending,
    and on a refusal. When every listed document is written and none is checked, it stays focusable and
    `aria-disabled` with *"Every document listed is already written. Check Rewrite on one to replace it,
    or delete it."* A 409 from the start — a document written in the meantime (§ 9) — shows its `error`
    as any other start error.
- **Preview layout** (*eye*, and a *chevron* that points up while open) opens a popover of the three
  document types (`frontend_ui_controls` § 4f), each *Executive Summary · 2 charts*, offered whether or
  not it is checked under *Documents*. Focus moves to the type the chart picker shows, or the first one
  available; Escape closes the popover only, and focus returns to the trigger. Choosing a type composes
  its selected figures as the documents would carry them and opens the server's layout preview PDF
  (§ 15) in `app-pdf-viewer-dialog`, titled *Layout preview — Executive Summary*, on the remembered
  paper; per-model, it previews the first chosen model. When the viewer closes, focus returns to Preview
  layout. A figure that cannot be drawn is listed under the action row (*Not drawn: …*). A type with no
  chosen chart is `aria-disabled` in the popover with *"No chart is chosen for the …"*. The trigger
  itself is `aria-disabled`, opens nothing and shows its reason under the row when the charts cannot be
  drawn, chart storage is not configured, the comparison has no number yet or too few models are chosen.
- **Same-provider writer**: Generate opens a nested *Same-Provider Report Writer* confirmation, **Write
  Anyway**, on every write, and only then sends `acknowledgeSameProvider: true`. Nothing is remembered.
- **Main area**, *Report pack progress*: a status line that changes with the job's phase; while a job
  runs, the stage rail (*Queued*, *Preparing*, one stage per document, *Done*); a stat strip (elapsed,
  writer, model calls, tokens, cost, estimate) that stays after the job finishes; one row per document
  (preceded by a *Model* column in a per-model job) with its status chip, a live duration, its model calls
  and a charts cell — *Charts: attaching…*,
  *Charts: 3* (the figures attached; the diagnostics add *(6 images, named and anonymized)* where the
  two counts differ), *Charts failed — retry* (a button that tries again) or *Charts: none*; and *Log and
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
context of this comparison's Report Pack documents — by its number, `comparisonId=<N>&origin=reportPack`,
with the documents written before comparisons were numbered by its entry keys (§§ 8, 9) — and nothing
else: no run report of a subject run. Its heading reads *Documents of Comparison #12 — <name>*, and it
has no *Comparison* facet. **Preselection:** the wizard records the document ids of
the last step 3 job that finished in this wizard session (its `jobFinished` output's
`documents[].documentId`); while they belong to the comparison on step 1 — the same entry set — step 4
starts with only those chosen (`preselect: { ids }`), otherwise with every document chosen, and the
context is rebuilt when the ids change. Above the panel, the step prints the note *"Run reports, and
each run's or battery run's own AI reports, are in that report's Downloads."*
(`COMPARISON_DOCUMENTS_NOTE`), which the context also carries as its subtitle; the panel placed directly
prints no context title or subtitle, since only the dialog wrapper does. Run-completion and battery-completion documents stay in their own report (§ 1a). A document belongs to a comparison by its number, or,
written before comparisons were numbered, when the comparison has the same set of entries (§ 5), so
changing *Prices* keeps the list, and adding or removing a model empties it. The wizard
lends the panel its chart actions (§ 13): the *Charts* option and filter, **Update charts…** and each
card's **More actions**. The list reloads when a job finishes and when a document is charted.

**The Model Comparison launcher** (Admin → GnollBench → Model Comparison) leads with the action: a hero
card with *Cross-model comparison*, its lead and **Open Comparison Wizard** (the page's only `.btn-gh`,
*compass* glyph), then *How the comparison works* — the four wizard steps and the like-for-like note —
in a disclosure that is open on the first visit and afterwards as the operator left it
(`localStorage['overseer.benchmark.modelComparison.launcher']`). Below the hero come the *Last
comparison* card and the **Comparison reports** library.

**The *Last comparison* card** (`comparison-tab/comparison-summary-card/`) is a full-width row of the
launcher between the hero and the library, built on the shared `.bm-summary-card` styles; it replaces
the hero's inline *Last comparison* read-out. It shows the comparison last computed in this browser,
kept in `localStorage['overseer.benchmark.modelComparison.last']` (`comparison-tab/last-comparison.ts`,
record version 1, at most 64 entries, each exclusion explanation cut at 240 characters). The wizard
records it when a comparison is computed and numbered — its `comparisonIdentified` output, after
`POST model-comparisons/identify` answers, goes to `BenchmarkComparisonState.recordLastComparison` — and
again when a recompute of the same entry set brings new figures or the comparison is renamed. The card
reads *Last comparison*, then *Comparison #N · <name>* and a meta line (*Runs and groups* or *Battery
results*, the battery or suite, the computed time, *remembered in this browser*), with these facts:

- **Charted** — *2 of 3 entries*, and *· 1 excluded* when some are;
- **Pricing** — *Catalog prices* or *As run*, with the basis label beneath;
- **Report documents** — the count of the comparison's Report Pack documents and the latest one's date,
  or *None yet*, from `GET model-comparisons` (`listComparisons()`), fetched when the tab is shown and
  again after the wizard closes; left out while the count is unknown or its request failed.

Its table lists every entry, charted ones by Intelligence Index, highest first, then the excluded ones:
*Model* (name, provider and thinking badges), *Intelligence Index* with its interval, *Median model
time*, *TTFT P50*, *Candidate cost / question* and *Status* (*Charted*, or *Excluded* with its
explanation in an info tip). **Open in wizard** (`.btn-ghost`) applies the record's entries and pricing
basis to the selection (`applyComparisonEntries`; a suite scope that would prune one of them is cleared)
and opens the wizard on step 1. With no record — another browser, private browsing, cleared storage —
there is no card; the next Compare brings it back. The server keeps nothing for it.

**Comparison reports** (`app-report-documents-launcher`, `report-pack/report-documents-launcher.component.*`) sums up
every Report Pack document in one line — *"5 report documents from 2 comparisons · the latest written
…"*, comparisons counted by their number (a document without one by the number another document of its
comparison key carries, else by that key), or *"No reports yet. Reports written on the comparison
wizard's Reports step appear here."* — with an
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
with peers can have charts — a comparison-wide document always covers two or more models, so it always
can; a stand-alone document (every run-completion document) has none. Each chart is **composed for a
target size in points** (below), and the server places it by the document's **chart layout**: its width
share of the text column, rows of two, and a height cap.

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

Several figures at one anchor appear in the order of the first table. A comparison-wide document has its
own anchors (§ 15); the picker shows each figure's section for the scope being written. A chat
consistency document has four figures of its own, `cc1-quality` … `cc4-timeline`, kept in
`BenchmarkReportChartPlacement.ChatConsistencyFigureKeys` apart from `FigureKeys` (`AllFigureKeys`
joins both); they are not chosen in a picker (§ 16).

**Markers.** The renderer writes a line `[[figure:<key>]]`, with a blank line before and after, at each
anchor for each chart it is given (`BenchmarkReportRenderOptions.Charts`), and only for a document with
peers — or for a chat consistency document, which has no peers and draws its own figures. The PDF and Word renderers draw the chart there (§ 8: `SemanticFigure` with the alternative text
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

On step 3 each figure also has a **Width** (*Full column*, *Two-thirds column*, *Half column*), and each
document type a closed ***Layout*** disclosure under its figure list:

| Setting | Values | Default |
|---|---|---|
| Bar orientation | *As in step 2* / *Vertical* / *Horizontal* | *As in step 2* |
| Side by side | consecutive *Half column* figures in one section form a row of two | on |
| Label size | 7, 7.5, 8, 8.5, 9, 10 pt | 8 pt |
| Maximum height | 40 / 50 / 60 % of the page | 60 % |
| Heading inside the chart | *None — the caption names it* / *Title* / *Title and badges* | None |
| GnollBench logo | on / off (step 2's variant and height) | off |
| Theme | *Light, for print* / *As in step 2* | *Light, for print* |

A width or label size that would not fit (below) stays offered, `aria-disabled` and marked *(does not
fit)*, its reason listed in the disclosure; choosing it keeps the current value and says why. The
disclosure sits 1 rem below the figures; **Preview layout** is beside **Generate** (§ 12).

The defaults are the Executive Summary's Intelligence and Intelligence against cost; all seven for the
Report for AI Researchers and Developers; and Intelligence, Speed and Cost for the Internal Improvement
Brief. The last selection and every document type's layout are remembered per browser in
`localStorage['overseer.benchmark.reportCharts']` (**version 2**, `{ selection, layout }`); a stored
version 1 keeps its selection and takes the default layout.

### How a chart is drawn

`composeReportChart` in the wizard composes one figure off-screen through the same export pipeline as
step 2's downloads, for a **target width and label size in points** (`documentChartLayout` and
`documentFigureLayout` in `report-pack/report-charts.ts`):

```
pxWidth     = round(widthPt × 300 / 72)                   // 300 dpi
layoutWidth = widthPt × BASE_LABEL_PX / labelPt           // BASE_LABEL_PX = 11, the bar label size in layout px
textScale   = (pxWidth / layoutWidth) / min(pxWidth / 960, pxHeight / 540)
```

- **The width** is the figure's share of the text column. Charts are composed for the A4 column,
  481.9 pt (US Letter's is 498.6 pt), so a chart printed on Letter is never shrunk. At the default 8 pt
  a full-column chart is about 663 layout px wide.
- **The height** follows the width: 16:10 at full column, 4:3 at two thirds and square at half for bars
  and scatters, one step taller for the profile; a figure grows taller where its heading, key and notes
  would leave the plot less than 200 layout px.
- **The minimum width.** A document chart is laid out with a minimum content width of **260** layout px
  (`DOCUMENT_MIN_CONTENT_WIDTH`, 300 with the padding), not the 360 of step 2's exports, so a *Half column*
  chart fits at 7 to 8.5 pt and is refused at 9 pt and above; *Two-thirds column* and *Full column* fit every
  size. A refused width is composed at full column.
- **Orientation**: *As in step 2* takes step 2's choice, whose *Automatic* is resolved at the document
  chart's own layout width (a full-column chart at 8 pt gets horizontal bars, being below the 720 px
  breakpoint), never at the chart on screen.
- **Theme, heading and logo** are baked into the PNG. *Light, for print* replaces step 2's theme,
  background, text, heading and border colors and nothing else; *None* leaves the title, the badges and
  the detail line to the caption. Everything else is **step 2's active setting, used as is**: font,
  weights, colors, the per-family styles, Show, Highlight, the model order and the measures, except
  the text sizes below.
- **The label size holds for every family** (`documentTextStyle`). The bar and scatter families' axis
  text is set to `BASE_LABEL_PX`, so it prints at the document type's *Label size*; their value labels,
  point labels and axis titles keep step 2's proportion to the axis text (rounded to 0.1 px). A bar
  style of axis 15, value 20 and title 16 px draws 11, 14.7 and 11.7. The profile family and the chrome
  are unchanged.
- **The footer.** A document chart carries no footer of its own unless the document type's *Heading
  inside the chart* is *Title and badges*: the document names the suite and the computation time
  itself. Where it is drawn, and in step 2's own exports, a battery comparison's footer reads *BATTERY*
  and the battery's name instead of *Suite not set*.
- **Value labels** past the value axis's end get room of their own: the bar panel pads the plot by the
  widest label, measured in its font, plus the longest whisker's share of the axis, so a label near the
  maximum is not cut.
- Documents print on white paper, so when a document type's theme is *As in step 2* and step 2 uses the
  dark theme, or a transparent background with light text, an **on-screen advisory** says so, beside
  the picker on step 3 and in the Update charts dialog; it changes nothing.

Each chart carries a title (the figure's), a caption (its detail line; a note on the measure where the
document's tables give another, then *"Drawn from the comparison computed {time}."*) and alternative
text (the title, then one clause per plotted model with its value and interval). The measure note is on
the figures that plot speed (Speed, Intelligence against speed, Speed against cost): *"Times are the
mean model time per question; the document's tables give the median."*, or the same with the total
time or the time to first token, and none under the Speed Index; and on the Cost panel: *"Costs are per
battery pass (per suite run for a suite); the document's tables give the cost per question."*
`chartSettingsHash` is the SHA-256 of the canonical JSON of the figure style, the layout (the
column width, 300 dpi, `BASE_LABEL_PX`, `textNormalization: 1`, PNG, and every document type's layout
settings), Show, Highlight, the order, the measures, the pricing basis and the comparison's
`computedAtUtc`; it is stored with the charts, so step 4 can tell charts drawn with the settings on
screen from older ones. `textNormalization` makes charts drawn before the text normalization read as
differing from step 2, which offers **Update charts**.

**The chart layout.** Width, rows and height are applied by the server at render time, from the layout
the publisher sends with the charts (`reportDocumentChartLayout`): each figure's `widthShare` (1, 2/3 or
1/2), a shared `rowGroup` for two consecutive half-width figures in one section while *Side by side* is
on, and `maxHeightShare` (0.4, 0.5 or 0.6). The PDF and Word renderers place figures by it (§ 8).

**Anonymized variants.** Every figure is drawn twice: **named**, as step 2 shows it, and, when the
document has peer letters, **anonymized** (`anonymizeComparisonForSubject`,
`model-comparison/report-chart-anonymize.ts`): the peers relabeled *Model A*… with the letters of **that
document's** fact sheet (`peerLetters` in the document list), their provider and model id removed and
drawn in the neutral gray, other free text naming a peer rewritten or dropped, entries without a letter
dropped, and the subject unchanged and highlighted. A comparison-wide document's variants plot its
covered models only (`anonymizeComparisonForAll` for the anonymized one, § 15). An anonymized render
draws only anonymized images; a missing variant is left out, never replaced by the other.

**The publisher.** `ReportChartPublisher` composes and uploads one document at a time: every selected
figure of the document's type, both variants, drawn by that type's layout, converted to base64 and sent
as the whole set in one `PUT` with the chart layout. It records a failure per document and carries on, can be canceled after the document in flight,
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
`naming`, `file`, `sha256`, `widthPx`, `heightPx`, `title`, `caption` and `altText`, and, when the upload
carried one, `layout`: `{ version: 1, figures: [{ key, widthShare, rowGroup }], maxHeightShare }`
(`BenchmarkReportChartLayout`). The upload's layout is validated (`ValidateLayout`): version 1 only; a
`widthShare` above 0 and at most 1; a `maxHeightShare` from 0.2 to 0.9 or null; a figure of an unknown
key dropped, and of a key given twice the first kept. An absent layout — every manifest written before
it — renders every figure full width on its own row under the 60 % cap; a stored layout that no longer
validates is logged and rendered as absent. **Update charts…** and a rewrite redraw the charts and send
the layout again. Every path is built
from the numeric document id, a known figure key and a fixed naming word, and is checked to lie inside
the folder.

**Atomic replace.** A `PUT` replaces the document's whole set, all or nothing, under a per-document lock:
the files and the manifest are written to a staging folder, the old folder is deleted, and the staging
folder is moved into place in one `Directory.Move`. A render reads the manifest and skips, with a logged
warning, an image that is missing or whose SHA-256 differs from the manifest; **a render never fails
because of its charts**. The list and detail DTOs read `chartCount`, `chartFigureKeys` and
`chartSettingsHash` from the manifest alone, and the PDF and Word endpoints its layout
(`BenchmarkReportRenderService.ReadRenderLayout`).

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
documents**. Why batteries have AI-written documents at all, why the members write none, and why each
kind of document has one home are `ai-benchmark-multi-suite.md` § 7.2 (decisions D2, D3 and D8).

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
and the graders' comments on it. They are chosen, per suite, in this order (harness 54): questions with
a **confirmed** critical error (most rounds first, then the lowest mean); questions where a panel member
raised a critical error that was **not confirmed** — overturned by the claim verifier or unresolved,
either member; questions the **panel disagreed** on (`BenchmarkRunAnswer.PanelDisagreed`, the flag the
*Disagreements* count reads); then the lowest and the highest remaining mean scores, alternately. The two
middle groups are ordered by mean ascending, then question key. Question topics and notes are asked for the questions in detail only, and a
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
`reportDocumentFileStem`), except a Report Pack document of a numbered comparison, which is named
`comparison-<N>_…` (§ 8). A comparison of battery results can have comparison-wide documents too (§ 15):
their questions keep the `S<n>-Q<m>` references, and their cover names the battery.

### Battery-completion documents

A battery run can carry a **report writer** (`BenchmarkBatteryRun.ReportWriterModelConfigurationId`),
chosen with the launcher's *Report Writer* field when the battery is started and checked then against the
tested model, with the same refusal and same-provider warning as a run's (§ 4). The battery run records
`ReportDocumentsStatus` and `ReportDocumentsMessage` with the statuses of § 11 (migration
`AddBatteryReportDocuments`). Its members are launched with no writer.

**When they are written.** `BenchmarkBatteryReportDocumentService.ScheduleIfDue(batteryRunId)` is called
right after the battery run's automatic analysis succeeds — at the end of its drive loop, or when a
repair of a member run or a Continue finishes it (`ai-benchmark-multi-suite.md` §§ 3.4, 3.5) — and
after a manual **Recompute analysis**, and returns at once. It writes when all of
these hold: the battery run has finished; its latest analysis is complete and not stale; it names a
writer; it has no battery-completion document yet; and no job for it is Pending or Writing. The
automatic job writes every missing document — the Executive Summary, the Report for AI Researchers and
Developers and the Internal Improvement Brief (`BenchmarkBatteryReportDocumentService.Audiences`, which
is the run service's list); a battery run that already has some gets the missing ones only on request. Each document
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
(`app-battery-ai-reports`) is the counterpart of a run's: all three documents listed, **View** in the PDF
viewer, **Delete** behind a confirmation, *Write missing reports* with the writer picker (starting on the
battery run's own writer), the cost estimate, the same-provider confirmation and **Show Progress**.

**The battery Download Center.** Opened on a battery run (its `battery` context, from the Battery Run
Report's **Downloads** or its **AI Reports** tab), it lists the battery's Markdown analysis report and
the battery run's own battery-completion documents, `subject=battery:<id>&origin=batteryCompletion` (§ 9),
and shows the *being written* notice while the battery run's job runs, asking every 5 s. The comparison
documents about the battery result are not listed: they are counted, and the pointer's **Open comparison
documents** opens them (§ 8). With **Include member runs**, checked whenever the dialog opens, it also
lists every current member run's report, tool-call log and diagnostics, so one Internal download holds
the whole battery (§ 8). The dialog's subtitle reads *Battery run #N · <battery> · <model>*: the
context's label is *<battery> · <model>*, and the dialog adds the id. It is the **only** Download Center
that lists the battery's own documents: a member run's Download Center lists that run's own documents
(`subject=run:<id>&origin=runCompletion`) and, above them, the pointer *"This run is a member of battery
run #<id>. Its AI-written documents are in the battery run's downloads."* with **Open battery run
downloads**, which switches the same dialog to this `battery` context (§ 8).

---

## 15. Comparisons and Comparison-Wide Documents

A Model Comparison has an identity of its own, **Comparison #N**, and its documents can describe its
models **as equals** rather than one model against its peers. Step 3 of the wizard writes these
**comparison-wide documents** by default (*Whole comparison*, noted *Recommended*); the per-model documents of
§§ 1–14 are the secondary choice, *One model at a time* (§ 12).

Implementation:

| Concern | Type |
|---|---|
| The numbered comparison | `BenchmarkComparison`, `BenchmarkComparisonSubjectKind` (`GnollHackServer.Data/BenchmarkComparison.cs`) |
| Numbering, naming, renaming and listing | `BenchmarkComparisonIdentityService`, `AdminBenchmarkComparisonsController`, `Overseer/Models/BenchmarkComparisonModels.cs` |
| The comparison fact sheet and content | `BenchmarkComparisonReportFacts`, `BenchmarkReportContent.BuildComparison` |
| The comparison-scope prompt | `BenchmarkReportPackPrompt.Comparison.cs` |
| The comparison-scope sections | `BenchmarkReportPackRenderer.Comparison.cs` |
| The layout preview | `BenchmarkReportLayoutPreview`, `POST report-packs/layout-preview` |
| Table layout shared by the PDF and Word | `BenchmarkTableLayout`, `BenchmarkPdfMarkdownComposer.TableLayout` |

### The numbered comparison

`BenchmarkComparison` is one row per **entry set**, keyed by the existing `ComparisonKey`
(`BenchmarkReportComparisonKey.From`, § 5), with a unique index on it. The same runs, analysis groups or
battery results, in any order, on either pricing basis and with any entries Excluded, are always the
same comparison. Its columns: `Id` (shown as *Comparison #Id*, never reused), `ComparisonKey`,
`EntryKeysJson` (runs, then groups, then battery results, each by ascending id), `SubjectKind` (`Runs`
or `Batteries`), `EntryCount`, `DefaultName`, `Name` (the admin's rename, nullable), `CreatedAtUtc`,
`CreatedByUserId` and `RenamedAtUtc`.

- **When it is created.** The wizard calls `POST model-comparisons/identify` after every successful
  **Compare**, and again whenever the entry set changes; a recompute of the same set asks nothing. The
  start of a Report Pack job (`POST report-packs`) numbers the comparison server-side before the job
  runs, so a Report Pack document always belongs to a numbered comparison. The preview, the estimate and
  the layout preview never create one; they report the number only once it exists. A failed identify
  leaves the wizard's header without a number and blocks nothing: step 3 shows the server's refusal at
  **Generate**. The comparison endpoint itself numbers nothing, since it also serves the paired tests,
  the battery leaderboard and other ad-hoc computations.
- **The default name** is computed once, when the comparison is first identified, from the computed
  comparison's entry labels in canonical order: up to three entries *"A vs B vs C"*; four or more
  *"10 models · <battery or suite name>"*, the name left out when the entries share none; at most 160
  characters, cut with an ellipsis. A concurrent insert of the same set loses on the unique index and
  returns the winner.
- **Rename.** `PATCH model-comparisons/{id}` trims the name; an empty or null name resets to the default;
  more than 160 characters is a 400. The display name is `Name ?? DefaultName`, read **at render time**:
  renaming a comparison changes what an old document's PDF or Word copy prints the next time it is
  downloaded, but *Comparison #N* never changes, so the document stays identifiable.
- **No deletion.** There is no endpoint or UI to delete a comparison. Deleting its documents leaves the
  row (the foreign key from the documents is `Restrict`), which keeps the number stable.
- **The startup backfill.** After the comparison-key backfill (§ 5), `Program.cs` runs
  `BenchmarkReportDocumentBackfill.BackfillComparisonsAsync`: every Report Pack row with a comparison key
  and no comparison is linked to the comparison ensured from its own `ComparisonRequestJson`, the input
  that produced its key; an entry set that can no longer be computed (a run deleted since) is still
  numbered, named by its entry keys. A row whose request is unreadable, or names another entry set than
  its key, keeps a null comparison and is logged. Every model-scope Report Pack row also gets its subject
  alone as `CoveredEntryKeysJson` and `CoveredSetKey`. Run- and battery-completion documents are left
  alone. The backfill saves every 200 rows and is idempotent.

### Document scope

`BenchmarkReportDocuments` carries four columns for this:

| Column | Model scope | Comparison scope |
|---|---|---|
| `Scope` (`BenchmarkReportScope`) | `Model` (1, the default; every earlier row) | `Comparison` (2), Report Pack only |
| `ComparisonId` | The numbered comparison; null on run- and battery-completion documents | The numbered comparison |
| `CoveredEntryKeysJson` | The subject alone | The covered entry keys, canonically sorted |
| `CoveredSetKey` | SHA-256 of the subject alone | SHA-256 of the covered set (`BenchmarkReportComparisonKey.ForCoveredSet`), computed as the comparison key is, so a set covering every entry has the comparison's own key |
| `SubjectKey` | `run:` / `group:` / `battery:` entry key | `comparison:<id>` for a comparison-wide document; `comparison:<id>/<first 16 hex of CoveredSetKey>` for a subset |
| Letters | Peers only, per document | Every covered model, A = highest Intelligence Index |
| One document per | `(ComparisonKey, SubjectKey, Audience)` | `(ComparisonId, CoveredSetKey, Audience)` |

An index on `(ComparisonId, Scope, CoveredSetKey, Audience)` serves the lookups; the uniqueness is
enforced by the start endpoint, not by a constraint, as in § 5. The comparison-wide and the subset
documents of one comparison are separate sets: every covered set is its own document set.

**Comparison-wide or subset.** The covered set defaults to every entry that is not Excluded. Whether a
document covers the whole comparison is decided when it is written and stored in its fact sheet as
`coversAllEntries`, with `comparisonEntryCount` (the comparison's non-excluded entries then). It decides
the title, the cover, the file name and the Download Center's *Scope* facet. A later exclusion or
restore does not change what a document was written as; the changed-since-written flags of § 5 tell the
reader when a covered run changed.

### The comparison fact sheet

`BenchmarkComparisonReportFacts.Build` prepares each covered model as the subject of its own per-model
sheet **over the covered models only** (so no sheet names an uncovered entry), then builds one sheet with
`scope: "Comparison"`, `SubjectKind = "Comparison"`, no subject, and every covered model in `peers` and
`models` with its letter. Everything below is computed over the covered models: a subset document reads
as a complete comparison of its models, and its cover and *Setup and method* count the comparison's
other models without naming them.

- **Letters** follow the Intelligence Index, highest first, an unmeasured model last, ties by entry key
  (ordinal). They are the same in the three documents of one covered set.
- **Per-model facts** `model.<L>.*`: every fact the per-model sheet has for its subject, the `subject.`
  prefix dropped (`subject.runs` becomes `model.A.runs`), plus `model.<L>.quality.rank` (the joint rank,
  below), `model.<L>.speed.rank`, `model.<L>.cost.rank` and `model.<L>.frontier` (*"on the intelligence
  against cost frontier"*).
- **Comparison facts**: `comparison.models`, `comparison.pricingBasis` and its kind, `comparison.signature`,
  `suite.name` (runs and groups), the Pareto frontiers `frontier.qualityCost`, `frontier.qualitySpeed` and
  `frontier.speedCost` (the models no other model matches or beats on both measures), the spreads
  `spread.quality`, `spread.dimension.<d>`, `spread.speed` and `spread.cost` (*"from 62 (Model D) to 85
  (Model A), 23 points apart"*), and `questions.anyCriticalError`, `questions.wideSpread` and
  `questions.sharedLow`.
- **Joint ranks.** `BenchmarkReportFacts.JointRanks` ranks by Intelligence Index and joins a model to the
  group of the model just above it when their 95 % intervals overlap (touching counts) or their scores
  are equal. Groups are **transitive overlap chains**: A–B and B–C overlapping make A, B and C one group
  even when A and C do not overlap. A joint rank prints *"joint 1st of 2 (intervals overlap)"*. The same
  helper words the per-model documents' ranks (W2, below).
- **The paired-test family** comes from the service step 2's *Paired tests* view uses, requested for the
  covered models only, so each Holm adjustment counts exactly the tests the document reports: *Against a
  reference* with model A as the reference always, plus *All pairs* when there are **3 to 6** covered
  models (`AllPairsMaxEntries`). Intelligence, Speed and Cost are each a family of their own. The sheet
  stores the families (`pairedTests`, each pair oriented with the earlier letter first) and the facts
  `pair.<L>.<M>.quality.difference`, `.quality.interval`, `.sharedQuestions`, `.speed.ratio`,
  `.cost.ratio`, `.intervalOverlap`, and one verdict per family, `pair.<L>.<M>.<quality|speed|cost>.reference`
  and `.allPairs` (*"Model A scored higher on the same questions, established after the Holm adjustment
  across 4 tests (adjusted p 0.012)"*, or *"no difference established …"*). A family that could not be
  computed leaves its reason in `pairedTestsUnavailableReason`.
- **The per-question matrix**: every question any covered model was asked (a battery question matched
  across models by its suite-qualified reference, any other by question and item revision), each with
  every model's score, critical error, refuted answer sentences, tool calls and model time. The rendered
  matrix heads each model's column with its letter. Its legend opens, in a named copy, with *"Columns:
  A = GPT-5.6 Luna (max), B = GPT-6.1 Sol (medium)."* and, in an anonymized one, with *"Each model's
  column is headed by its letter in the models table."*; it then says that *—* under a model marks a
  question it was not asked or not scored on and, when any topic is missing, that *—* under Topic marks
  a question not given in detail.
- **Excerpts** share one total budget, `AnswerExcerptChars` × the number of questions, allotted in this
  order: questions where any model made a critical error or had a refuted answer sentence; then those
  whose scores span at least 20 points, widest first; then those every model scored below 50 (a shared
  gap: chat, corpus or suite); then the rest. Within a question the models with a critical error or a
  refuted sentence come first, then the lowest scores; each excerpt is from the model's run whose score
  is closest to its score on the question. Each question's text and rubric are given once.

**The writer never sees a model name.** The sheet's free text — every fact's display and unavailable
reason, each model's explanation, the purpose statements and the paired tests' notes and caveats — is
lettered (*Model A*) when the sheet is built. As a backstop, the prompt writes any covered model's label,
display name or model id left in its text (an answer excerpt, a grader comment, a claim) as *Model X*,
and every covered model's provider as *[provider withheld]*. A named copy puts the names back when it
renders.

### The writer contract

The prompt (`BuildComparisonSystemPrompt`, per audience) tells the writer every covered model is an
equal known only by its letter, and that the document covers exactly the models listed. The user
message gives, in order: COMPARISON, MODELS, GRADERS (each role's shared provider by letter), PAIRED
TESTS, FACTS, RESPONSE STYLE, the QUESTION MATRIX, the questions with their excerpts, and the question
topics. The prompt's SHA-256 covers it.

- **Tokens.** `{{model:X}}` for a covered model and `{{key}}` for a fact; there is no `{{subject}}` and
  no `{{peer:X}}`. The validator accepts `{{model:X}}` for every letter of the sheet and the `model.<L>.*`
  and `pair.<L>.<M>.*` keys, and rejects any other letter.
- **Slots** (word caps): Executive Summary `overview` (90), `whichModel` (150, ending with a default
  choice for a typical Overseer player), `tradeOffs` (100) and
  `reliability` (80); Report for AI Researchers and Developers `abstract` (150), `results` (200),
  `dimensionProfiles` (150), `frontier` (120), `questionPatterns` (250), `graderReliability` (120) and
  `limitations` (120); Internal Improvement Brief `sharedGaps` (250), `modelGaps` (200) and
  `benchmarkSystem` (150).
- **Lists.** A `models` list with one entry per covered model, at most 2 points of at most 30 words in
  the Executive Summary and at most 4 of at most 40 words in the researcher report, each citing evidence;
  question topics in the researcher report and the brief; at most 8 leads in the brief, each tagged
  `chat`, `harness`, `suite`, `corpus` or `model`. No strengths, weaknesses, recommendations or question
  notes. A lead is still not a finding and goes through `server_benchmark_to_chat_transfer`.
- **Validation.** The rules of § 3 apply, read for comparison scope: rule 10 treats every covered
  model's name as a name; rule 16 reads two `{{model:X}}` tokens against the pair's `intervalOverlap`,
  and a family's established result may be stated instead of *overlap*; **rule 21**, a warning, asks for
  a `models` entry for every covered model.
- **Topics are written once per job.** The first document of a comparison-scope job writes the question
  topics; its later documents are given them and keep them, and their own topics are not checked.
- **The writer** cannot be a covered model's configuration: the same provider, model id and thinking
  level (400). A writer sharing a covered model's provider gets the amber warning naming those models
  and the *Same-Provider Report Writer* confirmation (§ 4).

### Limits, refusals and the estimate

- **2 to 12 covered models** (`MinEntries`, `MaxEntries`; the charts' `MAX_PLOTTED_ENTRIES`). Outside the
  bounds the start and the layout preview answer **409**, and the preview answers 200 with the refusal.
  A comparison of more than 12 models can still have documents: step 3's picker then starts with the 12
  highest-Index models chosen and says why.
- A covered entry that is not in the comparison, or is Excluded — **400**.
- **The writer's context window.** Each estimate carries `contextWindowShare`, the estimated input
  tokens over the writer configuration's context window from the model catalog. Step 3 warns in amber
  from **70 %**; the preview reports, and the start refuses with **400**, a prompt above **90 %**
  (`ContextWindowRefusalShare`), in both scopes.
- **Already written.** A requested document already written for this covered set (comparison scope) or
  subject (model scope) and not named in `replaceDocumentIds` — **409**; an id in `replaceDocumentIds`
  that this job does not write again — **400**.
- **The estimate** uses 3,000, 9,500 and 9,000 output tokens for the comparison-scope Executive Summary,
  researcher report and brief. These are first guesses, to be recalibrated after the first real jobs.

### Rewriting: replace after persist

`ReplaceDocumentIds` names the documents a job replaces. For each document it writes, the job stores the
new row and removes the replaced Report Pack rows of the same comparison, audience and covered set
(comparison scope) or subject (model scope) **in the same `SaveChanges`**; only then is each replaced
chart folder deleted (a failure there is logged and never fails the write). A write that fails deletes
nothing. A single document is deleted with `DELETE report-documents/{id}`, which step 3 offers per row
behind a confirmation.

### The rendered documents

`ReportFormatVersion` for comparison scope is **12** (`ComparisonReportFormatVersion`); per-model
documents stay at 11. The sections, by audience:

- **Executive Summary**: *The result in one sentence*, *The comparison in one paragraph*, *Which model to
  use*, *How they compare* (every model's Intelligence Index with its interval and joint rank, median
  answer time, cost per question and critical errors; a dimensions table; the figures), *Model by model*,
  *Trade-offs* (with the frontier lines), *How reliable this is* (with the interval-overlap sentence, the
  paired-test summary and the writer caveat), *About this benchmark* and *Evaluation terms*.
- **Report for AI Researchers and Developers**: *Abstract*, *Setup and method* (with *Compared models*),
  *Results* (the results table, figures, the writer's text and *Paired tests*), *Dimension profiles*,
  *Speed and cost frontier*, *Per-model analysis*, *Cross-model question patterns* (with the per-question
  matrix), *Grader reliability*, *Threats to validity*, *Reproducibility appendix*, and the question
  details at Detailed and Full.
- **Internal Improvement Brief**: *Models compared*, *1. Shared gaps*, *2. Model-specific gaps*, *3. The
  benchmarking system* (with *Paired tests*), *4. Leads*, *5. Per-question matrix* with the question
  details, and *6. Fact sheet*.

*Paired tests* names the family, its adjustment and its size, and notes that another set of models gives
another family: a subset document's adjusted *p* can differ from step 2's view over all models, and both
are correct for their own family. Tables that list the models carry a **Letter** column under both
namings, and a named copy prints each model's label and provider and **no letter in its prose (W1)**.
The per-model documents print the same way: a named peer reads *Grok 5*, never *Grok 5 (A)*, their
*Compared models* table gains the Letter column, and a rank whose interval overlaps a neighbor's reads
*joint* (**W2**), whatever format they were written under.

### Titles, covers and running headers

| | Named copy | Anonymized copy |
|---|---|---|
| Comparison-wide title | `Comparison #12 — <name>: Executive Summary` | `Comparison #12: Executive Summary` |
| Subset title | `Comparison #12 — A vs B: …` for up to three models, else `Comparison #12 — 4 of 10 models: …` | `Comparison #12 — 2 of 5 models: …` |
| Per-model title | unchanged: `<model> on the Overseer GnollHack Assistant Benchmark — <kind>` | unchanged |

- **The cover's first fact row is *Comparison*** for every document of a numbered comparison:
  *"Comparison #12 — <name> · 3 models · computed 2026-09-20"*, each unknown part left out (the date is
  the sheet's `comparison.pricedOn`). A comparison-scope cover then lists *Models* (the labels and count
  named; *"4 models (A to D), identities withheld"* anonymized), *Coverage* for a subset (*"2 of 5
  models; the other 3 are not part of this document"*), *Pricing basis*, *Suite* or *Battery*,
  *Questions* and *Runs* or *Member runs*, in place of *Compared with*.
- **The subject line**: a per-model document's line as in § 8, followed by *· Comparison #12*; a
  comparison-wide document's *Comparison #12 — <name>*; a subset's *Comparison #12 — <name> · 2 of 5
  models: <covered names>*.
- **The running header** prints *Comparison #12 — <name>* at its right for both scopes, kept to one line
  with an ellipsis; a document without a comparison prints its subject line.
- **An anonymized copy never prints the comparison's name**, which usually names its models: title,
  cover row, subject line, header, file name and ZIP name print *Comparison #N* alone, and a subset's
  covered models are counted, not named.
- The Markdown and HTML copies print a `**Comparison:**` line under the title.

### Figures in comparison-scope documents

| Document | Where the figures go |
|---|---|
| Executive Summary | All in *How they compare*, after its tables |
| Report for AI Researchers and Developers | Intelligence in *Results*; Model profiles in *Dimension profiles*; Speed, Cost and the three trade-off charts in *Speed and cost frontier* |
| Internal Improvement Brief | All in *Models compared*, after its table |

A comparison-scope document plots its **covered models only**: the named variant is step 2's chart with
*Show* limited to the covered set (step 2's *Highlight* kept where it falls inside it), and the
anonymized variant (`anonymizeComparisonForAll`) letters every covered model with the set's letters,
draws it in the neutral gray and removes every other entry. Such a document always covers two or more
models, so the chart endpoints never refuse it as stand-alone.

### The layout preview

`POST report-packs/layout-preview` renders one document as it would print, with **no model call** and
nothing stored. It is multipart: a `request` field holding JSON — a preview request (§ 9) with
`audience`, `paper` (`a4` or `letter`; empty is A4), `naming` (`named` or `anonymized`; empty is named),
`layout` (as stored in the manifest, § 13) and `charts` (`figureKey`, `title`, `caption`, `altText`) — and
up to **12** files named `<figureKey>.png`, in a request of at most 40,000,000 bytes, each image checked as
an uploaded chart is (§ 13). It prepares the request as the preview does (a model-scope request: its
first subject), builds the document in memory with **placeholder text** in every slot and list the writer
fills — *"The report writer's text for <slot> appears here."* repeated to **80 %** of the slot's word limit,
lists of typical length — and the real deterministic sections and tables, and renders it at Full with
the real PDF renderer, the given charts and layout. The cover banner opens *"LAYOUT PREVIEW — no AI
text."* and the *Document ID* row reads *none (layout preview)*.

Status codes: 200 `application/pdf`; 400 for a missing or invalid `request` field, battery results mixed
with runs or groups, an unknown audience, paper or naming, an invalid layout, a chart or file refused, a
file no chart names or a chart without its file, and a preparation refusal (a subject with no peer, a
covered entry outside the comparison or Excluded); 409 for fewer than 2 or more than 12 covered models;
413 for a document too large for a PDF; 499 when the client aborts.

---

## 16. Chat Consistency Report Documents

A saved GnollBench chat consistency analysis (`ai-benchmark-chat-consistency.md`) is written up through
this machinery as **Chat Consistency Report** documents — one model's Overseer chat across two periods
of runs, with no peers to rank against.

### Identity and storage

| Column | Value |
|---|---|
| `Origin` | `ChatConsistencyReport` (4) |
| `Scope` | `ChatConsistency` (3); the fact sheet's `Scope` is `"ChatConsistency"` (`BenchmarkReportFactSheet.ChatConsistencyScopeValue`) |
| `SubjectKey` | `chat-consistency:<id>` (`BenchmarkChatConsistencyReportFacts.SubjectKeyPrefix`) |
| `ChatConsistencyAnalysisId` | the analysis, a `Restrict` foreign key: the analysis cannot be deleted while documents written from it exist (the API answers 409) |
| `ComparisonKey`, `ComparisonId`, `CoveredEntryKeysJson` | null |
| `ReportFormatVersion` | `ChatConsistencyReportFormatVersion`, currently **1** (§ 7) |

The title is *Overseer Chat Consistency Report: \<model\>*. The sheet's subject block
(`BenchmarkReportChatConsistencySubject`) carries the analysis id, name, headline, protocol label,
`InputSha256`, analysis code version, the baseline, comparison and control run ids, and whether a
Provider Issue Report is available.

### Audiences and slots

Four audiences (`ChatConsistencyAudiences`), each with prose slots only — no strengths, weaknesses,
recommendations, question notes, topics or leads. The headline is at most 35 words.

| Audience | Slots (title, word limit) |
|---|---|
| Executive Summary | `asGoodAsBefore` *Is the Overseer chat with this model as good as before?* (120), `playerImpact` *What changed for players* (90), `ourChanges` *Our changes and their effect* (80), `providerChanges` *Provider-side changes* (80), `confidenceAndScope` *Confidence and scope* (90), `nextRuns` *Next runs* (60) |
| Report for AI Researchers and Developers | `questionAndDesign` *Question and design* (200), `runsAndCoverage` *Runs, coverage and scope* (200), `overseerEvents` *Overseer events* (150), `endpointResults` *Results by endpoint* (250), `attribution` *Attribution* (250), `robustness` *Robustness* (150), `limitations` *Limitations* (150), `reproducibility` *Reproducibility* (150) |
| Internal Improvement Brief | `chatFindings` *Findings for the chat* (150), `changeEffects` *Our changes that helped or hurt* (120), `infrastructureIssues` *Infrastructure issues* (100), `nextRuns` *Next runs* (100), `actions` *Actions* (150) |
| Provider Issue Report | `issueSummary` *Summary* (120), `affectedModel` *Affected model and configuration* (80), `timeline` *Timeline* (150), `measurements` *Measurements with intervals* (200), `hoursObserved` *Hours observed* (80), `ruledOut` *What we ruled out (our changes, infrastructure)* (200), `sampleRequestIds` *Sample request ids* (60), `providerRequest` *Request to the provider* (100) |

**The Provider Issue Report** (`BenchmarkReportAudience.ProviderIssueReport`, 4) exists only in this
scope. It is **available only when at least one attribution is provider-side and graded Established or
Indicated** (`BenchmarkChatConsistencyReportFacts.ProviderIssueReportAvailability`). The estimate
always answers, with `providerIssueReportAvailable` and `providerIssueReportReason`; a write that names
it while it is unavailable is refused with 400 and *"No provider-side finding graded Established or
Indicated in this analysis."*; a write that names no audience leaves it out. It never names a control
model — its peers are always anonymized, lettered *Model A* … — is stamped *Confidential. Prepared for
the model's provider.*, and its `sampleRequestIds` slot draws on at most 10 request ids of the
comparison period's candidate calls. Report Pack's own audience list (`REPORT_PACK_AUDIENCES`) does not
include it.

### Facts and claim support

The writer gets a SUBJECT block, PEERS (the controls as `{{peer:X}}`), every fact as
`key = display`, and a **CLAIM SUPPORT** block listing which tokens support a change, the inconclusive
endpoints, the Established grades, the attributions, the provider-confirmed causes, the time-of-day
assessability and the hours — for a Provider Issue Report also the provider-side attributions, the
Overseer events to list under `ruledOut` and the sample request ids. The fact families are `analysis.*`,
`subject.*`, `verdict.*`, `scope.*`, `coverage.*`, `period.<baseline|comparison>.*`, `protocol.*`,
`n.*`, `endpoint.<P1…P5>.*`, `quality.*`, `flip.*`, `grader.drift.*`, `reliability.*`, `tools.*`,
`secondary.*`, `events.*`, `controls.*`, `controls.missing.*`, `did.*`, `robustness.*`, `identity.*`,
`serving.*`, `ownWaits.*`, `pricing.*`, `annotation.*`, `attribution.*`, `limitation.*`, `nextRuns.*`
and, for the Provider Issue Report, `requestIds.sample.*` (`BenchmarkChatConsistencyReportFacts`).

### Validation

Rules 22 to 28 (**C1** to **C7**, § 3) apply on top of the prose rules. C1 to C4, the first half of C6
and the first half of C7 are checked sentence by sentence on the headline and every paragraph: a failing
paragraph is dropped, a failing headline is fatal. C5 is a **warning**, checked over each slot's kept
text. The second halves of C6 (*the document never cites `{{scope.hours}}`*) and C7 (*`ruledOut` misses
an event*) cannot drop text; a note of either marks the document **Completed with warnings**.

### Figures

| Key | Figure |
|---|---|
| `cc1-quality` | Intelligence per run (native, and common grader where one exists) |
| `cc2-speed` | Time to first answer text (telemetry; the legacy proxy hollow) |
| `cc3-work` | Output tokens per answer |
| `cc4-timeline` | Runs and events (telemetry and legacy runs; Overseer changes, annotations, served-model changes) |

Two anchors: `ChatConsistencyResults` (13), after the verdict table, and `ChatConsistencyEvents` (14),
after the events table.

| Document | Figures |
|---|---|
| Executive Summary | cc1, cc2 after the verdict table |
| Report for AI Researchers and Developers | cc1, cc2, cc3 after the verdict table; cc4 after the events table |
| Internal Improvement Brief | cc1, cc3 after the verdict table |
| Provider Issue Report | cc2 after the verdict table; cc4 after the events table |

The verdict table sits under *Results by endpoint* in the researcher report and under *Measurements with
intervals* in the Provider Issue Report, the events table under *Overseer events* and *What we ruled
out* respectively; elsewhere under the default headings *Verdicts by endpoint* and *Overseer events
between the periods*. The client draws the four PNGs when the writing job ends
(`chat-consistency-report-charts.ts`, print theme, 1200 × 675 at density 2) and uploads the named
variant only through `PUT report-documents/{id}/charts`; this scope is exempt from the stand-alone
refusal and always renders the named variant.

### File names

`BenchmarkPdfFileNames` names a chat consistency document
`chat-consistency-<analysis id>_<model slug>_<kind>_<disclosure>_<peers>[_INTERNAL].<pdf|docx>`, the kind
being `executive-summary`, `researcher-report`, `internal-brief` or `provider-issue-report`, for example
`chat-consistency-12_<model slug>_provider-issue-report_summary_anonymized.pdf`. The Download Center
mirrors the stem (`reportDocumentFileStem`). The PDF cover lists the analysis, model, both periods, the
hours, the control models, the protocol and the generated format.

### Endpoints

Under `AdminChatConsistencyController` (`/api/admin/benchmark/chat-consistency`), in the run
report-documents contract (`writerModelConfigurationId`, `audiences`, `acknowledgeSameProvider`; enums
as numbers):

- `POST analyses/{id}/report-documents/estimate` — the cost estimate with the Provider Issue Report's
  availability; no model call; 404, 400 for an audience outside the four, 499.
- `POST analyses/{id}/report-documents` — 202 with the job (`runId` is the analysis id); 404; 409 while
  its documents are being written, for a document already written, when every document exists, and for
  an unacknowledged writer of the model's provider; 400 for an audience outside the four, an
  unavailable Provider Issue Report, an unusable writer or the model under report, and a refused
  endpoint; 429 at the spend guard. Usage rows use report-pack usage context 8.
- `GET analyses/{id}/report-documents/job` — 200 with the job, 204 when this process knows none; jobs
  are held in memory.
- `POST analyses/{id}/report-documents/cancel` — 202; 409 when none is in progress; documents already
  written are kept.

The documents are then read, rendered, deleted and given charts through the ordinary report-document
endpoints (§ 9), listed with `subject=chat-consistency:<id>&origin=chatConsistencyReport`.

### The client

The Chat Consistency tab's Reports step writes and polls the documents and opens the Download Center
through the shell with the context `{ kind: 'chatConsistency', analysisId }` (title *Chat consistency
documents*). The Download Center has a **Provider Issue Report** row (category `providerIssueReport`):
the External preset selects it with the Executive Summary and the Report for AI Researchers and
Developers, Internal selects every row. Its stored settings are **version 4**
(`STORED_SETTINGS_VERSION`); versions 2 and 3 are read and migrated by giving every package the Provider
Issue Report's empty default choice (`providerIssueReport: {}`), which the package preset fills.
