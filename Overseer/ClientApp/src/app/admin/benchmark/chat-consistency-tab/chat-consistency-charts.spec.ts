import { Chart } from 'chart.js';
import type { ChartConfiguration } from 'chart.js';

import { APP_CHART_REGISTRABLES } from '../../../chart-registrables';
import { DEFAULT_FIGURE_STYLE } from '../model-comparison/figure-style';
import type { FigureAppearanceStyle } from '../model-comparison/figure-style';
import { resolveFigureTheme } from '../model-comparison/figure-theme';
import {
  CC_CHART_STYLE_DEFAULTS,
  CC_DECIMAL_CHOICES,
  CC_FIGURE_ENDPOINTS,
  CC_FIGURE_KEYS,
  CC_FIGURE_SERIES,
  CC_HEADER_LOGO_PX,
  CC_INTERVAL_COLUMN,
  CC_PRINT_THEME,
  CC_REPORT_FIGURES,
  CC_SCREEN_THEME,
  CC_TAG_GAP,
  CC_TAG_MAX_ROWS,
  CC_TAG_ROW_HEIGHT,
  CC_WORK_ENDPOINT_NOTE,
  CcChartDataset,
  CcChartPoint,
  CcChartStyle,
  CcChartTheme,
  CcFigure,
  CcFigureInput,
  analysisBands,
  analysisChartPoints,
  buildCcFigure,
  buildCcFigures,
  buildMarkers,
  ccAutoDecimalsText,
  ccAxisTimeTicks,
  ccChartThemeFor,
  ccHeaderHeight,
  ccIntervalText,
  ccNotComparableText,
  ccPeriodBreak,
  ccPeriodLabelPlacement,
  ccPlaceLabels,
  ccPointLabelFont,
  ccStepDecimals,
  ccTagBandHeight,
  ccTagRowHeight,
  ccTagRows,
  ccTimeTickLabel,
  ccTimeTicks,
  ccValueAxis,
  ccWhiskerPlugin,
  costFigure,
  dominantCommonGrader,
  dominantServedModel,
  qualityFigure,
  reliabilityFigure,
  sortedPoints,
  streamingRateFigure,
  timeToFirstAnswerFigure,
  timelineOverviewFigure,
  toolCallsFigure,
  workFigure
} from './chat-consistency-charts';
import { CcMarkerFilter, CcMarkerKind, eventGroupLabel, groupOverseerEvents } from './chat-consistency-events';
import { CC_REPORT_FIGURE_KEYS } from './chat-consistency.models';
import {
  CC_BATTERY_SET_KEY,
  ccAnalysisResult,
  ccAnnotation,
  ccBatteryPoint,
  ccEndpoint,
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
    it('captions a narrow range as the range it is, never as held, and labels the native grades', () => {
      const figure = qualityFigure(input);
      expect(figure.takeaway).toBe('The Intelligence Index ranged from 71 to 74 across 6 runs.');
      expect(figure.config!.data.datasets.map(ds => ds.label)).toEqual(['Intelligence Index (native grades)']);
      expect(figure.table.columns).toEqual(['Run', 'Started', 'Intelligence Index (native)']);
      expect(figure.table.rows[0]).toEqual(['#101', '2026-09-01 08:00 UTC', '71']);
    });

    it('captions a wide range with the latest score', () => {
      const figure = qualityFigure({ points: [ccPoint(1, '2026-09-01T00:00:00Z', { qualityIndex: 60 }), ccPoint(2, '2026-09-02T00:00:00Z', { qualityIndex: 70 })] });
      expect(figure.takeaway).toBe('The Intelligence Index ranged from 60 to 70 across 2 runs; the latest run scored 70.');
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
      expect(common.label).toBe('Intelligence, common grader (Opus)');
      // The latest calibration of the snapshot on a run wins.
      expect((common.data as CcChartPoint[]).map(p => p.y)).toEqual([70, 71]);
      expect(figure.table.columns[3]).toBe('Common grader (Opus)');
    });

    it('says there is nothing to chart without values', () => {
      const figure = qualityFigure({ points: [ccPoint(1, '2026-09-01T00:00:00Z', { qualityIndex: null })] });
      expect(figure.config).toBeNull();
      expect(figure.takeaway).toBe('No run in this range has an Intelligence figure.');
    });
  });

  describe('speed', () => {
    it('draws the legacy proxy hollow and dashed, and says so', () => {
      const figure = timeToFirstAnswerFigure(input);
      const [telemetry, proxy] = figure.config!.data.datasets;
      expect(telemetry.label).toBe('Time to first answer text (telemetry)');
      expect(proxy.label).toBe('Model time per answer (legacy proxy)');
      expect(proxy.borderDash).toEqual([4, 4]);
      expect(proxy.pointBackgroundColor).toBe(CC_SCREEN_THEME.surface);
      // Only run 103 has no telemetry time, so only it carries a proxy value.
      expect((proxy.data as CcChartPoint[]).filter(p => p.y !== null).map(p => p.runId)).toEqual([103]);
      expect(figure.takeaway).toBe(
        'Median time to first answer text was 2.4 s in all 5 telemetry runs. 1 legacy run is drawn hollow as the legacy proxy.');
    });

    it('paints the print theme background under a hollow point', () => {
      const figure = timeToFirstAnswerFigure(input, { theme: CC_PRINT_THEME });
      expect(figure.config!.data.datasets[1].pointBackgroundColor).toBe('#ffffff');
    });

    it('counts the runs without a streaming rate', () => {
      expect(streamingRateFigure(input).takeaway)
        .toBe('The answer streaming rate was 40.0 tok/s in all 5 runs. 1 legacy run recorded no rate.');
    });
  });

  it('draws output tokens and tool calls as two charts of one axis each', () => {
    const work = workFigure(input);
    expect(work.title).toBe('Work per turn (output tokens per answer)');
    expect(work.config!.data.datasets.map(ds => ds.seriesId)).toEqual(['work.tokens']);
    expect(work.config!.options.scales!['y1']).toBeUndefined();
    // The points are means over each run's delivered answers.
    expect(work.takeaway).toBe('Mean output tokens per answer were 1,200 in all 6 runs.');
    expect(work.table.columns).toEqual(['Run', 'Started', 'Output tokens per answer']);

    const tools = toolCallsFigure(input);
    expect(tools.key).toBe('tools');
    expect(tools.title).toBe('Tool calls per answer');
    expect(tools.config!.data.datasets.map(ds => ds.seriesId)).toEqual(['tools.calls']);
    expect(tools.config!.options.scales!['y1']).toBeUndefined();
    expect(tools.takeaway).toBe('Tool calls per answer were 3.5 in all 6 runs.');
    expect(tools.table.rows[0]).toEqual(['#101', '2026-09-01 08:00 UTC', '3.5']);
    expect(CC_REPORT_FIGURES['cc3-work']).toBe('work');
  });

  it('captions cost at one price card', () => {
    expect(costFigure(input).takeaway).toBe('Cost per question was $0.015 in all 6 runs, at one price card.');
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

  describe('time ticks', () => {
    const HOUR = 3_600_000;
    const DAY = 86_400_000;

    it('picks the smallest UTC-aligned step that keeps the ticks to the limit', () => {
      // A two-day span with six ticks: 12-hour steps on the half day.
      const twoDays = ccTimeTicks(at('2026-10-07T18:00:00Z'), at('2026-10-09T18:00:00Z'), 6);
      expect(twoDays.stepMs).toBe(12 * HOUR);
      expect(twoDays.values).toEqual([at('2026-10-08T00:00:00Z'), at('2026-10-08T12:00:00Z'), at('2026-10-09T00:00:00Z'),
        at('2026-10-09T12:00:00Z')]);
      expect(ccTimeTicks(at('2026-10-08T05:10:00Z'), at('2026-10-08T07:50:00Z'), 6).values)
        .toEqual([at('2026-10-08T06:00:00Z'), at('2026-10-08T07:00:00Z')]);
      // Weeks start on Monday; 2026-09-07 is a Monday.
      const weeks = ccTimeTicks(at('2026-09-01T00:00:00Z'), at('2026-10-10T00:00:00Z'), 6);
      expect(weeks.stepMs).toBe(7 * DAY);
      expect(weeks.values[0]).toBe(at('2026-09-07T00:00:00Z'));
      expect(new Date(weeks.values[1]).getUTCDay()).toBe(1);
    });

    it('steps by months from the first of a month, and by years beyond', () => {
      const months = ccTimeTicks(at('2026-01-15T00:00:00Z'), at('2026-06-20T00:00:00Z'), 6);
      expect(months.values).toEqual(['02', '03', '04', '05', '06'].map(m => at(`2026-${m}-01T00:00:00Z`)));
      const quarters = ccTimeTicks(at('2025-11-15T00:00:00Z'), at('2027-02-01T00:00:00Z'), 6);
      expect(quarters.values).toEqual([at('2026-01-01T00:00:00Z'), at('2026-04-01T00:00:00Z'), at('2026-07-01T00:00:00Z'),
        at('2026-10-01T00:00:00Z'), at('2027-01-01T00:00:00Z')]);
      const decades = ccTimeTicks(at('2000-01-01T00:00:00Z'), at('2030-01-01T00:00:00Z'), 4);
      expect(decades.values.length).toBeLessThanOrEqual(4);
      expect(decades.values.length).toBeGreaterThan(0);
      expect(ccTimeTicks(5, 5, 6).values).toEqual([]);
    });

    it('labels hours with the date on each day\'s first tick, days as month and day, long spans as months', () => {
      const first = at('2026-10-08T12:00:00Z');
      expect(ccTimeTickLabel(first, null, 12 * HOUR, 2 * DAY)).toBe('2026-10-08 12:00');
      expect(ccTimeTickLabel(at('2026-10-08T18:00:00Z'), first, 6 * HOUR, 2 * DAY)).toBe('18:00');
      expect(ccTimeTickLabel(at('2026-10-09T00:00:00Z'), at('2026-10-08T18:00:00Z'), 6 * HOUR, 2 * DAY)).toBe('2026-10-09 00:00');
      expect(ccTimeTickLabel(at('2026-10-08T00:00:00Z'), null, DAY, 20 * DAY)).toBe('Oct 8');
      expect(ccTimeTickLabel(at('2026-10-01T00:00:00Z'), null, 90 * DAY, 500 * DAY)).toBe('2026-10');
    });

    it('ticks a range under 48 hours in hours, with the date written once', () => {
      // 30 hours over 600 px: three-hour steps, the next day's midnight without its date.
      const short = ccAxisTimeTicks(at('2026-10-08T05:00:00Z'), at('2026-10-09T11:00:00Z'), 600);
      expect(short.stepMs).toBe(3 * HOUR);
      expect(short.values[0]).toBe(at('2026-10-08T06:00:00Z'));
      const span = 30 * HOUR;
      const labels = short.values.map((value, i) => ccTimeTickLabel(value, i > 0 ? short.values[i - 1] : null, short.stepMs, span));
      expect(labels[0]).toBe('2026-10-08 06:00');
      expect(labels.slice(1)).toEqual(['09:00', '12:00', '15:00', '18:00', '21:00', '00:00', '03:00', '06:00', '09:00']);
      // A narrow axis still ticks in hours, never in days.
      const narrow = ccAxisTimeTicks(at('2026-10-07T20:00:00Z'), at('2026-10-09T12:00:00Z'), 200);
      expect(narrow.stepMs).toBe(12 * HOUR);
      expect(ccTimeTickLabel(at('2026-10-09T00:00:00Z'), at('2026-10-08T12:00:00Z'), 12 * HOUR, 40 * HOUR)).toBe('00:00');
      // From 48 hours on, each day's first tick carries its date, as before.
      expect(ccAxisTimeTicks(at('2026-10-07T18:00:00Z'), at('2026-10-09T18:00:00Z'), 660))
        .toEqual(ccTimeTicks(at('2026-10-07T18:00:00Z'), at('2026-10-09T18:00:00Z'), 6));
    });

    it('builds the time axis from them', () => {
      const x = qualityFigure(input).config!.options.scales!['x'] as unknown as {
        afterBuildTicks: (scale: { min: number; max: number; width: number; ticks: { value: number }[] }) => void;
        grid: { display: boolean };
        title: { text: string };
      };
      const scale = { min: at('2026-10-07T18:00:00Z'), max: at('2026-10-09T18:00:00Z'), width: 660, ticks: [] as { value: number }[] };
      x.afterBuildTicks(scale);
      expect(scale.ticks.map(tick => tick.value)).toEqual(ccTimeTicks(scale.min, scale.max, 6).values);
      expect(x.grid.display).toBe(false);
      expect(x.title.text).toBe('Run start (UTC)');
    });
  });

  it('builds the overview with every marker kind counted', () => {
    const figure = timelineOverviewFigure(input);
    expect(figure.takeaway).toBe('6 runs, 1 Overseer change, 1 annotation and 0 served-model changes in this range.');
    expect(figure.config!.data.datasets.every(ds => (ds as { showLine?: boolean }).showLine === false)).toBe(true);
  });

  it('builds the timeline figures in order and maps every report key to one', () => {
    expect(buildCcFigures(input).map(f => f.key)).toEqual(['quality', 'ttfat', 'rate', 'work', 'tools', 'cost', 'reliability', 'timeline']);
    expect(CC_REPORT_FIGURE_KEYS.map(key => CC_REPORT_FIGURES[key])).toEqual(['quality', 'ttfat', 'work', 'timeline']);
  });

  it('turns analysis periods into bands', () => {
    const bands = analysisBands(
      { startUtc: '2026-09-01T00:00:00Z', endUtc: '2026-09-14T23:59:59.999Z' },
      { startUtc: '2026-09-15T00:00:00Z', endUtc: 'bad' });
    expect(bands).toEqual([{ name: 'Baseline', start: at('2026-09-01T00:00:00Z'), end: at('2026-09-14T23:59:59.999Z') }]);
  });

  describe('with an analysis', () => {
    // Runs 101–103 in the baseline, 104–106 in the comparison, which starts on 2026-09-15.
    const bands = analysisBands(
      { startUtc: '2026-09-01T00:00:00Z', endUtc: '2026-09-14T23:59:59.999Z' },
      { startUtc: '2026-09-15T00:00:00Z', endUtc: '2026-10-01T23:59:59.999Z' });
    const notComputed = (id: string, kind?: 'measurementChanged' | 'noCommonStratum' | 'tooFewPairs') =>
      ccEndpoint(id, { computed: false, notComputedReason: 'Not comparable.', ...(kind ? { notComputedKind: kind } : {}) });
    type SegmentOption = (ctx: { type: 'segment'; p0DataIndex: number; p1DataIndex: number; datasetIndex: number }) => unknown;
    const segmentColors = (ds: CcChartDataset) => [0, 1, 2, 3, 4].map(i =>
      ((ds.segment as unknown as Record<string, SegmentOption> | undefined)?.['borderColor'])?.(
        { type: 'segment', p0DataIndex: i, p1DataIndex: i + 1, datasetIndex: 0 }));

    it('joins no points of different periods where the measurement changed or no stratum is common', () => {
      for (const kind of ['measurementChanged', 'noCommonStratum'] as const) {
        const figureInput: CcFigureInput = { ...input, bands, endpoints: [notComputed('P1', kind), notComputed('P4', kind)] };
        expect(ccPeriodBreak('quality', figureInput), kind).toBe(at('2026-09-15T00:00:00Z'));
        // Only the segment from run 103 to run 104 crosses into the comparison.
        const transparent = 'rgba(0, 0, 0, 0)';
        expect(segmentColors(qualityFigure(figureInput).config!.data.datasets[0]), kind)
          .toEqual([undefined, undefined, transparent, undefined, undefined]);
        expect(segmentColors(workFigure(figureInput).config!.data.datasets[0]), kind)
          .toEqual([undefined, undefined, transparent, undefined, undefined]);
        // A run not in the analysis keeps its gray segments elsewhere.
        const marked = qualityFigure({ ...figureInput, notAnalyzed: new Map([[105, 'left out in step 1']]) });
        expect(segmentColors(marked.config!.data.datasets[0]), kind)
          .toEqual([undefined, undefined, transparent, CC_SCREEN_THEME.muted, CC_SCREEN_THEME.muted]);
      }
    });

    it('joins the periods as before for any other reason, a computed endpoint, or an analysis without the reason', () => {
      const cases: CcFigureInput[] = [
        { ...input, bands, endpoints: [notComputed('P1', 'tooFewPairs')] },
        { ...input, bands, endpoints: [ccEndpoint('P1')] },
        { ...input, bands, endpoints: [notComputed('P1')] },
        { ...input, endpoints: [notComputed('P1', 'measurementChanged')] }
      ];
      for (const figureInput of cases) {
        expect(ccPeriodBreak('quality', figureInput)).toBeNull();
        expect(qualityFigure(figureInput).config!.data.datasets[0].segment).toBeUndefined();
      }
      // The overview and the reliability chart have no endpoint.
      expect(ccPeriodBreak('timeline', { ...input, bands, endpoints: [notComputed('P1', 'measurementChanged')] })).toBeNull();
    });

    it('names the work figure\'s statistic, and says the endpoint pairs the questions while an analysis is shown', () => {
      expect(CC_FIGURE_KEYS.find(entry => entry.key === 'work')!.title).toBe('Work per turn (output tokens per answer)');
      expect(workFigure({ ...input, bands }).takeaway).toBe(`Mean output tokens per answer were 1,200 in all 6 runs. ${CC_WORK_ENDPOINT_NOTE}`);
      expect(CC_WORK_ENDPOINT_NOTE)
        .toBe('The endpoint compares the same questions in both periods, so its estimate can differ from these points.');
      expect(workFigure({ ...input, endpoints: [ccEndpoint('P4')] }).takeaway).toContain(CC_WORK_ENDPOINT_NOTE);
      expect(workFigure(input).takeaway).not.toContain(CC_WORK_ENDPOINT_NOTE);
    });
  });

  describe('period names', () => {
    const measure = (text: string) => text.length * 6;
    const area = { left: 0, top: 0, right: 600, bottom: 300 };

    it('writes the whole name where the band has room for it, and cuts it only where it has not', () => {
      expect(ccPeriodLabelPlacement('Comparison', measure, { left: 100, right: 172 }, area)).toEqual({
        text: 'Comparison', box: { left: 106, top: 4, right: 166, bottom: 15 }
      });
      expect(ccPeriodLabelPlacement('Comparison', measure, { left: 100, right: 150 }, area)!.text).toBe('Compa…');
      expect(ccPeriodLabelPlacement('Comparison', measure, { left: 100, right: 110 }, area)).toBeNull();
    });

    it('takes the first corner clear of the tags and the points, then the first clear of the tags', () => {
      const band = { left: 100, right: 400 };
      const topLeftTag = { left: 100, top: 0, right: 180, bottom: 20 };
      expect(ccPeriodLabelPlacement('Baseline', measure, band, area, [topLeftTag])!.box)
        .toEqual({ left: 346, top: 4, right: 394, bottom: 15 });
      // Points under both top corners: the bottom left.
      const points = [{ x: 120, y: 10 }, { x: 380, y: 10 }];
      expect(ccPeriodLabelPlacement('Baseline', measure, band, area, [], points)!.box)
        .toEqual({ left: 106, top: 285, right: 154, bottom: 296 });
      // A point under every corner: the first corner clear of the tags.
      const everywhere = [{ x: 120, y: 10 }, { x: 380, y: 10 }, { x: 120, y: 290 }, { x: 380, y: 290 }];
      expect(ccPeriodLabelPlacement('Baseline', measure, band, area, [topLeftTag], everywhere)!.box.left).toBe(346);
    });

    it('keeps the point labels clear of the names', () => {
      const name = { left: 106, top: 4, right: 154, bottom: 15 };
      expect(ccPlaceLabels([{ left: 120, top: 6, right: 140, bottom: 17 }, { left: 300, top: 6, right: 320, bottom: 17 }], area, [name]))
        .toEqual([1]);
    });
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
      // A run's Intelligence is its native Intelligence Index, a battery run's the Overall Intelligence Index.
      const forRuns = (key: string) => CC_FIGURE_SERIES[key as keyof typeof CC_FIGURE_SERIES]
        .map(series => series.id).filter(id => id !== 'quality.overall');
      for (const figure of buildCcFigures(full)) {
        expect(figure.config!.data.datasets.map(ds => ds.seriesId), figure.key).toEqual(forRuns(figure.key));
      }
      const battery = qualityFigure({
        points: [ccBatteryPoint(12, '2026-10-08T10:00:00Z', { commonGraderQuality: [grader] })], unitKind: 'batteryRun'
      });
      expect(battery.config!.data.datasets.map(ds => ds.seriesId)).toEqual(['quality.overall', 'quality.common']);
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

    it('hides the legacy proxy alone and keeps the telemetry series', () => {
      const shown = timeToFirstAnswerFigure(input);
      const hidden = timeToFirstAnswerFigure(input, { hiddenSeries: new Set(['ttfat.proxy']) });
      expect(hidden.config!.data.datasets.map(ds => ds.seriesId)).toEqual(['ttfat.telemetry']);
      expect(hidden.table).toEqual(shown.table);
      expect(hidden.takeaway).toBe(shown.takeaway);
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

  describe('value axes', () => {
    type ValueAxis = {
      beginAtZero?: boolean; min?: number; max?: number;
      ticks: { stepSize?: number; callback: (value: number) => string };
    } | undefined;
    const axis = (figure: CcFigure, id: 'y' | 'y1'): ValueAxis => figure.config!.options.scales![id] as unknown as ValueAxis;
    const bounds = (figure: CcFigure) => {
      const scale = axis(figure, 'y')!;
      return { min: scale.min, max: scale.max, stepSize: scale.ticks.stepSize };
    };
    const intelligence = { floor: 0, ceiling: 100, zeroBased: false, minSpan: 20, steps: [5, 10] };

    it('shows at least 20 points of Intelligence on nice bounds inside 0–100', () => {
      expect(ccValueAxis([82.0, 82.4], intelligence)).toEqual({ min: 70, max: 90, stepSize: 5 });
      // Near the ceiling, the window grows downward.
      expect(ccValueAxis([98, 99], intelligence)).toEqual({ min: 80, max: 100, stepSize: 5 });
      expect(ccValueAxis([1, 2], intelligence)).toEqual({ min: 0, max: 20, stepSize: 5 });
      // A wide spread is padded and snapped, never past the scale.
      const wide = ccValueAxis([40, 75], intelligence);
      expect(wide.min).toBeLessThanOrEqual(35);
      expect(wide.max).toBeGreaterThanOrEqual(80);
      expect([wide.min >= 0, wide.max <= 100, wide.stepSize]).toEqual([true, true, 10]);
    });

    it('draws the battery runs 82.0 and 82.4 on 70–90, and the full scale on request', () => {
      const figureInput: CcFigureInput = {
        points: [
          ccBatteryPoint(11, '2026-10-08T07:14:00Z', { overallIndex: 82.0 }),
          ccBatteryPoint(12, '2026-10-08T09:00:00Z', { overallIndex: 82.4 })
        ],
        unitKind: 'batteryRun'
      };
      expect(bounds(qualityFigure(figureInput))).toEqual({ min: 70, max: 90, stepSize: 5 });
      expect(bounds(qualityFigure(figureInput, { zeroBaseline: true }))).toEqual({ min: 0, max: 100, stepSize: 10 });
      const ticks = [70, 75, 80, 85, 90].map(value => axis(qualityFigure(figureInput), 'y')!.ticks.callback(value));
      expect(ticks).toEqual(['70', '75', '80', '85', '90']);
      expect(axis(qualityFigure(figureInput), 'y')!.beginAtZero).toBeUndefined();
    });

    it('starts the ratio measures at zero', () => {
      const ratio = (minSpan: number, ceiling: number | null = null) => ({ floor: 0, ceiling, zeroBased: true, minSpan });
      expect(ccValueAxis([38_700, 39_300], ratio(1000)).min).toBe(0);
      expect(ccValueAxis([317, 422], ratio(10))).toEqual({ min: 0, max: 500, stepSize: 100 });
      expect(ccValueAxis([0, 0], ratio(1))).toEqual({ min: 0, max: 1, stepSize: 0.2 });
      // Reliability at all zero reads 0–10 %, not 0–1 %.
      expect(ccValueAxis([0, 0, 0], ratio(10, 100))).toEqual({ min: 0, max: 10, stepSize: 2 });
      for (const figure of buildCcFigures(input).filter(f => f.key !== 'quality' && f.key !== 'timeline')) {
        expect(bounds(figure).min, figure.key).toBe(0);
        expect(axis(figure, 'y1'), figure.key).toBeUndefined();
      }
    });

    it('keeps a minimum span without values, from zero or the floor', () => {
      expect(ccValueAxis([], { floor: 0, ceiling: null, zeroBased: true, minSpan: 10 })).toEqual({ min: 0, max: 10, stepSize: 2 });
      expect(ccValueAxis([], intelligence)).toEqual({ min: 0, max: 20, stepSize: 5 });
    });

    it('writes the decimals each step needs', () => {
      expect([5, 10, 2.5, 0.2, 0.002, 0.0025].map(ccStepDecimals)).toEqual([0, 0, 1, 1, 3, 4]);
    });

    it('divides every figure’s axis into whole steps whose tick labels all differ', () => {
      const inputs: CcFigureInput[] = [
        input,
        { points: [ccPoint(1, '2026-09-01T00:00:00Z'), ccPoint(2, '2026-09-02T00:00:00Z')] },
        {
          points: [
            ccPoint(1, '2026-09-01T00:00:00Z', { qualityIndex: 82, medianTimeToFirstAnswerTextMs: 38_700, medianStreamingRate: 317, toolCallsPerAnswer: 0, costPerQuestionUsd: 0.0021 }),
            ccPoint(2, '2026-09-02T00:00:00Z', { qualityIndex: 83, medianTimeToFirstAnswerTextMs: 39_300, medianStreamingRate: 422, toolCallsPerAnswer: 0, costPerQuestionUsd: 0.0024 })
          ]
        },
        { points: [ccPoint(1, '2026-09-01T00:00:00Z', { medianTimeToFirstAnswerTextMs: 240, costPerQuestionUsd: 2.5, timeoutRate: 0.5 })] }
      ];
      for (const figureInput of inputs) {
        for (const zeroBaseline of [false, true]) {
          for (const figure of buildCcFigures(figureInput, { zeroBaseline }).filter(f => f.key !== 'timeline' && f.config)) {
            const scale = axis(figure, 'y')!;
            const { min, max, stepSize } = { min: scale.min!, max: scale.max!, stepSize: scale.ticks.stepSize! };
            const intervals = (max - min) / stepSize;
            expect(Math.abs(intervals - Math.round(intervals)), `${figure.key} ${min}–${max} by ${stepSize}`).toBeLessThan(1e-6);
            const labels = Array.from({ length: Math.round(intervals) + 1 }, (_, i) => scale.ticks.callback(min + i * stepSize));
            expect(new Set(labels).size, `${figure.key}: ${labels.join(' | ')}`).toBe(labels.length);
          }
        }
      }
    });

    it('takes the bounds from the drawn series only', () => {
      const figureInput: CcFigureInput = {
        points: [
          ccPoint(1, '2026-09-01T00:00:00Z', { medianTimeToFirstAnswerTextMs: 2000 }),
          ccPoint(2, '2026-09-02T00:00:00Z', { medianTimeToFirstAnswerTextMs: null, medianModelTimeMs: 90_000 })
        ]
      };
      expect(bounds(timeToFirstAnswerFigure(figureInput)).max).toBeGreaterThanOrEqual(90_000);
      expect(bounds(timeToFirstAnswerFigure(figureInput, { hiddenSeries: new Set(['ttfat.proxy']) })).max).toBeLessThan(5000);
    });

    it('leaves the other figures’ value axes unchanged by the full Intelligence scale', () => {
      const plain = buildCcFigures(input).filter(f => f.key !== 'quality' && f.key !== 'timeline');
      const zero = buildCcFigures(input, { zeroBaseline: true }).filter(f => f.key !== 'quality' && f.key !== 'timeline');
      expect(zero.map(bounds)).toEqual(plain.map(bounds));
    });
  });

  describe('point labels', () => {
    const box = (left: number, top: number, width = 20, height = 11) => ({ left, top, right: left + width, bottom: top + height });
    const area = box(0, 0, 600, 300);

    it('keeps labels in priority order and drops one that overlaps a kept label', () => {
      // The latest, the highest and the lowest come first; the fourth sits on the latest.
      const candidates = [box(500, 100), box(300, 20), box(100, 250), box(510, 105), box(200, 150)];
      expect(ccPlaceLabels(candidates, area)).toEqual([0, 1, 2, 4]);
    });

    it('keeps a 2 px gap between labels and drops one outside the area', () => {
      expect(ccPlaceLabels([box(0, 0), box(22, 0)], area)).toEqual([0, 1]);
      expect(ccPlaceLabels([box(0, 0), box(21, 0)], area)).toEqual([0]);
      expect(ccPlaceLabels([box(-5, 0), box(590, 0)], area)).toEqual([]);
    });

    describe('on a chart', () => {
      let chart: Chart | null = null;
      const drawn: string[] = [];

      beforeAll(() => {
        Chart.register(...APP_CHART_REGISTRABLES);
      });

      afterEach(() => {
        const canvas = chart?.canvas;
        chart?.destroy();
        canvas?.remove();
        chart = null;
        drawn.length = 0;
      });

      function mount(figure: CcFigure): Chart {
        const canvas = document.createElement('canvas');
        canvas.width = 640;
        canvas.height = 320;
        document.body.appendChild(canvas);
        const ctx = canvas.getContext('2d')!;
        const fillText = ctx.fillText.bind(ctx);
        ctx.fillText = (text: string, x: number, y: number) => {
          drawn.push(text);
          fillText(text, x, y);
        };
        chart = new Chart(canvas, {
          ...figure.config!,
          options: { ...figure.config!.options, responsive: false, animation: false }
        } as unknown as ChartConfiguration);
        return chart;
      }

      it('writes the value of both points of a two-point chart in the table’s format', () => {
        mount(qualityFigure({
          points: [
            ccBatteryPoint(11, '2026-10-08T07:14:00Z', { overallIndex: 82.0 }),
            ccBatteryPoint(12, '2026-10-08T09:00:00Z', { overallIndex: 82.4 })
          ],
          unitKind: 'batteryRun'
        }));
        expect(drawn).toContain('82.0');
        expect(drawn).toContain('82.4');
      });

      it('writes no point values on the reliability chart', () => {
        mount(reliabilityFigure({ points: [ccPoint(1, '2026-09-01T00:00:00Z', { timeoutRate: 0.04 }), ccPoint(2, '2026-09-02T00:00:00Z')] }));
        expect(drawn.some(text => text.endsWith(' %') && text.includes('.'))).toBe(false);
      });
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

    it('reserves the tag rows in their own layout box, leaving the top padding alone', () => {
      expect((qualityFigure(input).config!.options.layout!.padding as { top: number }).top).toBe(4);
      expect((reliabilityFigure(input).config!.options.layout!.padding as { right: number }).right).toBe(8);
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
      const plain = ['circle', CC_SCREEN_THEME.series[0], CC_SCREEN_THEME.surface, 4, 2];
      expect([0, 1, 2, 3, 4, 5].map(i => pointLook(ds, i))).toEqual([cross, plain, plain, cross, plain, plain]);
      // The data, and so the scales and gaps, are unchanged.
      expect(ds.data).toEqual(qualityFigure(input).config!.data.datasets[0].data);
    });

    it('keeps the hollow legacy look on a run in the analysis', () => {
      const figure = timeToFirstAnswerFigure({ ...input, notAnalyzed: new Map([[101, 'left out in step 1']]) }, { theme: CC_PRINT_THEME });
      const proxy = figure.config!.data.datasets[1];
      // Run 103, the legacy proxy, stays hollow on the print page; run 101 is a cross in every dataset.
      expect(pointLook(proxy, 2)).toEqual(['circle', '#ffffff', CC_PRINT_THEME.series[2], 4, 2]);
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
        .toBe('The Intelligence Index ranged from 71 to 74 across 6 runs. 2 runs not in the analysis are drawn as gray crosses.');
      const one = qualityFigure({ ...input, notAnalyzed: new Map([[104, 'left out in step 1']]) });
      expect(one.takeaway).toBe('The Intelligence Index ranged from 71 to 74 across 6 runs. 1 run not in the analysis is drawn as a gray cross.');
      expect(one.altText).toBe(`Intelligence per run. ${one.takeaway}`);

      // Run 103 has no streaming rate, so the rate figure does not draw it; its legacy proxy is drawn.
      const legacy: CcFigureInput = { ...input, notAnalyzed: new Map([[103, 'after the last run']]) };
      expect(streamingRateFigure(legacy).takeaway).toBe(streamingRateFigure(input).takeaway);
      expect(timeToFirstAnswerFigure(legacy).takeaway).toBe(
        'Median time to first answer text was 2.4 s in all 5 telemetry runs. 1 legacy run is drawn hollow as the legacy proxy.'
        + ' 1 run not in the analysis is drawn as a gray cross.');
      // A run outside the drawn points adds nothing.
      expect(qualityFigure({ ...input, notAnalyzed: new Map([[999, 'left out in step 1']]) }).takeaway)
        .toBe(qualityFigure(input).takeaway);
      expect(timelineOverviewFigure(marked).takeaway)
        .toBe('6 runs, 1 Overseer change, 1 annotation and 0 served-model changes in this range. 2 runs not in the analysis are drawn as gray crosses.');
    });

    it('names the reason on a second tooltip line for a run not in the analysis', () => {
      const config = qualityFigure(marked).config!;
      const ds = config.data.datasets[0];
      const label = config.options.plugins!.tooltip!.callbacks!.label as unknown as (item: unknown) => string | string[];
      expect(label({ raw: ds.data[3], dataset: ds })).toEqual(['Intelligence: 72', 'Not in the analysis: left out in step 1']);
      expect(label({ raw: ds.data[0], dataset: ds })).toEqual(['Intelligence: 71', 'Not in the analysis: before the first run']);
      expect(label({ raw: ds.data[1], dataset: ds })).toBe('Intelligence: 73');
    });

    it('adds an In the analysis column to every data table', () => {
      const table = qualityFigure(marked).table;
      expect(table.columns).toEqual(['Run', 'Started', 'Intelligence Index (native)', 'In the analysis']);
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
        label: 'Intelligence Index (native grades)',
        data: native.data,
        yAxisID: 'y',
        borderColor: CC_SCREEN_THEME.series[0],
        backgroundColor: CC_SCREEN_THEME.series[0],
        pointStyle: 'circle',
        pointBackgroundColor: CC_SCREEN_THEME.series[0],
        pointBorderColor: CC_SCREEN_THEME.surface,
        pointBorderWidth: 2,
        pointRadius: 4,
        pointHoverRadius: 6,
        pointHoverBorderWidth: 2,
        pointHitRadius: 12,
        borderWidth: 2,
        borderJoinStyle: 'round',
        borderCapStyle: 'round',
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
        expect([points[1]['pointStyle'], points[1]['radius'], points[1]['borderColor']]).toEqual(['circle', 4, CC_SCREEN_THEME.surface]);
        expect(drawn.legend!.legendItems!.map(item => [item.text, item.pointStyle, item.fillStyle, item.strokeStyle, item.lineWidth])).toEqual([
          ['Time to first answer text (telemetry)', 'circle', CC_SCREEN_THEME.series[1], CC_SCREEN_THEME.surface, 2],
          ['Model time per answer (legacy proxy)', 'circle', CC_SCREEN_THEME.surface, CC_SCREEN_THEME.series[2], 2]
        ]);
      });
    });
  });

  describe('battery runs', () => {
    const memberLabels = new Map([[301, 'Board Suite'], [302, 'Wiki Suite'], [303, 'Board Suite'], [304, 'Wiki Suite']]);
    const battery: CcFigureInput = {
      points: [
        ccBatteryPoint(12, '2026-10-08T10:00:00Z', { overallIndex: 86, memberRunIds: [303, 304] }),
        ccBatteryPoint(11, '2026-10-08T06:00:00Z', { overallIndex: 79, memberRunIds: [301, 302] })
      ],
      unitKind: 'batteryRun',
      memberLabels
    };

    it('plots one point per battery run, its quality the Overall Index', () => {
      const figure = qualityFigure(battery);
      const ds = figure.config!.data.datasets[0];
      expect(ds.label).toBe('Overall Intelligence Index (battery)');
      expect((ds.data as CcChartPoint[]).map(p => [p.runId, p.y])).toEqual([[11, 79], [12, 86]]);
      expect(figure.takeaway).toBe('The Overall Intelligence Index ranged from 79.0 to 86.0 across 2 battery runs; the latest battery run scored 86.0.');
      expect(figure.altText).toBe(`Intelligence per battery run. ${figure.takeaway}`);
      expect(figure.table.columns).toEqual(['Battery run', 'Started', 'Overall Intelligence Index', 'Note', 'Suites', 'Member runs']);
      expect(figure.table.rows[0]).toEqual(['#11', '2026-10-08 06:00 UTC', '79.0', '', '2', '#301 (Board Suite), #302 (Wiki Suite)']);
    });

    it('says which battery runs have no Overall Index, and why', () => {
      const figure = qualityFigure({
        ...battery,
        points: [
          ccBatteryPoint(11, '2026-10-08T06:00:00Z', {
            overallIndex: null, overallIndexNote: 'No battery analysis. Compute it from the battery report.'
          }),
          ccBatteryPoint(12, '2026-10-08T10:00:00Z', { overallIndex: 86 })
        ]
      });
      expect(figure.takeaway).toBe('The Overall Intelligence Index was 86.0 in the one battery run of this range. 1 battery run has no Overall Intelligence Index: no battery analysis.');
      expect(figure.table.rows[0][3]).toBe('No battery analysis. Compute it from the battery report.');
      expect(figure.table.rows[0][2]).toBe('—');
    });

    it('puts the Suites and Member runs columns on every table', () => {
      for (const figure of buildCcFigures(battery)) {
        expect(figure.table.columns[0], figure.key).toBe('Battery run');
        expect(figure.table.columns.slice(-2), figure.key).toEqual(['Suites', 'Member runs']);
      }
    });

    it('names battery runs on the axis, in the tooltip, the notes and the reliability takeaway', () => {
      const marked = qualityFigure({ ...battery, notAnalyzed: new Map([[11, 'left out in step 1']]) });
      const config = marked.config!;
      const callbacks = config.options.plugins!.tooltip!.callbacks! as unknown as Record<string, (items: unknown) => unknown>;
      const ds = config.data.datasets[0];
      const raw = ds.data[0];
      expect(callbacks['title']([{ raw }])).toBe('#11 · 2026-10-08 06:00 UTC');
      expect(callbacks['label']({ raw: ds.data[1], dataset: ds })).toBe('Overall Index: 86.0');
      // The members are on the data cards, not in the tooltip.
      expect(callbacks['footer']).toBeUndefined();
      expect((config.options.scales!['x'] as unknown as { title: { text: string } }).title.text).toBe('Battery run start (UTC)');
      expect(marked.takeaway.endsWith(' 1 battery run not in the analysis is drawn as a gray cross.')).toBe(true);
      expect(marked.table.columns[marked.table.columns.length - 1]).toBe('In the analysis');

      const failing = reliabilityFigure({
        ...battery, points: [...battery.points.slice(1), ccBatteryPoint(12, '2026-10-08T10:00:00Z', { timeoutRate: 0.05 })]
      });
      expect(failing.takeaway).toBe('The highest rate was timeouts at 5.0 % in battery run #12, across 2 battery runs.');
    });

    it('says the Overall Intelligence Index was one value when both battery runs format alike, never that it held', () => {
      const figure = qualityFigure({
        ...battery,
        points: [
          ccBatteryPoint(11, '2026-10-08T06:00:00Z', { overallIndex: 82.02 }),
          ccBatteryPoint(12, '2026-10-08T10:00:00Z', { overallIndex: 81.98 })
        ]
      });
      expect(figure.takeaway).toBe('The Overall Intelligence Index was 82.0 in both battery runs.');
      const close = qualityFigure({
        ...battery,
        points: [
          ccBatteryPoint(11, '2026-10-08T06:00:00Z', { overallIndex: 82.0 }),
          ccBatteryPoint(12, '2026-10-08T10:00:00Z', { overallIndex: 82.4 })
        ]
      });
      expect(close.takeaway).toBe('The Overall Intelligence Index ranged from 82.0 to 82.4 across 2 battery runs.');
      const three = qualityFigure({
        ...battery,
        points: [11, 12, 13].map(id => ccBatteryPoint(id, `2026-10-08T${String(id - 5).padStart(2, '0')}:00:00Z`, { overallIndex: 82 }))
      });
      expect(three.takeaway).toBe('The Overall Intelligence Index was 82.0 in all 3 battery runs.');
    });

    it('lists each battery run\'s member runs with their suite names, in member order', () => {
      const figure = qualityFigure({ ...battery, memberLabels: new Map([[301, 'Board Suite'], [303, 'Board Suite'], [304, 'Wiki Suite']]) });
      expect(figure.table.lists?.['Member runs']).toEqual([
        [{ ref: '#301', label: 'Board Suite' }, { ref: '#302', label: null }],
        [{ ref: '#303', label: 'Board Suite' }, { ref: '#304', label: 'Wiki Suite' }]
      ]);
      expect(figure.table.rows[0][5]).toBe('#301 (Board Suite), #302');
      const marked = qualityFigure({ ...battery, notAnalyzed: new Map([[11, 'left out in step 1']]) });
      expect(marked.table.columns[marked.table.columns.length - 1]).toBe('In the analysis');
      expect(marked.table.lists?.['Member runs']?.[1]).toEqual([{ ref: '#303', label: 'Board Suite' }, { ref: '#304', label: 'Wiki Suite' }]);
      expect(qualityFigure(input).table.lists).toBeUndefined();
    });

    it('writes the battery runs\' Intelligence to the chosen decimals', () => {
      const figure = qualityFigure(battery, { decimals: { quality: 2 } });
      expect(figure.table.rows.map(cells => cells[2])).toEqual(['79.00', '86.00']);
    });

    describe('interval whiskers', () => {
      // Battery run #12 has an interval resting on one round; #11 has none.
      const whiskered: CcFigureInput = {
        ...battery,
        points: [
          ccBatteryPoint(12, '2026-10-08T10:00:00Z', {
            overallIndex: 86, memberRunIds: [303, 304], overallIndexHalfWidth: 2.4, overallIndexIntervalNote: 'question sampling only'
          }),
          ccBatteryPoint(11, '2026-10-08T06:00:00Z', { overallIndex: 80, memberRunIds: [301, 302] })
        ]
      };
      const yBounds = (figure: CcFigure) => figure.config!.options.scales!['y'] as unknown as { min: number; max: number };

      it('draws a whisker only where a battery run has a half-width, in the axis range, with the interval column', () => {
        const figure = qualityFigure(whiskered);
        expect(figure.config!.plugins.map(p => p.id)).toEqual(['ccOverlay', 'ccWhiskers']);
        expect(yBounds(figure).max).toBeGreaterThanOrEqual(88.4);
        expect(figure.table.columns).toEqual(
          ['Battery run', 'Started', 'Overall Intelligence Index', CC_INTERVAL_COLUMN, 'Note', 'Suites', 'Member runs']);
        expect(CC_INTERVAL_COLUMN).toBe('95 % interval');
        expect(figure.table.rows.map(cells => cells[3])).toEqual(['—', '83.6–88.4']);
        expect(ccIntervalText(79.64, 84.51)).toBe('79.6–84.5');

        // No half-width, no whisker, no column; a run is never whiskered.
        expect(qualityFigure(battery).config!.plugins.map(p => p.id)).toEqual(['ccOverlay']);
        expect(qualityFigure(battery).table.columns).not.toContain(CC_INTERVAL_COLUMN);
        expect(qualityFigure(input).config!.plugins.map(p => p.id)).toEqual(['ccOverlay']);
        // A hidden Overall Index draws no whisker.
        const hidden = qualityFigure(whiskered, { hiddenSeries: new Set(['quality.overall']) });
        expect(hidden.config).toBeNull();
      });

      it('says what the bars are, with question sampling only when every bar rests on it', () => {
        expect(qualityFigure(whiskered).takeaway).toBe(
          'The Overall Intelligence Index ranged from 80.0 to 86.0 across 2 battery runs; the latest battery run scored 86.0. '
          + 'Bars show each battery run\'s 95 % interval for its own Overall Index (question sampling only); '
          + 'they are not the interval of the change between the periods.');
        const mixed = qualityFigure({
          ...whiskered,
          points: [whiskered.points[0], ccBatteryPoint(11, '2026-10-08T06:00:00Z', { overallIndex: 80, overallIndexHalfWidth: 1.5 })]
        });
        expect(mixed.takeaway).toContain('Bars show each battery run\'s 95 % interval for its own Overall Index; they are not');
        expect(qualityFigure(battery).takeaway).not.toContain('Bars show');
      });

      it('strokes each whisker and its caps in the series color, faded, between its ends', () => {
        const calls: string[] = [];
        const ctx = {
          strokeStyle: '', lineWidth: 0, lineCap: '',
          save: () => undefined, restore: () => undefined, setLineDash: () => undefined, beginPath: () => calls.push('begin'),
          moveTo: (x: number, y: number) => calls.push(`M${x},${y}`), lineTo: (x: number, y: number) => calls.push(`L${x},${y}`),
          stroke() { calls.push(`stroke ${this.strokeStyle}`); }
        };
        const chart = {
          ctx,
          chartArea: { left: 0, right: 1000, top: 0, bottom: 100 },
          scales: { x: { getPixelForValue: (value: number) => value }, y: { getPixelForValue: (value: number) => 100 - value } }
        };
        const plugin = ccWhiskerPlugin([{ x: 500, low: 70, high: 90, runId: 12, seriesId: 'quality.overall' }], '#c98500', '#a1a1aa');
        (plugin.beforeDatasetsDraw as unknown as (chart: unknown) => void)(chart);
        expect(calls).toEqual(['begin', 'M500,10', 'L500,30', 'M496,10', 'L504,10', 'M496,30', 'L504,30', 'stroke rgba(201, 133, 0, 0.55)']);
      });

      describe('on a chart', () => {
        let chart: Chart | null = null;
        const written: { text: string; y: number }[] = [];

        beforeAll(() => {
          Chart.register(...APP_CHART_REGISTRABLES);
        });

        afterEach(() => {
          const canvas = chart?.canvas;
          chart?.destroy();
          canvas?.remove();
          chart = null;
          written.length = 0;
        });

        it('keeps the point label clear of its whisker', () => {
          const figure = qualityFigure(whiskered);
          const canvas = document.createElement('canvas');
          canvas.width = 640;
          canvas.height = 320;
          document.body.appendChild(canvas);
          const ctx = canvas.getContext('2d')!;
          const fillText = ctx.fillText.bind(ctx);
          ctx.fillText = (text: string, x: number, y: number) => {
            written.push({ text, y });
            fillText(text, x, y);
          };
          chart = new Chart(canvas, {
            ...figure.config!, options: { ...figure.config!.options, responsive: false, animation: false }
          } as unknown as ChartConfiguration);
          const y = chart.scales['y'];
          const capTop = y.getPixelForValue(88.4);
          const capBottom = y.getPixelForValue(83.6);
          const label = written.find(entry => entry.text === '86.0')!;
          expect(label).toBeDefined();
          // Above the top cap, or below the bottom one: never across the whisker.
          expect(label.y + 11 <= capTop || label.y >= capBottom).toBe(true);
        });
      });
    });

    it('names the battery run of a served-model change', () => {
      const markers = buildMarkers([
        ccBatteryPoint(11, '2026-10-08T06:00:00Z', { servedModelIds: [{ modelId: 'a', callCount: 9 }] }),
        ccBatteryPoint(12, '2026-10-08T10:00:00Z', { servedModelIds: [{ modelId: 'b', callCount: 9 }] })
      ]);
      expect(markers.map(m => m.label)).toEqual(['Served model changed from a to b (battery run #12)']);
    });

    it('draws a battery analysis\'s battery runs and a run analysis\'s runs', () => {
      const batteryPoints = [
        ccBatteryPoint(11, '2026-10-08T06:00:00Z'),
        ccBatteryPoint(12, '2026-10-08T10:00:00Z'),
        ccBatteryPoint(13, '2026-10-08T12:00:00Z', { setKey: `battery:${'d'.repeat(64)}` })
      ];
      const result = ccAnalysisResult({
        comparisonSet: { kind: 'battery', key: CC_BATTERY_SET_KEY, label: 'Two initial suites (revision 1)' },
        units: [
          { unitId: 12, kind: 'batteryRun', period: 'comparison', startedAtUtc: '2026-10-08T10:00:00Z', memberRunIds: [1201, 1202] },
          { unitId: 13, kind: 'batteryRun', period: 'comparison', startedAtUtc: '2026-10-08T12:00:00Z', memberRunIds: [1301, 1302] }
        ]
      });
      const drawn = analysisChartPoints(result, timeline.points, batteryPoints);
      expect(drawn.unitKind).toBe('batteryRun');
      expect(drawn.points.map(p => p.runId)).toEqual([12]);

      const runs = analysisChartPoints(ccAnalysisResult(), timeline.points, batteryPoints);
      expect(runs.unitKind).toBe('run');
      expect(runs.points.map(p => p.runId)).toEqual([101, 102, 103, 104, 105, 106]);
    });
  });

  describe('decimal places', () => {
    type Callbacks = Record<string, (item: unknown) => unknown>;
    const label = (figure: CcFigure, datasetIndex = 0, pointIndex = 0) => {
      const config = figure.config!;
      const callbacks = config.options.plugins!.tooltip!.callbacks! as unknown as Callbacks;
      const ds = config.data.datasets[datasetIndex];
      return callbacks['label']({ raw: ds.data[pointIndex], dataset: ds });
    };
    const withFirst = (overrides: Parameters<typeof ccPoint>[2]) =>
      ({ ...input, points: timeline.points.map((point, i) => i === 0 ? { ...point, ...overrides } : point) });

    it('writes Intelligence to the chosen decimals in the table, takeaway and tooltip', () => {
      const figure = qualityFigure(input, { decimals: { quality: 2 } });
      expect(figure.table.rows[0][2]).toBe('71.00');
      expect(figure.takeaway).toBe('The Intelligence Index ranged from 71.00 to 74.00 across 6 runs.');
      expect(figure.altText).toBe(`Intelligence per run. ${figure.takeaway}`);
      expect(String(label(figure)).endsWith('71.00')).toBe(true);
    });

    it('writes times in seconds to the chosen decimals and keeps whole milliseconds under a second', () => {
      const figure = timeToFirstAnswerFigure(withFirst({ medianTimeToFirstAnswerTextMs: 850 }), { decimals: { ttfat: 2 } });
      expect(figure.table.rows.map(cells => cells[2])).toEqual(['850 ms', '2.40 s', '—', '2.40 s', '2.40 s', '2.40 s']);
      expect(figure.takeaway).toContain('from 850 ms to 2.40 s');
    });

    it('writes rates, tool calls, tokens and costs to the chosen decimals', () => {
      expect(streamingRateFigure(input, { decimals: { rate: 0 } }).table.rows[0][2]).toBe('40 tok/s');
      expect(toolCallsFigure(input, { decimals: { tools: 2 } }).table.rows[0][2]).toBe('3.50');
      const tokens = workFigure(input, { decimals: { work: 1 } });
      expect(tokens.table.rows[0][2]).toBe('1,200.0');
      expect(label(tokens)).toBe('Output tokens: 1,200.0');
      expect(costFigure(input, { decimals: { cost: 4 } }).table.rows[0][2]).toBe('$0.0150');
      expect(costFigure(input, { decimals: { cost: 0 } }).table.rows[0][2]).toBe('$0');
    });

    it('writes reliability rates to the chosen decimals', () => {
      const figure = reliabilityFigure(withFirst({ timeoutRate: 0.05 }), { decimals: { reliability: 0 } });
      expect(figure.table.columns[3]).toBe('Timeouts');
      expect(figure.table.rows[0][3]).toBe('5 %');
      expect(figure.table.rows[1][3]).toBe('0 %');
      expect(figure.takeaway).toBe('The highest rate was timeouts at 5 % in run #101, across 6 runs.');
      expect(label(figure, 1)).toBe('Timeouts: 5 %');
    });

    it('leaves every figure\'s axis ticks as their step writes them', () => {
      type ValueAxis = { min: number; max: number; ticks: { stepSize: number; callback: (value: number) => string } };
      const decimals = { quality: 3, ttfat: 3, rate: 3, work: 3, tools: 3, cost: 4, reliability: 3 };
      const ticks = (figure: CcFigure) => {
        const scale = figure.config!.options.scales!['y'] as unknown as ValueAxis;
        return Array.from({ length: Math.round((scale.max - scale.min) / scale.ticks.stepSize) + 1 },
          (_, i) => scale.ticks.callback(scale.min + i * scale.ticks.stepSize));
      };
      const plain = buildCcFigures(input).filter(f => f.key !== 'timeline');
      const chosen = buildCcFigures(input, { decimals }).filter(f => f.key !== 'timeline');
      expect(chosen.map(ticks)).toEqual(plain.map(ticks));
    });

    it('labels each chart\'s automatic precision and offers four decimals for cost alone', () => {
      expect(ccAutoDecimalsText('quality', 'run')).toBe('Automatic (0)');
      expect(ccAutoDecimalsText('quality', 'batteryRun')).toBe('Automatic (1)');
      expect(ccAutoDecimalsText('ttfat', 'run')).toBe('Automatic (1, in seconds)');
      for (const key of ['rate', 'tools', 'reliability'] as const) {
        expect(ccAutoDecimalsText(key, 'run'), key).toBe('Automatic (1)');
      }
      expect(ccAutoDecimalsText('work', 'batteryRun')).toBe('Automatic (0)');
      expect(ccAutoDecimalsText('cost', 'run')).toBe('Automatic (2–4)');
      expect(CC_DECIMAL_CHOICES.cost).toEqual([0, 1, 2, 3, 4]);
      expect(CC_DECIMAL_CHOICES.quality).toEqual([0, 1, 2, 3]);
    });
  });

  describe('styling', () => {
    const s = CC_SCREEN_THEME.series;

    it('keeps the validated palettes', () => {
      expect(CC_SCREEN_THEME.series).toEqual(['#c98500', '#3987e5', '#d95926', '#199e70', '#9085e9', '#d55181']);
      expect(CC_SCREEN_THEME.surface).toBe('#121212');
      expect(CC_PRINT_THEME.series).toEqual(['#b07400', '#2a78d6', '#eb6834', '#14936a', '#4a3aa7', '#cc4f86']);
      expect(CC_PRINT_THEME.surface).toBe('#ffffff');
    });

    it('colors each measure with its own palette slot, never by rank', () => {
      const colors = buildCcFigures(input).filter(f => f.key !== 'reliability')
        .map(f => [f.key, f.config!.data.datasets[0].borderColor]);
      expect(colors).toEqual([
        ['quality', s[0]], ['ttfat', s[1]], ['rate', s[3]], ['work', s[4]], ['tools', s[2]], ['cost', s[5]], ['timeline', s[0]]
      ]);
    });

    it('gives each reliability rate its own point shape', () => {
      expect(reliabilityFigure(input).config!.data.datasets.map(ds => ds.pointStyle))
        .toEqual(['circle', 'rect', 'triangle', 'rectRot', 'star']);
    });

    it('shows a legend only when a chart draws two or more series', () => {
      expect(qualityFigure(input).config!.options.plugins!.legend!.display).toBe(false);
      expect(timeToFirstAnswerFigure(input).config!.options.plugins!.legend!.display).toBe(true);
      expect(reliabilityFigure(input).config!.options.plugins!.legend!.display).toBe(true);
    });

    it('makes room on the right for half the widest point label of up to two series, and none on reliability or the overview', () => {
      const right = (figure: CcFigure) => (figure.config!.options.layout!.padding as { right: number }).right;
      // '73', two characters: half of 2 × 6.5, rounded up, plus 4; the 8 px floor otherwise.
      expect(right(qualityFigure(input))).toBe(11);
      // '1,200', five characters.
      expect(right(workFigure(input))).toBe(Math.ceil(5 * 6.5 / 2) + 4);
      expect(right(reliabilityFigure(input))).toBe(8);
      expect(right(timelineOverviewFigure(input))).toBe(8);
    });

    it('styles a compact screen tooltip and gives the print theme none', () => {
      const tooltip = (theme: typeof CC_SCREEN_THEME) =>
        qualityFigure(input, { theme }).config!.options.plugins!.tooltip as unknown as Record<string, unknown>;
      const screen = tooltip(CC_SCREEN_THEME);
      expect([screen['backgroundColor'], screen['cornerRadius'], screen['padding'], screen['caretSize']])
        .toEqual(['rgba(20, 20, 20, 0.96)', 6, { x: 8, y: 6 }, 5]);
      expect([(screen['titleFont'] as { size: number }).size, (screen['bodyFont'] as { size: number }).size]).toEqual([11, 11]);
      expect([screen['boxWidth'], screen['boxHeight']]).toEqual([6, 6]);
      expect(tooltip(CC_PRINT_THEME)['backgroundColor']).toBeUndefined();
      expect(tooltip(CC_PRINT_THEME)['enabled']).toBe(false);
    });

    it('shows the tooltip on a point only, its title the point and one line per series', () => {
      const config = streamingRateFigure({ points: [
        ccPoint(11, '2026-10-08T07:14:00Z', { medianStreamingRate: 422.2, streamingRateEstimated: true }),
        ccPoint(12, '2026-10-08T09:00:00Z', { medianStreamingRate: 317 })
      ], unitKind: 'run' }).config!;
      expect(config.options.interaction).toEqual({ mode: 'nearest', intersect: true, axis: 'xy' });
      const callbacks = config.options.plugins!.tooltip!.callbacks! as unknown as Record<string, (items: unknown) => unknown>;
      const estimated = config.data.datasets.find(ds => ds.seriesId === 'rate.estimated')!;
      expect(callbacks['title']([{ raw: estimated.data[0] }])).toBe('#11 · 2026-10-08 07:14 UTC');
      expect(callbacks['label']({ raw: estimated.data[0], dataset: estimated })).toBe('Estimated: 422.2 tok/s');
      expect(callbacks['footer']).toBeUndefined();
      // The series color only where the chart draws more than one series.
      expect(config.options.plugins!.tooltip!.displayColors).toBe(true);
      expect(qualityFigure(input).config!.options.plugins!.tooltip!.displayColors).toBe(false);
    });

    it('gives every series a short tooltip name', () => {
      for (const series of Object.values(CC_FIGURE_SERIES).flat()) {
        expect(series.shortLabel.length, series.id).toBeGreaterThan(0);
        expect(series.shortLabel.length, series.id).toBeLessThanOrEqual(series.label.length);
      }
      expect(CC_FIGURE_SERIES.quality.map(series => series.shortLabel)).toEqual(['Intelligence', 'Overall Index', 'Common grader']);
    });
  });

  describe('header band', () => {
    const logo = () => ({ image: document.createElement('canvas'), aspectRatio: 3248 / 850, heightPx: CC_HEADER_LOGO_PX });

    it('takes no height without a title, a subject or a logo', () => {
      expect(ccHeaderHeight(null, null, 600)).toBe(0);
      expect(ccHeaderHeight({ title: null, subject: null }, null, 600)).toBe(0);
    });

    it('fits the title and subject, or the logo where it is taller', () => {
      expect(ccHeaderHeight({ title: 'Quality per run', subject: 'GPT-5 high · runs' }, null, 600)).toBe(42);
      expect(ccHeaderHeight({ title: null, subject: null }, logo(), 600)).toBe(36);
      // At 100 px the logo is capped at 40 % of the width.
      expect(ccHeaderHeight({ title: null, subject: null }, logo(), 100)).toBe(Math.ceil(40 / (3248 / 850) + 8));
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

      it('leaves the plot at least 160 px high at 320 × 320 with the header and the logo', () => {
        const figure = qualityFigure(input, {
          header: { title: 'Quality per run', subject: 'GPT-5 high · Board Suite · runs' }, logo: logo()
        });
        const canvas = document.createElement('canvas');
        canvas.width = 320;
        canvas.height = 320;
        document.body.appendChild(canvas);
        chart = new Chart(canvas, {
          ...figure.config!,
          options: { ...figure.config!.options, responsive: false, animation: false }
        } as unknown as ChartConfiguration);
        expect(chart.chartArea.bottom - chart.chartArea.top).toBeGreaterThanOrEqual(160);
        // The header band sits above the plot.
        expect(chart.chartArea.top).toBeGreaterThanOrEqual(42);
      });
    });
  });

  describe('chart style', () => {
    const styled: CcChartStyle = {
      axisTextSizePx: 13,
      axisTitleSizePx: 15,
      axisTitleWeight: 400,
      gridlines: false,
      plotFrame: true,
      valueLabels: true,
      valueLabelSizePx: 13,
      legendTextSizePx: 14,
      markerTagSizePx: 14,
      lineWidthPx: 3,
      pointRadiusPx: 6,
      areaWash: false,
      labelWeight: 700
    };
    type Font = { family: string; size: number; weight?: number };
    type ScaleOptions = { ticks: { font: Font }; title: { font: Font }; grid: Record<string, unknown> };
    const scale = (figure: CcFigure, axis: 'x' | 'y') => figure.config!.options.scales![axis] as unknown as ScaleOptions;
    const legendFont = (figure: CcFigure) => figure.config!.options.plugins!.legend!.labels!.font as unknown as Font;
    const sizes = (figure: CcFigure) => figure.config!.data.datasets
      .map(ds => [ds.borderWidth, ds.pointRadius, ds.pointHoverRadius, ds.pointHoverBorderWidth]);
    const right = (figure: CcFigure) => (figure.config!.options.layout!.padding as { right: number }).right;
    const family = CC_SCREEN_THEME.fontFamily;

    it('draws the sizes and parts of the screen and report charts without a style', () => {
      expect(CC_CHART_STYLE_DEFAULTS).toEqual({
        axisTextSizePx: 11, axisTitleSizePx: 12, axisTitleWeight: 600, gridlines: true, plotFrame: false, valueLabels: true,
        valueLabelSizePx: 11, legendTextSizePx: 12, markerTagSizePx: 10, lineWidthPx: 2, pointRadiusPx: 4, areaWash: true,
        labelWeight: 600
      });
      const figure = timeToFirstAnswerFigure(input);
      for (const axis of ['x', 'y'] as const) {
        expect(scale(figure, axis).ticks.font, axis).toEqual({ family, size: 11 });
        expect(scale(figure, axis).title.font, axis).toEqual({ family, size: 12, weight: 600 });
      }
      expect(scale(figure, 'y').grid).toEqual({ color: CC_SCREEN_THEME.grid, lineWidth: 1 });
      expect(legendFont(figure)).toEqual({ family, size: 12 });
      expect(sizes(figure)).toEqual([[2, 4, 6, 2], [2, 4, 6, 2]]);
      expect(ccPointLabelFont()).toEqual({ sizePx: 11, weight: 600, offsetPx: 8 });
      expect(ccTagRowHeight(CC_CHART_STYLE_DEFAULTS.markerTagSizePx)).toBe(CC_TAG_ROW_HEIGHT);
      expect(right(qualityFigure(input))).toBe(11);
    });

    it('replaces the fonts, the grid and the line and point sizes with a given style', () => {
      const figure = timeToFirstAnswerFigure(input, { style: styled });
      for (const axis of ['x', 'y'] as const) {
        expect(scale(figure, axis).ticks.font, axis).toEqual({ family, size: 13 });
        expect(scale(figure, axis).title.font, axis).toEqual({ family, size: 15, weight: 400 });
      }
      expect(scale(figure, 'y').grid).toEqual({ display: false, color: CC_SCREEN_THEME.grid, lineWidth: 1 });
      expect(legendFont(figure)).toEqual({ family, size: 14, weight: 700 });
      // The hover radius scales with the point: 6 × 6 / 4.
      expect(sizes(figure)).toEqual([[3, 6, 9, 2], [3, 6, 9, 2]]);
      expect(ccPointLabelFont(styled)).toEqual({ sizePx: 13, weight: 700, offsetPx: 10 });
      expect(ccTagRowHeight(styled.markerTagSizePx)).toBe(20);
      expect(ccTagBandHeight(2, 20)).toBe(44);
    });

    it('scales the cross of a run not in the analysis with the point size', () => {
      type PointOption = (ctx: { raw: unknown; dataIndex: number }) => unknown;
      const ds = qualityFigure({ ...input, notAnalyzed: new Map([[104, 'left out in step 1']]) }, { style: styled })
        .config!.data.datasets[0];
      const radius = (i: number) => (ds.pointRadius as unknown as PointOption)({ raw: ds.data[i], dataIndex: i });
      expect([radius(0), radius(3)]).toEqual([6, 6.75]);
      expect(ds.pointHoverRadius).toBe(9);
    });

    it('leaves room for the point labels at their size, and none without them', () => {
      // '73', two characters of 6.5 × 22 / 11 px: half of 26, plus 4.
      expect(right(qualityFigure(input, { style: { ...styled, valueLabelSizePx: 22 } }))).toBe(17);
      expect(right(qualityFigure(input, { style: { ...styled, valueLabels: false } }))).toBe(8);
    });

    describe('on a chart', () => {
      let chart: Chart | null = null;
      const texts: { text: string; font: string }[] = [];
      const rects: { args: number[]; stroke: string }[] = [];
      let gradients = 0;

      beforeAll(() => {
        Chart.register(...APP_CHART_REGISTRABLES);
      });

      afterEach(() => {
        const canvas = chart?.canvas;
        chart?.destroy();
        canvas?.remove();
        chart = null;
        texts.length = 0;
        rects.length = 0;
        gradients = 0;
      });

      /** Mounts the figure, recording each text with its font, each outlined box and each gradient. */
      function mount(figure: CcFigure): Chart {
        const canvas = document.createElement('canvas');
        canvas.width = 640;
        canvas.height = 320;
        document.body.appendChild(canvas);
        const ctx = canvas.getContext('2d')!;
        const fillText = ctx.fillText.bind(ctx);
        ctx.fillText = (text: string, x: number, y: number) => {
          texts.push({ text, font: ctx.font });
          fillText(text, x, y);
        };
        const strokeRect = ctx.strokeRect.bind(ctx);
        ctx.strokeRect = (x: number, y: number, w: number, h: number) => {
          rects.push({ args: [x, y, w, h], stroke: String(ctx.strokeStyle) });
          strokeRect(x, y, w, h);
        };
        const createLinearGradient = ctx.createLinearGradient.bind(ctx);
        ctx.createLinearGradient = (x0: number, y0: number, x1: number, y1: number) => {
          gradients++;
          return createLinearGradient(x0, y0, x1, y1);
        };
        chart = new Chart(canvas, {
          ...figure.config!,
          options: { ...figure.config!.options, responsive: false, animation: false }
        } as unknown as ChartConfiguration);
        return chart;
      }

      const fontOf = (text: string) => texts.find(entry => entry.text === text)?.font ?? '';
      const tagBand = (drawn: Chart) =>
        (drawn as unknown as { boxes: object[] }).boxes.find(box => 'rowHeight' in box) as unknown as { rows: number; height: number };
      const frameRect = (drawn: Chart) => {
        const area = drawn.chartArea;
        return [area.left + 0.5, area.top + 0.5, area.right - area.left - 1, area.bottom - area.top - 1].join();
      };

      it('draws the tags and the point labels as before, and no frame, without a style; no wash over an axis not from zero', () => {
        const drawn = mount(qualityFigure(input));
        expect(fontOf('A1')).toMatch(/^bold 10px /);
        expect(fontOf('73')).toMatch(/^600 11px /);
        const band = tagBand(drawn);
        expect(band.rows).toBeGreaterThan(0);
        expect(band.height).toBe(band.rows * CC_TAG_ROW_HEIGHT + CC_TAG_GAP);
        // The Intelligence window starts above zero, so its area is not washed.
        expect(gradients).toBe(0);
        expect(rects.some(rect => rect.args.join() === frameRect(drawn))).toBe(false);
      });

      it('washes the area of a single series whose axis starts at zero', () => {
        mount(workFigure(input));
        expect(gradients).toBeGreaterThan(0);
      });

      it('draws a given style’s tags, point labels and frame, and no wash', () => {
        const theme = ccChartThemeFor(resolveFigureTheme(DEFAULT_FIGURE_STYLE.appearance));
        const drawn = mount(qualityFigure(input, { theme, style: styled }));
        expect(fontOf('A1')).toMatch(/^bold 14px /);
        expect(fontOf('73')).toMatch(/^(bold|700) 13px /);
        const band = tagBand(drawn);
        expect(band.rows).toBeGreaterThan(0);
        expect(band.height).toBe(band.rows * 20 + CC_TAG_GAP);
        expect(gradients).toBe(0);
        expect(rects.find(rect => rect.args.join() === frameRect(drawn))?.stroke).toBe(theme.frame);
      });

      it('writes no point values when the style turns them off', () => {
        mount(qualityFigure(input, { style: { ...styled, valueLabels: false } }));
        expect(texts.some(entry => ['71', '72', '73', '74'].includes(entry.text))).toBe(false);
        expect(texts.some(entry => entry.text === 'A1')).toBe(true);
      });
    });
  });

  describe('a theme without a tooltip', () => {
    /** `value` with every function replaced by a marker, so two builds of one figure compare equal. */
    const shape = (value: unknown): unknown => typeof value === 'function'
      ? 'function'
      : Array.isArray(value)
        ? value.map(shape)
        : value !== null && typeof value === 'object'
          ? Object.fromEntries(Object.entries(value).map(([key, entry]) => [key, shape(entry)]))
          : value;
    /** The options without the events and the tooltip. */
    const withoutInteraction = (figure: CcFigure) => {
      const options = shape(figure.config!.options) as Record<string, unknown> & { plugins: Record<string, unknown> };
      delete options['events'];
      delete options.plugins['tooltip'];
      return options;
    };
    /** The datasets without their hover radii. */
    const withoutHover = (figure: CcFigure) => figure.config!.data.datasets.map(ds => {
      const copy = shape(ds) as Record<string, unknown>;
      delete copy['pointHoverRadius'];
      return copy;
    });

    it('turns the tooltip off and ignores the pointer', () => {
      const options = qualityFigure(input, { theme: ccChartThemeFor(resolveFigureTheme()) }).config!.options;
      expect(options.plugins!.tooltip!.enabled).toBe(false);
      expect(options.events).toEqual([]);
      const screen = qualityFigure(input).config!.options;
      expect(screen.plugins!.tooltip!.enabled).toBeUndefined();
      expect('events' in screen).toBe(false);
    });

    it('differs from the same theme with a tooltip box only in the tooltip, the events and the hover radii', () => {
      const boxed: CcChartTheme = { ...CC_PRINT_THEME, tooltip: { background: '#ffffff', border: '#d1d5db' } };
      const marked: CcFigureInput = { ...input, notAnalyzed: new Map([[104, 'left out in step 1']]) };
      for (const figureInput of [input, marked]) {
        for (const { key } of CC_FIGURE_KEYS) {
          const bare = buildCcFigure(key, figureInput, { theme: CC_PRINT_THEME });
          const hover = buildCcFigure(key, figureInput, { theme: boxed });
          expect(withoutInteraction(bare), key).toEqual(withoutInteraction(hover));
          expect(withoutHover(bare), key).toEqual(withoutHover(hover));
          expect(bare.config!.options.events, key).toEqual([]);
          expect(hover.config!.options.events, key).toBeUndefined();
          expect(bare.config!.options.plugins!.tooltip!.enabled, key).toBe(false);
          expect(hover.config!.options.plugins!.tooltip!.enabled, key).toBeUndefined();
        }
      }
      // Without a tooltip box every point keeps its resting radius under the pointer, a cross included.
      expect(buildCcFigure('quality', input, { theme: CC_PRINT_THEME }).config!.data.datasets[0].pointHoverRadius).toBe(4);
      expect(buildCcFigure('quality', input, { theme: boxed }).config!.data.datasets[0].pointHoverRadius).toBe(6);
      const ds = buildCcFigure('quality', marked, { theme: CC_PRINT_THEME }).config!.data.datasets[0];
      expect(ds.pointHoverRadius).toBe(ds.pointRadius);
    });
  });

  describe('ccChartThemeFor', () => {
    const appearance = DEFAULT_FIGURE_STYLE.appearance;
    const mapped = (overrides: Partial<FigureAppearanceStyle>) => {
      const resolved = resolveFigureTheme({ ...appearance, ...overrides });
      return { resolved, theme: ccChartThemeFor(resolved) };
    };

    it('maps the dark appearance onto the screen palette with the appearance’s inks', () => {
      const { resolved, theme } = mapped({});
      expect(theme).toEqual({
        ...CC_SCREEN_THEME,
        text: resolved.chart.inkPrimary,
        secondary: resolved.chart.inkSecondary,
        muted: resolved.chart.inkMuted,
        grid: resolved.chart.gridline,
        background: null,
        surface: resolved.surface,
        tooltip: null,
        fontFamily: CC_SCREEN_THEME.fontFamily,
        frame: resolved.frameColor
      });
      expect(theme.series).toEqual(CC_SCREEN_THEME.series);
    });

    it('maps the light appearance onto the print palette', () => {
      const { resolved, theme } = mapped({ theme: 'light', fontFamily: 'inter' });
      expect(theme).toEqual({
        ...CC_PRINT_THEME,
        text: resolved.chart.inkPrimary,
        secondary: resolved.chart.inkSecondary,
        muted: resolved.chart.inkMuted,
        grid: resolved.chart.gridline,
        background: null,
        surface: resolved.surface,
        tooltip: null,
        fontFamily: resolved.fonts.chartStack,
        frame: resolved.frameColor
      });
      expect(theme.fontFamily).toContain('Inter');
      expect([theme.event, theme.baselineBand]).toEqual([CC_PRINT_THEME.event, CC_PRINT_THEME.baselineBand]);
    });

    it('writes a custom text color as the text, the axis titles and a muted mix', () => {
      const { resolved, theme } = mapped({ textColor: '#ff8800' });
      expect([theme.text, theme.secondary]).toEqual(['#ff8800', '#ff8800']);
      expect(theme.muted).toBe(resolved.chart.inkMuted);
      expect(theme.muted).not.toBe(resolveFigureTheme(appearance).chart.inkMuted);
    });

    it('paints no background on a transparent appearance and rings the points in its ground', () => {
      const { resolved, theme } = mapped({ background: 'transparent' });
      expect(resolved.background).toBeNull();
      expect(theme.background).toBeNull();
      expect(theme.surface).toBe(resolved.surface);
      expect(mapped({ background: 'custom', backgroundColor: '#203040' }).theme.surface).toBe('#203040');
    });
  });

  describe('the data table', () => {
    /** The table column of each series' value. */
    const COLUMNS: Readonly<Record<string, string>> = {
      'quality.native': 'Intelligence Index (native)',
      'quality.overall': 'Overall Intelligence Index',
      'ttfat.telemetry': 'Time to first answer text',
      'ttfat.proxy': 'Legacy proxy',
      'rate.measured': 'Streaming rate',
      'rate.estimated': 'Streaming rate',
      'work.tokens': 'Output tokens per answer',
      'tools.calls': 'Tool calls per answer',
      'cost.cost': 'Cost per question',
      ...Object.fromEntries(CC_FIGURE_SERIES.reliability.map(series => [series.id, series.label])),
      'timeline.telemetry': 'Measure',
      'timeline.legacy': 'Measure'
    };
    type Callbacks = { title: (items: unknown[]) => string; label: (item: unknown) => string | string[] };

    const battery: CcFigureInput = {
      points: [
        ccBatteryPoint(11, '2026-10-08T07:14:00Z', { overallIndex: 79, timeoutRate: 0.04 }),
        ccBatteryPoint(12, '2026-10-08T09:00:00Z', { overallIndex: 86, medianStreamingRate: 31.5, streamingRateEstimated: true })
      ],
      unitKind: 'batteryRun',
      memberLabels: new Map([[1101, 'Board Suite'], [1102, 'Wiki Suite']])
    };
    const notAnalyzed = new Map<number, string>([[101, 'before the first run'], [104, 'left out in step 1']]);

    /**
     * Every fact the figure's tooltip writes about a plotted point is in the point's table row: the unit
     * and its start, each series' value, and whether it is in the analysis.
     */
    function expectTooltipFactsInTable(figure: CcFigure, figureInput: CcFigureInput): void {
      const config = figure.config!;
      const callbacks = config.options.plugins!.tooltip!.callbacks as unknown as Callbacks;
      const { columns, rows } = figure.table;
      const marks = figureInput.notAnalyzed;
      expect(columns.slice(0, 2), figure.key).toEqual([figureInput.unitKind === 'batteryRun' ? 'Battery run' : 'Run', 'Started']);
      if (marks) expect(columns[columns.length - 1], figure.key).toBe('In the analysis');
      let checked = 0;
      for (const ds of config.data.datasets) {
        const column = columns.indexOf(COLUMNS[ds.seriesId]);
        expect(column, ds.seriesId).toBeGreaterThan(1);
        for (const raw of ds.data) {
          if (raw.y === null) continue;
          const where = `${ds.seriesId} #${raw.runId}`;
          const row = rows.find(cells => cells[0] === `#${raw.runId}`)!;
          expect(row, where).toBeDefined();
          expect(callbacks.title([{ raw }]), where).toBe(`${row[0]} · ${row[1]}`);
          const lines = [callbacks.label({ raw, dataset: ds })].flat();
          if (ds.seriesId.startsWith('timeline.')) {
            expect(row[column], where).toBe(ds.seriesId === 'timeline.legacy' ? 'legacy' : 'telemetry');
          } else {
            expect(row[column], where).toBe(lines[0].slice(lines[0].indexOf(': ') + 2));
          }
          if (ds.seriesId.startsWith('rate.')) {
            expect(row[columns.indexOf('Estimated')], where).toBe(ds.seriesId === 'rate.estimated' ? 'Yes' : 'No');
          }
          const reason = marks?.get(raw.runId);
          expect(lines[1], where).toBe(reason === undefined ? undefined : `Not in the analysis: ${reason}`);
          if (marks) expect(row[row.length - 1], where).toBe(reason === undefined ? 'Yes' : `No — ${reason}`);
          checked++;
        }
      }
      expect(checked, figure.key).toBeGreaterThan(0);
    }

    it('carries the unit, the start and every series’ value of a run', () => {
      for (const { key } of CC_FIGURE_KEYS) expectTooltipFactsInTable(buildCcFigure(key, input), input);
    });

    it('carries the unit, the start and every series’ value of a battery run', () => {
      for (const { key } of CC_FIGURE_KEYS) expectTooltipFactsInTable(buildCcFigure(key, battery), battery);
    });

    it('carries whether a run or a battery run is in the analysis, and why not', () => {
      const runs: CcFigureInput = { ...input, notAnalyzed };
      const batteries: CcFigureInput = { ...battery, notAnalyzed: new Map([[11, 'left out in step 1']]) };
      for (const { key } of CC_FIGURE_KEYS) {
        expectTooltipFactsInTable(buildCcFigure(key, runs), runs);
        expectTooltipFactsInTable(buildCcFigure(key, batteries), batteries);
      }
    });
  });

  describe('report charts', () => {
    const HOUR_MS = 3_600_000;
    /** Four runs on one day, inside periods that span two days. */
    const oneDay: CcFigureInput = {
      points: [
        ccPoint(201, '2026-10-08T08:00:00Z'),
        ccPoint(202, '2026-10-08T10:00:00Z'),
        ccPoint(203, '2026-10-08T14:00:00Z'),
        ccPoint(204, '2026-10-08T20:00:00Z')
      ],
      bands: analysisBands(
        { startUtc: '2026-10-07T00:00:00Z', endUtc: '2026-10-08T12:00:00Z' },
        { startUtc: '2026-10-08T12:00:00Z', endUtc: '2026-10-09T23:59:59Z' }
      )
    };
    const xBounds = (figure: CcFigure) => {
      const x = figure.config!.options.scales!['x'] as unknown as { min: number; max: number };
      return { min: x.min, max: x.max };
    };

    it('fits the time axis to the plotted points, padded by the larger of 5 % and 30 minutes, not to the periods', () => {
      const fitted = xBounds(qualityFigure(oneDay, { fitToData: true }));
      const span = 12 * HOUR_MS;
      expect(fitted.min).toBe(at('2026-10-08T08:00:00Z') - span * 0.05);
      expect(fitted.max).toBe(at('2026-10-08T20:00:00Z') + span * 0.05);

      // Without it, the axis spans the periods too.
      const wide = xBounds(qualityFigure(oneDay));
      expect(wide.min).toBeLessThan(at('2026-10-07T00:00:00Z'));
      expect(wide.max).toBeGreaterThan(at('2026-10-09T23:00:00Z'));

      const close: CcFigureInput = { points: [ccPoint(301, '2026-10-08T08:00:00Z'), ccPoint(302, '2026-10-08T09:00:00Z')] };
      const padded = xBounds(workFigure(close, { fitToData: true }));
      expect(padded.min).toBe(at('2026-10-08T07:30:00Z'));
      expect(padded.max).toBe(at('2026-10-08T09:30:00Z'));
    });

    it('keeps a marker near the points inside the fitted axis', () => {
      const withEvent: CcFigureInput = {
        ...oneDay,
        events: [ccEvent({ atUtc: '2026-10-08T12:00:00Z', runId: 203, previousRunId: 202 })]
      };
      const figure = timelineOverviewFigure(withEvent, { fitToData: true });
      const bounds = xBounds(figure);
      for (const marker of figure.markers) {
        expect(marker.x).toBeGreaterThan(bounds.min);
        expect(marker.x).toBeLessThan(bounds.max);
      }
    });

    it('opens the caption of a figure whose endpoint was not computable with Not comparable and its reason', () => {
      const endpoints = [
        ccEndpoint('P1', { computed: false, notComputedReason: 'No common grader covers every run.' }),
        ccEndpoint('P2'),
        ccEndpoint('P4', { computed: false, notComputedReason: null })
      ];
      const quality = buildCcFigure('quality', { ...input, endpoints });
      expect(quality.takeaway).toBe(
        'Not comparable across the periods: no common grader covers every run. The Intelligence Index ranged from 71 to 74 across 6 runs.');
      expect(quality.altText.startsWith('Not comparable across the periods: no common grader covers every run. ')).toBe(true);

      expect(buildCcFigure('ttfat', { ...input, endpoints }).takeaway).toBe(buildCcFigure('ttfat', input).takeaway);
      expect(buildCcFigure('work', { ...input, endpoints }).takeaway.startsWith(
        'Not comparable across the periods: the analysis could not compute this measure. ')).toBe(true);
      expect(buildCcFigure('timeline', { ...input, endpoints }).takeaway).toBe(buildCcFigure('timeline', input).takeaway);
      // Without endpoints nothing is said.
      expect(buildCcFigure('quality', input).takeaway).toBe('The Intelligence Index ranged from 71 to 74 across 6 runs.');
    });

    it('keeps an opening acronym of the reason in capitals', () => {
      expect(ccNotComparableText('TTFT telemetry is missing in the baseline.'))
        .toBe('Not comparable across the periods: TTFT telemetry is missing in the baseline.');
      expect(ccNotComparableText('  ')).toBe('Not comparable across the periods: the analysis could not compute this measure.');
    });

    it('maps each report figure to the endpoint it plots', () => {
      expect(CC_REPORT_FIGURE_KEYS.map(key => CC_FIGURE_ENDPOINTS[CC_REPORT_FIGURES[key]] ?? null)).toEqual(['P1', 'P2', 'P4', null]);
    });
  });
});
