/**
 * The AI Telemetry charts: nine line charts over the series endpoint's buckets, their Chart.js
 * configurations, and their composition through the GnollBench figure layer (`renderPlotOffscreen` →
 * `composeFigureImage`), so the screen, Copy, Download and Download all show one image.
 *
 * Six charts read the usage lines (`dto.series`) and follow *Split by* and *Keys*; the three activity
 * charts read `dto.activity` and do not. Every chart is a line chart over a linear time axis in epoch
 * milliseconds at bucket start, ticked by Chat Consistency's UTC helpers (no date adapter is
 * registered). A value that cannot be computed, such as a ratio over nothing, is a gap, never a zero.
 * Pure apart from `renderPlotOffscreen` and `composeFigureImage`.
 */

import type { Chart, ChartDataset, ChartOptions, Plugin, Scale, Tick } from 'chart.js';

import {
  AiTelemetryActivityPointDto,
  AiTelemetryKeySource,
  AiTelemetryPointDto,
  AiTelemetrySeriesDto,
  AiTelemetrySplitBy
} from '../../../services/admin.service';
import { parseServerUtcDate } from '../../../utils/date.util';
import {
  FigureBadge,
  FigureChrome,
  FigureFooter,
  FigureNote,
  figureSummary,
  formatComputedAt,
  visibleBadges
} from '../../benchmark/model-comparison/figure-chrome';
import {
  FigureExportFormat,
  FigureExportLayout,
  FigureExportRequest,
  OffscreenPlotConfig,
  composeFigureImage,
  exportTimestamp,
  renderPlotOffscreen,
  resolveFigureLayout
} from '../../benchmark/model-comparison/figure-export';
import { FigureLogo } from '../../benchmark/model-comparison/figure-logo';
import {
  FigureSizeSettings,
  resolveSizeDensity,
  resolveSizeResolution,
  sizeErrors
} from '../../benchmark/model-comparison/figure-size';
import { FigureAppearanceStyle, TimelineFigureStyle } from '../../benchmark/model-comparison/figure-style';
import { ResolvedFigureTheme, providerHue, withAlpha } from '../../benchmark/model-comparison/figure-theme';
import {
  CC_HOUR_TICKS_SPAN_MS,
  ccAxisTimeTicks,
  ccTimeTickLabel
} from '../../benchmark/chat-consistency-tab/chat-consistency-charts';

// --- Figures ---

export type TelemetryFigureKey =
  | 'requests' | 'cost' | 'tokens' | 'cacheHit' | 'duration' | 'providerErrors' | 'activeUsers' | 'chats' | 'messages';

/** The nine charts, in the order the dialog shows them. */
export const TELEMETRY_FIGURES: readonly { readonly key: TelemetryFigureKey; readonly title: string }[] = Object.freeze([
  { key: 'requests', title: 'Requests' },
  { key: 'cost', title: 'Estimated cost (USD)' },
  { key: 'tokens', title: 'Tokens' },
  { key: 'cacheHit', title: 'Cache hit ratio' },
  { key: 'duration', title: 'Mean duration' },
  { key: 'providerErrors', title: 'Provider error rate' },
  { key: 'activeUsers', title: 'Active users' },
  { key: 'chats', title: 'Chats created' },
  { key: 'messages', title: 'Messages' }
]);

export const TELEMETRY_FIGURE_KEYS: readonly TelemetryFigureKey[] = TELEMETRY_FIGURES.map(entry => entry.key);

const ACTIVITY_FIGURES: ReadonlySet<TelemetryFigureKey> = new Set<TelemetryFigureKey>(['activeUsers', 'chats', 'messages']);

/** The chart reads `dto.activity`, apart from *Split by* and *Keys*. */
export function isActivityFigure(key: TelemetryFigureKey): boolean {
  return ACTIVITY_FIGURES.has(key);
}

export function telemetryFigureTitle(key: TelemetryFigureKey): string {
  return TELEMETRY_FIGURES.find(entry => entry.key === key)?.title ?? key;
}

/** The note every activity chart carries. */
export const ACTIVITY_SCOPE_NOTE = 'Split by and Keys do not apply to this chart.';

/** The provider error rate chart's note when it leaves out the Benchmark role's lines. */
export const BENCHMARK_AVAILABILITY_NOTE = 'GnollBench provider availability is reported in GnollBench.';

/** The Chats created chart's note: the two kinds are counted within Chats. */
export const CHATS_SUBSET_NOTE = 'Confidential and GnollHack chats are also counted in Chats.';

/** Why the incognito lines are left out for one user. */
export const INCOGNITO_USER_NOTE = 'Incognito chats and messages are counted only anonymously, so one user\'s charts leave them out.';

// --- Series ---

/** One line's values, aligned with the buckets; null is a gap. */
export interface TelemetrySeriesLine {
  readonly key: string;
  readonly label: string;
  /** For provider colors; null where the line has none. */
  readonly provider: string | null;
  readonly isOther: boolean;
  readonly values: readonly (number | null)[];
}

export interface TelemetrySeries {
  readonly lines: readonly TelemetrySeriesLine[];
  readonly notes: readonly FigureNote[];
}

export interface TelemetrySeriesOptions {
  /** The cost chart sums each line from the first bucket on. */
  readonly cumulative?: boolean;
}

/** `value` ÷ `over`, or null where `over` is 0: a gap rather than 0. */
function ratio(value: number, over: number): number | null {
  return over > 0 ? value / over : null;
}

/** Each bucket start in epoch ms. */
export function telemetryBucketStarts(dto: AiTelemetrySeriesDto): number[] {
  return dto.bucketStartsUtc.map(start => parseServerUtcDate(start).getTime());
}

/** `2026-09-01`, the UTC day of a server timestamp; '' when it does not parse. */
function utcDay(value: string): string {
  const date = parseServerUtcDate(value);
  return Number.isNaN(date.getTime()) ? '' : date.toISOString().slice(0, 10);
}

/** The timestamp in epoch ms, or NaN. */
function utcMillis(value: string | null | undefined): number {
  return value ? parseServerUtcDate(value).getTime() : Number.NaN;
}

/** `Cost recorded since 2026-09-01.` when the recording started after the first bucket; '' otherwise. */
function recordedSinceText(what: string, since: string | null | undefined, firstBucket: number | undefined): string {
  const start = utcMillis(since);
  if (!Number.isFinite(start) || firstBucket === undefined || !(start > firstBucket)) return '';
  return `${what} recorded since ${utcDay(since!)}.`;
}

/** The share of requests without a price, as a note; null when every request is priced. */
function unpricedNote(points: readonly AiTelemetryPointDto[]): FigureNote | null {
  const requests = points.reduce((sum, point) => sum + point.requests, 0);
  const priced = points.reduce((sum, point) => sum + point.pricedRequests, 0);
  if (requests <= 0 || priced >= requests) return null;
  const share = (requests - priced) / requests * 100;
  const text = share < 1 ? share.toFixed(1) : String(Math.round(share));
  return { text: `${text}% of requests have no price and are not in the cost.`, tone: 'warning' };
}

const USAGE_VALUE: Readonly<Record<Exclude<TelemetryFigureKey, 'activeUsers' | 'chats' | 'messages'>, (point: AiTelemetryPointDto) => number | null>> = {
  requests: point => point.requests,
  cost: point => point.estimatedCost,
  tokens: point => point.promptTokens + point.outputTokens,
  cacheHit: point => ratio(point.cacheReadTokens, point.promptTokens),
  duration: point => ratio(point.durationSumMs, point.durationCount),
  providerErrors: point => ratio(point.providerErrors, point.validAttempts)
};

/** One activity line: its key, label and value per bucket. */
interface ActivityLine {
  readonly key: string;
  readonly label: string;
  readonly value: (point: AiTelemetryActivityPointDto) => number;
  /** Counted only anonymously, so left out under a user filter. */
  readonly incognito?: boolean;
}

const ACTIVITY_LINES: Readonly<Record<'activeUsers' | 'chats' | 'messages', readonly ActivityLine[]>> = {
  activeUsers: [
    { key: 'activeUsers', label: 'Active users', value: point => point.activeUsers },
    { key: 'newUsers', label: 'New users', value: point => point.newUsers }
  ],
  chats: [
    { key: 'chats', label: 'Chats', value: point => point.chatsCreated },
    { key: 'confidential', label: 'Confidential', value: point => point.confidentialChatsCreated },
    { key: 'gnollhack', label: 'GnollHack', value: point => point.gnollHackChatsCreated },
    { key: 'incognito', label: 'Incognito', value: point => point.incognitoChatsCreated, incognito: true }
  ],
  messages: [
    { key: 'sent', label: 'Sent', value: point => point.userMessages },
    { key: 'replies', label: 'Replies', value: point => point.assistantMessages },
    { key: 'incognitoSent', label: 'Incognito sent', value: point => point.incognitoUserMessages, incognito: true }
  ]
};

/** A line of the Benchmark role, which the provider error rate chart leaves to GnollBench. */
function isBenchmarkRoleLine(dto: AiTelemetrySeriesDto, key: string): boolean {
  return dto.splitBy === 'role' && key.toLowerCase() === 'benchmark';
}

/**
 * The activity chart's lines: ignoring *Split by* and *Keys*, a gap in every bucket that starts before
 * the counting began, and no incognito lines for one user.
 */
function activitySeries(dto: AiTelemetrySeriesDto, key: 'activeUsers' | 'chats' | 'messages'): TelemetrySeries {
  const starts = telemetryBucketStarts(dto);
  const counted = utcMillis(dto.activityCountedSinceUtc);
  const activity = dto.activity ?? [];
  const userFiltered = !!dto.userId;
  const notes: FigureNote[] = [{ text: ACTIVITY_SCOPE_NOTE, tone: 'info' }];
  if (key === 'chats') notes.push({ text: CHATS_SUBSET_NOTE, tone: 'info' });
  if (!Number.isFinite(counted)) {
    notes.push({ text: 'Users, chats and messages are not counted yet.', tone: 'info' });
  } else if (starts.length > 0 && starts[0] < counted) {
    notes.push({ text: `Counted since ${utcDay(dto.activityCountedSinceUtc!)}.`, tone: 'info' });
  }
  const lines = ACTIVITY_LINES[key]
    .filter(line => !(userFiltered && line.incognito))
    .map((line): TelemetrySeriesLine => ({
      key: line.key,
      label: line.label,
      provider: null,
      isOther: false,
      values: starts.map((start, index) => {
        const point = activity[index];
        // A bucket starting before the counting began holds part of its days at most: a gap.
        if (!point || !Number.isFinite(counted) || start < counted) return null;
        return line.value(point);
      })
    }));
  if (userFiltered && ACTIVITY_LINES[key].some(line => line.incognito)) {
    notes.push({ text: INCOGNITO_USER_NOTE, tone: 'info' });
  }
  return { lines, notes };
}

/**
 * One chart's lines and notes from the series: requests; the estimated cost, summed from the first
 * bucket when `cumulative`; tokens as prompt plus output; the cache hit ratio as cache read ÷ prompt;
 * the mean duration as the duration sum ÷ its count; the provider error rate as provider errors ÷
 * valid attempts, without the Benchmark role's lines. A ratio over nothing is null, a gap.
 */
export function buildFigureSeries(
  dto: AiTelemetrySeriesDto,
  key: TelemetryFigureKey,
  options: TelemetrySeriesOptions = {}
): TelemetrySeries {
  if (key === 'activeUsers' || key === 'chats' || key === 'messages') return activitySeries(dto, key);

  const firstBucket = telemetryBucketStarts(dto)[0];
  const notes: FigureNote[] = [];
  let source = dto.series;
  if (key === 'providerErrors') {
    const kept = source.filter(line => !isBenchmarkRoleLine(dto, line.key));
    if (kept.length !== source.length) notes.push({ text: BENCHMARK_AVAILABILITY_NOTE, tone: 'info' });
    source = kept;
    const since = recordedSinceText('Attempts', dto.attemptsRecordedSinceUtc, firstBucket);
    if (since) notes.push({ text: since, tone: 'info' });
  }
  if (key === 'cost') {
    const unpriced = unpricedNote(source.flatMap(line => line.points));
    if (unpriced) notes.push(unpriced);
    const since = recordedSinceText('Cost', dto.costRecordedSinceUtc, firstBucket);
    if (since) notes.push({ text: since, tone: 'info' });
  }

  const value = USAGE_VALUE[key];
  const lines = source.map((line): TelemetrySeriesLine => {
    let values: (number | null)[] = line.points.map(value);
    if (key === 'cost' && options.cumulative) {
      let sum = 0;
      values = values.map(cost => (sum += cost ?? 0));
    }
    return { key: line.key, label: line.label, provider: line.provider ?? null, isOther: line.isOther, values };
  });
  return { lines, notes };
}

/** No usage line holds a request in any bucket. */
export function telemetryUsageEmpty(dto: AiTelemetrySeriesDto): boolean {
  return dto.series.every(line => line.points.every(point => point.requests === 0));
}

// --- Colors ---

/**
 * The categorical line colors where the lines are not providers: eight hues that each keep 3 : 1
 * against both themes' plot grounds (dark `#181818` and `#0b0b0b`, light `#ffffff`).
 */
export const TELEMETRY_SERIES_COLORS: readonly string[] = Object.freeze([
  '#3987e5', '#d95926', '#199e70', '#9085e9', '#c98500', '#d55181', '#0f9bb0', '#7f9a1f'
]);

/** The *Other* line: gray, and dashed. */
export const TELEMETRY_OTHER_COLOR = '#8a8a8a';

/** A line's color: its provider's hue when split by provider, else the next categorical color; *Other* gray. */
export function telemetryLineColor(
  line: Pick<TelemetrySeriesLine, 'provider' | 'isOther'>,
  index: number,
  splitBy: AiTelemetrySplitBy,
  theme: Pick<ResolvedFigureTheme, 'chart'>,
  activity = false
): string {
  if (line.isOther) return TELEMETRY_OTHER_COLOR;
  if (!activity && splitBy === 'provider') return providerHue(line.provider, theme);
  return TELEMETRY_SERIES_COLORS[index % TELEMETRY_SERIES_COLORS.length];
}

// --- Value formats ---

const DAY_MS = 86_400_000;
const HOUR_MS = 3_600_000;

/** A bucket's length, for the axis of a single bucket. */
const BUCKET_MS: Readonly<Record<AiTelemetrySeriesDto['bucket'], number>> = { day: DAY_MS, week: 7 * DAY_MS, month: 30 * DAY_MS };

function groupedInteger(value: number): string {
  return Math.round(value).toLocaleString('en-US');
}

function usd(value: number): string {
  if (value === 0) return '$0';
  const magnitude = Math.abs(value);
  const decimals = magnitude < 0.01 ? 4 : magnitude < 0.1 ? 3 : magnitude < 100 ? 2 : 0;
  return `$${value.toLocaleString('en-US', { minimumFractionDigits: decimals, maximumFractionDigits: decimals })}`;
}

function compact(value: number): string {
  const magnitude = Math.abs(value);
  const scaled = (divisor: number, suffix: string) => `${Number((value / divisor).toFixed(1))}${suffix}`;
  if (magnitude >= 1e9) return scaled(1e9, 'B');
  if (magnitude >= 1e6) return scaled(1e6, 'M');
  if (magnitude >= 1e3) return scaled(1e3, 'k');
  return groupedInteger(value);
}

function percent(value: number): string {
  const pct = value * 100;
  const decimals = pct === 0 ? 0 : pct < 1 ? 2 : pct < 10 ? 1 : 0;
  return `${pct.toFixed(decimals)}%`;
}

function duration(ms: number): string {
  if (ms >= 1000) return `${(ms / 1000).toFixed(ms >= 10_000 ? 0 : 1)} s`;
  return `${Math.round(ms)} ms`;
}

const VALUE_FORMATS: Readonly<Record<TelemetryFigureKey, (value: number) => string>> = {
  requests: groupedInteger,
  cost: usd,
  tokens: compact,
  cacheHit: percent,
  duration,
  providerErrors: percent,
  activeUsers: groupedInteger,
  chats: groupedInteger,
  messages: groupedInteger
};

/** A chart's value as its axis and value labels write it. */
export function telemetryValueText(key: TelemetryFigureKey, value: number): string {
  return VALUE_FORMATS[key](value);
}

function axisTitle(key: TelemetryFigureKey, cumulative: boolean): string {
  switch (key) {
    case 'requests': return 'Requests';
    case 'cost': return cumulative ? 'Cumulative estimated cost (USD)' : 'Estimated cost (USD)';
    case 'tokens': return 'Tokens (prompt + output)';
    case 'cacheHit': return 'Cache read ÷ prompt';
    case 'duration': return 'Mean duration';
    case 'providerErrors': return 'Provider errors ÷ valid attempts';
    case 'activeUsers': return 'Users';
    case 'chats': return 'Chats';
    case 'messages': return 'Messages';
  }
}

const INTEGER_FIGURES: ReadonlySet<TelemetryFigureKey> = new Set<TelemetryFigureKey>(['requests', 'tokens', 'activeUsers', 'chats', 'messages']);

// --- The time axis ---

/** The x range: the first to the last bucket start; one bucket spans its own length. */
export function telemetryAxisRange(bucketStarts: readonly number[], bucket: AiTelemetrySeriesDto['bucket']): { min: number; max: number } | null {
  if (bucketStarts.length === 0) return null;
  const min = bucketStarts[0];
  const last = bucketStarts[bucketStarts.length - 1];
  return { min, max: last > min ? last : min + BUCKET_MS[bucket] };
}

/** One tick's label, from the ticks the axis built: Chat Consistency's rule, as its charts apply it. */
function tickLabel(values: readonly number[], index: number, span: number): string {
  // A lone tick of a short range is still an hour.
  const step = values.length > 1 ? values[1] - values[0] : span < CC_HOUR_TICKS_SPAN_MS ? HOUR_MS : 0;
  return ccTimeTickLabel(values[index], index > 0 ? values[index - 1] : null, step, span);
}

/** The ticks of a time axis `width` px wide from `min` to `max`, and their labels. */
export function telemetryTimeTicks(min: number, max: number, width: number): { values: number[]; labels: string[] } {
  const ticks = ccAxisTimeTicks(min, max, width);
  return { values: ticks.values, labels: ticks.values.map((_, index) => tickLabel(ticks.values, index, max - min)) };
}

// --- The chart ---

/** Everything a composition reads besides the series, the same for the screen and every file. */
export interface TelemetryComposeContext {
  readonly style: { readonly appearance: FigureAppearanceStyle; readonly timeline: TimelineFigureStyle };
  /** `resolveFigureTheme(style.appearance)`. */
  readonly theme: ResolvedFigureTheme;
  /** Null while the logo is hidden or not loaded. */
  readonly logo: FigureLogo | null;
  /** `2026-09-01 to 2026-10-13`. */
  readonly rangeLabel: string;
  readonly keys: AiTelemetryKeySource;
  readonly cumulative: boolean;
  /** The filtered user's name; null for every user. */
  readonly userName: string | null;
  /** When the series arrived, an ISO timestamp. */
  readonly loadedAt: string;
}

/** One drawn line: its values, its color and whether it is dashed. */
export interface TelemetryLine extends TelemetrySeriesLine {
  readonly color: string;
  readonly dashed: boolean;
}

export interface TelemetryFigure {
  readonly key: TelemetryFigureKey;
  readonly title: string;
  readonly yTitle: string;
  readonly bucket: AiTelemetrySeriesDto['bucket'];
  readonly bucketStarts: readonly number[];
  readonly lines: readonly TelemetryLine[];
  readonly notes: readonly FigureNote[];
  /** Null where no line has a value to draw. */
  readonly config: OffscreenPlotConfig | null;
}

/** Above this many buckets, only points between two gaps are drawn as dots. */
const DOTTED_BUCKETS = 62;

/** Value labels are written only on a single line of at most this many points. */
const VALUE_LABEL_MAX_POINTS = 24;

/** Each point's radius: every point on a short series; on a long one only a point standing alone between gaps. */
function pointRadii(values: readonly (number | null)[], radius: number): number[] {
  return values.map((value, index) => {
    if (value === null) return 0;
    if (values.length <= DOTTED_BUCKETS) return radius;
    const alone = (values[index - 1] ?? null) === null && (values[index + 1] ?? null) === null;
    return alone ? radius : 0;
  });
}

interface PointPosition {
  x: number;
  y: number;
  skip?: boolean;
}

/**
 * The wash under a single line, the values written at its points and the plot frame, each as the
 * timeline style asks.
 */
function telemetryOverlayPlugin(
  key: TelemetryFigureKey,
  lines: readonly TelemetryLine[],
  style: TimelineFigureStyle,
  theme: ResolvedFigureTheme,
  fontFamily: string
): Plugin<'line'> {
  const single = lines.length === 1 ? lines[0] : null;
  return {
    id: 'telemetryOverlay',
    beforeDatasetsDraw(chart: Chart<'line'>): void {
      if (!single || !style.areaWash) return;
      const points = chart.getDatasetMeta(0).data as unknown as PointPosition[];
      const area = chart.chartArea;
      const base = Math.min(area.bottom, chart.scales['y'].getPixelForValue(0));
      const ctx = chart.ctx;
      ctx.save();
      ctx.beginPath();
      ctx.rect(area.left, area.top, area.right - area.left, area.bottom - area.top);
      ctx.clip();
      ctx.fillStyle = withAlpha(single.color, 0.12);
      let run: PointPosition[] = [];
      const flush = (): void => {
        if (run.length >= 2) {
          ctx.beginPath();
          ctx.moveTo(run[0].x, base);
          for (const point of run) ctx.lineTo(point.x, point.y);
          ctx.lineTo(run[run.length - 1].x, base);
          ctx.closePath();
          ctx.fill();
        }
        run = [];
      };
      single.values.forEach((value, index) => {
        const point = points[index];
        if (value === null || !point || point.skip) flush(); else run.push(point);
      });
      flush();
      ctx.restore();
    },
    afterDatasetsDraw(chart: Chart<'line'>): void {
      if (!single || !style.valueLabels) return;
      const drawn = single.values.filter(value => value !== null).length;
      if (drawn === 0 || drawn > VALUE_LABEL_MAX_POINTS) return;
      const points = chart.getDatasetMeta(0).data as unknown as PointPosition[];
      const area = chart.chartArea;
      const size = style.valueLabelSizePx;
      const ctx = chart.ctx;
      ctx.save();
      ctx.font = `${theme.fonts.labelWeight} ${size}px ${fontFamily}`;
      ctx.fillStyle = theme.chart.inkPrimary;
      ctx.textAlign = 'center';
      single.values.forEach((value, index) => {
        const point = points[index];
        if (value === null || !point || point.skip) return;
        const offset = style.pointRadiusPx + 4;
        const above = point.y - offset - size >= area.top;
        ctx.textBaseline = above ? 'bottom' : 'top';
        ctx.fillText(telemetryValueText(key, value), point.x, above ? point.y - offset : point.y + offset);
      });
      ctx.restore();
    },
    afterDraw(chart: Chart<'line'>): void {
      if (!style.plotFrame) return;
      const area = chart.chartArea;
      const ctx = chart.ctx;
      ctx.save();
      ctx.strokeStyle = theme.frameColor;
      ctx.lineWidth = 1;
      ctx.strokeRect(area.left + 0.5, area.top + 0.5, area.right - area.left - 1, area.bottom - area.top - 1);
      ctx.restore();
    }
  };
}

/** The line chart of `lines` over the bucket starts, in the theme and the timeline style; no tooltip, no events. */
function telemetryChartConfig(
  key: TelemetryFigureKey,
  yTitle: string,
  lines: readonly TelemetryLine[],
  bucketStarts: readonly number[],
  bucket: AiTelemetrySeriesDto['bucket'],
  context: TelemetryComposeContext
): OffscreenPlotConfig | null {
  const range = telemetryAxisRange(bucketStarts, bucket);
  if (!range || !lines.some(line => line.values.some(value => value !== null))) return null;
  const style = context.style.timeline;
  const theme = context.theme;
  const fontFamily = theme.fonts.chartStack ?? theme.fonts.chromeStack;
  const tick = { color: theme.chart.inkMuted, font: { family: fontFamily, size: style.axisTextSizePx } };
  const title = {
    display: true,
    color: theme.chart.inkSecondary,
    font: { family: fontFamily, size: style.axisTitleSizePx, weight: style.axisTitleWeight }
  };
  const datasets: ChartDataset<'line', { x: number; y: number | null }[]>[] = lines.map(line => ({
    label: line.label,
    data: line.values.map((value, index) => ({ x: bucketStarts[index], y: value })),
    borderColor: line.color,
    backgroundColor: line.color,
    borderWidth: style.lineWidthPx,
    borderDash: line.dashed ? [6, 4] : [],
    pointRadius: pointRadii(line.values, style.pointRadiusPx),
    pointBackgroundColor: line.color,
    pointBorderColor: theme.surface,
    pointBorderWidth: 1,
    tension: 0,
    spanGaps: false
  }));
  const options: ChartOptions<'line'> = {
    events: [],
    layout: { padding: { top: 8, right: 12 } },
    scales: {
      x: {
        type: 'linear',
        min: range.min,
        max: range.max,
        afterBuildTicks: (scale: Scale) => {
          const ticks = ccAxisTimeTicks(scale.min, scale.max, scale.width);
          if (ticks.values.length > 0) scale.ticks = ticks.values.map(value => ({ value }));
        },
        ticks: {
          ...tick,
          autoSkip: false,
          maxRotation: 0,
          callback(this: Scale, _value: string | number, index: number, ticks: Tick[]) {
            return tickLabel(ticks.map(entry => entry.value), index, this.max - this.min);
          }
        },
        grid: { display: false },
        border: { display: false },
        title: { ...title, text: 'Date (UTC)' }
      },
      y: {
        type: 'linear',
        beginAtZero: true,
        min: 0,
        ...(key === 'cacheHit' ? { max: 1 } : {}),
        ticks: {
          ...tick,
          ...(INTEGER_FIGURES.has(key) ? { precision: 0 } : {}),
          callback: value => telemetryValueText(key, Number(value))
        },
        grid: { ...(style.gridlines ? {} : { display: false }), color: theme.chart.gridline, lineWidth: 1 },
        border: { display: false },
        title: { ...title, text: yTitle }
      }
    },
    plugins: {
      legend: {
        display: lines.length > 1,
        align: 'start',
        labels: {
          color: theme.chart.inkPrimary,
          font: { family: fontFamily, size: style.legendTextSizePx, weight: theme.fonts.labelWeight },
          boxWidth: 16,
          boxHeight: 4,
          padding: 12
        }
      },
      tooltip: { enabled: false }
    }
  };
  return {
    type: 'line',
    // Typed by the line chart's own datum shape; erased back to the union `renderPlotOffscreen` takes.
    data: { datasets } as unknown as OffscreenPlotConfig['data'],
    options: options as unknown as OffscreenPlotConfig['options'],
    plugins: [telemetryOverlayPlugin(key, lines, style, theme, fontFamily) as unknown as Plugin]
  };
}

/** One chart, built for composition: its lines colored, its notes and its configuration. */
export function buildTelemetryFigure(dto: AiTelemetrySeriesDto, key: TelemetryFigureKey, context: TelemetryComposeContext): TelemetryFigure {
  const series = buildFigureSeries(dto, key, { cumulative: context.cumulative });
  const activity = isActivityFigure(key);
  let colorIndex = 0;
  const lines = series.lines.map((line): TelemetryLine => ({
    ...line,
    color: telemetryLineColor(line, line.isOther ? -1 : colorIndex++, dto.splitBy, context.theme, activity),
    dashed: line.isOther
  }));
  const bucketStarts = telemetryBucketStarts(dto);
  const yTitle = axisTitle(key, context.cumulative);
  return {
    key,
    title: telemetryFigureTitle(key),
    yTitle,
    bucket: dto.bucket,
    bucketStarts,
    lines,
    notes: series.notes,
    config: telemetryChartConfig(key, yTitle, lines, bucketStarts, dto.bucket, context)
  };
}

/** The nine charts, in order. */
export function buildTelemetryFigures(dto: AiTelemetrySeriesDto, context: TelemetryComposeContext): TelemetryFigure[] {
  return TELEMETRY_FIGURE_KEYS.map(key => buildTelemetryFigure(dto, key, context));
}

// --- Composition ---

const BUCKET_WORDS: Readonly<Record<AiTelemetrySeriesDto['bucket'], string>> = { day: 'by day', week: 'by week', month: 'by month' };

/** `System AI configurations`, `User keys` or `All keys`. */
export function telemetryKeysLabel(keys: AiTelemetryKeySource): string {
  return keys === 'system' ? 'System AI configurations' : keys === 'user' ? 'User keys' : 'All keys';
}

/** `2026-09-01 to 2026-10-13`, one day alone, `from …`, `until …`, or `All dates`. */
export function telemetryRangeLabel(start: string | null | undefined, end: string | null | undefined): string {
  if (start && end) return start === end ? start : `${start} to ${end}`;
  if (start) return `from ${start}`;
  if (end) return `until ${end}`;
  return 'All dates';
}

/** The cost charts' pricing badge, which no style toggle hides. */
export const TELEMETRY_PRICING_BADGE: FigureBadge = Object.freeze({ text: 'Estimated, USD', tone: 'pricing' as const });

/**
 * The heading and notes around one chart: its title; the dates badge (the range and the bucket),
 * unless hidden, and on the cost chart the pricing badge; the detail line naming a cumulative cost
 * and the filtered user; the chart's notes.
 */
export function telemetryFigureChrome(figure: TelemetryFigure, context: TelemetryComposeContext): FigureChrome {
  const badges = visibleBadges([
    { text: `${context.rangeLabel} · ${BUCKET_WORDS[figure.bucket]}`, tone: 'neutral', kind: 'dates' },
    ...(figure.key === 'cost' ? [TELEMETRY_PRICING_BADGE] : [])
  ], context.style.timeline.hiddenBadges);
  const detail = [
    ...(figure.key === 'cost' && context.cumulative ? ['Cumulative'] : []),
    ...(context.userName ? [`User: ${context.userName}`] : [])
  ].join(' · ');
  return { title: figure.title, badges, detail, key: [], highlight: '', notes: figure.notes };
}

/** `Source: Overseer AI Telemetry · {keys}` on the left, the load time on the right; nothing while the footer is off. */
export function telemetryFigureFooter(context: TelemetryComposeContext): FigureFooter {
  if (!context.style.timeline.footer) return { suite: '', computedAt: '' };
  return {
    label: 'Source',
    suite: `Overseer AI Telemetry · ${telemetryKeysLabel(context.keys)}`,
    computedAt: formatComputedAt(context.loadedAt)
  };
}

/** The image's badges, lines and notes as one sentence for assistive technology. */
export function telemetryFigureSummary(figure: TelemetryFigure, chrome: FigureChrome): string {
  const parts = [figureSummary(chrome)];
  if (figure.lines.length > 0) parts.push(`Lines: ${figure.lines.map(line => line.label).join(', ')}`);
  const text = parts.filter(part => part !== '').map(part => `${part}.`).join(' ');
  const notes = chrome.notes.map(note => note.text).join(' ');
  return notes ? `${text} ${notes}`.trim() : text;
}

/** Everything a composition draws besides the plot, which `resolveFigureLayout` measures. */
export type TelemetryFigureSource = Omit<FigureExportRequest, 'canvas' | 'format' | 'layout'>;

export function telemetryFigureSource(figure: TelemetryFigure, context: TelemetryComposeContext): TelemetryFigureSource {
  const timeline = context.style.timeline;
  return {
    chrome: telemetryFigureChrome(figure, context),
    footer: telemetryFigureFooter(context),
    textSizes: { titlePx: timeline.titleSizePx, badgePx: timeline.badgeTextSizePx, footerPx: timeline.footerTextSizePx },
    theme: context.theme,
    logo: context.logo,
    betterBadgePlacement: timeline.betterBadgePlacement
  };
}

/**
 * The file's layout for one chart at a chart size, or why it is refused: a size error, a bitmap past
 * the limit, or a size too short for the chart's heading, notes and footer.
 */
export function telemetryFigureLayout(
  figure: TelemetryFigure,
  context: TelemetryComposeContext,
  size: FigureSizeSettings
): { layout: FigureExportLayout | null; refusal: string | null } {
  const error = sizeErrors(size, 'chart').any;
  if (error !== '') return { layout: null, refusal: error };
  return resolveFigureLayout(
    telemetryFigureSource(figure, context), resolveSizeResolution(size), resolveSizeDensity(size), size.textScalePercent / 100);
}

/**
 * Composes one chart at `layout`: the target layout for a file, the rasterized one for the screen.
 * Null for a chart with nothing to draw, or whose plot could not be built.
 */
export async function composeTelemetryFigure(
  figure: TelemetryFigure,
  context: TelemetryComposeContext,
  layout: FigureExportLayout
): Promise<HTMLCanvasElement | null> {
  if (!figure.config) return null;
  const plot = await renderPlotOffscreen(figure.config, layout);
  return plot ? composeFigureImage({ ...telemetryFigureSource(figure, context), canvas: plot, format: 'png', layout }) : null;
}

// --- File names ---

/** `ai-telemetry_<figure>_<yyyyMMdd_HHmmss>.<png|webp>`, in the format actually written. */
export function telemetryChartFilename(key: TelemetryFigureKey, format: FigureExportFormat, now: Date = new Date()): string {
  return `ai-telemetry_${key.replace(/[^A-Za-z0-9_-]/g, '-')}_${exportTimestamp(now)}.${format}`;
}

/** `ai-telemetry_charts_<yyyyMMdd_HHmmss>.zip`. */
export function telemetryArchiveFilename(now: Date = new Date()): string {
  return `ai-telemetry_charts_${exportTimestamp(now)}.zip`;
}
