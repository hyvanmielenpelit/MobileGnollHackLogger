# Report Packs — AI-Written Documents about One Model

A **report pack** is a set of up to three documents about one model of a Model Comparison, written in
the context of the other models compared with it. The documents are for three different readers: the
model's provider or a manager, AI researchers and model developers, and the Overseer team itself.

The same machinery also writes a run's own **run-completion documents**: the Executive Summary and the
Report for AI Researchers and Developers about one run on its own, with no peers, written once after the
run is scored by the report writer the run names (§ 11).

This document describes the feature for developers: what each document holds, how the figures and the
prose are kept apart, how the prose is validated, what is stored, and how a stored document is rendered
at download. It is the companion to [`ai-benchmark.md`](ai-benchmark.md), which describes the harness,
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
| The twelve validation rules and the drop policy | `BenchmarkReportPackValidator` |
| Generation: preparation, the writer call, repair, storage | `BenchmarkReportPackService` |
| The background job and its progress | `BenchmarkReportPackJob`, `BenchmarkReportPackJobManager` |
| A run's run-completion documents: scheduling, the queued job, the run's status | `BenchmarkRunReportDocumentService` (singleton) |
| Deterministic Markdown rendering | `BenchmarkReportPackRenderer` (pure, static), behind `BenchmarkReportRenderService` |
| Endpoints | `AdminBenchmarkReportPacksController` (the write-now endpoint included), `AdminBenchmarkReportDocumentsController` |
| Golden files | `Overseer.Tests/UnitTests/Golden/ReportPack/` |

---

## 1. Purpose and the Three Documents

The Model Comparison wizard's **Reports** button opens the full-screen **Report Pack dialog**. The admin
picks one comparison entry as the **subject** — a run or an analysis group — and a separate **report
writer** model, and chooses which documents to write. The other entries of the comparison are the
subject's **peers**, lettered A, B, C… in quality-rank order.

Any entry that is not **Excluded** can be the subject. A **Degraded** entry is allowed: its degraded axis
is omitted from ranking, and the fact sheet marks the affected facts unavailable with the reason.

| Document | Reader | Disclosure | Skeleton |
|---|---|---|---|
| **Executive Summary** | A non-specialist at the model's provider, or a manager. Plain language, short sentences, no jargon | Summary or Full (§ 6) | Headline (one sentence) · key figures · *What it did well* (at most 3, each at most 30 words) · *Where it fell short* (at most 3, each at most 30 words) · *What this means for use as a game assistant* (at most 90 words) · *How reliable this result is* (at most 60 words) · *Evaluation terms* |
| **Report for AI Researchers and Developers** (stored as `TechnicalReport`) | AI researchers and model developers. Precise and neutral | Any level | Headline · abstract (at most 150 words) · figures against the peers · *Speed and cost* · *Why it scored this way* (the patterns and causes behind the weaknesses, by category, at most 300 words) then *Weaknesses* · *What worked well* (the patterns behind the strengths, at most 150 words) then *Strengths* (at most 8 of each) · question topics for every question · a note for each question more than 15 points below the peer mean or with a critical error (with no peers: scoring below 50 or with a critical error) · at most 6 recommendations for the model's next iteration, each naming the change proposed and, in a few words, the weakness it answers, with its evidence · *Evaluation terms* |
| **Internal Improvement Brief** | The Overseer team and its AI agents. Direct and practical | **Full only**; internal | Headline · three parts in the order of *What the Benchmark Is For*: the Overseer chat and its tools, the benchmarking system, the model's result · strengths and weaknesses · question topics and notes · recommendations for the chat, the benchmark or model developers · **leads** |

The writer fills named **slots** and **lists** only; the renderer supplies every heading, table and
figure. The slot ids are in `BenchmarkReportSlots`: `meaning` and `confidence` (Executive Summary);
`abstract`, `whyItScored` and `whatWorked` (Report for AI Researchers and Developers); `overseerChat`, `benchmarkSystem` and
`modelResult` (Internal Brief). The *Why it scored this way* categories are domain knowledge, reading
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
(§ 11) — has no peers. The fact sheet then marks every fact that compares the subject with peers
unavailable with the reason *"A stand-alone run report has no peers."*
(`BenchmarkReportFacts.StandaloneReason`); the writer prompt says there are no peers, that `{{peer:X}}`
tokens are unavailable and that the subject is never compared with other models; and a question needs a
note when it scored below 50 (`BenchmarkReportPackPrompt.StandaloneNoteScore`) or carried a critical
error, in place of the peer gap. The renderer prints *Peers: none; this is a stand-alone report* in the
header, a results table of the subject's own dimensions and bands with no peer column, question tables
without *Peer mean* and *Difference*, a comparability line that describes the model on its own, and
no pairwise-significance statement.

**Evidence lines.** Under every strength, weakness and recommendation of the Report for AI Researchers
and Developers, the renderer — never the writer — prints **one** compact, indented *Evidence:* line,
its parts joined by ` · ` and each present only where it applies:

- the **finding rows** (`R3`) the item cites: each row's support label (§ 2) and category, and, for a
  group subject, in how many of its runs it recurred;
- the **facts** the item cites, by their human label (`BenchmarkReportFactLabels`) and display value, or
  *not available*;
- the **questions** the item is about — its own question list and the `Q` ids it cites, or, when it
  names none itself, the questions of the rows it cites — each with its score, *Q6 (59 / 100)*;
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
overlaps those of Models B and C"*. The comparison runs **no significance test**, and every document
repeats the comparison's *Pairwise significance* excluded-measure statement.

**Support labels.** Each strength and weakness is printed with a support label computed by code from the
finding rows it cites, never written by the model:

| Label | When |
|---|---|
| *Both graders* | The cited rows are Convergent between the two panel members |
| *One grader — different family* | One member found it, from a family other than the subject's |
| *One grader — same family as the model* | One member found it, from the subject's own family |
| *Graders disagree* | The cited rows are Conflicting |
| *Single assessor* | A single-assessor run |
| *Computed* | The item cites facts or questions only, no finding row |

The Executive Summary prints the same label in plain words (`BenchmarkReportPackRenderer.PlainSupportLabel`):
*both graders agreed*, *raised by one grader*, *raised by one grader, from the model's own company*, *the
graders disagree*, *raised by the grader* and *computed from the figures*.

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

`BenchmarkReportPackValidator` checks the writer's JSON against twelve rules. Each failure is a
`BenchmarkReportValidationNote` with its rule number, location (`headline`, `sections.abstract`,
`weaknesses[1]`…) and message.

| # | Rule |
|---|---|
| 1 | The output is JSON of the expected shape, and every required slot is present and non-empty |
| 2 | Every `{{…}}` token exists: a fact key, `{{subject}}` or a known peer letter |
| 3 | No bare digits in prose after masking tokens, question references and known names |
| 4 | Every question number exists, and the question topics cover every question where the document requires them |
| 5 | Every evidence id exists, and every strength, weakness and lead cites at least one — and so does every recommendation of the Report for AI Researchers and Developers |
| 6 | A strength may not cite a weakness row and a weakness may not cite a strength row; a finding citing only Conflicting rows must say the graders disagree |
| 7 | Length limits: headline at most 35 words; abstract at most 150 words; a question topic at most 12 words; in the Executive Summary at most 3 strengths and 3 weaknesses, each at most 30 words, *What this means for use as a game assistant* (`meaning`) at most 90 words and *How reliable this result is* (`confidence`) at most 60 words; in the Report for AI Researchers and Developers *Why it scored this way* (`whyItScored`) at most 300 words, *What worked well* (`whatWorked`) at most 150 words and at most 6 recommendations |
| 8 | No headings, tables or HTML inside any text |
| 9 | No run of 8 or more words shared with any question, rubric, answer or grader-evidence text |
| 10 | No peer names, model ids or providers in prose |
| 11 | No significance language: *significant*, *significantly*, *statistically*, *reliably better* or *worse*, *clearly outperforms* |
| 12 | US English: no word of the fixed British-spelling list (`BenchmarkReportPackValidator.BritishSpellings`: *colour, behaviour, analyse, analysed, organise, recognise, favour, honour, centre, defence, catalogue, programme, grey, travelled, modelling, labelled, cancelled, judgement*), matched as whole words ignoring case, in any prose |

Rules 2, 3, 8, 9, 10, 11 and 12 apply to every prose string: the headline, each paragraph of each slot,
and the text of every item, topic and note.

**Repair and drop policy.**

1. When any rule fails, the writer gets **one repair turn**: every issue, then the output rules in brief
   (`BenchmarkReportPackPrompt.BuildRepairMessage`).
2. What still fails after the repair is **dropped** — the item or paragraph is removed from the stored
   output, the note records `Dropped`, and the document is stored as **CompletedWithWarnings**. An
   over-cap slot with a word limit (the abstract, `meaning`, `confidence`, `whyItScored`, `whatWorked`)
   loses its last paragraphs until it fits.
3. **Rule 12 never drops.** A spelling slip is not worth losing a finding: after the repair turn, text
   that still uses a British spelling is **kept**, its note is recorded without `Dropped`, and the
   document is stored as **CompletedWithWarnings**.
4. A missing headline or an empty required slot cannot be dropped around: the **document fails** and no
   row is stored. A headline whose only fault is rule 12 is kept.

The dialog and the Download Center show the validation notes, so a dropped item is never silent.

---

## 4. The Writer Model

**Rules enforced by the server.**

- **The model under report is refused as its own writer** — the same provider and model id (400).
- **A writer from the subject's provider** triggers a warning that must be acknowledged: a checkbox in
  the dialog, sent as `acknowledgeSameProvider`; without it the start answers 409 with the warning. The
  acknowledgment is stored on each document as `SameProviderAcknowledged`.
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
models and effort*, the *How the graders work* guide, the dialog's writer info tip and the report-writer
info tip of the run report's **AI Reports** tab.

**Per document.** The two documents a run or a pack writes most often ask different things of the
writer, and each can be written by a different model:

- **Executive Summary** — short (about 2,000 output tokens) and plain-language, for decision-makers;
  clear, careful wording matters more than depth. A strong writing model — Claude Opus or GPT Sol — at
  medium effort; it is cheap even with a strong model.
- **Report for AI Researchers and Developers** — long (about 7,000 output tokens) and number-dense, and it
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

- audience, subject key (`run:<id>` or `group:<id>`) and label, the subject's run ids, the comparison
  request, and the suite;
- `Origin` (`BenchmarkReportDocumentOrigin`): **ReportPack** (1, the default, and the value every row
  written before the column existed carries) for a document of a Report Pack job, **RunCompletion** (2)
  for a run's own run-completion document (§ 11). An index on `(SubjectKey, Origin)` finds a run's
  run-completion documents, and the list DTO carries `origin`;
- the writer's identity and its configuration snapshot, and `SameProviderAcknowledged`;
- `ReportFormatVersion`, the writer prompt's SHA-256 and `AnswerExcerptChars`;
- `FactsJson` — the fact sheet;
- `ContentJson` — the verbatim content the renderer may print: question text as asked, rubric as
  graded, answer excerpts cut at generation, grader evidence and verifier rulings (each with its role
  from format version 3, § 7), all taken from the subject's **answer rows**, never from the live suite,
  so a later suite edit cannot change a document;
- `WriterOutputJson` — the final validated writer output, with dropped items already removed;
- `ValidationNotesJson`, status, tokens, duration and cost.

The child table **`BenchmarkReportDocumentRuns (DocumentId, RunId)`** stores each subject run's
**scoring fingerprint at generation**: `FinalScore`, `QualityIndex`, `SpeedIndex`,
`ScoringMethodVersion`, `RerunCompletedAtUtc`, and the first 16 hex characters of SHA-256 over
`AssessmentJson + "\n" + CoAssessorSynthesisJson`. List responses compare it with the run as it is now
and flag *Run changed since this document was written* (`runChangedSinceGeneration`) when a run was
re-scored or re-run afterwards.

There is **no foreign key to runs**: deleting a run keeps its documents. Deleting a document cascades to
its child rows.

---

## 6. Disclosure and Peer Naming

Both are chosen **at download, per document**; the stored row is the same whatever is chosen.

| Level | Question text | Rubric | Model's answer | Grader evidence | Stamp |
|---|---|---|---|---|---|
| **Summary** | Topic only | Never | Never | Never | *Confidential. Prepared for the model's provider. Questions are described, not quoted.* |
| **Detailed** | Verbatim, every question | Never | Excerpts, every question | Never | *Confidential. Prepared for the model's provider. Contains benchmark questions — do not publish.* |
| **Full** | Verbatim, every question | Verbatim | Excerpts | Verbatim | *INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.* |

The stamps are audience-aware (`BenchmarkReportPackRenderer.Stamp(audience, disclosure)`). The Executive
Summary quotes no question or rubric at any level, so its Detailed stamp reads *Confidential. Prepared for
the model's provider. Review before sharing.* and its Full stamp *INTERNAL — unpublished benchmark
results. Do not share outside the Overseer team.*; its Summary stamp is the one above. The PDF and Word
classification banners use the same text.

In the Report for AI Researchers and Developers, Detailed prints every question and each run's answer
excerpt in one *Questions and answers* section; Full prints the same section as *Question details*, with
each question's rubric, the graders' scores and notes and the claim verifier's rulings added. The notes
on individual questions never quote them at any level: the section does.

**Peer naming** is *Named* or *Anonymized*. Anonymized prints peers as *Model A*, *Model B*…, removes the
provider column, and replaces the peers' model ids and providers in every table. The subject is always
named.

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
calls it; its constructor takes only the DbContext and a logger, and the documents controller's
constructor takes only that service. Architecture tests pin both, so no model client, clock or
configuration can reach the render path.

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

**Format version 4** (the current one, 2026-09-29) makes the disclosure levels differ visibly (§ 6): at
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

The excerpt length used is stored on each row, so changing the setting never changes a re-render.

---

## 8. The Download Center

One dialog, reachable from the run report dialog's **Downloads** button and from the Report Pack dialog,
packages documents and run files for download.

| Package | Contents | Disclosure | Peers | Formats |
|---|---|---|---|---|
| **Internal** | Every available document: pack documents, the run report, the tool-call log, run diagnostics | Full | Named | PDF, Word and Markdown (PDF, Word and Text for the diagnostics) |
| **External** | Executive Summary and Report for AI Researchers and Developers only; internal-only rows are listed but unselectable, with their reason | Summary (Detailed as an option) | Anonymized (Named as an option, with a warning) | PDF |
| **Custom** | Any selection | Per document | Per document | Any, Word included |

The summary line, the ZIP's `MANIFEST.md` and its file name call them *Internal package* and *External package*.
Opened on a run, the dialog lists every document whose subject includes the run, so a run's
run-completion documents (§ 11) appear under both packages beside any Report Pack documents about it.

Each row offers its formats PDF first, then Word: pack documents and the run report PDF, Word, Markdown
and HTML; the tool-call log PDF, Word and Markdown; diagnostics PDF, Word and Text. The choices are
remembered per browser in settings **version 3**. A stored version 2 is migrated: the package, the paper
and every remembered choice are kept except the Internal package's remembered formats, which are dropped
so every admin meets the Internal package's Word default once. Any older version is ignored. The
dialog's explanations — each package's description, the column meanings, the paper size, a row's note
and the full reason a row is internal-only — sit behind click-mode info buttons; the red *Internal only*
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
PDF, Word and Download Center names alike — `run-73_…_full_named_INTERNAL.pdf`; the client's
`reportDocumentFileStem` and the server's `BenchmarkPdfFileNames.ForReportDocument` match the same key.
A group subject has no prefix. The run report and tool-call log are fetched from the
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
  *Source {first 16 hex of the Markdown's SHA-256} · PDF layout 2*, and a classification banner — amber
  *Confidential …* for a provider copy (the audience-aware stamp of § 6), red *INTERNAL …* for everything
  else, the text saying what the color says. A table of contents follows when a document other than the
  Executive Summary has four or more `##` sections. A report document's facts table reads *Document ID*,
  *Disclosure*, *Peers* (the count and how they are named, or *none (stand-alone report)*), *Suite*,
  *Questions*, *Run* or *Runs*, then the creation time, the report format and the writer; it has
  no *Audience* row, since the document kind says it. The Markdown's front matter — the stamp and the
  *Date*, *Suite*, *Questions*, *Runs* and *Peers* list under the title — is left out of the PDF and Word
  files (`BenchmarkReportRenderOptions.IncludeFrontMatter = false`), because the cover prints the same.
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
  *Source {first 16 hex} · Word layout 1* and the classification banner; the facts table and the banner
  text come from the same document information as the PDF's, so they changed with PDF layout 2 while
  the Word layout number did not. The table of contents follows
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
  Word 2019, Word 2021 or LibreOffice.
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
  `{ runIds, groupIds, pricingBasis, subjectKey, audiences[], writerModelConfigurationId, acknowledgeSameProvider }`,
  with audiences as numbers (1 Executive Summary, 2 Report for AI Researchers and Developers, 3 Internal
  Brief). Returns 202 `{ jobId }`.
- `GET /api/admin/benchmark/report-packs/jobs/{jobId}`: Job progress, per document.
- `GET /api/admin/benchmark/report-packs/jobs/active`: The running job, or 204.
- `POST /api/admin/benchmark/report-packs/jobs/{jobId}/cancel`: Cancel the job.

The start's refusals, in the order they are checked:

1. An unknown entry, or an Excluded subject — 400.
2. A writer that is invalid, disabled, keyless, not of the Benchmark role, or refused by the endpoint
   policy — 400.
3. A writer that is the subject's own model — 400.
4. No audience — 400.
5. The spend cap — 429.
6. A same-provider writer without `acknowledgeSameProvider` — 409, with the warning.
7. A job already running, or a run-completion job waiting for the slot — 409, with that job.

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

### Report documents (`AdminBenchmarkReportDocumentsController`)

- `GET /api/admin/benchmark/report-documents?suiteId=&runId=&take=`: List documents, without rendered
  text, each with `runChangedSinceGeneration`.
- `GET /api/admin/benchmark/report-documents/{id}`: Detail: metadata, validation notes and the facts JSON.
- `GET /api/admin/benchmark/report-documents/{id}/render?disclosure=summary|detailed|full&peers=named|anonymized`:
  The rendered Markdown (`text/markdown; charset=utf-8`), deterministic, with no model call; 400 for a
  refused combination.
- `GET /api/admin/benchmark/report-documents/{id}/render/pdf?disclosure=&peers=&paper=a4|letter&inline=`: The same
  document as a PDF (`application/pdf`), named `[run-<id>_]<title>_<disclosure>_<peers>[_INTERNAL].pdf`
  (the prefix for a `run:<id>` subject, § 8); the same refusals as `render`, 400 for another `paper`,
  413 over the size limit. A Report for AI Researchers and Developers is named
  `[run-<id>_]<title without its "— <document name>" ending>_Researcher_Report_<disclosure>_<peers>[_INTERNAL].pdf`,
  whether the stored title ends in the current name or the legacy *Technical Report*. With
  `inline=true` the response carries `Content-Disposition: inline` with the same file name, so a
  browser tab shows the PDF rather than saving it; the PDF viewer's *Open in new tab* uses it (§ 11).
- `GET /api/admin/benchmark/report-documents/{id}/render/docx?disclosure=&peers=&paper=a4|letter`: The
  same document as Word
  (`application/vnd.openxmlformats-officedocument.wordprocessingml.document`), named
  `[run-<id>_]<title>_<disclosure>_<peers>[_INTERNAL].docx` (with `_Researcher_Report` as for the PDF),
  with the PDF endpoint's refusals.
- `DELETE /api/admin/benchmark/report-documents/{id}`: Delete a document; its run rows cascade. Deleting
  a run-completion document also settles its run's status (§ 11); unlike the run endpoint above, this one
  does not refuse while the run's documents are being written.

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
  deleted (§ 11).

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
  for the Executive Summary). An (i) button after the tabs opens *What Summary, Detailed and Full mean*,
  a modal explanation of the three levels (`report-disclosure-guide.ts`, shared with the Download
  Center's *Disclosure* column). It has page
  navigation, zoom, a selectable text layer, **Download PDF** (under the server's file name) and **Open
  in new tab**, a real same-origin URL with `inline=true`. No PDF is framed or embedded, so the CSP is
  unchanged.

**Written once, rewritten after a delete.** A run-completion document is immutable like every other:
downloads only render it. A later re-synthesis or re-score marks it *Run changed since this document was
written* and does not rewrite it. To write it again, delete it — the tab's **Delete**, after a
confirmation, or `DELETE /api/admin/benchmark/runs/{runId}/report-documents/{documentId}`, which is
refused while the run's documents are being written — and use **Write Reports**. Deleting a run's
run-completion document through either delete endpoint returns the run's status to **NotRequested** with
no message, unless a job is in progress, so the status never describes a document that is gone; the run
keeps its report writer. After a delete the tab clears the writer picker when it held the model that
wrote the deleted document, so a rewrite starts from a deliberate choice.
