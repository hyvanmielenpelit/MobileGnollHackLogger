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

import { AdminService, AiTelemetryModelUserRowDto, AiTelemetryModelUsersDto, AiUsageRoleGroup } from '../../../services/admin.service';
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
  roleGroupLabel,
  thinkingBadgesOf
} from '../telemetry-breakdown';
import {
  ALL_LEVELS,
  MODEL_USER_SORT_FIELDS,
  ModelUserCard,
  ModelUserShareField,
  ModelUserShares,
  ModelUserSort,
  ModelUserSortField,
  SHARE_FIELD_LABELS,
  TelemetryModelUsersView,
  ThinkingLevelOption,
  UNKNOWN_USER_TEXT,
  buildModelUserCards,
  filterByThinkingLevel,
  isDefaultModelUserSort,
  modelUserDirectionLabel,
  modelUserRanks,
  modelUserShares,
  modelUserSortField,
  modelUserSortLabel,
  modelUsersStatus,
  readModelUsersView,
  searchModelUsers,
  shareFieldOf,
  sortModelUserCards,
  sumModelUserCards,
  thinkingLevelOptionOf,
  thinkingLevelOptions,
  writeModelUsersView,
  DEFAULT_MODEL_USER_SORT
} from './telemetry-model-users';

/** What the AI Telemetry tab opens the dialog with: one model card's scope. */
export interface TelemetryModelUsersRequest {
  /** The tab's range, "YYYY-MM-DD". */
  readonly start: string;
  readonly end: string;
  readonly keySource: 'system' | 'user';
  readonly provider: string;
  readonly modelId: string;
  /** The card's title: the model's name, else its id. */
  readonly modelTitle: string;
  readonly roleGroup: AiUsageRoleGroup;
  /** The card's level under *Show separately* ("" none sent, null not recorded); undefined while grouped. */
  readonly thinkingLevel: string | null | undefined;
}

/** Cards rendered at first, and added by *Show N more*. */
export const MODEL_USERS_BATCH = 50;

/** The applied search follows the field after this much quiet, so the status line is not announced per keystroke. */
export const MODEL_USERS_SEARCH_DEBOUNCE_MS = 200;

/** Everything the list shows, derived from the rows and the view; rebuilt only when one of them changes. */
interface ModelUsersModel {
  levelOptions: ThinkingLevelOption[];
  /** The scope sorted: the level filter applied, before the search. */
  sorted: ModelUserCard[];
  ranks: Map<string, number>;
  total: BreakdownMetrics;
  /** The sorted scope narrowed by the search. */
  matching: ModelUserCard[];
  matchingTotal: BreakdownMetrics;
}

/**
 * The *Usage by user* dialog of an AI Telemetry model card: every user of the card's model, role
 * group and key source over the tab's range, one card each, with their figures, share of the total
 * and rank. Self-contained: the tab calls `open()`.
 */
@Component({
  selector: 'app-telemetry-model-users-dialog',
  standalone: true,
  imports: [NgTemplateOutlet, ProviderBadgeComponent],
  templateUrl: './telemetry-model-users-dialog.component.html',
  styleUrls: ['./telemetry-model-users-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TelemetryModelUsersDialogComponent implements OnInit, OnDestroy {
  private readonly admin = inject(AdminService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly sortFields = MODEL_USER_SORT_FIELDS;
  readonly sortDirections: readonly BreakdownSortDirection[] = ['desc', 'asc'];
  readonly unknownUserText = UNKNOWN_USER_TEXT;
  readonly formatInteger = formatInteger;

  request: TelemetryModelUsersRequest | null = null;
  dto: AiTelemetryModelUsersDto | null = null;
  loading = false;
  error: string | null = null;

  /** Grouping and sort, remembered per browser. */
  view: TelemetryModelUsersView = readModelUsersView();
  /** The search field as typed. */
  searchInput = '';
  /** The search the list applies; follows `searchInput` after a short quiet. Never stored. */
  search = '';
  /** The Thinking level filter's option; never stored. */
  levelOption = ALL_LEVELS;
  visibleCount = MODEL_USERS_BATCH;
  /** Whether the Sort popover is open; the popover polyfill does not set `aria-expanded`. */
  sortOpen = false;

  private opener: HTMLElement | null = null;
  private fetchSubscription: Subscription | null = null;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private isOpen = false;
  private modelMemo: {
    rows: readonly AiTelemetryModelUserRowDto[];
    level: string;
    separate: boolean;
    sort: ModelUserSort;
    search: string;
    model: ModelUsersModel;
  } | null = null;
  private matchMemo = new WeakMap<ModelUserCard, { search: string; parts: ModelPickerMatchPart[] }>();
  private badgeMemo = new WeakMap<ModelUserCard, TelemetryCardBadge[]>();

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

  /** Shows the dialog for the card's scope, preset to its thinking level, and fetches its users. */
  open(request: TelemetryModelUsersRequest, opener?: HTMLElement): void {
    this.request = request;
    this.opener = opener ?? null;
    this.dto = null;
    this.error = null;
    this.cancelSearchTimer();
    this.searchInput = '';
    this.search = '';
    this.levelOption = thinkingLevelOptionOf(request.thinkingLevel);
    this.visibleCount = MODEL_USERS_BATCH;
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
    this.fetchSubscription = this.admin.getAiTelemetryModelUsers({
      ...(request.start ? { startDate: request.start } : {}),
      ...(request.end ? { endDate: request.end } : {}),
      keys: request.keySource,
      provider: request.provider,
      modelId: request.modelId,
      roles: [request.roleGroup]
    }).subscribe({
      next: dto => {
        this.loading = false;
        this.dto = dto;
        if (!thinkingLevelOptions(dto.rows).some(option => option.value === this.levelOption)) {
          this.levelOption = ALL_LEVELS;
        }
        this.cdr.markForCheck();
      },
      error: (err: { error?: { message?: string }; message?: string }) => {
        this.loading = false;
        this.error = err?.error?.message || err?.message || 'The users could not be loaded.';
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

  get dialogTitle(): string {
    return `Usage by user: ${this.request?.modelTitle ?? ''}`;
  }

  /** `Chat · System AI configurations · 2026-09-20 to 2026-10-20`. */
  get subtitle(): string {
    const request = this.request;
    if (!request) {
      return '';
    }
    return `${roleGroupLabel(request.roleGroup)} · ${KEY_SOURCE_LABELS[request.keySource]} · ${this.rangeLabel}`;
  }

  get rangeLabel(): string {
    return telemetryRangeLabel(this.request?.start, this.request?.end);
  }

  /** The model id is shown beside the title only when the title is a name. */
  get showModelId(): boolean {
    return !!this.request && this.request.modelTitle !== this.request.modelId;
  }

  get systemScope(): boolean {
    return this.request?.keySource !== 'user';
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

  get model(): ModelUsersModel {
    const rows = this.dto?.rows ?? [];
    const { separateThinkingLevels: separate, sort } = this.view;
    const memo = this.modelMemo;
    if (memo && memo.rows === rows && memo.level === this.levelOption && memo.separate === separate
      && memo.sort === sort && memo.search === this.search) {
      return memo.model;
    }
    const sameScope = memo && memo.rows === rows && memo.level === this.levelOption && memo.separate === separate && memo.sort === sort;
    let base: Omit<ModelUsersModel, 'matching' | 'matchingTotal'>;
    if (sameScope) {
      base = memo.model;
    } else {
      const cards = buildModelUserCards(filterByThinkingLevel(rows, this.levelOption), { separateThinkingLevels: separate });
      const sorted = sortModelUserCards(cards, sort);
      base = { levelOptions: thinkingLevelOptions(rows), sorted, ranks: modelUserRanks(sorted), total: sumModelUserCards(sorted) };
    }
    const matching = searchModelUsers(base.sorted, this.search);
    const model: ModelUsersModel = {
      ...base,
      matching,
      matchingTotal: matching.length === base.sorted.length ? base.total : sumModelUserCards(matching)
    };
    this.modelMemo = { rows, level: this.levelOption, separate, sort, search: this.search, model };
    return model;
  }

  get hasRows(): boolean {
    return (this.dto?.rows.length ?? 0) > 0;
  }

  /** The cards the batch renders. */
  get visibleCards(): ModelUserCard[] {
    return this.model.matching.slice(0, this.visibleCount);
  }

  get remaining(): number {
    return Math.max(0, this.model.matching.length - this.visibleCount);
  }

  get nextBatch(): number {
    return Math.min(MODEL_USERS_BATCH, this.remaining);
  }

  /** The Thinking level filter is offered while the rows hold two or more level values. */
  get showLevelFilter(): boolean {
    return this.model.levelOptions.length > 2;
  }

  get filtersActive(): boolean {
    return this.searchInput.trim() !== '' || this.search.trim() !== '' || this.levelOption !== ALL_LEVELS;
  }

  get status(): string {
    if (!this.dto) {
      return '';
    }
    const model = this.model;
    return modelUsersStatus({
      scope: model.sorted.length,
      matching: model.matching.length,
      shown: Math.min(this.visibleCount, model.matching.length),
      search: this.search,
      requests: model.total.requests
    });
  }

  /** The figure the share bars and *Top user* are of. */
  get shareField(): ModelUserShareField {
    return shareFieldOf(this.view.sort);
  }

  get shareFieldLabel(): string {
    return SHARE_FIELD_LABELS[this.shareField];
  }

  /** `alice · 41% of est. cost`; rank 1 of the scope. */
  get topUserText(): string {
    const top = this.model.sorted[0];
    if (!top) {
      return '—';
    }
    const name = top.userName ?? UNKNOWN_USER_TEXT;
    const share = modelUserShares(top, this.model.total)[this.shareField];
    return share === null ? name : `${name} · ${formatPercent(share)} of ${this.shareFieldLabel}`;
  }

  get totalTokens(): number {
    const total = this.model.total;
    return total.promptTokens + total.outputTokens;
  }

  rankOf(card: ModelUserCard): number {
    return this.model.ranks.get(card.key) ?? 0;
  }

  sharesOf(card: ModelUserCard): ModelUserShares {
    return modelUserShares(card, this.model.total);
  }

  shareText(share: number | null): string {
    return share === null ? '—' : formatPercent(share);
  }

  /** The bar's width, 0–100. */
  shareWidth(share: number | null): number {
    return share === null ? 0 : Math.min(100, Math.max(0, share * 100));
  }

  nameParts(card: ModelUserCard): ModelPickerMatchPart[] {
    let memo = this.matchMemo.get(card);
    if (!memo || memo.search !== this.search) {
      memo = { search: this.search, parts: matchParts(card.userName ?? '', searchTerms(this.search)) };
      this.matchMemo.set(card, memo);
    }
    return memo.parts;
  }

  thinkingBadges(card: ModelUserCard): TelemetryCardBadge[] {
    let badges = this.badgeMemo.get(card);
    if (!badges) {
      badges = thinkingBadgesOf(card);
      this.badgeMemo.set(card, badges);
    }
    return badges;
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
    this.visibleCount = all ? this.model.matching.length : this.visibleCount + MODEL_USERS_BATCH;
    this.cdr.detectChanges();
    this.host.nativeElement.querySelector<HTMLElement>(`#tl-mu-${first}-title`)?.focus();
  }

  // --- Search and filters ---

  onSearchInput(event: Event): void {
    this.searchInput = (event.target as HTMLInputElement).value;
    this.cancelSearchTimer();
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.applySearch(this.searchInput);
      this.cdr.markForCheck();
    }, MODEL_USERS_SEARCH_DEBOUNCE_MS);
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

  onLevelChange(event: Event): void {
    this.levelOption = (event.target as HTMLSelectElement).value;
    this.visibleCount = MODEL_USERS_BATCH;
  }

  onThinkingChange(event: Event): void {
    const separate = (event.target as HTMLSelectElement).value === 'separate';
    if (separate !== this.view.separateThinkingLevels) {
      this.saveView({ ...this.view, separateThinkingLevels: separate });
    }
  }

  /** Clears the search and the level filter, not the grouping or the sort, and focuses the search field. */
  clearFilters(): void {
    this.cancelSearchTimer();
    this.searchInput = '';
    this.applySearch('');
    this.levelOption = ALL_LEVELS;
    this.host.nativeElement.querySelector<HTMLInputElement>('#tl-mu-search')?.focus();
  }

  private applySearch(search: string): void {
    if (search !== this.search) {
      this.search = search;
      this.visibleCount = MODEL_USERS_BATCH;
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
    return modelUserSortLabel(this.view.sort);
  }

  get sortIsDefault(): boolean {
    return isDefaultModelUserSort(this.view.sort);
  }

  directionLabel(direction: BreakdownSortDirection): string {
    return modelUserDirectionLabel(modelUserSortField(this.view.sort.field).kind, direction);
  }

  /** Applies at once and keeps the direction; the popover stays open. */
  chooseSortField(field: ModelUserSortField): void {
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
      this.saveView({ ...this.view, sort: DEFAULT_MODEL_USER_SORT });
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
    return this.host.nativeElement.querySelector<HTMLElement>('#tl-mu-sort-panel');
  }

  private sortTrigger(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>('#tl-mu-sort-trigger');
  }

  /** Every grouping or sort change starts the batch again. */
  private saveView(view: TelemetryModelUsersView): void {
    this.view = view;
    this.visibleCount = MODEL_USERS_BATCH;
    writeModelUsersView(view);
  }
}
