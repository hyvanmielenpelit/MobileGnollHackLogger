/**
 * The run selection of the Chat Consistency wizard's step 1: an optional first and last run and the
 * runs the admin left out. Pure; runs are ordered chronologically by `startedAtUtc`, ties by
 * `runId`, never by the card list's sort. A function that changes the selection returns a new scope
 * object, so components can compare scopes by identity; a no-op returns its input.
 */

import { formatUtcDate, plural, utcMillis } from './chat-consistency-format';
import { CcRunRow } from './chat-consistency.models';

export interface CcRunScope {
  firstRunId: number | null;
  lastRunId: number | null;
  /** Run ids unchecked by the admin. */
  leftOut: ReadonlySet<number>;
}

export const CC_EMPTY_SCOPE: CcRunScope = Object.freeze<CcRunScope>({
  firstRunId: null,
  lastRunId: null,
  leftOut: new Set<number>()
});

export type CcRunInclusion = 'included' | 'leftOut' | 'beforeSpan' | 'afterSpan';

export const CC_INCLUSION_TEXT: Readonly<Record<Exclude<CcRunInclusion, 'included'>, string>> = Object.freeze({
  leftOut: 'left out in step 1',
  beforeSpan: 'before the first run',
  afterSpan: 'after the last run'
});

/** At most this many run ids are named in a note. */
const NOTE_ID_LIMIT = 10;

function orderKey(row: CcRunRow): number {
  const at = utcMillis(row.startedAtUtc);
  return Number.isFinite(at) ? at : Number.POSITIVE_INFINITY;
}

function compareRuns(a: CcRunRow, b: CcRunRow): number {
  const ka = orderKey(a);
  const kb = orderKey(b);
  if (ka < kb) return -1;
  if (ka > kb) return 1;
  return a.runId - b.runId;
}

function chronological(rows: readonly CcRunRow[]): CcRunRow[] {
  return [...rows].sort(compareRuns);
}

interface SpanBounds {
  first: CcRunRow | null;
  last: CcRunRow | null;
}

/** The rows holding the marks; a mark whose run is not in `rows` is an open bound. */
function spanBounds(rows: readonly CcRunRow[], scope: CcRunScope): SpanBounds {
  const find = (id: number | null) => (id === null ? null : rows.find(row => row.runId === id) ?? null);
  return { first: find(scope.firstRunId), last: find(scope.lastRunId) };
}

function inclusionOf(row: CcRunRow, bounds: SpanBounds, scope: CcRunScope): CcRunInclusion {
  if (bounds.first && compareRuns(row, bounds.first) < 0) return 'beforeSpan';
  if (bounds.last && compareRuns(row, bounds.last) > 0) return 'afterSpan';
  return scope.leftOut.has(row.runId) ? 'leftOut' : 'included';
}

/**
 * Whether a run is in the analysis. The span decides first: a left-out run outside [first, last]
 * reads as `beforeSpan` / `afterSpan`, which is what its disabled checkbox shows.
 */
export function runInclusion(row: CcRunRow, rows: readonly CcRunRow[], scope: CcRunScope): CcRunInclusion {
  return inclusionOf(row, spanBounds(rows, scope), scope);
}

/** The runs in the analysis: inside [first, last] (an absent bound is open) and not left out, oldest first. */
export function scopeRuns(rows: readonly CcRunRow[], scope: CcRunScope): CcRunRow[] {
  const bounds = spanBounds(rows, scope);
  return chronological(rows).filter(row => inclusionOf(row, bounds, scope) === 'included');
}

/** The runs not in the analysis, keyed by run id with the reason, in chronological order. */
export function notAnalyzedRuns(rows: readonly CcRunRow[], scope: CcRunScope): ReadonlyMap<number, CcRunInclusion> {
  const bounds = spanBounds(rows, scope);
  const result = new Map<number, CcRunInclusion>();
  for (const row of chronological(rows)) {
    const inclusion = inclusionOf(row, bounds, scope);
    if (inclusion !== 'included') result.set(row.runId, inclusion);
  }
  return result;
}

/**
 * Marks `runId` as the first run; choosing the run that already holds the mark, or null, clears it.
 * A first run later than the last run clears the last run, and the note says so.
 */
export function setFirstRun(
  scope: CcRunScope,
  rows: readonly CcRunRow[],
  runId: number | null
): { scope: CcRunScope; note: string } {
  if (runId === null || runId === scope.firstRunId) {
    return scope.firstRunId === null ? { scope, note: '' } : { scope: { ...scope, firstRunId: null }, note: '' };
  }
  const next: CcRunScope = { ...scope, firstRunId: runId };
  const bounds = spanBounds(rows, next);
  if (bounds.first && bounds.last && compareRuns(bounds.first, bounds.last) > 0) {
    return {
      scope: { ...next, lastRunId: null },
      note: `Run #${runId} is after the last run, so the last run was cleared.`
    };
  }
  return { scope: next, note: '' };
}

/**
 * Marks `runId` as the last run; choosing the run that already holds the mark, or null, clears it.
 * A last run earlier than the first run clears the first run, and the note says so.
 */
export function setLastRun(
  scope: CcRunScope,
  rows: readonly CcRunRow[],
  runId: number | null
): { scope: CcRunScope; note: string } {
  if (runId === null || runId === scope.lastRunId) {
    return scope.lastRunId === null ? { scope, note: '' } : { scope: { ...scope, lastRunId: null }, note: '' };
  }
  const next: CcRunScope = { ...scope, lastRunId: runId };
  const bounds = spanBounds(rows, next);
  if (bounds.first && bounds.last && compareRuns(bounds.first, bounds.last) > 0) {
    return {
      scope: { ...next, firstRunId: null },
      note: `Run #${runId} is before the first run, so the first run was cleared.`
    };
  }
  return { scope: next, note: '' };
}

/** Includes (`include` true) or leaves out a run. */
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
 * Drops the marks and left-out ids whose runs are no longer in `rows`, and names what it dropped.
 * Left-out ids of runs still listed are kept, even outside the span. Returns the same scope and an
 * empty note when nothing was dropped.
 */
export function pruneScope(scope: CcRunScope, rows: readonly CcRunRow[]): { scope: CcRunScope; note: string } {
  const listed = new Set(rows.map(row => row.runId));
  const dropFirst = scope.firstRunId !== null && !listed.has(scope.firstRunId);
  const dropLast = scope.lastRunId !== null && !listed.has(scope.lastRunId);
  const droppedLeftOut = [...scope.leftOut].filter(id => !listed.has(id)).sort((a, b) => a - b);
  if (!dropFirst && !dropLast && droppedLeftOut.length === 0) return { scope, note: '' };

  const notes: string[] = [];
  if (dropFirst && dropLast && scope.firstRunId === scope.lastRunId) {
    notes.push(`The first-run and last-run marks on run #${scope.firstRunId} were cleared because the run is no longer listed.`);
  } else if (dropFirst && dropLast) {
    notes.push(
      `The first-run mark on run #${scope.firstRunId} and the last-run mark on run #${scope.lastRunId} were cleared ` +
        'because the runs are no longer listed.'
    );
  } else if (dropFirst) {
    notes.push(`The first-run mark on run #${scope.firstRunId} was cleared because the run is no longer listed.`);
  } else if (dropLast) {
    notes.push(`The last-run mark on run #${scope.lastRunId} was cleared because the run is no longer listed.`);
  }
  if (droppedLeftOut.length === 1) {
    notes.push(`Left-out run #${droppedLeftOut[0]} is no longer listed and was dropped from the selection.`);
  } else if (droppedLeftOut.length > 1) {
    notes.push(
      `${plural(droppedLeftOut.length, 'left-out run')} (${idListText(droppedLeftOut)}) are no longer listed ` +
        'and were dropped from the selection.'
    );
  }

  const leftOut = droppedLeftOut.length === 0 ? scope.leftOut : new Set([...scope.leftOut].filter(id => listed.has(id)));
  return {
    scope: {
      firstRunId: dropFirst ? null : scope.firstRunId,
      lastRunId: dropLast ? null : scope.lastRunId,
      leftOut
    },
    note: notes.join(' ')
  };
}

/** The UTC days of the first and last of the scoped runs; null when there is none. */
export function scopeSpanDays(scoped: readonly CcRunRow[]): { first: string; last: string } | null {
  if (scoped.length === 0) return null;
  let first = scoped[0];
  let last = scoped[0];
  for (const row of scoped) {
    if (compareRuns(row, first) < 0) first = row;
    if (compareRuns(row, last) > 0) last = row;
  }
  return { first: formatUtcDate(first.startedAtUtc), last: formatUtcDate(last.startedAtUtc) };
}

/** `first|last|sorted left-out ids`, equal for equal selections. */
export function scopeKey(scope: CcRunScope): string {
  const leftOut = [...scope.leftOut].sort((a, b) => a - b).join(',');
  return `${scope.firstRunId ?? ''}|${scope.lastRunId ?? ''}|${leftOut}`;
}

/** No mark and no left-out run: every run in the dates is analyzed. */
export function scopeIsDefault(scope: CcRunScope): boolean {
  return scope.firstRunId === null && scope.lastRunId === null && scope.leftOut.size === 0;
}

/** The number of marks plus left-out runs. */
export function scopeChangeCount(scope: CcRunScope): number {
  return (scope.firstRunId !== null ? 1 : 0) + (scope.lastRunId !== null ? 1 : 0) + scope.leftOut.size;
}
