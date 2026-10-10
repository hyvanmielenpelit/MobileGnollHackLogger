/**
 * The Results images' draw pass: paints a laid-out section ({@link CcResultsImageLayout}) on a canvas
 * in the chosen palette. Status icons are Feather path data drawn with `Path2D`; the interval bar is the
 * margin band, the zero line, the 95 % interval and the estimate dot at `ccIntervalGeometry` positions.
 *
 * Every position comes from the layout; this module only resolves colors and strokes shapes.
 */

import {
  CanvasLogo,
  canvasFont,
  canvasLineHeight,
  drawLines,
  drawLogo,
  pathRoundedRect
} from '../../run-report-frame/canvas-drawing';
import { FactLayout, factBadgeHeight } from '../../run-report-frame/key-figures-image';
import { CcIntervalGeometry } from '../chat-consistency-results';
import { CcImageIcon } from './results-image-blocks';
import {
  CC_IMAGE_ICON_GAP,
  CC_IMAGE_PILL_PAD_X,
  CC_IMAGE_PILL_PAD_Y,
  CcImageInk,
  CcImageOp,
  CcImageSurface,
  CcResultsImageFrame,
  CcResultsImageLayout
} from './results-image-layout';
import { CcImageSwatch, CcResultsImagePalette } from './results-image-palette';

/** Feather icon path data, in a 24 × 24 box, stroked 2 units wide with round caps and joins. */
export const CC_IMAGE_ICON_PATHS: Readonly<Record<CcImageIcon, readonly string[]>> = {
  check: ['M20 6 9 17 4 12'],
  alert: ['M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z', 'M12 9v4', 'M12 17h.01'],
  minus: ['M5 12h14'],
  info: ['M22 12a10 10 0 1 1-20 0a10 10 0 1 1 20 0z', 'M12 16v-4', 'M12 8h.01'],
  x: ['M18 6 6 18', 'M6 6l12 12']
};

/** The fact rows' badge geometry, as `layoutFactRows` lays the badges out. */
const FACT_BADGE_PAD_X = 6;
const FACT_BADGE_BORDER = 1;
const FACT_BADGE_RADIUS = 4;
const FACT_BADGE_WEIGHT = '700';
const FACT_LABEL_WEIGHT = '600';

/** A text color of the palette. */
export function ccImageInkColor(palette: CcResultsImagePalette, ink: CcImageInk): string {
  switch (ink) {
    case 'title':
      return palette.title;
    case 'heading':
      return palette.heading;
    case 'body':
      return palette.body;
    case 'muted':
      return palette.muted;
    case 'warning':
      return palette.warning.ink;
    default:
      return palette.tones[ink].ink;
  }
}

/** A box's fill and border in the palette. */
export function ccImageSurface(palette: CcResultsImagePalette, surface: CcImageSurface): CcImageSwatch {
  switch (surface) {
    case 'card':
      return { ink: palette.body, fill: palette.cardFill, border: palette.cardBorder };
    case 'inset':
      return { ink: palette.body, fill: palette.inset, border: 'transparent' };
    case 'warning':
      return palette.warning;
    case 'band':
      return palette.band;
    default:
      return palette.tones[surface];
  }
}

/** Strokes a Feather icon `size` px square with its top left corner at `x`, `y`. */
export function drawImageIcon(context: CanvasRenderingContext2D, icon: CcImageIcon, x: number, y: number, size: number, color: string): void {
  context.save();
  context.translate(x, y);
  context.scale(size / 24, size / 24);
  context.strokeStyle = color;
  context.lineWidth = 2;
  context.lineCap = 'round';
  context.lineJoin = 'round';
  for (const data of CC_IMAGE_ICON_PATHS[icon]) {
    context.stroke(new Path2D(data));
  }
  context.restore();
}

function drawBox(context: CanvasRenderingContext2D, op: Extract<CcImageOp, { kind: 'box' }>, palette: CcResultsImagePalette): void {
  const swatch = ccImageSurface(palette, op.surface);
  context.save();
  pathRoundedRect(context, op.x + 0.5, op.y + 0.5, op.width - 1, op.height - 1, op.radius);
  context.fillStyle = swatch.fill;
  context.fill();
  if (op.rail) {
    context.save();
    context.clip();
    context.fillStyle = ccImageInkColor(palette, op.rail.ink);
    context.fillRect(op.x, op.y, op.rail.width, op.height);
    context.restore();
  }
  if (!op.borderless) {
    context.setLineDash(op.dashed ? [5, 4] : []);
    context.strokeStyle = swatch.border;
    context.lineWidth = 1;
    context.stroke();
  }
  context.restore();
}

function drawPill(context: CanvasRenderingContext2D, op: Extract<CcImageOp, { kind: 'pill' }>, palette: CcResultsImagePalette): void {
  const swatch = palette.tones[op.tone];
  context.save();
  pathRoundedRect(context, op.x + 0.5, op.y + 0.5, op.width - 1, op.height - 1, op.height / 2);
  context.fillStyle = swatch.fill;
  context.fill();
  context.setLineDash(op.dashed ? [4, 3] : []);
  context.strokeStyle = swatch.border;
  context.lineWidth = 1;
  context.stroke();
  // A pill narrower than its text clips it.
  context.clip();
  let textX = op.x + CC_IMAGE_PILL_PAD_X;
  if (op.icon) {
    drawImageIcon(context, op.icon, textX, op.y + (op.height - op.size) / 2, op.size, swatch.ink);
    textX += op.size + CC_IMAGE_ICON_GAP - 2;
  }
  drawLines(context, [op.text], textX, op.y + CC_IMAGE_PILL_PAD_Y, op.size, op.weight, swatch.ink);
  context.restore();
}

/** The margin band, the zero line, the interval and the estimate dot, across `width`. */
function drawInterval(
  context: CanvasRenderingContext2D,
  x: number,
  y: number,
  width: number,
  height: number,
  geometry: CcIntervalGeometry,
  palette: CcResultsImagePalette
): void {
  const at = (percent: number): number => x + width * percent / 100;
  context.save();

  const bandStart = at(geometry.marginStart);
  pathRoundedRect(context, bandStart + 0.5, y + 0.5, Math.max(1, at(geometry.marginEnd) - bandStart - 1), height - 1, 3);
  context.fillStyle = palette.band.fill;
  context.fill();
  context.strokeStyle = palette.band.border;
  context.lineWidth = 1;
  context.stroke();

  const zero = Math.round(at(geometry.zero)) + 0.5;
  context.strokeStyle = palette.zero;
  context.beginPath();
  context.moveTo(zero, y - 2);
  context.lineTo(zero, y + height + 2);
  context.stroke();

  const lower = at(geometry.lower);
  pathRoundedRect(context, lower, y + height / 2 - 2, Math.max(4, at(geometry.upper) - lower), 4, 2);
  context.fillStyle = palette.accent;
  context.fill();

  context.beginPath();
  context.arc(at(geometry.estimate), y + height / 2, 6, 0, Math.PI * 2);
  context.fillStyle = palette.accent;
  context.fill();
  context.strokeStyle = palette.dotRing;
  context.lineWidth = 2;
  context.stroke();

  context.restore();
}

/** Fact rows laid out by `layoutFactRows`, in the palette's colors. */
function drawFactLayout(context: CanvasRenderingContext2D, layout: FactLayout, x: number, y: number, palette: CcResultsImagePalette): void {
  const { labelPx, textPx } = layout.sizes;
  const badgePx = textPx - 2;
  const badgeHeight = factBadgeHeight(layout.sizes);
  const labelLineHeight = canvasLineHeight(labelPx);
  for (const row of layout.rows) {
    const rowTop = y + row.y;
    const firstLine = layout.stacked ? labelLineHeight : (row.lineHeights[0] ?? labelLineHeight);
    context.font = canvasFont(labelPx, FACT_LABEL_WEIGHT);
    context.fillStyle = palette.muted;
    context.fillText(row.label, x, rowTop + (firstLine - labelPx) / 2);

    let lineTop = rowTop + (layout.stacked ? labelLineHeight : 0);
    row.lines.forEach((line, index) => {
      const height = row.lineHeights[index];
      for (const item of line) {
        const left = x + layout.valueX + item.x;
        if (item.run.kind === 'badge') {
          const swatch = palette.badges[item.run.tone];
          const top = lineTop + (height - badgeHeight) / 2;
          pathRoundedRect(context, left + 0.5, top + 0.5, item.width - 1, badgeHeight - 1, FACT_BADGE_RADIUS);
          context.fillStyle = swatch.fill;
          context.fill();
          context.strokeStyle = swatch.border;
          context.lineWidth = 1;
          context.stroke();
          context.font = canvasFont(badgePx, FACT_BADGE_WEIGHT);
          context.fillStyle = swatch.ink;
          context.fillText(item.text, left + FACT_BADGE_PAD_X + FACT_BADGE_BORDER, top + (badgeHeight - badgePx) / 2);
        } else {
          const run = item.run;
          context.font = canvasFont(textPx, run.strong ? '600' : '400');
          context.fillStyle = run.warning ? palette.warning.ink : run.muted ? palette.muted : run.strong ? palette.heading : palette.body;
          context.fillText(item.text, left, lineTop + (height - textPx) / 2);
        }
      }
      lineTop += height;
    });
  }
}

/** Draws every operation of `layout` on a context already scaled to layout pixels. */
export function drawResultsImage(
  context: CanvasRenderingContext2D,
  layout: CcResultsImageLayout,
  palette: CcResultsImagePalette,
  logo: CanvasLogo | null
): void {
  context.textBaseline = 'top';
  context.fillStyle = palette.background;
  context.fillRect(0, 0, layout.width, layout.height);
  for (const op of layout.ops) {
    switch (op.kind) {
      case 'text':
        drawLines(context, op.lines, op.x, op.y, op.size, op.weight, ccImageInkColor(palette, op.ink));
        break;
      case 'box':
        drawBox(context, op, palette);
        break;
      case 'pill':
        drawPill(context, op, palette);
        break;
      case 'icon':
        drawImageIcon(context, op.icon, op.x, op.y, op.size, ccImageInkColor(palette, op.ink));
        break;
      case 'interval':
        drawInterval(context, op.x, op.y, op.width, op.height, op.geometry, palette);
        break;
      case 'rule':
        context.strokeStyle = palette.rule;
        context.lineWidth = 1;
        context.setLineDash([]);
        context.beginPath();
        context.moveTo(op.x, Math.round(op.y) + 0.5);
        context.lineTo(op.x + op.width, Math.round(op.y) + 0.5);
        context.stroke();
        break;
      case 'factRows':
        drawFactLayout(context, op.layout, op.x, op.y, palette);
        break;
      case 'logo':
        if (logo) {
          drawLogo(context, logo, op.x, op.y, op.height);
        }
        break;
    }
  }
}

/** A canvas of the frame's pixel size with `layout` drawn on it. */
export function paintResultsImage(
  layout: CcResultsImageLayout,
  frame: CcResultsImageFrame,
  palette: CcResultsImagePalette,
  logo: CanvasLogo | null
): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  canvas.width = frame.pixelWidth;
  canvas.height = frame.pixelHeight;
  const context = canvas.getContext('2d');
  if (context) {
    context.scale(frame.scale, frame.scale);
    drawResultsImage(context, layout, palette, logo);
  }
  return canvas;
}
