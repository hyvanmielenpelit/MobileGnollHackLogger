import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { AdminChatConsistencyService, CHAT_CONSISTENCY_ENDPOINT, ccErrorText } from './admin-chat-consistency.service';
import { BenchmarkReportAudience } from './admin-benchmark.service';

const BASE = CHAT_CONSISTENCY_ENDPOINT;

describe('AdminChatConsistencyService', () => {
  let service: AdminChatConsistencyService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AdminChatConsistencyService, provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(AdminChatConsistencyService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('uses the controller route prefix', () => {
    expect(BASE).toBe('/api/admin/benchmark/chat-consistency');
  });

  it('lists the model axes with GET models', () => {
    let count = -1;
    service.listModels().subscribe(models => count = models.length);
    const req = http.expectOne(`${BASE}/models`);
    expect(req.request.method).toBe('GET');
    req.flush([{ key: 'a' }, { key: 'b' }]);
    expect(count).toBe(2);
  });

  it('reads the timeline with the model key and both UTC bounds', () => {
    service.getTimeline('openai/gpt|high', '2026-09-01T00:00:00.000Z', '2026-10-01T23:59:59.999Z').subscribe();
    const req = http.expectOne(r => r.url === `${BASE}/timeline`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('modelKey')).toBe('openai/gpt|high');
    expect(req.request.params.get('from')).toBe('2026-09-01T00:00:00.000Z');
    expect(req.request.params.get('to')).toBe('2026-10-01T23:59:59.999Z');
    req.flush({});
  });

  it('leaves a missing bound out of the run table query', () => {
    service.getRuns('m', null, '2026-10-01T23:59:59.999Z').subscribe();
    const req = http.expectOne(r => r.url === `${BASE}/runs`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('modelKey')).toBe('m');
    expect(req.request.params.has('from')).toBe(false);
    expect(req.request.params.get('to')).toBe('2026-10-01T23:59:59.999Z');
    req.flush([]);
  });

  it('reads the comparison sets with the model key and both UTC bounds', () => {
    let defaultKey: string | null | undefined;
    service.getComparisonSets('openai/gpt|high', '2026-09-01T00:00:00.000Z', '2026-10-01T23:59:59.999Z')
      .subscribe(sets => defaultKey = sets.defaultKey);
    const req = http.expectOne(r => r.url === `${BASE}/comparison-sets`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('modelKey')).toBe('openai/gpt|high');
    expect(req.request.params.get('from')).toBe('2026-09-01T00:00:00.000Z');
    expect(req.request.params.get('to')).toBe('2026-10-01T23:59:59.999Z');
    req.flush({ sets: [], defaultKey: 'suite:id:5' });
    expect(defaultKey).toBe('suite:id:5');
  });

  it('reads the battery runs, leaving a missing bound out', () => {
    let count = -1;
    service.getBatteryRuns('m', '2026-09-01T00:00:00.000Z', null).subscribe(rows => count = rows.length);
    const req = http.expectOne(r => r.url === `${BASE}/battery-runs`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('modelKey')).toBe('m');
    expect(req.request.params.get('from')).toBe('2026-09-01T00:00:00.000Z');
    expect(req.request.params.has('to')).toBe(false);
    req.flush([{ batteryRunId: 12 }]);
    expect(count).toBe(1);
  });

  it('posts an analysis request as the body of POST analyses', () => {
    const body = {
      subjectModelKey: 'm',
      baselineStartUtc: '2026-09-01T00:00:00.000Z', baselineEndUtc: '2026-09-14T23:59:59.999Z',
      comparisonStartUtc: '2026-09-15T00:00:00.000Z', comparisonEndUtc: '2026-09-28T23:59:59.999Z',
      baselineRunIds: [1, 2], comparisonRunIds: [3, 4], controlRunIds: [9], relaxedPooling: true,
      protocolOverrides: { margins: { P1: 4 }, alpha: 0.1 }
    };
    service.analyze(body).subscribe();
    const req = http.expectOne(`${BASE}/analyses`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({ analysisId: 7 });
  });

  it('lists, reads and deletes saved analyses', () => {
    service.listAnalyses().subscribe();
    const list = http.expectOne(`${BASE}/analyses`);
    expect(list.request.method).toBe('GET');
    list.flush([]);

    service.getAnalysis(12).subscribe();
    const one = http.expectOne(`${BASE}/analyses/12`);
    expect(one.request.method).toBe('GET');
    one.flush({});

    service.deleteAnalysis(12).subscribe();
    const del = http.expectOne(`${BASE}/analyses/12`);
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('reads a saved analysis\'s freshness with GET analyses/{id}/freshness', () => {
    let outOfDate: boolean | undefined;
    service.getAnalysisFreshness(12).subscribe(freshness => outOfDate = freshness.outOfDate);
    const req = http.expectOne(`${BASE}/analyses/12/freshness`);
    expect(req.request.method).toBe('GET');
    req.flush({
      analysisId: 12, analysisCodeVersion: 5, currentAnalysisCodeVersion: 6, earlierAnalysisCode: true,
      inputsChanged: null, inputsNote: 'Saved before the request was stored.', outOfDate: true
    });
    expect(outOfDate).toBe(true);
  });

  it('estimates a re-grade with the run ids and the assessor', () => {
    service.estimateRegrade([3, 4], 21).subscribe();
    const req = http.expectOne(`${BASE}/regrade/estimate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ runIds: [3, 4], assessorConfigId: 21 });
    req.flush({});
  });

  it('starts a re-grade only with confirmed: true', () => {
    service.startRegrade([3], 21).subscribe();
    const req = http.expectOne(`${BASE}/regrade`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ runIds: [3], assessorConfigId: 21, confirmed: true });
    req.flush({ id: 'j', status: 'running' }, { status: 202, statusText: 'Accepted' });
  });

  it('reads the re-grade job, null on 204, and cancels it', () => {
    let job: unknown = 'unset';
    service.getRegradeJob().subscribe(value => job = value);
    const get = http.expectOne(`${BASE}/regrade/job`);
    expect(get.request.method).toBe('GET');
    get.flush(null, { status: 204, statusText: 'No Content' });
    expect(job).toBeNull();

    service.getRegradeJob().subscribe(value => job = value);
    http.expectOne(`${BASE}/regrade/job`).flush({ id: 'j', status: 'running' });
    expect((job as { id: string }).id).toBe('j');

    service.cancelRegrade().subscribe();
    const cancel = http.expectOne(`${BASE}/regrade/cancel`);
    expect(cancel.request.method).toBe('POST');
    expect(cancel.request.body).toEqual({});
    cancel.flush({}, { status: 202, statusText: 'Accepted' });
  });

  it('marks and unmarks an anchor with PUT runs/{id}/anchor', () => {
    service.setAnchor(55, true).subscribe();
    const mark = http.expectOne(`${BASE}/runs/55/anchor`);
    expect(mark.request.method).toBe('PUT');
    expect(mark.request.body).toEqual({ isAnchor: true });
    mark.flush({ runId: 55, isAnchor: true });

    service.setAnchor(55, false).subscribe();
    const unmark = http.expectOne(`${BASE}/runs/55/anchor`);
    expect(unmark.request.body).toEqual({ isAnchor: false });
    unmark.flush({ runId: 55, isAnchor: false });
  });

  it('lists annotations by provider and model, adds and deletes one', () => {
    service.listAnnotations('OpenAI', 'gpt-5').subscribe();
    const list = http.expectOne(r => r.url === `${BASE}/annotations`);
    expect(list.request.method).toBe('GET');
    expect(list.request.params.get('provider')).toBe('OpenAI');
    expect(list.request.params.get('modelId')).toBe('gpt-5');
    list.flush([]);

    service.listAnnotations().subscribe();
    const all = http.expectOne(r => r.url === `${BASE}/annotations`);
    expect(all.request.params.keys()).toEqual([]);
    all.flush([]);

    const body = {
      atUtc: '2026-10-01T12:00:00.000Z', provider: 'OpenAI', modelId: null, kind: 'modelRelease' as const,
      text: 'New snapshot', sourceUrl: 'https://example.com/notes'
    };
    service.addAnnotation(body).subscribe();
    const add = http.expectOne(`${BASE}/annotations`);
    expect(add.request.method).toBe('POST');
    expect(add.request.body).toEqual(body);
    add.flush({ id: 3 });

    service.deleteAnnotation(3).subscribe();
    const del = http.expectOne(`${BASE}/annotations/3`);
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('estimates and writes an analysis\'s reports with the Provider Issue Report among the audiences', () => {
    service.estimateReports(7, { writerModelConfigurationId: 30, audiences: [BenchmarkReportAudience.ProviderIssueReport] }).subscribe();
    const estimate = http.expectOne(`${BASE}/analyses/7/report-documents/estimate`);
    expect(estimate.request.method).toBe('POST');
    expect(estimate.request.body).toEqual({ writerModelConfigurationId: 30, audiences: [4] });
    estimate.flush({ estimates: [], providerIssueReportAvailable: true, providerIssueReportReason: null });

    service.writeReports(7, {
      writerModelConfigurationId: 30,
      audiences: [BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.ProviderIssueReport],
      acknowledgeSameProvider: true
    }).subscribe();
    const write = http.expectOne(`${BASE}/analyses/7/report-documents`);
    expect(write.request.method).toBe('POST');
    expect(write.request.body).toEqual({ writerModelConfigurationId: 30, audiences: [1, 4], acknowledgeSameProvider: true });
    write.flush({ runId: 7, status: 1 }, { status: 202, statusText: 'Accepted' });

    service.writeReports(7, {
      writerModelConfigurationId: 30, audiences: [BenchmarkReportAudience.ExecutiveSummary], acknowledgeOutOfDate: true
    }).subscribe();
    const acknowledged = http.expectOne(`${BASE}/analyses/7/report-documents`);
    expect(acknowledged.request.body).toEqual({ writerModelConfigurationId: 30, audiences: [1], acknowledgeOutOfDate: true });
    acknowledged.flush({ runId: 7, status: 1 }, { status: 202, statusText: 'Accepted' });
  });

  it('reads the report job, null on 204, and cancels it', () => {
    let job: unknown = 'unset';
    service.getReportJob(7).subscribe(value => job = value);
    const get = http.expectOne(`${BASE}/analyses/7/report-documents/job`);
    expect(get.request.method).toBe('GET');
    get.flush(null, { status: 204, statusText: 'No Content' });
    expect(job).toBeNull();

    service.cancelReportJob(7).subscribe();
    const cancel = http.expectOne(`${BASE}/analyses/7/report-documents/cancel`);
    expect(cancel.request.method).toBe('POST');
    cancel.flush({}, { status: 202, statusText: 'Accepted' });
  });
});

describe('ccErrorText', () => {
  it('reads the { error } body, a string body, then the status', () => {
    expect(ccErrorText(new HttpErrorResponse({ status: 409, error: { error: 'Reports reference it.' } }))).toBe('Reports reference it.');
    expect(ccErrorText(new HttpErrorResponse({ status: 429, error: 'Spend cap reached' }))).toBe('Spend cap reached');
    expect(ccErrorText(new HttpErrorResponse({ status: 0 }))).toBe('The server could not be reached.');
    expect(ccErrorText(new HttpErrorResponse({ status: 500 }), 'It failed.')).toBe('It failed. (HTTP 500)');
    expect(ccErrorText(new Error('x'), 'Fallback')).toBe('Fallback');
  });
});
