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
 * 3. **An explicit resolution buys sharpness, not more content.** Every explicit size composes in
 *    the smallest box that carries the target's own aspect ratio and is at least
 *    {@link FIGURE_EXPORT_LAYOUT_WIDTH} × {@link FIGURE_EXPORT_LAYOUT_HEIGHT} CSS px, and one
 *    density maps that box onto the bitmap on both axes. A 4K export and a Full HD export are
 *    therefore the same figure at two densities, while a 21:9 or a portrait target is the same
 *    typography over a wider or a taller composition: the type size never moves with the pixels.
 *    The pixel density chosen alongside the size multiplies the bitmap of whichever size was
 *    picked and never the composition, so the same figure comes out sharper rather than larger.
 */

import { Chart } from 'chart.js';
import type { ChartConfiguration, ChartType, Plugin } from 'chart.js';

export type FigureExportFormat = 'png' | 'webp';

export type WebpQuality = 75 | 80 | 85 | 90 | 95 | 100;
export const WEBP_QUALITY_OPTIONS: readonly WebpQuality[] = [75, 80, 85, 90, 95, 100];
export const DEFAULT_WEBP_QUALITY: WebpQuality = 85;

/** What `toBlob` is given: the percentage as a fraction. 1.0 is what Chromium encodes losslessly. */
export function webpEncoderQuality(quality: WebpQuality): number {
  return quality / 100;
}

/** The multiplier applied to a size's bitmap, as a factor: 2 is 200 %. */
export type FigureExportDensity = number;

/**
 * Every Windows display scaling step, ascending. `'custom'` is not among them: the view builds it.
 *
 * A browser at a zoom other than 100 % reports densities no fixed list contains — 220 % at 110 %
 * zoom on a 200 % display — which is what the custom percentage beside this list exists for.
 */
export const FIGURE_EXPORT_DENSITY_PRESETS: readonly FigureExportDensity[] =
  [1, 1.25, 1.5, 1.75, 2, 2.5, 3, 3.5, 4];

/** Bounds on a custom density, as percentages. */
export const FIGURE_EXPORT_MIN_DENSITY_PERCENT = 50;
export const FIGURE_EXPORT_MAX_DENSITY_PERCENT = 800;

/** How near a listed preset a density has to be for the control to show that preset. */
const DENSITY_PRESET_TOLERANCE = 0.005;

/** `'150%'` — the percentage the control shows for one factor. */
export function densityPercentLabel(density: FigureExportDensity): string {
  return `${Math.round(density * 100)}%`;
}

/**
 * The display's own density, clamped into the custom bounds and rounded to a whole percent.
 *
 * 1 where there is no window, so a server render or a headless test gets a defined value rather
 * than a NaN that would propagate into every pixel count downstream.
 */
export function displayDensity(
  win: { devicePixelRatio?: number } | null | undefined = globalThis.window
): FigureExportDensity {
  const ratio = win?.devicePixelRatio;
  if (typeof ratio !== 'number' || !Number.isFinite(ratio) || ratio <= 0) {
    return 1;
  }
  const percent = Math.min(
    FIGURE_EXPORT_MAX_DENSITY_PERCENT,
    Math.max(FIGURE_EXPORT_MIN_DENSITY_PERCENT, Math.round(ratio * 100))
  );
  return percent / 100;
}

/** The listed preset within {@link DENSITY_PRESET_TOLERANCE} of `density`, or null for Custom. */
export function densityPresetFor(density: FigureExportDensity): FigureExportDensity | null {
  if (!Number.isFinite(density)) {
    return null;
  }
  return FIGURE_EXPORT_DENSITY_PRESETS.find(
    preset => Math.abs(preset - density) <= DENSITY_PRESET_TOLERANCE
  ) ?? null;
}

/** One offered export size. */
export interface FigureExportResolution {
  /** `'onscreen' | 'hd' | … | 'custom'`. */
  readonly id: string;
  readonly label: string;
  /** Null for `'onscreen'`, which follows the live canvas at the chosen pixel density. */
  readonly widthPx: number | null;
  readonly heightPx: number | null;
  /** The aspect ratio the size belongs to, as the picker's `<optgroup>` names it. */
  readonly group: string;
}

/**
 * The offered sizes, grouped by aspect ratio and ascending inside each group. `'custom'` is not
 * among them: the view builds that one.
 *
 * The groups are the unit a reader chooses in: a figure destined for a 16:9 slide and one destined
 * for an A4 page differ in shape before they differ in pixels, and a flat ascending list hides that
 * distinction behind arithmetic.
 */
export const FIGURE_EXPORT_PRESETS: readonly FigureExportResolution[] = [
  { id: 'onscreen', label: 'On-screen', widthPx: null, heightPx: null, group: 'On-screen' },

  { id: 'hd', label: 'HD — 1280 × 720', widthPx: 1280, heightPx: 720, group: '16:9' },
  { id: 'fullhd', label: 'Full HD — 1920 × 1080', widthPx: 1920, heightPx: 1080, group: '16:9' },
  { id: 'qhd', label: 'QHD — 2560 × 1440', widthPx: 2560, heightPx: 1440, group: '16:9' },
  { id: 'uhd', label: '4K UHD — 3840 × 2160', widthPx: 3840, heightPx: 2160, group: '16:9' },

  { id: 'wxga', label: 'WXGA — 1920 × 1200', widthPx: 1920, heightPx: 1200, group: '16:10' },
  { id: 'wqxga', label: 'WQXGA — 2560 × 1600', widthPx: 2560, heightPx: 1600, group: '16:10' },

  { id: 'xga', label: 'XGA — 1024 × 768', widthPx: 1024, heightPx: 768, group: '4:3' },
  { id: 'uxga', label: 'UXGA — 1600 × 1200', widthPx: 1600, heightPx: 1200, group: '4:3' },
  { id: 'qxga', label: 'QXGA — 2048 × 1536', widthPx: 2048, heightPx: 1536, group: '4:3' },

  { id: 'p3x2', label: 'Classic photo — 1620 × 1080', widthPx: 1620, heightPx: 1080, group: '3:2' },
  { id: 'p3x2l', label: 'Classic photo large — 3000 × 2000', widthPx: 3000, heightPx: 2000, group: '3:2' },

  { id: 'square1080', label: 'Square — 1080 × 1080', widthPx: 1080, heightPx: 1080, group: '1:1' },
  { id: 'square2048', label: 'Square large — 2048 × 2048', widthPx: 2048, heightPx: 2048, group: '1:1' },

  { id: 'uw1080', label: 'Ultrawide — 2560 × 1080', widthPx: 2560, heightPx: 1080, group: '21:9' },
  { id: 'uw1440', label: 'Ultrawide QHD — 3440 × 1440', widthPx: 3440, heightPx: 1440, group: '21:9' },

  { id: 'a4l', label: 'A4 landscape 300 dpi — 3508 × 2480', widthPx: 3508, heightPx: 2480, group: 'Print' },
  { id: 'a4p', label: 'A4 portrait 300 dpi — 2480 × 3508', widthPx: 2480, heightPx: 3508, group: 'Print' },
  { id: 'letterl', label: 'Letter landscape 300 dpi — 3300 × 2550', widthPx: 3300, heightPx: 2550, group: 'Print' }
];

/** One `<optgroup>`: an aspect ratio, and the sizes offered in it. */
export interface FigureExportPresetGroup {
  readonly label: string;
  readonly presets: readonly FigureExportResolution[];
}

/**
 * {@link FIGURE_EXPORT_PRESETS} as the picker renders it, in the order the presets declare.
 *
 * Derived once at module load rather than per change-detection pass: the grouping is a property of
 * the constant above and cannot change while the application runs.
 */
export const FIGURE_EXPORT_PRESET_GROUPS: readonly FigureExportPresetGroup[] = (() => {
  const groups: { label: string; presets: FigureExportResolution[] }[] = [];
  for (const preset of FIGURE_EXPORT_PRESETS) {
    const last = groups[groups.length - 1];
    if (last && last.label === preset.group) {
      last.presets.push(preset);
    } else {
      groups.push({ label: preset.group, presets: [preset] });
    }
  }
  return groups;
})();

/**
 * `'16:9'`, `'4:3'`, `'1:1'` — the gcd-reduced ratio of one size.
 *
 * A ratio whose reduced terms are both small is the name a reader already knows the shape by. Above
 * {@link RATIO_TERM_CEILING} neither term means anything — A4's 877:620 names nothing — so the
 * width is expressed in units of the height instead.
 */
export function aspectRatioLabel(width: number, height: number): string {
  if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) {
    return '';
  }
  const divisor = greatestCommonDivisor(Math.round(width), Math.round(height));
  const reducedWidth = Math.round(width) / divisor;
  const reducedHeight = Math.round(height) / divisor;
  if (reducedWidth > RATIO_TERM_CEILING || reducedHeight > RATIO_TERM_CEILING) {
    return `${(width / height).toFixed(2)}:1`;
  }
  return `${reducedWidth}:${reducedHeight}`;
}

/** The narrowest and the shortest composition an explicit size is laid out in, in layout px. */
export const FIGURE_EXPORT_LAYOUT_WIDTH = 960;
export const FIGURE_EXPORT_LAYOUT_HEIGHT = 540;

/** Bounds on either side of a custom size. */
export const FIGURE_EXPORT_MIN_DIMENSION = 320;
export const FIGURE_EXPORT_MAX_DIMENSION = 8000;

/** The longest side of a written bitmap; a 2D context past this comes back null in Chromium. */
export const FIGURE_EXPORT_MAX_BITMAP_DIMENSION = 16384;

/**
 * Non-null when `width × height` at `density` exceeds the bitmap cap on either side.
 *
 * Refused here rather than at the encoder: past the cap `getContext('2d')` returns null and
 * `toBlob` yields nothing, which reaches the reader as a bare "could not be exported" with no
 * reason and no way to tell which of the two controls to lower.
 */
export function bitmapRefusal(
  width: number,
  height: number,
  density: FigureExportDensity
): string | null {
  const pixelWidth = Math.round(width * density);
  const pixelHeight = Math.round(height * density);
  if (
    pixelWidth <= FIGURE_EXPORT_MAX_BITMAP_DIMENSION &&
    pixelHeight <= FIGURE_EXPORT_MAX_BITMAP_DIMENSION
  ) {
    return null;
  }
  return (
    `At ${densityPercentLabel(density)} a ${Math.round(width)} × ${Math.round(height)} px export ` +
    `would be ${pixelWidth} × ${pixelHeight} px; each side of the written image must be at most ` +
    `${FIGURE_EXPORT_MAX_BITMAP_DIMENSION} px. Choose a lower density or a smaller size.`
  );
}

/** The smallest plot box a figure may be composed with, in layout px. */
export const FIGURE_EXPORT_MIN_PLOT_HEIGHT = 160;

/** The largest reduced term {@link aspectRatioLabel} will print as a ratio rather than a decimal. */
const RATIO_TERM_CEILING = 32;

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
  /** Read only where there is no `layout`: the density the live canvas is composed at. */
  readonly density?: FigureExportDensity;
  readonly webpQuality?: WebpQuality;
}

/** What `encodeFigureImage` produced, including the format actually written. */
export interface FigureExportResult {
  readonly blob: Blob;
  /** The requested format, or `'png'` where the browser could not encode WebP. */
  readonly format: FigureExportFormat;
  /** True when a WebP request was silently answered with a PNG. */
  readonly fellBackToPng: boolean;
}

/** Layout constants, in CSS pixels before the density transform is applied. */
const PADDING = 20;
const TITLE_SIZE = 18;
const SUBTITLE_SIZE = 13;
const BODY_SIZE = 12;
const LINE_GAP = 6;
const RULE_GAP = 12;

/** The narrowest content column a figure is laid out in. */
const MIN_CONTENT_WIDTH = 360;

/**
 * The card ground the comparison view draws on, so an exported figure matches what was on screen.
 *
 * Exported because the table composer draws on the same ground: two palettes would make a figure
 * and the table beside it in one document read as coming from two applications.
 */
export const FIGURE_BACKGROUND = '#181818';
export const FIGURE_TITLE_COLOR = '#e0ba6d';
export const FIGURE_BODY_COLOR = '#d4d4d8';
export const FIGURE_MUTED_COLOR = '#a1a1aa';
export const FIGURE_RULE_COLOR = '#2a2a2a';

export const FIGURE_FONT_STACK = '"Segoe UI", "Helvetica Neue", Arial, sans-serif';

/**
 * The composition box for one target bitmap.
 *
 * Typography is fixed in layout px, so the box decides how much plot a size gets. Anchoring the
 * width alone gives a 21:9 image a 405 px tall composition and a plot shorter than its caption;
 * anchoring both minimums lets a wide image grow wider and a tall image grow taller, at the same
 * type size. Density is what maps the box onto the bitmap and is the same on both axes, so the
 * box always has the target's exact aspect ratio.
 */
export function layoutBoxFor(pixelWidth: number, pixelHeight: number):
  { layoutWidth: number; layoutHeight: number; density: number } {
  const density = Math.min(
    pixelWidth / FIGURE_EXPORT_LAYOUT_WIDTH,
    pixelHeight / FIGURE_EXPORT_LAYOUT_HEIGHT
  );
  return { layoutWidth: pixelWidth / density, layoutHeight: pixelHeight / density, density };
}

/**
 * Resolves one figure's composition box, or refuses it.
 *
 * The chrome is measured with the same wrapping code {@link composeFigureImage} draws with, at the
 * same content width, so the refusal threshold and the drawn image can never disagree.
 *
 * `refusal` is non-null when the requested height leaves less than
 * {@link FIGURE_EXPORT_MIN_PLOT_HEIGHT} for the plot once the chrome is measured; it names the
 * figure and the minimum height that would work for it, or when the bitmap `density` asks for
 * exceeds {@link FIGURE_EXPORT_MAX_BITMAP_DIMENSION}. A refused figure returns `layout: null`.
 *
 * `density` multiplies the bitmap and leaves the composition box alone, so it is required rather
 * than defaulted: a call site that omitted it would silently keep a factor of its own.
 */
export function resolveFigureLayout(
  request: Omit<FigureExportRequest, 'canvas' | 'format'>,
  resolution: FigureExportResolution,
  onScreen: { width: number; height: number },
  density: FigureExportDensity
): { layout: FigureExportLayout | null; refusal: string | null } {
  if (resolution.widthPx === null && resolution.heightPx === null) {
    const layout = onScreenLayout(request, onScreen, density);
    const refusal = bitmapRefusal(layout.layoutWidth, layout.layoutHeight, density);
    return refusal ? { layout: null, refusal } : { layout, refusal: null };
  }

  const requestedWidth = resolution.widthPx;
  const requestedHeight = resolution.heightPx;
  if (!isUsableDimension(requestedWidth) || !isUsableDimension(requestedHeight)) {
    return { layout: null, refusal: dimensionRefusal(requestedWidth, requestedHeight) };
  }

  const pixelWidth = Math.round(requestedWidth);
  const pixelHeight = Math.round(requestedHeight);
  const oversized = bitmapRefusal(pixelWidth, pixelHeight, density);
  if (oversized) {
    return { layout: null, refusal: oversized };
  }

  const box = layoutBoxFor(pixelWidth, pixelHeight);
  const { layoutWidth, layoutHeight } = box;
  const plotWidth = layoutWidth - PADDING * 2;

  const chrome = measureFigureChrome(request, plotWidth);
  const plotHeight = layoutHeight - chrome.height;
  if (plotHeight < FIGURE_EXPORT_MIN_PLOT_HEIGHT) {
    // In the requested size's own units, which is what the reader typed: the density multiplies
    // whatever height they choose, so naming a written figure here would not answer their question.
    const minimumHeight = Math.ceil((chrome.height + FIGURE_EXPORT_MIN_PLOT_HEIGHT) * box.density);
    return {
      layout: null,
      refusal:
        `${figureName(request.title)} does not fit ${pixelWidth} × ${pixelHeight} px: its title, ` +
        `caption and notices leave too little room for the plot. Export it at ` +
        `${minimumHeight} px tall or more at this width.`
    };
  }

  return {
    layout: {
      layoutWidth,
      layoutHeight,
      plotWidth,
      plotHeight,
      density: box.density * density,
      pixelWidth: Math.round(pixelWidth * density),
      pixelHeight: Math.round(pixelHeight * density)
    },
    refusal: null
  };
}

/** The box a preview is shown in, in CSS px, and the device pixel ratio to rasterise at. */
export interface PreviewStage {
  readonly width: number;
  readonly height: number;
  readonly devicePixelRatio: number;
}

/**
 * The export's own layout, rasterised at the density that fits the stage.
 *
 * Every field that shapes the composition — layoutWidth, layoutHeight, plotWidth, plotHeight —
 * is carried over unchanged, so the preview is the export re-rendered, never a scaled copy of it:
 * only density, pixelWidth and pixelHeight differ. Letterboxed to the target's aspect ratio, and
 * never larger than the target itself. Returns null for a stage with no usable area.
 */
export function previewLayoutFor(
  target: FigureExportLayout,
  stage: PreviewStage
): { layout: FigureExportLayout; cssWidth: number; cssHeight: number } | null {
  const dpr = Math.min(4, Math.max(1, stage.devicePixelRatio));
  const aspect = target.pixelWidth / target.pixelHeight;
  if (!Number.isFinite(aspect) || aspect <= 0) {
    return null;
  }

  const cssWidth = Math.min(stage.width, stage.height * aspect, target.pixelWidth / dpr);
  const cssHeight = cssWidth / aspect;
  if (!(cssWidth >= 1) || !(cssHeight >= 1)) {
    return null;
  }

  const pixelWidth = Math.round(cssWidth * dpr);
  const pixelHeight = Math.round(cssHeight * dpr);
  return {
    layout: { ...target, density: pixelWidth / target.layoutWidth, pixelWidth, pixelHeight },
    cssWidth,
    cssHeight
  };
}

/**
 * Draws the chart and all of its chrome onto a new offscreen canvas.
 *
 * With a `layout`, the composition is laid out at `layout.layoutWidth`, scaled by `layout.density`
 * and given a plot box of exactly `layout.plotHeight`; the bitmap takes the layout's pixel
 * dimensions verbatim, which is what makes a requested resolution exact. Without one, the
 * composition follows the source canvas at `request.density`, or 1.
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
  const density = layout ? layout.density : (request.density ?? 1);

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
  context.fillStyle = FIGURE_BACKGROUND;
  context.fillRect(0, 0, width, height);

  context.textBaseline = 'top';
  let y = PADDING;

  y = drawBlock(context, chrome.titleLines, PADDING, y, TITLE_SIZE, '600', FIGURE_TITLE_COLOR);
  y = drawBlock(context, chrome.subtitleLines, PADDING, y, SUBTITLE_SIZE, '400', FIGURE_MUTED_COLOR);
  if (chrome.titleLines.length > 0 || chrome.subtitleLines.length > 0) {
    y += LINE_GAP;
  }

  if (chartWidth > 0 && chartHeight > 0 && plotWidth > 0 && plotHeight > 0) {
    context.drawImage(request.canvas, PADDING, y, plotWidth, plotHeight);
  }
  y += plotHeight;

  if (chrome.captionLines.length > 0) {
    y += LINE_GAP;
    y = drawBlock(context, chrome.captionLines, PADDING, y, BODY_SIZE, '400', FIGURE_BODY_COLOR);
  }
  for (const lines of chrome.noticeLines) {
    y += LINE_GAP;
    y = drawBlock(context, lines, PADDING, y, BODY_SIZE, '400', FIGURE_MUTED_COLOR);
  }

  if (chrome.footerLines.length > 0) {
    y += RULE_GAP / 2;
    context.strokeStyle = FIGURE_RULE_COLOR;
    context.lineWidth = 1;
    context.beginPath();
    context.moveTo(PADDING, Math.round(y) + 0.5);
    context.lineTo(width - PADDING, Math.round(y) + 0.5);
    context.stroke();
    y += RULE_GAP / 2;
    drawBlock(context, chrome.footerLines, PADDING, y, BODY_SIZE, '400', FIGURE_MUTED_COLOR);
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
  // Stated on the element, not left to the container: `responsive: false` is what keeps the chart
  // off the visible page's layout, and it also stops Chart.js reading the container at all, so an
  // unsized canvas would keep the HTML default of 300 × 150 and `retinaScale` would multiply that.
  canvas.width = layout.plotWidth;
  canvas.height = layout.plotHeight;
  canvas.style.width = `${layout.plotWidth}px`;
  canvas.style.height = `${layout.plotHeight}px`;
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
  format: FigureExportFormat,
  quality: WebpQuality = DEFAULT_WEBP_QUALITY
): Promise<FigureExportResult> {
  const mime = format === 'webp' ? 'image/webp' : 'image/png';
  const encoderQuality = format === 'webp' ? webpEncoderQuality(quality) : undefined;

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
      encoderQuality
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
  const safeId = (figureId || 'figure').replace(/[^A-Za-z0-9_-]/g, '-');
  return `model-comparison_${safeId}_${exportTimestamp(now)}.${format}`;
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

/** One file of an archive: the name it is stored under and the encoded image. */
export interface FigureArchiveEntry {
  name: string;
  blob: Blob;
}

/** The one function this module calls from `fflate`, typed to the shape version 0.8 exports. */
export interface ZipWriterModule {
  zipSync(
    data: Record<string, [Uint8Array, { level: 0 }]>,
    options?: { level: 0 }
  ): Uint8Array;
}

/**
 * The zip encoder, behind a holder rather than a bare `import()` call.
 *
 * Dynamic, so the admin bundle pays for `fflate` on the first archive rather than on load;
 * behind a holder so a spec can stand a fake in its place, which a bare dynamic import offers no
 * seam for. Same shape as `xlsxWriterModule` in `table-export.ts`.
 */
export const zipWriterModule: { load(): Promise<ZipWriterModule> } = {
  load: async (): Promise<ZipWriterModule> => (await import('fflate')) as unknown as ZipWriterModule
};

/**
 * Packs already-encoded figures into one zip.
 *
 * Stored, not deflated: PNG and WebP are already compressed, and deflating them again costs CPU
 * for a size change of a few bytes either way. Synchronous rather than `fflate`'s worker-backed
 * `zip()`: the Content-Security-Policy's `worker-src` falls back to `'self'` with no `blob:`, and
 * `fflate` builds its workers from blob URLs, so the async path would be refused at runtime.
 */
export async function buildFigureArchive(entries: readonly FigureArchiveEntry[]): Promise<Blob> {
  const writer = await zipWriterModule.load();
  const files: Record<string, [Uint8Array, { level: 0 }]> = {};
  for (const entry of entries) {
    files[entry.name] = [new Uint8Array(await entry.blob.arrayBuffer()), { level: 0 }];
  }
  return new Blob([writer.zipSync(files, { level: 0 }) as unknown as BlobPart], { type: 'application/zip' });
}

/** `model-comparison_figures_YYYYMMDD_HHMMSS.zip`, the same stamp shape the figures carry. */
export function figureArchiveFilename(now: Date = new Date()): string {
  return `model-comparison_figures_${exportTimestamp(now)}.zip`;
}

/** What a clipboard write did: it succeeded, the browser has no such API, or it was refused. */
export type ClipboardImageOutcome = 'copied' | 'unsupported' | 'denied';

/**
 * Writes one image onto the system clipboard.
 *
 * Only PNG is worth passing here: every engine that implements `ClipboardItem` rejects
 * `image/webp` in one, so a WebP blob returns `'denied'` rather than landing on the clipboard.
 *
 * Never throws. The API is absent outside a secure context and can be refused inside one — by a
 * permission prompt, by a document that is not focused — and a caller's only sane response to
 * either is an inline message, which is what the three outcomes are for.
 */
export async function copyImageToClipboard(blob: Blob): Promise<ClipboardImageOutcome> {
  const clipboard = navigator.clipboard as Clipboard | undefined;
  if (!clipboard || typeof clipboard.write !== 'function' || typeof ClipboardItem === 'undefined') {
    return 'unsupported';
  }
  try {
    await clipboard.write([new ClipboardItem({ [blob.type]: blob })]);
    return 'copied';
  } catch {
    return 'denied';
  }
}

// -----------------------------------------------------------------------------------------------
// Internals
// -----------------------------------------------------------------------------------------------

/** `yyyyMMdd_HHmmss` in local time: sortable, and shared by every filename this module writes. */
function exportTimestamp(now: Date): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  return (
    `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}_` +
    `${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`
  );
}

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

/** The box the live canvas is already laid out in, written at the chosen density. */
function onScreenLayout(
  request: FigureChromeSource,
  onScreen: { width: number; height: number },
  density: FigureExportDensity
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
    density,
    pixelWidth: Math.round(layoutWidth * density),
    pixelHeight: Math.round(layoutHeight * density)
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

/** Euclid, iteratively: the terms reach 8000 at most, but recursion buys nothing here. */
function greatestCommonDivisor(a: number, b: number): number {
  let left = Math.abs(a);
  let right = Math.abs(b);
  while (right > 0) {
    const remainder = left % right;
    left = right;
    right = remainder;
  }
  return left === 0 ? 1 : left;
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
  return `${weight} ${size}px ${FIGURE_FONT_STACK}`;
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

/**
 * Greedy word wrap. A single word wider than the column is left to overflow rather than broken.
 *
 * Exported for the table composer, which wraps header and cell text into the same typography.
 */
export function wrapText(
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
