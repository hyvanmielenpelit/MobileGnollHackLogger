---
name: server_tool_data_sources
description: >-
  The corpora behind Overseer's information-retrieval tools, and how to prove on disk what a
  tool could and could not have seen. Answers "did the AI have access to the wiki", "why did
  this tool return nothing", "where does Overseer read the source code from", "is the source
  index stale" and "how do I regenerate the NetHack wiki". Covers resolving every tool corpus
  path from User Secrets and Overseer/appsettings.json — the GnollHack wiki and source, the
  NetHack wiki and source, the knowledge base, the dumplog store and Overseer/ToolGuides —
  what each index includes and what it silently excludes (target directories, file
  extensions, filename exclusions, per-file size limits), the PowerShell that verifies a
  repository was reachable and which files a size limit dropped, refresh modes and what needs
  an Overseer restart, corpus staleness and the five fingerprints a benchmark run records
  plus the two corpora it does not, the secrets-hygiene rule that only keys are ever
  reported, and regenerating the NetHack wiki with its mandatory backup.
---

# Tool Data Sources: Overseer's Corpora, Their Paths and Their Silent Exclusions

---

## 1. Purpose

A tool answered "not found". That is one sentence covering three completely different worlds:
the corpus was never reachable, the corpus was reachable but the file was outside what the
indexer accepts, or the corpus was fully indexed and the fact genuinely is not in it. A run
report cannot tell them apart, and neither can the model — after indexing completes, all three
look like an ordinary `Success = true` "not found".

**So the question "did the agent have access to the repository?" is answered on disk, not in the
report.** This skill is the corpus layer: where each path comes from, what each indexer takes in
and quietly leaves out, and how to prove which of the three worlds you are in.

The diagnostic *method* that consumes this — the verdicts, the ladder, the replay tiers — belongs
to [`server_benchmark_tool_diagnostics`](../server_benchmark_tool_diagnostics/SKILL.md). § 5 and
§ 6 here are what its ladder delegates to.

---

## 2. Resolving a Path

Overseer reads every corpus path from `IConfiguration`, so a path can come from either of two
places, and **User Secrets override `appsettings.json` in Development**. Resolve in this order.

**Step 1 — find the secrets file.** `UserSecretsId` is declared in `Overseer/Overseer.csproj` and
is not itself a secret, so the file is always locatable:

```powershell
$id = ([xml](Get-Content 'Overseer\Overseer.csproj')).Project.PropertyGroup.UserSecretsId |
    Where-Object { $_ }
$secretsFile = Join-Path $env:APPDATA "Microsoft\UserSecrets\$id\secrets.json"
Test-Path $secretsFile
```

**Step 2 — read only the keys you need.** Never open the whole file into a transcript (§ 3):

```powershell
$secrets = Get-Content $secretsFile -Raw | ConvertFrom-Json
$wikiPath = $secrets.WikiPath          # value stays in the variable, never printed
$srcPath  = $secrets.SourceCodePath
```

`dotnet user-secrets list --project Overseer` is the equivalent, but it prints every key **with
its value**, so prefer the property read above, or pipe its output through a `Select-String` for
the one key name you want.

**Step 3 — fall back to `appsettings.json`.** `Overseer/appsettings.json` is committed, so its
values are quotable and can be read directly:

```powershell
$app = Get-Content 'Overseer\appsettings.json' -Raw | ConvertFrom-Json
$app.NetHackWikiPath        # "c:\hmp\nethackwiki"
$app.NetHackSourceCodePath  # "C:\repos\NetHack\NetHack"
```

**Step 4 — remember which one won.** A key present in both is served from User Secrets in
Development. This matters concretely: `NetHackSourceCodePath` has a committed value, so the value
in `appsettings.json` is *not necessarily* the path the running service used.

> ⚠️ **A missing GnollHack corpus raises no admin alert.** `ConfigHealthService.GetSystemAlerts`
> checks `SentryDSN`, `NetHackWikiPath` and `NetHackSourceCodePath` only. `WikiPath`,
> `SourceCodePath`, `KbPath` and `DumpLogPath` are unchecked — an unconfigured one degrades in
> silence, and `WikiService` makes it worse by substituting a hardcoded fallback literal instead
> of no-opping, so an unconfigured GnollHack wiki indexes *something* rather than nothing. See
> [`configuration_management`](../configuration_management/SKILL.md), whose rule 3 this violates.

---

## 3. Secrets Hygiene

> 🛑 **Report keys, never values.** In any document, plan, report, finding, commit message or
> code comment, name the configuration **key** and the resolution procedure — never the resolved
> path.
>
> The five keys whose values are secret: **`WikiPath`**, **`SourceCodePath`**, **`KbPath`**,
> **`DumpLogPath`**, **`MaxWikiFileSizeKB`**. All five live in User Secrets.
>
> The reason is concrete and not hypothetical: the shared `plans` repository is **pushed
> automatically without review**, so a diagnostics document written from this skill carries
> whatever it contains into a shared store. A machine-local path written once is published
> forever.

Rules that follow from it:

- **Only the path keys and the size-limit keys are ever needed** for this work. No connection
  string, token, key or admin list is relevant to a corpus question — do not read them.
- **Never dump the whole secrets file into a transcript.** Filter to the key you need (§ 2 step 2)
  and print only derived facts: a boolean, a count, a HEAD SHA, a file name.
- **Every snippet takes the path as a variable**, read from configuration or filled in by the
  reader. A hardcoded literal for a secret key in a snippet is the same leak as prose.
- Printing a **file name** (`soundset.c`) is fine; printing the **directory that contains it** is
  not.

The only absolute paths that may appear in a document written from this skill are: values already
committed in `Overseer/appsettings.json`, the conversion script's repository-relative path, and
the backup root `C:\Backup\NetHack Wiki`.

---

## 4. The Corpora

Verified against `WikiService`, `NetHackWikiService`, `SourceCodeService`,
`NetHackSourceCodeService`, `KnowledgeBaseService`, `SearchServerDumplogsTool`,
`ToolRegistry` and `Overseer/appsettings.json`.

| Corpus | Config key | Key lives in | Owning service | Indexed file types | Silent exclusions | Size limit key · code default | Refresh | Git-backed | Run fingerprint |
|---|---|---|---|---|---|---|---|---|---|
| GnollHack wiki | `WikiPath` | User Secrets | `WikiService` | `.md`, `.txt`, `.html`, all subdirectories | Any file over the size limit, dropped with no log line. An unconfigured key falls back to a hardcoded literal rather than no-opping | `MaxWikiFileSizeKB` · **100** (this key is set in User Secrets, so the effective value is not the default) | ~10 min Git HEAD poll | yes | `WikiHeadSha` |
| GnollHack source | `SourceCodePath` | User Secrets | `SourceCodeService` | Under `src`, `include`, `dat`, `win\win32\xpl` **only**: `.c`, `.h`, `.des`, `.txt`; plus `.cs` and `.xaml`, indexed but flagged `IsNetCode` and hidden unless a tool opts in | Everything outside those four directories. `vis_tab.c`, `vis_tab.h`, `date.h`; any file ending `conf.h`; any `win*.h` except `wintype.h`; any `mac*.h`; any `qt*.h`. Any file over the size limit | `MaxSourceFileSizeKB` · **800** (set in neither `appsettings.json` nor User Secrets, so the default applies) | ~10 min Git HEAD poll, plus makedefs header regeneration | yes | `SourceCodeHeadSha` |
| NetHack wiki | `NetHackWikiPath` | `appsettings.json` — `c:\hmp\nethackwiki` | `NetHackWikiService` | `.md` only, all subdirectories; YAML frontmatter `title` / `namespace` / `summary` parsed into searchable fields | Every non-`.md` file, including the generated `_index.json`. Any file over the size limit. A per-file read or parse error is logged and the article is skipped | `MaxNetHackWikiFileSizeKB` · **500**; `appsettings.json` also sets 500 | **startup only — no timer** | **no — generated, not versioned** | **none** |
| NetHack source | `NetHackSourceCodePath` | `appsettings.json` — `C:\repos\NetHack\NetHack` | `NetHackSourceCodeService` (derives from `SourceCodeService`) | Under `src`, `include`, `dat` only — **`win\win32\xpl` is not a target directory here** — otherwise the same extension rules | The same filename exclusions as above. Also: no makedefs header regeneration, no structured game-data parse (`monst.c` / `objects.c` macros), no flag descriptions — so the structured stats tools have nothing to read for NetHack and only the raw search and view tools work | `MaxSourceFileSizeKB` · **800** (shared with the GnollHack source; one key governs both) | ~10 min Git HEAD poll | yes | **none** |
| Knowledge base | `KbPath` | User Secrets | `KnowledgeBaseService` | `.md` under **`<KbPath>\Content`**, all subdirectories; topic id is the path relative to `Content` without the extension | Everything outside `Content` — a `Content` directory that does not exist logs a warning and loads **zero** articles. **No size limit at all** | none | ~10 min Git HEAD poll on `KbPath` | yes | `KnowledgeBaseHeadSha` |
| Dumplog store | `DumpLogPath` | User Secrets | none — `SearchServerDumplogsTool` reads on demand, per call | `<DumpLogPath>\<game.Name>\gnollhack.<game.Name>.<StartTimeUTC>.txt`, located from `GameLog` rows | Any file not matching that exact name. Games older than the newest `BatchSize × MaxBatches` rows — **100 × 5 = 500** by `Tools:search_server_dumplogs` in `appsettings.json`. An unconfigured key returns `Success = false`, *not* "not found" | none | none — read live from disk on every call, so never stale and never warm | no | **none** |
| Tool guides | none — `<AppBase>\ToolGuides` | n/a | `ToolRegistry.LoadGuides` | `_policy.md`, `spoiler_policy.md`, `_policy_parallel_disabled.md`, `_policy_parallel_on_request.md`, `<tool_name>.md` | A guide file is only picked up if the directory exists at the application base; the `Content` item in `Overseer.csproj` copies `ToolGuides\**` on build | none | loaded once, in the `ToolRegistry` constructor | in this repository | `ToolGuidesSha256` — hashed over the **copied output** directory, not the source tree |

Two consequences worth stating outright:

- **`get_monster_stats`, `get_item_stats` and `get_artifact_stats` are GnollHack-only.** They read
  `src/monst.c`, `src/objects.c` and `include/artilist.h` out of the *GnollHack* index and return an
  explicit error naming the missing file when it is absent from it. The structured parser is not
  wired up for NetHack at all.
- **A size limit is a silent, total exclusion.** An over-limit file is not truncated, not summarised
  and not logged — it is absent from the index, so every tool over it reports a clean "not found".

---

## 5. Reachability Checks

Run these against the corpus on the machine that produced the observation. Every snippet takes
`$path` as a variable the reader fills from § 2 — **never paste a literal for a secret key.**

**Does the directory exist, and is it a Git working tree?**

```powershell
$path = $secrets.SourceCodePath          # or $app.NetHackSourceCodePath, etc.
"exists : " + (Test-Path $path)
"git dir: " + (Test-Path (Join-Path $path '.git') -PathType Container)
```

> 🛑 **`GitHelper.GetGitHeadSha` requires `.git` to be a *directory*.** It returns null for a blank
> path, a missing directory, a missing `.git` directory, or a missing or empty `HEAD`, and it
> swallows every parse error. So a **Git worktree**, where `.git` is a *file*, yields null — the
> corpus is perfectly present and correctly checked out, and the fingerprint says nothing. Test for
> `-PathType Container`, exactly as the helper does, or you will diagnose a phantom.

**What is HEAD, and how old is it?** Read the same way the service does, without invoking git:

```powershell
$head = (Get-Content (Join-Path $path '.git\HEAD') -Raw).Trim()
if ($head -like 'ref:*') {
    $ref = $head.Substring(4).Trim()
    $refFile = Join-Path $path (".git\" + $ref.Replace('/', '\'))
    if (Test-Path $refFile) {
        (Get-Content $refFile -Raw).Trim()
        (Get-Item $refFile).LastWriteTimeUtc     # when this ref last moved
    } else {
        "loose ref absent - check .git\packed-refs for $ref"
    }
} else { $head.Substring(0, 40) }
```

A loose ref's `LastWriteTimeUtc` is the practical proxy for "when was this corpus last updated",
and it is what you compare against a run's `StartedAtUtc` when no fingerprint exists (§ 6).

**How many files match the indexed extensions?**

```powershell
# Wiki-shaped corpus
(Get-ChildItem $path -Recurse -File -Include *.md,*.txt,*.html).Count

# Source-shaped corpus - only the target directories count
$dirs = 'src','include','dat','win\win32\xpl'      # drop the last one for NetHack
$files = foreach ($d in $dirs) {
    $p = Join-Path $path $d
    if (Test-Path $p) {
        Get-ChildItem $p -Recurse -File |
            Where-Object { $_.Extension -in '.c','.h','.des','.txt' }
    }
}
$files.Count
```

**Which files does the size limit exclude?** This is the check that finds the invisible gaps:

```powershell
$limitKB = 800          # MaxSourceFileSizeKB code default; read config before trusting it
$files | Where-Object { $_.Length -gt $limitKB * 1024 } |
    Select-Object Name, Length | Sort-Object Length -Descending
```

### Worked example — `src/soundset.c`

Measured on this machine, 2026-09-09: `src/soundset.c` is **1,086,562 bytes ≈ 1,061 KB**, against
the **800 KB** `MaxSourceFileSizeKB` default that is in force because the key is set in neither
`appsettings.json` nor User Secrets. It is therefore **not in the source index at all**, and it is
the **only** file of the 781 candidates under the four target directories that the limit excludes.

A model asked about sound sets receives a confident "not found" that is a property of the indexer,
not of the game. That is a **Corpus / Environment Defect** in the triage of
[`server_benchmark_to_chat_transfer`](../server_benchmark_to_chat_transfer/SKILL.md) § 2, and
filing it as a model knowledge gap would be wrong.

Measured in the same pass: **zero** files exceed either wiki limit — the largest GnollHack wiki
file is 58.6 KB against a 100 KB default, and the largest of 9,323 NetHack wiki articles is
402.4 KB against 500 KB. **The wiki size limits are not presently a gap.** Re-measure rather than
citing this; a single regeneration can change it.

---

## 6. Provenance, and Its Two Holes

From **harness 16**, `BenchmarkService.PopulateInstrumentFingerprint` stamps five fingerprints on
every `BenchmarkRun`:

| Column | What it fingerprints | Read from |
|---|---|---|
| `CandidateSystemPromptSha256` | the candidate system prompt, as built | — |
| `ToolGuidesSha256` | the `ToolGuides` tree at the application base | — |
| `KnowledgeBaseHeadSha` | knowledge base Git HEAD | `KbPath` |
| `WikiHeadSha` | GnollHack wiki Git HEAD | `WikiPath` |
| `SourceCodeHeadSha` | GnollHack source Git HEAD | `SourceCodePath` |

**From harness 17, a run also records what each tool call actually asked and received.**
`BenchmarkRunAnswerToolCall` rows — one per attempted call, on `BenchmarkRunAnswer.ToolCalls` —
carry the arguments (`ArgsText`), the result (`Result`), the error (`Error`), the status, the
emission order (`SortOrder`) and the timings (`QueueWaitMs`, `ExecutionMs`) for every call a run's
turn attempted. This is not a corpus fingerprint — it fingerprints nothing about the wiki or
source tree — but it is what lets an analyst check what a tool was actually **asked**, and what it
actually **returned**, against the corpus on disk, instead of inferring the call from the question
text and the answer's own citations
([`server_benchmark_tool_diagnostics`](../server_benchmark_tool_diagnostics/SKILL.md) § 6). Full
detail, including the derived result cap and the retention window, is in
[`docs/overseer/ai-benchmark.md`](../../../docs/overseer/ai-benchmark.md) § **Harness Version 17
Updates**.

**Two corpora have no fingerprint at all, and both are reachable from a run — the per-call record
above does not change this, since it records a call's own arguments and result, never a corpus
revision:**

- **NetHack source** (`NetHackSourceCodePath`) is a Git working tree but is not fingerprinted.
  Every source tool accepts `repository: "nethack"`.
- **NetHack wiki** (`NetHackWikiPath`) is **not version-controlled at all** — it is generated
  wholesale by `Overseer/Scripts/NetHackWiki/convert_nethackwiki_dump_md.py`, so there is no
  revision to record. `nethack_wiki_search` and `nethack_wiki_view` are both in
  `Benchmark:AllowedTools`.

**For those two corpora, and for any run before harness 16, there is no recorded provenance.** Fall
back to comparing the run's `StartedAtUtc` against the corpus on disk — the ref mtime from § 5 for
the NetHack source, the backup directory names from § 8 for the NetHack wiki — and **state the
residual uncertainty in the finding**. "The corpus probably had not moved" is an honest sentence; a
finding that reads as though the revision were known is not.

> 🛑 **A null in any fingerprint column means "not recorded".** Never "no corpus", never
> "unchanged". A null arises from an unset key, an unreachable directory, or a `.git` that is a
> file rather than a directory — none of which says the corpus was missing.
>
> **The same reading extends to the per-call record.** An answer with **no** `BenchmarkRunAnswerToolCall`
> rows means the run predates harness 17 — never that the model made no tool calls.
> `BenchmarkRunAnswer.ToolCallSummary` remains the only record for those answers, exactly as a null
> fingerprint column means the hash was never captured rather than that the corpus was absent.

### They are provenance, not comparability keys

The two new columns were deliberately **not** registered as comparability keys.
`BenchmarkComparabilityKey` treats an absent value as a real value (`NoValue = "(none)"`), and
`BenchmarkComparabilityKeyKind.Instrument` holds that exactly one differing instrument key is Tier C
while **two or more drops a comparison below Tier B**. Every historical run is null on both new
columns, so registering them would have made every new run differ from every historical run on two
keys at once — silently converting the entire accumulated run history into non-comparable data,
with nothing anywhere erroring. The `HarnessVersion` bump to `16` is the single key that marks the
change.

**Consequence for a reader: a difference in either corpus hash is a fact to investigate, not an
automatic tier drop.** `KnowledgeBaseHeadSha`, `ToolGuidesSha256` and `CandidateSystemPromptSha256`
*are* instrument keys and do move a tier; `WikiHeadSha` and `SourceCodeHeadSha` do not.

The series resume guard is the exception: `BenchmarkRunSeries` records all five as
`FirstMember*` values and compares all five, returning early on an empty recorded value so a
pre-16 series stays resumable. No historical row is backfilled, and none can be.

---

## 7. Refresh Semantics, and What Needs a Restart

| Service | Refresh | What it actually polls |
|---|---|---|
| `WikiService` | ~10-minute timer | `GitHelper.GetGitHeadSha(WikiPath)`; re-indexes only when the SHA differs from the last one seen |
| `KnowledgeBaseService` | ~10-minute timer | `GitHelper.GetGitHeadSha(KbPath)`; reloads articles on a SHA change |
| `SourceCodeService` (and `NetHackSourceCodeService`) | ~10-minute timer, started at the end of `StartAsync` | `GitHelper.GetGitHeadSha(SourceCodePath)` / `(NetHackSourceCodePath)`; on a change, regenerates makedefs headers (GnollHack only) and re-indexes |
| `NetHackWikiService` | **none — startup only** | nothing. The constructor starts one indexing pass and no timer is ever created |

So: **a `git pull` in a wiki, knowledge base or source corpus is picked up within about ten
minutes**, with no restart and no deploy. **A NetHack wiki change is not visible until Overseer
restarts** — this is the deliberate policy recorded in this repository's `.agents/AGENTS.md`
("NetHack Wiki Re-Indexing Policy") and in `Overseer/Scripts/NetHackWiki/README.md`: thousands of
static files, changed only by a manual regeneration, so periodic scanning would cost CPU and disk
I/O for nothing.

Two corollaries for a diagnosis:

- A HEAD SHA that `GitHelper` cannot resolve means **the timer never fires a re-index** — the
  corpus is frozen at whatever the startup pass read, indefinitely, with no warning.
- The 10-minute poll compares SHAs, so an **uncommitted working-tree edit is never picked up** by
  any of the three polling services.

### The cold-start window

During the initial indexing pass a tool does **not** report "not found". It returns
`Success = false` with one of exactly five constants in
`Overseer/Services/Tools/ToolGuardMessages.cs` — `WikiIndexingInProgress`,
`NetHackWikiIndexingInProgress`, `KnowledgeBaseIndexingInProgress`, `SourceCodeIndexingInProgress`,
`NetHackSourceCodeIndexingInProgress` — each of which directs the model not to retry in that turn.
**After indexing completes, a miss returns an ordinary `Success = true` "not found"**, which is the
whole reason this skill exists.

The tool→service→guard map, the cold-vs-not-found state matrix and the `IsIndexingComplete` /
`InitializationTask` contract are **owned by**
[`background_indexing_architecture`](../background_indexing_architecture/SKILL.md). Read them there;
they are not restated here.

---

## 8. Regenerating the NetHack Wiki

**The procedure of record is `Overseer/Scripts/NetHackWiki/README.md`**, next to the converter
`Overseer/Scripts/NetHackWiki/convert_nethackwiki_dump_md.py`. Follow that file — it carries the
prerequisites, the dump source, the invocation, the test-titles mode and the namespace table. Do
not re-derive the command from this skill.

Three rules this repository adds, and none of them is optional:

1. **Articles are never hand-edited.** The converter is the only writer. A manual edit is erased by
   the next regeneration with no trace and no diff, because the tree is not under version control.
2. **The existing tree is backed up first**, to a new subdirectory of `C:\Backup\NetHack Wiki`
   named for the backup's date and time. The converter writes into the target directory and
   nothing else holds a copy.
3. **Overseer is restarted afterwards**, or nothing is re-indexed — `NetHackWikiService` is
   startup-only (§ 7), so every NetHack wiki tool keeps answering out of the previous tree.

> 🛑 **The backup directory's name is the only durable record of when a regeneration happened.**
> No `BenchmarkRun` column records a NetHack wiki revision (§ 6), and the tree carries no
> revision of its own. That is what makes the date-and-time naming convention **load-bearing
> rather than tidy**: it is the sole evidence available to a later finding that needs to know
> which wiki a run read. A backup directory named `old`, or a regeneration run without one,
> destroys the only provenance the corpus will ever have.

---

## 9. Cross-References

- [`server_benchmark_tool_diagnostics`](../server_benchmark_tool_diagnostics/SKILL.md) — the
  diagnostic method that consumes this skill; its ladder delegates to § 5 and § 6 here
- [`server_tool_parameter_reference`](../server_tool_parameter_reference/SKILL.md) — the per-tool
  parameter and result contract
- [`server_benchmark_to_chat_transfer`](../server_benchmark_to_chat_transfer/SKILL.md) — the
  mandatory triage, including the **Corpus / Environment Defect** category a § 5 finding lands in
- [`background_indexing_architecture`](../background_indexing_architecture/SKILL.md) — the
  tool→service→guard map, the cold/warm state matrix and the warm-up lifecycle; **owned there,
  referenced not restated**
- [`tool_execution_architecture`](../tool_execution_architecture/SKILL.md) — batching, concurrency
  throttles, output budgets and truncation markers; likewise owned there
- [`configuration_management`](../configuration_management/SKILL.md) — the
  `appsettings.json`-versus-User-Secrets split, and the `ConfigHealthService` alert pipeline that
  covers only two of the seven corpus keys
- [`docs/overseer/ai-benchmark.md`](../../../docs/overseer/ai-benchmark.md) — "Harness Version 16
  Updates", the authoritative account of the two corpus fingerprints and the two remaining holes;
  "Harness Version 17 Updates" for the per-call tool record, the derived result cap, and the
  retention window
