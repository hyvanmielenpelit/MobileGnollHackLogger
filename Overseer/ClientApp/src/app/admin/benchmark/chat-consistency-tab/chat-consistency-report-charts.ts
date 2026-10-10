/**
 * The charts attached to a chat consistency analysis's AI-written documents: the figures of
 * `chat-consistency-charts.ts` chosen for each document type in step 5, each composed off-screen at
 * the width it prints at in that document, with its labels at the chosen size in points, in the
 * print theme and with no heading; encoded as PNG and uploaded with the document's chart layout by
 * `PUT report-documents/{id}/charts`, the Report Pack's chart endpoint, keyed `cc1-quality`,
 * `cc2-speed`, `cc3-work` and `cc4-timeline`.
 */

import { firstValueFrom } from 'rxjs';

import { AdminBenchmarkService, ReportDocumentChartUpload, BenchmarkReportAudience } from '../../../services/admin-benchmark.service';
import {
  FigureExportRequest,
  FigureExportResolution,
  OffscreenPlotConfig,
  composeFigureImage,
  encodeFigureImage,
  renderPlotOffscreen,
  resolveFigureLayout
} from '../model-comparison/figure-export';
import { DEFAULT_APPEARANCE_STYLE } from '../model-comparison/figure-style';
import { resolveFigureTheme } from '../model-comparison/figure-theme';
import {
  BASE_LABEL_PX,
  DOCUMENT_CHART_COLUMN_PT,
  DOCUMENT_CHART_DPI,
  DOCUMENT_MIN_CONTENT_WIDTH,
  DOCUMENT_MIN_PLOT_HEIGHT,
  DocumentChartLayout,
  ReportChartWidth,
  canonicalJson,
  documentChartLayout,
  documentChartRefusal,
  documentChartWidth,
  documentFigureLayout,
  printFigureAppearance
} from '../report-pack/report-charts';
import { CC_PRINT_THEME, CC_REPORT_FIGURES, CcFigureInput, buildCcFigure, ccFigureTitle } from './chat-consistency-charts';
import { ccReportEventGroups } from './chat-consistency-events';
import {
  CcDocumentChartLayout,
  CcReportChartSettings,
  ccDocumentChartLayoutFor,
  ccReportDocumentChartLayout,
  normalizeCcReportChartLayout,
  normalizeCcReportChartSelection
} from './chat-consistency-report-chart-settings';
import { CC_REPORT_FIGURE_KEYS, CcReportFigureKey } from './chat-consistency.models';

/** Bumped whenever the drawing changes, so the settings hash tells old charts from new ones. */
export const CC_REPORT_CHART_VERSION = 8;

/** SHA-256 of the drawing settings, the chart choices and their layout, 64 lowercase hex characters; throws outside a secure context. */
export async function ccReportChartSettingsHash(settings: CcReportChartSettings): Promise<string> {
  const subtle = typeof crypto !== 'undefined' ? crypto.subtle : undefined;
  if (!subtle) {
    throw new Error('SHA-256 is not available: crypto.subtle needs a secure context.');
  }
  const input = {
    kind: 'chat-consistency',
    version: CC_REPORT_CHART_VERSION,
    figures: [...CC_REPORT_FIGURE_KEYS],
    theme: 'print',
    columnPt: DOCUMENT_CHART_COLUMN_PT,
    dpi: DOCUMENT_CHART_DPI,
    baseLabelPx: BASE_LABEL_PX,
    format: 'png',
    selection: normalizeCcReportChartSelection(settings.selection),
    layout: normalizeCcReportChartLayout(settings.layout)
  };
  const digest = await subtle.digest('SHA-256', new TextEncoder().encode(canonicalJson(input)));
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
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

/** Model Comparison's *Light, for print*, which frames every chat consistency document chart. */
const DOCUMENT_THEME = resolveFigureTheme(printFigureAppearance(DEFAULT_APPEARANCE_STYLE));

/** The chart inside the frame: the print palette and font, on the frame's ground. */
const DOCUMENT_CHART_THEME = Object.freeze({ ...CC_PRINT_THEME, background: null });

/** What a document chart draws around the plot: nothing but the frame's padding, the document captions it. */
export type CcDocumentChartSource = Omit<FigureExportRequest, 'canvas' | 'format' | 'layout'>;

export function ccDocumentChartSource(): CcDocumentChartSource {
  return {
    chrome: { title: '', badges: [], detail: '', key: [], highlight: '', notes: [] },
    footer: { suite: '', computedAt: '' },
    theme: DOCUMENT_THEME,
    logo: null,
    minContentWidth: DOCUMENT_MIN_CONTENT_WIDTH
  };
}

function documentResolution(box: DocumentChartLayout): FigureExportResolution {
  return { id: 'document', label: 'Document', widthPx: box.pxWidth, heightPx: box.pxHeight, group: 'Document' };
}

/**
 * A document figure's box, as Model Comparison's: its width's aspect, made taller where the frame
 * would leave the plot less than {@link DOCUMENT_MIN_PLOT_HEIGHT}. The frame is measured on a probe
 * three widths tall; a probe the composer refuses leaves the aspect's box for the real call to refuse.
 */
export function ccDocumentFigureBox(key: CcReportFigureKey, width: ReportChartWidth, labelPt: number, source: CcDocumentChartSource): DocumentChartLayout {
  const box = documentFigureLayout(key, width, labelPt);
  const probe = documentChartLayout(box.widthPt, box.widthPt * 3, labelPt);
  const probed = resolveFigureLayout(source, documentResolution(probe), 1, probe.textScale).layout;
  if (!probed) {
    return box;
  }
  const chromeHeight = probed.layoutHeight - probed.plotHeight;
  return documentFigureLayout(key, width, labelPt, DOCUMENT_CHART_COLUMN_PT, chromeHeight + DOCUMENT_MIN_PLOT_HEIGHT);
}

/**
 * The input of a report chart: `input` with the analysis's own events grouped and tagged as its report
 * documents number them (`ccReportEventGroups`), whatever the timeline numbered them. The split comes
 * from the period bands, which `analysisBands` draws at the baseline's end and the comparison's start.
 */
export function ccReportFigureInput(input: CcFigureInput): CcFigureInput {
  const baselineEnd = input.bands?.find(band => band.name === 'Baseline')?.end ?? null;
  const comparisonStart = input.bands?.find(band => band.name === 'Comparison')?.start ?? null;
  return { ...input, eventGroups: ccReportEventGroups(input.events ?? [], baselineEnd, comparisonStart), eventNumbering: undefined };
}

/**
 * One figure as a document of `layout` prints it: at its width of the text column at 300 dpi, its
 * axis labels at the layout's size in points (the charts' 11 px axis text is {@link BASE_LABEL_PX}),
 * the time axis fitted to the plotted points, in the print theme, its event markers tagged as the
 * document's events table tags them ({@link ccReportFigureInput}). Null when the figure has nothing to
 * draw; throws when its width does not fit or it cannot be composed.
 */
export async function composeCcReportChart(
  key: CcReportFigureKey,
  input: CcFigureInput,
  settingsHash: string,
  layout: CcDocumentChartLayout
): Promise<ReportDocumentChartUpload | null> {
  const title = ccFigureTitle(CC_REPORT_FIGURES[key]);
  const width = documentChartWidth(layout, key);
  const refusal = documentChartRefusal(width, layout.labelPt);
  if (refusal) {
    throw new Error(`${title} cannot be drawn: ${refusal}`);
  }
  const figure = buildCcFigure(CC_REPORT_FIGURES[key], ccReportFigureInput(input), {
    theme: DOCUMENT_CHART_THEME,
    reducedMotion: true,
    header: { title: null, subject: null },
    logo: null,
    fitToData: true
  });
  if (!figure.config) return null;
  const source = ccDocumentChartSource();
  const box = ccDocumentFigureBox(key, width, layout.labelPt, source);
  const resolved = resolveFigureLayout(source, documentResolution(box), 1, box.textScale);
  if (!resolved.layout) {
    throw new Error(`${title} cannot be drawn: ${resolved.refusal ?? 'it does not fit a document chart.'}`);
  }
  // The figure's line configuration, erased to the union the offscreen renderer takes.
  const plot = await renderPlotOffscreen(figure.config as unknown as OffscreenPlotConfig, resolved.layout);
  if (!plot) {
    throw new Error(`${title} could not be composed: its chart could not be built.`);
  }
  const canvas = composeFigureImage({ ...source, canvas: plot, format: 'png', layout: resolved.layout });
  const { blob } = await encodeFigureImage(canvas, 'png');
  return {
    figureKey: key,
    naming: 'named',
    title: figure.title,
    caption: figure.takeaway,
    altText: figure.altText,
    settingsHash,
    pngBase64: await blobToBase64(blob)
  };
}

/** A written document to chart; `chartCount` tells whether it has charts a choice of none removes. */
export interface CcReportChartDocument {
  readonly id: number;
  readonly audience: BenchmarkReportAudience;
  readonly chartCount?: number;
}

export interface CcChartPublishResult {
  /** Documents that received charts, with the number attached. */
  published: { documentId: number; chartCount: number }[];
  failed: { documentId: number; message: string }[];
  /** Documents with no chart chosen for their type: nothing was drawn, and charts they had were removed. */
  withoutCharts: number[];
}

function errorMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message) return error.message;
  const body = (error as { error?: unknown })?.error;
  if (typeof body === 'string' && body.trim()) return body.trim();
  if (body && typeof (body as { error?: unknown }).error === 'string') return (body as { error: string }).error;
  return fallback;
}

/**
 * Draws each document's chosen figures for its type's layout and attaches them with that layout,
 * one document at a time. A figure is composed once per width and label size and shared by the
 * documents that print it alike. A figure with nothing to draw is left out; a document whose every
 * figure is left out or fails, or whose upload fails, is recorded as failed and the others go on. A
 * document with no figure chosen gets none, and loses the charts it had.
 */
export async function publishCcReportCharts(
  service: AdminBenchmarkService,
  documents: readonly CcReportChartDocument[],
  input: CcFigureInput,
  settings: CcReportChartSettings
): Promise<CcChartPublishResult> {
  const result: CcChartPublishResult = { published: [], failed: [], withoutCharts: [] };
  if (documents.length === 0) return result;
  const selection = normalizeCcReportChartSelection(settings.selection);
  const layouts = normalizeCcReportChartLayout(settings.layout);
  const settingsHash = await ccReportChartSettingsHash({ selection, layout: layouts });
  const composed = new Map<string, Promise<ReportDocumentChartUpload | null>>();

  for (const doc of documents) {
    const keys = selection[doc.audience] ?? [];
    if (keys.length === 0) {
      if ((doc.chartCount ?? 0) > 0) {
        try {
          await firstValueFrom(service.deleteReportDocumentCharts(doc.id));
        } catch (error) {
          result.failed.push({ documentId: doc.id, message: errorMessage(error, 'The charts could not be removed.') });
          continue;
        }
      }
      result.withoutCharts.push(doc.id);
      continue;
    }

    const layout = ccDocumentChartLayoutFor(layouts, doc.audience);
    const uploads: ReportDocumentChartUpload[] = [];
    const errors: string[] = [];
    for (const key of keys) {
      const cacheKey = `${key}|${documentChartWidth(layout, key)}|${layout.labelPt}`;
      let pending = composed.get(cacheKey);
      if (!pending) {
        pending = composeCcReportChart(key, input, settingsHash, layout);
        composed.set(cacheKey, pending);
      }
      try {
        const upload = await pending;
        if (upload) uploads.push(upload);
      } catch (error) {
        errors.push(errorMessage(error, 'The chart could not be drawn.'));
      }
    }
    if (uploads.length === 0) {
      result.failed.push({ documentId: doc.id, message: errors[0] ?? 'No chart could be drawn.' });
      continue;
    }
    try {
      const placement = ccReportDocumentChartLayout(doc.audience, uploads.map(upload => upload.figureKey), layout);
      const summary = await firstValueFrom(service.putReportDocumentCharts(doc.id, uploads, placement));
      result.published.push({ documentId: doc.id, chartCount: summary?.chartCount ?? uploads.length });
    } catch (error) {
      result.failed.push({ documentId: doc.id, message: errorMessage(error, 'The charts could not be attached.') });
    }
  }
  return result;
}
