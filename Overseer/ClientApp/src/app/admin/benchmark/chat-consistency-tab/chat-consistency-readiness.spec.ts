import { groupOverseerEvents } from './chat-consistency-events';
import { CcPeriodIds, ccPeriodAssignment, ccPeriodUnits } from './chat-consistency-periods';
import {
  CC_PROTOCOL_V1_ENDPOINTS,
  CC_READINESS_STATUS_TEXT,
  ccEndpointReadiness,
  ccIneligibleInPeriods,
  ccMarginText,
  ccMissingControlsText,
  ccPeriodSample,
  ccPreviewNotes,
  ccSampleCountText,
  ccSampleDayText,
  ccSampleLine,
  ccSampleNeedText,
  ccSegmentNotes,
  ccUnitEligibleOn
} from './chat-consistency-readiness';
import { CcAxisEligibility, CcRunRow, CcTimelinePoint } from './chat-consistency.models';
import { ccBatteryRunRows, ccEvent, ccRunRow, ccRunRows, ccTimeline } from './chat-consistency-tab.testing';

function ids(baselineFirstId: number | null, baselineLastId: number | null,
  comparisonFirstId: number | null, comparisonLastId: number | null): CcPeriodIds {
  return { baselineFirstId, baselineLastId, comparisonFirstId, comparisonLastId };
}

/** Runs 101–106 of {@link ccRunRows}, oldest first; run 103 has no call telemetry. */
const sixRuns = () => ccPeriodUnits(ccRunRows(), [], false);

/** Battery runs #11 and #12 of 2026-10-08. */
const twoBatteryRuns = () => ccPeriodUnits([], ccBatteryRunRows(), true);

/** The timeline points of {@link ccTimeline} by run id: every run in the stratum `weekday 08–12 UTC`. */
function pointsById(points: readonly CcTimelinePoint[] = ccTimeline().points): Map<number, CcTimelinePoint> {
  return new Map(points.map(point => [point.runId, point]));
}

/** Every axis eligible in `segment`. */
function inSegment(segment: number): CcAxisEligibility[] {
  return (['quality', 'speedTelemetry', 'speedLegacy', 'work', 'cost'] as const)
    .map(axis => ({ axis, eligible: true, segment, reason: null }));
}

const readiness = (baselineIds: number[], comparisonIds: number[], points = pointsById()) => {
  const units = sixRuns();
  return ccEndpointReadiness(
    CC_PROTOCOL_V1_ENDPOINTS,
    units.filter(unit => baselineIds.includes(unit.id)),
    units.filter(unit => comparisonIds.includes(unit.id)),
    points,
    false
  );
};

describe('chat consistency readiness', () => {
  describe('the protocol', () => {
    it('copies the server\'s endpoint axes and stratification', () => {
      expect(CC_PROTOCOL_V1_ENDPOINTS.map(endpoint => [endpoint.id, endpoint.axis, endpoint.stratified, endpoint.legacyAxis])).toEqual([
        ['P1', 'quality', false, null],
        ['P2', 'speedTelemetry', true, 'speedLegacy'],
        ['P3', 'speedTelemetry', true, null],
        ['P4', 'work', false, null],
        ['P5', 'cost', false, null]
      ]);
    });

    it('writes a margin in its unit', () => {
      expect(ccMarginText(3, 'index points')).toBe('±3 index points');
      expect(ccMarginText(15, '%')).toBe('±15 %');
    });
  });

  describe('period samples', () => {
    it('counts the units and their days against the minimum sample', () => {
      const units = sixRuns();
      const sample = ccPeriodSample(units.slice(0, 3));
      expect(sample).toEqual({ count: 3, days: 3, firstDay: '2026-09-01', lastDay: '2026-09-12', meetsMinimum: true });
      expect(ccSampleCountText(sample, false)).toBe('3 runs on 3 days');
      expect(ccSampleDayText(sample)).toBe('2026-09-01 to 2026-09-12');

      const one = ccPeriodSample(units.slice(0, 1));
      expect(one.meetsMinimum).toBe(false);
      expect(ccSampleDayText(one)).toBe('2026-09-01');
      expect(ccPeriodSample([])).toEqual({ count: 0, days: 0, firstDay: null, lastDay: null, meetsMinimum: false });
    });

    it('needs two days as well as two units', () => {
      const sample = ccPeriodSample(twoBatteryRuns());
      expect(sample).toEqual({ count: 2, days: 1, firstDay: '2026-10-08', lastDay: '2026-10-08', meetsMinimum: false });
      expect(ccSampleCountText(sample, true)).toBe('2 battery runs on 1 day');
    });

    it('writes the sample line as the Analyze step always has', () => {
      const units = sixRuns();
      expect(ccSampleLine('baseline', units.slice(0, 3), false))
        .toBe('Baseline: 3 runs on 3 days (2026-09-01 to 2026-09-12), which meets the minimum sample for P1, P4 and P5.');
      expect(ccSampleLine('comparison', twoBatteryRuns().slice(1), true))
        .toBe('Comparison: 1 battery run on 1 day (2026-10-08). P1, P4 and P5 need at least 2 on 2 days to be Established.');
      expect(ccSampleNeedText()).toBe('P1, P4 and P5 need at least 2 on 2 days to be Established.');
    });
  });

  describe('endpoint readiness', () => {
    it('meets the minimum on P1, P4 and P5, and counts the telemetry runs per common stratum for P2 and P3', () => {
      const rows = readiness([101, 102, 103], [104, 105, 106]);
      expect(rows.map(row => [row.id, row.marginText, row.status])).toEqual([
        ['P1', '±3 index points', 'meets'],
        ['P2', '±15 %', 'belowMinimum'],
        ['P3', '±10 %', 'belowMinimum'],
        ['P4', '±15 %', 'meets'],
        ['P5', '±10 %', 'meets']
      ]);
      expect(rows[0].fact).toBe('Baseline 3 runs on 3 days · Comparison 3 runs on 3 days');
      // Run 103 has no call telemetry, so the baseline has two telemetry runs in the stratum.
      expect(rows[1].fact).toBe('No common stratum has 3 runs in each period: weekday 08–12 UTC: 2 / 3.');
      expect(CC_READINESS_STATUS_TEXT[rows[1].status]).toBe('Cannot be Established');
    });

    it('meets the stratum minimum with three runs a side', () => {
      // Run 100 joins the baseline's two telemetry runs.
      const units = ccPeriodUnits([...ccRunRows(), ccRunRow(100, '2026-08-30T08:00:00Z')], [], false);
      const points = pointsById([...ccTimeline().points, { ...ccTimeline().points[0], runId: 100 }]);
      const baseline = units.filter(unit => [100, 101, 102].includes(unit.id));
      const comparison = units.filter(unit => [104, 105, 106].includes(unit.id));
      const p3 = ccEndpointReadiness(CC_PROTOCOL_V1_ENDPOINTS, baseline, comparison, points, false)[2];
      expect(p3.status).toBe('meets');
      expect(p3.fact).toBe('Runs per common stratum, baseline / comparison: weekday 08–12 UTC: 3 / 3.');
    });

    it('names the period short of the minimum sample', () => {
      const p1 = readiness([101], [104, 105, 106])[0];
      expect(p1.status).toBe('belowMinimum');
      expect(p1.fact).toBe('The baseline needs at least 2 runs on 2 days: Baseline 1 run on 1 day · Comparison 3 runs on 3 days.');
    });

    it('says when a period has no unit eligible on the endpoint\'s axis', () => {
      const noQuality = ccRunRow(110, '2026-09-02T08:00:00Z', {
        eligibility: [{ axis: 'work', eligible: true, segment: 1, reason: null }]
      });
      const baseline = ccPeriodUnits([noQuality], [], false);
      const comparison = sixRuns().slice(3);
      const p1 = ccEndpointReadiness(CC_PROTOCOL_V1_ENDPOINTS, baseline, comparison, pointsById(), false)[0];
      expect(p1).toEqual({
        id: 'P1', name: 'Quality', marginText: '±3 index points', status: 'notComputed',
        fact: 'No run in the baseline is eligible for Quality.'
      });
    });

    it('falls back to the legacy proxy for P2 when a period has no telemetry run, as the server does; P3 has none', () => {
      const rows = readiness([103], [104, 105, 106]);
      expect(rows[1].status).toBe('belowMinimum');
      expect(rows[1].fact).toBe('No common stratum has 3 runs in each period: weekday 08–12 UTC: 1 / 3. Measured as model time (legacy proxy).');
      expect(rows[2]).toEqual(expect.objectContaining({ status: 'notComputed', fact: 'No run in the baseline is eligible for Speed (telemetry).' }));
    });

    it('does not compute P2 and P3 when the periods share no time stratum', () => {
      const rows = ccEndpointReadiness(CC_PROTOCOL_V1_ENDPOINTS, twoBatteryRuns().slice(0, 1), twoBatteryRuns().slice(1), new Map(), true);
      expect(rows.map(row => row.status)).toEqual(['belowMinimum', 'notComputed', 'notComputed', 'belowMinimum', 'belowMinimum']);
      expect(rows[0].fact).toBe('Both periods need at least 2 battery runs on 2 days: Baseline 1 battery run on 1 day · Comparison 1 battery run on 1 day.');
      expect(rows[1].fact).toBe('The periods share no time stratum.');
    });

    it('reads a battery run\'s eligibility from its aggregate', () => {
      const [eleven] = twoBatteryRuns();
      expect(ccUnitEligibleOn(eleven, 'quality')).toBe(true);
      const blocked = { ...eleven, battery: { ...eleven.battery!, eligibility: [{ axis: 'quality' as const, eligible: false, segment: null, reason: '#301: No grades' }] } };
      expect(ccUnitEligibleOn(blocked, 'quality')).toBe(false);
    });
  });

  describe('notes', () => {
    it('finds a measurement segment change across the periods, refused unless pooling is on', () => {
      const rows: CcRunRow[] = [
        ccRunRow(1, '2026-09-01T08:00:00Z', { eligibility: inSegment(2) }),
        ccRunRow(2, '2026-09-02T08:00:00Z', { eligibility: inSegment(2) }),
        ccRunRow(3, '2026-09-10T08:00:00Z', { eligibility: [...inSegment(2).filter(e => e.axis !== 'speedTelemetry'), { axis: 'speedTelemetry', eligible: true, segment: 3, reason: null }] })
      ];
      const units = ccPeriodUnits(rows, [], false);
      const refused = ccSegmentNotes(units.slice(0, 2), units.slice(2), false);
      expect(refused.map(note => [note.axis, note.segments, note.refused])).toEqual([['speedTelemetry', [2, 3], true]]);
      expect(refused[0].text).toBe('Speed (telemetry) spans measurement segments 2 and 3: the analysis refuses a change of measurement '
        + 'inside the span unless Pool across measurement segment boundaries is on.');
      const pooled = ccSegmentNotes(units.slice(0, 2), units.slice(2), true);
      expect(pooled[0].refused).toBe(false);
      expect(pooled[0].text).toBe('Speed (telemetry) spans measurement segments 2 and 3; they are pooled, which caps the grades at Indicated.');
      expect(ccSegmentNotes(sixRuns().slice(0, 3), sixRuns().slice(3), false)).toEqual([]);
    });

    it('names the runs without a matched control in one line', () => {
      const runs = ccRunRows();
      expect(ccMissingControlsText(runs))
        .toBe('2 of 6 runs have no matched control run (#103, #104); the analysis looks for controls among other models\' runs itself.');
      expect(ccMissingControlsText(runs.filter(row => row.runId !== 103)))
        .toBe('1 of 5 runs has no matched control run (#104); the analysis looks for controls among other models\' runs itself.');
      expect(ccMissingControlsText(runs.filter(row => row.matchedControlRunIds.length > 0))).toBe('');
    });

    it('finds the ineligible units inside a period range, not those between the periods', () => {
      const rows = ccRunRows().map(row => row.runId === 102 || row.runId === 104
        ? ccRunRow(row.runId, row.startedAtUtc, { eligibility: [{ axis: 'quality', eligible: false, segment: null, reason: 'No grades' }] })
        : row);
      const units = ccPeriodUnits(rows, [], false);
      const choice = ids(101, 103, 105, 106);
      const found = ccIneligibleInPeriods(units, choice, ccPeriodAssignment(units, choice));
      expect(found.map(entry => [entry.unit.id, entry.period])).toEqual([[102, 'baseline']]);
    });

    it('lists the notes in reading order, one per finding', () => {
      const timeline = ccTimeline({ events: [ccEvent()] });
      const groups = groupOverseerEvents(timeline.events, timeline.points);
      const units = twoBatteryRuns();
      const notes = ccPreviewNotes({
        battery: true,
        eventGroupsInSpan: groups,
        segmentNotes: [],
        periodRuns: units.flatMap(unit => unit.runs),
        leftOutInPeriods: [13],
        ineligible: [{ unit: units[1], period: 'comparison' }]
      });
      expect(notes.map(note => [note.kind, note.severity])).toEqual([
        ['events', 'warning'], ['controls', 'info'], ['leftOut', 'info'], ['ineligible', 'info']
      ]);
      expect(notes[0].eventGroups.map(group => group.tag)).toEqual(['E1']);
      expect(notes[1].text).toBe('3 of 4 runs have no matched control run (#301, #302, #303); the analysis looks for controls among other models\' runs itself.');
      expect(notes[2].text).toBe('Battery run left out in step 1 inside the windows: #13. It is not analyzed.');
      expect(notes[3].text).toBe('Battery run #12 is inside the comparison but not eligible and is not analyzed.');

      expect(ccPreviewNotes({
        battery: false, eventGroupsInSpan: [], segmentNotes: [], periodRuns: [], leftOutInPeriods: [103, 104], ineligible: []
      }).map(note => note.text)).toEqual(['Runs left out in step 1 inside the windows: #103, #104. They are not analyzed.']);
    });
  });
});
