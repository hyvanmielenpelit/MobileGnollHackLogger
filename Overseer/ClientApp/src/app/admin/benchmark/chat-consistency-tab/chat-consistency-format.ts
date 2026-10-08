/**
 * Number, date and label formatting for the Chat Consistency tab. Pure and locale-invariant: every
 * date is UTC, every number is written with `toFixed` and a period as the decimal mark, so a figure
 * reads the same in every browser, in the UI and in the uploaded charts.
 */

import {
  CcAnnotationKind,
  CcAxis,
  CcBatteryRunStatus,
  CcEndpointResult,
  CcGrade,
  CcInterval,
  CcNumber,
  CcRegradeJobStatus,
  CcRunRow,
  CcRunStatus,
  CcServedModelCount,
  CcVerdict,
  CC_ANNOTATION_KINDS
} from './chat-consistency.models';

/** The typographic minus, so a negative value lines up with the plus of a positive one. */
export const MINUS = '−';

/** The placeholder of a value that is absent or not a finite number. */
export const NO_VALUE = '—';

const MS_PER_DAY = 86_400_000;

// --- Numbers ---

/** A finite number from a JSON number or a named literal; null for `NaN`, `±Infinity`, null and anything else. */
export function ccNumber(value: CcNumber | null | undefined): number | null {
  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : null;
  }
  if (typeof value === 'string' && value.trim() !== '') {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
}

/** `value` rounded to `digits` decimals, with the typographic minus. */
export function formatFixed(value: CcNumber | null | undefined, digits = 1): string {
  const n = ccNumber(value);
  if (n === null) return NO_VALUE;
  const text = Math.abs(n).toFixed(digits);
  // A value that rounds to zero is written without a sign.
  return n < 0 && Number(text) !== 0 ? MINUS + text : text;
}

/** `value` with an explicit sign: `+1.2`, `−0.5`; zero has none. */
export function formatSigned(value: CcNumber | null | undefined, digits = 1): string {
  const n = ccNumber(value);
  if (n === null) return NO_VALUE;
  const text = Math.abs(n).toFixed(digits);
  if (Number(text) === 0) return text;
  return (n < 0 ? MINUS : '+') + text;
}

/** An integer with comma thousands separators: `12,345`. */
export function formatInteger(value: CcNumber | null | undefined): string {
  const n = ccNumber(value);
  if (n === null) return NO_VALUE;
  const rounded = Math.round(Math.abs(n)).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  return n < 0 && Math.round(n) !== 0 ? MINUS + rounded : rounded;
}

/** `value` rounded to `digits` decimals, with comma thousands separators and the typographic minus: `12,345.6`. */
export function formatGrouped(value: CcNumber | null | undefined, digits: number): string {
  const text = formatFixed(value, digits);
  if (text === NO_VALUE) return text;
  const sign = text.startsWith(MINUS) ? MINUS : '';
  const [whole, fraction] = text.slice(sign.length).split('.');
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  return sign + grouped + (fraction === undefined ? '' : `.${fraction}`);
}

/** A fraction as a percentage: `0.025` → `2.5 %`. */
export function formatFractionPercent(fraction: CcNumber | null | undefined, digits = 1): string {
  const n = ccNumber(fraction);
  return n === null ? NO_VALUE : `${formatFixed(n * 100, digits)} %`;
}

/** A percentage with its sign: `4.2` → `+4.2 %`. */
export function formatSignedPercent(percent: CcNumber | null | undefined, digits = 1): string {
  const n = ccNumber(percent);
  return n === null ? NO_VALUE : `${formatSigned(n, digits)} %`;
}

/** Milliseconds: `850 ms` below a second, `2.4 s` from one, the seconds to `secondsDigits` decimals. */
export function formatMs(ms: CcNumber | null | undefined, secondsDigits = 1): string {
  const n = ccNumber(ms);
  if (n === null) return NO_VALUE;
  return Math.abs(n) < 1000 ? `${formatFixed(n, 0)} ms` : `${formatFixed(n / 1000, secondsDigits)} s`;
}

/** A decode rate in tokens per second. */
export function formatTokenRate(rate: CcNumber | null | undefined, digits = 1): string {
  const n = ccNumber(rate);
  return n === null ? NO_VALUE : `${formatFixed(n, digits)} tok/s`;
}

/**
 * US dollars to `digits` decimals; without `digits`, with enough decimals that a cent-sized cost
 * does not read as zero.
 */
export function formatUsd(value: CcNumber | null | undefined, digits?: number): string {
  const n = ccNumber(value);
  if (n === null) return NO_VALUE;
  const abs = Math.abs(n);
  const places = digits ?? (abs >= 1 ? 2 : abs >= 0.01 ? 3 : 4);
  return `${n < 0 ? MINUS : ''}$${abs.toFixed(places)}`;
}

/** A p-value: `< 0.001` below it, else three decimals. */
export function formatPValue(p: CcNumber | null | undefined): string {
  const n = ccNumber(p);
  if (n === null) return NO_VALUE;
  return n < 0.001 ? '< 0.001' : n.toFixed(3);
}

/** An interval as `[lower, upper]`, each bound formatted by `format`. */
export function formatInterval(
  interval: CcInterval | null | undefined,
  format: (value: number) => string = value => formatSigned(value, 1)
): string {
  if (!interval) return NO_VALUE;
  const lower = ccNumber(interval.lower);
  const upper = ccNumber(interval.upper);
  if (lower === null || upper === null) return NO_VALUE;
  return `[${format(lower)}, ${format(upper)}]`;
}

/**
 * An endpoint's estimate as the verdict table shows it: index points for a difference endpoint, a
 * signed percentage for a log-ratio one, with the 95 % interval in the same unit.
 */
export function endpointEstimateText(endpoint: CcEndpointResult): string {
  if (!endpoint.computed) return 'Not computed';
  if (endpoint.scale === 'logRatio') {
    const estimate = formatSignedPercent(endpoint.estimatePercent);
    const ci = formatInterval(endpoint.ci95Percent, value => formatSigned(value, 1));
    return ci === NO_VALUE ? estimate : `${estimate} (95 % CI ${ci} %)`;
  }
  const unit = endpoint.unit ? ` ${endpoint.unit}` : '';
  const estimate = formatSigned(endpoint.estimate, 1);
  const ci = formatInterval(endpoint.ci95);
  return ci === NO_VALUE ? `${estimate}${unit}` : `${estimate}${unit} (95 % CI ${ci})`;
}

/** The minimum detectable effect in the endpoint's reporting unit. */
export function endpointMdeText(endpoint: CcEndpointResult): string {
  if (endpoint.scale === 'logRatio') {
    const percent = ccNumber(endpoint.minimumDetectableEffectPercent);
    return percent === null ? NO_VALUE : `±${formatFixed(Math.abs(percent), 1)} %`;
  }
  const mde = ccNumber(endpoint.minimumDetectableEffect);
  return mde === null ? NO_VALUE : `±${formatFixed(Math.abs(mde), 1)}${endpoint.unit ? ` ${endpoint.unit}` : ''}`;
}

// --- Dates ---

/** A server time as a Date; a value without an offset is UTC. */
export function parseUtc(value: string | Date): Date {
  if (value instanceof Date) return value;
  const text = value.trim();
  const hasOffset = /([zZ]|[+-]\d{2}:?\d{2})$/.test(text);
  const hasTime = text.includes('T');
  return new Date(hasOffset ? text : hasTime ? `${text}Z` : `${text}T00:00:00Z`);
}

/** Epoch milliseconds of a server time, or NaN when it does not parse. */
export function utcMillis(value: string | Date | null | undefined): number {
  if (value === null || value === undefined || value === '') return Number.NaN;
  return parseUtc(value).getTime();
}

function pad(value: number, width = 2): string {
  return value.toString().padStart(width, '0');
}

/** `2026-10-03`, the UTC date. */
export function formatUtcDate(value: string | Date | number | null | undefined): string {
  if (value === null || value === undefined || value === '') return NO_VALUE;
  const date = typeof value === 'number' ? new Date(value) : parseUtc(value);
  if (Number.isNaN(date.getTime())) return NO_VALUE;
  return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
}

/** `2026-10-03 14:05 UTC`. */
export function formatUtcDateTime(value: string | Date | number | null | undefined): string {
  if (value === null || value === undefined || value === '') return NO_VALUE;
  const date = typeof value === 'number' ? new Date(value) : parseUtc(value);
  if (Number.isNaN(date.getTime())) return NO_VALUE;
  return `${formatUtcDate(date)} ${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())} UTC`;
}

/** A `yyyy-MM-dd` date input's value is a valid calendar date. */
export function isUtcDateInput(value: string | null | undefined): boolean {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const date = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(date.getTime()) && formatUtcDate(date) === value;
}

/** The first instant of a `yyyy-MM-dd` UTC day, as an ISO string; null for an invalid date. */
export function startOfUtcDay(day: string | null | undefined): string | null {
  return isUtcDateInput(day) ? `${day}T00:00:00.000Z` : null;
}

/** The last millisecond of a `yyyy-MM-dd` UTC day, as an ISO string; null for an invalid date. */
export function endOfUtcDay(day: string | null | undefined): string | null {
  return isUtcDateInput(day) ? `${day}T23:59:59.999Z` : null;
}

/** `day` moved by `days` calendar days. */
export function addUtcDays(day: string, days: number): string {
  const date = new Date(`${day}T00:00:00Z`);
  return formatUtcDate(new Date(date.getTime() + days * MS_PER_DAY));
}

/** A UTC instant falls within the inclusive `yyyy-MM-dd` day range. */
export function withinUtcDays(value: string, startDay: string, endDay: string): boolean {
  const at = utcMillis(value);
  const start = utcMillis(startOfUtcDay(startDay));
  const end = utcMillis(endOfUtcDay(endDay));
  return Number.isFinite(at) && Number.isFinite(start) && Number.isFinite(end) && at >= start && at <= end;
}

/** A `datetime-local` value (`yyyy-MM-ddTHH:mm`) read as UTC, as an ISO string; null when it does not parse. */
export function utcDateTimeInputToIso(value: string | null | undefined): string | null {
  if (!value || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?$/.test(value)) return null;
  const date = new Date(`${value.length === 16 ? `${value}:00` : value}Z`);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

// --- Labels ---

const VERDICT_LABELS: Record<CcVerdict, string> = {
  changedDegraded: 'Degraded',
  changedImproved: 'Improved',
  changedNegligible: 'Changed, negligible',
  equivalent: 'Equivalent',
  inconclusive: 'Inconclusive'
};

/** A verdict in sentence case; the server's own label (`more work`) wins where it differs. */
export function verdictText(verdict: CcVerdict | null | undefined, serverLabel?: string | null): string {
  const label = serverLabel?.trim();
  if (label) return label.charAt(0).toUpperCase() + label.slice(1);
  return verdict ? VERDICT_LABELS[verdict] ?? verdict : 'Not computable';
}

const GRADE_LABELS: Record<CcGrade, string> = {
  established: 'Established',
  indicated: 'Indicated',
  notEstablished: 'Not established'
};

export function gradeText(grade: CcGrade | string | null | undefined): string {
  return (grade && GRADE_LABELS[grade as CcGrade]) || 'Not established';
}

const AXIS_LABELS: Record<CcAxis, string> = {
  quality: 'Quality',
  speedTelemetry: 'Speed (telemetry)',
  speedLegacy: 'Speed (legacy)',
  work: 'Work',
  cost: 'Cost'
};

export function axisText(axis: CcAxis | string): string {
  return AXIS_LABELS[axis as CcAxis] ?? axis;
}

const RUN_STATUS_LABELS: Record<CcRunStatus, string> = {
  running: 'Running',
  completed: 'Completed',
  completedWithErrors: 'Completed with errors',
  failed: 'Failed',
  canceled: 'Canceled',
  completedWithLimits: 'Completed with limits'
};

export function runStatusText(status: CcRunStatus | string | null | undefined): string {
  return (status && RUN_STATUS_LABELS[status as CcRunStatus]) || String(status ?? '');
}

const BATTERY_RUN_STATUS_LABELS: Record<CcBatteryRunStatus, string> = {
  pending: 'Pending',
  running: 'Running',
  waitingForCap: 'Waiting for run cap',
  stopped: 'Stopped',
  completed: 'Completed',
  completedWithErrors: 'Completed with errors',
  cancelled: 'Canceled',
  failed: 'Failed'
};

/** A battery run's status as the UI shows it; a PascalCase name reads the same as its camelCase one. */
export function batteryRunStatusText(status: CcBatteryRunStatus | string | null | undefined): string {
  if (!status) return '';
  const key = status.charAt(0).toLowerCase() + status.slice(1);
  return BATTERY_RUN_STATUS_LABELS[key as CcBatteryRunStatus] ?? status;
}

export function annotationKindText(kind: CcAnnotationKind | string): string {
  return CC_ANNOTATION_KINDS.find(entry => entry.kind === kind)?.label ?? String(kind);
}

const REGRADE_STATUS_LABELS: Record<CcRegradeJobStatus, string> = {
  running: 'Re-grading',
  completed: 'Completed',
  completedWithErrors: 'Completed with errors',
  canceled: 'Canceled',
  failed: 'Failed'
};

export function regradeStatusText(status: string | null | undefined): string {
  return (status && REGRADE_STATUS_LABELS[status as CcRegradeJobStatus]) || String(status ?? '');
}

// --- Run table cells ---

/** The served model ids of a run, most calls first: `gpt-5-2026-08 (40 calls)`. */
export function servedModelsText(served: readonly CcServedModelCount[] | null | undefined): string {
  const list = [...(served ?? [])].sort((a, b) => b.callCount - a.callCount);
  if (list.length === 0) return 'Not reported';
  return list.map(entry => `${entry.modelId} (${plural(entry.callCount, 'call')})`).join(', ');
}

/** A run's measurement segment: one number while every eligible axis shares it, else one per axis. */
export function segmentText(row: CcRunRow): string {
  const eligible = row.eligibility.filter(entry => entry.eligible && entry.segment !== null);
  if (eligible.length === 0) return NO_VALUE;
  const segments = new Set(eligible.map(entry => entry.segment));
  if (segments.size === 1) return String(eligible[0].segment);
  return eligible.map(entry => `${axisText(entry.axis)} ${entry.segment}`).join(' · ');
}

/** Which common-grader re-grades cover a run. */
export function regradeCoverageText(row: CcRunRow): string {
  if (row.regradeCoverage.length === 0) return 'Native grades only';
  return row.regradeCoverage
    .map(coverage => `Re-graded by ${coverage.display} (${formatUtcDate(coverage.latestAtUtc)})`)
    .join('; ');
}

/** The matched control runs of a run. */
export function controlRunsText(row: CcRunRow): string {
  return row.matchedControlRunIds.length === 0 ? 'None' : row.matchedControlRunIds.map(id => `#${id}`).join(', ');
}

/** A run, or a battery run, counts as eligible for an analysis when at least one axis can use it. */
export function isRunEligible(row: Pick<CcRunRow, 'eligibility'>): boolean {
  return row.eligibility.some(entry => entry.eligible);
}

/** `1 run`, `3 runs`. */
export function plural(count: number, singular: string, pluralForm = `${singular}s`): string {
  return `${formatInteger(count)} ${count === 1 ? singular : pluralForm}`;
}
