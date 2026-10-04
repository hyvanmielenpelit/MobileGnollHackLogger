/**
 * The key-figures images' download settings: format, WebP quality and size. One record shared by the
 * run report and the Battery Run Report, so a change made in either dialog applies in both.
 *
 * Hosts read it at use time — on every open of the chooser and on every copy and download — and never
 * hold a copy from construction. No Angular, so it unit-tests as plain TypeScript.
 */

import {
  DEFAULT_WEBP_QUALITY,
  FigureExportFormat,
  WEBP_QUALITY_OPTIONS,
  WebpQuality
} from '../model-comparison/figure-export';
import { FIT_RESOLUTION_ID, FigureSizeSettings, parseSizeSettings } from '../model-comparison/figure-size';

export interface KeyFiguresExportSettings {
  readonly format: FigureExportFormat;
  readonly webpQuality: WebpQuality;
  readonly size: FigureSizeSettings;
}

/** Where the settings are kept, per browser, as `{ version: 1, format, webpQuality, size }`. */
export const KEY_FIGURES_EXPORT_STORAGE_KEY = 'overseer.benchmark.keyFigures.export';

/** Which of the chooser's two file sections are open, as `{ version: 1, format, size }`. */
export const KEY_FIGURES_EXPORT_SECTIONS_STORAGE_KEY = 'overseer.benchmark.keyFigures.exportSections';

const STORAGE_VERSION = 1;

/** *Fit the figures* at 200 %: the size every key-figures image had before it could be chosen. */
export function defaultKeyFiguresSize(): FigureSizeSettings {
  return {
    resolutionId: FIT_RESOLUTION_ID,
    customWidthPx: 1920,
    customHeightPx: 1080,
    densitySelection: 2,
    customDensityPercent: 200,
    textScalePercent: 100
  };
}

/** PNG, quality 85 for a later WebP, and {@link defaultKeyFiguresSize}. */
export function defaultKeyFiguresExportSettings(): KeyFiguresExportSettings {
  return { format: 'png', webpQuality: DEFAULT_WEBP_QUALITY, size: defaultKeyFiguresSize() };
}

/** The raw text the last read parsed, and what it read as; a read of unchanged storage returns the same object. */
let lastRead: { raw: string | null; settings: KeyFiguresExportSettings } | null = null;

/**
 * The stored settings, field by field: a format other than PNG or WebP, a quality not offered or an
 * unreadable size field falls back to its default, and so does everything when storage is absent,
 * unreadable or throws. While storage holds the same text, the same object comes back.
 */
export function readStoredKeyFiguresExportSettings(): KeyFiguresExportSettings {
  let raw: string | null;
  try {
    raw = localStorage.getItem(KEY_FIGURES_EXPORT_STORAGE_KEY);
  } catch {
    return defaultKeyFiguresExportSettings();
  }
  if (lastRead && lastRead.raw === raw) {
    return lastRead.settings;
  }
  const settings = parseKeyFiguresExportSettings(raw);
  lastRead = { raw, settings };
  return settings;
}

function parseKeyFiguresExportSettings(raw: string | null): KeyFiguresExportSettings {
  const defaults = defaultKeyFiguresExportSettings();
  if (raw === null) {
    return defaults;
  }
  let stored: unknown;
  try {
    stored = JSON.parse(raw);
  } catch {
    return defaults;
  }
  if (stored === null || typeof stored !== 'object' || Array.isArray(stored)) {
    return defaults;
  }
  const record = stored as Record<string, unknown>;
  const format = record['format'];
  const quality = record['webpQuality'];
  return {
    format: format === 'png' || format === 'webp' ? format : defaults.format,
    webpQuality: WEBP_QUALITY_OPTIONS.includes(quality as WebpQuality) ? quality as WebpQuality : defaults.webpQuality,
    size: parseSizeSettings(record['size'], defaults.size, true)
  };
}

export function writeStoredKeyFiguresExportSettings(settings: KeyFiguresExportSettings): void {
  try {
    localStorage.setItem(KEY_FIGURES_EXPORT_STORAGE_KEY, JSON.stringify({
      version: STORAGE_VERSION,
      format: settings.format,
      webpQuality: settings.webpQuality,
      size: settings.size
    }));
  } catch {
    // Private mode or blocked storage: the settings are not remembered, and the next read is the default.
  }
}

/** Field by field, so the chooser can tell a change from the same settings handed back. */
export function sameKeyFiguresExportSettings(a: KeyFiguresExportSettings, b: KeyFiguresExportSettings): boolean {
  const x = a.size;
  const y = b.size;
  return a.format === b.format && a.webpQuality === b.webpQuality
    && x.resolutionId === y.resolutionId && x.customWidthPx === y.customWidthPx && x.customHeightPx === y.customHeightPx
    && x.densitySelection === y.densitySelection && x.customDensityPercent === y.customDensityPercent
    && x.textScalePercent === y.textScalePercent;
}

/** The open state of the chooser's Image format and Image size sections. */
export interface KeyFiguresExportSections {
  readonly format: boolean;
  readonly size: boolean;
}

/** Both open wherever nothing readable is stored. */
export function readStoredKeyFiguresExportSections(): KeyFiguresExportSections {
  const fallback: KeyFiguresExportSections = { format: true, size: true };
  try {
    const raw = localStorage.getItem(KEY_FIGURES_EXPORT_SECTIONS_STORAGE_KEY);
    const stored: unknown = raw === null ? null : JSON.parse(raw);
    if (stored === null || typeof stored !== 'object' || Array.isArray(stored)) {
      return fallback;
    }
    const { format, size } = stored as { format?: unknown; size?: unknown };
    return {
      format: typeof format === 'boolean' ? format : fallback.format,
      size: typeof size === 'boolean' ? size : fallback.size
    };
  } catch {
    return fallback;
  }
}

export function writeStoredKeyFiguresExportSections(sections: KeyFiguresExportSections): void {
  try {
    localStorage.setItem(KEY_FIGURES_EXPORT_SECTIONS_STORAGE_KEY, JSON.stringify({
      version: STORAGE_VERSION,
      format: sections.format,
      size: sections.size
    }));
  } catch {
    // Storage unavailable: the sections open as they default.
  }
}

/** `PNG` or `WebP`, as the download names and tooltips print it. */
export function keyFiguresFormatLabel(format: FigureExportFormat): 'PNG' | 'WebP' {
  return format === 'webp' ? 'WebP' : 'PNG';
}
