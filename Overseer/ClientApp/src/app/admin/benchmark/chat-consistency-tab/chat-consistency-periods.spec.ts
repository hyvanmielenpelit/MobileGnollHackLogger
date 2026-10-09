import {
  CC_NO_PERIOD_IDS,
  CC_PERIOD_BOUND_LABELS,
  CcPeriodIds,
  ccBeforeAfter,
  ccConfirmOnLaterData,
  ccEarliestVsLatest,
  ccIdsFromUnits,
  ccPeriodAssignment,
  ccPeriodMembers,
  ccPeriodUnits,
  ccPeriodWindows,
  ccPeriodsRefusal,
  ccPruneIds,
  ccToggleBound
} from './chat-consistency-periods';
import { CcRunRow } from './chat-consistency.models';
import { ccBatteryRunRow, ccBatteryRunRows, ccRunRow, ccRunRows } from './chat-consistency-tab.testing';

/** A run no axis can use. */
function ineligible(runId: number, startedAtUtc: string): CcRunRow {
  return ccRunRow(runId, startedAtUtc, {
    eligibility: [{ axis: 'quality', eligible: false, segment: null, reason: 'No grades' }]
  });
}

function ids(baselineFirstId: number | null, baselineLastId: number | null,
  comparisonFirstId: number | null, comparisonLastId: number | null): CcPeriodIds {
  return { baselineFirstId, baselineLastId, comparisonFirstId, comparisonLastId };
}

/** Runs 101–106 of {@link ccRunRows}, from 2026-09-01 to 2026-10-01. */
const sixRuns = () => ccPeriodUnits(ccRunRows(), [], false);

/** Battery runs #11 (06:00) and #12 (10:00), both on 2026-10-08. */
const twoBatteryRuns = () => ccPeriodUnits([], ccBatteryRunRows(), true);

describe('chat consistency periods', () => {
  describe('ccPeriodUnits', () => {
    it('orders the runs by start, then id, and keeps their eligibility', () => {
      const units = ccPeriodUnits([
        ccRunRow(3, '2026-09-02T08:00:00Z'),
        ccRunRow(2, '2026-09-01T08:00:00Z'),
        ineligible(1, '2026-09-02T08:00:00Z')
      ], [], false);
      expect(units.map(unit => unit.id)).toEqual([2, 1, 3]);
      expect(units.map(unit => unit.eligible)).toEqual([true, false, true]);
      expect(units.map(unit => unit.day)).toEqual(['2026-09-01', '2026-09-02', '2026-09-02']);
      expect(units[0].runs.map(run => run.runId)).toEqual([2]);
      expect(units[0].battery).toBeNull();
    });

    it('takes the complete battery runs in a battery set, their members as the runs', () => {
      const incomplete = ccBatteryRunRow(13, '2026-10-08T12:00:00Z', [305], { complete: false, incompleteReason: '1 of 2 suites usable' });
      const units = ccPeriodUnits(ccRunRows(), [...ccBatteryRunRows(), incomplete], true);
      expect(units.map(unit => unit.id)).toEqual([11, 12]);
      expect(units[1].runs.map(run => run.runId)).toEqual([303, 304]);
      expect(units[1].battery?.batteryRunId).toBe(12);
    });
  });

  describe('ccPeriodAssignment', () => {
    it('puts each unit in a period, between them, or out as not eligible', () => {
      const rows = ccRunRows().map(row => row.runId === 102 ? ineligible(102, row.startedAtUtc) : row);
      const units = ccPeriodUnits(rows, [], false);
      const choice = ids(101, 102, 104, 105);
      expect([...ccPeriodAssignment(units, choice)]).toEqual([
        [101, 'baseline'], [102, 'notEligible'], [103, 'notUsed'], [104, 'comparison'], [105, 'comparison'], [106, 'notUsed']
      ]);
      const members = ccPeriodMembers(units, choice);
      expect(members.baseline.map(unit => unit.id)).toEqual([101]);
      expect(members.comparison.map(unit => unit.id)).toEqual([104, 105]);
    });
  });

  describe('ccPeriodsRefusal', () => {
    it('accepts ordered, separate ranges', () => {
      expect(ccPeriodsRefusal(sixRuns(), ids(101, 103, 104, 106), false)).toBe('');
    });

    it('asks for two units in step 1', () => {
      const one = ccPeriodUnits([ccRunRow(101, '2026-09-01T08:00:00Z')], [], false);
      expect(ccPeriodsRefusal(one, ids(101, 101, 101, 101), false)).toBe('Choose at least two runs in step 1, one for each period.');
      const oneBattery = ccPeriodUnits([], [ccBatteryRunRows()[0]], true);
      expect(ccPeriodsRefusal(oneBattery, ids(12, 12, 12, 12), true))
        .toBe('Choose at least two battery runs in step 1, one for each period.');
    });

    it('asks for all four choices, an id outside the units counting as none', () => {
      expect(ccPeriodsRefusal(sixRuns(), ids(101, 103, 104, null), false)).toBe('Choose the first and last run of both periods.');
      expect(ccPeriodsRefusal(sixRuns(), ids(101, 103, 999, 106), false)).toBe('Choose the first and last run of both periods.');
    });

    it('refuses a period whose last run comes before its first', () => {
      expect(ccPeriodsRefusal(sixRuns(), ids(103, 101, 104, 106), false)).toBe('The baseline\'s last run comes before its first.');
      expect(ccPeriodsRefusal(sixRuns(), ids(101, 103, 106, 104), false)).toBe('The comparison\'s last run comes before its first.');
    });

    it('refuses a comparison that does not start after the baseline\'s last run', () => {
      expect(ccPeriodsRefusal(sixRuns(), ids(101, 103, 103, 106), false)).toBe('The comparison must start after the baseline\'s last run.');
      expect(ccPeriodsRefusal(sixRuns(), ids(102, 104, 101, 106), false)).toBe('The comparison must start after the baseline\'s last run.');
    });

    it('refuses a split between two units that started at the same moment', () => {
      const runs = ccPeriodUnits([ccRunRow(202, '2026-10-08T10:00:00Z'), ccRunRow(201, '2026-10-08T10:00:00Z')], [], false);
      expect(ccPeriodsRefusal(runs, ids(201, 201, 202, 202), false))
        .toBe('Run #201 and run #202 started at the same moment and cannot be split.');
      const batteries = ccPeriodUnits([], [
        ccBatteryRunRow(12, '2026-10-08T10:00:00Z', [303, 304]),
        ccBatteryRunRow(11, '2026-10-08T10:00:00Z', [301, 302])
      ], true);
      expect(ccPeriodsRefusal(batteries, ids(11, 11, 12, 12), true))
        .toBe('Battery run #11 and battery run #12 started at the same moment and cannot be split.');
    });

    it('refuses a period without an eligible unit', () => {
      const rows = ccRunRows().map(row => [101, 106].includes(row.runId) ? ineligible(row.runId, row.startedAtUtc) : row);
      const units = ccPeriodUnits(rows, [], false);
      expect(ccPeriodsRefusal(units, ids(101, 101, 102, 105), false)).toBe('The baseline has no eligible run.');
      expect(ccPeriodsRefusal(units, ids(102, 105, 106, 106), false)).toBe('The comparison has no eligible run.');
    });
  });

  describe('ccPeriodWindows', () => {
    it('takes whole UTC days when the split falls between days', () => {
      expect(ccPeriodWindows(sixRuns(), ids(101, 103, 104, 106))).toEqual({
        baselineStartUtc: '2026-09-01T00:00:00.000Z',
        baselineEndUtc: '2026-09-12T23:59:59.999Z',
        comparisonStartUtc: '2026-09-20T00:00:00.000Z',
        comparisonEndUtc: '2026-10-01T23:59:59.999Z'
      });
    });

    it('splits one day at the comparison\'s first start, the baseline ending 1 ms before it', () => {
      expect(ccPeriodWindows(twoBatteryRuns(), ids(11, 11, 12, 12))).toEqual({
        baselineStartUtc: '2026-10-08T00:00:00.000Z',
        baselineEndUtc: '2026-10-08T09:59:59.999Z',
        comparisonStartUtc: '2026-10-08T10:00:00.000Z',
        comparisonEndUtc: '2026-10-08T23:59:59.999Z'
      });
    });

    it('splits one day at the preset\'s anchor when it lies between the two starts', () => {
      const split = (anchor: string) => {
        const windows = ccPeriodWindows(twoBatteryRuns(), ids(11, 11, 12, 12), anchor)!;
        return [windows.baselineEndUtc, windows.comparisonStartUtc];
      };
      expect(split('2026-10-08T08:30:00Z')).toEqual(['2026-10-08T08:29:59.999Z', '2026-10-08T08:30:00.000Z']);
      // At the comparison's first start is still between; at the baseline's last, before it or after the comparison's first is not.
      expect(split('2026-10-08T10:00:00Z')).toEqual(['2026-10-08T09:59:59.999Z', '2026-10-08T10:00:00.000Z']);
      expect(split('2026-10-08T06:00:00Z')).toEqual(['2026-10-08T09:59:59.999Z', '2026-10-08T10:00:00.000Z']);
      expect(split('2026-10-08T05:00:00Z')).toEqual(['2026-10-08T09:59:59.999Z', '2026-10-08T10:00:00.000Z']);
      expect(split('2026-10-08T11:00:00Z')).toEqual(['2026-10-08T09:59:59.999Z', '2026-10-08T10:00:00.000Z']);
    });

    it('ignores the anchor when the split falls between days', () => {
      expect(ccPeriodWindows(sixRuns(), ids(101, 103, 104, 106), '2026-09-15T00:00:00Z')!.comparisonStartUtc)
        .toBe('2026-09-20T00:00:00.000Z');
    });

    it('has no windows while the choices are refused', () => {
      expect(ccPeriodWindows(sixRuns(), ids(101, 103, 103, 106))).toBeNull();
      expect(ccPeriodWindows(sixRuns(), CC_NO_PERIOD_IDS)).toBeNull();
    });
  });

  describe('Earliest vs latest', () => {
    it('splits battery runs #11 and #12 of 2026-10-08 into a baseline of #11 and a comparison of #12', () => {
      const units = twoBatteryRuns();
      const outcome = ccEarliestVsLatest(units, true);
      expect(outcome.ids).toEqual(ids(11, 11, 12, 12));
      expect(outcome.note).toBe('The runs span 1 day: the earlier half against the later half, 1 battery run each.');
      expect(outcome.anchorUtc).toBeNull();
      expect(ccPeriodsRefusal(units, outcome.ids, true)).toBe('');
      expect(ccPeriodWindows(units, outcome.ids)).not.toBeNull();
    });

    it('takes the first and last 14 days of a span of 28 days or more', () => {
      const outcome = ccEarliestVsLatest(sixRuns(), false);
      expect(outcome.ids).toEqual(ids(101, 103, 104, 106));
      expect(outcome.note).toBe('The runs span 31 days: the first 14 days against the last 14 days, 3 runs each.');
    });

    it('takes exactly 14 days at each end of a 28-day span', () => {
      const rows = [
        ccRunRow(1, '2026-09-01T08:00:00Z'), ccRunRow(2, '2026-09-14T08:00:00Z'), ccRunRow(3, '2026-09-15T08:00:00Z'),
        ccRunRow(4, '2026-09-15T08:00:00Z'), ccRunRow(5, '2026-09-28T08:00:00Z')
      ];
      const outcome = ccEarliestVsLatest(ccPeriodUnits(rows, [], false), false);
      // 2026-09-01 to 2026-09-28 is 28 days: 09-01..09-14 against 09-15..09-28.
      expect(outcome.ids).toEqual(ids(1, 2, 3, 5));
      expect(outcome.note).toBe('The runs span 28 days: the first 14 days against the last 14 days, 2 runs against 3.');
    });

    it('splits a shorter span of several days at the day boundary that best balances the counts', () => {
      const rows = [
        ccRunRow(1, '2026-09-01T08:00:00Z'), ccRunRow(2, '2026-09-01T09:00:00Z'),
        ccRunRow(3, '2026-09-05T08:00:00Z'), ccRunRow(4, '2026-09-06T08:00:00Z')
      ];
      const outcome = ccEarliestVsLatest(ccPeriodUnits(rows, [], false), false);
      expect(outcome.ids).toEqual(ids(1, 2, 3, 4));
      expect(outcome.note).toBe('The runs span 6 days: the earlier days against the later days, split at 2026-09-05, 2 runs each.');
    });

    it('puts more in the baseline when two day boundaries balance equally', () => {
      const rows = [
        ccRunRow(1, '2026-09-01T08:00:00Z'), ccRunRow(2, '2026-09-02T08:00:00Z'),
        ccRunRow(3, '2026-09-02T09:00:00Z'), ccRunRow(4, '2026-09-03T08:00:00Z')
      ];
      const outcome = ccEarliestVsLatest(ccPeriodUnits(rows, [], false), false);
      expect(outcome.ids).toEqual(ids(1, 3, 4, 4));
      expect(outcome.note).toBe('The runs span 3 days: the earlier days against the later days, split at 2026-09-03, 3 runs against 1.');
    });

    it('splits one day by count, an odd one out going to the baseline', () => {
      const rows = [
        ccRunRow(3, '2026-09-01T10:00:00Z'), ccRunRow(1, '2026-09-01T08:00:00Z'), ccRunRow(2, '2026-09-01T09:00:00Z')
      ];
      const outcome = ccEarliestVsLatest(ccPeriodUnits(rows, [], false), false);
      expect(outcome.ids).toEqual(ids(1, 2, 3, 3));
      expect(outcome.note).toBe('The runs span 1 day: the earlier half against the later half, 2 runs against 1.');
    });

    it('works on the eligible units only', () => {
      const rows = [
        ineligible(1, '2026-09-01T08:00:00Z'), ccRunRow(2, '2026-09-01T09:00:00Z'), ccRunRow(3, '2026-09-01T10:00:00Z')
      ];
      expect(ccEarliestVsLatest(ccPeriodUnits(rows, [], false), false).ids).toEqual(ids(2, 2, 3, 3));
    });

    it('cannot apply with fewer than two eligible units', () => {
      const one = ccPeriodUnits([ccRunRow(1, '2026-09-01T08:00:00Z'), ineligible(2, '2026-09-02T08:00:00Z')], [], false);
      expect(ccEarliestVsLatest(one, false)).toEqual({
        ids: CC_NO_PERIOD_IDS, note: 'Choose at least two runs in step 1, one for each period.', anchorUtc: null
      });
      expect(ccEarliestVsLatest([], true).note).toBe('Choose at least two battery runs in step 1, one for each period.');
    });
  });

  describe('Before vs after', () => {
    it('takes every unit before the anchor against every one from it, with no window around it', () => {
      const outcome = ccBeforeAfter(sixRuns(), '2026-09-15T00:00:00Z', false, 'the annotation');
      expect(outcome.ids).toEqual(ids(101, 103, 104, 106));
      expect(outcome.anchorUtc).toBe('2026-09-15T00:00:00Z');
      expect(outcome.note).toBe('The runs before the annotation (2026-09-15 00:00 UTC) against those from it: 3 runs each.');
    });

    it('puts a unit started at the anchor in the comparison', () => {
      expect(ccBeforeAfter(sixRuns(), '2026-09-20T08:00:00Z', false, 'the annotation').ids).toEqual(ids(101, 103, 104, 106));
    });

    it('splits one day at the anchor, which the windows then use', () => {
      const units = twoBatteryRuns();
      const outcome = ccBeforeAfter(units, '2026-10-08T08:00:00Z', true, 'the Overseer change E2');
      expect(outcome.ids).toEqual(ids(11, 11, 12, 12));
      expect(outcome.note).toBe('The battery runs before the Overseer change E2 (2026-10-08 08:00 UTC) against those from it: 1 battery run each.');
      expect(ccPeriodWindows(units, outcome.ids, outcome.anchorUtc)!.comparisonStartUtc).toBe('2026-10-08T08:00:00.000Z');
    });

    it('says when one side of the anchor has no unit', () => {
      expect(ccBeforeAfter(sixRuns(), '2026-08-01T00:00:00Z', false, 'the annotation')).toEqual({
        ids: CC_NO_PERIOD_IDS, note: 'No run on one side of the annotation (2026-08-01).', anchorUtc: null
      });
      expect(ccBeforeAfter(twoBatteryRuns(), '2026-10-09T00:00:00Z', true, 'the Overseer change E2').note)
        .toBe('No battery run on one side of the Overseer change E2 (2026-10-09).');
    });
  });

  describe('Confirm on later data', () => {
    const last = { baselineStartUtc: '2026-09-01T00:00:00Z', baselineEndUtc: '2026-09-14T23:59:59.999Z', createdAtUtc: '2026-09-20T07:00:00Z' };

    it('takes the last baseline\'s units against those started after the analysis was saved, as an instant', () => {
      const outcome = ccConfirmOnLaterData(sixRuns(), last, false);
      // Run 104 started at 08:00 on the day the analysis was saved at 07:00.
      expect(outcome.ids).toEqual(ids(101, 103, 104, 106));
      expect(outcome.note).toBe('Compares the runs after the last analysis, saved 2026-09-20 07:00 UTC, with its baseline.');
      expect(outcome.anchorUtc).toBeNull();
    });

    it('says when the last baseline\'s units are not in the step-1 selection', () => {
      const outcome = ccConfirmOnLaterData(sixRuns(), { ...last, baselineStartUtc: '2026-08-01T00:00:00Z', baselineEndUtc: '2026-08-10T23:59:59.999Z' }, false);
      expect(outcome.ids).toEqual(CC_NO_PERIOD_IDS);
      expect(outcome.note).toBe('The last analysis\'s baseline runs are not in the step-1 selection.');
    });

    it('says when no unit was made after the analysis was saved', () => {
      const outcome = ccConfirmOnLaterData(sixRuns(), { ...last, createdAtUtc: '2026-10-02T00:00:00Z' }, false);
      expect(outcome.ids).toEqual(CC_NO_PERIOD_IDS);
      expect(outcome.note).toBe('No run was made after the last analysis was saved (2026-10-02 00:00 UTC).');
    });
  });

  describe('choices from a saved analysis and pruning', () => {
    it('names the earliest and latest of each period\'s units found among the units', () => {
      expect(ccIdsFromUnits(sixRuns(), [103, 101, 999], [200])).toEqual(ids(101, 103, null, null));
      expect(ccIdsFromUnits(twoBatteryRuns(), [11], [12])).toEqual(ids(11, 11, 12, 12));
    });

    it('clears a choice whose unit left the scope, and keeps the object when nothing did', () => {
      const units = sixRuns();
      expect(ccPruneIds(units, ids(101, 103, 104, 999))).toEqual(ids(101, 103, 104, null));
      const kept = ids(101, 103, 104, 106);
      expect(ccPruneIds(units, kept)).toBe(kept);
    });
  });

  describe('ccToggleBound', () => {
    it('sets an unset bound on the unit', () => {
      expect(ccToggleBound(ids(101, 103, null, 106), 'comparisonFirstId', 104)).toEqual(ids(101, 103, 104, 106));
    });

    it('moves a bound from the unit that held it, leaving the other three untouched', () => {
      const before = ids(101, 103, 104, 106);
      expect(ccToggleBound(before, 'baselineLastId', 102)).toEqual(ids(101, 102, 104, 106));
      // Out of order is allowed; the refusal reports it.
      expect(ccToggleBound(before, 'comparisonFirstId', 101)).toEqual(ids(101, 103, 101, 106));
      expect(before).toEqual(ids(101, 103, 104, 106));
    });

    it('clears the bound when the unit already holds it', () => {
      expect(ccToggleBound(ids(101, 103, 104, 106), 'baselineFirstId', 101)).toEqual(ids(null, 103, 104, 106));
      // A unit may hold two bounds; pressing one leaves the other.
      expect(ccToggleBound(ids(11, 11, 12, 12), 'baselineLastId', 11)).toEqual(ids(11, null, 12, 12));
    });

    it('names each bound for a sentence', () => {
      expect(CC_PERIOD_BOUND_LABELS).toEqual({
        baselineFirstId: 'the baseline\'s first run',
        baselineLastId: 'the baseline\'s last run',
        comparisonFirstId: 'the comparison\'s first run',
        comparisonLastId: 'the comparison\'s last run'
      });
    });
  });
});
