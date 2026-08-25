---
name: powershell_agent_guidelines
description: >-
  Guidelines and best practices for executing PowerShell commands, running scratch scripts,
  and handling file I/O safely on Windows for AI agents. Covers quoting, exit codes,
  JSON serialization depth, UTF-8/BOM prevention, and avoiding common PowerShell 5.1 pitfalls.
  Also covers the PowerShell 5.1 parser limits (no &&, ||, ternary, or null-coalescing),
  Unix-to-PowerShell command equivalents, and avoiding commands that hang on missing stdin.
---

# PowerShell & Windows Shell Guidelines for AI Agents

## Overview
Development and tool execution in this repository take place on **Windows**. Terminal tool commands execute directly inside **PowerShell**. These guidelines prevent syntax errors, escaping failures, execution policy blocks, encoding/line-ending corruption, process hangs, and state pollution.

> [!NOTE]
> Every behaviour asserted in this document was measured on **Windows PowerShell 5.1.26100.9168**
> (Desktop edition, Windows 11 Pro 26200) on 2026-08-25 — not recalled from documentation. Where
> a rule depends on the host, the document says how to check rather than what to assume.
> Re-verify after a major Windows or PowerShell upgrade.

---

## 1. Direct Execution — Do NOT Wrap in PowerShell
- **DO NOT** wrap commands in nested `powershell` or `powershell.exe -c` wrappers. The tool environment already runs directly in a PowerShell session.
- Running nested PowerShell processes causes inner variables (such as `$_`, `$f`, `$var`), quotes, and newlines to get prematurely expanded, stripped, or corrupted by the outer shell parser. A child process also **loses the caller's session state** — see §3 and §6.
- **DO** write PowerShell commands and CLI invocations directly into the terminal tool's command input (e.g., `Get-ChildItem -Path .`, `dotnet build`, `git status`).

---

## 2. PowerShell 5.1 Language Limits (Read First)
Windows PowerShell 5.1 is **not** PowerShell 7. Several constructs that agents write by reflex are **parse errors** — the command fails before a single line executes, so no partial work happens and the error message often does not name the real cause.

**Confirm which shell you are in before trusting anything below:**
```powershell
"$($PSVersionTable.PSVersion) $($PSVersionTable.PSEdition)"   # expect e.g. 5.1.26100.9168 Desktop
```
Under PowerShell 7 (`Core`) every restriction in this section is lifted, and several encoding rules in §6 invert.

| You want to write | PS 5.1 result | Write instead |
|---|---|---|
| `A && B` | **Parse error**: `The token '&&' is not a valid statement separator in this version.` | `A; if ($?) { B }` |
| `A \|\| B` | **Parse error**, same cause | `A; if (-not $?) { B }` |
| `A; B` (unconditional) | Fine | — |
| `$c ? $x : $y` | **Parse error**: `Unexpected token '?' in expression or statement.` | `if ($c) { $x } else { $y }` |
| `$a ?? $b` | **Parse error**: `Unexpected token '??' in expression or statement.` | `if ($null -eq $a) { $b } else { $a }` |
| `$obj?.Prop` | **Parses without error — and does the wrong thing** | `if ($null -ne $obj) { $obj.Prop }` |

> [!CAUTION]
> **`?.` is the dangerous one.** `?` is a legal character in a PowerShell variable name, so
> `$null?.Length` parses cleanly as a variable named `null?` followed by `.Length`. There is no
> error to notice — the null-conditional silently means something else. The others at least fail
> loudly.

- **`ConvertFrom-Json` returns a `PSCustomObject`, not a hashtable**, and PS 5.1 has **no `-AsHashtable`** parameter. Access members with dot notation, or convert explicitly. (For the serialization direction, see the depth trap in §7.)

---

## 3. Multi-Line & Complex Logic — Use Scratch Scripts
- **DO NOT** write complex multi-line loops, pipeline blocks, or regex replacements inline in a single command string. Escaping quotes and variables across tool JSON boundaries is error-prone.
- **DO write a `.ps1` script** to the agent's dedicated scratch directory (`<appDataDir>\brain\<conversation-id>\scratch\script.ps1`) using the file-writing tool.
- **Script Execution — run it in the current session:**
  ```powershell
  & 'C:\full\path\to\scratch\script.ps1'
  ```
  This is the primary form. It keeps the session's `$PSDefaultParameterValues`, preference
  variables, and working directory, and it does not violate §1.
- **Fallback only if a policy blocks the script** (`PSSecurityException`):
  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'C:\full\path\to\scratch\script.ps1'
  ```
  > [!WARNING]
  > A child process **does not inherit the caller's `$PSDefaultParameterValues`**. Measured: the
  > same `'x' > file` statement wrote UTF-8-with-BOM in a harness session that had pre-set an
  > `Out-File:Encoding` default, but genuine **UTF-16 LE** (`FF FE`) inside
  > `powershell.exe -File`. Check `Get-ExecutionPolicy -Scope Process` before reaching for this —
  > many agent harnesses already run as `Bypass`, making the fallback unnecessary.
- **ASCII in Scratch Scripts:** Windows PowerShell 5.1 parses BOM-less `.ps1` files using the system ANSI code page (Windows-1252). Keep script source code ASCII-only (use Unicode escape codes like `[char]0x2014` or read UTF-8 data via .NET) to avoid script parsing syntax errors.

---

## 4. Error Handling & Native Command Quirks (Crucial)
- **Cmdlet Errors:** Place `$ErrorActionPreference = 'Stop'` at the top of your scripts to halt on PowerShell cmdlet errors.
- **Native Executables (git, dotnet, msbuild):** The Stop preference does NOT halt on native executable failures. You MUST explicitly check the process exit code:
  ```powershell
  dotnet build
  if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
  ```
  Measured: `cmd.exe /c "exit 3"` leaves `$LASTEXITCODE = 3` and `$? = $false`, and throws nothing.
- **The Stderr Redirect Crash:** If the Stop preference is active, redirecting stderr from a native command (e.g., appending `2>&1`) will instantly throw a fatal exception if the tool writes to stderr, even if it succeeds! Measured: a command that wrote one stderr line and **exited 0** threw `NativeCommandError`. Temporarily set the preference to `'Continue'` around noisy native commands if you must capture stderr logs.
- **`-ErrorAction SilentlyContinue` does not make a failure harmless.** It suppresses the error *output*, but the cmdlet still failed and the call still reports failure. To make a cmdlet failure genuinely non-fatal, promote it to terminating and swallow it:
  ```powershell
  try { Get-Item 'maybe-missing' -ErrorAction Stop } catch { }
  ```
  Without `-ErrorAction Stop` a *non-terminating* error bypasses `catch` entirely — the `try` block is not a safety net on its own.

---

## 5. Quoting, Strings, and Calling Executables
- **Single vs. Double Quotes:**
  - Use **single quotes** (`'...'`) by default for string literals, file paths, and regex patterns to prevent accidental variable expansion (`$`).
  - Use **double quotes** (`"..."`) only when variable interpolation is explicitly needed.
  - Subexpressions in double quotes: `$obj.Prop` only interpolates `$obj`. Use `$($obj.Prop)` to access properties inside double quotes.
- **Backtick Escaping:** PowerShell's escape character is the backtick (`` ` ``), NOT the backslash (`\`):
  - Newline: `` `n `` | Carriage Return: `` `r `` | Tab: `` `t `` | Literal double-quote: `` `" `` | Literal dollar sign: `` `$ `` | Literal backtick: ```` `` ````
- **Executables with Spaces in Path:**
  - Prefix with the call operator `&` when the executable path is quoted or stored in a variable:
    ```powershell
    & "C:\Program Files\dotnet\dotnet.exe" build
    & $msbuildPath win\win32\vs\makedefsdroid.vcxproj
    ```
  - Without `&`, PowerShell treats the quoted string as a literal and outputs the text instead of running the program.
- **Multi-line arguments to native executables — use a here-string.** Commit messages, PR bodies, and file content passed on a command line are the usual cases:
  ```powershell
  git commit -m @'
  Fix the thing.

  Second line with $literal dollar signs and `backticks` left alone.
  '@
  ```
  - Use `@'...'@` (**single**-quoted, literal) so `$` and backticks are not expanded. `@"..."@` interpolates — only use it when you want that.
  - The closing `'@` **must be at column 0**, on its own line. Indenting it is a parse error.
- **Arguments PowerShell wants to parse as operators:** use the stop-parsing token `--%` to pass the rest of the line through verbatim:
  ```powershell
  git log --% --format=%H
  ```

---

## 6. File Read/Write: Encoding, Redirection, and Line Endings
This is the single most common area where AI agents corrupt files on Windows.

- **NO REDIRECTION OPERATORS (`>` / `>>`):**
  **DO NOT** use `>` or `>>` to write or append to text files. What you get depends on the session, and **every** variant is wrong for a tree that requires BOM-less UTF-8:

  | Context | Bytes from `'hello' > file` |
  |---|---|
  | Stock PS 5.1 (including any child `powershell.exe`) | `FF FE …` — **UTF-16 LE** |
  | A session where the harness pre-set `$PSDefaultParameterValues['Out-File:Encoding'] = 'utf8'` | `EF BB BF …` — **UTF-8 with BOM** |

  Both break git diffs and compilers. Because the outcome is host-dependent, do not reason about
  which one you will get — use the .NET methods below instead. (`>` and `>>` are aliases for
  `Out-File`, so the same applies to it.)
- **`Set-Content` / `Add-Content` write ANSI**, not UTF-8. Measured: an em-dash became the single byte `97` (Windows-1252), which does not round-trip. Pass `-Encoding utf8` if you must use them — but note that PS 5.1's `utf8` **adds a BOM**, so prefer .NET.
- **Writing/Appending Files (BOM Prevention):**
  Use .NET methods to write clean BOM-less UTF-8:
  ```powershell
  $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
  [System.IO.File]::WriteAllText($absPath, $text, $utf8NoBom)
  [System.IO.File]::AppendAllText($absPath, "appended text`r`n", $utf8NoBom)
  ```
- **Absolute Paths with .NET Methods (Beware Resolve-Path):**
  `[System.IO.File]` methods use the underlying .NET working directory, which does **not** track PowerShell's `$PWD` variable. Measured: after a `Push-Location`, `$PWD` pointed at the new directory while `[Environment]::CurrentDirectory` still pointed at the original.
  - Always pass absolute paths to .NET methods.
  - **DO NOT use the `Resolve-Path` cmdlet for creating new files** — it throws `ObjectNotFound` if the file does not exist yet.
  - **DO use `$PWD`** to construct absolute paths safely: `Join-Path $PWD.Path 'newfile.txt'`.
    > `$PWD` is a `PathInfo` object, not a string. `"$PWD\newfile.txt"` happens to work on a
    > FileSystem drive, but on any other PSDrive the interpolation yields a provider-qualified
    > path that .NET cannot open. `$PWD.Path` is the safe form.
- **Reading Files (Preserving Line Endings & UTF-8):**
  - `Get-Content` **strips** `\r\n` line endings and returns a string array unless you pass `-Raw`.
  - `Get-Content` honours a **BOM**, and falls back to ANSI (Windows-1252) only when there is none. Measured on the same em-dash content, with no `-Encoding`:

    | Input file | Result |
    |---|---|
    | UTF-8 **without** BOM | `em-dash: â€”` — mojibake |
    | UTF-8 **with** BOM | `em-dash: —` — correct |

    Because a BOM-less tree is the convention here, **`-Encoding UTF8` is mandatory on every read** — otherwise non-ASCII silently corrupts, and only on the files that have any.
  - To read full raw text cleanly while preserving UTF-8 and exact line endings:
    ```powershell
    # Recommended (.NET UTF-8):
    $text = [System.IO.File]::ReadAllText($absPath, [System.Text.Encoding]::UTF8)

    # Or native PowerShell with explicit UTF-8 and -Raw:
    $text = Get-Content -Path $absPath -Encoding UTF8 -Raw
    ```
- **In-Place File Text Replacement (sed equivalent):**
  - **Literal replacement (recommended for exact code snippets):**
    ```powershell
    $f = Join-Path $PWD.Path 'path\to\file.txt'
    $t = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8).Replace('exact_old', 'exact_new')
    [System.IO.File]::WriteAllText($f, $t, (New-Object System.Text.UTF8Encoding($false)))
    ```
  - **Regex replacement:**
    ```powershell
    $f = Join-Path $PWD.Path 'path\to\file.txt'
    $t = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8) -replace 'pattern', 'replacement'
    [System.IO.File]::WriteAllText($f, $t, (New-Object System.Text.UTF8Encoding($false)))
    ```
- **Detecting Line Endings (CRLF vs LF):**
  Do NOT use Unix utilities like `grep` or `file` — MSYS and WSL shells open files in text mode and strip CR in memory, so they report LF for a CRLF file **with no error**. Count the bytes via .NET instead; in a clean CRLF file the two counts are equal:
  ```powershell
  $b = [System.IO.File]::ReadAllBytes($absPath)
  "CR=$(@($b | Where-Object { $_ -eq 0x0D }).Count) LF=$(@($b | Where-Object { $_ -eq 0x0A }).Count)"
  ```

---

## 7. Dangerous PowerShell 5.1 Quirks
- **The JSON Truncation & BOM Bug:**
  `ConvertTo-Json` in PS5.1 defaults to a serialization depth of **2**. If you convert deeply nested objects (like `package.json` or `appsettings.json`), it silently truncates objects into the literal string `"System.Collections.Hashtable"`, destroying the configuration. Measured: `@{a=@{b=@{c=@{d='leaf'}}}}` serialized to `{"a":{"b":{"c":"System.Collections.Hashtable"}}}`. Furthermore, piping to `Set-Content` or `Out-File` inserts a UTF-8 BOM or writes ANSI.
  - **Correct BOM-less JSON serialization:**
    ```powershell
    $json = $obj | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText($absPath, $json, (New-Object System.Text.UTF8Encoding($false)))
    ```
- **Never assume shell state survives a tool call.** Many agent harnesses run each command in a fresh PowerShell process, keeping only the working directory. Variables, functions, imported modules, preference variables, and `$env:` values are then discarded between calls.
  - **Default: assume nothing persists.** Set everything a command needs *within that same command*, or write it to a scratch file. Never split a `$var = …` from its use across two tool calls.
  - **Where state *does* persist, clean up after yourself.** `$env:VAR = 'val'` then leaks into every later call; unset with `$env:VAR = $null` when finished.
  - **Probe it if it matters** — set a marker in one call, read it in the next:
    ```powershell
    $env:PROBE = 'x'          # call 1
    if ($env:PROBE) { 'persists' } else { 'discarded' }   # call 2
    ```
  > [!NOTE]
  > The default above is deliberately the pessimistic one, and should stay that way. Assuming
  > persistence that is not there makes an agent silently read an empty value and act on wrong
  > data; assuming the reverse costs one redundant assignment. The failure modes are not
  > symmetric.
- **Command Prompt Path Separator Rule:** Paths passed to `cmd.exe` utilities (like `rmdir`) **MUST use Windows backslashes (`\`)**. Measured: `cmd /c "rmdir /s /q C:/path/fs"` printed `Invalid switch - "fs".`, exited 1, and left the directory in place; the backslash form exited 0 and removed it. Note the failure is **silent in effect** unless you check the exit code.
- **Web Requests (curl / wget equivalents):** In PS 5.1 both names are **aliases** for `Invoke-WebRequest`.
  - You **MUST** append `-UseBasicParsing` to avoid the legacy Internet Explorer DOM parser, which is absent from Windows 11 and fails outright in headless sessions.
  - **Do not blindly force TLS.** Measured `[System.Net.ServicePointManager]::SecurityProtocol` = `SystemDefault`, the .NET Framework 4.7+ default, which delegates to the OS and already negotiates TLS 1.2/1.3. Hard-assigning `Tls12` **pins** the process to TLS 1.2 and *disables* TLS 1.3 — a downgrade, not a fix. Check the value first, and only set it if a request actually fails a handshake and the reading is not `SystemDefault`.
  - To get the **real curl** rather than the alias, invoke `curl.exe` explicitly (present at `C:\Windows\system32\curl.exe` on Windows 10+, but shadowed by the alias). Otherwise Unix-style flags produce `Invoke-WebRequest` parameter-binding errors that look nothing like a curl diagnostic.

---

## 8. Non-Interactive Execution — Avoid Hangs
An agent's shell has no usable stdin. A command that waits for input does not error — it **blocks until the tool times out**, which is strictly worse than failing, because it burns the whole call and produces no diagnostic.

- **Never call** `Read-Host`, `Get-Credential`, `Out-GridView`, `$Host.UI.PromptForChoice`, or `pause`.
- **Destructive cmdlets may prompt for confirmation.** Pass `-Confirm:$false` when you intend the action to proceed (`Remove-Item`, `Stop-Process`, `Clear-Content`, …), and `-Force` for read-only or hidden items.
- **Never run interactive git**: `git rebase -i`, `git add -i`, `git commit` with no `-m`, or anything else that opens an editor. Add `--no-pager` to reading commands (`git --no-pager diff`, `git --no-pager log`) so the pager cannot block.
- **Prefer explicit non-interactive flags** on other CLIs too — e.g. `npm ci` over a prompt-capable install, `dotnet` commands with all arguments supplied.

---

## 9. Common Command Substitutions (No Unix Coreutils)
Use native PowerShell cmdlets or robust Windows alternatives:

| Unix / Bash Command | PowerShell / Windows Equivalent | Notes |
|---|---|---|
| `head -n 20 file.txt` | `Get-Content file.txt -Encoding UTF8 -TotalCount 20` | Output line by line |
| `tail -n 20 file.txt` | `Get-Content file.txt -Encoding UTF8 -Tail 20` | Output line by line |
| `cat file.txt` | `Get-Content file.txt -Encoding UTF8 -Raw` | Read entire file as single string |
| `grep "pattern" file.txt` | `Select-String -Pattern 'pattern' -Path file.txt -Encoding UTF8` | Match lines in file |
| `grep -r "pattern" dir/` | `Get-ChildItem -Recurse -File dir \| Select-String -Pattern 'pattern' -Encoding UTF8` | Recursive file search |
| `find . -name "*.cs"` | `Get-ChildItem -Recurse -Filter '*.cs'` | File search |
| `rm -rf dir/` | `Remove-Item -Path 'dir' -Recurse -Force` | For deep/locked trees run `rmdir /s /q` via `cmd.exe` — **backslashes only**, see §7 |
| `mkdir -p dir/subdir` | `New-Item -ItemType Directory -Force -Path 'dir/subdir' \| Out-Null` | `-Force` is safe here — it is a no-op on an existing directory |
| `touch file.txt` | `if (-not (Test-Path 'file.txt')) { New-Item -ItemType File 'file.txt' }` | **Never `New-Item -ItemType File -Force`** — see warning below |
| `which tool` | `(Get-Command tool -ErrorAction SilentlyContinue).Source` | Without `-ErrorAction` a missing tool emits a **non-terminating** error: `.Source` is `$null`, `catch` does **not** fire (unless `$ErrorActionPreference = 'Stop'`), and the console fills with noise. Always test for `$null` |
| `wc -l file.txt` | `(Get-Content file.txt -Encoding UTF8 \| Measure-Object -Line).Lines` | Count lines |
| `curl -s URL` | `Invoke-WebRequest URL -UseBasicParsing` | `curl`/`wget` are aliases for this; use `curl.exe` for real curl (§7) |
| `export VAR=val` | `$env:VAR = 'val'` | Scope is one command unless the harness reuses its shell — see §7 |
| `unset VAR` | `$env:VAR = $null` | Only needed where state persists — see §7 |
| `sed -i 's/a/b/' f` | `.NET` read-replace-write | See §6; there is no safe in-place one-liner |
| `git diff` | `git --no-pager diff` | Prevents interactive pager hang |

> [!WARNING]
> **`New-Item -ItemType File -Force` truncates an existing file to 0 bytes.** Measured: a file
> containing `important` had length 0 afterwards, with no error and no confirmation prompt.
> `-Force` is safe for *directories* (the `mkdir -p` row above) and destructive for *files* — do
> not carry the habit across. Guard a `touch` equivalent with `Test-Path`.
