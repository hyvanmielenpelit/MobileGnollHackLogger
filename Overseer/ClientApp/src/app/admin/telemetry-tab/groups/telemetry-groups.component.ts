import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  OnChanges,
  OnDestroy,
  OnInit,
  SimpleChanges,
  computed,
  inject,
  input,
  output,
  signal
} from '@angular/core';
import { Subscription } from 'rxjs';
import {
  AdminService,
  AiTelemetryGroupConfigDto,
  AiTelemetryGroupKey,
  AiTelemetryGroupRowDto,
  AiTelemetryKeySource
} from '../../../services/admin.service';
import { formatCost } from '../../benchmark/batteries/battery.models';
import { formatInteger } from '../../benchmark/shared/provider-availability/provider-availability';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { AdminPageStore } from '../../admin-page.store';
import { TelemetrySort, TelemetrySortPopoverComponent } from '../sort-popover/telemetry-sort-popover.component';
import {
  DEFAULT_TELEMETRY_GROUPS_SORT,
  TELEMETRY_GROUPS_SORT_FIELDS,
  TelemetryGroupsSort,
  TelemetryGroupsView,
  groupDisplayName,
  groupKeyOf,
  readGroupsView,
  searchGroups,
  sortGroups,
  writeGroupsView
} from './telemetry-groups-view';

/** Where the view's sort is kept, per browser. */
export { TELEMETRY_GROUPS_VIEW_STORAGE_KEY } from './telemetry-groups-view';

/** The cards rendered at first, and how many more Show more renders. */
export const TELEMETRY_GROUPS_BATCH = 20;

/** The applied search follows the field after this much quiet, so the status line is not announced per keystroke. */
const SEARCH_DEBOUNCE_MS = 200;

/** The group whose usage charts were asked for; `'none'` is the users in no group. */
export interface TelemetryGroupRef {
  groupId: AiTelemetryGroupKey;
  groupName: string;
}

/** The group whose usage by model was asked for, and the button to return focus to. */
export interface TelemetryGroupModelsRef {
  groupId: AiTelemetryGroupKey;
  groupName: string;
  opener: HTMLElement | null;
}

const GROUP_SCOPE_TEXT = "A group counts its current members' usage over the whole range; a member of two groups counts in both.";

const SCOPE_TEXTS: Readonly<Record<AiTelemetryKeySource, string>> = {
  system: 'System AI configurations. Chats, messages and refusals count every key.',
  user: "Users' own API keys. Limits apply to system AI configurations only; chats, messages and refusals count every key.",
  both: "System AI configurations and users' own keys. Chats, messages and refusals count every key."
};

/**
 * The AI Telemetry tab's Groups view: AI usage by group on the tab's key sources, every group and
 * the users in no group as a card list the browser searches, sorts and shows in batches. A group's
 * figures are its current members' usage over the whole range, so a member of two groups counts in
 * both and the cards do not add up to the total. Each card has the usage figures, the
 * configurations the group grants or its members used with the group's pooled limits, and actions
 * to open its usage by model and usage charts and to manage its limits.
 */
@Component({
  selector: 'app-telemetry-groups',
  imports: [TelemetrySortPopoverComponent],
  templateUrl: './telemetry-groups.component.html',
  styleUrl: './telemetry-groups.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TelemetryGroupsComponent implements OnInit, OnChanges, OnDestroy {
  private readonly adminService = inject(AdminService);
  private readonly store = inject(AdminPageStore);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly cdr = inject(ChangeDetectorRef);

  /** The tab's date range, "YYYY-MM-DD"; empty for no bound. */
  readonly startDate = input<string>('');
  readonly endDate = input<string>('');
  /** The tab's Keys filter. */
  readonly keys = input<AiTelemetryKeySource>('system');

  /** Usage charts was pressed for a group. */
  readonly usageCharts = output<TelemetryGroupRef>();
  /** Usage by model was pressed for a group. */
  readonly usageByModel = output<TelemetryGroupModelsRef>();

  readonly sortFields = TELEMETRY_GROUPS_SORT_FIELDS;
  readonly defaultSort = DEFAULT_TELEMETRY_GROUPS_SORT;
  readonly batch = TELEMETRY_GROUPS_BATCH;
  readonly formatInteger = formatInteger;
  readonly formatCost = formatCost;
  readonly displayName = groupDisplayName;

  /** Every group the server returned, in its order. */
  readonly rows = signal<AiTelemetryGroupRowDto[]>([]);
  readonly securityEventsAvailable = signal(true);
  readonly loading = signal(false);
  /** Whether the current query has answered. */
  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);

  /** The sort, remembered per browser. */
  readonly view = signal<TelemetryGroupsView>(readGroupsView());
  /** The search the list applies; follows `searchInput` after a short quiet. Never stored. */
  readonly search = signal('');
  /** The cards the batch renders. */
  readonly visibleCount = signal(TELEMETRY_GROUPS_BATCH);

  /** The rows matching the search, in the sort. */
  readonly matching = computed(() => sortGroups(searchGroups(this.rows(), this.search()), this.view().sort));
  readonly visibleRows = computed(() => this.matching().slice(0, this.visibleCount()));

  /** The search field as typed. */
  searchInput = '';

  private listSub: Subscription | null = null;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private initialized = false;

  ngOnInit(): void {
    ensureOverlayPolyfills();
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
  }

  // --- Filters ---

  get remainingCount(): number {
    return Math.max(0, this.matching().length - this.visibleCount());
  }

  get nextBatchCount(): number {
    return Math.min(this.batch, this.remainingCount);
  }

  /** What the counts cover, by the tab's Keys filter, and how a group counts. */
  get scopeText(): string {
    return `${SCOPE_TEXTS[this.keys()] ?? SCOPE_TEXTS.system} ${GROUP_SCOPE_TEXT}`;
  }

  get emptyText(): string {
    return this.rows().length > 0 && this.search().trim() ? 'No groups match this search.' : 'No group used AI in this range.';
  }

  /** `Showing 20 of 34 groups`, or `One group`. */
  get listStatus(): string {
    if (this.loading() && !this.loaded()) return 'Loading groups…';
    if (this.error()) return '';
    const total = this.matching().length;
    if (total === 0) return '';
    const shown = Math.min(this.visibleCount(), total);
    return total === 1 ? 'One group' : `Showing ${formatInteger(shown)} of ${formatInteger(total)} groups`;
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

  /** Escape with text clears the search at once; with none, Escape is left alone. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape' || this.searchInput === '') return;
    event.preventDefault();
    event.stopPropagation();
    this.clearSearch();
  }

  /** Clears the search and focuses the search field. */
  clearFilters(): void {
    this.clearSearch();
    this.focusAfterRender('tl-groups-search');
  }

  private clearSearch(): void {
    this.cancelSearchTimer();
    this.searchInput = '';
    this.applySearch('');
  }

  /** Applies the search; a change of the trimmed text starts the batch again. */
  private applySearch(text: string): void {
    if (text.trim() !== this.search().trim()) {
      this.visibleCount.set(this.batch);
    }
    this.search.set(text);
  }

  private cancelSearchTimer(): void {
    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }
  }

  // --- Sort ---

  /** Stores a sort of a known field and starts the batch again. */
  onSortChange(sort: TelemetrySort): void {
    const info = this.sortFields.find(f => f.field === sort.field);
    const current = this.view().sort;
    if (!info || (info.field === current.field && sort.direction === current.direction)) return;
    const next: TelemetryGroupsSort = { field: info.field, direction: sort.direction };
    this.view.set({ ...this.view(), sort: next });
    writeGroupsView(this.view());
    this.visibleCount.set(this.batch);
  }

  // --- Fetching ---

  /** Fetches every group of the current range and key sources, replacing the list, on one batch. */
  reload(): void {
    this.loaded.set(false);
    this.loading.set(true);
    this.error.set(null);
    this.listSub?.unsubscribe();
    this.listSub = this.adminService.getAiTelemetryGroups({
      startDate: this.startDate() || undefined,
      endDate: this.endDate() || undefined,
      keys: this.keys()
    }).subscribe({
      next: dto => {
        this.rows.set(dto.groups);
        this.securityEventsAvailable.set(dto.securityEventsAvailable);
        this.visibleCount.set(this.batch);
        this.loading.set(false);
        this.loaded.set(true);
      },
      error: err => {
        this.rows.set([]);
        this.loading.set(false);
        this.loaded.set(true);
        this.error.set('The groups could not be loaded: ' + TelemetryGroupsComponent.errorText(err));
      }
    });
  }

  /** Renders the next batch, or every matching group, and focuses the title of the first new card. */
  showMore(all = false): void {
    const first = this.visibleCount();
    if (first >= this.matching().length) return;
    this.visibleCount.set(all ? this.matching().length : first + this.batch);
    this.focusAfterRender(`tl-groups-${first}-title`);
  }

  /** Renders the view now, then focuses an element in it. */
  private focusAfterRender(id: string): void {
    this.cdr.detectChanges();
    refreshAnchorPositioning();
    this.host.nativeElement.querySelector<HTMLElement>(`#${id}`)?.focus();
  }

  // --- A card ---

  /** Whether the card is the users in no group. */
  isNoGroup(row: AiTelemetryGroupRowDto): boolean {
    return row.groupId == null;
  }

  trackKey(row: AiTelemetryGroupRowDto): string {
    return String(groupKeyOf(row));
  }

  /** `2026-10-13` (UTC). */
  dateText(utc: string | null | undefined): string {
    if (!utc) return '';
    const date = parseServerUtcDate(utc);
    if (isNaN(date.getTime())) return utc;
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
  }

  /** `12 members`, `1 member`; `3 users` for the users in no group. */
  membersText(row: AiTelemetryGroupRowDto): string {
    const n = row.memberCount;
    const noun = this.isNoGroup(row) ? (n === 1 ? 'user' : 'users') : (n === 1 ? 'member' : 'members');
    return `${formatInteger(n)} ${noun}`;
  }

  /** `9 active in this range`. */
  activeText(row: AiTelemetryGroupRowDto): string {
    return `${formatInteger(row.activeMembers)} active in this range`;
  }

  refusals(row: AiTelemetryGroupRowDto): number {
    return row.limitRefusals + row.providerRefusals;
  }

  /** `3 refusals`, `1 refusal`. */
  refusalsText(row: AiTelemetryGroupRowDto): string {
    const n = this.refusals(row);
    return `${formatInteger(n)} ${n === 1 ? 'refusal' : 'refusals'}`;
  }

  /** `2 blocked members`, `1 blocked member`. */
  blockedText(row: AiTelemetryGroupRowDto): string {
    const n = row.blockedMembers;
    return `${formatInteger(n)} blocked ${this.isNoGroup(row) ? (n === 1 ? 'user' : 'users') : (n === 1 ? 'member' : 'members')}`;
  }

  /** Whether the card's Group state row has a badge. */
  hasBadges(row: AiTelemetryGroupRowDto): boolean {
    return this.isNoGroup(row) || row.blockedMembers > 0 || (this.securityEventsAvailable() && this.refusals(row) > 0);
  }

  /** `chat 8 · title 2`, with `· benchmark 3` when there are benchmark requests. */
  requestsNote(row: AiTelemetryGroupRowDto): string {
    const parts = [`chat ${formatInteger(row.chatRequests)}`, `title ${formatInteger(row.titleRequests)}`];
    if (row.benchmarkRequests > 0) parts.push(`benchmark ${formatInteger(row.benchmarkRequests)}`);
    return parts.join(' · ');
  }

  /** Under both key sources: `system 7 · own key 3`; otherwise empty. */
  keySplitNote(row: AiTelemetryGroupRowDto): string {
    if (this.keys() !== 'both') return '';
    return `system ${formatInteger(row.systemRequests)} · own key ${formatInteger(row.userKeyRequests)}`;
  }

  /** Under both key sources: `own key $0.12`; otherwise empty. */
  ownKeyCostNote(row: AiTelemetryGroupRowDto): string {
    if (this.keys() !== 'both') return '';
    return `own key ${formatCost(row.userKeyEstimatedCost)}`;
  }

  unpricedRequests(row: AiTelemetryGroupRowDto): number {
    return Math.max(0, row.requests - row.pricedRequests);
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

  /** The table row's key: a configId is unique only within its key source, and the own-key line has none. */
  configKey(config: AiTelemetryGroupConfigDto): string {
    return `${config.keySource}|${config.configId ?? 'own'}`;
  }

  /** A system configuration the group does not grant; never said of the users in no group, which grant nothing. */
  notGranted(row: AiTelemetryGroupRowDto, config: AiTelemetryGroupConfigDto): boolean {
    return !this.isNoGroup(row) && config.keySource === 'system' && !config.grantedByGroup;
  }

  /** `12 / 50` or `12 (no limit)` against the group's limits; a dash where the group grants nothing or on own keys. */
  configLimitText(config: AiTelemetryGroupConfigDto, count: number | null | undefined, max: number | null | undefined): string {
    if (config.keySource === 'user' || !config.grantedByGroup) return '—';
    const used = formatInteger(count ?? 0);
    return max ? `${used} / ${formatInteger(max)}` : `${used} (no limit)`;
  }

  // --- Actions ---

  openUsageByModel(row: AiTelemetryGroupRowDto, event: Event): void {
    this.usageByModel.emit({
      groupId: groupKeyOf(row),
      groupName: groupDisplayName(row),
      opener: (event.currentTarget as HTMLElement | null) ?? null
    });
  }

  openUsageCharts(row: AiTelemetryGroupRowDto): void {
    this.usageCharts.emit({ groupId: groupKeyOf(row), groupName: groupDisplayName(row) });
  }

  /** Opens AI configuration access for the group on the Users tab's Groups sub-tab. */
  manageLimits(row: AiTelemetryGroupRowDto): void {
    if (row.groupId == null) return;
    this.store.requestManageGroup(row.groupId, groupDisplayName(row));
  }

  private static errorText(err: unknown): string {
    const http = err as { error?: { message?: string } | string; message?: string };
    if (http?.error && typeof http.error === 'object' && http.error.message) return http.error.message;
    if (typeof http?.error === 'string' && http.error) return http.error;
    return http?.message || 'Unknown error';
  }
}
