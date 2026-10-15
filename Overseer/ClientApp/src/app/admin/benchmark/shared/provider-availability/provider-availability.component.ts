import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';

import type {
  BenchmarkBatteryMemberAvailabilityDto,
  ProviderAttemptSide,
  ProviderAvailabilityBinDto,
  ProviderAvailabilityDto,
  ProviderAvailabilityOutcomeDto
} from '../../../../services/admin-benchmark.service';
import { parseServerUtcDate } from '../../../../utils/date.util';
import { rovingTabIndex } from '../../../../utils/roving-tab.util';
import {
  FEW_ATTEMPTS_THRESHOLD,
  NOT_RECORDED_TEXT,
  SIDE_LABELS,
  availabilityHeadline,
  formatCount,
  formatInteger,
  formatInterval,
  formatOneIn,
  formatPercent,
  formatReruns,
  outcomeLabel
} from './provider-availability';

/**
 * Where the Integrity tab's outcome view is kept, per browser, as `{ version: 1, view }`. Read and
 * written in `try/catch`; `strip` when absent or unreadable.
 */
export const PROVIDER_AVAILABILITY_VIEW_STORAGE_KEY = 'overseer.benchmark.providerAvailability.view';
const VIEW_STORAGE_VERSION = 1;

/** The outcome view: the 5-minute time strip, or the stacked bar of attempts by class. */
export type ProviderAvailabilityView = 'strip' | 'bar';

export const PROVIDER_AVAILABILITY_VIEWS: readonly { readonly key: ProviderAvailabilityView; readonly label: string }[] = [
  { key: 'strip', label: 'Time strip' },
  { key: 'bar', label: 'Stacked bar' }
];

/** The remembered outcome view; `strip` when none is stored, it is unknown, or storage is unavailable. */
export function readStoredProviderAvailabilityView(): ProviderAvailabilityView {
  try {
    const raw = localStorage.getItem(PROVIDER_AVAILABILITY_VIEW_STORAGE_KEY);
    if (!raw) {
      return 'strip';
    }
    const parsed = JSON.parse(raw) as { version?: unknown; view?: unknown } | null;
    if (parsed?.version !== VIEW_STORAGE_VERSION) {
      return 'strip';
    }
    return PROVIDER_AVAILABILITY_VIEWS.find(v => v.key === parsed.view)?.key ?? 'strip';
  } catch {
    return 'strip';
  }
}

export function storeProviderAvailabilityView(view: ProviderAvailabilityView): void {
  try {
    localStorage.setItem(PROVIDER_AVAILABILITY_VIEW_STORAGE_KEY, JSON.stringify({ version: VIEW_STORAGE_VERSION, view }));
  } catch {
    // Storage unavailable: the view is simply not remembered.
  }
}

// -----------------------------------------------------------------------------------------------
// The key-figure tile
// -----------------------------------------------------------------------------------------------

/** The Provider Errors key-figure tile of the run and battery run dialogs. */
export interface ProviderErrorsFigure {
  readonly recorded: boolean;
  /** `0.81%`; `Not recorded`; `—` without a valid attempt. */
  readonly value: string;
  readonly muted: boolean;
  /** `12 of 1,480 attempts`; null when not recorded. */
  readonly count: string | null;
  /** `95% CI 0.44–1.5% · 3 unattributed · 2 re-runs (1 provider)`, or why nothing is recorded. */
  readonly note: string;
  /** Fewer than 100 valid attempts on a recorded run. */
  readonly fewAttempts: boolean;
}

/** `2 re-runs (1 provider)`, `1 re-run`, `0 re-runs`. */
export function compactReruns(reruns: number, providerReruns: number): string {
  const head = `${formatInteger(reruns)} ${reruns === 1 ? 're-run' : 're-runs'}`;
  return providerReruns > 0 ? `${head} (${formatInteger(providerReruns)} provider)` : head;
}

/** The tile's figures from a run's or a battery run's availability. */
export function providerErrorsFigure(a: ProviderAvailabilityDto | null | undefined): ProviderErrorsFigure {
  if (!a || !a.recorded) {
    return {
      recorded: false,
      value: NOT_RECORDED_TEXT,
      muted: true,
      count: null,
      note: 'runs started before harness 56 record no provider attempts',
      fewAttempts: false
    };
  }
  const n = a.validAttempts;
  const rate = a.providerErrorRate ?? (n > 0 ? a.providerErrors / n : null);
  const parts: string[] = [];
  const interval = formatInterval(a.providerErrorRateLow, a.providerErrorRateHigh);
  if (interval) {
    parts.push(interval);
  }
  if (a.unattributedErrors > 0) {
    parts.push(`${formatInteger(a.unattributedErrors)} unattributed`);
  }
  parts.push(compactReruns(a.rerunsNeeded, a.providerRerunsNeeded));
  return {
    recorded: true,
    value: rate == null ? '—' : formatPercent(rate),
    muted: rate == null,
    count: formatCount(a.providerErrors, n),
    note: parts.join(' · '),
    fewAttempts: a.fewAttempts || n < FEW_ATTEMPTS_THRESHOLD
  };
}

// -----------------------------------------------------------------------------------------------
// The section's view models
// -----------------------------------------------------------------------------------------------

/** A strip cell's shading: no attempts, our-side failures only, or the provider-error share in five steps. */
export type StripShade = 'empty' | 'ours' | 's0' | 's1' | 's2' | 's3' | 's4';

/** The upper bounds of the shares shaded s1 to s3; above the last is s4. */
export const STRIP_SHARE_STEPS = [0.01, 0.05, 0.2] as const;

/** A bin's shade: `empty` with no attempt, `ours` with our-side failures only, else by its provider-error share. */
export function stripShadeOf(bin: ProviderAvailabilityBinDto): StripShade {
  if (bin.valid <= 0) {
    return bin.ours > 0 ? 'ours' : 'empty';
  }
  const share = bin.provider / bin.valid;
  if (share <= 0) return 's0';
  if (share <= STRIP_SHARE_STEPS[0]) return 's1';
  if (share <= STRIP_SHARE_STEPS[1]) return 's2';
  if (share <= STRIP_SHARE_STEPS[2]) return 's3';
  return 's4';
}

/** The strip's legend, in ramp order. */
export const STRIP_LEGEND: readonly { readonly shade: StripShade; readonly label: string }[] = [
  { shade: 'empty', label: 'No attempts' },
  { shade: 'ours', label: 'Our side only' },
  { shade: 's0', label: '0%' },
  { shade: 's1', label: 'up to 1%' },
  { shade: 's2', label: 'up to 5%' },
  { shade: 's3', label: 'up to 20%' },
  { shade: 's4', label: 'over 20%' }
];

/** `2026-10-12 13:05 UTC`. */
export function formatUtcMinute(value: string | Date): string {
  const date = parseServerUtcDate(value);
  if (Number.isNaN(date.getTime())) {
    return String(value);
  }
  const pad = (v: number): string => String(v).padStart(2, '0');
  return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())} `
    + `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())} UTC`;
}

/** The width of one strip bin. */
export const STRIP_BIN_MS = 5 * 60 * 1000;

/** What a replacement's cause says about the replaced run. */
export function replacementCauseText(cause: string | null | undefined): string {
  switch (cause) {
    case 'ProviderFailure': return 'which failed at the provider';
    case 'OurFailure': return 'which failed on our side';
    case 'InstrumentChange': return 'replaced under the current instrument';
    case 'OperatorRequest': return 'replaced at the operator\'s request';
    default: return 'cause not recorded';
  }
}

/** One row of the breakdown tables. */
export interface BreakdownRow {
  readonly outcome: string;
  readonly label: string;
  readonly count: string;
  readonly share: string;
  readonly statuses: string;
}

/** A side's rows of the valid-attempt breakdown; `label` null for the Succeeded row, which needs no group heading. */
export interface BreakdownGroup {
  readonly side: ProviderAttemptSide;
  readonly label: string | null;
  readonly rows: readonly BreakdownRow[];
}

/** One cell of the strip and one row of its hidden table. */
export interface StripCell {
  readonly startUtc: string;
  readonly time: string;
  readonly shade: StripShade;
  readonly valid: string;
  readonly provider: string;
  readonly unattributed: string;
  readonly ours: string;
  readonly share: string;
}

/** One segment of the stacked bar and its legend item. */
export interface BarSegment {
  readonly outcome: string;
  readonly label: string;
  readonly side: ProviderAttemptSide;
  readonly count: number;
  readonly countText: string;
  readonly share: string;
  /** Wide enough to carry its count inside the bar. */
  readonly labeled: boolean;
}

/** One replacement of the pool, its replaced run a link. */
export interface ReplacementLine {
  readonly runId: number;
  readonly replacedByRunId: number;
  readonly causeText: string;
}

/** One row of a battery run's member table. */
export interface MemberRow {
  readonly memberId: number;
  readonly runId: number;
  readonly label: string;
  readonly superseded: boolean;
  readonly recorded: boolean;
  readonly attempts: string;
  readonly providerErrors: string;
  readonly rate: string;
  readonly reruns: string;
}

/** A segment narrower than this share of the bar carries no count inside; its legend item does. */
const BAR_LABEL_MIN_SHARE = 0.06;

let nextUid = 0;

/**
 * The **Provider availability** section of the run and battery run dialogs' Integrity tab: the
 * candidate's provider errors as observed in GnollBench runs — a one-sentence summary, the breakdown
 * by outcome class with our side excluded, the outcome over time (strip) or by class (bar), the
 * re-runs and their lineage, the graders' errors for information and, for a battery run, one row per
 * member run. A run without the ledger (before harness 56) reads *Not recorded*.
 *
 * The method is in docs/overseer/ai-benchmark-provider-availability.md.
 */
@Component({
  selector: 'app-provider-availability',
  standalone: true,
  templateUrl: './provider-availability.component.html',
  styleUrls: ['./provider-availability.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProviderAvailabilityComponent {
  /** The run's, or the battery run's, lineage-pooled availability. */
  readonly availability = input<ProviderAvailabilityDto | null | undefined>(null);
  /** A battery run's member runs, superseded ones included; no member table without them. */
  readonly members = input<readonly BenchmarkBatteryMemberAvailabilityDto[] | null | undefined>(null);
  /** A battery run's suites, to name the member rows. */
  readonly suites = input<readonly { readonly index: number; readonly suiteName: string }[] | null | undefined>(null);

  /** A run id from a replacement or member link; the host opens that run's report. */
  readonly openRun = output<number>();

  readonly uid = `pa-${++nextUid}`;
  readonly views = PROVIDER_AVAILABILITY_VIEWS;
  readonly legend = STRIP_LEGEND;
  readonly view = signal<ProviderAvailabilityView>(readStoredProviderAvailabilityView());

  readonly recorded = computed(() => !!this.availability()?.recorded);

  /** The section's one sentence, as an observation. */
  readonly summary = computed(() => {
    const a = this.availability();
    if (!a?.recorded) {
      return NOT_RECORDED_TEXT;
    }
    let sentence = `Provider errors observed: ${availabilityHeadline(a)}`;
    const oneIn = formatOneIn(a.providerErrors, a.validAttempts);
    if (oneIn) {
      sentence += `, ${oneIn}`;
    }
    if (a.unattributedErrors > 0 && a.validAttempts > 0) {
      const inclusive = a.inclusiveRate ?? (a.providerErrors + a.unattributedErrors) / a.validAttempts;
      sentence += `; ≤ ${formatPercent(inclusive)} incl. ${formatInteger(a.unattributedErrors)} unattributed`;
    }
    return `${sentence}.`;
  });

  readonly fewAttempts = computed(() => {
    const a = this.availability();
    return !!a?.recorded && a.validAttempts > 0 && (a.fewAttempts || a.validAttempts < FEW_ATTEMPTS_THRESHOLD);
  });

  /** Which runs the figure covers, when that is not simply every run of the pool; null otherwise. */
  readonly scopeLine = computed(() => {
    const a = this.availability();
    if (!a?.recorded) {
      return null;
    }
    const parts: string[] = [];
    if (a.recordedRuns < a.totalRuns) {
      parts.push(`Recorded for ${formatInteger(a.recordedRuns)} of ${formatInteger(a.totalRuns)} runs`);
    }
    if (a.replacedRunsIncluded > 0) {
      parts.push(`includes ${formatInteger(a.replacedRunsIncluded)} replaced ${a.replacedRunsIncluded === 1 ? 'run' : 'runs'}`);
    }
    if (parts.length === 0) {
      return null;
    }
    const line = parts.join('; ');
    return `${line.charAt(0).toUpperCase()}${line.slice(1)}.`;
  });

  readonly validAttemptsText = computed(() => formatInteger(this.availability()?.validAttempts ?? 0));

  /** The valid classes, Succeeded first, then the provider's and the unattributed ones under their sides. */
  readonly validGroups = computed<readonly BreakdownGroup[]>(() => {
    const a = this.availability();
    if (!a?.recorded) {
      return [];
    }
    const sides: ProviderAttemptSide[] = ['ok', 'provider', 'unattributed'];
    return sides
      .map(side => ({
        side,
        label: side === 'ok' ? null : SIDE_LABELS[side],
        rows: this.rowsOf(a, side)
      }))
      .filter(group => group.rows.length > 0);
  });

  /** The our-side classes: excluded from the valid attempts, shown as proof of it. */
  readonly ourRows = computed<readonly BreakdownRow[]>(() => {
    const a = this.availability();
    return a?.recorded ? this.rowsOf(a, 'ours') : [];
  });

  readonly ourTotal = computed(() => formatInteger(this.availability()?.ourSideFailures ?? 0));

  readonly stripCells = computed<readonly StripCell[]>(() => {
    const a = this.availability();
    if (!a?.recorded) {
      return [];
    }
    return (a.bins ?? []).map(bin => ({
      startUtc: bin.startUtc,
      time: formatUtcMinute(bin.startUtc),
      shade: stripShadeOf(bin),
      valid: formatInteger(bin.valid),
      provider: formatInteger(bin.provider),
      unattributed: formatInteger(bin.unattributed),
      ours: formatInteger(bin.ours),
      share: bin.valid > 0 ? formatPercent(bin.provider / bin.valid) : '—'
    }));
  });

  /** The strip's ends: the first bin's start and the last bin's end, in UTC. */
  readonly stripRange = computed(() => {
    const bins = this.availability()?.bins ?? [];
    if (bins.length === 0) {
      return null;
    }
    const last = parseServerUtcDate(bins[bins.length - 1].startUtc);
    return {
      start: formatUtcMinute(bins[0].startUtc),
      end: Number.isNaN(last.getTime()) ? '' : formatUtcMinute(new Date(last.getTime() + STRIP_BIN_MS))
    };
  });

  readonly barSegments = computed<readonly BarSegment[]>(() => {
    const a = this.availability();
    if (!a?.recorded) {
      return [];
    }
    const outcomes = (a.byOutcome ?? []).filter(o => o.count > 0);
    const total = outcomes.reduce((sum, o) => sum + o.count, 0);
    return outcomes.map(o => ({
      outcome: o.outcome,
      label: outcomeLabel(o.outcome),
      side: o.side,
      count: o.count,
      countText: formatInteger(o.count),
      share: total > 0 ? formatPercent(o.count / total) : '0%',
      labeled: total > 0 && o.count / total >= BAR_LABEL_MIN_SHARE
    }));
  });

  /**
   * `3 re-runs (2 for provider errors): 2 repair passes (1 for provider errors), 1 replaced run
   * (1 for provider errors)`; the replaced runs only when there are any.
   */
  readonly rerunsText = computed(() => {
    const a = this.availability();
    if (!a) {
      return '';
    }
    const forProvider = (k: number): string => (k > 0 ? ` (${formatInteger(k)} for provider errors)` : '');
    let line = `${formatReruns(a.rerunsNeeded, a.providerRerunsNeeded)}: `
      + `${formatInteger(a.repairPasses)} ${a.repairPasses === 1 ? 'repair pass' : 'repair passes'}${forProvider(a.providerRepairPasses)}`;
    if (a.replacementRuns > 0) {
      line += `, ${formatInteger(a.replacementRuns)} replaced ${a.replacementRuns === 1 ? 'run' : 'runs'}`
        + forProvider(a.providerReplacementRuns);
    }
    return line;
  });

  readonly replacements = computed<readonly ReplacementLine[]>(() =>
    (this.availability()?.replaces ?? []).map(link => ({
      runId: link.runId,
      replacedByRunId: link.replacedByRunId,
      causeText: replacementCauseText(link.cause)
    })));

  /** `98% (1,450 of 1,480 calls)`, or `no valid calls`. */
  readonly firstAttemptText = computed(() => {
    const a = this.availability();
    if (!a || a.calls <= 0) {
      return 'no valid calls';
    }
    return `${formatPercent(a.firstAttemptSuccessCalls / a.calls)} (${formatInteger(a.firstAttemptSuccessCalls)} of `
      + `${formatInteger(a.calls)} ${a.calls === 1 ? 'call' : 'calls'})`;
  });

  readonly graderText = computed(() => {
    const a = this.availability();
    if (!a) {
      return '';
    }
    return `${formatInteger(a.graderProviderErrors)} provider ${a.graderProviderErrors === 1 ? 'error' : 'errors'} in `
      + `${formatInteger(a.graderValidAttempts)} grader ${a.graderValidAttempts === 1 ? 'attempt' : 'attempts'}; `
      + 'for information, not counted above';
  });

  readonly memberRows = computed<readonly MemberRow[]>(() => {
    const suites = this.suites() ?? [];
    return [...(this.members() ?? [])]
      .sort((x, y) => x.suiteIndex - y.suiteIndex || x.round - y.round || Number(x.superseded) - Number(y.superseded) || x.runId - y.runId)
      .map(m => {
        const suite = suites.find(s => s.index === m.suiteIndex)?.suiteName ?? `Suite ${m.suiteIndex + 1}`;
        const recorded = m.recorded && m.validAttempts != null;
        const reruns = m.rerunsNeeded;
        return {
          memberId: m.memberId,
          runId: m.runId,
          label: `${suite}, round ${m.round}`,
          superseded: m.superseded,
          recorded,
          attempts: recorded ? formatInteger(m.validAttempts ?? 0) : NOT_RECORDED_TEXT,
          providerErrors: recorded ? formatInteger(m.providerErrors ?? 0) : '—',
          rate: recorded && m.providerErrorRate != null ? formatPercent(m.providerErrorRate) : '—',
          reruns: reruns == null ? '—' : compactReruns(reruns, m.providerRerunsNeeded ?? 0)
        };
      });
  });

  selectView(view: ProviderAvailabilityView): void {
    this.view.set(view);
    storeProviderAvailabilityView(view);
  }

  onViewKeydown(event: KeyboardEvent, index: number): void {
    const target = rovingTabIndex(event, index, this.views.length);
    if (target === null) return;
    const next = this.views[target].key;
    this.selectView(next);
    document.getElementById(`${this.uid}-tab-${next}`)?.focus();
  }

  private rowsOf(a: ProviderAvailabilityDto, side: ProviderAttemptSide): BreakdownRow[] {
    const n = a.validAttempts;
    return (a.byOutcome ?? [])
      .filter((o: ProviderAvailabilityOutcomeDto) => o.side === side)
      .map(o => ({
        outcome: o.outcome,
        label: outcomeLabel(o.outcome),
        count: formatInteger(o.count),
        share: side !== 'ours' && n > 0 ? formatPercent(o.count / n) : '—',
        statuses: (o.httpStatuses ?? []).length > 0 ? o.httpStatuses.map(s => `HTTP ${s}`).join(', ') : '—'
      }));
  }
}
