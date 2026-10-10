/**
 * What the browser can tell about an analysis before it is sent: each period's sample against Protocol
 * V1's published minimums, which primary endpoints can reach a verdict, and the notes the Analyze step's
 * preview lists. Pure and DOM-free. It never predicts a verdict, and of a grade only that it is at
 * most Indicated or not computable: the server applies the protocol itself.
 */

import { CcEventGroup } from './chat-consistency-events';
import { axisText, plural } from './chat-consistency-format';
import { CcPeriod, CcPeriodIds, CcPeriodUnit, CcUnitPeriod } from './chat-consistency-periods';
import { CC_AXES, CcAxis, CcAxisEligibility, CcRunRow, CcTimelinePoint } from './chat-consistency.models';

/** One of Protocol V1's primary endpoints, as the client needs it. */
export interface CcProtocolEndpoint {
  readonly id: string;
  readonly name: string;
  readonly margin: number;
  readonly unit: 'index points' | '%';
  /** The measurement axis whose eligibility and segments the endpoint's runs need. */
  readonly axis: CcAxis;
  /** The effect is taken within the time strata both periods sampled. */
  readonly stratified: boolean;
  /** The axis the endpoint falls back to, as a legacy proxy, when a period has no run on `axis`; null when none. */
  readonly legacyAxis: CcAxis | null;
}

/**
 * Protocol V1's primary endpoints as the server publishes them (`ChatConsistencyProtocol.V1`, its
 * `DefaultEndpoints`): margins, axes and stratification. A change to the server protocol changes this
 * list with it.
 */
export const CC_PROTOCOL_V1_ENDPOINTS: readonly CcProtocolEndpoint[] = [
  { id: 'P1', name: 'Quality', margin: 3, unit: 'index points', axis: 'quality', stratified: false, legacyAxis: null },
  { id: 'P2', name: 'Time to first answer text', margin: 15, unit: '%', axis: 'speedTelemetry', stratified: true, legacyAxis: 'speedLegacy' },
  { id: 'P3', name: 'Answer streaming rate', margin: 10, unit: '%', axis: 'speedTelemetry', stratified: true, legacyAxis: null },
  { id: 'P4', name: 'Work per turn', margin: 15, unit: '%', axis: 'work', stratified: false, legacyAxis: null },
  { id: 'P5', name: 'Cost per question', margin: 10, unit: '%', axis: 'cost', stratified: false, legacyAxis: null }
];

/** Protocol V1's α and minimum samples. */
export const CC_PROTOCOL_V1 = Object.freeze({
  alpha: 0.05,
  minimumRunsPerPeriod: 2,
  minimumDaysPerPeriod: 2,
  minimumPairedItems: 20,
  minimumSpeedRunsPerStratum: 3
});

/** `±3 index points`, `±15 %`. */
export function ccMarginText(margin: number, unit: CcProtocolEndpoint['unit']): string {
  return unit === '%' ? `±${margin} %` : `±${margin} ${unit}`;
}

function unitNoun(battery: boolean): string {
  return battery ? 'battery run' : 'run';
}

function periodName(period: CcPeriod): string {
  return period === 'baseline' ? 'baseline' : 'comparison';
}

function capitalized(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** `#1, #2`. */
function idList(ids: readonly number[]): string {
  return ids.map(id => `#${id}`).join(', ');
}

/** `2 and 3`, `1, 2 and 3`. */
function andList(items: readonly string[]): string {
  if (items.length <= 1) return items.join('');
  return `${items.slice(0, -1).join(', ')} and ${items[items.length - 1]}`;
}

// --- Period samples ---

/** A period's units against the minimum sample of P1, P4 and P5. */
export interface CcPeriodSample {
  count: number;
  /** Distinct UTC days. */
  days: number;
  /** `yyyy-MM-dd`; null for an empty period. */
  firstDay: string | null;
  lastDay: string | null;
  meetsMinimum: boolean;
}

/** The sample of a period's eligible units. */
export function ccPeriodSample(units: readonly CcPeriodUnit[]): CcPeriodSample {
  const days = [...new Set(units.map(unit => unit.day))].sort();
  return {
    count: units.length,
    days: days.length,
    firstDay: days[0] ?? null,
    lastDay: days[days.length - 1] ?? null,
    meetsMinimum: units.length >= CC_PROTOCOL_V1.minimumRunsPerPeriod && days.length >= CC_PROTOCOL_V1.minimumDaysPerPeriod
  };
}

/** `2026-10-01 to 2026-10-03`, or one day; empty for an empty period. */
export function ccSampleDayText(sample: CcPeriodSample): string {
  if (sample.firstDay === null) return '';
  return sample.firstDay === sample.lastDay ? sample.firstDay : `${sample.firstDay} to ${sample.lastDay}`;
}

/** `3 runs on 2 days`. */
export function ccSampleCountText(sample: CcPeriodSample, battery: boolean): string {
  return `${plural(sample.count, unitNoun(battery))} on ${plural(sample.days, 'day')}`;
}

/** `P1, P4 and P5 need at least 2 on 2 days to be Established.` */
export function ccSampleNeedText(): string {
  return `P1, P4 and P5 need at least ${CC_PROTOCOL_V1.minimumRunsPerPeriod} on ${CC_PROTOCOL_V1.minimumDaysPerPeriod} days to be Established.`;
}

/**
 * A period's sample as one sentence: `Baseline: 1 battery run on 1 day (2026-10-08). P1, P4 and P5 need
 * at least 2 on 2 days to be Established.`, or `…, which meets the minimum sample for P1, P4 and P5.`
 */
export function ccSampleLine(period: CcPeriod, units: readonly CcPeriodUnit[], battery: boolean): string {
  const sample = ccPeriodSample(units);
  const facts = `${capitalized(periodName(period))}: ${ccSampleCountText(sample, battery)} (${ccSampleDayText(sample)})`;
  return sample.meetsMinimum ? `${facts}, which meets the minimum sample for P1, P4 and P5.` : `${facts}. ${ccSampleNeedText()}`;
}

// --- Endpoint readiness ---

/**
 * What the browser can say of an endpoint: it meets the minimum sample; it is below it, or pooled
 * across a measurement change, and so at most Indicated; or it cannot be computed.
 */
export type CcReadinessStatus = 'meets' | 'belowMinimum' | 'capped' | 'notComputed';

export const CC_READINESS_STATUS_TEXT: Readonly<Record<CcReadinessStatus, string>> = Object.freeze({
  meets: 'Meets the minimum sample',
  belowMinimum: 'At most Indicated',
  capped: 'At most Indicated',
  notComputed: 'Not computable'
});

export interface CcEndpointReadiness {
  id: string;
  name: string;
  /** `±3 index points`, `±15 %`. */
  marginText: string;
  status: CcReadinessStatus;
  /** The one fact behind the status. */
  fact: string;
  /** A re-grade of every compared run by one common grader would make the endpoint computable; absent otherwise. */
  regrade?: boolean;
}

/** What {@link ccEndpointReadiness} reads besides the periods. */
export interface CcReadinessOptions {
  /** *Pool across measurement segment boundaries* is on. */
  relaxedPooling?: boolean;
}

/** No endpoint can be better than Indicated, or be computed at all, with these periods. */
export function ccNothingEstablishable(endpoints: readonly CcEndpointReadiness[]): boolean {
  return endpoints.length > 0 && endpoints.every(endpoint => endpoint.status !== 'meets');
}

/** The eligibility a unit carries: a battery run's aggregate, or the run's own. */
function unitEligibility(unit: CcPeriodUnit): readonly CcAxisEligibility[] {
  return unit.battery?.eligibility ?? unit.runs[0]?.eligibility ?? [];
}

/** The unit can be used on the axis. */
export function ccUnitEligibleOn(unit: CcPeriodUnit, axis: CcAxis): boolean {
  return unitEligibility(unit).some(entry => entry.axis === axis && entry.eligible);
}

/** `Baseline 3 runs on 2 days · Comparison 2 runs on 2 days`. */
function samplesFact(baseline: CcPeriodSample, comparison: CcPeriodSample, battery: boolean): string {
  return `Baseline ${ccSampleCountText(baseline, battery)} · Comparison ${ccSampleCountText(comparison, battery)}`;
}

/** `no eligible run on Quality in the baseline`, or `in either period`. */
function noEligibleFact(axis: CcAxis, baselineEmpty: boolean, comparisonEmpty: boolean, battery: boolean): string {
  const where = baselineEmpty && comparisonEmpty ? 'either period' : baselineEmpty ? 'the baseline' : 'the comparison';
  return `No ${unitNoun(battery)} in ${where} is eligible for ${axisText(axis)}.`;
}

/** Per time stratum, the runs that sampled it. */
function strataCounts(units: readonly CcPeriodUnit[], points: ReadonlyMap<number, CcTimelinePoint>): Map<string, number> {
  const counts = new Map<string, number>();
  for (const run of units.flatMap(unit => unit.runs)) {
    for (const stratum of new Set(points.get(run.runId)?.strata ?? [])) counts.set(stratum, (counts.get(stratum) ?? 0) + 1);
  }
  return counts;
}

/** The quality grading change between the periods, as the preview can tell it from the run table. */
interface CcGradingSplit {
  /** `Grading changed between the periods (harness 53 → 54).` */
  text: string;
  /** `grading change (harness 53 → 54)`, for a pooled endpoint's fact. */
  phrase: string;
  /** The assessor snapshot whose re-grades cover every run of both periods; null when none does. */
  commonGrader: { snapshotId: number; display: string } | null;
}

function qualitySegments(units: readonly CcPeriodUnit[]): Set<number> {
  return new Set(units.flatMap(unit => unit.runs).flatMap(run => run.eligibility)
    .filter(entry => entry.axis === 'quality' && entry.eligible && entry.segment !== null)
    .map(entry => entry.segment as number));
}

function byStart(a: CcRunRow, b: CcRunRow): number {
  return a.startedAtUtc.localeCompare(b.startedAtUtc) || a.runId - b.runId;
}

/** The snapshot of the first re-grade in `runs[0]`'s coverage that every run of `runs` carries; null when none. */
function coveringGrader(runs: readonly CcRunRow[]): { snapshotId: number; display: string } | null {
  if (runs.length === 0) return null;
  const covers = (run: CcRunRow, snapshotId: number) => run.regradeCoverage.some(entry => entry.snapshotId === snapshotId);
  const found = runs[0].regradeCoverage.find(entry => runs.every(run => covers(run, entry.snapshotId)));
  return found ? { snapshotId: found.snapshotId, display: found.display } : null;
}

/**
 * Whether the quality measurement changed between the periods: the eligible units' runs of the two
 * periods share no quality measurement segment, which a grading or scoring change starts. Named by the
 * harness versions (and scoring methods, where they differ) of the baseline's last run and the
 * comparison's first; null when the periods share a segment. `allRuns` are every run of both periods,
 * which a common grader must cover, as the analysis requires.
 */
function gradingSplit(
  baseline: readonly CcPeriodUnit[],
  comparison: readonly CcPeriodUnit[],
  allRuns: readonly CcRunRow[]
): CcGradingSplit | null {
  const sb = qualitySegments(baseline);
  const sc = qualitySegments(comparison);
  if (sb.size === 0 || sc.size === 0 || [...sb].some(segment => sc.has(segment))) return null;
  const last = [...baseline.flatMap(unit => unit.runs)].sort(byStart).pop();
  const first = [...comparison.flatMap(unit => unit.runs)].sort(byStart)[0];
  const harness = last?.harnessVersion && first?.harnessVersion && last.harnessVersion !== first.harnessVersion
    ? `harness ${last.harnessVersion} → ${first.harnessVersion}` : '';
  const scoringChanged = !!last && !!first && last.scoringMethodVersion !== first.scoringMethodVersion;
  const scoring = scoringChanged ? `scoring method ${last!.scoringMethodVersion} → ${first!.scoringMethodVersion}` : '';
  const what = scoringChanged ? (harness ? 'grading and scoring' : 'scoring') : 'grading';
  const versions = [harness, scoring].filter(part => part).join('; ');
  const detail = versions ? ` (${versions})` : '';
  return {
    text: `${capitalized(what)} changed between the periods${detail}.`,
    phrase: `${what} change${detail}`,
    commonGrader: coveringGrader(allRuns)
  };
}

function plainReadiness(
  endpoint: CcProtocolEndpoint,
  baseline: readonly CcPeriodUnit[],
  comparison: readonly CcPeriodUnit[],
  battery: boolean,
  options: CcReadinessOptions
): Pick<CcEndpointReadiness, 'status' | 'fact' | 'regrade'> {
  const b = baseline.filter(unit => ccUnitEligibleOn(unit, endpoint.axis));
  const c = comparison.filter(unit => ccUnitEligibleOn(unit, endpoint.axis));
  if (b.length === 0 || c.length === 0) {
    return { status: 'notComputed', fact: noEligibleFact(endpoint.axis, b.length === 0, c.length === 0, battery) };
  }
  const split = endpoint.axis === 'quality'
    ? gradingSplit(b, c, [...baseline, ...comparison].flatMap(unit => unit.runs))
    : null;
  const pooled = split !== null && split.commonGrader === null && options.relaxedPooling === true;
  if (split && !split.commonGrader && !pooled) {
    return {
      status: 'notComputed',
      fact: `${split.text} Re-grade every compared run with a common grader to compare quality.`,
      regrade: true
    };
  }
  const graderNote = split?.commonGrader
    ? ` Quality is compared under ${split.commonGrader.display}, whose re-grades cover every compared run.`
    : '';
  const bs = ccPeriodSample(b);
  const cs = ccPeriodSample(c);
  if (bs.meetsMinimum && cs.meetsMinimum) {
    if (pooled) {
      return { status: 'capped', fact: `Pooled across a ${split!.phrase}, which caps the grade at Indicated: ${samplesFact(bs, cs, battery)}.` };
    }
    return { status: 'meets', fact: `${samplesFact(bs, cs, battery)}${graderNote ? `.${graderNote}` : ''}` };
  }
  const p = CC_PROTOCOL_V1;
  const fewer = `Fewer than ${plural(p.minimumRunsPerPeriod, unitNoun(battery))} on ${plural(p.minimumDaysPerPeriod, 'day')}`;
  const where = !bs.meetsMinimum && !cs.meetsMinimum ? 'per period' : `in the ${bs.meetsMinimum ? 'comparison' : 'baseline'}`;
  const poolNote = pooled ? ` Pooled across a ${split!.phrase}.` : '';
  return { status: 'belowMinimum', fact: `${fewer} ${where}: ${samplesFact(bs, cs, battery)}.${poolNote}${graderNote}` };
}

function stratifiedReadiness(
  endpoint: CcProtocolEndpoint,
  baseline: readonly CcPeriodUnit[],
  comparison: readonly CcPeriodUnit[],
  points: ReadonlyMap<number, CcTimelinePoint>,
  battery: boolean
): Pick<CcEndpointReadiness, 'status' | 'fact'> {
  let axis = endpoint.axis;
  let b = baseline.filter(unit => ccUnitEligibleOn(unit, axis));
  let c = comparison.filter(unit => ccUnitEligibleOn(unit, axis));
  let proxy = '';
  if ((b.length === 0 || c.length === 0) && endpoint.legacyAxis) {
    axis = endpoint.legacyAxis;
    b = baseline.filter(unit => ccUnitEligibleOn(unit, axis));
    c = comparison.filter(unit => ccUnitEligibleOn(unit, axis));
    proxy = ' Measured as model time (legacy proxy).';
  }
  if (b.length === 0 || c.length === 0) {
    return { status: 'notComputed', fact: noEligibleFact(endpoint.axis, b.length === 0, c.length === 0, battery) };
  }
  const bc = strataCounts(b, points);
  const cc = strataCounts(c, points);
  const common = [...bc.keys()].filter(stratum => cc.has(stratum)).sort();
  if (common.length === 0) return { status: 'notComputed', fact: `The periods share no time stratum.${proxy}` };
  const min = CC_PROTOCOL_V1.minimumSpeedRunsPerStratum;
  const counts = common.map(stratum => `${stratum}: ${bc.get(stratum)} / ${cc.get(stratum)}`).join(' · ');
  if (common.some(stratum => (bc.get(stratum) ?? 0) >= min && (cc.get(stratum) ?? 0) >= min)) {
    return { status: 'meets', fact: `Runs per common stratum, baseline / comparison: ${counts}.${proxy}` };
  }
  return { status: 'belowMinimum', fact: `No common stratum has ${min} runs in each period: ${counts}.${proxy}` };
}

/**
 * Each endpoint's readiness over the eligible units of the two periods: P1, P4 and P5 by the minimum
 * runs and days of the units eligible on their axis, below which they are at most Indicated; P2 and P3
 * by the runs per common time stratum, P2 falling back to its legacy proxy as the server does. P1 is
 * not computable where the periods share no quality measurement segment (a grading or scoring change)
 * and no re-grade by one assessor covers every run of both periods, unless pooling is on, which caps
 * it at Indicated. `points` are the timeline's run points by run id.
 */
export function ccEndpointReadiness(
  endpoints: readonly CcProtocolEndpoint[],
  baseline: readonly CcPeriodUnit[],
  comparison: readonly CcPeriodUnit[],
  points: ReadonlyMap<number, CcTimelinePoint>,
  battery: boolean,
  options: CcReadinessOptions = {}
): CcEndpointReadiness[] {
  return endpoints.map(endpoint => ({
    id: endpoint.id,
    name: endpoint.name,
    marginText: ccMarginText(endpoint.margin, endpoint.unit),
    ...(endpoint.stratified
      ? stratifiedReadiness(endpoint, baseline, comparison, points, battery)
      : plainReadiness(endpoint, baseline, comparison, battery, options))
  }));
}

// --- Notes ---

/** A measurement segment change on one axis across the two periods. */
export interface CcSegmentNote {
  axis: CcAxis;
  /** Distinct, ascending. */
  segments: number[];
  /** Pooling is off, so the analysis refuses it. */
  refused: boolean;
  text: string;
}

/**
 * The axes on which the runs of the two periods' eligible units carry more than one measurement
 * segment: refused unless pooling is on, which caps the grades at Indicated.
 */
export function ccSegmentNotes(
  baseline: readonly CcPeriodUnit[],
  comparison: readonly CcPeriodUnit[],
  relaxedPooling: boolean
): CcSegmentNote[] {
  const runs = [...baseline, ...comparison].flatMap(unit => unit.runs);
  const notes: CcSegmentNote[] = [];
  for (const axis of CC_AXES) {
    const segments = [...new Set(runs.flatMap(run => run.eligibility)
      .filter(entry => entry.axis === axis && entry.eligible && entry.segment !== null)
      .map(entry => entry.segment as number))].sort((a, b) => a - b);
    if (segments.length < 2) continue;
    const span = `${axisText(axis)} spans measurement segments ${andList(segments.map(String))}`;
    notes.push({
      axis,
      segments,
      refused: !relaxedPooling,
      text: relaxedPooling
        ? `${span}; they are pooled, which caps the grades at Indicated.`
        : `${span}: the analysis refuses a change of measurement inside the span unless Pool across measurement segment boundaries is on.`
    });
  }
  return notes;
}

/**
 * One line on the runs without a matched control: `2 of 6 runs have no matched control run (#103,
 * #104); the analysis looks for controls among other models' runs itself.` Empty when every run has one.
 */
export function ccMissingControlsText(runs: readonly CcRunRow[]): string {
  const missing = runs.filter(row => row.matchedControlRunIds.length === 0).map(row => row.runId).sort((a, b) => a - b);
  if (missing.length === 0) return '';
  const verb = missing.length === 1 ? 'has' : 'have';
  return `${missing.length} of ${plural(runs.length, 'run')} ${verb} no matched control run (${idList(missing)}); `
    + 'the analysis looks for controls among other models\' runs itself.';
}

/** The ineligible units inside either period's range, in unit order, each with the period it is inside. */
export function ccIneligibleInPeriods(
  units: readonly CcPeriodUnit[],
  ids: CcPeriodIds,
  assignment: ReadonlyMap<number, CcUnitPeriod>
): { unit: CcPeriodUnit; period: CcPeriod }[] {
  const at = (id: number | null) => (id === null ? -1 : units.findIndex(unit => unit.id === id));
  const ranges: [CcPeriod, number, number][] = [
    ['baseline', at(ids.baselineFirstId), at(ids.baselineLastId)],
    ['comparison', at(ids.comparisonFirstId), at(ids.comparisonLastId)]
  ];
  const result: { unit: CcPeriodUnit; period: CcPeriod }[] = [];
  units.forEach((unit, index) => {
    if (assignment.get(unit.id) !== 'notEligible') return;
    const range = ranges.find(([, first, last]) => first >= 0 && last >= first && index >= first && index <= last);
    if (range) result.push({ unit, period: range[0] });
  });
  return result;
}

/** What a preview note is about. */
export type CcPreviewNoteKind = 'events' | 'segment' | 'controls' | 'leftOut' | 'ineligible';

/** One finding of the preview; each counts once in the Preview tab's badge. */
export interface CcPreviewNote {
  kind: CcPreviewNoteKind;
  /** `warning` may change the result; `info` only informs. */
  severity: 'warning' | 'info';
  text: string;
  /** The composite events a `events` note lists. */
  eventGroups: readonly CcEventGroup[];
}

/** What {@link ccPreviewNotes} reads. */
export interface CcPreviewNoteInput {
  battery: boolean;
  /** The composite Overseer events between the baseline's start and the comparison's end. */
  eventGroupsInSpan: readonly CcEventGroup[];
  segmentNotes: readonly CcSegmentNote[];
  /** The runs of both periods: the members of their battery runs in a battery set. */
  periodRuns: readonly CcRunRow[];
  /** The units left out in step 1 that started inside either window. */
  leftOutInPeriods: readonly number[];
  ineligible: readonly { unit: CcPeriodUnit; period: CcPeriod }[];
}

/** The preview's notes, in reading order: Overseer changes, segments, controls, left-out units, ineligible units. */
export function ccPreviewNotes(input: CcPreviewNoteInput): CcPreviewNote[] {
  const noun = unitNoun(input.battery);
  const notes: CcPreviewNote[] = [];
  if (input.eventGroupsInSpan.length > 0) {
    notes.push({
      kind: 'events',
      severity: 'warning',
      text: 'A change inside a period mixes measurements; split at it, or check that it does not affect what you compare.',
      eventGroups: input.eventGroupsInSpan
    });
  }
  for (const segment of input.segmentNotes) {
    notes.push({ kind: 'segment', severity: segment.refused ? 'warning' : 'info', text: segment.text, eventGroups: [] });
  }
  const controls = ccMissingControlsText(input.periodRuns);
  if (controls) notes.push({ kind: 'controls', severity: 'info', text: controls, eventGroups: [] });
  if (input.leftOutInPeriods.length > 0) {
    const n = input.leftOutInPeriods.length;
    notes.push({
      kind: 'leftOut',
      severity: 'info',
      text: `${capitalized(n === 1 ? noun : `${noun}s`)} left out in step 1 inside the windows: `
        + `${idList(input.leftOutInPeriods)}. ${n === 1 ? 'It is' : 'They are'} not analyzed.`,
      eventGroups: []
    });
  }
  for (const { unit, period } of input.ineligible) {
    notes.push({
      kind: 'ineligible',
      severity: 'info',
      text: `${capitalized(noun)} #${unit.id} is inside the ${periodName(period)} but not eligible and is not analyzed.`,
      eventGroups: []
    });
  }
  return notes;
}
