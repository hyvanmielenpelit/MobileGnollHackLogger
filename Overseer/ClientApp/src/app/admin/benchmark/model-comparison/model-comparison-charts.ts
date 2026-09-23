/**
 * Chart core for the cross-model benchmark comparison view: the palette, the per-model identity
 * glyphs, the measure definitions, the Pareto-frontier computation, the error-bar and
 * dominated-region plugins and the configurations for the six figures (S1-S3 scatters, P1's three linked panels, P2's profile plot).
 *
 * The module is pure TypeScript: it constructs no components, touches no DOM node and imports
 * nothing from Angular, so its spec runs without a TestBed fixture. The one browser API it reaches
 * for is `matchMedia`, behind a guard and behind an injectable factory, so the reduced-motion
 * branch is testable without a real media query.
 */

import ChartDataLabels from 'chartjs-plugin-datalabels';
import type { Chart, ChartConfiguration, ChartType, DefaultDataPoint, Plugin, Point } from 'chart.js';
import { chooseScaleType, formatTick, linearDomain, logDomain, timeUnitFor } from './axis-domain';
import type { AxisBounds, AxisTickKind, ScaleType, TimeUnit } from './axis-domain';
import { pricingBadge, pricingNote, runsBadge } from './figure-chrome';
import type { FigureBadge, FigureChrome, FigureDirection, FigureKeyItem, FigureNote } from './figure-chrome';

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
  /** Display name, used on axes, in legends and in direct labels. */
  readonly label: string;
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

/** Facts shared by every entry in a comparable set, used for figure chrome and the suite-cost rescale. */
export interface ModelComparisonContext {
  /** Items per run. Constant across a comparable set, because every Fundamental key must match. */
  readonly itemsPerRun: number;
  /** The pricing basis and its date, as the view's header, methods block and table name it. */
  readonly pricingBasisLabel: string;
  /** The pricing basis key: `'Current'`, `'AsRun'` or `''`. Drives the pricing badge and note on cost figures. */
  readonly pricingBasis: string;
  /** ISO timestamp the comparison was priced on (the DTO's `computedAtUtc`), or `''`. */
  readonly pricedOn: string;
  /** Suite name, for figure titles. */
  readonly suiteName: string;
}

// ---------------------------------------------------------------------------------------------
// Palette
// ---------------------------------------------------------------------------------------------

/**
 * The composited chart surface. Overseer is dark-only - `styles.scss` sets a white-on-black body
 * with no `prefers-color-scheme` handling - and the panels these charts sit in are `--bg-glass`,
 * `rgba(17, 17, 17, 0.65)`, over the page. Over black that composites to `#0b0b0b`
 * (17 x 0.65 = 11.05 -> 0x0b), which is the surface every contrast figure below is measured against.
 * There is one palette, selected for this surface, not a light one flipped.
 */
export const CHART_SURFACE = '#0b0b0b';

/**
 * The categorical series hues, in fixed order, assigned in sequence and never cycled past three.
 *
 * Three, not eight, because every figure that uses hue for identity here is an all-pairs form -
 * a scatter, where any two marks can end up side by side - and an all-pairs palette caps at three
 * slots. The eight models are separated instead by a hue x shape composite (see
 * {@link IDENTITY_SHAPES}), which is the documented treatment past the three-hue ceiling.
 *
 * Validate with, from the dataviz skill's base directory:
 *   node scripts/validate_palette.js "#3987e5,#d95926,#199e70" --mode dark --surface "#0b0b0b"
 */
export const CATEGORICAL_PALETTE_DARK = ['#3987e5', '#d95926', '#199e70'] as const;

/** The exact string the palette validator takes, so the check is a copy-paste and never a retype. */
export const PALETTE_VALIDATION_INPUT: string = CATEGORICAL_PALETTE_DARK.join(',');

/**
 * The emphasis accent: Overseer's own `--gh-gold`. It is an emphasis token, not a series identity
 * colour - it marks whichever model is hovered or selected and never carries identity on its own -
 * so it stays out of the categorical set the validator gates. Contrast on `#0b0b0b` is ~10.7:1.
 */
export const ACCENT = '#e0ba6d';

/** Chart chrome and ink. Text always wears these, never a series hue. */
export const CHART_INK = {
  primary: '#ffffff',
  secondary: '#c3c2b7',
  muted: '#898781',
  gridline: '#2c2c2a',
  baseline: '#383835',
} as const;

/**
 * De-emphasis for marks that are not the emphasised one: the muted ink at 45 % over the surface.
 * It reuses a documented ink rather than introducing an undocumented gray, and the opacity keeps it
 * well below the axis labels drawn in the same hue at full strength.
 */
export const DE_EMPHASIS_FILL = 'rgba(137, 135, 129, 0.45)';
export const DE_EMPHASIS_STROKE = '#898781';

/** Tooltip chrome, matching the surrounding admin views. */
const TOOLTIP_STYLE = {
  backgroundColor: 'rgba(22, 22, 22, 0.95)',
  titleColor: CHART_INK.primary,
  bodyColor: CHART_INK.secondary,
  borderColor: 'rgba(224, 186, 109, 0.4)',
  borderWidth: 1,
  padding: 10,
  cornerRadius: 6,
} as const;

// ---------------------------------------------------------------------------------------------
// Identity glyphs
// ---------------------------------------------------------------------------------------------

/** Chart.js point styles used as the shape half of the identity glyph. */
export const IDENTITY_SHAPES = ['circle', 'rectRot', 'triangle'] as const;
export type IdentityShape = (typeof IDENTITY_SHAPES)[number];

/** A model's identity: the same hue and shape in every figure that uses glyphs, and in the table. */
export interface IdentityGlyph {
  readonly hue: string;
  readonly shape: IdentityShape;
}

/** The hard ceiling on plotted entries. Three hues x three shapes yields nine distinct glyphs. */
export const MAX_PLOTTED_ENTRIES = 8;

/**
 * Assigns each plottable entry its identity glyph, in the order the service returned them.
 *
 * The order deliberately does not depend on the current sort or the current filter selection, so a
 * reader who has learned a model's glyph keeps it: filtering a model out never repaints the
 * survivors. Excluded entries take no glyph - they are never plotted, and their table row carries
 * the differing comparability keys instead.
 */
export function buildIdentityGlyphs(entries: readonly ModelComparisonEntry[]): Map<string, IdentityGlyph> {
  const glyphs = new Map<string, IdentityGlyph>();
  let slot = 0;
  for (const entry of entries) {
    if (entry.excluded) {
      continue;
    }
    glyphs.set(entry.key, {
      hue: CATEGORICAL_PALETTE_DARK[slot % CATEGORICAL_PALETTE_DARK.length],
      shape: IDENTITY_SHAPES[Math.floor(slot / CATEGORICAL_PALETTE_DARK.length) % IDENTITY_SHAPES.length],
    });
    slot += 1;
  }
  return glyphs;
}

const FALLBACK_GLYPH: IdentityGlyph = { hue: CATEGORICAL_PALETTE_DARK[0], shape: IDENTITY_SHAPES[0] };

/** Looks a glyph up by entry key, falling back to slot one rather than throwing inside a render. */
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

export type ModelSortKey = 'intelligenceIndex' | 'speed' | 'cost' | 'label';
export type SortDirection = 'asc' | 'desc';

export interface ModelSort {
  readonly key: ModelSortKey;
  readonly direction: SortDirection;
}

/** One order control drives all three P1 panels; this is where it starts. */
export const DEFAULT_MODEL_SORT: ModelSort = { key: 'intelligenceIndex', direction: 'desc' };

/** Candidate cost of the whole question suite: per-question cost times the shared item count. */
export function suiteCostUsd(entry: ModelComparisonEntry, context: ModelComparisonContext): number {
  return entry.candidateCostPerQuestionUsd * context.itemsPerRun;
}

function suiteCostSdUsd(entry: ModelComparisonEntry, context: ModelComparisonContext): number | null {
  return entry.candidateCostPerQuestionSdUsd === null
    ? null
    : entry.candidateCostPerQuestionSdUsd * context.itemsPerRun;
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
  context: ModelComparisonContext,
): number {
  return measure === 'candidateSuite' ? suiteCostUsd(entry, context) : entry.totalRunCostUsd;
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
 */
export function sortEntriesForComparison(
  entries: readonly ModelComparisonEntry[],
  sort: ModelSort,
  speedMeasure: SpeedMeasure,
  costMeasure: CostMeasure,
  context: ModelComparisonContext,
): ModelComparisonEntry[] {
  const sign = sort.direction === 'asc' ? 1 : -1;
  const ordered = [...entries];
  ordered.sort((a, b) => {
    let delta = 0;
    switch (sort.key) {
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

/**
 * Draws the uncertainty every mark in this view is required to carry. Chart.js 4 has no error bars
 * and adding a dependency for a few strokes is not worth the bundle, so this lives here, where the
 * module's own spec covers it.
 */
export const errorBarPlugin: Plugin = {
  id: 'overseerErrorBars',
  afterDatasetsDraw(chart): void {
    const ctx = chart.ctx;
    const area = chart.chartArea;
    if (!ctx || !area) {
      return;
    }
    ctx.save();
    ctx.strokeStyle = CHART_INK.muted;
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
  /** The staircase bounding the dominated region, ready to draw as a polyline. */
  readonly steps: readonly PlotPoint[];
  /**
   * The staircase extended to the axis ends: from the worst-x edge at the first member's y, through
   * every step, down to the worst-y edge at the last member's x. Empty without bounds or members.
   */
  readonly boundary: readonly PlotPoint[];
}

/** The axis-domain ends on the unfavourable side of each axis. */
export interface ParetoBounds {
  readonly xWorst: number;
  readonly yWorst: number;
}

/**
 * The non-dominated set and its step function.
 *
 * This replaces the trend line a scatter of eight heterogeneous models invites. A regression over
 * eight points of different families is a statistical claim the data cannot support; the frontier is
 * a decision-analytic device that only says which models nothing else beats on both axes.
 */
export function computeParetoFrontier(
  candidates: readonly ParetoCandidate[],
  xBetter: BetterDirection,
  yBetter: BetterDirection,
  bounds?: ParetoBounds,
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

  // Along the frontier v falls as u rises, so ordering by u gives the staircase its reading order.
  const ordered = [...frontier].sort((a, b) => (u(a) - u(b)) || a.key.localeCompare(b.key));

  const steps: PlotPoint[] = [];
  const push = (point: PlotPoint): void => {
    const last = steps[steps.length - 1];
    if (!last || last.x !== point.x || last.y !== point.y) {
      steps.push(point);
    }
  };
  for (let i = 0; i < ordered.length; i += 1) {
    const current = ordered[i];
    if (i > 0) {
      // The riser sits at the previous point's x, not the current one's: the staircase may only
      // claim what some model actually achieved, and no model reaches the previous y at the
      // better x. Putting the riser on the other side would draw a corner nothing occupies.
      push({ x: ordered[i - 1].x, y: current.y });
    }
    push({ x: current.x, y: current.y });
  }

  // `ordered` runs from the least favourable x to the most favourable, so the first member closes
  // the region against the worst-x edge and the last against the worst-y edge. A one-member
  // frontier becomes an L through that model.
  const boundary: PlotPoint[] = [];
  if (bounds && ordered.length > 0) {
    const first = ordered[0];
    const last = ordered[ordered.length - 1];
    for (const point of [{ x: bounds.xWorst, y: first.y }, ...steps, { x: last.x, y: bounds.yWorst }]) {
      const previous = boundary[boundary.length - 1];
      if (!previous || previous.x !== point.x || previous.y !== point.y) {
        boundary.push(point);
      }
    }
  }

  return { frontier: ordered, steps, boundary };
}

/** The dominated region's fill: faint enough that marks and gridlines read straight through it. */
export const DOMINATED_REGION_FILL = 'rgba(255, 255, 255, 0.04)';

/**
 * Shades the region every frontier member beats on both axes. The polygon is the frontier dataset's
 * own boundary closed through the worst corner, which is the first vertex's x and the last vertex's
 * y, so the shading always matches the drawn line. It runs before the datasets draw, so every mark
 * and whisker sits on top of it.
 */
export const dominatedRegionPlugin: Plugin = {
  id: 'overseerDominatedRegion',
  beforeDatasetsDraw(chart): void {
    const ctx = chart.ctx;
    const area = chart.chartArea;
    if (!ctx || !area) {
      return;
    }
    const index = chart.data.datasets.findIndex((dataset) => dataset.label === PARETO_FRONTIER_LABEL);
    if (index < 0) {
      return;
    }
    const meta = chart.getDatasetMeta(index);
    if (meta.hidden) {
      return;
    }
    const vertices = meta.data
      .filter((element) => Number.isFinite(element.x) && Number.isFinite(element.y))
      .map((element) => ({ x: element.x, y: element.y }));
    if (vertices.length < 2) {
      return;
    }

    ctx.save();
    ctx.beginPath();
    ctx.rect(area.left, area.top, area.right - area.left, area.bottom - area.top);
    ctx.clip();
    ctx.beginPath();
    ctx.moveTo(vertices[0].x, vertices[0].y);
    for (let i = 1; i < vertices.length; i += 1) {
      ctx.lineTo(vertices[i].x, vertices[i].y);
    }
    ctx.lineTo(vertices[0].x, vertices[vertices.length - 1].y);
    ctx.closePath();
    ctx.fillStyle = DOMINATED_REGION_FILL;
    ctx.fill();
    ctx.restore();
  },
};

// ---------------------------------------------------------------------------------------------
// Figure plumbing
// ---------------------------------------------------------------------------------------------

/** Which corner of a scatter is the good one. Rendered as the direction marker, never as a reversed axis. */
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
}

/** Effective hit radius is `radius + hitRadius`, so the target is 36 px across - well over the 24 px floor. */
const POINT_RADIUS = 6;
const POINT_HOVER_RADIUS = 9;
const POINT_HIT_RADIUS = 12;

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

function gridOptions() {
  return { color: CHART_INK.gridline, lineWidth: 1, drawTicks: false };
}

function tickOptions() {
  return { color: CHART_INK.muted, font: { family: '"Lato", system-ui, sans-serif', size: 11 } };
}

/** Draws the axis label on its own line and, when given a direction, a "lower/higher is better" second line. */
function axisTitle(text: string | string[], better?: BetterDirection) {
  return {
    display: true,
    text: better
      ? [...(Array.isArray(text) ? text : [text]), better === 'lower' ? 'lower is better' : 'higher is better']
      : text,
    color: CHART_INK.secondary,
    font: { size: 12 },
  };
}

/** The badges every figure opens with: how many models, how many runs behind each, how many questions. */
function countBadges(plotted: readonly ModelComparisonEntry[], context: ModelComparisonContext): FigureBadge[] {
  return [
    { text: `${plotted.length} ${plotted.length === 1 ? 'model' : 'models'}`, tone: 'neutral' },
    runsBadge(plotted),
    { text: `${context.itemsPerRun} ${context.itemsPerRun === 1 ? 'question' : 'questions'}`, tone: 'neutral' },
  ];
}

function warningNotes(texts: readonly string[]): FigureNote[] {
  return texts.map((text): FigureNote => ({ text, tone: 'warning' }));
}

function formatMs(value: number): string {
  return value >= 1000 ? `${(value / 1000).toFixed(2)} s` : `${Math.round(value)} ms`;
}

function formatUsd(value: number): string {
  return `$${value.toFixed(4)}`;
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

/** The label font, matching the axis ticks so a direct label reads as chart chrome. */
const DIRECT_LABEL_FONT = '11px "Lato", system-ui, sans-serif';

/** Rings tried in turn, in pixels out from the mark. Past the last one the fallback applies. */
const DIRECT_LABEL_RINGS = [14, 28, 42] as const;

/** Padding around the label text, so the backing plate does not touch the glyphs. */
const DIRECT_LABEL_PAD_X = 3;
const DIRECT_LABEL_PAD_Y = 2;

/** Line box of one label at {@link DIRECT_LABEL_FONT}. Measured text carries no height. */
const DIRECT_LABEL_LINE_HEIGHT = 12;

/** How much of the surface the backing plate keeps, so a gridline behind the text stays subdued. */
const DIRECT_LABEL_PLATE_ALPHA = 0.85;

/** The value lines' font: a step under the name so the name stays the plate's headline. */
const DIRECT_LABEL_VALUE_FONT = '10px "Lato", system-ui, sans-serif';

/** Line box of one value line at {@link DIRECT_LABEL_VALUE_FONT}. */
const DIRECT_LABEL_VALUE_LINE_HEIGHT = 12;

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

/** What one mark's plate carries. A block with neither a name nor values draws nothing. */
export interface DirectLabelBlock {
  /** The model's name; absent when the legend names the marks. */
  readonly name?: string;
  readonly values: readonly DirectLabelValue[];
  /** The mark's glyph hue, drawn as the plate's left rule. */
  readonly hue: string;
}

/** What the plugin reads off the chart options: one block per model dataset, in dataset order. */
export interface DirectLabelPluginOptions {
  /** Indexed by dataset. Datasets past the list — the frontier — are never labelled. */
  readonly blocks: readonly DirectLabelBlock[];
  /** The emphasised model, whose plate wears the accent its mark already does. */
  readonly highlightedIndex?: number;
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
): { width: number; height: number; labelColumn: number; valueColumn: number } {
  ctx.font = DIRECT_LABEL_FONT;
  const nameWidth = block.name ? ctx.measureText(block.name).width : 0;

  ctx.font = DIRECT_LABEL_VALUE_FONT;
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
      (block.name ? DIRECT_LABEL_LINE_HEIGHT : 0) +
      block.values.length * DIRECT_LABEL_VALUE_LINE_HEIGHT,
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
 * The frontier staircase as pixel vertices, or nothing when the figure carries no frontier. It sits
 * at the index straight after the labelled model datasets, which is where `buildScatter` pushes it.
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
 * Names every mark on the canvas, on a leader line, with the legend switched off, and carries the
 * mark's own values when the figure asks for them.
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

    ctx.save();
    ctx.textBaseline = 'middle';
    ctx.textAlign = 'left';

    const anchors: DirectLabelAnchor[] = [];
    const obstacleRects: LabelRect[] = [];
    // Keyed by dataset index, so two models sharing a label cannot collide in the placer's map.
    const plates = new Map<string, { block: DirectLabelBlock; labelColumn: number; valueColumn: number }>();
    blocks.forEach((block, datasetIndex) => {
      if (!block.name && block.values.length === 0) {
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
      const metrics = measureDirectLabelBlock(ctx, block);
      anchors.push({ key, x: element.x, y: element.y, width: metrics.width, height: metrics.height });
      plates.set(key, { block, labelColumn: metrics.labelColumn, valueColumn: metrics.valueColumn });
      // The whiskers `errorBarPlugin` has already drawn, so a plate does not land on one.
      const raw = chart.data.datasets[datasetIndex]?.data[0];
      if (isErrorBarPoint(raw)) {
        obstacleRects.push(...whiskerRects(chart, meta, raw, element));
      }
    });

    const boxes = placeDirectLabels(anchors, area, POINT_HOVER_RADIUS, {
      rects: obstacleRects,
      polylines: frontierPolylines(chart, blocks.length),
    });
    const highlighted = options?.highlightedIndex === undefined ? undefined : String(options.highlightedIndex);

    // Leaders first, so a plate covers the end of its own line rather than the line crossing it.
    ctx.strokeStyle = CHART_INK.muted;
    ctx.lineWidth = 1;
    for (const box of boxes) {
      strokeLeader(ctx, box, POINT_HOVER_RADIUS);
    }

    for (const box of boxes) {
      const plate = plates.get(box.key);
      if (!plate) {
        continue;
      }
      const accented = box.key === highlighted;
      // A backing plate, so a label crossing a gridline stays readable without an outline halo.
      ctx.globalAlpha = DIRECT_LABEL_PLATE_ALPHA;
      ctx.fillStyle = CHART_SURFACE;
      ctx.fillRect(box.x, box.y, box.width, box.height);
      ctx.globalAlpha = 1;

      // The hue rule ties the plate to its mark, which in legend mode is its only identity.
      ctx.fillStyle = accented ? ACCENT : plate.block.hue;
      ctx.fillRect(box.x, box.y, DIRECT_LABEL_RULE_WIDTH, box.height);

      const textLeft = box.x + DIRECT_LABEL_RULE_WIDTH + DIRECT_LABEL_RULE_GAP + DIRECT_LABEL_PAD_X;
      const textRight = box.x + box.width - DIRECT_LABEL_PAD_X;
      let lineTop = box.y + DIRECT_LABEL_PAD_Y;

      if (plate.block.name) {
        ctx.font = DIRECT_LABEL_FONT;
        ctx.textAlign = 'left';
        ctx.fillStyle = accented ? ACCENT : CHART_INK.secondary;
        ctx.fillText(plate.block.name, textLeft, lineTop + DIRECT_LABEL_LINE_HEIGHT / 2);
        lineTop += DIRECT_LABEL_LINE_HEIGHT;
      }

      ctx.font = DIRECT_LABEL_VALUE_FONT;
      for (const value of plate.block.values) {
        const middle = lineTop + DIRECT_LABEL_VALUE_LINE_HEIGHT / 2;
        ctx.textAlign = 'left';
        ctx.fillStyle = CHART_INK.muted;
        ctx.fillText(value.label, textLeft, middle);
        ctx.textAlign = 'right';
        ctx.fillStyle = CHART_INK.secondary;
        ctx.fillText(value.text, textRight, middle);
        lineTop += DIRECT_LABEL_VALUE_LINE_HEIGHT;
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
  readonly format: (value: number) => string;
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
  format: (v) => v.toFixed(1),
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
  format: formatMs,
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
  format: formatUsd,
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
  format: (v) => v.toFixed(1),
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
  format: formatMs,
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
  format: formatMs,
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

/**
 * A scatter axis resolved against the plotted entries: its scale type, domain, ticks, unit and title,
 * and the Chart.js scale options that draw them.
 *
 * The result depends only on the entries and the measure, so every scatter plotting the same measure
 * over the same entries gets the same axis. Whiskers count towards the domain, so no interval is
 * clipped. The ticks are set outright in `afterBuildTicks`; `autoSkip` is only a safety net.
 */
function resolveAxis(spec: ScatterAxisSpec, plotted: readonly ModelComparisonEntry[]) {
  const values: number[] = [];
  const lows: number[] = [];
  const highs: number[] = [];
  for (const entry of plotted) {
    const value = spec.value(entry);
    if (!Number.isFinite(value)) {
      continue;
    }
    values.push(value);
    lows.push(value - drawnWhisker(spec.errLow(entry)));
    highs.push(value + drawnWhisker(spec.errHigh(entry)));
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

  return {
    type,
    min,
    max,
    ticks,
    title,
    scale: {
      type,
      min,
      max,
      title: axisTitle(title, spec.better),
      grid: gridOptions(),
      border: { color: CHART_INK.baseline },
      afterBuildTicks: (scale: { ticks: { value: number }[] }): void => {
        scale.ticks = ticks.map((value) => ({ value }));
      },
      ticks: {
        ...tickOptions(),
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
const FRONTIER_UNCERTAINTY_NOTE =
  'Some differences are within the 95 % intervals, so treat the frontier as indicative rather than a clear win.';

/**
 * What one mark's plate carries, from the two toggles the wizard offers.
 *
 * The value lines use the axis's own `format`, so the plate, the tick labels and the tooltip agree
 * to the digit. An unmeasured coordinate — a speed measure a model has no figure for — yields no
 * line at all rather than a printed NaN.
 */
function directLabelBlock(
  entry: ModelComparisonEntry,
  xAxis: ScatterAxisSpec,
  yAxis: ScatterAxisSpec,
  options: { glyphs: ReadonlyMap<string, IdentityGlyph>; named: boolean; valued: boolean },
): DirectLabelBlock {
  const values: DirectLabelValue[] = [];
  if (options.valued) {
    for (const axis of [xAxis, yAxis]) {
      const value = axis.value(entry);
      if (Number.isFinite(value)) {
        values.push({ label: axis.shortLabel, text: axis.format(value) });
      }
    }
  }
  return {
    name: options.named ? entry.label : undefined,
    values,
    hue: glyphFor(options.glyphs, entry.key).hue,
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
  const { context, glyphs, reducedMotion, highlightedKey } = options;
  const directLabels = options.directLabels ?? false;
  const inlineValues = options.inlineValues ?? false;
  // One plugin carries both: names and values share a plate, and so share its placement.
  const annotate = directLabels || inlineValues;

  const datasets = plotted.map((entry) => {
    const glyph = glyphFor(glyphs, entry.key);
    const highlighted = highlightedKey === entry.key;
    // R = 1 draws hollow; R >= 2 draws solid with a surface ring so overlapping marks stay legible.
    const hollow = entry.runCount === 1;
    return {
      label: entry.label,
      data: [scatterPoint(entry, xAxis, yAxis)],
      pointStyle: glyph.shape,
      backgroundColor: hollow ? 'transparent' : glyph.hue,
      borderColor: highlighted ? ACCENT : hollow ? glyph.hue : CHART_SURFACE,
      borderWidth: 2,
      radius: POINT_RADIUS,
      hoverRadius: POINT_HOVER_RADIUS,
      hitRadius: POINT_HIT_RADIUS,
      showLine: false,
    };
  });

  const xResolved = resolveAxis(xAxis, plotted);
  const yResolved = resolveAxis(yAxis, plotted);

  // An unmeasured coordinate draws no mark, so it can neither beat a model nor be beaten by one.
  const measured = plotted.filter((entry) => isMeasured(entry, xAxis, yAxis));
  const pareto = computeParetoFrontier(
    measured.map((e) => ({ key: e.key, x: xAxis.value(e), y: yAxis.value(e) })),
    xAxis.better,
    yAxis.better,
    {
      xWorst: xAxis.better === 'lower' ? xResolved.max : xResolved.min,
      yWorst: yAxis.better === 'higher' ? yResolved.min : yResolved.max,
    },
  );
  // Drawn whenever the frontier has a member: a lone member draws an L to the two worst edges.
  const frontierDrawn = pareto.boundary.length >= 2;
  if (frontierDrawn) {
    datasets.push({
      label: PARETO_FRONTIER_LABEL,
      data: pareto.boundary.map((p) => ({ x: p.x, y: p.y })),
      pointStyle: 'circle',
      backgroundColor: 'transparent',
      borderColor: CHART_INK.secondary,
      borderWidth: 2,
      radius: 0,
      hoverRadius: 0,
      hitRadius: 0,
      showLine: true,
    });
  }

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
          position: 'bottom',
          labels: {
            color: CHART_INK.secondary,
            usePointStyle: true,
            // The frontier is an annotation, not a series, so it stays out of the identity legend.
            filter: (item) => item.text !== PARETO_FRONTIER_LABEL,
          },
        },
        tooltip: {
          ...TOOLTIP_STYLE,
          // The frontier's step vertices sit on a model's own coordinates, so nearest-without-
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
                `${xAxis.tooltipLabel}: ${xAxis.format(x)}`,
                `${yAxis.tooltipLabel}: ${yAxis.format(y)}`,
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
                blocks: plotted.map((entry) => directLabelBlock(entry, xAxis, yAxis, {
                  glyphs,
                  named: directLabels,
                  valued: inlineValues,
                })),
                highlightedIndex: highlightedIndex < 0 ? undefined : highlightedIndex,
              } satisfies DirectLabelPluginOptions,
            }
          : {}),
      },
    },
  };

  const hasCostAxis = xAxis.kind === 'usd' || yAxis.kind === 'usd';
  const badges: FigureBadge[] = [
    ...countBadges(plotted, context),
    ...(hasCostAxis ? [pricingBadge(context.pricingBasis, context.pricedOn)] : []),
  ];

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
  if (measured.some(whiskered)) {
    key.push({ glyph: 'interval', text: '95 % interval' });
  }
  if (frontierDrawn) {
    key.push({ glyph: 'frontier', text: 'Pareto frontier: models nothing beats on both axes' });
    key.push({ glyph: 'dominated', text: 'Shaded: beaten on both axes' });
  }

  const labels = new Map(measured.map((entry): [string, string] => [entry.key, entry.label]));
  const frontierLabels = pareto.frontier.map((member) => labels.get(member.key) ?? member.key);
  const highlight = frontierLabels.length === 0
    ? ''
    : `${frontierLabels.length === 1 ? 'Best trade-off' : 'Best trade-offs'}: ${frontierLabels.join(', ')}`;

  const frontierKeys = new Set(pareto.frontier.map((member) => member.key));
  const frontierEntries = measured.filter((entry) => frontierKeys.has(entry.key));
  const withinIntervals = measured
    .filter((entry) => !frontierKeys.has(entry.key))
    .some((dominated) => frontierEntries.some((member) => intervalsOverlap(dominated, member, [xAxis, yAxis])));

  const notes: FigureNote[] = [
    ...warningNotes(extraNotices),
    ...(withinIntervals ? [{ text: FRONTIER_UNCERTAINTY_NOTE, tone: 'info' } satisfies FigureNote] : []),
  ];

  const chrome: FigureChrome = {
    title,
    badges,
    direction: preferredCorner,
    detail: hasCostAxis ? pricingNote(context.pricingBasis) : '',
    key,
    highlight,
    notes,
  };

  return {
    id,
    title,
    chrome,
    preferredCorner,
    config,
    // The shading goes under everything; a leader line draws over a whisker rather than under it.
    plugins: annotate
      ? [dominatedRegionPlugin, errorBarPlugin, directLabelPlugin]
      : [dominatedRegionPlugin, errorBarPlugin],
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
 * A panel's category tick: the model name, or the name over `n = 1` where one run backs it.
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
  panelHue: string,
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
): ChartSpec<'bar', ErrorBarPoint[], PanelCategoryLabel> {
  const { context, reducedMotion, highlightedKey, selectedKeys, orientation } = options;
  const emphasised = new Set<string>(selectedKeys ?? []);
  if (highlightedKey) {
    emphasised.add(highlightedKey);
  }
  const hasEmphasis = emphasised.size > 0;

  const data = plotted.map((_entry, index) =>
    barPoint(index, values[index] ?? 0, errLows[index], errHighs[index], orientation),
  );

  // A panel is one hue for every bar. Bars are never ramped by value: darker-where-bigger would
  // re-encode the length the bar already shows and fail the categorical checks by construction.
  // Identity here is axis position and label, so the hue is free to mark the measure instead.
  const fills = plotted.map((entry) => {
    if (!hasEmphasis) {
      return entry.runCount === 1 ? 'transparent' : panelHue;
    }
    return emphasised.has(entry.key) ? (entry.runCount === 1 ? 'transparent' : ACCENT) : DE_EMPHASIS_FILL;
  });
  const strokes = plotted.map((entry) => {
    if (!hasEmphasis) {
      return panelHue;
    }
    return emphasised.has(entry.key) ? ACCENT : DE_EMPHASIS_STROKE;
  });

  // Places a value label past the SD whisker rather than on top of it: zero when the entry carries
  // no errHigh, so an unmeasured or interval-free bar's label sits directly past its own end.
  const whiskerLength = (ctx: DataLabelCtx): number => {
    const value = values[ctx.dataIndex];
    const errHigh = errHighs[ctx.dataIndex];
    if (value === null || errHigh === undefined) {
      return 0;
    }
    const scale = ctx.chart.scales[orientation === 'vertical' ? 'y' : 'x'];
    return Math.abs(scale.getPixelForValue(value + errHigh) - scale.getPixelForValue(value));
  };

  const categoryScale = {
    type: 'category' as const,
    title: axisTitle('Model'),
    grid: { display: false },
    border: { color: CHART_INK.baseline },
    ticks: { ...tickOptions(), color: CHART_INK.secondary, maxRotation: orientation === 'vertical' ? 45 : 0 },
  };
  // Bars require a zero baseline, always: a truncated bar axis misstates the ratio that is the
  // entire reason to draw a bar.
  const valueScale = {
    type: 'linear' as const,
    beginAtZero: true,
    min: 0,
    max: axisMax,
    title: axisTitle(axisTitleText, better),
    grid: gridOptions(),
    border: { color: CHART_INK.baseline },
    // The tick decimals follow Chart.js's own step, read off the first two ticks. The unit is the
    // axis title's, so a zero tick reads `0 s` on a seconds axis.
    ticks: {
      ...tickOptions(),
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
          borderWidth: 2,
          borderRadius: 4,
          // Only the far end is rounded; the end sitting on the baseline stays square.
          borderSkipped: orientation === 'vertical' ? 'bottom' : 'left',
          maxBarThickness: 24,
          categoryPercentage: 0.8,
          barPercentage: 0.9,
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
          display: (ctx: DataLabelCtx) => values[ctx.dataIndex] !== null,
          formatter: (_v: unknown, ctx: DataLabelCtx) => format(values[ctx.dataIndex] ?? 0),
          anchor: 'end' as const,
          align: orientation === 'vertical' ? ('top' as const) : ('right' as const),
          clamp: true,
          offset: (ctx: DataLabelCtx) => whiskerLength(ctx) + 4,
          color: CHART_INK.secondary,
          font: { size: 11 },
        },
      },
    },
  };

  const plugins: Plugin[] = [errorBarPlugin, ChartDataLabels as Plugin];

  const chrome: FigureChrome = {
    title,
    badges: [
      ...countBadges(plotted, context),
      ...(costPanel ? [pricingBadge(context.pricingBasis, context.pricedOn)] : []),
    ],
    detail: costPanel ? pricingNote(context.pricingBasis) : '',
    key: [],
    highlight: '',
    notes: [...notes],
  };

  return {
    id,
    title,
    chrome,
    config,
    plugins,
  };
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
  // read as certainty, which is why the category tick says `n = 1` under the model's name instead.
  const costSd = plotted.map((e) =>
    costMeasure === 'candidateSuite' ? suiteCostSdUsd(e, context) ?? undefined : e.totalRunCostSdUsd ?? undefined,
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
  if (speedMeasure === 'meanModelTime') {
    speedNotes.push({
      text: 'Mean time per question has no uncertainty bar: the spread across questions is not recorded.',
      tone: 'info',
    });
  }

  // The unit follows the largest plotted time, and the title and the ticks both carry it.
  const measuredSpeeds = speedValues.filter((v): v is number => v !== null && Number.isFinite(v));
  const speedUnit = timeUnitFor(measuredSpeeds.length > 0 ? Math.max(...measuredSpeeds) : 0);
  const speedTitle =
    speedMeasure === 'speedIndex'
      ? 'Speed Index (0-100)'
      : speedMeasure === 'meanModelTime'
        ? `Mean time per question (${speedUnit})`
        : speedMeasure === 'totalModelTime'
          ? `Total time for the suite (${speedUnit})`
          : `Time to first token, median (${speedUnit})`;
  const costTitle =
    costMeasure === 'candidateSuite'
      ? `Candidate cost for the whole suite (USD, ${context.itemsPerRun} questions)`
      : 'Total run cost including grading roles (USD)';

  // One label list for all three panels. A two-line tick block on one panel alone would shrink
  // that panel's plot area and put its bars out of line with the other two, which share a height
  // and a model order.
  const categoryLabels: PanelCategoryLabel[] = plotted.map((e) =>
    e.runCount === 1 ? [e.label, 'n = 1'] : e.label,
  );

  return {
    quality: buildPanel(
      'p1a-quality',
      'Intelligence',
      plotted,
      categoryLabels,
      CATEGORICAL_PALETTE_DARK[0],
      'Intelligence Index (0-100)',
      'higher',
      100,
      (v) => v.toFixed(0),
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
      CATEGORICAL_PALETTE_DARK[1],
      speedTitle,
      speedLowerIsBetter(speedMeasure) ? 'lower' : 'higher',
      speedMeasure === 'speedIndex' ? 100 : undefined,
      speedMeasure === 'speedIndex' ? (v) => v.toFixed(0) : formatMs,
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
      CATEGORICAL_PALETTE_DARK[2],
      costTitle,
      'lower',
      undefined,
      formatUsd,
      { kind: 'usd' },
      costValues,
      costSd,
      costSd,
      options,
      warningNotes(degradedNotices(plotted, ['cost'])),
      true,
    ),
    order: plotted.map((e) => e.key),
    notices: [],
  };
}

// ---------------------------------------------------------------------------------------------
// P2: the normalized profile plot
// ---------------------------------------------------------------------------------------------

export type ProfileAxisId = 'quality' | 'speed' | 'cost';

/** The axis order is fixed. A reorder control would change which crossings show without changing
 *  the data, which invites reading a pattern that is an artifact of the control. */
export const PROFILE_AXIS_ORDER: readonly ProfileAxisId[] = ['quality', 'speed', 'cost'];

export interface ProfileAxis {
  readonly id: ProfileAxisId;
  readonly title: string;
  /** Real minimum across the plotted set, printed at the axis end so the shape stays anchored. */
  readonly min: number;
  readonly max: number;
  readonly minLabel: string;
  readonly maxLabel: string;
  /** True when the raw measure improves downwards, so normalization inverts it. */
  readonly lowerIsBetter: boolean;
}

export interface ProfileRow {
  readonly key: string;
  readonly label: string;
  /** Normalized 0-1 values in {@link PROFILE_AXIS_ORDER}, oriented so 1 is always better. */
  readonly values: readonly number[];
  /** The real values behind them, for the tooltip. */
  readonly raw: readonly number[];
}

export interface ProfileNormalization {
  readonly axes: readonly ProfileAxis[];
  readonly rows: readonly ProfileRow[];
}

/**
 * Min-max normalizes each axis independently and orients every one so that up is better.
 *
 * Inverting cost - and TTFT, when that is the speed measure - is the one reversed direction this
 * view permits: a normalized axis carries no absolute meaning to invert, and consistent orientation
 * is what makes a crossing read as a trade-off rather than as an axis pointing the other way. The
 * scatters, whose axes carry real units, keep their natural direction.
 */
export function normalizeProfile(
  plotted: readonly ModelComparisonEntry[],
  options: { context: ModelComparisonContext; speedMeasure: SpeedMeasure; costMeasure: CostMeasure },
): ProfileNormalization {
  const { context, speedMeasure, costMeasure } = options;

  const rawFor = (id: ProfileAxisId, entry: ModelComparisonEntry): number => {
    switch (id) {
      case 'quality':
        return entry.intelligenceIndex;
      case 'speed':
        return speedValue(entry, speedMeasure) ?? 0;
      case 'cost':
        return costValue(entry, costMeasure, context);
    }
  };

  const axisMeta: Record<ProfileAxisId, { title: string; lowerIsBetter: boolean; format: (v: number) => string }> = {
    quality: { title: 'Intelligence', lowerIsBetter: false, format: (v) => v.toFixed(1) },
    speed: {
      title: speedMeasure === 'speedIndex'
        ? 'Speed Index'
        : speedMeasure === 'meanModelTime'
          ? 'Speed (mean model time)'
          : speedMeasure === 'totalModelTime'
            ? 'Speed (total model time)'
            : 'Speed (TTFT P50)',
      lowerIsBetter: speedLowerIsBetter(speedMeasure),
      format: speedMeasure === 'speedIndex' ? (v) => v.toFixed(1) : formatMs,
    },
    cost: {
      title: costMeasure === 'candidateSuite' ? 'Cost (candidate, suite)' : 'Cost (total run)',
      lowerIsBetter: true,
      format: formatUsd,
    },
  };

  const axes = PROFILE_AXIS_ORDER.map((id) => {
    const meta = axisMeta[id];
    const values = plotted.map((e) => rawFor(id, e));
    const min = values.length > 0 ? Math.min(...values) : 0;
    const max = values.length > 0 ? Math.max(...values) : 0;
    return {
      id,
      title: meta.title,
      min,
      max,
      minLabel: meta.format(min),
      maxLabel: meta.format(max),
      lowerIsBetter: meta.lowerIsBetter,
    };
  });

  const rows = plotted.map((entry) => {
    const raw = PROFILE_AXIS_ORDER.map((id) => rawFor(id, entry));
    const values = axes.map((axis, i) => {
      const span = axis.max - axis.min;
      // A collapsed axis has no ordering to show, so every model sits mid-axis rather than at an
      // end that would read as "best" or "worst".
      const t = span === 0 ? 0.5 : (raw[i] - axis.min) / span;
      return axis.lowerIsBetter ? 1 - t : t;
    });
    return { key: entry.key, label: entry.label, values, raw };
  });

  return { axes, rows };
}

export interface ProfileOptions extends FigureOptions {
  readonly speedMeasure: SpeedMeasure;
  readonly costMeasure: CostMeasure;
}

/**
 * P2 — one polyline per model across three normalized axes, the companion that shows a trade-off as
 * a crossing rather than as two rankings held in the head.
 *
 * It carries no error bars, deliberately: a normalized axis cannot express an interval honestly, so
 * this is explicitly a shape view and P1 above it remains the figure that carries the units.
 */
export function buildProfilePlot(
  plotted: readonly ModelComparisonEntry[],
  options: ProfileOptions,
): ChartSpec<'line', (number | null)[], string> {
  const { context, glyphs, reducedMotion, highlightedKey, selectedKeys } = options;
  const normalization = normalizeProfile(plotted, options);

  const emphasised = new Set<string>(selectedKeys ?? []);
  if (highlightedKey) {
    emphasised.add(highlightedKey);
  }
  const hasEmphasis = emphasised.size > 0;
  // Eight equally coloured polylines is the failure this form is notorious for. Emphasis is the
  // remedy; categorical hues are only legible here when at most three models are selected.
  const categoricalEmphasis = hasEmphasis && emphasised.size <= CATEGORICAL_PALETTE_DARK.length;

  const datasets = normalization.rows.map((row) => {
    const glyph = glyphFor(glyphs, row.key);
    const isEmphasised = emphasised.has(row.key);
    const colour = !hasEmphasis
      ? DE_EMPHASIS_STROKE
      : isEmphasised
        ? categoricalEmphasis
          ? glyph.hue
          : ACCENT
        : DE_EMPHASIS_STROKE;
    return {
      label: row.label,
      data: [...row.values],
      borderColor: colour,
      backgroundColor: colour,
      borderWidth: isEmphasised ? 3 : 2,
      pointStyle: glyph.shape,
      pointRadius: POINT_RADIUS - 2,
      pointHoverRadius: POINT_HOVER_RADIUS - 2,
      pointHitRadius: POINT_HIT_RADIUS,
      pointBorderColor: CHART_SURFACE,
      pointBorderWidth: 2,
      borderCapStyle: 'round' as const,
      borderJoinStyle: 'round' as const,
      fill: false,
      tension: 0,
    };
  });

  const config: ChartConfiguration<'line', (number | null)[], string> = {
    type: 'line',
    data: {
      labels: normalization.axes.map((a) => a.title),
      datasets,
    },
    options: {
      ...baseOptions(reducedMotion),
      scales: {
        x: {
          type: 'category',
          grid: gridOptions(),
          border: { color: CHART_INK.baseline },
          ticks: { ...tickOptions(), color: CHART_INK.secondary },
        },
        y: {
          type: 'linear',
          min: 0,
          max: 1,
          title: axisTitle(['Normalized, 0-1', 'up is better on every axis']),
          grid: gridOptions(),
          border: { color: CHART_INK.baseline },
          // The tick values carry no absolute meaning, so only the two ends are labelled.
          ticks: { ...tickOptions(), callback: (value) => (Number(value) === 0 || Number(value) === 1 ? String(value) : '') },
        },
      },
      plugins: {
        legend: {
          display: true,
          position: 'bottom',
          labels: { color: CHART_INK.secondary, usePointStyle: true },
        },
        tooltip: {
          ...TOOLTIP_STYLE,
          callbacks: {
            // Real values, not the normalized ones: the plot is a shape, the tooltip is the record.
            label: (item) => {
              const row = normalization.rows[item.datasetIndex];
              if (!row) {
                return '';
              }
              const axis = normalization.axes[item.dataIndex];
              const format =
                axis?.id === 'cost' ? formatUsd : axis?.id === 'speed' && speedLowerIsBetter(options.speedMeasure) ? formatMs : (v: number) => v.toFixed(1);
              return `${row.label} — ${axis?.title}: ${format(row.raw[item.dataIndex])}`;
            },
          },
        },
        datalabels: { display: false },
      },
    },
  };

  const title = 'Model profiles';
  const chrome: FigureChrome = {
    title,
    badges: [...countBadges(plotted, context), pricingBadge(context.pricingBasis, context.pricedOn)],
    detail: pricingNote(context.pricingBasis),
    key: [],
    highlight: '',
    notes: [
      ...warningNotes(degradedNotices(plotted, ['speed', 'cost'])),
      {
        text:
          'Normalized view: compare shapes and crossings, not values. Up is better on every axis; ' +
          'real ranges are listed below the plot.',
        tone: 'info',
      },
    ],
  };

  return {
    id: 'p2-profile',
    title,
    chrome,
    config,
    // No error-bar plugin here, by design.
    plugins: [],
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
  readonly profile: ChartSpec<'line', (number | null)[], string>;
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

  const glyphs = buildIdentityGlyphs(options.glyphSource ?? entries);
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
