/**
 * The run selection of the Chat Consistency wizard's step 1: an optional first and last unit and the
 * units the admin left out. A unit is what the analysis counts: a run in a suite set (or when no set
 * is chosen), a battery run in a battery set. Pure; units are ordered chronologically by
 * `startedAtUtc`, ties by id, never by the card list's sort. A function that changes the selection
 * returns a new scope object, so components can compare scopes by identity; a no-op returns its input.
 */

import { formatUtcDate, plural, utcMillis } from './chat-consistency-format';
import {
  CC_BATTERY_SET_PREFIX,
  CC_SUITE_SET_PREFIX,
  CcBatteryRunRow,
  CcRunRow,
  CcUnitKind
} from './chat-consistency.models';

export interface CcRunScope {
  /** The first unit's id: a run id, or a battery run id when `unitKind` is `batteryRun`. */
  firstRunId: number | null;
  lastRunId: number | null;
  /** Unit ids unchecked by the admin. */
  leftOut: ReadonlySet<number>;
  /** What the ids name; absent reads as `run`. */
  unitKind?: CcUnitKind;
}

export const CC_EMPTY_SCOPE: CcRunScope = Object.freeze<CcRunScope>({
  firstRunId: null,
  lastRunId: null,
  leftOut: new Set<number>()
});

/** The empty selection over battery runs. */
export const CC_EMPTY_BATTERY_SCOPE: CcRunScope = Object.freeze<CcRunScope>({
  firstRunId: null,
  lastRunId: null,
  leftOut: new Set<number>(),
  unitKind: 'batteryRun'
});

/** The empty selection over units of the kind. */
export function ccEmptyScope(unitKind: CcUnitKind): CcRunScope {
  return unitKind === 'batteryRun' ? CC_EMPTY_BATTERY_SCOPE : CC_EMPTY_SCOPE;
}

/**
 * Whether a unit is in the analysis. `incomplete` is a battery run with a suite slot that holds no
 * usable member, never analyzed; `outsideSet` a run outside the compared battery or suite, which the
 * timeline marks but no card shows.
 */
export type CcRunInclusion = 'included' | 'leftOut' | 'beforeSpan' | 'afterSpan' | 'incomplete' | 'outsideSet';

export const CC_INCLUSION_TEXT: Readonly<Record<Exclude<CcRunInclusion, 'included'>, string>> = Object.freeze({
  leftOut: 'left out in step 1',
  beforeSpan: 'before the first run',
  afterSpan: 'after the last run',
  incomplete: 'incomplete battery run',
  outsideSet: 'outside the compared set'
});

/** A row the selection counts: a run, or a battery run. */
export type CcScopeUnit = CcRunRow | CcBatteryRunRow;

export function isBatteryRunRow(row: CcScopeUnit): row is CcBatteryRunRow {
  return 'members' in row;
}

/** The unit's id: its battery run id, else its run id. */
export function unitIdOf(row: CcScopeUnit): number {
  return isBatteryRunRow(row) ? row.batteryRunId : row.runId;
}

/** `run` or `battery run`, the noun the notes and labels use for a scope's units. */
export function unitNoun(unitKind: CcUnitKind | undefined): string {
  return unitKind === 'batteryRun' ? 'battery run' : 'run';
}

function capitalized(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** At most this many ids are named in a note. */
const NOTE_ID_LIMIT = 10;

function orderKey(row: CcScopeUnit): number {
  const at = utcMillis(row.startedAtUtc);
  return Number.isFinite(at) ? at : Number.POSITIVE_INFINITY;
}

function compareUnits(a: CcScopeUnit, b: CcScopeUnit): number {
  const ka = orderKey(a);
  const kb = orderKey(b);
  if (ka < kb) return -1;
  if (ka > kb) return 1;
  return unitIdOf(a) - unitIdOf(b);
}

function chronological<T extends CcScopeUnit>(rows: readonly T[]): T[] {
  return [...rows].sort(compareUnits);
}

interface SpanBounds {
  first: CcScopeUnit | null;
  last: CcScopeUnit | null;
}

/** The rows holding the marks; a mark whose unit is not in `rows` is an open bound. */
function spanBounds(rows: readonly CcScopeUnit[], scope: CcRunScope): SpanBounds {
  const find = (id: number | null) => (id === null ? null : rows.find(row => unitIdOf(row) === id) ?? null);
  return { first: find(scope.firstRunId), last: find(scope.lastRunId) };
}

function inclusionOf(row: CcScopeUnit, bounds: SpanBounds, scope: CcRunScope): CcRunInclusion {
  if (isBatteryRunRow(row) && !row.complete) return 'incomplete';
  if (bounds.first && compareUnits(row, bounds.first) < 0) return 'beforeSpan';
  if (bounds.last && compareUnits(row, bounds.last) > 0) return 'afterSpan';
  return scope.leftOut.has(unitIdOf(row)) ? 'leftOut' : 'included';
}

/**
 * Whether a unit is in the analysis. An incomplete battery run is never; then the span decides: a
 * left-out unit outside [first, last] reads as `beforeSpan` / `afterSpan`, which is what its disabled
 * checkbox shows.
 */
export function runInclusion<T extends CcScopeUnit>(row: T, rows: readonly T[], scope: CcRunScope): CcRunInclusion {
  return inclusionOf(row, spanBounds(rows, scope), scope);
}

/** The units in the analysis: inside [first, last] (an absent bound is open), complete and not left out, oldest first. */
export function scopeRuns<T extends CcScopeUnit>(rows: readonly T[], scope: CcRunScope): T[] {
  const bounds = spanBounds(rows, scope);
  return chronological(rows).filter(row => inclusionOf(row, bounds, scope) === 'included');
}

/** The units not in the analysis, keyed by unit id with the reason, in chronological order. */
export function notAnalyzedRuns(rows: readonly CcScopeUnit[], scope: CcRunScope): ReadonlyMap<number, CcRunInclusion> {
  const bounds = spanBounds(rows, scope);
  const result = new Map<number, CcRunInclusion>();
  for (const row of chronological(rows)) {
    const inclusion = inclusionOf(row, bounds, scope);
    if (inclusion !== 'included') result.set(unitIdOf(row), inclusion);
  }
  return result;
}

/**
 * Marks `runId` as the first unit; choosing the unit that already holds the mark, or null, clears it.
 * A first unit later than the last unit clears the last unit, and the note says so.
 */
export function setFirstRun(
  scope: CcRunScope,
  rows: readonly CcScopeUnit[],
  runId: number | null
): { scope: CcRunScope; note: string } {
  if (runId === null || runId === scope.firstRunId) {
    return scope.firstRunId === null ? { scope, note: '' } : { scope: { ...scope, firstRunId: null }, note: '' };
  }
  const next: CcRunScope = { ...scope, firstRunId: runId };
  const bounds = spanBounds(rows, next);
  if (bounds.first && bounds.last && compareUnits(bounds.first, bounds.last) > 0) {
    const noun = unitNoun(scope.unitKind);
    return {
      scope: { ...next, lastRunId: null },
      note: `${capitalized(noun)} #${runId} is after the last ${noun}, so the last ${noun} was cleared.`
    };
  }
  return { scope: next, note: '' };
}

/**
 * Marks `runId` as the last unit; choosing the unit that already holds the mark, or null, clears it.
 * A last unit earlier than the first unit clears the first unit, and the note says so.
 */
export function setLastRun(
  scope: CcRunScope,
  rows: readonly CcScopeUnit[],
  runId: number | null
): { scope: CcRunScope; note: string } {
  if (runId === null || runId === scope.lastRunId) {
    return scope.lastRunId === null ? { scope, note: '' } : { scope: { ...scope, lastRunId: null }, note: '' };
  }
  const next: CcRunScope = { ...scope, lastRunId: runId };
  const bounds = spanBounds(rows, next);
  if (bounds.first && bounds.last && compareUnits(bounds.first, bounds.last) > 0) {
    const noun = unitNoun(scope.unitKind);
    return {
      scope: { ...next, firstRunId: null },
      note: `${capitalized(noun)} #${runId} is before the first ${noun}, so the first ${noun} was cleared.`
    };
  }
  return { scope: next, note: '' };
}

/** Includes (`include` true) or leaves out a unit. */
export function toggleLeftOut(scope: CcRunScope, runId: number, include: boolean): CcRunScope {
  if (include !== scope.leftOut.has(runId)) return scope;
  const leftOut = new Set(scope.leftOut);
  if (include) leftOut.delete(runId); else leftOut.add(runId);
  return { ...scope, leftOut };
}

/** `#45`, `#45 and #51`, `#45, #51 and #60`, then `and 3 more` past the limit. */
function idListText(ids: readonly number[]): string {
  const named = ids.slice(0, NOTE_ID_LIMIT).map(id => `#${id}`);
  const rest = ids.length - named.length;
  if (rest > 0) return `${named.join(', ')} and ${rest} more`;
  if (named.length === 1) return named[0];
  return `${named.slice(0, -1).join(', ')} and ${named[named.length - 1]}`;
}

/**
 * Drops the marks and left-out ids whose units are no longer in `rows`, and names what it dropped.
 * Left-out ids of units still listed are kept, even outside the span. Returns the same scope and an
 * empty note when nothing was dropped.
 */
export function pruneScope(scope: CcRunScope, rows: readonly CcScopeUnit[]): { scope: CcRunScope; note: string } {
  const listed = new Set(rows.map(unitIdOf));
  const dropFirst = scope.firstRunId !== null && !listed.has(scope.firstRunId);
  const dropLast = scope.lastRunId !== null && !listed.has(scope.lastRunId);
  const droppedLeftOut = [...scope.leftOut].filter(id => !listed.has(id)).sort((a, b) => a - b);
  if (!dropFirst && !dropLast && droppedLeftOut.length === 0) return { scope, note: '' };

  const noun = unitNoun(scope.unitKind);
  const notes: string[] = [];
  if (dropFirst && dropLast && scope.firstRunId === scope.lastRunId) {
    notes.push(`The first-run and last-run marks on ${noun} #${scope.firstRunId} were cleared because the ${noun} is no longer listed.`);
  } else if (dropFirst && dropLast) {
    notes.push(
      `The first-run mark on ${noun} #${scope.firstRunId} and the last-run mark on ${noun} #${scope.lastRunId} were cleared ` +
        `because the ${noun}s are no longer listed.`
    );
  } else if (dropFirst) {
    notes.push(`The first-run mark on ${noun} #${scope.firstRunId} was cleared because the ${noun} is no longer listed.`);
  } else if (dropLast) {
    notes.push(`The last-run mark on ${noun} #${scope.lastRunId} was cleared because the ${noun} is no longer listed.`);
  }
  if (droppedLeftOut.length === 1) {
    notes.push(`Left-out ${noun} #${droppedLeftOut[0]} is no longer listed and was dropped from the selection.`);
  } else if (droppedLeftOut.length > 1) {
    notes.push(
      `${plural(droppedLeftOut.length, `left-out ${noun}`)} (${idListText(droppedLeftOut)}) are no longer listed ` +
        'and were dropped from the selection.'
    );
  }

  const leftOut = droppedLeftOut.length === 0 ? scope.leftOut : new Set([...scope.leftOut].filter(id => listed.has(id)));
  return {
    scope: {
      ...scope,
      firstRunId: dropFirst ? null : scope.firstRunId,
      lastRunId: dropLast ? null : scope.lastRunId,
      leftOut
    },
    note: notes.join(' ')
  };
}

/** The UTC days of the first and last of the scoped units; null when there is none. */
export function scopeSpanDays(scoped: readonly CcScopeUnit[]): { first: string; last: string } | null {
  if (scoped.length === 0) return null;
  let first = scoped[0];
  let last = scoped[0];
  for (const row of scoped) {
    if (compareUnits(row, first) < 0) first = row;
    if (compareUnits(row, last) > 0) last = row;
  }
  return { first: formatUtcDate(first.startedAtUtc), last: formatUtcDate(last.startedAtUtc) };
}

/**
 * `first|last|sorted left-out ids`, equal for equal selections; prefixed `{setKey}#` when a comparison
 * set is given, so the same ids in another set key differently.
 */
export function scopeKey(scope: CcRunScope, setKey: string | null = null): string {
  const leftOut = [...scope.leftOut].sort((a, b) => a - b).join(',');
  const key = `${scope.firstRunId ?? ''}|${scope.lastRunId ?? ''}|${leftOut}`;
  return setKey ? `${setKey}#${key}` : key;
}

// --- Comparison sets ---

/** The unit kind of a comparison set, by its key: battery runs in a battery set, runs otherwise. */
export function setUnitKind(setKey: string | null | undefined): CcUnitKind {
  return setKey?.startsWith(CC_BATTERY_SET_PREFIX) ? 'batteryRun' : 'run';
}

/** The runs of a suite set; every run when the key names no suite set. */
export function suiteSetRuns(rows: readonly CcRunRow[], setKey: string | null | undefined): readonly CcRunRow[] {
  if (!setKey?.startsWith(CC_SUITE_SET_PREFIX)) return rows;
  const suiteKey = setKey.slice(CC_SUITE_SET_PREFIX.length);
  return rows.filter(row => row.suiteKey === suiteKey);
}

/** The battery runs of a battery set; none when the key names no battery set. */
export function batterySetRuns(batteryRows: readonly CcBatteryRunRow[], setKey: string | null | undefined): CcBatteryRunRow[] {
  if (!setKey?.startsWith(CC_BATTERY_SET_PREFIX)) return [];
  return batteryRows.filter(row => row.setKey === setKey);
}

/** The units of a set: its battery runs in a battery set, else its runs (every run without a set). */
export function comparisonSetUnits(
  rows: readonly CcRunRow[],
  batteryRows: readonly CcBatteryRunRow[],
  setKey: string | null | undefined
): readonly CcScopeUnit[] {
  return setUnitKind(setKey) === 'batteryRun' ? batterySetRuns(batteryRows, setKey) : suiteSetRuns(rows, setKey);
}

/**
 * The runs in the dates that the analysis does not use, keyed by run id, for the timeline: a run
 * outside the compared set is `outsideSet`; a member of a battery run takes its battery run's reason.
 */
export function notAnalyzedSetRuns(
  rows: readonly CcRunRow[],
  batteryRows: readonly CcBatteryRunRow[],
  setKey: string | null | undefined,
  scope: CcRunScope
): ReadonlyMap<number, CcRunInclusion> {
  if (!setKey) return notAnalyzedRuns(rows, scope);
  const result = new Map<number, CcRunInclusion>();
  const inSet = new Set<number>();
  if (setUnitKind(setKey) === 'batteryRun') {
    const units = batterySetRuns(batteryRows, setKey);
    const excluded = notAnalyzedRuns(units, scope);
    for (const unit of units) {
      const inclusion = excluded.get(unit.batteryRunId);
      for (const member of unit.members) {
        inSet.add(member.runId);
        if (inclusion) result.set(member.runId, inclusion);
      }
    }
  } else {
    const setRows = suiteSetRuns(rows, setKey);
    for (const row of setRows) inSet.add(row.runId);
    for (const [runId, inclusion] of notAnalyzedRuns(setRows, scope)) result.set(runId, inclusion);
  }
  for (const row of chronological(rows)) {
    if (!inSet.has(row.runId)) result.set(row.runId, 'outsideSet');
  }
  return result;
}

/** The runs the analysis uses: the members of the scoped battery runs in a battery set, else the scoped runs. */
export function scopedMemberRuns(scoped: readonly CcScopeUnit[]): CcRunRow[] {
  return scoped.flatMap(unit => (isBatteryRunRow(unit) ? unit.members : [unit]));
}

/** No mark and no left-out unit: every unit in the dates is analyzed. */
export function scopeIsDefault(scope: CcRunScope): boolean {
  return scope.firstRunId === null && scope.lastRunId === null && scope.leftOut.size === 0;
}

/** The number of marks plus left-out units. */
export function scopeChangeCount(scope: CcRunScope): number {
  return (scope.firstRunId !== null ? 1 : 0) + (scope.lastRunId !== null ? 1 : 0) + scope.leftOut.size;
}
