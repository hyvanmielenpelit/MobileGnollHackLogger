import { AfterViewInit, Component, ChangeDetectionStrategy, ElementRef, OnInit, OnDestroy, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Observable, Subscription, forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  AdminService,
  AiTelemetrySummaryDto,
  AiGovernorStatusDto,
  AiTelemetryKeySource,
  AiUsageBreakdownRowDto,
  AiUsageRoleGroup
} from '../../services/admin.service';
import { ProviderBadgeComponent } from '../../shared/provider-badge/provider-badge.component';
import {
  ModelPickerMatchPart,
  ModelPickerProviderFacet,
  matchParts,
  searchTerms
} from '../../shared/model-picker/model-picker-view';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';
import { formatCost } from '../benchmark/batteries/battery.models';
import {
  FEW_ATTEMPTS_THRESHOLD,
  NOT_RECORDED_TEXT,
  formatCount,
  formatInteger,
  formatInterval,
  formatOneIn,
  formatPercent
} from '../benchmark/shared/provider-availability/provider-availability';
import { AdminPageStore, TelemetryTimeSpan } from '../admin-page.store';
import { TelemetryChartsDialogComponent } from './charts/telemetry-charts-dialog.component';
import { TelemetryModelUsersDialogComponent } from './model-users/telemetry-model-users-dialog.component';
import {
  BREAKDOWN_SORT_FIELDS,
  Breakdown,
  BreakdownCard,
  BreakdownMetrics,
  BreakdownSort,
  BreakdownSortDirection,
  BreakdownSortField,
  DEFAULT_BREAKDOWN_SORT,
  KEY_SOURCES,
  KEY_SOURCE_LABELS,
  NAMED_ROLE_GROUPS,
  TelemetryBreakdownView,
  TelemetryCardBadge,
  TelemetryMetricText,
  breakdownDirectionLabel,
  breakdownProviderFacets,
  breakdownSortField,
  breakdownSortLabel,
  buildBreakdown,
  cardTitle,
  costMetricText,
  effectiveBreakdownProviders,
  isDefaultBreakdownSort,
  readBreakdownView,
  roleCosts,
  roleCounts,
  roleGroupLabel,
  thinkingBadgesOf,
  thinkingLevelLabel,
  writeBreakdownView
} from './telemetry-breakdown';
import { TelemetryUserRef, TelemetryUsersComponent } from './users/telemetry-users.component';

/** The Date Range select's options, also the collapsed bar's summary. */
export const TELEMETRY_TIME_SPAN_LABELS: Readonly<Record<TelemetryTimeSpan, string>> = {
  '7d': 'Last 7 days',
  '28d': 'Last 28 days',
  '30d': 'Last 30 days',
  '90d': 'Last 90 days',
  '180d': 'Last 180 days',
  '1y': 'Last year',
  'custom': 'Custom date range'
};

const TIME_SPANS = Object.keys(TELEMETRY_TIME_SPAN_LABELS) as TelemetryTimeSpan[];

const ROLE_BADGE_CLASSES: Readonly<Record<AiUsageRoleGroup, string>> = {
  chat: 'badge-role-chat',
  title: 'badge-role-title',
  benchmark: 'badge-role-benchmark',
  other: 'badge-role-other'
};

/** The applied search follows the field after this much quiet, so the status line is not announced per keystroke. */
const SEARCH_DEBOUNCE_MS = 200;

/** A labeled line of a summary card. */
export interface TelemetrySummaryLine {
  label: string;
  value: string;
  note?: string;
}

const NO_ROWS: readonly AiUsageBreakdownRowDto[] = [];

/**
 * The Admin page's AI Telemetry tab: usage, cost, provider availability and Overseer activity for a
 * date range and key source, the request governor's partitions, a breakdown by model and role, and
 * the usage of system AI configurations by user.
 */
@Component({
  selector: 'app-admin-telemetry-tab',
  imports: [CommonModule, FormsModule, ProviderBadgeComponent, TelemetryUsersComponent, TelemetryChartsDialogComponent, TelemetryModelUsersDialogComponent],
  templateUrl: './admin-telemetry-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-telemetry-tab.component.scss'
})
export class AdminTelemetryTabComponent implements OnInit, AfterViewInit, OnDestroy {
  private adminService = inject(AdminService);
  private store = inject(AdminPageStore);
  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  @ViewChild('chartsDialog') chartsDialog?: TelemetryChartsDialogComponent;
  @ViewChild('modelUsersDialog') modelUsersDialog?: TelemetryModelUsersDialogComponent;
  @ViewChild(TelemetryUsersComponent, { static: true }) usersView?: TelemetryUsersComponent;
  @ViewChild('tlHead') private tlHead?: ElementRef<HTMLElement>;

  readonly keySources = KEY_SOURCES;
  readonly keySourceLabels = KEY_SOURCE_LABELS;
  readonly timeSpans = TIME_SPANS;
  readonly timeSpanLabels = TELEMETRY_TIME_SPAN_LABELS;
  readonly sortFields = BREAKDOWN_SORT_FIELDS;
  readonly sortDirections: readonly BreakdownSortDirection[] = ['desc', 'asc'];
  readonly roleGroupLabel = roleGroupLabel;
  readonly thinkingLevelLabel = thinkingLevelLabel;
  readonly cardTitle = cardTitle;
  readonly formatInteger = formatInteger;
  readonly formatCost = formatCost;
  readonly notRecordedText = NOT_RECORDED_TEXT;

  telemetrySummary: AiTelemetrySummaryDto | null = null;
  governorStatus: AiGovernorStatusDto | null = null;
  telemetryLoading = false;
  governorLoading = false;
  todayDate: string = '';

  /** The Keys filter and the breakdown's view settings, remembered per browser. */
  view: TelemetryBreakdownView = readBreakdownView();

  /** The breakdown's search field as typed. */
  searchInput = '';
  /** The search the breakdown applies; follows `searchInput` after a short quiet. Never stored. */
  search = '';
  /** Whether the Sort popover is open; the popover polyfill does not set `aria-expanded`. */
  sortOpen = false;

  private governorCountdownTimer?: ReturnType<typeof setInterval>;
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private headObserver?: ResizeObserver;
  private telemetryUserSub?: Subscription;
  private breakdownMemo: {
    rows: readonly AiUsageBreakdownRowDto[];
    roles: readonly AiUsageRoleGroup[];
    separate: boolean;
    search: string;
    providers: readonly string[];
    sort: BreakdownSort;
    breakdown: Breakdown;
  } | null = null;
  private facetsMemo: { all: readonly BreakdownCard[]; facets: ModelPickerProviderFacet[] } | null = null;
  private roleCountsMemo: { rows: readonly AiUsageBreakdownRowDto[]; counts: Record<AiUsageRoleGroup, number> } | null = null;
  private matchMemo = new WeakMap<BreakdownCard, { search: string; title: ModelPickerMatchPart[]; modelId: ModelPickerMatchPart[] }>();
  private badgeMemo = new WeakMap<BreakdownCard, TelemetryCardBadge[]>();

  /** The date range lives in the page store, so it survives a tab switch. */
  get telemetryTimeSpan(): TelemetryTimeSpan {
    return this.store.telemetryRange.timeSpan;
  }
  set telemetryTimeSpan(value: TelemetryTimeSpan) {
    this.store.telemetryRange.timeSpan = value;
  }

  get telemetryStartDate(): string {
    return this.store.telemetryRange.start;
  }
  set telemetryStartDate(value: string) {
    this.store.telemetryRange.start = value;
  }

  get telemetryEndDate(): string {
    return this.store.telemetryRange.end;
  }
  set telemetryEndDate(value: string) {
    this.store.telemetryRange.end = value;
  }

  get keys(): AiTelemetryKeySource {
    return this.view.keys;
  }

  ngOnInit() {
    ensureOverlayPolyfills();
    this.todayDate = this.formatLocalDate(new Date());
    if (!this.telemetryStartDate && !this.telemetryEndDate) {
      this.applyTelemetryPreset(this.telemetryTimeSpan, false);
    }
    this.takePendingTelemetryUser();
    this.telemetryUserSub = this.store.telemetryUserRequested$.subscribe(() => {
      if (this.takePendingTelemetryUser()) {
        document.getElementById('tl-users-heading')?.focus();
      }
    });
    this.refreshTelemetryTab();
  }

  /** Focused or scrolled-to elements stop below the sticky head: its height, plus a gap, as `--tl-sticky-clearance`. */
  ngAfterViewInit() {
    const head = this.tlHead?.nativeElement;
    if (!head || typeof ResizeObserver === 'undefined') {
      return;
    }
    this.headObserver = new ResizeObserver(() => {
      const height = head.getBoundingClientRect().height;
      this.host.nativeElement.style.setProperty('--tl-sticky-clearance', `${Math.ceil(height) + 8}px`);
    });
    this.headObserver.observe(head);
  }

  ngOnDestroy() {
    this.stopGovernorCountdown();
    this.telemetryUserSub?.unsubscribe();
    this.headObserver?.disconnect();
    this.cancelSearchTimer();
  }

  formatLocalDate(d: Date): string {
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  applyTelemetryPreset(preset: '7d' | '28d' | '30d' | '90d' | '180d' | '1y' | 'custom', fetch = true) {
    if (preset !== 'custom') {
      const end = new Date();
      const start = new Date();
      if (preset === '7d') start.setDate(end.getDate() - 7);
      else if (preset === '28d') start.setDate(end.getDate() - 28);
      else if (preset === '30d') start.setDate(end.getDate() - 30);
      else if (preset === '90d') start.setDate(end.getDate() - 90);
      else if (preset === '180d') start.setDate(end.getDate() - 180);
      else if (preset === '1y') start.setFullYear(end.getFullYear() - 1);

      this.telemetryStartDate = this.formatLocalDate(start);
      this.telemetryEndDate = this.formatLocalDate(end);
    } else {
      if (!this.telemetryStartDate || !this.telemetryEndDate) {
        const end = new Date();
        const start = new Date();
        start.setDate(end.getDate() - 30);
        this.telemetryStartDate = this.formatLocalDate(start);
        this.telemetryEndDate = this.formatLocalDate(end);
      }
    }
    if (fetch) {
      this.loadTelemetry();
    }
  }

  onTelemetryTimeSpanChange() {
    this.applyTelemetryPreset(this.telemetryTimeSpan, true);
  }

  onCustomDateChange() {
    if (this.telemetryStartDate && this.telemetryEndDate) {
      if (new Date(this.telemetryEndDate) < new Date(this.telemetryStartDate)) {
        this.telemetryEndDate = this.telemetryStartDate;
      }
    }
    this.loadTelemetry();
  }

  /** Stores the Keys filter and reloads the summary. */
  onKeysChange(keys: AiTelemetryKeySource) {
    if (!KEY_SOURCES.includes(keys) || keys === this.view.keys) {
      return;
    }
    this.saveView({ ...this.view, keys });
    this.loadTelemetry();
  }

  /** The collapsed bar's line: `Last 30 days · System AI configurations`, or the custom range's two dates. */
  get toolbarSummary(): string {
    const span = this.telemetryTimeSpan === 'custom'
      ? `${this.telemetryStartDate} – ${this.telemetryEndDate}`
      : TELEMETRY_TIME_SPAN_LABELS[this.telemetryTimeSpan] ?? this.telemetryTimeSpan;
    return `${span} · ${KEY_SOURCE_LABELS[this.keys]}`;
  }

  /** Collapses the Date Range and Keys bar to its summary line, or expands it; stored. The toggle stays, and so does focus. */
  toggleToolbar() {
    this.saveView({ ...this.view, toolbarCollapsed: !this.view.toolbarCollapsed });
  }

  loadTelemetry(showFeedback = false) {
    this.telemetryLoading = true;
    this.adminService.getAiTelemetrySummary(this.telemetryStartDate || undefined, this.telemetryEndDate || undefined, this.keys).subscribe({
      next: (data) => {
        this.telemetrySummary = data;
        this.telemetryLoading = false;
        if (showFeedback) {
          this.store.showToast('AI Telemetry refreshed.', 'info', 'Telemetry Refreshed');
        }
      },
      error: (err) => {
        console.error('Failed to load telemetry', err);
        this.telemetryLoading = false;
        if (showFeedback) {
          this.store.showToast('Failed to load telemetry: ' + (err.error?.message || err.message), 'error', 'Error');
        }
      }
    });
  }

  /** Partitions currently in a rate-limit cooldown. */
  get rateLimitedKeyCount(): number {
    return this.governorStatus?.activeKeys.filter(k => k.isRateLimited).length ?? 0;
  }

  /** Requests in the summary that the named role lines do not account for. */
  get telemetryOtherRequests(): number {
    const s = this.telemetrySummary;
    if (!s) {
      return 0;
    }
    return Math.max(0, s.totalRequests - (s.totalChatRequests + s.totalTitleRequests + s.totalBenchmarkRequests));
  }

  /**
   * Reloads the summary and the governor status together, and the users view. Each request settles
   * on its own, so one failure still shows the other's data; the feedback is a single toast either way.
   */
  refreshTelemetryTab(showFeedback = false) {
    this.telemetryLoading = true;
    this.governorLoading = true;
    const failures: string[] = [];
    const settle = <T>(request: Observable<T>, what: string) => request.pipe(
      catchError(err => {
        console.error(`Failed to load ${what}`, err);
        failures.push(`${what}: ${err.error?.message || err.message}`);
        return of(null);
      })
    );

    if (showFeedback) {
      this.usersView?.reload();
    }

    forkJoin({
      telemetry: settle(this.adminService.getAiTelemetrySummary(this.telemetryStartDate || undefined, this.telemetryEndDate || undefined, this.keys), 'telemetry'),
      governor: settle(this.adminService.getGovernorStatus(), 'governor status')
    }).subscribe(({ telemetry, governor }) => {
      if (telemetry) {
        this.telemetrySummary = telemetry;
      }
      if (governor) {
        this.setGovernorStatus(governor);
      }
      this.telemetryLoading = false;
      this.governorLoading = false;
      if (!showFeedback) {
        return;
      }
      if (failures.length > 0) {
        this.store.showToast('Failed to load ' + failures.join('; '), 'error', 'Error');
      } else {
        this.store.showToast('AI Telemetry refreshed.', 'info', 'Telemetry Refreshed');
      }
    });
  }

  loadGovernorStatus(showFeedback = false) {
    this.governorLoading = true;
    this.adminService.getGovernorStatus().subscribe({
      next: (data) => {
        this.setGovernorStatus(data);
        this.governorLoading = false;
        if (showFeedback) {
          this.store.showToast('Governor status refreshed.', 'info', 'Governor Refreshed');
        }
      },
      error: (err) => {
        console.error('Failed to load governor status', err);
        this.governorLoading = false;
        if (showFeedback) {
          this.store.showToast('Failed to load governor status: ' + (err.error?.message || err.message), 'error', 'Error');
        }
      }
    });
  }

  private setGovernorStatus(status: AiGovernorStatusDto) {
    this.governorStatus = status;
    this.startGovernorCountdown();
  }

  /**
   * Counts the fetched cooldowns down locally once a second. When the last one reaches zero the
   * status is re-fetched once, so the server's view replaces the local estimate.
   */
  private startGovernorCountdown() {
    this.stopGovernorCountdown();
    if (this.rateLimitedKeyCount === 0) {
      return;
    }
    this.governorCountdownTimer = setInterval(() => {
      for (const key of this.governorStatus?.activeKeys ?? []) {
        if (!key.isRateLimited) {
          continue;
        }
        key.remainingCooldownSeconds = Math.max(0, Math.round((key.remainingCooldownSeconds - 1) * 10) / 10);
        if (key.remainingCooldownSeconds === 0) {
          key.isRateLimited = false;
        }
      }
      if (this.rateLimitedKeyCount === 0) {
        this.stopGovernorCountdown();
        this.loadGovernorStatus();
      }
    }, 1000);
  }

  private stopGovernorCountdown() {
    if (this.governorCountdownTimer !== undefined) {
      clearInterval(this.governorCountdownTimer);
      this.governorCountdownTimer = undefined;
    }
  }

  /** Without a key, clears every partition; refused while nothing is rate-limited (the button is aria-disabled). */
  resetGovernorCooldown(credentialKey?: string) {
    if (!credentialKey && this.rateLimitedKeyCount === 0) {
      return;
    }
    this.adminService.resetGovernorCooldown(credentialKey).subscribe({
      next: () => {
        this.store.showToast(credentialKey ? `Cooldown cleared for ${credentialKey}.` : 'All rate-limit cooldowns cleared.', 'success', 'Cooldown Reset');
        this.loadGovernorStatus();
      },
      error: (err) => {
        this.store.showToast('Failed to reset cooldown: ' + (err.error?.message || err.message), 'error', 'Error');
      }
    });
  }

  // --- Charts ---

  /** Opens the charts dialog for the range, the Keys filter and the breakdown's roles. */
  openCharts() {
    this.chartsDialog?.open({
      start: this.telemetryStartDate,
      end: this.telemetryEndDate,
      keys: this.keys,
      roles: [...this.view.roles]
    });
  }

  /** Opens the charts dialog for one user. */
  openUserCharts(user: TelemetryUserRef) {
    this.chartsDialog?.open({
      start: this.telemetryStartDate,
      end: this.telemetryEndDate,
      keys: this.keys,
      roles: [...this.view.roles],
      userId: user.userId,
      userName: user.userName
    });
  }

  // --- Usage by user ---

  /** `Claude X · Chat`, with the level under Show separately and the key badge's text where there is one. */
  cardLabel(card: BreakdownCard): string {
    const parts = [cardTitle(card), roleGroupLabel(card.roleGroup)];
    if (card.thinkingLevel !== undefined) {
      parts.push(thinkingLevelLabel(card.thinkingLevel));
    }
    const key = this.keyLabel(card);
    if (key) {
      parts.push(key);
    }
    return parts.join(' · ');
  }

  /** Opens the Usage by user dialog for the card's model, role group, key source and level, over the tab's range. */
  openModelUsers(card: BreakdownCard, event?: Event) {
    this.modelUsersDialog?.open({
      start: this.telemetryStartDate,
      end: this.telemetryEndDate,
      keySource: card.keySource,
      provider: card.provider,
      modelId: card.modelId,
      modelTitle: cardTitle(card),
      roleGroup: card.roleGroup,
      thinkingLevel: card.thinkingLevel
    }, (event?.currentTarget as HTMLElement | null) ?? undefined);
  }

  // --- Users view ---

  /** Reads and clears the page store's pending telemetry-user request and filters the users view; true when there was one. */
  private takePendingTelemetryUser(): boolean {
    const id = this.store.pendingTelemetryUserId;
    if (!id) {
      return false;
    }
    const name = this.store.pendingTelemetryUserName || id;
    this.store.pendingTelemetryUserId = null;
    this.store.pendingTelemetryUserName = null;
    this.usersView?.filterToUser(id, name);
    return true;
  }

  // --- Summary cards ---

  /** The date part of a server timestamp or day, "YYYY-MM-DD". */
  utcDay(value: string | null | undefined): string {
    if (!value) {
      return '';
    }
    const match = /^\d{4}-\d{2}-\d{2}/.exec(value);
    if (match) {
      return match[0];
    }
    const date = new Date(value);
    return isNaN(date.getTime()) ? value : date.toISOString().slice(0, 10);
  }

  /** True when the range starts before the given day, so part of it precedes the recording. */
  startsBefore(day: string | null | undefined): boolean {
    const since = this.utcDay(day);
    return !!since && !!this.telemetryStartDate && this.telemetryStartDate < since;
  }

  get totalTokens(): number {
    const s = this.telemetrySummary;
    return s ? s.totalPromptTokens + s.totalOutputTokens : 0;
  }

  /** The estimated cost per named role group, and Other while it has a cost or requests. */
  get costLines(): TelemetrySummaryLine[] {
    const rows = this.telemetrySummary?.rows ?? NO_ROWS;
    const costs = roleCosts(rows);
    const counts = this.roleRequestCounts;
    const groups: AiUsageRoleGroup[] = [...NAMED_ROLE_GROUPS];
    if (counts.other > 0 || costs.other > 0) {
      groups.push('other');
    }
    return groups.map(group => ({ label: roleGroupLabel(group), value: formatCost(costs[group]) }));
  }

  get unpricedRequests(): number {
    const s = this.telemetrySummary;
    return s ? Math.max(0, s.totalRequests - s.pricedRequests) : 0;
  }

  /** `3 of 120 requests unpriced`. */
  get unpricedText(): string {
    const s = this.telemetrySummary;
    if (!s) {
      return '';
    }
    return `${formatInteger(this.unpricedRequests)} of ${formatInteger(s.totalRequests)} ${s.totalRequests === 1 ? 'request' : 'requests'} unpriced`;
  }

  /** The provider error rate headline; `Not recorded` before the ledger has a row. */
  get availabilityHeadline(): string {
    const a = this.telemetrySummary?.chatAvailability;
    if (!a || !a.recorded) {
      return NOT_RECORDED_TEXT;
    }
    return a.providerErrorRate != null ? formatPercent(a.providerErrorRate) : 'No attempts';
  }

  /** `12 of 1,480 attempts · 95% CI 0.42–1.4%`, with `· few attempts` below the threshold; empty while not recorded. */
  get availabilityCountText(): string {
    const a = this.telemetrySummary?.chatAvailability;
    if (!a || !a.recorded) {
      return '';
    }
    if (a.validAttempts <= 0) {
      return 'No valid attempts';
    }
    const parts = [formatCount(a.providerErrors, a.validAttempts)];
    const interval = formatInterval(a.low, a.high);
    if (interval) {
      parts.push(interval);
    }
    if (a.validAttempts < FEW_ATTEMPTS_THRESHOLD) {
      parts.push('few attempts');
    }
    return parts.join(' · ');
  }

  /** The availability card's term list: first-attempt success, turns ended, title generation. */
  get availabilityRows(): TelemetrySummaryLine[] {
    const a = this.telemetrySummary?.chatAvailability;
    if (!a || !a.recorded) {
      return [];
    }
    return [
      {
        label: 'First-attempt success',
        value: a.calls > 0 ? formatPercent(a.firstAttemptSuccessCalls / a.calls) : 'No calls',
        note: a.calls > 0 ? `${formatInteger(a.firstAttemptSuccessCalls)} of ${formatInteger(a.calls)} ${a.calls === 1 ? 'call' : 'calls'}` : undefined
      },
      {
        label: 'Turns ended by a provider error',
        value: `${formatInteger(a.chatTurnsEndedByProviderError)} of ${formatInteger(a.chatTurns)}`
      },
      {
        label: 'Title generation',
        value: a.titleValidAttempts > 0 ? formatCount(a.titleProviderErrors, a.titleValidAttempts) : 'No attempts'
      }
    ];
  }

  /** The four provider error classes, empty while not recorded. */
  get errorClassRows(): TelemetrySummaryLine[] {
    const a = this.telemetrySummary?.chatAvailability;
    if (!a || !a.recorded) {
      return [];
    }
    return [
      { label: 'Overloaded', value: formatInteger(a.overloaded) },
      { label: 'Server errors', value: formatInteger(a.serverErrors) },
      { label: 'Timeouts', value: formatInteger(a.timeouts) },
      { label: 'Stream failures', value: formatInteger(a.streamFailures) }
    ];
  }

  /** The unattributed and our-side failures, outside the rate; empty while not recorded. */
  get errorAttributionRows(): TelemetrySummaryLine[] {
    const a = this.telemetrySummary?.chatAvailability;
    if (!a || !a.recorded) {
      return [];
    }
    return [
      {
        label: 'Unattributed',
        value: formatInteger(a.unattributedErrors),
        note: `rate limited ${formatInteger(a.rateLimitedUnattributed)} · transport ${formatInteger(a.transportUnattributed)}`
          + (a.inclusiveRate != null ? ` · ${formatPercent(a.inclusiveRate)} with them` : '')
      },
      { label: 'Our side', value: formatInteger(a.ourSideFailures), note: 'excluded from the rate' }
    ];
  }

  // --- Breakdown ---

  /** Requests per role group over every row of the summary. */
  get roleRequestCounts(): Record<AiUsageRoleGroup, number> {
    const rows = this.telemetrySummary?.rows ?? NO_ROWS;
    if (this.roleCountsMemo?.rows !== rows) {
      this.roleCountsMemo = { rows, counts: roleCounts(rows) };
    }
    return this.roleCountsMemo.counts;
  }

  /** The role checkboxes: the named groups, and Other while it has rows. */
  get roleOptions(): AiUsageRoleGroup[] {
    const rows = this.telemetrySummary?.rows ?? NO_ROWS;
    const groups: AiUsageRoleGroup[] = [...NAMED_ROLE_GROUPS];
    if (rows.some(r => r.roleGroup === 'other')) {
      groups.push('other');
    }
    return groups;
  }

  get hasBreakdownRows(): boolean {
    return (this.telemetrySummary?.rows ?? NO_ROWS).length > 0;
  }

  /**
   * The cards and their total; memoized on the rows, the view settings and the applied search.
   * `saveView` always replaces the providers and the sort, so their identity marks a change.
   */
  get breakdown(): Breakdown {
    const rows = this.telemetrySummary?.rows ?? NO_ROWS;
    const { roles, separateThinkingLevels: separate, providers, sort } = this.view;
    const search = this.search;
    const memo = this.breakdownMemo;
    if (memo && memo.rows === rows && memo.roles === roles && memo.separate === separate
      && memo.search === search && memo.providers === providers && memo.sort === sort) {
      return memo.breakdown;
    }
    const breakdown = buildBreakdown(rows, { roles, separateThinkingLevels: separate, search, providers, sort });
    this.breakdownMemo = { rows, roles, separate, search, providers, sort, breakdown };
    return breakdown;
  }

  /** One provider toggle per provider of the selected roles' cards, counting cards whatever the search. */
  get providerFacets(): ModelPickerProviderFacet[] {
    const all = this.breakdown.all;
    if (this.facetsMemo?.all !== all) {
      this.facetsMemo = { all, facets: breakdownProviderFacets(all) };
    }
    return this.facetsMemo.facets;
  }

  /** The pressed providers that some card has. */
  get effectiveProviders(): string[] {
    return effectiveBreakdownProviders(this.view.providers, this.providerFacets);
  }

  /** The applied search or a provider filter narrows the cards. */
  get breakdownFiltered(): boolean {
    return this.search.trim() !== '' || this.effectiveProviders.length > 0;
  }

  /** Clear filters is offered while the field has text or a provider toggle is pressed. */
  get breakdownFiltersActive(): boolean {
    return this.searchInput.trim() !== '' || this.breakdownFiltered;
  }

  /**
   * `Showing 4 entries · 1,200 of 1,500 requests`; filtered, `Showing 4 of 13 entries · 1,200 of
   * 5,270 requests`, or `No entries match "luna".`; empty with nothing to show.
   */
  get breakdownStatus(): string {
    if (!this.hasBreakdownRows) {
      return '';
    }
    const { all, cards, total } = this.breakdown;
    if (all.length === 0) {
      return '';
    }
    const allRequests = (this.telemetrySummary?.rows ?? NO_ROWS).reduce((sum, r) => sum + r.requests, 0);
    if (this.breakdownFiltered) {
      if (cards.length === 0) {
        const search = this.search.trim();
        return search && this.effectiveProviders.length === 0 ? `No entries match "${search}".` : 'No entries match the filters.';
      }
      return `Showing ${cards.length} of ${all.length} ${all.length === 1 ? 'entry' : 'entries'}`
        + ` · ${formatInteger(total.requests)} of ${formatInteger(allRequests)} requests`;
    }
    const entries = cards.length === 1 ? 'One entry' : `Showing ${cards.length} entries`;
    const requests = total.requests === allRequests
      ? `${formatInteger(allRequests)} ${allRequests === 1 ? 'request' : 'requests'}`
      : `${formatInteger(total.requests)} of ${formatInteger(allRequests)} requests`;
    return `${entries} · ${requests}`;
  }

  // --- Breakdown search and provider filter ---

  onSearchInput(event: Event) {
    this.searchInput = (event.target as HTMLInputElement).value;
    this.cancelSearchTimer();
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.search = this.searchInput;
    }, SEARCH_DEBOUNCE_MS);
  }

  /** Escape with text clears the search at once; with none, Escape is left alone. */
  onSearchKeydown(event: KeyboardEvent) {
    if (event.key !== 'Escape' || this.searchInput === '') {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.clearSearch();
  }

  isProviderPressed(facet: ModelPickerProviderFacet): boolean {
    return this.view.providers.includes(facet.key);
  }

  /** Presses or releases a provider toggle and stores the view. */
  toggleProvider(facet: ModelPickerProviderFacet) {
    const providers = this.isProviderPressed(facet)
      ? this.view.providers.filter(p => p !== facet.key)
      : [...this.view.providers, facet.key];
    this.saveView({ ...this.view, providers });
  }

  /** Clears the search and the provider toggles, not the roles, the thinking setting or the sort, and focuses the search field. */
  clearBreakdownFilters() {
    this.clearSearch();
    if (this.view.providers.length > 0) {
      this.saveView({ ...this.view, providers: [] });
    }
    this.host.nativeElement.querySelector<HTMLInputElement>('#tl-breakdown-search')?.focus();
  }

  private clearSearch() {
    this.cancelSearchTimer();
    this.searchInput = '';
    this.search = '';
  }

  private cancelSearchTimer() {
    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }
  }

  // --- Breakdown sort ---

  /** `Requests, highest first`. */
  get sortLabel(): string {
    return breakdownSortLabel(this.view.sort);
  }

  get sortIsDefault(): boolean {
    return isDefaultBreakdownSort(this.view.sort);
  }

  /** `Highest first` or `A–Z`, by the kind of the chosen field. */
  directionLabel(direction: BreakdownSortDirection): string {
    return breakdownDirectionLabel(breakdownSortField(this.view.sort.field).kind, direction);
  }

  /** Applies at once and keeps the direction; the popover stays open. */
  chooseSortField(field: BreakdownSortField) {
    if (field !== this.view.sort.field) {
      this.saveView({ ...this.view, sort: { field, direction: this.view.sort.direction } });
    }
  }

  chooseSortDirection(direction: BreakdownSortDirection) {
    if (direction !== this.view.sort.direction) {
      this.saveView({ ...this.view, sort: { field: this.view.sort.field, direction } });
    }
  }

  /** Back to most requests first; refused at the default (the button is aria-disabled), and the button keeps focus. */
  resetSort() {
    if (!this.sortIsDefault) {
      this.saveView({ ...this.view, sort: DEFAULT_BREAKDOWN_SORT });
    }
  }

  onSortToggle(event: Event) {
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
  onSortPanelKeydown(event: KeyboardEvent) {
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
    return this.host.nativeElement.querySelector<HTMLElement>('#tl-sort-panel');
  }

  private sortTrigger(): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>('#tl-sort-trigger');
  }

  // --- Breakdown cards ---

  /** The title split into runs that match the applied search and runs that do not. */
  titleParts(card: BreakdownCard): ModelPickerMatchPart[] {
    return this.matchesOf(card).title;
  }

  modelIdParts(card: BreakdownCard): ModelPickerMatchPart[] {
    return this.matchesOf(card).modelId;
  }

  private matchesOf(card: BreakdownCard) {
    let memo = this.matchMemo.get(card);
    if (!memo || memo.search !== this.search) {
      const terms = searchTerms(this.search);
      memo = { search: this.search, title: matchParts(cardTitle(card), terms), modelId: matchParts(card.modelId, terms) };
      this.matchMemo.set(card, memo);
    }
    return memo;
  }

  roleBadgeClass(group: AiUsageRoleGroup): string {
    return ROLE_BADGE_CLASSES[group] ?? ROLE_BADGE_CLASSES.other;
  }

  /**
   * Shown separately, the card's one level, or None sent or Not recorded in a neutral badge, since
   * those are not levels; grouped, one badge per recorded level, none for the others.
   */
  thinkingBadges(card: BreakdownCard): TelemetryCardBadge[] {
    let badges = this.badgeMemo.get(card);
    if (!badges) {
      badges = thinkingBadgesOf(card);
      this.badgeMemo.set(card, badges);
    }
    return badges;
  }

  isRoleSelected(group: AiUsageRoleGroup): boolean {
    return this.view.roles.includes(group);
  }

  /** Adds or removes a role group from the breakdown and stores the view. */
  toggleRole(group: AiUsageRoleGroup, event: Event) {
    const checked = (event.target as HTMLInputElement).checked;
    const roles = checked
      ? [...this.view.roles.filter(r => r !== group), group]
      : this.view.roles.filter(r => r !== group);
    this.saveView({ ...this.view, roles });
  }

  onThinkingChange(event: Event) {
    const separate = (event.target as HTMLSelectElement).value === 'separate';
    if (separate !== this.view.separateThinkingLevels) {
      this.saveView({ ...this.view, separateThinkingLevels: separate });
    }
  }

  /** The key badge: `User key: alice` for a user's key, `System key` under Both. */
  keyLabel(card: BreakdownCard): string | null {
    if (card.keySource === 'user') {
      return `User key: ${card.userName || card.userId || 'unknown user'}`;
    }
    return this.keys === 'both' ? 'System key' : null;
  }

  cacheHitText(m: BreakdownMetrics): string {
    return m.cacheHitRatio != null ? formatPercent(m.cacheHitRatio) : '—';
  }

  durationText(m: BreakdownMetrics): string {
    return m.avgDurationMs != null ? `${formatInteger(Math.round(m.avgDurationMs))} ms` : '—';
  }

  /** The estimated cost, with `3 unpriced` while part of the requests has no price. */
  costMetric(m: BreakdownMetrics): TelemetryMetricText {
    return costMetricText(m);
  }

  /**
   * Provider errors of a Chat or Title Generation card: the rate with `k of n attempts` and
   * `about 1 in N`; `No attempts` at none. Benchmark availability is measured in GnollBench.
   */
  providerErrorsMetric(m: BreakdownMetrics, role: AiUsageRoleGroup | 'total'): TelemetryMetricText {
    if (role === 'benchmark' || (role === 'total' && this.breakdown.cards.every(c => c.roleGroup === 'benchmark'))) {
      return { value: 'In GnollBench', note: null, muted: true };
    }
    if (m.validAttempts <= 0) {
      return { value: 'No attempts', note: null, muted: true };
    }
    const oneIn = formatOneIn(m.providerErrors, m.validAttempts);
    const count = formatCount(m.providerErrors, m.validAttempts);
    return { value: formatPercent(m.providerErrors / m.validAttempts), note: oneIn ? `${count} · ${oneIn}` : count, muted: false };
  }

  private saveView(view: TelemetryBreakdownView) {
    this.view = view;
    writeBreakdownView(view);
  }
}
