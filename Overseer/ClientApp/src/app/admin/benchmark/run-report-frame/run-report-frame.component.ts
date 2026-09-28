import { ChangeDetectionStrategy, Component, Input, OnInit } from '@angular/core';

/** Makes each frame's figures id unique in the document. */
let nextFrameId = 0;

/**
 * The layout of a full-screen report: a sticky header (identity left, actions right), a strip of key
 * figures, and a body of two labeled, focusable sections that sit side by side, each scrolling on
 * its own, from 60rem of body width, and stack below it.
 *
 * Presentational only. The host owns the `<dialog class="gh-dialog gh-dialog-fullscreen …">` and
 * places this frame inside it, projecting into five slots:
 *
 * - `[runReportHeader]`: the title and status, left of the header;
 * - `[runReportActions]`: the action buttons, right of the header;
 * - `[runReportFigures]`: one element per key figure, each a cell of the strip's grid;
 * - `[runReportMain]`: the main column (findings, questions);
 * - `[runReportAside]`: the side column (configuration, cost, tool usage).
 *
 * With `figuresCollapsible`, a bar above the strip holds a disclosure toggle for it and two more
 * slots: `[runReportFiguresSummary]`, one line shown only while the strip is collapsed, and
 * `[runReportFiguresActions]`, right-aligned. The collapsed strip stays in the DOM, `hidden`.
 */
@Component({
  selector: 'app-run-report-frame',
  standalone: true,
  templateUrl: './run-report-frame.component.html',
  styleUrls: ['./run-report-frame.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RunReportFrameComponent implements OnInit {
  /** The accessible name of the main section. */
  @Input() mainLabel = 'Report';

  /** The accessible name of the side section. */
  @Input() asideLabel = 'Details';

  /** Marks the body `aria-busy` while the host loads or refreshes its content. */
  @Input() busy = false;

  /** Adds the key-figures bar with its disclosure toggle. Without it the strip is always shown. */
  @Input() figuresCollapsible = false;

  /** The toggle's visible label. */
  @Input() figuresLabel = 'Key figures';

  /** Where the collapsed state is remembered (`"1"` / `"0"`); null remembers nothing. */
  @Input() figuresStorageKey: string | null = null;

  /** The strip's id, for the toggle's `aria-controls`. */
  readonly figuresId = `rrf-figures-${++nextFrameId}`;

  /** Whether the strip is collapsed; expanded by default. */
  figuresCollapsed = false;

  ngOnInit(): void {
    if (this.figuresCollapsible && this.figuresStorageKey) {
      try {
        this.figuresCollapsed = localStorage.getItem(this.figuresStorageKey) === '1';
      } catch {
        this.figuresCollapsed = false;
      }
    }
  }

  toggleFigures(): void {
    this.figuresCollapsed = !this.figuresCollapsed;
    if (this.figuresStorageKey) {
      try {
        localStorage.setItem(this.figuresStorageKey, this.figuresCollapsed ? '1' : '0');
      } catch {
        // Storage unavailable: the state lasts until the frame closes.
      }
    }
  }
}
