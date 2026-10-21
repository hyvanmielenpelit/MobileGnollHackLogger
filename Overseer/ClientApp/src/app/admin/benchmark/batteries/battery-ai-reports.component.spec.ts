import type { Mock, MockedObject } from "vitest";
import { Component } from '@angular/core';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, flush, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkRootCauseReadinessDto,
  BenchmarkRunReportEstimateDto,
  BenchmarkRunReportJobDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { toModelPickerOptions } from '../../../shared/model-picker/model-picker.component';
import { PdfViewerDialogComponent } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { PDFJS_LOADER } from '../../../shared/pdf-viewer/pdfjs-loader';
import { BenchmarkDownloadCenterComponent, rememberedPdfPaper } from '../download-center/benchmark-download-center.component';
import { reportDisclosureInfo } from '../report-disclosure-guide';
import { recommendedRun, rootCauseControl, rootCauseControls } from '../report-pack/root-cause.testing';
import { RunAiReportsComponent } from '../run-ai-reports/run-ai-reports.component';
import { BenchmarkShellBridge, LauncherSetupPrefill } from '../state/benchmark-shell-bridge.service';
import { RunReportWritingDialogComponent } from '../run-ai-reports/run-report-writing-dialog.component';
import {
  REPORT_PACK_STORAGE_KEY,
  REPORT_WRITER_MEMORY_KEY,
  rememberReportWriterId,
  rememberedReportWriterId
} from '../run-ai-reports/report-writer-memory';
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
    jobsAhead: null, blockingJobLabel: null, audiences: [1, 5], writerConfigId: 1, writerDisplayName: 'Test Writer',
    writerProvider: 'Anthropic', writerModelId: 'claude-3-5-sonnet', writerThinkingLevel: null,
    job: null as any, serverTimeUtc: '2026-10-02T10:00:10Z',
    ...overrides
  };
}

/** The summary readiness of battery run 7's stored Root Cause Analysis #72. */
function readiness(state: BenchmarkRootCauseReadinessDto['state']): BenchmarkRootCauseReadinessDto {
  return {
    state,
    rootCauseDocumentId: state === 'missing' ? null : 72,
    message: state === 'usable' ? null : `The server says the Root Cause Analysis is ${state}.`,
    code: state === 'usable' ? null : state === 'missing' ? 'rootCauseMissing' : 'rootCauseOutdated'
  };
}

function estimateDto(overrides: Partial<BenchmarkRunReportEstimateDto> = {}): BenchmarkRunReportEstimateDto {
  return {
    estimates: [
      { audience: 1, promptChars: 1000, estimatedInputTokens: 900, estimatedOutputTokens: 2000, estimatedCostUsd: 0.06 },
      { audience: 5, promptChars: 3000, estimatedInputTokens: 2700, estimatedOutputTokens: 7000, estimatedCostUsd: 0.2 }
    ],
    estimatedTotalCostUsd: 0.26,
    refusal: null,
    sameProviderWarning: null,
    ...overrides
  };
}

/** The run report's AI Reports tab beside two battery run report tabs, one of them not writable yet. */
@Component({
  standalone: true,
  imports: [RunAiReportsComponent, BatteryAiReportsComponent],
  template: `
    <app-run-ai-reports [run]="run" [pickerOptions]="options" [writerConfigs]="configs" [dialogOpen]="true"></app-run-ai-reports>
    <app-battery-ai-reports [batteryRun]="batteryRuns[0]" [analysis]="latest" [pickerOptions]="options"
                            [writerConfigs]="configs"></app-battery-ai-reports>
    <app-battery-ai-reports [batteryRun]="batteryRuns[1]" [analysis]="null" [pickerOptions]="options"
                            [writerConfigs]="configs"></app-battery-ai-reports>
  `
})
class AiReportsTabsHostComponent {
  readonly run = {
    id: 55, suiteName: 'Default Suite', testedModelDisplayNameUsed: 'Test Model', testedModelProviderUsed: 'OpenAI',
    testedModelIdUsed: 'gpt-test', status: 'Completed', assessmentJson: '{}', answers: []
  } as any;
  readonly batteryRuns = [batteryRun(), batteryRun({ id: 8, status: 'Running' })];
  readonly latest = analysis();
  readonly options = OPTIONS;
  readonly configs = CONFIGS;
}

describe('BatteryAiReportsComponent', () => {
  let fixture: ComponentFixture<BatteryAiReportsComponent>;
  let component: BatteryAiReportsComponent;
  let service: MockedObject<AdminBenchmarkService>;
  let writingOpen: Mock;
  let viewerOpen: Mock;
  let downloadCenterOpen: Mock;

  beforeEach(async () => {
    localStorage.removeItem(REPORT_WRITER_MEMORY_KEY);
    localStorage.removeItem(REPORT_PACK_STORAGE_KEY);
    service = {
      listReportDocuments: vi.fn().mockName("AdminBenchmarkService.listReportDocuments"),
      getBatterySummaryReadiness: vi.fn().mockName("AdminBenchmarkService.getBatterySummaryReadiness"),
      getRunSummaryReadiness: vi.fn().mockName("AdminBenchmarkService.getRunSummaryReadiness"),
      writeBatteryReportDocuments: vi.fn().mockName("AdminBenchmarkService.writeBatteryReportDocuments"),
      getReportDocumentPdf: vi.fn().mockName("AdminBenchmarkService.getReportDocumentPdf"),
      reportDocumentPdfUrl: vi.fn().mockName("AdminBenchmarkService.reportDocumentPdfUrl"),
      getBatteryReportJob: vi.fn().mockName("AdminBenchmarkService.getBatteryReportJob"),
      cancelBatteryReportJob: vi.fn().mockName("AdminBenchmarkService.cancelBatteryReportJob"),
      getBatteryReportWriterReplies: vi.fn().mockName("AdminBenchmarkService.getBatteryReportWriterReplies"),
      getBatteryRun: vi.fn().mockName("AdminBenchmarkService.getBatteryRun"),
      estimateBatteryReports: vi.fn().mockName("AdminBenchmarkService.estimateBatteryReports"),
      deleteBatteryReportDocument: vi.fn().mockName("AdminBenchmarkService.deleteBatteryReportDocument"),
      getBatteryReportUrl: vi.fn().mockName("AdminBenchmarkService.getBatteryReportUrl"),
      getRunRootCauseControls: vi.fn().mockName("AdminBenchmarkService.getRunRootCauseControls"),
      getBatteryRootCauseControls: vi.fn().mockName("AdminBenchmarkService.getBatteryRootCauseControls"),
      getReportDocument: vi.fn().mockName("AdminBenchmarkService.getReportDocument"),
      redrawReportDocumentCharts: vi.fn().mockName("AdminBenchmarkService.redrawReportDocumentCharts")
    } as unknown as MockedObject<AdminBenchmarkService>;
    service.listReportDocuments.mockReturnValue(of([]));
    service.getBatterySummaryReadiness.mockReturnValue(of(readiness('usable')));
    service.getRunSummaryReadiness.mockReturnValue(of(readiness('usable')));
    service.getRunRootCauseControls.mockReturnValue(of(rootCauseControls({ controlRun: null })));
    service.getBatteryRootCauseControls.mockReturnValue(of(rootCauseControls({ controlRun: null })));
    service.getReportDocument.mockReturnValue(of({ controls: [], recommendedRuns: [], checks: [] } as any));
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
      imports: [BatteryAiReportsComponent, AiReportsTabsHostComponent],
      providers: [
        BenchmarkShellBridge,
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
    localStorage.removeItem(REPORT_WRITER_MEMORY_KEY);
    localStorage.removeItem(REPORT_PACK_STORAGE_KEY);
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

  it('lists every document as not written, from the battery run\'s battery-completion documents', () => {
    setUp();
    load(batteryRun());

    expect(service.listReportDocuments).toHaveBeenCalledWith({ subject: 'battery:7', origin: 'batteryCompletion' });
    expect(status()).toBe('');
    expect(rows().map(row => rowText(row, '.rr-ai-doc-name')))
      .toEqual(['Root Cause Analysis', 'Executive Summary']);
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
      aiDoc(72, 5, {
        runChangedSinceGeneration: true, createdAtUtc: '2026-10-02T11:00:00Z', writerDisplayName: 'Writer B',
        status: 'CompletedWithWarnings', durationMs: 72000, costUsd: 0.08, sameProviderAcknowledged: true
      }),
      aiDoc(71, 1),
      // A document of a retired type is never listed.
      aiDoc(74, 3),
      // A Report Pack document about the same battery run is not one of its AI-written reports.
      aiDoc(73, 1, { origin: 1, createdAtUtc: '2026-10-03T00:00:00Z' })
    ]));
    setUp();
    load(batteryRun({ reportDocumentsStatus: 4 }));

    const list = rows();
    expect(list.map(row => rowText(row, '.rr-ai-doc-status'))).toEqual(['Written with warnings', 'Written']);
    expect(list[0].querySelector('.rr-ai-doc-status')?.classList).toContain('is-warning');
    expect(list.map(row => row.getAttribute('data-document-id'))).toEqual(['72', '71']);
    expect(list.map(row => rowText(row, '.rr-ai-doc-meta'))).toEqual([
      'by Writer B on 2026-10-02 11:00 UTC · 1 min 12 s · $0.08 · same provider, acknowledged',
      'by Test Writer on 2026-10-02 10:15 UTC'
    ]);
    expect(list[1].querySelector('.gh-tag-changed')).toBeNull();
    expect(list[0].querySelector('.gh-tag-changed')?.textContent?.trim()).toBe('A member run changed since this document was written');
    const views = list.map(row => row.querySelector('button.rr-ai-doc-view') as HTMLButtonElement);
    expect(views.map(button => button.getAttribute('aria-label')))
      .toEqual(['View the Root Cause Analysis', 'View the Executive Summary']);
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
  });

  it('says an Executive Summary written alone is written from the battery run\'s stored Root Cause Analysis', () => {
    service.listReportDocuments.mockReturnValue(of([aiDoc(72, 5)]));
    service.writeBatteryReportDocuments.mockReturnValue(of({ runId: 7, status: 1 }));
    setUp();
    load(batteryRun({ reportWriterModelConfigurationId: 1 }));

    expect(service.getBatterySummaryReadiness).toHaveBeenCalledWith(7);
    expect(checkbox(1).checked).toBe(true);
    expect(checkbox(1).getAttribute('aria-describedby')).toBe(`${component.idPrefix}EsReason`);
    expect(byId('EsReason')?.textContent?.trim())
      .toBe('Written from Root Cause Analysis #72, which was written from this battery run\'s current data.');
    expect(section().querySelector('.rr-ai-order-hint')).toBeNull();

    writeButton().click();
    expect(service.writeBatteryReportDocuments).toHaveBeenCalledWith(7, { writerModelConfigurationId: 1, audiences: [1] });
  });

  it('words an outdated stored Root Cause Analysis for the battery run, and rewrites it with the Executive Summary', () => {
    service.listReportDocuments.mockReturnValue(of([aiDoc(72, 5)]));
    service.getBatterySummaryReadiness.mockReturnValue(of(readiness('outdated')));
    service.writeBatteryReportDocuments.mockReturnValue(of({ runId: 7, status: 1 }));
    setUp();
    load(batteryRun({ reportWriterModelConfigurationId: 1 }));

    expect(checkbox(1).getAttribute('aria-disabled')).toBe('true');
    expect(byId('EsReason')?.textContent?.trim()).toBe('Root Cause Analysis #72 was written from different data than this battery '
      + 'run has now. Check Root Cause Analysis — rewrite to write both.');
    const rewrite = byId('RootCauseRewrite') as HTMLInputElement;
    expect(rewrite.checked).toBe(false);
    rewrite.click();
    fixture.detectChanges();
    expect(checkbox(1).checked).toBe(true);

    writeButton().click();
    expect(service.writeBatteryReportDocuments).toHaveBeenCalledWith(7, {
      writerModelConfigurationId: 1, audiences: [5, 1], replaceDocumentIds: [72]
    });
  });

  it('blocks the Executive Summary while the Root Cause Analysis is unchecked, in the battery run\'s words', () => {
    setUp();
    load(batteryRun({ reportWriterModelConfigurationId: 1 }));

    checkbox(5).click();
    fixture.detectChanges();
    expect(checkbox(1).checked).toBe(false);
    expect(checkbox(1).getAttribute('aria-disabled')).toBe('true');
    expect(byId('EsReason')?.textContent?.trim()).toBe('Needs the Root Cause Analysis: check Root Cause Analysis to write both.');
    expect(status()).toBe('The Executive Summary was unchecked: it is written from the Root Cause Analysis.');

    checkbox(1).click();
    fixture.detectChanges();
    expect(byId('EsError')?.getAttribute('role')).toBe('alert');
    (byId('EsError')!.querySelector('button.rr-ai-es-fix') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(checkbox(5).checked).toBe(true);
    expect(checkbox(1).checked).toBe(true);
    expect(document.activeElement).toBe(checkbox(1));
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

  it('opens a battery run without its own writer on the writer used last, unless the candidate\'s provider shares it', () => {
    rememberReportWriterId(8);
    setUp();
    load(batteryRun());
    expect(component.writerConfigId).toBe(8);

    rememberReportWriterId(9);
    setUp();
    load(batteryRun());
    expect(component.writerConfigId).toBeNull();

    // The battery run's own writer comes first.
    rememberReportWriterId(8);
    setUp();
    load(batteryRun({ reportWriterModelConfigurationId: 1 }));
    expect(component.writerConfigId).toBe(1);
  });

  it('remembers a writer chosen in its picker', () => {
    setUp();
    load(batteryRun());
    component.selectWriter(CONFIGS[1]);
    expect(rememberedReportWriterId()).toBe(8);
  });

  it('chooses the default again when the configurations arrive after the battery run, but never over the user\'s choice', () => {
    setUp();
    fixture.componentRef.setInput('writerConfigs', []);
    load(batteryRun({ reportWriterModelConfigurationId: 1 }));
    expect(component.writerConfigId).toBeNull();

    fixture.componentRef.setInput('writerConfigs', CONFIGS);
    fixture.detectChanges();
    expect(component.writerConfigId).toBe(1);

    setUp();
    fixture.componentRef.setInput('writerConfigs', [CONFIGS[1]]);
    load(batteryRun({ id: 8, reportWriterModelConfigurationId: 1 }));
    component.selectWriter(CONFIGS[1]);
    fixture.componentRef.setInput('writerConfigs', CONFIGS);
    fixture.detectChanges();
    expect(component.writerConfigId).toBe(8);
  });

  it('explains the documents and the writer choice in its own Report writer info tip', () => {
    setUp();
    load(batteryRun());

    const tip = byId('ReportWriterHint') as HTMLElement;
    const groups = Array.from(tip.querySelectorAll('dl > div')) as HTMLElement[];
    expect(groups.map(group => group.querySelector('.gh-info-term')?.textContent?.trim()))
      .toEqual(['Executive Summary', 'Root Cause Analysis', 'Every document']);
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
    expect(service.estimateBatteryReports).toHaveBeenCalledWith(7, { writerModelConfigurationId: 1, audiences: [5, 1] });
    const estimate = byId('WriteEstimate') as HTMLElement;
    expect(estimate.classList).toContain('gh-estimate-panel');
    expect(estimate.getAttribute('role')).toBe('status');
    expect(estimate.querySelector('.gh-estimate-total')?.textContent?.trim()).toBe('about $0.26');
    expect(writeButton().getAttribute('aria-describedby')).toBe(`${component.idPrefix}WriteEstimate`);
    expect(writeButton().textContent?.trim()).toBe('Write Reports');
    expect(writeButton().querySelector('svg.btn-icon')?.getAttribute('aria-hidden')).toBe('true');

    writeButton().click();
    fixture.detectChanges();

    expect(service.writeBatteryReportDocuments).toHaveBeenCalledWith(7, { writerModelConfigurationId: 1, audiences: [5, 1] });
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
    service.getBatteryReportWriterReplies.mockReturnValue(of('# Replies\n'));
    context.source!.getWriterReplies!().subscribe();
    expect(service.getBatteryReportJob).toHaveBeenCalledWith(7);
    expect(service.cancelBatteryReportJob).toHaveBeenCalledWith(7);
    expect(service.getBatteryRun).toHaveBeenCalledWith(7);
    expect(service.getBatteryReportWriterReplies).toHaveBeenCalledWith(7);

    // The status poll sees the job finish and lists the documents again.
    service.getBatteryReportJob.mockReturnValue(of(jobView(3)));
    service.listReportDocuments.mockReturnValue(of([aiDoc(71, 1), aiDoc(72, 5)]));
    tick(BATTERY_REPORT_DOCUMENTS_POLL_MS);
    fixture.detectChanges();
    expect(status()).toBe('');
    expect(section().querySelectorAll('.rr-ai-doc-view').length).toBe(2);
    expect(section().querySelector('.rr-ai-write')).toBeNull();
    expect(changes.map(change => change.status)).toEqual([1, 3]);
    flush();
    discardPeriodicTasks();
  }));

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

  it('hands its member diagnostics callback to its own Download Center', () => {
    service.listReportDocuments.mockReturnValue(of([aiDoc(71, 1)]));
    setUp();
    const memberDiagnosticsText = (run: { id: number }): string => `diagnostics of run ${run.id}`;
    fixture.componentRef.setInput('memberDiagnosticsText', memberDiagnosticsText);
    load(batteryRun());

    (section().querySelector('.rr-ai-open-downloads') as HTMLButtonElement).click();

    expect(vi.mocked(downloadCenterOpen).mock.lastCall![0]).toEqual({
      kind: 'battery', batteryRunId: 7, label: 'Core Battery · Test Model', memberDiagnosticsText
    });
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

  // --- Beside the run report's tab ---

  it('gives every element an id no other AI Reports tab in the document uses', () => {
    service.listReportDocuments.mockImplementation((query: any) => of(query?.runId != null
      ? [{ ...aiDoc(55, 1), subjectKey: 'run:55', subjectRunIds: [55], origin: 2 }]
      : [aiDoc(query.subject === 'battery:7' ? 71 : 81, 1, { subjectKey: query.subject })]));
    const host = TestBed.createComponent(AiReportsTabsHostComponent);
    host.detectChanges();

    const batteries = host.debugElement.queryAll(By.directive(BatteryAiReportsComponent))
      .map(element => element.componentInstance as BatteryAiReportsComponent);
    const ids = (Array.from(host.nativeElement.querySelectorAll('[id]')) as HTMLElement[])
      .map(element => element.id)
      .filter(id => id !== '');
    for (const id of ['rrAudience5', 'rrWriteEstimate', 'rr-ai-tip-delete-1', 'rrAiDeleteTitle', 'rwTitle',
      `${batteries[0].idPrefix}Audience5`, `${batteries[0].idPrefix}-tip-delete-1`, `${batteries[0].idPrefix}RwTitle`,
      `${batteries[1].idPrefix}NotWritable`, `${batteries[1].idPrefix}DeleteTitle`]) {
      expect(ids).toContain(id);
    }
    expect(ids.filter((id, index) => ids.indexOf(id) !== index)).toEqual([]);
  });

  // --- The Root Cause Analysis ---

  describe('the Root Cause Analysis', () => {
    let prefills: LauncherSetupPrefill[];

    beforeEach(() => {
      prefills = [];
      TestBed.inject(BenchmarkShellBridge).prefillSetup$.subscribe(prefill => prefills.push(prefill));
    });

    it('labels the control run by the battery run', () => {
      service.getBatteryRootCauseControls.mockReturnValue(of(rootCauseControls()));
      service.listReportDocuments.mockReturnValue(of([aiDoc(71, 1)]));
      setUp();
      load(batteryRun());

      expect(service.getBatteryRootCauseControls).toHaveBeenCalledWith(7);
      (section().querySelector('.rr-ai-write .rcc-set-up') as HTMLButtonElement).click();

      expect(prefills.map(prefill => prefill.sourceLabel)).toEqual(['Control run for battery run #7']);
    });

    it('names battery-run controls before writing', () => {
      service.getBatteryRootCauseControls.mockReturnValue(of(rootCauseControls({
        controls: [rootCauseControl({ runId: null, batteryRunId: 31 })], controlLimit: 'Moderate', controlRun: null
      })));
      setUp();
      load(batteryRun());

      expect(section().querySelector('.rr-ai-write .rcc-line')?.textContent?.trim())
        .toBe('The Root Cause Analysis will use 1 control: GPT-6 Luna (high), battery run #31 (2026-10-10).');
    });

    it('hosts the next runs, redraws the charts and rewrites with controls on the written Root Cause Analysis', () => {
      service.listReportDocuments.mockReturnValue(of([aiDoc(71, 1), aiDoc(72, 5)]));
      service.getReportDocument.mockReturnValue(of({ controls: [], recommendedRuns: [recommendedRun()], checks: [] } as any));
      service.getBatteryRootCauseControls.mockReturnValue(of(rootCauseControls({
        controls: [rootCauseControl({ runId: null, batteryRunId: 31 })], controlLimit: 'Moderate', controlRun: null
      })));
      service.redrawReportDocumentCharts.mockReturnValue(of({ documentId: 72, chartCount: 8, figureKeys: [], settingsHash: null }));
      service.writeBatteryReportDocuments.mockReturnValue(of({ runId: 7, status: 1 }));
      setUp();
      load(batteryRun({ reportWriterModelConfigurationId: 1 }));
      const row = rows()[0];

      const details = row.querySelector('app-root-cause-runs details') as HTMLDetailsElement;
      details.open = true;
      details.dispatchEvent(new Event('toggle'));
      fixture.detectChanges();
      (row.querySelector('.rcr-set-up') as HTMLButtonElement).click();
      expect(prefills.map(prefill => prefill.sourceLabel)).toEqual(['Set up from Root Cause Analysis #72, run 1']);

      (row.querySelector('.rr-ai-doc-redraw') as HTMLButtonElement).click();
      expect(service.redrawReportDocumentCharts).toHaveBeenCalledWith(72);

      expect(row.querySelector('.rcc-new')?.textContent).toContain('(battery run #31)');
      (row.querySelector('.rcc-rewrite') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(service.estimateBatteryReports).toHaveBeenLastCalledWith(7, { writerModelConfigurationId: 1, audiences: [5] });
      buttonIn(fixture.nativeElement.querySelector('.rr-ai-rewrite-dialog'), 'Rewrite').click();

      expect(service.writeBatteryReportDocuments).toHaveBeenCalledWith(7, {
        writerModelConfigurationId: 1, audiences: [5], replaceDocumentIds: [72]
      });
    });
  });
});
