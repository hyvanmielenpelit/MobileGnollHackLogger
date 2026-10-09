/**
 * The periods of a Chat Consistency analysis, chosen as run ranges over the units step 1 chose: the
 * unit list, the per-unit period, the refusals, the windows sent to the server and the presets. Pure;
 * units are ordered by `startedAtUtc`, ties by id, and every date is UTC.
 */

import {
  addUtcDays,
  endOfUtcDay,
  formatUtcDate,
  formatUtcDateTime,
  isRunEligible,
  plural,
  startOfUtcDay,
  utcMillis
} from './chat-consistency-format';
import { CcBatteryRunRow, CcRunRow } from './chat-consistency.models';

const MS_PER_DAY = 86_400_000;

/** Units spanning this many UTC days or more split into their first and last {@link CC_EDGE_DAYS} days. */
export const CC_LONG_SPAN_DAYS = 28;

/** The days *Earliest vs latest* takes at each end of a long span. */
export const CC_EDGE_DAYS = 14;

export type CcPeriod = 'baseline' | 'comparison';

/** Where a unit falls: in a period, between or around them, or never sent. */
export type CcUnitPeriod = CcPeriod | 'notUsed' | 'notEligible';

/** One step-1 unit as the periods see it: a run, or a battery run in a battery set. */
export interface CcPeriodUnit {
  /** The run id, or the battery run id. */
  id: number;
  startedAtUtc: string;
  /** Epoch milliseconds of the start; +Infinity when it does not parse, so the unit sorts last. */
  at: number;
  /** The UTC day of the start, `yyyy-MM-dd`. */
  day: string;
  eligible: boolean;
  /** The runs the unit stands for: a battery run's members, or the run itself. */
  runs: readonly CcRunRow[];
  /** The battery run, in a battery set; null for a run. */
  battery: CcBatteryRunRow | null;
}

/** The four run choices: the first and last unit of each period, by id. */
export interface CcPeriodIds {
  readonly baselineFirstId: number | null;
  readonly baselineLastId: number | null;
  readonly comparisonFirstId: number | null;
  readonly comparisonLastId: number | null;
}

export const CC_NO_PERIOD_IDS: CcPeriodIds = Object.freeze({
  baselineFirstId: null,
  baselineLastId: null,
  comparisonFirstId: null,
  comparisonLastId: null
});

/** The period bounds the analysis request carries, as ISO instants. */
export interface CcPeriodWindows {
  baselineStartUtc: string;
  baselineEndUtc: string;
  comparisonStartUtc: string;
  comparisonEndUtc: string;
}

/** What a preset chose: the four ids (all null when it cannot apply), its note, and its anchor instant. */
export interface CcPresetOutcome {
  ids: CcPeriodIds;
  note: string;
  /** The annotation or composite event the periods are split at; null for other presets. */
  anchorUtc: string | null;
}

function unitNoun(battery: boolean): string {
  return battery ? 'battery run' : 'run';
}

function capitalized(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

function startMillis(value: string): number {
  const at = utcMillis(value);
  return Number.isFinite(at) ? at : Number.POSITIVE_INFINITY;
}

function dayMillis(day: string): number {
  return Date.parse(`${day}T00:00:00Z`);
}

/**
 * The units of the periods: in a battery set the complete battery runs, else the runs, ordered by start
 * then id. A battery run is eligible when complete and eligible; a run when eligible.
 */
export function ccPeriodUnits(
  rows: readonly CcRunRow[],
  batteryRows: readonly CcBatteryRunRow[],
  batteryMode: boolean
): CcPeriodUnit[] {
  const units: CcPeriodUnit[] = batteryMode
    ? batteryRows.filter(row => row.complete).map(row => ({
      id: row.batteryRunId,
      startedAtUtc: row.startedAtUtc,
      at: startMillis(row.startedAtUtc),
      day: formatUtcDate(row.startedAtUtc),
      eligible: row.complete && isRunEligible(row),
      runs: row.members,
      battery: row
    }))
    : rows.map(row => ({
      id: row.runId,
      startedAtUtc: row.startedAtUtc,
      at: startMillis(row.startedAtUtc),
      day: formatUtcDate(row.startedAtUtc),
      eligible: isRunEligible(row),
      runs: [row],
      battery: null
    }));
  return units.sort((a, b) => (a.at === b.at ? 0 : a.at < b.at ? -1 : 1) || a.id - b.id);
}

function indexOfId(units: readonly CcPeriodUnit[], id: number | null): number {
  return id === null ? -1 : units.findIndex(unit => unit.id === id);
}

interface RangeIndexes {
  bf: number;
  bl: number;
  cf: number;
  cl: number;
}

function rangeIndexes(units: readonly CcPeriodUnit[], ids: CcPeriodIds): RangeIndexes {
  return {
    bf: indexOfId(units, ids.baselineFirstId),
    bl: indexOfId(units, ids.baselineLastId),
    cf: indexOfId(units, ids.comparisonFirstId),
    cl: indexOfId(units, ids.comparisonLastId)
  };
}

function validRange(first: number, last: number): boolean {
  return first >= 0 && last >= 0 && first <= last;
}

/**
 * The period of every unit, by unit id. An ineligible unit is `notEligible` wherever it lies; an
 * eligible one is in the period whose range holds it (the baseline first, should the ranges overlap),
 * else `notUsed`.
 */
export function ccPeriodAssignment(units: readonly CcPeriodUnit[], ids: CcPeriodIds): Map<number, CcUnitPeriod> {
  const { bf, bl, cf, cl } = rangeIndexes(units, ids);
  const baseline = validRange(bf, bl);
  const comparison = validRange(cf, cl);
  const result = new Map<number, CcUnitPeriod>();
  units.forEach((unit, index) => {
    let period: CcUnitPeriod = 'notUsed';
    if (!unit.eligible) period = 'notEligible';
    else if (baseline && index >= bf && index <= bl) period = 'baseline';
    else if (comparison && index >= cf && index <= cl) period = 'comparison';
    result.set(unit.id, period);
  });
  return result;
}

/** The eligible units of each period, in unit order; empty for a period whose range is not valid. */
export function ccPeriodMembers(
  units: readonly CcPeriodUnit[],
  ids: CcPeriodIds
): { baseline: CcPeriodUnit[]; comparison: CcPeriodUnit[] } {
  const assignment = ccPeriodAssignment(units, ids);
  return {
    baseline: units.filter(unit => assignment.get(unit.id) === 'baseline'),
    comparison: units.filter(unit => assignment.get(unit.id) === 'comparison')
  };
}

/** Why the run choices cannot be analyzed, or '' when they can. */
export function ccPeriodsRefusal(units: readonly CcPeriodUnit[], ids: CcPeriodIds, battery: boolean): string {
  const noun = unitNoun(battery);
  if (units.length < 2) return `Choose at least two ${noun}s in step 1, one for each period.`;
  const { bf, bl, cf, cl } = rangeIndexes(units, ids);
  if (bf < 0 || bl < 0 || cf < 0 || cl < 0) return 'Choose the first and last run of both periods.';
  if (bl < bf) return 'The baseline\'s last run comes before its first.';
  if (cl < cf) return 'The comparison\'s last run comes before its first.';
  if (cf <= bl) return 'The comparison must start after the baseline\'s last run.';
  const before = units[bl];
  const after = units[cf];
  if (before.at === after.at) {
    return `${capitalized(noun)} #${before.id} and ${noun} #${after.id} started at the same moment and cannot be split.`;
  }
  const members = ccPeriodMembers(units, ids);
  if (members.baseline.length === 0) return `The baseline has no eligible ${noun}.`;
  if (members.comparison.length === 0) return `The comparison has no eligible ${noun}.`;
  return '';
}

/**
 * The windows the request carries, derived from the ranges; null while the choices are refused. The
 * baseline starts with the UTC day of its first unit and the comparison ends with the day of its last.
 * Between them: whole days when the baseline's last and the comparison's first started on different
 * days; on one day, a split instant `S` — the anchor when it lies after the baseline's last start and
 * no later than the comparison's first, else the comparison's first start — with the baseline ending
 * 1 ms before it.
 */
export function ccPeriodWindows(
  units: readonly CcPeriodUnit[],
  ids: CcPeriodIds,
  anchorUtc: string | null = null
): CcPeriodWindows | null {
  if (ccPeriodsRefusal(units, ids, false)) return null;
  const { bf, bl, cf, cl } = rangeIndexes(units, ids);
  const baselineStartUtc = startOfUtcDay(units[bf].day);
  const comparisonEndUtc = endOfUtcDay(units[cl].day);
  if (baselineStartUtc === null || comparisonEndUtc === null) return null;
  const before = units[bl];
  const after = units[cf];
  if (before.day !== after.day) {
    const baselineEndUtc = endOfUtcDay(before.day);
    const comparisonStartUtc = startOfUtcDay(after.day);
    if (baselineEndUtc === null || comparisonStartUtc === null) return null;
    return { baselineStartUtc, baselineEndUtc, comparisonStartUtc, comparisonEndUtc };
  }
  const anchor = anchorUtc === null ? Number.NaN : utcMillis(anchorUtc);
  const split = Number.isFinite(anchor) && before.at < anchor && anchor <= after.at ? anchor : after.at;
  return {
    baselineStartUtc,
    baselineEndUtc: new Date(split - 1).toISOString(),
    comparisonStartUtc: new Date(split).toISOString(),
    comparisonEndUtc
  };
}

// --- Presets ---

function idsOf(baseline: readonly CcPeriodUnit[], comparison: readonly CcPeriodUnit[]): CcPeriodIds {
  return {
    baselineFirstId: baseline[0].id,
    baselineLastId: baseline[baseline.length - 1].id,
    comparisonFirstId: comparison[0].id,
    comparisonLastId: comparison[comparison.length - 1].id
  };
}

function refused(note: string): CcPresetOutcome {
  return { ids: CC_NO_PERIOD_IDS, note, anchorUtc: null };
}

/** `2 runs each`, or `3 runs against 2`. */
function countsText(baseline: number, comparison: number, noun: string): string {
  return baseline === comparison ? `${plural(baseline, noun)} each` : `${plural(baseline, noun)} against ${comparison}`;
}

/** The eligible units with a start that parses. */
function presetUnits(units: readonly CcPeriodUnit[]): CcPeriodUnit[] {
  return units.filter(unit => unit.eligible && Number.isFinite(unit.at));
}

/**
 * *Earliest vs latest*, over the eligible units. A span of {@link CC_LONG_SPAN_DAYS} UTC days or more: the
 * units of the first {@link CC_EDGE_DAYS} days against those of the last. Two or more distinct days: the
 * earlier days against the later, split at the day boundary that best balances the counts (ties: more
 * in the baseline). One day: the earlier half by count against the later half (an odd one out goes to
 * the baseline). The note says which rule applied.
 */
export function ccEarliestVsLatest(units: readonly CcPeriodUnit[], battery: boolean): CcPresetOutcome {
  const noun = unitNoun(battery);
  const eligible = presetUnits(units);
  if (eligible.length < 2) return refused(`Choose at least two ${noun}s in step 1, one for each period.`);
  const days = [...new Set(eligible.map(unit => unit.day))].sort();
  const firstDay = days[0];
  const lastDay = days[days.length - 1];
  const span = Math.round((dayMillis(lastDay) - dayMillis(firstDay)) / MS_PER_DAY) + 1;
  let baseline: CcPeriodUnit[];
  let comparison: CcPeriodUnit[];
  let rule: string;
  if (span >= CC_LONG_SPAN_DAYS) {
    const baselineEnd = addUtcDays(firstDay, CC_EDGE_DAYS - 1);
    const comparisonStart = addUtcDays(lastDay, -(CC_EDGE_DAYS - 1));
    baseline = eligible.filter(unit => unit.day <= baselineEnd);
    comparison = eligible.filter(unit => unit.day >= comparisonStart);
    rule = `the first ${CC_EDGE_DAYS} days against the last ${CC_EDGE_DAYS} days`;
  } else if (days.length >= 2) {
    let best = 1;
    let bestGap = Number.POSITIVE_INFINITY;
    let bestCount = 0;
    for (let k = 1; k < days.length; k++) {
      const count = eligible.filter(unit => unit.day < days[k]).length;
      const gap = Math.abs(count - (eligible.length - count));
      if (gap < bestGap || (gap === bestGap && count > bestCount)) {
        best = k;
        bestGap = gap;
        bestCount = count;
      }
    }
    baseline = eligible.filter(unit => unit.day < days[best]);
    comparison = eligible.filter(unit => unit.day >= days[best]);
    rule = `the earlier days against the later days, split at ${days[best]}`;
  } else {
    const half = Math.ceil(eligible.length / 2);
    baseline = eligible.slice(0, half);
    comparison = eligible.slice(half);
    rule = 'the earlier half against the later half';
  }
  return {
    ids: idsOf(baseline, comparison),
    note: `The runs span ${plural(span, 'day')}: ${rule}, ${countsText(baseline.length, comparison.length, noun)}.`,
    anchorUtc: null
  };
}

/**
 * *Before vs after*: every eligible unit started before `anchorUtc` against every one started at or
 * after it. `what` names the anchor in the note: `the annotation`, `the Overseer change E2`.
 */
export function ccBeforeAfter(
  units: readonly CcPeriodUnit[],
  anchorUtc: string,
  battery: boolean,
  what: string
): CcPresetOutcome {
  const noun = unitNoun(battery);
  const anchor = utcMillis(anchorUtc);
  const eligible = presetUnits(units);
  const baseline = eligible.filter(unit => unit.at < anchor);
  const comparison = eligible.filter(unit => unit.at >= anchor);
  if (!Number.isFinite(anchor) || baseline.length === 0 || comparison.length === 0) {
    return refused(`No ${noun} on one side of ${what} (${formatUtcDate(anchorUtc)}).`);
  }
  return {
    ids: idsOf(baseline, comparison),
    note: `The ${noun}s before ${what} (${formatUtcDateTime(anchorUtc)}) against those from it: `
      + `${countsText(baseline.length, comparison.length, noun)}.`,
    anchorUtc
  };
}

/** The part of a saved analysis *Confirm on later data* starts from. */
export interface CcLastLook {
  baselineStartUtc: string;
  baselineEndUtc: string;
  createdAtUtc: string;
}

/**
 * *Confirm on later data*: the eligible units started inside the last analysis's baseline window against
 * those started after the analysis was saved.
 */
export function ccConfirmOnLaterData(units: readonly CcPeriodUnit[], last: CcLastLook, battery: boolean): CcPresetOutcome {
  const noun = unitNoun(battery);
  const start = utcMillis(last.baselineStartUtc);
  const end = utcMillis(last.baselineEndUtc);
  const saved = utcMillis(last.createdAtUtc);
  const eligible = presetUnits(units);
  const baseline = eligible.filter(unit => unit.at >= start && unit.at <= end);
  if (baseline.length === 0) return refused(`The last analysis's baseline ${noun}s are not in the step-1 selection.`);
  const comparison = eligible.filter(unit => unit.at > saved);
  if (comparison.length === 0) {
    return refused(`No ${noun} was made after the last analysis was saved (${formatUtcDateTime(last.createdAtUtc)}).`);
  }
  return {
    ids: idsOf(baseline, comparison),
    note: `Compares the ${noun}s after the last analysis, saved ${formatUtcDateTime(last.createdAtUtc)}, with its baseline.`,
    anchorUtc: null
  };
}

/**
 * The run choices that name a saved analysis's units: the earliest and latest of each period's ids
 * found among `units`; a period with none found stays unset.
 */
export function ccIdsFromUnits(
  units: readonly CcPeriodUnit[],
  baselineIds: readonly number[],
  comparisonIds: readonly number[]
): CcPeriodIds {
  const bounds = (ids: readonly number[]): [number | null, number | null] => {
    const wanted = new Set(ids);
    const present = units.filter(unit => wanted.has(unit.id));
    return present.length === 0 ? [null, null] : [present[0].id, present[present.length - 1].id];
  };
  const [baselineFirstId, baselineLastId] = bounds(baselineIds);
  const [comparisonFirstId, comparisonLastId] = bounds(comparisonIds);
  return { baselineFirstId, baselineLastId, comparisonFirstId, comparisonLastId };
}

/** The choices with every id that names no unit cleared; the same object when nothing changed. */
export function ccPruneIds(units: readonly CcPeriodUnit[], ids: CcPeriodIds): CcPeriodIds {
  const listed = new Set(units.map(unit => unit.id));
  const keep = (id: number | null) => (id !== null && listed.has(id) ? id : null);
  const next: CcPeriodIds = {
    baselineFirstId: keep(ids.baselineFirstId),
    baselineLastId: keep(ids.baselineLastId),
    comparisonFirstId: keep(ids.comparisonFirstId),
    comparisonLastId: keep(ids.comparisonLastId)
  };
  return sameIds(next, ids) ? ids : next;
}

export function sameIds(a: CcPeriodIds, b: CcPeriodIds): boolean {
  return a.baselineFirstId === b.baselineFirstId && a.baselineLastId === b.baselineLastId
    && a.comparisonFirstId === b.comparisonFirstId && a.comparisonLastId === b.comparisonLastId;
}
