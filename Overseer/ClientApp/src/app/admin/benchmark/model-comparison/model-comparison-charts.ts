/**
 * Chart core for the cross-model benchmark comparison view: the palette, the per-model identity
 * glyphs, the measure definitions, the Pareto-frontier computation, the error-bar plugin and the
 * configurations for the six figures (S1-S3 scatters, P1's three linked panels, P2's profile plot).
 *
 * The module is pure TypeScript: it constructs no components, touches no DOM node and imports
 * nothing from Angular, so its spec runs without a TestBed fixture. The one browser API it reaches
 * for is `matchMedia`, behind a guard and behind an injectable factory, so the reduced-motion
 * branch is testable without a real media query.
 */

import ChartDataLabels from 'chartjs-plugin-datalabels';
import type { ChartConfiguration, ChartType, DefaultDataPoint, Plugin, Point } from 'chart.js';

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

  /** Candidate-only cost of one question, in USD, on the request's pricing basis. */
  readonly candidateCostPerQuestionUsd: number;
  /** SD of that cost across runs. Null at R = 1, where the mark instead carries an `n = 1` note. */
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

/** Facts shared by every entry in a comparable set, used for subtitles and the suite-cost rescale. */
export interface ModelComparisonContext {
  /** Items per run. Constant across a comparable set, because every Fundamental key must match. */
  readonly itemsPerRun: number;
  /** The pricing basis and its date, named on every cost figure. */
  readonly pricingBasisLabel: string;
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

/** P1's speed panel switches between these two. The scatters always use TTFT P50. */
export type SpeedMeasure = 'speedIndex' | 'ttftP50';
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

/** The value P1's speed panel plots under the selected measure. */
export function speedValue(entry: ModelComparisonEntry, measure: SpeedMeasure): number | null {
  return measure === 'speedIndex' ? entry.speedIndex : entry.ttftP50Ms;
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
  return measure === 'ttftP50';
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
  /** Short annotation drawn beside the mark, used for the explicit `n = 1` marker. */
  readonly note?: string;
}

/** Half-width of an error-bar cap, in pixels. */
const ERROR_BAR_CAP_HALF_WIDTH = 5;
const ERROR_BAR_LINE_WIDTH = 1.5;
const ANNOTATION_FONT = '11px "Lato", system-ui, sans-serif';

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
    ctx.fillStyle = CHART_INK.muted;
    ctx.lineWidth = ERROR_BAR_LINE_WIDTH;
    ctx.font = ANNOTATION_FONT;
    ctx.textBaseline = 'middle';

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
        if (raw.note) {
          ctx.fillText(raw.note, element.x + 10, element.y - 10);
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

  return { frontier: ordered, steps };
}

// ---------------------------------------------------------------------------------------------
// Figure plumbing
// ---------------------------------------------------------------------------------------------

/** Which corner of a scatter is the good one. Rendered as a caption, never as a reversed axis. */
export interface PreferredCorner {
  readonly x: 'left' | 'right';
  readonly y: 'top' | 'bottom';
  readonly label: string;
}

/**
 * One figure: its chart.js configuration plus the chrome the component renders as HTML.
 *
 * Titles, subtitles and captions are strings rather than chart.js title plugins so they render as
 * real text in the page - selectable, translatable, and readable by a screen reader that never sees
 * the canvas.
 */
export interface ChartSpec<
  TType extends ChartType = ChartType,
  TData = DefaultDataPoint<TType>,
  TLabel = unknown,
> {
  readonly id: string;
  readonly title: string;
  /** States n: models plotted, runs behind them, items per run, and the pricing basis. */
  readonly subtitle: string;
  readonly caption?: string;
  readonly notices: readonly string[];
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

function axisTitle(text: string) {
  return { display: true, text, color: CHART_INK.secondary, font: { size: 12 } };
}

function subtitleFor(plotted: readonly ModelComparisonEntry[], context: ModelComparisonContext): string {
  const runs = plotted.reduce((sum, e) => sum + e.runCount, 0);
  return (
    `${plotted.length} ${plotted.length === 1 ? 'model' : 'models'} · ` +
    `${runs} ${runs === 1 ? 'run' : 'runs'} · ` +
    `${context.itemsPerRun} items per run · ${context.pricingBasisLabel}`
  );
}

function formatMs(value: number): string {
  return value >= 1000 ? `${(value / 1000).toFixed(2)} s` : `${Math.round(value)} ms`;
}

function formatUsd(value: number): string {
  if (value === 0) {
    return '$0';
  }
  return value < 0.01 ? `$${value.toFixed(5)}` : `$${value.toFixed(3)}`;
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

function n1Note(entry: ModelComparisonEntry): string | undefined {
  return entry.runCount === 1 ? 'n = 1' : undefined;
}

// ---------------------------------------------------------------------------------------------
// S1-S3: the scatters
// ---------------------------------------------------------------------------------------------

interface ScatterAxisSpec {
  readonly title: string;
  readonly type: 'linear' | 'logarithmic';
  readonly better: BetterDirection;
  readonly min?: number;
  readonly max?: number;
  readonly format: (value: number) => string;
  readonly value: (entry: ModelComparisonEntry) => number;
  readonly errLow: (entry: ModelComparisonEntry) => number | undefined;
  readonly errHigh: (entry: ModelComparisonEntry) => number | undefined;
}

const QUALITY_AXIS: ScatterAxisSpec = {
  title: 'Intelligence Index (0-100) — higher is better',
  type: 'linear',
  better: 'higher',
  min: 0,
  max: 100,
  format: (v) => v.toFixed(1),
  value: (e) => e.intelligenceIndex,
  errLow: (e) => e.intelligenceIndexCi95HalfWidth,
  errHigh: (e) => e.intelligenceIndexCi95HalfWidth,
};

// The axis is logarithmic because known runs span 7 s to 200 s; on a linear axis every fast model
// collapses into the left edge. The word is in the title because an unannounced log axis deceives.
const TTFT_AXIS: ScatterAxisSpec = {
  title: 'Time to first token, P50 (ms, logarithmic scale) — lower is better',
  type: 'logarithmic',
  better: 'lower',
  format: formatMs,
  value: (e) => e.ttftP50Ms,
  // Latency is right-skewed, so the spread is the P50-to-P90 whisker, never a symmetric SD.
  errLow: () => undefined,
  errHigh: (e) => Math.max(0, e.ttftP90Ms - e.ttftP50Ms),
};

const COST_AXIS: ScatterAxisSpec = {
  title: 'Candidate cost per question (USD, logarithmic scale) — lower is better',
  type: 'logarithmic',
  better: 'lower',
  format: formatUsd,
  value: (e) => e.candidateCostPerQuestionUsd,
  errLow: (e) => e.candidateCostPerQuestionSdUsd ?? undefined,
  errHigh: (e) => e.candidateCostPerQuestionSdUsd ?? undefined,
};

function scatterPoint(entry: ModelComparisonEntry, x: ScatterAxisSpec, y: ScatterAxisSpec): ErrorBarPoint {
  return {
    x: x.value(entry),
    y: y.value(entry),
    xErrLow: x.errLow(entry),
    xErrHigh: x.errHigh(entry),
    yErrLow: y.errLow(entry),
    yErrHigh: y.errHigh(entry),
    note: n1Note(entry),
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

  const pareto = computeParetoFrontier(
    plotted.map((e) => ({ key: e.key, x: xAxis.value(e), y: yAxis.value(e) })),
    xAxis.better,
    yAxis.better,
  );
  if (pareto.steps.length > 1) {
    datasets.push({
      label: 'Pareto frontier',
      data: pareto.steps.map((p) => ({ x: p.x, y: p.y })),
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

  const config: ChartConfiguration<'scatter', ErrorBarPoint[]> = {
    type: 'scatter',
    data: { datasets },
    options: {
      ...baseOptions(reducedMotion),
      scales: {
        x: {
          type: xAxis.type,
          min: xAxis.min,
          max: xAxis.max,
          title: axisTitle(xAxis.title),
          grid: gridOptions(),
          border: { color: CHART_INK.baseline },
          ticks: { ...tickOptions(), callback: (value) => xAxis.format(Number(value)) },
        },
        y: {
          type: yAxis.type,
          min: yAxis.min,
          max: yAxis.max,
          title: axisTitle(yAxis.title),
          grid: gridOptions(),
          border: { color: CHART_INK.baseline },
          ticks: { ...tickOptions(), callback: (value) => yAxis.format(Number(value)) },
        },
      },
      plugins: {
        legend: {
          display: true,
          position: 'bottom',
          labels: {
            color: CHART_INK.secondary,
            usePointStyle: true,
            // The frontier is an annotation, not a series, so it stays out of the identity legend.
            filter: (item) => item.text !== 'Pareto frontier',
          },
        },
        tooltip: {
          ...TOOLTIP_STYLE,
          callbacks: {
            label: (item) => {
              // Chart.js parses a gap as a null coordinate. No entry plotted here carries one, so
              // this is the unreachable branch rather than a formatting case.
              const { x, y } = item.parsed;
              return x === null || y === null
                ? `${item.dataset.label}: not measured`
                : `${item.dataset.label}: ${xAxis.format(x)}, ${yAxis.format(y)}`;
            },
          },
        },
        datalabels: {
          // Past three entries the hue x shape composite needs its text partner: at four or more
          // models the marks are direct-labelled so identity never rests on colour alone. The
          // frontier dataset sits past the model datasets and is never labelled.
          display: (ctx: { datasetIndex: number }) => plotted.length >= 4 && ctx.datasetIndex < plotted.length,
          color: CHART_INK.muted,
          align: 'right',
          offset: 8,
          font: { size: 11 },
          formatter: (_value: unknown, ctx: { datasetIndex: number }) =>
            plotted[ctx.datasetIndex]?.label ?? '',
        },
      },
    },
  };

  return {
    id,
    title,
    subtitle: subtitleFor(plotted, context),
    caption:
      'Hollow marks are single runs (R = 1); solid marks aggregate two or more. The stepped line ' +
      'is the Pareto frontier — the models nothing else beats on both axes. No trend line is ' +
      'drawn: a regression over eight heterogeneous models is a claim the data cannot support.',
    notices: [...extraNotices],
    preferredCorner,
    config,
    plugins: [errorBarPlugin, ChartDataLabels as Plugin],
  };
}

/** S1 — Intelligence Index against time to first token. */
export function buildQualitySpeedScatter(
  plotted: readonly ModelComparisonEntry[],
  options: FigureOptions,
): ChartSpec<'scatter', ErrorBarPoint[]> {
  return buildScatter(
    's1-quality-speed',
    'Quality against speed',
    plotted,
    TTFT_AXIS,
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
    'Quality against cost',
    plotted,
    COST_AXIS,
    QUALITY_AXIS,
    options,
    degradedNotices(plotted, ['cost']),
  );
}

/** S3 — candidate cost per question against time to first token. */
export function buildSpeedCostScatter(
  plotted: readonly ModelComparisonEntry[],
  options: FigureOptions,
): ChartSpec<'scatter', ErrorBarPoint[]> {
  return buildScatter(
    's3-speed-cost',
    'Speed against cost',
    plotted,
    TTFT_AXIS,
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
  readonly quality: ChartSpec<'bar', ErrorBarPoint[], string>;
  readonly speed: ChartSpec<'bar', ErrorBarPoint[], string>;
  readonly cost: ChartSpec<'bar', ErrorBarPoint[], string>;
  /** The single model order all three panels share, as entry keys, in drawing order. */
  readonly order: readonly string[];
  readonly notices: readonly string[];
}

function barPoint(
  index: number,
  value: number,
  errLow: number | undefined,
  errHigh: number | undefined,
  note: string | undefined,
  orientation: BarOrientation,
): ErrorBarPoint {
  return orientation === 'vertical'
    ? { x: index, y: value, yErrLow: errLow, yErrHigh: errHigh, note }
    : { x: value, y: index, xErrLow: errLow, xErrHigh: errHigh, note };
}

function buildPanel(
  id: string,
  title: string,
  plotted: readonly ModelComparisonEntry[],
  panelHue: string,
  axisTitleText: string,
  axisMax: number | undefined,
  format: (value: number) => string,
  values: readonly (number | null)[],
  errLows: readonly (number | undefined)[],
  errHighs: readonly (number | undefined)[],
  notes: readonly (string | undefined)[],
  options: SmallMultiplesOptions,
  notices: readonly string[],
): ChartSpec<'bar', ErrorBarPoint[], string> {
  const { context, reducedMotion, highlightedKey, selectedKeys, orientation } = options;
  const emphasised = new Set<string>(selectedKeys ?? []);
  if (highlightedKey) {
    emphasised.add(highlightedKey);
  }
  const hasEmphasis = emphasised.size > 0;

  const data = plotted.map((_entry, index) =>
    barPoint(index, values[index] ?? 0, errLows[index], errHighs[index], notes[index], orientation),
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
    title: axisTitle(axisTitleText),
    grid: gridOptions(),
    border: { color: CHART_INK.baseline },
    ticks: { ...tickOptions(), callback: (value: string | number) => format(Number(value)) },
  };

  const config: ChartConfiguration<'bar', ErrorBarPoint[], string> = {
    type: 'bar',
    data: {
      labels: plotted.map((e) => e.label),
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
        datalabels: { display: false },
      },
    },
  };

  return {
    id,
    title,
    subtitle: subtitleFor(plotted, context),
    notices: [...notices],
    config,
    plugins: [errorBarPlugin],
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
  // Speed Index has no percentile spread; TTFT carries the P50-to-P90 whisker, which is one-sided
  // because latency is right-skewed and an SD would imply a symmetry the data does not have.
  const speedErrLow = plotted.map((e) =>
    speedMeasure === 'speedIndex' ? e.speedIndexSd ?? undefined : undefined,
  );
  const speedErrHigh = plotted.map((e) =>
    speedMeasure === 'speedIndex'
      ? e.speedIndexSd ?? undefined
      : Math.max(0, e.ttftP90Ms - e.ttftP50Ms),
  );

  const costValues = plotted.map((e) => costValue(e, costMeasure, context));
  // At R = 1 there is no reproducibility SD, so the bar carries no interval and an explicit
  // `n = 1` marker instead - silence would read as certainty.
  const costSd = plotted.map((e) =>
    costMeasure === 'candidateSuite' ? suiteCostSdUsd(e, context) ?? undefined : e.totalRunCostSdUsd ?? undefined,
  );
  const notes = plotted.map((e) => n1Note(e));

  const saturated = plotted.filter((e) => e.speedIndexSaturated);
  const speedNotices = degradedNotices(plotted, ['speed']);
  if (speedMeasure === 'speedIndex' && saturated.length > 0) {
    speedNotices.push(
      `Speed Index is saturated for ${saturated.length} of ${plotted.length} plotted ` +
        `${plotted.length === 1 ? 'entry' : 'entries'}: several models sit at the ceiling and are not ` +
        'distinguishable on this panel even when their real latency differs severalfold. Switch the ' +
        'measure to TTFT P50 to separate them.',
    );
  }
  const missingIndex = plotted.filter((e) => e.speedIndex === null);
  if (speedMeasure === 'speedIndex' && missingIndex.length > 0) {
    speedNotices.push(
      `No Speed Index for ${missingIndex.map((e) => e.label).join(', ')}; those bars are drawn at zero.`,
    );
  }

  const speedTitle =
    speedMeasure === 'speedIndex'
      ? 'Speed Index (0-100) — higher is better'
      : 'Time to first token, P50 (ms) — lower is better';
  const costTitle =
    costMeasure === 'candidateSuite'
      ? `Candidate cost for the whole suite (USD, ${context.itemsPerRun} items) — lower is better`
      : 'Total run cost including grading roles (USD) — lower is better';

  const noop = plotted.map(() => undefined);

  return {
    quality: buildPanel(
      'p1a-quality',
      'Quality',
      plotted,
      CATEGORICAL_PALETTE_DARK[0],
      'Intelligence Index (0-100) — higher is better',
      100,
      (v) => v.toFixed(0),
      qualityValues,
      qualityErr,
      qualityErr,
      noop,
      options,
      [],
    ),
    speed: buildPanel(
      'p1b-speed',
      'Speed',
      plotted,
      CATEGORICAL_PALETTE_DARK[1],
      speedTitle,
      speedMeasure === 'speedIndex' ? 100 : undefined,
      speedMeasure === 'speedIndex' ? (v) => v.toFixed(0) : formatMs,
      speedValues,
      speedErrLow,
      speedErrHigh,
      noop,
      options,
      speedNotices,
    ),
    cost: buildPanel(
      'p1c-cost',
      'Cost',
      plotted,
      CATEGORICAL_PALETTE_DARK[2],
      costTitle,
      undefined,
      formatUsd,
      costValues,
      costSd,
      costSd,
      notes,
      options,
      degradedNotices(plotted, ['cost']),
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
    quality: { title: 'Quality', lowerIsBetter: false, format: (v) => v.toFixed(1) },
    speed: {
      title: speedMeasure === 'speedIndex' ? 'Speed Index' : 'Speed (TTFT P50)',
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
          title: axisTitle('Normalized, 0-1 — up is better on every axis'),
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

  return {
    id: 'p2-profile',
    title: 'Model profiles',
    subtitle: subtitleFor(plotted, context),
    caption:
      'Normalized — read shape and crossings, not values. Each axis is min-max scaled across the ' +
      'plotted set and oriented so up is better; the real minimum and maximum are printed at its ' +
      'ends. Values are read from the panels above and from the table. No error bars: a normalized ' +
      'axis cannot express an interval honestly.',
    notices: degradedNotices(plotted, ['speed', 'cost']),
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
  const speedMeasure = options.speedMeasure ?? 'speedIndex';
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
