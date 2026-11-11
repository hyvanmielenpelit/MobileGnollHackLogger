---
name: server_wiki_handoff
description: >-
  Mandatory method for turning a rung-2 wiki finding from an Overseer AI benchmark (GnollBench)
  analysis, a rung-1 knowledge-base finding, or a wiki or knowledge-base fact from a feedback issue,
  into a two-step handoff document: proposed changes, one at a time, as text and diffs, and one
  orchestration prompt for a session on the GnollHackWiki clone at C:\hmp\GnollHackWiki (or the
  knowledge-base clone) that validates each change against the GnollHack source at C:\hmp\GnollHack
  with a fresh read-only validator subagent, applies it on a pass, revises and re-validates it on a
  fail, up to five rounds, and reports what was not applied. Covers pre-flight checks on the wiki
  clone (target page, match count, links, clean tree, line endings, no presumed generator), what
  prompts must not prescribe, the validation loop, the confirmation gate that waits for
  the session's report, and post-session records. Read with the other four benchmark skills before
  the first finding; act on it when a finding lands on rung 1 or 2.
---

# Wiki Handoff: Turning a Finding Into Changes That Are Validated and Applied One at a Time

GnollBench is the user's name for the Overseer AI benchmark.

## 1. Purpose and When It Binds

A rung-2 wiki finding (`server_benchmark_to_chat_transfer` § 7) leaves this repository as a
**two-step document** — **Step 1**, the proposed changes, one change at a time, and **Step 2**, the
**orchestration prompt** for a session opened on the GnollHackWiki clone at `C:\hmp\GnollHackWiki`.
That session validates each change against the GnollHack source clone at `C:\hmp\GnollHack` with a
fresh read-only **validator** subagent, applies the changes that pass, revises and re-validates the
ones that fail (§ 3b), and ends with a report. Neither the session nor its validators can ask the
analyst anything, so **the orchestration prompt is the whole interface to its session, and the
validator prompt inside it the whole interface to each validator**. § 3a fixes what the document
carrying them may contain, because a handoff that buries the human's short sequence among steps
the analyst could have taken is a second interface, and a worse one.

The division of labor is fixed:

- **The analyst does the pre-flight and writes the changes** — the checks only someone with the
  run context and read access to `C:\hmp\GnollHackWiki` can do: which page carries the fact, how
  many pages the edit touches, what the run actually saw.
- **A fresh validator subagent independently checks each change's mechanics against the GnollHack
  source**, read-only, before the page changes. It is never the author of the text it checks.
- **The orchestration session revises failed changes, applies passed ones, and reports.** It applies
  them with the wiki's own skills, which own the wiki's conventions — `wiki_editing` for link
  syntax, headings and emoji rules, `wiki_bulk_edits` for cross-page mechanics. The orchestration
  prompt cites them by name and does not restate them.
- **The human pastes one prompt, reviews what the session revised, and commits the result.**

**Why the validator reads the GnollHack repository and the session opens on the wiki.** What the
edit asserts are game mechanics, and the only authority on a mechanic is the game's source. So the
validator reads `C:\hmp\GnollHack` and nothing else: a reader that could open the wiki would compare
the page with other pages, which is how a wrong fact spreads. The session's project folder is the
wiki because that is where the edit and its editing skills are. The document's framing names both
clones (§ 4.1), so that the session's request to read the source clone is expected.

This skill is read with the other four benchmark skills on **every** benchmark analysis
(`.agents/AGENTS.md`, "AI Benchmark Findings"); its body is acted on only when a finding lands
on rung 2, or on rung 1 through the knowledge-base substitutions below.

**A knowledge-base edit uses the same document.** A rung-1 finding
(`server_benchmark_to_chat_transfer` § 7) follows §§ 2–4a with four substitutions. The document is
`knowledge_base_handoff_v<V>.md`. The pre-flight confirms that the clone's remote names
`hyvanmielenpelit/OverseerKnowledgeBase` and compares its HEAD with the run's `KnowledgeBaseHeadSha`,
reading the clone through `KbPath` and printing no path. The orchestration session opens on that
clone, named by repository only, and cites its `.agents/skills/kb_article_generation/SKILL.md` in
place of `wiki_editing` and `wiki_bulk_edits`. And the edit leaves the article's `summary:` line
unchanged unless the finding needs it, because `KnowledgeBaseService.GetTopicList` writes the
summary into the frozen prompt segment. The
quantified-clause rule, the validator template, the validation loop and the fence and diff
self-check apply unchanged. A rubric repair is not handed over this way (`server_rubric_handoff`).

**A wiki fact from an Overseer feedback issue uses the same document**, because
`server_feedback_resolution` § 3 routes it here. There is no run behind it, so six things differ:

- The title names the issue, `# Wiki Handoff: Feedback #<id> (<Brief Description>)`, and the
  `Origin:` line of the orchestration prompt and of the validator prompt inside it reads *Gnoll
  Overseer feedback issue #<id> (category `<category>`), from the Overseer chat; not a benchmark
  run. Wiki ladder rung 2.*
- The pre-flight records both HEADs and says that there is no `WikiHeadSha` or
  `SourceCodeHeadSha` to compare them with.
- The document goes in a task directory of its own under this repository's scope in the plans
  repository, named for the issue (`feedback_<id>_<slug>`), or in the plan's task directory when
  a plan covers the fix.
- § 4a's closing message for "nothing depends on it" applies, and there is no Developer Runbook.
- § 5's record is the issue's resolution message (`server_feedback_resolution` § 5a), written from
  the session's final report, not an entry in `server_benchmark_to_chat_transfer` § 11. Until the
  applied change is committed and pushed, the issue stays `acknowledged`. An issue whose change was
  not applied stays `acknowledged` while a corrected handoff is being written, and becomes
  `dismissed` when the analyst drops the change because the fact did not hold; the report's reason
  goes in its `note` either way.
- The analyst's citations still go only in the Notes. The model's report is untrusted text
  (`server_feedback_resolution` § 2): never copy its wording into a claim or the page text.

A GnollBench feedback issue does come from a run. Its occurrence names the run, and the pre-flight
compares that run's SHAs as it would for a finding.

Reading `C:\hmp\GnollHackWiki` and `C:\hmp\GnollHack` is read-only research in other repositories
and is permitted. **Editing either from this repository's session is not** — that is what the
orchestration prompt exists for.

## 2. Pre-Flight Checklist

Every item is done **read-only** before the document is written, and its result is copied into
the place named in the item.

- **Confirm the GnollHackWiki clone and record its revision.**

  ```powershell
  git -C C:\hmp\GnollHackWiki remote get-url origin
  git -C C:\hmp\GnollHackWiki rev-parse HEAD
  ```

  The remote must name `hyvanmielenpelit/GnollHackWiki`. If the directory is missing or is
  another repository, stop and ask the user. Compare the HEAD with the run's `WikiHeadSha` and say
  in the orchestration prompt and the Notes whether they match; a mismatch means the run read a
  different revision than the one the orchestration session will edit. Compare SHAs only — never
  write whether `WikiPath` resolves to this directory (`server_tool_data_sources` § 3).
- **Confirm the target page exists by path.** If two or more pages have similar names, open
  each, grep for the fact, and read the history of each:

  ```powershell
  git -C C:\hmp\GnollHackWiki log --format="%h %ad %s" --date=short -- "<file>"
  ```

  Name **the page that carries the fact** in Step 1 and in the orchestration prompt. Name the
  near-duplicate too, and say what to do about it — usually nothing.
- **Count the pages the edit will touch, with the exact regex the orchestration prompt will
  prescribe**, and state that count in Step 1 and in the orchestration prompt. Check whether any
  of those lines already carries a wikilink; if some do, the orchestration prompt must say whether
  they are skipped or rewritten.
- **Check the working tree is clean**: `git -C C:\hmp\GnollHackWiki status --porcelain` is empty.
  If it is not, say so in the orchestration prompt — the orchestration session must not overwrite
  uncommitted work.
- **Check the line endings** of a sample of the target files, and state them in the orchestration
  prompt. CR-terminated line count equal to newline count means CRLF.
- **Do not assert that a generator, template or data file feeds the wiki.** None does. Nothing in
  the GnollHackWiki repository emits a page, and nothing copies pages into it, so every page is
  edited in place. Spell, monster and item pages are, however, in the format that three
  wizard-mode commands of desktop debug builds of GnollHack write: `#wizsavespells`,
  `#wizsavemonsters` and `#wizsaveitems` (`src/cmd.c`, `src/do.c`). Each writes one Markdown file
  per entry, with hyphens for spaces in the file name, into `spells_for_wiki/`,
  `monsters_for_wiki/` or `items_for_wiki/` under the game's working directory. Those exports
  are not a deploy path. A fresh export copied over a page would erase every hand-made edit
  since, so the orchestration prompt never asks for one. A fact the export lacks on every page of
  a kind is still added page by page; making the game's own item description carry it is a
  separate GnollHack change, not a substitute for the wiki edit. If you looked and found nothing
  else, the orchestration prompt says "edit in place".
- **Every fact the edit adds is verified** against the game source or another primary text, and
  the citation proving it goes **in the handoff document's Notes and in the analysis — never into
  the text the page will carry, and never into the orchestration prompt or the validator prompt
  inside it** (§ 3; `server_benchmark_to_chat_transfer` § 7 rung 2 authorship rule). The wiki is
  player documentation and forbids source references on a page outright; `wiki_editing` § 4 and
  § 17 own that rule. Verification is what licenses an AI-authored wiki edit, and a reviewer checks
  it in the handoff, where it is read once, rather than on the page, which is read by every player
  forever. A link-only edit needs no citation beyond the target page. Validation is an
  independent second check, not a replacement for this one: the analyst still verifies before
  writing a claim.
- **Confirm the GnollHack source clone and record the revision the validators will read.**

  ```powershell
  git -C C:\hmp\GnollHack remote get-url origin
  git -C C:\hmp\GnollHack rev-parse HEAD
  git -C C:\hmp\GnollHack status --porcelain
  ```

  The remote must name `hyvanmielenpelit/GnollHack`. If the directory is missing or is another
  repository, stop and ask the user; do not substitute the value of `SourceCodePath`. Compare the
  HEAD with the run's `SourceCodeHeadSha` and say in the orchestration prompt and the Notes whether
  they match; the validator prompt inside it carries the revision. A dirty tree means a validator
  could read uncommitted source; say so. Compare SHAs only — never write whether `SourceCodePath`
  resolves to this directory (`server_tool_data_sources` § 3).
- **Split the finding into changes.** A change is **one edit to one page**, carrying its own
  claims: `W1`, `W2`, … for the wiki, `K1`, `K2`, … for the knowledge base. A bulk edit is one
  change per page, `W3.01`, `W3.02`, …, in the order of the Step 1 table. Two edits to the same
  page are two changes only when each makes sense alone; otherwise they are one.
- **Declare dependencies.** Each change states `Depends on:` — *none*, or the IDs whose text it
  needs (a link to a section another change adds; a sentence that extends one). Order the changes
  so every dependency comes first.
- **Name each change's purpose in one sentence**: what the chat got wrong, and the fact the page
  must carry. The orchestration session revises against it, so it is what keeps a revision from
  drifting into a different edit.
- **Number every claim, per change.** Each game mechanic a change asserts becomes one claim,
  `W1-C1`, `W1-C2`, … (`K1-C1` for the knowledge base), one sentence each, in the game's
  vocabulary. A bulk edit whose line carries a per-page value (run 34: the saving-throw attribute
  of each of 27 spells) is one change per page with its own claim, `W3.07-C1`, listed in the
  Step 1 table. A link-only edit still names the claim the link implies (that the linked mechanic
  applies to this page).
- **A quantified or comparative clause is a claim about every member of its set.** Words such as
  *any*, *every*, *all*, *always*, *never*, *as for*, *like other* on one entity's page assert
  something about entities the page is not about. Either rewrite the clause to speak about the
  page's own subject, or list the set's members as separate claims and verify each. Run 124's W3
  added *"as they may be to any chaotic character"* to `Races/Orc.md`. It was true for orcs and
  false for a chaotic human, it passed validation, and it produced run 125's Q9 critical error.
  The rule applies to every change, and to every revision the orchestration session writes (§ 3b).
- **Keep the diffs independent.** Each change's diff is produced against the page at the recorded
  HEAD with no other change applied, so it still describes the edit when another change is not
  applied. The one exception is a change that declares `Depends on:`, whose diff is produced on top
  of the changes it names. Two changes to one page that do not depend on each other may not share a
  hunk: if their hunks would overlap or come within three lines of each other, they are one change,
  or one declares the other as its dependency.
- **Produce the diffs without touching `C:\hmp\GnollHackWiki`.** Copy each target file to the
  session scratchpad twice — `<scratch>\before\<file>` and `<scratch>\after\<file>` — edit the
  second copy, and let Git write the diff to a file, so that PowerShell never re-encodes the
  page's dashes and emoji:

  ```powershell
  git diff --no-index --no-color --output="<scratch>\<file>.diff" -- "<scratch>\before\<file>" "<scratch>\after\<file>"
  ```

  Exit code 1 means the files differ; that is success. The output opens with **four** header
  lines, three of which carry the quoted absolute scratch path, user profile name included.
  **Delete the `diff --git` and `index` lines, and replace the `---` and `+++` lines with
  `--- a/<repository-relative path>` and `+++ b/<repository-relative path>`**, forward slashes,
  unquoted. A scratch path must never reach the document. If a body line ends in a carriage
  return, strip it: the diff specifies content, and the orchestration prompt states the line
  endings separately. Take the file from `C:\hmp\GnollHackWiki` at the recorded HEAD with a clean
  tree, so every context line is verbatim from the page the orchestration session will edit. The
  recipe produces one diff block per change.

## 3. What the Prompts Must Not Do

### The Validator Prompt

The prompt the orchestration session gives each validator subagent (§ 4.2).

- **Do not include the analyst's source citations, file names or reasoning.** A validator handed
  a file and a line reads that line and stops; it must locate the evidence itself. The citations
  are in the Notes, where a reviewer compares them with the validators'.
- **Do not let the validator edit, stage, commit, push or check out anything**, in any repository.
- **Do not let the validator settle a claim from NetHack source, the NetHack wiki or general
  NetHack knowledge.** GnollHack diverges from NetHack, and a claim is CONFIRMED only by the
  GnollHack source at the recorded commit.
- **Do not accept a verdict without a citation**: every CONFIRMED and REFUTED carries `path:line`
  evidence at that commit.
- **Do not let the wording critique become a style review.** The validator checks facts. Its
  critique may block only on wording that **states or clearly implies a mechanic the source
  contradicts or does not support** — a false fact, a wrong condition, a missing exception that
  would make a player act wrongly. Tone, brevity, a phrasing the validator would have preferred, a
  detail that could be added, and a condition the text does not assert are not defects: they go
  under *Optional suggestions* and never change the verdict. How a page reads is owned by the wiki's
  own skills (`wiki_editing`) and settled by the orchestration session, which may reword as long as
  no mechanic changes. The § 4.2 template carries this rule; do not tighten it in a handoff. Its
  class-clause check (task item 3: a clause about *any*, *every*, *all*, *always* or *never* is
  checked against each member of the class) is a factual check and stays within this limit: a
  class clause that is false for one member states a false fact.
- **Give the validator one change** (or up to ten pages of one bulk edit), **never the change's
  earlier text, earlier reports or the orchestrator's reasoning.** A validator that sees why the
  text was revised checks the revision against the old objection, not against the source.
- **Never show the validator the analyst's Notes**, which carry the citations.
- **The validator is a fresh agent that starts from the validator prompt alone** — never a fork or
  a continuation of the orchestration session's conversation, and never a validator that was used
  before. An agent that inherits the conversation has seen the earlier text and reports.

### The Orchestration Prompt

- **Do not restate wiki link syntax, heading rules or emoji conventions.** `wiki_editing` owns
  them, it is longer and more current than any paraphrase, and the prompt cites it by name.
- **Do not prescribe `sed -i` or any Git Bash in-place editor.** MSYS `sed` rewrites every file
  it processes with LF endings, including files the pattern never matched. `wiki_bulk_edits`
  carries the PowerShell recipe that preserves them.
- **Do not ask the orchestration session to commit or push.** It leaves the changes in the working
  tree and prints the commands in its final report.
- **Do not write a verification step against a page name that was not confirmed in pre-flight.**
  A `grep -c` against a page that does not exist reports zero and reads as a failed edit.
- **Do not put source-code references in the page text the prompt prescribes** — no file paths,
  no line numbers, and no C identifiers, whether macros, struct fields, enum constants or
  function names. State the mechanic in the game's own vocabulary: *"monsters that are immune to
  fear"*, never `` `MR_FEAR` ``. The code locations belong in the document's Notes, below the
  prompt, which is what a reviewer reads. `wiki_editing` § 4 owns the rule; the prompt does not
  restate it, it simply must not ask for text that breaks it.
- **Do not verify with a grep for a code identifier.** A `Verify:` step reading
  `grep -c "MR_FEAR"` can only pass if a macro name reached a player-facing page, so the prompt's
  own verification enforces the defect the bullet above forbids. Grep for a distinctive
  player-facing phrase from the sentence instead.
- **Do not prescribe a heading or a table per finding.** One added fact is usually one sentence,
  extending what the page already says. `wiki_editing` § 17 owns the shape of a benchmark-driven
  edit; a prompt that dictates structure overrides it from the wrong side of the handoff.
- **Do not paraphrase the diffs.** The orchestration prompt carries the Step 1 diffs
  **byte-identical**, and the analyst checks that before handoff (§ 3a self-check).
- **Do not prescribe `git apply`.** The diffs specify the intended result; the edit is made by the
  mechanism `wiki_bulk_edits` prescribes, which preserves CRLF. Where `wiki_editing` improves the
  wording, the session writes the readable version and reports the deviation — **but it may not
  add, drop or alter a mechanic**, because the wording was validated and the rewording is not: a
  rewording that would change a mechanic is a revision and goes back to validation (§ 3b).

## 3a. The Document Contract: Two Steps, Then Notes That Need No Action

The deliverable is **one file** in the round's plans task directory,
`wiki_handoff_prompt_v<V>.md`. It is a member of the round's document set, so revising it bumps
every member to the same version.

**It contains these parts, in this order, and nothing else:**

1. **At most three sentences of framing** at the top: what the document is for, and the human's
   sequence — paste Step 2 into a new chat whose project folder is `C:\hmp\GnollHackWiki`; allow it
   to read `C:\hmp\GnollHack` if it asks; when it finishes, review the changes its report marks as
   revised or reworded, commit and push what it applied, and paste the report into the analyst's
   session. § 4.1 has the sentences.
2. **Step 1 — Proposed changes.** Two subsections, in this order:
   - **In plain text**: per change — its ID, `Depends on:`, its purpose, the repository-relative
     path of the page, where on the page the change goes, the exact text added or replaced, and
     the numbered claims it asserts. Name any near-duplicate page and say it is left unchanged.
   - **As diffs**: one fenced `diff` block per change, labeled with its ID, unified format, three
     context lines, produced by the § 2 recipe. **Bulk edits** (more than five pages sharing one
     line form): full diffs for three representative pages — the first, the last, and one
     exception (a line that already carries a wikilink, or a skipped page) if any — plus a table
     listing **every** page with its change ID, its per-page value and its claim. No page is
     omitted from the table.
3. **Step 2 — the orchestration prompt**, one fenced block, self-contained, template § 4.3. It
   carries the changes, their diffs, the validator prompt (§ 4.2) and the form of the final report
   (§ 4.4).
4. **Notes that require no action from the human**, under a heading that says so — the § 2
   pre-flight results, including the GnollHack HEAD comparison; the change list with its
   dependencies; the analyst's source citations for each claim; that the orchestration session
   validates, revises and applies; what to do when the session was interrupted (continue in the
   same chat, or restore the pages it changed and paste Step 2 again); what the analyst does with
   its report; the § 5 criterion. These are read, reviewed and filed; nothing in them is a step.

**Fence nesting.** A fence closes at the first line of at least as many backticks as opened it,
so **an outer fence is one backtick longer than the longest fence inside it**. A `diff` block
takes three. The validator prompt and the report form inside Step 2 contain no fence and take
three. The Step 2 prompt, which contains them and the `diff` blocks, takes four; anything quoting a
whole document, such as § 4.1 below, takes five. If a page being diffed itself contains a line
that begins with three backticks, the `diff` block takes four and everything around it one more.

**Version binding.** The orchestration prompt names the document and its version. Revisions the
session makes do not create a new document version; its report records them. A **new** version is
written only by the analyst, for example to retry changes that were not applied (§ 4a).

**It carries no step, checklist or instruction addressed to the human beyond this sequence:**
paste Step 2; allow the directory if asked; review, commit and push; paste the report back. The
document carries no other step. Four creep back most often:

- no *"first check…"* pre-steps — those are § 2, and the analyst has already done them;
- no post-steps beyond the review, the commit and the report — see the commit exception below;
- no *"verify the change landed"* step — the session verifies each change itself (§ 3b), and the
  analyst checks read-only in § 4a;
- no request to wait inside the document — the wait is requested **in chat** (§ 4a), which is
  where the user answers.

**The reassignment rule.** Any piece of work that surfaces while the handoff is being written has
exactly four legitimate destinations: **(a)** the analyst, before the document is written;
**(b)** the orchestration session, stated in Step 2; **(c)** a validator, stated in the validator
prompt inside Step 2; **(d)** the analyst, after § 4a. There is no fifth. A step that appears to
need the human beyond the sequence above has been put in the wrong place.

**Self-check before handoff.** The claim lists in Step 1 and in Step 2 match change by change;
every change ID appears in both in the same order with the same `Depends on:`; a bulk table is
identical in Step 1 and Step 2; and the diff blocks are byte-identical in both, which this checks
(for a four-backtick `diff` fence, lengthen both fence patterns to match):

```powershell
$text = [IO.File]::ReadAllText('<absolute path to wiki_handoff_prompt_v<V>.md>')
$m = [regex]::Matches($text, '(?ms)^```diff\r?\n.*?^```[ \t]*\r?$')
$half = $m.Count / 2
if ($m.Count -eq 0 -or $m.Count % 2 -ne 0) { "FAIL: $($m.Count) diff blocks" }
else {
    $bad = 0
    for ($i = 0; $i -lt $half; $i++) { if ($m[$i].Value -cne $m[$i + $half].Value) { $bad++; "FAIL: diff block $($i + 1) differs" } }
    if ($bad -eq 0) { "OK: $half diff block(s), identical in Step 1 and Step 2" }
}
```

**The one exception, stated rather than omitted: the commit and push in the wiki clone.** No agent
commits there. The framing names the commit in one clause, the § 4.4 report prints the commands,
and the document does not restate them.

**Document versus chat message.** The human's sequence is part of the document — the framing
sentences — because what to do with the report cannot be followed from a chat message that has
scrolled away. What lives only in the **chat message** is the § 4a wait request, which is where
the user answers it. The document must still read correctly on its own to someone opening it a
month later, which is what the framing sentences are for.

The rule that the document carries no avoidable step for the human was set on 2026-09-13 by the
user's instruction, not by an incident. Its purpose is that the one action that matters does not
compete for attention with steps the agent could have taken itself. The three-step shape was set
on 2026-09-18 by the user's instruction, so that every mechanic reaching a player-facing page is
checked against the GnollHack source by a session that did not write it, before the page changes.
The limit on the wording critique — blocking issues are factual and carry a citation, everything
else is an optional suggestion — was set on 2026-09-19 by the user's instruction, after validations
failed on wording that was true and within the wiki's guidelines, and one second-round validator
dismissed the first round's critique outright. The two-step shape, with each change validated and
applied on its own, replaced it on 2026-11-11 (§ 3b).

## 3b. The Validation Loop

The orchestration session runs this loop; the § 4.3 template carries it as instructions. A
**round** is one validation attempt: round 1 validates the analyst's text, rounds 2–5 validate up
to four revisions.

1. **Before the first change**, the session checks four things and, if one fails, changes nothing,
   validates nothing and ends with one line, `HANDOFF NOT STARTED: <reason>`:
   - its `origin` remote names the expected repository;
   - `git status --porcelain` lists none of the target pages (otherwise an earlier run was
     interrupted, or the pages carry other uncommitted work), and HEAD is the recorded revision or
     no target page changed since it (`git diff --quiet <sha> HEAD -- <target pages>`); the report
     names the revision it edited;
   - the recorded GnollHack commit exists in `C:\hmp\GnollHack` (`git cat-file -t <sha>` prints
     `commit`; the form with `^{commit}` is avoided because shells treat its braces and caret
     specially) and a file under it can be read. The clone's HEAD need not be that commit:
     validators and the reviser read at the recorded commit;
   - it can spawn subagents. No fallback validates in the orchestrator's own context, because the
     independence of the validator — a reader that did not write the text — is what the validation
     is for.

   It keeps the `git status --porcelain` output as its **baseline**.
2. **Per change, in document order**: if a `Depends on:` change was not applied, the change is
   skipped. Otherwise round 1 validates the analyst's text with a fresh validator (§ 4.2). After
   each change the session prints one progress line, so an interrupted session shows where it
   stopped.
3. **Reading a report** — mechanically, not by judgment:
   - **Usable PASS**: the verdict line is `VALIDATION <id>: PASS`, the table gives every claim of
     the change as CONFIRMED, and *Blocking wording issues* is *None* → apply (item 5).
   - **Usable FAIL**: the verdict line is `VALIDATION <id>: FAIL`, with a REFUTED or UNVERIFIABLE
     claim, or a blocking wording issue that carries a `path:line` citation → revise (item 4), then
     the next round.
   - **Not usable**: anything else — no verdict line, a claim missing from the table, a PASS that
     its own table or blocking issues contradict, a FAIL whose claims are all CONFIRMED and whose
     blocking issues carry no citation → validate the unchanged text again with a new validator;
     the round counts. This applies mechanically the rule that validated text is never rewritten
     to satisfy a style opinion, and keeps the cost bounded.
   - **No validation happened**: the subagent returned nothing, failed, or its report says
     `CANNOT READ SOURCE`. It is not a round. The session tries once more with a new validator; if
     that also fails it stops, and this change and every later one is *not applied — not attempted,
     session stopped*. One environment problem then does not mark every change as failed.
   - *Optional suggestions* never cause a revision.
4. **Revision rules** (the orchestration session is the reviser; it never validates):
   - Read every cited `path:line` in the GnollHack source, at the recorded commit, before changing
     anything. A citation that does not support the validator's statement is not adopted: the text
     stays and the next round re-validates it.
   - Change only this change's text, on its own page. Keep its purpose (§ 2). Never touch another
     page, never add a fact unrelated to the purpose.
   - A REFUTED claim is rewritten to the mechanic the source shows, in the game's vocabulary, or the
     clause is removed if the purpose does not need it. An UNVERIFIABLE claim is narrowed to what
     the source settles, or removed.
   - Apply § 2's quantified-clause rule and § 3's page-text rules to the new text: no source
     references, no code identifiers, no new heading or table.
   - Rewrite the claim list so it matches the new text one for one; every mechanic the revision
     adds is a claim.
   - Withdraw the change only when the corrected mechanic adds nothing the page does not already
     say. It is then *not applied — withdrawn*, with the reason, so the loop does not spend rounds
     polishing a sentence the page does not need.
5. **Applying a passed change**: edit the page in place with the mechanism `wiki_bulk_edits`
   prescribes, never `git apply`, preserving line endings and encoding. A readable rewording
   `wiki_editing` asks for is allowed when it changes no mechanic, and is reported; a rewording that
   would change one is a revision and goes back to validation in the next round, or, when no round
   is left, the validated wording is applied as it stands. Verify the change at once: the
   distinctive player-facing phrase is on the page with its expected count, and
   `git status --porcelain` shows the baseline plus the pages of the changes applied so far and
   nothing else. For a change applied exactly as proposed, the page's hunk is compared with the
   Step 2 diff. If the text's location is gone or ambiguous, or the page already carries the text,
   the page is left as it was, the change is *not applied — application failed*, and it is not
   retried.
6. **The limit**: a change that has no usable PASS after **five** rounds is *not applied — failed
   validation after 5 rounds*. The session moves on to the next change.
7. **After the last change** the session runs the closing checks the target repository's skills
   require of every editing session, on the pages it changed. The handoff does not restate them.
8. **The final report** (§ 4.4) is the session's last message, and its last line is the
   `HANDOFF RESULT:` line.

**Bulk edits** are one change per page. To keep the cost proportionate, one validator may check up
to **ten** pages of the same bulk edit in one round, returning one verdict line per page; pages
that pass are applied, pages that fail are revised and go into the next round's batches. Every page
keeps its own five-round limit.

**Nothing is committed.** The session leaves every change uncommitted. Its final report gives the
final diff of every change applied after a revision or rewording, because that text was written by
the session, not by the analyst. For a knowledge-base handoff, the developer's review and commit
remains the human authorship rung 1 requires (`server_benchmark_to_chat_transfer` § 7).

The per-change loop, automatic application and the five-round limit were set on 2026-11-11 by the
user's instruction, so that one wrong claim no longer blocks every correct change in a handoff, and
a failed claim is corrected from the validator's evidence without a round trip through the
analyst.

## 4. Templates

`<R>` is the benchmark run number, `<V>` the document version and `<N>` the number of changes, in
every template. For a feedback issue, the title and the `Origin:` lines take the forms § 1 gives.
The texts below are normative: copy them, adjusting only line breaks. The fence lengths shown are
the ones a handoff uses.

### 4.1 Document Skeleton

`````markdown
# Wiki Handoff: Run <R> <Finding Tk> (<Brief Description>)

This document proposes <N> change(s) to the GnollHack wiki, so that <what the chat got wrong> does not recur.
Paste Step 2 into a new chat whose project folder is the GnollHackWiki clone at `C:\hmp\GnollHackWiki`, and allow it to read the GnollHack source clone at `C:\hmp\GnollHack` if it asks: the session checks each change against the game's source, applies the changes that pass, and ends with a report whose last line starts `HANDOFF RESULT:`.
Then review the changes the report marks as revised or reworded, commit and push what it applied, and paste the report into the analyst's session.

## Step 1 — Proposed Changes

### In Plain Text

#### W1 — <short name>

- Depends on: none
- Purpose: <what the chat got wrong, and the fact the page must carry>
- Page: <repository-relative path>
- Location on the page: <section / heading / line context>
- Proposed text: <exact text added or replaced>
- Claims asserted:
  - W1-C1: <claim in the game's vocabulary>
  - W1-C2: <claim in the game's vocabulary>
- Near-duplicates and exceptions left unchanged: <paths and reasons, or "none">

#### W2 — <short name>

- Depends on: W1
- <the same fields>

[Bulk edits: the exact line form, and a table of every page: change ID, page, per-page value, claim]

### As Diffs

W1:

```diff
--- a/<path>
+++ b/<path>
@@ ... @@
 context
-old line
+new line
 context
```

[One block per change, in the order above. Bulk edits: diffs for three representative pages — first, last, and one exception if any]

## Step 2 — Validate and Apply, One Change at a Time

````text
<orchestration prompt, § 4.3>
````

## Notes (No Action Required)

- Pre-flight, read-only, on <date>:
  - GnollHack source: `C:\hmp\GnollHack` at HEAD <sha> (<clean / dirty> tree); <matches / differs from> the run's `SourceCodeHeadSha` <run sha>.
  - GnollHackWiki: `C:\hmp\GnollHackWiki` at HEAD <sha> (<clean / dirty> tree); <matches / differs from> the run's `WikiHeadSha` <run sha>.
  - Target page(s) exist; line endings CRLF; edited in place (nothing generates or copies wiki pages).
- Changes and their dependencies: <W1 (none), W2 (W1), …>
- Analyst's source citations — for the reviewer, withheld from the session and its validators:
  - W1-C1: <path:line> (<one-line rationale>)
  - W1-C2: <path:line> (<one-line rationale>)
- What the session does: validates each change with a fresh read-only validator, applies it when it passes, revises and re-validates it when it fails, up to five rounds; leaves everything uncommitted.
- If the session is interrupted: continue in the same chat. If that chat is gone, restore the pages it changed (`git restore -- <pages>`) and paste Step 2 again.
- What the analyst does with the report: records each change's outcome, and drops or re-hands the changes that were not applied.
- Sequencing: <the steps behind the § 4a gate, or "nothing in the round depends on the wiki change">
- Closure criterion: <pre-declared, per § 5, per change>
`````

### 4.2 Validator Prompt

The orchestration session fills it in per change and round.

````text
You are a read-only validator. Read the GnollHack source repository at C:\hmp\GnollHack and
nothing else: not the wiki, not the knowledge base and no other repository, even where the folder
you were started in is one of them. Do NOT edit, stage, commit, push or check out anything, in any
repository; read-only git commands are fine. Do not build, run or test anything.

Origin: <benchmark run <R>, finding <Tk> | Gnoll Overseer feedback issue #<id>>, ladder rung <1|2>.
You are validating change <ID> of handoff document <document>_v<V>.md.

Revision: check the claims at commit <sha>. Run `git -C C:\hmp\GnollHack rev-parse HEAD` and
`git -C C:\hmp\GnollHack status --porcelain -- src include dat` first. If HEAD is <sha> and the
second command prints nothing, read the files as they are on disk. Otherwise do not check anything
out: read files with `git -C C:\hmp\GnollHack show <sha>:<path>` and search with
`git -C C:\hmp\GnollHack grep -n <pattern> <sha> -- src include dat`, and say in the report that
you did. If you cannot read the repository at all, answer with one line,
"CANNOT READ SOURCE: <reason>", and give no verdict.

Proposed page text for <ID> (page <path>):
<the change's current text; for a bulk batch, one block per page ID>

Claims to validate:
- <ID>-C1: <claim, in the game's vocabulary>
- <ID>-C2: <claim>

Task:
1. Locate the evidence for each claim yourself, in the GnollHack C and data source at <sha>.
   Do NOT consult NetHack source, the NetHack wiki or general NetHack knowledge; GnollHack
   diverges from NetHack. Settle every claim from this repository only.
2. Give each claim one verdict:
   - CONFIRMED: the source directly supports it. Cite exact path:line.
   - REFUTED: the source contradicts it. Cite exact path:line and state the correct mechanic
     in a player's vocabulary.
   - UNVERIFIABLE: you could not find source that settles it. Say where you looked.
3. Check the proposed page text against what you confirmed, for factual problems only. A
   BLOCKING ISSUE is wording that states, or clearly implies, a game mechanic that the source
   contradicts or does not support: a false fact, a wrong condition, or a missing exception
   that would make a player act wrongly. Judge the text as written; do not fail it for
   something it does not say. The following are NOT blocking issues and must not cause a FAIL:
   tone, style, length, a phrasing you would have preferred, a detail that could be added, a
   simplification that stays true, and anything about how wiki pages are written — the wiki
   has its own editing rules, and the session that makes the edit applies them. If you have
   such remarks, put them under "Optional suggestions"; they are advice and do not affect the
   verdict. When in doubt whether a wording point is blocking, ask: would a player who
   believes this sentence be wrong about the game? If not, it is a suggestion. A clause that
   speaks about a whole class — "any", "every", "all", "always", "never", "as for other …" —
   is checked against each member of that class the source distinguishes, and a member for
   which it is false is a blocking issue.

Report format:
- A table: | Claim | Verdict | GnollHack source citation (path:line) | Notes / corrected statement |
  with one row for every claim listed above.
- Blocking wording issues: <each with the source citation that shows the text is wrong, or "None">
- Optional suggestions: <remarks that do not affect the verdict, or "None">
- End with one verdict line per change, and nothing after the last one:
  VALIDATION <ID>: PASS     or     VALIDATION <ID>: FAIL
  PASS is required when every claim of that change is CONFIRMED and it has no blocking wording
  issue, whatever "Optional suggestions" contains. FAIL is permitted only for a claim that is
  REFUTED or UNVERIFIABLE, or for a blocking wording issue that carries a citation.
````

### 4.3 Orchestration Prompt

Step 2 of the document.

````text
You are working in the GnollHack wiki repository at C:\hmp\GnollHackWiki (recorded HEAD <sha>,
<clean tree | dirty tree: the uncommitted files, which must not be overwritten>), which is this
session's project folder; paths below are repository-relative to it. You will also read the
GnollHack source at C:\hmp\GnollHack. Do not edit any repository other than this one. Do not
commit or push; leave the changes in the working tree and print the git commands at the end.

Origin: <benchmark run <R>, finding <Tk> | feedback issue #<id>>, ladder rung 2. <One sentence on
what the model got wrong and why the wiki is the right place to fix it.>
This prompt is Step 2 of <document>_v<V>.md. It carries <N> changes.

Read .agents/skills/wiki_editing/SKILL.md and .agents/skills/wiki_bulk_edits/SKILL.md first and
follow them. Nothing generates or copies wiki pages; edit pages in place. Page text carries no
source-code references and stays in the page's own register, for players.

Verified on disk before this prompt was written:
- <per change: target page, near-duplicates left as they are, match counts, existing wikilinks>
- Line endings: CRLF, UTF-8 without BOM.

Before the first change, check the four points below. If one fails, change nothing, validate
nothing, and end with one line: "HANDOFF NOT STARTED: <reason>".
1. `git remote get-url origin` names hyvanmielenpelit/GnollHackWiki.
2. `git status --porcelain` lists none of these pages: <target pages>. If it lists any, name them
   and say that either an earlier run of this prompt was interrupted or they carry other
   uncommitted work, and that the user can restore them (`git restore -- <pages>`) and paste this
   prompt again. Keep the output of this command as the baseline. Then `git rev-parse HEAD` prints
   <sha>, or `git diff --quiet <sha> HEAD -- <target pages>` exits 0, which means no target page
   changed since; say in the report which revision you edited.
3. `git -C C:\hmp\GnollHack cat-file -t <source sha>` prints "commit" and you can read a file
   under C:\hmp\GnollHack\src. If you cannot, ask the user to add C:\hmp\GnollHack as a directory
   of this session, and wait.
4. You can spawn subagents. If you cannot, say so: the validation must be done by a reader that
   did not write the text, so do not validate in this conversation instead.

Then process the changes one at a time, in the order below. After each one, print one line:
"<ID>: applied in round <k>" or "<ID>: not applied - <reason>". For each change:

A. If a change it depends on was not applied, skip it: "not applied - skipped, depends on <ID>".
B. Validation round (at most 5 per change). Spawn a NEW subagent for every round: a fresh agent
   that starts from the validator prompt alone, never a fork or a continuation of this
   conversation, and never a validator you used before. Use the `deep` tier as your harness's
   conventions resolve it. Prefer an agent type that cannot edit files, provided it can read whole
   files and run read-only git commands; a search-only type is not a validator. Give it the
   validator prompt below, filled in with this change's CURRENT text and claims only. Never give
   it earlier text, earlier reports, your reasoning, or anything else from this prompt.
C. Read its report for this change:
   - Usable PASS (the verdict line is PASS, the table gives every claim of the change as
     CONFIRMED, and "Blocking wording issues" is "None"): apply the change (D).
   - Usable FAIL (the verdict line is FAIL, with a REFUTED or UNVERIFIABLE claim, or with a
     blocking wording issue that carries a path:line citation): revise (E), then go to the next
     round.
   - Not usable (anything else: no verdict line, a claim missing from the table, a PASS that its
     own table or blocking issues contradict, a FAIL with every claim CONFIRMED and no citation on
     any blocking issue): do not change the text; validate it again with a new subagent. That
     counts as a round.
   - No validation happened (the subagent returned nothing, failed, or its report says
     "CANNOT READ SOURCE"): that is not a round. Try once more with a new subagent. If that fails
     too, stop processing: this change and every later one is "not applied - not attempted,
     session stopped: <reason>". Go to the final report.
   Optional suggestions never change the text.
   No usable PASS after round 5: "not applied - failed validation after 5 rounds". Go to the next
   change.
D. Apply: edit the page in place with the mechanism wiki_bulk_edits prescribes, never git apply,
   so the page's line endings and encoding are preserved. Make the page match the change's
   current text (for round 1, the diff below). Where wiki_editing asks for a more readable
   wording, use it only if no mechanic changes, and report it; a wording that changes a mechanic
   is a revision (E) and goes back to validation, or, if no round is left, apply the validated
   wording as it stands. Then verify: <per change: the distinctive player-facing phrase and its
   expected count; never a code identifier>, and `git status --porcelain` shows the baseline plus
   the pages of the changes applied so far, and nothing else. For a change applied exactly as
   proposed, compare its hunk in `git diff` with the diff below, ignoring the header lines, and
   report any difference. If the place to edit is gone or ambiguous, or the page already carries
   the text, leave the page as it was: "not applied - application failed"; do not retry.
E. Revise, against the change's purpose:
   - Read every cited path:line in C:\hmp\GnollHack yourself first, at commit <source sha>. Do not
     adopt a statement the cited source does not support; keep the text and re-validate it
     instead.
   - Change only this change's text on its own page. Keep its purpose; add no unrelated fact;
     touch no other page.
   - A REFUTED claim: rewrite it to the mechanic the source shows, in the game's vocabulary, or
     remove the clause if the purpose does not need it. An UNVERIFIABLE claim: narrow it to what
     the source settles, or remove it.
   - A clause about a whole class ("any", "every", "all", "always", "never", "as for other ...",
     "unless ...", a reference to a list) is a claim about each member; make it true of each, or
     make it speak about this page's own subject only.
   - No file paths, line numbers or code identifiers in the page text; no new heading or table.
   - Rewrite the claim list to match the new text one for one.
   - If the corrected mechanic adds nothing the page does not already say, withdraw the change:
     "not applied - withdrawn after round <k>: <reason>".

Changes, in order:

<per change: ID, Depends on, purpose, page, location, text, claims, and its diff block:>

```diff
<this change's Step 1 diff block, byte-identical>
```

<Bulk edits only: the exact line form, the Step 1 table, and: you may give one validator up to
ten pages of this bulk edit in one round; it returns one verdict line per page; group the pages
that are still open into batches of up to ten for the next round; each page keeps its own
five-round limit.>

Validator prompt:

```text
<§ 4.2, with the document name, version, origin and source commit filled in, and <ID>, text and
claims left for you to fill in per round>
```

After the last change, run the closing checks that wiki_editing and wiki_bulk_edits require of
every editing session, on the pages you changed. Then write the final report as your last
message, in exactly this form:

```text
<§ 4.4>
```
````

### 4.4 Final Report

The orchestration session's last message.

```text
## Handoff result: <a> of <N> changes applied, <n> not applied

Wiki revision edited: <sha> (<the recorded HEAD | the recorded HEAD was <sha>; no target page
changed in between>). Source commit validated at: <sha>.

| Change | Page | Result | Rounds | Note |
|--------|------|--------|--------|------|
| W1 | <page> | Applied | 1 | as proposed |
| W2 | <page> | Applied | 1 | reworded for readability, no mechanic changed |
| W3 | <page> | Applied | 3 | revised: <one line on what changed> |
| W4 | <page> | NOT APPLIED | 5 | failed validation after 5 rounds |
| W5 | <page> | NOT APPLIED | 0 | skipped, depends on W4 |

Applied: <a>  (as proposed: <p>, reworded: <q>, revised: <r>)
Not applied: <n>  (failed validation after 5 rounds: <f>, withdrawn: <w>, skipped as dependent: <s>,
application failed: <e>, not attempted: <x>)

### Not applied - read these
For each: the change ID and page; the reason; the last text that was validated; the last report's
REFUTED and UNVERIFIABLE claims and blocking issues, with their citations.

### Applied after revision or rewording - review before committing
For each: the change ID; what the validators refuted and how the text was changed, or what was
reworded and why; the final `git diff` of the page.

### Validation evidence
For each applied change: the round that passed and its verdict table, citations included.

### Verification
The per-change phrase counts, every difference from a proposed diff, the closing checks' output,
and the final `git status --porcelain`.

### To commit and push
The git commands of wiki_bulk_edits § 9, naming only the files of applied changes.

HANDOFF RESULT: APPLIED <a> / NOT APPLIED <n> / TOTAL <N>
```

The knowledge-base variant of § 4.3 differs only where § 1 says: its first sentence names the
developer's clone of `hyvanmielenpelit/OverseerKnowledgeBase` by repository and gives no path,
check 1 names that repository, the clone's `.agents/skills/kb_article_generation/SKILL.md` takes the
place of the two wiki skills in the opening and in the closing checks, the IDs are `K`, the report
prints the `git add`, `git commit` and `git push` commands for that clone, and the `summary:` rule
applies.

## 4a. Sequencing: When the Round Waits for the Human

**Decide explicitly whether anything else in the round depends on a wiki change having been
applied**, and say which way the answer went.

| Dependent on a change that was applied | Independent of it |
|---|---|
| Recording the new `WikiHeadSha` (§ 5) | A rubric handoff |
| Any verification that greps the wiki, or calls `wiki_view` / `wiki_search` | A harness fix |
| A re-run whose questions touch the edited pages | A tool-guide edit |
| A corpus refresh or an Overseer restart | A registry entry that does not name the new SHA |
| Any plan step whose acceptance criterion names the page | |

**Finish every independent piece of work first, then stop.** A round that gates before doing what
it could have done wastes the wait.

**When dependent work exists, the round pauses at an explicit gate**, announced in chat in one
message carrying: the clickable link to the handoff document; the human's sequence from the
document's framing — Step 2 in a new chat opened on `C:\hmp\GnollHackWiki`, allowed to read
`C:\hmp\GnollHack`, then the review, commit and push of what it applied; what is waiting on it; and
the request in plain words —

> Tell me when the Step 2 session has finished, and paste its final report (the section that ends
> in the `HANDOFF RESULT:` line). I will then continue with `<the dependent steps>` for the changes
> it applied.

**The report is the gate**, because a clean tree is still ambiguous and *"I pasted it"* is still
not *"it finished"*. A session that ended with `HANDOFF NOT STARTED:` did nothing: the analyst
reads the reason and says what to do, and nothing dependent starts.

**On a partial result**, the round continues with the dependent work of **applied** changes only.
A dependent step whose change was not applied is dropped from this round and recorded (§ 5); it is
never run against an unchanged page. Under a plan, its `task.md` line is marked skipped with the
reason, and the walkthrough lists it.

**Unapplied changes do not come back to the analyst automatically.** The analyst reads the
report's *Not applied* section and either drops the change, recording why, or writes a new
`_v<V+1>` handoff carrying **only** the unapplied changes, corrected from the report, when the
report shows the analyst's own pre-flight was wrong. The analyst never re-hands the same text. A
new version follows the round's harmonization rule: a set member with no content change is copied
verbatim with the one-line harmonization note, and a plan already approved stays approved when its
copy is verbatim. *Optional suggestions* in the report need no action.

**Do not poll, do not proceed on a timer, and do not read a clean `C:\hmp\GnollHackWiki` working
tree as confirmation.** The orchestration session leaves its changes uncommitted (§ 3), so a clean
tree is ambiguous between *not started* and *finished and committed*.

**On confirmation, verify read-only before continuing** rather than taking the report at face
value, with commands that hold whether or not the developer has committed:

```powershell
git -C C:\hmp\GnollHackWiki diff --name-only <recorded HEAD>
git -C C:\hmp\GnollHackWiki log -1 --format="%h %ad %s" --date=short
```

The first must list the page of every applied change, and the § 2 grep for each applied change's
distinctive player-facing phrase is run and its count recorded. `git status --porcelain` is not the
check: it is empty once the developer has committed. If a page or a phrase is absent, say so and do
**not** start the dependent work of that change.

**Under a plan, the gate is a step of its own**, named as such in Proposed Changes with the
dependent steps listed beneath it, so a resumed session can see it has not been passed. Its
`task.md` checkbox is ticked after the read-only verification, never on the user's word alone.

**When nothing depends on it, say so explicitly** and close the round, with this message:

> Paste Step 2 into a new chat opened on `C:\hmp\GnollHackWiki`, and allow it to read
> `C:\hmp\GnollHack` if it asks. When it finishes, review the changes it marks as revised or
> reworded, commit and push what it applied, and paste its final report here so the outcome can be
> recorded. Nothing else in this round depends on the wiki edit.

Rung 2 is exempt from the re-run requirement (`server_benchmark_to_chat_transfer` § 9), so this
is the common case; the defect is silence about the check, not the absence of a gate.

**The gate and both closing messages also appear in the round's Developer Runbook**
([`server_benchmark_runbook`](../server_benchmark_runbook/SKILL.md)) as `CHANGE` steps of its
one numbered list — the handoff session (this document's Step 2), the review, commit and push of
what it applied, and the wait for the 10-minute wiki re-index or the restart — so the developer
sees where the wiki work falls relative to the rubric import and the next run. The runbook numbers
its own steps, so it names these by what they are, not by this document's numbers. The session's
card carries these two lines, which the runbook copies:

- `Expect:` the session's last line is `HANDOFF RESULT: APPLIED <N> / NOT APPLIED 0 / TOTAL <N>`
- `If not:` do none of the later steps that name a wiki page; paste the session's whole final
  report, or its `HANDOFF NOT STARTED:` line, into the analyst's chat

This section still owns the gate's wording and its read-only verification.

## 5. After the Session

When the user has committed and pushed what the session applied, record in
`server_benchmark_to_chat_transfer` § 11, per change:

- applied or not applied, with the reason;
- for an applied change: the round in which it passed, whether it was revised or reworded and its
  final wording, the GnollHack commit it was validated at, and the new `WikiHeadSha` the next run
  will show;
- for an applied change only, the **pre-declared criterion** that says the finding is closed — for
  example, the assessor no longer marks the fact missing on the question that raised it, or
  `wiki_view` of the page resolves the section.

A not-applied change is recorded with its last blocking issue, so a later analysis does not
re-propose it unaware.

Rung 2 is exempt from the re-run requirement (§ 9), so this is a record, not a gate.

## 6. Worked Examples

### Run 34, T4 (2026-09-10)

The handoff prompt presumed a page generator or template in the wiki, where none exists, so the wiki
session spent a step looking for one. It named `Resistances and Saving Throws.md` as the home
of the saving-throw formula when the formula is in `Saving Throws.md`, split out on
2026-07-26, so the `grep -c` verification the prompt prescribed would have returned zero. Both
facts were checkable read-only on disk with the access the analyst already had.

The edit itself was correct once the wiki session re-derived the target: 27 spell pages, each
gaining `- **Saving throw:** Against <attribute> — see [[/Saving Throws]]`.

### Run 42, W1 and W2 (2026-09-12)

The prompt prescribed the page text with its citations inline — *"unaffected before any saving
throw is rolled (`src/zap.c` lines 951-956, `include/mondata.h` lines 752-753)"* — named the
`MR_FEAR` macro on two pages, and verified with `grep -c "MR_FEAR"`, a check that can only pass
if a C macro name reaches a player-facing page. It was faithful to this skill as it then stood,
which required a citation on every fact the edit added: the defect was in the method, not in the
analyst applying it.

The wiki session overrode the prescribed wording and wrote the readable form —
*"Monsters that are immune to fear, along with all undead and all mindless monsters, are
unaffected before any saving throw is rolled"* — which is what `wiki_editing` § 4 and § 17
require. So the containment came from the wiki repository, one step after the mistake, and not
from the side that made it.

Two rules above are the response: the analyst separates the fact from the evidence (§ 2), and the
prompt may not carry a source reference or a code-identifier grep into the page (§ 3). One page
still shows what the old rule produced — `Saving Throws.md` gained a paragraph of `save_adj`,
`src/zap.c` line numbers and a function name in the run-39/40 round, and is the only
player-facing page in the wiki that cites the source.

### Run 124, W3 (2026-10-20)

W3 added the clause *"as they may be to any chaotic character"* to `Races/Orc.md`, which was true
for orcs and false for a chaotic human. Validation passed it because each numbered claim was true,
and the clause was not a numbered claim. On run 125 it produced Q9's critical error. Two rules
are the response: the quantified-clause item of the § 2 pre-flight, and the class-clause check
in task item 3 of the § 4.2 validator prompt.

### Run 125, W4 (2026-10-21)

The round's own v2 handoff failed validation on the same rule twice: *"Chaotic creatures may be
peaceful to you, unless your race is always hostile with theirs"* omitted the always-peaceful
monsters and quest leaders that win before race hostility, and *"one of the prompts below"*
treated an incomplete table as the complete set of a gnoll's warnings. The validator caught both,
with the quantifier check now in the § 4.2 template. The lesson for the analyst's pre-flight is
that an exception clause ("unless …") and a reference to a list ("one of the prompts below") are
quantified claims too: read the code path for every earlier branch that can return first, and
every case the list may omit.

## 7. Cross-References

- [`server_benchmark_to_chat_transfer`](../server_benchmark_to_chat_transfer/SKILL.md) — § 2
  category 4 (check the corpus on disk before filing), § 7 rung 2 (the ladder and the
  authorship rule), § 9 (the rung-2 re-run exemption), § 11 (where the outcome is recorded)
- [`server_tool_data_sources`](../server_tool_data_sources/SKILL.md) — § 3 for the two standard
  clone locations, `C:\hmp\GnollHack` and `C:\hmp\GnollHackWiki`, and why only their HEADs are
  compared with `SourceCodeHeadSha` and `WikiHeadSha`; § 6 corpus provenance
- [`server_rubric_handoff`](../server_rubric_handoff/SKILL.md) — the sibling handoff whose store
  has **no** session to delegate to. Its human steps in the Admin UI are irreducible, so § 3a's
  contract does not transfer to it: there, the numbered steps for the human are the deliverable.
  The per-change loop of § 3b does not apply to it either, for the same reason
- `.agents/skills/wiki_editing/SKILL.md` and `.agents/skills/wiki_bulk_edits/SKILL.md` in the
  **GnollHackWiki** repository, cited by repository-relative path because they live in another
  repository
