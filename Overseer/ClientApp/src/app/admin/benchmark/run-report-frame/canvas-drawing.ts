/**
 * Canvas drawing helpers shared by the composed report images: the run report's key figures
 * (`key-figures-image.ts`) and the Chat Consistency Results images (`chat-consistency-tab/results-image/`).
 *
 * No Angular. Every helper draws in layout pixels on a context whose transform the caller has set, with
 * `textBaseline = 'top'`. The colors default to the key-figures images' dark theme; a caller with its own
 * palette passes its colors.
 */

import { FIGURE_FONT_STACK, FIGURE_MUTED_COLOR, FIGURE_RULE_COLOR } from '../model-comparison/figure-export';

/** The card fill of the dark theme. */
export const CANVAS_CARD_FILL = '#161616';

/** The card border of the dark theme. */
export const CANVAS_CARD_BORDER = '#333';

/** A decoded image and its natural size. */
export interface CanvasLogo {
  readonly image: CanvasImageSource;
  readonly width: number;
  readonly height: number;
}

/** The canvas font of the figure font stack at `size` px and `weight`. */
export function canvasFont(size: number, weight: string): string {
  return `${weight} ${size}px ${FIGURE_FONT_STACK}`;
}

/** The line box of a font size: 1.4 times it, rounded. */
export function canvasLineHeight(size: number): number {
  return Math.round(size * 1.4);
}

/** Draws `lines` from `y`, one line box each, and returns the y after the last. */
export function drawLines(
  context: CanvasRenderingContext2D,
  lines: readonly string[],
  x: number,
  y: number,
  size: number,
  weight: string,
  color: string
): number {
  context.font = canvasFont(size, weight);
  context.fillStyle = color;
  const height = canvasLineHeight(size);
  for (const line of lines) {
    // Centered in its line box, as the dialog's line-height centers it.
    context.fillText(line, x, y + (height - size) / 2);
    y += height;
  }
  return y;
}

/** Starts a path of a rectangle with rounded corners, the radius capped at half of either side. */
export function pathRoundedRect(context: CanvasRenderingContext2D, x: number, y: number, width: number, height: number, radius: number): void {
  const r = Math.max(0, Math.min(radius, width / 2, height / 2));
  context.beginPath();
  context.moveTo(x + r, y);
  context.lineTo(x + width - r, y);
  context.arcTo(x + width, y, x + width, y + r, r);
  context.lineTo(x + width, y + height - r);
  context.arcTo(x + width, y + height, x + width - r, y + height, r);
  context.lineTo(x + r, y + height);
  context.arcTo(x, y + height, x, y + height - r, r);
  context.lineTo(x, y + r);
  context.arcTo(x, y, x + r, y, r);
  context.closePath();
}

/** A filled card with a 1 px border, on the pixel grid. */
export function drawCardBox(
  context: CanvasRenderingContext2D,
  x: number,
  y: number,
  width: number,
  height: number,
  radius: number,
  fill: string = CANVAS_CARD_FILL,
  border: string = CANVAS_CARD_BORDER
): void {
  pathRoundedRect(context, x + 0.5, y + 0.5, width - 1, height - 1, radius);
  context.fillStyle = fill;
  context.fill();
  context.strokeStyle = border;
  context.lineWidth = 1;
  context.stroke();
}

/** Draws a logo `height` tall at its own aspect ratio. */
export function drawLogo(context: CanvasRenderingContext2D, logo: CanvasLogo, x: number, y: number, height: number): void {
  const width = logo.width * height / logo.height;
  context.drawImage(logo.image, x, y, width, height);
}

/** A rule `ruleGap / 2` below `y`, then the footer lines from `y + ruleGap`; nothing without lines. */
export function drawFooter(
  context: CanvasRenderingContext2D,
  lines: readonly string[],
  x: number,
  y: number,
  width: number,
  ruleGap: number,
  size: number,
  ruleColor: string = FIGURE_RULE_COLOR,
  color: string = FIGURE_MUTED_COLOR
): void {
  if (lines.length === 0) {
    return;
  }
  context.strokeStyle = ruleColor;
  context.lineWidth = 1;
  context.beginPath();
  const ruleY = Math.round(y + ruleGap / 2) + 0.5;
  context.moveTo(x, ruleY);
  context.lineTo(x + width, ruleY);
  context.stroke();
  drawLines(context, lines, x, y + ruleGap, size, '400', color);
}
