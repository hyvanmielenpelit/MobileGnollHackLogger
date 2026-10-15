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
| 2 | Every `{{…}}` token exists: a fact key, `{{subject}}` or a known peer letter |
| 3 | No bare digits in prose after masking tokens, question references, known names and the fixed phrase *95% interval* |
| 4 | Every question number exists, and the question topics cover every question where the document requires them (the Root Cause Analysis: every question it cites) |
| 5 | *Retired* (evidence for the retired audiences' recommendations) |
| 6 | A strength may not cite a weakness row and a weakness may not cite a strength row; a finding citing only Conflicting rows must say the graders disagree |
| 7 | Length limits: headline at most 35 words; a question topic at most 12 words; in the Executive Summary at most 3 strengths and 3 weaknesses, each at most 30 words, *What this means for use as a game assistant* (`meaning`) at most 90 words, *How reliable this result is* (`confidence`) at most 60 words and the *How it compares* paragraph (`comparison`) at most 70 words; the Root Cause Analysis caps are in § 17 |
| 8 | No headings, tables or HTML inside any text |
| 9 | No run of 8 or more words shared with any question, rubric, answer or grader-evidence text, including a comparison's *weakest answers* grader comments (`ModelNotes`, § 15) |
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
| 31 | *Token shape* (`ComparisonTokenShapeRule`), comparison scope and every Root Cause Analysis (both scopes), not a per-model Executive Summary: read with every token resolved to its display (a model token to *Model X*), the text holds **(a)** no full stop followed by a lower-case letter, outside abbreviations such as *e.g.* and *vs.* — a sentence-valued fact continued after its full stop; **(b)** no sentence-valued fact (a display that starts with a capital and ends with a full stop, every `notable.<n>.summary` among them) placed anywhere but at the start of a sentence, a paragraph or a list item, or just before `)`; **(c)** no clause-valued fact (*"Model A took 1.49 times as long as Model F …"*) right after a model or its own verb (*"Model A took {{…}}"*); and **(d)** no run of two or more *N of M* counts separated only by commas or semicolons, which leaves the reader guessing what each counts; and, in a Root Cause Analysis only, **(e)** no sentence-shaped fact (`RootCauseClaimSupport.IsSentenceShaped`: a hypothesis' statement, *for*, *against*, *refuted if* and grade reasons, a known fact, a check, a question's text and signals, a profile's joined flags, a notable finding's summary, the response-style conflict) after words of the writer's own sentence — it stands alone, or after a colon or a list marker; a label and a single flag are noun phrases and exempt. One note per kind found. A **warning**: it asks for the repair turn and keeps the text |
| 32–36 | Root Cause Analysis only: the claim discipline of § 17 (`RootCauseEvidenceCausalRule`, `RootCauseCausalClaimRule`, `RootCauseCertaintyRule`, `RootCauseWriterHypothesisRule`, `RootCauseAxisStatusRule`; 36 is a warning) |
| 37 | *Bare model letter* (`BareModelLetterRule`), comparison scope only: read with every token stripped, the prose holds no *Model X* or *Models X and Y* (`\bModels? [A-Z]{1,2}\b`, case-sensitive) — a model is named only through its `{{model:X}}` token, in every slot and list, the writer's hypotheses included. A **warning**: it asks for the repair turn and keeps the text; a named copy renders a bare letter that resolves to a peer as the model's name |

Rules 2, 3, 8, 9, 10, 11, 12, 16, 17 and 19 apply to every prose string: the headline, each paragraph of
each slot, and the text of every item and topic. Rule 13 applies to each paragraph of the Executive
Summary's `confidence` slot, rule 14 to the headline, rules 16 and 19 sentence by sentence, and rule 31
to each prose string of a comparison-scope document or a Root Cause Analysis.

**Readability.** The system prompt of every audience adds: one idea per sentence; sentences of at most
about 25 words; active voice; a count from the facts rather than *many* or *several*; the category of a
finding named rather than *"issues across many topics"*; and no hype words (the rule 17 list). The
Executive Summary keeps its plain-language rule. The comparison-scope prompts and the Root Cause
Analysis prompt, and their repair messages, also carry the **token-shape reminders** rule 31 checks
(`BenchmarkReportPackPrompt.TokenShapeReminders`): a sentence-valued fact is a sentence of its own, at
most once, never inside parentheses and never continued after its full stop; a fact whose value names a
model and a verb is written alone as the clause; and several *N of M* tokens are never listed in a row
without saying what each counts.

**Repair and drop policy.**

1. When any rule fails, the writer gets **one repair turn**: the issues **grouped by rule**, each group
   headed by the rule's number, name, count and one fix instruction from the rule catalog
   (*"- Rule 16 — Ranking without the overlap caveat (13): …"*), then its locations, a message that
   repeats the one before it written *"as above"*; then the output rules in brief
   (`BenchmarkReportPackPrompt.BuildRepairMessage` and `AppendGroupedIssues`; with the headline's word
   count against its cap when an issue is the headline's). The job log says how many issues the first
   reply had, counted per rule (*"7 validation issue(s) (rule 2: 5, rule 16: 2); sending one repair
   turn."*). A repair reply that cannot be parsed is logged as a warning, and the first reply is used
   instead.
2. What still fails after the repair is **dropped** — the item or paragraph is removed from the stored
   output, the note records `Dropped`, and the document is stored as **CompletedWithWarnings**. An
   over-cap slot with a word limit (`meaning`, `confidence`, `comparison`, and every Root Cause
   Analysis slot) loses its last paragraphs until it fits.
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
   2. **In comparison scope only**, when that fails too, one **headline-only turn** on the same
      conversation asks for the headline alone, with its issues and word count
      (`BenchmarkReportPackPrompt.BuildHeadlineRepairMessage`); a passing reply replaces it (*"… it was
      rewritten in a headline-only turn."*).

   A rescued headline leaves a rule 7 note marked dropped, so the document is stored *Completed with
   warnings*. A document therefore takes **at most three model calls**
   (`AgentRunBudget.MaxTotalModelCalls = 3`): the first, the repair turn and, in comparison scope, the
   headline-only turn.
5. A missing headline, a headline the rescue could not fix, or an empty required slot cannot be dropped
   around: the **document fails** and no row is stored. A headline whose only fault is rule 12 is kept.

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
message (holding no digit), and its severity: *removes the item*, *warning, text kept* or *fails the
document* (`SeverityFor`: a warning rule warns; a blocking issue on the headline, or an undropped rule-1
issue such as a missing section, fails the document; every other issue removes its paragraph or item).
`LocationLabel` puts a location in words: `sections.conclusions[p1]` becomes *"Conclusions, paragraph
1"*, `observations[2]` *"What the graders recorded, item 3"*.

**The removed text is kept.** A removed paragraph or item, and the paragraph or item a kept warning
flags, is copied into the note's `Text` (raw, tokens unresolved, at most 2,000 characters) and stored
with `ValidationNotesJson`; notes stored before have none. A Root Cause Analysis prints it under
*Removed content* at Full disclosure, tokens resolved under the document's peer naming (§ 17).

**Rule 36 after a drop.** When a removal in `conclusions` leaves an axis status uncited that was cited
before it, and the document is not otherwise fatal, Overseer appends a code-written paragraph for that
axis, citing `{{rca.axis.<axis>.status}}` (and the best hypothesis' statement where there is one),
prefixed *"Added by Overseer, because the writer's paragraph on this axis was removed:"* in italics. The
rule-36 note is still recorded, so the document stays *Completed with warnings*.

**Progress and diagnostics.** Each document row of a job carries:

- its **phase** (`BenchmarkReportPackPhases`): `Preparing`, `Writing`, `Checking`, `Revising`,
  `CheckingRevision`, `Storing`, `DrawingCharts`, `Done`; the headline-only turn passes through
  `Revising` and `CheckingRevision` too. On `Revising`, `phaseDetail` says why, from the catalog: *"The
  automatic check found 20 issues in the first draft: ranking without the overlap caveat (13), …"*. The
  status values are unchanged (`Repairing` stays on the wire; the UI shows it as *Revising*);
- `firstDraftClean`, and `calls`: every writer call with its kind (`Draft`, `Revision`, `Headline`),
  start, duration, input, cached-input and output tokens, cost, finish reason, and the messages and
  characters sent;
- its first-turn issues, its final issues and, for a failed document, its last raw reply (cut at
  200,000 characters), as `firstTurnIssues`, `finalIssues` and `failedReply`, each issue with its
  `ruleName`, `severity`, `locationLabel` and `text`. `firstTurnIssues` is sent **while the job runs**,
  once the document has reached `Revising`; the other two only once the job has ended.

The job itself carries its context: report format, engine, PDF and Word layout versions, the writer
prompt's SHA-256, the writer's provider, model id and thinking level, the reference model, the entries
(`battery:13`, …) and the preparation notes. The job log's line *"N item(s) removed by validation"* is
written only when N > 0 and names each removed location and rule.

The Report Pack diagnostics print all of it (§ 12): the context, one line per call, *First draft
clean*, each issue as *"rule 16 (Ranking without the overlap caveat, warning) at Hypotheses: cost,
paragraph 2: …"* with a removed item's text indented below, `== Preparation notes ==`, and a *Why
document N … was completed with warnings* (or *failed*) section built by the same function as the dialog
below.

The wizard's Write step marks such a document *Completed with warnings*, and the notes are stored with
it (`ValidationNotesJson`, returned by the detail endpoint as `validationNotes` and, with rule names,
severities and location labels, as `validationIssues`, § 9), so a dropped item is never silent. **An
info button** beside a *Completed with warnings* or *Failed* chip in the Report Pack panel, and beside
*Written with warnings* under *Documents of this comparison*, opens a dialog, *"Why <document> was
completed with warnings"* (`report-validation-details`): a one-sentence summary, what was removed (each
with its text), what was kept with a warning, how the draft was checked (*"First draft passed every
check."*, or the first draft's issues by rule, the revision and the final count), and what can be done.
Under each chip a phase line reads, while running, *"Revising the draft — 20 issues found by the automatic
check"* (with its own info button listing the first-draft issues), and after the job *"First draft passed
every check"* or *"Revised once — 20 issues in the first draft"*. The panel's polite status line
announces a phase change, never the clock.

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

**Callouts.** A renderer highlights a key finding, a question, a caveat or a conclusion with a GitHub
alert blockquote, so the Markdown copy stays portable (GitHub draws it as an alert, any other viewer as
a plain quote):

```markdown
> [!IMPORTANT]
> **Key finding.** The headline.
```

The first line is the marker alone (`[!NOTE]`, `[!IMPORTANT]`, `[!TIP]` or `[!WARNING]`), and the next
starts with a bold label as the callout's first bold run: *Question* (note), *Key finding* (important),
*Conclusion* (tip), *Caveat* (warning), or a specific one. Only the Root Cause Analysis uses them (§ 17).
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
- **Writer input**: the rubric's `SOURCE` paragraphs are removed from the prompt; answer excerpts are cut
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
  document and subject, the same-provider warning and any refusal. Every estimate is a **range**: its
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
  sharedQuestions }` (`completedAtUtc` carries the control's start time); `controlLimit`, `None`,
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
    the preview, asked 300 ms after the last change of scope, models, checks or writer, an answer for an
    older choice dropped. While it is awaited, the section is busy and shows a small spinner
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
    (refused from 90 %, § 15).
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
  writer, model calls, tokens, cost, estimate) that stays after the job finishes; one row per document
  (preceded by a *Model* column in a per-model job) with its status chip, a live duration, its model calls
  and a charts cell — *Charts: attaching…*,
  *Charts: 3* (the figures attached; the diagnostics add *(6 images, named and anonymized)* where the
  two counts differ), *Charts failed — retry* (a button that tries again) or *Charts: none*; and *Log and
  diagnostics*, a disclosure with the job log and icon-only **Copy diagnostics** and **Download
  diagnostics** (`report-pack_<subject>_diagnostics_<yyyyMMdd-HHmmss>.txt`, LF line endings, never naming
  the user who started the job). The diagnostics print the estimate as its range, *"Estimate: $1.04 to
  $2.10 (first call only, to one repair turn per document)"*, and, once a document row carries them, two
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
`SOURCE` paragraphs, one answer excerpt from the run whose score was the median of the question's rounds,
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
  `spread.quality`, `spread.dimension.<d>`, `spread.speed` and `spread.cost` (*"from 62 (Model D) to 85
  (Model A), 23 points apart"*), and `questions.anyCriticalError`, `questions.wideSpread` and
  `questions.sharedLow`.
- **Joint ranks.** `BenchmarkReportFacts.JointRanks` ranks by Intelligence Index and joins a model to the
  group of the model just above it when their 95 % intervals overlap (touching counts) or their scores
  are equal. Groups are **transitive overlap chains**: A–B and B–C overlapping make A, B and C one group
  even when A and C do not overlap. A tied rank prints the span of its group, *"2–9 of 9 (tied: intervals
  overlap)"* (`JointRankText`, span from `BenchmarkReportFacts.TiedRankSpan`), and a table cell *2–9
  (tied)*; the word *joint* is not used in document text. The same helper words the per-model documents'
  ranks (W2, below).
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
- **Excerpts** share one total budget, `AnswerExcerptChars` × the number of questions, allotted in this
  order: questions where any model made a critical error or had a refuted answer sentence; then those
  whose scores span at least 20 points, widest first; then those every model scored below 50 (a shared
  gap: chat, corpus or suite); then the rest. Within a question the models with a critical error or a
  refuted sentence come first, then the lowest scores; each excerpt is from the model's run whose score
  is closest to its score on the question. Each question's text and rubric are given once.

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
  from **70 %**; the preview reports, and the start refuses with **400**, a prompt above **90 %**
  (`ContextWindowRefusalShare`), in both scopes.
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

`ReportFormatVersion` for comparison scope is **18** (`ComparisonReportFormatVersion`); per-model
documents are at 14 (§ 7). Both comparison document types open with the document type as a bold kicker
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
| `ReportFormatVersion` | `ChatConsistencyReportFormatVersion`, currently **4** (§ 7) |

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
`ChatConsistencyReportFormatVersion` is 4.

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
| Comparison Report Pack (`ComparisonRootCauseAnalysis`) | The covered models, as equals | Each other: the pair rules, the reference and the first and last model on each axis |
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
`model.<L>.` prefix).

### Principles

Each practice is enforced in code, not left to the writer:

| Practice | How the document enforces it |
|---|---|
| Separate observation from inference | Evidence and known facts are computed by code. The validator removes any paragraph in an evidence slot that uses causal wording (rule 32) |
| Test the null first | Every axis opens with hypothesis `H0`: the difference is within sampling noise (intervals overlap and no paired test established). No mechanism can be graded above *Low* for a pair whose `H0` stands |
| Effect sizes with intervals, adjusted for multiplicity | Pair facts keep their Holm family; decomposition shares carry bootstrap intervals over questions |
| Exact decompositions, not correlations | Speed and cost differences are split additively with the logarithmic-mean Divisia index (LMDI), which is exact. Intelligence is split by the scoring weights. No cross-model correlation is ever a known fact (n ≤ a dozen models) |
| Name the confounds | A hypothesis lists the configuration keys that differ between the models it compares (thinking level, service tier, harness, scoring method, graders, prompt options). An open confound caps it at *Moderate* |
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
  `pair.L.M.configDifferences` (the keys that differ, from `BenchmarkPairedTests.ClassifyRunPair` and the
  entry's `Differences`).

Each `sheet.Questions` row also carries the subject's per-question `InputTokens`, `OutputTokens`,
`CostUsd`, `TtftMs` and `ModelCallCount`, means over runs, which the per-question decomposition reads.
**A fact whose source columns are missing on a run** — an older `CallTelemetryVersion` or harness — is
*Unavailable* with a reason, **never zero**; the hypotheses that need it do not fire, its chart row reads
*not recorded*, and a `replicate` run may be proposed instead.

**Decompositions** (`RootCauseDecomposition`), all exact, per pair — the reference against each model,
plus the leader and the last model on each axis — and within one model by group (difficulty band, or
suite on a battery), the overall model on one side and the group on the other:

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
  effect per component. Facts `rca.cost.<T>.<T>.<component>.<volume|price>.share`.
- **Intelligence.** The index difference split by the scoring weights into dimension contributions,
  plus `other` (the critical-error cap and difficulty weighting), summing exactly to the difference:
  `rca.quality.<T>.<T>.dimension.<d>.points`. The **at-risk points** of each failure signal — answers
  without tools; budget, truncation or output defects; confirmed critical errors; other low-scoring
  answers — count the points lost on answers showing the signal, relative to the model's own median,
  each answer once (`rca.atRisk.<T>.<segment>`). They are labeled *upper bound, not a causal estimate*.

LMDI uses the logarithmic mean `L(a,b) = (a − b) / (ln a − ln b)`, with `L(a,a) = a`; a zero component
takes the standard limit, its whole difference going to volume. Every share has a 95 % percentile
bootstrap interval over questions: **2,000 resamples, fixed seed 20261015**, so the same inputs always
print the same interval. Spearman's ρ (`RootCauseStatistics.Spearman`, with a Fisher-z interval over
questions) and the Mann–Whitney test of the within-model contrasts are used only within one model,
across its questions, never across models. *T* in a key is a model token: a letter, or `subject`.

**Known facts** (`RootCauseKnownFacts`) are numbered sentences, `rca.known.<n>`, each produced by a rule
and stating only what a test or a count proves, with its kind (`test`, `count`, `decomposition`,
`configuration`) and the fact keys it rests on: *"Model A's median model time is 1.5 times Model F's on
the same questions, established after Holm across 8 tests"*; *"Decode accounts for 71% (58–80%) of the
per-question time difference between Model C and Model F"*; *"Model I answered 9 of 36 questions without
a tool call"*.

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
pair rules only).

Each axis then gets a status, the fact `rca.axis.<axis>.status` with `rca.axis.<axis>.best`:
**Root cause established** (a non-null hypothesis graded High and its `H0` rejected), **Probable cause**
(the best hypothesis is Moderate), or **Not established** (otherwise, with the best hypothesis' grade
and reasons, or *"No hypothesis fired on this axis."*). In a document with pairs (a comparison or a
per-model pack) the first two need a **pair** hypothesis: a within-model hypothesis says where one
model's own time, cost or points go, not why the models differ, so an axis on which only within-model
hypotheses fired reads *Not established* with *"Only hypotheses about one model's own breakdown fired on
this axis; none compares the models."* A run- or battery-completion document has no pairs and takes the
best hypothesis of either kind.

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
| HQ4 | Intelligence | The order depends on the graders | harness | Judge-dependent pair; same-provider grader; panel disagreement rate at least twice the median | Both members rank alike; the reference reader agrees |
| HQ5 | Intelligence | Completeness was lowest under the concise style | chat | `responseStyleConflict` true for the models concerned | — (never graded above *Low*: no comparison run covers style; it states a condition, not a cause, as `server_benchmark_to_chat_transfer` § 3 requires) |
| HQ6 | Intelligence | A guessed where it should have abstained | model | `wrongInsteadOfAbstaining` higher, with Wilson intervals not overlapping | Intervals overlap |
| HQ7 | Intelligence | Budget, truncation or output defects cost A points | harness / provider | `toolLimitHits`, `truncated`, `outputDefects` at-risk points, at least 1 and above B's | Zero signals; points at risk under 1 |
| HQ8 | Intelligence | A's lower thinking level explains the gap | model | Same model at two levels in the set; the dimension test at the lower level | Thinking-level inversion (the notable finding) |
| HQ9 | Intelligence | The verifier would clear part of A's Accuracy shortfall | harness | Verification-cleared sensitivity above the index (by at least half a point) | Sensitivity equals the index |
| HS1 | Speed | A is slower mainly from more model calls (tool rounds) | model / chat | Call-count share is the largest, with an interval excluding 0 | Per-call latency or decode share is larger |
| HS2 | Speed | A is slower mainly from longer per-call latency (TTFT) | provider / model | TTFT share largest; `ttftPerCallP50` ratio established | Decode or call count dominates |
| HS3 | Speed | A is slower mainly from more output (reasoning) tokens | model | Output-token share largest; `reasoningShare` higher | Decode-rate share larger |
| HS4 | Speed | A is slower mainly from a slower decode rate | provider | Decode-rate share largest | Token share larger |
| HS5 | Speed | Own waits or retries inflated A's time | harness / provider | `ownWaitShare` or `retriesPerAnswer` above the median | Under 5 % of the gap |
| HS6 | Speed | Served tier differed from the requested tier | provider | `servedTierMismatch` | No mismatch |
| HC1 | Cost | A costs more mainly from token volume | model / chat | Volume effect largest; tokens per question ratio established | Price effect larger |
| HC2 | Cost | A costs more mainly from per-token price | model | Price effect largest | Volume larger |
| HC3 | Cost | Reasoning tokens drive A's cost | model | Reasoning component largest | — |
| HC4 | Cost | Weak prompt caching raises A's cost | chat / provider | `cacheReadShare` below the median by 20 points or more, and the uncached-input component is largest | Cache-read share comparable |
| HC5 | Cost | More tool rounds multiply A's input tokens | chat / model | Input-volume effect largest and `modelCallsPerAnswer` higher | Calls per answer comparable |

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
a model name is never cut. A reader meets a name, not only an id, and the writer can cite a cause inside
its own sentence; tables print the id in front (*HQ1.1: …*).

**Null hypotheses** keep their grade value and sort order, but documents print their grade as what it
means: a rejected null as *Rejected — a real difference (Not supported)*, a null that stands as *Stands —
may be chance (Low)* (`RootCauseGrades.HypothesisWordWithGrade`), so a reader is never told "Not
supported" about a difference that was found. **Grade reasons** print the one reason that applied, never
the rule's *"A, or B"* (`RootCauseHypotheses.CheckReason`, `NullStandsReason`, `QualitativeReason`).

### The research questions and the model profiles

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
higher answers it) and, for questions 1 and 2, `.orderEstablished`. Question 1 takes the pair hypotheses
where the model is the **better** side, and only its strengths as signals; questions 2 to 4 take the pair
hypotheses where it is the **worse** side and its own within-model hypotheses. HQ3 (shared failures)
answers none of them. The strength is the best **comparative** grade at Low or higher; when there is
none it is the best within-model grade capped at Low, so a question answered only by the model's own
breakdown reads *Suggestive* at most; with neither it is *Unknown*. The writer answers each in its answer
slot, starting with the reason, and is told when the order is not established or only the model's own
breakdown answers; code prints the answer's strength above it with the comparative and within-model
hypotheses named apart (*"Answer strength: Probable (Moderate) — against the other models, see HS1.1;
its own breakdown, see WS3.3."*), or *"no hypothesis about this model fired — see its profile"*. A result
stored before engine 4 prints the single list it was written with.

**Model profiles** (`RootCauseProfiles.Build`), `rca.profile.<T>.*`, are code-written from facts already
on the sheet, so they cannot be dropped by a rule and never use a causal word:

- **Lost points on** (`.lostPointsOn`): the largest segment of the model's index points at risk —
  answers without tools; budget, tool-limit, truncation or output defects; confirmed critical errors;
  other low-scoring answers — with its points and questions. The last of these is named by the dimension
  that lost more on its answers, Accuracy or Completeness, each weighted by the scoring profile
  (*"low-scoring answers, mostly Completeness: 2.2 index points on 17 questions"*); when the two tie it
  keeps the plain label;
- **Weakest dimension** (`.weakestDimension`): the dimension furthest below the model's Accuracy, and the
  gap; and `.lowestRank` when the model is last on a dimension;
- **Strengths** (`.strength.<n>`): the model's dimension ranks when first or second;
- **Flags** (`.flag.<n>`, joined in `.flags`): critical errors with their questions; answers without
  tools above the median; panel disagreement under the notable findings' rule
  (`BenchmarkNotableFindings.DisagreementConcentration`: at least 3 disagreements on the answers both
  members graded, at twice the **other** models' median rate or more, worded by `DisagreementPhrase`,
  *"panel disagreement on 19% of the answers both members graded, about three times the other models'
  median of 6%"*), which HQ4 uses too; isolated misses (three or more models); output defects and
  tool-limit hits. A flag is a noun phrase, so the writer can cite it inside a sentence.

At most two strengths and three weaknesses per model; every entry is a measured count, share or rank
with its questions.

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
`rca.controls.<n>.model`, `.runId` (or `.batteryRunId`), `.date`, `.provider`, `.sharedQuestions`, and
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
whose *Replicates* check failed.

| Kind | Fires for | Run | Purpose |
|---|---|---|---|
| `replicate` | Any graded pair limited by a single run per suite, or an `H0` that stands | Model batch, same target (suite or battery), *Runs per Suite* / *Runs per model* 3, the hypothesis' models plus the reference (or the subject) | M; B when every hypothesis it settles points first to the `harness` |
| `thinkingPair` | HQ8, HS3, HC3, WS2 | Model batch of the same model at two thinking levels: the pair already analyzed, else the model's configuration and the nearest configured, enabled Benchmark-role configuration of the same model id at another level | M |
| `stylePair` | HQ5 | One single run of the model at the other *Response Style* (*Detailed* after *Concise*, or the reverse), everything else unchanged; the analyzed runs are the other half of the pair | C |
| `panelSwap` | HQ4, HQ9, WQ6 | The same models graded by a panel from two other providers than the analyzed graders' (configured graders only) | B |
| `timeStratum` | HS2, HS4, HS5, HS6, WS1, WS5 | The model's run repeated, *Number of Runs* 3, each started at a different time of day (the run's note says so) | C |
| `control` | A run- or battery-completion document with fewer than 2 qualifying controls; a per-model pack whose subject has fewer than 2 comparable peers | Model batch, same target and graders, **without the subject** (its run already exists): the currently recommended model (`RecommendedModels` in `Overseer/appsettings.json`) of each other provider that has a configured, enabled Benchmark-role configuration, up to the controls needed. A finished run of it becomes a control automatically | B |

**Folding.** After the plans are merged by key, a `timeStratum` or `thinkingPair` plan whose models all
belong to a `replicate` of three or more runs per suite is folded into it (`RootCauseNextRuns.Fold`):
the replicate takes over its hypotheses, gains the instruction *"start each run at a different time of
day"* (`When`) when a time-of-day repeat was folded, and takes the most urgent purpose among them (C
before B before M). Three passes started at different times of day settle what the separate repeats
would, for a fraction of the cost: in Comparison #4's analysis under engine version 1, the nine-model
replicate covered the seven other proposed runs (eight in all, about $450).

A run is **Recommended** when it is a `control` run or settles an axis' best hypothesis, else
**Optional**. Runs are ordered **C, then B, then M** (the chat, then the benchmark, then model selection,
as `server_benchmark_runbook` § 4 orders a round's runs); with the *system first* flag set, every M run
is marked *after the system hypotheses are settled* and its notes say so.

Every run is a structured `RootCauseRecommendedRun`, stored in the document's `FactsJson`, its summary
the fact `rca.run.<n>.summary` — a short line, *"Run 1, a replicate of all nine models (about $260 and
528 minutes)"*, the card holding the rest — and returned on the document detail as `recommendedRuns`:

- `Number`, `Kind`, `Purpose` (C, B or M), `Mark` and `HypothesisIds`;
- `Decides`: one sentence per group of hypotheses with the same confirm and refute conditions,
  *"Confirms HQ2.1, HQ2.2 and HQ2.3 if …; refutes each if …"*, stated before the run;
- `PredictedTier`, from the keys the run changes;
- `EstimatedCostUsd` and `EstimatedMinutes`, from the analyzed runs' own costs and durations (a model
  without analyzed runs takes their mean, and a note says so);
- every launcher setting, named as `StartBenchmarkRunRequest` and `StartBenchmarkModelBatchRequest` name
  them so the prefill maps them one to one: `TargetKind` (*Suite* or *Battery*), `SuiteId` / `SuiteName`,
  `BatteryId` / `BatteryName`, `RunMode` (*Single* or *Batch*), `TestedModelConfigurationIds` and
  `ModelNames`, `Order`, `RunsPerModel`, `RunCount`, `ScoringProfileId`, `VerboseMode`,
  `AllowSourceCodeReferences`, and the grader configuration ids — assessor, co-assessor, reference or
  second reader, claim verifier — with the coverage mode. All are copied from the analyzed runs unless
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
headings are unnumbered. The document is **answer-first**: the questions and their answers, then a
per-model explanation, then the evidence, with the detail in lettered appendices, so the body of a
nine-model comparison is about a fifth of what it was. The renderer
(`BenchmarkReportPackRenderer.RootCause.cs`) prints, in order, each section's code-rendered blocks, then
the section's server-drawn figure where it has one, then the writer's slot; key findings, questions,
caveats and conclusions are callouts (§ 7). The comparison form (`ComparisonRootCauseAnalysis`; golden
`comparison_rca_full_named.md`), from format 16:

| Section | Code-rendered | Writer slot (word cap) |
|---|---|---|
| *Summary* | `### What we found`: the headline in a *Key finding* callout. `### The questions`: each question asked, in a *Question* callout with the question in bold and *Answer strength: Probable (Moderate) — see HQ2.1, HS2.1*. `### At a glance`: *Model · Intelligence (index, interval, rank) · Median time · Cost per question · Where it lost most points · Flags*. `### How sure are we?`: a *Caveat* callout with the grade words in plain order (*"How sure each answer is. **Established (High)**: measured, still clear after the correction for many comparisons, …"*) and the run-level cap stated once (*"Each model ran only once per suite, so this document cannot tell whether its results would repeat. No cause can be graded above Probable (Moderate) until a second run of each suite is made."*) | `headline` (35): in one plain sentence, what most explains the differences between the models; `answerBest`, `answerWorst`, `answerSpeed`, `answerCost` (60 each), in their question's callout, each citing `{{rca.question.<n>.status}}` |
| *Key terms* | Format 18: a two-sentence lead, then *Term · What it means*, one row per term the document actually uses, alphabetical, with the term's short gloss (`RootCause/RootCauseGlossary.cs`, the one term registry). Each less familiar term is also glossed in parentheses where template text first uses it (the render `Context`'s `GlossedTerms`), for example *Holm-adjusted across 4 tests (a correction for running many comparisons at once)* | — |
| *Why each model scored as it did* | The profile table, one row per model in rank order: *Model · Index · Strengths · Lost points on · Flags*, with a note that every entry is measured and none says why | — |
| *Scope and design* | `### Setup and method`, the grader roster, the availability table (one sentence when every model's is unrecorded); the compared models and configuration differences move to Appendix A | `scope` (120) |
| *Evidence: intelligence* | A five-column scoreboard (*Model · Intelligence Index · 95% interval · Rank · Critical errors*, a tied rank as *2–9 (tied)*), the dimensions table, figure R1, `### Paired tests against <reference>` with the adjustment family stated once under it, `### What the graders recorded` (the `observations`), one grouped response-style line | `evidenceIntelligence` (100), no causal words: what stands out and why it matters for choosing a model |
| *Evidence: speed* | `### Where the time goes` and figure R2, the paired tests, `### Time differences between the models`: one line per decomposed pair (its leading component in seconds and its share with interval), the pairs not decomposed in one line | `evidenceSpeed` (100) |
| *Evidence: cost* | The same, in dollars (figure R3) | `evidenceCost` (100) |
| *Hypotheses* | Per axis a table of the kept non-null hypotheses — *Hypothesis (id and label) · Statement · Grade · Share · Settled by* — the null hypotheses in one sentence (where it stands, where a paired result rejects it), the id legend under the first table (H0 null, H between two models, W within one; Q, S, C), figure R4; the writer's hypotheses in a *Not tested by code* caveat callout | `hypothesesIntelligence`, `hypothesesSpeed`, `hypothesesCost` (120 each), each after its axis' table |
| *Conclusions* | The table *Axis · Status · Best hypothesis · Why* | `conclusions` (120), in a *Conclusion* callout |
| *What to run next* | The order (C, B, M), a priority table *# · Run · Purpose · Models · Settles (hypothesis ids, at most eight, then "and N more (Appendix D)") · Estimate*, then `### Checks` | `nextRuns` (50): which run to make first and what it settles |
| *Limitations and threats to validity* | One merged, deduplicated list; the availability threats grouped by reason (*"Provider availability is not recorded for any of the 9 models (runs before harness 56), …"*) | `limitations` (120) |
| *Appendices* | The list of A–H, then **A** Intelligence detail (compared models, configuration differences, failure profile, grader findings, panel agreement, points at risk, the index difference by dimension, every paired test, the per-question matrix); **B** Speed and cost decompositions (decomposed pairs only); **C** Hypothesis details (*For*, *Against*, *Grade reasons*, *Refuted if*, *Robustness*, *Settled by* per hypothesis; the nulls as one table *Hypothesis · Pair · Paired result · Grade*, the grade read as *Stands — may be chance* or *Rejected — a real difference*); **D** Run settings (each run's *Launcher field · Setting* table); **E** Known facts; **F** Reproducibility; **G** Question details (every claim-verifier ruling that is not a supported answer sentence, then *"N further answer sentences supported."*; a grader evidence line that only says *&lt;Dimension&gt;: Matches rubric.* is left out); **H** Glossary: from format 18 every term the document uses, alphabetical, with its full definition from `RootCauseGlossary` (before, nine fixed bullets) | `questionTopics` for every question cited whose text the writer was given |
| *Fact sheet*, *Removed content*, *Evaluation terms* | As before (the fact sheet in the Markdown copy only, with no section at all in the PDF and Word); *Removed content* names each removal's location in words and its rule, and at Full disclosure prints the removed text with its tokens resolved under the document's peer naming (withheld names, ids and providers as letters in an anonymized copy) | — |

The table below is the structure **before format 16**, kept for reading a PDF or Word file downloaded
then. A stored document opened now renders in the structure above, its answer callouts saying to
regenerate (§ 7):

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
sit in Appendices A and B, and *The questions* is left out when the document covers one model only:

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
CONFIGURATION DIFFERENCES, FACTS (`rca.*` included), KNOWN FACTS, HYPOTHESES (each with for, against,
grade, reasons and refuted if), NEXT RUNS, CHECKS, **CLAIM SUPPORT** (per claim kind, the exact token keys
that qualify), the question matrix or per-question results, and the weakest answers and questions. The
writer weighs; it does not re-grade.

### Claim discipline

Rules 32 to 36 apply to the Root Cause Analysis only, in both scopes. Like C1 to C9 (§ 16), each is
built from one `RootCauseClaimSupport` object that the prompt and the validator share, so the prompt
names exactly the tokens the validator accepts.

| Rule | Check | Effect |
|---|---|---|
| 32 (`RootCauseEvidenceCausalRule`) | A causal word (`RootCauseClaimSupport.CausalWords`: *because*, *due to*, *caused*, *causes*, *explains*, *driven by*, *root cause*, *leads to*, *results in*, whole words and phrases ignoring case) in `scope`, an `evidence*` slot or an observation | The paragraph or item is dropped |
| 33 (`RootCauseCausalClaimRule`) | A causal word in an answer slot, a `hypotheses*` slot, `conclusions` or `nextRuns` with no token of a hypothesis graded **Low or higher** (`rca.hypothesis.<id>.<n>.*`, its `.label` included) or of an axis (`rca.axis.<axis>.*`) in the same sentence; in `nextRuns` only, an `rca.run.<n>.*` token serves as well, since the run order is decided by code. A causal word resting only on hypotheses graded Low needs a hedge word (`HedgeWords`: *may*, *might*, *could*, *possibly*, *probably*, *likely*, *perhaps*, *suggests*, *suggest*, *consistent with*) in the sentence. A **negated** causal word (directly after *not*, *never* or *cannot*, with *be*, *been*, *the* or *a* allowed between; *not only* asserts a cause and does not count) rules a cause out and may cite any hypothesis, *Not supported* included | The paragraph is dropped |
| 34 (`RootCauseCertaintyRule`) | A certainty word (`CertaintyWords`: *established*, *confirmed*, *proven*, *definitely*, *certainly*, *root cause was found*), anywhere, without a token of a *High* hypothesis, or the `rca.axis.<axis>.status` token of an axis whose root cause is established, in the same sentence; a certainty word after *not*, *never* or *cannot* does not count | The paragraph or item is dropped |
| 35 (`RootCauseWriterHypothesisRule`) | A `writerHypotheses` item without an axis, a statement, evidence for, a refuted-if statement or a known locus (`model`, `chat`, `harness`, `suite`, `corpus`, `provider`), with a confidence other than *Low*, or citing a model or fact that does not exist; each of its texts is capped at 40 words | The item is dropped |
| 36 (`RootCauseAxisStatusRule`) | `conclusions` does not cite each axis' `rca.axis.<axis>.status`. An answer slot need not cite its question's `rca.question.<n>.status`: code prints the answer strength above it | A **warning**, checked over the kept text |

A hypothesis' **label** is a noun phrase, so rule 31 lets it sit inside a sentence, while it still
carries a cause for rule 33 and, for a *High* hypothesis, certainty for rule 34; its **statement** is a
sentence and stands alone. CLAIM SUPPORT lists only the hypotheses graded Low or higher as support for a
cause, and says that a *Not supported* hypothesis' tokens support only a negated one (*not due to*, *not
because*). A *Low* hypothesis is a lead, not a cause, which is why rule 33 asks for the hedge. The answer slots exist only for the questions the sheet asks: an answer slot
is required when `rca.question.<n>.text` is on the sheet, and a filled slot for a question not asked is a
rule-1 issue.

The caps (`BenchmarkReportPackValidator.RootCauseSlotMaxWords`): the headline 35 words; each answer slot
60 (`RootCauseAnswerMaxWords`); `scope` and `limitations` 120; `nextRuns` 50; each `evidence*` slot 100;
each `hypotheses*` slot and `conclusions` 120; at most 8 observations of at most 35 words; at most 4
writer hypotheses. Rules 2–4, 7–12, 16, 17, 19 and 31 apply as to every comparison document (§ 3), rule 4
asking for a topic for every question the document cites whose text the writer was given.

**The prompt** states each claim rule once, in CLAIM DISCIPLINE, rule 16 with a worked example for
quality and one for cost; the system prompt points to a *CHECK BEFORE YOU ANSWER* block at the end of
the user message — one line each for rules 3, 16, 31, 33, 34 and 36, each with an example built from the
sheet's own tokens (a mechanism hypothesis graded Low or higher, never a null one, for the cause
example, worded *may be due to* when it is Low). The examples live
in the user message so the system prompt, whose SHA-256 identifies it, holds no data. The user message
also lists THE QUESTIONS with their tokens.

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
| `r3-cost-decomposition` | Where the cost goes | 4 | Each model (stand-alone: the subject, then per band or suite) | A stacked bar of cost per question: uncached input · cache reads · cache writes · visible output · reasoning |
| `r4-hypothesis-confidence` | How sure each hypothesis is | 6 | One row per hypothesis shown in the document's tables, grouped by axis | A filled dot at its grade on an ordinal axis (Not supported · Low · Moderate · High), the hypothesis id and its short statement as the row label. Where the hypothesis has a share, a second column shows a bar with its 95 % interval whisker |

Rules common to all four:

- **Color is never the only carrier.** Every segment of 8 % of the bar or more carries its own text
  label; smaller segments are named in the key, in stacking order. R4 prints its grade as text beside
  the dot.
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
- **Calls.** The first call, one repair turn and, in comparison scope, a headline-only turn: at most
  three per document (§ 3).

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
