import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';

import {
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPackJobDto,
  BenchmarkReportPackPreviewDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import type { PdfViewerRequest } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import type { BenchmarkModelComparisonEntryDto } from '../model-comparison/model-comparison.models';
import { RunReportFrameComponent } from '../run-report-frame/run-report-frame.component';
import {
  BenchmarkReportPackDialogComponent,
  REPORT_PACK_POLL_MS,
  REPORT_PACK_PREVIEW_DEBOUNCE_MS,
  REPORT_PACK_STORAGE_KEY,
  ReportPackContext
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
  entryKeys: ['run:1', 'run:2', 'run:3', 'group:4'],
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
    origin: 1,
    comparisonKey: 'cmp-1',
    comparisonEntryCount: 4,
    peerCount: 2,
    pricingBasis: 'Current',
    peersChangedSinceGeneration: false,
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
  const confirmDialog = (): HTMLDialogElement => q<HTMLDialogElement>('dialog.rp-same-provider-dialog')!;

  interface OpenOptions {
    configs?: SystemAiConfigDto[];
    documents?: BenchmarkReportDocumentListItemDto[];
    activeJob?: BenchmarkReportPackJobDto | null;
  }

  /** Opens the dialog and answers its three opening requests: writers, the comparison's documents, the active job. */
  function openDialog(options: OpenOptions = {}): void {
    component.open(CONTEXT);
    fixture.detectChanges();
    http.expectOne(SYSTEM_CONFIGS_URL).flush(options.configs ?? CONFIGS);
    const list = http.expectOne(r => r.method === 'GET' && r.url === DOCUMENTS_URL);
    expect(list.request.params.get('comparison')).toBe('run:1,run:2,run:3,group:4');
    expect(list.request.params.get('origin')).toBe('reportPack');
    expect(list.request.params.has('suiteId')).toBeFalse();
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

  /** The library lists the comparison's documents again once the host's change reaches it. */
  function expectDocumentsRefresh(documents: BenchmarkReportDocumentListItemDto[] = []): void {
    fixture.detectChanges();
    const list = http.expectOne(r => r.method === 'GET' && r.url === DOCUMENTS_URL);
    expect(list.request.params.get('comparison')).toBe('run:1,run:2,run:3,group:4');
    list.flush(documents);
    fixture.detectChanges();
  }

  // -------------------------------------------------------------------------------------------
  // Layout
  // -------------------------------------------------------------------------------------------

  it('lays out the form in the sidebar and the documents in the main area, under its subtitle', () => {
    openDialog();

    const frame = q('app-run-report-frame')!;
    expect(frame.classList).toContain('rrf-layout-sidebar');
    const sidebar = q('aside.rrf-sidebar')!;
    const main = q('section.rrf-main')!;
    expect(sidebar.getAttribute('aria-label')).toBe('New report pack');
    expect(main.getAttribute('aria-label')).toBe('Documents of this comparison');

    // Subject, Documents, Report writer, the estimate, Generate: in that order.
    const order = ['#rp-subject', '.rp-documents-choice', '.rp-writer-selector', '#rp-estimate', '.rp-generate']
      .map(selector => sidebar.querySelector(selector));
    expect(order.every(element => element !== null)).toBeTrue();
    for (let i = 1; i < order.length; i++) {
      expect(order[i - 1]!.compareDocumentPosition(order[i]!) & Node.DOCUMENT_POSITION_FOLLOWING).withContext(`${i}`).toBeTruthy();
    }

    expect(main.querySelector('h4.gh-section-title#rp-documents-heading')?.textContent?.trim()).toBe('Documents of this comparison');
    expect(text('.rp-scope-line'))
      .toBe('Reports whose subject is one of the 4 models of this comparison, written for this same set of models.');
    expect(main.querySelector('app-report-document-library')).not.toBeNull();
    expect(text('.rp-subtitle')).toBe('Board Suite · 4 models · 3 possible subjects');
  });

  it('restores the stored sidebar width and stores a new one beside the writer', fakeAsync(() => {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, JSON.stringify({ writerConfigId: 8, sidebarWidth: 448 }));
    openDialog();
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    http.expectOne(PREVIEW_URL).flush(previewDto());
    fixture.detectChanges();

    const frame = fixture.debugElement.query(By.directive(RunReportFrameComponent)).componentInstance as RunReportFrameComponent;
    expect(frame.sidebarWidth).toBe(448);

    frame.sidebarWidthChange.emit(416);
    expect(JSON.parse(localStorage.getItem(REPORT_PACK_STORAGE_KEY)!)).toEqual({ writerConfigId: 8, sidebarWidth: 416 });
    fixture.destroy();
  }));

  it('falls back to the frame\'s default width when the stored record is unreadable', () => {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, '{not json');
    openDialog();

    const frame = fixture.debugElement.query(By.directive(RunReportFrameComponent)).componentInstance as RunReportFrameComponent;
    expect(frame.sidebarWidth).toBeNull();
    expect(frame.sidebarWidthPx).toBe(384);
  });

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

    const line = q('.rp-refusal')!;
    expect(line.classList).toContain('gh-field-error');
    expect(line.getAttribute('role')).toBe('alert');
    expect(line.textContent).toContain(refusal);
    expect(line.compareDocumentPosition(generateButton()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(q('.rp-writer-selector .selector-trigger')!.getAttribute('aria-describedby')).toContain('rp-writer-refusal');
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

    const panel = q('#rp-estimate')!;
    expect(panel.classList).toContain('gh-estimate-panel');
    expect(panel.getAttribute('role')).toBe('status');
    expect(text('#rp-estimate .gh-estimate-label')).toBe('Estimated cost');
    expect(text('#rp-estimate .gh-estimate-total')).toBe('about $0.12');
    const parts = Array.from(panel.querySelectorAll('.gh-estimate-parts > div'))
      .map(part => [part.querySelector('dt')!.textContent!.trim(), part.querySelector('dd')!.textContent!.trim()]);
    expect(parts).toEqual([['Executive Summary', '$0.05'], ['Report for AI Researchers and Developers', '$0.07']]);
    expect(text('#rp-estimate .gh-estimate-note')).toContain('For Gemini Flash against 1 peer.');
    expect(generateButton().getAttribute('aria-describedby')).toBe('rp-estimate');
    expect(generateButton().disabled).toBeFalse();
    fixture.destroy();
  }));

  it('keeps the estimate panel in place while empty, and busy while estimating', fakeAsync(() => {
    openDialog();
    const panel = q('#rp-estimate')!;
    expect(panel.getAttribute('role')).toBe('status');
    expect(panel.classList).toContain('is-empty');

    component.selectWriter(7);
    fixture.detectChanges();
    expect(panel.getAttribute('aria-busy')).toBe('true');
    expect(panel.classList).toContain('is-muted');
    expect(text('#rp-estimate')).toBe('Estimating…');
    expect(generateButton().getAttribute('aria-describedby')).toBe('rp-estimate rp-generate-blocked');

    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    http.expectOne(PREVIEW_URL).flush(previewDto({ estimates: [previewDto().estimates[0]], estimatedTotalCostUsd: 0.05 }));
    fixture.detectChanges();
    expect(panel.hasAttribute('aria-busy')).toBeFalse();
    expect(panel.querySelector('.gh-estimate-parts')).toBeNull();
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
  // The same-provider writer (D3)
  // -------------------------------------------------------------------------------------------

  it('warns about a same-provider writer, and confirms on every Generate before sending the acknowledgment', fakeAsync(() => {
    openDialog();
    const warning = 'The writer shares Anthropic with the subject; its documents may favor its own family.';
    chooseWriter(7, previewDto({ sameProviderWarning: warning }));

    const alert = q('.rp-same-provider')!;
    expect(alert.classList).toContain('alert-warning');
    expect(alert.textContent).toContain(warning);
    expect(q('#rp-acknowledge')).toBeNull();
    expect(q('.rp-same-provider input[type="checkbox"]')).toBeNull();
    expect(generateButton().disabled).toBeFalse();

    // Cancel: nothing is sent, and focus returns to Generate.
    generateButton().click();
    fixture.detectChanges();
    expect(confirmDialog().open).toBeTrue();
    expect(text('#rp-same-provider-title')).toBe('Same-Provider Report Writer');
    expect(text('.rp-same-provider-confirm-text')).toBe(warning);
    http.expectNone(START_URL);
    q<HTMLButtonElement>('.rp-same-provider-cancel')!.click();
    fixture.detectChanges();
    expect(confirmDialog().open).toBeFalse();
    expect(document.activeElement).toBe(generateButton());
    http.expectNone(START_URL);

    // Asked again, never remembered; Write Anyway sends the acknowledgment.
    generateButton().click();
    fixture.detectChanges();
    expect(confirmDialog().open).toBeTrue();
    const confirm = q<HTMLButtonElement>('.rp-same-provider-confirm')!;
    expect(confirm.textContent!.trim()).toBe('Write Anyway');
    expect(confirm.querySelector('svg.btn-icon')).not.toBeNull();
    confirm.click();
    fixture.detectChanges();
    expect(confirmDialog().open).toBeFalse();

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

  it('opens the same confirmation when the server answers a start with a same-provider 409', fakeAsync(() => {
    openDialog();
    chooseWriter(7);
    generateButton().click();
    const message = 'The writer and the subject are both from Anthropic. Acknowledge the warning to continue.';
    const first = http.expectOne(START_URL);
    expect(first.request.body.acknowledgeSameProvider).toBeFalse();
    first.flush(
      { sameProvider: true, provider: 'Anthropic', testedModelDisplayName: 'Gemini Flash', assessorModelDisplayName: 'Claude Opus writer', message },
      { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(confirmDialog().open).toBeTrue();
    expect(text('.rp-same-provider-confirm-text')).toBe(message);
    expect(text('.rp-same-provider')).toContain(message);
    expect(q('.rp-start-error')).toBeNull();

    q<HTMLButtonElement>('.rp-same-provider-confirm')!.click();
    fixture.detectChanges();
    const second = http.expectOne(START_URL);
    expect(second.request.body.acknowledgeSameProvider).toBeTrue();
    second.flush({ jobId: 'job-1' });
    http.expectOne(jobUrl('job-1')).flush(jobDto());
    fixture.detectChanges();
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // 409 and 429
  // -------------------------------------------------------------------------------------------

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

  it('shows no job card until a job exists, with the live line in place', () => {
    openDialog();

    expect(q('.rp-job')).toBeNull();
    const line = q('.rp-job-status')!;
    expect(line.getAttribute('role')).toBe('status');
    expect(line.textContent!.trim()).toBe('');
    // The card is the first thing of the main area after its heading.
    const main = q('section.rrf-main')!;
    expect(main.querySelector('.rp-job-status')!.compareDocumentPosition(main.querySelector('app-report-document-library')!)
      & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('polls a started job with a stage rail and a stat strip, then collapses it and reloads the documents', fakeAsync(() => {
    openDialog();
    chooseWriter(7);
    generateButton().click();
    http.expectOne(START_URL).flush({ jobId: 'job-1' });
    fixture.detectChanges();
    // Before the first reading, the rail stands at Queued.
    expect(text('.rp-job-rail .run-stage.is-current .run-stage-name')).toBe('Queued');

    http.expectOne(jobUrl('job-1')).flush(jobDto());
    fixture.detectChanges();

    const rows = (): string[] => Array.from(host.querySelectorAll('.rp-job-row'))
      .map(row => (row.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(rows()).toEqual(['Executive Summary Writing 1', 'Report for AI Researchers and Developers Pending 0']);
    expect(q('.rp-job-status')!.getAttribute('role')).toBe('status');
    expect(text('.rp-job-status')).toBe('Writing 2 documents for Gemini Flash: 0 of 2 finished.');
    expect(q('.rp-job-log')).not.toBeNull();
    expect(generateButton().disabled).toBeTrue();

    const stages = Array.from(host.querySelectorAll('.rp-job-rail .run-stage'));
    expect(q('.rp-job-rail')!.classList).toContain('run-stage-rail');
    expect(stages.map(stage => stage.querySelector('.run-stage-name')!.textContent!.trim()))
      .toEqual(['Queued', 'Preparing', 'Executive Summary', 'Report for AI Researchers and Developers', 'Done']);
    expect(stages.map(stage => stage.classList.contains('is-done'))).toEqual([true, true, false, false, false]);
    expect(stages[2].classList).toContain('is-current');
    expect(stages[2].getAttribute('aria-current')).toBe('step');

    expect(q('.rp-job-stats')!.classList).toContain('run-stat-strip');
    expect(text('.rp-job-writer')).toBe('Claude Opus writer');
    expect(text('.rp-job-calls')).toBe('1');
    expect(text('.rp-job-tokens')).toBe('6,000 in · 0 out');
    expect(text('.rp-job-cost-label')).toBe('Cost so far');
    expect(text('.rp-job-estimate')).toBe('about $0.12');

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
    expect(text('.rp-job-rail .run-stage.is-current .run-stage-name')).toBe('Report for AI Researchers and Developers');

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
    expectDocumentsRefresh([doc(21, ExecutiveSummary), doc(22, TechnicalReport)]);

    expect(text('.rp-job-status')).toBe('Report pack for Gemini Flash: Completed.');
    expect(text('.rp-job-summary')).toBe('Written: 2 documents, 2 min 05 s, $0.13');
    expect(q('.rp-job-rail')).toBeNull();
    expect(q('.rp-job-stats')).toBeNull();
    expect(host.querySelectorAll('.rdl-row').length).toBe(2);
    expect(q('.rp-cancel-job')).toBeNull();
    tick(REPORT_PACK_POLL_MS * 3);
    http.expectNone(jobUrl('job-1'));

    // Dismiss removes the card; the live line empties with it.
    q<HTMLButtonElement>('.rp-dismiss-job')!.click();
    fixture.detectChanges();
    expect(q('.rp-job')).toBeNull();
    expect(text('.rp-job-status')).toBe('');
    expect(document.activeElement).toBe(q('#rp-documents-heading'));
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
    expectDocumentsRefresh();

    expect(text('.rp-job-status')).toBe('Report pack for Gemini Flash: Canceled.');
    expect(text('.rp-job-summary')).toBe('Canceled: 0 of 2 documents written, 30 s, Unknown');
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
  // The documents (the library, idPrefix "rp")
  // -------------------------------------------------------------------------------------------

  it('lists the comparison\'s documents in the library, with the ids under "rp", and flags one whose run changed', () => {
    openDialog({ documents: [doc(11, ExecutiveSummary, { runChangedSinceGeneration: true }), doc(12, TechnicalReport)] });

    expect(host.querySelectorAll('.rdl-row').length).toBe(2);
    const changed = q('.rdl-row[data-document-id="11"] .rdl-tag-run-changed');
    expect(changed?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Run changed since this document was written');
    expect(q('.rdl-row[data-document-id="12"] .rdl-tag-run-changed')).toBeNull();

    const view = q('#rp-doc-11-view')!;
    expect(view.getAttribute('aria-label')).toBe('View Executive Summary: Gemini Flash, 2026-09-21 16:00 UTC');
    expect(view.getAttribute('interestfor')).toBe('rp-tip-view-11');
    expect(view.hasAttribute('title')).toBeFalse();
    expect(q('#rp-doc-11-select')).not.toBeNull();
    expect(q('#rp-doc-11-download')).not.toBeNull();
    expect(q('#rp-doc-11-delete')!.classList).toContain('action-btn-danger');
    // The comparison is the dialog's own, so the library shows no Compared with column.
    expect(q('.rdl-col-compared')).toBeNull();
  });

  it('offers the Downloads notice once the comparison has a document, opening the Download Center on every one', () => {
    openDialog();
    expect(q('.rp-download-notice')).toBeNull();

    component.open(CONTEXT);
    fixture.detectChanges();
    http.expectOne(SYSTEM_CONFIGS_URL).flush(CONFIGS);
    http.expectOne(ACTIVE_JOB_URL).flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(r => r.method === 'GET' && r.url === DOCUMENTS_URL).flush([doc(11, ExecutiveSummary), doc(12, TechnicalReport)]);
    fixture.detectChanges();

    const notice = q('.rp-download-notice')!;
    expect(notice.classList).toContain('alert-info');
    expect(notice.compareDocumentPosition(q('app-report-document-library')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const open = spyOn(component.library!.downloadCenter!, 'open');
    const button = q<HTMLButtonElement>('.rp-open-downloads')!;
    expect(button.textContent!.trim()).toBe('Open Download Center');
    button.click();
    expect(open).toHaveBeenCalledOnceWith({
      kind: 'documents',
      documentIds: [11, 12],
      title: 'Comparison reports',
      subtitle: '2 documents of the comparison of 4 models'
    });
  });

  it('views a document in the PDF viewer, peer names offered, at its highest disclosure', () => {
    openDialog({ documents: [doc(11, TechnicalReport, { allowedDisclosures: [Summary, Detailed] })] });
    const open = spyOn(component.library!.pdfViewer!, 'open');

    q<HTMLButtonElement>('#rp-doc-11-view')!.click();

    expect(open).toHaveBeenCalledTimes(1);
    const request = open.calls.mostRecent().args[0] as PdfViewerRequest;
    expect(request.title).toBe('Technical Report: Gemini Flash');
    expect(request.variants!.map(v => v.key)).toEqual(['summary', 'detailed']);
    expect(request.initialVariant).toBe('detailed');
    expect(request.secondaryVariants?.initial).toBe('named');
    // Viewing renders server-side; no Markdown preview is requested.
    http.expectNone(r => r.url.endsWith('/render'));
  });

  // -------------------------------------------------------------------------------------------
  // Nesting and the grader guide
  // -------------------------------------------------------------------------------------------

  it('stops the close and cancel events of itself and every nested dialog', () => {
    openDialog({ documents: [doc(11, ExecutiveSummary)] });

    // The host element stands in for the wizard's dialog around this one.
    const heard: string[] = [];
    host.addEventListener('close', () => heard.push('close'));
    host.addEventListener('cancel', () => heard.push('cancel'));

    for (const selector of ['dialog.rp-dialog', 'dialog.rp-same-provider-dialog', 'dialog.rdl-delete-dialog',
      'dialog.benchmark-download-center-dialog', 'dialog.pdfv']) {
      const dialog = q<HTMLDialogElement>(selector)!;
      expect(dialog).withContext(selector).not.toBeNull();
      // A real close event does not bubble; a bubbling one proves the handlers stop it.
      dialog.dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
      dialog.dispatchEvent(new Event('close', { bubbles: true }));
    }
    expect(heard).toEqual([]);
  });

  it('asks for the grader guide from the writer field\'s link, and explains the writer per document in a dialog', () => {
    openDialog();
    const requests: number[] = [];
    component.graderGuideRequested.subscribe(() => requests.push(1));

    const link = q<HTMLButtonElement>('.rp-guide-link')!;
    expect(link.textContent!.trim()).toBe('More: How the graders work');
    link.click();
    expect(requests.length).toBe(1);

    const infoButton = q<HTMLButtonElement>('.gh-field-row app-info-tip button.gh-info-btn')!;
    expect(infoButton.getAttribute('aria-label')).toBe('About Report writer');
    expect(infoButton.getAttribute('aria-haspopup')).toBe('dialog');
    const tip = q('#rp-writer-tip')!;
    const terms = Array.from(tip.querySelectorAll('dl > div .gh-info-term')).map(term => term.textContent!.trim());
    expect(terms).toEqual(['Executive Summary', 'Report for AI Researchers and Developers', 'Internal Improvement Brief', 'Every document']);
    const brief = Array.from(tip.querySelectorAll('dl > div'))[2].textContent!.replace(/\s+/g, ' ');
    expect(brief).toContain('strongest scoring-tier model');
    expect(brief).toContain('medium effort');
    expect(q('#rp-writer-tip-title')!.textContent!.trim()).toBe('Choosing a report writer');
  });

  it('emits closed once the dialog has closed', async () => {
    openDialog();
    let closed = 0;
    component.closed.subscribe(() => closed++);

    // The browser fires `close` from its own queued task, which a fixed timer can overtake; the
    // component's template listener was registered first, so it has run when this one resolves.
    const dialog = component.dialog!.nativeElement;
    const dialogClosed = new Promise<void>(resolve =>
      dialog.addEventListener('close', () => resolve(), { once: true }));
    component.close();
    await dialogClosed;

    expect(closed).toBe(1);
    expect(dialog.open).toBeFalse();
  });
});
