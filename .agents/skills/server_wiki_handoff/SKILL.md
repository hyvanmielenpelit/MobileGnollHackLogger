---
name: server_wiki_handoff
description: >-
  Mandatory method for turning a rung-2 wiki finding from an Overseer AI benchmark analysis into
  a three-step handoff document: proposed changes in plain text and as diffs, an AI validation prompt
  for a read-only session on the GnollHack source clone at C:\hmp\GnollHack, and an execution prompt
  for a session on the GnollHackWiki clone at C:\hmp\GnollHackWiki run only after validation passes.
  Covers pre-flight checks against the wiki clone (target page exists, which page carries the fact,
  match count, existing links, clean tree, line endings, no presumed generator), what prompts must
  not prescribe, templates, the confirmation gate pausing a round until validation passes and the
  wiki session finishes, and post-session records. Read with the other four benchmark skills before
  the first finding is written; act on it whenever a finding lands on ladder rung 2.
---

# Wiki Handoff: Turning a Rung-2 Finding Into a Validated Edit Other Sessions Can Execute

## 1. Purpose and When It Binds

A rung-2 wiki finding (`server_benchmark_to_chat_transfer` § 7) leaves this repository as a
**three-step document** — the proposed changes, a prompt for a **validation session** opened on
the GnollHack source clone at `C:\hmp\GnollHack`, and a prompt for an **execution session**
opened on the GnollHackWiki clone at `C:\hmp\GnollHackWiki`. Neither session can ask the analyst
anything, so **each prompt is the whole interface to its session**. § 3a fixes what the document
carrying them may contain, because a handoff that buries the human's short sequence among steps
the analyst could have taken is a second interface, and a worse one.

The division of labour is fixed:

- **The analyst does the checks only someone with the run context and read access to
  `C:\hmp\GnollHackWiki` can do** — which page carries the fact, how many pages the edit
  touches, what the run actually saw.
- **The wiki's own skills own the wiki's conventions** — `wiki_editing` for link syntax,
  headings and emoji rules, `wiki_bulk_edits` for cross-page mechanics. The execution prompt
  cites them by name and does not restate them.
- **The validation session independently checks every mechanic the edit asserts against the
  GnollHack source**, read-only, before any page changes.

This skill is read with the other four benchmark skills on **every** benchmark analysis
(`.agents/AGENTS.md`, "AI Benchmark Findings"); its body is acted on only when a finding lands
on rung 2.

Reading `C:\hmp\GnollHackWiki` and `C:\hmp\GnollHack` is read-only research in other repositories
and is permitted. **Editing either from this repository's session is not** — that is what the two
prompts exist for.

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
  in the execution prompt and the Notes whether they match; a mismatch means the run read a
  different revision than the one the wiki session will edit. Compare SHAs only — never write
  whether `WikiPath` resolves to this directory (`server_tool_data_sources` § 3).
- **Confirm the target page exists by path.** If two or more pages have similar names, open
  each, grep for the fact, and read the history of each:

  ```powershell
  git -C C:\hmp\GnollHackWiki log --format="%h %ad %s" --date=short -- "<file>"
  ```

  Name **the page that carries the fact** in Step 1 and in the execution prompt. Name the
  near-duplicate too, and say what to do about it — usually nothing.
- **Count the pages the edit will touch, with the exact regex the execution prompt will
  prescribe**, and state that count in Step 1 and in the execution prompt. Check whether any of
  those lines already carries a wikilink; if some do, the execution prompt must say whether they
  are skipped or rewritten.
- **Check the working tree is clean**: `git -C C:\hmp\GnollHackWiki status --porcelain` is empty.
  If it is not, say so in the execution prompt — the wiki session must not overwrite uncommitted
  work.
- **Check the line endings** of a sample of the target files, and state them in the execution
  prompt. CR-terminated line count equal to newline count means CRLF.
- **Do not assert that a generator, template or data file exists unless you found one.** There
  is none for spell, monster or item pages as of 2026-09-10; every such page is hand-maintained
  Markdown. If you looked and found none, the execution prompt says "edit in place".
- **Every fact the edit adds is verified** against the game source or another primary text, and
  the citation proving it goes **in the handoff document's Notes and in the analysis — never into
  the text the page will carry, and never into the validation prompt** (§ 3;
  `server_benchmark_to_chat_transfer` § 7 rung 2 authorship rule). The wiki is player
  documentation and forbids source references on a page outright; `wiki_editing` § 4 and § 17 own
  that rule. Verification is what licenses an AI-authored wiki edit, and a reviewer checks it in
  the handoff, where it is read once, rather than on the page, which is read by every player
  forever. A link-only edit needs no citation beyond the target page. Validation is an
  independent second check, not a replacement for this one: the analyst still verifies before
  writing a claim.
- **Confirm the GnollHack source clone and record the revision the validation will read.**

  ```powershell
  git -C C:\hmp\GnollHack remote get-url origin
  git -C C:\hmp\GnollHack rev-parse HEAD
  git -C C:\hmp\GnollHack status --porcelain
  ```

  The remote must name `hyvanmielenpelit/GnollHack`. If the directory is missing or is another
  repository, stop and ask the user; do not substitute the value of `SourceCodePath`. Compare the
  HEAD with the run's `SourceCodeHeadSha` and say in the validation prompt and the Notes whether
  they match. A dirty tree means the validator could read uncommitted source; say so. Compare
  SHAs only — never write whether `SourceCodePath` resolves to this directory
  (`server_tool_data_sources` § 3).
- **Number every claim.** Each game mechanic the edit asserts becomes one claim, `C1`, `C2`, …,
  one sentence each, in the game's vocabulary. A bulk edit whose line carries a per-page value
  (run 34: the saving-throw attribute of each of 27 spells) has one claim per page, listed in the
  Step 1 table. A link-only edit still names the claim the link implies (that the linked mechanic
  applies to this page).
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
  return, strip it: the diff specifies content, and the execution prompt states the line endings
  separately. Take the file from `C:\hmp\GnollHackWiki` at the recorded HEAD with a clean tree, so
  every context line is verbatim from the page the execution session will edit.

## 3. What the Prompts Must Not Do

### The Validation Prompt

- **Do not include the analyst's source citations, file names or reasoning.** A validator handed
  a file and a line reads that line and stops; it must locate the evidence itself. The citations
  are in the Notes, where a reviewer compares them with the validator's.
- **Do not let the validator edit, stage, commit, push or check out anything**, in any repository.
- **Do not let the validator settle a claim from NetHack source, the NetHack wiki or general
  NetHack knowledge.** GnollHack diverges from NetHack, and a claim is CONFIRMED only by the
  GnollHack source at the recorded HEAD.
- **Do not accept a verdict without a citation**: every CONFIRMED and REFUTED carries `path:line`
  evidence at that HEAD.

### The Execution Prompt

- **Do not restate wiki link syntax, heading rules or emoji conventions.** `wiki_editing` owns
  them, it is longer and more current than any paraphrase, and the prompt cites it by name.
- **Do not prescribe `sed -i` or any Git Bash in-place editor.** MSYS `sed` rewrites every file
  it processes with LF endings, including files the pattern never matched. `wiki_bulk_edits`
  carries the PowerShell recipe that preserves them.
- **Do not ask the wiki session to commit or push.** It leaves the changes in the working tree
  and prints the commands.
- **Do not write a verification step against a page name that was not confirmed in pre-flight.**
  A `grep -c` against a page that does not exist reports zero and reads as a failed edit.
- **Do not put source-code references in the page text the prompt prescribes** — no file paths,
  no line numbers, and no C identifiers, whether macros, struct fields, enum constants or
  function names. State the mechanic in the game's own vocabulary: *"monsters that are immune to
  fear"*, never `` `MR_FEAR` ``. The code locations belong in the document's Notes, below the
  prompts, which is what a reviewer reads. `wiki_editing` § 4 owns the rule; the prompt does not
  restate it, it simply must not ask for text that breaks it.
- **Do not verify with a grep for a code identifier.** A `Verify:` step reading
  `grep -c "MR_FEAR"` can only pass if a macro name reached a player-facing page, so the prompt's
  own verification enforces the defect the bullet above forbids. Grep for a distinctive
  player-facing phrase from the sentence instead.
- **Do not prescribe a heading or a table per finding.** One added fact is usually one sentence,
  extending what the page already says. `wiki_editing` § 17 owns the shape of a benchmark-driven
  edit; a prompt that dictates structure overrides it from the wrong side of the handoff.
- **Do not paraphrase the diffs.** The execution prompt carries the Step 1 diffs
  **byte-identical**, and the analyst checks that before handoff (§ 3a self-check).
- **Do not prescribe `git apply`.** The diffs specify the intended result; the edit is made by the
  mechanism `wiki_bulk_edits` prescribes, which preserves CRLF. Where `wiki_editing` improves the
  wording, the session writes the readable version and reports the deviation — **but it may not
  add, drop or alter a mechanic**, because the wording was validated and the rewording is not.

## 3a. The Document Contract: Three Steps, Then Notes That Need No Action

The deliverable is **one file** in the round's plans task directory,
`wiki_handoff_prompt_v<V>.md`. It is a member of the round's document set, so revising it bumps
every member to the same version.

**It contains these parts, in this order, and nothing else:**

1. **At most three sentences of framing** at the top: what the document is for, and the human's
   sequence — paste Step 2 into a new chat whose project folder is `C:\hmp\GnollHack`; if its last
   line is `VALIDATION: PASS`, paste Step 3 into a new chat whose project folder is
   `C:\hmp\GnollHackWiki`; otherwise do not run Step 3, and paste the validation report back into
   the analyst's session.
2. **Step 1 — Proposed changes.** Two subsections, in this order:
   - **In plain text**: per page — the repository-relative path, where on the page the change
     goes, the exact text added or replaced, and the numbered claims it asserts. Name any
     near-duplicate page and say it is left unchanged.
   - **As diffs**: one fenced `diff` block per page, unified format, three context lines,
     produced by the § 2 recipe. **Bulk edits** (more than five pages sharing one line form):
     full diffs for three representative pages — the first, the last, and one exception (a line
     that already carries a wikilink, or a skipped page) if any — plus a table listing **every**
     page with its per-page value and claim number. No page is omitted from the table.
3. **Step 2 — Validation prompt**, one fenced block, self-contained, template § 4.2.
4. **Step 3 — Execution prompt**, one fenced block, self-contained, template § 4.3, preceded by
   the line "Run only after Step 2 returned `VALIDATION: PASS` for this version of this
   document."
5. **Notes that require no action from the human**, under a heading that says so — the § 2
   pre-flight results, including the GnollHack HEAD comparison; the analyst's source citations
   for each claim; what each session will do; what the analyst does afterwards; the § 5
   criterion. These are read, reviewed and filed; nothing in them is a step.

**Fence nesting.** A fence closes at the first line of at least as many backticks as opened it,
so **an outer fence is one backtick longer than the longest fence inside it**. A `diff` block
takes three; the Step 3 prompt, which contains `diff` blocks, takes four; anything quoting a
whole document, such as § 4.1 below, takes five. If a page being diffed itself contains a line
that begins with three backticks, the `diff` block takes four and everything around it one more.

**Version binding.** Both prompts name the document and its version. A PASS licenses Step 3 of
**that version only**; any revision re-runs validation.

**It carries no step, checklist or instruction addressed to the human beyond this sequence:**
paste Step 2; read its last line; on `VALIDATION: PASS` paste Step 3; on anything else, paste the
validation report back to the analyst. The document carries no other step. Four creep back most
often:

- no *"first check…"* pre-steps — those are § 2, and the analyst has already done them;
- no post-steps — see the commit exception below;
- no *"verify the change landed"* step — that is § 4a, done by the analyst, read-only;
- no *"tell me when you are done"* inside the document — the wait is requested **in chat**, which
  is where the user answers.

**The reassignment rule.** Any piece of work that surfaces while the handoff is being written has
exactly four legitimate destinations: **(a)** the analyst, before the document is written;
**(b)** the validation session, stated inside Step 2; **(c)** the execution session, stated inside
Step 3; **(d)** the analyst, after the confirmation in § 4a. There is no fifth. A step that
appears to need the human beyond the sequence above has been put in the wrong place.

**Self-check before handoff.** The claim list in Step 2 matches the claims in Step 1 one for one;
a bulk table is identical in Step 1 and Step 3; and the diff blocks are byte-identical in both,
which this checks (for a four-backtick `diff` fence, lengthen both fence patterns to match):

```powershell
$text = [IO.File]::ReadAllText('<absolute path to wiki_handoff_prompt_v<V>.md>')
$m = [regex]::Matches($text, '(?ms)^```diff\r?\n.*?^```[ \t]*\r?$')
$half = $m.Count / 2
if ($m.Count -eq 0 -or $m.Count % 2 -ne 0) { "FAIL: $($m.Count) diff blocks" }
else {
    $bad = 0
    for ($i = 0; $i -lt $half; $i++) { if ($m[$i].Value -cne $m[$i + $half].Value) { $bad++; "FAIL: diff block $($i + 1) differs" } }
    if ($bad -eq 0) { "OK: $half diff block(s), identical in Step 1 and Step 3" }
}
```

**The one exception, stated rather than omitted: the commit and push in the wiki clone.** No agent
commits there, and the § 4.3 prompt already ends by making the wiki session print those commands.
That is the wiki session's own handoff to the human, one step downstream, and this document does
not restate it.

**Document versus chat message.** The human's sequence is part of the document — the framing
sentences and the line above Step 3 — because a branch on PASS or FAIL cannot be followed from a
chat message that has scrolled away. What lives only in the **chat message** is the § 4a wait
request, which is where the user answers it. The document must still read correctly on its own
to someone opening it a month later, which is what the framing sentences are for.

The rule that the document carries no avoidable step for the human was set on 2026-09-13 by the
user's instruction, not by an incident. Its purpose is that the one action that matters does not
compete for attention with steps the agent could have taken itself. The three-step shape was set
on 2026-09-18 by the user's instruction, so that every mechanic reaching a player-facing page is
checked against the GnollHack source by a session that did not write it, before the page changes.

## 4. Templates

`<R>` is the benchmark run number and `<V>` the document version, in every template.

### 4.1 Document Skeleton

`````markdown
# Wiki Handoff: Run <R> <Finding Tk> (<Brief Description>)

Run Step 2 in a new chat whose project folder is the GnollHack source clone at `C:\hmp\GnollHack`.
If, and only if, its last line is `VALIDATION: PASS`, run Step 3 in a new chat whose project folder is the GnollHackWiki clone at `C:\hmp\GnollHackWiki`.
Otherwise do not run Step 3; paste the validation report back into the analyst's session.

## Step 1 — Proposed Changes

### In Plain Text

- Target article(s): <repository-relative path>
- Location on the page: <section / heading / line context>
- Proposed change: <exact text added or replaced>
- Claims asserted:
  - C1: <claim in the game's vocabulary>
  - C2: <claim in the game's vocabulary>
- Near-duplicates and exceptions left unchanged: <paths and reasons>

[Bulk edits: a table of every page, its per-page value and its claim number]

### As Diffs

```diff
--- a/<path>
+++ b/<path>
@@ ... @@
 context
-old line
+new line
 context
```

[Bulk edits: diffs for three representative pages — first, last, and one exception if any]

## Step 2 — Validate Against the GnollHack Source

````text
<validation prompt, § 4.2>
````

## Step 3 — Execute in the Wiki (Only After PASS)

Run only after Step 2 returned `VALIDATION: PASS` for this version of this document (`wiki_handoff_prompt_v<V>.md`).

````text
<execution prompt, § 4.3>
````

## Notes (No Action Required)

- Pre-flight, read-only, on <date>:
  - GnollHack source: `C:\hmp\GnollHack` at HEAD <sha> (<clean / dirty> tree); <matches / differs from> the run's `SourceCodeHeadSha` <run sha>.
  - GnollHackWiki: `C:\hmp\GnollHackWiki` at HEAD <sha> (<clean / dirty> tree); <matches / differs from> the run's `WikiHeadSha` <run sha>.
  - Target page(s) exist; line endings CRLF; no generator.
- Analyst's source citations — for the reviewer, withheld from the validator:
  - C1: <path:line> (<one-line rationale>)
  - C2: <path:line> (<one-line rationale>)
- What each session does: <validation: read-only verdict per claim; execution: the edit, left uncommitted>
- Sequencing: <the steps behind the § 4a gate, or "nothing in the round depends on the wiki change">
- Closure criterion: <pre-declared, per § 5>
`````

### 4.2 Validation Prompt

````text
You are working in the GnollHack source repository at C:\hmp\GnollHack, which is this
session's project folder; paths below are repository-relative to it. This is a strictly
read-only session: do NOT edit, stage, commit, push or check out anything, in any repository.
Read-only git commands are fine.

Origin: Gnoll Overseer benchmark run <R>, finding <Tk>, ladder rung 2.
You are validating the claims of wiki handoff document wiki_handoff_prompt_v<V>.md.

Revision: the claims are to be checked at HEAD <sha> (<clean tree / dirty tree when recorded>).
Run `git rev-parse HEAD` first. If it prints another revision, do not check anything out: read
files with `git show <sha>:<path>` and search with `git grep -n <pattern> <sha> -- src include dat`,
and say in the report that you did.

Proposed page text:
<verbatim added text from Step 1, or the bulk table>

Claims to validate:
- C1: <claim, in the game's vocabulary>
- C2: <claim, in the game's vocabulary>

Task:
1. Locate the evidence for each claim yourself, in the GnollHack C and data source at <sha>.
   Do NOT consult NetHack source, the NetHack wiki or general NetHack knowledge; GnollHack
   diverges from NetHack. Settle every claim from this repository only.
2. Give each claim one verdict:
   - CONFIRMED: the source directly supports it. Cite exact path:line.
   - REFUTED: the source contradicts it. Cite exact path:line and state the correct mechanic
     in a player's vocabulary.
   - UNVERIFIABLE: you could not find source that settles it. Say where you looked.
3. Critique the proposed page text against what you confirmed: name any wording that implies
   more than the source supports (an over-generalization, a missing monster or item exception,
   a wrong condition). List the issues, or write "None".

Report format:
- A table: | Claim | Verdict | GnollHack source citation (path:line) | Notes / corrected statement |
- Wording critique: <the issues, or "None">
- The very last line of your response must be exactly "VALIDATION: PASS" or "VALIDATION: FAIL",
  with nothing after it — no explanation, no closing remark.
  VALIDATION: PASS is permitted ONLY if every claim is CONFIRMED and the wording critique is "None".
````

### 4.3 Execution Prompt

````text
You are working in the GnollHack wiki repository at C:\hmp\GnollHackWiki (HEAD <sha>, clean tree),
which is this session's project folder; paths below are repository-relative to it.
Do not edit any other repository. Do not commit or push; leave the changes in the working
tree and print the git commands at the end.

Precondition, checked by the person who pasted this, not by you: the mechanics below were
validated against the GnollHack source, and that validation ended VALIDATION: PASS for
wiki_handoff_prompt_v<V>.md.

Read .agents/skills/wiki_editing/SKILL.md and .agents/skills/wiki_bulk_edits/SKILL.md
first and follow them. No page generator exists; edit pages in place. The page text
carries no source-code references and stays in the page's own register, for players;
where the wording below conflicts with those skills, theirs wins — write the readable
version and say so in the report. A rewording may not add, drop or change a mechanic: the
wording below was validated and a changed mechanic is not. If the readable version would
need one, stop and report instead of editing.

Origin: Gnoll Overseer benchmark run <R>, finding <Tk>, ladder rung 2. <One sentence on
what the model got wrong and why the wiki is the right place to fix it.>

Verified on disk before this prompt was written:
- Target article: <path>. It carries <the fact>; <near-duplicate path> is <an overview /
  a shorter page> and is left as is.
- Pages to edit: <count> files matching <regex> under <directory>; <count> of them already
  contain a wikilink on that line.
- Line endings: CRLF, UTF-8 without BOM.

Task: make the pages match the diffs below. They specify the intended result and are not
to be applied with git apply; use the mechanism wiki_bulk_edits prescribes.
<Bulk edits only: the exact line form to write, keeping each page's value exactly as listed
in the table below; the diffs show three representative pages.> Skip <exceptions>.

```diff
<the Step 1 diff blocks, byte-identical>
```

<Bulk edits only: the Step 1 table of every page, its value and its claim number>

Verify: compare the hunks of `git diff` with the diffs above, ignoring the header lines,
and report every difference. <commands using the confirmed names, with the expected
numbers; grep for a distinctive player-facing phrase, never for a code identifier>. No
other lines may change (git diff --stat).

Report: mechanism used (in place), number of files changed, the exact line form written,
every place the result differs from the diffs above and why, the verification output, and
the git commands to commit and push.
````

## 4a. Sequencing: When the Round Waits for the Human

**Decide explicitly whether anything else in the round depends on the wiki change having landed**,
and say which way the answer went.

| Dependent on the wiki change | Independent of it |
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
document's framing — Step 2 in a chat opened on `C:\hmp\GnollHack`, then Step 3 in a chat opened
on `C:\hmp\GnollHackWiki`, only on `VALIDATION: PASS`; what is waiting on it; and the request in
plain words —

> Tell me when Step 2 has returned `VALIDATION: PASS`, you have run Step 3 in a GnollHackWiki
> chat, **and that session has finished** — or, if validation did not pass, paste the validation
> report here and I will revise the handoff. I will then continue with `<the dependent steps>`.

**All three facts, not one.** *"Validation passed"* and *"I pasted it"* are not the gate: the edit
may not exist yet, and every dependent step would read an unchanged tree.

**On `VALIDATION: FAIL`**, the round does not wait at the gate. The user returns the report; the
analyst revises the claims and the proposed text, writes the next `_v<V>` of the whole document
set, and hands off again; nothing dependent starts. A set member with no content change is copied
verbatim with the one-line harmonization note, and a plan already approved stays approved when
its copy is verbatim.

**Do not poll, do not proceed on a timer, and do not read a clean `C:\hmp\GnollHackWiki` working
tree as confirmation.** The wiki session leaves its changes uncommitted (§ 3), so a clean tree is
ambiguous between *not started* and *finished and committed*.

**On confirmation, verify read-only before continuing** rather than taking the report at face
value:

```powershell
git -C C:\hmp\GnollHackWiki status --porcelain
git -C C:\hmp\GnollHackWiki log -1 --format="%h %ad %s" --date=short
```

together with the § 2 grep for the distinctive player-facing phrase, whose count is recorded. If
the phrase is absent, say so and do **not** start the dependent work — the wiki session's job is
not done.

**Under a plan, the gate is a step of its own**, named as such in Proposed Changes with the
dependent steps listed beneath it, so a resumed session can see it has not been passed. Its
`task.md` checkbox is ticked after the read-only verification, never on the user's word alone.

**When nothing depends on it, say so explicitly** and close the round, with this message:

> Run Step 2 in a new chat opened on `C:\hmp\GnollHack`. If its last line is `VALIDATION: PASS`,
> run Step 3 in a new chat opened on `C:\hmp\GnollHackWiki`. If it is anything else, paste the
> validation report back here and I will revise the handoff in a new version. Nothing else in
> this round depends on the wiki edit.

Rung 2 is exempt from the re-run requirement (`server_benchmark_to_chat_transfer` § 9), so this
is the common case; the defect is silence about the check, not the absence of a gate.

**The gate and both closing messages also appear in the round's Developer Runbook**
([`server_benchmark_runbook`](../server_benchmark_runbook/SKILL.md)) as numbered steps — Step 2,
Step 3, the commit and push, and the wait for the 10-minute wiki re-index or the restart — so
the developer sees where the wiki work falls relative to the rubric import and the next run.
This section still owns the gate's wording and its read-only verification.

## 5. After the Wiki Session

When the user has committed and pushed the wiki change, record in
`server_benchmark_to_chat_transfer` § 11:

- the new `WikiHeadSha` the next run will show,
- the GnollHack HEAD the claims were validated against, and the verdict — `PASS`, and at which
  document version — as the user reported it, and
- the **pre-declared criterion** that says the finding is closed — for example, the assessor
  no longer marks the fact missing on the question that raised it, or `wiki_view` of the page
  resolves the section.

Rung 2 is exempt from the re-run requirement (§ 9), so this is a record, not a gate.

## 6. Worked Examples

### Run 34, T4 (2026-09-10)

The handoff prompt presumed a page generator or template that does not exist, so the wiki
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

## 7. Cross-References

- [`server_benchmark_to_chat_transfer`](../server_benchmark_to_chat_transfer/SKILL.md) — § 2
  category 4 (check the corpus on disk before filing), § 7 rung 2 (the ladder and the
  authorship rule), § 9 (the rung-2 re-run exemption), § 11 (where the outcome is recorded)
- [`server_tool_data_sources`](../server_tool_data_sources/SKILL.md) — § 3 for the two standard
  clone locations, `C:\hmp\GnollHack` and `C:\hmp\GnollHackWiki`, and why only their HEADs are
  compared with `SourceCodeHeadSha` and `WikiHeadSha`; § 6 corpus provenance
- [`server_rubric_handoff`](../server_rubric_handoff/SKILL.md) — the sibling handoff whose store
  has **no** session to delegate to. Its human steps in the Admin UI are irreducible, so § 3a's
  contract does not transfer to it: there, the numbered steps for the human are the deliverable
- `.agents/skills/wiki_editing/SKILL.md` and `.agents/skills/wiki_bulk_edits/SKILL.md` in the
  **GnollHackWiki** repository, cited by repository-relative path because they live in another
  repository
