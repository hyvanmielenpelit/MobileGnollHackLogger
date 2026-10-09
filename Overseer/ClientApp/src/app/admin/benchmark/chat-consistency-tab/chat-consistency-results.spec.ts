import {
  CC_ENDPOINT_STATUS_TEXT,
  ccEndpointNotes,
  ccEndpointStatus,
  ccEndpointStatusText,
  ccEstimateParts,
  ccIntervalGeometry,
  ccModelBaseName,
  ccNextRunActionCount,
  ccNextRunGroups,
  ccNotComputableGroups,
  ccOverallOutcome,
  ccResultKeyFigures,
  ccResultPeriodUnits
} from './chat-consistency-results';
import { CcEndpointResult, CcNextRun } from './chat-consistency.models';
import {
  ccAnalysisResult,
  ccBatteryMemberRows,
  ccBatteryRunRows,
  ccEndpoint,
  ccRunRows,
  ccSubjectWithLevel,
  ccUnitView
} from './chat-consistency-tab.testing';

const INCONCLUSIVE_REASON = 'Inconclusive: the data cannot tell a change from no change at the margin ±15 %.';
const TELEMETRY_REASON = 'Fewer than two runs with call telemetry in a period.';

/** An endpoint the server could not compute, as it sends one: the reason repeated as a grade reason. */
function notComputable(id: string, reason: string, overrides: Partial<CcEndpointResult> = {}): CcEndpointResult {
  return ccEndpoint(id, {
    computed: false, notComputedReason: reason, estimate: null, estimatePercent: null, ci95: null, ci90: null, ci95Percent: null,
    pValue: null, adjustedPValue: null, verdict: null, verdictLabel: 'not computable', grade: 'notEstablished',
    gradeReasons: [`Not computable: ${reason}`], minimumDetectableEffect: null, minimumDetectableEffectPercent: null,
    ...overrides
  });
}

/** A computed endpoint whose interval crosses its margin. */
function inconclusive(id: string, overrides: Partial<CcEndpointResult> = {}): CcEndpointResult {
  return ccEndpoint(id, {
    verdict: 'inconclusive', verdictLabel: 'inconclusive', grade: 'notEstablished', gradeReasons: [INCONCLUSIVE_REASON],
    estimatePercent: -7, ci95Percent: { lower: -19.7, upper: 6.8 }, ...overrides
  });
}

/** The analysis of the screenshot: P1–P3 not computable, P4 and P5 inconclusive. */
function screenshotEndpoints(): CcEndpointResult[] {
  return [
    notComputable('P1', 'No common grader covers every run.'),
    notComputable('P2', TELEMETRY_REASON),
    notComputable('P3', TELEMETRY_REASON),
    inconclusive('P4'),
    inconclusive('P5')
  ];
}

describe('chat consistency results', () => {
  describe('ccModelBaseName', () => {
    it('strips the thinking level the server appends in parentheses', () => {
      expect(ccModelBaseName('Claude 5.5 Haiku (xhigh)', 'xhigh')).toBe('Claude 5.5 Haiku');
      const subject = ccSubjectWithLevel();
      expect(ccModelBaseName(subject.displayName, subject.thinkingLevel)).toBe('GPT-5');
    });

    it('matches the level case-insensitively and as the badge writes it', () => {
      expect(ccModelBaseName('GPT-5 (High)', 'high')).toBe('GPT-5');
      expect(ccModelBaseName('Gemini (Default)', null)).toBe('Gemini');
    });

    it('leaves a name alone whose parenthesis is not the level', () => {
      expect(ccModelBaseName('Claude Opus (preview)', 'high')).toBe('Claude Opus (preview)');
      expect(ccModelBaseName('GPT-5 high', 'high')).toBe('GPT-5 high');
      expect(ccModelBaseName('GPT-5 (high) mini', 'high')).toBe('GPT-5 (high) mini');
    });
  });

  describe('ccOverallOutcome', () => {
    it('reads the screenshot analysis as not enough evidence', () => {
      expect(ccOverallOutcome(ccAnalysisResult({ endpoints: screenshotEndpoints() }))).toEqual({
        kind: 'undecided',
        title: 'Not enough evidence yet',
        detail: '2 of 5 endpoints computed, none decisive'
      });
    });

    it('counts the within-margin endpoints of an undecided analysis', () => {
      const endpoints = [ccEndpoint('P1'), inconclusive('P2'), inconclusive('P3'), ccEndpoint('P4'), ccEndpoint('P5')];
      expect(ccOverallOutcome(ccAnalysisResult({ endpoints })).detail)
        .toBe('5 of 5 endpoints computed: 3 within their margins, 2 inconclusive');
    });

    it('says no meaningful change only when every endpoint is computed', () => {
      const all = ccAnalysisResult({
        endpoints: ['P1', 'P2', 'P3', 'P4', 'P5'].map(id => ccEndpoint(id, id === 'P3' ? { verdict: 'changedNegligible' } : {}))
      });
      expect(ccOverallOutcome(all)).toEqual({
        kind: 'noChange', title: 'No meaningful change', detail: '5 of 5 endpoints within their margins'
      });
    });

    it('hedges a no-change outcome over endpoints that were not computed', () => {
      const result = ccAnalysisResult({
        endpoints: [notComputable('P1', 'No grades.'), notComputable('P2', TELEMETRY_REASON), ccEndpoint('P3'), ccEndpoint('P4'), ccEndpoint('P5')]
      });
      expect(ccOverallOutcome(result)).toEqual({
        kind: 'noChange',
        title: 'No change on the computed endpoints',
        detail: '3 of 5 endpoints within their margins · 2 not computable'
      });
    });

    it('lists the changed endpoints with their grade', () => {
      expect(ccOverallOutcome(ccAnalysisResult())).toEqual({
        kind: 'changed', title: 'The chat changed', detail: 'Time to first answer text: degraded (indicated)'
      });
    });

    it('reads a change in work per turn as more work, never as worse', () => {
      const result = ccAnalysisResult({
        endpoints: [ccEndpoint('P1'), ccEndpoint('P4', { verdict: 'changedDegraded', verdictLabel: 'more work', grade: 'established' })]
      });
      const outcome = ccOverallOutcome(result);
      expect(outcome.kind).toBe('changed');
      expect(outcome.detail).toBe('Work per turn: more work (established)');
    });

    it('joins several changes with a middle dot', () => {
      const result = ccAnalysisResult({
        endpoints: [
          ccEndpoint('P1', { verdict: 'changedImproved', verdictLabel: 'improved', grade: 'established' }),
          ccEndpoint('P2', { verdict: 'changedDegraded', verdictLabel: 'degraded', grade: 'indicated' })
        ]
      });
      expect(ccOverallOutcome(result).detail).toBe('Quality: improved (established) · Time to first answer text: degraded (indicated)');
    });

    it('says nothing could be computed when no endpoint is', () => {
      const result = ccAnalysisResult({ endpoints: ['P1', 'P2'].map(id => notComputable(id, 'No runs.')) });
      expect(ccOverallOutcome(result)).toEqual({
        kind: 'noneComputed', title: 'Nothing could be computed', detail: 'See why under Verdicts'
      });
    });
  });

  describe('ccEndpointStatus', () => {
    it('maps each verdict to a status', () => {
      expect(ccEndpointStatus(ccEndpoint('P2', { verdict: 'changedDegraded' }))).toBe('changed');
      expect(ccEndpointStatus(ccEndpoint('P2', { verdict: 'changedImproved' }))).toBe('improved');
      expect(ccEndpointStatus(ccEndpoint('P2', { verdict: 'equivalent' }))).toBe('within');
      expect(ccEndpointStatus(ccEndpoint('P2', { verdict: 'changedNegligible' }))).toBe('within');
      expect(ccEndpointStatus(inconclusive('P2'))).toBe('inconclusive');
      expect(ccEndpointStatus(ccEndpoint('P2', { verdict: null }))).toBe('inconclusive');
      expect(ccEndpointStatus(notComputable('P2', TELEMETRY_REASON))).toBe('notComputable');
    });

    it('maps both changes in work per turn to changed, worded by the server', () => {
      const more = ccEndpoint('P4', { verdict: 'changedDegraded', verdictLabel: 'more work' });
      const less = ccEndpoint('P4', { verdict: 'changedImproved', verdictLabel: 'less work' });
      expect(ccEndpointStatus(more)).toBe('changed');
      expect(ccEndpointStatus(less)).toBe('changed');
      expect(ccEndpointStatusText(more)).toBe('More work');
      expect(ccEndpointStatusText(less)).toBe('Less work');
    });

    it('words the other statuses from the status table', () => {
      expect(ccEndpointStatusText(ccEndpoint('P2', { verdict: 'changedDegraded' }))).toBe(CC_ENDPOINT_STATUS_TEXT.changed);
      expect(ccEndpointStatusText(ccEndpoint('P1'))).toBe('Within margin');
      expect(ccEndpointStatusText(notComputable('P1', 'No grades.'))).toBe('Not computable');
    });
  });

  describe('ccEndpointNotes', () => {
    it('says a not-computable reason once, though the server repeats it as a grade reason', () => {
      expect(ccEndpointNotes(notComputable('P1', 'No common grader covers every run.'))).toEqual({
        meaning: null, notes: ['No common grader covers every run.']
      });
    });

    it('lifts the verdict sentence into the meaning, without its prefix', () => {
      const notes = ccEndpointNotes(inconclusive('P5', { minimumDetectableEffectNote: 'one run per period: the effect is capped.' }));
      expect(notes.meaning).toBe('The data cannot tell a change from no change at the margin ±15 %.');
      expect(notes.notes).toEqual(['one run per period: the effect is capped.']);
    });

    it('keeps the notes in order and drops a repeated minimum-sample note', () => {
      const endpoint = ccEndpoint('P2', {
        legacyProxy: true,
        relaxedPooling: true,
        minimumSampleMet: false,
        minimumSampleDetail: '1 run on 1 day in the baseline',
        gradeReasons: ['Below the minimum sample: 1 run on 1 day in the baseline', 'Robustness check failed: leave one out.']
      });
      expect(ccEndpointNotes(endpoint)).toEqual({
        meaning: null,
        notes: [
          'Measured with the legacy proxy (model time per answer), not telemetry.',
          'Pooled across a measurement segment boundary.',
          'Below the minimum sample: 1 run on 1 day in the baseline',
          'Robustness check failed: leave one out.'
        ]
      });
    });

    it('notes the native grades of a computed quality endpoint', () => {
      expect(ccEndpointNotes(ccEndpoint('P1')).notes).toEqual(['Native grades; no common grader covers every run.']);
    });
  });

  describe('ccEstimateParts', () => {
    it('splits a log-ratio estimate from its percent interval', () => {
      expect(ccEstimateParts(inconclusive('P5'))).toEqual({ estimate: '−7.0 %', interval: '−19.7 to +6.8 %' });
    });

    it('writes a difference estimate and interval in its unit', () => {
      expect(ccEstimateParts(ccEndpoint('P1', { estimate: 1.24 }))).toEqual({
        estimate: '+1.2 index points', interval: '−1.0 to +2.0 index points'
      });
    });

    it('has no interval when the endpoint is not computed or has none', () => {
      expect(ccEstimateParts(notComputable('P1', 'No grades.'))).toEqual({ estimate: 'Not computed', interval: null });
      expect(ccEstimateParts(ccEndpoint('P1', { ci95: null })).interval).toBeNull();
    });
  });

  describe('ccIntervalGeometry', () => {
    it('places a log-ratio interval that crosses its margin', () => {
      const geometry = ccIntervalGeometry(inconclusive('P5'))!;
      const marginLower = (Math.exp(-0.14) - 1) * 100;
      const marginUpper = (Math.exp(0.14) - 1) * 100;
      const half = 19.7 * 1.15;
      const at = (value: number) => ((value + half) / (2 * half)) * 100;
      expect(geometry.axisHalfWidth).toBeCloseTo(half, 6);
      expect(geometry.zero).toBeCloseTo(50, 6);
      expect(geometry.lower).toBeCloseTo(at(-19.7), 6);
      expect(geometry.upper).toBeCloseTo(at(6.8), 6);
      expect(geometry.estimate).toBeCloseTo(at(-7), 6);
      expect(geometry.marginStart).toBeCloseTo(at(marginLower), 6);
      expect(geometry.marginEnd).toBeCloseTo(at(marginUpper), 6);
      expect(geometry.marginValue).toBeCloseTo(marginUpper, 6);
      expect(geometry.unitSuffix).toBe(' %');
      expect(geometry.crossesMargin).toBe(true);
      expect(geometry.withinMargin).toBe(false);
      expect(geometry.label).toBe(
        'Estimate −7.0 %, 95 % interval −19.7 % to +6.8 %, margin ±15 %. The interval reaches beyond the margin.');
    });

    it('places a difference interval inside its margin, the margin setting the axis', () => {
      const geometry = ccIntervalGeometry(ccEndpoint('P1'))!;
      expect(geometry.axisHalfWidth).toBeCloseTo(3 * 1.15, 6);
      expect(geometry.marginStart).toBeCloseTo(((-3 + 3.45) / 6.9) * 100, 6);
      expect(geometry.marginEnd).toBeCloseTo(((3 + 3.45) / 6.9) * 100, 6);
      expect(geometry.marginValue).toBe(3);
      expect(geometry.withinMargin).toBe(true);
      expect(geometry.crossesMargin).toBe(false);
      expect(geometry.label).toBe(
        'Estimate +0.5 index points, 95 % interval −1.0 to +2.0 index points, margin ±3 index points. '
        + 'The whole interval lies inside the margin.');
    });

    it('draws no bar without an estimate, an interval or a computed result', () => {
      expect(ccIntervalGeometry(notComputable('P2', TELEMETRY_REASON))).toBeNull();
      expect(ccIntervalGeometry(ccEndpoint('P2', { ci95Percent: null }))).toBeNull();
      expect(ccIntervalGeometry(ccEndpoint('P2', { estimatePercent: 'NaN' }))).toBeNull();
      expect(ccIntervalGeometry(ccEndpoint('P1', { estimate: null }))).toBeNull();
    });
  });

  describe('ccNotComputableGroups', () => {
    it('groups the endpoints that share a reason, in endpoint order', () => {
      expect(ccNotComputableGroups(screenshotEndpoints())).toEqual([
        { reason: 'No common grader covers every run.', endpoints: [{ id: 'P1', name: 'Quality' }] },
        {
          reason: TELEMETRY_REASON,
          endpoints: [{ id: 'P2', name: 'Time to first answer text' }, { id: 'P3', name: 'Answer streaming rate' }]
        }
      ]);
    });

    it('reads a reason given only as a grade reason, and one that differs only by its prefix', () => {
      const groups = ccNotComputableGroups([
        notComputable('P2', TELEMETRY_REASON),
        notComputable('P3', TELEMETRY_REASON, { notComputedReason: null, gradeReasons: [`Not computable: ${TELEMETRY_REASON}`] })
      ]);
      expect(groups.length).toBe(1);
      expect(groups[0].endpoints.map(endpoint => endpoint.id)).toEqual(['P2', 'P3']);
    });

    it('is empty when every endpoint is computed', () => {
      expect(ccNotComputableGroups(ccAnalysisResult().endpoints)).toEqual([]);
    });
  });

  describe('ccNextRunGroups', () => {
    const control = (endpointId: string, repeatRunId: number): CcNextRun => ({
      kind: 'control', period: 'baseline', endpointId, reason: 'No control run in the baseline.',
      suggestion: 'Run Claude Opus on Board Suite.', repeatRunId
    });

    it('groups the per-suite control advice into one card with each run as a target', () => {
      const result = ccAnalysisResult({
        nextRuns: [
          { kind: 'regrade', period: 'baseline', endpointId: 'P1', reason: 'Native grades only.', suggestion: 'Re-grade.', repeatRunId: null },
          control('P1', 96),
          control('P2', 97),
          { kind: 'control', period: 'comparison', endpointId: null, reason: 'No control run in the comparison.',
            suggestion: 'Run Claude Opus on Board Suite.', repeatRunId: 106 },
          { kind: 'checkpoint', period: 'comparison', endpointId: 'P4', reason: 'One run per period.',
            suggestion: 'Repeat the run.', repeatRunId: 105 },
          { kind: 'checkpoint', period: 'comparison', endpointId: 'P4', reason: 'One run per period.',
            suggestion: 'Repeat the run.', repeatRunId: 105 }
        ]
      });
      const groups = ccNextRunGroups(result, ccRunRows());
      expect(groups.map(group => group.key)).toEqual(['checkpoint:comparison', 'control:baseline', 'control:comparison', 'regrade:baseline']);
      expect(groups[1]).toEqual({
        key: 'control:baseline',
        kind: 'control',
        period: 'baseline',
        title: 'A control run',
        endpointIds: ['P1', 'P2'],
        reasons: ['No control run in the baseline.'],
        suggestions: ['Run Claude Opus on Board Suite.'],
        targets: [{ runId: 96, suiteName: null }, { runId: 97, suiteName: null }]
      });
      expect(groups[0].targets).toEqual([{ runId: 105, suiteName: 'Board Suite' }]);
      expect(groups[2].endpointIds).toEqual([]);
      expect(groups[3].targets).toEqual([]);
      expect(ccNextRunActionCount(groups)).toBe(5);
    });

    it('is empty without next runs', () => {
      const groups = ccNextRunGroups(ccAnalysisResult({ nextRuns: [] }), ccRunRows());
      expect(groups).toEqual([]);
      expect(ccNextRunActionCount(groups)).toBe(0);
    });
  });

  describe('ccResultKeyFigures', () => {
    it('gives the endpoints decided, the periods, the paired items and the next runs', () => {
      expect(ccResultKeyFigures(ccAnalysisResult())).toEqual([
        { key: 'decided', label: 'Endpoints decided', value: '5 of 5', note: '5 computed' },
        { key: 'baseline', label: 'Baseline', value: '3 runs', sub: '3 days', note: '2026-09-01 – 2026-09-14' },
        { key: 'comparison', label: 'Comparison', value: '3 runs', sub: '3 days', note: '2026-09-15 – 2026-10-01' },
        { key: 'pairedItems', label: 'Paired items', value: '60', note: 'Items answered in both periods' },
        { key: 'nextRuns', label: 'Next runs', value: '2', note: '1 control · 1 re-grade' }
      ]);
    });

    it('counts none decided in the screenshot analysis, and omits paired items when no paired endpoint is computed', () => {
      const figures = ccResultKeyFigures(ccAnalysisResult({ endpoints: screenshotEndpoints() }));
      expect(figures[0]).toEqual({ key: 'decided', label: 'Endpoints decided', value: '0 of 5', note: '2 computed · 3 not computable' });
      expect(figures.find(figure => figure.key === 'pairedItems')?.value).toBe('60');

      const none = ccResultKeyFigures(ccAnalysisResult({
        endpoints: [notComputable('P1', 'No grades.'), ccEndpoint('P2'), notComputable('P4', 'No runs.'), notComputable('P5', 'No runs.')]
      }));
      expect(none.some(figure => figure.key === 'pairedItems')).toBe(false);
    });

    it('counts battery runs in a battery analysis', () => {
      const result = ccAnalysisResult({
        unitKind: 'batteryRun',
        units: [
          ccUnitView(11, 'baseline', { kind: 'batteryRun', memberRunIds: [301, 302] }),
          ccUnitView(12, 'comparison', { kind: 'batteryRun', memberRunIds: [303, 304] })
        ]
      });
      const figures = ccResultKeyFigures(result);
      expect(figures[1]).toEqual({ key: 'baseline', label: 'Baseline', value: '1 battery run', sub: '3 runs · 3 days', note: '2026-09-01 – 2026-09-14' });
    });

    it('says no verdict waits on more data without next runs', () => {
      const figures = ccResultKeyFigures(ccAnalysisResult({ nextRuns: [] }));
      expect(figures[figures.length - 1]).toEqual({ key: 'nextRuns', label: 'Next runs', value: 'None', note: 'No verdict waits on more data' });
    });
  });

  describe('ccResultPeriodUnits', () => {
    it('resolves the stored run units with their stored periods and counts a missing one', () => {
      const result = ccAnalysisResult({
        units: [
          ccUnitView(102, 'baseline', { startedAtUtc: '2026-09-05T08:00:00Z' }),
          ccUnitView(101, 'baseline', { startedAtUtc: '2026-09-01T08:00:00Z' }),
          ccUnitView(105, 'comparison', { startedAtUtc: '2026-09-26T08:00:00Z' }),
          ccUnitView(999, 'comparison', { startedAtUtc: '2026-09-30T08:00:00Z' })
        ]
      });
      const view = ccResultPeriodUnits(result, ccRunRows(), []);
      expect(view.units.map(unit => unit.id)).toEqual([101, 102, 105]);
      expect(view.units.every(unit => unit.eligible && unit.battery === null)).toBe(true);
      expect(view.units[0].runs.map(row => row.runId)).toEqual([101]);
      expect(view.assignment.get(101)).toBe('baseline');
      expect(view.assignment.get(105)).toBe('comparison');
      expect(view.ids).toEqual({ baselineFirstId: 101, baselineLastId: 102, comparisonFirstId: 105, comparisonLastId: 105 });
      expect(view.missing).toBe(1);
      expect(view.missingIds).toEqual([999]);
    });

    it('resolves battery units against the battery rows and their members against the run rows', () => {
      const result = ccAnalysisResult({
        unitKind: 'batteryRun',
        units: [
          ccUnitView(12, 'comparison', { kind: 'batteryRun', startedAtUtc: '2026-10-08T10:00:00Z', memberRunIds: [303, 304] }),
          ccUnitView(11, 'baseline', { kind: 'batteryRun', startedAtUtc: '2026-10-08T06:00:00Z', memberRunIds: [301, 302] })
        ]
      });
      const view = ccResultPeriodUnits(result, ccBatteryMemberRows(), ccBatteryRunRows());
      expect(view.units.map(unit => unit.id)).toEqual([11, 12]);
      expect(view.units[0].battery?.batteryRunId).toBe(11);
      expect(view.units[1].runs.map(row => row.runId)).toEqual([303, 304]);
      expect(view.units[1].runs[1].matchedControlRunIds).toEqual([404]);
      expect(view.ids).toEqual({ baselineFirstId: 11, baselineLastId: 11, comparisonFirstId: 12, comparisonLastId: 12 });
      expect(view.missing).toBe(0);
    });

    it('counts a battery unit missing from the battery rows', () => {
      const result = ccAnalysisResult({
        unitKind: 'batteryRun',
        units: [ccUnitView(40, 'baseline', { kind: 'batteryRun', memberRunIds: [4001, 4002] })]
      });
      const view = ccResultPeriodUnits(result, ccBatteryMemberRows(), ccBatteryRunRows());
      expect(view.units).toEqual([]);
      expect(view.missing).toBe(1);
      expect(view.ids).toEqual({ baselineFirstId: null, baselineLastId: null, comparisonFirstId: null, comparisonLastId: null });
    });

    it('reads the periods\' run ids as run units in an analysis without stored units', () => {
      const view = ccResultPeriodUnits(ccAnalysisResult(), ccRunRows(), []);
      expect(view.units.map(unit => unit.id)).toEqual([101, 102, 103, 104, 105, 106]);
      expect(view.units[0].startedAtUtc).toBe('2026-09-01T08:00:00Z');
      expect(view.ids).toEqual({ baselineFirstId: 101, baselineLastId: 103, comparisonFirstId: 104, comparisonLastId: 106 });
      expect(view.missing).toBe(0);
    });
  });
});
