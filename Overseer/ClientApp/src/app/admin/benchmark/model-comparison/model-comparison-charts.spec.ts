import { Chart } from 'chart.js';
import type { ChartConfiguration, ChartType, FontSpec, Plugin, Scale } from 'chart.js';
import { toFont } from 'chart.js/helpers';
import ChartDataLabels from 'chartjs-plugin-datalabels';
import {
  ACCENT,
  AXIS_TITLE_RESERVE_PX,
  axisTitleLines,
  buildNumberSamples,
  splitAxisTitle,
  CATEGORICAL_PALETTE_DARK,
  CATEGORICAL_PALETTE_LIGHT,
  CHART_INK,
  CHART_SURFACE,
  DEFAULT_MODEL_SORT,
  DE_EMPHASIS_FILL,
  DE_EMPHASIS_STROKE,
  FRONTIER_DASH,
  FRONTIER_UNCERTAINTY_NOTE,
  IDENTITY_SHAPES,
  PROFILE_IDEAL_LABEL,
  MEAN_TIME_NO_INTERVAL_NOTE,
  MAX_PLOTTED_ENTRIES,
  PALETTE_VALIDATION_INPUT,
  PALETTE_VALIDATION_INPUT_LIGHT,
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
  errorBarPlugin,
  formatQuestionsAsked,
  glyphFor,
  measureDirectLabelBlock,
  modelLabelLines,
  modelLabelText,
  normalizeProfile,
  placeDirectLabels,
  plotFramePlugin,
  profileColumns,
  profileTilePlugin,
  segmentIntersectsRect,
  segmentsIntersect,
  modelOrderKeys,
  selectPlottedEntries,
  sortEntriesForComparison,
  speedLowerIsBetter,
  speedValue,
  suiteCostSdUsd,
  suiteCostUsd,
} from './model-comparison-charts';
import type {
  BarOrientation,
  ChartSpec,
  CostMeasure,
  SpeedMeasure,
  DirectLabelAnchor,
  DirectLabelBlock,
  DirectLabelBox,
  DirectLabelPluginOptions,
  DirectLabelValue,
  FigureSetOptions,
  ModelComparisonContext,
  ModelComparisonEntry,
  ProfileFigure,
  ProfileTilePluginOptions,
  SmallMultiplesOptions,
} from './model-comparison-charts';
import { DEFAULT_APPEARANCE_STYLE, DEFAULT_FIGURE_STYLE, HIDDEN_INTERVALS_NOTE } from './figure-style';
import type {
  BarFigureStyle,
  FigureAppearanceStyle,
  FigureStyle,
  ProfileFigureStyle,
  ScatterFigureStyle,
} from './figure-style';
import { figureFont } from './figure-fonts';
import { resolveFigureTheme } from './figure-theme';
import type { NumberFormatStyle } from './measure-format';
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

/**
 * Every single-chart figure the set builds, flattened, so config assertions can cover them at once.
 * The profile is tiled and is read through {@link profileTiles} instead.
 */
function allSpecs(figures: ReturnType<typeof buildComparisonFigures>): ChartSpec[] {
  return [
    figures.qualitySpeed,
    figures.qualityCost,
    figures.speedCost,
    figures.smallMultiples.quality,
    figures.smallMultiples.speed,
    figures.smallMultiples.cost,
  ] as unknown as ChartSpec[];
}

/** Every figure's id, title and chrome, the profile's included, so chrome assertions cover all seven. */
function allFigures(
  figures: ReturnType<typeof buildComparisonFigures>,
): { readonly id: string; readonly title: string; readonly chrome: ChartSpec['chrome'] }[] {
  return [...allSpecs(figures), figures.profile];
}

/** The profile's tiles, read structurally like any other config. */
function profileTiles(profile: ProfileFigure): unknown[] {
  return [...profile.tiles];
}

/** A profile tile's own plugin options. */
function tileOptionsOf(tile: unknown): ProfileTilePluginOptions {
  return (tile as { options: { plugins: Record<string, unknown> } }).options.plugins[profileTilePlugin.id] as ProfileTilePluginOptions;
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
  scoredItemsMin: 10,
  scoredItemsMax: 10,
  examItemCount: 10,
  questionsAskedPerRun: 10,
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
    candidateCostPerRunUsd: 0.02,
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
 * cost inversion cannot pass by coincidence. One per colored provider, in palette order.
 */
const PROFILE_FIXTURE: ModelComparisonEntry[] = [
  makeEntry({
    key: 'A',
    provider: 'google',
    intelligenceIndex: 90,
    speedIndex: 100,
    ttftP50Ms: 500,
    candidateCostPerQuestionUsd: 0.03,
    candidateCostPerRunUsd: 0.3,
  }),
  makeEntry({
    key: 'B',
    provider: 'anthropic',
    intelligenceIndex: 60,
    speedIndex: 50,
    ttftP50Ms: 300,
    candidateCostPerQuestionUsd: 0.02,
    candidateCostPerRunUsd: 0.2,
  }),
  makeEntry({
    key: 'C',
    provider: 'openai',
    intelligenceIndex: 30,
    speedIndex: 0,
    ttftP50Ms: 100,
    candidateCostPerQuestionUsd: 0.01,
    candidateCostPerRunUsd: 0.1,
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

    it('exports the light palette and its validator input verbatim', () => {
      expect(CATEGORICAL_PALETTE_LIGHT).toEqual(['#2a78d6', '#eb6834', '#18a070']);
      expect(PALETTE_VALIDATION_INPUT_LIGHT).toBe('#2a78d6,#eb6834,#18a070');
      expect(resolveFigureTheme({ ...DEFAULT_APPEARANCE_STYLE, theme: 'light' }).chart.categorical)
        .toEqual([...CATEGORICAL_PALETTE_LIGHT]);
    });

    it('keeps the exported constants the dark theme resolves to', () => {
      const dark = resolveFigureTheme().chart;
      expect(dark.surface).toBe(CHART_SURFACE);
      expect(dark.categorical).toEqual([...CATEGORICAL_PALETTE_DARK]);
      expect(dark.accent).toBe(ACCENT);
      expect(dark.inkPrimary).toBe(CHART_INK.primary);
      expect(dark.inkSecondary).toBe(CHART_INK.secondary);
      expect(dark.inkMuted).toBe(CHART_INK.muted);
      expect(dark.gridline).toBe(CHART_INK.gridline);
      expect(dark.baseline).toBe(CHART_INK.baseline);
      expect(dark.deEmphasisFill).toBe(DE_EMPHASIS_FILL);
      expect(dark.deEmphasisStroke).toBe(DE_EMPHASIS_STROKE);
    });
  });

  describe('identity glyphs', () => {
    const providers = ['google', 'anthropic', 'openai', 'mistral'];
    const eight = Array.from({ length: 8 }, (_, i) => makeEntry({ key: `m${i}`, provider: providers[i % providers.length] }));

    it('colors each glyph by its provider and keeps a shape by position', () => {
      const glyphs = buildIdentityGlyphs(eight);
      const hues = eight.map((e) => glyphFor(glyphs, e.key).hue);
      expect(hues.slice(0, 4)).toEqual([...CATEGORICAL_PALETTE_DARK, DE_EMPHASIS_STROKE]);
      expect(eight.map((e) => glyphFor(glyphs, e.key).provider).slice(0, 4))
        .toEqual(['google', 'anthropic', 'openai', 'other']);
      expect(eight.every((e) => (IDENTITY_SHAPES as readonly string[]).includes(glyphFor(glyphs, e.key).shape))).toBe(true);
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
        makeEntry({ key: 'ok1', provider: 'google' }),
        makeEntry({ key: 'bad', excluded: true, excludedReasonKeys: ['ScoringMethodVersion'] }),
        makeEntry({ key: 'ok2', provider: 'anthropic' }),
      ];
      const glyphs = buildIdentityGlyphs(withExcluded);
      expect(glyphs.has('bad')).toBe(false);
      expect(glyphFor(glyphs, 'ok1').hue).toBe(CATEGORICAL_PALETTE_DARK[0]);
      expect(glyphFor(glyphs, 'ok2').hue).toBe(CATEGORICAL_PALETTE_DARK[1]);
    });
  });

  describe('the twelve-entry cap', () => {
    it('plots twelve and pushes the thirteenth into the table with a visible notice', () => {
      const thirteen = Array.from({ length: 13 }, (_, i) =>
        makeEntry({ key: `m${i}`, label: `Model ${i}`, intelligenceIndex: 90 - i }),
      );
      const selection = selectPlottedEntries(thirteen, DEFAULT_MODEL_SORT, 'speedIndex', 'candidateSuite', CONTEXT);

      expect(MAX_PLOTTED_ENTRIES).toBe(12);
      expect(selection.plotted.length).toBe(12);
      expect(selection.overflow.map((e) => e.key)).toEqual(['m12']);
      expect(selection.notices.join(' ')).toContain('Model 12');
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

  describe('the custom model order', () => {
    const three = [
      makeEntry({ key: 'a', label: 'Alpha', intelligenceIndex: 90 }),
      makeEntry({ key: 'b', label: 'Beta', intelligenceIndex: 60 }),
      makeEntry({ key: 'c', label: 'Gamma', intelligenceIndex: 30 }),
    ];

    it('orders by the list, ignoring the direction', () => {
      for (const direction of ['asc', 'desc'] as const) {
        const ordered = sortEntriesForComparison(three, { key: 'custom', direction, customOrder: ['c', 'a', 'b'] }, 'speedIndex', 'candidateSuite', CONTEXT);
        expect(ordered.map((e) => e.key), direction).toEqual(['c', 'a', 'b']);
      }
    });

    it('puts entries the list does not name after the named ones, by label', () => {
      const withMore = [...three, makeEntry({ key: 'd', label: 'Delta' })];
      const ordered = sortEntriesForComparison(
        withMore, { key: 'custom', direction: 'desc', customOrder: ['b'] }, 'speedIndex', 'candidateSuite', CONTEXT);
      expect(ordered.map((e) => e.key)).toEqual(['b', 'a', 'd', 'c']);
    });

    it('lets the plot cap follow the custom order', () => {
      const thirteen = Array.from({ length: 13 }, (_, i) =>
        makeEntry({ key: `m${i}`, label: `Model ${i}`, intelligenceIndex: 90 - i }));
      const customOrder = ['m12', ...thirteen.slice(0, 12).map((e) => e.key)];
      const selection = selectPlottedEntries(
        thirteen, { key: 'custom', direction: 'desc', customOrder }, 'speedIndex', 'candidateSuite', CONTEXT);
      expect(selection.plotted[0].key).toBe('m12');
      expect(selection.overflow.map((e) => e.key)).toEqual(['m11']);
    });

    it('keeps the default sort unchanged', () => {
      expect(DEFAULT_MODEL_SORT.customOrder).toBeUndefined();
    });
  });

  describe('modelOrderKeys', () => {
    const entries = [
      makeEntry({ key: 'x', label: 'Zed', excluded: true, excludedReasonKeys: ['ScoringMethodVersion'] }),
      makeEntry({ key: 'a', label: 'Alpha', intelligenceIndex: 40 }),
      makeEntry({ key: 'w', label: 'Why', excluded: true, excludedReasonKeys: ['ScoringMethodVersion'] }),
      makeEntry({ key: 'b', label: 'Beta', intelligenceIndex: 80 }),
    ];

    it('lists the comparable entries in chart order, then the excluded ones by label', () => {
      expect(modelOrderKeys(entries, DEFAULT_MODEL_SORT, 'speedIndex', 'candidateSuite', CONTEXT))
        .toEqual(['b', 'a', 'w', 'x']);
      expect(modelOrderKeys(entries, { key: 'intelligenceIndex', direction: 'asc' }, 'speedIndex', 'candidateSuite', CONTEXT))
        .toEqual(['a', 'b', 'w', 'x']);
    });

    it('lets a custom order place excluded entries', () => {
      expect(modelOrderKeys(
        entries, { key: 'custom', direction: 'desc', customOrder: ['x', 'a', 'b', 'w'] }, 'speedIndex', 'candidateSuite', CONTEXT))
        .toEqual(['x', 'a', 'b', 'w']);
    });

    it('agrees with the charts\' own order for the comparable entries', () => {
      const plotted = selectPlottedEntries(entries, DEFAULT_MODEL_SORT, 'speedIndex', 'candidateSuite', CONTEXT).plotted;
      const keys = modelOrderKeys(entries, DEFAULT_MODEL_SORT, 'speedIndex', 'candidateSuite', CONTEXT);
      expect(keys.slice(0, plotted.length)).toEqual(plotted.map((e) => e.key));
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
      expect(interaction?.intersect).toBe(false);
    });

    it('draws single-run entries hollow and multi-run entries solid', () => {
      const entries = [
        makeEntry({ key: 'once', runCount: 1, provider: 'google' }),
        makeEntry({ key: 'thrice', runCount: 3, provider: 'anthropic' }),
      ];
      const spec = buildQualitySpeedScatter(entries, {
        ...BASE_FIGURE_OPTIONS,
        glyphs: buildIdentityGlyphs(entries),
      });
      const datasets = datasetsOf(spec.config);
      expect(datasets[0]['backgroundColor']).toBe('transparent');
      expect(datasets[0]['borderColor']).toBe(CATEGORICAL_PALETTE_DARK[0]);
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
      expect(spec.chrome.key).toContainEqual({ glyph: 'hollow', text: 'Single run' });
      expect(spec.chrome.key.map((item) => item.glyph)).not.toContain('solid');
    });

    it('draws the Pareto frontier, keys it once and keeps it out of the identity legend', () => {
      // Distinguishing x values, on TTFT: the fixture's model time is tied across all three entries.
      const spec = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, speedMeasure: 'ttftP50' });
      const labels = datasetsOf(spec.config).map((d) => d['label']);
      expect(labels).toContain('Pareto frontier');

      const filter = spec.config.options?.plugins?.legend?.labels?.filter;
      expect(filter).toBeDefined();
      // Every fixture model is on the frontier here, so nothing is faded; one provider item each.
      expect(spec.chrome.key.map((item) => item.glyph))
        .toEqual(['solid', 'interval', 'frontier', 'provider', 'provider', 'provider']);
      expect(spec.plugins).toEqual([errorBarPlugin]);
    });

    it('keys the frontier, the faded models and the providers in that order', () => {
      const entries = [
        makeEntry({ key: 'best', provider: 'openai', intelligenceIndex: 80, ttftP50Ms: 200 }),
        makeEntry({ key: 'beaten', provider: 'anthropic', intelligenceIndex: 60, ttftP50Ms: 900 }),
        makeEntry({ key: 'fast', provider: 'openai', intelligenceIndex: 40, ttftP50Ms: 100 }),
      ];
      const spec = buildQualitySpeedScatter(entries, {
        ...BASE_FIGURE_OPTIONS,
        glyphs: buildIdentityGlyphs(entries),
        speedMeasure: 'ttftP50',
      });
      expect(spec.chrome.key.map((item) => item.glyph))
        .toEqual(['solid', 'interval', 'frontier', 'faded', 'provider', 'provider']);
      expect(spec.chrome.key.find((item) => item.glyph === 'faded')?.text)
        .toBe('Faded: another model is better on both axes');
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
      expect(display(many)).toBe(false);
      expect(display(few)).toBe(false);
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
        'Intelligence Index: 80',
      ]);

      // Three models are plotted, so dataset index 3 is the frontier annotation.
      expect(tooltip.filter?.({ datasetIndex: 0 })).toBe(true);
      expect(tooltip.filter?.({ datasetIndex: 3 })).toBe(false);
    });
  });

  describe('P1 small multiples', () => {
    it('gives all three panels a zero baseline, because a truncated bar misstates its ratio', () => {
      const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions());
      for (const panel of [figure.quality, figure.speed, figure.cost]) {
        const value = scaleOf(panel.config, 'y');
        expect(value.beginAtZero, panel.id).toBe(true);
        expect(value.min, panel.id).toBe(0);
        expect(value.type, panel.id).toBe('linear');
      }
    });

    it('carries the 95 % interval the trade-off charts use on the Intelligence bars', () => {
      const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions());
      const point = pointsOf(figure.quality.config)[0];
      expect(point['yErrLow']).toBe(PROFILE_FIXTURE[0].intelligenceIndexCi95HalfWidth);
      expect(point['yErrHigh']).toBe(PROFILE_FIXTURE[0].intelligenceIndexCi95HalfWidth);
      expect(figure.quality.plugins).toContain(errorBarPlugin);
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
      // The Better badge says which end is better, so the value-axis title does not.
      expect(titleLines(scaleOf(ttft.speed.config, 'y')).length).toBe(1);
      expect(scaleOf(ttft.speed.config, 'y').max).toBeUndefined();
      const ttftPoint = pointsOf(ttft.speed.config)[0];
      expect(ttftPoint['y']).toBe(500);
      expect(ttftPoint['yErrHigh']).toBe(1300);

      // 900 ms is the largest mean, so the panel stays in milliseconds.
      expect(scaleOf(mean.speed.config, 'y').title?.text).toEqual(['Mean time per question (ms)']);
      const meanPoint = pointsOf(mean.speed.config)[0];
      expect(meanPoint['y']).toBe(PROFILE_FIXTURE[0].modelTimeMeanMs);
      expect(meanPoint['yErrLow']).toBeUndefined();
      expect(meanPoint['yErrHigh']).toBeUndefined();
      expect(mean.speed.chrome.notes).toContainEqual({
        text: 'Mean time per question has no uncertainty bar: the spread across questions is not recorded.',
        tone: 'info',
      });

      // 9000 ms is past a second, so the title and the ticks switch to seconds together.
      expect(scaleOf(total.speed.config, 'y').title?.text).toEqual(['Total time for the suite (s)']);
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

      expect(titleLines(scaleOf(suite.cost.config, 'y'))).toEqual(['Candidate cost per suite run', '(USD, 10 questions)']);
      expect(pointsOf(suite.cost.config)[0]['y'] as number)
        .toBeCloseTo(suiteCostUsd(PROFILE_FIXTURE[0]), 10);
      expect(suiteCostUsd(PROFILE_FIXTURE[0])).toBeCloseTo(0.3, 10);

      expect(titleLines(scaleOf(total.cost.config, 'y'))[0]).toContain('Total run cost');
      expect(pointsOf(total.cost.config)[0]['y']).toBe(0.4);
    });

    it('plots each entry\'s own run cost as its suite cost, whatever the context\'s item counts', () => {
      const full = makeEntry({ key: 'full', candidateCostPerQuestionUsd: 0.01, candidateCostPerRunUsd: 0.18 });
      const partial = makeEntry({ key: 'partial', candidateCostPerQuestionUsd: 0.015, candidateCostPerRunUsd: 0.27 });
      const entries = [full, partial];
      const figure = buildSmallMultiples(entries, {
        ...smallMultiplesOptions({ costMeasure: 'candidateSuite' }),
        context: { ...CONTEXT, scoredItemsMin: 12, scoredItemsMax: 18, examItemCount: 18 },
        glyphs: buildIdentityGlyphs(entries),
      });

      expect(suiteCostUsd(full)).toBe(0.18);
      expect(suiteCostUsd(partial)).toBe(0.27);
      const plotted = pointsOf(figure.cost.config).map((point) => point['y'] as number).sort((a, b) => a - b);
      expectClose(plotted, [0.18, 0.27]);
    });

    it('names the shared asked count on the candidate cost title, and drops it when entries differ', () => {
      const asked = buildSmallMultiples(PROFILE_FIXTURE, {
        ...smallMultiplesOptions({ costMeasure: 'candidateSuite' }),
        context: { ...CONTEXT, questionsAskedPerRun: 18 },
      });
      const mixed = buildSmallMultiples(PROFILE_FIXTURE, {
        ...smallMultiplesOptions({ costMeasure: 'candidateSuite' }),
        context: { ...CONTEXT, questionsAskedPerRun: null },
      });

      // Always two lines, the head over the parenthetical, so the title is never clipped.
      expect(titleLines(scaleOf(asked.cost.config, 'y'))).toEqual(['Candidate cost per suite run', '(USD, 18 questions)']);
      expect(titleLines(scaleOf(mixed.cost.config, 'y'))).toEqual(['Candidate cost per suite run', '(USD)']);
    });

    it('formats an averaged asked count without a decimal when it is whole', () => {
      expect(formatQuestionsAsked(1)).toBe('1 question');
      expect(formatQuestionsAsked(18)).toBe('18 questions');
      expect(formatQuestionsAsked(17.5)).toBe('17.5 questions');
    });

    it('scales the per-question SD by the run-to-question cost ratio, and keeps a null SD null', () => {
      expect(suiteCostSdUsd(makeEntry({ key: 'none', candidateCostPerQuestionSdUsd: null }))).toBeNull();
      expect(suiteCostSdUsd(makeEntry({
        key: 'sd',
        candidateCostPerQuestionUsd: 0.01,
        candidateCostPerQuestionSdUsd: 0.002,
        candidateCostPerRunUsd: 0.18,
      }))).toBeCloseTo(0.036, 10);
      expect(suiteCostSdUsd(makeEntry({ key: 'unmeasured', candidateCostPerRunUsd: Number.NaN }))).toBeNull();
      expect(suiteCostSdUsd(makeEntry({ key: 'zero', candidateCostPerQuestionUsd: 0 }))).toBeNull();
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
        expect(panel.config.data.labels?.[0], panel.id).toEqual([once.label, 'n = 1']);
        expect(panel.config.data.labels?.[1], panel.id).toBe(twice.label);
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

    it('colors every bar by provider, never by value, and mutes the rest only when something is emphasised', () => {
      const plain = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions());
      for (const panel of [plain.quality, plain.speed, plain.cost]) {
        const fills = datasetsOf(panel.config)[0]['backgroundColor'] as string[];
        expect(fills, panel.id).toEqual([...CATEGORICAL_PALETTE_DARK]);
      }
      const other = makeEntry({ key: 'other', provider: 'mistral' });
      const gray = buildSmallMultiples([other], { ...smallMultiplesOptions(), glyphs: buildIdentityGlyphs([other]) });
      expect(datasetsOf(gray.quality.config)[0]['borderColor']).toEqual([DE_EMPHASIS_STROKE]);

      const emphasised = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ highlightedKey: 'B' }));
      const emphasisedFills = datasetsOf(emphasised.quality.config)[0]['backgroundColor'] as string[];
      expect(emphasisedFills).toEqual([DE_EMPHASIS_FILL, ACCENT, DE_EMPHASIS_FILL]);
    });

    it('turns the bars horizontal below the stacking breakpoint', () => {
      expect(P1_STACK_BREAKPOINT_PX).toBe(720);
      const horizontal = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ orientation: 'horizontal' }));
      expect(horizontal.quality.config.options?.indexAxis).toBe('y');
      // The value axis moves with the bars, and it still starts at zero.
      expect(scaleOf(horizontal.quality.config, 'x').beginAtZero).toBe(true);
      expect(scaleOf(horizontal.quality.config, 'x').min).toBe(0);
      expect(scaleOf(horizontal.quality.config, 'y').type).toBe('category');
    });

    it('carries no legend box, because each panel plots exactly one series', () => {
      const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions());
      expect(figure.quality.config.options?.plugins?.legend?.display).toBe(false);
    });

    it('states n in every panel\'s badges, and the pricing basis on the cost panel only', () => {
      const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions());
      for (const panel of [figure.quality, figure.speed, figure.cost]) {
        const texts = panel.chrome.badges.map((badge) => badge.text);
        expect(texts.slice(0, 3), panel.id).toEqual(['3 models', '3 runs each', '10 questions']);
        expect(panel.chrome.title, panel.id).toBe(panel.title);
        // The provider key row: one dot per provider present, in its hue.
        expect(panel.chrome.key, panel.id).toEqual([
          { glyph: 'provider', text: 'Google', color: CATEGORICAL_PALETTE_DARK[0] },
          { glyph: 'provider', text: 'Anthropic', color: CATEGORICAL_PALETTE_DARK[1] },
          { glyph: 'provider', text: 'OpenAI', color: CATEGORICAL_PALETTE_DARK[2] },
        ]);
        expect(panel.chrome.highlight, panel.id).toBe('');
      }
      expect(figure.cost.chrome.badges.length).toBe(4);
      expect(figure.cost.chrome.badges[3].tone).toBe('pricing');
      expect(figure.cost.chrome.badges[3].text.startsWith('Catalog prices')).toBe(true);
      expect(figure.cost.chrome.detail).not.toBe('');
      expect(figure.quality.chrome.detail).toBe('');
      expect(figure.speed.chrome.detail).toBe('');
    });

    it('states the scored questions against the exam when fewer than all are scored', () => {
      const figure = buildSmallMultiples(PROFILE_FIXTURE, {
        ...smallMultiplesOptions(),
        context: { ...CONTEXT, scoredItemsMin: 16, scoredItemsMax: 16, examItemCount: 18 },
      });
      const badge = figure.quality.chrome.badges.find((b) => b.kind === 'questions');
      expect(badge?.text).toBe('16 of 18 questions');
      expect(badge?.ariaLabel).toBe('16 of 18 asked questions scored');
    });

    it('gives every panel scriptable value labels, past the SD whisker with a grace margin', () => {
      const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ costMeasure: 'candidateSuite' }));

      for (const panel of [figure.quality, figure.speed, figure.cost]) {
        expect(panel.plugins, panel.id).toContain(ChartDataLabels);
        const datalabels = panel.config.options?.plugins?.datalabels as {
          display?: (ctx: { dataIndex: number }) => boolean;
        };
        expect(typeof datalabels.display, panel.id).toBe('function');
        expect(datalabels.display?.({ dataIndex: 0 }), panel.id).toBe(true);
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

      expect(costDatalabels.display?.({ dataIndex: 0 })).toBe(true);
      expect(costDatalabels.display?.({ dataIndex: 1 })).toBe(false);
    });
  });

  describe('P2 profile scales', () => {
    const SPEED_INDEX = { context: CONTEXT, speedMeasure: 'speedIndex', costMeasure: 'candidateSuite' } as const;

    it('keeps the axis order fixed at Quality, Speed, Cost', () => {
      expect(PROFILE_AXIS_ORDER).toEqual(['quality', 'speed', 'cost']);
      const normalization = normalizeProfile(PROFILE_FIXTURE, SPEED_INDEX);
      expect(normalization.axes.map((a) => a.id)).toEqual(['quality', 'speed', 'cost']);
    });

    it('scales intelligence over the intervals and cost and speed on log axes', () => {
      const normalization = normalizeProfile(PROFILE_FIXTURE, SPEED_INDEX);
      const [quality, speed, cost] = normalization.axes;

      // Quality 30..90 with a ±4 interval each: the domain is the union of the intervals.
      expect(quality.scale).toBe('linear');
      expect(quality.domainMin).toBe(26);
      expect(quality.domainMax).toBe(94);
      // The Speed Index is an index: linear over its values.
      expect(speed.scale).toBe('linear');
      expect(speed.domainMin).toBe(0);
      expect(speed.domainMax).toBe(100);
      // Suite cost $0.10..$0.30 on an inverted log axis padded by 1.25 each way.
      expect(cost.scale).toBe('log');
      expect(cost.lowerIsBetter).toBe(true);
      expect(cost.domainMin).toBeCloseTo(0.1 / 1.25, 10);
      expect(cost.domainMax).toBeCloseTo(0.3 * 1.25, 10);
      expect(cost.min).toBeCloseTo(0.1, 10);
      expect(cost.max).toBeCloseTo(0.3, 10);
      expect(cost.minLabel).toBe('$0.1000');
      expect(cost.maxLabel).toBe('$0.3000');

      const logHeight = (value: number): number =>
        1 - Math.log(value / cost.domainMin) / Math.log(cost.domainMax / cost.domainMin);
      const rows = new Map(normalization.rows.map((r) => [r.key, r.values]));
      expectClose(rows.get('A') ?? [], [64 / 68, 1, logHeight(0.3)]);
      expectClose(rows.get('B') ?? [], [0.5, 0.5, logHeight(0.2)]);
      expectClose(rows.get('C') ?? [], [4 / 68, 0, logHeight(0.1)]);
      // Equal steps are equal ratios: $0.10 to $0.20 is as far as $0.15 to $0.30.
      expect(logHeight(0.1) - logHeight(0.2)).toBeCloseTo(logHeight(0.15) - logHeight(0.3), 10);

      // A latency takes the inverted log axis too: C is the fastest and sits highest.
      const ttft = normalizeProfile(PROFILE_FIXTURE, { ...SPEED_INDEX, speedMeasure: 'ttftP50' });
      expect(ttft.axes[1].scale).toBe('log');
      expect(ttft.axes[1].lowerIsBetter).toBe(true);
      expect(ttft.axes[1].domainMin).toBeCloseTo(100 / 1.25, 10);
      expect(ttft.axes[1].domainMax).toBeCloseTo(500 * 1.25, 10);
      const speeds = ttft.rows.map((row) => row.values[1]);
      expect(speeds[2]).toBeGreaterThan(speeds[1]);
      expect(speeds[1]).toBeGreaterThan(speeds[0]);
    });

    it('keeps the intelligence domain at least 20 points wide, inside 0-100', () => {
      const close = [
        makeEntry({ key: 'x', intelligenceIndex: 50, intelligenceIndexCi95HalfWidth: 4 }),
        makeEntry({ key: 'y', intelligenceIndex: 52, intelligenceIndexCi95HalfWidth: 2 }),
      ];
      const narrow = normalizeProfile(close, SPEED_INDEX).axes[0];
      expect(narrow.domainMax - narrow.domainMin).toBe(20);
      expect(narrow.domainMin).toBe(40);

      const top = [makeEntry({ key: 'z', intelligenceIndex: 95, intelligenceIndexCi95HalfWidth: 10 })];
      const clamped = normalizeProfile(top, SPEED_INDEX).axes[0];
      expect(clamped.domainMin).toBe(80);
      expect(clamped.domainMax).toBe(100);
    });

    it('places every model mid-axis when an axis collapses, rather than at a misleading end', () => {
      const flat = [makeEntry({ key: 'x' }), makeEntry({ key: 'y' })];
      const normalization = normalizeProfile(flat, SPEED_INDEX);
      expectClose(normalization.rows[0].values, [0.5, 0.5, 0.5]);
    });

    it('describes every model by its three values and its weakest axis', () => {
      const normalization = normalizeProfile(PROFILE_FIXTURE, SPEED_INDEX);
      expect(normalization.descriptions.map((d) => d.key)).toEqual(['A', 'B', 'C']);
      // A is the most expensive, which is the lowest of its three heights.
      expect(normalization.descriptions[0].text)
        .toBe('A: intelligence 90, Speed Index 100, cost $0.3000 per suite run; weakest axis: cost');
      expect(normalization.descriptions[2].text).toContain('weakest axis: Speed Index');
    });

    it('lays the tiles out one row up to three, then two, three or four columns', () => {
      expect([1, 2, 3, 4, 5, 6, 7, 8].map(profileColumns)).toEqual([1, 2, 3, 2, 3, 3, 4, 4]);
    });
  });

  describe('P2 model profiles', () => {
    const PROFILE_OPTIONS = { ...BASE_FIGURE_OPTIONS, speedMeasure: 'speedIndex', costMeasure: 'candidateSuite' } as const;

    it('renders one profile tile per model with the other models in gray', () => {
      const profile = buildProfilePlot(PROFILE_FIXTURE, PROFILE_OPTIONS);
      expect(profile.tiles.length).toBe(3);
      expect(profile.columns).toBe(3);
      expect(profile.plugins).toEqual([errorBarPlugin, profileTilePlugin]);

      profileTiles(profile).forEach((tile, index) => {
        const entry = PROFILE_FIXTURE[index];
        const datasets = datasetsOf(tile);
        // The model's own line first, then the Ideal line, then every other model.
        expect(datasets.map((d) => d['label']), entry.key).toEqual([entry.label, PROFILE_IDEAL_LABEL, ...PROFILE_FIXTURE.filter((e) => e !== entry).map((e) => e.label)]);
        expect(datasets[0]['borderColor'], entry.key).toBe(CATEGORICAL_PALETTE_DARK[index]);
        expect(datasets[0]['borderWidth']).toBe(2.5);
        expect(datasets[0]['pointRadius']).toBe(4);
        expect(datasets[0]['pointBorderColor']).toBe(CHART_SURFACE);
        expect(datasets[0]['pointBorderWidth']).toBe(2);
        for (const other of datasets.slice(2)) {
          expect(other['borderColor']).toBe(DE_EMPHASIS_STROKE);
          expect(other['borderWidth']).toBe(1);
          expect(other['pointRadius']).toBe(0);
        }
        // Title in the ink with a provider-color dot; the text never wears the series color.
        const tileOptions = tileOptionsOf(tile);
        expect(tileOptions.title).toBe(entry.label);
        expect(tileOptions.dotColor).toBe(CATEGORICAL_PALETTE_DARK[index]);
        expect(tileOptions.inkColor).toBe(CHART_INK.primary);
      });
    });

    it('draws a dotted Ideal line at the top of every tile, labeled in the first only', () => {
      const profile = buildProfilePlot(PROFILE_FIXTURE, PROFILE_OPTIONS);
      profileTiles(profile).forEach((tile, index) => {
        const ideal = datasetsOf(tile)[1];
        expect(ideal['data']).toEqual([1, 1, 1]);
        expect(ideal['borderDash']).toEqual([...FRONTIER_DASH]);
        expect(ideal['borderCapStyle']).toBe('round');
        expect(ideal['borderColor']).toBe(ACCENT);
        expect(tileOptionsOf(tile).idealLabel).toBe(index === 0 ? PROFILE_IDEAL_LABEL : undefined);
      });
    });

    it('prints the model\'s values on its points and carries the intelligence interval', () => {
      const profile = buildProfilePlot(PROFILE_FIXTURE, PROFILE_OPTIONS);
      const first = profileTiles(profile)[0];
      expect(tileOptionsOf(first).values).toEqual(['90', '100', '$0.3000']);
      const points = pointsOf(first);
      // ±4 index points over the 68-point domain.
      expect(points[0]['yErrHigh'] as number).toBeCloseTo(4 / 68, 10);
      expect(points[0]['yErrLow'] as number).toBeCloseTo(4 / 68, 10);
      expect(points[1]['yErrHigh']).toBeUndefined();
      expect(points[2]['yErrHigh']).toBeUndefined();
    });

    it('draws the tile scales without numeric ticks', () => {
      const tile = profileTiles(buildProfilePlot(PROFILE_FIXTURE, PROFILE_OPTIONS))[0];
      const y = scaleOf(tile, 'y') as ScaleProbe & {
        display?: boolean;
      };
      expect(y.display).toBe(false);
      expect(y.min!).toBeLessThan(0);
      expect(y.max!).toBeGreaterThan(1);
      expect((tile as { data: { labels: unknown[] } }).data.labels)
        .toEqual(['Intelligence', 'Speed Index', ['Cost', '(candidate, suite)']]);
    });

    it('says whether any model is best on every axis, keys its marks and names its scales', () => {
      const profile = buildProfilePlot(PROFILE_FIXTURE, PROFILE_OPTIONS);
      expect(profile.chrome.highlight).toBe('No model is best on every axis.');
      expect(profile.chrome.key.map((item) => item.glyph))
        .toEqual(['ideal', 'other', 'interval', 'provider', 'provider', 'provider']);
      expect(profile.chrome.key[0].text).toBe('Ideal: best on every axis');
      const notes = profile.chrome.notes.map((note) => note.text);
      expect(notes).toContain('Cost uses a log scale: equal steps are equal ratios.');
      expect(notes).toContain('Intelligence 30–90 · Speed Index 0–100 · cost $0.1000–$0.3000 per suite run');
      expect(notes.join(' ')).not.toContain('listed below the plot');
      expect(profile.chrome.badges.some((badge) => badge.tone === 'pricing')).toBe(true);

      const timed = buildProfilePlot(PROFILE_FIXTURE, { ...PROFILE_OPTIONS, speedMeasure: 'meanModelTime' });
      expect(timed.chrome.notes.map((note) => note.text)).toContain('Speed and cost use log scales: equal steps are equal ratios.');

      const dominant = [
        makeEntry({ key: 'top', intelligenceIndex: 90, speedIndex: 90, candidateCostPerRunUsd: 0.1 }),
        makeEntry({ key: 'low', intelligenceIndex: 50, speedIndex: 50, candidateCostPerRunUsd: 0.3 }),
      ];
      const best = buildProfilePlot(dominant, { ...PROFILE_OPTIONS, glyphs: buildIdentityGlyphs(dominant) });
      expect(best.chrome.highlight).toBe('top is best on every axis.');
    });

    it('outlines the emphasised models\' tiles in the accent instead of graying lines', () => {
      const plain = buildProfilePlot(PROFILE_FIXTURE, PROFILE_OPTIONS);
      expect(profileTiles(plain).map((tile) => tileOptionsOf(tile).outlineColor)).toEqual([undefined, undefined, undefined]);

      const emphasised = buildProfilePlot(PROFILE_FIXTURE, { ...PROFILE_OPTIONS, selectedKeys: ['A'], highlightedKey: 'C' });
      expect(profileTiles(emphasised).map((tile) => tileOptionsOf(tile).outlineColor)).toEqual([ACCENT, undefined, ACCENT]);
      // The lines keep their provider hues.
      expect(datasetsOf(profileTiles(emphasised)[1])[0]['borderColor']).toBe(CATEGORICAL_PALETTE_DARK[1]);
    });
  });

  describe('the Pareto frontier', () => {
    it('drops dominated models and joins the members\' own points, least to most favorable x', () => {
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
      // No staircase and no extension to the plot edges.
      expect(result.path).toEqual([
        { x: 500, y: 90 },
        { x: 300, y: 60 },
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
      expect(result.path).toEqual([
        { x: 300, y: 0.01 },
        { x: 100, y: 0.03 },
      ]);
    });

    it('keeps tied models, since neither dominates the other, as one point', () => {
      const result = computeParetoFrontier(
        [
          { key: 'A', x: 100, y: 50 },
          { key: 'B', x: 100, y: 50 },
        ],
        'lower',
        'higher',
      );
      expect(result.frontier.map((c) => c.key)).toEqual(['A', 'B']);
      expect(result.path).toEqual([{ x: 100, y: 50 }]);
    });

    it('returns nothing to draw for an empty set', () => {
      const result = computeParetoFrontier([], 'lower', 'higher');
      expect(result.frontier).toEqual([]);
      expect(result.path).toEqual([]);
    });

    it('gives a one-member frontier a single point, which draws no line', () => {
      const result = computeParetoFrontier(
        [
          { key: 'A', x: 100, y: 50 },
          { key: 'B', x: 200, y: 40 },
        ],
        'lower',
        'higher',
      );
      expect(result.frontier.map((c) => c.key)).toEqual(['A']);
      expect(result.path).toEqual([{ x: 100, y: 50 }]);
    });

    it('draws the frontier as dotted straight segments through its members only', () => {
      const entries = [
        makeEntry({ key: 'slow', provider: 'google', intelligenceIndex: 90, ttftP50Ms: 500 }),
        makeEntry({ key: 'mid', provider: 'anthropic', intelligenceIndex: 60, ttftP50Ms: 300 }),
        makeEntry({ key: 'beaten', provider: 'openai', intelligenceIndex: 50, ttftP50Ms: 400 }),
        makeEntry({ key: 'fast', provider: 'openai', intelligenceIndex: 30, ttftP50Ms: 100 }),
      ];
      const spec = buildQualitySpeedScatter(entries, {
        ...BASE_FIGURE_OPTIONS,
        glyphs: buildIdentityGlyphs(entries),
        speedMeasure: 'ttftP50',
      });
      const datasets = datasetsOf(spec.config);
      const frontier = datasets[entries.length];
      expect(frontier['label']).toBe('Pareto frontier');
      expect(frontier['data']).toEqual([
        { x: 500, y: 90 },
        { x: 300, y: 60 },
        { x: 100, y: 30 },
      ]);
      expect(frontier['showLine']).toBe(true);
      expect(frontier['borderDash']).toEqual([0, 6]);
      expect(frontier['borderCapStyle']).toBe('round');
      expect(frontier['borderWidth']).toBe(DEFAULT_FIGURE_STYLE.scatter.frontierWidthPx);
      expect(frontier['borderColor']).toBe(CHART_INK.secondary);
      // The lowest order draws last, so the dots pass over the marks' surface rings.
      expect(frontier['order']).toBeLessThan(0);
      expect(spec.chrome.highlight).toBe('Best trade-offs: slow, mid, fast');
    });

    it('hides the frontier line and its key item on request, and keeps the fading and the highlight', () => {
      const entries = [
        makeEntry({ key: 'slow', provider: 'google', intelligenceIndex: 90, ttftP50Ms: 500 }),
        makeEntry({ key: 'mid', provider: 'anthropic', intelligenceIndex: 60, ttftP50Ms: 300 }),
        makeEntry({ key: 'beaten', provider: 'openai', intelligenceIndex: 50, ttftP50Ms: 400 }),
        makeEntry({ key: 'fast', provider: 'openai', intelligenceIndex: 30, ttftP50Ms: 100 }),
      ];
      const build = (frontierLine: boolean) => buildQualitySpeedScatter(entries, {
        ...BASE_FIGURE_OPTIONS,
        glyphs: buildIdentityGlyphs(entries),
        speedMeasure: 'ttftP50',
        style: { ...DEFAULT_FIGURE_STYLE, scatter: { ...DEFAULT_FIGURE_STYLE.scatter, frontierLine } },
      });
      const shown = build(true);
      const hidden = build(false);

      expect(datasetsOf(shown.config).some((d) => d['label'] === 'Pareto frontier')).toBe(true);
      expect(shown.chrome.key.map((item) => item.glyph)).toContain('frontier');

      expect(datasetsOf(hidden.config).some((d) => d['label'] === 'Pareto frontier')).toBe(false);
      expect(datasetsOf(hidden.config).length).toBe(entries.length);
      expect(hidden.chrome.key.map((item) => item.glyph)).not.toContain('frontier');
      expect(hidden.chrome.key.map((item) => item.glyph)).toContain('faded');
      expect(datasetsOf(hidden.config)[2]['backgroundColor']).toBe(datasetsOf(shown.config)[2]['backgroundColor']);
      expect(hidden.chrome.highlight).toBe(shown.chrome.highlight);
      expect(hidden.summary).toEqual(shown.summary);
    });

    it('fades a model another beats on both axes', () => {
      const entries = [
        makeEntry({ key: 'best', provider: 'google', intelligenceIndex: 80, ttftP50Ms: 200 }),
        makeEntry({ key: 'beaten', provider: 'anthropic', intelligenceIndex: 60, ttftP50Ms: 900, runCount: 3 }),
        makeEntry({ key: 'fast', provider: 'openai', intelligenceIndex: 40, ttftP50Ms: 100 }),
      ];
      const spec = buildQualitySpeedScatter(entries, {
        ...BASE_FIGURE_OPTIONS,
        glyphs: buildIdentityGlyphs(entries),
        speedMeasure: 'ttftP50',
        directLabels: true,
      });
      const datasets = datasetsOf(spec.config);
      // 35 % of the provider hue, on the fill, the ring and the whiskers alike.
      expect(datasets[1]['backgroundColor']).toBe('rgba(217, 89, 38, 0.35)');
      expect(datasets[1]['borderColor']).toBe('rgba(11, 11, 11, 0.35)');
      expect(pointsOf(spec.config, 1)[0]['alpha']).toBe(0.35);
      expect(datasets[0]['backgroundColor']).toBe(CATEGORICAL_PALETTE_DARK[0]);
      expect(pointsOf(spec.config, 0)[0]['alpha']).toBeUndefined();

      const blocks = (spec.config.options?.plugins as unknown as Record<string, DirectLabelPluginOptions>)[directLabelPlugin.id].blocks;
      expect(blocks.map((block) => block.muted ?? false)).toEqual([false, true, false]);
      expect(spec.summary).toEqual(['Pareto frontier: best, fast', 'Faded: beaten']);
    });

    it('colors every mark by provider and keys the providers present', () => {
      const entries = [
        makeEntry({ key: 'g', provider: 'Google', intelligenceIndex: 90, ttftP50Ms: 500 }),
        makeEntry({ key: 'o', provider: 'OpenAI', intelligenceIndex: 60, ttftP50Ms: 300 }),
        makeEntry({ key: 'x', provider: 'mistral', intelligenceIndex: 30, ttftP50Ms: 100 }),
      ];
      const spec = buildQualitySpeedScatter(entries, {
        ...BASE_FIGURE_OPTIONS,
        glyphs: buildIdentityGlyphs(entries),
        speedMeasure: 'ttftP50',
      });
      const datasets = datasetsOf(spec.config).slice(0, entries.length);
      expect(datasets.map((d) => d['backgroundColor'])).toEqual([CATEGORICAL_PALETTE_DARK[0], CATEGORICAL_PALETTE_DARK[2], DE_EMPHASIS_STROKE]);
      expect(datasets.every((d) => d['pointStyle'] === 'circle')).toBe(true);
      expect(spec.chrome.key.filter((item) => item.glyph === 'provider')).toEqual([
        { glyph: 'provider', text: 'Google', color: CATEGORICAL_PALETTE_DARK[0] },
        { glyph: 'provider', text: 'OpenAI', color: CATEGORICAL_PALETTE_DARK[2] },
        { glyph: 'provider', text: 'Other', color: DE_EMPHASIS_STROKE },
      ]);

      // The Highlight emphasis still overrides: the highlighted mark wears the accent ring.
      const highlighted = buildQualitySpeedScatter(entries, {
        ...BASE_FIGURE_OPTIONS,
        glyphs: buildIdentityGlyphs(entries),
        speedMeasure: 'ttftP50',
        highlightedKey: 'o',
      });
      expect(datasetsOf(highlighted.config)[1]['borderColor']).toBe(ACCENT);
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
        candidateCostPerRunUsd: 0.0756,
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
        candidateCostPerRunUsd: 0.0432,
        totalModelTimeSdMs: null,
        totalRunCostSdUsd: null,
      }),
    ];
    const SCREENSHOT_CONTEXT: ModelComparisonContext = {
      ...CONTEXT,
      scoredItemsMin: 18,
      scoredItemsMax: 18,
      examItemCount: 18,
      questionsAskedPerRun: 18,
    };
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
          expect((value - scale.min!) / span, `${entry.label} on ${axis}`).toBeGreaterThanOrEqual(0.05);
          expect((scale.max! - value) / span, `${entry.label} on ${axis}`).toBeGreaterThanOrEqual(0.05);
        }
      }
    });

    it('draws no line for S1\'s one-member frontier, says it is best on both axes, and flags the overlapping intervals', () => {
      const spec = buildQualitySpeedScatter(SCREENSHOT, SCREENSHOT_OPTIONS);
      const frontier = datasetsOf(spec.config).find((d) => d['label'] === 'Pareto frontier');

      expect(frontier).toBeUndefined();
      expect(spec.chrome.highlight).toBe('GPT-5.6 Luna is best on both axes');
      expect(spec.chrome.notes).toContainEqual({
        text: 'Some differences are within the 95 % intervals, so treat the frontier as indicative rather than a clear win.',
        tone: 'info',
      });
      // GPT-6 Luna is faded; the fixture carries no provider, so the key's one provider is Other.
      expect(spec.chrome.key.map((item) => item.glyph)).toEqual(['hollow', 'interval', 'faded', 'provider']);
      expect(spec.summary).toEqual(['Pareto frontier: GPT-5.6 Luna', 'Faded: GPT-6 Luna']);
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

    it('puts the direction on the scatters and the bars, never on the profile and never in a badge', () => {
      const figures = buildComparisonFigures(SCREENSHOT, { context: SCREENSHOT_CONTEXT });
      const { quality, speed, cost } = figures.smallMultiples;
      const directed = [quality, speed, cost, figures.qualitySpeed, figures.qualityCost, figures.speedCost];
      for (const figure of [...directed, figures.profile]) {
        expect(figure.chrome.badges.some((badge) => badge.text.includes('Better')), figure.id).toBe(false);
      }
      expect(figures.profile.chrome.direction).toBeUndefined();
      for (const figure of directed) {
        expect(figure.chrome.direction, figure.id).toBeDefined();
      }
      expect(quality.chrome.direction).toEqual({ y: 'top', label: 'Better' });
      // Mean time per question, the default speed measure, is better lower.
      expect(speed.chrome.direction).toEqual({ y: 'bottom', label: 'Better' });
      expect(cost.chrome.direction).toEqual({ y: 'bottom', label: 'Better' });
    });

    it('puts the pricing badge on S2, S3, P1 cost and P2 only', () => {
      const figures = buildComparisonFigures(SCREENSHOT, { context: SCREENSHOT_CONTEXT });
      const priced = (spec: { chrome: { badges: readonly { tone: string }[] } }): boolean =>
        spec.chrome.badges.some((badge) => badge.tone === 'pricing');

      expect(priced(figures.qualitySpeed)).toBe(false);
      expect(priced(figures.qualityCost)).toBe(true);
      expect(priced(figures.speedCost)).toBe(true);
      expect(priced(figures.smallMultiples.quality)).toBe(false);
      expect(priced(figures.smallMultiples.speed)).toBe(false);
      expect(priced(figures.smallMultiples.cost)).toBe(true);
      expect(priced(figures.profile)).toBe(true);

      const badge = figures.qualityCost.chrome.badges.find((b) => b.tone === 'pricing');
      expect(badge?.text.startsWith('Catalog prices')).toBe(true);
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
        for (const spec of allFigures(set)) {
          expect(spec.chrome.title, spec.id).toBe(spec.title);
          for (const note of spec.chrome.notes) {
            expect(note.text, spec.id).not.toContain('DTO');
          }
        }
      }
    });
  });

  describe('reduced motion', () => {
    it('turns animation off outright rather than merely shortening it', () => {
      const still = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, reducedMotion: true });
      expect(still.config.options?.animation).toBe(false);

      const moving = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, reducedMotion: false });
      expect(moving.config.options?.animation).toEqual({ duration: 300 });
    });

    it('applies to every figure, not only the scatters', () => {
      const figures = buildComparisonFigures(PROFILE_FIXTURE, { context: CONTEXT, reducedMotion: true });
      expect(figures.smallMultiples.quality.config.options?.animation).toBe(false);
      expect(figures.smallMultiples.speed.config.options?.animation).toBe(false);
      expect(figures.smallMultiples.cost.config.options?.animation).toBe(false);
      for (const tile of figures.profile.tiles) {
        expect(tile.options?.animation).toBe(false);
      }
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

      expect(watcher.matches).toBe(false);
      expect(listeners.length).toBe(1);

      query.matches = true;
      listeners[0]({ matches: true } as MediaQueryListEvent);
      expect(seen).toEqual([true]);
      expect(watcher.matches).toBe(true);

      unsubscribe();
      listeners[0]({ matches: false } as MediaQueryListEvent);
      expect(seen).toEqual([true]);

      watcher.dispose();
      expect(listeners.length).toBe(0);
    });

    it('reports no preference when the environment has no matchMedia', () => {
      const watcher = new ReducedMotionWatcher(() => null);
      expect(watcher.matches).toBe(false);
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
        expect(box.x, box.key).toBeGreaterThanOrEqual(AREA.left);
        expect(box.y, box.key).toBeGreaterThanOrEqual(AREA.top);
        expect(box.x + box.width, box.key).toBeLessThanOrEqual(AREA.right);
        expect(box.y + box.height, box.key).toBeLessThanOrEqual(AREA.bottom);
      }
      for (let i = 0; i < boxes.length; i += 1) {
        for (let j = i + 1; j < boxes.length; j += 1) {
          expect(rectsOverlap(boxes[i], boxes[j]), `${boxes[i].key} over ${boxes[j].key}`).toBe(false);
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
          expect(distance, `${box.key} over mark ${mark.key}`).toBeGreaterThanOrEqual(9);
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
        expect(box.x, box.key).toBeGreaterThanOrEqual(tight.left);
        expect(box.y, box.key).toBeGreaterThanOrEqual(tight.top);
        expect(box.x + box.width, box.key).toBeLessThanOrEqual(tight.right);
        expect(box.y + box.height, box.key).toBeLessThanOrEqual(tight.bottom);
      }
    });

    it('moves a label off a supplied whisker rect when a clear candidate exists', () => {
      const anchors = [anchor('A', 100, 100)];
      const whisker = { left: 110, top: 88, right: 200, bottom: 112 };

      const unobstructed = placeDirectLabels(anchors, AREA, 9);
      const avoiding = placeDirectLabels(anchors, AREA, 9, { rects: [whisker] });

      // With nothing in the way the placer takes the right-hand candidate, which lies on the whisker.
      expect(unobstructed[0].x).toBeGreaterThan(100);
      expect(boxOverlapsRect(unobstructed[0], whisker)).toBe(true);

      expect(boxOverlapsRect(avoiding[0], whisker)).toBe(false);
      expect(avoiding[0].x + avoiding[0].width).toBeLessThanOrEqual(100);
    });

    it('does not lay a label across a supplied frontier polyline when a clear candidate exists', () => {
      const frontier = [{ x: 120, y: 80 }, { x: 120, y: 200 }];
      const boxes = placeDirectLabels([anchor('A', 100, 100)], AREA, 9, { polylines: [frontier] });

      expect(boxes.length).toBe(1);
      expect(segmentIntersectsRect(frontier[0], frontier[1], boxEdges(boxes[0]))).toBe(false);
    });

    it('places the label of a mark 10 px from the right edge to its left', () => {
      const boxes = placeDirectLabels([anchor('A', AREA.right - 10, 150)], AREA, 9);

      expect(boxes.length).toBe(1);
      expect(boxes[0].x + boxes[0].width).toBeLessThanOrEqual(AREA.right - 10);
    });

    describe('segment geometry', () => {
      it('separates crossing, collinear-overlapping and disjoint segments', () => {
        expect(segmentsIntersect({ x: 0, y: 0 }, { x: 10, y: 10 }, { x: 0, y: 10 }, { x: 10, y: 0 })).toBe(true);
        expect(segmentsIntersect({ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 5, y: 0 }, { x: 15, y: 0 })).toBe(true);
        expect(segmentsIntersect({ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 0, y: 5 }, { x: 10, y: 5 })).toBe(false);
        expect(segmentsIntersect({ x: 0, y: 0 }, { x: 1, y: 1 }, { x: 5, y: 5 }, { x: 6, y: 6 })).toBe(false);
      });

      it('treats a rect as filled, so an endpoint inside it counts as much as an edge crossed', () => {
        const rect = { left: 0, top: 0, right: 10, bottom: 10 };

        expect(segmentIntersectsRect({ x: -5, y: 5 }, { x: 15, y: 5 }, rect)).toBe(true);
        expect(segmentIntersectsRect({ x: 2, y: 2 }, { x: 3, y: 3 }, rect)).toBe(true);
        expect(segmentIntersectsRect({ x: -5, y: -5 }, { x: 15, y: -5 }, rect)).toBe(false);
        expect(segmentIntersectsRect({ x: 20, y: 20 }, { x: 30, y: 30 }, rect)).toBe(false);
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
    ): { calls: DirectCall[]; fills: string[]; rectFills: string[]; halos: { color: string; width: number }[] } {
      const calls: DirectCall[] = [];
      const fills: string[] = [];
      const rectFills: string[] = [];
      const halos: { color: string; width: number }[] = [];
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
        strokeText: (...args: unknown[]) => {
          halos.push({ color: ctx.strokeStyle, width: ctx.lineWidth });
          calls.push({ op: 'strokeText', args });
        },
        measureText: (text: string) => ({ width: text.length * 6 }),
        strokeStyle: '',
        fillStyle: '',
        globalAlpha: 1,
        lineWidth: 0,
        lineJoin: '',
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
      return { calls, fills, rectFills, halos };
    }

    it('draws a name-only label as text with a 3 px surface halo, keeping the hue rule and no plate', () => {
      const { calls, rectFills, halos } = runDirectPlugin([block('Model A', [], '#d95926')], [{ x: 100, y: 100 }]);

      // The hue rule alone: no backing plate behind a name.
      expect(rectFills).toEqual(['#d95926']);
      expect(halos).toEqual([{ color: CHART_SURFACE, width: 6 }]);
      const order = calls.filter((c) => c.op === 'strokeText' || c.op === 'fillText').map((c) => c.op);
      expect(order).toEqual(['strokeText', 'fillText']);
    });

    it('writes a faded model\'s name in the muted ink', () => {
      const { fills } = runDirectPlugin(
        [{ name: 'Model A', values: [], hue: '#3987e5', muted: true }],
        [{ x: 100, y: 100 }],
      );
      expect(fills).toEqual([CHART_INK.muted]);
    });

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
      expect(off.config.options?.plugins?.legend?.display).toBe(true);

      const named = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, directLabels: true });
      expect(named.plugins).toContain(directLabelPlugin);
      expect(named.config.options?.plugins?.legend?.display).toBe(false);
      expect(pluginOptions(named).blocks.map((b) => b.name)).toEqual(PROFILE_FIXTURE.map((e) => e.label));
      expect(pluginOptions(named).blocks.every((b) => b.values.length === 0)).toBe(true);

      // Values are about the marks' numbers, not their names, so the legend stays.
      const valued = buildQualitySpeedScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, inlineValues: true });
      expect(valued.plugins).toContain(directLabelPlugin);
      expect(valued.config.options?.plugins?.legend?.display).toBe(true);
      expect(pluginOptions(valued).blocks.map((b) => b.name)).toEqual([undefined, undefined, undefined]);
      expect(pluginOptions(valued).blocks[0].values).toEqual([
        // 900 ms on an axis whose padded domain passes 1000 ms, so the plate follows the axis into seconds.
        { label: 'Mean time', text: '0.9 s' },
        { label: 'Intelligence', text: '90' },
      ]);
      expect(pluginOptions(valued).blocks[0].hue).toBe(glyphFor(BASE_FIGURE_OPTIONS.glyphs, 'A').hue);

      const both = buildQualitySpeedScatter(PROFILE_FIXTURE, {
        ...BASE_FIGURE_OPTIONS,
        directLabels: true,
        inlineValues: true,
      });
      expect(both.config.options?.plugins?.legend?.display).toBe(false);
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
      expect(calls.some((c) => c.op === 'fillText')).toBe(false);
      expect(calls.some((c) => c.op === 'lineTo')).toBe(false);
    });

    it('ignores plain numeric data, so it is inert on the profile plot', () => {
      const calls = runPlugin([0.5], { x: makeScale('linear'), y: makeScale('linear') });
      expect(calls.some((c) => c.op === 'lineTo')).toBe(false);
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

      expect(scaleOf(figures.smallMultiples.speed.config, 'y').title?.text).toEqual(['Mean time per question (ms)']);
      expect(titleLines(scaleOf(figures.qualitySpeed.config, 'x'))[0]).toContain('Mean time per question');
      expect(titleLines(scaleOf(figures.speedCost.config, 'x'))[0]).toContain('Mean time per question');
      expect(figures.profile.tiles[0].data.labels).toContainEqual(['Speed', '(mean model time)']);
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
      expect(speedLowerIsBetter('meanModelTime')).toBe(true);
      expect(speedLowerIsBetter('totalModelTime')).toBe(true);
      expect(speedLowerIsBetter('ttftP50')).toBe(true);
      expect(speedLowerIsBetter('speedIndex')).toBe(false);
    });
  });

  describe('figure style', () => {
    const style = (
      bar: Partial<BarFigureStyle> = {},
      scatter: Partial<ScatterFigureStyle> = {},
      profile: Partial<ProfileFigureStyle> = {},
    ): FigureStyle => ({
      bar: { ...DEFAULT_FIGURE_STYLE.bar, ...bar },
      scatter: { ...DEFAULT_FIGURE_STYLE.scatter, ...scatter },
      profile: { ...DEFAULT_FIGURE_STYLE.profile, ...profile },
      numbers: DEFAULT_FIGURE_STYLE.numbers,
      appearance: DEFAULT_FIGURE_STYLE.appearance,
      table: DEFAULT_FIGURE_STYLE.table,
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
        expect(value.grid?.display).toBe(true);
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
        expect('maxBarThickness' in dataset).toBe(false);
        expect(dataset['borderRadius']).toBe(0);
        expect(dataset['borderWidth']).toBe(3);
      });

      it('draws single-run bars as outlines by default and solid when filledBars is on', () => {
        const entries = [
          makeEntry({ key: 'once', runCount: 1, provider: 'google' }),
          makeEntry({ key: 'thrice', runCount: 3, provider: 'google' }),
        ];
        const hue = CATEGORICAL_PALETTE_DARK[0];
        const quality = (options: Partial<SmallMultiplesOptions>) =>
          datasetsOf(buildSmallMultiples(entries, smallMultiplesOptions(options)).quality.config)[0];

        const outlined = quality({});
        expect(outlined['backgroundColor']).toEqual(['transparent', hue]);

        const filled = quality({ style: style({ filledBars: true }) });
        expect(filled['backgroundColor']).toEqual([hue, hue]);
        expect(filled['borderColor']).toEqual(outlined['borderColor']);

        const emphasisedOutlined = quality({ highlightedKey: 'once' });
        expect(emphasisedOutlined['backgroundColor']).toEqual(['transparent', DE_EMPHASIS_FILL]);
        const emphasisedFilled = quality({ style: style({ filledBars: true }), highlightedKey: 'once' });
        expect(emphasisedFilled['backgroundColor']).toEqual([ACCENT, DE_EMPHASIS_FILL]);
        expect(emphasisedFilled['borderColor']).toEqual(emphasisedOutlined['borderColor']);
      });

      it('hides the value labels and the value-axis grid on request, and sizes the text', () => {
        const figure = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({
          style: style({ valueLabels: false, gridlines: false, axisTextSizePx: 16, axisTitleSizePx: 20, valueLabelSizePx: 18 }),
        }));
        const datalabels = figure.quality.config.options?.plugins?.datalabels as {
          display?: unknown;
          font?: {
            size?: number;
        };
        };
        expect(datalabels.display).toBe(false);
        expect(datalabels.font?.size).toBe(18);
        const value = scaleOf(figure.quality.config, 'y') as ScaleProbe & {
          ticks?: {
            font?: {
              size?: number;
        };
          };
          title?: {
            font?: {
              size?: number;
            };
          };
          grid?: {
            display?: boolean;
          };
        };
        expect(value.grid?.display).toBe(false);
        expect(value.ticks?.font?.size).toBe(16);
        expect(value.title?.font?.size).toBe(20);
        const category = scaleOf(figure.quality.config, 'x') as ScaleProbe & {
          ticks?: { font?: { size?: number } };
          title?: { font?: { size?: number } };
        };
        expect(category.ticks?.font?.size).toBe(16);
        expect(category.title?.font?.size).toBe(20);
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
          expect(datalabels.offset({ dataIndex: index, chart }), String(index)).toBe(4);
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
        expect(warning(noteTexts(on.speed))).toBe(true);

        for (const intervals of [true, false]) {
          const off = buildSmallMultiples(degraded, options(style({ intervals, meanTimeNoIntervalNote: false })));
          expect(noteTexts(off.speed), `intervals ${intervals}`).not.toContain(MEAN_TIME_NO_INTERVAL_NOTE);
          expect(warning(noteTexts(off.speed)), `intervals ${intervals}`).toBe(true);
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
          style: style({}, {
            markRadiusPx: 10,
            frontierWidthPx: 4,
            legendPosition: 'right',
            axisTextSizePx: 16,
            axisTitleSizePx: 22,
            gridlines: false,
          }),
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
        expect(x.title?.font?.size).toBe(22);
        expect(x.grid?.display).toBe(false);

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
          expect(scaleOf(hidden.config, axis).min, axis).toBe(scaleOf(bare.config, axis).min!);
          expect(scaleOf(hidden.config, axis).max, axis).toBe(scaleOf(bare.config, axis).max!);
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
        expect(pluginOptions.avoidWhiskers).toBe(false);
        expect(pluginOptions.fontSizePx).toBe(14);
        expect(pluginOptions.markRadiusPx).toBe(8);
        const defaultLabelled = buildQualityCostScatter(PROFILE_FIXTURE, { ...BASE_FIGURE_OPTIONS, directLabels: true });
        expect((defaultLabelled.config.options?.plugins as Record<string, DirectLabelPluginOptions>)[directLabelPlugin.id].avoidWhiskers).toBe(true);
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
          expect(noteTexts(spec), `intervals ${intervals}`).not.toContain(FRONTIER_UNCERTAINTY_NOTE);
          expect(spec.chrome.notes.some((note) => note.tone === 'warning'), `intervals ${intervals}`).toBe(true);
        }
        const silent = buildQualitySpeedScatter(WITHIN, withinOptions(style({}, {
          intervals: false,
          hiddenIntervalsNote: false,
          frontierIntervalsNote: false,
        })));
        expect(silent.chrome.notes.map((note) => note.tone)).toEqual(['warning']);
      });
    });

    describe('the Better badge', () => {
      const valueScaleOf = (spec: { config: unknown }, orientation: 'vertical' | 'horizontal'): ScaleProbe =>
        scaleOf(spec.config, orientation === 'vertical' ? 'y' : 'x');

      it('points each bar panel along its value axis toward better, in both orientations', () => {
        const vertical = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ speedMeasure: 'meanModelTime' }));
        expect(vertical.quality.chrome.direction).toEqual({ y: 'top', label: 'Better' });
        expect(vertical.speed.chrome.direction).toEqual({ y: 'bottom', label: 'Better' });
        expect(vertical.cost.chrome.direction).toEqual({ y: 'bottom', label: 'Better' });

        const horizontal = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({
          speedMeasure: 'speedIndex',
          orientation: 'horizontal',
        }));
        expect(horizontal.quality.chrome.direction).toEqual({ x: 'right', label: 'Better' });
        expect(horizontal.speed.chrome.direction).toEqual({ x: 'right', label: 'Better' });
        expect(horizontal.cost.chrome.direction).toEqual({ x: 'left', label: 'Better' });

        // The arrow follows the axis as drawn: no bar value axis is reversed.
        for (const [figure, orientation] of [[vertical, 'vertical'], [horizontal, 'horizontal']] as const) {
          for (const panel of [figure.quality, figure.speed, figure.cost]) {
            expect((valueScaleOf(panel, orientation) as {
              reverse?: boolean;
            }).reverse, panel.id).toBeUndefined();
          }
        }
      });

      it('drops the bar value-axis "is better" line while the badge shows, and brings it back when hidden', () => {
        const shown = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({ speedMeasure: 'meanModelTime' }));
        const hidden = buildSmallMultiples(PROFILE_FIXTURE, smallMultiplesOptions({
          speedMeasure: 'meanModelTime',
          style: style({ hiddenBadges: ['direction'] }),
        }));

        expect(titleLines(scaleOf(shown.quality.config, 'y'))).toEqual(['Intelligence Index (0-100)']);
        expect(titleLines(scaleOf(shown.speed.config, 'y'))).toEqual(['Mean time per question (ms)']);
        expect(titleLines(scaleOf(hidden.quality.config, 'y'))).toEqual(['Intelligence Index (0-100)', 'higher is better']);
        expect(titleLines(scaleOf(hidden.speed.config, 'y'))).toEqual(['Mean time per question (ms)', 'lower is better']);
        expect(titleLines(scaleOf(hidden.cost.config, 'y'))).toEqual(['Candidate cost per suite run', '(USD, 10 questions)', 'lower is better']);
        for (const panel of [hidden.quality, hidden.speed, hidden.cost]) {
          expect(panel.chrome.direction, panel.id).toBeUndefined();
        }
      });

      it('keeps both scatter axis titles on two lines, with the badge shown or hidden', () => {
        for (const hiddenBadges of [[], ['direction']] as const) {
          const spec = buildQualitySpeedScatter(PROFILE_FIXTURE, {
            ...BASE_FIGURE_OPTIONS,
            style: style({}, { hiddenBadges: [...hiddenBadges] }),
          });
          expect(titleLines(scaleOf(spec.config, 'x'))[1], `${hiddenBadges}`).toBe('lower is better');
          expect(titleLines(scaleOf(spec.config, 'y'))[1], `${hiddenBadges}`).toBe('higher is better');
          expect(spec.chrome.direction, `${hiddenBadges}`).toEqual(hiddenBadges.length === 0 ? { x: 'left', y: 'top', label: 'Better' } : undefined);
        }
      });

      it('leaves the plot of a scatter alone: no reserved top padding and no marker plugin', () => {
        const figures = buildComparisonFigures(PROFILE_FIXTURE, { context: CONTEXT });
        for (const spec of [figures.qualitySpeed, figures.qualityCost, figures.speedCost]) {
          const layout = (spec.config.options as {
            layout?: {
              padding?: {
                top?: number;
              };
            };
          }).layout;
          expect(layout?.padding?.top, spec.id).toBeUndefined();
          expect(Object.keys(spec.config.options?.plugins ?? {}), spec.id).not.toContain('overseerDirectionMarker');
        }
        expect(figures.qualitySpeed.plugins).toEqual([errorBarPlugin]);
      });

      it('hides only its own family\'s badge', () => {
        const figures = buildComparisonFigures(PROFILE_FIXTURE, {
          context: CONTEXT,
          style: style({}, { hiddenBadges: ['direction'] }),
        });
        for (const spec of [figures.qualitySpeed, figures.qualityCost, figures.speedCost]) {
          expect(spec.chrome.direction, spec.id).toBeUndefined();
        }
        for (const spec of [figures.smallMultiples.quality, figures.smallMultiples.speed, figures.smallMultiples.cost]) {
          expect(spec.chrome.direction, spec.id).toBeDefined();
        }
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

    describe('the n = 1 marker and the badges', () => {
      const PRICED_IDS = ['s2-quality-cost', 's3-speed-cost', 'p1c-cost', 'p2-profile'];
      const SCATTER_IDS = ['s1-quality-speed', 's2-quality-cost', 's3-speed-cost'];
      const BAR_IDS = ['p1a-quality', 'p1b-speed', 'p1c-cost'];

      const kindsOf = (spec: { chrome: ChartSpec['chrome'] }): (string | undefined)[] => spec.chrome.badges.map((badge) => badge.kind);

      it('leaves a single-run model with its plain name when the marker is off', () => {
        const once = makeEntry({ key: 'once', runCount: 1, candidateCostPerQuestionSdUsd: null });
        const twice = makeEntry({ key: 'twice', runCount: 2 });
        const entries = [once, twice];
        const figure = buildSmallMultiples(entries, {
          ...smallMultiplesOptions({ style: style({ singleRunMarker: false }) }),
          glyphs: buildIdentityGlyphs(entries),
        });
        for (const panel of [figure.quality, figure.speed, figure.cost]) {
          expect(panel.config.data.labels?.[0], panel.id).toBe(once.label);
          expect(panel.config.data.labels?.[1], panel.id).toBe(twice.label);
        }
      });

      it('gives every badge a kind, with the pricing badge exactly where a cost axis is', () => {
        const figures = buildComparisonFigures(PROFILE_FIXTURE, { context: CONTEXT });
        for (const spec of allFigures(figures)) {
          const expected = PRICED_IDS.includes(spec.id)
            ? ['models', 'runs', 'questions', 'pricing']
            : ['models', 'runs', 'questions'];
          expect(kindsOf(spec), spec.id).toEqual(expected);
          for (const badge of spec.chrome.badges) {
            expect(badge.kind === 'pricing', `${spec.id} ${badge.text}`).toBe(badge.tone === 'pricing');
          }
        }
      });

      it('hides a bar badge on the three bar panels only, and keeps the pricing sentence', () => {
        const plain = buildComparisonFigures(PROFILE_FIXTURE, { context: CONTEXT });
        const figures = buildComparisonFigures(PROFILE_FIXTURE, {
          context: CONTEXT,
          style: style({ hiddenBadges: ['runs', 'pricing'] }),
        });
        const plainById = new Map(allFigures(plain).map((spec) => [spec.id, spec]));
        for (const spec of allFigures(figures)) {
          if (BAR_IDS.includes(spec.id)) {
            expect(kindsOf(spec), spec.id).toEqual(['models', 'questions']);
          }
          else {
            expect(spec.chrome.badges, spec.id).toEqual(plainById.get(spec.id)!.chrome.badges);
          }
        }
        expect(figures.smallMultiples.cost.chrome.detail).toBe(plain.smallMultiples.cost.chrome.detail);
        expect(figures.smallMultiples.cost.chrome.detail).not.toBe('');
      });

      it('hides a trade-off badge on the three scatters only', () => {
        const plain = buildComparisonFigures(PROFILE_FIXTURE, { context: CONTEXT });
        const figures = buildComparisonFigures(PROFILE_FIXTURE, {
          context: CONTEXT,
          style: style({}, { hiddenBadges: ['models', 'pricing'] }),
        });
        const plainById = new Map(allFigures(plain).map((spec) => [spec.id, spec]));
        for (const spec of allFigures(figures)) {
          if (SCATTER_IDS.includes(spec.id)) {
            expect(kindsOf(spec), spec.id).toEqual(['runs', 'questions']);
          }
          else {
            expect(spec.chrome.badges, spec.id).toEqual(plainById.get(spec.id)!.chrome.badges);
          }
        }
      });

      it('hides a profile badge on the profile only', () => {
        const plain = buildComparisonFigures(PROFILE_FIXTURE, { context: CONTEXT });
        const figures = buildComparisonFigures(PROFILE_FIXTURE, {
          context: CONTEXT,
          style: style({}, {}, { hiddenBadges: ['questions'] }),
        });
        const plainById = new Map(allFigures(plain).map((spec) => [spec.id, spec]));
        for (const spec of allFigures(figures)) {
          if (spec.id === 'p2-profile') {
            expect(kindsOf(spec)).toEqual(['models', 'runs', 'pricing']);
            expect(spec.chrome.detail).toBe(plainById.get(spec.id)!.chrome.detail);
          }
          else {
            expect(spec.chrome.badges, spec.id).toEqual(plainById.get(spec.id)!.chrome.badges);
          }
        }
      });
    });

    describe('the thinking level line', () => {
      const leveled = (key: string, name: string, thinkingLevel: string | null, runCount = 3): ModelComparisonEntry =>
        makeEntry({ key, name, thinkingLevel, label: modelLabelText(name, thinkingLevel), runCount });
      const LEVELED = [
        leveled('luna', 'GPT-5.6 Luna', 'max'),
        leveled('flash', 'Gemini 3.7 Flash', 'high'),
        leveled('plain', 'Plain Model', null),
      ];

      it('composes the one-line label and breaks it only when asked and a level exists', () => {
        expect(modelLabelText('GPT-5.6 Luna', 'max')).toBe('GPT-5.6 Luna (max)');
        expect(modelLabelText('GPT-5.6 Luna', null)).toBe('GPT-5.6 Luna');
        expect(modelLabelText('GPT-5.6 Luna', undefined)).toBe('GPT-5.6 Luna');

        const [luna, , plain] = LEVELED;
        expect(modelLabelLines(luna, false)).toBe('GPT-5.6 Luna (max)');
        expect(modelLabelLines(luna, true)).toEqual(['GPT-5.6 Luna', '(max)']);
        expect(modelLabelLines(plain, true)).toBe('Plain Model');
        // An entry built without the parts is never broken.
        expect(modelLabelLines(makeEntry({ key: 'bare' }), true)).toBe('bare');
      });

      for (const orientation of ['vertical', 'horizontal'] as const) {
        it(`puts the level on its own tick line in all three ${orientation} bar panels when on`, () => {
          const build = (thinkingLevelBreak: boolean) => buildSmallMultiples(LEVELED, {
            ...smallMultiplesOptions({ orientation, style: style({ thinkingLevelBreak }) }),
            glyphs: buildIdentityGlyphs(LEVELED),
          });
          const off = build(false);
          const on = build(true);
          for (const panel of [off.quality, off.speed, off.cost]) {
            expect(panel.config.data.labels, panel.id).toEqual(['GPT-5.6 Luna (max)', 'Gemini 3.7 Flash (high)', 'Plain Model']);
          }
          for (const panel of [on.quality, on.speed, on.cost]) {
            expect(panel.config.data.labels, panel.id).toEqual([['GPT-5.6 Luna', '(max)'], ['Gemini 3.7 Flash', '(high)'], 'Plain Model']);
          }
        });
      }

      it('puts n = 1 after the level line on a single-run model', () => {
        const entries = [leveled('once', 'GPT-5.6 Luna', 'max', 1), leveled('plain', 'Plain Model', null, 1)];
        const build = (thinkingLevelBreak: boolean) => buildSmallMultiples(entries, {
          ...smallMultiplesOptions({ style: style({ thinkingLevelBreak }) }),
          glyphs: buildIdentityGlyphs(entries),
        });
        for (const panel of [build(true).quality, build(true).speed, build(true).cost]) {
          expect(panel.config.data.labels, panel.id).toEqual([['GPT-5.6 Luna', '(max)', 'n = 1'], ['Plain Model', 'n = 1']]);
        }
        expect(build(false).quality.config.data.labels)
          .toEqual([['GPT-5.6 Luna (max)', 'n = 1'], ['Plain Model', 'n = 1']]);
      });

      it('measures a two-line name one name line taller and as wide as its widest line', () => {
        const ctx = { font: '', measureText: (text: string) => ({ width: text.length * 6 }) };
        const values = [{ label: 'Intelligence', text: '82.4' }];
        const oneLine = measureDirectLabelBlock(ctx as unknown as CanvasRenderingContext2D, {
          name: 'GPT-5.6 Luna (max)', values, hue: '#3987e5',
        });
        const twoLines = measureDirectLabelBlock(ctx as unknown as CanvasRenderingContext2D, {
          name: ['GPT-5.6 Luna', '(max)'], values, hue: '#3987e5',
        });
        expect(twoLines.height).toBe(oneLine.height + 12);
        expect(twoLines.width).toBeGreaterThanOrEqual(12 + 'GPT-5.6 Luna'.length * 6);
        expect(twoLines.width).toBe(12 + Math.max('GPT-5.6 Luna'.length * 6, 72 + 8 + 24));
      });

      it('breaks the scatter plate names only when the option is on', () => {
        const blocks = (thinkingLevelBreak: boolean) => {
          const spec = buildQualitySpeedScatter(LEVELED, {
            ...BASE_FIGURE_OPTIONS,
            glyphs: buildIdentityGlyphs(LEVELED),
            directLabels: true,
            style: style({}, { thinkingLevelBreak }),
          });
          return (spec.config.options?.plugins as Record<string, DirectLabelPluginOptions>)[directLabelPlugin.id].blocks;
        };
        expect(blocks(false).map((b) => b.name)).toEqual(['GPT-5.6 Luna (max)', 'Gemini 3.7 Flash (high)', 'Plain Model']);
        expect(blocks(true).map((b) => b.name))
          .toEqual([['GPT-5.6 Luna', '(max)'], ['Gemini 3.7 Flash', '(high)'], 'Plain Model']);
      });

      describe('the scatter legend', () => {
        beforeAll(() => {
          Chart.register(...APP_CHART_REGISTRABLES);
        });

        type LegendLabels = { generateLabels?: (chart: Chart) => { text: string | string[]; datasetIndex?: number }[] };
        const legendLabels = (scatter: Partial<ScatterFigureStyle>, directLabels = false): LegendLabels => {
          const spec = buildQualitySpeedScatter(LEVELED, {
            ...BASE_FIGURE_OPTIONS,
            glyphs: buildIdentityGlyphs(LEVELED),
            directLabels,
            style: style({}, scatter),
          });
          return spec.config.options?.plugins?.legend?.labels as LegendLabels;
        };

        it('wraps only a right legend, with the option on and no direct labels', () => {
          expect(legendLabels({ legendPosition: 'right', thinkingLevelBreak: true }).generateLabels).toBeDefined();
          expect(legendLabels({ legendPosition: 'bottom', thinkingLevelBreak: true }).generateLabels).toBeUndefined();
          expect(legendLabels({ legendPosition: 'right', thinkingLevelBreak: false }).generateLabels).toBeUndefined();
          expect(legendLabels({ legendPosition: 'right', thinkingLevelBreak: true }, true).generateLabels).toBeUndefined();
        });

        it('gives model items two lines and leaves the frontier item as it is', () => {
          const defaults = [
            { text: 'GPT-5.6 Luna (max)', datasetIndex: 0 },
            { text: 'Gemini 3.7 Flash (high)', datasetIndex: 1 },
            { text: 'Plain Model', datasetIndex: 2 },
            { text: 'Pareto frontier', datasetIndex: 3 },
          ];
          vi.spyOn(Chart.defaults.plugins.legend.labels, 'generateLabels').mockReturnValue(defaults as unknown as ReturnType<typeof Chart.defaults.plugins.legend.labels.generateLabels>);
          const generate = legendLabels({ legendPosition: 'right', thinkingLevelBreak: true }).generateLabels!;
          expect(generate({} as Chart).map((item) => item.text)).toEqual([
            ['GPT-5.6 Luna', '(max)'],
            ['Gemini 3.7 Flash', '(high)'],
            'Plain Model',
            'Pareto frontier',
          ]);
        });
      });

      it('keeps the profile tile titles on one line, with the level, whatever the option', () => {
        for (const figureStyle of [style(), style({ thinkingLevelBreak: true }, { thinkingLevelBreak: true })]) {
          const spec = buildProfilePlot(LEVELED, {
            ...BASE_FIGURE_OPTIONS,
            glyphs: buildIdentityGlyphs(LEVELED),
            speedMeasure: 'speedIndex',
            costMeasure: 'candidateSuite',
            style: figureStyle,
          });
          expect(profileTiles(spec).map((tile) => tileOptionsOf(tile).title))
            .toEqual(['GPT-5.6 Luna (max)', 'Gemini 3.7 Flash (high)', 'Plain Model']);
        }
      });
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
    // Every profile tile is the same chart type; the first stands for them all.
    { id: figures.profile.id, config: figures.profile.tiles[0] },
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

// ---------------------------------------------------------------------------------------------
// Number formats, their samples and the value-axis title break
// ---------------------------------------------------------------------------------------------

const CONTEXT_18: ModelComparisonContext = { ...CONTEXT, questionsAskedPerRun: 18 };

/** Two models whose suite cost is read from the run cost; the per-question cost is that / 18. */
const FORMAT_FIXTURE: ModelComparisonEntry[] = [
  makeEntry({
    key: 'A',
    intelligenceIndex: 71.4,
    speedIndex: 64,
    modelTimeMeanMs: 22470,
    totalModelTimeMs: 504000,
    totalModelTimeSdMs: null,
    ttftP50Ms: 1250,
    ttftP90Ms: 1250,
    candidateCostPerRunUsd: 0.0761,
    candidateCostPerQuestionUsd: 0.0761 / 18,
    totalRunCostUsd: 0.312,
  }),
  makeEntry({
    key: 'B',
    intelligenceIndex: 71.2,
    speedIndex: 58,
    modelTimeMeanMs: 850,
    totalModelTimeMs: 15300,
    totalModelTimeSdMs: null,
    ttftP50Ms: 900,
    ttftP90Ms: 900,
    candidateCostPerRunUsd: 0.0428,
    candidateCostPerQuestionUsd: 0.0428 / 18,
    totalRunCostUsd: 0.2,
  }),
];

function withNumbers(overrides: Partial<NumberFormatStyle>, base: FigureStyle = DEFAULT_FIGURE_STYLE): FigureStyle {
  return { ...base, numbers: { ...base.numbers, ...overrides } };
}

function formatOptions(overrides: Partial<SmallMultiplesOptions> = {}): SmallMultiplesOptions {
  return {
    ...BASE_FIGURE_OPTIONS,
    context: CONTEXT_18,
    glyphs: buildIdentityGlyphs(FORMAT_FIXTURE),
    speedMeasure: 'meanModelTime',
    costMeasure: 'candidateSuite',
    orientation: 'vertical',
    ...overrides,
  };
}

/** A bar panel's value label and tooltip line for one bar, through the callbacks Chart.js calls. */
function barText(spec: { config: unknown }): { label: (index: number) => string; tooltip: (index: number) => string } {
  const plugins = (spec.config as { options: { plugins: unknown } }).options.plugins as {
    datalabels: { formatter: (value: unknown, ctx: { dataIndex: number }) => string };
    tooltip: { callbacks: { label: (item: { dataIndex: number }) => string } };
  };
  return {
    label: (index) => plugins.datalabels.formatter(undefined, { dataIndex: index }),
    tooltip: (index) => plugins.tooltip.callbacks.label({ dataIndex: index }),
  };
}

function scatterTooltip(spec: { config: unknown }): (x: number, y: number) => string[] {
  const plugins = (spec.config as { options: { plugins: unknown } }).options.plugins as {
    tooltip: { callbacks: { label: (item: { parsed: { x: number; y: number } }) => string[] } };
  };
  return (x, y) => plugins.tooltip.callbacks.label({ parsed: { x, y } });
}

function plateValues(spec: { config: unknown }): DirectLabelValue[][] {
  const plugins = (spec.config as { options: { plugins: Record<string, DirectLabelPluginOptions> } }).options.plugins;
  return plugins[directLabelPlugin.id].blocks.map((block) => [...block.values]);
}

describe('number formats in the figures', () => {
  it('writes bar value labels and bar tooltips to the same decimals', () => {
    const figure = buildSmallMultiples(FORMAT_FIXTURE, formatOptions({
      style: withNumbers({ intelligenceIndex: 1, meanModelTime: 2, suiteCost: 2 }),
    }));
    const quality = barText(figure.quality);
    expect(quality.label(0)).toBe('71.4');
    expect(quality.label(1)).toBe('71.2');
    expect(quality.tooltip(0)).toBe('Intelligence Index (0-100): 71.4');
    const speed = barText(figure.speed);
    expect(speed.label(0)).toBe('22.47 s');
    expect(speed.label(1)).toBe('0.85 s');
    expect(speed.tooltip(1)).toBe('Mean time per question (s): 0.85 s');
    const cost = barText(figure.cost);
    expect(cost.label(0)).toBe('$0.08');
    expect(cost.label(1)).toBe('$0.04');
    expect(cost.tooltip(0)).toBe('Candidate cost per suite run (USD, 18 questions): $0.08');
  });

  it('prints whole indices, one decimal of seconds and four of dollars by default', () => {
    const figure = buildSmallMultiples(FORMAT_FIXTURE, formatOptions());
    expect(barText(figure.quality).label(0)).toBe('71');
    expect(barText(figure.quality).label(1)).toBe('71');
    expect(barText(figure.speed).label(0)).toBe('22.5 s');
    expect(barText(figure.cost).label(0)).toBe('$0.0761');
    expect(barText(figure.cost).label(1)).toBe('$0.0428');
  });

  it('writes a sub-second model in seconds on a seconds axis, and in whole ms on a milliseconds one', () => {
    const entries = [FORMAT_FIXTURE[0], makeEntry({ key: 'fast', modelTimeMeanMs: 870 })];
    const figure = buildSmallMultiples(entries, formatOptions({ glyphs: buildIdentityGlyphs(entries) }));
    expect(titleLines(scaleOf(figure.speed.config, 'y'))[0]).toBe('Mean time per question (s)');
    expect(barText(figure.speed).label(1)).toBe('0.9 s');

    const msOnly = buildSmallMultiples([FORMAT_FIXTURE[1]], formatOptions({ style: withNumbers({ meanModelTime: 3 }) }));
    expect(titleLines(scaleOf(msOnly.speed.config, 'y'))[0]).toBe('Mean time per question (ms)');
    expect(barText(msOnly.speed).label(0)).toBe('850 ms');
  });

  it('follows the setting of whichever speed and cost measure is selected', () => {
    const speeds: [SpeedMeasure, string][] = [
      ['meanModelTime', '22.470 s'],
      ['totalModelTime', '504.000 s'],
      ['ttftP50', '1.250 s'],
      ['speedIndex', '64.000'],
    ];
    for (const [speedMeasure, text] of speeds) {
      const style = withNumbers({ meanModelTime: 0, totalModelTime: 0, ttftP50: 0, speedIndex: 0, [speedMeasure]: 3 });
      const figure = buildSmallMultiples(FORMAT_FIXTURE, formatOptions({ speedMeasure, style }));
      expect(barText(figure.speed).label(0), speedMeasure).toBe(text);
    }
    const costs: [CostMeasure, string][] = [['candidateSuite', '$0.076'], ['totalRun', '$0.3']];
    for (const [costMeasure, text] of costs) {
      const style = withNumbers({ suiteCost: costMeasure === 'candidateSuite' ? 3 : 0, totalRunCost: costMeasure === 'totalRun' ? 1 : 5 });
      const figure = buildSmallMultiples(FORMAT_FIXTURE, formatOptions({ costMeasure, style }));
      expect(barText(figure.cost).label(0), costMeasure).toBe(text);
    }
  });

  it('keeps each measure\'s setting to that measure', () => {
    const base = buildSmallMultiples(FORMAT_FIXTURE, formatOptions());
    const others = buildSmallMultiples(FORMAT_FIXTURE, formatOptions({
      style: withNumbers({ totalModelTime: 6, ttftP50: 6, speedIndex: 6, totalRunCost: 6, costPerQuestion: 6 }),
    }));
    for (const panel of ['quality', 'speed', 'cost'] as const) {
      for (const index of [0, 1]) {
        expect(barText(others[panel]).label(index), `${panel} ${index}`).toBe(barText(base[panel]).label(index));
      }
    }
  });

  it('rounds no value, domain, tick or normalized coordinate', () => {
    const six = withNumbers({
      intelligenceIndex: 6, speedIndex: 6, meanModelTime: 6, totalModelTime: 6, ttftP50: 6,
      suiteCost: 6, totalRunCost: 6, costPerQuestion: 6,
    });
    const plain = buildComparisonFigures(FORMAT_FIXTURE, { context: CONTEXT_18 });
    const precise = buildComparisonFigures(FORMAT_FIXTURE, { context: CONTEXT_18, style: six });
    const before = allSpecs(plain);
    const after = allSpecs(precise);
    before.forEach((spec, i) => {
      expect(datasetsOf(after[i].config).map((d) => d['data']), spec.id).toEqual(datasetsOf(spec.config).map((d) => d['data']));
      for (const axis of ['x', 'y'] as const) {
        const a = scaleOf(spec.config, axis);
        const b = scaleOf(after[i].config, axis);
        expect(b.min, `${spec.id} ${axis}`).toBe(a.min);
        expect(b.max, `${spec.id} ${axis}`).toBe(a.max);
        expect(ticksOf(b), `${spec.id} ${axis}`).toEqual(ticksOf(a));
        const steps = [{ value: 0 }, { value: 2500 }];
        for (const value of [0, 0.05, 2500, 12345]) {
          expect(b.ticks?.callback?.(value, 1, steps), `${spec.id} ${axis} ${value}`).toEqual(a.ticks?.callback?.(value, 1, steps));
        }
      }
    });

    const options = { context: CONTEXT_18, speedMeasure: 'meanModelTime', costMeasure: 'candidateSuite' } as const;
    const normal = normalizeProfile(FORMAT_FIXTURE, options);
    const sixProfile = normalizeProfile(FORMAT_FIXTURE, { ...options, numbers: six.numbers });
    expect(sixProfile.rows).toEqual(normal.rows);
    expect(sixProfile.axes.map((axis) => [axis.min, axis.max])).toEqual(normal.axes.map((axis) => [axis.min, axis.max]));
  });

  it('writes scatter plates and scatter tooltips alike, in the axis unit', () => {
    const style = withNumbers({ intelligenceIndex: 1, meanModelTime: 2, costPerQuestion: 5, suiteCost: 0 });
    const options = { ...BASE_FIGURE_OPTIONS, context: CONTEXT_18, glyphs: buildIdentityGlyphs(FORMAT_FIXTURE), inlineValues: true, style };
    const s1 = buildQualitySpeedScatter(FORMAT_FIXTURE, options);
    expect(plateValues(s1)[1]).toEqual([
      { label: 'Mean time', text: '0.85 s' },
      { label: 'Intelligence', text: '71.2' },
    ]);
    expect(scatterTooltip(s1)(850, 71.2)).toEqual(['Mean time per question: 0.85 s', 'Intelligence Index: 71.2']);

    // Trade-offs always show cost per question, whatever the suite-cost setting.
    const s2 = buildQualityCostScatter(FORMAT_FIXTURE, options);
    expect(plateValues(s2)[0]).toEqual([
      { label: 'Cost / question', text: '$0.00423' },
      { label: 'Intelligence', text: '71.4' },
    ]);
    expect(scatterTooltip(s2)(0.0761 / 18, 71.4)).toEqual(['Cost per question: $0.00423', 'Intelligence Index: 71.4']);
  });

  it('follows a scatter axis into seconds where its domain passes 1000 ms, though every value is below', () => {
    const entries = [makeEntry({ key: 'p', modelTimeMeanMs: 900 }), makeEntry({ key: 'q', modelTimeMeanMs: 950 })];
    const spec = buildQualitySpeedScatter(entries, {
      ...BASE_FIGURE_OPTIONS,
      glyphs: buildIdentityGlyphs(entries),
      inlineValues: true,
      style: withNumbers({ meanModelTime: 2 }),
    });
    expect(titleLines(scaleOf(spec.config, 'x'))[0]).toBe('Mean time per question (s)');
    expect(plateValues(spec).map((values) => values[0].text)).toEqual(['0.90 s', '0.95 s']);
    expect(scatterTooltip(spec)(900, 50)[0]).toBe('Mean time per question: 0.90 s');
  });

  it('writes the profile range labels and the profile tooltip to the style decimals', () => {
    const numbers = { ...DEFAULT_FIGURE_STYLE.numbers, intelligenceIndex: 1, meanModelTime: 2, suiteCost: 2 };
    const options = { context: CONTEXT_18, speedMeasure: 'meanModelTime', costMeasure: 'candidateSuite' } as const;
    const styled = normalizeProfile(FORMAT_FIXTURE, { ...options, numbers });
    expect(styled.axes.map((axis) => [axis.minLabel, axis.maxLabel])).toEqual([
      ['71.2', '71.4'],
      ['0.85 s', '22.47 s'],
      ['$0.04', '$0.08'],
    ]);
    // 850 ms at one decimal is a binary tie, so the defaults are checked away from it.
    const defaults = normalizeProfile(FORMAT_FIXTURE, options);
    expect([defaults.axes[0].minLabel, defaults.axes[0].maxLabel]).toEqual(['71', '71']);
    expect(defaults.axes[1].maxLabel).toBe('22.5 s');
    expect([defaults.axes[2].minLabel, defaults.axes[2].maxLabel]).toEqual(['$0.0428', '$0.0761']);

    const plot = buildProfilePlot(FORMAT_FIXTURE, {
      ...BASE_FIGURE_OPTIONS,
      glyphs: buildIdentityGlyphs(FORMAT_FIXTURE),
      ...options,
      style: withNumbers(numbers),
    });
    // Each tile's tooltip speaks for its own model; the plotted heights never reach it.
    const label = (tileIndex: number) => (plot.tiles[tileIndex].options?.plugins?.tooltip as unknown as {
      callbacks: { label: (item: { datasetIndex: number; dataIndex: number }) => string };
    }).callbacks.label;
    expect(label(0)({ datasetIndex: 0, dataIndex: 0 })).toBe('A — Intelligence: 71.4');
    expect(label(1)({ datasetIndex: 0, dataIndex: 1 })).toBe('B — Speed (mean model time): 0.85 s');
    expect(label(0)({ datasetIndex: 0, dataIndex: 2 })).toBe('A — Cost (candidate, suite): $0.08');
    expect(label(0)({ datasetIndex: 0, dataIndex: 7 })).toBe('');
    expect(label(0)({ datasetIndex: -1, dataIndex: -1 })).toBe('');
    // The printed values on the points follow the same decimals.
    expect(tileOptionsOf(plot.tiles[1]).values).toEqual(['71.2', '0.85 s', '$0.04']);
  });

  it('keeps the profile\'s zero for a missing Speed Index rather than a new format', () => {
    const entries = [makeEntry({ key: 'none', speedIndex: null }), makeEntry({ key: 'some', speedIndex: 40 })];
    const profile = normalizeProfile(entries, {
      context: CONTEXT,
      speedMeasure: 'speedIndex',
      costMeasure: 'candidateSuite',
      numbers: { ...DEFAULT_FIGURE_STYLE.numbers, speedIndex: 1 },
    });
    expect(profile.axes[1].minLabel).toBe('0.0');
    expect(profile.axes[1].maxLabel).toBe('40.0');
  });
});

describe('number format samples', () => {
  const options = { context: CONTEXT_18, speedMeasure: 'meanModelTime', costMeasure: 'candidateSuite' } as const;

  it('takes the first plotted value of each of the family\'s three measures, with the family unit', () => {
    expect(buildNumberSamples(FORMAT_FIXTURE, options, 'bar')).toEqual({
      intelligenceIndex: { value: 71.4 },
      meanModelTime: { value: 22470, unit: 's' },
      suiteCost: { value: 0.0761 },
    });
    expect(buildNumberSamples(FORMAT_FIXTURE, options, 'profile')).toEqual({
      intelligenceIndex: { value: 71.4 },
      meanModelTime: { value: 22470, unit: 's' },
      suiteCost: { value: 0.0761 },
    });
    expect(buildNumberSamples(FORMAT_FIXTURE, options, 'scatter')).toEqual({
      intelligenceIndex: { value: 71.4 },
      meanModelTime: { value: 22470, unit: 's' },
      costPerQuestion: { value: 0.0761 / 18 },
    });
  });

  it('follows the plotted order, the selected measures and skips unmeasured values', () => {
    const reversed = [...FORMAT_FIXTURE].reverse();
    expect(buildNumberSamples(reversed, options, 'bar').intelligenceIndex).toEqual({ value: 71.2 });
    expect(buildNumberSamples(reversed, options, 'bar').meanModelTime).toEqual({ value: 850, unit: 's' });

    const total = buildNumberSamples(FORMAT_FIXTURE, { ...options, speedMeasure: 'ttftP50', costMeasure: 'totalRun' }, 'bar');
    expect(total).toEqual({ intelligenceIndex: { value: 71.4 }, ttftP50: { value: 1250, unit: 's' }, totalRunCost: { value: 0.312 } });

    const index = [makeEntry({ key: 'x', speedIndex: null }), makeEntry({ key: 'y', speedIndex: 64 })];
    expect(buildNumberSamples(index, { ...options, speedMeasure: 'speedIndex' }, 'bar').speedIndex).toEqual({ value: 64 });

    const unmeasured = [makeEntry({ key: 'n', modelTimeMeanMs: Number.NaN }), makeEntry({ key: 'm', modelTimeMeanMs: 700 })];
    expect(buildNumberSamples(unmeasured, options, 'bar').meanModelTime).toEqual({ value: 700, unit: 'ms' });
  });

  it('leaves out a measure nothing plotted has, and everything for an empty set', () => {
    const none = [makeEntry({ key: 'x', speedIndex: null }), makeEntry({ key: 'y', speedIndex: null })];
    const samples = buildNumberSamples(none, { ...options, speedMeasure: 'speedIndex' }, 'profile');
    expect('speedIndex' in samples).toBe(false);
    expect(samples.intelligenceIndex).toEqual({ value: 50 });
    expect(buildNumberSamples([], options, 'scatter')).toEqual({});
  });

  it('takes the scatter unit from the resolved axis, which the interval visibility can move', () => {
    const entries = [
      makeEntry({ key: 'p', totalModelTimeMs: 700, totalModelTimeSdMs: 400 }),
      makeEntry({ key: 'q', totalModelTimeMs: 800, totalModelTimeSdMs: 400 }),
    ];
    const totals = { ...options, speedMeasure: 'totalModelTime' } as const;
    expect(buildNumberSamples(entries, totals, 'scatter').totalModelTime).toEqual({ value: 700, unit: 's' });
    const hidden = { ...DEFAULT_FIGURE_STYLE, scatter: { ...DEFAULT_FIGURE_STYLE.scatter, intervals: false } };
    expect(buildNumberSamples(entries, { ...totals, style: hidden }, 'scatter').totalModelTime).toEqual({ value: 700, unit: 'ms' });
    // The builder resolves the same unit for its title.
    const spec = buildQualitySpeedScatter(entries, { ...BASE_FIGURE_OPTIONS, glyphs: buildIdentityGlyphs(entries), speedMeasure: 'totalModelTime', style: hidden });
    expect(titleLines(scaleOf(spec.config, 'x'))[0]).toBe('Total time for the suite (ms)');
    // Bars and the profile use the largest raw value, so they stay in milliseconds either way.
    expect(buildNumberSamples(entries, totals, 'bar').totalModelTime).toEqual({ value: 700, unit: 'ms' });
    expect(buildNumberSamples(entries, totals, 'profile').totalModelTime).toEqual({ value: 700, unit: 'ms' });
  });
});

describe('the value-axis title break', () => {
  const COST_TITLE = 'Candidate cost per suite run (USD, 18 questions)';

  it('splits only a final parenthetical after a nonempty head', () => {
    expect(splitAxisTitle(COST_TITLE)).toEqual(['Candidate cost per suite run', '(USD, 18 questions)']);
    expect(splitAxisTitle('Intelligence Index (0-100)')).toEqual(['Intelligence Index', '(0-100)']);
    expect(splitAxisTitle('Mean time (per question) (s)')).toEqual(['Mean time (per question)', '(s)']);
    expect(splitAxisTitle('Cost (a (b))')).toEqual(['Cost', '(a (b))']);
    expect(splitAxisTitle('Model')).toBeNull();
    expect(splitAxisTitle('Cost (USD) per run')).toBeNull();
    expect(splitAxisTitle('(USD)')).toBeNull();
    expect(splitAxisTitle(' (USD)')).toBeNull();
    expect(splitAxisTitle('   (USD)')).toBeNull();
    expect(splitAxisTitle('Cost(USD)')).toBeNull();
    expect(splitAxisTitle('Cost USD)')).toBeNull();
  });

  it('keeps the direction line last and discards no title text', () => {
    const lines = axisTitleLines(COST_TITLE, 'lower');
    expect(lines.unbroken).toEqual([COST_TITLE, 'lower is better']);
    expect(lines.broken).toEqual(['Candidate cost per suite run', '(USD, 18 questions)', 'lower is better']);
    expect(lines.broken!.slice(0, 2).join(' ')).toBe(COST_TITLE);
    expect(axisTitleLines(COST_TITLE).broken).toEqual(['Candidate cost per suite run', '(USD, 18 questions)']);
    expect(axisTitleLines('Model', 'higher')).toEqual({ unbroken: ['Model', 'higher is better'], broken: null });
  });

  function barStyle(bar: Partial<BarFigureStyle>): FigureStyle {
    return { ...DEFAULT_FIGURE_STYLE, bar: { ...DEFAULT_FIGURE_STYLE.bar, ...bar } };
  }

  it('builds each mode: Always broken, Never unbroken, and only Automatic decides at fit', () => {
    const SUITE_TITLE = 'Candidate cost per suite run (USD, 18 questions)';
    const SUITE_BROKEN = ['Candidate cost per suite run', '(USD, 18 questions)'];
    const TOTAL_TITLE = 'Total run cost including grading roles (USD)';
    const build = (bar: Partial<BarFigureStyle>, costMeasure: CostMeasure = 'candidateSuite') =>
      buildSmallMultiples(FORMAT_FIXTURE, formatOptions({ costMeasure, style: barStyle(bar) }));
    const afterFitOf = (config: unknown): unknown => (scaleOf(config, 'y') as { afterFit?: unknown }).afterFit;

    const always = build({ axisTitleBreak: 'always' });
    expect(titleLines(scaleOf(always.cost.config, 'y'))).toEqual(SUITE_BROKEN);
    expect(titleLines(scaleOf(always.quality.config, 'y'))).toEqual(['Intelligence Index', '(0-100)']);
    expect(afterFitOf(always.cost.config)).toBeUndefined();

    const never = build({ axisTitleBreak: 'never' });
    expect(titleLines(scaleOf(never.cost.config, 'y'))).toEqual([SUITE_TITLE]);
    expect(afterFitOf(never.cost.config)).toBeUndefined();

    // The candidate suite cost title is always two lines under Automatic, so it is never clipped.
    const auto = build({});
    expect(scaleOf(auto.cost.config, 'y').title?.text).toEqual(SUITE_BROKEN);
    expect(afterFitOf(auto.cost.config)).toBeUndefined();
    // Any other title keeps the Automatic decision at fit.
    const autoTotal = build({}, 'totalRun');
    expect(scaleOf(autoTotal.cost.config, 'y').title?.text).toEqual([TOTAL_TITLE]);
    expect(typeof afterFitOf(autoTotal.cost.config)).toBe('function');

    // The category axis and the tooltip keep their one-line titles in every mode.
    for (const figure of [always, never, auto]) {
      expect(scaleOf(figure.cost.config, 'x').title?.text).toBe('Model');
      expect(barText(figure.cost).tooltip(0).startsWith(`${SUITE_TITLE}: `)).toBe(true);
    }

    const hidden = build({ axisTitleBreak: 'always', hiddenBadges: ['direction'] });
    expect(titleLines(scaleOf(hidden.cost.config, 'y'))).toEqual([...SUITE_BROKEN, 'lower is better']);
    const hiddenNever = build({ axisTitleBreak: 'never', hiddenBadges: ['direction'] });
    expect(titleLines(scaleOf(hiddenNever.cost.config, 'y'))).toEqual([SUITE_TITLE, 'lower is better']);

    const horizontal = buildSmallMultiples(FORMAT_FIXTURE, formatOptions({ orientation: 'horizontal', style: barStyle({ axisTitleBreak: 'always' }) }));
    expect(titleLines(scaleOf(horizontal.cost.config, 'x'))).toEqual(SUITE_BROKEN);
    expect(scaleOf(horizontal.cost.config, 'y').title?.text).toBe('Model');
  });

  it('leaves the scatter titles unbroken', () => {
    const spec = buildQualityCostScatter(FORMAT_FIXTURE, { ...BASE_FIGURE_OPTIONS, style: barStyle({ axisTitleBreak: 'always' }) });
    expect(titleLines(scaleOf(spec.config, 'x'))[0]).toBe('Cost per question (USD)');
    expect((scaleOf(spec.config, 'x') as { afterFit?: unknown }).afterFit).toBeUndefined();
  });
});

/**
 * The Automatic break on real Chart.js layouts. Each chart is constructed the way
 * `renderPlotOffscreen` constructs one — a shallow copy of the spec's options — because Chart.js
 * replaces `options.scales` of the object it is given with its own merged copy, and the break is
 * written there.
 */
describe('the automatic title break on a real chart', () => {
  const COST_TITLE = 'Total run cost including grading roles (USD)';
  const LONG_LABELS = [
    'An exceptionally long model name for layout (preview)',
    'Another rather long model name (latest)',
  ];
  let charts: Chart[] = [];
  let containers: HTMLElement[] = [];

  beforeAll(() => {
    Chart.register(...APP_CHART_REGISTRABLES);
  });

  afterEach(() => {
    charts.forEach((chart) => chart.destroy());
    containers.forEach((container) => container.remove());
    charts = [];
    containers = [];
  });

  function entriesFor(labels: 'short' | 'long' | 'eight'): ModelComparisonEntry[] {
    if (labels === 'eight') {
      return Array.from({ length: 8 }, (_, i) => makeEntry({
        key: `m${i}`,
        label: `Model ${i}${' with a longer name'.repeat(i % 3)}`,
        candidateCostPerRunUsd: 0.01 * (i + 1),
      }));
    }
    return labels === 'short'
      ? FORMAT_FIXTURE
      : FORMAT_FIXTURE.map((entry, i) => ({ ...entry, label: LONG_LABELS[i] }));
  }

  function costPanel(
    orientation: BarOrientation,
    options: { labels?: 'short' | 'long' | 'eight'; titleSize?: number; axisTextSize?: number; directionHidden?: boolean } = {},
  ) {
    const entries = entriesFor(options.labels ?? 'short');
    const style: FigureStyle = {
      ...DEFAULT_FIGURE_STYLE,
      bar: {
        ...DEFAULT_FIGURE_STYLE.bar,
        axisTextSizePx: options.axisTextSize ?? DEFAULT_FIGURE_STYLE.bar.axisTextSizePx,
        axisTitleSizePx: options.titleSize ?? DEFAULT_FIGURE_STYLE.bar.axisTitleSizePx,
        hiddenBadges: options.directionHidden ? ['direction'] : [],
      },
    };
    // The total run cost: the candidate suite cost title is always two lines, so it never decides at fit.
    return buildSmallMultiples(entries, formatOptions({
      glyphs: buildIdentityGlyphs(entries),
      costMeasure: 'totalRun',
      orientation,
      style,
    })).cost;
  }

  function mount(spec: { config: unknown; plugins: readonly Plugin[] }, width: number, height: number, density = 1): Chart {
    const container = document.createElement('div');
    container.style.cssText = `position: fixed; left: -10000px; top: 0; width: ${width}px; height: ${height}px`;
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    canvas.style.width = `${width}px`;
    canvas.style.height = `${height}px`;
    container.appendChild(canvas);
    document.body.appendChild(container);
    containers.push(container);
    const config = spec.config as { type: ChartType; data: unknown; options?: Record<string, unknown> };
    const chart = new Chart(canvas, {
      type: config.type,
      data: config.data,
      options: {
        ...(config.options ?? {}),
        responsive: false,
        maintainAspectRatio: false,
        animation: false,
        devicePixelRatio: density,
        font: { family: 'Arial', size: 12 },
      },
      // The break is decided in the layout (`afterFit`), which runs before drawing: nothing is painted.
      plugins: [...spec.plugins, { id: 'noPaint', beforeDraw: () => false }],
    } as unknown as ChartConfiguration);
    charts.push(chart);
    return chart;
  }

  /** Destroys a chart whose sample is asserted and removes its container; `afterEach` takes the rest. */
  function unmount(chart: Chart): void {
    const container = chart.canvas.parentElement;
    chart.destroy();
    container?.remove();
    charts = charts.filter((other) => other !== chart);
    containers = containers.filter((other) => other !== container);
  }

  function valueScale(chart: Chart, orientation: BarOrientation): Scale {
    return chart.scales[orientation === 'vertical' ? 'y' : 'x'];
  }

  function titleText(scale: Scale): unknown {
    return (scale.options as unknown as { title: { text: unknown } }).title.text;
  }

  function widest(chart: Chart, scale: Scale, lines: readonly string[]): number {
    const title = (scale.options as unknown as { title: { font: Partial<FontSpec> } }).title;
    const ctx = chart.ctx;
    ctx.save();
    ctx.font = toFont(title.font, chart.options.font).string;
    const width = Math.max(...lines.map((line) => ctx.measureText(line).width));
    ctx.restore();
    return width;
  }

  /** Checks the chart's title against its final axis length, and says which form that length needs. */
  function expectFinalDecision(chart: Chart, orientation: BarOrientation, directionHidden: boolean, context: string): 'broken' | 'unbroken' {
    const lines = axisTitleLines(COST_TITLE, directionHidden ? 'lower' : undefined);
    const scale = valueScale(chart, orientation);
    const length = orientation === 'vertical' ? scale.height : scale.width;
    const fits = widest(chart, scale, lines.unbroken) <= Math.max(0, length - AXIS_TITLE_RESERVE_PX);
    const text = titleText(scale) as string[];
    expect(text, `${context}, axis ${length.toFixed(2)} px`).toEqual(fits ? [...lines.unbroken] : [...lines.broken!]);
    // Nothing is dropped, whichever form was drawn.
    expect(text.slice(0, fits ? 1 : 2).join(' '), context).toBe(COST_TITLE);
    return fits ? 'unbroken' : 'broken';
  }

  function sourceTitle(spec: { config: unknown }, orientation: BarOrientation): { title: { text: unknown }; copy: unknown } {
    const title = (scaleOf(spec.config, orientation === 'vertical' ? 'y' : 'x') as { title: { text: unknown } }).title;
    return { title, copy: JSON.parse(JSON.stringify(title.text)) };
  }

  /** Lengths from `from` to `to` every `step` px, `to` always included. */
  function sweep(from: number, to: number, step: number): number[] {
    const lengths: number[] = [];
    for (let length = from; length <= to; length += step) {
      lengths.push(length);
    }
    if (lengths[lengths.length - 1] !== to) {
      lengths.push(to);
    }
    return lengths;
  }

  it('decides against the final value-axis length in both orientations, for every label, badge and title size', () => {
    for (const orientation of ['vertical', 'horizontal'] as const) {
      for (const labels of ['short', 'long'] as const) {
        for (const directionHidden of [false, true]) {
          for (const titleSize of [8, 12, 48]) {
            const spec = costPanel(orientation, { labels, titleSize, directionHidden });
            const source = sourceTitle(spec, orientation);
            const outcomes = new Set<string>();
            const lengths = orientation === 'vertical' ? sweep(140, 700, 80) : sweep(240, 1000, 80);
            for (const length of lengths) {
              const chart = orientation === 'vertical' ? mount(spec, 420, length) : mount(spec, length, 320);
              outcomes.add(expectFinalDecision(chart, orientation, directionHidden, `${orientation} ${labels} ${titleSize}px hidden=${directionHidden} ${length}`));
              unmount(chart);
            }
            expect(source.title.text, 'source').toEqual(source.copy);
            if (titleSize === 12) {
              expect(outcomes.size, `${orientation} ${labels} hidden=${directionHidden}: both forms reached`).toBe(2);
            }
          }
        }
      }
    }
  });

  it('decides correctly at every few pixels around the threshold', () => {
    for (const orientation of ['vertical', 'horizontal'] as const) {
      const spec = costPanel(orientation, { labels: 'long' });
      const outcomes: string[] = [];
      const [from, to] = orientation === 'vertical' ? [300, 520] : [380, 700];
      const sampled = new Set<number>();
      const sample = (length: number): string => {
        sampled.add(length);
        const chart = orientation === 'vertical' ? mount(spec, 420, length) : mount(spec, length, 320);
        const outcome = expectFinalDecision(chart, orientation, false, `${orientation} ${length}`);
        unmount(chart);
        outcomes.push(outcome);
        return outcome;
      };
      // Every 12 px across the range; the threshold lies between two neighbors whose expected forms
      // differ, and every pixel within 6 px of that pair is sampled as well.
      const coarse = sweep(from, to, 12);
      const expected = coarse.map(sample);
      for (let i = 1; i < coarse.length; i++) {
        if (expected[i] !== expected[i - 1]) {
          for (const length of sweep(Math.max(from, coarse[i - 1] - 6), Math.min(to, coarse[i] + 6), 1)) {
            if (!sampled.has(length)) {
              sample(length);
            }
          }
        }
      }
      expect(outcomes, orientation).toContain('broken');
      expect(outcomes, orientation).toContain('unbroken');
    }
  });

  /**
   * Eight horizontal models, where the category axis autoskips. Chart.js fits that axis at the full
   * plot height first, decides the value axis against what is left beside it, then refits the
   * category axis at the shorter final height and never refits the value axis: where the refit
   * drops ticks the category axis narrows, and the value axis ends up wider than the width its
   * title was decided on. That can leave a break the final width did not need, and nothing else:
   * an unbroken title always fits. At the default axis text size and the supported heights (the
   * page's horizontal panels are at least 260 px tall, an export's plot at least 160 px) the
   * decision matches the final width exactly.
   */
  it('decides against the final width with eight horizontal models, a break left unneeded only where the category axis autoskipped', () => {
    let skipped = false;
    for (const axisTextSize of [11, 24]) {
      const spec = costPanel('horizontal', { labels: 'eight', axisTextSize });
      for (const height of [110, 160, 200, 260]) {
        for (const width of sweep(260, 1000, 24)) {
          const context = `eight ${axisTextSize}px ${width}x${height}`;
          const chart = mount(spec, width, height);
          const autoskipped = chart.scales['y'].ticks.length < 8;
          skipped ||= autoskipped;
          const scale = valueScale(chart, 'horizontal');
          const unbrokenFits = widest(chart, scale, [COST_TITLE]) <= scale.width - AXIS_TITLE_RESERVE_PX;
          const broken = (titleText(scale) as string[]).length === 2;
          if (axisTextSize === 11 && height >= 160) {
            expectFinalDecision(chart, 'horizontal', false, context);
          } else if (!(broken && unbrokenFits && autoskipped)) {
            expectFinalDecision(chart, 'horizontal', false, context);
          }
          // Whatever was decided, an unbroken title is never squeezed.
          if (!broken) {
            expect(unbrokenFits, context).toBe(true);
          }
          unmount(chart);
        }
      }
    }
    expect(skipped, 'the category axis autoskipped somewhere').toBe(true);
  });

  it('keeps the whole broken title where even one line is longer than a too-short axis', () => {
    const spec = costPanel('vertical');
    const chart = mount(spec, 420, 110);
    const scale = valueScale(chart, 'vertical');
    const lines = axisTitleLines(COST_TITLE);
    expect(titleText(scale)).toEqual([...lines.broken!]);
    // The documented limit: one break is all there is, and the head alone does not fit.
    expect(widest(chart, scale, [lines.broken![0]])).toBeGreaterThan(scale.height);
  });

  it('breaks, restores and breaks again as one chart is resized', () => {
    const spec = costPanel('vertical');
    const source = sourceTitle(spec, 'vertical');
    const chart = mount(spec, 420, 200);
    const unbroken = [COST_TITLE];
    const broken = ['Total run cost including grading roles', '(USD)'];
    expect(titleText(chart.scales['y'])).toEqual(broken);
    chart.resize(420, 700);
    expect(titleText(chart.scales['y'])).toEqual(unbroken);
    expectFinalDecision(chart, 'vertical', false, 'grown');
    chart.resize(420, 200);
    expect(titleText(chart.scales['y'])).toEqual(broken);
    expect(source.title.text).toEqual(source.copy);
  });

  it('lets two charts from one spec decide apart, each in its own options', () => {
    const spec = costPanel('vertical');
    const source = sourceTitle(spec, 'vertical');
    const short = mount(spec, 420, 200);
    const tall = mount(spec, 420, 700);
    expect(titleText(short.scales['y'])).toEqual(['Total run cost including grading roles', '(USD)']);
    expect(titleText(tall.scales['y'])).toEqual([COST_TITLE]);
    short.update();
    expect(titleText(tall.scales['y'])).toEqual([COST_TITLE]);
    expect(source.title.text).toEqual(source.copy);
  });

  it('does not change its decision with the pixel density alone', () => {
    const spec = costPanel('vertical');
    for (const height of sweep(200, 600, 50)) {
      const one = mount(spec, 420, height, 1);
      const two = mount(spec, 420, height, 2);
      expect(titleText(two.scales['y']), String(height)).toEqual(titleText(one.scales['y']));
    }
  });
});

// ---------------------------------------------------------------------------------------------
// The figure theme: colours, fonts and the plot frame
// ---------------------------------------------------------------------------------------------

describe('the figure theme in the charts', () => {
  const LATO = '"Lato", system-ui, sans-serif';

  interface FontProbe {
    family?: string;
    weight?: number | string;
    size?: number;
  }

  interface ThemedScaleProbe {
    title?: { color?: string; font?: FontProbe };
    ticks?: { color?: string; font?: FontProbe };
    grid?: { color?: string };
    border?: { color?: string };
  }

  function themedScale(config: unknown, axis: 'x' | 'y'): ThemedScaleProbe {
    return (config as { options?: { scales?: Record<string, ThemedScaleProbe> } }).options?.scales?.[axis] ?? {};
  }

  function pluginsOf(config: unknown): Record<string, Record<string, unknown> | undefined> {
    return (config as { options: { plugins: Record<string, Record<string, unknown> | undefined> } }).options.plugins;
  }

  function legendLabels(config: unknown): Record<string, unknown> & { font?: FontProbe } {
    return (pluginsOf(config)['legend'] as { labels: Record<string, unknown> & { font?: FontProbe } }).labels;
  }

  function datalabelsFont(config: unknown): FontProbe | undefined {
    return (pluginsOf(config)['datalabels'] as { font?: FontProbe }).font;
  }

  function directOptions(config: unknown): DirectLabelPluginOptions {
    return pluginsOf(config)[directLabelPlugin.id] as unknown as DirectLabelPluginOptions;
  }

  function styled(
    appearance: Partial<FigureAppearanceStyle>,
    bar: Partial<BarFigureStyle> = {},
    scatter: Partial<ScatterFigureStyle> = {},
  ): FigureStyle {
    return {
      ...DEFAULT_FIGURE_STYLE,
      bar: { ...DEFAULT_FIGURE_STYLE.bar, ...bar },
      scatter: { ...DEFAULT_FIGURE_STYLE.scatter, ...scatter },
      appearance: { ...DEFAULT_APPEARANCE_STYLE, ...appearance },
    };
  }

  function figuresWith(options: Partial<FigureSetOptions> = {}) {
    return buildComparisonFigures(PROFILE_FIXTURE, { context: CONTEXT, ...options });
  }

  /** Every configuration and plugin list, with callbacks reduced to a marker so two builds compare as text. */
  function snapshot(figures: ReturnType<typeof buildComparisonFigures>): string {
    return JSON.stringify(
      {
        specs: allSpecs(figures).map((spec) => ({ id: spec.id, config: spec.config, plugins: spec.plugins.map((plugin) => plugin.id) })),
        profile: { tiles: figures.profile.tiles, chrome: figures.profile.chrome, plugins: figures.profile.plugins.map((plugin) => plugin.id) },
      },
      (_key, value: unknown) => (typeof value === 'function' ? '[function]' : value),
    );
  }

  describe('the default output', () => {
    const extra: Partial<FigureSetOptions> = { directLabels: true, inlineValues: true, selectedKeys: ['A'], highlightedKey: 'B' };

    it('is the same with no theme, the default style, and the dark theme passed explicitly', () => {
      const bare = figuresWith(extra);
      expect(snapshot(figuresWith({ ...extra, style: DEFAULT_FIGURE_STYLE }))).toBe(snapshot(bare));
      expect(snapshot(figuresWith({ ...extra, theme: resolveFigureTheme() }))).toBe(snapshot(bare));
      expect(snapshot(figuresWith({ ...extra, theme: resolveFigureTheme(DEFAULT_APPEARANCE_STYLE) }))).toBe(snapshot(bare));
    });

    it('sets no family, weight or colour key where none was set before a theme existed', () => {
      const figures = figuresWith();
      const labelled = figuresWith({ directLabels: true });

      const scatterX = themedScale(figures.qualitySpeed.config, 'x');
      expect(scatterX.ticks?.font).toEqual({ family: LATO, size: 11 });
      expect(scatterX.title?.font).toEqual({ size: 12 });
      expect('font' in legendLabels(figures.qualitySpeed.config)).toBe(false);
      expect(Object.keys(pluginsOf(figures.qualitySpeed.config))).toEqual(['legend', 'tooltip', 'datalabels']);
      expect(Object.keys(directOptions(labelled.qualitySpeed.config)))
        .toEqual(['blocks', 'highlightedIndex', 'fontSizePx', 'markRadiusPx', 'avoidWhiskers']);

      const panel = figures.smallMultiples.quality.config;
      expect(themedScale(panel, 'x').ticks?.font).toEqual({ family: LATO, size: 11 });
      expect(themedScale(panel, 'x').title?.font).toEqual({ size: 12 });
      expect(themedScale(panel, 'y').title?.font).toEqual({ size: 12 });
      expect(datalabelsFont(panel)).toEqual({ size: 11 });
      expect(Object.keys(pluginsOf(panel))).toEqual(['legend', 'tooltip', 'datalabels']);

      const tile = figures.profile.tiles[0];
      expect(themedScale(tile, 'x').ticks?.font).toEqual({ family: LATO, size: 10 });
      expect(pluginsOf(tile)['legend']).toEqual({ display: false });
      expect(tileOptionsOf(tile).fontFamily).toBe(LATO);
    });
  });

  describe('the light theme', () => {
    const light = styled({ theme: 'light' });
    const theme = resolveFigureTheme(light.appearance);

    it('reaches the surface, the inks, the gridlines and the axis baseline', () => {
      const figures = figuresWith({ style: light });
      const x = themedScale(figures.qualitySpeed.config, 'x');
      expect(x.ticks?.color).toBe(theme.chart.inkMuted);
      expect(x.ticks?.color).not.toBe(CHART_INK.muted);
      expect(x.grid?.color).toBe(theme.chart.gridline);
      expect(x.border?.color).toBe(theme.chart.baseline);
      expect(x.title?.color).toBe(theme.chart.inkSecondary);
      // A solid mark wears a ring of the surface it sits on.
      expect(datasetsOf(figures.qualitySpeed.config)[0]['borderColor']).toBe('#ffffff');
      expect(legendLabels(figures.qualitySpeed.config)['color']).toBe(theme.chart.inkSecondary);

      const panel = figures.smallMultiples.quality.config;
      expect(themedScale(panel, 'x').ticks?.color).toBe(theme.chart.inkSecondary);
      expect(themedScale(panel, 'y').ticks?.color).toBe(theme.chart.inkMuted);
      expect(themedScale(panel, 'y').grid?.color).toBe(theme.chart.gridline);
      expect((pluginsOf(panel)['datalabels'] as { color?: string }).color).toBe(theme.chart.inkSecondary);

      const profile = datasetsOf(figures.profile.tiles[0]);
      expect(profile[0]['pointBorderColor']).toBe('#ffffff');
      expect(profile[0]['borderColor']).toBe(CATEGORICAL_PALETTE_LIGHT[0]);
      expect(profile[1]['borderColor']).toBe(theme.chart.accent);
      expect(profile.slice(2).every((dataset) => dataset['borderColor'] === theme.chart.deEmphasisStroke)).toBe(true);
    });

    it('gives the glyphs and the three panels the light provider hues', () => {
      const figures = figuresWith({ style: light });
      expect(PROFILE_FIXTURE.map((entry) => glyphFor(figures.glyphs, entry.key).hue)).toEqual([...CATEGORICAL_PALETTE_LIGHT]);
      for (const panel of [figures.smallMultiples.quality, figures.smallMultiples.speed, figures.smallMultiples.cost]) {
        expect(datasetsOf(panel.config)[0]['backgroundColor'], panel.id).toEqual(['#2a78d6', '#eb6834', '#18a070']);
      }
    });

    it('marks emphasis in the light accent and the rest in the light de-emphasis', () => {
      const figures = figuresWith({ style: light, selectedKeys: ['A'], highlightedKey: 'A' });
      const bars = datasetsOf(figures.smallMultiples.quality.config)[0];
      expect(bars['backgroundColor']).toEqual(['#9a6b12', theme.chart.deEmphasisFill, theme.chart.deEmphasisFill]);
      expect(bars['borderColor']).toEqual(['#9a6b12', theme.chart.deEmphasisStroke, theme.chart.deEmphasisStroke]);
      expect(datasetsOf(figures.qualitySpeed.config)[0]['borderColor']).toBe(theme.chart.accent);
      // An emphasised model keeps its provider hue on the profile, and its tile is outlined in the accent.
      expect(datasetsOf(figures.profile.tiles[0])[0]['borderColor']).toBe(CATEGORICAL_PALETTE_LIGHT[0]);
      expect(tileOptionsOf(figures.profile.tiles[0]).outlineColor).toBe(theme.chart.accent);
    });

    it('hands the plugins their colours through the chart options', () => {
      const figures = figuresWith({ style: light, directLabels: true });
      const scatter = pluginsOf(figures.qualitySpeed.config);
      expect(scatter[errorBarPlugin.id]).toEqual({ color: theme.chart.inkMuted });
      const direct = directOptions(figures.qualitySpeed.config);
      expect(direct.surfaceColor).toBe('#ffffff');
      expect(direct.accentColor).toBe(theme.chart.accent);
      expect(direct.inkColor).toBe(theme.chart.inkSecondary);
      expect(direct.mutedColor).toBe(theme.chart.inkMuted);
      expect(pluginsOf(figures.smallMultiples.cost.config)[errorBarPlugin.id]).toEqual({ color: theme.chart.inkMuted });
    });
  });

  it('carries only the colours that differ from the dark defaults', () => {
    const custom = styled({ background: 'custom', backgroundColor: '#202020' });
    const figures = figuresWith({ style: custom, directLabels: true });
    expect(datasetsOf(figures.qualitySpeed.config)[0]['borderColor']).toBe('#202020');
    const direct = directOptions(figures.qualitySpeed.config);
    expect(direct.surfaceColor).toBe('#202020');
    expect('accentColor' in direct).toBe(false);
    expect('inkColor' in direct).toBe(false);
    expect(pluginsOf(figures.qualitySpeed.config)[errorBarPlugin.id]).toBeUndefined();
  });

  describe('the plugins draw in the colours they are given', () => {
    it('strokes the whiskers in the option colour', () => {
      const strokes: unknown[] = [];
      const ctx = {
        save: () => undefined,
        restore: () => undefined,
        beginPath: () => undefined,
        moveTo: () => undefined,
        lineTo: () => undefined,
        stroke: () => {
          strokes.push(ctx.strokeStyle);
        },
        strokeStyle: '',
        lineWidth: 0,
      };
      const scale = { type: 'linear', min: 0, getPixelForValue: (value: number) => value };
      const chart = {
        ctx,
        chartArea: { left: 0, top: 0, right: 100, bottom: 100 },
        data: { datasets: [{ data: [{ x: 1, y: 10, yErrLow: 1, yErrHigh: 1 }] }] },
        scales: { x: scale, y: scale },
        getDatasetMeta: () => ({ hidden: false, xAxisID: 'x', yAxisID: 'y', data: [{ x: 5, y: 5 }] }),
      };
      errorBarPlugin.afterDatasetsDraw?.(chart as unknown as Chart, {} as never, { color: '#6b6a66' } as never, false);
      expect(strokes).toEqual(['#6b6a66']);
    });

    it('draws the direct-label halos, names and leaders in the option colours', () => {
      const fills: unknown[] = [];
      const rectFills: unknown[] = [];
      const strokes: unknown[] = [];
      const halos: unknown[] = [];
      const noop = (): void => undefined;
      const ctx = {
        save: noop,
        restore: noop,
        beginPath: noop,
        moveTo: noop,
        lineTo: noop,
        stroke: () => {
          strokes.push(ctx.strokeStyle);
        },
        fillRect: () => {
          rectFills.push(ctx.fillStyle);
        },
        fillText: () => {
          fills.push(ctx.fillStyle);
        },
        strokeText: () => {
          halos.push(ctx.strokeStyle);
        },
        measureText: (text: string) => ({ width: text.length * 6 }),
        strokeStyle: '',
        fillStyle: '',
        globalAlpha: 1,
        lineWidth: 0,
        lineJoin: '',
        font: '',
        textBaseline: '',
        textAlign: '',
      };
      const marks = [{ x: 100, y: 100 }, { x: 200, y: 180 }];
      const chart = {
        ctx,
        chartArea: { left: 0, top: 0, right: 400, bottom: 300 },
        data: { datasets: marks.map(() => ({ data: [] })) },
        getDatasetMeta: (index: number) => ({ hidden: false, data: marks[index] ? [marks[index]] : [] }),
      };
      const options: DirectLabelPluginOptions = {
        blocks: [
          { name: 'Model A', values: [], hue: '#2a78d6' },
          { name: 'Model B', values: [], hue: '#eb6834' },
        ],
        highlightedIndex: 1,
        surfaceColor: '#ffffff',
        accentColor: '#9a6b12',
        inkColor: '#52514e',
        mutedColor: '#6b6a66',
      };
      directLabelPlugin.afterDatasetsDraw?.(chart as unknown as Chart, {} as never, options as never, false);

      // Names alone draw no plate: the surface reaches them as the halo.
      expect(rectFills).toEqual(['#2a78d6', '#9a6b12']);
      expect(halos).toEqual(['#ffffff', '#ffffff']);
      expect(fills).toContain('#9a6b12');
      expect(fills).toContain('#52514e');
      expect(fills).not.toContain(ACCENT);
      expect(strokes.every((stroke) => stroke === '#6b6a66')).toBe(true);
    });
  });

  describe('fonts', () => {
    const inter = figureFont('inter').stack;

    it('sets a bundled family on ticks, axis titles, legends, value labels and direct labels', () => {
      const style = styled({ fontFamily: 'inter' });
      const figures = figuresWith({ style });
      const labelled = figuresWith({ style, directLabels: true });

      const scatterX = themedScale(figures.qualitySpeed.config, 'x');
      expect(scatterX.ticks?.font?.family).toBe(inter);
      expect(scatterX.title?.font?.family).toBe(inter);
      expect(legendLabels(figures.qualitySpeed.config).font?.family).toBe(inter);
      expect(directOptions(labelled.qualitySpeed.config).fontFamily).toBe(inter);

      const panel = figures.smallMultiples.quality.config;
      expect(themedScale(panel, 'x').ticks?.font?.family).toBe(inter);
      expect(themedScale(panel, 'x').title?.font?.family).toBe(inter);
      expect(themedScale(panel, 'y').ticks?.font?.family).toBe(inter);
      expect(themedScale(panel, 'y').title?.font?.family).toBe(inter);
      expect(datalabelsFont(panel)?.family).toBe(inter);

      const tile = figures.profile.tiles[0];
      expect(themedScale(tile, 'x').ticks?.font?.family).toBe(inter);
      expect(tileOptionsOf(tile).fontFamily).toBe(inter);

      // At the default weights nothing carries a weight key.
      expect(scatterX.ticks?.font?.weight).toBeUndefined();
      expect(scatterX.title?.font?.weight).toBeUndefined();
      expect(datalabelsFont(panel)?.weight).toBeUndefined();
    });

    it('draws value labels, legends and direct-label names in the label weight, and ticks at 400', () => {
      const style = styled({ labelWeight: 600 });
      const figures = figuresWith({ style });
      const labelled = figuresWith({ style, directLabels: true });

      expect(datalabelsFont(figures.smallMultiples.speed.config)).toEqual({ size: 11, weight: 600 });
      expect(legendLabels(figures.qualitySpeed.config).font).toEqual({ weight: 600 });
      expect(directOptions(labelled.qualitySpeed.config).nameFontWeight).toBe(600);
      expect('fontFamily' in directOptions(labelled.qualitySpeed.config)).toBe(false);

      const scatterX = themedScale(figures.qualitySpeed.config, 'x');
      expect(scatterX.ticks?.font).toEqual({ family: LATO, size: 11 });
      expect(scatterX.title?.font).toEqual({ size: 12 });
    });

    it('draws each family\'s axis titles in its own axis title weight', () => {
      const figures = figuresWith({ style: styled({}, { axisTitleWeight: 700 }, { axisTitleWeight: 500 }) });

      const panel = figures.smallMultiples.cost.config;
      expect(themedScale(panel, 'x').title?.font).toEqual({ size: 12, weight: 700 });
      expect(themedScale(panel, 'y').title?.font).toEqual({ size: 12, weight: 700 });
      expect(themedScale(panel, 'y').ticks?.font?.weight).toBeUndefined();

      expect(themedScale(figures.speedCost.config, 'x').title?.font).toEqual({ size: 12, weight: 500 });
      expect(themedScale(figures.speedCost.config, 'y').title?.font).toEqual({ size: 12, weight: 500 });
      expect(themedScale(figures.speedCost.config, 'y').ticks?.font?.weight).toBeUndefined();

    });

    it('measures a plate in the family and the name weight it is given', () => {
      const fonts: string[] = [];
      const ctx = {
        set font(value: string) {
          fonts.push(value);
        },
        get font(): string {
          return fonts[fonts.length - 1] ?? '';
        },
        measureText: (text: string) => ({ width: text.length * 6 }),
      };
      const block: DirectLabelBlock = { name: 'Model A', values: [{ label: 'Intelligence', text: '82.4' }], hue: '#3987e5' };

      measureDirectLabelBlock(ctx as unknown as CanvasRenderingContext2D, block, 11);
      expect(fonts).toEqual([`11px ${LATO}`, `10px ${LATO}`]);

      fonts.length = 0;
      measureDirectLabelBlock(ctx as unknown as CanvasRenderingContext2D, block, 11, { family: inter, nameWeight: 600 });
      expect(fonts).toEqual([`600 11px ${inter}`, `10px ${inter}`]);
    });
  });

  describe('the plot frame', () => {
    const scatters = (figures: ReturnType<typeof buildComparisonFigures>) =>
      [figures.qualitySpeed, figures.qualityCost, figures.speedCost];
    const panels = (figures: ReturnType<typeof buildComparisonFigures>) =>
      [figures.smallMultiples.quality, figures.smallMultiples.speed, figures.smallMultiples.cost];

    it('is off by default', () => {
      const figures = figuresWith();
      expect(allSpecs(figures).some((spec) => spec.plugins.includes(plotFramePlugin))).toBe(false);
      expect(allSpecs(figures).some((spec) => pluginsOf(spec.config)[plotFramePlugin.id] !== undefined)).toBe(false);
    });

    it('frames the bar panels from the bar style and the scatters from the trade-off style', () => {
      const barFramed = figuresWith({ style: styled({}, { plotFrame: true }) });
      expect(panels(barFramed).every((spec) => spec.plugins.includes(plotFramePlugin))).toBe(true);
      expect(scatters(barFramed).some((spec) => spec.plugins.includes(plotFramePlugin))).toBe(false);
      expect(pluginsOf(barFramed.smallMultiples.speed.config)[plotFramePlugin.id]).toEqual({ color: CHART_INK.baseline });

      const scatterFramed = figuresWith({ style: styled({}, {}, { plotFrame: true }) });
      expect(scatters(scatterFramed).every((spec) => spec.plugins.includes(plotFramePlugin))).toBe(true);
      expect(panels(scatterFramed).some((spec) => spec.plugins.includes(plotFramePlugin))).toBe(false);
    });

    it('never frames the profile, and takes the theme\'s frame colour', () => {
      const style = styled({ theme: 'light' }, { plotFrame: true }, { plotFrame: true });
      const figures = figuresWith({ style });
      expect(figures.profile.plugins).not.toContain(plotFramePlugin);
      for (const tile of figures.profile.tiles) {
        expect(pluginsOf(tile)[plotFramePlugin.id]).toBeUndefined();
      }
      expect(pluginsOf(figures.qualityCost.config)[plotFramePlugin.id])
        .toEqual({ color: resolveFigureTheme(style.appearance).frameColor });
    });

    it('strokes a 1 px hairline around the plot area', () => {
      const calls: { op: string; args: unknown[]; strokeStyle: string; lineWidth: number }[] = [];
      const ctx = {
        save: () => undefined,
        restore: () => undefined,
        strokeRect: (...args: unknown[]) => {
          calls.push({ op: 'strokeRect', args, strokeStyle: ctx.strokeStyle, lineWidth: ctx.lineWidth });
        },
        strokeStyle: '',
        lineWidth: 0,
      };
      const chart = { ctx, chartArea: { left: 10, top: 20, right: 400, bottom: 300 } };
      plotFramePlugin.afterDraw?.(chart as unknown as Chart, {} as never, { color: '#c3c2b7' } as never);
      expect(calls).toEqual([{ op: 'strokeRect', args: [10.5, 20.5, 389, 279], strokeStyle: '#c3c2b7', lineWidth: 1 }]);
    });
  });
});
