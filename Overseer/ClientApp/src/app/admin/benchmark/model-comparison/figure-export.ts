/**
 * Composes one comparison figure into a presentation-quality image.
 *
 * Pure functions over a canvas: no Angular, no component state, no DOM beyond the canvases
 * themselves, so it unit-tests without a fixture and the view keeps only the wiring.
 *
 * Two things here are load-bearing rather than decorative:
 *
 * 1. **The caveats are composited into the image.** The whole reason the comparison view refuses to
 *    hide what it could not compare is that a chart is more persuasive than a table. A figure
 *    exported as a bare canvas and pasted into a document would drop exactly the notices that stop
 *    it being misread, so the title, subtitle, caption, every notice and the footer are drawn into
 *    the same bitmap as the plot.
 * 2. **The background is opaque.** Chart.js canvases are transparent; a PNG of one dropped into a
 *    light document renders as dark-on-dark and is unreadable.
 */

export type FigureExportFormat = 'png' | 'webp';

/** One figure, with every piece of chrome the exported image must carry. */
export interface FigureExportRequest {
  /** The live chart canvas. Read, never mutated. */
  readonly canvas: HTMLCanvasElement;
  readonly title: string;
  readonly subtitle: string;
  readonly caption: string;
  /** Degraded axes, saturation warnings, cap notices — every caveat the card renders. */
  readonly notices: readonly string[];
  /** Suite, pricing basis, entry count and the time the comparison was computed. */
  readonly footer: string;
  readonly format: FigureExportFormat;
}

/** What `encodeFigureImage` produced, including the format actually written. */
export interface FigureExportResult {
  readonly blob: Blob;
  /** The requested format, or `'png'` where the browser could not encode WebP. */
  readonly format: FigureExportFormat;
  /** True when a WebP request was silently answered with a PNG. */
  readonly fellBackToPng: boolean;
}

/** The composed image's device-pixel density. Reading the on-screen canvas would export it blurred. */
export const FIGURE_EXPORT_SCALE = 2;

/** WebP quality, fixed at this repository's image convention of 85. */
export const FIGURE_EXPORT_WEBP_QUALITY = 0.85;

/** Layout constants, in CSS pixels before the 2x scale is applied. */
const PADDING = 20;
const TITLE_SIZE = 18;
const SUBTITLE_SIZE = 13;
const BODY_SIZE = 12;
const LINE_GAP = 6;
const RULE_GAP = 12;

/** The card ground the comparison view draws on, so an exported figure matches what was on screen. */
const BACKGROUND = '#181818';
const TITLE_COLOR = '#e0ba6d';
const BODY_COLOR = '#d4d4d8';
const MUTED_COLOR = '#a1a1aa';
const RULE_COLOR = '#2a2a2a';

const FONT_STACK = '"Segoe UI", "Helvetica Neue", Arial, sans-serif';

/**
 * Draws the chart and all of its chrome onto a new offscreen canvas at {@link FIGURE_EXPORT_SCALE}.
 *
 * The source canvas is drawn at its own CSS size and scaled up by the context transform rather than
 * copied pixel for pixel, so a chart re-rendered at 2x device pixel ratio lands sharp; the caller is
 * responsible for having asked Chart.js for that density first.
 */
export function composeFigureImage(request: FigureExportRequest): HTMLCanvasElement {
  const chartWidth = cssWidthOf(request.canvas);
  const chartHeight = cssHeightOf(request.canvas);
  const contentWidth = Math.max(chartWidth, 360);

  const measure = document.createElement('canvas').getContext('2d');
  const wrap = (text: string, size: number, weight: string): string[] =>
    measure ? wrapText(measure, text, contentWidth, size, weight) : (text ? [text] : []);

  const titleLines = wrap(request.title, TITLE_SIZE, '600');
  const subtitleLines = wrap(request.subtitle, SUBTITLE_SIZE, '400');
  const captionLines = wrap(request.caption, BODY_SIZE, '400');
  const noticeLines = request.notices.map(notice => wrap(notice, BODY_SIZE, '400'));
  const footerLines = wrap(request.footer, BODY_SIZE, '400');

  let height = PADDING;
  height += blockHeight(titleLines, TITLE_SIZE);
  height += blockHeight(subtitleLines, SUBTITLE_SIZE);
  if (titleLines.length > 0 || subtitleLines.length > 0) {
    height += LINE_GAP;
  }
  height += chartHeight;
  if (captionLines.length > 0) {
    height += LINE_GAP + blockHeight(captionLines, BODY_SIZE);
  }
  for (const lines of noticeLines) {
    height += LINE_GAP + blockHeight(lines, BODY_SIZE);
  }
  if (footerLines.length > 0) {
    height += RULE_GAP + blockHeight(footerLines, BODY_SIZE);
  }
  height += PADDING;

  const width = contentWidth + PADDING * 2;
  const target = document.createElement('canvas');
  target.width = Math.round(width * FIGURE_EXPORT_SCALE);
  target.height = Math.round(height * FIGURE_EXPORT_SCALE);

  const context = target.getContext('2d');
  if (!context) {
    return target;
  }
  context.scale(FIGURE_EXPORT_SCALE, FIGURE_EXPORT_SCALE);

  // Opaque, and painted before anything else: a transparent PNG of a Chart.js canvas is
  // dark-on-dark in any light document it is pasted into.
  context.fillStyle = BACKGROUND;
  context.fillRect(0, 0, width, height);

  context.textBaseline = 'top';
  let y = PADDING;

  y = drawBlock(context, titleLines, PADDING, y, TITLE_SIZE, '600', TITLE_COLOR);
  y = drawBlock(context, subtitleLines, PADDING, y, SUBTITLE_SIZE, '400', MUTED_COLOR);
  if (titleLines.length > 0 || subtitleLines.length > 0) {
    y += LINE_GAP;
  }

  if (chartWidth > 0 && chartHeight > 0) {
    context.drawImage(request.canvas, PADDING, y, chartWidth, chartHeight);
  }
  y += chartHeight;

  if (captionLines.length > 0) {
    y += LINE_GAP;
    y = drawBlock(context, captionLines, PADDING, y, BODY_SIZE, '400', BODY_COLOR);
  }
  for (const lines of noticeLines) {
    y += LINE_GAP;
    y = drawBlock(context, lines, PADDING, y, BODY_SIZE, '400', MUTED_COLOR);
  }

  if (footerLines.length > 0) {
    y += RULE_GAP / 2;
    context.strokeStyle = RULE_COLOR;
    context.lineWidth = 1;
    context.beginPath();
    context.moveTo(PADDING, Math.round(y) + 0.5);
    context.lineTo(width - PADDING, Math.round(y) + 0.5);
    context.stroke();
    y += RULE_GAP / 2;
    drawBlock(context, footerLines, PADDING, y, BODY_SIZE, '400', MUTED_COLOR);
  }

  return target;
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

/**
 * The canvas' CSS width, falling back to its backing-store width.
 *
 * A Chart.js canvas resized for a 2x export has a backing store twice its layout box, so reading
 * `canvas.width` would lay the composition out at double size and draw the plot into a quarter of
 * it. `style.width` is what Chart.js sets and is therefore the size the plot was laid out at.
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
