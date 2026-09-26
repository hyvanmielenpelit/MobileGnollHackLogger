---
name: overseer_frontend
description: Guidelines for implementing and designing the frontend for the Overseer project.
---
# Overseer Frontend Guidelines

When working on the frontend for the Overseer project within MobileGnollHackLogger, follow these guidelines:

1. **Prefer Angular Components**: Whenever implementing frontend features or UI elements, always prefer using Angular components.
2. **Component Structure**: Always use separate files for templates (`.html`) and styles (`.scss`). Do not use inline templates or styles in the component TypeScript file.
3. **No Basic JS Popups**: NEVER use `alert()`, `confirm()`, or `prompt()` JavaScript popups under any circumstances. When these are needed, use inline error messages or a native `<dialog>` for confirmations and inputs, per **Popups and Modals** below.
4. **Follow modern web platform best practices**: UI/Layout, Scroll/Motion, Performance, Accessibility, and System/APIs. The baseline is in **Modern Web Baseline** below, and it binds in every harness.
5. **Buttons, icon buttons, and tabs have their own specification**: read
   [`frontend_ui_controls`](../frontend_ui_controls/SKILL.md) before adding or restyling any
   of them, including toolbars and dialog footers.
6. **UI text is US English.** See `AGENTS.md` § Language and Spelling.

### Before writing HTML, CSS, or client-side JS

**If your harness provides a `modern-web-guidance` skill, execute it first** — in *either*
harness. It carries more current and more detailed guidance than this file. It ships with
Antigravity (it is Google's), and it is also available in **Claude Code** as a plugin skill
(`modern-web-guidance:modern-web-guidance`), so check your own skill list rather than
assuming.

**If it genuinely is not available, the Modern Web Baseline below is the standard, and it is
sufficient to proceed.** Do not stall waiting for a skill your harness has no way to load,
and do not silently skip the requirement either: say in chat which of the two applied.

> [!NOTE]
> Claude Code's nearest skills are `artifact-design` (design fundamentals for self-contained Claude
> Artifact pages) and `dataviz` (chart and dashboard design). **Neither is a substitute here** —
> they target standalone generated pages, not an existing Angular application with its own design
> system. Use them only if the task really is a chart or a standalone artifact.

## Modern Web Baseline

Harness-neutral, and the floor for any Overseer frontend work.

### Semantics and accessibility
- Interactive controls are real elements: `<button type="button">`, `<a href>`, `<label>` — never a `div` with a click handler.
- Every control has an accessible name. An icon-only button needs `aria-label`, and the name must be **distinct**: `aria-label="Copy reply to question 3"`, not three buttons all named "Copy".
- State the user must notice goes in a live region: `aria-live="polite"` for transient confirmations ("Copied"), `role="status"` for progress that advances on its own. A change that is only visible is invisible to a screen-reader user.
- Keyboard reachable in a sensible order, with a **visible** focus ring. Never `outline: none` without a replacement.
- Respect `prefers-reduced-motion` for spinners, transitions, and auto-scrolling.
- Colour is never the only carrier of meaning — pair it with text or shape.

### Layout and content
- Long or unbounded content gets an explicit strategy: `overflow-x: auto` on wide blocks, `white-space: pre-wrap` plus a collapsed max-height and an expand control on long text. A page that grows without limit is a defect, not a detail.
- When content is collapsed or truncated for display, actions on it (copy, download, export) still operate on the **full** value.
- Prefer modern layout primitives (flex, grid, logical properties, container queries) over fixed pixel scaffolding.

### Platform APIs
- Feature-detect before use, and handle rejection. `navigator.clipboard` is **undefined outside a secure context** and can reject even inside one — show an inline failure message, never a bare `console.error`.
- Object URLs from `URL.createObjectURL` are revoked after use.
- Filenames built from user or model data are sanitised to a whitelist (`[A-Za-z0-9._-]`), never interpolated raw.
- Never render untrusted or model-generated text as HTML. Plain text in `<pre>`, never `[innerHTML]`.

### Angular specifics
- Standalone components; `@if` / `@for` control flow with `track`; `OnPush` change detection with explicit `markForCheck()`.
- Every subscription, timer, and observer is torn down in `ngOnDestroy`.
- Polling uses `switchMap` so requests cannot stack, and backs off on error rather than hammering.

## Styling UI Elements

### Global Styles
- **`Overseer/ClientApp/src/styles.scss` is the EXCLUSIVE global styles file** for the Overseer Angular project.
- **Do NOT modify `site2.scss` or `site2.css`** when working on the Overseer project (those files are strictly for the main ASP.NET MobileGnollHackLogger web pages).
- If you find duplicate styles across multiple component SCSS files (e.g. `.settings-container` or `.header-row`), move them to `styles.scss` for centralized management.
- **A style needed by a second component moves to `styles.scss`; it is not copied.** Copying
  is how `.btn-gh-small` ended up defined in `admin.component.scss` and therefore
  unavailable to the benchmark view that needed it.
- **Use the design tokens** — `var(--primary-color)`, `var(--gold-glow)`,
  `var(--border-glass)`, `var(--nav-color)` — not the literal hex values they hold.
- **`.gh-fieldset`** groups related controls in a themed `<fieldset>`/`<legend>`, with
  `.gh-fieldset-hint` for a one-sentence purpose line under the legend; **`.gh-disclosure`** is
  the themed native `<details>`, with `.gh-disclosure-body` for its content. Both are global.

When creating UI elements in the Overseer frontend, adhere to the following standards:

### Buttons, Icon Buttons, and Tabs

> [!IMPORTANT]
> **These three are specified in full by the [`frontend_ui_controls`](../frontend_ui_controls/SKILL.md)
> skill. Read it before adding or restyling any button, icon button, toolbar, or tab row.**
> The detail is deliberately not duplicated here — a second copy is a second thing to
> forget to update.

The short version, so you know whether you need it:

- Labelled actions are `.btn-gh`, the decorative GnollHack image button, with
  `.btn-gh-cancel`, `.btn-gh-delete`, or `.btn-gh-small`. **Those four are the entire
  vocabulary** — never invent a variant, and never redefine `.btn-gh` in a component
  stylesheet (view encapsulation makes such an override invisible everywhere but that one
  component, which is how the AI Benchmark tab silently lost its image buttons).
  Secondary actions beside one primary are `.btn-ghost`.
- **An icon on a labelled button is decided case by case, never by default.** The test: if
  you deleted the label, would the glyph still say what the button does? `+ New Profile` and
  `▷ Start Benchmark` pass. `Done`, `Cancel`, `Save Profile`, and `Scoring Profiles` do not
  — they stay text-only. Full guidance and the worked tables are in
  [`frontend_ui_controls`](../frontend_ui_controls/SKILL.md) §3a.
- Icon-only buttons are `.action-btn` / `.action-btn-danger` in rows and cards, and
  `.btn-icon-action` for dialog close buttons. Each needs a **distinct** `aria-label` that
  names its subject, and a tooltip via `interestfor` — never `title`.
- A control that swaps the content below it is a **tab**, not a button: use the shared
  `.gh-tabs` / `.gh-tab` widget with its full ARIA and keyboard contract.

### Popups and Modals
- **Use the Native `<dialog>` Element**: New popups and modals must use the semantic HTML `<dialog>` element rather than custom `div`-based overlays.
- Apply the `.gh-dialog` class to `<dialog>` elements for consistent theming (glassmorphism, padding, backdrop).
- Control the dialog via its native API (`dialog.showModal()` and `dialog.close()`) using Angular `@ViewChild` references.
- DO NOT use `.modal-overlay` wrappers for new popups, as `<dialog>` provides native accessibility, focus management, and top-layer positioning.

### Tooltips
- **Always use an interest-triggered tooltip**: `interestfor="tooltip-id"` on the trigger,
  with a matching `<div popover="hint" id="tooltip-id" class="gh-tooltip">`. Never the
  native `title` attribute — it cannot be styled, does not appear on keyboard focus, and is
  not a valid accessible-name mechanism.
- **`.gh-tooltip` is global**, in `styles.scss`. Load the polyfills with
  `ensureOverlayPolyfills()` from `app/utils/polyfills.util.ts`.
- **Full contract** — anchor naming, why `[attr.style]` rather than `[style.anchor-name]`,
  which ARIA attributes `interestfor` supplies for you (and must therefore not be set by
  hand), and the `:popover-open` polyfill caveat — is in
  [`frontend_ui_controls`](../frontend_ui_controls/SKILL.md) §4.

### Error Handling & User Prompts
- **Avoid Basic JS Dialogs**: As stated in the main rules, do NOT use basic JavaScript `alert()`, `prompt()`, or `confirm()` dialogs. They disrupt user flow, unfocus elements, and look outdated.
- **Use Modern Equivalents**: Implement inline error messaging (e.g., displaying error text near an input field) or use styled `<dialog>` modals for confirmation/input prompts, ensuring integration with Angular state and modern web guidance.

### Icons
- **Use SVGs exclusively**: DO NOT use Unicode emojis (e.g., ✨, 🐛, 🚀) for UI elements or icons. They are notoriously difficult to align correctly, render inconsistently across operating systems, and look unprofessional.
- Always use precise `<svg>` icons that inherit the surrounding text colour (`stroke="currentColor"` for the line icons used throughout, `fill="currentColor"` for solid ones) and align via Flexbox (`display: flex; align-items: center`).
- **Icons inside buttons** have a stricter contract — 16×16, `viewBox="0 0 24 24"`,
  `class="btn-icon"`, `aria-hidden="true"`, leading the label. See
  [`frontend_ui_controls`](../frontend_ui_controls/SKILL.md) §3.

### Checkboxes
- **Use the `.checkbox-label` Pattern**: Checkboxes should be wrapped inside a `<label class="checkbox-label">` element containing the `<input type="checkbox">` and the descriptive label text.
- **Dimensions & Theme**: Checkbox inputs are sized at 20x20px with a 10px flex gap between the input and text, and styled with the golden theme accent color (`accent-color: var(--primary-color, #d4af37)`).
- **Multi-Line Alignment (`.align-start`)**: When the checkbox copy spans multiple lines or contains descriptive subtext, add the `.align-start` class modifier to top-align the checkbox with the first line of text.
- **Centralized Styling**: Checkbox styling is managed centrally in `src/styles.scss`. Do not duplicate checkbox CSS rules in component SCSS files.
- **Radio groups** use the global `.gh-choice` fieldset (its `<legend>` names the group) with one `<label class="gh-radio">` wrapping each radio input, also from `src/styles.scss`.

## Project Structure and Navigation

The Overseer project is an ASP.NET Core backend serving an Angular frontend.
- **Frontend (Angular)**: Located in `Overseer/ClientApp/src/app/`. Contains all components, services, and routes.
- **Backend (ASP.NET Core)**: 
  - `Overseer/Controllers/`: API Endpoints
  - `Overseer/Hubs/`: SignalR Hubs (e.g., for real-time chat)
  - `Overseer/Services/`: Backend business logic
  - `Overseer/Models/`: Data Models

### Static Assets and Build Output (`wwwroot` vs `public`)
- **NEVER put source files, source images, or static assets directly into `Overseer/wwwroot/`.**
- The entire `Overseer/wwwroot/` folder is a **build output directory**. It is wiped and repopulated entirely by the Angular `npm run build` process. 
- Any files manually placed in `Overseer/wwwroot/` will be permanently deleted upon the next build.
- **Where to put assets**: Place all new images, icons, and static files in the Angular source directory: `Overseer/ClientApp/public/` (or `Overseer/ClientApp/public/img/`).
- During the build, Angular will automatically copy everything from `ClientApp/public/` into `wwwroot/`.
- **Git Tracking**: Because `wwwroot/` is strictly a build output, none of its contents should be tracked by Git. The `.gitignore` at the repository root prevents it from being committed.

### Pages (Routes)
The Angular application's routes are defined in `app.routes.ts`. The primary pages include:
- `/chat` (`chat.component`): The main chat interface.
- `/settings` and `/settings/:section` (`settings.component`): User preferences, sectioned.
  `:section` is one of `general`, `permissions`, `performance`, `confidentiality`, `masking`,
  `chats`; a bare `/settings` or an unknown section shows General.
- `/api-keys` (`api-keys.component`): Management of user API keys.
- `/models` (`models.component`): AI Model selection and configuration.
- `/admin` (`admin.component`): System administration (groups, configs, rate limits).
- `/debug-log` (`debug-log.component`): Developer debug logs.
- `/login` (`login.component`): Authentication entry point.

### Popups (`<dialog>` elements)
To find specific popups, look in the corresponding component's `.html` template:

- **Admin Component (`admin.component.html`)**
  - `#manageGroupsDialog`: Manage Groups
  - `#createGroupDialog`: Create Group
  - `#configDialog`: Config
  - `#config-filter-panel`: Config Filter (`popover="auto"`, anchored to `#config-filter-trigger`)
  - `#confirmDialog`: Confirm
  - `#manageUserConfigsDialog`: Manage User Configs
  - `#manageGroupConfigsDialog`: Manage Group Configs
  - `#editConfigOverrideDialog`: Edit Config Override
  - `#rateLimitsDialog`: Rate Limits
  - `#analyticsDialog`: Analytics

- **API Keys Component (`api-keys.component.html`)**
  - `#apiKeyInfoDialog`: API Key Info

- **Chat Component (`chat.component.html`)**
  - `#deleteConfirmDialog`: Delete Confirm
  - `#imagePreviewDialog`: Image Preview
  - `#reportConfirmDialog`: Report Confirm
  - `#logoutDialog`: Logout
  - `#privacyDialog`: Privacy for this chat (Standard / Confidential / Incognito), opened from
    the composer's privacy button; offered only while no chat is open.
  - `#privateBadgeDialog`: Privacy details for the open chat — opened by the Private badge in
    the composer's indicator strip.
  - `#ephemeralInfoDialog`: What Incognito means for the open chat — opened by the Incognito
    badge in the composer's indicator strip.
  - `#ephemeralCloseDialog`: Delete Incognito Chat confirmation — opened by the strip's
    Delete chat button, and by the navigation guard when leaving an incognito chat with content.

- **Admin Alerts Component (`admin-alerts.component.html`)**
  - `#popoverContainer`: System alert popover banner displaying missing configuration warnings from `AdminAlertService` (`/api/admin/system-alerts`) to admin users.

- **Models Component (`models.component.html`)**
  - `#modelPickerDialog`: Model Picker
  - `#editModelDialog`: Edit Model
  - `#deleteModelConfirmDialog`: Delete Model Confirm

- **Settings Component (`settings.component.html`)**
  - `#confirmDialog`: Confirm
  - `#changelogDialog`: Changelog

- **Benchmark Component (`benchmark.component.html`, Admin → AI Benchmark)** — not an exhaustive
  list of this component's dialogs, only the ones recorded here so far:
  - `#importDefaultSuitesDialog`: Import Default Suites (Manage Suites tab) — a multi-select
    catalog of the default suite files under `Overseer/Data/DefaultSuites/`, opened by the
    toolbar's Import Default Suites button (from harness 24).
  - `#questionYamlImportDialog` (`app-question-yaml-import-dialog`, `question-yaml/`): YAML import
    in three modes — replace one question (a question's Import from YAML), import into the open
    suite (Manage Questions toolbar), create a suite (Manage Suites toolbar's Import Suite from
    YAML) — with Provide YAML, Review and Done steps. In **suite** mode it attaches the game
    snapshot the document carries, behind a checkbox on the review step whose sentence comes from
    a server preflight (`POST snapshots/match`) that says whether an identical board is already
    stored and who owns it. Its help link routes to whichever help dialog matches the open mode.
  - `#questionYamlHelpDialog` and `#suiteYamlHelpDialog` (`app-question-yaml-help-dialog`): **two
    instances of one component**, selected by its `variant` input (`questions`, the default, and
    `suite`). Both live in one document, so every element id carries the variant's `idPrefix`
    (`yaml-help`, `suite-yaml-help`) — a duplicated id silently breaks `aria-labelledby`,
    `aria-controls`, the tooltip anchors and the exclusive `<details name>` accordion. The
    questions variant fetches the rubric authoring guidance and assembles its *AI Prompt* text;
    the suite variant makes **no** server call — its guide text is static (`suite-yaml-guide.ts`)
    and its *AI Prompt* tab explains the Snapshot Suite Wizard and emits `(wizardRequested)`, on
    which the page closes the help and opens the wizard. Every code sample in both variants is an
    `app-code-block` (`shared/code-block/`).
  - `#snapshotSuiteWizard` (`app-snapshot-suite-wizard`, `question-yaml/`): the Snapshot Suite Wizard
    from the Manage Suites toolbar — from a game snapshot to an imported suite, by adding questions
    to a snapshot suite (seven steps, the last, *Describe*, applying the description the agent
    suggested) or creating a new one (six steps). It holds `app-suite-prompt-builder` and an
    `app-question-yaml-import-panel` (the import body the standalone import dialog also wraps), and
    keeps its progress in `localStorage['overseer.snapshotSuiteWizard']`.
  - `#snapshotUploadDialog` (`app-snapshot-upload-dialog`, `snapshot-upload/`): Upload Snapshot from
    a suite card, with a nested `#replaceConfirmDialog` when the suite already has a snapshot.
    Delete Snapshot lives in the snapshot viewer's Delete tab (`#deleteConfirmDialog` in
    `snapshot-viewer.component.html`).

- **Comparison Source Picker (`comparison-source-picker.component.html`, Admin → AI Benchmark →
  Run History → Cross-model comparison, step 1)** — single runs and analysis groups on two
  kind tabs (`csp-src-tab-runs` / `csp-src-tab-groups`), one table per tab, with a pager above and below it.
  - `#legendDialog`: About conditions — full-screen: what a condition is, which one the charts
    use, the three kinds of key as plain-language cards with technical names collapsed, and
    every condition as an exclusive accordion (`<details name="csp-conditions">`, only the open
    body rendered), the charted one pinned first and the rest newest first, five more per Show
    more; technical details behind a toggle; nothing rendered while closed. Opened by the
    *About conditions* button (`#csp-legend-trigger`).
  - `#conditionDetailDialog`: Comparability detail for one source — one row per key it differs
    from the reference condition on, with this source's value beside the reference's, a
    `key=value;` configuration split into fields with the changed ones marked, and a copy-as-
    Markdown control. Opened by the info button beside a row's *Condition X* badge, and offered
    only where that source actually differs.

- **Model Comparison (`model-comparison.component.html`, Admin → AI Benchmark → Run History →
  Cross-model comparison)** — two steps: *1. Sources · 2. Charts & table*. Step 2 is reachable as
  soon as a comparison exists, even one no chart can draw.
  - No preview dialog: step 2 is a workspace with four view tabs — **All charts** (grid), **Single
    chart** (eye), **Interactive table** (table) and **Table preview** (image). When nothing can be
    charted the two chart tabs are `aria-disabled`, the reason is shown (per shape: no models, fewer
    than two models measured the same way, or the admin unticked models under Data → Models down to
    fewer than two), and the step opens on *Interactive table*. The view bar (`.mc-fig-bar`) holds
    only the sidebar toggle and the view tabs; each view's actions are on its own toolbar row.
  - **About** and **Recompute** sit at the right end of the step tab row (`.mc-wizard-tabbar`,
    group `.mc-wizard-meta`), on step 2 only.
  - **About** (`#mc-about-trigger`) is a `.btn-ghost` with the help-circle glyph the source picker's
    *About conditions* button already uses, visible text *About*, and a count badge equal to the
    number of caveat notes while any exist. It opens the **About this comparison** dialog
    (`#aboutDialog`): a one-line summary; *Read these first* (thinking levels differ; every model has
    only one run); *Not in the charts* (models measured differently, the keys they differ on, behind
    a Details disclosure); *How to read the charts*; and *Not shown as charts* (the server's refused
    measures, each with a summary, an *Instead: …* line and a *Why* disclosure). The dialog body
    renders only while it is open, and it stops propagation of its own close and cancel events so
    they never reach, and close, the wizard's own dialog.
  - **Recompute** is icon-only: a `.action-btn` with the rotate glyph,
    `aria-label="Recompute the comparison"` and the tooltip *Recompute this comparison*. A refetch
    whose payload carries the same entry keys (from changing Prices or from Recompute) keeps the
    admin's Show and Highlight choices; a different set of entries reseeds both.
  - **The sidebar follows the view.** Chart views show **Data · Theme · Charts · Download**; table
    views show **Data · Theme · Table · Download**. A tab in both sets stays selected across a view
    change; only *Charts* and *Table* swap. Stored tabs migrate `emphasis` → `data`, `style` →
    `charts`, `download` / `export` → `download`. The sidebar's collapsed state, width, tab and view
    are in `localStorage['overseer.modelComparison.figureSidebar']`.
  - **The sidebar is resizable** with an `app-pane-resizer`: 18–40 rem (at most half the
    workspace), 26 rem by default, stored as `sidebarWidth` (px) in the same record and applied as
    `--mc-sidebar-width` on `.mc-fig-workspace`. The resizer is in **its own 12 px grid column**
    between the sidebar and the main area, and the sidebar has no border of its own, so the
    handle's line is the only divider. The handle is not rendered while the sidebar is collapsed
    and is hidden where the sidebar stacks (workspace ≤ 860 px).
    - **Data**, in order:
      - **Models** (both view groups) — one row per model with a **Show** checkbox (plot this
        model, up to the chart cap) and a **Highlight** checkbox, shown only in the chart views
        (draw it in gold, grey the rest). This table merges the former step-2 *Entries in the
        charts* checklist and the Data tab's *Emphasis* list. "Emphasise" is renamed *Highlight* in
        the UI only; the internal names (`emphasisKeys`, `toggleEmphasis`, `isEmphasised`,
        `clearEmphasis`) are unchanged. Unticking Show takes a model out of every figure only; it
        stays in the table, whose own State column filter is independent of Show.
      - **Measures** (chart views only) — speed measure, cost measure, as radio groups.
      - **Prices** (both view groups) — the pricing basis select (*Today's prices — fair
        across dates* / *Prices at run time — what was spent*); changing it recomputes the
        comparison server-side.
      - **Model order** (both view groups) — *Order by* has a **Custom** choice: an
        `app-reorderable-list` of every entry, with a divider where the charts stop plotting,
        *table only* tags and *Reset custom order*; the custom order lives for the wizard session
        only. **One model order drives the charts and the table**: the Interactive table, the Table
        preview and every table export follow it until a column header is clicked, and *Use model
        order* returns to it.
    - **Theme**: `app-figure-style-panel kind="appearance"` — dark or light theme, theme /
      transparent / custom background with a never-exported preview backdrop, one of six
      self-hosted font families or *Overseer default*, heading and label weights, heading and text
      colors with contrast warnings, and a figure border. It reaches every chart and the table
      image.
    - **Charts**: the segmented *Bar panels / Profile / Trade-offs* tab row over
      `app-figure-style-panel`.
    - **Table**: `app-table-settings-panel` — *Columns* (an `app-reorderable-list` of the 28
      display columns, checkable, *Model* locked; *Default columns*, *Only columns with values*, *All
      columns*) and *Image layout* (row bands, row rules). One column configuration drives the
      Interactive table, the Table preview and every download and copy; reading formats keep a
      combined column combined, data formats write its parts, and no value is written twice.
    - **Download** holds settings only, no download or copy button: chart views — *Chart size*
      (`app-export-size-section`, id prefix `mc-export`), *Image format* and a hint naming where the
      downloads are; table views — *Table format* (Excel, CSV, TSV, Markdown, JSON, HTML, Image),
      *Table image size* (`app-export-size-section` with *Fit the table*, id prefix
      `mc-table-image`, the *Fit the table* size shown as information in custom mode), the same
      *Image format* and a scope line.
  - **Toolbar rows**, one per view, each ending in a right-aligned group of icon-only
    `.action-btn`s: *All charts* — zoom, then **Download all charts** (the *download-all* glyph,
    two arrows into one tray); *Single chart* — figure picker, zoom, **Copy** and **Download**;
    *Table preview* — *Comparison table* label, zoom, **Copy table** and **Download table**;
    *Interactive table* — *Comparison table* heading with an `app-info-tip` (*Every entry is
    listed here…*, also the table's `aria-describedby`), **Copy table** and **Download table**,
    sticky while the table scrolls, then one meta line *Computed … · Rows follow the model order: …*
    with a link-style *Use model order* once a header sorts it. Copy table's and Download table's
    names follow the format. The rows stay on one line until the panel is under 44 rem wide.
  - **All-charts tiles** each carry **Copy**, **Download** and **Open in Single view** top-right,
    shown while the tile is hovered or holds focus and always on devices without hover (opacity
    only). Copy and Download are tab stops; Open is not, since Enter on the tile opens it.
  - **Every chart view shows bitmaps composed by the export pipeline** — `resolveFigureLayout`,
    `renderPlotOffscreen` from each card's Chart.js configuration, then the chrome — the same code
    that writes the downloaded file, so what is on the page is what is downloaded. No
    `BaseChartDirective` renders on step 2 and nothing reads a live chart canvas; the component
    registers the `provideCharts` registerables itself for that reason. *All charts* shows every
    chart as a focusable tile (`<canvas role="img">` named by its title and subtitle; Enter or a
    click opens it in *Single chart*) in a natively scrolling grid with a zoom group and **Fit
    height**; tiles are composed at display resolution, debounced, only near the viewport, with a
    generation counter discarding a stale composition. *Single chart* has zoom, drag-pan, *Fit to
    screen*, *Actual pixels*, *Reset view*, Copy and an icon-only Download. *Table preview* reuses that stage for
    the table image, with its own view state opening at Fit. Every composition first awaits the
    chosen font (`ensureFigureFont`).
  - Stored per browser: `overseer.modelComparison.figureStyle` (the per-family styles plus
    `appearance` and `table`), `figureSize`, `tableImageSize`, `tableColumns`, `download` (table
    format, image format, WebP quality) and `tableSettingsOpen`. A figure with its uncertainty bars
    hidden says so in a caption note unless that note is switched off too; warning notes have no
    switch.

## Component Reuse and State Management

The Overseer frontend utilizes a custom `RouteReuseStrategy` (indicated by `data: { reuse: true }` in `app.routes.ts`) for primary views like the `ChatComponent`. This prevents the component from being destroyed when navigating away, ensuring chat history and UI state are preserved.

However, this design introduces a critical pitfall: **`ngOnInit()` is NOT triggered when navigating back to a reused component.**

### The Correct Design Pattern
If a user changes settings, API keys, or models on a different page and navigates back to the chat window, the chat window must reflect these changes immediately. To achieve this, use the following patterns:

1. **Router Navigation Events (Recommended for data fetching)**
   Subscribe to Angular's router `NavigationEnd` events in the reused component. When detecting a re-entry to the component's route, explicitly call a data-loading method (e.g., `loadSettings()`) to fetch fresh state.

   ```typescript
   // In the reused component (e.g., ChatComponent)
   let previousUrl = '';
   this.router.events.pipe(
     filter(event => event instanceof NavigationEnd)
   ).subscribe((event: any) => {
     const currentUrl = event.urlAfterRedirects;
     if (currentUrl && currentUrl.startsWith('/chat')) {
       if (previousUrl && !previousUrl.startsWith('/chat')) {
         // We re-entered the component. Refetch data.
         this.loadSettings(false); 
       }
     }
     previousUrl = currentUrl || '';
   });
   ```
   **CRITICAL**: Extract your initialization logic from `ngOnInit()` into a dedicated `loadSettings(isInit: boolean)` method so it can be called safely on both initial load and re-entry.

2. **Shared RxJS State (Recommended for single-value live UI updates)**
   Use `BehaviorSubject` or `Subject` in shared services (like `SettingsService` or `AuthService`). The reused component should subscribe to these observables in `ngOnInit()` so that any updates pushed by other pages automatically trigger UI updates in the background.

   ```typescript
   // In SettingsService
   public showThoughtsAndToolsUpdated = new Subject<number>();
   
   // In SettingsComponent (firing the change)
   this.settingsService.showThoughtsAndToolsUpdated.next(newValue);
   
   // In ChatComponent (listening)
   this.settingsService.showThoughtsAndToolsUpdated.subscribe(val => {
     this.showThoughtsAndTools = val;
   });
   ```

3. **Prevent HTTP Caching**
   When explicitly re-fetching data via HTTP GET upon component re-entry, ensure the request includes `no-cache` headers. Otherwise, the browser may return a stale cached response from before the settings were changed.

   ```typescript
   this.http.get<MyData>('/api/data', {
     headers: {
       'Cache-Control': 'no-cache',
       'Pragma': 'no-cache',
       'Expires': '0'
     }
   });
   ```

## Avatar Animation State Machine

The Overseer chat interface (`chat.component.ts`) features an animated avatar that reacts to the AI's generation process. When modifying the avatar logic, adhere strictly to these rules to ensure the animations correctly represent the AI's internal state.

Never use simplistic flags (like applying `thinking` for the entire streaming duration) that violate this matrix.

### Animation Lifecycle

The avatar follows a strict lifecycle from the moment the user clicks Send to the end of the response:

1. **User clicks Send** → **Thinking** animation plays immediately (waiting for the server to respond).
2. If the server takes longer than 30 seconds to respond → **Yawning** replaces Thinking. (Thinking and Yawning only happen in this initial wait phase).
3. When the first `thinking_chunk` or `tool_start` event arrives → **Tool Use** animation plays.
4. **Tool Use** animation plays continuously during all active work. It NEVER reverts to Thinking or Yawning, even if the server is silent for a long time.
5. When the main content (final response) starts streaming → **Talking** animation plays.
6. When the response finishes → **Static Image** (idle). This is a **terminal state** that never auto-transitions to any animation.

### Animation Rules

| Animation | Allowed to Play When | Angular Condition | Priority |
| :--- | :--- | :--- | :---: |
| **Static Image (idle)** | When no streaming is active. This is both the initial state and the **terminal state** after a response finishes. A static image NEVER auto-transitions to any animation; only a new user Send action restarts the animation cycle. | `this.isStreaming === false` | 1 (highest) |
| **Talking** | ONLY when the main content (final response) is actively streaming to the user. | `this.hasRealContent === true` | 2 |
| **Tool Use** | During the entire pre-response **active working phase**. This includes actively streaming native "thinking texts" (reasoning chunks), buffering pre-tool preamble text, or actively executing a tool call. Once the avatar enters Tool Use, it remains in Tool Use until Talking. | `this.hasEnteredWorkingPhase === true` | 3 |
| **Thinking** | ONLY during the **initial wait phase** before the server has started returning any active work (tools/thinking texts). Once the working phase starts, Thinking can NEVER play again for that message. | `this.isStreaming === true`<br>AND none of the above are true | 4 |
| **Yawning** | ONLY when the **Thinking** animation has been playing continuously for more than 30 seconds. It must NEVER trigger from any other animation state (Talking, Tool Use, or Static Image), regardless of how long those states last. | `desired === 'thinking'`<br>AND cumulative Thinking time > 30,000 ms | 5 (lowest) |

### Key Constraints

- **Yawning is exclusive to Thinking:** The yawning timer only counts time spent in the Thinking state. Because the server might be completely silent (e.g., repeatedly returning 503 errors and retrying) during the initial wait phase, `updateDesiredAvatarState()` schedules a dedicated `yawningCheckTimeout` exactly 30 seconds into the `thinking` state to guarantee the transition to yawning happens even without incoming server events. This timer is cleared as soon as the avatar enters `toolUse` or `idle`.
- **Session State Persistence:** When the user switches between different chat sessions, `ChatComponent` persists its internal timing state (`thinkingAnimStartTime` and `hasEnteredWorkingPhase`) in a `sessionStateMap` keyed by `sessionId`. This ensures that if the avatar was yawning before switching chats, returning to the chat restores the original elapsed time and instantly resumes the yawning animation, rather than restarting the 30-second timer from zero.
- **All animations wait for loop end by default:** No animation is interrupted mid-loop. This is controlled by the `INTERRUPTIBLE_ANIMATIONS` static set in `ChatComponent`. By default this set is empty, meaning all animations play their current loop to completion before switching. To make an animation immediately interruptible, add its name to the set.
- **`lastNetworkActivityTime`** was used historically but is no longer relied upon for state transitions, as the avatar is now strictly phase-locked into Tool Use once work begins.

### Smooth Animation Transitions

Each avatar animation is a looping animated WebP with a known loop duration (defined in `AVATAR_LOOP_DURATIONS`). To avoid jarring visual snaps (e.g., a hammer disappearing mid-swing), transitions between animations must respect loop boundaries. The `requestAvatarTransition()` method implements this system:

**Architecture:**
1. `updateDesiredAvatarState()` determines *what* the avatar should be doing based on the current flags.
2. `requestAvatarTransition(newState)` determines *when* to switch, using loop-boundary scheduling.
3. `applyAvatarState(state)` performs the actual DOM swap (changing `currentAvatarState`, which updates the `<img>` src).

**Transition rules per animation (default configuration):**

| Current Animation | Can be Interrupted Mid-Loop? | Configurable? |
| :--- | :--- | :--- |
| **Static Image (idle)** | Yes, always | No — idle is not an animation, so it is always immediately replaceable. |
| **Thinking** | No — waits for loop end | Yes — add `'thinking'` to `INTERRUPTIBLE_ANIMATIONS` to allow mid-loop interrupts. |
| **Tool Use** | No — waits for loop end | Yes — add `'toolUse'` to `INTERRUPTIBLE_ANIMATIONS` to allow mid-loop interrupts. |
| **Talking** | No — waits for loop end | Yes — add `'talking'` to `INTERRUPTIBLE_ANIMATIONS` to allow mid-loop interrupts. |
| **Yawning** | No — waits for loop end | Yes — add `'yawning'` to `INTERRUPTIBLE_ANIMATIONS` to allow mid-loop interrupts. |

The `INTERRUPTIBLE_ANIMATIONS` set is a `static readonly` constant in `ChatComponent`, located near the other avatar constants (`AVATAR_LOOP_DURATIONS`, `AVATAR_SRCS`). To change interruptibility, simply add or remove animation names from this set.

**Loop-boundary scheduling mechanism:**
- When a non-interruptible animation is playing, `requestAvatarTransition()` calculates the time remaining until the current loop ends: `timeUntilLoopEnd = loopDuration - (elapsed % loopDuration)`.
- It schedules a `setTimeout` for `timeUntilLoopEnd` milliseconds. When the timer fires, the latest `pendingAvatarState` is applied.
- If multiple state change requests arrive while a timer is already pending, only the `pendingAvatarState` value is updated — the timer is not rescheduled. This ensures the swap happens at the original loop boundary with the most up-to-date desired state.
- The `done` event handler has its own special loop-boundary wait (`executeDone` callback) that defers the final transition to idle and the commit of the message to `this.messages` until the current animation loop finishes gracefully.

## Chat Message Handling & Clipboard Specifications

Refer to the dedicated [`overseer_chat_message_handling`](file:///c:/hmp/MobileGnollHackLogger/.agents/skills/overseer_chat_message_handling/SKILL.md) skill for the full specification. Key rules include:

- **Thinking Blocks (`<div class="ai-thought">`)**: Thinking tokens and pre-tool reasoning are wrapped in `<div class="ai-thought">...</div>`.
- **Clipboard Copying (`.copy-btn`)**: When copying messages to the clipboard, all `<div class="ai-thought">` blocks and enclosed reasoning content MUST be stripped using `ChatComponent.stripThoughts()`, regardless of user visibility settings. Code block formatting inside the response is preserved.
- **Tool Result Copying (`.tool-copy-btn`)**: Individual tool call outputs are copied separately via their dedicated copy button, copying only `tc.result`.

## Conversation Loading & Exclusivity Lifecycle

When switching between chat sessions or loading a conversation in `ChatComponent`, adhere strictly to these rules:

1. **Mutual Exclusivity**: The "Loading conversation..." spinner and text (`isLoadingSession`) MUST NEVER be visible simultaneously with chat messages (`messages`), streaming bubbles, tool calls, handoff overlays (`isHandoffWaiting`), or settings warnings.
2. **Never in the Middle of an Active Chat**: The conversation loading state (`isLoadingSession`) is an exclusive full-panel state used ONLY during explicit user navigation between distinct sessions or initial page load. It must NEVER be triggered in the middle of an active conversation, during AI generation/streaming, or while waiting for a response.
3. **No Aggressive Watchdogs During Streaming**: Do NOT use aggressive client-side polling or timeout watchdogs during active streaming that call destructive session reloads (`loadSession()`). Complex AI reasoning, thinking tokens, and tool executions regularly exceed 10–30 seconds.
4. **Non-Destructive Background Sync**: When reconnecting to SignalR or synchronizing session state in the background for the current chat session, always use silent in-place synchronization (`syncSessionSilently`). Never set `isLoadingSession = true`, never clear `messages`, never reset avatar state, and never display the loading spinner during background re-syncs.
5. **Immediate State Reset on Navigation**: Only upon initiating a genuine session switch (`loadSession(id)` where `id !== currentSessionId`), the component purges previous session messages (`this.messages = [];`) and active streaming state (`this.clearStreamingState()`), and sets `this.isLoadingSession = true;`.
6. **Template Enforcement**: In `chat.component.html`, the message area must use structural conditional branches (`@if (isLoadingSession) { ... } @else { ... }`) to guarantee that message containers and loaders cannot coexist in the DOM.
7. **Loader Styling**: The conversation loader must use a semantic SCSS class (`.conversation-loader`) centered within the `.messages` container, with no ad-hoc inline styles.
8. **Scroll Position on Load Completion**: When a session finishes loading, the chat view must automatically scroll to the bottom of the conversation (`scrollToBottomClamped(false)`), ensuring that the user is positioned at the newest messages and not stuck at scroll position 0 (top).

## AI Model Form Property Preservation & Provider Lifecycle

When configuring or editing AI models in `AiModelFormComponent` (used across `/models` and `/admin` config dialogs), the form distinguishes between **Add Mode** and **Edit Mode**:

### 1. Add Mode vs. Edit Mode Behavior
- **Add Mode (`mode === 'add'`)**: Selecting a model from the dropdown automatically populates all property fields with the model's defaults (e.g. recommended or medium thinking level, default reasoning mode/summary, and maximum input/output token limits).
- **Edit Mode (`mode === 'edit'`)**: When modifying an existing model within the same provider, all existing property values configured by the user are preserved unless they are invalid or unsupported by the newly selected model.

### 2. Property Retention Rules (Same Provider in Edit Mode)
- **Effective Value Resolution**: The component resolves effective property values by evaluating both standard dropdown selections and custom text input fields.
- **Thinking Level, Reasoning Mode, Reasoning Summary, Service Tier**:
  - If the existing configured value is supported by the new model (or is empty / Default), it remains unchanged.
  - If the existing value is unsupported by the new model, it gracefully falls back to the new model's recommended default (e.g., `recommendedThinkingLevel` or `'medium'`); if the feature is entirely unsupported by the new model, the property resets to `''`.
- **Token Limits (`maxInputTokens` / `maxOutputTokens`)**:
  - User-configured limits are preserved as long as they do not exceed the new model's maximum capacity.
  - If a configured token limit exceeds the new model's maximum limit, it is clamped to that maximum limit.
  - If the limit was previously unconfigured (`null`), it remains unconfigured.
- **Switching to "Custom..." Model**:
  - If the user switches the model dropdown to "Custom...", all current property values are preserved by setting the picker dropdowns to `'custom'` and placing the retained values into the corresponding custom text inputs.

### 3. Provider Immutability in Edit Mode
- **Provider Immutability**: In Edit Mode (`mode === 'edit'`), the Provider dropdown is disabled (`[disabled]="mode === 'edit'"`) and programmatic provider changes via `onProviderChange()` are ignored. The AI Provider can only be chosen during creation in Add Mode (`mode === 'add'`).
- Because the provider cannot change during edits, property preservation always operates within models of the same provider.

## AI Benchmark Configuration Persistence

In the AI Benchmark tab (`/admin` -> AI Benchmark), the settings in the **Configure & Execute Benchmark** card (`.setup-card`) must be remembered across page reloads and tab navigations using `localStorage` under the key `'overseer_admin_benchmark_run_settings'`.

### 1. Stored Setting Fields (`BenchmarkRunSettings`)
Whenever modifying or extending the benchmark setup form, ensure the following fields are preserved in `BenchmarkRunSettings`:
- **`suiteId`**: Selected benchmark question suite ID.
- **`scoringProfileId`**: Selected scoring profile ID.
- **`testedConfigId`**: Target/candidate model configuration ID.
- **`assessorConfigId`**: Evaluator/assessor model configuration ID.
- **`secondOpinionConfigId`**: Second opinion model configuration ID (or `null`).
- **`secondOpinionMode`**: Explicit second opinion mode override (or `null` to follow the profile default).
- **`claimVerifierConfigId`**: Claim verifier model configuration ID (or `null`).
- **`verboseMode`**: Candidate response style (`false` for concise / production default, `true` for detailed / diagnostic).
- **`runCount`**: Number of runs (`1` for a single run, or `≥ 2` for a replicate multi-run series).

### 2. Persistence Lifecycle & Invariants
- **Persisted on Execution**: Settings are saved via `persistRunSettings()` when the operator initiates a run or multi-run series (`startBenchmark()`), capturing the exact configuration that was dispatched.
- **Immediate vs. List-Backed Restorations**:
  - `restoreRunSettings()` reads from `localStorage` during `ngOnInit()`.
  - Fields not backed by asynchronous lists (`verboseMode`, `runCount`) restore immediately.
  - Number of runs (`runCount`) must be validated to be a positive integer (`≥ 1`, floored). It is clamped against `maxRunCountPerSeries` both upon restoration (if limits are already available) and in `loadRunLimits()` when the server limits response arrives.
  - List-backed fields (`suiteId`, `scoringProfileId`, `testedConfigId`, `assessorConfigId`, etc.) are validated against their asynchronously loaded datasets before being applied. If a saved ID no longer exists or a configuration is disabled or lost its `Benchmark` role, it must fall back gracefully to the default rather than leaving a dangling ID.
- **Safety Acknowledgments Excluded**: Transient safety gates (such as `acknowledgeSameProvider`) must NEVER be persisted across sessions, ensuring the warning dialog cannot be silently bypassed.

## Angular Unit Testing

Always execute unit tests before completing frontend modifications in Overseer. Refer to [`testing_guidelines`](file:///c:/hmp/MobileGnollHackLogger/.agents/skills/testing_guidelines/SKILL.md) for full instructions.

- **Run Single-Run Headless Suite (Preferred)**:
  ```bash
  npm run test:headless
  ```
  *(Or: `npx ng test --no-watch --browsers=ChromeHeadless`)*
- **Single Test File (Headless)**:
  ```bash
  npx ng test --include="src/app/chat/chat.component.spec.ts" --no-watch --browsers=ChromeHeadless
  ```
- **Build Verification**:
  ```bash
  npm run build
  ```



