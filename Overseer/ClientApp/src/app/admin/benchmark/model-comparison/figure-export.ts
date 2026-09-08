/**
 * Composes one comparison figure into a presentation-quality image.
 *
 * Pure functions over a canvas: no Angular, no component state, no DOM beyond the canvases
 * themselves, so it unit-tests without a fixture and the view keeps only the wiring.
 *
 * Three things here are load-bearing rather than decorative:
 *
 * 1. **The caveats are composited into the image.** The whole reason the comparison view refuses to
 *    hide what it could not compare is that a chart is more persuasive than a table. A figure
 *    exported as a bare canvas and pasted into a document would drop exactly the notices that stop
 *    it being misread, so the title, subtitle, caption, every notice and the footer are drawn into
 *    the same bitmap as the plot.
 * 2. **The background is opaque.** Chart.js canvases are transparent; a PNG of one dropped into a
 *    light document renders as dark-on-dark and is unreadable.
 * 3. **An explicit resolution buys sharpness, not more content.** Every explicit size composes at
 *    one layout width of {@link FIGURE_EXPORT_LAYOUT_WIDTH} CSS px and scales the whole
 *    composition by `targetWidth / FIGURE_EXPORT_LAYOUT_WIDTH`, so a 4K export and a Full HD
 *    export are the same figure at different densities: the typography keeps its proportions and
 *    only the plot box absorbs the difference in height.
 */

import { Chart } from 'chart.js';
import type { ChartConfiguration, ChartType, Plugin } from 'chart.js';

export type FigureExportFormat = 'png' | 'webp';

/** One offered export size. */
export interface FigureExportResolution {
  /** `'onscreen' | 'hd' | … | 'custom'`. */
  readonly id: string;
  readonly label: string;
  /** Null for `'onscreen'`, which follows the live canvas. */
  readonly widthPx: number | null;
  readonly heightPx: number | null;
}

/** The offered sizes, in ascending order. `'custom'` is not among them: the view builds that one. */
export const FIGURE_EXPORT_PRESETS: readonly FigureExportResolution[] = [
  { id: 'onscreen', label: 'On-screen (2×)', widthPx: null, heightPx: null },
  { id: 'hd', label: 'HD — 1280 × 720', widthPx: 1280, heightPx: 720 },
  { id: 'wide', label: 'Slide — 1600 × 900', widthPx: 1600, heightPx: 900 },
  { id: 'fullhd', label: 'Full HD — 1920 × 1080', widthPx: 1920, heightPx: 1080 },
  { id: 'qhd', label: 'QHD — 2560 × 1440', widthPx: 2560, heightPx: 1440 },
  { id: 'uhd', label: '4K UHD — 3840 × 2160', widthPx: 3840, heightPx: 2160 }
];

/** The layout width every explicit resolution composes at, so relative typography never changes. */
export const FIGURE_EXPORT_LAYOUT_WIDTH = 960;

/** Bounds on either side of a custom size. */
export const FIGURE_EXPORT_MIN_DIMENSION = 320;
export const FIGURE_EXPORT_MAX_DIMENSION = 8000;

/** The smallest plot box a figure may be composed with, in layout px. */
export const FIGURE_EXPORT_MIN_PLOT_HEIGHT = 160;

/**
 * The box one figure is composed in, and the device pixels it is written at.
 *
 * `layoutWidth` × `layoutHeight` is the CSS-pixel composition; `density` is the transform applied
 * to it; `pixelWidth` and `pixelHeight` are the bitmap's dimensions and are authoritative, so a
 * requested size comes back to the byte.
 */
export interface FigureExportLayout {
  readonly layoutWidth: number;
  readonly layoutHeight: number;
  /** The plot box's width: the layout width less the composition's padding on both sides. */
  readonly plotWidth: number;
  /** What is left of the layout height once the chrome is measured. */
  readonly plotHeight: number;
  readonly density: number;
  readonly pixelWidth: number;
  readonly pixelHeight: number;
}

/** One figure, with every piece of chrome the exported image must carry. */
export interface FigureExportRequest {
  /** The live chart canvas, or one rendered by {@link renderPlotOffscreen}. Read, never mutated. */
  readonly canvas: HTMLCanvasElement;
  readonly title: string;
  readonly subtitle: string;
  readonly caption: string;
  /** Degraded axes, saturation warnings, cap notices — every caveat the card renders. */
  readonly notices: readonly string[];
  /** Suite, pricing basis, entry count and the time the comparison was computed. */
  readonly footer: string;
  readonly format: FigureExportFormat;
  /** From {@link resolveFigureLayout}. Absent composes at the on-screen size and density. */
  readonly layout?: FigureExportLayout | null;
}

/** What `encodeFigureImage` produced, including the format actually written. */
export interface FigureExportResult {
  readonly blob: Blob;
  /** The requested format, or `'png'` where the browser could not encode WebP. */
  readonly format: FigureExportFormat;
  /** True when a WebP request was silently answered with a PNG. */
  readonly fellBackToPng: boolean;
}

/** The `'onscreen'` density. Reading the on-screen canvas at 1x would export it blurred. */
export const FIGURE_EXPORT_SCALE = 2;

/** WebP quality, fixed at this repository's image convention of 85. */
export const FIGURE_EXPORT_WEBP_QUALITY = 0.85;

/** Layout constants, in CSS pixels before the density transform is applied. */
const PADDING = 20;
const TITLE_SIZE = 18;
const SUBTITLE_SIZE = 13;
const BODY_SIZE = 12;
const LINE_GAP = 6;
const RULE_GAP = 12;

/** The narrowest content column a figure is laid out in. */
const MIN_CONTENT_WIDTH = 360;

/** The card ground the comparison view draws on, so an exported figure matches what was on screen. */
const BACKGROUND = '#181818';
const TITLE_COLOR = '#e0ba6d';
const BODY_COLOR = '#d4d4d8';
const MUTED_COLOR = '#a1a1aa';
const RULE_COLOR = '#2a2a2a';

const FONT_STACK = '"Segoe UI", "Helvetica Neue", Arial, sans-serif';

/**
 * Resolves one figure's composition box, or refuses it.
 *
 * The chrome is measured with the same wrapping code {@link composeFigureImage} draws with, at the
 * same content width, so the refusal threshold and the drawn image can never disagree.
 *
 * `refusal` is non-null when the requested height leaves less than
 * {@link FIGURE_EXPORT_MIN_PLOT_HEIGHT} for the plot once the chrome is measured; it names the
 * figure and the minimum height that would work for it. A refused figure returns `layout: null`.
 */
export function resolveFigureLayout(
  request: Omit<FigureExportRequest, 'canvas' | 'format'>,
  resolution: FigureExportResolution,
  onScreen: { width: number; height: number }
): { layout: FigureExportLayout | null; refusal: string | null } {
  if (resolution.widthPx === null && resolution.heightPx === null) {
    return { layout: onScreenLayout(request, onScreen), refusal: null };
  }

  const requestedWidth = resolution.widthPx;
  const requestedHeight = resolution.heightPx;
  if (!isUsableDimension(requestedWidth) || !isUsableDimension(requestedHeight)) {
    return { layout: null, refusal: dimensionRefusal(requestedWidth, requestedHeight) };
  }

  const pixelWidth = Math.round(requestedWidth);
  const pixelHeight = Math.round(requestedHeight);
  const layoutWidth = FIGURE_EXPORT_LAYOUT_WIDTH;
  const plotWidth = layoutWidth - PADDING * 2;
  const density = pixelWidth / layoutWidth;
  const layoutHeight = pixelHeight / density;

  const chrome = measureFigureChrome(request, plotWidth);
  const plotHeight = layoutHeight - chrome.height;
  if (plotHeight < FIGURE_EXPORT_MIN_PLOT_HEIGHT) {
    const minimumHeight = Math.ceil((chrome.height + FIGURE_EXPORT_MIN_PLOT_HEIGHT) * density);
    return {
      layout: null,
      refusal:
        `${figureName(request.title)} does not fit ${pixelWidth} × ${pixelHeight} px: its title, ` +
        `caption and notices leave too little room for the plot. Export it at ` +
        `${minimumHeight} px tall or more at this width.`
    };
  }

  return {
    layout: { layoutWidth, layoutHeight, plotWidth, plotHeight, density, pixelWidth, pixelHeight },
    refusal: null
  };
}

/**
 * Draws the chart and all of its chrome onto a new offscreen canvas.
 *
 * With a `layout`, the composition is laid out at `layout.layoutWidth`, scaled by `layout.density`
 * and given a plot box of exactly `layout.plotHeight`; the bitmap takes the layout's pixel
 * dimensions verbatim, which is what makes a requested resolution exact. Without one, the
 * composition follows the source canvas at {@link FIGURE_EXPORT_SCALE}.
 *
 * The source canvas is drawn at its layout size and scaled up by the context transform rather than
 * copied pixel for pixel, so a chart rendered at a higher device pixel ratio lands sharp; the caller
 * is responsible for having asked Chart.js for that density first.
 */
export function composeFigureImage(request: FigureExportRequest): HTMLCanvasElement {
  const layout = request.layout ?? null;
  const chartWidth = cssWidthOf(request.canvas);
  const chartHeight = cssHeightOf(request.canvas);

  const contentWidth = layout
    ? layout.layoutWidth - PADDING * 2
    : Math.max(chartWidth, MIN_CONTENT_WIDTH);
  const plotWidth = layout ? contentWidth : chartWidth;
  const plotHeight = layout ? layout.plotHeight : chartHeight;
  const density = layout ? layout.density : FIGURE_EXPORT_SCALE;

  const chrome = measureFigureChrome(request, contentWidth);
  const width = contentWidth + PADDING * 2;
  const height = chrome.height + plotHeight;

  const target = document.createElement('canvas');
  target.width = layout ? layout.pixelWidth : Math.round(width * density);
  target.height = layout ? layout.pixelHeight : Math.round(height * density);

  const context = target.getContext('2d');
  if (!context) {
    return target;
  }
  context.scale(density, density);

  // Opaque, and painted before anything else: a transparent PNG of a Chart.js canvas is
  // dark-on-dark in any light document it is pasted into.
  context.fillStyle = BACKGROUND;
  context.fillRect(0, 0, width, height);

  context.textBaseline = 'top';
  let y = PADDING;

  y = drawBlock(context, chrome.titleLines, PADDING, y, TITLE_SIZE, '600', TITLE_COLOR);
  y = drawBlock(context, chrome.subtitleLines, PADDING, y, SUBTITLE_SIZE, '400', MUTED_COLOR);
  if (chrome.titleLines.length > 0 || chrome.subtitleLines.length > 0) {
    y += LINE_GAP;
  }

  if (chartWidth > 0 && chartHeight > 0 && plotWidth > 0 && plotHeight > 0) {
    context.drawImage(request.canvas, PADDING, y, plotWidth, plotHeight);
  }
  y += plotHeight;

  if (chrome.captionLines.length > 0) {
    y += LINE_GAP;
    y = drawBlock(context, chrome.captionLines, PADDING, y, BODY_SIZE, '400', BODY_COLOR);
  }
  for (const lines of chrome.noticeLines) {
    y += LINE_GAP;
    y = drawBlock(context, lines, PADDING, y, BODY_SIZE, '400', MUTED_COLOR);
  }

  if (chrome.footerLines.length > 0) {
    y += RULE_GAP / 2;
    context.strokeStyle = RULE_COLOR;
    context.lineWidth = 1;
    context.beginPath();
    context.moveTo(PADDING, Math.round(y) + 0.5);
    context.lineTo(width - PADDING, Math.round(y) + 0.5);
    context.stroke();
    y += RULE_GAP / 2;
    drawBlock(context, chrome.footerLines, PADDING, y, BODY_SIZE, '400', MUTED_COLOR);
  }

  return target;
}

/** A figure's chart.js inputs, as {@link renderPlotOffscreen} needs them. */
export interface OffscreenPlotConfig {
  readonly type: ChartType;
  readonly data: ChartConfiguration['data'];
  readonly options?: ChartConfiguration['options'];
  readonly plugins?: readonly Plugin[];
}

/**
 * Renders one plot into a transient offscreen chart sized to `layout`, and returns a snapshot of it.
 *
 * An explicit resolution needs a plot box of its own: raising the live chart's device pixel ratio
 * sharpens it but cannot change its layout box without reflowing the visible page. The chart is
 * therefore built in a container parked off-screen, sized to the layout's plot box, and torn down
 * again — construction, snapshot, `destroy` and removal all inside one `try/finally`, so a throw
 * cannot strand a detached chart or its container.
 *
 * The returned canvas is a copy: `destroy` clears the chart's own canvas. Failure returns null, so
 * the caller can fall back to the live-canvas path rather than losing the export.
 *
 * Chart.js controllers, elements and scales are registered globally by `provideCharts`, so no
 * further registration happens here.
 */
export async function renderPlotOffscreen(
  config: OffscreenPlotConfig,
  layout: FigureExportLayout
): Promise<HTMLCanvasElement | null> {
  if (layout.plotWidth <= 0 || layout.plotHeight <= 0) {
    return null;
  }

  const container = document.createElement('div');
  container.style.position = 'fixed';
  container.style.left = '-10000px';
  container.style.top = '0';
  container.style.width = `${layout.plotWidth}px`;
  container.style.height = `${layout.plotHeight}px`;

  const canvas = document.createElement('canvas');
  container.appendChild(canvas);
  document.body.appendChild(container);

  let chart: Chart | null = null;
  try {
    // Shallow copy: the figures carry scriptable callbacks in their options, which a deep clone
    // would drop.
    const options = {
      ...((config.options ?? {}) as Record<string, unknown>),
      responsive: false,
      maintainAspectRatio: false,
      animation: false,
      devicePixelRatio: layout.density
    };
    // The figures are typed each by their own chart type and datum shape; erased back to the
    // union, the constructor's inference no longer holds them.
    const chartConfig = {
      type: config.type,
      data: config.data,
      options,
      plugins: config.plugins ? [...config.plugins] : []
    } as unknown as ChartConfiguration;
    chart = new Chart(canvas, chartConfig);

    return snapshotOf(canvas, layout.plotWidth, layout.plotHeight);
  } catch {
    return null;
  } finally {
    chart?.destroy();
    container.remove();
  }
}

/**
 * Encodes a composed canvas.
 *
 * A browser that cannot encode WebP silently returns a PNG from `toBlob` and `toDataURL` rather
 * than failing, so the produced blob's own MIME type is what decides the result: the caller is told
 * a PNG was written, and names the file accordingly. A `.webp` whose bytes are a PNG is worse than
 * either format on its own.
 */
export function encodeFigureImage(
  canvas: HTMLCanvasElement,
  format: FigureExportFormat
): Promise<FigureExportResult> {
  const mime = format === 'webp' ? 'image/webp' : 'image/png';
  const quality = format === 'webp' ? FIGURE_EXPORT_WEBP_QUALITY : undefined;

  return new Promise<FigureExportResult>((resolve, reject) => {
    canvas.toBlob(
      blob => {
        if (!blob) {
          reject(new Error('The figure could not be encoded.'));
          return;
        }
        const encoded: FigureExportFormat = blob.type === 'image/webp' ? 'webp' : 'png';
        resolve({ blob, format: encoded, fellBackToPng: format === 'webp' && encoded !== 'webp' });
      },
      mime,
      quality
    );
  });
}

/**
 * `model-comparison_<figureId>_<yyyyMMdd_HHmmss>.<png|webp>`.
 *
 * Local time, and sortable: a set of figures downloaded together lands in one contiguous block in a
 * file listing, in the order they were written.
 */
export function figureExportFilename(
  figureId: string,
  format: FigureExportFormat,
  now: Date = new Date()
): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  const stamp =
    `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}_` +
    `${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
  const safeId = (figureId || 'figure').replace(/[^A-Za-z0-9_-]/g, '-');
  return `model-comparison_${safeId}_${stamp}.${format}`;
}

/** Saves a blob under a filename, through the object-URL and anchor pattern the debug log uses. */
export function saveFigureBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
}

// -----------------------------------------------------------------------------------------------
// Internals
// -----------------------------------------------------------------------------------------------

/** Everything the composition draws around the plot, wrapped to a content column and summed. */
interface FigureChrome {
  readonly titleLines: string[];
  readonly subtitleLines: string[];
  readonly captionLines: string[];
  readonly noticeLines: string[][];
  readonly footerLines: string[];
  /** The composition's height less the plot box: padding, every text block and the gaps between. */
  readonly height: number;
}

type FigureChromeSource = Omit<FigureExportRequest, 'canvas' | 'format' | 'layout'>;

/**
 * Wraps and sums the chrome for one content width.
 *
 * The single source of both the drawn text and the height {@link resolveFigureLayout} subtracts
 * from a target box; duplicating either would let a figure be accepted at a height it cannot be
 * drawn at.
 */
function measureFigureChrome(source: FigureChromeSource, contentWidth: number): FigureChrome {
  const measure = document.createElement('canvas').getContext('2d');
  const wrap = (text: string, size: number, weight: string): string[] =>
    measure ? wrapText(measure, text, contentWidth, size, weight) : (text ? [text] : []);

  const titleLines = wrap(source.title, TITLE_SIZE, '600');
  const subtitleLines = wrap(source.subtitle, SUBTITLE_SIZE, '400');
  const captionLines = wrap(source.caption, BODY_SIZE, '400');
  const noticeLines = source.notices.map(notice => wrap(notice, BODY_SIZE, '400'));
  const footerLines = wrap(source.footer, BODY_SIZE, '400');

  let height = PADDING * 2;
  height += blockHeight(titleLines, TITLE_SIZE);
  height += blockHeight(subtitleLines, SUBTITLE_SIZE);
  if (titleLines.length > 0 || subtitleLines.length > 0) {
    height += LINE_GAP;
  }
  if (captionLines.length > 0) {
    height += LINE_GAP + blockHeight(captionLines, BODY_SIZE);
  }
  for (const lines of noticeLines) {
    height += LINE_GAP + blockHeight(lines, BODY_SIZE);
  }
  if (footerLines.length > 0) {
    height += RULE_GAP + blockHeight(footerLines, BODY_SIZE);
  }

  return { titleLines, subtitleLines, captionLines, noticeLines, footerLines, height };
}

/** The box the live canvas is already laid out in, at {@link FIGURE_EXPORT_SCALE}. */
function onScreenLayout(
  request: FigureChromeSource,
  onScreen: { width: number; height: number }
): FigureExportLayout {
  const plotWidth = Math.max(onScreen.width, MIN_CONTENT_WIDTH);
  const layoutWidth = plotWidth + PADDING * 2;
  const plotHeight = onScreen.height;
  const layoutHeight = measureFigureChrome(request, plotWidth).height + plotHeight;
  return {
    layoutWidth,
    layoutHeight,
    plotWidth,
    plotHeight,
    density: FIGURE_EXPORT_SCALE,
    pixelWidth: Math.round(layoutWidth * FIGURE_EXPORT_SCALE),
    pixelHeight: Math.round(layoutHeight * FIGURE_EXPORT_SCALE)
  };
}

function isUsableDimension(value: number | null): value is number {
  return (
    value !== null &&
    Number.isFinite(value) &&
    value >= FIGURE_EXPORT_MIN_DIMENSION &&
    value <= FIGURE_EXPORT_MAX_DIMENSION
  );
}

function dimensionRefusal(width: number | null, height: number | null): string {
  const shown = (value: number | null): string =>
    value === null || !Number.isFinite(value) ? '?' : String(Math.round(value));
  return (
    `${shown(width)} × ${shown(height)} px is not a usable export size: each side must be between ` +
    `${FIGURE_EXPORT_MIN_DIMENSION} and ${FIGURE_EXPORT_MAX_DIMENSION} px.`
  );
}

/** A refusal reads as a sentence about the figure, so an untitled one still gets a subject. */
function figureName(title: string): string {
  const trimmed = (title ?? '').trim();
  return trimmed === '' ? 'This figure' : trimmed;
}

/**
 * Copies a rendered chart canvas at its full backing-store resolution.
 *
 * The style box is restated on the copy because {@link cssWidthOf} reads it: a canvas whose backing
 * store is `density` times its layout box would otherwise be composed at that larger size.
 */
function snapshotOf(
  source: HTMLCanvasElement,
  cssWidth: number,
  cssHeight: number
): HTMLCanvasElement {
  const snapshot = document.createElement('canvas');
  snapshot.width = source.width;
  snapshot.height = source.height;
  snapshot.style.width = `${cssWidth}px`;
  snapshot.style.height = `${cssHeight}px`;
  const context = snapshot.getContext('2d');
  if (context && source.width > 0 && source.height > 0) {
    context.drawImage(source, 0, 0);
  }
  return snapshot;
}

/**
 * The canvas' CSS width, falling back to its backing-store width.
 *
 * A Chart.js canvas rendered at a higher export density has a backing store larger than its layout
 * box, so reading `canvas.width` would lay the composition out at that size and draw the plot into
 * a fraction of it. `style.width` is what Chart.js sets and is therefore the size the plot was laid
 * out at.
 */
function cssWidthOf(canvas: HTMLCanvasElement): number {
  const styled = parseFloat(canvas.style.width || '');
  if (Number.isFinite(styled) && styled > 0) {
    return styled;
  }
  return canvas.clientWidth > 0 ? canvas.clientWidth : canvas.width;
}

function cssHeightOf(canvas: HTMLCanvasElement): number {
  const styled = parseFloat(canvas.style.height || '');
  if (Number.isFinite(styled) && styled > 0) {
    return styled;
  }
  return canvas.clientHeight > 0 ? canvas.clientHeight : canvas.height;
}

function fontOf(size: number, weight: string): string {
  return `${weight} ${size}px ${FONT_STACK}`;
}

function blockHeight(lines: readonly string[], size: number): number {
  return lines.length === 0 ? 0 : lines.length * Math.round(size * 1.4);
}

function drawBlock(
  context: CanvasRenderingContext2D,
  lines: readonly string[],
  x: number,
  y: number,
  size: number,
  weight: string,
  color: string
): number {
  if (lines.length === 0) {
    return y;
  }
  const lineHeight = Math.round(size * 1.4);
  context.font = fontOf(size, weight);
  context.fillStyle = color;
  let cursor = y;
  for (const line of lines) {
    context.fillText(line, x, cursor);
    cursor += lineHeight;
  }
  return cursor;
}

/** Greedy word wrap. A single word wider than the column is left to overflow rather than broken. */
function wrapText(
  context: CanvasRenderingContext2D,
  text: string,
  maxWidth: number,
  size: number,
  weight: string
): string[] {
  const trimmed = (text ?? '').trim();
  if (trimmed === '') {
    return [];
  }
  context.font = fontOf(size, weight);
  const lines: string[] = [];
  let current = '';
  for (const word of trimmed.split(/\s+/)) {
    const candidate = current === '' ? word : `${current} ${word}`;
    if (current !== '' && context.measureText(candidate).width > maxWidth) {
      lines.push(current);
      current = word;
    } else {
      current = candidate;
    }
  }
  if (current !== '') {
    lines.push(current);
  }
  return lines;
}
