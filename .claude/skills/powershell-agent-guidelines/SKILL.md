---
name: powershell-agent-guidelines
description: >-
  Guidelines and best practices for executing PowerShell commands, running scratch scripts,
  and handling file I/O safely on Windows for AI agents. Covers quoting, exit codes,
  JSON serialization depth, UTF-8/BOM prevention, and avoiding common PowerShell 5.1 pitfalls.
  Also covers the PowerShell 5.1 parser limits (no &&, ||, ternary, or null-coalescing),
  Unix-to-PowerShell command equivalents, and avoiding commands that hang on missing stdin.
---

# PowerShell Guidelines for Windows (Claude Code)

The canonical, tool-neutral guidelines live in this repository's shared agent directory:
`.agents/skills/powershell_agent_guidelines/SKILL.md` (path relative to the repository root).

**Read `.agents/skills/powershell_agent_guidelines/SKILL.md` in full before proceeding, and follow it.**
The sections below provide mandatory Claude Code-specific harness mechanics and integration instructions.

---

## Claude Code Shell Environment on Windows

Claude Code commands run on **Windows** using **PowerShell** as the primary execution environment.

1. **Direct Execution:**
   - Write PowerShell commands directly. **Do NOT wrap commands in `powershell -Command "..."` or `powershell.exe -c "..."`**, as this creates nested parsing layers that strip quotes and corrupt variable interpolation.
   - Run scratch `.ps1` scripts **in-session** with `& 'C:\full\path\script.ps1'`. Claude Code's session already runs at `Get-ExecutionPolicy -Scope Process` = `Bypass`, so the `powershell.exe -ExecutionPolicy Bypass -File` fallback in the canonical skill is **not needed here** — and using it would discard the session's encoding defaults.

2. **A `Bash` Tool Also Exists — They Are Not Interchangeable:**
   - This session has both a **PowerShell** tool (primary; Windows PowerShell 5.1) and a Git Bash **`Bash`** tool. Each takes its own syntax. **Default to PowerShell**; reach for `Bash` only for a genuinely POSIX task.
   - **Never use `grep`, `head`, or `file` to inspect line endings.** Git Bash/MSYS and WSL open files in text mode and silently strip CR, reporting LF for a CRLF file **with no error**. Acting on that produces a file with mixed line endings — worse than either convention. Count bytes via .NET, as the canonical skill documents.
   - Note that `bash` on the PATH *from PowerShell* resolves to `C:\Windows\system32\bash.exe` — **WSL**, a different filesystem and toolchain from the Git Bash the `Bash` tool uses. `bash -c` from PowerShell is not the same environment as the `Bash` tool.

3. **Native Tools vs. Shell Commands:**
   - **Always use Claude Code's native file tools (`Write`, `Edit`)** for creating and editing files. There is no `Replace` or `MultiEdit` tool — calling one wastes a turn on a validation error.
   - **Prefer the native search and read tools over their cmdlet equivalents**: `Grep` over `Select-String`, `Glob` over `Get-ChildItem -Recurse`, `Read` over `Get-Content`. Besides integrating with the permission UI and producing clickable results, this sidesteps the `Get-Content` ANSI/BOM decoding trap entirely.
   - **Do NOT use shell redirection (`>`, `>>`)** or shell echo to author or append to files. See the canonical skill for what PS 5.1 actually writes — it is host-dependent and wrong either way.

4. **Checking Return Codes:**
   - PowerShell's `$ErrorActionPreference = 'Stop'` only catches cmdlet errors. When running CLI tools (`dotnet`, `npm`, `npx`, `git`, `msbuild`), **always check `$LASTEXITCODE`**:
     ```powershell
     dotnet build
     if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
     ```
   - **The harness reports the whole call's status from the exit code left at the end.** Later cmdlets do not reset `$LASTEXITCODE`, so a native command that fails mid-script marks an otherwise-successful call as failed. Either put the exit-code check last, or reset with `$global:LASTEXITCODE = 0` after an intentionally-failing probe.

5. **Shell State Does Not Persist Between Tool Calls:**
   - Measured in Claude Code: **only the working directory persists.** Variables, functions, imported modules, and `$env:` values are gone by the next PowerShell call — a marker set in one call read as empty in the next.
   - This is the confirmed instance of the canonical skill's safe default. Set everything a command needs within that same command, or write it to a scratch file. Never split a `$var = …` from its use across two calls, and do not bother unsetting `$env:` variables here — there is nothing to clean up.

6. **Temporary Files & Scratch Scripts:**
   - **Never** write scratch scripts, temporary files, or test data to the repository root or source tree. The sole in-repository exception is `.plans/`, which is gitignored.
   - Use the session scratchpad directory Claude Code reports in its environment — Claude Code has no `<appDataDir>\brain\...` directory, so ignore that path in the canonical skill.

7. **Line Endings & Encodings:**
   - The working tree holds **CRLF** (`.gitattributes` sets `* text=auto`, so the repository stores LF). Write CRLF for new files and let Git normalize on commit — but when modifying an existing file, **match what it already uses** rather than inferring from the OS. Never mix conventions within one file.
   - **The `Write` tool emits pure LF**, so a newly created file does not satisfy the CRLF convention on its own. `Edit` on an existing CRLF file preserves CRLF, so this affects **new files only**. After `Write` creates one, verify CR==LF and normalize if not:
     ```powershell
     $t = [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8)
     $t = ($t -replace "`r`n", "`n") -replace "`n", "`r`n"
     [System.IO.File]::WriteAllText($p, $t, (New-Object System.Text.UTF8Encoding($false)))
     ```
   - Write UTF-8 **without** a BOM. Note that PowerShell 5.1's `Set-Content -Encoding utf8` and `Out-File -Encoding utf8` **add** one.
   - This session pre-sets `$PSDefaultParameterValues['Out-File:Encoding'] = 'utf8'`, which is why `>` here produces UTF-8-with-BOM rather than the stock UTF-16 LE. Treat that as an undocumented implementation detail: if the encoding matters, verify the bytes rather than trusting the value.
   - Do not use Unix commands (`grep \r`, `file`) to inspect line endings — see item 2. Inspect via `.NET` as documented in the canonical skill.
