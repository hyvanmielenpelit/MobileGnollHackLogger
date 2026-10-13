import { Component, ChangeDetectionStrategy, OnInit, OnDestroy, ViewChild, inject } from '@angular/core';
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
import { formatCost } from '../benchmark/batteries/battery.models';
import {
  NOT_RECORDED_TEXT,
  formatCount,
  formatInteger,
  formatOneIn,
  formatPercent,
  formatRate
} from '../benchmark/shared/provider-availability/provider-availability';
import { AdminPageStore, TelemetryTimeSpan } from '../admin-page.store';
import { TelemetryChartsDialogComponent } from './charts/telemetry-charts-dialog.component';
import {
  Breakdown,
  BreakdownCard,
  BreakdownMetrics,
  KEY_SOURCES,
  KEY_SOURCE_LABELS,
  NAMED_ROLE_GROUPS,
  TelemetryBreakdownView,
  buildBreakdown,
  cardTitle,
  readBreakdownView,
  roleCosts,
  roleCounts,
  roleGroupLabel,
  thinkingLevelLabel,
  writeBreakdownView
} from './telemetry-breakdown';
import { TelemetryUserRef, TelemetryUsersComponent } from './users/telemetry-users.component';

/** A metric's value, and the qualifier line under it when there is one. */
export interface TelemetryMetricText {
  value: string;
  note: string | null;
}

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
  imports: [CommonModule, FormsModule, ProviderBadgeComponent, TelemetryUsersComponent, TelemetryChartsDialogComponent],
  templateUrl: './admin-telemetry-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-telemetry-tab.component.scss'
})
export class AdminTelemetryTabComponent implements OnInit, OnDestroy {
  private adminService = inject(AdminService);
  private store = inject(AdminPageStore);

  @ViewChild('chartsDialog') chartsDialog?: TelemetryChartsDialogComponent;
  @ViewChild(TelemetryUsersComponent, { static: true }) usersView?: TelemetryUsersComponent;

  readonly keySources = KEY_SOURCES;
  readonly keySourceLabels = KEY_SOURCE_LABELS;
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

  private governorCountdownTimer?: ReturnType<typeof setInterval>;
  private telemetryUserSub?: Subscription;
  private breakdownMemo: {
    rows: readonly AiUsageBreakdownRowDto[];
    roles: readonly AiUsageRoleGroup[];
    separate: boolean;
    breakdown: Breakdown;
  } | null = null;
  private roleCountsMemo: { rows: readonly AiUsageBreakdownRowDto[]; counts: Record<AiUsageRoleGroup, number> } | null = null;

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

  ngOnDestroy() {
    this.stopGovernorCountdown();
    this.telemetryUserSub?.unsubscribe();
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

  /** The availability card's lines, empty while not recorded. */
  get availabilityLines(): TelemetrySummaryLine[] {
    const a = this.telemetrySummary?.chatAvailability;
    if (!a || !a.recorded) {
      return [];
    }
    const lines: TelemetrySummaryLine[] = [
      { label: 'Provider errors', value: formatRate(a.providerErrors, a.validAttempts, a.low, a.high) },
      {
        label: 'Classes',
        value: `overloaded ${formatInteger(a.overloaded)} · server errors ${formatInteger(a.serverErrors)} · timeouts ${formatInteger(a.timeouts)} · stream failures ${formatInteger(a.streamFailures)}`
      },
      {
        label: 'Unattributed',
        value: formatInteger(a.unattributedErrors),
        note: `rate limited ${formatInteger(a.rateLimitedUnattributed)} · transport ${formatInteger(a.transportUnattributed)}`
          + (a.inclusiveRate != null ? `; ${formatPercent(a.inclusiveRate)} with them` : '')
      },
      { label: 'Our side', value: formatInteger(a.ourSideFailures), note: 'excluded from the rate' },
      {
        label: 'First-attempt success',
        value: a.calls > 0 ? formatPercent(a.firstAttemptSuccessCalls / a.calls) : 'No calls',
        note: a.calls > 0 ? `${formatInteger(a.firstAttemptSuccessCalls)} of ${formatInteger(a.calls)} ${a.calls === 1 ? 'call' : 'calls'}` : undefined
      },
      {
        label: 'Turns ended by a provider error',
        value: `${formatInteger(a.chatTurnsEndedByProviderError)} of ${formatInteger(a.chatTurns)}`
      }
    ];
    return lines;
  }

  /** `Title generation: 2 of 140 attempts`, the title attempts with a provider error. */
  get titleAvailabilityText(): string {
    const a = this.telemetrySummary?.chatAvailability;
    if (!a || !a.recorded) {
      return '';
    }
    return a.titleValidAttempts > 0
      ? `Title generation: ${formatCount(a.titleProviderErrors, a.titleValidAttempts)}`
      : 'Title generation: no attempts';
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

  /** The cards and their total; memoized on the rows and the view settings. */
  get breakdown(): Breakdown {
    const rows = this.telemetrySummary?.rows ?? NO_ROWS;
    const memo = this.breakdownMemo;
    if (memo && memo.rows === rows && memo.roles === this.view.roles && memo.separate === this.view.separateThinkingLevels) {
      return memo.breakdown;
    }
    const breakdown = buildBreakdown(rows, { roles: this.view.roles, separateThinkingLevels: this.view.separateThinkingLevels });
    this.breakdownMemo = { rows, roles: this.view.roles, separate: this.view.separateThinkingLevels, breakdown };
    return breakdown;
  }

  /** `Showing 4 entries · 1,200 of 1,500 requests`; empty with nothing to show. */
  get breakdownStatus(): string {
    if (!this.hasBreakdownRows) {
      return '';
    }
    const { cards, total } = this.breakdown;
    if (cards.length === 0) {
      return '';
    }
    const all = (this.telemetrySummary?.rows ?? NO_ROWS).reduce((sum, r) => sum + r.requests, 0);
    const entries = cards.length === 1 ? 'One entry' : `Showing ${cards.length} entries`;
    const requests = total.requests === all
      ? `${formatInteger(all)} ${all === 1 ? 'request' : 'requests'}`
      : `${formatInteger(total.requests)} of ${formatInteger(all)} requests`;
    return `${entries} · ${requests}`;
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

  /** The kicker's key part: `User key: alice` for a user's key, `System` under Both. */
  keyLabel(card: BreakdownCard): string | null {
    if (card.keySource === 'user') {
      return `User key: ${card.userName || card.userId || 'unknown user'}`;
    }
    return this.keys === 'both' ? 'System' : null;
  }

  cacheHitText(m: BreakdownMetrics): string {
    return m.cacheHitRatio != null ? formatPercent(m.cacheHitRatio) : '—';
  }

  durationText(m: BreakdownMetrics): string {
    return m.avgDurationMs != null ? `${formatInteger(Math.round(m.avgDurationMs))} ms` : '—';
  }

  /** The estimated cost, with `3 unpriced` while part of the requests has no price. */
  costMetric(m: BreakdownMetrics): TelemetryMetricText {
    if (m.requests > 0 && m.pricedRequests === 0) {
      return { value: '—', note: `${formatInteger(m.unpricedRequests)} unpriced` };
    }
    return { value: formatCost(m.estimatedCost), note: m.unpricedRequests > 0 ? `${formatInteger(m.unpricedRequests)} unpriced` : null };
  }

  /**
   * Provider errors of a Chat or Title Generation card: the rate with `k of n attempts` and
   * `about 1 in N`; `No attempts` at none. Benchmark availability is measured in GnollBench.
   */
  providerErrorsMetric(m: BreakdownMetrics, role: AiUsageRoleGroup | 'total'): TelemetryMetricText {
    if (role === 'benchmark' || (role === 'total' && this.breakdown.cards.every(c => c.roleGroup === 'benchmark'))) {
      return { value: 'In GnollBench', note: null };
    }
    if (m.validAttempts <= 0) {
      return { value: 'No attempts', note: null };
    }
    const oneIn = formatOneIn(m.providerErrors, m.validAttempts);
    const count = formatCount(m.providerErrors, m.validAttempts);
    return { value: formatPercent(m.providerErrors / m.validAttempts), note: oneIn ? `${count} · ${oneIn}` : count };
  }

  private saveView(view: TelemetryBreakdownView) {
    this.view = view;
    writeBreakdownView(view);
  }
}
