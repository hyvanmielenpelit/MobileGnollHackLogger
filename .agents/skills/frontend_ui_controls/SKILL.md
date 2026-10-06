---
name: frontend_ui_controls
description: >-
  Specification for buttons, icon buttons, and tab rows in the Overseer Angular
  frontend and the MobileGnollHackLogger Razor pages. Covers the decorative
  GnollHack image button (.btn-gh) and its variants, icon-only buttons and their
  mandatory accessible names, interest-triggered tooltips, the shared tab widget
  (.gh-tabs / .gh-tab) with its required ARIA semantics and keyboard model, and
  when a control is a tab rather than a button. Also covers the shared data-table
  layer (TableState, app-sort-header, app-table-pager, .gh-datatable) that gives a
  table paging, column sorting and column filtering, and the rules that keep a
  paged table honest about selection; card lists with a faceted filter bar
  (app-filter-facet, search, chips, Sort by, Load more) for rows that are small
  forms; and the shared model picker (app-model-picker) with its
  collapsible-listbox keyboard and ARIA contract. Read before adding or restyling
  any button, icon button, toolbar, tab row, model picker, data table, or card list.
---

# Frontend UI Controls: Buttons, Icon Buttons, Tabs, and Data Tables

This skill is the specification for the control families that make up most of the
Overseer interface. It exists because the first three had drifted: the AI Benchmark admin tab
had re-implemented the button base class from scratch, invented two variant names used nowhere
else, accumulated three competing icon-button vocabularies, and styled a row of tabs as
pill buttons.

§8 covers the fourth family, the shared data table, which was written as one layer precisely so
that it never drifts the same way.

**Related skills**: [`overseer_frontend`](../overseer_frontend/SKILL.md) for the general
frontend rules (Angular structure, global stylesheet ownership, the no-emoji rule,
checkboxes) and [`scss_compilation`](../scss_compilation/SKILL.md) for the
MobileGnollHackLogger SCSS build. Run `modern-web-guidance` first if your harness has it —
both Antigravity and Claude Code do.

---

## 1. Choosing the right control

Answer this before writing any markup. Getting it wrong is not a styling mistake; it
produces a control that lies to assistive technology about what it does.

| The control... | is a | Element |
|----------------|------|---------|
| performs an action, submits, opens a dialog | **button** | `<button type="button">` |
| navigates to a different URL or route | **link** | `<a href>` / `<a routerLink>` |
| switches which panel is shown **in place**, without navigating | **tab** | `<button role="tab">` inside a `role="tablist"` |

> [!IMPORTANT]
> **A control that swaps the content below it is a tab, not a button.** This is the rule
> most often broken here, and it is broken in a way that looks fine. The AI Benchmark
> sub-navigation (`Run Benchmark` / `Run History` / `Manage Suites`) was three
> `.subnav-btn` pill buttons: rounded corners, a border, a filled active background. They
> worked, they just told the user they were three independent actions rather than three
> views of one thing — and told a screen-reader user nothing at all, because a plain
> `<button>` carries no notion of a selected sibling.

Never a `<div>` with a click handler. Never an `<a>` with no `href` standing in for a
button.

---

## 2. Image buttons: `.btn-gh`

`.btn-gh` is the GnollHack decorative image button, defined **once** in
`Overseer/ClientApp/src/styles.scss`. Its identity is a `background-image`
(`/img/decorativebutton-nobg-noglow.webp`, `background-size: 100% 100%`), Cinzel
typography, and a gold `drop-shadow` on hover.

**It is the default for every action that has a visible text label.** If you are adding a
labelled button, it is a `.btn-gh` unless you can say why not.

### The complete variant vocabulary

| Class | Use for | Mechanism |
|-------|---------|-----------|
| `.btn-gh` | The affirmative / primary action — Save, Start, Create, Add | The base gold treatment |
| `.btn-gh .btn-gh-cancel` | Cancel, Close, Done — dismissing without committing | `hue-rotate(180deg)` to blue |
| `.btn-gh .btn-gh-delete` | Destructive actions — Delete, Remove, Cancel Run | `hue-rotate(315deg)` to red |
| `.btn-gh .btn-gh-small` | Dense card and toolbar rows where the 140px default is too wide | Overrides size only |

That is the whole list. A dialog footer is `.btn-gh-cancel` on the left and plain `.btn-gh`
on the right — the gold/blue contrast *is* the primary/secondary distinction, so a separate
"primary" class would be redundant.

```html
<div class="dialog-actions">
  <button type="button" class="btn-gh btn-gh-cancel" (click)="dialog.close()">Cancel</button>
  <button type="button" class="btn-gh" (click)="save()">Save</button>
</div>
```

### Sizing: the label has to clear the end ornaments

The decorative plate is drawn with an ornamental frame and a notched "wing" at each end, and
it is applied with **`background-size: 100% 100%`** — so it *stretches* to whatever box the
button occupies. The ornaments therefore scale with the button: a wide button has wide
ornaments.

Measured from the 855×160 source (`decorativebutton-nobg-noglow.webp`), the flat inner
panel where the label belongs begins **~8.9% of the width in from each end**, and ~13% of
the height down from the top.

Two consequences:

1. **Horizontal padding is generous, and it is an approximation.** `.btn-gh` uses
   `padding: 10px 34px`, which puts the label on the panel edge for the ~340-390px buttons
   this application actually uses. It cannot be exact for every width, because CSS
   percentage padding resolves against the *containing block's* width, not the element's,
   and container query units resolve against an ancestor container rather than the element
   itself. Do not "tidy" this value down — 20px looks correct on a 220px button and visibly
   crowds the ornaments on a wide one, which is the defect it was raised to fix.
   The equilibrium is stable, incidentally: widening the button to fit the padding raises
   the required inset by only 8.9% of the added width, so 34px stays sufficient for labels
   up to roughly 300px of text.
2. **The exact fix, when it is worth doing, is a 9-slice `border-image`** — slicing the
   ornament ends at a fixed size so they stop stretching, after which a small constant
   padding is correct at every width. That changes the rendering of every button in the
   application at once, so it wants its own task and its own visual review.

**Never fix a too-wide button by switching it to `.btn-gh-small`.** A row containing both
gets two button heights and two text sizes, which reads as a rendering fault rather than a
deliberate size choice — it is exactly what made the benchmark suite cards look broken next
to their full-size neighbour. Every one of these rows already has `flex-wrap: wrap`; let it
wrap. `.btn-gh-small` is for a button that stands alone in a genuinely tight space.

### Two prohibitions

> [!CAUTION]
> **Never redefine `.btn-gh` in a component stylesheet.**
>
> Angular's emulated view encapsulation rewrites a component's selectors to match only that
> component's own elements. So a `.btn-gh { ... }` block in `some.component.scss` overrides
> the global class **inside that one component and nowhere else**. The button keeps
> working. Nothing errors. Nothing warns. The component simply stops looking like the rest
> of the application, and because every other page still looks right, the mismatch reads as
> a design decision rather than a bug.
>
> This is not hypothetical: `benchmark.component.scss` carried a 67-line `.btn-gh` block
> that replaced the decorative image with `background: #2a2a2a`, flattening every button in
> that view and all ten of its dialogs. It survived for as long as it did precisely because
> it was invisible from anywhere else.

> [!CAUTION]
> **Never invent variant class names.** The same component added `.btn-gh-primary` and
> `.btn-gh-danger`, which exist nowhere else in the codebase. A developer copying that
> markup to another component gets an unstyled button, and a developer grepping for the
> project's button vocabulary finds two answers.
>
> If a genuinely new variant is needed, add it to `styles.scss` next to the existing ones,
> so it is available everywhere and discoverable by name.

---

## 2b. Compact secondary actions: `.btn-ghost`

When a card or toolbar has one primary action and several secondary ones, the primary is
`.btn-gh` and the secondaries are `.btn-ghost` — a compact, outlined, 32px-tall button — in
one row beside it. `.btn-ghost-danger` marks the destructive one among them.

```html
<div class="suite-card-actions">
  <button type="button" class="btn-gh" (click)="openManageQuestions(suite)">Manage Questions</button>
  <div class="suite-card-secondary">
    <button type="button" class="btn-ghost" (click)="openDifficultyAssessorDialog(suite)"><svg class="btn-icon" ...></svg> Assess Difficulty</button>
    <button type="button" class="btn-ghost btn-ghost-danger" (click)="openBulkDeleteDialog(suite)"><svg class="btn-icon" ...></svg> Delete Runs</button>
  </div>
</div>
```

**Never inside a dialog footer** — there the gold/blue `.btn-gh` / `.btn-gh-cancel` pair
already carries the primary/secondary distinction, and a `.btn-ghost` beside it would be a
third, redundant hierarchy. `.btn-ghost` is for a card or a toolbar with more secondary
actions than a footer ever holds. Icons on a `.btn-ghost` follow the same §3 rules as any
other labelled button.

---

## 3. Icons inside image buttons

### 3a. First decide whether the button gets an icon at all

**An icon is not the default, and it is not decoration. It earns its place only when it
carries information the label does not.** Decide **case by case**, per button. There is no
rule that says "all primary buttons get icons" or "all dialog footers get icons" — applying
either uniformly is how you end up with a gear next to the words "Scoring Profiles", where
it says nothing except that someone was adding icons.

Ask one question: **if you deleted the label, would the glyph still tell the user what this
does?** If yes, it is carrying information — keep it. If the glyph only makes sense *because*
you already read the label, it is noise; drop it.

**Add an icon when the glyph names a recognised operation:**

| Icon | Buttons | Why it carries information |
|------|---------|----------------------------|
| plus | New Profile, Create Suite, Add Question | "Something new appears" — recognised without reading |
| play | Start Benchmark, Acknowledge & Start Run, **Continue — <reason>** (the battery and series progress dialogs) | "This begins now", and it reinforces the consequence of a button that starts real work |
| trash | Delete Runs, Delete All Suite Runs, Delete a report document (`.action-btn-danger`, on each Report Pack row of the Download Center, on each written row of the Model Comparison wizard's step 3 *Documents of this comparison*, and on the run report's AI Reports tab, and the **Delete** of its confirmation) | Destructive. The redundancy is *wanted*: a second signal before an irreversible action |
| pencil (Feather *edit-3*) | **Edit** (a battery card's actions), **Rename comparison** (icon-only `.action-btn` beside *Comparison #N — name* in the Model Comparison wizard's header; opens the nested rename dialog) | "Change this thing's text in place" — it edits a name or a definition and runs nothing |
| refresh / rotate | Refresh, Re-run failed questions, Re-run question, Re-assess question, **Re-run** (the run report's popover trigger, followed by a chevron state indicator), the icon-only **Recompute** of the Model Comparison | "This runs again" — the circular-arrow convention is universal; the repair verbs that use it are in §4g |
| flask | **Try another assessor (does not change the score)** (`.btn-ghost`, each question of the run report) | "An experiment": it records another assessor's verdict beside the score and changes nothing, so it must not look like the repairs beside it |
| undo (curved arrow back) | Reset a settings section to its defaults | "Back to where it started" — distinct from rotate, which means "runs again" |
| file-with-arrow | Download Markdown Report, Download table, **Downloads** (the run report's header; opens the Download Center), **Open Download Center** (the AI Reports tab and the report writing progress dialog), **Download PDF** (the PDF viewer), Download diagnostics (the report writing progress dialog and the Model Comparison wizard's Reports step), **Open Download Center** (`.btn-ghost`, the Model Comparison launcher's *Comparison reports*, `app-report-documents-launcher`), **Download Markdown report** (icon-only, each Run History card) | "A file arrives on your disk" |
| download (one arrow into a tray) | Download one chart | "This one image arrives on your disk" |
| download-all (two arrows into one tray) | Download all charts | "Every chart arrives at once" — the one-chart glyph doubled, so the pair reads as one versus all |
| copy (two rectangles) | Copy figure, Copy the table as Markdown, **Copy diagnostics** (icon-only, the run report's header, the report writing progress dialog and the Model Comparison wizard's Reports step) | "Copies to the clipboard" — nothing is saved to disk |
| eye | Open in Single view, **View** a run's AI-written report (the AI Reports tab; opens the PDF viewer), **View** a report document or a run report (icon-only, each row of the Download Center and each written row of the Model Comparison wizard's step 3; opens the PDF viewer), **Preview layout** (`.btn-ghost`, the *Layout* disclosure of step 3's chart picker; opens the layout preview PDF in the viewer) | "Look at it here" — shows content without changing or downloading it |
| more (three dots in a row) | **More actions** (icon-only, each Report Pack row of the Download Center in the Model Comparison wizard's Documents step; opens a §4f action popover with *Update charts* and *Remove charts*) | "More actions are behind this" — the row keeps its frequent actions as visible icon buttons and puts the rarer, worded ones in the popover |
| external-link (a box with an arrow leaving it) | **Open in new tab** (icon-only, the PDF viewer) | "Leaves this page for a browser tab" — the same content, outside the application |
| map | **View game snapshot** (icon-only, the run report's header) | The game board the suite's questions are asked about |
| layers | Create Default Suites | A stack: several suites are created at once from the built-in catalog |
| upload | Import Suite from YAML, Upload Snapshot | A file leaves the user's disk and enters the application; the arrow points out of the tray |
| zap | Generate Questions, **Generate** (the Model Comparison wizard's Reports step) and its **Write Anyway** confirmation, **Write Report** / **Write Reports** (the run report's AI Reports tab) and its **Write Anyway** confirmation | AI generation: content is produced by a model, not typed in |
| thermometer | Assess Difficulty | A reading on a scale; the button rates how hard each question is |
| heart | Suite Health | The health check; the glyph *is* the concept |
| compass | Snapshot Suite Wizard, Open the Snapshot Suite Wizard, **Open Comparison Wizard** (the Model Comparison launcher), **Open in Model Comparison** (the battery leaderboard dialog; opens the same wizard) | A guided route through several steps: the wizard finds the way, the admin follows it |
| award (a medal over two ribbon tails) | **Leaderboard** (`.btn-ghost`, first in each battery card's actions on the Multi-Suite tab) | "Rankings": it opens the ranked results of the battery's definition |
| columns (two columns side by side) | **Paired tests** (the Model Comparison wizard's fifth step-2 view tab) | Two models side by side, compared on the same questions |
| clipboard | Check Rubrics | A checklist to go through; the rubric is what is being inspected |
| check | Verify All | The same tick the "Reviewed" badge shows, so the button reads as "mark reviewed" |
| star | Set Default | The marker used for the default item elsewhere in the UI; the icon *is* the concept |
| chevron | Show / Hide Model Reasoning | A **state** indicator: which way it points says whether the section is open |
| search (a magnifier) | The leading glyph of a search field (the Download Center's *Search documents*) | Decorative, `aria-hidden`, never a button: the field's label names it, and the glyph says only "type to find" |
| x | Removes an active-filter chip (`.gh-filter-chip`, the Download Center's filter bar) | A dismissal that deletes nothing — the meaning Close already has; the chip's name is *Remove filter {facet}: {value}* |

*Changed 2026-09-12 (harness 24 round): Import Default Suite(s) moved from `upload` to `download` —
importing brings suites from the server's catalog into the application, which is the same
data-arrives-here direction `download` already carries elsewhere, not data leaving it. Download
Markdown Report keeps its meaning but moves to the distinct file-with-arrow glyph, since two buttons
now on the same toolbar cannot both read `download` under the one-glyph-one-meaning rule above.*

*Changed 2026-09-16: the Manage Suites toolbar keeps one `.btn-gh` (Create Suite) and demotes the
other two actions to `.btn-ghost`. Import Default Suites was renamed Create Default Suites and its
glyph moved from `download` to `layers`, since the action creates suites rather than importing a file.
Every `.btn-ghost` on a suite card now carries a glyph so the row scans as one family with Delete Runs.*

*Changed 2026-09-17: the Manage Suites toolbar gains a `.btn-ghost` **Snapshot Suite Wizard** with the
`compass` glyph, after Import Suite from YAML and before the help icon. The suite help's *Open the
Snapshot Suite Wizard* button uses the same glyph, since it opens the same thing.*

*Changed 2026-09-17: trash also covers **removing an attached file** in `app-file-picker` (§4a).
It still means "this thing goes away"; an ✕ was rejected because ✕ already means *close this
dialog*, and it sits in the header of the very dialogs the picker renders in.*

*Changed 2026-09-24: the model comparison's preview became a tab of step 4. Its icon-only view
controls use* maximize *(Fit to screen),* home *(Reset view) and a 1:1 glyph (Actual pixels); the
cards' "open in preview" moved from* maximize-2 *to* eye*, the glyph Run History already uses for
"view".*

*Changed 2026-09-24: the model comparison's Style sections gained a per-section reset icon button
with the* undo *glyph; rotate was not reused, because it already means "runs again".*

*Changed 2026-09-24 (runs 66 and 67 round): the model comparison's Charts / Preview tabs became
**All** (*grid*) and **Single** (*eye*). The eye button on each *All* tile is named "Open <figure> in
Single view". The *All* toolbar's **Fit height** uses* maximize*, the fitting glyph* Single *already
uses for Fit to screen; the new **Figure size** section's reset uses* undo *like the other Style
sections.*

*Changed 2026-09-24: the model comparison's step 3 **Download table** and **Copy as Markdown** became
icon-only `.action-btn`s beside the format select, using* file-with-arrow *and* copy*, with
`interestfor` tooltips and `aria-disabled` when there is nothing to export. The copy glyph, already
used by Copy figure, joined the table above.*

*Changed 2026-09-25: the model comparison became three steps, and its table a view of step 3.
**Move up** / **Move down** in a reorderable list (`app-reorderable-list`, §4c) use Feather*
arrow-up *and* arrow-down *— not the chevrons, which mean* Show / Hide *state; the row's grip is
decorative, and every drag has these buttons as its alternative. The* table *glyph marks the
**Interactive table** view tab and* image *the **Table preview** tab. **Copy table** keeps the*
copy *glyph, and its accessible name and tooltip follow the chosen format (*Copy the table as an
image*, *… as cells for Excel*, *… as a formatted table*, *… as Markdown*). A colour setting is a
**colour row**: a visible label, a native colour input and a hex text field that mirror each other.
The download-time column chooser dialog is gone; the Table tab's **Columns** section replaces it.*

*Changed 2026-09-26: the model comparison became two steps. Its view bar gained **About** — a
`.btn-ghost` with the* help-circle *glyph* About conditions *already uses, naming what explains
this view, with a count badge while caveats exist — and **Recompute**, an icon-only `.action-btn`
with the* rotate *glyph ("runs again"). The Data tab's **Models** table holds a Show and a
Highlight checkbox per model, each named for its model.*

*Changed 2026-09-26: the model comparison's step 2 toolbars became one row per view, each ending in
a right-aligned group of icon-only `.action-btn`s. **Single chart**'s Download is icon-only with the*
download *glyph; **Download all charts** sits only at the end of the **All charts** zoom row, icon-only
with the new* download-all *glyph, which replaces* archive*; **Download table** is icon-only with*
file-with-arrow*, beside Copy table on a **Comparison table** row in both table views. Each All-charts
tile holds Copy, Download and Open in a cluster shown while the tile is hovered or holds focus (and
always without hover); opacity, never `display`, so Copy and Download stay tab stops. **About** and
**Recompute** moved to the step tab row, step 2 only; the view bar is the sidebar toggle and the view
tabs. The Interactive table's lead note became an `app-info-tip` beside its heading, and the settings
sidebar gained an `app-pane-resizer` (§4d).*

*Changed 2026-09-26: a reorderable list's per-row arrow-up / arrow-down **Move up** / **Move down**
buttons were replaced by the row handle's **Move** menu (§4c). The handle is now a focusable button
named* Move <label>*, with a hint tooltip* Drag, or press for move options*.*

*Changed 2026-09-27: the benchmark launcher's field hints moved behind click-mode info buttons
(`app-info-tip trigger="click"`, §4b), each at the right end of its control in a `.gh-field-row`.
Only warnings, advisories, the reason a control is disabled and the reason Start is unavailable stay
on screen. The fieldsets lost their purpose lines, and the Evaluation Purpose & Compliance box was
removed.*

*Changed 2026-09-27 (runs 68–72 round): the model comparison's **Single chart** toolbar keeps
Previous, the figure picker and Next together — `.mc-preview-figure-group` never shrinks or wraps, so
Next can no longer drop under the picker — and the zoom group is the only group on the row that
shrinks, its slider giving way first; the zoom read-out is as wide as its text rather than reserving
room for its longest value. Under 44 rem the row wraps with the figure group on its own line and the
zoom and export groups sharing the next.*

*Changed 2026-09-28 (report packs): the run report dialog's header holds a `role="group"` *Run
actions*: **Downloads** (*file-with-arrow*, opens the Download Center), **Re-run** (*rotate*, then a
*chevron* for the popover's open state; the first action popover, §4f), and the icon-only **View game
snapshot** (*map*, new) and **Copy diagnostics** (*copy*). Model Comparison's **Reports** takes *zap*,
the AI-generation glyph of Generate Questions. Report documents are previewed with *eye* and deleted
with *trash* on an `.action-btn-danger`.*

*Changed 2026-09-28 (key figures and PDF downloads): *copy* and *download* also serve the run report's
key-figures images — icon-only **Copy** and **Download** for the whole strip on the Key figures bar, and a
per-card pair (`app-key-figure-card-actions`) revealed on hover or focus like the All-charts tiles, each
named for its card and run. The Download Center's explanations moved into click-mode info tips (§4b); the
red *Internal only* tag remains the visible short reason a row cannot be chosen.*

*Changed 2026-09-28 (run report tabs): the run report's sections are one `.gh-tabs-secondary` row of ten
tabs without icons (§5, all or none), its panels rendered and `hidden` rather than removed. The key-figures
disclosure bar is gone: the whole-set **Copy** and **Download** sit beside the *Key figures* heading of
the Summary tab. The AI Reports tab lost its own **Downloads** button; the header's **Downloads** is the
one entry to the Download Center.*

*Changed 2026-09-29: `app-model-picker` options can carry a `tag`, a role chip before the model
name (`.model-option-tag`, global). The run report's* Compare against *uses it: Assessor A,
Co-assessor B and Panel, each with its model.*

*Changed 2026-09-29 (AI Reports tab and PDF viewer): the run report's AI Reports tab gives* zap *to
**Write Report** / **Write Reports** and to the same-provider confirmation's **Write Anyway** — both
make a model write — *eye* to **View**, which now opens the in-app PDF viewer rather than a browser
tab, and* trash *to each document's icon-only **Delete** (`.action-btn-danger`) and its confirmation.
**Open Download Center**, the viewer's **Download PDF** and the progress dialog's **Download
diagnostics** take* file-with-arrow*, and **Copy diagnostics** there takes* copy*. The viewer adds one
new glyph,* external-link*, for the icon-only **Open in new tab**: it leaves this page for a browser
tab, which neither* eye *(look here) nor* file-with-arrow *(save to disk) says. Dismissals — **Keep
It**, **Keep Writing**, **Run in Background**, **Done** — and **Cancel Writing** stay text-only.*

*Changed 2026-09-29 (Report Pack dialog and Comparison reports): the Report Pack dialog lost its
Markdown preview. Its documents, and the Model Comparison launcher's* Comparison reports*, are one
shared table, `app-report-document-library`: **View** (*eye*) opens the in-app PDF viewer, **Download**
and the toolbar's **Download…** (*file-with-arrow*) open the Download Center, and **Delete** (*trash*,
`.action-btn-danger`) asks its confirmation. The dialog's same-provider acknowledgment checkbox became
the AI Reports tab's nested confirmation, **Write Anyway** (*zap*), and **Generate** keeps* zap*. The
launcher's **Open Comparison Wizard** takes* compass*, like the Snapshot Suite Wizard, and is the page's
only `.btn-gh`. The estimate panel of both writers is the global `.gh-estimate-panel`.*

*Changed 2026-09-30 (Model Comparison steps 3 and 4, charts in PDF and Word): the Report Pack dialog
and `app-report-document-library` are gone. The dialog's form became the wizard's step 3, *Reports*
(`app-report-pack-panel`), where **Generate** and its **Write Anyway** keep* zap*, and the log's
icon-only **Copy diagnostics** and **Download diagnostics** take* copy *and* file-with-arrow*. Model
Comparison's step-2 **Reports** button is gone with the dialog. The documents table moved into the
Download Center itself (`app-download-center-panel`, the §8 data-table layer), which is the wizard's
step 4, *Documents*, and the body of the Download Center dialog: each row's icon-only **View** (*eye*)
opens the PDF viewer and **Delete** (*trash*, `.action-btn-danger`, Report Pack documents only) asks its
confirmation; there is no per-row Download, since selecting the row is how a document is downloaded. In
the wizard only, each Report Pack row adds an icon-only **More actions** with a new glyph,* more *(three
dots), opening a §4f action popover of *Update charts* and *Remove charts*, each `aria-disabled` with its
reason on a second line; the toolbar's text-only `.btn-ghost` **Update charts…** opens a nested dialog
with the chart picker. The launcher's *Comparison reports* (`app-report-documents-launcher`) is a summary
and one `.btn-ghost` **Open Download Center** (*file-with-arrow*), `aria-disabled` while there is
nothing to open, with the summary line as its reason. The Database tab's **Clear Chart Files** is a
text-only `btn-gh btn-danger`, `aria-disabled` with a tooltip reason.*

*Changed 2026-09-30 (Download Center cards): the Download Center's documents table became a card list
(§8h). Its `app-filter-facet` triggers end in the* chevron *state glyph, pointing up while open; the
search field leads with a decorative* search *glyph; each active-filter chip ends in an* x*, accepted
here because removing a filter chip deletes nothing — it dismisses a view setting, which is what ✕
already means as* close*. (Removing an attached file keeps* trash*, since that does delete something.)
**Show N more** and **Show all** are text-only, and **Update charts…** moved from the toolbar to the
selection bar, still text-only.*

*Changed 2026-09-30 (run-75 round, key-figures chooser): the run report's *Key figures* head gains a
third button, **Choose figures**, a text-only `.btn-ghost` (*(9 of 12)* appended while a subset is
selected); the icon-only **Copy** and **Download** keep* copy *and* download *and export the saved
selection in one click. The chooser it opens is a nested `.gh-dialog` (§4b nested-dialog rule, light
dismiss): **Cancel** is `btn-gh btn-gh-cancel`, and **Copy Image** and **Download PNG** are `btn-gh`
with* copy *and* download*, both `aria-disabled` with the visible reason *"Select at least one figure."*
while nothing is selected. It is a dialog, not an action popover (§4f): a multi-select checklist with a
commit.*

*Changed 2026-09-30 (key-figures filter): the chooser, now* Choose key figures*, filters the Summary
cards as well as the image and applies each change at once; its footer is a single text-only **Done**
(`btn-gh btn-gh-cancel`) and its header a close `.btn-icon-action` with a* Close *tooltip (§4.2). Copy
Image, Download PNG and Cancel are gone; the head's icon-only Copy and Download export the selection.*

*Changed 2026-09-30 (run report header settings): *Choose key figures* gains a second checklist,
*Image details*, choosing the run settings the images carry. Its **All** / **None** are the same
text-only `.btn-link`s as the figures group's, with visually hidden completions naming their group
(*image details* / *of the image details*) so the two pairs have distinct names (§4.1). No new button
class or glyph.*

*Changed 2026-09-30 (Run History cards and a compact run report header): Run History became a card
list (§8h). Each card's icon-only **Download Markdown report** moved from the one-arrow* download *glyph,
which is reserved for "one chart", to* file-with-arrow*, the glyph every other Markdown report download
uses. The degraded-answer count in a card's kicker is a 12 px Feather* alert-triangle *SVG
(`aria-hidden`), the number and visually hidden *degraded answers*, replacing the ⚠ character and its
`title` (the no-emoji and no-`title` rules). The list head's **Refresh** is a compact `.btn-ghost` with*
rotate*, no longer a full-size `.btn-gh` (§2b). In the run report header, **Close** left the* Run
actions *group, a dialog control rather than a run action; the group is a two-column grid of*
Downloads *and* Re-run *beside the icon-only* View game snapshot *and* Copy diagnostics*. No new button
class.*

*Changed 2026-10-01 (Run History head): the list head's Runs help moved from a click-mode info tip
to a dialog-mode one titled About the run history (§4b), because its five `<dl>` entries overflowed
the popup. The head is one `align-items: center` row and the heading's text box is trimmed to its
caps. No new button class or glyph.*

*Changed 2026-10-03 (battery cards, leaderboard dialog and paired tests): a battery card's actions gain
**Leaderboard** first, with a new glyph,* award *(Feather: a medal over two ribbon tails), meaning
"rankings" — it opens the full-screen leaderboard dialog; no other control uses it. The card's **Edit**
keeps its* pencil *(Feather* edit-3*), **Delete** keeps* trash *on a `btn-ghost-danger`, and **Archive** /
**Restore** stay text-only. The leaderboard's **Open in Model Comparison** takes* compass*, since it opens
the same wizard as **Open Comparison Wizard**, and its icon-only **Refresh**, rotate. The Model
Comparison wizard's step-2 view row gains a fifth tab, **Paired tests**, with the new* columns *glyph (two
columns side by side): the row's other tabs all carry icons, so the new one needs one (§5, all or none).
Its toolbar's icon-only **Recompute**, **Copy as Markdown** and **Download** reuse* rotate*,* copy *and*
file-with-arrow*. Run History's battery cards reuse* eye *(**View details**),* file-with-arrow *(**Download
Markdown report**), the* activity *glyph the battery progress button already used (**Show progress**) and*
trash *(**Delete battery run**). The Battery Run Report's header mirrors the run report's: **Downloads**
(*file-with-arrow*), **Actions** (*rotate* plus a* chevron*, a §4f popover) and icon-only **Copy
diagnostics** (*copy*). The paired tests' **Compare** buttons are text-only `.btn-gh`: a plain commit.*

*Changed 2026-10-05 (repair and retry actions, §4g): the repair labels are sentence case. **Re-run
question** and **Re-assess question** take* rotate*; **Try another assessor (does not change the
score)** is a plain `.btn-ghost` with a new glyph,* flask*, since it is an experiment rather than a
repair (the `.btn-gh-trial` variant is gone). **Continue — <reason>** takes* play *in the battery and
series progress dialogs; **Re-run under current instrument**, **Recompute analysis** and *Continue
anyway (marks the group cross-condition)* are text-only.*

**Leave the icon off when the label is already the whole message:**

| Buttons | Why no icon |
|---------|-------------|
| **Done, Close, Cancel** | Dialog dismissal. The word is unambiguous in every language and context, the button's position in the footer already says "this ends the dialog", and `.btn-gh-cancel`'s blue already distinguishes it. A tick or an ✕ adds a second thing to look at and no meaning. |
| **Save Profile, Save Suite, Save Question** | A plain commit. "Save X" cannot be misread. A floppy-disk glyph is a skeuomorph for hardware most users have never seen, and it does not say *what* is being saved — the label does. |
| **Scoring Profiles, Manage Questions** | Opens a management view. The only available glyphs are generic — a gear reads as "application settings", which this is not; a book reads as nothing in particular. Both mislead slightly and inform not at all. |
| **Assess Question Difficulty, AI Auto-Rate All Difficulties** | The label already names the operation *and* says it is the AI one. These carried a circle-with-diamond and a lightning bolt; neither survives the test — a bolt alone could mean AI, or fast, or power. "Marks the AI action" is not enough justification when the word "AI" or "Assess" is right there. (The suite card's `Assess Difficulty` is the exception: it sits in a `.btn-ghost` row where every sibling has a glyph, and the thermometer names the scale being rated.) |

The pattern behind the second table: **dismissals and plain commits go text-only.** But that
is a consequence of the test, not a rule to apply mechanically — a footer button naming a
*distinct consequential operation* still earns its icon, which is why
`Delete All Suite Runs` (destructive), `Acknowledge & Start Run` (begins real work), and
`Assess Difficulty` (spends AI tokens) keep theirs while `Cancel` and `Save` next to them do
not. In a footer, that asymmetry is a feature: the icon marks the button that *does*
something.

Two further rules:

- **One glyph, one meaning, across the whole application.** If trash means delete, nothing
  else may use trash. Reusing a glyph for a second meaning costs more than having no icon.
- **Never add an icon to fill space or to balance a row.** Two buttons side by side, one with
  an icon and one without, is correct whenever only one of them has something to show.

### 3b. The markup contract

Once you have decided a button gets an icon:

```html
<button type="button" class="btn-gh" (click)="save()">
  <svg class="btn-icon" xmlns="http://www.w3.org/2000/svg" width="16" height="16"
       viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
       stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
    <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"></path>
    <polyline points="17 21 17 13 7 13 7 21"></polyline>
    <polyline points="7 3 7 8 15 8"></polyline>
  </svg>
  Save Profile
</button>
```

- **16×16, `viewBox="0 0 24 24"`, `fill="none"`, `stroke="currentColor"`,
  `stroke-width="2"`, round caps and joins.** This is the Feather icon geometry the rest of
  the application uses; matching it is what makes a new button look like it belongs.
- **`class="btn-icon"`** — sizes the icon and applies the −0.5px baseline nudge that centres
  it against Cinzel, which sits lower than most faces. Do not hand-roll the sizing.
- **`aria-hidden="true"`, always.** The visible label already names the button. An
  un-hidden decorative icon gets announced too, so the user hears the name twice.
- **Icon first, label second.** Spacing comes from the button's own `gap: 8px` — never a
  margin on the icon.
- **No emoji.** Carried from `overseer_frontend`: they misalign, they render differently per
  OS, and they cannot inherit `currentColor`.

> [!NOTE]
> **Icon-only buttons are a different case entirely.** Everything in §3a is about whether a
> *labelled* button also needs a glyph. An icon-only button has no label, so its icon is the
> whole control and is never optional — see §4, which adds requirements rather than removing
> them.
>
> **Tabs are a third case.** Icons in a tab row aid scanning between a small fixed set of
> destinations, so the "would the glyph work without the label" test is not the right one
> there. Either give every tab in a row an icon or give none of them one; a row with some
> icons and some not looks broken.

---

## 4. Icon-only buttons

For actions in table rows, card headers, and dialog title bars, where a label would not fit.

| Class | Use for | Size |
|-------|---------|------|
| `.action-btn` | Row and card actions — edit, duplicate, download, view | 32×32 |
| `.action-btn .action-btn-danger` | Destructive row actions | 32×32, red hover |
| `.btn-icon-action` | Dialog close buttons | 44×44 |
| `.btn-icon-action.delete` | Destructive, where a 44px target is wanted | 44×44, red hover |

All four are defined in `styles.scss`. Pick one; do not add a fifth.

### Two hard requirements

**4.1 — A distinct `aria-label`, naming the subject and not just the verb.**

An icon-only button has no text, so `aria-label` *is* its accessible name. A screen reader
lists controls by name, and a list of eleven controls named "Delete" is unusable. Include
what is being acted on, interpolating bound values:

```html
<!-- Wrong: three buttons per row, all named the same thing. -->
<button type="button" class="action-btn" aria-label="Edit">...</button>

<!-- Right: names the row. -->
<button type="button" class="action-btn"
        [attr.aria-label]="'Edit suite ' + suite.name">...</button>
```

The same applies to repeated dialog close buttons: `"Close run details"`,
`"Close question form"` — not ten buttons named `"Close dialog"`.

**4.2 — The tooltip is `interestfor` + `popover="hint"`, never `title`.**

`title` is prohibited twice over: it is not a valid naming mechanism (WCAG, and the
`modern-web-guidance` accessibility guide say so explicitly), and it cannot be styled, so it
never matches the application. It also does not appear on keyboard focus, only hover.

```html
<button type="button" class="action-btn"
        [attr.aria-label]="'Edit suite ' + suite.name"
        [attr.interestfor]="'tip-edit-suite-' + suite.id"
        [attr.style]="'anchor-name: --tip-edit-suite-' + suite.id">
  <svg class="btn-icon" ... aria-hidden="true">...</svg>
</button>
<div popover="hint" class="gh-tooltip" [id]="'tip-edit-suite-' + suite.id"
     [attr.style]="'position-anchor: --tip-edit-suite-' + suite.id">Edit suite</div>
```

Four things about that snippet are load-bearing:

1. **`popover="hint"` gives WCAG 1.4.13 for free** — dismissible with Escape, hoverable
   without vanishing, persistent until focus or hover leaves. Do not re-implement any of it.
2. **Do NOT add `role="tooltip"`, `aria-describedby`, or `aria-details`.** `interestfor`
   wires all three implicitly, switching between `describedby` and `details` depending on
   whether the tooltip holds interactive content. Setting them by hand fights the browser.
3. **Both ends must name the anchor explicitly.** `interestfor` establishes an *implicit*
   anchor natively, but the `@oddbird/css-anchor-positioning` polyfill does not support
   implicit anchors — so Firefox and Safari get no positioning at all without the explicit
   `anchor-name` / `position-anchor` pair. Derive the name from the entity's primary key so
   it is unique per row.
4. **Use `[attr.style]`, not `[style.anchor-name]`.** Angular's style binding calls
   `CSSStyleDeclaration.setProperty()`, which **silently discards properties the browser
   does not recognise**. In exactly the browsers that need the polyfill, `anchor-name` is
   unrecognised, so the property never lands in the CSSOM *or* the attribute text, and the
   polyfill has nothing to read. `[attr.style]` writes literal attribute text, which both
   native engines and the polyfill parse. (This only works because these elements have no
   other inline styles — if one needs them, put them in the same bound string.)

### Polyfills

Call `ensureOverlayPolyfills()` from `app/utils/polyfills.util.ts` in the component's
`ngOnInit`. It feature-detects and dynamically imports `@oddbird/popover-polyfill`,
`interestfor`, and `@oddbird/css-anchor-positioning` — all three already in
`package.json`, all three code-split into their own lazy chunks, so a supporting browser
downloads none of them. Never import them unconditionally.

> **Styling caveat.** The popover polyfill cannot define the real `:popover-open`
> pseudo-class and applies a `.\:popover-open` class instead. Any rule targeting the open
> state must combine both — `:is(:popover-open, .\:popover-open)` — because a browser that
> does not understand `:popover-open` discards the entire rule, not just that selector.

### 4a. File pickers: `app-file-picker`

A single-file upload is `app-file-picker` from `app/shared/file-picker/`. **Never a bare
`<input type="file" class="gh-input">`**: the native control reads *No file chosen* while the
host holds a file (hosts reset its value so the same file can be picked again), and its
*Choose File* button stays beside the attached file.

```html
<app-file-picker [inputId]="idPrefix + '-file'" label="YAML file"
                 accept=".yaml,.yml,text/plain" acceptHint=".yaml or .yml, up to 2 MB"
                 [fileName]="fileText !== null ? fileName : null" [fileDetail]="fileSizeLabel"
                 [error]="fileError"
                 (fileSelected)="loadFile($event)" (cleared)="clearFile()" />
```

- **Presentational.** The host reads the file and owns its state; a non-null `fileName` is what
  switches the picker to its attached state. Keep the host's own size limit and error text.
- **Empty state**: a dashed drop zone that is the `<label>` of a real file input. Clicking
  anywhere on it, or Enter on the focused input, opens the native picker; drag and drop is an
  enhancement on top, and a dropped file is checked against the `.ext` tokens of `accept`, which
  the browser does not apply to a drop.
- **The input is hidden by the component's own clip-path rule, never by the global
  `.visually-hidden`.** That class is `:where(:not(:focus-within, :active))` — it un-hides on
  focus, skip-link style, so the native control would pop into view when tabbed to. Never
  `display: none` either: the input must stay focusable.
- **Attached state**: the zone and the input are not rendered. A card shows the name and
  `fileDetail`, with an `.action-btn-danger` **trash** button named *Remove {file name}* and a
  *Remove file* tooltip (§4.1, §4.2). Focus moves to the card after a file is attached — not to
  the remove button, so Enter cannot delete what was just attached — and back to the input
  after removal. One `role="status"` line announces both.
- `inputId` must be unique in the document: the card (`{inputId}-card`), the remove button,
  the tooltip and both anchor names derive from it. A host that focuses the picker after a
  failed step targets `#{inputId}` and then `#{inputId}-card`.
- The chat composer's hidden multi-file attachment input is a different control and does not
  use this component.

### 4b. Info buttons: `app-info-tip`

A hint that would otherwise be a paragraph under a control — what a setting does, why it is
disabled — goes into `app-info-tip` from `app/shared/info-tip/` when the panel is dense enough
that the paragraphs would bury the controls.

```html
<input type="checkbox" id="mc-style-bar-filledBars" aria-describedby="mc-style-bar-filledBars-tip" ...>
<app-info-tip tipId="mc-style-bar-filledBars-tip" subject="Filled bars">{{ filledBarsHint }}</app-info-tip>
```

- **The button is `.gh-info-btn`**: a bare 14 px Feather *info* glyph in a **24×24 px** target
  (WCAG 2.5.8), defined in `styles.scss`. Its name is *About {subject}*, so several tips in one
  panel are told apart (§4.1). It is not a fifth action-button class: it performs no action and
  is only ever rendered by `app-info-tip`.
- **The tooltip is the §4.2 pattern**: `interestfor` + `popover="hint"`, with the
  `.gh-tooltip-multiline` modifier and explicit anchor names. The component writes all of it and
  calls `ensureOverlayPolyfills()`; `tipId` must be unique in the document, since it is both the
  tooltip's `id` and the anchor name.
- **The control keeps its description.** Its `aria-describedby` points at `tipId`.
  `aria-describedby` reads hidden content, so a screen-reader user tabbing through the controls
  still hears every hint without finding the (i) button. This is the control's own attribute,
  not one added to the tooltip, so §4.2's second rule is not broken.
- **Short text only.** One or two sentences. Content longer than a multiline tooltip holds, or
  content with interactive steps, belongs in a dialog.
- **A tip may explain static text.** After a badge or a read-out that is not a control (the
  coverage badge of the run and multi-run progress rosters), nothing carries `aria-describedby`;
  the (i) button is the way in, and its name says what it explains.

**Click mode: `trigger="click"`.** The default, `trigger="hover"`, is the tooltip above. Choose
click mode for an explanation the operator asks for, in a form where hovering is not the norm — a
launcher or a settings form whose hints moved off the page. It holds two or three sentences or a
short list (a `<dl>` of options, say); a multi-step or interactive explanation still belongs in a
`<dialog>`.

```html
<div class="gh-field-row">
  <select id="profileSelect" class="gh-input" aria-describedby="profileHint">...</select>
  <app-info-tip trigger="click" tipId="profileHint" subject="Scoring Profile">...</app-info-tip>
</div>
```

- **The button toggles a popup** by `popovertarget="{tipId}-popup"`, not `commandfor`: it is
  Baseline, and the popover polyfill implements it. The button adds `.gh-info-btn--click`
  (pointer cursor, gold while open).
- **The popup is `popover="auto"`**, non-modal and light-dismiss: Escape, a click outside it, or a
  second click on the button closes it. It has **no close button, no `role`** (focus stays on the
  button and nothing inside is interactive, so `dialog` would promise behavior it lacks) **and no
  `interestfor`**. It is `.gh-info-popup`, right-aligned under the button (natively, where that
  does not fit, it takes the §4f fallback chain), titled by the subject.
- **A list of options is a `<dl>` with each pair grouped in a `<div>`**, the label in a
  `<span class="gh-info-term">`; the global styles draw a hairline between groups. A term
  that needs a status marker (*Recommended*) gets a `<span class="gh-info-badge">` after the
  label inside its `<dt>` — a word, never a color alone. An intro paragraph before the list and a
  closing note after it get the same hairline.
- **The ids**: `{tipId}` is still the element holding the text, so the control's
  `aria-describedby` contract above is unchanged; the popup is `{tipId}-popup`, labelled by its
  title `{tipId}-title`, which is outside the description.
- **`aria-expanded` is bound by hand** from the popup's `toggle` event, because the popover
  polyfill does not set it. On open the component calls `refreshAnchorPositioning()`.
- **`.gh-field-row`** (global) puts the (i) at the right end of a select, a picker or an input:
  a flex row whose first child takes the remaining width. Do not wrap a checkbox label in it —
  the label would stretch, and with it the checkbox's click target. Nor a group caption: the caption
  would stretch and push the (i) to the far edge. A caption's (i) follows its text in a plain flex row
  (`.exec-heading-row` in the benchmark launcher).
- **Visible text stays visible.** A warning, an advisory, the reason a control is disabled, or a
  note that changes the decision (a condition that currently holds) is not moved into the popup.
- **The popup caps its height** at `min(32rem, 100dvh - 32px)` and scrolls; natively the §4f chain
  keeps it inside the viewport, capped to the free space. Content that needs the scroll is a sign it
  belongs in dialog mode.

**Dialog mode: `trigger="dialog"`.** For an explanation longer than a click tip holds — several
paragraphs, or a `<dl>` of long entries (the report writer advice in the AI Reports tab, the three
disclosure levels in the PDF viewer and the Download Center, and the Run History list head's *Runs*
help).

```html
<app-info-tip trigger="dialog" tipId="rrReportWriterHint" subject="Report writer"
              dialogTitle="Choosing a report writer">...</app-info-tip>
```

- **The button** is `.gh-info-btn gh-info-btn--click` named *About {subject}*, with
  `aria-haspopup="dialog"` and no `popovertarget`; a click calls `showModal()`.
- **The dialog** is `<dialog class="gh-dialog gh-info-dialog" closedby="any">`, rendered inside the
  `app-info-tip` host and kept in the DOM while closed. It is labelled by its `<h3 id="{tipId}-title"
  tabindex="-1">` (`dialogTitle`, else `subject`), which is focused on open; a close
  `.btn-icon-action` named *Close {title}* has an `interestfor` tooltip (`{tipId}-close-tip`); the
  scrolling body `.gh-info-dialog-body` is `{tipId}` and shares `.gh-info-popup-body`'s `<dl>` rules.
  Every id derives from `tipId`. A backdrop click closes it where `closedby` is unsupported.
- **Nested-dialog rule**: its `close`, `cancel` and `click` events stop propagating, because these
  tips sit inside other dialogs (run report, PDF viewer, Download Center) and inside table headers.
  Escape closes only the tip. Do not put a dialog-mode tip inside a `<label>`: stopping propagation
  cannot prevent label activation.
- **No `aria-describedby` at `tipId`.** The text is long; a control that pointed at it would read
  paragraphs on every focus. The (i) button is the way in.
- It resets the typography it would inherit (weight, size, color, white-space, text-transform,
  letter-spacing), so it reads as body text even inside a `<th>`.

### 4c. Reorderable lists: `app-reorderable-list`

**The** component for letting a user put items in an order — the model comparison's custom model
order and its table columns both use it. Reach for it rather than writing a third drag
implementation. It lives in `app/shared/reorderable-list/` and knows nothing about its callers.

- **Every drag has a button alternative.** Each row's **handle** is a `<button>` named *Move
  <label>*. Dragging it reorders with a pointer. Pressing it (click, tap, Enter or Space) opens the
  list's one shared **Move** popover: *Move to top*, *Move up*, *Move down*, *Move to bottom* — the
  keyboard, switch and single-pointer path (WCAG 2.2 §2.5.7, §2.1.1). A press becomes a drag only
  after 4 px of movement, and the click that ends a drag does not open the menu. *Up* / *down*
  keep the menu open with focus on the option, moving to the opposite option at an end, where the
  pressed one becomes `aria-disabled` (never `disabled`, so it stays focusable). *Top* / *bottom*
  close the menu and focus the handle, as Escape does. The popover sits outside the rows, so a
  move never disconnects it.
- **One announcement per committed move**, in the component's own `role="status"` line: *<label>
  moved to position <n> of <total>.* Nothing is announced mid-drag.
- **It never reorders its own input.** It emits `orderChange` (on drop, never per pointer move) and
  `checkedChange`, and renders what the parent hands back. Motion during a drag is transforms only.
- Options: `checkable` rows with a `.checkbox-label` checkbox (a `locked` item is checked, disabled
  and explained with an `app-info-tip`), small tags, an `itemTemplate` for a richer row body, and a
  non-interactive divider at `dividerIndex`.
- `@angular/cdk` drag-drop is not used: it offers no keyboard or single-pointer path.

### 4d. Pane resizers: `app-pane-resizer`

**The** splitter for letting a user widen or narrow a side pane — the model comparison's settings
sidebar uses it. It lives in `app/shared/pane-resizer/` and implements the WAI-ARIA *window
splitter* pattern on its host element.

```html
<app-pane-resizer controls="mc-fig-sidebar" label="Resize settings sidebar"
                  [value]="sidebarWidth" [min]="288" [max]="sidebarWidthMax" [defaultValue]="416"
                  (valueChange)="onSidebarWidthChange($event)"
                  (valueCommit)="onSidebarWidthCommit($event)"></app-pane-resizer>
```

- **ARIA on the host**: `role="separator"`, `aria-orientation="vertical"`, `tabindex="0"`,
  `aria-controls` (the pane's `id`), `aria-label`, and `aria-valuenow` / `-valuemin` / `-valuemax`
  as whole CSS pixels with `aria-valuetext` *"<n> pixels"*.
- **It keeps no copy of the width.** Every emitted value is clamped to `[min, max]`; the parent
  stores it and passes `value` back. `valueChange` is live (at most once per animation frame while
  dragging); `valueCommit` fires once, on release or after a key — persist on commit only.
- **Keyboard**: ArrowLeft / ArrowRight by `step` (16 px), with Shift by `largeStep` (64 px); Home to
  `min`, End to `max`. Any other key passes through. There is no Enter-to-collapse: a pane that
  collapses has its own disclosure toggle.
- **Pointer**: pointer events with `setPointerCapture`, primary button only; double-click resets to
  `defaultValue`.
- **Placement**: a 12 px track of its own between the pane and its neighbor, never overlaid on
  either. An overlay covers the pane's scrollbar on one side, and on the other a sticky header
  paints over it. Drop the pane's own edge border, so there is one line, not two. The line and
  grip turn gold on hover, focus and drag. Hide it
  where the pane is stacked rather than side by side, and do not render it while the pane is
  collapsed. The 12 px target is below WCAG 2.5.8's 24 px, accepted because it spans the full
  height and the keyboard path and the collapse toggle exist.
- **A `max` measured from the layout** is measured when the handle is grabbed or focused, never in
  a getter bound during change detection, which would fault in development mode.
- The question-generation dialog's older splitter (mouse and touch events, no keyboard) has not
  been migrated to it yet.

### 4e. Model pickers: `app-model-picker`

**The** control for every choice of an AI model — the benchmark launcher and its dialogs, Suite
Health, question and description generation, the chat composer and the Models page. Never
hand-roll a `.custom-model-selector` block again: the fifteen copies it replaced had no keyboard
support and, in the composer, no accessible name. It lives in `app/shared/model-picker/` and
implements the WAI-ARIA *collapsible listbox* pattern: a `<button aria-haspopup="listbox">` that
opens a popup `role="listbox"`, which takes focus and tracks the active option with
`aria-activedescendant`.

```html
<label id="bmCoAssessorModelLabel">Co-Assessor <span class="field-optional">Optional</span></label>
<app-model-picker class="co-assessor-model-selector"
                  labelledBy="bmCoAssessorModelLabel" describedBy="bmCoAssessorModelHint"
                  noneLabel="None — single assessor" [showPrice]="true"
                  [options]="benchmarkPickerOptions" [selectedKey]="coAssessorConfigId"
                  (selectionChange)="selectCoAssessorModel($event.model)"></app-model-picker>
<span id="bmCoAssessorModelHint" class="form-hint">…</span>
```

| Input | Meaning |
|-------|---------|
| `options` | `ModelPickerOption[]` — `{ key, model, group?, tag? }`. Build them with `toModelPickerOptions(models, group?, keyPrefix?)`, from a getter memoized on its source, never a fresh array per change-detection pass |
| `ModelPickerOption.tag` | Optional. A short role shown as a chip (`.model-option-tag`, global) before the model name, in the trigger and the option, and part of both accessible names and of type-ahead. For a choice of roles played by models (the Calibration tab's *Compare against*), not for grouping — that is `group` |
| `selectedKey` | The selected option's key, compared with `===`; the host owns it and feeds it back |
| `noneLabel` | Adds a first option with key `null`; shown muted on the trigger while nothing is selected |
| `placeholder` / `emptyHint` | Muted trigger text with no selection and no none option; the popup's text when `options` is empty (the listbox's `aria-describedby`) |
| `labelledBy` / `label` | The visible label's id; `label` only for a picker with no visible label (rendered `visually-hidden`). One of the two is required — development mode warns |
| `describedBy` | The hint's id, on the trigger's `aria-describedby` |
| `triggerId` | Only where a host already references the trigger's id |
| `showPrice` / `showParallel` | The price and parallel-execution badges; thinking level, reasoning mode and provider are always shown |
| `variant="compact"`, `dropsUp`, `narrowHidesBadges` | The composer and Models page look; `narrowHidesBadges` hides the provider and parallel badges below 992 px |

`selectionChange` emits `{ key, model }` on every committed choice, including re-choosing the
selected option, so the handler must be idempotent.

- **Keyboard.** On the trigger: Enter, Space and click toggle; ArrowDown (or Alt+ArrowDown) opens
  on the selected option, else the first; ArrowUp on the selected, else the last. In the list:
  ArrowUp / ArrowDown by one **without wrapping**, Home / End, PageUp / PageDown by ten; Enter or
  Space commits, closes and returns focus to the trigger; Tab closes without a change and focus
  moves on from the trigger; typing a printable character jumps to the next visible name starting
  with it (a 500 ms buffer, a repeated letter cycles).
- **Escape** closes the list without a change and returns focus to the trigger, and calls both
  `preventDefault()` and `stopPropagation()` — the picker is used inside native `<dialog>`s, which
  must not close on the same key. A second Escape closes the dialog.
- **Pointer.** A click commits; hover never moves the active option. A `pointerdown` outside the
  picker, or focus leaving it, closes it without a change. There is no host code for "one open at a
  time": opening one picker is an outside `pointerdown` for every other.
- **ARIA drives the styling.** The open trigger is `[aria-expanded="true"]` and the selected option
  `[aria-selected="true"]` in `styles.scss`, with no parallel class. **`.is-active` is the one
  exception, and it is unavoidable**: with `aria-activedescendant` the active state lives on the
  listbox, so the option has no attribute of its own to style from. The keyboard ring is drawn on
  `.selector-dropdown:focus-visible .model-option.is-active`; the listbox itself has no outline.
- **Badges** carry `visually-hidden` prefixes (*thinking level*, *reasoning mode*, *price*,
  *parallel execution*), and no `title` (§4.2): the parallel badge's explanation is visually hidden
  text. Consecutive options sharing a `group` are a `role="group"` named by its
  `.model-group-title` heading.
- **The trigger never truncates.** Its tag, name and badges are one `.selector-trigger-content` row
  that wraps, spaced only by `gap`, so in a narrow host the badges start a second line flush with the
  full model name, and a name wider than the trigger wraps inside itself. The chevron sits outside
  that row, at the end, vertically centered. The `compact` variant stays on one line
  (`flex-wrap: nowrap`, a `nowrap` name). The dropdown's options keep one line each.
- **Marker classes** on the host (`class="tested-model-selector"`) are merged with
  `custom-model-selector`; specs query the trigger as `.<marker> .selector-trigger`. A host
  stylesheet can size the host element, but a rule reaching into `.selector-trigger` or
  `.model-option` from a component stylesheet no longer matches — put it in `styles.scss`.
- The native customizable `<select>` (`appearance: base-select`) is not used until Firefox supports
  it: its fallback runs the badge texts together and loses the distinctions an administrator
  chooses by.
- The badge row is its own component, `app-model-option-badges` (`shared/model-picker/`, host
  `display: contents`; inputs `model`, `showPrice`, `showParallel`, `narrowHidesBadges`), which the
  multi-model picker (§4e-3) renders too, so both pickers draw identical badges.

### 4e-2. Multi-select picker: `app-multi-picker`

The control for choosing **several** items an action will act on — a generic, model-agnostic
multi-select in `app/shared/multi-picker/` (`multi-picker.component.*`, option types in
`multi-picker.models.ts`). It implements the WAI-ARIA *collapsible listbox* with
`aria-multiselectable="true"`: a `<button aria-haspopup="listbox" aria-expanded>` whose text summarizes the
selection opens a popup `role="listbox"`, which takes focus and tracks the active option with
`aria-activedescendant`; every option carries `aria-selected`, and a check glyph (`aria-hidden`) shows
selection, so color is never the only carrier. It reuses §4e's trigger and dropdown look. The
trigger's summary wraps rather than truncating, by the shared `.selector-trigger .model-name` rule.

**Facet or picker?** `app-filter-facet` (§8h) **filters a list it sits over**: its choice narrows what is
shown and changes nothing else. `app-multi-picker` **chooses what an action acts on**: the selection is
the input of a Generate, an export or a job. Never use one for the other's job.

```html
<label id="rpModelsLabel">Models</label>
<app-multi-picker labelledBy="rpModelsLabel" describedBy="rpModelsLine" summaryNoun="sources"
                  [options]="options" [selectedKeys]="chosenKeys" [min]="2" [max]="12"
                  (selectionChange)="onChosen($event.keys)"></app-multi-picker>
<p id="rpModelsLine" class="form-hint">…</p>
```

| Input | Meaning |
|-------|---------|
| `options` | `MultiPickerOption[]` — `{ key, label, tag?, detail?, group?, disabledReason? }`. `key` is a string or number, unique in the picker. `label` is the option's accessible name, the chip text and what type-ahead matches. `tag` is a short chip before the label (`.model-option-tag`), `detail` a muted part after it, `group` a `role="group"` heading shared by consecutive options, and `disabledReason` makes the option `aria-disabled` with the reason under it. From a memoized getter (§4e's rule) |
| `selectedKeys` | The chosen keys, compared with `===`; keys not among `options` are ignored. **The host owns the selection** and feeds it back after each `selectionChange` |
| `min` / `max` | The fewest that must stay selected (default 0) and the most allowed (default `null`, no limit) |
| `labelledBy` / `label` | As §4e: the visible label's id, or `label` only for a picker with no visible label (rendered `visually-hidden`); development mode warns when both are missing |
| `describedBy` | The hint's id, on the trigger's `aria-describedby` |
| `placeholder` / `emptyHint` | Muted trigger text while nothing is selected (default *Select <summaryNoun>*); the popup's text when `options` is empty |
| `summaryNoun` | What the options are, in the plural (default *items*): the trigger reads *All 5 sources* or *3 of 5 sources*, and with one selected that option's tag and label |
| `chips` | `'selected'` (default) adds a chip row after the trigger; `'none'` leaves the trigger's summary alone |
| `maxChips` | Chips shown before the row ends in **+N more**, which opens the list (default 6) |
| `showAllNone` | Text-only **All** / **None** `.btn-link`s after the trigger (default on) |
| `dropsUp` | Opens the popup upward |
| `optionTemplate` | `TemplateRef<{ $implicit: MultiPickerOption; selected: boolean }>`: **the extension point**, rendered in place of the default tag, label and detail. It must render **text only** — never a button, link or input — because options live inside a listbox |

`selectionChange` emits `{ keys }`, the chosen keys in option order, **once per toggle, All, None or
chip removal**.

- **Keyboard.** On the trigger: Enter, Space and click toggle the list; ArrowDown opens on the first
  selected option, else the first; ArrowUp on the first selected, else the last. In the list:
  ArrowUp / ArrowDown by one without wrapping, Home / End, PageUp / PageDown by ten; **Space toggles the
  active option and keeps the list open**; **Enter toggles it and closes**; **Ctrl+A** (Cmd+A) selects
  all, or clears all when all are selected; **Shift+ArrowUp / ArrowDown** moves and adds the next option
  to the selection (it never removes one); typing a printable character jumps by label (a 500 ms
  buffer, a repeated letter cycles; Space while a buffer is open is part of it).
- **Escape and Tab close without undoing anything**: a multi-select commits as it goes. Escape returns
  focus to the trigger and calls `stopPropagation()`, so an enclosing `<dialog>` stays open; Tab moves
  focus to the trigger first and the browser's Tab moves on from there. A `pointerdown` outside the
  picker, or focus leaving it, closes it too.
- **Limits.** A toggle that would go below `min` or above `max`, or touch a disabled option, does
  nothing and announces why. **All** and **None** honor `min` and `max` (All takes every enabled option
  and keeps the disabled ones already selected; None keeps only those) and are `aria-disabled` with an
  `interestfor` tooltip giving the reason when they cannot act (*All sources are already selected.*,
  *At least 2 sources must stay selected.*); their visually hidden completions (*All sources*, *None of
  the sources*) give each pair a distinct name (§4.1).
- **Chips** are a `ul` (*Selected sources*) after the trigger, never inside it (buttons in a button are
  invalid): each chip's tag, label and detail, and a remove `.action-btn` named *Remove <label>* (*Remove
  <label> (<detail>)* when it has one) with an `interestfor` tooltip — *Remove from the selection*, or the
  reason it cannot (`aria-disabled`). After a removal focus moves to the next chip's remove button, else
  to the trigger.
- **One polite status line** (`role="status"`, visually hidden) announces each change with the count:
  *"Battery run #10 selected. 2 of 5 selected."*; a refused toggle announces its reason there. A repeated
  message alternates a trailing space so it is read again.
- **Styles** live in `styles.scss` under `.gh-multi-picker` (the host's class), reusing
  `.custom-model-selector`, `.selector-trigger`, `.selector-dropdown`, `.model-option` and
  `.model-option-tag` and adding only the check column, the chip row and forced-colors rules; the
  component SCSS is `:host { display: block }`. Polyfills: `ensureOverlayPolyfills()` for the tooltips.
  No `title` attribute anywhere (§4.2).
- **Specs**: it is `OnPush`; drive a spec through real clicks and key events, or `markForCheck()`, never a
  bare property set and `detectChanges()`.

### 4e-3. Multi-model picker: `app-model-multi-picker`

A thin wrapper over `app-multi-picker` in `app/shared/model-picker/` for choosing several **models**:
each option is drawn as the model's name, the single picker's badges (`app-model-option-badges`) and a
muted detail. It keeps every rule of §4e-2; only the option rendering (its `optionTemplate`) is its own.
The Model Comparison wizard's step 3 uses it to choose which models a report covers (labeled *Models*).

| Input | Meaning |
|-------|---------|
| `options` | `ModelPickerOption[]`, the single picker's type (§4e), whose optional `detail` and `disabledReason` only this picker honors; the label is the model's display name, else its model id |
| `selectedKeys`, `min`, `max`, `labelledBy` / `label`, `describedBy`, `placeholder`, `emptyHint`, `chips`, `maxChips`, `showAllNone`, `dropsUp` | Passed through to `app-multi-picker`; `summaryNoun` is fixed to *models*, and with one model selected the trigger reads its name |
| `showPrice` / `showParallel` | The price and parallel-execution badges, **both off by default** |

`selectionChange` emits `{ keys, models }`, the chosen keys and their `ModelPickerModel`s in option order.

- **A picker choosing which models a report covers shows no price or parallel badge**: it chooses what a
  document is about, not what to run. Thinking level, reasoning mode and provider are always shown.
- **Keys stay the host's units.** Step 3 keys each option by comparison entry (`run:` / `group:` /
  `battery:`), so two entries of one model are two options; where two options would look identical,
  each carries its source as `detail` (*Battery run #10*), and only then.

### 4f. Action popovers: `.gh-action-popover`

A labelled trigger that reveals a short list of **related actions** — the run report dialog's
**Re-run** (panel `aria-label` *Re-run and repair*; *Re-run failed questions*, *Retry failed
assessments*, *Retry claim verification*, *Re-run final synthesis*, *Re-score run*, in that order, each
listed and gated by §4g) is the first. The Battery Run Report's **Actions** (*Recompute analysis* or
*Compute analysis*, and *Show progress*) is another. The Download Center's per-row **More actions**
(*Update charts*, *Remove charts*; Report Pack rows, in the Model Comparison wizard's Documents step
only) is the second: an icon-only `.action-btn` trigger named *More actions for <document>*, with a
hint tooltip, whose panel's `aria-label` is *Chart actions for <document>*. It is a **popover-revealed button group,
not an ARIA menu**: no `role="menu"` / `menuitem`, no arrow-key roving; Tab moves through the items.

**When to use it.** Several actions of one kind that would crowd a header or toolbar, whose names
need words (a row of five icon buttons for five re-run variants would be unreadable), and which the
user picks one of at a time. A few distinct, frequent actions with recognizable glyphs stay a row of
visible buttons or icon buttons (§4); a single action stays a button.

```html
<button type="button" class="btn-ghost" id="rr-rerun-trigger"
        popovertarget="rr-rerun-popover" [attr.aria-expanded]="rerunOpen"
        [attr.style]="'anchor-name: --rr-rerun'">
  <svg class="btn-icon" ... aria-hidden="true"><!-- rotate --></svg> Re-run
  <svg class="btn-icon" ... aria-hidden="true"><!-- chevron --></svg>
</button>
<div popover="auto" id="rr-rerun-popover" class="gh-action-popover"
     role="group" aria-label="Re-run and repair" (toggle)="onRerunToggle($event)"
     [attr.style]="'position-anchor: --rr-rerun'">
  <button type="button" class="gh-action-popover-item" (click)="onRerunAction(failed)">Re-run failed questions</button>
  <button type="button" class="gh-action-popover-item" aria-disabled="true" (click)="onRerunAction(rescore)">
    <span>Re-score run</span>
    <span class="gh-action-popover-item-reason">A retry is already running on this run.</span>
  </button>
</div>
```

- **The two global classes** in `styles.scss`: `.gh-action-popover` (the panel) and
  `.gh-action-popover-item` (a labeled, full-width button). Never restyle them in a component.
- **`popover="auto"`** gives the top layer and light dismiss (a click outside, or another auto
  popover opening). The trigger opens it with `popovertarget`.
- **ARIA.** The panel is `role="group"` with an accessible name (`aria-label` or
  `aria-labelledby`). The trigger's `aria-expanded` is bound from the panel's `toggle` event,
  because the popover polyfill does not set it.
- **Focus.** On open, focus moves to the first enabled item; on close, back to the trigger.
- **Escape** closes only the popover: its handler calls `stopPropagation()`, as the reorderable
  list's Move menu does (§4c), so the `<dialog>` it sits in does not close on the same key.
- **Positioning.** Explicit anchor names on **both** trigger and panel via `[attr.style]` (§4.2
  items 3–4), with `flip-block, flip-inline` fallbacks; natively (`.gh-anchor-native`, set by
  `markNativeAnchorPositioning()` because the polyfill ignores `@supports`) the panel also tries the
  opposite corner and, as a last resort, goes on the roomier side anywhere across the viewport width,
  with 16 px gutters, capped to the free height, and scrolls (`--gh-popover-below-fit` /
  `--gh-popover-above-fit` in `styles.scss`). Chrome tries at most five fallbacks. The click-mode
  info popup (§4b) uses the same chain.
  Call `ensureOverlayPolyfills()`; without anchor positioning the panel still works, centered.
- **Disabled items are `aria-disabled="true"`** with an inert handler and the reason on a second
  line inside the item — never `disabled`, and never silently absent, so the user learns why an
  action is unavailable (§6).
- **Open-state styling** uses `:is(:popover-open, .\:popover-open)` for the polyfill (§4, Polyfills),
  and the entry animation has a `prefers-reduced-motion: reduce` entry.
- The reorderable list's **Move** menu (§4c) predates this pattern and is not migrated to it.

**Grouping the trigger with its neighbors.** A header's cluster of a few Tab-stop buttons — the run
report's *Run actions* — is `role="group"` with an `aria-label`, **not** `role="toolbar"`. A toolbar
promises arrow-key roving focus with one Tab stop; use it only for a group that implements that.

### 4g. Repair and retry actions

The benchmark's run and battery repairs — re-running, retrying, re-assessing, re-scoring, continuing,
recomputing — follow one set of rules, so an action has the same name, glyph and availability wherever
it appears.

**One permanent home per action per surface**, plus at most **one contextual call to action** in the
alert that states the problem. The run report's **Re-run** popover (§4f) is the home of the run-level
repairs; its Summary tab's failure alert (*Run did not complete cleanly*) repeats **Re-run failed
questions** as its one contextual button; the run progress dialog's footer has the same action, gated
the same way. Per question, **Re-run question**, **Re-assess question** and **Try another assessor**
sit in the question's action row. For a battery run the **battery progress dialog** is the one home of
**Continue** and **Re-run under current instrument**: the run tab's banners and the Battery Run
Report's **Actions** only open it (*Show Battery Progress*, *Show progress*). Never add a second
permanent copy of an action to a banner or a header.

**The verbs.** Each names one kind of work; never use one for another's.

| Verb | Means | Glyph on a labelled button |
|---|---|---|
| **Re-run** | Ask the candidate again (*Re-run failed questions*, *Re-run question*, *Re-run final synthesis*) | *refresh-cw* |
| **Retry** | Repeat a grading step that failed (*Retry failed assessments*, *Retry claim verification*) | *refresh-cw* |
| **Re-assess** | Grade a stored answer again, replacing its verdict (*Re-assess question*) | *refresh-cw* |
| **Re-score** | Recompute the scores from the stored levels, with no model call (*Re-score run*) | — |
| **Continue** | Resume a stopped battery run or series (*Continue — <reason>*) | *play* |
| **Recompute** | Recompute an analysis (*Recompute analysis*, *Compute analysis* before the first) | none |
| **Try again** | Reload after a load error | none |

Popover items (§4f) carry no glyph; the trigger carries the family's.

*Try another assessor (does not change the score)* records a second verdict and changes no score: it is
a plain `.btn-ghost` with the *flask* glyph, never styled as a repair (there is no `.btn-gh-trial`).

**Labels.** Sentence case (*Re-run failed questions*, not *Re-run Failed Questions*). A confirmation
dialog's confirm button carries **exactly its item's label** — *Re-assess question* confirms *Re-assess
question* — with no `aria-label` overriding it, and the dialog is `aria-labelledby` its `<h3>`. A
progress label may stand in while the request runs (*Re-running…*, *Continuing…*).

**Availability is decided once, in pure gates.** `admin/benchmark/run-repair-actions.ts` exports one
function per action, returning `{ visible, disabledReason }`. The gates **mirror the server's
refusals** (`AdminBenchmarkController`, `BenchmarkService`), which stay authoritative, and check in the
server's order:

1. **busy** — the run is executing, or a repair of it was sent and not yet seen running: *"A retry is
   already running on this run."*
2. **aborted** — the run stopped before finishing its suite (`isAbortedRun`): *"The run stopped before
   finishing its suite."*
3. **older scoring method** — `BenchmarkRunDetailDto.isCurrentScoringMethod === false`: *"Scored under
   an older scoring method; re-score it first."* Re-score is the one action this does not refuse.
4. **the action's own precondition** — *"The run has no dimensional level ratings to re-score."*,
   *"The question text is empty."*

| Action | Visible when |
|---|---|
| Re-run failed questions | status Failed, Canceled or CompletedWithErrors, and a failed answer exists |
| Retry failed assessments | an answer is not Scored (in a panel run, member B's included) |
| Retry claim verification | an answer carries a claim-verification error |
| Re-run final synthesis | the run has answers |
| Re-score run | always |
| Re-run question | always |
| Re-assess question | always — one label whether or not the assessment failed |
| Try another assessor | not a panel run, or the answer's second-opinion slot is empty |

**A repair the server would refuse is never offered enabled.** It is shown `aria-disabled="true"`,
focusable, with an inert handler and its reason **visible** — on the popover item's second line, or in
a line under the row that the button's `aria-describedby` points at. Never `[disabled]` without a
reason, and never silently absent while it is visible by the table above. An action whose visibility
condition does not hold is left out, because there is nothing to repair.

---

## 5. Tabs

Use the shared `.gh-tabs` / `.gh-tab` widget from `styles.scss`. It is an underline
treatment with horizontal scroll-snap and scroll-edge indicator support.

| Class | Use |
|-------|-----|
| `.gh-tabs` | The `role="tablist"` container |
| `.gh-tab` | Each tab button |
| `.gh-tabs-secondary` | Modifier on the container for a **nested** row — smaller type, tighter spacing, fainter rule, so it reads as subordinate to the row above it |
| `.gh-tabs-segmented` | Modifier for a small fixed set of views nested under another tab row: equal segments in one rounded track, so it does not read as a second row at the same level |

The PDF viewer (`shared/pdf-viewer/pdf-viewer-dialog`) uses `.gh-tabs-segmented` for its disclosure
versions (`variants`) and, when the request carries `secondaryVariants`, for a second, independent row
after it — each its own `role="tablist"` named by its `label` (*Peer names*: *Named*, *Anonymized*),
each with the full contract below. `load(variant, secondary?)` and `tabUrl(variant, secondary?)` receive
the second key only when the second row exists; without it the viewer is unchanged.

### The markup contract

Every one of these attributes is required. ARIA roles are a behavioural promise: setting
`role="tab"` without the keyboard model produces a control that announces itself as a tab
and then does not behave like one, which is worse than a plain button.

```html
<div class="gh-tabs gh-tabs-secondary" role="tablist" aria-label="Benchmark sections">
  <button type="button" role="tab" class="gh-tab"
          id="bm-tab-run"
          aria-controls="bm-panel-run"
          [attr.aria-selected]="activeSubTab === 'run'"
          [attr.tabindex]="activeSubTab === 'run' ? 0 : -1"
          (keydown)="onTabKeydown($event, 0)"
          (click)="selectSubTab('run')">
    <svg class="btn-icon" ... aria-hidden="true">...</svg>
    Run Benchmark
  </button>
  <!-- ... -->
</div>

@if (activeSubTab === 'run') {
  <div role="tabpanel" id="bm-panel-run" aria-labelledby="bm-tab-run" tabindex="0">
    ...
  </div>
}
```

- **`aria-label` on the tablist**, naming the axis of choice ("Admin sections", "Benchmark
  sections"). Do not include the word "tabs" — the role already says that.
- **`aria-selected`** on every tab, `aria-controls` pointing at its panel's `id`.
- **Roving `tabindex`**: the selected tab is `0`, every other tab is `-1`. This is what
  makes the row a single Tab stop, so Tab moves *past* the row rather than through it.
- **The panel** carries `role="tabpanel"`, an `id`, `aria-labelledby` pointing back at its
  tab, and **`tabindex="0"`** so a keyboard user can Tab from the row into the content.
- **`@if` removes inactive panels from the DOM**, so no `hidden` or `inert` handling is
  needed. If you render all panels at once instead, the inactive ones need `hidden`.
- **A `tabpanel` may contain a `tablist`.** Nested tab rows are valid ARIA — that is what
  `.gh-tabs-secondary` is for.
- **Never put `role="tabpanel"` on a `<table>`** (or any element with its own meaningful
  role) — it replaces the table semantics. Wrap the table in a `<div>` instead.

### The keyboard model

Left/Right move and wrap, Home/End jump to the ends. Enter and Space come free because each
tab is a real `<button>`. Focus must follow selection in the same turn, or the tab holding
focus becomes `tabindex="-1"` and the next Tab press jumps somewhere unexpected.

```typescript
readonly subTabs = ['run', 'history', 'suites'] as const;

onTabKeydown(event: KeyboardEvent, index: number): void {
  const targets: Record<string, number> = {
    ArrowRight: index + 1,
    ArrowLeft: index - 1,
    Home: 0,
    End: this.subTabs.length - 1
  };
  const requested = targets[event.key];
  if (requested === undefined) {
    return;                       // Not our key: let it through untouched.
  }

  event.preventDefault();
  const next = (requested + this.subTabs.length) % this.subTabs.length;
  const tab = this.subTabs[next];
  this.selectSubTab(tab);
  document.getElementById(`bm-tab-${tab}`)?.focus();   // Focus follows selection.
}
```

Keep the tab list in a `readonly` array on the component and render it with `@for`. Seven
hand-written tab buttons is seven places to forget an attribute.

### `aria-selected` drives the styling

`.gh-tab[aria-selected="true"]` carries the active appearance. There is deliberately **no**
`.is-active` or `.admin-tab-active` class.

A parallel class is a second source of truth for one piece of state, and the two drift: the
old `.admin-tab-active` binding could be present while `aria-selected` was absent entirely,
which is exactly what it was — visually correct, silent to a screen reader. Binding the
appearance to the ARIA attribute makes a styled-but-unannounced tab impossible to write.

> **Why not the animated sliding underline?** The `modern-web-guidance`
> `anchor-positioning-tab-underline` guide describes tethering a `::before` pseudo-element
> with `position-anchor` to animate the underline between tabs. It is deliberately not used
> here: no major browser supports anchor positioning natively yet, so it would mean loading
> the anchor-positioning polyfill on every page with tabs purely for decoration — and the
> guide's own fallback for unsupported browsers is precisely the `border-bottom` we already
> have. Revisit when anchor positioning reaches Baseline. The guide's *mandatory* half —
> `aria-selected` alongside the visual indicator — is implemented.

### Data loading on tab change

Put it in the select method, not the template. `(click)="activeTab = 'history'; loadHistory()"`
is two statements in a template and duplicates the loading rule at every call site,
including the keyboard handler, which will forget it.

```typescript
selectSubTab(tab: 'run' | 'history' | 'suites'): void {
  this.activeSubTab = tab;
  if (tab === 'history') { this.loadHistory(); }
  if (tab === 'suites') { this.loadSuites(); }
}
```

### 5b. Settings sections

A long settings panel — the Theme and Charts tabs of the model comparison's sidebar are the
reference — is a stack of **non-exclusive** native disclosures, not a row of tabs:

```html
<details class="gh-disclosure gh-disclosure--section" id="mc-style-bar-section-bars"
         [open]="isOpen('bar', 'bars')" (toggle)="onSectionToggle('bar', 'bars', $event)">
  <summary>
    <span class="gh-disclosure-summary-title">Bars</span>
    <span class="gh-disclosure-summary-value" aria-hidden="true">{{ readout('bar', 'bars') }}</span>
  </summary>
  <div class="gh-disclosure-body">...</div>
</details>
```

- **It is a section, not a tab (§1).** Nothing is swapped in place, and several sections can be
  open at once, so a reader can compare one against another. No `name=` exclusive accordion.
- **The summary holds text only**: the title and a one-line read-out of the section's current
  values, which `.gh-disclosure--section` shows only while the section is closed. The read-out is
  `aria-hidden="true"`: the controls announce their own values once the section is open, and a
  summary name that changes on every drag step is noise. No button, input or link inside a
  summary — interactive content there is invalid.
- **Open state is a per-viewer convenience** in `localStorage`, every read and write in
  `try/catch`, with a default (the first section open) when the store is empty or unavailable.
  Follow the native `toggle` event rather than the summary's click, so a key press and a bound
  `open` are tracked too.
- **A radio group keeps its own `<fieldset>` and `<legend>`** inside the section; the section
  replaces only the outer bordered grouping box.
- **A section may carry a reset icon button** (`.action-btn`, undo glyph) positioned over the
  summary row's end. It is a sibling after `<details>` inside a positioned wrapper: never inside
  `<summary>`, and never in the body, which is hidden while the section is closed. It is named
  "Reset {title} to defaults", it is `aria-disabled` while the section is at its defaults, and a
  `role="status"` line announces the reset.
- The open and close animation (`interpolate-size` and `::details-content`) is a progressive
  enhancement in `styles.scss`, switched off under `prefers-reduced-motion: reduce`.

---

## 6. Focus, motion, and disabled state

- **Every control needs a visible `:focus-visible` ring.** `outline: none` without a
  replacement is a defect, full stop — it makes the control invisible to keyboard users.
  The shared classes all provide one; if you add a new control class, add one too:

  ```scss
  &:focus-visible {
    outline: 2px solid var(--primary-color);
    outline-offset: 3px;   /* or -2px where the control sits flush against an edge */
  }
  ```

- **Hover transitions are wrapped in `@media (prefers-reduced-motion: reduce)`.**
  `styles.scss` has one block covering `.btn-gh`, `.gh-tab`, `.action-btn`, and
  `.btn-icon-action`; extend it rather than adding scattered media queries.
- **`disabled` versus `aria-disabled`.** `disabled` removes the control from the focus order
  entirely, and `tabindex="0"` will not bring it back. That is right for a form submit
  button, and usually **wrong** for a toolbar or row-action button the user needs to be able
  to land on and discover is unavailable. Prefer `aria-disabled="true"` plus an inert click
  handler there.
- **`type="button"` on every `<button>`.** Inside a `<form>`, the default is `submit`, so an
  unmarked button submits the form the day someone wraps the markup in one. The benchmark
  view had 48 unmarked buttons and no `<form>` — safe only by accident.
- **Colour is never the only carrier of meaning.** The red hover on `.action-btn-danger` is
  a reinforcement of the icon and the accessible name, not a substitute for them.

---

## 7. Where the styles live

**`Overseer/ClientApp/src/styles.scss` is the only home** for button, icon-button, tooltip,
and tab base styles. Component stylesheets hold layout and genuinely component-local
concerns.

- A variant needed by a **second** component **moves to `styles.scss`**; it is not copied.
  `.btn-gh-small` lived in `admin.component.scss` and was therefore unavailable to the
  benchmark view that needed it — the kind of duplication that ends as divergence.
- `.gh-textarea-autosize` is the shared class for a `<textarea>` that grows with its content
  to a cap and then scrolls (`field-sizing: content`, bounded by `--autosize-min` /
  `--autosize-max`); a component sets those two custom properties for its own bounds rather
  than defining a second autosize class.
- `.gh-field-error` is the shared inline error line under a control or beside an action — a
  refusal, a failed copy or cancel: a leading 14 px *alert-circle* SVG (`aria-hidden`) and the text
  in `var(--color-error-text)`, a red that keeps 4.5:1 on the dialog surfaces. Give it an `id` and add
  that to the control's `aria-describedby`. The icon and the words carry the meaning with the color
  (§6); a warning the user may proceed past is an amber `alert-warning` instead, never this line.
- Use the design tokens: `var(--primary-color)`, `var(--gold-glow)`,
  `var(--border-glass)`, `var(--nav-color)`. Not `#e0ba6d`, which *is* `--primary-color`
  and will not follow it if the theme ever changes.
- For the **MobileGnollHackLogger** Razor pages the equivalent source is
  `wwwroot/css/site2.scss`, and the generated CSS is never edited directly — see
  [`scss_compilation`](../scss_compilation/SKILL.md).

---

## 8. Data tables

The benchmark tables — **Runs in this group** and **Analysis groups** (`multi-run.component`), and
the Model Comparison's source and entry tables — share one implementation of paging, column sorting and
column filtering. Hand-rolled copies in one tab would be several places for the same bug. **Run
History** was the first of these tables; it is now a card list (§8h).

The layer is deliberately **headless plus presentational**, not a generic `<app-data-table>`.
These tables' cells carry tier badges, score badges, instrument fingerprints, tooltips and
three-button action groups; funnelling all of that through a column-definition DSL would
produce worse markup than hand-written rows. What the tables genuinely share is the *state
arithmetic*, the *pager markup* and the *styling*.

The same `TableState` also drives **card lists** (§8h): a list of rich rows — small forms, or records
with many facts and several actions — filtered by a faceted filter bar and shown in batches with Load
more. `CardListState` wraps a `TableState` to add the filter bar's and Load more's state.

| Piece | File |
|---|---|
| `TableState<T>`, `exactFilter()`, `anyOfFilter()`, `customFilter()`, `PAGE_SIZES` | `shared/data-table/table-state.ts` |
| `CardListState<T>` and its option, facet and chip types | `shared/data-table/card-list-state.ts` |
| `<th app-sort-header>` | `shared/data-table/sort-header.component.ts` |
| `<app-table-pager>` | `shared/data-table/table-pager.component.ts` |
| `<app-filter-facet>` | `shared/data-table/filter-facet.component.ts` |
| `.gh-datatable` and the `.gh-pager` / `.gh-filter-*` families; the filter bar's `.gh-search-field`, `.gh-facet-*`, `.gh-list-status` and `.gh-load-more` | `styles.scss` |

### 8a. When a list becomes a table

The threshold is **paging, sorting or per-column filtering**. A five-row list that is never
sorted stays a plain `.gh-table`; adding a pager to it is noise.

**A checkbox list people scan and filter is a table.** *Runs in this group* was a
`<fieldset class="mr-run-picker">` with a `<legend>` and one `<label>` per row. As a table it
uses `<caption>` in place of the `<legend>` — `<caption>` is a table's native naming
mechanism — the `<fieldset>` is dropped, and each checkbox carries its own `aria-label`
naming its subject (*"Include run 21 in this group"*), exactly as §4 requires of any control
whose visible text is not its name.

A list whose every row is a **small form** — selects, several checkboxes, a few actions — is not a
table at all but a card list; §8h says when and how.

### 8b. The `TableState<T>` contract

A component owns one `TableState` per table as an ordinary field, declares its accessors once,
and renders `state.view(sourceRows)`:

```ts
groupTable = new TableState<BenchmarkRunGroupDto>('createdAtUtc', 'desc').registerAccessors(
  { name: g => g.name, tier: g => this.tierOrder(g.tier), createdAtUtc: g => new Date(g.createdAtUtc) },
  { name: g => g.name, tier: exactFilter(g => g.tier) }
);
```

```html
@for (group of groupTable.view(groups); track group.id) { … }
```

`TableState` has no Angular dependency — no injectables, no DOM, no events — so it unit-tests
as plain TypeScript. The rules it guarantees, each with a spec in `table-state.spec.ts`:

- **`view()` never mutates its input.** It returns a new array; the source list stays in server
  order however often the view is read.
- **Filter, then sort, then page**, and paging counts the *filtered* rows.
- **Null and undefined sort last in both directions.** A run with no Intelligence Index must
  not displace a scored one at the top of a descending sort.
- **Strings compare with `localeCompare` and numeric collation**, so `#9` ranks below `#10`.
- **Ties keep source order**, via a carried source index rather than trusting engine sort
  stability — so no secondary sort column is needed for a deterministic view.
- **The page is clamped when it is read**, not only when it is set: the row set can shrink
  under the table (a delete, a reload) with no setter ever being called.
- **`exactFilter()` for `<select>` columns.** The default is case-insensitive containment,
  which is right for a free-text input and wrong for a fixed option set — a *Failed* option
  must not also match *FailedValidation*.

> **The source list is the logic list; the view is only for rendering.**
> `instrumentChangeOf` (`benchmark-run-format.ts`) locates a run by `indexOf` in `historyRuns`
> and then treats later entries as chronologically earlier, and `completedRunsOfSelectedSuite`
> takes `.slice(0, 5)` as "the five newest". Both are correct only against server order. Under
> a user-chosen sort, in-place sorting would silently move the *INSTRUMENT CHANGED* badges onto
> the wrong runs. Never sort a source array in place; keep helpers reading the source list and
> give only the template the view. This holds for a card list too: Run History's cards render
> `historyList.view(historyRuns)` (§8h), and `historyRuns` stays in server order.

### 8c. Sortable headers

```html
<th app-sort-header [state]="groupTable" column="tier" label="Tier"
    (changed)="onTableChanged()"></th>
```

The component's host element **is** the `<th>` (hence the attribute selector), so there is no
wrapper between the row and the cell.

- **`aria-sort` on the `<th>` is the single source of truth.** The caret is drawn in CSS from
  `th[aria-sort="ascending"]` / `[aria-sort="descending"]` — never from a parallel `.active`
  class that could disagree with what is announced.
- The label is a real `<button>`, so keyboard operability and Enter/Space come free.

> **The Admin Users tab's table (`admin/users-tab/admin-users-tab.component.html`) is the old pattern — do not copy it.** It puts
> `(click)` on a bare `<th>` with a `▲`/`▼` span and no `aria-sort`: it is not keyboard-operable
> and announces nothing. It is out of scope rather than correct, and can adopt this layer later.

### 8d. The filter row

A `<tr class="gh-filter-row">` sits directly under the header row, one `<td>` per column,
empty where a column is not filterable.

- **Every control gets a real `<label class="visually-hidden" for="…">`.** A placeholder is not
  a label; it disappears the moment typing starts and several screen readers never announce it.
- Build a `<select>`'s options from the values **actually present** where you can, so an option
  the backend stops emitting disappears from the control on its own.
- A **Clear filters** control renders only when `state.hasActiveFilters`. It is the link-style
  `.gh-filter-clear`.
- **On/off view filters** — *Show selected only*, *Show comparable only* — are
  `<button type="button" class="gh-filter-toggle" [attr.aria-pressed]="…">`: a 28px pill that
  turns gold with a leading check glyph when pressed, so the state is carried by shape as well as
  colour. Not a checkbox, and not `.gh-filter-clear`, which is an action rather than a state.
- **Two empty states, not one.** `rows.length === 0` means nothing has been recorded yet;
  `state.noMatches(rows)` means the filters hide everything. They want different messages, and
  the second one offers *Clear filters*. One message for both causes is a support ticket.
- A server-side query control (one that changes what is fetched) is **not** a column filter.
  Keep it in the toolbar, left of the column filters, and let its `<label>` say which of the two
  it is. Prefer not to need one: Run History's *Filter by Suite* select was replaced by a
  client-side *Suite* facet over the loaded runs (§8h), because two suite filters with different
  semantics confuse.

### 8e. The pager

```html
<app-table-pager [state]="groupTable" [rows]="groups" noun="groups"
                 (changed)="onTableChanged()"></app-table-pager>
```

`[rows]` is the **full source list**, never the view — the pager computes the filtered count
and the range itself.

- **`aria-disabled`, never `disabled`, at the ends.** A `disabled` button leaves the focus
  order, so a keyboard user cannot land on it and learn why it does nothing. Each handler
  refuses independently, because an `aria-disabled` button is still clickable. This is the same
  convention §4 applies to the group list's download control.
- The current page is marked with **`aria-current="page"`**, not an `.active` class.
- **One polite live region per table.** The status line (*"Showing 11–20 of 47 runs"*, plus
  *"filtered from 122"* when filters are active) is `role="status" aria-live="polite"`. A table
  with a pager above **and** below sets `[announce]="false"` on the second: the line stays
  visible and is hidden from assistive technology, so a page change is announced once rather
  than twice. Many noisy live regions become spam.
- The page-size `<select>` has a real `<label for>`, and the icon-only step buttons carry
  `aria-label` plus the `interestfor` + `popover="hint"` tooltip pattern of §4 — never `title`.
- **The numbered slots never exceed `MAX_PAGE_SLOTS` (7)**, exported from `table-state.ts`:
  `TableState.pageNumbers()` collapses the rest into ellipses (`1 … 9 10 11 … 20`) at any page
  count, and `table-state.spec.ts` pins the bound. A pager cannot grow with the row count.
- **The button row does not wrap.** `app-table-pager` is a size container (`container:
  gh-pager / inline-size`), so the pager's **own** width decides its density, not the
  viewport's — it sits in dialogs and narrow columns. Below 40rem of that width the numbered
  slots give way to a *Page X of Y* label (`.gh-page-compact`, `aria-hidden` because the status
  line already announces the range); below 24rem the page-size and jump labels are hidden
  visually but kept for assistive technology.
- **A *Go to page* field appears above `MAX_PAGE_SLOTS` pages.** Enter or `change` moves there
  through `state.setPage`, which clamps, so there is no error state: an out-of-range number lands
  on the nearest valid page and the status line announces it. Server-paged tables work the same
  way, since `setPage` counts `remoteTotal` and the host refetches on `(changed)`.
- **The page buttons are a labelled `role="group"`** (*Pages of runs*), **not a `<nav>`.** Several
  tables carry two pagers, and a page full of identically named navigation landmarks is
  landmark overuse.
- **Above and below, always** — one pager above the table and one below, outside the scroll
  wrapper, the second with `[announce]="false"`.
- **Pagers go outside `.gh-datatable-scroll`, never inside it**, or they scroll sideways with a
  wide table.

### 8f. Selection and lookup in a paged table

A paged, filtered table can hide the operator's own selection. The rules that keep it honest:

- **Hold the selection by id**, not by row index or object identity, so it survives paging,
  filtering and a reload.
- **Say what is off-screen.** Whenever the selection is non-empty, a line under the table reads
  *"3 selected — 2 not on this page"*, with a **Show selected only** toggle (an
  `aria-pressed` `.gh-filter-toggle` backed by a filter over the selected ids) beside *Clear
  Selection*.
  Without it an operator filters the list, sees one tick, and builds a three-row set they never
  inspected.
- **No header "select all" checkbox.** Over a filtered, paged list it means one of three
  different things — this page, these filtered rows, or everything — and a set built by
  accident is exactly what a comparability preview exists to prevent.
- **Every lookup searches the source list.** `openGroupById`, `selectGroup` and `deleteGroup`
  search `this.groups`, never `groupTable.view(...)`: a row that exists but sits on page 2 must
  still open.

### 8g. Where the data-table styles live

The `.gh-datatable` layer lives in **`styles.scss`**, beside `.gh-table`, for the reason
recorded there: emulated encapsulation rewrites a component selector to
`.gh-table[_ngcontent-benchmark]`, and the multi-run and suite-health child components would
never receive it.

- The classes are `.gh-datatable`, `.gh-datatable-scroll`, `.gh-datatable-toolbar`,
  `.gh-filter-row` / `.gh-filter-input` / `.gh-filter-select` / `.gh-filter-clear` /
  `.gh-filter-toggle`, `.gh-th-sortable` / `.gh-th-sort` / `.gh-sort-caret`, `.gh-pager` /
  `.gh-pager-size` / `.gh-pager-buttons` / `.gh-pager-status` / `.gh-pager-jump` /
  `.gh-pager-jump-input`, `.gh-page-btn`, `.gh-page-ellipsis` and `.gh-page-compact`.
- **Sticky header.** `position: sticky; top: 0` on the header cells, whose sticky context is
  the table's own scroll container (`.gh-datatable-scroll`), not the page. `border-collapse:
  collapse` on `.gh-table` makes a sticky header's *borders* vanish in some engines, so the
  header cells carry a background and a `box-shadow` instead of relying on the collapsed
  border. The "stuck" shadow is a **progressive enhancement** — `container-type: scroll-state`
  with `@container scroll-state(stuck: top)`, Chromium-only — and without it the header still
  sticks, it simply does not gain the shadow. No JavaScript fallback.
- `.gh-th-sort` is a transparent button inheriting header typography, so it has no border of
  its own: without an explicit `:focus-visible` outline a keyboard user sees nothing.
- **Scope every rule under `.gh-datatable` or a `.gh-`-prefixed class.** `styles.scss` is
  global; a selector written loosely enough to match `.admin-table` or `.modern-table` would
  restyle the users, groups and analytics tables without any of their specs noticing.
- `@media (prefers-reduced-motion: reduce)` disables the caret and row-hover transitions.

### 8h. Card lists

The Download Center's documents (`app-download-center-panel`) are the first card list; **Run
History** (`history-tab/`, `#bm-panel-history`) is the second; the **Multi-Suite** tab's battery
definitions (`app-benchmark-batteries`) are the third. Use one **when every row is a
small form** — two selects, a group of checkboxes, several actions — rather than values to compare
down a column, **or when a row is a record too rich for one table line**: Run History's runs carry a
kicker of badges, a badged model name, four metrics with visible qualifier lines, four actions and five
instrument hashes, which as a 12-column table overflowed sideways and hid its qualifiers in `title`
tooltips. A table fights such rows at every width: it overflows sideways, wraps a title over ten lines,
and below some breakpoint has to fake cards anyway. A list of few values to scan and compare stays a §8
table. Where a card list still has to scan like a table, give its metrics fixed-width columns so they
line up down the list at a wide width, as Run History's `.rh-metrics` does.

**Markup.**

- A `<ul role="list">` (the explicit role, because Safari drops list semantics under `display: grid`
  or `list-style: none`), labelled by the section heading, then one `<li>` per row holding an
  `<article aria-labelledby="{rowId}-title">`. The title is a heading one level under the section's
  (`<h5>` under an `<h4>`), so screen-reader users hear the count up front and jump card to card by
  heading.
- **Selection, where the list has one, is a real checkbox, top left** (Run History has none),
  keeping the row's `aria-label` (*Include …*). The title
  is a `<label for>` that checkbox, so a click on it selects the card. A selected card is shown by the
  tick, a gold border and a tint together (`:has(input:checked)`), never by color alone. **Never make
  the whole card a label or a click target**: it holds selects and buttons, and interactive content
  inside a label is invalid.
- Actions top right in a `role="group"` named *Actions for {row}* — never `role="toolbar"` without
  arrow-key roving (§4f) — holding the same §4 icon-only buttons a table row would.
- Options under a hairline, **each with a visible label** above its control; a checkbox group is a
  `<fieldset>` with a `<legend>`. Keep the per-row `aria-label`s (*Disclosure of …*): they contain the
  visible label (WCAG 2.5.3) and keep names distinct across cards (§4.1).
- A meta line under the title separates its parts with an `aria-hidden` dot and a visually hidden
  comma, so assistive technology does not read the parts run together.
- Layout by **the list's own width**: the list is an inline-size container, and the card's grid
  switches from *select · head · actions / options* to one column of head, actions and options below
  about 30rem. Run History's card is *head · metrics · actions / instrument*, puts the metrics under the
  head below 60rem and goes to one column below 30rem. No viewport media queries.

**The filter bar**, above the list, in this order:

1. A **search** field: `<input type="search">` with a real visually hidden `<label for>`, a
   decorative leading glyph (`.gh-search-field`). `.gh-search-field` pads `input.gh-input`,
   element-qualified, so a component-local `.gh-input` rule of equal specificity cannot reset the
   padding and slide the text under the glyph. Debounced (~200 ms) so the status line is not
   announced per keystroke. **Escape with text clears it at once** and calls `preventDefault()` and
   `stopPropagation()`, so the `<dialog>` around it stays open; with no text, Escape is left alone.
   Pair it with **Sort by**: one native `<select>` of named orders (*Newest first*, *Title (A–Z)*…)
   with a visible `<label>`, rather than a column and a direction toggle; remember the choice in
   `localStorage` behind `try/catch`.
2. The **facets**: one `app-filter-facet` per filterable field (below), each applied live — no Apply
   button — and listed only while its rows hold two values or more, or while it has a selection.
3. **Chips**: a `<ul role="list" aria-label="Active filters">` of `.gh-filter-chip` buttons, one per
   selected value and one for the search, each named *Remove filter {facet}: {value}* and ending in an
   `aria-hidden` *x* (§3a). Removing one moves focus to the next chip, else the previous, else the
   search field. **Clear all** (`.gh-filter-clear`) clears every filter except *Show selected only*,
   which has its own control, and focuses the search field.
4. The **selection bar** (§8f, adapted), in a list with selection: *N selected — M not shown*, **Show
   selected only**, **Clear selection**, **Select all N** (**Select all N matching** while a filter is
   active), and any action that applies to the selection.

The bar may be **sticky** at the top of the list's scroller where the width allows (the Download
Center: from 36rem of the panel); below that a phone cannot spare the height. A sticky box stops at its
scroller's padding edge: offset it by the negative padding, with matching padding of its own, and pin
the flush position in a spec. Run History's bar is not sticky yet: the admin page's scroller has not
been measured for that offset.

**`TableState` for card lists.** `anyOfFilter(row => value | values)` declares a multi-select column
whose selection lives in `valueFilters` (`setFilterValues`, `filterValues`); it matches a row when any
of the row's values is selected, case-insensitively, and never a row with none. `customFilter((row,
value) => boolean)` is for a search over several fields or a derived condition (a date range); it is
active while its string is non-blank. `setSort(column, direction)` serves a Sort by select.
`filterRowsExcept(rows, column)` applies every active filter but one column's — what a facet counts its
options against. `hasActiveFilters` and `clearFilters()` cover both kinds.

**`CardListState` — the state every card list shares** (`shared/data-table/card-list-state.ts`). All
three card lists build on it: the Download Center behind a private `list` getter (built on first use,
because its `idPrefix` is an input), Run History as `historyList`, the batteries tab as `list`
(`idPrefix: 'bb'`, `overseer.benchmark.batteries.view`). **A further card list uses it too, rather than
copying a host**; if it needs something the class lacks, extend the class and its spec. Like `TableState` it is plain TypeScript with no Angular dependency, touches no DOM
beyond the event it is handed and emits nothing, so a component owns it as an ordinary field and it
unit-tests on its own (`card-list-state.spec.ts`). It wraps a caller-supplied `TableState`, which keeps
the filter registrations, the sort accessors and the filtering.

```ts
readonly historyList = new CardListState(this.historyTable, {
  idPrefix: 'rh',                       // facet ids: rh-facet-{column}
  sorts: RUN_HISTORY_SORTS, defaultSort: 'newest',   // Newest first
  storageKey: 'overseer.benchmark.runHistory.view',
  facets: [{ column: 'suite', label: 'Suite', values: r => r.suiteName }, /* … */],
  singleFacets: [{ column: 'started', label: 'Started', anyLabel: 'Any time', options, matches }],
  onChange: () => this.cdr.detectChanges()
});
```

- **Options** (`CardListStateOptions<T>`): `idPrefix`; `batch` (10); `searchColumn` (`search`, a
  `customFilter` column); `debounceMs` (200); `sorts` (`CardListSort`: `id`, `label`, `column`,
  `direction`), `defaultSort` and `storageKey`, under which the choice is stored as `{ version: 1, sort }`
  behind `try/catch` (an unknown or unreadable value falls back to `defaultSort`); `facets`
  (`CardListFacetSpec<T>`: `column`, `label`, `values` — the same accessor registered with
  `anyOfFilter` — and optionally `order` as `'alphabetical'` (the default), a fixed list or a
  comparator, `labelOf`, `anyLabel` and `enabled`); `singleFacets` (`CardListSingleFacetSpec<T>`:
  `column`, `label`, `anyLabel`, `options`, `matches(row, value)`, optionally `listedWhen` and
  `enabled`); `facetOrder`, the column order of the combined list; `memoDeps`, further inputs the facets
  depend on, compared element by element with `===` (the Download Center's chart actions and their
  settings hash); and `onChange`, called after a debounced search applies, outside any event handler —
  `detectChanges()` in a Default component, `markForCheck()` in an OnPush one.
- **State:** `visibleCount`, `searchText`, `sortId`.
- **Batching:** `matching(rows)`, `view(rows)` (the first `visibleCount` matching rows),
  `remainingCount(rows)`, `nextBatchCount(rows)`; `showMore(rows)` and `showAll(rows)` return **the index
  of the first new card**, for the host to focus; `resetBatch()`.
- **Search and sort:** `setSearchInput(text)` (debounced); `clearSearchOnEscape(event)` returns true
  when it cleared text and stopped the event, false for an empty field or another key;
  `setSort(id)` applies, stores and resets the batch.
- **Facets and chips:** `setFacet(column, values)`; `facets(rows)` and `chips(rows)`
  (`CardListFacet`, `CardListChip`), **memoized** on an internal revision, the rows array's identity and
  `memoDeps`, so a change-detection pass gets the same array back; `invalidate()` bumps the revision for
  a change the host makes itself (a selection, a reload into the same array); `removeChip(chip, rows?)`
  returns the chip's index before removal, for the host's focus logic; `clearFilters(keepColumns)` (the
  Download Center keeps `['selected']`).
- **Lifecycle:** `reset()` starts a new list — clears the search, its timer and every filter, returns to
  page 1, restores the stored sort and resets the batch; `statusText(rows, { one, many })` is the status
  line; `dispose()` cancels a pending search, from `ngOnDestroy`.

**What stays in the host:** the markup, the selection and its bar, and **focus** — after a chip is
removed (next chip, else the previous, else the search field), after **Clear all** (the search field),
after Show more (the title of the card at the returned index) and after a delete. Thin handlers update
the state, run change detection, then move focus. The host also calls `invalidate()` after replacing its
rows where the array identity alone would not show it.

**`app-filter-facet`** (`shared/data-table/filter-facet.component.ts`), standalone and `OnPush`:

| Input / output | Meaning |
|---|---|
| `facetId` | Unique in the document; the trigger (`{facetId}-trigger`), popover (`-popover`), options (`-opt-{n}`, `-opt-any`), option search (`-search`) and the anchor name derive from it |
| `label` | The facet's name: the trigger reads *{label}*, then a count badge with hidden *selected* (*Document 1 selected*) |
| `options` | `{ value, label, count }[]`, from a memoized getter (§4e's rule) |
| `selected` | The host's selection, fed back; the facet emits and never owns it |
| `mode` | `multiple` (checkboxes) or `single` (radios, `anyLabel` first, which emits `[]`) |
| `noun` | The hidden text after each count (*Executive Summary, 4 documents*); it defaults to *documents*, so any other list must pass its own (Run History: `noun="runs"`) |
| `selectedChange` | Emitted on every change; the popover stays open |

- The trigger is a `.gh-facet-btn` pill with `popovertarget` and `aria-expanded` bound from the
  popover's `toggle` event, ending in a *chevron* state glyph; gold while a value is selected
  (`.is-active`, since no ARIA state fits a filter button, and the badge text carries it too) or
  while open.
- The panel is a `popover="auto"` `.gh-facet-popover` holding a `<fieldset>` whose `<legend>` reads
  *Filter by {label}*. Each option shows its count right-aligned; a count of 0 is dimmed but stays
  operable, so a selected option that now matches nothing can still be cleared. **Clear {label}
  filter** appears while anything is selected. Above 10 options a visually labelled *Filter options*
  search narrows the options shown; options it hides keep their selection.
- Focus goes to the option search, else the first option, on open, and back to the trigger on close.
  **Escape** closes the popover only (`preventDefault()` and `stopPropagation()`), as §4f requires.
- Explicit anchor names on both ends by `[attr.style]`, the §4f fallback chain, a capped scrolling
  height, `ensureOverlayPolyfills()` and `refreshAnchorPositioning()` on open — the §4.2 and §4f rules.
- **Counts are memoized** — by `CardListState.facets(rows)`, on its revision, the rows array's identity
  and `memoDeps`; the host calls `invalidate()` whenever a row's selection or the package changes. A
  fresh array per change-detection pass would re-render every facet, and in Run History would rerun
  the O(n) `instrumentChangeOf` behind the *Changes* facet for every run.

**Load more**, not numbered pages, for card lists: cards differ in height, so a page of ten is not a
stable screen, and a selection spread over pages is what §8f has to warn about. Show a batch (10),
then **Show N more** (`.btn-ghost`) and, while more than one batch remains, **Show all M**
(`.gh-filter-clear`), both text-only in a centered `.gh-load-more`. After either, **focus the first
new card's title** (an `<h5 tabindex="-1">` with a `:focus-visible` ring) — the card at the index
`CardListState.showMore` / `showAll` returns — so a keyboard user carries on where the new content
starts. `CardListState` returns to one batch when a filter, the search, a chip or the sort changes, and
keeps the count on a reload of the same list and after a delete, because the host calls nothing then. Infinite scroll is not used: an
explicit button is predictable for keyboard and screen-reader users. The list's **one polite live
region** is the status line in its header (`.gh-list-status`, *Showing 10 of 23 documents*, *·
filtered from 40* while a filter is active).

**Two kinds of card in one list: Run History's battery runs** (2026-10-03). Run History lists single
runs and battery runs together. The list is **a union over `HistoryItem`** —
`{ kind: 'run', key, run } | { kind: 'battery', key, battery }` — built and memoized by the workspace
store (`historyItems`, from `mergeHistoryItems`), ordered by start time, newest first; `historyList` and
every `historyTable` accessor (search, facets, flags, sorts) read either kind, and a **Kind** facet
(*Single run* / *Battery run*) filters by it. Rules this pattern carries:

- **Keep the source list in its own order.** Helpers that depend on the server's run order
  (`instrumentChangeOf`, `completedRunsOfSelectedSuite`) keep reading `historyRuns`; the union is for
  rendering and filtering only, so an interleaved battery card moves no *INSTRUMENT CHANGED* badge.
- **A different kind is a different card, in the same grid.** `article.rh-card.rh-card-battery` shares
  `.rh-card`'s grid and container queries, and its kicker badge (*Battery run #N*,
  `.rh-battery-run-badge`) is distinct from a member run's *Battery #id · suite s/K*, so the two are
  never confused. Its actions follow §4 (icon-only, `aria-disabled` with a tooltip reason when
  unavailable); its delete is its own confirmation, with an unchecked *Also delete its M member runs*.
- **A toggle that hides rows is not a filter.** **Show battery member runs** is a `gh-filter-toggle`
  with `aria-pressed`, off by default, in the list head beside **Refresh**: member runs are left out of
  `historyItems` while it is off. It is **not a chip**, **Clear all** keeps it (as the Download Center
  keeps *Show selected only*), and it is stored per browser behind `try/catch`
  (`overseer.benchmark.runHistory.members`). Facet counts cover the rows it lets through.
- **The status line counts each card once** — a battery run is one run — and adds a note per endpoint
  whose limit was reached (*· Only the newest 1000 runs are loaded*, *· newest 500 battery runs*).

**The Multi-Suite tab's battery cards** are the third list, with the same filter bar, chips, status line
and batches, shown with more than three batteries or while a filter is active. **Show archived (n)** is
a `gh-filter-toggle` view setting, not a chip, kept by **Clear all**. The cards are **full width, one per
row**, not a fixed-width grid: a grid of 22 rem cards turns ragged when one battery has 2 suites and its
neighbor 12, while a full-width card pushes only itself down. Each card's grid is
`"head metrics actions" / "suites suites suites"` in the `bb-cards` inline-size container (metrics under
the head below 60 rem, one column below 30 rem); its metrics are fixed 7.5 rem columns behind a hairline,
as Run History's are; and its suite table shows four rows, the rest in a hidden `<tbody>` behind a
link-style **Show all K suites** / **Show fewer** (`aria-expanded`, `aria-controls`, focus stays on it), so
a card is never taller than four suite rows unless asked.

**Global classes** (`styles.scss`, beside `.gh-datatable`): `.gh-filter-bar` / `.gh-filter-bar-row`,
`.gh-search-field`, `.gh-facet-row` (wraps; below 36rem of the bar one row that scrolls sideways with
scroll snapping), `.gh-facet-btn` / `.gh-facet-count` / `.gh-facet-chevron`, `.gh-facet-popover` /
`.gh-facet-group` / `.gh-facet-legend` / `.gh-facet-option` / `.gh-facet-option-label` /
`.gh-facet-option-count` / `.gh-facet-search` / `.gh-facet-clear`, `.gh-filter-chips` /
`.gh-filter-chip` / `.gh-filter-chip-facet` / `.gh-filter-chip-value`, `.gh-list-status` and
`.gh-load-more`. The card itself is component-local.

### 8i. Clear selection in a selection band

A **selection band** sums up what the operator picked in tables elsewhere on the step and lets them
drop it without scrolling back: the Model Comparison wizard's step 1 band (`.mc-wizard-selection`,
`role="region"` labelled by `#mc-selection-label`) is the reference.

- **The label is the band's one `role="status"`** — *Your selection — 3 runs and 1 group
  selected* — and its programmatic focus target, `tabindex="-1"`. It stays rendered when nothing is
  selected, its text then visually hidden (*Your selection — nothing selected yet*), so a screen
  reader still hears the state.
- **Clear selection (N)** is `btn-gh btn-gh-cancel btn-gh-small` — a dismissal in a tight row, so blue,
  small and text-only (§2, §3a) — labelled with the count, and **first in the chip row**, so it is seen
  and reached by keyboard before the chips. It exists only while something is selected. After it,
  **focus moves to the band's label**, because the button that held focus is gone.
- **Each chip's remove button** is a 32 × 32 `.action-btn` with the *x* glyph, named for its subject
  (*Remove {detail} {label} from the selection*) with a hint tooltip (§4). Removing a chip moves
  focus to the **next chip's** remove button, else to **Clear selection**, else to the label.
- **One visible empty-state message.** With nothing selected the band shows one alert — *Nothing is
  selected yet* / *Select at least one completed run, analysis group or battery result in the tables
  above. Compare stays unavailable until you do.* — and no second hint line repeating it. The step's
  disabled **Compare** and the footer state the same fact once, in their own words (*Select at least
  one run, analysis group or battery result.*), as a disabled reason (§6).
- Notices about the selection sit under the chips in a sibling `aria-live="polite"` list, never inside
  the `role="status"` label, which would double-announce.

---

## 9. Checklist

Diff this against your markup before calling button, tab or table work finished.

**Buttons**
- [ ] Every labelled action button is `.btn-gh` with a variant from the table in §2 — no invented names.
- [ ] No component stylesheet redefines `.btn-gh`, `.action-btn`, or `.btn-icon-action`.
- [ ] Every `<button>` has `type="button"`.
- [ ] **Each icon was decided on its own merits (§3a):** for every labelled button with an
      icon, deleting the label would leave a glyph that still says what the button does.
- [ ] **No icon on a dismissal (Done, Close, Cancel) or a plain commit (Save X).**
- [ ] No glyph carries two different meanings anywhere in the application.
- [ ] Icons are 16×16 Feather geometry, `class="btn-icon"`, `aria-hidden="true"`, leading the label.
- [ ] **No row mixes `.btn-gh` with `.btn-gh-small`** — same height and text size throughout a row.
- [ ] The label clears the end ornaments; `.btn-gh`'s horizontal padding was not reduced.
- [ ] A row with more than three labelled actions has one `.btn-gh` and the rest `.btn-ghost`.

**Icon-only buttons**
- [ ] Every one has an `aria-label` that names its **subject**, unique among its siblings.
- [ ] No `title` attribute on any button.
- [ ] Each has an `interestfor` tooltip with a matching `popover="hint"` `.gh-tooltip`.
- [ ] Anchor names are set on **both** ends via `[attr.style]`, and are unique per row.
- [ ] `ensureOverlayPolyfills()` is called in `ngOnInit`.

**File pickers**
- [ ] Every single-file upload is `app-file-picker` (§4a); no bare `<input type="file" class="gh-input">` remains.
- [ ] Each picker's `inputId` is unique in the document, and the host clears its state on `(cleared)`.

**Info buttons and settings sections**
- [ ] A hint moved out of a paragraph is an `app-info-tip` (§4b) named *About {subject}*, with a
      document-unique `tipId`.
- [ ] The control the hint describes keeps `aria-describedby` pointing at that `tipId` — except in
      dialog mode (`trigger="dialog"`), where nothing points at it, and where the tip explains static
      text rather than a control.
- [ ] A click-mode tip (`trigger="click"`) follows its control in a `.gh-field-row`; its popup has
      no `role`, no `interestfor` and no close button, and the button's `aria-expanded` follows it.
- [ ] Warnings, advisories and the reason a control is disabled stay visible, never in a popup.
- [ ] A long settings panel is a stack of non-exclusive `.gh-disclosure--section` elements (§5b),
      each summary holding text only, its read-out `aria-hidden`.
- [ ] Section open state lives in `localStorage` behind `try/catch`, with a default when absent.
- [ ] Radio groups inside a section keep their own `<fieldset>` and `<legend>`.
- [ ] A section reset button sits after `<details>` in a positioned wrapper, outside both the summary
      and the body, and is named *Reset {title} to defaults*.

**Action popovers**
- [ ] A trigger revealing several related, worded actions uses `.gh-action-popover` /
      `.gh-action-popover-item` (§4f): `popover="auto"`, `role="group"` with an accessible name, no
      `role="menu"`.
- [ ] The trigger's `aria-expanded` follows the `toggle` event; focus goes to the first enabled item
      on open and back to the trigger on close; Escape closes only the popover (`stopPropagation`).
- [ ] Explicit anchor names on both ends, the §4f fallback chain (a new anchored panel gets a
      `.gh-anchor-native` rule), and `ensureOverlayPolyfills()`.
- [ ] Unavailable items are `aria-disabled` with their reason on a second line, never removed.
- [ ] A cluster of a few Tab-stop buttons is `role="group"` with an `aria-label`; `role="toolbar"`
      only with arrow-key roving focus.

**Repair and retry actions**
- [ ] Each action has one permanent home per surface, plus at most one contextual button in the alert
      that states the problem (§4g); battery Continue and Re-run live only in the progress dialog.
- [ ] Its verb is the §4g one, in sentence case, with that verb's glyph; a confirm button's label is
      its item's label, with no overriding `aria-label`.
- [ ] Availability comes from the `run-repair-actions.ts` gate, never a template condition of its own;
      a refused action is `aria-disabled` with its visible reason, never `[disabled]` and never enabled.

**Pane resizers**
- [ ] A user-resizable pane uses `app-pane-resizer` (§4d), named for the pane, with `aria-controls`
      pointing at it, and the width is persisted on `valueCommit` only.

**Model pickers**
- [ ] Every model choice is `app-model-picker` (§4e); no hand-rolled `.custom-model-selector` remains.
- [ ] A choice of several items an action acts on is `app-multi-picker` (§4e-2), or for models
      `app-model-multi-picker` (§4e-3) — never `app-filter-facet`, which only filters a list; an
      `optionTemplate` renders text only, and a picker choosing which models a report covers shows no
      price or parallel badge.
- [ ] Each picker has `labelledBy` (a visible label's id) or, with no visible label, `label`; its
      hint, where there is one, is passed as `describedBy`.
- [ ] `options` comes from a memoized getter, not a new array on every change-detection pass.
- [ ] Price and parallel badges are opted into with `showPrice` / `showParallel`, not re-added by hand.
- [ ] A role played by a model is a `tag`, not part of `displayName`.
- [ ] No component stylesheet reaches into `.selector-trigger`, `.selector-dropdown` or `.model-option`.

**Tabs**
- [ ] Every tab in the row has an icon, or none of them does.
- [ ] `role="tablist"` with an `aria-label` that does not contain the word "tabs".
- [ ] Every tab: `role="tab"`, `aria-selected`, `aria-controls`, roving `tabindex`.
- [ ] Every panel: `role="tabpanel"`, `id`, `aria-labelledby`, `tabindex="0"` — and not on a `<table>`.
- [ ] Arrow keys move and wrap; Home/End work; focus follows selection.
- [ ] Appearance is driven by `[aria-selected="true"]`, with no parallel active class.
- [ ] A nested row uses `.gh-tabs-secondary`, or `.gh-tabs-segmented` for a small fixed set of
      views under another tab row.

**Data tables**
- [ ] The table renders `state.view(sourceRows)`; **no source array is sorted in place**, and
      every helper that depends on server order still reads the source list.
- [ ] Sortable headers are `<th app-sort-header>`; `aria-sort` is on the `<th>` and nothing
      else styles the sorted state.
- [ ] Every filter control has a `visually-hidden` `<label for>` — a placeholder is not a label.
- [ ] `<select>` filter columns are declared with `exactFilter()`, not the substring default.
- [ ] "Nothing recorded yet" and "nothing matches these filters" are distinct empty states.
- [ ] Pager end buttons are `aria-disabled`, not `disabled`, and each handler refuses on its own.
- [ ] Exactly one pager per table has `[announce]="true"`.
- [ ] No pager inside `.gh-datatable-scroll`.
- [ ] On/off view filters are `.gh-filter-toggle` with `aria-pressed`.
- [ ] Selection is held by id; the off-page selection count and **Show selected only** are
      present; there is no header "select all"; every lookup searches the source list.
- [ ] New styles are scoped under `.gh-datatable` or a `.gh-`-prefixed class in `styles.scss`.

**Card lists**
- [ ] Rows that are small forms are a card list (§8h): `ul[role="list"]`, one `article[aria-labelledby]`
      per row, its title a heading one level under the section's.
- [ ] The list's search, sort, facets, chips, status line and batching come from `CardListState`
      (`shared/data-table/card-list-state.ts`), not a copy of another host's code.
- [ ] Selection, where there is one, is a real checkbox with the title as its `<label>`; the whole
      card is not a label.
- [ ] Facets are `app-filter-facet`, counted against the other filters, from a memoized getter, with
      `noun` set unless the list counts documents.
- [ ] Escape in the search clears its text without closing the dialog around it, and passes through
      when the field is empty.
- [ ] Load more focuses the first new card's title, and returns to one batch on a filter, search or
      sort change.
- [ ] Exactly one polite live region: the list's status line.
- [ ] A selection band (§8i) leads its chip row with **Clear selection (N)**, moves focus to its
      `tabindex="-1"` label after it, and shows one visible empty-state message.

**All controls**
- [ ] Visible `:focus-visible` ring; no unreplaced `outline: none`.
- [ ] Transitions respect `prefers-reduced-motion`.
- [ ] Design tokens, not hardcoded hex.
- [ ] `npm run test:headless` and `npm run build` both pass.
