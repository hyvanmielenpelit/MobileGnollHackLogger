/**
 * Which charts each chat consistency document carries into its PDF and Word copies, and how they are
 * laid out: the figures and document types `app-report-chart-picker` shows in step 5, their defaults,
 * their normalizers and their storage per browser, and the layout the server places a document's
 * charts by. Pure apart from the storage helpers, which wrap `localStorage` in try/catch.
 */

import { BenchmarkReportAudience, ReportDocumentChartLayout, ReportDocumentChartLayoutFigure } from '../../../services/admin-benchmark.service';
import {
  ReportChartLayoutField,
  ReportChartPickerAudienceOption,
  ReportChartPickerFigure,
  normalizePickerDocumentLayout,
  normalizePickerSelection
} from '../report-pack/report-chart-picker.component';
import {
  DEFAULT_DOCUMENT_CHART_LAYOUT,
  REPORT_CHART_MAX_HEIGHT_PERCENTS,
  REPORT_CHART_WIDTHS,
  ReportChartDocumentLayout,
  ReportChartLayoutSettings,
  ReportChartSelection,
  ReportChartWidth,
  documentChartWidth
} from '../report-pack/report-charts';
import { CC_FIGURE_ENDPOINTS, CC_REPORT_FIGURES, ccFigureTitle } from './chat-consistency-charts';
import { CC_REPORT_AUDIENCES, CC_REPORT_FIGURE_KEYS, CcEndpointResult, CcReportFigureKey } from './chat-consistency.models';

const { ExecutiveSummary, TechnicalReport, InternalBrief, ProviderIssueReport } = BenchmarkReportAudience;

/** Per document type, the chat consistency figures to include, in document order. */
export type CcReportChartSelection = ReportChartSelection<CcReportFigureKey>;

/** One document type's chart layout. */
export type CcDocumentChartLayout = ReportChartDocumentLayout<CcReportFigureKey>;

/** Per document type. */
export type CcReportChartLayout = ReportChartLayoutSettings<CcReportFigureKey>;

/** Everything step 5 remembers about the document charts. */
export interface CcReportChartSettings {
  readonly selection: CcReportChartSelection;
  readonly layout: CcReportChartLayout;
}

/** The four document types, in the Write step's order. */
export const CC_REPORT_CHART_AUDIENCE_KEYS: readonly BenchmarkReportAudience[] = CC_REPORT_AUDIENCES.map(entry => entry.audience);

const SHORT_LABELS: Readonly<Partial<Record<BenchmarkReportAudience, string>>> = {
  [ExecutiveSummary]: 'Executive',
  [TechnicalReport]: 'Researchers',
  [InternalBrief]: 'Internal',
  [ProviderIssueReport]: 'Provider'
};

/** The picker's document types: the Write step's labels, each with its segment's short one. */
export const CC_REPORT_CHART_PICKER_AUDIENCES: readonly ReportChartPickerAudienceOption[] = CC_REPORT_AUDIENCES.map(entry => ({
  audience: entry.audience,
  label: entry.label,
  shortLabel: SHORT_LABELS[entry.audience] ?? entry.label
}));

/** The section the server prints a figure in: after the verdicts, or after the Overseer events. */
export type CcReportChartAnchor = 'Results' | 'Events';

/**
 * Where a figure lands: every figure after the verdict table, but the overview after the events
 * table, except in the Executive Summary, which has no events table. Mirrors the server's anchors.
 */
export function ccReportChartAnchor(audience: BenchmarkReportAudience, key: CcReportFigureKey): CcReportChartAnchor {
  return key === 'cc4-timeline' && audience !== ExecutiveSummary ? 'Events' : 'Results';
}

/** The figures the picker lists, with the section each lands in per document type. */
export const CC_REPORT_CHART_PICKER_FIGURES: readonly ReportChartPickerFigure<CcReportFigureKey>[] = CC_REPORT_FIGURE_KEYS.map(key => ({
  key,
  title: ccFigureTitle(CC_REPORT_FIGURES[key]),
  placement: Object.fromEntries(CC_REPORT_CHART_AUDIENCE_KEYS.map(audience => [audience, ccReportChartAnchor(audience, key)])) as
    Readonly<Partial<Record<BenchmarkReportAudience, string>>>
}));

/** The picker's *Layout* fields: the charts always use the print theme and carry no heading. */
export const CC_REPORT_CHART_LAYOUT_FIELDS: readonly ReportChartLayoutField[] = ['labelPt', 'maxHeightPercent'];

/** Why a document type's charts cannot be chosen: it is neither written nor checked under Documents. */
export const CC_REPORT_CHART_COLUMN_DISABLED_REASON = 'Not chosen in New reports.';

export const CC_DEFAULT_REPORT_CHART_SELECTION: CcReportChartSelection = Object.freeze({
  [ExecutiveSummary]: Object.freeze(['cc1-quality', 'cc2-speed'] as CcReportFigureKey[]),
  [TechnicalReport]: Object.freeze([...CC_REPORT_FIGURE_KEYS]),
  [InternalBrief]: Object.freeze(['cc1-quality', 'cc3-work'] as CcReportFigureKey[]),
  [ProviderIssueReport]: Object.freeze(['cc2-speed', 'cc4-timeline'] as CcReportFigureKey[])
});

/**
 * Half column for the three measures, so two in a row at one anchor share it; two thirds for the
 * overview; 8 pt labels; at most half the page tall; the print theme, no heading and no logo.
 */
export const CC_DEFAULT_DOCUMENT_CHART_LAYOUT: CcDocumentChartLayout = Object.freeze({
  ...DEFAULT_DOCUMENT_CHART_LAYOUT,
  widths: Object.freeze({ 'cc1-quality': 'half', 'cc2-speed': 'half', 'cc3-work': 'half', 'cc4-timeline': 'twoThirds' }),
  sideBySide: true,
  labelPt: 8,
  maxHeightPercent: 50,
  heading: 'none',
  logo: false,
  theme: 'lightPrint'
} as CcDocumentChartLayout);

export const CC_DEFAULT_REPORT_CHART_LAYOUT: CcReportChartLayout = Object.freeze(
  Object.fromEntries(CC_REPORT_CHART_AUDIENCE_KEYS.map(audience => [audience, CC_DEFAULT_DOCUMENT_CHART_LAYOUT])) as CcReportChartLayout);

export const CC_DEFAULT_REPORT_CHART_SETTINGS: CcReportChartSettings = Object.freeze({
  selection: CC_DEFAULT_REPORT_CHART_SELECTION,
  layout: CC_DEFAULT_REPORT_CHART_LAYOUT
});

/** The four figures and four document types only, each figure once, in document order. */
export function normalizeCcReportChartSelection(selection: unknown): CcReportChartSelection {
  return normalizePickerSelection(selection, CC_REPORT_FIGURE_KEYS, CC_REPORT_CHART_AUDIENCE_KEYS);
}

/**
 * One document type's layout: its label size, maximum height and widths checked as the Report Pack's
 * are, a field it lacks taken from the defaults, and the fields the picker does not offer fixed at
 * theirs.
 */
export function normalizeCcDocumentChartLayout(value: unknown): CcDocumentChartLayout {
  const source = value && typeof value === 'object' ? value as Record<string, unknown> : {};
  const d = CC_DEFAULT_DOCUMENT_CHART_LAYOUT;
  const normalized = normalizePickerDocumentLayout({ ...d, ...source }, CC_REPORT_FIGURE_KEYS);
  // The Report Pack's normalizer falls back to its own 60 %; an unknown height here takes this default.
  const maxHeightPercent = REPORT_CHART_MAX_HEIGHT_PERCENTS.includes(source['maxHeightPercent'] as number)
    ? normalized.maxHeightPercent
    : d.maxHeightPercent;
  return {
    ...normalized, maxHeightPercent, orientation: d.orientation, sideBySide: d.sideBySide, heading: d.heading, logo: d.logo, theme: d.theme
  };
}

/** Every document type, each normalized; a missing one takes the defaults. */
export function normalizeCcReportChartLayout(layout: unknown): CcReportChartLayout {
  const source = (layout && typeof layout === 'object' ? layout : {}) as Record<string | number, unknown>;
  return Object.fromEntries(CC_REPORT_CHART_AUDIENCE_KEYS.map(audience => [
    audience,
    source[audience] === undefined ? CC_DEFAULT_DOCUMENT_CHART_LAYOUT : normalizeCcDocumentChartLayout(source[audience])
  ])) as CcReportChartLayout;
}

/** One document type's layout; the defaults without one. */
export function ccDocumentChartLayoutFor(layout: CcReportChartLayout, audience: BenchmarkReportAudience): CcDocumentChartLayout {
  return layout[audience] ?? CC_DEFAULT_DOCUMENT_CHART_LAYOUT;
}

// --- Storage ---

export const CC_REPORT_CHART_STORAGE_KEY = 'overseer.benchmark.chatConsistency.reportCharts';

const STORED_VERSION = 1;

/** The record in localStorage; the defaults when absent, unreadable or of another version. */
export function readStoredCcReportChartSettings(): CcReportChartSettings {
  try {
    const raw = localStorage.getItem(CC_REPORT_CHART_STORAGE_KEY);
    if (!raw) return CC_DEFAULT_REPORT_CHART_SETTINGS;
    const parsed = JSON.parse(raw) as { version?: unknown; selection?: unknown; layout?: unknown } | null;
    if (!parsed || parsed.version !== STORED_VERSION) return CC_DEFAULT_REPORT_CHART_SETTINGS;
    return {
      selection: parsed.selection && typeof parsed.selection === 'object'
        ? normalizeCcReportChartSelection(parsed.selection)
        : CC_DEFAULT_REPORT_CHART_SELECTION,
      layout: normalizeCcReportChartLayout(parsed.layout)
    };
  } catch {
    return CC_DEFAULT_REPORT_CHART_SETTINGS;
  }
}

export function storeCcReportChartSettings(settings: CcReportChartSettings): void {
  try {
    localStorage.setItem(CC_REPORT_CHART_STORAGE_KEY, JSON.stringify({
      version: STORED_VERSION,
      selection: normalizeCcReportChartSelection(settings.selection),
      layout: normalizeCcReportChartLayout(settings.layout)
    }));
  } catch {
    // Storage unavailable: the choices are simply not remembered.
  }
}

// --- Notes and the server's layout ---

/**
 * A note per figure whose endpoint the analysis could not compute, for the picker:
 * `P1 was not computable in this analysis; the chart's caption says it is not comparable.`
 */
export function ccReportChartNotes(endpoints: readonly CcEndpointResult[] | null | undefined): Readonly<Record<string, string>> {
  const notes: Record<string, string> = {};
  for (const key of CC_REPORT_FIGURE_KEYS) {
    const id = CC_FIGURE_ENDPOINTS[CC_REPORT_FIGURES[key]];
    const endpoint = id ? endpoints?.find(entry => entry.id === id) : undefined;
    if (endpoint && !endpoint.computed) {
      notes[key] = `${endpoint.id} was not computable in this analysis; the chart's caption says it is not comparable.`;
    }
  }
  return notes;
}

function widthShare(width: ReportChartWidth): number {
  return REPORT_CHART_WIDTHS.find(option => option.value === width)?.share ?? 1;
}

/**
 * The layout the server places a document's charts by, for the figures it has, in document order:
 * each figure's width share, the row two consecutive half-width figures at one anchor share, and the
 * maximum height. `{ version: 1, figures: [{ key, widthShare, rowGroup }], maxHeightShare }`.
 */
export function ccReportDocumentChartLayout(
  audience: BenchmarkReportAudience,
  figureKeys: readonly string[],
  layout: CcDocumentChartLayout
): ReportDocumentChartLayout {
  const keys = CC_REPORT_FIGURE_KEYS.filter(key => figureKeys.includes(key));
  const figures: ReportDocumentChartLayoutFigure[] = [];
  let rowGroup = 0;
  for (let i = 0; i < keys.length; i++) {
    const key = keys[i];
    const width = documentChartWidth(layout, key);
    const next = keys[i + 1];
    if (layout.sideBySide && width === 'half' && next !== undefined && documentChartWidth(layout, next) === 'half'
        && ccReportChartAnchor(audience, key) === ccReportChartAnchor(audience, next)) {
      rowGroup++;
      figures.push({ key, widthShare: 1 / 2, rowGroup }, { key: next, widthShare: 1 / 2, rowGroup });
      i++;
      continue;
    }
    figures.push({ key, widthShare: widthShare(width), rowGroup: null });
  }
  return { version: 1, figures, maxHeightShare: layout.maxHeightPercent / 100 };
}
