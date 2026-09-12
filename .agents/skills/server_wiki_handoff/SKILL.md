---
name: server_wiki_handoff
description: >-
  Mandatory method for turning a rung-2 wiki finding from an Overseer AI benchmark analysis into
  a handoff prompt for a separate session in the GnollHackWiki clone at WikiPath. Covers the
  read-only pre-flight checks against WikiPath the analyst must do before writing the prompt
  (target page exists, which of two similarly named pages carries the fact, exact match count,
  existing links, clean tree, line endings, no presumed generator), what the prompt must not
  restate or prescribe, the prompt template, and what to record after the wiki change lands.
  Read with the other four benchmark skills before the first finding is written; act on it
  whenever a finding lands on ladder rung 2.
---

# Wiki Handoff: Turning a Rung-2 Finding Into a Prompt Another Session Can Execute

## 1. Purpose and When It Binds

A rung-2 wiki finding (`server_benchmark_to_chat_transfer` § 7) leaves this repository as a
prompt for a **separate session** opened in the GnollHackWiki clone at `WikiPath`. That
session cannot ask the analyst anything, so **the prompt is the whole interface**.

The division of labour is fixed:

- **The analyst does the checks only someone with the run context and read access to
  `WikiPath` can do** — which page carries the fact, how many pages the edit touches, what the
  run actually saw.
- **The wiki's own skills own the wiki's conventions** — `wiki_editing` for link syntax,
  headings and emoji rules, `wiki_bulk_edits` for cross-page mechanics. The prompt cites them
  by name and does not restate them.

This skill is read with the other four benchmark skills on **every** benchmark analysis
(`.agents/AGENTS.md`, "AI Benchmark Findings"); its body is acted on only when a finding lands
on rung 2.

Reading `WikiPath` is read-only research in another repository and is permitted. **Editing it
from this repository's session is not** — that is what the handoff prompt exists for.

## 2. Pre-Flight Checklist

Every item is done **read-only** against `WikiPath` before the prompt is written, and its
result is copied into the prompt.

- **Resolve `WikiPath`** from configuration (`server_tool_data_sources` § 2) and record
  `git -C <WikiPath> rev-parse HEAD`. Compare it with the run's `WikiHeadSha` and say in the
  prompt whether they match; a mismatch means the run read a different revision than the one
  the wiki session will edit.
- **Confirm the target page exists by path.** If two or more pages have similar names, open
  each, grep for the fact, and read the history of each:

  ```powershell
  git -C <WikiPath> log --format="%h %ad %s" --date=short -- "<file>"
  ```

  Name **the page that carries the fact** in the prompt. Name the near-duplicate too, and say
  what to do about it — usually nothing.
- **Count the pages the edit will touch, with the exact regex the prompt will prescribe**, and
  state that count. Check whether any of those lines already carries a wikilink; if some do,
  the prompt must say whether they are skipped or rewritten.
- **Check the working tree is clean**: `git -C <WikiPath> status --porcelain` is empty. If it
  is not, say so in the prompt — the wiki session must not overwrite uncommitted work.
- **Check the line endings** of a sample of the target files. CR-terminated line count equal to
  newline count means CRLF.
- **Do not assert that a generator, template or data file exists unless you found one.** There
  is none for spell, monster or item pages as of 2026-09-10; every such page is hand-maintained
  Markdown. If you looked and found none, the prompt says "edit in place".
- **Every fact the edit adds is verified** against the game source or another primary text, and
  the citation proving it goes **in the handoff document and the analysis — never into the text
  the page will carry** (`server_benchmark_to_chat_transfer` § 7 rung 2 authorship rule). The
  wiki is player documentation and forbids source references on a page outright; `wiki_editing`
  § 4 and § 17 own that rule. Verification is what licenses an AI-authored wiki edit, and a
  reviewer checks it in the handoff, where it is read once, rather than on the page, which is
  read by every player forever. A link-only edit needs no citation beyond the target page.

## 3. What the Prompt Must Not Do

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
  fear"*, never `` `MR_FEAR` ``. The code locations belong in the evidence table above the
  prompt, which is what a reviewer reads. `wiki_editing` § 4 owns the rule; the prompt does not
  restate it, it simply must not ask for text that breaks it.
- **Do not verify with a grep for a code identifier.** A `Verify:` step reading
  `grep -c "MR_FEAR"` can only pass if a macro name reached a player-facing page, so the prompt's
  own verification enforces the defect the bullet above forbids. Grep for a distinctive
  player-facing phrase from the sentence instead.
- **Do not prescribe a heading or a table per finding.** One added fact is usually one sentence,
  extending what the page already says. `wiki_editing` § 17 owns the shape of a benchmark-driven
  edit; a prompt that dictates structure overrides it from the wrong side of the handoff.

## 4. Prompt Template

```text
You are working in the GnollHack wiki repository at <WikiPath> (HEAD <sha>, clean tree).
Do not edit any other repository. Do not commit or push; leave the changes in the working
tree and print the git commands at the end.

Read .agents/skills/wiki_editing/SKILL.md and .agents/skills/wiki_bulk_edits/SKILL.md
first and follow them. No page generator exists; edit pages in place. The page text
carries no source-code references and stays in the page's own register, for players;
where the wording below conflicts with those skills, theirs wins — write the readable
version and say so in the report.

Origin: Gnoll Overseer benchmark run <N>, finding <Tk>, ladder rung 2. <One sentence on
what the model got wrong and why the wiki is the right place to fix it.>

Verified on disk before this prompt was written:
- Target article: <path>. It carries <the fact>; <near-duplicate path> is <an overview /
  a shorter page> and is left as is.
- Pages to edit: <count> files matching <regex> under <directory>; <count> of them already
  contain a wikilink on that line.
- Line endings: CRLF, UTF-8 without BOM.

Task: <exact edit, with the exact line form to write, keeping any per-page value exactly as
found>. Skip <exceptions>.

Verify: <commands using the confirmed names, with the expected numbers; grep for a
distinctive player-facing phrase, never for a code identifier>. No other lines may
change (git diff --stat).

Report: mechanism used (in place), number of files changed, the exact line form written,
anything changed from the wording above and why, the verification output, and the git
commands to commit and push.
```

## 5. After the Wiki Session

When the user has committed and pushed the wiki change, record in
`server_benchmark_to_chat_transfer` § 11:

- the new `WikiHeadSha` the next run will show, and
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
- [`server_tool_data_sources`](../server_tool_data_sources/SKILL.md) — § 2 path resolution for
  `WikiPath`, § 6 corpus provenance and `WikiHeadSha`
- `.agents/skills/wiki_editing/SKILL.md` and `.agents/skills/wiki_bulk_edits/SKILL.md` in the
  **GnollHackWiki** repository, cited by repository-relative path because they live in another
  repository
