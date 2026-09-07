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
  paged table honest about selection. Read before adding or restyling any button,
  icon button, toolbar, tab row, or data table.
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
| play | Start Benchmark, Acknowledge & Start Run | "This begins now", and it reinforces the consequence of a button that starts real work |
| trash | Delete Runs, Delete All Suite Runs | Destructive. The redundancy is *wanted*: a second signal before an irreversible action |
| refresh / rotate | Refresh, Re-score Run, Re-run Failed Questions | "This runs again" — the circular-arrow convention is universal |
| download | Download Markdown Report | "A file arrives on your disk" |
| upload | Import Default Suite | Direction of data movement, which the word "Import" alone does not picture |
| star | Set Default | The marker used for the default item elsewhere in the UI; the icon *is* the concept |
| chevron | Show / Hide Model Reasoning | A **state** indicator: which way it points says whether the section is open |

**Leave the icon off when the label is already the whole message:**

| Buttons | Why no icon |
|---------|-------------|
| **Done, Close, Cancel** | Dialog dismissal. The word is unambiguous in every language and context, the button's position in the footer already says "this ends the dialog", and `.btn-gh-cancel`'s blue already distinguishes it. A tick or an ✕ adds a second thing to look at and no meaning. |
| **Save Profile, Save Suite, Save Question** | A plain commit. "Save X" cannot be misread. A floppy-disk glyph is a skeuomorph for hardware most users have never seen, and it does not say *what* is being saved — the label does. |
| **Scoring Profiles, Manage Questions** | Opens a management view. The only available glyphs are generic — a gear reads as "application settings", which this is not; a book reads as nothing in particular. Both mislead slightly and inform not at all. |
| **Assess Question Difficulty, AI Auto-Rate All Difficulties, Assess Difficulty** | The label already names the operation *and* says it is the AI one. These carried a circle-with-diamond and a lightning bolt; neither survives the test — a bolt alone could mean AI, or fast, or power. "Marks the AI action" is not enough justification when the word "AI" or "Assess" is right there. |

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

---

## 5. Tabs

Use the shared `.gh-tabs` / `.gh-tab` widget from `styles.scss`. It is an underline
treatment with horizontal scroll-snap and scroll-edge indicator support.

| Class | Use |
|-------|-----|
| `.gh-tabs` | The `role="tablist"` container |
| `.gh-tab` | Each tab button |
| `.gh-tabs-secondary` | Modifier on the container for a **nested** row — smaller type, tighter spacing, fainter rule, so it reads as subordinate to the row above it |

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
- Use the design tokens: `var(--primary-color)`, `var(--gold-glow)`,
  `var(--border-glass)`, `var(--nav-color)`. Not `#e0ba6d`, which *is* `--primary-color`
  and will not follow it if the theme ever changes.
- For the **MobileGnollHackLogger** Razor pages the equivalent source is
  `wwwroot/css/site2.scss`, and the generated CSS is never edited directly — see
  [`scss_compilation`](../scss_compilation/SKILL.md).

---

## 8. Data tables

The three benchmark tables — **Run History**, **Runs in this group** and **Analysis groups** —
share one implementation of paging, column sorting and column filtering. Three hand-rolled
copies in one tab would be three places for the same bug.

The layer is deliberately **headless plus presentational**, not a generic `<app-data-table>`.
These tables' cells carry tier badges, score badges, instrument fingerprints, tooltips and
three-button action groups; funnelling all of that through a column-definition DSL would
produce worse markup than hand-written rows. What the tables genuinely share is the *state
arithmetic*, the *pager markup* and the *styling*.

| Piece | File |
|---|---|
| `TableState<T>`, `exactFilter()`, `PAGE_SIZES` | `shared/data-table/table-state.ts` |
| `<th app-sort-header>` | `shared/data-table/sort-header.component.ts` |
| `<app-table-pager>` | `shared/data-table/table-pager.component.ts` |
| `.gh-datatable` and the `.gh-pager` / `.gh-filter-*` families | `styles.scss` |

### 8a. When a list becomes a table

The threshold is **paging, sorting or per-column filtering**. A five-row list that is never
sorted stays a plain `.gh-table`; adding a pager to it is noise.

**A checkbox list people scan and filter is a table.** *Runs in this group* was a
`<fieldset class="mr-run-picker">` with a `<legend>` and one `<label>` per row. As a table it
uses `<caption>` in place of the `<legend>` — `<caption>` is a table's native naming
mechanism — the `<fieldset>` is dropped, and each checkbox carries its own `aria-label`
naming its subject (*"Include run 21 in this group"*), exactly as §4 requires of any control
whose visible text is not its name.

### 8b. The `TableState<T>` contract

A component owns one `TableState` per table as an ordinary field, declares its accessors once,
and renders `state.view(sourceRows)`:

```ts
historyTable = new TableState<BenchmarkRunSummaryDto>('id', 'desc').registerAccessors(
  { id: r => r.id, suiteName: r => r.suiteName, qualityIndex: r => r.qualityIndex ?? null },
  { suiteName: r => r.suiteName, status: exactFilter(r => r.status) }
);

get historyView(): BenchmarkRunSummaryDto[] { return this.historyTable.view(this.historyRuns); }
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
> `benchmark.component.ts`'s `instrumentChangeOf` locates a run by `indexOf` in `historyRuns`
> and then treats later entries as chronologically earlier, and `completedRunsOfSelectedSuite`
> takes `.slice(0, 5)` as "the five newest". Both are correct only against server order. Under
> a user-chosen sort, in-place sorting would silently move the *INSTRUMENT CHANGED* badges onto
> the wrong runs. Never sort a source array in place; keep helpers reading the source list and
> give only the template the view.

### 8c. Sortable headers

```html
<th app-sort-header [state]="historyTable" column="qualityIndex" label="Index"
    (changed)="cdr.detectChanges()"></th>
```

The component's host element **is** the `<th>` (hence the attribute selector), so there is no
wrapper between the row and the cell.

- **`aria-sort` on the `<th>` is the single source of truth.** The caret is drawn in CSS from
  `th[aria-sort="ascending"]` / `[aria-sort="descending"]` — never from a parallel `.active`
  class that could disagree with what is announced.
- The label is a real `<button>`, so keyboard operability and Enter/Space come free.

> **`admin.component.html`'s users table is the old pattern — do not copy it.** It puts
> `(click)` on a bare `<th>` with a `▲`/`▼` span and no `aria-sort`: it is not keyboard-operable
> and announces nothing. It is out of scope rather than correct, and can adopt this layer later.

### 8d. The filter row

A `<tr class="gh-filter-row">` sits directly under the header row, one `<td>` per column,
empty where a column is not filterable.

- **Every control gets a real `<label class="visually-hidden" for="…">`.** A placeholder is not
  a label; it disappears the moment typing starts and several screen readers never announce it.
- Build a `<select>`'s options from the values **actually present** where you can, so an option
  the backend stops emitting disappears from the control on its own.
- A **Clear filters** control renders only when `state.hasActiveFilters`.
- **Two empty states, not one.** `rows.length === 0` means nothing has been recorded yet;
  `state.noMatches(rows)` means the filters hide everything. They want different messages, and
  the second one offers *Clear filters*. One message for both causes is a support ticket.
- A server-side query control (Run History's *Filter by Suite*, which changes what is fetched)
  is **not** a column filter. Keep it in the toolbar, left of the column filters, and let its
  `<label>` say which of the two it is.

### 8e. The pager

```html
<app-table-pager [state]="historyTable" [rows]="historyRuns" noun="runs"
                 (changed)="cdr.detectChanges()"></app-table-pager>
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

### 8f. Selection and lookup in a paged table

A paged, filtered table can hide the operator's own selection. The rules that keep it honest:

- **Hold the selection by id**, not by row index or object identity, so it survives paging,
  filtering and a reload.
- **Say what is off-screen.** Whenever the selection is non-empty, a line under the table reads
  *"3 selected — 2 not on this page"*, with a **Show selected only** toggle (an
  `aria-pressed` button backed by a filter over the selected ids) beside *Clear Selection*.
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
  `.gh-filter-row` / `.gh-filter-input` / `.gh-filter-select` / `.gh-filter-clear`,
  `.gh-th-sortable` / `.gh-th-sort` / `.gh-sort-caret`, `.gh-pager` / `.gh-pager-size` /
  `.gh-pager-buttons` / `.gh-pager-status`, `.gh-page-btn` and `.gh-page-ellipsis`.
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

**Icon-only buttons**
- [ ] Every one has an `aria-label` that names its **subject**, unique among its siblings.
- [ ] No `title` attribute on any button.
- [ ] Each has an `interestfor` tooltip with a matching `popover="hint"` `.gh-tooltip`.
- [ ] Anchor names are set on **both** ends via `[attr.style]`, and are unique per row.
- [ ] `ensureOverlayPolyfills()` is called in `ngOnInit`.

**Tabs**
- [ ] Every tab in the row has an icon, or none of them does.
- [ ] `role="tablist"` with an `aria-label` that does not contain the word "tabs".
- [ ] Every tab: `role="tab"`, `aria-selected`, `aria-controls`, roving `tabindex`.
- [ ] Every panel: `role="tabpanel"`, `id`, `aria-labelledby`, `tabindex="0"` — and not on a `<table>`.
- [ ] Arrow keys move and wrap; Home/End work; focus follows selection.
- [ ] Appearance is driven by `[aria-selected="true"]`, with no parallel active class.
- [ ] A nested row uses `.gh-tabs-secondary`.

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
- [ ] Selection is held by id; the off-page selection count and **Show selected only** are
      present; there is no header "select all"; every lookup searches the source list.
- [ ] New styles are scoped under `.gh-datatable` or a `.gh-`-prefixed class in `styles.scss`.

**All controls**
- [ ] Visible `:focus-visible` ring; no unreplaced `outline: none`.
- [ ] Transitions respect `prefers-reduced-motion`.
- [ ] Design tokens, not hardcoded hex.
- [ ] `npm run test:headless` and `npm run build` both pass.
