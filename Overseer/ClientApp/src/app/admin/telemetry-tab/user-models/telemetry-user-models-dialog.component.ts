import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
  inject
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Subscription } from 'rxjs';

import {
  AdminService,
  AiTelemetryKeySource,
  AiTelemetryUserModelRowDto,
  AiTelemetryUserModelsDto,
  AiUsageRoleGroup
} from '../../../services/admin.service';
import { ModelPickerMatchPart, matchParts, searchTerms } from '../../../shared/model-picker/model-picker-view';
import { ProviderBadgeComponent } from '../../../shared/provider-badge/provider-badge.component';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { formatInteger, formatPercent } from '../../benchmark/shared/provider-availability/provider-availability';
import { telemetryRangeLabel } from '../charts/telemetry-charts';
import {
  BreakdownMetrics,
  BreakdownSortDirection,
  KEY_SOURCE_LABELS,
  TelemetryCardBadge,
  TelemetryMetricText,
  costMetricText,
  roleBadgeClass,
  roleGroupLabel,
  thinkingBadgesOf
} from '../telemetry-breakdown';
import {
  ALL_LEVELS,
  ModelUserShareField,
  ModelUserShares,
  SHARE_FIELD_LABELS,
  ThinkingLevelOption,
  UNKNOWN_USER_TEXT,
  modelUserShares
} from '../model-users/telemetry-model-users';
import {
  DEFAULT_USER_MODEL_SORT,
  TelemetryUserModelsView,
  USER_MODEL_SORT_FIELDS,
  UserModelCard,
  UserModelKeyFilter,
  UserModelKeyOption,
  UserModelRoleFilter,
  UserModelRoleOption,
  UserModelSort,
  UserModelSortField,
  buildUserModelCards,
  distinctModelCount,
  filterUserModelRows,
  isDefaultUserModelSort,
  keyOptions,
  readUserModelsView,
  roleOptions,
  searchUserModels,
  sortUserModelCards,
  sumUserModelCards,
  topUserModelText,
  userModelDirectionLabel,
  userModelLevelOptions,
  userModelRanks,
  userModelShareFieldOf,
  userModelSortField,
  userModelSortLabel,
  userModelsStatus,
  writeUserModelsView
} from './telemetry-user-models';

/** What the AI Telemetry tab opens the dialog with: one user card's scope. */
export interface TelemetryUserModelsRequest {
  /** The tab's range, "YYYY-MM-DD". */
  readonly start: string;
  readonly end: string;
  readonly keys: AiTelemetryKeySource;
  readonly userId: string;
  /** The card's user name; empty when the account is gone. */
  readonly userName: string;
}

/** Cards rendered at first, and added by *Show N more*. */
export const USER_MODELS_BATCH = 50;

/** The applied search follows the field after this much quiet, so the status line is not announced per keystroke. */
export const USER_MODELS_SEARCH_DEBOUNCE_MS = 200;

/** Everything the list shows, derived from the rows, the filters and the view; rebuilt only when one of them changes. */
interface UserModelsModel {
  roleOptions: UserModelRoleOption[];
  keyOptions: UserModelKeyOption[];
  levelOptions: ThinkingLevelOption[];
  /** The scope sorted: the role, key and level filters applied, before the search. */
  sorted: UserModelCard[];
  ranks: Map<string, number>;
  total: BreakdownMetrics;
  /** Distinct provider and model id pairs of the scope. */
  modelCount: number;
  /** The sorted scope narrowed by the search. */
  matching: UserModelCard[];
  matchingTotal: BreakdownMetrics;
}

/**
 * The *Usage by model* dialog of an AI Telemetry user card: every model the user used in the tab's
 * key sources over its range, one card per key source, model and role group, with their figures,
 * share of the total and rank. Self-contained: the tab calls `open()`.
 */
@Component({
  selector: 'app-telemetry-user-models-dialog',
  standalone: true,
  imports: [NgTemplateOutlet, ProviderBadgeComponent],
  templateUrl: './telemetry-user-models-dialog.component.html',
  styleUrls: ['./telemetry-user-models-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TelemetryUserModelsDialogComponent implements OnInit, OnDestroy {
  private readonly admin = inject(AdminService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly sortFields = USER_MODEL_SORT_FIELDS;
  readonly sortDirections: readonly BreakdownSortDirection[] = ['desc', 'asc'];
  readonly formatInteger = formatInteger;

  request: TelemetryUserModelsRequest | null = null;
  dto: AiTelemetryUserModelsDto | null = null;
  loading = false;
  error: string | null = null;

  /** Grouping and sort, remembered per browser. */
  view: TelemetryUserModelsView = readUserModelsView();
  /** The search field as typed. */
  searchInput = '';
  /** The search the list applies; follows `searchInput` after a short quiet. Never stored. */
  search = '';
  /** The Role, Key and Thinking level filters; never stored. */
  roleFilter: UserModelRoleFilter = 'all';
  keyFilter: UserModelKeyFilter = 'all';
  levelOption = ALL_LEVELS;
  visibleCount = USER_MODELS_BATCH;
  /** Whether the Sort popover is open; the popover polyfill does not set `aria-expanded`. */
  sortOpen = false;

  private opener: HTMLElement | null = null;
  private fetchSubscription: Subscription | null = null;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private isOpen = false;
  private modelMemo: {
    rows: readonly AiTelemetryUserModelRowDto[];
    role: UserModelRoleFilter;
    key: UserModelKeyFilter;
    level: string;
    separate: boolean;
    sort: UserModelSort;
    search: string;
    model: UserModelsModel;
  } | null = null;
  private matchMemo = new WeakMap<UserModelCard, { search: string; title: ModelPickerMatchPart[]; modelId: ModelPickerMatchPart[] }>();
  private badgeMemo = new WeakMap<UserModelCard, TelemetryCardBadge[]>();

  @ViewChild('dialog') private dialogRef?: ElementRef<HTMLDialogElement>;
  @ViewChild('title') private titleRef?: ElementRef<HTMLElement>;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.isOpen = false;
    this.cancelFetch();
    this.cancelSearchTimer();
    const dialog = this.dialogRef?.nativeElement;
    if (dialog?.open) dialog.close();
  }

  // --- Dialog ---

  /** Shows the dialog for the user card's scope, every filter cleared, and fetches the user's models. */
  open(request: TelemetryUserModelsRequest, opener?: HTMLElement): void {
    this.request = request;
    this.opener = opener ?? null;
    this.dto = null;
    this.error = null;
    this.cancelSearchTimer();
    this.searchInput = '';
    this.search = '';
    this.roleFilter = 'all';
    this.keyFilter = 'all';
    this.levelOption = ALL_LEVELS;
    this.visibleCount = USER_MODELS_BATCH;
    this.isOpen = true;
    const dialog = this.dialogRef?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
    this.fetch();
    this.cdr.detectChanges();
    this.titleRef?.nativeElement.focus();
    refreshAnchorPositioning();
  }

  close(): void {
    const dialog = this.dialogRef?.nativeElement;
    if (dialog?.open) dialog.close();
    this.cdr.markForCheck();
  }

  /** Escape: closes this dialog and nothing behind it. */
  onCancel(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    this.close();
  }

  /** The native close, however it came: stopped here; focus goes back to the button that opened it. */
  onDialogClose(event: Event): void {
    event.stopPropagation();
    if (!this.isOpen) {
      return;
    }
    this.isOpen = false;
    this.cancelFetch();
    this.cancelSearchTimer();
    this.sortOpen = false;
    this.cdr.markForCheck();
    const opener = this.opener;
    this.opener = null;
    if (opener?.isConnected) {
      opener.focus();
    }
  }

  retry(): void {
    this.fetch();
  }

  private fetch(): void {
    const request = this.request;
    if (!request) {
      return;
    }
    this.fetchSubscription?.unsubscribe();
    this.loading = true;
    this.error = null;
    this.cdr.markForCheck();
    this.fetchSubscription = this.admin.getAiTelemetryUserModels({
      ...(request.start ? { startDate: request.start } : {}),
      ...(request.end ? { endDate: request.end } : {}),
      keys: request.keys,
      userId: request.userId
    }).subscribe({
      next: dto => {
        this.loading = false;
        this.dto = dto;
        if (!roleOptions(dto.rows).some(option => option.value === this.roleFilter)) {
          this.roleFilter = 'all';
        }
        if (!keyOptions(dto.rows).some(option => option.value === this.keyFilter)) {
          this.keyFilter = 'all';
        }
        if (!userModelLevelOptions(dto.rows).some(option => option.value === this.levelOption)) {
          this.levelOption = ALL_LEVELS;
        }
        this.cdr.markForCheck();
      },
      error: (err: { error?: { message?: string }; message?: string }) => {
        this.loading = false;
        this.error = err?.error?.message || err?.message || 'The models could not be loaded.';
        this.cdr.markForCheck();
      }
    });
  }

  private cancelFetch(): void {
    this.fetchSubscription?.unsubscribe();
    this.fetchSubscription = null;
    this.loading = false;
  }

  // --- Header ---

  /** The user's name from the card, else from the response; *Unknown user* when the account is gone. */
  get userName(): string {
    return this.request?.userName?.trim() || this.dto?.userName?.trim() || UNKNOWN_USER_TEXT;
  }

  get dialogTitle(): string {
    return `Usage by model: ${this.userName}`;
  }

  /** `All keys · 2026-09-20 to 2026-10-20`. */
  get subtitle(): string {
    const request = this.request;
    if (!request) {
      return '';
    }
    return `${KEY_SOURCE_LABELS[request.keys]} · ${this.rangeLabel}`;
  }

  get rangeLabel(): string {
    return telemetryRangeLabel(this.request?.start, this.request?.end);
  }

  /** Both key sources are in the scope, so each card says whose key it ran on. */
  get bothKeys(): boolean {
    return this.request?.keys === 'both';
  }

  /** The scope includes system AI configurations: the tab's keys, narrowed by the Key filter. */
  get systemScope(): boolean {
    return this.request?.keys !== 'user' && this.keyFilter !== 'user';
  }

  /** The cost recording began after the range's start. */
  get costRecordedLate(): boolean {
    const since = this.utcDay(this.dto?.costRecordedSinceUtc);
    return !!since && !!this.request?.start && this.request.start < since;
  }

  utcDay(value: string | null | undefined): string {
    if (!value) {
      return '';
    }
    const date = parseServerUtcDate(value);
    return isNaN(date.getTime()) ? value : date.toISOString().slice(0, 10);
  }

  /** `2026-10-12 21:15`, in UTC. */
  timeText(value: string | null | undefined): string {
    if (!value) {
      return '';
    }
    const date = parseServerUtcDate(value);
    if (isNaN(date.getTime())) {
      return value;
    }
    return `${date.toISOString().slice(0, 10)} ${date.toISOString().slice(11, 16)}`;
  }

  // --- The list ---

  get model(): UserModelsModel {
    const rows = this.dto?.rows ?? [];
    const { separateThinkingLevels: separate, sort } = this.view;
    const memo = this.modelMemo;
    const sameScope = !!memo && memo.rows === rows && memo.role === this.roleFilter && memo.key === this.keyFilter
      && memo.level === this.levelOption && memo.separate === separate && memo.sort === sort;
    if (memo && sameScope && memo.search === this.search) {
      return memo.model;
    }
    let base: Omit<UserModelsModel, 'matching' | 'matchingTotal'>;
    if (memo && sameScope) {
      base = memo.model;
    } else {
      const filtered = filterUserModelRows(rows, { role: this.roleFilter, key: this.keyFilter, level: this.levelOption });
      const cards = buildUserModelCards(filtered, { separateThinkingLevels: separate });
      const sorted = sortUserModelCards(cards, sort);
      base = {
        roleOptions: roleOptions(rows),
        keyOptions: keyOptions(rows),
        levelOptions: userModelLevelOptions(rows),
        sorted,
        ranks: userModelRanks(sorted),
        total: sumUserModelCards(sorted),
        modelCount: distinctModelCount(sorted)
      };
    }
    const matching = searchUserModels(base.sorted, this.search);
    const model: UserModelsModel = {
      ...base,
      matching,
      matchingTotal: matching.length === base.sorted.length ? base.total : sumUserModelCards(matching)
    };
    this.modelMemo = {
      rows, role: this.roleFilter, key: this.keyFilter, level: this.levelOption, separate, sort, search: this.search, model
    };
    return model;
  }

  get hasRows(): boolean {
    return (this.dto?.rows.length ?? 0) > 0;
  }

  /** The cards the batch renders. */
  get visibleCards(): UserModelCard[] {
    return this.model.matching.slice(0, this.visibleCount);
  }

  get remaining(): number {
    return Math.max(0, this.model.matching.length - this.visibleCount);
  }

  get nextBatch(): number {
    return Math.min(USER_MODELS_BATCH, this.remaining);
  }

  /** The Role filter is offered while the rows hold two or more role groups. */
  get showRoleFilter(): boolean {
    return this.model.roleOptions.length > 2;
  }

  /** The Key filter is offered under *Both* only. */
  get showKeyFilter(): boolean {
    return this.bothKeys;
  }

  /** The Thinking level filter is offered while the rows hold two or more level values. */
  get showLevelFilter(): boolean {
    return this.model.levelOptions.length > 2;
  }

  get filtersActive(): boolean {
    return this.searchInput.trim() !== '' || this.search.trim() !== '' || this.roleFilter !== 'all'
      || this.keyFilter !== 'all' || this.levelOption !== ALL_LEVELS;
  }

  get status(): string {
    if (!this.dto) {
      return '';
    }
    const model = this.model;
    return userModelsStatus({
      scope: model.sorted.length,
      matching: model.matching.length,
      shown: Math.min(this.visibleCount, model.matching.length),
      search: this.search,
      requests: model.total.requests
    });
  }

  /** The figure the share bars and *Top model* are of. */
  get shareField(): ModelUserShareField {
    return userModelShareFieldOf(this.view.sort);
  }

  get shareFieldLabel(): string {
    return SHARE_FIELD_LABELS[this.shareField];
  }

  /** `Claude 5 Opus · Benchmark · 41% of est. cost`; rank 1 of the scope. */
  get topModelText(): string {
    return topUserModelText(this.model.sorted, this.model.total, this.shareField);
  }

  get totalTokens(): number {
    const total = this.model.total;
    return total.promptTokens + total.outputTokens;
  }

  rankOf(card: UserModelCard): number {
    return this.model.ranks.get(card.key) ?? 0;
  }

  sharesOf(card: UserModelCard): ModelUserShares {
    return modelUserShares(card, this.model.total);
  }

  shareText(share: number | null): string {
    return share === null ? '—' : formatPercent(share);
  }

  /** The bar's width, 0–100. */
  shareWidth(share: number | null): number {
    return share === null ? 0 : Math.min(100, Math.max(0, share * 100));
  }

  /** The model id is shown beside the title only when the title is a name. */
  showModelId(card: UserModelCard): boolean {
    return card.title !== card.modelId;
  }

  titleParts(card: UserModelCard): ModelPickerMatchPart[] {
    return this.matchesOf(card).title;
  }

  modelIdParts(card: UserModelCard): ModelPickerMatchPart[] {
    return this.matchesOf(card).modelId;
  }

  private matchesOf(card: UserModelCard): { title: ModelPickerMatchPart[]; modelId: ModelPickerMatchPart[] } {
    let memo = this.matchMemo.get(card);
    if (!memo || memo.search !== this.search) {
      const terms = searchTerms(this.search);
      memo = { search: this.search, title: matchParts(card.title, terms), modelId: matchParts(card.modelId, terms) };
      this.matchMemo.set(card, memo);
    }
    return memo;
  }

  roleLabel(group: AiUsageRoleGroup): string {
    return roleGroupLabel(group);
  }

  roleBadgeClass(group: AiUsageRoleGroup): string {
    return roleBadgeClass(group);
  }

  thinkingBadges(card: UserModelCard): TelemetryCardBadge[] {
    let badges = this.badgeMemo.get(card);
    if (!badges) {
      badges = thinkingBadgesOf(card);
      this.badgeMemo.set(card, badges);
    }
    return badges;
  }

  /** `System key` or `Own key` under *Both*; none otherwise, where the subtitle already says whose keys. */
  keyBadgeText(card: UserModelCard): string | null {
    if (!this.bothKeys) {
      return null;
    }
    return card.keySource === 'user' ? 'Own key' : 'System key';
  }

  costMetric(m: BreakdownMetrics): TelemetryMetricText {
    return costMetricText(m);
  }

  cacheHitText(m: BreakdownMetrics): string {
    return m.cacheHitRatio != null ? formatPercent(m.cacheHitRatio) : '—';
  }

  durationText(m: BreakdownMetrics): string {
    return m.avgDurationMs != null ? `${formatInteger(Math.round(m.avgDurationMs))} ms` : '—';
  }

  /** Renders the next batch and focuses its first card's title. */
  showMore(all = false): void {
    const first = this.visibleCount;
    this.visibleCount = all ? this.model.matching.length : this.visibleCount + USER_MODELS_BATCH;
    this.cdr.detectChanges();
    this.host.nativeElement.querySelector<HTMLElement>(`#tl-um-${first}-title`)?.focus();
  }

  // --- Search and filters ---

  onSearchInput(event: Event): void {
    this.searchInput = (event.target as HTMLInputElement).value;
    this.cancelSearchTimer();
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.applySearch(this.searchInput);
      this.cdr.markForCheck();
    }, USER_MODELS_SEARCH_DEBOUNCE_MS);
  }

  /** Escape with text clears the search; the dialog's own close is Escape's default action, so it is prevented. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape' || this.searchInput === '') {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.cancelSearchTimer();
    this.searchInput = '';
    this.applySearch('');
  }

  onRoleChange(event: Event): void {
    this.roleFilter = (event.target as HTMLSelectElement).value as UserModelRoleFilter;
    this.visibleCount = USER_MODELS_BATCH;
  }

  onKeyChange(event: Event): void {
    this.keyFilter = (event.target as HTMLSelectElement).value as UserModelKeyFilter;
    this.visibleCount = USER_MODELS_BATCH;
  }

  onLevelChange(event: Event): void {
    this.levelOption = (event.target as HTMLSelectElement).value;
    this.visibleCount = USER_MODELS_BATCH;
  }

  onThinkingChange(event: Event): void {
    const separate = (event.target as HTMLSelectElement).value === 'separate';
    if (separate !== this.view.separateThinkingLevels) {
      this.saveView({ ...this.view, separateThinkingLevels: separate });
    }
  }

  /** Clears the search and the role, key and level filters, not the grouping or the sort, and focuses the search field. */
  clearFilters(): void {
    this.cancelSearchTimer();
    this.searchInput = '';
    this.applySearch('');
    this.roleFilter = 'all';
    this.keyFilter = 'all';
    this.levelOption = ALL_LEVELS;
    this.visibleCount = USER_MODELS_BATCH;
    this.host.nativeElement.querySelector<HTMLInputElement>('#tl-um-search')?.focus();
  }

  private applySearch(search: string): void {
    if (search !== this.search) {
      this.search = search;
      this.visibleCount = USER_MODELS_BATCH;
    }
  }

  private cancelSearchTimer(): void {
    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }
  }

  // --- Sort ---

  get sortLabel(): string {
    return userModelSortLabel(this.view.sort);
  }

  get sortIsDefault(): boolean {
    return isDefaultUserModelSort(this.view.sort);
  }

  directionLabel(direction: BreakdownSortDirection): string {
    return userModelDirectionLabel(userModelSortField(this.view.sort.field).kind, direction);
  }

  /** Applies at once and keeps the direction; the popover stays open. */
  chooseSortField(field: UserModelSortField): void {
    if (field !== this.view.sort.field) {
      this.saveView({ ...this.view, sort: { field, direction: this.view.sort.direction } });
    }
  }

  chooseSortDirection(direction: BreakdownSortDirection): void {
    if (direction !== this.view.sort.direction) {
      this.saveView({ ...this.view, sort: { field: this.view.sort.field, direction } });
    }
  }

  /** Back to cost, highest first; refused at the default (the button is aria-disabled), and the button keeps focus. */
  resetSort(): void {
    if (!this.sortIsDefault) {
      this.saveView({ ...this.view, sort: DEFAULT_USER_MODEL_SORT });
    }
  }

  onSortToggle(event: Event): void {
    this.sortOpen = (event as ToggleEvent).newState === 'open';
    this.cdr.markForCheck();
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
    if (event.key !== 'Escape') {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    try {
      this.sortPanel()?.hidePopover();
    } catch {
      // Already hidden.
    }
    this.sortTrigger()?.focus();
  }

  private sortPanel(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>('#tl-um-sort-panel');
  }

  private sortTrigger(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>('#tl-um-sort-trigger');
  }

  /** Every grouping or sort change starts the batch again. */
  private saveView(view: TelemetryUserModelsView): void {
    this.view = view;
    this.visibleCount = USER_MODELS_BATCH;
    writeUserModelsView(view);
  }
}
