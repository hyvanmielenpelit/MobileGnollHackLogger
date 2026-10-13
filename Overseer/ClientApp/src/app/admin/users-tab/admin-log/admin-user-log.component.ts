import { ChangeDetectionStrategy, ChangeDetectorRef, Component, ElementRef, OnDestroy, OnInit, inject } from '@angular/core';
import { Subscription, forkJoin } from 'rxjs';
import { map } from 'rxjs/operators';

import {
  ADMIN_USER_ACTION_KINDS,
  AdminUserActionKind,
  AdminUserLogEntry,
  AdminUserLogQuery,
  AdminUsersService,
  adminApiError
} from '../../../services/admin-users.service';
import { CardListChip, CardListSort, CardListState } from '../../../shared/data-table/card-list-state';
import { FilterFacetComponent, FilterFacetOption } from '../../../shared/data-table/filter-facet.component';
import { TableState, anyOfFilter, customFilter } from '../../../shared/data-table/table-state';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { AdminPageStore, AdminUsersLogFilter } from '../../admin-page.store';
import { formatUtcDateTime } from '../user-status/admin-user-status';

/** Where the log's view settings are kept, per browser. */
export const ADMIN_LOG_VIEW_STORAGE_KEY = 'overseer.admin.users.log.view';

/** The entries the log fetches at first, and how many more Show more fetches. */
export const ADMIN_LOG_BATCH = 20;

/** The tone of an action's tag: the word carries the meaning, the outline reinforces it. */
export type AdminLogKindTone = 'ok' | 'info' | 'warning' | 'danger';

const KIND_LABELS: Readonly<Record<AdminUserActionKind, string>> = {
  LockedOut: 'Locked out',
  Unlocked: 'Unlocked',
  Banned: 'Banned',
  Unbanned: 'Unbanned',
  GameRestrictionsChanged: 'Game restrictions changed',
  SignedOutEverywhere: 'Signed out everywhere',
  EmailConfirmed: 'Email confirmed',
  TwoFactorReset: 'Two-factor reset',
  Deleted: 'Deleted',
  AiChatBlocked: 'Blocked from AI chat',
  AiChatUnblocked: 'Unblocked from AI chat',
  HoldPlaced: 'Deletion hold placed',
  HoldReleased: 'Deletion hold released',
  DeletionRequestApproved: 'Deletion request approved',
  DeletionRequestDeclined: 'Deletion request declined'
};

const KIND_TONES: Readonly<Record<AdminUserActionKind, AdminLogKindTone>> = {
  LockedOut: 'danger',
  Unlocked: 'ok',
  Banned: 'danger',
  Unbanned: 'ok',
  GameRestrictionsChanged: 'warning',
  SignedOutEverywhere: 'info',
  EmailConfirmed: 'ok',
  TwoFactorReset: 'warning',
  Deleted: 'danger',
  AiChatBlocked: 'warning',
  AiChatUnblocked: 'ok',
  HoldPlaced: 'warning',
  HoldReleased: 'ok',
  DeletionRequestApproved: 'danger',
  DeletionRequestDeclined: 'info'
};

/** The word of an admin log kind: `Locked out`, `Deletion hold placed`; an unknown kind reads as its name. */
export function adminLogKindLabel(kind: string): string {
  return KIND_LABELS[kind as AdminUserActionKind] ?? kind;
}

export function adminLogKindTone(kind: string): AdminLogKindTone {
  return KIND_TONES[kind as AdminUserActionKind] ?? 'info';
}

/** The second row a deletion that failed part-way writes starts with this. */
const DELETION_FAILED_PREFIX = 'Deletion failed';

/** `Before: X. After: Y.`, the detail every state change writes. */
const BEFORE_AFTER = /^Before: ([\s\S]+?)\. After: ([\s\S]+?)\.?$/;

/** The log has one order: the server's, newest first. */
const ADMIN_LOG_SORTS: readonly CardListSort[] = [
  { id: 'newest', label: 'Newest first', column: 'occurred', direction: 'desc' }
];

/** The chip columns of the filters a link presets; neither is a facet. */
const OPERATION_COLUMN = 'operation';
const SUBJECT_COLUMN = 'subject';

const NO_KINDS: readonly string[] = [];

/** One card of the log, computed once per fetch. */
export interface AdminLogCard {
  entry: AdminUserLogEntry;
  kindLabel: string;
  tone: AdminLogKindTone;
  time: string;
  subject: string;
  actor: string;
  /** The before and after of a state change, when the detail has that form. */
  change: { before: string; after: string } | null;
  /** Any other detail, shown as written. */
  detailText: string | null;
  /** The bulk operation the action was part of. */
  operationId: number | null;
}

/**
 * The Users tab's Admin log: the durable record of every admin action on an account, newest first,
 * paged on the server in batches of 20. Search over the account, the administrator, the reason and
 * the detail; an Action facet; and the account and bulk operation chips that *Show in admin log*
 * links preset through the page store (`usersLogFilter`), which the log keeps current so a sub-tab
 * switch returns to the same filters.
 */
@Component({
  selector: 'app-admin-user-log',
  standalone: true,
  imports: [FilterFacetComponent],
  templateUrl: './admin-user-log.component.html',
  styleUrl: './admin-user-log.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager   // matches every other component here
})
export class AdminUserLogComponent implements OnInit, OnDestroy {
  private readonly usersService = inject(AdminUsersService);
  private readonly store = inject(AdminPageStore);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  /** The filter the log shows, as last fetched. */
  filter: AdminUsersLogFilter = {};

  /** The entries fetched so far, newest first, and their cards. */
  rows: AdminUserLogEntry[] = [];
  cards: AdminLogCard[] = [];
  totalCount = 0;
  loading = false;
  error: string | null = null;
  /** Whether the first page of the current query has arrived. */
  loaded = false;

  /** The account a link narrowed the log to; its name labels the chip. */
  subjectUserId: string | null = null;
  subjectUserName: string | null = null;
  /** The bulk operation a link narrowed the log to. */
  bulkOperationId: number | null = null;

  /** Each kind's entries under the search, the account and the operation; null until counted. */
  kindCounts: ReadonlyMap<string, number> | null = null;

  /** The server filters and pages the log; the table only carries the filter values. */
  readonly table = new TableState<AdminUserLogEntry>('occurred', 'desc').registerAccessors(
    { occurred: e => parseServerUtcDate(e.occurredUtc).getTime() },
    {
      search: customFilter<AdminUserLogEntry>(() => true),
      kind: anyOfFilter<AdminUserLogEntry>(e => e.kind)
    }
  );

  /** The search, the Action selection and their chips; every change refetches the first page. */
  readonly list = new CardListState<AdminUserLogEntry>(this.table, {
    idPrefix: 'aul',
    batch: ADMIN_LOG_BATCH,
    sorts: ADMIN_LOG_SORTS,
    defaultSort: 'newest',
    storageKey: ADMIN_LOG_VIEW_STORAGE_KEY,
    facets: [{ column: 'kind', label: 'Action', values: e => e.kind, order: ADMIN_USER_ACTION_KINDS, labelOf: adminLogKindLabel }],
    onChange: () => this.reload()
  });

  private filterSub: Subscription | null = null;
  private listSub: Subscription | null = null;
  private countsSub: Subscription | null = null;
  /** The search, account and operation the kind counts were taken for; null forces a recount. */
  private countedKey: string | null = null;

  private kindOptionsMemo: { counts: ReadonlyMap<string, number> | null; selected: readonly string[]; options: FilterFacetOption[] } | null = null;
  private chipsMemo: {
    base: CardListChip[];
    subjectUserId: string | null;
    subjectUserName: string | null;
    bulkOperationId: number | null;
    chips: CardListChip[];
  } | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.table.setRemoteTotal(0);
    this.applyFilter(this.store.usersLogFilter);
    this.reload();
    this.filterSub = this.store.usersLogFilterChanged$.subscribe(() => {
      this.applyFilter(this.store.usersLogFilter);
      this.reload();
      this.cdr.markForCheck();
    });
  }

  ngOnDestroy(): void {
    this.list.dispose();
    this.filterSub?.unsubscribe();
    this.listSub?.unsubscribe();
    this.countsSub?.unsubscribe();
  }

  // --- Filters ---

  /** The Action selection; one empty array while nothing is selected, so the memo and the facet input stay put. */
  get selectedKinds(): readonly string[] {
    const selected = this.table.filterValues('kind');
    return selected.length > 0 ? selected : NO_KINDS;
  }

  /** The Action facet's options: the kinds with entries under the other filters, and any selected, with their counts. */
  get kindOptions(): FilterFacetOption[] {
    const selected = this.selectedKinds;
    const memo = this.kindOptionsMemo;
    if (memo && memo.counts === this.kindCounts && memo.selected === selected) {
      return memo.options;
    }
    const counts = this.kindCounts;
    const options = ADMIN_USER_ACTION_KINDS
      .filter(kind => (counts?.get(kind) ?? 0) > 0 || selected.includes(kind))
      .map(kind => ({ value: kind, label: adminLogKindLabel(kind), count: counts?.get(kind) ?? 0 }));
    this.kindOptionsMemo = { counts, selected, options };
    return options;
  }

  /** The operation and account chips, then the Action and search chips; the same array until one changes. */
  get chips(): CardListChip[] {
    const base = this.list.chips(this.rows);
    const memo = this.chipsMemo;
    if (memo && memo.base === base && memo.subjectUserId === this.subjectUserId
        && memo.subjectUserName === this.subjectUserName && memo.bulkOperationId === this.bulkOperationId) {
      return memo.chips;
    }
    const chips: CardListChip[] = [];
    if (this.bulkOperationId !== null) {
      const value = String(this.bulkOperationId);
      chips.push({ key: `${OPERATION_COLUMN}:${value}`, column: OPERATION_COLUMN, value, facetLabel: 'Operation', valueLabel: `#${value}` });
    }
    if (this.subjectUserId !== null) {
      chips.push({
        key: `${SUBJECT_COLUMN}:${this.subjectUserId}`, column: SUBJECT_COLUMN, value: this.subjectUserId,
        facetLabel: 'Account', valueLabel: this.subjectUserName ?? this.subjectUserId
      });
    }
    chips.push(...base);
    this.chipsMemo = {
      base, subjectUserId: this.subjectUserId, subjectUserName: this.subjectUserName, bulkOperationId: this.bulkOperationId, chips
    };
    return chips;
  }

  get searchTerm(): string {
    return (this.table.filters['search'] ?? '').trim();
  }

  get hasFilters(): boolean {
    return this.searchTerm !== '' || this.selectedKinds.length > 0 || this.subjectUserId !== null || this.bulkOperationId !== null;
  }

  get remainingCount(): number {
    return Math.max(0, this.totalCount - this.rows.length);
  }

  get nextBatchCount(): number {
    return Math.min(this.list.batch, this.remainingCount);
  }

  /** `Showing 20 of 134 actions`, `One action`, or empty with no entries. */
  get listStatus(): string {
    if (this.loading && !this.loaded) return 'Loading the admin log…';
    if (this.error || this.totalCount === 0) return '';
    return this.totalCount === 1 && this.rows.length === 1
      ? 'One action'
      : `Showing ${this.rows.length} of ${this.totalCount} actions`;
  }

  onSearchInput(event: Event): void {
    this.list.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search at once and refetches. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (this.list.clearSearchOnEscape(event)) this.reload();
  }

  onKindChange(values: string[]): void {
    this.list.setFacet('kind', values);
    this.reload();
  }

  /** Removes a chip's filter and refetches, then focuses the chip now in its place, else the previous one, else the search. */
  removeChip(chip: CardListChip): void {
    const index = this.chips.findIndex(c => c.key === chip.key);
    if (chip.column === OPERATION_COLUMN) {
      this.bulkOperationId = null;
    } else if (chip.column === SUBJECT_COLUMN) {
      this.subjectUserId = null;
      this.subjectUserName = null;
    } else {
      this.list.removeChip(chip, this.rows);
      if (chip.column === this.list.searchColumn) this.setSearchField('');
    }
    this.reload();
    this.cdr.detectChanges();
    const chips = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.aul-chips .gh-filter-chip'));
    const target = index >= 0 ? chips[index] ?? chips[index - 1] : undefined;
    (target ?? this.element('#aul-search'))?.focus();
  }

  /** Clears the search, the Action filter, the account and the operation, refetches, then focuses the search. */
  clearFilters(): void {
    this.list.clearFilters();
    this.subjectUserId = null;
    this.subjectUserName = null;
    this.bulkOperationId = null;
    this.setSearchField('');
    this.reload();
    this.element('#aul-search')?.focus();
  }

  // --- Fetching ---

  /** Fetches the first page of the current query, replacing the list, and recounts the kinds when the other filters changed. */
  reload(): void {
    this.filter = this.currentFilter();
    this.store.usersLogFilter = this.filter;
    this.fetch(false);
    if (this.countedKey !== this.countKey()) {
      this.loadCounts();
    }
  }

  /** Appends the next page and focuses the title of the first new card. */
  showMore(): void {
    if (this.loading || this.remainingCount === 0) return;
    this.fetch(true);
  }

  private fetch(append: boolean): void {
    const query: AdminUserLogQuery = {
      ...this.contextQuery(),
      kinds: this.selectedKinds.length > 0 ? [...this.selectedKinds] as AdminUserActionKind[] : undefined,
      skip: append ? this.rows.length : 0,
      take: this.list.batch
    };
    if (!append) {
      this.loaded = false;
    }
    this.loading = true;
    this.error = null;
    this.listSub?.unsubscribe();
    this.listSub = this.usersService.log(query).subscribe({
      next: page => {
        const firstNew = append ? this.rows.length : -1;
        if (append) {
          const known = new Set(this.rows.map(r => r.id));
          this.rows = [...this.rows, ...page.rows.filter(r => !known.has(r.id))];
        } else {
          this.rows = page.rows;
        }
        this.cards = this.rows.map(AdminUserLogComponent.toCard);
        this.totalCount = page.totalCount;
        this.table.setRemoteTotal(page.totalCount);
        this.loading = false;
        this.loaded = true;
        this.list.invalidate();

        const focusId = firstNew >= 0 ? this.rows[firstNew]?.id ?? null : null;
        if (focusId === null) {
          this.cdr.markForCheck();
          return;
        }
        this.cdr.detectChanges();
        refreshAnchorPositioning();
        this.element(`#aul-${focusId}-title`)?.focus();
      },
      error: err => {
        this.loading = false;
        this.loaded = true;
        this.error = 'The admin log could not be loaded: ' + adminApiError(err).message;
        this.cdr.markForCheck();
      }
    });
  }

  /** Counts each kind's entries under the search, the account and the operation; a failed count keeps the counts there are. */
  private loadCounts(): void {
    const key = this.countKey();
    const context = this.contextQuery();
    this.countedKey = key;
    this.countsSub?.unsubscribe();
    this.countsSub = forkJoin(ADMIN_USER_ACTION_KINDS.map(kind =>
      this.usersService.log({ ...context, kinds: [kind], take: 1 }).pipe(map(page => [kind, page.totalCount] as const))
    )).subscribe({
      next: counts => {
        this.kindCounts = new Map<string, number>(counts);
        this.cdr.markForCheck();
      },
      error: () => {
        this.countedKey = null;
      }
    });
  }

  /** The search, account and operation, which the kind counts follow. */
  private contextQuery(): AdminUserLogQuery {
    return {
      search: this.searchTerm || undefined,
      subjectUserId: this.subjectUserId ?? undefined,
      bulkOperationId: this.bulkOperationId ?? undefined
    };
  }

  private countKey(): string {
    return JSON.stringify([this.searchTerm, this.subjectUserId, this.bulkOperationId]);
  }

  /** The filter as the page store keeps it; empty fields are left out. */
  private currentFilter(): AdminUsersLogFilter {
    const filter: AdminUsersLogFilter = {};
    if (this.searchTerm) filter.search = this.searchTerm;
    if (this.selectedKinds.length > 0) filter.kinds = [...this.selectedKinds] as AdminUserActionKind[];
    if (this.subjectUserId !== null) {
      filter.subjectUserId = this.subjectUserId;
      if (this.subjectUserName !== null) filter.subjectUserName = this.subjectUserName;
    }
    if (this.bulkOperationId !== null) filter.bulkOperationId = this.bulkOperationId;
    return filter;
  }

  /** Starts the list over from a filter the page store holds; unknown kinds are dropped. */
  private applyFilter(filter: AdminUsersLogFilter): void {
    this.list.reset();
    const search = filter.search?.trim() ?? '';
    if (search) {
      this.list.searchText = search;
      this.table.setFilter(this.list.searchColumn, search);
    }
    this.setSearchField(search);
    const known: readonly string[] = ADMIN_USER_ACTION_KINDS;
    const kinds = (filter.kinds ?? []).filter(kind => known.includes(kind));
    if (kinds.length > 0) {
      this.list.setFacet('kind', kinds);
    }
    this.subjectUserId = filter.subjectUserId?.trim() || null;
    this.subjectUserName = this.subjectUserId !== null ? filter.subjectUserName?.trim() || null : null;
    this.bulkOperationId = typeof filter.bulkOperationId === 'number' && Number.isFinite(filter.bulkOperationId)
      ? filter.bulkOperationId
      : null;
    this.list.invalidate();
  }

  // --- A card ---

  /** Reopens the Bulk Actions wizard on the operation's results. */
  openResults(operationId: number): void {
    this.store.requestBulkWizard({ operationId });
  }

  private static toCard(entry: AdminUserLogEntry): AdminLogCard {
    const detail = entry.detail?.trim() || null;
    const failed = entry.kind === 'Deleted' && !!detail && detail.startsWith(DELETION_FAILED_PREFIX);
    const match = detail ? BEFORE_AFTER.exec(detail) : null;
    return {
      entry,
      kindLabel: failed ? 'Deletion failed' : adminLogKindLabel(entry.kind),
      tone: adminLogKindTone(entry.kind),
      time: formatUtcDateTime(entry.occurredUtc) || entry.occurredUtc,
      subject: entry.subjectUserName?.trim() || 'Unknown account',
      actor: entry.actorUserName?.trim() || entry.actorUserId || 'Unknown administrator',
      change: match ? { before: match[1], after: match[2] } : null,
      detailText: match ? null : detail,
      operationId: typeof entry.bulkOperationId === 'number' ? entry.bulkOperationId : null
    };
  }

  // --- Helpers ---

  private setSearchField(value: string): void {
    const input = this.element('#aul-search') as HTMLInputElement | null;
    if (input) input.value = value;
  }

  private element(selector: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(selector);
  }
}
