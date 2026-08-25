@../.agents/AGENTS.md

## Claude Code

This project keeps its AI configuration in the tool-neutral `.agents/` directory so
that agents other than Claude can be pointed at the same files. `.claude/` is only a
thin adapter layer over it.

- **Project rules**: `.agents/AGENTS.md`, imported at the top of this file. Edit the
  rules there, not here.
- **Skills**: the full bodies live in `.agents/skills/<name>/SKILL.md`. Each one has a
  matching pointer stub in `.claude/skills/`, which exists purely so Claude Code
  discovers and triggers them.
- **Naming**: canonical folders use underscores (`scss_compilation`); the invocable skill
  name is the hyphenated form (`scss-compilation`), as the skill spec requires. Skill
  bodies cross-reference each other by the underscore folder name — that is correct and
  refers to the canonical file.
- **One exception — `server-implementation-planning`**: this is a **native** Claude
  Code skill whose full body lives in
  `.claude/skills/server-implementation-planning/SKILL.md`. It is Claude-specific — it
  describes this harness's plan mode and its `ExitPlanMode` approval mechanism — so it
  has **no** counterpart in `.agents/skills/` and must not be converted into a pointer
  stub. Edit it in place. The name is deliberately prefixed: skill names share one flat
  global namespace across projects, user-level skills, and plugins, and a generic
  `implementation-planning` collides with the skill of that name in the GnollHack
  repository — the loser of such a collision is silently never loaded.
- **Scratch files**: `.agents/AGENTS.md` names an Antigravity-specific scratch path
  (`<appDataDir>\brain\<conversation-id>\scratch\`). Claude Code has no such directory —
  use the session scratchpad directory Claude Code reports in its own environment
  instead. The binding part of that rule still holds: **never** write temporary files,
  scratch scripts, or guidance files anywhere inside the repository, including the
  repository root. The sole exception is `.plans/`, which is gitignored and is the
  intended home for AI-produced documents.

<!-- Maintainer note: a stubbed skill's `description` is duplicated into its stub, because
     the description is what Claude Code indexes for triggering. If you change a
     description in .agents/skills/, mirror it into the matching .claude/skills/ stub or
     the skill will trigger on stale wording. Bodies and references are never duplicated.
     Regenerate all stubs from the canonical files rather than editing them by hand.
     `server-implementation-planning` is exempt — it is a native skill with no
     canonical source to drift from. -->

## Shell and Commands (Windows)

Development on this project happens on **Windows**. Claude Code's primary shell here is
**PowerShell**; a Git Bash `Bash` tool may also be available. They are not
interchangeable — each takes its own syntax. **Default to PowerShell.**

- **Do not assume Unix utilities exist.** `od`, `xargs`, `wc`, `sed`, `awk`, `file`, and
  Bash process substitution (`diff <(...)`) are unavailable in PowerShell.
- **Windows PowerShell 5.1 limits**: `&&` and `||` are parser errors — write
  `A; if ($?) { B }` instead. There is no ternary (`?:`), null-coalescing (`??`), or
  null-conditional (`?.`) operator. `ConvertFrom-Json` returns a `PSCustomObject` and has
  no `-AsHashtable`.
- **Substitutions for common Unix commands**:

  | Unix | PowerShell |
  |------|------------|
  | `head -n N` / `tail -n N` | `Get-Content f -TotalCount N` / `Get-Content f -Tail N` |
  | `which x` | `(Get-Command x).Source` |
  | `wc -l f` | `(Get-Content f \| Measure-Object -Line).Lines` |
  | `mkdir -p d` | `New-Item -ItemType Directory -Force d` |
  | `rm -rf d` | `Remove-Item -Recurse -Force d` |
  | `2>/dev/null` | `2>$null` |
  | `VAR=x cmd` | `$env:VAR = 'x'; cmd` |

- **Never use `grep`, `head`, or `file` to inspect line endings.** Git Bash/MSYS and WSL
  open files in text mode and silently strip CR, so they report LF for a CRLF file — with
  no error. Acting on that produces a file with mixed line endings, which is worse than
  either convention. Read the bytes instead:

  ```powershell
  $b = [System.IO.File]::ReadAllBytes('path')
  "CR=$(@($b | Where-Object { $_ -eq 0x0D }).Count) LF=$(@($b | Where-Object { $_ -eq 0x0A }).Count)"
  ```

  In a clean CRLF file the two counts are equal.
- **Line endings**: `.gitattributes` sets `* text=auto`, so the repository stores **LF**
  and a Windows working tree holds **CRLF**. Write **CRLF** and let Git normalize on
  commit. When modifying an existing file, match what it already uses rather than
  guessing from the OS; never mix conventions within one file. `git ls-files --eol <path>`
  reports both sides (`i/` = index, `w/` = working tree).
- **No BOM.** Write UTF-8 without a byte order mark. Note that PowerShell 5.1's
  `Set-Content -Encoding utf8` and `Out-File -Encoding utf8` **add** a BOM — use
  `[System.IO.File]::WriteAllText($p, $t, (New-Object System.Text.UTF8Encoding($false)))`
  when writing a file from PowerShell.
- **`git`, `dotnet`, `npm`, and `npx` work normally** and take their usual arguments. It
  is the shell built-ins and the Unix coreutils that differ, not the toolchain.

### Installing Tools — Ask, Do Not Skip

When a task needs a tool, package, or library that is not installed, **ask the user
whether to install it.** Do not silently drop the step, weaken the approach, or
substitute a worse method to avoid the install.

- **Ask; do not install unprompted.** Installing changes the user's machine, so it is
  their decision — but it is a decision they must actually be given.
- **Do not quietly work around a missing tool.** Skipping a verification step, replacing a
  real parser with a regex heuristic, or downgrading a check to "probably fine" produces
  weaker work while looking complete. That is worse than pausing to ask.
- **Say what is missing and what it buys.** Name the tool, the command that would install
  it, and what becomes possible with it. "PyYAML is not installed — with it I can
  actually parse all 18 frontmatter blocks instead of pattern-matching them; install with
  `python -m pip install pyyaml`?"
- **If the user declines**, proceed with the best available approach and **state plainly
  in the final report** which check was weakened or skipped, and how.
- **Prefer project-local and already-declared dependencies.** If the tool belongs in
  `package.json` or a `.csproj`, adding it there is a project change and needs a plan, not
  just an install.

## Implementation Plans and AI Documents

**Non-trivial tasks require a written implementation plan, approved by the user before
any source file is modified.** Read the `server-implementation-planning` skill
(`.claude/skills/server-implementation-planning/SKILL.md`) for the full specification.
The summary below is binding regardless of whether the skill is triggered.

### When to Plan

A plan is **required** when any of these are true:

- The task touches **more than one file**, or more than one project
  (`MobileGnollHackLogger`, `Overseer`, `GnollHackServer.Data`, `Overseer.Tests`)
- It requires an **EF Core migration**
- It requires **SCSS-to-CSS recompilation** or an **Angular rebuild**
- It is a refactor, a new feature, or anything the user describes as non-trivial

A plan is **not** required for single-file fixes, typos, comment edits, answering
questions, or read-only investigation.

### Lifecycle (Five Phases)

1. **Research** — read files, search code, understand implications. **No edits.**
2. **Write the plan** — save it as a Markdown file inside the repository under the
   gitignored `.plans/` directory. Tell the user the file path so they can review it.
   Under plan mode, write it to the harness plan file and copy it to `.plans/` **before**
   requesting approval.
3. **Wait for approval** — **STOP.** Do not edit any source file until the user
   explicitly approves the plan. Request approval via `ExitPlanMode` when plan mode is
   active; otherwise print a brief summary and wait.
4. **Execute** — implement the approved plan step by step. If significant deviation is
   needed, stop, update the plan, and re-confirm.
5. **Verify** — build, run tests, confirm correctness. Summarize results.

### Plan Document Format

See the `server-implementation-planning` skill for the full template. The plan **must**
include at minimum:

- **Goal** — what and why
- **Affected Files** — table of every file touched
- **Build Impact** — SCSS compilation / EF Core migration / Angular build, or "None"
- **Proposed Changes** — grouped by component, ordered by dependency
- **Subagent Use** — mandatory even if "No"
- **Risks** — what could break
- **Verification Plan** — how correctness will be confirmed

### Saving Plans and Other Documents

All AI-produced documents — implementation plans, reviews, analyses, reports, and other
structured artifacts — are saved **inside the repository** under the gitignored
`.plans/` directory, using this structure:

```
.plans/YYYY-MM-DD/task_name/<document_name>_v<N>.md    ← N=1 for the first version
```

- **Date directory** (`YYYY-MM-DD`): the creation date of the **first document** for the
  task. Follow-up documents for the same task reuse the same date directory.
- **Task directory**: a short, descriptive `snake_case` name (e.g., `chat_page_update`).
- **Create subdirectories** if they do not exist.
- **Conflict resolution**: if a task directory with the desired name already exists under
  the same date, find the next free name in the sequence `task_name`, `task_name_2`,
  `task_name_3`, ... — never rename the existing folder, and never nest suffixes (e.g.,
  `_2_3` is wrong). Use the new directory for all work — do not touch the conflicting
  directory.

### Document Versioning (STRICT)

This applies to **all** documents in `.plans/` — implementation plans, reviews,
analyses, and reports:

- **First version** always gets a `_v1` suffix (e.g., `implementation_plan_v1.md`,
  `bug_analysis_v1.md`, `code_review_v1.md`).
- **Never overwrite** an existing version. To revise, create a new file with the next
  version number (read `_v1` → write `_v2`, read `_v2` → write `_v3`).
- Check which versions already exist before creating a revision.
- `task.md` and `walkthrough.md` are **singular** (no version suffix) — based on
  whichever plan version was ultimately approved. The walkthrough must state which plan
  version was implemented. **Follow-up rounds** use lettered variants (`task_A.md`,
  `walkthrough_A.md`, etc.) — see the `server-implementation-planning` skill for the
  full specification.

To deliver a document:

1. **Use your native file-writing tool** (`Write`) to create the document at
   `.plans/YYYY-MM-DD/task_name/<document_name>_v<N>.md`. **Do NOT use shell commands**
   (`cat << EOF`, heredoc, `echo`) — Markdown content contains backticks, dollar signs,
   and angle brackets that cause shell quoting failures and corrupted output, and shell
   redirection produces LF where this tree needs CRLF.
2. Print the file path in chat so the user can open and review it.
3. Print a **brief summary** — not the full document — directing the user to the file.
4. For implementation plans, wait for the user's approval before proceeding to execution.

### Claude Code Plan Mode

Plan mode restricts editing to its own plan file (`~/.claude/plans/<slug>.md`), which
conflicts with the `.plans/` rule above. **The harness wins.** Resolve it like this:

1. **Write** the plan to the harness plan file. In-place editing of that file is expected
   and does not violate the `_v<N>` rule — versioning binds to the `.plans/` copy only.
2. **Copy** the finished plan to
   `.plans/YYYY-MM-DD/task_name/implementation_plan_v1.md` — **before** asking for
   approval, not after. `.plans/` is the source of truth: other AIs read revisions from
   there and never look in `~/.claude/`, and the document then survives a rejection or a
   lost session. Creating the task directory is part of this step.
3. **Print** both paths in chat, plus a brief summary.
4. **Request approval** with the `ExitPlanMode` tool. Do not ask "is this plan okay?" in
   chat text — that is what the tool is for.
5. **On approval**, create `task.md`, execute, and finish with `walkthrough.md`.

> **Why step 2 is allowed during plan mode**: plan mode's restriction exists to keep the
> agent from changing the project before the user approves the work. Copying the plan
> document to its canonical location is a copy of the planning artifact — it touches no
> source file, project file, migration, or generated asset. Everything that would
> actually change the project still waits for approval.
