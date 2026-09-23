/**
 * Zoom arithmetic for the figure preview's stage.
 *
 * A zoom is **device pixels per export pixel**, so 1 is 100 %: one pixel of the written file on one
 * pixel of the display. None of it reaches an export; only the preview's own rasterisation and the
 * stage's CSS box read it.
 *
 * Deliberately free of any Angular dependency and of the DOM, so it unit-tests as plain TypeScript.
 */

/**
 * What the reader asked the stage to show.
 *
 * `'default'` shrinks the figure to fit and never enlarges it; `'fitScreen'` fills the stage, past
 * 100 % where the export is smaller than it; a number is an explicit zoom.
 */
export type PreviewViewRequest = 'default' | 'fitScreen' | number;

export interface PreviewZoomRange {
  readonly min: number;
  readonly max: number;
}

export const PREVIEW_MAX_ZOOM = 8;
export const PREVIEW_MIN_ZOOM_FLOOR = 0.1;

/** What the − / + buttons and keys step through, besides the screen fit. */
export const PREVIEW_ZOOM_STOPS: readonly number[] =
  [0.1, 0.125, 1 / 6, 0.25, 1 / 3, 0.5, 2 / 3, 1, 1.5, 2, 3, 4, 6, 8];

/** The zoom slider's resolution. Logarithmic, so every step multiplies the zoom by one factor. */
export const PREVIEW_SLIDER_STEPS = 1000;

/**
 * The largest bitmap the preview keeps alive while the dialog is open: 8K UHD, about 133 MB at
 * 4 bytes per pixel. A download frees its bitmap; the stage holds its own until it closes.
 */
export const PREVIEW_MAX_RASTER_PIXELS = 7680 * 4320;

/** Relative, so being on a stop steps past it however the zoom was reached. */
const STOP_TOLERANCE = 1e-6;

/** Every zoom the stage accepts. The floor drops below 10 % where the screen fit itself does. */
export function previewZoomRange(screenFitZoom: number): PreviewZoomRange {
  const fit = isUsableZoom(screenFitZoom) ? screenFitZoom : PREVIEW_MIN_ZOOM_FLOOR;
  return { min: Math.min(fit, PREVIEW_MIN_ZOOM_FLOOR), max: PREVIEW_MAX_ZOOM };
}

export function clampPreviewZoom(zoom: number, range: PreviewZoomRange): number {
  if (!Number.isFinite(zoom)) {
    return range.min;
  }
  return Math.min(range.max, Math.max(range.min, zoom));
}

/** `screenFitZoom` is expected clamped to {@link PREVIEW_MAX_ZOOM} already. */
export function resolvePreviewZoom(request: PreviewViewRequest, screenFitZoom: number): number {
  const range = previewZoomRange(screenFitZoom);
  if (request === 'default') {
    return clampPreviewZoom(Math.min(1, screenFitZoom), range);
  }
  if (request === 'fitScreen') {
    return clampPreviewZoom(screenFitZoom, range);
  }
  return clampPreviewZoom(request, range);
}

/** The stops inside the range, the screen fit and the range's own ends among them, ascending. */
function zoomStops(screenFitZoom: number, range: PreviewZoomRange): number[] {
  return [...PREVIEW_ZOOM_STOPS, screenFitZoom, range.min, range.max]
    .filter(stop => isUsableZoom(stop) && stop >= range.min && stop <= range.max)
    .sort((a, b) => a - b);
}

export function nextPreviewZoomStop(zoom: number, screenFitZoom: number, range: PreviewZoomRange): number {
  const next = zoomStops(screenFitZoom, range).find(stop => stop > zoom * (1 + STOP_TOLERANCE));
  return next ?? range.max;
}

export function previousPreviewZoomStop(
  zoom: number,
  screenFitZoom: number,
  range: PreviewZoomRange
): number {
  const stops = zoomStops(screenFitZoom, range);
  for (let index = stops.length - 1; index >= 0; index--) {
    if (stops[index] < zoom * (1 - STOP_TOLERANCE)) {
      return stops[index];
    }
  }
  return range.min;
}

export function canZoomPreviewIn(zoom: number, range: PreviewZoomRange): boolean {
  return zoom < range.max * (1 - STOP_TOLERANCE);
}

export function canZoomPreviewOut(zoom: number, range: PreviewZoomRange): boolean {
  return zoom > range.min * (1 + STOP_TOLERANCE);
}

export function zoomToSlider(zoom: number, range: PreviewZoomRange): number {
  if (!(range.max > range.min)) {
    return 0;
  }
  const z = clampPreviewZoom(zoom, range);
  return Math.round(PREVIEW_SLIDER_STEPS * Math.log(z / range.min) / Math.log(range.max / range.min));
}

export function sliderToZoom(value: number, range: PreviewZoomRange): number {
  if (!(range.max > range.min)) {
    return range.min;
  }
  const step = Math.min(PREVIEW_SLIDER_STEPS, Math.max(0, Number.isFinite(value) ? value : 0));
  return clampPreviewZoom(
    range.min * Math.exp(step / PREVIEW_SLIDER_STEPS * Math.log(range.max / range.min)), range);
}

/** A line is 16 px and a page 800 px; one event moves the zoom by at most a factor of two. */
export function wheelZoomFactor(deltaY: number, deltaMode: number): number {
  if (!Number.isFinite(deltaY)) {
    return 1;
  }
  const px = deltaMode === 1 ? deltaY * 16 : deltaMode === 2 ? deltaY * 800 : deltaY;
  return Math.min(2, Math.max(0.5, Math.exp(-px * 0.0015)));
}

/**
 * How far to scroll, on one axis, so the point under `clientPos` stays under it after the canvas
 * moved from `beforeEdge`/`beforeSize` to `afterEdge`/`afterSize`, all in client px.
 *
 * Holds whether the canvas is centred in the viewport or overflows it; where it cannot scroll, the
 * browser clamps the result.
 */
export function anchoredScrollDelta(
  beforeEdge: number,
  beforeSize: number,
  afterEdge: number,
  afterSize: number,
  clientPos: number
): number {
  if (!(beforeSize > 0) || !(afterSize > 0)) {
    return 0;
  }
  const fraction = Math.min(1, Math.max(0, (clientPos - beforeEdge) / beforeSize));
  return afterEdge + fraction * afterSize - clientPos;
}

/**
 * The fraction of the target the preview rasterises at a zoom: the target's own pixels at and above
 * 100 %, the displayed pixels below it, and less where the result would pass the raster budget.
 */
export function previewRasterZoom(
  zoom: number,
  pixelWidth: number,
  pixelHeight: number
): { zoom: number; capped: boolean } {
  const wanted = Math.min(1, zoom);
  const area = pixelWidth * pixelHeight * wanted * wanted;
  if (!(area > PREVIEW_MAX_RASTER_PIXELS)) {
    return { zoom: wanted, capped: false };
  }
  return { zoom: Math.sqrt(PREVIEW_MAX_RASTER_PIXELS / (pixelWidth * pixelHeight)), capped: true };
}

/** `"38%"`, `"12.5%"`, `"800%"`: one decimal below 20 %, where the stops are not whole numbers. */
export function formatPreviewZoom(zoom: number): string {
  const percent = zoom * 100;
  if (percent >= 20) {
    return `${Math.round(percent)}%`;
  }
  const tenths = Math.round(percent * 10) / 10;
  return Number.isInteger(tenths) ? `${tenths}%` : `${tenths.toFixed(1)}%`;
}

function isUsableZoom(zoom: number): boolean {
  return Number.isFinite(zoom) && zoom > 0;
}
