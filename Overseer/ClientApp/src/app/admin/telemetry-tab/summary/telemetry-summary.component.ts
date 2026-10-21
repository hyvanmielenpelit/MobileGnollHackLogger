import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import {
  AiGovernorStatusDto,
  AiTelemetryKeySource,
  AiTelemetrySummaryDto,
  AiUsageBreakdownRowDto,
  AiUsageRoleGroup
} from '../../../services/admin.service';
import { formatCost } from '../../benchmark/batteries/battery.models';
import {
  FEW_ATTEMPTS_THRESHOLD,
  NOT_RECORDED_TEXT,
  formatCount,
  formatInteger,
  formatInterval,
  formatPercent
} from '../../benchmark/shared/provider-availability/provider-availability';
import { NAMED_ROLE_GROUPS, roleCosts, roleCounts, roleGroupLabel } from '../telemetry-breakdown';

/** A labeled line of a summary card. */
export interface TelemetrySummaryLine {
  label: string;
  value: string;
  note?: string;
}

const NO_ROWS: readonly AiUsageBreakdownRowDto[] = [];

/**
 * The AI Telemetry tab's Summary panel: usage, cost, provider availability, Overseer activity and
 * the request governor's partitions. The tab fetches the summary and the governor status; the
 * panel shows them and asks the tab to clear cooldowns.
 */
@Component({
  selector: 'app-telemetry-summary',
  imports: [DecimalPipe],
  templateUrl: './telemetry-summary.component.html',
  // The governor countdown changes the partitions in place, so the panel is checked with the tab.
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './telemetry-summary.component.scss'
})
export class TelemetrySummaryComponent {
  /** The tab's summary for its range and Keys filter; null until the first load. */
  readonly summary = input<AiTelemetrySummaryDto | null>(null);
  /** The tab's Keys filter. */
  readonly keys = input<AiTelemetryKeySource>('system');
  /** The tab's date range, "YYYY-MM-DD"; empty for no bound. */
  readonly start = input<string>('');
  readonly end = input<string>('');
  /** The request governor's live state; null until the first load. */
  readonly governorStatus = input<AiGovernorStatusDto | null>(null);
  readonly governorLoading = input<boolean>(false);

  /** Clear cooldown was pressed: for one partition, or for every partition without a key. */
  readonly resetCooldown = output<string | undefined>();

  readonly formatInteger = formatInteger;
  readonly formatCost = formatCost;

  private roleCountsMemo: { rows: readonly AiUsageBreakdownRowDto[]; counts: Record<AiUsageRoleGroup, number> } | null = null;

  /** Partitions currently in a rate-limit cooldown. */
  get rateLimitedKeyCount(): number {
    return this.governorStatus()?.activeKeys.filter(k => k.isRateLimited).length ?? 0;
  }

  /** Requests in the summary that the named role lines do not account for. */
  get telemetryOtherRequests(): number {
    const s = this.summary();
    if (!s) {
      return 0;
    }
    return Math.max(0, s.totalRequests - (s.totalChatRequests + s.totalTitleRequests + s.totalBenchmarkRequests));
  }

  /** Clear All Cooldowns; the tab refuses it while nothing is rate-limited (the button is aria-disabled). */
  clearAllCooldowns() {
    this.resetCooldown.emit(undefined);
  }

  clearCooldown(credentialKey: string) {
    this.resetCooldown.emit(credentialKey);
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
    const start = this.start();
    return !!since && !!start && start < since;
  }

  get totalTokens(): number {
    const s = this.summary();
    return s ? s.totalPromptTokens + s.totalOutputTokens : 0;
  }

  /** Requests per role group over every row of the summary. */
  get roleRequestCounts(): Record<AiUsageRoleGroup, number> {
    const rows = this.summary()?.rows ?? NO_ROWS;
    if (this.roleCountsMemo?.rows !== rows) {
      this.roleCountsMemo = { rows, counts: roleCounts(rows) };
    }
    return this.roleCountsMemo.counts;
  }

  /** The estimated cost per named role group, and Other while it has a cost or requests. */
  get costLines(): TelemetrySummaryLine[] {
    const rows = this.summary()?.rows ?? NO_ROWS;
    const costs = roleCosts(rows);
    const counts = this.roleRequestCounts;
    const groups: AiUsageRoleGroup[] = [...NAMED_ROLE_GROUPS];
    if (counts.other > 0 || costs.other > 0) {
      groups.push('other');
    }
    return groups.map(group => ({ label: roleGroupLabel(group), value: formatCost(costs[group]) }));
  }

  get unpricedRequests(): number {
    const s = this.summary();
    return s ? Math.max(0, s.totalRequests - s.pricedRequests) : 0;
  }

  /** `3 of 120 requests unpriced`. */
  get unpricedText(): string {
    const s = this.summary();
    if (!s) {
      return '';
    }
    return `${formatInteger(this.unpricedRequests)} of ${formatInteger(s.totalRequests)} ${s.totalRequests === 1 ? 'request' : 'requests'} unpriced`;
  }

  /** The provider error rate headline; `Not recorded` before the ledger has a row. */
  get availabilityHeadline(): string {
    const a = this.summary()?.chatAvailability;
    if (!a || !a.recorded) {
      return NOT_RECORDED_TEXT;
    }
    return a.providerErrorRate != null ? formatPercent(a.providerErrorRate) : 'No attempts';
  }

  /** `12 of 1,480 attempts · 95% CI 0.42–1.4%`, with `· few attempts` below the threshold; empty while not recorded. */
  get availabilityCountText(): string {
    const a = this.summary()?.chatAvailability;
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
    const a = this.summary()?.chatAvailability;
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
    const a = this.summary()?.chatAvailability;
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
    const a = this.summary()?.chatAvailability;
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
}
