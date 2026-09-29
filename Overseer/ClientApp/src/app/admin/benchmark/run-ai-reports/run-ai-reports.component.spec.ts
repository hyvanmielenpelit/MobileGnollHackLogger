import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, flush, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Subject, of, throwError } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkRunReportEstimateDto,
  BenchmarkRunReportJobDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { toModelPickerOptions } from '../../../shared/model-picker/model-picker.component';
import { PdfViewerDialogComponent, PdfViewerRequest } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { PDFJS_LOADER } from '../../../shared/pdf-viewer/pdfjs-loader';
import { rememberedPdfPaper } from '../download-center/benchmark-download-center.component';
import { reportDisclosureInfo } from '../report-disclosure-guide';
import {
  RUN_REPORT_DOCUMENTS_POLL_MS,
  RUN_REPORT_ESTIMATE_DEBOUNCE_MS,
  RunAiReportsComponent,
  RunReportStatusChange
} from './run-ai-reports.component';
import { RunReportWritingContext, RunReportWritingDialogComponent } from './run-report-writing-dialog.component';

function config(id: number, displayName: string, provider: string, modelId: string): SystemAiConfigDto {
  return {
    id, displayName, provider, modelId, thinkingLevel: null, reasoningMode: null, reasoningSummary: null,
    serviceTier: null, orderIndex: id, isEnabled: true, hasApiKey: true, modelRole: 7
  } as unknown as SystemAiConfigDto;
}

/** The candidate is OpenAI's gpt-test; 1 and 8 are other providers, 9 shares it, 10 is the candidate's own model. */
const CONFIGS: SystemAiConfigDto[] = [
  config(1, 'Test Model', 'Anthropic', 'claude-3-5-sonnet'),
  config(8, 'Other Writer', 'Google', 'gemini-writer'),
  config(9, 'GPT Writer', 'OpenAI', 'gpt-writer'),
  config(10, 'Candidate Twin', 'OpenAI', 'gpt-test')
];
const OPTIONS = toModelPickerOptions(CONFIGS);

function reportRun(overrides: any = {}): any {
  return {
    id: 55, benchmarkSuiteId: 1, suiteName: 'Default Suite',
    testedModelDisplayNameUsed: 'Test Model', testedModelProviderUsed: 'OpenAI', testedModelIdUsed: 'gpt-test',
    testedModelParallelExecutionModeUsed: 0,
    assessorModelDisplayNameUsed: 'Test Assessor', assessorModelProviderUsed: 'Google', assessorModelIdUsed: 'gemini-test',
    status: 'Completed', startedAtUtc: '2026-09-03T06:52:00Z', completedAtUtc: '2026-09-03T07:10:00Z',
    answers: [],
    ...overrides
  };
}

/** A run-completion document of run 55, written by the Anthropic configuration. */
function aiDoc(id: number, audience: number, overrides: any = {}): any {
  return {
    id, packId: 'run-55', audience, title: `Document ${id}`, subjectKey: 'run:55', subjectLabel: 'Test Model',
    subjectRunIds: [55], suiteId: 1, suiteName: 'Default Suite', writerDisplayName: 'Test Model',
    writerProvider: 'Anthropic', writerModelId: 'claude-3-5-sonnet', writerThinkingLevel: null,
    sameProviderAcknowledged: false, status: 'Completed', reportFormatVersion: 2,
    createdAtUtc: '2026-09-28T10:15:00Z', inputTokens: 0, outputTokens: 0, durationMs: 0, costUsd: null,
    runChangedSinceGeneration: false, missingRunIds: [], allowedDisclosures: [1, 2, 3], origin: 2,
    ...overrides
  };
}

function jobView(status: number, overrides: Partial<BenchmarkRunReportJobDto> = {}): BenchmarkRunReportJobDto {
  return {
    runId: 55, status, message: null, phase: status >= 3 ? 'Finished' : 'Writing',
    queuedAtUtc: '2026-09-29T12:00:00Z', slotAcquiredAtUtc: null, finishedAtUtc: null, cancelRequestedAtUtc: null,
    jobsAhead: null, blockingJobLabel: null, audiences: [1, 2], writerConfigId: 1, writerDisplayName: 'Test Model',
    writerProvider: 'Anthropic', writerModelId: 'claude-3-5-sonnet', writerThinkingLevel: null,
    job: null as any, serverTimeUtc: '2026-09-29T12:00:10Z',
    ...overrides
  };
}

function estimateDto(overrides: Partial<BenchmarkRunReportEstimateDto> = {}): BenchmarkRunReportEstimateDto {
  return {
    estimates: [
      { audience: 1, promptChars: 1000, estimatedInputTokens: 900, estimatedOutputTokens: 2000, estimatedCostUsd: 0.04 },
      { audience: 2, promptChars: 3000, estimatedInputTokens: 2700, estimatedOutputTokens: 7000, estimatedCostUsd: 0.14 }
    ],
    estimatedTotalCostUsd: 0.18,
    refusal: null,
    sameProviderWarning: null,
    ...overrides
  };
}

describe('RunAiReportsComponent', () => {
  let fixture: ComponentFixture<RunAiReportsComponent>;
  let component: RunAiReportsComponent;
  let service: jasmine.SpyObj<AdminBenchmarkService>;
  let writingOpen: jasmine.Spy<(context: RunReportWritingContext) => void>;
  let viewerOpen: jasmine.Spy<(request: PdfViewerRequest) => void>;

  beforeEach(async () => {
    service = jasmine.createSpyObj('AdminBenchmarkService', [
      'listReportDocuments', 'writeRunReportDocuments', 'getReportDocumentPdf', 'reportDocumentPdfUrl',
      'getRunReportJob', 'cancelRunReportJob', 'getRun', 'estimateRunReports', 'deleteRunReportDocument'
    ]);
    service.listReportDocuments.and.returnValue(of([]));
    service.getRunReportJob.and.returnValue(of(null));
    service.getRun.and.returnValue(of(reportRun()));
    service.estimateRunReports.and.returnValue(of(estimateDto()));
    service.deleteRunReportDocument.and.returnValue(of(undefined));
    service.getReportDocumentPdf.and.returnValue(of({ bytes: new Uint8Array([37, 80, 68, 70]), fileName: null }));
    service.reportDocumentPdfUrl.and.callFake((id: number, disclosure: number, peers: number, paper: string, inline?: boolean) =>
      `/pdf/${id}/${disclosure}/${peers}/${paper}/${inline ? 'inline' : 'attachment'}`);

    // Neither hosted dialog really opens: the progress dialog would poll, the viewer would load pdf.js.
    writingOpen = spyOn(RunReportWritingDialogComponent.prototype, 'open');
    viewerOpen = spyOn(PdfViewerDialogComponent.prototype, 'open');

    await TestBed.configureTestingModule({
      imports: [RunAiReportsComponent],
      providers: [
        { provide: AdminBenchmarkService, useValue: service },
        { provide: PDFJS_LOADER, useValue: () => Promise.reject(new Error('pdf.js is not loaded in specs')) },
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();
  });

  afterEach(() => {
    for (const dialog of Array.from(document.querySelectorAll('dialog[open]')) as HTMLDialogElement[]) {
      dialog.close();
    }
  });

  function setUp(launcherWriterConfigId: number | null = null): void {
    fixture = TestBed.createComponent(RunAiReportsComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('writerConfigs', CONFIGS);
    fixture.componentRef.setInput('pickerOptions', OPTIONS);
    fixture.componentRef.setInput('pickerEmptyHint', 'No models.');
    fixture.componentRef.setInput('launcherWriterConfigId', launcherWriterConfigId);
  }

  function load(run: any, dialogOpen = true): void {
    fixture.componentRef.setInput('run', run);
    fixture.componentRef.setInput('dialogOpen', dialogOpen);
    fixture.detectChanges();
  }

  function section(): HTMLElement {
    return fixture.nativeElement.querySelector('.rr-ai-reports') as HTMLElement;
  }

  function rows(): HTMLElement[] {
    return Array.from(section().querySelectorAll('.rr-ai-doc-row')) as HTMLElement[];
  }

  function rowText(row: HTMLElement, selector: string): string | undefined {
    return row.querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim();
  }

  function statusLine(): HTMLElement {
    return section().querySelector('.rr-ai-status[role="status"]') as HTMLElement;
  }

  function status(): string {
    return (statusLine().textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function writeButton(): HTMLButtonElement {
    return section().querySelector('.rr-ai-write-btn') as HTMLButtonElement;
  }

  function checkbox(audience: number): HTMLInputElement {
    return section().querySelector(`#rrAudience${audience}`) as HTMLInputElement;
  }

  function estimateLine(): string {
    return (section().querySelector('.rr-ai-estimate')?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function dialog(selector: string): HTMLDialogElement {
    return fixture.nativeElement.querySelector(selector) as HTMLDialogElement;
  }

  function buttonIn(root: HTMLElement, label: string): HTMLButtonElement {
    return (Array.from(root.querySelectorAll('button')) as HTMLButtonElement[])
      .find(button => (button.textContent ?? '').replace(/\s+/g, ' ').trim() === label)!;
  }

  // --- Moved from the benchmark component's AI-Written Reports specs ---

  it('should be a plain section listing both documents as not written', () => {
    setUp(null);
    load(reportRun({ assessmentJson: '{}' }));

    expect(section().tagName).toBe('DIV');
    // The hosted progress dialog has its own Diagnostics disclosure; the section itself has none.
    expect(section().closest('details')).toBeNull();
    expect(section().querySelector('details summary')).toBeNull();

    expect(service.listReportDocuments).toHaveBeenCalledWith({ runId: 55 });
    expect(status()).toBe('');
    expect(rows().map(row => rowText(row, '.rr-ai-doc-name'))).toEqual(['Executive Summary', 'Report for AI Researchers and Developers']);
    for (const row of rows()) {
      expect(rowText(row, '.rr-ai-doc-status.is-missing')).toBe('Not written');
      expect(row.querySelector('button')).toBeNull();
    }
    expect(section().querySelector('#rrReportWriterModelLabel')?.textContent?.trim()).toBe('Report writer');
    expect(writeButton().disabled).toBeTrue();
    expect(section().querySelector('#rrReportWriterBlocked')).toBeNull();
    expect(section().textContent).not.toContain('Choose a report writer.');
    expect(section().querySelector('.rr-ai-download-notice')).toBeNull();
  });

  it('should show only the names until the document list answers', () => {
    const pending = new Subject<any>();
    service.listReportDocuments.and.returnValue(pending.asObservable());
    setUp();
    load(reportRun({ assessmentJson: '{}' }));

    expect(rows().length).toBe(2);
    expect(section().querySelector('.rr-ai-doc-status')).toBeNull();

    pending.next([aiDoc(71, 1)]);
    fixture.detectChanges();
    expect(rows().map(row => rowText(row, '.rr-ai-doc-status'))).toEqual(['Written', 'Not written']);
  });

  it('should preselect the run\'s own writer over the launcher\'s', () => {
    setUp(8);
    load(reportRun({ assessmentJson: '{}', reportWriterModelConfigurationId: 1 }));

    expect(component.writerConfigId).toBe(1);
    expect(writeButton().disabled).toBeFalse();
  });

  it('should preselect the launcher\'s writer when the run has none and it qualifies', () => {
    setUp(1);
    load(reportRun({ assessmentJson: '{}' }));

    expect(component.writerConfigId).toBe(1);
    expect(writeButton().disabled).toBeFalse();
  });

  it('should leave the writer empty when the launcher\'s writer would be warned about or refused for the run\'s candidate', () => {
    setUp(9);
    load(reportRun({ assessmentJson: '{}' }));
    expect(component.writerConfigId).toBeNull();
    expect(writeButton().disabled).toBeTrue();
    expect(section().querySelector('#rrReportWriterBlocked')).toBeNull();

    setUp(10);
    load(reportRun({ assessmentJson: '{}' }));
    expect(component.writerConfigId).toBeNull();
    expect(section().querySelector('#rrReportWriterBlocked')).toBeNull();
  });

  it('should write the missing reports with the chosen writer, then follow the job until they are written', fakeAsync(() => {
    service.writeRunReportDocuments.and.returnValue(of({ runId: 55, status: 1 }));
    setUp();
    const changes: RunReportStatusChange[] = [];
    load(reportRun({ assessmentJson: '{}', reportWriterModelConfigurationId: 1 }));
    component.reportStatusChange.subscribe(change => changes.push(change));

    expect(writeButton().disabled).toBeFalse();
    writeButton().click();
    fixture.detectChanges();

    expect(service.writeRunReportDocuments).toHaveBeenCalledOnceWith(55, { writerModelConfigurationId: 1, audiences: [1, 2] });
    expect(status()).toBe('Waiting for the report writer');
    expect(writeButton().disabled).toBeTrue();
    expect(section().querySelector('#rrReportWriterBlocked')).toBeNull();
    expect(section().querySelector('.rr-ai-show-progress')).not.toBeNull();
    expect(changes.map(change => change.status)).toEqual([1]);

    service.getRunReportJob.and.returnValue(of(jobView(3)));
    service.listReportDocuments.and.returnValue(of([
      aiDoc(71, 1),
      aiDoc(72, 2),
      // A Report Pack document about the same run is not one of its AI-written reports.
      aiDoc(73, 2, { origin: 1, subjectKey: 'group:4' })
    ]));
    tick(RUN_REPORT_DOCUMENTS_POLL_MS);
    fixture.detectChanges();

    expect(status()).toBe('');
    expect(rows().map(row => rowText(row, '.rr-ai-doc-meta')))
      .toEqual(['by Test Model on 2026-09-28 10:15 UTC', 'by Test Model on 2026-09-28 10:15 UTC']);
    expect(section().querySelectorAll('.rr-ai-doc-view').length).toBe(2);
    expect(section().querySelector('.rr-ai-write')).toBeNull();
    expect((component as any).pollSub).toBeNull();
    expect(changes.map(change => change.status)).toEqual([1, 3]);
    flush();
    discardPeriodicTasks();
  }));

  it('should list stored reports with their writer and date, each with a View and a Delete button named for it', () => {
    service.listReportDocuments.and.returnValue(of([
      aiDoc(72, 2, {
        runChangedSinceGeneration: true, createdAtUtc: '2026-09-28T11:00:00Z', writerDisplayName: 'Writer B',
        status: 'CompletedWithWarnings', durationMs: 72000, costUsd: 0.08, sameProviderAcknowledged: true
      }),
      aiDoc(71, 1)
    ]));
    setUp();
    load(reportRun({ assessmentJson: '{}', reportDocumentsStatus: 4, reportWriterDisplayName: 'Writer B' }));

    expect(status()).toBe('');
    const list = rows();
    expect(list.map(row => rowText(row, '.rr-ai-doc-name'))).toEqual(['Executive Summary', 'Report for AI Researchers and Developers']);
    expect(list.map(row => rowText(row, '.rr-ai-doc-status'))).toEqual(['Written', 'Written with warnings']);
    expect(list[1].querySelector('.rr-ai-doc-status')?.classList).toContain('is-warning');
    expect(list.map(row => rowText(row, '.rr-ai-doc-meta'))).toEqual([
      'by Test Model on 2026-09-28 10:15 UTC',
      'by Writer B on 2026-09-28 11:00 UTC · 1 min 12 s · $0.08 · same provider, acknowledged'
    ]);
    const views = list.map(row => row.querySelector('button.rr-ai-doc-view') as HTMLButtonElement);
    expect(views.map(button => button.getAttribute('aria-label')))
      .toEqual(['View the Executive Summary', 'View the Report for AI Researchers and Developers']);
    for (const button of views) {
      expect(button.classList).toContain('btn-gh');
      expect(button.classList).toContain('btn-gh-small');
    }
    const deletes = list.map(row => row.querySelector('button.rr-ai-doc-delete') as HTMLButtonElement);
    expect(deletes.map(button => button.getAttribute('aria-label')))
      .toEqual(['Delete the Executive Summary', 'Delete the Report for AI Researchers and Developers']);
    for (const button of deletes) {
      expect(button.classList).toContain('action-btn');
      expect(button.classList).toContain('action-btn-danger');
      expect(button.hasAttribute('title')).toBeFalse();
      expect(button.hasAttribute('aria-disabled')).toBeFalse();
      const tip = fixture.nativeElement.querySelector('#' + button.getAttribute('interestfor')) as HTMLElement;
      expect(tip.getAttribute('popover')).toBe('hint');
      expect(tip.textContent?.trim()).toBe('Delete');
      expect(button.getAttribute('style')).toContain('anchor-name: --' + button.getAttribute('interestfor'));
      expect(tip.getAttribute('style')).toContain('position-anchor: --' + button.getAttribute('interestfor'));
    }
    expect(list[0].querySelector('.gh-tag-changed')).toBeNull();
    expect(list[1].querySelector('.gh-tag-changed')?.textContent?.trim()).toBe('Run changed since this document was written');
    expect(section().querySelector('.rr-ai-write')).toBeNull();
    expect(section().querySelector('.rr-ai-download-notice')).not.toBeNull();
  });

  it('should offer Write Reports for the one report that is missing', () => {
    service.listReportDocuments.and.returnValue(of([aiDoc(71, 1)]));
    setUp();
    load(reportRun({ assessmentJson: '{}', reportDocumentsStatus: 5, reportDocumentsMessage: 'The writer returned no usable text.' }));

    expect(status()).toBe('Failed: The writer returned no usable text.');
    const list = rows();
    expect(list.map(row => rowText(row, '.rr-ai-doc-status'))).toEqual(['Written', 'Not written']);
    expect(list[0].querySelector('.rr-ai-doc-view')).not.toBeNull();
    expect(list[1].querySelector('button')).toBeNull();
    const write = section().querySelector('.rr-ai-write') as HTMLElement;
    expect(write).not.toBeNull();
    expect(section().querySelector('.rr-ai-doc-list')!.compareDocumentPosition(write) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(checkbox(1)).toBeNull();
    expect(checkbox(2).checked).toBeTrue();
    expect(write.querySelector('.rr-ai-written-hint')?.textContent?.trim())
      .toBe('Executive Summary is already written. Delete it to write it again.');
  });

  it('should open a report in the PDF viewer at its fullest allowed disclosure with peers named', () => {
    service.listReportDocuments.and.returnValue(of([aiDoc(71, 1, { allowedDisclosures: [2, 1] })]));
    const windowOpen = spyOn(window, 'open');
    setUp();
    load(reportRun({ assessmentJson: '{}' }));

    (section().querySelector('button.rr-ai-doc-view') as HTMLButtonElement).click();

    expect(windowOpen).not.toHaveBeenCalled();
    expect(viewerOpen).toHaveBeenCalledTimes(1);
    const request = viewerOpen.calls.mostRecent().args[0];
    const paper = rememberedPdfPaper();
    expect(request.title).toBe('Executive Summary');
    expect(request.subtitle).toBe('Run #55 · by Test Model on 2026-09-28 10:15 UTC');
    expect(request.variants).toEqual([{ key: 'summary', label: 'Summary' }, { key: 'detailed', label: 'Detailed' }]);
    expect(request.initialVariant).toBe('detailed');
    expect(request.fallbackFileName).toBe('run-55_executive-summary.pdf');

    request.load('detailed').subscribe();
    expect(service.getReportDocumentPdf.calls.mostRecent().args).toEqual([71, 2, 1, paper]);
    request.load('summary').subscribe();
    expect(service.getReportDocumentPdf.calls.mostRecent().args).toEqual([71, 1, 1, paper]);
    request.load(null).subscribe();
    expect(service.getReportDocumentPdf.calls.mostRecent().args).toEqual([71, 2, 1, paper]);
    expect(request.tabUrl!('summary')).toBe(`/pdf/71/1/1/${paper}/inline`);
    expect(service.reportDocumentPdfUrl.calls.mostRecent().args).toEqual([71, 1, 1, paper, true]);
    expect(request.variantsInfo).toEqual(reportDisclosureInfo(1));
    expect(request.variantsInfo?.title).toBe('What Summary, Detailed and Full mean');
  });

  it('should refuse the model under test as its own writer inline, in red, joined to the picker\'s description', () => {
    setUp();
    load(reportRun({ assessmentJson: '{}', reportWriterModelConfigurationId: 10 }));

    const blocked = section().querySelector('#rrReportWriterBlocked') as HTMLElement;
    expect(blocked.textContent?.trim()).toBe('The model under test cannot write its own reports.');
    expect(blocked.classList).toContain('gh-field-error');
    expect(blocked.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(writeButton().disabled).toBeTrue();
    expect(section().querySelector('.rr-report-writer-model-selector .selector-trigger')?.getAttribute('aria-describedby'))
      .toBe('rrReportWriterBlocked');
    expect(section().querySelector('.rr-ai-writer-warning')).toBeNull();
  });

  it('should show the server\'s refusal of Write Reports inline', () => {
    service.writeRunReportDocuments.and.returnValue(throwError(() => ({
      status: 400, error: { error: 'The model under test cannot write its own reports.' }
    })));
    setUp();
    load(reportRun({ assessmentJson: '{}', reportWriterModelConfigurationId: 1 }));

    writeButton().click();
    fixture.detectChanges();

    const alert = section().querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent?.trim()).toBe('The model under test cannot write its own reports.');
    expect(alert.classList).toContain('alert-danger');
    expect(alert.querySelector('svg.alert-icon')).not.toBeNull();
    expect(status()).toBe('');
    expect(writeButton().disabled).toBeFalse();
    expect(writingOpen).not.toHaveBeenCalled();
  });

  it('should poll a finished run\'s reports while they are written, and stop when the report dialog closes', () => {
    setUp();
    load(reportRun({
      assessmentJson: '{}', reportWriterModelConfigurationId: 1, reportWriterDisplayName: 'Test Model', reportDocumentsStatus: 2
    }));

    expect(status()).toBe('Writing…');
    expect((component as any).pollSub).not.toBeNull();

    fixture.componentRef.setInput('dialogOpen', false);
    fixture.detectChanges();

    expect((component as any).pollSub).toBeNull();
  });

  // --- Added with the component ---

  it('should poll the job, fall back to the run when the server knows no job, and list the documents only on a change', fakeAsync(() => {
    setUp();
    load(reportRun({ assessmentJson: '{}', reportDocumentsStatus: 2 }));
    expect(service.listReportDocuments).toHaveBeenCalledTimes(1);

    service.getRunReportJob.and.returnValue(of(jobView(2)));
    tick(RUN_REPORT_DOCUMENTS_POLL_MS);
    expect(service.getRunReportJob).toHaveBeenCalledWith(55);
    expect(service.listReportDocuments).toHaveBeenCalledTimes(1);

    service.getRunReportJob.and.returnValue(of(null));
    service.getRun.and.returnValue(of(reportRun({ reportDocumentsStatus: 6, reportDocumentsMessage: 'No writer.' })));
    tick(RUN_REPORT_DOCUMENTS_POLL_MS);
    fixture.detectChanges();
    expect(service.getRun).toHaveBeenCalledWith(55);
    expect(service.listReportDocuments).toHaveBeenCalledTimes(2);
    expect(status()).toBe('Skipped: No writer.');
    expect((component as any).pollSub).toBeNull();
    flush();
    discardPeriodicTasks();
  }));

  it('should list the documents again when the run it shows finishes', () => {
    setUp();
    load(reportRun({ status: 'Running', reportWriterModelConfigurationId: 1, reportWriterDisplayName: 'Test Model' }));
    expect(service.listReportDocuments).toHaveBeenCalledTimes(1);
    expect(status()).toBe('Not written yet: Test Model writes them once the run is scored');

    load(reportRun({ status: 'Completed', reportWriterModelConfigurationId: 1 }));
    expect(service.listReportDocuments).toHaveBeenCalledTimes(2);
  });

  it('should draw Failed in red and Skipped in amber, each with its icon, and word Canceled', () => {
    setUp();
    load(reportRun({ reportDocumentsStatus: 5, reportDocumentsMessage: 'Timed out.' }));
    expect(status()).toBe('Failed: Timed out.');
    expect(statusLine().classList).toContain('gh-field-error');
    expect(statusLine().querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');

    load(reportRun({ id: 56, reportDocumentsStatus: 6, reportDocumentsMessage: 'No writer.' }));
    expect(status()).toBe('Skipped: No writer.');
    expect(statusLine().classList).toContain('rr-ai-status-skipped');
    expect(statusLine().classList).not.toContain('gh-field-error');
    expect(statusLine().querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');

    load(reportRun({ id: 57, reportDocumentsStatus: 7, reportDocumentsMessage: null }));
    expect(status()).toBe('Canceled');
    expect(statusLine().querySelector('svg')).toBeNull();
    load(reportRun({ id: 58, reportDocumentsStatus: 7, reportDocumentsMessage: 'Overseer stopped' }));
    expect(status()).toBe('Canceled: Overseer stopped');
    load(reportRun({ id: 59, reportDocumentsStatus: 7, reportDocumentsMessage: 'Canceled before the writing began.' }));
    expect(status()).toBe('Canceled before the writing began.');
    // Older servers send the status by name.
    load(reportRun({ id: 60, reportDocumentsStatus: 'Failed', reportDocumentsMessage: 'Boom.' }));
    expect(status()).toBe('Failed: Boom.');
  });

  it('should check every missing document, send the checked ones, and name the button by their count', () => {
    service.writeRunReportDocuments.and.returnValue(of({ runId: 55, status: 1 }));
    setUp();
    load(reportRun({ assessmentJson: '{}', reportWriterModelConfigurationId: 1 }));

    const legend = section().querySelector('.rr-ai-write > legend')?.textContent?.trim();
    expect(legend).toBe('Write missing reports');
    expect(section().querySelector('.rr-ai-audiences > legend')?.textContent?.trim()).toBe('Documents');
    expect(checkbox(1).checked).toBeTrue();
    expect(checkbox(2).checked).toBeTrue();
    expect(checkbox(1).closest('label')?.classList).toContain('checkbox-label');
    expect(writeButton().textContent?.trim()).toBe('Write Reports');
    expect(writeButton().querySelector('svg.btn-icon')?.getAttribute('aria-hidden')).toBe('true');

    checkbox(1).click();
    fixture.detectChanges();
    expect(writeButton().textContent?.trim()).toBe('Write Report');
    expect(writeButton().disabled).toBeFalse();

    checkbox(2).click();
    fixture.detectChanges();
    expect(writeButton().disabled).toBeTrue();

    checkbox(2).click();
    fixture.detectChanges();
    writeButton().click();
    expect(service.writeRunReportDocuments).toHaveBeenCalledOnceWith(55, { writerModelConfigurationId: 1, audiences: [2] });
  });

  it('should keep the document choice across a poll', fakeAsync(() => {
    setUp();
    load(reportRun({ assessmentJson: '{}', reportWriterModelConfigurationId: 1, reportDocumentsStatus: 2 }));
    checkbox(1).click();
    fixture.detectChanges();

    service.getRunReportJob.and.returnValue(of(jobView(5, { message: 'Timed out.' })));
    tick(RUN_REPORT_DOCUMENTS_POLL_MS);
    fixture.detectChanges();

    expect(service.listReportDocuments).toHaveBeenCalledTimes(2);
    expect(checkbox(1).checked).toBeFalse();
    expect(checkbox(2).checked).toBeTrue();
    flush();
    discardPeriodicTasks();
  }));

  it('should estimate the cost once the choice rests, saying so while it waits', fakeAsync(() => {
    const pending = new Subject<BenchmarkRunReportEstimateDto>();
    service.estimateRunReports.and.returnValue(pending.asObservable());
    setUp();
    load(reportRun({ assessmentJson: '{}', reportWriterModelConfigurationId: 1 }));

    expect(estimateLine()).toBe('Estimating…');
    const block = section().querySelector('#rrWriteEstimate') as HTMLElement;
    expect(block.classList).toContain('rr-ai-estimate');
    expect(block.getAttribute('role')).toBe('status');
    expect(block.getAttribute('aria-busy')).toBe('true');
    expect(block.classList).toContain('is-muted');
    expect(writeButton().getAttribute('aria-describedby')).toBe('rrWriteEstimate');
    tick(RUN_REPORT_ESTIMATE_DEBOUNCE_MS - 1);
    expect(service.estimateRunReports).not.toHaveBeenCalled();

    // A change inside the pause starts it again: one request follows the last choice.
    checkbox(2).click();
    fixture.detectChanges();
    tick(RUN_REPORT_ESTIMATE_DEBOUNCE_MS - 1);
    checkbox(2).click();
    fixture.detectChanges();
    tick(RUN_REPORT_ESTIMATE_DEBOUNCE_MS);
    expect(service.estimateRunReports).toHaveBeenCalledOnceWith(55, { writerModelConfigurationId: 1, audiences: [1, 2] });
    expect(estimateLine()).toBe('Estimating…');

    pending.next(estimateDto());
    fixture.detectChanges();
    const ready = section().querySelector('#rrWriteEstimate') as HTMLElement;
    expect(ready.hasAttribute('aria-busy')).toBeFalse();
    expect(ready.classList).not.toContain('is-muted');
    expect(ready.classList).not.toContain('is-empty');
    expect(ready.querySelector('.rr-ai-estimate-label')?.textContent?.trim()).toBe('Estimated cost');
    expect(ready.querySelector('.rr-ai-estimate-total')?.textContent?.trim()).toBe('about $0.18');
    const parts = Array.from(ready.querySelectorAll('.rr-ai-estimate-parts > div')) as HTMLElement[];
    expect(parts.map(part => [part.querySelector('dt')?.textContent?.trim(), part.querySelector('dd')?.textContent?.trim()]))
      .toEqual([['Executive Summary', '$0.04'], ['Report for AI Researchers and Developers', '$0.14']]);
    expect(ready.querySelector('.rr-ai-estimate-note')?.textContent?.trim())
      .toBe('The actual cost is shown while the reports are written.');
    flush();
    discardPeriodicTasks();
  }));

  it('should show the estimate of one document without a breakdown', fakeAsync(() => {
    service.estimateRunReports.and.returnValue(of(estimateDto({
      estimates: [{ audience: 2, promptChars: 3000, estimatedInputTokens: 2700, estimatedOutputTokens: 7000, estimatedCostUsd: 0.14 }],
      estimatedTotalCostUsd: 0.14
    })));
    setUp();
    load(reportRun({ assessmentJson: '{}', reportWriterModelConfigurationId: 1 }));
    checkbox(1).click();
    fixture.detectChanges();
    tick(RUN_REPORT_ESTIMATE_DEBOUNCE_MS);
    fixture.detectChanges();

    const block = section().querySelector('#rrWriteEstimate') as HTMLElement;
    expect(block.querySelector('.rr-ai-estimate-total')?.textContent?.trim()).toBe('about $0.14');
    expect(block.querySelector('.rr-ai-estimate-parts')).toBeNull();
    flush();
    discardPeriodicTasks();
  }));

  it('should keep the estimate block empty while no writer is chosen', () => {
    setUp();
    load(reportRun({ assessmentJson: '{}' }));
    const block = section().querySelector('#rrWriteEstimate') as HTMLElement;
    expect(block.getAttribute('role')).toBe('status');
    expect(block.classList).toContain('is-empty');
    expect(estimateLine()).toBe('');
  });

  it('should say when the writer has no price card, and when the estimate failed without blocking the write', fakeAsync(() => {
    service.estimateRunReports.and.returnValue(of(estimateDto({ estimatedTotalCostUsd: null })));
    setUp();
    load(reportRun({ assessmentJson: '{}', reportWriterModelConfigurationId: 1 }));
    tick(RUN_REPORT_ESTIMATE_DEBOUNCE_MS);
    fixture.detectChanges();
    expect(estimateLine()).toBe('No price card for this model; the cost cannot be estimated.');
    expect(section().querySelector('.rr-ai-estimate .rr-ai-estimate-note')).not.toBeNull();
    expect(section().querySelector('.rr-ai-estimate .rr-ai-estimate-total')).toBeNull();
    expect(section().querySelector('.rr-ai-estimate')?.classList).toContain('is-muted');

    service.estimateRunReports.and.returnValue(throwError(() => ({ status: 500 })));
    checkbox(1).click();
    fixture.detectChanges();
    tick(RUN_REPORT_ESTIMATE_DEBOUNCE_MS);
    fixture.detectChanges();
    expect(estimateLine()).toBe('The cost could not be estimated.');
    expect(section().querySelector('.rr-ai-estimate')?.classList).toContain('is-muted');
    expect(writeButton().disabled).toBeFalse();
    flush();
    discardPeriodicTasks();
  }));

  it('should show the estimate\'s refusal and hold Write Reports back', fakeAsync(() => {
    service.estimateRunReports.and.returnValue(of(estimateDto({ refusal: 'The writer cannot be used for this run.' })));
    setUp();
    load(reportRun({ assessmentJson: '{}', reportWriterModelConfigurationId: 1 }));
    tick(RUN_REPORT_ESTIMATE_DEBOUNCE_MS);
    fixture.detectChanges();

    expect(section().querySelector('#rrReportWriterBlocked')?.textContent?.trim()).toBe('The writer cannot be used for this run.');
    expect(writeButton().disabled).toBeTrue();
    flush();
    discardPeriodicTasks();
  }));

  it('should explain the documents and the writer choice in the Report writer info tip', () => {
    setUp();
    load(reportRun({ assessmentJson: '{}' }));

    const tip = section().querySelector('#rrReportWriterHint') as HTMLElement;
    const text = (tip.textContent ?? '').replace(/\s+/g, ' ');
    const groups = Array.from(tip.querySelectorAll('dl > div')) as HTMLElement[];
    expect(groups.map(group => group.querySelector('.gh-info-term')?.textContent?.trim()))
      .toEqual(['Executive Summary', 'Report for AI Researchers and Developers', 'Both']);
    expect(groups.map(group => group.querySelector('.gh-info-badge')?.textContent?.trim() ?? null)).toEqual(['Short', 'Long', null]);
    expect(text).toContain('For decision-makers. A short, plain-language document (about 2,000 output tokens)');
    expect(text).toContain('It costs roughly three to four times the summary.');
    expect(text).toContain('a writer from the same provider is allowed after a warning');
    expect(text).toContain('write one, then choose another writer for the other.');
    const row = tip.closest('app-info-tip')?.parentElement as HTMLElement;
    expect(row.classList).toContain('gh-field-row');
    expect(row.querySelector('.rr-report-writer-model-selector')).not.toBeNull();
    expect(row.parentElement?.classList).toContain('rr-ai-write-row');
    expect(section().querySelector('#rrReportWriterModelLabel')?.closest('.rr-ai-write-row')).toBeNull();
  });

  it('should explain the report writer in a dialog, not as the picker\'s description', () => {
    setUp();
    load(reportRun({ assessmentJson: '{}' }));

    const infoButton = section().querySelector('app-info-tip button.gh-info-btn') as HTMLButtonElement;
    expect(infoButton.getAttribute('aria-label')).toBe('About Report writer');
    expect(infoButton.getAttribute('aria-haspopup')).toBe('dialog');
    expect(section().querySelector('.gh-info-popup')).toBeNull();
    expect(section().querySelector('.rr-report-writer-model-selector .selector-trigger')?.hasAttribute('aria-describedby'))
      .toBeFalse();

    infoButton.click();
    fixture.detectChanges();
    const infoDialog = section().querySelector('dialog.gh-info-dialog') as HTMLDialogElement;
    expect(infoDialog.open).toBeTrue();
    expect(infoDialog.getAttribute('aria-labelledby')).toBe('rrReportWriterHint-title');
    expect(section().querySelector('#rrReportWriterHint-title')?.textContent?.trim()).toBe('Choosing a report writer');
    expect(document.activeElement).toBe(section().querySelector('#rrReportWriterHint-title'));
    expect(infoDialog.contains(section().querySelector('#rrReportWriterHint'))).toBeTrue();

    // Escape and the close button end at the info dialog; the run report dialog never hears of it.
    const reached: string[] = [];
    section().addEventListener('cancel', () => reached.push('cancel'));
    section().addEventListener('close', () => reached.push('close'));
    infoDialog.dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
    infoDialog.dispatchEvent(new Event('close', { bubbles: true }));
    expect(reached).toEqual([]);
  });

  it('should warn about a writer from the candidate\'s provider and write only after the confirmation', () => {
    service.writeRunReportDocuments.and.returnValue(of({ runId: 55, status: 1 }));
    setUp();
    load(reportRun({ assessmentJson: '{}' }));
    component.selectWriter(CONFIGS[2]);
    fixture.detectChanges();

    const warning = 'GPT Writer is from OpenAI, the provider of the model under test. Its reports may describe that model more favorably.';
    const alert = section().querySelector('.rr-ai-writer-warning') as HTMLElement;
    expect(alert.classList).toContain('alert-warning');
    expect(alert.querySelector('svg.alert-icon')?.getAttribute('aria-hidden')).toBe('true');
    expect(alert.textContent?.trim()).toBe(warning);
    expect(section().querySelector('.rr-report-writer-model-selector .selector-trigger')?.getAttribute('aria-describedby'))
      .toBe('rrReportWriterWarning');
    expect(writeButton().disabled).toBeFalse();

    writeButton().click();
    fixture.detectChanges();
    const confirm = dialog('.rr-ai-same-provider-dialog');
    expect(confirm.open).toBeTrue();
    expect(confirm.querySelector('h3')?.textContent?.trim()).toBe('Same-Provider Report Writer');
    expect(confirm.querySelector('.rr-ai-same-provider-text')?.textContent?.trim()).toBe(warning);
    expect(service.writeRunReportDocuments).not.toHaveBeenCalled();

    buttonIn(confirm, 'Cancel').click();
    expect(confirm.open).toBeFalse();
    expect(document.activeElement).toBe(writeButton());
    expect(service.writeRunReportDocuments).not.toHaveBeenCalled();

    writeButton().click();
    fixture.detectChanges();
    const anyway = buttonIn(confirm, 'Write Anyway');
    expect(anyway.classList).toContain('btn-gh');
    expect(anyway.querySelector('svg.btn-icon')).not.toBeNull();
    anyway.click();
    expect(service.writeRunReportDocuments).toHaveBeenCalledOnceWith(55, {
      writerModelConfigurationId: 9, audiences: [1, 2], acknowledgeSameProvider: true
    });
    expect(confirm.open).toBeFalse();
  });

  it('should ask for the confirmation when the server answers 409 with a same-provider warning', () => {
    service.writeRunReportDocuments.and.returnValues(
      throwError(() => ({
        status: 409,
        error: {
          sameProvider: true, provider: 'Google', testedModelDisplayName: 'Test Model',
          assessorModelDisplayName: 'Other Writer', message: 'Same provider.', role: 'reportWriter'
        }
      })),
      of({ runId: 55, status: 1 })
    );
    setUp();
    load(reportRun({ assessmentJson: '{}', reportWriterModelConfigurationId: 8 }));

    writeButton().click();
    fixture.detectChanges();

    const confirm = dialog('.rr-ai-same-provider-dialog');
    expect(confirm.open).toBeTrue();
    expect(confirm.querySelector('.rr-ai-same-provider-text')?.textContent?.trim())
      .toBe('Other Writer is from Google, the provider of the model under test. Its reports may describe that model more favorably.');
    expect(section().querySelector('[role="alert"]')).toBeNull();

    buttonIn(confirm, 'Write Anyway').click();
    const calls = service.writeRunReportDocuments.calls.allArgs();
    expect(calls.length).toBe(2);
    expect(calls[0][1].acknowledgeSameProvider).toBeUndefined();
    expect(calls[1][1].acknowledgeSameProvider).toBeTrue();
  });

  it('should open the progress dialog at once after a successful write, with the estimate', fakeAsync(() => {
    service.writeRunReportDocuments.and.returnValue(of({ runId: 55, status: 1 }));
    setUp();
    load(reportRun({ assessmentJson: '{}', reportWriterModelConfigurationId: 1 }));
    tick(RUN_REPORT_ESTIMATE_DEBOUNCE_MS);
    fixture.detectChanges();

    writeButton().click();

    expect(writingOpen).toHaveBeenCalledTimes(1);
    const context = writingOpen.calls.mostRecent().args[0];
    expect(context.runId).toBe(55);
    expect(context.runLabel).toBe('Default Suite · Test Model');
    expect(context.estimateUsd).toBe(0.18);
    expect(context.run?.provider).toBe('OpenAI');
    flush();
    discardPeriodicTasks();
  }));

  it('should offer Show Progress while a job is queued or writing, the automatic one included', () => {
    setUp();
    load(reportRun({ assessmentJson: '{}', reportDocumentsStatus: 1, reportWriterModelConfigurationId: 1 }));

    const show = section().querySelector('.rr-ai-show-progress') as HTMLButtonElement;
    expect(show.classList).toContain('btn-ghost');
    expect(show.textContent?.trim()).toBe('Show Progress');
    expect(show.closest('[role="status"]')).toBeNull();
    show.click();

    expect(writingOpen).toHaveBeenCalledTimes(1);
    expect(writingOpen.calls.mostRecent().args[0].runId).toBe(55);
    expect(writingOpen.calls.mostRecent().args[0].estimateUsd).toBeNull();

    load(reportRun({ id: 56, assessmentJson: '{}', reportDocumentsStatus: 3 }));
    expect(section().querySelector('.rr-ai-show-progress')).toBeNull();
  });

  it('should read the documents again when the progress dialog\'s job finishes, and open one it asks to view', () => {
    setUp();
    load(reportRun({ assessmentJson: '{}', reportDocumentsStatus: 2 }));
    const changes: RunReportStatusChange[] = [];
    component.reportStatusChange.subscribe(change => changes.push(change));
    service.listReportDocuments.and.returnValue(of([aiDoc(71, 1)]));

    component.writingDialog!.finished.emit(jobView(3));
    fixture.detectChanges();

    expect(rows().map(row => rowText(row, '.rr-ai-doc-status'))).toEqual(['Written', 'Not written']);
    expect(changes.map(change => change.status)).toEqual([3]);

    component.writingDialog!.viewRequested.emit(71);
    expect(viewerOpen).toHaveBeenCalledTimes(1);
    expect(viewerOpen.calls.mostRecent().args[0].title).toBe('Executive Summary');
  });

  it('should point to the Download Center once a document is written, and hand the button to the host', () => {
    service.listReportDocuments.and.returnValue(of([aiDoc(71, 1)]));
    setUp();
    load(reportRun({ assessmentJson: '{}' }));
    const requested: HTMLElement[] = [];
    component.downloadsRequested.subscribe(button => requested.push(button));

    const notice = section().querySelector('.rr-ai-download-notice') as HTMLElement;
    expect(notice.classList).toContain('alert');
    expect(notice.classList).toContain('alert-info');
    expect(notice.hasAttribute('role')).toBeFalse();
    expect(notice.querySelector('.alert-heading')?.textContent?.trim()).toBe('Downloads');
    expect((notice.querySelector('.alert-body')?.textContent ?? '').replace(/\s+/g, ' ').trim()).toBe(
      'These reports, and the rest of this run, can be downloaded from the Download Center as PDF, Word or Markdown, ' +
      'at Summary, Detailed or Full disclosure, one by one or together in a ZIP.');
    const button = notice.querySelector('.alert-actions button.btn-ghost') as HTMLButtonElement;
    expect(button.textContent?.trim()).toBe('Open Download Center');
    button.click();
    expect(requested).toEqual([button]);

    // The progress dialog's own button goes to the host too.
    const other = document.createElement('button');
    component.writingDialog!.downloadsRequested.emit(other);
    expect(requested).toEqual([button, other]);
  });

  describe('Delete', () => {
    function deleteButton(audience: number): HTMLButtonElement {
      return section().querySelector(`.rr-ai-doc-row[data-audience="${audience}"] .rr-ai-doc-delete`) as HTMLButtonElement;
    }

    it('should refuse while a job is in progress, saying why', () => {
      service.listReportDocuments.and.returnValue(of([aiDoc(71, 1)]));
      setUp();
      load(reportRun({ assessmentJson: '{}', reportDocumentsStatus: 2 }));

      const button = deleteButton(1);
      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(button.disabled).toBeFalse();
      const tip = fixture.nativeElement.querySelector('#' + button.getAttribute('interestfor')) as HTMLElement;
      expect(tip.textContent?.trim()).toBe('Wait for the writing to finish, or cancel it');

      button.click();
      expect(dialog('.rr-ai-delete-dialog').open).toBeFalse();
      expect(service.deleteRunReportDocument).not.toHaveBeenCalled();
    });

    it('should ask first, and keep the document on Keep It', () => {
      service.listReportDocuments.and.returnValue(of([aiDoc(71, 1)]));
      setUp();
      load(reportRun({ assessmentJson: '{}' }));

      deleteButton(1).click();
      fixture.detectChanges();
      const confirm = dialog('.rr-ai-delete-dialog');
      expect(confirm.open).toBeTrue();
      expect(confirm.querySelector('h3')?.textContent?.trim()).toBe('Delete the Executive Summary?');
      expect((confirm.querySelector('.rr-ai-delete-text')?.textContent ?? '').replace(/\s+/g, ' ').trim()).toBe(
        'Written by Test Model on 2026-09-28 10:15 UTC. The document is removed permanently; ' +
        'you can write it again afterwards, with any report writer.');
      const keep = buttonIn(confirm, 'Keep It');
      const remove = buttonIn(confirm, 'Delete');
      expect(keep.classList).toContain('btn-gh-cancel');
      expect(remove.classList).toContain('btn-gh-delete');
      expect(remove.querySelector('svg.btn-icon')).not.toBeNull();

      keep.click();
      expect(confirm.open).toBeFalse();
      expect(service.deleteRunReportDocument).not.toHaveBeenCalled();
    });

    it('should stop its own close and cancel events at the component', () => {
      service.listReportDocuments.and.returnValue(of([aiDoc(71, 1)]));
      setUp();
      load(reportRun({ assessmentJson: '{}' }));
      const reached: string[] = [];
      fixture.nativeElement.addEventListener('close', (event: Event) => reached.push(event.type));
      fixture.nativeElement.addEventListener('cancel', (event: Event) => reached.push(event.type));

      // Bubbling, so only the component's own handlers can keep them from the host.
      for (const selector of ['.rr-ai-delete-dialog', '.rr-ai-same-provider-dialog']) {
        const nested = dialog(selector);
        nested.dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
        nested.dispatchEvent(new Event('close', { bubbles: true }));
      }

      expect(reached).toEqual([]);
    });

    it('should delete, announce it, clear a picker holding the deleted document\'s writer and focus that document\'s checkbox', () => {
      service.listReportDocuments.and.returnValue(of([aiDoc(71, 1)]));
      setUp();
      load(reportRun({ assessmentJson: '{}', reportWriterModelConfigurationId: 1 }));
      expect(component.writerConfigId).toBe(1);

      deleteButton(1).click();
      fixture.detectChanges();
      service.listReportDocuments.and.returnValue(of([]));
      buttonIn(dialog('.rr-ai-delete-dialog'), 'Delete').click();
      fixture.detectChanges();

      expect(service.deleteRunReportDocument).toHaveBeenCalledOnceWith(55, 71);
      expect(dialog('.rr-ai-delete-dialog').open).toBeFalse();
      expect(status()).toBe('The Executive Summary was deleted.');
      expect(rows().map(row => rowText(row, '.rr-ai-doc-status'))).toEqual(['Not written', 'Not written']);
      expect(component.writerConfigId).toBeNull();
      expect(section().querySelector('.rr-ai-writer-note')?.textContent?.trim())
        .toBe('Choose a report writer. The deleted document was written by Test Model.');
      expect(checkbox(1).checked).toBeTrue();
      expect(document.activeElement).toBe(checkbox(1));
    });

    it('should keep a picker holding another writer', () => {
      service.listReportDocuments.and.returnValue(of([aiDoc(71, 1)]));
      setUp();
      load(reportRun({ assessmentJson: '{}', reportWriterModelConfigurationId: 8 }));

      deleteButton(1).click();
      fixture.detectChanges();
      buttonIn(dialog('.rr-ai-delete-dialog'), 'Delete').click();
      fixture.detectChanges();

      expect(component.writerConfigId).toBe(8);
      expect(section().querySelector('.rr-ai-writer-note')).toBeNull();
    });

    it('should keep the confirmation open with the failure inside it', () => {
      service.listReportDocuments.and.returnValue(of([aiDoc(71, 1)]));
      service.deleteRunReportDocument.and.returnValue(throwError(() => ({
        status: 409, error: { error: 'The reports are being written.' }
      })));
      setUp();
      load(reportRun({ assessmentJson: '{}' }));

      deleteButton(1).click();
      fixture.detectChanges();
      const confirm = dialog('.rr-ai-delete-dialog');
      buttonIn(confirm, 'Delete').click();
      fixture.detectChanges();

      expect(confirm.open).toBeTrue();
      const alert = confirm.querySelector('.alert.alert-danger[role="alert"]') as HTMLElement;
      expect(alert.textContent?.trim()).toBe('The Executive Summary could not be deleted: The reports are being written.');
      expect(status()).toBe('');
      expect(rows().map(row => rowText(row, '.rr-ai-doc-status'))).toEqual(['Written', 'Not written']);
    });
  });
});
