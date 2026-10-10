import { of, throwError } from 'rxjs';

import { AdminBenchmarkService, BenchmarkReportAudience, ReportDocumentChartLayout, ReportDocumentChartUpload } from '../../../services/admin-benchmark.service';
import { DOCUMENT_CHART_COLUMN_PT, documentFigureLayout } from '../report-pack/report-charts';
import { CcFigureInput } from './chat-consistency-charts';
import {
  CC_DEFAULT_DOCUMENT_CHART_LAYOUT,
  CC_DEFAULT_REPORT_CHART_LAYOUT,
  CC_DEFAULT_REPORT_CHART_SELECTION,
  CcDocumentChartLayout,
  CcReportChartSettings
} from './chat-consistency-report-chart-settings';
import {
  CC_REPORT_CHART_VERSION,
  ccDocumentChartSource,
  ccDocumentFigureBox,
  ccReportChartSettingsHash,
  composeCcReportChart,
  publishCcReportCharts
} from './chat-consistency-report-charts';
import { ccEndpoint, ccPoint, ccTimeline } from './chat-consistency-tab.testing';

const { ExecutiveSummary, TechnicalReport, ProviderIssueReport } = BenchmarkReportAudience;
const DEFAULTS: CcReportChartSettings = { selection: CC_DEFAULT_REPORT_CHART_SELECTION, layout: CC_DEFAULT_REPORT_CHART_LAYOUT };

/** The pixel width and height a PNG's IHDR chunk records. */
function pngSize(base64: string): { width: number; height: number } {
  const bytes = Uint8Array.from(atob(base64.slice(0, 64)), char => char.charCodeAt(0));
  const view = new DataView(bytes.buffer);
  return { width: view.getUint32(16), height: view.getUint32(20) };
}

describe('chat-consistency-report-charts', () => {
  const input: CcFigureInput = { points: ccTimeline().points };

  it('is drawing version 6', () => {
    expect(CC_REPORT_CHART_VERSION).toBe(6);
  });

  it('hashes the drawing settings with the chart choices and their layout', async () => {
    const hash = await ccReportChartSettingsHash(DEFAULTS);
    expect(hash).toMatch(/^[0-9a-f]{64}$/);
    expect(await ccReportChartSettingsHash({ ...DEFAULTS })).toBe(hash);
    const fewer = { ...DEFAULTS, selection: { ...DEFAULTS.selection, [ExecutiveSummary]: ['cc1-quality'] as const } };
    expect(await ccReportChartSettingsHash(fewer)).not.toBe(hash);
    const smaller = { ...DEFAULTS, layout: { ...DEFAULTS.layout, [ExecutiveSummary]: { ...CC_DEFAULT_DOCUMENT_CHART_LAYOUT, labelPt: 7 } } };
    expect(await ccReportChartSettingsHash(smaller)).not.toBe(hash);
  });

  it('sizes a figure as Model Comparison does: its width\'s aspect, taller only where the frame leaves the plot too little', () => {
    const box = ccDocumentFigureBox('cc1-quality', 'half', 8, ccDocumentChartSource());
    const aspect = documentFigureLayout('cc1-quality', 'half', 8);
    expect(box.widthPt).toBeCloseTo(DOCUMENT_CHART_COLUMN_PT / 2, 6);
    expect(box.layoutWidth).toBeCloseTo((DOCUMENT_CHART_COLUMN_PT / 2) * 11 / 8, 6);
    // Square at half width: the empty frame leaves the plot more than the document minimum.
    expect(box.heightPt).toBeCloseTo(aspect.heightPt, 6);
    expect(box.pxWidth).toBe(aspect.pxWidth);
  });

  it('draws a chosen figure at the width it prints at, with its caption', async () => {
    const upload = await composeCcReportChart('cc1-quality', input, 'a'.repeat(64), CC_DEFAULT_DOCUMENT_CHART_LAYOUT);
    expect(upload).not.toBeNull();
    expect(upload!.figureKey).toBe('cc1-quality');
    expect(upload!.naming).toBe('named');
    expect(upload!.title).toBe('Intelligence per run');
    expect(upload!.caption).toBe('The Intelligence Index ranged from 71 to 74 across 6 runs.');
    const half = documentFigureLayout('cc1-quality', 'half', 8);
    expect(pngSize(upload!.pngBase64).width).toBe(half.pxWidth);

    const full: CcDocumentChartLayout = { ...CC_DEFAULT_DOCUMENT_CHART_LAYOUT, widths: {} };
    const wide = await composeCcReportChart('cc1-quality', input, 'a'.repeat(64), full);
    expect(pngSize(wide!.pngBase64).width).toBe(documentFigureLayout('cc1-quality', 'full', 8).pxWidth);
  });

  it('says first when the figure\'s endpoint was not comparable', async () => {
    const endpoints = [ccEndpoint('P1', { computed: false, notComputedReason: 'No common grader covers every run.' })];
    const upload = await composeCcReportChart('cc1-quality', { ...input, endpoints }, 'a'.repeat(64), CC_DEFAULT_DOCUMENT_CHART_LAYOUT);
    expect(upload!.caption.startsWith('Not comparable across the periods: no common grader covers every run. ')).toBe(true);
    expect(upload!.altText.startsWith('Not comparable across the periods: ')).toBe(true);
  });

  it('draws nothing for a figure without values', async () => {
    const empty: CcFigureInput = { points: [ccPoint(1, '2026-09-01T00:00:00Z', { qualityIndex: null })] };
    expect(await composeCcReportChart('cc1-quality', empty, 'a'.repeat(64), CC_DEFAULT_DOCUMENT_CHART_LAYOUT)).toBeNull();
  });

  describe('publishing', () => {
    let puts: { id: number; charts: ReportDocumentChartUpload[]; layout: ReportDocumentChartLayout | null }[];
    let deletes: number[];
    let service: AdminBenchmarkService;

    beforeEach(() => {
      puts = [];
      deletes = [];
      service = {
        putReportDocumentCharts: (id: number, charts: ReportDocumentChartUpload[], layout: ReportDocumentChartLayout | null = null) => {
          puts.push({ id, charts, layout });
          return of({ chartCount: charts.length, figureKeys: charts.map(chart => chart.figureKey) });
        },
        deleteReportDocumentCharts: (id: number) => {
          deletes.push(id);
          return of(undefined);
        }
      } as unknown as AdminBenchmarkService;
    });

    it('uploads each document\'s chosen figures with its layout, and removes the charts of a document with none chosen', async () => {
      const settings: CcReportChartSettings = {
        ...DEFAULTS,
        selection: { ...DEFAULTS.selection, [ExecutiveSummary]: ['cc1-quality', 'cc3-work'], [ProviderIssueReport]: [] }
      };
      const result = await publishCcReportCharts(service, [
        { id: 501, audience: ExecutiveSummary, chartCount: 0 },
        { id: 504, audience: ProviderIssueReport, chartCount: 3 },
        { id: 505, audience: ProviderIssueReport, chartCount: 0 }
      ], input, settings);

      expect(puts.map(put => put.id)).toEqual([501]);
      expect(puts[0].charts.map(chart => chart.figureKey)).toEqual(['cc1-quality', 'cc3-work']);
      expect(puts[0].layout).toEqual({
        version: 1,
        figures: [{ key: 'cc1-quality', widthShare: 0.5, rowGroup: 1 }, { key: 'cc3-work', widthShare: 0.5, rowGroup: 1 }],
        maxHeightShare: 0.5
      });
      expect(new Set(puts[0].charts.map(chart => chart.settingsHash)).size).toBe(1);
      expect(deletes).toEqual([504]);
      expect(result).toEqual({ published: [{ documentId: 501, chartCount: 2 }], failed: [], withoutCharts: [504, 505] });
    });

    it('records a failed upload and goes on with the next document', async () => {
      const failing = {
        ...service,
        putReportDocumentCharts: (id: number, charts: ReportDocumentChartUpload[]) => id === 501
          ? throwError(() => ({ status: 400, error: { error: 'Chart storage is not configured.' } }))
          : of({ chartCount: charts.length, figureKeys: [] })
      } as unknown as AdminBenchmarkService;
      const settings: CcReportChartSettings = {
        ...DEFAULTS,
        selection: { [ExecutiveSummary]: ['cc1-quality'], [TechnicalReport]: ['cc1-quality'] }
      };
      const result = await publishCcReportCharts(failing, [
        { id: 501, audience: ExecutiveSummary },
        { id: 502, audience: TechnicalReport }
      ], input, settings);
      expect(result.failed).toEqual([{ documentId: 501, message: 'Chart storage is not configured.' }]);
      expect(result.published).toEqual([{ documentId: 502, chartCount: 1 }]);
    });

    it('fails a document whose every chosen figure has nothing to draw', async () => {
      const empty: CcFigureInput = { points: [ccPoint(1, '2026-09-01T00:00:00Z', { qualityIndex: null })] };
      const settings: CcReportChartSettings = { ...DEFAULTS, selection: { [ExecutiveSummary]: ['cc1-quality'] } };
      const result = await publishCcReportCharts(service, [{ id: 501, audience: ExecutiveSummary }], empty, settings);
      expect(puts).toEqual([]);
      expect(result.failed).toEqual([{ documentId: 501, message: 'No chart could be drawn.' }]);
    });
  });
});
