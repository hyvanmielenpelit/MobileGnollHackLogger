import type { Mock, MockedObject } from "vitest";
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
import { PdfViewerDialogComponent } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { PDFJS_LOADER } from '../../../shared/pdf-viewer/pdfjs-loader';
import { BenchmarkDownloadCenterComponent, rememberedPdfPaper } from '../download-center/benchmark-download-center.component';
import { reportDisclosureInfo } from '../report-disclosure-guide';
import { RunReportWritingDialogComponent } from '../run-ai-reports/run-report-writing-dialog.component';
import {
  BATTERY_REPORT_DOCUMENTS_POLL_MS,
  BATTERY_REPORT_ESTIMATE_DEBOUNCE_MS,
  BATTERY_REPORT_NOT_WRITABLE,
  BatteryAiReportsComponent,
  BatteryReportStatusChange
} from './battery-ai-reports.component';

function config(id: number, displayName: string, provider: string, modelId: string): SystemAiConfigDto {
  return {
    id, displayName, provider, modelId, thinkingLevel: null, reasoningMode: null, reasoningSummary: null,
    serviceTier: null, orderIndex: id, isEnabled: true, hasApiKey: true, modelRole: 7
  } as unknown as SystemAiConfigDto;
}

/** The candidate is OpenAI's gpt-test; 1 and 8 are other providers, 9 shares it, 10 is the candidate's own model. */
const CONFIGS: SystemAiConfigDto[] = [
  config(1, 'Test Writer', 'Anthropic', 'claude-3-5-sonnet'),
  config(8, 'Other Writer', 'Google', 'gemini-writer'),
  config(9, 'GPT Writer', 'OpenAI', 'gpt-writer'),
  config(10, 'Candidate Twin', 'OpenAI', 'gpt-test')
];
const OPTIONS = toModelPickerOptions(CONFIGS);

/** A finished battery run with a complete, current analysis. */
function batteryRun(overrides: any = {}): any {
  return {
    id: 7, batteryId: 2, batteryName: 'Core Battery', definitionRevision: 1, definitionSha256: 'd'.repeat(64),
    weightingScheme: 'Equal', suites: [], suiteCount: 2, runsPerSuite: 1, requestedMemberCount: 2,
    completedMemberCount: 2, failedMemberCount: 0, completedSuiteCount: 2, status: 'Completed',
    allowCapWait: false, resumable: false, isDriving: false, startedAtUtc: '2026-10-02T08:00:00Z',
    completedAtUtc: '2026-10-02T09:00:00Z', testedModelConfigurationId: 20, testedModelLabel: 'Test Model',
    testedProvider: 'OpenAI', testedModelId: 'gpt-test', slots: [], members: [],
    latestAnalysisId: 3, latestAnalysisComplete: true, analysisStale: false, analysisHasExcludedMembers: false,
    reportWriterModelConfigurationId: null, reportDocumentsStatus: 0, reportDocumentsMessage: null,
    ...overrides
  };
}

function analysis(overrides: any = {}): any {
  return {
    id: 3, batteryRunId: 7, batteryName: 'Core Battery', computedAtUtc: '2026-10-02T09:05:00Z', memberRunIds: [101, 102],
    runCount: 2, definitionSha256: 'd'.repeat(64), complete: true, scoringMethodVersion: 1, stale: false,
    result: null, comparison: null, excludedMembers: [],
    ...overrides
  };
}

/** A battery-completion document of battery run 7. */
function aiDoc(id: number, audience: number, overrides: any = {}): any {
  return {
    id, packId: 'battery-7', audience, title: `Document ${id}`, subjectKey: 'battery:7', subjectLabel: 'Test Model',
    subjectRunIds: [101, 102], suiteId: null, suiteName: '', writerDisplayName: 'Test Writer',
    writerProvider: 'Anthropic', writerModelId: 'claude-3-5-sonnet', writerThinkingLevel: null,
    sameProviderAcknowledged: false, status: 'Completed', reportFormatVersion: 2,
    createdAtUtc: '2026-10-02T10:15:00Z', inputTokens: 0, outputTokens: 0, durationMs: 0, costUsd: null,
    runChangedSinceGeneration: false, missingRunIds: [], allowedDisclosures: [1, 2, 3], origin: 3,
    ...overrides
  };
}

function jobView(status: number, overrides: Partial<BenchmarkRunReportJobDto> = {}): BenchmarkRunReportJobDto {
  return {
    runId: 7, status, message: null, phase: status >= 3 ? 'Finished' : 'Writing',
    queuedAtUtc: '2026-10-02T10:00:00Z', slotAcquiredAtUtc: null, finishedAtUtc: null, cancelRequestedAtUtc: null,
    jobsAhead: null, blockingJobLabel: null, audiences: [1, 2], writerConfigId: 1, writerDisplayName: 'Test Writer',
    writerProvider: 'Anthropic', writerModelId: 'claude-3-5-sonnet', writerThinkingLevel: null,
    job: null as any, serverTimeUtc: '2026-10-02T10:00:10Z',
    ...overrides
  };
}

function estimateDto(overrides: Partial<BenchmarkRunReportEstimateDto> = {}): BenchmarkRunReportEstimateDto {
  return {
    estimates: [
      { audience: 1, promptChars: 1000, estimatedInputTokens: 900, estimatedOutputTokens: 2000, estimatedCostUsd: 0.06 },
      { audience: 2, promptChars: 3000, estimatedInputTokens: 2700, estimatedOutputTokens: 7000, estimatedCostUsd: 0.2 }
    ],
    estimatedTotalCostUsd: 0.26,
    refusal: null,
    sameProviderWarning: null,
    ...overrides
  };
}

describe('BatteryAiReportsComponent', () => {
  let fixture: ComponentFixture<BatteryAiReportsComponent>;
  let component: BatteryAiReportsComponent;
  let service: MockedObject<AdminBenchmarkService>;
  let writingOpen: Mock;
  let viewerOpen: Mock;
  let downloadCenterOpen: Mock;

  beforeEach(async () => {
    service = {
      listReportDocuments: vi.fn().mockName("AdminBenchmarkService.listReportDocuments"),
      writeBatteryReportDocuments: vi.fn().mockName("AdminBenchmarkService.writeBatteryReportDocuments"),
      getReportDocumentPdf: vi.fn().mockName("AdminBenchmarkService.getReportDocumentPdf"),
      reportDocumentPdfUrl: vi.fn().mockName("AdminBenchmarkService.reportDocumentPdfUrl"),
      getBatteryReportJob: vi.fn().mockName("AdminBenchmarkService.getBatteryReportJob"),
      cancelBatteryReportJob: vi.fn().mockName("AdminBenchmarkService.cancelBatteryReportJob"),
      getBatteryRun: vi.fn().mockName("AdminBenchmarkService.getBatteryRun"),
      estimateBatteryReports: vi.fn().mockName("AdminBenchmarkService.estimateBatteryReports"),
      deleteBatteryReportDocument: vi.fn().mockName("AdminBenchmarkService.deleteBatteryReportDocument"),
      getBatteryReportUrl: vi.fn().mockName("AdminBenchmarkService.getBatteryReportUrl")
    } as unknown as MockedObject<AdminBenchmarkService>;
    service.listReportDocuments.mockReturnValue(of([]));
    service.getBatteryReportJob.mockReturnValue(of(null));
    service.getBatteryRun.mockReturnValue(of(batteryRun()));
    service.estimateBatteryReports.mockReturnValue(of(estimateDto()));
    service.deleteBatteryReportDocument.mockReturnValue(of(undefined));
    service.getReportDocumentPdf.mockReturnValue(of({ bytes: new Uint8Array([37, 80, 68, 70]), fileName: null }));
    service.reportDocumentPdfUrl.mockImplementation((id: number, disclosure: number, peers: number, paper: string, inline?: boolean) => `/pdf/${id}/${disclosure}/${peers}/${paper}/${inline ? 'inline' : 'attachment'}`);

    // None of the hosted dialogs really opens: the progress dialog would poll, the viewer would load pdf.js.
    writingOpen = vi.spyOn(RunReportWritingDialogComponent.prototype, 'open').mockReturnValue(undefined);
    viewerOpen = vi.spyOn(PdfViewerDialogComponent.prototype, 'open').mockReturnValue(undefined);
    downloadCenterOpen = vi.spyOn(BenchmarkDownloadCenterComponent.prototype, 'open').mockReturnValue(undefined);

    await TestBed.configureTestingModule({
      imports: [BatteryAiReportsComponent],
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

  function setUp(): void {
    fixture = TestBed.createComponent(BatteryAiReportsComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('writerConfigs', CONFIGS);
    fixture.componentRef.setInput('pickerOptions', OPTIONS);
    fixture.componentRef.setInput('pickerEmptyHint', 'No models.');
  }

  function load(run: any, latest: any = analysis()): void {
    fixture.componentRef.setInput('analysis', latest);
    fixture.componentRef.setInput('batteryRun', run);
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

  function status(): string {
    return (section().querySelector('.rr-ai-status[role="status"]')?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function writeButton(): HTMLButtonElement {
    return section().querySelector('.rr-ai-write-btn') as HTMLButtonElement;
  }

  function checkbox(audience: number): HTMLInputElement {
    return section().querySelector(`#${component.idPrefix}Audience${audience}`) as HTMLInputElement;
  }

  function byId(suffix: string): HTMLElement | null {
    return fixture.nativeElement.querySelector(`[id="${component.idPrefix}${suffix}"]`) as HTMLElement | null;
  }

  function dialog(selector: string): HTMLDialogElement {
    return fixture.nativeElement.querySelector(selector) as HTMLDialogElement;
  }

  function buttonIn(root: HTMLElement, label: string): HTMLButtonElement {
    return (Array.from(root.querySelectorAll('button')) as HTMLButtonElement[])
      .find(button => (button.textContent ?? '').replace(/\s+/g, ' ').trim() === label)!;
  }

  // --- Rows and tags ---

  it('lists both documents as not written, from the battery run\'s battery-completion documents', () => {
    setUp();
    load(batteryRun());

    expect(service.listReportDocuments).toHaveBeenCalledWith({ subject: 'battery:7', origin: 'batteryCompletion' });
    expect(status()).toBe('');
    expect(rows().map(row => rowText(row, '.rr-ai-doc-name'))).toEqual(['Executive Summary', 'Report for AI Researchers and Developers']);
    for (const row of rows()) {
      expect(rowText(row, '.rr-ai-doc-status.is-missing')).toBe('Not written');
      expect(row.querySelector('button')).toBeNull();
    }
    expect(byId('ReportWriterModelLabel')?.textContent?.trim()).toBe('Report writer');
    expect(writeButton().disabled).toBe(true);
    expect(section().querySelector('.bai-not-writable')).toBeNull();
    expect(section().querySelector('.rr-ai-download-notice')).toBeNull();
  });

  it('lists stored reports with their tags and meta, each with a View and a Delete named for it', () => {
    service.listReportDocuments.mockReturnValue(of([
      aiDoc(72, 2, {
        runChangedSinceGeneration: true, createdAtUtc: '2026-10-02T11:00:00Z', writerDisplayName: 'Writer B',
        status: 'CompletedWithWarnings', durationMs: 72000, costUsd: 0.08, sameProviderAcknowledged: true
      }),
      aiDoc(71, 1),
      // A Report Pack document about the same battery run is not one of its AI-written reports.
      aiDoc(73, 1, { origin: 1, createdAtUtc: '2026-10-03T00:00:00Z' })
    ]));
    setUp();
    load(batteryRun({ reportDocumentsStatus: 4 }));

    const list = rows();
    expect(list.map(row => rowText(row, '.rr-ai-doc-status'))).toEqual(['Written', 'Written with warnings']);
    expect(list[1].querySelector('.rr-ai-doc-status')?.classList).toContain('is-warning');
    expect(list.map(row => row.getAttribute('data-document-id'))).toEqual(['71', '72']);
    expect(list.map(row => rowText(row, '.rr-ai-doc-meta'))).toEqual([
      'by Test Writer on 2026-10-02 10:15 UTC',
      'by Writer B on 2026-10-02 11:00 UTC · 1 min 12 s · $0.08 · same provider, acknowledged'
    ]);
    expect(list[0].querySelector('.gh-tag-changed')).toBeNull();
    expect(list[1].querySelector('.gh-tag-changed')?.textContent?.trim()).toBe('A member run changed since this document was written');
    const views = list.map(row => row.querySelector('button.rr-ai-doc-view') as HTMLButtonElement);
    expect(views.map(button => button.getAttribute('aria-label')))
      .toEqual(['View the Executive Summary', 'View the Report for AI Researchers and Developers']);
    const deletes = list.map(row => row.querySelector('button.rr-ai-doc-delete') as HTMLButtonElement);
    for (const button of deletes) {
      expect(button.classList).toContain('action-btn-danger');
      expect(button.hasAttribute('title')).toBe(false);
      const tipId = button.getAttribute('interestfor')!;
      expect(tipId.startsWith(component.idPrefix + '-tip-delete-')).toBe(true);
      const tip = fixture.nativeElement.querySelector('#' + tipId) as HTMLElement;
      expect(tip.getAttribute('popover')).toBe('hint');
      expect(tip.textContent?.trim()).toBe('Delete');
    }
    expect(section().querySelector('.rr-ai-write')).toBeNull();
    expect(section().querySelector('.rr-ai-download-notice')).not.toBeNull();
  });

  it('opens a report in the PDF viewer at its fullest allowed disclosure with peers named', () => {
    service.listReportDocuments.mockReturnValue(of([aiDoc(71, 1, { allowedDisclosures: [2, 1] })]));
    setUp();
    load(batteryRun());

    (section().querySelector('button.rr-ai-doc-view') as HTMLButtonElement).click();

    expect(viewerOpen).toHaveBeenCalledTimes(1);
    const request = vi.mocked(viewerOpen).mock.lastCall![0];
    const paper = rememberedPdfPaper();
    expect(request.title).toBe('Executive Summary');
    expect(request.subtitle).toBe('Battery run #7 · by Test Writer on 2026-10-02 10:15 UTC');
    expect(request.variants).toEqual([{ key: 'summary', label: 'Summary' }, { key: 'detailed', label: 'Detailed' }]);
    expect(request.initialVariant).toBe('detailed');
    expect(request.fallbackFileName).toBe('battery-run-7_executive-summary.pdf');
    request.load('summary').subscribe();
    expect(vi.mocked(service.getReportDocumentPdf).mock.lastCall).toEqual([71, 1, 1, paper]);
    expect(request.variantsInfo).toEqual(reportDisclosureInfo(1));
  });

  // --- Delete ---

  describe('Delete', () => {
    function deleteButton(audience: number): HTMLButtonElement {
      return section().querySelector(`.rr-ai-doc-row[data-audience="${audience}"] .rr-ai-doc-delete`) as HTMLButtonElement;
    }

    it('asks first, keeps the document on Keep It, and deletes through the battery endpoint', () => {
      service.listReportDocuments.mockReturnValue(of([aiDoc(71, 1)]));
      setUp();
      load(batteryRun({ reportWriterModelConfigurationId: 1 }));
      expect(component.writerConfigId).toBe(1);

      deleteButton(1).click();
      fixture.detectChanges();
      const confirm = dialog('.rr-ai-delete-dialog');
      expect(confirm.open).toBe(true);
      expect(confirm.getAttribute('aria-labelledby')).toBe(`${component.idPrefix}DeleteTitle`);
      expect(confirm.querySelector('h3')?.textContent?.trim()).toBe('Delete the Executive Summary?');
      buttonIn(confirm, 'Keep It').click();
      expect(confirm.open).toBe(false);
      expect(service.deleteBatteryReportDocument).not.toHaveBeenCalled();

      deleteButton(1).click();
      fixture.detectChanges();
      service.listReportDocuments.mockReturnValue(of([]));
      buttonIn(confirm, 'Delete').click();
      fixture.detectChanges();

      expect(service.deleteBatteryReportDocument).toHaveBeenCalledWith(7, 71);
      expect(confirm.open).toBe(false);
      expect(status()).toBe('The Executive Summary was deleted.');
      expect(rows().map(row => rowText(row, '.rr-ai-doc-status'))).toEqual(['Not written', 'Not written']);
      expect(component.writerConfigId).toBeNull();
      expect(section().querySelector('.rr-ai-writer-note')?.textContent?.trim())
        .toBe('Choose a report writer. The deleted document was written by Test Writer.');
      expect(document.activeElement).toBe(checkbox(1));
    });

    it('refuses while a job is in progress, saying why', () => {
      service.listReportDocuments.mockReturnValue(of([aiDoc(71, 1)]));
      setUp();
      load(batteryRun({ reportDocumentsStatus: 2 }));

      const button = deleteButton(1);
      expect(button.getAttribute('aria-disabled')).toBe('true');
      const tip = fixture.nativeElement.querySelector('#' + button.getAttribute('interestfor')) as HTMLElement;
      expect(tip.textContent?.trim()).toBe('Wait for the writing to finish, or cancel it');
      button.click();
      expect(dialog('.rr-ai-delete-dialog').open).toBe(false);
      expect(service.deleteBatteryReportDocument).not.toHaveBeenCalled();
    });

    it('keeps the confirmation open with the failure inside it', () => {
      service.listReportDocuments.mockReturnValue(of([aiDoc(71, 1)]));
      service.deleteBatteryReportDocument.mockReturnValue(throwError(() => ({
        status: 409, error: { error: 'The reports are being written.' }
      })));
      setUp();
      load(batteryRun());

      deleteButton(1).click();
      fixture.detectChanges();
      const confirm = dialog('.rr-ai-delete-dialog');
      buttonIn(confirm, 'Delete').click();
      fixture.detectChanges();

      expect(confirm.open).toBe(true);
      expect(confirm.querySelector('.alert.alert-danger[role="alert"]')?.textContent?.trim())
        .toBe('The Executive Summary could not be deleted: The reports are being written.');
    });

    it('stops its nested dialogs\' close and cancel events at the component', () => {
      setUp();
      load(batteryRun());
      const reached: string[] = [];
      fixture.nativeElement.addEventListener('close', (event: Event) => reached.push(event.type));
      fixture.nativeElement.addEventListener('cancel', (event: Event) => reached.push(event.type));
      for (const selector of ['.rr-ai-delete-dialog', '.rr-ai-same-provider-dialog']) {
        const nested = dialog(selector);
        nested.dispatchEvent(new Event('cancel', { bubbles: true, cancelable: true }));
        nested.dispatchEvent(new Event('close', { bubbles: true }));
      }
      expect(reached).toEqual([]);
    });
  });

  // --- The report writer ---

  it('preselects the battery run\'s own writer', () => {
    setUp();
    load(batteryRun({ reportWriterModelConfigurationId: 8 }));

    expect(component.writerConfigId).toBe(8);
    expect(writeButton().disabled).toBe(false);
  });

  it('leaves the writer empty when the battery run has none, or one the picker does not offer', () => {
    setUp();
    load(batteryRun());
    expect(component.writerConfigId).toBeNull();
    expect(writeButton().disabled).toBe(true);

    setUp();
    load(batteryRun({ reportWriterModelConfigurationId: 99 }));
    expect(component.writerConfigId).toBeNull();
  });

  it('explains the documents and the writer choice in its own Report writer info tip', () => {
    setUp();
    load(batteryRun());

    const tip = byId('ReportWriterHint') as HTMLElement;
    const groups = Array.from(tip.querySelectorAll('dl > div')) as HTMLElement[];
    expect(groups.map(group => group.querySelector('.gh-info-term')?.textContent?.trim()))
      .toEqual(['Executive Summary', 'Report for AI Researchers and Developers', 'Both']);
    expect((tip.textContent ?? '').replace(/\s+/g, ' ')).toContain('composite index over several suites');
  });

  // --- Writing ---

  it('estimates once the choice rests, then writes the missing reports and follows the battery run\'s job', fakeAsync(() => {
    service.writeBatteryReportDocuments.mockReturnValue(of({ runId: 7, status: 1 }));
    setUp();
    const changes: BatteryReportStatusChange[] = [];
    load(batteryRun({ reportWriterModelConfigurationId: 1 }));
    component.reportStatusChange.subscribe(change => changes.push(change));

    expect((byId('WriteEstimate')?.textContent ?? '').trim()).toBe('Estimating…');
    tick(BATTERY_REPORT_ESTIMATE_DEBOUNCE_MS);
    fixture.detectChanges();
    expect(service.estimateBatteryReports).toHaveBeenCalledWith(7, { writerModelConfigurationId: 1, audiences: [1, 2] });
    const estimate = byId('WriteEstimate') as HTMLElement;
    expect(estimate.classList).toContain('gh-estimate-panel');
    expect(estimate.getAttribute('role')).toBe('status');
    expect(estimate.querySelector('.gh-estimate-total')?.textContent?.trim()).toBe('about $0.26');
    expect(writeButton().getAttribute('aria-describedby')).toBe(`${component.idPrefix}WriteEstimate`);
    expect(writeButton().textContent?.trim()).toBe('Write Reports');
    expect(writeButton().querySelector('svg.btn-icon')?.getAttribute('aria-hidden')).toBe('true');

    writeButton().click();
    fixture.detectChanges();

    expect(service.writeBatteryReportDocuments).toHaveBeenCalledWith(7, { writerModelConfigurationId: 1, audiences: [1, 2] });
    expect(status()).toBe('Waiting for the report writer');
    expect(writeButton().disabled).toBe(true);
    expect(changes.map(change => change.status)).toEqual([1]);

    expect(writingOpen).toHaveBeenCalledTimes(1);
    const context = vi.mocked(writingOpen).mock.lastCall![0];
    expect(context.runId).toBe(7);
    expect(context.runLabel).toBe('Core Battery · Test Model');
    expect(context.estimateUsd).toBe(0.26);
    expect(context.source?.subjectLabel).toBe('Battery run #7');
    expect(context.source?.fileStem).toBe('battery-run-7');
    service.cancelBatteryReportJob.mockReturnValue(of(jobView(7)));
    context.source!.getJob().subscribe();
    context.source!.cancel().subscribe();
    context.source!.getStoredStatus!().subscribe();
    expect(service.getBatteryReportJob).toHaveBeenCalledWith(7);
    expect(service.cancelBatteryReportJob).toHaveBeenCalledWith(7);
    expect(service.getBatteryRun).toHaveBeenCalledWith(7);

    // The status poll sees the job finish and lists the documents again.
    service.getBatteryReportJob.mockReturnValue(of(jobView(3)));
    service.listReportDocuments.mockReturnValue(of([aiDoc(71, 1), aiDoc(72, 2)]));
    tick(BATTERY_REPORT_DOCUMENTS_POLL_MS);
    fixture.detectChanges();
    expect(status()).toBe('');
    expect(section().querySelectorAll('.rr-ai-doc-view').length).toBe(2);
    expect(section().querySelector('.rr-ai-write')).toBeNull();
    expect(changes.map(change => change.status)).toEqual([1, 3]);
    flush();
    discardPeriodicTasks();
  }));

  it('checks every missing document, sends the checked ones, and names the button by their count', () => {
    service.writeBatteryReportDocuments.mockReturnValue(of({ runId: 7, status: 1 }));
    setUp();
    load(batteryRun({ reportWriterModelConfigurationId: 1 }));

    expect(checkbox(1).checked).toBe(true);
    expect(checkbox(2).checked).toBe(true);
    checkbox(1).click();
    fixture.detectChanges();
    expect(writeButton().textContent?.trim()).toBe('Write Report');
    writeButton().click();
    expect(service.writeBatteryReportDocuments).toHaveBeenCalledWith(7, { writerModelConfigurationId: 1, audiences: [2] });
  });

  it('refuses the model under test as its own writer, in red, joined to the picker\'s description', () => {
    setUp();
    load(batteryRun({ reportWriterModelConfigurationId: 10 }));

    const blocked = byId('ReportWriterBlocked') as HTMLElement;
    expect(blocked.textContent?.trim()).toBe('The model under test cannot write its own reports.');
    expect(blocked.classList).toContain('gh-field-error');
    expect(writeButton().disabled).toBe(true);
    expect(section().querySelector('.bai-report-writer-model-selector .selector-trigger')?.getAttribute('aria-describedby'))
      .toBe(`${component.idPrefix}ReportWriterBlocked`);
  });

  it('shows the estimate\'s refusal and holds Write Reports back', fakeAsync(() => {
    service.estimateBatteryReports.mockReturnValue(of(estimateDto({ refusal: 'The writer cannot be used for this battery run.' })));
    setUp();
    load(batteryRun({ reportWriterModelConfigurationId: 1 }));
    tick(BATTERY_REPORT_ESTIMATE_DEBOUNCE_MS);
    fixture.detectChanges();

    expect(byId('ReportWriterBlocked')?.textContent?.trim()).toBe('The writer cannot be used for this battery run.');
    expect(writeButton().disabled).toBe(true);
    flush();
    discardPeriodicTasks();
  }));

  it('warns about a writer from the candidate\'s provider in amber and writes only after Write Anyway', () => {
    service.writeBatteryReportDocuments.mockReturnValue(of({ runId: 7, status: 1 }));
    setUp();
    load(batteryRun());
    component.selectWriter(CONFIGS[2]);
    fixture.detectChanges();

    const warning = 'GPT Writer is from OpenAI, the provider of the model under test. Its reports may describe that model more favorably.';
    const alert = section().querySelector('.rr-ai-writer-warning') as HTMLElement;
    expect(alert.classList).toContain('alert-warning');
    expect(alert.textContent?.trim()).toBe(warning);
    expect(writeButton().disabled).toBe(false);

    writeButton().click();
    fixture.detectChanges();
    const confirm = dialog('.rr-ai-same-provider-dialog');
    expect(confirm.open).toBe(true);
    expect(confirm.querySelector('h3')?.textContent?.trim()).toBe('Same-Provider Report Writer');
    expect(confirm.querySelector('.rr-ai-same-provider-text')?.textContent?.trim()).toBe(warning);
    expect(service.writeBatteryReportDocuments).not.toHaveBeenCalled();

    buttonIn(confirm, 'Cancel').click();
    expect(confirm.open).toBe(false);
    expect(document.activeElement).toBe(writeButton());

    writeButton().click();
    fixture.detectChanges();
    const anyway = buttonIn(confirm, 'Write Anyway');
    expect(anyway.querySelector('svg.btn-icon')).not.toBeNull();
    anyway.click();
    expect(service.writeBatteryReportDocuments).toHaveBeenCalledWith(7, {
      writerModelConfigurationId: 9, audiences: [1, 2], acknowledgeSameProvider: true
    });
    expect(confirm.open).toBe(false);
  });

  it('asks for the confirmation when the server answers 409 with a same-provider warning', () => {
    service.writeBatteryReportDocuments.mockReturnValueOnce(throwError(() => ({
      status: 409,
      error: {
        sameProvider: true, provider: 'Google', testedModelDisplayName: 'Test Model',
        assessorModelDisplayName: 'Other Writer', message: 'Same provider.', role: 'reportWriter'
      }
    }))).mockReturnValueOnce(of({ runId: 7, status: 1 }));
    setUp();
    load(batteryRun({ reportWriterModelConfigurationId: 8 }));

    writeButton().click();
    fixture.detectChanges();
    const confirm = dialog('.rr-ai-same-provider-dialog');
    expect(confirm.open).toBe(true);
    buttonIn(confirm, 'Write Anyway').click();
    const calls = vi.mocked(service.writeBatteryReportDocuments).mock.calls;
    expect(calls.length).toBe(2);
    expect(calls[0][1].acknowledgeSameProvider).toBeUndefined();
    expect(calls[1][1].acknowledgeSameProvider).toBe(true);
  });

  it('shows the server\'s refusal of Write Reports inline', () => {
    service.writeBatteryReportDocuments.mockReturnValue(throwError(() => ({
      status: 400, error: { error: 'The battery run has no complete, current analysis.' }
    })));
    setUp();
    load(batteryRun({ reportWriterModelConfigurationId: 1 }));

    writeButton().click();
    fixture.detectChanges();
    const alert = section().querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent?.trim()).toBe('The battery run has no complete, current analysis.');
    expect(writingOpen).not.toHaveBeenCalled();
  });

  // --- When the battery run cannot be written ---

  describe('not writable', () => {
    const cases: { name: string; run: any; latest: any; reason: string }[] = [
      { name: 'still running', run: batteryRun({ status: 'Running' }), latest: null, reason: BATTERY_REPORT_NOT_WRITABLE.notFinished },
      { name: 'stopped', run: batteryRun({ status: 'Stopped' }), latest: analysis(), reason: BATTERY_REPORT_NOT_WRITABLE.notFinished },
      { name: 'not analyzed', run: batteryRun({ latestAnalysisId: null, latestAnalysisComplete: null }), latest: null, reason: BATTERY_REPORT_NOT_WRITABLE.noAnalysis },
      { name: 'analyzed incompletely', run: batteryRun(), latest: analysis({ complete: false }), reason: BATTERY_REPORT_NOT_WRITABLE.incomplete },
      { name: 'analyzed before its members changed (analysis)', run: batteryRun(), latest: analysis({ stale: true }), reason: BATTERY_REPORT_NOT_WRITABLE.stale },
      { name: 'analyzed before its members changed (run)', run: batteryRun({ analysisStale: true }), latest: analysis(), reason: BATTERY_REPORT_NOT_WRITABLE.stale }
    ];

    for (const testCase of cases) {
      it(`holds Write Reports back and says why when the battery run is ${testCase.name}`, fakeAsync(() => {
        setUp();
        load({ ...testCase.run, reportWriterModelConfigurationId: 1 }, testCase.latest);
        tick(BATTERY_REPORT_ESTIMATE_DEBOUNCE_MS);
        fixture.detectChanges();

        const reason = byId('NotWritable') as HTMLElement;
        expect(reason.textContent?.trim()).toBe(testCase.reason);
        expect(reason.classList).toContain('bai-not-writable');
        expect(writeButton().disabled).toBe(true);
        expect(writeButton().getAttribute('aria-describedby'))
          .toBe(`${component.idPrefix}NotWritable ${component.idPrefix}WriteEstimate`);
        expect(byId('ReportWriterBlocked')).toBeNull();
        expect(service.estimateBatteryReports).not.toHaveBeenCalled();
        flush();
        discardPeriodicTasks();
      }));
    }

    it('reads the analysis from the battery run while no analysis is handed in', () => {
      setUp();
      load(batteryRun({ reportWriterModelConfigurationId: 1 }), null);
      expect(byId('NotWritable')).toBeNull();
      expect(writeButton().disabled).toBe(false);

      load(batteryRun({ id: 8, reportWriterModelConfigurationId: 1, latestAnalysisComplete: false }), null);
      expect(byId('NotWritable')?.textContent?.trim()).toBe(BATTERY_REPORT_NOT_WRITABLE.incomplete);
    });

    it('becomes writable when a recomputed analysis arrives', () => {
      setUp();
      load(batteryRun({ reportWriterModelConfigurationId: 1 }), analysis({ stale: true }));
      expect(writeButton().disabled).toBe(true);

      fixture.componentRef.setInput('analysis', analysis({ id: 4 }));
      fixture.detectChanges();
      expect(byId('NotWritable')).toBeNull();
      expect(writeButton().disabled).toBe(false);
    });

    it('says while it runs that the battery run\'s writer writes them once it is finished and analyzed', () => {
      setUp();
      load(batteryRun({ status: 'Running', reportWriterModelConfigurationId: 1 }), null);
      expect(status()).toBe('Not written yet: Test Writer writes them once the battery run is finished and analyzed');
    });
  });

  // --- Progress and polling ---

  it('offers Show Progress while a job is queued or writing, opening the progress dialog on the battery run\'s job', () => {
    setUp();
    load(batteryRun({ reportDocumentsStatus: 1, reportWriterModelConfigurationId: 1 }));

    const show = section().querySelector('.rr-ai-show-progress') as HTMLButtonElement;
    expect(show.classList).toContain('btn-ghost');
    expect(show.textContent?.trim()).toBe('Show Progress');
    show.click();

    expect(writingOpen).toHaveBeenCalledTimes(1);
    const context = vi.mocked(writingOpen).mock.lastCall![0];
    expect(context.runId).toBe(7);
    expect(context.estimateUsd).toBeNull();
    expect(context.source?.subjectLabel).toBe('Battery run #7');

    load(batteryRun({ id: 8, reportDocumentsStatus: 3 }));
    expect(section().querySelector('.rr-ai-show-progress')).toBeNull();
  });

  it('polls the job while the reports are written, falls back to the battery run, and stops when the dialog closes', fakeAsync(() => {
    setUp();
    load(batteryRun({ reportDocumentsStatus: 2 }));
    expect(status()).toBe('Writing…');
    expect(service.listReportDocuments).toHaveBeenCalledTimes(1);

    service.getBatteryReportJob.mockReturnValue(of(jobView(2)));
    tick(BATTERY_REPORT_DOCUMENTS_POLL_MS);
    expect(service.getBatteryReportJob).toHaveBeenCalledWith(7);
    expect(service.listReportDocuments).toHaveBeenCalledTimes(1);

    fixture.componentRef.setInput('dialogOpen', false);
    fixture.detectChanges();
    const calls = vi.mocked(service.getBatteryReportJob).mock.calls.length;
    tick(BATTERY_REPORT_DOCUMENTS_POLL_MS * 2);
    expect(vi.mocked(service.getBatteryReportJob).mock.calls.length).toBe(calls);

    fixture.componentRef.setInput('dialogOpen', true);
    fixture.detectChanges();
    service.getBatteryReportJob.mockReturnValue(of(null));
    service.getBatteryRun.mockReturnValue(of(batteryRun({ reportDocumentsStatus: 6, reportDocumentsMessage: 'No writer.' })));
    tick(BATTERY_REPORT_DOCUMENTS_POLL_MS);
    fixture.detectChanges();
    expect(service.getBatteryRun).toHaveBeenCalledWith(7);
    expect(service.listReportDocuments).toHaveBeenCalledTimes(2);
    expect(status()).toBe('Skipped: No writer.');
    flush();
    discardPeriodicTasks();
  }));

  it('reads the documents again when the progress dialog\'s job finishes, and opens one it asks to view', () => {
    setUp();
    load(batteryRun({ reportDocumentsStatus: 2 }));
    service.listReportDocuments.mockReturnValue(of([aiDoc(71, 1)]));

    component.writingDialog!.finished.emit(jobView(3));
    fixture.detectChanges();
    expect(rows().map(row => rowText(row, '.rr-ai-doc-status'))).toEqual(['Written', 'Not written']);

    component.writingDialog!.viewRequested.emit(71);
    expect(viewerOpen).toHaveBeenCalledTimes(1);
    expect(vi.mocked(viewerOpen).mock.lastCall![0].title).toBe('Executive Summary');
  });

  it('gives the progress dialog ids of its own', () => {
    setUp();
    load(batteryRun());
    expect(fixture.nativeElement.querySelector(`[id="${component.idPrefix}RwTitle"]`)).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#rwTitle')).toBeNull();
  });

  // --- Downloads ---

  it('opens a Download Center of its own on the battery run when no host listens, and returns focus to the button', () => {
    service.listReportDocuments.mockReturnValue(of([aiDoc(71, 1)]));
    setUp();
    load(batteryRun());
    expect(fixture.nativeElement.querySelector('app-benchmark-download-center')).toBeNull();

    const button = section().querySelector('.rr-ai-download-notice .rr-ai-open-downloads') as HTMLButtonElement;
    expect(button.textContent?.trim()).toBe('Open Download Center');
    button.click();

    expect(fixture.nativeElement.querySelector('app-benchmark-download-center')).not.toBeNull();
    expect(downloadCenterOpen).toHaveBeenCalledTimes(1);
    expect(vi.mocked(downloadCenterOpen).mock.lastCall![0]).toEqual({ kind: 'battery', batteryRunId: 7, label: 'Core Battery · Test Model' });

    component.downloadCenter!.closed.emit();
    expect(document.activeElement).toBe(button);
  });

  it('hands the button to a host that listens', () => {
    service.listReportDocuments.mockReturnValue(of([aiDoc(71, 1)]));
    setUp();
    load(batteryRun());
    const requested: HTMLElement[] = [];
    component.downloadsRequested.subscribe(button => requested.push(button));

    const button = section().querySelector('.rr-ai-open-downloads') as HTMLButtonElement;
    button.click();
    expect(requested).toEqual([button]);
    expect(downloadCenterOpen).not.toHaveBeenCalled();
  });

  it('shows only the names until the document list answers', () => {
    const pending = new Subject<any>();
    service.listReportDocuments.mockReturnValue(pending.asObservable());
    setUp();
    load(batteryRun());

    expect(rows().length).toBe(2);
    expect(section().querySelector('.rr-ai-doc-status')).toBeNull();
    pending.next([aiDoc(71, 1)]);
    fixture.detectChanges();
    expect(rows().map(row => rowText(row, '.rr-ai-doc-status'))).toEqual(['Written', 'Not written']);
  });
});
