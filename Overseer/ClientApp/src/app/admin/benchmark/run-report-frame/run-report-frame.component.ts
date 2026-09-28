import { ChangeDetectionStrategy, Component, ElementRef, Input, ViewChild } from '@angular/core';

/**
 * The layout of a full-screen report: a header (identity left, actions right), an optional tab row
 * under it, and a body.
 *
 * Presentational only. The host owns the `<dialog class="gh-dialog gh-dialog-fullscreen …">` and
 * places this frame inside it, projecting into these slots:
 *
 * - `[runReportHeader]`: the title and status, left of the header;
 * - `[runReportActions]`: the action buttons, right of the header;
 * - `[runReportTabs]`: a tab row under the header, always visible with it;
 * - `[runReportMain]`: the main column (`layout="columns"`);
 * - `[runReportAside]`: the side column (`layout="columns"`);
 * - `[runReportBody]`: the whole body (`layout="single"`), such as the host's tab panels.
 *
 * `columns` (the default): the header sticks while the frame scrolls, and from 60rem of body width
 * the two labeled, focusable sections sit side by side, each scrolling on its own; below it they
 * stack. `single`: the header and tab row stay put and the body is the one scroller.
 */
@Component({
  selector: 'app-run-report-frame',
  standalone: true,
  templateUrl: './run-report-frame.component.html',
  styleUrls: ['./run-report-frame.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.rrf-layout-single]': "layout === 'single'" }
})
export class RunReportFrameComponent {
  /** The accessible name of the main section. */
  @Input() mainLabel = 'Report';

  /** The accessible name of the side section. */
  @Input() asideLabel = 'Details';

  /** Marks the body `aria-busy` while the host loads or refreshes its content. */
  @Input() busy = false;

  /** Two columns, or one body under a tab row. */
  @Input() layout: 'columns' | 'single' = 'columns';

  @ViewChild('body', { static: true }) private body?: ElementRef<HTMLElement>;

  /** Scrolls the body back to its top, as when the host switches tabs. */
  scrollBodyToTop(): void {
    if (this.body) {
      this.body.nativeElement.scrollTop = 0;
    }
  }
}
