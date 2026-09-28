# Report Packs — AI-Written Documents about One Model

A **report pack** is a set of up to three documents about one model of a Model Comparison, written in
the context of the other models compared with it. The documents are for three different readers: the
model's provider or a manager, AI researchers and model developers, and the Overseer team itself.

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
| The eleven validation rules and the drop policy | `BenchmarkReportPackValidator` |
| Generation: preparation, the writer call, repair, storage | `BenchmarkReportPackService` |
| The background job and its progress | `BenchmarkReportPackJob`, `BenchmarkReportPackJobManager` |
| Deterministic Markdown rendering | `BenchmarkReportPackRenderer` (pure, static), behind `BenchmarkReportRenderService` |
| Endpoints | `AdminBenchmarkReportPacksController`, `AdminBenchmarkReportDocumentsController` |
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
| **Executive Summary** | A non-specialist at the model's provider, or a manager. Plain language, short sentences, no jargon | Any level | Headline (one sentence) · key figures · *What it did well* (at most 3) · *Where it fell short* (at most 3) · *What this means for use as a game assistant* · *How confident are we* |
| **Technical Report** | AI researchers and model developers. Precise and neutral | Any level | Headline · abstract (at most 150 words) · figures against the peers · *Why it scored as it did* (failures by category) · *What worked* · strengths and weaknesses (at most 8 each) · question topics for every question · a note for each question more than 15 points below the peer mean or with a critical error · recommendations for model developers |
| **Internal Improvement Brief** | The Overseer team and its AI agents. Direct and practical | **Full only**; internal | Headline · three parts in the order of *What the Benchmark Is For*: the Overseer chat and its tools, the benchmarking system, the model's result · strengths and weaknesses · question topics and notes · recommendations for the chat, the benchmark or model developers · **leads** |

The writer fills named **slots** and **lists** only; the renderer supplies every heading, table and
figure. The slot ids are in `BenchmarkReportSlots`: `meaning` and `confidence` (Executive Summary);
`abstract`, `whyItScored` and `whatWorked` (Technical Report); `overseerChat`, `benchmarkSystem` and
`modelResult` (Internal Brief). The *Why it scored as it did* categories are domain knowledge, reading
the game state, tool use, instruction following, completeness under the concise answer style, and
calibration; a category the data does not support is left out.

The prose is written once and must be safe at every disclosure level: the same stored output renders
for the provider, with questions described rather than quoted and peers anonymized, and for the team at
Full.

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

---

## 3. Validation, Repair and Drops

`BenchmarkReportPackValidator` checks the writer's JSON against eleven rules. Each failure is a
`BenchmarkReportValidationNote` with its rule number, location (`headline`, `sections.abstract`,
`weaknesses[1]`…) and message.

| # | Rule |
|---|---|
| 1 | The output is JSON of the expected shape, and every required slot is present and non-empty |
| 2 | Every `{{…}}` token exists: a fact key, `{{subject}}` or a known peer letter |
| 3 | No bare digits in prose after masking tokens, question references and known names |
| 4 | Every question number exists, and the question topics cover every question where the document requires them |
| 5 | Every evidence id exists, and every strength, weakness and lead cites at least one |
| 6 | A strength may not cite a weakness row and a weakness may not cite a strength row; a finding citing only Conflicting rows must say the graders disagree |
| 7 | Length limits: headline at most 35 words, at most 3 strengths and 3 weaknesses in the Executive Summary, a question topic at most 12 words |
| 8 | No headings, tables or HTML inside any text |
| 9 | No run of 8 or more words shared with any question, rubric, answer or grader-evidence text |
| 10 | No peer names, model ids or providers in prose |
| 11 | No significance language: *significant*, *significantly*, *statistically*, *reliably better* or *worse*, *clearly outperforms* |

**Repair and drop policy.**

1. When any rule fails, the writer gets **one repair turn**: every issue, then the output rules in brief
   (`BenchmarkReportPackPrompt.BuildRepairMessage`).
2. What still fails after the repair is **dropped** — the item or paragraph is removed from the stored
   output, the note records `Dropped`, and the document is stored as **CompletedWithWarnings**.
3. A missing headline or an empty required slot cannot be dropped around: the **document fails** and no
   row is stored.

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

**Recommended setting.** A **strong model — the tier recommended for scoring roles, Claude Opus or GPT
Sol, not the provider's top tier (Claude Fable, GPT Astra)** — **from a family other than the subject's**,
at **medium** thinking or reasoning effort. Use **high** only if its documents often need the repair turn
or lose items to validation; never **low**, because the writer must follow a strict schema and token
rules. Not an economy tier (Flash, Flash-Lite): the documents go to people outside the team, and one call
per document keeps the cost small. The same recommendation is in `ai-benchmark.md` § 3 *Choosing grader
models and effort*, the *How the graders work* guide and the dialog's writer info tip.

**Usage and guards.** Each writer call is recorded in `SystemAiUsageLog` with `RoleContext = 8`
(Report Pack). While a job runs, the writer configuration cannot be deleted: the usage guard reports a
blocker of kind `reportPackJob`. The start is refused with 429 when
`BenchmarkComplianceGuard.CanSpendAsync` denies it, and with 409 while another report-pack job is
running — one job at a time.

---

## 5. Storage and the Scoring Fingerprint

Each document is one **immutable** `BenchmarkReportDocument` row; there is no update endpoint. It holds:

- audience, subject key (`run:<id>` or `group:<id>`) and label, the subject's run ids, the comparison
  request, and the suite;
- the writer's identity and its configuration snapshot, and `SameProviderAcknowledged`;
- `ReportFormatVersion`, the writer prompt's SHA-256 and `AnswerExcerptChars`;
- `FactsJson` — the fact sheet;
- `ContentJson` — the verbatim content the renderer may print: question text as asked, rubric as
  graded, answer excerpts cut at generation, grader evidence and verifier rulings, all taken from the
  subject's **answer rows**, never from the live suite, so a later suite edit cannot change a document;
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
| **Detailed** | Verbatim, for the questions the notes discuss | Never | Excerpts | Never | *Confidential. Prepared for the model's provider. Contains benchmark questions — do not publish.* |
| **Full** | Verbatim, every question | Verbatim | Excerpts | Verbatim | *INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.* |

**Peer naming** is *Named* or *Anonymized*. Anonymized prints peers as *Model A*, *Model B*…, removes the
provider column, and replaces the peers' model ids and providers in every table. The subject is always
named.

The Executive Summary and Technical Report render at any level with either naming. The Internal Brief
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
| **Internal package** | Every available document: pack documents, the run report, the tool-call log, run diagnostics | Full | Named | Markdown and HTML |
| **Provider package** | Executive Summary and Technical Report only; internal-only rows are listed but unselectable, with their reason | Summary (Detailed as an option) | Anonymized (Named as an option, with a warning) | HTML and Markdown |
| **Custom** | Any selection | Per document | Per document | Either |

**File names.** A pack document at Full, and every internal-only file (run report, tool-call log,
diagnostics), gets an `_INTERNAL` file-name suffix. The run report and tool-call log are fetched from the
existing run endpoints and keep the server's file name. Run diagnostics are a point-in-time capture,
labeled *captured now*.

**ZIP and manifest.** Several files download as one ZIP, `<model>_<package>_<yyyyMMdd_HHmmss>.zip`, with a
`MANIFEST.md` listing each file's name, document id, audience, disclosure, naming, renderer version,
creation time, writer and SHA-256. The packaging time appears only in the manifest and the ZIP name, so
the documents themselves stay byte-identical across downloads.

**HTML.** HTML is built client-side from the rendered Markdown by a converter with its own private
`marked` and DOMPurify instances — never the chat pipe's global ones, whose options and hooks belong to
the chat. The output is self-contained, with print CSS, so *Print → Save as PDF* gives a document ready to
send.

---

## 9. Endpoints

All endpoints require the `AdminOnly` policy and sit under `api/admin/benchmark`.

### Report packs (`AdminBenchmarkReportPacksController`)

- `POST /api/admin/benchmark/report-packs/preview`: The fact sheet and prompts without a model call —
  subject, peers, estimated tokens and cost per document, the same-provider warning and any refusal.
- `POST /api/admin/benchmark/report-packs`: Start a job. Body
  `{ runIds, groupIds, pricingBasis, subjectKey, audiences[], writerModelConfigurationId, acknowledgeSameProvider }`,
  with audiences as numbers (1 Executive Summary, 2 Technical Report, 3 Internal Brief). Returns 202
  `{ jobId }`.
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
7. A job already running — 409, with the running job.

### Report documents (`AdminBenchmarkReportDocumentsController`)

- `GET /api/admin/benchmark/report-documents?suiteId=&runId=&take=`: List documents, without rendered
  text, each with `runChangedSinceGeneration`.
- `GET /api/admin/benchmark/report-documents/{id}`: Detail: metadata, validation notes and the facts JSON.
- `GET /api/admin/benchmark/report-documents/{id}/render?disclosure=summary|detailed|full&peers=named|anonymized`:
  The rendered Markdown (`text/markdown; charset=utf-8`), deterministic, with no model call; 400 for a
  refused combination.
- `DELETE /api/admin/benchmark/report-documents/{id}`: Delete a document; its run rows cascade.

---

## 10. What a Report Pack Does Not Do

- It changes no score, index, grading prompt or comparability key, and moves neither `HarnessVersion`
  nor `ScoringMethodVersion`.
- It runs no significance test and states none.
- It never reads the live suite: question and rubric text come from the subject's answer rows.
- It never re-writes a stored document. A changed run is flagged, not re-generated; generate a new pack
  if the old one is out of date.
