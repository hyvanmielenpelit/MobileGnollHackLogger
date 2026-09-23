import { Chart } from 'chart.js';
import ChartDataLabels from 'chartjs-plugin-datalabels';
import {
  ACCENT,
  CATEGORICAL_PALETTE_DARK,
  CHART_INK,
  CHART_SURFACE,
  DEFAULT_MODEL_SORT,
  DE_EMPHASIS_FILL,
  DIRECTION_MARKER_GAP,
  DOMINATED_REGION_FILL,
  FRONTIER_UNCERTAINTY_NOTE,
  IDENTITY_SHAPES,
  MEAN_TIME_NO_INTERVAL_NOTE,
  MAX_PLOTTED_ENTRIES,
  PALETTE_VALIDATION_INPUT,
  PROFILE_AXIS_ORDER,
  P1_STACK_BREAKPOINT_PX,
  ReducedMotionWatcher,
  buildComparisonFigures,
  buildIdentityGlyphs,
  buildProfilePlot,
  buildQualityCostScatter,
  buildQualitySpeedScatter,
  buildSmallMultiples,
  buildSpeedCostScatter,
  computeParetoFrontier,
  directLabelPlugin,
  directionMarkerMetrics,
  directionMarkerPlugin,
  dominatedRegionPlugin,
  errorBarPlugin,
  glyphFor,
  measureDirectLabelBlock,
  normalizeProfile,
  placeDirectLabels,
  segmentIntersectsRect,
  segmentsIntersect,
  selectPlottedEntries,
  speedLowerIsBetter,
  speedValue,
  suiteCostUsd,
} from './model-comparison-charts';
import type {
  ChartSpec,
  DirectLabelAnchor,
  DirectLabelBlock,
  DirectLabelBox,
  DirectLabelPluginOptions,
  DirectLabelValue,
  DirectionMarkerPluginOptions,
  ModelComparisonContext,
  ModelComparisonEntry,
  SmallMultiplesOptions,
} from './model-comparison-charts';
import { DEFAULT_FIGURE_STYLE, HIDDEN_INTERVALS_NOTE } from './figure-style';
import type { BarFigureStyle, FigureStyle, ScatterFigureStyle } from './figure-style';
import { APP_CHART_REGISTRABLES } from '../../../chart-registrables';
import { CONFIG_ANALYTICS_CHART_TYPE } from '../../config-analytics/config-analytics.component';

/** Minimal shape of the scale options the specs assert on, so no test reaches into deep partials. */
interface ScaleProbe {
  type?: string;
  min?: number;
  max?: number;
  beginAtZero?: boolean;
  grace?: string;
  title?: { text?: string | string[] };
  afterBuildTicks?: (scale: { ticks: { value: number }[] }) => void;
  ticks?: { callback?: (value: number | string, index: number, ticks: { value: number }[]) => string };
}

/** The tick values a scatter axis sets, read by running its `afterBuildTicks` on a fake scale. */
function ticksOf(scale: ScaleProbe): number[] {
  const fake = { ticks: [] as { value: number }[] };
  scale.afterBuildTicks?.(fake);
  return fake.ticks.map((tick) => tick.value);
}

/** Every figure the set builds, flattened, so chrome assertions can cover all seven at once. */
function allSpecs(figures: ReturnType<typeof buildComparisonFigures>): ChartSpec[] {
  return [
    figures.qualitySpeed,
    figures.qualityCost,
    figures.speedCost,
    figures.smallMultiples.quality,
    figures.smallMultiples.speed,
    figures.smallMultiples.cost,
    figures.profile,
  ] as unknown as ChartSpec[];
}

function scaleOf(config: unknown, axis: 'x' | 'y'): ScaleProbe {
  const probe = config as { options?: { scales?: Record<string, ScaleProbe> } };
  return probe.options?.scales?.[axis] ?? {};
}

/** An axis title's text, always as an array: a single-line title comes back as its one element. */
const titleLines = (scale: ScaleProbe): (string | undefined)[] =>
  Array.isArray(scale.title?.text) ? scale.title.text : [scale.title?.text];

/** Datasets and data points are read structurally; chart.js types are far wider than the assertion. */
function datasetsOf(config: unknown): Record<string, unknown>[] {
  return (config as { data: { datasets: Record<string, unknown>[] } }).data.datasets;
}

function pointsOf(config: unknown, datasetIndex = 0): Record<string, number | string | undefined>[] {
  return datasetsOf(config)[datasetIndex]['data'] as Record<string, number | string | undefined>[];
}

/** Float-tolerant array comparison, so a normalized value never fails on a binary rounding tail. */
function expectClose(actual: readonly number[], expected: readonly number[]): void {
  expect(actual.length).toBe(expected.length);
  actual.forEach((value, i) => expect(value).toBeCloseTo(expected[i], 10));
}

const CONTEXT: ModelComparisonContext = {
  itemsPerRun: 10,
  pricingBasisLabel: 'Current catalog, 2026-09-07',
  pricingBasis: 'Current',
  pricedOn: '2026-09-07T10:00:00Z',
  suiteName: 'Fundamental',
};

function makeEntry(overrides: Partial<ModelComparisonEntry> & { key: string }): ModelComparisonEntry {
  return {
    label: overrides.key,
    runCount: 3,
    intelligenceIndex: 50,
    intelligenceIndexCi95HalfWidth: 4,
    speedIndex: 50,
    speedIndexSaturated: false,
    speedIndexSd: null,
    ttftP50Ms: 1000,
    ttftP90Ms: 1800,
    modelTimeMeanMs: 900,
    totalModelTimeMs: 9000,
    totalModelTimeSdMs: 1200,
    candidateCostPerQuestionUsd: 0.002,
    candidateCostPerQuestionSdUsd: 0.0003,
    totalRunCostUsd: 0.4,
    totalRunCostSdUsd: 0.05,
    speedDegraded: false,
    costDegraded: false,
    excluded: false,
    excludedReasonKeys: [],
    ...overrides,
  };
}

/**
 * Three models whose quality order is the exact reverse of their cost order, so the profile plot's
 * cost inversion cannot pass by coincidence.
 */
const PROFILE_FIXTURE: ModelComparisonEntry[] = [
  makeEntry({
    key: 'A',
    intelligenceIndex: 90,
    speedIndex: 100,
    ttftP50Ms: 500,
    candidateCostPerQuestionUsd: 0.03,
  }),
  makeEntry({
    key: 'B',
    intelligenceIndex: 60,
    speedIndex: 50,
    ttftP50Ms: 300,
    candidateCostPerQuestionUsd: 0.02,
  }),
  makeEntry({
    key: 'C',
    intelligenceIndex: 30,
    speedIndex: 0,
    ttftP50Ms: 100,
    candidateCostPerQuestionUsd: 0.01,
  }),
];

const BASE_FIGURE_OPTIONS = {
  context: CONTEXT,
  glyphs: buildIdentityGlyphs(PROFILE_FIXTURE),
  reducedMotion: false,
};

function smallMultiplesOptions(overrides: Partial<SmallMultiplesOptions> = {}): SmallMultiplesOptions {
  return {
    ...BASE_FIGURE_OPTIONS,
    speedMeasure: 'speedIndex',
    costMeasure: 'candidateSuite',
    orientation: 'vertical',
    ...overrides,
  };
}

describe('model-comparison-charts', () => {
  describe('palette', () => {
    it('exports the validator input verbatim, so the check is a paste and never a retype', () => {
      expect(PALETTE_VALIDATION_INPUT).toBe('#3987e5,#d95926,#199e70');
      expect(CATEGORICAL_PALETTE_DARK).toEqual(['#3987e5', '#d95926', '#199e70']);
    });

    it('caps the categorical hues at three, the all-pairs ceiling for scatter forms', () => {
      expect(CATEGORICAL_PALETTE_DARK.length).toBe(3);
      expect(new Set(CATEGORICAL_PALETTE_DARK).size).toBe(3);
    });

    it('is designed against the composited dark panel surface', () => {
      expect(CHART_SURFACE).toBe('#0b0b0b');
    });

    it('keeps the emphasis accent out of the categorical set', () => {
      expect(CATEGORICAL_PALETTE_DARK as readonly string[]).not.toContain(ACCENT);
    });
  });

  describe('identity glyphs', () => {
    const eight = Array.from({ length: 8 }, (_, i) => makeEntry({ key: `m${i}` }));

    it('gives eight models eight distinct hue x shape pairs', () => {
      const glyphs = buildIdentityGlyphs(eight);
      const pairs = eight.map((e) => {
        const glyph = glyphFor(glyphs, e.key);
        return `${glyph.hue}|${glyph.shape}`;
      });
      expect(new Set(pairs).size).toBe(8);
      expect(pairs.every((p) => IDENTITY_SHAPES.some((s) => p.endsWith(s)))).toBeTrue();
    });

    it('does not repaint the survivors when a model is filtered out', () => {
      const glyphs = buildIdentityGlyphs(eight);
      const before = eight.map((e) => glyphFor(glyphs, e.key));

      const filtered = eight.filter((e) => e.key !== 'm2' && e.key !== 'm5');
      const figures = buildComparisonFigures(filtered, { context: CONTEXT, glyphSource: eight });

      for (const entry of filtered) {
        const index = eight.findIndex((e) => e.key === entry.key);
        expect(glyphFor(figures.glyphs, entry.key)).toEqual(before[index]);
      }
    });

    it('gives excluded entries no glyph slot, because they are never plotted', () => {
      const withExcluded = [
        makeEntry({ key: 'ok1' }),
        makeEntry({ key: 'bad', excluded: true, excludedReasonKeys: ['ScoringMethodVersion'] }),
        makeEntry({ key: 'ok2' }),
      ];
      const glyphs = buildIdentityGlyphs(withExcluded);
      expect(glyphs.has('bad')).toBeFalse();
      expect(glyphFor(glyphs, 'ok1').hue).toBe(CATEGORICAL_PALETTE_DARK[0]);
      expect(glyphFor(glyphs, 'ok2').hue).toBe(CATEGORICAL_PALETTE_DARK[1]);
    });
  });

  describe('the eight-entry cap', () => {
    it('plots eight and pushes the ninth into the table with a visible notice', () => {
      const nine = Array.from({ length: 9 }, (_, i) =>
        makeEntry({ key: `m${i}`, label: `Model ${i}`, intelligenceIndex: 90 - i }),
      );
      const selection = selectPlottedEntries(nine, DEFAULT_MODEL_SORT, 'speedIndex', 'candidateSuite', CONTEXT);

      expect(MAX_PLOTTED_ENTRIES).toBe(8);
      expect(selection.plotted.length).toBe(8);
      expect(selection.overflow.map((e) => e.key)).toEqual(['m8']);
      expect(selection.notices.join(' ')).toContain('Model 8');
    });

    it('never lets an excluded entry reach a chart', () => {
      const entries = [
        makeEntry({ key: 'ok' }),
        makeEntry({ key: 'v9', excluded: true, excludedReasonKeys: ['ScoringMethodVersion'] }),
      ];
      const selection = selectPlottedEntries(entries, DEFAULT_MODEL_SORT, 'speedIndex', 'candidateSuite', CONTEXT);
      expect(selection.plotted.map((e) => e.key)).toEqual(['ok']);
      expect(selection.excluded.map((e) => e.key)).toEqual(['v9']);
      expect(selection.notices.join(' ')).toContain('not comparable');
    });
  });

  describe('scatter scales', () => {
    it('S1 defaults to mean model time, linear over a narrow range and lower is better', () => {
      const spec = buildQualitySpeedScatter(PROFILE_FIXTURE, BASE_FIGURE_OPTIONS);
      const x = scaleOf(spec.config, 'x');
      const y = scaleOf(spec.config, 'y');

      expect(x.type).toBe('linear');
      expect(titleLines(x)[0]).toBe('Mean time per question (s)');
      expect(x.title?.text).toContain('lower is better');
      expect(y.type).toBe('linear');
      // The fixture's intervals reach from 26 to 94, so the padded domain clamps to the full index.
      expect(y.min).toBe(0);
      expect(y.max).toBe(100);
      expect(titleLines(y)[0]).toBe('Intelligence Index (0-100)');
    });

    it('S1 puts TTFT on a logarithmic x axis and says so in the axis title, when it spans a factor of ten', () => {
      // P50 100-500 ms with P90 whiskers up to 1800 ms: the axis must show a range of eighteen.
      const spec = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, speedMeasure: 'ttftP50' });
      const x = scaleOf(spec.config, 'x');

      expect(x.type).toBe('logarithmic');
      expect(titleLines(x)[0]).toBe('Time to first token, median (s, log scale)');
      const ticks = ticksOf(x);
      expect(ticks.length).toBeGreaterThanOrEqual(3);
      expect(ticks.length).toBeLessThanOrEqual(7);
      expect(x.min!).toBeLessThan(100);
      expect(x.max!).toBeGreaterThan(1800);
    });

    it('S1 switches to Speed Index, linear 0-100, when that measure is selected', () => {
      const spec = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, speedMeasure: 'speedIndex' });
      const x = scaleOf(spec.config, 'x');

      expect(x.type).toBe('linear');
      expect(x.min).toBe(0);
      expect(x.max).toBe(100);
      expect(x.title?.text).toContain('higher is better');
      expect(pointsOf(spec.config)[0]['x']).toBe(PROFILE_FIXTURE[0].speedIndex!);
    });

    it('S2 keeps a cost range under a factor of ten linear, and quality linear', () => {
      const spec = buildQualityCostScatter(PROFILE_FIXTURE, BASE_FIGURE_OPTIONS);
      expect(scaleOf(spec.config, 'x').type).toBe('linear');
      expect(titleLines(scaleOf(spec.config, 'x'))[0]).toBe('Cost per question (USD)');
      expect(scaleOf(spec.config, 'y').type).toBe('linear');
    });

    it('S3 labels both axes in their units, on the selected speed measure', () => {
      const spec = buildSpeedCostScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, speedMeasure: 'totalModelTime' });
      expect(scaleOf(spec.config, 'x').type).toBe('linear');
      expect(scaleOf(spec.config, 'y').type).toBe('linear');
      expect(titleLines(scaleOf(spec.config, 'x'))[0]).toBe('Total time for the suite (s)');
      expect(titleLines(scaleOf(spec.config, 'y'))[0]).toBe('Cost per question (USD)');
    });

    it('keeps the axes ascending and states the preferred corner instead of reversing them', () => {
      const spec = buildQualitySpeedScatter(PROFILE_FIXTURE, BASE_FIGURE_OPTIONS);
      expect(spec.preferredCorner).toEqual({ x: 'left', y: 'top', label: 'Better' });
      expect(scaleOf(spec.config, 'x').title?.text).toContain('lower is better');
      expect(scaleOf(spec.config, 'y').title?.text).toContain('higher is better');
    });

    it('sizes the hit target well past 24 px and uses nearest-without-intersect', () => {
      const spec = buildQualitySpeedScatter(PROFILE_FIXTURE, BASE_FIGURE_OPTIONS);
      const dataset = datasetsOf(spec.config)[0];
      expect(((dataset['radius'] as number) + (dataset['hitRadius'] as number)) * 2).toBeGreaterThanOrEqual(24);

      const interaction = spec.config.options?.interaction;
      expect(interaction?.mode).toBe('nearest');
      expect(interaction?.intersect).toBeFalse();
    });

    it('draws single-run entries hollow and multi-run entries solid', () => {
      const entries = [makeEntry({ key: 'once', runCount: 1 }), makeEntry({ key: 'thrice', runCount: 3 })];
      const spec = buildQualitySpeedScatter(entries, {
        ...BASE_FIGURE_OPTIONS,
        glyphs: buildIdentityGlyphs(entries),
      });
      const datasets = datasetsOf(spec.config);
      expect(datasets[0]['backgroundColor']).toBe('transparent');
      expect(datasets[1]['backgroundColor']).toBe(CATEGORICAL_PALETTE_DARK[1]);
    });

    it('carries the quality interval and the one-sided latency whisker on every mark, on TTFT', () => {
      const spec = buildQualitySpeedScatter([PROFILE_FIXTURE[0]], {
        ...BASE_FIGURE_OPTIONS,
        glyphs: buildIdentityGlyphs([PROFILE_FIXTURE[0]]),
        speedMeasure: 'ttftP50',
      });
      const point = pointsOf(spec.config)[0];
      expect(point['yErrLow']).toBe(4);
      // P50 -> P90 only: latency is right-skewed, so the whisker never mirrors below the median.
      expect(point['xErrHigh']).toBe(1300);
      expect(point['xErrLow']).toBeUndefined();
    });

    it('draws no whisker for mean model time, the default measure', () => {
      const spec = buildQualitySpeedScatter([PROFILE_FIXTURE[0]], {
        ...BASE_FIGURE_OPTIONS,
        glyphs: buildIdentityGlyphs([PROFILE_FIXTURE[0]]),
      });
      const point = pointsOf(spec.config)[0];
      expect(point['x']).toBe(PROFILE_FIXTURE[0].modelTimeMeanMs);
      expect(point['xErrLow']).toBeUndefined();
      expect(point['xErrHigh']).toBeUndefined();
    });

    it('carries the run-to-run SD whisker for total model time', () => {
      const entry = makeEntry({ key: 'once', totalModelTimeMs: 40000, totalModelTimeSdMs: 5000 });
      const spec = buildQualitySpeedScatter([entry], {
        ...BASE_FIGURE_OPTIONS,
        glyphs: buildIdentityGlyphs([entry]),
        speedMeasure: 'totalModelTime',
      });
      const point = pointsOf(spec.config)[0];
      expect(point['x']).toBe(40000);
      expect(point['xErrLow']).toBe(5000);
      expect(point['xErrHigh']).toBe(5000);
    });

    it('leaves a single-run mark unannotated: the key and P1 are where the run count is stated', () => {
      const entry = makeEntry({ key: 'once', runCount: 1, candidateCostPerQuestionSdUsd: null });
      const spec = buildQualityCostScatter([entry], {
        ...BASE_FIGURE_OPTIONS,
        glyphs: buildIdentityGlyphs([entry]),
      });
      const point = pointsOf(spec.config)[0];
      expect(point['note']).toBeUndefined();
      expect(point['xErrLow']).toBeUndefined();
      expect(spec.chrome.key).toContain({ glyph: 'hollow', text: 'Single run' });
      expect(spec.chrome.key.map((item) => item.glyph)).not.toContain('solid');
    });

    it('draws the Pareto frontier, keys it once and keeps it out of the identity legend', () => {
      // Distinguishing x values, on TTFT: the fixture's model time is tied across all three entries.
      const spec = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, speedMeasure: 'ttftP50' });
      const labels = datasetsOf(spec.config).map((d) => d['label']);
      expect(labels).toContain('Pareto frontier');

      const filter = spec.config.options?.plugins?.legend?.labels?.filter;
      expect(filter).toBeDefined();
      expect(spec.chrome.key.map((item) => item.glyph)).toEqual(['solid', 'interval', 'frontier', 'dominated']);
      expect(spec.plugins).toContain(dominatedRegionPlugin);
      expect(spec.plugins.indexOf(dominatedRegionPlugin)).toBeLessThan(spec.plugins.indexOf(errorBarPlugin));
    });

    it('leaves identity text to the direct-label toggle, whatever the entry count', () => {
      const four = Array.from({ length: 4 }, (_, i) => makeEntry({ key: `m${i}` }));
      const many = buildQualitySpeedScatter(four, { ...BASE_FIGURE_OPTIONS, glyphs: buildIdentityGlyphs(four) });
      const few = buildQualitySpeedScatter(PROFILE_FIXTURE.slice(0, 2), BASE_FIGURE_OPTIONS);

      const display = (spec: typeof many): unknown => {
        const options = spec.config.options as unknown as {
          plugins?: { datalabels?: { display?: unknown } };
        };
        return options.plugins?.datalabels?.display;
      };
      expect(display(many)).toBeFalse();
      expect(display(few)).toBeFalse();
      expect(many.plugins).not.toContain(ChartDataLabels);
    });

    it('names both axes in the tooltip body and keeps the frontier out of it', () => {
      const spec = buildQualitySpeedScatter(PROFILE_FIXTURE, {
        ...BASE_FIGURE_OPTIONS,
        speedMeasure: 'ttftP50',
      });
      const tooltip = spec.config.options?.plugins?.tooltip as unknown as {
        filter?: (item: { datasetIndex: number }) => boolean;
        callbacks?: {
          title?: (items: { dataset: { label?: string } }[]) => string;
          label?: (item: { parsed: { x: number | null; y: number | null } }) => string | string[];
        };
      };

      expect(tooltip.callbacks?.title?.([{ dataset: { label: 'Model A' } }])).toBe('Model A');
      expect(tooltip.callbacks?.label?.({ parsed: { x: 2000, y: 80.1 } })).toEqual([
        'Time to first token, median: 2.00 s',
        'Intelligence Index: 80.1',
      ]);

      // Three models are plotted, so dataset index 3 is the frontier annotation.
      expect(tooltip.filter?.({ datasetIndex: 0 })).toBeTrue();
      expect(tooltip.filter?.({ datasetIndex: 3 })).toBeFalse();
    });
  });

  describe('P1 small multiples', () => {
    it('gives all three panels a zero baseline, because a truncated bar misstates its ratio', () => {
      const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions());
      for (const panel of [figure.quality, figure.speed, figure.cost]) {
        const value = scaleOf(panel.config, 'y');
        expect(value.beginAtZero).withContext(panel.id).toBeTrue();
        expect(value.min).withContext(panel.id).toBe(0);
        expect(value.type).withContext(panel.id).toBe('linear');
      }
    });

    it('shares one model order across all three panels', () => {
      const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions());
      expect(figure.quality.config.data.labels).toEqual(figure.speed.config.data.labels);
      expect(figure.speed.config.data.labels).toEqual(figure.cost.config.data.labels);
      expect(figure.order).toEqual(['A', 'B', 'C']);
    });

    it('reorders all three panels when the sort changes, never just one', () => {
      const byQuality = buildComparisonFigures(PROFILE_FIXTURE, {
        context: CONTEXT,
        sort: { key: 'intelligenceIndex', direction: 'desc' },
      }).smallMultiples;
      const byCost = buildComparisonFigures(PROFILE_FIXTURE, {
        context: CONTEXT,
        sort: { key: 'cost', direction: 'asc' },
      }).smallMultiples;

      expect(byQuality.order).toEqual(['A', 'B', 'C']);
      // Ascending cost is the cheapest first, which reverses the quality order in this fixture.
      expect(byCost.order).toEqual(['C', 'B', 'A']);

      for (const figure of [byQuality, byCost]) {
        expect(figure.quality.config.data.labels).toEqual(figure.speed.config.data.labels);
        expect(figure.quality.config.data.labels).toEqual(figure.cost.config.data.labels);
      }
      expect(byQuality.quality.config.data.labels).not.toEqual(byCost.quality.config.data.labels ?? []);
    });

    it('switches the speed panel across all four measures', () => {
      const index = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ speedMeasure: 'speedIndex' }));
      const ttft = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ speedMeasure: 'ttftP50' }));
      const mean = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ speedMeasure: 'meanModelTime' }));
      const total = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ speedMeasure: 'totalModelTime' }));

      expect(titleLines(scaleOf(index.speed.config, 'y'))[0]).toContain('Speed Index');
      expect(scaleOf(index.speed.config, 'y').max).toBe(100);
      expect(pointsOf(index.speed.config)[0]['y']).toBe(100);

      expect(titleLines(scaleOf(ttft.speed.config, 'y'))[0]).toContain('Time to first token');
      expect(scaleOf(ttft.speed.config, 'y').title?.text).toContain('lower is better');
      expect(scaleOf(ttft.speed.config, 'y').max).toBeUndefined();
      const ttftPoint = pointsOf(ttft.speed.config)[0];
      expect(ttftPoint['y']).toBe(500);
      expect(ttftPoint['yErrHigh']).toBe(1300);

      // 900 ms is the largest mean, so the panel stays in milliseconds.
      expect(scaleOf(mean.speed.config, 'y').title?.text).toEqual([
        'Mean time per question (ms)',
        'lower is better',
      ]);
      const meanPoint = pointsOf(mean.speed.config)[0];
      expect(meanPoint['y']).toBe(PROFILE_FIXTURE[0].modelTimeMeanMs);
      expect(meanPoint['yErrLow']).toBeUndefined();
      expect(meanPoint['yErrHigh']).toBeUndefined();
      expect(mean.speed.chrome.notes).toContain({
        text: 'Mean time per question has no uncertainty bar: the spread across questions is not recorded.',
        tone: 'info',
      });

      // 9000 ms is past a second, so the title and the ticks switch to seconds together.
      expect(scaleOf(total.speed.config, 'y').title?.text).toEqual([
        'Total time for the suite (s)',
        'lower is better',
      ]);
      const totalTick = scaleOf(total.speed.config, 'y').ticks?.callback;
      expect(totalTick?.(0, 0, [{ value: 0 }, { value: 2000 }])).toBe('0 s');
      expect(totalTick?.(2000, 1, [{ value: 0 }, { value: 2000 }])).toBe('2 s');
      const totalPoint = pointsOf(total.speed.config)[0];
      expect(totalPoint['y']).toBe(PROFILE_FIXTURE[0].totalModelTimeMs);
      expect(totalPoint['yErrLow']).toBe(PROFILE_FIXTURE[0].totalModelTimeSdMs!);
      expect(totalPoint['yErrHigh']).toBe(PROFILE_FIXTURE[0].totalModelTimeSdMs!);
    });

    it('switches the cost panel between the candidate suite cost and the total run cost', () => {
      const suite = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ costMeasure: 'candidateSuite' }));
      const total = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ costMeasure: 'totalRun' }));

      expect(titleLines(scaleOf(suite.cost.config, 'y'))[0]).toContain('whole suite');
      expect(pointsOf(suite.cost.config)[0]['y'] as number)
        .toBeCloseTo(suiteCostUsd(PROFILE_FIXTURE[0], CONTEXT), 10);
      expect(suiteCostUsd(PROFILE_FIXTURE[0], CONTEXT)).toBeCloseTo(0.3, 10);

      expect(titleLines(scaleOf(total.cost.config, 'y'))[0]).toContain('Total run cost');
      expect(pointsOf(total.cost.config)[0]['y']).toBe(0.4);
    });

    it('drops the cost interval and marks n = 1 on the category tick of all three panels', () => {
      const once = makeEntry({ key: 'once', runCount: 1, candidateCostPerQuestionSdUsd: null });
      const twice = makeEntry({ key: 'twice', runCount: 2 });
      const entries = [once, twice];
      const figure = buildSmallMultiples(entries, {
        ...smallMultiplesOptions(),
        glyphs: buildIdentityGlyphs(entries),
      });

      const point = pointsOf(figure.cost.config)[0];
      expect(point['note']).toBeUndefined();
      expect(point['yErrHigh']).toBeUndefined();

      // One label list across the three panels: a two-line tick on one alone would shrink its
      // plot area and put its bars out of line with the other two.
      for (const panel of [figure.quality, figure.speed, figure.cost]) {
        expect(panel.config.data.labels?.[0]).withContext(panel.id).toEqual([once.label, 'n = 1']);
        expect(panel.config.data.labels?.[1]).withContext(panel.id).toBe(twice.label);
      }
    });

    it('badges Speed Index saturation whenever any plotted entry sits at the ceiling', () => {
      const saturated = [makeEntry({ key: 'fast', speedIndex: 100, speedIndexSaturated: true })];
      const figure = buildSmallMultiples(saturated, {
        ...smallMultiplesOptions(),
        glyphs: buildIdentityGlyphs(saturated),
      });
      const saturation = figure.speed.chrome.notes.find((note) => note.text.includes('saturated'));
      expect(saturation?.tone).toBe('warning');
      expect(saturation?.text).toContain('TTFT P50');

      const clean = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions());
      expect(clean.speed.chrome.notes.map((note) => note.text).join(' ')).not.toContain('saturated');
    });

    it('never ramps a bar by value, and mutes the rest only when something is emphasised', () => {
      const plain = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions());
      const fills = datasetsOf(plain.quality.config)[0]['backgroundColor'] as string[];
      expect(new Set(fills).size).toBe(1);
      expect(fills[0]).toBe(CATEGORICAL_PALETTE_DARK[0]);

      const emphasised = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ highlightedKey: 'B' }));
      const emphasisedFills = datasetsOf(emphasised.quality.config)[0]['backgroundColor'] as string[];
      expect(emphasisedFills).toEqual([DE_EMPHASIS_FILL, ACCENT, DE_EMPHASIS_FILL]);
    });

    it('turns the bars horizontal below the stacking breakpoint', () => {
      expect(P1_STACK_BREAKPOINT_PX).toBe(720);
      const horizontal = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ orientation: 'horizontal' }));
      expect(horizontal.quality.config.options?.indexAxis).toBe('y');
      // The value axis moves with the bars, and it still starts at zero.
      expect(scaleOf(horizontal.quality.config, 'x').beginAtZero).toBeTrue();
      expect(scaleOf(horizontal.quality.config, 'x').min).toBe(0);
      expect(scaleOf(horizontal.quality.config, 'y').type).toBe('category');
    });

    it('carries no legend box, because each panel plots exactly one series', () => {
      const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions());
      expect(figure.quality.config.options?.plugins?.legend?.display).toBeFalse();
    });

    it('states n in every panel\'s badges, and the pricing basis on the cost panel only', () => {
      const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions());
      for (const panel of [figure.quality, figure.speed, figure.cost]) {
        const texts = panel.chrome.badges.map((badge) => badge.text);
        expect(texts.slice(0, 3)).withContext(panel.id).toEqual(['3 models', '3 runs each', '10 questions']);
        expect(panel.chrome.title).withContext(panel.id).toBe(panel.title);
        expect(panel.chrome.key).withContext(panel.id).toEqual([]);
        expect(panel.chrome.highlight).withContext(panel.id).toBe('');
      }
      expect(figure.cost.chrome.badges.length).toBe(4);
      expect(figure.cost.chrome.badges[3].tone).toBe('pricing');
      expect(figure.cost.chrome.badges[3].text.startsWith('Catalog prices')).toBeTrue();
      expect(figure.cost.chrome.detail).not.toBe('');
      expect(figure.quality.chrome.detail).toBe('');
      expect(figure.speed.chrome.detail).toBe('');
    });

    it('gives every panel scriptable value labels, past the SD whisker with a grace margin', () => {
      const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ costMeasure: 'candidateSuite' }));

      for (const panel of [figure.quality, figure.speed, figure.cost]) {
        expect(panel.plugins).withContext(panel.id).toContain(ChartDataLabels);
        const datalabels = panel.config.options?.plugins?.datalabels as {
          display?: (ctx: { dataIndex: number }) => boolean;
        };
        expect(typeof datalabels.display).withContext(panel.id).toBe('function');
        expect(datalabels.display?.({ dataIndex: 0 })).withContext(panel.id).toBeTrue();
      }

      // Grace is the headroom the label needs, and it is ignored once an axis max is explicit —
      // which the intelligence panel sets at 100, and the speed panel at 100 on Speed Index only.
      // Where the max is explicit `clamp` keeps the label inside the plot area instead.
      expect(scaleOf(figure.cost.config, 'y').grace).toBe('12%');
      expect(scaleOf(figure.quality.config, 'y').grace).toBeUndefined();
      expect(scaleOf(figure.speed.config, 'y').grace).toBeUndefined();

      const timed = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ speedMeasure: 'meanModelTime' }));
      expect(scaleOf(timed.speed.config, 'y').grace).toBe('12%');
      // Grace extends both ends of the scale; an explicit min keeps the zero baseline a bar requires.
      expect(scaleOf(figure.cost.config, 'y').min).toBe(0);
    });

    // `buildPanel` takes a nullable value array and its label guard is written for it; the cost
    // DTO types the amount as a number, so the unmeasured entry is forced past the type here.
    it('does not label a cost bar for an entry the panel carries no cost value for', () => {
      const unmeasured = makeEntry({ key: 'no-cost', totalRunCostUsd: null as unknown as number });
      const entries = [PROFILE_FIXTURE[0], unmeasured];
      const figure = buildSmallMultiples(entries, {
        ...smallMultiplesOptions({ costMeasure: 'totalRun' }),
        glyphs: buildIdentityGlyphs(entries),
      });
      const costDatalabels = figure.cost.config.options?.plugins?.datalabels as {
        display?: (ctx: { dataIndex: number }) => boolean;
      };

      expect(costDatalabels.display?.({ dataIndex: 0 })).toBeTrue();
      expect(costDatalabels.display?.({ dataIndex: 1 })).toBeFalse();
    });
  });

  describe('P2 profile normalization', () => {
    it('keeps the axis order fixed at Quality, Speed, Cost', () => {
      expect(PROFILE_AXIS_ORDER).toEqual(['quality', 'speed', 'cost']);
      const normalization = normalizeProfile(PROFILE_FIXTURE, {
        context: CONTEXT,
        speedMeasure: 'speedIndex',
        costMeasure: 'candidateSuite',
      });
      expect(normalization.axes.map((a) => a.id)).toEqual(['quality', 'speed', 'cost']);
    });

    it('normalizes each axis min-max and inverts cost so that up is better everywhere', () => {
      const normalization = normalizeProfile(PROFILE_FIXTURE, {
        context: CONTEXT,
        speedMeasure: 'speedIndex',
        costMeasure: 'candidateSuite',
      });

      // Quality 30..90, Speed Index 0..100, suite cost $0.10..$0.30 with the cheapest at the top.
      const rows = new Map(normalization.rows.map((r) => [r.key, r.values]));
      expectClose(rows.get('A') ?? [], [1, 1, 0]);
      expectClose(rows.get('B') ?? [], [0.5, 0.5, 0.5]);
      expectClose(rows.get('C') ?? [], [0, 0, 1]);

      const cost = normalization.axes[2];
      expect(cost.lowerIsBetter).toBeTrue();
      expect(cost.min).toBeCloseTo(0.1, 10);
      expect(cost.max).toBeCloseTo(0.3, 10);
      expect(cost.minLabel).toBe('$0.1000');
      expect(cost.maxLabel).toBe('$0.3000');
    });

    it('inverts the speed axis too when the measure is a latency', () => {
      const normalization = normalizeProfile(PROFILE_FIXTURE, {
        context: CONTEXT,
        speedMeasure: 'ttftP50',
        costMeasure: 'candidateSuite',
      });
      const rows = new Map(normalization.rows.map((r) => [r.key, r.values]));
      // A is the slowest at 500 ms and lands at the bottom of the axis despite the best quality.
      expectClose(rows.get('A') ?? [], [1, 0, 0]);
      expectClose(rows.get('C') ?? [], [0, 1, 1]);
      expect(normalization.axes[1].lowerIsBetter).toBeTrue();
    });

    it('places every model mid-axis when an axis collapses, rather than at a misleading end', () => {
      const flat = [makeEntry({ key: 'x' }), makeEntry({ key: 'y' })];
      const normalization = normalizeProfile(flat, {
        context: CONTEXT,
        speedMeasure: 'speedIndex',
        costMeasure: 'candidateSuite',
      });
      expectClose(normalization.rows[0].values, [0.5, 0.5, 0.5]);
    });

    it('is a shape view: normalized 0-1 axis, no error bars and an info note that says so', () => {
      const spec = buildProfilePlot(PROFILE_FIXTURE, {
        ...BASE_FIGURE_OPTIONS,
        speedMeasure: 'speedIndex',
        costMeasure: 'candidateSuite',
      });
      const y = scaleOf(spec.config, 'y');
      expect(y.min).toBe(0);
      expect(y.max).toBe(1);
      expect(spec.plugins).toEqual([]);
      const shapeNote = spec.chrome.notes.find((note) => note.text.includes('compare shapes and crossings, not values'));
      expect(shapeNote?.tone).toBe('info');
      expect(spec.chrome.notes.map((note) => note.text).join(' ')).not.toContain('error bars');
      expect(spec.chrome.badges.some((badge) => badge.tone === 'pricing')).toBeTrue();
      expect(spec.config.data.labels).toEqual(['Intelligence', 'Speed Index', 'Cost (candidate, suite)']);
    });

    it('mutes every polyline and lifts only the emphasised one', () => {
      const muted = buildProfilePlot(PROFILE_FIXTURE, {
        ...BASE_FIGURE_OPTIONS,
        speedMeasure: 'speedIndex',
        costMeasure: 'candidateSuite',
      });
      expect(new Set(datasetsOf(muted.config).map((d) => d['borderColor'] as string)).size).toBe(1);

      const emphasised = buildProfilePlot(PROFILE_FIXTURE, {
        ...BASE_FIGURE_OPTIONS,
        speedMeasure: 'speedIndex',
        costMeasure: 'candidateSuite',
        selectedKeys: ['A', 'B'],
      });
      // Two selected models are within the three-hue ceiling, so they take their identity hues.
      expect(datasetsOf(emphasised.config)[0]['borderColor']).toBe(glyphFor(BASE_FIGURE_OPTIONS.glyphs, 'A').hue);
      expect(datasetsOf(emphasised.config)[2]['borderWidth']).toBe(2);
    });

    it('keeps the identity shape on the polyline markers', () => {
      const spec = buildProfilePlot(PROFILE_FIXTURE, {
        ...BASE_FIGURE_OPTIONS,
        speedMeasure: 'speedIndex',
        costMeasure: 'candidateSuite',
      });
      const shapes = datasetsOf(spec.config).map((d) => d['pointStyle']);
      expect(shapes).toEqual(PROFILE_FIXTURE.map((e) => glyphFor(BASE_FIGURE_OPTIONS.glyphs, e.key).shape));
    });
  });

  describe('the Pareto frontier', () => {
    it('drops dominated models and steps only where a model actually reached', () => {
      const result = computeParetoFrontier(
        [
          { key: 'A', x: 500, y: 90 },
          { key: 'B', x: 300, y: 60 },
          { key: 'C', x: 100, y: 30 },
          { key: 'D', x: 400, y: 50 },
        ],
        'lower',
        'higher',
      );

      // D is beaten by B on both axes: faster and better.
      expect(result.frontier.map((c) => c.key)).toEqual(['A', 'B', 'C']);
      expect(result.steps).toEqual([
        { x: 500, y: 90 },
        { x: 500, y: 60 },
        { x: 300, y: 60 },
        { x: 300, y: 30 },
        { x: 100, y: 30 },
      ]);
    });

    it('handles a both-lower-is-better pair of axes', () => {
      const result = computeParetoFrontier(
        [
          { key: 'P', x: 100, y: 0.03 },
          { key: 'Q', x: 300, y: 0.01 },
          { key: 'R', x: 500, y: 0.05 },
        ],
        'lower',
        'lower',
      );
      expect(result.frontier.map((c) => c.key)).toEqual(['Q', 'P']);
      expect(result.steps).toEqual([
        { x: 300, y: 0.01 },
        { x: 300, y: 0.03 },
        { x: 100, y: 0.03 },
      ]);
    });

    it('keeps tied models, since neither dominates the other', () => {
      const result = computeParetoFrontier(
        [
          { key: 'A', x: 100, y: 50 },
          { key: 'B', x: 100, y: 50 },
        ],
        'lower',
        'higher',
      );
      expect(result.frontier.map((c) => c.key)).toEqual(['A', 'B']);
      expect(result.steps).toEqual([{ x: 100, y: 50 }]);
    });

    it('returns nothing to draw for an empty set', () => {
      const result = computeParetoFrontier([], 'lower', 'higher', { xWorst: 600, yWorst: 0 });
      expect(result.frontier).toEqual([]);
      expect(result.steps).toEqual([]);
      expect(result.boundary).toEqual([]);
    });

    it('extends the staircase from the worst-x edge to the worst-y edge when given the bounds', () => {
      const candidates = [
        { key: 'A', x: 500, y: 90 },
        { key: 'B', x: 300, y: 60 },
        { key: 'C', x: 100, y: 30 },
        { key: 'D', x: 400, y: 50 },
      ];
      expect(computeParetoFrontier(candidates, 'lower', 'higher').boundary).toEqual([]);

      const result = computeParetoFrontier(candidates, 'lower', 'higher', { xWorst: 600, yWorst: 0 });
      expect(result.boundary).toEqual([
        { x: 600, y: 90 },
        { x: 500, y: 90 },
        { x: 500, y: 60 },
        { x: 300, y: 60 },
        { x: 300, y: 30 },
        { x: 100, y: 30 },
        { x: 100, y: 0 },
      ]);
    });

    it('draws a one-member frontier as an L through that model', () => {
      const result = computeParetoFrontier(
        [
          { key: 'A', x: 100, y: 50 },
          { key: 'B', x: 200, y: 40 },
        ],
        'lower',
        'higher',
        { xWorst: 250, yWorst: 0 },
      );
      expect(result.frontier.map((c) => c.key)).toEqual(['A']);
      expect(result.steps).toEqual([{ x: 100, y: 50 }]);
      expect(result.boundary).toEqual([
        { x: 250, y: 50 },
        { x: 100, y: 50 },
        { x: 100, y: 0 },
      ]);
    });

    it('orients the boundary on a both-lower-is-better pair of axes', () => {
      const result = computeParetoFrontier(
        [
          { key: 'P', x: 100, y: 0.03 },
          { key: 'Q', x: 300, y: 0.01 },
          { key: 'R', x: 500, y: 0.05 },
        ],
        'lower',
        'lower',
        { xWorst: 600, yWorst: 0.06 },
      );
      expect(result.boundary).toEqual([
        { x: 600, y: 0.01 },
        { x: 300, y: 0.01 },
        { x: 300, y: 0.03 },
        { x: 100, y: 0.03 },
        { x: 100, y: 0.06 },
      ]);
    });
  });

  describe('the dominated-region plugin', () => {
    function runRegionPlugin(datasets: { label?: string }[], vertices: { x: number; y: number }[]) {
      const calls: { op: string; args: unknown[] }[] = [];
      const record = (op: string) => (...args: unknown[]) => calls.push({ op, args });
      const ctx = {
        save: record('save'),
        restore: record('restore'),
        beginPath: record('beginPath'),
        rect: record('rect'),
        clip: record('clip'),
        moveTo: record('moveTo'),
        lineTo: record('lineTo'),
        closePath: record('closePath'),
        fill: () => calls.push({ op: 'fill', args: [ctx.fillStyle] }),
        fillStyle: '',
      };
      const chart = {
        ctx,
        chartArea: { left: 0, top: 0, right: 400, bottom: 300 },
        data: { datasets },
        getDatasetMeta: () => ({ hidden: false, data: vertices }),
      };
      dominatedRegionPlugin.beforeDatasetsDraw?.(chart as unknown as Chart, {} as never, {} as never);
      return calls;
    }

    it('fills the boundary closed through the worst corner, clipped to the plot area', () => {
      const calls = runRegionPlugin(
        [{ label: 'A' }, { label: 'Pareto frontier' }],
        [{ x: 400, y: 50 }, { x: 100, y: 50 }, { x: 100, y: 300 }],
      );
      expect(calls.some((c) => c.op === 'clip')).toBeTrue();
      const lines = calls.filter((c) => c.op === 'lineTo').map((c) => c.args);
      // The corner is the first vertex's x and the last vertex's y.
      expect(lines[lines.length - 1]).toEqual([400, 300]);
      expect(calls.find((c) => c.op === 'fill')?.args).toEqual([DOMINATED_REGION_FILL]);
    });

    it('draws nothing on a figure without a frontier', () => {
      const calls = runRegionPlugin([{ label: 'A' }], [{ x: 100, y: 50 }]);
      expect(calls.length).toBe(0);
    });
  });

  describe('axis domains and chrome on the scatters', () => {
    /** The two models of the reported screenshot: GPT-5.6 Luna beats GPT-6 Luna on both S1 axes. */
    const SCREENSHOT: ModelComparisonEntry[] = [
      makeEntry({
        key: 'gpt-5.6-luna',
        label: 'GPT-5.6 Luna',
        runCount: 1,
        modelTimeMeanMs: 21250,
        intelligenceIndex: 74.8,
        intelligenceIndexCi95HalfWidth: 8,
        candidateCostPerQuestionUsd: 0.0042,
        candidateCostPerQuestionSdUsd: null,
        totalModelTimeSdMs: null,
        totalRunCostSdUsd: null,
      }),
      makeEntry({
        key: 'gpt-6-luna',
        label: 'GPT-6 Luna',
        runCount: 1,
        modelTimeMeanMs: 29990,
        intelligenceIndex: 74.6,
        intelligenceIndexCi95HalfWidth: 8,
        candidateCostPerQuestionUsd: 0.0024,
        candidateCostPerQuestionSdUsd: null,
        totalModelTimeSdMs: null,
        totalRunCostSdUsd: null,
      }),
    ];
    const SCREENSHOT_CONTEXT: ModelComparisonContext = { ...CONTEXT, itemsPerRun: 18 };
    const SCREENSHOT_OPTIONS = {
      context: SCREENSHOT_CONTEXT,
      glyphs: buildIdentityGlyphs(SCREENSHOT),
      reducedMotion: false,
    };

    it('gives S1 a linear speed axis in seconds, with a handful of ticks inside the domain', () => {
      const spec = buildQualitySpeedScatter(SCREENSHOT, SCREENSHOT_OPTIONS);
      const x = scaleOf(spec.config, 'x');

      expect(x.type).toBe('linear');
      expect(titleLines(x)[0]).toMatch(/\(s\)$/);
      expect(titleLines(x)[0]).not.toContain('log');

      const ticks = ticksOf(x);
      expect(ticks.length).toBeGreaterThanOrEqual(3);
      expect(ticks.length).toBeLessThanOrEqual(7);
      for (const tick of ticks) {
        expect(tick).toBeGreaterThanOrEqual(x.min!);
        expect(tick).toBeLessThanOrEqual(x.max!);
      }
    });

    it('keeps both screenshot models at least 5 % of the span inside every edge', () => {
      const spec = buildQualitySpeedScatter(SCREENSHOT, SCREENSHOT_OPTIONS);
      for (const axis of ['x', 'y'] as const) {
        const scale = scaleOf(spec.config, axis);
        const span = scale.max! - scale.min!;
        for (const entry of SCREENSHOT) {
          const value = axis === 'x' ? entry.modelTimeMeanMs : entry.intelligenceIndex;
          expect((value - scale.min!) / span).withContext(`${entry.label} on ${axis}`).toBeGreaterThanOrEqual(0.05);
          expect((scale.max! - value) / span).withContext(`${entry.label} on ${axis}`).toBeGreaterThanOrEqual(0.05);
        }
      }
    });

    it('draws S1\'s one-member frontier as an L, names it, and flags the overlapping intervals', () => {
      const spec = buildQualitySpeedScatter(SCREENSHOT, SCREENSHOT_OPTIONS);
      const frontier = datasetsOf(spec.config).find((d) => d['label'] === 'Pareto frontier');

      expect(frontier).toBeDefined();
      expect((frontier!['data'] as unknown[]).length).toBe(3);
      expect(spec.chrome.highlight).toBe('Best trade-off: GPT-5.6 Luna');
      expect(spec.chrome.notes).toContain({
        text: 'Some differences are within the 95 % intervals, so treat the frontier as indicative rather than a clear win.',
        tone: 'info',
      });
      expect(spec.chrome.key.map((item) => item.glyph)).toEqual(['hollow', 'interval', 'frontier', 'dominated']);
    });

    it('orders the scatter badges and carries the direction apart from them', () => {
      const s1 = buildQualitySpeedScatter(SCREENSHOT, SCREENSHOT_OPTIONS);
      expect(s1.chrome.badges.map((badge) => badge.text)).toEqual([
        '2 models',
        '1 run each',
        '18 questions',
      ]);
      expect(s1.chrome.direction).toEqual({ x: 'left', y: 'top', label: 'Better' });
      expect(s1.chrome.title).toBe(s1.title);

      const s2 = buildQualityCostScatter(SCREENSHOT, SCREENSHOT_OPTIONS);
      expect(s2.chrome.direction).toEqual({ x: 'left', y: 'top', label: 'Better' });
      expect(s2.chrome.badges[s2.chrome.badges.length - 1].tone).toBe('pricing');

      const s3 = buildSpeedCostScatter(SCREENSHOT, SCREENSHOT_OPTIONS);
      expect(s3.chrome.direction).toEqual({ x: 'left', y: 'bottom', label: 'Better' });
    });

    it('puts the direction on the scatters only, and never in a badge', () => {
      const figures = buildComparisonFigures(SCREENSHOT, { context: SCREENSHOT_CONTEXT });
      const { quality, speed, cost } = figures.smallMultiples;
      const undirected = [quality, speed, cost, figures.profile];
      const scatters = [figures.qualitySpeed, figures.qualityCost, figures.speedCost];
      for (const figure of [...undirected, ...scatters]) {
        expect(figure.chrome.badges.some((badge) => badge.text.includes('Better'))).withContext(figure.id).toBeFalse();
      }
      for (const figure of undirected) {
        expect(figure.chrome.direction).withContext(figure.id).toBeUndefined();
      }
      for (const figure of scatters) {
        expect(figure.chrome.direction).withContext(figure.id).toBeDefined();
      }
    });

    it('puts the pricing badge on S2, S3, P1 cost and P2 only', () => {
      const figures = buildComparisonFigures(SCREENSHOT, { context: SCREENSHOT_CONTEXT });
      const priced = (spec: { chrome: { badges: readonly { tone: string }[] } }): boolean =>
        spec.chrome.badges.some((badge) => badge.tone === 'pricing');

      expect(priced(figures.qualitySpeed)).toBeFalse();
      expect(priced(figures.qualityCost)).toBeTrue();
      expect(priced(figures.speedCost)).toBeTrue();
      expect(priced(figures.smallMultiples.quality)).toBeFalse();
      expect(priced(figures.smallMultiples.speed)).toBeFalse();
      expect(priced(figures.smallMultiples.cost)).toBeTrue();
      expect(priced(figures.profile)).toBeTrue();

      const badge = figures.qualityCost.chrome.badges.find((b) => b.tone === 'pricing');
      expect(badge?.text.startsWith('Catalog prices')).toBeTrue();
      expect(figures.qualitySpeed.chrome.detail).toBe('');
      expect(figures.qualityCost.chrome.detail).not.toBe('');
    });

    it('gives S1 and S2 an identical Intelligence Index domain', () => {
      const s1 = scaleOf(buildQualitySpeedScatter(SCREENSHOT, SCREENSHOT_OPTIONS).config, 'y');
      const s2 = scaleOf(buildQualityCostScatter(SCREENSHOT, SCREENSHOT_OPTIONS).config, 'y');

      expect(s1.min).toBe(s2.min);
      expect(s1.max).toBe(s2.max);
      expect(ticksOf(s1)).toEqual(ticksOf(s2));
      expect(s1.min).toBe(60);
      expect(s1.max).toBe(90);
    });

    it('turns a 7 s to 200 s speed axis logarithmic and says so', () => {
      const wide = [
        makeEntry({ key: 'fast', modelTimeMeanMs: 7000 }),
        makeEntry({ key: 'slow', modelTimeMeanMs: 200000 }),
      ];
      const spec = buildQualitySpeedScatter(wide, { ...BASE_FIGURE_OPTIONS, glyphs: buildIdentityGlyphs(wide) });
      const x = scaleOf(spec.config, 'x');

      expect(x.type).toBe('logarithmic');
      expect(titleLines(x)[0]).toBe('Mean time per question (s, log scale)');
      const ticks = ticksOf(x);
      expect(ticks.length).toBeGreaterThanOrEqual(3);
      expect(ticks.length).toBeLessThanOrEqual(7);
    });

    it('writes the P1 speed panel\'s title and ticks in the same unit', () => {
      const figure = buildSmallMultiples(SCREENSHOT, {
        ...smallMultiplesOptions({ speedMeasure: 'meanModelTime' }),
        context: SCREENSHOT_CONTEXT,
        glyphs: buildIdentityGlyphs(SCREENSHOT),
      });
      const y = scaleOf(figure.speed.config, 'y');
      expect(titleLines(y)[0]).toBe('Mean time per question (s)');
      expect(y.ticks?.callback?.(0, 0, [{ value: 0 }, { value: 5000 }])).toBe('0 s');
      expect(y.ticks?.callback?.(25000, 5, [{ value: 0 }, { value: 5000 }])).toBe('25 s');
    });

    it('sets a chrome on every figure, titled as the figure, and never mentions the DTO', () => {
      for (const set of [
        buildComparisonFigures(SCREENSHOT, { context: SCREENSHOT_CONTEXT }),
        buildComparisonFigures(PROFILE_FIXTURE, { context: CONTEXT, speedMeasure: 'speedIndex' }),
      ]) {
        for (const spec of allSpecs(set)) {
          expect(spec.chrome.title).withContext(spec.id).toBe(spec.title);
          for (const note of spec.chrome.notes) {
            expect(note.text).withContext(spec.id).not.toContain('DTO');
          }
        }
      }
    });
  });

  describe('reduced motion', () => {
    it('turns animation off outright rather than merely shortening it', () => {
      const still = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, reducedMotion: true });
      expect(still.config.options?.animation).toBeFalse();

      const moving = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, reducedMotion: false });
      expect(moving.config.options?.animation).toEqual({ duration: 300 });
    });

    it('applies to every figure, not only the scatters', () => {
      const figures = buildComparisonFigures(PROFILE_FIXTURE, { context: CONTEXT, reducedMotion: true });
      expect(figures.smallMultiples.quality.config.options?.animation).toBeFalse();
      expect(figures.smallMultiples.speed.config.options?.animation).toBeFalse();
      expect(figures.smallMultiples.cost.config.options?.animation).toBeFalse();
      expect(figures.profile.config.options?.animation).toBeFalse();
    });

    it('subscribes to the media query instead of sampling it once', () => {
      const listeners: ((event: MediaQueryListEvent) => void)[] = [];
      const query = {
        matches: false,
        addEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => {
          listeners.push(listener);
        },
        removeEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => {
          const index = listeners.indexOf(listener);
          if (index >= 0) {
            listeners.splice(index, 1);
          }
        },
      };

      const watcher = new ReducedMotionWatcher(() => query as unknown as MediaQueryList);
      const seen: boolean[] = [];
      const unsubscribe = watcher.subscribe((reduced) => seen.push(reduced));

      expect(watcher.matches).toBeFalse();
      expect(listeners.length).toBe(1);

      query.matches = true;
      listeners[0]({ matches: true } as MediaQueryListEvent);
      expect(seen).toEqual([true]);
      expect(watcher.matches).toBeTrue();

      unsubscribe();
      listeners[0]({ matches: false } as MediaQueryListEvent);
      expect(seen).toEqual([true]);

      watcher.dispose();
      expect(listeners.length).toBe(0);
    });

    it('reports no preference when the environment has no matchMedia', () => {
      const watcher = new ReducedMotionWatcher(() => null);
      expect(watcher.matches).toBeFalse();
      watcher.dispose();
    });
  });

  describe('direct labels on a scatter', () => {
    const AREA = { left: 0, top: 0, right: 400, bottom: 300 };

    function anchor(key: string, x: number, y: number, width = 60, height = 16): DirectLabelAnchor {
      return { key, x, y, width, height };
    }

    function rectsOverlap(a: DirectLabelBox, b: DirectLabelBox): boolean {
      return a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height;
    }

    /** A placed box as the edge-shaped rect the obstacle and segment helpers work in. */
    function boxEdges(box: DirectLabelBox) {
      return { left: box.x, top: box.y, right: box.x + box.width, bottom: box.y + box.height };
    }

    function boxOverlapsRect(
      box: DirectLabelBox,
      rect: { left: number; top: number; right: number; bottom: number },
    ): boolean {
      const edges = boxEdges(box);
      return (
        edges.left < rect.right && rect.left < edges.right && edges.top < rect.bottom && rect.top < edges.bottom
      );
    }

    it('places every label inside the plot area and never two on top of each other', () => {
      const anchors = [
        anchor('A', 100, 100),
        anchor('B', 120, 110),
        anchor('C', 140, 100),
        anchor('D', 110, 130),
      ];
      const boxes = placeDirectLabels(anchors, AREA);

      expect(boxes.length).toBe(anchors.length);
      for (const box of boxes) {
        expect(box.x).withContext(box.key).toBeGreaterThanOrEqual(AREA.left);
        expect(box.y).withContext(box.key).toBeGreaterThanOrEqual(AREA.top);
        expect(box.x + box.width).withContext(box.key).toBeLessThanOrEqual(AREA.right);
        expect(box.y + box.height).withContext(box.key).toBeLessThanOrEqual(AREA.bottom);
      }
      for (let i = 0; i < boxes.length; i += 1) {
        for (let j = i + 1; j < boxes.length; j += 1) {
          expect(rectsOverlap(boxes[i], boxes[j]))
            .withContext(`${boxes[i].key} over ${boxes[j].key}`)
            .toBeFalse();
        }
      }
    });

    it('never lays a label over another mark, so identity is never hidden by identity', () => {
      const anchors = [anchor('A', 100, 100), anchor('B', 175, 100), anchor('C', 100, 140)];
      const boxes = placeDirectLabels(anchors, AREA, 9);

      for (const box of boxes) {
        for (const mark of anchors) {
          const nearestX = Math.min(Math.max(mark.x, box.x), box.x + box.width);
          const nearestY = Math.min(Math.max(mark.y, box.y), box.y + box.height);
          const distance = Math.hypot(mark.x - nearestX, mark.y - nearestY);
          expect(distance).withContext(`${box.key} over mark ${mark.key}`).toBeGreaterThanOrEqual(9);
        }
      }
    });

    it('keeps every label with its own mark and places them in a deterministic order', () => {
      const anchors = [anchor('A', 140, 100), anchor('B', 100, 100), anchor('C', 120, 160)];
      const first = placeDirectLabels(anchors, AREA);
      const second = placeDirectLabels([...anchors].reverse(), AREA);

      expect(first.map((b) => b.key)).toEqual(['B', 'C', 'A']);
      expect(second).toEqual(first);
      for (const box of first) {
        const mark = anchors.find((a) => a.key === box.key)!;
        expect(box.anchorX).toBe(mark.x);
        expect(box.anchorY).toBe(mark.y);
      }
    });

    it('falls back to a clamped box rather than dropping a label it cannot place cleanly', () => {
      // A plot area barely wider than one label, with two marks in it: no ring can separate them.
      const tight = { left: 0, top: 0, right: 70, bottom: 40 };
      const boxes = placeDirectLabels([anchor('A', 35, 20), anchor('B', 36, 21)], tight);

      expect(boxes.length).toBe(2);
      for (const box of boxes) {
        expect(box.x).withContext(box.key).toBeGreaterThanOrEqual(tight.left);
        expect(box.y).withContext(box.key).toBeGreaterThanOrEqual(tight.top);
        expect(box.x + box.width).withContext(box.key).toBeLessThanOrEqual(tight.right);
        expect(box.y + box.height).withContext(box.key).toBeLessThanOrEqual(tight.bottom);
      }
    });

    it('moves a label off a supplied whisker rect when a clear candidate exists', () => {
      const anchors = [anchor('A', 100, 100)];
      const whisker = { left: 110, top: 88, right: 200, bottom: 112 };

      const unobstructed = placeDirectLabels(anchors, AREA, 9);
      const avoiding = placeDirectLabels(anchors, AREA, 9, { rects: [whisker] });

      // With nothing in the way the placer takes the right-hand candidate, which lies on the whisker.
      expect(unobstructed[0].x).toBeGreaterThan(100);
      expect(boxOverlapsRect(unobstructed[0], whisker)).toBeTrue();

      expect(boxOverlapsRect(avoiding[0], whisker)).toBeFalse();
      expect(avoiding[0].x + avoiding[0].width).toBeLessThanOrEqual(100);
    });

    it('does not lay a label across a supplied frontier polyline when a clear candidate exists', () => {
      const frontier = [{ x: 120, y: 80 }, { x: 120, y: 200 }];
      const boxes = placeDirectLabels([anchor('A', 100, 100)], AREA, 9, { polylines: [frontier] });

      expect(boxes.length).toBe(1);
      expect(segmentIntersectsRect(frontier[0], frontier[1], boxEdges(boxes[0]))).toBeFalse();
    });

    it('places the label of a mark 10 px from the right edge to its left', () => {
      const boxes = placeDirectLabels([anchor('A', AREA.right - 10, 150)], AREA, 9);

      expect(boxes.length).toBe(1);
      expect(boxes[0].x + boxes[0].width).toBeLessThanOrEqual(AREA.right - 10);
    });

    describe('segment geometry', () => {
      it('separates crossing, collinear-overlapping and disjoint segments', () => {
        expect(segmentsIntersect({ x: 0, y: 0 }, { x: 10, y: 10 }, { x: 0, y: 10 }, { x: 10, y: 0 })).toBeTrue();
        expect(segmentsIntersect({ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 5, y: 0 }, { x: 15, y: 0 })).toBeTrue();
        expect(segmentsIntersect({ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 0, y: 5 }, { x: 10, y: 5 })).toBeFalse();
        expect(segmentsIntersect({ x: 0, y: 0 }, { x: 1, y: 1 }, { x: 5, y: 5 }, { x: 6, y: 6 })).toBeFalse();
      });

      it('treats a rect as filled, so an endpoint inside it counts as much as an edge crossed', () => {
        const rect = { left: 0, top: 0, right: 10, bottom: 10 };

        expect(segmentIntersectsRect({ x: -5, y: 5 }, { x: 15, y: 5 }, rect)).toBeTrue();
        expect(segmentIntersectsRect({ x: 2, y: 2 }, { x: 3, y: 3 }, rect)).toBeTrue();
        expect(segmentIntersectsRect({ x: -5, y: -5 }, { x: 15, y: -5 }, rect)).toBeFalse();
        expect(segmentIntersectsRect({ x: 20, y: 20 }, { x: 30, y: 30 }, rect)).toBeFalse();
      });
    });

    interface DirectCall {
      readonly op: string;
      readonly args: readonly unknown[];
    }

    function block(name: string | undefined, values: DirectLabelValue[] = [], hue = '#3987e5'): DirectLabelBlock {
      return { name, values, hue };
    }

    function runDirectPlugin(
      blocks: readonly DirectLabelBlock[],
      marks: readonly { x: number; y: number }[],
    ): { calls: DirectCall[]; fills: string[]; rectFills: string[] } {
      const calls: DirectCall[] = [];
      const fills: string[] = [];
      const rectFills: string[] = [];
      const record = (op: string) => (...args: unknown[]) => calls.push({ op, args });
      const ctx = {
        save: record('save'),
        restore: record('restore'),
        beginPath: record('beginPath'),
        moveTo: record('moveTo'),
        lineTo: record('lineTo'),
        stroke: record('stroke'),
        fillRect: (...args: unknown[]) => {
          rectFills.push(ctx.fillStyle);
          calls.push({ op: 'fillRect', args });
        },
        fillText: (...args: unknown[]) => {
          fills.push(ctx.fillStyle);
          calls.push({ op: 'fillText', args });
        },
        measureText: (text: string) => ({ width: text.length * 6 }),
        strokeStyle: '',
        fillStyle: '',
        globalAlpha: 1,
        lineWidth: 0,
        font: '',
        textBaseline: '',
        textAlign: '',
      };
      const chart = {
        ctx,
        chartArea: { left: 0, top: 0, right: 400, bottom: 300 },
        // One dataset per mark, plus a frontier dataset the plugin has no label for.
        data: { datasets: marks.map(() => ({ data: [] })).concat([{ data: [] }]) },
        getDatasetMeta: (index: number) => ({
          hidden: false,
          data: marks[index] ? [marks[index]] : [],
        }),
      };
      directLabelPlugin.afterDatasetsDraw?.(
        chart as unknown as Chart,
        {} as never,
        { blocks, highlightedIndex: 1 } as never,
        false as never,
      );
      return { calls, fills, rectFills };
    }

    it('writes one label per model dataset and none for the frontier', () => {
      const { calls } = runDirectPlugin(
        [block('Model A'), block('Model B')],
        [{ x: 100, y: 100 }, { x: 200, y: 180 }],
      );
      const written = calls.filter((c) => c.op === 'fillText').map((c) => c.args[0]);

      expect(written.length).toBe(2);
      expect(written).toContain('Model A');
      expect(written).toContain('Model B');
      // A leader per label, each a moveTo and a lineTo of its own.
      expect(calls.filter((c) => c.op === 'moveTo').length).toBe(2);
    });

    it('gives the highlighted model the accent its mark already wears', () => {
      const { fills } = runDirectPlugin(
        [block('Model A'), block('Model B')],
        [{ x: 100, y: 100 }, { x: 200, y: 180 }],
      );

      expect(fills).toContain(ACCENT);
      expect(fills).toContain(CHART_INK.secondary);
    });

    it('draws nothing when the figure carries no blocks', () => {
      const { calls } = runDirectPlugin([], [{ x: 100, y: 100 }]);
      expect(calls.length).toBe(0);
    });

    it('writes the name above its value lines, and a hue rule beside all three', () => {
      const values = [
        { label: 'Intelligence', text: '82.4' },
        { label: 'Mean time', text: '15.20 s' },
      ];
      const { calls, rectFills } = runDirectPlugin([block('Model A', values, '#199e70')], [{ x: 100, y: 100 }]);
      const written = calls.filter((c) => c.op === 'fillText').map((c) => c.args[0]);

      expect(written).toEqual(['Model A', 'Intelligence', '82.4', 'Mean time', '15.20 s']);
      // The backing plate, then the hue rule over its left edge.
      expect(rectFills).toEqual([CHART_SURFACE, '#199e70']);
    });

    it('writes the values alone when the legend names the marks', () => {
      const { calls, rectFills } = runDirectPlugin(
        [block(undefined, [{ label: 'Intelligence', text: '82.4' }], '#d95926')],
        [{ x: 100, y: 100 }],
      );
      const written = calls.filter((c) => c.op === 'fillText').map((c) => c.args[0]);

      expect(written).toEqual(['Intelligence', '82.4']);
      expect(rectFills).toEqual([CHART_SURFACE, '#d95926']);
    });

    it('measures a plate from its widest column pair, not from the name alone', () => {
      // The fake context measures 6 px per character regardless of the font it is asked for.
      const ctx = { font: '', measureText: (text: string) => ({ width: text.length * 6 }) };
      const values = [
        { label: 'Intelligence', text: '82.4' },
        { label: 'Mean time', text: '15.20 s' },
      ];
      const wide = measureDirectLabelBlock(ctx as unknown as CanvasRenderingContext2D, {
        name: 'Model ABCD',
        values,
        hue: '#3987e5',
      });

      // 2 px rule + 4 px gap + 2 × 3 px padding, then the columns: 12 chars, an 8 px gap, 7 chars.
      expect(wide.labelColumn).toBe(72);
      expect(wide.valueColumn).toBe(42);
      expect(wide.width).toBe(12 + 72 + 8 + 42);
      // 2 × 2 px padding, one 12 px name line and two 12 px value lines.
      expect(wide.height).toBe(4 + 12 + 24);

      const nameOnly = measureDirectLabelBlock(ctx as unknown as CanvasRenderingContext2D, {
        name: 'Model ABCD',
        values: [],
        hue: '#3987e5',
      });
      expect(nameOnly.width).toBe(12 + 60);
      expect(nameOnly.height).toBe(4 + 12);
    });

    it('carries names, values or both into the plugin as the two toggles ask', () => {
      const pluginOptions = (spec: { config: { options?: { plugins?: unknown } } }) =>
        (spec.config.options?.plugins as Record<string, DirectLabelPluginOptions>)[directLabelPlugin.id];

      const off = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, inlineValues: false });
      expect(off.plugins).not.toContain(directLabelPlugin);
      expect(off.config.options?.plugins?.legend?.display).toBeTrue();

      const named = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, directLabels: true });
      expect(named.plugins).toContain(directLabelPlugin);
      expect(named.config.options?.plugins?.legend?.display).toBeFalse();
      expect(pluginOptions(named).blocks.map((b) => b.name)).toEqual(PROFILE_FIXTURE.map((e) => e.label));
      expect(pluginOptions(named).blocks.every((b) => b.values.length === 0)).toBeTrue();

      // Values are about the marks' numbers, not their names, so the legend stays.
      const valued = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, inlineValues: true });
      expect(valued.plugins).toContain(directLabelPlugin);
      expect(valued.config.options?.plugins?.legend?.display).toBeTrue();
      expect(pluginOptions(valued).blocks.map((b) => b.name)).toEqual([undefined, undefined, undefined]);
      expect(pluginOptions(valued).blocks[0].values).toEqual([
        { label: 'Mean time', text: '900 ms' },
        { label: 'Intelligence', text: '90.0' },
      ]);
      expect(pluginOptions(valued).blocks[0].hue).toBe(glyphFor(BASE_FIGURE_OPTIONS.glyphs, 'A').hue);

      const both = buildQualitySpeedScatter(PROFILE_FIXTURE, {
        ...BASE_FIGURE_OPTIONS,
        directLabels: true,
        inlineValues: true,
      });
      expect(both.config.options?.plugins?.legend?.display).toBeFalse();
      expect(pluginOptions(both).blocks[0].name).toBe('A');
      expect(pluginOptions(both).blocks[0].values.length).toBe(2);
    });
  });

  describe('the error-bar plugin', () => {
    interface RecordedCall {
      readonly op: string;
      readonly args: readonly unknown[];
    }

    function makeCtx(): { ctx: CanvasRenderingContext2D; calls: RecordedCall[] } {
      const calls: RecordedCall[] = [];
      const record = (op: string) => (...args: unknown[]) => calls.push({ op, args });
      const ctx = {
        save: record('save'),
        restore: record('restore'),
        beginPath: record('beginPath'),
        moveTo: record('moveTo'),
        lineTo: record('lineTo'),
        stroke: record('stroke'),
        fillText: record('fillText'),
        strokeStyle: '',
        fillStyle: '',
        lineWidth: 0,
        font: '',
        textBaseline: '',
      };
      return { ctx: ctx as unknown as CanvasRenderingContext2D, calls };
    }

    function makeScale(type: string, min = 0) {
      return { type, min, max: 1000, getPixelForValue: (value: number) => value };
    }

    function runPlugin(
      data: unknown[],
      scales: Record<string, unknown>,
      element: { x: number; y: number } = { x: 50, y: 50 },
    ): RecordedCall[] {
      const { ctx, calls } = makeCtx();
      const chart = {
        ctx,
        chartArea: { left: 0, top: 0, right: 1000, bottom: 1000 },
        data: { datasets: [{ data }] },
        scales,
        getDatasetMeta: () => ({ hidden: false, xAxisID: 'x', yAxisID: 'y', data: [element] }),
      };
      errorBarPlugin.afterDatasetsDraw?.(chart as unknown as Chart, {}, {}, false);
      return calls;
    }

    it('strokes a capped whisker from the y error keys on the raw datum', () => {
      const calls = runPlugin([{ x: 1, y: 10, yErrLow: 2, yErrHigh: 3 }], {
        x: makeScale('linear'),
        y: makeScale('linear'),
      });
      const drawn = calls.filter((c) => c.op === 'moveTo' || c.op === 'lineTo').map((c) => `${c.op}:${c.args.join(',')}`);
      expect(drawn).toEqual([
        'moveTo:50,8',
        'lineTo:50,13',
        'moveTo:45,8',
        'lineTo:55,8',
        'moveTo:45,13',
        'lineTo:55,13',
      ]);
    });

    it('strokes horizontal whiskers from the x error keys', () => {
      const calls = runPlugin([{ x: 100, y: 1, xErrHigh: 20 }], {
        x: makeScale('linear'),
        y: makeScale('linear'),
      });
      const line = calls.filter((c) => c.op === 'moveTo' || c.op === 'lineTo')[1];
      expect(line.args).toEqual([120, 50]);
    });

    it('clamps a bound that would fall off the bottom of a logarithmic axis', () => {
      const calls = runPlugin([{ x: 1, y: 1, yErrLow: 5, yErrHigh: 1 }], {
        x: makeScale('linear'),
        y: makeScale('logarithmic', 0.5),
      });
      const first = calls.find((c) => c.op === 'moveTo');
      expect(first?.args).toEqual([50, 0.5]);
    });

    it('writes no text at all: the plugin strokes intervals and nothing else', () => {
      const calls = runPlugin([{ x: 1, y: 1 }], { x: makeScale('linear'), y: makeScale('linear') });
      expect(calls.some((c) => c.op === 'fillText')).toBeFalse();
      expect(calls.some((c) => c.op === 'lineTo')).toBeFalse();
    });

    it('ignores plain numeric data, so it is inert on the profile plot', () => {
      const calls = runPlugin([0.5], { x: makeScale('linear'), y: makeScale('linear') });
      expect(calls.some((c) => c.op === 'lineTo')).toBeFalse();
    });
  });

  describe('the whole figure set', () => {
    it('builds every figure from one entry set, one order and one glyph assignment', () => {
      const figures = buildComparisonFigures(PROFILE_FIXTURE, { context: CONTEXT });
      expect(figures.qualitySpeed.id).toBe('s1-quality-speed');
      expect(figures.qualityCost.id).toBe('s2-quality-cost');
      expect(figures.speedCost.id).toBe('s3-speed-cost');
      expect(figures.smallMultiples.quality.id).toBe('p1a-quality');
      expect(figures.smallMultiples.speed.id).toBe('p1b-speed');
      expect(figures.smallMultiples.cost.id).toBe('p1c-cost');
      expect(figures.profile.id).toBe('p2-profile');
      expect(figures.selection.plotted.length).toBe(3);
    });

    it('defaults to Intelligence Index descending', () => {
      expect(DEFAULT_MODEL_SORT).toEqual({ key: 'intelligenceIndex', direction: 'desc' });
      expect(buildComparisonFigures(PROFILE_FIXTURE, { context: CONTEXT }).smallMultiples.order).toEqual([
        'A',
        'B',
        'C',
      ]);
    });

    it('defaults the speed measure to mean model time, everywhere the measure is read', () => {
      const figures = buildComparisonFigures(PROFILE_FIXTURE, { context: CONTEXT });

      expect(scaleOf(figures.smallMultiples.speed.config, 'y').title?.text).toEqual([
        'Mean time per question (ms)',
        'lower is better',
      ]);
      expect(titleLines(scaleOf(figures.qualitySpeed.config, 'x'))[0]).toContain('Mean time per question');
      expect(titleLines(scaleOf(figures.speedCost.config, 'x'))[0]).toContain('Mean time per question');
      expect(figures.profile.config.data.labels).toContain('Speed (mean model time)');
    });
  });

  describe('speedValue and speedLowerIsBetter', () => {
    it('reads every one of the four measures off its own field', () => {
      const entry = makeEntry({
        key: 'x',
        modelTimeMeanMs: 111,
        totalModelTimeMs: 222,
        ttftP50Ms: 333,
        speedIndex: 44,
      });
      expect(speedValue(entry, 'meanModelTime')).toBe(111);
      expect(speedValue(entry, 'totalModelTime')).toBe(222);
      expect(speedValue(entry, 'ttftP50')).toBe(333);
      expect(speedValue(entry, 'speedIndex')).toBe(44);
    });

    it('is true for the three time measures and false only for Speed Index', () => {
      expect(speedLowerIsBetter('meanModelTime')).toBeTrue();
      expect(speedLowerIsBetter('totalModelTime')).toBeTrue();
      expect(speedLowerIsBetter('ttftP50')).toBeTrue();
      expect(speedLowerIsBetter('speedIndex')).toBeFalse();
    });
  });

  describe('figure style', () => {
    const style = (bar: Partial<BarFigureStyle> = {}, scatter: Partial<ScatterFigureStyle> = {}): FigureStyle => ({
      bar: { ...DEFAULT_FIGURE_STYLE.bar, ...bar },
      scatter: { ...DEFAULT_FIGURE_STYLE.scatter, ...scatter },
    });

    const noteTexts = (spec: { chrome: { notes: readonly { text: string }[] } }): string[] =>
      spec.chrome.notes.map((note) => note.text);

    /** Two models on one mean time, the weaker dominated but within the stronger one's interval. */
    const WITHIN = [
      makeEntry({ key: 'P', intelligenceIndex: 62 }),
      makeEntry({ key: 'Q', intelligenceIndex: 60, speedDegraded: true }),
    ];
    const withinOptions = (figureStyle: FigureStyle = DEFAULT_FIGURE_STYLE) => ({
      ...BASE_FIGURE_OPTIONS,
      glyphs: buildIdentityGlyphs(WITHIN),
      style: figureStyle,
    });

    describe('bars', () => {
      it('reproduces the unstyled panel with the default style', () => {
        const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions());
        const dataset = datasetsOf(figure.quality.config)[0];
        expect(dataset['maxBarThickness']).toBe(24);
        expect((dataset['categoryPercentage'] as number) * (dataset['barPercentage'] as number)).toBeCloseTo(0.72, 9);
        expect(dataset['borderRadius']).toBe(4);
        expect(dataset['borderWidth']).toBe(2);

        const value = scaleOf(figure.quality.config, 'y') as ScaleProbe & {
          ticks?: { font?: { size?: number } };
          title?: { font?: { size?: number } };
          grid?: { display?: boolean };
        };
        expect(value.ticks?.font?.size).toBe(11);
        expect(value.title?.font?.size).toBe(12);
        expect(value.grid?.display).toBeTrue();
        expect(figure.quality.plugins).toEqual([errorBarPlugin, ChartDataLabels]);
        expect(noteTexts(figure.quality)).not.toContain(HIDDEN_INTERVALS_NOTE);

        const explicit = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ style: DEFAULT_FIGURE_STYLE }));
        expect(datasetsOf(explicit.quality.config)[0]).toEqual(dataset);
      });

      it('maps the space, the width limit and the outline onto the dataset', () => {
        const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({
          style: style({ gapPercent: 10, maxBarWidthPx: null, cornerRadiusPx: 0, outlineWidthPx: 3 }),
        }));
        const dataset = datasetsOf(figure.quality.config)[0];
        expect(dataset['categoryPercentage']).toBe(1);
        expect(dataset['barPercentage']).toBeCloseTo(0.9, 9);
        expect('maxBarThickness' in dataset).toBeFalse();
        expect(dataset['borderRadius']).toBe(0);
        expect(dataset['borderWidth']).toBe(3);
      });

      it('hides the value labels and the value-axis grid on request, and sizes the text', () => {
        const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({
          style: style({ valueLabels: false, gridlines: false, axisTextSizePx: 16, valueLabelSizePx: 18 }),
        }));
        const datalabels = figure.quality.config.options?.plugins?.datalabels as {
          display?: unknown;
          font?: { size?: number };
        };
        expect(datalabels.display).toBeFalse();
        expect(datalabels.font?.size).toBe(18);
        const value = scaleOf(figure.quality.config, 'y') as ScaleProbe & {
          ticks?: { font?: { size?: number } };
          title?: { font?: { size?: number } };
          grid?: { display?: boolean };
        };
        expect(value.grid?.display).toBeFalse();
        expect(value.ticks?.font?.size).toBe(16);
        expect(value.title?.font?.size).toBe(17);
      });

      it('hides the whiskers, moves the labels to the bar ends and says so, but not where none would draw', () => {
        const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({
          speedMeasure: 'meanModelTime',
          style: style({ intervals: false }),
        }));
        expect(figure.quality.plugins).not.toContain(errorBarPlugin);
        expect(figure.quality.plugins).toContain(ChartDataLabels);

        const datalabels = figure.quality.config.options?.plugins?.datalabels as {
          offset: (ctx: { dataIndex: number; chart: { scales: Record<string, { getPixelForValue(v: number): number }> } }) => number;
        };
        const chart = { scales: { y: { getPixelForValue: (v: number) => 300 - v * 2 } } };
        for (let index = 0; index < PROFILE_FIXTURE.length; index += 1) {
          expect(datalabels.offset({ dataIndex: index, chart })).withContext(String(index)).toBe(4);
        }
        expect(noteTexts(figure.quality)).toContain(HIDDEN_INTERVALS_NOTE);
        // Mean model time per question draws no whisker anyway.
        expect(noteTexts(figure.speed)).not.toContain(HIDDEN_INTERVALS_NOTE);
      });

      it('leaves out the hidden-intervals note when it is switched off, and changes nothing while the bars show', () => {
        const off = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({
          style: style({ intervals: false, hiddenIntervalsNote: false }),
        }));
        expect(off.quality.plugins).not.toContain(errorBarPlugin);
        expect(noteTexts(off.quality)).not.toContain(HIDDEN_INTERVALS_NOTE);

        const shown = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({
          style: style({ hiddenIntervalsNote: false }),
        }));
        const plain = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions());
        expect(shown.quality.plugins).toEqual(plain.quality.plugins);
        expect(shown.quality.chrome).toEqual(plain.quality.chrome);
        expect(datasetsOf(shown.quality.config)).toEqual(datasetsOf(plain.quality.config));
      });

      it('adds the mean-time note only on mean time, and only while its switch is on', () => {
        const degraded = [makeEntry({ key: 'A' }), makeEntry({ key: 'B', speedDegraded: true })];
        const options = (figureStyle: FigureStyle, speedMeasure: SmallMultiplesOptions['speedMeasure'] = 'meanModelTime') =>
          smallMultiplesOptions({ glyphs: buildIdentityGlyphs(degraded), speedMeasure, style: figureStyle });
        const warning = (texts: string[]): boolean => texts.some((text) => text.startsWith('Speed is not comparable'));

        const on = buildSmallMultiples(degraded, options(DEFAULT_FIGURE_STYLE));
        expect(noteTexts(on.speed)).toContain(MEAN_TIME_NO_INTERVAL_NOTE);
        expect(noteTexts(on.quality)).not.toContain(MEAN_TIME_NO_INTERVAL_NOTE);
        expect(noteTexts(on.cost)).not.toContain(MEAN_TIME_NO_INTERVAL_NOTE);
        expect(warning(noteTexts(on.speed))).toBeTrue();

        for (const intervals of [true, false]) {
          const off = buildSmallMultiples(degraded, options(style({ intervals, meanTimeNoIntervalNote: false })));
          expect(noteTexts(off.speed)).withContext(`intervals ${intervals}`).not.toContain(MEAN_TIME_NO_INTERVAL_NOTE);
          expect(warning(noteTexts(off.speed))).withContext(`intervals ${intervals}`).toBeTrue();
        }

        for (const figureStyle of [DEFAULT_FIGURE_STYLE, style({ meanTimeNoIntervalNote: false })]) {
          const ttft = buildSmallMultiples(degraded, options(figureStyle, 'ttftP50'));
          expect(noteTexts(ttft.speed)).not.toContain(MEAN_TIME_NO_INTERVAL_NOTE);
        }
      });
    });

    describe('trade-off charts', () => {
      it('maps the mark size, the frontier width, the legend and the text sizes', () => {
        const spec = buildQualitySpeedScatter(PROFILE_FIXTURE, {
          ...BASE_FIGURE_OPTIONS,
          speedMeasure: 'ttftP50',
          style: style({}, { markRadiusPx: 10, frontierWidthPx: 4, legendPosition: 'right', axisTextSizePx: 16, gridlines: false }),
        });
        const datasets = datasetsOf(spec.config);
        expect(datasets[0]['radius']).toBe(10);
        expect(datasets[0]['hoverRadius']).toBe(13);
        expect(datasets[0]['hitRadius']).toBe(12);
        const frontier = datasets.find((dataset) => dataset['label'] === 'Pareto frontier')!;
        expect(frontier['borderWidth']).toBe(4);
        expect(spec.config.options?.plugins?.legend?.position).toBe('right');

        const x = scaleOf(spec.config, 'x') as ScaleProbe & {
          ticks?: { font?: { size?: number } };
          title?: { font?: { size?: number } };
          grid?: { display?: boolean };
        };
        expect(x.ticks?.font?.size).toBe(16);
        expect(x.title?.font?.size).toBe(17);
        expect(x.grid?.display).toBeFalse();

        const small = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, style: style({}, { markRadiusPx: 3 }) });
        expect(datasetsOf(small.config)[0]['hitRadius']).toBe(15);
        const plain = buildQualitySpeedScatter(PROFILE_FIXTURE, BASE_FIGURE_OPTIONS);
        expect(datasetsOf(plain.config)[0]['radius']).toBe(6);
        expect(datasetsOf(plain.config)[0]['hoverRadius']).toBe(9);
        expect(datasetsOf(plain.config)[0]['hitRadius']).toBe(12);
      });

      it('hides the whiskers, fits the axes to the values alone and says so', () => {
        const hidden = buildQualityCostScatter(PROFILE_FIXTURE, {
          ...BASE_FIGURE_OPTIONS,
          style: style({}, { intervals: false }),
        });
        const stripped = PROFILE_FIXTURE.map((entry) => ({
          ...entry,
          intelligenceIndexCi95HalfWidth: 0,
          candidateCostPerQuestionSdUsd: null,
        }));
        const bare = buildQualityCostScatter(stripped, BASE_FIGURE_OPTIONS);
        const shown = buildQualityCostScatter(PROFILE_FIXTURE, BASE_FIGURE_OPTIONS);

        expect(hidden.plugins).not.toContain(errorBarPlugin);
        for (const axis of ['x', 'y'] as const) {
          expect(scaleOf(hidden.config, axis).min).withContext(axis).toBe(scaleOf(bare.config, axis).min!);
          expect(scaleOf(hidden.config, axis).max).withContext(axis).toBe(scaleOf(bare.config, axis).max!);
        }
        expect(shown.chrome.key.map((item) => item.glyph)).toContain('interval');
        expect(hidden.chrome.key.map((item) => item.glyph)).not.toContain('interval');
        expect(noteTexts(hidden)).toContain(HIDDEN_INTERVALS_NOTE);
        expect(noteTexts(bare)).not.toContain(HIDDEN_INTERVALS_NOTE);
        const bareHidden = buildQualityCostScatter(stripped, { ...BASE_FIGURE_OPTIONS, style: style({}, { intervals: false }) });
        // Nothing would have drawn, so nothing is said.
        expect(noteTexts(bareHidden)).not.toContain(HIDDEN_INTERVALS_NOTE);

        const labelled = buildQualityCostScatter(PROFILE_FIXTURE, {
          ...BASE_FIGURE_OPTIONS,
          directLabels: true,
          style: style({}, { intervals: false, labelTextSizePx: 14, markRadiusPx: 8 }),
        });
        const pluginOptions = (labelled.config.options?.plugins as Record<string, DirectLabelPluginOptions>)[directLabelPlugin.id];
        expect(pluginOptions.avoidWhiskers).toBeFalse();
        expect(pluginOptions.fontSizePx).toBe(14);
        expect(pluginOptions.markRadiusPx).toBe(8);
        const defaultLabelled = buildQualityCostScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, directLabels: true });
        expect((defaultLabelled.config.options?.plugins as Record<string, DirectLabelPluginOptions>)[directLabelPlugin.id].avoidWhiskers)
          .toBeTrue();
      });

      it('keeps the frontier note whatever the whiskers, and drops each note only on its own switch', () => {
        const plain = buildQualitySpeedScatter(WITHIN, withinOptions());
        expect(noteTexts(plain)).toContain(FRONTIER_UNCERTAINTY_NOTE);

        const hidden = buildQualitySpeedScatter(WITHIN, withinOptions(style({}, { intervals: false })));
        const texts = noteTexts(hidden);
        // Warnings first, then the frontier note, then the hidden-intervals note.
        expect(hidden.chrome.notes[0].tone).toBe('warning');
        expect(texts.indexOf(FRONTIER_UNCERTAINTY_NOTE)).toBe(1);
        expect(texts.indexOf(HIDDEN_INTERVALS_NOTE)).toBe(2);

        const quiet = buildQualitySpeedScatter(WITHIN, withinOptions(style({}, { intervals: false, hiddenIntervalsNote: false })));
        expect(noteTexts(quiet)).not.toContain(HIDDEN_INTERVALS_NOTE);
        expect(noteTexts(quiet)).toContain(FRONTIER_UNCERTAINTY_NOTE);

        for (const intervals of [true, false]) {
          const spec = buildQualitySpeedScatter(WITHIN, withinOptions(style({}, { intervals, frontierIntervalsNote: false })));
          expect(noteTexts(spec)).withContext(`intervals ${intervals}`).not.toContain(FRONTIER_UNCERTAINTY_NOTE);
          expect(spec.chrome.notes.some((note) => note.tone === 'warning')).withContext(`intervals ${intervals}`).toBeTrue();
        }
        const silent = buildQualitySpeedScatter(WITHIN, withinOptions(style({}, {
          intervals: false,
          hiddenIntervalsNote: false,
          frontierIntervalsNote: false,
        })));
        expect(silent.chrome.notes.map((note) => note.tone)).toEqual(['warning']);
      });

      it('drops the shading and its key item, keeping the frontier', () => {
        const spec = buildQualitySpeedScatter(PROFILE_FIXTURE, {
          ...BASE_FIGURE_OPTIONS,
          speedMeasure: 'ttftP50',
          style: style({}, { dominatedShading: false }),
        });
        expect(spec.plugins).not.toContain(dominatedRegionPlugin);
        expect(spec.plugins).toContain(errorBarPlugin);
        const glyphs = spec.chrome.key.map((item) => item.glyph);
        expect(glyphs).toContain('frontier');
        expect(glyphs).not.toContain('dominated');
        expect(datasetsOf(spec.config).map((d) => d['label'])).toContain('Pareto frontier');
      });
    });

    describe('the direction marker', () => {
      it('is registered last on every scatter, with the figure direction, over reserved padding', () => {
        const figures = buildComparisonFigures(PROFILE_FIXTURE, { context: CONTEXT });
        for (const spec of [figures.qualitySpeed, figures.qualityCost, figures.speedCost]) {
          expect(spec.plugins[spec.plugins.length - 1]).withContext(spec.id).toBe(directionMarkerPlugin);
          const options = (spec.config.options?.plugins as Record<string, DirectionMarkerPluginOptions>)[directionMarkerPlugin.id];
          expect(options.direction).withContext(spec.id).toEqual(spec.chrome.direction!);
          expect(options.fontSizePx).withContext(spec.id).toBe(11);
          const padding = (spec.config.options as { layout?: { padding?: { top?: number } } }).layout?.padding?.top;
          expect(padding).withContext(spec.id).toBe(directionMarkerMetrics(11).height + DIRECTION_MARKER_GAP + 4);
        }
        expect(figures.qualitySpeed.plugins).toEqual([dominatedRegionPlugin, errorBarPlugin, directionMarkerPlugin]);
        for (const spec of [figures.smallMultiples.quality, figures.smallMultiples.speed, figures.smallMultiples.cost, figures.profile]) {
          expect((spec as unknown as ChartSpec).plugins).withContext(spec.id).not.toContain(directionMarkerPlugin);
        }

        const large = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, style: style({}, { axisTextSizePx: 16 }) });
        const largePadding = (large.config.options as { layout?: { padding?: { top?: number } } }).layout?.padding?.top;
        expect(largePadding).toBe(directionMarkerMetrics(16).height + DIRECTION_MARKER_GAP + 4);
        expect(directionMarkerMetrics(11).height).toBe(22);
      });

      it('draws a pill whose right edge is the plot area\'s and whose bottom sits above it', () => {
        const rects: number[][] = [];
        const texts: string[] = [];
        const noop = (): void => undefined;
        const ctx = {
          save: noop, restore: noop, beginPath: noop, fill: noop, stroke: noop, translate: noop, rotate: noop,
          scale: noop, moveTo: noop, lineTo: noop, rect: noop,
          roundRect: (...args: number[]) => rects.push(args),
          measureText: (text: string) => ({ width: text.length * 6 }),
          fillText: (text: string) => texts.push(text),
          font: '', fillStyle: '', strokeStyle: '', lineWidth: 0, lineCap: '', lineJoin: '', textAlign: '', textBaseline: '',
        };
        const chart = { ctx, chartArea: { left: 50, top: 40, right: 400, bottom: 300 } };
        directionMarkerPlugin.afterDraw?.(
          chart as unknown as Chart,
          {} as never,
          { direction: { x: 'left', y: 'top', label: 'Better' }, fontSizePx: 11 } as never,
        );

        expect(rects.length).toBe(1);
        const [x, y, width, height] = rects[0];
        expect(x + width).toBe(400);
        expect(y + height).toBe(40 - DIRECTION_MARKER_GAP);
        expect(height).toBe(22);
        expect(texts).toEqual(['Better']);
      });
    });

    it('measures a taller plate at a larger label size', () => {
      const ctx = { font: '', measureText: (text: string) => ({ width: text.length * 6 }) };
      const block = { name: 'Model A', values: [{ label: 'Intelligence', text: '82.4' }], hue: '#3987e5' };
      const normal = measureDirectLabelBlock(ctx as unknown as CanvasRenderingContext2D, block);
      const large = measureDirectLabelBlock(ctx as unknown as CanvasRenderingContext2D, block, 14);
      expect(large.height).toBeGreaterThan(normal.height);
      expect(normal.height).toBe(4 + 12 + 12);
    });
  });
});

/** A figure reduced to the registry keys it asks for: its chart type, dataset types and scales. */
interface RegistryDemand {
  readonly id: string;
  readonly type: string;
  readonly datasetTypes: readonly string[];
  readonly scaleTypes: readonly string[];
}

function demandOf(spec: unknown): RegistryDemand {
  const probe = spec as {
    id: string;
    config: {
      type: string;
      data: { datasets: { type?: string }[] };
      options?: { scales?: Record<string, { type?: string } | undefined> };
    };
  };
  const isString = (value: string | undefined): value is string => typeof value === 'string';
  return {
    id: probe.id,
    type: probe.config.type,
    datasetTypes: probe.config.data.datasets.map((dataset) => dataset.type).filter(isString),
    scaleTypes: Object.values(probe.config.options?.scales ?? {})
      .map((scale) => scale?.type)
      .filter(isString),
  };
}

/**
 * chart.js registers nothing on its own: a controller, element or scale missing from
 * APP_CHART_REGISTRABLES is absent from the registry, and the figure that needs it throws at render
 * time rather than failing to compile. These cases register exactly what the application registers,
 * then ask the registry for every key the built configurations actually name - so the check follows
 * the figures instead of a second list that has to be kept in step by hand.
 */
describe('chart.js registration', () => {
  const figures = buildComparisonFigures(PROFILE_FIXTURE, { context: CONTEXT });
  const demands = [
    figures.qualitySpeed,
    figures.qualityCost,
    figures.speedCost,
    figures.smallMultiples.quality,
    figures.smallMultiples.speed,
    figures.smallMultiples.cost,
    figures.profile,
  ].map(demandOf);

  beforeAll(() => {
    Chart.register(...APP_CHART_REGISTRABLES);
  });

  it('demands cover all seven figures', () => {
    expect(demands.length).toBe(7);
    expect(new Set(demands.map((demand) => demand.id)).size).toBe(7);
  });

  demands.forEach((demand) => {
    it(`registers everything ${demand.id} draws with`, () => {
      expect(() => Chart.registry.getController(demand.type)).not.toThrow();
      demand.datasetTypes.forEach((type) => {
        expect(() => Chart.registry.getController(type)).not.toThrow();
      });
      demand.scaleTypes.forEach((type) => {
        expect(() => Chart.registry.getScale(type)).not.toThrow();
      });
    });
  });

  it('registers the controller the config analytics panel draws with', () => {
    expect(() => Chart.registry.getController(CONFIG_ANALYTICS_CHART_TYPE)).not.toThrow();
  });
});
