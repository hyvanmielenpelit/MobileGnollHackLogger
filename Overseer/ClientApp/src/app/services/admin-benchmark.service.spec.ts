import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import {
  AdminBenchmarkService,
  BenchmarkBinaryFile,
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportPackPricingBasis,
  BenchmarkReportPackRequest,
  BenchmarkReportPeerNaming,
  BenchmarkTextFile,
  decodeBinaryErrorBody,
  fileNameFromContentDisposition,
  reportDisclosureParam,
  reportPeerNamingParam
} from './admin-benchmark.service';

describe('AdminBenchmarkService', () => {
  let service: AdminBenchmarkService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AdminBenchmarkService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    service = TestBed.inject(AdminBenchmarkService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should get suites', () => {
    const mockSuites = [{ id: 1, name: 'Default Suite', description: 'Test', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null, questionCount: 15 }];
    service.getSuites().subscribe(res => {
      expect(res.length).toBe(1);
      expect(res[0].name).toBe('Default Suite');
    });

    const req = httpMock.expectOne('/api/admin/benchmark/suites');
    expect(req.request.method).toBe('GET');
    req.flush(mockSuites);
  });

  it('should create suite', () => {
    service.createSuite({ name: 'New Suite', description: 'Desc' }).subscribe(res => {
      expect(res.name).toBe('New Suite');
    });

    const req = httpMock.expectOne('/api/admin/benchmark/suites');
    expect(req.request.method).toBe('POST');
    req.flush({ id: 2, name: 'New Suite', description: 'Desc', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null, questionCount: 0 });
  });

  it('should start a run', () => {
    service.startRun({ suiteId: 1, testedModelConfigurationId: 10, assessorModelConfigurationId: 20 }).subscribe(res => {
      expect(res.runId).toBe(42);
    });

    const req = httpMock.expectOne('/api/admin/benchmark/runs');
    expect(req.request.method).toBe('POST');
    req.flush({ runId: 42 });
  });

  it('should get the active run from the runs/active endpoint', () => {
    let result: { runId: number } | null | undefined;
    service.getActiveRun().subscribe(res => result = res);

    const req = httpMock.expectOne('/api/admin/benchmark/runs/active');
    expect(req.request.method).toBe('GET');
    req.flush({ runId: 42 });

    expect(result).toEqual({ runId: 42 });
  });

  it('should surface an idle server (204, empty body) as null', () => {
    let result: { runId: number } | null | undefined = { runId: 1 };
    service.getActiveRun().subscribe(res => result = res);

    const req = httpMock.expectOne('/api/admin/benchmark/runs/active');
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(result).toBeNull();
  });

  it('should retry claim verification for a run', () => {
    service.retryClaimVerification(42).subscribe(res => {
      expect(res.runId).toBe(42);
    });

    const req = httpMock.expectOne('/api/admin/benchmark/runs/42/retry-claim-verification');
    expect(req.request.method).toBe('POST');
    req.flush({ runId: 42 });
  });

  it('should get the default suite catalog', () => {
    const mockCatalog = [
      {
        key: 'gnollhack-player-assistance', version: 1, name: 'GnollHack Player Assistance Benchmark Suite',
        description: 'Core roguelike mechanics.', questionCount: 18,
        difficultyCounts: { Simple: 6, Intermediate: 6, Advanced: 6 },
        fileName: 'gnollhack_player_assistance.json', error: null,
        alreadyImportedCount: 0, alreadyImportedNames: [], nameMatchedSuiteNames: []
      }
    ];

    service.getDefaultSuiteCatalog().subscribe(res => {
      expect(res.length).toBe(1);
      expect(res[0].key).toBe('gnollhack-player-assistance');
    });

    const req = httpMock.expectOne('/api/admin/benchmark/suites/default-catalog');
    expect(req.request.method).toBe('GET');
    req.flush(mockCatalog);
  });

  it('should import default suites by key', () => {
    const mockResult = { imported: [{ id: 5, name: 'Imported Suite' }], skipped: [] };

    service.importDefaultSuites(['gnollhack-player-assistance']).subscribe(res => {
      expect(res.imported.length).toBe(1);
      expect(res.imported[0].name).toBe('Imported Suite');
    });

    const req = httpMock.expectOne('/api/admin/benchmark/suites/import-default');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ keys: ['gnollhack-player-assistance'] });
    req.flush(mockResult);
  });

  it('should post a suite description generation request', () => {
    const mockResult = {
      suiteId: 5,
      suiteName: 'Board Suite',
      questionCount: 18,
      snapshotIncluded: true,
      gameSnapshotName: 'Gnomish Mines level 3',
      snapshotCharCount: 4000,
      promptCharCount: 6500,
      generatorConfigId: 7,
      startedAtUtc: '2026-09-16T08:00:00Z',
      completedAtUtc: '2026-09-16T08:01:00Z',
      durationMs: 60000,
      modelCalls: 1,
      promptTokens: 1000,
      uncachedInputTokens: 1000,
      cacheReadTokens: 0,
      cacheCreationTokens: 0,
      outputTokens: 300,
      reasoningTokens: 0,
      tokensEstimated: false,
      costUsd: 0.0123,
      pricingSource: 'catalog',
      status: 'Completed',
      description: '## Draft description',
      log: []
    };
    const body = {
      generatorModelConfigurationId: 7,
      instructions: 'Emphasize the difficulty spread.',
      includeSnapshot: true,
      includeDebugText: false
    };

    service.generateSuiteDescription(5, body).subscribe(res => {
      expect(res.status).toBe('Completed');
      expect(res.description).toBe('## Draft description');
    });

    const req = httpMock.expectOne('/api/admin/benchmark/suites/5/description-generation');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(mockResult);
  });

  it('should get the comparability index with repeated runIds and groupIds params', () => {
    const mockIndex = {
      computedAtUtc: '2026-09-01T00:00:00Z',
      entries: [],
      conditions: [],
      largestConditionKeys: [],
      referenceSelectionRule: 'The reference condition is the one with the most sources.',
      mustMatchKeyNames: [],
      modelAxisKeyNames: [],
      degradingKeyNames: []
    };

    service.getComparabilityIndex({ runIds: [1, 2], groupIds: [3] }).subscribe(res => {
      expect(res).toEqual(mockIndex as any);
    });

    const req = httpMock.expectOne(request => request.url === '/api/admin/benchmark/model-comparison/comparability');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.getAll('runIds')).toEqual(['1', '2']);
    expect(req.request.params.getAll('groupIds')).toEqual(['3']);
    req.flush(mockIndex);
  });

  describe('report packs', () => {
    const packRequest: BenchmarkReportPackRequest = {
      runIds: [11, 12],
      groupIds: [3],
      pricingBasis: BenchmarkReportPackPricingBasis.Current,
      subjectKey: 'run:11',
      audiences: [BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport],
      writerModelConfigurationId: 7,
      acknowledgeSameProvider: false
    };

    it('posts a preview request with numeric enums', () => {
      let result: unknown;
      service.previewReportPack(packRequest).subscribe(res => result = res);

      const req = httpMock.expectOne('/api/admin/benchmark/report-packs/preview');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(packRequest);
      expect(req.request.body.pricingBasis).toBe(1);
      expect(req.request.body.audiences).toEqual([1, 2]);
      req.flush({ subjectKey: 'run:11', refusal: null });

      expect(result).toEqual({ subjectKey: 'run:11', refusal: null });
    });

    it('posts a start request and returns the job id', () => {
      let jobId: string | undefined;
      service.startReportPack(packRequest).subscribe(res => jobId = res.jobId);

      const req = httpMock.expectOne('/api/admin/benchmark/report-packs');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(packRequest);
      req.flush({ jobId: 'abc' }, { status: 202, statusText: 'Accepted' });

      expect(jobId).toBe('abc');
    });

    it('gets a job by id', () => {
      service.getReportPackJob('job-1').subscribe();
      const req = httpMock.expectOne('/api/admin/benchmark/report-packs/jobs/job-1');
      expect(req.request.method).toBe('GET');
      req.flush({ id: 'job-1' });
    });

    it('surfaces an idle report-pack job manager (204) as null', () => {
      let result: unknown = 'unset';
      service.getActiveReportPackJob().subscribe(res => result = res);

      const req = httpMock.expectOne('/api/admin/benchmark/report-packs/jobs/active');
      expect(req.request.method).toBe('GET');
      req.flush(null, { status: 204, statusText: 'No Content' });

      expect(result).toBeNull();
    });

    it('cancels a job', () => {
      let cancelled: boolean | undefined;
      service.cancelReportPackJob('job-1').subscribe(res => cancelled = res.cancelled);

      const req = httpMock.expectOne('/api/admin/benchmark/report-packs/jobs/job-1/cancel');
      expect(req.request.method).toBe('POST');
      req.flush({ cancelled: true });

      expect(cancelled).toBeTrue();
    });
  });

  describe('report documents', () => {
    it('lists documents with only the query parameters given', () => {
      service.listReportDocuments({ runId: 42 }).subscribe();

      const req = httpMock.expectOne(request => request.url === '/api/admin/benchmark/report-documents');
      expect(req.request.method).toBe('GET');
      expect(req.request.params.get('runId')).toBe('42');
      expect(req.request.params.has('suiteId')).toBeFalse();
      expect(req.request.params.has('take')).toBeFalse();
      req.flush([]);
    });

    it('lists documents by suite with a take', () => {
      service.listReportDocuments({ suiteId: 5, take: 20 }).subscribe();

      const req = httpMock.expectOne(request => request.url === '/api/admin/benchmark/report-documents');
      expect(req.request.params.get('suiteId')).toBe('5');
      expect(req.request.params.get('take')).toBe('20');
      expect(req.request.params.has('runId')).toBeFalse();
      req.flush([]);
    });

    it('writes the AI-written reports of a run with the chosen writer', () => {
      let status: number | undefined;
      service.writeRunReportDocuments(73, { writerModelConfigurationId: 5 }).subscribe(res => status = res.status);

      const req = httpMock.expectOne('/api/admin/benchmark/runs/73/report-documents');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ writerModelConfigurationId: 5 });
      req.flush({ runId: 73, status: 1 }, { status: 202, statusText: 'Accepted' });

      expect(status).toBe(1);
    });

    it('gets a document detail', () => {
      service.getReportDocument(9).subscribe();
      const req = httpMock.expectOne('/api/admin/benchmark/report-documents/9');
      expect(req.request.method).toBe('GET');
      req.flush({ id: 9 });
    });

    it('renders a document as text with lower-case query names', () => {
      let text: string | undefined;
      service.renderReportDocument(9, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Anonymized)
        .subscribe(res => text = res);

      const req = httpMock.expectOne(request => request.url === '/api/admin/benchmark/report-documents/9/render');
      expect(req.request.method).toBe('GET');
      expect(req.request.responseType).toBe('text');
      expect(req.request.params.get('disclosure')).toBe('detailed');
      expect(req.request.params.get('peers')).toBe('anonymized');
      req.flush('# Report\n');

      expect(text).toBe('# Report\n');
    });

    it('maps every disclosure and naming to its query value', () => {
      expect(reportDisclosureParam(BenchmarkReportDisclosure.Summary)).toBe('summary');
      expect(reportDisclosureParam(BenchmarkReportDisclosure.Detailed)).toBe('detailed');
      expect(reportDisclosureParam(BenchmarkReportDisclosure.Full)).toBe('full');
      expect(reportPeerNamingParam(BenchmarkReportPeerNaming.Named)).toBe('named');
      expect(reportPeerNamingParam(BenchmarkReportPeerNaming.Anonymized)).toBe('anonymized');
    });

    it('deletes a document', () => {
      service.deleteReportDocument(9).subscribe();
      const req = httpMock.expectOne('/api/admin/benchmark/report-documents/9');
      expect(req.request.method).toBe('DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });
    });
  });

  describe('run files as text', () => {
    it('fetches the run report as text and keeps the quoted server file name', () => {
      let file: BenchmarkTextFile | undefined;
      service.getRunReportText(42).subscribe(res => file = res);

      const req = httpMock.expectOne('/api/admin/benchmark/runs/42/report');
      expect(req.request.method).toBe('GET');
      expect(req.request.responseType).toBe('text');
      req.flush('# Run 42\n', {
        headers: { 'Content-Disposition': 'attachment; filename="Suite_Model_20260901_101500.md"' }
      });

      expect(file).toEqual({ text: '# Run 42\n', fileName: 'Suite_Model_20260901_101500.md' });
    });

    it('prefers the RFC 5987 file name of the tool-call log', () => {
      let file: BenchmarkTextFile | undefined;
      service.getToolCallLogText(42).subscribe(res => file = res);

      const req = httpMock.expectOne('/api/admin/benchmark/runs/42/tool-call-log');
      expect(req.request.responseType).toBe('text');
      req.flush('log', {
        headers: {
          'Content-Disposition': "attachment; filename=Suite_Model_run42_tool_calls.md; filename*=UTF-8''Suite_M%C3%B6del_run42_tool_calls.md"
        }
      });

      expect(file?.fileName).toBe('Suite_Mödel_run42_tool_calls.md');
    });

    it('falls back to a default name without a Content-Disposition header', () => {
      let file: BenchmarkTextFile | undefined;
      service.getToolCallLogText(42).subscribe(res => file = res);

      httpMock.expectOne('/api/admin/benchmark/runs/42/tool-call-log').flush('log');

      expect(file?.fileName).toBe('benchmark_run42_tool_calls.md');
    });

    it('fetches a report document as a PDF with its options and paper, and the server file name', () => {
      let file: BenchmarkBinaryFile | undefined;
      service.getReportDocumentPdf(9, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized, 'letter')
        .subscribe(res => file = res);

      const req = httpMock.expectOne(request => request.url === '/api/admin/benchmark/report-documents/9/render/pdf');
      expect(req.request.method).toBe('GET');
      expect(req.request.responseType).toBe('arraybuffer');
      expect(req.request.params.get('disclosure')).toBe('summary');
      expect(req.request.params.get('peers')).toBe('anonymized');
      expect(req.request.params.get('paper')).toBe('letter');
      req.flush(new Uint8Array([0x25, 0x50, 0x44, 0x46]).buffer, {
        headers: { 'Content-Disposition': 'attachment; filename="doc_summary_anonymized.pdf"' }
      });

      expect(Array.from(file!.bytes)).toEqual([0x25, 0x50, 0x44, 0x46]);
      expect(file!.fileName).toBe('doc_summary_anonymized.pdf');
    });

    it('fetches the run report and tool-call log PDFs on the chosen paper, with a null name when none is given', () => {
      let report: BenchmarkBinaryFile | undefined;
      let log: BenchmarkBinaryFile | undefined;
      service.getRunReportPdf(42, 'a4').subscribe(res => report = res);
      service.getToolCallLogPdf(42, 'letter').subscribe(res => log = res);

      const reportReq = httpMock.expectOne(request => request.url === '/api/admin/benchmark/runs/42/report/pdf');
      expect(reportReq.request.responseType).toBe('arraybuffer');
      expect(reportReq.request.params.get('paper')).toBe('a4');
      reportReq.flush(new ArrayBuffer(3), { headers: { 'Content-Disposition': 'attachment; filename=Suite_Model_20260901_101500_INTERNAL.pdf' } });
      const logReq = httpMock.expectOne(request => request.url === '/api/admin/benchmark/runs/42/tool-call-log/pdf');
      expect(logReq.request.params.get('paper')).toBe('letter');
      logReq.flush(new ArrayBuffer(2));

      expect(report?.fileName).toBe('Suite_Model_20260901_101500_INTERNAL.pdf');
      expect(report?.bytes.length).toBe(3);
      expect(log?.fileName).toBeNull();
    });

    it('posts the captured diagnostics text and time for their PDF', () => {
      service.renderDiagnosticsPdf(42, '=== DIAGNOSTICS ===\n', '2026-09-28T10:15:02Z', 'a4').subscribe();

      const req = httpMock.expectOne(request => request.url === '/api/admin/benchmark/runs/42/diagnostics/pdf');
      expect(req.request.method).toBe('POST');
      expect(req.request.responseType).toBe('arraybuffer');
      expect(req.request.params.get('paper')).toBe('a4');
      expect(req.request.body).toEqual({ text: '=== DIAGNOSTICS ===\n', capturedAtUtc: '2026-09-28T10:15:02Z' });
      req.flush(new ArrayBuffer(1));
    });

    it('decodes a JSON error body of a PDF request, so its message can be shown', () => {
      let error: HttpErrorResponse | undefined;
      service.getToolCallLogPdf(42, 'a4').subscribe({ error: (e: HttpErrorResponse) => error = e });

      const message = 'This document is too large for a PDF (4000001 characters); download the Markdown instead.';
      const body = new TextEncoder().encode(JSON.stringify({ error: message }));
      httpMock.expectOne(request => request.url === '/api/admin/benchmark/runs/42/tool-call-log/pdf')
        .flush(body.buffer.slice(body.byteOffset, body.byteOffset + body.byteLength), { status: 413, statusText: 'Payload Too Large' });

      expect(error).toBeInstanceOf(HttpErrorResponse);
      expect(error!.status).toBe(413);
      expect(error!.error).toEqual({ error: message });
    });

    it('decodes a non-JSON error body to its text and leaves other errors alone', () => {
      const text = new TextEncoder().encode('Bad paper');
      const decoded = decodeBinaryErrorBody(new HttpErrorResponse({ error: text.buffer, status: 400 })) as HttpErrorResponse;
      expect(decoded.error).toBe('Bad paper');
      expect(decoded.status).toBe(400);

      const plain = new HttpErrorResponse({ error: null, status: 404 });
      expect(decodeBinaryErrorBody(plain)).toBe(plain);
      const other = new Error('x');
      expect(decodeBinaryErrorBody(other)).toBe(other);
    });

    it('parses the Content-Disposition forms', () => {
      expect(fileNameFromContentDisposition(null, 'x.md')).toBe('x.md');
      expect(fileNameFromContentDisposition('attachment', 'x.md')).toBe('x.md');
      expect(fileNameFromContentDisposition('attachment; filename=plain.md', 'x.md')).toBe('plain.md');
      expect(fileNameFromContentDisposition('attachment; filename="with space.md"', 'x.md')).toBe('with space.md');
      expect(fileNameFromContentDisposition("attachment; filename*=UTF-8''a%20b.md", 'x.md')).toBe('a b.md');
      expect(fileNameFromContentDisposition("attachment; filename*=UTF-8''%E0%A4%A", 'x.md')).toBe('x.md');
    });
  });
});
