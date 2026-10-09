/**
 * One composition path for every Chat Consistency timeline chart: on screen, in Copy, Download and
 * Download all. The plot is the chart builder's (`buildCcFigure`), rendered off-screen and framed by
 * Model Comparison's composer (`composeFigureImage`) with its heading, badges, Better badge, logo,
 * notes and footer, styled by the figure style's `appearance` and `timeline` families.
 *
 * The screen and the file differ only in the layout passed in: the file's target layout, or that same
 * layout rasterized for the screen (`ccPreviewLayout`), which changes nothing but the density and the
 * pixel size. Pure apart from `renderPlotOffscreen` and `composeFigureImage`.
 */

import {
  FigureChrome,
  FigureDirection,
  FigureFooter,
  FigureNote,
  figureSummary,
  formatComputedAt,
  visibleBadges
} from '../../model-comparison/figure-chrome';
import {
  FigureExportLayout,
  FigureExportRequest,
  OffscreenPlotConfig,
  composeFigureImage,
  renderPlotOffscreen,
  resolveFigureLayout
} from '../../model-comparison/figure-export';
import { FigureLogo } from '../../model-comparison/figure-logo';
import { FigureSizeSettings, resolveSizeDensity, resolveSizeResolution, sizeErrors } from '../../model-comparison/figure-size';
import { FigureAppearanceStyle, TimelineFigureStyle } from '../../model-comparison/figure-style';
import { ResolvedFigureTheme } from '../../model-comparison/figure-theme';
import {
  CcChartConfig,
  CcChartOptions,
  CcDecimalPlaces,
  CcFigure,
  CcFigureInput,
  CcFigureKey,
  buildCcFigure,
  ccChartThemeFor
} from '../chat-consistency-charts';
import { plural } from '../chat-consistency-format';
import { CcComparisonSetKind } from '../chat-consistency.models';
import { markerSummary } from './cc-chart-figure.component';

/** Everything a composition reads besides the figure's data, the same for the screen and the file. */
export interface CcComposeContext {
  readonly style: { readonly appearance: FigureAppearanceStyle; readonly timeline: TimelineFigureStyle };
  /** `resolveFigureTheme(style.appearance)`. */
  readonly theme: ResolvedFigureTheme;
  /** Null while the logo is hidden or not loaded. */
  readonly logo: FigureLogo | null;
  /** `Claude 5.5 Haiku (xhigh)`. */
  readonly modelLabel: string;
  /** What one plotted point is: `run`, `battery run` or `member run`. */
  readonly unitNoun: string;
  /** Step 1's dates: `All dates`, `Last 30 days`. */
  readonly datesLabel: string;
  /** The compared set; null when none is. */
  readonly set: { readonly kind: CcComparisonSetKind; readonly label: string } | null;
  /** When the workspace received the timeline, an ISO timestamp. */
  readonly loadedAt: string;
  readonly hiddenSeries: ReadonlySet<string>;
  readonly zeroBaseline: boolean;
  readonly decimals: CcDecimalPlaces;
}

/**
 * The better direction per chart. The work charts read as more or less, never better or worse, and
 * the overview has no measure.
 */
const CC_FIGURE_DIRECTIONS: Readonly<Record<CcFigureKey, FigureDirection['y'] | null>> = {
  quality: 'top',
  ttfat: 'bottom',
  rate: 'top',
  work: null,
  tools: null,
  cost: 'bottom',
  reliability: 'bottom',
  timeline: null
};

/** The sentence the takeaway ends with when the chart draws runs not in the analysis. */
const NOT_ANALYZED_SENTENCE = /\d[\d,]* [^.]*? not in the analysis (?:is drawn as a gray cross|are drawn as gray crosses)\./;

/** The chart builder's options for a composed figure: no header band, no logo, no animation, no tooltip. */
export function ccComposedChartOptions(context: CcComposeContext): CcChartOptions {
  const timeline = context.style.timeline;
  return {
    theme: ccChartThemeFor(context.theme),
    reducedMotion: true,
    hiddenSeries: context.hiddenSeries,
    zeroBaseline: context.zeroBaseline,
    decimals: context.decimals,
    style: {
      axisTextSizePx: timeline.axisTextSizePx,
      axisTitleSizePx: timeline.axisTitleSizePx,
      axisTitleWeight: timeline.axisTitleWeight,
      gridlines: timeline.gridlines,
      plotFrame: timeline.plotFrame,
      valueLabels: timeline.valueLabels,
      valueLabelSizePx: timeline.valueLabelSizePx,
      legendTextSizePx: timeline.legendTextSizePx,
      markerTagSizePx: timeline.markerTagSizePx,
      lineWidthPx: timeline.lineWidthPx,
      pointRadiusPx: timeline.pointRadiusPx,
      areaWash: timeline.areaWash,
      labelWeight: context.theme.fonts.labelWeight
    },
    header: { title: null, subject: null },
    logo: null
  };
}

/** One chart built for composition from the plotted points. */
export function buildComposedCcFigure(key: CcFigureKey, input: CcFigureInput, context: CcComposeContext): CcFigure {
  return buildCcFigure(key, input, ccComposedChartOptions(context));
}

/** The points a figure draws with a value, counted once each. */
function plottedUnitCount(figure: CcFigure): number {
  const ids = new Set<number>();
  for (const dataset of figure.config?.data.datasets ?? []) {
    for (const point of dataset.data) {
      if (point.y !== null) ids.add(point.runId);
    }
  }
  return ids.size;
}

/** `Markers: E1–E2 Overseer changes · A1 annotation · S1 served-model change`, or '' without markers. */
export function ccMarkerNoteText(figure: CcFigure): string {
  const groups = markerSummary(figure.markers).map(group => {
    const runs = group.runs.map(run => (run.last === null ? run.first : `${run.first}–${run.last}`)).join(', ');
    return `${runs} ${group.noun}`;
  });
  return groups.length > 0 ? `Markers: ${groups.join(' · ')}` : '';
}

/** The takeaway's gray-crosses sentence, or '' where the chart marks no point. */
export function ccNotAnalyzedNoteText(figure: CcFigure): string {
  return NOT_ANALYZED_SENTENCE.exec(figure.takeaway)?.[0] ?? '';
}

/**
 * The heading and notes around one chart: its title; the model, the number of plotted points and the
 * dates as badges, less the hidden kinds; the Better badge where the chart has a better direction;
 * and the marker key and the gray-crosses sentence as notes, while their switches are on.
 */
export function ccFigureChrome(figure: CcFigure, context: CcComposeContext): FigureChrome {
  const timeline = context.style.timeline;
  const count = plottedUnitCount(figure);
  const badges = visibleBadges([
    { text: context.modelLabel, tone: 'neutral' as const, kind: 'model' as const },
    { text: plural(count, context.unitNoun), tone: 'neutral' as const, kind: 'runs' as const },
    { text: context.datesLabel, tone: 'neutral' as const, kind: 'dates' as const }
  ].filter(badge => badge.text !== ''), timeline.hiddenBadges);
  const better = CC_FIGURE_DIRECTIONS[figure.key];
  const notes: FigureNote[] = [];
  const markers = timeline.markerNote ? ccMarkerNoteText(figure) : '';
  if (markers) notes.push({ text: markers, tone: 'info' });
  const notAnalyzed = timeline.notAnalyzedNote ? ccNotAnalyzedNoteText(figure) : '';
  if (notAnalyzed) notes.push({ text: notAnalyzed, tone: 'info' });
  return {
    title: figure.title,
    badges,
    ...(better && !timeline.hiddenBadges.includes('direction') ? { direction: { y: better, label: 'Better' } } : {}),
    detail: '',
    key: [],
    highlight: '',
    notes
  };
}

/** The compared set on the left, *Battery* or *Suite*, or *Suites* without one; the load time on the right. */
export function ccFigureFooter(context: CcComposeContext): FigureFooter {
  if (!context.style.timeline.footer) return { suite: '', computedAt: '' };
  const set = context.set;
  return {
    label: set ? (set.kind === 'battery' ? 'Battery' : 'Suite') : 'Suites',
    suite: set?.label || 'All suites',
    computedAt: formatComputedAt(context.loadedAt)
  };
}

/** The image's badges, Better direction and notes as one sentence for assistive technology. */
export function ccFigureSummary(chrome: FigureChrome): string {
  const summary = figureSummary(chrome);
  const notes = chrome.notes.map(note => note.text).join(' ');
  if (!summary) return notes;
  return notes ? `${summary}. ${notes}` : `${summary}.`;
}

/** Everything a composition draws besides the plot, which `resolveFigureLayout` measures. */
export type CcFigureSource = Omit<FigureExportRequest, 'canvas' | 'format' | 'layout'>;

export function ccFigureSource(figure: CcFigure, context: CcComposeContext): CcFigureSource {
  const timeline = context.style.timeline;
  return {
    chrome: ccFigureChrome(figure, context),
    footer: ccFigureFooter(context),
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
export function ccFigureLayout(
  figure: CcFigure,
  context: CcComposeContext,
  size: FigureSizeSettings
): { layout: FigureExportLayout | null; refusal: string | null } {
  const error = sizeErrors(size, 'chart').any;
  if (error !== '') return { layout: null, refusal: error };
  return resolveFigureLayout(
    ccFigureSource(figure, context), resolveSizeResolution(size), resolveSizeDensity(size), size.textScalePercent / 100);
}

/** What one composition draws: the plot's configuration, and the request the composer frames it with. */
export interface CcFigureRequest {
  readonly config: CcChartConfig;
  readonly request: Omit<FigureExportRequest, 'canvas'>;
}

/** The plot and the frame of one composition at `layout`; null for a chart with nothing to draw. */
export function ccFigureRequest(figure: CcFigure, context: CcComposeContext, layout: FigureExportLayout): CcFigureRequest | null {
  if (!figure.config) return null;
  return { config: figure.config, request: { ...ccFigureSource(figure, context), format: 'png', layout } };
}

/**
 * Composes one chart at `layout`: the target layout for a file, the rasterized one for the screen.
 * Null for a chart with nothing to draw, or whose plot could not be built.
 */
export async function composeCcFigure(
  figure: CcFigure,
  context: CcComposeContext,
  layout: FigureExportLayout
): Promise<HTMLCanvasElement | null> {
  const built = ccFigureRequest(figure, context, layout);
  if (!built) return null;
  const plot = await renderPlotOffscreen(built.config as unknown as OffscreenPlotConfig, layout);
  return plot ? composeFigureImage({ ...built.request, canvas: plot }) : null;
}
