import { ChangeDetectorRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController, TestRequest } from '@angular/common/http/testing';

import {
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportDocumentOrigin,
  BenchmarkRunReportDocumentsStatus,
  ReportDocumentChartUpload
} from '../../../../services/admin-benchmark.service';
import { toModelPickerOptions } from '../../../../shared/model-picker/model-picker.component';
import { PdfViewerRequest } from '../../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { groupOverseerEvents } from '../chat-consistency-events';
import { CC_REPORT_CHART_STORAGE_KEY } from '../chat-consistency-report-chart-settings';
import {
  CC_API,
  CC_BATTERY_SET_KEY,
  ccAnalysisResult,
  ccBatteryPoint,
  ccConfig,
  ccEndpoint,
  ccFlushFreshness,
  ccFreshness,
  ccOutOfDateFreshness,
  ccPoint,
  ccReportEstimate,
  ccReportJob,
  ccSubjectWithLevel,
  ccTimeline,
  chatConsistencyTestProviders,
  textOf
} from '../chat-consistency-tab.testing';
import { CcAnalysisFreshness } from '../chat-consistency.models';
import {
  CC_ALL_WRITTEN_REASON,
  CC_ALL_WRITTEN_TEXT,
  CC_NO_DECISIVE_VERDICT_TEXT,
  CC_PROVIDER_REPORT_UNKNOWN,
  CC_REPORTS_STORAGE_KEY,
  CC_REPORT_ESTIMATE_DEBOUNCE_MS,
  CC_UPDATE_CHARTS_ATTACHING_REASON,
  CC_UPDATE_CHARTS_NONE_REASON,
  CC_UPDATE_CHARTS_WRITING_REASON,
  CcReportsStepComponent,
  ccReportIo
} from './reports-step.component';

const DOCUMENTS_URL = '/api/admin/benchmark/report-documents';

const settle = () => new Promise<void>(resolve => setTimeout(resolve, CC_REPORT_ESTIMATE_DEBOUNCE_MS + 60));

/** A written document of analysis 7, by the writer *Claude writer*. */
function ccDoc(id: number, audience: BenchmarkReportAudience, overrides: Partial<BenchmarkReportDocumentListItemDto> = {}): BenchmarkReportDocumentListItemDto {
  return {
    id,
    packId: `cc-pack-${id}`,
    audience,
    title: `Chat consistency ${id}: GPT-5 high`,
    subjectKey: 'chat-consistency:7',
    subjectLabel: 'GPT-5 high',
    subjectRunIds: [101, 102, 103, 104, 105, 106],
    suiteId: null,
    suiteName: '',
    writerDisplayName: 'Claude writer',
    writerProvider: 'Anthropic',
    writerModelId: 'claude-30',
    writerThinkingLevel: null,
    sameProviderAcknowledged: false,
    status: 'Completed',
    reportFormatVersion: 1,
    createdAtUtc: '2026-10-02T10:01:05Z',
    inputTokens: 30_000,
    outputTokens: 6_000,
    durationMs: 60_000,
    costUsd: 0.15,
    runChangedSinceGeneration: false,
    missingRunIds: [],
    allowedDisclosures: [BenchmarkReportDisclosure.Summary, BenchmarkReportDisclosure.Detailed, BenchmarkReportDisclosure.Full],
    origin: BenchmarkReportDocumentOrigin.ChatConsistencyReport,
    peerCount: 0,
    peerLetters: {},
    chartCount: 4,
    chatConsistencyAnalysisId: 7,
    ...overrides
  };
}

describe('CcReportsStepComponent', () => {
  let fixture: ComponentFixture<CcReportsStepComponent>;
  let http: HttpTestingController;
  let el: HTMLElement;
  const writer = ccConfig(30, { displayName: 'Claude writer', provider: 'Anthropic' });

  beforeEach(async () => {
    try {
      localStorage.removeItem(CC_REPORT_CHART_STORAGE_KEY);
    } catch {
      // No storage: the step starts from the default charts.
    }
    await TestBed.configureTestingModule({
      imports: [CcReportsStepComponent],
      providers: chatConsistencyTestProviders()
    }).compileComponents();
    fixture = TestBed.createComponent(CcReportsStepComponent);
    http = TestBed.inject(HttpTestingController);
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('result', ccAnalysisResult());
    fixture.componentRef.setInput('writerConfigs', [writer]);
    fixture.componentRef.setInput('writerOptions', toModelPickerOptions([writer]));
    fixture.componentRef.setInput('points', ccTimeline().points);
    fixture.detectChanges();
    http.expectOne(`${CC_API}/analyses/7/report-documents/job`).flush(null, { status: 204, statusText: 'No Content' });
    const documents = http.expectOne(r => r.url === DOCUMENTS_URL);
    expect(documents.request.params.get('subject')).toBe('chat-consistency:7');
    documents.flush([]);
    // The header's notice asks whether the analysis is out of date: it is current.
    http.expectOne(`${CC_API}/analyses/7/freshness`).flush(ccFreshness());
    fixture.detectChanges();
  });

  afterEach(() => {
    el.querySelectorAll('dialog').forEach(dialog => dialog.open && dialog.close());
    ccFlushFreshness(http);
    http.verify();
    fixture.destroy();
    vi.restoreAllMocks();
    try {
      localStorage.removeItem(CC_REPORTS_STORAGE_KEY);
      localStorage.removeItem(CC_REPORT_CHART_STORAGE_KEY);
    } catch {
      // No storage: nothing to clean.
    }
  });

  const component = () => fixture.componentInstance;
  const providerBox = () => el.querySelector<HTMLInputElement>('#cc-rep-audience-4')!;
  const row = (audience: BenchmarkReportAudience) => el.querySelector<HTMLElement>(`li.cds-row[data-audience="${audience}"]`)!;
  const writeButton = () => el.querySelector<HTMLButtonElement>('.cc-rep-write-btn')!;

  async function chooseWriter(analysisId = 7): Promise<TestRequest> {
    el.querySelector<HTMLButtonElement>('.cc-report-writer-model-selector .selector-trigger')!.click();
    fixture.detectChanges();
    el.querySelector<HTMLElement>('.cc-report-writer-model-selector [role="option"]')!.click();
    fixture.detectChanges();
    await settle();
    const estimate = http.expectOne(`${CC_API}/analyses/${analysisId}/report-documents/estimate`);
    expect(estimate.request.method).toBe('POST');
    return estimate;
  }

  /** Lists the analysis's documents again and answers with these. */
  function listDocuments(documents: BenchmarkReportDocumentListItemDto[]): void {
    component().reloadDocuments();
    http.expectOne(r => r.url === DOCUMENTS_URL).flush(documents);
    fixture.detectChanges();
  }

  /** Shows another analysis, whose job request answers with `job` and whose freshness with `freshness`. */
  function openAnalysis(job: ReturnType<typeof ccReportJob> | null, freshness: CcAnalysisFreshness = ccFreshness({ analysisId: 8 })): void {
    fixture.componentRef.setInput('result', ccAnalysisResult({ analysisId: 8 }));
    fixture.detectChanges();
    const request = http.expectOne(`${CC_API}/analyses/8/report-documents/job`);
    if (job) {
      request.flush(job);
    } else {
      request.flush(null, { status: 204, statusText: 'No Content' });
    }
    http.expectOne(r => r.url === DOCUMENTS_URL && r.params.get('subject') === 'chat-consistency:8').flush([]);
    http.expectOne(`${CC_API}/analyses/8/freshness`).flush(freshness);
    fixture.detectChanges();
  }

  it('draws the attached charts over the analyzed runs, with every timeline point for the harness and the timeline\'s numbering', () => {
    const points = [...ccTimeline().points, ccPoint(201, '2026-10-05T08:00:00Z')];
    const numbering = groupOverseerEvents(ccTimeline().events, points);
    fixture.componentRef.setInput('points', points);
    fixture.componentRef.setInput('eventNumbering', numbering);
    fixture.detectChanges();

    const input = component().chartInput();
    expect(input.points.map(point => point.runId)).toEqual([101, 102, 103, 104, 105, 106]);
    expect(input.harnessPoints).toBe(points);
    expect(input.eventNumbering).toBe(numbering);
    expect(input.events).toEqual(ccAnalysisResult().events);
    expect(input.bands!.map(band => band.name)).toEqual(['Baseline', 'Comparison']);
    expect(input.unitKind).toBe('run');
  });

  it('draws a battery analysis\'s attached charts over its battery runs, with the runs for the harness', () => {
    fixture.componentRef.setInput('result', ccAnalysisResult({
      comparisonSet: { kind: 'battery', key: CC_BATTERY_SET_KEY, label: 'Two initial suites (revision 1)' },
      unitKind: 'batteryRun',
      units: [{ unitId: 12, kind: 'batteryRun', period: 'comparison', startedAtUtc: '2026-10-08T10:00:00Z', memberRunIds: [1201, 1202] }]
    }));
    fixture.componentRef.setInput('batteryPoints', [ccBatteryPoint(11, '2026-10-08T06:00:00Z'), ccBatteryPoint(12, '2026-10-08T10:00:00Z')]);
    fixture.detectChanges();
    const input = component().chartInput();
    expect(input.unitKind).toBe('batteryRun');
    expect(input.points.map(point => point.runId)).toEqual([12]);
    expect(input.harnessPoints!.map(point => point.runId)).toEqual([101, 102, 103, 104, 105, 106]);
  });

  it('heads the step with the analysis and the model without its thinking level, in the sidebar layout', () => {
    expect(textOf(el.querySelector('h4#cc-rep-heading'))).toBe('Write reports');
    expect(el.querySelector('h4#cc-rep-heading')!.classList).toContain('gh-section-title');
    expect(textOf(el.querySelector('.rp-subtitle'))).toBe('Analysis #7 · Chat consistency: GPT-5 high');
    expect(el.querySelector('app-run-report-frame aside.rrf-sidebar')!.getAttribute('aria-label')).toBe('New reports');
    expect(el.querySelector('app-run-report-frame section.rrf-main')!.getAttribute('aria-label')).toBe('Report progress');
    expect(textOf(el.querySelector('.rp-idle-note'))).toBe('No report is being written. Choose the documents and a writer, then Write Reports.');
    expect(el.querySelector('.cc-rep-open-btn')).toBeNull();

    fixture.componentRef.setInput('result', ccAnalysisResult({ analysisId: 7, subject: ccSubjectWithLevel() }));
    fixture.detectChanges();
    expect(textOf(el.querySelector('.rp-subtitle'))).toBe('Analysis #7 · Chat consistency: GPT-5');
  });

  it('lists the four documents as not written, the first three checked to be written', () => {
    const names = Array.from(el.querySelectorAll('.cc-rep-audiences .cds-doc-name')).map(name => textOf(name));
    expect(names).toEqual(['Executive Summary', 'Report for AI Researchers and Developers', 'Internal Brief', 'Provider Issue Report']);
    const tags = Array.from(el.querySelectorAll('.cc-rep-audiences .cds-status')).map(tag => textOf(tag));
    expect(tags).toEqual(['Not written', 'Not written', 'Not written', 'Not written']);
    const boxes = Array.from(el.querySelectorAll<HTMLInputElement>('.cc-rep-audiences input[type="checkbox"]'));
    expect(boxes.map(box => box.checked)).toEqual([true, true, true, false]);
    expect(textOf(row(BenchmarkReportAudience.ExecutiveSummary).querySelector('.cds-check'))).toBe('Write: the Executive Summary');
  });

  /** Analysis 7 with every endpoint inconclusive or not computable. */
  const undecided = () => ccAnalysisResult({
    endpoints: [
      ccEndpoint('P1', { computed: false, notComputedReason: 'Grading changed.', verdict: null, verdictLabel: 'not computable', grade: 'notEstablished' }),
      ccEndpoint('P2', { computed: false, notComputedReason: 'No common stratum.', verdict: null, verdictLabel: 'not computable', grade: 'notEstablished' }),
      ccEndpoint('P3', { computed: false, notComputedReason: 'No common stratum.', verdict: null, verdictLabel: 'not computable', grade: 'notEstablished' }),
      ccEndpoint('P4', { verdict: 'inconclusive', verdictLabel: 'inconclusive', grade: 'notEstablished' }),
      ccEndpoint('P5', { verdict: 'inconclusive', verdictLabel: 'inconclusive', grade: 'notEstablished' })
    ]
  });

  it('says above the documents that they will find not enough evidence, and checks the Executive Summary alone on request', async () => {
    expect(el.querySelector('.cc-rep-evidence')).toBeNull();
    fixture.componentRef.setInput('result', undecided());
    fixture.detectChanges();
    const notice = () => el.querySelector<HTMLElement>('.cc-rep-audiences .alert.alert-warning.cc-rep-evidence');
    expect(notice()?.getAttribute('role')).toBe('note');
    expect(notice()?.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(textOf(notice()?.querySelector('.cc-rep-evidence-text'))).toBe(CC_NO_DECISIVE_VERDICT_TEXT);
    expect(CC_NO_DECISIVE_VERDICT_TEXT)
      .toBe('Every endpoint is inconclusive or not computable; the documents will say Not enough evidence yet.');
    // Above the document list.
    expect(notice()?.nextElementSibling?.classList.contains('cds-list')).toBe(true);

    const estimate = await chooseWriter();
    expect(estimate.request.body).toEqual({ writerModelConfigurationId: 30, audiences: [1, 2, 3] });
    estimate.flush(ccReportEstimate());
    fixture.detectChanges();

    const button = notice()!.querySelector<HTMLButtonElement>('button.btn-ghost.cc-rep-summary-only')!;
    expect(button.type).toBe('button');
    expect(textOf(button)).toBe('Write the Executive Summary only');
    button.click();
    fixture.detectChanges();
    const boxes = Array.from(el.querySelectorAll<HTMLInputElement>('.cc-rep-audiences input[type="checkbox"]'));
    expect(boxes.map(box => box.checked)).toEqual([true, false, false, false]);
    expect(textOf(el.querySelector('.cc-rep-audiences [role="status"]'))).toBe('Only the Executive Summary is checked.');
    // Not blocking: Write Report stays available, and the estimate follows the one document.
    expect(writeButton().disabled).toBe(false);
    expect(textOf(writeButton())).toBe('Write Report');
    await settle();
    const again = http.expectOne(`${CC_API}/analyses/7/report-documents/estimate`);
    expect(again.request.body).toEqual({ writerModelConfigurationId: 30, audiences: [1] });
    again.flush(ccReportEstimate());
    fixture.detectChanges();
  });

  it('keeps the notice but offers no Executive Summary only once the summary is written, and shows none for a decisive verdict', () => {
    fixture.componentRef.setInput('result', undecided());
    fixture.detectChanges();
    expect(el.querySelector('.cc-rep-summary-only')).not.toBeNull();
    listDocuments([ccDoc(41, BenchmarkReportAudience.ExecutiveSummary)]);
    expect(el.querySelector('.cc-rep-evidence')).not.toBeNull();
    expect(el.querySelector('.cc-rep-summary-only')).toBeNull();

    fixture.componentRef.setInput('result', ccAnalysisResult());
    fixture.detectChanges();
    expect(el.querySelector('.cc-rep-evidence')).toBeNull();
  });

  it('disables the Provider Issue Report with its reason shown as text', async () => {
    expect(providerBox().disabled).toBe(true);
    const reason = el.querySelector('#cc-rep-provider-reason')!;
    expect(textOf(reason)).toBe(CC_PROVIDER_REPORT_UNKNOWN);
    expect(providerBox().getAttribute('aria-describedby')).toBe('cc-rep-provider-reason');

    const estimate = await chooseWriter();
    expect(estimate.request.body).toEqual({ writerModelConfigurationId: 30, audiences: [1, 2, 3] });
    estimate.flush(ccReportEstimate());
    fixture.detectChanges();

    expect(providerBox().disabled).toBe(true);
    expect(textOf(el.querySelector('#cc-rep-provider-reason'))).toBe('No change attributed to the provider is established or indicated.');
    expect(textOf(el.querySelector('#cc-rep-estimate'))).toBe('Estimated cost: about $0.120');
  });

  it('enables the Provider Issue Report when the estimate says it is available', async () => {
    const estimate = await chooseWriter();
    estimate.flush(ccReportEstimate({ providerIssueReportAvailable: true, providerIssueReportReason: null }));
    fixture.detectChanges();
    expect(providerBox().disabled).toBe(false);
    expect(el.querySelector('#cc-rep-provider-reason')).toBeNull();

    providerBox().click();
    fixture.detectChanges();
    await settle();
    const again = http.expectOne(`${CC_API}/analyses/7/report-documents/estimate`);
    expect(again.request.body).toEqual({ writerModelConfigurationId: 30, audiences: [1, 2, 3, 4] });
    again.flush(ccReportEstimate({ providerIssueReportAvailable: true, providerIssueReportReason: null }));
  });

  it('keeps Write Reports disabled, with its reason, until a writer is chosen', async () => {
    expect(writeButton().disabled).toBe(true);
    expect(textOf(el.querySelector('#cc-rep-write-blocked'))).toBe('Choose a report writer.');
    expect(writeButton().getAttribute('aria-describedby')).toBe('cc-rep-estimate cc-rep-write-blocked');

    const estimate = await chooseWriter();
    estimate.flush(ccReportEstimate());
    fixture.detectChanges();
    expect(writeButton().disabled).toBe(false);
    expect(el.querySelector('#cc-rep-write-blocked')).toBeNull();
    expect(writeButton().getAttribute('aria-describedby')).toBe('cc-rep-estimate');
    expect(textOf(writeButton())).toBe('Write Reports');
  });

  it('writes the checked documents and follows the job, with Cancel', async () => {
    let changes = 0;
    component().stateChange.subscribe(() => changes++);
    const estimate = await chooseWriter();
    estimate.flush(ccReportEstimate());
    fixture.detectChanges();

    writeButton().click();
    const write = http.expectOne(`${CC_API}/analyses/7/report-documents`);
    expect(write.request.method).toBe('POST');
    expect(write.request.body).toEqual({ writerModelConfigurationId: 30, audiences: [1, 2, 3] });
    write.flush({ runId: 7, status: 1, audiences: [1, 2, 3] }, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    expect(changes).toBe(1);
    expect(textOf(el.querySelector('.cc-rep-status'))).toBe('Waiting for the report writer');
    expect(textOf(el.querySelector('.rp-job-title'))).toBe('Reports in progress');
    expect(writeButton().disabled).toBe(true);
    expect(textOf(el.querySelector('#cc-rep-write-blocked'))).toBe('Reports are being written. Wait for them to finish, or cancel the writing.');
    el.querySelector<HTMLButtonElement>('.cc-rep-cancel-btn')!.click();
    const cancel = http.expectOne(`${CC_API}/analyses/7/report-documents/cancel`);
    expect(cancel.request.method).toBe('POST');
    cancel.flush(null, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
  });

  it('asks for the same-provider acknowledgment before writing with such a writer', async () => {
    const sameProvider = ccConfig(31, { displayName: 'GPT writer', provider: 'OpenAI', modelId: 'gpt-4.1' });
    fixture.componentRef.setInput('writerConfigs', [sameProvider]);
    fixture.componentRef.setInput('writerOptions', toModelPickerOptions([sameProvider]));
    fixture.detectChanges();
    const estimate = await chooseWriter();
    estimate.flush(ccReportEstimate());
    fixture.detectChanges();
    expect(textOf(el.querySelector('#cc-rep-writer-warning'))).toContain('GPT writer is from OpenAI');

    writeButton().click();
    fixture.detectChanges();
    const dialog = el.querySelector<HTMLDialogElement>('dialog.cc-rep-confirm-dialog')!;
    expect(dialog.open).toBe(true);
    http.expectNone(`${CC_API}/analyses/7/report-documents`);

    dialog.querySelector<HTMLButtonElement>('.cc-rep-same-provider-confirm')!.click();
    const write = http.expectOne(`${CC_API}/analyses/7/report-documents`);
    expect(write.request.body).toEqual({ writerModelConfigurationId: 31, audiences: [1, 2, 3], acknowledgeSameProvider: true });
    write.flush({ error: 'The spending cap is reached.' }, { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();
    expect(textOf(el.querySelector('.cc-rep-write-error'))).toBe('The spending cap is reached.');
  });

  it('asks for the acknowledgment when the server answers a write with a same-provider 409', async () => {
    const estimate = await chooseWriter();
    estimate.flush(ccReportEstimate());
    fixture.detectChanges();

    writeButton().click();
    http.expectOne(`${CC_API}/analyses/7/report-documents`).flush(
      { sameProvider: true, provider: 'Anthropic', assessorModelDisplayName: 'Claude writer', role: 'reportWriter' },
      { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    const dialog = el.querySelector<HTMLDialogElement>('dialog.cc-rep-confirm-dialog')!;
    expect(dialog.open).toBe(true);
    expect(el.querySelector('.cc-rep-write-error')).toBeNull();

    dialog.querySelector<HTMLButtonElement>('.cc-rep-same-provider-confirm')!.click();
    const write = http.expectOne(`${CC_API}/analyses/7/report-documents`);
    expect(write.request.body).toEqual({ writerModelConfigurationId: 30, audiences: [1, 2, 3], acknowledgeSameProvider: true });
    write.flush({ runId: 7, status: 1, audiences: [1, 2, 3] }, { status: 202, statusText: 'Accepted' });
  });

  describe('an out-of-date analysis', () => {
    const outOfDateDialog = () => el.querySelector<HTMLDialogElement>('dialog.cc-rep-out-of-date-dialog')!;

    it('shows no notice while the analysis is current', () => {
      expect(el.querySelector('.cc-fresh-notice')).toBeNull();
      expect(el.querySelector('.cc-fresh-unchecked')).toBeNull();
    });

    it('shows the notice in the header, and Analyze again asks the host for Analyze', () => {
      let requested = 0;
      component().analyzeAgain.subscribe(() => requested++);
      openAnalysis(null, ccOutOfDateFreshness({ analysisId: 8 }));

      const notice = el.querySelector<HTMLElement>('.rp-identity .cc-fresh-notice')!;
      expect(textOf(notice.querySelector('.cc-fresh-title'))).toBe('This analysis is out of date.');
      expect(textOf(notice.querySelector('.cc-fresh-text'))).toBe(
        'Saved under analysis code version 5; Overseer now analyzes under version 6. '
        + 'Its runs, grades, controls, annotations or prices changed after it was saved. '
        + 'Saved analyses never change. Analyze again for a current analysis with the same settings; '
        + 'this one stays in Analysis history as a record.');
      notice.querySelector<HTMLButtonElement>('#cc-rep-fresh-again')!.click();
      expect(requested).toBe(1);
    });

    it('asks before writing; Cancel sends nothing, Write anyway sends the acknowledgment', async () => {
      openAnalysis(null, ccOutOfDateFreshness({ analysisId: 8, earlierAnalysisCode: false, analysisCodeVersion: 6 }));
      const estimate = await chooseWriter(8);
      estimate.flush(ccReportEstimate());
      fixture.detectChanges();

      writeButton().click();
      fixture.detectChanges();
      expect(outOfDateDialog().open).toBe(true);
      expect(textOf(outOfDateDialog().querySelector('#cc-rep-out-of-date-title'))).toBe('Write from an out-of-date analysis?');
      expect(textOf(outOfDateDialog().querySelector('.cc-rep-out-of-date-text'))).toBe(
        'Its runs, grades, controls, annotations or prices changed after it was saved. Every document will say so on its first page.');
      http.expectNone(`${CC_API}/analyses/8/report-documents`);

      outOfDateDialog().querySelector<HTMLButtonElement>('.cc-rep-out-of-date-cancel')!.click();
      expect(outOfDateDialog().open).toBe(false);
      http.expectNone(`${CC_API}/analyses/8/report-documents`);

      writeButton().click();
      fixture.detectChanges();
      expect(outOfDateDialog().open).toBe(true);
      expect(textOf(outOfDateDialog().querySelector('.cc-rep-out-of-date-confirm'))).toBe('Write anyway');
      outOfDateDialog().querySelector<HTMLButtonElement>('.cc-rep-out-of-date-confirm')!.click();
      const write = http.expectOne(`${CC_API}/analyses/8/report-documents`);
      expect(write.request.body).toEqual({ writerModelConfigurationId: 30, audiences: [1, 2, 3], acknowledgeOutOfDate: true });
      write.flush({ runId: 8, status: 1, audiences: [1, 2, 3] }, { status: 202, statusText: 'Accepted' });
    });

    it('asks both questions for a same-provider writer and sends both acknowledgments', async () => {
      const sameProvider = ccConfig(31, { displayName: 'GPT writer', provider: 'OpenAI', modelId: 'gpt-4.1' });
      fixture.componentRef.setInput('writerConfigs', [sameProvider]);
      fixture.componentRef.setInput('writerOptions', toModelPickerOptions([sameProvider]));
      openAnalysis(null, ccOutOfDateFreshness({ analysisId: 8 }));
      const estimate = await chooseWriter(8);
      estimate.flush(ccReportEstimate());
      fixture.detectChanges();

      writeButton().click();
      fixture.detectChanges();
      const sameProviderDialog = el.querySelector<HTMLDialogElement>('dialog.cc-rep-confirm-dialog')!;
      expect(sameProviderDialog.open).toBe(true);
      sameProviderDialog.querySelector<HTMLButtonElement>('.cc-rep-same-provider-confirm')!.click();
      fixture.detectChanges();
      expect(outOfDateDialog().open).toBe(true);
      http.expectNone(`${CC_API}/analyses/8/report-documents`);

      outOfDateDialog().querySelector<HTMLButtonElement>('.cc-rep-out-of-date-confirm')!.click();
      const write = http.expectOne(`${CC_API}/analyses/8/report-documents`);
      expect(write.request.body).toEqual({
        writerModelConfigurationId: 31, audiences: [1, 2, 3], acknowledgeSameProvider: true, acknowledgeOutOfDate: true
      });
      write.flush({ runId: 8, status: 1, audiences: [1, 2, 3] }, { status: 202, statusText: 'Accepted' });
    });

    it('asks when the server refuses a write as out of date, with its reason, and retries with the acknowledgment', async () => {
      const estimate = await chooseWriter();
      estimate.flush(ccReportEstimate());
      fixture.detectChanges();

      writeButton().click();
      http.expectOne(`${CC_API}/analyses/7/report-documents`).flush(
        { error: 'Analysis #7 is out of date: changed inputs. Analyze again, or confirm to write from it anyway.', outOfDate: true },
        { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();
      expect(outOfDateDialog().open).toBe(true);
      expect(el.querySelector('.cc-rep-write-error')).toBeNull();
      expect(textOf(outOfDateDialog().querySelector('.cc-rep-out-of-date-text'))).toBe(
        'Analysis #7 is out of date: changed inputs. Analyze again, or confirm to write from it anyway. '
        + 'Every document will say so on its first page.');

      outOfDateDialog().querySelector<HTMLButtonElement>('.cc-rep-out-of-date-confirm')!.click();
      const write = http.expectOne(`${CC_API}/analyses/7/report-documents`);
      expect(write.request.body).toEqual({ writerModelConfigurationId: 30, audiences: [1, 2, 3], acknowledgeOutOfDate: true });
      write.flush({ runId: 7, status: 1, audiences: [1, 2, 3] }, { status: 202, statusText: 'Accepted' });
    });
  });

  it('shows a written document as Written, with View and Delete and no checkbox, and offers only the others', () => {
    listDocuments([ccDoc(501, BenchmarkReportAudience.ExecutiveSummary)]);

    const written = row(BenchmarkReportAudience.ExecutiveSummary);
    expect(textOf(written.querySelector('.cds-status'))).toBe('Written');
    expect(textOf(written.querySelector('.cds-meta'))).toBe('by Claude writer on 2026-10-02 10:01 UTC · 1 min 00 s · $0.15');
    expect(written.querySelector('input[type="checkbox"]')).toBeNull();
    const view = el.querySelector<HTMLButtonElement>('#cc-rep-doc-1-view')!;
    const remove = el.querySelector<HTMLButtonElement>('#cc-rep-doc-1-delete')!;
    expect(view.getAttribute('aria-label')).toBe('View the Executive Summary');
    expect(view.getAttribute('interestfor')).toBe('cc-rep-doc-1-view-tip');
    expect(view.hasAttribute('title')).toBe(false);
    expect(remove.getAttribute('aria-label')).toBe('Delete the Executive Summary');
    expect(remove.classList).toContain('action-btn-danger');
    expect(remove.disabled).toBe(false);

    const boxes = Array.from(el.querySelectorAll<HTMLInputElement>('.cc-rep-audiences input[type="checkbox"]'));
    expect(boxes.map(box => box.id)).toEqual(['cc-rep-audience-2', 'cc-rep-audience-3', 'cc-rep-audience-4']);
    expect(boxes.map(box => box.checked)).toEqual([true, true, false]);
    expect(component().checkedAudiences).toEqual([2, 3]);
  });

  it('says when every document is written, and keeps Write Reports focusable with its reason', () => {
    listDocuments([
      ccDoc(501, BenchmarkReportAudience.ExecutiveSummary),
      ccDoc(502, BenchmarkReportAudience.TechnicalReport, { status: 'CompletedWithWarnings' }),
      ccDoc(503, BenchmarkReportAudience.InternalBrief),
      ccDoc(504, BenchmarkReportAudience.ProviderIssueReport)
    ]);

    expect(textOf(el.querySelector('.cc-rep-all-written'))).toBe(CC_ALL_WRITTEN_TEXT);
    expect(el.querySelectorAll('.cc-rep-audiences input[type="checkbox"]').length).toBe(0);
    expect(textOf(row(BenchmarkReportAudience.TechnicalReport).querySelector('.cds-status'))).toBe('Written with warnings');
    expect(writeButton().disabled).toBe(false);
    expect(writeButton().getAttribute('aria-disabled')).toBe('true');
    expect(textOf(el.querySelector('#cc-rep-write-blocked'))).toBe(CC_ALL_WRITTEN_REASON);
    expect(writeButton().getAttribute('aria-describedby')).toContain('cc-rep-write-blocked');

    writeButton().click();
    http.expectNone(`${CC_API}/analyses/7/report-documents`);
  });

  it('deletes a written document after the confirmation, lists the documents again and offers it to be written', async () => {
    let changes = 0;
    component().documentsChanged.subscribe(() => changes++);
    listDocuments([ccDoc(501, BenchmarkReportAudience.ExecutiveSummary)]);

    el.querySelector<HTMLButtonElement>('#cc-rep-doc-1-delete')!.click();
    fixture.detectChanges();
    const dialog = el.querySelector<HTMLDialogElement>('dialog.cc-rep-delete-dialog')!;
    expect(dialog.open).toBe(true);
    expect(textOf(dialog.querySelector('.cc-rep-delete-text'))).toContain('Chat consistency 501: GPT-5 high');

    const closed = new Promise<void>(resolve => dialog.addEventListener('close', () => resolve(), { once: true }));
    dialog.querySelector<HTMLButtonElement>('.cc-rep-delete-confirm')!.click();
    const remove = http.expectOne(r => r.method === 'DELETE');
    expect(remove.request.url).toBe(`${DOCUMENTS_URL}/501`);
    remove.flush(null);
    http.expectOne(r => r.method === 'GET' && r.url === DOCUMENTS_URL).flush([]);
    await closed;
    fixture.detectChanges();

    expect(changes).toBe(1);
    expect(el.querySelector('#cc-rep-doc-1-delete')).toBeNull();
    expect(el.querySelector<HTMLInputElement>('#cc-rep-audience-1')!.checked).toBe(true);
    expect(document.activeElement).toBe(el.querySelector('#cc-rep-new-heading'));
  });

  it('shows the server\'s reason when a delete fails, and keeps the document', () => {
    listDocuments([ccDoc(501, BenchmarkReportAudience.ExecutiveSummary)]);

    el.querySelector<HTMLButtonElement>('#cc-rep-doc-1-delete')!.click();
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('.cc-rep-delete-confirm')!.click();
    http.expectOne(r => r.method === 'DELETE').flush({ error: 'The document is being rendered.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(el.querySelector<HTMLDialogElement>('dialog.cc-rep-delete-dialog')!.open).toBe(true);
    expect(textOf(el.querySelector('.cc-rep-delete-error'))).toBe('The document is being rendered.');
    expect(el.querySelector('#cc-rep-doc-1-delete')).not.toBeNull();
  });

  it('disables Delete while charts are attached', () => {
    listDocuments([ccDoc(501, BenchmarkReportAudience.ExecutiveSummary)]);
    const cdr = fixture.debugElement.injector.get(ChangeDetectorRef);

    component().chartState = 'attaching';
    cdr.markForCheck();
    fixture.detectChanges();
    expect(el.querySelector<HTMLButtonElement>('#cc-rep-doc-1-delete')!.disabled).toBe(true);
    expect(el.querySelector('#cc-rep-delete-busy')).not.toBeNull();
    expect(el.querySelector<HTMLButtonElement>('#cc-rep-doc-1-view')!.disabled).toBe(false);

    component().chartState = 'done';
    cdr.markForCheck();
    fixture.detectChanges();
    expect(el.querySelector<HTMLButtonElement>('#cc-rep-doc-1-delete')!.disabled).toBe(false);
  });

  it('disables Delete while a job runs', () => {
    openAnalysis(ccReportJob());
    listDocuments([ccDoc(501, BenchmarkReportAudience.ExecutiveSummary, { subjectKey: 'chat-consistency:8' })]);
    expect(el.querySelector<HTMLButtonElement>('#cc-rep-doc-1-delete')!.disabled).toBe(true);
  });

  it('opens a written document in the PDF viewer at its fullest disclosure', () => {
    listDocuments([ccDoc(501, BenchmarkReportAudience.ExecutiveSummary)]);
    const open = vi.spyOn(component().pdfViewer!, 'open').mockImplementation(() => undefined);

    el.querySelector<HTMLButtonElement>('#cc-rep-doc-1-view')!.click();
    const request = open.mock.lastCall![0] as PdfViewerRequest;
    expect(request.title).toBe('Chat consistency 501: GPT-5 high');
    expect(request.subtitle).toBe('by Claude writer on 2026-10-02 10:01 UTC');
    expect(request.variants!.map(variant => variant.key)).toEqual(['summary', 'detailed', 'full']);
    expect(request.initialVariant).toBe('full');
    expect(request.secondaryVariants).toBeUndefined();
    expect(request.fallbackFileName).toMatch(/^chat-consistency-7_.*_executive-summary\.pdf$/);
  });

  it('follows a running job: the stage rail, the stat strip with the writer\'s badges, the document rows and the log', () => {
    openAnalysis(ccReportJob());

    expect(textOf(el.querySelector('.cc-rep-status'))).toBe('Writing… · Claude writer');
    expect(textOf(el.querySelector('.rp-job-title'))).toBe('Reports in progress');
    const stages = Array.from(el.querySelectorAll('.rp-job-rail .run-stage'));
    expect(stages.map(stage => textOf(stage.querySelector('.run-stage-name')))).toEqual([
      'Queued', 'Preparing', 'Executive Summary', 'Report for AI Researchers and Developers', 'Internal Brief', 'Done'
    ]);
    expect(textOf(el.querySelector('.rp-job-rail .run-stage.is-current .run-stage-name'))).toBe('Report for AI Researchers and Developers');

    expect(textOf(el.querySelector('.rp-job-elapsed'))).toBe('1 min 25 s');
    expect(textOf(el.querySelector('.rp-job-writer .cc-rep-writer-name'))).toBe('Claude writer');
    expect(el.querySelector('.rp-job-writer .thinking-badge')).not.toBeNull();
    expect(el.querySelector('.rp-job-writer app-provider-badge')).not.toBeNull();
    expect(textOf(el.querySelector('.rp-job-calls'))).toBe('3');
    expect(textOf(el.querySelector('.rp-job-tokens'))).toBe('42,000 in · 9,000 out');
    expect(textOf(el.querySelector('.rp-job-cost-label'))).toBe('Cost so far');
    expect(textOf(el.querySelector('.rp-job-cost'))).toBe('$0.21');
    expect(el.querySelector('.rp-job-estimate')).toBeNull();

    const rows = Array.from(el.querySelectorAll('.rp-job-row'));
    expect(rows.map(r => textOf(r.querySelector('.rp-doc-name')))).toEqual([
      'Executive Summary', 'Report for AI Researchers and Developers', 'Internal Brief'
    ]);
    expect(rows.map(r => textOf(r.querySelector('.rp-doc-charts')))).toEqual(['—', '—', '—']);
    expect(el.querySelectorAll('.rp-job-log .rp-log-list li').length).toBe(2);
    expect(el.querySelector('.cc-rep-cancel-btn')).not.toBeNull();
    expect(el.querySelector('.rp-job-summary')).toBeNull();
  });

  it('says how many jobs are ahead while the job is queued', () => {
    openAnalysis(ccReportJob({ phase: 'Queued', status: BenchmarkRunReportDocumentsStatus.Pending, slotAcquiredAtUtc: null, jobsAhead: 2 }));
    expect(textOf(el.querySelector('.cc-rep-status'))).toBe('Waiting for the report writer · 2 ahead in the queue · Claude writer');
  });

  it('sums up a finished job, with See the documents and Dismiss', () => {
    const running = ccReportJob();
    openAnalysis(ccReportJob(
      { phase: 'Finished', status: BenchmarkRunReportDocumentsStatus.Completed, finishedAtUtc: '2026-10-02T10:01:17Z' },
      { status: 'Completed', completedAtUtc: '2026-10-02T10:01:17Z', documents: running.job.documents.map(doc => ({ ...doc, status: 'Completed' })) }
    ));
    let requested = 0;
    component().documentsRequested.subscribe(() => requested++);

    expect(textOf(el.querySelector('.rp-job-title'))).toBe('Last reports');
    expect(textOf(el.querySelector('.rp-job-summary'))).toBe('3 documents written · $0.21 · 1 min 12 s');
    expect(el.querySelector('.rp-job-rail')).toBeNull();
    expect(el.querySelector('.cc-rep-cancel-btn')).toBeNull();
    expect(textOf(el.querySelector('.rp-job-cost-label'))).toBe('Cost');

    el.querySelector<HTMLButtonElement>('.rp-see-documents')!.click();
    expect(requested).toBe(1);

    el.querySelector<HTMLButtonElement>('.rp-dismiss-job')!.click();
    fixture.detectChanges();
    expect(el.querySelector('.rp-job')).toBeNull();
    expect(el.querySelector('.rp-idle-note')).not.toBeNull();
    expect(document.activeElement).toBe(el.querySelector('#cc-rep-new-heading'));
  });

  it('shows no visible progress heading or status line; the status stays a hidden polite live region', () => {
    openAnalysis(ccReportJob());
    expect(el.querySelector('#cc-rep-progress-heading')).toBeNull();
    const status = el.querySelector('.cc-rep-status')!;
    expect(status.tagName).toBe('P');
    expect(status.classList).toContain('visually-hidden');
    expect(status.getAttribute('role')).toBe('status');
    expect(status.getAttribute('aria-live')).toBe('polite');
    const heading = el.querySelector('#cc-rep-job-heading')!;
    expect(heading.tagName).toBe('H5');
    expect(el.querySelector('.rp-job')!.getAttribute('aria-labelledby')).toBe('cc-rep-job-heading');
  });

  describe('charts in PDF and Word', () => {
    const { ExecutiveSummary, TechnicalReport, InternalBrief, ProviderIssueReport } = BenchmarkReportAudience;
    const picker = () => el.querySelector<HTMLElement>('fieldset.cc-rep-charts-choice app-report-chart-picker')!;
    const pill = (audience: BenchmarkReportAudience) => el.querySelector<HTMLButtonElement>(`#cc-rep-charts-tab-${audience}`)!;
    const box = (audience: BenchmarkReportAudience, key: string) => el.querySelector<HTMLInputElement>(`#cc-rep-charts-${audience}-${key}`)!;
    const updateButton = () => el.querySelector<HTMLButtonElement>('.cc-rep-update-charts-btn')!;
    const stored = () => JSON.parse(localStorage.getItem(CC_REPORT_CHART_STORAGE_KEY)!) as {
      version: number; selection: Record<string, string[]>; layout: Record<string, { maxHeightPercent: number; labelPt: number }>;
    };
    const choose = (select: HTMLSelectElement, value: string) => {
      select.value = value;
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };

    it('offers the four figures for the four documents, with the sections, widths and the two layout fields', () => {
      expect(textOf(el.querySelector('fieldset.cc-rep-charts-choice legend'))).toBe('Charts in PDF and Word');
      const pills = Array.from(picker().querySelectorAll<HTMLElement>('[role="tab"]'));
      expect(pills.map(item => textOf(item.querySelector('.rcp-tab-name')))).toEqual(['Executive', 'Researchers', 'Internal', 'Provider']);
      expect(pills.map(item => textOf(item.querySelector('.rcp-tab-count')))).toEqual(['2', '4', '2', '—']);
      expect(textOf(pill(ProviderIssueReport).querySelector('.visually-hidden'))).toBe(', not chosen in New reports');

      const titles = Array.from(picker().querySelectorAll('.rcp-figure-title')).map(title => textOf(title));
      expect(titles).toEqual(['Intelligence per run', 'Time to first answer text', 'Work per turn (output tokens per answer)', 'Runs and events']);
      const sections = () => Array.from(picker().querySelectorAll('.rcp-placement')).map(section => textOf(section));
      expect(sections()).toEqual(['Results', 'Results', 'Results', 'Results']);
      expect(el.querySelector<HTMLSelectElement>(`#cc-rep-charts-${ExecutiveSummary}-cc1-quality-width`)!.value).toBe('half');
      expect(el.querySelector<HTMLSelectElement>(`#cc-rep-charts-${ExecutiveSummary}-cc4-timeline-width`)!.value).toBe('twoThirds');
      expect(el.querySelector<HTMLSelectElement>(`#cc-rep-charts-${ExecutiveSummary}-label`)!.value).toBe('8');
      expect(el.querySelector<HTMLSelectElement>(`#cc-rep-charts-${ExecutiveSummary}-maxHeight`)!.value).toBe('50');
      for (const field of ['orientation', 'sideBySide', 'heading', 'logo', 'theme']) {
        expect(el.querySelector(`#cc-rep-charts-${ExecutiveSummary}-${field}`), field).toBeNull();
      }

      pill(TechnicalReport).click();
      fixture.detectChanges();
      expect(sections()).toEqual(['Results', 'Results', 'Results', 'Events']);

      pill(ProviderIssueReport).click();
      fixture.detectChanges();
      expect(box(ProviderIssueReport, 'cc2-speed').getAttribute('aria-disabled')).toBe('true');
      expect(textOf(el.querySelector(`#cc-rep-charts-col-${ProviderIssueReport}-reason`))).toBe('Not chosen in New reports.');
    });

    it('remembers a change of figures and of layout in this browser', () => {
      box(ExecutiveSummary, 'cc3-work').click();
      fixture.detectChanges();
      expect(stored().version).toBe(1);
      expect(stored().selection[ExecutiveSummary]).toEqual(['cc1-quality', 'cc2-speed', 'cc3-work']);
      expect(stored().selection[InternalBrief]).toEqual(['cc1-quality', 'cc3-work']);

      el.querySelector<HTMLDetailsElement>('details.rcp-layout')!.open = true;
      choose(el.querySelector<HTMLSelectElement>(`#cc-rep-charts-${ExecutiveSummary}-maxHeight`)!, '40');
      expect(stored().layout[ExecutiveSummary].maxHeightPercent).toBe(40);
      expect(stored().layout[TechnicalReport].maxHeightPercent).toBe(50);

      const another = TestBed.createComponent(CcReportsStepComponent);
      expect(another.componentInstance.chartSettings.selection[ExecutiveSummary]).toEqual(['cc1-quality', 'cc2-speed', 'cc3-work']);
      expect(another.componentInstance.chartSettings.layout[ExecutiveSummary]!.maxHeightPercent).toBe(40);
      another.destroy();
    });

    it('lets a written document\'s charts be chosen, for Update charts', () => {
      listDocuments([ccDoc(501, ProviderIssueReport)]);
      expect(textOf(pill(ProviderIssueReport).querySelector('.rcp-tab-count'))).toBe('2');
      pill(ProviderIssueReport).click();
      fixture.detectChanges();
      expect(box(ProviderIssueReport, 'cc2-speed').hasAttribute('aria-disabled')).toBe(false);
    });

    it('notes a figure whose endpoint the analysis could not compute', () => {
      fixture.componentRef.setInput('result', ccAnalysisResult({
        endpoints: [ccEndpoint('P1', { computed: false, notComputedReason: 'No common grader covers every run.' }), ccEndpoint('P2'), ccEndpoint('P4')]
      }));
      fixture.detectChanges();
      const note = el.querySelector(`#cc-rep-charts-${ExecutiveSummary}-cc1-quality-note`)!;
      expect(textOf(note)).toBe('P1 was not computable in this analysis; the chart\'s caption says it is not comparable.');
      expect(box(ExecutiveSummary, 'cc1-quality').getAttribute('aria-describedby')).toContain(note.id);
      expect(el.querySelector(`#cc-rep-charts-${ExecutiveSummary}-cc2-speed-note`)).toBeNull();
      expect(component().chartInput().endpoints).toEqual(component().result.endpoints);
    });

    it('defaults the Executive Summary to the figures the shown analysis computed, without stored choices', () => {
      fixture.componentRef.setInput('result', ccAnalysisResult({
        analysisId: 8,
        endpoints: [
          ccEndpoint('P1', { computed: false, notComputedKind: 'measurementChanged' }),
          ccEndpoint('P2', { computed: false, notComputedKind: 'noCommonStratum' }),
          ccEndpoint('P4')
        ]
      }));
      fixture.detectChanges();
      http.expectOne(`${CC_API}/analyses/8/report-documents/job`).flush(null, { status: 204, statusText: 'No Content' });
      http.expectOne(r => r.url === DOCUMENTS_URL && r.params.get('subject') === 'chat-consistency:8').flush([]);
      http.expectOne(`${CC_API}/analyses/8/freshness`).flush(ccFreshness({ analysisId: 8 }));
      fixture.detectChanges();
      expect(component().chartSettings.selection[ExecutiveSummary]).toEqual(['cc3-work', 'cc4-timeline']);
      expect(textOf(pill(ExecutiveSummary).querySelector('.rcp-tab-count'))).toBe('2');
    });

    it('keeps Update charts aria-disabled, with its reason, while nothing is written, charts attach or a job writes', () => {
      expect(updateButton().getAttribute('aria-disabled')).toBe('true');
      expect(updateButton().disabled).toBe(false);
      expect(updateButton().getAttribute('aria-describedby')).toBe('cc-rep-update-charts-reason');
      expect(textOf(el.querySelector('#cc-rep-update-charts-reason'))).toBe(CC_UPDATE_CHARTS_NONE_REASON);
      updateButton().click();
      expect(component().chartState).toBe('idle');

      listDocuments([ccDoc(501, ExecutiveSummary)]);
      expect(updateButton().hasAttribute('aria-disabled')).toBe(false);
      expect(el.querySelector('#cc-rep-update-charts-reason')).toBeNull();

      const cdr = fixture.debugElement.injector.get(ChangeDetectorRef);
      component().chartState = 'attaching';
      cdr.markForCheck();
      fixture.detectChanges();
      expect(textOf(el.querySelector('#cc-rep-update-charts-reason'))).toBe(CC_UPDATE_CHARTS_ATTACHING_REASON);
      component().chartState = 'idle';

      openAnalysis(ccReportJob());
      listDocuments([ccDoc(511, ExecutiveSummary, { subjectKey: 'chat-consistency:8' })]);
      expect(updateButton().getAttribute('aria-disabled')).toBe('true');
      expect(textOf(el.querySelector('#cc-rep-update-charts-reason'))).toBe(CC_UPDATE_CHARTS_WRITING_REASON);
    });

    it('draws every written document\'s chosen charts again and uploads only those, with their layout', async () => {
      listDocuments([ccDoc(501, ExecutiveSummary), ccDoc(502, TechnicalReport)]);
      // The Executive Summary keeps Intelligence alone.
      box(ExecutiveSummary, 'cc2-speed').click();
      fixture.detectChanges();

      updateButton().click();
      fixture.detectChanges();
      expect(component().chartState).toBe('attaching');
      expect(textOf(el.querySelector('.cc-rep-update-charts-status'))).toBe('Updating the charts of 2 documents…');

      const waitForPut = (id: number) =>
        vi.waitFor(() => http.expectOne(r => r.method === 'PUT' && r.url === `${DOCUMENTS_URL}/${id}/charts`), { timeout: 20_000, interval: 50 });
      const first = await waitForPut(501);
      const keysOf = (request: TestRequest) => (request.request.body.charts as ReportDocumentChartUpload[]).map(chart => chart.figureKey);
      expect(keysOf(first)).toEqual(['cc1-quality']);
      expect(first.request.body.layout).toEqual({
        version: 1, figures: [{ key: 'cc1-quality', widthShare: 0.5, rowGroup: null }], maxHeightShare: 0.5
      });
      const chart = (first.request.body.charts as ReportDocumentChartUpload[])[0];
      expect(chart.naming).toBe('named');
      expect(chart.settingsHash).toMatch(/^[0-9a-f]{64}$/);
      expect(chart.pngBase64.length).toBeGreaterThan(0);
      first.flush({ chartCount: 1, figureKeys: ['cc1-quality'] });

      const second = await waitForPut(502);
      expect(keysOf(second)).toEqual(['cc1-quality', 'cc2-speed', 'cc3-work', 'cc4-timeline']);
      expect(second.request.body.layout.figures).toEqual([
        { key: 'cc1-quality', widthShare: 0.5, rowGroup: 1 },
        { key: 'cc2-speed', widthShare: 0.5, rowGroup: 1 },
        { key: 'cc3-work', widthShare: 0.5, rowGroup: null },
        { key: 'cc4-timeline', widthShare: 2 / 3, rowGroup: null }
      ]);
      second.flush({ chartCount: 4, figureKeys: ['cc1-quality', 'cc2-speed', 'cc3-work', 'cc4-timeline'] });

      await vi.waitFor(() => expect(component().chartState).toBe('done'), { timeout: 5_000, interval: 20 });
      fixture.detectChanges();
      expect(textOf(el.querySelector('.cc-rep-update-charts-status'))).toBe('Charts updated on 2 documents.');
    });
  });

  it('copies and downloads the diagnostics, LF only, named by the analysis, without the starting user', async () => {
    openAnalysis(ccReportJob({}, { startedByUserId: 'user-secret-17' }));
    const copy = vi.spyOn(ccReportIo, 'copy').mockResolvedValue(true);
    const download = vi.spyOn(ccReportIo, 'download').mockImplementation(() => undefined);

    el.querySelector<HTMLButtonElement>('.rp-download-diagnostics')!.click();
    const [fileName, text] = download.mock.lastCall!;
    expect(fileName).toMatch(/^chat-consistency-reports_8_diagnostics_\d{8}-\d{6}\.txt$/);
    expect(text).toContain('Analysis: #8');
    expect(text).not.toContain('\r');
    expect(text).not.toContain('user-secret-17');

    el.querySelector<HTMLButtonElement>('.rp-copy-diagnostics')!.click();
    await new Promise<void>(resolve => setTimeout(resolve, 0));
    fixture.detectChanges();
    expect(copy).toHaveBeenCalledTimes(1);
    expect(textOf(el.querySelector('.rp-copy-status'))).toBe('Copied');
  });

  it('remembers the sidebar width in this browser', () => {
    component().onSidebarWidthChange(420);
    expect(JSON.parse(localStorage.getItem(CC_REPORTS_STORAGE_KEY)!)).toEqual({ version: 1, sidebarWidth: 420 });

    const another = TestBed.createComponent(CcReportsStepComponent);
    expect(another.componentInstance.sidebarWidth).toBe(420);
    another.destroy();
  });
});
