/**
 * The Chat Consistency charts: Chart.js configurations, their takeaway captions and their data
 * tables, built from a subject's timeline points. The builders are pure functions of their input, so
 * the timeline, the analysis results and the uploaded report charts draw the same figures from the
 * same data; `prefersReducedMotion()` alone reads the environment.
 *
 * Every chart is a line chart over a linear time axis in epoch milliseconds (no date adapter is
 * registered), with an overlay plugin that draws the header band (title, subject and the GnollBench
 * logo), the period bands and their names, the area wash of a single-series chart whose value axis
 * starts at zero, composite Overseer events, annotations and served-model changes as labeled markers
 * (their tags staggered in a band above the plot), and the value of every point of a chart drawing at
 * most two series (`ccPlaceLabels`). A battery run's Overall Index carries its 95 % interval as a
 * whisker (`ccWhiskerPlugin`). A line breaks between the periods where the analysis found them not
 * comparable (`ccPeriodBreak`). A legacy latency proxy is drawn with hollow points and a dashed line,
 * so it is told apart by shape as well as by its legend label. Every dataset carries a stable series
 * id (`seriesId`); without notes of runs not in the analysis or a period break, it carries no
 * scriptable options. Every value
 * axis follows its measure's `CcAxisPolicy` (`ccValueAxis`). A `CcChartStyle` sets the text sizes,
 * line and point sizes and the optional parts; without one the charts draw `CC_CHART_STYLE_DEFAULTS`.
 * A theme without a tooltip box draws a chart that never reacts to the pointer.
 *
 * A point is a run, or a battery run with its members pooled (`CcFigureInput.unitKind`); a battery
 * run's Intelligence is its battery analysis's Overall Intelligence Index.
 *
 * Runs not in the analysis (`CcFigureInput.notAnalyzed`) are drawn as gray crosses joined by gray,
 * dotted segments, their value labels muted, and named in the caption, the tooltip and the data
 * table.
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
import type { FigureFontWeight, TimelineFigureStyle } from '../model-comparison/figure-style';
import type { ResolvedFigureTheme } from '../model-comparison/figure-theme';
import {
  CcAnalysisResult,
  CcAnnotation,
  CcBatteryTimelinePoint,
  CcEndpointResult,
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
  /**
   * The tooltip box; null draws a chart without a tooltip or hover look that ignores the pointer, for
   * a theme that is never interactive.
   */
  tooltip: { background: string; border: string } | null;
  fontFamily: string;
  /** The plot frame's stroke; `muted` when absent. */
  frame?: string;
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

/**
 * Model Comparison's resolved appearance as a chart theme, for a chart drawn inside a composed
 * figure: the series palette, the marker inks and the band tints of the print theme on a light theme
 * and of the screen theme otherwise, the text, grid and frame inks of `theme`, no background (the
 * composer paints the ground) and no tooltip.
 */
export function ccChartThemeFor(theme: ResolvedFigureTheme): CcChartTheme {
  const base = theme.name === 'light' ? CC_PRINT_THEME : CC_SCREEN_THEME;
  return {
    ...base,
    text: theme.chart.inkPrimary,
    secondary: theme.chart.inkSecondary,
    muted: theme.chart.inkMuted,
    grid: theme.chart.gridline,
    background: null,
    surface: theme.surface,
    tooltip: null,
    fontFamily: theme.fonts.chartStack ?? CC_SCREEN_THEME.fontFamily,
    frame: theme.frameColor
  };
}

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
  /**
   * Composite events already grouped and tagged, drawn as the event markers instead of grouping
   * `events`: a report chart's (`ccReportEventGroups`). `harnessPoints` and `eventNumbering` then go unused.
   */
  eventGroups?: readonly CcEventGroup[];
  /** Point id → why the run or battery run is not in the analysis; absent or empty draws every point alike. */
  notAnalyzed?: ReadonlyMap<number, string>;
  /**
   * The analysis's endpoints, for a report chart: a figure whose endpoint (`CC_FIGURE_ENDPOINTS`) is
   * not computable opens its caption with *Not comparable across the periods*. Absent says nothing.
   */
  endpoints?: readonly CcEndpointResult[];
}

/** How `buildMarkers` groups and numbers the events, beyond the drawn points. */
export interface CcEventContext {
  /** The points whose harness versions group the events; the drawn points when absent. */
  harnessPoints?: readonly CcTimelinePoint[];
  /** Composite events whose tags the event markers reuse. */
  numbering?: readonly CcEventGroup[];
  /** Composite events already grouped and tagged; the events are not grouped again when present. */
  groups?: readonly CcEventGroup[];
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
  /** The sizes and optional parts; absent draws `CC_CHART_STYLE_DEFAULTS`. */
  style?: CcChartStyle;
  /**
   * The time axis spans the plotted points and the markers near them, padded by 8 % of that span or
   * 45 minutes, whichever is more; the period bands are clipped to it, widening it only where a band
   * cut by its edge would be under `CC_MIN_BAND_SHARE` of the plot. Absent spans the bands too, padded
   * by 3 % or 12 hours.
   */
  fitToData?: boolean;
}

/** The chart-drawing part of a timeline figure's style, and the weight of the point labels and the legend. */
export type CcChartStyle = Pick<TimelineFigureStyle,
  'axisTextSizePx' | 'axisTitleSizePx' | 'axisTitleWeight' | 'gridlines' | 'plotFrame' | 'valueLabels'
  | 'valueLabelSizePx' | 'legendTextSizePx' | 'markerTagSizePx' | 'lineWidthPx' | 'pointRadiusPx' | 'areaWash'>
  & { readonly labelWeight: FigureFontWeight };

/** The drawing without a style: the sizes and parts of the screen charts and the report charts. */
export const CC_CHART_STYLE_DEFAULTS: CcChartStyle = Object.freeze<CcChartStyle>({
  axisTextSizePx: 11,
  axisTitleSizePx: 12,
  axisTitleWeight: 600,
  gridlines: true,
  plotFrame: false,
  valueLabels: true,
  valueLabelSizePx: 11,
  legendTextSizePx: 12,
  markerTagSizePx: 10,
  lineWidthPx: 2,
  pointRadiusPx: 4,
  areaWash: true,
  labelWeight: 600
});

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
  { key: 'work', title: 'Work per turn (output tokens per answer)' },
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

/** The analysis endpoint each figure plots; the reliability chart and the overview plot none. */
export const CC_FIGURE_ENDPOINTS: Readonly<Partial<Record<CcFigureKey, string>>> = Object.freeze({
  quality: 'P1',
  ttfat: 'P2',
  rate: 'P3',
  work: 'P4',
  cost: 'P5'
});

/**
 * `Not comparable across the periods: no common grader covers every run.`, the caption's opening for
 * a figure whose endpoint the analysis could not compute.
 */
export function ccNotComparableText(reason: string | null | undefined): string {
  const text = (reason ?? '').trim().replace(/\.+$/, '');
  if (!text) return 'Not comparable across the periods: the analysis could not compute this measure.';
  // An opening acronym (`TTFT`, `P2`) keeps its case.
  const phrase = /^[A-Z][A-Z0-9]/.test(text) ? text : text.charAt(0).toLowerCase() + text.slice(1);
  return `Not comparable across the periods: ${phrase}.`;
}

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
 * composites, or hand over composites already grouped; served-model changes are always over `points`.
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
    const groups = context.groups ?? groupOverseerEvents(events, context.harnessPoints ?? points, context.numbering);
    for (const group of groups) {
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

/** A time axis shorter than this is ticked in hours: `09:00`, the date written once. */
export const CC_HOUR_TICKS_SPAN_MS = 2 * DAY_MS;

/**
 * Tick values for a time axis from `min` to `max` (epoch ms): the smallest step of 1, 2, 3, 6 or 12
 * hours, 1, 2 or 7 days (weeks start on Monday), or 1, 3, 6 or 12 months (from the first of a month)
 * that keeps the ticks to `maxTicks`, aligned to UTC. With `maxStepMs`, no step longer than that is
 * taken: the longest one allowed when none keeps to the limit. Empty for an empty or unusable range.
 */
export function ccTimeTicks(min: number, max: number, maxTicks: number, maxStepMs?: number): CcTimeTicks {
  if (!Number.isFinite(min) || !Number.isFinite(max) || !(max > min)) return { values: [], stepMs: 0 };
  const limit = Math.max(2, Math.floor(maxTicks));
  const steps = maxStepMs === undefined ? FIXED_STEPS_MS : FIXED_STEPS_MS.filter(step => step <= maxStepMs);
  for (const [i, step] of steps.entries()) {
    const offset = step === 7 * DAY_MS ? MONDAY_OFFSET_MS : 0;
    const first = Math.ceil((min - offset) / step) * step + offset;
    const last = maxStepMs !== undefined && i === steps.length - 1;
    if (Math.floor((max - first) / step) + 1 <= limit || last) {
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
 * tick only while the range is under `CC_HOUR_TICKS_SPAN_MS`, else on the first tick of each day;
 * otherwise `Oct 8` for a range under a year and `2026-10` beyond.
 */
export function ccTimeTickLabel(value: number, previous: number | null, stepMs: number, spanMs: number): string {
  const date = new Date(value);
  const day = `${date.getUTCFullYear()}-${pad2(date.getUTCMonth() + 1)}-${pad2(date.getUTCDate())}`;
  if (stepMs > 0 && stepMs < DAY_MS) {
    const time = `${pad2(date.getUTCHours())}:${pad2(date.getUTCMinutes())}`;
    const dated = previous === null
      || (spanMs >= CC_HOUR_TICKS_SPAN_MS && Math.floor(previous / DAY_MS) !== Math.floor(value / DAY_MS));
    return dated ? `${day} ${time}` : time;
  }
  if (spanMs < 365 * DAY_MS) return `${MONTH_NAMES[date.getUTCMonth()]} ${date.getUTCDate()}`;
  return day.slice(0, 7);
}

/** About one tick per 110 px of axis, between 2 and 8. */
function maxTicksFor(width: number): number {
  return Math.max(2, Math.min(8, Math.floor((width > 0 ? width : 600) / 110)));
}

/** The ticks of a time axis: in hours of at most 12 under `CC_HOUR_TICKS_SPAN_MS`, whose labels are short. */
export function ccAxisTimeTicks(min: number, max: number, width: number): CcTimeTicks {
  if (max - min < CC_HOUR_TICKS_SPAN_MS) {
    // `09:00` is about half as wide as `Oct 8`, so the ticks may sit twice as close.
    const hourTicks = Math.max(2, Math.min(12, Math.floor((width > 0 ? width : 600) / 60)));
    return ccTimeTicks(min, max, hourTicks, 12 * HOUR_MS);
  }
  return ccTimeTicks(min, max, maxTicksFor(width));
}

// --- The overlay plugin ---

/** The height of one row of 10 px marker tags, a 14 px box and a 2 px gap. */
export const CC_TAG_ROW_HEIGHT = 16;
/** The most rows of marker tags a chart lays out. */
export const CC_TAG_MAX_ROWS = 3;
/** The least horizontal gap between two tags of one row, and the gap above the top row. */
export const CC_TAG_GAP = 4;

/** A marker tag's box for its text size: the text and 2 px above and below it. */
function tagBoxHeight(tagSizePx: number): number {
  return tagSizePx + 4;
}

/** The height of one row of marker tags for their text size: the box and a 2 px gap. */
export function ccTagRowHeight(tagSizePx: number): number {
  return tagBoxHeight(tagSizePx) + 2;
}

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
export function ccTagBandHeight(rows: number, rowHeight: number = CC_TAG_ROW_HEIGHT): number {
  return rows > 0 ? rows * rowHeight + CC_TAG_GAP : 0;
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
  rowHeight: number;
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
    rowHeight: CC_TAG_ROW_HEIGHT,
    isHorizontal: () => true,
    draw: () => undefined,
    update(width: number) {
      band.rows = tagBandRows(chart, band, width);
      band.width = width;
      band.height = ccTagBandHeight(band.rows, band.rowHeight);
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

/**
 * The layout box under the plot and its time axis that holds the period legend: one line of a swatch
 * and a word per period band. It takes a line only when, judged at layout from the bands' widths and
 * the plotted points' positions, a period's name may not find room in its band
 * (`ccPeriodNameNeedsLegend`); the overlay plugin draws it. Kept per chart like the tag band.
 */
interface CcPeriodLegendBand extends LayoutItem {
  options: Record<string, never>;
  bands: readonly CcPeriodBand[];
  range: { min: number; max: number } | null;
  font: string;
  /** The bands the legend names, in band order; empty when it takes no line. */
  shown: readonly CcPeriodBand[];
}

const PERIOD_LEGEND_BANDS = new WeakMap<object, CcPeriodLegendBand>();

const PERIOD_LEGEND_SWATCH_PX = 10;
const PERIOD_LEGEND_SWATCH_GAP = 4;
const PERIOD_LEGEND_ITEM_GAP = 12;

/** Per band, the x values of the first and last plotted point of each visible dataset inside it. */
function bandEdgePointXs(chart: Chart<'line'>, bands: readonly CcPeriodBand[]): number[][] {
  const xs: number[][] = bands.map(() => []);
  chart.data.datasets.forEach((dataset, index) => {
    if (!chart.isDatasetVisible(index)) return;
    const data = (dataset.data ?? []) as CcChartPoint[];
    bands.forEach((band, b) => {
      const inside = data.filter(point => point && point.y !== null && point.x >= band.start && point.x <= band.end);
      if (inside.length === 0) return;
      xs[b].push(inside[0].x, inside[inside.length - 1].x);
    });
  });
  return xs;
}

function periodLegendRows(chart: Chart<'line'>, legend: CcPeriodLegendBand, width: number): readonly CcPeriodBand[] {
  const range = legend.range;
  if (!range || !(range.max > range.min) || !(width > 0) || legend.bands.length === 0) return [];
  const ctx = chart.ctx;
  if (!ctx) return [];
  const px = (value: number) => ((value - range.min) / (range.max - range.min)) * width;
  const visible = legend.bands.filter(band => Math.min(px(band.end), width) > Math.max(px(band.start), 0));
  if (visible.length === 0) return [];
  ctx.save();
  ctx.font = legend.font;
  const edges = bandEdgePointXs(chart, visible);
  const needed = visible.some((band, i) => ccPeriodNameNeedsLegend(
    band.name, text => ctx.measureText(text).width,
    { left: Math.max(px(band.start), 0), right: Math.min(px(band.end), width) },
    edges[i].map(px)));
  ctx.restore();
  return needed ? visible : [];
}

function periodLegendBandOf(chart: Chart<'line'>): CcPeriodLegendBand {
  const existing = PERIOD_LEGEND_BANDS.get(chart);
  if (existing) return existing;
  const band: CcPeriodLegendBand = {
    position: 'bottom',
    // Below the time axis (0): a higher weight sits farther from the plot.
    weight: 1,
    fullSize: false,
    width: 0,
    height: 0,
    left: 0,
    top: 0,
    right: 0,
    bottom: 0,
    options: {},
    bands: [],
    range: null,
    font: '',
    shown: [],
    isHorizontal: () => true,
    draw: () => undefined,
    update(width: number) {
      band.shown = periodLegendRows(chart, band, width);
      band.width = width;
      band.height = band.shown.length > 0 ? CC_PERIOD_LEGEND_HEIGHT : 0;
    }
  };
  layouts.addBox(chart as unknown as Chart, band);
  PERIOD_LEGEND_BANDS.set(chart, band);
  return band;
}

/** The period legend in its box: a swatch of each band's fill and its name, left to right. */
function drawPeriodLegend(chart: Chart<'line'>, legend: CcPeriodLegendBand, theme: CcChartTheme): void {
  if (!(legend.height > 0) || legend.shown.length === 0) return;
  const { ctx } = chart;
  ctx.save();
  ctx.font = legend.font;
  ctx.textBaseline = 'middle';
  ctx.textAlign = 'left';
  const middle = legend.top + legend.height / 2;
  let x = legend.left;
  for (const band of legend.shown) {
    const top = middle - PERIOD_LEGEND_SWATCH_PX / 2;
    ctx.fillStyle = band.name === 'Baseline' ? theme.baselineBand : theme.comparisonBand;
    ctx.fillRect(x, top, PERIOD_LEGEND_SWATCH_PX, PERIOD_LEGEND_SWATCH_PX);
    ctx.strokeStyle = theme.muted;
    ctx.lineWidth = 1;
    ctx.strokeRect(x + 0.5, top + 0.5, PERIOD_LEGEND_SWATCH_PX - 1, PERIOD_LEGEND_SWATCH_PX - 1);
    x += PERIOD_LEGEND_SWATCH_PX + PERIOD_LEGEND_SWATCH_GAP;
    ctx.fillStyle = theme.muted;
    ctx.fillText(band.name, x, middle);
    x += ctx.measureText(band.name).width + PERIOD_LEGEND_ITEM_GAP;
  }
  ctx.restore();
}

function removeBands(chart: Chart<'line'>): void {
  for (const bands of [TAG_BANDS, HEADER_BANDS, PERIOD_LEGEND_BANDS] as WeakMap<object, LayoutItem>[]) {
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
  /** The value range a point's whisker covers, which its label keeps clear of; null or absent for none. */
  reach?: (point: CcChartPoint) => { low: number; high: number } | null;
}

/** What the overlay plugin draws besides the bands and the markers. */
export interface CcOverlayDecor {
  header?: CcChartHeader | null;
  logo?: FigureLogo | null;
  /** The dataset whose area is washed with its color; none when absent. */
  wash?: { datasetIndex: number; color: string } | null;
  /** The instant (epoch ms) the lines and the wash break at between the periods; none when absent. */
  breakAt?: number | null;
  pointLabels?: readonly CcPointLabelSet[];
  /** Point ids whose labels are muted: the points not in the analysis. */
  notAnalyzed?: ReadonlyMap<number, string> | null;
  /** The point labels' font and distance; `ccPointLabelFont()` when absent. */
  labelFont?: CcPointLabelFont;
  /** The marker tags' text size in px; 10 when absent. */
  tagSizePx?: number;
  /** A 1 px frame around the plot in the theme's frame color. */
  frame?: boolean;
}

/** How the point labels are written. */
export interface CcPointLabelFont {
  sizePx: number;
  weight: FigureFontWeight;
  /** From the point's center to the label's near edge, in px. */
  offsetPx: number;
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
/** The point labels' distance beyond the point's radius, in px. */
const POINT_LABEL_CLEARANCE = 4;

/** The point labels of `style`; without a style, 600 at 11 px, 8 px from the point's center. */
export function ccPointLabelFont(style: CcChartStyle = CC_CHART_STYLE_DEFAULTS): CcPointLabelFont {
  return { sizePx: style.valueLabelSizePx, weight: style.labelWeight, offsetPx: style.pointRadiusPx + POINT_LABEL_CLEARANCE };
}

/** Two boxes come within `POINT_LABEL_GAP` of each other. */
function boxesMeet(a: CcLabelBox, b: CcLabelBox): boolean {
  return a.left < b.right + POINT_LABEL_GAP && b.left < a.right + POINT_LABEL_GAP
    && a.top < b.bottom + POINT_LABEL_GAP && b.top < a.bottom + POINT_LABEL_GAP;
}

/**
 * The point labels kept, as indices into `candidates`, which come in priority order: each box is kept
 * unless it leaves `area`, or comes within `POINT_LABEL_GAP` of a box kept before it or of an obstacle
 * (a period name). The first `leading` candidates, the labels that are never given up for a period
 * name, ignore the obstacles.
 */
export function ccPlaceLabels(
  candidates: readonly CcLabelBox[],
  area: CcLabelBox,
  obstacles: readonly CcLabelBox[] = [],
  leading = 0
): number[] {
  const kept: number[] = [];
  candidates.forEach((box, i) => {
    if (box.left < area.left || box.right > area.right || box.top < area.top || box.bottom > area.bottom) return;
    if (i >= leading && obstacles.some(obstacle => boxesMeet(box, obstacle))) return;
    if (!kept.some(k => boxesMeet(box, candidates[k]))) kept.push(i);
  });
  return kept;
}

/**
 * A period band's name as drawn: its text, cut to the band, and its box in canvas px. `clear` when the
 * box meets no obstacle and covers no point.
 */
export interface CcPeriodLabel {
  text: string;
  box: CcLabelBox;
  clear: boolean;
}

/** The period names' text size, and their inset from the band's sides and the plot's top or bottom, in px. */
export const CC_PERIOD_LABEL_PX = 11;
const PERIOD_LABEL_INSET_X = 6;
const PERIOD_LABEL_INSET_Y = 4;
/** How near a point may come to a period name, in px: about a point's radius. */
const PERIOD_LABEL_POINT_CLEARANCE = 5;
/** The fewest letters a cut period name keeps before its ellipsis; a band too narrow for them names its period in the legend. */
export const CC_PERIOD_NAME_MIN_LETTERS = 4;
/** How far from a point its value label may reach sideways, in px, as the layout judges a corner taken. */
const PERIOD_LEGEND_POINT_REACH = 24;

/** The period legend's line: the 11 px names and 3 px above and below them. */
export const CC_PERIOD_LEGEND_HEIGHT = CC_PERIOD_LABEL_PX + 6;

/**
 * `text` whole while it fits `width`, else cut with an ellipsis; empty when fewer than `minLetters`
 * letters would be left.
 */
function fitMeasured(text: string, width: number, measure: (text: string) => number, minLetters = 1): string {
  if (!(width > 0)) return '';
  if (measure(text) <= width) return text;
  let end = text.length;
  while (end > 0 && measure(`${text.slice(0, end).trimEnd()}…`) > width) end--;
  const kept = text.slice(0, end).trimEnd();
  return kept.length >= Math.max(1, minLetters) ? `${kept}…` : '';
}

/**
 * Where a period band's name goes, inside the band (`left`–`right`, clipped to the plot `area`): its
 * top left, bottom left, top right or bottom right corner, so a name keeps the left of its band from
 * chart to chart; the first that meets no obstacle (a marker tag, a point label that is never given
 * up) and covers no point, which is `clear`; failing that, the first that meets no obstacle; failing
 * that, the bottom left. The whole name while the band has room for it, else cut with an ellipsis;
 * null when fewer than `CC_PERIOD_NAME_MIN_LETTERS` letters fit. The other point labels are then kept
 * clear of it.
 */
export function ccPeriodLabelPlacement(
  name: string,
  measure: (text: string) => number,
  band: { left: number; right: number },
  area: CcLabelBox,
  obstacles: readonly CcLabelBox[] = [],
  points: readonly { x: number; y: number }[] = [],
  sizePx: number = CC_PERIOD_LABEL_PX
): CcPeriodLabel | null {
  const text = fitMeasured(name, band.right - band.left - 2 * PERIOD_LABEL_INSET_X, measure, CC_PERIOD_NAME_MIN_LETTERS);
  if (!text) return null;
  const width = measure(text);
  const lefts = [band.left + PERIOD_LABEL_INSET_X, band.right - PERIOD_LABEL_INSET_X - width];
  const tops = [area.top + PERIOD_LABEL_INSET_Y, area.bottom - PERIOD_LABEL_INSET_Y - sizePx];
  const corners: CcLabelBox[] = [
    [lefts[0], tops[0]], [lefts[0], tops[1]], [lefts[1], tops[0]], [lefts[1], tops[1]]
  ].map(([left, top]) => ({ left, top, right: left + width, bottom: top + sizePx }));
  const covers = (box: CcLabelBox, point: { x: number; y: number }) =>
    point.x >= box.left - PERIOD_LABEL_POINT_CLEARANCE && point.x <= box.right + PERIOD_LABEL_POINT_CLEARANCE
    && point.y >= box.top - PERIOD_LABEL_POINT_CLEARANCE && point.y <= box.bottom + PERIOD_LABEL_POINT_CLEARANCE;
  const unobstructed = corners.filter(box => !obstacles.some(obstacle => boxesMeet(box, obstacle)));
  const free = unobstructed.find(candidate => !points.some(point => covers(candidate, point)));
  return free ? { text, box: free, clear: true } : { text, box: unobstructed[0] ?? corners[1], clear: false };
}

/**
 * Whether a period's name may need the legend under the plot, judged at layout before the value axis
 * is known: its band (`left`–`right`, in px of the plot) cannot hold the name with
 * `CC_PERIOD_NAME_MIN_LETTERS` letters, or both its left and its right corners lie over the first or
 * last point of a period (`pointXs`, in px), whose value labels are never given up.
 */
export function ccPeriodNameNeedsLegend(
  name: string,
  measure: (text: string) => number,
  band: { left: number; right: number },
  pointXs: readonly number[]
): boolean {
  const text = fitMeasured(name, band.right - band.left - 2 * PERIOD_LABEL_INSET_X, measure, CC_PERIOD_NAME_MIN_LETTERS);
  if (!text) return true;
  const width = measure(text);
  const reach = PERIOD_LABEL_POINT_CLEARANCE + PERIOD_LEGEND_POINT_REACH;
  const taken = (left: number) => pointXs.some(x => x >= left - reach && x <= left + width + reach);
  return taken(band.left + PERIOD_LABEL_INSET_X) && taken(band.right - PERIOD_LABEL_INSET_X - width);
}

interface PointLabel {
  text: string;
  value: number;
  /** The point's time, epoch ms. */
  at: number;
  x: number;
  box: CcLabelBox;
  muted: boolean;
}

/** The point labels in priority order; the first `leading` are never given up for a period name. */
interface PointLabels {
  labels: PointLabel[];
  leading: number;
}

/** The gap between a whisker's cap and the point label beyond it, in px. */
const WHISKER_LABEL_GAP_PX = 3;

/**
 * Each set's labels, ordered by priority: with period bands, the first and last point of each period
 * in every set, which are never given up for a period name; then per set its latest, highest and
 * lowest point; then every other point left to right. A label sits above its point (below for a
 * `below` set), beyond the cap of the point's whisker where it has one, and on the other side where it
 * would cross the plot's edge; it is kept inside the canvas horizontally. Measured in the context's
 * current font.
 */
function pointLabelCandidates(
  chart: Chart<'line'>,
  sets: readonly CcPointLabelSet[],
  notAnalyzed: ReadonlyMap<number, string> | null,
  font: CcPointLabelFont,
  bands: readonly CcPeriodBand[] = []
): PointLabels {
  const { sizePx, offsetPx } = font;
  const area = chart.chartArea;
  const ctx = chart.ctx;
  const yScale = chart.scales['y'];
  const guarded: PointLabel[] = [];
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
      const reach = yScale ? set.reach?.(point) ?? null : null;
      const capAbove = reach ? yScale!.getPixelForValue(reach.high) - WHISKER_LABEL_GAP_PX : Number.POSITIVE_INFINITY;
      const capBelow = reach ? yScale!.getPixelForValue(reach.low) + WHISKER_LABEL_GAP_PX : Number.NEGATIVE_INFINITY;
      const aboveTop = Math.min(element.y - offsetPx, capAbove) - sizePx;
      const belowTop = Math.max(element.y + offsetPx, capBelow);
      let top = set.below ? belowTop : aboveTop;
      if (!set.below && aboveTop < area.top) top = belowTop;
      if (set.below && belowTop + sizePx > area.bottom) top = aboveTop;
      labels.push({
        text, value: point.y, at: point.x, x: element.x, muted: notAnalyzed?.has(point.runId) ?? false,
        box: { left, top, right: left + width, bottom: top + sizePx }
      });
    });
    if (labels.length === 0) continue;
    const edges = [...new Set(bands.flatMap(band => {
      const inside = labels.filter(label => label.at >= band.start && label.at <= band.end);
      return inside.length > 0 ? [inside[0], inside[inside.length - 1]] : [];
    }))];
    guarded.push(...edges);
    const latest = labels[labels.length - 1];
    const highest = labels.reduce((best, label) => label.value > best.value ? label : best);
    const lowest = labels.reduce((best, label) => label.value < best.value ? label : best);
    const lead = [...new Set([latest, highest, lowest])].filter(label => !edges.includes(label));
    first.push(...lead);
    rest.push(...labels.filter(label => !edges.includes(label) && !lead.includes(label)));
  }
  return { labels: [...guarded, ...first, ...rest.sort((a, b) => a.x - b.x)], leading: guarded.length };
}

/** The point labels of `sets`, measured in their font; none without sets. */
function measurePointLabels(
  chart: Chart<'line'>,
  sets: readonly CcPointLabelSet[],
  theme: CcChartTheme,
  notAnalyzed: ReadonlyMap<number, string> | null,
  font: CcPointLabelFont,
  bands: readonly CcPeriodBand[]
): PointLabels {
  if (sets.length === 0) return { labels: [], leading: 0 };
  const ctx = chart.ctx;
  ctx.save();
  ctx.font = `${font.weight} ${font.sizePx}px ${theme.fontFamily}`;
  const labels = pointLabelCandidates(chart, sets, notAnalyzed, font, bands);
  ctx.restore();
  return labels;
}

/**
 * The kept point labels, each over a halo of the background so it reads across lines and fills; all
 * but the leading ones clear of `obstacles`.
 */
function drawPointLabels(
  chart: Chart<'line'>,
  candidates: PointLabels,
  theme: CcChartTheme,
  font: CcPointLabelFont,
  obstacles: readonly CcLabelBox[] = []
): void {
  if (candidates.labels.length === 0) return;
  const ctx = chart.ctx;
  ctx.save();
  ctx.font = `${font.weight} ${font.sizePx}px ${theme.fontFamily}`;
  const kept = ccPlaceLabels(candidates.labels.map(label => label.box), { left: 0, top: 0, right: chart.width, bottom: chart.height },
    obstacles, candidates.leading);
  ctx.textAlign = 'left';
  ctx.textBaseline = 'top';
  ctx.lineJoin = 'round';
  ctx.lineWidth = 3;
  ctx.strokeStyle = theme.background ?? theme.surface;
  for (const i of kept) {
    const { text, box, muted } = candidates.labels[i];
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

/**
 * The drawn, non-skipped point elements of a dataset, split where a null leaves a gap and, with
 * `breakPx`, between the points on either side of it.
 */
function lineRuns(chart: Chart<'line'>, datasetIndex: number, breakPx: number | null = null): { x: number; y: number }[][] {
  const meta = chart.getDatasetMeta(datasetIndex);
  if (!meta || meta.hidden) return [];
  const runs: { x: number; y: number }[][] = [];
  let current: { x: number; y: number }[] = [];
  for (const element of meta.data as unknown as { x: number; y: number; skip?: boolean }[]) {
    if (element.skip || !Number.isFinite(element.x) || !Number.isFinite(element.y)) {
      if (current.length > 0) runs.push(current);
      current = [];
    } else {
      const previous = current[current.length - 1];
      if (previous && breakPx !== null && (previous.x < breakPx) !== (element.x < breakPx)) {
        runs.push(current);
        current = [];
      }
      current.push({ x: element.x, y: element.y });
    }
  }
  if (current.length > 0) runs.push(current);
  return runs;
}

/** The drawn, non-skipped point elements of every visible dataset, in canvas px. */
function drawnPoints(chart: Chart<'line'>): { x: number; y: number }[] {
  const points: { x: number; y: number }[] = [];
  chart.data.datasets.forEach((_, index) => {
    const meta = chart.getDatasetMeta(index);
    if (!meta || meta.hidden || !chart.isDatasetVisible(index)) return;
    for (const element of meta.data as unknown as { x: number; y: number; skip?: boolean }[]) {
      if (!element.skip && Number.isFinite(element.x) && Number.isFinite(element.y)) points.push({ x: element.x, y: element.y });
    }
  });
  return points;
}

/**
 * Draws the header band, the period bands and the area wash under the datasets, and the plot frame,
 * the markers, the period names and the point labels over them: a vertical line per marker and its
 * tag in a small box above the plot, the line dashed for an event, dotted for an annotation and
 * dash-dotted for a served-model change, its tag box filled for an event and outlined otherwise, so
 * the kinds differ by shape, not by color.
 *
 * The tags sit in a band of up to `CC_TAG_MAX_ROWS` rows between the legend and the plot, laid out
 * by `ccTagRows`; the band is sized from the markers within `range` (the x axis's bounds when null)
 * before drawing, so a crowded day never covers the data. Each line starts under its own tag. Each
 * period's name takes a corner of its band clear of the tags and of the value labels of each period's
 * first and last point, which are never given up (`ccPeriodLabelPlacement`); the other point labels
 * keep clear of the names. Where a name may not find room in its band, a one-line period legend under
 * the time axis names every period instead, and a name stays in its band only where it is clear.
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
  const tagSizePx = decor.tagSizePx ?? CC_CHART_STYLE_DEFAULTS.markerTagSizePx;
  const font = `bold ${tagSizePx}px ${theme.fontFamily}`;
  const rowHeight = ccTagRowHeight(tagSizePx);
  const boxHeight = tagBoxHeight(tagSizePx);
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
      band.rowHeight = rowHeight;
      const legend = periodLegendBandOf(chart);
      legend.bands = bands;
      legend.range = range;
      legend.font = `${CC_PERIOD_LABEL_PX}px ${theme.fontFamily}`;
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
      }
      const wash = decor.wash;
      if (wash) {
        const gradient = ctx.createLinearGradient(0, area.top, 0, area.bottom);
        gradient.addColorStop(0, withAlpha(wash.color, 0.14));
        gradient.addColorStop(1, withAlpha(wash.color, 0));
        ctx.fillStyle = gradient;
        const breakPx = typeof decor.breakAt === 'number' ? x.getPixelForValue(decor.breakAt) : null;
        for (const run of lineRuns(chart, wash.datasetIndex, breakPx).filter(points => points.length > 1)) {
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
      if (decor.frame) {
        ctx.strokeStyle = theme.frame ?? theme.muted;
        ctx.lineWidth = 1;
        // Half a pixel in, so the 1 px stroke covers whole pixels inside the area.
        ctx.strokeRect(area.left + 0.5, area.top + 0.5, area.right - area.left - 1, area.bottom - area.top - 1);
      }
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
        ? area.top - (reserved - r) * rowHeight
        : area.top + r * rowHeight;
      // The lines first, so every tag box covers the lines of the rows above it.
      ctx.lineWidth = 1.5;
      placed.forEach(({ marker, px }, i) => {
        ctx.strokeStyle = color[marker.kind];
        ctx.setLineDash(dash[marker.kind]);
        ctx.beginPath();
        ctx.moveTo(px, tagTop(row[i]) + boxHeight);
        ctx.lineTo(px, area.bottom);
        ctx.stroke();
      });
      ctx.setLineDash([]);
      ctx.lineWidth = 1;
      const tagBoxes: CcLabelBox[] = [];
      placed.forEach(({ marker, px, width }, i) => {
        const top = tagTop(row[i]);
        const filled = marker.kind === 'event';
        ctx.strokeStyle = color[marker.kind];
        ctx.fillStyle = filled ? color[marker.kind] : theme.background ?? theme.surface;
        ctx.fillRect(px - width / 2, top, width, boxHeight);
        ctx.strokeRect(px - width / 2, top, width, boxHeight);
        ctx.fillStyle = filled ? theme.background ?? theme.surface : color[marker.kind];
        ctx.fillText(marker.tag, px, top + boxHeight / 2);
        tagBoxes.push({ left: px - width / 2, top, right: px + width / 2, bottom: top + boxHeight });
      });
      ctx.restore();
      const labelFont = decor.labelFont ?? ccPointLabelFont();
      const pointLabels = measurePointLabels(chart, decor.pointLabels ?? [], theme, decor.notAnalyzed ?? null, labelFont, bands);
      const guarded = pointLabels.labels.slice(0, pointLabels.leading).map(label => label.box);
      const legend = PERIOD_LEGEND_BANDS.get(chart) ?? null;
      const periodBoxes = drawPeriodLabels(chart, bands, theme, [...tagBoxes, ...guarded], legend !== null && legend.height > 0);
      if (legend) drawPeriodLegend(chart, legend, theme);
      drawPointLabels(chart, pointLabels, theme, labelFont, periodBoxes);
    }
  };
}

/**
 * Writes each period band's name where `ccPeriodLabelPlacement` puts it, over a halo of the background,
 * and returns the boxes written, for the other point labels to keep clear of. With the period legend
 * laid out (`inLegend`), which names every band, a name is written in its band only where it is
 * clear of the obstacles and the points.
 */
function drawPeriodLabels(
  chart: Chart<'line'>,
  bands: readonly CcPeriodBand[],
  theme: CcChartTheme,
  obstacles: readonly CcLabelBox[],
  inLegend = false
): CcLabelBox[] {
  const x = chart.scales['x'];
  const area = chart.chartArea;
  if (!x || !area || bands.length === 0) return [];
  const { ctx } = chart;
  ctx.save();
  ctx.font = `${CC_PERIOD_LABEL_PX}px ${theme.fontFamily}`;
  ctx.textBaseline = 'top';
  ctx.textAlign = 'left';
  ctx.lineJoin = 'round';
  ctx.lineWidth = 3;
  ctx.strokeStyle = theme.background ?? theme.surface;
  ctx.fillStyle = theme.muted;
  const points = drawnPoints(chart);
  const plot: CcLabelBox = { left: area.left, top: area.top, right: area.right, bottom: area.bottom };
  const written: CcLabelBox[] = [];
  for (const band of bands) {
    const left = Math.max(area.left, x.getPixelForValue(band.start));
    const right = Math.min(area.right, x.getPixelForValue(band.end));
    if (right <= left) continue;
    const label = ccPeriodLabelPlacement(band.name, text => ctx.measureText(text).width, { left, right }, plot,
      [...obstacles, ...written], points);
    if (!label || (inLegend && !label.clear)) continue;
    ctx.strokeText(label.text, label.box.left, label.box.top);
    ctx.fillText(label.text, label.box.left, label.box.top);
    written.push(label.box);
  }
  ctx.restore();
  return written;
}

/** A battery run's 95 % interval of its Overall Index, drawn as a whisker at its point. */
export interface CcWhisker {
  /** The point's time, epoch ms. */
  x: number;
  low: number;
  high: number;
  runId: number;
  /** The series the whisker belongs to; drawn only while that series is. */
  seriesId: string;
}

/** A whisker's stroke and its caps' half width in px, and its opacity against the series color. */
const WHISKER_LINE_PX = 1.5;
const WHISKER_CAP_PX = 4;
const WHISKER_ALPHA = 0.55;

/**
 * Draws a vertical whisker with caps from `low` to `high` at each point of `whiskers`, in `color` at
 * reduced opacity (`muted` for a point not in the analysis), under the datasets and clipped to the
 * plot. Registered after `ccOverlayPlugin`, so the whiskers lie over the period bands.
 */
export function ccWhiskerPlugin(
  whiskers: readonly CcWhisker[],
  color: string,
  muted: string,
  notAnalyzed: ReadonlyMap<number, string> | null = null
): Plugin<'line'> {
  return {
    id: 'ccWhiskers',
    beforeDatasetsDraw(chart) {
      const x = chart.scales['x'];
      const y = chart.scales['y'];
      const area = chart.chartArea;
      if (!x || !y || !area) return;
      const clamp = (value: number) => Math.min(area.bottom, Math.max(area.top, value));
      const { ctx } = chart;
      ctx.save();
      ctx.lineWidth = WHISKER_LINE_PX;
      ctx.setLineDash([]);
      ctx.lineCap = 'butt';
      for (const whisker of whiskers) {
        const px = x.getPixelForValue(whisker.x);
        if (!Number.isFinite(px) || px < area.left || px > area.right) continue;
        const top = clamp(y.getPixelForValue(whisker.high));
        const bottom = clamp(y.getPixelForValue(whisker.low));
        if (!Number.isFinite(top) || !Number.isFinite(bottom)) continue;
        ctx.strokeStyle = withAlpha(notAnalyzed?.has(whisker.runId) ? muted : color, WHISKER_ALPHA);
        ctx.beginPath();
        ctx.moveTo(px, top);
        ctx.lineTo(px, bottom);
        ctx.moveTo(px - WHISKER_CAP_PX, top);
        ctx.lineTo(px + WHISKER_CAP_PX, top);
        ctx.moveTo(px - WHISKER_CAP_PX, bottom);
        ctx.lineTo(px + WHISKER_CAP_PX, bottom);
        ctx.stroke();
      }
      ctx.restore();
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

/** The fitted time axis's padding on each side: 8 % of the span, at least 45 minutes. */
const FIT_PAD_SHARE = 0.08;
const FIT_PAD_MIN_MS = 45 * 60_000;

/** The least share of the plot a period band cut by the time axis's edge is widened to. */
export const CC_MIN_BAND_SHARE = 0.1;

/**
 * `range` widened on the side where a period band runs past its edge, until the band's visible part
 * is at least `share` of the range or the band's own end is reached. A band inside the range, or
 * wholly outside it, is left as it is.
 */
export function ccWidenRangeForBands(
  range: { min: number; max: number },
  bands: readonly CcPeriodBand[],
  share: number = CC_MIN_BAND_SHARE
): { min: number; max: number } {
  let { min, max } = range;
  // Widening one side changes the other band's share, so a few passes settle both.
  for (let pass = 0; pass < 4; pass++) {
    let changed = false;
    for (const band of bands) {
      if (!(band.end > band.start)) continue;
      const left = Math.max(band.start, min);
      const right = Math.min(band.end, max);
      if (right <= left || right - left >= share * (max - min)) continue;
      if (band.end > max) {
        const target = Math.min(band.end, (left - share * min) / (1 - share));
        if (target > max) {
          max = target;
          changed = true;
        }
      } else if (band.start < min) {
        const target = Math.max(band.start, (right - share * max) / (1 - share));
        if (target < min) {
          min = target;
          changed = true;
        }
      }
    }
    if (!changed) break;
  }
  return { min, max };
}

/**
 * The time axis: the plotted points, the period bands unless `fitToData`, and the markers within a
 * week of them, padded on both sides, then widened where a period band would be a sliver
 * (`ccWidenRangeForBands`).
 */
function xRange(
  series: readonly CcChartPoint[][],
  markers: readonly CcChartMarker[],
  bands: readonly CcPeriodBand[],
  fitToData = false
): { min: number; max: number } | null {
  const xs = [
    ...series.flat().filter(point => point.y !== null).map(point => point.x),
    ...(fitToData ? [] : bands.flatMap(band => [band.start, band.end]))
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
  const pad = fitToData
    ? Math.max((max - min) * FIT_PAD_SHARE, FIT_PAD_MIN_MS)
    : Math.max((max - min) * 0.03, 12 * 3_600_000);
  return ccWidenRangeForBands({ min: min - pad, max: max + pad }, bands);
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
  paddingRight: number,
  style: CcChartStyle,
  /** The legend's weight; null leaves Chart.js's own. */
  legendWeight: FigureFontWeight | null
): ChartOptions<'line'> {
  const tick = { color: theme.muted, font: { family: theme.fontFamily, size: style.axisTextSizePx } };
  const axisTitle = {
    display: true,
    color: theme.secondary,
    font: { family: theme.fontFamily, size: style.axisTitleSizePx, weight: style.axisTitleWeight }
  };
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
    // A theme without a tooltip box draws a figure that nothing on it reacts to.
    ...(theme.tooltip ? {} : { events: [] }),
    // On a point only (its hit radius keeps it easy to reach), never anywhere over the plot.
    interaction: { mode: 'nearest', intersect: true, axis: 'xy' },
    layout: { padding: { top: 4, right: paddingRight } },
    scales: {
      x: {
        type: 'linear',
        ...(range ? { min: range.min, max: range.max } : {}),
        afterBuildTicks: (scale: Scale) => {
          const ticks = ccAxisTimeTicks(scale.min, scale.max, scale.width);
          if (ticks.values.length > 0) scale.ticks = ticks.values.map(value => ({ value }));
        },
        ticks: {
          ...tick,
          autoSkip: false,
          maxRotation: 0,
          callback(this: Scale, value: string | number, index: number, ticks: Tick[]) {
            const span = this.max - this.min;
            // A lone tick of a short range is still an hour.
            const step = ticks.length > 1 ? ticks[1].value - ticks[0].value : span < CC_HOUR_TICKS_SPAN_MS ? HOUR_MS : 0;
            return ccTimeTickLabel(Number(value), index > 0 ? ticks[index - 1].value : null, step, span);
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
        grid: { ...(style.gridlines ? {} : { display: false }), color: theme.grid, lineWidth: 1 },
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
          font: { family: theme.fontFamily, size: style.legendTextSizePx, ...(legendWeight === null ? {} : { weight: legendWeight }) },
          usePointStyle: true,
          boxWidth: 8,
          boxHeight: 8,
          padding: 12,
          ...(marks ? { generateLabels: plainPointLegend(marks.looks) } : {})
        }
      },
      tooltip: {
        ...(theme.tooltip ? {} : { enabled: false }),
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

/** How a chart's datasets are drawn: its style's line and point sizes, and whether points grow on hover. */
interface DatasetLook {
  style: CcChartStyle;
  hover: boolean;
}

/** The point radius, and the cross and hover radii, of the unstyled drawing. */
const POINT_RADIUS_PX = 4;
const CROSS_RADIUS_PX = 4.5;
const HOVER_RADIUS_PX = 6;

/** `px` of the unstyled drawing at the style's point size. */
function scaledRadius(px: number, style: CcChartStyle): number {
  return px * style.pointRadiusPx / POINT_RADIUS_PX;
}

function pointLook(spec: DatasetSpec, theme: CcChartTheme, draw: DatasetLook): PointLook {
  return {
    pointStyle: spec.pointStyle ?? 'circle',
    pointBackgroundColor: spec.hollow ? theme.surface : spec.color,
    pointBorderColor: spec.hollow ? spec.color : theme.surface,
    pointBorderWidth: 2,
    pointRadius: draw.style.pointRadiusPx
  };
}

/** A segment's line where it crosses the break between the periods: none. */
const NO_LINE = 'rgba(0, 0, 0, 0)';

/** The segment from point `p0DataIndex` to `p1DataIndex` of `data` crosses `breakAt`. */
function crossesBreak(data: readonly CcChartPoint[], ctx: ScriptableLineSegmentContext, breakAt: number | null): boolean {
  if (breakAt === null) return false;
  const from = data[ctx.p0DataIndex];
  const to = data[ctx.p1DataIndex];
  return from !== undefined && to !== undefined && (from.x < breakAt) !== (to.x < breakAt);
}

function dataset(spec: DatasetSpec, theme: CcChartTheme, draw: DatasetLook, breakAt: number | null = null): CcChartDataset {
  const resting = pointLook(spec, theme, draw);
  return {
    ...(breakAt === null ? {} : {
      segment: { borderColor: (ctx: ScriptableLineSegmentContext) => crossesBreak(spec.data, ctx, breakAt) ? NO_LINE : undefined }
    }),
    seriesId: spec.id,
    label: spec.label,
    data: spec.data,
    yAxisID: 'y',
    borderColor: spec.color,
    backgroundColor: spec.color,
    ...resting,
    pointHoverRadius: draw.hover ? scaledRadius(HOVER_RADIUS_PX, draw.style) : resting.pointRadius,
    pointHoverBorderWidth: resting.pointBorderWidth,
    pointHitRadius: 12,
    borderWidth: draw.style.lineWidthPx,
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
 * segments its line. Without hover, a point keeps its resting radius under the pointer. A segment
 * across `breakAt` is not drawn.
 */
function markedDataset(
  spec: DatasetSpec,
  theme: CcChartTheme,
  notAnalyzed: ReadonlyMap<number, string>,
  draw: DatasetLook,
  breakAt: number | null = null
): CcChartDataset {
  const look = pointLook(spec, theme, draw);
  const crossRadius = scaledRadius(CROSS_RADIUS_PX, draw.style);
  const marked = (point: CcChartPoint | undefined) => point !== undefined && notAnalyzed.has(point.runId);
  const at = (ctx: ScriptableContext<'line'>) => marked(ctx.raw as CcChartPoint | undefined);
  const segmentAt = (ctx: ScriptableLineSegmentContext) =>
    marked(spec.data[ctx.p0DataIndex]) || marked(spec.data[ctx.p1DataIndex]);
  const pointRadius = (ctx: ScriptableContext<'line'>) => at(ctx) ? crossRadius : look.pointRadius;
  return {
    ...dataset(spec, theme, draw),
    pointStyle: (ctx: ScriptableContext<'line'>): PointStyle => at(ctx) ? 'crossRot' : look.pointStyle,
    pointBackgroundColor: (ctx: ScriptableContext<'line'>) => at(ctx) ? 'rgba(0, 0, 0, 0)' : look.pointBackgroundColor,
    pointBorderColor: (ctx: ScriptableContext<'line'>) => at(ctx) ? theme.muted : look.pointBorderColor,
    pointBorderWidth: (ctx: ScriptableContext<'line'>) => at(ctx) ? 2 : look.pointBorderWidth,
    pointRadius,
    ...(draw.hover ? {} : { pointHoverRadius: pointRadius }),
    segment: {
      borderColor: (ctx: ScriptableLineSegmentContext) =>
        crossesBreak(spec.data, ctx, breakAt) ? NO_LINE : segmentAt(ctx) ? theme.muted : undefined,
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
  /** The area under a lone, solid dataset washed with its color, while its value axis starts at zero. */
  wash?: boolean;
  /** Where the lines break between the periods (`ccPeriodBreak`); none when null or absent. */
  breakAt?: number | null;
  /** Interval whiskers, drawn for the series drawn and taken into the value axis. */
  whiskers?: readonly CcWhisker[];
}

/**
 * Where a figure's lines break between the periods: the comparison's start, while the analysis could
 * not compute the figure's endpoint because the measurement changed between the periods or they
 * share no time-of-week stratum, and its period bands are drawn. Null otherwise, and for an analysis
 * saved before the reason was recorded, whose lines join as before.
 */
export function ccPeriodBreak(key: CcFigureKey, input: CcFigureInput): number | null {
  const id = CC_FIGURE_ENDPOINTS[key];
  const endpoint = id ? input.endpoints?.find(entry => entry.id === id) : undefined;
  if (!endpoint || endpoint.computed) return null;
  if (endpoint.notComputedKind !== 'measurementChanged' && endpoint.notComputedKind !== 'noCommonStratum') return null;
  const comparison = input.bands?.find(band => band.name === 'Comparison');
  return comparison && Number.isFinite(comparison.start) ? comparison.start : null;
}

/** About 6.5 px a character for the 600-weight, 11 px point labels; in proportion at other sizes. */
const POINT_LABEL_CHAR_PX = 6.5;
const POINT_LABEL_CHAR_SIZE_PX = 11;

function config(
  datasets: DatasetSpec[],
  input: CcFigureInput,
  markers: CcChartMarker[],
  options: CcChartOptions,
  y: AxisSpec,
  style: ConfigStyle = {}
): CcChartConfig | null {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const chartStyle = options.style ?? CC_CHART_STYLE_DEFAULTS;
  const draw: DatasetLook = { style: chartStyle, hover: theme.tooltip !== null };
  const drawn = datasets.filter(spec => !options.hiddenSeries?.has(spec.id) && spec.data.some(point => point.y !== null));
  if (drawn.length === 0) return null;
  const bands = input.bands ?? [];
  const range = xRange(drawn.map(spec => spec.data), markers, bands, options.fitToData ?? false);
  const notAnalyzed = notAnalyzedOf(input);
  const marks = notAnalyzed ? { notAnalyzed, looks: drawn.map(spec => pointLook(spec, theme, draw)) } : null;

  const breakAt = style.breakAt ?? null;
  const whiskers = (style.whiskers ?? []).filter(whisker => drawn.some(spec => spec.id === whisker.seriesId));
  const reachOf = (spec: DatasetSpec) => {
    const own = whiskers.filter(whisker => whisker.seriesId === spec.id);
    return own.length === 0
      ? undefined
      : (point: CcChartPoint) => own.find(whisker => whisker.runId === point.runId && whisker.x === point.x) ?? null;
  };

  const formatOf = (spec: DatasetSpec) => spec.format ?? y.format;
  const pointLabels: CcPointLabelSet[] = style.pointLabels && chartStyle.valueLabels && drawn.length <= 2
    ? drawn.map((spec, datasetIndex) => {
      const reach = reachOf(spec);
      return { datasetIndex, format: formatOf(spec), below: datasetIndex === 1, ...(reach ? { reach } : {}) };
    })
    : [];
  // Room for half the widest point label, centered on the last point, and a 4 px margin.
  const charPx = POINT_LABEL_CHAR_PX * chartStyle.valueLabelSizePx / POINT_LABEL_CHAR_SIZE_PX;
  const widest = Math.max(0, ...pointLabels.flatMap((set, i) =>
    valuesOf(drawn[i].data).map(value => set.format(value).length)));
  const paddingRight = Math.max(8, widest > 0 ? Math.ceil(widest * charPx / 2) + 4 : 0);
  // The bounds follow the drawn series and their whiskers, as the time range does.
  const yAxis = y.policy
    ? ccValueAxis([...drawn.flatMap(spec => valuesOf(spec.data)), ...whiskers.flatMap(whisker => [whisker.low, whisker.high])], y.policy)
    : null;
  // A wash fills down to the axis's foot, so it only reads as an amount where that foot is zero.
  const wash = style.wash && chartStyle.areaWash && drawn.length === 1 && !drawn[0].hollow && yAxis !== null && yAxis.min === 0
    ? { datasetIndex: 0, color: drawn[0].color }
    : null;

  const series = new Map(drawn.map(spec => [spec.id, { name: seriesShortLabel(spec.id), format: formatOf(spec) }]));
  const plugins: Plugin<'line'>[] = [ccOverlayPlugin(markers, bands, theme, range, {
    header: options.header ?? null, logo: options.logo ?? null, wash, breakAt, pointLabels, notAnalyzed,
    labelFont: ccPointLabelFont(chartStyle), tagSizePx: chartStyle.markerTagSizePx, frame: chartStyle.plotFrame
  })];
  if (whiskers.length > 0) {
    const color = drawn.find(spec => spec.id === whiskers[0].seriesId)?.color ?? theme.series[0];
    plugins.push(ccWhiskerPlugin(whiskers, color, theme.muted, notAnalyzed));
  }
  return {
    type: 'line',
    data: {
      datasets: drawn.map(spec => notAnalyzed
        ? markedDataset(spec, theme, notAnalyzed, draw, breakAt)
        : dataset(spec, theme, draw, breakAt))
    },
    options: chartOptions(theme, options.reducedMotion ?? false, range,
      { input, series, marks, colors: drawn.length > 1 }, y, yAxis, drawn.length > 1, paddingRight,
      chartStyle, options.style ? options.style.labelWeight : null),
    plugins
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

/**
 * `ranged from 81.7 to 82.0 across 4 runs`, or `was 82.0 in all 4 runs` (`in both runs` for two)
 * where every value formats alike: what the points show, never a judgment such as *held*. `verb`
 * agrees with the measure's name: `were` for *Output tokens per answer*.
 */
function spanPhrase(values: readonly number[], format: (value: number) => string, noun: string, verb = 'was'): string {
  const { min, max } = rangeText(values, format);
  if (min !== max) return `ranged from ${min} to ${max} across ${plural(values.length, noun)}`;
  return values.length === 2 ? `${verb} ${min} in both ${noun}s` : `${verb} ${min} in all ${plural(values.length, noun)}`;
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
  return input.eventGroups
    ? { groups: input.eventGroups }
    : { harnessPoints: input.harnessPoints, numbering: input.eventNumbering };
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

// --- Interval whiskers ---

/** The data table's column of a battery run's 95 % interval. */
export const CC_INTERVAL_COLUMN = '95 % interval';

/** The interval cell of a point without one. */
const NO_INTERVAL = '—';

/** The interval note the server gives a half-width resting on one round. */
const QUESTION_SAMPLING_ONLY = 'question sampling only';

/** `79.6–84.5`: both ends to one decimal, joined by an en dash. */
export function ccIntervalText(low: number, high: number): string {
  return `${formatFixed(low, 1)}–${formatFixed(high, 1)}`;
}

/** The half-width of a battery point's Overall Index; null without a positive one. */
function overallIndexHalfWidth(point: CcTimelinePoint): number | null {
  const battery = batteryPointOf(point);
  const half = battery ? ccNumber(battery.overallIndexHalfWidth ?? null) : null;
  return half !== null && Number.isFinite(half) && half > 0 ? half : null;
}

/** A whisker per battery point with an Overall Index and its half-width, in `points` order. */
function overallIndexWhiskers(points: readonly CcTimelinePoint[], primary: readonly CcChartPoint[], seriesId: string): CcWhisker[] {
  return points.flatMap((point, i) => {
    const index = primary[i].y;
    const half = overallIndexHalfWidth(point);
    return index === null || half === null ? [] : [{ x: primary[i].x, low: index - half, high: index + half, runId: point.runId, seriesId }];
  });
}

/**
 * The takeaway's sentence on the whiskers. *(question sampling only)* is added when every whiskered
 * point's interval rests on question sampling alone, so the words never claim it of an interval that
 * does not.
 */
function whiskerSentence(points: readonly CcTimelinePoint[], whiskers: readonly CcWhisker[]): string {
  const ids = new Set(whiskers.map(whisker => whisker.runId));
  const samplingOnly = points
    .filter(point => ids.has(point.runId))
    .every(point => batteryPointOf(point)?.overallIndexIntervalNote?.trim().toLowerCase() === QUESTION_SAMPLING_ONLY);
  return `Bars show each battery run's 95 % interval for its own Overall Index${samplingOnly ? ' (question sampling only)' : ''}; `
    + 'they are not the interval of the change between the periods.';
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
    const spread = Math.max(...values) - Math.min(...values);
    takeaway = spread <= 3
      ? `${which} ${spanPhrase(values, format, noun)}.`
      : `${which} ${spanPhrase(values, format, noun)}; the latest ${noun} scored ${format(values[values.length - 1])}.`;
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
  // A battery run's 95 % interval of its own Overall Index, where its battery analysis gives one.
  const whiskers = battery ? overallIndexWhiskers(points, primary, primaryId) : [];
  if (whiskers.length > 0) takeaway += ` ${whiskerSentence(points, whiskers)}`;
  takeaway += notAnalyzedNote(input, [primary, common]);

  const datasets: DatasetSpec[] = [seriesSpec('quality', primaryId, primary, theme.series[0], { format: primaryFormat })];
  if (grader) datasets.push(seriesSpec('quality', 'quality.common', common, theme.series[1], { label: commonLabel, format: commonFormat }));
  const columns = [...leadColumns(input), battery ? 'Overall Intelligence Index' : 'Intelligence Index (native)'];
  // The interval column only while a point has an interval, so a figure without one keeps its table.
  if (whiskers.length > 0) columns.push(CC_INTERVAL_COLUMN);
  if (grader) columns.push(`Common grader (${grader.display})`);
  if (battery) columns.push('Note');
  return {
    key: 'quality',
    title: ccFigureTitle('quality'),
    takeaway,
    altText: `${altLead('Intelligence', input)} ${takeaway}`,
    config: config(datasets, input, markers, options,
      { title: 'Intelligence (0–100)', tick: numberTick, format: primaryFormat, policy: intelligencePolicy(options.zeroBaseline ?? false) },
      { pointLabels: true, wash: true, breakAt: ccPeriodBreak('quality', input), whiskers }),
    table: finishTable({
      columns,
      rows: points.map((point, i) => {
        const cells = [primaryFormat(primary[i].y)];
        if (whiskers.length > 0) {
          const whisker = whiskers.find(entry => entry.runId === point.runId);
          cells.push(whisker ? ccIntervalText(whisker.low, whisker.high) : NO_INTERVAL);
        }
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
    takeaway = proxyValues.length === 1
      ? `Only the legacy proxy is available: model time per answer was ${ms(proxyValues[0])} in the one ${noun}.`
      : `Only the legacy proxy is available: model time per answer ${spanPhrase(proxyValues, ms, noun)}.`;
  } else {
    takeaway = telemetryValues.length === 1
      ? `Median time to first answer text was ${ms(telemetryValues[0])} in the one telemetry ${noun}.`
      : `Median time to first answer text ${spanPhrase(telemetryValues, ms, `telemetry ${noun}`)}.`;
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
    { pointLabels: true, wash: true, breakAt: ccPeriodBreak('ttfat', input) }),
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
    takeaway = values.length === 1
      ? `The answer streaming rate was ${rate(values[0])} in the one ${noun} that recorded it.`
      : `The answer streaming rate ${spanPhrase(values, rate, noun)}.`;
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
    { pointLabels: true, wash: true, breakAt: ccPeriodBreak('rate', input) }),
    table: finishTable({
      columns: [...leadColumns(input), 'Streaming rate', 'Estimated'],
      rows: points.map(point => row(point, rate(point.medianStreamingRate), point.streamingRateEstimated ? 'Yes' : 'No'))
    }, points, input),
    markers
  };
}

/** What the work figure's points are next to the analysis's P4, which pairs the same questions. */
export const CC_WORK_ENDPOINT_NOTE =
  'The endpoint compares the same questions in both periods, so its estimate can differ from these points.';

/**
 * Work per turn: each point's mean output tokens over its delivered answers. Shown with an analysis,
 * the caption adds that the endpoint pairs the same questions, so the points are not its estimate.
 */
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
    takeaway = `Mean output tokens per answer were ${format(values[0])} in the one ${noun} of this range.`;
  } else {
    takeaway = `Mean output tokens per answer ${spanPhrase(values, format, noun, 'were')}.`;
  }
  if (values.length > 0 && ((input.endpoints?.length ?? 0) > 0 || (input.bands?.length ?? 0) > 0)) {
    takeaway += ` ${CC_WORK_ENDPOINT_NOTE}`;
  }
  takeaway += notAnalyzedNote(input, [tokens]);
  return {
    key: 'work',
    title: ccFigureTitle('work'),
    takeaway,
    altText: `${altLead('Mean output tokens per answer', input)} ${takeaway}`,
    config: config([seriesSpec('work', 'work.tokens', tokens, theme.series[4])], input, markers, options,
      { title: 'Output tokens', tick: integerTick, format, policy: ratioPolicy(100) },
      { pointLabels: true, wash: true, breakAt: ccPeriodBreak('work', input) }),
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
    takeaway = `Tool calls per answer ${spanPhrase(values, format, noun, 'were')}.`;
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
    takeaway = `Cost per question ${spanPhrase(values, usd, noun)}, at one price card.`;
  }
  takeaway += notAnalyzedNote(input, [cost]);
  return {
    key: 'cost',
    title: ccFigureTitle('cost'),
    takeaway,
    altText: `${altLead('Cost per question', input)} ${takeaway}`,
    config: config([seriesSpec('cost', 'cost.cost', cost, theme.series[5])], input, markers, options,
      { title: 'USD per question', tick: usdTick, format: usd, policy: ratioPolicy(0.01) },
      { pointLabels: true, wash: true, breakAt: ccPeriodBreak('cost', input) }),
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

/** One figure by key; with the input's endpoints, a figure whose endpoint is not computable says so first. */
export function buildCcFigure(key: CcFigureKey, input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  return withComparability(figureOf(key, input, options), input);
}

function figureOf(key: CcFigureKey, input: CcFigureInput, options: CcChartOptions): CcFigure {
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

/**
 * The figure with its caption and alt text opened by `ccNotComparableText` when its endpoint is in
 * the input's endpoints and not computed; unchanged otherwise.
 */
function withComparability(figure: CcFigure, input: CcFigureInput): CcFigure {
  const id = CC_FIGURE_ENDPOINTS[figure.key];
  const endpoint = id ? input.endpoints?.find(entry => entry.id === id) : undefined;
  if (!endpoint || endpoint.computed) return figure;
  const lead = ccNotComparableText(endpoint.notComputedReason);
  return { ...figure, takeaway: `${lead} ${figure.takeaway}`, altText: `${lead} ${figure.altText}` };
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
