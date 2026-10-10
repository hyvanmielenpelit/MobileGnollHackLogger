/**
 * The Chat Consistency Results images' settings: what each section's image includes, the image details
 * common to every section, the colors, the format and the size. Kept apart from the key-figures images'
 * settings, so a change in one never moves the other.
 *
 * Hosts read the record at use time — on every open of the Image settings dialog and on every copy and
 * download — and never hold a copy from construction. No Angular, so it unit-tests as plain TypeScript.
 */

import {
  DEFAULT_WEBP_QUALITY,
  FigureExportFormat,
  WEBP_QUALITY_OPTIONS,
  WebpQuality
} from '../../model-comparison/figure-export';
import { FIT_RESOLUTION_ID, FigureSizeSettings, parseSizeSettings } from '../../model-comparison/figure-size';

/** The Results step's sections, in tab order; each is one image. */
export const CC_RESULTS_IMAGE_SECTIONS = ['summary', 'verdicts', 'periods', 'attribution', 'nextRuns', 'details'] as const;

export type CcResultsImageSection = typeof CC_RESULTS_IMAGE_SECTIONS[number];

/** Each section's name, as its tab reads. */
export const CC_RESULTS_IMAGE_SECTION_LABELS: Readonly<Record<CcResultsImageSection, string>> = {
  summary: 'Summary',
  verdicts: 'Verdicts',
  periods: 'Periods',
  attribution: 'Attribution',
  nextRuns: 'Next runs',
  details: 'Details'
};

/** *Dark, as on screen* or *Light, for print*. */
export type CcResultsImageScheme = 'dark' | 'light';

/** The parts every section's image may carry around its content, in drawing order. */
export const CC_RESULTS_IMAGE_DETAILS: readonly { readonly key: string; readonly label: string }[] = [
  { key: 'header', label: 'Header' },
  { key: 'model', label: 'Model line' },
  { key: 'analysis', label: 'Analysis line' },
  { key: 'footer', label: 'Footer' }
];

export type CcResultsImageExclusions = Readonly<Record<CcResultsImageSection, readonly string[]>>;

export interface CcResultsImageSettings {
  readonly format: FigureExportFormat;
  readonly webpQuality: WebpQuality;
  readonly size: FigureSizeSettings;
  readonly scheme: CcResultsImageScheme;
  /** Per section, the item keys left out; an item not named is included. */
  readonly excluded: CcResultsImageExclusions;
  /** The image details left out. */
  readonly detailsExcluded: readonly string[];
}

/** Where the settings are kept, per browser, as `{ version: 1, format, webpQuality, size, scheme, excluded, detailsExcluded }`. */
export const CC_RESULTS_IMAGE_STORAGE_KEY = 'overseer.benchmark.chatConsistency.resultsImage';

/** Which of the dialog's two file sections are open, as `{ version: 1, format, size }`. */
export const CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY = 'overseer.benchmark.chatConsistency.resultsImage.sections';

const STORAGE_VERSION = 1;

/** The items off while nothing is stored: the long or secondary parts of a section. */
export function defaultCcResultsImageExclusions(): CcResultsImageExclusions {
  return {
    summary: [],
    verdicts: ['more'],
    periods: ['units'],
    attribution: [],
    nextRuns: ['reasons'],
    details: []
  };
}

/** *Fit the content* at 200 % and 100 % text. The custom pair is where a switch to Custom starts. */
export function defaultCcResultsImageSize(): FigureSizeSettings {
  return {
    resolutionId: FIT_RESOLUTION_ID,
    customWidthPx: 1920,
    customHeightPx: 1080,
    densitySelection: 2,
    customDensityPercent: 200,
    textScalePercent: 100
  };
}

/** PNG, quality 85 for a later WebP, fit at 200 %, the dark scheme, and the default exclusions. */
export function defaultCcResultsImageSettings(): CcResultsImageSettings {
  return {
    format: 'png',
    webpQuality: DEFAULT_WEBP_QUALITY,
    size: defaultCcResultsImageSize(),
    scheme: 'dark',
    excluded: defaultCcResultsImageExclusions(),
    detailsExcluded: []
  };
}

/**
 * The stored settings, field by field: an unreadable field falls back to its default, and so does
 * everything when storage is absent, unreadable, of another version or throws. A section's exclusions
 * are read only when stored as a list of strings.
 */
export function readStoredCcResultsImageSettings(): CcResultsImageSettings {
  let raw: string | null;
  try {
    raw = localStorage.getItem(CC_RESULTS_IMAGE_STORAGE_KEY);
  } catch {
    return defaultCcResultsImageSettings();
  }
  return parseCcResultsImageSettings(raw);
}

/** The settings a stored text reads as; the defaults for null or anything unreadable. */
export function parseCcResultsImageSettings(raw: string | null): CcResultsImageSettings {
  const defaults = defaultCcResultsImageSettings();
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
  if (record['version'] !== STORAGE_VERSION) {
    return defaults;
  }
  const format = record['format'];
  const quality = record['webpQuality'];
  const scheme = record['scheme'];
  return {
    format: format === 'png' || format === 'webp' ? format : defaults.format,
    webpQuality: WEBP_QUALITY_OPTIONS.includes(quality as WebpQuality) ? quality as WebpQuality : defaults.webpQuality,
    size: parseSizeSettings(record['size'], defaults.size, true),
    scheme: scheme === 'dark' || scheme === 'light' ? scheme : defaults.scheme,
    excluded: parseExclusions(record['excluded'], defaults.excluded),
    detailsExcluded: stringList(record['detailsExcluded']) ?? defaults.detailsExcluded
  };
}

function parseExclusions(value: unknown, fallback: CcResultsImageExclusions): CcResultsImageExclusions {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return fallback;
  }
  const record = value as Record<string, unknown>;
  const result = {} as Record<CcResultsImageSection, readonly string[]>;
  for (const section of CC_RESULTS_IMAGE_SECTIONS) {
    result[section] = stringList(record[section]) ?? fallback[section];
  }
  return result;
}

/** Trimmed, non-empty and distinct strings in their first order; null for anything but an array. */
function stringList(value: unknown): string[] | null {
  if (!Array.isArray(value)) {
    return null;
  }
  const result: string[] = [];
  for (const entry of value) {
    if (typeof entry !== 'string') {
      continue;
    }
    const key = entry.trim();
    if (key !== '' && !result.includes(key)) {
      result.push(key);
    }
  }
  return result;
}

export function writeStoredCcResultsImageSettings(settings: CcResultsImageSettings): void {
  try {
    localStorage.setItem(CC_RESULTS_IMAGE_STORAGE_KEY, JSON.stringify({
      version: STORAGE_VERSION,
      format: settings.format,
      webpQuality: settings.webpQuality,
      size: settings.size,
      scheme: settings.scheme,
      excluded: settings.excluded,
      detailsExcluded: settings.detailsExcluded
    }));
  } catch {
    // Private mode or blocked storage: the settings are not remembered, and the next read is the default.
  }
}

/** The settings with one section's exclusions replaced. */
export function withSectionExclusions(
  settings: CcResultsImageSettings,
  section: CcResultsImageSection,
  excluded: readonly string[]
): CcResultsImageSettings {
  return { ...settings, excluded: { ...settings.excluded, [section]: [...excluded] } };
}

/** The open state of the dialog's Image format and Image size sections. */
export interface CcResultsImageFileSections {
  readonly format: boolean;
  readonly size: boolean;
}

/** Both open wherever nothing readable is stored. */
export function readStoredCcResultsImageFileSections(): CcResultsImageFileSections {
  const fallback: CcResultsImageFileSections = { format: true, size: true };
  try {
    const raw = localStorage.getItem(CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY);
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

export function writeStoredCcResultsImageFileSections(sections: CcResultsImageFileSections): void {
  try {
    localStorage.setItem(CC_RESULTS_IMAGE_SECTIONS_STORAGE_KEY, JSON.stringify({
      version: STORAGE_VERSION,
      format: sections.format,
      size: sections.size
    }));
  } catch {
    // Storage unavailable: the sections open as they default.
  }
}

/** `PNG` or `WebP`, as the download names and tooltips print it. */
export function ccResultsImageFormatLabel(format: FigureExportFormat): 'PNG' | 'WebP' {
  return format === 'webp' ? 'WebP' : 'PNG';
}
