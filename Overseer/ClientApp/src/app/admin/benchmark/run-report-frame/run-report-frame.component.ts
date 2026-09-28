import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

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
 */
@Component({
  selector: 'app-run-report-frame',
  standalone: true,
  templateUrl: './run-report-frame.component.html',
  styleUrls: ['./run-report-frame.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RunReportFrameComponent {
  /** The accessible name of the main section. */
  @Input() mainLabel = 'Report';

  /** The accessible name of the side section. */
  @Input() asideLabel = 'Details';

  /** Marks the body `aria-busy` while the host loads or refreshes its content. */
  @Input() busy = false;
}
