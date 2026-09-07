import { AfterViewInit, Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PAGE_ELLIPSIS, TableState } from './table-state';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';

/** Distinguishes the ids and tooltip anchors of the several pagers a page may hold. */
let pagerSequence = 0;

/**
 * The page-size selector, page buttons and result summary for one `TableState`.
 *
 * Purely presentational: it owns no data and mutates nothing but the state handed to it.
 * The history table renders one instance above the table and one below, both bound to the
 * same state.
 */
@Component({
  selector: 'app-table-pager',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="gh-pager">
      <div class="gh-pager-size">
        <label [attr.for]="sizeSelectId">Rows per page</label>
        <select class="gh-input gh-pager-size-select" [attr.id]="sizeSelectId"
                [ngModel]="state.pageSize" [ngModelOptions]="{ standalone: true }"
                (ngModelChange)="onPageSize($event)">
          @for (size of state.pageSizes; track size) {
            <option [ngValue]="size">{{ size }}</option>
          }
        </select>
      </div>

      <div class="gh-pager-buttons">
        <button type="button" class="gh-page-btn gh-page-btn-step"
                [attr.aria-disabled]="hasPrevious ? null : 'true'"
                [attr.aria-label]="'First page of ' + noun"
                [attr.interestfor]="idPrefix + '-first-tip'"
                [attr.style]="'anchor-name: --' + idPrefix + '-first'"
                (click)="goFirst()">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
               fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"
               stroke-linejoin="round" aria-hidden="true">
            <polyline points="11 17 6 12 11 7"></polyline>
            <polyline points="18 17 13 12 18 7"></polyline>
          </svg>
        </button>
        <div popover="hint" class="gh-tooltip" [attr.id]="idPrefix + '-first-tip'"
             [attr.style]="'position-anchor: --' + idPrefix + '-first'">First page</div>

        <button type="button" class="gh-page-btn gh-page-btn-step"
                [attr.aria-disabled]="hasPrevious ? null : 'true'"
                [attr.aria-label]="'Previous page of ' + noun"
                [attr.interestfor]="idPrefix + '-prev-tip'"
                [attr.style]="'anchor-name: --' + idPrefix + '-prev'"
                (click)="goPrevious()">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
               fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"
               stroke-linejoin="round" aria-hidden="true">
            <polyline points="15 18 9 12 15 6"></polyline>
          </svg>
        </button>
        <div popover="hint" class="gh-tooltip" [attr.id]="idPrefix + '-prev-tip'"
             [attr.style]="'position-anchor: --' + idPrefix + '-prev'">Previous page</div>

        @for (slot of state.pageNumbers(rows); track $index) {
          @if (slot === ellipsis) {
            <span class="gh-page-ellipsis" aria-hidden="true">{{ ellipsis }}</span>
          } @else {
            <button type="button" class="gh-page-btn"
                    [attr.aria-current]="slot === state.page ? 'page' : null"
                    [attr.aria-label]="'Page ' + slot"
                    (click)="goTo(slot)">{{ slot }}</button>
          }
        }

        <button type="button" class="gh-page-btn gh-page-btn-step"
                [attr.aria-disabled]="hasNext ? null : 'true'"
                [attr.aria-label]="'Next page of ' + noun"
                [attr.interestfor]="idPrefix + '-next-tip'"
                [attr.style]="'anchor-name: --' + idPrefix + '-next'"
                (click)="goNext()">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
               fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"
               stroke-linejoin="round" aria-hidden="true">
            <polyline points="9 18 15 12 9 6"></polyline>
          </svg>
        </button>
        <div popover="hint" class="gh-tooltip" [attr.id]="idPrefix + '-next-tip'"
             [attr.style]="'position-anchor: --' + idPrefix + '-next'">Next page</div>

        <button type="button" class="gh-page-btn gh-page-btn-step"
                [attr.aria-disabled]="hasNext ? null : 'true'"
                [attr.aria-label]="'Last page of ' + noun"
                [attr.interestfor]="idPrefix + '-last-tip'"
                [attr.style]="'anchor-name: --' + idPrefix + '-last'"
                (click)="goLast()">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
               fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"
               stroke-linejoin="round" aria-hidden="true">
            <polyline points="13 17 18 12 13 7"></polyline>
            <polyline points="6 17 11 12 6 7"></polyline>
          </svg>
        </button>
        <div popover="hint" class="gh-tooltip" [attr.id]="idPrefix + '-last-tip'"
             [attr.style]="'position-anchor: --' + idPrefix + '-last'">Last page</div>
      </div>

      <p class="gh-pager-status"
         [attr.role]="announce ? 'status' : null"
         [attr.aria-live]="announce ? 'polite' : null"
         [attr.aria-hidden]="announce ? null : 'true'">{{ summary }}</p>
    </div>
  `
})
export class TablePagerComponent implements OnInit, AfterViewInit {
  /** The state this pager drives. Several pagers may share one. */
  @Input({ required: true }) state!: TableState<any>;

  /** The unpaged, unfiltered source rows the state is applied to. */
  @Input({ required: true }) rows: readonly any[] = [];

  /** Plural noun for the summary line and the button names — "runs", "groups". */
  @Input() noun = 'rows';

  /**
   * Whether the summary is a live region. Two pagers around one table would otherwise
   * announce every page change twice, so the second instance sets this false: the line stays
   * visible and is hidden from assistive technology instead.
   */
  @Input() announce = true;

  /** Fires after any page or page-size change, so the host can re-render or persist state. */
  @Output() changed = new EventEmitter<void>();

  readonly ellipsis = PAGE_ELLIPSIS;
  readonly idPrefix = `gh-pager-${++pagerSequence}`;
  readonly sizeSelectId = `${this.idPrefix}-size`;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngAfterViewInit(): void {
    // The anchor-positioning polyfill does not observe the DOM, so a pager revealed behind an
    // @if is invisible to its first scan.
    setTimeout(() => refreshAnchorPositioning(), 0);
  }

  get hasPrevious(): boolean {
    return this.state.hasPrevious(this.rows);
  }

  get hasNext(): boolean {
    return this.state.hasNext(this.rows);
  }

  get summary(): string {
    const matching = this.state.filteredCount(this.rows);
    if (matching === 0) {
      return this.rows.length === 0 ? `No ${this.noun}` : `No ${this.noun} match these filters`;
    }
    const start = this.state.rangeStart(this.rows);
    const end = this.state.rangeEnd(this.rows);
    const shown = `Showing ${start}–${end} of ${matching} ${this.noun}`;
    return this.state.hasActiveFilters ? `${shown}, filtered from ${this.rows.length}` : shown;
  }

  onPageSize(size: number): void {
    this.state.setPageSize(size);
    this.changed.emit();
  }

  goTo(page: number | '…'): void {
    if (typeof page !== 'number') {
      return;
    }
    this.state.setPage(page, this.rows);
    this.changed.emit();
  }

  // The end buttons are aria-disabled rather than disabled, so a keyboard user can still land
  // on them and hear why they do nothing. That leaves them clickable, so each handler refuses
  // on its own rather than relying on the attribute.
  goFirst(): void {
    if (!this.hasPrevious) {
      return;
    }
    this.state.setPage(1, this.rows);
    this.changed.emit();
  }

  goPrevious(): void {
    if (!this.hasPrevious) {
      return;
    }
    this.state.setPage(this.state.page - 1, this.rows);
    this.changed.emit();
  }

  goNext(): void {
    if (!this.hasNext) {
      return;
    }
    this.state.setPage(this.state.page + 1, this.rows);
    this.changed.emit();
  }

  goLast(): void {
    if (!this.hasNext) {
      return;
    }
    this.state.setPage(this.state.totalPages(this.rows), this.rows);
    this.changed.emit();
  }
}
