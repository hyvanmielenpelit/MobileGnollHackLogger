import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { Subscription, forkJoin } from 'rxjs';
import { map } from 'rxjs/operators';
import {
  AccountDeletionDecisionResult,
  AccountDeletionRequest,
  AccountDeletionRequestStatus,
  AccountDeletionRequestsQuery,
  AdminService
} from '../../../services/admin.service';
import { CardListChip, CardListSort, CardListState } from '../../../shared/data-table/card-list-state';
import { FilterFacetComponent, FilterFacetOption } from '../../../shared/data-table/filter-facet.component';
import { TableState, customFilter } from '../../../shared/data-table/table-state';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { AdminReasonDialogComponent } from '../../admin-dialogs/admin-reason-dialog.component';
import { AdminPageStore } from '../../admin-page.store';

/** Where the list's view settings are kept, per browser. */
export const DELETION_REQUESTS_VIEW_STORAGE_KEY = 'overseer.admin.users.deletions.view';

/** The requests the list fetches at first, and how many more Show more fetches. */
export const DELETION_REQUESTS_BATCH = 20;

/** The longest decline note the server accepts. */
export const DELETION_DECLINE_NOTE_MAX = 512;

/** The Status facet's values, in the order it lists them. */
export const DELETION_REQUEST_STATUSES: readonly AccountDeletionRequestStatus[] = ['Pending', 'Completed', 'Declined'];

/** What the Approve button says while the erasure paths are missing and the server sent no message. */
export const ERASURE_PATHS_FALLBACK_MESSAGE = 'The account erasure paths are not configured.';

/** The list has one order: the server's. */
const DELETION_REQUEST_SORTS: readonly CardListSort[] = [
  { id: 'server', label: 'Server order', column: 'requested', direction: 'asc' }
];

const NO_STATUSES: readonly string[] = [];

type StatusCounts = Readonly<Record<AccountDeletionRequestStatus, number>>;

/**
 * The Users tab's Deletion requests: a server-paged card list of held account deletions with a
 * search by user name and a Status facet (Pending by default), each pending request with Approve
 * and delete, which needs the user name typed, and Decline, which needs a note.
 */
@Component({
  selector: 'app-deletion-requests',
  standalone: true,
  imports: [FilterFacetComponent, AdminReasonDialogComponent],
  templateUrl: './deletion-requests.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './deletion-requests.component.scss'
})
export class DeletionRequestsComponent implements OnInit, OnDestroy {
  private readonly adminService = inject(AdminService);
  private readonly store = inject(AdminPageStore);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  @ViewChild('approveDialog') approveDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild(AdminReasonDialogComponent, { static: true }) reasonDialog!: AdminReasonDialogComponent;

  /** The pending requests after each fetch, for the Users tab's sub-tab label. */
  @Output() readonly pendingCountChange = new EventEmitter<number>();

  readonly statuses = DELETION_REQUEST_STATUSES;

  /** The requests fetched so far, in the server's order. */
  rows: AccountDeletionRequest[] = [];
  totalCount = 0;
  pendingCount = 0;
  erasurePathsConfigured = true;
  erasurePathsMessage: string | null = null;
  loading = false;
  error: string | null = null;
  /** Whether the first page of the current query has arrived. */
  loaded = false;

  /** Each status's requests matching the search; null until counted. */
  statusCounts: StatusCounts | null = null;

  approveTarget: AccountDeletionRequest | null = null;
  approveName = '';
  approveError: string | null = null;
  approving = false;
  /** The request whose Decline is in flight. */
  decliningId: number | null = null;

  /** The server filters and pages the requests; the table only carries the filter values. */
  readonly table = new TableState<AccountDeletionRequest>('requested', 'asc').registerAccessors(
    { requested: r => parseServerUtcDate(r.requestedUtc).getTime() },
    {
      search: customFilter<AccountDeletionRequest>(() => true),
      status: customFilter<AccountDeletionRequest>((r, value) => r.status === value)
    }
  );

  /** The search, the Status selection and the chips; every change refetches the first page. */
  readonly list = new CardListState<AccountDeletionRequest>(this.table, {
    idPrefix: 'del',
    batch: DELETION_REQUESTS_BATCH,
    sorts: DELETION_REQUEST_SORTS,
    defaultSort: 'server',
    storageKey: DELETION_REQUESTS_VIEW_STORAGE_KEY,
    facets: [],
    singleFacets: [{
      column: 'status',
      label: 'Status',
      anyLabel: 'Any status',
      options: DELETION_REQUEST_STATUSES.map(status => ({ value: status, label: status })),
      matches: (r, value) => r.status === value,
      listedWhen: () => true
    }],
    onChange: () => this.reload()
  });

  private listSub: Subscription | null = null;
  private countsSub: Subscription | null = null;
  /** The search the status counts were taken for; null forces a recount. */
  private countedSearch: string | null = null;
  private returnFocus: HTMLElement | null = null;
  /** After a decision the reload moves focus to the list heading. */
  private focusHeadingAfterLoad = false;

  private statusOptionsMemo: { counts: StatusCounts | null; options: FilterFacetOption[] } | null = null;
  private selectedStatusesMemo: { status: string; selected: readonly string[] } | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.table.setRemoteTotal(0);
    this.list.setFacet('status', ['Pending']);
    this.reload();
  }

  ngOnDestroy(): void {
    this.list.dispose();
    this.listSub?.unsubscribe();
    this.countsSub?.unsubscribe();
    const dialog = this.approveDialog?.nativeElement;
    if (dialog?.open) dialog.close();
  }

  // --- Filters ---

  /** The selected status, or undefined for every status. */
  get selectedStatus(): AccountDeletionRequestStatus | undefined {
    const value = (this.table.filters['status'] ?? '').trim();
    return DELETION_REQUEST_STATUSES.find(status => status === value);
  }

  /** The Status selection as the facet takes it; the same array until it changes. */
  get selectedStatuses(): readonly string[] {
    const status = this.selectedStatus ?? '';
    const memo = this.selectedStatusesMemo;
    if (memo && memo.status === status) {
      return memo.selected;
    }
    const selected = status ? [status] : NO_STATUSES;
    this.selectedStatusesMemo = { status, selected };
    return selected;
  }

  /** The Status facet's options with each status's count under the search; 0 until counted. */
  get statusOptions(): FilterFacetOption[] {
    const memo = this.statusOptionsMemo;
    if (memo && memo.counts === this.statusCounts) {
      return memo.options;
    }
    const counts = this.statusCounts;
    const options = DELETION_REQUEST_STATUSES.map(status => ({ value: status, label: status, count: counts?.[status] ?? 0 }));
    this.statusOptionsMemo = { counts, options };
    return options;
  }

  get chips(): CardListChip[] {
    return this.list.chips(this.rows);
  }

  get searchTerm(): string {
    return (this.table.filters['search'] ?? '').trim();
  }

  get remainingCount(): number {
    return Math.max(0, this.totalCount - this.rows.length);
  }

  get nextBatchCount(): number {
    return Math.min(this.list.batch, this.remainingCount);
  }

  /** `Showing 20 of 31 requests`, `One request`, or empty with no requests. */
  get listStatus(): string {
    if (this.loading && !this.loaded) return 'Loading deletion requests…';
    if (this.error || this.totalCount === 0) return '';
    return this.totalCount === 1 && this.rows.length === 1
      ? 'One request'
      : `Showing ${this.rows.length} of ${this.totalCount} requests`;
  }

  /** `No pending deletion requests.`, or `No deletion requests.` with every status. */
  get emptyText(): string {
    const status = this.selectedStatus;
    return status ? `No ${status.toLowerCase()} deletion requests.` : 'No deletion requests.';
  }

  /** Why Approve is unavailable while the erasure paths are missing. */
  get erasureBlockedText(): string {
    return this.erasurePathsMessage?.trim() || ERASURE_PATHS_FALLBACK_MESSAGE;
  }

  onSearchInput(event: Event): void {
    this.list.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search at once and refetches. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (this.list.clearSearchOnEscape(event)) this.reload();
  }

  onStatusChange(values: string[]): void {
    this.list.setFacet('status', values);
    this.reload();
  }

  /** Removes a chip's filter and refetches, then focuses the chip now in its place, else the previous one, else the search. */
  removeChip(chip: CardListChip): void {
    const index = this.list.removeChip(chip, this.rows);
    this.reload();
    this.cdr.detectChanges();
    const chips = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.del-chips .gh-filter-chip'));
    const target = index >= 0 ? chips[index] ?? chips[index - 1] : undefined;
    (target ?? this.element('#del-search'))?.focus();
  }

  /** Clears the search and the Status filter, refetches, then focuses the search. */
  clearFilters(): void {
    this.list.clearFilters();
    const input = this.element('#del-search') as HTMLInputElement | null;
    if (input) input.value = '';
    this.reload();
    input?.focus();
  }

  // --- Fetching ---

  /** Fetches the first page of the current query, replacing the list, and recounts when the search changed. */
  reload(): void {
    this.fetch(false);
    if (this.countedSearch !== this.searchTerm) {
      this.loadCounts();
    }
  }

  /** Appends the next page and focuses the title of the first new card. */
  showMore(): void {
    if (this.loading || this.remainingCount === 0) return;
    this.fetch(true);
  }

  private fetch(append: boolean): void {
    const query: AccountDeletionRequestsQuery = {
      status: this.selectedStatus,
      search: this.searchTerm || undefined,
      skip: append ? this.rows.length : 0,
      take: this.list.batch
    };
    if (!append) {
      this.loaded = false;
    }
    this.loading = true;
    this.error = null;
    this.listSub?.unsubscribe();
    this.listSub = this.adminService.getAccountDeletionRequests(query).subscribe({
      next: page => {
        const firstNew = append ? this.rows.length : -1;
        if (append) {
          const known = new Set(this.rows.map(r => r.id));
          this.rows = [...this.rows, ...page.rows.filter(r => !known.has(r.id))];
        } else {
          this.rows = page.rows;
        }
        this.totalCount = page.totalCount;
        this.pendingCount = page.pendingCount;
        this.erasurePathsConfigured = page.erasurePathsConfigured;
        this.erasurePathsMessage = page.erasurePathsMessage ?? null;
        this.table.setRemoteTotal(page.totalCount);
        this.loading = false;
        this.loaded = true;
        this.list.invalidate();
        this.pendingCountChange.emit(page.pendingCount);

        const focusHeading = this.focusHeadingAfterLoad;
        this.focusHeadingAfterLoad = false;
        const focusId = firstNew >= 0 ? this.rows[firstNew]?.id ?? null : null;
        if (focusId === null && !focusHeading) {
          this.cdr.markForCheck();
          return;
        }
        this.cdr.detectChanges();
        refreshAnchorPositioning();
        (focusId !== null ? this.element(`#del-${focusId}-title`) : this.element('#del-title'))?.focus();
      },
      error: err => {
        this.loading = false;
        this.loaded = true;
        this.focusHeadingAfterLoad = false;
        this.error = 'The deletion requests could not be loaded: ' + DeletionRequestsComponent.errorText(err);
        this.cdr.markForCheck();
      }
    });
  }

  /** Counts each status's requests under the current search; a failed count keeps the counts there are. */
  private loadCounts(): void {
    const search = this.searchTerm;
    this.countedSearch = search;
    this.countsSub?.unsubscribe();
    this.countsSub = forkJoin(DELETION_REQUEST_STATUSES.map(status =>
      this.adminService.getAccountDeletionRequests({ status, search: search || undefined, take: 1 }).pipe(map(page => page.totalCount))
    )).subscribe({
      next: ([pending, completed, declined]) => {
        this.statusCounts = { Pending: pending, Completed: completed, Declined: declined };
        this.cdr.markForCheck();
      },
      error: () => {
        this.countedSearch = null;
      }
    });
  }

  /** Refetches page 1 and the counts after a decision, then focuses the list heading. */
  private reloadAfterDecision(): void {
    this.focusHeadingAfterLoad = true;
    this.countedSearch = null;
    this.reload();
  }

  // --- A card ---

  /** The user name, or *Deleted account* once the account is gone. */
  userLabel(request: AccountDeletionRequest): string {
    return request.userName?.trim() || 'Deleted account';
  }

  /** `27 days`, `1 day`. */
  ageText(request: AccountDeletionRequest): string {
    return `${request.ageDays} ${request.ageDays === 1 ? 'day' : 'days'}`;
  }

  /** `2026-10-13` (UTC). */
  dateText(utc: string | null | undefined): string {
    if (!utc) return '';
    const date = parseServerUtcDate(utc);
    if (isNaN(date.getTime())) return utc;
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
  }

  /** Whether the reasons evaluated now differ from those stored with the request. */
  showCurrentReasons(request: AccountDeletionRequest): boolean {
    const current = request.currentReasons;
    return current != null && current.trim() !== request.holdReasons.trim();
  }

  /** `Completed by alice on 2026-10-01`; empty while pending. */
  decisionText(request: AccountDeletionRequest): string {
    if (request.status === 'Pending') return '';
    const decider = request.decidedByUserName?.trim() ?? '';
    const by = decider ? ` by ${decider}` : '';
    const on = request.decidedUtc ? ` on ${this.dateText(request.decidedUtc)}` : '';
    return `${request.status}${by}${on}`;
  }

  /** Shows the user's security events on the Security tab. */
  showSecurityEvents(request: AccountDeletionRequest): void {
    if (!request.userId) return;
    this.store.requestSecurityUser(request.userId, this.userLabel(request));
  }

  // --- Approve ---

  get approveNameMatches(): boolean {
    const target = this.approveTarget;
    return !!target?.userName && this.approveName.trim() === target.userName;
  }

  /** Opens the confirmation that needs the user name typed; does nothing while the erasure paths are missing. */
  requestApprove(request: AccountDeletionRequest, button: HTMLElement): void {
    if (!this.erasurePathsConfigured || request.status !== 'Pending' || !request.userName || this.approving) return;
    this.approveTarget = request;
    this.approveName = '';
    this.approveError = null;
    this.returnFocus = button;
    this.cdr.detectChanges();
    const input = this.element('#del-approve-name') as HTMLInputElement | null;
    if (input) input.value = '';
    const dialog = this.approveDialog?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
    input?.focus();
  }

  onApproveNameInput(event: Event): void {
    this.approveName = (event.target as HTMLInputElement).value;
    this.approveError = null;
  }

  cancelApprove(): void {
    this.approveDialog?.nativeElement.close();
  }

  /** The dialog's own close, by Cancel or Escape: focus goes back to the Approve pressed. After an approval the reload moves it instead. */
  onApproveDialogClose(): void {
    this.approveTarget = null;
    this.approveName = '';
    this.approveError = null;
    const back = this.returnFocus;
    this.returnFocus = null;
    if (back?.isConnected) back.focus();
  }

  /** Does nothing until the typed name matches (the button is aria-disabled). */
  confirmApprove(): void {
    const target = this.approveTarget;
    if (!target || !this.approveNameMatches || this.approving) return;
    const name = this.userLabel(target);
    const typed = this.approveName.trim();
    this.approving = true;
    this.approveError = null;
    this.adminService.approveAccountDeletion(target.id, typed).subscribe({
      next: result => {
        this.approving = false;
        this.returnFocus = null;
        this.approveDialog?.nativeElement.close();
        const text = [`The account ${name} was deleted.`, ...DeletionRequestsComponent.missingFileSentences(result), DeletionRequestsComponent.emailSentence(result)];
        this.store.showToast(text.join(' '), result.emailSent ? 'success' : 'info', 'Account Deleted');
        this.reloadAfterDecision();
      },
      error: err => {
        this.approving = false;
        this.approveError = DeletionRequestsComponent.errorText(err);
        this.cdr.markForCheck();
      }
    });
  }

  // --- Decline ---

  openDecline(request: AccountDeletionRequest): void {
    if (request.status !== 'Pending' || this.decliningId !== null) return;
    const name = this.userLabel(request);
    this.reasonDialog.open({
      title: 'Decline the deletion request',
      message: `The account ${name} stays, and the user is emailed that the request could not be completed. The note is for administrators only and is never shown to the user.`,
      label: 'Note',
      maxLength: DELETION_DECLINE_NOTE_MAX,
      confirmText: 'Decline',
      confirmClass: 'btn-gh'
    }, note => this.decline(request, note));
  }

  private decline(request: AccountDeletionRequest, note: string): void {
    const name = this.userLabel(request);
    this.decliningId = request.id;
    this.adminService.declineAccountDeletion(request.id, note).subscribe({
      next: result => {
        this.decliningId = null;
        const text = [`The deletion request of ${name} was declined; the account stays.`, DeletionRequestsComponent.emailSentence(result)];
        this.store.showToast(text.join(' '), result.emailSent ? 'success' : 'info', 'Request Declined');
        this.reloadAfterDecision();
      },
      error: err => {
        this.decliningId = null;
        this.store.showToast(DeletionRequestsComponent.errorText(err), 'error', 'Error');
        this.cdr.markForCheck();
      }
    });
  }

  // --- Helpers ---

  private element(selector: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(selector);
  }

  /** `2 bones files were not found on disk.`, one sentence per non-zero count. */
  private static missingFileSentences(result: AccountDeletionDecisionResult): string[] {
    const sentences: string[] = [];
    if (result.bonesFilesMissing > 0) {
      sentences.push(result.bonesFilesMissing === 1
        ? '1 bones file was not found on disk.'
        : `${result.bonesFilesMissing} bones files were not found on disk.`);
    }
    if (result.foldersMissing > 0) {
      sentences.push(result.foldersMissing === 1
        ? '1 folder was not found on disk.'
        : `${result.foldersMissing} folders were not found on disk.`);
    }
    return sentences;
  }

  private static emailSentence(result: AccountDeletionDecisionResult): string {
    return result.emailSent
      ? 'The user was emailed the decision.'
      : `The decision email was not sent: ${result.emailError?.trim() || 'Unknown error'}`;
  }

  private static errorText(err: unknown): string {
    const http = err as { error?: { message?: string } | string; message?: string };
    if (http?.error && typeof http.error === 'object' && http.error.message) return http.error.message;
    if (typeof http?.error === 'string' && http.error) return http.error;
    return http?.message || 'Unknown error';
  }
}
