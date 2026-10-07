/**
 * Zoom arithmetic for the Timeline step's live charts.
 *
 * A zoom here scales the chart's box, not a bitmap: at 1 (100 %) the on-screen chart box is the
 * download's layout box for the chosen chart size, so the page and the file agree there; zooming in
 * gives the chart more room at the same text size. Free of Angular and the DOM.
 */

import { layoutBoxFor } from '../../model-comparison/figure-export';
import { FigureSizeSettings, resolveSizeResolution } from '../../model-comparison/figure-size';
import { PreviewZoomRange, clampPreviewZoom } from '../../model-comparison/preview-view';

/** The chart box at 100 % zoom, in CSS px: the download's layout box for the chosen size. */
export interface CcChartBox {
  readonly width: number;
  readonly height: number;
}

/**
 * The layout box of the size's resolution at its text size (`layoutBoxFor`); the density is ignored,
 * because it changes only the file's pixels.
 */
export function ccChartBox(size: FigureSizeSettings): CcChartBox {
  const resolution = resolveSizeResolution(size);
  const box = layoutBoxFor(resolution.widthPx, resolution.heightPx, size.textScalePercent / 100);
  return { width: box.layoutWidth, height: box.layoutHeight };
}

/** The lowest zoom the controls reach, unless a fit itself is lower. */
export const CC_ZOOM_FLOOR = 0.25;
export const CC_ZOOM_MAX = 4;

/** The most device pixels one on-screen chart canvas holds. */
export const CC_MAX_CANVAS_PIXELS = 8_000_000;

/** What the reader asked the view to show: a fit, or an explicit zoom. */
export type CcZoomView = 'fitWidth' | 'fitHeight' | 'fitScreen' | number;

function isUsableZoom(zoom: number): boolean {
  return Number.isFinite(zoom) && zoom > 0;
}

/** `{ min: min(CC_ZOOM_FLOOR, fit), max: CC_ZOOM_MAX }`: the floor follows the fit below 25 %, as `previewZoomRange` does. */
export function ccZoomRange(fit: number): PreviewZoomRange {
  const usable = isUsableZoom(fit) ? fit : CC_ZOOM_FLOOR;
  return { min: Math.min(CC_ZOOM_FLOOR, usable), max: CC_ZOOM_MAX };
}

/** A fit ratio, at most `CC_ZOOM_MAX`; `CC_ZOOM_FLOOR` where there is no room to fit into. */
function fitRatio(available: number, extent: number): number {
  const ratio = available / extent;
  return isUsableZoom(ratio) ? Math.min(CC_ZOOM_MAX, ratio) : CC_ZOOM_FLOOR;
}

/** The zoom at which the chart box fills `contentWidth`, the CSS px a chart box may take across. */
export function ccFitWidthZoom(box: CcChartBox, contentWidth: number): number {
  return fitRatio(contentWidth, box.width);
}

/**
 * The zoom at which one chart, with its figure's caption, marker line and *Show data* summary
 * (`chromeHeight`), fills `contentHeight`.
 */
export function ccFitHeightZoom(box: CcChartBox, contentHeight: number, chromeHeight: number): number {
  const chrome = Number.isFinite(chromeHeight) && chromeHeight > 0 ? chromeHeight : 0;
  return fitRatio(contentHeight - chrome, box.height);
}

/** The larger zoom at which the whole chart, chrome included, fits both ways. */
export function ccFitScreenZoom(
  box: CcChartBox,
  contentWidth: number,
  contentHeight: number,
  chromeHeight: number
): number {
  return Math.min(ccFitWidthZoom(box, contentWidth), ccFitHeightZoom(box, contentHeight, chromeHeight));
}

/**
 * The zoom a view stands for, clamped into `ccZoomRange` of the lowest usable fit, so a fit below
 * 25 % is still reachable.
 */
export function ccResolveZoom(view: CcZoomView, fits: { width: number; height: number; screen: number }): number {
  const usable = [fits.width, fits.height, fits.screen].filter(isUsableZoom);
  const range = ccZoomRange(usable.length > 0 ? Math.min(...usable) : CC_ZOOM_FLOOR);
  const zoom = view === 'fitWidth'
    ? fits.width
    : view === 'fitHeight'
      ? fits.height
      : view === 'fitScreen'
        ? fits.screen
        : view;
  return clampPreviewZoom(zoom, range);
}

/**
 * The device pixel ratio one chart is drawn at: the display's, lowered where the canvas would pass
 * `CC_MAX_CANVAS_PIXELS`. Never above `deviceRatio`.
 */
export function ccCanvasRatio(cssWidth: number, cssHeight: number, deviceRatio: number): number {
  const ratio = isUsableZoom(deviceRatio) ? deviceRatio : 1;
  const area = cssWidth * cssHeight;
  if (!(area > 0) || !Number.isFinite(area)) {
    return ratio;
  }
  return Math.min(ratio, Math.sqrt(CC_MAX_CANVAS_PIXELS / area));
}
