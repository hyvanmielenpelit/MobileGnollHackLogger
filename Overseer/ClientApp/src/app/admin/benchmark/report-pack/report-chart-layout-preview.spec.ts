import type { Mock } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom, of, throwError } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportPackLayoutPreviewChart,
  BenchmarkReportPackLayoutPreviewRequest,
  BenchmarkReportPackPricingBasis,
  BenchmarkReportPackRequest,
  BenchmarkReportScope
} from '../../../services/admin-benchmark.service';
import { pdfLoadErrorMessage } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import {
  ComposedDocumentCharts,
  DocumentChartsComposer,
  decodeBlobError,
  layoutPreviewFileName,
  layoutPreviewTitle,
  layoutPreviewViewerRequest,
  renderLayoutPreview
} from './report-chart-layout-preview';

const { ExecutiveSummary, TechnicalReport } = BenchmarkReportAudience;

const REQUEST: BenchmarkReportPackRequest = {
  runIds: [1, 2, 3],
  groupIds: [],
  pricingBasis: BenchmarkReportPackPricingBasis.Current,
  subjectKey: '',
  scope: BenchmarkReportScope.Comparison,
  coveredEntryKeys: ['run:1', 'run:2'],
  audiences: [ExecutiveSummary, TechnicalReport],
  writerModelConfigurationId: 0,
  acknowledgeSameProvider: false
};

const COMPOSED: ComposedDocumentCharts = {
  charts: [{
    key: 'p1a-quality',
    chart: { png: new Blob(['png'], { type: 'image/png' }), widthPx: 2008, heightPx: 1255, title: 'Intelligence', caption: 'Drawn.', altText: 'Bars.' }
  }],
  failed: [{ key: 'p2-profile', message: 'needs three or more models' }],
  layout: { version: 1, figures: [{ key: 'p1a-quality', widthShare: 1, rowGroup: null }], maxHeightShare: 0.6 }
};

describe('report-chart-layout-preview', () => {
  let sent: { request: BenchmarkReportPackLayoutPreviewRequest; charts: readonly BenchmarkReportPackLayoutPreviewChart[] }[];
  let service: AdminBenchmarkService;
  let compose: Mock;

  beforeEach(() => {
    sent = [];
    service = {
      reportPackLayoutPreview: (request: BenchmarkReportPackLayoutPreviewRequest, charts: readonly BenchmarkReportPackLayoutPreviewChart[]) => {
        sent.push({ request, charts });
        return of(new Blob([new Uint8Array([37, 80, 68, 70])], { type: 'application/pdf' }));
      }
    } as unknown as AdminBenchmarkService;
    compose = vi.fn().mockResolvedValue(COMPOSED);
  });

  it('titles and names the preview after the document type', () => {
    expect(layoutPreviewTitle(ExecutiveSummary)).toBe('Layout preview — Executive Summary');
    expect(layoutPreviewFileName(TechnicalReport)).toBe('layout-preview_report-for-ai-researchers-and-developers.pdf');
  });

  it('composes the covered models\' charts, sends them with the document\'s request and layout, and returns the PDF', async () => {
    const result = await renderLayoutPreview(service, compose as unknown as DocumentChartsComposer, {
      request: REQUEST, audience: TechnicalReport, paper: 'letter', scope: 'comparison', coveredKeys: ['run:1', 'run:2']
    });

    expect(compose).toHaveBeenCalledWith(TechnicalReport, { kind: 'named', coveredKeys: ['run:1', 'run:2'] }, 'comparison');
    expect(sent.length).toBe(1);
    expect(sent[0].request).toEqual({
      ...REQUEST,
      audiences: [TechnicalReport],
      audience: TechnicalReport,
      paper: 'letter',
      naming: 'named',
      layout: COMPOSED.layout
    });
    expect(sent[0].charts).toEqual([{ figureKey: 'p1a-quality', title: 'Intelligence', caption: 'Drawn.', altText: 'Bars.', png: COMPOSED.charts[0].chart.png }]);
    expect(Array.from(result.file.bytes)).toEqual([37, 80, 68, 70]);
    expect(result.failed).toEqual(COMPOSED.failed);
  });

  it('draws a per-model document\'s charts as step 2 does', async () => {
    await renderLayoutPreview(service, compose as unknown as DocumentChartsComposer, {
      request: { ...REQUEST, scope: BenchmarkReportScope.Model, subjectKey: 'run:1', subjectKeys: ['run:1'] },
      audience: ExecutiveSummary, paper: 'a4', scope: 'model', coveredKeys: null
    });
    expect(compose).toHaveBeenCalledWith(ExecutiveSummary, { kind: 'named' }, 'model');
  });

  it('gives the viewer a request that composes afresh on every load and reports the figures it could not draw', async () => {
    const heard: unknown[] = [];
    const request = layoutPreviewViewerRequest(service, compose as unknown as DocumentChartsComposer, {
      request: REQUEST, audience: ExecutiveSummary, paper: 'a4', scope: 'comparison', coveredKeys: ['run:1', 'run:2']
    }, failed => heard.push(failed));

    expect(request.title).toBe('Layout preview — Executive Summary');
    expect(request.fallbackFileName).toBe('layout-preview_executive-summary.pdf');
    expect(request.variants).toBeUndefined();
    expect(compose).not.toHaveBeenCalled();
    await firstValueFrom(request.load(null));
    await firstValueFrom(request.load(null));
    expect(compose).toHaveBeenCalledTimes(2);
    expect(heard).toEqual([COMPOSED.failed, COMPOSED.failed]);
  });

  it('reads the server\'s refusal out of a Blob body, so the viewer can show it', async () => {
    const refusal = new HttpErrorResponse({
      error: new Blob([JSON.stringify({ error: 'Choose at most 12 models.' })], { type: 'application/json' }),
      status: 409,
      statusText: 'Conflict'
    });
    service = { reportPackLayoutPreview: () => throwError(() => refusal) } as unknown as AdminBenchmarkService;

    let thrown: unknown = null;
    try {
      await renderLayoutPreview(service, compose as unknown as DocumentChartsComposer, {
        request: REQUEST, audience: ExecutiveSummary, paper: 'a4', scope: 'comparison', coveredKeys: ['run:1', 'run:2']
      });
    } catch (error) {
      thrown = error;
    }
    expect(thrown).toBeInstanceOf(HttpErrorResponse);
    expect((thrown as HttpErrorResponse).status).toBe(409);
    expect(pdfLoadErrorMessage(thrown)).toBe('Choose at most 12 models.');

    const plain = await decodeBlobError(new HttpErrorResponse({ error: new Blob(['Too large.']), status: 413 }));
    expect(pdfLoadErrorMessage(plain)).toBe('Too large.');
    const other = new Error('boom');
    expect(await decodeBlobError(other)).toBe(other);
  });
});
