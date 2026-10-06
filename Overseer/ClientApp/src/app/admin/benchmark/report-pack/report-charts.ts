import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportScope,
  ReportDocumentChartLayout,
  ReportDocumentChartLayoutFigure,
  ReportDocumentChartUpload
} from '../../../services/admin-benchmark.service';
import type { FigureAppearanceStyle } from '../model-comparison/figure-style';

/*
 * The charts a Report Pack document carries into its PDF and Word copies: which figures, where each
 * lands, how each is laid out on the page, the size they are drawn at, the hash of the settings that
 * shaped them, and the publisher that composes and uploads them one document at a time. The wizard
 * supplies the composer, so nothing here knows Chart.js.
 */

export type ReportChartFigureKey =
  'p1a-quality' | 'p1b-speed' | 'p1c-cost' | 'p2-profile' | 's1-quality-speed' | 's2-quality-cost' | 's3-speed-cost';

/** In placement order. */
export const REPORT_CHART_FIGURES: readonly { readonly key: ReportChartFigureKey; readonly title: string; readonly minModels: number }[] = [
  { key: 'p1a-quality', title: 'Intelligence', minModels: 2 },
  { key: 'p1b-speed', title: 'Speed', minModels: 2 },
  { key: 'p1c-cost', title: 'Cost', minModels: 2 },
  { key: 'p2-profile', title: 'Model profiles', minModels: 3 },
  { key: 's1-quality-speed', title: 'Intelligence against speed', minModels: 2 },
  { key: 's2-quality-cost', title: 'Intelligence against cost', minModels: 2 },
  { key: 's3-speed-cost', title: 'Speed against cost', minModels: 2 }
];

const FIGURE_KEYS: ReadonlySet<string> = new Set<string>(REPORT_CHART_FIGURES.map(figure => figure.key));

const KNOWN_AUDIENCES: readonly BenchmarkReportAudience[] = [
  BenchmarkReportAudience.ExecutiveSummary,
  BenchmarkReportAudience.TechnicalReport,
  BenchmarkReportAudience.InternalBrief
];

export function isReportChartFigureKey(value: string): value is ReportChartFigureKey {
  return FIGURE_KEYS.has(value);
}

// --- Placements ---

/** A per-model document, or one that covers several entries of a comparison as equals. */
export type ReportChartScope = 'model' | 'comparison';

/** Per document type, the section each figure lands in. */
export type ReportChartPlacementTable =
  Readonly<Partial<Record<BenchmarkReportAudience, Readonly<Partial<Record<ReportChartFigureKey, string>>>>>>;

function everyFigureIn(section: string): Readonly<Record<ReportChartFigureKey, string>> {
  return Object.freeze(Object.fromEntries(REPORT_CHART_FIGURES.map(figure => [figure.key, section])) as Record<ReportChartFigureKey, string>);
}

/** Per-model documents; mirrors the server's placement table. */
export const MODEL_SCOPE_CHART_PLACEMENTS: ReportChartPlacementTable = Object.freeze({
  [BenchmarkReportAudience.ExecutiveSummary]: everyFigureIn('How it compares'),
  [BenchmarkReportAudience.TechnicalReport]: Object.freeze({
    'p1a-quality': 'Results against peers → Quality',
    'p1b-speed': 'Results against peers → Speed',
    'p1c-cost': 'Results against peers → Cost',
    'p2-profile': 'Results against peers',
    's1-quality-speed': 'Speed and cost',
    's2-quality-cost': 'Speed and cost',
    's3-speed-cost': 'Speed and cost'
  }),
  [BenchmarkReportAudience.InternalBrief]: everyFigureIn('§3 Key figures')
});

/**
 * Comparison-scope documents; mirrors the comparison-scope chart anchors per document type in the
 * server's `BenchmarkReportPackRenderer`. A document type or figure missing here takes its
 * model-scope section.
 */
export const COMPARISON_SCOPE_CHART_PLACEMENTS: ReportChartPlacementTable = Object.freeze({
  [BenchmarkReportAudience.ExecutiveSummary]: everyFigureIn('How they compare'),
  [BenchmarkReportAudience.TechnicalReport]: Object.freeze({
    'p1a-quality': 'Results',
    'p1b-speed': 'Speed and cost frontier',
    'p1c-cost': 'Speed and cost frontier',
    'p2-profile': 'Dimension profiles',
    's1-quality-speed': 'Speed and cost frontier',
    's2-quality-cost': 'Speed and cost frontier',
    's3-speed-cost': 'Speed and cost frontier'
  }),
  [BenchmarkReportAudience.InternalBrief]: everyFigureIn('Models compared')
});

export const REPORT_CHART_PLACEMENTS: Readonly<Record<ReportChartScope, ReportChartPlacementTable>> = Object.freeze({
  model: MODEL_SCOPE_CHART_PLACEMENTS,
  comparison: COMPARISON_SCOPE_CHART_PLACEMENTS
});

/** The section a figure lands in for a document type: 'How it compares', 'Results against peers → Quality', … */
export function reportChartPlacementLabel(
  audience: BenchmarkReportAudience,
  key: ReportChartFigureKey,
  scope: ReportChartScope = 'model'
): string {
  const label = REPORT_CHART_PLACEMENTS[scope][audience]?.[key];
  if (label !== undefined) {
    return label;
  }
  return scope === 'model' ? '' : reportChartPlacementLabel(audience, key, 'model');
}

// --- The figure selection ---

/** Per document type, the figures to include, in placement order. A missing audience means none. */
export type ReportChartSelection = Readonly<Partial<Record<BenchmarkReportAudience, readonly ReportChartFigureKey[]>>>;

export const DEFAULT_CHART_SELECTION: ReportChartSelection = Object.freeze({
  [BenchmarkReportAudience.ExecutiveSummary]: Object.freeze(['p1a-quality', 's2-quality-cost'] as ReportChartFigureKey[]),
  [BenchmarkReportAudience.TechnicalReport]: Object.freeze(REPORT_CHART_FIGURES.map(figure => figure.key)),
  [BenchmarkReportAudience.InternalBrief]: Object.freeze(['p1a-quality', 'p1b-speed', 'p1c-cost'] as ReportChartFigureKey[])
});

/** Dedupes, puts in placement order and drops unknown keys and audiences. An audience present with no figures stays, as none. */
export function normalizeChartSelection(selection: ReportChartSelection): ReportChartSelection {
  const normalized: Partial<Record<BenchmarkReportAudience, readonly ReportChartFigureKey[]>> = {};
  if (!selection || typeof selection !== 'object') {
    return normalized;
  }
  const source = selection as Record<string | number, unknown>;
  for (const audience of KNOWN_AUDIENCES) {
    const keys = source[audience];
    if (!Array.isArray(keys)) {
      continue;
    }
    const known = new Set(keys.filter((key): key is ReportChartFigureKey => typeof key === 'string' && isReportChartFigureKey(key)));
    normalized[audience] = REPORT_CHART_FIGURES.map(figure => figure.key).filter(key => known.has(key));
  }
  return normalized;
}

// --- Document charts sized in points ---

/** The resolution document charts are written at. */
export const DOCUMENT_CHART_DPI = 300;

/** The bar value labels' size in layout px, which the label size in points is mapped onto. */
export const BASE_LABEL_PX = 11;

/** The text column of a document page, in points. */
export const DOCUMENT_TEXT_COLUMN_PT: { readonly a4: 481.9; readonly letter: 498.6 } = Object.freeze({ a4: 481.9, letter: 498.6 } as const);

/** The column charts are composed for: A4's, the narrower, so a chart printed on Letter is never shrunk. */
export const DOCUMENT_CHART_COLUMN_PT = DOCUMENT_TEXT_COLUMN_PT.a4;

/**
 * The narrowest content column a document chart is laid out in, in layout px: passed to
 * `resolveFigureLayout` as the request's `minContentWidth`, in place of the 360 px step 2 uses.
 */
export const DOCUMENT_MIN_CONTENT_WIDTH = 260;

/** The composer's padding on each side of the content column, in layout px. */
const FIGURE_PADDING_PX = 20;

/** The narrowest layout a document chart composes in: its content column and the padding on both sides. */
export const DOCUMENT_CHART_MIN_LAYOUT_WIDTH = DOCUMENT_MIN_CONTENT_WIDTH + FIGURE_PADDING_PX * 2;

/**
 * The least plot height a document chart is composed with, in layout px. A chart whose heading, key
 * and notes would leave less at its aspect ratio is made taller instead; the composer's own floor is 160.
 */
export const DOCUMENT_MIN_PLOT_HEIGHT = 200;

/** What `resolveFigureLayout` lays a composition out against: 960 × 540 layout px at 100 % text. */
const FIGURE_LAYOUT_WIDTH = 960;
const FIGURE_LAYOUT_HEIGHT = 540;

/** One document chart's size on the page, its bitmap, its layout box and the text scale that gives it. */
export interface DocumentChartLayout {
  readonly widthPt: number;
  readonly heightPt: number;
  readonly labelPt: number;
  /** The bitmap, at {@link DOCUMENT_CHART_DPI}. */
  readonly pxWidth: number;
  readonly pxHeight: number;
  /** The composition box in layout px: a bar label of {@link BASE_LABEL_PX} prints at `labelPt`. */
  readonly layoutWidth: number;
  readonly layoutHeight: number;
  /** The text scale `resolveFigureLayout` takes to compose `pxWidth` in `layoutWidth`. */
  readonly textScale: number;
}

/**
 * A chart `widthPt` × `heightPt` on the page whose bar labels print at `labelPt`:
 * `pxWidth = round(widthPt × 300 / 72)`, `layoutWidth = widthPt × 11 / labelPt` and
 * `textScale = (pxWidth / layoutWidth) / min(pxWidth / 960, pxHeight / 540)`.
 */
export function documentChartLayout(widthPt: number, heightPt: number, labelPt: number): DocumentChartLayout {
  const pxWidth = Math.round(widthPt * DOCUMENT_CHART_DPI / 72);
  const pxHeight = Math.round(heightPt * DOCUMENT_CHART_DPI / 72);
  const layoutWidth = widthPt * BASE_LABEL_PX / labelPt;
  const textScale = (pxWidth / layoutWidth) / Math.min(pxWidth / FIGURE_LAYOUT_WIDTH, pxHeight / FIGURE_LAYOUT_HEIGHT);
  return {
    widthPt,
    heightPt,
    labelPt,
    pxWidth,
    pxHeight,
    layoutWidth,
    layoutHeight: layoutWidth * pxHeight / pxWidth,
    textScale
  };
}

// --- Layout settings per document type ---

export type ReportChartOrientationSetting = 'asInStep2' | 'vertical' | 'horizontal';
export type ReportChartWidth = 'full' | 'twoThirds' | 'half';
export type ReportChartHeading = 'none' | 'title' | 'titleAndBadges';
export type ReportChartTheme = 'lightPrint' | 'asInStep2';

export const REPORT_CHART_ORIENTATIONS: readonly { readonly value: ReportChartOrientationSetting; readonly label: string }[] = [
  { value: 'asInStep2', label: 'As in step 2' },
  { value: 'vertical', label: 'Vertical' },
  { value: 'horizontal', label: 'Horizontal' }
];

export const REPORT_CHART_WIDTHS: readonly {
  readonly value: ReportChartWidth; readonly label: string; readonly share: number; readonly phrase: string;
}[] = [
  { value: 'full', label: 'Full column', share: 1, phrase: 'A full-column chart' },
  { value: 'twoThirds', label: 'Two thirds', share: 2 / 3, phrase: 'A two-thirds-width chart' },
  { value: 'half', label: 'Half', share: 1 / 2, phrase: 'A half-width chart' }
];

export const REPORT_CHART_LABEL_SIZES_PT: readonly number[] = [7, 7.5, 8, 8.5, 9, 10];

/** Of the page height. */
export const REPORT_CHART_MAX_HEIGHT_PERCENTS: readonly number[] = [40, 50, 60];

export const REPORT_CHART_HEADINGS: readonly { readonly value: ReportChartHeading; readonly label: string }[] = [
  { value: 'none', label: 'None — the caption names it' },
  { value: 'title', label: 'Title' },
  { value: 'titleAndBadges', label: 'Title and badges' }
];

export const REPORT_CHART_THEMES: readonly { readonly value: ReportChartTheme; readonly label: string }[] = [
  { value: 'lightPrint', label: 'Light, for print' },
  { value: 'asInStep2', label: 'As in step 2' }
];

/** How one document type's charts are drawn and placed. */
export interface ReportChartDocumentLayout {
  /** *As in step 2* takes step 2's choice, its *Automatic* resolved at the chart's own layout width. */
  readonly orientation: ReportChartOrientationSetting;
  /** Each figure's share of the text column; a figure missing here is full column. */
  readonly widths: Readonly<Partial<Record<ReportChartFigureKey, ReportChartWidth>>>;
  /** Consecutive half-width figures in the same section print as a row of two. */
  readonly sideBySide: boolean;
  /** The bar labels' printed size, one of {@link REPORT_CHART_LABEL_SIZES_PT}. */
  readonly labelPt: number;
  /** The tallest a figure prints, as a percentage of the page; one of {@link REPORT_CHART_MAX_HEIGHT_PERCENTS}. */
  readonly maxHeightPercent: number;
  readonly heading: ReportChartHeading;
  /** The GnollBench logo, in step 2's variant and height. */
  readonly logo: boolean;
  /** *Light, for print* replaces step 2's theme, background, text and border colors, and nothing else. */
  readonly theme: ReportChartTheme;
}

export const DEFAULT_DOCUMENT_CHART_LAYOUT: ReportChartDocumentLayout = Object.freeze({
  orientation: 'asInStep2',
  widths: Object.freeze({}),
  sideBySide: true,
  labelPt: 8,
  maxHeightPercent: 60,
  heading: 'none',
  logo: false,
  theme: 'lightPrint'
} as ReportChartDocumentLayout);

/** Per document type. */
export type ReportChartLayoutSettings = Readonly<Partial<Record<BenchmarkReportAudience, ReportChartDocumentLayout>>>;

export const DEFAULT_CHART_LAYOUT_SETTINGS: ReportChartLayoutSettings = Object.freeze({
  [BenchmarkReportAudience.ExecutiveSummary]: DEFAULT_DOCUMENT_CHART_LAYOUT,
  [BenchmarkReportAudience.TechnicalReport]: DEFAULT_DOCUMENT_CHART_LAYOUT,
  [BenchmarkReportAudience.InternalBrief]: DEFAULT_DOCUMENT_CHART_LAYOUT
});

/** Everything step 3 remembers about the document charts: the figures and their layout, per document type. */
export interface ReportChartSettings {
  readonly selection: ReportChartSelection;
  readonly layout: ReportChartLayoutSettings;
}

export const DEFAULT_CHART_SETTINGS: ReportChartSettings = Object.freeze({
  selection: DEFAULT_CHART_SELECTION,
  layout: DEFAULT_CHART_LAYOUT_SETTINGS
});

function widthOption(width: ReportChartWidth): (typeof REPORT_CHART_WIDTHS)[number] {
  return REPORT_CHART_WIDTHS.find(option => option.value === width) ?? REPORT_CHART_WIDTHS[0];
}

/** `8`, `7.5`. */
export function formatPoints(points: number): string {
  return Number.isInteger(points) ? String(points) : points.toFixed(1);
}

function fitsWidth(width: ReportChartWidth, labelPt: number, columnPt: number): boolean {
  return columnPt * widthOption(width).share * BASE_LABEL_PX / labelPt >= DOCUMENT_CHART_MIN_LAYOUT_WIDTH;
}

/**
 * Why a figure of `width` cannot be composed with `labelPt` labels, or null when it can: its layout
 * would be narrower than {@link DOCUMENT_CHART_MIN_LAYOUT_WIDTH}, which `resolveFigureLayout` refuses
 * given {@link DOCUMENT_MIN_CONTENT_WIDTH}. Half width fits up to 8.5 pt labels; full and two thirds
 * fit every offered size.
 */
export function documentChartRefusal(width: ReportChartWidth, labelPt: number, columnPt: number = DOCUMENT_CHART_COLUMN_PT): string | null {
  if (fitsWidth(width, labelPt, columnPt)) {
    return null;
  }
  const option = widthOption(width);
  const layoutWidth = Math.round(columnPt * option.share * BASE_LABEL_PX / labelPt);
  const fitting = REPORT_CHART_LABEL_SIZES_PT.filter(size => fitsWidth(width, size, columnPt));
  const remedy = fitting.length > 0
    ? `Choose ${formatPoints(fitting[fitting.length - 1])} pt labels or smaller, or a wider chart.`
    : 'Choose a wider chart.';
  return `${option.phrase} with ${formatPoints(labelPt)} pt labels would be ${layoutWidth} layout px wide; `
    + `a chart needs at least ${DOCUMENT_CHART_MIN_LAYOUT_WIDTH}. ${remedy}`;
}

/** Why `labelPt` cannot be chosen while the document's selected figures have the widths they have, or null. */
export function documentLabelSizeRefusal(
  layout: ReportChartDocumentLayout,
  figures: readonly ReportChartFigureKey[],
  labelPt: number,
  columnPt: number = DOCUMENT_CHART_COLUMN_PT
): string | null {
  const narrowest = [...REPORT_CHART_WIDTHS].reverse()
    .find(option => figures.some(key => (layout.widths[key] ?? 'full') === option.value));
  return narrowest ? documentChartRefusal(narrowest.value, labelPt, columnPt) : null;
}

/** The width a figure is composed and placed at: its own, or full column where its own is refused. */
export function documentChartWidth(
  layout: ReportChartDocumentLayout,
  key: ReportChartFigureKey,
  columnPt: number = DOCUMENT_CHART_COLUMN_PT
): ReportChartWidth {
  const width = layout.widths[key] ?? 'full';
  return documentChartRefusal(width, layout.labelPt, columnPt) === null ? width : 'full';
}

/**
 * Height over width per figure width: the narrower the figure, the taller, so its heading, key and
 * notes, which keep their size, still leave room for the plot.
 */
const DOCUMENT_CHART_ASPECTS: Readonly<Record<ReportChartWidth, { readonly chart: number; readonly profile: number }>> = {
  full: { chart: 10 / 16, profile: 3 / 4 },
  twoThirds: { chart: 3 / 4, profile: 1 },
  half: { chart: 1, profile: 5 / 4 }
};

/**
 * One figure at its width and label size: 16:10 full column, 4:3 at two thirds and square at half
 * width for bars and scatters, one step taller for the profile. `minLayoutHeight` makes it taller
 * where its chrome needs more room than that aspect gives.
 */
export function documentFigureLayout(
  key: ReportChartFigureKey,
  width: ReportChartWidth = 'full',
  labelPt: number = DEFAULT_DOCUMENT_CHART_LAYOUT.labelPt,
  columnPt: number = DOCUMENT_CHART_COLUMN_PT,
  minLayoutHeight = 0
): DocumentChartLayout {
  const widthPt = columnPt * widthOption(width).share;
  const aspects = DOCUMENT_CHART_ASPECTS[width];
  const aspect = key === 'p2-profile' ? aspects.profile : aspects.chart;
  const layoutWidth = widthPt * BASE_LABEL_PX / labelPt;
  const heightPt = Math.max(widthPt * aspect, widthPt * minLayoutHeight / layoutWidth);
  return documentChartLayout(widthPt, heightPt, labelPt);
}

/** The bitmap a figure is written at, by default full column at 8 pt labels. */
export function documentChartSize(
  key: ReportChartFigureKey,
  width: ReportChartWidth = 'full',
  labelPt: number = DEFAULT_DOCUMENT_CHART_LAYOUT.labelPt
): { widthPx: number; heightPx: number } {
  const layout = documentFigureLayout(key, width, labelPt);
  return { widthPx: layout.pxWidth, heightPx: layout.pxHeight };
}

function oneOf<T>(value: unknown, allowed: readonly T[], fallback: T): T {
  return allowed.includes(value as T) ? value as T : fallback;
}

/** Every field checked against its options, defaulted where unknown; full-column and refused widths are left out. */
export function normalizeDocumentChartLayout(value: unknown): ReportChartDocumentLayout {
  const source = (value && typeof value === 'object' ? value : {}) as Record<string, unknown>;
  const d = DEFAULT_DOCUMENT_CHART_LAYOUT;
  const labelPt = oneOf(source['labelPt'], REPORT_CHART_LABEL_SIZES_PT, d.labelPt);
  const widths: Partial<Record<ReportChartFigureKey, ReportChartWidth>> = {};
  const storedWidths = (source['widths'] && typeof source['widths'] === 'object' ? source['widths'] : {}) as Record<string, unknown>;
  for (const figure of REPORT_CHART_FIGURES) {
    const width = oneOf<ReportChartWidth | null>(storedWidths[figure.key], ['twoThirds', 'half'], null);
    if (width !== null && documentChartRefusal(width, labelPt) === null) {
      widths[figure.key] = width;
    }
  }
  return {
    orientation: oneOf(source['orientation'], REPORT_CHART_ORIENTATIONS.map(option => option.value), d.orientation),
    widths,
    sideBySide: typeof source['sideBySide'] === 'boolean' ? source['sideBySide'] : d.sideBySide,
    labelPt,
    maxHeightPercent: oneOf(source['maxHeightPercent'], REPORT_CHART_MAX_HEIGHT_PERCENTS, d.maxHeightPercent),
    heading: oneOf(source['heading'], REPORT_CHART_HEADINGS.map(option => option.value), d.heading),
    logo: typeof source['logo'] === 'boolean' ? source['logo'] : d.logo,
    theme: oneOf(source['theme'], REPORT_CHART_THEMES.map(option => option.value), d.theme)
  };
}

/** Every document type, each normalized; a missing one takes the defaults. */
export function normalizeChartLayoutSettings(layout: ReportChartLayoutSettings | null | undefined): ReportChartLayoutSettings {
  const source = (layout && typeof layout === 'object' ? layout : {}) as Record<string | number, unknown>;
  const normalized: Partial<Record<BenchmarkReportAudience, ReportChartDocumentLayout>> = {};
  for (const audience of KNOWN_AUDIENCES) {
    normalized[audience] = normalizeDocumentChartLayout(source[audience]);
  }
  return normalized;
}

/** One document type's layout; the defaults for an unknown type or none. */
export function documentChartLayoutFor(
  layout: ReportChartLayoutSettings | null | undefined,
  audience: BenchmarkReportAudience | null | undefined
): ReportChartDocumentLayout {
  return (audience !== null && audience !== undefined ? layout?.[audience] : undefined) ?? DEFAULT_DOCUMENT_CHART_LAYOUT;
}

/**
 * The layout the server places a document's charts with, for the figures it has, in placement
 * order: each figure's width share, the row two consecutive half-width figures in one section share
 * while side by side is on, and the maximum height.
 */
export function reportDocumentChartLayout(
  audience: BenchmarkReportAudience,
  figureKeys: readonly ReportChartFigureKey[],
  layout: ReportChartDocumentLayout,
  scope: ReportChartScope = 'model',
  columnPt: number = DOCUMENT_CHART_COLUMN_PT
): ReportDocumentChartLayout {
  const keys = REPORT_CHART_FIGURES.map(figure => figure.key).filter(key => figureKeys.includes(key));
  const figures: ReportDocumentChartLayoutFigure[] = [];
  let rowGroup = 0;
  for (let i = 0; i < keys.length; i++) {
    const key = keys[i];
    const width = documentChartWidth(layout, key, columnPt);
    const next = keys[i + 1];
    if (layout.sideBySide && width === 'half' && next !== undefined && documentChartWidth(layout, next, columnPt) === 'half'
        && reportChartPlacementLabel(audience, key, scope) === reportChartPlacementLabel(audience, next, scope)) {
      rowGroup++;
      figures.push({ key, widthShare: 1 / 2, rowGroup }, { key: next, widthShare: 1 / 2, rowGroup });
      i++;
      continue;
    }
    figures.push({ key, widthShare: widthOption(width).share, rowGroup: null });
  }
  return { version: 1, figures, maxHeightShare: layout.maxHeightPercent / 100 };
}

/** Step 2's appearance as *Light, for print* draws it: the light theme on its own background, with the theme's text and border colors. */
export function printFigureAppearance(appearance: FigureAppearanceStyle): FigureAppearanceStyle {
  return { ...appearance, theme: 'light', background: 'theme', headingColor: null, textColor: null, borderColor: null };
}

// --- Storage ---

export const REPORT_CHART_STORAGE_KEY = 'overseer.benchmark.reportCharts';

/** The stored record's version. Version 1 held the selection alone and is read with the default layout. */
const STORED_SETTINGS_VERSION = 2;

/** The record in localStorage, try/catch; DEFAULT_CHART_SETTINGS when absent, unreadable or of an unknown version. */
export function readStoredChartSettings(): ReportChartSettings {
  try {
    const raw = localStorage.getItem(REPORT_CHART_STORAGE_KEY);
    if (!raw) {
      return DEFAULT_CHART_SETTINGS;
    }
    const parsed = JSON.parse(raw) as { version?: unknown; selection?: unknown; layout?: unknown } | null;
    if (!parsed || (parsed.version !== 1 && parsed.version !== STORED_SETTINGS_VERSION)
        || !parsed.selection || typeof parsed.selection !== 'object') {
      return DEFAULT_CHART_SETTINGS;
    }
    return {
      selection: normalizeChartSelection(parsed.selection as ReportChartSelection),
      layout: parsed.version === 1
        ? DEFAULT_CHART_LAYOUT_SETTINGS
        : normalizeChartLayoutSettings(parsed.layout as ReportChartLayoutSettings)
    };
  } catch {
    return DEFAULT_CHART_SETTINGS;
  }
}

export function storeChartSettings(settings: ReportChartSettings): void {
  try {
    localStorage.setItem(REPORT_CHART_STORAGE_KEY, JSON.stringify({
      version: STORED_SETTINGS_VERSION,
      selection: normalizeChartSelection(settings.selection),
      layout: normalizeChartLayoutSettings(settings.layout)
    }));
  } catch {
    // Storage unavailable: the settings are simply not remembered.
  }
}

/** The stored figure selection alone; DEFAULT_CHART_SELECTION when absent or unreadable. */
export function readStoredChartSelection(): ReportChartSelection {
  return readStoredChartSettings().selection;
}

/** Stores the selection, keeping the stored layout. */
export function storeChartSelection(selection: ReportChartSelection): void {
  storeChartSettings({ selection, layout: readStoredChartSettings().layout });
}

// --- The settings hash ---

/** What shapes the images; hashed as canonical JSON (object keys sorted recursively). */
export interface ReportChartSettingsInput {
  readonly figureStyle: unknown;
  /** {@link documentChartHashLayout}: the composition constants and every document type's layout. */
  readonly layout: unknown;
  readonly show: unknown;
  readonly highlight: unknown;
  readonly order: unknown;
  readonly measures: unknown;
  readonly pricingBasis: string;
  readonly computedAtUtc: string | null;
}

/** The layout part of the settings hash. */
export function documentChartHashLayout(layout: ReportChartLayoutSettings): unknown {
  return {
    columnPt: DOCUMENT_CHART_COLUMN_PT,
    dpi: DOCUMENT_CHART_DPI,
    baseLabelPx: BASE_LABEL_PX,
    format: 'png',
    documents: normalizeChartLayoutSettings(layout)
  };
}

function canonicalPart(value: unknown): string | undefined {
  if (value === null) {
    return 'null';
  }
  switch (typeof value) {
    case 'string':
      return JSON.stringify(value);
    case 'number':
      return Number.isFinite(value) ? JSON.stringify(value) : 'null';
    case 'boolean':
      return value ? 'true' : 'false';
    case 'bigint':
      return JSON.stringify(value.toString());
    case 'object':
      break;
    default:
      // undefined, functions and symbols are left out, as JSON.stringify leaves them out.
      return undefined;
  }
  const toJson = (value as { toJSON?: unknown }).toJSON;
  if (typeof toJson === 'function') {
    return canonicalPart((toJson as () => unknown).call(value));
  }
  if (Array.isArray(value)) {
    return `[${value.map(item => canonicalPart(item) ?? 'null').join(',')}]`;
  }
  const record = value as Record<string, unknown>;
  const parts: string[] = [];
  for (const key of Object.keys(record).sort()) {
    const part = canonicalPart(record[key]);
    if (part !== undefined) {
      parts.push(`${JSON.stringify(key)}:${part}`);
    }
  }
  return `{${parts.join(',')}}`;
}

/** JSON with every object's keys sorted, at every depth, and no whitespace; the same value always gives the same text. */
export function canonicalJson(value: unknown): string {
  return canonicalPart(value) ?? 'null';
}

/** SHA-256 via crypto.subtle over canonicalJson(input); 64 lowercase hex characters. */
export async function chartSettingsHash(input: ReportChartSettingsInput): Promise<string> {
  const subtle = typeof crypto !== 'undefined' ? crypto.subtle : undefined;
  if (!subtle) {
    throw new Error('SHA-256 is not available: crypto.subtle needs a secure context.');
  }
  const digest = await subtle.digest('SHA-256', new TextEncoder().encode(canonicalJson(input)));
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
}

// --- Publishing ---

export const CHART_STORAGE_NOT_CONFIGURED = 'Chart storage is not configured';

/**
 * Which copy of a document a chart is drawn for. `coveredKeys` is present for a comparison-scope
 * document: the chart plots those entries only, and the anonymized copy letters every one of them.
 */
export type ReportChartVariant =
  | { readonly kind: 'named'; readonly coveredKeys?: readonly string[] }
  | {
    readonly kind: 'anonymized'; readonly subjectKey: string; readonly letters: Readonly<Record<string, string>>;
    readonly coveredKeys?: readonly string[];
  };

export interface ComposedReportChart {
  readonly png: Blob; readonly widthPx: number; readonly heightPx: number;
  readonly title: string; readonly caption: string; readonly altText: string;
}

/** Draws one figure of one copy, laid out for the document type's settings; the defaults without one. */
export type ReportChartComposer =
  (key: ReportChartFigureKey, variant: ReportChartVariant, audience?: BenchmarkReportAudience) => Promise<ComposedReportChart>;

/** A document to chart. */
export interface ReportChartTarget {
  readonly documentId: number;
  readonly audience: BenchmarkReportAudience;
  readonly subjectKey: string;
  /**
   * Entry key → letter, from the document list (`peerLetters`): the peers of a per-model document,
   * every covered entry of a comparison-scope one. Without letters no anonymized variant is drawn.
   */
  readonly peerLetters: Readonly<Record<string, string>>;
  readonly label: string;
  /** The entries a comparison-scope document covers; absent for a per-model document. */
  readonly coveredKeys?: readonly string[];
}

/** A comparison-scope document's subject key: `comparison:12`, or `comparison:12/<covered set>` for a subset. */
export function isComparisonScopeSubjectKey(subjectKey: string): boolean {
  return subjectKey.startsWith('comparison:');
}

/** The entries a target's charts plot, or null for a per-model document, which plots step 2's Show. */
export function reportChartCoveredKeys(target: ReportChartTarget): readonly string[] | null {
  if (target.coveredKeys) {
    return target.coveredKeys;
  }
  return isComparisonScopeSubjectKey(target.subjectKey) ? Object.keys(target.peerLetters ?? {}) : null;
}

/** A listed document as a chart target; a comparison-scope one carries the entries it covers. */
export function reportChartTargetFor(doc: BenchmarkReportDocumentListItemDto, label: string = doc.title): ReportChartTarget {
  const target: ReportChartTarget = {
    documentId: doc.id,
    audience: doc.audience,
    subjectKey: doc.subjectKey,
    peerLetters: doc.peerLetters ?? {},
    label
  };
  if (doc.scope !== BenchmarkReportScope.Comparison && !isComparisonScopeSubjectKey(doc.subjectKey)) {
    return target;
  }
  const covered = (doc.coveredModels ?? []).map(model => model.entryKey);
  return { ...target, coveredKeys: covered.length > 0 ? covered : Object.keys(doc.peerLetters ?? {}) };
}

export interface ReportChartPublishProgress { readonly done: number; readonly total: number; readonly step: string; readonly documentId: number | null; }

export interface ReportChartPublishResult {
  readonly published: readonly { readonly documentId: number; readonly chartCount: number }[];
  readonly failed: readonly { readonly documentId: number; readonly message: string }[];
  /** Targets whose selection for their audience was empty; nothing was uploaded for them. */
  readonly skipped: readonly number[];
  readonly canceled: boolean;
  /** The server's message, once, when a 400 said chart storage is not configured; the publish stopped there. */
  readonly storageNotConfigured: string | null;
}

/** Per-document chart state shown in the Reports panel's progress list. */
export type ReportChartRowStatus =
  | { readonly state: 'attaching' }
  | { readonly state: 'done'; readonly count: number }
  | { readonly state: 'failed'; readonly message: string }
  | { readonly state: 'skipped'; readonly reason: string };

function errorText(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const body = error.error;
    if (typeof body === 'string' && body.trim()) {
      return body.trim();
    }
    if (body && typeof body === 'object') {
      const message = (body as { error?: unknown }).error ?? (body as { message?: unknown }).message;
      if (typeof message === 'string' && message.trim()) {
        return message.trim();
      }
    }
    return error.status > 0 ? `HTTP ${error.status}${error.statusText ? ` ${error.statusText}` : ''}` : 'The server could not be reached.';
  }
  if (error instanceof Error && error.message) {
    return error.message;
  }
  return typeof error === 'string' && error ? error : 'Unknown error';
}

async function blobToBase64(blob: Blob): Promise<string> {
  const bytes = new Uint8Array(await blob.arrayBuffer());
  const chunk = 0x8000;
  let binary = '';
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...Array.from(bytes.subarray(i, i + chunk)));
  }
  return btoa(binary);
}

function variantsFor(target: ReportChartTarget): ReportChartVariant[] {
  const covered = reportChartCoveredKeys(target);
  const scoped = covered ? { coveredKeys: covered } : {};
  const variants: ReportChartVariant[] = [{ kind: 'named', ...scoped }];
  if (target.peerLetters && Object.keys(target.peerLetters).length > 0) {
    variants.push({ kind: 'anonymized', subjectKey: target.subjectKey, letters: target.peerLetters, ...scoped });
  }
  return variants;
}

function figureTitle(key: ReportChartFigureKey): string {
  return REPORT_CHART_FIGURES.find(figure => figure.key === key)?.title ?? key;
}

/**
 * Composes and uploads the charts of Report Pack documents, one document at a time. One publish
 * runs at a time per instance; `cancel()` stops it after the document in flight.
 */
export class ReportChartPublisher {
  private runningNow = false;
  private cancelRequested = false;

  constructor(private readonly service: AdminBenchmarkService) {}

  get running(): boolean {
    return this.runningNow;
  }

  /**
   * One document at a time: for each target, composes every selected figure for its audience, named and
   * (when the target has peer letters) anonymized, converts each Blob to base64, and PUTs the whole set
   * with the layout `layout` gives its audience, for the figures drawn; without `layout` the server's
   * default placement applies. A figure the composer rejects is left out of the set (its error is
   * recorded in the step text); a failed PUT is recorded and the rest continue; a 400 containing
   * CHART_STORAGE_NOT_CONFIGURED stops everything. An empty set for a target (every figure failed to
   * compose) is a failure for that target, not a PUT.
   */
  async publish(
    targets: readonly ReportChartTarget[],
    selection: ReportChartSelection,
    compose: ReportChartComposer,
    settingsHash: string,
    onProgress?: (progress: ReportChartPublishProgress) => void,
    layout?: ReportChartLayoutSettings | null
  ): Promise<ReportChartPublishResult> {
    if (this.runningNow) {
      throw new Error('A chart publish is already running.');
    }
    this.runningNow = true;
    this.cancelRequested = false;

    const normalized = normalizeChartSelection(selection);
    const published: { documentId: number; chartCount: number }[] = [];
    const failed: { documentId: number; message: string }[] = [];
    const skipped: number[] = [];
    let storageNotConfigured: string | null = null;
    let canceled = false;

    // One unit per image to compose and one per upload.
    const total = targets.reduce((sum, target) => {
      const figures = normalized[target.audience]?.length ?? 0;
      return figures === 0 ? sum : sum + figures * variantsFor(target).length + 1;
    }, 0);
    let done = 0;
    const report = (step: string, documentId: number | null): void => {
      onProgress?.({ done, total, step, documentId });
    };

    try {
      for (const target of targets) {
        if (this.cancelRequested) {
          canceled = true;
          break;
        }
        const figures = normalized[target.audience] ?? [];
        if (figures.length === 0) {
          skipped.push(target.documentId);
          continue;
        }

        const uploads: ReportDocumentChartUpload[] = [];
        const composeErrors: string[] = [];
        for (const key of figures) {
          for (const variant of variantsFor(target)) {
            const what = `${figureTitle(key)} (${variant.kind})`;
            report(`Drawing ${what} for ${target.label}`, target.documentId);
            try {
              const chart = await compose(key, variant, target.audience);
              uploads.push({
                figureKey: key,
                naming: variant.kind,
                title: chart.title,
                caption: chart.caption,
                altText: chart.altText,
                settingsHash,
                pngBase64: await blobToBase64(chart.png)
              });
              done++;
            } catch (error) {
              done++;
              const message = errorText(error);
              composeErrors.push(`${what}: ${message}`);
              report(`Could not draw ${what} for ${target.label}: ${message}`, target.documentId);
            }
          }
        }

        if (uploads.length === 0) {
          done++;
          failed.push({
            documentId: target.documentId,
            message: `No chart could be drawn${composeErrors.length > 0 ? `: ${composeErrors[0]}` : '.'}`
          });
          continue;
        }

        const placement = layout
          ? reportDocumentChartLayout(
            target.audience,
            uploads.map(upload => upload.figureKey as ReportChartFigureKey),
            documentChartLayoutFor(layout, target.audience),
            reportChartCoveredKeys(target) ? 'comparison' : 'model')
          : null;
        report(`Attaching ${uploads.length} ${uploads.length === 1 ? 'chart' : 'charts'} to ${target.label}`, target.documentId);
        try {
          const summary = await firstValueFrom(this.service.putReportDocumentCharts(target.documentId, uploads, placement));
          done++;
          published.push({ documentId: target.documentId, chartCount: summary?.chartCount ?? uploads.length });
        } catch (error) {
          done++;
          const message = errorText(error);
          if (error instanceof HttpErrorResponse && error.status === 400 && message.includes(CHART_STORAGE_NOT_CONFIGURED)) {
            // Reported once, not as one failure per document; nothing after this target is attempted.
            storageNotConfigured = message;
            break;
          }
          failed.push({ documentId: target.documentId, message });
        }
      }
    } finally {
      this.runningNow = false;
      this.cancelRequested = false;
    }

    const step = storageNotConfigured
      ?? (canceled ? 'Canceled' : `Charts attached to ${published.length} of ${targets.length - skipped.length} ${targets.length - skipped.length === 1 ? 'document' : 'documents'}`);
    report(step, null);
    return { published, failed, skipped, canceled, storageNotConfigured };
  }

  /** Stops after the document in flight; the result reports canceled: true. */
  cancel(): void {
    if (this.runningNow) {
      this.cancelRequested = true;
    }
  }
}
