/**
 * The Results step's reading of a stored analysis: the overall outcome, each endpoint's status, notes,
 * estimate and interval bar, the not-computable endpoints by reason, the key figures, the next runs as
 * cards and the analyzed units as period cards. Pure; every function reads only the stored result and
 * the rows it is given, so a saved analysis reads the same whatever step 3 shows.
 */

import { formatThinkingLevel } from '../../../utils/model-badge-format.util';
import {
  NO_VALUE,
  ccNumber,
  formatFixed,
  formatInteger,
  formatSigned,
  formatSignedPercent,
  formatUtcDate,
  gradeText,
  plural,
  utcMillis,
  verdictText
} from './chat-consistency-format';
import { CcPeriodIds, CcPeriodUnit, CcUnitPeriod } from './chat-consistency-periods';
import {
  CC_BATTERY_SET_PREFIX,
  CC_SUITE_SET_PREFIX,
  CcAnalysisFreshness,
  CcAnalysisResult,
  CcBatteryRunRow,
  CcEndpointBrief,
  CcEndpointResult,
  CcRunRow,
  CcUnitView,
  CcVerdict
} from './chat-consistency.models';

// --- Model name ---

/**
 * The subject's display name without its trailing ` (<level>)` when that is the thinking level the
 * badge beside it shows; any other name is returned unchanged.
 */
export function ccModelBaseName(displayName: string, thinkingLevel: string | null): string {
  const match = /^(.*\S)\s*\(([^()]*)\)\s*$/.exec(displayName);
  if (!match) return displayName;
  const inner = match[2].trim().toLowerCase();
  const levels = [thinkingLevel?.trim().toLowerCase(), formatThinkingLevel(thinkingLevel).toLowerCase()];
  return inner !== '' && levels.includes(inner) ? match[1] : displayName;
}

// --- Overall outcome ---

export type CcOutcome = 'changed' | 'noChange' | 'undecided' | 'noneComputed';

/** The verdict banner's title and the line under it. */
export interface CcOverallOutcome {
  kind: CcOutcome;
  title: string;
  detail: string;
}

const CHANGE_VERDICTS: readonly CcVerdict[] = ['changedDegraded', 'changedImproved'];
const WITHIN_VERDICTS: readonly CcVerdict[] = ['equivalent', 'changedNegligible'];

function isChange(endpoint: CcEndpointResult): boolean {
  return endpoint.computed && endpoint.verdict !== null && CHANGE_VERDICTS.includes(endpoint.verdict);
}

function isWithin(endpoint: CcEndpointResult): boolean {
  return endpoint.computed && endpoint.verdict !== null && WITHIN_VERDICTS.includes(endpoint.verdict);
}

/**
 * The analysis's outcome from the server's verdicts alone: a decisive change on any endpoint; no change
 * on every computed one; computed endpoints but none decisive; or nothing computed. The title never
 * names a cause, and says *on the computed endpoints* while any endpoint is not computable.
 */
export function ccOverallOutcome(result: CcAnalysisResult): CcOverallOutcome {
  const endpoints = result.endpoints;
  const total = endpoints.length;
  const computed = endpoints.filter(endpoint => endpoint.computed);
  const notComputable = total - computed.length;
  const changes = endpoints.filter(isChange);
  if (changes.length > 0) {
    return {
      kind: 'changed',
      title: 'The chat changed',
      detail: changes
        .map(endpoint => `${endpoint.name}: ${verdictText(endpoint.verdict, endpoint.verdictLabel).toLowerCase()}`
          + ` (${gradeText(endpoint.grade).toLowerCase()})`)
        .join(' · ')
    };
  }
  if (computed.length === 0) {
    return { kind: 'noneComputed', title: 'Nothing could be computed', detail: 'See why under Verdicts' };
  }
  const within = computed.filter(isWithin).length;
  if (within === computed.length) {
    return {
      kind: 'noChange',
      title: notComputable === 0 ? 'No meaningful change' : 'No change on the computed endpoints',
      detail: `${within} of ${total} endpoints within their margins`
        + (notComputable > 0 ? ` · ${notComputable} not computable` : '')
    };
  }
  const open = computed.length - within;
  return {
    kind: 'undecided',
    title: 'Not enough evidence yet',
    detail: within === 0
      ? `${computed.length} of ${total} endpoints computed, none decisive`
      : `${computed.length} of ${total} endpoints computed: ${within} within ${within === 1 ? 'its margin' : 'their margins'}, `
        + `${open} inconclusive`
  };
}

// --- Endpoint status ---

export type CcEndpointStatus = 'changed' | 'improved' | 'within' | 'inconclusive' | 'notComputable';

/** The visible word of each endpoint status. */
export const CC_ENDPOINT_STATUS_TEXT: Readonly<Record<CcEndpointStatus, string>> = Object.freeze({
  changed: 'Changed',
  improved: 'Improved',
  within: 'Within margin',
  inconclusive: 'Inconclusive',
  notComputable: 'Not computable'
});

/**
 * The one mapping from a verdict to a status: a degradation is `changed`, an improvement `improved`, an
 * equivalence or a negligible change `within`. Work per turn has no better or worse, so both of its
 * changes are `changed`. A computed endpoint without a verdict reads as `inconclusive`.
 */
function statusOf(computed: boolean, verdict: CcVerdict | null, work: boolean): CcEndpointStatus {
  if (!computed) return 'notComputable';
  switch (verdict) {
    case 'changedDegraded':
      return 'changed';
    case 'changedImproved':
      return work ? 'changed' : 'improved';
    case 'equivalent':
    case 'changedNegligible':
      return 'within';
    default:
      return 'inconclusive';
  }
}

/** An endpoint's status, from its verdict and whether it is work per turn (`direction === 'work'`). */
export function ccEndpointStatus(endpoint: CcEndpointResult): CcEndpointStatus {
  return statusOf(endpoint.computed, endpoint.verdict, endpoint.direction === 'work');
}

/** The status word of an endpoint; a change in work per turn reads as the server's *More work* / *Less work*. */
export function ccEndpointStatusText(endpoint: CcEndpointResult): string {
  const status = ccEndpointStatus(endpoint);
  if (status === 'changed' && endpoint.direction === 'work') return verdictText(endpoint.verdict, endpoint.verdictLabel);
  return CC_ENDPOINT_STATUS_TEXT[status];
}

/** The server's verdict labels (`ChatConsistencyAnalysisService.VerdictLabel`), read back as a verdict. */
const VERDICT_OF_LABEL: Readonly<Record<string, { verdict: CcVerdict; work: boolean }>> = Object.freeze({
  'degraded': { verdict: 'changedDegraded', work: false },
  'improved': { verdict: 'changedImproved', work: false },
  'more work': { verdict: 'changedDegraded', work: true },
  'less work': { verdict: 'changedImproved', work: true },
  'changed, negligible': { verdict: 'changedNegligible', work: false },
  'equivalent': { verdict: 'equivalent', work: false },
  'inconclusive': { verdict: 'inconclusive', work: false }
});

function verdictOfLabel(label: string | null | undefined): { verdict: CcVerdict; work: boolean } | null {
  return VERDICT_OF_LABEL[(label ?? '').trim().toLowerCase()] ?? null;
}

/** A summary endpoint's status, by the same mapping as {@link ccEndpointStatus}; an unknown label reads as `inconclusive`. */
export function ccEndpointBriefStatus(endpoint: CcEndpointBrief): CcEndpointStatus {
  const read = verdictOfLabel(endpoint.verdictLabel);
  return statusOf(endpoint.computed, read?.verdict ?? null, read?.work ?? false);
}

/** The status word of a summary endpoint; a change in work per turn reads as *More work* / *Less work*. */
export function ccEndpointBriefStatusText(endpoint: CcEndpointBrief): string {
  const status = ccEndpointBriefStatus(endpoint);
  if (status === 'changed' && verdictOfLabel(endpoint.verdictLabel)?.work) return verdictText(null, endpoint.verdictLabel);
  return CC_ENDPOINT_STATUS_TEXT[status];
}

// --- Compared set ---

/** What a saved analysis compared, as the `cc-kind-tag` names it. */
export interface CcCompareKind {
  /** `battery`, `suite` or `all`: the tag's `data-kind`. */
  kind: 'battery' | 'suite' | 'all';
  /** `Battery`, `Suite` or `All suites`. */
  text: string;
}

/** The compared set's kind from its key: a battery, a suite, or every suite run by run when there is none. */
export function ccCompareKindOf(setKey: string | null | undefined): CcCompareKind {
  if (setKey?.startsWith(CC_BATTERY_SET_PREFIX)) return { kind: 'battery', text: 'Battery' };
  if (setKey?.startsWith(CC_SUITE_SET_PREFIX)) return { kind: 'suite', text: 'Suite' };
  return { kind: 'all', text: 'All suites' };
}

// --- Freshness ---

/** What an out-of-date analysis is, as the notices' info tip says it. */
export const CC_OUT_OF_DATE_RULE =
  'An analysis is out of date when it was saved under an earlier analysis code version, or when its inputs changed '
  + 'after it was saved. Saved analyses never change.';

/** The out-of-date notice's closing advice. */
export const CC_OUT_OF_DATE_ADVICE =
  'Saved analyses never change. Analyze again for a current analysis with the same settings; this one stays in '
  + 'Analysis history as a record.';

/** The reason sentence of a changed input. */
export const CC_INPUTS_CHANGED_SENTENCE = 'Its runs, grades, controls, annotations or prices changed after it was saved.';

/** The reason sentence of an earlier analysis code version. */
export function ccEarlierCodeSentence(savedVersion: number, currentVersion: number): string {
  return `Saved under analysis code version ${savedVersion}; Overseer now analyzes under version ${currentVersion}.`;
}

/** Why an analysis is out of date, one sentence per reason; empty when it is current. */
export function ccOutOfDateReasons(freshness: CcAnalysisFreshness | null | undefined): string[] {
  if (!freshness?.outOfDate) return [];
  const reasons: string[] = [];
  if (freshness.earlierAnalysisCode) {
    reasons.push(ccEarlierCodeSentence(freshness.analysisCodeVersion, freshness.currentAnalysisCodeVersion));
  }
  if (freshness.inputsChanged === true) reasons.push(CC_INPUTS_CHANGED_SENTENCE);
  return reasons;
}

/**
 * The quiet line of a current-code analysis whose inputs could not be checked; null when they were
 * checked, when the code is earlier, or when the server gave no reason.
 */
export function ccInputsUncheckedText(freshness: CcAnalysisFreshness | null | undefined): string | null {
  if (!freshness || freshness.earlierAnalysisCode || freshness.inputsChanged !== null) return null;
  const note = freshness.inputsNote?.trim();
  return note ? `Changes since saving could not be checked: ${note}` : null;
}

// --- Endpoint notes ---

/** An endpoint's verdict in plain language, and its remaining notes, each said once. */
export interface CcEndpointNotes {
  meaning: string | null;
  notes: string[];
}

const REPEATED_PREFIX = /^(not computable|inconclusive):\s*/i;
const MEANING_PREFIX = /^(inconclusive|equivalent|degraded|improved|changed, negligible|negligible|changed|more work|less work):\s*/i;

/** The text two notes are compared by: without a repeated verdict prefix, case, spacing or a final period. */
function noteKey(text: string): string {
  return text.trim().replace(REPEATED_PREFIX, '').replace(/\.$/, '').replace(/\s+/g, ' ').trim().toLowerCase();
}

function capitalized(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/**
 * An endpoint's notes in order: why it is not computable, the legacy data or proxy, the grader, the
 * relaxed pooling, the minimum-sample shortfall, the detectable-effect note, then the grade reasons. A
 * note that repeats an earlier one once `Not computable: ` or `Inconclusive: ` is stripped is dropped,
 * and the first grade reason that states the verdict (`Inconclusive: …`) becomes `meaning`, without
 * its prefix.
 */
export function ccEndpointNotes(endpoint: CcEndpointResult): CcEndpointNotes {
  const raw: string[] = [];
  if (!endpoint.computed && endpoint.notComputedReason) raw.push(endpoint.notComputedReason);
  if (endpoint.legacyProxy) raw.push('Measured with the legacy proxy (model time per answer), not telemetry.');
  else if (endpoint.usesLegacyData) raw.push('Some compared runs have no call telemetry.');
  if (endpoint.commonGrader) raw.push('Graded by a common grader.');
  else if (endpoint.id === 'P1' && endpoint.computed) raw.push('Native grades; no common grader covers every run.');
  if (endpoint.relaxedPooling) raw.push('Pooled across a measurement segment boundary.');
  if (!endpoint.minimumSampleMet && endpoint.minimumSampleDetail) raw.push(`Below the minimum sample: ${endpoint.minimumSampleDetail}`);
  if (endpoint.minimumDetectableEffectNote) raw.push(endpoint.minimumDetectableEffectNote);

  let meaning: string | null = null;
  const seen = new Set<string>();
  const notes: string[] = [];
  const add = (text: string) => {
    const key = noteKey(text);
    if (key === '' || seen.has(key)) return;
    seen.add(key);
    notes.push(text.trim());
  };
  raw.forEach(add);
  for (const reason of endpoint.gradeReasons) {
    const text = reason.trim();
    if (meaning === null && MEANING_PREFIX.test(text)) {
      const plain = text.replace(MEANING_PREFIX, '').trim();
      if (plain !== '') {
        meaning = capitalized(plain);
        seen.add(noteKey(plain));
        continue;
      }
    }
    add(text);
  }
  return { meaning, notes };
}

// --- Estimate and interval ---

/** An endpoint's estimate and its 95 % interval as separate texts. */
export interface CcEstimateParts {
  estimate: string;
  /** `−19.7 to +6.8 %`; null when the endpoint is not computed or has no interval. */
  interval: string | null;
}

function isLogRatio(endpoint: CcEndpointResult): boolean {
  return endpoint.scale === 'logRatio';
}

/** The unit written after a value: ` %` on a log-ratio endpoint, ` index points` and the like otherwise. */
function unitSuffix(endpoint: CcEndpointResult): string {
  if (isLogRatio(endpoint)) return ' %';
  return endpoint.unit ? ` ${endpoint.unit}` : '';
}

function intervalBounds(endpoint: CcEndpointResult): { lower: number; upper: number } | null {
  const interval = isLogRatio(endpoint) ? endpoint.ci95Percent : endpoint.ci95;
  if (!interval) return null;
  const lower = ccNumber(interval.lower);
  const upper = ccNumber(interval.upper);
  return lower === null || upper === null ? null : { lower, upper };
}

function estimateValue(endpoint: CcEndpointResult): number | null {
  return ccNumber(isLogRatio(endpoint) ? endpoint.estimatePercent : endpoint.estimate);
}

/**
 * The estimate alone (`+4.2 %`, `+1.2 index points`) and the 95 % interval alone (`−19.7 to +6.8 %`),
 * in the number formatting of `endpointEstimateText`; *Not computed* and no interval when not computed.
 */
export function ccEstimateParts(endpoint: CcEndpointResult): CcEstimateParts {
  if (!endpoint.computed) return { estimate: 'Not computed', interval: null };
  const suffix = unitSuffix(endpoint);
  let estimate: string;
  if (isLogRatio(endpoint)) {
    estimate = formatSignedPercent(endpoint.estimatePercent);
  } else {
    const text = formatSigned(endpoint.estimate, 1);
    estimate = text === NO_VALUE ? text : `${text}${suffix}`;
  }
  const bounds = intervalBounds(endpoint);
  const interval = bounds === null ? null : `${formatSigned(bounds.lower, 1)} to ${formatSigned(bounds.upper, 1)}${suffix}`;
  return { estimate, interval };
}

/**
 * The interval bar of a computed endpoint, on an axis symmetric around zero. Positions are percentages
 * (0–100) of the bar's width; values are in the endpoint's reporting unit (percent on a log-ratio
 * endpoint, whose margin band is `(exp(±margin) − 1) × 100` and therefore slightly asymmetric).
 */
export interface CcIntervalGeometry {
  /** Half the axis, in the reporting unit: the largest of the bounds and the margin, plus 15 %. */
  axisHalfWidth: number;
  marginStart: number;
  marginEnd: number;
  zero: number;
  lower: number;
  upper: number;
  estimate: number;
  estimateValue: number;
  lowerValue: number;
  upperValue: number;
  /** The margin band's ends in the reporting unit. */
  marginLowerValue: number;
  marginUpperValue: number;
  /** The margin as `marginText` states it: `(exp(margin) − 1) × 100` on a log-ratio endpoint, `margin` otherwise. */
  marginValue: number;
  /** ` %`, or ` index points` and the like. */
  unitSuffix: string;
  /** Part of the interval lies outside the margin band. */
  crossesMargin: boolean;
  /** The whole interval lies inside the margin band. */
  withinMargin: boolean;
  /** The bar's accessible reading. */
  label: string;
}

/** The interval bar of an endpoint; null when it is not computed, or its estimate, interval or margin is missing. */
export function ccIntervalGeometry(endpoint: CcEndpointResult): CcIntervalGeometry | null {
  if (!endpoint.computed) return null;
  const estimate = estimateValue(endpoint);
  const bounds = intervalBounds(endpoint);
  const margin = ccNumber(endpoint.margin);
  if (estimate === null || bounds === null || margin === null || margin <= 0) return null;
  const log = isLogRatio(endpoint);
  const marginLowerValue = log ? (Math.exp(-margin) - 1) * 100 : -margin;
  const marginUpperValue = log ? (Math.exp(margin) - 1) * 100 : margin;
  const lowerValue = Math.min(bounds.lower, bounds.upper);
  const upperValue = Math.max(bounds.lower, bounds.upper);
  const half = Math.max(Math.abs(lowerValue), Math.abs(upperValue), Math.abs(marginLowerValue),
    Math.abs(marginUpperValue), Math.abs(estimate)) * 1.15;
  const position = (value: number) => Math.min(100, Math.max(0, ((value + half) / (2 * half)) * 100));
  const withinMargin = lowerValue >= marginLowerValue && upperValue <= marginUpperValue;
  const suffix = unitSuffix(endpoint);
  const intervalText = log
    ? `${formatSigned(lowerValue, 1)}${suffix} to ${formatSigned(upperValue, 1)}${suffix}`
    : `${formatSigned(lowerValue, 1)} to ${formatSigned(upperValue, 1)}${suffix}`;
  const marginText = (endpoint.marginText || '').trim() || `±${formatFixed(marginUpperValue, 1)}${suffix}`;
  const label = `Estimate ${formatSigned(estimate, 1)}${suffix}, 95 % interval ${intervalText}, margin ${marginText}. `
    + (withinMargin ? 'The whole interval lies inside the margin.' : 'The interval reaches beyond the margin.');
  return {
    axisHalfWidth: half,
    marginStart: position(marginLowerValue),
    marginEnd: position(marginUpperValue),
    zero: position(0),
    lower: position(lowerValue),
    upper: position(upperValue),
    estimate: position(estimate),
    estimateValue: estimate,
    lowerValue,
    upperValue,
    marginLowerValue,
    marginUpperValue,
    marginValue: marginUpperValue,
    unitSuffix: suffix,
    crossesMargin: !withinMargin,
    withinMargin,
    label
  };
}

// --- Not-computable endpoints ---

/** The not-computable endpoints that share one reason. */
export interface CcNotComputableGroup {
  reason: string;
  endpoints: { id: string; name: string }[];
}

/** The reason an endpoint is not computable, without a `Not computable: ` prefix. */
function notComputedReasonOf(endpoint: CcEndpointResult): string {
  const stated = endpoint.notComputedReason?.trim()
    || endpoint.gradeReasons.find(reason => /^not computable:/i.test(reason.trim()))?.trim()
    || '';
  const reason = stated.replace(REPEATED_PREFIX, '').trim();
  return reason === '' ? 'No reason was recorded.' : capitalized(reason);
}

/** The not-computable endpoints grouped by their reason, groups and endpoints in endpoint order. */
export function ccNotComputableGroups(endpoints: readonly CcEndpointResult[]): CcNotComputableGroup[] {
  const groups: CcNotComputableGroup[] = [];
  const byKey = new Map<string, CcNotComputableGroup>();
  for (const endpoint of endpoints) {
    if (endpoint.computed) continue;
    const reason = notComputedReasonOf(endpoint);
    const key = noteKey(reason);
    let group = byKey.get(key);
    if (!group) {
      group = { reason, endpoints: [] };
      byKey.set(key, group);
      groups.push(group);
    }
    group.endpoints.push({ id: endpoint.id, name: endpoint.name });
  }
  return groups;
}

// --- Next runs ---

export type CcNextRunKind = 'checkpoint' | 'stratum' | 'control' | 'regrade';

/** The next-run kinds in card order, with a card's title and the word a count of them takes. */
export const CC_NEXT_RUN_KINDS: readonly { readonly kind: CcNextRunKind; readonly title: string; readonly countText: string }[] = [
  { kind: 'checkpoint', title: 'Another run of the model', countText: 'of the model' },
  { kind: 'stratum', title: 'A run at another time of day', countText: 'at another time of day' },
  { kind: 'control', title: 'A control run', countText: 'control' },
  { kind: 'regrade', title: 'A re-grade', countText: 're-grade' }
];

const PERIOD_ORDER = ['baseline', 'comparison'];

/** A run whose setup a next run repeats. */
export interface CcRepeatTarget {
  runId: number;
  /** From the given rows; null when the run is not among them. */
  suiteName: string | null;
}

/** The next runs of one kind and period, as one card. */
export interface CcNextRunGroup {
  /** `control:baseline`. */
  key: string;
  kind: string;
  period: string;
  title: string;
  /** Distinct, in the server's order. */
  endpointIds: string[];
  reasons: string[];
  suggestions: string[];
  /** Every distinct run to repeat, in the server's order; empty for a re-grade. */
  targets: CcRepeatTarget[];
}

function distinctPush(list: string[], value: string | null | undefined): void {
  const text = value?.trim();
  if (text && !list.includes(text)) list.push(text);
}

function orderIndex(order: readonly string[], value: string): number {
  const index = order.indexOf(value);
  return index < 0 ? order.length : index;
}

/**
 * The server's next runs grouped by kind and period, so a battery analysis's per-suite advice reads as
 * one card: kinds in the order checkpoint, stratum, control, re-grade (any other kind after them), the
 * baseline before the comparison. A group's endpoints, reasons and suggestions are said once, and each
 * distinct run to repeat becomes a target, named by its suite from `rows`.
 */
export function ccNextRunGroups(result: CcAnalysisResult, rows: readonly CcRunRow[]): CcNextRunGroup[] {
  const kindOrder = CC_NEXT_RUN_KINDS.map(entry => entry.kind as string);
  const suiteOf = new Map(rows.map(row => [row.runId, row.suiteName] as const));
  const groups: CcNextRunGroup[] = [];
  const byKey = new Map<string, CcNextRunGroup>();
  for (const next of result.nextRuns) {
    const key = `${next.kind}:${next.period}`;
    let group = byKey.get(key);
    if (!group) {
      group = {
        key,
        kind: next.kind,
        period: next.period,
        title: CC_NEXT_RUN_KINDS.find(entry => entry.kind === next.kind)?.title ?? next.kind,
        endpointIds: [],
        reasons: [],
        suggestions: [],
        targets: []
      };
      byKey.set(key, group);
      groups.push(group);
    }
    distinctPush(group.endpointIds, next.endpointId);
    distinctPush(group.reasons, next.reason);
    distinctPush(group.suggestions, next.suggestion);
    const runId = next.repeatRunId;
    if (typeof runId === 'number' && !group.targets.some(target => target.runId === runId)) {
      group.targets.push({ runId, suiteName: suiteOf.get(runId) ?? null });
    }
  }
  // A stable sort keeps the server's order among kinds outside the fixed order.
  return groups
    .map((group, index) => ({ group, index }))
    .sort((a, b) => orderIndex(kindOrder, a.group.kind) - orderIndex(kindOrder, b.group.kind)
      || orderIndex(PERIOD_ORDER, a.group.period) - orderIndex(PERIOD_ORDER, b.group.period)
      || a.index - b.index)
    .map(entry => entry.group);
}

/** The runs and re-grades of one card: one per repeat target, one for a re-grade or a card without a target. */
function groupActionCount(group: CcNextRunGroup): number {
  return group.kind === 'regrade' ? 1 : Math.max(1, group.targets.length);
}

/** How many runs and re-grades the cards ask for: *6 runs would resolve the open questions.* */
export function ccNextRunActionCount(groups: readonly CcNextRunGroup[]): number {
  return groups.reduce((sum, group) => sum + groupActionCount(group), 0);
}

// --- Key figures ---

export type CcResultKeyFigureKey = 'decided' | 'baseline' | 'comparison' | 'pairedItems' | 'nextRuns';

/** One key figure card. */
export interface CcResultKeyFigure {
  key: CcResultKeyFigureKey;
  label: string;
  value: string;
  sub?: string;
  note?: string;
}

/** The endpoints whose paired items the *Paired items* figure counts. */
const PAIRED_ENDPOINTS = ['P1', 'P4', 'P5'];

function periodFigure(result: CcAnalysisResult, period: 'baseline' | 'comparison'): CcResultKeyFigure {
  const summary = period === 'baseline' ? result.baseline : result.comparison;
  const label = period === 'baseline' ? 'Baseline' : 'Comparison';
  const note = `${formatUtcDate(summary.startUtc)} – ${formatUtcDate(summary.endUtc)}`;
  const days = plural(summary.days.length, 'day');
  if (result.unitKind === 'batteryRun' && (result.units ?? []).length > 0) {
    const count = (result.units ?? []).filter(unit => unit.period === period).length;
    return { key: period, label, value: plural(count, 'battery run'), sub: `${plural(summary.runCount, 'run')} · ${days}`, note };
  }
  return { key: period, label, value: plural(summary.runCount, 'run'), sub: days, note };
}

/**
 * The key figure cards, in order: the endpoints decided, the two periods, the paired items (only while
 * a paired endpoint is computed) and the next runs. *Next runs* counts the runs and re-grades the cards
 * ask for, with the count of each kind.
 */
export function ccResultKeyFigures(result: CcAnalysisResult): CcResultKeyFigure[] {
  const endpoints = result.endpoints;
  const computed = endpoints.filter(endpoint => endpoint.computed).length;
  const notComputable = endpoints.length - computed;
  const decided = endpoints.filter(endpoint => isChange(endpoint) || isWithin(endpoint)).length;
  const decidedNote = [
    computed > 0 || notComputable === 0 ? `${computed} computed` : '',
    notComputable > 0 ? `${notComputable} not computable` : ''
  ].filter(part => part !== '').join(' · ');
  const figures: CcResultKeyFigure[] = [
    { key: 'decided', label: 'Endpoints decided', value: `${decided} of ${endpoints.length}`, note: decidedNote },
    periodFigure(result, 'baseline'),
    periodFigure(result, 'comparison')
  ];

  const paired = endpoints
    .filter(endpoint => endpoint.computed && PAIRED_ENDPOINTS.includes(endpoint.id))
    .map(endpoint => endpoint.itemCount);
  if (paired.length > 0) {
    figures.push({ key: 'pairedItems', label: 'Paired items', value: formatInteger(Math.max(...paired)), note: 'Items answered in both periods' });
  }

  const groups = ccNextRunGroups(result, []);
  if (groups.length === 0) {
    figures.push({ key: 'nextRuns', label: 'Next runs', value: 'None', note: 'No verdict waits on more data' });
  } else {
    const perKind = new Map<string, number>();
    for (const group of groups) perKind.set(group.kind, (perKind.get(group.kind) ?? 0) + groupActionCount(group));
    const note = [...perKind.entries()].map(([kind, count]) => {
      if (kind === 'regrade') return plural(count, 're-grade');
      const word = CC_NEXT_RUN_KINDS.find(entry => entry.kind === kind)?.countText ?? kind;
      return `${formatInteger(count)} ${word}`;
    }).join(' · ');
    figures.push({ key: 'nextRuns', label: 'Next runs', value: formatInteger(ccNextRunActionCount(groups)), note });
  }
  return figures;
}

// --- Analyzed units ---

/** The stored analysis's units as step 3's unit cards read them. */
export interface CcResultPeriodUnits {
  /** The units found in the given rows, by start, then id. */
  units: CcPeriodUnit[];
  /** Each found unit's stored period. */
  assignment: Map<number, CcUnitPeriod>;
  /** The first and last found unit of each period. */
  ids: CcPeriodIds;
  /** The analyzed units not found in the given rows. */
  missing: number;
  missingIds: number[];
}

function startMillis(value: string | null | undefined): number {
  const at = utcMillis(value);
  return Number.isFinite(at) ? at : Number.POSITIVE_INFINITY;
}

function storedPeriod(period: string): CcUnitPeriod {
  return period === 'baseline' || period === 'comparison' ? period : 'notUsed';
}

/**
 * The units of a stored analysis, resolved against the given rows: a battery run against `batteryRows`
 * and its member runs against `rows` (else the battery row's own members), a run against `rows`. Each
 * unit's period is the one stored with it. An analysis without stored units (code version 3 or
 * earlier) reads the periods' run ids as run units.
 */
export function ccResultPeriodUnits(
  result: CcAnalysisResult,
  rows: readonly CcRunRow[],
  batteryRows: readonly CcBatteryRunRow[]
): CcResultPeriodUnits {
  const runById = new Map(rows.map(row => [row.runId, row] as const));
  const batteryById = new Map(batteryRows.map(row => [row.batteryRunId, row] as const));
  const runUnit = (unitId: number, period: string): CcUnitView => ({ unitId, kind: 'run', period, startedAtUtc: '', memberRunIds: [unitId] });
  const stored: readonly CcUnitView[] = result.units && result.units.length > 0
    ? result.units
    : [
      ...result.baseline.runIds.map(id => runUnit(id, 'baseline')),
      ...result.comparison.runIds.map(id => runUnit(id, 'comparison'))
    ];

  const units: CcPeriodUnit[] = [];
  const assignment = new Map<number, CcUnitPeriod>();
  const missingIds: number[] = [];
  for (const unit of stored) {
    let resolved: CcPeriodUnit | null = null;
    if (unit.kind === 'batteryRun') {
      const battery = batteryById.get(unit.unitId);
      if (battery) {
        const members = new Map(battery.members.map(member => [member.runId, member] as const));
        const ids = unit.memberRunIds.length > 0 ? unit.memberRunIds : battery.members.map(member => member.runId);
        const runs = ids
          .map(id => runById.get(id) ?? members.get(id))
          .filter((row): row is CcRunRow => row !== undefined);
        resolved = periodUnit(unit.unitId, unit.startedAtUtc || battery.startedAtUtc, runs, battery);
      }
    } else {
      const row = runById.get(unit.unitId);
      if (row) resolved = periodUnit(unit.unitId, unit.startedAtUtc || row.startedAtUtc, [row], null);
    }
    if (resolved === null || assignment.has(resolved.id)) {
      if (resolved === null) missingIds.push(unit.unitId);
      continue;
    }
    units.push(resolved);
    assignment.set(resolved.id, storedPeriod(unit.period));
  }
  units.sort((a, b) => (a.at === b.at ? 0 : a.at < b.at ? -1 : 1) || a.id - b.id);

  const bounds = (period: CcUnitPeriod): [number | null, number | null] => {
    const inPeriod = units.filter(unit => assignment.get(unit.id) === period);
    return inPeriod.length === 0 ? [null, null] : [inPeriod[0].id, inPeriod[inPeriod.length - 1].id];
  };
  const [baselineFirstId, baselineLastId] = bounds('baseline');
  const [comparisonFirstId, comparisonLastId] = bounds('comparison');
  return {
    units,
    assignment,
    ids: { baselineFirstId, baselineLastId, comparisonFirstId, comparisonLastId },
    missing: missingIds.length,
    missingIds
  };
}

function periodUnit(id: number, startedAtUtc: string, runs: readonly CcRunRow[], battery: CcBatteryRunRow | null): CcPeriodUnit {
  return {
    id,
    startedAtUtc,
    at: startMillis(startedAtUtc),
    day: formatUtcDate(startedAtUtc),
    // An analyzed unit was eligible when the analysis ran.
    eligible: true,
    runs,
    battery
  };
}
