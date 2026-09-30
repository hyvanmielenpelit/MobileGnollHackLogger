import { HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  ReportDocumentChartUpload,
  ReportDocumentChartsSummaryDto
} from '../../../services/admin-benchmark.service';
import {
  CHART_STORAGE_NOT_CONFIGURED,
  ComposedReportChart,
  DEFAULT_CHART_SELECTION,
  DOCUMENT_CHART_LAYOUT,
  REPORT_CHART_FIGURES,
  REPORT_CHART_STORAGE_KEY,
  ReportChartFigureKey,
  ReportChartPublishProgress,
  ReportChartPublisher,
  ReportChartSelection,
  ReportChartSettingsInput,
  ReportChartTarget,
  ReportChartVariant,
  canonicalJson,
  chartSettingsHash,
  documentChartSize,
  isReportChartFigureKey,
  normalizeChartSelection,
  readStoredChartSelection,
  reportChartPlacementLabel,
  storeChartSelection
} from './report-charts';

const { ExecutiveSummary, TechnicalReport, InternalBrief } = BenchmarkReportAudience;

const HASH = 'a'.repeat(64);

describe('report-charts', () => {
  describe('figures and placements', () => {
    it('lists the seven figures in placement order, the profile needing three models', () => {
      expect(REPORT_CHART_FIGURES.map(figure => figure.key)).toEqual([
        'p1a-quality', 'p1b-speed', 'p1c-cost', 'p2-profile', 's1-quality-speed', 's2-quality-cost', 's3-speed-cost'
      ]);
      expect(REPORT_CHART_FIGURES.map(figure => figure.title)).toEqual([
        'Intelligence', 'Speed', 'Cost', 'Model profiles',
        'Intelligence against speed', 'Intelligence against cost', 'Speed against cost'
      ]);
      expect(REPORT_CHART_FIGURES.map(figure => figure.minModels)).toEqual([2, 2, 2, 3, 2, 2, 2]);
      expect(isReportChartFigureKey('p2-profile')).toBeTrue();
      expect(isReportChartFigureKey('p9-nothing')).toBeFalse();
    });

    it('places each figure in the section the server draws it in', () => {
      const table = REPORT_CHART_FIGURES.map(figure => [
        reportChartPlacementLabel(ExecutiveSummary, figure.key),
        reportChartPlacementLabel(TechnicalReport, figure.key),
        reportChartPlacementLabel(InternalBrief, figure.key)
      ]);
      expect(table).toEqual([
        ['How it compares', 'Results against peers → Quality', '§3 Key figures'],
        ['How it compares', 'Results against peers → Speed', '§3 Key figures'],
        ['How it compares', 'Results against peers → Cost', '§3 Key figures'],
        ['How it compares', 'Results against peers', '§3 Key figures'],
        ['How it compares', 'Speed and cost', '§3 Key figures'],
        ['How it compares', 'Speed and cost', '§3 Key figures'],
        ['How it compares', 'Speed and cost', '§3 Key figures']
      ]);
    });

    it('defaults to two charts in the Executive Summary, all seven in the researchers\' report and the three bars in the brief', () => {
      expect(DEFAULT_CHART_SELECTION[ExecutiveSummary]).toEqual(['p1a-quality', 's2-quality-cost']);
      expect(DEFAULT_CHART_SELECTION[TechnicalReport]).toEqual(REPORT_CHART_FIGURES.map(figure => figure.key));
      expect(DEFAULT_CHART_SELECTION[InternalBrief]).toEqual(['p1a-quality', 'p1b-speed', 'p1c-cost']);
    });

    it('draws 1800 px wide: 16:10 for bars and scatters, 4:3 for the profile, text at 175 %, as PNG', () => {
      expect(DOCUMENT_CHART_LAYOUT).toEqual({
        widthPx: 1800, barHeightPx: 1125, scatterHeightPx: 1125, profileHeightPx: 1350, textScalePercent: 175, format: 'png'
      });
      expect(documentChartSize('p1a-quality')).toEqual({ widthPx: 1800, heightPx: 1125 });
      expect(documentChartSize('s3-speed-cost')).toEqual({ widthPx: 1800, heightPx: 1125 });
      expect(documentChartSize('p2-profile')).toEqual({ widthPx: 1800, heightPx: 1350 });
    });
  });

  describe('selection', () => {
    afterEach(() => localStorage.removeItem(REPORT_CHART_STORAGE_KEY));

    it('normalizes: dedupes, orders by placement, drops unknown keys and audiences, keeps an explicit none', () => {
      const messy = {
        [ExecutiveSummary]: ['s2-quality-cost', 'p1a-quality', 's2-quality-cost', 'bogus'],
        [InternalBrief]: [],
        9: ['p1a-quality']
      } as unknown as ReportChartSelection;
      expect(normalizeChartSelection(messy)).toEqual({
        [ExecutiveSummary]: ['p1a-quality', 's2-quality-cost'],
        [InternalBrief]: []
      });
    });

    it('reads the defaults when nothing is stored, the record is unreadable or of another version', () => {
      expect(readStoredChartSelection()).toEqual(DEFAULT_CHART_SELECTION);
      localStorage.setItem(REPORT_CHART_STORAGE_KEY, '{not json');
      expect(readStoredChartSelection()).toEqual(DEFAULT_CHART_SELECTION);
      localStorage.setItem(REPORT_CHART_STORAGE_KEY, JSON.stringify({ version: 2, selection: { [ExecutiveSummary]: [] } }));
      expect(readStoredChartSelection()).toEqual(DEFAULT_CHART_SELECTION);
    });

    it('stores a version-1 record and reads it back normalized', () => {
      storeChartSelection({ [ExecutiveSummary]: ['p1c-cost', 'p1a-quality'], [TechnicalReport]: [] });
      expect(JSON.parse(localStorage.getItem(REPORT_CHART_STORAGE_KEY)!)).toEqual({
        version: 1,
        selection: { [ExecutiveSummary]: ['p1a-quality', 'p1c-cost'], [TechnicalReport]: [] }
      });
      expect(readStoredChartSelection()).toEqual({ [ExecutiveSummary]: ['p1a-quality', 'p1c-cost'], [TechnicalReport]: [] });
    });

    it('survives storage that throws', () => {
      spyOn(localStorage, 'getItem').and.throwError('denied');
      spyOn(localStorage, 'setItem').and.throwError('denied');
      expect(() => storeChartSelection(DEFAULT_CHART_SELECTION)).not.toThrow();
      expect(readStoredChartSelection()).toEqual(DEFAULT_CHART_SELECTION);
    });
  });

  describe('the settings hash', () => {
    const settings = (overrides: Partial<ReportChartSettingsInput> = {}): ReportChartSettingsInput => ({
      figureStyle: { theme: 'dark', font: 'Lato' },
      layout: DOCUMENT_CHART_LAYOUT,
      show: ['run:1', 'run:2'],
      highlight: [],
      order: { mode: 'score' },
      measures: { speed: 'median', cost: 'perQuestion' },
      pricingBasis: 'Current',
      computedAtUtc: '2026-09-30T10:00:00Z',
      ...overrides
    });

    it('writes canonical JSON: keys sorted at every depth, arrays in order, undefined left out', () => {
      expect(canonicalJson({ b: 1, a: { d: [3, { z: true, y: null }], c: 'x' }, u: undefined }))
        .toBe('{"a":{"c":"x","d":[3,{"y":null,"z":true}]},"b":1}');
      // Integer-like keys sort as text too, unlike an object's own key order.
      expect(canonicalJson({ 10: 'ten', 2: 'two', a: 'a' })).toBe('{"10":"ten","2":"two","a":"a"}');
      expect(canonicalJson([undefined, Number.NaN])).toBe('[null,null]');
    });

    it('hashes to 64 lowercase hex characters, pinned, whatever the key order', async () => {
      const hash = await chartSettingsHash(settings());
      expect(hash).toBe('ef5d0fb4513d67a6ff96b70ce74daa3ff09e848f22d5fb0fafb05ccabbaae6fc');
      const reordered = await chartSettingsHash(settings({ figureStyle: { font: 'Lato', theme: 'dark' } }));
      expect(reordered).toBe(hash);
    });

    it('changes with anything that shapes the images', async () => {
      const base = await chartSettingsHash(settings());
      expect(await chartSettingsHash(settings({ computedAtUtc: '2026-09-30T10:00:01Z' }))).not.toBe(base);
      expect(await chartSettingsHash(settings({ pricingBasis: 'AsRun' }))).not.toBe(base);
      expect(await chartSettingsHash(settings({ show: ['run:2', 'run:1'] }))).not.toBe(base);
      expect(await chartSettingsHash(settings({ highlight: ['run:1'] }))).not.toBe(base);
    });
  });

  describe('ReportChartPublisher', () => {
    let puts: { id: number; charts: ReportDocumentChartUpload[] }[];
    let putResponse: (id: number, charts: ReportDocumentChartUpload[]) => Observable<ReportDocumentChartsSummaryDto>;
    let service: AdminBenchmarkService;
    let composed: string[];
    let rejectKeys: Set<string>;
    let onCompose: ((key: ReportChartFigureKey, variant: ReportChartVariant) => void) | null;

    const target = (documentId: number, audience: BenchmarkReportAudience, letters: Record<string, string> = { 'run:2': 'A' }): ReportChartTarget => ({
      documentId,
      audience,
      subjectKey: 'run:1',
      peerLetters: letters,
      label: `Document ${documentId}`
    });

    const compose = async (key: ReportChartFigureKey, variant: ReportChartVariant): Promise<ComposedReportChart> => {
      composed.push(`${key}/${variant.kind}`);
      onCompose?.(key, variant);
      if (rejectKeys.has(key)) {
        throw new Error(`cannot draw ${key}`);
      }
      return {
        png: new Blob([new Uint8Array([1, 2, 3])], { type: 'image/png' }),
        widthPx: 1800,
        heightPx: 1125,
        title: `Title ${key}`,
        caption: `Caption ${key}`,
        altText: `Alt ${key}`
      };
    };

    beforeEach(() => {
      puts = [];
      composed = [];
      rejectKeys = new Set();
      onCompose = null;
      putResponse = (id, charts) => of({ documentId: id, chartCount: charts.length, figureKeys: [], settingsHash: HASH });
      service = {
        putReportDocumentCharts: (id: number, charts: ReportDocumentChartUpload[]) => {
          puts.push({ id, charts });
          return putResponse(id, charts);
        }
      } as unknown as AdminBenchmarkService;
    });

    it('charts one document at a time, in order: each selected figure named, then anonymized, then one PUT', async () => {
      const publisher = new ReportChartPublisher(service);
      const progress: ReportChartPublishProgress[] = [];
      const selection: ReportChartSelection = {
        [ExecutiveSummary]: ['s2-quality-cost', 'p1a-quality'],
        [InternalBrief]: ['p1b-speed']
      };

      const running = publisher.publish([target(11, ExecutiveSummary), target(12, InternalBrief, {})], selection, compose, HASH,
        p => progress.push(p));
      expect(publisher.running).toBeTrue();
      const result = await running;

      expect(publisher.running).toBeFalse();
      expect(composed).toEqual([
        'p1a-quality/named', 'p1a-quality/anonymized', 's2-quality-cost/named', 's2-quality-cost/anonymized',
        'p1b-speed/named'
      ]);
      expect(puts.map(put => put.id)).toEqual([11, 12]);
      expect(puts[0].charts.map(chart => `${chart.figureKey}/${chart.naming}`))
        .toEqual(['p1a-quality/named', 'p1a-quality/anonymized', 's2-quality-cost/named', 's2-quality-cost/anonymized']);
      expect(puts[0].charts[0]).toEqual({
        figureKey: 'p1a-quality',
        naming: 'named',
        title: 'Title p1a-quality',
        caption: 'Caption p1a-quality',
        altText: 'Alt p1a-quality',
        settingsHash: HASH,
        pngBase64: 'AQID'
      });
      expect(result).toEqual({
        published: [{ documentId: 11, chartCount: 4 }, { documentId: 12, chartCount: 1 }],
        failed: [],
        skipped: [],
        canceled: false,
        storageNotConfigured: null
      });

      // Four images and one upload, then one image and one upload.
      const last = progress[progress.length - 1];
      expect(last.total).toBe(7);
      expect(last.done).toBe(7);
      expect(last.documentId).toBeNull();
      expect(progress.some(p => p.documentId === 11 && p.step.startsWith('Drawing Intelligence (anonymized)'))).toBeTrue();
      expect(progress.every((p, i) => i === 0 || p.done >= progress[i - 1].done)).toBeTrue();
    });

    it('skips a document whose selection is empty, uploading nothing for it', async () => {
      const publisher = new ReportChartPublisher(service);
      const result = await publisher.publish([target(11, ExecutiveSummary), target(12, TechnicalReport)],
        { [ExecutiveSummary]: [], [TechnicalReport]: ['p1c-cost'] }, compose, HASH);

      expect(result.skipped).toEqual([11]);
      expect(puts.map(put => put.id)).toEqual([12]);
    });

    it('stops after the document in flight when canceled', async () => {
      const publisher = new ReportChartPublisher(service);
      onCompose = () => publisher.cancel();
      const result = await publisher.publish([target(11, ExecutiveSummary), target(12, ExecutiveSummary)],
        { [ExecutiveSummary]: ['p1a-quality'] }, compose, HASH);

      expect(puts.map(put => put.id)).toEqual([11]);
      expect(result.canceled).toBeTrue();
      expect(result.published).toEqual([{ documentId: 11, chartCount: 2 }]);
      expect(publisher.running).toBeFalse();
    });

    it('leaves out a figure the composer rejects, and fails a document none of whose figures could be drawn', async () => {
      rejectKeys = new Set(['p1b-speed']);
      const publisher = new ReportChartPublisher(service);
      const progress: ReportChartPublishProgress[] = [];
      const result = await publisher.publish([target(11, ExecutiveSummary, {}), target(12, InternalBrief, {})],
        { [ExecutiveSummary]: ['p1a-quality', 'p1b-speed'], [InternalBrief]: ['p1b-speed'] }, compose, HASH,
        p => progress.push(p));

      expect(puts.map(put => put.id)).toEqual([11]);
      expect(puts[0].charts.map(chart => chart.figureKey)).toEqual(['p1a-quality']);
      expect(progress.some(p => p.step.includes('Could not draw Speed (named)') && p.step.includes('cannot draw p1b-speed'))).toBeTrue();
      expect(result.published).toEqual([{ documentId: 11, chartCount: 1 }]);
      expect(result.failed.length).toBe(1);
      expect(result.failed[0].documentId).toBe(12);
      expect(result.failed[0].message).toContain('No chart could be drawn');
    });

    it('records a failed upload and carries on with the next document', async () => {
      putResponse = (id, charts) => id === 11
        ? throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Server Error', error: { error: 'Disk full.' } }))
        : of({ documentId: id, chartCount: charts.length, figureKeys: [], settingsHash: HASH });
      const publisher = new ReportChartPublisher(service);
      const result = await publisher.publish([target(11, ExecutiveSummary), target(12, ExecutiveSummary)],
        { [ExecutiveSummary]: ['p1a-quality'] }, compose, HASH);

      expect(puts.map(put => put.id)).toEqual([11, 12]);
      expect(result.failed).toEqual([{ documentId: 11, message: 'Disk full.' }]);
      expect(result.published).toEqual([{ documentId: 12, chartCount: 2 }]);
      expect(result.storageNotConfigured).toBeNull();
    });

    it('stops the whole publish on a 400 saying chart storage is not configured, and reports it once', async () => {
      const message = `${CHART_STORAGE_NOT_CONFIGURED}. Set ReportCharts:Location.`;
      putResponse = () => throwError(() => new HttpErrorResponse({ status: 400, statusText: 'Bad Request', error: { error: message } }));
      const publisher = new ReportChartPublisher(service);
      const progress: ReportChartPublishProgress[] = [];
      const result = await publisher.publish([target(11, ExecutiveSummary), target(12, ExecutiveSummary), target(13, TechnicalReport)],
        { [ExecutiveSummary]: ['p1a-quality'], [TechnicalReport]: ['p1a-quality'] }, compose, HASH, p => progress.push(p));

      expect(puts.map(put => put.id)).toEqual([11]);
      expect(result.storageNotConfigured).toBe(message);
      expect(result.failed).toEqual([]);
      expect(result.published).toEqual([]);
      expect(result.canceled).toBeFalse();
      expect(progress.filter(p => p.step === message).length).toBe(1);
    });

    it('treats any other 400 as a failure of that document alone', async () => {
      putResponse = (id, charts) => id === 11
        ? throwError(() => new HttpErrorResponse({ status: 400, statusText: 'Bad Request', error: { error: 'A stand-alone document takes no charts.' } }))
        : of({ documentId: id, chartCount: charts.length, figureKeys: [], settingsHash: HASH });
      const publisher = new ReportChartPublisher(service);
      const result = await publisher.publish([target(11, ExecutiveSummary), target(12, ExecutiveSummary)],
        { [ExecutiveSummary]: ['p1a-quality'] }, compose, HASH);

      expect(result.failed).toEqual([{ documentId: 11, message: 'A stand-alone document takes no charts.' }]);
      expect(result.published.map(p => p.documentId)).toEqual([12]);
      expect(result.storageNotConfigured).toBeNull();
    });

    it('refuses a second publish while one runs', async () => {
      const publisher = new ReportChartPublisher(service);
      const first = publisher.publish([target(11, ExecutiveSummary)], { [ExecutiveSummary]: ['p1a-quality'] }, compose, HASH);
      await expectAsync(publisher.publish([target(12, ExecutiveSummary)], { [ExecutiveSummary]: ['p1a-quality'] }, compose, HASH))
        .toBeRejectedWithError(/already running/);
      await first;
    });
  });
});
