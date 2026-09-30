import { ChangeDetectorRef } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick, discardPeriodicTasks, flush } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { ModelPickerComponent } from '../../shared/model-picker/model-picker.component';
import { of, throwError, Subject } from 'rxjs';
import {
  AdminBenchmarkComponent, RUN_HISTORY_VIEW_STORAGE_KEY, RUN_REPORT_HEADER_STORAGE_KEY, RUN_REPORT_TAB_STORAGE_KEY
} from './benchmark.component';
import { MarkdownEditorComponent } from '../../shared/markdown-editor/markdown-editor.component';
import { MultiRunComponent } from './multi-run/multi-run.component';
import { AdminBenchmarkService, BenchmarkRunAnswerDto, BenchmarkRunReportDocumentsStatus } from '../../services/admin-benchmark.service';
import { SystemService } from '../../services/system.service';
import { BenchmarkCompletionSoundService } from '../../services/benchmark-completion-sound.service';
import { BenchmarkCompletionNotificationService } from '../../services/benchmark-completion-notification.service';
import { BenchmarkBackgroundActivityService } from '../../services/benchmark-background-activity.service';
import { BenchmarkPollTickerService } from '../../services/benchmark-poll-ticker.service';
import { serializeQuestionsYaml } from './question-yaml/question-yaml-format';
import { COMPARISON_WIZARD_STEPS } from './model-comparison/model-comparison.component';
import { IMAGE_DETAILS_STORAGE_KEY, KEY_FIGURES_STORAGE_KEY, keyFiguresImageIo } from './run-report-frame/key-figures-image';
import { PDFJS_LOADER } from '../../shared/pdf-viewer/pdfjs-loader';
import { ReportDocumentsLauncherComponent } from './report-pack/report-documents-launcher.component';

describe('AdminBenchmarkComponent', () => {
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: jasmine.SpyObj<AdminBenchmarkService>;
  let systemServiceMock: jasmine.SpyObj<SystemService>;

  /** The key AdminBenchmarkComponent remembers the last run setup under. */
  const RUN_SETTINGS_KEY = 'overseer_admin_benchmark_run_settings';

  /** The key it remembers the Model Comparison selection under. */
  const COMPARISON_SELECTION_KEY = 'overseer_admin_benchmark_comparison_selection';

  /** The key the Model Comparison launcher remembers its "How the comparison works" disclosure under. */
  const COMPARISON_LAUNCHER_KEY = 'overseer.benchmark.modelComparison.launcher';

  function clearStoredState(): void {
    // All are real browser state, so without this a spec that starts a run, picks a comparison or
    // chooses a run report tab leaks its selections into every spec that constructs the component afterwards.
    try {
      localStorage.removeItem(RUN_SETTINGS_KEY);
      localStorage.removeItem(COMPARISON_SELECTION_KEY);
      localStorage.removeItem(COMPARISON_LAUNCHER_KEY);
      localStorage.removeItem(RUN_REPORT_TAB_STORAGE_KEY);
      localStorage.removeItem(RUN_REPORT_HEADER_STORAGE_KEY);
      localStorage.removeItem(RUN_HISTORY_VIEW_STORAGE_KEY);
      localStorage.removeItem(KEY_FIGURES_STORAGE_KEY);
      localStorage.removeItem(IMAGE_DETAILS_STORAGE_KEY);
    } catch { /* private-browsing modes throw */ }
  }

  beforeEach(clearStoredState);

  afterEach(clearStoredState);

  beforeEach(async () => {
    benchmarkServiceMock = jasmine.createSpyObj('AdminBenchmarkService', [
      'getSuites',
      'getRuns',
      'getQuestions',
      'getScoringProfiles',
      'createScoringProfile',
      'updateScoringProfile',
      'startRun',
      'getRun',
      'getActiveRun',
      'cancelRun',
      'rerunFailedQuestions',
      'deleteRun',
      'createSuite',
      'updateSuite',
      'deleteSuite',
      'duplicateSuite',
      'getDefaultSuiteCatalog',
      'importDefaultSuites',
      'getSuiteRunsFootprint',
      'deleteSuiteRuns',
      'reorderQuestions',
      'startDifficultyAssessment',
      'getDifficultyAssessment',
      'getActiveDifficultyAssessment',
      'cancelDifficultyAssessment',
      'reassessAnswer',
      'reassessPanelAnswer',
      'rerunAnswer',
      'rerunFinalSynthesis',
      'retryFailedAssessments',
      'rescoreRun',
      'trialReassessAnswer',
      'calibrateAssessor',
      'getCalibrations',
      'getLastAssessor',
      'retryClaimVerification',
      'getRunLimits',
      'startRunSeries',
      'getRunSeries',
      'getActiveRunSeries',
      'cancelRunSeries',
      'resumeRunSeries',
      'getRunGroups',
      'createRunGroup',
      'updateRunGroup',
      'previewRunGroupTier',
      'getRunReportUrl',
      'getToolCallLogUrl',
      'compareModels',
      'getComparabilityIndex',
      'importQuestions',
      'importSuite',
      'uploadSuiteSnapshot',
      'deleteSnapshot',
      'getSnapshot',
      'getRunBoard',
      'getActiveQuestionGeneration',
      'getBoardFactsCheck',
      'listReportDocuments',
      'writeRunReportDocuments',
      'getReportDocumentPdf',
      'getRunReportJob',
      'cancelRunReportJob',
      'estimateRunReports',
      'deleteRunReportDocument',
      'reportDocumentPdfUrl'
    ]);

    benchmarkServiceMock.getActiveQuestionGeneration.and.returnValue(of(null));
    // The run report's AI Reports tab lists the run's AI-written reports whenever it loads a run,
    // estimates the cost of a missing one and follows a writing job.
    benchmarkServiceMock.listReportDocuments.and.returnValue(of([]));
    benchmarkServiceMock.getRunReportJob.and.returnValue(of(null));
    benchmarkServiceMock.estimateRunReports.and.returnValue(of({
      estimates: [], estimatedTotalCostUsd: null, refusal: null, sameProviderWarning: null
    }));
    benchmarkServiceMock.deleteRunReportDocument.and.returnValue(of(undefined));
    benchmarkServiceMock.reportDocumentPdfUrl.and.returnValue('/api/admin/benchmark/report-documents/0/render/pdf');
    benchmarkServiceMock.getBoardFactsCheck.and.returnValue(of(null));

    benchmarkServiceMock.getActiveDifficultyAssessment.and.returnValue(of(null));
    benchmarkServiceMock.getActiveRun.and.returnValue(of(null));
    // ngOnInit reads the caps and reattaches a live series, and entering Run History loads the
    // groups for the group column. All three run on paths every test in this file goes through.
    benchmarkServiceMock.getActiveRunSeries.and.returnValue(of(null));
    benchmarkServiceMock.getRunSeries.and.returnValue(of({ id: 1, status: 'Running', completedRunCount: 0, requestedRunCount: 1, members: [] } as any));
    benchmarkServiceMock.getRunGroups.and.returnValue(of([]));
    benchmarkServiceMock.getRunLimits.and.returnValue(of({
      maxRunsPerHour: 4,
      maxRunsPerDay: 20,
      runsInLastHour: 0,
      runsInLast24Hours: 0,
      remainingDailyHeadroom: 20,
      maxRunCountPerSeries: 20
    }));
    benchmarkServiceMock.getRun.and.returnValue(of({ id: 1, answers: [] } as any));
    benchmarkServiceMock.getQuestions.and.returnValue(of([]));
    benchmarkServiceMock.getSuiteRunsFootprint.and.returnValue(of({ runCount: 0, totalAnswerCharacters: 0 }));
    benchmarkServiceMock.getCalibrations.and.returnValue(of([]));
    benchmarkServiceMock.getDefaultSuiteCatalog.and.returnValue(of([]));
    // A suite with no completed run has no assessor to differ from, which is not an error.
    benchmarkServiceMock.getLastAssessor.and.returnValue(of({}));
    benchmarkServiceMock.getSuites.and.returnValue(of([
      { id: 1, name: 'Default Suite', description: 'Test', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null, questionCount: 15, assessedQuestionCount: 15, difficultyFullyAssessed: true }
    ]));
    benchmarkServiceMock.getScoringProfiles.and.returnValue(of([
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
    benchmarkServiceMock.getRuns.and.returnValue(of([]));
    benchmarkServiceMock.compareModels.and.returnValue(of({ entries: [] } as any));
    benchmarkServiceMock.getComparabilityIndex.and.returnValue(of({
      computedAtUtc: '2026-09-07T12:00:00Z',
      entries: [],
      conditions: [],
      largestConditionKeys: [],
      referenceSelectionRule: 'The reference condition is the one with the most sources.',
      mustMatchKeyNames: ['BenchmarkSuiteId'],
      modelAxisKeyNames: ['ModelId'],
      degradingKeyNames: ['PricingSnapshot']
    } as any));

    systemServiceMock = jasmine.createSpyObj('SystemService', ['getVersion']);
    systemServiceMock.getVersion.and.returnValue(of('1.0.29'));

    // BenchmarkPollTickerService prefers a real Worker when one exists, which ChromeHeadless does,
    // but a worker fetching '/workers/benchmark-poll-ticker.js' from the Karma server is not the
    // same thing this suite's many fakeAsync/tick()-driven polling specs need: a deterministic,
    // zone-visible timer. Every spec in this file gets the plain setInterval fallback instead, so
    // polling behaves exactly as it did before the ticker existed; BenchmarkPollTickerService's own
    // spec file is what actually exercises the worker path and its post-start fallback.
    spyOn(BenchmarkPollTickerService.prototype, 'start').and.callFake((intervalMs: number, onTick: () => void) => {
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
        // The AI Reports tab hosts the PDF viewer; Karma never loads pdf.js.
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
  });

  it('should open a run requested through openRunId exactly once, and report it handled', () => {
    const viewRunDetail = spyOn(component, 'viewRunDetail');
    const handled = spyOn(component.openRunHandled, 'emit');

    component.openRunId = 123;
    component.openRunId = null;

    expect(viewRunDetail).toHaveBeenCalledOnceWith(123);
    expect(handled).toHaveBeenCalledTimes(1);
  });

  it('should open a run requested before its view existed once the view is initialised', async () => {
    const early = TestBed.createComponent(AdminBenchmarkComponent);
    const viewRunDetail = spyOn(early.componentInstance, 'viewRunDetail');
    early.componentInstance.openRunId = 77;

    expect(viewRunDetail).not.toHaveBeenCalled();

    early.detectChanges();
    await Promise.resolve();

    expect(viewRunDetail).toHaveBeenCalledOnceWith(77);
    early.destroy();
  });

  it('should create and load suites', () => {
    expect(component).toBeTruthy();
    expect(component.suites.length).toBe(1);
    expect(component.suites[0].name).toBe('Default Suite');
  });

  it('should filter benchmarkCapableConfigs based on modelRole bitmask 4', () => {
    expect(component.benchmarkCapableConfigs.length).toBe(1);
    
    // Add non-benchmark model (modelRole = 3: Chat + Title)
    component.systemConfigs.push({
      id: 2,
      displayName: 'Chat Only Model',
      displayNameMode: null,
      provider: 'OpenAI',
      modelId: 'gpt-5.6-luna',
      thinkingLevel: null,
      reasoningMode: null,
      reasoningSummary: null,
      serviceTier: null,
      maxInputTokens: null,
      maxOutputTokens: null,
      orderIndex: 1,
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
      modelRole: 3,
      parallelExecutionMode: 0,
      apiKey: '',
      note: null
    });

    expect(component.benchmarkCapableConfigs.length).toBe(1);
    expect(component.benchmarkCapableConfigs[0].id).toBe(1);
  });

  it('should format status strings correctly', () => {
    expect(component.formatStatus(1)).toBe('Running');
    expect(component.formatStatus(2)).toBe('Completed');
    expect(component.formatStatus(3)).toBe('CompletedWithErrors');
    expect(component.formatStatus(4)).toBe('Failed');
    expect(component.formatStatus(5)).toBe('Canceled');
  });

  it('should compute score badge classes correctly', () => {
    expect(component.getScoreBadgeClass(90)).toBe('badge-score-high');
    expect(component.getScoreBadgeClass(65)).toBe('badge-score-mid');
    expect(component.getScoreBadgeClass(40)).toBe('badge-score-low');
    expect(component.getScoreBadgeClass(null)).toBe('badge-score-na');
  });

  it('should render suite description markdown via CollapsibleMarkdownComponent and sanitize XSS vectors', () => {
    component.activeSubTab = 'suites';
    component.suites = [
      {
        id: 1,
        name: 'Markdown Suite',
        description: '**Bold Title**\n\n- Item 1\n- Item 2\n\n<script>alert("xss")</script><img src=x onerror="alert(1)">',
        createdAtUtc: '2026-09-01T00:00:00Z',
        modifiedAtUtc: null,
        questionCount: 18,
        assessedQuestionCount: 18,
        difficultyFullyAssessed: true
      }
    ];
    fixture.detectChanges();

    const compEl = fixture.nativeElement.querySelector('.suite-desc-container app-collapsible-markdown');
    expect(compEl).toBeTruthy();
    const contentEl = compEl.querySelector('.markdown-content');
    expect(contentEl).toBeTruthy();
    expect(contentEl.querySelector('strong')?.textContent).toContain('Bold Title');
    expect(contentEl.querySelector('ul')).toBeTruthy();
    expect(contentEl.querySelectorAll('li').length).toBe(2);
    // Ensure script and onerror attributes were stripped by DOMPurify
    expect(contentEl.querySelector('script')).toBeNull();
    expect(contentEl.innerHTML).not.toContain('onerror');
    expect(contentEl.innerHTML).not.toContain('<script');
  });

  it('labels the suite card buttons and the Reviewed badge without emoji or check-mark characters', () => {
    component.activeSubTab = 'suites';
    const boardSuite = {
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: null,
      description: '',
      questionCount: 18,
      assessedQuestionCount: 18,
      difficultyFullyAssessed: true,
      gameSnapshotId: 7,
      gameSnapshotName: 'Low HP',
      gameSnapshotCharCount: 12000,
      hasGeneratedQuestions: true
    };
    component.suites = [
      { ...boardSuite, id: 1, name: 'Reviewed Board Suite', reviewedQuestionCount: 18 },
      { ...boardSuite, id: 2, name: 'Unreviewed Board Suite', reviewedQuestionCount: 3 }
    ];
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const buttons = Array.from(host.querySelectorAll<HTMLElement>('.suite-card-actions button'));
    const labels = buttons.map(b => (b.textContent ?? '').trim());
    for (const label of ['Manage Questions', 'Generate Questions', 'Check Rubrics', 'Verify All']) {
      expect(labels).withContext(label).toContain(label);
    }

    const primaryButtons = buttons.filter(b => b.classList.contains('btn-gh'));
    expect(primaryButtons.length).toBe(2); // One "Manage Questions" per suite card.
    const secondaryButtons = buttons.filter(b => !b.classList.contains('btn-gh'));
    expect(secondaryButtons.every(b => b.classList.contains('btn-ghost'))).toBeTrue();

    const snapshotBadges = Array.from(host.querySelectorAll<HTMLElement>('.badge-board'));
    expect(snapshotBadges.length).toBe(2);
    for (const badge of snapshotBadges) {
      expect(badge.querySelector('svg[aria-hidden="true"]')).toBeTruthy();
      expect(badge.getAttribute('aria-label')).toContain('Low HP');
    }

    const reviewed = Array.from(host.querySelectorAll<HTMLElement>('.suite-card .badge-success'));
    expect(reviewed.length).toBe(1);
    expect(reviewed[0].textContent?.trim()).toBe('Reviewed');
    expect(reviewed[0].querySelector('svg[aria-hidden="true"]')).toBeTruthy();

    const needsReview = Array.from(host.querySelectorAll<HTMLElement>('.suite-card .badge-warning'));
    expect(needsReview.length).toBe(1);
    expect(needsReview[0].textContent?.trim()).toBe('Needs review (3 of 18)');
    expect(needsReview[0].querySelector('svg[aria-hidden="true"]')).toBeTruthy();

    const iconCharacters = /\p{Extended_Pictographic}|✓|✔/u;
    for (const element of [...buttons, ...reviewed]) {
      expect(iconCharacters.test(element.textContent ?? '')).withContext(element.textContent ?? '').toBeFalse();
    }
  });

  it('sets generationDialogVisible and passes the suite to the child when Generate Questions is clicked', () => {
    component.activeSubTab = 'suites';
    const suiteWithSnapshot = {
      id: 1,
      name: 'Board Suite',
      description: '',
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: null,
      questionCount: 18,
      assessedQuestionCount: 18,
      difficultyFullyAssessed: true,
      gameSnapshotId: 7,
      gameSnapshotName: 'Low HP'
    } as any;
    component.suites = [suiteWithSnapshot];
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const generateBtn = Array.from(host.querySelectorAll<HTMLElement>('.suite-card-actions button'))
      .find(b => (b.textContent ?? '').trim() === 'Generate Questions');
    expect(generateBtn).withContext('Generate Questions button should render for a suite with a game snapshot').toBeTruthy();

    // The click sets component state synchronously through the (click) binding; fixture.detectChanges()
    // is deliberately not called again afterwards, so the newly visible child's own ngOnChanges (which
    // calls service methods this spec does not stub) never fires.
    generateBtn!.click();

    expect(component.generationDialogVisible).toBeTrue();
    expect(component.generationSuiteForJob).toBe(suiteWithSnapshot);
  });

  it('should render question expected criteria via CollapsibleMarkdownComponent in questions list', () => {
    component.activeSubTab = 'suites';
    component.currentSuiteForQuestions = {
      id: 1,
      name: 'Default Suite',
      description: 'Test',
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: null,
      questionCount: 1,
      assessedQuestionCount: 1,
      difficultyFullyAssessed: true
    };
    component.questions = [
      {
        id: 1,
        benchmarkSuiteId: 1,
        orderIndex: 1,
        questionText: 'What are the stats of silver dragon scale mail?',
        difficulty: 1,
        assessedDifficulty: 15,
        assessedDifficultyModel: 'claude-3-5-sonnet',
        assessedDifficultyAtUtc: '2026-09-01T00:00:00Z',
        createdAtUtc: '2026-09-01T00:00:00Z',
        expectedPoints: '**REQUIRED** (accuracy + completeness)\n- Base AC 1\n- Confeers cold resistance and reflection\n\n**SOURCE** — src/objects.c'
      }
    ];
    fixture.detectChanges();

    const criteriaBox = fixture.nativeElement.querySelector('.q-criteria-box');
    expect(criteriaBox).toBeTruthy();
    const collapsibleComp = criteriaBox.querySelector('app-collapsible-markdown');
    expect(collapsibleComp).toBeTruthy();
    const markdownContent = collapsibleComp.querySelector('.markdown-content');
    expect(markdownContent).toBeTruthy();
    expect(markdownContent.querySelector('strong')?.textContent).toContain('REQUIRED');
    expect(markdownContent.querySelectorAll('li').length).toBe(2);
  });

  it('should edit the expected answer criteria through the markdown editor', () => {
    component.questionForm = {
      questionText: 'What are the stats of silver dragon scale mail?',
      difficulty: 1,
      expectedPoints: '**REQUIRED**\n- Base AC 1'
    };
    fixture.detectChanges();

    const editors = fixture.debugElement.queryAll(By.directive(MarkdownEditorComponent));
    const editor = editors.find(e => e.componentInstance.inputId === 'qExpectedInput');
    expect(editor).toBeTruthy();
    expect(editor!.componentInstance.value).toBe('**REQUIRED**\n- Base AC 1');

    // The two-way binding writes back through the component's own accessor.
    editor!.componentInstance.valueChange.emit('**REQUIRED**\n- Reflection');
    fixture.detectChanges();
    expect(component.questionForm.expectedPoints).toBe('**REQUIRED**\n- Reflection');
  });

  it('should mark the question form dialog as the wide markdown-editor variant', () => {
    fixture.detectChanges();

    const dialog: HTMLDialogElement = fixture.nativeElement.querySelector('dialog.benchmark-question-form-dialog');
    expect(dialog).toBeTruthy();
    expect(dialog.classList.contains('benchmark-form-dialog')).toBeTrue();
  });

  it('should mark the suite form dialog as the wide markdown-editor variant', () => {
    fixture.detectChanges();

    const dialog: HTMLDialogElement = fixture.nativeElement.querySelector('dialog.benchmark-suite-form-dialog');
    expect(dialog).toBeTruthy();
    expect(dialog.classList.contains('benchmark-form-dialog')).toBeTrue();
  });

  it('should render the suite description through the markdown editor and write back through suiteForm', () => {
    component.suiteForm = { name: 'Core Mechanics', description: '**Bold**\n- Item' };
    fixture.detectChanges();

    const editors = fixture.debugElement.queryAll(By.directive(MarkdownEditorComponent));
    const editor = editors.find(e => e.componentInstance.inputId === 'suiteDescInput');
    expect(editor).toBeTruthy();
    expect(editor!.componentInstance.value).toBe('**Bold**\n- Item');

    editor!.componentInstance.valueChange.emit('**Bold**\n- Changed');
    fixture.detectChanges();
    expect(component.suiteForm.description).toBe('**Bold**\n- Changed');
  });

  describe('suite description generation and unsaved-changes guard', () => {
    const suite = {
      id: 5,
      name: 'Core Mechanics',
      description: 'Original',
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: null,
      questionCount: 12,
      assessedQuestionCount: 12,
      difficultyFullyAssessed: true
    } as any;

    let suiteClose: jasmine.Spy;
    let confirmShowModal: jasmine.Spy;

    function generateButton(): HTMLButtonElement | undefined {
      const host = fixture.nativeElement as HTMLElement;
      return Array.from(host.querySelectorAll<HTMLButtonElement>('dialog.benchmark-suite-form-dialog .suite-desc-tools button'))
        .find(b => (b.textContent ?? '').trim() === 'Generate with AI');
    }

    beforeEach(() => {
      fixture.detectChanges();
      spyOn(component.suiteDialog.nativeElement, 'showModal');
      suiteClose = spyOn(component.suiteDialog.nativeElement, 'close');
      confirmShowModal = spyOn(component.confirmActionDialog.nativeElement, 'showModal');
      spyOn(component.confirmActionDialog.nativeElement, 'close');
    });

    it('disables Generate with AI in create mode and enables it for an existing suite', () => {
      component.openCreateSuite();
      fixture.detectChanges();
      expect(generateButton()).toBeTruthy();
      expect(generateButton()!.disabled).toBeTrue();

      component.openEditSuite(suite);
      fixture.detectChanges();
      expect(generateButton()!.disabled).toBeFalse();
    });

    it('sets descriptionGenerationVisible when Generate with AI is clicked', () => {
      component.openEditSuite(suite);
      fixture.detectChanges();

      // No detectChanges after the click, so the child's ngOnChanges never reaches unstubbed services.
      generateButton()!.click();

      expect(component.descriptionGenerationVisible).toBeTrue();
      expect(component.descriptionGenerationSuite).toBe(suite);
    });

    it('writes a generated description into the suite form and marks it dirty', () => {
      component.openEditSuite(suite);
      expect(component.suiteFormDirty).toBeFalse();

      component.onDescriptionGenerated('## Draft');

      expect(component.suiteForm.description).toBe('## Draft');
      expect(component.suiteFormDirty).toBeTrue();
    });

    it('closes an unchanged suite dialog without asking', () => {
      component.openEditSuite(suite);
      const titleBefore = component.confirmDialogTitle;

      component.requestCloseSuiteDialog();

      expect(suiteClose).toHaveBeenCalled();
      expect(confirmShowModal).not.toHaveBeenCalled();
      expect(component.confirmDialogTitle).toBe(titleBefore);
    });

    it('asks before discarding an edited name, and closes on confirmation', () => {
      component.openEditSuite(suite);
      component.suiteForm.name = 'Renamed';

      component.requestCloseSuiteDialog();

      expect(suiteClose).not.toHaveBeenCalled();
      expect(confirmShowModal).toHaveBeenCalled();
      expect(component.confirmDialogTitle).toBe('Discard unsaved changes?');

      component.executeConfirmAction();
      expect(suiteClose).toHaveBeenCalled();
    });

    it('routes Escape on a dirty suite dialog through the confirmation', () => {
      component.openEditSuite(suite);
      component.suiteForm.description = 'Edited';
      const event = new Event('cancel', { cancelable: true });

      component.onSuiteDialogCancel(event);

      expect(event.defaultPrevented).toBeTrue();
      expect(suiteClose).not.toHaveBeenCalled();
      expect(component.confirmDialogTitle).toBe('Discard unsaved changes?');
    });
  });

  describe('AI Auto-Rate All Difficulties disabled state', () => {
    it('is aria-disabled and inert while the question list is empty', () => {
      component.currentSuiteForQuestions = {
        id: 1,
        name: 'Empty Suite',
        description: '',
        createdAtUtc: '2026-09-01T00:00:00Z',
        modifiedAtUtc: null,
        questionCount: 0,
        assessedQuestionCount: 0,
        difficultyFullyAssessed: false
      };
      component.questions = [];
      component.loadingQuestions = false;
      fixture.detectChanges();

      expect(component.canAutoRateAll).toBeFalse();

      const button = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.questions-toolbar button'))
        .find(b => b.textContent?.trim() === 'AI Auto-Rate All Difficulties');
      expect(button).toBeTruthy();
      expect(button!.getAttribute('aria-disabled')).toBe('true');

      spyOn(component, 'openDifficultyAssessorDialog');
      button!.click();
      expect(component.openDifficultyAssessorDialog).not.toHaveBeenCalled();

      expect(fixture.nativeElement.textContent).toContain('Add questions to enable AI rating.');
    });

    it('is enabled once the suite has at least one question', () => {
      component.currentSuiteForQuestions = {
        id: 1,
        name: 'Suite With Questions',
        description: '',
        createdAtUtc: '2026-09-01T00:00:00Z',
        modifiedAtUtc: null,
        questionCount: 1,
        assessedQuestionCount: 0,
        difficultyFullyAssessed: false
      };
      component.questions = [{
        id: 1,
        benchmarkSuiteId: 1,
        orderIndex: 1,
        questionText: 'Sample question',
        difficulty: 1,
        createdAtUtc: '2026-09-01T00:00:00Z'
      }] as any;
      component.loadingQuestions = false;
      fixture.detectChanges();

      expect(component.canAutoRateAll).toBeTrue();

      const button = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.questions-toolbar button'))
        .find(b => b.textContent?.trim() === 'AI Auto-Rate All Difficulties');
      expect(button!.getAttribute('aria-disabled')).toBeNull();

      spyOn(component, 'openDifficultyAssessorDialog');
      button!.click();
      expect(component.openDifficultyAssessorDialog).toHaveBeenCalledWith(component.currentSuiteForQuestions);
    });
  });

  it('should render model answers, thought text, and assessor comments as plain text and not innerHTML', () => {
    component.selectedRunDetail = {
      id: 10,
      benchmarkSuiteId: 1,
      suiteName: 'Test Suite',
      testedModelConfigurationId: 1,
      testedModelDisplayNameUsed: 'Candidate Model',
      testedModelProviderUsed: 'Anthropic',
      testedModelIdUsed: 'claude-3-5-sonnet',
      testedModelThinkingLevelUsed: null,
      testedModelReasoningModeUsed: null,
      testedModelReasoningSummaryUsed: null,
      testedModelServiceTierUsed: null,
      testedModelMaxOutputTokensUsed: null,
      testedModelParallelExecutionModeUsed: 0,
      assessorModelConfigurationId: 1,
      assessorModelDisplayNameUsed: 'Assessor Model',
      assessorModelProviderUsed: 'Anthropic',
      assessorModelIdUsed: 'claude-3-5-sonnet',
      assessorModelThinkingLevelUsed: null,
      assessorModelReasoningModeUsed: null,
      startedByUserId: null,
      startedByUserName: 'admin',
      status: 2,
      startedAtUtc: '2026-09-01T00:00:00Z',
      completedAtUtc: '2026-09-01T00:05:00Z',
      finalScore: 85,
      computedScore: 85,
      qualityIndex: 85,
      speedIndex: 90,
      totalAnswerDurationMs: 12000,
      scoringProfileId: 1,
      scoringProfileName: 'Default',
      scoringProfileSnapshotJson: null,
      scoringMethodVersion: 1,
      transportDefectAnswerCount: 0,
      advisoryFlagAnswerCount: 0,
      scrubbedArtifactAnswerCount: 0,
      difficultyFallbackUsed: false,
      speedMeasurementDegraded: false,
      maxParallelQuestionsUsed: 1,
      answeredQuestionCount: 1,
      unansweredQuestionCount: 0,
      totalQuestionCount: 1,
      purposeStatementUsed: 'Test Purpose',
      sameProviderAcknowledged: false,
      assessmentJson: null,
      assessmentText: '<div id="assessment-html">Assessor <b>Overview</b></div>',
      assessmentParseFailed: false,
      totalInputTokens: 100,
      totalOutputTokens: 100,
      totalCacheReadTokens: 0,
      totalCacheCreationTokens: 0,
      totalDurationMs: 12000,
      errorMessage: null,
      answers: [
        {
          id: 101,
          benchmarkRunId: 10,
          orderIndex: 1,
          questionText: 'Test Question 1',
          difficulty: 1,
          assessedDifficulty: 10,
          answerText: 'Model Answer with <strong>raw html tag</strong> and <script>alert("hack")</script>',
          thoughtText: 'Thought with <em>markdown/html</em> syntax',
          status: 1,
          assessmentStatus: 1,
          assessmentError: null,
          errorMessage: null,
          httpStatusCode: 200,
          score: 85,
          accuracyLevel: 5,
          completenessLevel: 5,
          concisenessLevel: 5,
          readabilityLevel: 5,
          criticalError: false,
          accuracyScore: 87,
          completenessScore: 87,
          concisenessScore: 87,
          readabilityScore: 87,
          qualityScore: 87,
          speedScore: 92,
          reviewComment: 'Assessor comment with <span class="badge">tag</span>',
          durationMs: 3000,
          modelTimeMs: 3000,
          scrubbedArtifactCount: 0,
          timeToFirstTokenMs: 200,
          actualServiceTierUsed: null,
          toolCallSummary: null,
          inputTokens: 50,
          outputTokens: 50,
          cacheReadInputTokens: 0,
          cacheCreationInputTokens: 0
        }
      ]
    };
    component.expandedQuestions.add(1);
    component.expandedThoughts.add(1);
    fixture.detectChanges();

    const answerBox = fixture.nativeElement.querySelector('.answer-box');
    expect(answerBox).toBeTruthy();
    expect(answerBox.querySelector('strong')).toBeNull();
    expect(answerBox.textContent).toContain('<strong>raw html tag</strong>');

    const thoughtBox = fixture.nativeElement.querySelector('.thought-box');
    expect(thoughtBox).toBeTruthy();
    expect(thoughtBox.querySelector('em')).toBeNull();
    expect(thoughtBox.textContent).toContain('<em>markdown/html</em>');

    const assessorComment = fixture.nativeElement.querySelector('.assessor-comment-box');
    expect(assessorComment).toBeTruthy();
    expect(assessorComment.querySelector('span.badge')).toBeNull();
    expect(assessorComment.textContent).toContain('<span class="badge">tag</span>');

    const proseReview = fixture.nativeElement.querySelector('.prose-review');
    expect(proseReview).toBeTruthy();
    expect(proseReview.querySelector('#assessment-html')).toBeNull();
    expect(proseReview.textContent).toContain('<div id="assessment-html">');
  });

  it('should display "Create Default Suites" on the import button without hardcoded question count', () => {
    component.activeSubTab = 'suites';
    fixture.detectChanges();

    const buttons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll('.suites-toolbar .btn-ghost'));
    const importBtn = buttons.find(b => b.textContent!.trim() === 'Create Default Suites');
    expect(importBtn).toBeTruthy();
    expect(importBtn!.textContent).not.toContain('15-Question');
  });

  describe('Import Default Suites dialog', () => {
    const catalogEntry = {
      key: 'gnollhack-player-assistance',
      version: 1,
      name: 'GnollHack Player Assistance Benchmark Suite',
      description: 'Eighteen questions. ### Covered Domains\n- **Character & World**: creation.',
      questionCount: 18,
      difficultyCounts: { Simple: 6, Intermediate: 6, Advanced: 6 },
      fileName: 'gnollhack_player_assistance.json',
      error: null,
      alreadyImportedCount: 0,
      alreadyImportedNames: [],
      nameMatchedSuiteNames: []
    };

    beforeEach(() => {
      component.activeSubTab = 'suites';
      spyOn(component.importDefaultSuitesDialog.nativeElement, 'showModal');
      spyOn(component.importDefaultSuitesDialog.nativeElement, 'close');
    });

    it('loads the catalog and renders one checkbox per entry, named for the suite', () => {
      benchmarkServiceMock.getDefaultSuiteCatalog.and.returnValue(of([catalogEntry] as any));
      fixture.detectChanges();

      component.openImportDefaultSuitesDialog();
      fixture.detectChanges();

      expect(benchmarkServiceMock.getDefaultSuiteCatalog).toHaveBeenCalled();
      expect(component.importDefaultSuitesDialog.nativeElement.showModal).toHaveBeenCalled();

      const dialogEl = component.importDefaultSuitesDialog.nativeElement;
      const label = dialogEl.querySelector('.default-suite-picker label.checkbox-label');
      expect(label).toBeTruthy();
      expect(label!.textContent).toContain(catalogEntry.name);
      expect(label!.querySelector('input[type="checkbox"]')).toBeTruthy();
    });

    it('keeps Import selected aria-disabled until a suite is checked', () => {
      benchmarkServiceMock.getDefaultSuiteCatalog.and.returnValue(of([catalogEntry] as any));
      fixture.detectChanges();
      component.openImportDefaultSuitesDialog();
      fixture.detectChanges();

      const dialogEl = component.importDefaultSuitesDialog.nativeElement;
      const importBtn = dialogEl.querySelector('.dialog-footer .btn-gh:not(.btn-gh-cancel)') as HTMLButtonElement;
      expect(importBtn.getAttribute('aria-disabled')).toBe('true');

      component.toggleDefaultSuite(catalogEntry.key);
      (component as unknown as { cdr: ChangeDetectorRef }).cdr.detectChanges();

      expect(component.canImportDefaultSuites).toBeTrue();
      expect(importBtn.getAttribute('aria-disabled')).toBe('false');
    });

    it('imports the selected keys and reloads the suite list', () => {
      benchmarkServiceMock.getDefaultSuiteCatalog.and.returnValue(of([catalogEntry] as any));
      benchmarkServiceMock.importDefaultSuites.and.returnValue(of({
        imported: [{ id: 9, name: catalogEntry.name }],
        skipped: []
      } as any));
      fixture.detectChanges();
      component.openImportDefaultSuitesDialog();
      component.toggleDefaultSuite(catalogEntry.key);
      fixture.detectChanges();

      component.importSelectedDefaultSuites();

      expect(benchmarkServiceMock.importDefaultSuites).toHaveBeenCalledWith([catalogEntry.key]);
      expect(component.importDefaultSuitesDialog.nativeElement.close).toHaveBeenCalled();
      // Once from ngOnInit, once from the post-import reload.
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalledTimes(2);
      expect(component.suiteActionAnnouncement).toContain(catalogEntry.name);
    });

    it('announces a skipped entry with its reason', () => {
      benchmarkServiceMock.getDefaultSuiteCatalog.and.returnValue(of([catalogEntry] as any));
      benchmarkServiceMock.importDefaultSuites.and.returnValue(of({
        imported: [],
        skipped: [{ key: catalogEntry.key, reason: 'Suite quota reached.' }]
      } as any));
      fixture.detectChanges();
      component.openImportDefaultSuitesDialog();
      component.toggleDefaultSuite(catalogEntry.key);
      fixture.detectChanges();

      component.importSelectedDefaultSuites();

      expect(component.suiteActionAnnouncement).toContain('Suite quota reached.');
    });

    it('shows an invalid catalog entry with its error and no checkbox', () => {
      const invalidEntry = {
        key: null, version: null, name: 'broken.json', description: null, questionCount: 0,
        difficultyCounts: {}, fileName: 'broken.json', error: 'Missing "key" field.',
        alreadyImportedCount: 0, alreadyImportedNames: [], nameMatchedSuiteNames: []
      };
      benchmarkServiceMock.getDefaultSuiteCatalog.and.returnValue(of([invalidEntry] as any));
      fixture.detectChanges();
      component.openImportDefaultSuitesDialog();
      fixture.detectChanges();

      const row = component.importDefaultSuitesDialog.nativeElement.querySelector('.default-suite-row-invalid');
      expect(row).toBeTruthy();
      expect(row!.textContent).toContain('Missing "key" field.');
      expect(row!.querySelector('input[type="checkbox"]')).toBeNull();
    });

    it('renders the description as HTML, not Markdown source', () => {
      benchmarkServiceMock.getDefaultSuiteCatalog.and.returnValue(of([catalogEntry] as any));
      fixture.detectChanges();
      component.openImportDefaultSuitesDialog();
      fixture.detectChanges();

      const dialogEl = component.importDefaultSuitesDialog.nativeElement;
      expect(dialogEl.querySelector('.default-suite-description .markdown-body strong')).toBeTruthy();
      expect(dialogEl.textContent).not.toContain('**');
    });

    it('renders one difficulty badge per band with its count', () => {
      benchmarkServiceMock.getDefaultSuiteCatalog.and.returnValue(of([catalogEntry] as any));
      fixture.detectChanges();
      component.openImportDefaultSuitesDialog();
      fixture.detectChanges();

      const badges: HTMLElement[] = Array.from(
        component.importDefaultSuitesDialog.nativeElement.querySelectorAll('.default-suite-bands .difficulty-badge'));
      expect(badges.length).toBe(3);
      expect(badges[0].classList).toContain('diff-simple');
      expect(badges[0].textContent).toContain('Simple');
      expect(badges[0].textContent).toContain('6');
    });

    it('shows the selection status bar and updates it on toggle', () => {
      benchmarkServiceMock.getDefaultSuiteCatalog.and.returnValue(of([catalogEntry] as any));
      fixture.detectChanges();
      component.openImportDefaultSuitesDialog();
      fixture.detectChanges();

      const bar = component.importDefaultSuitesDialog.nativeElement.querySelector('.dialog-status-bar') as HTMLElement;
      expect(bar).toBeTruthy();
      expect(bar.textContent).toContain('Select at least one suite');
      expect(bar.classList).not.toContain('is-ready');

      component.toggleDefaultSuite(catalogEntry.key);
      (component as unknown as { cdr: ChangeDetectorRef }).cdr.detectChanges();

      expect(bar.textContent).toContain('1 of 1');
      expect(bar.classList).toContain('is-ready');
    });

    it('is sized like the Manage Questions dialog', () => {
      expect(component.importDefaultSuitesDialog.nativeElement.classList)
        .toContain('benchmark-import-suites-dialog');
    });

    it('does not put the description inside the checkbox label', () => {
      benchmarkServiceMock.getDefaultSuiteCatalog.and.returnValue(of([catalogEntry] as any));
      fixture.detectChanges();
      component.openImportDefaultSuitesDialog();
      fixture.detectChanges();

      const label = component.importDefaultSuitesDialog.nativeElement
        .querySelector('.default-suite-picker label.checkbox-label') as HTMLElement;
      expect(label).toBeTruthy();
      expect(label.querySelector('.default-suite-description')).toBeNull();
    });
  });

  it('shows the Manage Suites empty state and the Run Benchmark notice when no suites exist', () => {
    benchmarkServiceMock.getSuites.and.returnValue(of([]));
    component.loadSuites();
    fixture.detectChanges();

    component.activeSubTab = 'suites';
    fixture.detectChanges();

    const suitesEmptyState = fixture.nativeElement.querySelector('.suites-grid .empty-state[role="status"]');
    expect(suitesEmptyState).toBeTruthy();
    expect(suitesEmptyState.textContent).toContain('No question suites yet');

    component.activeSubTab = 'run';
    (component as unknown as { cdr: ChangeDetectorRef }).cdr.detectChanges();

    const runNotice = fixture.nativeElement.querySelector('.setup-card .empty-state[role="status"]');
    expect(runNotice).toBeTruthy();
    expect(runNotice.textContent).toContain('No question suite available');
  });

  it('keeps Start Benchmark aria-disabled with a hint naming the missing suite when none is selected', () => {
    benchmarkServiceMock.getSuites.and.returnValue(of([]));
    component.loadSuites();
    fixture.detectChanges();

    const startBtn = fixture.nativeElement.querySelector('.form-actions .btn-gh') as HTMLButtonElement;
    expect(startBtn.getAttribute('aria-disabled')).toBe('true');

    const hint = fixture.nativeElement.querySelector('#startBenchmarkHint');
    expect(hint.textContent.trim()).toBe('Select a question suite first.');

    startBtn.click();
    expect(benchmarkServiceMock.startRun).not.toHaveBeenCalled();
  });

  it('should open confirmActionDialog modal on deleteSuite and delete when confirmed', () => {
    spyOn(window, 'confirm');
    benchmarkServiceMock.deleteSuite.and.returnValue(of(void 0));
    component.activeSubTab = 'suites';
    component.suites = [
      { id: 42, name: 'Target Suite', description: 'Test', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null, questionCount: 1, assessedQuestionCount: 0, difficultyFullyAssessed: false }
    ];
    fixture.detectChanges();

    component.deleteSuite(42);

    expect(window.confirm).not.toHaveBeenCalled();
    expect(component.confirmDialogTitle).toBe('Delete Benchmark Suite');
    expect(component.confirmDialogMessage).toContain('"Target Suite"');

    component.executeConfirmAction();

    expect(benchmarkServiceMock.deleteSuite).toHaveBeenCalledWith(42);
  });

  it('should show the refusal a Delete Suite comes back with, rather than only logging it', () => {
    const refusal = 'The suite has runs. Delete its runs first.';
    benchmarkServiceMock.deleteSuite.and.returnValue(throwError(() => ({ status: 409, error: refusal })));
    const logged = spyOn(console, 'error');
    component.activeSubTab = 'suites';
    component.suites = [
      { id: 42, name: 'Target Suite', description: 'Test', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null, questionCount: 1, assessedQuestionCount: 0, difficultyFullyAssessed: false }
    ];
    fixture.detectChanges();

    component.deleteSuite(42);
    component.executeConfirmAction();

    expect(component.actionErrorMessage).toBe(refusal);
    expect(logged).not.toHaveBeenCalledWith('Failed to delete suite', jasmine.anything());
    const alert = fixture.nativeElement.querySelector('#bm-panel-suites .alert-danger .alert-message') as HTMLElement;
    expect(alert?.textContent?.trim()).toBe(refusal);
  });

  it('should open difficultyAssessorDialog on clicking Assess Question Difficulty without calling rateSuiteDifficulty immediately', () => {
    component.activeSubTab = 'suites';
    const testSuite = {
      id: 1,
      name: 'Default Suite',
      description: 'Test',
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: null,
      questionCount: 15,
      assessedQuestionCount: 10,
      difficultyFullyAssessed: false
    };
    component.suites = [testSuite];
    fixture.detectChanges();

    spyOn(component.difficultyAssessorDialog.nativeElement, 'showModal');

    component.openDifficultyAssessorDialog(testSuite);

    expect(component.difficultyAssessorDialog.nativeElement.showModal).toHaveBeenCalled();
    expect(component.suiteForDifficultyAssessment).toBe(testSuite);
    expect(component.difficultyAssessmentScope).toBe('unassessed');
    expect(component.difficultyAssessmentTargetDescription).toBe('the 5 of 15 questions in Default Suite that do not yet have an assessed difficulty');
    expect(benchmarkServiceMock.startDifficultyAssessment).not.toHaveBeenCalled();

    (component as unknown as { cdr: ChangeDetectorRef }).cdr.detectChanges();
    const radios: HTMLInputElement[] = Array.from(fixture.nativeElement.querySelectorAll('.difficulty-scope-fieldset input[type="radio"]'));
    expect(radios.map(r => r.value)).toEqual(['unassessed', 'suite']);
    expect(radios[0].checked).toBeTrue();
  });

  it('should default the difficulty assessment scope to the whole suite when no question is assessed yet', () => {
    component.activeSubTab = 'suites';
    const unassessedSuite = {
      id: 1,
      name: 'Default Suite',
      description: 'Test',
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: null,
      questionCount: 15,
      assessedQuestionCount: 0,
      difficultyFullyAssessed: false
    };
    component.suites = [unassessedSuite];
    fixture.detectChanges();
    spyOn(component.difficultyAssessorDialog.nativeElement, 'showModal');

    component.openDifficultyAssessorDialog(unassessedSuite);
    (component as unknown as { cdr: ChangeDetectorRef }).cdr.detectChanges();

    expect(component.difficultyAssessmentScope).toBe('suite');
    expect(fixture.nativeElement.querySelector('.difficulty-scope-fieldset')).toBeNull();
  });

  it('should default the difficulty assessment scope to the whole suite when every question is assessed', () => {
    component.activeSubTab = 'suites';
    const assessedSuite = {
      id: 1,
      name: 'Default Suite',
      description: 'Test',
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: null,
      questionCount: 15,
      assessedQuestionCount: 15,
      difficultyFullyAssessed: true
    };
    component.suites = [assessedSuite];
    fixture.detectChanges();
    spyOn(component.difficultyAssessorDialog.nativeElement, 'showModal');

    component.openDifficultyAssessorDialog(assessedSuite);
    (component as unknown as { cdr: ChangeDetectorRef }).cdr.detectChanges();

    expect(component.difficultyAssessmentScope).toBe('suite');
    expect(fixture.nativeElement.querySelector('.difficulty-scope-fieldset')).toBeNull();
  });

  it('should resolve default difficulty assessor preferring question stored config id if benchmark-capable', () => {
    const questionWithValidConfig = {
      id: 1,
      benchmarkSuiteId: 1,
      orderIndex: 1,
      questionText: 'Q1',
      difficulty: 1,
      expectedPoints: null,
      assessedDifficultyModelConfigurationId: 1,
      createdAtUtc: '2026-09-01T00:00:00Z'
    };
    expect(component.resolveDefaultDifficultyAssessor(questionWithValidConfig)).toBe(1);

    const questionWithInvalidConfig = {
      id: 2,
      benchmarkSuiteId: 1,
      orderIndex: 2,
      questionText: 'Q2',
      difficulty: 1,
      expectedPoints: null,
      assessedDifficultyModelConfigurationId: 999,
      createdAtUtc: '2026-09-01T00:00:00Z'
    };
    // Falls back to assessorConfigId or first benchmark capable config (id: 1)
    expect(component.resolveDefaultDifficultyAssessor(questionWithInvalidConfig)).toBe(1);
  });

  it('should render suite progress badge classes correctly', () => {
    const partialSuite = {
      id: 1,
      name: 'Partial Suite',
      description: null,
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: null,
      questionCount: 18,
      assessedQuestionCount: 10,
      difficultyFullyAssessed: false
    };
    expect(component.difficultyProgressLabel(partialSuite)).toBe('Difficulty 10/18 Assessed');
    expect(component.difficultyProgressClass(partialSuite)).toBe('partial');

    const completeSuite = {
      id: 2,
      name: 'Complete Suite',
      description: null,
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: null,
      questionCount: 18,
      assessedQuestionCount: 18,
      difficultyFullyAssessed: true
    };
    expect(component.difficultyProgressLabel(completeSuite)).toBe('Difficulty 18/18 Assessed');
    expect(component.difficultyProgressClass(completeSuite)).toBe('complete');

    const emptySuite = {
      id: 3,
      name: 'Empty Suite',
      description: null,
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: null,
      questionCount: 0,
      assessedQuestionCount: 0,
      difficultyFullyAssessed: false
    };
    expect(component.difficultyProgressClass(emptySuite)).toBe('none');
  });

  it('should start difficulty assessment on confirm, set phase to progress, and start polling', () => {
    const testSuite = {
      id: 1,
      name: 'Default Suite',
      description: 'Test',
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: null,
      questionCount: 15,
      assessedQuestionCount: 0,
      difficultyFullyAssessed: false
    };
    component.suiteForDifficultyAssessment = testSuite;
    component.difficultyAssessmentScope = 'suite';
    component.difficultyAssessorConfigId = 1;

    const mockJob = {
      id: 'job-123',
      suiteId: 1,
      suiteName: 'Default Suite',
      scope: 'suite',
      assessorConfigId: 1,
      assessorDisplayName: 'Test Assessor',
      startedAtUtc: '2026-09-02T00:00:00Z',
      completedAtUtc: null,
      status: 'Running',
      ratedCount: 0,
      failedCount: 0,
      totalCount: 15,
      totalModelCalls: 0,
      promptTokens: 0,
      outputTokens: 0,
      items: [],
      log: []
    };

    benchmarkServiceMock.startDifficultyAssessment.and.returnValue(of({ jobId: 'job-123' }));
    benchmarkServiceMock.getDifficultyAssessment.and.returnValue(of(mockJob));

    component.confirmDifficultyAssessment();

    expect(benchmarkServiceMock.startDifficultyAssessment).toHaveBeenCalledWith({
      suiteId: 1,
      questionIds: null,
      onlyUnassessed: false,
      assessorModelConfigurationId: 1
    });
    expect(component.difficultyDialogPhase).toBe('progress');
    expect(benchmarkServiceMock.getDifficultyAssessment).toHaveBeenCalledWith('job-123');
    expect(component.difficultyJob).toEqual(mockJob);
    expect(component.difficultyJobIsRunning).toBeTrue();
  });

  it('should send onlyUnassessed when confirming the unassessed scope', () => {
    component.suiteForDifficultyAssessment = {
      id: 1,
      name: 'Default Suite',
      description: 'Test',
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: null,
      questionCount: 15,
      assessedQuestionCount: 10,
      difficultyFullyAssessed: false
    };
    component.difficultyAssessmentScope = 'unassessed';
    component.difficultyAssessorConfigId = 1;

    benchmarkServiceMock.startDifficultyAssessment.and.returnValue(of({ jobId: 'job-unassessed' }));
    benchmarkServiceMock.getDifficultyAssessment.and.returnValue(of({
      id: 'job-unassessed',
      suiteId: 1,
      suiteName: 'Default Suite',
      scope: 'unassessed',
      assessorConfigId: 1,
      assessorDisplayName: 'Test Assessor',
      startedAtUtc: '2026-09-02T00:00:00Z',
      completedAtUtc: null,
      status: 'Running',
      ratedCount: 0,
      failedCount: 0,
      totalCount: 5,
      totalModelCalls: 0,
      promptTokens: 0,
      outputTokens: 0,
      items: [],
      log: []
    }));

    component.confirmDifficultyAssessment();

    expect(benchmarkServiceMock.startDifficultyAssessment).toHaveBeenCalledWith({
      suiteId: 1,
      questionIds: null,
      onlyUnassessed: true,
      assessorModelConfigurationId: 1
    });
  });

  it('should handle 409 conflict when starting difficulty assessment by adopting running job', () => {
    component.suiteForDifficultyAssessment = {
      id: 1,
      name: 'Default Suite',
      description: 'Test',
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: null,
      questionCount: 15,
      assessedQuestionCount: 0,
      difficultyFullyAssessed: false
    };
    component.difficultyAssessorConfigId = 1;

    const existingJob = {
      id: 'job-conflict',
      suiteId: 1,
      suiteName: 'Default Suite',
      scope: 'suite',
      assessorConfigId: 1,
      assessorDisplayName: 'Test Assessor',
      startedAtUtc: '2026-09-02T00:00:00Z',
      completedAtUtc: null,
      status: 'Running',
      ratedCount: 5,
      failedCount: 0,
      totalCount: 15,
      totalModelCalls: 2,
      promptTokens: 100,
      outputTokens: 50,
      items: [],
      log: []
    };

    const errorResponse = { status: 409, error: existingJob };
    benchmarkServiceMock.startDifficultyAssessment.and.returnValue(throwError(() => errorResponse));
    benchmarkServiceMock.getDifficultyAssessment.and.returnValue(of(existingJob));

    component.confirmDifficultyAssessment();

    expect(component.difficultyDialogPhase).toBe('progress');
    expect(component.difficultyJob).toEqual(existingJob);
    expect(component.difficultyJobIsRunning).toBeTrue();
  });

  it('should cancel running assessment on terminateDifficultyAssessment', () => {
    const runningJob = {
      id: 'job-to-cancel',
      suiteId: 1,
      suiteName: 'Default Suite',
      scope: 'suite',
      assessorConfigId: 1,
      assessorDisplayName: 'Test Assessor',
      startedAtUtc: '2026-09-02T00:00:00Z',
      completedAtUtc: null,
      status: 'Running',
      ratedCount: 2,
      failedCount: 0,
      totalCount: 10,
      totalModelCalls: 1,
      promptTokens: 50,
      outputTokens: 20,
      items: [],
      log: []
    };

    component.difficultyJob = runningJob;
    benchmarkServiceMock.cancelDifficultyAssessment.and.returnValue(of({ cancelled: true }));
    benchmarkServiceMock.getDifficultyAssessment.and.returnValue(of({ ...runningJob, status: 'Cancelled' }));

    component.terminateDifficultyAssessment();

    expect(benchmarkServiceMock.cancelDifficultyAssessment).toHaveBeenCalledWith('job-to-cancel');
  });

  it('should stay terminating until a poll reports the job has stopped', () => {
    const runningJob = {
      id: 'job-terminating',
      suiteId: 1,
      suiteName: 'Default Suite',
      scope: 'suite',
      assessorConfigId: 1,
      assessorDisplayName: 'Test Assessor',
      startedAtUtc: '2026-09-02T00:00:00Z',
      completedAtUtc: null as string | null,
      status: 'Running',
      ratedCount: 1,
      failedCount: 0,
      totalCount: 3,
      totalModelCalls: 1,
      promptTokens: 50,
      outputTokens: 20,
      items: [
        { questionId: 1, orderIndex: 1, questionTextExcerpt: 'Q1', status: 'Rated', difficulty: 40, errorMessage: null },
        { questionId: 2, orderIndex: 2, questionTextExcerpt: 'Q2', status: 'Assessing', difficulty: null, errorMessage: null },
        { questionId: 3, orderIndex: 3, questionTextExcerpt: 'Q3', status: 'Pending', difficulty: null, errorMessage: null }
      ],
      log: []
    };

    component.difficultyJob = runningJob;
    component.difficultyDialogPhase = 'progress';
    benchmarkServiceMock.cancelDifficultyAssessment.and.returnValue(of({ cancelled: true }));
    benchmarkServiceMock.getDifficultyAssessment.and.returnValue(of(runningJob));

    component.terminateDifficultyAssessment();
    fixture.detectChanges();

    expect(component.terminatingDifficultyJob).toBeTrue();
    expect(component.difficultyJobIsRunning).toBeTrue();
    const terminatingButtons: HTMLButtonElement[] = Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button'))
      .filter(b => b.textContent?.includes('Terminating…'));
    expect(terminatingButtons.length).toBeGreaterThan(0);
    terminatingButtons.forEach(b => {
      expect(b.disabled).toBeTrue();
      expect(b.getAttribute('aria-busy')).toBe('true');
    });

    const cancelledJob = {
      ...runningJob,
      status: 'Cancelled',
      completedAtUtc: '2026-09-02T00:01:00Z',
      items: runningJob.items.map(i => i.status === 'Rated' ? i : { ...i, status: 'Cancelled' })
    };
    benchmarkServiceMock.getDifficultyAssessment.and.returnValue(of(cancelledJob));

    component.startDifficultyPolling('job-terminating');
    component.stopDifficultyPolling();
    fixture.detectChanges();

    expect(component.terminatingDifficultyJob).toBeFalse();
    expect(component.difficultyJobIsTerminal).toBeTrue();
    expect(fixture.nativeElement.querySelectorAll('.job-status-chip.status-cancelled').length).toBe(2);
    expect(fixture.nativeElement.querySelectorAll('.job-status-chip.status-rated').length).toBe(1);
  });

  it('should clear the terminating state when the cancel request fails', () => {
    component.difficultyJob = {
      id: 'job-cancel-fails',
      suiteId: 1,
      suiteName: 'Default Suite',
      scope: 'suite',
      assessorConfigId: 1,
      assessorDisplayName: 'Test Assessor',
      startedAtUtc: '2026-09-02T00:00:00Z',
      completedAtUtc: null,
      status: 'Running',
      ratedCount: 0,
      failedCount: 0,
      totalCount: 1,
      totalModelCalls: 0,
      promptTokens: 0,
      outputTokens: 0,
      items: [],
      log: []
    };
    benchmarkServiceMock.cancelDifficultyAssessment.and.returnValue(throwError(() => ({ status: 500, error: 'Boom' })));

    component.terminateDifficultyAssessment();

    expect(component.terminatingDifficultyJob).toBeFalse();
    expect(component.actionErrorMessage).toBe('Boom');
  });

  it('should retry failed questions by starting assessment with failed question ids', () => {
    const failedJob = {
      id: 'job-failed',
      suiteId: 1,
      suiteName: 'Default Suite',
      scope: 'suite',
      assessorConfigId: 1,
      assessorDisplayName: 'Test Assessor',
      startedAtUtc: '2026-09-02T00:00:00Z',
      completedAtUtc: '2026-09-02T00:01:00Z',
      status: 'Failed',
      ratedCount: 1,
      failedCount: 2,
      totalCount: 3,
      totalModelCalls: 3,
      promptTokens: 150,
      outputTokens: 60,
      items: [
        { questionId: 101, orderIndex: 1, questionTextExcerpt: 'Q1', status: 'Rated', difficulty: 50, errorMessage: null },
        { questionId: 102, orderIndex: 2, questionTextExcerpt: 'Q2', status: 'Failed', difficulty: null, errorMessage: 'Timeout' },
        { questionId: 103, orderIndex: 3, questionTextExcerpt: 'Q3', status: 'Failed', difficulty: null, errorMessage: 'Parse error' }
      ],
      log: []
    };

    component.difficultyJob = failedJob;
    benchmarkServiceMock.startDifficultyAssessment.and.returnValue(of({ jobId: 'retry-job-1' }));
    benchmarkServiceMock.getDifficultyAssessment.and.returnValue(of({ ...failedJob, id: 'retry-job-1', status: 'Running' }));

    component.retryFailedQuestions();

    expect(benchmarkServiceMock.startDifficultyAssessment).toHaveBeenCalledWith({
      suiteId: 1,
      questionIds: [102, 103],
      assessorModelConfigurationId: 1
    });
  });

  describe('difficulty diagnostics copy button', () => {
    const buildFailedJob = () => ({
      id: 'job-diag',
      suiteId: 1,
      suiteName: 'Default Suite',
      scope: 'suite',
      assessorConfigId: 1,
      assessorDisplayName: 'Test Assessor',
      startedAtUtc: '2026-09-02T00:00:00Z',
      completedAtUtc: '2026-09-02T00:01:00Z',
      status: 'Failed',
      ratedCount: 1,
      failedCount: 1,
      totalCount: 2,
      totalModelCalls: 3,
      promptTokens: 150,
      outputTokens: 60,
      items: [
        { questionId: 101, orderIndex: 1, questionTextExcerpt: 'Q1', status: 'Rated', difficulty: 50, errorMessage: null },
        { questionId: 102, orderIndex: 2, questionTextExcerpt: 'Q2', status: 'Failed', difficulty: null, errorMessage: 'Timeout' }
      ],
      log: []
    });

    beforeEach(() => {
      component.difficultyDialogPhase = 'progress';
      component.difficultyJob = buildFailedJob();
      fixture.detectChanges();
    });

    it('should write the diagnostics text to the clipboard, announce it, and reset after the timeout', fakeAsync(() => {
      const writeTextSpy = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
      const expectedText = component.difficultyDiagnosticsText;
      expect(expectedText).toContain('Job ID: job-diag');

      const copyButton = fixture.nativeElement.querySelector(
        'button[aria-label="Copy difficulty assessment diagnostics"]'
      ) as HTMLButtonElement;
      expect(copyButton).toBeTruthy();

      copyButton.click();
      tick();
      fixture.detectChanges();

      expect(writeTextSpy).toHaveBeenCalledWith(expectedText);
      expect(component.copiedDiagnostics).toBeTrue();

      const status = fixture.nativeElement.querySelector('.diagnostics-copy-status') as HTMLElement;
      expect(status.textContent?.trim()).toBe('Diagnostics copied to clipboard');

      tick(2000);
      fixture.detectChanges();

      expect(component.copiedDiagnostics).toBeFalse();
      expect(status.textContent?.trim()).toBe('');
    }));

    it('should surface a clipboard failure in the inline dialog error rather than throwing', fakeAsync(() => {
      spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.reject(new Error('denied')));

      const copyButton = fixture.nativeElement.querySelector(
        'button[aria-label="Copy difficulty assessment diagnostics"]'
      ) as HTMLButtonElement;

      copyButton.click();
      tick();
      fixture.detectChanges();

      expect(component.copiedDiagnostics).toBeFalse();
      expect(component.difficultyDialogError).toBe('Could not copy the diagnostics to the clipboard.');
    }));
  });

  it('should disable start button and render warning notice when selected suite is not fully assessed', () => {
    component.activeSubTab = 'run';
    component.selectedSuiteId = 1;
    component.testedConfigId = 1;
    component.assessorConfigId = 1;
    component.suites = [
      {
        id: 1,
        name: 'Incomplete Suite',
        description: null,
        createdAtUtc: '2026-09-01T00:00:00Z',
        modifiedAtUtc: null,
        questionCount: 10,
        assessedQuestionCount: 5,
        difficultyFullyAssessed: false
      }
    ];
    fixture.detectChanges();

    expect(component.canStartRun).toBeFalse();

    const warningEl = fixture.nativeElement.querySelector('.alert.alert-warning');
    expect(warningEl).toBeTruthy();
    expect(warningEl.textContent).toContain('Difficulty 5/10 Assessed');
    expect(warningEl.textContent).toContain('Every question must have an assessed difficulty');

    const startBtn = fixture.nativeElement.querySelector('.form-actions button.btn-gh');
    expect(startBtn.getAttribute('aria-disabled')).toBe('true');
  });

  it('should render per-question assessor info and badges when assessed, or not assessed message', () => {
    component.activeSubTab = 'suites';
    component.currentSuiteForQuestions = {
      id: 1,
      name: 'Test Suite',
      description: null,
      createdAtUtc: '2026-09-01T00:00:00Z',
      modifiedAtUtc: null,
      questionCount: 2,
      assessedQuestionCount: 1,
      difficultyFullyAssessed: false
    };
    component.questions = [
      {
        id: 1,
        benchmarkSuiteId: 1,
        orderIndex: 1,
        questionText: 'Question 1',
        difficulty: 1,
        expectedPoints: null,
        assessedDifficulty: 40,
        assessedDifficultyModel: 'Claude 3.5 Sonnet',
        assessedDifficultyThinkingLevelUsed: 'High',
        assessedDifficultyReasoningModeUsed: 'Extended',
        assessedDifficultyServiceTierUsed: 'standard_only',
        assessedDifficultyAtUtc: '2026-09-01T12:00:00Z',
        createdAtUtc: '2026-09-01T00:00:00Z'
      },
      {
        id: 2,
        benchmarkSuiteId: 1,
        orderIndex: 2,
        questionText: 'Question 2',
        difficulty: 2,
        expectedPoints: null,
        assessedDifficulty: null,
        assessedDifficultyModel: null,
        createdAtUtc: '2026-09-01T00:00:00Z'
      }
    ];
    fixture.detectChanges();

    const assessorInfos = fixture.nativeElement.querySelectorAll('.q-assessor-info');
    expect(assessorInfos.length).toBe(2);

    // Question 1: assessed with badges
    expect(assessorInfos[0].textContent).toContain('Assessed by');
    expect(assessorInfos[0].textContent).toContain('Claude 3.5 Sonnet');
    const badges = assessorInfos[0].querySelectorAll('.q-model-badge');
    expect(badges.length).toBe(3);
    expect(badges[0].textContent).toContain('High');
    expect(badges[1].textContent).toContain('Extended');
    expect(badges[2].textContent).toContain('Standard Only');

    // Question 2: not assessed
    expect(assessorInfos[1].textContent).toContain('Difficulty not assessed');
  });

  // ---------------------------------------------------------------------------
  // Tab semantics and keyboard navigation for the benchmark sub-navigation.
  // Regression guards for the harmonization that replaced .subnav-btn pill
  // buttons with the shared .gh-tabs / .gh-tab ARIA tab widget.
  // ---------------------------------------------------------------------------
  describe('sub-navigation tab widget', () => {
    const tabList = () => fixture.nativeElement.querySelector('[role="tablist"]');
    // Scoped to the sub-navigation's own tablist: the question form dialog carries a second
    // one for the markdown editor's view modes.
    const tabs = () =>
      Array.from(tabList().querySelectorAll('[role="tab"]')) as HTMLButtonElement[];

    beforeEach(() => fixture.detectChanges());

    it('should expose the sub-navigation as a labelled tablist', () => {
      expect(tabList()).toBeTruthy();
      expect(tabList().getAttribute('aria-label')).toBe('Benchmark sections');
      expect(tabs().length).toBe(6);
    });

    it('should mark exactly one tab selected, matching activeSubTab', () => {
      const selected = tabs().filter(t => t.getAttribute('aria-selected') === 'true');
      expect(selected.length).toBe(1);
      expect(selected[0].id).toBe('bm-tab-' + component.activeSubTab);
    });

    it('should give exactly one tab tabindex="0" and the rest tabindex="-1"', () => {
      const all = tabs();
      expect(all.filter(t => t.getAttribute('tabindex') === '0').length).toBe(1);
      expect(all.filter(t => t.getAttribute('tabindex') === '-1').length).toBe(5);
    });

    it('should wrap forward from the last tab to the first with ArrowRight', () => {
      component.activeSubTab = 'modelcomparison';
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 5);
      expect(component.activeSubTab).toBe('run');
    });

    it('should wrap backward from the first tab to the last with ArrowLeft', () => {
      component.activeSubTab = 'run';
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }), 0);
      expect(component.activeSubTab).toBe('modelcomparison');
    });

    it('should select the first and last tab with Home and End', () => {
      component.activeSubTab = 'history';
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'End' }), 1);
      expect(component.activeSubTab).toBe('modelcomparison');

      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'Home' }), 5);
      expect(component.activeSubTab).toBe('run');
    });

    it('should ignore keys that are not part of the tab keyboard model', () => {
      component.activeSubTab = 'history';
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'a' }), 1);
      expect(component.activeSubTab).toBe('history');
    });

    it('should move focus to the newly selected tab after a keyboard change', () => {
      tabs()[0].focus();
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 0);
      fixture.detectChanges();
      expect(document.activeElement).toBe(
        fixture.nativeElement.querySelector('#bm-tab-history')
      );
    });

    it('should render a tabpanel labelled by the selected tab', () => {
      const panel = fixture.nativeElement.querySelector('[role="tabpanel"]');
      expect(panel).toBeTruthy();
      expect(panel.id).toBe('bm-panel-run');
      expect(panel.getAttribute('aria-labelledby')).toBe('bm-tab-run');
      expect(panel.getAttribute('tabindex')).toBe('0');
      expect(fixture.nativeElement.querySelector('#bm-tab-run')).toBeTruthy();
    });

    it('should render the Multi-Run Analysis panel, and nothing else, on the multirun tab', () => {
      // Clicked rather than assigned: the click is what marks the view dirty, so the panel this
      // asserts on is the one an operator actually gets.
      fixture.nativeElement.querySelector('#bm-tab-multirun').click();
      fixture.detectChanges();

      const panel = fixture.nativeElement.querySelector('[role="tabpanel"]');
      expect(panel.id).toBe('bm-panel-multirun');
      expect(panel.getAttribute('aria-labelledby')).toBe('bm-tab-multirun');
      // The panel is the MultiRunComponent's own; the host contributes no data loading of its own.
      expect(panel.querySelector('app-benchmark-multi-run')).toBeTruthy();
    });

    it('should hand the selected suite to the Multi-Run Analysis panel', () => {
      component.selectedSuiteId = 5;
      fixture.nativeElement.querySelector('#bm-tab-multirun').click();
      fixture.detectChanges();

      const multiRun = fixture.debugElement.query(By.directive(MultiRunComponent));
      expect(multiRun).toBeTruthy();
      expect((multiRun.componentInstance as MultiRunComponent).suiteId).toBe(5);
    });

    it('should load history when the history tab is selected', () => {
      benchmarkServiceMock.getRuns.calls.reset();
      component.selectSubTab('history');
      expect(benchmarkServiceMock.getRuns).toHaveBeenCalled();
    });

    it('should load suites when the suites tab is selected', () => {
      benchmarkServiceMock.getSuites.calls.reset();
      component.selectSubTab('suites');
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalled();
    });

    it('should render the Scoring Profiles panel on the profiles tab', () => {
      fixture.nativeElement.querySelector('#bm-tab-profiles').click();
      fixture.detectChanges();

      const panel = fixture.nativeElement.querySelector('[role="tabpanel"]');
      expect(panel).toBeTruthy();
      expect(panel.id).toBe('bm-panel-profiles');
      expect(panel.getAttribute('aria-labelledby')).toBe('bm-tab-profiles');
      expect(panel.querySelector('.profiles-list')).toBeTruthy();
    });

    it('should load profiles when the profiles tab is selected', () => {
      benchmarkServiceMock.getScoringProfiles.calls.reset();
      component.selectSubTab('profiles');
      expect(benchmarkServiceMock.getScoringProfiles).toHaveBeenCalled();
    });
  });

  describe('formatStatusLabel', () => {
    it('should format CompletedWithLimits as "Completed with limits"', () => {
      expect(component.formatStatusLabel('CompletedWithLimits')).toBe('Completed with limits');
      expect(component.formatStatusLabel(6)).toBe('Completed with limits');
    });

    it('should format CompletedWithErrors as "Completed with errors"', () => {
      expect(component.formatStatusLabel('CompletedWithErrors')).toBe('Completed with errors');
      expect(component.formatStatusLabel(3)).toBe('Completed with errors');
    });

    it('should return standard status for other values', () => {
      expect(component.formatStatusLabel('Running')).toBe('Running');
      expect(component.formatStatusLabel(1)).toBe('Running');
      expect(component.formatStatusLabel('Completed')).toBe('Completed');
      expect(component.formatStatusLabel(2)).toBe('Completed');
      expect(component.formatStatusLabel('Failed')).toBe('Failed');
      expect(component.formatStatusLabel(4)).toBe('Failed');
      expect(component.formatStatusLabel('Canceled')).toBe('Canceled');
      expect(component.formatStatusLabel(5)).toBe('Canceled');
    });
  });

  describe('runCountInput layout', () => {
    it('should render narrow run count input inside the Execution group', () => {
      fixture.detectChanges();
      const input = fixture.nativeElement.querySelector('#runCountInput');
      expect(input).toBeTruthy();
      expect(input.classList.contains('run-count-input')).toBeTrue();
      expect(input.closest('.setup-group-exec')).toBeTruthy();
    });
  });

  // ---------------------------------------------------------------------------
  // Button harmonization guards. These assert the shared design-system
  // vocabulary is used and that no control is left without an accessible name.
  // ---------------------------------------------------------------------------
  describe('button harmonization', () => {
    /** Renders each sub-tab in turn so every button in the view is inspected. */
    function forEachSubTab(check: (where: string) => void): void {
      for (const tab of ['run', 'history', 'suites', 'profiles'] as const) {
        component.activeSubTab = tab;
        fixture.detectChanges();
        check(tab);
      }
    }

    it('should give every button an accessible name', () => {
      forEachSubTab(where => {
        const buttons = Array.from(
          fixture.nativeElement.querySelectorAll('button')
        ) as HTMLButtonElement[];
        expect(buttons.length).toBeGreaterThan(0);

        for (const btn of buttons) {
          const name = (btn.textContent || '').trim() || btn.getAttribute('aria-label');
          expect(name)
            .withContext(`unnamed button in "${where}" tab: ${btn.outerHTML.slice(0, 120)}`)
            .toBeTruthy();
        }
      });
    });

    it('should give every button an explicit type="button"', () => {
      forEachSubTab(where => {
        const untyped = (
          Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[]
        ).filter(b => b.getAttribute('type') !== 'button');

        expect(untyped.map(b => b.outerHTML.slice(0, 100)))
          .withContext(`buttons without type="button" in "${where}" tab`)
          .toEqual([]);
      });
    });

    it('should not use the title attribute on any button', () => {
      forEachSubTab(where => {
        const titled = (
          Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[]
        ).filter(b => b.hasAttribute('title'));

        expect(titled.map(b => b.getAttribute('title')))
          .withContext(`buttons still using title in "${where}" tab`)
          .toEqual([]);
      });
    });

    it('should not use the invented btn-gh-primary or btn-gh-danger variants', () => {
      forEachSubTab(where => {
        const stale = fixture.nativeElement.querySelectorAll(
          '.btn-gh-primary, .btn-gh-danger, .btn-gh-icon, .btn-danger-icon, .subnav-btn'
        );
        expect(stale.length)
          .withContext(`stale button classes in "${where}" tab`)
          .toBe(0);
      });
    });

    it('should pair every icon-only action button with an interest-triggered tooltip', () => {
      component.activeSubTab = 'suites';
      fixture.detectChanges();

      const triggers = Array.from(
        fixture.nativeElement.querySelectorAll('button[interestfor]')
      ) as HTMLButtonElement[];
      expect(triggers.length).toBeGreaterThan(0);

      for (const trigger of triggers) {
        const id = trigger.getAttribute('interestfor')!;
        const tooltip = fixture.nativeElement.querySelector(`#${id}`);
        expect(tooltip)
          .withContext(`no tooltip element for interestfor="${id}"`)
          .toBeTruthy();
        expect(tooltip.getAttribute('popover')).toBe('hint');
        // The polyfill cannot use the implicit anchor interestfor establishes,
        // so both ends must name it explicitly.
        expect(trigger.getAttribute('style')).toContain(`anchor-name: --${id}`);
        expect(tooltip.getAttribute('style')).toContain(`position-anchor: --${id}`);
      }
    });

    it('should default the confirm dialog to the delete icon and class', () => {
      expect(component.confirmDialogIcon).toBe('delete');
      expect(component.confirmDialogButtonClass).toBe('btn-gh btn-gh-delete');
    });

    it('should announce active run progress in a live region', () => {
      component.activeSubTab = 'run';
      component.activeRunDetail = {
        id: 7,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Test Model',
        assessorModelDisplayNameUsed: 'Test Model',
        totalQuestionCount: 10,
        answers: []
      } as any;
      fixture.detectChanges();

      const progress = fixture.nativeElement.querySelector('.banner-progress');
      expect(progress).toBeTruthy();
      expect(progress.getAttribute('role')).toBe('status');
    });
  });

  describe('run progress dialog', () => {
    /**
     * A run detail fixture. Defaults describe a run mid-answering; the overrides let each
     * test move it to a later stage without restating the whole DTO.
     */
    function buildRun(overrides: any = {}): any {
      return {
        id: 42,
        benchmarkSuiteId: 1,
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Test Model',
        testedModelProviderUsed: 'Anthropic',
        testedModelIdUsed: 'claude-3-5-sonnet',
        testedModelParallelExecutionModeUsed: 2,
        assessorModelDisplayNameUsed: 'Test Assessor',
        assessorModelProviderUsed: 'Anthropic',
        assessorModelIdUsed: 'claude-3-5-sonnet',
        startedByUserName: 'admin',
        status: 'Running',
        startedAtUtc: '2026-09-02T00:00:00Z',
        completedAtUtc: null,
        totalAnswerDurationMs: 0,
        scoringProfileName: 'Default Intelligence Profile',
        scoringProfileId: 1,
        scoringMethodVersion: 2,
        difficultyFallbackUsed: false,
        speedMeasurementDegraded: false,
        maxParallelQuestionsUsed: 1,
        answeredQuestionCount: 0,
        totalQuestionCount: 3,
        assessmentParseFailed: false,
        totalInputTokens: 0,
        totalOutputTokens: 0,
        totalCacheReadTokens: 0,
        totalCacheCreationTokens: 0,
        totalDurationMs: 0,
        errorMessage: null,
        answers: [],
        ...overrides
      };
    }

    function buildAnswer(orderIndex: number, overrides: any = {}): any {
      return {
        id: 100 + orderIndex,
        benchmarkRunId: 42,
        orderIndex,
        questionText: `Answered question ${orderIndex}`,
        difficulty: 1,
        answerText: `SECRET ANSWER BODY ${orderIndex}`,
        thoughtText: `SECRET THOUGHT BODY ${orderIndex}`,
        reviewComment: `SECRET REVIEW BODY ${orderIndex}`,
        status: 'Ok',
        assessmentStatus: 'Scored',
        durationMs: 1234,
        timeToFirstTokenMs: 456,
        inputTokens: 900,
        outputTokens: 310,
        ...overrides
      };
    }

    it('should open the dialog when a benchmark run starts successfully', () => {
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 1;
      fixture.detectChanges();

      const showModal = spyOn(component.runProgressDialog.nativeElement, 'showModal');
      benchmarkServiceMock.startRun.and.returnValue(of({ runId: 42 }));
      benchmarkServiceMock.getRun.and.returnValue(of(buildRun()));

      component.startBenchmark();

      expect(showModal).toHaveBeenCalled();
      expect(component.isRunProgressDialogOpen).toBeTrue();
      component.closeRunProgressDialog();
    });

    describe('board quote check before start', () => {
      function selectSuiteWithBoard(): void {
        fixture.detectChanges();
        component.suites = [{
          id: 1, name: 'Snapshot Suite', description: null, createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null,
          questionCount: 3, assessedQuestionCount: 3, difficultyFullyAssessed: true, gameSnapshotId: 7
        }];
        component.selectedSuiteId = 1;
        component.testedConfigId = 1;
        component.assessorConfigId = 1;
        spyOn(component.runProgressDialog.nativeElement, 'showModal');
        benchmarkServiceMock.startRun.and.returnValue(of({ runId: 42 }));
        benchmarkServiceMock.getRun.and.returnValue(of(buildRun()));
      }

      const missingCheck = {
        bulletCount: 10, checkedLiteralCount: 10, unquotedBulletCount: 0, unquotedBullets: [],
        missingLiterals: [{ questionId: 9, orderIndex: 3, literal: 'a blessed +1 long sword', lineExcerpt: 'x' }]
      };

      it('should warn and wait for acknowledgement when the suite rubrics quote text the board lacks', () => {
        selectSuiteWithBoard();
        const showWarning = spyOn(component.boardQuoteWarningDialog!.nativeElement, 'showModal');
        benchmarkServiceMock.getBoardFactsCheck.and.returnValue(of(missingCheck));

        component.startBenchmark();

        expect(benchmarkServiceMock.getBoardFactsCheck).toHaveBeenCalledWith(1);
        expect(benchmarkServiceMock.startRun).not.toHaveBeenCalled();
        expect(showWarning).toHaveBeenCalled();
        expect(component.startingRun).toBeFalse();
        const text = (component.boardQuoteWarningDialog!.nativeElement.textContent || '').replace(/\s+/g, ' ');
        expect(text).toContain('These rubrics quote text the board does not contain; grades on them will rest on stale facts.');
        expect(text).toContain('Q3: "a blessed +1 long sword"');

        const buttons = Array.from(component.boardQuoteWarningDialog!.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
        for (const btn of buttons) {
          expect(btn.getAttribute('type')).toBe('button');
          expect((btn.textContent || '').trim() || btn.getAttribute('aria-label')).toBeTruthy();
        }
        const acknowledge = buttons.find(b => (b.textContent || '').includes('Acknowledge & Start Run'))!;
        acknowledge.click();

        expect(benchmarkServiceMock.getBoardFactsCheck).toHaveBeenCalledTimes(1);
        expect(benchmarkServiceMock.startRun).toHaveBeenCalledTimes(1);
        expect(component.launchBoardFactsCheck).toBeNull();
        component.closeRunProgressDialog();
      });

      it('should start nothing when the warning is cancelled', () => {
        selectSuiteWithBoard();
        spyOn(component.boardQuoteWarningDialog!.nativeElement, 'showModal');
        benchmarkServiceMock.getBoardFactsCheck.and.returnValue(of(missingCheck));

        component.startBenchmark();
        component.closeBoardQuoteWarningDialog();

        expect(benchmarkServiceMock.startRun).not.toHaveBeenCalled();
        expect(component.launchBoardFactsCheck).toBeNull();
      });

      it('should start straight away when every quote is on the board', () => {
        selectSuiteWithBoard();
        const showWarning = spyOn(component.boardQuoteWarningDialog!.nativeElement, 'showModal');
        benchmarkServiceMock.getBoardFactsCheck.and.returnValue(of({ ...missingCheck, missingLiterals: [] }));

        component.startBenchmark();

        expect(showWarning).not.toHaveBeenCalled();
        expect(benchmarkServiceMock.startRun).toHaveBeenCalledTimes(1);
        component.closeRunProgressDialog();
      });

      it('should start anyway when the check itself fails, since it is advisory', () => {
        selectSuiteWithBoard();
        spyOn(console, 'warn');
        benchmarkServiceMock.getBoardFactsCheck.and.returnValue(throwError(() => ({ status: 500 })));

        component.startBenchmark();

        expect(benchmarkServiceMock.startRun).toHaveBeenCalledTimes(1);
        component.closeRunProgressDialog();
      });

      it('should not check a suite that has no board', () => {
        selectSuiteWithBoard();
        component.suites = [{ ...component.suites[0], gameSnapshotId: null }];

        component.startBenchmark();

        expect(benchmarkServiceMock.getBoardFactsCheck).not.toHaveBeenCalled();
        expect(benchmarkServiceMock.startRun).toHaveBeenCalledTimes(1);
        component.closeRunProgressDialog();
      });
    });

    it('should derive the run stage from the run detail', () => {
      component.activeRunDetail = buildRun({ answers: [buildAnswer(1)] });
      expect(component.runStage).toBe('answering');

      // Answering and assessing are one stage: the executor assesses each answer immediately
      // after producing it, inside the same loop, so they never separate in wall-clock terms.
      component.activeRunDetail = buildRun({
        answers: [
          buildAnswer(1),
          buildAnswer(2, { assessmentStatus: 'Pending' }),
          buildAnswer(3, { assessmentStatus: 'Assessing' })
        ]
      });
      expect(component.runStage).toBe('answering');

      component.activeRunDetail = buildRun({
        answers: [buildAnswer(1), buildAnswer(2), buildAnswer(3)]
      });
      expect(component.runStage).toBe('finalizing');

      component.activeRunDetail = buildRun({
        status: 'Completed',
        completedAtUtc: '2026-09-02T00:05:00Z',
        answers: [buildAnswer(1), buildAnswer(2), buildAnswer(3)]
      });
      expect(component.runStage).toBe('terminal');
      expect(component.runIsTerminal).toBeTrue();
    });

    it('should merge suite questions with answers and mark unanswered questions Pending', () => {
      component.activeRunDetail = buildRun({ answers: [buildAnswer(2, { benchmarkQuestionId: 2 })] });
      component.runProgressQuestions = [
        { id: 1, benchmarkSuiteId: 1, orderIndex: 1, questionText: 'First question', difficulty: 1, expectedPoints: null, createdAtUtc: '2026-09-01T00:00:00Z' },
        { id: 2, benchmarkSuiteId: 1, orderIndex: 2, questionText: 'Second question', difficulty: 1, expectedPoints: null, createdAtUtc: '2026-09-01T00:00:00Z' },
        { id: 3, benchmarkSuiteId: 1, orderIndex: 3, questionText: 'Third question', difficulty: 1, expectedPoints: null, createdAtUtc: '2026-09-01T00:00:00Z' }
      ];

      const rows = component.runProgressRows;
      expect(rows.length).toBe(3);
      expect(rows.map(r => r.orderIndex)).toEqual([1, 2, 3]);
      expect(rows[0].status).toBe('Pending');
      expect(rows[0].questionText).toBe('First question');
      expect(rows[1].status).toBe('Ok');
      expect(rows[2].status).toBe('Pending');

      expect(component.runRowChipLabel(rows[0])).toBe('Pending');
      expect(component.runRowChipClass(rows[0])).toBe('status-pending');
      expect(component.runRowChipLabel(rows[1])).toBe('Scored');
      expect(component.runRowChipClass(rows[1])).toBe('status-scored');
    });

    it('should label an answered but unassessed question Answered rather than guessing Assessing', () => {
      component.activeRunDetail = buildRun({
        answers: [buildAnswer(1, { assessmentStatus: 'Pending' })]
      });
      const row = component.runProgressRows[0];
      expect(component.runRowChipLabel(row)).toBe('Answered');
      expect(component.runRowChipClass(row)).toBe('status-ok');
    });

    it('should expose exactly one polling live region in the dialog', () => {
      component.activeRunDetail = buildRun({ answers: [buildAnswer(1)] });
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const dialog = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog') as HTMLElement;
      expect(dialog).toBeTruthy();

      const liveRegions = Array.from(dialog.querySelectorAll('[role="status"], [role="alert"]'))
        .filter(el => !el.closest('.job-diagnostics'));
      expect(liveRegions.length).toBe(1);
      expect(liveRegions[0].classList.contains('progress-status')).toBeTrue();
      expect(liveRegions[0].getAttribute('aria-live')).toBe('polite');

      // It moved into the Assessments block when the claims and second-opinion bars were
      // replaced by counters; the dialog must not have gained a second one on the way.
      const block = liveRegions[0].closest('.job-progress-block') as HTMLElement;
      expect(block).toBeTruthy();
      expect(block.querySelector('#runAssessmentsProgressBar')).toBeTruthy();
    });

    it('should hide the active run banner while the dialog is open', () => {
      component.activeSubTab = 'run';
      component.activeRunDetail = buildRun({ answers: [] });
      fixture.detectChanges();
      const bannerShown = () => !!fixture.nativeElement.querySelector('.active-run-banner');
      expect(bannerShown()).toBeTrue();

      // Driven through the real API: the flag is only ever set by these two methods,
      // and each refreshes the view itself.
      spyOn(component.runProgressDialog.nativeElement, 'showModal');
      component.openRunProgressDialog();
      expect(component.isRunProgressDialogOpen).toBeTrue();
      expect(bannerShown()).toBeFalse();

      component.closeRunProgressDialog();
      expect(bannerShown()).toBeTrue();
    });

    it('should give every button in the open dialog an accessible name, type, and no title', () => {
      component.activeRunDetail = buildRun({
        status: 'CompletedWithErrors',
        completedAtUtc: '2026-09-02T00:05:00Z',
        answers: [buildAnswer(1), buildAnswer(2, { status: 'ProviderError', httpStatusCode: 429, errorMessage: 'Rate limited' })]
      });
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const dialog = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog');
      const buttons = Array.from(dialog.querySelectorAll('button')) as HTMLButtonElement[];
      expect(buttons.length).toBeGreaterThan(0);

      for (const btn of buttons) {
        const name = (btn.textContent || '').trim() || btn.getAttribute('aria-label');
        expect(name).withContext(btn.outerHTML.slice(0, 120)).toBeTruthy();
        expect(btn.getAttribute('type')).toBe('button');
        expect(btn.hasAttribute('title')).toBeFalse();
      }

      expect(dialog.querySelectorAll('.btn-gh-primary, .btn-gh-danger, .btn-gh-icon').length).toBe(0);

      // The icon-only copy button must still carry its interest-triggered tooltip.
      const copyButton = dialog.querySelector('button[aria-label="Copy benchmark run diagnostics"]') as HTMLButtonElement;
      expect(copyButton).toBeTruthy();
      const tooltipId = copyButton.getAttribute('interestfor')!;
      expect(tooltipId).toBe('tip-copy-run-diagnostics');
      const tooltip = dialog.querySelector(`#${tooltipId}`);
      expect(tooltip).toBeTruthy();
      expect(tooltip!.getAttribute('popover')).toBe('hint');
      expect(copyButton.getAttribute('style')).toContain(`anchor-name: --${tooltipId}`);
      expect(tooltip!.getAttribute('style')).toContain(`position-anchor: --${tooltipId}`);

      // So must the icon-only download button beside it.
      const downloadButton = dialog.querySelector('button[aria-label="Download benchmark run diagnostics as a text file"]') as HTMLButtonElement;
      expect(downloadButton).toBeTruthy();
      const downloadTipId = downloadButton.getAttribute('interestfor')!;
      expect(downloadTipId).toBe('tip-download-run-diagnostics');
      const downloadTip = dialog.querySelector(`#${downloadTipId}`);
      expect(downloadTip).toBeTruthy();
      expect(downloadTip!.getAttribute('popover')).toBe('hint');
      expect(downloadTip!.textContent!.trim()).toBe('Download diagnostics');
      expect(downloadButton.getAttribute('style')).toContain(`anchor-name: --${downloadTipId}`);
      expect(downloadTip!.getAttribute('style')).toContain(`position-anchor: --${downloadTipId}`);
      expect(copyButton.nextElementSibling!.nextElementSibling).toBe(downloadButton);
    });

    /** The diagnostics text without its capture timestamp, which differs between two reads. */
    function withoutCaptureTime(text: string): string {
      return text.replace(/^Captured:.*$/m, '');
    }

    it('should download the run diagnostics as a text file named for suite, model and run', async () => {
      component.activeRunDetail = buildRun({
        status: 'CompletedWithErrors',
        suiteName: 'Snapshot: Tommi2 2026-09-17',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        id: 55,
        completedAtUtc: '2026-09-02T00:05:00Z',
        answers: [buildAnswer(1), buildAnswer(2, { status: 'ProviderError', httpStatusCode: 429, errorMessage: 'Rate limited' })]
      });
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const create = spyOn(URL, 'createObjectURL').and.returnValue('blob:test-url');
      spyOn(URL, 'revokeObjectURL');
      const click = spyOn(HTMLAnchorElement.prototype, 'click');
      const expectedText = component.runDiagnosticsText;

      const downloadButton = fixture.nativeElement.querySelector(
        'button[aria-label="Download benchmark run diagnostics as a text file"]'
      ) as HTMLButtonElement;
      downloadButton.click();

      expect(click).toHaveBeenCalledTimes(1);
      const anchor = click.calls.mostRecent().object as HTMLAnchorElement;
      expect(anchor.download).toBe('snapshot-tommi2-2026-09-17_gemini-3.7-flash_run55_diagnostics.txt');
      expect(create).toHaveBeenCalledTimes(1);
      const blob = create.calls.mostRecent().args[0] as Blob;
      expect(blob.type).toBe('text/plain;charset=utf-8');
      expect(withoutCaptureTime(await blob.text())).toBe(withoutCaptureTime(expectedText));
    });

    it('should fall back to a generic diagnostics file name when no run is loaded', () => {
      component.activeRunDetail = null;

      expect(component.runDiagnosticsFileName).toBe('overseer-benchmark-run-diagnostics.txt');
    });

    it('should copy the run diagnostics, announce it, and reset after the timeout', fakeAsync(() => {
      component.activeRunDetail = buildRun({
        status: 'CompletedWithErrors',
        completedAtUtc: '2026-09-02T00:05:00Z',
        answers: [buildAnswer(1), buildAnswer(2, { status: 'ProviderError', httpStatusCode: 429, errorMessage: 'Rate limited' })]
      });
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const writeTextSpy = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
      const expectedText = component.runDiagnosticsText;
      expect(expectedText).toContain('Run ID: 42');

      const copyButton = fixture.nativeElement.querySelector(
        'button[aria-label="Copy benchmark run diagnostics"]'
      ) as HTMLButtonElement;
      expect(copyButton).toBeTruthy();

      copyButton.click();
      tick();
      fixture.detectChanges();

      expect(writeTextSpy).toHaveBeenCalledWith(expectedText);
      expect(component.copiedRunDiagnostics).toBeTrue();
      expect(component.runDiagnosticsCopyStatus).toBe('Diagnostics copied to clipboard');

      tick(2000);
      fixture.detectChanges();

      expect(component.copiedRunDiagnostics).toBeFalse();
      expect(component.runDiagnosticsCopyStatus).toBe('');
    }));

    it('should surface a run diagnostics clipboard failure inline rather than throwing', fakeAsync(() => {
      component.activeRunDetail = buildRun({
        status: 'Failed',
        completedAtUtc: '2026-09-02T00:05:00Z',
        answers: [buildAnswer(1, { status: 'Failed', errorMessage: 'boom' })]
      });
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.reject(new Error('denied')));

      const copyButton = fixture.nativeElement.querySelector(
        'button[aria-label="Copy benchmark run diagnostics"]'
      ) as HTMLButtonElement;
      copyButton.click();
      tick();
      fixture.detectChanges();

      expect(component.copiedRunDiagnostics).toBeFalse();
      expect(component.runDiagnosticsCopyStatus).toBe('Could not copy the diagnostics to the clipboard.');
      expect(component.runErrorMessage).toBe('Could not copy the benchmark run diagnostics to the clipboard.');
    }));

    it('should not leak answer, thought, or assessor comment text into the diagnostics', () => {
      component.activeRunDetail = buildRun({
        answers: [buildAnswer(1), buildAnswer(2, { status: 'ProviderError', httpStatusCode: 429, errorMessage: 'Rate limited' })]
      });

      const text = component.runDiagnosticsText;
      expect(text).not.toContain('SECRET ANSWER BODY');
      expect(text).not.toContain('SECRET THOUGHT BODY');
      expect(text).not.toContain('SECRET REVIEW BODY');
      // What it must contain: the failure the operator would report.
      expect(text).toContain('http=429');
      expect(text).toContain('error: Rate limited');
      // Section headers and timezone/timestamp diagnostics
      expect(text).toContain('--- RUN ---');
      expect(text).toContain('--- POLLING ---');
      expect(text).toContain('--- QUESTIONS ---');
      expect(text).toContain('Started (raw):');
      expect(text).toContain('Started (parsed):');
    });

    it('prints the ticker mode on the Run poll line, and an attempts section for the sound and the notification', () => {
      component.activeRunDetail = buildRun({ answers: [] });
      benchmarkServiceMock.getRun.and.returnValue(of(buildRun({ id: 42, status: 'Running', answers: [] })));
      (component as any).startPolling(42);

      // Pushed directly rather than exercised through a real play() call, so this spec does not
      // depend on the actual browser audio stack; the sound service's own spec exercises play().
      const soundService = TestBed.inject(BenchmarkCompletionSoundService);
      (soundService as any).attempts.push({
        atUtc: '2026-09-19T00:00:00.000Z', key: 'run:999', hidden: false, focused: true,
        path: 'element', contextStateBefore: null, contextStateAfter: null,
        clockAdvanced: null, rebuilt: false, outcome: 'played'
      });

      const text = component.runDiagnosticsText;
      expect(text).toContain('Run poll: active every 2000 ms (timer)');
      expect(text).toContain('  attempts:');
      expect(text).toContain('key=run:999');

      (component as any).stopPolling();
    });

    it('should reattach to a run already in progress without opening the dialog', () => {
      benchmarkServiceMock.getActiveRun.and.returnValue(of({ runId: 77 }));
      benchmarkServiceMock.getRun.and.returnValue(of(buildRun({ id: 77 })));
      const showModal = spyOn(component.runProgressDialog.nativeElement, 'showModal');

      component.checkActiveRun();

      expect(component.activeRunId).toBe(77);
      expect(benchmarkServiceMock.getRun).toHaveBeenCalledWith(77);
      expect(component.isRunProgressDialogOpen).toBeFalse();
      expect(showModal).not.toHaveBeenCalled();
    });

    it('should do nothing when no run is active', () => {
      benchmarkServiceMock.getActiveRun.and.returnValue(of(null));
      benchmarkServiceMock.getRun.calls.reset();

      component.checkActiveRun();

      expect(component.activeRunId).toBeNull();
      expect(benchmarkServiceMock.getRun).not.toHaveBeenCalled();
    });

    it('should fetch the suite questions once per dialog open, not per poll tick', () => {
      component.activeRunDetail = buildRun({ answers: [] });
      benchmarkServiceMock.getQuestions.calls.reset();
      benchmarkServiceMock.getQuestions.and.returnValue(of([]));
      spyOn(component.runProgressDialog.nativeElement, 'showModal');

      component.openRunProgressDialog();
      component.closeRunProgressDialog();
      component.openRunProgressDialog();

      expect(benchmarkServiceMock.getQuestions).toHaveBeenCalledTimes(1);
      expect(benchmarkServiceMock.getQuestions).toHaveBeenCalledWith(1);
      component.closeRunProgressDialog();
    });

    it('should render question text as plain text and never as innerHTML', () => {
      component.activeRunDetail = buildRun({ answers: [] });
      component.runProgressQuestions = [
        { id: 1, benchmarkSuiteId: 1, orderIndex: 1, questionText: '<img src=x onerror="alert(1)">', difficulty: 1, expectedPoints: null, createdAtUtc: '2026-09-01T00:00:00Z' }
      ];
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const excerpt = fixture.nativeElement.querySelector('.run-question-list .job-item-excerpt') as HTMLElement;
      expect(excerpt).toBeTruthy();
      expect(excerpt.querySelector('img')).toBeNull();
      expect(excerpt.textContent).toContain('<img src=x onerror="alert(1)">');
    });

    it('should compute elapsed time correctly for UTC timestamps without a Z designator', () => {
      // 5 minutes ago without Z suffix
      const fiveMinutesAgo = new Date(Date.now() - 300000).toISOString().replace('Z', '');
      component.activeRunDetail = buildRun({
        startedAtUtc: fiveMinutesAgo,
        completedAtUtc: null
      });

      const label = component.runElapsedLabel;
      // Should format as ~5m (e.g. 5m 00s or 5m 01s), not inflated by local timezone offset
      expect(label).toMatch(/^5m 0\ds$/);
    });

    it('should advance elapsed time at 1 Hz while dialog is open and stop on close', fakeAsync(() => {
      const now = Date.now();
      const startTime = new Date(now - 10000).toISOString().replace('Z', '');
      component.activeRunDetail = buildRun({
        status: 'Running',
        startedAtUtc: startTime,
        completedAtUtc: null
      });

      spyOn(component.runProgressDialog.nativeElement, 'showModal');
      spyOn(component.runProgressDialog.nativeElement, 'close');

      component.openRunProgressDialog();
      expect(component.runElapsedLabel).toBe('10s');

      tick(1000);
      expect(component.runElapsedLabel).toBe('11s');

      component.closeRunProgressDialog();
      expect((component as any).runElapsedInterval).toBeNull();

      tick(5000);
      discardPeriodicTasks();
    }));

    it('should not start ticker for terminal run and stop ticker when poll reports terminal', fakeAsync(() => {
      spyOn(component.runProgressDialog.nativeElement, 'showModal');
      component.activeRunDetail = buildRun({
        status: 'Completed',
        startedAtUtc: '2026-09-02T17:00:00Z',
        completedAtUtc: '2026-09-02T17:05:00Z'
      });
      component.openRunProgressDialog();
      expect((component as any).runElapsedInterval).toBeNull();

      // Now set to running and open
      component.activeRunDetail = buildRun({
        status: 'Running',
        startedAtUtc: '2026-09-02T17:00:00Z',
        completedAtUtc: null
      });
      component.openRunProgressDialog();
      expect((component as any).runElapsedInterval).not.toBeNull();

      // Poll returns terminal run
      benchmarkServiceMock.getRun.and.returnValue(of(buildRun({
        status: 'Completed',
        startedAtUtc: '2026-09-02T17:00:00Z',
        completedAtUtc: '2026-09-02T17:05:00Z'
      })));
      (component as any).pollRunDetail(42);
      expect((component as any).runElapsedInterval).toBeNull();

      discardPeriodicTasks();
    }));

    it('should render diagnostics details unconditionally closed by default and without failure count on healthy run', () => {
      component.activeRunDetail = buildRun({
        status: 'Running',
        answers: [buildAnswer(1)]
      });
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const details = fixture.nativeElement.querySelector('.job-diagnostics') as HTMLDetailsElement;
      expect(details).toBeTruthy();
      expect(details.open).toBeFalse();

      const copyBtn = details.querySelector('button[aria-label="Copy benchmark run diagnostics"]');
      expect(copyBtn).toBeTruthy();

      const summary = details.querySelector('summary') as HTMLElement;
      expect(summary.textContent).toContain('Diagnostics');
      expect(details.querySelector('.job-diagnostics-count')).toBeNull();
    });

    it('should show failure count in diagnostics summary when answers fail', () => {
      component.activeRunDetail = buildRun({
        status: 'Running',
        answers: [buildAnswer(1, { status: 'Failed' }), buildAnswer(2, { status: 'Failed' })]
      });
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const details = fixture.nativeElement.querySelector('.job-diagnostics') as HTMLDetailsElement;
      expect(details).toBeTruthy();
      const countChip = details.querySelector('.job-diagnostics-count') as HTMLElement;
      expect(countChip).toBeTruthy();
      expect(countChip.textContent).toContain('2 failed');
    });

    it('should record lastRunPollError on failed poll and report it in diagnostics text', () => {
      const consoleError = spyOn(console, 'error');
      benchmarkServiceMock.getRun.and.returnValue(throwError(() => ({
        status: 500,
        message: 'Internal Server Error'
      })));

      (component as any).pollRunDetail(42);

      expect(component.lastRunPollError).toContain('500');
      expect(component.runDiagnosticsText).toContain('Last poll error:');
      expect(component.runDiagnosticsText).toContain('500');
      expect(consoleError).toHaveBeenCalledWith('Failed to poll run detail', jasmine.any(Object));
    });

    it('should produce non-empty diagnostics text when activeRunDetail is null', () => {
      component.activeRunDetail = null;
      const text = component.runDiagnosticsText;
      expect(text).toBeTruthy();
      expect(text).toContain('=== BENCHMARK RUN DIAGNOSTICS ===');
      expect(text).toContain('No run detail received yet.');
      expect(text).toContain('--- POLLING ---');
      expect(text).toContain('--- ERRORS ---');
    });

    it('should render diagnostics pre containing code child with tabindex 0', () => {
      component.activeRunDetail = buildRun({ answers: [] });
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const pre = fixture.nativeElement.querySelector('.job-diagnostics pre') as HTMLPreElement;
      expect(pre).toBeTruthy();
      expect(pre.getAttribute('tabindex')).toBe('0');
      const code = pre.querySelector('code');
      expect(code).toBeTruthy();
    });

    it('should set returnToSeriesOnClose to true when opened from a series', () => {
      spyOn(component.runProgressDialog.nativeElement, 'showModal');
      expect(component.returnToSeriesOnClose).toBeFalse();

      component.onOpenRunProgressFromSeries(42);

      expect(component.returnToSeriesOnClose).toBeTrue();
      expect(component.isRunProgressDialogOpen).toBeTrue();
      expect(component.multiRunDialogVisible).toBeFalse();
    });

    it('should reopen multi-run dialog when closing single-run progress with returnToSeriesOnClose true', () => {
      spyOn(component.runProgressDialog.nativeElement, 'showModal');
      spyOn(component.runProgressDialog.nativeElement, 'close');
      component.activeSeriesId = 10;
      component.onOpenRunProgressFromSeries(42);

      expect(component.multiRunDialogVisible).toBeFalse();
      expect(component.returnToSeriesOnClose).toBeTrue();

      component.closeRunProgressDialog();

      expect(component.multiRunDialogVisible).toBeTrue();
      expect(component.returnToSeriesOnClose).toBeFalse();
      expect(component.isRunProgressDialogOpen).toBeFalse();
    });

    it('should not reopen multi-run dialog when closing single-run progress with returnToSeriesOnClose false', () => {
      spyOn(component.runProgressDialog.nativeElement, 'showModal');
      spyOn(component.runProgressDialog.nativeElement, 'close');
      component.activeSeriesId = 10;
      component.openRunProgressDialog();

      expect(component.returnToSeriesOnClose).toBeFalse();
      expect(component.multiRunDialogVisible).toBeFalse();

      component.closeRunProgressDialog();

      expect(component.multiRunDialogVisible).toBeFalse();
      expect(component.returnToSeriesOnClose).toBeFalse();
    });

    it('should not reopen multi-run dialog when viewing full report detail from progress dialog', () => {
      spyOn(component.runProgressDialog.nativeElement, 'showModal');
      spyOn(component.runProgressDialog.nativeElement, 'close');
      spyOn(component, 'viewRunDetail');
      benchmarkServiceMock.getRun.and.returnValue(of(buildRun({ id: 42 })));
      component.activeSeriesId = 10;
      component.onOpenRunProgressFromSeries(42);

      expect(component.returnToSeriesOnClose).toBeTrue();

      component.viewActiveRunDetail();

      expect(component.multiRunDialogVisible).toBeFalse();
      expect(component.returnToSeriesOnClose).toBeFalse();
      expect(component.viewRunDetail).toHaveBeenCalledWith(42);
    });

    it('should render Back to Series and updated aria-label when returnToSeriesOnClose is true', () => {
      component.activeRunDetail = buildRun({ status: 'Running', answers: [] });
      component.returnToSeriesOnClose = true;
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const dialogEl = component.runProgressDialog.nativeElement;
      const closeBtn = dialogEl.querySelector('.dialog-header .btn-icon-action') as HTMLButtonElement;
      expect(closeBtn.getAttribute('aria-label')).toBe('Return to series progress');

      const cancelBtn = dialogEl.querySelector('.dialog-footer .btn-gh-cancel') as HTMLButtonElement;
      expect(cancelBtn.textContent?.trim()).toBe('Back to Series');

      // Check when terminal
      component.activeRunDetail = buildRun({ status: 'Completed', answers: [] });
      fixture.detectChanges();

      const terminalCancelBtn = dialogEl.querySelector('.dialog-footer .btn-gh-cancel') as HTMLButtonElement;
      expect(terminalCancelBtn.textContent?.trim()).toBe('Back to Series');
    });

    it('should keep polling and stay non-terminal when the first poll after a re-run launch still reports the previous status', () => {
      spyOn(component.runProgressDialog.nativeElement, 'showModal');
      spyOn(component.runProgressDialog.nativeElement, 'close');

      component.selectedRunDetail = buildRun({
        id: 37,
        status: 'CompletedWithErrors',
        answers: [buildAnswer(3, { status: 'Failed' })]
      });
      benchmarkServiceMock.rerunFailedQuestions.and.returnValue(of({ runId: 37 }));
      // The server has not yet flipped the row to Running: the first poll after the launch
      // still sees the previous attempt's terminal status.
      benchmarkServiceMock.getRun.and.returnValue(of(buildRun({
        id: 37,
        status: 'CompletedWithErrors',
        answers: [buildAnswer(3, { status: 'Failed' })]
      })));

      component.rerunFailedFromRunDetail(37);

      expect(component.rerunLaunchPending).toBeTrue();
      expect(component.runIsTerminal).toBeFalse();
      expect(component.runStageLabel).toContain('Starting');
      expect((component as any).pollTickerHandle).not.toBeNull();

      fixture.detectChanges();
      const footer = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-footer') as HTMLElement;
      expect(footer.querySelector('.btn-gh-delete')).toBeTruthy();
      const buttons = Array.from(footer.querySelectorAll('button')) as HTMLButtonElement[];
      expect(buttons.some(b => (b.textContent || '').trim() === 'View Full Report')).toBeFalse();

      component.closeRunProgressDialog();
    });

    it('should clear the launch-pending state once a poll reports the run running', () => {
      spyOn(component.runProgressDialog.nativeElement, 'showModal');
      spyOn(component.runProgressDialog.nativeElement, 'close');

      component.activeRunId = 37;
      component.rerunLaunchPending = true;
      (component as any).rerunLaunchedAtMs = Date.now();
      benchmarkServiceMock.getRun.and.returnValue(of(buildRun({ id: 37, status: 'Running' })));

      (component as any).pollRunDetail(37);

      expect(component.rerunLaunchPending).toBeFalse();
      expect(component.runIsRunning).toBeTrue();

      component.closeRunProgressDialog();
    });

    it('should surface a refused re-run inside the progress dialog and keep the loaded run detail', () => {
      spyOn(component.runProgressDialog.nativeElement, 'showModal');
      spyOn(component.runProgressDialog.nativeElement, 'close');

      component.activeRunDetail = buildRun({
        id: 37,
        status: 'CompletedWithErrors',
        suiteName: 'Suite X',
        answers: [buildAnswer(1, { status: 'Failed' })]
      });
      component.isRunProgressDialogOpen = true;
      benchmarkServiceMock.rerunFailedQuestions.and.returnValue(throwError(() => ({
        status: 409,
        error: 'A benchmark run is already in progress.'
      })));

      component.rerunFailedFromProgress();

      expect(component.runErrorMessage).toBe('A benchmark run is already in progress.');
      expect(component.rerunLaunchPending).toBeFalse();
      expect(component.activeRunDetail).not.toBeNull();

      fixture.detectChanges();
      const alert = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-body .alert-danger') as HTMLElement;
      expect(alert).toBeTruthy();
      expect(alert.textContent).toContain('A benchmark run is already in progress.');
      const subtitle = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-subtitle') as HTMLElement;
      expect(subtitle.textContent).toContain('Suite X');

      component.closeRunProgressDialog();
    });

    it('should open the run progress dialog full-screen', () => {
      fixture.detectChanges();
      const dialog = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog') as HTMLElement;
      expect(dialog).toBeTruthy();
      expect(dialog.classList.contains('gh-dialog-fullscreen')).toBeTrue();
    });

    it('should put the question list in its own section and everything else before it', () => {
      component.activeRunDetail = buildRun({
        status: 'Completed',
        completedAtUtc: '2026-09-02T00:05:00Z',
        answers: [buildAnswer(1), buildAnswer(2)]
      });
      fixture.detectChanges();

      const body = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-body') as HTMLElement;
      const sections = Array.from(body.children) as HTMLElement[];
      expect(sections.map(s => s.tagName)).toEqual(['SECTION', 'SECTION']);
      const [overview, questions] = sections;
      expect(overview.classList.contains('run-progress-overview')).toBeTrue();
      expect(questions.classList.contains('run-progress-questions')).toBeTrue();

      // The overview keeps everything but the question list, in its order.
      for (const selector of ['.run-model-strip', '.run-stage-rail', '.job-progress-block',
        '.run-stat-strip', 'app-benchmark-cost-panel', '.job-diagnostics']) {
        expect(overview.querySelector(selector)).withContext(selector).toBeTruthy();
      }
      expect(overview.querySelector('.run-question-list')).toBeNull();
      expect(overview.querySelector('[role="status"][aria-live="polite"].progress-status')).toBeTruthy();

      // The overview begins with the roster: no heading of its own. The dialog title is the focus target.
      expect(overview.querySelector('.progress-heading')).toBeNull();
      const dialogTitle = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-header #runProgressDialogTitle') as HTMLElement;
      expect(dialogTitle.getAttribute('tabindex')).toBe('-1');

      expect(questions.getAttribute('aria-labelledby')).toBe('runProgressQuestionsTitle');
      expect(questions.getAttribute('tabindex')).toBe('0');
      const title = questions.querySelector('h4#runProgressQuestionsTitle') as HTMLElement;
      expect(title.textContent?.replace(/\s+/g, ' ').trim()).toBe('Questions 2');
      expect(questions.querySelectorAll('.run-question-list .job-item-row').length).toBe(2);
      expect(questions.querySelector('.gh-section-title')).toBe(title);
    });

    it('should focus the dialog title when the run progress dialog opens', () => {
      component.activeRunDetail = buildRun({ status: 'Completed', answers: [buildAnswer(1)] });
      const dialog = component.runProgressDialog.nativeElement as HTMLDialogElement;

      component.openRunProgressDialog();

      try {
        expect(dialog.open).toBeTrue();
        expect(document.activeElement?.id).toBe('runProgressDialogTitle');
      } finally {
        component.closeRunProgressDialog();
      }
    });

    const progressSubtitle = () => (fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-subtitle') as HTMLElement)
      .textContent!.replace(/\s+/g, ' ').trim();

    it('should say the run is starting in the subtitle until the run detail loads', () => {
      component.activeRunDetail = null;
      fixture.detectChanges();
      expect(progressSubtitle()).toBe('Starting benchmark run…');
    });

    it('should name the suite and profile in the subtitle once the run detail loads', () => {
      component.activeRunDetail = buildRun({ suiteName: 'Suite X', scoringProfileName: 'Strict' });
      fixture.detectChanges();
      expect(progressSubtitle()).toBe('Suite X | Profile: Strict');
    });

    describe('layout by width', () => {
      let dialog: HTMLDialogElement;

      function openAtWidth(width: string): HTMLElement {
        component.activeRunDetail = buildRun({ answers: [buildAnswer(1), buildAnswer(2)] });
        fixture.detectChanges();
        dialog = component.runProgressDialog.nativeElement as HTMLDialogElement;
        dialog.style.width = width;
        dialog.style.maxWidth = width;
        dialog.showModal();
        return dialog.querySelector('.dialog-body') as HTMLElement;
      }

      afterEach(() => {
        if (dialog?.open) dialog.close();
        dialog?.style.removeProperty('width');
        dialog?.style.removeProperty('max-width');
      });

      it('should lay the dialog out in two columns when the body is wide', () => {
        const body = openAtWidth('1400px');

        const style = getComputedStyle(body);
        expect(style.display).toBe('grid');
        expect(style.gridTemplateColumns.trim().split(/\s+/).length).toBe(2);
        const questions = body.querySelector('.run-progress-questions') as HTMLElement;
        expect(getComputedStyle(questions).overflowY).toBe('auto');
        expect(getComputedStyle(body.querySelector('.run-progress-overview') as HTMLElement).overflowY).toBe('auto');
      });

      it('should keep one column when narrow', () => {
        const body = openAtWidth('700px');

        const style = getComputedStyle(body);
        expect(style.display).not.toBe('grid');
        expect(style.overflowY).toBe('auto');
        const questions = body.querySelector('.run-progress-questions') as HTMLElement;
        expect(getComputedStyle(questions).overflowY).toBe('visible');
      });
    });

    it('should show the published score on a scored question row', () => {
      component.activeRunDetail = buildRun({
        answers: [
          buildAnswer(1, { qualityScore: 83 }),
          buildAnswer(2, { assessmentStatus: 'Pending', qualityScore: null })
        ]
      });
      fixture.detectChanges();

      const rows = Array.from(fixture.nativeElement.querySelectorAll('.run-question-list .job-item-row')) as HTMLElement[];
      const score = rows[0].querySelector('.job-item-score') as HTMLElement;
      expect(score.textContent?.trim()).toBe('Score 83');
      expect(score.querySelector('.visually-hidden')?.textContent).toBe('Score ');
      // Before the status chip.
      expect(score.nextElementSibling?.classList.contains('job-status-chip')).toBeTrue();
      expect(rows[1].querySelector('.job-item-score')).toBeNull();
    });

    it('should give a scored question row a score tier badge', () => {
      component.activeRunDetail = buildRun({
        answers: [
          buildAnswer(1, { qualityScore: 83 }),
          buildAnswer(2, { qualityScore: 45 })
        ]
      });
      fixture.detectChanges();

      const scores = Array.from(fixture.nativeElement.querySelectorAll('.run-question-list .job-item-score')) as HTMLElement[];
      expect(scores.length).toBe(2);
      expect(scores[0].classList.contains('job-item-score')).toBeTrue();
      expect(scores[0].classList.contains('badge-score-high')).toBeTrue();
      expect(scores[1].classList.contains('badge-score-low')).toBeTrue();
      expect(scores[1].textContent?.trim()).toBe('Score 45');
      expect(scores[1].nextElementSibling?.classList.contains('job-status-chip')).toBeTrue();
    });

    it('should show the panel score on a panel run\'s question row, and none until both members have scored', () => {
      component.activeRunDetail = buildRun({
        isPanelRun: true,
        answers: [
          buildAnswer(1, { qualityScore: 90, panelQualityScore: 82.5 }),
          buildAnswer(2, { qualityScore: 70, panelQualityScore: null })
        ]
      });
      fixture.detectChanges();

      const panelRows = Array.from(fixture.nativeElement.querySelectorAll('.run-question-list .job-item-row')) as HTMLElement[];
      expect(panelRows[0].querySelector('.job-item-score')?.textContent?.trim()).toBe('Score 82.5');
      expect(panelRows[1].querySelector('.job-item-score')).toBeNull();
    });

    it('should point the re-run badge at the Questions column', () => {
      component.rerunScopeOrderIndexes = [2];
      component.activeRunDetail = buildRun({ answers: [buildAnswer(1), buildAnswer(2)] });
      fixture.detectChanges();

      const badge = fixture.nativeElement.querySelector('.rerun-scope-badge') as HTMLElement;
      expect(badge.textContent?.replace(/\s+/g, ' ')).toContain('Every question of the suite is listed under Questions; the re-run ones are marked.');
      expect(badge.querySelector('strong')?.textContent).toBe('Questions');
    });

    it('should name the assessor in the active run banner', () => {
      component.activeSubTab = 'run';
      component.activeRunDetail = buildRun({ answers: [] });
      fixture.detectChanges();
      const bannerText = ((fixture.nativeElement.querySelector('.active-run-banner') as HTMLElement).textContent || '')
        .replace(/\s+/g, ' ');

      expect(bannerText).toContain('Assessor: Test Assessor');
      expect(bannerText).not.toContain('Evaluator');
    });

    it('should name both assessors of a panel in the active run banner', () => {
      component.activeSubTab = 'run';
      component.activeRunDetail = buildRun({ answers: [], isPanelRun: true, coAssessorModelDisplayNameUsed: 'Co Assessor' });
      fixture.detectChanges();
      const bannerText = ((fixture.nativeElement.querySelector('.active-run-banner') as HTMLElement).textContent || '')
        .replace(/\s+/g, ' ');

      expect(bannerText).toContain('Assessors: Test Assessor + Co Assessor');
    });

    it('should give benchmark dialog content no padding of its own', () => {
      spyOn(component.runProgressDialog.nativeElement, 'showModal');
      spyOn(component.runProgressDialog.nativeElement, 'close');

      fixture.detectChanges();
      const content = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog .dialog-content') as HTMLElement;
      expect(content).toBeTruthy();
      const style = getComputedStyle(content);
      expect(style.paddingTop).toBe('0px');
      expect(style.paddingBottom).toBe('0px');

      component.closeRunProgressDialog();
    });
  });

  describe('retry actions', () => {
    beforeEach(() => {
      component.selectedRunDetail = {
        id: 42,
        suiteName: 'Test Suite',
        testedModelConfigurationId: 1,
        assessorModelConfigurationId: 1,
        assessorAvailable: true,
        status: 'CompletedWithErrors',
        answers: [
          { id: 101, orderIndex: 1, questionText: 'Q1', status: 'Ok', assessmentStatus: 'Scored' },
          { id: 102, orderIndex: 2, questionText: 'Q2', status: 'ProviderError', assessmentStatus: 'Failed', assessmentError: 'Timeout' }
        ]
      } as any;
      fixture.detectChanges();
    });

    it('should correctly open retry dialog with resolved assessor', () => {
      const answer = component.selectedRunDetail!.answers[1];
      component.openRetryDialog('question', 42, answer);

      expect(component.retryScope).toBe('question');
      expect(component.retryRunId).toBe(42);
      expect(component.retryAnswer).toBe(answer);
      expect(component.retryAssessorConfigId).toBe(1);
    });

    it('should trigger rerunAnswer on confirmRetry when scope is question', () => {
      benchmarkServiceMock.rerunAnswer.and.returnValue(of({ runId: 42 }));
      benchmarkServiceMock.getRun.and.returnValue(of(component.selectedRunDetail!));

      const answer = component.selectedRunDetail!.answers[1];
      component.openRetryDialog('question', 42, answer);
      component.confirmRetry();

      expect(benchmarkServiceMock.rerunAnswer).toHaveBeenCalledWith(42, 102, 1);
      expect(component.rerunningAnswerId).toBe(102);
    });

    it('should trigger reassessAnswer on confirmRetry when scope is assessment', () => {
      benchmarkServiceMock.reassessAnswer.and.returnValue(of({ runId: 42 }));
      benchmarkServiceMock.getRun.and.returnValue(of(component.selectedRunDetail!));

      const answer = component.selectedRunDetail!.answers[1];
      component.openRetryDialog('assessment', 42, answer);
      component.confirmRetry();

      expect(benchmarkServiceMock.reassessAnswer).toHaveBeenCalledWith(42, 102, 1);
      expect(component.reassessingAnswerId).toBe(102);
    });

    it('should trigger rerunFinalSynthesis on confirmRetry when scope is synthesis', () => {
      benchmarkServiceMock.rerunFinalSynthesis.and.returnValue(of({ runId: 42 }));
      benchmarkServiceMock.getRun.and.returnValue(of(component.selectedRunDetail!));

      component.openRetryDialog('synthesis', 42);
      component.confirmRetry();

      expect(benchmarkServiceMock.rerunFinalSynthesis).toHaveBeenCalledWith(42, 1);
      expect(component.runningSynthesis).toBeTrue();
    });

    it('should trigger retryFailedAssessments on confirmRetry when scope is assessments', () => {
      benchmarkServiceMock.retryFailedAssessments.and.returnValue(of({ runId: 42 }));
      benchmarkServiceMock.getRun.and.returnValue(of(component.selectedRunDetail!));

      component.openRetryDialog('assessments', 42);
      component.confirmRetry();

      expect(benchmarkServiceMock.retryFailedAssessments).toHaveBeenCalledWith(42, 1);
      expect(component.retryingAssessments).toBeTrue();
    });
  });

  describe('downloadToolCallLog', () => {
    it('should open the tool-call log through window.open using the service URL', () => {
      benchmarkServiceMock.getToolCallLogUrl.and.returnValue('/api/admin/benchmark/runs/42/tool-call-log');
      const openSpy = spyOn(window, 'open');

      component.downloadToolCallLog(42);

      expect(benchmarkServiceMock.getToolCallLogUrl).toHaveBeenCalledWith(42);
      expect(openSpy).toHaveBeenCalledWith('/api/admin/benchmark/runs/42/tool-call-log', '_blank');
    });
  });

  describe('run integrity accounting', () => {
    /**
     * A finished run detail with no integrity problems. Each test raises exactly the counters
     * it is about, so a clause appearing in the banner can only have come from that counter.
     */
    function buildCompletedRun(overrides: any = {}): any {
      return {
        id: 55,
        benchmarkSuiteId: 1,
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Test Model',
        testedModelProviderUsed: 'OpenAI',
        testedModelIdUsed: 'gpt-5.6-luna',
        testedModelParallelExecutionModeUsed: 0,
        assessorModelDisplayNameUsed: 'Test Assessor',
        assessorModelProviderUsed: 'Google',
        assessorModelIdUsed: 'gemini-3.7-flash',
        startedByUserName: 'admin',
        status: 'Completed',
        startedAtUtc: '2026-09-03T06:52:00Z',
        completedAtUtc: '2026-09-03T07:10:00Z',
        totalAnswerDurationMs: 900000,
        scoringProfileName: 'Standard Intelligence Index (Default)',
        scoringProfileId: 1,
        scoringMethodVersion: 4,
        harnessVersion: '3',
        degradedAnswerCount: 0,
        toolStarvedAnswerCount: 0,
        transportDefectAnswerCount: 0,
        advisoryFlagAnswerCount: 0,
        scrubbedArtifactAnswerCount: 0,
        toolOverheadMs: 0,
        difficultyFallbackUsed: false,
        speedMeasurementDegraded: false,
        maxParallelQuestionsUsed: 1,
        answeredQuestionCount: 18,
        totalQuestionCount: 18,
        assessmentParseFailed: false,
        totalInputTokens: 0,
        totalOutputTokens: 0,
        totalCacheReadTokens: 0,
        totalCacheCreationTokens: 0,
        totalDurationMs: 900000,
        errorMessage: null,
        answers: [],
        ...overrides
      };
    }

    function buildScoredAnswer(orderIndex: number, overrides: any = {}): any {
      return {
        id: 200 + orderIndex,
        benchmarkRunId: 55,
        orderIndex,
        questionText: `Question ${orderIndex}`,
        difficulty: 2,
        assessedDifficulty: 50,
        answerText: `Answer ${orderIndex}`,
        status: 'Ok',
        assessmentStatus: 'Scored',
        durationMs: 48800,
        modelTimeMs: 48800,
        toolCallCount: 4,
        scrubbedArtifactCount: 0,
        answerFlags: 0,
        answerFlagNames: [],
        ...overrides
      };
    }

    function integrityNoticeText(): string {
      const notices: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.alert-heading'));
      const heading = notices.find(n => (n.textContent || '').includes('Run Integrity Notice'));
      if (!heading) return '';
      return (heading.parentElement?.querySelector('.alert-body') as HTMLElement)?.textContent ?? '';
    }

    it('should describe transport defects, recoveries, harness limits, and advisory flags as separate causes', () => {
      component.selectedRunDetail = buildCompletedRun({
        transportDefectAnswerCount: 4,
        recoveredAnswerCount: 5,
        toolStarvedAnswerCount: 3,
        advisoryFlagAnswerCount: 6
      });
      fixture.detectChanges();

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('4 answer(s) were corrupted beyond recovery (empty or truncated).');
      expect(text).toContain('5 answer(s) were repaired by the harness before grading');
      expect(text).toContain('3 answer(s) hit a configured harness limit (tool budget).');
      expect(text).toContain('6 answer(s) carry advisory flags.');
      // The wording this replaced described tool-starved answers as one of "empty, harness
      // artifacts, or truncated", which they are not — and counted a repaired answer as a
      // defect, which is what made a healthy run read as errored.
      expect(text).not.toContain('degraded answer(s)');
      expect(text).not.toContain('tool-starved');
      expect(text).not.toContain('empty, harness artifacts, or truncated');
    });

    it('should report a disputed assessment in the integrity notice and badge the answer', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          buildScoredAnswer(1, {
            qualityScore: 25,
            criticalError: true,
            secondOpinionQualityScore: 72,
            secondOpinionCriticalError: false,
            secondOpinionByModelDisplayNameUsed: 'Second Assessor',
            secondOpinionDisagreed: true
          })
        ]
      });
      fixture.detectChanges();

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('1 answer(s) were read by a second reader that reached a materially different verdict. The assessor\'s verdict is what scored.');
      expect(fixture.nativeElement.querySelector('.disputed-badge')).toBeTruthy();
    });

    function missingQuote(orderIndex: number, literal: string): any {
      return { questionId: 100 + orderIndex, orderIndex, literal, lineExcerpt: `- "${literal}"` };
    }

    it('should list each missing board quote by its 1-based question number, even with no other integrity cause', () => {
      component.selectedRunDetail = buildCompletedRun({
        boardFactsCheck: {
          bulletCount: 40, checkedLiteralCount: 38, unquotedBulletCount: 0, unquotedBullets: [],
          missingLiterals: [missingQuote(1, 'a +2 elven mithril-coat'), missingQuote(15, 'Dlvl:12')]
        }
      });
      fixture.detectChanges();

      const notice = fixture.nativeElement.querySelector('.board-facts-notice') as HTMLElement;
      expect(notice).toBeTruthy();
      const items = Array.from(notice.querySelectorAll('li')).map(li => (li.textContent || '').trim());
      expect(items).toEqual(['Q1: "a +2 elven mithril-coat"', 'Q15: "Dlvl:12"']);
    });

    it('should cap the missing board quote list at 20 and say how many more there are', () => {
      component.selectedRunDetail = buildCompletedRun({
        boardFactsCheck: {
          bulletCount: 40, checkedLiteralCount: 40, unquotedBulletCount: 0, unquotedBullets: [],
          missingLiterals: Array.from({ length: 23 }, (_, i) => missingQuote(i + 1, `quote ${i + 1}`))
        }
      });
      fixture.detectChanges();

      const items = Array.from(fixture.nativeElement.querySelectorAll('.board-facts-notice li'))
        .map(li => ((li as HTMLElement).textContent || '').trim());
      expect(items.length).toBe(21);
      expect(items[19]).toBe('Q20: "quote 20"');
      expect(items[20]).toBe('and 3 more');
    });

    it('should show no board quote notice when the check found every quote', () => {
      component.selectedRunDetail = buildCompletedRun({
        boardFactsCheck: { bulletCount: 4, checkedLiteralCount: 4, unquotedBulletCount: 0, unquotedBullets: [], missingLiterals: [] }
      });
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.board-facts-notice')).toBeNull();
    });

    it('should show the assessor evidence and the second opinion as plain text when expanded', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          buildScoredAnswer(1, {
            accuracyLevel: 2,
            completenessLevel: 2,
            concisenessLevel: 4,
            readabilityLevel: 5,
            qualityScore: 42,
            assessmentEvidenceJson: JSON.stringify({
              accuracy: 'Rubric point 3: Exceptional/Elite give -4/-8 AC.',
              completeness: 'Not in rubric: from my own knowledge of the source.',
              criticalErrorDemoted: false
            }),
            secondOpinionQualityScore: 44,
            secondOpinionCriticalError: false,
            secondOpinionByModelDisplayNameUsed: 'Second Assessor',
            secondOpinionDisagreed: false
          })
        ]
      });
      component.expandedQuestions.add(1);
      fixture.detectChanges();

      const evidence = fixture.nativeElement.querySelector('.assessment-evidence') as HTMLElement;
      expect(evidence).toBeTruthy();
      expect(evidence.textContent).toContain('Rubric point 3');
      expect(evidence.textContent).toContain('Not in rubric');

      const secondOpinion = fixture.nativeElement.querySelector('.second-opinion-box') as HTMLElement;
      expect(secondOpinion).toBeTruthy();
      expect(secondOpinion.textContent).toContain('Second Assessor');
      expect(secondOpinion.textContent).toContain('agrees with');
      expect(secondOpinion.classList).not.toContain('is-disputed');
    });

    it('should render only the harness limit clause when a configured cap was the only cause', () => {
      component.selectedRunDetail = buildCompletedRun({ toolStarvedAnswerCount: 3 });
      fixture.detectChanges();

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('3 answer(s) hit a configured harness limit (tool budget).');
      expect(text).not.toContain('transport defects');
      expect(text).not.toContain('advisory flags');
    });

    it('should render no integrity notice when every cause is zero', () => {
      component.selectedRunDetail = buildCompletedRun();
      fixture.detectChanges();

      expect(integrityNoticeText()).toBe('');
    });

    it('should treat empty answers and the Empty, HarnessArtifacts and Truncated bits as transport defects', () => {
      expect(component.hasTransportDefect(buildScoredAnswer(1, { status: 'EmptyAnswer' }))).toBeTrue();
      expect(component.hasTransportDefect(buildScoredAnswer(1, { status: 5 }))).toBeTrue();
      expect(component.hasTransportDefect(buildScoredAnswer(1, { answerFlags: 1 }))).toBeTrue();
      expect(component.hasTransportDefect(buildScoredAnswer(1, { answerFlags: 2 }))).toBeTrue();
      expect(component.hasTransportDefect(buildScoredAnswer(1, { answerFlags: 4 }))).toBeTrue();
      expect(component.hasTransportDefect(buildScoredAnswer(1, { answerFlags: 6 }))).toBeTrue();
    });

    it('should treat a tool budget cap as a harness limit and never as a transport defect', () => {
      const capped = buildScoredAnswer(1, { toolBudgetExhausted: true, toolCallCount: 25, toolCallBudgetUsed: 25 });

      expect(component.hasHarnessLimit(capped)).toBeTrue();
      expect(component.hasTransportDefect(capped)).toBeFalse();
      expect(component.hasAdvisoryFlag(capped)).toBeFalse();
      expect(component.hasHarnessLimit(buildScoredAnswer(1))).toBeFalse();
    });

    it('should count an answer carrying only advisory flags as clean', () => {
      const bleed = buildScoredAnswer(1, { answerFlags: 8, answerFlagNames: ['ReasoningBleed'] });
      const repeated = buildScoredAnswer(2, { answerFlags: 16, answerFlagNames: ['RepeatedFragments'] });
      const both = buildScoredAnswer(3, { answerFlags: 24, answerFlagNames: ['ReasoningBleed', 'RepeatedFragments'] });

      for (const answer of [bleed, repeated, both]) {
        expect(component.hasAdvisoryFlag(answer)).toBeTrue();
        expect(component.hasTransportDefect(answer)).toBeFalse();
        expect(component.hasHarnessLimit(answer)).toBeFalse();
      }

      // Advisory flags may overlap a defect without masking it.
      const overlapping = buildScoredAnswer(4, { answerFlags: 2 | 8 });
      expect(component.hasTransportDefect(overlapping)).toBeTrue();
      expect(component.hasAdvisoryFlag(overlapping)).toBeTrue();

      const clean = buildScoredAnswer(5);
      expect(component.hasAdvisoryFlag(clean)).toBeFalse();
      expect(component.hasTransportDefect(clean)).toBeFalse();
    });

    it('should flag an AnswerFramingOpener-only answer as advisory and list its question number', () => {
      const framed = buildScoredAnswer(7, { answerFlags: 1024, answerFlagNames: ['AnswerFramingOpener'] });

      expect(component.hasAdvisoryFlag(framed)).toBeTrue();
      expect(component.hasTransportDefect(framed)).toBeFalse();
      expect(component.hasHarnessLimit(framed)).toBeFalse();

      component.selectedRunDetail = buildCompletedRun({ answers: [framed] });
      fixture.detectChanges();

      expect(component.advisoryFlagQuestionNumbers).toBe('7');
    });

    it('should name only the advisory flags as advisory', () => {
      expect(component.isAdvisoryFlagName('ReasoningBleed')).toBeTrue();
      expect(component.isAdvisoryFlagName('RepeatedFragments')).toBeTrue();
      expect(component.isAdvisoryFlagName('ContestedVerdict')).toBeTrue();
      expect(component.isAdvisoryFlagName('UnevidencedDeduction')).toBeTrue();
      expect(component.isAdvisoryFlagName('RefutedClaim')).toBeTrue();
      expect(component.isAdvisoryFlagName('OutOfRubricAccuracyDeduction')).toBeTrue();
      expect(component.isAdvisoryFlagName('AnswerFramingOpener')).toBeTrue();
      expect(component.isAdvisoryFlagName('ContestedCriticalError')).toBeTrue();
      expect(component.isAdvisoryFlagName('ContestedAccuracyDeduction')).toBeTrue();
      expect(component.isAdvisoryFlagName('DimensionOutlier')).toBeTrue();
      expect(component.isAdvisoryFlagName('HarnessArtifacts')).toBeFalse();
      expect(component.isAdvisoryFlagName('Truncated')).toBeFalse();
      expect(component.isAdvisoryFlagName('Empty')).toBeFalse();
    });

    it('should count and name the critical error answers alongside the advisory ones', () => {
      component.selectedRunDetail = buildCompletedRun({
        advisoryFlagAnswerCount: 2,
        answers: [
          buildScoredAnswer(1, { qualityScore: 25, criticalError: true, rawQualityScore: 60 }),
          buildScoredAnswer(2),
          buildScoredAnswer(3, { qualityScore: 25, criticalError: true, rawQualityScore: 70, answerFlags: 8, answerFlagNames: ['ReasoningBleed'] }),
          buildScoredAnswer(4),
          buildScoredAnswer(5, { answerFlags: 16, answerFlagNames: ['RepeatedFragments'] })
        ]
      });
      fixture.detectChanges();

      expect(component.criticalErrorAnswerCount).toBe(2);
      expect(component.criticalErrorQuestionNumbers).toBe('1, 3');
      expect(component.advisoryFlagQuestionNumbers).toBe('3, 5');

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('2 answer(s) flagged with a critical error (question(s) 1, 3).');
      expect(text).toContain('2 answer(s) carry advisory flags (question(s) 3, 5).');
    });

    it('should raise the integrity notice for a critical error that is the only cause', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [buildScoredAnswer(1, { qualityScore: 25, criticalError: true, rawQualityScore: 60 })]
      });
      fixture.detectChanges();

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('1 answer(s) flagged with a critical error (question(s) 1).');
      expect(text).toContain('read this count, not the index, for this failure mode.');
      expect(text).not.toContain('advisory flags');
    });

    it('should add the cap-binding clause only when it differs from the flagged count', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          // Flagged and genuinely capped: the raw score was above the ceiling the ceiling pulled it down to.
          buildScoredAnswer(1, { qualityScore: 25, rawQualityScore: 60, criticalError: true }),
          // Flagged but not capped in effect: the raw score already sat at or below the ceiling.
          buildScoredAnswer(2, { qualityScore: 25, rawQualityScore: 25, criticalError: true })
        ]
      });
      fixture.detectChanges();

      expect(component.criticalErrorAnswerCount).toBe(2);
      expect(component.criticalErrorCapBindingCount).toBe(1);

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('2 answer(s) flagged with a critical error (question(s) 1, 2), of which 1 had their score lowered by the cap.');
    });

    it('should omit the cap-binding clause when every flagged answer was actually capped', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          buildScoredAnswer(1, { qualityScore: 25, rawQualityScore: 60, criticalError: true }),
          buildScoredAnswer(2, { qualityScore: 25, rawQualityScore: 70, criticalError: true })
        ]
      });
      fixture.detectChanges();

      expect(component.criticalErrorAnswerCount).toBe(2);
      expect(component.criticalErrorCapBindingCount).toBe(2);

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('2 answer(s) flagged with a critical error (question(s) 1, 2).');
      expect(text).not.toContain('had their score lowered by the cap');
    });

    it('should name no questions when there is no run detail or no flagged answer', () => {
      component.selectedRunDetail = null;
      expect(component.criticalErrorAnswerCount).toBe(0);
      expect(component.criticalErrorQuestionNumbers).toBe('');
      expect(component.advisoryFlagQuestionNumbers).toBe('');

      component.selectedRunDetail = buildCompletedRun({ answers: [buildScoredAnswer(1), buildScoredAnswer(2)] });
      fixture.detectChanges();

      expect(component.criticalErrorAnswerCount).toBe(0);
      expect(component.criticalErrorQuestionNumbers).toBe('');
      expect(component.advisoryFlagQuestionNumbers).toBe('');
      expect(integrityNoticeText()).toBe('');
    });

    it('should format CompletedWithLimits from both the numeric and the string status', () => {
      expect(component.formatStatus(6)).toBe('CompletedWithLimits');
      expect(component.formatStatus('CompletedWithLimits')).toBe('CompletedWithLimits');
    });

    it('should mute advisory flag badges and show the effective tool budget and scrubbed count', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          buildScoredAnswer(1, {
            toolBudgetExhausted: true,
            toolCallCount: 25,
            toolCallBudgetUsed: 25,
            scrubbedArtifactCount: 2,
            answerFlags: 2 | 8,
            answerFlagNames: ['HarnessArtifacts', 'ReasoningBleed']
          })
        ]
      });
      fixture.detectChanges();

      const starved = fixture.nativeElement.querySelector('.badge-starved') as HTMLElement;
      expect(starved).toBeTruthy();
      expect(starved.textContent!.replace(/\s+/g, ' ').trim()).toBe('Budget Hit (25/25)');

      const scrubbed = fixture.nativeElement.querySelector('.badge-scrubbed') as HTMLElement;
      expect(scrubbed).toBeTruthy();
      expect(scrubbed.getAttribute('title')).toContain('2 transport artifact block(s)');

      const defectBadge = fixture.nativeElement.querySelector('.badge-flag-harnessartifacts') as HTMLElement;
      expect(defectBadge).toBeTruthy();
      expect(defectBadge.classList).not.toContain('badge-flag-advisory');

      const advisoryBadge = fixture.nativeElement.querySelector('.badge-flag-reasoningbleed') as HTMLElement;
      expect(advisoryBadge).toBeTruthy();
      expect(advisoryBadge.classList).toContain('badge-flag-advisory');
    });

    it('should label a contested accuracy deduction badge "contested deduction" and mute it as advisory', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          buildScoredAnswer(1, {
            answerFlags: 512 | 4096,
            answerFlagNames: ['OutOfRubricAccuracyDeduction', 'ContestedAccuracyDeduction']
          })
        ]
      });
      fixture.detectChanges();

      const badge = fixture.nativeElement.querySelector('.badge-flag-contestedaccuracydeduction') as HTMLElement;
      expect(badge).toBeTruthy();
      expect(badge.textContent!.trim()).toBe('contested deduction');
      expect(badge.classList).toContain('badge-flag-advisory');
      expect(badge.getAttribute('title')).toContain('refuted');
      expect(badge.getAttribute('title')).toContain('the deduction stands');
    });

    it('should label a dimension outlier badge "dimension outlier" and mute it as advisory', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          buildScoredAnswer(1, {
            answerFlags: 8192,
            answerFlagNames: ['DimensionOutlier']
          })
        ]
      });
      fixture.detectChanges();

      const badge = fixture.nativeElement.querySelector('.badge-flag-dimensionoutlier') as HTMLElement;
      expect(badge).toBeTruthy();
      expect(badge.textContent!.trim()).toBe('dimension outlier');
      expect(badge.classList).toContain('badge-flag-advisory');
      expect(badge.getAttribute('title')).toContain('Routed to a second reader');
    });

    it('should toggle the removed transport artifacts block per answer', () => {
      expect(component.expandedArtifacts.has(1)).toBeFalse();

      component.toggleArtifact(1);
      expect(component.expandedArtifacts.has(1)).toBeTrue();
      expect(component.expandedArtifacts.has(2)).toBeFalse();

      component.toggleArtifact(1);
      expect(component.expandedArtifacts.has(1)).toBeFalse();
    });

    it('should reveal the scrubbed artifact text as plain text only once expanded', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [
          buildScoredAnswer(1, {
            scrubbedArtifactCount: 1,
            scrubbedArtifactText: 'to=multi_tool_use.parallel <em>{"tool_uses":[]}</em>'
          })
        ]
      });
      component.expandedQuestions.add(1);
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.artifact-box')).toBeNull();
      const toggle = fixture.nativeElement.querySelector('.question-card-body .btn-link') as HTMLButtonElement;
      expect(toggle).toBeTruthy();
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
      expect(toggle.getAttribute('aria-controls')).toBe('bm-artifact-1');

      toggle.click();
      fixture.detectChanges();

      const box = fixture.nativeElement.querySelector('.artifact-box') as HTMLElement;
      expect(box).toBeTruthy();
      expect(box.id).toBe('bm-artifact-1');
      expect(box.querySelector('em')).toBeNull();
      expect(box.textContent).toContain('to=multi_tool_use.parallel');
      expect(box.textContent).toContain('<em>{"tool_uses":[]}</em>');
      expect((fixture.nativeElement.querySelector('.question-card-body .btn-link') as HTMLButtonElement)
        .getAttribute('aria-expanded')).toBe('true');
    });

    it('should offer the second opinion assessor as an optional selector, defaulting to none', () => {
      fixture.detectChanges();

      const selector = fixture.nativeElement.querySelector('.second-opinion-model-selector') as HTMLElement;
      expect(selector).toBeTruthy();
      // A System AI Config is a database row chosen here, never a value in appsettings.json.
      expect(selector.textContent).toContain('None — no second reader');
      expect(component.secondOpinionConfigId).toBeNull();
    });

    it('should send the second opinion assessor only when one is selected', () => {
      benchmarkServiceMock.startRun.and.returnValue(of({ runId: 9 }));
      // Suppress the success path's side effects: polling would leave a live interval behind
      // and the dialog would need a real <dialog> to open.
      spyOn<any>(component, 'startPolling');
      spyOn<any>(component, 'openRunProgressDialog');
      component.selectedSuiteId = 1;
      component.testedConfigId = 10;
      component.assessorConfigId = 11;

      component.startBenchmark();
      expect(benchmarkServiceMock.startRun.calls.mostRecent().args[0].secondOpinionAssessorModelConfigurationId)
        .toBeNull();

      component.secondOpinionConfigId = 12;
      component.startBenchmark();
      expect(benchmarkServiceMock.startRun.calls.mostRecent().args[0].secondOpinionAssessorModelConfigurationId)
        .toBe(12);
    });

    it('should reject a second opinion threshold outside 0 to 100 before calling the server', () => {
      component.editingProfileId = null;
      component.profileForm = { ...component.profileForm, name: 'Threshold Profile', secondOpinionQualityThreshold: 140 };

      component.saveProfile();

      expect(component.profileValidationErrors)
        .toContain('Second reader threshold must be between 0 and 100.');
      expect(benchmarkServiceMock.createScoringProfile).not.toHaveBeenCalled();
    });

    it('should label the profile form second-reader fields and open the guide at coverage from it', () => {
      fixture.detectChanges();
      const label = (forId: string) =>
        (fixture.nativeElement.querySelector(`label[for="${forId}"]`) as HTMLElement | null)?.textContent?.trim();
      expect(label('secondOpinionThreshold')).toBe('Second Reader Threshold');
      expect(label('secondOpinionModeProfile')).toBe('Second Reader Coverage');
      const blind = (fixture.nativeElement.querySelector('#profileSecondOpinionBlind') as HTMLElement).closest('label') as HTMLElement;
      expect(blind.textContent?.trim()).toBe('Blind Second Reader');

      const open = spyOn(component.graderGuide!, 'open');
      const guideButton = (fixture.nativeElement.querySelector('#secondOpinionModeProfile') as HTMLElement)
        .closest('.form-group')!.querySelector('.grader-guide-btn') as HTMLButtonElement;
      expect(guideButton.textContent?.trim()).toBe('How the graders work');
      guideButton.click();
      expect(open).toHaveBeenCalledWith('coverage');
    });

    it('should badge both models below the header rather than in it', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        testedModelThinkingLevelUsed: 'max',
        testedModelServiceTierUsed: 'flex',
        assessorModelThinkingLevelUsed: 'high'
      });
      fixture.detectChanges();

      const strip = fixture.nativeElement.querySelector('.run-model-strip') as HTMLElement;
      expect(strip).toBeTruthy();
      expect(strip.textContent).toContain('Test Model');
      expect(strip.textContent).toContain('Test Assessor');
      expect(strip.querySelector('.thinking-badge')?.textContent?.trim()).toBe('thinking level Max');
      expect(strip.querySelector('.provider-badge')).toBeTruthy();
      expect(strip.querySelector('.config-badge')?.textContent?.trim()).toBe('requested service tier Flex');

      const subtitle = fixture.nativeElement
        .querySelector('.benchmark-run-progress-dialog .dialog-subtitle') as HTMLElement;
      expect(subtitle.textContent).toContain('Default Suite');
      expect(subtitle.textContent).not.toContain('Model:');
      expect(subtitle.textContent).not.toContain('Evaluator:');
      expect(subtitle.textContent).not.toContain('Assessor:');
    });

    it('should render Cancel Run as a text-only button', () => {
      component.activeRunDetail = buildCompletedRun({ status: 'Running' });
      fixture.detectChanges();

      const buttons: HTMLButtonElement[] = Array.from(
        fixture.nativeElement.querySelectorAll('.benchmark-run-progress-dialog .dialog-footer button'));
      const cancel = buttons.find(b => (b.textContent || '').includes('Cancel Run'));

      expect(cancel).toBeTruthy();
      expect(cancel!.querySelector('svg')).toBeNull();
    });

    it('should present the run as three stages', () => {
      // BenchmarkService assesses, verifies and second-guesses each answer immediately after
      // producing it, inside the same loop, so stage 1 is all of that, not "collecting".
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        answers: [buildScoredAnswer(1), buildScoredAnswer(2)]
      });

      expect(component.runStage).toBe('finalizing');
      expect(component.runStageLabel).toContain('Stage 3 of 3 — Synthesis and scoring');

      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        answers: [buildScoredAnswer(1, { assessmentStatus: 'Pending' }), buildScoredAnswer(2)]
      });

      // An answer still awaiting assessment keeps the run in stage 1: the stage covers both.
      expect(component.runStage).toBe('answering');
      expect(component.runStageLabel).toContain('Stage 1 of 3 — Answering and grading');
    });

    it('should mark a dispatched question Answering and an undispatched one Pending', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 3,
        inFlightOrderIndexes: [2],
        answers: [buildScoredAnswer(1)]
      });
      component.runProgressQuestions = [
        { orderIndex: 1, questionText: 'Question 1' },
        { orderIndex: 2, questionText: 'Question 2' },
        { orderIndex: 3, questionText: 'Question 3' }
      ] as any;

      const rows = component.runProgressRows;

      expect(rows.length).toBe(3);
      expect(component.runRowChipLabel(rows[0])).toBe('Scored');
      // In flight: the request has reached the provider but no answer row exists yet.
      expect(rows[1].status).toBe('Answering');
      expect(component.runRowChipLabel(rows[1])).toBe('Answering');
      expect(component.runRowChipClass(rows[1])).toBe('status-answering');
      // Not dispatched: Pending keeps its original, narrower meaning.
      expect(rows[2].status).toBe('Pending');
      expect(component.runRowChipLabel(rows[2])).toBe('Pending');
    });

    it('should record in-flight questions and the stage number in the diagnostics text', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        inFlightOrderIndexes: [2],
        answers: [buildScoredAnswer(1)]
      });
      component.runProgressQuestions = [
        { orderIndex: 1, questionText: 'Question 1' },
        { orderIndex: 2, questionText: 'Question 2' }
      ] as any;

      const diagnostics = component.runDiagnosticsText;

      expect(diagnostics).toContain('Stage: 1 of 3 (derived)');
      expect(diagnostics).toContain('In flight: Q2');
      expect(diagnostics).toContain('[Q2] status=Answering');
    });

    it('should prefer the server stage over the derivation, and fall back when the server reports none', () => {
      // Nothing in the answer rows moves during verification or the second-opinion passes, so
      // the derivation cannot see either stage. Only the server can.
      const answers = [buildScoredAnswer(1), buildScoredAnswer(2)];

      component.activeRunDetail = buildCompletedRun({
        status: 'Running', totalQuestionCount: 2, stage: 'Verifying', answers
      });
      expect(component.runStage).toBe('verifying');
      expect(component.runStageLabel)
        .toContain('Stage 2 of 3 — Follow-up grading passes: verifying remaining claims');

      component.activeRunDetail = buildCompletedRun({
        status: 'Running', totalQuestionCount: 2, stage: 'SecondOpinion', answers
      });
      expect(component.runStage).toBe('secondopinion');
      expect(component.runStageLabel)
        .toContain('Stage 2 of 3 — Follow-up grading passes: second-reader sweep');

      component.activeRunDetail = buildCompletedRun({
        status: 'Running', totalQuestionCount: 2, stage: 'Answering', answers
      });
      expect(component.runStage).toBe('answering');

      // A run detail from a server predating the field: the derivation still renders a stage.
      component.activeRunDetail = buildCompletedRun({
        status: 'Running', totalQuestionCount: 2, answers
      });
      expect(component.runStage).toBe('finalizing');
      expect(component.runDiagnosticsText).toContain('Stage: 3 of 3 (derived)');

      // A terminal run is terminal regardless of a stale stage.
      component.activeRunDetail = buildCompletedRun({
        status: 'Completed', totalQuestionCount: 2, stage: 'Verifying', answers
      });
      expect(component.runStage).toBe('terminal');
    });

    /**
     * The rail, rendered once per state. Each case builds its own fixture render because this
     * fixture only reflects a state change on its first detectChanges — the existing dialog tests
     * rely on the component refreshing its own view for the same reason.
     */
    function railItems(stage: string): HTMLElement[] {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        stage,
        answers: [buildScoredAnswer(1), buildScoredAnswer(2)]
      });
      fixture.detectChanges();
      return Array.from(
        fixture.nativeElement.querySelectorAll('.benchmark-run-progress-dialog .run-stage-rail .run-stage'));
    }

    it('should render three rail items and make stage 2 current while claims are verified', () => {
      const items = railItems('Verifying');

      expect(items.length).toBe(3);
      expect(items[0].classList).toContain('is-done');
      expect(items[1].classList).toContain('is-current');
      expect(items[1].getAttribute('aria-current')).toBe('step');
      expect(items[2].classList).not.toContain('is-current');
      expect(items[2].getAttribute('aria-current')).toBeNull();
    });

    it('should keep the same rail item current during the second-opinion pass', () => {
      // The server marks Verifying again after this pass, so the two sharing one item is what
      // stops the rail stepping backwards late in a run.
      const items = railItems('SecondOpinion');

      expect(items.length).toBe(3);
      expect(items[0].classList).toContain('is-done');
      expect(items[1].classList).toContain('is-current');
      expect(items[1].getAttribute('aria-current')).toBe('step');
      expect(items[2].classList).not.toContain('is-current');
    });

    it('should mark both earlier rail items done during synthesis', () => {
      const items = railItems('Synthesizing');

      expect(items[0].classList).toContain('is-done');
      expect(items[1].classList).toContain('is-done');
      expect(items[1].classList).not.toContain('is-current');
      expect(items[2].classList).toContain('is-current');
      expect(items[2].getAttribute('aria-current')).toBe('step');
    });

    /** The text of the stat-strip cell with this term, or null when the run does not show one. */
    function runStatText(label: string): string | null {
      const cells: HTMLElement[] = Array.from(
        fixture.nativeElement.querySelectorAll('.benchmark-run-progress-dialog .run-stat-strip .run-stat'));
      const cell = cells.find(c => (c.querySelector('dt')?.textContent || '').trim() === label);
      return cell ? (cell.querySelector('dd')?.textContent || '').trim() : null;
    }

    it('should show plain counters instead of bars for claims verified and second opinions', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        stage: 'Answering',
        claimVerifierDisplayNameUsed: 'Test Verifier',
        secondOpinionAssessorModelDisplayNameUsed: 'Test Second Opinion',
        secondOpinionModeUsed: 1,
        inFlightVerificationOrderIndexes: [2],
        answers: [
          buildScoredAnswer(1, { claimVerificationJson: '{}' }),
          buildScoredAnswer(2, { secondOpinionQualityScore: 70 })
        ]
      });
      fixture.detectChanges();

      // Only answers with unverified claims or a fired trigger are candidates, so a bar against
      // the answered count has no honest maximum. The counts replace both bars outright.
      const dialog = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog') as HTMLElement;
      expect(dialog.querySelector('#runVerifiedProgressBar')).toBeNull();
      expect(dialog.querySelector('#runSecondOpinionProgressBar')).toBeNull();
      expect(dialog.querySelector('#runAnswersProgressBar')).toBeTruthy();
      expect(dialog.querySelector('#runAssessmentsProgressBar')).toBeTruthy();

      expect(runStatText('Claims verified')).toBe('1 · 1 in progress');
      expect(runStatText('Second readings')).toBe('1');
    });

    it('should show neither counter for a run graded by neither role', () => {
      // Not two zeroes: a run that configured no verifier did not fail to verify anything.
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        stage: 'Answering',
        answers: [buildScoredAnswer(1), buildScoredAnswer(2)]
      });
      fixture.detectChanges();

      expect(runStatText('Claims verified')).toBeNull();
      expect(runStatText('Second readings')).toBeNull();
    });

    describe('report writing stage', () => {
      const WRITER = {
        reportWriterModelConfigurationId: 9,
        reportWriterDisplayName: 'Report Writer Model',
        reportWriterProvider: 'Anthropic',
        reportWriterModelId: 'claude-writer',
        reportWriterThinkingLevel: 'high'
      };

      function writerRun(overrides: any = {}): any {
        return buildCompletedRun({
          ...WRITER,
          totalQuestionCount: 2,
          answeredQuestionCount: 2,
          answers: [buildScoredAnswer(1), buildScoredAnswer(2)],
          ...overrides
        });
      }

      /** A job view writing the first of two documents; the writer took the slot 72 s before the server's clock. */
      function reportJob(overrides: any = {}): any {
        return {
          runId: 55,
          status: BenchmarkRunReportDocumentsStatus.Writing,
          message: null,
          phase: 'Writing',
          queuedAtUtc: '2026-09-03T07:10:01Z',
          slotAcquiredAtUtc: '2026-09-03T07:10:05Z',
          finishedAtUtc: null,
          cancelRequestedAtUtc: null,
          jobsAhead: null,
          blockingJobLabel: null,
          audiences: [1, 2],
          writerConfigId: 9,
          writerDisplayName: 'Report Writer Model',
          writerProvider: 'Anthropic',
          writerModelId: 'claude-writer',
          writerThinkingLevel: 'high',
          job: {
            id: 'job-1', packId: 'pack-1', subjectKey: 'run:55', subjectLabel: 'Test Model', suiteId: 1,
            suiteName: 'Default Suite', writerConfigId: 9, writerDisplayName: 'Report Writer Model',
            startedByUserId: null, startedAtUtc: '2026-09-03T07:10:05Z', completedAtUtc: null, status: 'Running',
            totalModelCalls: 1, inputTokens: 1000, outputTokens: 200, costUsd: 0.05,
            documents: [
              { audience: 1, status: 'Writing', documentId: null, errorMessage: null, modelCalls: 1 },
              { audience: 2, status: 'Pending', documentId: null, errorMessage: null, modelCalls: 0 }
            ],
            log: []
          },
          serverTimeUtc: '2026-09-03T07:11:17Z',
          ...overrides
        };
      }

      /** Starts the real poll on a run seen Running, with the lock and the document's visibility held still. */
      function startWatching(): jasmine.Spy {
        const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
        spyOn(lockService, 'acquireForRun');
        spyOn(lockService, 'release');
        spyOnProperty(document, 'hidden', 'get').and.returnValue(false);
        const playSpy = spyOn(TestBed.inject(BenchmarkCompletionSoundService), 'play').and.returnValue(Promise.resolve('played'));
        component.completionSound = true;
        benchmarkServiceMock.getRun.and.returnValue(of(writerRun({ status: 'Running', stage: 'Synthesizing', completedAtUtc: null })));
        (component as any).startPolling(55);
        return playSpy;
      }

      function pollTicker(): unknown {
        return (component as any).pollTickerHandle;
      }

      it('should show the report writer in the model strip after the claim verifier', () => {
        component.activeRunDetail = writerRun({
          status: 'Running', stage: 'Answering', claimVerifierDisplayNameUsed: 'Test Verifier'
        });
        fixture.detectChanges();

        const rows: HTMLElement[] = Array.from(
          fixture.nativeElement.querySelectorAll('.benchmark-run-progress-dialog .run-model-strip .run-model-row'));
        const terms = rows.map(row => (row.querySelector('dt')?.textContent || '').trim());
        expect(terms.indexOf('Report writer')).toBe(terms.indexOf('Claim verifier') + 1);

        const writerRow = rows[terms.indexOf('Report writer')];
        expect(writerRow.querySelector('.model-name')?.textContent?.trim()).toBe('Report Writer Model');
        expect(writerRow.querySelector('.thinking-badge')).toBeTruthy();
        expect(writerRow.querySelector('app-provider-badge')).toBeTruthy();
      });

      it('should show no report writer row, no fourth rail item and no Reports cell for a run without a writer', () => {
        component.activeRunDetail = buildCompletedRun({
          status: 'Running', stage: 'Verifying', totalQuestionCount: 2,
          answers: [buildScoredAnswer(1), buildScoredAnswer(2)]
        });
        fixture.detectChanges();

        const dialog = fixture.nativeElement.querySelector('.benchmark-run-progress-dialog') as HTMLElement;
        expect(dialog.textContent).not.toContain('Report writer');
        expect(dialog.querySelectorAll('.run-stage-rail .run-stage').length).toBe(3);
        expect(runStatText('Reports')).toBeNull();
        expect(component.runStageLabel).toContain('Stage 2 of 3 — Follow-up grading passes');
      });

      it('should render a fourth rail item and count the stages of 4 when the run names a writer', () => {
        component.activeRunDetail = writerRun({ status: 'Running', stage: 'Verifying' });
        fixture.detectChanges();

        const items: HTMLElement[] = Array.from(
          fixture.nativeElement.querySelectorAll('.benchmark-run-progress-dialog .run-stage-rail .run-stage'));
        expect(items.length).toBe(4);
        expect(items[3].querySelector('.run-stage-name')?.textContent?.trim()).toBe('Writing reports');
        expect(items[3].classList).not.toContain('is-current');
        expect(items[3].classList).not.toContain('is-done');
        expect(items[1].classList).toContain('is-current');
        expect(component.runStageLabel).toContain('Stage 2 of 4 — Follow-up grading passes');
        expect(component.runDiagnosticsText).toContain('Stage: 2 of 4 (verifying) (server)');
      });

      it('should make stage 4 current while the reports are written, with the job in the status line, stat strip and cost panel', () => {
        benchmarkServiceMock.getRun.and.returnValue(of(writerRun({
          status: 'Completed', reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Writing
        })));
        benchmarkServiceMock.getRunReportJob.and.returnValue(of(reportJob()));

        (component as any).pollRunDetail(55);
        fixture.detectChanges();

        expect(benchmarkServiceMock.getRunReportJob).toHaveBeenCalledWith(55);
        expect(component.runRailStage).toBe(4);
        expect(component.runStageLabel).toBe('Stage 4 of 4 — Writing reports: writing the Executive Summary (1 of 2)');

        const items: HTMLElement[] = Array.from(
          fixture.nativeElement.querySelectorAll('.benchmark-run-progress-dialog .run-stage-rail .run-stage'));
        expect(items.length).toBe(4);
        expect(items[0].classList).toContain('is-done');
        expect(items[1].classList).toContain('is-done');
        expect(items[2].classList).toContain('is-done');
        expect(items[3].classList).toContain('is-current');
        expect(items[3].getAttribute('aria-current')).toBe('step');

        expect(runStatText('Reports')).toBe('1m 12s · writing');
        const writerCost = fixture.nativeElement.querySelector(
          '.benchmark-run-progress-dialog .gh-cost-role--report-writer .gh-cost-role__amount') as HTMLElement;
        expect(writerCost.textContent?.trim()).toBe('$0.0500');
        (component as any).stopPolling();
      });

      it('should name the queue position while the job waits for the report writer', () => {
        benchmarkServiceMock.getRun.and.returnValue(of(writerRun({
          status: 'Completed', reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Pending
        })));
        benchmarkServiceMock.getRunReportJob.and.returnValue(of(reportJob({
          phase: 'Queued', status: BenchmarkRunReportDocumentsStatus.Pending, slotAcquiredAtUtc: null, jobsAhead: 1
        })));

        (component as any).pollRunDetail(55);

        expect(component.runStageLabel).toBe('Stage 4 of 4 — Writing reports: waiting for the report writer (1 job ahead)');
        expect(component.runReportsStatLabel).toBe('Waiting');
        (component as any).stopPolling();
      });

      it('should keep polling through Pending and Writing, then stop and chime once the reports are written', fakeAsync(() => {
        const playSpy = startWatching();
        expect(pollTicker()).not.toBeNull();

        benchmarkServiceMock.getRun.and.returnValue(of(writerRun({
          status: 'Completed', reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Pending
        })));
        benchmarkServiceMock.getRunReportJob.and.returnValue(of(reportJob({
          phase: 'Queued', status: BenchmarkRunReportDocumentsStatus.Pending, slotAcquiredAtUtc: null, jobsAhead: 0
        })));
        tick(2000);
        expect(component.runReportStage).toBe('current');
        expect(component.runStageLabel).toBe('Stage 4 of 4 — Writing reports: waiting for the report writer');
        expect(pollTicker()).not.toBeNull();
        expect(playSpy).not.toHaveBeenCalled();

        benchmarkServiceMock.getRun.and.returnValue(of(writerRun({
          status: 'Completed', reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Writing
        })));
        benchmarkServiceMock.getRunReportJob.and.returnValue(of(reportJob()));
        tick(2000);
        expect(component.runStageLabel).toBe('Stage 4 of 4 — Writing reports: writing the Executive Summary (1 of 2)');
        expect(pollTicker()).not.toBeNull();
        expect(playSpy).not.toHaveBeenCalled();

        benchmarkServiceMock.getRun.and.returnValue(of(writerRun({
          status: 'Completed',
          reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Completed,
          reportDocumentsWrittenCount: 2,
          reportDocumentsDurationMs: 72000,
          reportDocumentsCostUsd: 0.12
        })));
        tick(2000);
        expect(component.runReportStage).toBe('done');
        expect(component.runStageLabel).toBe('Completed. Answered 2 of 2. Reports written: 2 documents, 1m 12s.');
        expect(component.runReportsStatLabel).toBe('2 documents, 1m 12s');
        expect(component.runReportWriterCost).toBe(0.12);
        expect(pollTicker()).toBeNull();
        expect(playSpy).toHaveBeenCalledOnceWith('run:55');

        const polls = benchmarkServiceMock.getRun.calls.count();
        tick(10000);
        expect(benchmarkServiceMock.getRun.calls.count()).toBe(polls);
        discardPeriodicTasks();
      }));

      it('should stop polling a run whose writer never starts once the 30-second grace has passed, and chime then', fakeAsync(() => {
        const playSpy = startWatching();

        benchmarkServiceMock.getRun.and.returnValue(of(writerRun({
          status: 'Completed', reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.NotRequested
        })));
        tick(2000);
        // First seen terminal at 2 s: the grace runs to 32 s.
        expect(component.runReportStage).toBe('current');
        expect(component.runStageLabel).toBe('Stage 4 of 4 — Writing reports: waiting for the report writer');
        expect(playSpy).not.toHaveBeenCalled();

        tick(28000);
        expect(pollTicker()).not.toBeNull();
        expect(playSpy).not.toHaveBeenCalled();

        tick(2000);
        expect(pollTicker()).toBeNull();
        expect(component.runReportStage).toBe('notWritten');
        expect(component.runStageLabel).toBe('Completed. Answered 2 of 2.');
        expect(playSpy).toHaveBeenCalledOnceWith('run:55');

        const polls = benchmarkServiceMock.getRun.calls.count();
        tick(10000);
        expect(benchmarkServiceMock.getRun.calls.count()).toBe(polls);
        discardPeriodicTasks();
      }));

      it('should stop at once and chime for a writer run that ends with another terminal status', fakeAsync(() => {
        const playSpy = startWatching();
        benchmarkServiceMock.getRunReportJob.calls.reset();

        benchmarkServiceMock.getRun.and.returnValue(of(writerRun({
          status: 'CompletedWithErrors', reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Pending
        })));
        tick(2000);

        expect(pollTicker()).toBeNull();
        expect(component.runReportStage).toBe('notWritten');
        expect(benchmarkServiceMock.getRunReportJob).not.toHaveBeenCalled();
        expect(playSpy).toHaveBeenCalledOnceWith('run:55');
        discardPeriodicTasks();
      }));

      it('should put a failed report stage in the status line with its message', () => {
        component.activeRunDetail = writerRun({
          status: 'Completed',
          reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Failed,
          reportDocumentsMessage: 'The writer refused.',
          reportDocumentsWrittenCount: 1,
          reportDocumentsDurationMs: 30000,
          reportDocumentsCostUsd: 0.04
        });

        expect(component.runReportStage).toBe('ended');
        expect(component.runRailStage).toBe(0);
        expect(component.runStageLabel).toBe('Completed. Answered 2 of 2. Report writing failed: The writer refused.');
        expect(component.runReportWriterCost).toBe(0.04);
      });

      it('should add a REPORTS block to the diagnostics of a run that names a writer', () => {
        component.activeRunDetail = writerRun({
          status: 'Completed',
          reportDocumentsStatus: BenchmarkRunReportDocumentsStatus.Completed,
          reportDocumentsMessage: null,
          reportDocumentsWrittenCount: 2,
          reportDocumentsDurationMs: 72000,
          reportDocumentsInputTokens: 1000,
          reportDocumentsOutputTokens: 500,
          reportDocumentsCostUsd: 0.12
        });

        const text = component.runDiagnosticsText;
        expect(text).toContain('--- REPORTS ---');
        expect(text).toContain('Writer: Report Writer Model (Anthropic / claude-writer), thinking: high');
        expect(text).toContain('Status: Completed');
        expect(text).toContain('Message: none');
        expect(text).toContain('Documents written: 2');
        expect(text).toContain('Duration: 1m 12s');
        expect(text).toContain('Tokens: input 1000, output 500');
        expect(text).toContain("Cost: $0.1200 (outside the run's own cost)");
        expect(text.indexOf('--- REPORTS ---')).toBeLessThan(text.indexOf('--- FLAGS ---'));

        component.activeRunDetail = buildCompletedRun();
        expect(component.runDiagnosticsText).not.toContain('--- REPORTS ---');
      });
    });

    it('should chip a re-graded row as Verifying or Second opinion rather than Scored', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 3,
        stage: 'Verifying',
        inFlightVerificationOrderIndexes: [1],
        inFlightSecondOpinionOrderIndexes: [2],
        answers: [buildScoredAnswer(1), buildScoredAnswer(2), buildScoredAnswer(3)]
      });

      const rows = component.runProgressRows;

      // Both rows already carry a score; without the in-flight sets they read as finished for
      // the whole pass.
      expect(rows[0].status).toBe('Verifying');
      expect(component.runRowChipLabel(rows[0])).toBe('Verifying');
      expect(component.runRowChipClass(rows[0])).toBe('status-verifying');

      expect(rows[1].status).toBe('SecondOpinion');
      expect(component.runRowChipLabel(rows[1])).toBe('Second reader');
      expect(component.runRowChipClass(rows[1])).toBe('status-secondopinion');

      expect(component.runRowChipLabel(rows[2])).toBe('Scored');
    });

    it('should list every question during a re-run and mark only the re-run set', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 3,
        answers: [
          buildScoredAnswer(1),
          buildScoredAnswer(2, { status: 'ProviderError', assessmentStatus: 'Pending' }),
          buildScoredAnswer(3)
        ]
      });
      component.rerunScopeOrderIndexes = [2];

      const rows = component.runProgressRows;

      // The row list is unchanged: the whole suite stays listed and the rows outside the
      // re-run keep the status they already have.
      expect(rows.length).toBe(3);
      expect(component.runHasRerunScope).toBeTrue();
      expect(component.isRerunScope(rows[0])).toBeFalse();
      expect(component.isRerunScope(rows[1])).toBeTrue();
      expect(component.isRerunScope(rows[2])).toBeFalse();
      expect(component.runRowChipLabel(rows[0])).toBe('Scored');
      expect(component.runRowChipLabel(rows[2])).toBe('Scored');
    });

    function buildRerunRun(overrides: any = {}, answer2: any = {}): any {
      return buildCompletedRun({
        status: 'Running',
        stage: 'Answering',
        totalQuestionCount: 3,
        rerunScopeOrderIndexes: [2],
        inFlightOrderIndexes: [],
        rerunAnsweredOrderIndexes: [],
        answers: [
          buildScoredAnswer(1),
          buildScoredAnswer(2, { status: 'ProviderError', assessmentStatus: 'Failed', ...answer2 }),
          buildScoredAnswer(3)
        ],
        ...overrides
      });
    }

    it('should chip a re-run question Answering while its request is in flight even though it already has an answer row', () => {
      component.activeRunDetail = buildRerunRun({ inFlightOrderIndexes: [2] });

      const rows = component.runProgressRows;

      expect(rows[1].status).toBe('Answering');
      expect(component.runRowChipLabel(rows[1])).toBe('Answering');
      expect(component.runRowChipClass(rows[1])).toBe('status-answering');
      expect(component.runRowChipLabel(rows[0])).toBe('Scored');
      expect(component.runRowChipLabel(rows[2])).toBe('Scored');
    });

    it('should chip a queued re-run question Pending rather than its previous failure while the re-run is running', () => {
      component.activeRunDetail = buildRerunRun();

      let rows = component.runProgressRows;

      expect(rows[1].status).toBe('Pending');
      expect(component.runRowChipLabel(rows[1])).toBe('Pending');

      // Bounded to a running re-run: a terminal run shows the row's real status.
      component.activeRunDetail = buildRerunRun({ status: 'Completed' });
      rows = component.runProgressRows;

      expect(component.runRowChipLabel(rows[1])).toBe('Provider Error');
    });

    it('should follow a re-answered re-run question through Answered, Assessing and Scored', () => {
      component.activeRunDetail = buildRerunRun({ rerunAnsweredOrderIndexes: [2] }, { status: 'Ok', assessmentStatus: 'Pending' });
      expect(component.runRowChipLabel(component.runProgressRows[1])).toBe('Answered');

      component.activeRunDetail = buildRerunRun({ rerunAnsweredOrderIndexes: [2] }, { status: 'Ok', assessmentStatus: 'Assessing' });
      let row = component.runProgressRows[1];
      expect(component.runRowChipLabel(row)).toBe('Assessing');
      expect(component.runRowChipClass(row)).toBe('status-assessing');

      component.activeRunDetail = buildRerunRun({ rerunAnsweredOrderIndexes: [2] }, { status: 'Ok', assessmentStatus: 'Scored' });
      row = component.runProgressRows[1];
      expect(component.runRowChipLabel(row)).toBe('Scored');
    });

    it("should list a finished run's own answers only, whatever its suite lost or gained since", () => {
      // The suite lost question 12 (answered as Q2) and gained question 40, which now sits at Q2.
      component.activeRunDetail = buildCompletedRun({
        status: 'Completed',
        totalQuestionCount: 3,
        answers: [
          buildScoredAnswer(1, { benchmarkQuestionId: 11, questionText: 'Stored question 1' }),
          buildScoredAnswer(2, { benchmarkQuestionId: null, questionText: 'Deleted question, as asked' }),
          buildScoredAnswer(3, { benchmarkQuestionId: 13, questionText: 'Stored question 3' })
        ]
      });
      component.runProgressQuestions = [
        { id: 11, orderIndex: 1, questionText: 'Question 1 as the suite words it now' },
        { id: 40, orderIndex: 2, questionText: 'A question added after the run' },
        { id: 13, orderIndex: 3, questionText: 'Question 3 as the suite words it now' }
      ] as any;

      const rows = component.runProgressRows;

      expect(rows.map(r => r.orderIndex)).toEqual([1, 2, 3]);
      expect(rows.map(r => r.questionText))
        .toEqual(['Stored question 1', 'Deleted question, as asked', 'Stored question 3']);
      expect(rows.map(r => component.runRowChipLabel(r))).toEqual(['Scored', 'Scored', 'Scored']);
      expect(rows.every(r => r.answer != null)).toBeTrue();
    });

    it('should add a question the first pass has not answered yet, matched by question id rather than order index', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 3,
        inFlightOrderIndexes: [3],
        answers: [buildScoredAnswer(1, { benchmarkQuestionId: 11 })]
      });
      component.runProgressQuestions = [
        { id: 11, orderIndex: 1, questionText: 'Question 1' },
        { id: 12, orderIndex: 2, questionText: 'Question 2' },
        { id: 13, orderIndex: 3, questionText: 'Question 3' }
      ] as any;

      const rows = component.runProgressRows;

      expect(rows.map(r => r.orderIndex)).toEqual([1, 2, 3]);
      expect(rows[0].questionText).toBe('Question 1');
      expect(rows[0].answer?.benchmarkQuestionId).toBe(11);
      expect(rows[1].status).toBe('Pending');
      expect(rows[1].answer).toBeNull();
      expect(rows[2].status).toBe('Answering');
    });

    it('should add no live question while a re-run is running, even one the suite gained since', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        rerunScopeOrderIndexes: [2],
        rerunAnsweredOrderIndexes: [],
        answers: [
          buildScoredAnswer(1, { benchmarkQuestionId: 11 }),
          buildScoredAnswer(2, { benchmarkQuestionId: 12, status: 'ProviderError', assessmentStatus: 'Failed' })
        ]
      });
      component.runProgressQuestions = [
        { id: 11, orderIndex: 1, questionText: 'Question 1' },
        { id: 12, orderIndex: 2, questionText: 'Question 2' },
        { id: 40, orderIndex: 3, questionText: 'A question added after the run' }
      ] as any;

      expect(component.runIsFirstPass).toBeFalse();
      expect(component.runProgressRows.map(r => r.orderIndex)).toEqual([1, 2]);

      // A single-answer re-run whose scope the server has not reported yet is still not a first pass.
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        rerunStartedAtUtc: '2026-09-24T10:00:00Z',
        answers: [buildScoredAnswer(1, { benchmarkQuestionId: 11 }), buildScoredAnswer(2, { benchmarkQuestionId: 12 })]
      });
      expect(component.runIsFirstPass).toBeFalse();
      expect(component.runProgressRows.map(r => r.orderIndex)).toEqual([1, 2]);
    });

    it("should write each question's diagnostics line from its own answer", () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Completed',
        totalQuestionCount: 2,
        answers: [
          buildScoredAnswer(1, { benchmarkQuestionId: 11, durationMs: 1111 }),
          buildScoredAnswer(2, { benchmarkQuestionId: null, durationMs: 2222 })
        ]
      });
      component.runProgressQuestions = [
        { id: 40, orderIndex: 2, questionText: 'A question added after the run' }
      ] as any;

      const diagnostics = component.runDiagnosticsText;

      expect(diagnostics).toMatch(/\[Q1\][^\n]*duration=1111ms/);
      expect(diagnostics).toMatch(/\[Q2\][^\n]*duration=2222ms/);
      expect(diagnostics).not.toContain('[Q2] status=Pending');
    });

    /** The icon-only View game snapshot button of the run report's header actions. */
    function viewGameSnapshotButton(): HTMLButtonElement | undefined {
      const buttons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll(
        '.benchmark-run-detail-dialog [role="group"][aria-label="Run actions"] button'));
      return buttons.find(b => (b.getAttribute('aria-label') || '').startsWith('View game snapshot'));
    }

    it("should open the run's own board read-only from View Game Snapshot", () => {
      const board = { name: 'Run board', sanitizedText: 'Line 1\nLine 2', digestText: null, charCount: 13, sha256: 'feedbeef' };
      benchmarkServiceMock.getRunBoard.and.returnValue(of(board));
      component.selectedRunDetail = buildCompletedRun({ hasBoardRecord: true, gameSnapshotSha256Used: 'feedbeef' });
      fixture.detectChanges();

      const button = viewGameSnapshotButton();
      expect(button).toBeTruthy();
      button!.click();

      expect(benchmarkServiceMock.getRunBoard).toHaveBeenCalledWith(55);
      const viewer = component.snapshotViewer!;
      expect(viewer.readOnly).toBeTrue();
      expect(viewer.readOnlyBoard).toEqual(board);

      const dialog = viewer.viewerDialog.nativeElement;
      expect(dialog.open).toBeTrue();
      expect(dialog.textContent).toContain('The board this run was made with.');
      expect(dialog.querySelector('.readonly-board-text')?.textContent).toBe('Line 1\nLine 2');
      expect(Array.from(dialog.querySelectorAll('[role="tab"]')).map(t => (t.textContent || '').trim()))
        .toEqual(['Game Snapshot']);
      expect(dialog.querySelector('.delete-snapshot-btn')).toBeNull();
      expect(dialog.querySelector('.save-all-btn')).toBeNull();
      expect(dialog.querySelector('app-snapshot-text-editor')).toBeNull();

      viewer.close();
    });

    it('should offer View Game Snapshot for a run that recorded only its board hash', () => {
      component.selectedRunDetail = buildCompletedRun({ gameSnapshotSha256Used: 'feedbeef' });
      expect(component.selectedRunHasBoard).toBeTrue();
    });

    it('should hide View Game Snapshot for a run made without a board', () => {
      component.selectedRunDetail = buildCompletedRun();
      fixture.detectChanges();

      expect(component.selectedRunHasBoard).toBeFalse();
      expect(viewGameSnapshotButton()).toBeUndefined();
    });

    it("should measure Elapsed from the re-run's own start while a re-run scope is active", () => {
      const ninetySecondsAgo = new Date(Date.now() - 90000).toISOString();
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        startedAtUtc: '2026-09-11T00:00:00Z',
        completedAtUtc: '2026-09-11T01:00:00Z',
        rerunStartedAtUtc: ninetySecondsAgo,
        rerunCompletedAtUtc: null,
        rerunScopeOrderIndexes: [4],
        rerunAnsweredOrderIndexes: [4],
        rerunScoredOrderIndexes: []
      });

      expect(component.runElapsedIsRerun).toBeTrue();
      expect(component.runElapsedLabel).toMatch(/^1m 3\ds$/);

      const diagnostics = component.runDiagnosticsText;
      expect(diagnostics).toContain('Re-run answered: Q4 (1 of 1); re-run scored: none (0 of 1)');
      expect(diagnostics).toContain(`Re-run started:   ${ninetySecondsAgo}`);
      expect(diagnostics).toContain('Re-run completed: n/a');
      expect(diagnostics).toContain('Elapsed (run):    1h 00m 00s');
      expect(diagnostics).toMatch(/Re-run elapsed: {3}1m 3\ds/);
      expect(diagnostics).toContain('Failed-question re-run in progress over: Q4');
      expect(diagnostics).not.toContain('re-run covered');
    });

    it('should print the run span, the re-run span, the covered scope and the run-wide counts for a terminal re-run', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Completed',
        totalQuestionCount: 3,
        startedAtUtc: '2026-09-18T21:01:24Z',
        completedAtUtc: '2026-09-18T21:19:44Z',
        rerunStartedAtUtc: '2026-09-18T21:30:00Z',
        rerunCompletedAtUtc: '2026-09-18T21:32:12Z',
        rerunScopeOrderIndexes: [2],
        rerunAnsweredOrderIndexes: [2],
        rerunScoredOrderIndexes: [2],
        // The finalizer's run-wide figures, which the scoped meters must not replace.
        claimVerifiedAnswerCount: 3,
        secondOpinionGradedAnswerCount: 2,
        answers: [
          buildScoredAnswer(1, { claimVerificationJson: '[]', secondOpinionQualityScore: 70 }),
          buildScoredAnswer(2, { claimVerificationJson: '[]' }),
          buildScoredAnswer(3, { claimVerificationJson: '[]', secondOpinionQualityScore: 60 })
        ]
      });
      component.rerunScopeOrderIndexes = [];

      const diagnostics = component.runDiagnosticsText;
      expect(diagnostics).toContain('Elapsed (run):    18m 20s');
      expect(diagnostics).toContain('Re-run elapsed:   2m 12s');
      expect(diagnostics).toContain('Failed-question re-run covered: Q2');
      expect(diagnostics).not.toContain('in progress over');
      expect(diagnostics).toContain('Answers with verified claims: 3, second-graded 2 (re-run scope: 1, 0)');
    });

    it('should print the re-run span from its stamps once the process no longer reports a scope', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Completed',
        startedAtUtc: '2026-09-18T21:01:24Z',
        completedAtUtc: '2026-09-18T21:19:44Z',
        rerunStartedAtUtc: '2026-09-18T21:30:00Z',
        rerunCompletedAtUtc: '2026-09-18T21:32:12Z',
        rerunScopeOrderIndexes: [],
        claimVerifiedAnswerCount: 18,
        secondOpinionGradedAnswerCount: 13
      });
      component.rerunScopeOrderIndexes = [];

      const diagnostics = component.runDiagnosticsText;
      expect(diagnostics).toContain('Elapsed (run):    18m 20s');
      expect(diagnostics).toContain('Re-run elapsed:   2m 12s');
      expect(diagnostics).toContain('Answers with verified claims: 18, second-graded 13');
      expect(diagnostics).not.toContain('re-run scope:');
    });

    it("should keep counting Re-run elapsed when a previous re-run's completion stamp is still on the row", () => {
      const ninetySecondsAgo = new Date(Date.now() - 90000).toISOString();
      const anHourBeforeThatStart = new Date(Date.now() - 90000 - 3600000).toISOString();
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        startedAtUtc: '2026-09-11T00:00:00Z',
        completedAtUtc: '2026-09-11T01:00:00Z',
        rerunStartedAtUtc: ninetySecondsAgo,
        // Left over from an earlier re-run of the same run; a legacy row predating the server-side
        // clear-on-start fix. Earlier than rerunStartedAtUtc, so elapsedMsBetween would clamp to 0
        // if it were passed as the end while the re-run is still running.
        rerunCompletedAtUtc: anHourBeforeThatStart,
        rerunScopeOrderIndexes: [4],
        rerunAnsweredOrderIndexes: [],
        rerunScoredOrderIndexes: []
      });

      expect(component.runElapsedIsRerun).toBeTrue();
      expect(component.runElapsedLabel).toMatch(/^1m 3\ds$/);
    });

    it('should count only gradeable answers as the index population', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Completed',
        totalQuestionCount: 4,
        answers: [
          buildScoredAnswer(1),
          buildScoredAnswer(2, { status: 'ProviderError' }),
          // Finished normally and produced nothing: scored 0 rather than excused, so it counts.
          buildScoredAnswer(3, { status: 'EmptyAnswer', providerFinishReason: 'end_turn' }),
          // No recorded finish reason is not evidence of a normal stop.
          buildScoredAnswer(4, { status: 'EmptyAnswer', providerFinishReason: null })
        ]
      });

      expect(component.runGradeableAnswerCount).toBe(2);
      expect(component.runDiagnosticsText).toContain('Gradeable answers (index population): 2 of 4');
    });

    it('should call zero knowledge-base calls prompt-compliant only when the suite has no knowledge-base topic', () => {
      const run = (hasKnowledgeBaseRoutingQuestion: boolean) => buildCompletedRun({
        status: 'Completed',
        toolFamilyCounts: { source: 2, wiki: 1 },
        zeroKnowledgeBaseAnswerCount: 2,
        hasKnowledgeBaseRoutingQuestion,
        answers: [buildScoredAnswer(1), buildScoredAnswer(2)]
      });

      component.activeRunDetail = run(false);
      let text = component.runDiagnosticsText;
      expect(text).toContain('answers with 0 knowledge base calls: 2 of 2 gradeable');
      expect(text).toContain('  (prompt-compliant on game-mechanics topics');
      expect(text).not.toContain('the suite has knowledge-base topics');

      component.activeRunDetail = run(true);
      text = component.runDiagnosticsText;
      expect(text).toContain('answers with 0 knowledge base calls: 2 of 2 gradeable');
      expect(text).toContain("  (the suite has knowledge-base topics; see the report's Tool Routing section)");
      expect(text).not.toContain('prompt-compliant');
    });

    it('should read the rerun meters against the client-captured scope before any re-run answer lands', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        stage: 'Answering',
        totalQuestionCount: 18,
        answers: Array.from({ length: 18 }, (_, i) => buildScoredAnswer(i + 1)),
        rerunAnsweredOrderIndexes: [],
        rerunScoredOrderIndexes: []
      });
      component.rerunScopeOrderIndexes = [4, 9];

      expect(component.effectiveRerunScope).toEqual([4, 9]);
      expect(component.runMeterTotal).toBe(2);
      expect(component.runMeterAnswered).toBe(0);
      expect(component.runMeterScored).toBe(0);
      expect(component.runStageLabel).toContain('Answered 0 of 2 re-run questions');
    });

    it('should read the rerun meters as re-run answers land inside the scope', () => {
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        stage: 'Answering',
        totalQuestionCount: 18,
        answers: Array.from({ length: 18 }, (_, i) => buildScoredAnswer(i + 1)),
        rerunAnsweredOrderIndexes: [4],
        rerunScoredOrderIndexes: [4]
      });
      component.rerunScopeOrderIndexes = [4, 9];

      expect(component.runMeterTotal).toBe(2);
      expect(component.runMeterAnswered).toBe(1);
      expect(component.runMeterScored).toBe(1);
      expect(component.runStageLabel).toContain('Answered 1 of 2 re-run questions');
    });

    it('should prefer the server-reported rerun scope over an empty client-captured one', () => {
      component.activeRunDetail = buildCompletedRun({
        rerunScopeOrderIndexes: [3, 7, 11]
      });
      component.rerunScopeOrderIndexes = [];

      expect(component.effectiveRerunScope).toEqual([3, 7, 11]);
    });

    it('should chip a Canceled row with the Canceled label and class', () => {
      const row = { orderIndex: 5, questionText: 'Q5', status: 'Canceled', assessmentStatus: '', errorMessage: null };

      expect(component.runRowChipLabel(row)).toBe('Canceled');
      expect(component.runRowChipClass(row)).toBe('status-canceled');
    });

    it('should format answer status 6 as Canceled', () => {
      expect(component.formatAnswerStatus(6)).toBe('Canceled');
    });

    it('should reject a speed difficulty scaling outside 0.0 to 5.0 before calling the server', () => {
      component.editingProfileId = null;
      component.profileForm = { ...component.profileForm, name: 'Scaled Profile', speedDifficultyScaling: 5.5 };

      component.saveProfile();

      expect(component.profileValidationErrors)
        .toContain('Speed difficulty scaling must be between 0.0 and 5.0.');
      expect(benchmarkServiceMock.createScoringProfile).not.toHaveBeenCalled();
    });
  });

  // ---------------------------------------------------------------------------
  // Layout regression guards: the launcher's three groups and the fields each holds, and the
  // run-model-strip in the progress dialog shares one column edge between rows.
  // ---------------------------------------------------------------------------
  describe('model selector row layout', () => {
    /** The text of each direct field's first label in one launcher group, whitespace-normalized. */
    function groupLabels(group: string): string[] {
      const fieldset = fixture.nativeElement.querySelector(`.setup-group-${group}`) as HTMLElement;
      return (Array.from(fieldset.querySelectorAll(':scope > .form-group')) as HTMLElement[])
        .map(g => (g.querySelector('label')?.textContent ?? '').replace(/\s+/g, ' ').trim());
    }

    it('should lay the launcher out as three setup groups in order', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const groups = Array.from(
        fixture.nativeElement.querySelectorAll('.setup-groups > fieldset.setup-group')
      ) as HTMLElement[];
      expect(groups.length).toBe(3);
      expect(groups.map(g => g.querySelector('legend')?.textContent?.trim()))
        .toEqual(['Test Setup', 'Grading', 'Execution']);
    });

    it('should hold the Test Setup fields in order', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const labels = groupLabels('test');
      expect(labels.length).toBe(4);
      expect(labels[0]).toMatch(/^Benchmark Suite/);
      expect(labels[1]).toMatch(/^Scoring Profile/);
      expect(labels[2]).toMatch(/^Response Style/);
      expect(labels[3]).toMatch(/^Source Code References/);
    });

    it('should lift the Model Under Test into a primary field above the setup groups', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const card = fixture.nativeElement.querySelector('.setup-card') as HTMLElement;
      const primary = card.querySelector('.setup-primary-field') as HTMLElement;
      const groups = card.querySelector('.setup-groups-container') as HTMLElement;
      expect(primary).toBeTruthy();
      expect(groups).toBeTruthy();
      expect(primary.compareDocumentPosition(groups) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(primary.closest('fieldset')).toBeNull();
      expect(primary.querySelector('#bmTestedModelLabel')?.textContent?.trim()).toBe('Model Under Test');
      expect(primary.querySelector('.tested-model-selector')).toBeTruthy();
      expect(card.querySelector('fieldset .tested-model-selector')).toBeNull();
    });

    it('should hold the Grading fields in order, with the optional ones tagged', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const labels = groupLabels('grading');
      expect(labels.length).toBe(5);
      expect(labels[0]).toBe('Assessor');
      expect(labels[1]).toMatch(/^Co-Assessor/);
      expect(labels[2]).toMatch(/^Second Reader/);
      expect(labels[3]).toMatch(/^Claim Verifier/);
      expect(labels[4]).toMatch(/^Report Writer/);

      for (const id of ['bmCoAssessorModelLabel', 'bmSecondOpinionModelLabel', 'bmClaimVerifierModelLabel', 'bmReportWriterModelLabel']) {
        expect(fixture.nativeElement.querySelector(`#${id} .field-optional`)?.textContent?.trim()).toBe('Optional');
      }
      expect(fixture.nativeElement.querySelector('#bmAssessorModelLabel .field-optional')).toBeNull();

      // Second Opinion Mode depends on the second opinion model, so it hangs off that field.
      const mode = fixture.nativeElement.querySelector('#secondOpinionModeSelect') as HTMLElement;
      const dependent = mode.closest('.dependent-field') as HTMLElement;
      expect(dependent).toBeTruthy();
      expect(dependent.parentElement!.querySelector(':scope > #bmSecondOpinionModelLabel')).toBeTruthy();

      expect(fixture.nativeElement.querySelector('#bmCoAssessorModelHint')?.textContent)
        .toContain('the score is the mean of the two');
    });

    it('should name and describe each launcher picker by its own field', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const pickers: [string, string][] = [
        ['tested-model-selector', 'bmTestedModel'],
        ['assessor-model-selector', 'bmAssessorModel'],
        ['co-assessor-model-selector', 'bmCoAssessorModel'],
        ['second-opinion-model-selector', 'bmSecondOpinionModel'],
        ['claim-verifier-model-selector', 'bmClaimVerifierModel']
      ];
      for (const [marker, prefix] of pickers) {
        const trigger = fixture.nativeElement.querySelector(`.${marker} .selector-trigger`) as HTMLButtonElement;
        expect(trigger).withContext(marker).toBeTruthy();
        expect(trigger.getAttribute('aria-labelledby')!.startsWith(`${prefix}Label `)).withContext(marker).toBeTrue();
        expect(trigger.getAttribute('aria-describedby')).withContext(marker).toBe(`${prefix}Hint`);
      }
    });

    it('should render both dt/dd pairs and keep .run-model-row present in the run-model-strip', () => {
      component.activeRunDetail = {
        id: 42,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.7-flash',
        testedModelParallelExecutionModeUsed: 2,
        assessorModelDisplayNameUsed: 'GPT-5.6 Luna',
        assessorModelProviderUsed: 'OpenAI',
        assessorModelIdUsed: 'gpt-5.6-luna',
        totalQuestionCount: 10,
        answers: []
      } as any;
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const strip = fixture.nativeElement.querySelector('.run-model-strip');
      expect(strip).toBeTruthy();

      const rows = strip.querySelectorAll('.run-model-row');
      expect(rows.length).toBe(2);

      const dts = strip.querySelectorAll('dt');
      const dds = strip.querySelectorAll('dd');
      expect(dts.length).toBe(2);
      expect(dds.length).toBe(2);
      expect(dts[0].textContent?.trim()).toBe('Model under test');
      expect(dds[0].textContent).toContain('Gemini 3.7 Flash');
      expect(dts[1].textContent?.trim()).toBe('Assessor');
      expect(dds[1].textContent).toContain('GPT-5.6 Luna');

      // The alignment itself comes from the grid CSS (max-content / minmax(0, 1fr)),
      // which a unit test cannot assert — only that the markup it depends on is present.
    });

    // H6. The Endpoint row under Model under test follows the same rule as the grader rows
    // below it: the official endpoint is the assumed default and prints nothing extra.
    it('should render no Endpoint row for the official endpoint', () => {
      component.activeRunDetail = {
        id: 42,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.7-flash',
        testedModelParallelExecutionModeUsed: 2,
        testedModelEndpoint: 'official',
        assessorModelDisplayNameUsed: 'GPT-5.6 Luna',
        assessorModelProviderUsed: 'OpenAI',
        assessorModelIdUsed: 'gpt-5.6-luna',
        totalQuestionCount: 10,
        answers: []
      } as any;
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const strip = fixture.nativeElement.querySelector('.run-model-strip');
      const dts = Array.from(strip.querySelectorAll('dt')) as HTMLElement[];
      expect(dts.map(dt => dt.textContent?.trim())).not.toContain('Endpoint');
    });

    it('should render the Endpoint row for a custom endpoint', () => {
      component.activeRunDetail = {
        id: 42,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.7-flash',
        testedModelParallelExecutionModeUsed: 2,
        testedModelEndpoint: 'custom (contoso.example; fingerprint ab12cd34)',
        assessorModelDisplayNameUsed: 'GPT-5.6 Luna',
        assessorModelProviderUsed: 'OpenAI',
        assessorModelIdUsed: 'gpt-5.6-luna',
        totalQuestionCount: 10,
        answers: []
      } as any;
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const strip = fixture.nativeElement.querySelector('.run-model-strip');
      const dts = Array.from(strip.querySelectorAll('dt')) as HTMLElement[];
      const endpointIndex = dts.findIndex(dt => dt.textContent?.trim() === 'Endpoint');
      expect(endpointIndex).toBeGreaterThan(-1);
      const dds = strip.querySelectorAll('dd');
      expect(dds[endpointIndex].textContent).toContain('custom (contoso.example; fingerprint ab12cd34)');
    });

    it('should give every roster badge a spoken prefix and no title attribute', () => {
      component.activeRunDetail = {
        id: 42,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.7-flash',
        testedModelReasoningModeUsed: 'pro',
        testedModelServiceTierUsed: 'flex',
        testedModelParallelExecutionModeUsed: 0,
        assessorModelDisplayNameUsed: 'GPT-5 Mini',
        assessorModelProviderUsed: 'OpenAI',
        assessorModelIdUsed: 'gpt-5-mini',
        isPanelRun: true,
        coAssessorModelDisplayNameUsed: 'Claude Opus 4',
        coAssessorModelProviderUsed: 'Anthropic',
        coAssessorModelIdUsed: 'claude-opus-4',
        secondOpinionAssessorModelDisplayNameUsed: 'Gemini 3.7 Pro',
        secondOpinionAssessorModelProviderUsed: 'Google',
        secondOpinionAssessorModelIdUsed: 'gemini-3.7-pro',
        secondOpinionModeUsed: 3,
        claimVerifierDisplayNameUsed: 'GPT-5 Nano',
        claimVerifierProviderUsed: 'OpenAI',
        claimVerifierModelIdUsed: 'gpt-5-nano',
        totalQuestionCount: 10,
        answers: []
      } as any;
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const strip = fixture.nativeElement.querySelector('.run-model-strip') as HTMLElement;
      const text = (el: Element | null | undefined) => el?.textContent?.trim();
      expect(strip.querySelectorAll('[title]').length).toBe(0);
      expect(strip.querySelector('.tier-badge')).toBeNull();
      expect(strip.querySelector('.second-opinion-mode-badge')).toBeNull();
      expect(text(strip.querySelector('.parallel-badge'))).toBe('parallel execution Sequential, disabled for this key');
      expect(text(strip.querySelector('.reasoning-badge'))).toBe('reasoning mode pro');

      const dts = Array.from(strip.querySelectorAll('dt')) as HTMLElement[];
      const readerIndex = dts.findIndex(dt => dt.textContent?.trim() === 'Reference reader');
      expect(readerIndex).toBeGreaterThan(-1);
      const readerRow = strip.querySelectorAll('dd')[readerIndex];
      expect(text(readerRow.querySelector('.config-badge'))).toBe('coverage Every answer, blind (reference reading)');
      expect(readerRow.querySelector('app-info-tip .gh-info-btn')?.getAttribute('aria-label')).toBe('About Reference reader coverage');
      expect(fixture.nativeElement.querySelector('#runProgressCoverageTip')?.textContent).toContain('never scores');
    });

    it('should render second opinion assessor row under Assessor with selected mode when configured', () => {
      component.activeRunDetail = {
        id: 42,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.7-flash',
        testedModelParallelExecutionModeUsed: 2,
        assessorModelDisplayNameUsed: 'GPT-5.6 Luna',
        assessorModelProviderUsed: 'OpenAI',
        assessorModelIdUsed: 'gpt-5.6-luna',
        secondOpinionAssessorModelDisplayNameUsed: 'Claude Opus 5',
        secondOpinionAssessorModelProviderUsed: 'Anthropic',
        secondOpinionAssessorModelIdUsed: 'claude-opus-5',
        secondOpinionAssessorModelThinkingLevelUsed: 'high',
        secondOpinionModeUsed: 1, // Only flagged answers
        totalQuestionCount: 10,
        answers: []
      } as any;
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const strip = fixture.nativeElement.querySelector('.run-model-strip');
      expect(strip).toBeTruthy();

      const rows = strip.querySelectorAll('.run-model-row');
      expect(rows.length).toBe(3);

      const dts = strip.querySelectorAll('dt');
      const dds = strip.querySelectorAll('dd');
      expect(dts.length).toBe(3);
      expect(dds.length).toBe(3);
      expect(dts[0].textContent?.trim()).toBe('Model under test');
      expect(dts[1].textContent?.trim()).toBe('Assessor');
      expect(dts[2].textContent?.trim()).toBe('Second reader');

      expect(dds[2].textContent).toContain('Claude Opus 5');
      expect(dds[2].querySelector('.thinking-badge')?.textContent?.trim()).toBe('thinking level High');
      expect(dds[2].querySelector('.provider-badge')?.textContent?.trim()).toBe('Anthropic');
      const modeBadge = dds[2].querySelector('.config-badge');
      expect(modeBadge).toBeTruthy();
      expect(modeBadge?.textContent?.trim()).toBe('coverage Only flagged answers');
      expect(modeBadge?.hasAttribute('title')).toBeFalse();
      expect(dds[2].querySelector('app-info-tip .gh-info-btn')?.getAttribute('aria-label')).toBe('About Second reader coverage');
      expect(fixture.nativeElement.querySelector('#runProgressCoverageTip')?.textContent).toContain('raised a flag');
    });

    it('should not render second opinion row if mode is Off (0)', () => {
      component.activeRunDetail = {
        id: 42,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.7-flash',
        assessorModelDisplayNameUsed: 'GPT-5.6 Luna',
        assessorModelProviderUsed: 'OpenAI',
        assessorModelIdUsed: 'gpt-5.6-luna',
        secondOpinionAssessorModelDisplayNameUsed: 'Claude Opus 5',
        secondOpinionAssessorModelProviderUsed: 'Anthropic',
        secondOpinionAssessorModelIdUsed: 'claude-opus-5',
        secondOpinionModeUsed: 0,
        totalQuestionCount: 10,
        answers: []
      } as any;
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const strip = fixture.nativeElement.querySelector('.run-model-strip');
      expect(strip).toBeTruthy();
      const rows = strip.querySelectorAll('.run-model-row');
      expect(rows.length).toBe(2);
    });

    it('should add a Co-assessor row after Assessor and call the second opinion the reference reader in a panel run', () => {
      component.activeRunDetail = {
        id: 42,
        status: 'Running',
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.7 Flash',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.7-flash',
        testedModelParallelExecutionModeUsed: 2,
        assessorModelDisplayNameUsed: 'GPT-5 Mini',
        assessorModelProviderUsed: 'OpenAI',
        assessorModelIdUsed: 'gpt-5-mini',
        isPanelRun: true,
        coAssessorModelDisplayNameUsed: 'Claude Opus 4',
        coAssessorModelProviderUsed: 'Anthropic',
        coAssessorModelIdUsed: 'claude-opus-4',
        coAssessorModelThinkingLevelUsed: 'high',
        secondOpinionAssessorModelDisplayNameUsed: 'Gemini 3.7 Pro',
        secondOpinionAssessorModelProviderUsed: 'Google',
        secondOpinionAssessorModelIdUsed: 'gemini-3.7-pro',
        secondOpinionModeUsed: 3,
        totalQuestionCount: 10,
        answers: []
      } as any;
      component.isRunProgressDialogOpen = true;
      fixture.detectChanges();

      const strip = fixture.nativeElement.querySelector('.run-model-strip');
      const dts = Array.from(strip.querySelectorAll('dt')) as HTMLElement[];
      expect(dts.map(dt => dt.textContent?.trim())).toEqual(['Model under test', 'Assessor', 'Co-assessor', 'Reference reader']);

      const dds = strip.querySelectorAll('dd');
      expect(dds[2].textContent).toContain('Claude Opus 4');
      expect(dds[2].querySelector('.thinking-badge')?.textContent?.trim()).toBe('thinking level High');
      expect(dds[2].querySelector('.provider-badge')?.textContent?.trim()).toBe('Anthropic');
    });

    it('should format second opinion modes and hints correctly', () => {
      expect(component.formatSecondOpinionMode(0)).toBe('');
      expect(component.formatSecondOpinionMode(1)).toBe('Only flagged answers');
      expect(component.formatSecondOpinionMode(2)).toBe('Flagged answers and statistical outliers');
      expect(component.formatSecondOpinionMode(3)).toBe('Every answer (double grading)');

      expect(component.secondOpinionModeHintOf(1)).toContain('raised a flag');
      expect(component.secondOpinionModeHintOf(3)).toContain('unbiased measure of grading reliability');
    });

    it('should explain what the Model Under Test and the Assessor each do', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('#bmTestedModelHint')?.textContent).toContain('Answers every question');
      expect(fixture.nativeElement.querySelector('#bmAssessorModelHint')?.textContent)
        .toContain('four dimensions');
    });

    it('should describe the benchmark suite and the scoring profile', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      // Both controls decide what a run's numbers mean, and both were undescribed: the suite had
      // no hint at all, and the profile could only ever show the conditional fit advisory.
      const suiteHint = fixture.nativeElement.querySelector('#suiteHint') as HTMLElement | null;
      expect(suiteHint).toBeTruthy();
      expect(suiteHint!.textContent).toContain('only comparable within one suite');

      const profileHint = fixture.nativeElement.querySelector('#profileHint') as HTMLElement | null;
      expect(profileHint).toBeTruthy();
      expect(profileHint!.textContent).toContain('Turns the four dimension grades into indices');
    });

    it('should say what a second run buys rather than referring to previous behaviour', () => {
      component.activeSubTab = 'run';
      component.runLimits = {
        maxRunsPerHour: 4,
        maxRunsPerDay: 20,
        runsInLastHour: 0,
        runsInLast24Hours: 0,
        remainingDailyHeadroom: 20,
        maxRunCountPerSeries: 20
      };
      fixture.detectChanges();

      const hint = fixture.nativeElement.querySelector('#runCountHint') as HTMLElement | null;
      expect(hint).toBeTruthy();
      const text = (hint!.textContent ?? '').replace(/\s+/g, ' ').trim();

      expect(text).toContain('replicate set');
      expect(text).toContain('run-to-run noise');
      expect(text).toContain('20');

      // The regression this wording exists to prevent: "exactly as before" described the
      // pre-multi-run implementation, which tells an operator nothing about the field.
      expect(text).not.toContain('as before');
    });
  });

  describe('launcher info buttons and notes', () => {
    function card(): HTMLElement {
      return fixture.nativeElement.querySelector('.setup-card') as HTMLElement;
    }

    beforeEach(() => {
      component.activeSubTab = 'run';
    });

    it('should keep each field hint in a click-mode popup beside its control', () => {
      fixture.detectChanges();

      const hints: [string, string][] = [
        ['suiteHint', '#suiteSelect'],
        ['profileHint', '#profileSelect'],
        ['bmTestedModelHint', '.tested-model-selector'],
        ['bmAssessorModelHint', '.assessor-model-selector'],
        ['bmCoAssessorModelHint', '.co-assessor-model-selector'],
        ['runCountHint', '#runCountInput']
      ];
      for (const [id, controlSelector] of hints) {
        const hint = card().querySelector(`#${id}`) as HTMLElement;
        expect(hint).withContext(id).toBeTruthy();
        const popup = hint.closest('.gh-info-popup') as HTMLElement;
        expect(popup).withContext(id).toBeTruthy();
        expect(popup.getAttribute('popover')).withContext(id).toBe('auto');

        const tip = popup.closest('app-info-tip') as HTMLElement;
        const control = card().querySelector(controlSelector) as HTMLElement;
        expect(control.parentElement!.classList).withContext(id).toContain('gh-field-row');
        expect(control.nextElementSibling).withContext(id).toBe(tip);
      }
    });

    it('should show no compliance box and no fieldset purpose lines', () => {
      fixture.detectChanges();

      expect(card().querySelector('.compliance-purpose-box')).toBeNull();
      expect(card().textContent).not.toContain('Evaluation Purpose');
      expect(card().querySelector('.gh-fieldset-hint')).toBeNull();
    });

    // Each state is set before the run tab's first render: a second fixture.detectChanges()
    // does not refresh the launcher's conditional branches in this spec.
    it('should hide the Response Style note while Concise is selected', () => {
      component.candidateVerboseMode = false;
      fixture.detectChanges();

      expect(card().querySelector('#candidateResponseStyleNote')).toBeNull();
      expect(card().querySelector('#candidateResponseStyle')!.getAttribute('aria-describedby'))
        .toBe('candidateResponseStyleHint');
    });

    it('should show the Response Style note while Detailed is selected', () => {
      component.candidateVerboseMode = true;
      fixture.detectChanges();

      expect(card().querySelector('#candidateResponseStyleNote')?.textContent).toContain('Only Accuracy stays comparable');
      expect(card().querySelector('#candidateResponseStyle')!.getAttribute('aria-describedby'))
        .toBe('candidateResponseStyleHint candidateResponseStyleNote');
    });

    it('should offer Source Code References beside Response Style, Disallowed first and by default', () => {
      fixture.detectChanges();

      const select = card().querySelector('#candidateSourceCodeReferences') as HTMLSelectElement;
      expect(select).toBeTruthy();
      expect(select.closest('fieldset')).toBe(card().querySelector('#candidateResponseStyle')!.closest('fieldset'));
      expect((card().querySelector('label[for="candidateSourceCodeReferences"]')?.textContent || '').trim())
        .toBe('Source Code References');
      expect(Array.from(select.options).map(o => o.textContent?.trim())).toEqual([
        'Disallowed — production default',
        'Allowed — answers cite source files and lines'
      ]);
      expect(component.candidateAllowSourceCodeReferences).toBeFalse();
      expect(select.getAttribute('aria-describedby')).toBe('candidateSourceCodeReferencesHint');

      const hint = card().querySelector('#candidateSourceCodeReferencesHint') as HTMLElement;
      expect(hint.closest('.gh-info-popup')?.getAttribute('popover')).toBe('auto');
      expect(hint.textContent).toContain('Show source code references');
      expect(select.parentElement!.classList).toContain('gh-field-row');
    });

    it('should show the Second Opinion Mode reason while the mode is disabled', () => {
      component.secondOpinionConfigId = null;
      fixture.detectChanges();

      expect(card().querySelector('#secondOpinionModeHint')?.textContent).toContain('Choose a second reader to set its coverage');
      expect(card().querySelector('#secondOpinionModeSelect')!.getAttribute('aria-describedby'))
        .toBe('secondOpinionModeTip secondOpinionModeHint');
    });

    it('should hide the Second Opinion Mode reason while the mode is enabled', () => {
      component.secondOpinionConfigId = 1;
      fixture.detectChanges();

      expect(card().querySelector('#secondOpinionModeHint')).toBeNull();
      expect(card().querySelector('#secondOpinionModeSelect')!.getAttribute('aria-describedby'))
        .toBe('secondOpinionModeTip');
    });

    it('should show no same-model note without a second opinion', () => {
      component.assessorConfigId = 1;
      component.secondOpinionConfigId = null;
      fixture.detectChanges();

      expect(card().querySelector('#bmSecondOpinionSameModelNote')).toBeNull();
      expect(card().querySelector('.second-opinion-model-selector .selector-trigger')!.getAttribute('aria-describedby'))
        .toBe('bmSecondOpinionModelHint');
    });

    it('should show the same-model note when the second opinion is the assessor', () => {
      component.assessorConfigId = 1;
      component.secondOpinionConfigId = 1;
      fixture.detectChanges();

      expect(card().querySelector('#bmSecondOpinionSameModelNote')?.textContent)
        .toContain('Same model as the assessor');
      expect(card().querySelector('.second-opinion-model-selector .selector-trigger')!.getAttribute('aria-describedby'))
        .toBe('bmSecondOpinionModelHint bmSecondOpinionSameModelNote');
    });

    it('should list every coverage option in its popup', () => {
      fixture.detectChanges();

      const tip = card().querySelector('#secondOpinionModeTip') as HTMLElement;
      expect(tip.closest('.gh-info-popup')).toBeTruthy();
      expect(tip.closest('.gh-info-popup')?.querySelector('.gh-info-popup-title')?.textContent?.trim()).toBe('Coverage');
      expect(card().querySelector('label[for="secondOpinionModeSelect"]')?.textContent?.trim()).toBe('Coverage');
      const terms = Array.from(tip.querySelectorAll('dt .gh-info-term')).map(t => (t.textContent ?? '').trim());
      expect(terms).toEqual(component.secondOpinionModeOptions.map(o => o.label));

      const badges = Array.from(tip.querySelectorAll('dt .gh-info-badge'));
      expect(badges.length).toBe(1);
      expect(badges[0].closest('dt')?.querySelector('.gh-info-term')?.textContent?.trim())
        .toBe('Every answer (double grading)');
      expect(tip.textContent).toContain('No coverage setting changes a score');
    });

    it('should end each grader popup with a recommendation', () => {
      fixture.detectChanges();

      for (const tipId of ['bmAssessorModelHint', 'bmCoAssessorModelHint', 'bmSecondOpinionModelHint', 'bmClaimVerifierModelHint']) {
        const tip = card().querySelector(`#${tipId}`) as HTMLElement;
        expect(Array.from(tip.querySelectorAll('p > strong')).map(s => s.textContent))
          .withContext(tipId).toContain('Recommended:');
        expect(tip.textContent).withContext(tipId).toContain('More: How the graders work.');
      }
      const reader = card().querySelector('#bmSecondOpinionModelHint') as HTMLElement;
      expect(reader.textContent).toContain('A second model grades answers again');
      const verifier = card().querySelector('#bmClaimVerifierModelHint') as HTMLElement;
      expect(verifier.textContent).not.toContain('advisory, changes no score');
      expect(verifier.textContent).toContain('It never changes a score');
    });

    it('should open the grader guide at the roles overview from the Grading group', () => {
      fixture.detectChanges();

      const open = spyOn(component.graderGuide!, 'open');
      const button = card().querySelector('.setup-group-grading .grader-guide-btn') as HTMLButtonElement;
      expect(button.textContent?.trim()).toBe('How the graders work');
      button.click();
      expect(open).toHaveBeenCalledWith('roles');
      expect(component.graderGuideProfile).toBe(component.selectedScoringProfile ?? null);
    });

    it('should offer an optional Report Writer after the Claim Verifier, with its hint in a click-mode popup', () => {
      fixture.detectChanges();

      const grading = card().querySelector('.setup-group-grading') as HTMLElement;
      const labels = Array.from(grading.querySelectorAll(':scope > .form-group > label'))
        .map(label => (label.textContent ?? '').replace(/\s+/g, ' ').trim());
      expect(labels.slice(-2)).toEqual(['Claim Verifier Optional', 'Report Writer Optional']);

      const picker = grading.querySelector('.report-writer-model-selector') as HTMLElement;
      const trigger = picker.querySelector('.selector-trigger') as HTMLElement;
      expect(trigger.getAttribute('aria-labelledby')).toContain('bmReportWriterModelLabel');
      expect(trigger.getAttribute('aria-describedby')).toBe('bmReportWriterModelHint');
      expect(trigger.textContent).toContain('None — no AI-written reports');
      expect(component.reportWriterConfigId).toBeNull();

      const hint = card().querySelector('#bmReportWriterModelHint') as HTMLElement;
      expect(hint.closest('.gh-info-popup')?.getAttribute('popover')).toBe('auto');
      expect(picker.parentElement!.classList).toContain('gh-field-row');
      expect(picker.nextElementSibling).toBe(hint.closest('app-info-tip'));
      const text = (hint.textContent ?? '').replace(/\s+/g, ' ');
      expect(text).toContain('this model writes an Executive Summary and a Report for AI Researchers and Developers once');
      expect(text).toContain('Downloads render them without calling it again');
      expect(text).toContain('from another provider than the model under test');
    });

    it('should warn about a report writer of the candidate\'s provider before Start, without holding Start back', () => {
      component.systemConfigs = [
        component.systemConfigs[0],
        { ...component.systemConfigs[0], id: 2, displayName: 'Other Claude', modelId: 'claude-other' }
      ];
      component.testedConfigId = 1;
      component.assessorConfigId = 1;
      component.reportWriterConfigId = 2;
      fixture.detectChanges();

      const warning = 'Other Claude is from Anthropic, the provider of the model under test. ' +
        'Its reports may describe that model more favorably.';
      expect(component.reportWriterLaunchRefusal).toBe('');
      expect(component.reportWriterLaunchWarning).toBe(warning);
      const advisory = card().querySelector('.setup-group-grading .report-writer-advisory') as HTMLElement;
      expect(advisory.classList).toContain('alert-warning');
      expect(advisory.querySelector('svg.alert-icon')?.getAttribute('aria-hidden')).toBe('true');
      expect(advisory.textContent?.replace(/\s+/g, ' ').trim()).toBe(warning);
      expect(card().querySelector('.setup-group-grading .report-writer-refusal')).toBeNull();
      expect(component.canStartRun).toBeTrue();
      expect(component.startBenchmarkHint).toBe('');
    });

    it('should refuse the model under test as its own report writer in red and hold Start back', () => {
      component.testedConfigId = 1;
      component.assessorConfigId = 1;
      component.reportWriterConfigId = 1;
      fixture.detectChanges();

      const refusal = 'The model under test cannot write its own reports.';
      expect(component.reportWriterLaunchRefusal).toBe(refusal);
      expect(component.reportWriterLaunchWarning).toBe('');
      const line = card().querySelector('.setup-group-grading .report-writer-refusal') as HTMLElement;
      expect(line.classList).toContain('gh-field-error');
      expect(line.id).toBe('bmReportWriterRefusal');
      expect(line.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
      expect(line.textContent?.trim()).toBe(refusal);
      expect(card().querySelector('.setup-group-grading .report-writer-advisory')).toBeNull();
      const trigger = card().querySelector('.report-writer-model-selector .selector-trigger') as HTMLElement;
      expect(trigger.getAttribute('aria-describedby')).toBe('bmReportWriterModelHint bmReportWriterRefusal');
      expect(component.canStartRun).toBeFalse();
      expect(component.startBenchmarkHint).toBe(refusal);

      component.reportWriterConfigId = null;
      expect(component.reportWriterLaunchRefusal).toBe('');
    });
  });

  describe('same-provider acknowledgments at launch', () => {
    const writerWarning = {
      sameProvider: true, provider: 'Anthropic', testedModelDisplayName: 'Test Model',
      assessorModelDisplayName: 'Other Claude', message: 'The report writer shares the provider.', role: 'reportWriter'
    };
    const assessorWarning = {
      sameProvider: true, provider: 'Anthropic', testedModelDisplayName: 'Test Model',
      assessorModelDisplayName: 'Test Model', message: 'The assessor shares the provider.', role: 'assessor'
    };

    function prepare(): jasmine.Spy {
      component.activeSubTab = 'run';
      fixture.detectChanges();
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 1;
      spyOn(component.runProgressDialog.nativeElement, 'showModal');
      benchmarkServiceMock.getRun.and.returnValue(of({ id: 42, answers: [] } as any));
      return spyOn(component.sameProviderDialog.nativeElement, 'showModal');
    }

    function dialogHeading(): string {
      return component.sameProviderDialog.nativeElement.querySelector('h3')?.textContent?.trim() ?? '';
    }

    function dialogText(): string {
      return (component.sameProviderDialog.nativeElement.textContent ?? '').replace(/\s+/g, ' ');
    }

    function sentBodies(): any[] {
      return benchmarkServiceMock.startRun.calls.allArgs().map(args => args[0]);
    }

    afterEach(() => component.ngOnDestroy());

    it('should open the dialog in report-writer wording on the writer\'s 409 and re-send with the writer\'s flag', () => {
      const showModal = prepare();
      benchmarkServiceMock.startRun.and.returnValues(
        throwError(() => ({ status: 409, error: writerWarning })),
        of({ runId: 42 })
      );

      component.startBenchmark();
      fixture.detectChanges();

      expect(showModal).toHaveBeenCalledTimes(1);
      expect(component.sameProviderWarningIsReportWriter).toBeTrue();
      expect(dialogHeading()).toBe('Same-Provider Report Writer');
      expect(dialogText()).toContain('Report Writer: Other Claude');
      expect(dialogText()).not.toContain('Assessor Model:');
      expect(dialogText()).toContain('The run\'s AI-written reports would be written by a model from the same provider ' +
        'as the model under test, which may describe it more favorably.');

      const confirm = (Array.from(component.sameProviderDialog.nativeElement.querySelectorAll('button')) as HTMLButtonElement[])
        .find(button => (button.textContent ?? '').includes('Acknowledge & Start Run'))!;
      confirm.click();

      const bodies = sentBodies();
      expect(bodies.length).toBe(2);
      expect(bodies[0].acknowledgeSameProvider).toBeFalse();
      expect(bodies[0].acknowledgeSameProviderReportWriter).toBeUndefined();
      expect(bodies[1].acknowledgeSameProvider).toBeFalse();
      expect(bodies[1].acknowledgeSameProviderReportWriter).toBeTrue();
      // Per-run safety acknowledgments: neither is remembered with the launcher's settings.
      expect(localStorage.getItem(RUN_SETTINGS_KEY) ?? '').not.toContain('acknowledge');
    });

    it('should keep the assessor\'s acknowledgment when the report writer\'s warning follows it', () => {
      const showModal = prepare();
      benchmarkServiceMock.startRun.and.returnValues(
        throwError(() => ({ status: 409, error: assessorWarning })),
        throwError(() => ({ status: 409, error: writerWarning })),
        of({ runId: 42 })
      );

      component.startBenchmark();
      fixture.detectChanges();
      expect(component.sameProviderWarningIsReportWriter).toBeFalse();
      expect(dialogHeading()).toBe('Same-Provider Assessment Warning');
      expect(dialogText()).toContain('Assessor Model: Test Model');

      component.confirmSameProviderRun();
      fixture.detectChanges();
      expect(component.sameProviderWarningIsReportWriter).toBeTrue();
      expect(dialogHeading()).toBe('Same-Provider Report Writer');

      component.confirmSameProviderRun();

      const bodies = sentBodies();
      expect(bodies.length).toBe(3);
      expect(bodies[1].acknowledgeSameProvider).toBeTrue();
      expect(bodies[1].acknowledgeSameProviderReportWriter).toBeUndefined();
      expect(bodies[2].acknowledgeSameProvider).toBeTrue();
      expect(bodies[2].acknowledgeSameProviderReportWriter).toBeTrue();
      expect(showModal).toHaveBeenCalled();
      expect(localStorage.getItem(RUN_SETTINGS_KEY) ?? '').not.toContain('acknowledge');
    });

    it('should start a new attempt without the acknowledgments of the last one', () => {
      prepare();
      benchmarkServiceMock.startRun.and.returnValues(
        throwError(() => ({ status: 409, error: writerWarning })),
        throwError(() => ({ status: 500, error: 'Boom' })),
        throwError(() => ({ status: 500, error: 'Boom' }))
      );

      component.startBenchmark();
      component.confirmSameProviderRun();
      component.closeSameProviderDialog();
      component.startBenchmark();

      const bodies = sentBodies();
      expect(bodies.length).toBe(3);
      expect(bodies[1].acknowledgeSameProviderReportWriter).toBeTrue();
      expect(bodies[2].acknowledgeSameProvider).toBeFalse();
      expect(bodies[2].acknowledgeSameProviderReportWriter).toBeUndefined();
    });
  });

  describe('scoring profile fit advisory', () => {
    /** Re-points the Model Under Test at a config carrying the given thinking level. */
    function selectTestedModelWithThinkingLevel(level: string | null): void {
      component.systemConfigs = [{ ...component.systemConfigs[0], thinkingLevel: level }];
      component.testedConfigId = component.systemConfigs[0].id;
    }

    /**
     * The fit advisory specifically, by its own id — not the first .form-hint in the profile's
     * .form-group, which is the permanent description of what a scoring profile is.
     */
    function profileFitHintText(): string {
      const hint = fixture.nativeElement.querySelector('#profileFitHint') as HTMLElement | null;
      return (hint?.textContent ?? '').replace(/\s+/g, ' ').trim();
    }

    beforeEach(() => {
      component.activeSubTab = 'run';
    });

    it('should warn when a deliberating model is graded against an interactive latency profile', () => {
      // The default profile targets 2000 ms, which is well inside the interactive band.
      for (const level of ['high', 'max', 'Max', 'HIGH']) {
        selectTestedModelWithThinkingLevel(level);
        expect(component.showProfileFitAdvisory).withContext(level).toBeTrue();
      }

      fixture.detectChanges();
      expect(profileFitHintText()).toContain('This profile targets interactive latency.');
      expect(profileFitHintText()).toContain('consider a Reasoning Agent profile');
    });

    it('should stay silent for a shallow thinking level or a profile with a slow speed target', () => {
      selectTestedModelWithThinkingLevel('low');
      expect(component.showProfileFitAdvisory).toBeFalse();

      selectTestedModelWithThinkingLevel('max');
      component.scoringProfiles = [{ ...component.scoringProfiles[0], speedTargetMs: 30000 }];
      expect(component.showProfileFitAdvisory).toBeFalse();

      fixture.detectChanges();
      expect(profileFitHintText()).toBe('');
    });

    it('should stay silent while either half of the pairing is unselected', () => {
      component.testedConfigId = null;
      component.selectedScoringProfileId = null;
      expect(component.showProfileFitAdvisory).toBeFalse();

      // A model chosen, but no profile yet.
      selectTestedModelWithThinkingLevel('max');
      component.selectedScoringProfileId = null;
      expect(component.showProfileFitAdvisory).toBeFalse();

      // A profile chosen, but no model yet.
      component.selectedScoringProfileId = 1;
      component.testedConfigId = null;
      expect(component.showProfileFitAdvisory).toBeFalse();

      // A model with no thinking level at all is not a deliberating one.
      selectTestedModelWithThinkingLevel(null);
      expect(component.showProfileFitAdvisory).toBeFalse();
    });
  });
  describe('second opinion mode', () => {
    /**
     * NgModel treats `disabled` as one of its own inputs and applies it through the form control
     * in a microtask, so the DOM property is not settled by the end of detectChanges. This must
     * be called inside fakeAsync, and `tick()` is what makes an assertion about it mean anything:
     * `whenStable()` never resolves here, because the component holds polling intervals.
     */
    function modeSelect(): HTMLSelectElement | null {
      component.activeSubTab = 'run';
      fixture.detectChanges();
      tick();
      fixture.detectChanges();
      return fixture.nativeElement.querySelector('#secondOpinionModeSelect') as HTMLSelectElement | null;
    }

    it('should offer the five modes in coverage order', fakeAsync(() => {
      const select = modeSelect();
      expect(select).toBeTruthy();

      // FlaggedPlusSample sits between the outlier mode and All because that is where it falls on
      // coverage: more than flagged-plus-outliers, less than every answer.
      const labels = Array.from(select!.querySelectorAll('option')).map(o => (o.textContent || '').trim());
      expect(labels).toEqual([
        'Never',
        'Only flagged answers',
        'Flagged answers and statistical outliers',
        'Flagged answers plus a sample',
        'Every answer (double grading)'
      ]);
      discardPeriodicTasks();
    }));

    it('should be disabled, with a reason, until a second opinion assessor is chosen', fakeAsync(() => {
      component.secondOpinionConfigId = null;
      const select = modeSelect();

      // The hard gate that silently produced the 2026-09-03 run's zero second verdicts: the
      // mode is inert without an assessor, so the control says so rather than looking set.
      expect(select!.disabled).toBeTrue();
      expect(component.secondOpinionModeHint).toContain('Choose a second reader to set its coverage');
      discardPeriodicTasks();
    }));

    it('should enable and describe the selected mode once an assessor is chosen', fakeAsync(() => {
      component.secondOpinionConfigId = 1;
      component.secondOpinionMode = 3;
      const select = modeSelect();

      expect(select!.disabled).toBeFalse();
      expect(component.secondOpinionModeHint).toContain('unbiased measure of grading reliability');
      discardPeriodicTasks();
    }));

    it('should default from the selected profile and be overridable for one run', () => {
      component.scoringProfiles = [{ ...component.scoringProfiles[0], secondOpinionMode: 2 }];
      component.selectedScoringProfileId = 1;
      expect(component.secondOpinionMode).toBe(2);

      component.secondOpinionMode = 3;
      expect(component.secondOpinionMode).toBe(3);
    });

    it('should send the mode only when an assessor is selected', fakeAsync(() => {
      const consoleError = spyOn(console, 'error');
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 2;
      component.secondOpinionConfigId = null;
      benchmarkServiceMock.startRun.and.returnValue(of({ runId: 7 }));
      benchmarkServiceMock.getRun.and.returnValue(throwError(() => ({ status: 0 })));
      spyOn(component.runProgressDialog.nativeElement, 'showModal');

      component.startBenchmark();
      expect(benchmarkServiceMock.startRun.calls.mostRecent().args[0].secondOpinionMode).toBeNull();

      component.secondOpinionConfigId = 3;
      component.secondOpinionMode = 3;
      component.startBenchmark();
      expect(benchmarkServiceMock.startRun.calls.mostRecent().args[0].secondOpinionMode).toBe(3);
      expect(consoleError).toHaveBeenCalledWith('Failed to poll run detail', jasmine.any(Object));

      (component as any).stopPolling();
      discardPeriodicTasks();
    }));

    it('should enable the profile editor outlier delta for FlaggedAndOutliers only', () => {
      component.profileForm.secondOpinionMode = 1;
      expect(component.outlierDeltaEnabled).toBeFalse();

      component.profileForm.secondOpinionMode = 3;
      expect(component.outlierDeltaEnabled).toBeFalse();

      component.profileForm.secondOpinionMode = 2;
      expect(component.outlierDeltaEnabled).toBeTrue();
    });

    it('should reject a non-positive outlier delta under FlaggedAndOutliers', () => {
      component.editingProfileId = null;
      component.profileForm = {
        ...component.profileForm,
        name: 'Outlier Profile',
        secondOpinionMode: 2,
        secondOpinionOutlierDeltaPoints: 0
      };

      component.saveProfile();

      expect(benchmarkServiceMock.createScoringProfile).not.toHaveBeenCalled();
      expect(component.profileValidationErrors.join(' ')).toContain('Outlier delta must be between 1 and 100');
    });
  });

  describe('assessor advisories', () => {
    it('should warn when the assessor and the second opinion share a provider', () => {
      component.systemConfigs = [
        { id: 1, displayName: 'Gemini Flash', modelId: 'gemini-3.7-flash', provider: 'Google', modelRole: 4, hasApiKey: true, isEnabled: true } as any,
        { id: 2, displayName: 'Gemini Pro', modelId: 'gemini-3.7-pro', provider: 'Google', modelRole: 4, hasApiKey: true, isEnabled: true } as any,
        { id: 3, displayName: 'Claude Opus 5', modelId: 'claude-opus-5', provider: 'Anthropic', modelRole: 4, hasApiKey: true, isEnabled: true } as any
      ];
      component.assessorConfigId = 1;
      component.secondOpinionConfigId = 2;
      expect(component.showAssessorPairingAdvisory).toBeTrue();

      component.secondOpinionConfigId = 3;
      expect(component.showAssessorPairingAdvisory).toBeFalse();
    });

    it('should warn when the assessor differs from the suite\'s last completed run', () => {
      component.assessorConfigId = 5;
      component.lastAssessor = {
        runId: 7,
        assessorModelConfigurationId: 2,
        assessorModelDisplayNameUsed: 'Gemini 3.7 Flash',
        assessorModelProviderUsed: 'Google'
      };
      expect(component.showAssessorChangeAdvisory).toBeTrue();

      component.assessorConfigId = 2;
      expect(component.showAssessorChangeAdvisory).toBeFalse();
    });

    it('should stay silent for a suite with no completed run to compare against', () => {
      component.assessorConfigId = 5;
      component.lastAssessor = {};
      expect(component.showAssessorChangeAdvisory).toBeFalse();
    });
  });

  describe('results screen: agreement, weighting and profile fit', () => {
    function buildFinishedRun(overrides: any = {}): any {
      return {
        id: 77,
        benchmarkSuiteId: 1,
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'GPT-5.6 Luna',
        testedModelProviderUsed: 'OpenAI',
        testedModelIdUsed: 'gpt-5.6-luna',
        testedModelThinkingLevelUsed: 'max',
        testedModelParallelExecutionModeUsed: 0,
        assessorModelDisplayNameUsed: 'Gemini 3.7 Flash',
        assessorModelProviderUsed: 'Google',
        assessorModelIdUsed: 'gemini-3.7-flash',
        status: 'Completed',
        startedAtUtc: '2026-09-03T06:52:00Z',
        completedAtUtc: '2026-09-03T07:28:00Z',
        qualityIndex: 94,
        unweightedQualityIndex: 92,
        speedIndex: 67,
        totalAnswerDurationMs: 900000,
        totalDurationMs: 900000,
        scoringProfileId: 1,
        scoringProfileName: 'Standard Intelligence Index (Default)',
        scoringProfileSpeedTargetMs: 15000,
        scoringMethodVersion: 6,
        harnessVersion: '7',
        transportDefectAnswerCount: 0,
        advisoryFlagAnswerCount: 0,
        scrubbedArtifactAnswerCount: 0,
        difficultyFallbackUsed: false,
        speedMeasurementDegraded: false,
        maxParallelQuestionsUsed: 1,
        answeredQuestionCount: 18,
        totalQuestionCount: 18,
        assessmentParseFailed: false,
        totalInputTokens: 0,
        totalOutputTokens: 0,
        totalCacheReadTokens: 0,
        totalCacheCreationTokens: 0,
        errorMessage: null,
        answers: [],
        ...overrides
      };
    }

    function scoreCardText(label: string): string {
      const cards: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.score-card'));
      const card = cards.find(c => (c.querySelector('.score-label')?.textContent || '').trim() === label);
      return (card?.textContent || '').replace(/\s+/g, ' ').trim();
    }

    it('should show the unweighted mean beside the weighted index, with the weighting delta', () => {
      component.selectedRunDetail = buildFinishedRun();
      fixture.detectChanges();

      expect(component.showUnweightedQualityTile).toBeTrue();
      expect(component.weightingDeltaLabel).toBe('+2');
      expect(scoreCardText('Unweighted Mean')).toContain('92 / 100');
      expect(scoreCardText('Unweighted Mean')).toContain('equal weights · difficulty weighting moved the index +2');
    });

    it('should omit the unweighted tile when the two aggregations agree', () => {
      component.selectedRunDetail = buildFinishedRun({ qualityIndex: 92, unweightedQualityIndex: 92 });
      fixture.detectChanges();

      expect(component.showUnweightedQualityTile).toBeFalse();
      expect(scoreCardText('Unweighted Mean')).toBe('');
    });

    it('should mark the Speed Index advisory for a deliberating candidate on an interactive profile', () => {
      component.selectedRunDetail = buildFinishedRun();
      fixture.detectChanges();

      expect(component.showRunProfileFitAdvisory).toBeTrue();
      expect(scoreCardText('Speed Index')).toContain('*');
      expect(component.runProfileFitAdvisoryTitle).toContain('thinking level max');
    });

    it('should not mark it advisory against a profile that is not an interactive one', () => {
      component.selectedRunDetail = buildFinishedRun({ scoringProfileSpeedTargetMs: 30000 });
      fixture.detectChanges();

      expect(component.showRunProfileFitAdvisory).toBeFalse();
    });

    it('should show the agreement tile with its coverage fraction beneath the value', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 1,
        secondOpinionGradedAnswerCount: 4,
        secondOpinionMeanAbsDelta: 4.25,
        secondOpinionDisagreementCount: 2
      });
      fixture.detectChanges();

      // The coverage never travels separately from the figure: 4 of 18 selected by trigger and
      // 18 of 18 are different measurements, and only the fraction tells them apart.
      expect(component.agreementCoverageLabel).toBe('4 of 18 answers graded twice');
      expect(component.agreementIsSelective).toBeTrue();
      const text = scoreCardText('Assessor Agreement');
      expect(text).toContain('mean |Δ| 4.3 pts');
      expect(text).toContain('4 of 18 answers graded twice · Flagged only');
    });

    it('should name the signed mean after the absolute one', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 3,
        secondOpinionGradedAnswerCount: 18,
        secondOpinionMeanAbsDelta: 3.1,
        secondOpinionMeanSignedDelta: -0.4
      });
      fixture.detectChanges();

      expect(scoreCardText('Assessor Agreement')).toContain('mean |Δ| 3.1 pts · signed −0.4');
    });

    it('should give the trigger-selected cause alone when the sample is large enough', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 1,
        secondOpinionGradedAnswerCount: 6,
        secondOpinionMeanAbsDelta: 2.0
      });
      fixture.detectChanges();

      expect(component.showAgreementAdvisory).toBeTrue();
      expect(component.agreementAdvisoryTitle)
        .toBe('Coverage is selected by trigger, so this is conditioned on the first assessor’s own uncertainty, not an unbiased agreement rate. n = 6 of 18.');
    });

    it('should give the small-sample cause alone for a small Every answer sample', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 3,
        secondOpinionGradedAnswerCount: 3,
        secondOpinionMeanAbsDelta: 2.0
      });
      fixture.detectChanges();

      expect(component.showAgreementAdvisory).toBeTrue();
      expect(component.agreementAdvisoryTitle)
        .toBe('Only n = 3 of 18 answers were graded twice, too few for a mean to be an agreement rate.');
    });

    it('should give both causes when coverage is selective and the sample small', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 1,
        secondOpinionGradedAnswerCount: 2,
        secondOpinionMeanAbsDelta: 2.0
      });
      fixture.detectChanges();

      const title = component.agreementAdvisoryTitle;
      expect(title).toContain('Coverage is selected by trigger');
      expect(title).toContain('Only n = 2 of 18 answers were graded twice');
    });

    it('should drop the selective caveat when every answer was graded twice', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 3,
        secondOpinionGradedAnswerCount: 18,
        secondOpinionMeanAbsDelta: 3.0
      });
      fixture.detectChanges();

      expect(component.agreementCoverageLabel).toBe('18 of 18 answers graded twice');
      expect(component.agreementIsSelective).toBeFalse();
      expect(scoreCardText('Assessor Agreement')).toContain('Every answer');
    });

    it('should label FlaggedPlusSample rather than falling through to Manual only', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 4,
        secondOpinionGradedAnswerCount: 6,
        secondOpinionMeanAbsDelta: 2.5
      });
      fixture.detectChanges();

      expect(component.agreementModeLabel).toBe('Flagged plus sample');
      const text = scoreCardText('Assessor Agreement');
      expect(text).toContain('Flagged plus sample');
      expect(text).not.toContain('Manual only');
    });

    it('should hide the agreement tile when nothing was graded twice', () => {
      component.selectedRunDetail = buildFinishedRun({ secondOpinionGradedAnswerCount: 0 });
      fixture.detectChanges();

      expect(component.showAgreementTile).toBeFalse();
      expect(scoreCardText('Assessor Agreement')).toBe('');
    });

    describe('key figures: notes, Critical Errors and Answered', () => {
      function answer(orderIndex: number, overrides: any = {}): any {
        return {
          id: orderIndex, orderIndex, questionText: `Q${orderIndex}`, difficulty: 1, answerText: 'a',
          status: 'Ok', assessmentStatus: 'Scored', durationMs: 1000, modelTimeMs: 1000,
          scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 80,
          ...overrides
        };
      }

      function figureCard(key: string): HTMLElement | null {
        return fixture.nativeElement.querySelector(`.score-card[data-figure="${key}"]`);
      }

      function figureNotes(key: string): string[] {
        return Array.from(figureCard(key)?.querySelectorAll('.score-note') ?? [])
          .map(n => (n.textContent || '').replace(/\s+/g, ' ').trim());
      }

      function figureValue(key: string): string {
        return (figureCard(key)?.querySelector('.score-subvalue')?.textContent || '').replace(/\s+/g, ' ').trim();
      }

      it('should say the raw index is before the caps', () => {
        component.selectedRunDetail = buildFinishedRun({ rawQualityIndex: 97 });
        fixture.detectChanges();

        expect(figureNotes('raw-quality')).toEqual(['before critical-error caps']);
      });

      it('should say the holistic score is not an index on a single-assessor run', () => {
        component.selectedRunDetail = buildFinishedRun({ finalScore: 90 });
        fixture.detectChanges();

        expect(figureNotes('holistic')).toEqual(['assessor\'s whole-run judgment, not an index']);
      });

      it('should mark the holistic fallback on a run with no per-question index', () => {
        component.selectedRunDetail = buildFinishedRun({ qualityIndex: null, unweightedQualityIndex: null, finalScore: 81 });
        fixture.detectChanges();

        const card = figureCard('intelligence')!;
        expect(card.querySelector('.score-value')?.textContent).toContain('81 / 100');
        expect(figureNotes('intelligence')).toContain('holistic score — this run has no per-question index');
      });

      it('should not mark the fallback when the index exists', () => {
        component.selectedRunDetail = buildFinishedRun({ finalScore: 81 });
        fixture.detectChanges();

        expect(figureNotes('intelligence')).not.toContain('holistic score — this run has no per-question index');
      });

      it('should count critical errors and name their questions', () => {
        component.selectedRunDetail = buildFinishedRun({
          answers: [answer(7, { criticalError: true }), answer(3, { criticalError: true }), answer(5)]
        });
        fixture.detectChanges();

        expect(component.keyFigureCriticalErrorAnswers.map(a => a.orderIndex)).toEqual([3, 7]);
        expect(figureValue('critical-errors')).toBe('2');
        expect(figureNotes('critical-errors')).toEqual(['Q3, Q7']);
      });

      it('should read None with no critical error', () => {
        component.selectedRunDetail = buildFinishedRun({ answers: [answer(1), answer(2)] });
        fixture.detectChanges();

        expect(figureValue('critical-errors')).toBe('None');
        expect(figureNotes('critical-errors')).toEqual([]);
      });

      it('should count member B\'s critical errors on a panel run, as the report does', () => {
        component.selectedRunDetail = buildFinishedRun({
          isPanelRun: true,
          answers: [answer(1, { criticalError: true }), answer(2, { coAssessmentCriticalError: true }), answer(3)]
        });
        fixture.detectChanges();

        expect(component.keyFigureCriticalErrorAnswers.length).toBe(2);
        expect(figureValue('critical-errors')).toBe('2');
        expect(figureNotes('critical-errors')).toEqual(['Q1, Q2']);
      });

      it('should ignore member B\'s flag outside a panel run', () => {
        component.selectedRunDetail = buildFinishedRun({
          answers: [answer(1, { coAssessmentCriticalError: true })]
        });
        fixture.detectChanges();

        expect(figureValue('critical-errors')).toBe('None');
      });

      it('should say how many critical errors the second reader disputed', () => {
        component.selectedRunDetail = buildFinishedRun({
          answers: [
            answer(1, { criticalError: true, secondOpinionCriticalError: false }),
            answer(2, { criticalError: true, secondOpinionCriticalError: true })
          ]
        });
        fixture.detectChanges();

        expect(figureNotes('critical-errors')).toEqual(['Q1, Q2', '1 disputed by the second reader']);
      });

      it('should name the reference reader on a panel run', () => {
        component.selectedRunDetail = buildFinishedRun({
          isPanelRun: true,
          answers: [answer(1, { criticalError: true, secondOpinionCriticalError: false })]
        });
        fixture.detectChanges();

        expect(figureNotes('critical-errors')).toEqual(['Q1', '1 disputed by the reference reader']);
      });

      it('should hide the Critical Errors card on a run with no answer rows', () => {
        component.selectedRunDetail = buildFinishedRun({ answers: [] });
        fixture.detectChanges();

        expect(figureCard('critical-errors')).toBeNull();
        expect(component.shownKeyFigureKeys).not.toContain('critical-errors');
        expect(component.shownKeyFigureKeys).toContain('answered');
      });

      it('should show every question answered', () => {
        component.selectedRunDetail = buildFinishedRun();
        fixture.detectChanges();

        expect(figureValue('answered')).toBe('18 / 18');
        expect(figureNotes('answered')).toEqual(['every question answered']);
      });

      it('should give one clause per cause, adding up to the shortfall', () => {
        const answers = Array.from({ length: 17 }, (_, i) => answer(i + 1));
        component.selectedRunDetail = buildFinishedRun({
          answeredQuestionCount: 12,
          totalQuestionCount: 18,
          unansweredQuestionCount: 2,
          terminalFailureAnswerCount: 1,
          answers
        });
        fixture.detectChanges();

        expect(figureValue('answered')).toBe('12 / 18');
        const note = component.answeredNote;
        expect(note).toBe('2 without text · 1 failed at the provider · 1 never asked · 2 other errors');
        const sum = note.split(' · ').reduce((total, clause) => total + parseInt(clause, 10), 0);
        expect(sum).toBe(18 - 12);
      });

      it('should use the singular for one other error', () => {
        component.selectedRunDetail = buildFinishedRun({
          answeredQuestionCount: 17,
          totalQuestionCount: 18,
          unansweredQuestionCount: 0,
          answers: Array.from({ length: 18 }, (_, i) => answer(i + 1))
        });

        expect(component.answeredNote).toBe('1 other error');
      });

      it('should cap the clauses at the shortfall', () => {
        component.selectedRunDetail = buildFinishedRun({
          answeredQuestionCount: 16,
          totalQuestionCount: 18,
          unansweredQuestionCount: 2,
          terminalFailureAnswerCount: 3,
          answers: Array.from({ length: 18 }, (_, i) => answer(i + 1))
        });

        expect(component.answeredNote).toBe('2 without text');
      });

      it('should not report never-asked questions from an empty answer list', () => {
        component.selectedRunDetail = buildFinishedRun({
          answeredQuestionCount: 16,
          totalQuestionCount: 18,
          unansweredQuestionCount: 0,
          answers: []
        });

        expect(component.answeredNote).toBe('2 other errors');
      });

      it('should place the two new cards after the quality cards', () => {
        component.selectedRunDetail = buildFinishedRun({ answers: [answer(1)] });
        fixture.detectChanges();

        const keys: string[] = Array.from(fixture.nativeElement.querySelectorAll('.rr-figures .score-card'))
          .map(c => (c as HTMLElement).getAttribute('data-figure') ?? '');
        expect(keys.indexOf('critical-errors')).toBe(keys.indexOf('unweighted-mean') + 1);
        expect(keys.indexOf('answered')).toBe(keys.indexOf('critical-errors') + 1);
        expect(keys.indexOf('speed')).toBe(keys.indexOf('answered') + 1);
        expect(component.shownKeyFigureKeys as string[]).toEqual(keys);
      });
    });

    it('should name the questions on the tool-budget line and report contested and re-assessed answers', () => {
      component.selectedRunDetail = buildFinishedRun({
        toolStarvedAnswerCount: 1,
        contestedVerdictAnswerCount: 2,
        reassessedAnswerCount: 1,
        answers: [
          { id: 1, orderIndex: 10, questionText: 'Q10', difficulty: 1, answerText: 'a', status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1, scrubbedArtifactCount: 0, answerFlags: 32, answerFlagNames: ['ContestedVerdict'], toolBudgetExhausted: true, qualityScore: 60 },
          { id: 2, orderIndex: 11, questionText: 'Q11', difficulty: 1, answerText: 'b', status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1, scrubbedArtifactCount: 0, answerFlags: 32, answerFlagNames: ['ContestedVerdict'], qualityScore: 84, reassessmentCount: 1, previousQualityScore: 60, reassessedByModelDisplayNameUsed: 'Claude Opus 5' }
        ]
      });
      fixture.detectChanges();

      expect(component.toolBudgetQuestionNumbers).toBe('10');
      expect(component.contestedVerdictQuestionNumbers).toBe('10, 11');
      expect(component.reassessedQuestionNumbers).toBe('11');

      const notices: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.alert-heading'));
      const heading = notices.find(n => (n.textContent || '').includes('Run Integrity Notice'));
      const body = ((heading?.parentElement?.querySelector('.alert-body') as HTMLElement)?.textContent || '')
        .replace(/\s+/g, ' ').trim();

      expect(body).toContain('harness limit (tool budget) (question(s) 10)');
      expect(body).toContain('2 contested verdict(s)');
      expect(body).toContain('1 answer(s) were re-assessed after the run finished');
      expect(body).toContain('The Speed Index is advisory for this run');
    });

    it('should list the assessor\'s unverified claims on the answer that carried them', () => {
      const answer: any = {
        id: 1, orderIndex: 1, questionText: 'Q1', difficulty: 1, answerText: 'a',
        status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1,
        scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 60,
        unverifiedClaimCount: 2,
        unverifiedClaimsJson: '["gnomes gain infravision","orcs gain poison resistance"]'
      };
      expect(component.unverifiedClaimsOf(answer).length).toBe(2);
      // A malformed blob costs the panel, never the screen.
      expect(component.unverifiedClaimsOf({ ...answer, unverifiedClaimsJson: '{oops' }).length).toBe(0);
      expect(component.unverifiedClaimTotal).toBe(0);

      component.selectedRunDetail = buildFinishedRun({ answers: [answer] });
      expect(component.unverifiedClaimTotal).toBe(2);
    });

    it('should render the zero-coverage clause in the integrity notice when second opinion was selected but unused', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionAssessorModelConfigurationId: 4,
        secondOpinionAssessorModelDisplayNameUsed: 'Claude Opus 5',
        secondOpinionGradedAnswerCount: 0
      });
      fixture.detectChanges();

      expect(component.secondOpinionSelectedButUnused).toBeTrue();
      const notices: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.alert-heading'));
      const heading = notices.find(n => (n.textContent || '').includes('Run Integrity Notice'));
      const body = ((heading?.parentElement?.querySelector('.alert-body') as HTMLElement)?.textContent || '')
        .replace(/\s+/g, ' ').trim();

      expect(body).toContain('A second reader was selected but no answer met a trigger');
      expect(body).toContain('grader agreement is not measured for this run');
    });

    it('should render claim verification rows and colour only Refuted verdicts', () => {
      const answer: any = {
        id: 1, orderIndex: 1, questionText: 'Q1', difficulty: 1, answerText: 'a',
        status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1,
        scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 80,
        claimVerificationJson: JSON.stringify([
          { claimIndex: 0, claim: 'Claim 1', verdict: 'Supported', citation: 'src/a.c', basis: 'Valid.' },
          { claimIndex: 1, claim: 'Claim 2', verdict: 'Refuted', citation: 'src/b.c', basis: 'Invalid.' },
          { claimIndex: 2, claim: 'Claim 3', verdict: 'Indeterminate', citation: null, basis: 'Unknown.' }
        ])
      };

      component.selectedRunDetail = buildFinishedRun({ answers: [answer] });
      component.expandedQuestions.add(1);
      fixture.detectChanges();

      const verifications = component.claimVerificationsOf(answer);
      expect(verifications.length).toBe(3);

      const box = fixture.nativeElement.querySelector('.claim-verification-box');
      expect(box).toBeTruthy();

      const pills: HTMLElement[] = Array.from(box.querySelectorAll('.verdict-pill'));
      expect(pills.length).toBe(3);
      expect(pills[0].classList.contains('verdict-refuted')).toBeFalse();
      expect(pills[1].classList.contains('verdict-refuted')).toBeTrue();
      expect(pills[2].classList.contains('verdict-refuted')).toBeFalse();
    });

    function renderClaimVerifications(claimVerificationJson: string): HTMLElement {
      const answer: any = {
        id: 1, orderIndex: 1, questionText: 'Q1', difficulty: 1, answerText: 'a',
        status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1,
        scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 80,
        claimVerificationJson
      };
      component.selectedRunDetail = buildFinishedRun({ answers: [answer] });
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('.question-card-header') as HTMLElement).click();
      fixture.detectChanges();
      return fixture.nativeElement.querySelector('.claim-verification-box') as HTMLElement;
    }

    it('should label the entries the harness sent to check the assessor', () => {
      const box = renderClaimVerifications(JSON.stringify([
        { claimIndex: 0, claim: 'Quoted as critical', verdict: 'Supported', citation: 'src/a.c', basis: 'True.', roles: ['criticalErrorQuote'] },
        { claimIndex: 1, claim: 'Charged as false', verdict: 'Supported', citation: 'src/b.c', basis: 'True.', roles: ['accusedQuote'] },
        { claimIndex: 2, claim: 'Unverified and charged', verdict: 'Refuted', citation: 'src/c.c', basis: 'False.', roles: ['unverifiedClaim', 'accusedQuote'] },
        { claimIndex: 3, claim: 'Own claim', verdict: 'Indeterminate', citation: null, basis: 'Unknown.', roles: ['unverifiedClaim'] }
      ]));

      expect(box).toBeTruthy();
      const items: HTMLElement[] = Array.from(box.querySelectorAll('.claim-verification-item'));
      expect(items.length).toBe(4);
      const labelsOf = (item: HTMLElement) =>
        Array.from(item.querySelectorAll('.claim-role-label')).map(l => (l.textContent || '').trim());
      expect(labelsOf(items[0])).toEqual(['(critical-error quote)']);
      expect(labelsOf(items[1])).toEqual(['(sentence the assessor charged as false)']);
      expect(labelsOf(items[2])).toEqual(['(sentence the assessor charged as false)']);
      expect(labelsOf(items[3])).toEqual([]);
      expect(box.querySelector('.claim-roles-note')).toBeTruthy();
    });

    it('should name the panel member who submitted each checked entry', () => {
      const box = renderClaimVerifications(JSON.stringify([
        { claimIndex: 0, claim: 'Quoted as critical', verdict: 'Supported', citation: 'src/a.c', basis: 'True.', roles: ['criticalErrorQuote'], raisedBy: ['A'] },
        { claimIndex: 1, claim: 'Charged as false', verdict: 'Supported', citation: 'src/b.c', basis: 'True.', roles: ['accusedQuote'], raisedBy: ['B'] },
        { claimIndex: 2, claim: 'Basis', verdict: 'Refuted', citation: 'src/c.c', basis: 'False.', roles: ['outOfRubricBasis'], raisedBy: ['A', 'B'] },
        { claimIndex: 3, claim: 'Own claim', verdict: 'Indeterminate', citation: null, basis: 'Unknown.', roles: ['unverifiedClaim'], raisedBy: ['A'] }
      ]));

      const items: HTMLElement[] = Array.from(box.querySelectorAll('.claim-verification-item'));
      const labelsOf = (item: HTMLElement) =>
        Array.from(item.querySelectorAll('.claim-role-label')).map(l => (l.textContent || '').trim());
      expect(labelsOf(items[0])).toEqual(['(critical-error quote from member A)']);
      expect(labelsOf(items[1])).toEqual(['(sentence member B charged as false)']);
      expect(labelsOf(items[2])).toEqual(['(out-of-rubric basis from both members)']);
      expect(labelsOf(items[3])).toEqual([]);

      // Without raisedBy, a single-assessor run's labels are unchanged.
      expect(component.claimRoleLabels(['criticalErrorQuote', 'outOfRubricBasis', 'accusedQuote']))
        .toEqual(['critical-error quote', 'out-of-rubric basis', 'sentence the assessor charged as false']);
    });

    it('should name only the accusing member of a sentence both members raised', () => {
      const box = renderClaimVerifications(JSON.stringify([
        { claimIndex: 0, claim: 'Charged by A', verdict: 'Supported', citation: 'src/a.c', basis: 'True.', roles: ['unverifiedClaim', 'accusedQuote'], raisedBy: ['A', 'B'], accusedBy: ['A'] },
        { claimIndex: 1, claim: 'Legacy record', verdict: 'Supported', citation: 'src/b.c', basis: 'True.', roles: ['accusedQuote'], raisedBy: ['A', 'B'] }
      ]));

      const items: HTMLElement[] = Array.from(box.querySelectorAll('.claim-verification-item'));
      const labelsOf = (item: HTMLElement) =>
        Array.from(item.querySelectorAll('.claim-role-label')).map(l => (l.textContent || '').trim());
      expect(labelsOf(items[0])).toEqual(['(sentence member A charged as false)']);
      // A record stored before harness 44 has no accusedBy and falls back to raisedBy.
      expect(labelsOf(items[1])).toEqual(['(sentence both members charged as false)']);
    });

    it('should render a legacy verification record without roles and without labels', () => {
      const box = renderClaimVerifications(JSON.stringify([
        { claimIndex: 0, claim: 'Claim 1', verdict: 'Supported', citation: 'src/a.c', basis: 'Valid.' }
      ]));

      expect(box).toBeTruthy();
      expect(box.querySelectorAll('.claim-verification-item').length).toBe(1);
      expect(box.querySelectorAll('.claim-role-label').length).toBe(0);
      expect(box.querySelector('.claim-roles-note')).toBeNull();
    });

    describe('Band drift', () => {
      function bandedAnswer(orderIndex: number, difficulty: number, assessedDifficulty: number): any {
        return {
          id: orderIndex, orderIndex, questionText: `Q${orderIndex}`, difficulty, assessedDifficulty,
          answerText: 'a', status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1,
          scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 80
        };
      }

      function bandSectionText(): string {
        const section = fixture.nativeElement.querySelector('.band-agreement-section');
        return ((section as HTMLElement)?.textContent || '').replace(/\s+/g, ' ').trim();
      }

      it('should call out a drift that moved every mismatch the same way', () => {
        // Two authored Simple (midpoint 25) assessed into the Intermediate band, one authored
        // Intermediate (midpoint 55) assessed into Advanced: +30, +40, +30, all upward.
        component.selectedRunDetail = buildFinishedRun({
          answers: [
            bandedAnswer(1, 1, 55), bandedAnswer(2, 1, 65),
            bandedAnswer(3, 2, 85), bandedAnswer(4, 1, 25)
          ]
        });
        fixture.detectChanges();

        expect(component.bandDisagreements().length).toBe(3);
        const drift = component.bandDriftSummary()!;
        expect(drift.up).toBe(3);
        expect(drift.down).toBe(0);
        expect(drift.meanDelta).toBeCloseTo(33.3, 1);
        expect(drift.oneDirection).toBe('up');

        const text = bandSectionText();
        expect(text).toContain('3 assessed harder than authored, 0 easier');
        expect(text).toContain('+33.3');
        expect(text).toContain('Every mismatch moved the same way — upward');
      });

      it('should not claim a direction when the mismatches disagree', () => {
        component.selectedRunDetail = buildFinishedRun({
          answers: [bandedAnswer(1, 1, 55), bandedAnswer(2, 3, 25)]
        });
        fixture.detectChanges();

        const drift = component.bandDriftSummary()!;
        expect(drift.up).toBe(1);
        expect(drift.down).toBe(1);
        expect(drift.oneDirection).toBeNull();
        expect(bandSectionText()).not.toContain('Every mismatch moved the same way');
      });

      it('should render no drift line when every question stayed in its authored band', () => {
        component.selectedRunDetail = buildFinishedRun({
          answers: [bandedAnswer(1, 1, 25), bandedAnswer(2, 2, 55)]
        });
        fixture.detectChanges();

        expect(component.bandDisagreements().length).toBe(0);
        expect(component.bandDriftSummary()).toBeNull();
        expect(fixture.nativeElement.querySelector('.band-agreement-section')).toBeNull();
      });
    });

    describe('Speed Index saturation', () => {
      function scoredAnswer(orderIndex: number, speedScore: number, modelTimeMs = 1): any {
        return {
          id: orderIndex, orderIndex, questionText: `Q${orderIndex}`, difficulty: 1, answerText: 'a',
          status: 'Ok', assessmentStatus: 'Scored', durationMs: modelTimeMs, modelTimeMs,
          scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 80, speedScore
        };
      }

      /**
       * The speed card, found by either of its two labels: the card leads with the Speed Index
       * ordinarily and with median model time once the index cannot discriminate.
       */
      function speedCard(): HTMLElement | undefined {
        const cards: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.score-card'));
        return cards.find(c => {
          const label = (c.querySelector('.score-label')?.textContent || '').trim();
          return label === 'Speed Index' || label === 'Median Model Time';
        });
      }

      function speedCardLabel(): string {
        return (speedCard()?.querySelector('.score-label')?.textContent || '').trim();
      }

      function speedCardValueText(): string {
        return ((speedCard()?.querySelector('.score-subvalue') as HTMLElement)?.textContent || '')
          .replace(/\s+/g, ' ').trim();
      }

      function speedIndexNoteText(): string {
        const notes: HTMLElement[] = Array.from(speedCard()?.querySelectorAll('.score-note') ?? []);
        return notes.map(n => (n.textContent || '').replace(/\s+/g, ' ').trim()).join(' ');
      }

      it('should lead with median model time once exactly half the scored answers sit at the ceiling', () => {
        component.selectedRunDetail = buildFinishedRun({
          scoringProfileSpeedTargetMs: 30000,
          answers: [
            scoredAnswer(1, 100, 1000), scoredAnswer(2, 100, 2000),
            scoredAnswer(3, 50, 3000), scoredAnswer(4, 50, 4000)
          ]
        });
        fixture.detectChanges();

        expect(component.speedIndexScoredAnswerCount).toBe(4);
        expect(component.speedIndexCeilingAnswerCount).toBe(2);
        expect(component.showSpeedIndexSaturationAdvisory).toBeTrue();
        expect(component.demoteSpeedIndex).toBeTrue();
        expect(component.medianModelTimeMs).toBe(2500);
        expect(speedCardLabel()).toBe('Median Model Time');
        expect(speedCardValueText()).toBe('2.5 s');
        expect(speedIndexNoteText()).toContain('Speed Index');
        expect(speedIndexNoteText()).toContain('saturated: 2 of 4 at the ceiling');
      });

      it('should keep the concurrency marker on the demoted card and on Mean Time', () => {
        component.selectedRunDetail = buildFinishedRun({
          scoringProfileSpeedTargetMs: 30000,
          speedMeasurementDegraded: true,
          answers: [
            scoredAnswer(1, 100, 1000), scoredAnswer(2, 100, 2000),
            scoredAnswer(3, 50, 3000), scoredAnswer(4, 50, 4000)
          ]
        });
        fixture.detectChanges();

        expect(component.demoteSpeedIndex).toBeTrue();
        const marker = speedCard()?.querySelector('.score-subvalue .degraded-tag');
        expect(marker?.getAttribute('title')).toBe('Concurrency enabled; speed advisory');
        const cards: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.score-card'));
        const meanTime = cards.find(c => c.getAttribute('data-figure') === 'mean-time');
        expect(meanTime?.querySelector('.score-subvalue .degraded-tag')?.getAttribute('title'))
          .toBe('Concurrency enabled; speed advisory');
      });

      it('should not flag saturation just below half', () => {
        component.selectedRunDetail = buildFinishedRun({
          scoringProfileSpeedTargetMs: 30000,
          answers: [scoredAnswer(1, 100), scoredAnswer(2, 100), scoredAnswer(3, 50), scoredAnswer(4, 50), scoredAnswer(5, 50)]
        });
        fixture.detectChanges();

        expect(component.speedIndexScoredAnswerCount).toBe(5);
        expect(component.speedIndexCeilingAnswerCount).toBe(2);
        expect(component.showSpeedIndexSaturationAdvisory).toBeFalse();
        expect(component.demoteSpeedIndex).toBeFalse();
        expect(speedCardLabel()).toBe('Speed Index');
        expect(speedIndexNoteText()).not.toContain('saturated');
      });
    });

    describe('instrument measurements', () => {
      function measurementsText(): string {
        const notes: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.alert-info'));
        const block = notes.find(n => (n.querySelector('.alert-heading')?.textContent || '')
          .includes('Instrument Measurements'));
        return (block?.textContent || '').replace(/\s+/g, ' ').trim();
      }

      it('should report both counts as measurements, outside the integrity notice', () => {
        component.selectedRunDetail = buildFinishedRun({
          completenessOutOfScopeCount: 2,
          readabilityFormOnlyCount: 3,
          completenessOutOfScopeDeductedCount: 1,
          readabilityFormOnlyDeductedCount: 2
        });
        fixture.detectChanges();

        expect(component.completenessOutOfScopeCount).toBe(2);
        expect(component.readabilityFormOnlyCount).toBe(3);
        expect(component.completenessOutOfScopeDeductedCount).toBe(1);
        expect(component.readabilityFormOnlyDeductedCount).toBe(2);
        expect(component.hasInstrumentMeasurements).toBeTrue();
        expect(measurementsText()).toContain('2 out-of-scope completeness deduction(s)');
        expect(measurementsText()).toContain('3 rubric format suggestion(s) not followed');
        expect(measurementsText()).toContain('1 of them sit beside a Completeness level below 6');
        expect(measurementsText()).toContain('2 of them sit beside a Readability level below 6');
      });

      it('should show only the count that was recorded', () => {
        component.selectedRunDetail = buildFinishedRun({
          completenessOutOfScopeCount: 0,
          readabilityFormOnlyCount: 1
        });
        fixture.detectChanges();

        expect(component.hasInstrumentMeasurements).toBeTrue();
        expect(measurementsText()).not.toContain('out-of-scope completeness deduction(s)');
        expect(measurementsText()).toContain('1 rubric format suggestion(s) not followed');
      });

      it('should give each panel member its own counts, and name member A on the docked subsets', () => {
        const memberB = (orderIndex: number, flags: any): any => ({
          id: 600 + orderIndex, orderIndex, questionText: `Q${orderIndex}`, difficulty: 1, answerText: 'a',
          status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1,
          scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 80,
          panelQualityScore: 78, coAssessmentStatus: 'Scored', coAssessmentQualityScore: 76,
          coAssessmentCriticalError: false, coAssessmentJson: JSON.stringify({ qualityScore: 76, flags })
        });
        component.selectedRunDetail = buildFinishedRun({
          isPanelRun: true,
          completenessOutOfScopeCount: 0,
          readabilityFormOnlyCount: 8,
          readabilityFormOnlyDeductedCount: 2,
          answers: [
            memberB(1, { readabilityFormOnly: true, completenessOutOfScope: true }),
            memberB(2, { readabilityFormOnly: true }),
            memberB(3, {})
          ]
        });
        fixture.detectChanges();

        expect(component.memberBReadabilityFormOnlyCount).toBe(2);
        expect(component.memberBCompletenessOutOfScopeCount).toBe(1);
        expect(component.hasInstrumentMeasurements).toBeTrue();
        const text = measurementsText();
        expect(text).toContain('8 (member A) and 2 (member B) rubric format suggestion(s) not followed');
        expect(text).toContain("2 of member A's sit beside a Readability level below 6");
        // Member A recorded none, member B one: the line shows because either member counted it.
        expect(text).toContain('0 (member A) and 1 (member B) out-of-scope completeness deduction(s)');
        expect(text).toContain('rubric points a panel member itself placed outside what the question asked');
        expect(text).not.toContain('the assessor itself');
      });

      it('should stay hidden when neither was recorded', () => {
        // Zero is not a finding here: a run graded before either marker existed reports zero too.
        component.selectedRunDetail = buildFinishedRun({
          completenessOutOfScopeCount: 0,
          readabilityFormOnlyCount: 0
        });
        fixture.detectChanges();

        expect(component.hasInstrumentMeasurements).toBeFalse();
        expect(measurementsText()).toBe('');
      });
    });
  });

  describe('run diagnostics capture', () => {
    function buildDiagnosticsRun(overrides: any = {}): any {
      return {
        id: 88,
        benchmarkSuiteId: 1,
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'GPT-5.6 Luna',
        testedModelProviderUsed: 'OpenAI',
        testedModelIdUsed: 'gpt-5.6-luna',
        testedModelThinkingLevelUsed: 'max',
        testedModelParallelExecutionModeUsed: 0,
        assessorModelDisplayNameUsed: 'Gemini 3.7 Flash',
        assessorModelProviderUsed: 'Google',
        assessorModelIdUsed: 'gemini-3.7-flash',
        secondOpinionAssessorModelConfigurationId: 4,
        secondOpinionAssessorModelDisplayNameUsed: 'Claude Opus 5',
        secondOpinionAssessorModelProviderUsed: 'Anthropic',
        secondOpinionAssessorModelIdUsed: 'claude-opus-5',
        status: 'Completed',
        startedAtUtc: '2026-09-03T06:52:00Z',
        completedAtUtc: '2026-09-03T07:28:00Z',
        qualityIndex: 94,
        unweightedQualityIndex: 92,
        rawQualityIndex: 96,
        speedIndex: 67,
        finalScore: 91,
        computedScore: null,
        totalAnswerDurationMs: 900000,
        totalDurationMs: 900000,
        scoringProfileId: 1,
        scoringProfileName: 'Standard Intelligence Index (Default)',
        scoringProfileSpeedTargetMs: 15000,
        scoringProfileSpeedDecayK: 20,
        scoringProfileSecondOpinionQualityThreshold: 50,
        scoringProfileSecondOpinionOutlierDeltaPoints: 25,
        scoringMethodVersion: 6,
        harnessVersion: '7',
        secondOpinionModeUsed: 3,
        secondOpinionGradedAnswerCount: 2,
        secondOpinionMeanAbsDelta: 4.25,
        secondOpinionDisagreementCount: 1,
        contestedVerdictAnswerCount: 1,
        reassessedAnswerCount: 1,
        transportDefectAnswerCount: 0,
        recoveredAnswerCount: 0,
        toolStarvedAnswerCount: 1,
        advisoryFlagAnswerCount: 1,
        scrubbedArtifactAnswerCount: 0,
        toolOverheadMs: 1056,
        difficultyFallbackUsed: false,
        speedMeasurementDegraded: false,
        maxParallelQuestionsUsed: 1,
        answeredQuestionCount: 2,
        totalQuestionCount: 2,
        assessmentParseFailed: false,
        totalInputTokens: 100,
        totalOutputTokens: 200,
        totalCacheReadTokens: 0,
        totalCacheCreationTokens: 0,
        errorMessage: null,
        answers: [
          {
            id: 1, benchmarkRunId: 88, orderIndex: 1, questionText: 'Q1', answerText: 'a',
            difficulty: 1, assessedDifficulty: 25, status: 'Ok', assessmentStatus: 'Scored',
            durationMs: 48800, modelTimeMs: 47744, toolTimeMs: 1056, scrubbedArtifactCount: 0,
            answerFlags: 32, answerFlagNames: ['ContestedVerdict'],
            qualityScore: 60, rawQualityScore: 60, speedScore: 29, criticalError: false,
            accuracyLevel: 3, completenessLevel: 4, concisenessLevel: 5, readabilityLevel: 6,
            toolCallCount: 34, toolCallBudgetUsed: 35, toolCallSummary: 'wiki_search×34',
            narrationBlockCount: 3, unverifiedClaimCount: 2,
            secondOpinionQualityScore: 85, secondOpinionTrigger: 'All', secondOpinionDisagreed: true,
            reassessmentCount: 1, previousQualityScore: 42,
            reassessedByModelDisplayNameUsed: 'Claude Opus 5'
          },
          {
            id: 2, benchmarkRunId: 88, orderIndex: 2, questionText: 'Q2', answerText: 'b',
            difficulty: 3, assessedDifficulty: 85, status: 'Ok', assessmentStatus: 'Scored',
            durationMs: 20000, modelTimeMs: 20000, scrubbedArtifactCount: 0,
            answerFlags: 0, answerFlagNames: [], qualityScore: 99, speedScore: 70,
            criticalError: false, toolCallCount: 25, toolCallBudgetUsed: 25,
            toolCallSummary: 'wiki_search×25 (3 blocked by budget)', toolBudgetExhausted: true,
            secondOpinionQualityScore: 97, secondOpinionTrigger: 'All'
          }
        ],
        ...overrides
      };
    }

    it('should name all three model roles', () => {
      component.activeRunDetail = buildDiagnosticsRun();
      const text = component.runDiagnosticsText;

      expect(text).toContain('Second:   Claude Opus 5 (Anthropic / claude-opus-5)');
    });

    it('should say so when no second opinion assessor was selected', () => {
      component.activeRunDetail = buildDiagnosticsRun({
        secondOpinionAssessorModelConfigurationId: null
      });
      expect(component.runDiagnosticsText).toContain('Second:   none selected');
    });

    it('should include a Verifier line built like the neighbouring model lines', () => {
      component.activeRunDetail = buildDiagnosticsRun({
        claimVerifierModelConfigurationId: 5,
        claimVerifierDisplayNameUsed: 'GnollHack Verifier',
        claimVerifierProviderUsed: 'Anthropic',
        claimVerifierModelIdUsed: 'claude-verifier',
        claimVerifierThinkingLevelUsed: 'high',
        claimVerifierReasoningModeUsed: 'enabled'
      });
      expect(component.runDiagnosticsText)
        .toContain('Verifier: GnollHack Verifier (Anthropic / claude-verifier), thinking: high, reasoning: enabled');
    });

    it('should say so when no claim verifier was selected', () => {
      component.activeRunDetail = buildDiagnosticsRun();
      expect(component.runDiagnosticsText).toContain('Verifier: none selected');
    });

    it('should print an unset service tier as default (none requested) in the MODELS block', () => {
      component.activeRunDetail = buildDiagnosticsRun();
      expect(component.runDiagnosticsText).toContain('service tier: default (none requested)');
    });

    it('should record the scoring constants the run was actually scored with', () => {
      component.activeRunDetail = buildDiagnosticsRun();
      const text = component.runDiagnosticsText;

      expect(text).toContain('harness version: 7');
      expect(text).toContain('scoring method version: 6');
      expect(text).toContain('Speed: target 15000 ms, decay k 20');
      // The outlier delta is read only by the FlaggedAndOutliers trigger, so under All it governed
      // nothing and printing it read as a threshold this run applied.
      expect(text).toContain('Second reader: mode All, threshold 50');
      expect(text).not.toContain('outlier delta');
    });

    it('should print the outlier delta only under the trigger that reads it', () => {
      component.activeRunDetail = buildDiagnosticsRun({ secondOpinionModeUsed: 2 });
      expect(component.runDiagnosticsText)
        .toContain('Second reader: mode FlaggedAndOutliers, threshold 50, outlier delta 25');
    });

    it('should name the mode added after this capture was written', () => {
      component.activeRunDetail = buildDiagnosticsRun({ secondOpinionModeUsed: 4 });
      expect(component.runDiagnosticsText).toContain('Second reader: mode FlaggedPlusSample');
      expect(component.runDiagnosticsText).not.toContain('mode unknown');
    });

    it('should omit the superseded computed score rather than printing "computed: n/a"', () => {
      component.activeRunDetail = buildDiagnosticsRun();
      const text = component.runDiagnosticsText;

      expect(text).not.toContain('computed:');
      expect(text).toContain('unweighted mean: 92');

      component.activeRunDetail = buildDiagnosticsRun({ computedScore: 88 });
      expect(component.runDiagnosticsText).toContain('computed (superseded): 88');
    });

    it('should carry an integrity block with the four-class accounting and the agreement figures', () => {
      component.activeRunDetail = buildDiagnosticsRun();
      const text = component.runDiagnosticsText;

      expect(text).toContain('--- INTEGRITY ---');
      expect(text).toContain('clean: 1, transport defects: 0, recovered: 0, harness limits: 1 (sums to 2)');
      expect(text).toContain('contested verdicts: 1, unevidenced deductions: 0, refuted claims: 0, contested critical errors: 0, contested accuracy deductions: not recorded, dimension outliers: not recorded, re-assessed: 1');
      expect(text).toContain('unverified claims: 2');
      expect(text).toContain('4.3 mean abs delta');
      expect(text).toContain('over 2 of 2 answered, disagreements: 1');
      // Full coverage, so no conditioning caveat.
      expect(text).not.toContain('coverage selected by trigger');
    });

    it('should print the contested accuracy deduction count when recorded, zero included', () => {
      component.activeRunDetail = buildDiagnosticsRun({ contestedAccuracyDeductionAnswerCount: 2 });
      expect(component.runDiagnosticsText).toContain('contested critical errors: 0, contested accuracy deductions: 2, dimension outliers: not recorded, re-assessed: 1');

      // Zero is a measurement on a harness-20 run; only null reads as not recorded.
      component.activeRunDetail = buildDiagnosticsRun({ contestedAccuracyDeductionAnswerCount: 0 });
      expect(component.runDiagnosticsText).toContain('contested accuracy deductions: 0,');

      component.activeRunDetail = buildDiagnosticsRun({ contestedAccuracyDeductionAnswerCount: null });
      expect(component.runDiagnosticsText).toContain('contested accuracy deductions: not recorded');
    });

    it('should caveat the agreement rate when coverage was selected by trigger', () => {
      component.activeRunDetail = buildDiagnosticsRun({ secondOpinionModeUsed: 1 });
      expect(component.runDiagnosticsText).toContain('coverage selected by trigger');
    });

    it('should extend each question line with the fields that explain its score', () => {
      component.activeRunDetail = buildDiagnosticsRun();
      const text = component.runDiagnosticsText;

      expect(text).toContain('band=Simple');
      expect(text).toContain('assessedDiff=25');
      expect(text).toContain('levels=3/4/5/6');
      expect(text).toContain('critical=false');
      expect(text).toContain('tools=34/35');
      expect(text).toContain('narration=3');
      expect(text).toContain('unverified=2');
      expect(text).toContain('flags=ContestedVerdict');
      expect(text).toContain('secondOpinion=85/All disagreed');
      expect(text).toContain('reassessed=42→60/Claude Opus 5');
      // Blocked calls come from the tool summary, because toolCallCount counts attempts.
      expect(text).toContain('tools=25/25 (3 blocked) exhausted');
    });

    it('should record whether the candidate prompt carried a game snapshot', () => {
      component.activeRunDetail = buildDiagnosticsRun({
        candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true,"hasGameSnapshot":true}'
      });
      expect(component.runDiagnosticsText).toContain('snapshot=true');

      component.activeRunDetail = buildDiagnosticsRun({
        candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true}'
      });
      expect(component.runDiagnosticsText).toContain('snapshot=false');
    });

    it('should record the candidate delivery probe and the board delivery per grading role', () => {
      component.activeRunDetail = buildDiagnosticsRun();
      expect(component.runDiagnosticsText).toContain('Candidate delivery probe: not recorded');
      expect(component.runDiagnosticsText).not.toContain('Board delivered');

      component.activeRunDetail = buildDiagnosticsRun({
        candidateDeliveryVerifiedAtUtc: '2026-09-18T07:11:00Z',
        boardDelivery: [
          { role: 'assessor', delivered: 18, total: 18, missingQuestions: [] },
          { role: 'second reader', delivered: 13, total: 14, missingQuestions: [6] },
          { role: 'claim verifier', delivered: 9, total: 9, missingQuestions: [] }
        ]
      });
      const text = component.runDiagnosticsText;

      expect(text).toContain('Candidate delivery probe: verified at 2026-09-18T07:11:00Z');
      expect(text).toContain('Board delivered — assessor 18 of 18 graded, second reader 13 of 14, claim verifier 9 of 9; synthesis: yes; difficulty assessment: digest (no map).');
      expect(text).toContain('Board not delivered — second reader: Q6');
    });

    it('should name the reference reader throughout the diagnostics of a panel run', () => {
      component.activeRunDetail = buildDiagnosticsRun({
        isPanelRun: true,
        secondOpinionModeUsed: 3,
        boardDelivery: [
          { role: 'assessor', delivered: 18, total: 18, missingQuestions: [] },
          { role: 'co-assessor', delivered: 18, total: 18, missingQuestions: [] },
          { role: 'reference reader', delivered: 18, total: 18, missingQuestions: [] },
          { role: 'claim verifier', delivered: 9, total: 9, missingQuestions: [] }
        ]
      });
      const text = component.runDiagnosticsText;

      expect(text).toContain('Reference reader: Claude Opus 5 (Anthropic / claude-opus-5)');
      expect(text).not.toContain('Second:');
      expect(text).toContain('Reference reader: mode All');
      expect(text).toContain('reference reader now:');
      expect(text).toContain('Answers with verified claims: 0, reference-read 2');
      expect(text).not.toContain('second-graded');
      expect(text).toContain('reference reader vs panel: 4.3 mean abs delta');
      expect(text).not.toMatch(/^agreement:/m);
      expect(text).toContain('sameProviderAcknowledged=n/a (panel run)');
      expect(text).toContain('Board delivered — assessor 18 of 18 graded, co-assessor 18 of 18, reference reader 18 of 18, claim verifier 9 of 9');
      expect(text).not.toMatch(/second opinion/i);
      expect(text).not.toContain('second reader');

      component.activeRunDetail = buildDiagnosticsRun({ secondOpinionModeUsed: 3 });
      const single = component.runDiagnosticsText;
      expect(single).toContain('Second reader: mode All');
      expect(single).toContain('second reader now:');
      expect(single).toContain('Second:   Claude Opus 5');
      expect(single).toContain('Answers with verified claims: 0, second-graded 2');
      expect(single).toContain('agreement: 4.3 mean abs delta');
      expect(single).toContain('sameProviderAcknowledged=false');
    });

    it('should print the panel line and member B\'s per-question figures', () => {
      const run = buildDiagnosticsRun({
        isPanelRun: true,
        coAssessorFinalScore: 88,
        assessorOnlyQualityIndex: 93,
        coAssessorOnlyQualityIndex: 90,
        panelMeanAbsDelta: 6.2,
        panelMeanSignedDelta: -3.5,
        panelIntraclassCorrelation: 0.8123,
        panelDisagreementCount: 1,
        panelCriticalErrorSplitCount: 0
      });
      run.answers[0] = {
        ...run.answers[0],
        panelQualityScore: 72.5,
        coAssessmentQualityScore: 85,
        coAssessmentJson: JSON.stringify({
          accuracyLevel: 5, completenessLevel: 4, concisenessLevel: 6, readabilityLevel: 5,
          unverifiedClaims: ['x'],
          flags: { contestedVerdict: true, readabilityFormOnly: true }
        }),
        assessorBoardChars: 12037,
        coAssessorBoardChars: 12040,
        secondOpinionBoardChars: null,
        verifierBoardChars: 900,
        claimVerificationJson: JSON.stringify([
          { claimIndex: 0, claim: 'c1', verdict: 'Supported', roles: ['unverifiedClaim'], raisedBy: ['A'] },
          { claimIndex: 1, claim: 'c2', verdict: 'Refuted', roles: ['unverifiedClaim'], raisedBy: ['A', 'B'] },
          { claimIndex: 2, claim: 'c3', verdict: 'Indeterminate', roles: ['unverifiedClaim'], raisedBy: ['B'] },
          { claimIndex: 3, claim: 'c4', verdict: 'Supported', roles: ['accusedQuote'], raisedBy: ['B'] }
        ])
      };
      run.answers[1] = {
        ...run.answers[1],
        panelQualityScore: 98,
        coAssessmentQualityScore: 97,
        coAssessmentJson: JSON.stringify({ flags: { contestedVerdict: true } })
      };
      component.activeRunDetail = run;
      const text = component.runDiagnosticsText;
      const lines = text.split('\n');

      expect(text).toContain('holistic: A 91, B 88, quality index: 94');
      expect(text).toContain('panel: A-alone 93, B-alone 90, mean |B−A| 6.2, mean B−A −3.5, ICC 0.81, disagreements 1, critical-error splits 0');
      const advisory = lines.findIndex(l => l.startsWith('advisory flags:'));
      expect(lines[advisory + 1]).toBe('member B flags: contested verdicts: 2, unevidenced deductions: 0, omission as accuracy: 0, '
        + 'out-of-rubric accuracy: 0, dimension outliers: 0, completeness out of scope: 0, readability form only: 1, '
        + 'contested critical errors: 0, contested accuracy deductions: 0');
      // The union of both members' ordinary claims; the accused sentence is not one.
      expect(text).toContain('unverified claims: 3 (member A 1, member B 1, both 1)');

      const q1 = lines.find(l => l.startsWith('[Q1]'))!;
      expect(q1).toContain('panel=72.5 b=85 bLevels=5/4/6/5 band=');
      expect(q1).toContain('levels=3/4/5/6');
      expect(q1).toContain('boardChars=12037/12040/-/900');
      const q2 = lines.find(l => l.startsWith('[Q2]'))!;
      expect(q2).toContain('panel=98 b=97 band=');
      expect(q2).not.toContain('bLevels=');
      expect(q2).not.toContain('boardChars=');

      // Without a verified answer there is no union: each member's recorded count instead.
      run.answers[0] = { ...run.answers[0], claimVerificationJson: null };
      component.activeRunDetail = { ...run };
      expect(component.runDiagnosticsText).toContain('unverified claims: not verified (member A 2, member B 1 recorded)');

      // A single-assessor capture carries none of it.
      component.activeRunDetail = buildDiagnosticsRun();
      const single = component.runDiagnosticsText;
      expect(single).toContain('holistic: 91, quality index: 94');
      expect(single).not.toContain('panel:');
      expect(single).not.toContain('member B flags');
      expect(single).not.toContain('panel=');
      expect(single).toContain('unverified claims: 2');
    });

    it('should append the re-verified stamp when the candidate delivery probe was re-checked before the re-run', () => {
      component.activeRunDetail = buildDiagnosticsRun({
        candidateDeliveryVerifiedAtUtc: '2026-09-18T07:11:00Z',
        rerunCandidateDeliveryVerifiedAtUtc: '2026-09-19T09:00:00Z'
      });
      expect(component.runDiagnosticsText).toContain(
        'Candidate delivery probe: verified at 2026-09-18T07:11:00Z; re-verified before the re-run at 2026-09-19T09:00:00Z'
      );
    });

    it('should record the board format when the run had a board', () => {
      const boardFactsCheck = { bulletCount: 1, checkedLiteralCount: 1, unquotedBulletCount: 0, unquotedBullets: [], missingLiterals: [] };

      component.activeRunDetail = buildDiagnosticsRun({ boardFactsCheck, gameSnapshotFormatVersionUsed: 3 });
      expect(component.runDiagnosticsText).toContain('Board format: 3');

      component.activeRunDetail = buildDiagnosticsRun({ boardFactsCheck, harnessVersion: '33', gameSnapshotFormatVersionUsed: null });
      expect(component.runDiagnosticsText).toContain('Board format: not stated');

      component.activeRunDetail = buildDiagnosticsRun({ boardFactsCheck, harnessVersion: '32', gameSnapshotFormatVersionUsed: null });
      expect(component.runDiagnosticsText).toContain('Board format: not recorded (before harness 33)');
    });

    it('should omit the board format line for a board-less run', () => {
      component.activeRunDetail = buildDiagnosticsRun();
      expect(component.runDiagnosticsText).not.toContain('Board format:');
    });

    it('should add a continuation line for a re-executed answer', () => {
      const run = buildDiagnosticsRun();
      run.answers[0] = {
        ...run.answers[0],
        rerunAtUtc: '2026-09-19T10:00:00Z',
        rerunOfStatus: 'ProviderError',
        rerunOfErrorMessage: 'HTTP 529: overloaded'
      };
      component.activeRunDetail = run;
      const text = component.runDiagnosticsText;

      expect(text).toContain('     re-executed at 2026-09-19T10:00:00Z: was ProviderError — HTTP 529: overloaded');
    });

    it('should extend a question line with its board characters and evidence-informed re-grade', () => {
      const run = buildDiagnosticsRun();
      run.answers[0] = {
        ...run.answers[0],
        assessorBoardChars: 12037,
        secondOpinionBoardChars: 12037,
        verifierBoardChars: null,
        evidenceInformedQualityScore: 74,
        evidenceInformedCriticalError: false,
        evidenceInformedJson: '{"withdrawn":["Accuracy deduction: peacefuls are never displaced"]}'
      };
      component.activeRunDetail = run;
      const text = component.runDiagnosticsText;

      expect(text).toContain('boardChars=12037/12037/-');
      expect(text).toContain('evidenceInformed=74 withdrew=1');
      // Neither is printed for an answer that recorded neither.
      expect(text.split('\n').find(l => l.startsWith('[Q2]'))).not.toContain('boardChars=');
      expect(component.evidenceInformedWithdrawn(run.answers[0])).toEqual(['Accuracy deduction: peacefuls are never displaced']);
      expect(component.evidenceInformedWithdrawn({ ...run.answers[0], evidenceInformedJson: 'not json' })).toEqual([]);
    });

    it('should name the snapshot in the prompt summary only when the run had one', () => {
      expect(component.candidatePromptSummaryOf({
        candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true,"hasGameSnapshot":true}'
      } as any)).toBe('Gameplay Help · concise (tools on) · snapshot');

      expect(component.candidatePromptSummaryOf({
        candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true}'
      } as any)).toBe('Gameplay Help · concise (tools on)');
    });
  });

  describe('assessor calibration panel', () => {
    it('should load calibrations when a run detail opens and clear them on close', () => {
      benchmarkServiceMock.getRun.and.returnValue(of({
        id: 99, suiteName: 'Default Suite', status: 'Completed',
        testedModelDisplayNameUsed: 'M', testedModelProviderUsed: 'OpenAI', testedModelIdUsed: 'm',
        testedModelParallelExecutionModeUsed: 0,
        assessorModelDisplayNameUsed: 'A', assessorModelProviderUsed: 'Google', assessorModelIdUsed: 'a',
        startedAtUtc: '2026-09-03T06:52:00Z', totalAnswerDurationMs: 0, totalDurationMs: 0,
        scoringMethodVersion: 6, transportDefectAnswerCount: 0, advisoryFlagAnswerCount: 0,
        scrubbedArtifactAnswerCount: 0, difficultyFallbackUsed: false, speedMeasurementDegraded: false,
        maxParallelQuestionsUsed: 1, answeredQuestionCount: 0, totalQuestionCount: 0,
        assessmentParseFailed: false, totalInputTokens: 0, totalOutputTokens: 0,
        totalCacheReadTokens: 0, totalCacheCreationTokens: 0, answers: []
      } as any));
      benchmarkServiceMock.getCalibrations.and.returnValue(of([
        {
          id: 1, benchmarkRunId: 99, assessorDisplayNameUsed: 'Claude Opus 5',
          assessorProviderUsed: 'Anthropic', assessorModelIdUsed: 'claude-opus-5',
          createdAtUtc: '2026-09-04T08:00:00Z', answerCount: 18, skippedAnswerCount: 0,
          meanAbsDelta: 5.5, disagreementCount: 2, inputTokens: 1000, outputTokens: 500,
          durationMs: 42000
        }
      ]));
      spyOn(component.runDetailDialog.nativeElement, 'showModal');

      component.viewRunDetail(99);

      expect(benchmarkServiceMock.getCalibrations).toHaveBeenCalledWith(99);
      expect(component.calibrations.length).toBe(1);

      component.closeRunDetail();
      expect(component.calibrations.length).toBe(0);
    });

    it('should refuse to calibrate without an assessor selected', () => {
      component.calibrationAssessorConfigId = null;
      component.runCalibration(99);
      expect(benchmarkServiceMock.calibrateAssessor).not.toHaveBeenCalled();
    });

    it('should reload the list after a calibration completes', () => {
      benchmarkServiceMock.calibrateAssessor.and.returnValue(of({ id: 2 } as any));
      benchmarkServiceMock.getCalibrations.and.returnValue(of([]));
      component.calibrationAssessorConfigId = 3;

      component.runCalibration(99);

      expect(benchmarkServiceMock.calibrateAssessor).toHaveBeenCalledWith(99, 3);
      expect(benchmarkServiceMock.getCalibrations).toHaveBeenCalledWith(99);
      expect(component.calibrating).toBeFalse();
    });
  });

  describe('trial re-assessment', () => {
    const answer: any = {
      id: 5, benchmarkRunId: 99, orderIndex: 3, questionText: 'Q3', answerText: 'a',
      difficulty: 1, status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1,
      scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 60
    };

    beforeEach(() => {
      spyOn(component.retryDialog.nativeElement, 'showModal');
      spyOn(component.retryDialog.nativeElement, 'close');
      benchmarkServiceMock.trialReassessAnswer.and.returnValue(of({ runId: 99 }));
    });

    it('should call the trial endpoint and never the one that replaces the verdict', fakeAsync(() => {
      component.openRetryDialog('trial', 99, answer);
      component.retryAssessorConfigId = 4;
      component.confirmRetry();

      expect(benchmarkServiceMock.trialReassessAnswer).toHaveBeenCalledWith(99, 5, 4, false);
      expect(benchmarkServiceMock.reassessAnswer).not.toHaveBeenCalled();

      component.stopDetailPolling();
      discardPeriodicTasks();
    }));

    it('should ask to replace an automatic second opinion, but not a previous trial', fakeAsync(() => {
      component.openRetryDialog('trial', 99, { ...answer, secondOpinionQualityScore: 80, secondOpinionTrigger: 'All' });
      component.retryAssessorConfigId = 4;
      component.confirmRetry();
      expect(benchmarkServiceMock.trialReassessAnswer.calls.mostRecent().args[3]).toBeTrue();

      component.openRetryDialog('trial', 99, { ...answer, secondOpinionQualityScore: 80, secondOpinionTrigger: 'Manual' });
      component.retryAssessorConfigId = 4;
      component.confirmRetry();
      expect(benchmarkServiceMock.trialReassessAnswer.calls.mostRecent().args[3]).toBeFalse();

      component.stopDetailPolling();
      discardPeriodicTasks();
    }));
  });

  describe('live run statistics, token formatting, and integrity notice', () => {
    it('should format token cards in run-stat strip with commas', () => {
      component.activeRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Running',
        totalInputTokens: 1234567,
        totalOutputTokens: 8910,
        totalCacheReadTokens: 50000,
        totalCacheCreationTokens: 12000,
        answers: []
      } as any;

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('1,234,567');
      expect(text).toContain('8,910');
      expect(text).toContain('50,000');
      expect(text).toContain('12,000');
    });

    it("should report Cache Creation as n/a for OpenAI when the provider reports cache reads but no cache creation", () => {
      component.activeRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Running',
        testedModelProviderUsed: 'OpenAI',
        totalInputTokens: 0,
        totalOutputTokens: 0,
        totalCacheReadTokens: 50000,
        totalCacheCreationTokens: 0,
        answers: []
      } as any;

      expect(component.runCacheCreationUnreported).toBeTrue();

      fixture.detectChanges();
      const el: HTMLElement = fixture.nativeElement;
      expect(el.textContent || '').toContain('n/a');
    });

    it('should report Cache Creation as a number for a provider that does report it', () => {
      component.activeRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Running',
        testedModelProviderUsed: 'Anthropic',
        totalInputTokens: 0,
        totalOutputTokens: 0,
        totalCacheReadTokens: 50000,
        totalCacheCreationTokens: 12000,
        answers: []
      } as any;

      expect(component.runCacheCreationUnreported).toBeFalse();

      fixture.detectChanges();
      const el: HTMLElement = fixture.nativeElement;
      expect(el.textContent || '').toContain('12,000');
    });

    it('should display candidate totals when run is running', () => {
      component.activeRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Running',
        totalInputTokens: 15000,
        totalOutputTokens: 3000,
        totalCacheReadTokens: 0,
        totalCacheCreationTokens: 0,
        answers: [
          { orderIndex: 1, inputTokens: 5000, outputTokens: 1000 } as any,
          { orderIndex: 2, inputTokens: 10000, outputTokens: 2000 } as any
        ]
      } as any;

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('15,000');
      expect(text).toContain('3,000');
    });

    it('should display claim verification failure clause in Run Integrity Notice when claimVerificationFailedAnswerCount > 0', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        answers: [
          { orderIndex: 3, status: 'Ok', claimVerificationError: 'Model timeout after 120s' } as any
        ]
      } as any;

      expect(component.claimVerificationFailedAnswerCount).toBe(1);
      expect(component.claimVerificationFailedQuestionNumbers).toBe('3');

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('Run Integrity Notice');
      expect(text).toContain('1 answer(s) had claim verification fail');
      expect(text).toContain('(question(s) 3)');
      expect(text).toContain('unverified claims were never checked');
    });

    it('should display contested critical error clause in Run Integrity Notice when the count is above zero', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        contestedCriticalErrorAnswerCount: 1,
        answers: [
          { orderIndex: 1, status: 'Ok', criticalError: true, answerFlagNames: ['ContestedCriticalError'] } as any
        ]
      } as any;

      expect(component.contestedCriticalErrorAnswerCount).toBe(1);
      expect(component.contestedCriticalErrorQuestionNumbers).toBe('1');

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('Run Integrity Notice');
      expect(text).toContain('1 answer(s) carry a contested critical error');
      expect(text).toContain('(question(s) 1)');
      expect(text).toContain('the cap stands and no score changed');
    });

    it('should display contested accuracy deduction clause in Run Integrity Notice when the count is above zero', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        contestedAccuracyDeductionAnswerCount: 2,
        answers: [
          { orderIndex: 3, status: 'Ok', answerFlagNames: ['OutOfRubricAccuracyDeduction', 'ContestedAccuracyDeduction'] } as any,
          { orderIndex: 7, status: 'Ok', answerFlagNames: ['OutOfRubricAccuracyDeduction', 'ContestedAccuracyDeduction'] } as any
        ]
      } as any;

      expect(component.contestedAccuracyDeductionAnswerCount).toBe(2);
      expect(component.contestedAccuracyDeductionQuestionNumbers).toBe('3, 7');

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('Run Integrity Notice');
      expect(text).toContain('2 answer(s) carry a contested accuracy deduction');
      expect(text).toContain('(question(s) 3, 7)');
      expect(text).toContain('the deduction stands and no score changed');
    });

    it('should display dimension outlier clause in Run Integrity Notice when the count is above zero', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        dimensionOutlierAnswerCount: 2,
        answers: [
          { orderIndex: 3, status: 'Ok', answerFlagNames: ['DimensionOutlier'] } as any,
          { orderIndex: 7, status: 'Ok', answerFlagNames: ['DimensionOutlier'] } as any
        ]
      } as any;

      expect(component.dimensionOutlierAnswerCount).toBe(2);
      expect(component.dimensionOutlierQuestionNumbers).toBe('3, 7');

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('Run Integrity Notice');
      expect(text).toContain('2 answer(s) with a dimension outlier');
      expect(text).toContain('(question(s) 3, 7)');
      expect(text).toContain('routed to a second reader and no score changed');
    });

    it('should omit the contested accuracy deduction clause when the count is null or zero', () => {
      for (const count of [null, 0]) {
        component.selectedRunDetail = {
          id: 1,
          suiteName: 'Suite',
          status: 'Completed',
          contestedAccuracyDeductionAnswerCount: count,
          answers: [{ orderIndex: 1, status: 'Ok', answerFlagNames: [] } as any]
        } as any;

        fixture.detectChanges();

        const text = (fixture.nativeElement as HTMLElement).textContent || '';
        expect(text).not.toContain('contested accuracy deduction');
      }
      expect(component.contestedAccuracyDeductionAnswerCount).toBe(0);
    });

    it('should render the tool call outcome split only when every answer with tool calls has recorded outcomes', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        answers: [
          { orderIndex: 1, status: 'Ok', toolCallCount: 5, toolCallsSucceeded: 4, toolCallsFailed: 1, toolCallsRefused: 0 } as any,
          { orderIndex: 2, status: 'Ok', toolCallCount: 3, toolCallsSucceeded: 3, toolCallsFailed: 0, toolCallsRefused: 0 } as any
        ]
      } as any;

      expect(component.toolCallOutcomeSummary()).toBe('7 succeeded, 1 failed, 0 refused by budget');

      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).textContent || '')
        .toContain('7 succeeded, 1 failed, 0 refused by budget');

      // One answer whose outcomes predate the per-call record makes the run-level sum a figure
      // that omits it silently, so the line is withheld entirely rather than under-reported.
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        answers: [
          { orderIndex: 1, status: 'Ok', toolCallCount: 5, toolCallsSucceeded: 4, toolCallsFailed: 1, toolCallsRefused: 0 } as any,
          { orderIndex: 2, status: 'Ok', toolCallCount: 3 } as any
        ]
      } as any;

      expect(component.toolCallOutcomeSummary()).toBeNull();
    });

    it('should render the tool rounds line from the per-call rows the dialog has loaded', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        answers: [
          { orderIndex: 1, status: 'Ok', toolCallCount: 4 } as any
        ]
      } as any;

      expect(component.toolRoundsSummary()).toBeNull();

      component.toolCallsByAnswer.set(1, [
        { id: 1, iterationIndex: 0, name: 'wiki_search' } as any,
        { id: 2, iterationIndex: 0, name: 'wiki_view' } as any,
        { id: 3, iterationIndex: 1, name: 'source_code_search' } as any,
        { id: 4, iterationIndex: 1, name: 'source_code_view' } as any
      ]);

      const summary = component.toolRoundsSummary();
      expect(summary).toContain('2.0 tool round(s) per answer on average');
      expect(summary).toContain('2.0 call(s) per round');
      expect(summary).toContain('over 1 answer(s) loaded');

      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).textContent || '')
        .toContain('2.0 tool round(s) per answer on average');
    });

    it('should display second-opinion failure clause and suppress no-trigger clause when secondOpinionFailedAnswerCount > 0', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        secondOpinionAssessorModelConfigurationId: 4,
        secondOpinionGradedAnswerCount: 0,
        answers: [
          { orderIndex: 7, status: 'Ok', secondOpinionError: '429 Rate limited' } as any
        ]
      } as any;

      expect(component.secondOpinionFailedAnswerCount).toBe(1);
      expect(component.secondOpinionFailedQuestionNumbers).toBe('7');
      expect(component.secondOpinionSelectedButUnused).toBeTrue();

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('Run Integrity Notice');
      expect(text).toContain('1 answer(s) met a trigger but the second-reader call failed');
      expect(text).toContain('(question(s) 7)');
      expect(text).not.toContain('A second reader was selected but no answer met a trigger');
    });

    it('should display no-trigger clause when secondOpinionSelectedButUnused is true and secondOpinionFailedAnswerCount is 0', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        secondOpinionAssessorModelConfigurationId: 4,
        secondOpinionGradedAnswerCount: 0,
        answers: [
          { orderIndex: 1, status: 'Ok' } as any
        ]
      } as any;

      expect(component.secondOpinionFailedAnswerCount).toBe(0);
      expect(component.secondOpinionSelectedButUnused).toBeTrue();

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('Run Integrity Notice');
      expect(text).toContain('A second reader was selected but no answer met a trigger');
      expect(text).not.toContain('second-reader call failed');
    });

    it('should measure agreement over the completed second opinions when some failed but others completed', () => {
      component.selectedRunDetail = {
        id: 1,
        suiteName: 'Suite',
        status: 'Completed',
        secondOpinionAssessorModelConfigurationId: 4,
        secondOpinionGradedAnswerCount: 3,
        answers: [
          { orderIndex: 3, status: 'Ok', secondOpinionError: '429 Rate limited' } as any,
          { orderIndex: 1, status: 'Ok' } as any,
          { orderIndex: 2, status: 'Ok' } as any,
          { orderIndex: 4, status: 'Ok' } as any
        ]
      } as any;

      expect(component.secondOpinionFailedAnswerCount).toBe(1);
      expect(component.secondOpinionCompletedAnswerCount).toBe(3);

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('1 answer(s) met a trigger but the second-reader call failed');
      expect(text).toContain('(question(s) 3)');
      expect(text).toContain('grader agreement is measured over the 3 answer(s) whose second reading completed');
      expect(text).not.toContain('grader agreement is not measured for this run');
    });
  });

  describe('Harness Version 11 fidelity features', () => {
    it('should compute indexConfidenceLabel correctly from qualityIndexStandardError', () => {
      component.selectedRunDetail = {
        id: 1,
        qualityIndexStandardError: 3.06
      } as any;
      expect(component.indexConfidenceLabel).toBe('± 6');

      component.selectedRunDetail = {
        id: 1,
        qualityIndexStandardError: null
      } as any;
      expect(component.indexConfidenceLabel).toBe('');

      component.selectedRunDetail = {
        id: 1,
        qualityIndexStandardError: 0
      } as any;
      expect(component.indexConfidenceLabel).toBe('');
    });

    it('should compute secondOpinionBlindLabel correctly', () => {
      component.selectedRunDetail = {
        id: 1,
        secondOpinionBlindUsed: true
      } as any;
      expect(component.secondOpinionBlindLabel).toBe('blind');

      component.selectedRunDetail = {
        id: 1,
        secondOpinionBlindUsed: false
      } as any;
      expect(component.secondOpinionBlindLabel).toBe('anchored');
    });

    it('should compute disputeVerificationLabel for single and multiple disputed answers with verification', () => {
      // Single disputed answer with verified claims
      component.selectedRunDetail = {
        id: 1,
        answers: [
          {
            orderIndex: 1,
            secondOpinionDisagreed: true,
            claimsSupportedCount: 3,
            claimsRefutedCount: 0,
            claimsIndeterminateCount: 0
          } as any,
          {
            orderIndex: 2,
            secondOpinionDisagreed: false
          } as any
        ]
      } as any;

      expect(component.disputeVerificationLabel).toBe('Claim verification for Q1: 3 supported, 0 refuted, 0 indeterminate.');

      // Multiple disputed answers with verified claims
      component.selectedRunDetail = {
        id: 1,
        answers: [
          {
            orderIndex: 1,
            secondOpinionDisagreed: true,
            claimsSupportedCount: 2,
            claimsRefutedCount: 1,
            claimsIndeterminateCount: 0
          } as any,
          {
            orderIndex: 3,
            secondOpinionDisagreed: true,
            claimsSupportedCount: 1,
            claimsRefutedCount: 0,
            claimsIndeterminateCount: 1
          } as any
        ]
      } as any;

      expect(component.disputeVerificationLabel).toBe('Claim verification for disputed answer(s): 3 supported, 1 refuted, 1 indeterminate.');

      // Disputed answer with no claim verification
      component.selectedRunDetail = {
        id: 1,
        answers: [
          {
            orderIndex: 1,
            secondOpinionDisagreed: true
          } as any
        ]
      } as any;

      expect(component.disputeVerificationLabel).toBe('');
    });

    it('should append the accused-sentence counts only when they are non-zero', () => {
      // Zero claim counts, but the accused-sentence check found something.
      component.selectedRunDetail = {
        id: 1,
        answers: [
          {
            orderIndex: 3,
            secondOpinionDisagreed: true,
            claimsSupportedCount: 0,
            claimsRefutedCount: 0,
            claimsIndeterminateCount: 0,
            accusedSupportedCount: 1,
            accusedRefutedCount: 1,
            accusedIndeterminateCount: 0
          } as any
        ]
      } as any;

      expect(component.disputeVerificationLabel).toBe(
        'Claim verification for Q3: 0 supported, 0 refuted, 0 indeterminate; accused sentences: 1 supported, 1 refuted, 0 indeterminate.'
      );

      // Accused counts all zero: no accused part, but the answer still counts as verified.
      component.selectedRunDetail = {
        id: 1,
        answers: [
          {
            orderIndex: 3,
            secondOpinionDisagreed: true,
            claimsSupportedCount: 0,
            claimsRefutedCount: 0,
            claimsIndeterminateCount: 0,
            accusedSupportedCount: 0,
            accusedRefutedCount: 0,
            accusedIndeterminateCount: 0
          } as any
        ]
      } as any;

      expect(component.disputeVerificationLabel).toBe('Claim verification for Q3: 0 supported, 0 refuted, 0 indeterminate.');
    });

    it('should compute omissionAsAccuracyAnswerCount and omissionAsAccuracyQuestionNumbers', () => {
      component.selectedRunDetail = {
        id: 1,
        omissionAsAccuracyAnswerCount: 2,
        answers: [
          { orderIndex: 1, answerFlagNames: ['OmissionAsAccuracy'] } as any,
          { orderIndex: 4, answerFlagNames: ['UnevidencedDeduction'] } as any,
          { orderIndex: 10, answerFlagNames: ['OmissionAsAccuracy', 'RefutedClaim'] } as any
        ]
      } as any;

      expect(component.omissionAsAccuracyAnswerCount).toBe(2);
      expect(component.omissionAsAccuracyQuestionNumbers).toBe('1, 10');
    });

    it('should map secondOpinionTriggerLabel for RefutedClaim and OmissionAsAccuracy', () => {
      expect(component.secondOpinionTriggerLabel('RefutedClaim')).toBe('refuted claim');
      expect(component.secondOpinionTriggerLabel('OmissionAsAccuracy')).toBe('omission docked as accuracy');
    });

    it('should map every second-opinion trigger to words, never to its raw name', () => {
      // SecondOpinionTriggers in Overseer/Services/Benchmarking/BenchmarkService.cs.
      const triggers = [
        'CriticalError', 'RefutedClaim', 'ContestedVerdict', 'OutOfRubricAccuracy',
        'UnevidencedDeduction', 'OmissionAsAccuracy', 'DimensionOutlier', 'UnverifiedClaims',
        'BelowThreshold', 'Outlier', 'All', 'Manual', 'Sample'
      ];
      for (const trigger of triggers) {
        const label = component.secondOpinionTriggerLabel(trigger);
        expect(label).not.toBe(trigger);
        expect(label.includes(' ') || /^[a-z]/.test(label)).withContext(trigger).toBeTrue();
      }
    });

    it('should render 95% CI score note under Intelligence Index tile and blind label in Assessor Agreement', () => {
      component.selectedRunDetail = {
        id: 1,
        status: 'Completed',
        suiteName: 'Suite',
        qualityIndex: 91,
        qualityIndexStandardError: 3.06,
        secondOpinionGradedAnswerCount: 2,
        secondOpinionBlindUsed: true,
        answers: []
      } as any;

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('± 6 (95% CI)');
      expect(text).toContain('blind');
    });

    it('should render omission-as-accuracy clause and dispute claim verification in Run Integrity Notice', () => {
      component.selectedRunDetail = {
        id: 1,
        status: 'Completed',
        suiteName: 'Suite',
        omissionAsAccuracyAnswerCount: 1,
        answers: [
          {
            orderIndex: 1,
            status: 'Ok',
            secondOpinionDisagreed: true,
            claimsSupportedCount: 3,
            claimsRefutedCount: 0,
            claimsIndeterminateCount: 0,
            answerFlagNames: ['OmissionAsAccuracy']
          } as any
        ]
      } as any;

      fixture.detectChanges();

      const el: HTMLElement = fixture.nativeElement;
      const text = el.textContent || '';
      expect(text).toContain('Run Integrity Notice');
      expect(text).toContain('1 answer(s) carry an omission docked as accuracy');
      expect(text).toContain('(question(s) 1)');
      expect(text).toContain('Claim verification for Q1: 3 supported, 0 refuted, 0 indeterminate.');
    });

    it('should default candidateVerboseMode to false and reflect appropriate hint', () => {
      expect(component.candidateVerboseMode).toBe(false);
      expect(component.candidateResponseStyleHint).toContain('production chat');

      component.candidateVerboseMode = true;
      expect(component.candidateResponseStyleHint).toContain('Only Accuracy stays comparable');
    });

    it('should include verboseMode in startRun payload', () => {
      benchmarkServiceMock.startRun.and.returnValue(of({ runId: 101 } as any));
      component.selectedSuiteId = 1;
      component.testedConfigId = 10;
      component.assessorConfigId = 20;
      component.candidateVerboseMode = true;

      component.startBenchmark();

      expect(benchmarkServiceMock.startRun).toHaveBeenCalledWith(jasmine.objectContaining({
        verboseMode: true
      }));
    });

    it('should send allowSourceCodeReferences, false by default', () => {
      benchmarkServiceMock.startRun.and.returnValue(of({ runId: 101 } as any));
      component.selectedSuiteId = 1;
      component.testedConfigId = 10;
      component.assessorConfigId = 20;

      component.startBenchmark();
      expect(benchmarkServiceMock.startRun.calls.mostRecent().args[0].allowSourceCodeReferences).toBeFalse();

      component.candidateAllowSourceCodeReferences = true;
      component.startBenchmark();
      expect(benchmarkServiceMock.startRun.calls.mostRecent().args[0].allowSourceCodeReferences).toBeTrue();
    });

    it('should identify failed claim verifications and trigger retry', () => {
      component.selectedRunDetail = {
        id: 55,
        status: 'Completed',
        answers: [
          { orderIndex: 1, claimVerificationError: null },
          { orderIndex: 2, claimVerificationError: 'Model timeout' }
        ]
      } as any;

      expect(component.claimVerificationFailedAnswerCount).toBe(1);
      expect(component.claimVerificationFailedQuestionNumbers).toBe('2');

      benchmarkServiceMock.retryClaimVerification.and.returnValue(of({ runId: 55 } as any));
      benchmarkServiceMock.getRun.and.returnValue(of({ id: 55, answers: [] } as any));

      component.openRetryDialog('claim-verification', 55);
      expect(component.retryScope).toBe('claim-verification');
      expect(component.retryRunId).toBe(55);

      component.confirmRetry();
      expect(benchmarkServiceMock.retryClaimVerification).toHaveBeenCalledWith(55, jasmine.anything());
      component.stopDetailPolling();
    });
  });

  // ---------------------------------------------------------------------------
  // U1. Tool routing, budget pressure, ungrounded Advanced answers and the
  // source-share correlations, all computed client-side from answer DTOs the run
  // detail dialog already holds. No endpoint backs any of it.
  // ---------------------------------------------------------------------------
  describe('run answer analytics (U1)', () => {
    /** Only the fields the four analytics read; everything else is filler the DTO demands. */
    function answer(overrides: Partial<BenchmarkRunAnswerDto> = {}): BenchmarkRunAnswerDto {
      return {
        id: 1,
        benchmarkRunId: 14,
        orderIndex: 1,
        questionText: 'Q',
        difficulty: 'Intermediate',
        assessedDifficulty: 55,
        answerText: 'A',
        status: 'Ok',
        durationMs: 20000,
        modelTimeMs: 15000,
        toolTimeMs: 5000,
        toolCallCount: 0,
        toolCallBudgetUsed: 45,
        toolBudgetExhausted: false,
        toolCallsBlocked: 0,
        toolCallSummary: null,
        qualityScore: 70,
        ...overrides
      } as BenchmarkRunAnswerDto;
    }

    function withAnswers(answers: BenchmarkRunAnswerDto[]): void {
      component.selectedRunDetail = { id: 14, answers } as any;
    }

    // --- Tool routing families ---

    it('should count tool calls by family and report each family share of the run', () => {
      withAnswers([
        answer({ orderIndex: 1, toolCallSummary: 'source_code_search×6, wiki_search×4' }),
        answer({
          orderIndex: 2,
          toolCallSummary: 'source_code_view×4, wiki_view×2, monster_lookup×3, get_knowledge_article×1'
        })
      ]);

      const rows = component.toolRoutingFamilies();

      expect(component.totalRoutedToolCalls()).toBe(20);
      expect(rows.map(r => r.label)).toEqual(['Source Code', 'Wiki', 'Structured Lookup', 'Knowledge Base']);
      expect(rows.map(r => r.count)).toEqual([10, 6, 3, 1]);
      expect(rows.map(r => r.sharePercentage)).toEqual([50, 30, 15, 5]);
    });

    it('should omit families with no calls, exactly as the report table does', () => {
      withAnswers([answer({ toolCallSummary: 'wiki_search×2' })]);

      const rows = component.toolRoutingFamilies();

      expect(rows.length).toBe(1);
      expect(rows[0].label).toBe('Wiki');
      expect(rows[0].sharePercentage).toBe(100);
    });

    it('should classify an unlisted tool as Other rather than dropping its calls', () => {
      withAnswers([answer({ toolCallSummary: 'wiki_search×3, some_new_tool×1' })]);

      const rows = component.toolRoutingFamilies();

      // Dropping it would make the shares sum to 100 % of a total that is not the run's.
      expect(component.totalRoutedToolCalls()).toBe(4);
      expect(rows.find(r => r.family === 'Other')?.count).toBe(1);
    });

    it('should route only answers the run actually produced', () => {
      withAnswers([
        answer({ orderIndex: 1, toolCallSummary: 'wiki_search×2' }),
        answer({ orderIndex: 2, status: 'Failed', toolCallSummary: 'source_code_search×9' })
      ]);

      expect(component.totalRoutedToolCalls()).toBe(2);
      expect(component.toolRoutingFamilies().map(r => r.family)).toEqual(['Wiki']);
    });

    // --- Budget pressure ---

    it('should select answers at or above 90 % of budget that never reached it', () => {
      withAnswers([
        answer({ orderIndex: 11, toolCallCount: 41, toolCallBudgetUsed: 45 }),
        answer({ orderIndex: 16, toolCallCount: 43, toolCallBudgetUsed: 45 }),
        answer({ orderIndex: 1, toolCallCount: 10, toolCallBudgetUsed: 45 })
      ]);

      expect(component.budgetPressuredAnswers().map(a => a.orderIndex)).toEqual([11, 16]);
    });

    it('should treat the 90 % threshold as inclusive and the budget itself as exclusive', () => {
      withAnswers([
        // 40.5 is the threshold: 40 is below it, 41 is not.
        answer({ orderIndex: 1, toolCallCount: 40, toolCallBudgetUsed: 45 }),
        answer({ orderIndex: 2, toolCallCount: 41, toolCallBudgetUsed: 45 }),
        // Reaching the budget is exhaustion, which the exhausted list already reports.
        answer({ orderIndex: 3, toolCallCount: 45, toolCallBudgetUsed: 45 })
      ]);

      expect(component.budgetPressuredAnswers().map(a => a.orderIndex)).toEqual([2]);
    });

    it('should exclude exhausted answers and answers with blocked calls', () => {
      withAnswers([
        answer({ orderIndex: 1, toolCallCount: 44, toolCallBudgetUsed: 45, toolBudgetExhausted: true }),
        // An answer whose calls were blocked is exhausted, not pressured.
        answer({ orderIndex: 2, toolCallCount: 44, toolCallBudgetUsed: 45, toolCallsBlocked: 2 }),
        answer({ orderIndex: 3, toolCallCount: 44, toolCallBudgetUsed: 45 })
      ]);

      expect(component.budgetPressuredAnswers().map(a => a.orderIndex)).toEqual([3]);
    });

    // --- Ungrounded Advanced answers ---

    it('should select Advanced answers produced with one tool call or fewer', () => {
      withAnswers([
        answer({ orderIndex: 14, assessedDifficulty: 85, toolCallCount: 1 }),
        answer({ orderIndex: 15, assessedDifficulty: 85, toolCallCount: 0 }),
        answer({ orderIndex: 16, assessedDifficulty: 85, toolCallCount: 5 }),
        answer({ orderIndex: 17, assessedDifficulty: 55, toolCallCount: 0 })
      ]);

      expect(component.ungroundedAdvancedAnswers().map(a => a.orderIndex)).toEqual([14, 15]);
    });

    it('should band an unrated answer by its authored band midpoint', () => {
      withAnswers([
        // No assessed difficulty: authored Advanced falls back to 85, which bands Advanced.
        answer({ orderIndex: 1, assessedDifficulty: null, difficulty: 'Advanced', toolCallCount: 1 }),
        answer({ orderIndex: 2, assessedDifficulty: null, difficulty: 'Intermediate', toolCallCount: 1 })
      ]);

      expect(component.ungroundedAdvancedAnswers().map(a => a.orderIndex)).toEqual([1]);
    });

    // --- Source-share correlations ---

    it('should pair the two correlations over one sample of scored answers', () => {
      withAnswers([
        answer({ orderIndex: 1, toolCallSummary: 'wiki_search×4', modelTimeMs: 10000, qualityScore: 50 }),
        answer({ orderIndex: 2, toolCallSummary: 'source_code_search×2, wiki_search×2', modelTimeMs: 20000, qualityScore: 70 }),
        answer({ orderIndex: 3, toolCallSummary: 'source_code_search×4', modelTimeMs: 30000, qualityScore: 50 })
      ]);

      const correlations = component.sourceShareCorrelations();

      // Shares 0, 0.5, 1 against times 10k, 20k, 30k are exactly linear.
      expect(correlations.modelTimeR).toBeCloseTo(1, 10);
      // The same shares against qualities 50, 70, 50 have zero covariance: this is run 14's shape,
      // where source calls bought time and not accuracy.
      expect(correlations.qualityR).toBeCloseTo(0, 10);
      expect(correlations.sampleSize).toBe(3);
    });

    it('should drop an unscored answer from both correlations, not from one', () => {
      withAnswers([
        answer({ orderIndex: 1, toolCallSummary: 'wiki_search×4', modelTimeMs: 10000, qualityScore: 50 }),
        answer({ orderIndex: 2, toolCallSummary: 'source_code_search×2, wiki_search×2', modelTimeMs: 20000, qualityScore: 70 }),
        answer({ orderIndex: 3, toolCallSummary: 'source_code_search×4', modelTimeMs: 30000, qualityScore: 50 }),
        answer({ orderIndex: 4, toolCallSummary: 'source_code_search×4', modelTimeMs: 99000, qualityScore: null })
      ]);

      const correlations = component.sourceShareCorrelations();

      expect(correlations.sampleSize).toBe(3);
      expect(correlations.modelTimeR).toBeCloseTo(1, 10);
    });

    it('should fall back to duration minus tool time when model time was never recorded', () => {
      withAnswers([
        answer({ orderIndex: 1, toolCallSummary: 'wiki_search×4', modelTimeMs: 0, durationMs: 15000, toolTimeMs: 5000, qualityScore: 50 }),
        answer({ orderIndex: 2, toolCallSummary: 'source_code_search×2, wiki_search×2', modelTimeMs: 0, durationMs: 25000, toolTimeMs: 5000, qualityScore: 70 }),
        answer({ orderIndex: 3, toolCallSummary: 'source_code_search×4', modelTimeMs: 0, durationMs: 35000, toolTimeMs: 5000, qualityScore: 50 })
      ]);

      const correlations = component.sourceShareCorrelations();

      // 10k, 20k, 30k again: leaving these out would shrink the sample silently instead.
      expect(correlations.sampleSize).toBe(3);
      expect(correlations.modelTimeR).toBeCloseTo(1, 10);
    });

    it('should report no coefficient where r is undefined rather than calling it zero', () => {
      withAnswers([answer({ toolCallSummary: 'wiki_search×2', qualityScore: 50 })]);

      const correlations = component.sourceShareCorrelations();

      expect(correlations.sampleSize).toBe(1);
      expect(correlations.modelTimeR).toBeNull();
      expect(correlations.qualityR).toBeNull();
    });
  });
  describe('Model Pricing Feature', () => {
    // U3. Four decimals throughout, so a sub-cent run resolves to something other than $0.00 and
    // every cost in the admin benchmark views lines up on the decimal point.
    it('should format every cost with four decimals', () => {
      expect(component.formatCostAmount(2.5312)).toBe('$2.5312');
      expect(component.formatCostAmount(1)).toBe('$1.0000');
      expect(component.formatCostAmount(0.9912)).toBe('$0.9912');
      expect(component.formatCostAmount(0.0004)).toBe('$0.0004');
    });

    it('should render a missing or non-finite cost as a dash rather than $0', () => {
      expect(component.formatCostAmount(null)).toBe('-');
      expect(component.formatCostAmount(undefined)).toBe('-');
      expect(component.formatCostAmount(Number.NaN)).toBe('-');
    });

    it('should render the Total Cost card with the incomplete-pricing marker when pricingIncomplete is true', () => {
      component.activeSubTab = 'run';
      component.selectedRunDetail = {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Test',
        status: 2,
        estimatedCost: 1.2345,
        pricingSource: 'catalog',
        pricingIncomplete: true,
        answers: []
      } as any;
      fixture.detectChanges();

      const cards = Array.from(fixture.nativeElement.querySelectorAll('.score-card')) as HTMLElement[];
      const card = cards.find(c => c.querySelector('.score-label')?.textContent?.trim() === 'Total Cost');
      expect(card).toBeTruthy();

      const content = card!.textContent?.replace(/\s+/g, ' ').trim() || '';
      expect(content).toContain('$1.2345');
      expect(content).toContain('no single total — a role has no price');

      const marker = card!.querySelector('.degraded-tag');
      expect(marker).toBeTruthy();
      expect(marker?.textContent?.trim()).toBe('*');
    });

    // H5. The summary row carries the same pair the run history Cost cell does: the model under
    // test first, the catalog total beside it.
    it('should render a Candidate Cost card with the candidate figure and its share of the total', () => {
      component.activeSubTab = 'run';
      component.selectedRunDetail = {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Test',
        status: 2,
        estimatedCost: 3.03,
        estimatedCandidateCost: 2.30,
        estimatedAssessorCost: 0.50,
        estimatedSecondOpinionCost: 0.10,
        estimatedVerifierCost: 0.10,
        estimatedSynthesisCost: 0.03,
        pricingSource: 'catalog',
        answers: []
      } as any;
      fixture.detectChanges();

      const cards = Array.from(fixture.nativeElement.querySelectorAll('.score-card')) as HTMLElement[];
      const card = cards.find(c => c.querySelector('.score-label')?.textContent?.trim() === 'Candidate Cost');
      expect(card).toBeTruthy();

      const content = card!.textContent?.replace(/\s+/g, ' ').trim() || '';
      expect(content).toContain('$2.3000');
      expect(content).toContain('76 % of estimated total');
      // No answer rows, so no per-question figure.
      expect(content).not.toContain('per question');
      expect(card!.querySelector('app-key-figure-card-actions')).toBeTruthy();
    });

    it('should add the candidate cost per question asked, over every answer row', () => {
      component.activeSubTab = 'run';
      component.selectedRunDetail = {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Test',
        status: 2,
        estimatedCost: 1.0,
        estimatedCandidateCost: 0.3978,
        estimatedAssessorCost: 0.6022,
        pricingSource: 'catalog',
        answers: Array.from({ length: 18 }, (_, i) => ({ id: i + 1, orderIndex: i + 1, status: i === 0 ? 'Error' : 'Ok' }))
      } as any;

      expect(component.candidateCostPerQuestionLabel(component.selectedRunDetail!)).toBe('$0.0221 per question · 18 asked');
    });

    it('should word the Total Cost note by pricing source', () => {
      const run = (overrides: any) => ({ id: 1, answers: [], ...overrides }) as any;
      expect(component.totalCostNote(run({ pricingSource: 'catalog' }))).toBe('estimated · all roles · catalog prices');
      expect(component.totalCostNote(run({ pricingSource: 'custom' }))).toBe('estimated · all roles · custom prices');
      expect(component.totalCostNote(run({ pricingSource: 'mixed' }))).toBe('estimated · all roles · catalog and custom prices');
      expect(component.totalCostNote(run({ pricingSource: null }))).toBe('pricing unknown');
      expect(component.totalCostNote(run({ pricingSource: 'mixed', pricingIncomplete: true }))).toBe('no single total — a role has no price');
    });

    it('should give the share of the priced roles when pricing is incomplete', () => {
      const run = {
        id: 1,
        estimatedCost: null,
        estimatedCandidateCost: 1.0,
        estimatedAssessorCost: 3.0,
        pricingIncomplete: true,
        answers: []
      } as any;
      expect(component.candidateCostShareLabel(run)).toBe('25 % of the priced roles');
    });

    // H4. The card and the cost panel below it apportion the same five role amounts, in the same
    // order, by the same largest-remainder rule, so they can never disagree on the whole percent.
    it('should print the same apportioned percent as the cost panel below it', () => {
      component.activeSubTab = 'run';
      component.selectedRunDetail = {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Test',
        status: 2,
        estimatedCost: 2.7247,
        estimatedCandidateCost: 0.6926,
        estimatedAssessorCost: 0.80,
        estimatedSecondOpinionCost: 0.10,
        estimatedVerifierCost: 1.10,
        estimatedSynthesisCost: 0.0321,
        estimatedGradingCost: 2.0321,
        pricingSource: 'Anthropic API',
        answers: []
      } as any;
      fixture.detectChanges();

      // Exact shares: candidate 25.42, assessor 29.36, second opinion 3.67, claim verifier 40.37,
      // synthesis 1.18 -- the floors sum to 98, so the two largest remainders (second opinion,
      // then candidate) each get one extra point, giving the candidate 26 %.
      const cards = Array.from(fixture.nativeElement.querySelectorAll('.score-card')) as HTMLElement[];
      const card = cards.find(c => c.querySelector('.score-label')?.textContent?.trim() === 'Candidate Cost');
      expect(card!.textContent).toContain('26 % of estimated total');

      const panelShares = Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
          '.gh-cost-role:not(.gh-cost-role--subtotal) .gh-cost-role__share'
        )
      ).map(el => el.textContent?.trim());
      expect(panelShares[0]).toBe('26%');
    });

    it('should omit the Candidate Cost card when the candidate cost was never recorded', () => {
      component.activeSubTab = 'run';
      component.selectedRunDetail = {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Test',
        status: 2,
        estimatedCost: 3.03,
        estimatedCandidateCost: null,
        pricingSource: 'Anthropic API',
        answers: []
      } as any;
      fixture.detectChanges();

      const labels = (Array.from(fixture.nativeElement.querySelectorAll('.score-card')) as HTMLElement[])
        .map(c => c.querySelector('.score-label')?.textContent?.trim());
      expect(labels).not.toContain('Candidate Cost');
      expect(labels).toContain('Total Cost');
    });

    it('should say pricing incomplete in words under the run history cost, with no asterisk and no title', () => {
      component.activeSubTab = 'history';
      component.historyRuns = [
        {
          id: 10,
          suiteName: 'Test Suite',
          status: 2,
          estimatedCost: 0.50,
          pricingIncomplete: true
        } as any
      ];
      fixture.detectChanges();

      const metric = fixture.nativeElement.querySelector('.rh-card .rh-metric[data-metric="cost"]') as HTMLElement;
      expect(metric).toBeTruthy();
      expect(metric.querySelector('dt')?.textContent?.trim()).toBe('Cost');

      const lines = Array.from(metric.querySelectorAll('dd > span')).map(span => span.textContent?.trim());
      expect(lines).toEqual(['$0.5000', 'catalog $0.5000', 'pricing incomplete']);
      expect(metric.textContent).not.toContain('*');
      expect(metric.querySelector('[title]')).toBeNull();
      expect(metric.querySelector('.degraded-tag')).toBeNull();
    });
  });

  describe('run setting recall', () => {
    /**
     * The fixture's own configuration is id 1 with modelRole 7 (Chat + Title + Benchmark), so it is the
     * only benchmark-capable configuration unless a spec adds another.
     */
    const secondConfig = (id: number) => ({ ...component.systemConfigs[0], id, displayName: `Model ${id}` });

    it('should write the run settings to localStorage when a run is started', () => {
      benchmarkServiceMock.startRun.and.returnValue(of({ runId: 99 }));
      component.systemConfigs = [component.systemConfigs[0], secondConfig(2)];
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 2;
      component.secondOpinionConfigId = 2;
      component.claimVerifierConfigId = 1;
      component.selectedScoringProfileId = 1;
      component.candidateVerboseMode = true;

      component.startBenchmark();

      const stored = JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!);
      expect(stored.suiteId).toBe(1);
      expect(stored.testedConfigId).toBe(1);
      expect(stored.assessorConfigId).toBe(2);
      expect(stored.secondOpinionConfigId).toBe(2);
      expect(stored.claimVerifierConfigId).toBe(1);
      expect(stored.scoringProfileId).toBe(1);
      expect(stored.verboseMode).toBeTrue();
      expect(stored.allowSourceCodeReferences).toBeFalse();
      expect(stored.runCount).toBe(1);
      component.ngOnDestroy();
    });

    it('should remember Source Code References, and start a stored setup without it at Disallowed', () => {
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({ suiteId: 1, allowSourceCodeReferences: true }));
      const allowed = TestBed.createComponent(AdminBenchmarkComponent);
      allowed.componentInstance.systemConfigs = [component.systemConfigs[0]];
      allowed.detectChanges();
      expect(allowed.componentInstance.candidateAllowSourceCodeReferences).toBeTrue();
      allowed.componentInstance.ngOnDestroy();

      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({ suiteId: 1, verboseMode: true }));
      const older = TestBed.createComponent(AdminBenchmarkComponent);
      older.componentInstance.systemConfigs = [component.systemConfigs[0]];
      older.detectChanges();
      expect(older.componentInstance.candidateAllowSourceCodeReferences).toBeFalse();
      older.componentInstance.ngOnDestroy();
    });

    it('should restore every remembered selection on the next construction', () => {
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1,
        testedConfigId: 2,
        assessorConfigId: 1,
        secondOpinionConfigId: 2,
        claimVerifierConfigId: 1,
        secondOpinionMode: 3,
        scoringProfileId: 1,
        verboseMode: true,
        runCount: 5
      }));

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0], secondConfig(2)];
      restored.detectChanges();

      const c = restored.componentInstance;
      expect(c.selectedSuiteId).toBe(1);
      expect(c.testedConfigId).toBe(2);
      expect(c.assessorConfigId).toBe(1);
      expect(c.secondOpinionConfigId).toBe(2);
      expect(c.claimVerifierConfigId).toBe(1);
      expect(c.secondOpinionMode).toBe(3);
      expect(c.selectedScoringProfileId).toBe(1);
      expect(c.candidateVerboseMode).toBeTrue();
      expect(c.runCount).toBe(5);
      c.ngOnDestroy();
    });

    it('should fall back to default runCount of 1 when stored runCount is invalid or non-positive', () => {
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1,
        testedConfigId: 1,
        assessorConfigId: 1,
        runCount: -3
      }));

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
      restored.detectChanges();

      expect(restored.componentInstance.runCount).toBe(1);
      restored.componentInstance.ngOnDestroy();
    });

    it('should clamp remembered runCount when loadRunLimits receives a lower maxRunCountPerSeries', () => {
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1,
        testedConfigId: 1,
        assessorConfigId: 1,
        runCount: 25
      }));

      benchmarkServiceMock.getRunLimits.and.returnValue(of({
        maxRunsPerHour: 4,
        maxRunsPerDay: 20,
        runsInLastHour: 0,
        runsInLast24Hours: 0,
        remainingDailyHeadroom: 20,
        maxRunCountPerSeries: 10
      }));

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
      restored.detectChanges();

      expect(restored.componentInstance.runCount).toBe(10);
      restored.componentInstance.ngOnDestroy();
    });

    it('should fall back to the default when a remembered configuration is no longer benchmark-capable', () => {
      // Id 7 is not in systemConfigs at all, which is what a disabled configuration, one whose key was
      // removed, or one that lost its Benchmark role looks like to this screen.
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1,
        testedConfigId: 7,
        assessorConfigId: 7,
        secondOpinionConfigId: 7,
        claimVerifierConfigId: 7,
        secondOpinionMode: null,
        scoringProfileId: 1,
        verboseMode: false
      }));

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
      restored.detectChanges();

      const c = restored.componentInstance;
      expect(c.testedConfigId).toBe(1);
      expect(c.assessorConfigId).toBe(1);
      // The optional roles restore to "not selected" rather than to a dangling id.
      expect(c.secondOpinionConfigId).toBeNull();
      expect(c.claimVerifierConfigId).toBeNull();
      c.ngOnDestroy();
    });

    it('should fall back to the first suite when the remembered suite no longer exists', () => {
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 999, testedConfigId: null, assessorConfigId: null,
        secondOpinionConfigId: null, claimVerifierConfigId: null,
        secondOpinionMode: null, scoringProfileId: 999, verboseMode: null
      }));

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
      restored.detectChanges();

      expect(restored.componentInstance.selectedSuiteId).toBe(1);
      expect(restored.componentInstance.selectedScoringProfileId).toBe(1);
      restored.componentInstance.ngOnDestroy();
    });

    it('should leave every default untouched when localStorage throws', () => {
      spyOn(localStorage, 'getItem').and.throwError('SecurityError');

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];

      expect(() => restored.detectChanges()).not.toThrow();
      expect(restored.componentInstance.selectedSuiteId).toBe(1);
      expect(restored.componentInstance.testedConfigId).toBe(1);
      expect(restored.componentInstance.candidateVerboseMode).toBeFalse();
      restored.componentInstance.ngOnDestroy();
    });

    it('should not remember the same-provider acknowledgement', () => {
      benchmarkServiceMock.startRun.and.returnValue(of({ runId: 99 }));
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 1;

      component.startBenchmark(true);

      const stored = JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!);
      // A per-run safety acknowledgement: remembering it would silently defeat the warning dialog.
      expect(stored.acknowledgeSameProvider).toBeUndefined();
      component.ngOnDestroy();
    });

    it('should send and remember the report writer, and restore it while it still qualifies', () => {
      benchmarkServiceMock.startRun.and.returnValue(of({ runId: 99 }));
      const writer = { ...secondConfig(2), provider: 'OpenAI', modelId: 'gpt-writer' };
      component.systemConfigs = [component.systemConfigs[0], writer];
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 1;
      component.reportWriterConfigId = 2;

      component.startBenchmark();

      expect(benchmarkServiceMock.startRun.calls.mostRecent().args[0].reportWriterModelConfigurationId).toBe(2);
      expect(JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!).reportWriterConfigId).toBe(2);
      component.ngOnDestroy();

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0], writer];
      restored.detectChanges();
      expect(restored.componentInstance.reportWriterConfigId).toBe(2);
      restored.componentInstance.ngOnDestroy();

      // A writer that no longer qualifies restores to "None".
      const dropped = TestBed.createComponent(AdminBenchmarkComponent);
      dropped.componentInstance.systemConfigs = [component.systemConfigs[0]];
      dropped.detectChanges();
      expect(dropped.componentInstance.reportWriterConfigId).toBeNull();
      dropped.componentInstance.ngOnDestroy();
    });

    it('should default completionSound to true before anything is remembered', () => {
      expect(component.completionSound).toBeTrue();
    });

    it('should persist completionSound when a run is started', () => {
      benchmarkServiceMock.startRun.and.returnValue(of({ runId: 99 }));
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 1;
      component.completionSound = false;

      component.startBenchmark();

      const stored = JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!);
      expect(stored.completionSound).toBeFalse();
      component.ngOnDestroy();
    });

    it('should restore a remembered completionSound value on the next construction', () => {
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1, testedConfigId: 1, assessorConfigId: 1,
        secondOpinionConfigId: null, claimVerifierConfigId: null,
        secondOpinionMode: null, scoringProfileId: 1, verboseMode: null,
        completionSound: false
      }));

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
      restored.detectChanges();

      expect(restored.componentInstance.completionSound).toBeFalse();
      restored.componentInstance.ngOnDestroy();
    });

    it('should default completionSound to true when the stored blob predates the field', () => {
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1, testedConfigId: 1, assessorConfigId: 1,
        secondOpinionConfigId: null, claimVerifierConfigId: null,
        secondOpinionMode: null, scoringProfileId: 1, verboseMode: null
      }));

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
      restored.detectChanges();

      expect(restored.componentInstance.completionSound).toBeTrue();
      restored.componentInstance.ngOnDestroy();
    });
  });

  describe('completion sound transition detection', () => {
    let playSpy: jasmine.Spy;

    function makeRun(overrides: Record<string, unknown> = {}): any {
      return {
        id: 42,
        benchmarkSuiteId: 1,
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Test Model',
        testedModelProviderUsed: 'Anthropic',
        testedModelIdUsed: 'claude-3-5-sonnet',
        assessorModelDisplayNameUsed: 'Test Assessor',
        assessorModelProviderUsed: 'Anthropic',
        assessorModelIdUsed: 'claude-3-5-sonnet',
        startedByUserName: 'admin',
        status: 'Running',
        startedAtUtc: '2026-09-02T00:00:00Z',
        completedAtUtc: null,
        totalQuestionCount: 3,
        answers: [],
        ...overrides
      };
    }

    beforeEach(() => {
      const soundService = TestBed.inject(BenchmarkCompletionSoundService);
      playSpy = spyOn(soundService, 'play').and.returnValue(Promise.resolve('played'));
    });

    it('should chime once for a run seen Running and then reaching a terminal status', () => {
      benchmarkServiceMock.getRun.and.returnValue(of(makeRun({ id: 42, status: 'Running' })));
      (component as any).pollRunDetail(42);
      expect(playSpy).not.toHaveBeenCalled();

      benchmarkServiceMock.getRun.and.returnValue(of(makeRun({ id: 42, status: 'Completed' })));
      (component as any).pollRunDetail(42);
      expect(playSpy).toHaveBeenCalledWith('run:42');
      expect(playSpy).toHaveBeenCalledTimes(1);

      // A later poll of the same, already-terminal run must not chime a second time.
      (component as any).pollRunDetail(42);
      expect(playSpy).toHaveBeenCalledTimes(1);
    });

    it('should not chime for a run first observed already terminal', () => {
      benchmarkServiceMock.getRun.and.returnValue(of(makeRun({ id: 42, status: 'Completed' })));
      (component as any).pollRunDetail(42);

      expect(playSpy).not.toHaveBeenCalled();
    });

    it('should not chime individually for a run that is a member of a still-live series', () => {
      component.activeSeries = { id: 7, status: 'Running', members: [] } as any;
      component.activeSeriesId = 7;

      benchmarkServiceMock.getRun.and.returnValue(of(makeRun({ id: 42, status: 'Running' })));
      (component as any).pollRunDetail(42);

      benchmarkServiceMock.getRun.and.returnValue(of(makeRun({ id: 42, status: 'Completed' })));
      (component as any).pollRunDetail(42);

      expect(playSpy).not.toHaveBeenCalledWith('run:42');
    });

    it('should chime once for a series seen live and then reaching a terminal status', () => {
      benchmarkServiceMock.getRunSeries.and.returnValue(of({
        id: 8, status: 'Running', completedRunCount: 0, requestedRunCount: 2, members: []
      } as any));
      (component as any).pollSeries(8);
      expect(playSpy).not.toHaveBeenCalled();

      benchmarkServiceMock.getRunSeries.and.returnValue(of({
        id: 8, status: 'Completed', completedRunCount: 2, requestedRunCount: 2, members: []
      } as any));
      (component as any).pollSeries(8);
      expect(playSpy).toHaveBeenCalledWith('series:8');
      expect(playSpy).toHaveBeenCalledTimes(1);

      // A later poll of the same, already-finished series must not chime a second time.
      (component as any).pollSeries(8);
      expect(playSpy).toHaveBeenCalledTimes(1);
    });

    it('should not chime for a series first observed already finished', () => {
      benchmarkServiceMock.getRunSeries.and.returnValue(of({
        id: 8, status: 'Completed', completedRunCount: 2, requestedRunCount: 2, members: []
      } as any));
      (component as any).pollSeries(8);

      expect(playSpy).not.toHaveBeenCalled();
    });

    describe('cancellation', () => {
      let notifySpy: jasmine.Spy;

      function series(status: string, members: { runId: number }[] = []): any {
        return { id: 8, status, completedRunCount: 1, requestedRunCount: 2, members };
      }

      function seeRunLive(id = 42): void {
        benchmarkServiceMock.getRun.and.returnValue(of(makeRun({ id, status: 'Running' })));
        (component as any).pollRunDetail(id);
      }

      function pollRun(id: number, status: string | number): void {
        benchmarkServiceMock.getRun.and.returnValue(of(makeRun({ id, status })));
        (component as any).pollRunDetail(id);
      }

      beforeEach(() => {
        const notificationService = TestBed.inject(BenchmarkCompletionNotificationService);
        notifySpy = spyOn(notificationService, 'notify');
        spyOn(notificationService, 'permission').and.returnValue('granted');
        component.completionSound = true;
        component.completionNotification = true;
        spyOnProperty(document, 'hidden', 'get').and.returnValue(true);
        spyOn(document, 'hasFocus').and.returnValue(false);
      });

      it('does not chime or notify for a run that ends Canceled', () => {
        seeRunLive();
        pollRun(42, 'Canceled');

        expect(playSpy).not.toHaveBeenCalled();
        expect(notifySpy).not.toHaveBeenCalled();
      });

      it('does not chime for a run whose numeric status is Canceled', () => {
        seeRunLive();
        pollRun(42, 5);

        expect(playSpy).not.toHaveBeenCalled();
      });

      it('does not chime for a run cancelled here that the server returns to Completed', () => {
        seeRunLive();
        component.activeRunId = 42;
        benchmarkServiceMock.cancelRun.and.returnValue(of({ success: true }));
        benchmarkServiceMock.getRun.and.returnValue(of(makeRun({ id: 42, status: 'Completed' })));

        component.cancelActiveRun();

        expect(benchmarkServiceMock.cancelRun).toHaveBeenCalledWith(42);
        expect(playSpy).not.toHaveBeenCalled();
        expect(notifySpy).not.toHaveBeenCalled();
      });

      it('still chimes when the cancel request fails and the run then completes', () => {
        spyOn(console, 'error');
        seeRunLive();
        component.activeRunId = 42;
        benchmarkServiceMock.cancelRun.and.returnValue(throwError(() => ({ status: 500 })));

        component.cancelActiveRun();
        pollRun(42, 'Completed');

        expect(playSpy).toHaveBeenCalledOnceWith('run:42');
      });

      it('does not chime for a run cancelled from the run detail view', () => {
        seeRunLive();
        benchmarkServiceMock.cancelRun.and.returnValue(new Subject<{ success: boolean }>());

        component.cancelRunById(42);
        pollRun(42, 'Completed');

        expect(playSpy).not.toHaveBeenCalled();
      });

      it('chimes for a failed-question re-run launched after a cancel of the same run', () => {
        seeRunLive();
        benchmarkServiceMock.cancelRun.and.returnValue(new Subject<{ success: boolean }>());
        component.cancelRunById(42);

        benchmarkServiceMock.rerunFailedQuestions.and.returnValue(of({ runId: 42 }));
        benchmarkServiceMock.getRun.and.returnValue(of(makeRun({ id: 42, status: 'Running' })));
        (component as any).launchFailedQuestionRerun(42, [0]);
        pollRun(42, 'Completed');

        expect(playSpy).toHaveBeenCalledOnceWith('run:42');
        component.closeRunProgressDialog();
      });

      it('does not chime or notify for a series that ends Cancelled, then or later', () => {
        benchmarkServiceMock.getRunSeries.and.returnValue(of(series('Running')));
        (component as any).pollSeries(8);
        benchmarkServiceMock.getRunSeries.and.returnValue(of(series('Cancelled')));
        (component as any).pollSeries(8);
        (component as any).pollSeries(8);

        expect(playSpy).not.toHaveBeenCalled();
        expect(notifySpy).not.toHaveBeenCalled();
      });

      it('does not chime for a member that completes as its series is cancelled', () => {
        seeRunLive();
        component.activeSeries = series('Cancelled', [{ runId: 42 }]);
        component.activeSeriesId = 8;

        pollRun(42, 'Completed');

        expect(playSpy).not.toHaveBeenCalled();
      });

      it('still chimes for a run that ends Failed', () => {
        seeRunLive();
        pollRun(42, 'Failed');

        expect(playSpy).toHaveBeenCalledOnceWith('run:42');
        expect(notifySpy).toHaveBeenCalledTimes(1);
      });

      for (const status of ['Stopped', 'Failed']) {
        it(`still chimes for a series that ends ${status}`, () => {
          benchmarkServiceMock.getRunSeries.and.returnValue(of(series('Running')));
          (component as any).pollSeries(8);
          benchmarkServiceMock.getRunSeries.and.returnValue(of(series(status)));
          (component as any).pollSeries(8);

          expect(playSpy).toHaveBeenCalledOnceWith('series:8');
        });
      }
    });
  });

  describe('Part C: arming the completion signals from a user gesture', () => {
    let armSpy: jasmine.Spy;

    function buildRun(overrides: Record<string, unknown> = {}): any {
      return {
        id: 37, benchmarkSuiteId: 1, suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Test Model', testedModelProviderUsed: 'Anthropic', testedModelIdUsed: 'x',
        assessorModelDisplayNameUsed: 'Test Assessor', assessorModelProviderUsed: 'Anthropic', assessorModelIdUsed: 'x',
        startedByUserName: 'admin', status: 'CompletedWithErrors', startedAtUtc: '2026-09-02T00:00:00Z',
        completedAtUtc: null, totalQuestionCount: 3, answers: [], ...overrides
      };
    }

    beforeEach(() => {
      const soundService = TestBed.inject(BenchmarkCompletionSoundService);
      armSpy = spyOn(soundService, 'arm').and.returnValue(Promise.resolve());
    });

    it('arms from startBenchmark', () => {
      benchmarkServiceMock.startRun.and.returnValue(of({ runId: 99 }));
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 1;

      component.startBenchmark();

      expect(armSpy).toHaveBeenCalledTimes(1);
      component.ngOnDestroy();
    });

    it('arms from resumeActiveSeries', () => {
      component.activeSeriesId = 5;
      benchmarkServiceMock.resumeRunSeries.and.returnValue(of({} as any));
      benchmarkServiceMock.getRunSeries.and.returnValue(of({
        id: 5, status: 'Running', completedRunCount: 0, requestedRunCount: 2, members: []
      } as any));

      component.resumeActiveSeries();

      expect(armSpy).toHaveBeenCalledTimes(1);
      component.ngOnDestroy();
    });

    it('arms from rerunFailedFromProgress', () => {
      component.activeRunDetail = buildRun({ id: 37, answers: [] });
      benchmarkServiceMock.rerunFailedQuestions.and.returnValue(of({ runId: 37 }));
      benchmarkServiceMock.getRun.and.returnValue(of(buildRun({ id: 37 })));

      component.rerunFailedFromProgress();

      expect(armSpy).toHaveBeenCalledTimes(1);
      component.closeRunProgressDialog();
    });

    it('arms from rerunFailedFromRunDetail', () => {
      component.selectedRunDetail = buildRun({ id: 37, answers: [] });
      benchmarkServiceMock.rerunFailedQuestions.and.returnValue(of({ runId: 37 }));
      benchmarkServiceMock.getRun.and.returnValue(of(buildRun({ id: 37 })));

      component.rerunFailedFromRunDetail(37);

      expect(armSpy).toHaveBeenCalledTimes(1);
      component.closeRunProgressDialog();
    });

    it('arms from testCompletionSound, before priming', () => {
      const soundService = TestBed.inject(BenchmarkCompletionSoundService);
      const primeSpy = spyOn(soundService, 'prime').and.returnValue(Promise.resolve('played'));

      component.testCompletionSound();

      expect(armSpy).toHaveBeenCalledTimes(1);
      expect(primeSpy).toHaveBeenCalledTimes(1);
    });

    it('does nothing when neither signal is enabled', () => {
      component.completionSound = false;
      component.completionNotification = false;
      benchmarkServiceMock.startRun.and.returnValue(of({ runId: 99 }));
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 1;

      component.startBenchmark();

      expect(armSpy).not.toHaveBeenCalled();
      component.ngOnDestroy();
    });
  });

  describe('Part C: the desktop notification', () => {
    let notificationService: BenchmarkCompletionNotificationService;
    let notifySpy: jasmine.Spy;
    let permissionSpy: jasmine.Spy;

    function buildRun(overrides: Record<string, unknown> = {}): any {
      return {
        id: 42, benchmarkSuiteId: 1, suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Test Model', testedModelProviderUsed: 'Anthropic', testedModelIdUsed: 'x',
        assessorModelDisplayNameUsed: 'Test Assessor', assessorModelProviderUsed: 'Anthropic', assessorModelIdUsed: 'x',
        startedByUserName: 'admin', status: 'Running', startedAtUtc: '2026-09-02T00:00:00Z',
        completedAtUtc: null, totalQuestionCount: 3, answers: [], ...overrides
      };
    }

    beforeEach(() => {
      notificationService = TestBed.inject(BenchmarkCompletionNotificationService);
      notifySpy = spyOn(notificationService, 'notify');
      permissionSpy = spyOn(notificationService, 'permission').and.returnValue('granted');
      spyOn(TestBed.inject(BenchmarkCompletionSoundService), 'play').and.returnValue(Promise.resolve('played'));
      spyOn(TestBed.inject(BenchmarkCompletionSoundService), 'arm').and.returnValue(Promise.resolve());
    });

    describe('when Start is pressed with the box ticked', () => {
      it('prompts under the gesture when this browser has not decided, and keeps the box on a grant', fakeAsync(() => {
        permissionSpy.and.returnValue('default');
        const requestSpy = spyOn(notificationService, 'requestPermission').and.returnValue(Promise.resolve('granted'));
        component.completionNotification = true;

        (component as any).armCompletionSignalsFromGesture();
        expect(requestSpy).toHaveBeenCalledTimes(1);
        tick();

        expect(component.completionNotification).toBeTrue();
        expect(component.completionNotificationStatus).toBeNull();
      }));

      it('unticks the box with the reason when the Start prompt is dismissed', fakeAsync(() => {
        permissionSpy.and.returnValue('default');
        spyOn(notificationService, 'requestPermission').and.returnValue(Promise.resolve('default'));
        component.completionNotification = true;

        (component as any).armCompletionSignalsFromGesture();
        tick();

        expect(component.completionNotification).toBeFalse();
        expect(component.completionNotificationStatus).toBe('The permission prompt was dismissed.');
      }));

      it('does not prompt again once permission was granted', () => {
        const requestSpy = spyOn(notificationService, 'requestPermission');
        component.completionNotification = true;

        (component as any).armCompletionSignalsFromGesture();

        expect(requestSpy).not.toHaveBeenCalled();
        expect(component.completionNotification).toBeTrue();
      });

      it('does not prompt when blocked, and unticks the box with the reason at once', () => {
        permissionSpy.and.returnValue('denied');
        const requestSpy = spyOn(notificationService, 'requestPermission');
        component.completionNotification = true;

        (component as any).armCompletionSignalsFromGesture();

        expect(requestSpy).not.toHaveBeenCalled();
        expect(component.completionNotification).toBeFalse();
        expect(component.completionNotificationStatus).toBe("Notifications are blocked for this site in the browser's settings.");
      });

      it('never prompts when the box is unticked', () => {
        permissionSpy.and.returnValue('default');
        const requestSpy = spyOn(notificationService, 'requestPermission');
        component.completionSound = true;
        component.completionNotification = false;

        (component as any).armCompletionSignalsFromGesture();

        expect(requestSpy).not.toHaveBeenCalled();
      });
    });

    it('takes the empty status line out of the layout, and puts it back when it has a message', fakeAsync(() => {
      fixture.detectChanges();
      const status = fixture.nativeElement.querySelector('.completion-signals-status') as HTMLElement;
      expect(status.classList).toContain('is-empty');
      expect(status.getAttribute('role')).toBe('status');
      expect(getComputedStyle(status).position).toBe('absolute');
      expect(getComputedStyle(status).marginTop).toBe('0px');
      const toggle = fixture.nativeElement.querySelector('label[for="completionNotificationInput"]') as HTMLElement;
      const testSound = Array.from(fixture.nativeElement.querySelectorAll('.completion-signals button.btn-gh-small') as NodeListOf<HTMLElement>)
        .find(b => b.textContent?.trim() === 'Test sound') as HTMLElement;
      expect(toggle.getBoundingClientRect().height).toBeGreaterThan(0);
      expect(testSound.getBoundingClientRect().height).toBeGreaterThan(0);
      const gap = testSound.getBoundingClientRect().top - toggle.getBoundingClientRect().bottom;
      expect(gap).toBeLessThanOrEqual(12);

      spyOn(notificationService, 'requestPermission').and.returnValue(Promise.resolve('default'));
      component.onCompletionNotificationChange(true);
      tick();

      expect(status.classList).not.toContain('is-empty');
      expect(getComputedStyle(status).position).toBe('static');
      expect(status.textContent).toContain('The permission prompt was dismissed.');
    }));

    it('persists and restores completionNotification alongside completionSound', () => {
      benchmarkServiceMock.startRun.and.returnValue(of({ runId: 99 }));
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 1;
      component.completionNotification = true;

      component.startBenchmark();

      const stored = JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!);
      expect(stored.completionNotification).toBeTrue();
      component.ngOnDestroy();

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
      restored.detectChanges();
      expect(restored.componentInstance.completionNotification).toBeTrue();
      restored.componentInstance.ngOnDestroy();
    });

    it('turns on and clears the status once permission is granted', fakeAsync(() => {
      spyOn(notificationService, 'requestPermission').and.returnValue(Promise.resolve('granted'));

      component.onCompletionNotificationChange(true);
      tick();

      expect(component.completionNotification).toBeTrue();
      expect(component.completionNotificationStatus).toBeNull();
    }));

    (['denied', 'default', 'unsupported'] as const).forEach(outcome => {
      it(`unticks the box and explains a "${outcome}" permission result`, fakeAsync(() => {
        spyOn(notificationService, 'requestPermission').and.returnValue(Promise.resolve(outcome));

        component.onCompletionNotificationChange(true);
        tick();
        fixture.detectChanges();

        expect(component.completionNotification).toBeFalse();
        expect(component.completionNotificationStatus).toBeTruthy();
        const status = fixture.nativeElement.querySelector('.completion-signals-status') as HTMLElement;
        expect(status.textContent).toContain(component.completionNotificationStatus);
      }));
    });

    it('unticking directly clears the status without requesting permission', () => {
      const requestSpy = spyOn(notificationService, 'requestPermission');
      component.completionNotification = true;
      component.completionNotificationStatus = 'stale';

      component.onCompletionNotificationChange(false);

      expect(component.completionNotification).toBeFalse();
      expect(component.completionNotificationStatus).toBeNull();
      expect(requestSpy).not.toHaveBeenCalled();
    });

    it('notifies once for a hidden completion with the sound off and the notification on', () => {
      component.completionSound = false;
      component.completionNotification = true;
      spyOnProperty(document, 'hidden', 'get').and.returnValue(true);
      spyOn(document, 'hasFocus').and.returnValue(false);

      benchmarkServiceMock.getRun.and.returnValue(of(buildRun({ id: 42, status: 'Running' })));
      (component as any).pollRunDetail(42);
      benchmarkServiceMock.getRun.and.returnValue(of(buildRun({ id: 42, status: 'Completed' })));
      (component as any).pollRunDetail(42);

      expect(notifySpy).toHaveBeenCalledTimes(1);
      expect(notifySpy.calls.mostRecent().args[0]).toBe('run:42');
    });

    it('notifies a visible, focused completion too — the ticked box no longer checks focus', () => {
      component.completionNotification = true;
      spyOnProperty(document, 'hidden', 'get').and.returnValue(false);
      spyOn(document, 'hasFocus').and.returnValue(true);

      benchmarkServiceMock.getRun.and.returnValue(of(buildRun({ id: 42, status: 'Running' })));
      (component as any).pollRunDetail(42);
      benchmarkServiceMock.getRun.and.returnValue(of(buildRun({ id: 42, status: 'Completed' })));
      (component as any).pollRunDetail(42);

      expect(notifySpy).toHaveBeenCalledTimes(1);
      expect(notifySpy.calls.mostRecent().args[0]).toBe('run:42');
    });

    it('records a notification attempt with the tab focused, surfaced in diagnostics', () => {
      component.completionNotification = true;
      spyOnProperty(document, 'hidden', 'get').and.returnValue(false);
      spyOn(document, 'hasFocus').and.returnValue(true);
      notifySpy.and.returnValue('shown');

      benchmarkServiceMock.getRun.and.returnValue(of(buildRun({ id: 42, status: 'Running' })));
      (component as any).pollRunDetail(42);
      benchmarkServiceMock.getRun.and.returnValue(of(buildRun({ id: 42, status: 'Completed' })));
      (component as any).pollRunDetail(42);

      const attempts = (component as any).notificationAttempts;
      expect(attempts.length).toBe(1);
      expect(attempts[0].key).toBe('run:42');
      expect(attempts[0].hidden).toBeFalse();
      expect(attempts[0].focused).toBeTrue();
      expect(attempts[0].outcome).toBe('shown');

      const diagnostics = component.runDiagnosticsText;
      expect(diagnostics).toContain('key=run:42 hidden=false focused=true outcome=shown');
    });
  });

  describe('Part C: the second series-resume path and the Web Lock', () => {
    it('restarts the series poll when the progress dialog resumes a stopped series', () => {
      benchmarkServiceMock.getRunSeries.and.returnValue(of({
        id: 11, status: 'Running', completedRunCount: 1, requestedRunCount: 3, members: []
      } as any));

      component.onSeriesResumedFromDialog(11);

      expect(component.activeSeriesId).toBe(11);
      expect(benchmarkServiceMock.getRunSeries).toHaveBeenCalledWith(11);
      component.ngOnDestroy();
    });

    it('does not release the background lock on a single run poll error, and stays polling', () => {
      const consoleError = spyOn(console, 'error');
      const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
      const releaseSpy = spyOn(lockService, 'release');
      const acquireSpy = spyOn(lockService, 'acquireForRun');

      // startPolling's own poll is the first failure; its stopPolling() of any previous poller
      // may release, so the spy is reset once polling has started.
      benchmarkServiceMock.getRun.and.returnValue(throwError(() => ({ status: 500 })));
      (component as any).startPolling(42);
      expect(acquireSpy).toHaveBeenCalledWith(42);
      releaseSpy.calls.reset();

      (component as any).pollRunDetail(42);

      expect(releaseSpy).not.toHaveBeenCalled();
      expect((component as any).pollTickerHandle).not.toBeNull();
      expect(consoleError).toHaveBeenCalledWith('Failed to poll run detail', jasmine.any(Object));
      (component as any).stopPolling();
      component.ngOnDestroy();
    });

    it('releases the background lock only on the 5th consecutive run poll error', () => {
      const consoleError = spyOn(console, 'error');
      const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
      const releaseSpy = spyOn(lockService, 'release');

      benchmarkServiceMock.getRun.and.returnValue(throwError(() => ({ status: 500 })));
      (component as any).startPolling(42);
      releaseSpy.calls.reset();

      // startPolling's own poll was the first failure; three more make four.
      for (let i = 0; i < 3; i++) {
        (component as any).pollRunDetail(42);
      }
      expect(releaseSpy).not.toHaveBeenCalled();
      expect((component as any).pollTickerHandle).not.toBeNull();

      (component as any).pollRunDetail(42);
      expect(releaseSpy).toHaveBeenCalled();
      expect((component as any).pollTickerHandle).toBeNull();
      expect(consoleError).toHaveBeenCalledWith('Failed to poll run detail', jasmine.any(Object));
      component.ngOnDestroy();
    });

    it('resets the consecutive run poll failure count on a successful poll', () => {
      const consoleError = spyOn(console, 'error');
      const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
      const releaseSpy = spyOn(lockService, 'release');

      benchmarkServiceMock.getRun.and.returnValue(throwError(() => ({ status: 500 })));
      (component as any).startPolling(42);
      releaseSpy.calls.reset();
      for (let i = 0; i < 3; i++) {
        (component as any).pollRunDetail(42);
      }

      benchmarkServiceMock.getRun.and.returnValue(of({ id: 42, status: 'Running', answers: [] } as any));
      (component as any).pollRunDetail(42);

      benchmarkServiceMock.getRun.and.returnValue(throwError(() => ({ status: 500 })));
      for (let i = 0; i < 4; i++) {
        (component as any).pollRunDetail(42);
      }

      expect(releaseSpy).not.toHaveBeenCalled();
      expect(consoleError).toHaveBeenCalledWith('Failed to poll run detail', jasmine.any(Object));
      (component as any).stopPolling();
      component.ngOnDestroy();
    });

    it('does not release the background lock on a single series poll error, and stays polling', () => {
      const consoleError = spyOn(console, 'error');
      const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
      const releaseSpy = spyOn(lockService, 'release');
      const acquireSpy = spyOn(lockService, 'acquireForSeries');

      (component as any).startSeriesPolling(11);
      expect(acquireSpy).toHaveBeenCalledWith(11);
      releaseSpy.calls.reset();

      benchmarkServiceMock.getRunSeries.and.returnValue(throwError(() => ({ status: 500 })));
      (component as any).pollSeries(11);

      expect(releaseSpy).not.toHaveBeenCalled();
      expect((component as any).seriesPollTickerHandle).not.toBeNull();
      expect(consoleError).toHaveBeenCalledWith('Failed to poll benchmark run series', jasmine.any(Object));
      (component as any).stopSeriesPolling();
      component.ngOnDestroy();
    });

    it('releases the background lock only on the 5th consecutive series poll error', () => {
      const consoleError = spyOn(console, 'error');
      const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
      const releaseSpy = spyOn(lockService, 'release');

      (component as any).startSeriesPolling(11);
      releaseSpy.calls.reset();
      benchmarkServiceMock.getRunSeries.and.returnValue(throwError(() => ({ status: 500 })));

      for (let i = 0; i < 4; i++) {
        (component as any).pollSeries(11);
      }
      expect(releaseSpy).not.toHaveBeenCalled();

      (component as any).pollSeries(11);
      expect(releaseSpy).toHaveBeenCalled();
      expect((component as any).seriesPollTickerHandle).toBeNull();
      expect(consoleError).toHaveBeenCalledWith('Failed to poll benchmark run series', jasmine.any(Object));
      component.ngOnDestroy();
    });

    it('keeps the series lock while a member run is polled and when that run poller stops', () => {
      const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
      benchmarkServiceMock.getRunSeries.and.returnValue(of({
        id: 11, status: 'Running', completedRunCount: 0, requestedRunCount: 3, members: []
      } as any));
      const acquireSeriesSpy = spyOn(lockService, 'acquireForSeries');
      const acquireRunSpy = spyOn(lockService, 'acquireForRun');
      const releaseSpy = spyOn(lockService, 'release');

      (component as any).startSeriesPolling(11);
      expect(acquireSeriesSpy).toHaveBeenCalledOnceWith(11);

      (component as any).startPolling(42);
      (component as any).stopPolling();

      expect(acquireRunSpy).not.toHaveBeenCalled();
      expect(releaseSpy).not.toHaveBeenCalled();

      (component as any).stopSeriesPolling();
      expect(releaseSpy).toHaveBeenCalledTimes(1);
      component.ngOnDestroy();
    });
  });

  describe('series banner lifecycle', () => {
    function attachSeries(status: string): void {
      component.activeSeries = {
        id: 2,
        suiteName: 'GnollHack Player Assistance Benchmark Suite',
        status,
        requestedRunCount: 3,
        completedRunCount: 3,
        failedRunCount: 0,
        members: [],
        resumable: false,
        allowCapWait: true
      } as unknown as typeof component.activeSeries;
      component.activeSeriesId = 2;
      component.multiRunDialogVisible = false;
    }

    // A banner describing work that is still going to produce something.
    ['Pending', 'Running', 'WaitingForCap'].forEach(status => {
      it(`should show the series banner while the series is ${status}`, () => {
        attachSeries(status);

        expect(component.seriesIsFinished).toBeFalse();
        expect(component.seriesBannerVisible).toBeTrue();
      });
    });

    // Stopped is the one non-terminal end state, and the only one with a Continue button to offer.
    it('should keep the series banner for a Stopped series, which is resumable', () => {
      attachSeries('Stopped');

      expect(component.seriesIsFinished).toBeFalse();
      expect(component.seriesBannerVisible).toBeTrue();
    });

    ['Completed', 'Cancelled', 'Failed'].forEach(status => {
      it(`should hide the series banner once the series is ${status}`, () => {
        attachSeries(status);

        expect(component.seriesIsFinished).toBeTrue();
        expect(component.seriesBannerVisible).toBeFalse();
      });
    });

    it('should not render the banner element for a completed series', () => {
      attachSeries('Completed');
      component.activeSubTab = 'run';
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.series-banner')).toBeNull();
    });

    it('should keep activeSeries so the dialog and run labelling still resolve it', () => {
      attachSeries('Completed');

      // Gated rendering, not cleared state: seriesIdForRun and the progress dialog read this after
      // the series ends.
      expect(component.activeSeries).not.toBeNull();
      expect(component.activeSeriesId).toBe(2);
    });

    it('should hide the banner while the progress dialog is open', () => {
      attachSeries('Running');
      component.multiRunDialogVisible = true;

      expect(component.seriesBannerVisible).toBeFalse();
    });
  });

  describe('opening a series that this page is not driving', () => {
    it('should point the progress dialog at the requested series and open it', () => {
      component.openSeriesDialog(7);

      expect(component.seriesDialogId).toBe(7);
      expect(component.dialogSeriesId).toBe(7);
      expect(component.multiRunDialogVisible).toBeTrue();
    });

    it('should fall back to the live series when none was explicitly opened', () => {
      component.activeSeriesId = 2;

      expect(component.seriesDialogId).toBeNull();
      expect(component.dialogSeriesId).toBe(2);
    });

    it('should clear the explicitly opened series when the dialog closes', () => {
      component.activeSeriesId = 2;
      component.openSeriesDialog(7);

      component.onMultiRunDialogClosed();

      expect(component.seriesDialogId).toBeNull();
      expect(component.multiRunDialogVisible).toBeFalse();
      // Back to the live series, which is what the banner and the run labelling describe.
      expect(component.dialogSeriesId).toBe(2);
    });
  });

  describe('handing a group analysis from the series dialog to the multirun panel', () => {
    beforeEach(() => fixture.detectChanges());

    it('should switch to the Multi-Run Analysis tab and clear the series dialog state', () => {
      // The panel's own fetch is not the subject here, and it would reach a service method this
      // suite's mock does not carry.
      spyOn(MultiRunComponent.prototype, 'openGroupById');
      component.multiRunDialogVisible = true;
      component.seriesDialogId = 9;

      component.onOpenGroupAnalysisFromSeries(42);

      expect(component.activeSubTab).toBe('multirun');
      expect(component.multiRunDialogVisible).toBeFalse();
      expect(component.seriesDialogId).toBeNull();
    });

    it('should hand the group id to the multirun panel once the tab has rendered it', () => {
      const openGroupByIdSpy = spyOn(MultiRunComponent.prototype, 'openGroupById');

      component.onOpenGroupAnalysisFromSeries(42);

      // The panel lives inside @if (activeSubTab === 'multirun'), so it does not exist until the
      // tab switch above has been flushed through change detection.
      expect(component.multiRunPanel).toBeTruthy();
      expect(openGroupByIdSpy).toHaveBeenCalledWith(42);
    });
  });

  describe('Run History card list (data-table)', () => {
    function buildHistoryRun(overrides: Record<string, unknown> = {}): any {
      return {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Model A',
        testedModelProviderUsed: 'Anthropic',
        testedModelIdUsed: 'model-a',
        assessorModelDisplayNameUsed: 'Model B',
        status: 'Completed',
        startedAtUtc: '2026-09-01T00:00:00Z',
        totalAnswerDurationMs: 1000,
        totalDurationMs: 1000,
        speedMeasurementDegraded: false,
        answeredQuestionCount: 5,
        totalQuestionCount: 5,
        candidateSystemPromptSha256: 'sha-a',
        toolGuidesSha256: 'guide-a',
        knowledgeBaseHeadSha: 'kb-a',
        wikiHeadSha: 'wiki-a',
        sourceCodeHeadSha: 'src-a',
        ...overrides
      };
    }

    it('should default to sorting by ID, descending', () => {
      expect(component.historyTable.sortColumn).toBe('id');
      expect(component.historyTable.sortDirection).toBe('desc');
    });

    it('should keep instrumentChangeOf verdicts unchanged when the view is sorted', () => {
      component.historyRuns = [
        buildHistoryRun({ id: 3, startedAtUtc: '2026-09-03T00:00:00Z', candidateSystemPromptSha256: 'sha-b' }),
        buildHistoryRun({ id: 2, startedAtUtc: '2026-09-02T00:00:00Z', status: 'Running' }),
        buildHistoryRun({ id: 1, startedAtUtc: '2026-09-01T00:00:00Z' })
      ];

      const before = component.instrumentChangeOf(component.historyRuns[0]);
      expect(before).toBeTruthy();
      expect(before?.comparedToRunId).toBe(1);

      // historyView is a new sorted array; historyRuns itself — which instrumentChangeOf and
      // completedRunsOfSelectedSuite both read by position — must not move under it.
      expect(component.historyView.map(r => r.id)).toEqual([3, 2, 1]);
      component.historyTable.toggleSort('startedAtUtc');
      component.historyTable.toggleSort('startedAtUtc');
      expect(component.historyView.map(r => r.id)).toEqual([1, 2, 3]);

      const after = component.instrumentChangeOf(component.historyRuns[0]);
      expect(after).toEqual(before);
    });

    it('should derive the Status filter options from the statuses present in the history', () => {
      component.historyRuns = [
        buildHistoryRun({ id: 1, status: 'Completed' }),
        buildHistoryRun({ id: 2, status: 'CompletedWithLimits' }),
        buildHistoryRun({ id: 3, status: 'Completed' })
      ];

      expect(component.historyStatusOptions).toEqual(['Completed', 'Completed with limits']);
    });

    it('should batch and filter the view without touching historyRuns', () => {
      const runs = Array.from({ length: 25 }, (_, i) => buildHistoryRun({ id: 25 - i, suiteName: `Suite ${(i % 2) + 1}` }));
      component.historyRuns = runs;

      expect(component.historyView.length).toBe(10);
      component.showMoreHistory();
      expect(component.historyView.length).toBe(20);

      // A facet change returns the list to one batch.
      component.onHistoryFacetChange('suite', ['Suite 2']);
      expect(component.historyView.length).toBe(10);
      expect(component.historyView.every(r => r.suiteName === 'Suite 2')).toBeTrue();
      expect(component.historyList.matching(component.historyRuns).length).toBe(12);

      expect(component.historyRuns).toBe(runs);
      expect(component.historyRuns.length).toBe(25);
      expect(component.historyRuns.map(r => r.id)).toEqual(Array.from({ length: 25 }, (_, i) => 25 - i));
    });

    /** An element's visible text: its text without the visually hidden parts. */
    function visibleText(element: Element): string {
      const clone = element.cloneNode(true) as Element;
      clone.querySelectorAll('.visually-hidden').forEach(hidden => hidden.remove());
      return (clone.textContent || '').replace(/\s+/g, ' ').trim();
    }

    it('should list a labeled pair per fingerprint, dashing a hash that was not recorded, and the full hashes in its info tip', () => {
      component.historyRuns = [buildHistoryRun({ id: 1, wikiHeadSha: null })];
      component.activeSubTab = 'history';
      fixture.detectChanges();

      const strip = fixture.nativeElement.querySelector('.rh-card .rh-instrument-strip') as HTMLElement;
      const pairs = Array.from(strip.querySelectorAll('dl.rh-instrument > div')) as HTMLElement[];

      // Five pairs whatever the run recorded: the label carries the meaning, so the strip stays
      // legible in grayscale, and a missing hash is visible as a dash rather than absent.
      expect(pairs.length).toBe(5);
      expect(pairs.map(pair => visibleText(pair.querySelector('dt')!))).toEqual(['PROMPT', 'GUIDES', 'KB', 'WIKI', 'SRC']);
      expect(pairs.map(pair => pair.querySelector('dt .visually-hidden')?.textContent?.trim())).toEqual([
        '(candidate system prompt)', '(tool guides)', '(knowledge base)', '(wiki)', '(source code)'
      ]);
      const values = pairs.map(pair => pair.querySelector('dd') as HTMLElement);
      expect(values.map(dd => dd.textContent?.trim())).toEqual(['sha-a', 'guide-a', 'kb-a', '-', 'src-a']);

      const cssClasses = ['fp-prompt', 'fp-guides', 'fp-kb', 'fp-wiki', 'fp-source'];
      cssClasses.forEach((cssClass, i) => expect(values[i].classList.contains(cssClass)).toBeTrue());
      expect(getComputedStyle(values[0]).fontFamily).toContain('monospace');

      expect(strip.querySelectorAll('[title]').length).toBe(0);

      const button = strip.querySelector('app-info-tip button.gh-info-btn') as HTMLButtonElement;
      expect(button.getAttribute('aria-label')).toBe('About Instrument of run 1');
      const tip = strip.querySelector('#rh-instr-1') as HTMLElement;
      const terms = Array.from(tip.querySelectorAll('dt')).map(dt => dt.textContent?.trim());
      const full = Array.from(tip.querySelectorAll('dd')).map(dd => dd.textContent?.trim());
      expect(terms).toEqual([
        'Candidate system prompt SHA-256',
        'Tool guides SHA-256',
        'Knowledge base Git HEAD SHA',
        'GnollHack wiki Git HEAD SHA',
        'GnollHack source Git HEAD SHA'
      ]);
      expect(full).toEqual(['sha-a', 'guide-a', 'kb-a', 'not recorded', 'src-a']);
    });

    /** Enters Run History through its real tab, with the server returning these runs. */
    function openHistoryWith(runs: any[]): void {
      benchmarkServiceMock.getRuns.and.returnValue(of(runs));
      (fixture.nativeElement.querySelector('#bm-tab-history') as HTMLButtonElement).click();
      fixture.detectChanges();
    }

    /** The runs whose cards are shown, by id, in order. */
    function shownIds(): number[] {
      return (Array.from(fixture.nativeElement.querySelectorAll('.rh-card-list article.rh-card')) as HTMLElement[])
        .map(card => Number(card.getAttribute('data-run-id')));
    }

    /** The active-filter chips. */
    function chipButtons(): HTMLButtonElement[] {
      return Array.from(fixture.nativeElement.querySelectorAll('.rh-filter-chips .gh-filter-chip')) as HTMLButtonElement[];
    }

    function historyStatus(): string {
      return (fixture.nativeElement.querySelector('#rh-list-status')?.textContent || '').trim();
    }

    /** Resolves once a debounced search has applied. */
    function afterSearchDebounce(): Promise<void> {
      return new Promise(resolve => setTimeout(resolve, 250));
    }

    it('should show the degraded count as a glyph and words, the start time, both cost lines and the four actions', () => {
      openHistoryWith([buildHistoryRun({
        id: 42,
        startedAtUtc: '2026-09-28T16:12:00',
        estimatedCandidateCost: 2.5211,
        estimatedCost: 6.0068,
        degradedAnswerCount: 2
      })]);

      const card = fixture.nativeElement.querySelector('.rh-card-list > li > article.rh-card') as HTMLElement;
      expect(card).toBeTruthy();
      expect(card.getAttribute('aria-labelledby')).toBe('rh-run-42-title');
      expect(card.querySelector('h5#rh-run-42-title')?.getAttribute('tabindex')).toBe('-1');

      const degraded = card.querySelector('.rh-card-kicker .badge-degraded-count') as HTMLElement;
      expect(degraded.textContent?.replace(/\s+/g, ' ').trim()).toBe('2 degraded answers');
      expect(visibleText(degraded)).toBe('2');
      const glyph = degraded.querySelector('svg') as SVGElement;
      expect(glyph.getAttribute('aria-hidden')).toBe('true');
      expect(glyph.getAttribute('width')).toBe('12');
      expect(degraded.hasAttribute('title')).toBeFalse();
      expect(card.textContent).not.toContain('⚠');

      const time = card.querySelector('.rh-card-meta time') as HTMLTimeElement;
      expect(time.getAttribute('datetime')).toBe('2026-09-28T16:12:00');
      expect(time.textContent?.trim()).toBe('2026-09-28 16:12 UTC');

      const cost = card.querySelector('.rh-metric[data-metric="cost"]') as HTMLElement;
      expect(Array.from(cost.querySelectorAll('dd > span')).map(span => span.textContent?.trim()))
        .toEqual(['$2.5211', 'catalog $6.0068']);

      const actions = card.querySelector('.rh-card-actions[role="group"]') as HTMLElement;
      expect(actions.getAttribute('aria-label')).toBe('Actions for run 42');
      const buttons = Array.from(actions.querySelectorAll('button.action-btn')) as HTMLButtonElement[];
      expect(buttons.map(b => b.getAttribute('aria-label'))).toEqual([
        'View details for run 42',
        'Download Markdown report for run 42',
        'Download tool-call log for run 42',
        'Delete run 42'
      ]);
      // The file-with-arrow glyph: a file whose arrow points down into it.
      expect(buttons[1].querySelector('path')?.getAttribute('d')).toMatch(/^M14 2H6/);
      expect(buttons[1].querySelector('polyline[points="9 15 12 18 15 15"]')).toBeTruthy();
      expect(buttons[2].querySelector('polyline[points="9 15 12 18 15 15"]')).toBeNull();
      expect(buttons[3].classList.contains('action-btn-danger')).toBeTrue();
      expect(buttons.map(b => b.getAttribute('interestfor'))).toEqual([
        'tip-view-run-42', 'tip-dl-run-42', 'tip-tcl-run-42', 'tip-del-run-42'
      ]);
      expect(card.querySelectorAll('[title]').length).toBe(0);
    });

    it('should fit ten run cards in 1360 px without scrolling sideways, their metrics lined up', async () => {
      const hash = (seed: string, length: number) => seed.repeat(Math.ceil(length / seed.length)).substring(0, length);
      const instrument = {
        candidateSystemPromptSha256: hash('3f9d06fa', 64),
        toolGuidesSha256: hash('b66a59b2', 64),
        knowledgeBaseHeadSha: hash('7424b03c', 40),
        wikiHeadSha: hash('080485a1', 40),
        sourceCodeHeadSha: hash('429db58e', 40),
        candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true}'
      };
      const suites = [
        { benchmarkSuiteId: 7, suiteName: 'Snapshot: Tommi2 2026-09-17 (long variant for wrapping)' },
        { benchmarkSuiteId: 5, suiteName: 'Snapshot: Tommi2 2026-09-17' },
        { benchmarkSuiteId: 3, suiteName: 'GnollHack Mechanics Core' }
      ];
      const models = ['GPT-5.6 Luna', 'Claude 5 Opus', 'Gemini 3.8 Flash'];
      const runs = Array.from({ length: 10 }, (_, i) => buildHistoryRun({
        ...instrument,
        ...suites[i % suites.length],
        id: 110 - i,
        testedModelDisplayNameUsed: models[i % models.length],
        assessorModelDisplayNameUsed: 'Claude 5 Opus',
        qualityIndex: 60 + i * 3,
        speedIndex: 90 - i * 4,
        totalAnswerDurationMs: 765466 + i * 61000,
        totalDurationMs: 1419000 + i * 61000,
        estimatedCandidateCost: 2.5211,
        estimatedCost: 6.0068,
        startedAtUtc: `2026-09-${String(28 - i).padStart(2, '0')}T16:12:00`
      }));
      // The newest run of the long-named suite moved its knowledge base since run 107, the next
      // older run of that suite, so it carries the INSTRUMENT CHANGED badge.
      runs[0].knowledgeBaseHeadSha = hash('c0ffee42', 40);
      runs[1].degradedAnswerCount = 3;

      fixture.nativeElement.style.width = '1360px';
      openHistoryWith(runs);
      await document.fonts.ready;
      fixture.detectChanges();

      const cards = Array.from(fixture.nativeElement.querySelectorAll('.rh-card-list > li > article.rh-card')) as HTMLElement[];
      expect(cards.length).toBe(10);
      expect(fixture.nativeElement.querySelectorAll('.rh-card .rh-instrument dd').length).toBe(50);
      expect(Array.from(fixture.nativeElement.querySelectorAll('.rh-card .instrument-changed'))
        .map((badge: any) => badge.textContent.trim())).toEqual(['INSTRUMENT CHANGED']);

      const list = fixture.nativeElement.querySelector('.rh-card-list') as HTMLElement;
      expect(list.clientWidth).toBeGreaterThan(0);
      expect(list.scrollWidth).toBeLessThanOrEqual(list.clientWidth);
      for (const card of cards) {
        expect(card.scrollWidth).withContext(card.getAttribute('data-run-id')!).toBeLessThanOrEqual(card.clientWidth);
      }

      // Each metric column starts at the same x-position on every card, so the list scans like a table.
      for (let column = 0; column < 4; column++) {
        const lefts = cards.map(card =>
          Math.round((card.querySelectorAll('.rh-metrics > .rh-metric')[column] as HTMLElement).getBoundingClientRect().left));
        expect(new Set(lefts).size).withContext(`metric column ${column}`).toBe(1);
      }

      // The model under test is the card's headline.
      const model = getComputedStyle(cards[0].querySelector('.rh-card-model') as HTMLElement);
      const meta = getComputedStyle(cards[0].querySelector('.rh-card-meta') as HTMLElement);
      expect(parseFloat(model.fontSize)).toBeGreaterThan(parseFloat(meta.fontSize));
      expect(parseFloat(model.fontWeight)).toBeGreaterThanOrEqual(700);
    });

    it('should filter by the Suite facet without asking the server again', () => {
      openHistoryWith([
        buildHistoryRun({ id: 3, suiteName: 'Alpha' }),
        buildHistoryRun({ id: 2, suiteName: 'Beta' }),
        buildHistoryRun({ id: 1, suiteName: 'Alpha' })
      ]);
      benchmarkServiceMock.getRuns.calls.reset();

      const facet = component.historyFacets.find(f => f.column === 'suite')!;
      expect(facet.facetId).toBe('rh-facet-suite');
      expect(facet.options.map(o => [o.value, o.count])).toEqual([['Alpha', 2], ['Beta', 1]]);
      expect(fixture.nativeElement.querySelector('.rh-facet-row #rh-facet-suite-trigger')).toBeTruthy();
      // The server-side suite select is gone.
      expect(fixture.nativeElement.querySelector('#historySuiteFilter')).toBeNull();

      component.onHistoryFacetChange('suite', ['Alpha']);

      expect(shownIds()).toEqual([3, 1]);
      expect(chipButtons().map(chip => chip.getAttribute('aria-label'))).toEqual(['Remove filter Suite: Alpha']);
      expect(benchmarkServiceMock.getRuns).not.toHaveBeenCalled();
    });

    it('should find a run by its #id and by a hash prefix', async () => {
      openHistoryWith([
        buildHistoryRun({ id: 42, knowledgeBaseHeadSha: '1b512e27aa55' }),
        buildHistoryRun({ id: 7 })
      ]);
      const search = fixture.nativeElement.querySelector('#rh-search') as HTMLInputElement;
      expect(fixture.nativeElement.querySelector('label[for="rh-search"]')?.textContent?.trim()).toBe('Search runs');

      search.value = '#42';
      search.dispatchEvent(new Event('input'));
      await afterSearchDebounce();
      expect(shownIds()).toEqual([42]);

      search.value = '1B512E';
      search.dispatchEvent(new Event('input'));
      await afterSearchDebounce();
      expect(shownIds()).toEqual([42]);
      expect(chipButtons().map(chip => chip.getAttribute('aria-label'))).toEqual(['Remove filter Search: “1B512E”']);

      search.value = '#7';
      search.dispatchEvent(new Event('input'));
      await afterSearchDebounce();
      expect(shownIds()).toEqual([7]);
    });

    it('should start the search text to the right of its glyph', async () => {
      openHistoryWith([buildHistoryRun({ id: 1 })]);
      await document.fonts.ready;
      fixture.detectChanges();

      const search = fixture.nativeElement.querySelector('#rh-search') as HTMLInputElement;
      const glyph = fixture.nativeElement.querySelector('.rh-search > svg') as SVGElement;
      expect(getComputedStyle(search).paddingInlineStart).toBe('34px');
      const field = search.getBoundingClientRect();
      expect(field.left + 34).toBeGreaterThanOrEqual(glyph.getBoundingClientRect().right);
      expect(field.height).toBeLessThanOrEqual(33);
    });

    it('should clear the search on Escape without closing anything, and let Escape through when it is empty', async () => {
      openHistoryWith([buildHistoryRun({ id: 2 }), buildHistoryRun({ id: 1 })]);
      const panel = fixture.nativeElement.querySelector('#bm-panel-history') as HTMLElement;
      const reached: KeyboardEvent[] = [];
      panel.addEventListener('keydown', event => reached.push(event));
      const search = fixture.nativeElement.querySelector('#rh-search') as HTMLInputElement;

      search.value = 'model';
      search.dispatchEvent(new Event('input'));
      const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      search.dispatchEvent(escape);

      expect(escape.defaultPrevented).toBeTrue();
      expect(reached).toEqual([]);
      expect(search.value).toBe('');
      expect(component.historyList.searchText).toBe('');
      // The pending search was dropped with the text.
      await afterSearchDebounce();
      expect(component.historyTable.hasActiveFilters).toBeFalse();
      expect(shownIds()).toEqual([2, 1]);

      const again = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      search.dispatchEvent(again);
      expect(again.defaultPrevented).toBeFalse();
      expect(reached.length).toBe(1);
      expect(reached[0]).toBe(again);
    });

    it('should remember the Sort by choice and restore it', () => {
      openHistoryWith([buildHistoryRun({ id: 2, qualityIndex: 50 }), buildHistoryRun({ id: 1, qualityIndex: 90 })]);
      const select = fixture.nativeElement.querySelector('#rh-sort') as HTMLSelectElement;
      expect(fixture.nativeElement.querySelector('label[for="rh-sort"]')?.textContent?.trim()).toBe('Sort by');
      expect(Array.from(select.options).map(option => option.textContent?.trim())).toEqual([
        'Newest first',
        'Oldest first',
        'Intelligence Index, highest first',
        'Speed Index, highest first',
        'Cost, lowest first',
        'Cost, highest first',
        'Duration, shortest first',
        'Tested model (A–Z)',
        'Suite (A–Z)'
      ]);
      expect(select.value).toBe('newest');
      expect(shownIds()).toEqual([2, 1]);

      select.value = 'intelligence-desc';
      select.dispatchEvent(new Event('change'));

      expect(shownIds()).toEqual([1, 2]);
      expect(JSON.parse(localStorage.getItem(RUN_HISTORY_VIEW_STORAGE_KEY)!)).toEqual({ version: 1, sort: 'intelligence-desc' });

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      expect(restored.componentInstance.historyList.sortId).toBe('intelligence-desc');
      expect(restored.componentInstance.historyTable.sortColumn).toBe('qualityIndex');
      expect(restored.componentInstance.historyTable.sortDirection).toBe('desc');
      restored.destroy();
    });

    it('should move focus to the next chip after a chip is removed, then to the search field', () => {
      openHistoryWith([
        buildHistoryRun({ id: 3, suiteName: 'Alpha' }),
        buildHistoryRun({ id: 2, suiteName: 'Beta' }),
        buildHistoryRun({ id: 1, suiteName: 'Gamma' })
      ]);
      component.onHistoryFacetChange('suite', ['Alpha', 'Beta']);
      expect(chipButtons().map(chip => chip.getAttribute('aria-label')))
        .toEqual(['Remove filter Suite: Alpha', 'Remove filter Suite: Beta']);

      chipButtons()[0].click();
      expect(chipButtons().map(chip => chip.getAttribute('aria-label'))).toEqual(['Remove filter Suite: Beta']);
      expect(document.activeElement).toBe(chipButtons()[0]);
      expect(shownIds()).toEqual([2]);

      chipButtons()[0].click();
      expect(chipButtons()).toEqual([]);
      expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#rh-search'));
      expect(shownIds()).toEqual([3, 2, 1]);
    });

    it('should focus the first new card title after Show 10 more', () => {
      openHistoryWith(Array.from({ length: 25 }, (_, i) => buildHistoryRun({ id: 25 - i })));
      expect(shownIds().length).toBe(10);

      const more = fixture.nativeElement.querySelector('.rh-load-more .rh-show-more') as HTMLButtonElement;
      expect(more.classList.contains('btn-ghost')).toBeTrue();
      expect(more.textContent?.trim()).toBe('Show 10 more');
      const all = fixture.nativeElement.querySelector('.rh-load-more .rh-show-all') as HTMLButtonElement;
      expect(all.classList.contains('gh-filter-clear')).toBeTrue();
      expect(all.textContent?.trim()).toBe('Show all 25');

      more.click();

      expect(shownIds().length).toBe(20);
      expect(document.activeElement?.id).toBe('rh-run-15-title');
      expect(historyStatus()).toBe('Showing 20 of 25 runs');
      expect((fixture.nativeElement.querySelector('.rh-show-more') as HTMLElement).textContent?.trim()).toBe('Show 5 more');
      // One batch or less remains, so Show all is not offered.
      expect(fixture.nativeElement.querySelector('.rh-show-all')).toBeNull();
    });

    it('should move focus to the next card title after a delete, else the previous one, else the heading', () => {
      const runs = [3, 2, 1].map(id => buildHistoryRun({ id }));
      openHistoryWith(runs);
      benchmarkServiceMock.deleteRun.and.returnValue(of(undefined as any));

      // The middle card: its successor takes its place.
      benchmarkServiceMock.getRuns.and.returnValue(of([runs[0], runs[2]]));
      (fixture.nativeElement.querySelector('button[aria-label="Delete run 2"]') as HTMLButtonElement).click();
      component.executeConfirmAction();
      expect(benchmarkServiceMock.deleteRun).toHaveBeenCalledWith(2);
      expect(shownIds()).toEqual([3, 1]);
      expect(document.activeElement?.id).toBe('rh-run-1-title');

      // The last card: the one before it.
      benchmarkServiceMock.getRuns.and.returnValue(of([runs[0]]));
      (fixture.nativeElement.querySelector('button[aria-label="Delete run 1"]') as HTMLButtonElement).click();
      component.executeConfirmAction();
      expect(document.activeElement?.id).toBe('rh-run-3-title');

      // The only card: the list's heading.
      benchmarkServiceMock.getRuns.and.returnValue(of([]));
      (fixture.nativeElement.querySelector('button[aria-label="Delete run 3"]') as HTMLButtonElement).click();
      component.executeConfirmAction();
      expect(document.activeElement?.id).toBe('rh-list-title');
    });

    it('should count the runs in the status line, and say when the newest 200 are all that is loaded', () => {
      openHistoryWith(Array.from({ length: 12 }, (_, i) => buildHistoryRun({ id: 12 - i })));
      const status = fixture.nativeElement.querySelector('#rh-list-status') as HTMLElement;
      expect(status.getAttribute('role')).toBe('status');
      expect(historyStatus()).toBe('Showing 10 of 12 runs');

      benchmarkServiceMock.getRuns.calls.reset();
      benchmarkServiceMock.getRuns.and.returnValue(of(Array.from({ length: 200 }, (_, i) => buildHistoryRun({ id: 200 - i }))));
      const refresh = fixture.nativeElement.querySelector('.rh-list-head .rh-refresh') as HTMLButtonElement;
      expect(refresh.classList.contains('btn-ghost')).toBeTrue();
      refresh.click();

      expect(benchmarkServiceMock.getRuns).toHaveBeenCalledOnceWith(undefined, 200);
      expect(historyStatus()).toBe('Showing 10 of 200 runs · Only the newest 200 runs are loaded');
    });

    it('should tell no runs recorded apart from no runs matching the filters', () => {
      openHistoryWith([]);
      const panel = () => fixture.nativeElement.querySelector('#bm-panel-history') as HTMLElement;
      expect(panel().textContent).toContain('No benchmark runs recorded yet.');
      expect(panel().querySelector('.rh-no-matches')).toBeNull();
      expect(panel().querySelector('.rh-filter-bar')).toBeNull();

      benchmarkServiceMock.getRuns.and.returnValue(of([buildHistoryRun({ id: 2 }), buildHistoryRun({ id: 1 })]));
      (panel().querySelector('.rh-refresh') as HTMLButtonElement).click();
      component.onHistoryFacetChange('status', ['Failed']);

      const noMatches = panel().querySelector('.rh-no-matches') as HTMLElement;
      expect(noMatches.textContent).toContain('No runs match these filters.');
      expect(panel().textContent).not.toContain('No benchmark runs recorded yet.');
      expect(panel().querySelector('.rh-card-list')).toBeNull();

      const clear = Array.from(noMatches.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Clear all filters') as HTMLButtonElement;
      clear.click();
      expect(shownIds()).toEqual([2, 1]);
      expect(document.activeElement).toBe(panel().querySelector('#rh-search'));
    });

    it('should count the Changes facet by instrumentChangeOf', () => {
      openHistoryWith([
        // A knowledge-base move since run 3, whose options cannot be compared with none.
        buildHistoryRun({ id: 4, knowledgeBaseHeadSha: 'kb-b' }),
        // Detailed rather than concise since run 2.
        buildHistoryRun({ id: 3, candidatePromptOptionsJson: '{"verboseMode":true}' }),
        buildHistoryRun({ id: 2, candidatePromptOptionsJson: '{"verboseMode":false}' }),
        buildHistoryRun({ id: 1 })
      ]);

      const counts = new Map<string, number>();
      for (const run of component.historyRuns) {
        const change = component.instrumentChangeOf(run);
        const value = change ? (change.kind === 'options' ? 'Options changed' : 'Instrument changed') : 'No change';
        counts.set(value, (counts.get(value) ?? 0) + 1);
      }

      const facet = component.historyFacets.find(f => f.column === 'changes')!;
      expect(facet.options.map(o => [o.value, o.count])).toEqual([
        ['Instrument changed', counts.get('Instrument changed') ?? 0],
        ['Options changed', counts.get('Options changed') ?? 0],
        ['No change', counts.get('No change') ?? 0]
      ]);
      expect(facet.options.map(o => o.count)).toEqual([1, 1, 2]);

      component.onHistoryFacetChange('changes', ['Instrument changed']);
      expect(shownIds()).toEqual([4]);
    });
  });

  // ---------------------------------------------------------------------------
  // Aborted runs, the answer shortfall badge, and the instrument-vs-options distinction
  //
  // A run that stopped early has no answer-duration total to be measured by, and a run that
  // answered fewer questions than its suite holds must say so beside its status. The instrument
  // badge is a separate claim: a changed run option moves the candidate hash on its own, and
  // reporting that as instrument drift blames the measuring stick for a change to what is measured.
  // ---------------------------------------------------------------------------
  describe('aborted runs, answer shortfall and the instrument badge', () => {
    function buildRun(overrides: Record<string, unknown> = {}): any {
      return {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Gemini 3.1 Pro',
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-3.1-pro',
        assessorModelDisplayNameUsed: 'Claude Opus',
        status: 'Completed',
        startedAtUtc: '2026-09-08T13:35:00Z',
        completedAtUtc: '2026-09-08T13:58:39Z',
        totalAnswerDurationMs: 765466,
        totalDurationMs: 1419000,
        speedMeasurementDegraded: false,
        answeredQuestionCount: 18,
        totalQuestionCount: 18,
        unansweredQuestionCount: 0,
        candidateSystemPromptSha256: 'sha-a',
        toolGuidesSha256: 'guide-a',
        knowledgeBaseHeadSha: 'kb-a',
        wikiHeadSha: 'wiki-a',
        sourceCodeHeadSha: 'src-a',
        candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true}',
        ...overrides
      };
    }

    it('should measure an aborted run by the wall clock, not by the time its answers took', () => {
      const run = buildRun({ status: 'Canceled', totalAnswerDurationMs: 2000000, totalDurationMs: 1419000 });

      expect(component.runDurationMs(run)).toBe(1419000);
      expect(component.isAbortedRun(run)).toBeTrue();
    });

    it('should not treat a Canceled run whose answers cover its suite as aborted', () => {
      // A cancelled retry of a finished run: every answer row is still there, so the server
      // reports isAborted false and the run is measured by its answers like any complete run.
      const run = buildRun({ status: 'Canceled', isAborted: false });

      expect(component.isAbortedRun(run)).toBeFalse();
      expect(component.runDurationMs(run)).toBe(765466);
    });

    it('should trust the server flag over the status', () => {
      const run = buildRun({ status: 'CompletedWithErrors', isAborted: true });

      expect(component.isAbortedRun(run)).toBeTrue();
    });

    it('should measure a completed run by the time its answers took', () => {
      const run = buildRun();

      expect(component.runDurationMs(run)).toBe(765466);
      expect(component.isAbortedRun(run)).toBeFalse();
    });

    it('should derive a duration from the timestamps when neither total was recorded', () => {
      const run = buildRun({ status: 'Canceled', totalAnswerDurationMs: 0, totalDurationMs: 0 });

      // 13:35:00 to 13:58:39 is run 24's own wall clock: 23m 39s.
      expect(component.runDurationMs(run)).toBe(1419000);
    });

    // The run detail's two duration cards. The Answer Duration card has no wall-clock fallback:
    // the two figures measure different things, so substituting one for the other would put a
    // wall-clock number under a label that says answer time.
    it('should label the two run detail durations as the separate figures they are', () => {
      const run = buildRun({ status: 'Canceled' });

      expect(component.runAnswerDurationLabel(run)).toBe('12m 45s');
      expect(component.runWallClockLabel(run)).toBe('23m 39s');
    });

    it('should dash the Answer Duration card rather than borrow the wall clock', () => {
      const run = buildRun({ status: 'Canceled', totalAnswerDurationMs: 0 });

      expect(component.runAnswerDurationLabel(run)).toBe('—');
      expect(component.runWallClockLabel(run)).toBe('23m 39s');
    });

    it('should derive the wall clock card from the timestamps when the run recorded none', () => {
      // What an interrupted run looks like: cleanup leaves TotalDurationMs at zero, because the
      // outage between the crash and the restart is not run time.
      const run = buildRun({ status: 'Failed', totalDurationMs: 0 });

      expect(component.runWallClockLabel(run)).toBe('23m 39s');
    });

    it('should dash the wall clock card for a run with no completion timestamp', () => {
      const run = buildRun({ status: 'Failed', totalDurationMs: 0, completedAtUtc: null });

      expect(component.runWallClockLabel(run)).toBe('—');
    });

    it('should leave both card notes unchanged for a run that was never re-run', () => {
      const run = buildRun({ status: 'Canceled' });

      expect(component.runAnswerDurationNote(run)).toBe('sum over answers, tools included');
      expect(component.runWallClockNote(run)).toBe('start to finish, grading included');
    });

    it("should name the re-run's own span on the wall time note and flag re-executed answers on the answer duration note", () => {
      const run = buildRun({
        status: 'Canceled',
        rerunStartedAtUtc: '2026-09-09T10:00:00Z',
        rerunCompletedAtUtc: '2026-09-09T10:20:04Z'
      });

      expect(component.runAnswerDurationNote(run)).toBe('sum over answers, tools included · includes re-executed answers');
      expect(component.runWallClockNote(run)).toBe('start to finish, grading included · plus re-run 20m 4s');
    });

    it('should report the shortfall of a run that finished with errors', () => {
      const run = buildRun({ status: 'CompletedWithErrors', answeredQuestionCount: 16, totalQuestionCount: 18 });

      expect(component.answerShortfallOf(run)).toEqual({ answered: 16, total: 18 });
    });

    it('should report the shortfall of a cancelled run', () => {
      const run = buildRun({ status: 'Canceled', answeredQuestionCount: 3, totalQuestionCount: 18 });

      expect(component.answerShortfallOf(run)).toEqual({ answered: 3, total: 18 });
    });

    it('should report no shortfall while running, at a full answer set, or with no suite total', () => {
      expect(component.answerShortfallOf(buildRun({ status: 'Running', answeredQuestionCount: 3, totalQuestionCount: 18 }))).toBeNull();
      expect(component.answerShortfallOf(buildRun({ answeredQuestionCount: 18, totalQuestionCount: 18 }))).toBeNull();
      expect(component.answerShortfallOf(buildRun({ answeredQuestionCount: 0, totalQuestionCount: 0 }))).toBeNull();
    });

    it('should badge a changed run option as an option change, not as instrument drift', () => {
      // Runs 24 and 25: verboseMode flipped, so the candidate prompt hash moved with it.
      component.historyRuns = [
        buildRun({
          id: 25,
          candidateSystemPromptSha256: 'bb19dc24',
          candidatePromptOptionsJson: '{"verboseMode":true,"enableToolUse":true}'
        }),
        buildRun({
          id: 24,
          candidateSystemPromptSha256: 'e9b3e9a7',
          candidatePromptOptionsJson: '{"verboseMode":false,"enableToolUse":true}'
        })
      ];

      const change = component.instrumentChangeOf(component.historyRuns[0]);

      expect(change?.kind).toBe('options');
      expect(change?.comparedToRunId).toBe(24);
      expect(change?.description).toContain('verboseMode');
    });

    it('should badge a moved hash as instrument drift when the options match', () => {
      component.historyRuns = [
        buildRun({ id: 25, knowledgeBaseHeadSha: 'kb-b' }),
        buildRun({ id: 24 })
      ];

      const change = component.instrumentChangeOf(component.historyRuns[0]);

      expect(change?.kind).toBe('instrument');
      expect(change?.comparedToRunId).toBe(24);
      expect(change?.description).toContain('knowledge base');
    });

    it('should fall back to the hash comparison when the options cannot be parsed', () => {
      component.historyRuns = [
        buildRun({ id: 25, knowledgeBaseHeadSha: 'kb-b', candidatePromptOptionsJson: 'not json' }),
        buildRun({ id: 24 })
      ];

      expect(component.instrumentChangeOf(component.historyRuns[0])?.kind).toBe('instrument');
    });

    it('should badge a moved GnollHack wiki HEAD as instrument drift', () => {
      component.historyRuns = [
        buildRun({ id: 25, wikiHeadSha: 'wiki-b' }),
        buildRun({ id: 24 })
      ];

      const change = component.instrumentChangeOf(component.historyRuns[0]);

      expect(change?.kind).toBe('instrument');
      expect(change?.description).toContain('GnollHack wiki');
    });

    it('should badge a moved GnollHack source HEAD as instrument drift', () => {
      component.historyRuns = [
        buildRun({ id: 25, sourceCodeHeadSha: 'src-b' }),
        buildRun({ id: 24 })
      ];

      const change = component.instrumentChangeOf(component.historyRuns[0]);

      expect(change?.kind).toBe('instrument');
      expect(change?.description).toContain('GnollHack source');
    });

    it('should report no drift when a corpus HEAD is recorded on only one of the two runs', () => {
      // "Not recorded" on either side is not "unchanged", so neither direction may be badged.
      component.historyRuns = [
        buildRun({ id: 25, wikiHeadSha: null, sourceCodeHeadSha: 'src-a' }),
        buildRun({ id: 24, wikiHeadSha: 'wiki-a', sourceCodeHeadSha: null })
      ];

      expect(component.instrumentChangeOf(component.historyRuns[0])).toBeNull();

      component.historyRuns = [
        buildRun({ id: 27, wikiHeadSha: 'wiki-b', sourceCodeHeadSha: 'src-b' }),
        buildRun({ id: 26, wikiHeadSha: null, sourceCodeHeadSha: null })
      ];

      expect(component.instrumentChangeOf(component.historyRuns[0])).toBeNull();
    });

    it('should badge nothing when both the options and all five hashes match', () => {
      component.historyRuns = [buildRun({ id: 25 }), buildRun({ id: 24 })];

      expect(component.instrumentChangeOf(component.historyRuns[0])).toBeNull();
    });

    it('should sort the Duration column by the figure each cell shows', () => {
      component.historyRuns = [
        // The cancelled run's answers took the longest, but only 100s of wall clock elapsed.
        buildRun({ id: 3, status: 'Canceled', totalAnswerDurationMs: 900000, totalDurationMs: 100000 }),
        buildRun({ id: 2, totalAnswerDurationMs: 500000, totalDurationMs: 700000 }),
        buildRun({ id: 1, totalAnswerDurationMs: 300000, totalDurationMs: 300000 })
      ];

      component.historyTable.toggleSort('durationMs');

      expect(component.historyView.map(r => r.id)).toEqual([2, 1, 3]);
    });
  });

  // ---------------------------------------------------------------------------
  // Model Comparison
  //
  // The host owns the selection, the request and the two lists the picker offers, so every
  // guard that keeps a comparison honest — placement, request shape, ordering, scope and
  // persistence — is asserted here rather than in either presentational component.
  // ---------------------------------------------------------------------------
  describe('model comparison', () => {
    function buildRun(id: number, suiteId: number, overrides: any = {}): any {
      return {
        id,
        benchmarkSuiteId: suiteId,
        suiteName: `Suite ${suiteId}`,
        testedModelDisplayNameUsed: `Model ${id}`,
        testedModelProviderUsed: 'Google',
        testedModelIdUsed: 'gemini-2.5-flash',
        assessorModelDisplayNameUsed: 'Claude Opus',
        status: 'Completed',
        startedAtUtc: '2026-09-01T10:00:00Z',
        qualityIndex: 60 + id,
        speedIndex: 80,
        totalAnswerDurationMs: 1000,
        speedMeasurementDegraded: false,
        answeredQuestionCount: 18,
        totalQuestionCount: 18,
        totalDurationMs: 1200,
        ...overrides
      };
    }

    function buildGroup(id: number, suiteId: number): any {
      return {
        id,
        name: `Group ${id}`,
        benchmarkSuiteId: suiteId,
        suiteName: `Suite ${suiteId}`,
        tier: 'Replicate',
        tierLabel: 'Tier A — Replicate',
        crossCondition: false,
        createdAtUtc: '2026-09-05T10:00:00Z',
        modifiedAtUtc: '2026-09-05T10:00:00Z',
        runCount: 3,
        members: [],
        analysisStale: false
      };
    }

    beforeEach(() => {
      component.historyRuns = [buildRun(1, 5), buildRun(2, 5), buildRun(3, 6)];
      component.runGroups = [buildGroup(11, 5), buildGroup(12, 6)];
      fixture.detectChanges();
    });

    // --- The sticky-container regression guard ---

    it('renders the comparison panel inside .benchmark-container, so the sub-tab row stays pinned', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const container = fixture.nativeElement.querySelector('.benchmark-container');
      const panel = fixture.nativeElement.querySelector('#bm-panel-modelcomparison');
      expect(panel).toBeTruthy();
      // Structural, not a computed style: a sticky element only sticks while its parent's box is
      // on screen, and the parent is what this asserts.
      expect(container.contains(panel)).toBeTrue();
    });

    it('gives the comparison panel the same panel class as the other five sub-tabs', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const panel = fixture.nativeElement.querySelector('#bm-panel-modelcomparison');
      expect(panel.classList.contains('benchmark-tab-content')).toBeTrue();
      // gh-tab-panel is defined in no stylesheet in the repository.
      expect(panel.classList.contains('gh-tab-panel')).toBeFalse();
    });

    it('shows a launcher, and mounts nothing of the wizard until it is opened', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const panel = fixture.nativeElement.querySelector('#bm-panel-modelcomparison');
      expect(panel.querySelector('.mc-launcher .mc-launcher-hero')).toBeTruthy();
      // The task itself is a dialog, so neither of the two components is in the panel.
      expect(panel.querySelector('app-comparison-source-picker')).toBeNull();
      expect(panel.querySelector('app-benchmark-model-comparison')).toBeNull();

      // Deferred: nothing of the wizard is constructed for an operator who never opens it, and a
      // modal that appeared without a gesture would leave them pressing Escape onto an empty tab.
      expect(component.comparisonWizardMounted).toBeFalse();
      const dialog = fixture.nativeElement.querySelector('.benchmark-model-comparison-dialog');
      expect(dialog).toBeTruthy();
      expect(dialog.querySelector('app-benchmark-model-comparison')).toBeNull();
      expect(dialog.open).toBeFalse();
    });

    it('reports no pending selection in the launcher, and offers no control that could change one', () => {
      component.comparisonRunIds = [1, 2];
      component.comparisonGroupIds = [11];
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const launcher = fixture.nativeElement.querySelector('.mc-launcher-hero');
      // The picker lives in the wizard, so a "Selected" read-out here would label a control that
      // is not on this panel, and Clear would clear something it never showed.
      const terms = Array.from(launcher.querySelectorAll('dt'))
        .map((dt: any) => dt.textContent.trim());
      expect(terms).not.toContain('Selected');
      expect(launcher.textContent).not.toContain('analysis groups');
      expect(launcher.textContent).not.toContain('Clear selection');
      // No comparison yet: the state list is absent rather than empty.
      expect(launcher.querySelector('.mc-launcher-state')).toBeNull();
      // One action, and it is the one that opens the surface that owns the selection.
      const actions = launcher.querySelectorAll('.mc-launcher-actions button');
      expect(actions.length).toBe(1);
      expect(actions[0].textContent.trim()).toBe('Open Comparison Wizard');
      // The page's primary task, and its only image button outside a dialog footer.
      expect(actions[0].classList.contains('btn-gh')).toBeTrue();
      expect(actions[0].classList.contains('btn-gh-small')).toBeFalse();
      const imageButtons = (Array.from(
        fixture.nativeElement.querySelectorAll('#bm-panel-modelcomparison .btn-gh')) as HTMLElement[])
        .filter(button => !button.closest('dialog'));
      expect(imageButtons).toEqual([actions[0]]);
    });

    it('puts Open Comparison Wizard directly under the lead, before the steps', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const hero = fixture.nativeElement.querySelector('.mc-launcher-hero') as HTMLElement;
      const lead = hero.querySelector('.mc-launcher-lead') as HTMLElement;
      const actions = hero.querySelector('.mc-launcher-actions') as HTMLElement;
      expect(lead.nextElementSibling).toBe(actions);

      const steps = hero.querySelector('ol.mc-launcher-steps') as HTMLElement;
      expect(actions.compareDocumentPosition(steps) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('keeps the steps and the like-for-like note in a non-exclusive disclosure after the action', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const hero = fixture.nativeElement.querySelector('.mc-launcher-hero') as HTMLElement;
      const details = hero.querySelector('details.gh-disclosure.mc-launcher-howto') as HTMLDetailsElement;
      expect(details).toBeTruthy();
      expect(details.hasAttribute('name')).toBeFalse();
      expect(details.querySelector('summary')?.textContent?.trim()).toBe('How the comparison works');
      expect(details.querySelector('ol.mc-launcher-steps')).toBeTruthy();
      expect(details.querySelector('.alert.alert-info[role="note"]')?.textContent)
        .toContain('Only like-for-like runs are charted together');
    });

    it('opens the disclosure on the first visit only, and remembers how the operator left it', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      let details = fixture.nativeElement.querySelector('.mc-launcher-howto') as HTMLDetailsElement;
      expect(details.open).toBeTrue();
      // Recorded closed at once, so the next visit starts closed unless the operator keeps it open.
      expect(JSON.parse(localStorage.getItem(COMPARISON_LAUNCHER_KEY)!)).toEqual({ howItWorksOpen: false });

      // As a page reload does: the state is read from storage again on the next showing.
      component.comparisonHowItWorksOpen = null;
      fixture.nativeElement.querySelector('#bm-tab-run').click();
      fixture.detectChanges();
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      details = fixture.nativeElement.querySelector('.mc-launcher-howto') as HTMLDetailsElement;
      expect(details.open).toBeFalse();

      // The native toggle, dispatched synchronously rather than awaited.
      details.open = true;
      details.dispatchEvent(new Event('toggle'));
      expect(JSON.parse(localStorage.getItem(COMPARISON_LAUNCHER_KEY)!)).toEqual({ howItWorksOpen: true });

      component.comparisonHowItWorksOpen = null;
      fixture.nativeElement.querySelector('#bm-tab-run').click();
      fixture.detectChanges();
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      details = fixture.nativeElement.querySelector('.mc-launcher-howto') as HTMLDetailsElement;
      expect(details.open).toBeTrue();
    });

    it('opens the disclosure when storage throws, and does not throw itself', () => {
      spyOn(localStorage, 'getItem').and.throwError('private browsing');
      spyOn(localStorage, 'setItem').and.throwError('private browsing');

      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const details = fixture.nativeElement.querySelector('.mc-launcher-howto') as HTMLDetailsElement;
      expect(details.open).toBeTrue();
      details.open = false;
      expect(() => details.dispatchEvent(new Event('toggle'))).not.toThrow();
      expect(component.comparisonHowItWorksOpen).toBeFalse();
    });

    // --- The Comparison reports card ---

    /** The card's list requests: the report-pack documents, apart from any run's own list. */
    function comparisonReportLoads(): number {
      return benchmarkServiceMock.listReportDocuments.calls.all()
        .filter(call => (call.args[0] as { origin?: string } | undefined)?.origin === 'reportPack')
        .length;
    }

    it('renders the Comparison reports card below the hero card, over every comparison document', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const launcherDebug = fixture.debugElement.query(By.directive(ReportDocumentsLauncherComponent));
      expect(launcherDebug).toBeTruthy();
      const launcher = launcherDebug.componentInstance as ReportDocumentsLauncherComponent;
      expect(launcher.idPrefix).toBe('mcl');

      const host = launcherDebug.nativeElement as HTMLElement;
      const hero = fixture.nativeElement.querySelector('.mc-launcher-hero') as HTMLElement;
      expect(hero.contains(host)).toBeFalse();
      expect(hero.compareDocumentPosition(host) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      // The lead paragraph is the card's info tip now.
      expect(fixture.nativeElement.querySelector('.mc-launcher-library-lead')).toBeNull();
      expect(host.querySelector('#mcl-tip')?.textContent).toContain('Every report document written from a model comparison, newest first');
      expect(host.querySelector('#mcl-open')?.textContent?.trim()).toBe('Open Download Center');
      // No document yet: the button is aria-disabled, and the summary says why.
      expect(host.querySelector('#mcl-open')?.getAttribute('aria-disabled')).toBe('true');
      expect(host.querySelector('.rdl-launcher-summary')?.textContent).toContain('No reports yet.');

      expect(benchmarkServiceMock.listReportDocuments).toHaveBeenCalledWith(
        jasmine.objectContaining({ origin: 'reportPack', take: 500 }));
    });

    it('loads the Comparison reports only once the tab is shown, then on every showing and wizard close', () => {
      // Page load, on another tab: nothing of the card exists and nothing is fetched for it.
      expect(fixture.nativeElement.querySelector('app-report-documents-launcher')).toBeNull();
      expect(comparisonReportLoads()).toBe(0);

      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();
      // One load on the first showing, not one on init and a second for the reload token.
      expect(comparisonReportLoads()).toBe(1);

      // A stable scope: another change-detection pass does not read it as a new one.
      fixture.detectChanges();
      expect(comparisonReportLoads()).toBe(1);

      fixture.nativeElement.querySelector('#bm-tab-run').click();
      fixture.detectChanges();
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();
      expect(comparisonReportLoads()).toBe(2);

      // The wizard's Report Pack may have written documents.
      component.onComparisonWizardClose();
      fixture.detectChanges();
      expect(comparisonReportLoads()).toBe(3);
    });

    it('states what the last comparison produced once one exists', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      component.comparison = {
        baselineSuiteName: 'Suite 5',
        pricingBasis: 'Current',
        pricingBasisLabel: 'Current prices',
        comparableCount: 2,
        entries: [{}, {}, {}],
        computedAtUtc: '2026-09-08T10:00:00Z'
      } as any;
      // As runComparison does on its response.
      (component as unknown as { cdr: ChangeDetectorRef }).cdr.detectChanges();

      const state = fixture.nativeElement.querySelector('.mc-launcher .mc-launcher-state');
      expect(state).toBeTruthy();
      const terms = Array.from(state.querySelectorAll('dt')).map((dt: any) => dt.textContent.trim());
      expect(terms).toEqual(['Suite', 'Pricing basis', 'Charted', 'Computed']);
      expect(state.textContent).toContain('2 of 3 entries');
    });

    it('lists the four wizard steps under the titles the wizard itself uses', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const items = Array.from(
        fixture.nativeElement.querySelectorAll('.mc-launcher-hero .mc-launcher-howto ol.mc-launcher-steps > li')) as HTMLElement[];
      expect(items.length).toBe(4);
      expect(COMPARISON_WIZARD_STEPS.map(step => step.title)).toEqual(['Sources', 'Charts & table', 'Reports', 'Documents']);
      expect(items.map(item => item.querySelector('strong')?.textContent?.trim()))
        .toEqual(COMPARISON_WIZARD_STEPS.map(step => step.title));
    });

    it('carries one plain-language callout, and none of the internal key vocabulary', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const launcher = fixture.nativeElement.querySelector('.mc-launcher-hero') as HTMLElement;
      expect(launcher.querySelectorAll('.alert.alert-info[role="note"]').length).toBe(1);
      expect(launcher.textContent).not.toContain('must-match');
    });

    it('opens the wizard modally from the launcher, and keeps it mounted after a close', () => {
      fixture.nativeElement.querySelector('#bm-tab-modelcomparison').click();
      fixture.detectChanges();

      const dialog = fixture.nativeElement
        .querySelector('.benchmark-model-comparison-dialog') as HTMLDialogElement;
      const showModal = spyOn(dialog, 'showModal').and.callThrough();

      component.openComparisonWizard();
      fixture.detectChanges();

      expect(showModal).toHaveBeenCalledTimes(1);
      expect(component.comparisonWizardMounted).toBeTrue();
      expect(dialog.querySelector('app-benchmark-model-comparison')).toBeTruthy();
      expect(dialog.querySelector('app-comparison-source-picker')).toBeTruthy();

      component.closeComparisonWizard();
      fixture.detectChanges();

      // Mount-once, destroy-never: reopening has to preserve the picker's table state, the step,
      // the filters, the entry selection and the rendered charts.
      expect(component.comparisonWizardMounted).toBeTrue();
      expect(dialog.querySelector('app-benchmark-model-comparison')).toBeTruthy();
    });

    it('refuses Escape while an export is running, and allows it otherwise', () => {
      component.openComparisonWizard();
      fixture.detectChanges();

      const cancel = new Event('cancel', { cancelable: true });
      component.onComparisonWizardCancel(cancel);
      expect(cancel.defaultPrevented).toBeFalse();

      // An export re-renders charts and writes files in sequence; tearing the DOM out from under
      // it would leave a detached chart and a half-written batch.
      component.comparisonWizard!.exporting = true;
      const blocked = new Event('cancel', { cancelable: true });
      component.onComparisonWizardCancel(blocked);
      expect(blocked.defaultPrevented).toBeTrue();
    });

    it('refuses Escape while the wizard draws and uploads document charts', () => {
      component.openComparisonWizard();
      fixture.detectChanges();
      const wizard = component.comparisonWizard!;
      expect(wizard.chartsPublishing).toBeFalse();

      (wizard as unknown as { pendingPublishes: number }).pendingPublishes = 1;
      expect(wizard.chartsPublishing).toBeTrue();
      const blocked = new Event('cancel', { cancelable: true });
      component.onComparisonWizardCancel(blocked);
      expect(blocked.defaultPrevented).toBeTrue();

      (wizard as unknown as { pendingPublishes: number }).pendingPublishes = 0;
      const allowed = new Event('cancel', { cancelable: true });
      component.onComparisonWizardCancel(allowed);
      expect(allowed.defaultPrevented).toBeFalse();
    });

    it('loads the comparability index for the sources on offer, and survives it failing', () => {
      benchmarkServiceMock.getComparabilityIndex.calls.reset();

      // A new suite scope is a new set of offered sources, so it re-indexes them.
      component.onComparisonSuiteChange(5);

      expect(benchmarkServiceMock.getComparabilityIndex).toHaveBeenCalledWith({
        runIds: [1, 2],
        groupIds: [11]
      });
      expect(component.comparabilityIndex).toBeTruthy();

      benchmarkServiceMock.getComparabilityIndex.and.returnValue(
        throwError(() => ({ error: 'The index could not be built.' })));
      component.onComparisonSuiteChange(6);

      // Non-fatal: the Condition column falls back to a dash and Compare still works.
      expect(component.comparabilityIndex).toBeNull();
      expect(component.comparabilityIndexError).toContain('could not be built');
    });

    it('derives the wizard band notices from the index, the selection and the pricing basis', () => {
      // One owner: the picker's checkboxes and the wizard's band both read this list, so neither
      // can hold its own account of what the selection costs.
      component.comparabilityIndexError = null;
      component.comparabilityIndexLoading = false;
      component.comparabilityIndex = {
        computedAtUtc: '2026-09-07T12:00:00Z',
        entries: [
          {
            key: 'run:1', sourceKind: 'Run', sourceId: 1, conditionOrdinal: 1,
            conditionLabel: 'Condition A', signature: 'sig-a', selfInconsistent: false,
            selfInconsistentKeys: [], differencesFromLargest: [],
            questionParallelism: '1', speedCalibration: 'speed-a', pricingSnapshot: '2026-09-01'
          },
          {
            key: 'run:2', sourceKind: 'Run', sourceId: 2, conditionOrdinal: 1,
            conditionLabel: 'Condition A', signature: 'sig-a', selfInconsistent: false,
            selfInconsistentKeys: [], differencesFromLargest: [],
            questionParallelism: '1', speedCalibration: 'speed-a', pricingSnapshot: '2026-09-01'
          },
          {
            key: 'run:3', sourceKind: 'Run', sourceId: 3, conditionOrdinal: 2,
            conditionLabel: 'Condition B', signature: 'sig-b', selfInconsistent: false,
            selfInconsistentKeys: [], differencesFromLargest: [],
            questionParallelism: '1', speedCalibration: 'speed-a', pricingSnapshot: '2026-09-01'
          }
        ],
        conditions: [
          {
            ordinal: 1, label: 'Condition A', sourceCount: 2, runCount: 2,
            signature: 'sig-a', newestRunStartedAtUtc: '2026-09-05T10:00:00Z'
          },
          {
            ordinal: 2, label: 'Condition B', sourceCount: 1, runCount: 1,
            signature: 'sig-b', newestRunStartedAtUtc: '2026-09-04T10:00:00Z'
          }
        ],
        largestConditionKeys: [],
        referenceSelectionRule: 'The reference condition is the one with the most sources.',
        mustMatchKeyNames: ['BenchmarkSuiteId'],
        modelAxisKeyNames: ['ModelId'],
        degradingKeyNames: ['PricingSnapshot']
      } as any;

      component.comparisonRunIds = [1, 2];
      component.comparisonGroupIds = [];
      expect(component.comparisonSelectionNotices).toEqual([]);

      component.comparisonRunIds = [1, 2, 3];
      expect(component.comparisonSelectionNotices.map(notice => notice.id))
        .toEqual(['cross-condition']);

      component.comparabilityIndex = null;
      component.comparabilityIndexError = 'The index could not be built.';
      const failed = component.comparisonSelectionNotices;
      expect(failed.map(notice => notice.id)).toEqual(['index-error']);
      expect(failed[0].severity).toBe('error');
      expect(failed[0].body).toContain('The index could not be built.');
    });

    it('drops the payload when the selection changes, so Compare is asked for again', () => {
      component.comparison = { entries: [] } as any;
      component.onComparisonSelectionChange({ runIds: [1], groupIds: [] });

      // The figures on hand describe the previous set of sources. It is also what the wizard reads
      // to know Compare has not run for this selection yet.
      expect(component.comparison).toBeNull();
    });

    // --- The selection band's chips ---

    it('names every selected source for the band, runs then groups, skipping one outside suite scope', () => {
      component.comparisonSuiteId = 5;
      component.comparisonRunIds = [1, 3, 2];
      component.comparisonGroupIds = [11, 12];

      // Run 3 and group 12 belong to suite 6, which the current scope no longer offers: skipped
      // rather than rendered as a placeholder, same as the picker's own checkboxes.
      expect(component.comparisonSelectedSources).toEqual([
        { kind: 'run', id: 1, label: 'Model 1', provider: 'Google', detail: '#1' },
        { kind: 'run', id: 2, label: 'Model 2', provider: 'Google', detail: '#2' },
        { kind: 'group', id: 11, label: 'Group 11', provider: null, detail: '3 runs' }
      ]);
    });

    it('names a group of one run in the singular', () => {
      component.runGroups = [...component.runGroups, { ...buildGroup(13, 5), runCount: 1 }];
      component.comparisonGroupIds = [13];

      expect(component.comparisonSelectedSources).toEqual([
        { kind: 'group', id: 13, label: 'Group 13', provider: null, detail: '1 run' }
      ]);
    });

    it('removes one source through the same path every other selection change takes', () => {
      component.comparisonRunIds = [1, 2];
      component.comparisonGroupIds = [11];
      component.comparison = { entries: [] } as any;

      component.onComparisonRemoveSource(
        { kind: 'run', id: 1, label: 'Model 1', provider: 'Google', detail: '#1' });

      expect(component.comparisonRunIds).toEqual([2]);
      expect(component.comparisonGroupIds).toEqual([11]);
      // Persistence, the dropped payload and the Compare reset are onComparisonSelectionChange's
      // job, so routing through it is what keeps them all in force after a chip is removed.
      expect(component.comparison).toBeNull();
    });

    // --- The .gh-dialog-fullscreen lift ---

    it('leaves the suite health dialog opening and closing after the full-screen lift', () => {
      // Its viewport sizing, transition and backdrop now come from styles.scss, and it is the only
      // other consumer of that block, so this is the regression guard for the move.
      const dialog = fixture.nativeElement
        .querySelector('.benchmark-suite-health-dialog') as HTMLDialogElement;
      expect(dialog.classList.contains('gh-dialog-fullscreen')).toBeTrue();

      component.suiteHealthSuiteId = null;
      component.openSuiteHealth({ id: 5, name: 'Suite 5', questionCount: 4 } as any);
      fixture.detectChanges();
      expect(dialog.open).toBeTrue();

      component.closeSuiteHealth();
      fixture.detectChanges();
      expect(dialog.open).toBeFalse();
    });

    // --- Loading and the request ---

    it('loads the three lists the picker needs on tab entry, and fetches no comparison', () => {
      benchmarkServiceMock.getRuns.calls.reset();
      benchmarkServiceMock.getRunGroups.calls.reset();
      benchmarkServiceMock.getSuites.calls.reset();
      benchmarkServiceMock.compareModels.calls.reset();

      component.selectSubTab('modelcomparison');

      expect(benchmarkServiceMock.getRuns).toHaveBeenCalled();
      expect(benchmarkServiceMock.getRunGroups).toHaveBeenCalled();
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalled();
      // An unattended request on tab entry would re-price for a selection nobody confirmed.
      expect(benchmarkServiceMock.compareModels).not.toHaveBeenCalled();
    });

    it('issues one request carrying the selected ids and the basis name', () => {
      component.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [11] });
      benchmarkServiceMock.compareModels.calls.reset();

      component.runComparison();

      expect(benchmarkServiceMock.compareModels).toHaveBeenCalledTimes(1);
      expect(benchmarkServiceMock.compareModels).toHaveBeenCalledWith({
        runIds: [1, 2],
        groupIds: [11],
        pricingBasis: 'Current'
      });
    });

    it('refuses an empty selection rather than sending a request the server will reject', () => {
      component.clearComparisonSelection();
      benchmarkServiceMock.compareModels.calls.reset();

      component.runComparison();

      expect(benchmarkServiceMock.compareModels).not.toHaveBeenCalled();
      expect(component.comparisonError).toContain('at least one run');
    });

    it('reports the server error text rather than a generic failure', () => {
      benchmarkServiceMock.compareModels.and.returnValue(
        throwError(() => ({ error: 'Run(s) not found: 4' })));
      component.onComparisonSelectionChange({ runIds: [4], groupIds: [] });

      component.runComparison();

      expect(component.comparisonError).toBe('Run(s) not found: 4');
      expect(component.comparison).toBeNull();
      expect(component.comparisonLoading).toBeFalse();
    });

    it('discards an out-of-order response so the older payload never overwrites the newer', () => {
      const first = new Subject<any>();
      const second = new Subject<any>();
      benchmarkServiceMock.compareModels.and.returnValues(first as any, second as any);
      component.onComparisonSelectionChange({ runIds: [1], groupIds: [] });

      component.runComparison();
      component.runComparison();

      second.next({ entries: [], explanation: 'newer' });
      first.next({ entries: [], explanation: 'older' });

      expect((component.comparison as any).explanation).toBe('newer');
    });

    // --- Cancelling, and never trapping the operator in the wizard while loading ---

    it('cancels the comparison in flight, releasing the request and ignoring its late result', () => {
      const request = new Subject<any>();
      benchmarkServiceMock.compareModels.and.returnValue(request as any);
      component.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [] });

      component.runComparison();
      expect(component.comparisonLoading).toBeTrue();
      expect(request.observed).toBeTrue();

      component.cancelComparison();

      expect(component.comparisonLoading).toBeFalse();
      // Unsubscribed, so the HTTP request is aborted and the server stops pricing.
      expect(request.observed).toBeFalse();
      request.next({ entries: [], explanation: 'late' });
      expect(component.comparison).toBeNull();
    });

    it('releases a superseded request when Compare runs again', () => {
      const first = new Subject<any>();
      const second = new Subject<any>();
      benchmarkServiceMock.compareModels.and.returnValues(first as any, second as any);
      component.onComparisonSelectionChange({ runIds: [1], groupIds: [] });

      component.runComparison();
      component.runComparison();

      expect(first.observed).toBeFalse();
      expect(second.observed).toBeTrue();
    });

    it('drops the request in flight when the selection changes under it', () => {
      const request = new Subject<any>();
      benchmarkServiceMock.compareModels.and.returnValue(request as any);
      component.onComparisonSelectionChange({ runIds: [1], groupIds: [] });
      component.runComparison();

      component.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [] });
      request.next({ entries: [], explanation: 'for the previous selection' });

      // The older response would otherwise chart the previous selection and advance the wizard.
      expect(component.comparison).toBeNull();
      expect(component.comparisonLoading).toBeFalse();
      expect(request.observed).toBeFalse();
    });

    it('treats a cancel with nothing in flight as a no-op', () => {
      const detectChanges = spyOn((component as any).cdr, 'detectChanges').and.callThrough();

      component.cancelComparison();

      expect(component.comparisonLoading).toBeFalse();
      expect(detectChanges).not.toHaveBeenCalled();
    });

    it('never refuses Escape because a comparison is loading', () => {
      component.comparisonLoading = true;
      component.comparisonWizard = { exporting: false } as any;
      const event = { preventDefault: jasmine.createSpy('preventDefault') } as unknown as Event;

      component.onComparisonWizardCancel(event);

      expect(event.preventDefault).not.toHaveBeenCalled();
    });

    it('never switches close requests off on the wizard dialog', () => {
      component.openComparisonWizard();
      fixture.detectChanges();

      const dialog = fixture.nativeElement
        .querySelector('dialog.benchmark-model-comparison-dialog') as HTMLDialogElement;
      expect(dialog).toBeTruthy();
      expect(dialog.getAttribute('closedby')).not.toBe('none');
    });

    it('keeps the projected picker live and the wizard uncovered while a comparison loads', () => {
      const request = new Subject<any>();
      benchmarkServiceMock.compareModels.and.returnValue(request as any);
      component.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [] });
      component.openComparisonWizard();
      fixture.detectChanges();

      component.runComparison();
      fixture.detectChanges();
      expect(component.comparisonLoading).toBeTrue();

      const dialog = fixture.nativeElement
        .querySelector('dialog.benchmark-model-comparison-dialog') as HTMLDialogElement;
      const checkboxes = Array.from(
        dialog.querySelectorAll('#mc-step-panel-1 input[type="checkbox"]')) as HTMLInputElement[];
      expect(checkboxes.length).toBeGreaterThan(0);
      expect(checkboxes.some(checkbox => !checkbox.disabled)).toBeTrue();
      expect(dialog.querySelectorAll('[inert]').length).toBe(0);

      const close = dialog.querySelector('[aria-label="Close cross-model comparison"]') as HTMLButtonElement;
      expect(close.disabled).toBeFalse();

      component.closeComparisonWizard();
    });

    // --- Suite scope ---

    it('scopes both offered lists to the suite scope', () => {
      component.onComparisonSuiteChange(5);

      expect(component.comparisonRunOptions.map(r => r.id)).toEqual([1, 2]);
      expect(component.comparisonGroupOptions.map(g => g.id)).toEqual([11]);

      component.onComparisonSuiteChange(null);
      expect(component.comparisonRunOptions.length).toBe(3);
      expect(component.comparisonGroupOptions.length).toBe(2);
    });

    it('drops out-of-scope ids when the suite scope changes', () => {
      component.onComparisonSelectionChange({ runIds: [1, 3], groupIds: [11, 12] });

      component.onComparisonSuiteChange(5);

      // Run 3 and group 12 belong to suite 6: leaving them selected is how a figure ends up with
      // a model the picker does not show.
      expect(component.comparisonRunIds).toEqual([1]);
      expect(component.comparisonGroupIds).toEqual([11]);
    });

    it('clears the figures when nothing survives a suite scope change', () => {
      component.onComparisonSelectionChange({ runIds: [3], groupIds: [] });
      component.comparison = { entries: [] } as any;

      component.onComparisonSuiteChange(5);

      expect(component.comparisonRunIds).toEqual([]);
      expect(component.comparison).toBeNull();
    });

    // --- Pricing basis ---

    it('refetches at once on a pricing basis change, because it re-prices an unchanged set', () => {
      component.onComparisonSelectionChange({ runIds: [1], groupIds: [] });
      benchmarkServiceMock.compareModels.calls.reset();

      component.onComparisonPricingBasisChange('AsRun');

      expect(component.comparisonPricingBasis).toBe('AsRun');
      expect(benchmarkServiceMock.compareModels).toHaveBeenCalledWith(
        jasmine.objectContaining({ pricingBasis: 'AsRun' }));
    });

    // --- Persistence ---

    it('remembers the selection, the scope and the basis across a reload', () => {
      component.onComparisonSuiteChange(5);
      component.onComparisonSelectionChange({ runIds: [1, 2], groupIds: [11] });

      const stored = JSON.parse(localStorage.getItem(COMPARISON_SELECTION_KEY)!);
      expect(stored).toEqual({
        runIds: [1, 2], groupIds: [11], suiteId: 5, pricingBasis: 'Current'
      });
    });

    it('drops a persisted id that no longer exists rather than sending it', () => {
      localStorage.setItem(COMPARISON_SELECTION_KEY, JSON.stringify({
        runIds: [1, 999], groupIds: [11, 888], suiteId: null, pricingBasis: 'AsRun'
      }));

      component.selectSubTab('modelcomparison');

      // getRuns and getRunGroups both resolve to [] under the default mocks, so the lists that
      // validate the restore are re-seeded here to what the tab actually offers.
      component.historyRuns = [buildRun(1, 5)];
      component.runGroups = [buildGroup(11, 5)];
      (component as any).pruneComparisonSelection();

      expect(component.comparisonRunIds).toEqual([1]);
      expect(component.comparisonGroupIds).toEqual([11]);
      expect(component.comparisonPricingBasis).toBe('AsRun');
    });

    it('survives a localStorage read that throws, leaving every default standing', () => {
      spyOn(localStorage, 'getItem').and.throwError('private browsing');

      expect(() => component.selectSubTab('modelcomparison')).not.toThrow();
      expect(component.comparisonRunIds).toEqual([]);
      expect(component.comparisonPricingBasis).toBe('Current');
    });
  });

  describe('YAML import and export, snapshot upload and delete', () => {
    const suite = {
      id: 1, name: 'Default Suite', description: 'Test', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null,
      questionCount: 2, assessedQuestionCount: 2, difficultyFullyAssessed: true,
      gameSnapshotId: 7, gameSnapshotName: 'Low HP', gameSnapshotCharCount: 12000
    } as any;
    const questions = [
      { id: 11, benchmarkSuiteId: 1, orderIndex: 1, questionText: 'First?', difficulty: 1, expectedPoints: '- a', createdAtUtc: '' },
      { id: 12, benchmarkSuiteId: 1, orderIndex: 2, questionText: 'Second?', difficulty: 3, expectedPoints: null, createdAtUtc: '' }
    ] as any[];

    function host(): HTMLElement {
      return fixture.nativeElement as HTMLElement;
    }

    function tooltipTexts(container: Element): string[] {
      return Array.from(container.querySelectorAll('.gh-tooltip')).map(t => (t.textContent ?? '').trim());
    }

    function showQuestions(): void {
      component.activeSubTab = 'suites';
      component.suites = [{ ...suite }];
      component.currentSuiteForQuestions = component.suites[0];
      component.questions = questions.map(q => ({ ...q }));
      component.loadingQuestions = false;
      fixture.detectChanges();
    }

    it('renders the four toolbar icon buttons with their tooltips', () => {
      showQuestions();
      const icons = host().querySelector('.questions-toolbar-icons')!;
      expect(tooltipTexts(icons)).toEqual(['Download All as YAML', 'Copy All to Clipboard', 'Import Questions from YAML', 'Import/Export Help']);
      expect(Array.from(icons.querySelectorAll('button')).every(b => b.getAttribute('aria-label'))).toBeTrue();
    });

    it('gives every per-question YAML button a distinct accessible name', () => {
      showQuestions();
      const labels = Array.from(host().querySelectorAll('.card-actions-group button'))
        .map(b => b.getAttribute('aria-label') ?? '')
        .filter(l => l.includes('YAML'));
      expect(labels).toContain('Download question 1 as YAML');
      expect(labels).toContain('Copy question 2 as YAML to the clipboard');
      expect(labels).toContain('Replace question 2 from YAML');
      expect(new Set(labels).size).toBe(labels.length);
    });

    it('renders the suite card export buttons, the toolbar import and Upload Snapshot', () => {
      showQuestions();
      const card = host().querySelector('.suite-card')!;
      expect(tooltipTexts(card.querySelector('.suite-card-export')!)).toEqual(['Download Suite as YAML', 'Copy Suite as YAML to Clipboard']);
      const toolbarLabels = Array.from(host().querySelectorAll('.suites-toolbar button')).map(b => (b.textContent ?? '').trim());
      expect(toolbarLabels).toContain('Import Suite from YAML');
      expect(card.querySelector('.upload-snapshot-card-btn')!.getAttribute('aria-disabled')).toBeNull();
    });

    it('offers suite YAML help on the Manage Suites toolbar', () => {
      showQuestions();
      const toolbar = host().querySelector('.suites-toolbar-grouped')!;
      const help = toolbar.querySelector('.action-btn') as HTMLButtonElement;
      expect(help.getAttribute('aria-label')).toBe('Open suite YAML import and export help');
      expect(tooltipTexts(toolbar)).toEqual(['Suite Import/Export Help']);

      const openSuite = spyOn(component.suiteYamlHelpDialog!, 'open');
      const openQuestions = spyOn(component.questionYamlHelpDialog!, 'open');
      help.click();
      expect(openSuite).toHaveBeenCalled();
      expect(openQuestions).not.toHaveBeenCalled();
    });

    it('places the Snapshot Suite Wizard after Import Suite from YAML and before the help icon', () => {
      showQuestions();
      const toolbar = host().querySelector('.suites-toolbar-grouped')!;
      const buttons = Array.from(toolbar.querySelectorAll('button'));
      const labels = buttons.map(b => (b.textContent ?? '').trim());
      const wizardIndex = labels.indexOf('Snapshot Suite Wizard');
      expect(wizardIndex).toBe(labels.indexOf('Import Suite from YAML') + 1);
      expect(buttons[wizardIndex + 1].classList).toContain('action-btn');
      expect(buttons[wizardIndex].classList).toContain('btn-ghost');

      const open = spyOn(component.snapshotSuiteWizard!, 'open');
      buttons[wizardIndex].click();
      expect(open).toHaveBeenCalled();
    });

    it('closes the suite help before opening the wizard it asks for', () => {
      showQuestions();
      const order: string[] = [];
      spyOn(component.suiteYamlHelpDialog!, 'close').and.callFake(() => { order.push('close help'); });
      spyOn(component.snapshotSuiteWizard!, 'open').and.callFake(() => { order.push('open wizard'); });
      component.onSuiteWizardRequestedFromHelp();
      expect(order).toEqual(['close help', 'open wizard']);
    });

    it('opens the assessor for the current copy of the suite the wizard names', () => {
      showQuestions();
      const assess = spyOn(component, 'openDifficultyAssessorDialog');
      const stale = { ...component.suites[0], questionCount: 0 };
      component.onWizardAssessRequested(stale);
      expect(assess).toHaveBeenCalledWith(component.suites[0]);
    });

    it('reloads the suites when the wizard applies a description', () => {
      showQuestions();
      const load = spyOn(component, 'loadSuites');
      component.onWizardSuiteUpdated();
      expect(load).toHaveBeenCalled();
    });

    it('exports an empty snapshot suite without asking for its questions, and keeps a bare suite inert', async () => {
      showQuestions();
      benchmarkServiceMock.getQuestions.calls.reset();
      benchmarkServiceMock.getSnapshot.and.returnValue(of({
        id: 7, name: 'Low HP', sanitizedText: 'GnollHack 4.2.0 Build 47', charCount: 24,
        sha256: 'a'.repeat(64), captureMethod: 'TextUpload', createdAtUtc: ''
      } as any));
      const emptySnapshotSuite = { ...component.suites[0], questionCount: 0, gameSnapshotId: 7 };
      const bare = { ...component.suites[0], questionCount: 0, gameSnapshotId: null };
      expect(component.canExportSuite(emptySnapshotSuite)).toBeTrue();
      expect(component.canExportSuite(bare)).toBeFalse();

      const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
      const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
      try {
        await component.copySuiteYaml(emptySnapshotSuite);
        expect(benchmarkServiceMock.getQuestions).not.toHaveBeenCalled();
        const yaml = writeText.calls.mostRecent().args[0] as string;
        expect(yaml).toContain('\nquestions: []\n');
        expect(yaml).toContain('  snapshot:\n');

        writeText.calls.reset();
        await component.copySuiteYaml(bare);
        expect(writeText).not.toHaveBeenCalled();
      } finally {
        delete (navigator as { clipboard?: unknown }).clipboard;
        if (original) Object.defineProperty(navigator, 'clipboard', original);
      }
    });

    it('routes the import dialog help request by the mode the import was opened for', () => {
      showQuestions();
      const openSuite = spyOn(component.suiteYamlHelpDialog!, 'open');
      const openQuestions = spyOn(component.questionYamlHelpDialog!, 'open');

      component.questionYamlImportDialog!.mode = 'suite';
      component.onYamlHelpRequested();
      expect(openSuite).toHaveBeenCalled();
      expect(openQuestions).not.toHaveBeenCalled();

      component.questionYamlImportDialog!.mode = 'questions';
      component.onYamlHelpRequested();
      expect(openQuestions).toHaveBeenCalled();
    });

    it('exports a suite with its whole snapshot: text, hash and metadata', async () => {
      showQuestions();
      benchmarkServiceMock.getQuestions.and.returnValue(of(questions.map(q => ({ ...q }))));
      benchmarkServiceMock.getSnapshot.and.returnValue(of({
        id: 7, name: 'Low HP', sanitizedText: 'GnollHack 4.2.0 Build 47\nDlvl:11 HP:14(58)', charCount: 44,
        sha256: 'a'.repeat(64), captureMethod: 'TextUpload', sourceGnollHackVersion: '4.2.0 Build 47',
        notes: 'From the viewer.', capturedAtUtc: '2026-09-16T18:04:11Z', createdAtUtc: ''
      } as any));

      const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
      const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
      try {
        await component.copySuiteYaml(component.suites[0]);
        expect(benchmarkServiceMock.getSnapshot).toHaveBeenCalledWith(7, true);
        const yaml = writeText.calls.mostRecent().args[0] as string;
        expect(yaml).toContain('  snapshot:\n');
        expect(yaml).toContain('    sha256: "' + 'a'.repeat(64) + '"');
        expect(yaml).toContain('    text: |\n');
        expect(yaml).toContain('      GnollHack 4.2.0 Build 47');
        expect(component.suitesCopyStatus).toContain('Copied suite Default Suite as YAML');
      } finally {
        delete (navigator as { clipboard?: unknown }).clipboard;
        if (original) Object.defineProperty(navigator, 'clipboard', original);
      }
    });

    it('exports without the board, and says so, when the snapshot fetch fails', async () => {
      showQuestions();
      benchmarkServiceMock.getQuestions.and.returnValue(of(questions.map(q => ({ ...q }))));
      benchmarkServiceMock.getSnapshot.and.returnValue(throwError(() => new Error('gone')));

      const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
      const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
      try {
        await component.copySuiteYaml(component.suites[0]);
        expect(writeText.calls.mostRecent().args[0] as string).not.toContain('snapshot');
        expect(component.suitesCopyStatus).toContain('Exported without the snapshot text');
      } finally {
        delete (navigator as { clipboard?: unknown }).clipboard;
        if (original) Object.defineProperty(navigator, 'clipboard', original);
      }
    });

    it('disables Upload Snapshot while a generation job runs on that suite', () => {
      component.runningGenerationSuiteId = 1;
      showQuestions();
      const button = host().querySelector('.suite-card .upload-snapshot-card-btn') as HTMLButtonElement;
      expect(button.getAttribute('aria-disabled')).toBe('true');

      const open = spyOn(component.snapshotUploadDialog!, 'open');
      button.click();
      expect(open).not.toHaveBeenCalled();
      expect(component.snapshotDeleteBlockedReason).toBeNull();
    });

    it('copies one question as YAML', async () => {
      showQuestions();
      const writeText = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
      const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true, writable: true });
      try {
        await component.copyQuestionYaml(component.questions[0]);
        expect(writeText).toHaveBeenCalledWith(serializeQuestionsYaml([component.questions[0]], component.currentSuiteForQuestions));
        expect(component.questionsCopyStatus).toBe('Copied question 1 as YAML.');
      } finally {
        delete (navigator as { clipboard?: unknown }).clipboard;
        if (original) Object.defineProperty(navigator, 'clipboard', original);
      }
    });

    it('reloads questions and suites after an import', () => {
      showQuestions();
      benchmarkServiceMock.getQuestions.calls.reset();
      benchmarkServiceMock.getSuites.calls.reset();

      component.onQuestionsImported({ createdCount: 1, replacedCount: 0, unchangedCount: 0, questions: [] });
      expect(benchmarkServiceMock.getQuestions).toHaveBeenCalledWith(1);
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalled();

      benchmarkServiceMock.getSuites.calls.reset();
      component.onSuiteImported({ ...suite, id: 5, name: 'Imported' });
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalled();
    });

    it('patches the suite card after an upload', () => {
      showQuestions();
      const card = component.suites[0];
      Object.assign(card, { gameSnapshotId: null });
      component.onSnapshotUploaded({
        board: { id: 40, name: 'New board', charCount: 321 } as any,
        suite: { ...suite, gameSnapshotId: 40, gameSnapshotName: 'New board', gameSnapshotCharCount: 321 }
      });
      // loadSuites then replaces the list from the mock, so the patched card object is checked.
      expect(card.gameSnapshotId).toBe(40);
      expect(card.gameSnapshotName).toBe('New board');
    });

    it('clears the snapshot fields after a delete and reloads', () => {
      showQuestions();
      benchmarkServiceMock.getSuites.calls.reset();
      const card = component.suites[0];

      component.onSnapshotDeleted(7);

      expect(card.gameSnapshotId).toBeNull();
      expect(card.gameSnapshotName).toBeNull();
      expect(component.currentSuiteForQuestions!.gameSnapshotId).toBeNull();
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalled();
    });
  });

  // ---------------------------------------------------------------------------
  // Two-family assessor panel: the launcher's co-assessor, and the run detail's
  // panel rendering, member re-assessment and calibration target.
  // ---------------------------------------------------------------------------
  describe('assessor panel launcher', () => {
    /**
     * Id 1 is the fixture's Anthropic claude-3-5-sonnet. The rest are the other roles a panel run
     * needs: two OpenAI models, an older Anthropic model, a Google model, and a second
     * configuration of the candidate's own model.
     */
    function usePanelConfigs(): void {
      const base = component.systemConfigs[0];
      component.systemConfigs = [
        base,
        { ...base, id: 2, displayName: 'GPT-5 Mini', provider: 'OpenAI', modelId: 'gpt-5-mini' },
        { ...base, id: 3, displayName: 'Gemini 3.7 Pro', provider: 'Google', modelId: 'gemini-3.7-pro' },
        { ...base, id: 4, displayName: 'Claude Opus 4', provider: 'Anthropic', modelId: 'claude-opus-4' },
        { ...base, id: 5, displayName: 'GPT-4.1', provider: 'OpenAI', modelId: 'gpt-4.1' },
        { ...base, id: 6, displayName: 'Sonnet (second key)', provider: 'Anthropic', modelId: 'claude-3-5-sonnet' }
      ];
    }

    /** A valid panel: Anthropic candidate, OpenAI member A, an older Anthropic member B. */
    function selectValidPanel(): void {
      usePanelConfigs();
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 2;
      component.coAssessorConfigId = 4;
    }

    function startButton(): HTMLButtonElement {
      return fixture.nativeElement.querySelector('.form-actions .btn-gh') as HTMLButtonElement;
    }

    it('should offer a co-assessor selector that defaults to none', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      expect(component.coAssessorConfigId).toBeNull();
      expect(component.isPanelLaunch).toBeFalse();
      const trigger = fixture.nativeElement.querySelector('.co-assessor-model-selector .selector-trigger') as HTMLButtonElement;
      expect(trigger).toBeTruthy();
      expect(trigger.textContent).toContain('None — single assessor');
    });

    it('should select a co-assessor through its dropdown, and clear it with the None option', () => {
      usePanelConfigs();
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const trigger = fixture.nativeElement.querySelector('.co-assessor-model-selector .selector-trigger') as HTMLButtonElement;
      trigger.click();
      fixture.detectChanges();

      const options = Array.from(fixture.nativeElement.querySelectorAll('.co-assessor-model-selector .model-option')) as HTMLElement[];
      expect(options[0].textContent).toContain('None — single assessor');
      options.find(o => o.textContent?.includes('Claude Opus 4'))!.click();
      fixture.detectChanges();
      expect(component.coAssessorConfigId).toBe(4);
      expect(trigger.textContent).toContain('Claude Opus 4');

      trigger.click();
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('.co-assessor-model-selector .model-option') as HTMLElement).click();
      fixture.detectChanges();
      expect(component.coAssessorConfigId).toBeNull();
    });

    it('should send the co-assessor only when one is selected', () => {
      benchmarkServiceMock.startRun.and.returnValue(of({ runId: 99 }));
      spyOn(component.runProgressDialog.nativeElement, 'showModal');
      usePanelConfigs();
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 2;

      component.startBenchmark();
      const single = benchmarkServiceMock.startRun.calls.mostRecent().args[0];
      expect('coAssessorModelConfigurationId' in single).toBeFalse();

      component.coAssessorConfigId = 4;
      component.startBenchmark();
      expect(benchmarkServiceMock.startRun.calls.mostRecent().args[0].coAssessorModelConfigurationId).toBe(4);
      component.ngOnDestroy();
    });

    it('should persist the co-assessor when a run is started', () => {
      benchmarkServiceMock.startRun.and.returnValue(of({ runId: 99 }));
      selectValidPanel();

      component.startBenchmark();

      const stored = JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!);
      expect(stored.coAssessorConfigId).toBe(4);
      component.ngOnDestroy();
    });

    it('should restore a remembered co-assessor, and drop one that no longer qualifies', () => {
      usePanelConfigs();
      const configs = component.systemConfigs;

      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1, testedConfigId: 1, assessorConfigId: 2, coAssessorConfigId: 4
      }));
      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = configs;
      restored.detectChanges();
      expect(restored.componentInstance.coAssessorConfigId).toBe(4);
      restored.componentInstance.ngOnDestroy();

      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1, testedConfigId: 1, assessorConfigId: 2, coAssessorConfigId: 77
      }));
      const dropped = TestBed.createComponent(AdminBenchmarkComponent);
      dropped.componentInstance.systemConfigs = configs;
      dropped.detectChanges();
      expect(dropped.componentInstance.coAssessorConfigId).toBeNull();
      dropped.componentInstance.ngOnDestroy();
    });

    it('should fix the reference reader coverage at every answer and name the reference reader', fakeAsync(() => {
      usePanelConfigs();
      component.secondOpinionConfigId = 3;
      component.secondOpinionMode = 1;
      component.coAssessorConfigId = 4;
      component.activeSubTab = 'run';
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      // A panel run's coverage is not a choice: a read-only line replaces the select.
      expect(fixture.nativeElement.querySelector('#secondOpinionModeSelect')).toBeNull();
      const fixed = fixture.nativeElement.querySelector('.second-opinion-mode-fixed') as HTMLElement;
      expect(fixed.querySelector('#secondOpinionModeFixedLabel')?.textContent?.trim()).toBe('Coverage');
      expect(fixed.textContent).toContain('Every answer, blind');
      expect(component.secondOpinionMode).toBe(3);

      const label = fixture.nativeElement.querySelector('#bmSecondOpinionModelLabel') as HTMLElement;
      expect(label.textContent?.replace(/\s+/g, ' ').trim()).toBe('Reference Reader Optional');
      const tip = fixture.nativeElement.querySelector('#bmSecondOpinionModelHint') as HTMLElement;
      expect(tip.textContent).toContain('A third model grades every answer blind');
      expect(Array.from(tip.querySelectorAll('p > strong')).map(s => s.textContent)).toContain('Recommended:');

      const picker = fixture.debugElement.query(By.css('.second-opinion-model-selector')).componentInstance as ModelPickerComponent;
      expect(picker.noneLabel).toBe('None — no reference reader');

      // The operator's own override is kept, and returns with a single-assessor run.
      component.coAssessorConfigId = null;
      expect(component.secondOpinionMode).toBe(1);
      discardPeriodicTasks();
    }));

    it('should warn and disable Start with a reason when the panel members share a provider', () => {
      selectValidPanel();
      component.coAssessorConfigId = 5;
      component.activeSubTab = 'run';
      fixture.detectChanges();

      expect(component.showCoAssessorSameProviderAdvisory).toBeTrue();
      const advisory = fixture.nativeElement.querySelector('.setup-group-grading .co-assessor-advisory') as HTMLElement;
      expect(advisory).toBeTruthy();
      expect(advisory.classList).toContain('alert-warning');
      expect(advisory.getAttribute('role')).toBe('note');
      expect(advisory.textContent).toContain('Panel members share a provider');

      expect(component.canStartRun).toBeFalse();
      expect(startButton().getAttribute('aria-disabled')).toBe('true');
      expect((fixture.nativeElement.querySelector('#startBenchmarkHint') as HTMLElement).textContent)
        .toContain('must come from different providers');

      component.startBenchmark();
      expect(benchmarkServiceMock.startRun).not.toHaveBeenCalled();
    });

    it('should warn and disable Start when either panel member is the model under test', () => {
      selectValidPanel();
      // Another configuration of the candidate's own provider and model id.
      component.coAssessorConfigId = 6;
      component.activeSubTab = 'run';
      fixture.detectChanges();

      expect(component.showCoAssessorCandidateAdvisory).toBeTrue();
      expect(fixture.nativeElement.querySelector('.setup-group-grading .co-assessor-advisory')?.textContent)
        .toContain('A panel member is the model under test');
      expect(component.startBenchmarkHint).toContain('Neither panel member may be the model under test');
      expect(component.canStartRun).toBeFalse();

      // Member A as the candidate is refused the same way.
      component.coAssessorConfigId = 2;
      component.assessorConfigId = 6;
      expect(component.showCoAssessorCandidateAdvisory).toBeTrue();
    });

    it('should accept a member from the candidate\'s own provider when the model differs', () => {
      selectValidPanel();
      component.activeSubTab = 'run';
      fixture.detectChanges();

      expect(component.panelLaunchRefusal).toBe('');
      expect(component.canStartRun).toBeTrue();
      expect(fixture.nativeElement.querySelector('.co-assessor-advisory')).toBeNull();
    });

    it('should name the roles a reference reader or claim verifier shares a provider with', () => {
      selectValidPanel();
      component.secondOpinionConfigId = 3;
      component.claimVerifierConfigId = 3;
      expect(component.referenceReaderSharedFamilyRoles).toEqual([]);
      expect(component.claimVerifierSharedFamilyRoles).toEqual([]);

      component.secondOpinionConfigId = 5;
      component.claimVerifierConfigId = 1;
      expect(component.referenceReaderSharedFamilyRoles).toEqual(['panel member A']);
      expect(component.claimVerifierSharedFamilyRoles).toEqual(['the model under test', 'panel member B']);
      // The single-run pairing advisory gives way to the panel's fuller one.
      expect(component.showAssessorPairingAdvisory).toBeFalse();

      component.activeSubTab = 'run';
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.reference-reader-family-advisory')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('.claim-verifier-family-advisory')).toBeTruthy();

      // A single-assessor run keeps the silence of before.
      component.coAssessorConfigId = null;
      expect(component.referenceReaderSharedFamilyRoles).toEqual([]);
      expect(component.claimVerifierSharedFamilyRoles).toEqual([]);
    });

    it('should never open the same-provider dialog for a panel run', () => {
      benchmarkServiceMock.startRun.and.returnValue(throwError(() => ({
        status: 409, error: { sameProvider: true, provider: 'Anthropic' }
      })));
      const showModal = spyOn(component.sameProviderDialog.nativeElement, 'showModal');
      selectValidPanel();

      component.startBenchmark();

      expect(showModal).not.toHaveBeenCalled();
      expect(component.sameProviderWarning).toBeNull();
    });
  });

  describe('assessor panel run detail', () => {
    function panelAnswer(overrides: any = {}): any {
      return {
        id: 501, orderIndex: 1, questionText: 'Q1', difficulty: 1, answerText: 'a',
        status: 'Ok', assessmentStatus: 'Scored', durationMs: 1, modelTimeMs: 1,
        scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [],
        qualityScore: 82, panelQualityScore: 78, panelDisagreed: true,
        coAssessmentStatus: 'Scored', coAssessmentQualityScore: 74, coAssessmentCriticalError: false,
        coAssessedByModelDisplayNameUsed: 'Claude Opus 4',
        coAssessmentJson: JSON.stringify({
          accuracyLevel: 5, completenessLevel: 4, concisenessLevel: 6, readabilityLevel: 5,
          criticalError: false, qualityScore: 74,
          comment: 'Misses <b>one</b> step.',
          accuracyEvidence: 'Matches the rubric.',
          completenessEvidence: 'Omits the prayer timeout.',
          unverifiedClaims: ['gnomes gain infravision']
        }),
        secondOpinionQualityScore: 80, secondOpinionCriticalError: false,
        secondOpinionByModelDisplayNameUsed: 'Gemini 3.7 Pro', secondOpinionTrigger: 'All',
        ...overrides
      };
    }

    function buildPanelRun(overrides: any = {}): any {
      return {
        id: 77, benchmarkSuiteId: 1, suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Claude Sonnet 5', testedModelProviderUsed: 'Anthropic',
        testedModelIdUsed: 'claude-sonnet-5', testedModelParallelExecutionModeUsed: 0,
        assessorModelConfigurationId: 2,
        assessorModelDisplayNameUsed: 'GPT-5 Mini', assessorModelProviderUsed: 'OpenAI', assessorModelIdUsed: 'gpt-5-mini',
        assessorAvailable: true,
        isPanelRun: true, coAssessorModelConfigurationId: 4,
        coAssessorModelDisplayNameUsed: 'Claude Opus 4', coAssessorModelProviderUsed: 'Anthropic', coAssessorModelIdUsed: 'claude-opus-4',
        secondOpinionAssessorModelConfigurationId: 3,
        secondOpinionAssessorModelDisplayNameUsed: 'Gemini 3.7 Pro', secondOpinionAssessorModelProviderUsed: 'Google',
        secondOpinionModeUsed: 3, secondOpinionBlindUsed: true,
        secondOpinionGradedAnswerCount: 18, secondOpinionMeanAbsDelta: 3.0,
        panelGradedAnswerCount: 18, panelMeanAbsDelta: 6.2, panelMeanSignedDelta: -4, panelIntraclassCorrelation: 0.712,
        panelDisagreementCount: 2, panelCriticalErrorSplitCount: 1,
        assessorOnlyQualityIndex: 82, coAssessorOnlyQualityIndex: 78,
        status: 'Completed', startedAtUtc: '2026-09-26T06:52:00Z', completedAtUtc: '2026-09-26T07:28:00Z',
        qualityIndex: 80, speedIndex: 67, finalScore: 81, coAssessorFinalScore: 77,
        totalAnswerDurationMs: 900000, totalDurationMs: 900000,
        scoringMethodVersion: 12, harnessVersion: '40',
        transportDefectAnswerCount: 0, advisoryFlagAnswerCount: 0, scrubbedArtifactAnswerCount: 0,
        difficultyFallbackUsed: false, speedMeasurementDegraded: false, maxParallelQuestionsUsed: 1,
        answeredQuestionCount: 18, unansweredQuestionCount: 0, totalQuestionCount: 18,
        assessmentText: 'Member A synthesis.', assessmentParseFailed: false,
        coAssessorSynthesisText: 'Member B synthesis.', coAssessorSynthesisParseFailed: false,
        synthesisFindings: [], coAssessorSynthesisFindings: [], synthesisConvergence: [],
        estimatedCost: 4, estimatedCandidateCost: 1, estimatedAssessorCost: 1, estimatedCoAssessorCost: 1,
        estimatedSynthesisCost: 1, estimatedCoSynthesisCost: 0,
        totalInputTokens: 0, totalOutputTokens: 0, totalCacheReadTokens: 0, totalCacheCreationTokens: 0,
        errorMessage: null,
        answers: [panelAnswer()],
        ...overrides
      };
    }

    function scoreCardText(label: string): string {
      const cards: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.score-card'));
      const card = cards.find(c => (c.querySelector('.score-label')?.textContent || '').trim() === label);
      return (card?.textContent || '').replace(/\s+/g, ' ').trim();
    }

    /** Opens the first answer card the way the operator does, by its header. */
    function expandFirstAnswer(): HTMLElement {
      (fixture.nativeElement.querySelector('.question-card-header') as HTMLElement).click();
      fixture.detectChanges();
      return fixture.nativeElement.querySelector('.question-detail-card') as HTMLElement;
    }

    it('should list both panel assessors with their badges in the header', () => {
      component.selectedRunDetail = buildPanelRun();
      fixture.detectChanges();

      const assessors = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog app-run-facts [data-fact="assessor"]') as HTMLElement;
      expect(assessors.querySelector('dt')!.textContent!.trim()).toBe('Assessors');
      const models = Array.from(assessors.querySelectorAll('.rr-fact-model'));
      const text = (element: Element | null) => (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
      expect(models.map(model => text(model.querySelector('.rr-fact-model-name')))).toEqual(['GPT-5 Mini', 'Claude Opus 4']);
      expect(models.map(model => text(model.querySelector('.model-option-tag')))).toEqual(['Member A', 'Member B']);
      expect(models.map(model => text(model.querySelector('app-provider-badge')))).toEqual(['OpenAI', 'Anthropic']);
    });

    it('should show the Panel tile and relabel the agreement tile for the reference reader', () => {
      component.selectedRunDetail = buildPanelRun();
      fixture.detectChanges();

      const panel = scoreCardText('Panel');
      expect(panel).toContain('A 82 · B 78');
      expect(panel).toContain('ICC 0.71');
      expect(panel).toContain('mean B − A −4.0');
      expect(panel).toContain('2 disagreement(s) over 18 answers both scored');

      expect(scoreCardText('Reference Reader Agreement')).toContain('3.0 pts');
      expect(scoreCardText('Assessor Agreement')).toBe('');
    });

    it('should render the co-assessment box, the panel chip and the reference reader label on an answer', () => {
      component.selectedRunDetail = buildPanelRun();
      fixture.detectChanges();
      const card = expandFirstAnswer();

      expect(card.querySelector('.panel-score-chip')?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Panel: 78');
      expect(card.querySelector('.question-card-header .panel-disagree-badge')?.textContent?.trim()).toBe('MEMBERS DISAGREE');

      const box = card.querySelector('.co-assessment-box') as HTMLElement;
      expect(box).toBeTruthy();
      const text = box.textContent!.replace(/\s+/g, ' ');
      expect(text).toContain('Co-assessor (Claude Opus 4): 74 / 100, critical error no');
      expect(text).toContain('Panel score: 78 / 100');
      expect(text).toContain('members disagree');
      expect(text).toContain('Level 5/6');
      expect(text).toContain('Omits the prayer timeout.');
      expect(text).toContain('gnomes gain infravision');
      // Model output is text, never markup.
      expect(box.querySelector('b')).toBeNull();
      expect(text).toContain('Misses <b>one</b> step.');

      const reference = card.querySelector('.second-opinion-box strong') as HTMLElement;
      expect(reference.textContent).toContain('Reference reader (Gemini 3.7 Pro)');
    });

    it('should keep the single-assessor labels on a single-assessor run', () => {
      component.selectedRunDetail = buildPanelRun({
        isPanelRun: false, coAssessorModelConfigurationId: null, coAssessorModelDisplayNameUsed: null,
        coAssessorSynthesisText: null,
        answers: [panelAnswer({ panelQualityScore: null, panelDisagreed: null, coAssessmentStatus: null, coAssessmentJson: null, coAssessmentQualityScore: null })]
      });
      fixture.detectChanges();
      const card = expandFirstAnswer();

      expect(scoreCardText('Panel')).toBe('');
      expect(scoreCardText('Assessor Agreement')).toContain('3.0 pts');
      expect(card.querySelector('.co-assessment-box')).toBeNull();
      expect(card.querySelector('.panel-score-chip')).toBeNull();
      expect((card.querySelector('.second-opinion-box strong') as HTMLElement).textContent).toContain('Second reader (');
      expect(fixture.nativeElement.querySelectorAll('app-benchmark-synthesis-panel [role="tab"]').length).toBe(0);
    });

    it('should omit the All trigger on a reference reading', () => {
      component.selectedRunDetail = buildPanelRun({ answers: [panelAnswer({ secondOpinionTrigger: 'All' })] });
      fixture.detectChanges();
      const card = expandFirstAnswer();
      expect(card.querySelector('.second-opinion-box')).toBeTruthy();
      expect(card.querySelector('.second-opinion-trigger')).toBeNull();
    });

    it('should show a manual trial trigger on a panel run', () => {
      component.selectedRunDetail = buildPanelRun({ answers: [panelAnswer({ secondOpinionTrigger: 'Manual' })] });
      fixture.detectChanges();
      expect(expandFirstAnswer().querySelector('.second-opinion-trigger')?.textContent).toContain('manual trial');
    });

    it('should label the coverage badge by run type', () => {
      const panel = buildPanelRun();
      expect(component.runSecondOpinionModeLabel(panel)).toBe('Every answer, blind (reference reading)');
      expect(component.runSecondOpinionModeHint(panel)).toContain('never scores');

      const single = buildPanelRun({ isPanelRun: false, secondOpinionModeUsed: 1 });
      expect(component.runSecondOpinionModeLabel(single)).toBe('Only flagged answers');
      expect(component.runSecondOpinionModeHint(single)).toContain('raised a flag');
    });

    it('should show both syntheses as tabs, from one cached list per run object', () => {
      const run = buildPanelRun();
      component.selectedRunDetail = run;
      fixture.detectChanges();

      const tabs = fixture.nativeElement.querySelectorAll('app-benchmark-synthesis-panel [role="tab"]');
      expect(tabs.length).toBe(3);
      expect(component.synthesisViewsOf(run)).toBe(component.synthesisViewsOf(run));
      expect(component.synthesisViewsOf(run).map(s => s.familyRelation)).toEqual(['cross-family', 'same-family']);
    });

    it('should list the co-assessor cost lines and apportion the candidate share across them', () => {
      const run = buildPanelRun();
      component.selectedRunDetail = run;
      fixture.detectChanges();

      const names = Array.from(fixture.nativeElement.querySelectorAll('.benchmark-run-detail-dialog .gh-cost-role:not(.gh-cost-role--subtotal) .gh-cost-role__name'))
        .map((el: any) => el.textContent.trim());
      expect(names).toContain('Co-assessor');
      expect(names).toContain('Co-assessor synthesis');
      // Candidate, assessor, co-assessor and synthesis at 1 each and co-synthesis at 0: a quarter.
      expect(component.candidateCostShareLabel(run)).toBe('25 % of estimated total');
    });

    describe('re-assessment', () => {
      beforeEach(() => {
        component.selectedRunDetail = buildPanelRun();
        fixture.detectChanges();
      });

      function retryDialog(): HTMLElement {
        return fixture.nativeElement.querySelector('.retry-dialog') as HTMLElement;
      }

      it('should offer the member choice without an assessor picker, and send the chosen member', fakeAsync(() => {
        benchmarkServiceMock.reassessPanelAnswer.and.returnValue(of({ runId: 77 }));
        const answer = component.selectedRunDetail!.answers[0];

        component.openRetryDialog('assessment', 77, answer);
        fixture.detectChanges();

        expect(retryDialog().querySelector('.retry-assessor-model-selector')).toBeNull();
        const radios = Array.from(retryDialog().querySelectorAll('.retry-panel-member input[type="radio"]')) as HTMLInputElement[];
        expect(radios.length).toBe(3);
        expect(radios[0].checked).toBeTrue();

        radios[2].click();
        fixture.detectChanges();
        expect(component.retryPanelMember).toBe('B');

        const confirm = retryDialog().querySelector('.dialog-footer .btn-gh:not(.btn-gh-cancel)') as HTMLButtonElement;
        expect(confirm.disabled).toBeFalse();
        confirm.click();

        expect(benchmarkServiceMock.reassessPanelAnswer).toHaveBeenCalledWith(77, 501, 'B');
        expect(benchmarkServiceMock.reassessAnswer).not.toHaveBeenCalled();

        component.stopDetailPolling();
        discardPeriodicTasks();
      }));

      it('should send no assessor override for a panel run\'s question, synthesis and failed-assessment re-runs', fakeAsync(() => {
        benchmarkServiceMock.rerunAnswer.and.returnValue(of({ runId: 77 }));
        benchmarkServiceMock.rerunFinalSynthesis.and.returnValue(of({ runId: 77 }));
        benchmarkServiceMock.retryFailedAssessments.and.returnValue(of({ runId: 77 }));
        const answer = component.selectedRunDetail!.answers[0];

        component.openRetryDialog('question', 77, answer);
        fixture.detectChanges();
        expect(retryDialog().querySelector('.retry-assessor-model-selector')).toBeNull();
        expect(retryDialog().querySelector('.retry-panel-member')).toBeNull();
        component.confirmRetry();
        expect(benchmarkServiceMock.rerunAnswer).toHaveBeenCalledWith(77, 501, null);

        component.openRetryDialog('synthesis', 77);
        fixture.detectChanges();
        expect(retryDialog().querySelector('.retry-assessor-model-selector')).toBeNull();
        component.confirmRetry();
        expect(benchmarkServiceMock.rerunFinalSynthesis).toHaveBeenCalledWith(77, null);

        component.openRetryDialog('assessments', 77);
        component.confirmRetry();
        expect(benchmarkServiceMock.retryFailedAssessments).toHaveBeenCalledWith(77, null);

        component.stopDetailPolling();
        discardPeriodicTasks();
      }));

      it('should offer the trial only where the reference reader has not graded, and never ask to replace', fakeAsync(() => {
        benchmarkServiceMock.trialReassessAnswer.and.returnValue(of({ runId: 77 }));
        const graded = component.selectedRunDetail!.answers[0];
        const empty = { ...graded, secondOpinionQualityScore: null, secondOpinionTrigger: null };
        expect(component.canTrialReassess(graded)).toBeFalse();
        expect(component.canTrialReassess(empty)).toBeTrue();

        const card = expandFirstAnswer();
        expect(card.querySelector('.btn-gh-trial')).toBeNull();

        component.openRetryDialog('trial', 77, graded);
        component.retryAssessorConfigId = 1;
        component.confirmRetry();
        expect(benchmarkServiceMock.trialReassessAnswer.calls.mostRecent().args[3]).toBeFalse();

        component.stopDetailPolling();
        discardPeriodicTasks();
      }));
    });

    describe('calibration target', () => {
      it('should offer Compare against on a panel run and send the chosen target', fakeAsync(() => {
        benchmarkServiceMock.calibrateAssessor.and.returnValue(of({ id: 2 } as any));
        component.selectedRunDetail = buildPanelRun();
        component.calibrationAssessorConfigId = 1;
        fixture.detectChanges();
        tick();
        fixture.detectChanges();

        const trigger = fixture.nativeElement.querySelector('.calibration-target-selector .selector-trigger') as HTMLButtonElement;
        expect(trigger).toBeTruthy();
        trigger.click();
        fixture.detectChanges();

        const options = Array.from(fixture.nativeElement.querySelectorAll('.calibration-target-selector [role="option"]')) as HTMLElement[];
        expect(options.map(o => o.querySelector('.model-option-tag')?.textContent?.trim()))
          .toEqual(['Assessor A', 'Co-assessor B', 'Panel']);
        expect(options.map(o => o.querySelector('.model-name')?.textContent?.trim()))
          .toEqual(['GPT-5 Mini', 'Claude Opus 4', 'Mean of GPT-5 Mini and Claude Opus 4']);
        expect(options[0].querySelector('app-provider-badge')?.textContent).toContain('OpenAI');
        expect(options[1].querySelector('app-provider-badge')?.textContent).toContain('Anthropic');
        expect(options[2].querySelector('app-provider-badge')).toBeNull();

        options[2].click();
        fixture.detectChanges();
        expect(component.calibrationTarget).toBe('Panel');
        expect(trigger.querySelector('.model-option-tag')?.textContent?.trim()).toBe('Panel');

        component.runCalibration(77);
        expect(benchmarkServiceMock.calibrateAssessor).toHaveBeenCalledWith(77, 1, 'Panel');
        discardPeriodicTasks();
      }));

      it('should label Compare against like the calibration assessor', () => {
        component.selectedRunDetail = buildPanelRun();
        fixture.detectChanges();

        const trigger = fixture.nativeElement.querySelector('.calibration-target-selector .selector-trigger') as HTMLButtonElement;
        const labelId = trigger.getAttribute('aria-labelledby')!.split(' ')[0];
        expect(labelId).toBe('bmCalibrationTargetLabel');
        expect(fixture.nativeElement.querySelector(`#${labelId}`)?.textContent?.trim()).toBe('Compare against');
      });

      it('should rebuild the Compare against options only when the run changes', () => {
        component.selectedRunDetail = buildPanelRun();
        const first = component.calibrationTargetPickerOptions;
        expect(component.calibrationTargetPickerOptions).toBe(first);

        component.selectedRunDetail = buildPanelRun();
        expect(component.calibrationTargetPickerOptions).not.toBe(first);
      });

      it('should show the Compared against column on a panel run', () => {
        const rows = [
          { id: 1, benchmarkRunId: 77, assessorDisplayNameUsed: 'Claude Opus 5', assessorProviderUsed: 'Anthropic',
            createdAtUtc: '2026-09-27T08:00:00Z', answerCount: 18, skippedAnswerCount: 0, meanAbsDelta: 5.5,
            disagreementCount: 2, inputTokens: 1, outputTokens: 1, durationMs: 1, comparedAgainst: 'CoAssessor' },
          { id: 2, benchmarkRunId: 77, assessorDisplayNameUsed: 'Claude Opus 5', assessorProviderUsed: 'Anthropic',
            createdAtUtc: '2026-09-27T09:00:00Z', answerCount: 18, skippedAnswerCount: 0, meanAbsDelta: 4.5,
            disagreementCount: 1, inputTokens: 1, outputTokens: 1, durationMs: 1, comparedAgainst: null }
        ] as any[];

        component.selectedRunDetail = buildPanelRun();
        component.calibrations = rows;
        fixture.detectChanges();

        expect(fixture.nativeElement.querySelectorAll('.calibration-list > li.calibration-item').length).toBe(2);
        expect(statValues('Compared against')).toEqual(['Co-assessor B', 'Assessor A']);
      });

      it('should show neither the Compared against column nor the target select on a single-assessor run', () => {
        const rows = [
          { id: 2, benchmarkRunId: 77, assessorDisplayNameUsed: 'Claude Opus 5', assessorProviderUsed: 'Anthropic',
            createdAtUtc: '2026-09-27T09:00:00Z', answerCount: 18, skippedAnswerCount: 0, meanAbsDelta: 4.5,
            disagreementCount: 1, inputTokens: 1, outputTokens: 1, durationMs: 1, comparedAgainst: null }
        ] as any[];

        component.selectedRunDetail = buildPanelRun({ isPanelRun: false });
        component.calibrations = rows;
        fixture.detectChanges();
        expect(fixture.nativeElement.querySelectorAll('.calibration-item').length).toBe(1);
        expect(statTerms()).not.toContain('Compared against');
        expect(fixture.nativeElement.querySelector('.calibration-target-selector')).toBeNull();
      });

      it('should wrap the calibration controls and list the calibrations without a table', () => {
        component.selectedRunDetail = buildPanelRun();
        component.calibrations = [
          { id: 2, benchmarkRunId: 77, assessorDisplayNameUsed: 'Claude Opus 5', assessorProviderUsed: 'Anthropic',
            createdAtUtc: '2026-09-27T09:00:00Z', answerCount: 18, skippedAnswerCount: 3, meanAbsDelta: 4.5,
            disagreementCount: 1, inputTokens: 1, outputTokens: 1, durationMs: 1, comparedAgainst: null,
            errorMessage: 'Three answers could not be graded.' }
        ] as any[];
        fixture.detectChanges();

        const controls = fixture.nativeElement.querySelector('.calibration-panel .calibration-controls') as HTMLElement;
        expect(controls).toBeTruthy();
        expect(controls.querySelector('.calibration-model-field app-model-picker')).toBeTruthy();
        expect(controls.querySelector('.calibration-target-field app-model-picker.calibration-target-selector')).toBeTruthy();
        expect(controls.querySelector('button.btn-gh')?.textContent?.trim()).toBe('Calibrate assessor');
        expect(fixture.nativeElement.querySelector('.calibration-table')).toBeNull();
        expect(fixture.nativeElement.querySelector('.calibration-panel table')).toBeNull();

        expect(statTerms()).toEqual(['Compared against', 'Mean abs. delta', 'Disagreements', 'Answers', 'Tokens', 'Duration']);
        expect(statValues('Answers')).toEqual(['18 (3 skipped)']);
        const item = fixture.nativeElement.querySelector('.calibration-item') as HTMLElement;
        expect(item.querySelector('.calibration-item-head')?.textContent).toContain('Claude Opus 5');
        expect(item.querySelector('p.calibration-error')?.textContent?.trim()).toBe('Three answers could not be graded.');
      });

      it('should order the calibration controls model, target, button on a panel run', () => {
        component.selectedRunDetail = buildPanelRun();
        fixture.detectChanges();
        expect(calibrationControlOrder()).toEqual(['model', 'target', 'button:Calibrate assessor']);
      });

      it('should order the calibration controls model, button on a single-assessor run', () => {
        component.selectedRunDetail = buildPanelRun({ isPanelRun: false });
        fixture.detectChanges();
        expect(calibrationControlOrder()).toEqual(['model', 'button:Calibrate assessor']);
      });

      function calibrationControlOrder(): string[] {
        return Array.from((fixture.nativeElement.querySelector('.calibration-panel .calibration-controls') as HTMLElement).children)
          .map(child => child.matches('.calibration-model-field') ? 'model'
            : child.matches('.calibration-target-field') ? 'target'
            : child.matches('button.btn-gh') ? `button:${child.textContent?.trim()}` : child.tagName);
      }

      function statTerms(): string[] {
        return Array.from(fixture.nativeElement.querySelectorAll('.calibration-stats dt'))
          .map((dt: any) => dt.textContent.trim());
      }

      function statValues(term: string): string[] {
        return Array.from(fixture.nativeElement.querySelectorAll('.calibration-stats dt'))
          .filter((dt: any) => dt.textContent.trim() === term)
          .map((dt: any) => dt.nextElementSibling.textContent.replace(/\s+/g, ' ').trim());
      }
    });
  });

  describe('run report dialog', () => {
    function reportAnswer(orderIndex: number, overrides: any = {}): any {
      return {
        id: 300 + orderIndex, benchmarkRunId: 55, orderIndex, questionText: `Question ${orderIndex}`,
        difficulty: 2, assessedDifficulty: 50, answerText: `Answer ${orderIndex}`, status: 'Ok',
        assessmentStatus: 'Scored', durationMs: 1000, modelTimeMs: 1000, toolCallCount: 1,
        scrubbedArtifactCount: 0, answerFlags: 0, answerFlagNames: [], qualityScore: 85,
        ...overrides
      };
    }

    /** Q1 has a critical error and scores 25, Q2 scores 60, Q3 carries a flag and scores 85. */
    function reportRun(overrides: any = {}): any {
      return {
        id: 55, benchmarkSuiteId: 1, suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Test Model', testedModelProviderUsed: 'OpenAI', testedModelIdUsed: 'gpt-test',
        testedModelParallelExecutionModeUsed: 0,
        assessorModelDisplayNameUsed: 'Test Assessor', assessorModelProviderUsed: 'Google', assessorModelIdUsed: 'gemini-test',
        startedByUserName: 'admin', status: 'Completed',
        startedAtUtc: '2026-09-03T06:52:00Z', completedAtUtc: '2026-09-03T07:10:00Z',
        totalAnswerDurationMs: 900000, totalDurationMs: 900000,
        scoringProfileName: 'Standard', scoringProfileId: 1, scoringMethodVersion: 12, harnessVersion: '40',
        transportDefectAnswerCount: 0, advisoryFlagAnswerCount: 0, scrubbedArtifactAnswerCount: 0,
        difficultyFallbackUsed: false, speedMeasurementDegraded: false, maxParallelQuestionsUsed: 1,
        answeredQuestionCount: 3, unansweredQuestionCount: 0, totalQuestionCount: 3,
        assessmentParseFailed: false, totalInputTokens: 0, totalOutputTokens: 0,
        totalCacheReadTokens: 0, totalCacheCreationTokens: 0, errorMessage: null,
        answers: [
          reportAnswer(1, { criticalError: true, qualityScore: 25 }),
          reportAnswer(2, { qualityScore: 60 }),
          reportAnswer(3, { answerFlagNames: ['RefutedClaim'] })
        ],
        ...overrides
      };
    }

    function reportDialog(): HTMLDialogElement {
      return component.runDetailDialog.nativeElement;
    }

    function runActions(): HTMLElement {
      return fixture.nativeElement.querySelector('.benchmark-run-detail-dialog [role="group"][aria-label="Run actions"]') as HTMLElement;
    }

    /**
     * Resolves on the next event of a type, by which time the listeners Angular added first have run,
     * or after a second, so a missing event fails the expectations that follow rather than hanging.
     */
    function nextEvent(target: EventTarget, type: string): Promise<Event | null> {
      return new Promise(resolve => {
        const timer = setTimeout(() => resolve(null), 1000);
        target.addEventListener(type, event => {
          clearTimeout(timer);
          resolve(event);
        }, { once: true });
      });
    }

    function macrotask(): Promise<void> {
      return new Promise(resolve => setTimeout(resolve));
    }

    /** Opens the report the way Run History does, so the dialog is really modal. */
    function openReport(run: any): void {
      benchmarkServiceMock.getRun.and.returnValue(of(run));
      component.viewRunDetail(run.id);
      fixture.detectChanges();
    }

    afterEach(async () => {
      const dialog = reportDialog();
      if (dialog.open) {
        const closed = nextEvent(dialog, 'close');
        dialog.close();
        await closed;
      }
      component.stopDetailPolling();
    });

    it('should hold the header actions in a named group, not a toolbar, and have no footer', () => {
      component.selectedRunDetail = reportRun({ hasBoardRecord: true });
      fixture.detectChanges();

      const dialog = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog') as HTMLDialogElement;
      expect(dialog.classList.contains('gh-dialog-fullscreen')).toBeTrue();
      expect(dialog.getAttribute('aria-labelledby')).toBe('runDetailTitle');
      expect(dialog.querySelector('#runDetailTitle')?.textContent?.trim()).toBe('Run #55: Default Suite');

      const group = runActions();
      expect(group).toBeTruthy();
      expect(dialog.querySelector('[role="toolbar"]')).toBeNull();

      const buttons = Array.from(group.querySelectorAll(':scope > button')) as HTMLButtonElement[];
      const names = buttons.map(b => b.getAttribute('aria-label') || (b.textContent || '').replace(/\s+/g, ' ').trim());
      expect(names).toEqual(['Downloads', 'Re-run', 'View game snapshot of run 55', 'Copy diagnostics of run 55']);
      for (const button of buttons) {
        expect(button.getAttribute('type')).toBe('button');
        expect(button.hasAttribute('title')).withContext(names[buttons.indexOf(button)]).toBeFalse();
      }
      expect(buttons[0].classList.contains('btn-ghost')).toBeTrue();
      expect(buttons[1].classList.contains('btn-ghost')).toBeTrue();
      expect(buttons[1].getAttribute('popovertarget')).toBe('rr-rerun-popover');
      expect(buttons[1].getAttribute('aria-expanded')).toBe('false');
      expect(buttons[2].classList.contains('action-btn')).toBeTrue();
      expect(buttons[2].getAttribute('interestfor')).toBe('rr-snapshot-tip');
      expect(buttons[3].classList.contains('action-btn')).toBeTrue();
      expect(buttons[3].getAttribute('interestfor')).toBe('rr-copy-diagnostics-tip');
      expect(getComputedStyle(group).display).toBe('grid');

      // Close is a dialog control, not a run action: it sits beside the group, not in it.
      const close = dialog.querySelector('.rr-header-controls > .rr-close') as HTMLButtonElement;
      expect(close).toBeTruthy();
      expect(close.classList.contains('btn-icon-action')).toBeTrue();
      expect(close.getAttribute('aria-label')).toBe('Close run details');
      expect(close.getAttribute('type')).toBe('button');
      expect(close.hasAttribute('title')).toBeFalse();
      expect(group.contains(close)).toBeFalse();
      expect(group.parentElement?.classList.contains('rr-header-controls')).toBeTrue();

      const popover = dialog.querySelector('#rr-rerun-popover') as HTMLElement;
      expect(popover.getAttribute('popover')).toBe('auto');
      expect(popover.getAttribute('role')).toBe('group');
      expect(popover.getAttribute('aria-label')).toBe('Re-run and repair');
      expect(popover.querySelector('[role="menu"], [role="menuitem"]')).toBeNull();

      // The report's own; the dialogs nested in its AI Reports tab keep their footers.
      const own = (selector: string) => Array.from(dialog.querySelectorAll(selector)).filter(el => el.closest('dialog') === dialog);
      expect(own('.modal-actions-bar')).toEqual([]);
      expect(own('.dialog-footer')).toEqual([]);
    });

    const HEADER_PROMPT_OPTIONS = JSON.stringify({ verboseMode: false, enableToolUse: true, hasGameSnapshot: true });
    const FULL_BOARD = [
      { role: 'assessor', delivered: 3, total: 3, missingQuestions: [] },
      { role: 'claim verifier', delivered: 3, total: 3, missingQuestions: [] }
    ];
    const BOARD_WITH_GAP = [
      { role: 'assessor', delivered: 3, total: 3, missingQuestions: [] },
      { role: 'second reader', delivered: 2, total: 3, missingQuestions: [2] }
    ];

    function runDetails(): HTMLDetailsElement {
      return reportDialog().querySelector('details#rr-run-details') as HTMLDetailsElement;
    }

    function factKeys(selector: string): (string | null)[] {
      return Array.from(reportDialog().querySelectorAll(`${selector} [data-fact]`)).map(fact => fact.getAttribute('data-fact'));
    }

    it('should show Model and Assessors always, and the other facts inside a closed Run details with its read-out', () => {
      openReport(reportRun({ candidatePromptOptionsJson: HEADER_PROMPT_OPTIONS, boardDelivery: FULL_BOARD }));

      expect(factKeys('.rr-identity app-run-facts.rr-run-facts-primary')).toEqual(['model', 'assessor']);
      const details = runDetails();
      expect(details.classList).toContain('gh-disclosure');
      expect(details.open).toBeFalse();
      expect(factKeys('#rr-run-details app-run-facts')).toEqual(['prompt', 'profile', 'started', 'board']);
      // Both lists together keep the header's order, and the board note's tip is rendered once.
      expect(factKeys('.rr-identity')).toEqual(['model', 'assessor', 'prompt', 'profile', 'started', 'board']);
      expect(reportDialog().querySelectorAll('#rr-board-note-tip').length).toBe(1);

      const summary = details.querySelector(':scope > summary') as HTMLElement;
      expect(summary.querySelector('.rr-run-details-title')?.textContent?.trim()).toBe('Run details');
      expect(summary.querySelector('button, a, input, select, textarea')).toBeNull();
      const readout = summary.querySelector('.rr-run-details-readout') as HTMLElement;
      expect(readout.getAttribute('aria-hidden')).toBe('true');
      expect(readout.textContent?.trim()).toBe(component.runDetailsReadout);
      expect(component.runDetailsReadout).toMatch(/^Gameplay Help · Standard · Started \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} UTC · Board 3\/3$/);
      expect(getComputedStyle(readout).display).not.toBe('none');
      expect(summary.querySelector('.rr-run-details-gap')).toBeNull();
    });

    it('should remember Run details open, and restore it on the next report and the next visit', async () => {
      openReport(reportRun());
      const toggled = nextEvent(runDetails(), 'toggle');
      (runDetails().querySelector(':scope > summary') as HTMLElement).click();
      await toggled;
      fixture.detectChanges();

      expect(runDetails().open).toBeTrue();
      expect(component.runHeaderDetailsOpen).toBeTrue();
      expect(JSON.parse(localStorage.getItem(RUN_REPORT_HEADER_STORAGE_KEY)!)).toEqual({ version: 1, detailsOpen: true });
      expect(getComputedStyle(runDetails().querySelector('.rr-run-details-readout') as HTMLElement).display).toBe('none');

      const closed = nextEvent(reportDialog(), 'close');
      component.closeRunDetail();
      await closed;
      await macrotask();
      openReport(reportRun({ id: 56 }));
      expect(runDetails().open).toBeTrue();

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      expect(restored.componentInstance.runHeaderDetailsOpen).toBeTrue();
      restored.destroy();
    });

    it('should start Run details closed when the stored state is unreadable', () => {
      localStorage.setItem(RUN_REPORT_HEADER_STORAGE_KEY, '{not json');
      const unreadable = TestBed.createComponent(AdminBenchmarkComponent);
      expect(unreadable.componentInstance.runHeaderDetailsOpen).toBeFalse();
      unreadable.destroy();

      localStorage.setItem(RUN_REPORT_HEADER_STORAGE_KEY, JSON.stringify({ version: 2, detailsOpen: true }));
      const unknown = TestBed.createComponent(AdminBenchmarkComponent);
      expect(unknown.componentInstance.runHeaderDetailsOpen).toBeFalse();
      unknown.destroy();
    });

    it('should say Graded without the board in the Run details summary while it is closed', () => {
      openReport(reportRun({ boardDelivery: BOARD_WITH_GAP }));

      const details = runDetails();
      expect(details.open).toBeFalse();
      const tag = details.querySelector(':scope > summary .rr-run-details-gap') as HTMLElement;
      expect(tag).toBeTruthy();
      expect(tag.classList).toContain('gh-tag');
      expect(tag.classList).toContain('gh-tag-changed');
      expect(tag.textContent?.trim()).toBe('Graded without the board');
      expect(tag.closest('[aria-hidden="true"]')).toBeNull();
      expect(getComputedStyle(tag).display).not.toBe('none');
      expect(tag.getBoundingClientRect().height).toBeGreaterThan(0);
      expect(component.runDetailsReadout).toContain('Board incomplete');
    });

    describe('header layout at 1200 px', () => {
      function openWide(run: any): HTMLElement {
        const dialog = reportDialog();
        dialog.style.width = '1200px';
        dialog.style.maxWidth = '1200px';
        openReport(run);
        return dialog.querySelector('.rrf-header') as HTMLElement;
      }

      afterEach(() => {
        reportDialog().style.removeProperty('width');
        reportDialog().style.removeProperty('max-width');
      });

      it('should keep the header within 190 px with Run details closed', async () => {
        const header = openWide(reportRun({
          isPanelRun: true, coAssessorModelDisplayNameUsed: 'Second Assessor', coAssessorModelProviderUsed: 'Anthropic',
          testedModelThinkingLevelUsed: 'high', candidatePromptOptionsJson: HEADER_PROMPT_OPTIONS, boardDelivery: FULL_BOARD,
          hasBoardRecord: true
        }));
        await document.fonts.ready;
        fixture.detectChanges();

        expect(runDetails().open).toBeFalse();
        expect(header.getBoundingClientRect().width).toBeGreaterThan(1000);
        expect(header.getBoundingClientRect().height).toBeLessThanOrEqual(190);
      });

      it('should not stretch a primary fact beyond its tallest child', async () => {
        openWide(reportRun({
          isPanelRun: true, coAssessorModelDisplayNameUsed: 'Second Assessor', coAssessorModelProviderUsed: 'Anthropic',
          testedModelThinkingLevelUsed: 'high'
        }));
        await document.fonts.ready;
        fixture.detectChanges();

        const facts = Array.from(reportDialog().querySelectorAll('.rr-run-facts-primary .rr-fact')) as HTMLElement[];
        expect(facts.length).toBe(2);
        for (const fact of facts) {
          const height = fact.getBoundingClientRect().height;
          const tallest = Math.max(...Array.from(fact.children).map(child => child.getBoundingClientRect().height));
          expect(height).withContext(fact.getAttribute('data-fact')!).toBeGreaterThan(0);
          expect(height).withContext(fact.getAttribute('data-fact')!).toBeLessThanOrEqual(tallest + 2);
        }
      });

      it('should lay the open Run details out as a fact sheet, not one word per line', async () => {
        openWide(reportRun({ candidatePromptOptionsJson: HEADER_PROMPT_OPTIONS, boardDelivery: FULL_BOARD, hasBoardRecord: true }));
        const toggled = nextEvent(runDetails(), 'toggle');
        (runDetails().querySelector(':scope > summary') as HTMLElement).click();
        await toggled;
        await document.fonts.ready;
        fixture.detectChanges();

        const details = runDetails();
        expect(details.open).toBeTrue();
        const primary = reportDialog().querySelector('.rr-run-facts-primary') as HTMLElement;
        expect(Math.abs(details.getBoundingClientRect().width - primary.getBoundingClientRect().width)).toBeLessThanOrEqual(1);
        expect(details.querySelector('app-run-facts')?.classList).toContain('rr-facts-stacked');

        const fact = (key: string) => (details.querySelector(`[data-fact="${key}"]`) as HTMLElement).getBoundingClientRect();
        expect(Math.abs(fact('profile').top - fact('started').top)).toBeLessThanOrEqual(1);
        expect(fact('board').top).toBeGreaterThanOrEqual(fact('profile').top - 1);
        expect(fact('profile').height).toBeLessThan(60);
      });
    });

    it('should open the Re-run popover on its first enabled item, with aria-expanded from its toggle event', async () => {
      openReport(reportRun());
      const trigger = fixture.nativeElement.querySelector('#rr-rerun-trigger') as HTMLButtonElement;
      const popover = fixture.nativeElement.querySelector('#rr-rerun-popover') as HTMLElement;

      const opened = nextEvent(popover, 'toggle');
      trigger.click();
      await opened;
      fixture.detectChanges();

      expect(popover.matches(':popover-open')).toBeTrue();
      expect(trigger.getAttribute('aria-expanded')).toBe('true');
      expect(document.activeElement).toBe(popover.querySelector('[data-action="rescore"]'));
    });

    it('should close only the popover on Escape and return focus to its trigger', async () => {
      openReport(reportRun());
      const trigger = fixture.nativeElement.querySelector('#rr-rerun-trigger') as HTMLButtonElement;
      const popover = fixture.nativeElement.querySelector('#rr-rerun-popover') as HTMLElement;
      const opened = nextEvent(popover, 'toggle');
      trigger.click();
      await opened;
      fixture.detectChanges();

      const dialogKeydowns: Event[] = [];
      reportDialog().addEventListener('keydown', e => dialogKeydowns.push(e));
      const closed = nextEvent(popover, 'toggle');
      const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      (document.activeElement as HTMLElement).dispatchEvent(escape);
      await closed;
      fixture.detectChanges();

      expect(escape.defaultPrevented).toBeTrue();
      expect(dialogKeydowns.length).toBe(0);
      expect(popover.matches(':popover-open')).toBeFalse();
      expect(reportDialog().open).toBeTrue();
      expect(trigger.getAttribute('aria-expanded')).toBe('false');
      expect(document.activeElement).toBe(trigger);
    });

    it('should keep an applicable Re-run action listed with its reason while it cannot run, and refuse it', () => {
      component.selectedRunDetail = reportRun({
        status: 'Failed',
        isAborted: true,
        answers: [reportAnswer(1, { status: 'Failed', errorMessage: 'boom', qualityScore: null })]
      });
      fixture.detectChanges();
      const rescore = spyOn(component, 'rescoreRun');
      const rerunFailed = spyOn(component, 'rerunFailedFromRunDetail');

      const items = Array.from(fixture.nativeElement.querySelectorAll('#rr-rerun-popover .gh-action-popover-item')) as HTMLButtonElement[];
      const item = (key: string) => items.find(i => i.getAttribute('data-action') === key)!;
      for (const key of ['rescore', 'failed-questions']) {
        expect(item(key)).withContext(key).toBeTruthy();
        expect(item(key).getAttribute('aria-disabled')).withContext(key).toBe('true');
        expect(item(key).disabled).withContext(key).toBeFalse();
        expect(item(key).querySelector('.gh-action-popover-item-reason')?.textContent)
          .withContext(key).toContain('The run stopped before finishing its suite.');
      }

      item('rescore').click();
      item('failed-questions').click();
      expect(rescore).not.toHaveBeenCalled();
      expect(rerunFailed).not.toHaveBeenCalled();
    });

    it('should give every Re-run action the busy reason while a retry is running', () => {
      component.selectedRunDetail = reportRun({ status: 'Running' });
      fixture.detectChanges();
      const reasons = Array.from(fixture.nativeElement.querySelectorAll('#rr-rerun-popover .gh-action-popover-item'))
        .map((i: any) => ({ key: i.getAttribute('data-action'), reason: i.querySelector('.gh-action-popover-item-reason')?.textContent?.trim() }));
      expect(reasons).toEqual([
        { key: 'rescore', reason: 'A retry is already running on this run.' },
        { key: 'synthesis', reason: 'A retry is already running on this run.' }
      ]);
    });

    it('should run an available Re-run action', () => {
      component.selectedRunDetail = reportRun();
      fixture.detectChanges();
      const rescore = spyOn(component, 'rescoreRun');
      const available = fixture.nativeElement.querySelector('#rr-rerun-popover [data-action="rescore"]') as HTMLButtonElement;
      expect(available.hasAttribute('aria-disabled')).toBeFalse();
      available.click();
      expect(rescore).toHaveBeenCalledOnceWith(55);
    });

    it('should filter the questions by any pressed filter, without Members disagree on a single-assessor run', () => {
      component.selectedRunDetail = reportRun();
      fixture.detectChanges();

      const toggles = () => Array.from(fixture.nativeElement.querySelectorAll('.questions-detail-section .gh-filter-toggle')) as HTMLButtonElement[];
      const toggle = (key: string) => toggles().find(t => t.getAttribute('data-filter') === key)!;
      const shown = () => Array.from(fixture.nativeElement.querySelectorAll('.question-detail-card .q-number'))
        .map((e: any) => e.textContent.trim());
      const status = () => (fixture.nativeElement.querySelector('.questions-detail-section [role="status"]').textContent || '')
        .replace(/\s+/g, ' ').trim();

      expect(toggles().map(t => t.getAttribute('data-filter'))).toEqual(['critical', 'disputed', 'below70', 'flagged']);
      expect(toggle('critical').textContent?.trim()).toBe('Critical errors (1)');
      expect(toggle('below70').textContent?.trim()).toBe('Below 70 (2)');
      expect(toggle('critical').getAttribute('aria-pressed')).toBe('false');
      expect(shown()).toEqual(['Q1', 'Q2', 'Q3']);
      expect(status()).toBe('');

      toggle('critical').click();
      fixture.detectChanges();
      expect(toggle('critical').getAttribute('aria-pressed')).toBe('true');
      expect(shown()).toEqual(['Q1']);

      toggle('flagged').click();
      fixture.detectChanges();
      expect(shown()).toEqual(['Q1', 'Q3']);
      expect(status()).toContain('Showing 2 of 3 questions');

      toggle('critical').click();
      toggle('flagged').click();
      toggle('disputed').click();
      fixture.detectChanges();
      expect(shown()).toEqual([]);
      const clear = fixture.nativeElement.querySelector('.questions-detail-section .gh-filter-clear') as HTMLButtonElement;
      expect(clear.textContent?.trim()).toBe('Clear filters');
      clear.click();
      fixture.detectChanges();
      expect(shown()).toEqual(['Q1', 'Q2', 'Q3']);
    });

    it('should offer Members disagree on a panel run, and read Below 70 from the panel score', () => {
      const toggles = () => Array.from(fixture.nativeElement.querySelectorAll('.questions-detail-section .gh-filter-toggle')) as HTMLButtonElement[];
      const toggle = (key: string) => toggles().find(t => t.getAttribute('data-filter') === key)!;
      const shown = () => Array.from(fixture.nativeElement.querySelectorAll('.question-detail-card .q-number'))
        .map((e: any) => e.textContent.trim());

      component.selectedRunDetail = reportRun({
        isPanelRun: true,
        answers: [
          reportAnswer(1, { panelDisagreed: true, panelQualityScore: 65, qualityScore: 80 }),
          reportAnswer(2, { panelQualityScore: 90, qualityScore: 40 })
        ]
      });
      fixture.detectChanges();
      expect(toggles().map(t => t.getAttribute('data-filter'))).toEqual(['critical', 'disputed', 'disagree', 'below70', 'flagged']);
      toggle('below70').click();
      fixture.detectChanges();
      expect(shown()).toEqual(['Q1']);
      toggle('below70').click();
      toggle('disagree').click();
      fixture.detectChanges();
      expect(shown()).toEqual(['Q1']);
    });

    it('should expand and collapse every shown question from real header buttons', () => {
      component.selectedRunDetail = reportRun();
      fixture.detectChanges();

      const headers = () => Array.from(fixture.nativeElement.querySelectorAll('.question-card-header')) as HTMLButtonElement[];
      expect(headers().length).toBe(3);
      for (const header of headers()) {
        expect(header.tagName).toBe('BUTTON');
        expect(header.getAttribute('type')).toBe('button');
        expect(header.getAttribute('aria-expanded')).toBe('false');
        expect(header.querySelector('button, a, input, select, textarea')).toBeNull();
      }

      (fixture.nativeElement.querySelector('#rr-expand-all') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(headers().map(h => h.getAttribute('aria-expanded'))).toEqual(['true', 'true', 'true']);
      for (const header of headers()) {
        const body = fixture.nativeElement.querySelector('#' + header.getAttribute('aria-controls'));
        expect(body?.classList.contains('question-card-body')).toBeTrue();
      }

      (fixture.nativeElement.querySelector('#rr-collapse-all') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(headers().map(h => h.getAttribute('aria-expanded'))).toEqual(['false', 'false', 'false']);
      expect(fixture.nativeElement.querySelectorAll('.question-card-body').length).toBe(0);
    });

    it('should make the per-question actions ghost buttons, the trial named by a tooltip rather than a title', () => {
      component.selectedRunDetail = reportRun();
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('.question-card-header') as HTMLButtonElement).click();
      fixture.detectChanges();

      const actions = Array.from(fixture.nativeElement.querySelectorAll('.question-card-body .question-actions > button')) as HTMLButtonElement[];
      expect(actions.map(b => (b.textContent || '').replace(/\s+/g, ' ').trim()))
        .toEqual(['Re-run Question', 'Re-assess Question', 'Try another assessor (does not change the score)']);
      for (const action of actions) {
        expect(action.classList.contains('btn-ghost')).toBeTrue();
        expect(action.classList.contains('btn-gh')).toBeFalse();
      }
      const trial = actions[2];
      expect(trial.classList.contains('btn-gh-trial')).toBeTrue();
      expect(trial.hasAttribute('title')).toBeFalse();
      const tip = fixture.nativeElement.querySelector('#' + trial.getAttribute('interestfor')) as HTMLElement;
      expect(tip.getAttribute('popover')).toBe('hint');
      expect(tip.textContent).toContain('Changes no score, level, flag or index.');
    });

    it('should clean up exactly once when the header Close closes the report, stopping detail polling', async () => {
      openReport(reportRun());
      component.startDetailPolling(55);
      expect(component.detailPollInterval).not.toBeNull();
      const cleanup = spyOn(component, 'onRunDetailClosed').and.callThrough();

      const closed = nextEvent(reportDialog(), 'close');
      (reportDialog().querySelector('button.rr-close[aria-label="Close run details"]') as HTMLButtonElement).click();
      await closed;
      await macrotask();

      expect(cleanup).toHaveBeenCalledTimes(1);
      expect(reportDialog().open).toBeFalse();
      expect(component.detailPollInterval).toBeNull();
      expect(component.selectedRunDetail).toBeNull();
      expect(component.runDetailRequestedId).toBeNull();
    });

    it('should clean up exactly once when Escape closes the report, stopping detail polling', async () => {
      openReport(reportRun());
      component.startDetailPolling(55);
      const cleanup = spyOn(component, 'onRunDetailClosed').and.callThrough();
      const dialog = reportDialog() as HTMLDialogElement & { requestClose?: () => void };

      const closed = nextEvent(dialog, 'close');
      // What Escape sends: a close request, which is a cancel event and then the close.
      if (typeof dialog.requestClose === 'function') {
        dialog.requestClose();
      } else {
        dialog.dispatchEvent(new Event('cancel', { cancelable: true }));
        dialog.close();
      }
      await closed;
      await macrotask();

      expect(cleanup).toHaveBeenCalledTimes(1);
      expect(component.detailPollInterval).toBeNull();
      expect(component.selectedRunDetail).toBeNull();
    });

    it('should clean up exactly once when code closes the report, whether or not it is open', async () => {
      openReport(reportRun());
      const cleanup = spyOn(component, 'onRunDetailClosed').and.callThrough();

      const closed = nextEvent(reportDialog(), 'close');
      component.closeRunDetail();
      expect(cleanup).not.toHaveBeenCalled();
      await closed;
      await macrotask();
      expect(cleanup).toHaveBeenCalledTimes(1);

      cleanup.calls.reset();
      component.selectedRunDetail = reportRun();
      component.closeRunDetail();
      expect(cleanup).toHaveBeenCalledTimes(1);
      expect(component.selectedRunDetail).toBeNull();
    });

    it('should keep a run reopened before the previous close event arrived', async () => {
      openReport(reportRun());
      const closed = nextEvent(reportDialog(), 'close');
      component.closeRunDetail();
      openReport(reportRun({ id: 56 }));
      await closed;
      await macrotask();

      expect(reportDialog().open).toBeTrue();
      expect(component.selectedRunDetail?.id).toBe(56);
    });

    it('should show the header with Close and a skeleton while the run loads', () => {
      const pending = new Subject<any>();
      benchmarkServiceMock.getRun.and.returnValue(pending.asObservable());
      component.viewRunDetail(77);
      fixture.detectChanges();

      const dialog = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog') as HTMLElement;
      expect(dialog.querySelector('#runDetailTitle')?.textContent?.trim()).toBe('Run #77');
      expect(dialog.querySelector('.rr-header-controls > button.rr-close[aria-label="Close run details"]')).toBeTruthy();
      expect(dialog.querySelector('#rr-downloads-trigger')).toBeNull();
      expect(dialog.querySelector('[role="group"][aria-label="Run actions"]')).toBeNull();
      expect(dialog.querySelector('#rr-run-details')).toBeNull();
      expect(dialog.querySelectorAll('.rr-skeleton').length).toBeGreaterThan(0);
      expect(dialog.querySelector('.rrf-body')?.getAttribute('aria-busy')).toBe('true');

      pending.next(reportRun({ id: 77 }));
      fixture.detectChanges();
      expect(dialog.querySelector('#runDetailTitle')?.textContent?.trim()).toBe('Run #77: Default Suite');
      expect(dialog.querySelectorAll('.rr-skeleton').length).toBe(0);
      expect(dialog.querySelector('.rrf-body')?.hasAttribute('aria-busy')).toBeFalse();
    });

    it('should show a load failure with Close and Try again', () => {
      spyOn(console, 'error');
      benchmarkServiceMock.getRun.and.returnValue(throwError(() => ({ status: 500, error: 'Database unavailable' })));
      component.viewRunDetail(77);
      fixture.detectChanges();

      const dialog = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog') as HTMLElement;
      const alert = dialog.querySelector('[role="alert"]') as HTMLElement;
      expect(alert.textContent).toContain('Run #77 could not be loaded: Database unavailable');
      expect(dialog.querySelector('button[aria-label="Close run details"]')).toBeTruthy();
      expect(dialog.querySelector('.rr-skeleton')).toBeNull();

      benchmarkServiceMock.getRun.calls.reset();
      benchmarkServiceMock.getRun.and.returnValue(of(reportRun({ id: 77 })));
      (dialog.querySelector('#rr-retry-load') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(benchmarkServiceMock.getRun).toHaveBeenCalledOnceWith(77);
      expect(component.selectedRunDetail?.id).toBe(77);
      expect(component.runDetailLoadError).toBeNull();
      expect(dialog.querySelector('#rr-retry-load')).toBeNull();
    });

    it('should open the Download Center on the viewed run from Downloads', () => {
      component.selectedRunDetail = reportRun();
      fixture.detectChanges();
      const open = spyOn(component.runDownloadCenter!, 'open');

      (fixture.nativeElement.querySelector('#rr-downloads-trigger') as HTMLButtonElement).click();

      expect(open).toHaveBeenCalledTimes(1);
      const context = open.calls.mostRecent().args[0] as any;
      expect(context.kind).toBe('run');
      expect(context.run).toEqual({
        id: 55, suiteName: 'Default Suite', modelLabel: 'Test Model',
        startedAtUtc: '2026-09-03T06:52:00Z', completedAtUtc: '2026-09-03T07:10:00Z'
      });
      const text = context.diagnosticsText() as string;
      expect(text).toContain('Run ID: 55');
      expect(text).toContain('Answered 3 of 3');
    });

    it('the AI Reports notice\'s Open Download Center opens the Download Center and returns focus to that button', () => {
      benchmarkServiceMock.listReportDocuments.and.returnValue(of([{
        id: 71, packId: 'run-55', audience: 1, title: 'Document 71', subjectKey: 'run:55', subjectLabel: 'Test Model',
        subjectRunIds: [55], suiteId: 1, suiteName: 'Default Suite', writerDisplayName: 'Test Model',
        writerProvider: 'Anthropic', writerModelId: 'claude-3-5-sonnet', writerThinkingLevel: null,
        sameProviderAcknowledged: false, status: 'Completed', reportFormatVersion: 2,
        createdAtUtc: '2026-09-28T10:15:00Z', inputTokens: 0, outputTokens: 0, durationMs: 0, costUsd: null,
        runChangedSinceGeneration: false, missingRunIds: [], allowedDisclosures: [1, 2, 3], origin: 2
      }]));
      openReport(reportRun({ assessmentJson: '{}' }));
      component.selectRunReportTab('reports');
      fixture.detectChanges();
      const open = spyOn(component.runDownloadCenter!, 'open');

      const button = fixture.nativeElement.querySelector('#rr-panel-reports .rr-ai-download-notice .rr-ai-open-downloads') as HTMLButtonElement;
      expect(button.textContent?.replace(/\s+/g, ' ').trim()).toBe('Open Download Center');
      button.click();

      expect(open).toHaveBeenCalledTimes(1);
      expect((open.calls.mostRecent().args[0] as any).run.id).toBe(55);

      // The Download Center closed and focus fell to the page: it goes back to the notice's button.
      (document.activeElement as HTMLElement | null)?.blur();
      component.onRunDownloadsClosed();
      expect(document.activeElement).toBe(button);
    });

    it('should copy the viewed run diagnostics from Copy diagnostics and announce it', fakeAsync(() => {
      const run = reportRun();
      component.selectedRunDetail = run;
      fixture.detectChanges();
      const writeText = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
      const expected = component.runDiagnosticsTextFor(run, component.runStageOf(run));
      expect(expected).toContain('Run ID: 55');

      (runActions().querySelector('button[aria-label="Copy diagnostics of run 55"]') as HTMLButtonElement).click();
      tick();
      fixture.detectChanges();

      expect(writeText).toHaveBeenCalledOnceWith(expected);
      const status = runActions().querySelector('.rr-status[role="status"]') as HTMLElement;
      expect(status.textContent?.trim()).toBe('Diagnostics copied to the clipboard.');

      tick(3000);
      fixture.detectChanges();
      expect(status.textContent?.trim()).toBe('');
    }));

    it('should list the run configuration and the tool routing table in their tabs', () => {
      component.selectedRunDetail = reportRun({
        secondOpinionAssessorModelConfigurationId: 3, secondOpinionAssessorModelDisplayNameUsed: 'Reader',
        secondOpinionAssessorModelProviderUsed: 'OpenAI', secondOpinionAssessorModelIdUsed: 'gpt-reader',
        secondOpinionModeUsed: 1, candidateSystemPromptSha256: 'abc123',
        answers: [
          reportAnswer(1, { toolCallSummary: 'source_code_search×3, wiki_search×1', qualityScore: 70, modelTimeMs: 3000 }),
          reportAnswer(2, { toolCallSummary: 'wiki_view×2', qualityScore: 90, modelTimeMs: 1000 })
        ]
      });
      fixture.detectChanges();

      const configuration = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog #rr-panel-configuration') as HTMLElement;
      const tools = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog #rr-panel-tools') as HTMLElement;
      const terms = Array.from(configuration.querySelectorAll('.rr-config dt')).map((dt: any) => dt.textContent.trim());
      const value = (term: string) => (configuration.querySelectorAll('.rr-config dd')[terms.indexOf(term)]?.textContent || '')
        .replace(/\s+/g, ' ').trim();
      expect(value('Assessor')).toContain('Test Assessor (Google / gemini-test)');
      expect(value('Assessor')).toContain('different family from the model under test');
      expect(value('Second reader')).toContain('same family as the model under test');
      expect(value('Claim verifier')).toBe('None');
      expect(value('Harness version')).toBe('40');
      expect(value('Prompt SHA-256')).toBe('abc123');

      const routing = tools.querySelector('.tool-routing-table') as HTMLTableElement;
      expect(routing).toBeTruthy();
      const rows = Array.from(routing.querySelectorAll('tbody tr')).map((tr: any) =>
        Array.from(tr.querySelectorAll('td')).map((td: any) => td.textContent.trim()));
      expect(rows).toEqual([['Source Code', '3', '50 %'], ['Wiki', '3', '50 %']]);
      expect(tools.textContent).toContain('(n = 2)');
    });

    it('should right-align the Tools counts', () => {
      component.selectedRunDetail = reportRun({
        answers: [
          reportAnswer(1, { toolCallSummary: 'source_code_search×3, wiki_search×1' }),
          reportAnswer(2, { toolCallSummary: 'wiki_view×2' })
        ]
      });
      fixture.detectChanges();

      const tools = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog #rr-panel-tools') as HTMLElement;
      const numericCells = (table: string) => Array.from(tools.querySelectorAll(`${table} tbody tr`))
        .map(tr => Array.from(tr.querySelectorAll('td')).map(td => td.classList.contains('rr-num')));

      const usage = numericCells('.tool-usage-table');
      expect(usage.length).toBeGreaterThan(0);
      expect(usage.every(row => row.length === 2 && !row[0] && row[1])).toBeTrue();
      expect(tools.querySelector('.tool-usage-table thead th:nth-child(2)')!.classList).toContain('rr-num');

      const routing = numericCells('.tool-routing-table');
      expect(routing.length).toBeGreaterThan(0);
      expect(routing.every(row => row.length === 3 && !row[0] && row[1] && row[2])).toBeTrue();
      const routingHeads = Array.from(tools.querySelectorAll('.tool-routing-table thead th')).map(th => th.classList.contains('rr-num'));
      expect(routingHeads).toEqual([false, true, true]);
    });

    describe('typography', () => {
      // Expected sizes follow the root size, so the specs hold whatever Karma's root font size is.
      const rootPx = () => parseFloat(getComputedStyle(document.documentElement).fontSize);
      const sizeOf = (el: Element) => parseFloat(getComputedStyle(el).fontSize);

      function panel(key: string): HTMLElement {
        return fixture.nativeElement.querySelector(`.benchmark-run-detail-dialog #rr-panel-${key}`) as HTMLElement;
      }

      function one(root: HTMLElement, selector: string): HTMLElement {
        const el = root.querySelector(selector) as HTMLElement;
        expect(el).withContext(selector).toBeTruthy();
        return el;
      }

      /** Tool calls, a band disagreement, a configuration hash, a written report and a calibration row. */
      function openTypographyReport(): void {
        benchmarkServiceMock.listReportDocuments.and.returnValue(of([{
          id: 71, packId: 'run-55', audience: 1, title: 'Document 71', subjectKey: 'run:55', subjectLabel: 'Test Model',
          subjectRunIds: [55], suiteId: 1, suiteName: 'Default Suite', writerDisplayName: 'Test Model',
          writerProvider: 'Anthropic', writerModelId: 'claude-3-5-sonnet', writerThinkingLevel: null,
          sameProviderAcknowledged: false, status: 'Completed', reportFormatVersion: 2,
          createdAtUtc: '2026-09-28T10:15:00Z', inputTokens: 0, outputTokens: 0, durationMs: 0, costUsd: null,
          runChangedSinceGeneration: false, missingRunIds: [], allowedDisclosures: [1, 2, 3], origin: 2
        }]));
        benchmarkServiceMock.getCalibrations.and.returnValue(of([
          { id: 2, benchmarkRunId: 55, assessorDisplayNameUsed: 'Claude Opus 5', assessorProviderUsed: 'Anthropic',
            createdAtUtc: '2026-09-27T09:00:00Z', answerCount: 3, skippedAnswerCount: 0, meanAbsDelta: 4.5,
            disagreementCount: 1, inputTokens: 1, outputTokens: 1, durationMs: 1, comparedAgainst: null }
        ]));
        openReport(reportRun({
          assessmentJson: '{}', candidateSystemPromptSha256: 'abc123',
          answers: [
            reportAnswer(1, { toolCallSummary: 'source_code_search×3, wiki_search×1', toolCallCount: 4, assessedDifficulty: 90 }),
            reportAnswer(2, { toolCallSummary: 'wiki_view×2', toolCallCount: 2 })
          ]
        }));
      }

      it('should set every tab\'s running text at the body size', () => {
        openTypographyReport();
        const body = rootPx() * 0.875;

        const panels = Array.from(fixture.nativeElement.querySelectorAll('.benchmark-run-detail-dialog [role="tabpanel"].rr-panel')) as HTMLElement[];
        expect(panels.length).toBe(10);
        for (const p of panels) {
          expect(sizeOf(p)).withContext(p.id).toBeCloseTo(body, 2);
        }

        const texts: [string, string][] = [
          ['difficulty', '.section-note'],
          ['difficulty', '.band-shift-list li'],
          ['tools', '.section-note'],
          ['tools', '.tool-usage-table td'],
          ['tools', '.tool-routing-table td'],
          ['configuration', '.rr-config dt'],
          ['configuration', '.rr-config dd'],
          ['calibration', '.calibration-item'],
          ['reports', '.rr-ai-status'],
          ['questions', '.section-note']
        ];
        for (const [key, selector] of texts) {
          expect(sizeOf(one(panel(key), selector))).withContext(`${key} ${selector}`).toBeCloseTo(body, 2);
        }
      });

      it('should set metadata and hints one step smaller', () => {
        openTypographyReport();
        const secondary = rootPx() * 0.8125;

        expect(sizeOf(one(panel('reports'), '.rr-ai-doc-meta'))).toBeCloseTo(secondary, 2);
        expect(sizeOf(one(panel('calibration'), '.form-hint'))).toBeCloseTo(secondary, 2);
      });

      it('should head the Questions tab like the other tabs', () => {
        openTypographyReport();

        expect(one(panel('questions'), '#rrQuestionsTitle').classList).toContain('gh-section-title');
      });

      it('should set inline code in the monospace stack', () => {
        openTypographyReport();

        const family = getComputedStyle(one(panel('configuration'), 'code')).fontFamily;
        expect(family).toContain('Consolas');
        expect(family).not.toBe('monospace');
      });

      it('should size Tool Routing as a sub-heading', () => {
        openTypographyReport();

        const heading = one(panel('tools'), 'h5');
        expect(heading.textContent?.trim()).toBe('Tool Routing');
        expect(sizeOf(heading)).toBeCloseTo(rootPx() * 0.875, 2);
        expect(getComputedStyle(heading).fontWeight).toBe('700');
      });
    });

    describe('tabs', () => {
      const KEYS = ['summary', 'integrity', 'synthesis', 'questions', 'difficulty', 'tools', 'cost', 'configuration', 'reports', 'calibration'];

      function tablist(): HTMLElement {
        return fixture.nativeElement.querySelector('.benchmark-run-detail-dialog [role="tablist"][aria-label="Run report sections"]') as HTMLElement;
      }

      function tabs(): HTMLButtonElement[] {
        return Array.from(tablist().querySelectorAll('[role="tab"]')) as HTMLButtonElement[];
      }

      function tab(key: string): HTMLButtonElement {
        return fixture.nativeElement.querySelector(`#rr-tab-${key}`) as HTMLButtonElement;
      }

      function shownPanels(): HTMLElement[] {
        return (Array.from(fixture.nativeElement.querySelectorAll('.benchmark-run-detail-dialog [role="tabpanel"].rr-panel')) as HTMLElement[])
          .filter(panel => !panel.hidden);
      }

      function press(target: HTMLElement, key: string): KeyboardEvent {
        const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
        target.dispatchEvent(event);
        fixture.detectChanges();
        return event;
      }

      /** A run with no integrity clause: no critical error, flag or second reading. */
      function cleanRun(): any {
        return reportRun({ answers: [reportAnswer(1), reportAnswer(2), reportAnswer(3)] });
      }

      it('should render ten tabs in order under the header, each controlling its own panel', () => {
        openReport(reportRun());

        const list = tablist();
        expect(list.classList).toContain('gh-tabs');
        expect(list.classList).toContain('gh-tabs-secondary');
        expect(list.closest('.rrf-tabs')).not.toBeNull();
        const all = tabs();
        expect(all.map(t => t.id)).toEqual(KEYS.map(key => `rr-tab-${key}`));
        expect(all.map(t => (t.textContent || '').replace(/\s+/g, ' ').trim())).toEqual([
          'Summary', 'Integrity Notice', 'Synthesis', 'Questions (3)', 'Difficulty', 'Tools', 'Cost',
          'Configuration', 'AI Reports', 'Calibration'
        ]);
        expect(list.querySelector('svg')).toBeNull();
        expect(all.filter(t => t.getAttribute('tabindex') === '0').map(t => t.id)).toEqual(['rr-tab-summary']);
        for (const t of all) {
          expect(t.getAttribute('type')).toBe('button');
          const panel = fixture.nativeElement.querySelector('#' + t.getAttribute('aria-controls')) as HTMLElement;
          expect(panel.getAttribute('role')).withContext(t.id).toBe('tabpanel');
          expect(panel.getAttribute('aria-labelledby')).withContext(t.id).toBe(t.id);
          expect(panel.getAttribute('tabindex')).withContext(t.id).toBe('0');
        }
        expect(tab('summary').getAttribute('aria-selected')).toBe('true');
        expect(shownPanels().map(p => p.id)).toEqual(['rr-panel-summary']);
      });

      it('should cap the Difficulty, Tools, Cost, Configuration, AI Reports and Calibration panels and no other', () => {
        openReport(reportRun());

        const panel = (key: string) => fixture.nativeElement.querySelector(`#rr-panel-${key}`) as HTMLElement;
        for (const key of ['difficulty', 'tools', 'cost']) {
          expect(panel(key).classList).withContext(key).toContain('rr-panel-narrow');
          expect(panel(key).classList).withContext(key).not.toContain('rr-panel-medium');
        }
        for (const key of ['configuration', 'reports', 'calibration']) {
          expect(panel(key).classList).withContext(key).toContain('rr-panel-medium');
          expect(panel(key).classList).withContext(key).not.toContain('rr-panel-narrow');
        }
        for (const key of ['summary', 'questions']) {
          expect(panel(key).classList).withContext(key).not.toContain('rr-panel-narrow');
          expect(panel(key).classList).withContext(key).not.toContain('rr-panel-medium');
        }
      });

      it('should show exactly the chosen panel, keep the others rendered, and remember the choice', () => {
        openReport(reportRun());

        tab('cost').click();
        fixture.detectChanges();

        expect(component.runReportTab).toBe('cost');
        expect(tab('cost').getAttribute('aria-selected')).toBe('true');
        expect(tab('cost').getAttribute('tabindex')).toBe('0');
        expect(tab('summary').getAttribute('tabindex')).toBe('-1');
        expect(shownPanels().map(p => p.id)).toEqual(['rr-panel-cost']);
        expect(getComputedStyle(fixture.nativeElement.querySelector('#rr-panel-summary')).display).toBe('none');
        expect(fixture.nativeElement.querySelectorAll('#rr-panel-summary .score-card').length).toBeGreaterThan(0);
        expect(localStorage.getItem(RUN_REPORT_TAB_STORAGE_KEY)).toBe('cost');
      });

      it('should wrap with the arrow keys, jump with Home and End, and move focus with the selection', () => {
        openReport(reportRun());

        let event = press(tab('summary'), 'ArrowLeft');
        expect(event.defaultPrevented).toBeTrue();
        expect(component.runReportTab).toBe('calibration');
        expect(document.activeElement).toBe(tab('calibration'));

        press(tab('calibration'), 'ArrowRight');
        expect(component.runReportTab).toBe('summary');
        expect(document.activeElement).toBe(tab('summary'));

        press(tab('summary'), 'End');
        expect(component.runReportTab).toBe('calibration');
        press(tab('calibration'), 'Home');
        expect(component.runReportTab).toBe('summary');
        press(tab('summary'), 'ArrowRight');
        expect(component.runReportTab).toBe('integrity');
        expect(document.activeElement).toBe(tab('integrity'));

        event = press(tab('integrity'), 'a');
        expect(event.defaultPrevented).toBeFalse();
        expect(component.runReportTab).toBe('integrity');
      });

      it('should reopen on the last tab chosen, but keep the shown tab when a re-score reloads the open report', async () => {
        openReport(reportRun());
        tab('tools').click();
        fixture.detectChanges();

        // A reload of the open dialog keeps the tab even if the stored one differs.
        localStorage.setItem(RUN_REPORT_TAB_STORAGE_KEY, 'cost');
        openReport(reportRun());
        expect(component.runReportTab).toBe('tools');

        const closed = nextEvent(reportDialog(), 'close');
        component.closeRunDetail();
        await closed;
        openReport(reportRun());

        expect(component.runReportTab).toBe('cost');
        expect(shownPanels().map(p => p.id)).toEqual(['rr-panel-cost']);
      });

      it('should fall back to Summary for an unknown stored tab or unreadable storage', async () => {
        localStorage.setItem(RUN_REPORT_TAB_STORAGE_KEY, 'no-such-tab');
        component.runReportTab = 'cost';
        openReport(reportRun());
        expect(component.runReportTab).toBe('summary');

        const closed = nextEvent(reportDialog(), 'close');
        reportDialog().close();
        await closed;
        spyOn(localStorage, 'getItem').and.throwError('denied');
        component.runReportTab = 'cost';
        openReport(reportRun());
        expect(component.runReportTab).toBe('summary');
      });

      it('should mark the Integrity tab with Notice exactly while the Run Integrity Notice shows', () => {
        openReport(reportRun());
        expect(component.hasRunIntegrityNotice).toBeTrue();
        expect(tab('integrity').querySelector('.gh-tag.rr-tab-flag')?.textContent?.trim()).toBe('Notice');
        expect(fixture.nativeElement.querySelector('#rr-panel-integrity')?.textContent).toContain('Run Integrity Notice');

        openReport(cleanRun());
        expect(component.hasRunIntegrityNotice).toBeFalse();
        expect(tab('integrity').querySelector('.rr-tab-flag')).toBeNull();
        const panel = fixture.nativeElement.querySelector('#rr-panel-integrity') as HTMLElement;
        expect(panel.textContent).not.toContain('Run Integrity Notice');
        expect(panel.textContent).toContain('No integrity notices for this run.');
      });

      it('should select Questions when jumping to an answer', () => {
        openReport(reportRun());

        component.jumpToAnswer(3);
        fixture.detectChanges();

        expect(component.runReportTab).toBe('questions');
        expect(shownPanels().map(p => p.id)).toEqual(['rr-panel-questions']);
        expect(component.expandedQuestions.has(3)).toBeTrue();
      });

      it('should keep the run-wide strips above the panels, outside every tab panel', () => {
        component.selectedRunDetail = reportRun();
        component.actionErrorMessage = 'Re-scoring failed.';
        fixture.detectChanges();

        const body = fixture.nativeElement.querySelector('.benchmark-run-detail-dialog .rrf-single') as HTMLElement;
        const alert = Array.from(body.querySelectorAll('[role="alert"]'))
          .find(el => el.textContent?.includes('Re-scoring failed.')) as HTMLElement;
        expect(alert.closest('[role="tabpanel"]')).toBeNull();
        const firstPanel = body.querySelector('[role="tabpanel"]') as HTMLElement;
        expect(alert.compareDocumentPosition(firstPanel) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      });

      it('should show no tab row while the run loads or after it failed to load', () => {
        const pending = new Subject<any>();
        benchmarkServiceMock.getRun.and.returnValue(pending.asObservable());
        component.viewRunDetail(77);
        fixture.detectChanges();
        expect(fixture.nativeElement.querySelector('.benchmark-run-detail-dialog [role="tablist"]')).toBeNull();
        expect(fixture.nativeElement.querySelector('.benchmark-run-detail-dialog [role="tabpanel"]')).toBeNull();

        spyOn(console, 'error');
        pending.error({ status: 500 });
        fixture.detectChanges();
        expect(fixture.nativeElement.querySelector('#rr-retry-load')).not.toBeNull();
        expect(fixture.nativeElement.querySelector('.benchmark-run-detail-dialog [role="tablist"]')).toBeNull();
      });
    });

    describe('key figures', () => {
      const NOW = new Date(2026, 8, 28, 12, 34, 56);

      beforeEach(() => {
        spyOn(keyFiguresImageIo, 'loadImage').and.callFake(() => Promise.reject(new Error('404')));
        spyOn(keyFiguresImageIo, 'now').and.returnValue(NOW);
      });

      function dialog(): HTMLElement {
        return fixture.nativeElement.querySelector('.benchmark-run-detail-dialog') as HTMLElement;
      }

      function status(): string {
        return (runActions().querySelector('.rr-status[role="status"]')?.textContent || '').trim();
      }

      /** Clicks a button whose handler is async, and waits for the handler to finish. */
      async function clickAndSettle(button: HTMLButtonElement, handler: jasmine.Spy): Promise<void> {
        button.click();
        await handler.calls.mostRecent().returnValue;
        fixture.detectChanges();
      }

      it('should show no wordmark above the tab row, and the emblem before the run title', () => {
        component.selectedRunDetail = reportRun();
        fixture.detectChanges();

        expect(fixture.nativeElement.querySelector('.benchmark-container .gnollbench-wordmark')).toBeNull();
        expect(fixture.nativeElement.querySelector('.bm-brand')).toBeNull();
        const container = fixture.nativeElement.querySelector('.benchmark-container') as HTMLElement;
        const firstChild = container.firstElementChild as HTMLElement;
        expect(firstChild.matches('.gh-tabs[role="tablist"]')).toBeTrue();

        const emblem = dialog().querySelector('.rr-identity > img.gnollbench-emblem') as HTMLImageElement;
        expect(emblem).toBeTruthy();
        expect(emblem.getAttribute('alt')).toBe('');
        expect(emblem.getAttribute('src')).toBe('/img/gnollbench/gnollbench-logo-v3-256.webp');
        expect(emblem.getAttribute('width')).toBe('64');
        expect(emblem.getAttribute('height')).toBe('64');
        const titleGroup = emblem.nextElementSibling as HTMLElement;
        expect(titleGroup.classList).toContain('dialog-title-group');
        expect(titleGroup.querySelector('#runDetailTitle')).toBeTruthy();
      });

      it('should head the Summary panel with Key figures, its Copy and Download beside it, then the cards', () => {
        component.selectedRunDetail = reportRun({ qualityIndex: 73, qualityIndexStandardError: 4, estimatedCost: 3.2322 });
        fixture.detectChanges();

        const panel = dialog().querySelector('#rr-panel-summary') as HTMLElement;
        const title = panel.querySelector('.rr-figures-head > h4#rrFiguresTitle') as HTMLElement;
        expect(title.textContent?.trim()).toBe('Key figures');
        expect(title.classList).toContain('gh-section-title');

        const group = panel.querySelector('.rr-figures-head > [role="group"][aria-label="Key figures actions"]') as HTMLElement;
        const names = Array.from(group.querySelectorAll('button')).map(b => b.getAttribute('aria-label'));
        expect(names).toEqual([
          'Copy key figures of run 55 as an image',
          'Download key figures of run 55 as a PNG image',
          'Choose key figures for run 55'
        ]);
        const choose = group.querySelector('#rr-figures-choose-btn') as HTMLButtonElement;
        expect(choose.classList).toContain('btn-ghost');
        expect(choose.textContent?.replace(/\s+/g, ' ').trim()).toBe('Choose figures');
        expect(choose.querySelector('svg')).toBeNull();
        expect(choose.getAttribute('aria-haspopup')).toBe('dialog');
        for (const button of Array.from(group.querySelectorAll('button'))) {
          const tip = group.querySelector('#' + button.getAttribute('interestfor')) as HTMLElement;
          expect(tip.getAttribute('popover')).toBe('hint');
          expect(button.getAttribute('style')).toContain('anchor-name: --' + tip.id);
          expect(tip.getAttribute('style')).toContain('position-anchor: --' + tip.id);
        }
        expect(group.querySelector('#rr-figures-copy-tip')?.textContent?.trim()).toBe('Copy key figures as an image');
        expect(group.querySelector('#rr-figures-download-tip')?.textContent?.trim()).toBe('Download key figures as PNG');
        expect(group.querySelector('#rr-figures-choose-tip')?.textContent?.trim()).toBe('Choose which key figures to show and export');

        const head = panel.querySelector('.rr-figures-head') as HTMLElement;
        expect(getComputedStyle(head).paddingBottom).toBe('8px');
        expect(getComputedStyle(title).paddingBottom).toBe('0px');

        const figures = panel.querySelector('.rr-figures') as HTMLElement;
        expect(figures.getAttribute('role')).toBe('group');
        expect(figures.getAttribute('aria-labelledby')).toBe('rrFiguresTitle');
        expect(figures.querySelectorAll(':scope > .score-card').length).toBeGreaterThan(0);
        expect(figures.hidden).toBeFalse();
        expect(getComputedStyle(figures).display).toBe('grid');
        expect(panel.querySelector('.rr-figures-empty')).toBeNull();
        expect(dialog().querySelector('.rrf-figures-toggle, .rrf-figures, .rrf-figures-bar')).toBeNull();
      });

      it('should give every score card its own card actions, as its last child', () => {
        component.selectedRunDetail = reportRun({ estimatedCandidateCost: 1, pricingIncomplete: true });
        fixture.detectChanges();

        const cards = Array.from(dialog().querySelectorAll('.score-card')) as HTMLElement[];
        expect(cards.length).toBeGreaterThan(5);
        for (const card of cards) {
          const label = (card.querySelector('.score-label')?.textContent || '').trim();
          const actions = card.querySelectorAll(':scope > app-key-figure-card-actions');
          expect(actions.length).withContext(label).toBe(1);
          expect(card.lastElementChild?.tagName.toLowerCase()).withContext(label).toBe('app-key-figure-card-actions');
          const copy = actions[0].querySelector('button') as HTMLButtonElement;
          expect(copy.getAttribute('aria-label')).withContext(label).toBe(`Copy ${label} of run 55 as an image`);
        }
      });

      it('should copy the strip and announce each copy outcome', async () => {
        component.selectedRunDetail = reportRun();
        fixture.detectChanges();
        const copy = spyOn(keyFiguresImageIo, 'copy').and.resolveTo('copied');
        const handler = spyOn(component, 'copyKeyFigures').and.callThrough();
        const button = dialog().querySelector('#rr-figures-copy-btn') as HTMLButtonElement;

        await clickAndSettle(button, handler);
        expect(copy).toHaveBeenCalledTimes(1);
        expect((copy.calls.mostRecent().args[0] as Blob).type).toBe('image/png');
        expect(status()).toBe('Key figures copied as an image.');

        copy.and.resolveTo('unsupported');
        await clickAndSettle(button, handler);
        expect(status()).toBe('This browser cannot copy images here; use Download instead.');

        copy.and.resolveTo('denied');
        await clickAndSettle(button, handler);
        expect(status()).toBe('Could not copy the image.');
        expect(component.keyFiguresExporting).toBeFalse();
      });

      it('should copy one card and name it in the status line', async () => {
        component.selectedRunDetail = reportRun();
        fixture.detectChanges();
        const copy = spyOn(keyFiguresImageIo, 'copy').and.resolveTo('copied');
        const handler = spyOn(component, 'exportKeyFigureCard').and.callThrough();
        const card = dialog().querySelector('.score-card.main-score') as HTMLElement;

        await clickAndSettle(card.querySelector('app-key-figure-card-actions button') as HTMLButtonElement, handler);

        expect(handler.calls.mostRecent().args[0]).toEqual({ action: 'copy', card });
        expect(copy).toHaveBeenCalledTimes(1);
        expect(status()).toBe('Intelligence Index copied as an image.');
      });

      it('should download the strip through the IO holder, whichever tab is shown', async () => {
        component.selectedRunDetail = reportRun();
        fixture.detectChanges();
        component.selectRunReportTab('cost');
        fixture.detectChanges();
        expect((dialog().querySelector('#rr-panel-summary') as HTMLElement).hidden).toBeTrue();
        const save = spyOn(keyFiguresImageIo, 'save');
        const handler = spyOn(component, 'downloadKeyFigures').and.callThrough();

        await clickAndSettle(dialog().querySelector('#rr-figures-download-btn') as HTMLButtonElement, handler);

        expect(save).toHaveBeenCalledTimes(1);
        const [blob, fileName] = save.calls.mostRecent().args;
        expect(blob.type).toBe('image/png');
        expect(fileName).toBe('gnollbench_run55_default-suite_test-model_key-figures_20260928_123456.png');
        expect(status()).toBe('Image downloaded.');
      });

      it('should refuse a second export while one runs, and mark the buttons aria-disabled', async () => {
        component.selectedRunDetail = reportRun();
        component.keyFiguresExporting = true;
        fixture.detectChanges();
        const copy = spyOn(keyFiguresImageIo, 'copy').and.resolveTo('copied');

        const strip = dialog().querySelector('#rr-figures-copy-btn') as HTMLButtonElement;
        expect(strip.getAttribute('aria-disabled')).toBe('true');
        const cardCopy = dialog().querySelector('.score-card app-key-figure-card-actions button') as HTMLButtonElement;
        expect(cardCopy.getAttribute('aria-disabled')).toBe('true');

        await component.copyKeyFigures();
        cardCopy.click();
        expect(copy).not.toHaveBeenCalled();
        component.keyFiguresExporting = false;
      });

      function meanTimeCard(): HTMLElement {
        return dialog().querySelector('.score-card[data-figure="mean-time"]') as HTMLElement;
      }

      function textOf(element: Element | null | undefined): string {
        return (element?.textContent || '').replace(/\s+/g, ' ').trim();
      }

      it('should show Mean Time per Question right after the speed card, with the median in its note', () => {
        component.selectedRunDetail = reportRun();
        fixture.detectChanges();

        const card = meanTimeCard();
        expect(card).toBeTruthy();
        expect(card.previousElementSibling?.getAttribute('data-figure')).toBe('speed');
        expect(textOf(card.querySelector('.score-label'))).toBe('Mean Time per Question');
        expect(textOf(card.querySelector('.score-subvalue'))).toBe('1.0 s');
        expect(textOf(card.querySelector('.score-note'))).toBe('model time, tools excluded · median 1.0 s');
        expect(card.querySelector('app-key-figure-card-actions button')?.getAttribute('aria-label'))
          .toBe('Copy Mean Time per Question of run 55 as an image');
      });

      it('should write the mean in tenths of a second under a minute', () => {
        component.selectedRunDetail = reportRun({
          answers: [1, 2, 3].map(i => reportAnswer(i, { modelTimeMs: [15000, 14700, 21900][i - 1] }))
        });
        fixture.detectChanges();
        expect(component.meanModelTimeMs).toBe(17200);
        expect(textOf(meanTimeCard().querySelector('.score-subvalue'))).toBe('17.2 s');
        expect(textOf(meanTimeCard().querySelector('.score-note'))).toBe('model time, tools excluded · median 15.0 s');
      });

      it('should write the mean in minutes and seconds from one minute', () => {
        component.selectedRunDetail = reportRun({
          answers: [1, 2, 3].map(i => reportAnswer(i, { modelTimeMs: [60000, 90000, 66000][i - 1] }))
        });
        fixture.detectChanges();
        expect(component.meanModelTimeMs).toBe(72000);
        expect(textOf(meanTimeCard().querySelector('.score-subvalue'))).toBe('1m 12s');
        expect(textOf(meanTimeCard().querySelector('.score-note'))).toBe('model time, tools excluded · median 1m 6s');

        expect(component.formatModelTime(59940)).toBe('59.9 s');
        expect(component.formatModelTime(59960)).toBe('1m 0s');
      });

      it('should keep the card with a dash when no question was answered', () => {
        component.selectedRunDetail = reportRun({
          answers: [1, 2].map(i => reportAnswer(i, { status: 'Failed', qualityScore: null }))
        });
        fixture.detectChanges();

        expect(component.meanModelTimeMs).toBeNull();
        const value = meanTimeCard().querySelector('.score-subvalue') as HTMLElement;
        expect(textOf(value)).toBe('—');
        expect(value.classList).toContain('text-muted');
        expect(textOf(meanTimeCard().querySelector('.score-note'))).toBe('no answered question');
      });

      it('should give every card a stable data-figure key, in display order', () => {
        component.selectedRunDetail = reportRun({
          isPanelRun: true, coAssessorModelDisplayNameUsed: 'Second Assessor',
          secondOpinionGradedAnswerCount: 3, estimatedCandidateCost: 1, rawQualityIndex: 50, qualityIndex: 73
        });
        fixture.detectChanges();

        const cards = Array.from(dialog().querySelectorAll<HTMLElement>('.rr-figures > .score-card'));
        const keys = cards.map(card => card.getAttribute('data-figure'));
        expect(cards.filter(card => card.hidden)).toEqual([]);
        expect(keys).toEqual(component.shownKeyFigureKeys);
        expect(keys).toEqual([
          'intelligence', 'raw-quality', 'critical-errors', 'answered', 'speed', 'mean-time', 'panel',
          'agreement', 'holistic', 'answer-duration', 'wall-time', 'model-cost', 'estimated-cost'
        ]);
      });

      it('should name the answered count in the mean-time note when it is below the question count', () => {
        component.selectedRunDetail = reportRun({
          answeredQuestionCount: 2,
          answers: [
            reportAnswer(1, { modelTimeMs: 2000 }),
            reportAnswer(2, { modelTimeMs: 4000 }),
            reportAnswer(3, { status: 'Failed', qualityScore: null })
          ]
        });
        fixture.detectChanges();

        expect(textOf(meanTimeCard().querySelector('.score-note'))).toBe('model time, tools excluded · median 3.0 s · over 2 answered');
      });

      function figureCards(): HTMLElement[] {
        return Array.from(dialog().querySelectorAll<HTMLElement>('.rr-figures > .score-card'));
      }

      function shownFigureKeys(): (string | null)[] {
        return figureCards().filter(card => !card.hidden).map(card => card.getAttribute('data-figure'));
      }

      it('should filter the Summary cards live from the chooser, remember the choice, and export it', async () => {
        openReport(reportRun());
        const choose = dialog().querySelector('#rr-figures-choose-btn') as HTMLButtonElement;
        choose.click();
        fixture.detectChanges();

        const chooser = dialog().querySelector('app-key-figures-chooser dialog') as HTMLDialogElement;
        expect(chooser.open).toBeTrue();
        expect(chooser.matches(':modal')).toBeTrue();
        const allKeys = [
          'intelligence', 'critical-errors', 'answered', 'speed', 'mean-time', 'holistic', 'answer-duration',
          'wall-time', 'estimated-cost'
        ];
        const rows = Array.from(chooser.querySelectorAll('li[data-figure]')).map(li => li.getAttribute('data-figure'));
        expect(rows).toEqual(allKeys);
        expect(textOf(chooser.querySelector('li[data-figure="mean-time"] label'))).toBe('Mean Time per Question — 1.0 s');
        expect(textOf(chooser.querySelector('li[data-figure="critical-errors"] label'))).toBe('Critical Errors — 1');
        expect(textOf(chooser.querySelector('li[data-figure="answered"] label'))).toBe('Answered — 3 / 3');
        expect(textOf(chooser.querySelector('[role="status"]'))).toBe('9 of 9 selected');

        (chooser.querySelector('#kfch-speed') as HTMLInputElement).click();
        (chooser.querySelector('#kfch-estimated-cost') as HTMLInputElement).click();
        fixture.detectChanges();
        expect(textOf(chooser.querySelector('[role="status"]'))).toBe('7 of 9 selected');

        expect(JSON.parse(localStorage.getItem(KEY_FIGURES_STORAGE_KEY)!))
          .toEqual({ version: 1, excluded: ['speed', 'estimated-cost'] });
        expect(component.keyFigureExclusions).toEqual(['speed', 'estimated-cost']);
        for (const key of ['speed', 'estimated-cost']) {
          const card = dialog().querySelector(`.score-card[data-figure="${key}"]`) as HTMLElement;
          expect(card.hidden).withContext(key).toBeTrue();
          expect(getComputedStyle(card).display).withContext(key).toBe('none');
        }
        expect(shownFigureKeys()).toEqual([
          'intelligence', 'critical-errors', 'answered', 'mean-time', 'holistic', 'answer-duration', 'wall-time'
        ]);

        const closed = nextEvent(chooser, 'close');
        (chooser.querySelector('.kfch-done') as HTMLButtonElement).click();
        await closed;
        fixture.detectChanges();

        expect(chooser.open).toBeFalse();
        expect(document.activeElement).toBe(choose);
        expect(reportDialog().open).toBeTrue();
        expect(textOf(choose)).toBe('Choose figures (7 of 9)');
        expect(choose.getAttribute('aria-label')).toBe('Choose key figures for run 55, 7 of 9 selected');
        expect(dialog().querySelector('#rr-figures-copy-btn')?.getAttribute('aria-label'))
          .toBe('Copy key figures of run 55 as an image, 7 of 9 key figures');
        expect(dialog().querySelector('#rr-figures-download-btn')?.getAttribute('aria-label'))
          .toBe('Download key figures of run 55 as a PNG image, 7 of 9 key figures');

        const save = spyOn(keyFiguresImageIo, 'save');
        const handler = spyOn(component, 'downloadKeyFigures').and.callThrough();
        await clickAndSettle(dialog().querySelector('#rr-figures-download-btn') as HTMLButtonElement, handler);
        expect(save).toHaveBeenCalledTimes(1);
        expect(status()).toBe('Image downloaded.');

        choose.click();
        fixture.detectChanges();
        expect(Array.from(chooser.querySelectorAll('li[data-figure]')).map(li => li.getAttribute('data-figure'))).toEqual(allKeys);
        const unchecked = Array.from(chooser.querySelectorAll<HTMLInputElement>('li[data-figure] input[type="checkbox"]'))
          .filter(box => !box.checked).map(box => box.id);
        expect(unchecked).toEqual(['kfch-speed', 'kfch-estimated-cost']);
      });

      it('should keep the live choice when the chooser is closed by its close button, and say so when nothing is selected', async () => {
        openReport(reportRun());
        const choose = dialog().querySelector('#rr-figures-choose-btn') as HTMLButtonElement;
        choose.click();
        fixture.detectChanges();
        const chooser = dialog().querySelector('app-key-figures-chooser dialog') as HTMLDialogElement;
        (chooser.querySelector('.kfch-none') as HTMLButtonElement).click();
        fixture.detectChanges();

        expect(component.keyFigureExclusions).toEqual(component.shownKeyFigureKeys);
        expect(figureCards().every(card => card.hidden)).toBeTrue();
        const figures = dialog().querySelector('.rr-figures') as HTMLElement;
        expect(figures.hidden).toBeTrue();
        expect(getComputedStyle(figures).display).toBe('none');
        expect(textOf(dialog().querySelector('.rr-figures-empty')))
          .toBe('No key figures are selected. Use Choose figures to show them.');
        expect(textOf(choose)).toBe('Choose figures (0 of 9)');

        (chooser.querySelector('#kfch-holistic') as HTMLInputElement).click();
        fixture.detectChanges();
        expect(dialog().querySelector('.rr-figures-empty')).toBeNull();
        expect(figures.hidden).toBeFalse();
        expect(shownFigureKeys()).toEqual(['holistic']);

        const closed = nextEvent(chooser, 'close');
        (chooser.querySelector('.kfch-close') as HTMLButtonElement).click();
        await closed;
        fixture.detectChanges();

        const others = [
          'intelligence', 'critical-errors', 'answered', 'speed', 'mean-time', 'answer-duration', 'wall-time',
          'estimated-cost'
        ];
        expect(document.activeElement).toBe(choose);
        expect(reportDialog().open).toBeTrue();
        expect(component.keyFigureExclusions).toEqual(others);
        expect(JSON.parse(localStorage.getItem(KEY_FIGURES_STORAGE_KEY)!)).toEqual({ version: 1, excluded: others });
      });

      it('should export the remembered selection from the one-click Copy, and nothing when none of it is shown', async () => {
        component.selectedRunDetail = reportRun();
        component.keyFigureExclusions = [...component.shownKeyFigureKeys];
        fixture.detectChanges();
        expect(textOf(dialog().querySelector('.rr-figures-empty')))
          .toBe('No key figures are selected. Use Choose figures to show them.');
        expect((dialog().querySelector('.rr-figures') as HTMLElement).hidden).toBeTrue();
        const copy = spyOn(keyFiguresImageIo, 'copy').and.resolveTo('copied');
        const handler = spyOn(component, 'copyKeyFigures').and.callThrough();

        await clickAndSettle(dialog().querySelector('#rr-figures-copy-btn') as HTMLButtonElement, handler);
        expect(copy).not.toHaveBeenCalled();
        expect(status()).toBe('None of this run\'s key figures is selected; use Choose figures.');

        component.keyFigureExclusions = ['panel', 'holistic'];
        fixture.detectChanges();
        expect(component.keyFiguresSelectionLabel).toBe('8 of 9');
        await clickAndSettle(dialog().querySelector('#rr-figures-copy-btn') as HTMLButtonElement, handler);
        expect(copy).toHaveBeenCalledTimes(1);
        expect(status()).toBe('Key figures copied as an image.');
        component.keyFigureExclusions = [];
      });

      it('should read the remembered selection when constructed', () => {
        localStorage.setItem(KEY_FIGURES_STORAGE_KEY, JSON.stringify({ version: 1, excluded: ['holistic'] }));
        const restored = TestBed.createComponent(AdminBenchmarkComponent);
        expect(restored.componentInstance.keyFigureExclusions).toEqual(['holistic']);
        restored.destroy();
      });

      const PROMPT_OPTIONS = JSON.stringify({ verboseMode: false, enableToolUse: true, hasGameSnapshot: true });
      const BOARD_DELIVERY = [
        { role: 'assessor', delivered: 18, total: 18, missingQuestions: [] },
        { role: 'second reader', delivered: 13, total: 14, missingQuestions: [2] }
      ];

      it('should describe the run in the image context with the header facts, the board left out by default', () => {
        const run = reportRun({
          isPanelRun: true, coAssessorModelDisplayNameUsed: 'Second Assessor', coAssessorModelProviderUsed: 'Anthropic',
          testedModelThinkingLevelUsed: 'high', candidatePromptOptionsJson: PROMPT_OPTIONS, boardDelivery: BOARD_DELIVERY
        });
        expect(component.imageDetailExclusions).toEqual(['board']);
        const context = component.keyFiguresContext(run);
        expect(context.title).toBe('Run #55 · Default Suite');
        expect(context.facts.map(row => row.label)).toEqual(['Model', 'Assessors', 'Prompt', 'Scoring profile', 'Started']);
        expect(context.facts.map(row => row.primary)).toEqual([true, true, false, false, false]);
        expect(context.facts[0].runs.map(run => [run.kind, run.text])).toEqual([
          ['text', 'Test Model'], ['badge', 'High'], ['badge', 'OpenAI']
        ]);
        const started = context.facts[4].runs;
        expect(started[0].text).toMatch(/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} UTC$/);
        expect(started[1]).toEqual({ kind: 'badge', text: 'Completed', tone: 'success' });
        expect([context.runId, context.suiteName, context.modelName]).toEqual([55, 'Default Suite', 'Test Model']);
      });

      it('should leave out of the image the rows the image details exclude, while the header lists them all', () => {
        const run = reportRun({ candidatePromptOptionsJson: PROMPT_OPTIONS, boardDelivery: BOARD_DELIVERY });
        component.selectedRunDetail = run;
        fixture.detectChanges();

        component.imageDetailExclusions = ['prompt'];
        const withoutPrompt = component.keyFiguresContext(run).facts.map(row => row.label);
        expect(withoutPrompt).toEqual(['Model', 'Assessor', 'Scoring profile', 'Started', 'Board']);
        const header = Array.from(dialog().querySelectorAll('app-run-facts [data-fact]')).map(fact => fact.getAttribute('data-fact'));
        expect(header).toEqual(['model', 'assessor', 'prompt', 'profile', 'started', 'board']);

        component.imageDetailExclusions = [];
        const board = component.keyFiguresContext(run).facts.find(row => row.label === 'Board')!;
        expect(board.runs.map(run => run.text)).toEqual([
          'Assessor 18/18 · Second reader 13/14',
          '· Synthesis: yes · Difficulty assessment: digest (no map)',
          'Graded without the board — second reader: Q2'
        ]);
        expect(component.keyFiguresContext(run).facts.map(row => row.label))
          .toEqual(['Model', 'Assessor', 'Prompt', 'Scoring profile', 'Started', 'Board']);
      });

      it('should build the header facts once per run object', () => {
        component.selectedRunDetail = reportRun();
        const first = component.selectedRunFacts;
        expect(component.selectedRunFacts).toBe(first);
        component.selectedRunDetail = reportRun();
        expect(component.selectedRunFacts).not.toBe(first);
      });

      it('should offer the run settings as image details in the chooser, and remember a change at once', () => {
        openReport(reportRun());
        const choose = dialog().querySelector('#rr-figures-choose-btn') as HTMLButtonElement;
        choose.click();
        fixture.detectChanges();

        const chooser = dialog().querySelector('app-key-figures-chooser dialog') as HTMLDialogElement;
        const details = Array.from(chooser.querySelectorAll('li[data-detail]'));
        expect(details.map(li => li.getAttribute('data-detail'))).toEqual(['model', 'assessor', 'profile', 'started']);
        expect(textOf(chooser.querySelector('li[data-detail="assessor"] label'))).toBe('Assessor — Test Assessor');
        expect(textOf(chooser.querySelector('.kfch-detail-count'))).toBe('4 of 4 selected');

        (chooser.querySelector('#kfch-detail-profile') as HTMLInputElement).click();
        fixture.detectChanges();
        expect(component.imageDetailExclusions).toEqual(['board', 'profile']);
        expect(JSON.parse(localStorage.getItem(IMAGE_DETAILS_STORAGE_KEY)!)).toEqual({ version: 1, excluded: ['board', 'profile'] });
        expect(component.keyFigureExclusions).toEqual([]);
        expect(component.keyFiguresContext(component.selectedRunDetail!).facts.map(row => row.label))
          .toEqual(['Model', 'Assessor', 'Started']);
      });

      it('should store the image details the chooser reports', () => {
        component.onImageDetailSelectionChange(['prompt', 'started']);
        expect(component.imageDetailExclusions).toEqual(['prompt', 'started']);
        expect(JSON.parse(localStorage.getItem(IMAGE_DETAILS_STORAGE_KEY)!)).toEqual({ version: 1, excluded: ['prompt', 'started'] });

        const restored = TestBed.createComponent(AdminBenchmarkComponent);
        expect(restored.componentInstance.imageDetailExclusions).toEqual(['prompt', 'started']);
        restored.destroy();
      });
    });
  });
});
