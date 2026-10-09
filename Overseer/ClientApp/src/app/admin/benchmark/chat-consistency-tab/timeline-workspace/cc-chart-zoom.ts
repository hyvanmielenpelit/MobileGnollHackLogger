/**
 * Zoom arithmetic for the Charts step's composed charts, on Model Comparison's preview model
 * (`preview-view.ts`): a zoom is **device pixels per file pixel**, so 1 (100 %) shows one pixel of
 * the downloaded file on one pixel of the display. The views show the download itself, rasterized
 * for the screen (`previewLayoutFor`), never a different composition.
 *
 * A fit leaves room for the figure's HTML around the image (`CcFigureChrome`), and its CSS box is
 * floored to whole CSS px, so a fitted chart never overflows its scroller by a fraction of a pixel.
 * Free of Angular and the DOM.
 */

import { FigureExportLayout, PreviewLayout, previewLayoutFor } from '../../model-comparison/figure-export';
import { FigureSizeSettings, resolveSizeDensity, resolveSizeResolution, sizeErrors } from '../../model-comparison/figure-size';
import { PREVIEW_MAX_ZOOM, PreviewZoomRange, clampPreviewZoom, previewZoomRange } from '../../model-comparison/preview-view';

/** The pixel size of the file a chart size writes: the resolution at its density. */
export interface CcTargetPixels {
  readonly pixelWidth: number;
  readonly pixelHeight: number;
}

/** The figure's HTML around the image, in whole CSS px: its padding and border across, and under it the takeaway, the footer row and the *Show data* summary. */
export interface CcFigureChrome {
  readonly width: number;
  readonly height: number;
}

/** What the reader asked the view to show: a fit, or an explicit zoom. */
export type CcZoomView = 'fitWidth' | 'fitScreen' | number;

/** The three fits of one view, each in device pixels per file pixel. */
export interface CcZoomFits {
  readonly width: number;
  readonly height: number;
  readonly screen: number;
}

/** The displayed image's CSS box. */
export interface CcDisplaySize {
  readonly cssWidth: number;
  readonly cssHeight: number;
}

/** Below a pixel's float noise, so a box that is whole up to rounding is not floored a pixel short. */
const WHOLE_PIXEL_TOLERANCE = 1e-6;

function isUsableZoom(zoom: number): boolean {
  return Number.isFinite(zoom) && zoom > 0;
}

/** The device pixel ratio the views rasterize at: the display's, between 1 and 4, as `previewLayoutFor` takes it. */
export function ccPreviewDpr(devicePixelRatio: number): number {
  return Number.isFinite(devicePixelRatio) ? Math.min(4, Math.max(1, devicePixelRatio)) : 1;
}

/**
 * The file's pixel size for a usable chart size, as `resolveFigureLayout` rounds it; null while the
 * size is refused.
 */
export function ccTargetPixels(size: FigureSizeSettings): CcTargetPixels | null {
  if (sizeErrors(size, 'chart').any !== '') return null;
  const resolution = resolveSizeResolution(size);
  const density = resolveSizeDensity(size);
  return {
    pixelWidth: Math.round(Math.round(resolution.widthPx) * density),
    pixelHeight: Math.round(Math.round(resolution.heightPx) * density)
  };
}

/** A fit, at most `PREVIEW_MAX_ZOOM`; NaN where there is no room to fit into. */
function fitRatio(availableCss: number, filePixels: number, dpr: number): number {
  const ratio = availableCss * ccPreviewDpr(dpr) / filePixels;
  return isUsableZoom(ratio) ? Math.min(PREVIEW_MAX_ZOOM, ratio) : Number.NaN;
}

/** The zoom at which the image fills `contentWidth` less the figure's HTML across. */
export function ccFitWidthZoom(target: CcTargetPixels, contentWidth: number, chrome: CcFigureChrome, dpr: number): number {
  return fitRatio(contentWidth - chromeOf(chrome).width, target.pixelWidth, dpr);
}

/** The zoom at which the image and the HTML under it fill `contentHeight`. */
export function ccFitHeightZoom(target: CcTargetPixels, contentHeight: number, chrome: CcFigureChrome, dpr: number): number {
  return fitRatio(contentHeight - chromeOf(chrome).height, target.pixelHeight, dpr);
}

/** The larger zoom at which one whole figure, its HTML included, fits both ways. */
export function ccFitScreenZoom(
  target: CcTargetPixels,
  contentWidth: number,
  contentHeight: number,
  chrome: CcFigureChrome,
  dpr: number
): number {
  return Math.min(ccFitWidthZoom(target, contentWidth, chrome, dpr), ccFitHeightZoom(target, contentHeight, chrome, dpr));
}

/**
 * *Fit width* against the scroller's full content box, `contentWidth` × `contentHeight` with no
 * scrollbar. Where the fitted figure is taller than the box, a vertical scrollbar of `scrollbar` CSS px
 * will narrow it, so the fit is taken against the narrower width instead; no horizontal scrollbar
 * follows either way.
 */
export function ccFitWidthWithScrollbar(
  target: CcTargetPixels,
  contentWidth: number,
  contentHeight: number,
  chrome: CcFigureChrome,
  dpr: number,
  scrollbar: number
): number {
  const fit = ccFitWidthZoom(target, contentWidth, chrome, dpr);
  if (!isUsableZoom(fit) || !(scrollbar > 0)) return fit;
  const height = ccDisplaySize(target, fit, dpr, true).cssHeight + chromeOf(chrome).height;
  return height <= contentHeight ? fit : ccFitWidthZoom(target, contentWidth - scrollbar, chrome, dpr);
}

/** Every zoom the controls reach: 10 % to 800 %, the floor following a lower fit (`previewZoomRange`). */
export function ccZoomRange(fit: number): PreviewZoomRange {
  return previewZoomRange(fit);
}

/** The zoom a view stands for, clamped into the range of the lowest usable fit. */
export function ccResolveZoom(view: CcZoomView, fits: CcZoomFits): number {
  const usable = [fits.width, fits.height, fits.screen].filter(isUsableZoom);
  const range = ccZoomRange(usable.length > 0 ? Math.min(...usable) : Number.NaN);
  const zoom = view === 'fitWidth' ? fits.width : view === 'fitScreen' ? fits.screen : view;
  return clampPreviewZoom(isUsableZoom(zoom) ? zoom : range.min, range);
}

/**
 * The image's CSS box at `zoom`: the file's pixels over the device ratio, times the zoom. A fit floors
 * both sides to whole CSS px, since a fractional box rounds differently in layout and in paint; an
 * explicit zoom keeps them exact.
 */
export function ccDisplaySize(target: CcTargetPixels, zoom: number, dpr: number, fit: boolean): CcDisplaySize {
  const ratio = ccPreviewDpr(dpr);
  const width = target.pixelWidth / ratio * zoom;
  const height = target.pixelHeight / ratio * zoom;
  return fit
    ? {
      cssWidth: Math.max(1, Math.floor(width + WHOLE_PIXEL_TOLERANCE)),
      cssHeight: Math.max(1, Math.floor(height + WHOLE_PIXEL_TOLERANCE))
    }
    : { cssWidth: Math.max(1, width), cssHeight: Math.max(1, height) };
}

/**
 * The target composition rasterized for the screen at `zoom` (`previewLayoutFor`): the same layout box,
 * plot box and chrome, only the density and the pixel size changed, within `PREVIEW_MAX_RASTER_PIXELS`.
 * Null where the zoom leaves no usable box.
 */
export function ccPreviewLayout(target: FigureExportLayout, zoom: number, dpr: number): PreviewLayout | null {
  const ratio = ccPreviewDpr(dpr);
  if (!isUsableZoom(zoom)) return null;
  // A stage exactly the zoomed image, so the fit `previewLayoutFor` derives from it is this zoom.
  const stage = {
    width: target.pixelWidth / ratio * zoom,
    height: target.pixelHeight / ratio * zoom,
    devicePixelRatio: ratio
  };
  return previewLayoutFor(target, stage, zoom);
}

function chromeOf(chrome: CcFigureChrome): CcFigureChrome {
  const whole = (value: number) => (Number.isFinite(value) && value > 0 ? value : 0);
  return { width: whole(chrome.width), height: whole(chrome.height) };
}
