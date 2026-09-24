/**
 * The figure size: the shape, sharpness and text size every figure is composed at — on the All
 * tab, on the Single tab and in every download.
 *
 * One record rather than six fields, so it is stored, validated and reset as one thing. Free of any
 * Angular dependency, so it unit-tests as plain TypeScript.
 */

import {
  FIGURE_EXPORT_DENSITY_PRESETS,
  FIGURE_EXPORT_MAX_DENSITY_PERCENT,
  FIGURE_EXPORT_MAX_DIMENSION,
  FIGURE_EXPORT_MAX_TEXT_SCALE_PERCENT,
  FIGURE_EXPORT_MIN_DENSITY_PERCENT,
  FIGURE_EXPORT_MIN_DIMENSION,
  FIGURE_EXPORT_MIN_TEXT_SCALE_PERCENT,
  FIGURE_EXPORT_PRESETS,
  FigureExportDensity,
  densityPresetFor,
  displayDensity
} from './figure-export';

/** Where the figure size is kept, per browser. Read and written in `try/catch`; never required. */
export const FIGURE_SIZE_STORAGE_KEY = 'overseer.modelComparison.figureSize';

/** The size a figure opens at, and the one a stored id no longer offered is read as. */
export const DEFAULT_FIGURE_RESOLUTION_ID = 'fullhd';

export interface FigureSizeSettings {
  /** A preset id from `FIGURE_EXPORT_PRESETS`, or `'custom'`. */
  readonly resolutionId: string;
  readonly customWidthPx: number;
  readonly customHeightPx: number;
  /** A listed density factor, or `'custom'` for {@link customDensityPercent}. */
  readonly densitySelection: number | 'custom';
  readonly customDensityPercent: number;
  readonly textScalePercent: number;
}

/**
 * Full HD, at the display's own density and at 100 % text.
 *
 * A function rather than a constant because "this display" is a property of the window the view
 * opens in: a density no listed step matches is held in the custom field, prefilled.
 */
export function defaultFigureSize(display: FigureExportDensity = displayDensity()): FigureSizeSettings {
  return {
    resolutionId: DEFAULT_FIGURE_RESOLUTION_ID,
    customWidthPx: 1920,
    customHeightPx: 1080,
    densitySelection: densityPresetFor(display) ?? 'custom',
    customDensityPercent: Math.round(display * 100),
    textScalePercent: 100
  };
}

/** Field by field, so a reset button can tell whether there is anything to reset. */
export function sameFigureSize(a: FigureSizeSettings, b: FigureSizeSettings): boolean {
  return a.resolutionId === b.resolutionId
    && a.customWidthPx === b.customWidthPx
    && a.customHeightPx === b.customHeightPx
    && a.densitySelection === b.densitySelection
    && a.customDensityPercent === b.customDensityPercent
    && a.textScalePercent === b.textScalePercent;
}

/**
 * The stored size, validated field by field; the default wherever a field is missing, out of range
 * or unreadable, and wherever storage itself is absent.
 *
 * A stored `'onscreen'`, the size that followed the page's own canvases, or any other id no longer
 * offered, reads as Full HD.
 */
export function readStoredFigureSize(display: FigureExportDensity = displayDensity()): FigureSizeSettings {
  const fallback = defaultFigureSize(display);
  let stored: unknown = null;
  try {
    const raw = localStorage.getItem(FIGURE_SIZE_STORAGE_KEY);
    stored = raw === null ? null : JSON.parse(raw);
  } catch {
    return fallback;
  }
  if (stored === null || typeof stored !== 'object' || Array.isArray(stored)) {
    return fallback;
  }
  const record = stored as Record<string, unknown>;
  return {
    resolutionId: isOfferedResolutionId(record['resolutionId'])
      ? record['resolutionId']
      : DEFAULT_FIGURE_RESOLUTION_ID,
    customWidthPx: inRange(record['customWidthPx'], FIGURE_EXPORT_MIN_DIMENSION, FIGURE_EXPORT_MAX_DIMENSION)
      ?? fallback.customWidthPx,
    customHeightPx: inRange(record['customHeightPx'], FIGURE_EXPORT_MIN_DIMENSION, FIGURE_EXPORT_MAX_DIMENSION)
      ?? fallback.customHeightPx,
    densitySelection: isDensitySelection(record['densitySelection'])
      ? record['densitySelection']
      : fallback.densitySelection,
    customDensityPercent: inRange(
      record['customDensityPercent'], FIGURE_EXPORT_MIN_DENSITY_PERCENT, FIGURE_EXPORT_MAX_DENSITY_PERCENT
    ) ?? fallback.customDensityPercent,
    textScalePercent: inRange(
      record['textScalePercent'], FIGURE_EXPORT_MIN_TEXT_SCALE_PERCENT, FIGURE_EXPORT_MAX_TEXT_SCALE_PERCENT
    ) ?? fallback.textScalePercent
  };
}

export function writeStoredFigureSize(settings: FigureSizeSettings): void {
  try {
    localStorage.setItem(FIGURE_SIZE_STORAGE_KEY, JSON.stringify({ version: 1, ...settings }));
  } catch {
    // Private mode or blocked storage: the size still applies for this session.
  }
}

function isOfferedResolutionId(value: unknown): value is string {
  return typeof value === 'string'
    && (value === 'custom' || FIGURE_EXPORT_PRESETS.some(preset => preset.id === value));
}

function isDensitySelection(value: unknown): value is number | 'custom' {
  return value === 'custom'
    || (typeof value === 'number' && FIGURE_EXPORT_DENSITY_PRESETS.includes(value));
}

/** A whole number inside `[min, max]`, or null. */
function inRange(value: unknown, min: number, max: number): number | null {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return null;
  }
  const rounded = Math.round(value);
  return rounded >= min && rounded <= max ? rounded : null;
}
