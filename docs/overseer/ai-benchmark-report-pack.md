# Report Packs — AI-Written Documents about the Models of a Comparison

A **report pack** is a set of up to two documents about the models of a Model Comparison. By default
they are **comparison-wide**: one document of each type covering every model of the comparison, or a
chosen subset of two to twelve of them, each model described as an equal (§ 15). The secondary choice is
a **per-model** set: documents about one model, written in the context of the other models compared with
it — at least one other, since a pack compares models (§ 1a). Every comparison is numbered, *Comparison
#N*, and its documents carry the number (§ 15). The documents are for two different readers: the
**Executive Summary** for a model's provider or a manager, and the **Root Cause Analysis** (§ 17) for
the Overseer team and its AI agents, explaining why a model, or the compared models, did well or badly
on intelligence, speed and cost.

The same machinery also writes a run's own **run-completion documents**: the Executive Summary and the
Root Cause Analysis about one run, written once after the run is scored by the report writer the run
names (§ 11). The Executive Summary has no peers there; the Root Cause Analysis reads earlier runs of
other models on the same exam as **controls** (§ 17).

The *Report for AI Researchers and Developers* (audience 2) and the *Internal Improvement Brief*
(audience 3) were retired on 2026-10-15, and their stored documents deleted (§ 17, *Retirement of
audiences 2 and 3*).

A **battery result** — one model's composite over the suites of a battery — can be a subject too: of a
pack from a comparison of battery results, and of its own **battery-completion documents**, written once
after the battery run finishes and is analyzed (§ 14).

A saved **chat consistency analysis** — one model's Overseer chat across two periods of runs — is
written up as **Chat Consistency Report** documents: the Executive Summary and the **Provider Issue
Report** (§ 16). A chat consistency analysis has no Root Cause Analysis.

This document describes the feature for developers: what each document holds, how the figures and the
prose are kept apart, how the prose is validated, what is stored, how a stored document is rendered
at download, how its PDF and Word copies carry charts (§ 13), how a battery result is a subject
(§ 14), how comparisons are numbered and written about as a whole (§ 15), and how the Root Cause
Analysis is computed and written (§ 17). §§ 1–14 describe the per-model documents unless they say
otherwise. It is the companion to [`ai-benchmark.md`](ai-benchmark.md), which describes the harness,
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
| The root-cause engine: evidence, decompositions, known facts, hypotheses, grades, next runs, controls | `Overseer/Services/Benchmarking/RootCause/` (`RootCauseEngine`, `RootCauseEvidence`, `RootCauseDecomposition`, `RootCauseStatistics`, `RootCauseKnownFacts`, `RootCauseHypotheses`, `RootCauseNextRuns`, `RootCauseControls`, `RootCauseModels`) (§ 17) |
| The Root Cause Analysis prompt, claim support and rendering | `BenchmarkReportPackPrompt.RootCause.cs`, `RootCauseClaimSupport`, `BenchmarkReportPackRenderer.RootCause.cs` (§ 17) |
| The server-drawn Root Cause Analysis charts | `Overseer/Services/Benchmarking/Charts/` (`RootCauseChartRenderer`, `RootCauseChartPalette`) (§§ 13, 17) |
| Deleting the retired audiences' documents at startup | `BenchmarkReportAudienceRetirement` (§ 17) |
| Endpoints | `AdminBenchmarkReportPacksController` (the write-now endpoint included), `AdminBenchmarkReportDocumentsController` |
| A chat consistency analysis's fact sheet, prompt, rendering and endpoints | `BenchmarkChatConsistencyReportFacts`, `BenchmarkReportPackPrompt.ChatConsistency.cs`, `BenchmarkReportPackRenderer.ChatConsistency.cs`, `AdminChatConsistencyController` (§ 16) |
| Golden files | `Overseer.Tests/UnitTests/Golden/ReportPack/` |

---

## 1. Purpose and the Two Documents

Step 3 of the Model Comparison wizard, **Write**, starts a report pack (§ 12). Under *Whole
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
| **Root Cause Analysis** (`RootCauseAnalysis`, 5) | The Overseer team and its AI agents. Evidence first, then hypotheses graded by code | **Full only**; internal | Headline · *Scope and design* (with controls, in a run- or battery-completion document) · evidence for intelligence, speed and cost, with the server-drawn charts R1–R3 · *Known facts* and the writer's observations · *Hypotheses* per axis, with chart R4 · *Conclusions* · *Next runs and checks* · *Limitations* · *Threats to validity* · *Reproducibility appendix* · *Question details* · the fact sheet as JSON (in the Markdown copy only) · *Evaluation terms*. The full specification is § 17 |

The writer fills named **slots** and **lists** only; the renderer supplies every heading, table and
figure. The slot ids are in `BenchmarkReportSlots`: `comparison`, `meaning` and `confidence` (Executive
Summary); the Root Cause Analysis slots are listed in § 17. The slots are
**peer-aware**: `comparison` is required only when the fact sheet has peers
(`BenchmarkReportAudienceSpec.PeerOnlySlots`, `SlotsFor(hasPeers)`), and a stand-alone reply that
supplies it has the extra slot dropped with a rule 1 note.

The prose is written once and must be safe at every disclosure level: the same stored output renders
for the provider, with questions described rather than quoted and peers anonymized, and for the team at
Full. The writer is told to write in US English (*color, behavior, analyze, center, gray, labeled,
canceled*), and rule 12 (§ 3) checks it.

**The stand-alone (peerless) form.** A comparison with only the subject — every run-completion
Executive Summary (§ 11) and every battery-completion Executive Summary (§ 14) — has no peers. The
Report Pack no longer writes this form (§ 1a); Report Pack documents stored before 2026-10-06 may still
have it. The fact sheet then marks every fact that compares the subject with peers
unavailable with the reason *"A stand-alone run report has no peers."*
(`BenchmarkReportFacts.StandaloneReason`); the writer prompt says there are no peers, that `{{peer:X}}`
tokens are unavailable and that the subject is never compared with other models. The renderer prints
*Peers: none; this is a stand-alone report* in the header, a results table of the subject's own
dimensions and bands with no peer column, question tables without *Peer mean* and *Difference*, a
comparability line that describes the model on its own, and no pairwise-significance statement. A
run- or battery-completion Root Cause Analysis is stand-alone in the same sense — it has no peers — but
reads its **controls**, earlier runs of other models on the same exam, as evidence (§ 17).

The Executive Summary prints no evidence lines under its items; it prints each item's support label in
plain words instead (§ 2). Rule 9 (§ 3) binds the writer's prose.

**Evaluation terms.** The Executive Summary and the Root Cause Analysis end, after *Removed content*
(and, in the Root Cause Analysis, its Markdown fact sheet) and before the footer, with an *Evaluation
terms* block: the subject runs' purpose
statements (`PurposeStatementUsed`, deduplicated, carried in the fact sheet's `purposeStatements`); the
distillation and training prohibition sentence as the run report prints it; and a third-party model
content sentence naming the subject and its provider, the graders' providers and the writer and its
provider. The subject is always named; under anonymized peer naming, a grader provider that is also a
peer's provider (and not the subject's) reads *"a withheld provider"*.

**Leads are gone.** The Internal Improvement Brief's free-text *leads* were replaced by the Root Cause
Analysis' **checks** (§ 17): each check is grounded in a hypothesis that a run cannot settle. Like the
leads before them, a Root Cause Analysis' hypotheses and checks are provisional inputs to the triage,
evidence bar and tool-layer diagnostics of the `server_benchmark_to_chat_transfer` skill, never
findings.

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
- **The Root Cause Analysis is also a completion document** (§§ 11, 14, 17), so a run's or battery
  run's analysis has a home without a one-entry comparison; its controls are evidence, not peers, and
  do not make it a comparison document.
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

Counts in prose are number words. Interval overlap is stated descriptively — *"its 95% interval
overlaps those of Models B and C"*. The comparison runs **no significance test**. A document with peers
says so once, in its own words, where the comparison left pairwise significance out: *"No pair of models
is tested for significance: with N models, testing every pair would flag chance differences."* (*"The two
models are not tested for significance, so a gap between them may be noise."* for two models), in the
Executive Summary's *How reliable this result is*. The writer prompt carries the statement as its *NO
SIGNIFICANCE TEST* block.

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
models instead, and carry no such statement (§ 15). The Root Cause Analysis grades its hypotheses on
those tests, or on the subject's paired bootstrap in model scope, and states which (§ 17).

**Per-peer facts.** Each peer `X` has its own facts, so the writer can say how the subject compares with
it: `peer.X.quality.index`, `peer.X.quality.interval`, `peer.X.quality.rank`,
`peer.X.speed.medianSeconds`, `peer.X.cost.perQuestion`, `peer.X.runs` (*"3 runs"*) and the boolean
`peer.X.intervalOverlap` (the peer's 95% interval overlaps the subject's). A fact on an axis the peer is
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
true the writer may state the paired result for that pair instead of *not established* (§ 3, rule 16).

**Comparison and per-question facts (format 8).** `comparison.peerRuns` states the peers' run counts in
one clause — *"every peer has 1 run"*, or *"peers have 1 to 3 runs"* — and is withheld on a stand-alone
sheet. Each question of the sheet also carries `peerMin`, `peerMax` and `peersAbove`: the lowest and
highest peer score on it, and how many peers scored more than 5 points above the subject on the same item
and revision (`BenchmarkReportFacts.PeerAboveMarginPoints`). The writer prompt shows them on each question
line as *"peers: min 60, max 90, 2 of 4 scored clearly higher"*. The `dimension.<d>.difference` and
`band.<b>.difference` facts keep their unrounded value but display the difference of the two printed
whole numbers, and so does a question's *Difference* column, so every printed row adds up.

**Provider availability facts (format 12).** A run sheet (`BenchmarkReportFacts`) and a battery sheet
(`BenchmarkBatteryReportFacts`) carry the `availability.*` family, the candidate's provider errors
observed in the subject's runs and their lineage
([`ai-benchmark-provider-availability.md`](ai-benchmark-provider-availability.md)): `availability.attempts`,
`.providerErrors`, `.providerErrorRate`, `.providerErrorRateCi95Low`, `.providerErrorRateCi95High`,
`.unattributedErrors`, `.inclusiveRate`, `.inclusiveRateCi95Low`, `.inclusiveRateCi95High`, `.overloaded`,
`.serverErrors`, `.timeouts`, `.streamFailures`, `.rateLimitedOurs`, `.rateLimitedUnattributed`,
`.transportUnattributed`, `.ourSideFailures`, `.questionsLost`, `.reruns`, `.providerReruns`,
`.replacedRuns` and `.oneIn` (*about 1 in 123*, prose only). The interval bounds are two-part keys after
the `ci95Low` / `ci95High` convention, and every key has a label in `BenchmarkReportFactLabels`. A legacy
run, started before harness 56, marks every one of them unavailable, *not recorded before harness 56*.
A comparison sheet carries only the **eight headline keys** through `model.<L>.*` (`attempts`,
`providerErrors`, `providerErrorRate` and its two bounds, `unattributedErrors`, `reruns`,
`providerReruns`), because the comparison prompt lists every sheet fact (§ 15). The writer cites them as
tokens like every figure, and its prompt has one sentence for them: report provider errors as observed
in GnollBench runs, always with their denominator, and never their cause or intent.

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
`BenchmarkReportFactLabels` (*Intelligence Index*, *95% interval*, *Critical errors*…), used by the
tables, the labeled lines and the evidence lines alike, so one figure never has two names. A key without
an explicit label gets a readable fallback built from its dotted parts. Every key the per-model and the
comparison builders emit has a label, explicit or by a pattern rule (`model.<L>.*`, `pair.<L>.<M>.*`,
`spread.dimension.<d>`, `notable.<n>.*`), and a test holds both builders to it. Labels are chosen at
render, so a comparison document of format 13 or earlier prints the newer labels too (*Questions with a
critical error*, where the fallback read *Questions any critical error*).

**Grader vocabulary.** The writer calls the graders by one set of role names, in lower case: *panel
member A*, *panel member B*, *the reference reader* and *the claim verifier* (in a single-assessor run,
*the assessor* and *the second reader*); the Executive Summary says *one grader* or *both graders*.

The writer prompt's row list finds the two panel members by the role names the fact sheet writes —
`Panel member A` and `Panel member B` — as well as the legacy `Assessor` and `Co-assessor`, so in a panel
run a row raised by one member only carries the note that it was *raised only by* that member and
whether that member shares the subject's provider.

---

## 3. Validation, Repair and Drops

`BenchmarkReportPackValidator` checks the writer's JSON against the rules below, a comparison-wide
document against rule 21 too, with rules 2, 10 and 16 read for its tokens (§ 15); a chat consistency
document is checked against rules 22 to 30 (C1 to C9, § 16) as well, and a Root Cause Analysis against
rules 32 to 36 (§ 17). Each failure is a
`BenchmarkReportValidationNote` with its rule number, location (`headline`, `sections.meaning`,
`weaknesses[1]`…) and message. Rule 20 is not a check on the writer: it is the note a battery
subject's preparation records when it left question detail out of the prompt (§ 14). Rules 5 (evidence
for recommendations), 15 (question notes) and 18 (`model_developers` recommendations) checked parts of
the retired audiences (§ 17); their numbers stay unused.

| # | Rule |
|---|---|
| 1 | The output is JSON of the expected shape, and every required slot is present and non-empty |
| 2 | Every `{{…}}` token exists: a fact key, `{{subject}}` or a known peer letter. In a Root Cause Analysis a fact kept from its writer (`RootCauseFacts.RootCauseWriterHidden`: a price effect's `rca.hypothesis.<id>.<n>.*`, and `rca.rule.<id>.*` of a rule whose hypotheses are all price effects, § 17) counts as unknown, and the message adds that it is a price-card difference Overseer prints in one line |
| 3 | No bare digits in prose after masking tokens, question references, known names and the fixed phrase *95% interval* |
| 4 | Every question number exists, and the question topics cover every question where the document requires them (the Root Cause Analysis: every question it cites) |
| 5 | *Retired* (evidence for the retired audiences' recommendations) |
| 6 | A strength may not cite a weakness row and a weakness may not cite a strength row; a finding citing only Conflicting rows must say the graders disagree |
| 7 | Length limits: headline at most 35 words; a question topic at most 12 words; in the Executive Summary at most 3 strengths and 3 weaknesses, each at most 30 words, *What this means for use as a game assistant* (`meaning`) at most 90 words, *How reliable this result is* (`confidence`) at most 60 words and the *How it compares* paragraph (`comparison`) at most 70 words; the Root Cause Analysis caps are in § 17. The Root Cause Analysis' optional `unexpectedResults` slot is checked like a required slot when it holds text; on a sheet with no unexpected result, text in it is a rule-7 issue (*"There are no unexpected results; leave the slot empty."*) and is removed. A cap counts the writer's raw text, in which every `{{token}}` is one word however long its value; the prompt of every audience says so |
| 8 | No headings, tables or HTML inside any text |
| 9 | No run of 8 or more words shared with any question, rubric, answer or grader-evidence text, the claim verifier's claims and rationales, or a comparison's *weakest answers* grader comments (`ModelNotes`, § 15) |
| 10 | No peer names, model ids or providers in prose |
| 11 | No significance language: *significant*, *significantly*, *statistically*, *reliably better* or *worse*, *clearly outperforms* |
| 12 | US English: no word of the fixed British-spelling list (`BenchmarkReportPackValidator.BritishSpellings`: *colour, behaviour, analyse, analysed, organise, recognise, favour, honour, centre, defence, catalogue, programme, grey, travelled, modelling, labelled, cancelled, judgement*), matched as whole words ignoring case, in any prose |
| 13 | In the Executive Summary's `confidence` slot only: the quality interval is not called *narrow*, *narrower*, *wide*, *wider*, *tight*, *tighter* or *broad* (`BenchmarkReportPackValidator.IntervalWidthWords`, whole words ignoring case); the code-rendered sentence after the slot states the interval and its span |
| 14 | The headline does not mention the claim verifier: *verifier* or *verifiers*, as a whole word ignoring case (`VerifierInSummaryRule`). A claim-verifier ruling is advisory and belongs, attributed, among the weaknesses |
| 15 | *Retired* (question notes of the retired audiences) |
| 16 | A sentence that holds `{{subject}}` and a `{{peer:X}}` token together with a comparative word (`ComparativeWords`: *higher, lower, better, worse, ahead, behind, outperform(s/ed), beat(s), leads, trails*), where `peer.X.intervalOverlap` says the two intervals overlap, also says *overlap* or *not established* (`OverlapHedgeRule`); where that peer's `peer.X.pairedExcludesZero` is true, stating the paired result — *paired* and *excludes zero* in the sentence, or the fact's token — satisfies it as well. In comparison scope (§ 15) a comparative word relates only the nearest `{{model:X}}` before it and the nearest after it in the sentence, so a sentence naming several models is checked for the pairs its comparative words rank, not every pair it names; only when a comparative word has no model token on one side is every pair of the sentence read. A speed or cost sentence is read against the pair's speed or cost paired result instead, and one note per sentence lists every unhedged pair (*Repair and drop policy* below). Tokens are set aside before the text is split into sentences, so a fact key's dots never end one |
| 17 | No hype or filler words (`HypeWords`: *impressive, remarkable, outstanding, stellar, exceptional, robust, seamless, leverage, delve, game-changing, cutting-edge*), as whole words ignoring case, in any prose (`HypeWordRule`) |
| 18 | *Retired* (`model_developers` recommendations of the retired audience 2) |
| 19 | No negation — *no, none, never, without, zero*, ignoring case — among the four words before a token whose display value starts with `0` in the same sentence, as in *"no critical errors across {{errors.critical}}"* reading *"0 of 18 answers"* (format 9). Tokens are set aside before the text is split into sentences |
| 20 | *Not a writer check.* A battery subject's prompt exceeded `Benchmark:ReportPack:BatteryMaxPromptChars`, so the full detail of the questions it names was left out and only their rows were given (`BenchmarkBatteryReportFacts.PromptBudgetRule`, location `prompt`, § 14). Recorded before the writer call, stored with the document and logged as a warning; it neither drops anything nor marks the document *Completed with warnings* |
| 21 | A comparison-wide document's `models` list has an entry for every covered model (`ModelCoverageRule`, § 15). A warning, kept after the repair turn like rules 12 to 19 |
| 22 | **C1**, chat consistency only (§ 16): a sentence with a change word (`ChatChangeWords`) holds a token that supports a change — a changed endpoint, verdict or attribution, a control DiD whose interval excludes zero, an established reliability increase, or a rejected secondary estimate (`ChatChangeClaimRule`). An error: the paragraph is dropped |
| 23 | **C2**, chat consistency only: an intent word (`ChatIntentWords`) or mechanism word (`ChatMechanismWords`) needs a *ProviderConfirmedCause* annotation token in the sentence (`ChatIntentMechanismRule`); and a monitoring word (`ChatMonitoringWords`: *monitor, monitors, monitored, monitoring*, whole words ignoring case) is refused outright, with no annotation that allows it, since GnollBench runs are made by hand. An error |
| 24 | **C3**, chat consistency only: a causal connective (`ChatCausalConnectives`) needs an attribution token in the sentence (`ChatCausalClaimRule`). An error |
| 25 | **C4**, chat consistency only: a public-claim word (`ChatPublicClaimWords`: *publishable, established, confirmed, proven, definitively, conclusively, we can state*) needs an Established grade token; not counted after *not*, *never*, *not yet* or a hyphen, and *confirmed* is allowed with a provider-confirmed cause token (`ChatPublicClaimRule`). An error |
| 26 | **C5**, chat consistency only: a slot that cites an inconclusive endpoint also cites its `{{endpoint.<P>.mde}}` (`ChatInconclusiveMdeRule`). A **warning**, checked over the slot's kept text |
| 27 | **C6**, chat consistency only: an all-hours word (`ChatAllHoursWords`) needs `{{serving.timeOfDayAssessable}}` in the sentence and that fact true; and the document cites `{{scope.hours}}` somewhere (`ChatHoursRule`). An error; the second half cannot drop text and marks the document *Completed with warnings* |
| 28 | **C7**, Provider Issue Report only: a model or serving term (`ChatModelServingTerms`) needs a provider-side attribution token, except in `affectedModel` and `sampleRequestIds`; and `ruledOut` cites a token of every `events.<n>` (`ChatProviderReportRule`). An error; the second half cannot drop text and marks the document *Completed with warnings* |
| 29 | **C8**, *readable text*, chat consistency only: the headline and every paragraph hold no run of twelve or more hex digits, no JSON (`{"`), no `OverseerEventKinds` identifier (`CandidateSystemPromptSha256`, `CorpusIndexFingerprintsJson`…) as a whole word, and no token of a fact kept from the writer (`BenchmarkChatConsistencyReportFacts.WriterHidden`: `analysis.inputSha256` and an older sheet's raw `events.<n>.from` / `.to`; and the sheet's own `WriterHiddenKeys`, § 16) (`ChatReadableTextRule`). One note names every offending token. An error: the paragraph is dropped |
| 30 | **C9**, *repetition*, chat consistency only: read with every fact token replaced by its display value, the headline and every paragraph hold no *A and A, respectively* whose two operands are the same words, ignoring case, and no full stop followed by a lower-case *and* inside one sentence — a sentence-valued fact spliced into a sentence — outside abbreviations such as *e.g.*, *i.e.*, *etc.*, *vs.*, *cf.*, *approx.*, *incl.* and *resp.* (`ChatRepetitionRule`). One note per kind, quoting the offending text. A **warning**: it asks for the repair turn and marks the document *Completed with warnings*, but never drops a paragraph |
| 31 | *Token shape* (`ComparisonTokenShapeRule`), comparison scope and every Root Cause Analysis (both scopes), not a per-model Executive Summary: read with every token resolved to its display (a model token to *Model X*), the text holds **(a)** no full stop followed by a lower-case letter, outside abbreviations such as *e.g.* and *vs.* — a sentence-valued fact continued after its full stop; **(b)** no sentence-valued fact (a display that starts with a capital and ends with a full stop, every `notable.<n>.summary` among them) placed anywhere but at the start of a sentence, a paragraph or a list item, or just before `)`; **(c)** no clause-valued fact (*"Model A took 1.49 times as long as Model F …"*) right after a model or its own verb (*"Model A took {{…}}"*); and **(d)** no run of two or more *N of M* counts of which at least one is bare (its display is exactly its `BenchmarkReportFormat.CountHead`, as a display stored before format 23 is; a run of counts that each say what they count reads, format 23), two or more `rca.axis.<axis>.status` tokens, or two or more hypothesis grade tokens, separated only by commas, semicolons or *and* (in or out of parentheses), which leaves the reader guessing what each one is; and, in a Root Cause Analysis only, **(e)** no sentence-shaped fact (`RootCauseClaimSupport.IsSentenceShaped`: a hypothesis's statement, *for*, *against*, *refuted if* and grade reasons, a known fact, a check, a question's text and signals, a profile's joined flags, an unexpected result's summary, a notable finding's summary; an axis's price note up to format 25) after words of the writer's own sentence — it stands alone, or after a colon or a list marker; a label, a single flag, a check's `.summary` and an unexpected result's `rca.anomaly.<n>.ref` are noun phrases and exempt, and the fix for `{{rca.check.<n>}}` says to cite `{{rca.check.<n>.summary}}` inside a sentence instead. **(a)** names the token whose full stop the writer continued (the last token ending before it). A token display that starts a sentence is sentence-cased (`BenchmarkReportSentenceCase.UpperFirst`) before the check, as the renderer prints it, so (a) fires only on the writer's own lower-case word, never on a fact such as *"none recorded."*; the abbreviations come from `BenchmarkReportSentenceCase.Abbreviations`. One note per token occurrence: where (a) fires on a token (b) or (e) also flags, only (a) is recorded, with (b)'s fix appended. Excerpts widen to the nearest whitespace, so they never cut a word in half. In a Root Cause Analysis a parenthesis inside another is flagged too; its message names the token whose display brought the inner parenthesis (*"{{pair.A.C.configDifferences}} prints its own parentheses: write it outside parentheses, as a clause of its own."*), and adds *"never put a model token inside parentheses"* only when a model token sits inside the outer one. A **warning**: it asks for the repair turn and keeps the text |
| 32–36 | Root Cause Analysis only: the claim discipline of § 17 (`RootCauseEvidenceCausalRule`, `RootCauseCausalClaimRule`, `RootCauseCertaintyRule`, `RootCauseWriterHypothesisRule`, `RootCauseAxisStatusRule`; 36 is a warning) |
| 37 | *Bare model letter* (`BareModelLetterRule`), comparison scope only: read with every token stripped, the prose holds no *Model X* or *Models X and Y* (`\bModels? [A-Z]{1,2}\b`, case-sensitive) — a model is named only through its `{{model:X}}` token, in every slot and list, the writer's hypotheses included. A **warning**: it asks for the repair turn and keeps the text; a named copy renders a bare letter that resolves to a peer as the model's name |
| 38 | *Share beside another hypothesis* (`RootCauseShareBesideRefRule`), Root Cause Analysis only: a `{{rca.hypothesis.<id>.<n>.share}}` token whose nearest following parenthesis, at most six words on in the same sentence and read on the writer's own text, cites any hypothesis reference other than its own `.ref` alone — a range or a list (*"45% of the gap (HQ1.1–HQ1.3)"*). The message names the share and the references and points to `{{rca.rule.<id>.shareSpan}}` where the sheet has it. A **warning**: it asks for the repair turn and keeps the text |
| 39 | *Words repeated beside a count* (`CountRepetitionRule`), every scope: a count token whose qualifier (`BenchmarkReportFormat.CountQualifier`) shares a content word with the writer's own words within five words before it or four after it, in the same sentence. A content word has four letters or more, is no word of `CountRepetitionStopWords` (*with, that, this, from, each, their, were, which*) and no counted noun (*answers, questions, runs, attempts, calls, checks*), and two words match when the first five letters of one begin the other (*tools*, *tool*). The message quotes what the token prints and the repeated words. A **warning**: it asks for the repair turn and keeps the text |
| 40 | *Repeats what code prints* (`RootCauseCodePrintedRule`), Root Cause Analysis only: the `unexpectedResults` slot cites a fact of an unexpected result other than its reference — `rca.anomaly.<n>.summary`, `.prior`, `.confounds`, `.graders`, `.settledBy` or `.tested` — which Overseer prints in the *Unexpected results* section; the writer cites a result by `{{rca.anomaly.<n>.ref}}` alone and says what the grader comments show. A **warning**: it asks for the repair turn and keeps the text. Up to format 25 it flagged the conclusions citing an axis's `{{rca.axis.<axis>.priceNote}}`, and before format 25 an answer slot citing its question's `{{rca.question.<n>.signals}}`; neither fact nor slot exists now |
| 41 | *Unavailable figure cited* (`UnavailableTokenRule`), every Root Cause Analysis and every comparison-wide document: a token of a fact the sheet holds without a value, which prints *not available* (*"… are not available and not available"*). The writer says in plain words that a figure is unavailable and why, and never cites its token. A **warning**: it asks for the repair turn and keeps the text |
| 42 | *Next runs out of order* (`RootCauseNextRunsOrderRule`), Root Cause Analysis only, on the `nextRuns` slot: read in order of first appearance, the `rca.run.<n>.*` tokens ascend, the first is run 1 where the sheet has one, and no `rca.check.<n>.*` token comes before the first run unless run 1's `rca.run.1.purpose` is model selection. The message names code's order (*"Write the runs in code's order: {{rca.run.1.summary}} first, then {{rca.run.2.summary}}; a check comes after them."*). A **warning**: it asks for the repair turn and keeps the text |
| 43 | *Figure of one model given for another* (`BorrowedModelFigureRule`), comparison-wide Root Cause Analysis only: a model's figure token — `model.<X>.*`, `rca.atRisk.<X>.*` or `rca.within.<X>.*` — followed in its sentence, before the next fact token, by *for* and a list of model tokens (*"for {{model:E}}, {{model:F}} and {{model:G}}"*), where a listed model other than X has no such fact, has it without a value, or has another display. A list whose models share the display passes. The message gives each differing model's own token and display. A **warning**: it asks for the repair turn and keeps the text |
| 44 | *A count written in words* (`NumberWordCountRule`), Root Cause Analysis only, in every prose string: read with the tokens stripped, a number word (*zero* to *nineteen*, the tens with their hyphenated units such as *twenty-one*, *hundred*), *all* or *every*, then *of* or *of the* and a second number word, as whole words ignoring case (*"eight of the nine models"*, *"all of the nine"*). A count across a total comes from a fact token; a plain count with no total (*"three pairs"*) passes. The message quotes each count. A **warning**: it asks for the repair turn and keeps the text |
| 45 | *Nulls restated in a hypotheses paragraph* (`RootCauseNullRestatedRule`), Root Cause Analysis only, in `hypothesesIntelligence`, `hypothesesSpeed` and `hypothesesCost`: a sentence, read with its tokens blanked, holding *null*, *real difference* or *may be chance* (`NullRestatedPhrases`, whole words with their plurals, ignoring case), which restates the nulls Overseer prints beside the paragraph. One note per sentence, quoting the phrases. A **warning**: it asks for the repair turn and keeps the text |

Rules 2, 3, 8, 9, 10, 11, 12, 16, 17 and 19 apply to every prose string: the headline, each paragraph of
each slot, and the text of every item and topic. Rule 13 applies to each paragraph of the Executive
Summary's `confidence` slot, rule 14 to the headline, rules 16 and 19 sentence by sentence, rule 31
to each prose string of a comparison-scope document or a Root Cause Analysis, and rule 44 to each prose
string of a Root Cause Analysis.

**Readability.** The system prompt of every audience adds: one idea per sentence; sentences of at most
about 25 words; active voice; a count from the facts rather than *many* or *several*; the category of a
finding named rather than *"issues across many topics"*; and no hype words (the rule 17 list). The
Executive Summary keeps its plain-language rule. The comparison-scope prompts and the Root Cause
Analysis prompt, and their repair messages, also carry the **token-shape reminders** rule 31 checks
(`BenchmarkReportPackPrompt.TokenShapeReminders`): a sentence-valued fact is a sentence of its own, at
most once, never inside parentheses and never continued after its full stop; a fact whose value names a
model and a verb is written alone as the clause; and a count token already says what it counts, so it
is written as a noun phrase with none of its words repeated beside it (rule 39).

**Counts say what they count** (format 23, per-model format 19, chat consistency format 6). Every *N of
M* fact a writer can cite prints its own qualifier after its noun, built by
`BenchmarkReportFormat.CountOf(count, total, noun, qualifier)`: *"9 of 36 answers without a tool call"*,
*"1 of 36 answers with a critical error"*, *"4 of 4 questions with a scored answer"*, *"12 of 1,480
attempts that ended in a provider error"*. The qualifier states exactly what the count counts, from the
key's label. The display still starts with the count, so rule 19's zero test and rule 31's count test
read it as before. Tables and code-written lines whose own words say what is counted print
`BenchmarkReportFormat.CountHead(display)`, the leading *"k of n &lt;noun&gt;"* with a share in
parentheses that ends the display (*"3 of 36 valid attempts (8%)"*); a display stored before this, bare,
passes through whole. Displays that already said what they count are unchanged:
`outcome.wrongInsteadOfAbstaining`, `<p>legacyRuns`, `availability.<p>.recordedRuns`,
`availability.attempts` and the Root Cause Analysis flag texts.

**Repair and drop policy.**

1. When any rule fails, the writer gets **one repair turn**: the issues **grouped by rule**, each group
   headed by the rule's number, name, count and one fix instruction from the rule catalog
   (*"- Rule 16 — Ranking without the overlap caveat (13): …"*), then its locations, a message that
   repeats the one before it written *"as above"*; then the output rules in brief
   (`BenchmarkReportPackPrompt.BuildRepairMessage` and `AppendGroupedIssues`; with the headline's word
   count against its cap when an issue is the headline's). Whenever an issue is rule 7 on a slot, or a
   capped slot is within 10 words of its cap, the message adds a **word-budget block**
   (`BenchmarkReportPackValidator.SlotWordCounts`): each over-cap slot with its count, its paragraphs'
   counts, its cap and how many words to cut (*"Hypotheses: speed has 135 words (paragraph 1: 52;
   paragraph 2: 83); its cap is 120: cut at least 15 words."*), one line per other capped slot
   (*"Conclusions: 104 of 120."*), a warning not to lengthen a section to fix another issue and that a
   section still over its cap loses sentences from its end, and, in a Root Cause Analysis, to keep one
   conclusions paragraph per axis, in axis order, shortening each rather than removing one. The digits
   are in the message, never in the catalog's fix text. The job log says how many issues the first
   reply had, counted per rule (*"7 validation issue(s) (rule 2: 5, rule 16: 2); sending one repair
   turn."*). A repair reply that cannot be parsed is logged as a warning, and the first reply is used
   instead. A first reply that **cannot be parsed** gets the repair turn too, and the readable reply that
   comes back gets **a repair turn of its own** when it has issues, since the first one was spent on
   rebuilding the JSON (*"the first reply could not be read, so the revised reply gets a repair turn of its
   own: …"*); the final turn of item 5 then follows the last revision. A reply whose finish reason says it
   stopped at the **output token limit** (`MAX_TOKENS`, `max_tokens` or `max_output_tokens`) is logged as
   a warning with the limit and its reasoning tokens (*"the reply stopped at the output token limit (32,000
   tokens, 14,950 reasoning)."*), and its rule-1 note, which the repair message repeats to the writer,
   reads *"The reply stopped at the output token limit (32,000 tokens, reasoning included), so its JSON is
   incomplete: …"*.
2. What still fails after the repair is **dropped** — the item or paragraph is removed from the stored
   output, the note records `Dropped`, and the document is stored as **CompletedWithWarnings**. An
   over-cap slot with a word limit (`meaning`, `confidence`, `comparison`, and every Root Cause
   Analysis slot) is **trimmed by sentence** (`TrimToCap`): while it is over its cap, the last sentence
   of the last paragraph that still has an unprotected last sentence is removed, and a paragraph left
   with no sentence goes. Only trailing sentences go, so no kept sentence refers forward to a removed
   one. In a Root Cause Analysis's `conclusions`, a sentence citing `{{rca.axis.<axis>.status}}` is
   **protected**; when only protected last sentences remain and the slot is still over its cap, it is
   kept over its cap with a rule-7 warning (*"Kept over its cap at 131 words: every sentence left to
   remove states an axis's status."*, severity *warning, text kept*). Each trimmed paragraph gets one rule-7 note (*"Removed its last sentence
   to bring "Hypotheses: speed" within 120 words."*) with the removed sentences as its `Text`. Items and
   topics over their caps are still dropped whole. **A required slot that would lose every paragraph** is
   salvaged by sentence first (`SalvageBySentence`): each of its paragraphs is split into sentences, each
   sentence is checked alone, the ones with a blocking issue are removed, and the rest is kept when it
   passes every blocking rule as a whole paragraph. Each removed sentence gets its own dropped note with
   `SentenceOnly`, the sentence as its `Text` and *"Only this sentence was removed, so the section kept its
   paragraph."* after the rule's message; the job log and the stored document's *Removed content* add
   *"one sentence"* after the rule's name, and the why-dialog counts it as a shortened paragraph. This runs
   only where the document would otherwise fail; a slot with another clean paragraph still loses a faulty
   paragraph whole.
3. **Rules 12 to 19 never drop** (`BenchmarkReportPackValidator.IsWarningRule`); a zero-count token
   after a negation (rule 19) is kept after the repair turn like the others. A spelling slip, an
   interval adjective, a mention of the claim verifier, an unhedged comparison across overlapping
   intervals or a hype word is not worth losing a finding or the one paragraph of a required slot: after
   the repair turn, text that still uses a British spelling, calls the interval narrow or wide, names
   the verifier in the headline, ranks the subject against an overlapping peer without saying so or uses
   a hype word is **kept**, its note is recorded without `Dropped`, and the document is stored as
   **CompletedWithWarnings**. The
   Executive Summary states the interval's span in a code-rendered sentence whatever the writer wrote
   (§ 6). Rules 21, 26 (C5), 30 (C9), 31, 36 and 37 are warning rules in the same way.
4. **The headline rescue.** A headline cannot be dropped like a paragraph. When, after the repair turn,
   the headline is the **only** fatal cause — every required slot kept a paragraph
   (`BenchmarkReportCleanResult.HeadlineOnlyFatal`) — the job tries to rescue it, checking each
   candidate with `BenchmarkReportPackValidator.HeadlineIssues`:
   1. **In every scope**, the first draft's headline is kept if it passes every blocking check; the
      note reads *"The repaired headline was invalid (…); the first draft's headline was kept."*
   2. **The shortening turn**, below, carries the headline when that fails too.

   A rescued headline leaves a rule 7 note marked dropped, so the document is stored *Completed with
   warnings*.
5. **The shortening turn.** When the repaired reply's only blocking problems — before any trim — are
   word caps of whole slots and/or the headline, and a call is left, **one more writer turn** on the
   same conversation, **in every scope**, asks for just those slots (and the headline) shortened to
   stated word counts, as a JSON object holding only `headline` (when asked) and the named `sections`
   (`BenchmarkReportPackPrompt.BuildShortenMessage`, with the word-budget wording of item 1). Its call
   kind is `Shorten`, or `Headline` when only the headline is in it; the phase stays `Revising`, with
   *"The revised draft is over the word cap in Hypotheses: speed and Conclusions; the writer is asked to
   shorten them."* The reply's slots and headline are merged into a copy of the repaired output, which
   is validated and trimmed, and kept when it has no more blocking notes than before and is not fatal;
   otherwise the repaired output stands. An unparseable reply is logged as a warning and ignored. An
   accepted shortening is recorded as an **info** line of the job log per section, with its figures
   (*"Root Cause Analysis: Evidence: intelligence was 102 words, 2 over its cap of 100, after the
   repair; the shortening turn brought it to 98."*), not as a validation note: a shortening that brings
   every section within its cap leaves the document *Completed*. A section still over its cap after the
   merge is trimmed by item 2 and leaves that trim's dropped rule-7 note, so the document is *Completed
   with warnings*. Documents stored before this carry a kept rule-7 note at the bare location
   `sections`, labeled *"The capped sections"*. A document therefore takes **at most three model
   calls, or four when the first reply was unreadable** (`AgentRunBudget.MaxTotalModelCalls = 4`, one
   budget counted across every turn of the document): the first, the repair turn, the second repair turn
   of item 1 and the shortening turn, which replaces the comparison-only headline-only turn of earlier
   versions.
6. A missing headline, a headline the rescue could not fix, or a required slot left empty — missing, or
   with no paragraph that survives the sentence salvage of item 2 — cannot be dropped around: the
   **document fails** and no row is stored. A headline whose only fault is rule 12 is kept.

**A revision should be the exception.** The repair turn resends the system prompt, the whole first
user message with its fact sheet, the first reply and the issues, so it roughly doubles a document's
cost and time. The policy stays *any issue sends one revision*; what keeps revisions rare is that a
careful first draft can pass every rule. To that end the rules do not contradict one another or the
prompt:

- **Rule 3 and the interval level.** The fixed phrase *95% interval* (also *95% bootstrap*,
  *confidence* or *paired interval*, singular or plural, and the older spaced form *95 % interval*,
  which the prompt used before 2026-10-15) is masked before the digit search: the level is
  a method constant, not a figure from the data. No other digit is exempt. Rule 16's messages say
  *"whose intervals overlap"*, with no digit.
- **Rule 16 is axis-aware.** A ranking in `evidenceSpeed`, `evidenceCost`, `hypothesesSpeed`,
  `hypothesesCost`, `answerSpeed` or `answerCost`, or a sentence with a speed or cost word and no quality
  word, is checked against the pair's **speed or cost paired result**, and fires only where that result
  exists and is not established; every other sentence keeps the quality check against
  `pair.<L>.<M>.intervalOverlap`. One note per sentence lists every unhedged pair. Citing a
  `pair.<L>.<M>.intervalOverlap`, `model.<L>.quality.rank` or `pair.<L>.<M>.<axis>.short` token whose
  display carries the caveat counts as the hedge, besides the words *overlap* and *not established*. For
  an established pair the message asks for the pair's paired-result token, never for the word
  *established*, which rule 34 polices in a Root Cause Analysis.
- **Run and label tokens carry a cause** in a Root Cause Analysis (§ 17, *Claim discipline*).
- **The prompts state each rule once, in their claim-discipline block, with an example**, and the Root
  Cause Analysis prompt ends its user message with a *CHECK BEFORE YOU ANSWER* block whose examples use
  the sheet's own tokens.

The job diagnostics record whether each first draft passed every check (`firstDraftClean`), so a
change to the revision policy can later be decided from data.

**The rule catalog.** `BenchmarkReportRuleCatalog` gives every rule number a short name (rule 3 *Digit
in prose*, 16 *Ranking without the overlap caveat*, 31 *Fact placed in the wrong sentence shape*, 33
*Cause without a hypothesis*, 34 *Certainty without support*, 36 *Conclusions leave an axis out*, …), a
plain one-sentence meaning, what a flagged item means for a reader, one fix instruction for the repair
message (holding no digit), and its severity: *removes the item*, *warning*, *warning, text kept*,
*added by Overseer* or *fails the document* (`SeverityFor`: a warning rule warns; a blocking issue on the
headline, or an undropped rule-1 issue such as a missing section, fails the document; a note
`AddedByOverseer` is *added by Overseer*; an undropped note of a rule that would remove the item, such
as a slot kept over its cap, is *warning, text kept*; every other issue removes its paragraph or item).
The wire values are `removesItem`, `warning`, `warningTextKept`, `addedByOverseer` and `failsDocument`.
`LocationLabel` puts a location in words: `sections.conclusions[p1]` becomes *"Conclusions, paragraph
1"*, `observations[2]` *"What the graders recorded, item 3"*.

**The removed text is kept.** A removed paragraph or item, and the paragraph or item a kept warning
flags, is copied into the note's `Text` (raw, tokens unresolved, at most 2,000 characters) and stored
with `ValidationNotesJson`; notes stored before have none. A Root Cause Analysis prints it under
*Removed content* at Full disclosure, tokens resolved under the document's peer naming (§ 17).

**Rule 36 after a drop.** When a removal in `conclusions` leaves an axis status uncited that was cited
before it, and the document is not otherwise fatal, Overseer inserts a code-written paragraph for that
axis **at the axis's place** (axis order intelligence, speed, cost, among the kept paragraphs by the
first axis-status token each cites), citing `{{rca.axis.<axis>.status}}` (and the best hypothesis's
statement where there is one), prefixed *"Added by Overseer, because the writer's paragraph on this axis
was removed:"* in italics. It is not counted against the cap. The rule-36 note of a restored axis is
**replaced** by a note with `AddedByOverseer` (rule 36, location `sections.conclusions`, not dropped):
*"The writer's paragraph on cost was removed; Overseer wrote the axis's status in its place."*, with the
added paragraph as its `Text`; a rule-36 note of an axis that was not restored stays. The removal still
marks the document *Completed with warnings*, and so does an added-by-Overseer note alone.

**Progress and diagnostics.** Each document row of a job carries:

- its **phase** (`BenchmarkReportPackPhases`): `Preparing`, `Writing`, `Checking`, `Revising`,
  `CheckingRevision`, `Storing`, `DrawingCharts`, `Done`; the shortening turn passes through
  `Revising` and `CheckingRevision` too. On `Revising`, `phaseDetail` says why, from the catalog: *"The
  automatic check found 20 issues in the first draft: ranking without the overlap caveat (13), …"*. The
  status values are unchanged (`Repairing` stays on the wire; the UI shows it as *Revising*);
- `firstDraftClean`, and `calls`: every writer call with its kind (`Draft`, `Revision`, `Shorten`,
  `Headline`), start, duration, input, cached-input and output tokens, the reasoning tokens inside the
  output (`reasoningTokens`, null when the provider reported none), cost, finish reason, the messages
  and characters sent, and `replyChars`, the length of that call's reply (null when there was none);
- its first-turn issues, the revision's own issues (the repaired reply's validation before any drop;
  null without a revision), its final issues and, for a failed document, its last raw reply (cut at
  200,000 characters), as `firstTurnIssues`, `revisionIssues`, `finalIssues` and `failedReply`, each
  issue with its `ruleName`, `severity`, `locationLabel`, `text`, `addedByOverseer` and `sentenceOnly`.
  `firstTurnIssues` is sent **while the job runs**, once the document has reached `Revising`; the others
  only once the job has ended;
- `wordCounts`, once the job has ended: per capped slot its location, label, cap and raw word counts in
  the draft, the revision, the shortening turn and the final text, with the final per-paragraph counts.

**The writer's replies.** Each writer call's raw reply, cut at 200,000 characters, is kept on the job in
memory, never in the polled job object and never in the diagnostics file, and so is the user message each
later turn (a repair, second repair, shortening or headline turn) added to the conversation, cut the same
way; the first turn's request, the system prompt and the fact sheet, is not kept, since the writer prompt's
SHA-256 and the request size identify it.
`GET report-packs/jobs/{jobId}/writer-replies` (§ 9) builds one Markdown file from them on request
(`BenchmarkReportPackWriterReplies.BuildMarkdown`, LF line endings): a header marked **INTERNAL** with the
job, pack, capture time, writer, its output limit (*"Output limit 32,000 tokens (reasoning included)"*),
writer prompt SHA-256 and versions; then per document its status, and per call its kind, start, duration,
tokens with their reasoning share (*"16,000 out tokens (14,950 reasoning)"*), cost, finish reason and reply
length; a later turn's request under *"Request (the message this turn added):"* in its own fence; and the
reply verbatim in a fence one backtick longer than its longest backtick run (*"No reply (provider error:
…)."* when there was none; *"(cut at 200,000 characters)"* when cut). Only the latest report job is held
(`BenchmarkReportPackJobManager`), so the replies last until the next report-pack or run-completion job
starts, or Overseer restarts; then the endpoint answers 404. It covers report-pack jobs (the Model
Comparison wizard's Write step and the per-model packs), not the chat consistency reports step or the
run-completion writing dialog. Admin-only, like every endpoint of this controller.

The job itself carries its context: report format, engine, PDF and Word layout versions, the writer
prompt's SHA-256, the writer's provider, model id and thinking level, the output token limit each writer
call is sent (`writerMaxOutputTokens`, reasoning included), the reference model, the entries
(`battery:13`, …) and the preparation notes. The job log's line *"N item(s) removed by validation"* is
written only when N > 0 and names each removed location and rule.

The Report Pack diagnostics print all of it (§ 12): the context with *Writer output limit*, one line per
call with its reasoning tokens when known, *First draft
clean*, `== Word counts ==` (*"Hypotheses: speed — cap 120 — draft 135, revision 128, shortened 117,
final 117 (raw words; a token counts as one) — paragraphs 52, 65"*), the revision's issues and how they
differ from the first draft's (*fixed by the revision*, *still there*, *new in the revision*, matched by
rule and location), each issue as *"rule 16 (Ranking without the overlap caveat, warning) at Hypotheses:
cost, paragraph 2: …"* with a removed item's text indented below, an *Added by Overseer* line for each
paragraph Overseer wrote, `== Preparation notes ==`, and a *Why document N … was completed with
warnings* (or *failed*) section built by the same function as the dialog below. When any call has a
reply, one line per document points to the writer's replies as a separate download; the file itself
holds no reply text.

The wizard's Write step marks such a document *Completed with warnings*, and the notes are stored with
it (`ValidationNotesJson`, returned by the detail endpoint as `validationNotes` and, with rule names,
severities and location labels, as `validationIssues`, § 9), so a dropped item is never silent. **An
info button** beside a *Completed with warnings* or *Failed* chip in the Report Pack panel, and beside
*Written with warnings* under *Documents of this comparison*, opens a dialog, *"Why <document> was
completed with warnings"* (`report-validation-details`): a one-sentence summary that tells shortened
paragraphs from removed items and counts what Overseer added, what was removed (each with its text),
*What Overseer added*, what was kept with a warning, how the draft was checked (*"First draft passed
every check."*, or the first draft's issues by rule, the revision, any shortening turn and the final
count), and what can be done; a `{{…}}` fact name in a message or quote shows as a code chip.
Under each chip a phase line reads, while running, *"Revising the draft — 20 issues found by the automatic
check"* (with its own info button listing the first-draft issues), and after the job *"First draft passed
every check"*, *"Revised once — 20 issues in the first draft"* or *"Revised once and shortened — …"*.
The panel's polite status line announces a phase change, never the clock.

**Writer rules added in format 8** (the R numbers of § 7), in the system prompt of every audience:

- **Peer-aware triage (R13).** Each question line carries the peers' spread (§ 2). Where most peers
  answered a question well and the subject missed it, that is evidence about the model; where every
  model missed it, the writer suspects the chat, its tools, the corpus or the rubric first. Strengths
  and weaknesses prefer points where the subject differs from its peers. The Root Cause Analysis turns
  the same reasoning into code (HQ3 and the controls of § 17).
- **Paired results (R14).** Where a peer's `pairedExcludesZero` is true, the writer says that on the
  same questions the higher-scoring model scored higher on average and that the paired interval excludes
  zero, not adjusted for comparing several models, and never *not established* for that pair; *significant*
  and its kin stay banned.
- **Unavailable figures and fact keys (R15).** An unavailable figure is mentioned only where leaving it
  out would mislead, in plain words and never estimated; a fact key never appears outside its token, and
  the prose never describes the facts list or the fact sheet.
- **Shared values (R16).** A value several peers share is stated once, for all of them.

**Writer rules added with harness 54** (2026-10-08, no format version change;
`BenchmarkReportPackPrompt.SharedWritingRules`), at the end of WEIGHING THE EVIDENCE in the system prompt
of every audience for a run, group or battery subject — run- and battery-completion documents included,
the comparison-wide (§ 15) and chat consistency (§ 16) prompts not:

- **Levers, not training.** *"Never recommend training, fine-tuning or using outputs as training
  targets; recommend a lever the facts name — a tool, a tool guide, the knowledge base, a wiki page, a
  rubric, the grading, or the model and its settings."*
- **Disagreement means a panel disagreement.** *Disagreed* and *disagreement* are written only for an
  answer the facts mark as a panel disagreement; any other gap gives both members' scores.

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
rules. Not an economy tier (Flash, Flash-Lite): the documents go to people outside the team, and one to
three calls per document keep the cost small. The same recommendation is in `ai-benchmark.md` § 3 *Choosing grader
models and effort*, the *How the graders work* guide, the Write step's writer info tip and the
report-writer info tip of the run report's **AI Reports** tab.

**Per document.** The documents ask different things of the writer, and each can be written by a
different model:

- **Executive Summary** — short (about 2,400 output tokens) and plain-language, for decision-makers;
  clear, careful wording matters more than depth. A strong writing model — Claude Opus or GPT Sol — at
  medium effort; it is cheap even with a strong model.
- **Root Cause Analysis** — long (estimated at 10,000 output tokens in model scope and 14,000 in
  comparison scope, both marked for recalibration) and number-dense: it weighs the hypotheses code has
  graded, the known facts and the grader notes, and must follow the claim rules of § 17. The strongest
  scoring-tier reasoning model you trust with numbers — Claude Opus or GPT Sol — at medium effort, high
  if its documents often need the repair turn. It costs several times the summary, roughly $0.3–0.8 per
  run in model scope.
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
- `FactsJson` — the fact sheet; on a comparison or model-scope sheet it also carries the root-cause
  engine's stored result (`BenchmarkReportFactSheet.RootCause`), with the recommended runs and the
  controls used, so a Root Cause Analysis needs no column of its own (§ 17);
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

**The synthesis fingerprint is a column of the run** (2026-10-17, migration
`AddBenchmarkRunSynthesisFingerprint`): `BenchmarkRuns.SynthesisFingerprint`, `nvarchar(16)`, nullable,
holds that same hash (`BenchmarkReportRenderService.SynthesisSha256`), so neither the lists nor a job reads
and hashes every run's whole synthesis JSON. `BenchmarkService` sets it right after each assignment to
`AssessmentJson` or `CoAssessorSynthesisJson` (the final synthesis, parsed or not, and the co-assessor's),
the only places either column is written; the job's fingerprint at generation reads the column and hashes
the JSON only when it is null. A row written before the column existed stays null until a list needs it:
`CurrentFingerprintsAsync` selects the column, reads the JSON of the null rows in one second query, hashes
each, and writes it back with one `ExecuteUpdateAsync` per run filtered on a still-null fingerprint, so no
entity is tracked and no `SaveChangesAsync` runs on the read path; two concurrent backfills write the same
value. (A provider that cannot translate a set-based update, the in-memory test provider, writes it through
the change tracker.)

**The lists read each stored fact sheet once.** `BenchmarkReportDocumentFactsCache`, a singleton over the
global `IMemoryCache` (one size unit per entry, a 30-minute sliding expiry), keeps what a list reads from a
document's `FactsJson` — the peers and their letters, the covered models' labels, covers-all-entries —
by document id. `ListAsync` no longer selects `FactsJson`: it takes each row's facts from the cache, reads
the misses' `FactsJson` in one query and parses each once, and logs *"Listed N report documents, H fact
sheets from the cache, in T ms."* at `Information`. The preview's other covered sets read the same cache,
with one query for the misses. `FactsJson` is written once, with the row, and never updated in place, so
an entry stays right while its document exists; the delete endpoints remove a deleted document's entry,
and an entry whose row is gone is never read, since the lists look entries up by the rows they found.

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
results. Do not share outside the Overseer team.*; its Summary stamp is the one above. The Root Cause
Analysis is Full only and carries the Full stamp, *INTERNAL — contains benchmark questions and
rubrics*. The PDF and Word classification banners use the same text.

The Root Cause Analysis' *Question details* (§ 17) print each question it cites with each run's
**complete answer** under *Answer:*, the question's rubric, the graders' scores and notes and the claim
verifier's rulings. An answer short enough to be stored whole prints as its excerpt,
which is the whole answer; a cut excerpt on a document stored before complete answers were kept prints
under *Answer excerpt:* with the note *"This document predates complete-answer capture; the answer is
shown as the excerpt stored when it was written."* The size guard
(`BenchmarkPdfRenderer.MaxSourceCharacters`, shared by Word) still applies to the rendered Markdown.

Where a document prints a *Tool-use behavior* section, it says that a recorded success or failure describes only
whether a tool call executed — not whether the query was well chosen, the result relevant or the corpus
current. At Full it adds where the per-call detail is: each run's Tool-call log, until the retention sweep
prunes it, when the runs recorded per-call rows (`tools.failed` is available, harness 17 and later), or
that the runs did not record per-call arguments or results.

**Writer independence.** When the stored writer provider equals the subject's provider (trimmed,
case-insensitive, as `BenchmarkComplianceGuard.IsSameProvider` compares), the Executive Summary's *How
reliable this result is* prints a code-rendered caveat naming the writer and the shared provider,
whatever the writer returned. The Executive Summary's reliability section also prints *"The 95% interval is 73–82,
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

The Executive Summary is **offered** at Summary and Full only
(`BenchmarkReportPackRenderer.AllowedDisclosures`, which fills each list item's `allowedDisclosures`),
since its Detailed text is its Summary text; it still **renders** at Detailed (`IsAllowed`), with its
own stamp, so older links keep working. The Root Cause Analysis renders at **Full only**, named or, where
peers exist, anonymized; any other combination answers 400. Its controls are always named: they are
evidence, not peers of the document.

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

**Root Cause Analysis (2026-10-15)** moves per-model documents to **format 13** (`ReportFormatVersion`
12 → 13) and comparison-wide documents to **format 15** (`ComparisonReportFormatVersion` 14 → 15) for the
new facts: the per-model speed, token, cost and configuration facts, the per-question cost and timing
columns, a model-scope subject's `profile.*`, and the `rca.*` facts with the stored `RootCause` result
(§ 17). The `rca.*` facts and the evidence facts only the Root Cause Analysis reads are kept off the
Executive Summary's prompt (`RootCauseFacts.WriterHidden`); they are computed for every comparison and
model-scope sheet, so a later Root Cause Analysis of a stored subject reads the same sheet. Chat
consistency documents stay at format 3. `BenchmarkPdfRenderer.LayoutVersion` moves 8 → 9 (§ 8). A
stored document keeps rendering under its stored version.

**Callouts.** A renderer highlights a key finding, a caveat or a conclusion with a GitHub
alert blockquote, so the Markdown copy stays portable (GitHub draws it as an alert, any other viewer as
a plain quote):

```markdown
> [!IMPORTANT]
> **Key finding.** The headline.
```

The first line is the marker alone (`[!NOTE]`, `[!IMPORTANT]`, `[!TIP]` or `[!WARNING]`), and the next
starts with a bold label as the callout's first bold run: *Key finding* (important), *Conclusion* (tip),
*Caveat* (warning), or a specific one; the research questions' *Question* (note) callouts were printed up
to comparison format 24 (per-model format 20). Only the Root Cause Analysis uses them (§ 17).
The PDF and Word files draw them as tinted, left-ruled boxes (§ 8); the Download Center's printable HTML
(`printable-html.ts`) turns such a `<blockquote>` into `<aside class="report-callout
report-callout--<kind>" aria-label="<label>">`, the marker removed, with print-safe CSS
(`print-color-adjust: exact`).

**Readable Root Cause Analysis (2026-10-15)** moves comparison-wide documents to **format 16**
(`ComparisonReportFormatVersion` 15 → 16): the answer-first structure of the Root Cause Analysis with
its appendices and callouts (§ 17), the `pair.<L>.<M>.<axis>.short` facts, and the availability threats
grouped by reason, which an Executive Summary prints as well. `RootCauseEngine.Version` moves 1 → 2
(established-only decompositions, labels, questions, profiles, folded runs). The per-model format stays
13, although a model-scope Root Cause Analysis takes the same new structure. A Root Cause Analysis
stored before format 16 has no answer slots; each of its answer callouts reads *"Written before format
16; regenerate to get the answers."* (in model scope, *"Written before the answers were added;
regenerate to get them."*). `BenchmarkPdfRenderer.LayoutVersion` moves 9 → 10 and the Word layout 2 → 3
(§ 8).

**Format version 2** added the *Evaluation terms* block and the stand-alone form (§ 1); the fact sheet
gained `purposeStatements`. Format 1 had none of these.

**Provider availability (2026-10-12, with harness 56 in `ai-benchmark.md`)** moves every scope:
per-model documents to **format 12** (`ReportFormatVersion`), comparison-wide documents to **format 13**
(`ComparisonReportFormatVersion`) and chat consistency documents to **format 3**
(`ChatConsistencyReportFormatVersion`). The three numbers are independent; the per-model 12 is not the
comparison-wide 12 of 2026-10-06. What the formats add
([`ai-benchmark-provider-availability.md`](ai-benchmark-provider-availability.md) is the method):

- **Key Figures** gains a fifth code-written bullet, *Provider availability:*
  the rate *of attempts failed at the provider* (*k of n*; *95% CI …*), *plus* the re-runs, from the
  facts' display values.
- **A `### Provider availability` subsection** holds the table *class · count · share* with a footnote
  that defines the classes and the denominator, where a document prints one (the Root Cause Analysis,
  § 17). The Executive Summary has the bullet only.
- **Threats to validity** (`ComparisonThreats` in a comparison) gains code-written lines when errors
  could not be attributed (*"N errors could not be attributed; the upper bound including them is …"*),
  when the subject needed re-runs, and when provider availability was not recorded.
- **Figure A1** (`a1-availability`, § 13) and, in chat consistency documents, endpoint P6, the
  `availability.<period>.*` facts and figure `cc5-availability` (§ 16).

Every block renders **only when the sheet carries the availability facts**: a document stored under an
earlier format re-renders without them, and a legacy run's unavailable facts render as *not recorded*.
`BenchmarkReportChartLayout.CurrentVersion` does not move: the chart layout schema is unchanged and a new
figure key is additive.

**Plain-language Root Cause Analysis and the US percent sign (2026-10-15)** move every scope:
per-model documents to **format 14** (`ReportFormatVersion` 13 → 14), comparison-wide documents to
**format 18** (`ComparisonReportFormatVersion` 17 → 18) and chat consistency documents to **format 4**
(`ChatConsistencyReportFormatVersion` 3 → 4); `BenchmarkPdfRenderer.LayoutVersion` moves 10 → 11 (§ 8)
and the Word layout stays 3. Every GnollBench document writes a percentage in the US style, the sign
right after the number (*96%*, *68–69%*), where it wrote *96 %* (and a chat consistency document a
no-break space); a test reads every golden file for a space before a percent sign
(`BenchmarkReportPercentSignTests`). The Root Cause Analysis gains a *Key terms* section, first-use
glosses and an Appendix H built from one term registry, tied ranks read *2–9 (tied)*, hypothesis labels
read inside a sentence, and the grade notes, grade reasons and null-hypothesis displays are in plain
words (§ 17). Comparison documents of both types open with the document type as a kicker and the
comparison's name as the H1 (§ 15). A document stored earlier keeps its stored facts and writer text,
so its numbers keep their old spacing; it re-renders in the new template.

**Root Cause Analysis claim and question fixes (2026-10-15)** move comparison-wide documents to
**format 19** (`ComparisonReportFormatVersion` 18 → 19) and `RootCauseEngine.Version` 3 → 4; per-model
documents stay at 14 and chat consistency documents at 4. A research question is asked only when its
model is strictly first or last on its axis, says when that place is not established against the
neighbor, and keeps comparative and within-model hypotheses apart; a causal word must cite a hypothesis
graded Low or higher, hedged when Low; an axis in a document with pairs needs a pair hypothesis for
*Probable cause* or better; and the tool-use and answer-length notable findings compare a model with the
median of all models, three or more (§§ 15 and 17). A document stored earlier keeps its stored facts:
its research questions render as before, apart from its footer.

**Root Cause Analysis correctness, completeness and concision (2026-10-15, Comparison #4 review)** move
comparison-wide documents to **format 20** (`ComparisonReportFormatVersion` 19 → 20), per-model
documents to **format 15** (`ReportFormatVersion` 14 → 15) and `RootCauseEngine.Version` 4 → 5; chat
consistency documents stay at 4 and `BenchmarkReportChartLayout.CurrentVersion` does not move. Tied ranks
are per-model spans (§ 15); a provider that counts thinking inside its output (Anthropic) has its
reasoning *Unavailable*, never zero, and one *output, thinking included* row; the grader-family
interaction contrast of the panel diagnostics becomes HQ4's measured evidence; a null with no family
test is judged by its paired-bootstrap interval; the thinking level is the explanation between two
levels of one model, not a confound; a standing null joins the replicate run; at most one response-style
pair, always optional; excerpts are allocated in rounds across questions with suites interleaved;
critical-error resolutions are printed; and the body, appendices and server figures drop repetition and
constant columns (§ 17). A document stored earlier keeps its stored facts and renders with its old
wording where a new fact or field is absent; **its tied-rank text changes**, because ranks are recomputed
from the stored entries at render time.

**Sentence case (2026-10-16, Comparison #4 review, round C)** moves every scope: per-model documents to
**format 17** (`ReportFormatVersion` 16 → 17), comparison-wide documents to **format 22**
(`ComparisonReportFormatVersion` 21 → 22) and chat consistency documents to **format 5**
(`ChatConsistencyReportFormatVersion` 4 → 5); `RootCauseEngine.Version` moves 6 → 7, and the PDF layout
(11), the Word layout (3) and `BenchmarkReportChartLayout.CurrentVersion` do not move. `Prose` applies
`BenchmarkReportSentenceCase.Apply` after resolving tokens, so every sentence of writer text, and a fact
display that starts one, begins with a capital in Markdown, PDF and Word alike; a call site whose text
continues a sentence begun by fixed text passes `sentenceStart: false`. A word is capitalized only when
it is all lower-case ASCII letters, optionally joined by an apostrophe or a hyphen (*none*, *it's*,
*well-known*); a word with a digit, an internal capital, an underscore or a dot, or inside a code span,
is never touched (*gpt-6*, *verboseMode*, *e.g.*), and a full stop after an abbreviation of
`BenchmarkReportSentenceCase.Abbreviations` (*e.g.*, *i.e.*, *etc.*, *vs.*, *cf.*, *approx.*, *incl.*,
*resp.*) ends no sentence. Code-written list items, text after a label, and table cells start with a
capital too (`UpperFirst`: *None*, *Volume*, *Price*, *Not established*, *Run 1*, *Check 1*). Because
`Prose` runs at render time, a document stored earlier gets the capitals when it is downloaded again; its
stored facts (spreads, check facts, notes) stay as stored.

**Controls table thinking level (2026-10-16)** moves per-model documents to **format 18**
(`ReportFormatVersion` 17 → 18); comparison-wide documents stay at 22, chat consistency documents at 5,
and `RootCauseEngine.Version`, the PDF layout and the Word layout do not move. The `### Controls` table of
a run- or battery-completion Root Cause Analysis names each control's thinking level once: a control
label that already ends with *(level)*, as a configuration display name often does, prints as it is;
any other label gets the level appended. A document stored earlier re-renders with the level once.

**Control names with their thinking level (2026-10-16)** moves `RootCauseEngine.Version` 7 → 8; no
report format version, PDF layout or Word layout moves. A control is named with its
thinking level once wherever it appears — the `rca.controls.<n>.model` fact, and so the writer's prompt
and prose, the `root-cause-controls` endpoint and a document's stored controls in the AI reports tabs —
by `RootCauseControl.LabelWithLevel()`, the same rule the controls table uses; `ModelLabel` stays the
configuration's name as recorded. A document stored earlier shows the level in its Controls line and
table, but its prose keeps its stored fact until it is written again.

**Comparison #4 warnings round (2026-10-17)** moves every scope: comparison-wide documents to **format
23** (`ComparisonReportFormatVersion` 22 → 23), per-model documents to **format 19**
(`ReportFormatVersion` 18 → 19) and chat consistency documents to **format 6**
(`ChatConsistencyReportFormatVersion` 5 → 6); `RootCauseEngine.Version` moves 8 → 9; the PDF layout and
the Word layout do not move. The writer prompt of every scope changes with it, and so its SHA-256.
A mean figure reads *"a mean of 21.7 s per question"*; a rule with several shares gets
`rca.rule.<id>.shareSpan` and `.count`, and rule 38 keeps a share beside its own hypothesis; the
comparison sheet gains `comparison.runsPerSuite`, `.suiteCount`, `.memberRuns` (battery comparisons) and
`.configDifferences`, and a pair's `configDifferences` names no model and prints no parenthesis
(*"thinking level, medium against high"*); the cheapest-model bullet of *What we found* states the paired
tests that cover the model; a share interval wider than one hundred percentage points fails a robustness
check (§ 17); every count fact says what it counts, with rule 39 against repeating it (§ 3); a shortening
turn that brings every section within its cap leaves the document *Completed* (§ 3, item 5). A document
stored earlier keeps its stored facts and status; its tables print its bare counts as before.

**Comparison #4 prose-quality round (2026-10-17)** moves comparison-wide documents to **format 24**
(`ComparisonReportFormatVersion` 23 → 24) and per-model documents to **format 20** (`ReportFormatVersion`
19 → 20); chat consistency documents stay at 6, and `RootCauseEngine.Version`, the PDF layout and the Word
layout do not move. The writer prompt of the Root Cause Analysis and of the comparison-wide Executive
Summary changes with it, and so its SHA-256. A research question's box prints its measured signals under
its answer strength (§ 17), and the writer answers without citing them (rule 40); the response-style fact
reads as a noun phrase, *"Completeness 30.0 points below Accuracy, its lowest dimension"*, in every
document that prints it, so rule 31 no longer counts it as sentence-shaped; the next runs gain
`rca.run.<n>.purpose`, the writer is given their order, and the *model-selection runs wait* line is printed
only when a run waits (§ 17); the writer is told never to cite an unavailable figure's token, the price note
in the conclusions, a count of the nulls Overseer already lists, or *across* before a share span; and
rules 40 to 43 are added as warnings (§ 3). The comparison-wide `comparison.runsPerSuite`, `.suiteCount`
and `.memberRuns` of a battery comparison now read the members' whole-number battery counts, which they
had always reported as unavailable. A document stored earlier keeps its stored facts, so its response-style
display and its question boxes render as before.

**Root Cause Analysis Summary findings (2026-10-17)** moves comparison-wide documents to **format 25**
(`ComparisonReportFormatVersion` 24 → 25) and per-model documents to **format 21** (`ReportFormatVersion`
20 → 21); chat consistency documents stay at 6, and `RootCauseEngine.Version`, the PDF layout and the Word
layout do not move. The Root Cause Analysis' Summary loses *The questions*, the writer output its four
answer slots and the prompt THE QUESTIONS and the `rca.question.*` facts, and *What we found* prints the
findings code ranks, the grader-family sentence among them (§ 17). The writer prompt changes, and so its
SHA-256. Rule 40 keeps only its price-note half. A document stored earlier renders without its question
boxes; its stored answers are not shown.

**Root Cause Analysis cost insights, unexpected results and a shorter report (2026-10-17)** moves
comparison-wide documents to **format 26** (`ComparisonReportFormatVersion` 25 → 26) and per-model
documents to **format 22** (`ReportFormatVersion` 21 → 22), and `RootCauseEngine.Version` 9 → 10; chat
consistency documents stay at 6, and the PDF layout (11), the Word layout (3) and
`BenchmarkReportChartLayout.CurrentVersion` do not move. The writer prompt of the Root Cause Analysis
changes, and so its SHA-256. The engine splits a cost pair whose two sides are not billed for the same
token kinds into input and output, with the price card and caching apart from token volume, and gives
every decomposed cost pair its cost ratio at equal prices (`rca.cost.<F>.<S>.volumeRatio`); it adds the
same model at adjacent thinking levels and the pairs of unexpected results to the analyzed pairs, stores
the unexpected results (U1, U2, …), states HQ5 once for a comparison, and gives each profile where the
model gained points, leaving a weakness every model shares out of where it lost them; the price note is
gone (§ 17). A price-card difference is left out of the hypotheses tables, figure R4 and the writer's
facts, and named in one line. The Root Cause Analysis gains an *Unexpected results* section; *What we
found* adds the unexpected results and token volume, states value for money without a cost multiple and
the thinking levels in one bullet; *At a glance* prints the figures alone; *Why each model scored as it
did* is one block per model; each axis' nulls are one sentence; the Conclusions state a reason every axis
shares once; and Appendices A, B, C, F and G and the grader-findings table print what they share once
(§ 17). The writer gets an optional `unexpectedResults` slot, and rules 44 and 45 are added as warnings
(§ 3). The Executive Summaries of both scopes are unchanged apart from their footers. A document stored earlier
keeps its stored result: written under engine 9 or earlier, it has no unexpected results (the section is
left out), no gained-points entry, no `Grouped` or `VolumeRatio` on its cost pairs (the *At equal prices*
cells read *—*) and its five-component cost split; its price note is no longer printed.

**Comparison format version 14** (2026-10-14) moves comparison-wide documents only
(`ComparisonReportFormatVersion` 13 → 14); per-model documents stay at 12 and chat consistency documents
at 3. It adds the reference model and the baseline model, the failure profile, the comparison with the
reference and the notable findings (§ 15, *Format 14*). Every new section renders only when the sheet
carries its facts, and the new writer slots are optional slots, so a document stored under format 13 or
earlier renders as before apart from its footer and the fact labels it shares (§ 2, *Fact labels*).

**Format version 12** (2026-10-06) is the format of the comparison-wide documents
(`ComparisonReportFormatVersion`, § 15, 13 since 2026-10-12, 14 since 2026-10-14); `BenchmarkReportPackRenderer.CurrentFormatVersion(scope)` gives
each scope's version. Per-model documents were written under format 11 until 2026-10-12. Every Report Pack document of a
numbered comparison prints its comparison (§ 15), and every per-model document prints a named peer
without its letter, a *Letter* column in *Compared models* and tied ranks (*"1–2 of 2 (tied: intervals
overlap)"*; *"joint 1st of 2 (intervals overlap)"* before 2026-10-15), whatever format it was written
under.

**Chat consistency format version 2** was the format of the chat consistency documents
(`ChatConsistencyReportFormatVersion`, § 16, **3** since 2026-10-12), numbered on its own: `CurrentFormatVersion(scope)`
returns it for `ChatConsistency`, the document stores it, and its reproducibility section and Markdown
footer print it; the PDF and Word covers of a chat consistency document have no *Generated format* row
(§ 16).

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

**Format version 11** (per-model, 2026-10-04, with harness 49 in `ai-benchmark.md`) comes from the
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
- **Mean time (N4)**: the fact `speed.modelTimeMean` (*Mean answer time*, from
  `BenchmarkModelComparisonSpeedDto.ModelTimeMeanMs`, formatted like the median); *Key figures* reads
  *"median answer time 14.7 s, mean 17.2 s"* and *Speed and cost* adds *Mean answer time*.
- **Zero-count tokens (R1)**: the fact `answers.scored` (*Scored answers*, the denominator of
  `errors.critical`), a writer rule that an *N of M* token is a noun phrase — never after *no* or the
  object of *made*; to say none occurred, *"no critical errors across all {{answers.scored}} answers"* —
  and validator rule 19 (§ 3).
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
  reliable this result is*; the *Significance* line of *Threats to validity* is gone, and the
  comparison's *Instead* instruction is never printed.
- **Named peers (R2)**: a Named copy names the peers in the interval-overlap figure and in *Judge-dependent
  pairs* instead of giving their letters (§ 6).
- **Whole-number differences (R3)**: every *Difference* — dimension, band and question — is the
  difference of the two whole numbers printed beside it (§ 2).
- **Cover (R6)**: with peers, the PDF and Word cover's subject line reads *"{Suite} · run #68 · compared
  with 4 models"* (*group #N* for a group subject), and its facts table replaces the *Peers* row with
  *Compared with* — the peers' labels and *(4 models)* in a Named copy, *"4 models (A to D), identities
  withheld"* in an Anonymized one — and *Pricing basis*. The Markdown front matter lists *Compared with*
  and *Pricing basis* in place of *Peers*. A stand-alone document's cover is unchanged (§ 8).
- **File names (R7)**: `vs-<N>-models_` after `run-<id>_`, or first for a group subject, when the
  document has peers (§ 8; since 2026-10-06 the comparison part names the peers or the comparison key).
- **Fact sheet in Markdown only (R8)**: the PDF and Word copies of a document that carries the fact
  sheet (today the Root Cause Analysis, § 17) leave out the *Fact sheet* section, its heading and its
  contents entry (`BenchmarkReportRenderOptions.IncludeFactSheet = false` for native renders); the
  Markdown copy keeps the JSON. Until 2026-10-15 the section printed only *"The fact sheet is in the
  Markdown copy of this document."*
- **No Rank line (R9)**: the Executive Summary's separate *Rank* line of format 7 is gone; the rank is
  on the *Intelligence* line.
- **How it compares (R10)**: the table gains a *Critical errors* column and is followed by a compact
  *Dimensions* table (subject, peer mean, difference); the Executive Summary's items no longer print
  *(both graders agreed)* (§ 2).
- **Paired results (R14)**: the `peer.X.pairedExcludesZero` fact (§ 2); validator rule 16 accepts the
  paired result for that peer (§ 3).
- **Withheld graders (R18)**: in an Anonymized copy a grader whose provider is withheld reads *a model
  from a withheld provider* (§ 6).
- **New facts**: `peer.X.pairedExcludesZero`, `comparison.peerRuns`, and the per-question `peerMin`,
  `peerMax` and `peersAbove` (§ 2).
- **Writer prompt**: the per-question peer spread, the peer-aware triage rule and rules R13 to R16 (§ 3);
  the parser's `KnownSlots` gain `comparison`.
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
- **Writer prompt**: the PEERS block, the subject's explanation, the *NO SIGNIFICANCE TEST* block, the
  readability rules (§ 3) and the `comparison` slot. Validator rules 16–17 (§ 3). The Executive
  Summary's output-token estimate is 2,400.
- **Footer**: from format 7 the checks line names *interval-overlap wording* and *hype words* among the
  automatic checks; an older document keeps the wording of the checks it received.

A document stored under format 6 renders as before: *How it compares* and the *Rank* line appear only when
its sheet has `peer.*` facts, and the `comparison` paragraph only when the slot is present.

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
- **Displays (R5)**: the response-style fact reads *"Completeness X points below Accuracy, its lowest
  dimension"* (from per-model format 20 and comparison format 24, a noun phrase; *"Completeness is the
  lowest dimension, X points below Accuracy"* before) or *"No response-style conflict"*; its value stays the
  boolean, and a display stored before format 6 loses its leading *yes: * when rendered. `quality.scoredItems` reads *"N of M questions"*.
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
  fact about the game, and never list refuted claims in the one-sentence result. Validator rule 14
  (`VerifierInSummaryRule`, § 3) flags *verifier* in the headline. The warning rules — 12, 13 and 14 (`IsWarningRule`) — keep their text,
  ask for the repair turn and mark the document CompletedWithWarnings.
- **Response-style note (A2)**: a new writer rule — the response-style note is Overseer's own
  observation, never attributed to a grader.

A document stored under format 5 renders with the new labels and displays; where it lacks a fact (the
reference reader's, say) the line is omitted, and where its confidence paragraph already places the
interval the computed sentence is not added.

**Format version 5** (2026-09-29) comes from a review of the run-73 Executive Summary
and the PDFs of the since-retired technical report:

- **Provider wording**: the single-grader support labels read *One grader — different provider* and *One
  grader — same provider as the model* (§ 2); *Setup and method* and *Threats to validity* speak of a
  grader's provider. A sheet stored under format 2–4 is normalized when it is read
  (`BenchmarkReportFacts.NormalizeSupportLabels`, in both the render and the anonymized appendix copy), so
  its items keep their single-grader label rather than falling to *Computed*. The stored property
  `sameFamilyAsSubject` keeps its name, and a raw fact-sheet appendix still prints it.
- **Writer independence**: a writer sharing the subject's provider gets a code-rendered caveat (§ 6),
  also on re-rendered format 2–4 documents.
- **Interval span**: the new fact `quality.intervalSpan` (the printed upper bound minus the printed lower
  bound, *"9 points"*); the writer prompt's confidence slot names `{{quality.interval}}` and
  `{{quality.intervalSpan}}` instead of asking for the interval's width; validator rule 13 (§ 3); the
  Executive Summary's code-rendered span sentence (§ 6).
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

**Format version 4** (2026-09-29) made the disclosure levels of the since-retired audience 2 differ
visibly. Nothing stored changed, so a format 3 document renders the same way.

**Format version 3** (2026-09-29) came from a review of the run-completion documents
written for run 73, which found the evidence repeated under every item, raw fact keys in the text, the
quality index printed as *±* a half-width, refuted grader statements read as errors of the answer, rubric
source notes and cut-off tables leaking into the writer's input, and the slots restating their lists:

- **Evidence**: the Executive Summary prints plain support labels (§§ 1–2); no question expansion under
  an item.
- **Labels**: a human label for every fact key (`BenchmarkReportFactLabels`); *Serious errors* became
  *Critical errors*.
- **Speed and cost** table (median and 90th-percentile answer time, cost per question, cost per run, the
  total cost per run with the graders included, input and output tokens per question, from the new facts
  `tokens.inputPerQuestion` and `tokens.outputPerQuestion`), each row only when its fact is available.
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
- **Writer input**: the rubric's `SOURCE` paragraphs are removed from the prompt
  (`BenchmarkReportPackPrompt.WithoutSourceParagraphs`, shared by every writer prompt and by the
  rendered rubrics). A paragraph is a line whose label is `SOURCE` or `SOURCES`, plain or in bold
  (`**SOURCE** —`, `__SOURCE__:`), followed by a colon, a dash or the line end, together with its
  continuation up to a blank line, a bold-label line (`**NOTE**`) or a heading; every such paragraph of a
  rubric is removed. Before 2026-10-16 only the plain label matched, so the bold form the rubrics use
  reached both the writer and the document; answer excerpts are cut
  at a sentence end or line break and never inside a Markdown table (*"… (a table follows in the
  answer)"*).
- **Writer prompt**: the one grader vocabulary (§ 2); a job for each slot; the reliability slot says when
  a grader shares the subject's provider; a Conflicting row whose two texts are about different things is
  left out; a refuted grader's statement means the grader was wrong.
- **Front matter**: the PDF and Word downloads render with `IncludeFrontMatter = false`, since their
  cover prints the stamp and the facts; Markdown and HTML keep it (§ 8).

A document stored under format 2 keeps rendering: where it lacks a role, a count or a fact, the renderer
falls back to what format 2 printed. Two observations of that review are deliberately left for a later
benchmark round: on one run-73 question the claim verifier's verdict contradicted its own rationale, and
the synthesis can merge unrelated items into one Conflicting row.

The goldens include the stand-alone `exec_standalone.md`, rendered from the fixture's single-run
subject; the comparison-wide documents' `comparison_*.md` and `comparison_subset_*.md`; the
comparison-scope prompts' `prompt_*_comparison.txt`; and the Root Cause Analysis goldens
`comparison_rca_full_named.md`, `comparison_rca_full_anonymized.md`, `rca_full_named.md` (a per-model
pack), `rca_run_with_controls.md` and `rca_run_no_controls.md` (run completion),
`rca_battery_standalone.md`, `prompt_rca_comparison.txt` and `prompt_rca_standalone_run.txt`. To regenerate every golden after an intended change, set
`$env:OVERSEER_UPDATE_GOLDENS = '1'` and run the normal test command (`ai-benchmark.md` and the
`testing-guidelines` skill give it), then review each diff before keeping it.

**Configuration**, read only at generation:

| Key | Default | Meaning |
|---|---|---|
| `Benchmark:ReportPack:MaxOutputTokens` | 32000 | The writer call's output limit, reasoning included; lowered to the writer model's catalog limit (`BenchmarkReportPackPreparation.WriterMaxOutputTokens`). The provider request timeout (`AiRateLimitSettings:RequestTimeoutSeconds`) bounds the call, so the cap divided by the writer's output rate must stay under it |
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
| **External** | Executive Summary (and a chat consistency analysis' Provider Issue Report) only; internal-only rows, the Root Cause Analysis among them (*"Internal only: contains benchmark questions and rubrics"*), are listed but unselectable, with their reason | Summary (Detailed as an option) | Anonymized (Named as an option, with a warning) | PDF |
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
- **Delete** (Report Pack and Chat Consistency Report documents only; a run's or a battery run's own
  documents are deleted from its report) asks a
  nested confirmation, then moves focus to the next row, else the previous, else the *Documents* heading.
- **More actions**, in the wizard only, holds *Update charts* and *Remove charts* (§ 13).

The panel is an inline-size container: the package column sits beside the list from 48rem of its own
width, and the filter bar sticks from 36rem. The card list lays itself out by its own width: below 30rem a
card puts its actions on their own line and its options one per line, and the filters scroll sideways in
one row.

Each row offers its formats PDF first, then Word: pack documents and the run report PDF, Word, Markdown
and HTML; the tool-call log PDF, Word and Markdown; diagnostics PDF, Word and Text. The choices are
remembered per browser in settings **version 5** (`STORED_SETTINGS_VERSION`), keyed by document category:
`executiveSummary`, `rootCauseAnalysis`, `providerIssueReport` and the run and battery file kinds.
Versions 2 to 4 are migrated: every package loses the choices remembered for the two retired document
types (`technicalReport`, `internalBrief`); versions 2 and 3 also gain the Provider Issue Report's
default choice, and version 2 loses the Internal package's remembered formats, so every admin meets the
Internal package's Word default once. Any other version is ignored. The
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
slug `executive-summary` or `root-cause-analysis` (`BenchmarkPdfFileNames.KindSlug`; a chat consistency
document's `provider-issue-report` is in § 16):

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
  *Source {first 16 hex of the source hash} · PDF layout 10* (*PDF layout 10* alone for a chat consistency
  document, whose cover and footer carry no hash, § 16), and a classification banner — amber
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
  a footer with the short classification, the source hash and layout version, and *Page X of Y* — for a
  chat consistency document (`BenchmarkPdfDocumentInfo.FooterText`) the classification, then *Chat
  consistency analysis #4 · Executive Summary · page X of Y*; for
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
- **Headings before short tables** (*PDF layout 7*, 2026-10-10): a run of headings followed by a short
  table (`IsShortTable`) is kept on one page together with it, so a heading never stays alone at a page
  foot while its table moves to the next page.
- **Bold labels before tables** (*PDF layout 8*, 2026-10-10): a paragraph whose whole text is one bold
  span, such as `**Where the chat stands**` (`IsBoldHeadingParagraph`), introduces the block after it as a
  heading does. Followed by a short table, the label is kept on one page with it (with any headings
  before the label as well); followed by a longer table, it needs room on its page for its own line and
  the first rows (`KeepWithTableHeight`), else it moves to the next page; followed by anything else, room
  for its own line and the first lines after it.
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
  workaround. From *PDF layout 9* an inline code span is set at **0.86** of the surrounding text size
  (`BenchmarkPdfStyle.InlineCodeScale`), so a long identifier no longer towers over its line.
- **PDF layout 9** (2026-10-15, `BenchmarkPdfRenderer.LayoutVersion = 9`): besides the inline code size,
  a short table that already sits in a no-break group with its headings or bold label (the three cases
  of layouts 7 and 8) is composed with `keepTogether: false`, so no unbreakable block is nested inside
  another and the heading is no longer left on a page of its own; and a server-drawn figure (§ 13)
  prints full width in a row of its own, under the 60 % height cap, never in a figure row and never
  placed by the chart layout. The Word writer reuses the composer's figure rule, so a server-drawn figure
  is full width there too; the Word layout version stays 2.
- **Callouts** (*PDF layout 10*, 2026-10-15, `BenchmarkPdfRenderer.LayoutVersion = 10`): a blockquote
  whose first line is a GitHub alert marker alone — `[!NOTE]`, `[!IMPORTANT]`, `[!TIP]` or `[!WARNING]`,
  ignoring case — is a **callout** (`BenchmarkPdfMarkdownComposer.CalloutOf`; Markdig's alert extension
  is not enabled, so the marker arrives as literal text and is matched on the source line). It prints as
  a box: a light tint, a 3 pt left rule in the kind's color, and the body in the ordinary ink, not the
  muted quote color. Its first bold run is its label (*Question*, *Key finding*, *Conclusion*, *Caveat*,
  or a specific one such as *Not tested by code*), written by the renderer; a callout without one gets
  its kind's default label, so the box never relies on color alone. The marker never prints, and any
  other blockquote is unchanged. Colors (`BenchmarkDocumentPalette.Callout`, rule on tint): note
  `#2F6FB5` on `#EAF2FB`, important `#7048B8` on `#F2EDFA`, tip `#2B7A4B` on `#E8F4EC`, warning
  `#9A6200` on `#FFF4D6`; the ink `#1B1B1B` on every tint is at least 14.9 : 1, and every rule at least
  4.5 : 1 against its tint and 5 : 1 against white (`TheCalloutColors_MeetTextContrast`). The tints are
  light enough that a grayscale print keeps the label and the rule.
- **Keep-together** (*PDF layout 11*, 2026-10-15, `BenchmarkPdfRenderer.LayoutVersion = 11`): a callout
  whose estimated height is at most a third of the text area (`CalloutKeepMaxShare`) prints on one page
  (`PreventPageBreak`), with a heading run directly above it; a longer callout still breaks. A table is
  kept together only when it is short (at most 10 body rows) **and** its estimate fits on a page
  (`KeepsTableTogether`); a short table taller than a page flows from where it starts, so it no longer
  pulls its heading off a nearly empty page. A heading, its lead paragraph and a kept table go in one
  group when the whole fits; otherwise the heading and paragraph get `EnsureSpace` for themselves plus
  the first table rows. Nothing forces a page break per heading. For a comparison-wide document the
  cover prints the comparison's name (`BenchmarkPdfDocumentInfo.HeadingTitle`) once, its *Comparison* row
  keeps only what the name does not say (*"9 models · computed 2026-10-15"*), and the metadata title
  still names the document type; the body's H1 is dropped whether or not it carries the type.
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
  *Source {first 16 hex} · Word layout 3* (*Word layout 3* alone for a chat consistency document) and
  the classification banner; the subject line, the facts table
  and the banner text come from the same document information as the PDF's, so they change with the
  PDF's cover. The source hash follows the PDF's rule, charts included. The table of contents follows
  under the PDF's rule, as a real `TOC` field pre-filled with links to the `##` sections and marked for
  Word to refresh (page numbers included) when the file is opened; Word may ask once to update fields.
- **Every page**: from page 2 a header with the emblem, *GnollBench · {kind}* and, at the right tab, the
  PDF header's text (a comparison heading cut with an ellipsis to the characters half the text width
  holds); a footer
  with the short classification, the source hash and layout version, and *Page X of Y* as `PAGE` and
  `NUMPAGES` fields — for a chat consistency document the PDF's footer text, *Chat consistency analysis
  #4 · Executive Summary · page X of Y*, at the right tab; for internal documents Word's own *INTERNAL* text watermark, which *Design →
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
- **Callouts** (*Word layout 3*, 2026-10-15, `BenchmarkWordRenderer.LayoutVersion = 3`): a callout (as in
  the PDF) is written as paragraphs in one of four styles, *Callout Note*, *Callout Important*, *Callout
  Tip* and *Callout Warning*, each shaded with the PDF's tint and given a 3 pt left border in its rule
  color; the label is bold, from the Markdown or the kind's default. A list inside a callout is written
  in the ordinary list styles, outside the shading.
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
- `audiences` are numbers (1 Executive Summary, 5 Root Cause Analysis); each scope accepts only its own
  list (`ScopeAudiences`: model and comparison scope 1 and 5, chat consistency 1 and 4), and any other
  audience, a retired 2 or 3 included, is refused 400 with *"<name> is not written for <model |
  comparison | chat consistency> documents."*; `batteryRunIds` names battery results, and a request
  naming any may name no run or group;
- `replaceDocumentIds` names the written documents the job replaces (§ 15).

- `POST /api/admin/benchmark/report-packs/preview`: The fact sheets and prompts without a model call.
  Model scope: the first subject, its peers, the estimated tokens, cost and `contextWindowShare` per
  document and subject, the same-provider warning and any refusal. The preview estimates **every document
  its scope offers** (`ScopeAudiences`), whatever `audiences` asks for, each estimate with
  `exceedsContextWindow` (its `contextWindowShare` above `ContextWindowRefusalShare`, 0.9; false when the
  window is unknown), while `estimatedTotalCostUsd` and `estimatedTotalCostWithRepairUsd` sum only the
  requested documents; so a check change needs no new preview, and the client refuses a checked document
  too large for the writer itself (§ 12). The preview therefore never answers the context-window refusal;
  the start still does, over the requested documents. Every estimate is a **range**: its
  low end (`estimatedCostUsd`, `estimatedTotalCostUsd`) counts the first call only, its high end
  (`estimatedCostWithRepairUsd`, `estimatedTotalCostWithRepairUsd`,
  `BenchmarkReportPackService.EstimateCostWithRepair`) adds one repair turn per document, which resends
  the prompt with the reply and the issues; the panels show *"first call only, to one repair turn per
  document"*. The run, battery and chat consistency estimate endpoints return the same fields. A subject with no peer answers 200
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
  writer that is a covered model — is answered 200 in `refusal`.

  **The preparation cache.** The comparison, the paired tests, the fact sheet and the prompts are
  computed once per preparation and kept in `BenchmarkReportPackPreviewCache`, a singleton over the global
  `IMemoryCache` (one size unit per entry, a 15-minute absolute expiry), so a writer change answers
  quickly. An entry (`BenchmarkReportPackPreviewEntry`) holds what the preview and Start's
  pre-flight read — the subject, peers, covered models, covered set, counts and each offered document's
  prompt length in characters — never the preparation itself; model scope keeps one entry per subject.
  The key (`BenchmarkReportPackPreviewCache.KeyAsync`) is a SHA-256 over: the request's scope, sources in
  order, pricing basis, covered models and reference (never its writer, `audiences`,
  `acknowledgeSameProvider` or `replaceDocumentIds`), and today's UTC date on the *Current* basis; the
  model-scope subject; `AnswerExcerptChars`, the battery detail and prompt limits, the
  `Benchmark:ReportPack` and `RecommendedModels` settings; both report format versions and
  `RootCauseEngine.Version`; the numbered comparison's id; the latest baseline model setting; each group's
  name and members; each battery run's state, members and latest analysis; for each source run its
  scoring, `SynthesisFingerprint`, status, re-run, re-assessment, repair and provider-ledger figures, and
  its answers' count, quality sum and latest grading and re-run times; and the enabled Benchmark-role
  configurations with the configurations the runs name, with their pricing. Anything else can make an
  entry up to 15 minutes stale. In comparison scope, on a miss the comparison is computed once and handed to the paired tests
  (a `BenchmarkPairedTestsService.CompareAsync` overload that takes it, used for both families, whose
  result is not put in the wizard's paired-test cache), so it is no longer computed three times. Start's
  pre-flight reads the cache too; the job always prepares fresh. Each preview logs one `Information`
  line, *"Report Pack <scope> preview: preparation cache hit|miss|not used; key … ms, compare … ms,
  preparation … ms, lists … ms, estimate … ms."*
- `POST /api/admin/benchmark/report-packs`: Start a job. Returns 202 `{ jobId }`. The job's view carries
  `scope` and `comparisonId`, and each document row its `subjectKey` and `subjectLabel` (a model's label,
  or *Comparison #12*, with *· 2 of 5 models* for a subset).
- `POST /api/admin/benchmark/report-packs/layout-preview`: One document laid out with placeholder text,
  as a PDF; multipart, no model call, nothing stored (§ 15).
- `GET /api/admin/benchmark/report-packs/jobs/{jobId}`: Job progress, per document.
- `GET /api/admin/benchmark/report-packs/jobs/active`: The running job, or 204.
- `GET /api/admin/benchmark/report-packs/jobs/{jobId}/writer-replies`: The writer's raw replies of a
  held job as one Markdown file (`text/markdown; charset=utf-8`, § 3, *The writer's replies*); 404 when
  the job is no longer held. It answers while the job runs too, with the calls so far.
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
6. No audience, or an audience model scope does not write — 400.
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
configuration (provider, model id and thinking level) — 400; no audience, or one comparison scope does
not write — 400; a prompt above 90 % of
the writer's context window — 400; an id in `replaceDocumentIds` this job does not write again — 400; a
requested document already written for this covered set and not named in `replaceDocumentIds` — 409
(*"The <document name> of these models is already written for this comparison. Delete it, or rewrite it
to replace it."*); the spend cap — 429; a writer sharing a covered model's provider without
`acknowledgeSameProvider` — 409 with the warning naming those models; a job already running — 409.
Either scope numbers the comparison before the job starts (§ 15).

- `POST /api/admin/benchmark/runs/{runId}/report-documents`: Write a finished run's missing
  run-completion documents now (§ 11). Body `{ writerModelConfigurationId, audiences?, acknowledgeSameProvider, replaceDocumentIds? }`:
  `audiences` names the documents to write (1 Executive Summary, 5 Root Cause Analysis); null or empty
  writes every missing one. `replaceDocumentIds` names the run's own Root Cause Analysis that a
  **Rewrite with controls** replaces (§ 17): the new document replaces it once stored, through the same
  replace-after-persist path as a Report Pack rewrite, matched on subject key and audience; the
  automatic path never replaces anything. The writer is recorded on the run as its report
  writer, replacing an earlier one. Returns 202 `{ runId, status, audiences }` with the status Pending
  and the documents the job will write. Refusals, in order: no body — 400; an unknown run — 404; a run
  that has not finished (Completed, CompletedWithErrors or CompletedWithLimits) with a final synthesis —
  400; a job for the run Pending or Writing — 409; a requested audience that is not a run-completion
  document — 400 (`InvalidAudienceMessage`, *"Only the Executive Summary and the Root Cause Analysis are
  written for a run."*); a requested document already written and not named in `replaceDocumentIds` —
  409 (*"The <name> is already written. Delete it first to write it again."*), or, with
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
- `GET /api/admin/benchmark/runs/{runId}/root-cause-controls`: The controls a Root Cause Analysis of the
  run would use if written now (§ 17), as `RootCauseControlsDto`: `controls`, at most three, in the
  order the analysis uses them, each `{ modelLabel, provider, runId, batteryRunId, completedAtUtc,
  sharedQuestions }` (`modelLabel` is the control's model with its thinking level once;
  `completedAtUtc` carries the control's start time); `controlLimit`, `None`,
  `Moderate` or `Low`, against the subject's provider; and `controlRun`, the `control` run the analysis
  would propose when fewer than two controls qualify — taken from the same run-completion preparation
  the Root Cause Analysis uses, and null when the run has no final synthesis yet or no other provider
  has a configured Benchmark-role recommended model. The same `RootCauseControls.FindAsync` serves the
  preparation, so the tab shows exactly the controls the document would use. No model call; 404 for an
  unknown run; 499 when the client aborts.

### Battery-completion documents (`AdminBenchmarkBatteryReportsController`)

Under `api/admin/benchmark/batteries/runs/{batteryRunId}/report-documents`, with the request and
response shapes of the run endpoints above (§ 14):

- `POST …/report-documents`: Write the finished battery run's missing battery-completion documents with
  the writer in the body, which becomes the battery run's writer. 202 with the battery run's Pending
  status (its id in `runId`) and the documents to write. Refusals, in order: no body — 400; an unknown
  battery run — 404; a battery run that has not finished, or whose latest analysis is missing, stale or
  incomplete — 400; a job for it Pending or Writing — 409; an audience other than the two — 400
  (*"Only the Executive Summary and the Root Cause Analysis are written for a battery run."*); a
  requested document already written and not named in `replaceDocumentIds`, or with none requested
  every one written — 409 (`AllWrittenMessage`, *"This battery run already has every
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
- `GET /api/admin/benchmark/batteries/runs/{batteryRunId}/root-cause-controls`: The battery run's
  controls (§ 17), as the run endpoint returns them (`RootCauseControlsDto`), each with its
  `batteryRunId`; `controlRun` comes from the battery-completion preparation and is null while the
  battery result cannot be written about. 404 for an unknown battery run.

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
- `GET /api/admin/benchmark/report-documents/{id}`: Detail: metadata, validation notes and the facts JSON;
  for a Root Cause Analysis also `recommendedRuns` and `checks`, read from the stored `RootCause` result,
  and, for a run- or battery-completion one, the `controls` it used (`RootCauseControlDto`, § 17); null on
  other documents.
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
  413 over the size limit. With
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
  for any chart the validation refuses and for an invalid layout; 404 for an unknown document. A browser
  upload carries browser-drawn keys only; the document's server-drawn charts (§ 13) are kept.
- `DELETE /api/admin/benchmark/report-documents/{id}/charts`: Remove the document's charts. 204, also
  when there were none or chart storage is not configured; 404 for an unknown document.
- `POST /api/admin/benchmark/report-documents/{id}/charts/redraw`: Redraw a stored Root Cause Analysis's
  server-drawn charts R1–R4 from its `FactsJson` (§ 13), for a renderer change, replacing the ones drawn
  before and keeping the uploaded charts; no model call. 200 with the stored set's summary; 400 `{ error }`
  for a document that is not a Root Cause Analysis (*"Only a Root Cause Analysis has charts drawn by
  Overseer."*), for chart storage that is not configured, for a fact sheet that cannot be read or holds
  nothing to draw from, and for a drawing or storage failure; 404 for an unknown document; 499 when the
  client aborts. The Root Cause Analysis rows offer it as **Redraw charts**.

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
  run-completion documents are written again only after they are deleted, or, for a Root Cause
  Analysis, by **Rewrite with controls**, which replaces it once the new one is stored (§§ 11, 17). A
  document's charts can be replaced or removed at any time (§ 13), and a Root Cause Analysis's
  server-drawn charts redrawn from its stored facts; that changes its PDF and Word copies but not the
  stored row, and involves no model call.
- It never applies a change. A Root Cause Analysis' hypotheses, checks and recommended runs are inputs
  to the triage of the `server_benchmark_to_chat_transfer` skill, never findings; **Set up this run**
  pre-fills the launcher and never starts a run.

---

## 11. Run-Completion Documents

A run can name a **report writer** when it is launched. Once the run is scored, that writer writes the
run's **Executive Summary** and **Root Cause Analysis** once — the summary in the stand-alone form
(§ 1), the analysis with the run's controls (§ 17) — and stores them as ordinary
`BenchmarkReportDocument` rows with `Origin = RunCompletion` and the
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
| NotRequested (0) | The default: no job has been asked for; also where a run returns after one of its documents is deleted, and where a run with an Executive Summary but no Root Cause Analysis settles, with the message *"Root Cause Analysis not written yet."* (`RootCauseNotWrittenMessage`) |
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
some run-completion documents — a run with only an Executive Summary, written before the Root Cause
Analysis existed, for instance — never has its missing one written automatically; its **AI Reports**
tab offers it as *write the missing documents*.

**The job.** One job has one writer and writes the requested documents the run is missing, in
`BenchmarkRunReportDocumentService.Audiences` order — the Executive Summary, then the Root Cause
Analysis; an automatic job requests every missing one (`audiences: null`), so it writes both. A model
batch's guardrail MB-A11 counts two documents per run when the batch names a report writer. Documents with different writers are separate jobs, one
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
3. **Writing.** The run becomes **Writing**. A single-run comparison is prepared — with the run's
   controls found by `RootCauseControls.FindAsync` for the Root Cause Analysis (§ 17) — and the
   documents are written through the Report Pack's own path: parsing, validation, one repair turn, the
   headline rescue from the first draft (§ 3; the headline-only turn is comparison scope only), drops,
   storage and a `SystemAiUsageLog` row with `RoleContext = 8`, charged to the
   user who launched the run (or who pressed **Write Reports**). Right after a Root Cause Analysis is
   stored its four server-drawn charts are drawn (§ 13). The job records each document's start and end
   times, model calls, tokens and cost.
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
or *"… Nothing was written."*).
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

- **The documents.** Both are listed, written or not: a written one with *Written* or *Written with
  warnings*, a line of meta — writer, date, duration, cost and *same provider, acknowledged* when the
  document records the acknowledgment — the *Run changed since this document was written* tag when
  flagged, **View** and a **Delete** icon button; a missing one marked *Not written*. Under a written
  Root Cause Analysis the tab shows its next runs and checks (`root-cause-runs`, each run with **Set up
  this run**) and a **Redraw charts** action (§ 17). A status line above
  says the job state (*Waiting for the report writer*, *Writing…*, *Failed: …*, *Skipped: …*, the
  cancellation message), with **Show Progress** while the run's documents are Pending or Writing, the
  automatic job's included. A *Downloads* notice with **Open Download Center** opens the Download Center
  on the run, and focus returns to the button when it closes.
- **The Controls section** (`root-cause-controls`, § 17). Before writing, it names the controls the
  Root Cause Analysis will use — *"The Root Cause Analysis will use 2 controls: <model>, run #812
  (2026-10-10); <model>, run #807 (2026-10-09)."* — and the cap they leave, or says *"No earlier run of
  another model answered these questions under the same graders. Speed and cost causes will be analyzed;
  intelligence causes will stay undetermined."*; while fewer than two qualify it offers **Set up a
  control run**. After writing, it says which controls the document used and, when a fresh lookup holds a
  control the document did not use, *"New controls available since this document was written (run #…)."*
  with **Rewrite with controls**. It also notes *"For several models, Model Comparison → Report Pack
  gives a fuller analysis."*
- **The write panel**, while a finished run lacks a document: one checkbox per missing document (the
  button reads **Write Report** for one and **Write Reports** for two or more; a written one is named as
  already written), the report writer picker with an (i) button that opens the per-document advice (§ 4)
  — each of the two documents, then *Every document* — as a modal *Choosing a report writer* dialog,
  and a live cost estimate from the estimate endpoint, shown as an *Estimated cost* panel with the total
  and, for two documents, the cost of each, each as the range of § 9 (first call only, to one repair
  turn per document). The picker starts on the run's own writer, else on the launcher's
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
  document allows, and the others offered as disclosure tabs (*Summary* / *Full* for the Executive
  Summary; the Root Cause Analysis opens at *Full*, its only level). An (i) button after the tabs opens
  a modal that explains what each offered level contains in that document (`report-disclosure-guide.ts`,
  whose per-document texts the Download Center's *Disclosure* column also shows, one section per
  document).
  It has page
  navigation, zoom, a selectable text layer, **Download PDF** (under the server's file name) and **Open
  in new tab**, a real same-origin URL with `inline=true`. No PDF is framed or embedded, so the CSP is
  unchanged.

**In the run progress dialog.** A run launched with a report writer shows the automatic job as its
**stage 4**, *Writing reports*: the dialog's labels read *Stage n of 4*, the stage is current while the
Completed run's documents are Pending or Writing (and for up to 30 s while they are still NotRequested),
the status line follows the job's phase (*"Stage 4 of 4 — Writing reports: waiting for the report writer
(1 job ahead)"*) and then appends *"Reports written: 2 documents, 1m 48s."*, and the completion chime
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
written* and does not rewrite it. A later comparable run of another model never changes a stored Root
Cause Analysis either; it only makes **Rewrite with controls** appear (§ 17), which Overseer never
starts by itself. To write it again otherwise, delete it — the tab's **Delete**, after a
confirmation, or `DELETE /api/admin/benchmark/runs/{runId}/report-documents/{documentId}`, which is
refused while the run's documents are being written — and use **Write Reports**. Deleting a run's
run-completion document through either delete endpoint returns the run's status to **NotRequested** with
no message, unless a job is in progress, so the status never describes a document that is gone; the run
keeps its report writer. After a delete the tab clears the writer picker when it held the model that
wrote the deleted document, so a rewrite starts from a deliberate choice.

**Provider availability.** The fact sheet of a run-completion document carries the run's `availability.*`
facts (§ 2), computed by `BenchmarkProviderAvailabilityService` from the run's attempt ledger and its
lineage predecessors when the job prepares the sheet, and the documents print them as § 7 describes
(format 12). A run started before harness 56 has no ledger, and its documents say *not recorded*. A
stored document's facts are never recomputed, so a document written before a later repair pass keeps
the figures it was written with. A stand-alone document carries no browser-drawn chart, so it has no
figure A1; a run-completion Root Cause Analysis carries only its four server-drawn charts (§ 13).

---

## 12. The Comparison Wizard's Write and Documents Steps, and the Comparison Reports Launcher

The Model Comparison wizard has four steps: *1. Sources*, *2. Charts & table*, *3. Write* (*"Write AI
reports that compare the models of this comparison."*) and *4. Documents* (*"View, chart,
download and delete this comparison's documents"*). Steps 3 and 4 are reachable once a comparison
exists; step 3 also needs **two** entries that are not Excluded (`hasComparisonPeers`), because a
comparison report needs a subject and a peer (§ 1a). Otherwise it is `aria-disabled` with a visually
hidden reason — with one comparable entry *"A comparison report compares one model with at least one
other. Add another model on step 1, or write a run's or battery run's own reports in the AI Reports tab
of its report."*, with none *"No model in this comparison can be charted, so none of them can be the
subject of a report."* — and **Next** skips it. Next runs 2 → 3 → 4, and closes the wizard on step 4. Step 2's former **Reports** button and
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

**Step 3, Write** (`app-report-pack-panel`, `report-pack/report-pack-panel.component.*`) uses
`app-run-report-frame` in its **sidebar** layout with no header (`[showHeader]="false"`), since the
wizard's header already names the suite and the charted entries; the step adds no padding of its own
(as step 4), so the frame's inset is the only one. The layout is a resizable sidebar with the form, then
the main area with the job, in reading and tab order. From 60rem of body width the sidebar is 20–32rem
wide, at most 40 % of the body, 24rem by default, set by the `app-pane-resizer` between the two (drag, or
Left and Right on it); the width is kept under `sidebarWidth` in
`localStorage['overseer.benchmark.reportPack']`. Below 60rem the two stack, the sidebar first. Each
column scrolls on its own.

The sidebar's heading, *New report pack*, is followed by a hover info tip, *Where these reports are
kept*: *"These reports are kept with the comparison: step 4 lists them, and so does Comparison reports
on the Model Comparison tab. A run's or battery run's own reports are written in the AI Reports tab of
its report."*

- **Sidebar**, *New report pack*, in order:
  - **Document scope**: a `.gh-tabs-segmented` pair, *Whole comparison* (with the visible note
    *Recommended*) and *One model at a time*, remembered as `documentScope` in `localStorage['overseer.benchmark.reportPack']`, with an info
    tip: *"Per-model documents restate the comparison from one model's side. Use them only when one
    model's document must be shared on its own."*
  - **Models**: `app-model-multi-picker` (`frontend_ui_controls` § 4e-3) over the comparison's entries
    that are not Excluded, with no price or parallel badge and no chips; each option is the entry's model
    name with its thinking-level, reasoning-mode and provider badges, keyed by entry key, and two options
    that would look the same each name their source (*Battery run #10*). Under *Whole comparison* every
    model starts chosen (2 to 12; with more than 12, the 12 of highest Intelligence Index — or, when the
    comparison holds the baseline model, the baseline model and the 11 of highest Index — and a note says
    why); under *One model at a time* the highest-Index model starts chosen (at least one). The choice is
    component state, reset when the comparison changes. One line under the picker says what Generate
    writes: *"Writes the comparison-wide documents."*, *"Writes documents for 3 of 5 models — leaves out
    Claude 5 Opus (high), Gemini 3.8 Pro (high)."* (naming only what is left out, with its source where
    two models share a name), or the per-model documents of the chosen models. While any entry is
    Excluded, the line ends with *"1 entry of this comparison is Excluded and is not offered."* (*"N
    entries of this comparison are Excluded and are not offered."*). Once the preview answers,
    under *Whole comparison* a legend follows: a boxed group titled *Letters in the anonymized copies*
    whose `<dl>` pairs each letter (a small gold tile, read as *Letter A*) with the model's label, in a
    grid of 13 rem columns. It is not added to the picker's description, which it would make long.
  - **Reference model** (*Whole comparison* only): a select of *Highest Intelligence Index* and each
    covered model, the baseline model's option marked *(baseline model)*; it starts on the baseline model
    when the comparison holds a comparable result of it among the covered models, else on *Highest
    Intelligence Index*. The documents judge the other models against it (§ 15). It is sent as
    `referenceEntryKey` in the preview, layout preview and start bodies **only when a model is chosen**;
    *Highest Intelligence Index* sends nothing. Below it, *Reference of the written documents* lists each
    written document's reference, and an amber note names the documents written against another
    reference than the one chosen, which a **Rewrite** would change.
  - **Documents of this comparison** (`app-comparison-documents-status`): under *Whole comparison*,
    two rows, *Executive Summary* and *Root Cause Analysis*, for the chosen set — the comparison-wide documents while every
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
    rows start checked, written ones unchecked. The other sets' rows have no checkbox. Under a written
    Root Cause Analysis row sit its recommended runs and checks (`root-cause-runs`, each run with **Set
    up this run**) and a **Redraw charts** action (§ 17). The list follows
    the preview, asked 300 ms after the last change of scope, models, reference or writer, an answer for an
    older choice dropped; a preview always asks for every document type, so checking or unchecking a row
    sends no request. The flags and chart counts come from the documents list
    (`GET report-documents?comparisonId=`, § 9), fetched as soon as the context names the comparison, in
    parallel with the first preview (after the preview, when only the preview supplies the number), and
    merged into the rows in either arrival order. While it is awaited, the section is busy and shows a small spinner
    (`app-loading-indicator`) with *Looking up the documents already written…*, which appears only if
    the lookup takes longer than 0.3 s.
  - **Charts in PDF and Word**: the chart picker with its per-figure width select (no visible caption;
    beside each figure, and under its title below a 20 rem picker width) and its *Layout* disclosure
    (§ 13), 1 rem below the figures, with the note *"The Root Cause Analysis also carries four charts
    drawn by Overseer: where the points were lost, where the time goes, where the cost goes, and how
    sure each hypothesis is."* when the Root Cause Analysis is shown; on screen,
    the print advisory when a document type's theme *As in step 2* would print badly and, while the
    `report-charts-location-missing` alert is present, *"Chart storage is not configured; documents will
    be written without charts."*
  - **Report writer**, with the dialog-mode info tip *Choosing a report writer*
    (`run-ai-reports/report-writer-advice.ts`, shared with the run report's **AI Reports** tab, with a
    Root Cause Analysis entry) and the *How the graders work* link; the refusal or the amber same-provider
    warning (*Writer from a chosen model's provider*).
  - The *Estimated cost* panel (`.gh-estimate-panel`, shared with the AI Reports tab), summing the
    checked rows as a range, *first call only, to one repair turn per document* (§ 9); while it is
    computed it reads *Estimating…* beside a small spinner, shown at once; with an amber
    warning when a checked document's prompt would fill 70 % or more of the writer's context window
    (refused from 90 %, § 15). A check change recomputes the total, the warning and the context-window
    refusal from the checked rows' estimates, with no request: a checked estimate with
    `exceedsContextWindow` shows the server's refusal wording (`reportPackContextWindowRefusal`, word for
    word `ContextWindowRefusal`) in `.rp-refusal` and makes Generate unavailable, exactly as a refusal from
    the server does; an unchecked one refuses nothing.
  - **Preview layout** and **Generate**, two gold `.btn-gh` buttons in one action row at the bottom of
    the sidebar, Generate last. At the default sidebar width they stack, Preview layout above Generate,
    both full width; from a sidebar about 34 rem wide they sit side by side. The keep-open note is in
    the job card (**Main area** below). The layout preview's status line under the row is a live region
    that takes no space while empty (`.rp-note:empty` in `styles.scss` is visually hidden, never
    `display: none`).
  - **Generate** (*zap*), which sends `scope`, `coveredEntryKeys` (whole comparison) or `subjectKeys`
    (the chosen models with a checked row), the checked document types and `replaceDocumentIds` (the
    checked written rows). It is unavailable, with its reason under it, while a job runs, with fewer than
    two (or more than twelve) models under *Whole comparison* or none under *One model at a time*, with
    no row checked, when the checked document types differ between the chosen models (*"One job writes
    the same documents for every model it covers. …"*), without a writer, while the preview is pending,
    and on a refusal. When every listed document is written and none is checked, it stays focusable and
    `aria-disabled` with *"Every document listed is already written. Check Rewrite on one to replace it,
    or delete it."* Two reasons are visually hidden, though still Generate's description, because the
    fact is already in view beside it: *"A report pack is being written. Wait for it to finish, or cancel
    it."* (the main area shows the job and **Cancel Job**) and *"Checking the writer and estimating the
    cost…"* (the estimate panel shows *Estimating…*). A 409 from the start — a document written in the meantime (§ 9) — shows its `error`
    as any other start error.
- **Preview layout** (*eye*, and a *chevron* that points up while open) opens a popover of the two
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
- **Main area**, *Report pack progress*: a status line that changes with the job's phase. **Generate**
  answers at once: focus moves to the *Report pack progress* heading (in the stacked layout the view
  scrolls to it) and, until the job's first progress reading, the main area shows a large spinner with
  *Starting the report pack…* and the line *"Checking the comparison, the report writer and the cost,
  then queuing the documents. This can take several seconds."* in place of the job card, while the
  status line reads *Starting the report pack…*. A refused start returns focus to Generate when it can be
  used again. In the job card, under its title, only while charts can still be pending (the job runs,
  or a document's charts are being attached) and chart storage is configured: *"Keep this wizard open
  until every document shows its charts: this browser draws them as each document finishes. If you
  close it, add them later on step 4 with Update charts…"* (§ 13, *When charts are attached*; a finished
  model batch's dialog gives the same advice as four steps, `ai-benchmark-model-batches.md` § 8.1).
  While a job runs, the stage rail (*Queued*, *Preparing*, one stage per document, *Done*); a stat strip (elapsed,
  writer, model calls, tokens, cost, estimate) that stays after the job finishes; one **progress card**
  per document (`app-report-job-documents`, a read-only card list, `frontend_ui_controls` § 8h): the
  document's title, then a status group of the chip and its info buttons that moves under the title when
  the card is narrow and lets the chip wrap its own words, a meta line (the model in a per-model job,
  the subject), the whole phase line with no ellipsis, an error line, and a `<dl>` of Duration (live),
  Model calls, Tokens (*"390,985 in · 11,875 out"*) and Cost in four columns, two below 36 rem of the
  list's own width and one below 18 rem (container queries, no viewport media query). A footer line
  carries the charts — *Charts: attaching…*,
  *Charts: 3* (the figures attached; the diagnostics add *(6 images, named and anonymized)* where the
  two counts differ), *Charts failed — retry* (a button that tries again) or *Charts: none* — and, in
  the run- and battery-completion writing dialog, which uses the same cards, its **View** button. The
  chat consistency reports step uses them too. Then *Log and
  diagnostics*, a disclosure with the job log and icon-only **Copy diagnostics**, **Download
  diagnostics** (`report-pack_<subject>_diagnostics_<yyyyMMdd-HHmmss>.txt`, LF line endings, never naming
  the user who started the job) and, once the job has ended and a call has a reply, **Download the
  writer's replies** (`report-pack_<subject>_writer-replies_<yyyyMMdd-HHmmss>.md`, from the endpoint of
  § 9; *"Preparing the writer's replies…"* in the status line while it fetches, and on a 404 *"The
  writer's replies are no longer held: Overseer keeps only the latest report job, and a newer job or a
  restart replaced this one."*). The writer's replies name each call as the diagnostics do (*Shortening
  turn*), cut durations to whole seconds as the diagnostics do, and after each document's calls give a
  `#### Tokens cited` table: every token its replies cite with what the stored document prints for it in a
  named, fully disclosed copy (`BenchmarkReportPackRenderer.TokenDisplays`), or *not in the fact sheet*;
  a document with no stored row gets one line instead. The diagnostics list under *Charts selected* only
  the selected figures the document type draws, and the others under *Selected but not drawn in this
  document type*; a Root Cause Analysis's *Charts* line splits its count into the selected figures and
  those Overseer drew (*"8 attached: 4 selected, 4 drawn by Overseer (16 images, named and
  anonymized)"*). Each call line adds how close it came to the writer's output limit (*", output 85% of the
  limit (near the limit)"*, near from 80 %); a battery comparison's *Runs* and *Groups* read *none (battery
  comparison)*. The diagnostics print the estimate as its range, *"Estimate: $1.04 to $2.10 (first call
  only, to one repair turn per document; a shortening turn or a second repair is not included)"*, and,
  once a document row carries them, two
  sections after *Documents*: **`== Validation ==`**, per document its first-turn and final issues
  (counts per rule, then each issue with its rule, location and message, *(dropped)* where it was), and
  **`== Failed writer replies ==`**, the raw reply of each document that failed, between *--- reply ---*
  and *--- end of reply ---*. A running job's rows carry neither, so neither section is written then.
  Elapsed time ticks every second on the server's clock
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
card's **More actions**. The list reloads when a job finishes and when a document is charted. While
the documents load, the list shows a centered spinner with *Loading report documents…* on the first
load, and a small one above the cards on a reload, so the list does not jump; *Listing the member
runs…* is shown the same small way. The same applies in the stand-alone Download Center dialog.

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
wizard's Write step appear here."* — with an
*N changed since written* tag when a subject's or a peer's run changed, and a click-mode info tip. Its one
`.btn-ghost` **Open Download Center** (*file-with-arrow*) is `aria-disabled` while there is nothing to
open, the summary line saying why, and opens the Download Center dialog on every Report Pack document
(`{ kind: 'all' }`) with **nothing preselected** and no chart actions. The summary loads when the tab is
shown, not with the page, and again each time the tab is shown, the wizard closes (it may have written
documents), a document is deleted in the Download Center or the Download Center closes; focus then
returns to the button. Below it, the newest Root Cause Analysis among the listed documents is named
(*Root Cause Analysis #<id> — <title> · <time>*) with its next runs and checks (`root-cause-runs`, § 17),
so its **Set up this run** is reachable without opening the wizard. The launcher still duplicates none
of the wizard's controls.

The run report's **Downloads** also opens the Download Center dialog, on the run, without chart actions.
Both places, like step 4, show and download the charts a document already has in its PDF and Word
copies; only the wizard can change them.

---

## 13. Charts in PDF and Word

A comparison document's PDF and Word copies can carry the Model Comparison's own charts — the figures of
step 2, drawn for print. **Charts are drawn in the browser, from step 2's settings, and stored on the
server as PNG files beside the document; rendering only places them.** No model call is involved, the
stored document row never changes, and the Markdown and HTML copies never carry a chart. Only a document
with peers can have browser-drawn charts — a comparison-wide document always covers two or more models,
so it always can; a stand-alone document (a run- or battery-completion document) has none. Each chart is
**composed for a target size in points** (below), and the server places it by the document's **chart
layout**: its width share of the text column, rows of two, and a height cap.

The one exception is the Root Cause Analysis: its four charts R1–R4 are **drawn on the server** with
QuestPDF when the document is stored (§ 17, *Server-drawn charts*), so every Root Cause Analysis has
them, a stand-alone one included.

### The figures and where they go

Eight figures can be chosen per document type (`BenchmarkReportChartPlacement`; the client's
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
| `a1-availability` | Provider errors: each model's provider error rate with its cluster-adjusted 95 % interval, labeled *k/n* (format 12, [`ai-benchmark-provider-availability.md`](ai-benchmark-provider-availability.md)) | Two plotted models, one with provider attempts recorded |

Four more figures are **drawn by the server**, never chosen in the picker, and carried by every Root
Cause Analysis (`BenchmarkReportChartPlacement.ServerFigureKeys`, `IsServerDrawn(key)`; the full
specification is in § 17, *Server-drawn charts*):

| Key | Figure | Section of the Root Cause Analysis (anchor) |
|---|---|---|
| `r1-points-at-risk` | Where the points were lost | 2, *Evidence: intelligence* (`RcaIntelligence`) |
| `r2-speed-decomposition` | Where the time goes | 3, *Evidence: speed* (`RcaSpeed`) |
| `r3-cost-decomposition` | Where the cost goes | 4, *Evidence: cost* (`RcaCost`) |
| `r4-hypothesis-confidence` | How sure each hypothesis is | 6, *Hypotheses* (`RcaHypotheses`) |

| Document | Where the figures go |
|---|---|
| Executive Summary | All in *How it compares*, after the comparison and dimensions tables and before the writer's paragraph; Provider errors after the *Provider availability* Key Figures bullet (anchor `ProviderAvailability`) |
| Root Cause Analysis | The four server-drawn figures at their anchors above. Where peers exist (a comparison or a per-model pack), the browser-drawn Intelligence, Speed, Cost, Intelligence against speed and Intelligence against cost may be chosen too: Intelligence in section 2, Speed and Intelligence against speed in section 3, Cost and Intelligence against cost in section 4 (anchors `RcaIntelligence`, `RcaSpeed`, `RcaCost`). Model profiles, Speed against cost and Provider errors are not drawn there |

Several figures at one anchor appear in the order of the first table. Every model-scope audience has
an entry for each browser figure key, because the placement tables hold one entry per key, `null` where a
figure is not drawn (`RootCauseAnchors` holds the server keys' anchors apart). `AllFigureKeys` joins the eight browser keys,
the five chat consistency keys and the four server keys, 17 in all, and `IsKnown` accepts every one. A
comparison-wide document has its own anchors (§ 15); the picker shows each figure's section for the
scope being written. A chat consistency document has five figures of its own, `cc1-quality` …
`cc5-availability`, kept in `BenchmarkReportChartPlacement.ChatConsistencyFigureKeys` apart from
`FigureKeys`; they are chosen per document type in the same picker, on the Chat Consistency wizard's
step 5, with its own figures, document types and defaults (§ 16).

**Markers.** The renderer writes a line `[[figure:<key>]]`, with a blank line before and after, at each
anchor for each chart it is given (`BenchmarkReportRenderOptions.Charts`). Browser-drawn keys get a
marker only in a document with peers — or in a chat consistency document, which has no peers and draws
its own figures; **server-drawn keys get one in every document**, a stand-alone run- or
battery-completion Root Cause Analysis included. The PDF and Word renderers draw the chart there (§ 8: `SemanticFigure` with the alternative text
and a `SemanticCaption` in the PDF, an inline picture with its description in Word, both captioned
**Figure N.** *Title* — caption and numbered in order of appearance); a marker with no chart prints
nothing. The Markdown and HTML downloads pass no charts, so they never carry a marker. The source hash of
a PDF or Word copy covers its drawn charts (§ 8), and adding charts moved the layouts to **PDF layout 3**
and **Word layout 2**.

### Choosing charts: the picker and its defaults

`app-report-chart-picker` is a group captioned *Charts in PDF and Word*: a segmented tab row of the
document types the scope writes (*Executive*, *Root Cause* for a comparison or per-model pack), each
with the number of charts selected for it, or `—` for a document type not checked under *Documents*;
beneath it the selected document's full name (*Executive Summary*, *Root Cause Analysis*),
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

The defaults are the Executive Summary's Intelligence and Intelligence against cost, and every figure
the Root Cause Analysis draws: Intelligence, Speed, Cost, Intelligence against speed and Intelligence
against cost. The Root Cause Analysis tab of the picker says *"The Root Cause
Analysis also carries four charts drawn by Overseer: where the points were lost, where the time goes,
where the cost goes, and how sure each hypothesis is."*; the server-drawn figures are not in its list.
The last selection and every document type's layout are remembered per browser in
`localStorage['overseer.benchmark.reportCharts']` (**version 2**, `{ selection, layout }`); a stored
version 1 keeps its selection and takes the default layout, and the settings of the two retired document
types are dropped when read.

The picker is shared with the Chat Consistency Write step. Its optional inputs — `figures` (each with
its title and its section, one name or one per document type), `audienceOptions` (the document types
and their labels), `notes` (a note per figure), `layoutFields` (the *Layout* settings offered) and
`columnDisabledReason` — default to exactly the Model Comparison behavior above, so Model Comparison's
bindings and stored settings are unchanged; § 16 gives the Chat Consistency values.

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
The wizard owns one publisher and queues step 3's and step 4's work through it. **The publisher never
uploads a server-drawn key** (`isDocumentChartFigureKey` ignores them), and Model Comparison's chart
counts and document cards ignore them too, so a Root Cause Analysis's R1–R4 never read as browser charts
that differ from step 2.

### When charts are attached

- **While the wizard is open**, each document of a step-3 job is charted as soon as its row reaches
  *Completed* or *Completed with warnings*, with step 3's selection for its type (none when that type's
  selection is empty). Its row shows the progress (§ 12).
- **Otherwise** — a document of the job that finished while the wizard was closed (the wizard cannot be
  closed while a chart is being drawn or uploaded, and reopening it does not chart what finished
  meanwhile), a document written before charts existed, or charts to redraw after step 2 changed — step 4 shows *None* or *differs from step 2* and **Update charts…**
  adds or redraws them.
- **Server-drawn charts** need no browser. `WriteDocumentAsync` draws R1–R4 right after a Root Cause
  Analysis is stored, in the same job, from the stored `RootCause` result, and logs *"Root Cause
  Analysis: N charts drawn."*; a drawing failure is logged as a warning and never fails the document.
  This is what gives run- and battery-completion analyses, written with no browser present, their
  charts. **Redraw charts** (`POST …/report-documents/{id}/charts/redraw`, § 9) draws them again from
  the stored `FactsJson` after a renderer change.

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
`report-charts-location-missing`, which the wizard's Write step reads. The folder is created on the
first write, never at startup.

**Layout.** One folder per document, `<ChartsDataLocation>/<documentId>/`, holding `manifest.json` and one
`<figureKey>.<named|anonymized>.png` per image; `<ChartsDataLocation>/.staging/<documentId>-<guid>/`
exists only while a set is being written. The manifest is camelCase UTF-8 JSON without a BOM: `version`
(1), `documentId`, `settingsHash`, `createdAtUtc` and `charts`, one entry per image with `figureKey`,
`naming`, `file`, `sha256`, `widthPx`, `heightPx`, `title`, `caption`, `altText` and the optional
`origin`, `"server"` or `"browser"` (absent reads as `browser`, so the manifest stays version 1), and,
when the upload carried one, `layout`: `{ version: 1, figures: [{ key, widthShare, rowGroup }], maxHeightShare }`
(`BenchmarkReportChartLayout`). The upload's layout is validated (`ValidateLayout`): version 1 only; a
`widthShare` above 0 and at most 1; a `maxHeightShare` from 0.2 to 0.9 or null; a figure of an unknown
key dropped, and of a key given twice the first kept. An absent layout — every manifest written before
it — renders every figure full width on its own row under the 60 % cap; a stored layout that no longer
validates is logged and rendered as absent. Server-drawn figures are never in the layout: each renders
full width, in its own row, under the height cap. **Update charts…** and a rewrite redraw the charts and send
the layout again. Every path is built
from the numeric document id, a known figure key and a fixed naming word, and is checked to lie inside
the folder.

**Atomic replace.** A `PUT` replaces the document's whole browser-drawn set, all or nothing, under a
per-document lock: the files and the manifest are written to a staging folder, the old folder is
deleted, and the staging folder is moved into place in one `Directory.Move`. Two writers share that
path: `SetChartsAsync` (a browser upload) carries the server-drawn entries it does not replace into the
new set, and `SetServerChartsAsync` (the server's drawing) replaces only the server-drawn entries, so
neither wipes the other's images. A render reads the manifest and skips, with a logged
warning, an image that is missing or whose SHA-256 differs from the manifest; **a render never fails
because of its charts**. The list and detail DTOs read `chartCount`, `chartFigureKeys` and
`chartSettingsHash` from the manifest alone, and the PDF and Word endpoints its layout
(`BenchmarkReportRenderService.ReadRenderLayout`).

**Upload limits** (`BenchmarkReportChartStore.ValidateCharts`; every chart is checked before anything is
written): a known figure key; naming `named` or `anonymized`; no figure and naming twice; at least one
and at most 24 charts (`MaxCharts`: 8 browser kinds and 4 server kinds, two namings each); one settings hash of 64 hex characters for the whole set; alternative text
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
copies simply have no charts, and **Update charts…** on step 4 draws them again from the comparison, and
**Redraw charts** a Root Cause Analysis's server-drawn ones from its stored facts, with no AI call.

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
  On a run or group subject's sheet, by contrast, `quality.rawIndex` is the pre-cap Intelligence Index
  and `quality.unweightedMean` the equal-weight mean of answer quality, each the mean of one figure per
  run of the subject (unavailable if any run has no scored answer) and taken over each run's scored
  items, like `quality.index`.
  A run or group sheet also carries `quality.withheldRuns` (*1 of 3 runs*, labeled *Runs with a
  withheld index*) when one or more subject runs withhold their own Intelligence Index because a
  question failed at the provider; the fact is **absent**, not unavailable, when none does, so every
  other sheet, prompt and document is unchanged. When it is present, each single-subject document —
  the Executive Summary and the Root Cause Analysis — follows its key figures with
  *1 of 3 runs withholds its own Intelligence Index because a question failed at the provider; the
  Intelligence figure above is over the scored questions.* (*withhold their own* when more than one
  run does). A comparison's per-model sheet carries the fact, prefixed `model.<letter>.`, for a model
  with a withheld run; the comparison documents add no sentence. `panel.memberAAlone` and
  `panel.memberBAlone` include a withheld panel run too, at the member-alone index recomputed over its
  answers, since its stored member-alone indices are withheld with its headline. A battery sheet never
  carries the fact: a battery's members exclude withheld runs.
- **The battery's own keys:** `battery.*` (name, revision, scheme, suite count, runs per suite, member
  runs, rounds, definition and class hashes, pooled identity, critical-error rate, speed index, the
  suites' SD and range, excluded members, caveats), `suite.<n>.*` (name, weight, index, contribution,
  interval, scored items, runs, speed index, cost per run, critical-error rate; *n* from 1),
  `sensitivity.<scheme>`, `sensitivity.panelVerificationCleared` (harness 48, only when a member is a
  panel run: the advisory panel grading sensitivity, not a weighting scheme; the writer is told never to
  present it as a corrected result) and `loo.<n>`.

A battery sheet has **no finding rows**: its items cite fact keys and question references as evidence.

**Provider availability (format 12).** A battery sheet carries every `availability.*` key a run sheet
does (§ 2; `ABatterySheet_SuppliesEveryKeyARunSheetCarries` requires it), pooled over the battery run's
member runs **including its superseded members and their lineage predecessors**, with every repair pass
of each ([`ai-benchmark-provider-availability.md`](ai-benchmark-provider-availability.md) § 8): a member
that failed at the provider and was relaunched still counts. The battery's deterministic Markdown report
(`BenchmarkBatteryReportBuilder`) adds a `### n.2 Provider availability` subsection (`n.1` when the
battery is incomplete), with a per-member table, inside its numbered `## n. Overall Intelligence Index`
section rather than a new numbered `##` section; the controller loads the
availability summaries for it, since it loads the member runs without their answers.

**Questions.** They are numbered across the battery, suite by suite, and referred to as **`S<n>-Q<m>`**
— `S2-Q7` is question seven of suite two — in the prose, in `evidence` and in the rendered document;
that form replaces `Q7` and is the only place a digit may appear in the prose. The validator applies
rules 3, 4 and 5 to that form: a plain `Q7`, or a reference to no question of the battery, is an issue.
Every question gets a **one-line row** — band, mean score over the runs that scored it, critical errors,
refuted claims, tool calls. At most `Benchmark:ReportPack:BatteryDetailQuestionsPerSuite` (default
**6**) questions per suite also get **full detail**: the question as asked, its rubric without its
`SOURCE` paragraphs (plain or bold label, § 7), one answer excerpt from the run whose score was the median of the question's rounds,
and the graders' comments on it. They are chosen, per suite, in this order (harness 54): questions with
a **confirmed** critical error (most rounds first, then the lowest mean); questions where a panel member
raised a critical error that was **not confirmed** — overturned by the claim verifier or unresolved,
either member; questions the **panel disagreed** on (`BenchmarkRunAnswer.PanelDisagreed`, the flag the
*Disagreements* count reads); then the lowest and the highest remaining mean scores, alternately. The two
middle groups are ordered by mean ascending, then question key. Question topics are asked for the
questions in detail only.

**The Root Cause Analysis of a battery subject** reads more than the Executive Summary does. Battery
preparation loads the subject's member runs' tool calls and `ModelCallTelemetry` (as projections of the
columns the engine needs, never whole entities), so the speed and cost decompositions and the
no-tool-answer evidence are available; the within-model rules use the **suites** as WQ2's groups and
leave-one-suite-out as its robustness check, and the chart rows of R1–R3 after the subject's own row are
one per suite (§ 17).

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
automatic job writes every missing document — the Executive Summary and the Root Cause Analysis
(`BenchmarkBatteryReportDocumentService.Audiences`, which is the run service's list); a battery run
that already has some gets the missing ones only on request. Each document
is a one-entry comparison of the battery result, stored with `Origin = BatteryCompletion` (3) and the
subject key `battery:<id>`; the writer call goes through `WriteBatteryCompletionDocumentsAsync` and the
Report Pack's own path — parsing, validation, one repair turn, the headline rescue (the first draft's
headline; the headline-only turn is comparison scope only), drops, storage. The
Root Cause Analysis reads the battery run's **controls**, earlier battery runs of other models on the
same battery revision (§ 17), and its server-drawn charts are drawn once it is stored. Its status
settles as a run's does: an Executive Summary without a Root Cause Analysis reads NotRequested with
*"Root Cause Analysis not written yet."*

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
battery run's own writer), the cost estimate, the same-provider confirmation and **Show Progress**; the
**Controls** line with **Set up a control run** (battery target), *New controls available* with
**Rewrite with controls**, and under a written Root Cause Analysis its recommended runs with **Set up
this run** and **Redraw charts** (§ 17).
Both tabs extend `ReportDocumentsTabBase` (`run-ai-reports/report-documents-tab.base.ts`) and render
`run-ai-reports.component.html`.

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
| `ReferenceEntryKey` | null | The entry key of the reference model (format 14); null on older rows, which used model A. The document list and detail return it as `referenceEntryKey` |
| Letters | Peers only, per document | Every covered model, A = highest Intelligence Index |
| One document per | `(ComparisonKey, SubjectKey, Audience)` | `(ComparisonId, CoveredSetKey, Audience)` |

An index on `(ComparisonId, Scope, CoveredSetKey, Audience)` serves the lookups; the uniqueness is
enforced by the start endpoint, not by a constraint, as in § 5. The comparison-wide and the subset
documents of one comparison are separate sets: every covered set is its own document set. The reference
model is not part of the key: a document written against another reference is a *Rewrite* of the one
already there, which it replaces.

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
  (ordinal). They are the same in the two documents of one covered set.
- **Per-model facts** `model.<L>.*`: every fact the per-model sheet has for its subject, the `subject.`
  prefix dropped (`subject.runs` becomes `model.A.runs`), plus `model.<L>.quality.rank` (the tied rank,
  below), `model.<L>.speed.rank`, `model.<L>.cost.rank` and `model.<L>.frontier` (*"on the intelligence
  against cost frontier"*). Of the `availability.*` family only the eight headline keys pass through
  (format 13: `model.<L>.availability.attempts`, `.providerErrors`, `.providerErrorRate`, its two bounds,
  `.unattributedErrors`, `.reruns`, `.providerReruns`); the by-class breakdown stays out, since the
  prompt lists every sheet fact and the breakdown for twelve models would add about 260 lines. The entry
  figures (`BenchmarkReportEntryFigures`) carry the same headline fields, so `BuildEntries` copies them
  from each subject row.
- **Comparison facts**: `comparison.models`, `comparison.pricingBasis` and its kind, `comparison.signature`,
  `suite.name` (runs and groups), the Pareto frontiers `frontier.qualityCost`, `frontier.qualitySpeed` and
  `frontier.speedCost` (the models no other model matches or beats on both measures), the spreads
  `spread.quality`, `spread.dimension.<d>`, `spread.speed` and `spread.cost`, each from the lowest value
  to the highest with no model in parentheses (*"from Model D at 62 to Model A at 85, 23 points apart"*,
  *"from Model I at 8.9 s to Model B at 60.3 s"*, *"from Model C at $0.0029 to Model A at $0.194"*; *"97
  for every model"* when all are equal), and `questions.anyCriticalError`, `questions.wideSpread` and
  `questions.sharedLow`. The *scored higher than* notable finding reads *"Model X scored higher than
  Model Y at 0.27 times its median answer time, established by 95% intervals that do not overlap."*, its
  basis after a comma.
- **Joint ranks.** `BenchmarkReportFacts.JointRanks` gives each model the **span of places its 95 %
  interval allows**: first = 1 + the number of models above it, last = N − the number below it. X is
  *above* Y when both have intervals and X's lower bound is above Y's upper bound, or when either has no
  interval and X's score is higher; equal scores and touching bounds are never *above*. Spans are per
  model, not transitive chains: with A–B and B–C overlapping and A above C, A reads 1–2, B 1–3 and C 2–3.
  A tied rank prints its span, *"5–9 of 9 (tied: intervals overlap)"* (`JointRankText`, span from
  `BenchmarkReportFacts.TiedRankSpan`), and a table cell *5–9 (tied)*; the word *joint* is not used in
  document text. The same helper words the per-model documents' ranks (W2, below). Ranks are recomputed
  from the stored entries at render time, so a document stored before 2026-10-15's format 20 shows the
  spans too; its stored `quality.rank` fact keeps the old group rank.
- **The paired-test family** comes from the service step 2's *Paired tests* view uses, requested for the
  covered models only, so each Holm adjustment counts exactly the tests the document reports: *Against a
  reference* with the document's **reference model** (below; model A when none was chosen), plus *All pairs* when there are **3 to 6** covered
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
- **Excerpts** share one total budget, `AnswerExcerptChars` × the number of questions. Questions are
  ordered in four groups: those where any model made a critical error or had a refuted answer sentence;
  then those whose scores span at least 20 points, widest first; then those every model scored below 50
  (a shared gap: chat, corpus or suite); then the rest. Within a group the suites are **interleaved**:
  the first question of every suite by the group's key, then the second, and so on. Within a question the
  models with a critical error or a refuted sentence come first, then scored before unscored, then the
  lowest scores. The budget is spent in **rounds**, at most three: round *r* gives each question, in that
  order, its *r*-th excerpt while the budget lasts, so every question of the first group has an excerpt
  before any question has a second. An excerpt longer than the remaining budget is skipped. Each excerpt
  is from the model's run whose score is closest to its score on the question. Each question's text and
  rubric are given once.

**The reference model and the baseline model (format 14).** `BenchmarkReportPackRequest.ReferenceEntryKey`
names the covered model the documents compare the others with; null means model A, the highest
Intelligence Index. A key that is not a covered, non-Excluded entry is refused with **400** *"The
reference model must be one of the covered models."* by the preview, the layout preview and the start;
the start copies it into the background job's request (`NewJob`). Letters do not move: A is still the
highest Index. The sheet keeps `referenceLetter`, `referenceIsBaselineModel` and `baselineModelLetter`
(the GnollBench baseline model's letter when it is covered, `ai-benchmark.md` § *Baseline Model*, even
when another model is the reference), and the fact `comparison.reference` reads *"Model C, the baseline
model"*, *"Model A, the highest Intelligence Index"* or *"Model C, chosen as the reference"*. A sheet
stored before format 14 reads as reference A with no baseline model. The server never picks the baseline
model by itself: step 3 sends it (§ 12).

**The failure profile (format 14)**, `BenchmarkComparisonModelProfile`, is computed in preparation from
every covered run's answers and passed on `BenchmarkComparisonReportEntry.Profile`, so `Build` stays pure.
Over each model's scored answers in all its runs (a panel run's level is the mean of the two members'):

| Key `model.<L>.profile.*` | Value | Unavailable when |
|---|---|---|
| `answers` | scored answers, the denominator | — |
| `mayMislead`, `mayMisleadPer20` | answers with an accuracy level below 4 or a critical error; *0 in 20 answers* at zero, else *about 3.5 in 20 answers* | — |
| `outcome.correct`, `.partial`, `.incorrect`, `.notAttempted`, `.noAnswer`, `.wrongInsteadOfAbstaining` | `BenchmarkOutcomeSummary` per answer | a run before scoring method 13 |
| `deficit.accuracy`, `.completeness`, `.conciseness`, `.readability` | answers at level 2 or below on the dimension | — |
| `truncated`, `outputDefects`, `noToolAnswers`, `toolLimitHits` | answers flagged Truncated; flagged Empty, Truncated, HarnessArtifacts, ReasoningBleed or RepeatedFragments or not Ok; with no tool call; that hit the tool budget or the iteration limit | — |
| `answerChars.median` | median answer length in characters | — |
| `isolatedMisses`, `sharedMisses`, `lowestScorer` | from the question matrix: 50 or less where the others' median is 75 or more (at least two others scored it); below 50 with at least half the others below 50; the lowest of all, ties counting for each | fewer than three covered models |
| `findings.runsWithSynthesis`, `findings.<category>` (`accuracy`, `completeness`, `conciseness`, `readability`, `critical_error`, `tool_use`, `other`) | runs whose final synthesis has structured findings; of those, how many raised a weakness in the category, by either panel member (*2 of 3 runs*) | no structured findings (before harness 40) |

**Against the reference (format 14).** For every model but the reference, on the questions both scored on
the same item revision, `model.<L>.vsReference.shared`, `.better` (more than 15 points above the
reference), `.similar` and `.worse`. They are counts, not tests.

**The weakest answers (format 14).** Per model, up to three questions scored below 70, lowest first; from
its lowest-scoring run on each, each grader's comment (the assessor's `ReviewComment`, panel member B's from
`CoAssessmentJson`) cut at a word boundary to 300 characters, and the critical-error flag. They are stored
in the content snapshot as `ModelNotes` (`{ letter, question, role, criticalError, comment }`) and given to
the writer as **WEAKEST ANSWERS BY MODEL**; rule 9 and the name backstop cover them.

**Notable findings (format 14).** `BenchmarkNotableFindings`, pure, detects unusual results from the facts,
the matrix and the profiles. Each finding has a kind, the letters and questions it is about, the fact keys
it rests on, and one sentence written by code with tokens, so it renders named or anonymized. In priority
order, at most two per kind and ten in all:

| Kind | Detected when |
|---|---|
| `cheaperAndBetter` | X's quality is established above Y's (a paired verdict, or 95 % intervals that do not overlap) and X costs at most half of Y per question |
| `fasterAndBetter` | the same, with median model time |
| `thinkingLevelInversion` | two covered entries of one provider and model id at different thinking levels, the lower level's index at least the higher's, and the higher level not established above the lower (a paired verdict or non-overlapping 95 % intervals in its favor rule the finding out); says whether the order is established, with the time and cost ratios |
| `leaderMiss` | model A, or the reference, scored 50 or less where the others' median is 75 or more |
| `underdogWin` | the lowest-index model scored at least 15 points above every other model on a question |
| `sharedFailure` | every covered model scored below 50 on a question (up to three questions in one finding): likely a chat, corpus or question problem |
| `difficultyInversion` | a model's advanced-band score at least 10 points above its simple-band score, each band with at least 3 questions |
| `criticalErrorConcentration` | at least 2 critical errors and at least twice the others' median rate |
| `graderDisagreementConcentration` | at least 3 panel disagreements and at least twice the others' median rate |
| `judgeDependentOrder` | a pair whose order depends on which panel member grades |
| `outputTrouble` | truncated answers plus tool-limit hits at least 10 % of its answers |
| `toolUseOutlier` | tool calls per question at most half or at least twice the median of all models, its own value included (median above 0.5); three models or more |
| `answerLengthOutlier` | median answer length at most half or at least twice the median of all models, its own value included; three models or more |
| `providerErrors` | a provider error rate of at least 2 % and at least five times the median |

The facts are `notable.count` and per finding `notable.<n>.kind`, `.models`, `.questions` and `.summary`; a
detection that needs a figure a model lacks is skipped. With nothing detected the documents print
*"Nothing unusual was detected by the rules below."*

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
topics. The prompt's SHA-256 covers it. From format 14 it also carries a **Reference:** line (*"Reference:
{{model:C}}, the Overseer team's baseline model"* or *"Reference: {{model:A}}, the highest Intelligence
Index"*, and a *Baseline model:* line when the baseline model is covered but not the reference), the
**NOTABLE FINDINGS** block and **WEAKEST ANSWERS BY MODEL**, and the system prompt's wording of the
reference family names the reference instead of assuming model A.

- **Rules of format 14** in the system prompt: the failure profile counts what the graders recorded,
  never why; a cause is stated only where the data shows it (a grader comment under WEAKEST ANSWERS BY
  MODEL, a tool-limit or truncation count) and attributed (*"the graders charged …"*), never a model's
  training, size or design; models with different numbers of answers are compared through the per-20 and
  *of* facts, not raw counts; a signal most low scorers share is a pattern, one only one model shows is
  that model's, and one every model shows points first at the chat, its tools, the corpus or the
  question; better, similar and worse counts are not tests, and a model is ranked above or below the
  reference only as the paired tests and intervals allow; notable findings are explained, or said to be
  unexplained by the data, never restated. `whichModel` adds: when the reference is the baseline model,
  say whether any model is established as better than it, and on which measure.

- **Tokens.** `{{model:X}}` for a covered model and `{{key}}` for a fact; there is no `{{subject}}` and
  no `{{peer:X}}`. The validator accepts `{{model:X}}` for every letter of the sheet and the `model.<L>.*`
  and `pair.<L>.<M>.*` keys, and rejects any other letter.
- **Slots** (word caps): Executive Summary `overview` (90), `whichModel` (150, ending with a default
  choice for a typical Overseer player), `tradeOffs` (100), `reliability` (80) and, from format 14,
  `againstReference` (100: what a player would notice using each model instead of the reference —
  answer quality, misleading answers, waiting, cost — each as far as the paired tests and intervals
  establish). The Root Cause Analysis slots are in § 17.
- **Lists.** In the Executive Summary a `models` list with one entry per covered model, at most 2 points
  of at most 30 words, each citing evidence; no strengths, weaknesses, recommendations or question notes.
  The Root Cause Analysis' lists (`observations`, `writerHypotheses`, question topics) are in § 17.
- **Validation.** The rules of § 3 apply, read for comparison scope: rule 10 treats every covered
  model's name as a name; rule 16 reads two `{{model:X}}` tokens against the pair's `intervalOverlap` —
  for each comparative word, the nearest model token before it and the nearest after it, every pair of
  the sentence only when one side has none — and a family's established result may be stated instead of
  *overlap*; **rule 21**, a warning, asks for a `models` entry for every covered model; rule 31 and, in
  the Root Cause Analysis, rules 32–36 apply as well.
- **Topics.** Only the Root Cause Analysis writes question topics, for every question it cites; no
  topics are shared between the documents of a job.
- **The writer** cannot be a covered model's configuration: the same provider, model id and thinking
  level (400). A writer sharing a covered model's provider gets the amber warning naming those models
  and the *Same-Provider Report Writer* confirmation (§ 4).

### Limits, refusals and the estimate

- **2 to 12 covered models** (`MinEntries`, `MaxEntries`; the charts' `MAX_PLOTTED_ENTRIES`). Outside the
  bounds the start and the layout preview answer **409**, and the preview answers 200 with the refusal.
  A comparison of more than 12 models can still have documents: step 3's picker then starts with the 12
  highest-Index models chosen and says why.
- A covered entry that is not in the comparison, or is Excluded — **400**; so is a `referenceEntryKey`
  that is not one of the covered, non-Excluded entries.
- **The writer's context window.** Each estimate carries `contextWindowShare`, the estimated input
  tokens over the writer configuration's context window from the model catalog. Step 3 warns in amber
  from **70 %**; above **90 %** (`ContextWindowRefusalShare`) the estimate carries
  `exceedsContextWindow`, step 3 refuses while that document is checked, and the start refuses it with
  **400**, in both scopes. The preview estimates every offered document and leaves the refusal to the
  client, which alone knows which rows are checked (§§ 9, 12).
- **Already written.** A requested document already written for this covered set (comparison scope) or
  subject (model scope) and not named in `replaceDocumentIds` — **409**; an id in `replaceDocumentIds`
  that this job does not write again — **400**.
- **The estimate** uses 3,800 output tokens for the comparison-scope Executive Summary and 14,000 for
  the Root Cause Analysis (`ComparisonEstimatedOutputTokens`), and 2,400 and 10,000 in model scope
  (`EstimatedOutputTokens`); the Root Cause Analysis figures are first guesses, to be recalibrated after
  its first five jobs in each scope. Every estimate is shown as the range of § 9, first call only to one
  repair turn per document. A test holds an 11-model, 20-question comparison's prompt below 70 % of a
  200,000-token writer.

### Rewriting: replace after persist

`ReplaceDocumentIds` names the documents a job replaces. For each document it writes, the job stores the
new row and removes the replaced rows of the same comparison, audience and covered set
(comparison scope) or subject (model scope) — or, for a run- or battery-completion **Rewrite with
controls** (§ 17), the replaced RunCompletion or BatteryCompletion row of the same subject key and
audience (`ReplacedDocumentsAsync`) — **in the same `SaveChanges`**; only then is each replaced
chart folder deleted (a failure there is logged and never fails the write). A write that fails deletes
nothing. A single document is deleted with `DELETE report-documents/{id}`, which step 3 offers per row
behind a confirmation.

### The rendered documents

`ReportFormatVersion` for comparison scope is **26** (`ComparisonReportFormatVersion`); per-model
documents are at 22 (§ 7). Both comparison document types open with the document type as a bold kicker
(*EXECUTIVE SUMMARY*, *ROOT CAUSE ANALYSIS*), then the comparison's name as the H1
(`BenchmarkPdfDocumentInfo.ComparisonDocumentHeading`: *Comparison #12 — Spring model sweep*), then a
*Comparison:* line with only what the name does not say (*5 models · computed 2026-09-21*); the stored
`document.Title` and the file name keep the type. The sections, by audience:

- **Executive Summary**: *The result in one sentence*, *The comparison in one paragraph*, *Which model to
  use*, *How they compare* (every model's Intelligence Index with its interval and joint rank, median
  answer time, cost per question and critical errors; a dimensions table; the figures), *Model by model*,
  *Trade-offs* (with the frontier lines), *How reliable this is* (with the interval-overlap sentence, the
  paired-test summary and the writer caveat), *About this benchmark* and *Evaluation terms*.
- **Root Cause Analysis**: the sections of § 17, with the per-question matrix, the paired tests, the
  failure profile (as **two tables**, *Outcomes* and *Signals*), the grader findings by category, the
  threats to validity, the reproducibility appendix and the question details it reuses from the
  comparison renderer, and the fact sheet in the Markdown copy.

**Format 14** adds, each block only when its facts or sheet fields are there:

- **Executive Summary**: after *How they compare*, **Compared with the baseline model** (*Compared with the
  reference model* when the reference is not the baseline model): the *Reference:* line, a compact table
  of every model — paired difference with its 95 % interval, better / similar / worse questions, answers
  that may mislead per 20, time and cost ratio — its note, and the `againstReference` slot; then **Also
  worth knowing**, the first three notable findings as code wrote them.
- **Marks.** In every table that lists the models, the reference row reads *(reference)*, the baseline
  model's *(baseline model)*, and one row that is both *(baseline model, reference)*, in named and
  anonymized copies alike; neither word names a model or a provider. The paired-test summary and the
  *Paired tests* heading name the reference instead of assuming model A. `Pdf/BenchmarkTableLayout`
  abbreviates the wide new tables' headers.

**Provider availability (format 13).** The results table (`ResultsTable`) gains a **Provider errors**
column, *0.81% (12/1,480)* or *not recorded*, from the entry figures; `Pdf/BenchmarkTableLayout.cs`
abbreviates its header (`HeaderAbbreviations`) so the eleven-column table still fits, and
`ComparisonThreats` gains the unattributed, re-run and not-recorded lines of § 7. Each block renders only
when the sheet carries the facts.

*Paired tests* names the family, its adjustment and its size, and notes that another set of models gives
another family: a subset document's adjusted *p* can differ from step 2's view over all models, and both
are correct for their own family. Tables that list the models carry a **Letter** column under both
namings, and a named copy prints each model's label and provider and **no letter in its prose (W1)**.
The per-model documents print the same way: a named peer reads *Grok 5*, never *Grok 5 (A)*, their
*Compared models* table gains the Letter column, and a rank whose interval overlaps a neighbor's reads
as the span of its tie group, *1–2 of 3 (tied: intervals overlap)* (**W2**), whatever format they were
written under.

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
| Executive Summary | All in *How they compare*, after its tables, except Provider errors (A1), which this audience does not place |
| Root Cause Analysis | As in model scope (§ 13): Intelligence in section 2, Speed and Intelligence against speed in section 3, Cost and Intelligence against cost in section 4, and the four server-drawn figures R1–R4 |

A comparison-scope document plots its **covered models only**: the named variant is step 2's chart with
*Show* limited to the covered set (step 2's *Highlight* kept where it falls inside it), and the
anonymized variant (`anonymizeComparisonForAll`) letters every covered model with the set's letters,
draws it in the neutral gray and removes every other entry. Such a document always covers two or more
models, so the chart endpoints never refuse it as stand-alone.

### The layout preview

`POST report-packs/layout-preview` renders one document as it would print, with **no model call** and
nothing stored. It is multipart: a `request` field holding JSON — a preview request (§ 9) with
`audience` (1 Executive Summary or 5 Root Cause Analysis; any other is refused with *"audience must be 1
(Executive Summary) or 5 (Root Cause Analysis)."*), `paper` (`a4` or `letter`; empty is A4), `naming` (`named` or `anonymized`; empty is named),
`layout` (as stored in the manifest, § 13) and `charts` (`figureKey`, `title`, `caption`, `altText`) — and
up to **16** files named `<figureKey>.png` (`BenchmarkReportLayoutPreview.MaxCharts`), in a request of at most 40,000,000 bytes, each image checked as
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
| `ReportFormatVersion` | `ChatConsistencyReportFormatVersion`, currently **6** (§ 7) |

The title is *Overseer Chat Consistency Report: \<model\>*. The sheet's subject block
(`BenchmarkReportChatConsistencySubject`) carries the analysis id, name, headline, protocol label,
`InputSha256`, analysis code version, the baseline, comparison and control run ids, and whether a
Provider Issue Report is available. The input hash is kept for the record only: no rendered block, no
cover and no footer prints it, and the writer never receives it.

### Audiences and slots

Two audiences (`ChatConsistencyAudiences`): the Executive Summary and the Provider Issue Report, each
with prose slots only — no strengths, weaknesses, recommendations, question notes or topics. A chat
consistency analysis has no Root Cause Analysis: a write naming one, or a retired audience, is refused
with *"Only the Executive Summary and the Provider Issue Report are written for a chat consistency
analysis."* (`ChatConsistencyInvalidAudienceMessage`). The headline is at most 35 words.

| Audience | Slots (title, word limit) |
|---|---|
| Executive Summary | `asGoodAsBefore` *Is the Overseer chat with this model as good as before?* (120), `playerImpact` *What changed for players* (90), `ourChanges` *Our changes and their effect* (80), `providerChanges` *Provider-side changes* (80), `confidenceAndScope` *Confidence and scope* (90), `nextRuns` *Next runs* (60) |
| Provider Issue Report | `issueSummary` *Summary* (120), `affectedModel` *Affected model and configuration* (80), `timeline` *Timeline* (150), `measurements` *Measurements with intervals* (200), `hoursObserved` *Hours observed* (80), `ruledOut` *What we ruled out (our changes, infrastructure)* (200), `sampleRequestIds` *Sample request ids* (60), `providerRequest` *Request to the provider* (100) |

**The Provider Issue Report** (`BenchmarkReportAudience.ProviderIssueReport`, 4) exists only in this
scope. It is **available only when at least one attribution is provider-side and graded Established or
Indicated** (`BenchmarkChatConsistencyReportFacts.ProviderIssueReportAvailability`). The estimate
always answers, with `providerIssueReportAvailable` and `providerIssueReportReason`; a write that names
it while it is unavailable is refused with 400 and *"No provider-side finding graded Established or
Indicated in this analysis."*; a write that names no audience leaves it out. It never names a control
model — its peers are always anonymized, lettered *Model A* … — is stamped *Confidential. Prepared for
the model's provider.*, and its `sampleRequestIds` slot draws on at most 10 request ids of the
comparison period's candidate calls. The Report Pack's per-scope audience lists do not include it.

### The code-built blocks per audience

Every document opens with the title (and, in the Markdown, the stamp and the front-matter list), the
**out-of-date box** when the analysis is out of date (below), the
**Overall verdict**, *The result in one sentence* (the writer's headline), then the blocks no slot
takes, then the slots in order, each code-built block printed under the slot that discusses it
(`ChatBlocksOf`) or in its default place, then *How to read this*, the limitations and reproducibility
list where no slot took them, *Removed content*, and *Evaluation terms*. **No block prints a hash, JSON
or an internal field name**, and no table is wider than five columns; a table of one row becomes a
sentence.

| Block | Executive Summary | Provider Issue Report |
|---|---|---|
| Out-of-date box | under the title block when the analysis is out of date (below) | the same |
| Overall verdict | *Verdict* (`verdict.short`), one bullet per group of endpoints not computable with its reason in plain words (a grading change on quality, periods run at different hours), *Sample* (`sample.shortfall` when the minimum is not met, each part stated as met or missed: *1 battery run per period on 1 day, below the minimum of 2 battery runs on 2 days per period; paired items 32, above the minimum of 20*), *Hours*, *Controls* | the same with the technical not-computable reasons, plus *Time strata* |
| Verdicts | *Measure · Result · What it means* for the computed endpoints, named without P1–P5 ids, one line naming those not computable, and one sentence on where the change came from | *Endpoint · Change (95% interval) · Margin · Verdict and grade · Smallest detectable* for the computed endpoints; under it one note line per endpoint (the 90% interval, the Holm-adjusted p-value, the MDE note, the legacy-proxy note and the grade's reasons); the endpoints not computable as bullets with their reasons; and a *Secondary measures* table, *Measure · Baseline → comparison, or change (95% interval) · Verdict* |
| Where the chat stands | after the verdict table | the same |
| Overseer events | one sentence naming each update by its tag with its time and changes and no run ids (*Between the periods, Overseer was updated once: E1, 2026-10-08 14:49 UTC — harness 53 → 54.*), and how many more the control models' runs show; when there are more than three updates, the sentence gives their count and is followed by the **table of Overseer updates**, as the Provider Issue Report prints it, so the per-update detail stays in the Executive Summary | *Event · When (UTC) · Run · What changed*, one row per update (`eventGroups.*`, below), the time reading from–to when the update's events span time, a control model's update naming its series |
| Control models | in the overall verdict | one bullet per control model with its runs, periods and difference-in-differences estimates, then the missing controls as a table, *Period · Battery run (suites) · What to do* (*Run (suite)* when the units are runs), its suggestions without an instrument note |
| Attribution | the sentence under the verdicts | one sentence for a single attribution, else *Endpoints · Side · Grade*; then each attribution's evidence |
| How to read this | what was measured, the five measures (quality, time to first answer text, answer streaming rate, work per turn, cost per question), the grades, the verdicts, the hours | the full list: what was measured, the endpoints, the verdicts, the evidence grades, the hours (the different-hours wording when the periods share none), attribution, control models when there are any, and what is never inferred |
| Limitations | the analysis's data-quality notes only, and no section without one | every limitation and data-quality note |
| Reproducibility | — | analysis number, code version, protocol with overrides and alpha, *Saved*, the common grader, the price card, each period's battery runs with their member runs (or runs), the control runs and the report format version; **no hash** |
| Evaluation terms | the distillation prohibition and the third-party content, *evaluated solely to check whether the Overseer chat with this model stays consistent over time* | the same |

**The out-of-date box.** A document whose analysis is out of date (`ai-benchmark-chat-consistency.md`
§ 16.1) prints, in every audience, directly under the title block: *This document comes from an
out-of-date analysis: \<reasons\>. Saved analyses never change; analyze the same periods again for a
current result before relying on this document.* The reasons, joined with *"; and "*, are *it was saved
under analysis code version \<n\>, and Overseer now analyzes under version \<m\>* and *its runs, grades,
controls, annotations or prices changed after it was saved*. The box is decided when the document is
rendered: the first reason from the sheet's analysis code version against the current one, so a
document written earlier shows it when downloaded again; the second from the fact
`analysis.writtenOutOfDate` (*earlier analysis code (version n)*, *changed inputs*, or both joined with
*and*), which the report job records when the writer started after the operator's acknowledgment
(`acknowledgeOutOfDate`, § *Endpoints* below).

**Where the chat stands** follows the verdict table in every audience: *Measure · Baseline · Comparison*
for Quality (the battery Overall Index with its interval where recorded, else the mean score), Time to
first answer text, Answer streaming rate, Work per turn (output tokens per answer), Cost per question and
Failed answers, from the `level.*` facts. A measure whose endpoint was not computed for a measurement
change or for lack of common hours is marked *(not comparable)*, and the table closes with *Descriptive
levels of each period, not a comparison: the verdicts above say what changed.* A sheet without `level.*`
facts prints no table.

**Smaller rules of the blocks.** Under every endpoint table, when a period has one unit: *With one
\<unit\> per period, the intervals and smallest detectable changes reflect question-to-question variation
only; run-to-run variation is not included, so the true uncertainty is larger.* The secondary-measures
table collapses reliability rates that are zero in both periods into one row, *Failures of any kind:
none in either period (…)*, and a cell that cannot be computed gives its reason. The overall verdict does
not repeat the analysis's headline. An attribution to an undetermined side reads *The analysis cannot
say where the change in \<endpoints\> came from (graded \<grade\>).* With no common hours, the hours lines
say that the periods ran at different hours and give each period's hours. The Executive Summary's
code-built blocks name the endpoints without their P1–P5 ids.

A document written before these blocks renders them from its stored facts too: its events are put in
words from the stored kind and raw values (`ChatConsistencyEventText.Describe`), grouped by UTC day and
series; its control suggestions lose their *(instrument …)* note; a figure display loses its *(log
ratio …)* note and an MDE display gains its ±. Its writer prose keeps the wording it was written with.
A substituted fact that ends in a period, followed by the prose's own period, prints one (`Prose`).

### Facts and claim support

The writer gets a SUBJECT block, PEERS (the controls as `{{peer:X}}`), every fact as
`key = display`, and a **CLAIM SUPPORT** block listing which tokens support a change, the inconclusive
endpoints, the Established grades, the attributions, the provider-confirmed causes, the time-of-day
assessability and the hours — for a Provider Issue Report also the provider-side attributions, the
Overseer events to list under `ruledOut` and the sample request ids. The fact families are `analysis.*`,
`subject.*`, `verdict.*`, `scope.*`, `coverage.*`, `period.<baseline|comparison>.*`, `sample.*`,
`protocol.*`, `n.*`, `endpoint.<P1…P6>.*` (P6 under Protocol V2 only), `availability.<period>.*`, `level.*`, `quality.*`, `flip.*`, `grader.drift.*`, `reliability.*`,
`tools.*`, `secondary.*`, `events.*`, `eventGroups.*`, `controls.*`, `controls.missing.*`, `did.*`, `robustness.*`, `identity.*`,
`serving.*`, `ownWaits.*`, `pricing.*`, `annotation.*`, `attribution.*`, `limitation.*`, `nextRuns.*`
and, for the Provider Issue Report, `requestIds.sample.*` (`BenchmarkChatConsistencyReportFacts`).

The displays are written for a reader. Numbers print a minus as U+2212 and keep their unit on the line
with a no-break space (U+00A0), *38.7 s*, except a percent sign, which follows the number directly:
*−7.6%*. An MDE reads *±12.7%*, with its caveat in
`endpoint.<P>.mdeNote` (*A floor: one unit per period, so run-to-run noise is not included.* for an
item-paired endpoint from analysis code version 6, *one run per period: run-to-run noise not estimable*
for a speed endpoint), and no estimate carries a *(log ratio …)* note. The served models are labeled
with *IDs* in capitals, and per-question costs print with at least two decimals. `verdict.short` is the outcome title (*Not enough evidence yet*);
`analysis.savedAt` the saved time. A period states its units (`period.<p>.units`, *1 battery run*, with
`.unitNoun`) beside its member runs (`.memberRuns`), and the minimum sample is `sample.minimumUnits`,
`.minimumDays`, `.minimumPairedItems`, `.met` and, when not met, `.shortfall` with real plurals.
`controls.missing.count` reads *N missing-control notes*. An Overseer event's `events.<n>.kind` displays
its kind in the event table's words, never as a field name (`EventKindText`: *harness version*, *wiki
revision*, *source code revision*, *corpus re-index* …), and `events.<n>.change` its short before → after
(`ChatConsistencyEventText.Change`: *harness 53 → 54*, *wiki revision a8fa85a → 4bb80dc* with seven
characters of a revision, *re-indexed: GnollHack wiki, GnollHack source code (file counts unchanged)*, *game snapshot: off
→ on* for the changed prompt options only, *tool iterations per question: 8 → 10*, *changed* for a
system prompt or tool guides; never more than 120 characters, a hex run of twelve or more, or a `{`);
`eventGroups.count` and `eventGroups.<n>.tag` (*E1*, *E2*, … in time order), `.at`, `.lastAt`, `.run`,
`.changes` and `.series` are the same events grouped into updates
(`BenchmarkChatConsistencyReportFacts.EventGroups`, keyed by `EventGroupKey`): by UTC day, series, the
harness version of the event's run (from the series' harness events) and the side of the period split, so
a group never spans the baseline and the comparison. The client's report charts tag their event markers
the same way (`ccReportEventGroups`), and the shared fixture
`Overseer/ClientApp/src/app/admin/benchmark/chat-consistency-tab/cc-event-groups.fixture.json` pins
both. `events.<n>.from` and `.to` are no longer stated. In a battery comparison an event's and an update's
runs (`events.<n>.run`, `.previousRun`, `eventGroups.<n>.run`) read under their battery run, *battery run
#12 (run #98)*, the members of one battery run together; a control's run reads as before.

A period's **window** is `period.<p>.window`: the comparison *from 2026-10-08 14:49 UTC to 2026-10-08
23:59 UTC*; the baseline *2026-10-08 00:00 UTC until 14:49 UTC* when it ends within a minute before the
comparison starts (its end then the comparison's start, the date left out on the start's day), and *… to
…* otherwise (`WindowText`). The cover's *Baseline* and *Comparison* facts and the documents' period lines
print the window, and a sheet written before `.window` is read the same way from its `.start` and `.end`.

**Combined facts.** Where the two periods print alike, one fact states the value once:
`identity.servedModels` (*\<ids\> in both periods (n and m calls)*), `serving.speeds`, `ownWaits.share` and
`ownWaits.retries` (*… in both periods*). The writer then gets the combined fact instead of the pair it
stands for (`CombinedPairs`), so a sentence never repeats one value.

Further facts: `analysis.writtenOutOfDate` (the out-of-date box above); `period.<p>.hours` (the hours
each period ran at); `level.<p>.<answers|quality|overallIndex|timeToFirstAnswerText|streamingRate|outputTokens|costPerQuestion|failedAnswers>`
(the descriptive levels, unavailable with a reason on an analysis saved before code version 6);
`robustness.<check>.summary` (a check in one sentence); `endpoint.<P>.notComputedKind`;
`controls.missing.<n>.batteryRun` and `.buildReplaced`. `scope.hours` is unavailable whenever the
periods share no time-of-week stratum, and `serving.timeOfDayAssessable` then reads *not assessable:
the periods share no hours*. **Facts kept from the
writer** (`WriterHidden`): `analysis.inputSha256`, which stays on the sheet, and the raw
`events.<n>.from` / `.to` an older sheet still carries; the writer's fact list leaves them out, and a
token of one in older prose resolves to *not shown*, *the earlier value* or *the later value*. A sheet
also keeps its own `WriterHiddenKeys` from the writer, which stay on the sheet for the rendered blocks:
each period's pair a combined fact states once (above), and in a battery comparison where both periods
have a battery Overall Index, each period's `level.<p>.quality`, the mean answer score, so that quality
has one figure. Rule C8 treats a token of either kind as kept from the writer.

**Provider availability (format 3).** Under Protocol V2 the analysis has a sixth primary endpoint,
**P6 Provider error rate** ([`ai-benchmark-chat-consistency.md`](ai-benchmark-chat-consistency.md) § 9).
It renders through the generic `endpoint.<Id>.*` block with its margin `protocol.margin.P6` (*±1.0
percentage point*), and the validator's C1 and C4 already read any `endpoint.<id>.*` key, so P6 needed no
validator change. Each period's availability is the three-part family `availability.<period>.*` (its
attempts, provider errors, rate with its interval, unattributed errors and re-runs), labeled through the
period names. The per-period levels table (*Where the chat stands*) and the writer prompt's endpoint
sentence iterate the analysis's own protocol endpoints rather than a fixed P1–P5 list, so a V1 analysis
renders its five endpoints as before and only the sentence's wording is general. The writer is told to
report provider errors as observed in GnollBench runs, with their denominator, and never a cause or an
intent.

The writer prompt names the canonical endpoint names of the analysis's protocol (P1 Quality, P2 Time to
first answer text, P3 Answer streaming rate, P4 Work per turn, P5 Cost per question, and under Protocol V2
P6 Provider error rate) and forbids any other; counts in battery
runs where the analysis compares battery runs; asks for the sample shortfall where the minimum is not
met; says that GnollBench runs are made by hand, never *monitored* or *watched*; and lists, before the
slots and beside each slot, the code-built blocks printed above it (`ChatBlocksBeforeSlotsText`,
`ChatBlocksAboveText`) with the rule *interpret them, never restate them*. The Executive Summary cites
no run id, rule id or hash; `asGoodAsBefore` leads with `{{verdict.short}}`.

A **REPETITION AND LENGTH** section holds the prose short: a sentence-valued fact (`limitation.*`, the
data-quality notes, robustness details, missing-control suggestions, next-run reasons and suggestions,
any `*.reason`) is cited at most once per document and never in parentheses; endpoints sharing a status
or a value go in one sentence; an inconclusive endpoint's MDE is cited once per document, where it is
first discussed; `robustness.<check>.summary` is preferred to the details; and with `verdict.short`
*Not enough evidence yet*, a slot has at most two sentences. Since rule C9 the section adds: never *X and
X, respectively* — a value the two periods share, or a verdict two endpoints share, is named once (*"inconclusive
for both"*), and where FACTS states one value for both periods (`identity.servedModels`, `serving.speeds`,
`ownWaits.share`, `ownWaits.retries`) that fact is cited instead of the two; a sentence-valued fact is a
sentence of its own, or quoted without its final full stop, and a sentence never goes on after it with
*and*; and an interval (a `ci95`, `ci90` or `controlChangeCi95` fact) is cited only in a sentence that
also cites its estimate and names its measure. The terms add that an Overseer update is named by what
changed, in the words of its `events.<n>.change`, `eventGroups.<n>.changes` or `events.<n>.kind` fact,
never by a field name; the Executive Summary's *What it means for a player* slot says that quality is the
battery Overall Index when one is given and that a mean answer score is never called quality; and the
Provider Issue Report's *Timeline* slot cites the windows, `{{period.baseline.window}}` and
`{{period.comparison.window}}`. **The hours rule is conditional**: with
`scope.hours` available every document cites it; without it the writer says once that the periods ran
at different hours, citing `period.baseline.hours` and `period.comparison.hours`, and never writes
*within no stratum*. CLAIM SUPPORT states which of the two applies.

### Validation

Rules 22 to 30 (**C1** to **C9**, § 3) apply on top of the prose rules. C1 to C4, the first half of C6
and the first half of C7 are checked sentence by sentence on the headline and every paragraph, and C8,
*readable text*, on each whole string: a failing paragraph is dropped, a failing headline is fatal. C9,
*repetition*, reads the headline and every paragraph with each fact token replaced by its display value,
for a repeated *A and A, respectively* and for a sentence spliced on after a full stop with a lower-case
*and*; it is a **warning**, which asks for the repair turn and marks the document **Completed with
warnings** but never drops a paragraph. C5 is a **warning**,
satisfied when the inconclusive endpoint's MDE is cited anywhere in the document, and so checked once per
document (location *sections*). The second halves of C6 (*the document never cites `{{scope.hours}}`*,
which applies only while `scope.hours` is available) and C7 (*`ruledOut` misses
an event*) cannot drop text; a note of either marks the document **Completed with warnings**.

### Figures

| Key | Figure |
|---|---|
| `cc1-quality` | Intelligence per run (native, and common grader where one exists); a battery run's Overall Index with its own 95 % interval as a whisker, the caption saying the bars are each battery run's interval, not the interval of the change |
| `cc2-speed` | Time to first answer text (telemetry; the legacy proxy hollow) |
| `cc3-work` | Output tokens per answer |
| `cc4-timeline` | Runs and events (telemetry and legacy runs; Overseer changes, annotations, served-model changes) |
| `cc5-availability` | Provider availability (format 3): each unit's provider error rate with its interval, availability-only units hollow, in the form chosen on the Charts step — points, a Laney p′ chart with *control limits*, or a day strip ([`ai-benchmark-chat-consistency.md`](ai-benchmark-chat-consistency.md) § 17.3) |

Two anchors: `ChatConsistencyResults` (13), after the verdict table, and `ChatConsistencyEvents` (14),
after the events table. cc5 is placed with cc1–cc3, after the verdict table. **Every document type may carry every figure**; which ones it carries is
chosen per document type on the wizard's step 5, and the server places what it is given
(`BenchmarkReportChartPlacement.ChatConsistencyAnchors`):

| Document | cc1, cc2, cc3, cc5 | cc4 |
|---|---|---|
| Executive Summary | after the verdict table | after the verdict table (its events table, printed only for more than three updates, is not an anchor) |
| Provider Issue Report | after the verdict table | after the events table |

The verdict table sits under *Measurements with intervals* in the Provider Issue Report and the events
table under *What we ruled out*; elsewhere under the default headings *Verdicts by endpoint* and
*Overseer updates between the periods*. In the Executive Summary the events sentence, and its table
when there is one, stand under *Our changes and their effect*.

**The legacy placement.** A document whose stored chart set has **no layout** — every chart set
uploaded before the figures were chosen per document — is placed by the earlier per-audience table,
`LegacyChatConsistencyAnchors`, so it renders as it did until its charts are drawn again:

| Document | Figures (legacy) |
|---|---|
| Executive Summary | cc1, cc2 after the verdict table |
| Provider Issue Report | cc2 after the verdict table; cc4 after the events table |

The legacy table places no cc5, which did not exist when those chart sets were uploaded.

The render service passes whether the chart set has a layout (`ReadRenderLayout`) into the renderer
(`BenchmarkReportPackRenderer.Render(document, options, chartLayoutPresent)`, the renderer's
`Context.ChartLayoutPresent`), and `KeysAt(audience, anchor, scope, legacy)` reads the legacy table
when it has none; the layout preview (`BenchmarkReportLayoutPreview`) passes it the same way.

**The client draws each document's charts at the width it prints at.** When the writing job ends, the
documents without charts get the figures chosen for their type (`chat-consistency-report-charts.ts`,
`publishCcReportCharts`): each figure is composed off-screen with Model Comparison's document sizing
(`documentChartWidth`, `documentFigureLayout`, 300 dpi, the chosen label size in points), in the print
theme with no heading and no logo, its time axis fitted to the plotted runs (`fitToData`), and its
caption opening *Not comparable across the periods: {reason}.* when its endpoint is not computable. A
figure drawn alike for several documents is composed once. Each document's PNGs are uploaded named
through `PUT report-documents/{id}/charts` with its chart layout, `{ version: 1, figures: [{ key,
widthShare, rowGroup }], maxHeightShare }`; two consecutive half-width figures at one anchor share a
row. A document whose type has no figure chosen gets none, and loses the charts it had. Step 5's
**Update charts** does the same for every written document of the analysis. The client's
`CC_REPORT_CHART_VERSION` is 8, and its settings hash covers the choice and the layout. Version 8 fits the
time axis to the plotted points and the markers near them, padded by 8 % of that span or 45 minutes,
whichever is more; widens it where a period band would otherwise be under 10 % of the plot; never gives
up the value labels of each period's first and last point for a period name; and, where a period's name
may not find room in its band, adds a one-line period legend under the time axis. A report chart
tags its Overseer-event markers as the document's events table does (`ccReportFigureInput`, from the
analysis's own events and its period split), whatever the timeline numbered them. The defaults:
Executive Summary cc1, cc2; Provider Issue Report cc2, cc4 (stored settings of the two retired document
types are dropped when read); cc1–cc3 half column and cc4 two-thirds; 8 pt labels; at most 50 % of the page
tall; stored per browser in `localStorage['overseer.benchmark.chatConsistency.reportCharts']`
(`{ version: 1, selection, layout }`). In the picker the figures are *Intelligence per run*, *Time to
first answer text*, *Output tokens per answer*, *Runs and events* and, from format 3, *Provider
availability*, placed *Results* or *Events*; the
document types *Executive* and *Provider*; a not computable endpoint's
figure carries a note; the *Layout* disclosure offers only the label size and the maximum height; and
a document type neither checked nor already written reads *Not chosen in New reports.* This scope is
exempt from the stand-alone refusal and always renders the named variant.

### File names

`BenchmarkPdfFileNames` names a chat consistency document
`chat-consistency-<analysis id>_<model slug>_<kind>_<disclosure>_<peers>[_INTERNAL].<pdf|docx>`, the kind
being `executive-summary` or `provider-issue-report`, for example
`chat-consistency-12_<model slug>_provider-issue-report_summary_anonymized.pdf`. The Download Center
mirrors the stem (`reportDocumentFileStem`).

**Cover and footer.** The PDF and Word covers of a chat consistency document carry the subject line
*Chat consistency analysis #12 — name* (the name left out of an anonymized copy, and when it is the
default name *Chat consistency: \<model\>*, which the title already says) and the facts
*Model*, *Compared* (the battery or suite, else the suites), *Baseline* and *Comparison* (each with its
dates and its battery runs or runs), *Controls* (as the naming allows, never by name in a Provider
Issue Report), *Hours* (`scope.hours`, or *none: the periods ran at different hours*), *Protocol*
(*\<protocol label\>, α \<alpha\>*), *Analysis code* (*version 7*, or *version 4 — out of date (current:
7)*, or *version 7 — out of date (its inputs changed)*, decided when the document is rendered), *Written*
(the time and the writer) and *Provenance*; the line under the facts reads *PDF layout 11*
or *Word layout 3* without a source hash. The page footer reads the short classification, then *Chat
consistency analysis #12 · Executive Summary · page 2 of 3* (`BenchmarkPdfDocumentInfo.FooterText`).
The Word file still records the source hash in its *GnollBench Source SHA-256* custom property, as
metadata only. Other scopes keep their cover and the *Source … · PDF layout 11* footer
(`BenchmarkPdfRenderer.LayoutVersion` 11: a heading followed by a short table, at most 10 body rows,
which is kept on one page, moves with the table instead of staying alone at a page foot, layout 7; a
bold label paragraph such as **Where the chat stands** does the same before its short table, layout 8;
the smaller inline code and full-width server-drawn figures of layout 9; the callouts of layout 10; and
the keep-together rules of layout 11, § 8).
`ChatConsistencyReportFormatVersion` is 6.

### Endpoints

Under `AdminChatConsistencyController` (`/api/admin/benchmark/chat-consistency`), in the run
report-documents contract (`writerModelConfigurationId`, `audiences`, `acknowledgeSameProvider`, and
here `acknowledgeOutOfDate`; enums as numbers):

- `POST analyses/{id}/report-documents/estimate` — the cost estimate with the Provider Issue Report's
  availability; no model call; 404, 400 for an audience outside the two, 499.
- `POST analyses/{id}/report-documents` — 202 with the job (`runId` is the analysis id); 404; 409 while
  its documents are being written, for a document already written, when every document exists, for
  an unacknowledged writer of the model's provider, and for an out-of-date analysis without
  `acknowledgeOutOfDate: true`, the last with `{ error, outOfDate: true }` and *"Analysis #\<id\> is out
  of date: \<reasons\>. Analyze again, or confirm to write from it anyway."* (the freshness check is `GET
  analyses/{id}/freshness`, `ai-benchmark-chat-consistency.md` § 16.1); 400 for an audience outside the two, an
  unavailable Provider Issue Report, an unusable writer or the model under report, and a refused
  endpoint; 429 at the spend guard. Usage rows use report-pack usage context 8.
- `GET analyses/{id}/report-documents/job` — 200 with the job, 204 when this process knows none; jobs
  are held in memory.
- `POST analyses/{id}/report-documents/cancel` — 202; 409 when none is in progress; documents already
  written are kept.

The documents are then read, rendered, deleted and given charts through the ordinary report-document
endpoints (§ 9), listed with `subject=chat-consistency:<id>&origin=chatConsistencyReport`.

### The client

The Write step (step 5) of the Chat Consistency wizard writes and polls the documents, in the layout
of Model Comparison's step 3, shows the analysis's out-of-date notice and asks *Write from an
out-of-date analysis?* before writing from one, views and deletes each written one, chooses each document type's charts
in *Charts in PDF and Word*, and redraws the charts of every written document with **Update charts**
(§ *Figures* above); its Documents step (step 6)
shows them in the Download Center panel with the context `{ kind: 'chatConsistency', analysisId }`,
where they can be viewed, downloaded and deleted too, and which lists them again when the host bumps
its `reloadToken`. The Download Center dialog still titles that context *Chat consistency documents*,
though the shell no longer opens it. The Download Center has a **Provider Issue Report** row (category `providerIssueReport`):
the External preset selects it with the Executive Summary, Internal selects every row. Its stored
settings are **version 5** (`STORED_SETTINGS_VERSION`, § 8); versions 2 and 3 are read and migrated by
giving every package the Provider Issue Report's empty default choice (`providerIssueReport: {}`), which
the package preset fills, and versions 2 to 4 also lose the retired document types' choices.

---

## 17. The Root Cause Analysis

The **Root Cause Analysis** (`BenchmarkReportAudience.RootCauseAnalysis`, 5) explains why a model, or
the compared models, did well or badly on the **Intelligence Index, speed and cost**. It works through
evidence, then known facts, then hypotheses with evidence for and against and a confidence grade, then
conclusions; where a root cause is not established it names the next runs, with every launcher setting
and a **Set up this run** button, and the checks a run cannot settle. It is written in every scope except
chat consistency:

| Origin | Subject | What it is set against |
|---|---|---|
| Comparison Report Pack (`ComparisonRootCauseAnalysis`) | The covered models, as equals | Each other: the pair rules, the reference, the first and last model on each axis, one model at adjacent thinking levels and the pairs of unexpected results |
| Per-model Report Pack | One entry of a comparison | Its peers, the comparison's other non-excluded entries |
| Run completion (`run:<id>`) | One run | Its **controls**: earlier runs of other models on the same exam |
| Battery completion (`battery:<id>`) | One battery run | Its controls: earlier battery runs of other models on the same battery revision |

**Numbers come from code, words from the writer** holds here more strictly than anywhere else. The
root-cause engine (`Overseer/Services/Benchmarking/RootCause/`, `RootCauseEngine.Compute`) is pure: it
takes plain inputs — the subject, zero or more peers, the comparison's pair statistics when present,
and the controls — never a DbContext, so one engine serves every origin and is unit-tested directly. It
computes the evidence, the known facts, every hypothesis and its grade, the status of each axis, the
next runs and checks, and the chart rows. Its result, `RootCauseResult`, is stored on the fact sheet as
`BenchmarkReportFactSheet.RootCause` inside `FactsJson` (no schema change), and flattened into the
`rca.*` facts for the writer and the validator. **The writer cannot change a grade, add a figure or
invent a run.** It weighs the hypotheses code graded, records grader-observed facts, and may add at
most four qualitative hypotheses of its own, capped at *Low*.

The facts are computed for every comparison and model-scope sheet, so a later Root Cause Analysis of a
stored subject reads the same sheet; the Executive Summary's prompt never sees them
(`RootCauseFacts.WriterHidden`: every `rca.*` fact, a per-model sheet's `profile.*`, the
`pair.<L>.<M>.dimension.*` and `.configDifferences` facts, and the evidence facts below, bare or under a
`model.<L>.` prefix). The Root Cause Analysis writer, in turn, never sees a price effect's facts
(`RootCauseFacts.RootCauseWriterHidden(result)`, engine version 10): every
`rca.hypothesis.<id>.<n>.*` of a hypothesis marked `PriceEffect`, and `rca.rule.<id>.*` of a rule whose
hypotheses are all price effects. They stay on the sheet for the rendered blocks; the prompt counts them on
their axis instead of listing them, leaves them out of the runs, checks, configuration differences, known
facts' *rests on* lists, CLAIM SUPPORT and CHECK examples, and the validator reads their tokens as unknown
(rule 2), in the Root Cause Analysis only.

### Principles

Each practice is enforced in code, not left to the writer:

| Practice | How the document enforces it |
|---|---|
| Separate observation from inference | Evidence and known facts are computed by code. The validator removes any paragraph in an evidence slot that uses causal wording (rule 32) |
| Test the null first | Every axis opens with hypothesis `H0`: the difference is within sampling noise (intervals overlap and no paired test established). No mechanism can be graded above *Low* for a pair whose `H0` stands. A pair no family tests is judged by the 95 % paired-bootstrap interval of its per-question differences over at least 5 shared questions (`RootCauseContext.PairedBootstrapExcludesZero`, not adjusted), engine version 5 |
| Effect sizes with intervals, adjusted for multiplicity | Pair facts keep their Holm family; decomposition shares carry bootstrap intervals over questions |
| Exact decompositions, not correlations | Speed and cost differences are split additively with the logarithmic-mean Divisia index (LMDI), which is exact. Intelligence is split by the scoring weights. No cross-model correlation is ever a known fact (n ≤ a dozen models) |
| Name the confounds | A hypothesis lists the configuration keys that differ between the models it compares (thinking level, service tier, harness, scoring method, graders, prompt options). An open confound caps it at *Moderate*. For **one model at two thinking levels** (`RootCauseHypotheses.SameModelAtTwoLevels`) the level is the explanation, not a confound: the hypothesis says so in a *For* line. Between two different models the *Configuration* check passes and names the level as part of the model's configuration, so it never contradicts Appendix A |
| Instrument before model | Every hypothesis has a **locus**: `model`, `chat` (prompt or tools), `harness` (grading or instrument), `suite` (question or rubric), `corpus`, or `provider` (serving or infrastructure) — the triage categories of `server_benchmark_to_chat_transfer` § 2, plus model and provider. A `harness` or `suite` hypothesis graded Moderate or higher sets the **system first** flag (`rca.systemFirst`), which puts model-selection runs behind it, as `server_benchmark_runbook` § 4 orders them |
| Falsifiability | Every hypothesis carries a *refuted if* statement. Every hypothesis below *High* names the next run, or the check, that would settle it |
| Robustness | Code runs leave-one-question-out, leave-one-suite-out (battery), replicate stability and panel-member agreement checks on each hypothesis. Each one that fails or cannot be assessed lowers the grade, with a stated reason |
| Grader neutrality | Judge-dependent pairs and same-provider graders become `harness`-locus hypotheses of their own (HQ4, WQ6), never footnotes |
| No re-grading, no invention | The writer cannot change a grade, add a figure or invent a run. Its own hypotheses are capped at *Low* and validated (rule 35) |

### Evidence and known facts

**Per-model facts** (`RootCauseEvidence`), unprefixed on a model-scope sheet (`speed.ttftP90`) and as
`model.<L>.*` on a comparison sheet, from `BenchmarkRunAnswer` columns and the candidate's
`ModelCallTelemetry`, which comparison, model-scope run and battery preparation load as projections of
the columns needed, never whole entities (battery preparation also loads the subject's tool calls):

- **Speed** — `speed.ttftP90`, `.toolTimeP50`, `.modelCallsPerAnswer`, `.ttftPerCallP50`, `.decodeRate`
  (visible tokens per second, `CallTelemetryMeasures.AnswerStreamingRate`), `.ownWaitShare`
  (`CallTelemetryMeasures.OwnWaitShare`), `.retriesPerAnswer`, `.servedTierMismatch`;
  `tokens.reasoningPerQuestion`, `.reasoningShare`.
- **Cost** — `cost.component.<c>.perQuestion` for `c` ∈ {`uncachedInput`, `cacheRead`, `cacheWrite`,
  `output`, `reasoning`}; `tokens.cacheReadShare`; `price.<c>.perMillion` from the run's
  `PricingSnapshotJson` (the current card under the *Current* pricing basis); `servedTier`.
- **Intelligence** — the per-dimension paired families (`pair.L.M.dimension.<d>.<family>`);
  `score.withTools` and `.withoutTools` (the mean score of answers with and without tool calls);
  `outcome.confirmedCriticalRate` with its Wilson interval; on a model-scope sheet the subject's failure
  profile, `profile.*`, from `BenchmarkComparisonModelProfile.FromRuns` (the cross-model matrix counts —
  isolated and shared misses, lowest scorer — need three models and stay unavailable there).
- **Configuration** — `config.serviceTier`, `.reasoningMode`, `.parallelMode`, `.maxOutputTokens`, and
  `pair.L.M.configDifferences` (the keys other than the model that differ, from
  `RootCauseContext.ConfigDifferenceParts`, written without parentheses: *"thinking level, medium against
  high; provider"*, engine version 9).
- **Comparison-wide** (comparison sheet, format 23) — `comparison.runsPerSuite` (*"1 run per suite"*, or a
  span *"1 to 2 runs per suite"*), `comparison.suiteCount` (*"2 suites"*), on a battery comparison
  `comparison.memberRuns` (*"2 runs per model"*), and `comparison.configDifferences`, every key other than
  the model and the provider that differs anywhere across the models with its values in letter order
  (*"thinking level: medium, high, max, xhigh; reasoning mode: not set, standard"*, or *"no configuration
  key other than the model"*). A statement about every compared model cites these, never one model's or
  one pair's fact.
- **Rule share spans** (engine version 9) — for a rule with two or more hypotheses that carry a share,
  `rca.rule.<id>.shareSpan` (*"29% to 48% of each gap"* for pair rules; *"… of each model's own time"*,
  *cost* or *points at risk* for within-model rules) and `rca.rule.<id>.count` (*"3 pairs"*, *"3
  models"*); the nulls get none. A group of hypotheses is described by its span beside the reference range (rule 38).

Each `sheet.Questions` row also carries the subject's per-question `InputTokens`, `OutputTokens`,
`CostUsd`, `TtftMs` and `ModelCallCount`, means over runs, which the per-question decomposition reads.
**A fact whose source columns are missing on a run** — an older `CallTelemetryVersion` or harness — is
*Unavailable* with a reason, **never zero**; the hypotheses that need it do not fire, its chart row reads
*not recorded*, and a `replicate` run may be proposed instead.

**A provider that counts thinking inside its output tokens** (Anthropic,
`CallTelemetryMeasures.ReportsReasoningSeparately` false) stores `ReasoningTokens = 0`, which is not a
measurement. Its answers carry `ReasoningReported = false`: `tokens.reasoningPerQuestion`,
`.reasoningShare`, `cost.component.reasoning.perQuestion` and `price.reasoning.perMillion` are
*Unavailable* (*"This provider counts thinking inside its output tokens and does not report it
separately."*), and `cost.component.output.perQuestion` and `tokens.outputPerQuestion` end *(thinking
included)*. The cost rows and the chart's R3 segments show one *output, thinking included* component
(`RootCauseSegments.OutputWithThinking`). A cost pair of one side that reports reasoning and one that
does not is split into input and output from engine version 10 (*Decompositions* below). A pair split by
component in which neither side reports keeps its five components for the rules, and its written form
merges output and reasoning into one row whose effects are the sums, so it still sums exactly
(`RootCauseContext.WrittenCostPair`; a grouped pair is written as it is). HS3 is not established for such
a pair and says why; HC3 does not fire.

**Grader-family gaps** come from the panel diagnostics, unchanged (`BenchmarkPanelDiagnostics.BuildFamilyGaps`,
copied by `RootCauseEvidence.FamilyGaps`): per provider pair, the quality gap under member A, under member
B and under the panel, and for the two members' own provider pair the **interaction contrast** (member A's
gap minus member B's, per question) with its 95 % interval, withheld under 5 paired questions. Facts
`rca.graders.familyGap.count` and per gap `rca.graders.familyGap.<n>.providers`, `.underA`, `.underB`,
`.contrast` and `.notable` (the contrast's interval excludes zero). A panel score is the mean of two
members, so a contrast of *d* moves a cross-provider pair by about *d* ÷ 2.

**A battery comparison** carries no panel diagnostics on the comparison itself
(`BenchmarkBatteryModelComparison.Build` leaves `PanelDiagnostics` null, so the Model Comparison page
shows none for batteries). The report path computes them: `BenchmarkComparisonReportFacts.Build` runs
`BenchmarkPanelDiagnostics.Compute` over the covered entries' member runs, already loaded with their
answers (the grader snapshots it fingerprints are `AutoInclude` navigations), and works on a copy of the
comparison that carries them. Answers pair on the global question id and item revision, so the two
suites' questions never mix. `Compute` pools a battery's answers with the single-suite formula, so its
per-entry indices are **not** on the battery's weighted scale; they serve only the family gaps, which are
per-question differences. The indices HQ4 compares are the battery's declared-weights composites instead
(`panel.memberAAlone`, `panel.memberBAlone`, `panel.referenceReaderIndex`, passed to
`RootCauseEvidence.ModelInput` as `memberAIndex`, `memberBIndex` and `referenceReaderIndex`, each winning
over the diagnostics entry), and the judge-dependent pairs are the strict reversals of the two member
composites (`BenchmarkPanelDiagnostics.RankReversals` on labelled indices: a tie is not a reversal, a
missing index is skipped), which feed both HQ4 and the notable findings. Two consequences are accepted:
a family gap is unweighted across suites, and the reader composite skips the questions the reader left
ungraded, whereas the run-level reader index is withheld unless the reader graded every answer, so HQ4
can be refuted on a battery where a run-level comparison would leave it standing.

**Decompositions** (`RootCauseDecomposition`), all exact, per pair — the reference against each model,
plus the leader and the last model on each axis — and within one model by group (difficulty band, or
suite on a battery), the overall model on one side and the group on the other:

**Pair coverage** (`RootCauseContext.BuildPairs`, engine version 10). After the reference pairs and the
axis extremes, a comparison adds **the same model at adjacent thinking levels** — for each model id at two
or more levels, each adjacent pair in thinking order — so HQ8 can fire for siblings that are neither the
reference nor an extreme, and then **the pairs of the unexpected results** not yet present, in their
order, at most `RootCauseEngine.MaxAnomalyPairs` (6) (*Unexpected results* below; they are found before the
pairs are built). An added pair has a paired test only where the all-pairs family exists (3 to
`BenchmarkComparisonReportFacts.AllPairsMaxEntries`, 6, models); otherwise its rules rest on the bootstrap
intervals, as for any untested pair. The reference and extreme pairs keep their hypothesis numbers except
where the per-group and per-axis caps now keep another set.

**Only an established difference is decomposed** (engine version 2). A speed or cost difference between
two models is split into components only when it is established: its family's paired test, or, where no
family tests the pair, its bootstrap interval excluding zero (`RootCauseContext.DifferenceEstablished`).
A share of a difference that may be zero has no meaning — a near-zero total gave shares such as *676 %
(−5654–5689 %)* — so every other analyzed pair keeps its total and interval, is marked *not decomposed:
difference not established*, and the HS and HC rules do not fire on it.

- **Speed.** Per question, `model time = Σ calls (TTFT + decode) + gap`. The difference splits into TTFT,
  decode and gap; TTFT splits by LMDI into *call count* and *per-call latency*, decode into *output
  tokens* (with the reasoning share) and *decode rate*. Facts `rca.speed.<T>.<T>.<component>.share` and
  `.interval`, and `.total`.
- **Cost.** `cost = Σ_k tokens_k × price_k`. LMDI splits the difference into a *volume* and a *price*
  effect per component. Facts `rca.cost.<T>.<T>.<component>.<volume|price>.share`. From engine version 10
  the split depends on whether the pair is **comparable** (`RootCauseDecomposition.Comparable`): over the
  shared questions, each of the five components has positive mean tokens on both sides or on neither, and
  both sides report reasoning apart from output or neither does. A comparable pair keeps the five
  components (`Grouped` false). Any other pair — typically two providers that bill different token kinds:
  one with no cache writes, the other with no uncached input, one folding thinking into output — is
  **grouped** (`RootCausePairDecomposition.Grouped`): **input** (uncached input, cache reads and cache
  writes) and **output** (output and reasoning), in tokens and in cost, each split into token volume and
  price; with both sides' uncached-input list prices known, the input's price effect is split again by a
  three-factor LMDI (`RootCauseStatistics.Lmdi3`: volume `L·ln(v₁/v₂)`, list price `L·ln(p₁/p₂)`, caching
  the remainder, so the three sum exactly; a non-positive input falls back to the two-factor split) into
  the **price card** (`input/price`) and **caching** (`input/caching`, what sets the effective price per
  input token apart from the list price: the cache discounts and surcharges). The labels read *input,
  token volume*, *input, price card*, *input, caching*, *output (with reasoning), token volume* and
  *output (with reasoning), price card*; the all-components rows are `all/volume`, `all/price` and, grouped
  with both list prices, `all/caching`. So a billing-scheme difference is no longer counted as token volume,
  which had made claims such as *"costs more mainly from token volume"* between providers artifacts. Every
  decomposed pair also gets **`VolumeRatio`**, its cost ratio at equal prices, `exp(all/volume ÷
  L(total₁, total₂))`: an LMDI volume index, not a ratio of token counts, and with the price ratio and (grouped)
  the caching ratio, formed the same way, it multiplies exactly to `total₁ ÷ total₂`. Facts
  `rca.cost.<T>.<T>.grouped` (*"split into input and output, because the two models are not billed for the
  same token kinds"* or *"split by token kind"*), `.volumeRatio` (*"2.4 times"*: one decimal below 10, whole
  from 10, `RootCauseFacts.RatioDisplay`), and `.input.<volume|price|caching>.share`,
  `.output.<volume|price>.share`, `.all.caching.share`. The bootstrap intervals, the leave-one-out and
  across-suites checks cover the grouped components as they cover the others; the within-model cost shares
  are unchanged.
- **Intelligence.** The index difference split by the scoring weights into dimension contributions,
  plus `other` (the critical-error cap and difficulty weighting), summing exactly to the difference:
  `rca.quality.<T>.<T>.dimension.<d>.points`. The **at-risk points** of each failure signal — answers
  without tools; budget, truncation or output defects; confirmed critical errors; other low-scoring
  answers — count the points lost on answers showing the signal, relative to the model's own median,
  each answer once (`rca.atRisk.<T>.<segment>`). They are labeled *upper bound, not a causal estimate*.

LMDI uses the logarithmic mean `L(a,b) = (a − b) / (ln a − ln b)`, with `L(a,a) = a`; a zero component
takes the standard limit, its whole difference going to volume (between two sides that bill different
components that limit is what the grouped split avoids). Two models priced on the same card (on
the catalog basis, GPT-6 Luna and Claude 5.5 Haiku have identical cards) have no price effect in any
component, so their volume share is exactly 100 % in every resample, *100% (100–100%)*: a correct
reading, not a defect of the zero-component limit. Every share has a 95 % percentile
bootstrap interval over questions: **2,000 resamples, fixed seed 20261015**, so the same inputs always
print the same interval. Spearman's ρ (`RootCauseStatistics.Spearman`, with a Fisher-z interval over
questions) and the Mann–Whitney test of the within-model contrasts are used only within one model,
across its questions, never across models. *T* in a key is a model token: a letter, or `subject`.

**Known facts** (`RootCauseKnownFacts`) are numbered sentences, `rca.known.<n>`, each produced by a rule
and stating only what a test or a count proves, with its kind (`test`, `count`, `decomposition`,
`configuration`) and the fact keys it rests on: *"Model A's median model time is 1.5 times Model F's on
the same questions, established after Holm across 8 tests"*; *"Decode accounts for 71% (58–80%) of the
per-question time difference between Model C and Model F"*; *"Model I answered 9 of 36 questions without
a tool call"*. At most 24 are kept (`MaxFacts`); the first 6 count and configuration facts
(`ReservedCountAndConfigurationFacts`) always keep their slots, and the tests and decompositions fill the
rest in rule order (engine version 6). Before, the tests and decompositions filled the list first, so a
large comparison had no count or configuration fact left for Appendix E.

### Confidence grades and axis status

| Grade | Code rule |
|---|---|
| **High** | The mechanism is quantified, and its paired test (or its share's 95 % interval) is established after adjustment. The pair's `H0` is rejected. No open confound. Every robustness check passed. At least 2 runs per suite on both sides |
| **Moderate** | Quantified and directionally consistent, but exactly one of these holds: one robustness check failed or could not be assessed, a single run per suite, or one open confound |
| **Low** | Qualitative only (grader comments or excerpts), or two or more of the *Moderate* conditions, or the pair's `H0` stands |
| **Not supported** | The evidence against outweighs the evidence for, under the rule's own pre-declared test. Example: "slower because of more tool calls" when tool time is under 10 % of the time difference |

In code (`RootCauseHypotheses.Grade`) each robustness check that failed or could not be assessed is one
condition — the *Replicates* check carries the single-run condition and the *Configuration* check an
open confound — and so is a test or share interval not established: no condition gives High, one gives
Moderate, two or more give Low. A pair whose `H0` stands caps the grade at Low, and every other cap (the
control limit, a rule's own ceiling) applies with its reason, listed in `gradeReasons`. A **null
hypothesis** that is rejected is graded *Not supported*; one that stands is graded at most Moderate
(*"the absence of an established difference is not proof of equality"*), or Low when either side has a
single run per suite.

The robustness checks, each `Passed`, `Failed` or `NotAssessable` with its detail: **Leave one question
out** (the deciding share keeps its sign and stays the largest component after the question that
contributes most is dropped); **Across suites** (battery: the same sign in every suite with at least 5
questions); **Replicates** (at least 2 runs per suite on both sides, or on the subject for a
within-model rule); **Panel agreement** (both panel members' per-member indices order the pair alike,
or show the same within-model contrast); **Configuration** (no differing key other than the model axis;
pair rules only); **Share interval** (engine version 9: a share whose 95 % interval is wider than one
hundred percentage points fails, one grade lower, with the reason *"the share's 95% interval, 139–825%,
is wider than one hundred percentage points"*). A *"mainly from"* statement whose share is above 100 %
adds *", which alone exceeds the gap while other components pull the other way"* (engine version 9).

Each axis then gets a status, the fact `rca.axis.<axis>.status` with `rca.axis.<axis>.best`:
**Root cause established** (a non-null hypothesis graded High and its `H0` rejected), **Probable cause**
(the best hypothesis is Moderate), or **Not established** (otherwise, with the best hypothesis' grade
and reasons, or *"No hypothesis fired on this axis."*). In a document with pairs (a comparison or a
per-model pack) the first two need a **pair** hypothesis: a within-model hypothesis says where one
model's own time, cost or points go, not why the models differ, so an axis on which only within-model
hypotheses fired reads *Not established* with *"Only hypotheses about one model's own breakdown fired on
this axis; none compares the models."* A run- or battery-completion document has no pairs and takes the
best hypothesis of either kind.

**A price effect never decides an axis** (engine version 6). An HC2 whose price effect follows from the
price cards — catalog pricing, or neither side's prices differing between runs — is marked `PriceEffect`
(`RootCauseHypothesis.PriceEffect`, the fact `rca.hypothesis.<id>.<n>.priceCard`). It is what the price
cards show, not a cause the runs found, so it never sets an axis' status. When another candidate of the
axis is graded Low or higher, that one is the axis' best and sets the status. When none is, the axis
reads *Not established* and its best hypothesis is the highest-ranked price effect, which the
`rca.axis.<axis>.best` fact names; from format 26 the conclusions table prints *—* as that row's best
hypothesis, since a price-card difference is no mechanism the runs found. The reason is
*"The runs show no cost mechanism beyond the price cards: every hypothesis that fired on this axis is a
price-card difference."* (`PriceOnlyAxisReason`) when price effects alone fired, and *"No cost mechanism
beyond the price cards remains: every other hypothesis on this axis was ruled out."*
(`PriceCardsAxisReason`) when the others were all ruled out (engine version 10; before, both said that
price per token accounts for the gap). A price effect keeps its grade, sorts after every other hypothesis
of its axis, and prints as *Known from the price cards (High)* (`RootCauseGrades.PriceCardWordWithGrade`,
also the `.grade` display). It still answers question 4 and supports a causal sentence about cost (rule
33), but not a certainty word (rule 34), and it is exempt from the single-run cap. A stored result without
the flag keeps the status it was written with.

**Price effects are set aside** (engine version 10, format 26). What the price cards already say is not
repeated as a finding: the hypotheses tables and figure R4 leave price effects out, and one line under an
axis' table counts them (*"5 cost differences follow mainly from the price cards (HC2.1–HC2.5); Appendix C
lists them."*), which stands alone when nothing else fired; Appendix C keeps them; the writer never sees
their facts (`RootCauseWriterHidden`, above).

**A price effect is not refutable by a run** (engine version 7): its *refuted if* reads *"Not testable
by a run: the share follows from the price cards and these runs' token counts."* (`PriceEffectRefutedIf`),
and its *confirmed if* says the same; a
non-price HC2 keeps *"The volume effect is larger."* The prompt tells the writer to give a price effect
no refutation.

**The price note** (engine versions 7 to 9) is gone from engine version 10. It said, when an axis's best
hypothesis was not a price effect and a price effect covered a larger difference, that *"Price per token,
known from the price cards, explains the largest cost differences (HC2.1–HC2.2; 103% of the largest)."*,
as the fact `rca.axis.<axis>.priceNote`, which the Conclusions' *Why* cell appended and the conclusions
were asked to restate. That is what the price cards say. `RootCauseAxisStatus.PriceNote`, the fact, its
label and its sentence shape are removed, and a document stored with one no longer prints it. The writer
is told never to mention price per token or the price cards in the headline or the conclusions; when the
cost axis has nothing beyond the price cards, the conclusions say that the runs show no cost mechanism
beyond them.

### The hypothesis rules

Each rule fires on a pre-declared condition, names the models (the worse first), and computes its
evidence for and against and its grade. Every pair rule is applied per axis to the pairs where the axis
differs; excluded and degraded entries take part only on the axes they are comparable on.

**Pair rules** (`RootCauseHypotheses.PairRules`):

| Id | Axis | Hypothesis (template) | Locus | Evidence for | Evidence against / refuted if |
|---|---|---|---|---|---|
| H0-Q / H0-S / H0-C | each | The difference between A and B is within sampling noise | — | Intervals overlap; no family establishes the pair | A paired test established (where no family test exists, the difference's bootstrap interval excluding zero, unadjusted) |
| HQ1 | Intelligence | A scored lower because it answered without tools more often | model / chat | Higher `noToolAnswers` rate; `score.withoutTools` below `.withTools` | Without-tool answers score as well; rate difference under 2 answers |
| HQ2 | Intelligence | A's shortfall is concentrated in confirmed critical errors | model | Critical-error at-risk points at least half the gap | Caps unresolved or overturned (method 13); the shortfall is spread across many questions |
| HQ3 | Intelligence | Shared failures on specific questions point to the instrument, not the models | suite / corpus / chat | `sharedLow` questions; every model or nearly every model below 50 | One model answered well |
| HQ4 | Intelligence | The order depends on the graders | harness | Judge-dependent pair; a scoring grader (member A, member B, or the assessor of a single-assessor run) whose provider matches exactly one side — the reference reader, second reader and claim verifier never count; panel disagreement rate at least twice the median. For a pair of the members' own two providers with an available interaction contrast it is **quantified**: share 0.5 × \|contrast\| ÷ the pair's gap, interval from the halved contrast bounds, established when the contrast's interval excludes zero | Both members and the reference reader order the two alike, and the pair is not judge-dependent (`PanelAgreementCheck` plus both reader indices); without a reader the rule is not refuted |
| HQ5 | Intelligence | In comparison scope, **once for the document** (`RootCauseHypotheses.SharedStyleConflict`, engine version 10): *"Completeness was the lowest dimension of N of M models under the concise response style."*, shaped as HQ3 is — kind `pair`, group `all`, every model with the conflict in `Models`, label *"the concise response style, N of M models"* — so it answers no research question and names no model's block (`RootCauseHypotheses.IsDocumentWide`); it fires when at least two models have the conflict, or the one model of a single-model document, with no worse-side gate, since it is about the instrument. In model scope, **once per model** (`StyleConflict`, engine version 6): Completeness was A's lowest dimension under the concise style, kind `within`, group `style:<token>`, for each model whose `responseStyleConflict` is true and that is the lower-scoring side of some analyzed pair. Both forms are capped Low, with fact keys each model's `style.responseStyleConflict` | chat | `responseStyleConflict` true | A run of the reference model with the detailed response style shows that Completeness is no longer its lowest dimension, which tests the style effect under this configuration and is indicative for the other models (never graded above *Low*: no comparison run covers style; it states a condition, not a cause, as `server_benchmark_to_chat_transfer` § 3 requires) |
| HQ6 | Intelligence | A guessed where it should have abstained | model | `wrongInsteadOfAbstaining` higher, with Wilson intervals not overlapping (the measure is *Unavailable* under 3 incorrect or not-attempted outcomes) | Intervals overlap |
| HQ7 | Intelligence | Budget, truncation or output defects cost A points | harness / provider | `toolLimitHits`, `truncated`, `outputDefects` at-risk points, at least 1 and above B's | Zero signals; points at risk under 1 |
| HQ8 | Intelligence | A's lower thinking level explains the gap | model | Same model at two levels in the set; the dimension test at the lower level | Thinking-level inversion (the notable finding) |
| HQ9 | Intelligence | The verifier would clear part of A's Accuracy shortfall | harness | Verification-cleared sensitivity above the index (by at least half a point) | Sensitivity equals the index |
| HS1 | Speed | A is slower mainly from more model calls (tool rounds) | model / chat | Call-count share is the largest, with an interval excluding 0 | Per-call latency or decode share is larger |
| HS2 | Speed | A is slower mainly from longer per-call latency (TTFT) | provider / model | TTFT share largest; `ttftPerCallP50` ratio established | Decode or call count dominates |
| HS3 | Speed | A is slower mainly from more output (reasoning) tokens | model | Output-token share largest; `reasoningShare` higher. Not established, with an *Against* line, when either side does not report reasoning separately | Decode-rate share larger |
| HS4 | Speed | A is slower mainly from a slower decode rate | provider | Decode-rate share largest | Token share larger |
| HS5 | Speed | Own waits or retries inflated A's time | harness / provider | `ownWaitShare` or `retriesPerAnswer` above the median | Under 5 % of the gap |
| HS6 | Speed | Served tier differed from the requested tier | provider | `servedTierMismatch` | No mismatch |
| HC1 | Cost | A costs more mainly from token volume | model / chat | Volume effect largest among the all-components effects (volume, price and, grouped, caching); tokens per question ratio established | Price effect larger; a grouped pair's caching effect is listed against it |
| HC2 | Cost | A costs more mainly from per-token price | model | Price effect largest (in a grouped pair, the price-card effect, with caching apart). Under catalog pricing, or when neither side's prices differ between runs, it carries no *Replicates* check but a *configuration* check (*"Compare the two models' price cards; the price effect follows from them."*), the single-run grade cap exempts it, and it is a **price effect**: *Known from the price cards*, never an axis' root cause (*Confidence grades and axis status*) | Volume larger |
| HC3 | Cost | Reasoning tokens drive A's cost | model | Reasoning component largest; both sides report reasoning separately; a pair split by component only | — |
| HC4 | Cost | Weak prompt caching raises A's cost | chat / provider | Split by component: `cacheReadShare` below the median by 20 points or more, and the uncached-input component is largest. Grouped (engine version 10): the caching effect is the largest all-components effect, stated *"Prompt caching raises A's price per input token against B."* (short *Caching, input price*), its share the `input/caching` component; the cache-read share line is context, under *For* when it is 20 points or more below the median and under *Against* otherwise | Split by component: cache-read share comparable. Grouped: the volume or the price-card effect is larger |
| HC5 | Cost | More tool rounds multiply A's input tokens | chat / model | Input-volume effect largest (grouped: the input group's) and `modelCallsPerAnswer` higher | Calls per answer comparable |

**Within-model rules** (`RootCauseHypotheses.WithinRules`) apply to a model on its own, in every scope:
in comparison scope to the reference and to the first and last model on each axis, in model scope to the
subject. Their null, `H0-W`, is that the contrast between two question groups of the same model is
within sampling noise, tested by a Mann–Whitney test on the two groups' per-question values,
Holm-adjusted across the contrasts the rules make for that model. A within-model hypothesis is a
**decomposition statement** — where the time, the cost or the points go — never a comparison with other
models, and its wording says so.

| Id | Axis | Hypothesis (template) | Locus | Evidence for | Evidence against / refuted if |
|---|---|---|---|---|---|
| WQ1 | Intelligence | A loses most of its points on answers without tool calls | model / chat | `score.withoutTools` below `.withTools`, `H0-W` rejected; their at-risk share is the largest | `H0-W` stands, or fewer than 3 answers without tools |
| WQ2 | Intelligence | A's points are lost mostly in one difficulty band (or, on a battery, one suite) | model / suite | That group's at-risk share is the largest, exceeds the other models' median share there by at least 15 points (`WithinGroupExcessOverPeers`; a document with one model skips this) and its contrast rejects `H0-W`; on a battery, the leave-one-suite-out index moves by more than the interval half-width | Contrast within noise; shares even |
| WQ3 | Intelligence | Confirmed critical errors cost A most of its points | model | Critical-error at-risk share largest | Caps unconfirmed |
| WQ4 | Intelligence | A guesses where it should abstain | model | `wrongInsteadOfAbstaining` above 0.5 with a Wilson interval above 0.33 | Interval includes 0.33 |
| WQ5 | Intelligence | Budget, truncation or output defects cost A points | harness / provider | At-risk points of those signals at least 1 | — |
| WQ6 | Intelligence | Grader disagreement makes A's index uncertain | harness | Panel disagreement rate above 0.15, or a member-index gap above the interval half-width | Members agree |
| WS1 | Speed | Most of A's time is model deliberation before output (TTFT) | provider / model | TTFT share of model time is the largest component, with an interval excluding the next one | Decode share larger |
| WS2 | Speed | Most of A's time is output generation (decode), driven by reasoning tokens | model | Decode share largest and `reasoningShare` ≥ 0.5 | Reasoning share under 0.5 |
| WS3 | Speed | Tool rounds multiply A's time | model / chat | Call-count term largest; model time rises with model calls per answer across questions (Spearman ρ ≥ 0.5, confidence interval excluding 0) | ρ interval includes 0 |
| WS4 | Speed | Tool execution adds materially to A's wall time | chat / corpus | `toolTimeP50` ≥ 25 % of total time | Under 10 % |
| WS5 | Speed | Own waits or retries inflate A's time | harness / provider | `ownWaitShare` ≥ 10 % or retries on ≥ 10 % of answers | Below both |
| WC1 | Cost | Output (with reasoning) is A's largest cost component | model | Output component share largest, interval excluding the next | — |
| WC2 | Cost | Uncached input is A's largest cost component | chat / provider | Uncached-input share largest and `cacheReadShare` under 0.5 | Cache-read share ≥ 0.7 |
| WC3 | Cost | Tool rounds multiply A's input cost | chat / model | Input cost rises with model calls per answer (ρ ≥ 0.5, interval excluding 0) | ρ interval includes 0 |

**What is kept.** Every null hypothesis is kept; of the rest, the strongest three per axis and group (a
pair, or one model's own rules), and at most **12 per axis** in total (`KeepPerGroup`, `MaxPerAxis`), so
the prompt stays bounded. A research question (below) that no kept comparative hypothesis graded Low or
higher answers **reserves** the best comparative one that does, even beyond the cap, or, when there is
none and no kept within-model one answers it either, the best within-model one: at most one per question, so intelligence holds at most `MaxPerAxis` + 2 and speed and
cost `MaxPerAxis` + 1, and a question printed with no hypothesis truly has none. Hypotheses rank
(`RootCauseHypotheses.Ranked`) by grade; then a pair whose null is rejected before one whose null stands;
then by **the amount they explain in the axis' own unit** (`Explains`: the share, between 0 and 100 %,
of the pair's gap in index points, milliseconds or dollars per question, or of the model's own points at
risk, time or cost per question; a hypothesis without such a share last); then by rule id. A share above
100 % arises when another component pulls the other way and is largest on the smallest gaps, so ranking
by the share itself would put a negligible gap first; a negative share works against the gap and
explains none of it (HQ1 is *Not supported* when the worse model's answers without tools put no more of
its points at risk than the better model's). The axis' best hypothesis is the first in this order. Each kept hypothesis becomes the
facts `rca.hypothesis.<id>.<n>.statement`, `.label`, `.axis`, `.models`, `.locus`, `.grade`,
`.gradeReasons`, `.for`, `.against`, `.refutedIf`, `.nextRun` or `.check`, and `.share` and `.interval`
where it has a share; `<n>` numbers the hypotheses of one rule from 1. The **label**
(`RootCauseLabels.Of`) is a noun phrase that reads inside a sentence, built from the rule and its
models: a pair's is *"{first}'s {topic}, compared with {second}"* (*"Model F's critical errors, compared
with Model E"*, *"Vega Pro's higher price per token, compared with Orion Max"*) and a within-model one
*"{model}'s {topic}"* (*"Model D's time spent on tool rounds"*). `MaxWords` (10) caps the topic only, so
a model name is never cut. Labels appear in Conclusions and Appendix C; the hypothesis tables print the
id alone in their first cell. From engine version 5 each hypothesis also has
`rca.hypothesis.<id>.<n>.ref`, and the writer cites a hypothesis in prose by its `.ref` token, never by
chaining labels into a sentence; rule 33 accepts any token under the hypothesis prefix. From engine
version 6 the `.ref` displays the **id alone** (*"HQ1.1"*; version 5 added the rule's topic, *"HQ1.1
(answers without tools)"*, which put a parenthesis inside the writer's own and repeated topics the
writer had just named). `RootCauseLabels.Topic` still builds the labels. The writer names what the
hypothesis is about in its own words, once per paragraph, and cites the `.ref` after it; several
hypotheses of one rule are cited once, as a range (*HQ1.1–HQ1.3*).

**Displays print the figure alone** (engine version 6), so that a token reads inside the writer's
sentence without repeating it or nesting parentheses: a share is the bare percentage (*"72%"*, no longer
*"… of the difference"*), which the writer gives as *"… of the gap"* after the cause; a pair total
`rca.<axis>.<F>.<S>.total` is the amount alone (*"+46.1 s per question"*, *"+$0.012 per question"*,
*"+3.2 index points"*), its direction (F minus S; speed is model time) stated once in a FACTS note of the
prompt; a speed or cost difference that is not established has its own fact,
`rca.<speed|cost>.<F>.<S>.decomposed` = false (*"not split into parts: the difference may be chance"*);
an at-risk figure is *"3.2 index points"*, its total *"10.7 index points below a median answer of 89"*,
its label ending *(upper bound)*; `tokens.cacheReadShare` is the bare percentage; and
`price.<c>.perMillion` is *"$0.50 per million tokens"*, the pricing basis stated once, with differing
cards their own fact, `price.cardsDiffer` (*"the runs' cards differ; the lowest-id run's prices are
shown"*). Under FACTS the prompt prints one note per kind of such fact on the sheet (*"rca.atRisk.\*
are index points at risk: an upper bound on what a failure signal could have cost, never points
lost."*).

**Null hypotheses** keep their grade value and sort order, but documents print their grade as what it
means: a rejected null as *Rejected — a real difference (Not supported)*, a null that stands as *Stands —
may be chance (Low)* (`RootCauseGrades.HypothesisWordWithGrade`), so a reader is never told "Not
supported" about a difference that was found. **Grade reasons** print the one reason that applied, never
the rule's *"A, or B"* (`RootCauseHypotheses.CheckReason`, `NullStandsReason`, `QualitativeReason`).

### The research questions and the model profiles

**From format 25 (per-model format 21) the research questions are not printed.** The engine still computes
them and stores them in `RootCauseResult.Questions` and the `rca.question.<n>.*` facts (the stored format
and `RootCauseEngine.Version` are unchanged), because they select hypotheses: `RootCauseHypotheses.Keep`
reserves a hypothesis per question beyond the per-axis cap, and question 1 adds the leader's quality split
to Appendix A. The writer's FACTS list leaves the `rca.question.*` facts out, the writer output has no
answer slots, and the Summary has no *The questions* box, for a stored document as well. The rest of this
section describes how the questions are built, and, in the paragraphs on answers, strengths and signals,
what documents printed up to format 24 (per-model format 20).

**The four questions** (`RootCauseProfiles.Questions`) are the ones a reader brings to the document, each
about an axis extreme `AxisExtremes` already picks, and asked only when that axis has at least two
comparable models and the model is **strictly** ahead of its neighbor there: a tie at the top, the
bottom, the slowest time or the highest cost asks nothing (`RootCauseProfiles.Targets`):

| n | Question | Model |
|---|---|---|
| 1 | *Why did {model} score highest?* | Rank 1 on the Intelligence Index |
| 2 | *Why did {model} score lowest?* | Last on the Intelligence Index |
| 3 | *Why is {model} slowest?* | Slowest by median answer time |
| 4 | *Why is {model} the most expensive?* | Highest cost per question |

For questions 1 and 2 the model's place against its neighbor is **established** when a paired
Intelligence test favoring it is established, or when the two 95 % intervals do not overlap (bounds that
touch overlap, as in tied ranks). When it is not, the question says so: *"Why did {model} score highest,
though its lead over {neighbor} is not established?"*, *"Why did {model} score lowest, though its gap to
{neighbor} is not established?"*.

Each is the facts `rca.question.<n>.text`, `.model`, `.hypotheses` (the kept hypotheses that answer it,
comparative first, then within-model, *"none fired"* without one), `.status` (the strength in plain
words: *Established (High)*, *Probable (Moderate)*, *Suggestive (Low)*, or *Unknown*), `.signals` (the
model's top measured signals, from its profile), `.withinOnly` (no comparative hypothesis graded Low or
higher answers it) and, for questions 1 and 2, `.orderEstablished` and `.neighbor` (engine version 6:
the runner-up for question 1, the model above for question 2, displayed by name; also
`RootCauseQuestion.Neighbor`). Question 1 takes only the hypotheses about the pair itself, **HQ4 and HQ8
with the leader as their second model**: the other intelligence rules locate the mechanism in the other
model (its tool use, its critical errors, its guessing), which says why the runner-up lost points, not
why the leader scored highest (engine version 6; before, it took every pair hypothesis where the model
was the better side). With none, its status is null. As its first signal it takes the dimension split of its lead over the
runner-up (*"most of the lead over {runner-up} is Completeness (+2.7 of +4.4 points)"*, or *"the largest
part"* under half), computed beside the analyzed pairs when that pair is not one of them; then its
strengths. Questions 2 to 4 take the pair hypotheses where the model is the **worse** side and its own
within-model hypotheses. HQ3 (shared failures) and, in comparison scope, the single HQ5 answer none of
them (`RootCauseHypotheses.IsDocumentWide`). Question 1's strengths among its signals include the
gained-points entry only when it names questions.

**The partner a question is answered against** (`RootCauseProfiles.PartnerPreference`, engine version
6) is, nearest first: the same model at another thinking level (the nearest level first), then the
reference, then the fastest model (question 3) or the cheapest (question 4), then the rest in model
order. Questions 3 and 4 order their comparative hypotheses by this partner before rank and take the
first graded Low or higher as their strength; `Keep`'s per-question reservation follows the same order,
so the preferred partner's hypothesis is kept even when another pair's is. Question 3's signals add, when
such a decomposed speed pair exists, the slowest model against the first partner in that order: *"against
{other}: {component} carries {share} of the {x} s difference"*.

The strength is the best **comparative** grade at Low or higher; when there is none it is the best
within-model grade capped at Low, so a question answered only by the model's own breakdown reads
*Suggestive* at most; with neither it is *Unknown*. When question 4's strength comes from a price effect,
`RootCauseQuestion.PriceCard` is true and `rca.question.4.status` displays *Known from the price cards*.
The writer answers each in its answer slot, starting with the reason, and is told when the order is not
established or only the model's own breakdown answers, and, for a price-card question 4, to cite the
model's `price.*.perMillion` cards with no certainty word; code prints the answer's strength above it
with the comparative and within-model hypotheses named apart (*"Answer strength: Probable (Moderate) —
against the other models, see HS1.1; its own breakdown, see WS3.3."*), or *"no hypothesis about this
model fired — see its profile"*. From format 21 (per-model format 16):

- a price-card question 4 prints *"Known from the price cards — see HC2.2."*, naming its first price
  effect graded Low or higher (and its own breakdown after it, as above);
- question 1 with comparative hypotheses none of which is graded Low or higher, or with none at all,
  prints the measured lead instead, naming the runner-up from `Neighbor` and the verdict from
  `.orderEstablished`: *"Measured lead over Vega Pro: not established — see its split; no cause
  compared."*, or *"…; no cause graded Low or higher, see HQ4.1."* when HQ4 or HQ8 was ruled out. A result
  stored without `Neighbor` prints *"Measured lead: …"*.

A result stored before engine 4 prints the single list it was written with.

From format 24 (per-model format 20) the box also prints the question's measured signals under its answer
strength, *"Measured signals: Lost points on low-scoring answers, mostly Accuracy: …"*, resolved from
`rca.question.<n>.signals` as the writer's prose is, so a named and an anonymized copy name the models as
the rest of the document does; it is left out when the fact records none. The writer answers without
citing that token (rule 40): a question no hypothesis answers is answered from the profile in the writer's
own words, citing the model's own facts. A document stored under an earlier format prints no such line, so
an answer that cited the signals is never shown with them twice.

**Model profiles** (`RootCauseProfiles.Build`), `rca.profile.<T>.*`, are code-written from facts already
on the sheet, so they cannot be dropped by a rule and never use a causal word:

- **Gained points on** (`.gainedPointsOn`, engine version 10), the mirror of *Lost points on*, measured
  against the other models rather than the model's own median: with at least
  `BenchmarkComparisonModelProfile.MatrixMinModels` (3) models with per-question means, each question the
  model scored where at least `IsolatedMissMinOthers` (2) other models scored it has the margin *own −
  median(others)*; a question with a margin of `RootCauseProfiles.GainMargin` (10) or more is gained, and
  the points are the gained margins' sum divided by the model's question count, in index points
  (*"answers above the other models' median by 10 or more: +1.8 index points on 4 questions (S2-Q1,
  S1-Q4, S2-Q9 and 1 more)"*, questions by margin, largest first; *"no answer 10 or more above the other
  models' median"* when none is). It is first among the strengths; with fewer than three models it is
  absent;
- **Lost points on** (`.lostPointsOn`): the largest segment of the model's index points at risk —
  answers without tools; budget, tool-limit, truncation or output defects; confirmed critical errors;
  other low-scoring answers — with its points and questions. The last of these is named by the dimension
  that lost more on its answers, Accuracy or Completeness, each weighted by the scoring profile
  (*"low-scoring answers, mostly Accuracy: 2.2 index points on 17 questions"*); when the two tie it
  keeps the plain label. From engine version 10 the dimension is left out (*"low-scoring answers: 2.2
  index points on 17 questions"*) when it is the **shared lowest dimension**,
  `RootCauseResult.SharedLowestDimension`: the dimension with the lowest mean for every model with all four
  dimensions (at least two such models, each with some spread), the test the shared-weakness finding
  applies. A model whose low-scoring answers lose mostly the other dimension keeps it;
- **Weakest dimension** (`.weakestDimension`): the dimension furthest below the model's Accuracy, and the
  gap; and `.lowestRank` when the model is last on a dimension;
- **Strengths** (`.strength.<n>`): the model's dimension ranks when first or second, numbered without the
  gained-points entry, so the keys did not move;
- **Flags** (`.flag.<n>`, joined in `.flags`): critical errors with their questions; answers without
  tools above the median; panel disagreement under the notable findings' rule
  (`BenchmarkNotableFindings.DisagreementConcentration`: at least 3 disagreements on the answers both
  members graded, at twice the **other** models' median rate or more, worded by `DisagreementPhrase`,
  *"panel disagreement on 19% of the answers both members graded, about three times the other models'
  median of 6%"*), which HQ4 uses too; isolated misses (three or more models); output defects and
  tool-limit hits. A flag is a noun phrase, so the writer can cite it inside a sentence.

At most three strengths (`MaxStrengths`: the gained-points entry, then at most two dimension ranks) and
three weaknesses per model; every entry is a measured count, share or rank with its questions.

**Why each model scored as it did** (format 26, per-model format 22) prints the profiles as **one block
per model**, in rank order, because the entries no longer fit a table. A bold lead line gives the name as
the row names it (with its role marks in comparison scope) and the Index cell as *At a glance* prints it
(*"**Orion Max (reference)**: Intelligence 85 (82–88), rank 1–2 (tied)."*, or *Intelligence not
available*); then one run-in-labeled bullet per kind, the text after each label sentence-cased:

- *Strengths:* the dimension ranks, joined by `;`, or *None ranked first or second*;
- *Gained points on:* the entry's text; *"Not measured with fewer than 3 models."* when the document has
  fewer than three profiles;
- *Lost points on:* the weaknesses as the table's cell printed them — *Lost points on* first, then
  *weakest dimension …*, then the last rank — or *No answer below the model's median* first when there is
  no lost-points entry;
- *Against the reference:* comparison scope only, from the `QualitySplits` entry of the model and the
  reference, model minus reference: the signed Index difference, then the dimensions of at least 0.05
  points, largest first (*"-4.1 index points: Accuracy -5.0, Completeness +1.2."*), the critical-error cap
  and difficulty weighting named only when they are the largest part; *The reference.* on the
  reference's own block; left out when the pair has no split;
- *Flags:* as the table's cell, or *None*;
- *Hypotheses:* the kept non-null intelligence hypotheses about the model, in rank order, at most three
  (`RcaMaxModelHypotheses`): a pair hypothesis with the model as its worse side, or a within-model one, a
  document-wide one never; each as the conclusions name it; left out when there is none;
- *Unexpected results:* the references of the unexpected results naming the model on either side,
  *"U1, U2 (Unexpected results)."*; left out when there is none.

The note under the blocks keeps the flag glosses and rules, and adds a sentence on *Gained points on*
where an entry exists, one on *Against the reference* where a non-reference block printed it, and, when
the shared lowest dimension is set, *"Completeness is the weakest dimension of every model (What we
found), so a Lost points entry that would say 'mostly Completeness' leaves it out."* The *Hypothesis* and
*Unexpected result* terms join the glossary when their lines are printed.

### Unexpected results

A comparison-scope analysis looks for results that go against what the models' price tiers or release
dates predict (`RootCauseAnomalies.Find`, engine version 10, run before the pairs are built). Every pair of
analyzed models of **one provider with different model ids** is checked against three priors
(`RootCausePriors`), each predicting an order:

| Prior | Applies when | Predicts |
|---|---|---|
| Price tier, quality (`PriceTierQuality`) | Both list output prices known, the dearer at least `RootCauseEngine.TierPriceRatio` (1.5) times the cheaper | The dearer tier scores at least as high |
| Newer release, quality (`NewerRelease`) | One model line — the model id split on `-` without its version-number segments (`gpt-6-luna` and `gpt-5.6-luna` are both `gpt-luna`, `RootCauseAnomalies.ModelLine`) — at one thinking level, both catalog release dates known and different | The newer release scores at least as high |
| Price tier, speed (`PriceTierSpeed`) | As the first, neither model speed-degraded, both median model times known | The cheaper tier answers at least as fast |

A result is **unexpected** when the reversal is established, or when it reaches
`RootCauseEngine.AnomalyMinPoints` (2.0) Intelligence Index points against a quality prior, or
`AnomalyTimeRatio` (1.25) times the median model time against the speed prior. **Established** reads the
pair's paired test where it has one (`Tested`): established and favoring the model the prior did not
expect. The all-pairs family exists only for 3 to 6 models, so in a larger comparison two non-reference
models have no test; then established means, for quality, that the two 95% intervals do not overlap
(touching bounds overlap, as `OrderEstablished` reads them), and for speed that the bootstrap interval of
the pair's time difference excludes zero. The results are ordered by prior, quality first, then by gap,
and numbered **U1, U2, …** (`RootCauseResult.Anomalies`); several results with one model on the same side
stay separate. `RootCauseResult.PriorsChecked` records which priors at least one pair could be checked
against. Both are null in model scope and in a result stored before engine version 10.

**Release dates** reach the engine in comparison scope only: `PrepareComparisonAsync` and
`PrepareForRequestAsync` take an optional lookup (provider, model id → ISO date), which
`AdminBenchmarkReportPacksController` and the job (`BenchmarkReportPackService`, which now receives a
`ModelMetadataService`) fill from `ModelMetadataService.GetMetadata(...).ReleaseDate`, the model catalog's
date; the preparation maps `provider|modelId` (lower case, `RootCauseEvidence.ReleaseDateKey`) to each
covered entry's known date, through `BenchmarkComparisonReportFacts` into `RootCauseInput.ReleaseDates`.
Without a lookup the release prior is not checked.

**What each result records** (`RootCauseAnomaly`), for the reader to weigh rather than as a cause: the
configuration keys other than the model that differ (`Confounds`); both models' Index under panel member
A, panel member B and the reference reader, and which member splits them further (`Graders`); the two
dimensions with the largest difference, with both values; for a quality prior, the three questions the
other model scored furthest above the expected one, with both scores and the other models' median; for
the speed prior, the leading component of the pair's speed decomposition with its share; the keys of at
most two kept pair hypotheses of the two models on the prior's axis, not ruled out, best first; and what
settles it. **Settled by** (`RootCauseNextRuns`): a result across two thinking levels joins a
thinking-level pair of the model the prior expected ahead at the other model's level, or, with no such
configuration, a *configuration* check naming the result (*"No configured, enabled Benchmark-role
configuration of … exists at … thinking; add one to test U1."*); every other result joins the replicate
with both models and the reference. A thinking-level pair folded into the replicate takes its results along. The run's *Decides* adds *"Shows whether U1 holds with both models at the same thinking
level."* or *"Shows whether U2 and U3 repeat over three runs per model."*, and the result records the run or
the check (`SettledByRun`, `SettledByCheck`).

**Its summary** (`RootCauseAnomalies.Summary`) is one sentence: *"Claude 5.5 Opus (medium) scored 79,
below the cheaper Claude 5.5 Haiku (xhigh) at 81; not established, the intervals overlap."*, *older* for
the release prior, *"… was slower than the dearer …: a median model time of 38.7 s against 23.3 s; …"*
for speed. It ends in one of six ways: *established by the paired test* or *not established by the
paired test* (tested); *established, the intervals do not overlap* or, for speed, *established, the 95%
interval of the time difference excludes zero* (untested and established); *not established, the
intervals overlap*; or *not established, and the pair had no paired test* when no interval comparison
applies.

**Facts** (comparison scope): `rca.anomaly.count`, `rca.anomaly.priorsChecked` (the priors checked, in
words, or unavailable with why: *"No two models share a provider."*, or that no two models of one provider
have list prices in different tiers or release dates within one line), `rca.anomaly.untestedPairs`, and per
result `rca.anomaly.<n>.ref` (a noun phrase), `.summary` (sentence-shaped), `.prior`, `.confounds`,
`.graders`, `.settledBy` (*"next run 1"*, *"check 2"*) and `.tested`. Each has a label; the glossary term
*Unexpected result* reads *"A result that goes against what the models' price tiers or release dates
predict."*

### The control limit

Only other models' scores on the same questions separate the model from the instrument. A within-model
intelligence hypothesis whose locus could be `suite`, `corpus` or `chat` as well as `model` — WQ1, WQ2
and WQ4 — is graded with the controls, and capped by how many there are
(`RootCauseControls.Limit`):

| Controls found | Cap | Grade reason |
|---|---|---|
| 2 or more, at least one from another provider | none (graded as above) | — |
| 1, or 2 or more all from the subject's provider | *Moderate* | "one control, or controls from one provider only" |
| 0 | *Low* | "no other model answered these questions" |

In a run- or battery-completion document the controls are those found below; in a per-model pack the
subject's peers count as its controls, and in a comparison the other covered models. Each capped
hypothesis also lists, for the model, the questions the controls answered well that it missed, and,
against it, the questions every control missed too. With fewer than two qualifying controls the analysis
proposes a `control` run. **Speed and cost hypotheses are never capped by controls**: they decompose the
model's own time and cost, measured exactly. The limit is the fact `rca.controls.limit`.

### Controls for a run or a battery run

**What a control is.** An earlier, finished run of **another model** that answered the **same exam under
the same instrument**, so that its per-question scores tell how hard each question was. Only the
candidate differs.

**Selection** (`RootCauseControls.FindAsync`, at preparation time; the newest 60 candidates are examined):

| Subject | Candidate controls | Qualifies when |
|---|---|---|
| A single run | Completed runs (Completed, CompletedWithLimits, CompletedWithErrors) on the same suite, newest first | `BenchmarkPairedTests.ClassifyRunPair(subject, candidate, repriced: false).Kind == ModelComparison`, on the same item revisions. Every must-match key agrees: item revisions, assessed difficulties, board, harness and scoring method, profile, graders, prompt options. Only model-axis keys differ |
| A battery run | Finished battery runs of the same battery revision with a current, complete analysis | `BenchmarkCrossModelComparability.Resolve` over the subject and the candidate gives the candidate a state other than *Excluded* — the same comparability class, built from each suite's `MustMatchSignature` — and the model axis differs |

In both cases the candidate is a different `ModelId`, or the same model at another thinking level, which
counts as a different model; its own index is not withheld (the `WithheldRunIds` rule of the model
comparison service); and it shares at least **5** scored questions with the subject. Up to **three**
controls are kept, one per model: first the best candidate of each provider other than the subject's,
then the best remaining models, *best* meaning the most recent, then the most shared questions. Speed-
and cost-degraded candidates are allowed, because controls serve intelligence only. With none, the
reason is *"No earlier run of another model answered these questions under the same graders, scoring
and prompt options."* (a battery: *"No earlier battery run of another model ran this battery revision
with a current, complete analysis in the same comparability class."*).

**What the controls feed** — intelligence only:

- **Item-level difficulty.** For each shared question, the controls' mean score. The subject's deficit
  is its score minus that mean, with a paired bootstrap interval over questions:
  `rca.control.deficit.mean` and `.interval`. Section 2 prints the item-level control table: per
  question, the subject's score, the controls' mean and the difference.
- **Shared failure.** A question the subject scored below 50 **and** every control did too is marked
  *instrument-suspect*; HQ3's rule then applies in model scope, with locus `suite`, `corpus` or `chat`,
  and adds a check.
- **Model-specific failure.** A question the subject scored below 50 while every control scored 75 or
  above is marked *model-specific*, evidence for the `model` locus of WQ1, WQ2 and WQ4.
- **The control limit** above. Two or more qualifying controls, one from another provider, lift it.

**Recorded.** The facts `rca.controls.count`, `rca.controls.providers`, `rca.controls.limit` and
`rca.controls.<n>.model` (the model with its thinking level once), `.runId` (or `.batteryRunId`), `.date`,
`.provider`, `.sharedQuestions`, and
`rca.control.instrumentSuspect` and `.modelSpecific`; the controls themselves go into `RootCauseResult`.
Controls are **named** in every copy: they are evidence, not peers of the document. Speed and cost are
never read from a control, because those keys may differ (time of day, service tier).

**Never blocking.** Writing never waits for a control, and a document with none is complete: its
intelligence status usually reads *Not established*, with the reason, and the `control` run is
proposed.

**The UI** (`root-cause-controls`, in the run's and the battery run's **AI Reports** tab, §§ 11, 14):

- **Before writing**, a **Controls** block from `GET …/runs/{id}/root-cause-controls` or
  `GET …/batteries/runs/{id}/root-cause-controls` (§ 9), the same `FindAsync` the preparation calls, so
  it names exactly the controls the document will use: *"The Root Cause Analysis will use 2 controls:
  GPT-6 Luna (high), run #812 (2026-10-10); Claude 5.5 Sonnet (medium), run #807 (2026-10-09)."*, then
  the cap it leaves (*"With these controls, an intelligence cause can be graded at most Moderate."*, or
  *"Without a qualifying control, an intelligence cause can be graded at most Low."*). With none: *"No
  earlier run of another model answered these questions under the same graders. Speed and cost causes
  will be analyzed; intelligence causes will stay undetermined."* While fewer than two controls qualify
  and the endpoint returns a `controlRun`, **Set up a control run** pre-fills the launcher with that run
  through the same prefill path as **Set up this run**, with the note heading *"Control run for run
  #<id>"*; without one it is unavailable, with the reason *"No control run can be proposed: no other
  provider has a recommended model with an enabled Benchmark configuration."*
- **After writing**, the block reads *"This Root Cause Analysis used 2 controls: …"* (or that it used
  none), from the document detail's `controls`, and compares them with a fresh `root-cause-controls`
  call. When the fresh set holds a control the document did not use, it says *"New controls available
  since this document was written (run #…)."* with **Rewrite with controls**, which confirms the
  estimate in the writing dialog and then writes a new Root Cause Analysis for the same subject with
  `replaceDocumentIds` naming the old one (§ 9). The new document replaces the old one only once it is
  stored (§ 15, *Rewriting*; `ReplacedDocumentsAsync` matches a run- or battery-completion document of
  the same subject key and audience); the Executive Summary is untouched. Both actions are unavailable
  while the subject's documents are being written.
- **For several models run together**, the tab notes *"For several models, Model Comparison → Report
  Pack gives a fuller analysis."* A model batch's members are controls for each other, but a comparison
  sets them side by side.

**Not automatic.** A rewrite is never started by Overseer itself: it spends money, and only the
developer decides when a document is worth redoing. A later run never changes a stored document; it
only makes **Rewrite with controls** appear.

### Next runs and checks

`RootCauseNextRuns` proposes a run **only for a hypothesis below High that a run can settle**. A
hypothesis a run cannot settle — every locus `suite` or `corpus`, or a shared failure — becomes a
**check** instead (`rca.check.<n>`, kind `rubric`, `corpus`, `configuration` or `graders`): *"check the
rubric of S1-Q12 against the source"*. So does a run that cannot be set up: a thinking pair with no
configured model at another level, a panel swap without two configured graders of other providers, or a
control run with no configured recommended model of another provider, each with what to add. A
`replicate` is proposed only where it can settle something: a pair whose `H0` stands, or a hypothesis
whose *Replicates* check failed. **A null that stands** (any `H0` not rejected, `H0-W` included) joins
the replicate with its models, confirming *"if the paired test still finds no difference with three runs
per model"* (engine version 5), so no analyzed model is left out of it. A hypothesis graded *Not
supported* proposes nothing. An unexpected result (engine version 10) joins a thinking-level pair, a
configuration check or the replicate, as *Unexpected results* above says.

**Every check has a noun phrase** (engine version 7): `RootCauseCheck.Summary`, the fact
`rca.check.<n>.summary`, written *"Check N, <noun phrase>"* in the shape of a run's summary — *"Check 1,
a panel swap with two graders of other providers"*, *"Check 2, a comparison of the two models' price
cards"*. `rca.check.<n>` is a whole sentence the document prints in its *Checks* list; the writer cites a
check inside its own sentence by the summary (it is not sentence-shaped, rule 31 (e)), and never repeats
`{{rca.check.<n>}}` under *What to run next*. The prompt's data block prints *"settled by:
{{rca.check.N.summary}}"*. The *Checks* list and the *Settled by* column are unchanged.

| Kind | Fires for | Run | Purpose |
|---|---|---|---|
| `replicate` | Any graded pair limited by a single run per suite, or an `H0` that stands | Model batch, same target (suite or battery), *Runs per Suite* / *Runs per model* 3, the hypothesis' models plus the reference (or the subject) | M; B when every hypothesis it settles points first to the `harness` |
| `thinkingPair` | HQ8, HS3, HC3, WS2, only for a hypothesis about **one** model id (a hypothesis comparing two different models takes the replicate, or nothing) | Model batch of the same model at two thinking levels: an **analyzed** sibling first (same model id, another level, nearest rank), else the model's configuration and the nearest configured, enabled Benchmark-role configuration of the same model id at another level | M |
| `stylePair` | HQ5 | **At most one per document**: one single run of the reference model at the other *Response Style* (*Detailed* after *Concise*, or the reverse), everything else unchanged, serving every HQ5 (in comparison scope the one HQ5.1); always **Optional** (an isolated pair showed no style effect, and the controlled verbose pair was declined on 2026-09-27) | C |
| `panelSwap` | HQ4, HQ9, WQ6 | The same models graded by a panel from two other providers than the analyzed graders' (configured graders only) | B |
| `timeStratum` | HS2, HS4, HS5, HS6, WS1, WS5 | The model's run repeated, *Number of Runs* 3, each started at a different time of day (the run's note says so) | C |
| `control` | A run- or battery-completion document with fewer than 2 qualifying controls; a per-model pack whose subject has fewer than 2 comparable peers | Model batch, same target and graders, **without the subject** (its run already exists): the currently recommended model (`RecommendedModels` in `Overseer/appsettings.json`) of each other provider that has a configured, enabled Benchmark-role configuration, up to the controls needed. A finished run of it becomes a control automatically | B |

**Folding.** After the plans are merged by key, a `timeStratum` or `thinkingPair` plan whose **analyzed**
models all belong to a `replicate` of three or more runs per suite is folded into it
(`RootCauseNextRuns.Fold`); a model the plan adds that has no analyzed runs is appended to the replicate.
The replicate takes over its hypotheses, gains the instruction *"start each run at a different time of
day"* (`When`) when a time-of-day repeat was folded, and takes the most urgent purpose among them (C
before B before M). Three passes started at different times of day settle what the separate repeats
would, for a fraction of the cost: in Comparison #4's analysis under engine version 1, the nine-model
replicate covered the seven other proposed runs (eight in all, about $450).

A run is **Recommended** when it is a `control` run or settles an axis' best hypothesis, else
**Optional**; a `stylePair` is always Optional. Runs are ordered **C, then B, then M** (the chat, then the benchmark, then model selection,
as `server_benchmark_runbook` § 4 orders a round's runs); with the *system first* flag set, every M run
is marked *after the system hypotheses are settled* and its notes say so. *What to run next* prints *"A
harness or suite hypothesis is graded Moderate or higher, so the model-selection runs wait …"* only when
some run is so marked, and the writer's NEXT RUNS block says *"Model selection waits: …"* under the same
condition, and nothing otherwise. Each run's purpose is the fact `rca.run.<n>.purpose` (*"C, the Overseer
chat"*, worded by `RootCausePurposes.Text` as the run table's Purpose column is); NEXT RUNS ends with
*"Write them in this order: {{rca.run.1.summary}}, {{rca.run.2.summary}}; a check comes before a run only
when that run is a model-selection run."*, and rule 42 checks the writer's `nextRuns` against it.

Every run is a structured `RootCauseRecommendedRun`, stored in the document's `FactsJson`, its summary
the fact `rca.run.<n>.summary` — a short line, *"Run 1, a replicate of all nine models (about $260 and
528 minutes)"*, the card holding the rest — and returned on the document detail as `recommendedRuns`:

- `Number`, `Kind`, `Purpose` (C, B or M), `Mark` and `HypothesisIds`;
- `Decides`: one sentence per group of hypotheses with the same confirm and refute conditions,
  *"Confirms HQ2.1, HQ2.2 and HQ2.3 if …; refutes each if …"*, stated before the run; from engine
  version 5 also `DecidesItems`, the same sentences as a list, which Appendix D prints as nested bullets
  (a stored run without it prints the joined `Decides`);
- `PredictedTier`, from the keys the run changes;
- `EstimatedCostUsd` and `EstimatedMinutes`, from the analyzed runs' own costs and durations (a model
  without analyzed runs takes their mean, and a note says so);
- every launcher setting, named as `StartBenchmarkRunRequest` and `StartBenchmarkModelBatchRequest` name
  them so the prefill maps them one to one: `TargetKind` (*Suite* or *Battery*), `SuiteId` / `SuiteName`,
  `BatteryId` / `BatteryName`, `RunMode` (*Single* or *Batch*), `TestedModelConfigurationIds` and
  `ModelNames`, `Order`, `RunsPerModel`, `RunCount`, `ScoringProfileId`, `VerboseMode`,
  `AllowSourceCodeReferences`, and the grader configuration ids — assessor, co-assessor, reference or
  second reader, claim verifier — with the coverage mode, and from engine version 5 their names
  (`ScoringProfileName`, `AssessorName`, `CoAssessorName`, `ReferenceReaderName`, `ClaimVerifierName`,
  from the snapshots' labels), which Appendix D prints beside the ids and an anonymized copy withholds.
  All are copied from the analyzed runs unless
  the kind changes them; a setting the runs did not share is left null with a note
  (*"The analyzed runs differ on …, so it is left for you to choose."*). `ReportWriterModelConfigurationId`
  is always null: a recommended run never writes documents of its own.

### Set up this run

The recommended runs and checks of a written Root Cause Analysis are listed by `root-cause-runs`, a
closed *Next runs and checks (2 runs, 8 checks)* disclosure under the document's row in the comparison's
*Documents of this comparison* (§ 12) and the run's and the battery run's **AI Reports** tab (§§ 11, 14),
and under the latest Root Cause Analysis in the launcher's **Comparison reports** (§ 12). It reads the
document detail's `recommendedRuns` and `checks`. Each run is a card: *Run 2: Thinking-level pair*, tags
for its purpose, its mark and the hypotheses it settles, its decision rule, *"Make this run after the
system hypotheses are settled."* where that applies, its predicted tier, estimate and settings, its notes,
and **Set up this run**; then the checks (*"No check by hand is needed."* with none).

**Set up this run** calls the shell bridge's `prefillSetup(setup, sourceLabel)`; the shell closes the
comparison wizard, the run report dialog or the battery dialog first (as *Repeat this run's setup*
does), and the launcher's `prefillFromSetup` fills Run Benchmark, in the style of *Repeat this run's
setup*:

- every non-null setting is applied; a suite, battery, scoring profile or model that no longer resolves
  keeps the current choice, or is left out, with a note naming it;
- the **graders are applied as a set**, as the analyzed runs had them: a null grader role is set to
  none, and a null coverage follows the profile; a grader that no longer qualifies keeps the current
  choice with a note, and a run naming no assessor says to choose one. When the server's notes say the
  analyzed runs differ on a grader field, no grader is applied at all and the note stands;
- a runs field set to 3 says so (*"Runs per Suite was set to 3 for this recommendation."*), and one held
  below the request by its limit says what it was set to; a time-stratum run adds that each run starts at
  a different time of day;
- the report writer is cleared, and the server's own notes follow;
- the note heading reads *"Set up from Root Cause Analysis #<id>, run <k>"* (*"Control run for run
  #<id>"* from **Set up a control run**).

**It never starts a run.** While the active sub-tab cannot be left, or outside the GnollBench tab, the
button is refused with its reason on the card.

### The document

Disclosure **Full** only, named and, where peers exist, anonymized. Stamp *"INTERNAL — contains
benchmark questions and rubrics. Do not share outside the Overseer team."* Title *Root Cause Analysis*
(*Comparison #12 — <name>: Root Cause Analysis*, printed as the kicker *ROOT CAUSE ANALYSIS* over the H1
*Comparison #12 — <name>* (§ 15), or *<model> on the Overseer GnollHack Assistant Benchmark — Root Cause
Analysis* in model scope); file kind slug `root-cause-analysis`. The `##`
headings are unnumbered. The document is **answer-first**: the key finding and what code found, then a
per-model explanation, then the evidence, with the detail in lettered appendices, so the body of a
nine-model comparison is about a fifth of what it was. The renderer
(`BenchmarkReportPackRenderer.RootCause.cs`) prints, in order, each section's code-rendered blocks, then
the section's server-drawn figure where it has one, then the writer's slot; key findings, caveats and
conclusions are callouts (§ 7). The comparison form (`ComparisonRootCauseAnalysis`; golden
`comparison_rca_full_named.md`), from format 16:

| Section | Code-rendered | Writer slot (word cap) |
|---|---|---|
| *Summary* | `### What we found`: the headline in a *Key finding* callout, then, from format 25, up to five findings code ranks, seven from format 26 (below). Up to format 24, `### The questions`: each question asked, in a *Question* callout with the question in bold and *Answer strength: Probable (Moderate) — see HQ2.1, HS2.1*. `### At a glance`: from format 26 the figures alone, *Model · Intelligence (index, interval, rank) · Median time · Cost per question*, and one line under it, *"Why each model scored as it did gives, for each model, its strengths, where it gained and lost points, its difference from the reference by dimension, its flags and the hypotheses about it."* (up to format 25 also *Where it lost most points · Flags*). `### How sure are we?`: a *Caveat* callout with the grade words in plain order (*"How sure each answer is. **Established (High)**: measured, still clear after the correction for many comparisons, …"*) and the run-level cap stated once (*"Each model ran only once per suite, so this document cannot tell whether its results would repeat. No cause can be graded above Probable (Moderate) until a second run of each suite is made."*) | `headline` (35): in one plain sentence, what most explains the differences between the models, naming at most two causes; up to format 24 also `answerBest`, `answerWorst`, `answerSpeed`, `answerCost` (60 each), in their question's callout |
| *Key terms* | Format 18: a two-sentence lead, then *Term · What it means*, one row per term the document actually uses, alphabetical, with the term's short gloss (`RootCause/RootCauseGlossary.cs`, the one term registry). Each less familiar term is also glossed in parentheses where template text first uses it (the render `Context`'s `GlossedTerms`), for example *Holm-adjusted across 4 tests (a correction for running many comparisons at once)* | — |
| *Why each model scored as it did* | From format 26, one block per model in rank order — the Index lead line, then *Strengths*, *Gained points on*, *Lost points on*, *Against the reference*, *Flags*, *Hypotheses* and *Unexpected results* (§ 17, *The research questions and the model profiles*) — with a note that every entry is measured and none says why; up to format 25 the table *Model · Index · Strengths · Lost points on · Flags* | — |
| *Unexpected results* | Format 26, comparison scope: one block per result, its summary in bold with its reference (*"**U1. Claude 5.5 Opus (medium) scored 79, …**"*), then *Configuration* (or *"none: only the model differs"*), *Graders*, *Dimensions*, *Where* (quality: each question with both scores and the other models' median in parentheses) or *Time* (speed: the leading component and its share, Appendix B), *Hypotheses* and *Settled by* (*Run 1* or *Check 2*, What to run next), each where it has content; then, when any pair had no paired test, *"N of these pairs had no paired test: the paired tests cover the reference pairs, and every pair only up to 6 models (Appendix A). Established here means the two 95% intervals do not overlap."* With none: *"No unexpected results: every ordering the priors predict either holds or is within noise. Checked: <priors>."*, or *"No unexpected results could be looked for: no two models share a provider."* (or the other reason `rca.anomaly.priorsChecked` gives). Left out of a model-scope document and of a result stored before engine version 10 | `unexpectedResults` (80), **optional**, under the section: what the grader comments and excerpts show about each result, citing its `rca.anomaly.<n>.ref` and at least one question; no causal word (rule 32); asked for only when the sheet has an unexpected result |
| *Scope and design* | `### Setup and method`, the grader roster, the availability table (one sentence when every model's is unrecorded); the compared models and configuration differences move to Appendix A | `scope` (120) |
| *Evidence: intelligence* | A five-column scoreboard (*Model · Intelligence Index · 95% interval · Rank · Critical errors*, a tied rank as its span, such as *5–9 (tied)*), the dimensions table, figure R1, `### Paired tests against <reference>` with the adjustment family stated once under it, `### What the graders recorded` (the `observations`), one grouped response-style line | `evidenceIntelligence` (100), no causal words: what stands out — a pattern several models share, a model that departs from the others, or a result the settings or the price cards would not predict — and, from format 26, no sentence on what it means for choosing a model or a deployment (up to format 25: *"and why it matters for choosing a model"*) |
| *Evidence: speed* | `### Where the time goes` and figure R2, the paired tests, `### Time differences between the models`: one line per decomposed pair (its leading component in seconds and its share with interval), the pairs not decomposed in one line | `evidenceSpeed` (100) |
| *Evidence: cost* | The same, in dollars (figure R3). From format 26 the paired cost tests read *Pair · Spend ratio (95% interval) · At equal prices · Result*: *At equal prices* is the decomposed pair's `VolumeRatio`, oriented to the row, to two decimals (*—* when not decomposed), and *Result* prints *may be chance* when the test is not established, *not tested* when it was not made, and nothing when established; the note adds *"Spend ratios include the price cards. At equal prices is the token-volume ratio, the part the price cards do not set."* Under *Cost differences between the models* the pairs the price card leads (its all-components effect the largest) are one line, *"Mostly the price cards: A vs B; C vs D."*, and every other pair names its leading component that is not a price effect | `evidenceCost` (100): what the price cards do not explain — token volume per question, the share of thinking, caching and model calls per answer — never that a dearer model costs more, nor a spend ratio or a price-per-token difference; a pair's token volume is cited by its `rca.cost.<F>.<S>.volumeRatio` |
| *Hypotheses* | Per axis a table of the kept non-null hypotheses that are not price effects — *Hypothesis (id and label) · Statement · Grade · Share · Settled by* — then, from format 26, one line for the price effects (*"5 cost differences follow mainly from the price cards (HC2.1–HC2.5); Appendix C lists them."*, standing in for the table when nothing else fired) and the axis' null hypotheses in **one sentence** (`RcaNullsSentence`): *"Every tested difference is real (H0-C.1–H0-C.10)."*, *"No tested difference could be shown to be real; each may be chance."*, or *"3 differences may be chance: A vs B; …; the other 7 are real."*, naming at most five (`RcaNullPairsMax`) and then *"and N more (Appendix C)"*; the id legend under the first table (H0 null, H between two models, W within one; Q, S, C), figure R4; the writer's hypotheses in a *Not tested by code* caveat callout | `hypothesesIntelligence`, `hypothesesSpeed`, `hypothesesCost` (120 each), each after its axis' table: the grade word only, never a price-card difference, and never a restatement of the nulls (rule 45) |
| *Conclusions* | The table *Axis · Status · Best hypothesis · Why*. From format 26 it has no price note; an axis whose best hypothesis is a price effect prints *—* as its best hypothesis; and when two or more axes all have the same reason and it contains *because*, each *Why* cell keeps the part before it (the grade) and one sentence under the table states the reason once (*"Each grade is capped at Probable (Moderate) because each model ran only once per suite."*), the reasons compared by code, never hard-coded | `conclusions` (120), in a *Conclusion* callout; never mentions price per token or the price cards, and says, when the cost axis has nothing beyond them, that the runs show no cost mechanism beyond the price cards |
| *What to run next* | The order (C, B, M), a priority table *# · Run · Purpose · Models · Settles (hypothesis ids, at most eight, then "and N more (Appendix D)") · Estimate*, then `### Checks` | `nextRuns` (50): which run to make first and what it settles |
| *Limitations and threats to validity* | One merged, deduplicated list; the availability threats grouped by reason (*"Provider availability is not recorded for any of the 9 models (runs before harness 56), …"*) | `limitations` (120) |
| *Appendices* | The list of A–H, then **A** Intelligence detail (compared models, configuration differences, failure profile, grader findings, panel agreement, points at risk, the index difference by dimension, every paired test, the per-question matrix); **B** Speed and cost decompositions (decomposed pairs only); **C** Hypothesis details (*For*, *Against*, *Grade reasons*, *Refuted if*, *Robustness*, *Settled by* per hypothesis; the nulls as one table *Hypothesis · Pair · Paired result · Grade*, the grade read as *Stands — may be chance* or *Rejected — a real difference*); **D** Run settings (each run's *Launcher field · Setting* table); **E** Known facts; **F** Reproducibility; **G** Question details (every claim-verifier ruling that is not a supported answer sentence, then *"N further answer sentences supported."*; a grader evidence line that only says *&lt;Dimension&gt;: Matches rubric.* is left out). Format 26 shortens A, B, C, F and G (*Appendices from format 26*, below); **H** Glossary: from format 18 every term the document uses, alphabetical, with its full definition from `RootCauseGlossary` (before, nine fixed bullets) | `questionTopics` for every question cited whose text the writer was given |
| *Fact sheet*, *Removed content*, *Evaluation terms* | As before (the fact sheet in the Markdown copy only, with no section at all in the PDF and Word); *Removed content* names each removal's location in words and its rule, and at Full disclosure prints the removed text with its tokens resolved under the document's peer naming (withheld names, ids and providers as letters in an anonymized copy) | — |

**From format 20** (per-model format 15) the table above changes as follows (format 25 replaces the
*What we found* bullets and moves the *Grader family* sentence; see below). *What we found* adds up to
four code-built bullets under the Key finding: the models the paired tests establish above or below the
reference; the cheapest model and how many models its intelligence interval overlaps; the weakest
dimension when it is the same for at least two thirds of the models (with how many are at least 13 points
below Accuracy, `BenchmarkChatTransfer.HasResponseStyleConflict`); and the models that answered 20 % or
more of their scored questions without a tool call. *How sure are we?* adds a *Grader family* sentence when
the members' provider pair has an interaction contrast whose interval excludes zero (§ 17, *Evidence and
known facts*), and prints the single-run caveat only when no grade cap says it. *Key terms* lists only the
terms the Summary and Conclusions use (Appendix H keeps every term), and the renderer removes a gloss
already printed (`Prose`, the RCA's `TracksTerms`). The profile flags are compact (*"panel disagreement
19% (others' median 6%)"*), with the no-tool median and the isolated-miss rule stated once in the note.
The tied-rank notes describe the per-model span and add that interval bounds are rounded while ranks use
the unrounded bounds. Paired tests list the reference second. *Where the time goes* heads its total
*Wall time per question (median, tools included)* and says it differs from the median model time.
A column, or a decomposition row, that is zero or not recorded in every row is dropped
(`DropConstantColumns`) with one sentence naming it; a share above 100 % says, on its first occurrence,
that another component pulls the other way. The hypothesis tables print the id alone. Appendix A has no
*Results* block in comparison scope and prints only the all-pairs family the body did not print, or a
pointer sentence; Appendix D prints *Decides* as bullets and the grader and profile names beside their ids
(withheld in an anonymized copy); Appendix E lists only the `count` and `configuration` facts, keeping
their numbers; Appendix G prints only the questions the writer cited, the disputed and critical-error
questions and the control items, each answer as the excerpt the writer was given, rubrics without
their `SOURCE` paragraphs, and each flagged answer's *Critical error:* resolution. The comparison RCA
places no browser *Intelligence* figure at *Evidence: intelligence* (server and client placement). A
stored document missing a new fact or field renders with the wording it had.

**From format 21** (per-model format 16, engine version 6):

- **Appendix E** is left out, heading and list bullet, when it has no count or configuration fact to list,
  and the appendices after it close up: Reproducibility becomes E, Question details F and the Glossary G,
  the titles, the appendix list and the *Key terms* lead's pointer to the glossary all computed
  (`RcaAppendixLetters`). A document with no analysis at all keeps E for its note. With the known-facts
  quota above it is rarely empty.
- **Appendix G** prints the questions the writer cited, the **critical-error** questions and the control
  items, and no longer those with only a refuted answer sentence or a disputed claim. A question is a
  critical-error question when one of its cells records a critical error (`CriticalError`, or
  `CriticalErrorCount` above zero, the only signal under scoring method 12) or a resolution other than
  `None` — confirmed (`Agreed`, `UpheldByVerifier`) or overturned (`OverturnedByVerifier`) — in
  `BenchmarkReportQuestionModelScore.CriticalErrorResolutions`, or when a content answer stores one. The
  rubrics are printed without their `SOURCE` paragraphs, plain or bold (§ 7). The G bullet reads *"the
  questions the writer cites, those with a critical error, confirmed or overturned, and the control
  items"*.
- **Critical-error resolutions per cell.** `BenchmarkComparisonReportFacts.Build` fills each matrix cell's
  `CriticalErrorResolutions` (scoring method 13 or later) from every answer of the model's runs on the
  question, not only the excerpted one: one `BenchmarkCriticalErrorResolution` name per answer, `None`
  included, in run and answer order, null where no answer records one (every method-12 run and every
  sheet stored before). In comparison scope the *Critical error:* line reads them, drops `None`,
  deduplicates per model and joins the models, so an answer with no excerpt is named too; a cell without
  the field falls back to the content answers.
- **One output column.** When any row's provider counts thinking inside its output tokens, the cost table
  and Appendix B's tokens table head their visible-output column *Output*, and a merged row prints its
  output with thinking included there, marked `*`, rather than in a column of its own (the *Reasoning*
  column stays for the providers that report it, and is dropped when no row does); and the note reads *"\* Thinking included; the provider does not
  report it separately."* A trailing `*` does not stop a column from being dropped as zero
  (`IsZeroLikeCell`).
- **Evidence labels name the models.** An `rca.profile.<T>.*` fact cited as evidence prints as
  *"{model}: {display}"*, and fact labels resolve letters to names in the named copy (letters stay in the
  anonymized one). The profile labels read *"Model E's profile: flag 1"*.

**From format 25 (per-model format 21)** the Summary is *What we found*, *At a glance* and *How sure are
we?*: there is no *The questions* box, and the writer output has no answer slots. A document stored under
formats 16–24 (per-model 15–20) renders the same way: its stored answers stay in its writer output but are
not shown, and a question cited only in one of them is not among Appendix G's cited questions
(`BenchmarkReportPackValidator.CitedQuestions` skips `RootCauseAnswerSlots`). Under the Key finding,
*What we found* prints the findings code ranks (`RcaFindings`), in the project's order of priority and at
most seven (`RcaMaxFindings`; five up to format 25), each only when its gate passes, so none restates an
extreme the *At a glance* table shows. Items 4 and 7 are new in format 26:

1. **Answers without tools.** A model that answered at least 20 % of its scored answers without a tool
   call and, with two or more models, at least twice the other models' median share, with the most any
   other model did (*"Claude 5.5 Sonnet (medium) answered 9 of 36 scored answers (25%) without any tool
   call; no other model did so more than 3 times."*); one shared sentence when every model is at or above
   20 %; a model on its own needs the 20 % only.
2. **A weakness every model shares.** The dimension lowest for every model with four dimensions, or for
   at least two thirds of them where the response-style rule (`BenchmarkChatTransfer.HasResponseStyleConflict`)
   marks at least two thirds of those, with its gaps below Accuracy, the concise response style named
   when the rule marks them and the sheet's response-style facts allow it, and *"a pattern this wide does
   not tell the models apart"*. The lowest-dimension sentence under *Evidence: intelligence* is unchanged.
3. **Grader family.** The sentence that was in *How sure are we?*, whose caveat now ends *"The order
   between models of those two providers depends on the panel's balance; see What we found."*
4. **Unexpected results** (comparison scope). One bullet when there is any (§ 17, *Unexpected results*),
   the results of one prior with the same model expected ahead in one clause, their references as ranges:
   *"Unexpected: Claude 5.5 Opus (medium) scored below the cheaper Claude 5.5 Haiku (xhigh) (U1); Claude
   5.5 Haiku (xhigh) was slower than the dearer Claude 5.5 Opus (medium), Claude 5.5 Sonnet (high) and
   Claude 5.5 Sonnet (medium) (U2–U4). See Unexpected results."* (*below the older* for the release
   prior).
5. **Value for money.** With three or more priced models (a degraded cost is not priced) and one uniquely
   cheapest, the other priced models split against it into those established to score higher, those
   established lower and the rest, by count, with **no cost multiple** from format 26, since the price
   cards give them: *"GPT-6 Luna (high) costs the least per question. Only GPT-6 Astra (medium) is
   established to score higher, and Claude 5.5 Sonnet (medium) scores lower; for the other 6 models the
   order is not established."* The cost ratio still gates it: printed when a model is established higher
   or one of the rest costs at least 3 times as much.
6. **Thinking levels.** One provider's model id at two thinking levels
   (`BenchmarkNotableFindings.SameModel`, `CompareLevels`) where the higher level is not established above
   the lower and takes or costs at least 1.5 times as much; at most two pairs, the largest time ratio
   first, from format 26 as clauses of **one bullet** joined by `;`. A ratio is the pair's paired time or
   cost test where one was made (the all-pairs family first, then the reference family,
   `RcaPairedRatio`), else the ratio of the median times or the mean costs. It takes precedence over a
   *Thinking level inversion* notable finding on the same pair.
7. **Token volume.** With three or more models with the figure, each model whose input tokens per question
   are at least `RcaVolumeContrast` (2.0) times the other models' median, with its model calls per answer
   against theirs, or, where only its output tokens (reasoning included) are, those; at most two models,
   the largest multiple first, in one bullet: *"Claude 5.5 Haiku (xhigh) read 231,442 input tokens per
   question, 2.4 times the other models' median, at 5.6 model calls per answer against their median of
   3.4."* The gate is high because providers count tokens differently.
8. **Notable findings** (comparison scope): the sheet's stored ones, in their order, at most two, printed
   as *Also worth knowing* prints them, leaving out grader-disagreement concentration, judge-dependent
   order and answer length, a *Cheaper and better* finding about the cheapest model and a *Thinking level
   inversion* on a pair the thinking-level bullet printed.
9. **Differences from the reference** (comparison scope): what the paired tests establish against the
   reference, the models the 95 % intervals separate from it, and the other differences, which may be
   chance; printed only when one of those is at least 3 index points, and not when the value-for-money
   bullet printed with the reference as the cheapest model.

"Established" in 5 and 6 is `RcaQualityVerdict`, read as the notable findings and the tied ranks read it:
a paired intelligence test that establishes the order (all-pairs family first, then the reference family),
otherwise Intelligence Index intervals that do not overlap. A ratio prints with one decimal below 10
(*2.3 times*) and whole from 10 (*67 times*) (`RcaTimes`); models are named as the copy names them.

The Key finding (the `headline`) names at most two causes, the most consequential first, never restates
a ranking the *At a glance* table shows, never mentions price per token or the price cards, and does not
repeat what code prints under it, the unexpected results included. *Key terms* keeps the *Hypothesis*
row: the Conclusions register it.

**Appendices from format 26** (per-model format 22) print what they share once:

- **A.** *Configuration differences* leaves out each pair's `pair.<L>.<M>.configDifferences` line that a
  listed configuration fact of Appendix E rests on, and adds one line, *"The configuration differences
  between the analyzed pairs are Appendix E's known facts 3–5."*; the *Open confounds of …* lines stay.
  In comparison scope *Grader findings by category* (`FindingsTable(onlyDifferingRows: true)`) leaves out
  each model whose count in every category equals that column's most common value (held by at least two
  models and by no other value as often), names them under the table (*"Left out, with the most common
  count in every category: …"*), and prints *"Every model had the most common count in every category."*
  instead of the table when none is left.
- **B.** *Speed differences, decomposed* and *Cost differences, decomposed* are **one row per decomposed
  pair**, each cell a share with its 95% interval: speed *Pair · Questions · Difference ·
  Model calls · Time to first token · Output tokens · Decode rate · Other* (*Other* is the time outside
  the model calls), cost *Pair · Questions · Difference · Token volume · Price card · Caching · Leading
  component*, the first three splitting the whole difference (*Caching* only for a grouped pair, *—*
  otherwise) and the last the largest component with its share. A column zero or missing in every row is
  dropped with one sentence (`DropConstantColumns`). The calls, latency and tokens tables stay.
- **C.** A rule that fired for several pairs on an axis is **one entry**: its keys as a range, the pair
  count and locus, and its statement as a pattern (*"The first model costs more than the second model
  mainly from its price per token."*); the fields every partner shares (*For*, *Against*, *Grade
  reasons*, *Refuted if*, *Robustness*, *Open confounds*) once; a table *Hypothesis · Pair · Share (95%
  interval) · Grade · Settled by*; then, per partner, only the fields that differ. A hypothesis alone, a
  within-model or document-wide one, or one whose statement does not name both models prints as before.
  Price effects are listed here.
- **F.** In comparison scope a hash prefix every model shares (the system prompt, the tool guides, each
  on its own) is printed once, on an *All models* line before the models (`ComparisonReproducibility`
  with `sharedOnce`), and each model's line keeps its run ids, dates and any hash that differs.
- **G.** In comparison scope each answer gives its graders in the **compact format**
  (`ComparisonQuestionDetails` with `compactGraders`): one line, *"Graders: Panel member A 95, panel
  member B 92, reference reader 93."*, each critical error with the grader that flagged it and its quote,
  the claim verifier's rulings as before, each panel member's comment and fault lines only for a **panel
  disagreement** — both members' scores more than `BenchmarkService.SecondOpinionDisagreementPoints` (15)
  apart, or a critical error only one member flagged, marked *(panel disagreement)* after the answer's
  heading — and the reference reader's comment only when its score is more than 15 points from the panel
  mean. The introduction adds a sentence saying so. At most `RcaMaxDetailedQuestions` (8) questions are
  detailed, in priority order — those with a critical error, then those an observation or a writer's
  hypothesis cites, then the rest, each by the widest spread between models, then by number — and the
  others are named in one line, *"Also cited, in the question matrix only (Appendix A): S1-Q9, S2-Q14."*

Each option is a parameter of the shared comparison renderer that defaults to the earlier behavior, so the
comparison Executive Summary does not change.

The table below is the structure **before format 16**, kept for reading a PDF or Word file downloaded
then. A stored document opened now renders in the structure above (§ 7):

| Section | Code-rendered | Writer slot (word cap) |
|---|---|---|
| *The root causes in one sentence* | — | `headline` (35): which axes have an established root cause; under it, each axis' status (*"Intelligence: Not established; speed: Probable cause; cost: Probable cause."*) |
| *Scope and design* | `### Setup and method` (suite, coverage, chat configuration, grading, graders, formulas, comparability, pricing basis, versions), `### Compared models` (letter, model, provider, kind, runs, thinking level, harness version, run dates, price card), `### Configuration differences` (per analyzed pair) and, where recorded, the provider availability subsection | `scope` (120): what this design can and cannot decide |
| *Evidence: intelligence* | The models table (Intelligence Index, interval, ranks, median answer time, cost per question, critical errors), the dimensions table and spreads, `### Failure profile` as **two tables**, *Outcomes* and *Signals*, `### Grader findings by category`, `### Panel agreement`, `### Points at risk`, `### Intelligence Index difference by dimension`, `### Paired tests: intelligence, speed and cost`, `### Per-question matrix`; figure R1 | `evidenceIntelligence` (150), no causal words |
| *Evidence: speed* | `### Where the time goes` (per model), `### Calls, latency and output`, `### Speed differences, decomposed` (pair LMDI with intervals), the speed and cost table; figure R2 | `evidenceSpeed` (150), no causal words |
| *Evidence: cost* | `### Where the cost goes`, `### Tokens per question`, `### Cost differences, decomposed` (volume and price effects); figure R3 | `evidenceCost` (150), no causal words |
| *Known facts* | The numbered `rca.known.<n>` list, then `### What the graders recorded` | `observations` (at most 8 items, each at most 35 words, citing a question and evidence): grader-recorded facts from the excerpts and comments, no causal words |
| *Hypotheses* | A note on the grades, then per axis (`### Intelligence`, `### Speed`, `### Cost`) a five-column table — *Hypothesis* (the key, `HQ1.1`), *Statement*, *Models*, *Locus*, *Grade* — followed by one bullet block per hypothesis: *For*, *Against*, *Grade reasons*, *Refuted if*, *Open confounds* where any, and *Settled by: next run n* or *check n*; then figure R4 | `hypothesesIntelligence`, `hypothesesSpeed`, `hypothesesCost` (200 each, each after its axis' table): weigh them. Then `### The writer's hypotheses` (`writerHypotheses`, at most 4, each text at most 40 words: axis, models, locus, statement, `for[]`, `against[]`, `refutedIf`; confidence always *Low*) |
| *Conclusions* | A table, *Axis · Status · Best hypothesis · Why*, then the statuses in one line | `conclusions` (200): per axis, the root cause and its confidence, or that it was not established and why |
| *Next runs and checks* | A note on the order, then one `### Run n: <kind> (<purpose>, <mark>)` per run — *Decides*, *Predicted comparability*, *Estimate*, *Settles*, a two-column *Launcher field · Setting* table under the launcher's own labels (a setting the runs did not share reads *not shared by the analyzed runs: choose it*; *Report Writer* is always *none*), and the run's notes — then `### Checks`, numbered, each with its kind and the hypotheses it settles | `nextRuns` (120): why this order |
| *Limitations* | What the benchmark does not measure (single-turn questions, the missing wiki context of live chat, the options graded), what the rule table and the decompositions can and cannot say, the pricing basis | `limitations` (120) |
| *Threats to validity*, *Reproducibility appendix* | As the comparison renderer prints them | — |
| *Question details* | The questions cited anywhere above (`### Q1: <topic>`, then `#### <model>, run <id>`), with rubric, complete answers and grading | `questionTopics` for every question cited whose text the writer was given |
| *Fact sheet* | The anonymized JSON appendix, in the Markdown copy only (`IncludeFactSheet`) | — |
| *Evaluation terms* | The purpose statements, the distillation prohibition and the third-party content sentence | — |

**Model scope** — a per-model pack, a run completion and a battery completion (goldens
`rca_full_named.md`, `rca_run_with_controls.md`, `rca_run_no_controls.md`, `rca_battery_standalone.md`)
— keeps the sections in that order, with these differences. From format 16 the detail blocks named here
sit in Appendices A and B; *The questions* was left out when the document covers one model only, and from
per-model format 21 it is left out of every document:

- *Scope and design* opens with what the analysis describes (*"This analysis describes run 31 of Orion
  Max on its own. Other models enter only as controls: 2 qualified."*); a run- or battery-completion
  document adds `### Controls`: what a control is, the table *Control · Provider · Run · Date (UTC) ·
  Shared questions*, the **control limit** that applied (*"none: two or more controls, at least one from
  another provider, so the intelligence hypotheses are graded as any other."*) and the subject's mean
  difference against the controls with its interval; with none, it says that none qualified.
- *Evidence: intelligence* shows `### Key figures`, `### Results`, `### Dimensions`, `### Difficulty
  bands (assessed)`, `### Failure profile` (**one table**, *Measure* against the subject: a single row
  is narrow), `### Grader findings and panel agreement`, `### Tool use`, `### Points at risk` and
  `### Per-question results`; with peers (a per-model pack), also `### Compared models` and
  `### Configuration differences` under *Scope and design*, `### Results against peers` (quality, speed
  and cost against each peer, with the paired differences), `### Judge-dependent pairs` and `### Intelligence
  Index difference by dimension`, and the pair decompositions under speed and cost; on a battery, the
  `### Battery profile`, `### Weighting sensitivity` and `### Leave one suite out` blocks; and in a run-
  or battery-completion document with controls, `### Item-level controls`: *Question · Topic · <subject> ·
  Controls' mean · Difference · Controls · Mark*, the instrument-suspect and model-specific questions
  marked.
- *Evidence: speed* and *Evidence: cost* show the subject's own decomposition (`### Where the time goes`,
  `### Calls, latency and output`, `### Speed and cost figures`; `### Where the cost goes`,
  `### Tokens per question`), and with peers `### Speed differences, decomposed` and `### Cost
  differences, decomposed`.
- The `evidence*` slots are capped at **100** words in a run- or battery-completion document
  (`RootCauseCompletionEvidenceMaxWords`).
- In a per-model pack the pair rules run between the subject and each peer on the subject-centered keys
  (`peer.X.*`, the paired bootstrap of `BenchmarkReportFacts`), and rule 16 reads `{{subject}}` against
  `{{peer:X}}`.

| Origin | Peers | Rules that run | Next runs |
|---|---|---|---|
| Run completion | none; controls | Within-model rules; the control limit; the item-level control rules | `control` when fewer than 2 qualifying controls (Recommended); `replicate` (*Number of Runs* 3) when a speed or cost hypothesis is Moderate only because of a single run; `thinkingPair`, `stylePair`, `timeStratum` as they fire |
| Battery completion | none; controls | Within-model rules, with suites as WQ2's groups and leave-one-suite-out as its robustness check; the control limit; the item-level control rules | `control` (battery target) when fewer than 2 qualifying controls; `replicate` (*Runs per Suite* 3) when the battery has 1 run per suite |
| Per-model Report Pack | the comparison's other non-excluded entries | Within-model rules for the subject, plus the pair rules between the subject and each peer | As above, centered on the subject |

**Figures.** Every Root Cause Analysis carries the four server-drawn figures R1–R4 (below). Where peers
exist (a comparison or a per-model pack) the browser-drawn Intelligence, Speed, Cost, Intelligence
against speed and Intelligence against cost may be chosen as well (§ 13).

**The prompt** (`BenchmarkReportPackPrompt.RootCause.cs`, in comparison and model-scope forms). The system
prompt states the method, the grades, the control limit, the slot rules and the claim rules 32–36 in
plain words. The user message lists SCOPE, MODELS (or SUBJECT and PEERS, and the controls),
CONFIGURATION DIFFERENCES, FACTS (`rca.*` included, except the `rca.question.*` facts from format 25 and
the price effects' facts from format 26), KNOWN FACTS, HYPOTHESES (each with for, against, grade, reasons
and refuted if; from format 26 an axis' price effects are counted, *"(five price-card differences left
out: Overseer prints them in one line; never mention them)"*, not listed), NEXT RUNS, CHECKS, from format
26 **UNEXPECTED RESULTS** (each result's `.ref` and `.summary` tokens, then the instruction of the
optional `unexpectedResults` slot, printed only when `rca.anomaly.count` is above zero, so the system
prompt stays free of data), **CLAIM SUPPORT** (per claim kind, the exact token keys that qualify), the
question matrix or per-question results, and the weakest answers and questions. The writer weighs; it does
not re-grade.

**Prompt rules of format 26.** The evidence slots say what stands out — a pattern several models share, a
model that departs from the others, or a result the settings or the price cards would not predict — with
no sentence on what it means for choosing a model or a deployment; the cost slot asks for what the price
cards do not explain, never that a dearer model costs more, and cites `volumeRatio` for token volume; the
hypotheses slots give the grade word only, never mention a price-card difference and leave the nulls to
Overseer; the headline and the conclusions never mention price per token or the price cards; GRADES has
no price-note line; a writer hypothesis' *against* is a measured fact that cuts against the statement,
never its own evidence restated, and empty when there is none; and a count across models, questions,
answers or pairs written *N of M* comes only from a fact token, a plain count with no total such as
*three pairs* staying a number word (rule 44). FACTS notes explain `volumeRatio`, `.grouped` and the
`rca.anomaly.*` facts.

### Claim discipline

Rules 32 to 36 apply to the Root Cause Analysis only, in both scopes. Like C1 to C9 (§ 16), each is
built from one `RootCauseClaimSupport` object that the prompt and the validator share, so the prompt
names exactly the tokens the validator accepts.

| Rule | Check | Effect |
|---|---|---|
| 32 (`RootCauseEvidenceCausalRule`) | A causal word (`RootCauseClaimSupport.CausalWords`: *because*, *due to*, *caused*, *causes*, *explains*, *driven by*, *root cause*, *leads to*, *results in*, whole words and phrases ignoring case) in `scope`, an `evidence*` slot, `unexpectedResults` (format 26) or an observation | The paragraph or item is dropped |
| 33 (`RootCauseCausalClaimRule`) | A causal word in a `hypotheses*` slot, `conclusions` or `nextRuns` with no token of a hypothesis graded **Low or higher** (`rca.hypothesis.<id>.<n>.*`, its `.label` included) or of an axis (`rca.axis.<axis>.*`) in the same sentence; in `nextRuns` only, an `rca.run.<n>.*` token serves as well, since the run order is decided by code. A causal word resting only on hypotheses graded Low needs a hedge word (`HedgeWords`: *may*, *might*, *could*, *possibly*, *probably*, *likely*, *perhaps*, *suggests*, *suggest*, *consistent with*) in the sentence. A **negated** causal word (directly after *not*, *never* or *cannot*, with *be*, *been*, *the* or *a* allowed between; *not only* asserts a cause and does not count) rules a cause out and may cite any hypothesis, *Not supported* included | The paragraph is dropped |
| 34 (`RootCauseCertaintyRule`) | A certainty word (`CertaintyWords`: *established*, *confirmed*, *proven*, *prove*, *proves*, *proved*, *definitely*, *certainly*, *root cause was found*), anywhere, without a token of a *High* hypothesis that is not a price effect (its `.priceCard` fact not true), or the `rca.axis.<axis>.status` token of an axis whose root cause is established, in the same sentence; a certainty word after *not*, *never* or *cannot* does not count (*does not prove* passes) | The paragraph or item is dropped |
| 35 (`RootCauseWriterHypothesisRule`) | A `writerHypotheses` item without an axis, a statement, evidence for, a refuted-if statement or a known locus (`model`, `chat`, `harness`, `suite`, `corpus`, `provider`), with a confidence other than *Low*, or citing a model or fact that does not exist; each of its texts is capped at 40 words. A kept item's `evidence` gains, after its own entries, every question reference its texts name that the data has (`Q7`, or `S2-Q7` on a battery), from format 26 | The item is dropped |
| 36 (`RootCauseAxisStatusRule`) | `conclusions` does not cite each axis' `rca.axis.<axis>.status` | A **warning**, checked over the kept text |

A hypothesis' **label** is a noun phrase that appears in tables; the writer cites a hypothesis in prose
by its **`.ref`** token (*HQ1.1*, the id alone from engine version 6), which carries a cause for
rule 33 and, for a *High* hypothesis, certainty for rule 34 like any other token under the hypothesis
prefix; the rule-33 and rule-34 examples in CHECK are built from it, and the repair message for rule 33
names it. Its **statement** is a sentence and stands alone. The writer names a cause for "the gaps" only
when the best hypotheses cover more than one pair, and gives its share when it names one; it writes each
axis status once (*"Cost: {{rca.axis.cost.status}}."*) and never repeats the status words beside it; it
does not gloss terms, which the document glosses. CLAIM SUPPORT lists only the hypotheses graded Low or higher as support for a
cause, and says that a *Not supported* hypothesis' tokens support only a negated one (*not due to*, *not
because*). A *Low* hypothesis is a lead, not a cause, which is why rule 33 asks for the hedge. A grade's
reason, which code prints under each hypothesis, is written without a causal word (*"graded Moderate, with
one run per suite"*, never *"graded Moderate because each model ran once per suite"*): the validator cannot
check that a stated reason is the one code computed, so rule 33 has no exemption for it; the claim rules
and the rule 32–36 repair reminders say so (`GradeReasonRule`). From format 25 there are no answer slots:
none is required, a filled one is a rule-1 unknown slot like any other, and `BenchmarkReportPackParser`
still reads the four names, so a stored writer output parses.

**Readable prose** (from 2026-10-16). The headline takes the axes in the order intelligence, speed, cost
and leads with the first whose status is *Root cause established* or *Probable cause*; cost leads only
when neither other axis has one, and a price-per-token difference is what the price cards show, never the
cause found (`AxisOrderRule`; the conclusions slot and the rule-33 and rule-34 examples of CHECK follow
the same order, price effects last). The writer never puts a parenthesis inside another or a model token
inside parentheses, never joins two fact tokens with *to* or *from … to* unless both print a bare figure
in one unit, and never repeats beside a token the words it already prints (`TokenShapeReminders`, shared
with the comparison Executive Summary). The validator warns under rule 31, in a Root Cause Analysis only
and keeping the text, on a parenthesis inside another (read with the tokens resolved) and on two worded
fact displays joined by *to*. A **control** is defined once (`ControlWordRule`): *"A control is only an
earlier run of another model that answered the same questions under the same graders and settings, as
CONTROLS lists it; never call a measurement, a count, a figure or a check a control, and in a comparison,
which has none, never use the word."*; in comparison scope the scope, intelligence-evidence and
limitations slots name no controls and no control limit, and the prompt's TERMS leave out *Control*.

The caps (`BenchmarkReportPackValidator.RootCauseSlotMaxWords`): the headline 35 words; `scope` and `limitations` 120; `nextRuns` 50; each `evidence*` slot 100;
each `hypotheses*` slot and `conclusions` 120; the optional `unexpectedResults` 80
(`RootCauseUnexpectedResultsMaxWords`, format 26); at most 8 observations of at most 35 words; at most 4
writer hypotheses. Rules 2–4, 7–12, 16, 17, 19 and 31 apply as to every comparison document (§ 3), rule 4
asking for a topic for every question the document cites whose text the writer was given, and the
warnings of rules 38 to 45 apply as § 3 states. The **optional slot** is the one slot a Root Cause
Analysis may leave out (`OptionalSlots`; it is not in `RequiredSlots`, and the parser reads it as a known
slot): filled, its paragraphs are checked and dropped as a required slot's are and it is trimmed to its
cap; filled on a sheet with no unexpected result, it is removed with a rule-7 note.

**The prompt** states each claim rule once, in CLAIM DISCIPLINE, rule 16 with a worked example for
quality and one for cost; the system prompt points to a *CHECK BEFORE YOU ANSWER* block at the end of
the user message — one line each for rules 3, 16, 31, 33, 34 and 36, each with an example built from the
sheet's own tokens (a mechanism hypothesis graded Low or higher, never a null one, for the cause
example, worded *may be due to* when it is Low). The examples live
in the user message so the system prompt, whose SHA-256 identifies it, holds no data.

### Server-drawn charts

**Why on the server.** Every other chart is drawn in the browser while a wizard is open, then uploaded
(§ 13), and only for a document with peers. A Root Cause Analysis written after a run, with no browser
present and no peers, would never have a chart. So its four figures are drawn **on the server**, from
the stored `RootCauseResult`, right after the document is stored: `WriteDocumentAsync` calls
`RootCauseChartPublisher.PublishAsync` and logs *"Root Cause Analysis: N charts drawn."* (N counts each
figure once, whatever its variants), or *"Root Cause Analysis: the charts were not drawn: <reason>"* as a
warning; a drawing failure never fails the document.

- Each chart is composed as a small one-page **QuestPDF** document whose page is the chart — rows of
  proportional boxes, text in the embedded document fonts that `BenchmarkPdfResources` registers, and a
  key.
- It is rasterized with QuestPDF's own image generation,
  `document.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 300 })`
  (QuestPDF 2026.9.1), and stored through `BenchmarkReportChartStore.SetServerChartsAsync`, its manifest
  entries marked `"origin": "server"`. With no row to draw in any chart, the server-drawn charts stored
  before are removed.
- No new package and no browser, and the same image appears in the PDF and in Word.

`RootCauseChartRenderer.Draw(result, sheet, anonymized)` returns each chart's key, title, caption, alt
text, PNG bytes and size; it is static and pure, so the same result always gives the same charts.
Rejected alternatives: SkiaSharp or ScottPlot (a new native dependency for four
bar charts); QuestPDF's `.Svg()` (vector output in the PDF only, so Word would still need a PNG); and
drawing them in the browser, which cannot reach run- and battery-completion documents.

| Key | Title | Section | Rows | Segments and marks |
|---|---|---|---|---|
| `r1-points-at-risk` | Where the points were lost | 2 | Each model (stand-alone: the subject, then one row per difficulty band, or per suite on a battery) | A stacked horizontal bar of at-risk index points: answers without tools · budget, truncation or output defects · confirmed critical errors · other low-scoring answers. The bar's end is labeled with the total; the axis is points out of 100 |
| `r2-speed-decomposition` | Where the time goes | 3 | Each model (stand-alone: the subject, then per band or suite) | A stacked bar of the median seconds per question, split among time to first token · output generation · tool execution · own waits and retries · other in proportion to each segment's mean, so the segments sum to the median exactly |
| `r3-cost-decomposition` | Where the cost goes | 4 | Each model (stand-alone: the subject, then per band or suite) | A stacked bar of cost per question: uncached input · cache reads · cache writes · visible output · reasoning; a model whose provider counts thinking inside output has one *output, thinking included* segment, in the visible-output color slot |
| `r4-hypothesis-confidence` | How sure each hypothesis is | 6 | One row per non-null hypothesis shown in the document's tables, grouped by axis (null hypotheses are not plotted, and from engine version 10 neither are price effects, the caption then adding *"; price-card differences are left out"*) | A filled dot at its grade on an ordinal axis (Not supported · Low · Moderate · High), the hypothesis id and its short statement as the row label; the named variant prints each model's name where the statement says *Model X*. Where the hypothesis has a share, a second column shows a bar with its 95 % interval whisker |

Rules common to all four:

- **Color is never the only carrier.** A segment prints its **value** (one decimal for points and
  seconds, dollars for cost) when its width is at least the label's estimated width (4.5 pt per
  character) plus 4 pt; the key names every segment present by color, in stacking order, without
  numbers. R4 prints its grade as text beside the dot.
- **Axis.** The scale's ceiling steps through 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8 and 10 times a power of
  ten (`NiceCeiling`), so a 60 s maximum keeps a 60 s axis; the axis title has a row of its own under the
  ticks (R1–R3 are 11 pt taller for it).
- **Palette** (`RootCauseChartPalette`), for print on white; text always wears the ink (`#0b0b0b`, the
  key and axis `#52514e`), never a segment or grade color:
  - **Segments**, in stacking order, the `dataviz` reference slots yellow, green, magenta, violet and red:
    `#eda100`, `#008300`, `#e87ba4`, `#4a3aa7`, `#e34948` (R1 uses the first four). None is one of the
    three provider hues of the browser charts, because a segment is a cause, not a provider; the order is
    the one of the 120 permutations that keeps the worst adjacent pair furthest apart. Validated on
    2026-10-15 with the skill's `validate_palette.js` (`--mode light --surface "#ffffff"`): the lightness
    band, chroma floor, color-vision-deficiency separation (worst adjacent ΔE 16.2, protan, green and
    yellow) and normal-vision floor (worst adjacent ΔE 30.3) pass; contrast warns for yellow (2.17:1) and
    magenta (2.69:1) on white, which the printed numbers and the key relieve. The number inside a segment
    is in ink or white, whichever contrasts more, each at least 4.5:1.
  - **Grades**, one blue hue in four lightness steps from Not supported to High: `#86b6ef`, `#3987e5`,
    `#1c5cab`, `#0d366b` (`--ordinal`: monotone lightness, adjacent ΔL at least 0.06, light-end contrast
    2.11:1, single hue, all pass). The grade is printed as text as well.

  The validation commands and results are recorded beside the constants.
- **Size.** Full column width (481.9 pt); height 40 pt plus 18 pt per row, **at least 78 pt** — so a
  one- or two-row chart still rasterizes at least 320 px tall, the chart store's minimum — and at most
  60 % of an A4 page's content height. Rows that would pass the cap are summarized as *"and N more rows,
  in the tables"*. Text is 8 pt, the body font. A server-drawn figure prints full width in a row of its
  own, never in a figure row, and the chart layout never lists it (from PDF layout 9, § 8).
- **Named and anonymized variants.** A document with peers, or of comparison scope, gets both — model
  names, then letters as in the document; a stand-alone document gets the named variant only.
- **Alt text.** The title, then one clause per row with its total and its largest segment — *"Model A:
  14.1 points at risk, most from confirmed critical errors (6.0)"* — at most 1,000 characters, cut at a
  row boundary with *"and N more rows"*.
- **Caption.** What is plotted, for whom (*per model*, or *for the subject and per difficulty band* or
  *per suite*) and its basis — the pricing basis for R3, *median per question* for R2 — then *"Drawn by
  Overseer from the analysis computed <UTC>"*.
- **Unavailable data.** A row whose decomposition is unavailable shows *"not recorded (<reason>)"*
  instead of a bar. A chart with no available row is not drawn, and the document still stores.

**Store, render and redraw.** `BenchmarkReportChartPlacement.ServerFigureKeys` holds R1–R4 with their own
anchor table, `RootCauseAnchors` (`RcaIntelligence` 16, `RcaSpeed` 17, `RcaCost` 18, `RcaHypotheses` 19), and
`IsServerDrawn(key)` tells them apart; `IsKnown` accepts them and `AllFigureKeys` counts 17.
`BenchmarkReportChartStore.SetServerChartsAsync` replaces only a document's server-drawn entries, and a
browser upload through `SetChartsAsync` keeps them; both use the staging and atomic-swap path, and
`MaxCharts` is 24. The render service's stand-alone chart refusal stays for browser uploads only, and
the renderer's `Figures` writes markers for server-drawn keys even in a stand-alone document.
**Redraw charts** (`POST /api/admin/benchmark/report-documents/{id}/charts/redraw`,
`BenchmarkReportRenderService.RedrawServerChartsAsync`) draws them again from the stored `FactsJson`
after a renderer change, with no model call. The client's chart picker never lists them and its
publisher never uploads one (§ 13).

### Writing it

- **Audiences.** `BenchmarkRunReportDocumentService.Audiences` and its battery counterpart are
  `{ ExecutiveSummary, RootCauseAnalysis }`: a run or battery run with a *Report Writer* gets both, and
  *write the missing documents* fills in a Root Cause Analysis for one that already has an Executive
  Summary. A model batch's MB-A11 counts two documents per run. The per-model and comparison Report
  Packs offer both audiences; chat consistency offers neither the Root Cause Analysis nor any retired
  audience, and each scope refuses an audience it does not write (§ 9).
- **The estimate.** 10,000 output tokens in model scope and 14,000 in comparison scope, both marked for
  recalibration after the first five jobs of each; with the repair-turn range of § 9, roughly $0.3–0.8
  per run in model scope.
- **Calls.** The first call, one repair turn and, when only word caps and/or the headline still block,
  one shortening turn: at most three per document, or four when the first reply was unreadable (§ 3).
- **Prompt rules of engine version 7.** The writer names an axis before its status token (*Speed:
  {{rca.axis.speed.status}}*) and never puts two status or grade tokens side by side; cites a check
  inside a sentence by `{{rca.check.<n>.summary}}`; said, up to format 25 where
  `{{rca.axis.<axis>.priceNote}}` existed, that price per token explains the largest gaps on that axis and
  the best hypothesis its own pair only (from format 26 there is no price note, and the writer never
  mentions the price cards); gives a price effect no refutation; and uses a model's figure token for that model only, citing each
  model's token or a count across them for several.

### Retirement of audiences 2 and 3

On **2026-10-15** the *Report for AI Researchers and Developers* (`TechnicalReport`, 2) and the
*Internal Improvement Brief* (`InternalBrief`, 3) were retired from every scope — per-run and battery
documents, per-model and comparison Report Packs, and chat consistency documents — and replaced by the
Root Cause Analysis. **On the first Overseer start after the change, every stored document of either
audience was deleted, with its run fingerprint rows and its chart folder**, by the startup step
`BenchmarkReportAudienceRetirement.RunAsync`, called in its own try/catch in the benchmark cleanup block
of `Program.cs`, after `SettleInterruptedAsync`. PDFs and Word files downloaded before then were not
touched.

How the step works, since it stays in place: it selects the documents whose stored `Audience` is in
`RetiredAudienceValues` (`{ 2, 3 }`, integer literals, because the enum members no longer exist) in
batches of 200; leaves any whose run or battery run is Pending or Writing for the next start; deletes
each through the ordinary delete path (row, cascade, chart folder under the chart store's per-document
lock; a chart folder that cannot be removed is logged and the row deletion stands); then re-settles each
affected run and battery run once (`ResettleAsync`): Completed, or CompletedWithWarnings when a present
document has warnings, with every audience present; NotRequested with *"Root Cause Analysis not written
yet."* when the Executive Summary is present without it; NotRequested with no message when nothing
remains. It logs *"Retired N documents (TechnicalReport a, InternalBrief b); charts removed c; d runs and
e battery runs re-settled."* It is safe to keep forever: neither audience can be written any more, so a
second start deletes nothing. No EF migration and no schema change were needed, since the audience is an
`int` column. Values 2 and 3 are never reused; the enum's comment records them. Validator rules 5, 15 and
18, which checked parts of the retired documents, keep their numbers unused, and rule 14 keeps only its
headline half. The Download Center and chart settings stored in browsers drop the two document types when
read (§§ 8, 13).
