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
  AiTelemetryKeySource,
  AiTelemetryUserConfigDto,
  AiTelemetryUserRowDto,
  AiTelemetryUsersQuery
} from '../../../services/admin.service';
import { AdminUsersService } from '../../../services/admin-users.service';
import { formatCost } from '../../benchmark/batteries/battery.models';
import { formatInteger } from '../../benchmark/shared/provider-availability/provider-availability';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { AdminConfirmDialogComponent } from '../../admin-dialogs/admin-confirm-dialog.component';
import { AdminReasonDialogComponent } from '../../admin-dialogs/admin-reason-dialog.component';
import { AdminPageStore } from '../../admin-page.store';
import {
  DEFAULT_TELEMETRY_USERS_SORT,
  TELEMETRY_USERS_SORT_FIELDS,
  TelemetryUsersSort,
  TelemetryUsersSortDirection,
  TelemetryUsersSortField,
  TelemetryUsersView,
  isDefaultUsersSort,
  readUsersView,
  usersDirectionLabel,
  usersSortField,
  usersSortLabel,
  writeUsersView
} from './telemetry-users-view';

/** Where the view's sort is kept, per browser. */
export { TELEMETRY_USERS_VIEW_STORAGE_KEY } from './telemetry-users-view';

/** The users the list fetches at first, and how many more Show more fetches. */
export const TELEMETRY_USERS_BATCH = 20;

/** The longest block reason the server accepts. */
export const TELEMETRY_BLOCK_REASON_MAX = 256;

/** The largest page the server returns. */
const MAX_PAGE = 100;

/** The applied search follows the field after this much quiet, so the status line is not announced per keystroke. */
const SEARCH_DEBOUNCE_MS = 200;

/** The user whose usage charts were asked for. */
export interface TelemetryUserRef {
  userId: string;
  userName: string;
}

/** The user whose usage by model was asked for, and the button to return focus to. */
export interface TelemetryUserModelsRef {
  userId: string;
  userName: string;
  opener: HTMLElement | null;
}

const LIMIT_SOURCE_LABELS: Readonly<Record<AiTelemetryUserConfigDto['limitSource'], string>> = {
  user: 'user',
  group: 'group',
  systemWide: 'system-wide',
  ownKey: 'own key',
  none: '—'
};

const SCOPE_TEXTS: Readonly<Record<AiTelemetryKeySource, string>> = {
  system: 'System AI configurations. Chats, messages and refusals count every key.',
  user: "Users' own API keys. Limits apply to system AI configurations only; chats, messages and refusals count every key.",
  both: "System AI configurations and users' own keys. Chats, messages and refusals count every key."
};

const EMPTY_TEXTS: Readonly<Record<AiTelemetryKeySource, string>> = {
  system: 'No system AI usage in this range.',
  user: "No usage of users' own keys in this range.",
  both: 'No AI usage in this range.'
};

/**
 * The AI Telemetry tab's Users view: AI usage by user on the tab's key sources, as a server-paged
 * card list with a search by user name or email and a Sort popover of a field and an order, each
 * user with the usage figures, the configurations they used with their limits, and actions to open
 * their usage by model and usage charts, manage their limits and block or unblock them. The
 * figures are information for setting limits, never a misuse signal.
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
  /** The tab's Keys filter. */
  readonly keys = input<AiTelemetryKeySource>('system');

  /** Usage charts was pressed for a user. */
  readonly usageCharts = output<TelemetryUserRef>();
  /** Usage by model was pressed for a user. */
  readonly usageByModel = output<TelemetryUserModelsRef>();

  readonly sortFields = TELEMETRY_USERS_SORT_FIELDS;
  readonly sortDirections: readonly TelemetryUsersSortDirection[] = ['desc', 'asc'];
  readonly batch = TELEMETRY_USERS_BATCH;
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

  /** The sort, remembered per browser. */
  view: TelemetryUsersView = readUsersView();
  /** The search field as typed. */
  searchInput = '';
  /** The search the list applies; follows `searchInput` after a short quiet. Never stored. */
  search = '';
  /** Whether the Sort popover is open; the popover polyfill does not set `aria-expanded`. */
  sortOpen = false;

  private listSub: Subscription | null = null;
  private telemetryUserSub: Subscription | null = null;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private initialized = false;

  ngOnInit(): void {
    ensureOverlayPolyfills();
    const pending = this.takePendingUser();
    if (pending) this.userFilter.set(pending);
    this.telemetryUserSub = this.store.telemetryUserRequested$.subscribe(() => {
      const requested = this.takePendingUser();
      if (requested) {
        this.filterToUser(requested.userId, requested.userName);
        this.focusAfterRender('tl-users-filter');
      }
    });
    this.initialized = true;
    this.reload();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const changed = (name: string) => !!changes[name] && !changes[name].firstChange;
    if (this.initialized && (changed('keys') || changed('startDate') || changed('endDate'))) {
      this.reload();
    }
  }

  ngOnDestroy(): void {
    this.cancelSearchTimer();
    this.listSub?.unsubscribe();
    this.telemetryUserSub?.unsubscribe();
  }

  // --- Filters ---

  get searchTerm(): string {
    return this.search.trim();
  }

  get remainingCount(): number {
    return Math.max(0, this.totalUsers() - this.rows().length);
  }

  get nextBatchCount(): number {
    return Math.min(this.batch, this.remainingCount);
  }

  /** What the counts cover, by the tab's Keys filter. */
  get scopeText(): string {
    return SCOPE_TEXTS[this.keys()] ?? SCOPE_TEXTS.system;
  }

  get emptyText(): string {
    if (this.searchTerm && !this.userFilter()) return 'No users match this search.';
    return EMPTY_TEXTS[this.keys()] ?? EMPTY_TEXTS.system;
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
    this.searchInput = (event.target as HTMLInputElement).value;
    this.cancelSearchTimer();
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.applySearch(this.searchInput);
      this.cdr.markForCheck();
    }, SEARCH_DEBOUNCE_MS);
  }

  /** Escape with text clears the search at once and refetches; with none, Escape is left alone. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape' || this.searchInput === '') return;
    event.preventDefault();
    event.stopPropagation();
    this.clearSearch();
  }

  /** Clears the search and focuses the search field. */
  clearFilters(): void {
    this.clearSearch();
    this.focusAfterRender('tl-users-search');
  }

  /** Restricts the list to one user and refetches once the list exists. */
  filterToUser(userId: string, userName: string): void {
    this.userFilter.set({ userId, userName: userName || userId });
    if (this.initialized) this.reload();
  }

  /** Drops the one-user restriction, refetches, then focuses the search field it brings back. */
  clearUserFilter(): void {
    this.userFilter.set(null);
    this.reload();
    this.focusAfterRender('tl-users-search');
  }

  private clearSearch(): void {
    this.cancelSearchTimer();
    this.searchInput = '';
    this.applySearch('');
  }

  /** Applies the search; a change of the trimmed text refetches the first page. */
  private applySearch(text: string): void {
    const changed = text.trim() !== this.search.trim();
    this.search = text;
    if (changed) this.reload();
  }

  private cancelSearchTimer(): void {
    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }
  }

  /** Reads and clears the page store's pending user; null when there is none. The name falls back to the id. */
  private takePendingUser(): TelemetryUserRef | null {
    const id = this.store.pendingTelemetryUserId;
    if (!id) return null;
    const name = this.store.pendingTelemetryUserName || id;
    this.store.pendingTelemetryUserId = null;
    this.store.pendingTelemetryUserName = null;
    return { userId: id, userName: name };
  }

  // --- Sort ---

  /** `Est. cost, highest first`. */
  get sortLabel(): string {
    return usersSortLabel(this.view.sort);
  }

  get sortIsDefault(): boolean {
    return isDefaultUsersSort(this.view.sort);
  }

  /** `Highest first`, `Newest first` or `A–Z`, by the kind of the chosen field. */
  directionLabel(direction: TelemetryUsersSortDirection): string {
    return usersDirectionLabel(usersSortField(this.view.sort.field).kind, direction);
  }

  /** Applies at once and keeps the direction; the popover stays open. */
  chooseSortField(field: TelemetryUsersSortField): void {
    if (field !== this.view.sort.field) {
      this.saveSort({ field, direction: this.view.sort.direction });
    }
  }

  chooseSortDirection(direction: TelemetryUsersSortDirection): void {
    if (direction !== this.view.sort.direction) {
      this.saveSort({ field: this.view.sort.field, direction });
    }
  }

  /** Back to the highest cost first; refused at the default (the button is aria-disabled), and the button keeps focus. */
  resetSort(): void {
    if (!this.sortIsDefault) {
      this.saveSort(DEFAULT_TELEMETRY_USERS_SORT);
    }
  }

  onSortToggle(event: Event): void {
    this.sortOpen = (event as ToggleEvent).newState === 'open';
    const panel = this.sortPanel();
    if (this.sortOpen) {
      refreshAnchorPositioning();
      (panel?.querySelector<HTMLInputElement>('input:checked') ?? panel?.querySelector<HTMLInputElement>('input'))?.focus();
      return;
    }
    const active = document.activeElement;
    if (!active || active === document.body || !!panel?.contains(active)) {
      this.sortTrigger()?.focus();
    }
  }

  /** Escape closes the Sort popover only. */
  onSortPanelKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape') return;
    event.preventDefault();
    event.stopPropagation();
    try {
      this.sortPanel()?.hidePopover();
    } catch {
      // Already hidden.
    }
    this.sortTrigger()?.focus();
  }

  /** Stores the sort and refetches the first page in it. */
  private saveSort(sort: TelemetryUsersSort): void {
    this.view = { ...this.view, sort };
    writeUsersView(this.view);
    this.reload();
  }

  private sortPanel(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>('#tl-users-sort-panel');
  }

  private sortTrigger(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>('#tl-users-sort-trigger');
  }

  // --- Fetching ---

  /** Fetches the first page of the current query, replacing the list. */
  reload(take: number = this.batch): void {
    this.fetch(false, take);
  }

  /** Appends the next page and focuses the title of the first new card. */
  showMore(): void {
    if (this.loading() || this.remainingCount === 0) return;
    this.fetch(true, this.batch);
  }

  private fetch(append: boolean, take: number): void {
    const filter = this.userFilter();
    const query: AiTelemetryUsersQuery = {
      startDate: this.startDate() || undefined,
      endDate: this.endDate() || undefined,
      search: filter ? undefined : this.searchTerm || undefined,
      userId: filter?.userId,
      keys: this.keys(),
      sort: this.view.sort.field,
      direction: this.view.sort.direction,
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
    this.reload(Math.min(MAX_PAGE, Math.max(this.batch, this.rows().length)));
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

  /** Whether the card's Account state row has a badge. */
  hasBadges(user: AiTelemetryUserRowDto): boolean {
    return user.isOverseerBlocked || user.hasAccountHold || (this.securityEventsAvailable() && this.refusals(user) > 0);
  }

  /** `chat 8 · title 2`, with `· benchmark 3` when there are benchmark requests. */
  requestsNote(user: AiTelemetryUserRowDto): string {
    const parts = [`chat ${formatInteger(user.chatRequests)}`, `title ${formatInteger(user.titleRequests)}`];
    if (user.benchmarkRequests > 0) parts.push(`benchmark ${formatInteger(user.benchmarkRequests)}`);
    return parts.join(' · ');
  }

  /** Under both key sources: `system 7 · own key 3`; otherwise empty. */
  keySplitNote(user: AiTelemetryUserRowDto): string {
    if (this.keys() !== 'both') return '';
    return `system ${formatInteger(user.systemRequests)} · own key ${formatInteger(user.userKeyRequests)}`;
  }

  /** Under both key sources: `own key $0.12`; otherwise empty. */
  ownKeyCostNote(user: AiTelemetryUserRowDto): string {
    if (this.keys() !== 'both') return '';
    return `own key ${formatCost(user.userKeyEstimatedCost)}`;
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

  /** `12 / 50` on a system configuration; a dash on the user's own key, which has no limits here. */
  configLimitText(config: AiTelemetryUserConfigDto, count: number | null | undefined, max: number | null | undefined): string {
    return config.keySource === 'user' ? '—' : this.limitText(count, max);
  }

  limitSourceText(config: AiTelemetryUserConfigDto): string {
    return LIMIT_SOURCE_LABELS[config.limitSource] ?? config.limitSource;
  }

  // --- Actions ---

  openUsageByModel(user: AiTelemetryUserRowDto, event: Event): void {
    this.usageByModel.emit({
      userId: user.userId,
      userName: user.userName,
      opener: (event.currentTarget as HTMLElement | null) ?? null
    });
  }

  openUsageCharts(user: AiTelemetryUserRowDto): void {
    this.usageCharts.emit({ userId: user.userId, userName: user.userName });
  }

  /** Opens AI configuration access for the user on the Users tab. */
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
