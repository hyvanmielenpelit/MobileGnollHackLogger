/**
 * Chart core for the cross-model benchmark comparison view: the palette and its provider colors, the
 * per-model identity glyphs, the measure definitions, the Pareto-frontier computation, the error-bar,
 * direct-label and profile-tile plugins and the configurations for the six figures (S1-S3 scatters,
 * P1's three linked panels, P2's small-multiples model profiles).
 *
 * The module is pure TypeScript: it constructs no components, touches no DOM node and imports
 * nothing from Angular, so its spec runs without a TestBed fixture. The one browser API it reaches
 * for is `matchMedia`, behind a guard and behind an injectable factory, so the reduced-motion
 * branch is testable without a real media query.
 */

import ChartDataLabels from 'chartjs-plugin-datalabels';
import { Chart } from 'chart.js';
import type { ChartConfiguration, ChartType, DefaultDataPoint, FontSpec, Plugin, Point, Scale } from 'chart.js';
import { toFont } from 'chart.js/helpers';
import { chooseScaleType, formatTick, linearDomain, logDomain, timeUnitFor } from './axis-domain';
import type { AxisBounds, AxisTickKind, ScaleType, TimeUnit } from './axis-domain';
import { pricingBadge, pricingNote, questionsBadge, runsBadge, visibleBadges } from './figure-chrome';
import type { FigureBadge, FigureChrome, FigureDirection, FigureKeyItem, FigureNote } from './figure-chrome';
import { DEFAULT_FIGURE_STYLE, HIDDEN_INTERVALS_NOTE } from './figure-style';
import type { AxisTitleBreak, FigureFontWeight, FigureStyle, ScatterFigureStyle } from './figure-style';
import {
  FADED_MARK_ALPHA,
  providerDisplayName,
  providerHue,
  providerKey,
  providerPaletteHue,
  resolveFigureTheme,
  withAlpha,
} from './figure-theme';
import type { ProviderKey, ResolvedFigureTheme } from './figure-theme';
import { DEFAULT_MEASURE_DECIMALS, costNumberMeasure, formatMeasure, speedNumberMeasure } from './measure-format';
import type { NumberFormatStyle, NumberMeasure, NumberSamples } from './measure-format';

// ---------------------------------------------------------------------------------------------
// Input model
// ---------------------------------------------------------------------------------------------

/**
 * One plotted candidate model, aggregated over a single run (R = 1) or a Tier A group (R >= 2).
 *
 * This is the view's own input shape, deliberately narrow: the server DTO is adapted onto it in the
 * component layer, so a rename on the wire never reaches the chart code.
 */
export interface ModelComparisonEntry {
  /** Stable identity of the entry across sorts, filters and reloads. Drives the identity glyph. */
  readonly key: string;
  /**
   * Display name followed by the thinking level in parentheses (`GPT-5.6 Luna (max)`): the
   * one-line form, used wherever a label is not broken.
   */
  readonly label: string;
  /** The display name without the thinking level. Absent: `label` is never broken. */
  readonly name?: string;
  /** Lower-case thinking level, or null/absent when the entry has none. */
  readonly thinkingLevel?: string | null;
  /**
   * Lower-case provider (`openai`, `anthropic`, `google`), which colors every mark of the entry.
   * Absent or unknown: the neutral gray.
   */
  readonly provider?: string;
  /** R - the number of runs behind the entry. R = 1 draws hollow marks and carries no cost SD. */
  readonly runCount: number;

  /** Intelligence Index point estimate, 0-100. */
  readonly intelligenceIndex: number;
  /**
   * Half-width of the 95 % interval on the Intelligence Index: item-sampling only at R = 1,
   * item-sampling combined with reproducibility at R >= 3.
   */
  readonly intelligenceIndexCi95HalfWidth: number;

  /**
   * Speed Index, 0-100. Null when the entry carries no index. Used only by P1's speed panel; the
   * scatters exclude it outright because it saturates at the ceiling and would collapse an axis.
   */
  readonly speedIndex: number | null;
  /** True when the entry's Speed Index sits at or near the ceiling, which makes ties meaningless. */
  readonly speedIndexSaturated: boolean;
  /** Reproducibility SD of the Speed Index across runs, where R >= 2 makes one available. */
  readonly speedIndexSd: number | null;

  /** Time to first token, P50, in milliseconds - the latency a chat user actually perceives. */
  readonly ttftP50Ms: number;
  /** Time to first token, P90, in milliseconds. The upper whisker; latency is right-skewed. */
  readonly ttftP90Ms: number;

  /** Mean model time per question, ms - turn duration with tool I/O removed. UNMEASURED when absent. */
  readonly modelTimeMeanMs: number;
  /** Mean per-run total model time for the whole suite, ms. UNMEASURED when absent. */
  readonly totalModelTimeMs: number;
  /** SD of totalModelTimeMs across runs. Null below R = 2, where no run-to-run spread exists. */
  readonly totalModelTimeSdMs: number | null;

  /** Candidate-only cost of one question, in USD, on the request's pricing basis. */
  readonly candidateCostPerQuestionUsd: number;
  /** SD of that cost across runs. Null at R = 1, where P1's category tick says `n = 1` instead. */
  readonly candidateCostPerQuestionSdUsd: number | null;
  /** Candidate-only mean cost of one run, in USD. UNMEASURED when absent. */
  readonly candidateCostPerRunUsd: number;
  /** Cost of the whole run including grading roles, in USD. */
  readonly totalRunCostUsd: number;
  /** SD of the total run cost across runs. Null at R = 1. */
  readonly totalRunCostSdUsd: number | null;

  /** Set when `QuestionParallelism` differs from the set's baseline: the speed axis is untrustworthy. */
  readonly speedDegraded: boolean;
  /** Set when `PricingSnapshot` differs from the set's baseline: the cost axis is untrustworthy. */
  readonly costDegraded: boolean;
  /** Set when a Fundamental or Instrument key differs: the entry is never plotted, only tabulated. */
  readonly excluded: boolean;
  /** Names of the comparability keys that differ, so the table can say why rather than only that. */
  readonly excludedReasonKeys: readonly string[];
}

/** Facts shared by every entry in a comparable set, used for figure chrome. */
export interface ModelComparisonContext {
  /** Fewest scored questions behind any charted entry's index. */
  readonly scoredItemsMin: number;
  /** Most scored questions behind any charted entry's index. */
  readonly scoredItemsMax: number;
  /** Questions these runs were asked, or 0 when unknown. */
  readonly examItemCount: number;
  /** The asked count every charted entry shares, or null when they differ. Labels only; no arithmetic reads it. */
  readonly questionsAskedPerRun: number | null;
  /** The pricing basis and its date, as the view's header, methods block and table name it. */
  readonly pricingBasisLabel: string;
  /** The pricing basis key: `'Current'`, `'AsRun'` or `''`. Drives the pricing badge and note on cost figures. */
  readonly pricingBasis: string;
  /** ISO timestamp the comparison was priced on (the DTO's `computedAtUtc`), or `''`. */
  readonly pricedOn: string;
  /** Suite name, for figure titles. */
  readonly suiteName: string;
  /** What one candidate-cost figure covers; absent reads as a suite run. */
  readonly costUnit?: 'suite run' | 'battery pass';
}

/** `GPT-5.6 Luna (max)`; the name alone when there is no thinking level. */
export function modelLabelText(name: string, thinkingLevel: string | null | undefined): string {
  return thinkingLevel ? `${name} (${thinkingLevel})` : name;
}

/** The label's drawn lines: the name, then `(level)` under it when broken; otherwise the one-line label. */
export function modelLabelLines(entry: ModelComparisonEntry, breakThinkingLevel: boolean): string | string[] {
  return breakThinkingLevel && entry.name && entry.thinkingLevel
    ? [entry.name, `(${entry.thinkingLevel})`]
    : entry.label;
}

// ---------------------------------------------------------------------------------------------
// Palette
// ---------------------------------------------------------------------------------------------

/**
 * The dark theme's composited chart surface. The panels these charts sit in on the page are
 * `--bg-glass`, `rgba(17, 17, 17, 0.65)`, over Overseer's black body, which composites to `#0b0b0b`
 * (17 x 0.65 = 11.05 -> 0x0b). The builders draw from the resolved figure theme
 * (`figure-theme.ts`); this constant and the others below are the dark theme's values, which a
 * build without a theme draws in.
 */
export const CHART_SURFACE = '#0b0b0b';

/**
 * The categorical series hues, one per colored provider: Google, Anthropic, OpenAI, in that order
 * (`providerHue` in `figure-theme.ts`). There are two palettes, one per theme, each selected for its
 * own ground rather than one flipped into the other: the dark one for the dark export ground
 * `#181818`, the light one for white, PowerPoint's default slide.
 *
 * Three, because every figure that uses hue here includes an all-pairs form - a scatter, where any
 * two marks can end up side by side - and an all-pairs palette caps at three slots. Color therefore
 * means provider, and model names are carried by direct labels.
 *
 * Validate both with, from the dataviz skill's base directory:
 *   node scripts/validate_palette.js "#3987e5,#d95926,#199e70" --mode dark --surface "#181818" --pairs all
 *   node scripts/validate_palette.js "#2a78d6,#eb6834,#18a070" --mode light --surface "#ffffff" --pairs all
 */
export const CATEGORICAL_PALETTE_DARK = ['#3987e5', '#d95926', '#199e70'] as const;

/** The light theme's hues. The third is darker than the reference aqua so that it keeps 3:1 on white. */
export const CATEGORICAL_PALETTE_LIGHT = ['#2a78d6', '#eb6834', '#18a070'] as const;

/** The exact string the palette validator takes, so the check is a copy-paste and never a retype. */
export const PALETTE_VALIDATION_INPUT: string = CATEGORICAL_PALETTE_DARK.join(',');

/** {@link PALETTE_VALIDATION_INPUT} for the light palette. */
export const PALETTE_VALIDATION_INPUT_LIGHT: string = CATEGORICAL_PALETTE_LIGHT.join(',');

/**
 * The dark theme's emphasis accent: Overseer's own `--gh-gold`. It is an emphasis token, not a
 * series identity colour - it marks whichever model is hovered or selected and never carries
 * identity on its own - so it stays out of the categorical set the validator gates. Contrast on
 * `#0b0b0b` is ~10.7:1.
 */
export const ACCENT = '#e0ba6d';

/** The dark theme's chart chrome and ink. Text always wears the ink, never a series hue. */
export const CHART_INK = {
  primary: '#ffffff',
  secondary: '#c3c2b7',
  muted: '#898781',
  gridline: '#2c2c2a',
  baseline: '#383835',
} as const;

/**
 * The dark theme's de-emphasis for marks that are not the emphasised one: the muted ink at 45 % over the surface.
 * It reuses a documented ink rather than introducing an undocumented gray, and the opacity keeps it
 * well below the axis labels drawn in the same hue at full strength.
 */
export const DE_EMPHASIS_FILL = 'rgba(137, 135, 129, 0.45)';
export const DE_EMPHASIS_STROKE = '#898781';

/** Tooltip chrome, matching the surrounding admin views. On screen only, so it keeps the dark theme. */
const TOOLTIP_STYLE = {
  backgroundColor: 'rgba(22, 22, 22, 0.95)',
  titleColor: CHART_INK.primary,
  bodyColor: CHART_INK.secondary,
  borderColor: 'rgba(224, 186, 109, 0.4)',
  borderWidth: 1,
  padding: 10,
  cornerRadius: 6,
} as const;

/** What a builder draws in when its options carry no theme: the dark theme, the constants above. */
const DEFAULT_THEME: ResolvedFigureTheme = resolveFigureTheme();

function themeOf(options: { readonly theme?: ResolvedFigureTheme }): ResolvedFigureTheme {
  return options.theme ?? DEFAULT_THEME;
}

/** The ticks' and direct labels' family under the Overseer default font; the other sites keep Chart.js's. */
const LATO_STACK = '"Lato", system-ui, sans-serif';

/** A Chart.js weight, or nothing at 400, which is the `normal` a site without a weight already draws. */
function weightKey(weight: FigureFontWeight): { weight?: number } {
  return weight === 400 ? {} : { weight };
}

/** The bundled family, or nothing under the Overseer default, where each site keeps its own family. */
function familyKey(theme: ResolvedFigureTheme): { family?: string } {
  return theme.fonts.chartStack === null ? {} : { family: theme.fonts.chartStack };
}

/** `{ [key]: value }` where the value differs from the plugin's dark default, so a dark build adds no key. */
function unlessDefault<K extends string>(key: K, value: string, fallback: string): Partial<Record<K, string>> {
  return value === fallback ? {} : ({ [key]: value } as Record<K, string>);
}

// ---------------------------------------------------------------------------------------------
// Identity glyphs
// ---------------------------------------------------------------------------------------------

/** Chart.js point styles kept as the shape half of the identity glyph. Chart marks are always circles. */
export const IDENTITY_SHAPES = ['circle', 'rectRot', 'triangle'] as const;
export type IdentityShape = (typeof IDENTITY_SHAPES)[number];

/**
 * A model's identity outside the plot: its provider's hue and a shape by position. The table draws
 * the provider's color as a circle; the charts color every mark by provider and name it directly.
 */
export interface IdentityGlyph {
  readonly hue: string;
  readonly shape: IdentityShape;
  /** The provider the hue comes from, as the table's color class names it. */
  readonly provider: ProviderKey;
}

/** The hard ceiling on plotted entries. */
export const MAX_PLOTTED_ENTRIES = 12;

/**
 * Assigns each plottable entry its identity glyph, in the order the service returned them.
 *
 * The hue is the provider's slot in `palette`, the theme's categorical set (Google, Anthropic,
 * OpenAI), or `otherHue` for any other provider. The shape follows the order, which deliberately
 * does not depend on the current sort or filter, so filtering a model out never reshapes the
 * survivors. Excluded entries take no glyph - they are never plotted, and their table row carries
 * the differing comparability keys instead.
 */
export function buildIdentityGlyphs(
  entries: readonly ModelComparisonEntry[],
  palette: readonly string[] = CATEGORICAL_PALETTE_DARK,
  otherHue: string = DE_EMPHASIS_STROKE,
): Map<string, IdentityGlyph> {
  const glyphs = new Map<string, IdentityGlyph>();
  let slot = 0;
  for (const entry of entries) {
    if (entry.excluded) {
      continue;
    }
    glyphs.set(entry.key, {
      hue: providerPaletteHue(entry.provider, palette, otherHue),
      shape: IDENTITY_SHAPES[slot % IDENTITY_SHAPES.length],
      provider: providerKey(entry.provider),
    });
    slot += 1;
  }
  return glyphs;
}

const FALLBACK_GLYPH: IdentityGlyph = { hue: DE_EMPHASIS_STROKE, shape: IDENTITY_SHAPES[0], provider: 'other' };

/** Looks a glyph up by entry key, falling back to the neutral gray rather than throwing inside a render. */
export function glyphFor(glyphs: ReadonlyMap<string, IdentityGlyph>, key: string): IdentityGlyph {
  return glyphs.get(key) ?? FALLBACK_GLYPH;
}

// ---------------------------------------------------------------------------------------------
// Measures, sorting and the plotted set
// ---------------------------------------------------------------------------------------------

/**
 * P1's speed panel switches between these four; the three scatters and the profile plot read
 * whichever one is selected off {@link speedValue} rather than a figure-specific fixed measure.
 */
export type SpeedMeasure = 'meanModelTime' | 'totalModelTime' | 'ttftP50' | 'speedIndex';
/** P1's cost panel switches between these two. The scatters always use candidate cost per question. */
export type CostMeasure = 'candidateSuite' | 'totalRun';

export type ModelSortKey = 'intelligenceIndex' | 'speed' | 'cost' | 'label' | 'custom';
export type SortDirection = 'asc' | 'desc';

export interface ModelSort {
  readonly key: ModelSortKey;
  readonly direction: SortDirection;
  /** Entry keys in the admin's order; read only while `key` is `'custom'`, which ignores `direction`. */
  readonly customOrder?: readonly string[];
}

/** One order control drives all three P1 panels; this is where it starts. */
export const DEFAULT_MODEL_SORT: ModelSort = { key: 'intelligenceIndex', direction: 'desc' };

/** Candidate cost of one suite run: the entry's own mean run cost, independent of any item count. */
export function suiteCostUsd(entry: ModelComparisonEntry): number {
  return entry.candidateCostPerRunUsd;
}

/** `1 question`, `18 questions`, `17.5 questions`: an averaged asked count, as labels print it. */
export function formatQuestionsAsked(n: number): string {
  const number = Number.isInteger(n) ? String(n) : n.toFixed(1);
  return `${number} ${n === 1 ? 'question' : 'questions'}`;
}

/** The per-question SD scaled by the same factor that turns the per-question cost into the run cost. */
export function suiteCostSdUsd(entry: ModelComparisonEntry): number | null {
  const perRun = entry.candidateCostPerRunUsd;
  const perQuestion = entry.candidateCostPerQuestionUsd;
  if (entry.candidateCostPerQuestionSdUsd === null
    || !Number.isFinite(perRun) || perRun <= 0
    || !Number.isFinite(perQuestion) || perQuestion <= 0) {
    return null;
  }
  return entry.candidateCostPerQuestionSdUsd * perRun / perQuestion;
}

/** The value P1's speed panel, the scatters and the profile plot read under the selected measure. */
export function speedValue(entry: ModelComparisonEntry, measure: SpeedMeasure): number | null {
  switch (measure) {
    case 'meanModelTime':
      return entry.modelTimeMeanMs;
    case 'totalModelTime':
      return entry.totalModelTimeMs;
    case 'ttftP50':
      return entry.ttftP50Ms;
    case 'speedIndex':
      return entry.speedIndex;
  }
}

/** The value P1's cost panel plots under the selected measure. */
export function costValue(
  entry: ModelComparisonEntry,
  measure: CostMeasure,
  _context: ModelComparisonContext,
): number {
  return measure === 'candidateSuite' ? suiteCostUsd(entry) : entry.totalRunCostUsd;
}

/** True when lower is the better direction for the measure - drives axis markers and the frontier. */
export function speedLowerIsBetter(measure: SpeedMeasure): boolean {
  return measure !== 'speedIndex';
}

/**
 * Orders entries for every figure at once. P1's three panels take this one order, so sorting on any
 * of them reorders all three; panels that sorted independently would stop a row meaning one model.
 *
 * The direction is the measure's own numeric direction, not "best first": ascending cost is the
 * cheapest first and ascending Intelligence Index is the weakest first, which is what a column
 * header arrow leads a reader to expect.
 *
 * `custom` orders by each entry's position in `sort.customOrder` and has no direction; entries the
 * list does not name follow the named ones.
 */
export function sortEntriesForComparison(
  entries: readonly ModelComparisonEntry[],
  sort: ModelSort,
  speedMeasure: SpeedMeasure,
  costMeasure: CostMeasure,
  context: ModelComparisonContext,
): ModelComparisonEntry[] {
  const sign = sort.key === 'custom' || sort.direction === 'asc' ? 1 : -1;
  const customRank = new Map<string, number>();
  if (sort.key === 'custom') {
    (sort.customOrder ?? []).forEach((key, index) => {
      if (!customRank.has(key)) {
        customRank.set(key, index);
      }
    });
  }
  const ordered = [...entries];
  ordered.sort((a, b) => {
    let delta = 0;
    switch (sort.key) {
      case 'custom':
        delta = (customRank.get(a.key) ?? Number.MAX_SAFE_INTEGER) - (customRank.get(b.key) ?? Number.MAX_SAFE_INTEGER);
        break;
      case 'intelligenceIndex':
        delta = a.intelligenceIndex - b.intelligenceIndex;
        break;
      case 'speed': {
        const av = speedValue(a, speedMeasure) ?? Number.NEGATIVE_INFINITY;
        const bv = speedValue(b, speedMeasure) ?? Number.NEGATIVE_INFINITY;
        delta = av - bv;
        break;
      }
      case 'cost':
        delta = costValue(a, costMeasure, context) - costValue(b, costMeasure, context);
        break;
      case 'label':
        delta = a.label.localeCompare(b.label);
        break;
    }
    if (delta !== 0) {
      return sign * delta;
    }
    // Ties resolve by name so a re-render never shuffles equal entries.
    return a.label.localeCompare(b.label);
  });
  return ordered;
}

export interface PlottedSelection {
  /** The entries the figures draw, in the shared order, capped at {@link MAX_PLOTTED_ENTRIES}. */
  readonly plotted: readonly ModelComparisonEntry[];
  /** Comparable entries the cap pushed out. They stay in the table view. */
  readonly overflow: readonly ModelComparisonEntry[];
  /** Entries a Fundamental or Instrument key difference removed from every chart. */
  readonly excluded: readonly ModelComparisonEntry[];
  /** Notices the view must render; the cap and the exclusions are never silent. */
  readonly notices: readonly string[];
}

/**
 * Splits a comparison set into what is drawn, what the eight-entry cap pushed out and what
 * comparability excluded. The cap is enforced here rather than described in documentation: a ninth
 * model is never given a generated hue or a reused shape, it is named in a notice and tabulated.
 */
export function selectPlottedEntries(
  entries: readonly ModelComparisonEntry[],
  sort: ModelSort,
  speedMeasure: SpeedMeasure,
  costMeasure: CostMeasure,
  context: ModelComparisonContext,
): PlottedSelection {
  const excluded = entries.filter((e) => e.excluded);
  const comparable = sortEntriesForComparison(
    entries.filter((e) => !e.excluded),
    sort,
    speedMeasure,
    costMeasure,
    context,
  );
  const plotted = comparable.slice(0, MAX_PLOTTED_ENTRIES);
  const overflow = comparable.slice(MAX_PLOTTED_ENTRIES);

  const notices: string[] = [];
  if (overflow.length > 0) {
    notices.push(
      `Charts plot at most ${MAX_PLOTTED_ENTRIES} models. ` +
        `${overflow.length} further comparable ${overflow.length === 1 ? 'model is' : 'models are'} ` +
        `listed in the table view only: ${overflow.map((e) => e.label).join(', ')}.`,
    );
  }
  if (excluded.length > 0) {
    notices.push(
      `${excluded.length} ${excluded.length === 1 ? 'entry is' : 'entries are'} not comparable with ` +
        'this set and are excluded from every chart. The table view names the differing keys.',
    );
  }
  return { plotted, overflow, excluded, notices };
}

/**
 * Every entry's key in the model order the charts use, so the table and the charts cannot disagree:
 * the comparable entries by {@link sortEntriesForComparison}, then the excluded ones, which have no
 * measures to sort by, by label. Under `custom` the list places excluded entries too.
 */
export function modelOrderKeys(
  entries: readonly ModelComparisonEntry[],
  sort: ModelSort,
  speedMeasure: SpeedMeasure,
  costMeasure: CostMeasure,
  context: ModelComparisonContext,
): string[] {
  if (sort.key === 'custom') {
    return sortEntriesForComparison(entries, sort, speedMeasure, costMeasure, context).map((e) => e.key);
  }
  const comparable = sortEntriesForComparison(
    entries.filter((e) => !e.excluded), sort, speedMeasure, costMeasure, context);
  const excluded = entries.filter((e) => e.excluded).sort((a, b) => a.label.localeCompare(b.label));
  return [...comparable, ...excluded].map((e) => e.key);
}

// ---------------------------------------------------------------------------------------------
// Reduced motion
// ---------------------------------------------------------------------------------------------

export const REDUCED_MOTION_QUERY = '(prefers-reduced-motion: reduce)';

/** Injectable seam for the media query, so the reduced-motion branch is testable without a browser. */
export type MediaQueryFactory = (query: string) => MediaQueryList | null;

const defaultMediaQueryFactory: MediaQueryFactory = (query) =>
  typeof window !== 'undefined' && typeof window.matchMedia === 'function' ? window.matchMedia(query) : null;

/**
 * Tracks `prefers-reduced-motion` and reports changes.
 *
 * It subscribes rather than sampling once: the setting can be toggled while the view is open, and a
 * chart built under the old answer would keep animating for the rest of the session.
 */
export class ReducedMotionWatcher {
  private readonly query: MediaQueryList | null;
  private readonly listeners = new Set<(reduced: boolean) => void>();
  private readonly onChange = (event: MediaQueryListEvent | MediaQueryList): void => {
    for (const listener of this.listeners) {
      listener(event.matches);
    }
  };

  constructor(factory: MediaQueryFactory = defaultMediaQueryFactory) {
    this.query = factory(REDUCED_MOTION_QUERY);
    this.query?.addEventListener('change', this.onChange);
  }

  /** Whether motion should currently be suppressed. False when no media query is available. */
  get matches(): boolean {
    return this.query?.matches ?? false;
  }

  /** Registers a change listener and returns its unsubscribe. */
  subscribe(listener: (reduced: boolean) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  dispose(): void {
    this.query?.removeEventListener('change', this.onChange);
    this.listeners.clear();
  }
}

// ---------------------------------------------------------------------------------------------
// The error-bar plugin
// ---------------------------------------------------------------------------------------------

/**
 * A parsed point carrying its own uncertainty. Chart.js does not copy unknown keys from the raw
 * datum into `parsed`, so the plugin reads the raw datum off `dataset.data[index]`, which is where
 * these values survive.
 */
export interface ErrorBarPoint extends Point {
  /**
   * Chart.js allows a null coordinate to punch a gap in a series. A mark carrying an interval
   * always has both coordinates, so they are narrowed here and the guard below enforces it.
   */
  x: number;
  y: number;
  readonly xErrLow?: number;
  readonly xErrHigh?: number;
  readonly yErrLow?: number;
  readonly yErrHigh?: number;
  /** The whiskers' opacity, 0-1: a faded mark's whiskers fade with it. Absent: 1. */
  readonly alpha?: number;
}

/** Half-width of an error-bar cap, in pixels. */
const ERROR_BAR_CAP_HALF_WIDTH = 5;
const ERROR_BAR_LINE_WIDTH = 1.5;

function isErrorBarPoint(value: unknown): value is ErrorBarPoint {
  if (typeof value !== 'object' || value === null || !('x' in value) || !('y' in value)) {
    return false;
  }
  const point = value as { x: unknown; y: unknown };
  return typeof point.x === 'number' && typeof point.y === 'number';
}

/** What the error-bar plugin reads off the chart options. */
export interface ErrorBarPluginOptions {
  /** The whisker stroke. Defaults to the dark theme's muted ink. */
  readonly color?: string;
}

/**
 * Draws the uncertainty every mark in this view is required to carry. Chart.js 4 has no error bars
 * and adding a dependency for a few strokes is not worth the bundle, so this lives here, where the
 * module's own spec covers it.
 */
export const errorBarPlugin: Plugin = {
  id: 'overseerErrorBars',
  afterDatasetsDraw(chart, _args, pluginOptions): void {
    const ctx = chart.ctx;
    const area = chart.chartArea;
    if (!ctx || !area) {
      return;
    }
    const options = pluginOptions as unknown as ErrorBarPluginOptions | undefined;
    ctx.save();
    ctx.strokeStyle = options?.color ?? CHART_INK.muted;
    ctx.lineWidth = ERROR_BAR_LINE_WIDTH;

    chart.data.datasets.forEach((dataset, datasetIndex) => {
      const meta = chart.getDatasetMeta(datasetIndex);
      if (meta.hidden) {
        return;
      }
      const xScale = chart.scales[meta.xAxisID ?? 'x'];
      const yScale = chart.scales[meta.yAxisID ?? 'y'];
      meta.data.forEach((element, index) => {
        const raw = dataset.data[index];
        if (!isErrorBarPoint(raw)) {
          return;
        }
        ctx.globalAlpha = raw.alpha ?? 1;
        if (yScale && (raw.yErrLow !== undefined || raw.yErrHigh !== undefined)) {
          const low = pixelForBound(yScale, raw.y, -(raw.yErrLow ?? 0));
          const high = pixelForBound(yScale, raw.y, raw.yErrHigh ?? 0);
          strokeWhisker(ctx, element.x, low, element.x, high, 'vertical');
        }
        if (xScale && (raw.xErrLow !== undefined || raw.xErrHigh !== undefined)) {
          const low = pixelForBound(xScale, raw.x, -(raw.xErrLow ?? 0));
          const high = pixelForBound(xScale, raw.x, raw.xErrHigh ?? 0);
          strokeWhisker(ctx, low, element.y, high, element.y, 'horizontal');
        }
      });
    });
    ctx.restore();
  },
};

/** Scale-side of the whisker: a logarithmic axis cannot represent a bound that reaches zero. */
function pixelForBound(scale: { type: string; min: number; getPixelForValue(v: number): number }, value: number, delta: number): number {
  let bound = value + delta;
  if (scale.type === 'logarithmic' && bound <= 0) {
    bound = scale.min;
  }
  return scale.getPixelForValue(bound);
}

function strokeWhisker(
  ctx: CanvasRenderingContext2D,
  x0: number,
  y0: number,
  x1: number,
  y1: number,
  orientation: 'vertical' | 'horizontal',
): void {
  if (!Number.isFinite(x0) || !Number.isFinite(y0) || !Number.isFinite(x1) || !Number.isFinite(y1)) {
    return;
  }
  ctx.beginPath();
  ctx.moveTo(x0, y0);
  ctx.lineTo(x1, y1);
  if (orientation === 'vertical') {
    ctx.moveTo(x0 - ERROR_BAR_CAP_HALF_WIDTH, y0);
    ctx.lineTo(x0 + ERROR_BAR_CAP_HALF_WIDTH, y0);
    ctx.moveTo(x1 - ERROR_BAR_CAP_HALF_WIDTH, y1);
    ctx.lineTo(x1 + ERROR_BAR_CAP_HALF_WIDTH, y1);
  } else {
    ctx.moveTo(x0, y0 - ERROR_BAR_CAP_HALF_WIDTH);
    ctx.lineTo(x0, y0 + ERROR_BAR_CAP_HALF_WIDTH);
    ctx.moveTo(x1, y1 - ERROR_BAR_CAP_HALF_WIDTH);
    ctx.lineTo(x1, y1 + ERROR_BAR_CAP_HALF_WIDTH);
  }
  ctx.stroke();
}

// ---------------------------------------------------------------------------------------------
// The Pareto frontier
// ---------------------------------------------------------------------------------------------

export type BetterDirection = 'lower' | 'higher';

/** The frontier dataset's series name. It identifies the annotation to the legend and the placer. */
export const PARETO_FRONTIER_LABEL = 'Pareto frontier';

export interface ParetoCandidate {
  readonly key: string;
  readonly x: number;
  readonly y: number;
}

/**
 * A plotted coordinate pair. Chart.js `Point` admits a null coordinate so that a series can carry
 * a gap; every mark in this view is a measured model, so these are always numbers.
 */
export interface PlotPoint {
  readonly x: number;
  readonly y: number;
}

export interface ParetoResult {
  /** The non-dominated set, ordered from the least favourable x to the most favourable. */
  readonly frontier: readonly ParetoCandidate[];
  /**
   * The line through the members' own points, in the same order, ready to draw as straight
   * segments. No staircase and no extension to the plot edges: the line claims only what some
   * model achieved. Fewer than two points draw no line.
   */
  readonly path: readonly PlotPoint[];
}

/**
 * The non-dominated set and the line through it.
 *
 * This replaces the trend line a scatter of eight heterogeneous models invites. A regression over
 * eight points of different families is a statistical claim the data cannot support; the frontier is
 * a decision-analytic device that only says which models nothing else beats on both axes.
 */
export function computeParetoFrontier(
  candidates: readonly ParetoCandidate[],
  xBetter: BetterDirection,
  yBetter: BetterDirection,
): ParetoResult {
  // Work in maximisation space so one dominance test covers all four corner orientations.
  const u = (c: ParetoCandidate): number => (xBetter === 'higher' ? c.x : -c.x);
  const v = (c: ParetoCandidate): number => (yBetter === 'higher' ? c.y : -c.y);

  const frontier = candidates.filter((c) =>
    !candidates.some((other) => {
      if (other === c) {
        return false;
      }
      const atLeastAsGood = u(other) >= u(c) && v(other) >= v(c);
      const strictlyBetter = u(other) > u(c) || v(other) > v(c);
      return atLeastAsGood && strictlyBetter;
    }),
  );

  // Along the frontier v falls as u rises, so ordering by u gives the line its reading order.
  const ordered = [...frontier].sort((a, b) => (u(a) - u(b)) || a.key.localeCompare(b.key));

  const path: PlotPoint[] = [];
  for (const member of ordered) {
    const last = path[path.length - 1];
    if (!last || last.x !== member.x || last.y !== member.y) {
      path.push({ x: member.x, y: member.y });
    }
  }

  return { frontier: ordered, path };
}

/** The frontier's dash pattern: zero-length dashes with round caps, so the line is a row of dots. */
export const FRONTIER_DASH: readonly number[] = [0, 6];

/** What the plot-frame plugin reads off the chart options. */
export interface PlotFramePluginOptions {
  /** Defaults to the dark theme's axis baseline. */
  readonly color?: string;
}

/**
 * A 1 px hairline around the plot area, drawn last so it sits over the gridlines and the marks'
 * clipped edges. A figure family carries it only while its style's `plotFrame` is on.
 */
export const plotFramePlugin: Plugin = {
  id: 'overseerPlotFrame',
  afterDraw(chart, _args, pluginOptions): void {
    const ctx = chart.ctx;
    const area = chart.chartArea;
    if (!ctx || !area) {
      return;
    }
    ctx.save();
    ctx.strokeStyle = (pluginOptions as unknown as PlotFramePluginOptions | undefined)?.color ?? CHART_INK.baseline;
    ctx.lineWidth = 1;
    // Half a pixel in, so the 1 px stroke covers whole pixels inside the area rather than straddling its edge.
    ctx.strokeRect(area.left + 0.5, area.top + 0.5, area.right - area.left - 1, area.bottom - area.top - 1);
    ctx.restore();
  },
};

/** A figure spec's plugins with the plot frame appended while the family draws one. */
function withPlotFrame(plugins: Plugin[], frame: boolean): Plugin[] {
  return frame ? [...plugins, plotFramePlugin] : plugins;
}

/** The plot frame's options entry while the family draws one; nothing otherwise. */
function plotFrameOptions(theme: ResolvedFigureTheme, frame: boolean) {
  return frame ? { [plotFramePlugin.id]: { color: theme.frameColor } satisfies PlotFramePluginOptions } : {};
}

/** The error-bar plugin's options entry where the theme's whisker ink is not the dark default. */
function errorBarOptions(theme: ResolvedFigureTheme) {
  const options: ErrorBarPluginOptions = unlessDefault('color', theme.chart.inkMuted, CHART_INK.muted);
  return options.color === undefined ? {} : { [errorBarPlugin.id]: options };
}

// ---------------------------------------------------------------------------------------------
// Figure plumbing
// ---------------------------------------------------------------------------------------------

/**
 * Which corner of a scatter is the good one. Drawn as the Better badge, under the logo or at the
 * end of the badge row, never as a reversed axis.
 */
export type PreferredCorner = FigureDirection;

/**
 * One figure: its chart.js configuration plus the chrome the component renders as HTML.
 *
 * The chrome is structured data rather than chart.js title plugins so it renders as real text in the
 * page - selectable, translatable, and readable by a screen reader that never sees the canvas - and
 * so the export composer draws the same content.
 */
export interface ChartSpec<
  TType extends ChartType = ChartType,
  TData = DefaultDataPoint<TType>,
  TLabel = unknown,
> {
  readonly id: string;
  readonly title: string;
  /** Badges, detail, key, highlight and notes; `chrome.title` equals `title`. */
  readonly chrome: FigureChrome;
  readonly preferredCorner?: PreferredCorner;
  readonly config: ChartConfiguration<TType, TData, TLabel>;
  readonly plugins: Plugin[];
  /**
   * Sentences the figure's accessible name adds to its chrome summary, such as `Pareto frontier:
   * A, B` and `Faded: C`. Absent: none.
   */
  readonly summary?: readonly string[];
}

/** Options every figure builder takes. */
export interface FigureOptions {
  readonly context: ModelComparisonContext;
  readonly glyphs: ReadonlyMap<string, IdentityGlyph>;
  /** Suppresses animation. Read from {@link ReducedMotionWatcher}, never sampled at construction. */
  readonly reducedMotion: boolean;
  /** The model under the pointer. Highlighting it lights the same model in every panel and in P2. */
  readonly highlightedKey?: string | null;
  /** Explicitly selected models. Empty means "nothing selected", which is not the same as "none". */
  readonly selectedKeys?: readonly string[];
  /** Which measure the scatters' speed axis reads. Defaults to mean model time. */
  readonly speedMeasure?: SpeedMeasure;
  /** Names every scatter mark on the canvas and hides the legend. Off unless the wizard asks. */
  readonly directLabels?: boolean;
  /** Draws each scatter mark's two measured values on the canvas, beside it. Off unless the caller asks. */
  readonly inlineValues?: boolean;
  /** Bar and trade-off styling. Defaults to {@link DEFAULT_FIGURE_STYLE}. */
  readonly style?: FigureStyle;
  /** Colours and fonts. Absent: the dark theme, whatever `style.appearance` says. */
  readonly theme?: ResolvedFigureTheme;
}

/** Effective hit radius is `radius + hitRadius`, so the target is 36 px across - well over the 24 px floor. */
const POINT_RADIUS = 6;
const POINT_HOVER_RADIUS = 9;
const POINT_HIT_RADIUS = 12;

/** A scatter mark's hover and hit radii for its drawn radius; the hit target never shrinks below 36 px. */
function scatterPointRadii(radius: number): { radius: number; hoverRadius: number; hitRadius: number } {
  return { radius, hoverRadius: radius + 3, hitRadius: Math.max(POINT_HIT_RADIUS, 18 - radius) };
}

function baseOptions(reducedMotion: boolean) {
  return {
    responsive: true,
    maintainAspectRatio: false,
    // `prefers-reduced-motion: reduce` turns animation off outright rather than shortening it.
    animation: reducedMotion ? (false as const) : { duration: 300 },
    // Nearest-without-intersect means the pointer only has to be closest to a mark, not on it.
    interaction: { mode: 'nearest' as const, intersect: false, axis: 'xy' as const },
  };
}

function gridOptions(display = true, theme: ResolvedFigureTheme = DEFAULT_THEME) {
  return { display, color: theme.chart.gridline, lineWidth: 1, drawTicks: false };
}

/** Tick labels, always at weight 400. */
function tickOptions(size = 11, theme: ResolvedFigureTheme = DEFAULT_THEME) {
  return { color: theme.chart.inkMuted, font: { family: theme.fonts.chartStack ?? LATO_STACK, size } };
}

/** The family and weight an axis title draws in beyond its size; empty keeps the Chart.js default. */
interface AxisTitleFont {
  readonly theme?: ResolvedFigureTheme;
  readonly weight?: FigureFontWeight;
}

/** Draws the axis label on its own line and, when given a direction, a "lower/higher is better" second line. */
function axisTitle(text: string | string[], better?: BetterDirection, size = 12, font: AxisTitleFont = {}) {
  const theme = font.theme ?? DEFAULT_THEME;
  return {
    display: true,
    text: better
      ? [...(Array.isArray(text) ? text : [text]), better === 'lower' ? 'lower is better' : 'higher is better']
      : text,
    color: theme.chart.inkSecondary,
    font: { size, ...familyKey(theme), ...weightKey(font.weight ?? 400) },
  };
}

/** The font keys of a label a figure's label weight applies to: value labels, direct-label names, legends. */
function labelFont(theme: ResolvedFigureTheme): { family?: string; weight?: number } {
  return { ...familyKey(theme), ...weightKey(theme.fonts.labelWeight) };
}

/** A legend's `labels.font` where the theme sets a family or weight; nothing otherwise. */
function legendFont(theme: ResolvedFigureTheme): { font?: { family?: string; weight?: number } } {
  const font = labelFont(theme);
  return Object.keys(font).length === 0 ? {} : { font };
}

/** The badges every figure opens with: how many models, how many runs behind each, how many questions. */
function countBadges(plotted: readonly ModelComparisonEntry[], context: ModelComparisonContext): FigureBadge[] {
  return [
    { text: `${plotted.length} ${plotted.length === 1 ? 'model' : 'models'}`, tone: 'neutral', kind: 'models' },
    runsBadge(plotted),
    questionsBadge(context.scoredItemsMin, context.scoredItemsMax, context.examItemCount),
  ];
}

function warningNotes(texts: readonly string[]): FigureNote[] {
  return texts.map((text): FigureNote => ({ text, tone: 'warning' }));
}

/**
 * A title's final parenthetical split onto a line of its own: `Candidate cost of one suite run
 * (USD, 18 questions asked)` becomes the head and `(USD, 18 questions asked)`. Null where the title
 * does not end in a parenthetical preceded by a space and a nonempty head.
 */
export function splitAxisTitle(text: string): [string, string] | null {
  if (!text.endsWith(')')) {
    return null;
  }
  let depth = 0;
  let open = -1;
  for (let i = text.length - 1; i >= 0; i -= 1) {
    if (text[i] === ')') {
      depth += 1;
    } else if (text[i] === '(') {
      depth -= 1;
      if (depth === 0) {
        open = i;
        break;
      }
    }
  }
  if (open < 1 || text[open - 1] !== ' ') {
    return null;
  }
  const head = text.slice(0, open - 1);
  return head.trim() === '' ? null : [head, text.slice(open)];
}

/** An axis title's lines unbroken and, where it has a final parenthetical, broken; the direction line last in both. */
export function axisTitleLines(
  text: string,
  better?: BetterDirection,
): { readonly unbroken: readonly string[]; readonly broken: readonly string[] | null } {
  const direction = better ? [better === 'lower' ? 'lower is better' : 'higher is better'] : [];
  const split = splitAxisTitle(text);
  return { unbroken: [text, ...direction], broken: split ? [...split, ...direction] : null };
}

/** Room, in px, an unbroken title must leave along its axis before Automatic keeps it on one line. */
export const AXIS_TITLE_RESERVE_PX = 8;

function sameLines(current: unknown, lines: readonly string[]): boolean {
  return Array.isArray(current) && current.length === lines.length && current.every((line, i) => line === lines[i]);
}

/**
 * The value scale's `afterFit`: breaks the title when its widest unbroken line is longer than the
 * fitted axis less {@link AXIS_TITLE_RESERVE_PX}, and refits once to reserve the new thickness.
 *
 * Both alternatives come from the closure on every fit, so a longer axis restores the unbroken title.
 * The write lands in the chart's own merged options, never in the spec the builder returned.
 */
function breakTitleToFit(unbroken: readonly string[], broken: readonly string[]): (scale: Scale) => void {
  return (scale) => {
    const title = (scale.options as unknown as { title: { text: string | string[]; font?: Partial<FontSpec> } }).title;
    const font = toFont(title.font ?? {}, scale.chart.options.font);
    const ctx = scale.ctx;
    ctx.save();
    ctx.font = font.string;
    const widest = Math.max(...unbroken.map((line) => ctx.measureText(line).width));
    ctx.restore();
    const available = scale.isHorizontal() ? scale.width : scale.height;
    const chosen = widest > Math.max(0, available - AXIS_TITLE_RESERVE_PX) ? broken : unbroken;
    if (!sameLines(title.text, chosen)) {
      title.text = [...chosen];
      // A second fit recomputes the tick-label padding from pixel state the first one has already
      // moved, and the layout sizes the axis from that padding: keep the first fit's.
      const { paddingLeft, paddingRight, paddingTop, paddingBottom } = scale;
      scale.fit();
      Object.assign(scale, { paddingLeft, paddingRight, paddingTop, paddingBottom });
    }
  };
}

/** A bar value axis's title options, and the `afterFit` that decides its break while it is Automatic. */
function valueAxisTitle(
  text: string,
  better: BetterDirection | undefined,
  mode: AxisTitleBreak,
  size: number,
  font: AxisTitleFont = {},
) {
  const { unbroken, broken } = axisTitleLines(text, better);
  const lines = mode === 'always' && broken ? broken : unbroken;
  return {
    title: axisTitle([...lines], undefined, size, font),
    ...(mode === 'auto' && broken ? { afterFit: breakTitleToFit(unbroken, broken) } : {}),
  };
}

function degradedNotices(plotted: readonly ModelComparisonEntry[], axes: readonly ('speed' | 'cost')[]): string[] {
  const notices: string[] = [];
  if (axes.includes('speed')) {
    const degraded = plotted.filter((e) => e.speedDegraded);
    if (degraded.length > 0) {
      notices.push(
        `Speed is not comparable for ${degraded.map((e) => e.label).join(', ')}: question parallelism ` +
          'differs from the rest of the set.',
      );
    }
  }
  if (axes.includes('cost')) {
    const degraded = plotted.filter((e) => e.costDegraded);
    if (degraded.length > 0) {
      notices.push(
        `Cost is not comparable for ${degraded.map((e) => e.label).join(', ')}: the pricing snapshot ` +
          'differs from the rest of the set.',
      );
    }
  }
  return notices;
}

// ---------------------------------------------------------------------------------------------
// Direct labels on a scatter
// ---------------------------------------------------------------------------------------------

/** The default label size, matching the axis ticks so a direct label reads as chart chrome. */
const DIRECT_LABEL_DEFAULT_SIZE = 11;

/** The family and name weight a plate draws in. Absent: Lato, and the name at 400. */
export interface DirectLabelFont {
  readonly family?: string;
  readonly nameWeight?: FigureFontWeight;
}

/**
 * Fonts and line boxes of a plate at name size `size`: the value lines a step under the name so the
 * name stays the plate's headline, and both line boxes `size + 1`, since measured text carries no height.
 * The name takes the label weight; the value lines stay at 400.
 */
function directLabelMetrics(size: number, font: DirectLabelFont = {}): {
  nameFont: string;
  valueFont: string;
  nameLineHeight: number;
  valueLineHeight: number;
} {
  const family = font.family ?? LATO_STACK;
  const nameWeight = font.nameWeight ?? 400;
  return {
    nameFont: `${nameWeight === 400 ? '' : `${nameWeight} `}${size}px ${family}`,
    valueFont: `${size - 1}px ${family}`,
    nameLineHeight: size + 1,
    valueLineHeight: size + 1,
  };
}

/** Rings tried in turn, in pixels out from the mark. Past the last one the fallback applies. */
const DIRECT_LABEL_RINGS = [14, 28, 42] as const;

/** Padding around the label text, so the backing plate does not touch the glyphs. */
const DIRECT_LABEL_PAD_X = 3;
const DIRECT_LABEL_PAD_Y = 2;

/** How much of the surface the backing plate keeps, so a gridline behind the text stays subdued. */
const DIRECT_LABEL_PLATE_ALPHA = 0.85;

/** The surface-colored halo around a name-only label's text, in pixels on each side of a glyph. */
const DIRECT_LABEL_HALO_PX = 3;

/** Between the measure column and the value column. */
const DIRECT_LABEL_COLUMN_GAP = 8;

/** The hue rule along the plate's left edge, and the text inset it pushes. */
const DIRECT_LABEL_RULE_WIDTH = 2;
const DIRECT_LABEL_RULE_GAP = 4;

/** One mark to label: its pixel position, and the plate size its text needs. */
export interface DirectLabelAnchor {
  readonly key: string;
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

/** A placed label: where the plate goes, and the mark its leader line runs back to. */
export interface DirectLabelBox {
  readonly key: string;
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
  readonly anchorX: number;
  readonly anchorY: number;
}

/** The plot area, and the shape every overlap test works in. */
export interface LabelRect {
  readonly left: number;
  readonly top: number;
  readonly right: number;
  readonly bottom: number;
}

/** A pixel coordinate, structural so a chart element and a plain vertex both fit it. */
export interface PixelPoint {
  readonly x: number;
  readonly y: number;
}

/** Other ink already on the canvas, which a label should avoid without being forbidden it. */
export interface DirectLabelObstacles {
  /** Axis-aligned rects a label should not sit on: whisker extents, one per mark. */
  readonly rects?: readonly LabelRect[];
  /** Polylines a label should not cross: the Pareto frontier, as pixel vertices. */
  readonly polylines?: readonly { x: number; y: number }[][];
}

function boxRect(box: { x: number; y: number; width: number; height: number }): LabelRect {
  return { left: box.x, top: box.y, right: box.x + box.width, bottom: box.y + box.height };
}

function rectsOverlap(a: LabelRect, b: LabelRect): boolean {
  return a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;
}

function rectInside(rect: LabelRect, area: LabelRect): boolean {
  return rect.left >= area.left && rect.top >= area.top && rect.right <= area.right && rect.bottom <= area.bottom;
}

/** True when the box reaches into a mark's disc, its own mark included. */
function coversMark(rect: LabelRect, mark: { x: number; y: number }, radius: number): boolean {
  const nearestX = Math.min(Math.max(mark.x, rect.left), rect.right);
  const nearestY = Math.min(Math.max(mark.y, rect.top), rect.bottom);
  const dx = mark.x - nearestX;
  const dy = mark.y - nearestY;
  return dx * dx + dy * dy < radius * radius;
}

/** Sign of the cross product of `ab` and `bc`: 1 turning left, -1 turning right, 0 collinear. */
function orientation(a: PixelPoint, b: PixelPoint, c: PixelPoint): number {
  const value = (b.y - a.y) * (c.x - b.x) - (b.x - a.x) * (c.y - b.y);
  if (value > 0) {
    return 1;
  }
  return value < 0 ? -1 : 0;
}

/** True when `c`, already known to be collinear with `ab`, lies within that segment's extent. */
function withinSegment(a: PixelPoint, b: PixelPoint, c: PixelPoint): boolean {
  return (
    c.x >= Math.min(a.x, b.x) &&
    c.x <= Math.max(a.x, b.x) &&
    c.y >= Math.min(a.y, b.y) &&
    c.y <= Math.max(a.y, b.y)
  );
}

/** True when segments `p1p2` and `p3p4` share at least one point, a collinear touch included. */
export function segmentsIntersect(p1: PixelPoint, p2: PixelPoint, p3: PixelPoint, p4: PixelPoint): boolean {
  const o1 = orientation(p1, p2, p3);
  const o2 = orientation(p1, p2, p4);
  const o3 = orientation(p3, p4, p1);
  const o4 = orientation(p3, p4, p2);
  if (o1 !== o2 && o3 !== o4) {
    return true;
  }
  return (
    (o1 === 0 && withinSegment(p1, p2, p3)) ||
    (o2 === 0 && withinSegment(p1, p2, p4)) ||
    (o3 === 0 && withinSegment(p3, p4, p1)) ||
    (o4 === 0 && withinSegment(p3, p4, p2))
  );
}

function pointInRect(point: PixelPoint, rect: LabelRect): boolean {
  return point.x >= rect.left && point.x <= rect.right && point.y >= rect.top && point.y <= rect.bottom;
}

/** True when the segment meets the filled rectangle: an endpoint inside it, or any edge crossed. */
export function segmentIntersectsRect(p1: PixelPoint, p2: PixelPoint, rect: LabelRect): boolean {
  if (pointInRect(p1, rect) || pointInRect(p2, rect)) {
    return true;
  }
  const corners: PixelPoint[] = [
    { x: rect.left, y: rect.top },
    { x: rect.right, y: rect.top },
    { x: rect.right, y: rect.bottom },
    { x: rect.left, y: rect.bottom },
  ];
  return corners.some((corner, index) => segmentsIntersect(p1, p2, corner, corners[(index + 1) % corners.length]));
}

/** True when the segment passes within `radius` of `centre` - the test a mark's disc needs. */
function segmentMeetsDisc(p1: PixelPoint, p2: PixelPoint, centre: PixelPoint, radius: number): boolean {
  const dx = p2.x - p1.x;
  const dy = p2.y - p1.y;
  const lengthSq = dx * dx + dy * dy;
  const t = lengthSq === 0 ? 0 : Math.min(1, Math.max(0, ((centre.x - p1.x) * dx + (centre.y - p1.y) * dy) / lengthSq));
  return Math.hypot(centre.x - (p1.x + t * dx), centre.y - (p1.y + t * dy)) < radius;
}

/** The eight directions a label is tried in, in preference order, as unit offsets on each ring. */
const DIRECT_LABEL_DIRECTIONS: readonly { dx: number; dy: number }[] = [
  { dx: 1, dy: 0 },
  { dx: -1, dy: 0 },
  { dx: 0, dy: -1 },
  { dx: 0, dy: 1 },
  { dx: 0.7071, dy: -0.7071 },
  { dx: -0.7071, dy: -0.7071 },
  { dx: 0.7071, dy: 0.7071 },
  { dx: -0.7071, dy: 0.7071 },
];

/** The ring offset moves the box edge nearest the mark, so a wide label is not pulled in by half. */
function candidateBox(
  anchor: DirectLabelAnchor,
  direction: { dx: number; dy: number },
  ring: number,
): { x: number; y: number; width: number; height: number } {
  const centreX = anchor.x + direction.dx * (ring + anchor.width / 2);
  const centreY = anchor.y + direction.dy * (ring + anchor.height / 2);
  return {
    x: centreX - anchor.width / 2,
    y: centreY - anchor.height / 2,
    width: anchor.width,
    height: anchor.height,
  };
}

function clampIntoArea(
  box: { x: number; y: number; width: number; height: number },
  area: LabelRect,
): { x: number; y: number; width: number; height: number } {
  return {
    ...box,
    x: Math.min(Math.max(box.x, area.left), Math.max(area.left, area.right - box.width)),
    y: Math.min(Math.max(box.y, area.top), Math.max(area.top, area.bottom - box.height)),
  };
}

/**
 * Soft penalties, in the same pixel units as the ring radius that forms a candidate's base score.
 *
 * Sitting on another piece of ink costs more than two extra rings of leader, so a label crosses a
 * whisker or the frontier only when every ring is blocked; a leader crossing costs less than one
 * ring, and the direction preferences less again.
 */
const PENALTY_OBSTACLE_RECT = 40;
const PENALTY_OBSTACLE_SEGMENT = 40;
const PENALTY_LEADER_CROSSING = 25;
const PENALTY_TOWARD_EDGE = 10;
const PENALTY_DIAGONAL = 5;

/** What the candidate costs for the ink it lands on: whisker rects and frontier segments. */
function obstaclePenalty(rect: LabelRect, obstacles: DirectLabelObstacles): number {
  let penalty = obstacles.rects?.some((obstacle) => rectsOverlap(rect, obstacle)) ? PENALTY_OBSTACLE_RECT : 0;
  for (const polyline of obstacles.polylines ?? []) {
    for (let i = 1; i < polyline.length; i += 1) {
      if (segmentIntersectsRect(polyline[i - 1], polyline[i], rect)) {
        penalty += PENALTY_OBSTACLE_SEGMENT;
      }
    }
  }
  return penalty;
}

/** The leader as the plugin draws it: the mark centre to the point of the plate nearest to it. */
function leaderTarget(anchor: DirectLabelAnchor, box: { x: number; y: number; width: number; height: number }): PixelPoint {
  return {
    x: Math.min(Math.max(anchor.x, box.x), box.x + box.width),
    y: Math.min(Math.max(anchor.y, box.y), box.y + box.height),
  };
}

/** True when the leader would run through a label already placed or through another mark's disc. */
function leaderCrosses(
  anchor: DirectLabelAnchor,
  box: { x: number; y: number; width: number; height: number },
  placed: readonly DirectLabelBox[],
  marks: readonly DirectLabelAnchor[],
  markRadius: number,
): boolean {
  const target = leaderTarget(anchor, box);
  if (placed.some((other) => segmentIntersectsRect(anchor, target, boxRect(other)))) {
    return true;
  }
  return marks.some((mark) => mark.key !== anchor.key && segmentMeetsDisc(anchor, target, mark, markRadius));
}

/**
 * True when the direction heads for the plot edge the mark is already closest to, and that edge is
 * within `2 * ring`. It fans labels inward along the margins while leaving the middle unbiased.
 */
function pointsTowardNearEdge(
  anchor: DirectLabelAnchor,
  direction: { dx: number; dy: number },
  area: LabelRect,
  ring: number,
): boolean {
  const gaps = [
    { distance: anchor.x - area.left, toward: direction.dx < 0 },
    { distance: area.right - anchor.x, toward: direction.dx > 0 },
    { distance: anchor.y - area.top, toward: direction.dy < 0 },
    { distance: area.bottom - anchor.y, toward: direction.dy > 0 },
  ];
  let nearest = gaps[0];
  for (const gap of gaps) {
    if (gap.distance < nearest.distance) {
      nearest = gap;
    }
  }
  return nearest.toward && nearest.distance < 2 * ring;
}

/**
 * Places a label beside every mark so that no two labels overlap and no label covers a mark.
 *
 * Pure geometry, with no canvas and no chart: the caller measures the text and passes the sizes in,
 * which is what makes the placement testable and its determinism checkable. The order is fixed —
 * ascending x, then y, then key — so a rebuild on hover re-places the labels identically rather
 * than reshuffling them under the pointer.
 *
 * Every (ring, direction) candidate that clears the three hard constraints is scored, and the
 * cheapest wins; ties keep the earlier candidate, so the ring and direction orders still decide and
 * the output stays deterministic. The score starts at the ring radius and adds the penalties above,
 * which is what lets the placer route a label around a whisker, the frontier, or a plot edge instead
 * of taking the first opening it finds.
 *
 * A dense corner can exhaust every ring. The fallback then takes the first ring's right-hand
 * candidate clamped into the plot area: a visible label that may touch its neighbour beats a hidden
 * one, and the leader line still says which mark it belongs to.
 */
export function placeDirectLabels(
  anchors: readonly DirectLabelAnchor[],
  area: LabelRect,
  markRadius: number = POINT_HOVER_RADIUS,
  obstacles: DirectLabelObstacles = {},
): DirectLabelBox[] {
  const ordered = [...anchors].sort((a, b) => a.x - b.x || a.y - b.y || a.key.localeCompare(b.key));
  const placed: DirectLabelBox[] = [];

  for (const anchor of ordered) {
    let chosen: { x: number; y: number; width: number; height: number } | null = null;
    let bestScore = Number.POSITIVE_INFINITY;

    for (const ring of DIRECT_LABEL_RINGS) {
      for (const direction of DIRECT_LABEL_DIRECTIONS) {
        const box = candidateBox(anchor, direction, ring);
        const rect = boxRect(box);
        if (!rectInside(rect, area)) {
          continue;
        }
        if (placed.some((other) => rectsOverlap(rect, boxRect(other)))) {
          continue;
        }
        if (anchors.some((other) => coversMark(rect, other, markRadius))) {
          continue;
        }

        let score = ring + obstaclePenalty(rect, obstacles);
        if (leaderCrosses(anchor, box, placed, anchors, markRadius)) {
          score += PENALTY_LEADER_CROSSING;
        }
        if (pointsTowardNearEdge(anchor, direction, area, ring)) {
          score += PENALTY_TOWARD_EDGE;
        }
        if (direction.dx !== 0 && direction.dy !== 0) {
          score += PENALTY_DIAGONAL;
        }
        if (score < bestScore) {
          bestScore = score;
          chosen = box;
        }
      }
    }

    const fallback = candidateBox(anchor, DIRECT_LABEL_DIRECTIONS[0], DIRECT_LABEL_RINGS[0]);
    const box = chosen ?? clampIntoArea(fallback, area);
    placed.push({ ...box, key: anchor.key, anchorX: anchor.x, anchorY: anchor.y });
  }

  return placed;
}

/** One value line on a plate: the measure's short name and its formatted value. */
export interface DirectLabelValue {
  readonly label: string;
  readonly text: string;
}

/**
 * What one mark's label carries. A block with neither a name nor values draws nothing. A name alone
 * draws as text with a surface halo; a block with values draws on a backing plate.
 */
export interface DirectLabelBlock {
  /** The model's name, one line or several; absent when the legend names the marks. */
  readonly name?: string | readonly string[];
  readonly values: readonly DirectLabelValue[];
  /** The mark's provider hue, drawn as the label's left rule. */
  readonly hue: string;
  /** A faded mark's label: its name in muted ink. */
  readonly muted?: boolean;
}

function directLabelNameLines(name: string | readonly string[] | undefined): readonly string[] {
  if (name === undefined) {
    return [];
  }
  return (typeof name === 'string' ? [name] : name).filter((line) => line.length > 0);
}

/** What the plugin reads off the chart options: one block per model dataset, in dataset order. */
export interface DirectLabelPluginOptions {
  /** Indexed by dataset. Datasets past the list — the frontier — are never labelled. */
  readonly blocks: readonly DirectLabelBlock[];
  /** The emphasised model, whose plate wears the accent its mark already does. */
  readonly highlightedIndex?: number;
  /** The name's font size; value lines are one smaller. Defaults to 11. */
  readonly fontSizePx?: number;
  /** The drawn mark radius, which plates and leaders keep clear of. Defaults to 6. */
  readonly markRadiusPx?: number;
  /** Whether plates avoid the drawn whiskers. False when the figure hides them. Defaults to true. */
  readonly avoidWhiskers?: boolean;
  /** The plates' font family. Defaults to Lato. */
  readonly fontFamily?: string;
  /** The name's weight; value lines stay at 400. Defaults to 400. */
  readonly nameFontWeight?: FigureFontWeight;
  /** The backing plate. Defaults to the dark theme's chart surface. */
  readonly surfaceColor?: string;
  /** The highlighted plate's rule and name. Defaults to the dark theme's accent. */
  readonly accentColor?: string;
  /** Names and values. Defaults to the dark theme's secondary ink. */
  readonly inkColor?: string;
  /** Leaders and measure names. Defaults to the dark theme's muted ink. */
  readonly mutedColor?: string;
}

/**
 * The plate one block needs: its outer size, and the two column widths its value lines align on.
 *
 * The columns are measured over the whole block rather than per line, which is what makes eight
 * plates read as one table instead of eight captions. The context's font is set here, so a caller
 * measuring a block need not know which font each line is drawn in.
 */
export function measureDirectLabelBlock(
  ctx: CanvasRenderingContext2D,
  block: DirectLabelBlock,
  size: number = DIRECT_LABEL_DEFAULT_SIZE,
  font: DirectLabelFont = {},
): { width: number; height: number; labelColumn: number; valueColumn: number } {
  const metrics = directLabelMetrics(size, font);
  ctx.font = metrics.nameFont;
  const nameLines = directLabelNameLines(block.name);
  let nameWidth = 0;
  for (const line of nameLines) {
    nameWidth = Math.max(nameWidth, ctx.measureText(line).width);
  }

  ctx.font = metrics.valueFont;
  let labelColumn = 0;
  let valueColumn = 0;
  for (const value of block.values) {
    labelColumn = Math.max(labelColumn, ctx.measureText(value.label).width);
    valueColumn = Math.max(valueColumn, ctx.measureText(value.text).width);
  }
  const valuesWidth = block.values.length === 0 ? 0 : labelColumn + DIRECT_LABEL_COLUMN_GAP + valueColumn;

  return {
    width:
      DIRECT_LABEL_RULE_WIDTH +
      DIRECT_LABEL_RULE_GAP +
      DIRECT_LABEL_PAD_X * 2 +
      Math.max(nameWidth, valuesWidth),
    height:
      DIRECT_LABEL_PAD_Y * 2 +
      nameLines.length * metrics.nameLineHeight +
      block.values.length * metrics.valueLineHeight,
    labelColumn,
    valueColumn,
  };
}

/** Draws a leader from the edge of the mark to the nearest edge of its label plate. */
function strokeLeader(ctx: CanvasRenderingContext2D, box: DirectLabelBox, markRadius: number): void {
  const targetX = Math.min(Math.max(box.anchorX, box.x), box.x + box.width);
  const targetY = Math.min(Math.max(box.anchorY, box.y), box.y + box.height);
  const dx = targetX - box.anchorX;
  const dy = targetY - box.anchorY;
  const distance = Math.hypot(dx, dy);
  // A plate that already touches the mark needs no leader, and normalising zero would be NaN.
  if (distance <= markRadius) {
    return;
  }
  ctx.beginPath();
  ctx.moveTo(box.anchorX + (dx / distance) * markRadius, box.anchorY + (dy / distance) * markRadius);
  ctx.lineTo(targetX, targetY);
  ctx.stroke();
}

/**
 * The whiskers `errorBarPlugin` draws for one mark, as rects: the vertical extent widened to the
 * cap, the horizontal extent heightened to it. A scale a datum has no interval on yields nothing.
 */
function whiskerRects(
  chart: Chart,
  meta: { xAxisID?: string; yAxisID?: string },
  raw: ErrorBarPoint,
  mark: PixelPoint,
): LabelRect[] {
  const rects: LabelRect[] = [];
  const xScale = chart.scales[meta.xAxisID ?? 'x'];
  const yScale = chart.scales[meta.yAxisID ?? 'y'];
  if (yScale && (raw.yErrLow !== undefined || raw.yErrHigh !== undefined)) {
    const low = pixelForBound(yScale, raw.y, -(raw.yErrLow ?? 0));
    const high = pixelForBound(yScale, raw.y, raw.yErrHigh ?? 0);
    if (Number.isFinite(low) && Number.isFinite(high)) {
      rects.push({
        left: mark.x - ERROR_BAR_CAP_HALF_WIDTH,
        right: mark.x + ERROR_BAR_CAP_HALF_WIDTH,
        top: Math.min(low, high),
        bottom: Math.max(low, high),
      });
    }
  }
  if (xScale && (raw.xErrLow !== undefined || raw.xErrHigh !== undefined)) {
    const low = pixelForBound(xScale, raw.x, -(raw.xErrLow ?? 0));
    const high = pixelForBound(xScale, raw.x, raw.xErrHigh ?? 0);
    if (Number.isFinite(low) && Number.isFinite(high)) {
      rects.push({
        left: Math.min(low, high),
        right: Math.max(low, high),
        top: mark.y - ERROR_BAR_CAP_HALF_WIDTH,
        bottom: mark.y + ERROR_BAR_CAP_HALF_WIDTH,
      });
    }
  }
  return rects;
}

/**
 * The frontier line as pixel vertices - the members' own points - or nothing when the figure carries
 * no frontier. It sits at the index straight after the labelled model datasets, which is where
 * `buildScatter` pushes it.
 */
function frontierPolylines(chart: Chart, frontierIndex: number): { x: number; y: number }[][] {
  if (chart.data.datasets[frontierIndex]?.label !== PARETO_FRONTIER_LABEL) {
    return [];
  }
  const meta = chart.getDatasetMeta(frontierIndex);
  if (meta.hidden) {
    return [];
  }
  const vertices = meta.data
    .filter((element) => Number.isFinite(element.x) && Number.isFinite(element.y))
    .map((element) => ({ x: element.x, y: element.y }));
  return vertices.length > 1 ? [vertices] : [];
}

/**
 * Names every mark on the canvas, on a leader line when the label is displaced, with the legend
 * switched off, and carries the mark's own values when the figure asks for them.
 *
 * It draws rather than delegating to `chartjs-plugin-datalabels`, which places a label at a fixed
 * offset with no knowledge of its neighbours — two models close together got two names on top of
 * each other. Drawing on the canvas is also what carries the labels into every export path without
 * a change to the composer.
 */
export const directLabelPlugin: Plugin = {
  id: 'overseerDirectLabels',
  afterDatasetsDraw(chart, _args, pluginOptions): void {
    const options = pluginOptions as unknown as DirectLabelPluginOptions | undefined;
    const blocks = options?.blocks ?? [];
    const ctx = chart.ctx;
    const area = chart.chartArea;
    if (!ctx || !area || blocks.length === 0) {
      return;
    }
    const size = options?.fontSizePx ?? DIRECT_LABEL_DEFAULT_SIZE;
    const font: DirectLabelFont = { family: options?.fontFamily, nameWeight: options?.nameFontWeight };
    const text = directLabelMetrics(size, font);
    const markRadius = (options?.markRadiusPx ?? POINT_RADIUS) + 3;
    const avoidWhiskers = options?.avoidWhiskers ?? true;
    const surface = options?.surfaceColor ?? CHART_SURFACE;
    const accent = options?.accentColor ?? ACCENT;
    const ink = options?.inkColor ?? CHART_INK.secondary;
    const muted = options?.mutedColor ?? CHART_INK.muted;

    ctx.save();
    ctx.textBaseline = 'middle';
    ctx.textAlign = 'left';

    const anchors: DirectLabelAnchor[] = [];
    const obstacleRects: LabelRect[] = [];
    // Keyed by dataset index, so two models sharing a label cannot collide in the placer's map.
    const plates = new Map<string, { block: DirectLabelBlock; labelColumn: number; valueColumn: number }>();
    blocks.forEach((block, datasetIndex) => {
      if (directLabelNameLines(block.name).length === 0 && block.values.length === 0) {
        return;
      }
      const meta = chart.getDatasetMeta(datasetIndex);
      if (meta.hidden) {
        return;
      }
      // One mark per model dataset, which is how the scatters are built.
      const element = meta.data[0];
      if (!element || !Number.isFinite(element.x) || !Number.isFinite(element.y)) {
        return;
      }
      const key = String(datasetIndex);
      const metrics = measureDirectLabelBlock(ctx, block, size, font);
      anchors.push({ key, x: element.x, y: element.y, width: metrics.width, height: metrics.height });
      plates.set(key, { block, labelColumn: metrics.labelColumn, valueColumn: metrics.valueColumn });
      // The whiskers `errorBarPlugin` has already drawn, so a plate does not land on one.
      const raw = chart.data.datasets[datasetIndex]?.data[0];
      if (avoidWhiskers && isErrorBarPoint(raw)) {
        obstacleRects.push(...whiskerRects(chart, meta, raw, element));
      }
    });

    const boxes = placeDirectLabels(anchors, area, markRadius, {
      rects: obstacleRects,
      polylines: frontierPolylines(chart, blocks.length),
    });
    const highlighted = options?.highlightedIndex === undefined ? undefined : String(options.highlightedIndex);

    // Leaders first, so a plate covers the end of its own line rather than the line crossing it.
    ctx.strokeStyle = muted;
    ctx.lineWidth = 1;
    for (const box of boxes) {
      strokeLeader(ctx, box, markRadius);
    }

    for (const box of boxes) {
      const plate = plates.get(box.key);
      if (!plate) {
        continue;
      }
      const accented = box.key === highlighted;
      // A name alone is text with a surface halo; a label carrying values keeps a backing plate, so
      // its two columns read as a table over any gridline.
      const haloed = plate.block.values.length === 0;
      if (!haloed) {
        ctx.globalAlpha = DIRECT_LABEL_PLATE_ALPHA;
        ctx.fillStyle = surface;
        ctx.fillRect(box.x, box.y, box.width, box.height);
        ctx.globalAlpha = 1;
      }

      // The hue rule ties the label to its mark and names its provider.
      ctx.fillStyle = accented ? accent : plate.block.hue;
      ctx.fillRect(box.x, box.y, DIRECT_LABEL_RULE_WIDTH, box.height);

      const textLeft = box.x + DIRECT_LABEL_RULE_WIDTH + DIRECT_LABEL_RULE_GAP + DIRECT_LABEL_PAD_X;
      const textRight = box.x + box.width - DIRECT_LABEL_PAD_X;
      let lineTop = box.y + DIRECT_LABEL_PAD_Y;

      const nameLines = directLabelNameLines(plate.block.name);
      if (nameLines.length > 0) {
        ctx.font = text.nameFont;
        ctx.textAlign = 'left';
        const nameInk = accented ? accent : plate.block.muted ? muted : ink;
        for (const line of nameLines) {
          const middle = lineTop + text.nameLineHeight / 2;
          if (haloed) {
            ctx.lineJoin = 'round';
            ctx.lineWidth = DIRECT_LABEL_HALO_PX * 2;
            ctx.strokeStyle = surface;
            ctx.strokeText(line, textLeft, middle);
          }
          ctx.fillStyle = nameInk;
          ctx.fillText(line, textLeft, middle);
          lineTop += text.nameLineHeight;
        }
      }

      ctx.font = text.valueFont;
      for (const value of plate.block.values) {
        const middle = lineTop + text.valueLineHeight / 2;
        ctx.textAlign = 'left';
        ctx.fillStyle = muted;
        ctx.fillText(value.label, textLeft, middle);
        ctx.textAlign = 'right';
        ctx.fillStyle = ink;
        ctx.fillText(value.text, textRight, middle);
        lineTop += text.valueLineHeight;
      }
    }

    ctx.restore();
  },
};

// ---------------------------------------------------------------------------------------------
// S1-S3: the scatters
// ---------------------------------------------------------------------------------------------

/**
 * One scatter axis's measure. The scale type, domain, ticks and unit are not fixed here: they are
 * resolved from the plotted entries by {@link resolveAxis}.
 */
interface ScatterAxisSpec {
  /**
   * The measure's name. Time and cost axes append their unit, and `log scale` when logarithmic, in
   * parentheses; an index axis prints it as is.
   */
  readonly baseTitle: string;
  /** The name a tooltip line is prefixed with: the measure without its unit parenthetical. */
  readonly tooltipLabel: string;
  /** The measure name a value plate prints, short enough that two fit beside a mark. */
  readonly shortLabel: string;
  readonly kind: AxisTickKind;
  readonly better: BetterDirection;
  /** Hard limits of a linear domain. */
  readonly bounds: AxisBounds;
  /** The narrowest linear domain allowed, given the largest plotted value. */
  readonly minSpan: (largest: number) => number;
  /** Whose decimal setting the plates and tooltips follow. */
  readonly measure: NumberMeasure;
  readonly value: (entry: ModelComparisonEntry) => number;
  readonly errLow: (entry: ModelComparisonEntry) => number | undefined;
  readonly errHigh: (entry: ModelComparisonEntry) => number | undefined;
}

/** Both indices: 0-100, never narrower than 20 points. */
const INDEX_BOUNDS: AxisBounds = { min: 0, max: 100 };
const INDEX_MIN_SPAN = (): number => 20;
/** Time and cost: non-negative, never narrower than 30 % of the largest value. */
const MEASURE_BOUNDS: AxisBounds = { min: 0 };
const MEASURE_MIN_SPAN = (largest: number): number => 0.3 * Math.abs(largest);

const QUALITY_AXIS: ScatterAxisSpec = {
  baseTitle: 'Intelligence Index (0-100)',
  tooltipLabel: 'Intelligence Index',
  shortLabel: 'Intelligence',
  kind: 'index',
  better: 'higher',
  bounds: INDEX_BOUNDS,
  minSpan: INDEX_MIN_SPAN,
  measure: 'intelligenceIndex',
  value: (e) => e.intelligenceIndex,
  errLow: (e) => e.intelligenceIndexCi95HalfWidth,
  errHigh: (e) => e.intelligenceIndexCi95HalfWidth,
};

// Known runs can span 7 s to 200 s, where a linear axis collapses every fast model into the left
// edge, so a time or cost axis turns logarithmic once the plotted values span a factor of ten. The
// title then says so, because an unannounced log axis deceives.
const TTFT_AXIS: ScatterAxisSpec = {
  baseTitle: 'Time to first token, median',
  tooltipLabel: 'Time to first token, median',
  shortLabel: 'TTFT P50',
  kind: 'time',
  better: 'lower',
  bounds: MEASURE_BOUNDS,
  minSpan: MEASURE_MIN_SPAN,
  measure: 'ttftP50',
  value: (e) => e.ttftP50Ms,
  // Latency is right-skewed, so the spread is the P50-to-P90 whisker, never a symmetric SD.
  errLow: () => undefined,
  errHigh: (e) => Math.max(0, e.ttftP90Ms - e.ttftP50Ms),
};

const COST_AXIS: ScatterAxisSpec = {
  baseTitle: 'Cost per question',
  tooltipLabel: 'Cost per question',
  shortLabel: 'Cost / question',
  kind: 'usd',
  better: 'lower',
  bounds: MEASURE_BOUNDS,
  minSpan: MEASURE_MIN_SPAN,
  measure: 'costPerQuestion',
  value: (e) => e.candidateCostPerQuestionUsd,
  errLow: (e) => e.candidateCostPerQuestionSdUsd ?? undefined,
  errHigh: (e) => e.candidateCostPerQuestionSdUsd ?? undefined,
};

const SPEED_INDEX_AXIS: ScatterAxisSpec = {
  baseTitle: 'Speed Index (0-100)',
  tooltipLabel: 'Speed Index',
  shortLabel: 'Speed Index',
  kind: 'index',
  better: 'higher',
  bounds: INDEX_BOUNDS,
  minSpan: INDEX_MIN_SPAN,
  measure: 'speedIndex',
  value: (e) => speedValue(e, 'speedIndex') ?? Number.NaN,
  errLow: (e) => e.speedIndexSd ?? undefined,
  errHigh: (e) => e.speedIndexSd ?? undefined,
};

// Model time shares TTFT's order of magnitude (known runs span seconds to minutes), so it takes the
// same scale-type rule. No per-answer dispersion is returned at the DTO level, so the mean draws no
// whisker at all.
const MEAN_MODEL_TIME_AXIS: ScatterAxisSpec = {
  baseTitle: 'Mean time per question',
  tooltipLabel: 'Mean time per question',
  shortLabel: 'Mean time',
  kind: 'time',
  better: 'lower',
  bounds: MEASURE_BOUNDS,
  minSpan: MEASURE_MIN_SPAN,
  measure: 'meanModelTime',
  value: (e) => speedValue(e, 'meanModelTime') ?? Number.NaN,
  errLow: () => undefined,
  errHigh: () => undefined,
};

// Unlike the mean, the per-run total carries a genuine run-to-run SD once R >= 2 - the same
// dispersion the small-multiples panel draws as a whisker.
const TOTAL_MODEL_TIME_AXIS: ScatterAxisSpec = {
  baseTitle: 'Total time for the suite',
  tooltipLabel: 'Total time for the suite',
  shortLabel: 'Suite time',
  kind: 'time',
  better: 'lower',
  bounds: MEASURE_BOUNDS,
  minSpan: MEASURE_MIN_SPAN,
  measure: 'totalModelTime',
  value: (e) => speedValue(e, 'totalModelTime') ?? Number.NaN,
  errLow: (e) => e.totalModelTimeSdMs ?? undefined,
  errHigh: (e) => e.totalModelTimeSdMs ?? undefined,
};

/** The scatter axis for whichever speed measure is currently selected. */
function speedAxisFor(measure: SpeedMeasure): ScatterAxisSpec {
  switch (measure) {
    case 'speedIndex':
      return SPEED_INDEX_AXIS;
    case 'meanModelTime':
      return MEAN_MODEL_TIME_AXIS;
    case 'totalModelTime':
      return TOTAL_MODEL_TIME_AXIS;
    case 'ttftP50':
      return TTFT_AXIS;
  }
}

function scatterPoint(entry: ModelComparisonEntry, x: ScatterAxisSpec, y: ScatterAxisSpec): ErrorBarPoint {
  return {
    x: x.value(entry),
    y: y.value(entry),
    xErrLow: x.errLow(entry),
    xErrHigh: x.errHigh(entry),
    yErrLow: y.errLow(entry),
    yErrHigh: y.errHigh(entry),
  };
}

/** A whisker length that is drawn: positive and finite, otherwise zero. */
function drawnWhisker(err: number | undefined): number {
  return err !== undefined && Number.isFinite(err) && err > 0 ? err : 0;
}

/** True when both coordinates are measured, so the entry is drawn and can take part in the frontier. */
function isMeasured(entry: ModelComparisonEntry, x: ScatterAxisSpec, y: ScatterAxisSpec): boolean {
  return Number.isFinite(x.value(entry)) && Number.isFinite(y.value(entry));
}

/** What a scatter axis takes from the trade-off style. */
interface ScatterAxisStyle {
  readonly whiskers: boolean;
  readonly textSizePx: number;
  readonly titleSizePx: number;
  readonly gridlines: boolean;
  /** Absent: 400. */
  readonly titleWeight?: FigureFontWeight;
  /** Absent: the dark theme. */
  readonly theme?: ResolvedFigureTheme;
}

function scatterAxisStyle(style: ScatterFigureStyle, theme?: ResolvedFigureTheme): ScatterAxisStyle {
  return {
    whiskers: style.intervals,
    textSizePx: style.axisTextSizePx,
    titleSizePx: style.axisTitleSizePx,
    gridlines: style.gridlines,
    titleWeight: style.axisTitleWeight,
    theme,
  };
}

/**
 * A scatter axis resolved against the plotted entries: its scale type, domain, ticks, unit and title,
 * and the Chart.js scale options that draw them.
 *
 * The result depends only on the entries and the measure, so every scatter plotting the same measure
 * over the same entries gets the same axis. Drawn whiskers count towards the domain, so no interval
 * is clipped; hidden ones do not, so the axis fits what is drawn. The ticks are set outright in
 * `afterBuildTicks`; `autoSkip` is only a safety net.
 */
function resolveAxis(
  spec: ScatterAxisSpec,
  plotted: readonly ModelComparisonEntry[],
  axisStyle: ScatterAxisStyle = { whiskers: true, textSizePx: 11, titleSizePx: 12, gridlines: true },
) {
  const values: number[] = [];
  const lows: number[] = [];
  const highs: number[] = [];
  for (const entry of plotted) {
    const value = spec.value(entry);
    if (!Number.isFinite(value)) {
      continue;
    }
    values.push(value);
    lows.push(value - (axisStyle.whiskers ? drawnWhisker(spec.errLow(entry)) : 0));
    highs.push(value + (axisStyle.whiskers ? drawnWhisker(spec.errHigh(entry)) : 0));
  }

  // The indices are bounded 0-100 and always linear.
  const type: ScaleType = spec.kind === 'index'
    ? 'linear'
    : chooseScaleType([...values, ...highs, ...lows.filter((low) => low > 0)]);

  const largest = values.length > 0 ? Math.max(...values.map((value) => Math.abs(value))) : 0;
  // A lower whisker reaching zero cannot sit on a log axis, so the log domain takes the value there;
  // the error-bar plugin clamps that whisker to the axis end.
  const domain: { readonly min: number; readonly max: number; readonly ticks: readonly number[]; readonly step?: number } =
    type === 'logarithmic'
      ? logDomain({ lows: lows.map((low, i) => (low > 0 ? low : values[i])), highs })
      : linearDomain({ lows, highs, bounds: spec.bounds, minSpan: spec.minSpan(largest) });
  const { min, max, ticks, step } = domain;

  const unit: TimeUnit | undefined = spec.kind === 'time' ? timeUnitFor(max) : undefined;
  const parenthetical = [spec.kind === 'time' ? unit : 'USD', ...(type === 'logarithmic' ? ['log scale'] : [])];
  const title = spec.kind === 'index' ? spec.baseTitle : `${spec.baseTitle} (${parenthetical.join(', ')})`;
  const format = (value: number): string => formatTick(value, { kind: spec.kind, unit, step });
  const theme = axisStyle.theme ?? DEFAULT_THEME;

  return {
    type,
    min,
    max,
    ticks,
    unit,
    title,
    scale: {
      type,
      min,
      max,
      title: axisTitle(title, spec.better, axisStyle.titleSizePx, { theme, weight: axisStyle.titleWeight }),
      grid: gridOptions(axisStyle.gridlines, theme),
      border: { color: theme.chart.baseline },
      afterBuildTicks: (scale: { ticks: { value: number }[] }): void => {
        scale.ticks = ticks.map((value) => ({ value }));
      },
      ticks: {
        ...tickOptions(axisStyle.textSizePx, theme),
        autoSkip: true,
        autoSkipPadding: 10,
        maxRotation: 0,
        callback: (value: string | number) => format(Number(value)),
      },
    },
  };
}

/**
 * True when the two entries' 95 % intervals overlap on every axis that carries one, and at least one
 * axis does. An axis carries an interval here when either entry has a whisker on it.
 */
function intervalsOverlap(
  a: ModelComparisonEntry,
  b: ModelComparisonEntry,
  axes: readonly ScatterAxisSpec[],
): boolean {
  let carried = 0;
  for (const axis of axes) {
    const aLow = drawnWhisker(axis.errLow(a));
    const aHigh = drawnWhisker(axis.errHigh(a));
    const bLow = drawnWhisker(axis.errLow(b));
    const bHigh = drawnWhisker(axis.errHigh(b));
    if (aLow + aHigh + bLow + bHigh === 0) {
      continue;
    }
    carried += 1;
    const aValue = axis.value(a);
    const bValue = axis.value(b);
    if (aValue + aHigh < bValue - bLow || bValue + bHigh < aValue - aLow) {
      return false;
    }
  }
  return carried > 0;
}

/** The info note a scatter carries when a dominated model is within the intervals of a frontier model. */
export const FRONTIER_UNCERTAINTY_NOTE =
  'Some differences are within the 95 % intervals, so treat the frontier as indicative rather than a clear win.';

/**
 * What one mark's plate carries, from the two toggles the wizard offers.
 *
 * The value lines use the same formatters as the tooltip, so the plate and the tooltip agree to the
 * digit; the ticks follow their own step. An unmeasured coordinate — a speed measure a model has no
 * figure for — yields no line at all rather than a printed NaN.
 */
function directLabelBlock(
  entry: ModelComparisonEntry,
  axes: readonly (readonly [ScatterAxisSpec, (value: number) => string])[],
  options: { hue: string; muted: boolean; named: boolean; valued: boolean; breakThinkingLevel: boolean },
): DirectLabelBlock {
  const values: DirectLabelValue[] = [];
  if (options.valued) {
    for (const [axis, format] of axes) {
      const value = axis.value(entry);
      if (Number.isFinite(value)) {
        values.push({ label: axis.shortLabel, text: format(value) });
      }
    }
  }
  return {
    name: options.named ? modelLabelLines(entry, options.breakThinkingLevel) : undefined,
    values,
    hue: options.hue,
    ...(options.muted ? { muted: true } : {}),
  };
}

/**
 * One key item per provider among `entries`, in order of first appearance: its dot in the provider's
 * hue and its name. Empty for no entries.
 */
function providerKeyItems(entries: readonly ModelComparisonEntry[], theme: ResolvedFigureTheme): FigureKeyItem[] {
  const seen = new Set<ProviderKey>();
  const items: FigureKeyItem[] = [];
  for (const entry of entries) {
    const key = providerKey(entry.provider);
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    items.push({ glyph: 'provider', text: providerDisplayName(key), color: providerHue(key, theme) });
  }
  return items;
}

/**
 * The direct-label plugin's font and colour keys that differ from its defaults: a family under a
 * bundled font, a name weight other than 400, and the colours where the theme's are not the dark ones.
 */
function directLabelThemeOptions(
  theme: ResolvedFigureTheme,
): Pick<DirectLabelPluginOptions, 'fontFamily' | 'nameFontWeight' | 'surfaceColor' | 'accentColor' | 'inkColor' | 'mutedColor'> {
  return {
    ...(theme.fonts.chartStack === null ? {} : { fontFamily: theme.fonts.chartStack }),
    ...(theme.fonts.labelWeight === 400 ? {} : { nameFontWeight: theme.fonts.labelWeight }),
    ...unlessDefault('surfaceColor', theme.chart.surface, CHART_SURFACE),
    ...unlessDefault('accentColor', theme.chart.accent, ACCENT),
    ...unlessDefault('inkColor', theme.chart.inkSecondary, CHART_INK.secondary),
    ...unlessDefault('mutedColor', theme.chart.inkMuted, CHART_INK.muted),
  };
}

function buildScatter(
  id: string,
  title: string,
  plotted: readonly ModelComparisonEntry[],
  xAxis: ScatterAxisSpec,
  yAxis: ScatterAxisSpec,
  options: FigureOptions,
  extraNotices: readonly string[],
): ChartSpec<'scatter', ErrorBarPoint[]> {
  const { context, reducedMotion, highlightedKey } = options;
  const style = (options.style ?? DEFAULT_FIGURE_STYLE).scatter;
  const theme = themeOf(options);
  const directLabels = options.directLabels ?? false;
  const inlineValues = options.inlineValues ?? false;
  // One plugin carries both: names and values share a label, and so share its placement.
  const annotate = directLabels || inlineValues;
  const radii = scatterPointRadii(style.markRadiusPx);

  const axisStyle = scatterAxisStyle(style, theme);
  const xResolved = resolveAxis(xAxis, plotted, axisStyle);
  const yResolved = resolveAxis(yAxis, plotted, axisStyle);
  // Labels and tooltips write each value in its axis's unit, to the measure's decimal setting.
  const numbers = (options.style ?? DEFAULT_FIGURE_STYLE).numbers;
  const formatX = (value: number): string => formatMeasure(value, xAxis.measure, numbers[xAxis.measure], xResolved.unit);
  const formatY = (value: number): string => formatMeasure(value, yAxis.measure, numbers[yAxis.measure], yResolved.unit);

  // An unmeasured coordinate draws no mark, so it can neither beat a model nor be beaten by one.
  const measured = plotted.filter((entry) => isMeasured(entry, xAxis, yAxis));
  const pareto = computeParetoFrontier(
    measured.map((e) => ({ key: e.key, x: xAxis.value(e), y: yAxis.value(e) })),
    xAxis.better,
    yAxis.better,
  );
  const frontierKeys = new Set(pareto.frontier.map((member) => member.key));
  // A measured model the frontier excludes: another model is better on both axes.
  const isFaded = (entry: ModelComparisonEntry): boolean =>
    isMeasured(entry, xAxis, yAxis) && !frontierKeys.has(entry.key);

  const modelDatasets = plotted.map((entry) => {
    const hue = providerHue(entry.provider, theme);
    const highlighted = highlightedKey === entry.key;
    const faded = isFaded(entry);
    // R = 1 draws hollow; R >= 2 draws solid with a surface ring so overlapping marks stay legible.
    const hollow = entry.runCount === 1;
    const fill = hollow ? 'transparent' : hue;
    const border = highlighted ? theme.chart.accent : hollow ? hue : theme.chart.surface;
    const point = scatterPoint(entry, xAxis, yAxis);
    return {
      label: entry.label,
      data: [faded ? { ...point, alpha: FADED_MARK_ALPHA } : point],
      pointStyle: 'circle' as const,
      backgroundColor: faded ? withAlpha(fill, FADED_MARK_ALPHA) : fill,
      borderColor: faded && !highlighted ? withAlpha(border, FADED_MARK_ALPHA) : border,
      borderWidth: 2,
      ...radii,
      showLine: false,
    };
  });

  // The members' own points joined by straight dotted segments, drawn after the marks (the lowest
  // `order` draws last) so the dots pass through the marks' surface rings. One member draws no line,
  // and the style's switch can hide it; fading and the highlight still follow the frontier.
  const frontierDrawn = style.frontierLine && pareto.path.length >= 2;
  const frontierDatasets = frontierDrawn
    ? [{
        label: PARETO_FRONTIER_LABEL,
        data: pareto.path.map((p): ErrorBarPoint => ({ x: p.x, y: p.y })),
        pointStyle: 'circle' as const,
        backgroundColor: 'transparent',
        borderColor: theme.chart.inkSecondary,
        borderWidth: style.frontierWidthPx,
        borderDash: [...FRONTIER_DASH],
        borderCapStyle: 'round' as const,
        radius: 0,
        hoverRadius: 0,
        hitRadius: 0,
        showLine: true,
        order: -1,
      }]
    : [];
  const datasets = [...modelDatasets, ...frontierDatasets];

  const preferredCorner: PreferredCorner = {
    x: xAxis.better === 'lower' ? 'left' : 'right',
    y: yAxis.better === 'higher' ? 'top' : 'bottom',
    label: 'Better',
  };

  const highlightedIndex = plotted.findIndex((entry) => entry.key === highlightedKey);

  const config: ChartConfiguration<'scatter', ErrorBarPoint[]> = {
    type: 'scatter',
    data: { datasets },
    options: {
      ...baseOptions(reducedMotion),
      scales: {
        x: xResolved.scale,
        y: yResolved.scale,
      },
      plugins: {
        legend: {
          // The direct labels name every mark on the canvas, so a legend would say it all twice.
          display: !directLabels,
          position: style.legendPosition,
          labels: {
            color: theme.chart.inkSecondary,
            usePointStyle: true,
            ...legendFont(theme),
            // The frontier is an annotation, not a series, so it stays out of the identity legend.
            filter: (item) => item.text !== PARETO_FRONTIER_LABEL,
            // Only a vertical legend stacks an array's lines; a horizontal one would overlap them.
            ...(!directLabels && style.legendPosition === 'right' && style.thinkingLevelBreak
              ? {
                  generateLabels: (chart: Chart) =>
                    Chart.defaults.plugins.legend.labels.generateLabels(chart).map((item) =>
                      item.datasetIndex !== undefined && item.datasetIndex < plotted.length
                        // Chart.js 4 draws an array as stacked lines; its typings declare a string.
                        ? { ...item, text: modelLabelLines(plotted[item.datasetIndex], true) as string }
                        : item),
                }
              : {}),
          },
        },
        tooltip: {
          ...TOOLTIP_STYLE,
          // The frontier's vertices sit on the members' own coordinates, so nearest-without-
          // intersect would list the annotation beside the model it was derived from.
          filter: (item) => item.datasetIndex < plotted.length,
          callbacks: {
            title: (items) => items[0]?.dataset.label ?? '',
            label: (item) => {
              // Chart.js parses a gap as a null coordinate. No entry plotted here carries one, so
              // this is the unreachable branch rather than a formatting case.
              const { x, y } = item.parsed;
              if (x === null || y === null) {
                return 'not measured';
              }
              return [
                `${xAxis.tooltipLabel}: ${formatX(x)}`,
                `${yAxis.tooltipLabel}: ${formatY(y)}`,
              ];
            },
          },
        },
        // Identity text on a scatter is the direct-label plugin's job, behind the wizard's own
        // toggle; the datalabels plugin is not registered on these figures at all.
        datalabels: { display: false },
        ...(annotate
          ? {
              [directLabelPlugin.id]: {
                blocks: plotted.map((entry) => directLabelBlock(entry, [[xAxis, formatX], [yAxis, formatY]], {
                  hue: providerHue(entry.provider, theme),
                  muted: isFaded(entry),
                  named: directLabels,
                  valued: inlineValues,
                  breakThinkingLevel: style.thinkingLevelBreak,
                })),
                highlightedIndex: highlightedIndex < 0 ? undefined : highlightedIndex,
                fontSizePx: style.labelTextSizePx,
                markRadiusPx: style.markRadiusPx,
                // A hidden whisker must not push a label away from empty space.
                avoidWhiskers: style.intervals,
                ...directLabelThemeOptions(theme),
              } satisfies DirectLabelPluginOptions,
            }
          : {}),
        ...(style.intervals ? errorBarOptions(theme) : {}),
        ...plotFrameOptions(theme, style.plotFrame),
      },
    },
  };

  const hasCostAxis = xAxis.kind === 'usd' || yAxis.kind === 'usd';
  const badges: FigureBadge[] = visibleBadges([
    ...countBadges(plotted, context),
    ...(hasCostAxis ? [pricingBadge(context.pricingBasis, context.pricedOn)] : []),
  ], style.hiddenBadges);

  // The key explains only the marks this figure actually draws.
  const key: FigureKeyItem[] = [];
  if (measured.some((entry) => entry.runCount === 1)) {
    key.push({ glyph: 'hollow', text: 'Single run' });
  }
  if (measured.some((entry) => entry.runCount >= 2)) {
    key.push({ glyph: 'solid', text: 'Mean of 2+ runs' });
  }
  const whiskered = (entry: ModelComparisonEntry): boolean =>
    [xAxis.errLow, xAxis.errHigh, yAxis.errLow, yAxis.errHigh].some((err) => drawnWhisker(err(entry)) > 0);
  const anyWhisker = measured.some(whiskered);
  if (style.intervals && anyWhisker) {
    key.push({ glyph: 'interval', text: '95 % interval' });
  }
  if (frontierDrawn) {
    key.push({ glyph: 'frontier', text: 'Pareto frontier: models nothing beats on both axes' });
  }
  const faded = measured.filter(isFaded);
  if (faded.length > 0) {
    key.push({ glyph: 'faded', text: 'Faded: another model is better on both axes' });
  }
  key.push(...providerKeyItems(measured, theme));

  const labels = new Map(measured.map((entry): [string, string] => [entry.key, entry.label]));
  const frontierLabels = pareto.frontier.map((member) => labels.get(member.key) ?? member.key);
  const highlight = frontierLabels.length === 0
    ? ''
    : frontierLabels.length === 1
      ? `${frontierLabels[0]} is best on both axes`
      : `Best trade-offs: ${frontierLabels.join(', ')}`;

  const frontierEntries = measured.filter((entry) => frontierKeys.has(entry.key));
  const withinIntervals = faded
    .some((dominated) => frontierEntries.some((member) => intervalsOverlap(dominated, member, [xAxis, yAxis])));

  const notes: FigureNote[] = [
    ...warningNotes(extraNotices),
    ...(withinIntervals && style.frontierIntervalsNote
      ? [{ text: FRONTIER_UNCERTAINTY_NOTE, tone: 'info' } satisfies FigureNote]
      : []),
    ...(!style.intervals && style.hiddenIntervalsNote && anyWhisker
      ? [{ text: HIDDEN_INTERVALS_NOTE, tone: 'info' } satisfies FigureNote]
      : []),
  ];

  const chrome: FigureChrome = {
    title,
    badges,
    ...(style.hiddenBadges.includes('direction') ? {} : { direction: preferredCorner }),
    detail: hasCostAxis ? pricingNote(context.pricingBasis) : '',
    key,
    highlight,
    notes,
  };

  const summary = [
    ...(frontierLabels.length > 0 ? [`Pareto frontier: ${frontierLabels.join(', ')}`] : []),
    ...(faded.length > 0 ? [`Faded: ${faded.map((entry) => entry.label).join(', ')}`] : []),
  ];

  return {
    id,
    title,
    chrome,
    preferredCorner,
    config,
    // A leader line draws over a whisker rather than under it.
    plugins: withPlotFrame([
      ...(style.intervals ? [errorBarPlugin] : []),
      ...(annotate ? [directLabelPlugin] : []),
    ], style.plotFrame),
    summary,
  };
}

/** S1 — Intelligence Index against speed, on whichever speed measure is currently selected. */
export function buildQualitySpeedScatter(
  plotted: readonly ModelComparisonEntry[],
  options: FigureOptions,
): ChartSpec<'scatter', ErrorBarPoint[]> {
  return buildScatter(
    's1-quality-speed',
    'Intelligence against speed',
    plotted,
    speedAxisFor(options.speedMeasure ?? 'meanModelTime'),
    QUALITY_AXIS,
    options,
    degradedNotices(plotted, ['speed']),
  );
}

/** S2 — Intelligence Index against candidate cost per question. */
export function buildQualityCostScatter(
  plotted: readonly ModelComparisonEntry[],
  options: FigureOptions,
): ChartSpec<'scatter', ErrorBarPoint[]> {
  return buildScatter(
    's2-quality-cost',
    'Intelligence against cost',
    plotted,
    COST_AXIS,
    QUALITY_AXIS,
    options,
    degradedNotices(plotted, ['cost']),
  );
}

/** S3 — candidate cost per question against speed, on whichever speed measure is currently selected. */
export function buildSpeedCostScatter(
  plotted: readonly ModelComparisonEntry[],
  options: FigureOptions,
): ChartSpec<'scatter', ErrorBarPoint[]> {
  return buildScatter(
    's3-speed-cost',
    'Speed against cost',
    plotted,
    speedAxisFor(options.speedMeasure ?? 'meanModelTime'),
    COST_AXIS,
    options,
    degradedNotices(plotted, ['speed', 'cost']),
  );
}

// ---------------------------------------------------------------------------------------------
// P1: the three linked panels
// ---------------------------------------------------------------------------------------------

export type BarOrientation = 'vertical' | 'horizontal';

/**
 * Below this container width the three panels stack and their bars turn horizontal, so eight model
 * names read as left-aligned labels instead of colliding or being rotated past 45 degrees.
 */
export const P1_STACK_BREAKPOINT_PX = 720;

export interface SmallMultiplesOptions extends FigureOptions {
  readonly speedMeasure: SpeedMeasure;
  readonly costMeasure: CostMeasure;
  readonly orientation: BarOrientation;
}

export interface SmallMultiplesFigure {
  readonly quality: ChartSpec<'bar', ErrorBarPoint[], PanelCategoryLabel>;
  readonly speed: ChartSpec<'bar', ErrorBarPoint[], PanelCategoryLabel>;
  readonly cost: ChartSpec<'bar', ErrorBarPoint[], PanelCategoryLabel>;
  /** The single model order all three panels share, as entry keys, in drawing order. */
  readonly order: readonly string[];
  readonly notices: readonly string[];
}

/**
 * A panel's category tick: the model name, or the name over `n = 1` where one run backs it and the
 * style shows the marker.
 *
 * Chart.js renders a string array as a multi-line tick on either axis orientation, which is where
 * the single-run marker lives: beside the bar it collided with the value label, and on the axis it
 * stays out of the plot area entirely.
 */
export type PanelCategoryLabel = string | string[];

function barPoint(
  index: number,
  value: number,
  errLow: number | undefined,
  errHigh: number | undefined,
  orientation: BarOrientation,
): ErrorBarPoint {
  return orientation === 'vertical'
    ? { x: index, y: value, yErrLow: errLow, yErrHigh: errHigh }
    : { x: value, y: index, xErrLow: errLow, xErrHigh: errHigh };
}

/** The context a datalabels callback receives: only the fields this panel's labels read. */
interface DataLabelCtx {
  readonly dataIndex: number;
  readonly chart: { scales: Record<string, { getPixelForValue(value: number): number }> };
}

function buildPanel(
  id: string,
  title: string,
  plotted: readonly ModelComparisonEntry[],
  categoryLabels: readonly PanelCategoryLabel[],
  axisTitleText: string,
  better: BetterDirection,
  axisMax: number | undefined,
  format: (value: number) => string,
  tick: { readonly kind: AxisTickKind; readonly unit?: TimeUnit },
  values: readonly (number | null)[],
  errLows: readonly (number | undefined)[],
  errHighs: readonly (number | undefined)[],
  options: SmallMultiplesOptions,
  notes: readonly FigureNote[],
  costPanel: boolean,
  /** Breaks the value-axis title at its final parenthetical unless the style says never. */
  breakAxisTitle = false,
): ChartSpec<'bar', ErrorBarPoint[], PanelCategoryLabel> {
  const { context, reducedMotion, highlightedKey, selectedKeys, orientation } = options;
  const style = (options.style ?? DEFAULT_FIGURE_STYLE).bar;
  const theme = themeOf(options);
  const emphasised = new Set<string>(selectedKeys ?? []);
  if (highlightedKey) {
    emphasised.add(highlightedKey);
  }
  const hasEmphasis = emphasised.size > 0;

  const data = plotted.map((_entry, index) =>
    barPoint(index, values[index] ?? 0, errLows[index], errHighs[index], orientation),
  );

  // A bar wears its model's provider hue, as the marks of every other figure do. Bars are never
  // ramped by value: darker-where-bigger would re-encode the length the bar already shows. The
  // Highlight emphasis overrides the hue with the accent and the gray.
  const hollow = (entry: ModelComparisonEntry): boolean => entry.runCount === 1 && !style.filledBars;
  const fills = plotted.map((entry) => {
    if (!hasEmphasis) {
      return hollow(entry) ? 'transparent' : providerHue(entry.provider, theme);
    }
    return emphasised.has(entry.key) ? (hollow(entry) ? 'transparent' : theme.chart.accent) : theme.chart.deEmphasisFill;
  });
  const strokes = plotted.map((entry) => {
    if (!hasEmphasis) {
      return providerHue(entry.provider, theme);
    }
    return emphasised.has(entry.key) ? theme.chart.accent : theme.chart.deEmphasisStroke;
  });

  // Places a value label past the SD whisker rather than on top of it: zero when the entry carries
  // no errHigh or the whiskers are hidden, so the label sits directly past the bar's own end.
  const whiskerLength = (ctx: DataLabelCtx): number => {
    const value = values[ctx.dataIndex];
    const errHigh = errHighs[ctx.dataIndex];
    if (!style.intervals || value === null || errHigh === undefined) {
      return 0;
    }
    const scale = ctx.chart.scales[orientation === 'vertical' ? 'y' : 'x'];
    return Math.abs(scale.getPixelForValue(value + errHigh) - scale.getPixelForValue(value));
  };

  // The Better badge says which end is better, so the value-axis title says it only while the
  // badge is hidden.
  const directionShown = !style.hiddenBadges.includes('direction');
  const direction: FigureDirection = orientation === 'vertical'
    ? { y: better === 'higher' ? 'top' : 'bottom', label: 'Better' }
    : { x: better === 'higher' ? 'right' : 'left', label: 'Better' };

  const titleFont: AxisTitleFont = { theme, weight: style.axisTitleWeight };
  const categoryScale = {
    type: 'category' as const,
    title: axisTitle('Model', undefined, style.axisTitleSizePx, titleFont),
    grid: { display: false },
    border: { color: theme.chart.baseline },
    ticks: {
      ...tickOptions(style.axisTextSizePx, theme),
      color: theme.chart.inkSecondary,
      maxRotation: orientation === 'vertical' ? 45 : 0,
    },
  };
  // Bars require a zero baseline, always: a truncated bar axis misstates the ratio that is the
  // entire reason to draw a bar.
  const valueScale = {
    type: 'linear' as const,
    beginAtZero: true,
    min: 0,
    max: axisMax,
    ...valueAxisTitle(
      axisTitleText,
      directionShown ? undefined : better,
      breakAxisTitle && style.axisTitleBreak !== 'never' ? 'always' : style.axisTitleBreak,
      style.axisTitleSizePx,
      titleFont,
    ),
    grid: gridOptions(style.gridlines, theme),
    border: { color: theme.chart.baseline },
    // The tick decimals follow Chart.js's own step, read off the first two ticks. The unit is the
    // axis title's, so a zero tick reads `0 s` on a seconds axis.
    ticks: {
      ...tickOptions(style.axisTextSizePx, theme),
      callback: (value: string | number, _index: number, ticks: readonly { value: number }[]) =>
        formatTick(Number(value), {
          ...tick,
          step: ticks.length > 1 ? Math.abs(ticks[1].value - ticks[0].value) : undefined,
        }),
    },
    // Headroom for the value label every bar carries. Only reached where axisMax is undefined -
    // grace is ignored once max is explicit, and the label is clamped into the area there instead.
    ...(axisMax === undefined ? { grace: '12%' } : {}),
  };

  const config: ChartConfiguration<'bar', ErrorBarPoint[], PanelCategoryLabel> = {
    type: 'bar',
    data: {
      labels: [...categoryLabels],
      datasets: [
        {
          label: title,
          data,
          backgroundColor: fills,
          borderColor: strokes,
          borderWidth: style.outlineWidthPx,
          borderRadius: style.cornerRadiusPx,
          // Only the far end is rounded; the end sitting on the baseline stays square.
          borderSkipped: orientation === 'vertical' ? 'bottom' : 'left',
          // The bar fills its slot less the space, up to the maximum width; past it the space grows.
          ...(style.maxBarWidthPx === null ? {} : { maxBarThickness: style.maxBarWidthPx }),
          categoryPercentage: 1,
          barPercentage: 1 - style.gapPercent / 100,
        },
      ],
    },
    options: {
      ...baseOptions(reducedMotion),
      indexAxis: orientation === 'vertical' ? 'x' : 'y',
      scales:
        orientation === 'vertical' ? { x: categoryScale, y: valueScale } : { x: valueScale, y: categoryScale },
      plugins: {
        // One series, so no legend box: the panel title already names what is plotted.
        legend: { display: false },
        tooltip: {
          ...TOOLTIP_STYLE,
          callbacks: {
            title: (items) => plotted[items[0]?.dataIndex ?? 0]?.label ?? '',
            label: (item) => {
              const value = values[item.dataIndex];
              return value === null ? 'not measured' : `${axisTitleText}: ${format(value)}`;
            },
          },
        },
        // Every bar states its own value, past the whisker cap rather than inside the bar: a
        // solid bar and a hollow one would carry secondary ink differently, and on the panel hue
        // it would fail contrast outright.
        datalabels: {
          display: style.valueLabels ? (ctx: DataLabelCtx) => values[ctx.dataIndex] !== null : false,
          formatter: (_v: unknown, ctx: DataLabelCtx) => format(values[ctx.dataIndex] ?? 0),
          anchor: 'end' as const,
          align: orientation === 'vertical' ? ('top' as const) : ('right' as const),
          clamp: true,
          offset: (ctx: DataLabelCtx) => whiskerLength(ctx) + 4,
          color: theme.chart.inkSecondary,
          font: { size: style.valueLabelSizePx, ...labelFont(theme) },
        },
        ...(style.intervals ? errorBarOptions(theme) : {}),
        ...plotFrameOptions(theme, style.plotFrame),
      },
    },
  };

  const plugins: Plugin[] = withPlotFrame(style.intervals
    ? [errorBarPlugin, ChartDataLabels as Plugin]
    : [ChartDataLabels as Plugin], style.plotFrame);

  const drawsWhisker = plotted.some((_entry, index) =>
    [errLows[index], errHighs[index]].some((err) => drawnWhisker(err) > 0),
  );
  const panelNotes: FigureNote[] = [
    ...notes,
    ...(!style.intervals && style.hiddenIntervalsNote && drawsWhisker
      ? [{ text: HIDDEN_INTERVALS_NOTE, tone: 'info' } satisfies FigureNote]
      : []),
  ];

  const chrome: FigureChrome = {
    title,
    badges: visibleBadges([
      ...countBadges(plotted, context),
      ...(costPanel ? [pricingBadge(context.pricingBasis, context.pricedOn)] : []),
    ], style.hiddenBadges),
    ...(directionShown ? { direction } : {}),
    detail: costPanel ? pricingNote(context.pricingBasis) : '',
    key: providerKeyItems(plotted, theme),
    highlight: '',
    notes: panelNotes,
  };

  return {
    id,
    title,
    chrome,
    config,
    plugins,
  };
}

/** The Speed panel's info note under mean model time per question, which carries no dispersion. */
export const MEAN_TIME_NO_INTERVAL_NOTE =
  'Mean time per question has no uncertainty bar: the spread across questions is not recorded.';

/** The Speed panel's time unit, from the largest finite plotted value. */
function barSpeedUnit(plotted: readonly ModelComparisonEntry[], speedMeasure: SpeedMeasure): TimeUnit {
  const measured = plotted
    .map((e) => speedValue(e, speedMeasure))
    .filter((v): v is number => v !== null && Number.isFinite(v));
  return timeUnitFor(measured.length > 0 ? Math.max(...measured) : 0);
}

/**
 * P1 — three aligned bar panels sharing one model order, so a row is one model and a column is one
 * measure. Each panel keeps its own real units and its own axis title; there is no shared y-axis and
 * no second y-axis, because three incommensurable measures on one scale is the worst chart mistake
 * there is.
 */
export function buildSmallMultiples(
  plotted: readonly ModelComparisonEntry[],
  options: SmallMultiplesOptions,
): SmallMultiplesFigure {
  const { context, speedMeasure, costMeasure } = options;
  const barStyle = (options.style ?? DEFAULT_FIGURE_STYLE).bar;

  const qualityValues = plotted.map((e) => e.intelligenceIndex);
  const qualityErr = plotted.map((e) => e.intelligenceIndexCi95HalfWidth);

  const speedValues = plotted.map((e) => speedValue(e, speedMeasure));
  // Speed Index has no percentile spread, so its whisker is the reproducibility SD. TTFT carries
  // the P50-to-P90 whisker, one-sided because latency is right-skewed. The total model time for
  // the whole suite carries a genuine run-to-run SD once R >= 2. The mean model time per question
  // has no per-answer dispersion at the DTO level, so it draws no whisker at all.
  const speedErrLow = plotted.map((e) => {
    if (speedMeasure === 'speedIndex') {
      return e.speedIndexSd ?? undefined;
    }
    if (speedMeasure === 'totalModelTime') {
      return e.totalModelTimeSdMs ?? undefined;
    }
    return undefined;
  });
  const speedErrHigh = plotted.map((e) => {
    if (speedMeasure === 'speedIndex') {
      return e.speedIndexSd ?? undefined;
    }
    if (speedMeasure === 'totalModelTime') {
      return e.totalModelTimeSdMs ?? undefined;
    }
    if (speedMeasure === 'ttftP50') {
      return Math.max(0, e.ttftP90Ms - e.ttftP50Ms);
    }
    return undefined;
  });

  const costValues = plotted.map((e) => costValue(e, costMeasure, context));
  // At R = 1 there is no reproducibility SD, so the bar carries no interval at all - silence would
  // read as certainty, which is why the category tick says `n = 1` under the model's name unless the
  // style hides it.
  const costSd = plotted.map((e) =>
    costMeasure === 'candidateSuite' ? suiteCostSdUsd(e) ?? undefined : e.totalRunCostSdUsd ?? undefined,
  );

  const saturated = plotted.filter((e) => e.speedIndexSaturated);
  const speedNotes = warningNotes(degradedNotices(plotted, ['speed']));
  if (speedMeasure === 'speedIndex' && saturated.length > 0) {
    speedNotes.push({
      text:
        `Speed Index is saturated for ${saturated.length} of ${plotted.length} plotted ` +
        `${plotted.length === 1 ? 'entry' : 'entries'}: several models sit at the ceiling and are not ` +
        'distinguishable on this panel even when their real latency differs severalfold. Switch the ' +
        'measure to TTFT P50 to separate them.',
      tone: 'warning',
    });
  }
  const missingIndex = plotted.filter((e) => e.speedIndex === null);
  if (speedMeasure === 'speedIndex' && missingIndex.length > 0) {
    speedNotes.push({
      text: `No Speed Index for ${missingIndex.map((e) => e.label).join(', ')}; those bars are drawn at zero.`,
      tone: 'warning',
    });
  }
  if (speedMeasure === 'meanModelTime' && barStyle.meanTimeNoIntervalNote) {
    speedNotes.push({ text: MEAN_TIME_NO_INTERVAL_NOTE, tone: 'info' });
  }

  // The unit follows the largest plotted time, and the title, the ticks and the values all carry it.
  const speedUnit = barSpeedUnit(plotted, speedMeasure);
  const numbers = (options.style ?? DEFAULT_FIGURE_STYLE).numbers;
  const speedKey = speedNumberMeasure(speedMeasure);
  const costKey = costNumberMeasure(costMeasure);
  const speedTitle =
    speedMeasure === 'speedIndex'
      ? 'Speed Index (0-100)'
      : speedMeasure === 'meanModelTime'
        ? `Mean time per question (${speedUnit})`
        : speedMeasure === 'totalModelTime'
          ? `Total time for the suite (${speedUnit})`
          : `Time to first token, median (${speedUnit})`;
  // Two lines on the axis, the parenthetical under the head, so the title is never clipped.
  const costTitle =
    costMeasure === 'candidateSuite'
      ? context.questionsAskedPerRun != null
        ? `Candidate cost per ${context.costUnit ?? 'suite run'} (USD, ${formatQuestionsAsked(context.questionsAskedPerRun)})`
        : `Candidate cost per ${context.costUnit ?? 'suite run'} (USD)`
      : 'Total run cost including grading roles (USD)';

  // One label list for all three panels. A two-line tick block on one panel alone would shrink
  // that panel's plot area and put its bars out of line with the other two, which share a height
  // and a model order.
  const categoryLabels: PanelCategoryLabel[] = plotted.map((e) => {
    const lines = ([] as string[]).concat(modelLabelLines(e, barStyle.thinkingLevelBreak));
    if (e.runCount === 1 && barStyle.singleRunMarker) {
      lines.push('n = 1');
    }
    return lines.length === 1 ? lines[0] : lines;
  });

  return {
    quality: buildPanel(
      'p1a-quality',
      'Intelligence',
      plotted,
      categoryLabels,
      'Intelligence Index (0-100)',
      'higher',
      100,
      (v) => formatMeasure(v, 'intelligenceIndex', numbers.intelligenceIndex),
      { kind: 'index' },
      qualityValues,
      qualityErr,
      qualityErr,
      options,
      [],
      false,
    ),
    speed: buildPanel(
      'p1b-speed',
      'Speed',
      plotted,
      categoryLabels,
      speedTitle,
      speedLowerIsBetter(speedMeasure) ? 'lower' : 'higher',
      speedMeasure === 'speedIndex' ? 100 : undefined,
      (v) => formatMeasure(v, speedKey, numbers[speedKey], speedUnit),
      speedMeasure === 'speedIndex' ? { kind: 'index' } : { kind: 'time', unit: speedUnit },
      speedValues,
      speedErrLow,
      speedErrHigh,
      options,
      speedNotes,
      false,
    ),
    cost: buildPanel(
      'p1c-cost',
      'Cost',
      plotted,
      categoryLabels,
      costTitle,
      'lower',
      undefined,
      (v) => formatMeasure(v, costKey, numbers[costKey]),
      { kind: 'usd' },
      costValues,
      costSd,
      costSd,
      options,
      warningNotes(degradedNotices(plotted, ['cost'])),
      true,
      costMeasure === 'candidateSuite',
    ),
    order: plotted.map((e) => e.key),
    notices: [],
  };
}

// ---------------------------------------------------------------------------------------------
// P2: the model profiles, as small multiples
// ---------------------------------------------------------------------------------------------

export type ProfileAxisId = 'quality' | 'speed' | 'cost';

/** The axis order is fixed. A reorder control would change which dips show without changing the
 *  data, which invites reading a pattern that is an artifact of the control. */
export const PROFILE_AXIS_ORDER: readonly ProfileAxisId[] = ['quality', 'speed', 'cost'];

/** The narrowest index domain a profile axis spans, in index points. */
export const PROFILE_INDEX_MIN_SPAN = 20;

/** How far a log domain reaches past the best and the worst value, as a ratio. */
export const PROFILE_LOG_PADDING = 1.25;

export interface ProfileAxis {
  readonly id: ProfileAxisId;
  readonly title: string;
  /** Real minimum across the plotted set. */
  readonly min: number;
  readonly max: number;
  readonly minLabel: string;
  readonly maxLabel: string;
  /** True when the raw measure improves downwards, so the scale inverts it. */
  readonly lowerIsBetter: boolean;
  /** Log for times and costs, where equal steps are equal ratios; linear for the indices. */
  readonly scale: 'linear' | 'log';
  /** The shared domain, in the measure's own units. Its better end is 1 on every tile, its worse end 0. */
  readonly domainMin: number;
  readonly domainMax: number;
  /** The measure's name inside a sentence: `intelligence`, `model time`, `cost`. */
  readonly noun: string;
  /** What follows a value in a sentence, such as ` per suite run`; empty for most axes. */
  readonly suffix: string;
}

export interface ProfileRow {
  readonly key: string;
  readonly label: string;
  /**
   * Values on the shared scales in {@link PROFILE_AXIS_ORDER}, oriented so 1 is always the better
   * end. NaN where the model has no value.
   */
  readonly values: readonly number[];
  /** The real values behind them, for the printed values and the tooltip. */
  readonly raw: readonly number[];
}

/** One model's accessible description: its three values and its weakest axis. */
export interface ProfileDescription {
  readonly key: string;
  readonly text: string;
}

export interface ProfileNormalization {
  readonly axes: readonly ProfileAxis[];
  readonly rows: readonly ProfileRow[];
  /** One sentence per model, in entry order, e.g. `GPT-6 Astra (medium): intelligence 84, model time 15.5 s, cost $0.140 per suite run; weakest axis: cost`. */
  readonly descriptions: readonly ProfileDescription[];
}

/**
 * Puts every model on three shared scales, oriented so that up is better on each.
 *
 * Intelligence is linear over the union of the plotted models' 95 % intervals, at least
 * {@link PROFILE_INDEX_MIN_SPAN} points wide and inside 0-100, so a small spread inside overlapping
 * intervals never spans the whole axis. Times and costs are logarithmic over
 * [best / {@link PROFILE_LOG_PADDING}, worst x {@link PROFILE_LOG_PADDING}] and inverted, so faster
 * and cheaper are up and equal steps are equal ratios. The Speed Index, when it is the speed
 * measure, is an index and takes the intelligence rule without the intervals.
 */
export function normalizeProfile(
  plotted: readonly ModelComparisonEntry[],
  options: ProfileNormalizationOptions,
): ProfileNormalization {
  const { axes, rows, descriptions } = prepareProfile(plotted, options);
  return { axes, rows, descriptions };
}

export interface ProfileNormalizationOptions {
  readonly context: ModelComparisonContext;
  readonly speedMeasure: SpeedMeasure;
  readonly costMeasure: CostMeasure;
  /** Decimals of the printed values, the ranges and the tooltip. Defaults to {@link DEFAULT_MEASURE_DECIMALS}. */
  readonly numbers?: NumberFormatStyle;
}

function profileRawValue(
  id: ProfileAxisId,
  entry: ModelComparisonEntry,
  options: ProfileNormalizationOptions,
): number {
  switch (id) {
    case 'quality':
      return entry.intelligenceIndex;
    case 'speed':
      return speedValue(entry, options.speedMeasure) ?? 0;
    case 'cost':
      return costValue(entry, options.costMeasure, options.context);
  }
}

/** The profile's time unit, from the largest finite raw speed value in the plotted set. */
function profileTimeUnit(plotted: readonly ModelComparisonEntry[], options: ProfileNormalizationOptions): TimeUnit {
  const raw = plotted.map((entry) => profileRawValue('speed', entry, options)).filter((v) => Number.isFinite(v));
  return timeUnitFor(raw.length > 0 ? Math.max(...raw) : 0);
}

/** An index domain over the given extents: inside 0-100 and at least {@link PROFILE_INDEX_MIN_SPAN} wide. */
export function profileIndexDomain(lows: readonly number[], highs: readonly number[]): [number, number] {
  const finiteLows = lows.filter((v) => Number.isFinite(v));
  const finiteHighs = highs.filter((v) => Number.isFinite(v));
  let low = finiteLows.length > 0 ? Math.max(0, Math.min(...finiteLows)) : 0;
  let high = finiteHighs.length > 0 ? Math.min(100, Math.max(...finiteHighs)) : 100;
  if (high - low < PROFILE_INDEX_MIN_SPAN) {
    const middle = (low + high) / 2;
    low = Math.max(0, Math.min(100 - PROFILE_INDEX_MIN_SPAN, middle - PROFILE_INDEX_MIN_SPAN / 2));
    high = low + PROFILE_INDEX_MIN_SPAN;
  }
  return [low, high];
}

/** A log domain from the best value / {@link PROFILE_LOG_PADDING} to the worst x it; [1, 1] with no positive value. */
export function profileRatioDomain(values: readonly number[]): [number, number] {
  const positive = values.filter((v) => Number.isFinite(v) && v > 0);
  if (positive.length === 0) {
    return [1, 1];
  }
  return [Math.min(...positive) / PROFILE_LOG_PADDING, Math.max(...positive) * PROFILE_LOG_PADDING];
}

/** A raw value's height on its axis's shared scale: 0 at the worse end, 1 at the better, NaN when unmeasured. */
function profileHeight(axis: Pick<ProfileAxis, 'scale' | 'lowerIsBetter' | 'domainMin' | 'domainMax'>, value: number): number {
  if (!Number.isFinite(value)) {
    return Number.NaN;
  }
  let t: number;
  if (axis.scale === 'log') {
    const span = Math.log(axis.domainMax) - Math.log(axis.domainMin);
    // A collapsed axis has no ordering to show, so every model sits mid-axis.
    t = span === 0 ? 0.5 : value <= 0 ? 0 : (Math.log(value) - Math.log(axis.domainMin)) / span;
  } else {
    const span = axis.domainMax - axis.domainMin;
    t = span === 0 ? 0.5 : (value - axis.domainMin) / span;
  }
  const oriented = axis.lowerIsBetter ? 1 - t : t;
  return Math.min(1, Math.max(0, oriented));
}

/** The shared scales, each model's heights and descriptions, and each axis's value formatter. */
function prepareProfile(
  plotted: readonly ModelComparisonEntry[],
  options: ProfileNormalizationOptions,
): ProfileNormalization & { readonly formats: readonly ((value: number) => string)[] } {
  const { speedMeasure, costMeasure } = options;
  const numbers = options.numbers ?? DEFAULT_MEASURE_DECIMALS;
  const rawFor = (id: ProfileAxisId, entry: ModelComparisonEntry): number => profileRawValue(id, entry, options);
  const speedKey = speedNumberMeasure(speedMeasure);
  const costKey = costNumberMeasure(costMeasure);
  const timeUnit = profileTimeUnit(plotted, options);
  const speedIsIndex = speedMeasure === 'speedIndex';

  const axisMeta: Record<ProfileAxisId, {
    title: string;
    noun: string;
    suffix: string;
    lowerIsBetter: boolean;
    scale: 'linear' | 'log';
    format: (v: number) => string;
  }> = {
    quality: {
      title: 'Intelligence',
      noun: 'intelligence',
      suffix: '',
      lowerIsBetter: false,
      scale: 'linear',
      format: (v) => formatMeasure(v, 'intelligenceIndex', numbers.intelligenceIndex),
    },
    speed: {
      title: speedIsIndex
        ? 'Speed Index'
        : speedMeasure === 'meanModelTime'
          ? 'Speed (mean model time)'
          : speedMeasure === 'totalModelTime'
            ? 'Speed (total model time)'
            : 'Speed (TTFT P50)',
      noun: speedIsIndex
        ? 'Speed Index'
        : speedMeasure === 'meanModelTime'
          ? 'model time'
          : speedMeasure === 'totalModelTime'
            ? 'total model time'
            : 'time to first token',
      suffix: '',
      lowerIsBetter: speedLowerIsBetter(speedMeasure),
      scale: speedIsIndex ? 'linear' : 'log',
      format: (v) => formatMeasure(v, speedKey, numbers[speedKey], timeUnit),
    },
    cost: {
      title: costMeasure === 'candidateSuite' ? 'Cost (candidate, suite)' : 'Cost (total run)',
      noun: 'cost',
      suffix: costMeasure === 'candidateSuite' ? ` per ${options.context.costUnit ?? 'suite run'}` : ' per run, all roles',
      lowerIsBetter: true,
      scale: 'log',
      format: (v) => formatMeasure(v, costKey, numbers[costKey]),
    },
  };

  const axes: ProfileAxis[] = PROFILE_AXIS_ORDER.map((id) => {
    const meta = axisMeta[id];
    const values = plotted.map((e) => rawFor(id, e));
    const finite = values.filter((v) => Number.isFinite(v));
    const min = finite.length > 0 ? Math.min(...finite) : 0;
    const max = finite.length > 0 ? Math.max(...finite) : 0;
    let domain: [number, number];
    if (meta.scale === 'log') {
      domain = profileRatioDomain(values);
    } else if (id === 'quality') {
      const halfWidth = (e: ModelComparisonEntry): number => drawnWhisker(e.intelligenceIndexCi95HalfWidth);
      domain = profileIndexDomain(
        plotted.map((e) => e.intelligenceIndex - halfWidth(e)),
        plotted.map((e) => e.intelligenceIndex + halfWidth(e)),
      );
    } else {
      domain = profileIndexDomain(values, values);
    }
    return {
      id,
      title: meta.title,
      min,
      max,
      minLabel: meta.format(min),
      maxLabel: meta.format(max),
      lowerIsBetter: meta.lowerIsBetter,
      scale: meta.scale,
      domainMin: domain[0],
      domainMax: domain[1],
      noun: meta.noun,
      suffix: meta.suffix,
    };
  });

  const rows: ProfileRow[] = plotted.map((entry) => {
    const raw = PROFILE_AXIS_ORDER.map((id) => rawFor(id, entry));
    return { key: entry.key, label: entry.label, values: axes.map((axis, i) => profileHeight(axis, raw[i])), raw };
  });

  const formats = PROFILE_AXIS_ORDER.map((id) => axisMeta[id].format);
  const descriptions: ProfileDescription[] = rows.map((row) => {
    const parts: string[] = [];
    axes.forEach((axis, i) => {
      if (Number.isFinite(row.raw[i])) {
        parts.push(`${axis.noun} ${formats[i](row.raw[i])}${axis.suffix}`);
      }
    });
    let weakest = -1;
    row.values.forEach((value, i) => {
      if (Number.isFinite(value) && (weakest < 0 || value < row.values[weakest])) {
        weakest = i;
      }
    });
    const tail = weakest < 0 ? '' : `; weakest axis: ${axes[weakest].noun}`;
    return { key: row.key, text: `${row.label}: ${parts.join(', ')}${tail}` };
  });

  return { axes, rows, descriptions, formats };
}

/** The tile grid's column count for `n` tiles: one row up to three, then two, three or four columns. */
export function profileColumns(n: number): number {
  if (n <= 3) {
    return Math.max(1, n);
  }
  return n <= 4 ? 2 : n <= 6 ? 3 : 4;
}

/** The label of every tile's dotted line at y = 1, and the key item it is named by. */
export const PROFILE_IDEAL_LABEL = 'Ideal';

/** The tile's title band and the room above it for the printed values, in pixels. */
const PROFILE_TILE_TITLE_BAND = 20;
const PROFILE_TILE_VALUE_ROOM = 16;

/** The model's own markers, and the surface ring around them. */
const PROFILE_MARKER_RADIUS = 4;
const PROFILE_MARKER_RING = 2;

/** What the profile-tile plugin reads off a tile's options. */
export interface ProfileTilePluginOptions {
  /** The model's display name, in the ink. */
  readonly title: string;
  /** The provider hue of the dot before the title. */
  readonly dotColor: string;
  /** The model's printed value at each axis, or null where it has none. */
  readonly values: readonly (string | null)[];
  /** Printed beside the dotted line at y = 1; set on the first tile only. */
  readonly idealLabel?: string;
  /** A Highlight-emphasised model's tile is outlined in this color. */
  readonly outlineColor?: string;
  readonly inkColor: string;
  readonly valueColor: string;
  readonly mutedColor: string;
  readonly fontFamily: string;
  readonly titleWeight: FigureFontWeight;
  readonly fontSizePx: number;
}

/** The title ellipsized to `width` pixels in the context's current font. */
function fitText(ctx: CanvasRenderingContext2D, text: string, width: number): string {
  if (ctx.measureText(text).width <= width) {
    return text;
  }
  let fitted = text;
  while (fitted.length > 1 && ctx.measureText(`${fitted}…`).width > width) {
    fitted = fitted.slice(0, -1);
  }
  return `${fitted}…`;
}

/**
 * Draws a profile tile's own text: the title with its provider dot, the model's values on its three
 * points (above the interval where the point carries one), the *Ideal* label in the first tile, and
 * the gold outline of an emphasised model. Text never wears the series color.
 */
export const profileTilePlugin: Plugin = {
  id: 'overseerProfileTile',
  afterDatasetsDraw(chart, _args, pluginOptions): void {
    const options = pluginOptions as unknown as ProfileTilePluginOptions | undefined;
    const ctx = chart.ctx;
    const area = chart.chartArea;
    if (!ctx || !area || !options || typeof options.title !== 'string') {
      return;
    }
    const size = options.fontSizePx;
    ctx.save();

    // The title band: the provider dot, then the name in the ink.
    const bandMiddle = PROFILE_TILE_TITLE_BAND / 2;
    ctx.fillStyle = options.dotColor;
    ctx.beginPath();
    ctx.arc(8, bandMiddle, 4, 0, Math.PI * 2);
    ctx.fill();
    ctx.font = `${options.titleWeight} ${size + 1}px ${options.fontFamily}`;
    ctx.textAlign = 'left';
    ctx.textBaseline = 'middle';
    ctx.fillStyle = options.inkColor;
    ctx.fillText(fitText(ctx, options.title, Math.max(0, chart.width - 24)), 18, bandMiddle);

    const yScale = chart.scales['y'];
    // The model's own values, above each point and above its whisker's cap.
    const meta = chart.getDatasetMeta(0);
    ctx.font = `${size}px ${options.fontFamily}`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    ctx.fillStyle = options.valueColor;
    meta.data.forEach((element, index) => {
      const text = options.values[index];
      if (!text || !Number.isFinite(element.x) || !Number.isFinite(element.y)) {
        return;
      }
      let top = element.y - PROFILE_MARKER_RADIUS - PROFILE_MARKER_RING - 2;
      const raw = chart.data.datasets[0]?.data[index];
      if (yScale && isErrorBarPoint(raw) && raw.yErrHigh !== undefined) {
        const whiskerTop = yScale.getPixelForValue(raw.y + raw.yErrHigh) - 3;
        if (Number.isFinite(whiskerTop)) {
          top = Math.min(top, whiskerTop);
        }
      }
      ctx.fillText(text, element.x, Math.max(top, PROFILE_TILE_TITLE_BAND + size + 2));
    });

    if (options.idealLabel && yScale) {
      ctx.textAlign = 'left';
      ctx.textBaseline = 'bottom';
      ctx.fillStyle = options.mutedColor;
      ctx.fillText(options.idealLabel, area.left + 2, yScale.getPixelForValue(1) - 3);
    }

    if (options.outlineColor) {
      ctx.strokeStyle = options.outlineColor;
      ctx.lineWidth = 2;
      ctx.strokeRect(1, 1, chart.width - 2, chart.height - 2);
    }
    ctx.restore();
  },
};

export interface ProfileOptions extends FigureOptions {
  readonly speedMeasure: SpeedMeasure;
  readonly costMeasure: CostMeasure;
}

/** A profile tile's datum: the model's own points carry their interval; the context lines are plain heights. */
export type ProfileDatum = number | null | ErrorBarPoint;

/** One tile: a line chart over the three axes, with a category label per axis. */
export type ProfileTileConfig = ChartConfiguration<'line', ProfileDatum[], string | string[]>;

/** P2 as drawn: one tile per plotted model, laid out `columns` wide, and the chrome around the grid. */
export interface ProfileFigure {
  readonly id: string;
  readonly title: string;
  readonly chrome: FigureChrome;
  /** One line chart per plotted model, in entry order. */
  readonly tiles: readonly ProfileTileConfig[];
  /** The plugins every tile draws with; each tile's own options carry what differs. */
  readonly plugins: Plugin[];
  readonly columns: number;
  /** One sentence per model for the figure's description. */
  readonly descriptions: readonly ProfileDescription[];
}

/** The better end of an axis across the plotted set, or NaN with no value. */
function bestRaw(axis: ProfileAxis, rows: readonly ProfileRow[], index: number): number {
  const finite = rows.map((row) => row.raw[index]).filter((v) => Number.isFinite(v));
  if (finite.length === 0) {
    return Number.NaN;
  }
  return axis.lowerIsBetter ? Math.min(...finite) : Math.max(...finite);
}

/**
 * P2 — model profiles as small multiples: one tile per model, its three-axis profile in its
 * provider's hue over every other model in thin gray, on scales every tile shares, under a dotted
 * *Ideal* line at the better end of every axis. Each tile's dip away from that line is what the
 * model gives up; no model is best everywhere is the point the figure shows.
 *
 * The model's own values are printed on its points and its intelligence carries its 95 % interval.
 */
export function buildProfilePlot(
  plotted: readonly ModelComparisonEntry[],
  options: ProfileOptions,
): ProfileFigure {
  const { context, reducedMotion, highlightedKey, selectedKeys } = options;
  const theme = themeOf(options);
  const normalization = prepareProfile(plotted, {
    context,
    speedMeasure: options.speedMeasure,
    costMeasure: options.costMeasure,
    numbers: (options.style ?? DEFAULT_FIGURE_STYLE).numbers,
  });
  const { axes, rows, formats } = normalization;

  const emphasised = new Set<string>(selectedKeys ?? []);
  if (highlightedKey) {
    emphasised.add(highlightedKey);
  }

  const qualityAxis = axes[0];
  const qualitySpan = qualityAxis.domainMax - qualityAxis.domainMin;
  const axisLabels: (string | string[])[] = axes.map((axis) => {
    const split = splitAxisTitle(axis.title);
    return split ? [...split] : axis.title;
  });
  const fontFamily = theme.fonts.chartStack ?? LATO_STACK;
  const tickSize = 10;

  const tiles: ProfileTileConfig[] = plotted.map((entry, tileIndex) => {
    const row = rows[tileIndex];
    const hue = providerHue(entry.provider, theme);
    const halfWidth = drawnWhisker(entry.intelligenceIndexCi95HalfWidth);
    const ownPoints: ErrorBarPoint[] = row.values.map((value, i) => ({
      x: i,
      y: value,
      ...(i === 0 && halfWidth > 0 && qualitySpan > 0
        ? { yErrLow: halfWidth / qualitySpan, yErrHigh: halfWidth / qualitySpan }
        : {}),
    }));
    const own = {
      label: row.label,
      data: ownPoints,
      borderColor: hue,
      backgroundColor: hue,
      borderWidth: 2.5,
      pointRadius: PROFILE_MARKER_RADIUS,
      pointHoverRadius: PROFILE_MARKER_RADIUS + 1,
      pointHitRadius: POINT_HIT_RADIUS,
      pointBackgroundColor: hue,
      pointBorderColor: theme.chart.surface,
      pointBorderWidth: PROFILE_MARKER_RING,
      borderCapStyle: 'round' as const,
      borderJoinStyle: 'round' as const,
      fill: false,
      tension: 0,
      order: 0,
    };
    const ideal = {
      label: PROFILE_IDEAL_LABEL,
      data: axes.map(() => 1),
      borderColor: theme.chart.accent,
      backgroundColor: theme.chart.accent,
      borderWidth: 2,
      borderDash: [...FRONTIER_DASH],
      borderCapStyle: 'round' as const,
      pointRadius: 0,
      pointHoverRadius: 0,
      pointHitRadius: 0,
      fill: false,
      tension: 0,
      order: 1,
    };
    const others = rows
      .filter((other) => other.key !== row.key)
      .map((other) => ({
        label: other.label,
        data: other.values.map((value): number | null => (Number.isFinite(value) ? value : null)),
        borderColor: theme.chart.deEmphasisStroke,
        backgroundColor: theme.chart.deEmphasisStroke,
        borderWidth: 1,
        pointRadius: 0,
        pointHoverRadius: 0,
        pointHitRadius: 0,
        fill: false,
        tension: 0,
        order: 2,
      }));

    const tileOptions: ProfileTilePluginOptions = {
      title: entry.label,
      dotColor: hue,
      values: row.raw.map((value, i) => (Number.isFinite(value) ? formats[i](value) : null)),
      ...(tileIndex === 0 ? { idealLabel: PROFILE_IDEAL_LABEL } : {}),
      ...(emphasised.has(entry.key) ? { outlineColor: theme.chart.accent } : {}),
      inkColor: theme.chart.inkPrimary,
      valueColor: theme.chart.inkSecondary,
      mutedColor: theme.chart.inkMuted,
      fontFamily,
      titleWeight: theme.fonts.headingWeight,
      fontSizePx: 11,
    };

    const datasets = [own, ideal, ...others];
    const config: ProfileTileConfig = {
      type: 'line',
      data: { labels: axisLabels, datasets },
      options: {
        ...baseOptions(reducedMotion),
        layout: {
          padding: { top: PROFILE_TILE_TITLE_BAND + PROFILE_TILE_VALUE_ROOM, right: 6, bottom: 2, left: 6 },
        },
        scales: {
          x: {
            type: 'category',
            // Half a category of room at each end, so the end points and their values are not cut.
            offset: true,
            // One vertical rule through each axis, not between them.
            grid: { ...gridOptions(true, theme), offset: false },
            border: { display: false },
            ticks: { ...tickOptions(tickSize, theme), color: theme.chart.inkSecondary, maxRotation: 0, autoSkip: false },
          },
          y: {
            type: 'linear',
            // A little past 0 and 1, so a marker at either end is drawn whole.
            min: -0.08,
            max: 1.08,
            display: false,
          },
        },
        plugins: {
          legend: { display: false },
          tooltip: {
            ...TOOLTIP_STYLE,
            filter: (item) => item.datasetIndex === 0,
            callbacks: {
              // Real values, not the heights: the plot is a shape, the tooltip is the record.
              label: (item) => {
                const axis = axes[item.dataIndex];
                const format = formats[item.dataIndex];
                const value = row.raw[item.dataIndex];
                if (!axis || !format || !Number.isFinite(value)) {
                  return '';
                }
                return `${row.label} — ${axis.title}: ${format(value)}`;
              },
            },
          },
          datalabels: { display: false },
          ...errorBarOptions(theme),
          ...({ [profileTilePlugin.id]: tileOptions }),
        },
      },
    };
    return config;
  });

  // The profile's highlight: the model that is best on every axis, which is rarely any.
  const best = axes.map((axis, i) => bestRaw(axis, rows, i));
  const bestEverywhere = rows.find((row) =>
    row.raw.every((value, i) => Number.isFinite(value) && Number.isFinite(best[i]) && value === best[i]));
  const highlight = rows.length === 0
    ? ''
    : bestEverywhere
      ? `${bestEverywhere.label} is best on every axis.`
      : 'No model is best on every axis.';

  const key: FigureKeyItem[] = [
    { glyph: 'ideal', text: 'Ideal: best on every axis', color: theme.chart.accent },
    ...(rows.length > 1 ? [{ glyph: 'other', text: 'Other models' } satisfies FigureKeyItem] : []),
    ...(plotted.some((entry) => drawnWhisker(entry.intelligenceIndexCi95HalfWidth) > 0)
      ? [{ glyph: 'interval', text: '95 % interval' } satisfies FigureKeyItem]
      : []),
    ...providerKeyItems(plotted, theme),
  ];

  const logNote = options.speedMeasure === 'speedIndex'
    ? 'Cost uses a log scale: equal steps are equal ratios.'
    : 'Speed and cost use log scales: equal steps are equal ratios.';
  const ranges = axes
    .filter((axis) => rows.some((row) => Number.isFinite(row.raw[PROFILE_AXIS_ORDER.indexOf(axis.id)])))
    .map((axis, i) => {
      const noun = i === 0 ? axis.noun.charAt(0).toUpperCase() + axis.noun.slice(1) : axis.noun;
      return axis.minLabel === axis.maxLabel
        ? `${noun} ${axis.minLabel}${axis.suffix}`
        : `${noun} ${axis.minLabel}–${axis.maxLabel}${axis.suffix}`;
    })
    .join(' · ');

  const title = 'Model profiles';
  const chrome: FigureChrome = {
    title,
    badges: visibleBadges(
      [...countBadges(plotted, context), pricingBadge(context.pricingBasis, context.pricedOn)],
      (options.style ?? DEFAULT_FIGURE_STYLE).profile.hiddenBadges,
    ),
    detail: pricingNote(context.pricingBasis),
    key,
    highlight,
    notes: [
      ...warningNotes(degradedNotices(plotted, ['speed', 'cost'])),
      { text: logNote, tone: 'info' },
      ...(ranges === '' ? [] : [{ text: ranges, tone: 'info' } satisfies FigureNote]),
    ],
  };

  return {
    id: 'p2-profile',
    title,
    chrome,
    tiles,
    plugins: [errorBarPlugin, profileTilePlugin],
    columns: profileColumns(tiles.length),
    descriptions: normalization.descriptions,
  };
}

// ---------------------------------------------------------------------------------------------
// The whole figure set
// ---------------------------------------------------------------------------------------------

export interface ComparisonFigureSet {
  readonly selection: PlottedSelection;
  readonly glyphs: ReadonlyMap<string, IdentityGlyph>;
  readonly qualitySpeed: ChartSpec<'scatter', ErrorBarPoint[]>;
  readonly qualityCost: ChartSpec<'scatter', ErrorBarPoint[]>;
  readonly speedCost: ChartSpec<'scatter', ErrorBarPoint[]>;
  readonly smallMultiples: SmallMultiplesFigure;
  readonly profile: ProfileFigure;
}

export interface FigureSetOptions {
  readonly context: ModelComparisonContext;
  readonly sort?: ModelSort;
  readonly speedMeasure?: SpeedMeasure;
  readonly costMeasure?: CostMeasure;
  readonly orientation?: BarOrientation;
  readonly reducedMotion?: boolean;
  /** Names every scatter mark on the canvas and hides the legend. Off unless the wizard asks. */
  readonly directLabels?: boolean;
  /** Draws each scatter mark's two measured values on the canvas, beside it. Off unless the caller asks. */
  readonly inlineValues?: boolean;
  readonly highlightedKey?: string | null;
  readonly selectedKeys?: readonly string[];
  /** Bar and trade-off styling. Defaults to {@link DEFAULT_FIGURE_STYLE}. */
  readonly style?: FigureStyle;
  /** Colours and fonts. Absent: resolved from `style.appearance`, so no style and no theme draw dark. */
  readonly theme?: ResolvedFigureTheme;
  /**
   * The complete, unfiltered set the glyphs were assigned from. Pass it whenever the caller draws a
   * subset, so filtering a model out never repaints the survivors.
   */
  readonly glyphSource?: readonly ModelComparisonEntry[];
}

/** Builds every figure from one entry set, one order and one glyph assignment. */
export function buildComparisonFigures(
  entries: readonly ModelComparisonEntry[],
  options: FigureSetOptions,
): ComparisonFigureSet {
  const context = options.context;
  const sort = options.sort ?? DEFAULT_MODEL_SORT;
  const speedMeasure = options.speedMeasure ?? 'meanModelTime';
  const costMeasure = options.costMeasure ?? 'candidateSuite';
  const orientation = options.orientation ?? 'vertical';
  const reducedMotion = options.reducedMotion ?? false;
  const theme = options.theme ?? resolveFigureTheme(options.style?.appearance);

  const glyphs = buildIdentityGlyphs(options.glyphSource ?? entries, theme.chart.categorical, theme.chart.deEmphasisStroke);
  const selection = selectPlottedEntries(entries, sort, speedMeasure, costMeasure, context);

  const figureOptions: FigureOptions = {
    context,
    glyphs,
    reducedMotion,
    highlightedKey: options.highlightedKey ?? null,
    selectedKeys: options.selectedKeys ?? [],
    speedMeasure,
    directLabels: options.directLabels ?? false,
    inlineValues: options.inlineValues ?? false,
    style: options.style ?? DEFAULT_FIGURE_STYLE,
    theme,
  };
  const smallMultiplesOptions: SmallMultiplesOptions = {
    ...figureOptions,
    speedMeasure,
    costMeasure,
    orientation,
  };

  return {
    selection,
    glyphs,
    qualitySpeed: buildQualitySpeedScatter(selection.plotted, figureOptions),
    qualityCost: buildQualityCostScatter(selection.plotted, figureOptions),
    speedCost: buildSpeedCostScatter(selection.plotted, figureOptions),
    smallMultiples: buildSmallMultiples(selection.plotted, smallMultiplesOptions),
    profile: buildProfilePlot(selection.plotted, { ...figureOptions, speedMeasure, costMeasure }),
  };
}

// ---------------------------------------------------------------------------------------------
// Number format samples
// ---------------------------------------------------------------------------------------------

export interface NumberSampleOptions {
  readonly context: ModelComparisonContext;
  readonly speedMeasure: SpeedMeasure;
  readonly costMeasure: CostMeasure;
  /** The style the figures were built with: the scatter's time unit depends on its intervals. */
  readonly style?: FigureStyle;
}

/** The first finite value in plotted order, or undefined where no plotted entry has one. */
function firstFinite(
  plotted: readonly ModelComparisonEntry[],
  value: (entry: ModelComparisonEntry) => number | null,
): number | undefined {
  for (const entry of plotted) {
    const v = value(entry);
    if (v !== null && Number.isFinite(v)) {
      return v;
    }
  }
  return undefined;
}

/**
 * What the Number format options of one figure family preview on: the first plotted value of each
 * of its three measures, with the time unit that family writes it in. A measure no plotted entry
 * has is left out, and the panel previews it on a fixed example instead.
 */
export function buildNumberSamples(
  plotted: readonly ModelComparisonEntry[],
  options: NumberSampleOptions,
  family: 'bar' | 'scatter' | 'profile',
): NumberSamples {
  const { context, speedMeasure, costMeasure } = options;
  const style = options.style ?? DEFAULT_FIGURE_STYLE;
  const speedKey = speedNumberMeasure(speedMeasure);
  const samples: Partial<Record<NumberMeasure, { value: number; unit?: TimeUnit }>> = {};

  const quality = firstFinite(plotted, (e) => e.intelligenceIndex);
  if (quality !== undefined) {
    samples.intelligenceIndex = { value: quality };
  }

  const speed = firstFinite(plotted, (e) => speedValue(e, speedMeasure));
  if (speed !== undefined) {
    const unit = speedMeasure === 'speedIndex'
      ? undefined
      : family === 'bar'
        ? barSpeedUnit(plotted, speedMeasure)
        : family === 'scatter'
          ? resolveAxis(speedAxisFor(speedMeasure), plotted, scatterAxisStyle(style.scatter)).unit
          : profileTimeUnit(plotted, { context, speedMeasure, costMeasure });
    samples[speedKey] = unit === undefined ? { value: speed } : { value: speed, unit };
  }

  const cost = family === 'scatter'
    ? firstFinite(plotted, (e) => e.candidateCostPerQuestionUsd)
    : firstFinite(plotted, (e) => costValue(e, costMeasure, context));
  if (cost !== undefined) {
    samples[family === 'scatter' ? 'costPerQuestion' : costNumberMeasure(costMeasure)] = { value: cost };
  }
  return samples;
}
