import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnDestroy,
  OnInit,
  Output,
  inject
} from '@angular/core';
import { Observable, Subject, Subscription, catchError, map, of, switchMap } from 'rxjs';

import {
  AdminBulkItem,
  AdminBulkOperation,
  AdminBulkOperationStatus,
  AdminUsersService,
  adminApiError
} from '../../../services/admin-users.service';
import { CardListChip, CardListState } from '../../../shared/data-table/card-list-state';
import { FilterFacetComponent } from '../../../shared/data-table/filter-facet.component';
import { TableState, anyOfFilter, customFilter } from '../../../shared/data-table/table-state';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { formatUtcDateTime } from '../user-status/admin-user-status';
import { bulkActionInfo } from './bulk-action-step.component';

/** How often a running operation is polled. */
export const BULK_POLL_MS = 1500;
/** The longest wait between polls after repeated errors. */
export const BULK_POLL_MAX_MS = 30_000;
/** The status line changes at most this often while the operation runs. */
export const BULK_STATUS_MIN_MS = 2000;
/** The page size the finished operation's items are read in (the server's maximum). */
export const BULK_ITEMS_PAGE = 200;
/** The results list's batch. */
export const BULK_RESULTS_BATCH = 50;

/** Whether the runner may still change accounts of an operation in this status. */
export function bulkOperationRunning(status: AdminBulkOperationStatus): boolean {
  return status === 'Queued' || status === 'Running' || status === 'CancelRequested';
}

/** A result card's outcome: the item's, or `Excluded` for an account left out in the review. */
export type BulkResultOutcome = 'Changed' | 'Skipped' | 'Failed' | 'Excluded' | 'Pending';

const OUTCOME_ORDER: readonly BulkResultOutcome[] = ['Changed', 'Skipped', 'Failed', 'Excluded', 'Pending'];

const OUTCOME_LABELS: Readonly<Record<BulkResultOutcome, string>> = {
  Changed: 'Changed',
  Skipped: 'Skipped',
  Failed: 'Failed',
  Excluded: 'Excluded',
  Pending: 'Not processed'
};

const OUTCOME_TONES: Readonly<Record<BulkResultOutcome, 'ok' | 'info' | 'warning' | 'danger'>> = {
  Changed: 'ok',
  Skipped: 'info',
  Failed: 'danger',
  Excluded: 'info',
  Pending: 'warning'
};

export function bulkResultOutcome(item: AdminBulkItem): BulkResultOutcome {
  return item.excluded ? 'Excluded' : item.outcome;
}

/** Saves a file through a temporary object URL, revoked once the download has started. */
export function saveBulkReport(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  try {
    anchor.click();
  } finally {
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 0);
  }
}

type PollResult = { op: AdminBulkOperation; error: null } | { op: null; error: unknown };

/**
 * Step 4 of the Bulk Actions wizard. While the operation runs: a progress line whose status changes at
 * most every 2 s, polling `GET bulk/{id}` every 1.5 s (backing off on error), and *Stop after the
 * current account*. Once it is done: the summary, a search and an outcome facet over a card list of
 * every account with its before → after and message, *Download report (CSV)* and *Show in admin log*.
 * Polling stops when the operation finishes and when the step is destroyed.
 */
@Component({
  selector: 'app-bulk-results-step',
  standalone: true,
  imports: [FilterFacetComponent],
  templateUrl: './bulk-results-step.component.html',
  styleUrl: './bulk-results-step.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager   // matches every other component here
})
export class BulkResultsStepComponent implements OnInit, OnDestroy {
  private readonly usersService = inject(AdminUsersService);
  private readonly cdr = inject(ChangeDetectorRef);

  @Input({ required: true }) operationId!: number;
  /** The operation as the execute request returned it; without it the step reads it first. */
  @Input() initial: AdminBulkOperation | null = null;

  /** Every newer state of the operation, polled or after Stop. */
  @Output() readonly operationChange = new EventEmitter<AdminBulkOperation>();
  /** *Show in admin log*. */
  @Output() readonly showInLog = new EventEmitter<void>();

  operation: AdminBulkOperation | null = null;
  /** Why the operation could not be read; polling goes on, slower. */
  pollError: string | null = null;
  /** The status line: throttled while running, at once when the operation finishes. */
  statusText = '';

  /** Every item of the finished operation. */
  items: AdminBulkItem[] = [];
  itemsLoading = false;
  itemsError: string | null = null;

  cancelBusy = false;
  cancelError: string | null = null;
  downloadBusy = false;
  downloadError: string | null = null;

  /** Saves the report; replaced in specs. */
  saveFile: (blob: Blob, fileName: string) => void = saveBulkReport;

  readonly table = new TableState<AdminBulkItem>('id', 'asc').registerAccessors({}, {
    search: customFilter<AdminBulkItem>((item, value) => {
      const term = value.toLowerCase();
      return item.userName.toLowerCase().includes(term) || (item.message ?? '').toLowerCase().includes(term);
    }),
    outcome: anyOfFilter<AdminBulkItem>(item => bulkResultOutcome(item))
  });

  readonly list = new CardListState<AdminBulkItem>(this.table, {
    idPrefix: 'bw-results',
    batch: BULK_RESULTS_BATCH,
    sorts: [],
    defaultSort: '',
    storageKey: 'overseer.admin.users.bulkResults.view',
    facets: [{
      column: 'outcome',
      label: 'Outcome',
      values: item => bulkResultOutcome(item),
      order: OUTCOME_ORDER,
      labelOf: value => OUTCOME_LABELS[value as BulkResultOutcome] ?? value
    }],
    onChange: () => this.cdr.markForCheck()
  });

  private readonly poll$ = new Subject<void>();
  private pollSub: Subscription | null = null;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private failures = 0;
  private statusTimer: ReturnType<typeof setTimeout> | null = null;
  private pendingStatus: string | null = null;
  private itemsSub: Subscription | null = null;
  private cancelSub: Subscription | null = null;
  private downloadSub: Subscription | null = null;
  private destroyed = false;

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.pollSub = this.poll$.pipe(
      switchMap((): Observable<PollResult> => this.usersService.bulkGet(this.operationId, { take: 1 }).pipe(
        map(op => ({ op, error: null }) as PollResult),
        catchError(error => of({ op: null, error } as PollResult))
      ))
    ).subscribe(result => this.onPolled(result));

    if (this.initial) {
      this.apply(this.initial);
    } else {
      this.schedulePoll(0);
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.stopPolling();
    this.pollSub?.unsubscribe();
    this.itemsSub?.unsubscribe();
    this.cancelSub?.unsubscribe();
    this.downloadSub?.unsubscribe();
    if (this.statusTimer !== null) clearTimeout(this.statusTimer);
    this.list.dispose();
  }

  // --- Reading ---

  get running(): boolean {
    return !!this.operation && bulkOperationRunning(this.operation.status);
  }

  get actionTitle(): string {
    return this.operation ? bulkActionInfo(this.operation.action).title : '';
  }

  get processed(): number {
    const op = this.operation;
    return op ? op.changedCount + op.skippedCount + op.failedCount : 0;
  }

  get total(): number {
    return this.processed + (this.operation?.pendingCount ?? 0);
  }

  /** The progress bar's fill, 0–100. */
  get percent(): number {
    const total = this.total;
    return total > 0 ? Math.round((this.processed / total) * 100) : 0;
  }

  get stopping(): boolean {
    return this.operation?.status === 'CancelRequested';
  }

  /** Why Stop does nothing now, or null. */
  get stopBlockedReason(): string | null {
    if (this.cancelBusy) return 'Asking the operation to stop…';
    if (this.stopping) return 'Stopping after the current account…';
    return null;
  }

  /** What became of the run, beyond the counts: stopped on request, interrupted, never run. */
  get endNote(): string | null {
    const op = this.operation;
    if (!op || this.running) return null;
    switch (op.status) {
      case 'Canceled':
        return op.startedUtc
          ? 'Stopped on request. The accounts not reached were left as they were.'
          : 'This preview was discarded; no account was changed.';
      case 'Interrupted':
        return 'Interrupted when Overseer restarted. The accounts already processed stay as they are; the rest were not changed.';
      case 'Previewed':
      case 'Expired':
        return 'This preview was never run; no account was changed.';
      default:
        return null;
    }
  }

  get facets() {
    return this.list.facets(this.items);
  }

  get chips(): CardListChip[] {
    return this.list.chips(this.items);
  }

  get itemsView(): AdminBulkItem[] {
    return this.list.view(this.items);
  }

  get remainingCount(): number {
    return this.list.remainingCount(this.items);
  }

  get nextBatchCount(): number {
    return this.list.nextBatchCount(this.items);
  }

  get listStatus(): string {
    return this.list.statusText(this.items, { one: 'account', many: 'accounts' });
  }

  outcomeOf(item: AdminBulkItem): BulkResultOutcome {
    return bulkResultOutcome(item);
  }

  outcomeLabel(item: AdminBulkItem): string {
    return OUTCOME_LABELS[bulkResultOutcome(item)];
  }

  outcomeTone(item: AdminBulkItem): string {
    return OUTCOME_TONES[bulkResultOutcome(item)];
  }

  changeText(item: AdminBulkItem): string | null {
    const before = item.beforeStatus?.trim();
    const after = item.afterStatus?.trim();
    if (!before && !after) return null;
    return `${before || 'Unknown'} → ${after || 'Unknown'}`;
  }

  processedText(item: AdminBulkItem): string {
    return formatUtcDateTime(item.processedUtc);
  }

  // --- Actions ---

  /** Stop after the current account. Does nothing while it is already stopping (aria-disabled). */
  stop(): void {
    const op = this.operation;
    if (!op || !this.running || this.stopBlockedReason) return;
    this.cancelBusy = true;
    this.cancelError = null;
    this.cancelSub?.unsubscribe();
    this.cancelSub = this.usersService.bulkCancel(op.id).subscribe({
      next: updated => {
        this.cancelBusy = false;
        this.apply(updated);
        this.operationChange.emit(updated);
        this.cdr.markForCheck();
      },
      error: err => {
        this.cancelBusy = false;
        const e = adminApiError(err);
        if (e.code === 'finished') {
          this.schedulePoll(0);
        } else {
          this.cancelError = `The operation could not be stopped: ${e.message}`;
        }
        this.cdr.markForCheck();
      }
    });
  }

  download(): void {
    const op = this.operation;
    if (!op || this.downloadBusy) return;
    this.downloadBusy = true;
    this.downloadError = null;
    this.downloadSub?.unsubscribe();
    this.downloadSub = this.usersService.bulkReport(op.id).subscribe({
      next: blob => {
        this.downloadBusy = false;
        this.saveFile(blob, `bulk-operation-${op.id}.csv`);
        this.cdr.markForCheck();
      },
      error: err => {
        this.downloadBusy = false;
        this.downloadError = `The report could not be downloaded: ${adminApiError(err).message}`;
        this.cdr.markForCheck();
      }
    });
  }

  onSearchInput(event: Event): void {
    this.list.setSearchInput((event.target as HTMLInputElement).value);
  }

  onSearchKeydown(event: KeyboardEvent): void {
    this.list.clearSearchOnEscape(event);
  }

  onFacetChange(column: string, values: string[]): void {
    this.list.setFacet(column, values);
  }

  removeChip(chip: CardListChip): void {
    this.list.removeChip(chip, this.items);
    if (chip.column === this.list.searchColumn) {
      const input = document.getElementById('bw-results-search') as HTMLInputElement | null;
      if (input) input.value = '';
    }
    this.cdr.detectChanges();
    document.getElementById('bw-results-search')?.focus();
  }

  /** Shows the next batch and focuses the first new card's title. */
  showMore(): void {
    const index = this.list.showMore(this.items);
    this.cdr.detectChanges();
    const item = this.list.matching(this.items)[index];
    if (item) document.getElementById(`bw-result-${item.id}-title`)?.focus();
  }

  // --- Polling ---

  private onPolled(result: PollResult): void {
    if (this.destroyed) return;
    if (result.op === null) {
      this.failures++;
      this.pollError = `The operation could not be read: ${adminApiError(result.error).message}. Trying again.`;
      this.schedulePoll(Math.min(BULK_POLL_MS * 2 ** this.failures, BULK_POLL_MAX_MS));
      this.cdr.markForCheck();
      return;
    }
    this.failures = 0;
    this.pollError = null;
    this.apply(result.op);
    this.operationChange.emit(result.op);
    this.cdr.markForCheck();
  }

  /** Takes a newer state: polls again while it runs, else stops and reads every item. */
  private apply(op: AdminBulkOperation): void {
    const wasRunning = this.running;
    this.operation = op;
    if (bulkOperationRunning(op.status)) {
      this.setStatus(this.progressText(op), false);
      this.schedulePoll(BULK_POLL_MS);
      return;
    }
    this.stopPolling();
    this.setStatus(this.doneText(op), true);
    if (wasRunning || (this.items.length === 0 && !this.itemsLoading)) {
      this.loadItems();
    }
  }

  private schedulePoll(delay: number): void {
    if (this.destroyed) return;
    if (this.pollTimer !== null) clearTimeout(this.pollTimer);
    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      this.poll$.next();
    }, delay);
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }

  /** `Deleting 12 of 37…`. */
  private progressText(op: AdminBulkOperation): string {
    const info = bulkActionInfo(op.action);
    const processed = op.changedCount + op.skippedCount + op.failedCount;
    const total = processed + op.pendingCount;
    const progress = `${info.progressive} ${processed.toLocaleString('en-US')} of ${total.toLocaleString('en-US')}…`;
    return op.status === 'CancelRequested' ? `Stopping after the current account. ${progress}` : progress;
  }

  /** `Deleted 35 · Skipped 2 · Failed 0`. */
  private doneText(op: AdminBulkOperation): string {
    const info = bulkActionInfo(op.action);
    const parts = [
      `${info.past} ${op.changedCount.toLocaleString('en-US')}`,
      `Skipped ${op.skippedCount.toLocaleString('en-US')}`,
      `Failed ${op.failedCount.toLocaleString('en-US')}`
    ];
    if (op.excludedCount > 0) parts.push(`Excluded ${op.excludedCount.toLocaleString('en-US')}`);
    return parts.join(' · ');
  }

  /**
   * The status line changes at once, then at most once per `BULK_STATUS_MIN_MS`; a change inside
   * the window is shown when it ends. `now` shows it at once, for the end of the run.
   */
  private setStatus(text: string, now: boolean): void {
    if (now) {
      if (this.statusTimer !== null) {
        clearTimeout(this.statusTimer);
        this.statusTimer = null;
      }
      this.pendingStatus = null;
      this.statusText = text;
      return;
    }
    if (this.statusTimer !== null) {
      this.pendingStatus = text;
      return;
    }
    this.statusText = text;
    this.pendingStatus = null;
    this.statusTimer = setTimeout(() => {
      this.statusTimer = null;
      const pending = this.pendingStatus;
      if (pending !== null && pending !== this.statusText) {
        this.setStatus(pending, false);
        this.cdr.markForCheck();
      }
    }, BULK_STATUS_MIN_MS);
  }

  /** Reads every item of the finished operation, a page at a time. */
  private loadItems(): void {
    this.itemsLoading = true;
    this.itemsError = null;
    this.itemsSub?.unsubscribe();
    const read = (skip: number, acc: AdminBulkItem[]): void => {
      this.itemsSub = this.usersService.bulkGet(this.operationId, { skip, take: BULK_ITEMS_PAGE }).subscribe({
        next: op => {
          const all = [...acc, ...op.items];
          if (op.items.length > 0 && all.length < op.itemsTotalCount) {
            read(all.length, all);
            return;
          }
          this.items = all;
          this.itemsLoading = false;
          this.list.invalidate();
          this.cdr.markForCheck();
        },
        error: err => {
          this.itemsLoading = false;
          this.itemsError = `The accounts of this operation could not be loaded: ${adminApiError(err).message}`;
          this.cdr.markForCheck();
        }
      });
    };
    read(0, []);
  }
}
