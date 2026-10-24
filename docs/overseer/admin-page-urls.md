# Admin Page URLs

Every tab and sub-tab of the Overseer Admin page has one address, as a **path**: a reload or a shared
link opens the same view, and Back and Forward move between the views the user chose. The one place
that knows the scheme is `Overseer/ClientApp/src/app/admin/admin-url.ts`, pinned by
`admin-url.spec.ts`.

## 1. The paths

| Path | Shows |
|---|---|
| `/admin` | Replaced by `/admin/users` |
| `/admin/users` · `/admin/users/groups` · `/admin/users/deletions` · `/admin/users/log` | Users and its sub-tabs *Users*, *Groups*, *Deletion requests*, *Admin log* |
| `/admin/configs` · `/admin/configs/api-keys` | System Configs; the second with the **API Keys** dialog (*Default API Keys*) open |
| `/admin/database` · `/admin/security` | Database, Security (no sub-tabs) |
| `/admin/telemetry` · `/admin/telemetry/models` · `/admin/telemetry/users` · `/admin/telemetry/groups` | AI Telemetry: *Summary*, *Models*, *Users*, *Groups* |
| `/admin/gnollbench` · `/admin/gnollbench/history` · `…/multi-run` · `…/multi-suite` · `…/suites` · `…/profiles` · `…/model-comparison` · `…/chat-consistency` | GnollBench: *Run Benchmark*, *Run History*, *Multi-Run Analysis*, *Batteries*, *Manage Suites*, *Scoring Profiles*, *Model Comparison*, *Chat Consistency* |
| `/admin/services` · `/admin/services/gnollhack-wiki` · `…/nethack-wiki` · `…/knowledge-base` · `…/config-health` · `…/developer-tools` | Services: *GnollHack Source*, *GnollHack Wiki*, *NetHack Wiki*, *Knowledge Base*, *Configuration Health*, *Developer Tools* |
| `/admin/feedback` · `/admin/feedback/gnollbench` | Feedback: *Overseer* (the default: the chat's feedback tool and the issues the chat assistant reported) and *GnollBench* (the issues GnollBench candidates reported, grouped by fingerprint) |

- **The first sub-tab is the default and has no segment.** `/admin/users/users`,
  `/admin/telemetry/summary` and `/admin/services/source`, which name the default by its id, are
  accepted and replaced by `/admin/users`, `/admin/telemetry` and `/admin/services` (and
  `/admin/feedback/overseer` by `/admin/feedback`). A sub-tab named by a code id that differs from its slug
  (`/admin/gnollbench/multirun`, `/admin/services/sentry`) is replaced by its slug the same way. A
  tab is found by its slug, or by a retired slug in `RETIRED_ADMIN_TAB_SLUGS` (`devtools`, the
  Services tab's slug until 2026-10-18), which is then replaced by the current one;
  `/admin/benchmark` is still an unknown tab.
- **An unknown value is corrected, never an error page.** An unknown tab becomes `/admin/users`, an
  unknown sub-tab the tab's default, and a path of more than three segments matches no route.
  Every correction replaces the history entry.
- **Slugs are not code ids.** The code keeps its ids — `benchmark`, `multirun`, `modelcomparison`,
  `deletions` and the rest, in every `localStorage` key — and `ADMIN_TAB_SLUGS` and
  `ADMIN_SUBTAB_SLUGS` translate. The address shows the visible name, so the GnollBench tab is
  `gnollbench`, and the Services tab, whose code id is `devtools`, is `services`. The one exception
  is the *Batteries* sub-tab, which keeps the slug `multi-suite` from its earlier name *Multi-Suite*:
  sub-tabs have no retired-slug list, so a new slug would break its bookmarks.
- **A renamed slug is kept as a retired slug.** A slug is part of every bookmark and alert that holds
  it, so renaming one would break them. A tab's old slug therefore stays in `RETIRED_ADMIN_TAB_SLUGS`,
  mapped to the tab it named, and **a retired slug is never reused** for another tab. A sub-tab's old
  slug needs no such list when it was the sub-tab's code id, because a sub-tab is also accepted by its
  id: that is how `/admin/services/sentry` still opens *Developer Tools*.
- **A sub-tab promoted to a tab keeps its old path.** `RETIRED_ADMIN_SUBTABS` maps a tab's former
  sub-tab slug to the tab it became: `/admin/telemetry/feedback` opens `/admin/feedback`, the Feedback
  tab since 2026-10-22. Such a slug is never reused as a sub-tab slug of that tab, or the redirect
  would shadow it.

## 2. Query parameters

**One-shot actions stay query parameters.** They are not places, so they are applied once and removed
(the history entry is replaced), and a reload or Back does not apply them again:

| Parameter | With | Does |
|---|---|---|
| `suiteId` | `/admin/gnollbench/suites` | Focuses one suite on Manage Suites (the chat's capture-board links) |
| `resolveConfig` | `/admin/configs` | Opens the model resolution dialog for one configuration (the chat composer's **Open System Configs**, the GnollBench launcher's resolve link) |

A valid `resolveConfig` on any other path is moved to `/admin/configs`, keeping it.

**Filter state on the Feedback tab stays in the query.** Both Feedback sub-tabs keep their facet
values as query parameters, so a filtered view can be reloaded and shared among administrators. Every
list is comma-separated. An absent `status` means the default, *New* and *Regressed*; `status=all` means
no status filter. Unknown values are dropped.

`/admin/feedback` (the *Overseer* sub-tab):

| Parameter | Holds |
|---|---|
| `status` | The *Status* facet's values (wire values such as `new`, `regressed`) |
| `category` | The *Category* facet's values (`missing_information`, `tool_problem`, `own_mistake`) |
| `model` | The *Model* facet's values (model ids; sent to the server as `modelId`) |
| `provider` | The *Provider* facet's values |
| `user` | The *User* facet's values (Identity user ids) |
| `group` | The *Group* facet's values (group ids, and `none` for *No group*) |
| `source` | The *Model source* facet's values (`system`, `user`, `unknown`) |
| `config` | The *Configuration* facet's values (system configuration ids) |
| `version` | The *Version* facet's values (Overseer versions) |
| `tool` | The *Tool* facet's values (related tool names) |
| `resolution` | *Has resolution*: `with` or `without` |
| `ids` | Issue ids, at most 500 (chips *Issue: #42*); set by the GnollBench sub-tab's *Overseer issue* link, not by a facet |
| `q` | The search text |

`/admin/feedback/gnollbench` (the *GnollBench* sub-tab):

| Parameter | Holds |
|---|---|
| `status` | As on the Overseer sub-tab |
| `category` | As on the Overseer sub-tab |
| `model` | The *Model* facet's values (the tested model's id) |
| `provider` | The *Provider* facet's values |
| `run` · `series` · `group` · `battery` · `batch` | Run, run series, Multi-Run Analysis group, battery run and model batch ids |
| `suite` · `question` | Suite and question ids |
| `condition` | Information conditions (`production`, `sourceOnly`, `closedBook`) |
| `corpus` | Corpus conditions (`live` for none) |
| `variant` | Experimental Changes: a spec, the change ids joined by `+` (a space reads as `+`), without the hash, or `none` |
| `harness` | Harness versions |
| `accepted` | The *Self-reports* select: absent for accepted calls only, `all`, or `refused` |
| `q` | The search text |

The two sub-tabs share some names (`status`, `category`, `model`, `provider`, `group`, `q`), so a sub-tab
switch removes every Feedback filter parameter (`FEEDBACK_FILTER_QUERY_PARAMS` in `admin-url.ts`) and
keeps the others, such as `sessionId`. The entering sub-tab then applies the address when it carries
any of its own filter parameters, and otherwise its remembered view, which it writes to the address with
the history entry replaced.

They are view state, not one-shot actions, so they are not removed. Each sub-tab also remembers its
filters, sort, search and date range in `localStorage` (`overseer.admin.feedback.overseer.view`,
`overseer.admin.feedback.gnollbench.view`); the sort and the date range are not in the address. See
`ai-feedback-reports.md` § 10.

**Every other query parameter is kept.** The chat's *Admin* link carries the chat's `sessionId` into
`/admin` with `queryParamsHandling="preserve"`, and the Admin page's *Back* link carries it back the
same way. So every tab navigation and every correction uses `queryParamsHandling: 'preserve'`; dropping
the query on a tab click would send *Back* to the wrong chat session. The GnollBench launcher's link
adds `resolveConfig` with `queryParamsHandling: 'merge'` for the same reason.

## 3. Old links

An address of the earlier form, `/admin?tab=X&subtab=Y` — in a bookmark, or in an account deletion
review email already sent — is replaced by its path, removing `tab` and `subtab` and keeping every
other parameter (so `suiteId` and `resolveConfig` still work):

| Old | New |
|---|---|
| `?tab=users&subtab=deletions` | `/admin/users/deletions` |
| `?tab=groups` | `/admin/users/groups` |
| `?tab=apikeys` | `/admin/configs/api-keys` |
| `?tab=benchmark&subtab=suites&suiteId=7` | `/admin/gnollbench/suites?suiteId=7` |
| `?tab=configs&resolveConfig=7` | `/admin/configs?resolveConfig=7` |
| `?tab=telemetry&subtab=users` | `/admin/telemetry/users` |
| `?tab=telemetry&subtab=feedback` | `/admin/feedback` |
| `?tab=` an unknown tab | `/admin/users` |

`legacyAdminPath()` does the mapping. The legacy redirect runs before any other correction, in the
same handler, so an old query on a non-canonical path issues one navigation.

A path with a retired tab slug, or with a sub-tab that became a tab (§ 1), is corrected like any other
non-canonical path, keeping every query parameter:

| Old | New |
|---|---|
| `/admin/devtools` | `/admin/services` |
| `/admin/devtools/config-health` | `/admin/services/config-health` |
| `/admin/devtools/sentry` | `/admin/services/developer-tools` |
| `/admin/telemetry/feedback?status=all&q=x` | `/admin/feedback?status=all&q=x` |

`parseAdminPath()` reads the retired slug, and the promoted sub-tab through `RETIRED_ADMIN_SUBTABS`,
which `legacyAdminPath()` also reads for `?tab=telemetry&subtab=feedback`. An old query link, `?tab=devtools`, names the code id and
maps through `legacyAdminPath()` to `/admin/services`.

The server's own links use the path form: the system alerts (`/admin/configs`,
`/admin/users/deletions`, `/admin/telemetry/users`, `/admin/services`) and the deletion review email, whose
link is `OverseerAdminUrl` with `/users/deletions` appended to its path
(`AccountDeletionReviewEmail.ReviewLink`).

## 4. History

| Selection by | History |
|---|---|
| A click, Enter or Space on a tab or sub-tab (GnollBench's included) | **Adds** an entry |
| Arrow keys, Home, End (they select as focus moves) | **Replaces** the current entry, so holding a key does not fill the history |
| Code: a page store request (`requestTab`, `showUsersLog`, `requestManageUser`, …), a GnollBench sub-tab its own code switches (*Repeat this run's setup*, a battery opening Multi-Run), a correction | **Replaces** the current entry |

Opening the API Keys dialog with its button adds an entry; Done, the close button and Escape replace it
with `/admin/configs`, and Back closes the dialog too. A tab chosen by click returns to the sub-tab it
showed last (System Configs always without the dialog).

Back and Forward restore the tab and sub-tab the address names; focus is not moved.

**Back and Forward do not ask GnollBench's leave guard.** Some GnollBench actions consult
`refuseLeaving()` before they switch sub-tab. A sub-tab change that Back or Forward brings arrives
through the component's `navigation` input and is applied without that check, like any sub-tab handed
in through that input. Pressing Back is an explicit user action, so this is accepted.

## 5. How it works

- **One route serves all three shapes.** `app.routes.ts` routes `admin`, `admin/:tab` and
  `admin/:tab/:subtab` through one entry with `matcher: adminUrlMatcher`. `CustomRouteReuseStrategy`
  reuses a route whose `routeConfig` is the same object, so a tab change keeps the same
  `AdminComponent`, its page-scoped `AdminPageStore` and the data it loaded; separate route entries per
  shape would re-create the page and reload groups, configurations and keys on every click.
- **The address sets the view.** `AdminComponent` subscribes once to the path and the query together
  and applies each address in order: an old `?tab=` link is redirected; a non-canonical path (or a
  `resolveConfig` off System Configs) is corrected; then `store.applyLocation(tab, subTab)` records the
  tab and its sub-tab (and whether the API Keys dialog is open), the GnollBench panel receives the
  sub-tab it does not show yet, and the one-shot parameters are applied and removed.
- **A tab moves the address.** Tabs and sub-tabs call `store.goTo(tab, subTab, 'push' | 'replace')`,
  which records the selection and navigates to `adminCommands(tab, subTab)`; a `null` sub-tab means the
  one the store remembers for that tab.
- **GnollBench** reports its own sub-tab changes through `(subTabChange)` (`{ tab, byUser }`); the
  shell compares the address with the sub-tab the panel **shows**, not with the store's memory, so a
  re-entered `/admin/gnollbench/suites` opens Manage Suites on a freshly created panel. A navigation to
  the address already shown is ignored by the router, which ends the round trip.
- **The guard runs on every tab change** (`requireSignedIn`); it answers from the cached user at once.
- **Deep paths are safe on the server**: `index.html` has `<base href="/">`, and the SPA fallback in
  `Program.cs` serves any unmatched path.

## 6. Adding a tab or a sub-tab

1. Add the id to `AdminTabId` and its slug to `ADMIN_TAB_SLUGS`, or the `[id, slug]` pair to the tab's
   list in `ADMIN_SUBTAB_SLUGS` (the first entry is the default, with an empty slug). A new GnollBench
   sub-tab extends `BenchmarkSubTab`; `BENCHMARK_SUBTAB_SLUGS` is a `Record` over that type, so a
   missing slug fails to compile.
2. A new top-level tab also goes into `AdminComponent.tabs` (its label and order) and gets its panel in
   `admin.component.html`.
3. If the tab has sub-tabs, give `AdminPageStore` a field that remembers the selected one and handle
   the tab in `applyLocation` and `rememberedSubTab`.
4. Select through `store.goTo` with `'push'` for a click and `'replace'` for the keyboard, as
   `frontend_ui_controls` § 5 describes.
5. Extend `admin-url.spec.ts`, which reads every tab and sub-tab from its canonical path and checks the
   legacy mapping and that every GnollBench sub-tab has a slug.
6. A sub-tab promoted to a tab keeps its old path working through `RETIRED_ADMIN_SUBTABS`: add its
   slug under the tab it left, mapped to the new tab, and drop it from `ADMIN_SUBTAB_SLUGS`.
