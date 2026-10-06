import { HttpErrorResponse } from '@angular/common/http';
import { Observable, defer, firstValueFrom, from, map } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkPdfPaper,
  BenchmarkReportAudience,
  BenchmarkReportPackLayoutPreviewChart,
  BenchmarkReportPackLayoutPreviewRequest,
  BenchmarkReportPackRequest,
  ReportDocumentChartLayout
} from '../../../services/admin-benchmark.service';
import type { PdfViewerFile, PdfViewerRequest } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { audienceLabel } from './report-document-format';
import type { ComposedReportChart, ReportChartFigureKey, ReportChartScope, ReportChartVariant } from './report-charts';

/*
 * Step 3's Preview layout: the selected figures of one document type, composed by the wizard as the
 * document's charts would be, sent with the document's request to `POST report-packs/layout-preview`,
 * which renders the PDF with placeholder text and stores nothing.
 */

/** A document type's chosen figures as the wizard composes them, and the layout the server places them by. */
export interface ComposedDocumentCharts {
  readonly charts: readonly { readonly key: ReportChartFigureKey; readonly chart: ComposedReportChart }[];
  readonly failed: readonly ReportChartLayoutPreviewFailure[];
  readonly layout: ReportDocumentChartLayout;
}

/** A figure that could not be drawn, and why. */
export interface ReportChartLayoutPreviewFailure {
  readonly key: ReportChartFigureKey;
  readonly message: string;
}

/** The wizard's `composeDocumentCharts`, lent to step 3. */
export type DocumentChartsComposer =
  (audience: BenchmarkReportAudience, variant: ReportChartVariant, scope: ReportChartScope) => Promise<ComposedDocumentCharts>;

/** What one layout preview is of. */
export interface ReportChartLayoutPreviewInput {
  /** Step 3's request for the documents: the comparison, the scope and its models. */
  readonly request: BenchmarkReportPackRequest;
  readonly audience: BenchmarkReportAudience;
  readonly paper: BenchmarkPdfPaper;
  readonly scope: ReportChartScope;
  /** The entries a comparison-scope document covers, which its charts plot; null for a per-model document. */
  readonly coveredKeys: readonly string[] | null;
}

/** `Layout preview — Executive Summary`. */
export function layoutPreviewTitle(audience: BenchmarkReportAudience): string {
  return `Layout preview — ${audienceLabel(audience)}`;
}

/** `layout-preview_executive-summary.pdf`. */
export function layoutPreviewFileName(audience: BenchmarkReportAudience): string {
  const slug = audienceLabel(audience).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
  return `layout-preview_${slug}.pdf`;
}

/** The named copy's charts: those of a comparison-scope document plot its covered entries only. */
export function layoutPreviewVariant(coveredKeys: readonly string[] | null): ReportChartVariant {
  return coveredKeys ? { kind: 'named', coveredKeys: [...coveredKeys] } : { kind: 'named' };
}

/**
 * The refusal of a request made for a Blob, its body read as JSON where it is JSON and as text
 * otherwise, so the viewer can show the server's message. Anything else is returned unchanged.
 */
export async function decodeBlobError(error: unknown): Promise<unknown> {
  if (!(error instanceof HttpErrorResponse) || !(error.error instanceof Blob)) {
    return error;
  }
  let body: unknown = null;
  try {
    const text = await error.error.text();
    try {
      body = JSON.parse(text);
    } catch {
      body = text;
    }
  } catch {
    body = null;
  }
  return new HttpErrorResponse({
    error: body,
    headers: error.headers,
    status: error.status,
    statusText: error.statusText,
    url: error.url ?? undefined
  });
}

/** One layout preview: the composed figures sent with the document's request, and the PDF that comes back. */
export async function renderLayoutPreview(
  service: AdminBenchmarkService,
  compose: DocumentChartsComposer,
  input: ReportChartLayoutPreviewInput
): Promise<{ file: PdfViewerFile; failed: readonly ReportChartLayoutPreviewFailure[] }> {
  const composed = await compose(input.audience, layoutPreviewVariant(input.coveredKeys), input.scope);
  const request: BenchmarkReportPackLayoutPreviewRequest = {
    ...input.request,
    audiences: [input.audience],
    audience: input.audience,
    paper: input.paper,
    naming: 'named',
    layout: composed.layout
  };
  const charts: BenchmarkReportPackLayoutPreviewChart[] = composed.charts.map(({ key, chart }) => ({
    figureKey: key,
    title: chart.title,
    caption: chart.caption,
    altText: chart.altText,
    png: chart.png
  }));
  let blob: Blob;
  try {
    blob = await firstValueFrom(service.reportPackLayoutPreview(request, charts));
  } catch (error) {
    throw await decodeBlobError(error);
  }
  const bytes = new Uint8Array(await blob.arrayBuffer());
  return { file: { bytes, fileName: null }, failed: composed.failed };
}

/**
 * The PDF viewer's request for a layout preview. Every load (the first, and each Try again) composes
 * the figures afresh; `onComposed` hears which figures could not be drawn.
 */
export function layoutPreviewViewerRequest(
  service: AdminBenchmarkService,
  compose: DocumentChartsComposer,
  input: ReportChartLayoutPreviewInput,
  onComposed?: (failed: readonly ReportChartLayoutPreviewFailure[]) => void
): PdfViewerRequest {
  return {
    title: layoutPreviewTitle(input.audience),
    subtitle: 'Placeholder text in place of the report writer\'s; the charts, tables and sections as they would print.',
    load: (): Observable<PdfViewerFile> => defer(() => from(renderLayoutPreview(service, compose, input))).pipe(
      map(result => {
        onComposed?.(result.failed);
        return result.file;
      })
    ),
    fallbackFileName: layoutPreviewFileName(input.audience)
  };
}
