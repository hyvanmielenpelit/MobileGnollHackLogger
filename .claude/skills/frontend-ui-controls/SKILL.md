---
name: frontend-ui-controls
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

The full skill lives in this repository's tool-neutral agent directory (`.agents/`),
which is shared with other AI coding agents. This file is only a pointer.

Read `.agents/skills/frontend_ui_controls/SKILL.md` (path relative to the repository root) in full
before proceeding, and follow it. Any `references/` files it links are relative to that
same directory.