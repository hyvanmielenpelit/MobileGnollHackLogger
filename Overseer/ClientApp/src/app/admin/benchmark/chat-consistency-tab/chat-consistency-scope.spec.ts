import {
  CC_EMPTY_BATTERY_SCOPE,
  CC_EMPTY_SCOPE,
  CC_INCLUSION_TEXT,
  CcRunScope,
  batterySetRuns,
  ccEmptyScope,
  comparisonSetUnits,
  notAnalyzedRuns,
  notAnalyzedSetRuns,
  notAnalyzedUnits,
  pruneScope,
  runInclusion,
  scopeChangeCount,
  scopeIsDefault,
  scopeKey,
  scopeRuns,
  scopeSpanDays,
  scopedMemberRuns,
  setFirstRun,
  setLastRun,
  setUnitKind,
  suiteSetRuns,
  toggleLeftOut,
  unitIdOf
} from './chat-consistency-scope';
import { CcBatteryRunRow, CcRunRow } from './chat-consistency.models';
import {
  CC_BATTERY_SET_KEY,
  ccBatteryMemberRows,
  ccBatteryRunRow,
  ccBatteryRunRows,
  ccRunRow
} from './chat-consistency-tab.testing';

/** Five runs two days apart, newest first as the card list shows them. */
function rows(): CcRunRow[] {
  return [
    ccRunRow(14, '2026-09-09T08:00:00Z'),
    ccRunRow(13, '2026-09-07T08:00:00Z'),
    ccRunRow(12, '2026-09-05T08:00:00Z'),
    ccRunRow(11, '2026-09-03T08:00:00Z'),
    ccRunRow(10, '2026-09-01T08:00:00Z')
  ];
}

function scope(firstRunId: number | null, lastRunId: number | null, leftOut: number[] = []): CcRunScope {
  return { firstRunId, lastRunId, leftOut: new Set(leftOut) };
}

function ids(list: readonly CcRunRow[]): number[] {
  return list.map(row => row.runId);
}

function entries(map: ReadonlyMap<number, string>): [number, string][] {
  return [...map.entries()];
}

describe('chat-consistency-scope', () => {
  describe('scopeRuns and notAnalyzedRuns', () => {
    it('analyze every run, oldest first, without marks', () => {
      expect(ids(scopeRuns(rows(), CC_EMPTY_SCOPE))).toEqual([10, 11, 12, 13, 14]);
      expect(entries(notAnalyzedRuns(rows(), CC_EMPTY_SCOPE))).toEqual([]);
    });

    it('start at the first run when only it is marked', () => {
      const s = scope(12, null);
      expect(ids(scopeRuns(rows(), s))).toEqual([12, 13, 14]);
      expect(entries(notAnalyzedRuns(rows(), s))).toEqual([[10, 'beforeSpan'], [11, 'beforeSpan']]);
    });

    it('end at the last run when only it is marked', () => {
      const s = scope(null, 12);
      expect(ids(scopeRuns(rows(), s))).toEqual([10, 11, 12]);
      expect(entries(notAnalyzedRuns(rows(), s))).toEqual([[13, 'afterSpan'], [14, 'afterSpan']]);
    });

    it('run from the first to the last run, both included', () => {
      const s = scope(11, 13);
      expect(ids(scopeRuns(rows(), s))).toEqual([11, 12, 13]);
      expect(entries(notAnalyzedRuns(rows(), s))).toEqual([[10, 'beforeSpan'], [14, 'afterSpan']]);
    });

    it('drop a left-out run inside the span and list the reasons chronologically', () => {
      const s = scope(11, 13, [12]);
      expect(ids(scopeRuns(rows(), s))).toEqual([11, 13]);
      expect(entries(notAnalyzedRuns(rows(), s))).toEqual([[10, 'beforeSpan'], [12, 'leftOut'], [14, 'afterSpan']]);
    });

    it('report a left-out run outside the span by the span, and keep it out once the span widens', () => {
      const narrow = scope(11, null, [10]);
      expect(runInclusion(rows()[4], rows(), narrow)).toBe('beforeSpan');
      expect(entries(notAnalyzedRuns(rows(), narrow))).toEqual([[10, 'beforeSpan']]);

      const widened = scope(null, null, [10]);
      expect(runInclusion(rows()[4], rows(), widened)).toBe('leftOut');
      expect(ids(scopeRuns(rows(), widened))).toEqual([11, 12, 13, 14]);
    });

    it('treat a mark whose run is not listed as an open bound', () => {
      expect(ids(scopeRuns(rows(), scope(99, 98)))).toEqual([10, 11, 12, 13, 14]);
    });

    it('allow a span of one run', () => {
      expect(ids(scopeRuns(rows(), scope(12, 12)))).toEqual([12]);
    });

    it('order runs that start at the same instant by run id', () => {
      const tied = [ccRunRow(31, '2026-09-05T08:00:00Z'), ccRunRow(30, '2026-09-05T08:00:00Z'), ccRunRow(32, '2026-09-06T08:00:00Z')];
      expect(ids(scopeRuns(tied, CC_EMPTY_SCOPE))).toEqual([30, 31, 32]);
      expect(runInclusion(tied[1], tied, scope(31, null))).toBe('beforeSpan');
      expect(runInclusion(tied[0], tied, scope(null, 30))).toBe('afterSpan');
    });

    it('order by start time, not by the timestamp text', () => {
      const mixed = [ccRunRow(2, '2026-09-05T10:00:00+02:00'), ccRunRow(1, '2026-09-05T09:00:00Z')];
      expect(ids(scopeRuns(mixed, CC_EMPTY_SCOPE))).toEqual([2, 1]);
    });

    it('never reorder or change the rows given', () => {
      const list = rows();
      scopeRuns(list, scope(11, 13, [12]));
      expect(ids(list)).toEqual([14, 13, 12, 11, 10]);
    });
  });

  describe('runInclusion', () => {
    it('classifies each run', () => {
      const s = scope(11, 13, [12]);
      const list = rows();
      expect(list.map(row => runInclusion(row, list, s))).toEqual(['afterSpan', 'included', 'leftOut', 'included', 'beforeSpan']);
    });
  });

  describe('setFirstRun and setLastRun', () => {
    it('set a mark on a new scope object without changing the input', () => {
      const before = scope(null, 13, [12]);
      const { scope: after, note } = setFirstRun(before, rows(), 11);
      expect(after).not.toBe(before);
      expect(after.firstRunId).toBe(11);
      expect(after.lastRunId).toBe(13);
      expect([...after.leftOut]).toEqual([12]);
      expect(note).toBe('');
      expect(before.firstRunId).toBeNull();
    });

    it('clear the last run when the first run is after it, and say so', () => {
      const result = setFirstRun(scope(null, 12), rows(), 14);
      expect(result.scope.firstRunId).toBe(14);
      expect(result.scope.lastRunId).toBeNull();
      expect(result.note).toBe('Run #14 is after the last run, so the last run was cleared.');
    });

    it('clear the first run when the last run is before it, and say so', () => {
      const result = setLastRun(scope(13, null), rows(), 11);
      expect(result.scope.lastRunId).toBe(11);
      expect(result.scope.firstRunId).toBeNull();
      expect(result.note).toBe('Run #11 is before the first run, so the first run was cleared.');
    });

    it('allow the same run as first and last', () => {
      const result = setLastRun(scope(12, null), rows(), 12);
      expect(result.scope).toEqual(scope(12, 12));
      expect(result.note).toBe('');
    });

    it('clear a mark when its run is chosen again', () => {
      const first = setFirstRun(scope(12, 14), rows(), 12);
      expect(first.scope).toEqual(scope(null, 14));
      expect(first.note).toBe('');

      const last = setLastRun(scope(12, 14), rows(), 14);
      expect(last.scope).toEqual(scope(12, null));
      expect(last.note).toBe('');
    });

    it('clear a mark when given null, and return the same scope when there is none', () => {
      expect(setFirstRun(scope(12, null), rows(), null).scope).toEqual(scope(null, null));
      expect(setLastRun(scope(null, 12), rows(), null).scope).toEqual(scope(null, null));
      const empty = scope(null, null);
      expect(setFirstRun(empty, rows(), null)).toEqual({ scope: empty, note: '' });
      expect(setFirstRun(empty, rows(), null).scope).toBe(empty);
      expect(setLastRun(empty, rows(), null).scope).toBe(empty);
    });

    it('move a mark from one run to another', () => {
      expect(setFirstRun(scope(11, null), rows(), 12).scope).toEqual(scope(12, null));
      expect(setLastRun(scope(null, 13), rows(), 14).scope).toEqual(scope(null, 14));
    });
  });

  describe('toggleLeftOut', () => {
    it('leaves a run out and includes it again on new scope objects', () => {
      const out = toggleLeftOut(CC_EMPTY_SCOPE, 12, false);
      expect(out).not.toBe(CC_EMPTY_SCOPE);
      expect([...out.leftOut]).toEqual([12]);
      expect(CC_EMPTY_SCOPE.leftOut.size).toBe(0);

      const back = toggleLeftOut(out, 12, true);
      expect(back).not.toBe(out);
      expect(back.leftOut.size).toBe(0);
      expect(out.leftOut.has(12)).toBe(true);
    });

    it('returns the same scope when nothing changes', () => {
      const s = scope(null, null, [12]);
      expect(toggleLeftOut(s, 12, false)).toBe(s);
      expect(toggleLeftOut(s, 13, true)).toBe(s);
    });
  });

  describe('pruneScope', () => {
    it('returns the same scope and no note when every run is still listed', () => {
      const s = scope(11, 13, [12]);
      const result = pruneScope(s, rows());
      expect(result.scope).toBe(s);
      expect(result.note).toBe('');
    });

    it('clears a first-run mark whose run is gone', () => {
      const result = pruneScope(scope(21, 13), rows());
      expect(result.scope).toEqual(scope(null, 13));
      expect(result.note).toBe('The first-run mark on run #21 was cleared because the run is no longer listed.');
    });

    it('clears a last-run mark whose run is gone', () => {
      const result = pruneScope(scope(11, 93), rows());
      expect(result.scope).toEqual(scope(11, null));
      expect(result.note).toBe('The last-run mark on run #93 was cleared because the run is no longer listed.');
    });

    it('clears both marks in one sentence', () => {
      expect(pruneScope(scope(21, 93), rows()).note).toBe(
        'The first-run mark on run #21 and the last-run mark on run #93 were cleared because the runs are no longer listed.'
      );
      expect(pruneScope(scope(21, 21), rows()).note).toBe(
        'The first-run and last-run marks on run #21 were cleared because the run is no longer listed.'
      );
    });

    it('drops left-out runs that are gone and keeps those still listed, even outside the span', () => {
      const one = pruneScope(scope(11, null, [10, 45]), rows());
      expect(one.scope).toEqual(scope(11, null, [10]));
      expect(one.note).toBe('Left-out run #45 is no longer listed and was dropped from the selection.');

      const many = pruneScope(scope(null, null, [51, 12, 45]), rows());
      expect(many.scope).toEqual(scope(null, null, [12]));
      expect(many.note).toBe('2 left-out runs (#45 and #51) are no longer listed and were dropped from the selection.');
    });

    it('names a mark and left-out runs together', () => {
      const result = pruneScope(scope(21, null, [45, 51, 60]), rows());
      expect(result.scope).toEqual(scope(null, null));
      expect(result.note).toBe(
        'The first-run mark on run #21 was cleared because the run is no longer listed. ' +
          '3 left-out runs (#45, #51 and #60) are no longer listed and were dropped from the selection.'
      );
    });

    it('empties the selection when no run is listed', () => {
      expect(pruneScope(scope(11, 13, [12]), []).scope).toEqual(scope(null, null));
    });
  });

  describe('scopeSpanDays', () => {
    it('gives the UTC days of the first and last scoped run', () => {
      expect(scopeSpanDays(scopeRuns(rows(), scope(11, 13)))).toEqual({ first: '2026-09-03', last: '2026-09-07' });
      expect(scopeSpanDays(rows())).toEqual({ first: '2026-09-01', last: '2026-09-09' });
    });

    it('is null without runs', () => {
      expect(scopeSpanDays([])).toBeNull();
    });
  });

  describe('scopeKey, scopeIsDefault and scopeChangeCount', () => {
    it('keys a scope by its marks and sorted left-out ids', () => {
      expect(scopeKey(CC_EMPTY_SCOPE)).toBe('||');
      expect(scopeKey(scope(12, 14, [45, 3, 10]))).toBe('12|14|3,10,45');
      expect(scopeKey(scope(null, 14))).toBe('|14|');
      expect(scopeKey(scope(12, null, [10, 3]))).toBe(scopeKey(scope(12, null, [3, 10])));
    });

    it('knows the default scope', () => {
      expect(scopeIsDefault(CC_EMPTY_SCOPE)).toBe(true);
      expect(scopeIsDefault(scope(null, null))).toBe(true);
      expect(scopeIsDefault(scope(12, null))).toBe(false);
      expect(scopeIsDefault(scope(null, null, [12]))).toBe(false);
    });

    it('counts the marks and left-out runs', () => {
      expect(scopeChangeCount(CC_EMPTY_SCOPE)).toBe(0);
      expect(scopeChangeCount(scope(12, null))).toBe(1);
      expect(scopeChangeCount(scope(11, 13, [12, 45]))).toBe(4);
    });
  });

  it('describes why a run is not analyzed', () => {
    expect(CC_INCLUSION_TEXT).toEqual({
      leftOut: 'left out in step 1',
      beforeSpan: 'before the first run',
      afterSpan: 'after the last run',
      incomplete: 'incomplete battery run',
      outsideSet: 'outside the compared set'
    });
  });

  describe('comparison sets', () => {
    /** Battery runs #11 and #12 of the fixtures, and an incomplete #13 with run 305 alone. */
    function batteryRows(): CcBatteryRunRow[] {
      return [
        ccBatteryRunRow(13, '2026-10-08T14:00:00Z', [305], { complete: false, incompleteReason: '1 of 2 suites usable' }),
        ...ccBatteryRunRows()
      ];
    }

    it('takes the units of a set by its key: battery runs, a suite\'s runs, or every run', () => {
      const all = [...rows(), ccRunRow(20, '2026-09-02T08:00:00Z', { suiteName: 'Wiki Suite', suiteId: 6, suiteKey: 'id:6' })];
      expect(setUnitKind(CC_BATTERY_SET_KEY)).toBe('batteryRun');
      expect(setUnitKind('suite:id:5')).toBe('run');
      expect(setUnitKind(null)).toBe('run');
      expect(comparisonSetUnits(all, batteryRows(), CC_BATTERY_SET_KEY).map(unitIdOf)).toEqual([13, 12, 11]);
      expect(ids(suiteSetRuns(all, 'suite:id:6'))).toEqual([20]);
      expect(comparisonSetUnits(all, batteryRows(), 'suite:id:5').length).toBe(5);
      expect(comparisonSetUnits(all, batteryRows(), null)).toBe(all);
      expect(batterySetRuns(batteryRows(), 'suite:id:5')).toEqual([]);
    });

    it('never analyzes an incomplete battery run, and orders battery runs by start', () => {
      const units = batteryRows();
      const battery = ccEmptyScope('batteryRun');
      expect(battery).toBe(CC_EMPTY_BATTERY_SCOPE);
      expect(ccEmptyScope('run')).toBe(CC_EMPTY_SCOPE);
      expect(scopeRuns(units, battery).map(unitIdOf)).toEqual([11, 12]);
      expect(entries(notAnalyzedRuns(units, battery))).toEqual([[13, 'incomplete']]);
      expect(runInclusion(units[0], units, { ...battery, firstRunId: 13 })).toBe('incomplete');
      expect(scopeSpanDays(scopeRuns(units, battery))).toEqual({ first: '2026-10-08', last: '2026-10-08' });
    });

    it('names battery runs in its notes', () => {
      const units = batteryRows();
      const last = setLastRun(CC_EMPTY_BATTERY_SCOPE, units, 11).scope;
      expect(setFirstRun(last, units, 12).note).toBe('Battery run #12 is after the last battery run, so the last battery run was cleared.');
      expect(pruneScope({ ...CC_EMPTY_BATTERY_SCOPE, leftOut: new Set([9]) }, units).note)
        .toBe('Left-out battery run #9 is no longer listed and was dropped from the selection.');
    });

    it('marks the runs outside the compared set, and a battery run\'s members with its reason, for the timeline', () => {
      const all = [...ccBatteryMemberRows(), ...rows()];
      const leftOut = { ...CC_EMPTY_BATTERY_SCOPE, leftOut: new Set([11]) };
      const marks = notAnalyzedSetRuns(all, ccBatteryRunRows(), CC_BATTERY_SET_KEY, leftOut);
      expect(marks.get(301)).toBe('leftOut');
      expect(marks.get(302)).toBe('leftOut');
      expect(marks.has(303)).toBe(false);
      expect(marks.get(10)).toBe('outsideSet');
      expect(marks.size).toBe(7);

      const suite = notAnalyzedSetRuns(all, [], 'suite:id:6', scope(null, null, [302]));
      expect(suite.get(302)).toBe('leftOut');
      expect(suite.has(304)).toBe(false);
      expect(suite.get(301)).toBe('outsideSet');
      expect(notAnalyzedSetRuns(rows(), [], null, scope(null, null, [12]))).toEqual(notAnalyzedRuns(rows(), scope(null, null, [12])));
    });

    it('marks the battery runs not in the analysis by battery run id, for a timeline drawn by battery run', () => {
      const units = batteryRows();
      expect(entries(notAnalyzedUnits(units, CC_BATTERY_SET_KEY, CC_EMPTY_BATTERY_SCOPE))).toEqual([[13, 'incomplete']]);
      const leftOut = { ...CC_EMPTY_BATTERY_SCOPE, leftOut: new Set([11]) };
      expect(entries(notAnalyzedUnits(units, CC_BATTERY_SET_KEY, leftOut))).toEqual([[11, 'leftOut'], [13, 'incomplete']]);
      const spanned = { ...CC_EMPTY_BATTERY_SCOPE, firstRunId: 12 };
      expect(entries(notAnalyzedUnits(units, CC_BATTERY_SET_KEY, spanned))).toEqual([[11, 'beforeSpan'], [13, 'incomplete']]);
      const ended = { ...CC_EMPTY_BATTERY_SCOPE, lastRunId: 11 };
      expect(entries(notAnalyzedUnits(ccBatteryRunRows(), CC_BATTERY_SET_KEY, ended))).toEqual([[12, 'afterSpan']]);
      // No member run id is ever a key, and a suite set or no set has no battery runs.
      expect(notAnalyzedUnits(units, CC_BATTERY_SET_KEY, leftOut).has(301)).toBe(false);
      expect(notAnalyzedUnits(units, 'suite:id:5', leftOut).size).toBe(0);
      expect(notAnalyzedUnits(units, null, leftOut).size).toBe(0);
    });

    it('hands on the members of the scoped battery runs as the runs of the analysis', () => {
      expect(ids(scopedMemberRuns(scopeRuns(ccBatteryRunRows(), CC_EMPTY_BATTERY_SCOPE)))).toEqual([301, 302, 303, 304]);
      expect(ids(scopedMemberRuns(scopeRuns(rows(), CC_EMPTY_SCOPE)))).toEqual([10, 11, 12, 13, 14]);
    });

    it('keys a scope by its set as well', () => {
      expect(scopeKey(scope(12, null), 'suite:id:5')).toBe('suite:id:5#12||');
      expect(scopeKey(scope(12, null), 'suite:id:5')).not.toBe(scopeKey(scope(12, null), 'suite:id:6'));
      expect(scopeKey(scope(12, null), null)).toBe('12||');
    });
  });
});
