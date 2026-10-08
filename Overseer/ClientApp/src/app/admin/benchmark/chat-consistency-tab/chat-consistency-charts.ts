/**
 * The Chat Consistency charts: Chart.js configurations, their takeaway captions and their data
 * tables, built from a subject's timeline points. The builders are pure functions of their input, so
 * the timeline, the analysis results and the uploaded report charts draw the same figures from the
 * same data; `prefersReducedMotion()` alone reads the environment.
 *
 * Every chart is a line chart over a linear time axis in epoch milliseconds (no date adapter is
 * registered), with an overlay plugin that draws the header band (title, subject and the GnollBench
 * logo), the period bands, the area wash of a single-series chart, composite Overseer events,
 * annotations and served-model changes as labeled markers (their tags staggered in a band above the
 * plot), and the value of every point of a chart drawing at most two series (`ccPlaceLabels`). A
 * legacy latency proxy is drawn with hollow points and a dashed line, so it is told apart by shape as
 * well as by its legend label. Every dataset carries a stable series id (`seriesId`). Every value
 * axis follows its measure's `CcAxisPolicy` (`ccValueAxis`).
 *
 * A point is a run, or a battery run with its members pooled (`CcFigureInput.unitKind`); a battery
 * run's Intelligence is its battery analysis's Overall Intelligence Index.
 *
 * Runs not in the analysis (`CcFigureInput.notAnalyzed`) are drawn as gray crosses joined by gray,
 * dotted segments, their value labels muted, and named in the caption, the tooltip and the data
 * table; without that map the datasets carry no scriptable options.
 */

import { Chart, layouts } from 'chart.js';
import type {
  ChartData,
  ChartDataset,
  ChartOptions,
  LayoutItem,
  LegendItem,
  Plugin,
  PointStyle,
  Scale,
  ScriptableContext,
  ScriptableLineSegmentContext,
  Tick,
  TooltipItem
} from 'chart.js';

import { FIGURE_LOGO_GAP, FigureLogo, drawFigureLogo, figureLogoBox } from '../model-comparison/figure-logo';
import {
  CcAnalysisResult,
  CcAnnotation,
  CcBatteryTimelinePoint,
  CcEvent,
  CcReportFigureKey,
  CcTimelinePoint,
  CcUnitKind
} from './chat-consistency.models';
import {
  CcEventGroup,
  CcMarkerFilter,
  CcMarkerKind,
  dominantServedModel,
  eventGroupLabel,
  eventGroupShown,
  groupOverseerEvents,
  servedChangeLabel,
  servedModelChanges,
  taggedAnnotations
} from './chat-consistency-events';
import {
  annotationKindText,
  ccNumber,
  formatFixed,
  formatFractionPercent,
  formatGrouped,
  formatInteger,
  formatMs,
  formatTokenRate,
  formatUsd,
  formatUtcDateTime,
  MINUS,
  plural,
  utcMillis
} from './chat-consistency-format';
import { unitNoun } from './chat-consistency-scope';

// --- Types ---

export type { CcMarkerFilter, CcMarkerKind };
export { dominantServedModel };

/** One plotted value; `runId` (a battery run id for a battery point) rides along for the tooltip. */
export interface CcChartPoint {
  x: number;
  y: number | null;
  runId: number;
}

/** A labeled vertical marker: a composite Overseer event, an annotation or a served-model change. */
export interface CcChartMarker {
  kind: CcMarkerKind;
  x: number;
  /** The short tag drawn on the chart: `E1`, `A2`, `S1`. */
  tag: string;
  /** What the tag stands for, listed under the chart. */
  label: string;
}

/** A shaded period, for a chart restricted to an analysis. */
export interface CcPeriodBand {
  name: 'Baseline' | 'Comparison';
  start: number;
  end: number;
}

export interface CcChartTheme {
  /** The header title, the legend and the point labels. */
  text: string;
  /** The axis titles. */
  secondary: string;
  /** The ticks, the subject line, the band labels and the crosses of runs not in the analysis. */
  muted: string;
  grid: string;
  /** Painted behind the chart; null leaves the canvas transparent. */
  background: string | null;
  /** The surface the chart sits on: the ring around each point and the fill of a hollow one. */
  surface: string;
  series: readonly string[];
  /** The marker inks: neutral, so color stays with the data. */
  event: string;
  annotation: string;
  served: string;
  baselineBand: string;
  comparisonBand: string;
  /** The tooltip box; null keeps Chart.js's own, for a theme that is never interactive. */
  tooltip: { background: string; border: string } | null;
  fontFamily: string;
}

/**
 * The dark application surface. The values are the design tokens' (`styles.scss`), which a canvas
 * cannot read; the series palette is validated against the `#121212` surface for lightness, chroma,
 * color-vision-deficiency separation and 3 : 1 contrast.
 */
export const CC_SCREEN_THEME: CcChartTheme = Object.freeze({
  text: '#e4e4e7',
  secondary: '#d4d4d8',
  muted: '#a1a1aa',
  grid: 'rgba(255, 255, 255, 0.07)',
  background: null,
  surface: '#121212',
  series: ['#c98500', '#3987e5', '#d95926', '#199e70', '#9085e9', '#d55181'],
  event: '#d4d4d8',
  annotation: '#a1a1aa',
  served: '#e4e4e7',
  baselineBand: 'rgba(57, 135, 229, 0.10)',
  comparisonBand: 'rgba(201, 133, 0, 0.12)',
  tooltip: { background: 'rgba(20, 20, 20, 0.96)', border: 'rgba(255, 255, 255, 0.12)' },
  fontFamily: '"Lato", "Segoe UI", Arial, sans-serif'
});

/** A light page, for the charts printed in PDF and Word; its palette is validated against white. */
export const CC_PRINT_THEME: CcChartTheme = Object.freeze({
  text: '#1f2937',
  secondary: '#374151',
  muted: '#4b5563',
  grid: 'rgba(0, 0, 0, 0.08)',
  background: '#ffffff',
  surface: '#ffffff',
  series: ['#b07400', '#2a78d6', '#eb6834', '#14936a', '#4a3aa7', '#cc4f86'],
  event: '#374151',
  annotation: '#6b7280',
  served: '#111827',
  baselineBand: 'rgba(57, 135, 229, 0.05)',
  comparisonBand: 'rgba(201, 133, 0, 0.06)',
  tooltip: null,
  fontFamily: '"Segoe UI", "Helvetica Neue", Arial, sans-serif'
});

/** A Chart.js line dataset with its stable series id from `CC_FIGURE_SERIES`. */
export type CcChartDataset = ChartDataset<'line', CcChartPoint[]> & { seriesId: string };

/** What `<canvas baseChart>` and `renderPlotOffscreen` take. */
export interface CcChartConfig {
  type: 'line';
  data: Omit<ChartData<'line', CcChartPoint[]>, 'datasets'> & { datasets: CcChartDataset[] };
  options: ChartOptions<'line'>;
  plugins: Plugin<'line'>[];
}

/** One item of a list cell: a member run's `#id` and its suite name, null where none is known. */
export interface CcDataListItem {
  ref: string;
  label: string | null;
}

/** A chart's data as a real table, for the figure's *Show data* disclosure. */
export interface CcFigureTable {
  columns: string[];
  rows: string[][];
  /**
   * Column label → per row, the column's cells as lists, for a card to show one item per line; the
   * string cell holds the same items joined.
   */
  lists?: Readonly<Record<string, readonly (readonly CcDataListItem[])[]>>;
}

export type CcFigureKey = 'quality' | 'ttfat' | 'rate' | 'work' | 'tools' | 'cost' | 'reliability' | 'timeline';

/** The figures that write values: every figure but the overview. */
export type CcValueFigureKey = Exclude<CcFigureKey, 'timeline'>;

/** Decimal places per chart; a missing chart writes its automatic precision. */
export type CcDecimalPlaces = Partial<Record<CcValueFigureKey, number>>;

/** The decimal places each chart offers besides its automatic precision. */
export const CC_DECIMAL_CHOICES: Readonly<Record<CcValueFigureKey, readonly number[]>> = Object.freeze({
  quality: [0, 1, 2, 3],
  ttfat: [0, 1, 2, 3],
  rate: [0, 1, 2, 3],
  work: [0, 1, 2, 3],
  tools: [0, 1, 2, 3],
  cost: [0, 1, 2, 3, 4],
  reliability: [0, 1, 2, 3]
});

/** The label of a chart's automatic precision, as its figure builder writes values without a setting. */
export function ccAutoDecimalsText(key: CcValueFigureKey, unitKind: CcUnitKind): string {
  switch (key) {
    case 'quality': return unitKind === 'batteryRun' ? 'Automatic (1)' : 'Automatic (0)';
    case 'ttfat': return 'Automatic (1, in seconds)';
    case 'work': return 'Automatic (0)';
    case 'cost': return 'Automatic (2–4)';
    default: return 'Automatic (1)';
  }
}

export interface CcFigure {
  key: CcFigureKey;
  title: string;
  /** One sentence generated from the data, the figure's caption. */
  takeaway: string;
  /** The canvas's accessible name. */
  altText: string;
  /** Null when no point has a value to plot. */
  config: CcChartConfig | null;
  table: CcFigureTable;
  markers: CcChartMarker[];
}

export interface CcFigureInput {
  /** Runs, or battery points (`CcBatteryTimelinePoint`) when `unitKind` is `batteryRun`. */
  points: readonly CcTimelinePoint[];
  /** What a point is; `run` when absent. */
  unitKind?: CcUnitKind;
  /** Member run id → its suite name, for a battery point's *Member runs* in the data table. */
  memberLabels?: ReadonlyMap<number, string>;
  events?: readonly CcEvent[];
  annotations?: readonly CcAnnotation[];
  bands?: readonly CcPeriodBand[];
  /** The markers drawn; every marker when absent. */
  markerFilter?: CcMarkerFilter;
  /** The points whose harness versions group the events; `points` when absent. */
  harnessPoints?: readonly CcTimelinePoint[];
  /** Composite events whose tags the markers reuse. */
  eventNumbering?: readonly CcEventGroup[];
  /** Point id → why the run or battery run is not in the analysis; absent or empty draws every point alike. */
  notAnalyzed?: ReadonlyMap<number, string>;
}

/** How `buildMarkers` groups and numbers the events, beyond the drawn points. */
export interface CcEventContext {
  /** The points whose harness versions group the events; the drawn points when absent. */
  harnessPoints?: readonly CcTimelinePoint[];
  /** Composite events whose tags the event markers reuse. */
  numbering?: readonly CcEventGroup[];
}

/** The header band's text; a null line is not drawn. */
export interface CcChartHeader {
  title: string | null;
  subject: string | null;
}

export interface CcChartOptions {
  theme?: CcChartTheme;
  /** Chart.js animation off; set from `prefers-reduced-motion`, and always for an offscreen render. */
  reducedMotion?: boolean;
  /**
   * Series ids (`CC_FIGURE_SERIES`) left out of the drawing; the takeaway and the data table still
   * describe every series.
   */
  hiddenSeries?: ReadonlySet<string>;
  /**
   * The Intelligence axis shows the full 0–100 scale instead of a window around the data; the other
   * value axes always start at zero.
   */
  zeroBaseline?: boolean;
  /** The header band's title and subject line; absent draws neither. */
  header?: CcChartHeader;
  /** The GnollBench logo at the header band's right; absent or null draws none. */
  logo?: FigureLogo | null;
  /** The decimals of the point labels, tooltip, takeaway and table; the axis ticks keep their step's. */
  decimals?: CcDecimalPlaces;
}

/** One dataset of a figure: its stable id, its legend label and the tooltip's short name for it. */
export interface CcFigureSeries {
  id: string;
  label: string;
  shortLabel: string;
}

/** The figures with their titles, in `buildCcFigures` order. */
export const CC_FIGURE_KEYS: readonly { readonly key: CcFigureKey; readonly title: string }[] = Object.freeze([
  { key: 'quality', title: 'Intelligence per run' },
  { key: 'ttfat', title: 'Time to first answer text' },
  { key: 'rate', title: 'Answer streaming rate' },
  { key: 'work', title: 'Output tokens per answer' },
  { key: 'tools', title: 'Tool calls per answer' },
  { key: 'cost', title: 'Cost per question' },
  { key: 'reliability', title: 'Reliability' },
  { key: 'timeline', title: 'Runs and events' }
] as const);

export function ccFigureTitle(key: CcFigureKey): string {
  return CC_FIGURE_KEYS.find(entry => entry.key === key)?.title ?? key;
}

const RELIABILITY_RATES: readonly { key: keyof CcTimelinePoint & string; label: string; pointStyle: PointStyle }[] = [
  { key: 'terminalFailureRate', label: 'Terminal failures', pointStyle: 'circle' },
  { key: 'timeoutRate', label: 'Timeouts', pointStyle: 'rect' },
  { key: 'emptyAnswerRate', label: 'Empty answers', pointStyle: 'triangle' },
  { key: 'refusalRate', label: 'Refusals', pointStyle: 'rectRot' },
  { key: 'toolBudgetExhaustedRate', label: 'Tool budget exhausted', pointStyle: 'star' }
];

/**
 * Every series a figure can draw, by figure, with its stable id. `quality.native` is drawn for runs and
 * `quality.overall` for battery runs; `quality.common` only while a common grader covers points, and
 * its legend names that grader.
 */
export const CC_FIGURE_SERIES: Readonly<Record<CcFigureKey, readonly CcFigureSeries[]>> = Object.freeze({
  quality: [
    { id: 'quality.native', label: 'Intelligence Index (native grades)', shortLabel: 'Intelligence' },
    { id: 'quality.overall', label: 'Overall Intelligence Index (battery)', shortLabel: 'Overall Index' },
    { id: 'quality.common', label: 'Intelligence, common grader', shortLabel: 'Common grader' }
  ],
  ttfat: [
    { id: 'ttfat.telemetry', label: 'Time to first answer text (telemetry)', shortLabel: 'Telemetry' },
    { id: 'ttfat.proxy', label: 'Model time per answer (legacy proxy)', shortLabel: 'Legacy proxy' }
  ],
  rate: [
    { id: 'rate.measured', label: 'Streaming rate (measured)', shortLabel: 'Measured' },
    { id: 'rate.estimated', label: 'Streaming rate (estimated)', shortLabel: 'Estimated' }
  ],
  work: [
    { id: 'work.tokens', label: 'Output tokens per answer', shortLabel: 'Output tokens' }
  ],
  tools: [
    { id: 'tools.calls', label: 'Tool calls per answer', shortLabel: 'Tool calls' }
  ],
  cost: [
    { id: 'cost.cost', label: 'Cost per question (USD)', shortLabel: 'Cost' }
  ],
  reliability: RELIABILITY_RATES.map(rate => ({ id: `reliability.${rate.key}`, label: rate.label, shortLabel: rate.label })),
  timeline: [
    { id: 'timeline.telemetry', label: 'Run with call telemetry', shortLabel: 'Telemetry run' },
    { id: 'timeline.legacy', label: 'Legacy run', shortLabel: 'Legacy run' }
  ]
});

function seriesLabel(key: CcFigureKey, id: string): string {
  return CC_FIGURE_SERIES[key].find(series => series.id === id)?.label ?? id;
}

/** The tooltip's name of a series: `Overall Index`, `Estimated`; the id where none is listed. */
function seriesShortLabel(id: string): string {
  for (const series of Object.values(CC_FIGURE_SERIES)) {
    const found = series.find(entry => entry.id === id);
    if (found) return found.shortLabel;
  }
  return id;
}

/** The report figure each uploaded chart key draws. */
export const CC_REPORT_FIGURES: Readonly<Record<CcReportFigureKey, CcFigureKey>> = Object.freeze({
  'cc1-quality': 'quality',
  'cc2-speed': 'ttfat',
  'cc3-work': 'work',
  'cc4-timeline': 'timeline'
});

// --- Units ---

/** The point as a battery point, or null for a run's point. */
export function batteryPointOf(point: CcTimelinePoint): CcBatteryTimelinePoint | null {
  return 'memberRunIds' in point ? point as CcBatteryTimelinePoint : null;
}

function unitKindOf(input: CcFigureInput): CcUnitKind {
  return input.unitKind ?? 'run';
}

/** `run` or `battery run`. */
function nounOf(input: CcFigureInput): string {
  return unitNoun(unitKindOf(input));
}

function capitalized(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** `#301 (Board Suite), #302 (Wiki Suite)`; the ids alone where no suite name is known. */
function membersText(point: CcBatteryTimelinePoint, labels: ReadonlyMap<number, string> | undefined): string {
  return point.memberRunIds.map(id => {
    const label = labels?.get(id);
    return label ? `#${id} (${label})` : `#${id}`;
  }).join(', ');
}

/** The member runs of `point` as list items, in member order. */
function membersList(point: CcBatteryTimelinePoint, labels: ReadonlyMap<number, string> | undefined): CcDataListItem[] {
  return point.memberRunIds.map(id => ({ ref: `#${id}`, label: labels?.get(id) || null }));
}

/**
 * The points an analysis's charts draw: in a battery analysis the battery points of its units (one
 * per battery run), else the runs of its periods.
 */
export function analysisChartPoints(
  result: CcAnalysisResult,
  points: readonly CcTimelinePoint[],
  batteryPoints: readonly CcBatteryTimelinePoint[]
): { points: CcTimelinePoint[]; unitKind: CcUnitKind } {
  const set = result.comparisonSet;
  if (set?.kind === 'battery' && (result.units?.length ?? 0) > 0) {
    const ids = new Set(result.units!.map(unit => unit.unitId));
    return { points: batteryPoints.filter(point => point.setKey === set.key && ids.has(point.runId)), unitKind: 'batteryRun' };
  }
  const ids = new Set([...result.baseline.runIds, ...result.comparison.runIds]);
  return { points: points.filter(point => ids.has(point.runId)), unitKind: 'run' };
}

// --- Series ---

/** The points in time order, those without a parsable start left out. */
export function sortedPoints(points: readonly CcTimelinePoint[]): CcTimelinePoint[] {
  return points
    .filter(point => Number.isFinite(utcMillis(point.startedAtUtc)))
    .slice()
    .sort((a, b) => utcMillis(a.startedAtUtc) - utcMillis(b.startedAtUtc) || a.runId - b.runId);
}

function seriesOf(points: readonly CcTimelinePoint[], value: (point: CcTimelinePoint) => number | null): CcChartPoint[] {
  return points.map(point => ({ x: utcMillis(point.startedAtUtc), y: value(point), runId: point.runId }));
}

function valuesOf(series: readonly CcChartPoint[]): number[] {
  return series.map(point => point.y).filter((y): y is number => y !== null);
}

/** The common-grader snapshot that covers the most points, with its display name; null when none does. */
export function dominantCommonGrader(points: readonly CcTimelinePoint[]): { snapshotId: number; display: string } | null {
  const counts = new Map<number, { display: string; count: number }>();
  for (const point of points) {
    for (const snapshotId of new Set(point.commonGraderQuality.map(entry => entry.snapshotId))) {
      const entry = point.commonGraderQuality.find(e => e.snapshotId === snapshotId)!;
      const current = counts.get(snapshotId);
      counts.set(snapshotId, { display: entry.display, count: (current?.count ?? 0) + 1 });
    }
  }
  let best: { snapshotId: number; display: string; count: number } | null = null;
  for (const [snapshotId, value] of counts) {
    if (!best || value.count > best.count || (value.count === best.count && snapshotId < best.snapshotId)) {
      best = { snapshotId, ...value };
    }
  }
  return best ? { snapshotId: best.snapshotId, display: best.display } : null;
}

/** The latest calibration's mean quality of `snapshotId` on one point, or null. */
export function commonGraderQualityOf(point: CcTimelinePoint, snapshotId: number): number | null {
  const entries = point.commonGraderQuality
    .filter(entry => entry.snapshotId === snapshotId)
    .sort((a, b) => utcMillis(b.calibratedAtUtc) - utcMillis(a.calibratedAtUtc));
  return entries.length > 0 ? ccNumber(entries[0].meanQuality) : null;
}

/**
 * The markers of the range: one per composite Overseer event (`E1`…, `groupOverseerEvents`),
 * annotations (`A1`…) and the runs whose dominant served model differs from the previous run's
 * (`S1`…), each kind numbered in time order. The filter drops markers and never renumbers them.
 * `context` can group the events by a wider set of points and take their tags from reference
 * composites; served-model changes are always over `points`.
 */
export function buildMarkers(
  points: readonly CcTimelinePoint[],
  events: readonly CcEvent[] = [],
  annotations: readonly CcAnnotation[] = [],
  filter?: CcMarkerFilter,
  context: CcEventContext = {}
): CcChartMarker[] {
  const markers: CcChartMarker[] = [];
  const shows = (kind: CcMarkerKind) => !filter || filter.kinds.has(kind);

  if (shows('event')) {
    for (const group of groupOverseerEvents(events, context.harnessPoints ?? points, context.numbering)) {
      if (eventGroupShown(group, filter)) {
        markers.push({ kind: 'event', x: utcMillis(group.atUtc), tag: group.tag, label: eventGroupLabel(group) });
      }
    }
  }
  if (shows('annotation')) {
    for (const { tag, annotation } of taggedAnnotations(annotations)) {
      markers.push({ kind: 'annotation', x: utcMillis(annotation.atUtc), tag, label: `${annotationKindText(annotation.kind)}: ${annotation.text}` });
    }
  }
  if (shows('served')) {
    for (const change of servedModelChanges(points)) {
      markers.push({ kind: 'served', x: utcMillis(change.atUtc), tag: change.tag, label: servedChangeLabel(change) });
    }
  }
  return markers;
}

// --- Time ticks ---

const HOUR_MS = 3_600_000;
const DAY_MS = 86_400_000;
/** 1970-01-05, the first Monday of the epoch, so weekly ticks fall on Mondays. */
const MONDAY_OFFSET_MS = 4 * DAY_MS;
const FIXED_STEPS_MS: readonly number[] = [HOUR_MS, 2 * HOUR_MS, 3 * HOUR_MS, 6 * HOUR_MS, 12 * HOUR_MS, DAY_MS, 2 * DAY_MS, 7 * DAY_MS];
const MONTH_STEPS: readonly number[] = [1, 3, 6, 12];
const MONTH_NAMES = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** The tick values of a time axis: UTC-aligned, at most `maxTicks` of them, and the step between them. */
export interface CcTimeTicks {
  values: number[];
  /** In ms; a month step is counted as 30 days. */
  stepMs: number;
}

/**
 * Tick values for a time axis from `min` to `max` (epoch ms): the smallest step of 1, 2, 3, 6 or 12
 * hours, 1, 2 or 7 days (weeks start on Monday), or 1, 3, 6 or 12 months (from the first of a month)
 * that keeps the ticks to `maxTicks`, aligned to UTC. Empty for an empty or unusable range.
 */
export function ccTimeTicks(min: number, max: number, maxTicks: number): CcTimeTicks {
  if (!Number.isFinite(min) || !Number.isFinite(max) || !(max > min)) return { values: [], stepMs: 0 };
  const limit = Math.max(2, Math.floor(maxTicks));
  for (const step of FIXED_STEPS_MS) {
    const offset = step === 7 * DAY_MS ? MONDAY_OFFSET_MS : 0;
    const first = Math.ceil((min - offset) / step) * step + offset;
    if (Math.floor((max - first) / step) + 1 <= limit) {
      const values: number[] = [];
      for (let value = first; value <= max; value += step) values.push(value);
      return { values, stepMs: step };
    }
  }
  const start = new Date(min);
  const year = start.getUTCFullYear();
  for (let i = 0; ; i++) {
    // 1, 3, 6 and 12 months, then 2, 4, 8… years.
    const months = i < MONTH_STEPS.length ? MONTH_STEPS[i] : 12 * 2 ** (i - MONTH_STEPS.length + 1);
    const values: number[] = [];
    // Date.UTC carries a month past December into the following years.
    let month = Math.ceil(start.getUTCMonth() / months) * months;
    if (Date.UTC(year, month, 1) < min) month += months;
    for (let value = Date.UTC(year, month, 1); value <= max && values.length <= limit; value = Date.UTC(year, month, 1)) {
      values.push(value);
      month += months;
    }
    if (values.length <= limit) return { values, stepMs: months * 30 * DAY_MS };
  }
}

function pad2(value: number): string {
  return String(value).padStart(2, '0');
}

/**
 * One time tick's label. Under a day's step: `06:00`, with the date (`2026-10-08 06:00`) on the first
 * tick of each day; otherwise `Oct 8` for a range under a year and `2026-10` beyond.
 */
export function ccTimeTickLabel(value: number, previous: number | null, stepMs: number, spanMs: number): string {
  const date = new Date(value);
  const day = `${date.getUTCFullYear()}-${pad2(date.getUTCMonth() + 1)}-${pad2(date.getUTCDate())}`;
  if (stepMs > 0 && stepMs < DAY_MS) {
    const time = `${pad2(date.getUTCHours())}:${pad2(date.getUTCMinutes())}`;
    const newDay = previous === null || Math.floor(previous / DAY_MS) !== Math.floor(value / DAY_MS);
    return newDay ? `${day} ${time}` : time;
  }
  if (spanMs < 365 * DAY_MS) return `${MONTH_NAMES[date.getUTCMonth()]} ${date.getUTCDate()}`;
  return day.slice(0, 7);
}

/** About one tick per 110 px of axis, between 2 and 8. */
function maxTicksFor(width: number): number {
  return Math.max(2, Math.min(8, Math.floor((width > 0 ? width : 600) / 110)));
}

// --- The overlay plugin ---

/** The height of one row of marker tags, a 14 px box and a 2 px gap. */
export const CC_TAG_ROW_HEIGHT = 16;
/** The most rows of marker tags a chart lays out. */
export const CC_TAG_MAX_ROWS = 3;
/** The least horizontal gap between two tags of one row, and the gap above the top row. */
export const CC_TAG_GAP = 4;
const TAG_BOX_HEIGHT = 14;

/** The header band's text sizes and the logo's height, in layout px. */
export const CC_HEADER_TITLE_PX = 14;
export const CC_HEADER_SUBJECT_PX = 12;
export const CC_HEADER_LOGO_PX = 28;
const HEADER_LINE_GAP = 4;
const HEADER_PAD_TOP = 2;
const HEADER_PAD_BOTTOM = 6;

/**
 * The row of each marker tag, given by its horizontal center and width in pixels, in input order;
 * `rows` is the number of rows used. Tags are placed left to right: each takes the first row whose
 * last tag ends at least `CC_TAG_GAP` before it begins, a new row while fewer than `maxRows` are in
 * use, and otherwise the row whose last tag ends earliest (the lowest such row on a tie).
 */
export function ccTagRows(
  tags: readonly { center: number; width: number }[],
  maxRows: number = CC_TAG_MAX_ROWS
): { rows: number; row: number[] } {
  const limit = Math.max(1, Math.floor(maxRows));
  const order = tags.map((_, i) => i).sort((a, b) => tags[a].center - tags[b].center || a - b);
  const ends: number[] = [];
  const row = tags.map(() => 0);
  for (const i of order) {
    const left = tags[i].center - tags[i].width / 2;
    let chosen = ends.findIndex(end => left >= end + CC_TAG_GAP);
    if (chosen < 0) {
      chosen = ends.length < limit ? ends.length : ends.indexOf(Math.min(...ends));
    }
    ends[chosen] = Math.max(ends[chosen] ?? Number.NEGATIVE_INFINITY, left + tags[i].width);
    row[i] = chosen;
  }
  return { rows: ends.length, row };
}

/** The height of the tag band for `rows` rows: the rows and the gap above them, nothing without tags. */
export function ccTagBandHeight(rows: number): number {
  return rows > 0 ? rows * CC_TAG_ROW_HEIGHT + CC_TAG_GAP : 0;
}

/**
 * The header band's height for its content at `width`: the title and subject lines, or the logo if it
 * is taller, with the gaps around them; nothing when there is no title, no subject and no logo.
 */
export function ccHeaderHeight(header: CcChartHeader | null | undefined, logo: FigureLogo | null | undefined, width: number): number {
  const lines = [header?.title ? CC_HEADER_TITLE_PX + HEADER_LINE_GAP : 0, header?.subject ? CC_HEADER_SUBJECT_PX + HEADER_LINE_GAP : 0];
  const text = lines[0] + lines[1];
  const logoHeight = figureLogoBox(logo, width)?.height ?? 0;
  const content = Math.max(text, logoHeight);
  return content > 0 ? Math.ceil(content + HEADER_PAD_TOP + HEADER_PAD_BOTTOM) : 0;
}

function tagWidth(ctx: CanvasRenderingContext2D | null | undefined, tag: string): number {
  return (ctx ? ctx.measureText(tag).width : tag.length * 6) + 8;
}

/**
 * The layout box between the legend and the plot that holds the marker tags. One per chart, kept
 * across reconfigurations (Chart.js keeps a plugin by id, so a new overlay plugin object never sees
 * `start`); the overlay plugin drawing the chart hands it the markers before each layout.
 */
interface CcTagBand extends LayoutItem {
  options: Record<string, never>;
  rows: number;
  markers: readonly CcChartMarker[];
  range: { min: number; max: number } | null;
  font: string;
}

const TAG_BANDS = new WeakMap<object, CcTagBand>();

function tagBandRows(chart: Chart<'line'>, band: CcTagBand, width: number): number {
  const scale = chart.scales['x'];
  const range = band.range ?? (scale ? { min: scale.min, max: scale.max } : null);
  if (!range || !(range.max > range.min) || !(width > 0)) return 0;
  const shown = band.markers.filter(marker => marker.x >= range.min && marker.x <= range.max);
  if (shown.length === 0) return 0;
  const ctx = chart.ctx;
  ctx?.save();
  if (ctx) ctx.font = band.font;
  const tags = shown.map(marker => ({
    center: ((marker.x - range.min) / (range.max - range.min)) * width,
    width: tagWidth(ctx, marker.tag)
  }));
  ctx?.restore();
  return ccTagRows(tags).rows;
}

function tagBandOf(chart: Chart<'line'>): CcTagBand {
  const existing = TAG_BANDS.get(chart);
  if (existing) return existing;
  const band: CcTagBand = {
    position: 'top',
    // Under the legend (1000) and the title (2000): a lower weight sits nearer the plot.
    weight: 1,
    fullSize: false,
    width: 0,
    height: 0,
    left: 0,
    top: 0,
    right: 0,
    bottom: 0,
    options: {},
    rows: 0,
    markers: [],
    range: null,
    font: '',
    isHorizontal: () => true,
    draw: () => undefined,
    update(width: number) {
      band.rows = tagBandRows(chart, band, width);
      band.width = width;
      band.height = ccTagBandHeight(band.rows);
    }
  };
  layouts.addBox(chart as unknown as Chart, band);
  TAG_BANDS.set(chart, band);
  return band;
}

/**
 * The layout box at the top of the chart, above the legend, holding the title and subject line on the
 * left and the logo on the right. Kept per chart like the tag band; it draws itself.
 */
interface CcHeaderBand extends LayoutItem {
  options: Record<string, never>;
  header: CcChartHeader | null;
  logo: FigureLogo | null;
  theme: CcChartTheme;
}

const HEADER_BANDS = new WeakMap<object, CcHeaderBand>();

/** `text` cut with an ellipsis to fit `width` in the context's current font. */
function fitText(ctx: CanvasRenderingContext2D, text: string, width: number): string {
  if (!(width > 0)) return '';
  if (ctx.measureText(text).width <= width) return text;
  let end = text.length;
  while (end > 0 && ctx.measureText(`${text.slice(0, end).trimEnd()}…`).width > width) end--;
  return end > 0 ? `${text.slice(0, end).trimEnd()}…` : '';
}

function drawHeader(chart: Chart<'line'>, band: CcHeaderBand): void {
  if (!(band.height > 0)) return;
  const ctx = chart.ctx;
  const { theme, header, logo } = band;
  const box = figureLogoBox(logo, band.width);
  ctx.save();
  if (logo && box) {
    drawFigureLogo(ctx, logo, box, band.right - box.width, band.top + HEADER_PAD_TOP);
  }
  const textWidth = band.width - (box ? box.width + FIGURE_LOGO_GAP : 0);
  let y = band.top + HEADER_PAD_TOP;
  ctx.textBaseline = 'top';
  ctx.textAlign = 'left';
  if (header?.title) {
    ctx.font = `bold ${CC_HEADER_TITLE_PX}px ${theme.fontFamily}`;
    ctx.fillStyle = theme.text;
    ctx.fillText(fitText(ctx, header.title, textWidth), band.left, y);
    y += CC_HEADER_TITLE_PX + HEADER_LINE_GAP;
  }
  if (header?.subject) {
    ctx.font = `${CC_HEADER_SUBJECT_PX}px ${theme.fontFamily}`;
    ctx.fillStyle = theme.muted;
    ctx.fillText(fitText(ctx, header.subject, textWidth), band.left, y);
  }
  ctx.restore();
}

function headerBandOf(chart: Chart<'line'>): CcHeaderBand {
  const existing = HEADER_BANDS.get(chart);
  if (existing) return existing;
  const band: CcHeaderBand = {
    position: 'top',
    // Above the title (2000) and the legend (1000): the outermost box.
    weight: 3000,
    fullSize: true,
    width: 0,
    height: 0,
    left: 0,
    top: 0,
    right: 0,
    bottom: 0,
    options: {},
    header: null,
    logo: null,
    theme: CC_SCREEN_THEME,
    isHorizontal: () => true,
    draw: () => drawHeader(chart, band),
    update(width: number) {
      band.width = width;
      band.height = ccHeaderHeight(band.header, band.logo, width);
    }
  };
  layouts.addBox(chart as unknown as Chart, band);
  HEADER_BANDS.set(chart, band);
  return band;
}

function removeBands(chart: Chart<'line'>): void {
  for (const bands of [TAG_BANDS, HEADER_BANDS] as WeakMap<object, LayoutItem>[]) {
    const band = bands.get(chart);
    if (!band) continue;
    layouts.removeBox(chart as unknown as Chart, band);
    bands.delete(chart);
  }
}

/** The values of one drawn dataset, written at its points. */
export interface CcPointLabelSet {
  datasetIndex: number;
  /** The value as the data table writes it. */
  format: (value: number) => string;
  /** The labels prefer the space below their points: the second of two series. */
  below: boolean;
}

/** What the overlay plugin draws besides the bands and the markers. */
export interface CcOverlayDecor {
  header?: CcChartHeader | null;
  logo?: FigureLogo | null;
  /** The dataset whose area is washed with its color; none when absent. */
  wash?: { datasetIndex: number; color: string } | null;
  pointLabels?: readonly CcPointLabelSet[];
  /** Point ids whose labels are muted: the points not in the analysis. */
  notAnalyzed?: ReadonlyMap<number, string> | null;
}

/** A label's box in canvas px. */
export interface CcLabelBox {
  left: number;
  top: number;
  right: number;
  bottom: number;
}

/** The least gap between two point labels, in px. */
const POINT_LABEL_GAP = 2;
/** The point labels' text size, and their distance from the point's center, in px. */
const POINT_LABEL_PX = 11;
const POINT_LABEL_OFFSET = 8;

/**
 * The point labels kept, as indices into `candidates`, which come in priority order: each box is kept
 * unless it leaves `area` or comes within `POINT_LABEL_GAP` of a box kept before it.
 */
export function ccPlaceLabels(candidates: readonly CcLabelBox[], area: CcLabelBox): number[] {
  const kept: number[] = [];
  candidates.forEach((box, i) => {
    if (box.left < area.left || box.right > area.right || box.top < area.top || box.bottom > area.bottom) return;
    const overlaps = kept.some(k => {
      const other = candidates[k];
      return box.left < other.right + POINT_LABEL_GAP && other.left < box.right + POINT_LABEL_GAP
        && box.top < other.bottom + POINT_LABEL_GAP && other.top < box.bottom + POINT_LABEL_GAP;
    });
    if (!overlaps) kept.push(i);
  });
  return kept;
}

interface PointLabel {
  text: string;
  value: number;
  x: number;
  box: CcLabelBox;
  muted: boolean;
}

/**
 * Each set's labels, ordered by priority: per set its latest, highest and lowest point, then every
 * other point left to right. A label sits above its point (below for a `below` set), and on the other
 * side where it would cross the plot's edge; it is kept inside the canvas horizontally.
 */
function pointLabelCandidates(
  chart: Chart<'line'>,
  sets: readonly CcPointLabelSet[],
  notAnalyzed: ReadonlyMap<number, string> | null
): PointLabel[] {
  const area = chart.chartArea;
  const ctx = chart.ctx;
  const first: PointLabel[] = [];
  const rest: PointLabel[] = [];
  for (const set of sets) {
    const meta = chart.getDatasetMeta(set.datasetIndex);
    if (!meta || meta.hidden || !chart.isDatasetVisible(set.datasetIndex)) continue;
    const data = (chart.data.datasets[set.datasetIndex]?.data ?? []) as CcChartPoint[];
    const labels: PointLabel[] = [];
    (meta.data as unknown as { x: number; y: number; skip?: boolean }[]).forEach((element, i) => {
      const point = data[i];
      if (!point || point.y === null || element.skip || !Number.isFinite(element.x) || !Number.isFinite(element.y)) return;
      const text = set.format(point.y);
      const width = ctx.measureText(text).width;
      const left = Math.min(Math.max(element.x - width / 2, 1), chart.width - width - 1);
      const aboveTop = element.y - POINT_LABEL_OFFSET - POINT_LABEL_PX;
      const belowTop = element.y + POINT_LABEL_OFFSET;
      let top = set.below ? belowTop : aboveTop;
      if (!set.below && aboveTop < area.top) top = belowTop;
      if (set.below && belowTop + POINT_LABEL_PX > area.bottom) top = aboveTop;
      labels.push({
        text, value: point.y, x: element.x, muted: notAnalyzed?.has(point.runId) ?? false,
        box: { left, top, right: left + width, bottom: top + POINT_LABEL_PX }
      });
    });
    if (labels.length === 0) continue;
    const latest = labels[labels.length - 1];
    const highest = labels.reduce((best, label) => label.value > best.value ? label : best);
    const lowest = labels.reduce((best, label) => label.value < best.value ? label : best);
    const lead = [...new Set([latest, highest, lowest])];
    first.push(...lead);
    rest.push(...labels.filter(label => !lead.includes(label)));
  }
  return [...first, ...rest.sort((a, b) => a.x - b.x)];
}

/** The kept point labels, each over a halo of the background so it reads across lines and fills. */
function drawPointLabels(
  chart: Chart<'line'>,
  sets: readonly CcPointLabelSet[],
  theme: CcChartTheme,
  notAnalyzed: ReadonlyMap<number, string> | null
): void {
  if (sets.length === 0) return;
  const ctx = chart.ctx;
  ctx.save();
  ctx.font = `600 ${POINT_LABEL_PX}px ${theme.fontFamily}`;
  const candidates = pointLabelCandidates(chart, sets, notAnalyzed);
  const kept = ccPlaceLabels(candidates.map(label => label.box), { left: 0, top: 0, right: chart.width, bottom: chart.height });
  ctx.textAlign = 'left';
  ctx.textBaseline = 'top';
  ctx.lineJoin = 'round';
  ctx.lineWidth = 3;
  ctx.strokeStyle = theme.background ?? theme.surface;
  for (const i of kept) {
    const { text, box, muted } = candidates[i];
    ctx.strokeText(text, box.left, box.top);
    ctx.fillStyle = muted ? theme.muted : theme.text;
    ctx.fillText(text, box.left, box.top);
  }
  ctx.restore();
}

/** `#rrggbb` as `rgba(r, g, b, alpha)`; any other color is returned as it is. */
function withAlpha(color: string, alpha: number): string {
  const match = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(color);
  if (!match) return color;
  const [r, g, b] = match.slice(1).map(hex => Number.parseInt(hex, 16));
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

/** The drawn, non-skipped point elements of a dataset, split where a null leaves a gap. */
function lineRuns(chart: Chart<'line'>, datasetIndex: number): { x: number; y: number }[][] {
  const meta = chart.getDatasetMeta(datasetIndex);
  if (!meta || meta.hidden) return [];
  const runs: { x: number; y: number }[][] = [];
  let current: { x: number; y: number }[] = [];
  for (const element of meta.data as unknown as { x: number; y: number; skip?: boolean }[]) {
    if (element.skip || !Number.isFinite(element.x) || !Number.isFinite(element.y)) {
      if (current.length > 0) runs.push(current);
      current = [];
    } else {
      current.push({ x: element.x, y: element.y });
    }
  }
  if (current.length > 0) runs.push(current);
  return runs;
}

/**
 * Draws the header band, the period bands and the area wash under the datasets, and the markers and
 * end labels over them: a vertical line per marker and its tag in a small box above the plot, the
 * line dashed for an event, dotted for an annotation and dash-dotted for a served-model change, its
 * tag box filled for an event and outlined otherwise, so the kinds differ by shape, not by color.
 *
 * The tags sit in a band of up to `CC_TAG_MAX_ROWS` rows between the legend and the plot, laid out
 * by `ccTagRows`; the band is sized from the markers within `range` (the x axis's bounds when null)
 * before drawing, so a crowded day never covers the data. Each line starts under its own tag.
 */
export function ccOverlayPlugin(
  markers: readonly CcChartMarker[],
  bands: readonly CcPeriodBand[],
  theme: CcChartTheme,
  range: { min: number; max: number } | null = null,
  decor: CcOverlayDecor = {}
): Plugin<'line'> {
  const dash: Record<CcMarkerKind, number[]> = { event: [6, 4], annotation: [2, 3], served: [8, 3, 2, 3] };
  const color: Record<CcMarkerKind, string> = { event: theme.event, annotation: theme.annotation, served: theme.served };
  const font = `bold 10px ${theme.fontFamily}`;
  return {
    id: 'ccOverlay',
    beforeLayout(chart) {
      const header = headerBandOf(chart);
      header.header = decor.header ?? null;
      header.logo = decor.logo ?? null;
      header.theme = theme;
      const band = tagBandOf(chart);
      band.markers = markers;
      band.range = range;
      band.font = font;
    },
    stop(chart) {
      removeBands(chart);
    },
    beforeDraw(chart) {
      if (!theme.background) return;
      const { ctx, width, height } = chart;
      ctx.save();
      ctx.fillStyle = theme.background;
      ctx.fillRect(0, 0, width, height);
      ctx.restore();
    },
    beforeDatasetsDraw(chart) {
      const x = chart.scales['x'];
      const area = chart.chartArea;
      if (!x || !area) return;
      const { ctx } = chart;
      ctx.save();
      for (const band of bands) {
        const left = Math.max(area.left, x.getPixelForValue(band.start));
        const right = Math.min(area.right, x.getPixelForValue(band.end));
        if (right <= left) continue;
        ctx.fillStyle = band.name === 'Baseline' ? theme.baselineBand : theme.comparisonBand;
        ctx.fillRect(left, area.top, right - left, area.bottom - area.top);
        ctx.fillStyle = theme.muted;
        ctx.font = `11px ${theme.fontFamily}`;
        ctx.textBaseline = 'top';
        ctx.textAlign = 'left';
        ctx.fillText(band.name, left + 6, area.top + 4);
      }
      const wash = decor.wash;
      if (wash) {
        const gradient = ctx.createLinearGradient(0, area.top, 0, area.bottom);
        gradient.addColorStop(0, withAlpha(wash.color, 0.14));
        gradient.addColorStop(1, withAlpha(wash.color, 0));
        ctx.fillStyle = gradient;
        for (const run of lineRuns(chart, wash.datasetIndex).filter(points => points.length > 1)) {
          ctx.beginPath();
          ctx.moveTo(run[0].x, area.bottom);
          for (const point of run) ctx.lineTo(point.x, point.y);
          ctx.lineTo(run[run.length - 1].x, area.bottom);
          ctx.closePath();
          ctx.fill();
        }
      }
      ctx.restore();
    },
    afterDatasetsDraw(chart) {
      const x = chart.scales['x'];
      const area = chart.chartArea;
      if (!x || !area) return;
      const { ctx } = chart;
      ctx.save();
      ctx.font = font;
      ctx.textBaseline = 'middle';
      ctx.textAlign = 'center';
      const placed = markers
        .map(marker => ({ marker, px: x.getPixelForValue(marker.x) }))
        .filter(({ px }) => Number.isFinite(px) && px >= area.left && px <= area.right)
        .map(entry => ({ ...entry, width: tagWidth(ctx, entry.marker.tag) }));
      // In the band above the plot when one is laid out; otherwise inside the plot's top edge.
      const reserved = TAG_BANDS.get(chart)?.rows ?? 0;
      const { row } = ccTagRows(placed.map(entry => ({ center: entry.px, width: entry.width })),
        reserved > 0 ? reserved : CC_TAG_MAX_ROWS);
      const tagTop = (r: number) => reserved > 0
        ? area.top - (reserved - r) * CC_TAG_ROW_HEIGHT
        : area.top + r * CC_TAG_ROW_HEIGHT;
      // The lines first, so every tag box covers the lines of the rows above it.
      ctx.lineWidth = 1.5;
      placed.forEach(({ marker, px }, i) => {
        ctx.strokeStyle = color[marker.kind];
        ctx.setLineDash(dash[marker.kind]);
        ctx.beginPath();
        ctx.moveTo(px, tagTop(row[i]) + TAG_BOX_HEIGHT);
        ctx.lineTo(px, area.bottom);
        ctx.stroke();
      });
      ctx.setLineDash([]);
      ctx.lineWidth = 1;
      placed.forEach(({ marker, px, width }, i) => {
        const top = tagTop(row[i]);
        const filled = marker.kind === 'event';
        ctx.strokeStyle = color[marker.kind];
        ctx.fillStyle = filled ? color[marker.kind] : theme.background ?? theme.surface;
        ctx.fillRect(px - width / 2, top, width, TAG_BOX_HEIGHT);
        ctx.strokeRect(px - width / 2, top, width, TAG_BOX_HEIGHT);
        ctx.fillStyle = filled ? theme.background ?? theme.surface : color[marker.kind];
        ctx.fillText(marker.tag, px, top + TAG_BOX_HEIGHT / 2);
      });
      ctx.restore();
      drawPointLabels(chart, decor.pointLabels ?? [], theme, decor.notAnalyzed ?? null);
    }
  };
}

// --- Value axes ---

/** How a measure's value axis is bounded. */
export interface CcAxisPolicy {
  /** Lowest and highest value the measure can take; null where unbounded. */
  floor: number | null;
  ceiling: number | null;
  /** The axis starts at zero (ratio measures, and the Intelligence axis on request). */
  zeroBased: boolean;
  /** The least span the axis shows, in the measure's unit, so noise cannot fill the plot. */
  minSpan: number;
  /** The tick steps allowed; any of {1, 2, 2.5, 5} × 10ᵏ when absent. */
  steps?: readonly number[];
}

/** A value axis's bounds and the step between its ticks. */
export interface CcValueAxis {
  min: number;
  max: number;
  stepSize: number;
}

/** About this many tick intervals on a value axis. */
const VALUE_AXIS_INTERVALS = 5;
const NICE_MANTISSAS: readonly number[] = [1, 2, 2.5, 5, 10];

/** Rounds away the binary noise of decimal steps. */
function clean(value: number): number {
  return Number(value.toFixed(10));
}

/** The smallest step of {1, 2, 2.5, 5} × 10ᵏ, or of `steps`, at least `target`; `steps`' largest when none is. */
function niceStep(target: number, steps?: readonly number[]): number {
  if (steps && steps.length > 0) {
    const sorted = [...steps].sort((a, b) => a - b);
    return sorted.find(step => step >= target - 1e-9) ?? sorted[sorted.length - 1];
  }
  const power = 10 ** Math.floor(Math.log10(target));
  return clean(power * NICE_MANTISSAS.find(m => m * power >= target - 1e-9 * power)!);
}

/** The decimals that write every multiple of `step` exactly: 0 for 5, 1 for 2.5 or 0.2, 3 for 0.002. */
export function ccStepDecimals(step: number): number {
  for (let digits = 0; digits < 10; digits++) {
    const scaled = step * 10 ** digits;
    if (Math.abs(scaled - Math.round(scaled)) < 1e-6) return digits;
  }
  return 10;
}

/**
 * A value axis for `values` under `policy`. From the values padded by 15 % of their span (at least
 * 5 % of `minSpan`), or from zero for a zero-based measure, clamped to the floor and ceiling; a step
 * for about five intervals; both bounds snapped outward to the step; then widened step by step to
 * `minSpan` (alternately down and up for a bounded score, starting down; up for a zero-based measure;
 * the other side where a bound stops it). Without values: `minSpan` from zero or the floor.
 */
export function ccValueAxis(values: readonly number[], policy: CcAxisPolicy): CcValueAxis {
  const { floor, ceiling, zeroBased, minSpan } = policy;
  const finite = values.filter(Number.isFinite);
  let lo: number;
  let hi: number;
  if (finite.length === 0) {
    lo = zeroBased ? 0 : floor ?? 0;
    hi = lo + minSpan;
  } else {
    const dataMin = Math.min(...finite);
    const dataMax = Math.max(...finite);
    const pad = Math.max((dataMax - dataMin) * 0.15, minSpan * 0.05);
    lo = zeroBased ? Math.min(0, dataMin < 0 ? dataMin - pad : 0) : dataMin - pad;
    hi = dataMax + pad;
  }
  if (floor !== null) lo = Math.max(lo, floor);
  if (ceiling !== null) hi = Math.min(hi, ceiling);
  const room = floor !== null && ceiling !== null ? ceiling - floor : Number.POSITIVE_INFINITY;
  const span = Math.min(Math.max(hi - lo, minSpan), room);
  const stepSize = niceStep(span / VALUE_AXIS_INTERVALS, policy.steps);

  lo = clean(Math.floor(lo / stepSize + 1e-9) * stepSize);
  hi = clean(Math.ceil(hi / stepSize - 1e-9) * stepSize);
  if (floor !== null) lo = Math.max(lo, floor);
  if (ceiling !== null) hi = Math.min(hi, ceiling);
  if (!(hi > lo)) hi = clean(lo + stepSize);

  let down = !zeroBased;
  while (hi - lo < Math.min(minSpan, room) - 1e-9) {
    const canDown = !zeroBased && (floor === null || lo - stepSize >= floor - 1e-9);
    const canUp = ceiling === null || hi + stepSize <= ceiling + 1e-9;
    if (!canDown && !canUp) break;
    if ((down && canDown) || !canUp) {
      lo = clean(lo - stepSize);
    } else {
      hi = clean(hi + stepSize);
    }
    down = !down;
  }
  return { min: lo, max: hi, stepSize };
}

/** A tick in plain numbers, with the decimals its step needs. */
function numberTick(value: number, axis: CcValueAxis): string {
  return formatFixed(value, ccStepDecimals(axis.stepSize));
}

/** A tick with thousands separators while the step is whole. */
function integerTick(value: number, axis: CcValueAxis): string {
  return ccStepDecimals(axis.stepSize) === 0 ? formatInteger(value) : numberTick(value, axis);
}

/** A tick of a millisecond axis: in seconds once the axis reaches one, else in milliseconds. */
function msTick(value: number, axis: CcValueAxis): string {
  return axis.max >= 1000
    ? `${formatFixed(value / 1000, ccStepDecimals(axis.stepSize / 1000))} s`
    : `${formatFixed(value, ccStepDecimals(axis.stepSize))} ms`;
}

/** A tick of a US dollar axis: at least cents, more where the step needs them. */
function usdTick(value: number, axis: CcValueAxis): string {
  return `${value < 0 ? MINUS : ''}$${Math.abs(value).toFixed(Math.max(2, ccStepDecimals(axis.stepSize)))}`;
}

/** A ratio measure's axis: from zero, so a point's height is proportional to its value. */
function ratioPolicy(minSpan: number, ceiling: number | null = null): CcAxisPolicy {
  return { floor: 0, ceiling, zeroBased: true, minSpan };
}

/** The Intelligence axis: a window of at least 20 points inside 0–100, or the full scale. */
function intelligencePolicy(fullScale: boolean): CcAxisPolicy {
  return fullScale
    ? { floor: 0, ceiling: 100, zeroBased: true, minSpan: 100, steps: [10] }
    : { floor: 0, ceiling: 100, zeroBased: false, minSpan: 20, steps: [5, 10] };
}

// --- Options ---

interface AxisSpec {
  title: string;
  /** A tick label, its decimals following the axis's step. */
  tick: (value: number, axis: CcValueAxis) => string;
  /** A value as the data table writes it: the tooltip and the point labels, unless a dataset has its own. */
  format: (value: number) => string;
  /** The axis's bounds; null leaves them to the figure (the overview's fixed axis). */
  policy: CcAxisPolicy | null;
}

function xRange(series: readonly CcChartPoint[][], markers: readonly CcChartMarker[], bands: readonly CcPeriodBand[]):
  { min: number; max: number } | null {
  const xs = [
    ...series.flat().filter(point => point.y !== null).map(point => point.x),
    ...bands.flatMap(band => [band.start, band.end])
  ].filter(Number.isFinite);
  if (xs.length === 0) return null;
  let min = Math.min(...xs);
  let max = Math.max(...xs);
  for (const marker of markers) {
    if (marker.x >= min - 7 * 86_400_000 && marker.x <= max + 7 * 86_400_000) {
      min = Math.min(min, marker.x);
      max = Math.max(max, marker.x);
    }
  }
  const pad = Math.max((max - min) * 0.03, 12 * 3_600_000);
  return { min: min - pad, max: max + pad };
}

/** The points not in the analysis and the plain point look of each drawn dataset, for the legend. */
interface NotAnalyzedMarks {
  notAnalyzed: ReadonlyMap<number, string>;
  looks: readonly PointLook[];
}

/**
 * The legend items with each dataset's plain point look: the default items take the look of the
 * first point, which can be a point not in the analysis.
 */
function plainPointLegend(looks: readonly PointLook[]): (chart: Chart) => LegendItem[] {
  return chart => Chart.defaults.plugins.legend.labels.generateLabels(chart).map((item): LegendItem => {
    const look = item.datasetIndex === undefined ? undefined : looks[item.datasetIndex];
    return look
      ? {
        ...item, pointStyle: look.pointStyle, fillStyle: look.pointBackgroundColor, strokeStyle: look.pointBorderColor,
        lineWidth: look.pointBorderWidth
      }
      : item;
  });
}

/** What the tooltip says about a point: its series' short name and its value in the table's format. */
interface TooltipContext {
  input: CcFigureInput;
  /** Series id → the tooltip's name and value format of that series. */
  series: ReadonlyMap<string, { name: string; format: (value: number) => string }>;
  marks: NotAnalyzedMarks | null;
  /** The chart draws more than one series, so each line carries its series color. */
  colors: boolean;
}

function chartOptions(
  theme: CcChartTheme,
  reducedMotion: boolean,
  range: { min: number; max: number } | null,
  tooltip: TooltipContext,
  y: AxisSpec,
  yAxis: CcValueAxis | null,
  legend: boolean,
  paddingRight: number
): ChartOptions<'line'> {
  const tick = { color: theme.muted, font: { family: theme.fontFamily, size: 11 } };
  const axisTitle = { display: true, color: theme.secondary, font: { family: theme.fontFamily, size: 12, weight: 600 } };
  const { series, marks } = tooltip;
  const tooltipBox = theme.tooltip
    ? {
      backgroundColor: theme.tooltip.background,
      borderColor: theme.tooltip.border,
      borderWidth: 1,
      cornerRadius: 6,
      padding: { x: 8, y: 6 },
      caretSize: 5,
      titleMarginBottom: 3,
      titleFont: { family: theme.fontFamily, size: 11, weight: 'bold' as const },
      bodyFont: { family: theme.fontFamily, size: 11 }
    }
    : {};
  const options: ChartOptions<'line'> = {
    responsive: true,
    maintainAspectRatio: false,
    animation: reducedMotion ? false : { duration: 250 },
    // On a point only (its hit radius keeps it easy to reach), never anywhere over the plot.
    interaction: { mode: 'nearest', intersect: true, axis: 'xy' },
    layout: { padding: { top: 4, right: paddingRight } },
    scales: {
      x: {
        type: 'linear',
        ...(range ? { min: range.min, max: range.max } : {}),
        afterBuildTicks: (scale: Scale) => {
          const ticks = ccTimeTicks(scale.min, scale.max, maxTicksFor(scale.width));
          if (ticks.values.length > 0) scale.ticks = ticks.values.map(value => ({ value }));
        },
        ticks: {
          ...tick,
          autoSkip: false,
          maxRotation: 0,
          callback(this: Scale, value: string | number, index: number, ticks: Tick[]) {
            const step = ticks.length > 1 ? ticks[1].value - ticks[0].value : 0;
            return ccTimeTickLabel(Number(value), index > 0 ? ticks[index - 1].value : null, step, this.max - this.min);
          }
        },
        grid: { display: false },
        border: { display: false },
        title: { ...axisTitle, text: `${capitalized(nounOf(tooltip.input))} start (UTC)` }
      },
      y: {
        type: 'linear',
        ...(yAxis ? { min: yAxis.min, max: yAxis.max } : {}),
        ticks: {
          ...tick,
          ...(yAxis ? { stepSize: yAxis.stepSize } : {}),
          callback: value => yAxis ? y.tick(Number(value), yAxis) : ''
        },
        grid: { color: theme.grid, lineWidth: 1 },
        border: { display: false },
        title: { ...axisTitle, text: y.title }
      }
    },
    plugins: {
      legend: {
        display: legend,
        align: 'start',
        labels: {
          color: theme.text,
          font: { family: theme.fontFamily, size: 12 },
          usePointStyle: true,
          boxWidth: 8,
          boxHeight: 8,
          padding: 12,
          ...(marks ? { generateLabels: plainPointLegend(marks.looks) } : {})
        }
      },
      tooltip: {
        usePointStyle: true,
        displayColors: tooltip.colors,
        boxWidth: 6,
        boxHeight: 6,
        ...tooltipBox,
        callbacks: {
          title: (items: TooltipItem<'line'>[]) => {
            const raw = items[0]?.raw as CcChartPoint | undefined;
            return raw ? `#${raw.runId} · ${formatUtcDateTime(raw.x)}` : '';
          },
          label: (item: TooltipItem<'line'>) => {
            const raw = item.raw as CcChartPoint;
            const id = (item.dataset as Partial<CcChartDataset>).seriesId ?? '';
            const entry = series.get(id);
            const name = entry?.name ?? item.dataset.label ?? id;
            const text = `${name}: ${raw.y === null ? '—' : (entry?.format ?? y.format)(raw.y)}`;
            const reason = marks?.notAnalyzed.get(raw.runId);
            return reason === undefined ? text : [text, `Not in the analysis: ${reason}`];
          }
        }
      }
    }
  };
  return options;
}

interface DatasetSpec {
  /** The series id from `CC_FIGURE_SERIES`. */
  id: string;
  label: string;
  data: CcChartPoint[];
  color: string;
  hollow?: boolean;
  pointStyle?: PointStyle;
  /** The value as the data table writes it, for the tooltip and the point labels; the axis's when absent. */
  format?: (value: number) => string;
}

/** A dataset's own point look: a solid point ringed in the surface color, or a hollow one filled with it. */
interface PointLook {
  pointStyle: PointStyle;
  pointBackgroundColor: string;
  pointBorderColor: string;
  pointBorderWidth: number;
  pointRadius: number;
}

function pointLook(spec: DatasetSpec, theme: CcChartTheme): PointLook {
  return {
    pointStyle: spec.pointStyle ?? 'circle',
    pointBackgroundColor: spec.hollow ? theme.surface : spec.color,
    pointBorderColor: spec.hollow ? spec.color : theme.surface,
    pointBorderWidth: 2,
    pointRadius: 4
  };
}

function dataset(spec: DatasetSpec, theme: CcChartTheme): CcChartDataset {
  return {
    seriesId: spec.id,
    label: spec.label,
    data: spec.data,
    yAxisID: 'y',
    borderColor: spec.color,
    backgroundColor: spec.color,
    ...pointLook(spec, theme),
    pointHoverRadius: 6,
    pointHoverBorderWidth: 2,
    pointHitRadius: 12,
    borderWidth: 2,
    borderJoinStyle: 'round',
    borderCapStyle: 'round',
    borderDash: spec.hollow ? [4, 4] : [],
    spanGaps: false,
    tension: 0
  };
}

/**
 * `dataset` with each point of `notAnalyzed` drawn as a gray, unfilled, rotated cross, and each
 * segment touching one gray and dotted; the other points keep the dataset's own look, and the other
 * segments its line.
 */
function markedDataset(
  spec: DatasetSpec,
  theme: CcChartTheme,
  notAnalyzed: ReadonlyMap<number, string>
): CcChartDataset {
  const look = pointLook(spec, theme);
  const marked = (point: CcChartPoint | undefined) => point !== undefined && notAnalyzed.has(point.runId);
  const at = (ctx: ScriptableContext<'line'>) => marked(ctx.raw as CcChartPoint | undefined);
  const segmentAt = (ctx: ScriptableLineSegmentContext) =>
    marked(spec.data[ctx.p0DataIndex]) || marked(spec.data[ctx.p1DataIndex]);
  return {
    ...dataset(spec, theme),
    pointStyle: (ctx: ScriptableContext<'line'>): PointStyle => at(ctx) ? 'crossRot' : look.pointStyle,
    pointBackgroundColor: (ctx: ScriptableContext<'line'>) => at(ctx) ? 'rgba(0, 0, 0, 0)' : look.pointBackgroundColor,
    pointBorderColor: (ctx: ScriptableContext<'line'>) => at(ctx) ? theme.muted : look.pointBorderColor,
    pointBorderWidth: (ctx: ScriptableContext<'line'>) => at(ctx) ? 2 : look.pointBorderWidth,
    pointRadius: (ctx: ScriptableContext<'line'>) => at(ctx) ? 4.5 : look.pointRadius,
    segment: {
      borderColor: (ctx: ScriptableLineSegmentContext) => segmentAt(ctx) ? theme.muted : undefined,
      borderDash: (ctx: ScriptableLineSegmentContext) => segmentAt(ctx) ? [2, 3] : undefined
    }
  };
}

/** The input's points not in the analysis; null when the map is absent or empty. */
function notAnalyzedOf(input: CcFigureInput): ReadonlyMap<number, string> | null {
  return input.notAnalyzed && input.notAnalyzed.size > 0 ? input.notAnalyzed : null;
}

/** How a figure decorates its datasets. */
interface ConfigStyle {
  /** The value at every point of each drawn dataset, while at most two are drawn. */
  pointLabels?: boolean;
  /** The area under a lone, solid dataset washed with its color. */
  wash?: boolean;
}

/** About 6.5 px a character for the 600-weight, 11 px point labels. */
const POINT_LABEL_CHAR_PX = 6.5;

function config(
  datasets: DatasetSpec[],
  input: CcFigureInput,
  markers: CcChartMarker[],
  options: CcChartOptions,
  y: AxisSpec,
  style: ConfigStyle = {}
): CcChartConfig | null {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const drawn = datasets.filter(spec => !options.hiddenSeries?.has(spec.id) && spec.data.some(point => point.y !== null));
  if (drawn.length === 0) return null;
  const bands = input.bands ?? [];
  const range = xRange(drawn.map(spec => spec.data), markers, bands);
  const notAnalyzed = notAnalyzedOf(input);
  const marks = notAnalyzed ? { notAnalyzed, looks: drawn.map(spec => pointLook(spec, theme)) } : null;

  const formatOf = (spec: DatasetSpec) => spec.format ?? y.format;
  const pointLabels: CcPointLabelSet[] = style.pointLabels && drawn.length <= 2
    ? drawn.map((spec, datasetIndex) => ({ datasetIndex, format: formatOf(spec), below: datasetIndex === 1 }))
    : [];
  // Room for half the widest point label, centered on the last point, and a 4 px margin.
  const widest = Math.max(0, ...pointLabels.flatMap((set, i) =>
    valuesOf(drawn[i].data).map(value => set.format(value).length)));
  const paddingRight = Math.max(8, widest > 0 ? Math.ceil(widest * POINT_LABEL_CHAR_PX / 2) + 4 : 0);
  const wash = style.wash && drawn.length === 1 && !drawn[0].hollow ? { datasetIndex: 0, color: drawn[0].color } : null;
  // The bounds follow the drawn series, as the time range does.
  const yAxis = y.policy ? ccValueAxis(drawn.flatMap(spec => valuesOf(spec.data)), y.policy) : null;

  const series = new Map(drawn.map(spec => [spec.id, { name: seriesShortLabel(spec.id), format: formatOf(spec) }]));
  return {
    type: 'line',
    data: { datasets: drawn.map(spec => notAnalyzed ? markedDataset(spec, theme, notAnalyzed) : dataset(spec, theme)) },
    options: chartOptions(theme, options.reducedMotion ?? false, range,
      { input, series, marks, colors: drawn.length > 1 }, y, yAxis, drawn.length > 1, paddingRight),
    plugins: [ccOverlayPlugin(markers, bands, theme, range, {
      header: options.header ?? null, logo: options.logo ?? null, wash, pointLabels, notAnalyzed
    })]
  };
}

/**
 * The caption sentence on the plotted points not in the analysis, with its leading space: the points
 * with a value in any of `series`, drawn or hidden. Empty when there are none.
 */
function notAnalyzedNote(input: CcFigureInput, series: readonly (readonly CcChartPoint[])[]): string {
  const notAnalyzed = notAnalyzedOf(input);
  if (!notAnalyzed) return '';
  const units = new Set(series.flat().filter(point => point.y !== null && notAnalyzed.has(point.runId)).map(point => point.runId));
  if (units.size === 0) return '';
  return ` ${plural(units.size, nounOf(input))} not in the analysis ${units.size === 1 ? 'is drawn as a gray cross' : 'are drawn as gray crosses'}.`;
}

/**
 * `table` with the battery columns *Suites* and *Member runs* when its points are battery runs, and a
 * last column *In the analysis* while points are marked; its rows are `points`'.
 */
function finishTable(table: CcFigureTable, points: readonly CcTimelinePoint[], input: CcFigureInput): CcFigureTable {
  let result = table;
  if (unitKindOf(input) === 'batteryRun') {
    result = {
      columns: [...result.columns, 'Suites', 'Member runs'],
      rows: result.rows.map((cells, i) => {
        const battery = batteryPointOf(points[i]);
        return battery
          ? [...cells, String(battery.suiteCount), membersText(battery, input.memberLabels)]
          : [...cells, '—', '—'];
      }),
      lists: {
        ...result.lists,
        'Member runs': points.map(point => {
          const battery = batteryPointOf(point);
          return battery ? membersList(battery, input.memberLabels) : [];
        })
      }
    };
  }
  const notAnalyzed = notAnalyzedOf(input);
  if (!notAnalyzed) return result;
  return {
    ...(result.lists ? { lists: result.lists } : {}),
    columns: [...result.columns, 'In the analysis'],
    rows: result.rows.map((cells, i) => {
      const reason = notAnalyzed.get(points[i].runId);
      return [...cells, reason === undefined ? 'Yes' : `No — ${reason}`];
    })
  };
}

/** A dataset of `CC_FIGURE_SERIES[key]`, labeled from it unless `extra` names it. */
function seriesSpec(key: CcFigureKey, id: string, data: CcChartPoint[], color: string, extra: Partial<DatasetSpec> = {}): DatasetSpec {
  return { id, label: seriesLabel(key, id), data, color, ...extra };
}

function rangeText(values: readonly number[], format: (value: number) => string): { min: string; max: string } {
  return { min: format(Math.min(...values)), max: format(Math.max(...values)) };
}

/** The first two columns of a figure's table: the unit and its start. */
function leadColumns(input: CcFigureInput): string[] {
  return [capitalized(nounOf(input)), 'Started'];
}

function row(point: CcTimelinePoint, ...cells: string[]): string[] {
  return [`#${point.runId}`, formatUtcDateTime(point.startedAtUtc), ...cells];
}

/** The markers a figure draws: the input's, through its marker filter. */
function markersOf(points: readonly CcTimelinePoint[], input: CcFigureInput): CcChartMarker[] {
  return buildMarkers(points, input.events, input.annotations, input.markerFilter, eventContextOf(input));
}

function eventContextOf(input: CcFigureInput): CcEventContext {
  return { harnessPoints: input.harnessPoints, numbering: input.eventNumbering };
}

/** `Intelligence per run.` or `Intelligence per battery run.`, the alt text's lead. */
function altLead(what: string, input: CcFigureInput): string {
  return `${what} per ${nounOf(input)}.`;
}

/** The first sentence of an Overall Index note, lowercased and without its period: the takeaway's reason. */
function noteReason(note: string): string {
  const sentence = note.split('. ')[0].replace(/\.$/, '').trim();
  return sentence.charAt(0).toLowerCase() + sentence.slice(1);
}

// --- The figures ---

/**
 * Intelligence per point: a run's Intelligence Index from its native grades, a battery run's Overall
 * Intelligence Index from its battery analysis, and, where one covers points, the common grader's mean.
 */
export function qualityFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = markersOf(points, input);
  const battery = unitKindOf(input) === 'batteryRun';
  const noun = nounOf(input);
  const primaryId = battery ? 'quality.overall' : 'quality.native';
  const primary = seriesOf(points, point => battery
    ? ccNumber(batteryPointOf(point)?.overallIndex ?? null)
    : point.qualityIndex ?? null);
  const grader = dominantCommonGrader(points);
  const common = grader ? seriesOf(points, point => commonGraderQualityOf(point, grader.snapshotId)) : [];
  const commonLabel = grader ? `Intelligence, common grader (${grader.display})` : '';
  // The table's precision: a battery run's Overall Index and the common grader to one decimal.
  const places = options.decimals?.quality;
  const primaryFormat = (value: number | null) => formatFixed(value, places ?? (battery ? 1 : 0));
  const commonFormat = (value: number | null) => formatFixed(value, places ?? 1);

  const primaryValues = valuesOf(primary);
  const commonValues = valuesOf(common);
  const values = primaryValues.length > 0 ? primaryValues : commonValues;
  const format = primaryValues.length > 0 ? primaryFormat : commonFormat;
  const which = primaryValues.length > 0
    ? (battery ? 'The Overall Intelligence Index' : 'The Intelligence Index')
    : 'Common-grader intelligence';
  let takeaway: string;
  if (values.length === 0) {
    takeaway = `No ${noun} in this range has an Intelligence figure.`;
  } else if (values.length === 1) {
    takeaway = `${which} was ${format(values[0])} in the one ${noun} of this range.`;
  } else {
    const { min, max } = rangeText(values, format);
    const spread = Math.max(...values) - Math.min(...values);
    takeaway = min === max
      ? `${which} held at ${min} across ${plural(values.length, noun)}.`
      : spread <= 3
        ? `${which} held between ${min} and ${max} across ${plural(values.length, noun)}.`
        : `${which} ranged from ${min} to ${max} across ${plural(values.length, noun)}; the latest ${noun} scored ${format(values[values.length - 1])}.`;
  }
  if (battery) {
    const missing = points.map(batteryPointOf).filter(point => point !== null && ccNumber(point.overallIndex) === null);
    if (missing.length > 0) {
      const reasons = [...new Set(missing.map(point => noteReason(point!.overallIndexNote ?? 'no battery analysis')))];
      takeaway += ` ${plural(missing.length, noun)} ${missing.length === 1 ? 'has' : 'have'} no Overall Intelligence Index: ${reasons.join('; ')}.`;
    }
  }
  if (grader && primaryValues.length > 0 && commonValues.length > 0) {
    takeaway += ` ${plural(commonValues.length, noun)} also ${commonValues.length === 1 ? 'has' : 'have'} a common-grader figure.`;
  }
  takeaway += notAnalyzedNote(input, [primary, common]);

  const datasets: DatasetSpec[] = [seriesSpec('quality', primaryId, primary, theme.series[0], { format: primaryFormat })];
  if (grader) datasets.push(seriesSpec('quality', 'quality.common', common, theme.series[1], { label: commonLabel, format: commonFormat }));
  const columns = [...leadColumns(input), battery ? 'Overall Intelligence Index' : 'Intelligence Index (native)'];
  if (grader) columns.push(`Common grader (${grader.display})`);
  if (battery) columns.push('Note');
  return {
    key: 'quality',
    title: ccFigureTitle('quality'),
    takeaway,
    altText: `${altLead('Intelligence', input)} ${takeaway}`,
    config: config(datasets, input, markers, options,
      { title: 'Intelligence (0–100)', tick: numberTick, format: primaryFormat, policy: intelligencePolicy(options.zeroBaseline ?? false) },
      { pointLabels: true, wash: true }),
    table: finishTable({
      columns,
      rows: points.map((point, i) => {
        const cells = [primaryFormat(primary[i].y)];
        if (grader) cells.push(commonFormat(common[i].y));
        if (battery) cells.push(batteryPointOf(point)?.overallIndexNote ?? '');
        return row(point, ...cells);
      })
    }, points, input),
    markers
  };
}

/** Median time to first answer text per point; legacy points as their model-time proxy, hollow. */
export function timeToFirstAnswerFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = markersOf(points, input);
  const noun = nounOf(input);
  const telemetry = seriesOf(points, point => ccNumber(point.medianTimeToFirstAnswerTextMs));
  const proxy = seriesOf(points, point => ccNumber(point.medianTimeToFirstAnswerTextMs) === null
    ? ccNumber(point.medianModelTimeMs) : null);
  const telemetryValues = valuesOf(telemetry);
  const proxyValues = valuesOf(proxy);
  const places = options.decimals?.ttfat ?? 1;
  const ms = (value: number | null) => formatMs(value, places);

  let takeaway: string;
  if (telemetryValues.length === 0 && proxyValues.length === 0) {
    takeaway = `No ${noun} in this range has a latency figure.`;
  } else if (telemetryValues.length === 0) {
    const { min, max } = rangeText(proxyValues, ms);
    takeaway = `Only the legacy proxy is available: model time per answer ranged from ${min} to ${max} across ${plural(proxyValues.length, noun)}.`;
  } else {
    const { min, max } = rangeText(telemetryValues, ms);
    takeaway = telemetryValues.length === 1
      ? `Median time to first answer text was ${min} in the one telemetry ${noun}.`
      : `Median time to first answer text ranged from ${min} to ${max} across ${plural(telemetryValues.length, `telemetry ${noun}`)}.`;
    if (proxyValues.length > 0) {
      takeaway += ` ${plural(proxyValues.length, `legacy ${noun}`)} ${proxyValues.length === 1 ? 'is' : 'are'} drawn hollow as the legacy proxy.`;
    }
  }
  takeaway += notAnalyzedNote(input, [telemetry, proxy]);
  return {
    key: 'ttfat',
    title: ccFigureTitle('ttfat'),
    takeaway,
    altText: `${altLead('Time to first answer text', input)} ${takeaway}`,
    config: config([
      seriesSpec('ttfat', 'ttfat.telemetry', telemetry, theme.series[1]),
      seriesSpec('ttfat', 'ttfat.proxy', proxy, theme.series[2], { hollow: true })
    ], input, markers, options, { title: 'Median time', tick: msTick, format: ms, policy: ratioPolicy(1000) },
    { pointLabels: true, wash: true }),
    table: finishTable({
      columns: [...leadColumns(input), 'Time to first answer text', 'Legacy proxy', 'Measure'],
      rows: points.map((point, i) => row(point, ms(telemetry[i].y), ms(proxy[i].y),
        point.latencyLabel || (point.isLegacy ? 'legacy proxy' : 'telemetry')))
    }, points, input),
    markers
  };
}

/** Median answer streaming rate per point; an estimated rate is drawn hollow. */
export function streamingRateFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = markersOf(points, input);
  const noun = nounOf(input);
  const measured = seriesOf(points, point => point.streamingRateEstimated ? null : ccNumber(point.medianStreamingRate));
  const estimated = seriesOf(points, point => point.streamingRateEstimated ? ccNumber(point.medianStreamingRate) : null);
  const values = [...valuesOf(measured), ...valuesOf(estimated)];
  const legacy = points.filter(point => point.isLegacy).length;
  const places = options.decimals?.rate ?? 1;
  const rate = (value: CcTimelinePoint['medianStreamingRate']) => formatTokenRate(value, places);

  let takeaway: string;
  if (values.length === 0) {
    takeaway = `No ${noun} in this range has a streaming rate; legacy ${noun}s did not record one.`;
  } else {
    const { min, max } = rangeText(values, rate);
    takeaway = values.length === 1
      ? `The answer streaming rate was ${min} in the one ${noun} that recorded it.`
      : `The answer streaming rate ranged from ${min} to ${max} across ${plural(values.length, noun)}.`;
    if (legacy > 0) takeaway += ` ${plural(legacy, `legacy ${noun}`)} recorded no rate.`;
  }
  takeaway += notAnalyzedNote(input, [measured, estimated]);
  return {
    key: 'rate',
    title: ccFigureTitle('rate'),
    takeaway,
    altText: `${altLead('Answer streaming rate', input)} ${takeaway}`,
    config: config([
      seriesSpec('rate', 'rate.measured', measured, theme.series[3]),
      seriesSpec('rate', 'rate.estimated', estimated, theme.series[3], { hollow: true })
    ], input, markers, options, { title: 'Tokens per second', tick: numberTick, format: rate, policy: ratioPolicy(10) },
    { pointLabels: true, wash: true }),
    table: finishTable({
      columns: [...leadColumns(input), 'Streaming rate', 'Estimated'],
      rows: points.map(point => row(point, rate(point.medianStreamingRate), point.streamingRateEstimated ? 'Yes' : 'No'))
    }, points, input),
    markers
  };
}

/** Output tokens per answer. */
export function workFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = markersOf(points, input);
  const noun = nounOf(input);
  const tokens = seriesOf(points, point => ccNumber(point.outputTokensPerAnswer));
  const values = valuesOf(tokens);
  const places = options.decimals?.work;
  const format = places === undefined ? formatInteger : (value: number | null) => formatGrouped(value, places);
  let takeaway: string;
  if (values.length === 0) {
    takeaway = `No ${noun} in this range has an output-token figure.`;
  } else if (values.length === 1) {
    takeaway = `Output tokens per answer were ${format(values[0])} in the one ${noun} of this range.`;
  } else {
    const { min, max } = rangeText(values, format);
    takeaway = `Output tokens per answer ranged from ${min} to ${max} across ${plural(values.length, noun)}.`;
  }
  takeaway += notAnalyzedNote(input, [tokens]);
  return {
    key: 'work',
    title: ccFigureTitle('work'),
    takeaway,
    altText: `${altLead('Output tokens per answer', input)} ${takeaway}`,
    config: config([seriesSpec('work', 'work.tokens', tokens, theme.series[4])], input, markers, options,
      { title: 'Output tokens', tick: integerTick, format, policy: ratioPolicy(100) }, { pointLabels: true, wash: true }),
    table: finishTable({
      columns: [...leadColumns(input), 'Output tokens per answer'],
      rows: points.map((point, i) => row(point, format(tokens[i].y)))
    }, points, input),
    markers
  };
}

/** Tool calls per answer. */
export function toolCallsFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = markersOf(points, input);
  const noun = nounOf(input);
  const calls = seriesOf(points, point => ccNumber(point.toolCallsPerAnswer));
  const values = valuesOf(calls);
  const places = options.decimals?.tools ?? 1;
  const format = (value: number | null) => formatFixed(value, places);
  let takeaway: string;
  if (values.length === 0) {
    takeaway = `No ${noun} in this range has a tool-call figure.`;
  } else if (values.length === 1) {
    takeaway = `Tool calls per answer were ${format(values[0])} in the one ${noun} of this range.`;
  } else {
    const { min, max } = rangeText(values, format);
    takeaway = `Tool calls per answer ranged from ${min} to ${max} across ${plural(values.length, noun)}.`;
  }
  takeaway += notAnalyzedNote(input, [calls]);
  return {
    key: 'tools',
    title: ccFigureTitle('tools'),
    takeaway,
    altText: `${altLead('Tool calls per answer', input)} ${takeaway}`,
    config: config([seriesSpec('tools', 'tools.calls', calls, theme.series[2])], input, markers, options,
      { title: 'Tool calls', tick: numberTick, format, policy: ratioPolicy(1) }, { pointLabels: true, wash: true }),
    table: finishTable({
      columns: [...leadColumns(input), 'Tool calls per answer'],
      rows: points.map((point, i) => row(point, format(calls[i].y)))
    }, points, input),
    markers
  };
}

/** Candidate cost per question. */
export function costFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = markersOf(points, input);
  const noun = nounOf(input);
  const cost = seriesOf(points, point => ccNumber(point.costPerQuestionUsd));
  const values = valuesOf(cost);
  const places = options.decimals?.cost;
  const usd = (value: number | null) => formatUsd(value, places);
  let takeaway: string;
  if (values.length === 0) {
    takeaway = `No ${noun} in this range has a cost figure.`;
  } else if (values.length === 1) {
    takeaway = `Cost per question was ${usd(values[0])} in the one ${noun} of this range.`;
  } else {
    const { min, max } = rangeText(values, usd);
    takeaway = `Cost per question ranged from ${min} to ${max} across ${plural(values.length, noun)}, at one price card.`;
  }
  takeaway += notAnalyzedNote(input, [cost]);
  return {
    key: 'cost',
    title: ccFigureTitle('cost'),
    takeaway,
    altText: `${altLead('Cost per question', input)} ${takeaway}`,
    config: config([seriesSpec('cost', 'cost.cost', cost, theme.series[5])], input, markers, options,
      { title: 'USD per question', tick: usdTick, format: usd, policy: ratioPolicy(0.01) }, { pointLabels: true, wash: true }),
    table: finishTable({
      columns: [...leadColumns(input), 'Cost per question'],
      rows: points.map((point, i) => row(point, usd(cost[i].y)))
    }, points, input),
    markers
  };
}

/** The five reliability rates per point, as percentages, each with its own point shape. */
export function reliabilityFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = markersOf(points, input);
  const noun = nounOf(input);
  const series = RELIABILITY_RATES.map(rate => ({
    ...rate,
    data: seriesOf(points, point => {
      const value = ccNumber(point[rate.key] as number | string | null);
      return value === null ? null : value * 100;
    })
  }));

  let worst: { label: string; value: number; runId: number } | null = null;
  for (const rate of series) {
    for (const point of rate.data) {
      if (point.y !== null && point.y > 0 && (!worst || point.y > worst.value)) {
        worst = { label: rate.label, value: point.y, runId: point.runId };
      }
    }
  }
  const counted = points.filter(point => RELIABILITY_RATES.some(rate => ccNumber(point[rate.key] as number | string | null) !== null)).length;
  const places = options.decimals?.reliability ?? 1;
  const percent = (value: number) => `${formatFixed(value, places)} %`;
  const takeaway = (counted === 0
    ? `No ${noun} in this range has reliability figures.`
    : worst === null
      ? `No terminal failures, timeouts, empty answers, refusals or exhausted tool budgets in ${plural(counted, noun)}.`
      : `The highest rate was ${worst.label.toLowerCase()} at ${percent(worst.value)} in ${noun} #${worst.runId}, across ${plural(counted, noun)}.`)
    + notAnalyzedNote(input, series.map(rate => rate.data));
  return {
    key: 'reliability',
    title: ccFigureTitle('reliability'),
    takeaway,
    altText: `${altLead('Reliability rates', input)} ${takeaway}`,
    config: config(series.map((rate, i) =>
      seriesSpec('reliability', `reliability.${rate.key}`, rate.data, theme.series[i % theme.series.length], { pointStyle: rate.pointStyle })),
      input, markers, options, {
        title: 'Share of answers (%)',
        tick: (value, axis) => `${numberTick(value, axis)} %`,
        format: percent,
        policy: ratioPolicy(10, 100)
      }),
    table: finishTable({
      columns: [...leadColumns(input), ...RELIABILITY_RATES.map(rate => rate.label)],
      rows: points.map(point => row(point,
        ...RELIABILITY_RATES.map(rate => formatFractionPercent(point[rate.key] as number | string | null, places))))
    }, points, input),
    markers
  };
}

/**
 * The points on one line over time, with every marker: what happened when, for the reports. The
 * takeaway counts every marker of the range, whatever the marker filter draws.
 */
export function timelineOverviewFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = markersOf(points, input);
  const noun = nounOf(input);
  const every = input.markerFilter
    ? buildMarkers(points, input.events, input.annotations, undefined, eventContextOf(input))
    : markers;
  const telemetry = seriesOf(points, point => point.isLegacy ? null : 1);
  const legacy = seriesOf(points, point => point.isLegacy ? 1 : null);
  const count = (kind: CcMarkerKind) => every.filter(marker => marker.kind === kind).length;
  const takeaway = (points.length === 0
    ? `No ${noun} in this range.`
    : `${plural(points.length, noun)}, ${plural(count('event'), 'Overseer change')}, ${plural(count('annotation'), 'annotation')} `
      + `and ${plural(count('served'), 'served-model change')} in this range.`) + notAnalyzedNote(input, [telemetry, legacy]);
  const built = config([
    seriesSpec('timeline', 'timeline.telemetry', telemetry, theme.series[0]),
    seriesSpec('timeline', 'timeline.legacy', legacy, theme.series[0], { hollow: true })
  ], input, markers, options, { title: '', tick: () => '', format: () => '', policy: null });
  if (built) {
    for (const ds of built.data.datasets) {
      (ds as { showLine?: boolean }).showLine = false;
    }
    const y = built.options.scales?.['y'];
    if (y) {
      Object.assign(y, { min: 0, max: 2, display: false });
    }
  }
  return {
    key: 'timeline',
    title: ccFigureTitle('timeline'),
    takeaway,
    altText: `${capitalized(noun)}s, Overseer changes, annotations and served-model changes over time. ${takeaway}`,
    config: built,
    table: finishTable({
      columns: [...leadColumns(input), 'Suite', 'Measure', 'Served model'],
      rows: points.map(point => row(point, point.suiteName, point.isLegacy ? 'legacy' : 'telemetry', dominantServedModel(point) ?? '—'))
    }, points, input),
    markers
  };
}

/** Every figure of the timeline, in the order the tab shows them. */
export function buildCcFigures(input: CcFigureInput, options: CcChartOptions = {}): CcFigure[] {
  return CC_FIGURE_KEYS.map(entry => buildCcFigure(entry.key, input, options));
}

/** One figure by key. */
export function buildCcFigure(key: CcFigureKey, input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  switch (key) {
    case 'quality': return qualityFigure(input, options);
    case 'ttfat': return timeToFirstAnswerFigure(input, options);
    case 'rate': return streamingRateFigure(input, options);
    case 'work': return workFigure(input, options);
    case 'tools': return toolCallsFigure(input, options);
    case 'cost': return costFigure(input, options);
    case 'reliability': return reliabilityFigure(input, options);
    default: return timelineOverviewFigure(input, options);
  }
}

/** The viewer asked for reduced motion; the on-screen charts then draw without animation. */
export function prefersReducedMotion(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/** The period bands of an analysis, from its periods' UTC bounds. */
export function analysisBands(
  baseline: { startUtc: string; endUtc: string },
  comparison: { startUtc: string; endUtc: string }
): CcPeriodBand[] {
  return [
    { name: 'Baseline' as const, start: utcMillis(baseline.startUtc), end: utcMillis(baseline.endUtc) },
    { name: 'Comparison' as const, start: utcMillis(comparison.startUtc), end: utcMillis(comparison.endUtc) }
  ].filter(band => Number.isFinite(band.start) && Number.isFinite(band.end));
}
