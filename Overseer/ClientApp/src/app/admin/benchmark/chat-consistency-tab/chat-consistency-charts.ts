/**
 * The Chat Consistency charts: Chart.js configurations, their takeaway captions and their data
 * tables, built from a subject's timeline points. The builders are pure functions of their input, so
 * the timeline, the analysis results and the uploaded report charts draw the same figures from the
 * same data; `prefersReducedMotion()` alone reads the environment.
 *
 * Every chart is a line chart over a linear time axis in epoch milliseconds (no date adapter is
 * registered), with an overlay plugin that draws the period bands, Overseer events, annotations and
 * served-model changes as labeled markers. A legacy latency proxy is drawn with hollow points and a
 * dashed line, so it is told apart by shape as well as by its legend label.
 */

import type { ChartData, ChartOptions, Plugin, TooltipItem } from 'chart.js';

import {
  CcAnnotation,
  CcEvent,
  CcReportFigureKey,
  CcTimelinePoint
} from './chat-consistency.models';
import {
  annotationKindText,
  ccNumber,
  formatFixed,
  formatFractionPercent,
  formatInteger,
  formatMs,
  formatTokenRate,
  formatUsd,
  formatUtcDate,
  formatUtcDateTime,
  plural,
  utcMillis
} from './chat-consistency-format';

// --- Types ---

/** One plotted value; `runId` rides along for the tooltip. */
export interface CcChartPoint {
  x: number;
  y: number | null;
  runId: number;
}

export type CcMarkerKind = 'event' | 'annotation' | 'served';

/** A labeled vertical marker: an Overseer event, an annotation or a served-model change. */
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
  text: string;
  muted: string;
  grid: string;
  /** Painted behind the chart; null leaves the canvas transparent. */
  background: string | null;
  series: readonly string[];
  event: string;
  annotation: string;
  served: string;
  baselineBand: string;
  comparisonBand: string;
  fontFamily: string;
}

/** The dark application surface. The values are the design tokens' (`styles.scss`), which a canvas cannot read. */
export const CC_SCREEN_THEME: CcChartTheme = Object.freeze({
  text: '#e4e4e7',
  muted: '#a1a1aa',
  grid: 'rgba(255, 255, 255, 0.08)',
  background: null,
  series: ['#e0ba6d', '#7fe8d2', '#a8c7fa', '#f7b39b', '#81c784', '#ffb74d'],
  event: '#e0ba6d',
  annotation: '#a8c7fa',
  served: '#ffb74d',
  baselineBand: 'rgba(168, 199, 250, 0.08)',
  comparisonBand: 'rgba(224, 186, 109, 0.10)',
  fontFamily: '"Lato", "Segoe UI", Arial, sans-serif'
});

/** A light page, for the charts printed in PDF and Word. */
export const CC_PRINT_THEME: CcChartTheme = Object.freeze({
  text: '#1f2937',
  muted: '#4b5563',
  grid: 'rgba(0, 0, 0, 0.10)',
  background: '#ffffff',
  series: ['#8a6514', '#0f766e', '#1d4ed8', '#b45309', '#15803d', '#9d174d'],
  event: '#8a6514',
  annotation: '#1d4ed8',
  served: '#6d28d9',
  baselineBand: 'rgba(29, 78, 216, 0.06)',
  comparisonBand: 'rgba(138, 101, 20, 0.08)',
  fontFamily: '"Segoe UI", "Helvetica Neue", Arial, sans-serif'
});

/** What `<canvas baseChart>` and `renderPlotOffscreen` take. */
export interface CcChartConfig {
  type: 'line';
  data: ChartData<'line', CcChartPoint[]>;
  options: ChartOptions<'line'>;
  plugins: Plugin<'line'>[];
}

/** A chart's data as a real table, for the figure's *Show data* disclosure. */
export interface CcFigureTable {
  columns: string[];
  rows: string[][];
}

export type CcFigureKey = 'quality' | 'ttfat' | 'rate' | 'work' | 'cost' | 'reliability' | 'timeline';

export interface CcFigure {
  key: CcFigureKey;
  title: string;
  /** One sentence generated from the data, the figure's caption. */
  takeaway: string;
  /** The canvas's accessible name. */
  altText: string;
  /** Null when no run has a value to plot. */
  config: CcChartConfig | null;
  table: CcFigureTable;
  markers: CcChartMarker[];
}

export interface CcFigureInput {
  points: readonly CcTimelinePoint[];
  events?: readonly CcEvent[];
  annotations?: readonly CcAnnotation[];
  bands?: readonly CcPeriodBand[];
}

export interface CcChartOptions {
  theme?: CcChartTheme;
  /** Chart.js animation off; set from `prefers-reduced-motion`, and always for an offscreen render. */
  reducedMotion?: boolean;
}

/** The report figure each uploaded chart key draws. */
export const CC_REPORT_FIGURES: Readonly<Record<CcReportFigureKey, CcFigureKey>> = Object.freeze({
  'cc1-quality': 'quality',
  'cc2-speed': 'ttfat',
  'cc3-work': 'work',
  'cc4-timeline': 'timeline'
});

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

/** The common-grader snapshot that covers the most runs, with its display name; null when none does. */
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

/** The latest calibration's mean quality of `snapshotId` on one run, or null. */
export function commonGraderQualityOf(point: CcTimelinePoint, snapshotId: number): number | null {
  const entries = point.commonGraderQuality
    .filter(entry => entry.snapshotId === snapshotId)
    .sort((a, b) => utcMillis(b.calibratedAtUtc) - utcMillis(a.calibratedAtUtc));
  return entries.length > 0 ? ccNumber(entries[0].meanQuality) : null;
}

/** The provider-reported model id that served most of a run's calls, or null. */
export function dominantServedModel(point: CcTimelinePoint): string | null {
  let best: { modelId: string; callCount: number } | null = null;
  for (const entry of point.servedModelIds) {
    if (!best || entry.callCount > best.callCount) best = entry;
  }
  return best?.modelId ?? null;
}

/**
 * The markers of the range: Overseer events (`E1`…), annotations (`A1`…) and the runs whose dominant
 * served model differs from the previous run's (`S1`…), each kind numbered in time order.
 */
export function buildMarkers(
  points: readonly CcTimelinePoint[],
  events: readonly CcEvent[] = [],
  annotations: readonly CcAnnotation[] = []
): CcChartMarker[] {
  const markers: CcChartMarker[] = [];
  const timed = <T>(items: readonly T[], at: (item: T) => string) => items
    .map(item => ({ item, x: utcMillis(at(item)) }))
    .filter(entry => Number.isFinite(entry.x))
    .sort((a, b) => a.x - b.x);

  timed(events, event => event.atUtc).forEach(({ item, x }, i) =>
    markers.push({ kind: 'event', x, tag: `E${i + 1}`, label: `Overseer change: ${item.label}` }));
  timed(annotations, annotation => annotation.atUtc).forEach(({ item, x }, i) =>
    markers.push({ kind: 'annotation', x, tag: `A${i + 1}`, label: `${annotationKindText(item.kind)}: ${item.text}` }));

  let previous: string | null = null;
  let served = 0;
  for (const point of sortedPoints(points)) {
    const current = dominantServedModel(point);
    if (current === null) continue;
    if (previous !== null && current !== previous) {
      served++;
      markers.push({
        kind: 'served', x: utcMillis(point.startedAtUtc), tag: `S${served}`,
        label: `Served model changed from ${previous} to ${current} (run #${point.runId})`
      });
    }
    previous = current;
  }
  return markers;
}

// --- The overlay plugin ---

/**
 * Draws the period bands under the datasets, and the markers over them: a vertical line per marker
 * and its tag in a small box at the top, the line dashed for an event, dotted for an annotation and
 * dash-dotted for a served-model change, so the kinds differ by shape as well as by color.
 */
export function ccOverlayPlugin(
  markers: readonly CcChartMarker[],
  bands: readonly CcPeriodBand[],
  theme: CcChartTheme
): Plugin<'line'> {
  const dash: Record<CcMarkerKind, number[]> = { event: [6, 4], annotation: [2, 3], served: [8, 3, 2, 3] };
  const color: Record<CcMarkerKind, string> = { event: theme.event, annotation: theme.annotation, served: theme.served };
  return {
    id: 'ccOverlay',
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
        ctx.textBaseline = 'bottom';
        ctx.fillText(band.name, left + 4, area.bottom - 4);
      }
      ctx.restore();
    },
    afterDatasetsDraw(chart) {
      const x = chart.scales['x'];
      const area = chart.chartArea;
      if (!x || !area) return;
      const { ctx } = chart;
      ctx.save();
      ctx.font = `bold 10px ${theme.fontFamily}`;
      ctx.textBaseline = 'middle';
      ctx.textAlign = 'center';
      for (const marker of markers) {
        const px = x.getPixelForValue(marker.x);
        if (!Number.isFinite(px) || px < area.left || px > area.right) continue;
        ctx.strokeStyle = color[marker.kind];
        ctx.lineWidth = 1.5;
        ctx.setLineDash(dash[marker.kind]);
        ctx.beginPath();
        ctx.moveTo(px, area.top + 14);
        ctx.lineTo(px, area.bottom);
        ctx.stroke();
        ctx.setLineDash([]);
        const width = ctx.measureText(marker.tag).width + 8;
        ctx.fillStyle = theme.background ?? 'rgba(17, 17, 17, 0.9)';
        ctx.fillRect(px - width / 2, area.top, width, 14);
        ctx.strokeRect(px - width / 2, area.top, width, 14);
        ctx.fillStyle = color[marker.kind];
        ctx.fillText(marker.tag, px, area.top + 7);
      }
      ctx.restore();
    }
  };
}

// --- Options ---

interface AxisSpec {
  title: string;
  format: (value: number) => string;
  beginAtZero?: boolean;
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

function chartOptions(
  theme: CcChartTheme,
  reducedMotion: boolean,
  range: { min: number; max: number } | null,
  y: AxisSpec,
  y1?: AxisSpec
): ChartOptions<'line'> {
  const tick = { color: theme.muted, font: { family: theme.fontFamily, size: 11 } };
  const grid = { color: theme.grid };
  const options: ChartOptions<'line'> = {
    responsive: true,
    maintainAspectRatio: false,
    animation: reducedMotion ? false : { duration: 250 },
    interaction: { mode: 'nearest', intersect: false },
    layout: { padding: { top: 4, right: 8 } },
    scales: {
      x: {
        type: 'linear',
        ...(range ? { min: range.min, max: range.max } : {}),
        ticks: { ...tick, maxTicksLimit: 6, callback: value => formatUtcDate(Number(value)) },
        grid,
        title: { display: true, text: 'Run start (UTC)', color: theme.muted, font: { family: theme.fontFamily, size: 11 } }
      },
      y: {
        type: 'linear',
        beginAtZero: y.beginAtZero ?? false,
        ticks: { ...tick, callback: value => y.format(Number(value)) },
        grid,
        title: { display: true, text: y.title, color: theme.muted, font: { family: theme.fontFamily, size: 11 } }
      },
      ...(y1 ? {
        y1: {
          type: 'linear' as const,
          position: 'right' as const,
          beginAtZero: y1.beginAtZero ?? false,
          ticks: { ...tick, callback: (value: string | number) => y1.format(Number(value)) },
          grid: { drawOnChartArea: false },
          title: { display: true, text: y1.title, color: theme.muted, font: { family: theme.fontFamily, size: 11 } }
        }
      } : {})
    },
    plugins: {
      legend: { labels: { color: theme.text, font: { family: theme.fontFamily, size: 12 }, usePointStyle: true } },
      tooltip: {
        callbacks: {
          title: (items: TooltipItem<'line'>[]) => {
            const raw = items[0]?.raw as CcChartPoint | undefined;
            return raw ? `Run #${raw.runId} · ${formatUtcDateTime(raw.x)}` : '';
          },
          label: (item: TooltipItem<'line'>) => {
            const raw = item.raw as CcChartPoint;
            const format = item.dataset.yAxisID === 'y1' && y1 ? y1.format : y.format;
            return `${item.dataset.label}: ${raw.y === null ? '—' : format(raw.y)}`;
          }
        }
      }
    }
  };
  return options;
}

interface DatasetSpec {
  label: string;
  data: CcChartPoint[];
  color: string;
  hollow?: boolean;
  yAxisID?: 'y' | 'y1';
}

function dataset(spec: DatasetSpec, theme: CcChartTheme): ChartData<'line', CcChartPoint[]>['datasets'][number] {
  return {
    label: spec.label,
    data: spec.data,
    yAxisID: spec.yAxisID ?? 'y',
    borderColor: spec.color,
    backgroundColor: spec.hollow ? (theme.background ?? 'rgba(0, 0, 0, 0)') : spec.color,
    pointBackgroundColor: spec.hollow ? (theme.background ?? 'rgba(0, 0, 0, 0)') : spec.color,
    pointBorderColor: spec.color,
    pointBorderWidth: spec.hollow ? 2 : 1,
    pointRadius: 3.5,
    pointHoverRadius: 5,
    borderWidth: 1.5,
    borderDash: spec.hollow ? [4, 4] : [],
    spanGaps: false,
    tension: 0
  };
}

function config(
  datasets: DatasetSpec[],
  input: CcFigureInput,
  markers: CcChartMarker[],
  options: CcChartOptions,
  y: AxisSpec,
  y1?: AxisSpec
): CcChartConfig | null {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const drawn = datasets.filter(spec => spec.data.some(point => point.y !== null));
  if (drawn.length === 0) return null;
  const bands = input.bands ?? [];
  return {
    type: 'line',
    data: { datasets: drawn.map(spec => dataset(spec, theme)) },
    options: chartOptions(theme, options.reducedMotion ?? false, xRange(drawn.map(spec => spec.data), markers, bands), y, y1),
    plugins: [ccOverlayPlugin(markers, bands, theme)]
  };
}

function rangeText(values: readonly number[], format: (value: number) => string): { min: string; max: string } {
  return { min: format(Math.min(...values)), max: format(Math.max(...values)) };
}

function row(point: CcTimelinePoint, ...cells: string[]): string[] {
  return [`#${point.runId}`, formatUtcDateTime(point.startedAtUtc), ...cells];
}

// --- The figures ---

/** Quality Index per run, native grades and, where one covers runs, the common grader's mean quality. */
export function qualityFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = buildMarkers(points, input.events, input.annotations);
  const native = seriesOf(points, point => point.qualityIndex ?? null);
  const grader = dominantCommonGrader(points);
  const common = grader ? seriesOf(points, point => commonGraderQualityOf(point, grader.snapshotId)) : [];
  const nativeLabel = 'Quality Index (native grades)';
  const commonLabel = grader ? `Mean quality (common grader: ${grader.display})` : '';

  const nativeValues = valuesOf(native);
  const commonValues = valuesOf(common);
  const values = nativeValues.length > 0 ? nativeValues : commonValues;
  const which = nativeValues.length > 0 ? 'Quality' : 'Common-grader quality';
  let takeaway: string;
  if (values.length === 0) {
    takeaway = 'No run in this range has a quality figure.';
  } else if (values.length === 1) {
    takeaway = `${which} was ${formatFixed(values[0], 0)} in the one run of this range.`;
  } else {
    const { min, max } = rangeText(values, value => formatFixed(value, 0));
    const spread = Math.max(...values) - Math.min(...values);
    takeaway = spread <= 3
      ? `${which} held between ${min} and ${max} across ${plural(values.length, 'run')}.`
      : `${which} ranged from ${min} to ${max} across ${plural(values.length, 'run')}; the latest run scored ${formatFixed(values[values.length - 1], 0)}.`;
  }
  if (grader && nativeValues.length > 0 && commonValues.length > 0) {
    takeaway += ` ${plural(commonValues.length, 'run')} also ${commonValues.length === 1 ? 'has' : 'have'} a common-grader figure.`;
  }

  const datasets: DatasetSpec[] = [{ label: nativeLabel, data: native, color: theme.series[0] }];
  if (grader) datasets.push({ label: commonLabel, data: common, color: theme.series[1] });
  const columns = ['Run', 'Started', 'Quality Index (native)'];
  if (grader) columns.push(`Common grader (${grader.display})`);
  return {
    key: 'quality',
    title: 'Quality per run',
    takeaway,
    altText: `Quality per run. ${takeaway}`,
    config: config(datasets, input, markers, options, { title: 'Quality (0–100)', format: value => formatFixed(value, 0) }),
    table: {
      columns,
      rows: points.map((point, i) => grader
        ? row(point, formatFixed(native[i].y, 0), formatFixed(common[i].y, 1))
        : row(point, formatFixed(native[i].y, 0)))
    },
    markers
  };
}

/** Median time to first answer text per run; legacy runs as their model-time proxy, hollow. */
export function timeToFirstAnswerFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = buildMarkers(points, input.events, input.annotations);
  const telemetry = seriesOf(points, point => ccNumber(point.medianTimeToFirstAnswerTextMs));
  const proxy = seriesOf(points, point => ccNumber(point.medianTimeToFirstAnswerTextMs) === null
    ? ccNumber(point.medianModelTimeMs) : null);
  const telemetryValues = valuesOf(telemetry);
  const proxyValues = valuesOf(proxy);

  let takeaway: string;
  if (telemetryValues.length === 0 && proxyValues.length === 0) {
    takeaway = 'No run in this range has a latency figure.';
  } else if (telemetryValues.length === 0) {
    const { min, max } = rangeText(proxyValues, formatMs);
    takeaway = `Only the legacy proxy is available: model time per answer ranged from ${min} to ${max} across ${plural(proxyValues.length, 'run')}.`;
  } else {
    const { min, max } = rangeText(telemetryValues, formatMs);
    takeaway = telemetryValues.length === 1
      ? `Median time to first answer text was ${min} in the one telemetry run.`
      : `Median time to first answer text ranged from ${min} to ${max} across ${plural(telemetryValues.length, 'telemetry run')}.`;
    if (proxyValues.length > 0) {
      takeaway += ` ${plural(proxyValues.length, 'legacy run')} ${proxyValues.length === 1 ? 'is' : 'are'} drawn hollow as the legacy proxy.`;
    }
  }
  return {
    key: 'ttfat',
    title: 'Time to first answer text',
    takeaway,
    altText: `Time to first answer text per run. ${takeaway}`,
    config: config([
      { label: 'Time to first answer text (telemetry)', data: telemetry, color: theme.series[0] },
      { label: 'Model time per answer (legacy proxy)', data: proxy, color: theme.series[3], hollow: true }
    ], input, markers, options, { title: 'Median time', format: formatMs, beginAtZero: true }),
    table: {
      columns: ['Run', 'Started', 'Time to first answer text', 'Legacy proxy', 'Measure'],
      rows: points.map((point, i) => row(point, formatMs(telemetry[i].y), formatMs(proxy[i].y),
        point.latencyLabel || (point.isLegacy ? 'legacy proxy' : 'telemetry')))
    },
    markers
  };
}

/** Median answer streaming rate per run; an estimated rate is drawn hollow. */
export function streamingRateFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = buildMarkers(points, input.events, input.annotations);
  const measured = seriesOf(points, point => point.streamingRateEstimated ? null : ccNumber(point.medianStreamingRate));
  const estimated = seriesOf(points, point => point.streamingRateEstimated ? ccNumber(point.medianStreamingRate) : null);
  const values = [...valuesOf(measured), ...valuesOf(estimated)];
  const legacy = points.filter(point => point.isLegacy).length;

  let takeaway: string;
  if (values.length === 0) {
    takeaway = 'No run in this range has a streaming rate; legacy runs did not record one.';
  } else {
    const { min, max } = rangeText(values, formatTokenRate);
    takeaway = values.length === 1
      ? `The answer streaming rate was ${min} in the one run that recorded it.`
      : `The answer streaming rate ranged from ${min} to ${max} across ${plural(values.length, 'run')}.`;
    if (legacy > 0) takeaway += ` ${plural(legacy, 'legacy run')} recorded no rate.`;
  }
  return {
    key: 'rate',
    title: 'Answer streaming rate',
    takeaway,
    altText: `Answer streaming rate per run. ${takeaway}`,
    config: config([
      { label: 'Streaming rate (measured)', data: measured, color: theme.series[1] },
      { label: 'Streaming rate (estimated)', data: estimated, color: theme.series[1], hollow: true }
    ], input, markers, options, { title: 'Tokens per second', format: value => formatFixed(value, 0), beginAtZero: true }),
    table: {
      columns: ['Run', 'Started', 'Streaming rate', 'Estimated'],
      rows: points.map(point => row(point, formatTokenRate(point.medianStreamingRate), point.streamingRateEstimated ? 'Yes' : 'No'))
    },
    markers
  };
}

/** Output tokens and tool calls per answer. */
export function workFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = buildMarkers(points, input.events, input.annotations);
  const tokens = seriesOf(points, point => ccNumber(point.outputTokensPerAnswer));
  const tools = seriesOf(points, point => ccNumber(point.toolCallsPerAnswer));
  const tokenValues = valuesOf(tokens);
  const toolValues = valuesOf(tools);

  const parts: string[] = [];
  if (tokenValues.length > 0) {
    const { min, max } = rangeText(tokenValues, formatInteger);
    parts.push(`output tokens per answer ranged from ${min} to ${max}`);
  }
  if (toolValues.length > 0) {
    const { min, max } = rangeText(toolValues, value => formatFixed(value, 1));
    parts.push(`tool calls per answer from ${min} to ${max}`);
  }
  const count = Math.max(tokenValues.length, toolValues.length);
  const takeaway = parts.length === 0
    ? 'No run in this range has a work figure.'
    : `Across ${plural(count, 'run')}, ${parts.join('; ')}.`;
  return {
    key: 'work',
    title: 'Work per answer',
    takeaway,
    altText: `Work per answer per run. ${takeaway}`,
    config: config([
      { label: 'Output tokens per answer', data: tokens, color: theme.series[2] },
      { label: 'Tool calls per answer', data: tools, color: theme.series[4], yAxisID: 'y1' }
    ], input, markers, options,
    { title: 'Output tokens', format: formatInteger, beginAtZero: true },
    { title: 'Tool calls', format: value => formatFixed(value, 1), beginAtZero: true }),
    table: {
      columns: ['Run', 'Started', 'Output tokens per answer', 'Tool calls per answer'],
      rows: points.map((point, i) => row(point, formatInteger(tokens[i].y), formatFixed(tools[i].y, 1)))
    },
    markers
  };
}

/** Candidate cost per question. */
export function costFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = buildMarkers(points, input.events, input.annotations);
  const cost = seriesOf(points, point => ccNumber(point.costPerQuestionUsd));
  const values = valuesOf(cost);
  let takeaway: string;
  if (values.length === 0) {
    takeaway = 'No run in this range has a cost figure.';
  } else if (values.length === 1) {
    takeaway = `Cost per question was ${formatUsd(values[0])} in the one run of this range.`;
  } else {
    const { min, max } = rangeText(values, formatUsd);
    takeaway = `Cost per question ranged from ${min} to ${max} across ${plural(values.length, 'run')}, at one price card.`;
  }
  return {
    key: 'cost',
    title: 'Cost per question',
    takeaway,
    altText: `Cost per question per run. ${takeaway}`,
    config: config([{ label: 'Cost per question (USD)', data: cost, color: theme.series[5] }], input, markers, options,
      { title: 'USD per question', format: formatUsd, beginAtZero: true }),
    table: {
      columns: ['Run', 'Started', 'Cost per question'],
      rows: points.map((point, i) => row(point, formatUsd(cost[i].y)))
    },
    markers
  };
}

const RELIABILITY_RATES: readonly { key: keyof CcTimelinePoint; label: string }[] = [
  { key: 'terminalFailureRate', label: 'Terminal failures' },
  { key: 'timeoutRate', label: 'Timeouts' },
  { key: 'emptyAnswerRate', label: 'Empty answers' },
  { key: 'refusalRate', label: 'Refusals' },
  { key: 'toolBudgetExhaustedRate', label: 'Tool budget exhausted' }
];

/** The five reliability rates per run, as percentages. */
export function reliabilityFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = buildMarkers(points, input.events, input.annotations);
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
  const takeaway = counted === 0
    ? 'No run in this range has reliability figures.'
    : worst === null
      ? `No terminal failures, timeouts, empty answers, refusals or exhausted tool budgets in ${plural(counted, 'run')}.`
      : `The highest rate was ${worst.label.toLowerCase()} at ${formatFixed(worst.value, 1)} % in run #${worst.runId}, across ${plural(counted, 'run')}.`;
  return {
    key: 'reliability',
    title: 'Reliability',
    takeaway,
    altText: `Reliability rates per run. ${takeaway}`,
    config: config(series.map((rate, i) => ({ label: rate.label, data: rate.data, color: theme.series[i % theme.series.length] })),
      input, markers, options, { title: 'Share of answers (%)', format: value => `${formatFixed(value, 0)} %`, beginAtZero: true }),
    table: {
      columns: ['Run', 'Started', ...RELIABILITY_RATES.map(rate => rate.label)],
      rows: points.map(point => row(point,
        ...RELIABILITY_RATES.map(rate => formatFractionPercent(point[rate.key] as number | string | null))))
    },
    markers
  };
}

/** The runs on one line over time, with every marker: what happened when, for the reports. */
export function timelineOverviewFigure(input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  const theme = options.theme ?? CC_SCREEN_THEME;
  const points = sortedPoints(input.points);
  const markers = buildMarkers(points, input.events, input.annotations);
  const telemetry = seriesOf(points, point => point.isLegacy ? null : 1);
  const legacy = seriesOf(points, point => point.isLegacy ? 1 : null);
  const count = (kind: CcMarkerKind) => markers.filter(marker => marker.kind === kind).length;
  const takeaway = points.length === 0
    ? 'No run in this range.'
    : `${plural(points.length, 'run')}, ${plural(count('event'), 'Overseer change')}, ${plural(count('annotation'), 'annotation')} `
      + `and ${plural(count('served'), 'served-model change')} in this range.`;
  const built = config([
    { label: 'Run with call telemetry', data: telemetry, color: theme.series[0] },
    { label: 'Legacy run', data: legacy, color: theme.series[3], hollow: true }
  ], input, markers, options, { title: '', format: () => '' });
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
    title: 'Runs and events',
    takeaway,
    altText: `Runs, Overseer changes, annotations and served-model changes over time. ${takeaway}`,
    config: built,
    table: {
      columns: ['Run', 'Started', 'Suite', 'Measure', 'Served model'],
      rows: points.map(point => row(point, point.suiteName, point.isLegacy ? 'legacy' : 'telemetry', dominantServedModel(point) ?? '—'))
    },
    markers
  };
}

/** Every figure of the timeline, in the order the tab shows them. */
export function buildCcFigures(input: CcFigureInput, options: CcChartOptions = {}): CcFigure[] {
  return [
    qualityFigure(input, options),
    timeToFirstAnswerFigure(input, options),
    streamingRateFigure(input, options),
    workFigure(input, options),
    costFigure(input, options),
    reliabilityFigure(input, options),
    timelineOverviewFigure(input, options)
  ];
}

/** One figure by key. */
export function buildCcFigure(key: CcFigureKey, input: CcFigureInput, options: CcChartOptions = {}): CcFigure {
  switch (key) {
    case 'quality': return qualityFigure(input, options);
    case 'ttfat': return timeToFirstAnswerFigure(input, options);
    case 'rate': return streamingRateFigure(input, options);
    case 'work': return workFigure(input, options);
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
