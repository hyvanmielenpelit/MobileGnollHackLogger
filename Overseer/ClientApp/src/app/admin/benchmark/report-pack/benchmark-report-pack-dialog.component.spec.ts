import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';

import {
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentDetailDto,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPackJobDto,
  BenchmarkReportPackPreviewDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import type { BenchmarkModelComparisonEntryDto } from '../model-comparison/model-comparison.models';
import {
  BenchmarkReportPackDialogComponent,
  REPORT_PACK_POLL_MS,
  REPORT_PACK_PREVIEW_DEBOUNCE_MS,
  REPORT_PACK_STORAGE_KEY,
  ReportPackContext,
  reportPackPreviewIo
} from './benchmark-report-pack-dialog.component';

const { ExecutiveSummary, TechnicalReport, InternalBrief } = BenchmarkReportAudience;
const { Summary, Detailed, Full } = BenchmarkReportDisclosure;

const SYSTEM_CONFIGS_URL = '/api/admin/systemconfigs';
const DOCUMENTS_URL = '/api/admin/benchmark/report-documents';
const ACTIVE_JOB_URL = '/api/admin/benchmark/report-packs/jobs/active';
const PREVIEW_URL = '/api/admin/benchmark/report-packs/preview';
const START_URL = '/api/admin/benchmark/report-packs';
const jobUrl = (id: string): string => `/api/admin/benchmark/report-packs/jobs/${id}`;

function entry(key: string, label: string, overrides: Partial<BenchmarkModelComparisonEntryDto> = {}): BenchmarkModelComparisonEntryDto {
  const [kind, id] = key.split(':');
  return {
    key,
    sourceKind: kind === 'group' ? 'Group' : 'Run',
    sourceId: Number(id),
    label,
    provider: 'Google',
    modelDisplayName: label,
    state: 'Comparable',
    comparable: true,
    excluded: false,
    speedDegraded: false,
    costDegraded: false,
    ...overrides
  } as BenchmarkModelComparisonEntryDto;
}

const ENTRIES: BenchmarkModelComparisonEntryDto[] = [
  entry('run:1', 'Gemini Flash'),
  entry('run:2', 'Claude Opus', { provider: 'Anthropic', state: 'Degraded', comparable: false, speedDegraded: true }),
  entry('run:3', 'Old GPT', { provider: 'OpenAI', state: 'Excluded', comparable: false, excluded: true }),
  entry('group:4', 'GPT Sol group', { provider: 'OpenAI' })
];

const CONTEXT: ReportPackContext = {
  runIds: [1, 2, 3],
  groupIds: [4],
  pricingBasis: 'Current',
  entries: ENTRIES,
  suiteId: 5,
  suiteName: 'Board Suite'
};

function config(id: number, displayName: string, provider: string, overrides: Partial<SystemAiConfigDto> = {}): SystemAiConfigDto {
  return {
    id,
    displayName,
    provider,
    modelId: `model-${id}`,
    thinkingLevel: 'medium',
    reasoningMode: null,
    modelRole: 4,
    isEnabled: true,
    hasApiKey: true,
    ...overrides
  } as SystemAiConfigDto;
}

const CONFIGS: SystemAiConfigDto[] = [
  config(7, 'Claude Opus writer', 'Anthropic'),
  config(8, 'GPT Sol writer', 'OpenAI', { modelRole: 5 }),
  config(9, 'Chat only', 'OpenAI', { modelRole: 1 }),
  config(10, 'Disabled writer', 'OpenAI', { isEnabled: false })
];

function previewDto(overrides: Partial<BenchmarkReportPackPreviewDto> = {}): BenchmarkReportPackPreviewDto {
  return {
    subjectKey: 'run:1',
    subjectLabel: 'Gemini Flash',
    subjectState: 'Comparable',
    suiteName: 'Board Suite',
    peers: [{ letter: 'A', entryKey: 'run:2', label: 'Claude Opus', provider: 'Anthropic', state: 'Degraded' }],
    estimates: [
      { audience: ExecutiveSummary, promptChars: 20000, estimatedInputTokens: 6000, estimatedOutputTokens: 1500, estimatedCostUsd: 0.05 },
      { audience: TechnicalReport, promptChars: 30000, estimatedInputTokens: 9000, estimatedOutputTokens: 3000, estimatedCostUsd: 0.07 }
    ],
    estimatedTotalCostUsd: 0.12,
    writerDisplayName: 'Claude Opus writer',
    sameProviderWarning: null,
    refusal: null,
    ...overrides
  };
}

function jobDto(overrides: Partial<BenchmarkReportPackJobDto> = {}): BenchmarkReportPackJobDto {
  return {
    id: 'job-1',
    packId: 'pack-1',
    subjectKey: 'run:1',
    subjectLabel: 'Gemini Flash',
    suiteId: 5,
    suiteName: 'Board Suite',
    writerConfigId: 7,
    writerDisplayName: 'Claude Opus writer',
    startedByUserId: null,
    startedAtUtc: '2026-09-28T10:00:00Z',
    completedAtUtc: null,
    status: 'Running',
    totalModelCalls: 1,
    inputTokens: 6000,
    outputTokens: 0,
    costUsd: null,
    documents: [
      { audience: ExecutiveSummary, status: 'Writing', documentId: null, errorMessage: null, modelCalls: 1 },
      { audience: TechnicalReport, status: 'Pending', documentId: null, errorMessage: null, modelCalls: 0 }
    ],
    log: [{ timestampUtc: '2026-09-28T10:00:00Z', message: 'Started.', severity: 'Info' }],
    ...overrides
  };
}

function doc(id: number, audience: BenchmarkReportAudience, overrides: Partial<BenchmarkReportDocumentListItemDto> = {}): BenchmarkReportDocumentListItemDto {
  const titles: Record<number, string> = {
    [ExecutiveSummary]: 'Executive Summary: Gemini Flash',
    [TechnicalReport]: 'Technical Report: Gemini Flash',
    [InternalBrief]: 'Internal Improvement Brief: Gemini Flash'
  };
  return {
    id,
    packId: 'pack-1',
    audience,
    title: titles[audience],
    subjectKey: 'run:1',
    subjectLabel: 'Gemini Flash',
    subjectRunIds: [1],
    suiteId: 5,
    suiteName: 'Board Suite',
    writerDisplayName: 'Claude Opus writer',
    writerProvider: 'Anthropic',
    writerModelId: 'claude-opus',
    writerThinkingLevel: 'medium',
    sameProviderAcknowledged: false,
    status: 'Completed',
    reportFormatVersion: 1,
    createdAtUtc: `2026-09-2${id % 10}T16:00:00Z`,
    inputTokens: 6000,
    outputTokens: 1500,
    durationMs: 30000,
    costUsd: 0.05,
    runChangedSinceGeneration: false,
    missingRunIds: [],
    allowedDisclosures: audience === InternalBrief ? [Full] : [Summary, Detailed, Full],
    ...overrides
  };
}

describe('BenchmarkReportPackDialogComponent', () => {
  let fixture: ComponentFixture<BenchmarkReportPackDialogComponent>;
  let component: BenchmarkReportPackDialogComponent;
  let http: HttpTestingController;
  let host: HTMLElement;

  beforeEach(async () => {
    localStorage.removeItem(REPORT_PACK_STORAGE_KEY);
    await TestBed.configureTestingModule({
      imports: [BenchmarkReportPackDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    fixture = TestBed.createComponent(BenchmarkReportPackDialogComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  afterEach(() => {
    // A modal left open would make the rest of the page inert for the next spec.
    host.querySelectorAll('dialog').forEach(dialog => {
      if (dialog.open) {
        dialog.close();
      }
    });
    fixture.destroy();
    localStorage.removeItem(REPORT_PACK_STORAGE_KEY);
  });

  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => host.querySelector<T>(selector);
  const text = (selector: string): string => (q(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const generateButton = (): HTMLButtonElement => q<HTMLButtonElement>('.rp-generate')!;

  interface OpenOptions {
    configs?: SystemAiConfigDto[];
    documents?: BenchmarkReportDocumentListItemDto[];
    activeJob?: BenchmarkReportPackJobDto | null;
  }

  /** Opens the dialog and answers its three opening requests. */
  function openDialog(options: OpenOptions = {}): void {
    component.open(CONTEXT);
    fixture.detectChanges();
    http.expectOne(SYSTEM_CONFIGS_URL).flush(options.configs ?? CONFIGS);
    const list = http.expectOne(r => r.method === 'GET' && r.url === DOCUMENTS_URL);
    expect(list.request.params.get('suiteId')).toBe('5');
    list.flush(options.documents ?? []);
    const active = http.expectOne(ACTIVE_JOB_URL);
    if (options.activeJob) {
      active.flush(options.activeJob);
    } else {
      active.flush(null, { status: 204, statusText: 'No Content' });
    }
    fixture.detectChanges();
  }

  /** Chooses a writer and answers the estimate the debounce then requests. */
  function chooseWriter(id: number, preview: BenchmarkReportPackPreviewDto = previewDto()): TestRequest {
    component.selectWriter(id);
    fixture.detectChanges();
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    const request = http.expectOne(PREVIEW_URL);
    request.flush(preview);
    fixture.detectChanges();
    return request;
  }

  function expectDocumentsRefresh(documents: BenchmarkReportDocumentListItemDto[] = []): void {
    http.expectOne(r => r.method === 'GET' && r.url === DOCUMENTS_URL).flush(documents);
    fixture.detectChanges();
  }

  // -------------------------------------------------------------------------------------------
  // Defaults and the self-refusal
  // -------------------------------------------------------------------------------------------

  it('opens as a modal with the first non-Excluded entry, Excluded entries absent and Degraded ones marked', () => {
    openDialog();

    expect(component.dialog!.nativeElement.open).toBeTrue();
    const select = q<HTMLSelectElement>('#rp-subject')!;
    const options = Array.from(select.options).map(option => option.textContent!.trim());
    expect(options).toEqual(['Gemini Flash', 'Claude Opus (speed degraded)', 'GPT Sol group']);
    expect(select.value).toBe('run:1');
    expect(component.subjectKey).toBe('run:1');
  });

  it('checks the Executive Summary and the Report for AI Researchers and Developers by default, not the Internal Brief', () => {
    openDialog();

    const names = Array.from(host.querySelectorAll('.rp-audience-name')).map(name => (name.textContent ?? '').trim());
    expect(names).toEqual(['Executive Summary', 'Report for AI Researchers and Developers', 'Internal Improvement Brief']);

    expect(q<HTMLInputElement>(`#rp-audience-${ExecutiveSummary}`)!.checked).toBeTrue();
    expect(q<HTMLInputElement>(`#rp-audience-${TechnicalReport}`)!.checked).toBeTrue();
    expect(q<HTMLInputElement>(`#rp-audience-${InternalBrief}`)!.checked).toBeFalse();
    expect(component.selectedAudiences).toEqual([ExecutiveSummary, TechnicalReport]);
  });

  it('offers only enabled Benchmark-role configurations with a key as writers, and requires one', () => {
    openDialog({ configs: [...CONFIGS, config(11, 'Keyless writer', 'OpenAI', { hasApiKey: false })] });

    expect(component.writers.map(writer => writer.id)).toEqual([7, 8]);
    expect(component.writerId).toBeNull();
    expect(generateButton().disabled).toBeTrue();
    expect(text('#rp-generate-blocked')).toBe('Choose a report writer.');
    expect(http.match(PREVIEW_URL).length).toBe(0);
  });

  it('restores the remembered writer while it still qualifies', fakeAsync(() => {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, JSON.stringify({ writerConfigId: 8 }));
    openDialog();

    expect(component.writerId).toBe(8);
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    http.expectOne(PREVIEW_URL).flush(previewDto());
    fixture.destroy();
  }));

  it('shows the self-refusal before Generate, and keeps Generate disabled while it stands', fakeAsync(() => {
    openDialog();
    const refusal = 'The model under report cannot write its own report. Choose a writer of another model.';
    chooseWriter(7, previewDto({ refusal, estimates: [], estimatedTotalCostUsd: null }));

    const alert = q('.rp-refusal')!;
    expect(alert.getAttribute('role')).toBe('alert');
    expect(alert.textContent).toContain(refusal);
    expect(alert.compareDocumentPosition(generateButton()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(generateButton().disabled).toBeTrue();
    expect(text('#rp-generate-blocked')).toBe('This writer is refused for this subject. Choose another writer.');

    generateButton().click();
    http.expectNone(START_URL);
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // The estimate
  // -------------------------------------------------------------------------------------------

  it('requests the estimate with the comparison request, the subject, the documents and the writer', fakeAsync(() => {
    openDialog();
    const request = chooseWriter(7);

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      runIds: [1, 2, 3],
      groupIds: [4],
      pricingBasis: 1,
      subjectKey: 'run:1',
      audiences: [ExecutiveSummary, TechnicalReport],
      writerModelConfigurationId: 7,
      acknowledgeSameProvider: false
    });
    expect(text('.rp-estimate-total')).toBe('$0.12');
    expect(text('.rp-estimate-table tbody')).toContain('Executive Summary');
    expect(generateButton().disabled).toBeFalse();
    fixture.destroy();
  }));

  it('debounces the estimate and re-requests it when the documents, the subject or the writer change', fakeAsync(() => {
    openDialog();
    chooseWriter(7);

    // Two quick changes make one request, with both applied.
    (q<HTMLInputElement>(`#rp-audience-${InternalBrief}`)!).click();
    fixture.detectChanges();
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS / 2);
    const select = q<HTMLSelectElement>('#rp-subject')!;
    select.value = 'run:2';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(generateButton().disabled).toBeTrue();
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS - 1);
    http.expectNone(PREVIEW_URL);
    tick(1);
    const second = http.expectOne(PREVIEW_URL);
    expect(second.request.body.audiences).toEqual([ExecutiveSummary, TechnicalReport, InternalBrief]);
    expect(second.request.body.subjectKey).toBe('run:2');
    second.flush(previewDto({ subjectKey: 'run:2' }));
    fixture.detectChanges();

    component.selectWriter(8);
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    const third = http.expectOne(PREVIEW_URL);
    expect(third.request.body.writerModelConfigurationId).toBe(8);
    third.flush(previewDto());

    // No documents: no estimate and no Generate.
    for (const audience of [ExecutiveSummary, TechnicalReport, InternalBrief]) {
      (q<HTMLInputElement>(`#rp-audience-${audience}`)!).click();
      fixture.detectChanges();
    }
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    http.expectNone(PREVIEW_URL);
    expect(generateButton().disabled).toBeTrue();
    expect(text('#rp-generate-blocked')).toBe('Choose at least one document.');
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // The same-provider warning
  // -------------------------------------------------------------------------------------------

  it('requires the same-provider acknowledgment before Generate, and sends it', fakeAsync(() => {
    openDialog();
    const warning = 'The writer shares Anthropic with the subject; its documents may favor its own family.';
    chooseWriter(7, previewDto({ sameProviderWarning: warning }));

    expect(text('.rp-same-provider')).toContain(warning);
    const acknowledge = q<HTMLInputElement>('#rp-acknowledge')!;
    expect(acknowledge.checked).toBeFalse();
    expect(generateButton().disabled).toBeTrue();
    expect(text('#rp-generate-blocked')).toBe('Acknowledge the same-provider warning first.');

    acknowledge.click();
    fixture.detectChanges();
    expect(generateButton().disabled).toBeFalse();

    generateButton().click();
    fixture.detectChanges();
    const start = http.expectOne(START_URL);
    expect(start.request.method).toBe('POST');
    expect(start.request.body.acknowledgeSameProvider).toBeTrue();
    expect(start.request.body.writerModelConfigurationId).toBe(7);
    expect(JSON.parse(localStorage.getItem(REPORT_PACK_STORAGE_KEY)!)).toEqual({ writerConfigId: 7 });

    start.flush({ jobId: 'job-1' });
    http.expectOne(jobUrl('job-1')).flush(jobDto());
    fixture.detectChanges();
    expect(host.querySelectorAll('.rp-job-row').length).toBe(2);
    fixture.destroy();
  }));

  it('clears the acknowledgment when the writer changes', fakeAsync(() => {
    openDialog();
    chooseWriter(7, previewDto({ sameProviderWarning: 'Same provider.' }));
    q<HTMLInputElement>('#rp-acknowledge')!.click();
    fixture.detectChanges();
    expect(component.acknowledgeSameProvider).toBeTrue();

    chooseWriter(8, previewDto({ sameProviderWarning: 'Same provider.' }));
    expect(component.acknowledgeSameProvider).toBeFalse();
    expect(q<HTMLInputElement>('#rp-acknowledge')!.checked).toBeFalse();
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // 409 and 429
  // -------------------------------------------------------------------------------------------

  it('shows a 409 same-provider refusal in an alert, with the acknowledgment it asks for', fakeAsync(() => {
    openDialog();
    chooseWriter(7);
    generateButton().click();
    const message = 'The writer and the subject are both from Anthropic. Acknowledge the warning to continue.';
    http.expectOne(START_URL).flush(
      { sameProvider: true, provider: 'Anthropic', testedModelDisplayName: 'Gemini Flash', assessorModelDisplayName: 'Claude Opus writer', message },
      { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    const alert = q('.rp-start-error')!;
    expect(alert.getAttribute('role')).toBe('alert');
    expect(alert.textContent!.trim()).toBe(message);
    expect(q('#rp-acknowledge')).not.toBeNull();
    expect(generateButton().disabled).toBeTrue();
    fixture.destroy();
  }));

  it('shows a 409 running job in an alert and follows that job', fakeAsync(() => {
    openDialog();
    chooseWriter(7);
    generateButton().click();
    const running = jobDto({ id: 'job-9', subjectLabel: 'GPT Sol group' });
    http.expectOne(START_URL).flush(running, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(q('.rp-start-error')!.getAttribute('role')).toBe('alert');
    expect(text('.rp-start-error')).toContain('Another report pack is being written, for GPT Sol group');
    expect(host.querySelectorAll('.rp-job-row').length).toBe(2);
    expect(generateButton().disabled).toBeTrue();

    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-9')).flush(jobDto({ id: 'job-9', status: 'Completed', completedAtUtc: '2026-09-28T10:01:00Z' }));
    expectDocumentsRefresh();
    fixture.destroy();
  }));

  it('shows a 429 spend-cap refusal with the server\'s message', fakeAsync(() => {
    openDialog();
    chooseWriter(7);
    generateButton().click();
    const message = 'The benchmark spend cap for today has been reached.';
    http.expectOne(START_URL).flush(message, { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();

    const alert = q('.rp-start-error')!;
    expect(alert.getAttribute('role')).toBe('alert');
    expect(alert.textContent!.trim()).toBe(message);
    expect(generateButton().disabled).toBeFalse();
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // Progress
  // -------------------------------------------------------------------------------------------

  it('polls a started job, one row per document, and refreshes the documents when it finishes', fakeAsync(() => {
    openDialog();
    chooseWriter(7);
    generateButton().click();
    http.expectOne(START_URL).flush({ jobId: 'job-1' });
    http.expectOne(jobUrl('job-1')).flush(jobDto());
    fixture.detectChanges();

    const rows = (): string[] => Array.from(host.querySelectorAll('.rp-job-row'))
      .map(row => (row.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(rows()).toEqual(['Executive Summary Writing 1', 'Report for AI Researchers and Developers Pending 0']);
    expect(q('.rp-job-status')!.getAttribute('role')).toBe('status');
    expect(text('.rp-job-status')).toBe('Writing 2 documents for Gemini Flash: 0 of 2 finished.');
    expect(q('.rp-job-log')).not.toBeNull();
    expect(generateButton().disabled).toBeTrue();

    tick(REPORT_PACK_POLL_MS - 1);
    http.expectNone(jobUrl('job-1'));
    tick(1);
    http.expectOne(jobUrl('job-1')).flush(jobDto({
      totalModelCalls: 3,
      documents: [
        { audience: ExecutiveSummary, status: 'Completed', documentId: 21, errorMessage: null, modelCalls: 1 },
        { audience: TechnicalReport, status: 'Repairing', documentId: null, errorMessage: null, modelCalls: 2 }
      ]
    }));
    fixture.detectChanges();
    expect(rows()).toEqual(['Executive Summary Completed 1', 'Report for AI Researchers and Developers Repairing 2']);
    expect(text('.rp-job-status')).toBe('Writing 2 documents for Gemini Flash: 1 of 2 finished.');

    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-1')).flush(jobDto({
      status: 'Completed',
      completedAtUtc: '2026-09-28T10:02:05Z',
      costUsd: 0.13,
      documents: [
        { audience: ExecutiveSummary, status: 'Completed', documentId: 21, errorMessage: null, modelCalls: 1 },
        { audience: TechnicalReport, status: 'CompletedWithWarnings', documentId: 22, errorMessage: null, modelCalls: 2 }
      ]
    }));
    fixture.detectChanges();
    expectDocumentsRefresh([doc(21, ExecutiveSummary), doc(22, TechnicalReport)]);

    expect(text('.rp-job-status')).toBe('Report pack for Gemini Flash: Completed.');
    expect(text('.rp-job-meta')).toContain('elapsed 2 min 05 s');
    expect(host.querySelectorAll('.rp-doc-row').length).toBe(2);
    expect(q('.rp-cancel-job')).toBeNull();
    tick(REPORT_PACK_POLL_MS * 3);
    http.expectNone(jobUrl('job-1'));
    fixture.destroy();
  }));

  it('cancels the running job, then reads its final state at once', fakeAsync(() => {
    openDialog();
    chooseWriter(7);
    generateButton().click();
    http.expectOne(START_URL).flush({ jobId: 'job-1' });
    http.expectOne(jobUrl('job-1')).flush(jobDto());
    fixture.detectChanges();

    q<HTMLButtonElement>('.rp-cancel-job')!.click();
    fixture.detectChanges();
    const cancel = http.expectOne(`${jobUrl('job-1')}/cancel`);
    expect(cancel.request.method).toBe('POST');
    cancel.flush({ cancelled: true });
    http.expectOne(jobUrl('job-1')).flush(jobDto({ status: 'Canceled', completedAtUtc: '2026-09-28T10:00:30Z' }));
    fixture.detectChanges();
    expectDocumentsRefresh();

    expect(text('.rp-job-status')).toBe('Report pack for Gemini Flash: Canceled.');
    expect(q('.rp-cancel-job')).toBeNull();
    expect(generateButton().disabled).toBeFalse();
    fixture.destroy();
  }));

  it('picks up a running job on open and backs off when its progress cannot be read', fakeAsync(() => {
    openDialog({ activeJob: jobDto({ id: 'job-5' }) });

    expect(host.querySelectorAll('.rp-job-row').length).toBe(2);
    expect(text('#rp-generate-blocked')).toBe('A report pack is being written. Wait for it to finish, or cancel it.');

    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush('down', { status: 503, statusText: 'Service Unavailable' });
    fixture.detectChanges();
    expect(text('.rp-job')).toContain('The job’s progress could not be read. Retrying.');

    tick(REPORT_PACK_POLL_MS);
    http.expectNone(jobUrl('job-5'));
    tick(REPORT_PACK_POLL_MS * 2);
    http.expectOne(jobUrl('job-5')).flush(jobDto({ id: 'job-5', status: 'Failed', completedAtUtc: '2026-09-28T10:00:10Z' }));
    expectDocumentsRefresh();
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // The documents
  // -------------------------------------------------------------------------------------------

  it('lists the suite\'s documents and flags one whose run changed', () => {
    openDialog({ documents: [doc(11, ExecutiveSummary, { runChangedSinceGeneration: true }), doc(12, TechnicalReport)] });

    const rows = host.querySelectorAll('.rp-doc-row');
    expect(rows.length).toBe(2);
    const changed = host.querySelector('.rp-doc-row[data-document-id="11"] .gh-tag-changed');
    expect(changed?.textContent?.trim()).toBe('Run changed since this document was written');
    expect(host.querySelector('.rp-doc-row[data-document-id="12"] .gh-tag-changed')).toBeNull();

    const preview = q('#rp-doc-11-preview')!;
    expect(preview.getAttribute('aria-label')).toBe('Preview Executive Summary: Gemini Flash, 2026-09-21 16:00 UTC');
    expect(preview.getAttribute('interestfor')).toBe('rp-tip-preview-11');
    expect(preview.hasAttribute('title')).toBeFalse();
    expect(q('#rp-doc-11-delete')!.classList).toContain('action-btn-danger');
  });

  it('opens the Download Center with the row\'s document, or with the selected ones', () => {
    openDialog({ documents: [doc(11, ExecutiveSummary), doc(12, TechnicalReport)] });
    const open = spyOn(component.downloadCenter!, 'open');

    q<HTMLButtonElement>('#rp-doc-12-download')!.click();
    expect(open).toHaveBeenCalledOnceWith({ kind: 'documents', documentIds: [12] });

    const toolbar = q<HTMLButtonElement>('#rp-download-selected')!;
    expect(toolbar.getAttribute('aria-disabled')).toBe('true');
    toolbar.click();
    expect(open).toHaveBeenCalledTimes(1);

    q<HTMLInputElement>('#rp-doc-11-select')!.click();
    q<HTMLInputElement>('#rp-doc-12-select')!.click();
    fixture.detectChanges();
    expect(text('.rp-selection-count')).toBe('2 selected');
    expect(toolbar.getAttribute('aria-disabled')).toBeNull();
    toolbar.click();
    expect(open).toHaveBeenCalledWith({ kind: 'documents', documentIds: [11, 12] });
  });

  it('deletes a document only after the confirmation', () => {
    openDialog({ documents: [doc(11, ExecutiveSummary), doc(12, TechnicalReport)] });

    q<HTMLButtonElement>('#rp-doc-11-delete')!.click();
    fixture.detectChanges();
    const confirm = q<HTMLDialogElement>('dialog.rp-delete-dialog')!;
    expect(confirm.open).toBeTrue();
    expect(text('.rp-delete-text')).toContain('Executive Summary: Gemini Flash');

    q<HTMLButtonElement>('.rp-delete-cancel')!.click();
    fixture.detectChanges();
    expect(confirm.open).toBeFalse();
    http.expectNone(r => r.method === 'DELETE');

    q<HTMLButtonElement>('#rp-doc-11-delete')!.click();
    fixture.detectChanges();
    q<HTMLButtonElement>('.rp-delete-confirm')!.click();
    const request = http.expectOne(r => r.method === 'DELETE');
    expect(request.request.url).toBe(`${DOCUMENTS_URL}/11`);
    request.flush(null);
    fixture.detectChanges();

    expect(confirm.open).toBeFalse();
    expect(Array.from(host.querySelectorAll('.rp-doc-row')).map(row => row.getAttribute('data-document-id'))).toEqual(['12']);
    expect(text('.rp-library-status')).toBe('Deleted Executive Summary: Gemini Flash, 2026-09-21 16:00 UTC.');
  });

  // -------------------------------------------------------------------------------------------
  // The preview
  // -------------------------------------------------------------------------------------------

  it('previews through the safe converter, and re-renders for each disclosure and naming it switches to', () => {
    const toHtml = spyOn(reportPackPreviewIo, 'toHtml').and.callThrough();
    openDialog({ documents: [doc(11, TechnicalReport, { allowedDisclosures: [Summary, Detailed] })] });

    q<HTMLButtonElement>('#rp-doc-11-preview')!.click();
    fixture.detectChanges();
    const dialog = q<HTMLDialogElement>('dialog.rp-preview-dialog')!;
    expect(dialog.open).toBeTrue();

    const detail = http.expectOne(r => r.method === 'GET' && r.url === `${DOCUMENTS_URL}/11`);
    detail.flush({
      ...doc(11, TechnicalReport),
      validationNotes: [{ rule: 7, location: 'weaknesses[1]', message: 'Too long.', dropped: true }]
    } as BenchmarkReportDocumentDetailDto);

    const render = (): TestRequest => http.expectOne(r => r.url === `${DOCUMENTS_URL}/11/render`);
    const first = render();
    expect(first.request.params.get('disclosure')).toBe('summary');
    expect(first.request.params.get('peers')).toBe('anonymized');
    const summaryMarkdown = '# Technical Report\n\nTopics only. <img src="x" onerror="alert(1)"> <script>alert(1)</script>';
    first.flush(summaryMarkdown);
    fixture.detectChanges();

    expect(toHtml).toHaveBeenCalledOnceWith(summaryMarkdown);
    const panel = q('#rp-preview-panel')!;
    expect(panel.querySelector('h1')?.textContent).toBe('Technical Report');
    expect(panel.querySelector('img')).toBeNull();
    expect(panel.querySelector('script')).toBeNull();
    expect(text('.rp-preview-notes')).toContain('Rule 7, weaknesses[1]: Too long. (dropped)');

    // Only the document's own levels are offered.
    const disclosureTabs = Array.from(host.querySelectorAll<HTMLButtonElement>('.rp-preview-disclosure [role="tab"]'));
    expect(disclosureTabs.map(tab => tab.textContent!.trim())).toEqual(['Summary', 'Detailed']);
    expect(disclosureTabs[0].getAttribute('aria-selected')).toBe('true');

    disclosureTabs[1].click();
    fixture.detectChanges();
    const second = render();
    expect(second.request.params.get('disclosure')).toBe('detailed');
    expect(second.request.params.get('peers')).toBe('anonymized');
    second.flush('## Detailed text');
    fixture.detectChanges();
    expect(toHtml).toHaveBeenCalledTimes(2);
    expect(toHtml.calls.mostRecent().args[0]).toBe('## Detailed text');
    expect(panel.querySelector('h2')?.textContent).toBe('Detailed text');
    expect(disclosureTabs[1].getAttribute('aria-selected')).toBe('true');
    expect(panel.getAttribute('aria-labelledby')).toBe(`rp-preview-disclosure-${Detailed}`);

    const named = Array.from(host.querySelectorAll<HTMLButtonElement>('.rp-preview-naming [role="tab"]'))
      .find(tab => tab.textContent!.trim() === 'Named')!;
    named.click();
    fixture.detectChanges();
    const third = render();
    expect(third.request.params.get('disclosure')).toBe('detailed');
    expect(third.request.params.get('peers')).toBe('named');
    third.flush('## Named peers');
    fixture.detectChanges();
    expect(toHtml).toHaveBeenCalledTimes(3);
    expect(named.getAttribute('aria-selected')).toBe('true');
  });

  it('previews an Internal Brief at Full only', () => {
    openDialog({ documents: [doc(13, InternalBrief)] });
    q<HTMLButtonElement>('#rp-doc-13-preview')!.click();
    fixture.detectChanges();

    http.expectOne(r => r.method === 'GET' && r.url === `${DOCUMENTS_URL}/13`).flush({ ...doc(13, InternalBrief), validationNotes: [] });
    const render = http.expectOne(r => r.url === `${DOCUMENTS_URL}/13/render`);
    expect(render.request.params.get('disclosure')).toBe('full');
    render.flush('# Brief');
    fixture.detectChanges();

    const tabs = Array.from(host.querySelectorAll<HTMLButtonElement>('.rp-preview-disclosure [role="tab"]'));
    expect(tabs.map(tab => tab.textContent!.trim())).toEqual(['Full']);
  });

  // -------------------------------------------------------------------------------------------
  // Nesting and the grader guide
  // -------------------------------------------------------------------------------------------

  it('stops the close and cancel events of itself and every nested dialog', () => {
    openDialog({ documents: [doc(11, ExecutiveSummary)] });
    q<HTMLButtonElement>('#rp-doc-11-preview')!.click();
    fixture.detectChanges();

    // The host element stands in for the wizard's dialog around this one.
    const heard: string[] = [];
    host.addEventListener('close', () => heard.push('close'));
    host.addEventListener('cancel', () => heard.push('cancel'));

    for (const selector of ['dialog.rp-dialog', 'dialog.rp-preview-dialog', 'dialog.rp-delete-dialog',
      'dialog.benchmark-download-center-dialog']) {
      const dialog = q<HTMLDialogElement>(selector)!;
      expect(dialog).withContext(selector).not.toBeNull();
      // A real close event does not bubble; a bubbling one proves the handlers stop it.
      dialog.dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
      dialog.dispatchEvent(new Event('close', { bubbles: true }));
    }
    expect(heard).toEqual([]);
  });

  it('asks for the grader guide from the writer field\'s link', () => {
    openDialog();
    const requests: number[] = [];
    component.graderGuideRequested.subscribe(() => requests.push(1));

    const link = q<HTMLButtonElement>('.rp-guide-link')!;
    expect(link.textContent!.trim()).toBe('More: How the graders work');
    link.click();

    expect(requests.length).toBe(1);
    expect(text('#rp-writer-tip')).toContain('Recommended:');
  });

  it('emits closed once the dialog has closed', async () => {
    openDialog();
    let closed = 0;
    component.closed.subscribe(() => closed++);

    component.close();
    await new Promise(resolve => setTimeout(resolve));

    expect(closed).toBe(1);
    expect(component.dialog!.nativeElement.open).toBeFalse();
  });
});
