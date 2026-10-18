# Corpus Dashboards

The Overseer chat's wiki and knowledge-base tools read three in-memory corpora that Overseer builds
in the background: the **GnollHack wiki** (`WikiService`), the **NetHack wiki** (`NetHackWikiService`)
and the **knowledge base** (`KnowledgeBaseService`). The Admin page's **Services** tab has one
dashboard for each. A dashboard shows whether the corpus is indexed, when and from which commit, what
was left out, how the chat's tools have used it, and lets an administrator start a new pass and run
the chat's own search tool against the live index.

The server side is `Overseer/Services/CorpusIndexing/` (the job runner, the status types, the NetHack
conversion report reader and the tool usage query), the three services, and
`Overseer/Controllers/AdminDevToolsController.cs`. The client side is
`Overseer/ClientApp/src/app/admin/devtools-tab/`: one folder per dashboard and the shared parts in
`devtools-tab/shared/`. The GnollHack source has its own dashboard on the same tab, described in
[GnollHack Source Indexing](gnollhack-source-indexing.md) § 6.

## 1. The Services tab

Admin → **Services** (`/admin/services`) has six sub-tabs, in this order:

| Sub-tab | Path | Shows |
|---|---|---|
| GnollHack Source | `/admin/services` | The GnollHack source index and makedefs ([GnollHack Source Indexing](gnollhack-source-indexing.md)) |
| GnollHack Wiki | `/admin/services/gnollhack-wiki` | The GnollHack wiki index (§ 3) |
| NetHack Wiki | `/admin/services/nethack-wiki` | The NetHack wiki index and its last regeneration (§ 4) |
| Knowledge Base | `/admin/services/knowledge-base` | The knowledge base articles (§ 5) |
| Configuration Health | `/admin/services/config-health` | The configuration report (`configuration_management` § 4) |
| Developer Tools | `/admin/services/developer-tools` | The *Sentry Crash Reporting* fieldset: **Test Frontend Error** and **Test Backend Error** |

The visible names and the address slugs are `Services` and `developer-tools`; the code ids stay
`devtools` and `sentry`, as do `devtools-tab/`, the DOM ids and the API route `api/admin/devtools`.
The tab's slug was `devtools` until 2026-10-18, and every old `/admin/devtools…` address still opens
the same view and is replaced by its new path ([Admin Page URLs](admin-page-urls.md) §§ 1 and 3).

All endpoints are `AdminOnly`.

## 2. What every corpus dashboard shows

The three dashboards share one layout, built from the components in `devtools-tab/shared/` and the
global `.gh-dash-*` and `.gh-health-*` classes in `styles.scss`.

**Polling.** A dashboard polls its status endpoint every second while a pass runs and every 15
seconds otherwise, backs off 5, 10 and then 30 seconds after failed polls, and pauses while the browser
tab is hidden (`DashboardPoller`, shared with the GnollHack Source dashboard). A failed poll shows a
load error with **Retry**.

**The status banner** has a status word and one sentence. The first matching condition wins:

| Condition | Word | Sentence (GnollHack wiki example) |
|---|---|---|
| The key is not set and there is no folder | *Not configured* (warning) | WikiPath is not set, so the wiki tools have nothing to search. |
| The key is not set and a built-in default is read | *Using default path* (warning) | WikiPath is not set; the service reads the built-in default c:\wiki. |
| The folder does not exist | *Folder missing* (error) | The WikiPath folder does not exist on the server. |
| A pass is running | *Indexing* (running) | The stage and, while reading, *N of M* files |
| The last pass failed | *Last pass failed* (error) | The job's message |
| No index has been published | *Not indexed* (warning) | The tools answer that indexing is in progress. |
| A Git-polled folder is not a Git working tree | *Never refreshes* (warning) | Automatic refresh never fires; use Re-index after a change. |
| HEAD has moved since the index was built | *Out of date* (warning) | The time of the next automatic check |
| NetHack only: the tree was regenerated after the index | *Tree regenerated* (warning) | Re-index to load it. |
| Some files could not be read | *Some files unreadable* (warning) | How many, and that they are not searchable |
| Otherwise | *Ready* (ok) | The index matches the current HEAD (NetHack: the generated tree; knowledge base: all N articles are loaded). |

**Actions.** One **Re-index** button (*Re-index wiki* on the two wikis, *Reload articles* on the
knowledge base), with an info tip that says what the pass reads. It is unavailable, with the reason
beside it, while a pass runs or the corpus is not configured. A refusal is shown on a feedback line:
a warning for a 409, an error otherwise. There is no Cancel: a pass takes seconds.

**The progress card**, while a pass runs and for 10 seconds after it ends: the three phases
(*Reading files · Building index · Publishing*; a knowledge base pass has no *Building index*), a
progress bar with *N of M files* while the files are read, the elapsed time and the trigger.

**Key figures**, all describing the published index:

| Figure | Comes from |
|---|---|
| Articles, Total size | The files the pass indexed and their bytes on disk |
| Last indexed, Index time | When the pass published, and how long it took |
| Indexed commit | The HEAD and branch read **before** the pass read the files, with the current HEAD when it differs. On the NetHack wiki, *Refresh: Startup and manual only* instead |
| Content fingerprint | The first 12 characters of the SHA-256 content fingerprint, with a copy button. It is the fingerprint a GnollBench run records for the corpus when it starts (harness 48+) |
| Next automatic check | The 10-minute Git check (GnollHack wiki and knowledge base only) |

Each dashboard adds its own tiles (§§ 3–5).

**Excluded files**, three disclosures: *Excluded by size* (each path and size, with the limit and its
configuration key), *In dot-directories* (a count) and *Unreadable* (each path with the exception
type and message). The size-skipped and unreadable lists hold at most 200 entries each and say how
many more there are.

**Recent activity**: the last 20 finished passes, newest first, with when, trigger (*Startup*,
*RepositoryChange*, *Manual*, *FollowUp*), duration, outcome (*Succeeded*, *Failed*, *Canceled*) and
message, such as *2,512 articles, 3 over the size limit, 1 unreadable*. The history is in memory, so it
starts empty when Overseer starts.

**Tool usage** — how the chat called this corpus's tools (§ 9).

**Search probe.** A form that runs the chat's real tool handler against the live index and shows
what it returned: the tool name, the result's length against its character limit, the time it took,
*Found* or *Tool refused*, and the content as plain text, collapsed with **Show all** and a button that
copies the full text. The tool runs with the administrator's user id and the larger of 10,000
characters and the tool's own result limit, and gives up after 15 seconds. Nothing is stored: no chat
session, no tool call row, no usage count.

## 3. GnollHack Wiki

`WikiPath`, read by `WikiService`: `.md`, `.txt` and `.html` files in every subdirectory, except files
under a directory whose name begins with `.` and files over `MaxWikiFileSizeKB` (default 100).

- **Not configured.** When `WikiPath` is **unset**, the service still reads the built-in default
  `c:\wiki`: the status reports `Configured == false` with that default as its path, and the banner
  says *Using default path*. When it is set to a **blank** value there is no folder, and Re-index
  answers *WikiPath is not configured.*
- **Missing folder.** At startup a missing folder records a failed pass, *The WikiPath folder does not
  exist.*, and `IsIndexingComplete` still becomes true, so the tools answer as if nothing matched
  instead of saying that indexing is in progress. The banner says *Folder missing*. The message names the key, never its value.
- **Unreadable files.** A file that cannot be read is logged, left out and listed under
  *Unreadable*; the pass goes on and publishes the rest.
- **Extra tiles**: *Categories* and *Duplicate titles*.
- **Categories**: indexed articles and bytes per top-level directory, `(root)` for files at the root,
  most articles first, with a share bar. A directory name works as a `wiki_search` `category` value;
  the filter matches it as a case-insensitive substring of the article's path.
- **Duplicate titles**: every title that two or more indexed articles share, compared the way
  `wiki_view` compares a request (the normalized title, ignoring case), each with its paths. `wiki_view`
  answers such a title with a disambiguation list. At most 100 groups are listed, with the full count.
- **Tool usage**: `wiki_search`, `wiki_view`, `monster_lookup`, `item_lookup`.
- **Search probe**: `wiki_search`, with an optional *Category*.
- **Re-index** reads the wiki folder as it is now, uncommitted edits included, and swaps in the new
  index when it is complete; the chat keeps answering from the current index meanwhile. Automatic
  refresh only follows new commits (§ 6).

## 4. NetHack Wiki

`NetHackWikiPath`, read by `NetHackWikiService`: the `.md` files the converter generated, over
`MaxNetHackWikiFileSizeKB` (default 500) left out. The tree is not under version control, so there is
no commit and no automatic refresh (§ 8).

- **Extra tile**: *Namespaces*.
- **Namespaces**: indexed articles per front matter `namespace` value, lowercased, `article` when a
  file has none, with share bars. The converter writes main-namespace game articles only, so this
  normally shows one row.
- **Last regeneration**, read from `<NetHackWikiPath>\_conversion_report.json` by
  `NetHackWikiConversionReportReader`: the converter's time (its local time, as written), the dump's
  date and size, the duration, pages written, redirects, errors, tables converted, dropped templates,
  the largest file and its size, and the number of files over 500 KB. Then **Skipped by the
  converter**, a table of reasons and page counts: each property of `pages.skipped` is a reason — a
  number is its count, an array its length, an object the sum of its numbers — and an object named
  `X_by_Y` beside a number `X` is folded in as the breakdown of `X` rather than listed as a reason of
  its own. Without a report the card says *No conversion report in the wiki folder.*; an unreadable or
  malformed one shows its error. A field the report lacks, or holds with an unexpected type, is left
  empty. The parsed report is reused until the file's last write time changes.
- **Tree regenerated**: the banner's warning when the report file was written after the index was
  built — the tree on disk is newer than what the tools read.
- **How to regenerate**: a disclosure with the four rules of `server_tool_data_sources` § 8 — back up
  the tree to a new dated folder under `C:\Backup\NetHack Wiki`, run the converter as
  `Overseer/Scripts/NetHackWiki/README.md` says, remove the old generated files, then click
  **Re-index** here. The dashboard never runs the converter.
- **Tool usage**: `nethack_wiki_search`, `nethack_wiki_view`.
- **Search probe**: `nethack_wiki_search`, with no filter, because the tool takes no namespace
  argument.
- **Re-index** indexes the wiki folder once, now. This corpus never refreshes on its own.

## 5. Knowledge Base

`KbPath`, read by `KnowledgeBaseService`: the articles are `<KbPath>\Content\**\*.md`, each topic its
path relative to `Content` without the extension. There is no size limit.

- **Missing `Content` folder.** The pass fails with *The Content folder under KbPath does not exist.*
  and keeps the previous article set; at startup that is zero articles.
- **Shadowed topics.** Topics are matched ignoring case. When two files differ only by case, the one
  enumerated last wins, as it always has, and the one it replaced is listed with its path and the path
  that shadows it.
- **Extra tiles**: *In the system prompt* — the length of the topic list the chat system prompt
  carries, in characters and an estimated token count (characters ÷ 4); *Missing summary*; *Shadowed
  topics*.
- **Articles**: a data table of every article — topic, title, whether the front matter has a summary,
  size and last modified — with sorting, paging, filters for topic and title and *Present* / *Missing*
  selects for summary and title. Each row has a **Preview** button that opens the article's raw text as
  loaded, front matter included, in a dialog with a copy button.
- **Health checks**: missing titles, missing summaries, shadowed topics and unreadable files, or
  *No problems found.*
- **Tool usage**: `get_knowledge_article`.
- **Search probe**: `get_knowledge_article`, with a *Topic* field that suggests the known topics. A
  miss shows the tool's own list of topics.
- **Reload articles** reads the `Content` folder as it is now, uncommitted edits included. The topic
  list is part of the chat system prompt, so after a reload the next turn of every chat starts without
  the prompt cache once.

## 6. Re-index and the job runner

Each service owns one `CorpusIndexJobRunner`, and every pass of that corpus goes through it: the
startup pass, the 10-minute Git check (GnollHack wiki and knowledge base) and the dashboard's
Re-index.

- **One pass at a time.** The gate is a flag guarded by a lock. A manual request while a pass runs is
  refused (`AlreadyRunning`, HTTP 409 `alreadyRunning`). An automatic request while a pass runs queues
  at most **one** follow-up pass, which runs the most recent request's delegate when the current pass
  ends (trigger *FollowUp*).
- **No pass ever faults.** An exception is recorded as *Failed* with the first line of its message (at
  most 500 characters) and logged once. A pass canceled because Overseer is shutting down is recorded
  as *Canceled*, and the previous index keeps serving. On success the pass's summary becomes the job's
  message.
- **The startup pass is `InitializationTask`.** It completes when that pass has ended and its job is
  recorded, and never faults; `IsIndexingComplete` becomes true then, whether the pass succeeded or
  not, and never turns false again. A later pass never brings back the *indexing in progress* answer:
  the tools read the previous index until the new one is published.
- **One swap.** A pass publishes its statistics — generation, HEAD and branch, counts, the excluded
  files — in the same swap that installs the new searcher or article set and its content fingerprint,
  so a status never pairs one index's figures with another's fingerprint.
- **The HEAD is recorded only when a pass publishes.** The 10-minute check compares the current HEAD
  with the published index's HEAD and requests a pass through the gate when they differ. A failed pass
  publishes nothing, so the next check retries it rather than waiting for the next commit, and a timer
  pass never overlaps a manual one.
- **Re-index reads the working tree**, uncommitted edits included; the timer only follows commits.

`POST …/reindex` answers:

| Answer | When |
|---|---|
| 202 `{ jobId }` | The pass started |
| 409 `{ code: "benchmarkRunActive", message }` | A GnollBench run is executing and the request was not acknowledged (§ 7) |
| 409 `{ code: "alreadyRunning", message }` | A pass is already running |
| 400 `{ message }` | *The {key} folder does not exist.* or *{key} is not configured.* — the key, never its value |

## 7. The GnollBench warning

A GnollBench run records the content fingerprint of every corpus when it starts. A re-index in the
middle of a run makes the run's later questions read a corpus that record does not describe. So while
any GnollBench run has the status *Running*, a Re-index is answered with **409 `benchmarkRunActive`**
— for example *A GnollBench run is executing and reads the GnollHack wiki.* — and the dashboard opens a
confirmation dialog, *A GnollBench run is executing*: the server's message, and that questions after
this point read the new index while the run's recorded fingerprints describe the old one. **Cancel**
is focused first; **Re-index anyway** resends the request with `acknowledgeActiveBenchmark=true`. It is
a warning, not a refusal. The 10-minute Git check does not ask.

The GnollHack Source dashboard's **Re-index Source Files** and **Build makedefs** ask the same way —
*A GnollBench run is executing and reads the GnollHack source.* — because a run records the source
index's fingerprint as `gnollhackSource`, and Build makedefs re-indexes when the headers changed. Their
confirm buttons are **Re-index anyway** and **Build anyway**. The source's 10-minute HEAD check,
repository-change passes and follow-up passes do not ask.

## 8. The NetHack wiki's manual pass

The NetHack wiki is thousands of static files that change only when the converter regenerates them,
so the service has **no timer, no file watcher and no Git check**: it indexes once at startup, and
again only when an administrator clicks **Re-index** on its dashboard (or Overseer restarts). This is
the policy in `.agents/AGENTS.md` (*NetHack Wiki Re-Indexing Policy*); the manual pass replaces the
restart that used to be the only way to load a regenerated tree. The pass keeps the parallel chunked
build of the startup pass, and its status reports `RefreshMode = StartupAndManual` with no next check.

## 9. Chat tool usage

The *Tool usage* card counts the chat's stored calls of the corpus's tools over the last **7, 30 or 90
UTC days** (today included; the choice is remembered in the browser's `localStorage`). It loads the
first time it scrolls into view.

| Corpus | Tools |
|---|---|
| GnollHack wiki | `wiki_search`, `wiki_view`, `monster_lookup`, `item_lookup` |
| NetHack wiki | `nethack_wiki_search`, `nethack_wiki_view` |
| Knowledge base | `get_knowledge_article` |

`CorpusToolUsageService` reads `ChatMessageToolCall` rows, dated by their message's `TimestampUtc`,
and for each call only its name, status, execution time and whether its error is the corpus's
*indexing in progress* guard message (`ToolGuardMessages`). It never reads the arguments, the result
or the session's user. Per tool, and for the corpus's tools together:

- **Calls**, **Completed** (status `completed`), **Errors** (status `error`, guard refusals included)
  and **Other** (any other status, or none).
- **Index-warming refusals**: calls refused because the index was not ready yet.
- **Median** and **95th percentile** execution time, nearest-rank, over the calls that recorded one.
- **Trend**: calls per UTC day, drawn as a sparkline; the table row is its text alternative.

What the numbers include, as the card states:

- **GnollBench calls are not included**; they are stored apart, in `BenchmarkRunAnswerToolCalls`.
- **Ephemeral sessions are never stored**, so they are never counted.
- **Sub-agent calls are included**; they are stored with the turn.
- **A guard refusal in a confidential (encrypted) session counts as an error but not as a refusal**,
  because its error text is stored encrypted.

The result is cached for 5 minutes per period.

## 10. Endpoints

All under `api/admin/devtools/`, `AdminOnly`. A re-index, probe or usage request the browser
abandons ends with 499.

| Method | Route | Returns |
|---|---|---|
| `GET` | `gnollhack-wiki/status` · `nethack-wiki/status` · `knowledge-base/status` | The corpus status: the common `CorpusIndexStatusDto` plus `WikiStatusDto` (categories, duplicate titles), `NetHackWikiStatusDto` (namespaces, conversion report, `treeChangedSinceIndex`) or `KnowledgeBaseStatusDto` (articles, shadowed topics, `topicListChars`, `contentPathExists`) |
| `POST` | `gnollhack-wiki/reindex` · `nethack-wiki/reindex` · `knowledge-base/reindex`, query `acknowledgeActiveBenchmark` | § 6 |
| `POST` | `gnollhack-wiki/probe` · `nethack-wiki/probe` · `knowledge-base/probe`, body `{ query, filter? }` | `{ toolName, success, content, error, lengthChars, maxResultLength, elapsedMs }`. 400 for an empty query or one over 500 characters. `filter` is the `wiki_search` category; the other two tools take none, and it is ignored |
| `GET` | `knowledge-base/article?topic=` | `{ topic, title, source }`, the raw text as loaded; 400 without a topic, 404 for an unknown one |
| `GET` | `corpus-tool-usage?days=` | The usage of every corpus tool and corpus (§ 9); `days` other than 7, 30 or 90 becomes 30 |

The status shows the configured folder paths, which are not secrets; job and refusal messages name
the configuration key only.

## 11. What is deliberately not shown

- **What users searched for.** The usage card counts calls and times them; it never shows a query, an
  argument or a result. Those are user content, and confidential and ephemeral sessions would leak
  through them.
- **GnollBench tool calls.** A run's calls are part of its own report, not of the chat's usage.
- **Searches that found nothing.** A tool call has no structured outcome, and its result text is
  pruned by retention, so "no match" cannot be counted reliably.
- **Editing.** The knowledge base and the GnollHack wiki are Git working trees, so changes belong in
  Git; the dashboards preview and index them, read-only. The NetHack converter is run by hand
  (§ 4).
