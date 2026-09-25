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
 *    it being misread, so the title, badges and the Better badge, detail, key, highlight, every
 *    note and the footer are drawn into the same bitmap as the plot.
 * 2. **The background is opaque by default.** Chart.js canvases are transparent; a PNG of one
 *    dropped into a light document renders as dark-on-dark and is unreadable, so the theme's ground
 *    is painted first. A transparent image (`theme.background === null`) is an explicit choice, made
 *    for a slide or page whose own colour the theme was picked to suit; PNG and WebP carry its alpha.
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

import type {
  FigureBadge,
  FigureBadgeTone,
  FigureChrome,
  FigureFooter,
  FigureKeyGlyph,
  FigureKeyItem,
  FigureNoteTone
} from './figure-chrome';
import { figureDirectionRotation } from './figure-chrome';
import { resolveFigureTheme } from './figure-theme';
import type { ResolvedChromeColors, ResolvedFigureBorder, ResolvedFigureTheme } from './figure-theme';
import {
  PREVIEW_MAX_ZOOM,
  PreviewViewRequest,
  previewRasterZoom,
  resolvePreviewZoom
} from './preview-view';

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
  /** `'hd' | 'fullhd' | … | 'custom'`. */
  readonly id: string;
  readonly label: string;
  readonly widthPx: number;
  readonly heightPx: number;
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

/** Bounds on the export text size, as percentages of the default composition's type size. */
export const FIGURE_EXPORT_MIN_TEXT_SCALE_PERCENT = 50;
export const FIGURE_EXPORT_MAX_TEXT_SCALE_PERCENT = 250;

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

/** The composed figure's caption sizes, in layout px. */
export interface FigureChromeTextSizes {
  readonly titlePx: number;
  /** Also sizes the Better badge. */
  readonly badgePx: number;
  /** The footer's body text; its `SUITE` eyebrow keeps the 10 : 12 ratio to it. */
  readonly footerPx: number;
}

export const DEFAULT_FIGURE_TEXT_SIZES: FigureChromeTextSizes = { titlePx: 18, badgePx: 11, footerPx: 12 };

/** One figure, with every piece of chrome the exported image must carry. */
export interface FigureExportRequest {
  /** The plot, as {@link renderPlotOffscreen} renders it. Read, never mutated. */
  readonly canvas: HTMLCanvasElement;
  /** Title, badges, direction, detail, key, highlight and notes: everything the card and the export share. */
  readonly chrome: FigureChrome;
  /** The export's last line: suite on the left, computation time on the right. An empty one draws nothing. */
  readonly footer: FigureFooter;
  /** Absent draws at {@link DEFAULT_FIGURE_TEXT_SIZES}. */
  readonly textSizes?: FigureChromeTextSizes;
  readonly format: FigureExportFormat;
  /** From {@link resolveFigureLayout}. Absent composes at the source canvas's own size, at `density`. */
  readonly layout?: FigureExportLayout | null;
  /** Read only where there is no `layout`: the density the source canvas is composed at. */
  readonly density?: FigureExportDensity;
  readonly webpQuality?: WebpQuality;
  /** Colours, chrome font and border. Absent draws the dark theme, {@link resolveFigureTheme}'s default. */
  readonly theme?: ResolvedFigureTheme;
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
const LINE_GAP = 6;
const RULE_GAP = 12;

/** The space between the header block (title, badges, detail) and the plot. */
const PLOT_GAP = 16;

/** The narrowest content column a figure is laid out in. */
const MIN_CONTENT_WIDTH = 360;

/** Badge pill geometry; the text size and the pill height come from the request's badge size. */
const BADGE_RADIUS = 4;
const BADGE_BORDER_WIDTH = 1;
const BADGE_PAD_X = 7;
const BADGE_PAD_Y = 3;
const BADGE_GAP = 6;

/** The Better badge: padding either side, the arrow-to-label gap and the arrow's stroke floor. */
const DIRECTION_PAD_START = BADGE_PAD_X - 1;
const DIRECTION_PAD_END = BADGE_PAD_X + 2;
const DIRECTION_ARROW_GAP = 5;
const DIRECTION_ARROW_MIN_STROKE = 2;
/**
 * The dark theme's Better badge border and fill; its ink is {@link FIGURE_TITLE_COLOR}. The composer
 * draws with `theme.chrome.direction`, which resolves to these under the dark theme.
 */
export const DIRECTION_COLORS = { border: 'rgba(224, 186, 109, 0.55)', fill: 'rgba(224, 186, 109, 0.1)' } as const;

/** The detail line under the badge row. */
const DETAIL_SIZE = 12;

/** Key row geometry and typography. */
const KEY_GLYPH_SIZE = 12;
const KEY_TEXT_SIZE = 12;
const KEY_GLYPH_TEXT_GAP = 6;
const KEY_ITEM_GAP = 16;
const KEY_ROW_HEIGHT = Math.round(KEY_TEXT_SIZE * 1.4);
const KEY_FRONTIER_LENGTH = 14;
const KEY_DOMINATED_SIDE = 10;

/** The highlight line under the key row. */
const HIGHLIGHT_SIZE = 12;

/** One note's rule and indent. */
const NOTE_SIZE = 12;
const NOTE_RULE_WIDTH = 2;
const NOTE_INDENT = 10;

/** The footer's eyebrow label and the gap it leaves before the suite name and the right column. */
const FOOTER_LABEL_TEXT = 'SUITE';
const FOOTER_LABEL_GAP = 8;
const FOOTER_LABEL_LETTER_SPACING = 1;
const FOOTER_MIN_GAP = 16;

/**
 * The dark theme's ground and chrome inks, which `resolveFigureTheme()` resolves to by default.
 *
 * The composers draw with the resolved theme, never with these; they stay as named aliases of the
 * default palette, which the figure and the table image share so that a figure and the table beside
 * it in one document read as coming from one application.
 */
export const FIGURE_BACKGROUND = '#181818';
export const FIGURE_TITLE_COLOR = '#e0ba6d';
export const FIGURE_BODY_COLOR = '#d4d4d8';
export const FIGURE_MUTED_COLOR = '#a1a1aa';
export const FIGURE_RULE_COLOR = '#2a2a2a';

/** The dark theme's key glyph ink, and the dominated glyph's fill. */
export const FIGURE_KEY_INK = '#c3c2b7';
export const FIGURE_DOMINATED_FILL = 'rgba(255, 255, 255, 0.12)';

/**
 * The dark theme's badge pill colours by tone: border, fill, then text. The composer draws with
 * `theme.chrome.badge`, which resolves to these under the dark theme.
 */
export const BADGE_TONE_COLORS: Record<FigureBadgeTone, { readonly border: string; readonly fill: string; readonly text: string }> = {
  neutral: { border: 'rgba(255, 255, 255, 0.25)', fill: 'rgba(255, 255, 255, 0.04)', text: FIGURE_TITLE_COLOR },
  pricing: { border: 'rgba(16, 185, 129, 0.3)', fill: 'rgba(16, 185, 129, 0.1)', text: '#6ee7b7' }
};

/** The dark theme's note colours by tone: left rule, then text. */
export const NOTE_TONE_COLORS: Record<FigureNoteTone, { readonly rule: string; readonly text: string }> = {
  warning: { rule: '#e0ba6d', text: '#e0ba6d' },
  info: { rule: '#6b6b66', text: FIGURE_MUTED_COLOR }
};

/** The chrome stack under the *Overseer default* font. */
export const FIGURE_FONT_STACK = '"Segoe UI", "Helvetica Neue", Arial, sans-serif';

/** The chrome's colours and font stack, as the draw helpers read them. */
interface ChromePaint {
  readonly colors: ResolvedChromeColors;
  readonly stack: string;
}

/**
 * The composition box for one target bitmap.
 *
 * Typography is fixed in layout px, so the box decides how much plot a size gets. Anchoring the
 * width alone gives a 21:9 image a 405 px tall composition and a plot shorter than its chrome;
 * anchoring both minimums lets a wide image grow wider and a tall image grow taller, at the same
 * type size. Density is what maps the box onto the bitmap and is the same on both axes, so the
 * box always has the target's exact aspect ratio.
 *
 * `textScale` multiplies the density: a larger scale composes in a smaller box, so every glyph —
 * the chrome and the plot's own text alike — grows by that factor while the bitmap size stays put.
 */
export function layoutBoxFor(pixelWidth: number, pixelHeight: number, textScale = 1):
  { layoutWidth: number; layoutHeight: number; density: number } {
  const density = Math.min(
    pixelWidth / FIGURE_EXPORT_LAYOUT_WIDTH,
    pixelHeight / FIGURE_EXPORT_LAYOUT_HEIGHT
  ) * textScale;
  return { layoutWidth: pixelWidth / density, layoutHeight: pixelHeight / density, density };
}

/**
 * Resolves one figure's composition box, or refuses it.
 *
 * The chrome is measured with the same wrapping code {@link composeFigureImage} draws with, at the
 * same content width and in the request theme's font stack and heading weight, so the refusal
 * threshold and the drawn image can never disagree.
 *
 * `refusal` is non-null when the requested height leaves less than
 * {@link FIGURE_EXPORT_MIN_PLOT_HEIGHT} for the plot once the chrome is measured; it names the
 * figure and the minimum height that would work for it, or when the bitmap `density` asks for
 * exceeds {@link FIGURE_EXPORT_MAX_BITMAP_DIMENSION}. A refused figure returns `layout: null`.
 *
 * `density` multiplies the bitmap and leaves the composition box alone, so it is required rather
 * than defaulted: a call site that omitted it would silently keep a factor of its own. `textScale`
 * is required for the same reason; it shrinks the composition box ({@link layoutBoxFor}) at every
 * size.
 */
export function resolveFigureLayout(
  request: Omit<FigureExportRequest, 'canvas' | 'format'>,
  resolution: FigureExportResolution,
  density: FigureExportDensity,
  textScale: number
): { layout: FigureExportLayout | null; refusal: string | null } {
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

  const box = layoutBoxFor(pixelWidth, pixelHeight, textScale);
  const { layoutWidth, layoutHeight } = box;
  const plotWidth = layoutWidth - PADDING * 2;
  if (plotWidth < MIN_CONTENT_WIDTH) {
    return {
      layout: null,
      refusal:
        `At ${Math.round(textScale * 100)}% text, ${figureName(request.chrome.title)} does not fit ` +
        `${pixelWidth} × ${pixelHeight} px: the caption column would be narrower than ` +
        `${MIN_CONTENT_WIDTH} px. Lower the text size or choose a wider export.`
    };
  }

  const chrome = measureFigureChrome(request, plotWidth);
  const plotHeight = layoutHeight - chrome.height;
  if (plotHeight < FIGURE_EXPORT_MIN_PLOT_HEIGHT) {
    // In the requested size's own units, which is what the reader typed: the density multiplies
    // whatever height they choose, so naming a written figure here would not answer their question.
    const minimumHeight = Math.ceil((chrome.height + FIGURE_EXPORT_MIN_PLOT_HEIGHT) * box.density);
    return {
      layout: null,
      refusal:
        `${figureName(request.chrome.title)} does not fit ${pixelWidth} × ${pixelHeight} px: its ` +
        `header, key and notes leave too little room for the plot. Export it at ` +
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

/** What {@link previewLayoutFor} fitted: the raster to compose, and the box to show it in. */
export interface PreviewLayout {
  readonly layout: FigureExportLayout;
  /** The CSS box at the requested zoom; it may exceed the stage. */
  readonly cssWidth: number;
  readonly cssHeight: number;
  /** The zoom that fills the stage, at most {@link PREVIEW_MAX_ZOOM}. */
  readonly screenFitZoom: number;
  /** The requested view, resolved: device pixels per export pixel. */
  readonly zoom: number;
  /** The fraction of the target's pixels the raster carries. */
  readonly rasterZoom: number;
  /** True where the raster budget, not the zoom, set the raster's size. */
  readonly rasterCapped: boolean;
}

/**
 * The export's own layout, rasterised for the stage at the requested view.
 *
 * Every field that shapes the composition — layoutWidth, layoutHeight, plotWidth, plotHeight —
 * is carried over unchanged, so the preview is the export re-rendered, never a different
 * composition: only density, pixelWidth and pixelHeight differ. The raster is the displayed pixels
 * below 100 % and the target's own at and above it, so an enlarged view shows the export's pixels
 * magnified rather than a sharper rendering the file would not contain; past
 * `PREVIEW_MAX_RASTER_PIXELS` it is scaled down further. `'default'` letterboxes the figure
 * into the stage and never enlarges it. Returns null for a stage with no usable area.
 */
export function previewLayoutFor(
  target: FigureExportLayout,
  stage: PreviewStage,
  view: PreviewViewRequest = 'default'
): PreviewLayout | null {
  const dpr = Math.min(4, Math.max(1, stage.devicePixelRatio));
  const aspect = target.pixelWidth / target.pixelHeight;
  if (!Number.isFinite(aspect) || aspect <= 0) {
    return null;
  }

  const screenFitZoom = Math.min(
    PREVIEW_MAX_ZOOM,
    stage.width * dpr / target.pixelWidth,
    stage.height * dpr / target.pixelHeight
  );
  if (!(stage.width >= 1) || !(stage.height >= 1) || !(screenFitZoom > 0)) {
    return null;
  }

  const zoom = resolvePreviewZoom(view, screenFitZoom);
  const cssWidth = target.pixelWidth / dpr * zoom;
  const cssHeight = cssWidth / aspect;
  if (!(cssWidth >= 1) || !(cssHeight >= 1)) {
    return null;
  }

  const raster = previewRasterZoom(zoom, target.pixelWidth, target.pixelHeight);
  const pixelWidth = Math.max(1, Math.round(target.pixelWidth * raster.zoom));
  const pixelHeight = Math.max(1, Math.round(target.pixelHeight * raster.zoom));
  return {
    layout: { ...target, density: pixelWidth / target.layoutWidth, pixelWidth, pixelHeight },
    cssWidth,
    cssHeight,
    screenFitZoom,
    zoom,
    rasterZoom: raster.zoom,
    rasterCapped: raster.capped
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
 *
 * Colours, the chrome font and the heading weight come from `request.theme`, the dark theme when
 * absent. The ground is painted first ({@link paintFigureBackground}) and the border last
 * ({@link drawFigureBorder}), inside the bitmap, so neither changes its pixel size.
 */
export function composeFigureImage(request: FigureExportRequest): HTMLCanvasElement {
  const theme = request.theme ?? resolveFigureTheme();
  const paint: ChromePaint = { colors: theme.chrome, stack: theme.fonts.chromeStack };
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

  // Painted before anything else: a transparent PNG of a Chart.js canvas is dark-on-dark in any
  // light document it is pasted into, so only a theme that asks for it leaves the ground empty.
  paintFigureBackground(context, width, height, theme);

  context.textBaseline = 'top';
  let y = PADDING;

  const sizes = chrome.sizes;
  y = drawBlock(
    context, chrome.titleLines, PADDING, y, sizes.titlePx, String(theme.fonts.headingWeight), paint.colors.title, paint.stack);

  const badgeRowCount = badgeRowCountOf(chrome);
  if (badgeRowCount > 0) {
    y += LINE_GAP;
    drawBadgeRows(context, chrome.badgeRows, PADDING, y, sizes, paint);
    if (chrome.direction) {
      // Right-aligned on the first badge row, which it shares the height of.
      drawDirectionBadge(context, chrome.direction, PADDING + contentWidth - chrome.direction.width, y, sizes, paint);
    }
    y += badgeRowCount * sizes.badgeHeight + (badgeRowCount - 1) * BADGE_GAP;
  }

  if (chrome.detailLines.length > 0) {
    y += LINE_GAP;
    y = drawBlock(context, chrome.detailLines, PADDING, y, DETAIL_SIZE, '400', paint.colors.muted, paint.stack);
  }

  y += PLOT_GAP;
  if (chartWidth > 0 && chartHeight > 0 && plotWidth > 0 && plotHeight > 0) {
    context.drawImage(request.canvas, PADDING, y, plotWidth, plotHeight);
  }
  y += plotHeight;

  if (chrome.keyRows.length > 0) {
    y += LINE_GAP;
    y = drawKeyRows(context, chrome.keyRows, PADDING, y, paint);
  }

  if (chrome.highlightLines.length > 0) {
    y += LINE_GAP;
    y = drawBlock(context, chrome.highlightLines, PADDING, y, HIGHLIGHT_SIZE, '600', paint.colors.body, paint.stack);
  }

  for (const note of chrome.noteBlocks) {
    y += LINE_GAP;
    y = drawNote(context, note, PADDING, y, paint);
  }

  if (chrome.footer.height > 0) {
    y += RULE_GAP / 2;
    context.strokeStyle = paint.colors.rule;
    context.lineWidth = 1;
    context.beginPath();
    context.moveTo(PADDING, Math.round(y) + 0.5);
    context.lineTo(width - PADDING, Math.round(y) + 0.5);
    context.stroke();
    y += RULE_GAP / 2;
    drawFooter(context, chrome.footer, PADDING, y, width - PADDING * 2, sizes, paint);
  }

  drawFigureBorder(context, width, height, theme.border);
  return target;
}

/**
 * Paints the ground of a composed image, `width` × `height` in the context's own units.
 *
 * A transparent theme (`background === null`) paints nothing. Otherwise the ground is a rectangle
 * rounded by the border's radius, so the corners outside the radius stay transparent; at radius 0
 * it is one full `fillRect`. Shared with the table composer.
 */
export function paintFigureBackground(
  context: CanvasRenderingContext2D,
  width: number,
  height: number,
  theme: ResolvedFigureTheme
): void {
  if (theme.background === null) {
    return;
  }
  context.fillStyle = theme.background;
  const radius = theme.border?.radiusPx ?? 0;
  if (radius > 0) {
    pathRoundedRect(context, 0, 0, width, height, radius);
    context.fill();
  } else {
    context.fillRect(0, 0, width, height);
  }
}

/**
 * Strokes the border of a composed image, inset by half its width so that the whole stroke lies
 * inside the bitmap and inside the padding, and its outer edge follows the ground's radius. Draws
 * nothing for a null border. Shared with the table composer.
 */
export function drawFigureBorder(
  context: CanvasRenderingContext2D,
  width: number,
  height: number,
  border: ResolvedFigureBorder | null
): void {
  if (!border || !(border.widthPx > 0)) {
    return;
  }
  const inset = border.widthPx / 2;
  context.strokeStyle = border.color;
  context.lineWidth = border.widthPx;
  pathRoundedRect(
    context, inset, inset, width - border.widthPx, height - border.widthPx, Math.max(0, border.radiusPx - inset));
  context.stroke();
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
 * Every figure — on the page, in the preview and in a download — is plotted here, so all three are
 * the same rendering. The chart is built in a container parked off-screen, sized to the layout's
 * plot box, and torn down again — construction, snapshot, `destroy` and removal all inside one
 * `try/finally`, so a throw cannot strand a detached chart or its container.
 *
 * The returned canvas is a copy: `destroy` clears the chart's own canvas. Failure returns null, and
 * the caller reports the figure as not composed.
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

/** One row of wrapped badge pills, each with the width it measured at. */
interface MeasuredBadgeRow {
  readonly badges: readonly { readonly badge: FigureBadge; readonly width: number }[];
}

/** One row of wrapped key items, each with the width it measured at. */
interface MeasuredKeyRow {
  readonly items: readonly { readonly item: FigureKeyItem; readonly width: number }[];
}

/** One note, wrapped to the content column less its indent. */
interface MeasuredNote {
  readonly tone: FigureNoteTone;
  readonly lines: readonly string[];
}

/** The footer's two texts, and whether the right one wrapped onto a second line. */
interface MeasuredFooter {
  readonly labelText: string;
  readonly suiteText: string;
  readonly computedText: string;
  readonly twoLines: boolean;
  /** 0 when both `suite` and `computedAt` are empty: the footer draws nothing and takes no height. */
  readonly height: number;
}

/** The Better badge: its arrow's rotation, its word and the pill width it measured at. */
interface MeasuredDirection {
  readonly rotation: number;
  readonly label: string;
  readonly width: number;
}

/** The request's caption sizes, resolved into every size the measure and draw paths read. */
interface ResolvedTextSizes {
  readonly titlePx: number;
  readonly badgePx: number;
  readonly badgeHeight: number;
  readonly footerPx: number;
  readonly footerLabelPx: number;
  readonly footerLineHeight: number;
}

/** Everything the composition draws around the plot, wrapped to a content column and summed. */
export interface MeasuredChrome {
  readonly sizes: ResolvedTextSizes;
  readonly titleLines: string[];
  /** Narrowed by the Better badge's width, when there is one, so no badge runs under it. */
  readonly badgeRows: readonly MeasuredBadgeRow[];
  /** Drawn right-aligned on the first badge row, or on a row of its own when there are no badges. */
  readonly direction: MeasuredDirection | null;
  readonly detailLines: string[];
  readonly keyRows: readonly MeasuredKeyRow[];
  readonly highlightLines: string[];
  readonly noteBlocks: readonly MeasuredNote[];
  readonly footer: MeasuredFooter;
  /** The composition's height less the plot box: padding, every block and the gaps between. */
  readonly height: number;
}

type FigureChromeSource = Omit<FigureExportRequest, 'canvas' | 'format' | 'layout'>;

/**
 * Wraps and sums the chrome for one content width.
 *
 * The single source of both the drawn content and the height {@link resolveFigureLayout} subtracts
 * from a target box; duplicating either would let a figure be accepted at a height it cannot be
 * drawn at. An empty `detail`, `highlight` or `key` measures to no lines and adds no height. The
 * font stack and the title's weight are the source theme's, the dark theme's when absent, which is
 * what {@link composeFigureImage} draws with.
 */
export function measureFigureChrome(source: FigureChromeSource, contentWidth: number): MeasuredChrome {
  const theme = source.theme ?? resolveFigureTheme();
  const stack = theme.fonts.chromeStack;
  const measure = document.createElement('canvas').getContext('2d');
  const wrap = (text: string, size: number, weight: string): string[] =>
    measure ? wrapText(measure, text, contentWidth, size, weight, stack) : (text ? [text] : []);

  const chrome = source.chrome;
  const sizes = resolveTextSizes(source.textSizes ?? DEFAULT_FIGURE_TEXT_SIZES);
  const titleLines = wrap(chrome.title, sizes.titlePx, String(theme.fonts.headingWeight));
  const direction = chrome.direction
    ? {
        rotation: figureDirectionRotation(chrome.direction),
        label: chrome.direction.label,
        width: measure ? directionBadgeWidth(measure, chrome.direction.label, sizes, stack) : 0
      }
    : null;
  const badgeWidth = direction ? contentWidth - direction.width - BADGE_GAP : contentWidth;
  const badgeRows: MeasuredBadgeRow[] = measure
    ? wrapBadges(measure, chrome.badges, badgeWidth, sizes.badgePx, stack)
    : (chrome.badges.length > 0 ? [{ badges: chrome.badges.map(badge => ({ badge, width: 0 })) }] : []);
  const detailLines = wrap(chrome.detail, DETAIL_SIZE, '400');
  const keyRows: MeasuredKeyRow[] = measure
    ? wrapKeyItems(measure, chrome.key, contentWidth, stack)
    : (chrome.key.length > 0 ? [{ items: chrome.key.map(item => ({ item, width: 0 })) }] : []);
  const highlightLines = wrap(chrome.highlight, HIGHLIGHT_SIZE, '600');
  const noteBlocks: MeasuredNote[] =
    chrome.notes.map(note => ({ tone: note.tone, lines: wrap(note.text, NOTE_SIZE, '400') }));
  const footer = measureFooter(measure, source.footer, contentWidth, sizes, stack);

  let headerHeight = blockHeight(titleLines, sizes.titlePx);
  const badgeRowCount = badgeRowCountOf({ badgeRows, direction });
  if (badgeRowCount > 0) {
    headerHeight += LINE_GAP + badgeRowCount * sizes.badgeHeight + (badgeRowCount - 1) * BADGE_GAP;
  }
  if (detailLines.length > 0) {
    headerHeight += LINE_GAP + blockHeight(detailLines, DETAIL_SIZE);
  }

  let height = PADDING * 2;
  height += headerHeight;
  height += PLOT_GAP;
  if (keyRows.length > 0) {
    height += LINE_GAP + keyRows.length * KEY_ROW_HEIGHT + (keyRows.length - 1) * LINE_GAP;
  }
  if (highlightLines.length > 0) {
    height += LINE_GAP + blockHeight(highlightLines, HIGHLIGHT_SIZE);
  }
  for (const note of noteBlocks) {
    height += LINE_GAP + blockHeight(note.lines, NOTE_SIZE);
  }
  if (footer.height > 0) {
    height += RULE_GAP + footer.height;
  }

  return { sizes, titleLines, badgeRows, direction, detailLines, keyRows, highlightLines, noteBlocks, footer, height };
}

function resolveTextSizes(sizes: FigureChromeTextSizes): ResolvedTextSizes {
  return {
    titlePx: sizes.titlePx,
    badgePx: sizes.badgePx,
    badgeHeight: sizes.badgePx + BADGE_PAD_Y * 2,
    footerPx: sizes.footerPx,
    footerLabelPx: Math.round(sizes.footerPx * 10 / 12),
    footerLineHeight: Math.round(sizes.footerPx * 1.4)
  };
}

/** The badge rows, or one row for the Better badge alone when there are no other badges. */
function badgeRowCountOf(chrome: Pick<MeasuredChrome, 'badgeRows' | 'direction'>): number {
  return Math.max(chrome.badgeRows.length, chrome.direction ? 1 : 0);
}

/** The Better badge's arrow side, in layout px. */
function directionArrowSize(sizes: ResolvedTextSizes): number {
  return Math.round(sizes.badgePx * 1.3);
}

function directionBadgeWidth(
  context: CanvasRenderingContext2D,
  label: string,
  sizes: ResolvedTextSizes,
  stack: string
): number {
  context.font = fontOf(sizes.badgePx, '700', stack);
  return DIRECTION_PAD_START + directionArrowSize(sizes) + DIRECTION_ARROW_GAP
    + context.measureText(label).width + DIRECTION_PAD_END;
}

/** Wraps badge pills into rows that fit `maxWidth`, greedily, in the order given. */
function wrapBadges(
  context: CanvasRenderingContext2D,
  badges: readonly FigureBadge[],
  maxWidth: number,
  size: number,
  stack: string
): MeasuredBadgeRow[] {
  context.font = fontOf(size, '400', stack);
  const rows: MeasuredBadgeRow[] = [];
  let current: { badge: FigureBadge; width: number }[] = [];
  let rowWidth = 0;
  for (const badge of badges) {
    const width = context.measureText(badge.text).width + BADGE_PAD_X * 2;
    const advanced = current.length === 0 ? width : rowWidth + BADGE_GAP + width;
    if (current.length > 0 && advanced > maxWidth) {
      rows.push({ badges: current });
      current = [{ badge, width }];
      rowWidth = width;
    } else {
      current.push({ badge, width });
      rowWidth = advanced;
    }
  }
  if (current.length > 0) {
    rows.push({ badges: current });
  }
  return rows;
}

/** Wraps key items into rows that fit `maxWidth`, greedily, in the order given. */
function wrapKeyItems(
  context: CanvasRenderingContext2D,
  items: readonly FigureKeyItem[],
  maxWidth: number,
  stack: string
): MeasuredKeyRow[] {
  context.font = fontOf(KEY_TEXT_SIZE, '400', stack);
  const rows: MeasuredKeyRow[] = [];
  let current: { item: FigureKeyItem; width: number }[] = [];
  let rowWidth = 0;
  for (const item of items) {
    const width = KEY_GLYPH_SIZE + KEY_GLYPH_TEXT_GAP + context.measureText(item.text).width;
    const advanced = current.length === 0 ? width : rowWidth + KEY_ITEM_GAP + width;
    if (current.length > 0 && advanced > maxWidth) {
      rows.push({ items: current });
      current = [{ item, width }];
      rowWidth = width;
    } else {
      current.push({ item, width });
      rowWidth = advanced;
    }
  }
  if (current.length > 0) {
    rows.push({ items: current });
  }
  return rows;
}

/**
 * The footer's two texts and whether they fit one line.
 *
 * The same measurement {@link drawFooter} draws with, so a footer accepted at a height is the
 * footer drawn at it. `suite` and `computedAt` are independently optional: either alone still
 * draws, and both empty draws nothing.
 */
function measureFooter(
  context: CanvasRenderingContext2D | null,
  footer: FigureFooter,
  contentWidth: number,
  sizes: ResolvedTextSizes,
  stack: string
): MeasuredFooter {
  const suite = (footer.suite ?? '').trim();
  const computedText = footer.computedAt ? `Computed ${footer.computedAt}` : '';
  if (suite === '' && computedText === '') {
    return { labelText: '', suiteText: '', computedText: '', twoLines: false, height: 0 };
  }
  const labelText = suite === '' ? '' : FOOTER_LABEL_TEXT;
  const lineHeight = sizes.footerLineHeight;
  if (!context) {
    return { labelText, suiteText: suite, computedText, twoLines: false, height: lineHeight };
  }

  const labelWidth = labelText === '' ? 0 : letterSpacedWidth(context, labelText, sizes.footerLabelPx, stack);
  context.font = fontOf(sizes.footerPx, '400', stack);
  const suiteWidth = suite === '' ? 0 : context.measureText(suite).width;
  const computedWidth = computedText === '' ? 0 : context.measureText(computedText).width;
  const gapAfterLabel = labelText !== '' && suite !== '' ? FOOTER_LABEL_GAP : 0;
  const leftWidth = labelWidth + gapAfterLabel + suiteWidth;
  const fitsOneLine = computedText === '' || leftWidth + FOOTER_MIN_GAP + computedWidth <= contentWidth;

  return {
    labelText,
    suiteText: suite,
    computedText,
    twoLines: !fitsOneLine,
    height: fitsOneLine ? lineHeight : lineHeight * 2
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

function fontOf(size: number, weight: string, stack: string): string {
  return `${weight} ${size}px ${stack}`;
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
  color: string,
  stack: string
): number {
  if (lines.length === 0) {
    return y;
  }
  const lineHeight = Math.round(size * 1.4);
  context.font = fontOf(size, weight, stack);
  context.fillStyle = color;
  let cursor = y;
  for (const line of lines) {
    context.fillText(line, x, cursor);
    cursor += lineHeight;
  }
  return cursor;
}

/** Draws every badge row, wrapping tones and borders per pill. Returns the y past the last row. */
function drawBadgeRows(
  context: CanvasRenderingContext2D,
  rows: readonly MeasuredBadgeRow[],
  x: number,
  y: number,
  sizes: ResolvedTextSizes,
  paint: ChromePaint
): number {
  let cursorY = y;
  for (const row of rows) {
    let cursorX = x;
    for (const { badge, width } of row.badges) {
      drawBadge(context, badge, cursorX, cursorY, width, sizes, paint);
      cursorX += width + BADGE_GAP;
    }
    cursorY += sizes.badgeHeight + BADGE_GAP;
  }
  return cursorY - BADGE_GAP;
}

/** One badge pill: a rounded, bordered rect in the tone's colors, with its text inset. */
function drawBadge(
  context: CanvasRenderingContext2D,
  badge: FigureBadge,
  x: number,
  y: number,
  width: number,
  sizes: ResolvedTextSizes,
  paint: ChromePaint
): void {
  const tone = paint.colors.badge[badge.tone];
  pathRoundedRect(context, x, y, width, sizes.badgeHeight, BADGE_RADIUS);
  context.fillStyle = tone.fill;
  context.fill();
  context.strokeStyle = tone.border;
  context.lineWidth = BADGE_BORDER_WIDTH;
  context.stroke();
  context.font = fontOf(sizes.badgePx, '400', paint.stack);
  context.fillStyle = tone.text;
  context.fillText(badge.text, x + BADGE_PAD_X, y + BADGE_PAD_Y);
}

/** The Better badge: a full-radius gold pill holding the arrow, turned toward the better side, and its word. */
function drawDirectionBadge(
  context: CanvasRenderingContext2D,
  direction: MeasuredDirection,
  x: number,
  y: number,
  sizes: ResolvedTextSizes,
  paint: ChromePaint
): void {
  const colors = paint.colors.direction;
  const height = sizes.badgeHeight;
  pathRoundedRect(context, x, y, direction.width, height, height / 2);
  context.fillStyle = colors.fill;
  context.fill();
  context.strokeStyle = colors.border;
  context.lineWidth = BADGE_BORDER_WIDTH;
  context.stroke();

  const arrowSize = directionArrowSize(sizes);
  drawDirectionArrow(
    context, x + DIRECTION_PAD_START + arrowSize / 2, y + height / 2, arrowSize, direction.rotation, colors.ink);

  context.font = fontOf(sizes.badgePx, '700', paint.stack);
  context.fillStyle = colors.ink;
  context.fillText(direction.label, x + DIRECTION_PAD_START + arrowSize + DIRECTION_ARROW_GAP, y + BADGE_PAD_Y);
}

/** The up-right arrow in its own 24-unit box, centred on `(centerX, centerY)` and rotated clockwise by `rotation` degrees. */
function drawDirectionArrow(
  context: CanvasRenderingContext2D,
  centerX: number,
  centerY: number,
  size: number,
  rotation: number,
  color: string
): void {
  context.save();
  context.translate(centerX, centerY);
  context.rotate((rotation * Math.PI) / 180);
  context.scale(size / 24, size / 24);
  context.translate(-12, -12);
  context.beginPath();
  context.moveTo(7, 17);
  context.lineTo(17, 7);
  context.moveTo(8, 7);
  context.lineTo(17, 7);
  context.lineTo(17, 16);
  context.strokeStyle = color;
  // The context is scaled, so the stroke is stated in box units.
  context.lineWidth = (Math.max(DIRECTION_ARROW_MIN_STROKE, size / 7) * 24) / size;
  context.lineCap = 'round';
  context.lineJoin = 'round';
  context.stroke();
  context.restore();
}

/** A rectangular path, rounded where `roundRect` is available and square otherwise. */
function pathRoundedRect(
  context: CanvasRenderingContext2D,
  x: number,
  y: number,
  width: number,
  height: number,
  radius: number
): void {
  context.beginPath();
  if (typeof context.roundRect === 'function') {
    context.roundRect(x, y, width, height, radius);
  } else {
    context.rect(x, y, width, height);
  }
}

/** Draws every key row: each item's glyph, then its text. Returns the y past the last row. */
function drawKeyRows(
  context: CanvasRenderingContext2D,
  rows: readonly MeasuredKeyRow[],
  x: number,
  y: number,
  paint: ChromePaint
): number {
  let cursorY = y;
  for (const row of rows) {
    let cursorX = x;
    for (const { item, width } of row.items) {
      drawKeyGlyph(context, item.glyph, cursorX, cursorY, KEY_GLYPH_SIZE, paint.colors);
      context.font = fontOf(KEY_TEXT_SIZE, '400', paint.stack);
      context.fillStyle = paint.colors.body;
      context.fillText(item.text, cursorX + KEY_GLYPH_SIZE + KEY_GLYPH_TEXT_GAP, cursorY);
      cursorX += width + KEY_ITEM_GAP;
    }
    cursorY += KEY_ROW_HEIGHT + LINE_GAP;
  }
  return cursorY - LINE_GAP;
}

/** One key glyph in its `size` × `size` slot: a hollow or solid circle, a frontier, a dominated square, or a whisker. */
function drawKeyGlyph(
  context: CanvasRenderingContext2D,
  glyph: FigureKeyGlyph,
  x: number,
  y: number,
  size: number,
  colors: ResolvedChromeColors
): void {
  const mid = y + size / 2;
  const ink = colors.keyInk;
  switch (glyph) {
    case 'hollow':
      context.strokeStyle = ink;
      context.lineWidth = 1;
      context.beginPath();
      context.arc(x + size / 2, mid, size / 2 - 0.5, 0, Math.PI * 2);
      context.stroke();
      break;
    case 'solid':
      context.fillStyle = ink;
      context.beginPath();
      context.arc(x + size / 2, mid, size / 2, 0, Math.PI * 2);
      context.fill();
      break;
    case 'frontier': {
      const overhang = (KEY_FRONTIER_LENGTH - size) / 2;
      context.strokeStyle = ink;
      context.lineWidth = 1;
      context.beginPath();
      context.moveTo(x - overhang, mid);
      context.lineTo(x + size + overhang, mid);
      context.stroke();
      break;
    }
    case 'dominated': {
      const side = KEY_DOMINATED_SIDE;
      const top = mid - side / 2;
      const left = x + (size - side) / 2;
      context.fillStyle = colors.dominatedKeyFill;
      context.fillRect(left, top, side, side);
      context.strokeStyle = ink;
      context.lineWidth = 1;
      context.strokeRect(left + 0.5, top + 0.5, side - 1, side - 1);
      break;
    }
    case 'interval': {
      const capHalf = size / 3;
      const centerX = x + size / 2;
      context.strokeStyle = ink;
      context.lineWidth = 1;
      context.beginPath();
      context.moveTo(centerX, y);
      context.lineTo(centerX, y + size);
      context.moveTo(centerX - capHalf, y);
      context.lineTo(centerX + capHalf, y);
      context.moveTo(centerX - capHalf, y + size);
      context.lineTo(centerX + capHalf, y + size);
      context.stroke();
      break;
    }
  }
}

/** One note: a left rule in the tone's color, and its wrapped text indented past it. */
function drawNote(context: CanvasRenderingContext2D, note: MeasuredNote, x: number, y: number, paint: ChromePaint): number {
  const tone = paint.colors.note[note.tone];
  const height = blockHeight(note.lines, NOTE_SIZE);
  context.fillStyle = tone.rule;
  context.fillRect(x, y, NOTE_RULE_WIDTH, height);
  drawBlock(context, note.lines, x + NOTE_INDENT, y, NOTE_SIZE, '400', tone.text, paint.stack);
  return y + height;
}

/**
 * The footer's one or two lines: the `SUITE` label and the suite name on the left, and
 * `Computed …` right-aligned when it fits beside them or left-aligned on its own line otherwise.
 */
function drawFooter(
  context: CanvasRenderingContext2D,
  footer: MeasuredFooter,
  x: number,
  y: number,
  contentWidth: number,
  sizes: ResolvedTextSizes,
  paint: ChromePaint
): number {
  if (footer.height === 0) {
    return y;
  }
  const lineHeight = sizes.footerLineHeight;
  let cursorX = x;
  if (footer.labelText !== '') {
    context.fillStyle = paint.colors.muted;
    cursorX += drawLetterSpacedText(context, footer.labelText, cursorX, y, sizes.footerLabelPx, paint.stack)
      + FOOTER_LABEL_GAP;
  }
  if (footer.suiteText !== '') {
    context.font = fontOf(sizes.footerPx, '400', paint.stack);
    context.fillStyle = paint.colors.body;
    context.fillText(footer.suiteText, cursorX, y);
  }
  if (footer.computedText !== '') {
    context.font = fontOf(sizes.footerPx, '400', paint.stack);
    context.fillStyle = paint.colors.muted;
    const computedWidth = context.measureText(footer.computedText).width;
    const computedY = footer.twoLines ? y + lineHeight : y;
    const computedX = footer.twoLines ? x : x + contentWidth - computedWidth;
    context.fillText(footer.computedText, computedX, computedY);
  }
  return y + footer.height;
}

/** Whether this context implements the `letterSpacing` property, rather than silently ignoring it. */
function supportsLetterSpacing(context: CanvasRenderingContext2D): boolean {
  return typeof context.letterSpacing === 'string';
}

/** The width of `text` set at `${FOOTER_LABEL_LETTER_SPACING}px` letter-spacing, at `size`. */
function letterSpacedWidth(context: CanvasRenderingContext2D, text: string, size: number, stack: string): number {
  context.font = fontOf(size, '400', stack);
  if (supportsLetterSpacing(context)) {
    context.letterSpacing = `${FOOTER_LABEL_LETTER_SPACING}px`;
    const width = context.measureText(text).width;
    context.letterSpacing = '0px';
    return width;
  }
  if (text.length === 0) {
    return 0;
  }
  let width = 0;
  for (const char of text) {
    width += context.measureText(char).width + FOOTER_LABEL_LETTER_SPACING;
  }
  return width - FOOTER_LABEL_LETTER_SPACING;
}

/** Draws `text` letter-spaced at `(x, y)`, by the same method {@link letterSpacedWidth} measures with. */
function drawLetterSpacedText(
  context: CanvasRenderingContext2D,
  text: string,
  x: number,
  y: number,
  size: number,
  stack: string
): number {
  context.font = fontOf(size, '400', stack);
  if (supportsLetterSpacing(context)) {
    context.letterSpacing = `${FOOTER_LABEL_LETTER_SPACING}px`;
    context.fillText(text, x, y);
    const width = context.measureText(text).width;
    context.letterSpacing = '0px';
    return width;
  }
  if (text.length === 0) {
    return 0;
  }
  let cursor = x;
  for (const char of text) {
    context.fillText(char, cursor, y);
    cursor += context.measureText(char).width + FOOTER_LABEL_LETTER_SPACING;
  }
  return cursor - x - FOOTER_LABEL_LETTER_SPACING;
}

/**
 * Greedy word wrap. A single word wider than the column is left to overflow rather than broken.
 *
 * Exported for the table composer, which wraps header and cell text into the same typography.
 * `stack` is the font stack measured in, the *Overseer default* stack when absent.
 */
export function wrapText(
  context: CanvasRenderingContext2D,
  text: string,
  maxWidth: number,
  size: number,
  weight: string,
  stack: string = FIGURE_FONT_STACK
): string[] {
  const trimmed = (text ?? '').trim();
  if (trimmed === '') {
    return [];
  }
  context.font = fontOf(size, weight, stack);
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
