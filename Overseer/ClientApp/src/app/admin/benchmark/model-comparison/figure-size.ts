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
  FigureExportResolution,
  bitmapRefusal,
  densityPercentLabel,
  densityPresetFor,
  displayDensity,
  layoutBoxFor
} from './figure-export';

/** Where the figure size is kept, per browser. Read and written in `try/catch`; never required. */
export const FIGURE_SIZE_STORAGE_KEY = 'overseer.modelComparison.figureSize';

/** Where the table image size is kept, per browser, apart from the charts' size. */
export const TABLE_IMAGE_SIZE_STORAGE_KEY = 'overseer.modelComparison.tableImageSize';

/** The size a figure opens at, and the one a stored id no longer offered is read as. */
export const DEFAULT_FIGURE_RESOLUTION_ID = 'fullhd';

/** The resolution id of the table image's content-sized mode. Never offered for charts. */
export const FIT_RESOLUTION_ID = 'fit';

export interface FigureSizeSettings {
  /** A preset id from `FIGURE_EXPORT_PRESETS`, `'custom'`, or `'fit'` where the caller allows it. */
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
  return readStoredSize(FIGURE_SIZE_STORAGE_KEY, defaultFigureSize(display), false);
}

export function writeStoredFigureSize(settings: FigureSizeSettings): void {
  writeStoredSize(FIGURE_SIZE_STORAGE_KEY, settings);
}

/**
 * The table image's default: *Fit the table* at 200 % and 100 % text, which is the content-sized,
 * 2× image the table was always written as. The custom pair is where a switch to Custom starts.
 */
export function defaultTableImageSize(): FigureSizeSettings {
  return {
    resolutionId: FIT_RESOLUTION_ID,
    customWidthPx: 1920,
    customHeightPx: 1080,
    densitySelection: 2,
    customDensityPercent: 200,
    textScalePercent: 100
  };
}

/** The stored table image size, validated as {@link readStoredFigureSize} does, with `'fit'` accepted. */
export function readStoredTableImageSize(): FigureSizeSettings {
  return readStoredSize(TABLE_IMAGE_SIZE_STORAGE_KEY, defaultTableImageSize(), true);
}

export function writeStoredTableImageSize(settings: FigureSizeSettings): void {
  writeStoredSize(TABLE_IMAGE_SIZE_STORAGE_KEY, settings);
}

function readStoredSize(key: string, fallback: FigureSizeSettings, allowFit: boolean): FigureSizeSettings {
  let stored: unknown = null;
  try {
    const raw = localStorage.getItem(key);
    stored = raw === null ? null : JSON.parse(raw);
  } catch {
    return fallback;
  }
  if (stored === null || typeof stored !== 'object' || Array.isArray(stored)) {
    return fallback;
  }
  const record = stored as Record<string, unknown>;
  const resolutionId = record['resolutionId'];
  return {
    resolutionId: isOfferedResolutionId(resolutionId) || (allowFit && resolutionId === FIT_RESOLUTION_ID)
      ? resolutionId as string
      : allowFit ? fallback.resolutionId : DEFAULT_FIGURE_RESOLUTION_ID,
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

function writeStoredSize(key: string, settings: FigureSizeSettings): void {
  try {
    localStorage.setItem(key, JSON.stringify({ version: 1, ...settings }));
  } catch {
    // Private mode or blocked storage: the size still applies for this session.
  }
}

/** A side rounded and clamped into the supported custom range. */
export function clampExportDimension(value: number): number {
  if (!Number.isFinite(value)) {
    return FIGURE_EXPORT_MIN_DIMENSION;
  }
  return Math.min(FIGURE_EXPORT_MAX_DIMENSION, Math.max(FIGURE_EXPORT_MIN_DIMENSION, Math.round(value)));
}

/** A density percentage rounded and clamped into its bounds; 100 for anything unreadable. */
export function clampDensityPercent(value: number): number {
  if (!Number.isFinite(value)) {
    return 100;
  }
  return Math.min(FIGURE_EXPORT_MAX_DENSITY_PERCENT, Math.max(FIGURE_EXPORT_MIN_DENSITY_PERCENT, Math.round(value)));
}

/** Whether a custom side is inside the supported range, as typed. */
export function isUsableDimension(value: number): boolean {
  return Number.isFinite(value) && value >= FIGURE_EXPORT_MIN_DIMENSION && value <= FIGURE_EXPORT_MAX_DIMENSION;
}

/** `2` rather than `2.00`, and `1.33` rather than `1.3333333`. */
export function formatDensityFactor(density: number): string {
  return Number.isInteger(density) ? String(density) : density.toFixed(2);
}

/**
 * The chosen preset, or the custom pair clamped into the supported range. `'fit'` and any id no
 * longer offered resolve as Full HD: a caller that allows fit checks for it first.
 */
export function resolveSizeResolution(settings: FigureSizeSettings): FigureExportResolution {
  if (settings.resolutionId !== 'custom') {
    return FIGURE_EXPORT_PRESETS.find(preset => preset.id === settings.resolutionId)
      ?? FIGURE_EXPORT_PRESETS.find(preset => preset.id === DEFAULT_FIGURE_RESOLUTION_ID)
      ?? FIGURE_EXPORT_PRESETS[0];
  }
  return {
    id: 'custom',
    label: 'Custom',
    group: 'Custom',
    widthPx: clampExportDimension(settings.customWidthPx),
    heightPx: clampExportDimension(settings.customHeightPx)
  };
}

/** The chosen factor: a listed preset, or the custom percentage clamped into its bounds. */
export function resolveSizeDensity(settings: FigureSizeSettings): FigureExportDensity {
  const selection = settings.densitySelection;
  return selection !== 'custom' ? selection : clampDensityPercent(settings.customDensityPercent) / 100;
}

/** The size's problems, each empty while that part is usable; `any` is the first non-empty one. */
export interface SizeErrors {
  readonly customResolution: string;
  readonly customDensity: string;
  /** Empty while either input is out of range, which is the more specific complaint, and in fit mode. */
  readonly bitmap: string;
  readonly any: string;
}

/** What is wrong with a size, named. `noun` is what the messages call the image (`figure`, `image`). */
export function sizeErrors(settings: FigureSizeSettings, noun = 'figure'): SizeErrors {
  const custom = settings.resolutionId === 'custom';
  const bad = custom
    ? [
      isUsableDimension(settings.customWidthPx) ? '' : 'width',
      isUsableDimension(settings.customHeightPx) ? '' : 'height'
    ].filter(name => name !== '')
    : [];
  const customResolution = bad.length === 0
    ? ''
    : `The ${noun} ${bad.join(' and ')} must be between ${FIGURE_EXPORT_MIN_DIMENSION} and ` +
      `${FIGURE_EXPORT_MAX_DIMENSION} px.`;
  const percent = settings.customDensityPercent;
  const customDensity = settings.densitySelection !== 'custom'
    || (Number.isFinite(percent)
      && percent >= FIGURE_EXPORT_MIN_DENSITY_PERCENT
      && percent <= FIGURE_EXPORT_MAX_DENSITY_PERCENT)
    ? ''
    : `The pixel density must be between ${FIGURE_EXPORT_MIN_DENSITY_PERCENT} and ` +
      `${FIGURE_EXPORT_MAX_DENSITY_PERCENT} %.`;
  let bitmap = '';
  if (customResolution === '' && customDensity === '' && settings.resolutionId !== FIT_RESOLUTION_ID) {
    const resolution = resolveSizeResolution(settings);
    bitmap = bitmapRefusal(resolution.widthPx, resolution.heightPx, resolveSizeDensity(settings)) ?? '';
  }
  return { customResolution, customDensity, bitmap, any: customResolution || customDensity || bitmap };
}

/**
 * What the size will actually write, in the reader's own units. `'figure'` names the chart
 * composition box ({@link layoutBoxFor}); `'plain'` names the table's box, where one layout px is
 * one requested px times the text size. Empty in fit mode, where the size depends on the table.
 */
export function sizeDimensionsLabel(settings: FigureSizeSettings, rule: 'figure' | 'plain' = 'figure'): string {
  if (settings.resolutionId === FIT_RESOLUTION_ID) {
    return '';
  }
  const resolution = resolveSizeResolution(settings);
  const density = resolveSizeDensity(settings);
  const percent = densityPercentLabel(density);
  const textScale = settings.textScalePercent / 100;
  const written = `${Math.round(resolution.widthPx * density)} × ` +
    `${Math.round(resolution.heightPx * density)} px`;
  // At 100 % the requested size and the written one are the same number, and printing it twice
  // would read as an error rather than as a multiplication.
  const requested = density === 1
    ? `at ${percent}`
    : `(${resolution.widthPx} × ${resolution.heightPx} at ${percent})`;
  const box = rule === 'figure'
    ? layoutBoxFor(resolution.widthPx, resolution.heightPx, textScale)
    : { layoutWidth: resolution.widthPx / textScale, layoutHeight: resolution.heightPx / textScale, density: textScale };
  return `${written} ${requested} — laid out at ` +
    `${Math.round(box.layoutWidth)} × ${Math.round(box.layoutHeight)}, ` +
    `${formatDensityFactor(box.density * density)}× density`;
}

/** The closed size section's one-line read-out. */
export function sizeReadout(settings: FigureSizeSettings): string {
  const density = densityPercentLabel(resolveSizeDensity(settings));
  if (settings.resolutionId === FIT_RESOLUTION_ID) {
    return `Fit the table · ${density}`;
  }
  const resolution = resolveSizeResolution(settings);
  const size = settings.resolutionId === 'custom'
    ? `Custom ${resolution.widthPx} × ${resolution.heightPx}`
    : resolution.label;
  return `${size} · ${density} · text ${settings.textScalePercent} %`;
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
