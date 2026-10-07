import {
  CC_PRINT_THEME,
  CC_REPORT_FIGURES,
  CcChartPoint,
  analysisBands,
  buildCcFigures,
  buildMarkers,
  costFigure,
  dominantCommonGrader,
  dominantServedModel,
  qualityFigure,
  reliabilityFigure,
  sortedPoints,
  streamingRateFigure,
  timeToFirstAnswerFigure,
  timelineOverviewFigure,
  workFigure
} from './chat-consistency-charts';
import { CC_REPORT_FIGURE_KEYS } from './chat-consistency.models';
import { ccAnnotation, ccEvent, ccPoint, ccTimeline } from './chat-consistency-tab.testing';

const at = (iso: string) => Date.parse(iso);

describe('chat-consistency-charts', () => {
  const timeline = ccTimeline();
  const input = { points: timeline.points, events: timeline.events, annotations: timeline.annotations };

  it('orders points by start and leaves out unparsable starts', () => {
    const points = sortedPoints([ccPoint(2, '2026-09-05T00:00:00Z'), ccPoint(1, '2026-09-01T00:00:00Z'), ccPoint(3, 'garbage')]);
    expect(points.map(p => p.runId)).toEqual([1, 2]);
  });

  describe('quality', () => {
    it('captions a narrow range as held and labels the native grades', () => {
      const figure = qualityFigure(input);
      expect(figure.takeaway).toBe('Quality held between 71 and 74 across 6 runs.');
      expect(figure.config!.data.datasets.map(ds => ds.label)).toEqual(['Quality Index (native grades)']);
      expect(figure.table.columns).toEqual(['Run', 'Started', 'Quality Index (native)']);
      expect(figure.table.rows[0]).toEqual(['#101', '2026-09-01 08:00 UTC', '71']);
    });

    it('captions a wide range with the latest score', () => {
      const figure = qualityFigure({ points: [ccPoint(1, '2026-09-01T00:00:00Z', { qualityIndex: 60 }), ccPoint(2, '2026-09-02T00:00:00Z', { qualityIndex: 70 })] });
      expect(figure.takeaway).toBe('Quality ranged from 60 to 70 across 2 runs; the latest run scored 70.');
    });

    it('adds the common grader that covers most runs as its own labeled series', () => {
      const grader = (snapshotId: number, display: string, meanQuality: number, calibratedAtUtc = '2026-09-10T00:00:00Z') =>
        ({ snapshotId, display, calibrationId: snapshotId * 10, calibratedAtUtc, meanQuality, itemCount: 20 });
      const points = [
        ccPoint(1, '2026-09-01T00:00:00Z', { commonGraderQuality: [grader(5, 'Opus', 70), grader(6, 'Flash', 50)] }),
        ccPoint(2, '2026-09-02T00:00:00Z', { commonGraderQuality: [grader(5, 'Opus', 68), grader(5, 'Opus', 71, '2026-09-20T00:00:00Z')] })
      ];
      expect(dominantCommonGrader(points)).toEqual({ snapshotId: 5, display: 'Opus' });
      const figure = qualityFigure({ points });
      const common = figure.config!.data.datasets[1];
      expect(common.label).toBe('Mean quality (common grader: Opus)');
      // The latest calibration of the snapshot on a run wins.
      expect((common.data as CcChartPoint[]).map(p => p.y)).toEqual([70, 71]);
      expect(figure.table.columns[3]).toBe('Common grader (Opus)');
    });

    it('says there is nothing to chart without values', () => {
      const figure = qualityFigure({ points: [ccPoint(1, '2026-09-01T00:00:00Z', { qualityIndex: null })] });
      expect(figure.config).toBeNull();
      expect(figure.takeaway).toBe('No run in this range has a quality figure.');
    });
  });

  describe('speed', () => {
    it('draws the legacy proxy hollow and dashed, and says so', () => {
      const figure = timeToFirstAnswerFigure(input);
      const [telemetry, proxy] = figure.config!.data.datasets;
      expect(telemetry.label).toBe('Time to first answer text (telemetry)');
      expect(proxy.label).toBe('Model time per answer (legacy proxy)');
      expect(proxy.borderDash).toEqual([4, 4]);
      expect(proxy.pointBackgroundColor).toBe('rgba(0, 0, 0, 0)');
      // Only run 103 has no telemetry time, so only it carries a proxy value.
      expect((proxy.data as CcChartPoint[]).filter(p => p.y !== null).map(p => p.runId)).toEqual([103]);
      expect(figure.takeaway).toBe(
        'Median time to first answer text ranged from 2.4 s to 2.4 s across 5 telemetry runs. 1 legacy run is drawn hollow as the legacy proxy.');
    });

    it('paints the print theme background under a hollow point', () => {
      const figure = timeToFirstAnswerFigure(input, { theme: CC_PRINT_THEME });
      expect(figure.config!.data.datasets[1].pointBackgroundColor).toBe('#ffffff');
    });

    it('counts the runs without a streaming rate', () => {
      expect(streamingRateFigure(input).takeaway)
        .toBe('The answer streaming rate ranged from 40.0 tok/s to 40.0 tok/s across 5 runs. 1 legacy run recorded no rate.');
    });
  });

  it('puts tool calls on a second axis in the work figure', () => {
    const figure = workFigure(input);
    expect(figure.config!.data.datasets[1].yAxisID).toBe('y1');
    expect(figure.config!.options.scales!['y1']).toBeDefined();
    expect(figure.takeaway).toBe('Across 6 runs, output tokens per answer ranged from 1,200 to 1,200; tool calls per answer from 3.5 to 3.5.');
  });

  it('captions cost at one price card', () => {
    expect(costFigure(input).takeaway).toBe('Cost per question ranged from $0.015 to $0.015 across 6 runs, at one price card.');
  });

  it('names the highest reliability rate and its run', () => {
    const clean = reliabilityFigure(input);
    expect(clean.takeaway).toBe('No terminal failures, timeouts, empty answers, refusals or exhausted tool budgets in 6 runs.');
    const points = [ccPoint(1, '2026-09-01T00:00:00Z'), ccPoint(2, '2026-09-02T00:00:00Z', { timeoutRate: 0.04 })];
    expect(reliabilityFigure({ points }).takeaway).toBe('The highest rate was timeouts at 4.0 % in run #2, across 2 runs.');
    expect(reliabilityFigure({ points }).table.rows[1][3]).toBe('4.0 %');
  });

  describe('markers', () => {
    it('numbers events, annotations and served-model changes separately, in time order', () => {
      const points = [
        ccPoint(1, '2026-09-01T00:00:00Z', { servedModelIds: [{ modelId: 'a', callCount: 10 }] }),
        ccPoint(2, '2026-09-02T00:00:00Z', { servedModelIds: [{ modelId: 'a', callCount: 3 }, { modelId: 'b', callCount: 9 }] }),
        ccPoint(3, '2026-09-03T00:00:00Z', { servedModelIds: [] })
      ];
      expect(dominantServedModel(points[1])).toBe('b');
      const markers = buildMarkers(points,
        [ccEvent({ atUtc: '2026-09-02T12:00:00Z', label: 'later' }), ccEvent({ atUtc: '2026-09-01T12:00:00Z', label: 'earlier' })],
        [ccAnnotation(1, { atUtc: '2026-09-02T00:00:00Z', kind: 'priceChange', text: 'Price cut' })]);
      expect(markers.map(m => `${m.tag} ${m.label}`)).toEqual([
        'E1 Overseer change: earlier',
        'E2 Overseer change: later',
        'A1 Price change: Price cut',
        'S1 Served model changed from a to b (run #2)'
      ]);
      expect(markers[3].x).toBe(at('2026-09-02T00:00:00Z'));
    });

    it('attaches the overlay plugin and turns animation off for reduced motion', () => {
      const figure = qualityFigure(input, { reducedMotion: true });
      expect(figure.config!.plugins.map(p => p.id)).toEqual(['ccOverlay']);
      expect(figure.config!.options.animation).toBe(false);
      expect(qualityFigure(input).config!.options.animation).toEqual({ duration: 250 });
    });
  });

  it('formats the time axis as UTC dates', () => {
    const x = qualityFigure(input).config!.options.scales!['x'] as unknown as { ticks: { callback: (v: number) => string } };
    expect(x.ticks.callback.call(null, at('2026-09-05T08:00:00Z'))).toBe('2026-09-05');
  });

  it('builds the overview with every marker kind counted', () => {
    const figure = timelineOverviewFigure(input);
    expect(figure.takeaway).toBe('6 runs, 1 Overseer change, 1 annotation and 0 served-model changes in this range.');
    expect(figure.config!.data.datasets.every(ds => (ds as { showLine?: boolean }).showLine === false)).toBe(true);
  });

  it('builds the timeline figures in order and maps every report key to one', () => {
    expect(buildCcFigures(input).map(f => f.key)).toEqual(['quality', 'ttfat', 'rate', 'work', 'cost', 'reliability', 'timeline']);
    expect(CC_REPORT_FIGURE_KEYS.map(key => CC_REPORT_FIGURES[key])).toEqual(['quality', 'ttfat', 'work', 'timeline']);
  });

  it('turns analysis periods into bands', () => {
    const bands = analysisBands(
      { startUtc: '2026-09-01T00:00:00Z', endUtc: '2026-09-14T23:59:59.999Z' },
      { startUtc: '2026-09-15T00:00:00Z', endUtc: 'bad' });
    expect(bands).toEqual([{ name: 'Baseline', start: at('2026-09-01T00:00:00Z'), end: at('2026-09-14T23:59:59.999Z') }]);
  });
});
