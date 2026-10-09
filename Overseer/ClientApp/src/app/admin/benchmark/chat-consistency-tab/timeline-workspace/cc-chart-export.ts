/**
 * File names of a Chat Consistency chart image and of the archive of several. The image itself is
 * composed by `cc-figure-compose.ts`. Free of Angular and the DOM.
 */

import { FigureExportFormat, exportTimestamp } from '../../model-comparison/figure-export';
import { CcFigureKey } from '../chat-consistency-charts';

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
