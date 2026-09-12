---
name: server_rubric_handoff
description: >-
  Mandatory method for turning a Suite Defect finding from an Overseer AI benchmark analysis
  into a rubric edit a human can paste into the suite editor. Covers why the agent never edits a
  rubric itself, how to obtain the current rubric text (it lives only in the database, so the
  user runs a query), the pre-flight checks before a replacement is written (verify every fact
  on disk with a citation, the units rule, scope, no rubric point copied from a candidate
  answer), the copy-paste deliverable (the whole rubric verbatim, never a diff or a paraphrase),
  what the harness does on save (item revision bump, assessed difficulty cleared, review mark
  invalidated, launcher refusal until re-assessed), the human steps in the Admin UI, and what to
  record in the registry afterwards. Read whenever an analysis files a Suite Defect that needs a
  rubric change, or a user asks for a rubric edit they can copy-paste.
---

# Rubric Handoff: Turning a Suite Defect Into an Edit a Human Can Paste

## 1. Purpose and When It Binds

A rubric is `BenchmarkQuestion.ExpectedPoints` — one free-text column per question, injected
into the assessor prompt verbatim between `--- BEGIN RUBRIC ---` and `--- END RUBRIC ---`
(`BenchmarkAssessmentPrompt.cs`, the `expectedPoints` block). It is ground truth for the grader,
so a wrong rubric point docks a correct answer on every run of the suite, and the blind second
reader, grading against the same text, agrees with the mistake. Run 38 Q7 is the worked example
(§ 7).

**An agent never edits a rubric.** There is no write path from this repository to the database
that is not the Admin UI, and there should not be one: rubric authorship is a human act the
harness records (`BenchmarkRubricAdditionAcceptance` stores who accepted what, and whether the
text was edited from a draft). What the agent produces is the **complete replacement text**, in
a form the human pastes without further thought, plus the facts that make it right.

This skill binds whenever a benchmark analysis files a **Suite Defect**
(`server_benchmark_to_chat_transfer` § 2, category 2) whose repair is a rubric change, and
whenever a user asks for a rubric edit to copy-paste. It is not one of the five skills every
benchmark analysis must read; the analysis reaches it through the Suite Defect category.

## 2. Get the Current Text First — It Lives Only in the Database

A report never carries the rubric; it carries the assessor's **paraphrase** of the points it
charged (*"Rubric point 3: …"*). A replacement written from the paraphrase rewrites points the
grader never quoted and drops the ones it did not mention. So the first step is always the real
text, and the agent cannot read it: follow `database_queries` and ask the user to run, in SSMS
against the Overseer database:

```sql
SELECT q.Id, q.BenchmarkSuiteId, s.Name AS SuiteName, q.OrderIndex,
       q.ItemRevision, q.Difficulty, q.AssessedDifficulty, q.ReviewedAtRevision, q.IsGenerated,
       q.QuestionText, q.ExpectedPoints
FROM BenchmarkQuestions q
JOIN BenchmarkSuites s ON s.Id = q.BenchmarkSuiteId
WHERE q.BenchmarkSuiteId = <suite id> AND q.OrderIndex = <report question number>;
```

**The report's question number is `OrderIndex` itself** — `BenchmarkReportBuilder` prints
`Question {OrderIndex}` — so "Question 7" is `OrderIndex = 7`. Do not subtract one: the first
handoff written from this skill did, fetched Question 6, and cost a round trip. Confirm the row
by its `QuestionText` and its `AssessedDifficulty` against the report before using it. Ask for
the result with headers, and treat `ExpectedPoints` as the document to edit — line breaks and
any Markdown in it are part of the text the grader sees.

**House format.** Suite 6's rubrics follow one shape, and a replacement keeps it: a
`**REQUIRED** (accuracy + completeness)` bullet list, a `**CRITICAL ERROR** (set
\`criticalError\`) — when the answer:` list, `**SCOPE** (conciseness)`, `**FORM** (readability)`,
and a `**SOURCE**` line naming files and line numbers. Only REQUIRED and CRITICAL ERROR points
are ever charged; SCOPE and FORM are recorded under the `OUT-OF-SCOPE:` / `FORM:` markers and
not deducted for. Put a changed fact's citation into SOURCE as well as into the point.

## 3. Pre-Flight Checklist

Every item is done before the replacement is written, and its result is stated beside the
deliverable.

- **Read the rubric point against the game source on disk**, not against the candidate answer
  and not against the wiki alone. Resolve `SourceCodePath` per `server_tool_data_sources` § 2,
  print only file names and line numbers, and quote the lines the new point rests on. A point
  that names a formula cites the function that applies it, not only a data table
  (`server_benchmark_to_chat_transfer` § 11, the verifier caution — the same failure mode
  applies to rubric authors).
- **Apply the units rule.** A rubric point states player-visible values, or says explicitly
  which internal unit it uses. Run 28 Q3 charged a critical error because the rubric quoted
  `objects.c` macro *arguments* (base AC 1, spellcasting penalty 5) as if they were what the
  player sees. Where the two differ, write both and label them.
- **Scope.** The question defines the scope (`BenchmarkAssessmentPrompt.cs`, the
  `OUT-OF-SCOPE:` rule). A rubric point the question did not ask for is recorded, not charged,
  so do not add one to "help" the grader; remove or mark as out of scope any point the analysis
  found charged that way. Do not add length or presentation expectations the candidate cannot
  see — the candidate is graded under the production concise prompt, and a `FORM:` criterion
  is not a Readability criterion.
- **Never copy a candidate answer's sentence into the rubric.** A point derived from a graded
  answer is Goodhart's law in the instrument (`server_benchmark_to_chat_transfer` § 8 rule 1).
  Derive the point from the source; if a candidate happened to be right, the source says the
  same thing in its own words.
- **Keep every other point byte-for-byte.** The deliverable replaces one field, so the parts
  the analysis did not touch are copied from the query result unchanged — same order, same
  numbering, same line breaks. State which point(s) changed.
- **Say what the edit does to the item** (§ 5), so the human is not surprised when the launcher
  refuses the next run.
- **Mirror the edit into the seed file — but only for a default suite.** See § 3a for the rule,
  how to tell a default suite from a custom one, and what the handoff says when it is custom.

## 3a. The Seed File Moves With the Database — For Default Suites

From harness 24, default suites are discovered from one file per suite under
`Overseer/Data/DefaultSuites/<key>.json` (`DefaultSuiteCatalogService`), each carrying its own
`key` and `version` alongside `name`, `description` and `questions`. **Importing never overwrites
a row already in the database** (`docs/overseer/ai-benchmark.md`, *Default Suites*), so a rubric
fixed only in the database comes back wrong the moment someone deletes that suite and re-imports
its key — the file still carries the defect.

**The rule:** when the changed question belongs to a suite that was imported from a default
suite, the matching entry in that suite's `Overseer/Data/DefaultSuites/<key>.json` is updated in
the same round — a single-file edit, JSON-escaped (`\n` for line breaks, `\"` for quotes), with
the file's existing LF line endings and no BOM, verified by a `ConvertFrom-Json` round-trip before
and after. This is a repository edit the agent makes itself; it is one file and needs no plan.
**When the suite is a custom suite, there is no seed entry and no file to edit — the handoff says
so explicitly**, rather than staying silent about the check having been made.

**How to tell, in order of preference:**

1. **The run report's manifest line *Suite origin*, or the run's `DefaultSuiteKeyUsed` column**
   (harness 24 onward) — the most direct evidence, because it names the exact key the run's
   questions came from at launch time.
2. **The suite row's `DefaultSuiteKey`** — add the column to the § 2 query
   (`SELECT … s.DefaultSuiteKey … FROM BenchmarkSuites s …`). Present and non-null means the suite
   was imported from that key; null means either a custom suite or one imported before harness 24
   (the column is not backfilled).
3. **For an older run or an unbackfilled suite, a `SuiteName` match against a catalog file's
   `name`** — stated in the handoff as an **inference**, never as a fact, because a suite can be
   renamed after import and two files could plausibly share a display name.

Applies to every change of a default-suite question, not rubric edits alone: question text, band,
an added or deleted question, or an accepted rubric addition (§ 5) all move the file the same way.

## 4. The Deliverable

One fenced block containing the **entire** new `ExpectedPoints` text, ready to select-all and
paste over the field. Never a diff, never "replace point 3 with…", never a paraphrase. Beside
it, in prose:

- the suite and question (name, number, `Id`, `ItemRevision` read from the query);
- which point(s) changed and why, with the source citations from § 3;
- the human steps (§ 6) and the consequences (§ 5);
- the pre-declared criterion that says the defect is closed on the next run (for example:
  *the assessor no longer charges Accuracy on Q7 for a table matching `src/zap.c:361-364` plus
  `:949` at 5 % per point*);
- **the seed-mirror step**, when § 3a's check finds this is a default suite: the
  `Overseer/Data/DefaultSuites/<key>.json` entry the agent updates, alongside the deliverable, in
  the same round. When § 3a's check finds a custom suite, this bullet says so instead — *"custom
  suite; no seed file to mirror"* — so the check is visibly made rather than silently skipped.

If the query result has not been received yet, say so and deliver the block only after it has.
A "full rubric" reconstructed from a report paraphrase is not the deliverable; it is a guess
that overwrites text nobody has seen.

## 5. What the Harness Does When the Rubric Is Saved

Verified in `AdminBenchmarkController` (the question editor and `AcceptRubricAddition`) and
`BenchmarkQuestionAssessment.Clear`:

- **`ItemRevision` is incremented.** An edited question is a *different item*; every stored
  answer records the revision it was graded against (`BenchmarkRunAnswer.ItemRevisionUsed`) and
  item analysis groups by it. Earlier runs' Q-statistics do not straddle the edit.
- **The assessed difficulty is cleared** — `AssessedDifficulty` and every
  `AssessedDifficulty*` snapshot column become null. **`BenchmarkRunLauncher` then refuses to
  launch the suite** (*"… question(s) without an assessed difficulty. Assess question difficulty
  for the whole suite before running a benchmark."*) until the human re-runs **Assess
  Difficulty**. The new assessed difficulty may differ from the old one, and assessed difficulty
  is the Intelligence Index weight for that question — say so in the handoff.
  - **Assess Difficulty also moves this weight on an item nobody edited** — running it again on a
    question whose `AssessedDifficulty` is already set re-assesses and overwrites it, with **no**
    `ItemRevision` bump (H5, run 39). Two runs of a suite can therefore agree on every item
    revision and still have been weighted by two different exams. From harness 24 this is a
    Fundamental comparability key of its own, `SuiteAssessedDifficulties`
    (`BenchmarkComparabilityKey.AssessedDifficultiesKey`), read alongside `SuiteItemRevisions`
    rather than folded into it — record both old and new `AssessedDifficulty` in § 8 even when
    `ItemRevision` did not move.
- **The review mark is invalidated** for a generated question: `IsReviewed` holds only while
  `ReviewedAtRevision == ItemRevision`, so the human re-marks the question reviewed after the
  edit (`ReviewedAtRevision` is set to the current revision by the review endpoint).
- **`AcceptRubricAddition` appends; the question editor replaces.** The suite-health "accept
  rubric addition" path adds the accepted text after the existing rubric. A repair that
  *changes* a point goes through the question editor (**Edit question**), pasting the whole
  field — which is why the deliverable is the whole field.
- **It is a suite comparability break** on that question. Runs before and after the edit are
  not comparable on it; the registry entry (§ 8) records the revision and the run from which
  the new text applies.

## 6. The Human Steps (Admin UI)

1. Admin → **AI Benchmark** → the suite → the question → **Edit question** (the pencil action
   button on the question row).
2. Select all of **Expected answer criteria** and paste the block from § 4. Save.
3. On the suite, **Assess Difficulty** (the suite-level action; it re-assesses only questions
   whose assessed difficulty is null). Wait for the assessment to complete.
4. If the question is generated, mark it **reviewed** again.
5. Note the question's new `ItemRevision` and `AssessedDifficulty` (the same query as § 2), for
   the registry entry.
6. **If § 3a found this is a default suite**, the agent — not the human — mirrors the saved text
   into the matching `Overseer/Data/DefaultSuites/<key>.json` entry in the same round; for a
   custom suite this step does not apply and the handoff says so.

## 7. Worked Examples

### Run 38, S9 (Q7, GnollHack Player Assistance Benchmark Suite)

The rubric's point 3 listed the general Wisdom-save skill modifiers (+15/0/−15/−30/−45/−60 %).
The Fear spell adds an extra `save_adj -= 2 * (skill_level - P_UNSKILLED)` on top of the general
`-3 * (max(0, skill_level - 1) - 1)` term, and one point of `save_adj` is 5 %
(`src/zap.c:943-949`, `:361-364`; `src/mhitu.c:1517`). The correct effective table is
+15 / −10 / −35 / −60 / −85 / −110 %. The candidate wrote exactly that, was docked to Accuracy
3/6, the blind second reader agreed, and the synthesis called the numbers "fabricated". Four
runs recorded the defect before it cost a score. The wiki (`Saving Throws.md`, section *Extra
Penalty for Slow, Hold, and Fear Spells*) had carried the right table since the run-36 round;
only the rubric had not moved.

### Run 39, S1–S3 (Q1, Q3, Q18, GnollHack Player Assistance Benchmark Suite)

Three defects on one suite, each fourth-or-later observation, each closed the same way: read the
source, replace the point, cite the line. **Q1** (`Id` 88) charged a critical error for a true
Gnoll intrinsic — the CRITICAL ERROR clause banned invented intrinsics generally, and REQUIRED
never listed lycanthropy resistance, so a correct answer tripped it (`src/attrib.c:117-119`,
`gnl_abil[]`; `src/u_init.c:1475`). **Q3** (`Id` 90) is the same units-rule shape as run 28 (§ 3):
the CRITICAL ERROR clause forbade *"NetHack's … base AC of 9"*, but 9 **is** the GnollHack
player-visible AC bonus (`objects.c:1005`'s `10 − ac`), so the clause forbade GnollHack's own
number under a NetHack label. **Q18** (`Id` 105) had it backwards: the CRITICAL ERROR clause
penalised describing erosion-on-attack, which is exactly what GnollHack's `u_wipe_engr` does on
every melee hit (`src/uhitm.c:602`, `src/engrave.c:304-323`), and the REQUIRED point it should
have carried — the wear-by-engraving-type rule — had been miscopied from `include/engrave.h`'s
type *constants* as if they were durability figures.

All three rows queried at `ItemRevision` 1, byte-identical to the seed file at query time — no
edit had reached either the database or the file since the suite was authored, which is what let
the same three defects reach a fourth run undetected. All three are default-suite questions (the
suite's `SuiteName` matched the seed's `name`, the only test available before harness 24 added
`DefaultSuiteKeyUsed`), so § 3a's mirror applied to all three: the same replacement texts were
written into `Overseer/Data/DefaultSuites/gnollhack_player_assistance.json` in the same round the
deliverable was produced, verified by the `ConvertFrom-Json` round-trip.

## 8. After the Edit

When the human has saved, re-assessed and (if generated) re-reviewed the question, record in
`server_benchmark_to_chat_transfer` § 11, on the run that next uses the suite:

- suite, question, old → new `ItemRevision`, old → new `AssessedDifficulty`;
- the run number from which the new text applies, and that the question is a comparability
  break against earlier runs;
- the pre-declared criterion from § 4 and whether the next run met it;
- **whether the seed file was mirrored** (§ 3a) — the `Overseer/Data/DefaultSuites/<key>.json`
  entry updated, or *"custom suite; no seed file"* when it does not apply. A default-suite repair
  recorded without this line leaves the next reader unable to tell whether a re-import would
  bring the defect back.

A rubric repair is exempt from the re-run requirement of § 9 (it adds no instruction to the
candidate), but it is not exempt from being recorded: a later reader comparing two runs on that
question must be able to see the break.

## 9. Cross-References

- [`server_benchmark_to_chat_transfer`](../server_benchmark_to_chat_transfer/SKILL.md) — § 2
  category 2 (Suite Defect), § 8 (anti-overfitting, rule 1), § 11 (where the edit is recorded;
  the run-28 units rule and the verifier caution)
- [`server_wiki_handoff`](../server_wiki_handoff/SKILL.md) — the sibling handoff for rung-2 wiki
  findings; the same division of labour, a different store
- [`database_queries`](../database_queries/SKILL.md) — how the user runs the § 2 query
- [`server_tool_data_sources`](../server_tool_data_sources/SKILL.md) — § 2 resolving
  `SourceCodePath` for the on-disk source reading, § 3 secrets hygiene
