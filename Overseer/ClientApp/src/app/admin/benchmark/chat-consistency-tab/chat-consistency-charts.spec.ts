import { Chart } from 'chart.js';
import type { ChartConfiguration } from 'chart.js';

import { APP_CHART_REGISTRABLES } from '../../../chart-registrables';
import {
  CC_FIGURE_KEYS,
  CC_FIGURE_SERIES,
  CC_PRINT_THEME,
  CC_REPORT_FIGURES,
  CC_SCREEN_THEME,
  CC_TAG_GAP,
  CC_TAG_MAX_ROWS,
  CC_TAG_ROW_HEIGHT,
  CcChartDataset,
  CcChartPoint,
  CcFigure,
  CcFigureInput,
  analysisBands,
  buildCcFigures,
  buildMarkers,
  ccTagBandHeight,
  ccTagRows,
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
import { CcMarkerFilter, CcMarkerKind, eventGroupLabel, groupOverseerEvents } from './chat-consistency-events';
import { CC_REPORT_FIGURE_KEYS } from './chat-consistency.models';
import {
  ccAnnotation,
  ccEvent,
  ccEventAnnotations,
  ccEventPoints,
  ccOverseerEvents,
  ccPoint,
  ccTimeline
} from './chat-consistency-tab.testing';

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
    it('numbers composite events, annotations and served-model changes separately, in time order', () => {
      const points = [
        ccPoint(1, '2026-09-01T00:00:00Z', { servedModelIds: [{ modelId: 'a', callCount: 10 }] }),
        ccPoint(2, '2026-09-02T00:00:00Z', { servedModelIds: [{ modelId: 'a', callCount: 3 }, { modelId: 'b', callCount: 9 }] }),
        ccPoint(3, '2026-09-03T00:00:00Z', { servedModelIds: [] })
      ];
      expect(dominantServedModel(points[1])).toBe('b');
      const markers = buildMarkers(points,
        [
          ccEvent({ atUtc: '2026-09-02T12:00:00Z', runId: 2, kind: 'WikiHeadSha' }),
          ccEvent({ atUtc: '2026-09-01T12:00:00Z', runId: 1, kind: 'ToolGuidesSha256' })
        ],
        [ccAnnotation(1, { atUtc: '2026-09-02T00:00:00Z', kind: 'priceChange', text: 'Price cut' })]);
      expect(markers.map(m => `${m.kind} ${m.tag} ${m.label}`)).toEqual([
        'event E1 Changes under harness 30 on 2026-09-01: Tool guides',
        'event E2 Changes under harness 30 on 2026-09-02: Wiki',
        'annotation A1 Price change: Price cut',
        'served S1 Served model changed from a to b (run #2)'
      ]);
      expect(markers[0].x).toBe(at('2026-09-01T12:00:00Z'));
      expect(markers[3].x).toBe(at('2026-09-02T00:00:00Z'));
    });

    it('draws one event marker per composite, at its earliest event, labeled with the group label', () => {
      const points = ccEventPoints();
      const groups = groupOverseerEvents(ccOverseerEvents(), points);
      const markers = buildMarkers(points, ccOverseerEvents(), ccEventAnnotations());
      const events = markers.filter(m => m.kind === 'event');
      expect(events.length).toBe(4);
      expect(events.map(m => m.tag)).toEqual(['E1', 'E2', 'E3', 'E4']);
      expect(events.map(m => m.label)).toEqual(groups.map(eventGroupLabel));
      expect(events.map(m => m.x)).toEqual(groups.map(g => at(g.atUtc)));
      expect(events[0].label).toBe('Changes under harness 27 on 2026-09-03: System prompt, Knowledge base (2 runs)');
      expect(markers.map(m => m.tag)).toEqual(['E1', 'E2', 'E3', 'E4', 'A1', 'A2', 'S1', 'S2']);
    });

    it('drops filtered markers without renumbering the rest', () => {
      const points = ccEventPoints();
      const tags = (filter: CcMarkerFilter) => buildMarkers(points, ccOverseerEvents(), ccEventAnnotations(), filter).map(m => m.tag);
      expect(tags({ kinds: new Set<CcMarkerKind>(['event', 'served']), hiddenEventKinds: new Set(['ToolGuidesSha256', 'HarnessVersion']) }))
        .toEqual(['E1', 'E3', 'E4', 'S1', 'S2']);
      expect(tags({ kinds: new Set<CcMarkerKind>(['annotation']), hiddenEventKinds: new Set() })).toEqual(['A1', 'A2']);
      expect(tags({ kinds: new Set<CcMarkerKind>(), hiddenEventKinds: new Set() })).toEqual([]);
    });

    it('applies the figure input’s marker filter, while the overview still counts every marker', () => {
      const eventInput: CcFigureInput = { points: ccEventPoints(), events: ccOverseerEvents(), annotations: ccEventAnnotations() };
      const filtered: CcFigureInput = {
        ...eventInput, markerFilter: { kinds: new Set<CcMarkerKind>(['annotation']), hiddenEventKinds: new Set() }
      };
      expect(qualityFigure(filtered).markers.map(m => m.tag)).toEqual(['A1', 'A2']);
      const overview = timelineOverviewFigure(filtered);
      expect(overview.markers.map(m => m.tag)).toEqual(['A1', 'A2']);
      expect(overview.takeaway).toBe('6 runs, 4 Overseer changes, 2 annotations and 2 served-model changes in this range.');
      expect(overview.takeaway).toBe(timelineOverviewFigure(eventInput).takeaway);
    });

    describe('over a subset of the timeline', () => {
      // The analyzed runs are 204–206; the events are the composites of 2026-09-03 (runs 202 and 203)
      // and 2026-09-10, which the full timeline tags E1 and E4.
      const all = ccEventPoints();
      const drawn = all.filter(point => point.runId >= 204);
      const events = ccOverseerEvents().filter(e => e.atUtc.startsWith('2026-09-03') || e.atUtc.startsWith('2026-09-10'));
      const numbering = groupOverseerEvents(ccOverseerEvents(), all);
      const eventLines = (markers: { kind: string; tag: string; label: string }[]) =>
        markers.filter(m => m.kind === 'event').map(m => `${m.tag} ${m.label}`);

      it('groups by the harness points and reuses the numbering, while the served-model markers stay over the drawn points', () => {
        const markers = buildMarkers(drawn, events, [], undefined, { harnessPoints: all, numbering });
        expect(eventLines(markers)).toEqual([
          'E1 Changes under harness 27 on 2026-09-03: System prompt, Knowledge base (2 runs)',
          'E4 Changes under harness 29 on 2026-09-10: Source code, Corpus index (2 runs)'
        ]);
        // Over runs 204–206 only, the served model changes at run 205; run 204 has no earlier run drawn.
        expect(markers.filter(m => m.kind === 'served').map(m => `${m.tag} ${m.label}`))
          .toEqual(['S1 Served model changed from gpt-5-2026-09 to gpt-5-2026-08 (run #205)']);

        // Without the context the 2026-09-03 runs have no harness, and the numbering restarts.
        expect(eventLines(buildMarkers(drawn, events))).toEqual([
          'E1 Overseer changes on 2026-09-03: System prompt, Knowledge base (2 runs)',
          'E2 Changes under harness 29 on 2026-09-10: Source code, Corpus index (2 runs)'
        ]);
      });

      it('draws a figure input\'s harness points and event numbering the same way', () => {
        const figureInput: CcFigureInput = { points: drawn, events, harnessPoints: all, eventNumbering: numbering };
        expect(qualityFigure(figureInput).markers.filter(m => m.kind === 'event').map(m => m.tag)).toEqual(['E1', 'E4']);
        const filtered = timelineOverviewFigure({
          ...figureInput, markerFilter: { kinds: new Set<CcMarkerKind>(['annotation']), hiddenEventKinds: new Set() }
        });
        expect(filtered.markers).toEqual([]);
        expect(filtered.takeaway).toBe('3 runs, 2 Overseer changes, 0 annotations and 1 served-model change in this range.');
        expect(qualityFigure(figureInput).table.rows.map(row => row[0])).toEqual(['#204', '#205', '#206']);
      });
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

  describe('figure keys and series ids', () => {
    const grader = { snapshotId: 5, display: 'Opus', calibrationId: 50, calibratedAtUtc: '2026-09-10T00:00:00Z', meanQuality: 70, itemCount: 20 };
    // Every series has a value: a common grader, an estimated streaming rate and a legacy run.
    const full: CcFigureInput = {
      points: [
        ccPoint(1, '2026-09-01T00:00:00Z', { commonGraderQuality: [grader] }),
        ccPoint(2, '2026-09-02T00:00:00Z', { streamingRateEstimated: true }),
        ccPoint(3, '2026-09-03T00:00:00Z', {
          isLegacy: true, medianTimeToFirstAnswerTextMs: null, medianStreamingRate: null, latencyLabel: 'legacy proxy'
        })
      ]
    };

    it('lists the figure keys and titles in buildCcFigures order', () => {
      const figures = buildCcFigures(input);
      expect(CC_FIGURE_KEYS.map(entry => entry.key)).toEqual(figures.map(f => f.key));
      expect(CC_FIGURE_KEYS.map(entry => entry.title)).toEqual(figures.map(f => f.title));
    });

    it('gives every drawn dataset the series id CC_FIGURE_SERIES lists for its figure', () => {
      for (const figure of buildCcFigures(full)) {
        expect(figure.config!.data.datasets.map(ds => ds.seriesId), figure.key)
          .toEqual(CC_FIGURE_SERIES[figure.key].map(series => series.id));
      }
      expect(Object.keys(CC_FIGURE_SERIES)).toEqual(CC_FIGURE_KEYS.map(entry => entry.key));
    });
  });

  describe('hidden series', () => {
    it('leaves a hidden series out of the drawing but not out of the table or the takeaway', () => {
      const shown = reliabilityFigure(input);
      const hidden = reliabilityFigure(input, { hiddenSeries: new Set(['reliability.timeoutRate', 'reliability.refusalRate']) });
      expect(hidden.config!.data.datasets.map(ds => ds.seriesId))
        .toEqual(['reliability.terminalFailureRate', 'reliability.emptyAnswerRate', 'reliability.toolBudgetExhaustedRate']);
      expect(hidden.table).toEqual(shown.table);
      expect(hidden.takeaway).toBe(shown.takeaway);
    });

    it('removes the tool-call axis with the tool-call series', () => {
      const shown = workFigure(input);
      const hidden = workFigure(input, { hiddenSeries: new Set(['work.tools']) });
      expect(hidden.config!.data.datasets.map(ds => ds.seriesId)).toEqual(['work.tokens']);
      expect(hidden.config!.options.scales!['y1']).toBeUndefined();
      expect(hidden.table).toEqual(shown.table);
      expect(hidden.table.columns).toContain('Tool calls per answer');
      expect(hidden.takeaway).toBe(shown.takeaway);

      const tokensHidden = workFigure(input, { hiddenSeries: new Set(['work.tokens']) });
      expect(tokensHidden.config!.data.datasets.map(ds => ds.seriesId)).toEqual(['work.tools']);
      expect(tokensHidden.config!.options.scales!['y1']).toBeDefined();
    });

    it('draws nothing when every series of a figure is hidden, and still describes the data', () => {
      const every = new Set(Object.values(CC_FIGURE_SERIES).flat().map(series => series.id));
      const shown = buildCcFigures(input);
      const hidden = buildCcFigures(input, { hiddenSeries: every });
      expect(hidden.map(f => f.config)).toEqual(shown.map(() => null));
      expect(hidden.map(f => f.takeaway)).toEqual(shown.map(f => f.takeaway));
      expect(hidden.map(f => f.table)).toEqual(shown.map(f => f.table));
      expect(qualityFigure(input, { hiddenSeries: new Set(['quality.native']) }).config).toBeNull();
    });
  });

  describe('zero baseline', () => {
    type ValueAxis = { beginAtZero?: boolean; min?: number; max?: number } | undefined;
    const axis = (figure: CcFigure, id: 'y' | 'y1'): ValueAxis => figure.config!.options.scales![id] as unknown as ValueAxis;
    const bounds = (figure: CcFigure) => (['y', 'y1'] as const).map(id => {
      const scale = axis(figure, id);
      return scale ? { beginAtZero: scale.beginAtZero, min: scale.min, max: scale.max } : null;
    });

    it('starts the quality axis at zero only when asked', () => {
      expect(axis(qualityFigure(input), 'y')!.beginAtZero).toBe(false);
      expect(axis(qualityFigure(input, { zeroBaseline: false }), 'y')!.beginAtZero).toBe(false);
      expect(axis(qualityFigure(input, { zeroBaseline: true }), 'y')!.beginAtZero).toBe(true);
    });

    it('leaves the other figures’ value axes unchanged', () => {
      const plain = buildCcFigures(input).filter(f => f.key !== 'quality');
      const zero = buildCcFigures(input, { zeroBaseline: true }).filter(f => f.key !== 'quality');
      expect(zero.map(bounds)).toEqual(plain.map(bounds));
      // Every value axis but the overview's fixed one already starts at zero.
      for (const figure of plain.filter(f => f.key !== 'timeline')) {
        expect(axis(figure, 'y')!.beginAtZero, figure.key).toBe(true);
      }
      expect(axis(workFigure(input), 'y1')!.beginAtZero).toBe(true);
    });
  });

  describe('staggered tag rows', () => {
    it('lays no tags into no rows and no band', () => {
      expect(ccTagRows([])).toEqual({ rows: 0, row: [] });
      expect(ccTagBandHeight(0)).toBe(0);
    });

    it('keeps tags in one row while each clears the previous one by the gap', () => {
      expect(CC_TAG_GAP).toBe(4);
      // The second tag begins exactly 4 px after the first ends.
      expect(ccTagRows([{ center: 10, width: 20 }, { center: 34, width: 20 }])).toEqual({ rows: 1, row: [0, 0] });
      // 3 px is too close.
      expect(ccTagRows([{ center: 10, width: 20 }, { center: 33, width: 20 }])).toEqual({ rows: 2, row: [0, 1] });
    });

    it('puts each tag into the first row where it fits, in order of position, and reports the row per input tag', () => {
      expect(ccTagRows([{ center: 10, width: 20 }, { center: 25, width: 20 }, { center: 40, width: 20 }]))
        .toEqual({ rows: 2, row: [0, 1, 0] });
      // Given right to left, the rows still follow the positions.
      expect(ccTagRows([{ center: 25, width: 20 }, { center: 10, width: 20 }])).toEqual({ rows: 2, row: [1, 0] });
    });

    it('uses at most three rows, then the row whose last tag ends earliest', () => {
      expect(CC_TAG_MAX_ROWS).toBe(3);
      // Rows end at 30, 16 and 18 when the fourth tag (12–20) arrives: row 1 ends earliest.
      expect(ccTagRows([
        { center: 10, width: 40 },
        { center: 12, width: 8 },
        { center: 14, width: 8 },
        { center: 16, width: 8 }
      ])).toEqual({ rows: 3, row: [0, 1, 2, 1] });
      // On a tie the lowest row wins.
      const same = { center: 10, width: 20 };
      expect(ccTagRows([same, same, same, same])).toEqual({ rows: 3, row: [0, 1, 2, 0] });
    });

    it('honors a lower row limit', () => {
      const same = { center: 10, width: 20 };
      expect(ccTagRows([same, same, same], 2)).toEqual({ rows: 2, row: [0, 1, 0] });
      expect(ccTagRows([same, same], 0)).toEqual({ rows: 1, row: [0, 0] });
    });

    it('reserves 16 px per row and a 4 px gap above them', () => {
      expect(CC_TAG_ROW_HEIGHT).toBe(16);
      expect(ccTagBandHeight(1)).toBe(20);
      expect(ccTagBandHeight(2)).toBe(36);
      expect(ccTagBandHeight(3)).toBe(52);
    });

    it('reserves the tag rows in their own layout box, leaving the chart padding alone', () => {
      expect(qualityFigure(input).config!.options.layout!.padding).toEqual({ top: 4, right: 8 });
    });
  });

  describe('runs not in the analysis', () => {
    // Runs 101–106 in time order: run 101 lies before the first run and run 104 is left out.
    const notAnalyzed = new Map<number, string>([[101, 'before the first run'], [104, 'left out in step 1']]);
    const marked: CcFigureInput = { ...input, notAnalyzed };
    const muted = CC_SCREEN_THEME.muted;

    type PointOption = (ctx: { raw: unknown; dataIndex: number }) => unknown;
    type SegmentOption = (ctx: { type: 'segment'; p0DataIndex: number; p1DataIndex: number; datasetIndex: number }) => unknown;
    const POINT_OPTIONS = ['pointStyle', 'pointBackgroundColor', 'pointBorderColor', 'pointRadius', 'pointBorderWidth'];
    /** The scriptable point options of point `i`, resolved as Chart.js would, in `POINT_OPTIONS` order. */
    const pointLook = (ds: CcChartDataset, i: number) => POINT_OPTIONS.map(name =>
      ((ds as unknown as Record<string, PointOption>)[name])({ raw: ds.data[i], dataIndex: i }));
    /** A scriptable segment option of the segment from point `i` to point `i + 1`. */
    const segmentOption = (ds: CcChartDataset, name: 'borderColor' | 'borderDash', i: number) =>
      ((ds.segment as unknown as Record<string, SegmentOption>)[name])({ type: 'segment', p0DataIndex: i, p1DataIndex: i + 1, datasetIndex: 0 });

    it('draws each run not in the analysis as an unfilled gray cross and every other run as before', () => {
      const ds = qualityFigure(marked).config!.data.datasets[0];
      const cross = ['crossRot', 'rgba(0, 0, 0, 0)', muted, 4.5, 2];
      const plain = ['circle', CC_SCREEN_THEME.series[0], CC_SCREEN_THEME.series[0], 3.5, 1];
      expect([0, 1, 2, 3, 4, 5].map(i => pointLook(ds, i))).toEqual([cross, plain, plain, cross, plain, plain]);
      // The data, and so the scales and gaps, are unchanged.
      expect(ds.data).toEqual(qualityFigure(input).config!.data.datasets[0].data);
    });

    it('keeps the hollow legacy look on a run in the analysis', () => {
      const figure = timeToFirstAnswerFigure({ ...input, notAnalyzed: new Map([[101, 'left out in step 1']]) }, { theme: CC_PRINT_THEME });
      const proxy = figure.config!.data.datasets[1];
      // Run 103, the legacy proxy, stays hollow on the print page; run 101 is a cross in every dataset.
      expect(pointLook(proxy, 2)).toEqual(['circle', '#ffffff', CC_PRINT_THEME.series[3], 3.5, 2]);
      expect(pointLook(proxy, 0)).toEqual(['crossRot', 'rgba(0, 0, 0, 0)', CC_PRINT_THEME.muted, 4.5, 2]);
      expect(proxy.borderDash).toEqual([4, 4]);
    });

    it('draws the segments touching a run not in the analysis gray and dotted, and leaves the others to the dataset', () => {
      const ds = qualityFigure(marked).config!.data.datasets[0];
      // Segments 101–102, 103–104 and 104–105 touch a marked run.
      expect([0, 1, 2, 3, 4].map(i => segmentOption(ds, 'borderDash', i))).toEqual([[2, 3], undefined, [2, 3], [2, 3], undefined]);
      expect([0, 1, 2, 3, 4].map(i => segmentOption(ds, 'borderColor', i))).toEqual([muted, undefined, muted, muted, undefined]);
      expect(ds.borderDash).toEqual([]);
      expect(ds.borderColor).toBe(CC_SCREEN_THEME.series[0]);
    });

    it('counts the plotted runs not in the analysis in the takeaway and the alt text', () => {
      expect(qualityFigure(marked).takeaway)
        .toBe('Quality held between 71 and 74 across 6 runs. 2 runs not in the analysis are drawn as gray crosses.');
      const one = qualityFigure({ ...input, notAnalyzed: new Map([[104, 'left out in step 1']]) });
      expect(one.takeaway).toBe('Quality held between 71 and 74 across 6 runs. 1 run not in the analysis is drawn as a gray cross.');
      expect(one.altText).toBe(`Quality per run. ${one.takeaway}`);

      // Run 103 has no streaming rate, so the rate figure does not draw it; its legacy proxy is drawn.
      const legacy: CcFigureInput = { ...input, notAnalyzed: new Map([[103, 'after the last run']]) };
      expect(streamingRateFigure(legacy).takeaway).toBe(streamingRateFigure(input).takeaway);
      expect(timeToFirstAnswerFigure(legacy).takeaway).toBe(
        'Median time to first answer text ranged from 2.4 s to 2.4 s across 5 telemetry runs. 1 legacy run is drawn hollow as the legacy proxy.'
        + ' 1 run not in the analysis is drawn as a gray cross.');
      // A run outside the drawn points adds nothing.
      expect(qualityFigure({ ...input, notAnalyzed: new Map([[999, 'left out in step 1']]) }).takeaway)
        .toBe(qualityFigure(input).takeaway);
      expect(timelineOverviewFigure(marked).takeaway)
        .toBe('6 runs, 1 Overseer change, 1 annotation and 0 served-model changes in this range. 2 runs not in the analysis are drawn as gray crosses.');
    });

    it('names the reason in the tooltip of a run not in the analysis', () => {
      const config = qualityFigure(marked).config!;
      const ds = config.data.datasets[0];
      const label = config.options.plugins!.tooltip!.callbacks!.label as unknown as (item: unknown) => string;
      expect(label({ raw: ds.data[3], dataset: ds })).toBe('Quality Index (native grades): 72 — not in the analysis (left out in step 1)');
      expect(label({ raw: ds.data[0], dataset: ds })).toBe('Quality Index (native grades): 71 — not in the analysis (before the first run)');
      expect(label({ raw: ds.data[1], dataset: ds })).toBe('Quality Index (native grades): 73');
    });

    it('adds an In the analysis column to every data table', () => {
      const table = qualityFigure(marked).table;
      expect(table.columns).toEqual(['Run', 'Started', 'Quality Index (native)', 'In the analysis']);
      expect(table.rows.map(cells => cells[cells.length - 1]))
        .toEqual(['No — before the first run', 'Yes', 'Yes', 'No — left out in step 1', 'Yes', 'Yes']);
      expect(table.rows[0].slice(0, 3)).toEqual(qualityFigure(input).table.rows[0]);
      for (const figure of buildCcFigures(marked)) {
        expect(figure.table.columns[figure.table.columns.length - 1], figure.key).toBe('In the analysis');
      }
    });

    it('keeps the series ids, markers and overlay plugin', () => {
      const plain = buildCcFigures(input);
      buildCcFigures(marked).forEach((figure, i) => {
        expect(figure.config!.data.datasets.map(ds => ds.seriesId), figure.key).toEqual(plain[i].config!.data.datasets.map(ds => ds.seriesId));
        expect(figure.markers, figure.key).toEqual(plain[i].markers);
        expect(figure.config!.plugins.map(p => p.id), figure.key).toEqual(['ccOverlay']);
      });
    });

    it('draws every figure exactly as before with an empty map', () => {
      const plain = buildCcFigures(input);
      buildCcFigures({ ...input, notAnalyzed: new Map() }).forEach((figure, i) => {
        expect(figure.config!.data.datasets, figure.key).toEqual(plain[i].config!.data.datasets);
        expect(figure.takeaway, figure.key).toBe(plain[i].takeaway);
        expect(figure.altText, figure.key).toBe(plain[i].altText);
        expect(figure.table, figure.key).toEqual(plain[i].table);
        expect(figure.config!.options.plugins!.legend!.labels!.generateLabels, figure.key).toBeUndefined();
      });
      for (const ds of plain.flatMap(figure => figure.config!.data.datasets)) {
        expect(Object.values(ds).some(value => typeof value === 'function'), ds.seriesId).toBe(false);
        expect(ds.segment, ds.seriesId).toBeUndefined();
      }
      // The dataset options without a map.
      const native = plain[0].config!.data.datasets[0];
      expect(native).toEqual({
        seriesId: 'quality.native',
        label: 'Quality Index (native grades)',
        data: native.data,
        yAxisID: 'y',
        borderColor: CC_SCREEN_THEME.series[0],
        backgroundColor: CC_SCREEN_THEME.series[0],
        pointBackgroundColor: CC_SCREEN_THEME.series[0],
        pointBorderColor: CC_SCREEN_THEME.series[0],
        pointBorderWidth: 1,
        pointRadius: 3.5,
        pointHoverRadius: 5,
        borderWidth: 1.5,
        borderDash: [],
        spanGaps: false,
        tension: 0
      });
    });

    describe('on a chart', () => {
      let chart: Chart | null = null;

      beforeAll(() => {
        Chart.register(...APP_CHART_REGISTRABLES);
      });

      afterEach(() => {
        const canvas = chart?.canvas;
        chart?.destroy();
        canvas?.remove();
        chart = null;
      });

      function mount(figure: CcFigure): Chart {
        const canvas = document.createElement('canvas');
        canvas.width = 640;
        canvas.height = 320;
        document.body.appendChild(canvas);
        chart = new Chart(canvas, {
          ...figure.config!,
          options: { ...figure.config!.options, responsive: false, animation: false }
        } as unknown as ChartConfiguration);
        return chart;
      }

      it('resolves the cross on the marked point and keeps each legend item in its dataset’s own look', () => {
        // Run 101, the first point of both datasets, is not in the analysis.
        const drawn = mount(timeToFirstAnswerFigure(marked));
        const points = drawn.getDatasetMeta(0).data.map(point => point.options as Record<string, unknown>);
        expect([points[0]['pointStyle'], points[0]['radius'], points[0]['borderColor']]).toEqual(['crossRot', 4.5, muted]);
        expect([points[1]['pointStyle'], points[1]['radius'], points[1]['borderColor']]).toEqual(['circle', 3.5, CC_SCREEN_THEME.series[0]]);
        expect(drawn.legend!.legendItems!.map(item => [item.text, item.pointStyle, item.fillStyle, item.strokeStyle, item.lineWidth])).toEqual([
          ['Time to first answer text (telemetry)', 'circle', CC_SCREEN_THEME.series[0], CC_SCREEN_THEME.series[0], 1],
          ['Model time per answer (legacy proxy)', 'circle', 'rgba(0, 0, 0, 0)', CC_SCREEN_THEME.series[3], 2]
        ]);
      });
    });
  });
});
