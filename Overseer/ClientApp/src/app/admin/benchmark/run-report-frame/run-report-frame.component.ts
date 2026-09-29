import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';

import { PaneResizerComponent } from '../../../shared/pane-resizer/pane-resizer.component';

/** The sidebar's narrowest width, in CSS px (20rem). */
export const RRF_SIDEBAR_WIDTH_MIN = 320;
/** The sidebar's width until the user resizes it, in CSS px (24rem). */
export const RRF_SIDEBAR_WIDTH_DEFAULT = 384;
/** The sidebar's widest width, in CSS px (32rem); never more than 40 % of the body either. */
export const RRF_SIDEBAR_WIDTH_MAX = 512;
/** The share of the body the sidebar may take at most. */
export const RRF_SIDEBAR_MAX_SHARE = 0.4;

let nextFrameId = 0;

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

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
 * - `[runReportSidebar]`: the sidebar (`layout="sidebar"`);
 * - `[runReportMain]`: the main column (`layout="columns"` and `layout="sidebar"`);
 * - `[runReportAside]`: the side column (`layout="columns"`);
 * - `[runReportBody]`: the whole body (`layout="single"`), such as the host's tab panels.
 *
 * `columns` (the default): the header sticks while the frame scrolls, and from 60rem of body width
 * the two labeled, focusable sections sit side by side, each scrolling on its own; below it they
 * stack. `sidebar`: the same, with a resizable sidebar first and the main column after it, the
 * resizer in its own track between them; below 60rem they stack, sidebar first, with no resizer.
 * `single`: the header and tab row stay put and the body is the one scroller.
 */
@Component({
  selector: 'app-run-report-frame',
  standalone: true,
  imports: [PaneResizerComponent],
  templateUrl: './run-report-frame.component.html',
  styleUrls: ['./run-report-frame.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class.rrf-layout-single]': "layout === 'single'",
    '[class.rrf-layout-sidebar]': "layout === 'sidebar'"
  }
})
export class RunReportFrameComponent implements OnChanges {
  private readonly cdr = inject(ChangeDetectorRef);

  /** The accessible name of the main section. */
  @Input() mainLabel = 'Report';

  /** The accessible name of the side section. */
  @Input() asideLabel = 'Details';

  /** The accessible name of the sidebar, and the subject of its resizer's name. */
  @Input() sidebarLabel = 'Settings';

  /** The sidebar's width in CSS px, as the host last stored it; the default when null. */
  @Input() sidebarWidth: number | null = null;

  /** Marks the body `aria-busy` while the host loads or refreshes its content. */
  @Input() busy = false;

  /** Two columns, a sidebar and a main column, or one body under a tab row. */
  @Input() layout: 'columns' | 'sidebar' | 'single' = 'columns';

  /** The sidebar's width once a resize ends, for the host to store. */
  @Output() readonly sidebarWidthChange = new EventEmitter<number>();

  @ViewChild('body', { static: true }) private body?: ElementRef<HTMLElement>;

  readonly sidebarId = `rrf-sidebar-${++nextFrameId}`;
  readonly sidebarWidthMin = RRF_SIDEBAR_WIDTH_MIN;
  readonly sidebarWidthDefault = RRF_SIDEBAR_WIDTH_DEFAULT;

  /**
   * The widest the resizer goes: 32rem, or 40 % of the body when that is narrower. Measured when
   * the handle is grabbed or focused, never inside a change-detection pass that renders from it.
   */
  sidebarWidthMax = RRF_SIDEBAR_WIDTH_MAX;

  /** The width while a resize is in progress or since it ended; null follows `sidebarWidth`. */
  private liveSidebarWidth: number | null = null;

  /** The sidebar's width as rendered, in CSS px. */
  get sidebarWidthPx(): number {
    const width = this.liveSidebarWidth ?? this.sidebarWidth ?? RRF_SIDEBAR_WIDTH_DEFAULT;
    return clamp(Math.round(width), RRF_SIDEBAR_WIDTH_MIN, RRF_SIDEBAR_WIDTH_MAX);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['sidebarWidth']) {
      this.liveSidebarWidth = null;
    }
  }

  measureSidebarWidthMax(): void {
    const body = this.body?.nativeElement.clientWidth ?? 0;
    this.sidebarWidthMax = body > 0
      ? clamp(Math.floor(body * RRF_SIDEBAR_MAX_SHARE), RRF_SIDEBAR_WIDTH_MIN, RRF_SIDEBAR_WIDTH_MAX)
      : RRF_SIDEBAR_WIDTH_MAX;
    this.cdr.markForCheck();
  }

  onSidebarWidthChange(width: number): void {
    this.liveSidebarWidth = width;
    this.cdr.markForCheck();
  }

  onSidebarWidthCommit(width: number): void {
    this.liveSidebarWidth = width;
    this.cdr.markForCheck();
    this.sidebarWidthChange.emit(this.sidebarWidthPx);
  }

  /** Scrolls the body back to its top, as when the host switches tabs. */
  scrollBodyToTop(): void {
    if (this.body) {
      this.body.nativeElement.scrollTop = 0;
    }
  }
}
