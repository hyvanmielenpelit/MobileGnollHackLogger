import {
  ChangeDetectorRef,
  Component,
  ElementRef,
  OnChanges,
  OnDestroy,
  OnInit,
  SimpleChanges,
  ViewChild,
  inject,
  input,
  output,
  signal
} from '@angular/core';
import { Subscription } from 'rxjs';
import {
  AdminService,
  AiTelemetryUserConfigDto,
  AiTelemetryUserRowDto,
  AiTelemetryUsersQuery,
  AiTelemetryUsersSort
} from '../../../services/admin.service';
import { AdminUsersService } from '../../../services/admin-users.service';
import { formatCost } from '../../benchmark/batteries/battery.models';
import { formatInteger } from '../../benchmark/shared/provider-availability/provider-availability';
import { CardListSort, CardListState } from '../../../shared/data-table/card-list-state';
import { TableState, customFilter } from '../../../shared/data-table/table-state';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { AdminConfirmDialogComponent } from '../../admin-dialogs/admin-confirm-dialog.component';
import { AdminReasonDialogComponent } from '../../admin-dialogs/admin-reason-dialog.component';
import { AdminPageStore } from '../../admin-page.store';

/** Where the list's Sort by choice is kept, per browser. */
export const TELEMETRY_USERS_VIEW_STORAGE_KEY = 'overseer.admin.telemetry.users.view';

/** The users the list fetches at first, and how many more Show more fetches. */
export const TELEMETRY_USERS_BATCH = 20;

/** The longest block reason the server accepts. */
export const TELEMETRY_BLOCK_REASON_MAX = 256;

/** The largest page the server returns. */
const MAX_PAGE = 100;

/** The orders Sort by offers; the server sorts. Highest cost is the default. */
export const TELEMETRY_USERS_SORTS: readonly CardListSort[] = [
  { id: 'cost', label: 'Highest cost', column: 'cost', direction: 'desc' },
  { id: 'requests', label: 'Most requests', column: 'requests', direction: 'desc' },
  { id: 'tokens', label: 'Most tokens', column: 'tokens', direction: 'desc' },
  { id: 'peakHour', label: 'Busiest hour', column: 'peakHour', direction: 'desc' },
  { id: 'refusals', label: 'Most refusals', column: 'refusals', direction: 'desc' },
  { id: 'lastActive', label: 'Most recently active', column: 'lastActive', direction: 'desc' }
];

/** The user whose usage charts were asked for. */
export interface TelemetryUserRef {
  userId: string;
  userName: string;
}

const LIMIT_SOURCE_LABELS: Readonly<Record<AiTelemetryUserConfigDto['limitSource'], string>> = {
  user: 'user',
  group: 'group',
  systemWide: 'system-wide',
  none: '—'
};

/**
 * The AI Telemetry tab's usage of system AI configurations by user: a server-paged card list with a
 * search by user name or email and a Sort by order, each user with the usage figures, the limits of
 * the configurations they used, and actions to open their usage charts, manage their limits and
 * block or unblock them. The figures are information for setting limits, never a misuse signal.
 */
@Component({
  selector: 'app-telemetry-users',
  imports: [AdminReasonDialogComponent, AdminConfirmDialogComponent],
  templateUrl: './telemetry-users.component.html',
  styleUrl: './telemetry-users.component.scss'
})
export class TelemetryUsersComponent implements OnInit, OnChanges, OnDestroy {
  private readonly adminService = inject(AdminService);
  private readonly usersService = inject(AdminUsersService);
  private readonly store = inject(AdminPageStore);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild(AdminReasonDialogComponent, { static: true }) reasonDialog!: AdminReasonDialogComponent;
  @ViewChild(AdminConfirmDialogComponent, { static: true }) confirmDialog!: AdminConfirmDialogComponent;

  /** The tab's date range, "YYYY-MM-DD"; empty for no bound. */
  readonly startDate = input<string>('');
  readonly endDate = input<string>('');

  /** Usage charts was pressed for a user. */
  readonly usageCharts = output<TelemetryUserRef>();

  readonly sorts = TELEMETRY_USERS_SORTS;
  readonly formatInteger = formatInteger;
  readonly formatCost = formatCost;

  /** The users fetched so far, in the server's order. */
  readonly rows = signal<AiTelemetryUserRowDto[]>([]);
  readonly totalUsers = signal(0);
  readonly securityEventsAvailable = signal(true);
  readonly loading = signal(false);
  /** Whether the first page of the current query has arrived. */
  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);
  /** The one user the list is restricted to, from a Usage link. */
  readonly userFilter = signal<TelemetryUserRef | null>(null);
  /** The user whose Block or Unblock is in flight. */
  readonly busyUserId = signal<string | null>(null);

  /** The server pages and sorts the users; the table only carries the search. */
  readonly table = new TableState<AiTelemetryUserRowDto>('cost', 'desc').registerAccessors(
    {},
    { search: customFilter<AiTelemetryUserRowDto>(() => true) }
  );

  /** The search and Sort by; a change refetches the first page. */
  readonly list = new CardListState<AiTelemetryUserRowDto>(this.table, {
    idPrefix: 'tl-users',
    batch: TELEMETRY_USERS_BATCH,
    sorts: TELEMETRY_USERS_SORTS,
    defaultSort: 'cost',
    storageKey: TELEMETRY_USERS_VIEW_STORAGE_KEY,
    facets: [],
    onChange: () => this.reload()
  });

  private listSub: Subscription | null = null;
  private initialized = false;

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.table.setRemoteTotal(0);
    this.initialized = true;
    this.reload();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const rangeChanged = (changes['startDate'] && !changes['startDate'].firstChange)
      || (changes['endDate'] && !changes['endDate'].firstChange);
    if (rangeChanged && this.initialized) {
      this.reload();
    }
  }

  ngOnDestroy(): void {
    this.list.dispose();
    this.listSub?.unsubscribe();
  }

  // --- Filters ---

  get searchTerm(): string {
    return (this.table.filters['search'] ?? '').trim();
  }

  get sortKey(): AiTelemetryUsersSort {
    return this.list.sortId as AiTelemetryUsersSort;
  }

  get remainingCount(): number {
    return Math.max(0, this.totalUsers() - this.rows().length);
  }

  get nextBatchCount(): number {
    return Math.min(this.list.batch, this.remainingCount);
  }

  /** `Showing 20 of 143 users`, `One user`, or `Showing 1 user — alice` while restricted to one. */
  get listStatus(): string {
    if (this.loading() && !this.loaded()) return 'Loading users…';
    if (this.error()) return '';
    const filter = this.userFilter();
    if (filter) return `Showing 1 user — ${filter.userName}`;
    const total = this.totalUsers();
    if (total === 0) return '';
    return total === 1 && this.rows().length === 1 ? 'One user' : `Showing ${this.rows().length} of ${formatInteger(total)} users`;
  }

  onSearchInput(event: Event): void {
    this.list.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search at once and refetches. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (this.list.clearSearchOnEscape(event)) this.reload();
  }

  onSortChange(event: Event): void {
    if (this.list.setSort((event.target as HTMLSelectElement).value)) {
      this.reload();
    }
  }

  /** Restricts the list to one user and refetches once the list exists. */
  filterToUser(userId: string, userName: string): void {
    this.userFilter.set({ userId, userName: userName || userId });
    if (this.initialized) this.reload();
  }

  /** Drops the one-user restriction, refetches, then focuses the section heading. */
  clearUserFilter(): void {
    this.userFilter.set(null);
    this.reload();
    document.getElementById('tl-users-heading')?.focus();
  }

  // --- Fetching ---

  /** Fetches the first page of the current query, replacing the list. */
  reload(take: number = this.list.batch): void {
    this.fetch(false, take);
  }

  /** Appends the next page and focuses the title of the first new card. */
  showMore(): void {
    if (this.loading() || this.remainingCount === 0) return;
    this.fetch(true, this.list.batch);
  }

  private fetch(append: boolean, take: number): void {
    const filter = this.userFilter();
    const query: AiTelemetryUsersQuery = {
      startDate: this.startDate() || undefined,
      endDate: this.endDate() || undefined,
      search: filter ? undefined : this.searchTerm || undefined,
      userId: filter?.userId,
      sort: this.sortKey,
      skip: append ? this.rows().length : 0,
      take
    };
    if (!append) this.loaded.set(false);
    this.loading.set(true);
    this.error.set(null);
    this.listSub?.unsubscribe();
    this.listSub = this.adminService.getAiTelemetryUsers(query).subscribe({
      next: page => {
        const firstNew = append ? this.rows().length : -1;
        if (append) {
          const known = new Set(this.rows().map(u => u.userId));
          this.rows.set([...this.rows(), ...page.users.filter(u => !known.has(u.userId))]);
        } else {
          this.rows.set(page.users);
        }
        this.totalUsers.set(page.totalUsers);
        this.securityEventsAvailable.set(page.securityEventsAvailable);
        this.table.setRemoteTotal(page.totalUsers);
        this.loading.set(false);
        this.loaded.set(true);
        if (firstNew >= 0 && firstNew < this.rows().length) {
          this.focusAfterRender(`tl-users-${firstNew}-title`);
        }
      },
      error: err => {
        this.loading.set(false);
        this.loaded.set(true);
        this.error.set('The users could not be loaded: ' + TelemetryUsersComponent.errorText(err));
      }
    });
  }

  /** Renders the view now, then focuses an element in it. */
  private focusAfterRender(id: string): void {
    this.cdr.detectChanges();
    refreshAnchorPositioning();
    this.host.nativeElement.querySelector<HTMLElement>(`#${id}`)?.focus();
  }

  /** Refetches the cards shown, keeping their number. */
  private refetchShown(): void {
    this.reload(Math.min(MAX_PAGE, Math.max(this.list.batch, this.rows().length)));
  }

  // --- A card ---

  /** `2026-10-13` (UTC). */
  dateText(utc: string | null | undefined): string {
    if (!utc) return '';
    const date = parseServerUtcDate(utc);
    if (isNaN(date.getTime())) return utc;
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
  }

  refusals(user: AiTelemetryUserRowDto): number {
    return user.limitRefusals + user.providerRefusals;
  }

  /** `3 refusals`, `1 refusal`. */
  refusalsText(user: AiTelemetryUserRowDto): string {
    const n = this.refusals(user);
    return `${formatInteger(n)} ${n === 1 ? 'refusal' : 'refusals'}`;
  }

  unpricedRequests(user: AiTelemetryUserRowDto): number {
    return Math.max(0, user.requests - user.pricedRequests);
  }

  /** `12 requests`, `1 request`. */
  requestsText(n: number): string {
    return `${formatInteger(n)} ${n === 1 ? 'request' : 'requests'}`;
  }

  /** The typical requests in an active hour, to one decimal below 10. */
  perHourText(value: number): string {
    if (!Number.isFinite(value)) return '0';
    return value >= 10 || Number.isInteger(value) ? formatInteger(Math.round(value)) : value.toFixed(1);
  }

  /** `12 / 50`, or `12 (no limit)` when the maximum is null or 0. */
  limitText(count: number | null | undefined, max: number | null | undefined): string {
    const used = formatInteger(count ?? 0);
    return max ? `${used} / ${formatInteger(max)}` : `${used} (no limit)`;
  }

  limitSourceText(config: AiTelemetryUserConfigDto): string {
    return LIMIT_SOURCE_LABELS[config.limitSource] ?? config.limitSource;
  }

  // --- Actions ---

  openUsageCharts(user: AiTelemetryUserRowDto): void {
    this.usageCharts.emit({ userId: user.userId, userName: user.userName });
  }

  /** Opens Manage User Configs for the user on the Users tab. */
  manageLimits(user: AiTelemetryUserRowDto): void {
    this.store.requestManageUser(user.userId, user.userName);
  }

  openBlock(user: AiTelemetryUserRowDto): void {
    if (this.busyUserId() !== null) return;
    this.reasonDialog.open({
      title: 'Block from the Overseer AI chat',
      message: `${user.userName} keeps signing in, reading their chats, exporting their data, deleting their account and changing their settings, but cannot start any AI work in Overseer. The reason is shown to them.`,
      label: 'Reason',
      maxLength: TELEMETRY_BLOCK_REASON_MAX,
      confirmText: 'Block',
      confirmClass: 'btn-gh btn-gh-delete'
    }, reason => this.block(user, reason));
  }

  private block(user: AiTelemetryUserRowDto, reason: string): void {
    this.busyUserId.set(user.userId);
    this.usersService.blockFromAiChat(user.userId, reason).subscribe({
      next: () => {
        this.busyUserId.set(null);
        this.store.showToast(`${user.userName} is blocked from the Overseer AI chat.`, 'success', 'User Blocked');
        this.refetchShown();
      },
      error: err => {
        this.busyUserId.set(null);
        this.store.showToast(err?.error?.message || `Could not block ${user.userName}.`, 'error', 'Error');
      }
    });
  }

  openUnblock(user: AiTelemetryUserRowDto): void {
    if (this.busyUserId() !== null) return;
    this.confirmDialog.open({
      title: 'Unblock from the Overseer AI chat',
      message: `${user.userName} can start AI work in Overseer again.`,
      buttonText: 'Unblock',
      buttonClass: 'btn-gh'
    }, () => this.unblock(user));
  }

  private unblock(user: AiTelemetryUserRowDto): void {
    this.busyUserId.set(user.userId);
    this.usersService.unblockFromAiChat(user.userId).subscribe({
      next: () => {
        this.busyUserId.set(null);
        this.store.showToast(`${user.userName} can use the Overseer AI chat again.`, 'success', 'User Unblocked');
        this.refetchShown();
      },
      error: err => {
        this.busyUserId.set(null);
        this.store.showToast(err?.error?.message || `Could not unblock ${user.userName}.`, 'error', 'Error');
      }
    });
  }

  private static errorText(err: unknown): string {
    const http = err as { error?: { message?: string } | string; message?: string };
    if (http?.error && typeof http.error === 'object' && http.error.message) return http.error.message;
    if (typeof http?.error === 'string' && http.error) return http.error;
    return http?.message || 'Unknown error';
  }
}
