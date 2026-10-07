/**
 * The charts attached to a chat consistency analysis's AI-written documents: the figures of
 * `chat-consistency-charts.ts` drawn offscreen in the print theme, encoded as PNG and uploaded with
 * `PUT report-documents/{id}/charts`, the Report Pack's chart endpoint, keyed `cc1-quality`,
 * `cc2-speed`, `cc3-work` and `cc4-timeline`.
 */

import { firstValueFrom } from 'rxjs';

import { AdminBenchmarkService, ReportDocumentChartUpload } from '../../../services/admin-benchmark.service';
import { FigureExportLayout, OffscreenPlotConfig, encodeFigureImage, renderPlotOffscreen } from '../model-comparison/figure-export';
import { canonicalJson } from '../report-pack/report-charts';
import { CC_PRINT_THEME, CC_REPORT_FIGURES, CcFigureInput, buildCcFigure } from './chat-consistency-charts';
import { CC_REPORT_FIGURE_KEYS, CcReportFigureKey } from './chat-consistency.models';

/** A 16:9 plot, written at twice its layout size. */
export const CC_REPORT_CHART_LAYOUT: FigureExportLayout = Object.freeze({
  layoutWidth: 1200,
  layoutHeight: 675,
  plotWidth: 1200,
  plotHeight: 675,
  density: 2,
  pixelWidth: 2400,
  pixelHeight: 1350
});

/** Bumped whenever the drawing changes, so the settings hash tells old charts from new ones. */
export const CC_REPORT_CHART_VERSION = 2;

/** SHA-256 of the drawing settings, 64 lowercase hex characters; throws outside a secure context. */
export async function ccReportChartSettingsHash(): Promise<string> {
  const subtle = typeof crypto !== 'undefined' ? crypto.subtle : undefined;
  if (!subtle) {
    throw new Error('SHA-256 is not available: crypto.subtle needs a secure context.');
  }
  const input = {
    kind: 'chat-consistency',
    version: CC_REPORT_CHART_VERSION,
    figures: [...CC_REPORT_FIGURE_KEYS],
    theme: 'print',
    layout: { width: CC_REPORT_CHART_LAYOUT.layoutWidth, height: CC_REPORT_CHART_LAYOUT.layoutHeight, density: CC_REPORT_CHART_LAYOUT.density }
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

/** One figure as an upload, or null when it has nothing to draw or the drawing fails. */
export async function composeCcReportChart(
  key: CcReportFigureKey,
  input: CcFigureInput,
  settingsHash: string
): Promise<ReportDocumentChartUpload | null> {
  const figure = buildCcFigure(CC_REPORT_FIGURES[key], input, { theme: CC_PRINT_THEME, reducedMotion: true });
  if (!figure.config) return null;
  // The figure's line configuration, erased to the union the offscreen renderer takes.
  const plot = await renderPlotOffscreen(figure.config as unknown as OffscreenPlotConfig, CC_REPORT_CHART_LAYOUT);
  if (!plot) return null;
  const { blob } = await encodeFigureImage(plot, 'png');
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

export interface CcChartPublishResult {
  /** Documents that received charts, with the number attached. */
  published: { documentId: number; chartCount: number }[];
  failed: { documentId: number; message: string }[];
}

/** Draws the four figures once and attaches them to each document. A failed upload does not stop the others. */
export async function publishCcReportCharts(
  service: AdminBenchmarkService,
  documentIds: readonly number[],
  input: CcFigureInput
): Promise<CcChartPublishResult> {
  const result: CcChartPublishResult = { published: [], failed: [] };
  if (documentIds.length === 0) return result;
  const settingsHash = await ccReportChartSettingsHash();
  const uploads: ReportDocumentChartUpload[] = [];
  for (const key of CC_REPORT_FIGURE_KEYS) {
    const upload = await composeCcReportChart(key, input, settingsHash);
    if (upload) uploads.push(upload);
  }
  if (uploads.length === 0) {
    for (const documentId of documentIds) result.failed.push({ documentId, message: 'No chart could be drawn.' });
    return result;
  }
  for (const documentId of documentIds) {
    try {
      const summary = await firstValueFrom(service.putReportDocumentCharts(documentId, uploads));
      result.published.push({ documentId, chartCount: summary?.chartCount ?? uploads.length });
    } catch (error) {
      const body = (error as { error?: unknown })?.error;
      const message = typeof body === 'string' ? body
        : body && typeof (body as { error?: unknown }).error === 'string' ? (body as { error: string }).error
          : 'The charts could not be attached.';
      result.failed.push({ documentId, message });
    }
  }
  return result;
}
