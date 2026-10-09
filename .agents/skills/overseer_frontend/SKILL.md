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
- **`.gh-field-error`** is the inline error line for a field or an action: a leading 14 px Feather
  *alert-circle* SVG (`aria-hidden`), then the text, in `--color-error-text` (a lighter red that
  clears 4.5:1 on the dialog surfaces, where `--color-error` does not). Use it for a refusal under a
  control rather than `form-hint text-danger`; the icon, the word and the color all carry the message.
- Shared since 2026-09-29, and not to be copied back into a component: **`.run-stage-rail`** (the
  stage list of the run, multi-run, battery and AI report writing progress dialogs; `.is-done` /
  `.is-current`, plus `.is-skipped` (muted, dashed ring) and `.is-ended` (`--color-warning`) for a
  finished run's stages, a `.run-stage-note` under the `.run-stage-label`, and a visually hidden state
  word) and the **`.dc-ring`** ring spinner (`.dc-ring-track`,
  `.dc-ring-arc`; the Download Center, the PDF viewer and the Chat Consistency wizard's step-1 runs
  loading state), which turns slowly (6 s, still arc) under reduced motion.
- **`.config-badge`** is the neutral configuration badge (service tier, custom endpoint, prompt
  options) beside `.thinking-badge`, `.reasoning-badge` and `.provider-badge`: a configuration fact,
  not a capability claim. It badges the requested service tier and the reader coverage in the run and
  multi-run progress rosters, and the run report's facts strip. It is global and is not copied into a
  component; the settings page's `.tier-badge` (the Tier 1–4 permission chips) is a different,
  component-local class. The key-figures images draw all four with the same colors (`BADGE_PALETTE`
  in `run-report-frame/key-figures-image.ts`); change both together.
- **The run-report frame rules are global** since 2026-10-03, because the single-run report
  (`#runDetailDialog`) and the Battery Run Report share them: `.score-card` (with `.score-label`,
  `.score-value`, `.score-subvalue`, `.score-headline`, `.score-note`), `.rr-identity` / `.rr-emblem`,
  `.rr-header-controls`, `.rr-run-facts-primary`, the `.rr-run-details*` disclosure, `.rr-tabs`,
  `.rr-tab-flag`, `.rr-panel` with `.rr-panel-narrow` (48 rem) and `.rr-panel-medium` (60 rem), and the
  `.rr-figures*` key-figures grid and actions. They are in `styles.scss`; do not copy them back into
  either component.
- **`.settings-dialog.model-form-dialog`** is the near-full-screen frame of every dialog hosting
  `app-ai-model-form` (Admin config, My Models add and edit): `min(96rem, 100dvw - 32px)` by
  `100dvh - 32px`, 8 px inset on a phone, a flex column in which only the form body scrolls. It
  is global; do not copy it into a component.
- Shared since 2026-10-07, and not to be copied back into a component:
  - **`gh-wizard*`** — the full-screen wizard frame (`.dialog-content.gh-wizard`,
    `.dialog-header.gh-wizard-header`, `.gh-wizard-alert`, `-tabbar`, `-steps`, `-meta`, `-body`,
    `-step`, `-nav`, `-position`, `-blocked`, `-busy`, `-nav-actions`), shared by the Model Comparison
    wizard and the Chat Consistency wizard.
  - **`gh-fig-*`** — the figure workspace: `.gh-fig-host` (the edge-to-edge step that holds it),
    `.gh-fig-workspace` (sidebar width `--gh-fig-sidebar-width`), `-resizer`, `-sidebar`,
    `-sidebar-tabs`, `-side-panel`, `-main`, `-bar`, `-tabs`, `-panels`, `-panel`, `-viewport`
    (`-side-panel` and `-panel` set `display: flex`, so each carries `&[hidden] { display: none; }`;
    without it a `[hidden]` panel stays visible),
    `.gh-fig-tile*` (a tile and its hover-or-focus action cluster), `.gh-fig-toolbar*` (the toolbar row
    and its zoom, figure and export groups), `.gh-fig-all-toolbar` and `.gh-fig-figure-select`, with
    the zoom controls `.gh-zoom-*` (`-label`, `-slider`, `-value`) and the read-out `.gh-range-value`,
    and the figure canvas `.gh-fig-canvas` (block display, so no inline baseline gap under the bitmap)
    with `.gh-fig-canvas.is-transparent-figure`, the preview backdrop behind a transparent figure as the
    canvas's own CSS background, never in a written file: the color is `--gh-fig-backdrop`, inherited
    from the scroller (each wizard's `figureBackdropStyle`), the checkerboard without it;
    shared by Model Comparison step 2 and the Chat Consistency Timeline step. Model Comparison keeps its
    `mc-` classes beside them, which its specs query.
  - **`bm-launcher*`** — the launcher page of a benchmark sub-tab whose task is a full-screen wizard
    (`.bm-launcher`, `-hero`, `-lead`, `-actions`, `-howto`, `-steps`, `-step-number`, `-library`),
    shared by the Model Comparison and Chat Consistency launchers.
  - **`bm-summary-card*`** (since 2026-10-08) — a launcher's full-width summary card, a row of the
    launcher grid between the hero and the library: `.bm-summary-card` with `-header`, `-eyebrow`,
    `-title`, `-meta` and `-actions` (a container query stacks the header below 36 rem), the facts list
    `.bm-summary-facts` with `.bm-summary-facts-note`, and the entries table `.bm-summary-table-wrap` /
    `.bm-summary-table`. Shared by the Model Comparison *Last comparison* card
    (`comparison-tab/comparison-summary-card/`) and the Chat Consistency *Current model* card
    (`chat-consistency-tab/current-model-card/`).
  - **`.cc-marker-tag`** (`.is-event`, `.is-annotation`, `.is-served`, which differ by border style as
    well as color) — the Chat Consistency marker pill, shared by the chart figure, the event list and the
    Runs and controls preview.
  - **`.gh-date-field*` and `.gh-calendar*`** — the shared date field `app-date-field`
    (`shared/date-field/`): `.gh-date-field` (the positioned wrapper), `.gh-date-field-input` (end
    padding for the button, tabular figures), `.gh-date-field-btn` (the 30 px calendar button's
    placement), and the glass calendar popover `.gh-calendar` with `-head`, `-title`, `-grid`, `-day`
    and `-foot`. The component's own SCSS holds only `:host` layout. Contract:
    [`frontend_ui_controls`](../frontend_ui_controls/SKILL.md) § 4h.

  `.mc-fig-sidebar label` and `.mc-fig-sidebar .checkbox-label` deliberately stay in
  `model-comparison.component.scss`: a global form would reach the labels inside the sidebar's child
  components, which style their own. Chat Consistency scopes its own the same way (`.cc-tl-sidebar` in
  `timeline-workspace.component.scss`).

### Typography
- **Type tokens** on `:root` in `styles.scss`: `--text-body` (0.875rem: running text and
  values), `--text-secondary` (0.8125rem: labels, metadata, hints), `--text-xs` (0.75rem: notes
  under a stage-rail item), `--leading-body` (1.5, unitless) and `--font-mono` (the monospace stack).
- **New text rules use `rem` or the tokens, never `px`.**
- **A container sets the body size once, and its children inherit it.** Do not restate the size
  on each child.
- **Terms and values in a `dl` share one size** and differ by color.
- **A tab's heading is `.gh-section-title`.**
- **Inline code uses `--font-mono`** under `font-size-adjust: from-font` on its container, so it
  matches the x-height of the text around it.
- The run report dialog (`#runDetailDialog`) is the first adopter; other screens move over when
  they are next touched.

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
  component, which is how the GnollBench tab silently lost its image buttons).
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
- **There is no `.gh-checkbox` class.** A label carrying it renders the browser's default checkbox; use `.checkbox-label`.
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
- `/admin` (`admin.component`): System administration (groups, configs, rate limits). Tabs, in
  order: Users, Groups, **API Keys** (`app-admin-api-keys`, the per-provider default keys), System
  Configs, Database, AI Telemetry, GnollBench, Developer Tools.
- `/debug-log` (`debug-log.component`): Developer debug logs.
- `/login` (`login.component`): Authentication entry point.

### Popups (`<dialog>` elements)
To find specific popups, look in the corresponding component's `.html` template:

- **Admin Component (`admin/`)** — `admin.component.html` is the page shell: the tab row, the toast and
  one tab panel per tab. Each tab is a component of its own (`users-tab/`, `groups-tab/`, `configs-tab/`,
  `database-tab/`, `telemetry-tab/`, `devtools-tab/`), and `AdminPageStore` (`admin-page.store.ts`) holds
  what the tabs share or keep across a tab switch. Dialogs, by the component that holds them:
  - `#manageGroupsDialog`: Manage Groups (`users-tab/`)
  - `#createGroupDialog`: Create Group (`groups-tab/`)
  - `#configDialog`: Config (`configs-tab/`) — near full screen (`settings-dialog model-form-dialog`);
    only the body of its `app-ai-model-form` scrolls.
  - `#config-filter-panel`: Config Filter (`popover="auto"`, anchored to `#config-filter-trigger`)
  - `#confirmDialog`: Confirm (`admin-dialogs/admin-confirm-dialog.component.html`; Groups, Database and
    the rate limits dialog each embed their own)
  - `#manageUserConfigsDialog`: Manage User Configs (`users-tab/`)
  - `#manageGroupConfigsDialog`: Manage Group Configs (`groups-tab/`)
  - `#editConfigOverrideDialog`: Edit Config Override (`admin-dialogs/admin-config-override-dialog.component.html`)
  - `#rateLimitsDialog`: Rate Limits (`admin-dialogs/admin-rate-limits-dialog.component.html`)
  - `#analyticsDialog`: Analytics (`configs-tab/`)
  - Not a dialog: the **Database** tab's report chart files (`database-tab/`). A stat box *Report Chart Files* after
    *Disk Attachments* reads *N documents · M files · X MB*, or *Not configured
    (Benchmark:ReportPack:ChartsDataLocation)*, and is absent when an older server sends no
    `reportChartsConfigured`. The maintenance card *Clear Report Chart Files*, after *Sweep Orphaned
    Disk Folders*, says the documents and their text are kept and their PDF and Word copies have no
    charts until *Update charts* in the Comparison Wizard adds them again; its **Clear Chart Files**
    (`btn-gh btn-danger`) is `aria-disabled` with a tooltip reason while a maintenance task runs, when
    chart storage is not configured, or when there are no chart files. It runs through
    `runGranularMaintenance` like the other cards, so the tab's Dry Run switch applies and a real run
    asks *Delete N chart files (X MB) for M documents? …* first; Maintenance History shows the trigger
    `Manual:ClearReportCharts` verbatim. See `docs/overseer/chat-data-retention.md`.

- **Admin API Keys Component (`admin/admin-api-keys/admin-api-keys.component.html`, Admin → API Keys)**
  - `#deleteDefaultKeyDialog`: *Delete the default <Provider> key?* — the count and names of the
    system AI configurations that use it (from `deletion-check`, fetched on open), which a delete
    disables; **Delete Key** (`.btn-gh btn-gh-delete`, trash).
  - `app-key-verification-dialog` (below), opened by a 409 on **Verify and Save**.

- **API Keys Component (`api-keys.component.html`)**
  - `#apiKeyInfoDialog`: API Key Info
  - `app-key-verification-dialog` (below), opened by a 409 on **Save Key**.

- **Key Verification Dialog (`shared/key-verification/key-verification-dialog.component.html`)** —
  *Could not verify the key*, shared by the admin API Keys tab and the user API Keys page. Opened with
  `open(provider, refusal)` for a 409 `unverifiable` refusal: one sentence, *What failed* as a `dl` of
  the detail parts present (*Check*, *Response*, *Error*, *Provider's message*, *Time*), and **Cancel** /
  **Save Anyway**, which emits `(saveAnyway)`; the host resends with `saveUnverified: true` and the
  dialog shows *Saving…* until the host closes it. It stops its own `close` and `cancel` events. The
  types and `readApiKeyRefusal()` are in `shared/key-verification/key-verification.ts`. A saved key
  carries a *Verified* (`status-badge badge-success`) or *Not verified* (`status-badge badge-warning`)
  label with an `interestfor` tooltip (*Checked <date>* and the stored message), no label while never
  checked, and **Verify Again** beside *Not verified*.

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
  - Both are near full screen (`settings-dialog model-form-dialog`); only the body of their
    `app-ai-model-form` scrolls.
  - `#deleteModelConfirmDialog`: Delete Model Confirm

- **Settings Component (`settings.component.html`)**
  - `#confirmDialog`: Confirm
  - `#changelogDialog`: Changelog

- **Benchmark Component (`admin/benchmark/`, Admin → GnollBench)** — the tab opens directly
  with the benchmark tabs, with no brand row above them. `benchmark.component.html` is the shell: the
  sub-tab row, one tab panel per sub-tab, and the dialogs more than one sub-tab opens (the run report,
  the run progress, difficulty assessor and confirm dialogs, the snapshot viewer, the grader guide, the
  comparison wizard, the multi-run and battery progress dialogs, the Battery Run Report). The eight
  sub-tabs, in the order of `subTabs` in `benchmark.component.ts` (labels from `subTabLabels`), are
  **Run Benchmark** (`run`), **Run History** (`history`), **Multi-Run Analysis** (`multirun`),
  **Multi-Suite** (`multisuite`), **Manage Suites** (`suites`), **Scoring Profiles** (`profiles`),
  **Model Comparison** (`modelcomparison`) and **Chat Consistency** (`chatconsistency`, always last).
  The row is one `@for` over `subTabs` — an `@switch` supplies each tab's whole `<svg>` — inside
  `.gh-tabs.gh-tabs-secondary.gh-tabs-wrap`, so it **wraps onto further lines instead of scrolling**
  and every tab stays visible on a narrow screen; the order never changes and `onTabKeydown` stays
  linear (`frontend_ui_controls` § 5). The Run,
  History, Manage Suites, Scoring Profiles, Model Comparison and Chat Consistency sub-tabs are components of their own (`run-tab/`,
  `history-tab/`, `suites-tab/`, `profiles-tab/`, `comparison-tab/`, `chat-consistency-tab/`) holding their own dialogs; Manage
  Questions and Import Default Suites are `suites-tab/suite-questions-dialog.component.*` and
  `suites-tab/import-default-suites-dialog.component.*`. Their shared state is in `state/` (the
  workspace store, the launcher, the active-run monitor, the difficulty job, the comparison), the
  sub-tabs reach the shell's dialogs through `BenchmarkShellBridge`, and the pure run formatters are
  in `benchmark-run-format.ts`.
  - **The Chat Consistency tab** (`bm-panel-chatconsistency`, `chat-consistency-tab/`,
    `app-chat-consistency-tab`) is the client of `docs/overseer/ai-benchmark-chat-consistency.md`; its
    HTTP calls are all in `services/admin-chat-consistency.service.ts` (`AdminChatConsistencyService`,
    base `/api/admin/benchmark/chat-consistency`, errors through `ccErrorText`). The tab component
    owns the chosen model, the date range (`CcDateRange`), the step-1 run selection (`CcRunScope`), the
    timeline, the run rows, the anchor saves and the saved analyses, and performs the run actions the
    wizard asks for. **Reload runs** re-anchors a rolling date preset to now (`ccAnchorRange`) before
    loading; when rows arrive, `pruneScope` drops marks and left-out ids of runs no longer listed and the
    drop is announced. Choosing another model clears the selection silently; opening a saved analysis
    clears it and, when one was set, announces *The run selection in step 1 was cleared to show the
    saved analysis.* It is a **launcher page**
    (`section.bm-launcher.cc-launcher`) plus a **six-step wizard in a full-screen dialog**
    (`dialog.gh-dialog.gh-dialog-fullscreen.cc-wizard-dialog`, `showModal()`, no `closedby="any"`),
    modeled on the Model Comparison wizard. The state lives as long as the tab component, so a GnollBench
    sub-tab switch loses the run selection, the step and an analysis in progress (`ngOnDestroy` closes an
    open wizard); the model and its dates are remembered in this browser
    (`localStorage['overseer.benchmark.chatConsistency.subject']`, `{ version: 2, modelKey, range,
    compare: { kind, key } }`, `CC_SUBJECT_STORAGE_KEY`; a version-1 record is read as one with no
    compared set), written on every model, date or **Compare** change and restored once the model axes
    load with no model chosen — a rolling preset moved to now, a record whose model is unknown or has no
    runs removed. Step 1's **Compare** select (`GET comparison-sets`, groups *Batteries* and *Suites*)
    picks the battery or suite the analysis compares within; the server's `defaultKey` applies unless the
    stored key is still offered, and a change clears the selection with an announcement. In a battery set
    the cards, the selection band, step 3's span and step 4's rows are **battery runs**
    (`GET battery-runs`, `CcBatteryRunRow`, view stored in
    `overseer.benchmark.chatConsistency.batteryRuns.view`); `chat-consistency-scope.ts` keys the scope by
    unit id and unit kind, and `scopeKey` includes the set key. With no set the runs are not filtered and
    the request carries no `comparisonSet`. The parts, each in its own folder or file:
    - **The launcher** (`chat-consistency-tab.component.*`): the hero with the gold `.btn-gh` **Open Chat
      Consistency Wizard** (*compass*, `#cc-open-wizard`) and the non-exclusive
      *How chat consistency works* disclosure listing `CC_WIZARD_STEPS` under the wizard's own titles
      (open on the first visit, then as left, in `localStorage['overseer.benchmark.chatConsistency.launcher']`,
      `{ version: 1, howItWorksOpen }`); then, while a model is chosen, the *Current model* summary card
      (`current-model-card/`, `app-cc-current-model-card`, `.bm-summary-card`): the model with its
      thinking, provider and tier badges, the facts *Runs*, *Dates* (with *· N runs in these dates*
      unless *All dates*, a small spinner while the runs load), *In the analysis* (only while the
      selection narrows them), *First run*, *Latest run*, *Suites* and *Latest analysis*, and the
      `.btn-ghost` actions **Open latest run report** and **Open analysis #N**; its content comes from
      the server's model axes and saved analyses, and the wizard owns the choice. Below it
      `saved-analyses/` (`app-cc-saved-analyses`): a card
      list with *Open* and *Delete*; a delete the server refuses with 409 (report documents exist) shows
      its reason inside the dialog. **Open** fetches the analysis, switches the subject to its model,
      opens the wizard and calls `CcWizardComponent.showResult`, which mounts the analysis component
      before handing it the result and selects step 5.
    - `cc-wizard/` (`app-cc-wizard`): the whole dialog content in the global `gh-wizard*` frame —
      header (`#cc-wizard-title`, a subtitle *model · N runs · dates*, plus *· N in the analysis* while
      the step-1 selection narrows the runs, the icon-only **Reload runs**
      `.action-btn` with *rotate* on steps 1–2, `aria-disabled` while loading or without a model, and the
      close `.btn-icon-action`), the step tabs *1. Model · 2. Timeline · 3. Periods · 4. Runs and controls
      · 5. Results · 6. Reports* (`frontend_ui_controls` §5, a tab that cannot be opened is `aria-disabled`
      and described by its reason), the step panels, and the footer (*Previous*, *Step N of 6 — title*
      with the reason Next is blocked, *Next* / **Analyze** on step 4 / *Close* on step 6, and *Stop
      Analysis* while analyzing). Reachability: step 1 always; 2 and 3 a model; 4 valid periods and
      overrides; 5 and 6 a result. Every step is mounted on its first visit and then kept, hidden;
      `wizardMounted` on the tab is never reset, so reopening keeps everything. `closeBlocked` — a chart
      export running or the Reports step's `chartsAttaching` — disables the close button and Close and
      makes the tab refuse Escape (`onWizardCancel`). It also locks step 1 (`subjectLockedReason` →
      `lockedReason`: the picker's `disabled`, an `aria-disabled` *Dates* select whose `change` writes
      the stored preset back, `readonly` date fields whose calendar buttons are `aria-disabled` and open
      nothing, one reason line `#cc-tl-lock-reason` describing them all), binds `closedby="none"` on the dialog, and `onWizardClose` reopens a close that slips
      through (Chrome lets a second Escape's `cancel` through), restoring focus without reloading the saved
      analyses. The tab registers a leave guard on `BenchmarkShellBridge` (`setLeaveGuard`, returning
      `CC_LEAVE_REFUSAL` while blocked), which the run report's sub-tab-switching actions (*Repeat this
      run's setup*, *Re-run failed questions*, *Open run progress*) honor through `leaveRefusal()`,
      showing it in `.rr-status`. Closing reloads the saved analyses. *Repeat this run's setup* closes the
      wizard first; *Open run report* and the Download Center are shell dialogs opened after it, so they
      show above it.
    - `model-step/` (`app-cc-model-step`, step 1): the model picker (field capped at 32 rem), the
      **Dates** select `#cc-tl-range` (`CC_RANGE_PRESETS`: *All dates*, *Last 1 day* … *Last 180 days*,
      *Last year*, *Custom*; a rolling preset is emitted anchored at now, with the hint `#cc-tl-range-hint`
      *Since 2026-09-30 14:05 UTC · Reload runs moves it to now*) and, while *Custom*, **From (UTC)** /
      **To (UTC)** as `app-date-field` (`cc-tl-from` / `cc-tl-to`, each bounding the other's calendar by
      `max` / `min`, prefilled by `ccPresetToCustom`, validated into `#cc-tl-range-error`), on one subgrid
      row of labels, controls and hints from 44 rem (*Model · Dates · From · To*). Then the runs as
      **cards** (`frontend_ui_controls` § 8h, the fourth card list): the head `h5#cc-tl-runs-title` *Runs
      of {model}* with the polite `#cc-runs-status`; while the runs load for the first time,
      `.cc-runs-loading` (a `.dc-ring` and *Loading the runs of {model}…*, `aria-hidden`, revealed after
      0.3 s, the status line announcing the same text) stands in for the list, and on a reload the cards
      stay, `.cc-run-cards.is-refreshing` with `aria-busy`, and `.cc-runs-updating` (*Updating…*) shows
      beside the heading; the **selection band** (§ 8i) `role="region"` over
      `#cc-scope-label` (`role="status"`, `tabindex="-1"`: *Runs in the analysis — 15 of 19 runs in these
      dates · from #21 (2026-09-20) to #93 (2026-10-05) · 2 left out*, or *… — all 19 runs in these
      dates*), **Clear selection (N)**, the chips *First: #21*, *Last: #93* and up to six *Left out: #45*
      (each `.action-btn` remove with a hint tooltip; *+N more* sets the *In the analysis* facet to *Left
      out*), the hint *The filters below change what is shown, not what is analyzed. The saved analysis
      records this selection.*, and the amber *No run is left in the analysis. Check at least one run.*;
      then the filter bar (a `CardListState` over a `TableState<CcRunRow>`, `idPrefix: 'cc-runs'`, batch
      10, Sort by *Newest first* / *Oldest first* / *Suite (A–Z)* / *Harness* stored in
      `overseer.benchmark.chatConsistency.runs.view`, facets *Suite*, *Harness*, *Telemetry*, *In the
      analysis*, *Eligibility*, `memoDeps` carrying the scope) and `ul.cc-run-cards`. Each
      `article.cc-run-card[data-run-id][data-inclusion]` is a grid of *select* (the checkbox
      `#cc-run-{id}-include`, *Include run #{id} in the analysis*; for a run before the first or after the
      last mark it is unchecked, `aria-disabled`, refuses the click, and is described by *Before the first
      run (#21)* / *After the last run (#93)* — also when the run is left out, whose id stays in the
      scope), *head* (kicker `#id` · status · *Harness N* · *Legacy* / *Recorded* · *Anchor* · *First
      run* / *Last run* (gold) · *Left out* (dashed); the title `h6#cc-run-{id}-title[tabindex=-1]`
      holding the checkbox's `<label>` with the suite name; the `<time>` and *Served*), *actions*
      (`role="group"`: the **First run** / **Last run** `.btn-ghost` toggles with `aria-pressed`, a
      leading check glyph when pressed; *Open run report*, *eye*; *More actions*, a § 4f
      `.gh-action-popover` with *Repeat this run's setup* and *Mark as anchor* / *Unmark anchor*,
      `aria-disabled` with *Saving the anchor…* while busy), *elig* and *facts* (`dl.cc-run-facts`:
      *Segment*, *Telemetry*, *Re-grade*, *Matched controls*); the actions go under the head below 48 rem
      of the list and everything into one column below 30 rem (container `cc-run-cards`). Handlers call
      the `chat-consistency-scope.ts` helpers and emit `scopeChange`; a helper's note (*Run #40 is after
      the last run, so the last run was cleared.*) goes to the polite `.cc-scope-note`. The search and the
      facets never change the emitted scope.
    - `chat-consistency-range.ts`: `CcDateRange` (`preset`, `fromDay`, `toDay`, `anchorUtc`),
      `CC_RANGE_PRESETS`, `CC_ALL_DATES`, `ccRangeBounds` (a rolling preset is the anchor minus N × 24 h,
      `1y` one calendar year, with an open end; `custom` inclusive UTC days), `ccAnchorRange`,
      `ccDateRangeText` (*All dates*, *Last 7 days*, *2026-09-01 to 2026-10-05*, *From …*, *Until …*: the
      one text the launcher, the subtitle, the Timeline read-out and the recorded `rangeLabel` share) and
      `ccPresetToCustom`. Pure.
    - `chat-consistency-scope.ts`: `CcRunScope` (`firstRunId`, `lastRunId`, `leftOut`), `CC_EMPTY_SCOPE`,
      `runInclusion` (`included`, `leftOut`, `beforeSpan`, `afterSpan`; the span is tested first),
      `scopeRuns` (oldest first), `notAnalyzedRuns`, `setFirstRun` / `setLastRun` (a mark past the other
      clears it, with a note; the run holding the mark clears it), `toggleLeftOut`, `pruneScope`,
      `scopeSpanDays`, `scopeKey`, `scopeIsDefault`, `scopeChangeCount`, `CC_INCLUSION_TEXT`. Order is by
      `startedAtUtc`, then run id, never the card sort; every change returns a new object, and components
      compare scopes by identity. `cc-wizard` memoizes `scopedRows`, `notAnalyzed`, `analysisSpan` and
      `scopeKeyValue` on the rows and scope objects. Pure.
    - `timeline-workspace/` (`app-cc-timeline-workspace`, step 2, in a `.gh-fig-host` step): the global
      `gh-fig-*` workspace — a sidebar (`#cc-tl-sidebar`, 18–40 rem, at most half the workspace, 26 rem
      by default, `app-pane-resizer`, width as `--gh-fig-sidebar-width`; collapsed by the view bar's
      toggle) with the six tabs of `CC_TIMELINE_SIDEBAR_TABS`: **Data** (*Plot by* in a battery set,
      charts, series, *Value axes* — *Show the full 0–100 Intelligence scale* `#cc-tl-zero-baseline`,
      stored as `zeroBaseline` — and *Runs* — **Mark runs not in the analysis**
      `#cc-tl-mark-not-analyzed`, on by default), **Events** (marker kinds, Overseer change kinds with
      composite counts, and `app-cc-event-list`), **Annotations** (`annotations/`,
      `app-cc-annotations-panel`: add and delete only; there is no edit), **Theme**
      (`app-figure-style-panel kind="appearance"`), **Charts** (`app-figure-style-panel kind="timeline"`
      — *Heading and badges*, *Lines and points*, *Values and axes*, *Markers and legend*, *Notes*,
      *Footer*, no *Number format* section — then the workspace's own *Number format* fieldset
      `.cc-tl-decimals`: **Decimal places**, one select per value chart, `#cc-tl-decimals-<key>`,
      *Automatic (n)* from `ccAutoDecimalsText` and the choices of `CC_DECIMAL_CHOICES`, reaching the
      figure builders as `CcChartOptions.decimals` for point labels, takeaway and table but never the
      axis ticks, and *All automatic*; Results and the report charts pass none), both panels with
      `idPrefix="cc-style"` and `[openStorageKey]="figureStylePanelOpenKey"`, and **Download** (settings
      only: `app-export-size-section` *Chart size*, id prefix `cc-export`; `app-export-format-section`,
      id prefix `cc-image-format`; a hint; the theme and the logo are the Theme tab's); and the views
      **All charts** (one column of `app-cc-chart-figure` tiles, each tile's Copy / Download / Open in
      Single view projected into the figure's footer as `[ccFigureActions]` `.cc-tl-tile-actions`, always
      shown; toolbar zoom, **Fit width**, **Fit to screen**, **Download all**) and **Single chart**
      (Previous / select / Next, zoom, **Fit to screen**, **Actual size**, Copy, Download); both views
      open at Fit to screen. **Every chart is a bitmap composed by `cc-figure-compose.ts`**, on screen
      exactly as in Copy, Download and Download all, with **no hover tooltip**: `buildComposedCcFigure`
      builds the chart with `ccComposedChartOptions` (`ccChartThemeFor` of the resolved appearance,
      whose `tooltip: null` gives the chart `events: []` and a disabled tooltip; the `timeline` family as
      `CcChartStyle`; no header band, no logo, no animation), `ccFigureChrome` gives Model Comparison's
      heading (the title; the badges `model`, `runs` — `plural(count, unitNoun)` over the plotted
      points — and `dates`, step 1's range text, less `hiddenBadges`; the Better badge from
      `CC_FIGURE_DIRECTIONS` on quality, ttfat, rate, cost and reliability, none on work, tools and the
      overview; the notes `ccMarkerNoteText` and `ccNotAnalyzedNoteText` behind `markerNote` and
      `notAnalyzedNote`), `ccFigureFooter` the footer (`label` *Battery* or *Suite* with the set's name,
      else *Suites* and *All suites*; `computedAt` from `loadedAt`, when the workspace received the
      timeline), `ccFigureLayout` the file's layout or its refusal through `resolveFigureLayout`, and
      `composeCcFigure` renders the plot with `renderPlotOffscreen` and frames it with
      `composeFigureImage`. The screen passes `ccPreviewLayout` of that layout (`previewLayoutFor`,
      which changes only the density and the pixel size), so the screen is the download. Only tiles
      within one viewport height of the All view are composed (`isNear`); `invalidateImages` bumps
      `composeVersion` and composes once changes have been quiet for `COMPOSE_DEBOUNCE_MS` (150 ms), a
      pass of an older version is abandoned, every pass awaits `ensureFigureFont` and the logo first,
      and the last image stays shown, scaled to the new box, until its replacement arrives.
      `app-cc-chart-figure` takes the image as `composed` (`CcComposedFigure`): the bitmap copied into
      `canvas.gh-fig-canvas.cc-chart-image` (`role="img"`, `cursor: default`, named by the alt text
      plus `ccFigureSummary`; `is-transparent-figure` on a transparent background, the scroller passing
      `--gh-fig-backdrop` as `figureBackdropStyle`), a `.cc-chart-pending` spinner in a box of the
      reserved size before the first image, or `.cc-figure-refusal` in its place for a refused size; its
      footer row keeps *Show events* and the projected actions, without the marker pills the live chart
      shows. Without `composed` it draws the live `BaseChartDirective` chart with its tooltip (Results).
      The figure puts the takeaway under the image, then the footer, and *Show data* as a single bordered
      list of rows, its summary counting them (`ccDataCards`, headings at `dataHeadingLevel`, 5 under
      the step's `h4`, 6 by default; a column in `CcFigureTable.lists`, *Member runs*, shows one item per
      line as `.cc-data-list`, the string cell still holding them joined). Zoom is `cc-chart-zoom.ts`,
      on Model Comparison's `preview-view.ts`: a zoom is **device pixels per file pixel**, 1 showing one
      pixel of the file (`ccTargetPixels`) on one device pixel (`ccPreviewDpr`, 1–4); the range is
      `ccZoomRange` (`previewZoomRange`: 10 %, lower where a fit is, to 800 %), and the slider applies
      once per frame. **A fit is whole CSS px**: `ccFitWidthZoom` / `ccFitHeightZoom` /
      `ccFitScreenZoom` leave room for the figure's HTML (`CcFigureChrome`: the figure's box less the
      image's, rounded up, an open *Show data* body left out), `ccDisplaySize` floors a fitted box, the
      viewport is its border box less borders and padding, floored (`previewStageContentBox`),
      `.cc-tl-viewport` reserves no scrollbar gutter, and `ccFitWidthWithScrollbar` fits against a
      narrower width only where the fitted figure will be taller than the view; with more than one tile
      the All column keeps its scrollbar and every fit leaves it room, so Fit to screen fits one whole
      tile. Keys on a view panel, outside form fields and without Ctrl / ⌘ / Alt: `+` / `=`, `-`, `0`
      (Fit to screen in both views) and `1` (100 %, Single only). `cc-chart-export.ts` holds only the
      file names (`ccChartFilename`, `ccChartArchiveFilename`); a file is `composeCcFigure` at
      `ccFigureLayout` from one export snapshot, then Model Comparison's `encodeFigureImage`,
      `copyImageToClipboard` and `buildFigureArchive`: Download in the chosen PNG or WebP, Copy always
      PNG, Download all one file or a ZIP. `exporting` is emitted as `exportingChange` for the close
      guard. Stored per browser in `try/catch`: `overseer.benchmark.chatConsistency.timeline`
      (`{ version: 2, … }`: sidebar, view, charts, series, markers, hidden event kinds, zero baseline,
      `decimals`, `markNotAnalyzed`, format, WebP quality, section open states, Single chart, and no
      `imageTheme` or `logo`; `parseTimelineLayout` migrates a version-1 layout's *Work per answer*
      chart to `work` and `tools`, reads a missing or non-boolean `markNotAnalyzed` as `true` and keeps
      only the `decimals` each chart offers, so neither of those changed the record version);
      `overseer.benchmark.chatConsistency.figureStyle` (`CC_FIGURE_STYLE_STORAGE_KEY`,
      `{ version: 1, appearance, timeline }`, repaired by `normalizeFigureStyle`, apart from Model
      Comparison's `overseer.modelComparison.figureStyle`; where none is stored,
      `readStoredTimelineFigureStyle` seeds it once from the layout record's legacy fields,
      `imageTheme: 'print'` as `appearance.theme = 'light'` and `logo: false` as
      `appearance.logo = false`); `overseer.benchmark.chatConsistency.figureStylePanel.open`
      (`CC_FIGURE_STYLE_PANEL_OPEN_KEY`, the panel's open sections, apart from Model Comparison's
      `overseer.figureStylePanel.open`); and `overseer.benchmark.chatConsistency.chartSize`, apart
      from Model Comparison's `figureSize`. The workspace draws **every run in the step-1 dates** (the composite events would otherwise renumber);
      its `notAnalyzed` input (the wizard's `notAnalyzedRuns`) reaches `CcFigureInput.notAnalyzed` as
      `CC_INCLUSION_TEXT` reasons while the switch is on, for the screen and the exports alike, and
      `chat-consistency-charts.ts` draws those runs as gray `crossRot` points with gray `[2, 3]` dotted
      segments (scriptable options only when the map is non-empty, so charts without one are unchanged),
      keeps each series' own symbol in the legend, adds the caption sentence *N runs not in the analysis
      are drawn as gray crosses.* (runs with a value in any of the figure's series; a composed image
      repeats it as a note while `notAnalyzedNote` is on), the tooltip line *Not in the analysis: reason* (only a theme with a tooltip box draws
      it, so never the Timeline, whose data cards carry the reason) and the table column *In the
      analysis*. The report charts pass no map. Its `rangeLabel` input gives the read-out *model · Last
      30 days · change in step 1* and the charts' *dates* badge.
    - `chat-consistency-events.ts`: `groupOverseerEvents` groups the timeline's Overseer events into
      **composite events** — one per UTC day and harness version, tagged `E1`… in time order — for the
      chart markers, the event list, the Runs and controls preview and the *Before vs after an Overseer
      change* preset; presentation only, the server's analysis is unchanged. It also builds the
      served-model changes (`S1`…), the tagged annotations (`A1`…) and the day list `buildEventDays`; a
      filter drops items, never renumbers them. `event-list/` (`app-cc-event-list`) renders that list,
      sticky day headings, chips per kind and a *Details* disclosure per composite; it is in the
      Timeline's Events tab and on Results.
    - `analysis-wizard/` (`app-cc-analysis-wizard`, steps 3–6): one component shared by the four
      steps, its `step` input chosen by the wizard, which draws the step bar, headings and footer. Step 3
      offers the presets *Launch vs last 14 days*, *Before vs after an annotation*, *Before vs after an
      Overseer change* (a select of composite events), *Confirm on later data* and *Custom dates*, shows
      Protocol V1 and a *Override the protocol* disclosure; its four dates are `app-date-field`s
      (`cc-wiz-bs`, `cc-wiz-be`, `cc-wiz-cs`, `cc-wiz-ce`) emitting into `onDayInput(field, value)`. Its
      inputs: `rows` is the **scoped** list (the runs in the analysis), `allRows` every run in the dates,
      `scope`, `range`, `span` (`scopeSpanDays` of the scoped rows) and `scopeKey`. The presets span
      `seriesDays(axis, span)` — the span when given, else the subject's first and last run — and the note
      `.cc-wiz-span-note` says so (*Presets use the runs chosen in step 1: #21 (2026-09-20) to #93
      (2026-10-05), 15 runs.* / *Presets use every run in the dates: …*); a span change re-applies a
      preset other than *Custom dates*, and `periodKey` carries `scopeKey`, so a new selection preselects
      the runs again. Step 4 chooses runs and controls, holds
      `regrade-panel.component.*` (estimate dialog first, `confirmed: true` only from its Re-grade
      button) and the relaxed-pooling checkbox, previews strata, composite events, missing controls and
      *Left out in step 1: #45, #51* (left-out runs of `allRows` inside either period), and posts the
      analysis from the footer's Analyze with `runSelection` (`rangeLabel` = `ccDateRangeText`, the
      `ccRangeBounds`, the marks and the left-out ids ascending), always sent, the default selection
      included; step 5 is `results-view.component.*` (one column of charts, then *Events in the analyzed
      span* as an `app-cc-event-list`, then the **Run selection** section before *Limitations* while
      `runSelection.recorded` or it lists unanalyzed runs: a `dl` of *Dates* (the label and its UTC
      bounds, an open one read as *the first run* / *the last run*), *First run*, *Last run* and *Left out
      in step 1* (*none* when empty), then *Not analyzed* by reason in `CC_UNANALYZED_REASONS` order, or
      *Every usable run of the model in the periods was analyzed.*; absent for a code-version-1
      analysis); step 6 is
      `reports-step.component.*` (the four Chat Consistency Report audiences, the Provider Issue Report
      enabled only when the estimate says it is available, a same-provider dialog asked on every write,
      then the charts drawn by `chat-consistency-report-charts.ts` and uploaded;
      `CC_REPORT_CHART_VERSION` 2 marks charts drawn with composite events).
    The Reports step opens the Download Center through the shell (`openDocuments` →
    `openChatConsistencyDownloads`) with the `chatConsistency` context.
  - **"Repeat this run's setup"** fills Run Benchmark from a stored run and **never starts anything**.
    It appears in the run report header (`#rr-repeat-setup-btn`), in a Chat Consistency run card's *More
    actions* and
    on the Results step's next-run suggestions. Each calls `BenchmarkShellBridge.repeatRunSetup(runId)`,
    which emits on `repeatRunSetup$`; the shell closes the run report, selects the `run` sub-tab, loads
    the run and hands it to `BenchmarkLauncherState.prefillFromRun(run)`. The prefill waits until the
    stored launcher settings have been restored and the suite, profile and configuration lists have
    arrived (`prefillReady`), so it is applied **after** the restore and overwrites it; it then
    persists the result as the remembered launcher settings. It sets the suite, scoring profile, Model
    Under Test, Assessor, the optional roles (cleared when the run had none), Coverage, Response Style
    and source code references, with `runCount = 1` and a suite target. A suite, profile or
    configuration that no longer exists or is no longer benchmark-capable keeps the current choice and
    adds a note (*… no longer exists, so the selected suite was kept.*, *… is no longer available for
    benchmark runs, so the current choice was kept.*); the `role="status"` block `.bm-prefill-status`
    above the launcher shows *Set up from run #N* with *Check the settings below, then press Start.
    Nothing starts on its own.* and the notes. Leaving the Run sub-tab clears it.
  - **The Model Comparison tab** (`bm-panel-modelcomparison`, `comparison-tab/`) is a one-column grid. The hero card
    `.mc-launcher-hero` holds the h3, the lead, then **Open Comparison Wizard** — the page's only
    `.btn-gh`, full size, *compass* glyph — then a non-exclusive
    `details.gh-disclosure.mc-launcher-howto` *How the comparison works* (the steps and the
    like-for-like note), open on the first visit and afterwards as left
    (`localStorage['overseer.benchmark.modelComparison.launcher']`, try/catch); its step list names
    the wizard's four steps. Below the hero, the *Last comparison* summary card
    (`comparison-tab/comparison-summary-card/`, `app-comparison-summary-card`, `.bm-summary-card`) shows
    the comparison last computed and numbered in this browser, read from
    `localStorage['overseer.benchmark.modelComparison.last']` (`comparison-tab/last-comparison.ts`, record
    version 1, at most 64 entries; every access in try/catch): *Comparison #N · <name>*, the facts
    *Charted*, *Pricing* and *Report documents* (counted from `listComparisons()`, refreshed on tab
    entry and after the wizard closes, omitted while unknown or failed), an entries table (*Model*,
    *Intelligence Index* with interval, *Median model time*, *TTFT P50*, *Candidate cost / question*,
    *Status*), and **Open in wizard** (`BenchmarkComparisonState.applyComparisonEntries`, then
    `openComparisonWizard()`). The wizard's `comparisonIdentified` output feeds
    `BenchmarkComparisonState.recordLastComparison` after `identify` answers, on a rename and on a
    recompute of the same entry set; with no record there is no card. Below it, `.mc-launcher-library`
    holds **Comparison reports**:
    `app-report-documents-launcher` (`report-pack/report-documents-launcher.component.*`,
    `idPrefix="mcl"`), a summary (*N report documents from M comparisons · the latest written …*, the
    comparisons counted by `comparisonId`, a document without one by the number another document of its
    comparison key carries, else by that key; or *No reports yet…*), an *N changed since written* `gh-tag gh-tag-changed` when subjects' or peers'
    runs changed, a click-mode info tip, and one `.btn-ghost` **Open Download Center**
    (*file-with-arrow*), `aria-disabled` while there is nothing to open, the summary line being its
    reason. It opens its own `app-benchmark-download-center` with a `library` context of every Report
    Pack document (`scope: { kind: 'all' }`, `preselect: 'none'`, title *Comparison reports*) and no
    chart actions, and counts again on the Download Center's `documentsChanged` and `closed` (focus
    returns to the button). The panel is inside `@if (activeSubTab === 'modelcomparison')`, so the
    summary loads when the tab is shown and again on every showing; the wizard's close bumps
    `comparisonReportsReloadToken`. The panel duplicates none of the wizard's controls.
  - **The Run History tab** (`#bm-panel-history`, `history-tab/`) is a card list (`frontend_ui_controls` §8h), one
    full-width `article.rh-card` per run in a `ul.rh-card-list[role=list]` labelled by the `h4.gh-section-title`
    *Runs* (`#rh-list-title`). The head is one centered row (`align-items: center`, the heading's text
    box trimmed to its caps). It holds a dialog-mode info tip (`rh-list-tip`, *About the run history*:
    the indexes, the cost pair, the five hashes and what *Instrument changed* / *Options changed*
    compare against), the
    polite status line `#rh-list-status` (*Showing 10 of 75 runs*, a battery run counting as one run,
    *· filtered from N*, *· Only the newest 1000 runs are loaded* when `RUN_HISTORY_LIMIT` came back,
    *· newest 500 battery runs* when `BATTERY_RUN_HISTORY_LIMIT` did, and *· Battery runs could not be
    loaded*), **Show battery member runs** (a `gh-filter-toggle` with `aria-pressed`, below) and
    **Refresh** (`.btn-ghost`, *rotate*).
    The filter bar copies the Download Center's markup with `rh-` ids: `#rh-search` (*Search runs*, over
    `#id`, suite, model name and id, provider, assessor, status, harness version and the five
    fingerprints; Escape with text clears it), `#rh-sort` **Sort by** (*Newest first*, *Oldest first*,
    the two index orders, the two cost orders, *Duration, shortest first*, *Tested model (A–Z)*, *Suite
    (A–Z)*; stored under `overseer.benchmark.runHistory.view`), the facets *Kind* (*Single run* /
    *Battery run*), *Suite*, *Tested model*, *Assessor*, *Status*, *Flags*, *Changes* and *Started*
    (single mode), each with `noun="runs"`, and the chips with **Clear all**; there is no selection and
    no sticky bar. The state is the workspace store's `historyList`, a `CardListState` over
    `historyTable` (`idPrefix: 'rh'`, `onChange` calling `BenchmarkViewSync.notify()`, since the
    Benchmark components are OnPush). **It runs over `historyItems`**, a memoized `HistoryItem` union
    (`{ kind: 'run', key, run }` | `{ kind: 'battery', key, battery }`, `benchmark.models.ts`) that
    `mergeHistoryItems` builds from `historyRuns` and `batteryRuns`, each battery run placed before the
    first run that started no later; `historyView` is `historyList.view(historyItems)`, and every
    `historyTable` accessor reads either kind. **`historyRuns` stays the server-order run list** for
    `instrumentChangeOf`, `completedRunsOfSelectedSuite` and the other helpers that depend on it — never
    read those from `historyItems`. `loadHistory()` fetches the newest `RUN_HISTORY_LIMIT` (1000) runs
    and, in parallel, the newest `BATTERY_RUN_HISTORY_LIMIT` (500) battery runs
    (`getBatteryRuns(undefined, 500)`; a failure leaves single runs only and sets `batteryRunsFailed`);
    the server-side suite select and `historySuiteFilter` are gone, replaced by the client-side *Suite*
    facet. A run card holds a
    kicker (`#N`, the status badge, the degraded count with a Feather *alert-triangle* SVG and visually
    hidden *degraded answers*, the progress count, `INSTRUMENT CHANGED` / `OPTIONS CHANGED`, parts
    separated by an `aria-hidden` dot and a visually hidden comma), the `h5.rh-card-title`
    (`#rh-run-{id}-title`, `tabindex="-1"`) with the model under test's `runFactBadges`
    (`.rh-card-model` is `1.125rem`/700, the card's headline), a meta line
    (suite · *assessed by* · `<time datetime>` · *by* user), `dl.rh-metrics` (*Intelligence*, *Speed*,
    *Duration*, *Cost*, fixed 7.5 rem columns behind an inline-start hairline, which moves to their
    block-start side below 60 rem; *advisory timing*, *until stopped*, *pricing incomplete*
    and *catalog …* as visible second lines, never `*` with `title`), the `role="group"` *Actions for
    run N* (**View details** *eye*, **Download Markdown report** *file-with-arrow*, **Download tool-call
    log**, and **Delete run** `.action-btn-danger` set apart) and, under a hairline, `dl.rh-instrument`
    (*PROMPT*, *GUIDES*, *KB*, *WIKI*, *SRC* with 8-character `fp-*` hashes and visually hidden long
    names; the full hashes in a click-mode info tip `rh-instr-{id}`, no `title`). The list is an
    inline-size container (`rh-cards`): below 60 rem the metrics move under the head, below 30 rem one
    column. Ten cards, then **Show 10 more** / **Show all N**, focusing the first new card's title; a
    removed chip moves focus to the next chip, else the previous, else `#rh-search`; after a delete,
    focus goes to the card now at the deleted one's index, else the previous, else `#rh-list-title`.
    *No benchmark runs or battery runs recorded yet…*, *No runs match these filters.* (with **Clear all
    filters**) and *Every loaded run is a member of a battery run…* are separate empty states.
    **Battery member runs are hidden by default**: `mergeHistoryItems` leaves out runs with a
    `batteryRunId` while `showBatteryMembers` is off. **Show battery member runs** toggles it, stored
    under `overseer.benchmark.runHistory.members` (`'1'` / `'0'`, try/catch) — a view setting, **not a
    chip and not a filter**, which **Clear all** keeps, as the Download Center keeps *Show selected
    only*. Shown, a member's kicker carries *Battery #id · suite s/K* (`.rh-battery-badge`).
    **A battery card** is `article.rh-card.rh-card-battery[data-battery-run-id]` in the same list and grid
    (`#rh-battery-{id}-title`): the kicker `.rh-battery-run-badge` *Battery run #N* (distinct from a
    member's badge), the status badge from `batteryStatusBadgeClass()` (`benchmark-run-format.ts`, onto
    the `badge-status-*` classes) with `batteryRunStatusLabel`, *k of K suites*, *R runs per suite* and
    *Analysis stale* (`gh-tag gh-tag-changed`) or *Not analyzed*; the model with `runFactBadges`; the
    battery name · revision · *assessed by* · `<time>` · *by* user; the metrics *Intelligence* (Overall
    Index ± half-width as a score badge, else *N/A* with *Incomplete (k of K suites)*), *Speed*,
    *Duration* (`batteryRunDurationMs`) and *Cost*; the group *Actions for battery run N* — **View
    details** (*eye*, `bridge.openBatteryRunReport(id)`), **Download Markdown report**
    (*file-with-arrow*, `aria-disabled` with its tooltip reason until analyzed), **Show progress**
    (*activity*, while live or resumable) and **Delete battery run** (`.action-btn-danger`, *trash*,
    `aria-disabled` while live); and a *DEF* / *CLASS* instrument strip with a click-mode info tip
    (`rh-binstr-{id}`). **Delete** opens `#rhDeleteBatteryDialog` (*Delete battery run #N?*): the
    analyses and AI documents go with it, a `.checkbox-label` **Also delete its M member runs**,
    unchecked by default, and **Delete** (`btn-gh btn-gh-delete`, *trash*) calling
    `deleteBatteryRun(id, deleteMembers)`; focus afterwards moves as after a run delete.
  - **The Multi-Suite tab** (`#bm-tab-multisuite` / `#bm-panel-multisuite`, `activeSubTab ===
    'multisuite'`) is the fourth tab, after *Multi-Run Analysis*; the tab row passes positional indexes
    to `onTabKeydown($event, n)`, so a tab inserted before the end moves the indexes and the spec's
    tab-order pins after it. The panel is `app-benchmark-batteries` (`batteries/`, `bb-` classes and ids)
    and holds the battery **definitions only** — battery runs are in Run History, and the analysis, the
    leaderboard and the paired test are dialogs (below); there is no *Battery Runs* table, *Analysis*
    panel or *Compare two results* section. The head is `h4.gh-section-title#bb-batteries-title`
    *Batteries* with a click-mode info tip, the polite `#bb-list-status.gh-list-status` (*Showing 3 of 3
    batteries*), **Show archived (n)** (a `gh-filter-toggle` with `aria-pressed`, shown while an archived
    battery exists; a view setting that **Clear all** keeps) and **New Battery** (`.btn-gh`, *plus*). A
    card list (`frontend_ui_controls` §8h) on `CardListState`: the filter bar shows above
    `BATTERY_FILTER_BAR_MIN_EXCLUSIVE` (3) batteries or while a filter is active — `#bb-search` (name,
    description, suite names), `#bb-sort` (*Recently modified*, *Name (A–Z)*, *Most runs*, *Most
    suites*; `overseer.benchmark.batteries.view`), the facets *Suite* and *Weighting* with
    `noun="batteries"`, chips and **Clear all** — and batches of 10. **One full-width
    `article.bb-card` per row** in the `bb-cards` inline-size container (`"head metrics actions" /
    "suites suites suites"`; metrics under the head below 60 rem, one column below 30 rem), so a 2-suite
    and a 12-suite battery never make a ragged grid. The kicker (`#id`, *Revision n* and the scheme as
    `.config-badge`, then `gh-tag`s *Archived*, *Running*, *Broken*, *Difficulties incomplete*, parts
    separated by `.bb-sep`), the `h5.bb-card-title` (`tabindex="-1"`), the description, a meta line
    (*Modified* `<time>` · *by* user · *definition* hash with an info tip), validation errors as
    `.gh-field-error`, `dl.bb-metrics` (*Suites*, *Questions*, *Runs*, *Ranked results* from the DTO's
    `rankedResultCount`), the `role="group"` *Actions for battery {name}* — `.btn-ghost` **Leaderboard**
    (*award*, first), **Edit** (*pencil*), **Archive** / **Restore**, **Delete** (`btn-ghost-danger`,
    *trash*, `aria-disabled` with a tooltip while a run is active; its own `bb-delete-dialog`) — and,
    under a hairline, the suites: an `aria-hidden` stacked weight bar `.bb-weight-mix` (run order, width
    = declared weight, alternating gold tints, a hatched gray segment for a deleted suite) over
    `table.gh-table.bb-suite-table` (*#*, *Suite*, *Questions*, *Weight*, *Status*) showing
    `BATTERY_CARD_SUITE_ROWS` (4) rows, the rest in a hidden `<tbody id="bb-suites-more-{id}">` behind a
    link-style **Show all K suites** / **Show fewer** (`aria-expanded`, `aria-controls`; focus stays on
    it). The tab hosts `app-battery-leaderboard-dialog`. Interfaces mirroring the server's analysis
    records, the scheme labels, sorts, facets, the weight-mix and interval-strip helpers,
    `batteryRunDiagnosticsText` and the client-side weight preview are in `batteries/battery.models.ts`.
    The launcher's battery mode, the battery banner (`.battery-banner`, holding the `.lost-contact-notice`
    of the monitor's back-off) and the documentation of the whole feature are in
    `docs/overseer/ai-benchmark-multi-suite.md`. The run tab's battery and series banners show progress
    only: **Show Battery Progress** / **Show Series Progress**, and **Cancel Battery** / **Cancel
    Series** while there is something to cancel; a stopped banner ends *"Continue from the progress
    dialog."* The battery banner shows while the battery run is live, `Stopped` or has post-run work
    (`batteryAwaitsPostRun`); a `CompletedWithErrors` battery run counts as finished and has none.

  Not an exhaustive list of the Benchmark dialogs, only the ones recorded here so far. Each names the
  component that holds it when that is not the shell:
  - `#runProgressDialog`: the run progress dialog, **full-screen** (`gh-dialog-fullscreen`, no
    component sizing of its own). Its content wrapper is an inline-size container (`run-progress`)
    and its body holds two sections: `section.run-progress-overview` (everything but the questions,
    in order, beginning with the model roster; it has no heading of its own) and
    `section.run-progress-questions` (`aria-labelledby="runProgressQuestionsTitle"`, `tabindex="0"`
    with a visible focus ring, then the question list). **The dialog title is the focus target**:
    `<h3 id="runProgressDialogTitle" #runProgressHeading tabindex="-1">`, also the dialog's accessible
    name, focused on open, with a `:focus-visible` ring; while the run detail loads, the subtitle reads
    *Starting benchmark run…*. The header's bottom margin is 0 and the body's top padding 16px, scoped
    to this dialog. The questions heading is `h4.gh-section-title` (the shared benchmark section
    heading in `styles.scss`: gold Cinzel capitals over a fine rule, also the Download Center's) with
    the count in a small neutral Lato pill (`.run-progress-questions-count`); its accessible name stays
    *Questions N*. Narrow is the default — one column, the body scrolls. From `60rem` of content width
    the body is a grid, `minmax(0, 3fr) minmax(22rem, 2fr)`, and each section scrolls on its own
    (`overscroll-behavior: contain`, `scrollbar-gutter: stable`). **Keep the overview a block
    container**: the cost panel relies on margin collapse. Keep exactly one polling live region. A
    scored row shows its published score (panel score in a panel run) before its chip as a tier badge
    of the chip's height and shape — `getQuestionScoreBadgeClass` gives `badge-score-high` (≥ 80),
    `badge-score-mid` (≥ 50) or `badge-score-low`, colored from the `--score-*` tokens on `:root` —
    named by a visually hidden *Score* word, not `aria-label`; the color repeats the number, never
    replaces it. **An Intelligence Index** (a member's, a battery's *Overall Index*) uses the shared
    `app-index-badge` (`shared/index-badge/`, standalone, OnPush; styles global in `styles.scss` beside
    `.score-badge`): inputs `value`, `halfWidth`, `size` (`'sm'` default, `'md'` for a stat tile) and
    `label` (default *Intelligence Index*); the tier class from `getScoreBadgeClass` (80 / 50) on the
    rounded value; the whole number in tabular figures and *± n* when a half-width is known; a ring
    (`conic-gradient` arc to `--index-fill`, hollowed by a `radial-gradient` mask) that is decorative and
    `aria-hidden`; and a visually hidden label (*Intelligence Index 85 ± 4, out of 100*). No `title`,
    tooltip or live region — the host announces, if anything. `@property --index-fill` lets the arc
    ease, with a `0s` transition unless `prefers-reduced-motion: no-preference` (then `0.4s`). The
    color repeats the number and never replaces it. The roster and banner say **Assessor** (never *Evaluator*); a panel run's banner says
    *Assessors*. The roster's badges carry the model picker's visually hidden prefixes and no `title`,
    and the coverage badge is followed by a hover `app-info-tip` (`runProgressCoverageTip`) holding
    the coverage hint. **When the run names a report writer**, the model strip gains a *Report writer* row,
    the rail a fourth stage *Writing reports* (stage labels read *of 4*; a run without a writer keeps
    *of 3*), the stat strip a *Reports* cell, and the cost panel a *Report writer* row and *Run total
    with reports*. The dialog keeps polling after *Completed* while the documents are *Pending* or
    *Writing* (a 30-s grace for *NotRequested*), and the completion chime fires at the end of stage 4,
    not at *Completed*. The Download Center, opened on such a run, shows a *being written* notice and
    polls the run's report job every 5 s until it finishes.
  - `#runDetailDialog`: the run report dialog, **full-screen**. The header's actions slot holds
    `div.rr-header-controls`: a `role="group"` *Run actions* (never `role="toolbar"`: it has no
    arrow-key roving) and, outside it, **Close** (`.rr-close`, a `.btn-icon-action` named *Close run
    details*: a dialog control, not a run action). The group's direct children are **Downloads** (opens
    the Download Center on the run's own documents), **Re-run** (an action popover,
    `frontend_ui_controls` §4f, `aria-label` *Re-run and repair*: *Re-run failed questions*, *Retry failed
    assessments*, *Retry claim verification*, *Re-run final synthesis*, *Re-score run*, in that order,
    each listed and gated by `admin/benchmark/run-repair-actions.ts` and an unavailable one
    `aria-disabled` with its reason — the rules are `frontend_ui_controls` §4g) with its popover, and the
    icon-only **View game snapshot** and **Copy diagnostics**. The group is a two-column grid —
    Downloads over Re-run, equal width and left-aligned, then snapshot over copy — with the `.rr-status`
    *Copied* line positioned under it so it takes no cell; below 40 rem of the `run-report-frame`
    container it is one row. **Repairs outside the popover:** the Summary tab's *Run did not complete
    cleanly* alert carries the one contextual **Re-run failed questions** (*refresh-cw*, same gate, its
    reason in a `.repair-reason` line it is described by), and each question's row **Re-run question**,
    **Re-assess question** (*refresh-cw*) and **Try another assessor (does not change the score)** (plain
    `.btn-ghost`, *flask*), each disabled one described by its reason line under the row. Their
    confirmation dialog is `aria-labelledby` its `<h3>`, its confirm button labelled as the item (the
    trial's title *Try another assessor*, its description *"Grades this answer with the model you choose
    and shows the verdict beside the panel's. The score does not change."*). The busy strip above the
    run report's panels reads *Retry in progress on this run…* only while a re-run of a Running run is
    under way (`rerunStartedAtUtc` set), else *Run in progress.* In the run progress dialog the re-run
    badge reads *Re-running 1 question* for a **Re-run question** (*Re-running N failed questions* for
    a failed-question re-run), its footer's **Re-run failed questions** is gated like the report's, and
    a row's *Being re-run* marker is visually hidden text. The decorative GnollBench emblem (`.gnollbench-emblem`, `alt=""`) stands before *Run #N*. Under the
    title the header lists the run's settings as `app-run-facts` (`run-report-frame/run-facts.*`,
    OnPush) built by `buildRunFacts` in `run-facts.ts`: *Model*, *Assessor(s)* (panel members tagged
    `A` / `B`), *Prompt*, *Scoring profile*, *Started* (`<time datetime>`) and *Board*, every model badged
    by `runFactBadges` with the model pickers' rules (plus service tier and *Custom endpoint* on the
    model under test, as `.config-badge`); no `title` attributes. **The primary rows**
    (`RUN_FACT_PRIMARY_KEYS`: *Model*, *Assessor(s)*) are always shown, in `.rr-run-facts-primary`
    (`selectedRunPrimaryFacts`); **the rest** (`selectedRunDetailFacts`) sit in
    `details.gh-disclosure.rr-run-details` *Run details*, closed by default, as
    `app-run-facts layout="stacked"` (`.rr-run-details-facts`, an inset panel: label above value, facts
    side by side). The disclosure is `align-self: stretch`, because the size container inside gives it
    no width. Its text-only summary holds
    the title, the `aria-hidden` one-line read-out `runFactsReadout` (*Gameplay Help · Standard
    Intelligence Index · Started … · Board 18/18*, or *Board incomplete*) while closed, and, while the
    board has gaps, a visible `gh-tag gh-tag-changed` *Graded without the board*, open or closed. The
    open state is read once from `localStorage['overseer.benchmark.runReport.header']` (`{ version: 1,
    detailsOpen }`, `RUN_REPORT_HEADER_STORAGE_KEY`, try/catch, default closed) and written from the
    native `toggle` event. Both splits are memoized on the same run object as `selectedRunFacts`. The
    facts are a wrapping flow, not a grid (`display: flex; flex-wrap: wrap`, inline `dt` + `dd` pairs, so
    a tall pair stretches nothing), with labels stacked below 36 rem; the *Board* figures carry their
    separator dot at their end (`li:not(:last-child)::after`, `nowrap`), so a wrapped line never starts
    with a dot, and the same-every-run note is a click-mode `app-info-tip` `#rr-board-note-tip`
    (*Board delivery*). In the single layout the header's items align to the top
    (`:host(.rrf-layout-single) .rrf-header`). `app-run-report-frame layout="single"`
    keeps the header and a tab row (`[runReportTabs]`) in place and scrolls one body (`[runReportBody]`;
    `scrollBodyToTop()` on a tab change). The row is `.gh-tabs .gh-tabs-secondary`, *Run report
    sections*, eleven tabs without icons from `runReportTabs` (`rr-tab-<key>` controlling
    `rr-panel-<key>`): *Summary · Integrity · Synthesis · Questions (N) · Difficulty · Tools · Cost ·
    Configuration · AI Reports · Calibration · Paired Test*, with arrow-key, Home and End roving. Every panel is
    rendered and the unchosen ones are `hidden` — never `@if` — so the key-figures export reads the
    Summary cards from any tab. The chosen tab is in `localStorage['overseer.benchmark.runReport.tab']`,
    restored on every open (unknown → *Summary*); a re-score reload keeps the tab shown; `jumpToAnswer`
    selects *Questions*. While `hasRunIntegrityNotice` — the one getter the notice and the tab both read —
    the Integrity tab carries a `gh-tag` *Notice*. Only the *Action Error* alert and the *Retry in
    progress* strip sit above the panels. *Summary* heads the key figures with an `h4.gh-section-title`
    *Key figures* and, beside it, icon-only **Copy** and **Download** for the whole set and a text-only
    `.btn-ghost` **Choose figures** (*(9 of 12)* while a subset is selected); the cards sit in
    `.rr-figures` (a grid), and every `.score-card` carries a stable `data-figure` key (`intelligence`,
    `raw-quality`, `unweighted-mean`, `speed`, `mean-time`, `panel`, `agreement`, `holistic`,
    `answer-duration`, `wall-time`, `model-cost`, `estimated-cost` — unchanged by a label variant) and
    ends in an `app-key-figure-card-actions` pair shown on hover or focus (opacity only). *Mean Time per
    Question* sits right after the Speed Index card and is always shown (`—` with no answered
    question). **Choose figures** opens `app-key-figures-chooser` (`run-report-frame/`), a nested
    `<dialog class="gh-dialog">` *Choose key figures* with light dismiss: a checklist, *All* / *None*
    and a status count, a header close `.btn-icon-action` with an `interestfor` *Close* tooltip, and a
    single footer **Done**; below it a second checklist, *Image details*, one box per run-fact row with
    its own *All* / *None* and count, emitting `detailSelectionChange` and stored as excluded row keys in
    `localStorage['overseer.benchmark.runReport.imageDetails']` (`["board"]` while nothing is stored).
    The dialog (`frame.frame($width: 60rem)`) is two columns from a 44 rem container width, one below
    it: *What to show* (the two checklists) and *Image file*, which holds `app-export-format-section`
    (`model-comparison/`, PNG or WebP with the wizard's WebP quality list; its note says Copy always
    places a PNG, because `ClipboardItem` rejects WebP) and `app-export-size-section` with
    `fitLabel="Fit the figures"` and `[showTextSize]="false"` (a box size lays the figures out for its
    aspect ratio and scales them to fill it; the default, *Fit the figures* at 200 %, reproduces the
    unboxed images exactly). The footer's `p.kfch-summary` says what Download writes (*Downloads a
    WebP, 3840 × 2160 px.*; *about* in fit mode, measured by the host's `measureImage`), beside the one
    **Done**. The settings, `KeyFiguresExportSettings` (`run-report-frame/key-figures-export-settings.ts`),
    are stored **once for both report dialogs** in
    `localStorage['overseer.benchmark.keyFigures.export']` (`{ version: 1, format, webpQuality, size }`)
    and the two sections' open states in `….keyFigures.exportSections`; each host reads them **fresh on
    every open, copy and download**, never once at construction, so a change made in one dialog applies
    in the other. The Download buttons' names and tooltips name the format (*… as a WebP image*), and the
    file extension follows the encoded format (a browser that cannot write WebP saves a PNG and says so).
    The selection is a live filter on both the Summary cards and the image:
    every change emits `selectionChange` at once, and Done, the close button, Escape and light dismiss
    only close. An unselected card, and the whole `.rr-figures` grid when nothing is selected, gets the
    `hidden` attribute — **never an `@if`**, because the chooser builds its list from, and the export
    filters, every rendered `.score-card`; a removed card could never be chosen again. With nothing
    selected the Summary shows *No key figures are selected. Use Choose figures to show them.*
    (`.rr-figures-empty`) in the grid's place. The choice is stored as the *excluded* keys in
    `localStorage['overseer.benchmark.runReport.keyFigures']` (`{ version: 1, excluded: [...] }`), so a
    card added later is included, and the header's one-click Copy and Download export the selection. The PNGs come from `run-report-frame/key-figures-image.ts`, which reads the rendered
    cards, filters them by the selection (footnotes from the included cells only) and composes them
    **square, or landscape as near square as possible, never portrait**, for any subset; the spec pins
    the layouts. Above the cards the images draw the chosen run-fact rows through `toImageFactRows`
    (badges atomic, colors from `BADGE_PALETTE`, the run status after *Started*); the card image draws
    only the chosen *Model* and *Assessor(s)* rows. The Intelligence Index and the Speed Index values
    share the headline size (`.score-value`, and `.score-subvalue.score-headline` on the Speed card
    except in its demoted *Median Model Time* form); the image draws a cell at the headline size from
    `KeyFigureCell.headline`, read from those classes, while `main` only makes the Intelligence card
    span two strip columns — change both together. In the images' fact rows each model after the first
    (the panel's *B* assessor) starts a line of its own. While loading or after a failed load there is no tab row.
    **No footer.** Escape and the header Close both close it, and closing always stops detail polling.
    Question cards have real `<button>` headers, filter toggles (*Critical errors*, *Disputed*,
    *Members disagree*, *Below 70*, *Flagged*) and *Expand all* / *Collapse all*. The **AI Reports**
    tab is its own component, `app-run-ai-reports` (`run-ai-reports/`), fed the run, the picker
    options, the configurations, the launcher's writer and whether the dialog is open. It lists the
    run's two run-completion documents, one row each, written or not: a written one with a *Written* /
    *Written with warnings* tag, a meta line (*by <writer> on <date>*, then duration, cost and *same
    provider, acknowledged* where known), a *Run changed since this document was written* badge when
    the list item says so, a **View** button (`btn-gh btn-gh-small` with the *eye* glyph, named *View
    the Executive Summary* / *View the Report for AI Researchers and Developers*) that opens the
    document in `app-pdf-viewer-dialog`, and a **Delete** icon button (`.action-btn-danger`, *trash*,
    `aria-disabled` with a tooltip saying why while a job is Pending or Writing) behind a confirmation;
    a missing one with a neutral *Not written* tag (no tag at all until the list answers). A
    `role="status"` line above says only the job state (*Waiting for the report writer*, *Writing…*,
    *Failed: …*, *Skipped: …*, the cancellation message, *Not written yet: …*), with **Show Progress**
    (`.btn-ghost`) while the documents are Pending or Writing, an automatic job's included; it is
    refreshed by the run poll and, after completion, by a 5-second poll of the job endpoint that stops
    when the dialog closes. When a document is written, an `alert-info` *Downloads* notice holds **Open
    Download Center** (*file-with-arrow*), and focus returns to that button when the Download Center
    closes. When the finished run lacks a document, the *Write missing reports* fieldset holds a
    checkbox per missing document, the **Report writer** picker — preselected with the run's own
    writer, else the launcher's when it needs neither a refusal nor a warning for the run's candidate —
    with a dialog-mode info tip (*Choosing a report writer*) of per-document advice, **Write Report** /
    **Write Reports** (*zap*) vertically centered beside the picker, a live cost estimate (debounced,
    from the estimate endpoint) shown as the `#rrWriteEstimate` *Estimated cost* panel — a quiet
    `.gh-estimate-panel` block (global, in `styles.scss`, shared with the Model Comparison wizard's Reports step) with a gold start border, the total, a per-document `dl` breakdown when two
    documents are checked, and a note; `role="status"`, always rendered so the live region exists, and
    named by the Write button's `aria-describedby` — a refusal as a red `.gh-field-error` line that disables the button, a same-provider
    writer as an amber `alert-warning` that leaves it enabled and opens a nested *Same-Provider Report
    Writer* confirmation (**Write Anyway**, *zap*) on every write, and server errors as
    `alert-danger`. Write is disabled with no writer, no document checked, a refusal, and while a job
    is Pending or Writing. After a delete the picker is cleared, with a note, if it held the model that
    wrote the deleted document. Two documents with two writers are two rounds. The nested
    confirmations stop their own `close` and `cancel` events so the run report dialog never sees them.
    The section is not a run action.
    The **Difficulty**, **Tools** and **Cost** panels are capped at 48 rem (`.rr-panel-narrow`) and
    **Configuration**, **AI Reports**, **Calibration** and **Paired Test** at 60 rem
    (`.rr-panel-medium`), each centered in the dialog (`margin-inline: auto`); the other tabs are full
    width. The **Paired Test** tab is its own component, `app-run-paired-test` (`run-paired-test/`, `rpt-`
    ids), fed the run, `workspace.historyRuns` and whether the tab is shown: this run is the treatment;
    the `#rpt-baseline` select offers the finished runs on the same suite, newest first, labeled *#id ·
    model · index · date* and grouped in `<optgroup>`s by the kind the server names
    (`POST runs/{id}/paired-comparison/kinds`, fetched when the tab is first shown) — *Model
    comparison*, *Verification of a change*, *Replicate* — with not-comparable runs left out and
    counted in a `role="status"` line; a kind sentence and the changed keys; **Compare** (`.btn-gh`, no
    glyph: a plain commit); and the result in `app-paired-test-result`. The Tools tables right-align
    their counts (`.rr-num`). The panels share one type scale: running text at `--text-body`,
    metadata and hints at `--text-secondary`, and every tab heading `.gh-section-title`; the Summary
    cards, question cards, synthesis panel and cost panel keep their own scales.
    Calibration's controls are one column, at most 36 rem wide: *Calibration assessor*, then *Compare
    against*, then **Calibrate assessor** at its own width. On a panel run *Compare against* is an
    `app-model-picker` (`.calibration-target-selector`, labelled `bmCalibrationTargetLabel`) whose
    options are tagged *Assessor A*, *Co-assessor B* and *Panel*, each naming its model; the Panel
    option reads *Mean of A and B* with the two model names. The launcher's *Grading*
    fieldset has the matching optional **Report Writer** field after *Claim Verifier* (an
    `app-model-picker`, empty choice *None — no AI-written reports*, a click-mode info tip, remembered
    with the other launcher fields). The model under test as writer is a red `.gh-field-error` refusal
    (`#bmReportWriterRefusal`, added to the picker's `aria-describedby`) that holds Start back; a
    writer from the model under test's provider is an amber `alert-warning report-writer-advisory`
    that does not. Both rules come from `run-ai-reports/report-writer-policy.ts`, which the AI Reports
    tab shares; the server stays authoritative. Audience 2 is shown everywhere as **Report for AI
    Researchers and Developers** (its stored type is still `TechnicalReport`).
  - `#sameProviderDialog` (`run-tab/`): the same-provider confirmation on Start, **role-aware** by the 409's
    `role`. For `assessor` it is the *Same-Provider Assessment Warning* (*Model Under Test / Assessor
    Model / Provider Family*); for `reportWriter` the title is *Same-Provider Report Writer*, the lines
    *Model Under Test / Report Writer / Provider Family*, and the note says the reports may describe the
    model more favorably. **Acknowledge & Start Run** re-sends the start with the matching flag
    (`acknowledgeSameProvider` or `acknowledgeSameProviderReportWriter`) and keeps any acknowledgment
    already given in that attempt, so an assessor prompt followed by a report-writer prompt resolves
    both. Neither flag is persisted (*Safety Acknowledgments Excluded*, below).
  - `app-run-report-writing-dialog` (`run-ai-reports/`, owned by `app-run-ai-reports`): the progress of
    a run's AI report writing job, opened after a write and by **Show Progress**. A `role="status"` line
    that changes with the phase only (never with the clock), the shared `.run-stage-rail` (*Queued*,
    *Preparing*, one stage per document, *Done*), an indeterminate `progress`, a `.run-stat-strip`
    (elapsed on the server's clock, writer, model calls, tokens, cost so far or total, estimate), a
    documents table with **View** once finished, and a *Diagnostics* `gh-disclosure` with icon-only
    **Copy** (*copy*) and **Download** (*file-with-arrow*,
    `run-<id>_ai-report-writing-diagnostics_<yyyyMMdd-HHmmss>.txt`). It polls through the worker
    ticker every 2 s, backing off 2 → 4 → 8 → 16 → 30 s on failures. **Run in Background** and the
    close button never cancel; **Cancel Writing** opens a nested confirmation. The server answers with a
    *Queued* starting view (empty writer, shown as *—*) from the moment a write is accepted; a 204 while
    the run's stored status is Pending or Writing keeps polling for up to 30 s after `open()`
    (`RUN_REPORT_JOB_START_GRACE_MS`), and otherwise shows the run's stored status. It is
    `frame($width: 60rem, $max-height: 92dvh)`, so the finished documents table fits with **View**.
    Finished, it offers **Open Download Center** and **Done**. It and its confirmation stop their own
    `close` and `cancel` events.
  - `app-pdf-viewer-dialog` (`shared/pdf-viewer/`): a **generic, shared** full-screen PDF viewer; the
    caller supplies the title, subtitle, variants, a loader returning the bytes and, optionally, a
    same-origin URL for *Open in new tab*. pdf.js (`pdfjs-dist`, loaded only through the dynamic
    `import()` in `pdfjs-loader.ts`, behind the `PDFJS_LOADER` token specs replace) draws canvases and a
    selectable text layer. The toolbar holds the variants as a segmented `gh-tabs` row (the AI Reports
    tab passes the document's allowed disclosures) followed, when the caller passes `variantsInfo`, by a
    dialog-mode `app-info-tip` (*Versions*; the AI Reports tab passes `reportDisclosureInfo(audience)`,
    that document type's own explanation — items may carry `points`, rendered as a list — from
    `admin/benchmark/report-disclosure-guide.ts`), page navigation, zoom, **Download PDF** (the server's
    file name) and **Open in new tab** (*external-link*), with **Close** in the header. **It uses
    `ViewEncapsulation.None`**, because pdf.js builds the page DOM outside Angular's templates, where
    emulated encapsulation cannot reach; every rule in its stylesheet is therefore scoped under `.pdfv`,
    including the subset of pdf.js's `pdf_viewer.css` it carries. It focuses its title on open and stops
    its own `close` and `cancel` events. No PDF is framed or embedded, so the CSP needs nothing new.
  - **Download Center**: `download-center/download-center-panel.component.*`
    (`app-download-center-panel`) holds the whole body and footer; `benchmark-download-center.component.*`
    (`app-benchmark-download-center`) is a thin dialog wrapper around it — header, emblem, title and
    Close — with the same `open(context)` / `closed`, a `documentsChanged` output and an
    `openBatteryDownloads` output relaying the panel's **Open battery run downloads** (the battery run
    id), on which the shell's `openBatteryDownloads` reopens the same dialog on that battery run's
    `battery` context. The wrapper is
    opened from the run report's **Downloads** (a `run` context), the Battery Run Report's **Downloads**
    and its AI Reports tab (a `battery` context) and from the Model Comparison launcher's **Open
    Download Center**; the panel is also placed directly as the Model Comparison wizard's step 4
    (below). Contexts: `run` (the run's files and its own documents, `subject=run:<id>`; see the
    package paragraph below for a battery member's pointer), `battery` (`{ kind: 'battery',
    batteryRunId, label }`: the **Battery analysis report** file row — the Markdown from `batteries/runs/{id}/report`,
    `battery-run-<id>_report.md` as fallback name — and every document whose subject is
    `battery:<id>`, battery-completion and Report Pack alike, via `listReportDocuments({ subject })`, with
    the *being written* notice and a 5-s poll of the battery run's report job; **no member-run rows**),
    `documents` (chosen ids) and `library` (`scope` — `{ kind: 'comparison', comparisonId, name,
    entryKeys }`, `{ kind: 'subject', subjectKey, label }` or `{ kind: 'all' }` — and `preselect`, listed
    with `origin=reportPack`; a numbered comparison is listed by `comparisonId` plus, by its entry keys,
    the documents that carry no number, merged once each; a context differing only in the comparison's
    name keeps the rows and choices). **The documents list** is a card list (`frontend_ui_controls`
    §8h). Its search, **Sort by**, facets, chips, status line and Load more come from the shared
    `CardListState` (`shared/data-table/card-list-state.ts`), behind a private `list` getter that
    builds it on first use, after the `idPrefix` input is bound; the panel's public members (`searchText`, `sortId`,
    `facets`, `activeChips`, `showMore`…) and exported constants are one-line delegates and aliases
    over it (`DownloadFacet` = `CardListFacet`, `DownloadFilterChip` = `CardListChip`), and the
    selection, its bar and every focus move stay in the panel. Its header is the *Documents* heading
    (`documentsHeading`: *Documents of Comparison #12 — <name>* while one numbered comparison's documents
    are listed), a dialog-mode `app-info-tip` **About document options**
    (sections *Sharing*, *Disclosure* — the guides per document type and their notes — *Peer names*,
    *Formats* and, with chart actions, *Charts*; text in `DOWNLOAD_OPTIONS_HELP`) and the polite status
    line *Showing 10 of 23 documents* (*· filtered from N* while a filter is active). The filter bar
    holds a search (*Search title, comparison, model, suite or writer*: the title, detail, subject, covered
    models, *Comparison #12* / *#12* and its name, suite and writer, debounced 200 ms; Escape with text
    clears it without closing the dialog around it), **Sort by** (a native select of named orders:
    *Newest first*, *Oldest first*, *Document type*, *Title (A–Z)*, *Model (A–Z)* — by the first covered
    model, its stored id still `subject` — *Suite (A–Z)*, *Writer (A–Z)*, *Writing cost, highest first*,
    *Changed since written first*; remembered per browser under
    `overseer.benchmark.downloadCenter.view`, apart from the download settings), the `app-filter-facet`
    facets *Document*, *Scope* (*Whole comparison*, *Model subset*, *One model*, from
    `reportDocumentScope`), *Comparison* (*#12 — <name>*, highest number first; never while one
    comparison's documents are listed, as on step 4), *Model* (from the list DTO's `coveredModels`: a
    comparison-wide or subset document counts under each covered model, a per-model one under its
    subject), *Suite*, *Written by*, *Changes*, *Charts* (only with chart actions)
    and *Created* (single mode: last 24 hours, 7 days, 30 days) — each listed only while its rows hold
    two values or it has a selection, each option counting the rows the other filters leave — the
    removable active-filter chips with **Clear all** (which keeps *Show selected only*), and the
    selection bar (*N selected — M not shown*, **Show selected only**, **Clear selection**, **Select
    all N** / **Select all N matching**, and, with chart actions, **Update charts…**). There is no
    Sharing facet, and no select-all checkbox. Each card is an `<article>` in a `ul[role=list]`: the
    *Include …* checkbox top left, a kicker (document type, *Shareable* / *Internal only* with its
    reason tip, *Run changed…* / *Comparison changed* tags), the `<h5>` title as the checkbox's
    `<label>`, a meta line (`cardMeta`: for a document of a numbered comparison first *Comparison #12*,
    its name and, for a subset, *2 of 5 models*; then the date, the subject for a per-model document
    only, the suite and the writer; or a run file's suite and model), the
    icon-only actions top right in a `role="group"`, and under a hairline the options with visible
    labels (*Disclosure*, *Peer names*, the *Formats* fieldset, *Charts*); a chosen card takes the
    package cards' gold border and tint. The list shows 10 cards and **Show N more** / **Show all M**
    (focus moves to the first new card's title); the count returns to 10 when a filter, the search or
    the sort changes. Card actions are icon-only: **View** (*eye*; a pack document in `app-pdf-viewer-dialog`
    at its highest allowed disclosure with a *Peer names* second row when it has peers, or the run
    report's PDF) and **Delete** (*trash*, Report Pack documents only, a nested confirmation, focus to
    the next card after it). The panel is an inline-size container (`dc-panel`): the package column
    sits beside the list from 48rem and the filter bar sticks to the top of the body from 36rem; the
    card list is its own container (`dc-cards`), and below 30rem a card puts its actions on their own
    line and its options one per line. **Chart
    actions** (`DownloadCenterChartActions`, lent only by the wizard): the *Charts* option (*None*, *3 ·
    current*, *3 · differs from step 2* in `.gh-tag-changed`) and facet, the selection bar's `.btn-ghost`
    **Update charts…** on the selected documents, and a per-card **More actions** `.gh-action-popover`
    (`frontend_ui_controls` §4f) with *Update charts* and *Remove charts*. **Update charts** opens a
    nested dialog with `app-report-chart-picker` limited to the chosen documents' types and prefilled
    from their current figures, the print advisory, and Update / Cancel; progress uses the preparing
    overlay; documents without peers, from another comparison or on another pricing basis are listed
    under *Not charted* with their reason, and a storage-not-configured stop shows a visible warning.
    Package presets *Internal*,
    *External* (the Executive Summary and the Report for AI Researchers and Developers; internal-only
    rows listed but unselectable, with their reason) and *Custom*; a run context lists the run's files
    and the run's own documents — every document whose subject **is** the run,
    `listReportDocuments({ subject: 'run:<id>' })`, run-completion documents included, never a group's
    or a battery result's that merely includes it — and, for a battery member, an `alert-info` pointer
    (`.dc-battery-pointer`) *"This run is a member of battery run #<id>. Its AI-written documents are
    in the battery run's downloads."* with a `.btn-ghost` **Open battery run downloads**
    (*file-with-arrow*), which asks the host to switch the same Download Center to the battery run's
    `battery` context;
    per-document disclosure (*Summary* / *Detailed* / *Full*, from each row's server-supplied
    `allowedDisclosures`: the Executive Summary offers *Summary* / *Full*) and peer naming (*Named* /
    *Anonymized*); `_INTERNAL` file-name suffixes; `reportDocumentFileStem(doc, title, naming)`, matching
    the server's PDF and Word names (`BenchmarkPdfFileNames`): a Report Pack document of a numbered
    comparison by `comparisonFilePart` — `comparison-12_<model slug>_<kind>` per model,
    `comparison-12_<name slug ≤ 40>_<kind>` comparison-wide (`comparison-12_<kind>` anonymized),
    `comparison-12_subset-<up to three model slugs joined by -vs->_<kind>` for a subset, or
    `subset-2-of-5-models-<6 hex>` (always when anonymized), the kind `executive-summary`,
    `researcher-report` or `internal-brief` (`reportKindSlug`) — and any other document by its title with a
    `run-<id>_` or `battery-run-<id>_` prefix and a `vs-` part; several files as one ZIP named by
    `downloadZipStem` from the **chosen** rows (one comparison: `comparison-12_<name slug>`, its name only
    when every chosen document of it is named; several: `comparison-reports`; a run or battery context:
    its model or label) with a `MANIFEST.md` whose header lists *Comparison(s)* (`manifestComparisons`)
    and each file's *Comparison* and *Model* or *Models* (*all 5*, a subset's names, or letters when
    anonymized). Its
    HTML converter owns private `marked` and DOMPurify instances — **never** the chat pipe's global
    ones. Every row offers **PDF** first, then **Word** (both rendered server-side, see
    `ai-benchmark-report-pack.md` § 8); the External preset is PDF only and Internal PDF, Word and
    Markdown, with a remembered *A4* / *US Letter* paper size (stored settings version 4,
    `STORED_SETTINGS_VERSION`; versions 2 and 3 are migrated on reading — every package gains the
    Provider Issue Report row's default choice, `PROVIDER_ISSUE_REPORT_DEFAULT_CHOICE`, an empty choice
    the package preset fills, and version 2 also loses the Internal package's remembered formats). The
    **Provider Issue Report** row (`DownloadRowCategory` `providerIssueReport`) exists only for chat
    consistency documents; the External preset selects it with the Executive Summary and the Report for
    AI Researchers and Developers, and Internal selects every row. The `chatConsistency` context
    (`{ kind: 'chatConsistency', analysisId }`, title *Chat consistency documents*, subtitle *Chat
    consistency analysis #N*) lists the documents of one analysis by subject key
    `chat-consistency:<id>` and origin `chatConsistencyReport`, and names a file
    `chat-consistency-<id>_<subject slug>_<kind>` (`provider-issue-report` for the new kind). Run diagnostics are captured once per download, so the `.txt`, `.pdf` and `.docx` agree.
    While a package is prepared, an overlay over the body shows a ring spinner, the step and a progress
    bar. The footer's Cancel appears only while documents are being prepared and cancels the
    preparation without closing; the X and Escape close, and abandon a preparation. It is
    `btn-gh btn-gh-cancel dc-cancel` with no icon, left of **Download**, in every host (the wrapper and
    Model Comparison's inline panel); it aborts the in-flight requests (`takeUntil` on a cancel notifier
    around every `firstValueFrom`), saves nothing, announces *Download canceled. Nothing was saved.* in
    the `role="status"` line and focuses **Download**. A close, a cleared context or the panel's
    destruction abandons the same way, silently. An *Update charts* run is not a download and shows no
    Cancel. The dialog is `frame($width: 92rem)` and nearly full height, the GnollBench emblem
    (`.dc-emblem`) precedes its title at 64 px, 40 px under 600 px — the run report's size — and
    **its title is the focus target on open**
    (`<h3 #downloadCenterHeading tabindex="-1">`, focused after `showModal()`), so the Close button's
    `interestfor` tooltip does not open by itself. Its explanations are click-mode `app-info-tip`s,
    except the *Disclosure* column's, a dialog-mode tip (*What each disclosure level contains*) with
    one section per document type from `report-disclosure-guide.ts` — the same per-document texts the
    PDF viewer shows — and a note on how Markdown and HTML differ; the *Internal only* tag, the
    *Peers are named* warning and failures stay visible. Its *Documents* heading and the two package
    legends use the shared `gh-section-title`; the legends keep a `dc-section-title` override only for
    what the global `.gh-choice > legend` rule would otherwise change.
  - `#importDefaultSuitesDialog` (`suites-tab/import-default-suites-dialog.component.*`): Import Default Suites (Manage Suites tab) — a multi-select
    catalog of the default suite files under `Overseer/Data/DefaultSuites/`, opened by the
    toolbar's Import Default Suites button (from harness 24).
  - `#questionYamlImportDialog` (`app-question-yaml-import-dialog`, `question-yaml/`, hosted by `suites-tab/`): YAML import
    in three modes — replace one question (a question's Import from YAML), import into the open
    suite (Manage Questions toolbar), create a suite (Manage Suites toolbar's Import Suite from
    YAML) — with Provide YAML, Review and Done steps. In **suite** mode it attaches the game
    snapshot the document carries, behind a checkbox on the review step whose sentence comes from
    a server preflight (`POST snapshots/match`) that says whether an identical board is already
    stored and who owns it. Its help link routes to whichever help dialog matches the open mode.
  - `#questionYamlHelpDialog` and `#suiteYamlHelpDialog` (`app-question-yaml-help-dialog`, hosted by `suites-tab/`): **two
    instances of one component**, selected by its `variant` input (`questions`, the default, and
    `suite`). Both live in one document, so every element id carries the variant's `idPrefix`
    (`yaml-help`, `suite-yaml-help`) — a duplicated id silently breaks `aria-labelledby`,
    `aria-controls`, the tooltip anchors and the exclusive `<details name>` accordion. The
    questions variant fetches the rubric authoring guidance and assembles its *AI Prompt* text;
    the suite variant makes **no** server call — its guide text is static (`suite-yaml-guide.ts`)
    and its *AI Prompt* tab explains the Snapshot Suite Wizard and emits `(wizardRequested)`, on
    which the page closes the help and opens the wizard. Every code sample in both variants is an
    `app-code-block` (`shared/code-block/`).
  - `#snapshotSuiteWizard` (`app-snapshot-suite-wizard`, `question-yaml/`, hosted by `suites-tab/`): the Snapshot Suite Wizard
    from the Manage Suites toolbar — from a game snapshot to an imported suite, by adding questions
    to a snapshot suite (seven steps, the last, *Describe*, applying the description the agent
    suggested) or creating a new one (six steps). It holds `app-suite-prompt-builder` and an
    `app-question-yaml-import-panel` (the import body the standalone import dialog also wraps), and
    keeps its progress in `localStorage['overseer.snapshotSuiteWizard']`.
  - `#snapshotUploadDialog` (`app-snapshot-upload-dialog`, `snapshot-upload/`, hosted by `suites-tab/`): Upload Snapshot from
    a suite card, with a nested `#replaceConfirmDialog` when the suite already has a snapshot.
    Delete Snapshot lives in the snapshot viewer's Delete tab (`#deleteConfirmDialog` in
    `snapshot-viewer.component.html`).
  - `#graderGuideDialog` (`app-benchmark-grader-guide`, `grader-guide/`): *How the graders work* —
    the grading roles, second-reader coverage, the reference reader, the claim verifier, choosing
    grader models and the recommended settings. Opened from the launcher's Grading group and from
    the scoring profile form (a nested modal over the profile dialog); `open(section?)` scrolls to a
    section, and a `profile` input fills in that profile's trigger values.
  - `app-battery-editor-dialog` (`batteries/battery-editor-dialog.component.*`, `bbe` ids, title
    `#bbeTitle`): **New Battery** / **Edit Battery** — an 88 rem **full-height** frame. A details row
    (name ≤ 26 rem beside description ≤ 44 rem from 48 rem of body, stacked below), then two columns
    from 56 rem of body (`bbe-body` container) that **scroll independently**: an
    `app-reorderable-list` of checkable suites (list order is run order) with an *N of M selected*
    count, and the weighting. **Weighting** is a `fieldset.gh-choice` radio group named `bbeScheme`
    (ids `bbeScheme-{value}`, legend visually hidden, the info tip beside the section `h4`) defaulting
    to *Questions and difficulty*; the custom-weight error sits directly under it. The live preview,
    computed in the browser from the per-suite masses, is a `ul.bbe-weight-list` of
    `article.bbe-weight-card`s in run order: a kicker *Suite n of N*, an `h5`, the unassessed warning
    (and an amber inline-start border), a `dl.bbe-weight-metrics` (questions, difficulty mass, the
    custom weight under *Custom*, the chosen weight), a `.bbe-weight-bar` share bar and
    `.bbe-weight-others` under a hairline. The `bbe-weights` container moves the metrics under the
    head below 40 rem and to one column below 26 rem. Below 56 rem of body everything is one column
    and the body scrolls.
  - `app-battery-progress-dialog` (`batteries/battery-progress-dialog.component.*`, `bp` ids):
    **full-screen** battery progress — under the heading *Model under test* (`batteryModelName` /
    `batteryModelBadges` in `battery.models.ts`, service tier included) and *Report writer*, both
    rendered by one `ng-template` (`#modelIdentity`: `.model-name` plus the `runFactBadges` thinking,
    reasoning, provider and service-tier badges); the writer from the battery run's `reportWriter…`
    fields, else the report job, else the System AI Configs list. Then the shared `.run-stage-rail`
    (*Suite runs*, *Battery analysis*, and *AI-written reports* when a writer is set, whose *done* note
    counts `reportDocumentsWrittenCount`; stacked below 40 rem of an inline-size container), the
    labelled progress bar, the state block, the stat strip (after *Failed*, three live figures from the
    polled battery run: *Mean answer* — `meanModelTimeMs` in the run report's `formatModelTime`
    format, noted *model time, tools excluded* in a `.bp-stat-note` — *Candidate cost* and *Total cost*
    — `liveCost.total` plus the report writer's cost, noted *so far* while live and *incl. report
    writer* once that cost exists — each *—* with a visually hidden *not available yet* when missing;
    then a sixth tile, *Overall Index*, holding an `app-index-badge size="md"` once the analysis is
    done), below it a closed `details.gh-disclosure.bp-cost-details` *Cost by role* with the live
    `app-benchmark-cost-panel` (absent while `liveCost` is null), and a suite × round grid of status chips
    (`BATTERY_SLOT_STATE_LABELS`). Under its chip a running member shows *Run #N · Stage n of 3 —
    name* (`runStageCaption`, `run-stage-labels.ts`, whose names the run dialog's rail shares), a
    full-width `progress.job-progress` labelled by *n of m questions answered* (the shared job bar
    look applies to `progress.job-progress` anywhere, not only inside `.job-progress-block`), and
    *Elapsed …*; a completed member
    an `app-index-badge`, a facts line (*Speed 71 · 13m 40s · 0 refuted claims · 3 flagged answers ·
    est. $0.42*, flagged omitted at 0, the estimate omitted when `estimatedCost` is null) and *Run #N*; an empty pending slot of a live run *Waiting for suite …*.
    A cell whose member run is in `repairingRunIds` shows the chip *Re-run in progress*
    (`BATTERY_REPAIRING_LABEL`, `data-repairing`) and is marked current; an *Index withheld* cell's hint
    is `INDEX_WITHHELD_HINT`, *"Re-run failed questions on this run; the battery run follows the re-run
    when it finishes."* Elapsed times and durations are `formatElapsed`. The dialog polls while the
    battery run is live and, after it, while `batteryAwaitsPostRun` holds — the server's `postRunWork`
    is not `None` (`Repairing`, `Analysing` or `WritingReports`, `battery.models.ts`
    `batteryPostRunWork`); there is no client-side grace window — and the stage rail follows
    `postRunWork`; the report job is polled while the reports stage is current. One polite live region
    (the stage line, post-run texts included), per-member *Open run progress* (emits `{ runId,
    batteryRunId }`, which opens the run progress dialog on that run as the monitor's viewed run, with
    **Back to Battery** returning to that battery), **Attach existing run** on empty, superseded and
    index-withheld cells, and **Cancel Battery**, **Re-run under current instrument**, **Continue** and
    **Open Analysis** in the footer. **This dialog is the one home of Continue and Re-run under current
    instrument** (`frontend_ui_controls` §4g): Continue reads *Continue — <reason>* (plain *Continue*
    without a stop reason) behind the *play* glyph; a 409 on Continue offers Re-run under current
    instrument in its place only when the body carries `instrumentChanged: true`, and any other refusal
    shows its message alone. Both are `aria-disabled` while a resume is in flight, and call the
    monitor's `armCompletionSignalsFromGesture()` synchronously in the click. Opened from the battery
    banner's **Show Battery Progress**, a Run History battery card's **Show progress** and the Battery
    Run Report's *Show progress* action. **Open Analysis** emits `openAnalysis` with the battery run id, and
    the shell's `onOpenBatteryAnalysis` opens the Battery Run Report.
  - **The progress dialogs' elapsed clock.** The run (`BenchmarkActiveRunMonitor`, stop function in
    `runElapsedInterval`), multi-run and battery progress dialogs share one format, `formatElapsed`
    (`benchmark-run-format.ts`: *45s*, *3m 05s*, *1h 02m 05s*, whole seconds floored), and one tick,
    `startElapsedTicker(startedAtUtc, onTick)` (`utils/elapsed-ticker.ts`), which returns its stop
    function. It is a self-aligning `setTimeout` chain, not `setInterval`: each tick is scheduled
    `1000 − elapsed % 1000 + 20` ms ahead (a plain 1000 ms without a known start; the start is read on
    every tick), so the figure changes once a second and a late timer neither skips nor repeats one.
    Nothing is scheduled while `document.hidden`; on `visibilitychange` back to visible it ticks once
    and re-aligns. A new progress clock uses it rather than its own interval. The report-writing and
    Report Pack views count on the server's clock and are not on it.
  - `app-battery-leaderboard-dialog` (`batteries/battery-leaderboard-dialog.component.*`, `bl-` ids,
    hosted by `app-benchmark-batteries`, opened with `open({ definitionSha256, name, … })`):
    `<dialog class="gh-dialog gh-dialog-fullscreen battery-leaderboard-dialog" aria-labelledby="blTitle">`,
    *Leaderboard: {name}*. It focuses `h3#blTitle[tabindex=-1]` on open, closes on Escape and the header
    **Close** (`.btn-icon-action`, *Close leaderboard*, `interestfor` tooltip) and stops its own `close`
    and `cancel` events. The header has the decorative emblem, a subtitle, a `ul.bl-badges` (*Revision
    n*, the scheme, *K suites*, *R runs per suite* when uniform, *definition* hash with a click-mode
    info tip) and the group *Leaderboard actions*: **Open in Model Comparison** (`.btn-ghost`,
    *compass*; `aria-disabled` with a tooltip reason below two ranked rows in the shown class; it calls
    `BenchmarkShellBridge.openComparisonWizard({ batteryRunIds })` with at most
    `MAX_COMPARISON_SOURCES`, dropping the newest first with the visible `#bl-comparison-cap-note`) and
    an icon-only **Refresh** (*rotate*). The body: a `role="status"` loading line, an `alert-danger` with
    **Retry**, or the empty state; the note *Each table ranks results of one comparability class.
    Overlapping intervals are not a ranking.*; with several classes a `.gh-tabs .gh-tabs-secondary`
    tablist *Comparability classes* (*Class A*, *Class B*…, a count, most rows then newest first; the
    full §5 contract), with one none; the class line *Harness h · scoring method m · class `a1b2c3d4`*
    and *Differs from other classes on* with one `gh-tag` per key; `table.gh-table.gh-datatable.bl-table`
    in a `gh-datatable-scroll` with a sticky header and a hidden caption — *Rank* (index rank, an *≈*
    with hidden *interval overlaps the result above*), *Model* (badges with hidden prefixes), *Overall
    Index*, *95 % interval* with an `aria-hidden` interval strip (one scale per class, padded 5 %, the top
    row gold, chevrons for truncation, hidden below 48 rem), *Speed*, *Pass cost*, *R*, *Analyzed*
    (`<time>`) and an icon-only **View the report of battery run #N** (*eye*), which asks the shell
    through the bridge to open the Battery Run Report over the dialog; `TableState` with
    `app-sort-header` on *Model*, *Overall Index*, *Speed*, *Pass cost* and *Analyzed*, no pager, rank
    unchanged by a sort — and a closed `details.gh-disclosure` *Incomplete battery runs (n) — not
    ranked*.
  - `app-battery-run-report-dialog` (`batteries/battery-run-report-dialog.component.*`, `brr-` ids;
    the analysis panels keep their `bb-` classes): the **Battery Run Report**, hosted by the shell beside
    `#runDetailDialog` and reached through `BenchmarkShellBridge.openBatteryRunReport(id)` from Run
    History, the leaderboard and the progress dialog. **It mirrors `#runDetailDialog` part for part**:
    `<dialog class="gh-dialog gh-dialog-fullscreen" id="batteryRunReportDialog" aria-labelledby="brrTitle">`
    with `app-run-report-frame layout="single"`, `[runReportTabs]` and a `[runReportBody]` scrolled to the
    top on a tab change; no footer; Escape and **Close** (`.rr-close`, *Close battery run report*) close
    it and stop polling. The header: emblem, `h3#brrTitle[tabindex=-1]` *Battery Run #N* (focused on
    open), `app-run-facts` primary rows *Model* and *Assessor(s)*, and the *Run details* disclosure
    (`app-run-facts layout="stacked"`: *Prompt*, *Scoring profile*, *Started*, *Battery*, *Suites*;
    `buildBatteryRunFacts` in `run-facts.ts`, whose `battery` and `suites` keys a single run never
    emits), its open state in `overseer.benchmark.batteryRunReport.header`. `div.rr-header-controls`
    holds the `role="group"` *Battery run actions* — **Downloads** (*file-with-arrow*, the Download
    Center's `battery` context), **Actions** (*rotate* plus a *chevron*: a §4f popover of only *Recompute
    analysis* / *Compute analysis* and *Show progress*, each `aria-disabled` with its reason on a second
    line; Continue and Re-run under current instrument are the progress dialog's, which *Show progress*
    opens) and an icon-only **Copy
    diagnostics** (*copy*, `batteryRunDiagnosticsText`) — and **Close** outside it; there is no *View
    game snapshot*. The tab row `.gh-tabs .gh-tabs-secondary` *Battery run report sections*, no icons,
    every panel rendered and `hidden`, the chosen tab in `overseer.benchmark.batteryRunReport.tab`
    (unknown → *Summary*): **Summary** (*Key figures* with **Choose figures**, **Copy** and **Download**
    over `.rr-figures` `.score-card`s keyed `intelligence`, `critical-errors`, `answered`, `speed`,
    `mean-time`, `wall-time`, `model-cost`, `estimated-cost`, the choices in
    `overseer.benchmark.batteryRunReport.keyFigures` and `….imageDetails`, the download format and size
    shared with the run report in `overseer.benchmark.keyFigures.export`, the images named through
    `ImageContext.fileStem` `battery-run-<id>`; then the recompute callout and the caveats),
    **Integrity** (a `gh-tag` *Notice* from one getter), **Suites** (the *Profile unevenness* strip to
    two decimals with a click-mode info tip, then `ul.brr-suite-cards[role=list]` labelled by
    `h4#brrSuitesTitle`, one full-width `li > article.brr-suite-card` per suite, modeled on Run History's
    `.rh-card`: kicker, `h5` title, meta line, a `dl.brr-suite-metrics` of fixed 7.5 rem columns —
    *Index* as a `.score-badge`, *Contribution*, *Speed*, *Cost per run*, *Critical errors* — and a
    *Member runs* group of `.btn-gh` buttons *Run #N · Round r* with a visually hidden suite name, each
    emitting `openRunReport`; the `brr-suites` inline-size container moves the metrics under the head
    below 60 rem and goes to one column below 30 rem), **Robustness**, **Members**
    (the suite × round grid with **Open run report**), **Dimensions**, **Speed**, **Cost**,
    **Configuration**, **Paired Test** (this run the treatment; the baseline select from
    `getBatteryLeaderboard(definitionSha256)` grouped by class; a kind line — model comparison,
    verification, replicate (Compare disabled: M7 refuses it), probably refused; **Compare** runs `analyseBatteryRun(id, baselineId)` (M7,
    persisted) and then `POST model-comparison/paired/battery`, shown in `app-paired-test-result` with
    `[showPrimary]="false"` under the M7 block) and **AI Reports**.
  - `app-battery-ai-reports` (`batteries/battery-ai-reports.component.*`): the Battery Run Report's AI
    Reports tab, the counterpart of `app-run-ai-reports` with the same rows and tags, **View** (PDF
    viewer), **Delete** (confirmation), the *Write missing reports* fieldset, the **Report writer**
    picker preselected with the battery run's writer, the `.gh-estimate-panel`, the refusal line, the
    amber same-provider warning with **Write Anyway** (*zap*), **Show Progress** and **Open Download
    Center**. The row-building and tag rules both components use are in
    `run-ai-reports/report-documents-list.ts`; `report-writer-policy.ts` and `report-writer-advice.ts` (with
    a battery entry) are shared. Its progress dialog is `app-run-report-writing-dialog` with a
    `ReportJobSource` (`{ getJob(), cancel(), subjectLabel }`) for the battery run; without one the
    dialog builds the run source from `runId`, as before.

- **Comparison Source Picker (`comparison-source-picker.component.html`, Admin → GnollBench →
  Run History → Cross-model comparison, step 1)** — single runs, analysis groups and battery results
  on three kind tabs (`csp-src-tab-runs` / `csp-src-tab-groups` / `csp-src-tab-batteries`, *Single
  runs*, *Analysis groups*, *Battery results*), one table per tab, with a pager above and below it. The
  battery table (`csp-src-panel-batteries`, rows from the workspace store's `batteryRuns`) has *Compare*
  (*Include battery run N in the comparison*), *ID*, *Battery* (name and revision, with an
  `exactFilter`), *Tested Model*, *Status*, *Class* (short hash, or *Incomplete* / *Stale*), *Overall
  Index*, *R* and *Analyzed*. **No mixing:** while runs or groups are selected the battery checkboxes are
  `aria-disabled`, and the other way round, with the visible line *A comparison holds either battery
  results or runs and groups. Clear the selection to switch.*; the suite scope select says it does not
  apply to battery results. `ModelComparisonSelection` and `BenchmarkComparisonSelection` carry
  `batteryRunIds` (an older stored blob restores `[]`), and the comparability index is requested with
  `POST model-comparison/comparability`.
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

- **Model Comparison (`model-comparison.component.html`, Admin → GnollBench → Run History →
  Cross-model comparison)** — four steps: *1. Sources · 2. Charts & table · 3. Reports · 4.
  Documents* (`COMPARISON_WIZARD_STEPS`; step 3's summary *Write AI reports that compare the models of
  this comparison.*, step 4's *View, chart, download and delete this comparison's
  documents.*). Steps 2 and
  4 are reachable as soon as a comparison exists, step 2 even over one no chart can draw; step 3
  also needs an entry that is not Excluded, and is otherwise `aria-disabled` with a visually hidden
  reason, and **Next** skips it. Next runs 2 → 3 → 4, and on step 4 it closes the wizard. Step 1 uses `[hidden]` and step 2 `@if`; steps
  3 and 4 are mounted on their first visit (`visited3`, `visited4`) and then hidden, never destroyed,
  so step 3's form, running job and polling and step 4's table page, filters and selection survive a
  trip back to step 2. The loop the steps serve: set the charts on step 2, generate on step 3, view on
  step 4, go back to step 2 to change them, then **Update charts…** on step 4 and view again. While
  charts are being published the wizard's own close controls are disabled and the benchmark
  component's Escape guard refuses Escape, as during an export. For both, the dialog binds
  `closedby="none"` to the wizard's `closeBlocked`, and `onComparisonWizardClose` reopens a close that
  slips through, restoring focus, before the reports token and `viewSync.notify()`.
  - **The numbered comparison.** After a successful Compare, and whenever the entry set changes (a
    recompute of the same set asks nothing), the wizard calls `identifyComparison`
    (`POST model-comparisons/identify`); an answer for an older set, or a failure, is dropped, and the
    header then simply has no number. `currentComparison` (`{ id, name, entryKeys }`, one object per
    identity) feeds step 3's `comparisonId` and step 4's library scope. The header shows
    `.mc-identity`: *Comparison #12 — <name>* and an icon-only **Rename comparison** (`.action-btn`,
    *edit-3*, `aria-label` *Rename comparison #12*, an `interestfor` tooltip), which renders and opens the
    nested `app-comparison-rename-dialog` (`model-comparison/comparison-rename-dialog.component.*`,
    `mc-rename` ids; it stops its own close, cancel and click events): a *Name* field, `maxlength` 160,
    with the hint naming the default name, a **Reset to default** `.btn-link` (fills in the default name,
    which Save sends as a reset), **Cancel** and **Save** (`aria-disabled` and `aria-busy` while saving;
    a refusal shows as a `role="alert"` field error). The dialog leaves the DOM once closed, and focus
    returns to the button. `documentMatchesComparison` compares by number where both the document and
    the comparison have one, else by entry set.
  - **Battery results** are the third source kind. A battery comparison's `subjectKind` is
    `Batteries`: `sourceLabel` and `table-export.ts` read *Battery run N*, the cost axis titles say *per
    battery pass*, the launcher's *Last comparison* card names the baseline battery, `reportPackContext`
    carries `batteryRunIds` and `documentsContext` the `battery:` keys. No table display column was
    added. `BenchmarkShellBridge.openComparisonWizard(preset?)` opens the wizard on step 1 with a preset's
    `batteryRunIds` selected (the leaderboard's **Open in Model Comparison**). The charts plot at most
    `MAX_PLOTTED_ENTRIES` (12) models.
  - **Step 1's selection band** (`.mc-wizard-selection`, `role="region"` labelled by
    `#mc-selection-label`; the pattern is `frontend_ui_controls` §8i): the label is the band's one
    `role="status"` and a `tabindex="-1"` focus target, *Your selection — 2 runs and 1 group selected*,
    its text visually hidden while nothing is selected (*Your selection — nothing selected yet*). With
    nothing selected the one visible message is the alert *Nothing is selected yet* / *Select at least
    one completed run, analysis group or battery result in the tables above. Compare stays unavailable
    until you do.* — no hint line beside it — and the footer's and Compare's disabled reason, and the
    state error, read *Select at least one run, analysis group or battery result.* Once something is
    selected, **Clear selection (N)** (`btn-gh btn-gh-cancel btn-gh-small`) leads the chip row; after it
    focus moves to the label. Each chip's remove button is a 32 × 32 `.action-btn` named *Remove
    {detail} {label} from the selection*; removing one moves focus to the next chip's remove button,
    else to Clear selection, else to the label.
  - No preview dialog: step 2 is a workspace with five view tabs — **All charts** (grid), **Single
    chart** (eye), **Interactive table** (table), **Table preview** (image) and **Paired tests**
    (*columns*; `aria-disabled` while fewer than two entries are comparable, its reason as a visually
    hidden suffix; see `app-paired-tests-view` below). When nothing can be
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
  - **Step 3, Reports** is `app-report-pack-panel` (`report-pack/report-pack-panel.component.*`, the
    former Report Pack dialog's body; step 2's **Reports** button and the dialog are gone), headed by
    `h4.gh-section-title` *Reports*, in `app-run-report-frame`'s `layout="sidebar"`: a resizable
    sidebar (20–32rem, at most 40 % of the body, width kept as `sidebarWidth` in
    `localStorage['overseer.benchmark.reportPack']`; stacked below 60rem) holds the form, in order:
    - **Document scope**: a `.gh-tabs-segmented` pair (`role="tablist"` *Document scope*, arrow keys wrap,
      Home / End) *Whole comparison* (a smaller second line, *Recommended*, `.rp-scope-tab-note`) /
      *One model at a time*; each segment wraps its label (`text-wrap: balance`) rather than clipping
      it in a narrow sidebar. Remembered as
      `documentScope` in the same storage record, with an `app-info-tip` in its default hover mode
      (`REPORT_PACK_MODEL_SCOPE_TIP`). Its tab panel holds the next two blocks.
    - **Models**: `app-model-multi-picker` (`frontend_ui_controls` §4e-3), labelled *Models*,
      `chips="none"`, no price or parallel badge, over `comparisonModelOptions(entries)`
      (`report-pack/comparison-model-options.ts`: one option per entry that is not Excluded, keyed by
      entry key, labeled *GPT-5.6 Luna (max)*, the source as `detail` only where two options would look
      identical). Whole comparison: `min` 2, `max` 12 (`MIN_` / `MAX_COMPARISON_DOCUMENT_ENTRIES`), every
      option chosen at first, or the 12 of highest Index with a note saying why. One model at a time:
      `min` 1, the highest-Index option chosen at first. The choice is component state, reset when the
      comparison changes. Under it the `describedBy` line `modelsLine` (*Writes the comparison-wide
      documents.* / *Writes documents for 3 of 5 models — leaves out …* / the per-model documents of the
      chosen models) and, once the preview has lettered them, under Whole comparison only, the letters
      legend (`coveredLetterPairs`, memoized on the preview and the scope): a boxed `role="group"` named by
      its title *Letters in the anonymized copies*, holding a `<dl>` grid of 13 rem columns whose `dt` is
      the letter tile (*Letter A* to a screen reader) and `dd` the model's label. It is not in the
      picker's `aria-describedby`.
    - **Documents of this comparison**: `app-comparison-documents-status`
      (`report-pack/comparison-documents-status.component.*`, view built by `comparisonDocumentsView`):
      whole comparison, three rows for the chosen set and a closed `details.gh-disclosure` *Other model
      sets (n)* (each set's label, rows, and a **Choose these models** `.btn-link`, `aria-disabled` with
      its reason when a model of it is Excluded now or it is outside 2 to 12); one model at a time, a
      group per chosen model. Each row: the document name, a `.gh-tag` *Written* / *Written with
      warnings* / *Not written*, *Comparison changed since written* (`.gh-tag-changed`), and for a
      written row *by <writer> (<provider>; <thinking level>)*, `<time>`, duration, cost and charts
      count, with icon-only **View** (*eye*, the nested `app-pdf-viewer-dialog`) and **Delete** (*trash*,
      `.action-btn-danger`, a nested *Delete Document* confirmation; `aria-disabled` while a job runs).
      Its checkbox reads *Write* (checked by default) or *Rewrite — replaces the current document*
      (unchecked by default), with the row named in visually hidden text; the other sets' rows have
      none. One polite live line. The list comes from the preview, requested 300 ms after the last
      change (`REPORT_PACK_PREVIEW_DEBOUNCE_MS`, `debounceTime` then `switchMap`, so an older answer is
      dropped), and the written documents' list entries from `listReportDocuments({ comparisonId })`.
    - **Charts in PDF and Word**: `app-report-chart-picker` with `[layout]` and `[scope]`, then the
      visible print advisory and, while the `report-charts-location-missing` alert is present, the
      warning *Chart storage is not configured; documents will be written without charts.*
    - The report writer (an `app-model-picker` with the dialog-mode *Choosing a report writer* tip from
      `run-ai-reports/report-writer-advice.ts`; a subject's own model, or a covered model's
      configuration, is refused), the refusal or the amber same-provider warning (*Writer from a chosen
      model's provider*), the `.gh-estimate-panel` estimate (the checked rows summed, per row and model
      when several) with an amber warning from 70 % of the writer's context window
      (`REPORT_PACK_CONTEXT_WARNING_SHARE`), and **Generate** (*zap*), sending `scope`,
      `coveredEntryKeys` or `subjectKeys`, the checked audiences and `replaceDocumentIds`. Its blocked
      reason is a visible note; it stays focusable and `aria-disabled` only when every listed document is
      written and none is checked (`REPORT_PACK_ALL_WRITTEN_REASON`), and one model at a time needs the
      same documents checked for every chosen model (`REPORT_PACK_UNEVEN_MODELS_REASON`).
    - **Preview layout** and **Generate** share `.rp-actions-row`, a grid of
      `repeat(auto-fit, minmax(min(100%, 16.5rem), 1fr))`: both gold `.btn-gh`, full width, Generate
      last; they stack at the default 416 px sidebar and sit side by side from about 34 rem. **Preview
      layout** (*eye*, then a *chevron* state glyph) is a `frontend_ui_controls` §4f trigger:
      `popover="auto"` panel `.rp-preview-menu`, `role="group"`, *Preview the chart layout of a
      document*, one `.gh-action-popover-item` per document type in tab order, checked under
      *Documents* or not (`layoutPreviewItems`, memoized on the selection, the available figures and the
      checked audiences), each *Executive Summary · 2 charts* and `aria-disabled` with *No chart is
      chosen for the …* (`layoutPreviewAudienceReason`). On open (`onPreviewMenuToggle`,
      `refreshAnchorPositioning()`), focus goes to the type the chart picker shows, else the first
      available; Escape closes the popover only. A reason that applies to every type
      (`layoutPreviewGlobalReason`: the charts cannot be drawn here, chart storage is not configured,
      the comparison has no number yet, too few models) makes the trigger `aria-disabled` with no
      `popovertarget`, its reason on a visible line under the row. Choosing a type
      (`previewLayout(audience)`) closes the popover and opens the nested viewer at once
      (`layoutPreviewViewerRequest`, `report-pack/report-chart-layout-preview.ts`), titled *Layout
      preview — Executive Summary*, which composes the figures through the wizard's lent
      `documentChartsComposer` and posts `report-packs/layout-preview` as multipart; figures that could
      not be drawn are listed in a `role="status"` line (*Not drawn: …*, hidden while empty); focus
      returns to the trigger when the viewer closes.

    A same-provider writer makes Generate ask
    the nested *Same-Provider Report Writer* confirmation (**Write Anyway**) on every write; nothing is
    remembered. The main area, *Report pack progress*, holds the job: the stage rail while it runs,
    the stat strip (which stays, with the cost and estimate, after it finishes; elapsed ticks every
    second on the server's clock from `serverTimeUtc`), one row per document (with a *Model* column for a
    per-model job) with its status chip,
    live duration, centered model calls and a charts cell (*Charts: attaching…*, *Charts: 3* — the
    figures attached, `ReportChartRowStatus` `done.count`, while `done.images` counts both namings and
    the diagnostics read *3 attached (6 images, named and anonymized)* where they differ —
    *Charts failed — retry* as a link-style button, *Charts: none*), a *Log and diagnostics*
    disclosure with icon-only **Copy diagnostics** and **Download diagnostics**
    (`report-pack_<subject>_diagnostics_<yyyyMMdd-HHmmss>.txt`, LF, never the starting user), and,
    once finished, a summary with **See the documents** (switches to step 4) and **Dismiss**. Polling
    continues while the step is hidden and stops when the wizard is destroyed.
    `ReportPackContext.entryKeys` is every entry key of the comparison, Excluded ones included, since
    the server keys a document by the request's run and group ids. See
    `docs/overseer/ai-benchmark-report-pack.md` §§ 12 and 15.
  - **`app-report-chart-picker`** (`report-pack/report-chart-picker.component.*`): a group captioned
    *Charts in PDF and Word* holding a `.gh-tabs-segmented` tab row (`frontend_ui_controls` §5) of the
    document types — short labels *Executive* / *Researchers* / *Internal*, each with a count badge of
    the charts selected, or `—` when not checked under *Documents* — over the selected document's
    panel: its full name, **All** / **None**, and one checkbox per figure named *Include <figure> in
    the <document>* with the target section under it. Only the selected document's checkboxes are in
    the DOM; the active segment is component state, never stored. One document type renders no tab
    row, only its panel as a `role="group"`. A `:host` inline-size container stacks each segment's
    label above its badge below 22 rem. A document type not checked under *Documents* stays
    selectable with `aria-disabled` checkboxes and its reason; a figure the comparison cannot draw
    stays listed the same way (*needs three or more models*). The section under each figure follows
    `scope` (`'model'` or `'comparison'`, `REPORT_CHART_PLACEMENTS`). With `[layout]` bound (step 3 only),
    each figure gets a width select (*Full column* / *Two-thirds column* / *Half column*, no visible
    caption, its `aria-label` *Width of … in the …*; beside the figure, and under its title, indented
    past the checkbox, below a 20 rem `rcp` container width) and each document type a
    closed *Layout* `details.gh-disclosure` — *Bar orientation*, *Side by side*, *Label size* (7–10 pt),
    *Maximum height* (40 / 50 / 60 % of the page), *Heading inside the chart*, *GnollBench logo*,
    *Theme* — 1 rem below the figures, with no projected content. A choice that does not fit stays offered with
    `aria-disabled` and *(does not fit)*; choosing it keeps the current value and a `role="status"` line
    says why, and the reasons are listed in the disclosure. It never touches storage: the wizard
    remembers the selection and the layout in `localStorage['overseer.benchmark.reportCharts']`
    (**version 2**, `{ selection, layout }`; version 1 keeps its selection with the default layout),
    defaulting to Intelligence and Intelligence against cost for the Executive Summary, all seven for
    the researcher report, and Intelligence, Speed and Cost for the Internal Improvement Brief, and to
    `DEFAULT_DOCUMENT_CHART_LAYOUT` (*As in step 2*, full column, side by side, 8 pt, 60 %, no heading,
    no logo, *Light, for print*).
  - **Document charts** (`report-pack/report-charts.ts`): the figure keys and their placement labels per
    scope; **charts sized in points** — `documentChartLayout(widthPt, heightPt, labelPt)` (300 dpi,
    `BASE_LABEL_PX` 11, `layoutWidth = widthPt × 11 / labelPt`); `documentTextStyle`, which sets the bar
    and scatter families' axis text to `BASE_LABEL_PX` so the label size holds for both, keeping their
    value labels, point labels and axis titles in proportion and the profile family and chrome as they
    are; and `documentFigureLayout` (the A4 column,
    481.9 pt, `DOCUMENT_CHART_COLUMN_PT`; 16:10 full, 4:3 two thirds, square half, the profile one step
    taller, taller still where the chrome leaves the plot under 200 layout px); `DOCUMENT_MIN_CONTENT_WIDTH`
    260 (step 2's exports use 360), so *Half column* is refused from 9 pt (`documentChartRefusal`);
    `reportDocumentChartLayout` (the manifest `layout`: `widthShare`, `rowGroup` for consecutive half-width
    figures in one section, `maxHeightShare`); `chartSettingsHash` (SHA-256 over canonical JSON of the
    figure style, `documentChartHashLayout` — the column, dpi, base label size, `textNormalization`, format and every document
    type's layout — Show, Highlight, order, measures, pricing basis and the comparison's `computedAtUtc`);
    and **`ReportChartPublisher`**, which composes and uploads one document at a time, named and (with
    peer letters) anonymized variants, `PUT …/report-documents/{id}/charts` for the whole set with its
    layout, records per-document failures, can be canceled after the document in flight, and stops
    once on *Chart storage is not configured*. The wizard owns one publisher and queues step 3's and
    step 4's publishes through it. **`composeReportChart(key, variant)`** on the wizard composes one
    figure off-screen through the same export pipeline as step 2 (`resolveFigureLayout`,
    `composeFigure`, `encodeFigureImage`) for the document type's layout — its width at 300 dpi, the label
    size, the orientation, the heading, the logo and the theme (*Light, for print*:
    `printFigureAppearance`) — with everything else from step 2's active settings; *As in step 2* takes
    step 2's orientation, its *Automatic* resolved at the document chart's own layout width
    (`documentChartOrientation`), and its text through `documentTextStyle` (`documentLook`). A document
    chart has no footer unless the heading is *Title and badges*; step 2's exports keep theirs, which for
    a battery comparison reads *BATTERY* and the battery's name (`FigureFooter.label`; the table's
    provenance row `suiteLabel`). The caption is the figure's detail line, a measure note where the
    document's tables give another measure (`documentChartMeasureNote`: the speed figures' time measure,
    none for the Speed Index; the Cost panel's per-pass cost), and *Drawn from the comparison computed
    …*; the alt text is the title plus one clause per plotted model. The bar panel pads its value-axis
    end by the widest value label (`widestLabelPx`) and the longest whisker's share of the axis. The
    anonymized variant is `anonymizeComparisonForSubject` (`model-comparison/report-chart-anonymize.ts`):
    peers relabeled *Model A…* with the document's own letters, provider and model id removed (neutral
    gray), free text naming a peer rewritten or dropped, unlettered entries dropped, the subject
    unchanged and highlighted. A comparison-scope document's figures plot its covered entries only: the
    named variant is step 2's chart restricted to them (`restrictComparisonToEntries`, Highlight kept
    where it falls among them), the anonymized one `anonymizeComparisonForAll` (every covered entry
    lettered, every other removed). `composeDocumentCharts` (lent to step 3 as `documentChartsComposer`)
    composes a document type's chosen figures with the layout the server would place them by, for the
    layout preview. `chartAdvisory` warns, without changing anything, when a document type's theme is
    *As in step 2* and step 2 uses the dark theme or a transparent background with light text. A
    document written while the wizard is open is charted as soon as its row reaches Completed, with step
    3's selection and layout.
  - **Step 4, Documents** is `app-download-center-panel` placed directly (`idPrefix="mc-dc"`) with a
    `library` context of this comparison's documents (`scope: { kind: 'comparison', comparisonId, name,
    entryKeys }`; the documents of the last step 3 job that finished for this entry set preselected,
    else every row; title *Documents of this comparison*, which only the dialog wrapper would print —
    the panel's own heading reads *Documents of Comparison #12 — <name>* and it offers no *Comparison*
    facet), a `reloadToken` bumped when a job finishes, a document is charted or step 3 deletes one
    (`documentsChanged`), and the wizard's `DownloadCenterChartActions` (see the Download Center entry
    above), which add the *Charts* option and facet, **Update charts…** and the per-card **More actions**.
  - **`app-paired-tests-view`** (`model-comparison/paired-tests-view.component.*`, `mc-paired-` ids):
    step 2's **Paired tests** view, mounted on its first showing and then kept (`pairedViewMounted`),
    fed the comparison, the pricing basis, step 2's Highlight keys and whether it is shown. With fewer
    than two comparable entries it shows only *Comparing needs a second comparable entry…* and where a
    single model's result lives. The toolbar: the mode as a `.gh-tabs-segmented` pair *Against a
    reference* / *All pairs* (`role="tablist"` *Pairs to test*, hidden with two entries; *All pairs*
    `aria-disabled` above 12 entries with `#mc-paired-allpairs-reason`), the **Reference** select
    (`#mc-paired-reference`; the default is the first highlighted comparable entry, else the server's —
    the highest Intelligence Index), an icon-only **Recompute** (*rotate*), and, at the right end, the
    group *Export the paired tests*: icon-only **Copy as Markdown** (*copy*) and **Download**
    (*file-with-arrow*; Markdown or CSV by the Download tab's table format,
    `paired-tests_<yyyyMMdd-HHmmss>`, built by `paired-tests-export.ts`), both `aria-disabled` while there
    is nothing to export. One `role="status"` line (*Computing paired tests…*, then the export outcome).
    The body (`#mc-paired-body`, the mode's tabpanel): the family line (*3 tests against GPT-6.1 Sol ·
    Holm-adjusted*), the single-run caveat (`role="note"`), the measures note, then one non-exclusive
    `details.gh-disclosure` per section — *Intelligence* (*primary test*), *Quality dimensions*,
    *Speed*, *Cost* — open state in `overseer.modelComparison.pairedSections` (try/catch, all open by
    default). Reference mode is a `.gh-datatable` (*Model* as a details button, *Against*, *Paired
    questions*, the difference or ratio with its interval, an `aria-hidden` forest strip on a shared
    scale with the zero (or one) line, *dz*, *p*, *Adjusted p*, *Verdict*); All-pairs mode is the
    lower-triangle `table.mc-paired-matrix` whose cell buttons open the pair, then *Every pair as a
    list*. The opened pair is `section#mc-paired-detail` with `app-paired-test-result`. The request is a
    `switchMap`, so a change of selection, prices, mode or reference cancels one in flight; it is sent
    when the view is shown and the request differs from the last, and always on Recompute (with
    `recompute: true`). The verdict is a word plus a shape — filled dot (established), ring, dash (not
    tested) — and color only repeats it. The About dialog's *Not shown as charts* points *Pairwise
    significance* to this view.
  - **`app-paired-test-result`** (`benchmark/shared/paired-test/`): one pair on every measure — the
    Intelligence headline (difference, interval, p, verdict; suppressed by `[showPrimary]="false"`, as the
    battery report does under its M7 block), then *Quality dimensions*, *Speed* and *Cost* as
    non-exclusive disclosures (`overseer.benchmark.pairedTest.sections`), the kind sentence and, for a
    verification, its changed keys unless `[showKind]="false"` (the run report's tab shows its own), the
    adjustment line when `[showAdjustment]`, and the single-run caveat.
    It takes either a whole `BenchmarkPairComparisonDto` (`comparison`: the run and battery reports) or
    a family's `measures` with the pair's keys and labels (the wizard's details), plus an `idPrefix`.
    The wording, effect and *p* formats, the forest-strip scale and the kind sentences are in
    `paired-test-format.ts`, shared by all three hosts so a verdict never reads *equal*.
  - **Recompute** is icon-only: a `.action-btn` with the rotate glyph,
    `aria-label="Recompute the comparison"` and the tooltip *Recompute this comparison*. A refetch
    whose payload carries the same entry keys (from changing Prices or from Recompute) keeps the
    admin's Show and Highlight choices; a different set of entries reseeds both.
  - **The sidebar follows the view.** Chart views show **Data · Theme · Charts · Download**; table
    views show **Data · Theme · Table · Download**; the Paired tests view shows **Data** alone (Models
    and Prices; *Model order* is not offered, since the paired tests keep the comparison's entry order). A tab in both sets stays selected across a view
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
    - `app-figure-style-panel` (kinds `bar`, `scatter`, `profile`, `timeline` and `appearance`) has
      **two hosts**: these Theme and Charts tabs, with the default `idPrefix="mc-style"` and the
      default `openStorageKey`, `overseer.figureStylePanel.open`; and the Chat Consistency Timeline's
      Theme (`kind="appearance"`) and Charts (`kind="timeline"`) tabs, with `idPrefix="cc-style"` and
      `overseer.benchmark.chatConsistency.figureStylePanel.open`, on a figure style of their own
      (`overseer.benchmark.chatConsistency.figureStyle`). Control ids derive from `idPrefix`, so both
      can be in one document, and a section opened in one host stays closed in the other. The
      `timeline` kind has no *Number format* section; the Timeline adds its own per-chart decimals.
    - **Table**: `app-table-settings-panel` — *Columns* (an `app-reorderable-list` of the 28
      display columns, checkable, *Model* locked, reordered by drag or with the handle's Move menu;
      *Default columns*, *Only columns with values*, *All columns*) and **Row style** (*Shade
      alternate rows*: None / Light / Medium / Strong, the tint being the text color at 5 / 10 / 16 %;
      and *Lines between rows*). One column configuration drives the
      Interactive table, the Table preview and every download and copy; reading formats keep a
      combined column combined, data formats write its parts, and no value is written twice.
    - **Download** holds settings only, no download or copy button: chart views — *Chart size*
      (`app-export-size-section`, id prefix `mc-export`), *Image format* and a hint naming where the
      downloads are; table views — *Table format* (Excel, CSV, TSV, Markdown, JSON, HTML, Image),
      *Table image size* (`app-export-size-section` with *Fit the table*, id prefix
      `mc-table-image`, the *Fit the table* size shown as information in custom mode), the same
      *Image format* and a scope line. `app-export-size-section` and `app-export-format-section`
      (`model-comparison/`) have three hosts: this tab, the key-figures chooser of the run report
      dialogs, and the Chat Consistency Timeline's *Download* tab (id prefixes `cc-export` and
      `cc-image-format`), which keeps its own storage keys —
      `overseer.benchmark.chatConsistency.chartSize` for the chart size, beside
      `overseer.benchmark.chatConsistency.timeline` for the workspace layout and the format, and
      `overseer.benchmark.chatConsistency.launcher` for the launcher — so a change in one never moves
      another.
  - **Toolbar rows**, one per view, each ending in a right-aligned group of icon-only
    `.action-btn`s: *All charts* — zoom, then **Download all charts** (the *download-all* glyph,
    two arrows into one tray); *Single chart* — figure picker, zoom, **Copy** and **Download**;
    *Table preview* — *Comparison table* label, zoom, **Copy table** and **Download table**;
    *Interactive table* — *Comparison table* heading with an `app-info-tip` (*Every entry is
    listed here…*, also the table's `aria-describedby`), **Copy table** and **Download table**,
    sticky while the table scrolls, then one meta line *Computed … · Rows follow the model order: …*
    with a link-style *Use model order* once a header sorts it. Copy table's and Download table's
    names follow the format. The rows stay on one line until the panel is under 44 rem wide: the
    Single chart's Previous / picker / Next group (`.mc-preview-figure-group`) never shrinks or
    wraps, the zoom group is the only one that shrinks (its slider first), and the zoom read-out is
    as wide as its text. Under 44 rem the figure group takes a line of its own and zoom and export
    share the next.
  - **Color means provider** in every figure, the key and the table glyphs:
    `figure-theme.ts` `providerHue` maps Google, Anthropic and OpenAI to the theme's validated
    three-hue palette (in that order) and any other provider to `deEmphasisStroke`. Never give a
    model its own hue — no four- or five-hue set passes the colorblind validator on all pairs — and
    never color by list position or by measure; names travel in direct labels, and the key lists the
    providers present. The Highlight emphasis (gold, the rest gray) still overrides color. Scatter
    marks are circles; the table glyph is a circle in `.mc-glyph-provider-*`.
  - **Trade-off scatters**: the Pareto frontier is a dotted line (`borderDash [0, 6]`, round caps)
    through the frontier models' own points only — no staircase, no extension to the edges, no
    line for a one-member frontier; dominated models are faded to 35 %, never shaded as a region.
    Direct labels (model names, with a surface halo) are on by default and inline values off.
  - **Model profiles are small multiples**: one tile per model on shared scales (intelligence
    linear over the plotted intervals, speed and cost inverted log), the model in its provider's
    hue over gray context lines, a dotted *Ideal* line, values on the points. `buildProfilePlot`
    returns `tiles` and `columns`, and `renderTiledPlotOffscreen` stitches the tiles into the one
    plot every view, download and copy uses. Do not go back to one overlay chart.
  - **All-charts tiles** each carry **Copy**, **Download** and **Open in Single view** top-right,
    shown while the tile is hovered or holds focus and always on devices without hover (opacity
    only). Copy and Download are tab stops; Open is not, since Enter on the tile opens it.
  - **Every chart view shows bitmaps composed by the export pipeline** — `resolveFigureLayout`,
    `renderPlotOffscreen` from each card's Chart.js configuration, then the chrome — the same code
    that writes the downloaded file, so what is on the page is what is downloaded. No
    `BaseChartDirective` renders on step 2 and nothing reads a live chart canvas;
    `renderPlotOffscreen` registers the application's registrables itself (`registerAppCharts` in
    `chart-registrables.ts`), which is what lets a view plot only off-screen, and the component's
    own `ngOnInit` registration is left in place. *All charts* shows every
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

### 4. Layout
- The body (`.form-scroll-area`) is the `model-form` inline-size container, and
  `.model-form-layout` holds four areas in this DOM order: **Connection** (Provider; admin: the API
  key choice, Check Models, custom key), **Model** (catalog listbox, Model ID, Display Name),
  **Settings** (`.mf-settings`: *Model Properties*; admin: *Configuration* with Note, Enabled,
  System Wide and the *Model Role* fieldset) and **Advanced** (`details`, its fieldsets in
  `.advanced-grid`).
- Columns follow the container width, not the viewport. Admin (`.mf-admin`): one column below
  40 rem, `connection model / settings settings` from 40 rem, `connection model settings` from
  70 rem. My Models: one column below 40 rem, `connection settings / model settings` from 40 rem.
  Advanced always spans the full width. Read column by column the order is the DOM order, so no
  `order` property is used.
- Each group stays one column of label-above-field rows; columns separate groups, never the fields
  of one group.
- The body is capped at `max-height: var(--model-form-scroll-max, 65vh)`. A host frame that sizes
  the dialog itself (`.model-form-dialog`) sets `--model-form-scroll-max: none` to lift the cap.

## AI Benchmark Configuration Persistence

In the GnollBench tab (`/admin` -> GnollBench), the settings in the **New Benchmark Run** card (`.setup-card`) must be remembered across page reloads and tab navigations using `localStorage` under the key `'overseer_admin_benchmark_run_settings'`.

The card opens with a primary **Model Under Test** field (`.setup-primary-field`, a gold-accented panel outside every fieldset), followed by three fieldsets on a container-query grid (the `setup` container: one column below 50 rem, two up to 90 rem, three above): *Test Setup* holds the **Run Target** radio group (*Single suite* / *Battery*, a `role="radiogroup"` fieldset), then Benchmark Suite — or, for a battery, the **Battery** select `#batterySelect` — Scoring Profile and Response Style; *Grading* holds Assessor, Co-Assessor, Second Reader or Reference Reader (with its dependent Coverage) and Claim Verifier, plus the *How the graders work* button that opens the grader guide; *Execution* holds Number of Runs (its click tip lists when one run fits and when several do), then — for a series or a battery — a borderless `fieldset.exec-options` whose `legend#execOptionsCaption` (*Series Options* / *Battery Options*, in the `.field-caption` style) holds *Wait when the run cap blocks the next run* and, for a battery, *Reuse earlier runs*, 0.5 rem apart, then *Completion Alerts*: a `role="group"` captioned by `#completionSignalsCaption`, its (i) directly after the caption in `.exec-heading-row`, two `.checkbox-label` checkboxes and **Test sound**. Launcher field labels and group captions share one heading style (0.875 rem, 600), lighter checkbox text below them. Its five pickers are `app-model-picker`s named by `bm<X>ModelLabel` and described by `bm<X>ModelHint`. Each field's hint lives in a click-mode `app-info-tip` (`frontend_ui_controls` § 4b) at the right end of its control, keyed by the old hint id (`suiteHint`, `profileHint`, `bmTestedModelHint`, …), so every `aria-describedby` still resolves; only warnings, advisories, disabled-control reasons and the Start hint stay on screen. The field ids and the picker marker classes (`.tested-model-selector` and the like) are stable, and the specs rely on them.

### 1. Stored Setting Fields (`BenchmarkRunSettings`)
Whenever modifying or extending the benchmark setup form, ensure the following fields are preserved in `BenchmarkRunSettings`:
- **`suiteId`**: Selected benchmark question suite ID.
- **`scoringProfileId`**: Selected scoring profile ID.
- **`testedConfigId`**: Target/candidate model configuration ID.
- **`assessorConfigId`**: Evaluator/assessor model configuration ID.
- **`coAssessorConfigId`**: Co-assessor (panel member B) model configuration ID (or `null` for a single-assessor run). Restored to `null` when the configuration no longer qualifies.
- **`secondOpinionConfigId`**: Second reader (reference reader in a panel run) model configuration ID (or `null`).
- **`secondOpinionMode`**: Explicit second-reader coverage override (or `null` to follow the profile default).
- **`claimVerifierConfigId`**: Claim verifier model configuration ID (or `null`).
- **`reportWriterConfigId`**: Report writer model configuration ID (or `null` for *None — no AI-written reports*).
- **`verboseMode`**: Candidate response style (`false` for concise / production default, `true` for detailed / diagnostic).
- **`runCount`**: Number of runs (`1` for a single run, or `≥ 2` for a replicate multi-run series). In battery mode the same field is **Runs per Suite** and the same `runCount` holds it; there is no second field.
- **`targetKind`**: The **Run Target** radio group (`runTargetKind`): `'suite'` (Single suite) or `'battery'`. Absent (a blob predating it) restores Single suite.
- **`batteryId`**: The battery selected in `#batterySelect` (`selectedBatteryId`), restored only while that battery is listed, unarchived and runnable (no deleted suite, no validation error); otherwise the launcher falls back to Single suite.

### 2. Persistence Lifecycle & Invariants
- **Persisted on change**: Every operator change handler of the launcher calls `persistRunSettings()` (in the run tab through `rememberSettings()`; the desktop-notification checkbox once its permission outcome is applied, so a refused prompt is stored as off), and Start calls it again before the request is sent. A loader's own fallback — a remembered suite that is gone, a configuration that lost its role — does not write, so opening the page never overwrites a remembered value. **A new launcher control must call the save from its change handler.**
- **Pending parts survive an early write**: While `pendingRunSettings` is set, the snapshot (`runSettingsSnapshot()`) takes each list-backed part not yet applied (`suite` → `suiteId`; `profile` → `scoringProfileId`; `configs` → the six configuration ids; `battery` → `targetKind` and `batteryId`) from the pending blob rather than from the live form, so a change made before the lists arrive cannot overwrite a remembered value that has not been applied yet.
- **Immediate vs. List-Backed Restorations**:
  - `restoreRunSettings()` reads from `localStorage` during `ngOnInit()`.
  - Fields not backed by asynchronous lists (`verboseMode`, `runCount`) restore immediately. The launcher's state and these rules are in `BenchmarkLauncherState` (`state/benchmark-launcher.state.ts`); `restoreRunSettings()` runs from `AdminBenchmarkComponent.ngOnInit()`.
  - Number of runs (`runCount`) must be validated to be a positive integer (`≥ 1`, floored). It is clamped against `maxRunCountPerSeries` both upon restoration (if limits are already available) and when the server limits response arrives (the workspace store's `loadRunLimits()` announces it on `runLimitsLoaded$`). In battery mode the bound is `floor(maxMembersPerBattery / K)` instead (`maxRunsPerSuite`), applied by `clampRunCountToTarget()` whenever the Run Target, the battery, the battery list or the limits change.
  - List-backed fields (`suiteId`, `scoringProfileId`, `testedConfigId`, `assessorConfigId`, etc.) are validated against their asynchronously loaded datasets before being applied. If a saved ID no longer exists or a configuration is disabled or lost its `Benchmark` role, it must fall back gracefully to the default rather than leaving a dangling ID.
  - **Four list-backed parts.** `runSettingsApplied` tracks `suite`, `profile`, `configs` and `battery`, and `markRunSettingsApplied()` drops the pending blob only once **all four** have had their turn. The `battery` part is `targetKind` plus `batteryId`, applied by the launcher when the workspace store's `loadBatteries()` announces the battery list on `batteriesLoaded$` (or its failure, which restores Single suite). Dropping the blob after three parts would discard it before the batteries arrive, and the Run Target would never restore. A fifth list-backed field needs a fifth part.
- **Safety Acknowledgments Excluded**: Transient safety gates (such as `acknowledgeSameProvider` and `acknowledgeSameProviderReportWriter`) must NEVER be persisted across sessions, ensuring the warning dialog cannot be silently bypassed. The launcher keeps the acknowledgments given in one start attempt only, by role, and a new attempt starts with none; the AI Reports tab's *Write Anyway* is likewise asked on every write.
- **Reuse Excluded**: The battery launcher's **Reuse earlier runs** checkbox is deliberately **not** persisted and starts unchecked on every load. Reuse is a decision about one start: the runs it would attach depend on the instrument hashes at that moment, and a remembered check would quietly attach old runs to a later battery run.
- **Cap Wait Excluded**: *Wait when the run cap blocks the next run* (`allowCapWait`) is deliberately **not** persisted and starts unchecked on every load. The Series and Battery Projections show whether the planned runs fit under the daily cap, so the operator decides at each start (user decision, 2026-10-03).

## Angular Unit Testing

Always execute unit tests before completing frontend modifications in Overseer. Refer to [`testing_guidelines`](file:///c:/hmp/MobileGnollHackLogger/.agents/skills/testing_guidelines/SKILL.md) for full instructions.

- **Run Single-Run Headless Suite (Preferred)**:
  ```bash
  npm run test:headless
  ```
  *(Or: `npx ng test --no-watch`)*
- **Single Test File (Headless)**:
  ```bash
  npx ng test --include="src/app/chat/chat.component.spec.ts" --no-watch
  ```
- **Build Verification**:
  ```bash
  npm run build
  ```



