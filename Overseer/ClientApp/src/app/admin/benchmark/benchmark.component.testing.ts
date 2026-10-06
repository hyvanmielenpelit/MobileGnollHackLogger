import type { MockedObject } from 'vitest';
import { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import {
  AdminBenchmarkComponent, RUN_HISTORY_VIEW_STORAGE_KEY, RUN_REPORT_HEADER_STORAGE_KEY, RUN_REPORT_TAB_STORAGE_KEY
} from './benchmark.component';
import {
  AdminBenchmarkService, BenchmarkBatteryDto, BenchmarkBatteryRunDto, BenchmarkComparisonDto
} from '../../services/admin-benchmark.service';
import { SystemService } from '../../services/system.service';
import { BenchmarkPollTickerService } from '../../services/benchmark-poll-ticker.service';
import { IMAGE_DETAILS_STORAGE_KEY, KEY_FIGURES_STORAGE_KEY } from './run-report-frame/key-figures-image';
import {
  KEY_FIGURES_EXPORT_SECTIONS_STORAGE_KEY,
  KEY_FIGURES_EXPORT_STORAGE_KEY
} from './run-report-frame/key-figures-export-settings';
import { RUN_HISTORY_MEMBERS_STORAGE_KEY } from './benchmark.models';
import { PDFJS_LOADER } from '../../shared/pdf-viewer/pdfjs-loader';
import { BenchmarkWorkspaceStore } from './state/benchmark-workspace.store';
import { BenchmarkLauncherState } from './state/benchmark-launcher.state';
import { BenchmarkActiveRunMonitor } from './state/benchmark-active-run.monitor';
import { BenchmarkDifficultyJobService } from './state/benchmark-difficulty-job.service';
import { BenchmarkComparisonState } from './state/benchmark-comparison.state';
import { BenchmarkShellBridge } from './state/benchmark-shell-bridge.service';
import { BenchmarkViewSync } from './state/benchmark-view-sync.service';
import { BenchmarkRunTabComponent } from './run-tab/benchmark-run-tab.component';
import { BenchmarkHistoryTabComponent } from './history-tab/benchmark-history-tab.component';
import { BenchmarkSuitesTabComponent } from './suites-tab/benchmark-suites-tab.component';
import { BenchmarkProfilesTabComponent } from './profiles-tab/benchmark-profiles-tab.component';
import { BenchmarkComparisonTabComponent } from './comparison-tab/benchmark-comparison-tab.component';

// Spec helper for the AdminBenchmarkComponent spec files, which are split by area and share this
// setup. Imported by specs only.

/** The key AdminBenchmarkComponent remembers the last run setup under. */
export const RUN_SETTINGS_KEY = 'overseer_admin_benchmark_run_settings';

/** The key it remembers the Model Comparison selection under. */
export const COMPARISON_SELECTION_KEY = 'overseer_admin_benchmark_comparison_selection';

/** The key the Model Comparison launcher remembers its "How the comparison works" disclosure under. */
export const COMPARISON_LAUNCHER_KEY = 'overseer.benchmark.modelComparison.launcher';

/** What the service mock's `identifyComparison` and `renameComparison` answer. */
export const BENCHMARK_SPEC_COMPARISON: BenchmarkComparisonDto = {
  id: 12,
  name: 'Model 1 vs Model 2',
  customName: null,
  defaultName: 'Model 1 vs Model 2',
  entryCount: 2,
  subjectKind: 'Runs',
  entryKeys: ['run:1', 'run:2'],
  createdAtUtc: '2026-10-06T10:00:00Z',
  renamedAtUtc: null
};

export function clearStoredState(): void {
  // All are real browser state, so without this a spec that starts a run, picks a comparison or
  // chooses a run report tab leaks its selections into every spec that constructs the component afterwards.
  try {
    localStorage.removeItem(RUN_SETTINGS_KEY);
    localStorage.removeItem(COMPARISON_SELECTION_KEY);
    localStorage.removeItem(COMPARISON_LAUNCHER_KEY);
    localStorage.removeItem(RUN_REPORT_TAB_STORAGE_KEY);
    localStorage.removeItem(RUN_REPORT_HEADER_STORAGE_KEY);
    localStorage.removeItem(RUN_HISTORY_VIEW_STORAGE_KEY);
    localStorage.removeItem(RUN_HISTORY_MEMBERS_STORAGE_KEY);
    localStorage.removeItem(KEY_FIGURES_STORAGE_KEY);
    localStorage.removeItem(IMAGE_DETAILS_STORAGE_KEY);
    localStorage.removeItem(KEY_FIGURES_EXPORT_STORAGE_KEY);
    localStorage.removeItem(KEY_FIGURES_EXPORT_SECTIONS_STORAGE_KEY);
  } catch { /* private-browsing modes throw */ }
}

/** A runnable two-suite battery, weighted 75 / 25 under its declared scheme. */
export function buildBattery(overrides: Partial<BenchmarkBatteryDto> = {}): BenchmarkBatteryDto {
  return {
    id: 5, name: 'Core Battery', description: null, weightingScheme: 'DifficultyMass',
    weightingSchemeLabel: 'Questions and difficulty', revision: 1, definitionSha256: 'def-abc', isArchived: false,
    brokenSuiteNames: [], validationErrors: [], createdByUserName: 'admin',
    createdAtUtc: '2026-10-01T00:00:00Z', modifiedAtUtc: '2026-10-01T00:00:00Z',
    batteryRunCount: 0, hasActiveBatteryRun: false,
    suites: [
      {
        index: 0, suiteId: 1, suiteName: 'Default Suite', deleted: false, customWeight: null,
        questionCount: 15, assessedQuestionCount: 15, difficultyFullyAssessed: true, difficultyMass: 750
      },
      {
        index: 1, suiteId: 2, suiteName: 'Second Suite', deleted: false, customWeight: null,
        questionCount: 10, assessedQuestionCount: 10, difficultyFullyAssessed: true, difficultyMass: 250
      }
    ],
    weightPreviews: [
      { scheme: 'DifficultyMass', schemeLabel: 'Questions and difficulty', declared: true, weights: [0.75, 0.25] },
      { scheme: 'Equal', schemeLabel: 'Equal', declared: false, weights: [0.5, 0.5] }
    ],
    ...overrides
  };
}

/** Battery run 9 of battery 5, running suite 1 of 2 in round 1 of 1. */
export function buildBatteryRun(overrides: Partial<BenchmarkBatteryRunDto> = {}): BenchmarkBatteryRunDto {
  return {
    id: 9, batteryId: 5, batteryName: 'Core Battery', definitionRevision: 1, definitionSha256: 'def-abc',
    weightingScheme: 'DifficultyMass', suites: [], suiteCount: 2, runsPerSuite: 1, requestedMemberCount: 2,
    completedMemberCount: 0, failedMemberCount: 0, completedSuiteCount: 0, status: 'Running',
    stopReason: null, stopReasonText: null, allowCapWait: false, resumable: false, isDriving: true,
    startedAtUtc: '2026-10-02T00:00:00Z', currentSuitePosition: 1, currentSuiteName: 'Default Suite',
    currentRound: 1, currentRunId: null, slots: [], members: [],
    analysisStale: false, analysisHasExcludedMembers: false, postRunWork: 'None', repairingRunIds: [],
    ...overrides
  };
}

/** What createAdminBenchmarkFixture builds; each spec file keeps them in variables of its own. */
export interface AdminBenchmarkSpecContext {
  component: AdminBenchmarkComponent;
  fixture: ComponentFixture<AdminBenchmarkComponent>;
  benchmarkServiceMock: MockedObject<AdminBenchmarkService>;
  systemServiceMock: MockedObject<SystemService>;
  /** The state services AdminBenchmarkComponent provides. */
  workspace: BenchmarkWorkspaceStore;
  launcher: BenchmarkLauncherState;
  monitor: BenchmarkActiveRunMonitor;
  difficulty: BenchmarkDifficultyJobService;
  comparison: BenchmarkComparisonState;
  bridge: BenchmarkShellBridge;
  viewSync: BenchmarkViewSync;
  /** The sub-tab components; each exists only while its sub-tab is selected and rendered. */
  runTab(): BenchmarkRunTabComponent;
  historyTab(): BenchmarkHistoryTabComponent;
  suitesTab(): BenchmarkSuitesTabComponent;
  profilesTab(): BenchmarkProfilesTabComponent;
  comparisonTab(): BenchmarkComparisonTabComponent;
  /** Announces a service state change set directly by a test, then runs change detection. */
  refresh(): void;
}

/** The services and sub-tab accessors of a fixture of AdminBenchmarkComponent. */
export function benchmarkSpecHandles(fixture: ComponentFixture<AdminBenchmarkComponent>) {
  const injector = fixture.debugElement.injector;
  const tab = <T>(type: Type<T>): T => {
    const el = fixture.debugElement.query(By.directive(type));
    if (!el) {
      throw new Error(`${type.name} is not rendered; select its sub-tab and run change detection first.`);
    }
    return el.componentInstance as T;
  };
  const viewSync = injector.get(BenchmarkViewSync);
  return {
    workspace: injector.get(BenchmarkWorkspaceStore),
    launcher: injector.get(BenchmarkLauncherState),
    monitor: injector.get(BenchmarkActiveRunMonitor),
    difficulty: injector.get(BenchmarkDifficultyJobService),
    comparison: injector.get(BenchmarkComparisonState),
    bridge: injector.get(BenchmarkShellBridge),
    viewSync,
    runTab: () => tab(BenchmarkRunTabComponent),
    historyTab: () => tab(BenchmarkHistoryTabComponent),
    suitesTab: () => tab(BenchmarkSuitesTabComponent),
    profilesTab: () => tab(BenchmarkProfilesTabComponent),
    comparisonTab: () => tab(BenchmarkComparisonTabComponent),
    refresh: () => {
      viewSync.notify();
      fixture.detectChanges();
    }
  };
}

/** Configures the TestBed with the service mocks, creates the component and runs its first change detection. */
export async function createAdminBenchmarkFixture(): Promise<AdminBenchmarkSpecContext> {
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;
  let systemServiceMock: MockedObject<SystemService>;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let component: AdminBenchmarkComponent;

  benchmarkServiceMock = {
    getSuites: vi.fn().mockName("AdminBenchmarkService.getSuites"),
    getRuns: vi.fn().mockName("AdminBenchmarkService.getRuns"),
    getQuestions: vi.fn().mockName("AdminBenchmarkService.getQuestions"),
    getScoringProfiles: vi.fn().mockName("AdminBenchmarkService.getScoringProfiles"),
    createScoringProfile: vi.fn().mockName("AdminBenchmarkService.createScoringProfile"),
    updateScoringProfile: vi.fn().mockName("AdminBenchmarkService.updateScoringProfile"),
    startRun: vi.fn().mockName("AdminBenchmarkService.startRun"),
    getRun: vi.fn().mockName("AdminBenchmarkService.getRun"),
    getActiveRun: vi.fn().mockName("AdminBenchmarkService.getActiveRun"),
    cancelRun: vi.fn().mockName("AdminBenchmarkService.cancelRun"),
    rerunFailedQuestions: vi.fn().mockName("AdminBenchmarkService.rerunFailedQuestions"),
    deleteRun: vi.fn().mockName("AdminBenchmarkService.deleteRun"),
    createSuite: vi.fn().mockName("AdminBenchmarkService.createSuite"),
    updateSuite: vi.fn().mockName("AdminBenchmarkService.updateSuite"),
    deleteSuite: vi.fn().mockName("AdminBenchmarkService.deleteSuite"),
    duplicateSuite: vi.fn().mockName("AdminBenchmarkService.duplicateSuite"),
    getDefaultSuiteCatalog: vi.fn().mockName("AdminBenchmarkService.getDefaultSuiteCatalog"),
    importDefaultSuites: vi.fn().mockName("AdminBenchmarkService.importDefaultSuites"),
    getSuiteRunsFootprint: vi.fn().mockName("AdminBenchmarkService.getSuiteRunsFootprint"),
    deleteSuiteRuns: vi.fn().mockName("AdminBenchmarkService.deleteSuiteRuns"),
    reorderQuestions: vi.fn().mockName("AdminBenchmarkService.reorderQuestions"),
    startDifficultyAssessment: vi.fn().mockName("AdminBenchmarkService.startDifficultyAssessment"),
    getDifficultyAssessment: vi.fn().mockName("AdminBenchmarkService.getDifficultyAssessment"),
    getActiveDifficultyAssessment: vi.fn().mockName("AdminBenchmarkService.getActiveDifficultyAssessment"),
    cancelDifficultyAssessment: vi.fn().mockName("AdminBenchmarkService.cancelDifficultyAssessment"),
    reassessAnswer: vi.fn().mockName("AdminBenchmarkService.reassessAnswer"),
    reassessPanelAnswer: vi.fn().mockName("AdminBenchmarkService.reassessPanelAnswer"),
    rerunAnswer: vi.fn().mockName("AdminBenchmarkService.rerunAnswer"),
    rerunFinalSynthesis: vi.fn().mockName("AdminBenchmarkService.rerunFinalSynthesis"),
    retryFailedAssessments: vi.fn().mockName("AdminBenchmarkService.retryFailedAssessments"),
    rescoreRun: vi.fn().mockName("AdminBenchmarkService.rescoreRun"),
    trialReassessAnswer: vi.fn().mockName("AdminBenchmarkService.trialReassessAnswer"),
    calibrateAssessor: vi.fn().mockName("AdminBenchmarkService.calibrateAssessor"),
    getCalibrations: vi.fn().mockName("AdminBenchmarkService.getCalibrations"),
    getLastAssessor: vi.fn().mockName("AdminBenchmarkService.getLastAssessor"),
    retryClaimVerification: vi.fn().mockName("AdminBenchmarkService.retryClaimVerification"),
    getRunLimits: vi.fn().mockName("AdminBenchmarkService.getRunLimits"),
    startRunSeries: vi.fn().mockName("AdminBenchmarkService.startRunSeries"),
    getRunSeries: vi.fn().mockName("AdminBenchmarkService.getRunSeries"),
    getActiveRunSeries: vi.fn().mockName("AdminBenchmarkService.getActiveRunSeries"),
    cancelRunSeries: vi.fn().mockName("AdminBenchmarkService.cancelRunSeries"),
    resumeRunSeries: vi.fn().mockName("AdminBenchmarkService.resumeRunSeries"),
    getRunGroups: vi.fn().mockName("AdminBenchmarkService.getRunGroups"),
    createRunGroup: vi.fn().mockName("AdminBenchmarkService.createRunGroup"),
    updateRunGroup: vi.fn().mockName("AdminBenchmarkService.updateRunGroup"),
    previewRunGroupTier: vi.fn().mockName("AdminBenchmarkService.previewRunGroupTier"),
    getRunReportUrl: vi.fn().mockName("AdminBenchmarkService.getRunReportUrl"),
    getToolCallLogUrl: vi.fn().mockName("AdminBenchmarkService.getToolCallLogUrl"),
    compareModels: vi.fn().mockName("AdminBenchmarkService.compareModels"),
    getComparabilityIndex: vi.fn().mockName("AdminBenchmarkService.getComparabilityIndex"),
    importQuestions: vi.fn().mockName("AdminBenchmarkService.importQuestions"),
    importSuite: vi.fn().mockName("AdminBenchmarkService.importSuite"),
    uploadSuiteSnapshot: vi.fn().mockName("AdminBenchmarkService.uploadSuiteSnapshot"),
    deleteSnapshot: vi.fn().mockName("AdminBenchmarkService.deleteSnapshot"),
    getSnapshot: vi.fn().mockName("AdminBenchmarkService.getSnapshot"),
    getRunBoard: vi.fn().mockName("AdminBenchmarkService.getRunBoard"),
    getActiveQuestionGeneration: vi.fn().mockName("AdminBenchmarkService.getActiveQuestionGeneration"),
    getBoardFactsCheck: vi.fn().mockName("AdminBenchmarkService.getBoardFactsCheck"),
    listReportDocuments: vi.fn().mockName("AdminBenchmarkService.listReportDocuments"),
    writeRunReportDocuments: vi.fn().mockName("AdminBenchmarkService.writeRunReportDocuments"),
    getReportDocumentPdf: vi.fn().mockName("AdminBenchmarkService.getReportDocumentPdf"),
    getRunReportJob: vi.fn().mockName("AdminBenchmarkService.getRunReportJob"),
    cancelRunReportJob: vi.fn().mockName("AdminBenchmarkService.cancelRunReportJob"),
    estimateRunReports: vi.fn().mockName("AdminBenchmarkService.estimateRunReports"),
    deleteRunReportDocument: vi.fn().mockName("AdminBenchmarkService.deleteRunReportDocument"),
    reportDocumentPdfUrl: vi.fn().mockName("AdminBenchmarkService.reportDocumentPdfUrl"),
    getBatteries: vi.fn().mockName("AdminBenchmarkService.getBatteries"),
    getBattery: vi.fn().mockName("AdminBenchmarkService.getBattery"),
    createBattery: vi.fn().mockName("AdminBenchmarkService.createBattery"),
    updateBattery: vi.fn().mockName("AdminBenchmarkService.updateBattery"),
    deleteBattery: vi.fn().mockName("AdminBenchmarkService.deleteBattery"),
    archiveBattery: vi.fn().mockName("AdminBenchmarkService.archiveBattery"),
    getBatteryRuns: vi.fn().mockName("AdminBenchmarkService.getBatteryRuns"),
    deleteBatteryRun: vi.fn().mockName("AdminBenchmarkService.deleteBatteryRun"),
    getPairedComparison: vi.fn().mockName("AdminBenchmarkService.getPairedComparison"),
    getRunPairedComparison: vi.fn().mockName("AdminBenchmarkService.getRunPairedComparison"),
    getRunPairKinds: vi.fn().mockName("AdminBenchmarkService.getRunPairKinds"),
    getBatteryPairedComparison: vi.fn().mockName("AdminBenchmarkService.getBatteryPairedComparison"),
    writeBatteryReportDocuments: vi.fn().mockName("AdminBenchmarkService.writeBatteryReportDocuments"),
    getBatteryReportJob: vi.fn().mockName("AdminBenchmarkService.getBatteryReportJob"),
    cancelBatteryReportJob: vi.fn().mockName("AdminBenchmarkService.cancelBatteryReportJob"),
    estimateBatteryReports: vi.fn().mockName("AdminBenchmarkService.estimateBatteryReports"),
    deleteBatteryReportDocument: vi.fn().mockName("AdminBenchmarkService.deleteBatteryReportDocument"),    startBatteryRun: vi.fn().mockName("AdminBenchmarkService.startBatteryRun"),
    getActiveBatteryRun: vi.fn().mockName("AdminBenchmarkService.getActiveBatteryRun"),
    getBatteryRun: vi.fn().mockName("AdminBenchmarkService.getBatteryRun"),
    cancelBatteryRun: vi.fn().mockName("AdminBenchmarkService.cancelBatteryRun"),
    resumeBatteryRun: vi.fn().mockName("AdminBenchmarkService.resumeBatteryRun"),
    analyseBatteryRun: vi.fn().mockName("AdminBenchmarkService.analyseBatteryRun"),
    getBatteryAnalysis: vi.fn().mockName("AdminBenchmarkService.getBatteryAnalysis"),
    getBatteryReportUrl: vi.fn().mockName("AdminBenchmarkService.getBatteryReportUrl"),
    getBatteryLeaderboard: vi.fn().mockName("AdminBenchmarkService.getBatteryLeaderboard"),
    previewBatteryReuse: vi.fn().mockName("AdminBenchmarkService.previewBatteryReuse"),
    attachBatteryMember: vi.fn().mockName("AdminBenchmarkService.attachBatteryMember"),
    getBatteryAttachCandidates: vi.fn().mockName("AdminBenchmarkService.getBatteryAttachCandidates"),
    identifyComparison: vi.fn().mockName("AdminBenchmarkService.identifyComparison"),
    renameComparison: vi.fn().mockName("AdminBenchmarkService.renameComparison")
  } as unknown as MockedObject<AdminBenchmarkService>;

  // The comparison wizard numbers every computed comparison, and its header can rename it.
  benchmarkServiceMock.identifyComparison.mockReturnValue(of(BENCHMARK_SPEC_COMPARISON));
  benchmarkServiceMock.renameComparison.mockReturnValue(of(BENCHMARK_SPEC_COMPARISON));

  // ngOnInit loads the launcher's batteries and reattaches a live battery run; the Multi-Suite tab
  // and the Battery Progress dialog read the rest.
  benchmarkServiceMock.getBatteries.mockReturnValue(of([]));
  benchmarkServiceMock.getActiveBatteryRun.mockReturnValue(of(null));
  benchmarkServiceMock.getBatteryRuns.mockReturnValue(of([]));
  benchmarkServiceMock.getRunPairKinds.mockReturnValue(of([]));
  benchmarkServiceMock.getBatteryReportJob.mockReturnValue(of(null));
  benchmarkServiceMock.getBatteryRun.mockReturnValue(of(buildBatteryRun()));
  benchmarkServiceMock.getBatteryAnalysis.mockReturnValue(of(null));
  benchmarkServiceMock.getBatteryLeaderboard.mockReturnValue(of({ definitionSha256: 'def-abc', classes: [], incomplete: [] }));
  benchmarkServiceMock.getBatteryReportUrl.mockReturnValue('/api/admin/benchmark/batteries/runs/9/report');

  benchmarkServiceMock.getActiveQuestionGeneration.mockReturnValue(of(null));
  // The run report's AI Reports tab lists the run's AI-written reports whenever it loads a run,
  // estimates the cost of a missing one and follows a writing job.
  benchmarkServiceMock.listReportDocuments.mockReturnValue(of([]));
  benchmarkServiceMock.getRunReportJob.mockReturnValue(of(null));
  benchmarkServiceMock.estimateRunReports.mockReturnValue(of({
    estimates: [], estimatedTotalCostUsd: null, refusal: null, sameProviderWarning: null
  }));
  benchmarkServiceMock.deleteRunReportDocument.mockReturnValue(of(undefined));
  benchmarkServiceMock.reportDocumentPdfUrl.mockReturnValue('/api/admin/benchmark/report-documents/0/render/pdf');
  benchmarkServiceMock.getBoardFactsCheck.mockReturnValue(of(null));

  benchmarkServiceMock.getActiveDifficultyAssessment.mockReturnValue(of(null));
  benchmarkServiceMock.getActiveRun.mockReturnValue(of(null));
  // ngOnInit reads the caps and reattaches a live series, and entering Run History loads the
  // groups for the group column. All three run on paths every test in this file goes through.
  benchmarkServiceMock.getActiveRunSeries.mockReturnValue(of(null));
  benchmarkServiceMock.getRunSeries.mockReturnValue(of({ id: 1, status: 'Running', completedRunCount: 0, requestedRunCount: 1, members: [] } as any));
  benchmarkServiceMock.getRunGroups.mockReturnValue(of([]));
  benchmarkServiceMock.getRunLimits.mockReturnValue(of({
    maxRunsPerHour: 4,
    maxRunsPerDay: 20,
    runsInLastHour: 0,
    runsInLast24Hours: 0,
    remainingDailyHeadroom: 20,
    maxRunCountPerSeries: 20
  }));
  benchmarkServiceMock.getRun.mockReturnValue(of({ id: 1, answers: [] } as any));
  benchmarkServiceMock.getQuestions.mockReturnValue(of([]));
  benchmarkServiceMock.getSuiteRunsFootprint.mockReturnValue(of({ runCount: 0, totalAnswerCharacters: 0 }));
  benchmarkServiceMock.getCalibrations.mockReturnValue(of([]));
  benchmarkServiceMock.getDefaultSuiteCatalog.mockReturnValue(of([]));
  // A suite with no completed run has no assessor to differ from, which is not an error.
  benchmarkServiceMock.getLastAssessor.mockReturnValue(of({}));
  benchmarkServiceMock.getSuites.mockReturnValue(of([
    { id: 1, name: 'Default Suite', description: 'Test', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null, questionCount: 15, assessedQuestionCount: 15, difficultyFullyAssessed: true }
  ]));
  benchmarkServiceMock.getScoringProfiles.mockReturnValue(of([
    {
      id: 1,
      name: 'Default Intelligence Profile',
      isDefault: true,
      weightAccuracy: 0.55,
      weightCompleteness: 0.25,
      weightConciseness: 0.10,
      weightReadability: 0.10,
      levelScoresJson: '[1, 15, 35, 55, 72, 87, 100]',
      criticalErrorCeiling: 25,
      secondOpinionQualityThreshold: 50,
      secondOpinionMode: 1,
      secondOpinionOutlierDeltaPoints: 25,
      speedTargetMs: 15000,
      speedDecayK: 20.0,
      speedDifficultyScaling: 1.0,
      maxParallelQuestions: 1,
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: '2026-09-01T00:00:00Z'
    }
  ]));
  benchmarkServiceMock.getRuns.mockReturnValue(of([]));
  benchmarkServiceMock.compareModels.mockReturnValue(of({ entries: [] } as any));
  benchmarkServiceMock.getComparabilityIndex.mockReturnValue(of({
    computedAtUtc: '2026-09-07T12:00:00Z',
    entries: [],
    conditions: [],
    largestConditionKeys: [],
    referenceSelectionRule: 'The reference condition is the one with the most sources.',
    mustMatchKeyNames: ['BenchmarkSuiteId'],
    modelAxisKeyNames: ['ModelId'],
    degradingKeyNames: ['PricingSnapshot']
  } as any));

  systemServiceMock = {
    getVersion: vi.fn().mockName("SystemService.getVersion")
  } as unknown as MockedObject<SystemService>;
  systemServiceMock.getVersion.mockReturnValue(of('1.0.29'));

  // BenchmarkPollTickerService prefers a real Worker when one exists, which ChromeHeadless does,
  // but a worker fetching '/workers/benchmark-poll-ticker.js' from the test server is not the
  // same thing this suite's many fakeAsync/tick()-driven polling specs need: a deterministic,
  // zone-visible timer. Every spec in this file gets the plain setInterval fallback instead, so
  // polling behaves exactly as it did before the ticker existed; BenchmarkPollTickerService's own
  // spec file is what actually exercises the worker path and its post-start fallback.
  vi.spyOn(BenchmarkPollTickerService.prototype, 'start').mockImplementation((intervalMs: number, onTick: () => void) => {
    const id = setInterval(onTick, intervalMs);
    const handle = (() => clearInterval(id)) as any;
    Object.defineProperty(handle, 'mode', { value: 'timer', enumerable: true });
    return handle;
  });

  await TestBed.configureTestingModule({
    imports: [AdminBenchmarkComponent],
    providers: [
      { provide: AdminBenchmarkService, useValue: benchmarkServiceMock },
      { provide: SystemService, useValue: systemServiceMock },
      // The AI Reports tab hosts the PDF viewer; the test runner never loads pdf.js.
      { provide: PDFJS_LOADER, useValue: () => Promise.reject(new Error('pdf.js is not loaded in specs')) },
      provideHttpClient(),
      provideHttpClientTesting()
    ]
  }).compileComponents();

  fixture = TestBed.createComponent(AdminBenchmarkComponent);
  component = fixture.componentInstance;
  component.systemConfigs = [
    {
      id: 1,
      displayName: 'Test Model',
      displayNameMode: null,
      provider: 'Anthropic',
      modelId: 'claude-3-5-sonnet',
      thinkingLevel: null,
      reasoningMode: null,
      reasoningSummary: null,
      serviceTier: null,
      maxInputTokens: null,
      maxOutputTokens: null,
      orderIndex: 0,
      isEnabled: true,
      hasApiKey: true,
      isSystemWide: false,
      maxDailyChatRequests: null,
      maxMonthlyChatRequests: null,
      maxTotalChatRequests: null,
      dailyChatRequestsCount: 0,
      monthlyChatRequestsCount: 0,
      totalChatRequestsCount: 0,
      maxDailyTitleRequests: null,
      maxMonthlyTitleRequests: null,
      maxTotalTitleRequests: null,
      dailyTitleRequestsCount: 0,
      monthlyTitleRequestsCount: 0,
      totalTitleRequestsCount: 0,
      maxDailyChatTokens: null,
      maxMonthlyChatTokens: null,
      maxTotalChatTokens: null,
      dailyChatTokensCount: 0,
      monthlyChatTokensCount: 0,
      totalChatTokensCount: 0,
      maxDailyTitleTokens: null,
      maxMonthlyTitleTokens: null,
      maxTotalTitleTokens: null,
      dailyTitleTokensCount: 0,
      monthlyTitleTokensCount: 0,
      totalTitleTokensCount: 0,
      modelRole: 7,
      parallelExecutionMode: 2,
      apiKey: '',
      note: null
    }
  ];
  fixture.detectChanges();

  return { component, fixture, benchmarkServiceMock, systemServiceMock, ...benchmarkSpecHandles(fixture) };
}
