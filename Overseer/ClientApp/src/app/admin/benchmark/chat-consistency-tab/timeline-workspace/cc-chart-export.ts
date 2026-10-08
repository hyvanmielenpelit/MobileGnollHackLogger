/**
 * Export layout, file names and theme of a Chat Consistency chart image. The chart draws its own
 * header band, so the layout has no chrome around it: the plot box is the whole layout box. Free of
 * Angular and the DOM.
 */

import {
  FigureExportFormat,
  FigureExportLayout,
  bitmapRefusal,
  exportTimestamp,
  layoutBoxFor
} from '../../model-comparison/figure-export';
import {
  FigureSizeSettings,
  resolveSizeDensity,
  resolveSizeResolution,
  sizeErrors
} from '../../model-comparison/figure-size';
import { CC_PRINT_THEME, CC_SCREEN_THEME, CcChartTheme, CcFigureKey } from '../chat-consistency-charts';

export interface CcExportPlan {
  readonly layout: FigureExportLayout | null;
  readonly refusal: string | null;
}

/**
 * The plot-only layout for one size, or why the size is refused. The arithmetic of
 * `resolveFigureLayout`, without its chrome.
 */
export function ccExportLayout(size: FigureSizeSettings): CcExportPlan {
  const error = sizeErrors(size, 'chart').any;
  if (error !== '') {
    return { layout: null, refusal: error };
  }
  const resolution = resolveSizeResolution(size);
  const density = resolveSizeDensity(size);
  const oversized = bitmapRefusal(resolution.widthPx, resolution.heightPx, density);
  if (oversized) {
    return { layout: null, refusal: oversized };
  }
  const box = layoutBoxFor(resolution.widthPx, resolution.heightPx, size.textScalePercent / 100);
  return {
    layout: {
      layoutWidth: box.layoutWidth,
      layoutHeight: box.layoutHeight,
      plotWidth: box.layoutWidth,
      plotHeight: box.layoutHeight,
      density: box.density * density,
      pixelWidth: Math.round(resolution.widthPx * density),
      pixelHeight: Math.round(resolution.heightPx * density)
    },
    refusal: null
  };
}

/** A file name part reduced to `[A-Za-z0-9_-]`; anything else becomes `-`. */
function filePart(value: string, fallback: string): string {
  const part = (value || fallback).replace(/[^A-Za-z0-9_-]/g, '-');
  return part === '' ? fallback : part;
}

/** `chat-consistency_<model key>_<figure key>_<yyyyMMdd_HHmmss>.<png|webp>`, in the format actually written. */
export function ccChartFilename(
  modelKey: string,
  figureKey: CcFigureKey,
  format: FigureExportFormat,
  now: Date = new Date()
): string {
  return `chat-consistency_${filePart(modelKey, 'model')}_${filePart(figureKey, 'chart')}_${exportTimestamp(now)}.${format}`;
}

/** `chat-consistency_<model key>_charts_<yyyyMMdd_HHmmss>.zip`. */
export function ccChartArchiveFilename(modelKey: string, now: Date = new Date()): string {
  return `chat-consistency_${filePart(modelKey, 'model')}_charts_${exportTimestamp(now)}.zip`;
}

/**
 * The theme an image is drawn in: *As shown* is the screen theme on its opaque surface, since a
 * transparent dark chart is unreadable on a light page; *Light, for print* is the print theme.
 */
export function ccExportTheme(choice: 'screen' | 'print'): CcChartTheme {
  return choice === 'print' ? CC_PRINT_THEME : { ...CC_SCREEN_THEME, background: CC_SCREEN_THEME.surface };
}
