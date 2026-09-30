import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';

import {
  BenchmarkReportAudience,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackJobDto,
  BenchmarkReportPackPreviewDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import type { BenchmarkModelComparisonEntryDto } from '../model-comparison/model-comparison.models';
import { RunReportFrameComponent } from '../run-report-frame/run-report-frame.component';
import { ReportChartPickerComponent } from './report-chart-picker.component';
import { DEFAULT_CHART_SELECTION, REPORT_CHART_FIGURES, ReportChartRowStatus, ReportChartSelection } from './report-charts';
import { reportPackIo } from './report-pack-diagnostics';
import {
  REPORT_PACK_POLL_MS,
  REPORT_PACK_PREVIEW_DEBOUNCE_MS,
  REPORT_PACK_STORAGE_KEY,
  ReportPackContext,
  ReportPackPanelComponent
} from './report-pack-panel.component';

const { ExecutiveSummary, TechnicalReport, InternalBrief } = BenchmarkReportAudience;

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
    startedByUserId: 'user-secret-id',
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

function written(audience: BenchmarkReportAudience, documentId: number, status = 'Completed'): BenchmarkReportPackDocumentProgressDto {
  return { audience, status, documentId, errorMessage: null, modelCalls: 1 };
}

describe('ReportPackPanelComponent', () => {
  let fixture: ComponentFixture<ReportPackPanelComponent>;
  let component: ReportPackPanelComponent;
  let http: HttpTestingController;
  let host: HTMLElement;

  beforeEach(async () => {
    localStorage.removeItem(REPORT_PACK_STORAGE_KEY);
    await TestBed.configureTestingModule({
      imports: [ReportPackPanelComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    fixture = TestBed.createComponent(ReportPackPanelComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    host = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('chartSelection', DEFAULT_CHART_SELECTION);
    fixture.componentRef.setInput('chartsAvailable', REPORT_CHART_FIGURES.map(figure => figure.key));
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
  const cellText = (audience: BenchmarkReportAudience, selector: string): string =>
    text(`.rp-job-row[data-audience="${audience}"] ${selector}`);

  interface OpenOptions {
    configs?: SystemAiConfigDto[];
    activeJob?: BenchmarkReportPackJobDto | null;
    context?: ReportPackContext;
  }

  /** Gives the panel its comparison and answers its two opening requests: the writers and the active job. */
  function openPanel(options: OpenOptions = {}): void {
    fixture.componentRef.setInput('context', options.context ?? CONTEXT);
    fixture.detectChanges();
    http.expectOne(SYSTEM_CONFIGS_URL).flush(options.configs ?? CONFIGS);
    answerActiveJob(options.activeJob ?? null);
  }

  function answerActiveJob(job: BenchmarkReportPackJobDto | null): void {
    const active = http.expectOne(ACTIVE_JOB_URL);
    if (job) {
      active.flush(job);
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

  /** Generates with writer 7 and answers the start and the first reading of the job. */
  function startJob(job: BenchmarkReportPackJobDto = jobDto()): void {
    chooseWriter(7);
    generateButton().click();
    http.expectOne(START_URL).flush({ jobId: job.id });
    http.expectOne(jobUrl(job.id)).flush(job);
    fixture.detectChanges();
  }

  // -------------------------------------------------------------------------------------------
  // Layout
  // -------------------------------------------------------------------------------------------

  it('lays out the form in the sidebar and the progress in the main area, under the step heading', () => {
    openPanel();

    const frame = q('app-run-report-frame')!;
    expect(frame.classList).toContain('rrf-layout-sidebar');
    const sidebar = q('aside.rrf-sidebar')!;
    const main = q('section.rrf-main')!;
    expect(sidebar.getAttribute('aria-label')).toBe('New report pack');
    expect(main.getAttribute('aria-label')).toBe('Report pack progress');

    // Subject, Documents, Charts, Report writer, the estimate, Generate: in that order.
    const order = ['#rp-subject', '.rp-documents-choice', '.rp-charts-choice', '.rp-writer-selector', '#rp-estimate', '.rp-generate']
      .map(selector => sidebar.querySelector(selector));
    expect(order.every(element => element !== null)).toBeTrue();
    for (let i = 1; i < order.length; i++) {
      expect(order[i - 1]!.compareDocumentPosition(order[i]!) & Node.DOCUMENT_POSITION_FOLLOWING).withContext(`${i}`).toBeTruthy();
    }

    const heading = q('h4.gh-section-title#rp-heading')!;
    expect(heading.textContent!.trim()).toBe('Reports');
    expect(component.headingId).toBe('rp-heading');
    expect(text('.rp-subtitle')).toBe('Board Suite · 4 models · 3 possible subjects');
    expect(main.querySelector('#rp-progress-heading')?.textContent?.trim()).toBe('Report pack progress');

    // No dialog shell and no document library: the wizard is the dialog, and step 4 lists the documents.
    expect(q('dialog.rp-dialog')).toBeNull();
    expect(q('app-report-document-library')).toBeNull();
    expect(q('.rp-open-downloads')).toBeNull();
    http.expectNone(r => r.url === DOCUMENTS_URL);
  });

  it('derives its ids from idPrefix', () => {
    fixture.componentRef.setInput('idPrefix', 'mcr');
    openPanel();

    expect(q('h4#mcr-heading')).not.toBeNull();
    expect(q('#mcr-subject')).not.toBeNull();
    expect(q('#mcr-estimate')).not.toBeNull();
    expect(q('#rp-subject')).toBeNull();
    expect(generateButton().getAttribute('aria-describedby')).toBe('mcr-estimate mcr-generate-blocked');
  });

  it('restores the stored sidebar width and stores a new one beside the writer', fakeAsync(() => {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, JSON.stringify({ writerConfigId: 8, sidebarWidth: 448 }));
    openPanel();
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
    openPanel();

    const frame = fixture.debugElement.query(By.directive(RunReportFrameComponent)).componentInstance as RunReportFrameComponent;
    expect(frame.sidebarWidth).toBeNull();
    expect(frame.sidebarWidthPx).toBe(384);
  });

  // -------------------------------------------------------------------------------------------
  // Defaults and the self-refusal
  // -------------------------------------------------------------------------------------------

  it('offers the first non-Excluded entry, Excluded entries absent and Degraded ones marked', () => {
    openPanel();

    const select = q<HTMLSelectElement>('#rp-subject')!;
    const options = Array.from(select.options).map(option => option.textContent!.trim());
    expect(options).toEqual(['Gemini Flash', 'Claude Opus (speed degraded)', 'GPT Sol group']);
    expect(select.value).toBe('run:1');
    expect(component.subjectKey).toBe('run:1');
  });

  it('checks the Executive Summary and the Report for AI Researchers and Developers by default, not the Internal Brief', () => {
    openPanel();

    const names = Array.from(host.querySelectorAll('.rp-audience-name')).map(name => (name.textContent ?? '').trim());
    expect(names).toEqual(['Executive Summary', 'Report for AI Researchers and Developers', 'Internal Improvement Brief']);

    expect(q<HTMLInputElement>(`#rp-audience-${ExecutiveSummary}`)!.checked).toBeTrue();
    expect(q<HTMLInputElement>(`#rp-audience-${TechnicalReport}`)!.checked).toBeTrue();
    expect(q<HTMLInputElement>(`#rp-audience-${InternalBrief}`)!.checked).toBeFalse();
    expect(component.selectedAudiences).toEqual([ExecutiveSummary, TechnicalReport]);
  });

  it('offers only enabled Benchmark-role configurations with a key as writers, and requires one', () => {
    openPanel({ configs: [...CONFIGS, config(11, 'Keyless writer', 'OpenAI', { hasApiKey: false })] });

    expect(component.writers.map(writer => writer.id)).toEqual([7, 8]);
    expect(component.writerId).toBeNull();
    expect(generateButton().disabled).toBeTrue();
    expect(text('#rp-generate-blocked')).toBe('Choose a report writer.');
    expect(http.match(PREVIEW_URL).length).toBe(0);
  });

  it('restores the remembered writer while it still qualifies', fakeAsync(() => {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, JSON.stringify({ writerConfigId: 8 }));
    openPanel();

    expect(component.writerId).toBe(8);
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    http.expectOne(PREVIEW_URL).flush(previewDto());
    fixture.destroy();
  }));

  it('shows the self-refusal before Generate, and keeps Generate disabled while it stands', fakeAsync(() => {
    openPanel();
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
  // The comparison changes
  // -------------------------------------------------------------------------------------------

  it('keeps the form when the comparison is recomputed with the same entry keys', fakeAsync(() => {
    openPanel();
    chooseWriter(7);
    const select = q<HTMLSelectElement>('#rp-subject')!;
    select.value = 'group:4';
    select.dispatchEvent(new Event('change'));
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    http.expectOne(PREVIEW_URL).flush(previewDto({ subjectKey: 'group:4' }));

    fixture.componentRef.setInput('context', { ...CONTEXT, pricingBasis: 'AsRun' });
    fixture.detectChanges();

    http.expectNone(ACTIVE_JOB_URL);
    expect(component.subjectKey).toBe('group:4');
    expect(component.writerId).toBe(7);
    // The pricing basis changed, so the estimate is asked again, with it.
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    const again = http.expectOne(PREVIEW_URL);
    expect(again.request.body.pricingBasis).toBe(0);
    expect(again.request.body.subjectKey).toBe('group:4');
    again.flush(previewDto());
    fixture.destroy();
  }));

  it('resets the form, and looks for a running job again, when the entry keys change', fakeAsync(() => {
    openPanel();
    chooseWriter(7);
    q<HTMLInputElement>(`#rp-audience-${InternalBrief}`)!.click();
    fixture.detectChanges();
    tick(REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    http.expectOne(PREVIEW_URL).flush(previewDto());

    fixture.componentRef.setInput('context', {
      ...CONTEXT,
      runIds: [2],
      entries: ENTRIES.slice(1, 2).concat(ENTRIES.slice(3)),
      entryKeys: ['run:2', 'group:4']
    });
    fixture.detectChanges();
    answerActiveJob(jobDto({ id: 'job-7', subjectKey: 'run:2', subjectLabel: 'Claude Opus' }));

    expect(component.subjectKey).toBe('run:2');
    expect(component.selectedAudiences).toEqual([ExecutiveSummary, TechnicalReport]);
    expect(component.activeJobId).toBe('job-7');
    expect(host.querySelectorAll('.rp-job-row').length).toBe(2);
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // The estimate
  // -------------------------------------------------------------------------------------------

  it('requests the estimate with the comparison request, the subject, the documents and the writer', fakeAsync(() => {
    openPanel();
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
    openPanel();
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
    openPanel();
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
    openPanel();
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
    openPanel();
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

  it('stops the close and cancel events of its nested dialogs', () => {
    openPanel();

    // The host element stands in for the wizard's dialog around the panel.
    const heard: string[] = [];
    host.addEventListener('close', () => heard.push('close'));
    host.addEventListener('cancel', () => heard.push('cancel'));

    const dialogs = Array.from(host.querySelectorAll('dialog'));
    expect(dialogs.length).toBeGreaterThanOrEqual(2);
    for (const dialog of dialogs) {
      // A real close event does not bubble; a bubbling one proves the handlers stop it.
      dialog.dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
      dialog.dispatchEvent(new Event('close', { bubbles: true }));
    }
    expect(heard).toEqual([]);
  });

  // -------------------------------------------------------------------------------------------
  // 409 and 429
  // -------------------------------------------------------------------------------------------

  it('shows a 409 running job in an alert and follows that job', fakeAsync(() => {
    openPanel();
    const finished: BenchmarkReportPackJobDto[] = [];
    component.jobFinished.subscribe(job => finished.push(job));
    chooseWriter(7);
    generateButton().click();
    const running = jobDto({ id: 'job-9', subjectLabel: 'GPT Sol group' });
    http.expectOne(START_URL).flush(running, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(q('.rp-start-error')!.getAttribute('role')).toBe('alert');
    expect(text('.rp-start-error')).toContain('Another report pack is being written, for GPT Sol group');
    expect(text('.rp-start-error')).toContain('its progress is shown here.');
    expect(host.querySelectorAll('.rp-job-row').length).toBe(2);
    expect(generateButton().disabled).toBeTrue();

    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-9')).flush(jobDto({ id: 'job-9', status: 'Completed', completedAtUtc: '2026-09-28T10:01:00Z' }));
    fixture.detectChanges();
    expect(finished.map(job => job.id)).toEqual(['job-9']);
    fixture.destroy();
  }));

  it('shows a 429 spend-cap refusal with the server\'s message', fakeAsync(() => {
    openPanel();
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
    openPanel();

    expect(q('.rp-job')).toBeNull();
    const line = q('.rp-job-status')!;
    expect(line.getAttribute('role')).toBe('status');
    expect(line.textContent!.trim()).toBe('');
    expect(text('.rp-idle-note')).toContain('step 4, Documents');
    expect(component.jobRunning).toBeFalse();
  });

  it('polls a started job with a stage rail, a stat strip and a row per document, then summarizes it above the strip', fakeAsync(() => {
    openPanel();
    const busy: boolean[] = [];
    const finished: BenchmarkReportPackJobDto[] = [];
    let documentsRequests = 0;
    component.busyChange.subscribe(value => busy.push(value));
    component.jobFinished.subscribe(job => finished.push(job));
    component.documentsRequested.subscribe(() => documentsRequests++);
    chooseWriter(7);
    generateButton().click();
    http.expectOne(START_URL).flush({ jobId: 'job-1' });
    fixture.detectChanges();
    // Before the first reading, the rail stands at Queued.
    expect(text('.rp-job-rail .run-stage.is-current .run-stage-name')).toBe('Queued');
    expect(component.jobRunning).toBeTrue();

    http.expectOne(jobUrl('job-1')).flush(jobDto());
    fixture.detectChanges();

    expect(cellText(ExecutiveSummary, '.rp-doc-name')).toBe('Executive Summary');
    expect(cellText(ExecutiveSummary, '.job-status-chip')).toBe('Writing');
    expect(cellText(ExecutiveSummary, '.rp-doc-calls')).toBe('Model calls: 1');
    expect(cellText(TechnicalReport, '.job-status-chip')).toBe('Pending');
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
    expect(cellText(ExecutiveSummary, '.job-status-chip')).toBe('Completed');
    expect(cellText(TechnicalReport, '.job-status-chip')).toBe('Repairing');
    expect(cellText(TechnicalReport, '.rp-doc-calls')).toBe('Model calls: 2');
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
    fixture.detectChanges();

    expect(text('.rp-job-status')).toBe('Report pack for Gemini Flash: Completed.');
    expect(text('.rp-job-summary')).toBe('Written: 2 documents, 2 min 05 s, $0.13');
    expect(q('.rp-job-rail')).toBeNull();
    // The strip stays, with Cost and the estimate, under the summary line and Dismiss.
    expect(q('.rp-job-stats')).not.toBeNull();
    expect(text('.rp-job-cost-label')).toBe('Cost');
    expect(text('.rp-job-cost')).toBe('$0.13');
    expect(text('.rp-job-estimate')).toBe('about $0.12');
    expect(q('.rp-job-summary-row')!.compareDocumentPosition(q('.rp-job-stats')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(q('.rp-cancel-job')).toBeNull();
    expect(finished.map(job => job.id)).toEqual(['job-1']);
    expect(busy).toEqual([true, false]);
    expect(component.jobRunning).toBeFalse();
    tick(REPORT_PACK_POLL_MS * 3);
    http.expectNone(jobUrl('job-1'));

    // See the documents: the host shows step 4.
    const see = q<HTMLButtonElement>('.rp-see-documents')!;
    expect(see.textContent!.trim()).toBe('See the documents');
    see.click();
    expect(documentsRequests).toBe(1);

    // Dismiss removes the card; the live line empties with it.
    q<HTMLButtonElement>('.rp-dismiss-job')!.click();
    fixture.detectChanges();
    expect(q('.rp-job')).toBeNull();
    expect(text('.rp-job-status')).toBe('');
    expect(document.activeElement).toBe(q('#rp-progress-heading'));
    fixture.destroy();
  }));

  it('cancels the running job, then reads its final state at once', fakeAsync(() => {
    openPanel();
    startJob();

    q<HTMLButtonElement>('.rp-cancel-job')!.click();
    fixture.detectChanges();
    const cancel = http.expectOne(`${jobUrl('job-1')}/cancel`);
    expect(cancel.request.method).toBe('POST');
    cancel.flush({ cancelled: true });
    http.expectOne(jobUrl('job-1')).flush(jobDto({ status: 'Canceled', completedAtUtc: '2026-09-28T10:00:30Z' }));
    fixture.detectChanges();

    expect(text('.rp-job-status')).toBe('Report pack for Gemini Flash: Canceled.');
    expect(text('.rp-job-summary')).toBe('Canceled: 0 of 2 documents written, 30 s, Unknown');
    expect(q('.rp-cancel-job')).toBeNull();
    expect(generateButton().disabled).toBeFalse();
    fixture.destroy();
  }));

  it('picks up a running job on creation and backs off when its progress cannot be read', fakeAsync(() => {
    openPanel({ activeJob: jobDto({ id: 'job-5' }) });

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
    fixture.detectChanges();
    expect(text('.rp-job-summary')).toContain('Failed: 0 of 2 documents written');
    fixture.destroy();
  }));

  it('announces each written document once, those already written when it reattaches included', fakeAsync(() => {
    const announced: BenchmarkReportPackDocumentProgressDto[] = [];
    component.documentWritten.subscribe(doc => announced.push(doc));
    openPanel({
      activeJob: jobDto({
        id: 'job-5',
        documents: [written(ExecutiveSummary, 31), { audience: TechnicalReport, status: 'Writing', documentId: null, errorMessage: null, modelCalls: 1 }]
      })
    });
    expect(announced.map(doc => doc.documentId)).toEqual([31]);

    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush(jobDto({
      id: 'job-5',
      documents: [written(ExecutiveSummary, 31), { audience: TechnicalReport, status: 'Failed', documentId: null, errorMessage: 'Refused.', modelCalls: 2 }]
    }));
    expect(announced.map(doc => doc.documentId)).toEqual([31]);

    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush(jobDto({
      id: 'job-5',
      status: 'CompletedWithErrors',
      completedAtUtc: '2026-09-28T10:03:00Z',
      documents: [written(ExecutiveSummary, 31), written(TechnicalReport, 32, 'CompletedWithWarnings')]
    }));
    expect(announced.map(doc => doc.documentId)).toEqual([31, 32]);
    expect(announced[1].status).toBe('CompletedWithWarnings');

    // A comparison change that finds the same job again announces nothing twice.
    fixture.componentRef.setInput('context', { ...CONTEXT, entryKeys: ['run:1', 'run:2'] });
    fixture.detectChanges();
    answerActiveJob(jobDto({ id: 'job-5', status: 'CompletedWithErrors', documents: [written(ExecutiveSummary, 31), written(TechnicalReport, 32)] }));
    expect(announced.map(doc => doc.documentId)).toEqual([31, 32]);
    fixture.destroy();
  }));

  it('keeps polling and ticking while hidden, as when another step of the wizard shows', fakeAsync(() => {
    openPanel({ activeJob: jobDto({ id: 'job-5' }) });
    host.hidden = true;

    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush(jobDto({ id: 'job-5' }));
    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush(jobDto({ id: 'job-5' }));
    expect(component['tickSub']).not.toBeNull();
    fixture.destroy();
  }));

  it('stops polling and its clock when destroyed', fakeAsync(() => {
    openPanel({ activeJob: jobDto({ id: 'job-5' }) });
    expect(component['tickSub']).not.toBeNull();

    fixture.destroy();
    expect(component['tickSub']).toBeNull();
    tick(REPORT_PACK_POLL_MS * 10);
    http.expectNone(jobUrl('job-5'));
  }));

  // -------------------------------------------------------------------------------------------
  // The document progress list (v1 Task 1)
  // -------------------------------------------------------------------------------------------

  it('lists each document with a chip, a live duration and centered model calls, under an aria-hidden header', fakeAsync(() => {
    openPanel({
      activeJob: jobDto({
        id: 'job-5',
        serverTimeUtc: '2026-09-28T10:01:00Z',
        documents: [
          { audience: ExecutiveSummary, status: 'Writing', documentId: null, errorMessage: null, modelCalls: 1, startedAtUtc: '2026-09-28T10:00:30Z' },
          { audience: TechnicalReport, status: 'Failed', documentId: null, errorMessage: 'The writer refused.', modelCalls: 2 }
        ]
      })
    });

    const list = q('ol.rp-doc-progress')!;
    expect(list.getAttribute('aria-label')).toBe('Progress of each document');
    const head = q('.rp-doc-progress-head')!;
    expect(head.getAttribute('aria-hidden')).toBe('true');
    expect(Array.from(head.children).map(cell => cell.textContent!.trim()))
      .toEqual(['Document', 'Status', 'Duration', 'Model calls', 'Charts']);
    expect(list.querySelectorAll('li.rp-job-row').length).toBe(2);

    const chip = q(`.rp-job-row[data-audience="${ExecutiveSummary}"] .job-status-chip`)!;
    expect(chip.classList).toContain('status-generating');
    expect(q(`.rp-job-row[data-audience="${TechnicalReport}"] .job-status-chip`)!.classList).toContain('status-failed');
    expect(cellText(TechnicalReport, '.rp-job-row-error')).toBe('The writer refused.');
    expect(cellText(ExecutiveSummary, '.rp-doc-status')).toBe('Status: Writing');
    expect(q(`.rp-job-row[data-audience="${ExecutiveSummary}"] .rp-doc-calls`)!.classList).toContain('rp-num');

    // On the server's clock: 30 s at its last reading, one more second later.
    expect(cellText(ExecutiveSummary, '.rp-doc-duration')).toBe('Duration: 30 s');
    expect(cellText(TechnicalReport, '.rp-doc-duration')).toBe('Duration: —');
    tick(1000);
    fixture.detectChanges();
    expect(cellText(ExecutiveSummary, '.rp-doc-duration')).toBe('Duration: 31 s');
    fixture.destroy();
  }));

  it('shows each written document\'s chart state, with a retry after a failure', fakeAsync(() => {
    const retries: BenchmarkReportPackDocumentProgressDto[] = [];
    component.chartRetryRequested.subscribe(doc => retries.push(doc));
    const statuses: Record<number, ReportChartRowStatus> = {
      31: { state: 'attaching' },
      32: { state: 'done', count: 3 },
      33: { state: 'failed', message: 'Disk full.' }
    };
    fixture.componentRef.setInput('chartStatus', statuses);
    openPanel({
      activeJob: jobDto({
        id: 'job-5',
        documents: [written(ExecutiveSummary, 31), written(TechnicalReport, 32), written(InternalBrief, 33)]
      })
    });

    expect(cellText(ExecutiveSummary, '.rp-doc-charts')).toBe('Charts: attaching…');
    expect(cellText(TechnicalReport, '.rp-doc-charts')).toBe('Charts: 3');
    const retry = q<HTMLButtonElement>(`.rp-job-row[data-audience="${InternalBrief}"] .rp-chart-retry`)!;
    expect(retry.textContent!.replace(/\s+/g, ' ').trim()).toBe('Charts failed — retry for the Internal Improvement Brief');
    retry.click();
    expect(retries.map(doc => doc.documentId)).toEqual([33]);

    // No chart selected for a document type: none.
    fixture.componentRef.setInput('chartSelection', { ...DEFAULT_CHART_SELECTION, [TechnicalReport]: [] } as ReportChartSelection);
    fixture.detectChanges();
    expect(cellText(TechnicalReport, '.rp-doc-charts')).toBe('Charts: none');

    // No chart storage: none everywhere.
    fixture.componentRef.setInput('chartStorageMissing', true);
    fixture.detectChanges();
    expect(cellText(ExecutiveSummary, '.rp-doc-charts')).toBe('Charts: none');
    expect(q('.rp-chart-retry')).toBeNull();
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // Diagnostics
  // -------------------------------------------------------------------------------------------

  it('copies the diagnostics from an icon-only button with a tooltip, and says so', fakeAsync(() => {
    const copy = spyOn(reportPackIo, 'copy').and.returnValue(Promise.resolve(true));
    openPanel({ activeJob: jobDto({ id: 'job-5' }) });

    expect(text('.rp-job-log > summary')).toBe('Log and diagnostics');
    const button = q<HTMLButtonElement>('.rp-copy-diagnostics')!;
    expect(button.classList).toContain('action-btn');
    expect(button.getAttribute('aria-label')).toBe('Copy the report pack diagnostics for Gemini Flash');
    expect(button.getAttribute('interestfor')).toBe('rp-copy-diagnostics-tip');
    expect(button.hasAttribute('title')).toBeFalse();
    expect(text('#rp-copy-diagnostics-tip')).toBe('Copy diagnostics');
    expect(q('#rp-copy-diagnostics-tip')!.getAttribute('popover')).toBe('hint');

    button.click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(copy).toHaveBeenCalledTimes(1);
    const copied = copy.calls.mostRecent().args[0];
    expect(copied).toContain('Overseer Report Pack diagnostics');
    expect(copied).toContain('Job id: job-5');
    expect(copied).not.toContain('user-secret-id');
    expect(copied).not.toContain('\r');
    const status = q('.rp-copy-status')!;
    expect(status.getAttribute('role')).toBe('status');
    expect(status.textContent!.trim()).toBe('Copied');
    expect(q('.rp-copy-error')).toBeNull();
    fixture.destroy();
  }));

  it('shows an inline error when the clipboard refuses the diagnostics', fakeAsync(() => {
    spyOn(reportPackIo, 'copy').and.returnValue(Promise.resolve(false));
    openPanel({ activeJob: jobDto({ id: 'job-5' }) });

    q<HTMLButtonElement>('.rp-copy-diagnostics')!.click();
    flushMicrotasks();
    fixture.detectChanges();
    const error = q('.rp-copy-error')!;
    expect(error.classList).toContain('gh-field-error');
    expect(error.textContent).toContain('could not be copied');
    expect(text('.rp-copy-status')).toBe('');
    fixture.destroy();
  }));

  it('downloads the diagnostics as a text file named for the subject', fakeAsync(() => {
    const download = spyOn(reportPackIo, 'download');
    openPanel({ activeJob: jobDto({ id: 'job-5' }) });

    const button = q<HTMLButtonElement>('.rp-download-diagnostics')!;
    expect(button.getAttribute('aria-label')).toBe('Download the report pack diagnostics for Gemini Flash');
    expect(text('#rp-download-diagnostics-tip')).toBe('Download diagnostics');
    button.click();

    expect(download).toHaveBeenCalledTimes(1);
    const [fileName, body] = download.calls.mostRecent().args;
    expect(fileName).toMatch(/^report-pack_gemini-flash_diagnostics_\d{8}-\d{6}\.txt$/);
    expect(body).toContain('== Documents ==');
    expect(body).toContain('Polls: ');
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // The clock (v1 Task 3)
  // -------------------------------------------------------------------------------------------

  it('measures elapsed time on the server\'s clock, ticks each second while running, and keeps the live line still', fakeAsync(() => {
    openPanel({ activeJob: jobDto({ id: 'job-5', serverTimeUtc: '2026-09-28T10:01:00Z' }) });

    expect(text('.rp-job-elapsed')).toBe('1 min 00 s');
    const line = text('.rp-job-status');
    tick(1000);
    fixture.detectChanges();
    expect(text('.rp-job-elapsed')).toBe('1 min 01 s');
    expect(text('.rp-job-status')).toBe(line);

    // A new reading resets the clock to the server's.
    tick(REPORT_PACK_POLL_MS - 1000);
    http.expectOne(jobUrl('job-5')).flush(jobDto({ id: 'job-5', serverTimeUtc: '2026-09-28T10:01:30Z' }));
    fixture.detectChanges();
    expect(text('.rp-job-elapsed')).toBe('1 min 30 s');

    // Finished: the clock stops.
    tick(REPORT_PACK_POLL_MS);
    http.expectOne(jobUrl('job-5')).flush(jobDto({
      id: 'job-5', status: 'Completed', completedAtUtc: '2026-09-28T10:02:00Z', serverTimeUtc: '2026-09-28T10:02:01Z'
    }));
    fixture.detectChanges();
    expect(component['tickSub']).toBeNull();
    expect(text('.rp-job-elapsed')).toBe('2 min 00 s');
    fixture.destroy();
  }));

  it('falls back to the client\'s clock when the server sends no time of its own', fakeAsync(() => {
    const started = new Date(Date.now() - 42000).toISOString();
    openPanel({ activeJob: jobDto({ id: 'job-5', startedAtUtc: started }) });

    expect(text('.rp-job-elapsed')).toBe('42 s');
    tick(1000);
    fixture.detectChanges();
    expect(text('.rp-job-elapsed')).toBe('43 s');
    fixture.destroy();
  }));

  // -------------------------------------------------------------------------------------------
  // Charts in PDF and Word
  // -------------------------------------------------------------------------------------------

  it('holds the chart picker between Documents and Report writer, its segments following the documents checked', () => {
    openPanel();
    const selections: ReportChartSelection[] = [];
    component.chartSelectionChange.subscribe(selection => selections.push(selection));

    const fieldset = q('fieldset.rp-charts-choice')!;
    expect(fieldset.querySelector('legend')!.textContent!.trim()).toBe('Charts in PDF and Word');
    const picker = fixture.debugElement.query(By.directive(ReportChartPickerComponent)).componentInstance as ReportChartPickerComponent;
    expect(picker.enabledAudiences).toEqual([ExecutiveSummary, TechnicalReport]);
    expect(picker.available).toEqual(REPORT_CHART_FIGURES.map(figure => figure.key));
    expect(picker.idPrefix).toBe('rp-charts');
    q<HTMLButtonElement>(`#rp-charts-tab-${InternalBrief}`)!.click();
    fixture.detectChanges();
    expect(q(`#rp-charts-${InternalBrief}-p1a-quality`)!.getAttribute('aria-disabled')).toBe('true');

    q<HTMLInputElement>(`#rp-audience-${InternalBrief}`)!.click();
    fixture.detectChanges();
    expect(picker.enabledAudiences).toEqual([ExecutiveSummary, TechnicalReport, InternalBrief]);
    expect(q(`#rp-charts-${InternalBrief}-p1a-quality`)!.hasAttribute('aria-disabled')).toBeFalse();

    q<HTMLButtonElement>(`#rp-charts-tab-${ExecutiveSummary}`)!.click();
    fixture.detectChanges();
    q<HTMLInputElement>(`#rp-charts-${ExecutiveSummary}-p1b-speed`)!.click();
    expect(selections.length).toBe(1);
    expect(selections[0][ExecutiveSummary]).toEqual(['p1a-quality', 'p1b-speed', 's2-quality-cost']);

    expect(q('.rp-chart-advisory')).toBeNull();
    expect(q('.rp-chart-storage-missing')).toBeNull();
  });

  it('shows the chart advisory and the missing chart storage as visible warnings', () => {
    fixture.componentRef.setInput('chartAdvisory', 'The charts use today\'s prices; the documents were written at run-time prices.');
    fixture.componentRef.setInput('chartStorageMissing', true);
    openPanel();

    const advisory = q('.rp-chart-advisory')!;
    expect(advisory.classList).toContain('alert-warning');
    expect(advisory.textContent).toContain('The charts use today\'s prices');
    const missing = q('.rp-chart-storage-missing')!;
    expect(missing.classList).toContain('alert-warning');
    expect(missing.textContent!.trim()).toBe('Chart storage is not configured; documents will be written without charts.');
    expect(q('fieldset.rp-charts-choice')!.contains(missing)).toBeTrue();
  });

  it('lists a figure the comparison cannot draw with the reason', () => {
    fixture.componentRef.setInput('chartsAvailable', REPORT_CHART_FIGURES.map(figure => figure.key).filter(key => key !== 'p2-profile'));
    openPanel();
    q<HTMLButtonElement>(`#rp-charts-tab-${TechnicalReport}`)!.click();
    fixture.detectChanges();

    expect(q(`#rp-charts-${TechnicalReport}-p2-profile`)!.getAttribute('aria-disabled')).toBe('true');
    expect(text('#rp-charts-row-p2-profile-reason')).toBe('needs three or more models');
  });

  // -------------------------------------------------------------------------------------------
  // The grader guide
  // -------------------------------------------------------------------------------------------

  it('asks for the grader guide from the writer field\'s link, and explains the writer per document in a dialog', () => {
    openPanel();
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
});
