# GnollHack Source Indexing

The Overseer chat's source tools (`source_code_search`, `source_code_view`, `get_function_definition`,
`search_definitions`, `get_constants`, `get_monster_stats`, `get_item_stats`, `get_artifact_stats`,
`list_indexed_files`) read an in-memory index of the GnollHack C source that `SourceCodeService`
builds in the background. Part of that source is generated: makedefs writes `include/pm.h`,
`include/onames.h`, `include/animoff.h` and `include/animtotals.h`, which hold the monster, object and
animation constants (`NUM_MONSTERS`, `PM_*`, the object indices). So every pass first makes sure those headers
match the tree, then reads the files, and only then swaps the new index in.

The code lives in `Overseer/Services/SourceCodeService.cs` and `Overseer/Services/SourceIndexing/`.
`NetHackSourceCodeService` derives from the same class and runs the same pipeline without makedefs.

## 1. What is indexed

| | GnollHack (`SourceCodePath`) | NetHack (`NetHackSourceCodePath`) |
|---|---|---|
| Directories | `src`, `include`, `dat`, `win\win32\xpl`, all subdirectories | `src`, `include`, `dat` |
| File types | `.c`, `.h`, `.des`, `.txt`; `.cs` and `.xaml` are indexed as .NET code and shown only when a tool asks for it | the same |
| Left out | `vis_tab.c`, `vis_tab.h`, `date.h`; any `*conf.h`; `win*.h` except `wintype.h` and `winprocs.h`; `mac*.h`; `qt*.h`; any path with a segment that begins with `.` or equals `bin` or `obj`; any file over `MaxSourceFileSizeKB` (default 800) | the same |
| Generated headers | built by makedefs before each read when its inputs changed (§ 3) | none; the headers in the tree are read as they are |
| Game data | `monst.c`, `objects.c`, `permonst.h` and `objclass.h` are parsed for the stats tools | not parsed; only the search and view tools work |

The files left out for their size are not logged one by one, but the published index records them and
the dashboard lists them (§ 6).

## 2. The pipeline

`StartAsync` loads the flag descriptions, starts the first pass on the thread pool and the 10-minute
HEAD poll, and returns at once, so Kestrel starts listening without waiting for makedefs or the index.
Every pass — at start-up, after a change the poll sees, or from a dashboard button — runs these steps:

1. **One pass at a time.** The pass takes the service's single gate. An automatic trigger that finds it
   taken queues one follow-up pass (never more than one); a manual request is refused with *A source
   job is already running.*
2. **No source path.** When the path is not configured or does not exist, the pass ends as `Failed`
   with that reason, and the service keeps its empty index.
3. **Wait for a quiet repository.** Until `.git/index.lock` is absent and HEAD reads the same twice
   3 s apart, at most 2 minutes; after that the pass reads the tree anyway and logs a warning. The HEAD
   read here is the one the index records.
4. **Nothing new.** A pass started by the poll or as a follow-up ends as `SkippedNoChange` (*No change*) when that
   HEAD is the one the published index was read at. A manual **Re-index Source Files** always reads the
   tree, so it also picks up uncommitted edits.
5. **makedefs** (GnollHack only): the check of § 3. A makedefs failure does not stop the pass: the
   headers on disk are indexed as they are, and the job records the failure.
6. **Read the files** into new collections, counting files read against the total, and parse their
   constants.
7. **Parse the game data** into a new `GameDataParser` and `ObjectsMacroResolver`.
8. **Publish.** The documents, constants, parser, resolver, content fingerprint, HEAD, branch and a
   generation number become one immutable `SourceIndexSnapshot`, swapped in with one reference
   assignment. Every query reads that reference once and uses the snapshot to the end, so the chat keeps
   answering from the previous index for the whole pass and never sees a partial one. A pass that fails
   or is canceled publishes nothing.
9. **Follow-up.** When HEAD has moved since step 3, one follow-up pass is queued; the index just
   published is still kept, since it is newer than the previous one.

`IsIndexingComplete` turns true when the **first** pass ends, published or not, and never returns to
false. `InitializationTask` is that first pass and never faults. `IndexGeneration` counts publishes;
`SourceLivenessIndex` re-reads its corpus when it changes. `ContentFingerprint`, which a GnollBench run
records as `gnollhackSource`, is the published snapshot's.

The poll compares HEAD only, so it never notices an uncommitted edit; the dashboard's **Re-index
Source Files** does. A pull on the server is picked up within ten minutes, with no restart.

## 3. makedefs: when it runs

makedefs runs only when its **inputs** changed. `MakedefsInputSet` hashes, with SHA-256, each file's
repository-relative path and bytes, in a fixed order, then the build command:

- the four sources `makedefs.vcxproj` compiles: `src/animdef.c`, `util/makedefs.c`, `src/monst.c`,
  `src/objects.c`;
- every `include/*.h` except the four generated headers and `date.h`;
- every `sys/winnt/*.h` (the project's system include directory);
- `util/mdgrep.h`, `win/win32/vs/makedefs.vcxproj` and `win/win32/vs/dirs.props`;
- the `MakedefsBuildCommand` string.

Hashing about 120 small files takes milliseconds, and it sees uncommitted edits too.

**The stamp.** After a successful generation `MakedefsHeaderGenerator` writes
`overseer-makedefs-stamp.json` **beside the makedefs executable** (by default
`<SourceCodePath>\tools\Release\x64\`, which GnollHack's `.gitignore` already ignores through
`[Rr]elease/`). It records the input hash, the SHA-256 of each generated header, the time and the HEAD.
It is written to a `.tmp` file and moved over the old one, so a crash never leaves half a stamp.

**The check**, in order; each ends in a `MakedefsOutcomeKind`, shown on the dashboard in words:

1. `MakedefsBranch` is set and another branch is checked out → `SkippedBranch`; the headers on disk
   are used as they are.
2. The stamp's input hash equals the current one **and** all four headers exist with the hashes the
   stamp recorded → `UpToDate`; no process is started. A **Build makedefs** request skips this test.
3. Otherwise, when `MakedefsBuildCommand` is set, it runs in `SourceCodePath` with
   `MakedefsBuildTimeoutSeconds`. A failure → `BuildFailed`, and the executable is **not** run, since
   it may be stale.
4. The executable (`MakedefsExecutablePath`, else `tools\Release\x64\makedefs.exe` under the source
   root) missing → `ExecutableMissing`.
5. `makedefs -o` and `-p` run in `util\`, `makedefs -a` in `dat\`, each with `MakedefsRunTimeoutSeconds`.
   All succeed and all four headers exist → the stamp is written → `Generated`, noting whether any
   header's content changed. A run that fails or leaves a header unwritten → `RunFailed`, with the
   stamp untouched, so the next pass retries.

With `MakedefsBuildCommand` empty, an existing executable is still run, and the outcome says it may
be older than its sources.

So a server whose tree has not changed since the last generation starts with no build at all. With no
stamp — a fresh clone, a *Clean* that removed the executable and the stamp together, or the first start
of a server that has never written one — the first pass builds makedefs once, and the source tools
answer with the guard message (§ 5) until it ends. A header rewritten by something else (a GnollHack
build from another branch) no longer matches the stamp and is regenerated.

**The process runner** (`ExternalProcessRunner`) runs each command through `cmd.exe /c` on Windows, captures the
last 200 lines of standard output and standard error (error lines prefixed `[err] `), sets
`MSBUILDDISABLENODEREUSE=1` so no MSBuild worker node outlives the build, and on a timeout or a
cancellation kills the whole process tree.

## 4. Configuration

| Key | Default | Meaning |
|---|---|---|
| `SourceCodePath` | none (User Secrets) | The GnollHack clone. Unset → no index and the `source-code-path-missing` alert |
| `MakedefsBuildCommand` | the MSBuild command in `appsettings.json` | Builds `win\win32\vs\makedefs.vcxproj` (Release, x64). Use the full path of `MSBuild.exe` of the Visual Studio or Build Tools installation on the machine |
| `MakedefsExecutablePath` | empty → `<SourceCodePath>\tools\Release\x64\makedefs.exe` | The executable the build produces; the stamp lives beside it |
| `MakedefsBranch` | empty → any branch | Runs makedefs only while this branch is checked out |
| `MakedefsBuildTimeoutSeconds` | 300 | How long the build may run before its process tree is killed |
| `MakedefsRunTimeoutSeconds` | 60 | The same for each of the three makedefs runs |
| `MaxSourceFileSizeKB` | 800 | Files over this size are not indexed; shared with the NetHack source |
| `NetHackSourceCodePath` | none | The NetHack clone; indexed without makedefs |

A missing or non-positive timeout uses its default. All of these are non-sensitive and belong in
`appsettings.json`, except a machine-specific `SourceCodePath`, which lives in User Secrets.

## 5. While the first pass runs

Until the first pass ends, every GnollHack source tool returns `Success = false` with
`ToolGuardMessages.SourceCodeIndexingInProgress`:

> Source Code service initialization in progress: GnollHack source file indexing has not finished yet
> (the server is generating headers with makedefs and indexing the C source in the background). Do not
> retry this tool in this turn. Tell the user that source file indexing has not finished yet and to ask
> again in a moment, as the source code data is still warming up; you may answer from general knowledge
> meanwhile, saying that it is not checked against the source.

The NetHack tools return `NetHackSourceCodeIndexingInProgress`, the same text for the NetHack source
without the makedefs clause. After the first pass a miss is an ordinary *not found*, and a later pass
never brings the message back. A GnollBench run that meets the message met a cold server; that is an
environment fact, not a finding about the model (`server_benchmark_tool_diagnostics`).

## 6. The GnollHack Source dashboard

Admin → **Developer Tools** → **GnollHack Source** (`/admin/devtools`) polls
`GET api/admin/devtools/source/status` every second while a job runs and every 15 seconds otherwise,
and pauses while the browser tab is hidden. It shows:

- **A status banner**: *Ready*, *Indexing*, *Building makedefs*, *Waiting for repository*, *Not
  indexed*, *Index out of date* (HEAD has moved; the next automatic check is at …), *Last pass failed*
  or *Not configured*, with one sentence.
- **Re-index Source Files** — a pass now; makedefs is built only when its inputs changed.
- **Build makedefs** — always builds and runs makedefs, then re-indexes when a header changed or no
  index exists yet; otherwise the job ends with *The index was kept.*
  Both are unavailable, with the reason beside them, while a job runs or the source path is not
  configured.
- **A progress card** while a job runs and for 10 s after it ends: the seven stages (*Waiting for
  repository · Checking makedefs inputs · Building makedefs · Generating headers · Reading files ·
  Parsing game data · Publishing index*), a progress bar with *N of M files* while reading, the elapsed
  time, the trigger, **Cancel**, and for a build the *Build output* with **Copy build output**. Cancel
  kills a running build or makedefs run; the previous index keeps serving.
- **Key figures**: indexed files, lines, constants, last indexed, index time, the indexed commit and
  branch (and the current HEAD when it differs), the next automatic check.
- **Generated headers**: each of the four, present or missing, modified, in the index or not.
- **makedefs**: the build command, whether its program exists, the executable and its time, the branch
  restriction, the inputs (*Up to date*, *Changed since last build*, *Unknown*), the last build's time,
  duration and outcome, and its *Last build output*.
- **Excluded files**: the files over `MaxSourceFileSizeKB`, with their sizes.
- **NetHack source**: a read-only card — configured, files, last indexed, commit. It has no buttons; its
  own 10-minute poll refreshes it.
- **Recent activity**: the last 20 jobs with trigger, kind, duration, outcome and message.

| Method | Route | Returns |
|---|---|---|
| `GET` | `api/admin/devtools/source/status` | `{ gnollHack, netHack, serverTimeUtc }`, each a `SourceIndexStatusDto` |
| `POST` | `api/admin/devtools/source/reindex` | 202 `{ jobId }`; 409 while a job runs; 400 when `SourceCodePath` is not configured or missing |
| `POST` | `api/admin/devtools/source/makedefs` | the same |
| `POST` | `api/admin/devtools/source/cancel` | `{ canceled }` |

All are `AdminOnly`. The build command and the paths are shown; they are not secrets.

**Alerts.** `source-code-path-missing` (warning) while `SourceCodePath` is not set, and
`gnollhack-makedefs-failed` (error, **Open GnollHack Source** → `/admin/devtools`) when the last
makedefs outcome is `BuildFailed` or `RunFailed` **and** a generated header is missing. A failure over
headers still on disk raises no alert, since the index still has the constants; the dashboard and
Configuration Health (*GnollHack source build*) still show it.

## 7. Troubleshooting

Start from the dashboard's banner, the makedefs card and the build output; the server log has one
Information line per phase change (`{Service}: job {Id} phase {Phase}.`) and a Warning for every failed
makedefs outcome.

- **Build failed at once, with `cmd.exe` saying the program is not recognized or the path is not
  found**: the program in `MakedefsBuildCommand` does not exist. The makedefs card's *Build tool* says
  *Not found*; Configuration Health names the path. Point the command at the `MSBuild.exe` that is
  installed.
- **Build failed with MSB8020 or MSB8036 in the output**: the C++ build tools or the Windows SDK the
  project asks for are not installed. Install the *Desktop development with C++* workload (Visual Studio
  or Build Tools) with that SDK, then **Build makedefs**.
- **Build failed, "timed out after 300 s"**: a cold build on a slow machine. The process tree was killed;
  raise `MakedefsBuildTimeoutSeconds` and press **Build makedefs**.
- **Build failed with LNK1104 on `makedefs.exe`**: something holds the executable — a debugger, a
  GnollHack build in Visual Studio, an earlier process. Close it and build again. The runner itself
  leaves no MSBuild node behind.
- **Run failed, "did not write pm.h"**: makedefs ran from the wrong tree or failed silently; read the
  output tail. The stamp was not written, so the next pass retries.
- **Executable missing**: with an empty `MakedefsBuildCommand` nothing builds makedefs, so set the
  command or build `makedefs.vcxproj` once by hand; after a successful build, `MakedefsExecutablePath`
  names another file than the one the build writes, so clear it or correct it.
- **Waiting for repository** for up to 2 minutes: a `git pull`, checkout or rebase is running, or a
  stale `.git/index.lock` was left behind by a crashed Git process. The pass proceeds after the limit;
  remove a stale lock only when no Git process is running.
- **Index out of date**: HEAD has moved and the next automatic check, at most ten minutes away, has
  not run yet. Press **Re-index Source Files** to index at once.
- **Headers present but *Not indexed***: a header was written after the last pass (a GnollHack build,
  say); re-index.
