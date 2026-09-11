import { ComponentFixture, TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { of, throwError, Subject } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { MultiRunComponent } from './multi-run/multi-run.component';
import { AdminBenchmarkService, BenchmarkRunAnswerDto } from '../../services/admin-benchmark.service';
import { SystemService } from '../../services/system.service';

describe('AdminBenchmarkComponent', () => {
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: jasmine.SpyObj<AdminBenchmarkService>;
  let systemServiceMock: jasmine.SpyObj<SystemService>;

  /** The key AdminBenchmarkComponent remembers the last run setup under. */
  const RUN_SETTINGS_KEY = 'overseer_admin_benchmark_run_settings';

  /** The key it remembers the Model Comparison selection under. */
  const COMPARISON_SELECTION_KEY = 'overseer_admin_benchmark_comparison_selection';

  function clearStoredState(): void {
    // Both are real browser state, so without this a spec that starts a run or picks a comparison
    // leaks its selections into every spec that constructs the component afterwards.
    try {
      localStorage.removeItem(RUN_SETTINGS_KEY);
      localStorage.removeItem(COMPARISON_SELECTION_KEY);
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
      'importDefaultSuite',
      'getSuiteRunsFootprint',
      'deleteSuiteRuns',
      'reorderQuestions',
      'startDifficultyAssessment',
      'getDifficultyAssessment',
      'getActiveDifficultyAssessment',
      'cancelDifficultyAssessment',
      'reassessAnswer',
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
      'getComparabilityIndex'
    ]);

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

    await TestBed.configureTestingModule({
      imports: [AdminBenchmarkComponent],
      providers: [
        { provide: AdminBenchmarkService, useValue: benchmarkServiceMock },
        { provide: SystemService, useValue: systemServiceMock },
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

  it('should display "Import Default Suite" on import button without hardcoded question count', () => {
    component.activeSubTab = 'suites';
    fixture.detectChanges();

    const importBtn = fixture.nativeElement.querySelectorAll('.suites-toolbar .btn-gh')[1];
    expect(importBtn.textContent.trim()).toBe('Import Default Suite');
    expect(importBtn.textContent).not.toContain('15-Question');
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
    expect(component.difficultyAssessmentScope).toBe('suite');
    expect(benchmarkServiceMock.startDifficultyAssessment).not.toHaveBeenCalled();
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
      assessorModelConfigurationId: 1
    });
    expect(component.difficultyDialogPhase).toBe('progress');
    expect(benchmarkServiceMock.getDifficultyAssessment).toHaveBeenCalledWith('job-123');
    expect(component.difficultyJob).toEqual(mockJob);
    expect(component.difficultyJobIsRunning).toBeTrue();
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
    expect(startBtn.disabled).toBeTrue();
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
    const tabs = () =>
      Array.from(fixture.nativeElement.querySelectorAll('[role="tab"]')) as HTMLButtonElement[];

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
    it('should render narrow run count input inside claim-verifier-row', () => {
      fixture.detectChanges();
      const input = fixture.nativeElement.querySelector('#runCountInput');
      expect(input).toBeTruthy();
      expect(input.classList.contains('run-count-input')).toBeTrue();
      expect(input.closest('.claim-verifier-row')).toBeTruthy();
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
      component.activeRunDetail = buildRun({ answers: [buildAnswer(2)] });
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
      benchmarkServiceMock.getRun.and.returnValue(throwError(() => ({
        status: 500,
        message: 'Internal Server Error'
      })));

      (component as any).pollRunDetail(42);

      expect(component.lastRunPollError).toContain('500');
      expect(component.runDiagnosticsText).toContain('Last poll error:');
      expect(component.runDiagnosticsText).toContain('500');
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
      expect((component as any).pollInterval).not.toBeNull();

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
      expect(text).toContain('1 answer(s) were re-graded by a second assessor');
      expect(fixture.nativeElement.querySelector('.disputed-badge')).toBeTruthy();
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
      expect(selector.textContent).toContain('None — no second opinion');
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
        .toContain('Second opinion threshold must be between 0 and 100.');
      expect(benchmarkServiceMock.createScoringProfile).not.toHaveBeenCalled();
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
      expect(strip.querySelector('.thinking-badge')?.textContent?.trim()).toBe('Max');
      expect(strip.querySelector('.provider-badge')).toBeTruthy();
      expect(strip.querySelector('.tier-badge')).toBeTruthy();

      const subtitle = fixture.nativeElement
        .querySelector('.benchmark-run-progress-dialog .dialog-subtitle') as HTMLElement;
      expect(subtitle.textContent).toContain('Default Suite');
      expect(subtitle.textContent).not.toContain('Model:');
      expect(subtitle.textContent).not.toContain('Evaluator:');
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
        .toContain('Stage 2 of 3 — Follow-up grading passes: second-opinion sweep');

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
      expect(runStatText('Second opinions')).toBe('1');
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
      expect(runStatText('Second opinions')).toBeNull();
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
      expect(component.runRowChipLabel(rows[1])).toBe('Second opinion');
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
  // Layout regression guards: the three model selectors share one row, and the
  // run-model-strip in the progress dialog shares one column edge between rows.
  // ---------------------------------------------------------------------------
  describe('model selector row layout', () => {
    it('should place Model Under Test, Assessor Model, and Second Opinion Assessor in one .form-row.three-cols', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const rows = fixture.nativeElement.querySelectorAll('.form-row.three-cols');
      expect(rows.length).toBe(1);

      const groups = Array.from(rows[0].querySelectorAll(':scope > .form-group')) as HTMLElement[];
      expect(groups.length).toBe(3);
      expect(groups[0].querySelector('label')?.textContent?.trim()).toBe('Model Under Test');
      expect(groups[1].querySelector('label')?.textContent?.trim()).toBe('Assessor Model');
      expect(groups[2].querySelector('label')?.textContent?.trim()).toBe('Second Opinion Assessor (optional)');

      // The explanatory hint travels with the second-opinion selector, not loose in the row.
      // The trigger list moved to the mode dropdown's own hint when that control was added, so
      // this one describes what the second assessor is for rather than when it fires.
      const hint = groups[2].querySelector('.form-hint');
      expect(hint).toBeTruthy();
      expect(hint!.textContent).toContain('Produces a second, independent verdict');
    });

    it('should place Claim Verifier and Candidate Response Style in .form-row.claim-verifier-row in col 1 and col 2', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const row = fixture.nativeElement.querySelector('.form-row.claim-verifier-row');
      expect(row).toBeTruthy();

      const groups = Array.from(row.querySelectorAll(':scope > .form-group')) as HTMLElement[];
      expect(groups.length).toBe(2);
      expect(groups[0].querySelector('label')?.textContent?.trim()).toBe('Claim Verifier (optional)');
      expect(groups[1].querySelector('label')?.textContent?.trim()).toBe('Candidate Response Style');
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
      expect(dts[1].textContent?.trim()).toBe('Evaluator');
      expect(dds[1].textContent).toContain('GPT-5.6 Luna');

      // The alignment itself comes from the grid CSS (max-content / minmax(0, 1fr)),
      // which a unit test cannot assert — only that the markup it depends on is present.
    });

    it('should render second opinion assessor row under Evaluator with selected mode when configured', () => {
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
      expect(dts[1].textContent?.trim()).toBe('Evaluator');
      expect(dts[2].textContent?.trim()).toBe('Second opinion assessor');

      expect(dds[2].textContent).toContain('Claude Opus 5');
      expect(dds[2].querySelector('.thinking-badge')?.textContent?.trim()).toBe('High');
      expect(dds[2].querySelector('.provider-badge')?.textContent?.trim()).toBe('Anthropic');
      const modeBadge = dds[2].querySelector('.second-opinion-mode-badge');
      expect(modeBadge).toBeTruthy();
      expect(modeBadge?.textContent?.trim()).toBe('Only flagged answers');
      expect(modeBadge?.getAttribute('title')).toContain('Critical errors');
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

    it('should format second opinion modes and hints correctly', () => {
      expect(component.formatSecondOpinionMode(0)).toBe('');
      expect(component.formatSecondOpinionMode(1)).toBe('Only flagged answers');
      expect(component.formatSecondOpinionMode(2)).toBe('Flagged answers and statistical outliers');
      expect(component.formatSecondOpinionMode(3)).toBe('Every answer (double grading)');

      expect(component.secondOpinionModeHintOf(1)).toContain('Critical errors');
      expect(component.secondOpinionModeHintOf(3)).toContain('measures grader agreement');
    });

    it('should explain what the Model Under Test and the Assessor Model each do', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const groups = Array.from(
        fixture.nativeElement.querySelectorAll('.form-row.three-cols > .form-group')
      ) as HTMLElement[];

      expect(groups[0].querySelector('.form-hint')?.textContent).toContain('The candidate.');
      expect(groups[1].querySelector('.form-hint')?.textContent)
        .toContain('four BARS dimensions');
    });

    it('should describe the benchmark suite and the scoring profile', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      // Both controls decide what a run's numbers mean, and both were undescribed: the suite had
      // no hint at all, and the profile could only ever show the conditional fit advisory.
      const suiteHint = fixture.nativeElement.querySelector('#suiteHint') as HTMLElement | null;
      expect(suiteHint).toBeTruthy();
      expect(suiteHint!.textContent).toContain('only comparable with other runs of the same');

      const profileHint = fixture.nativeElement.querySelector('#profileHint') as HTMLElement | null;
      expect(profileHint).toBeTruthy();
      expect(profileHint!.textContent).toContain('Turns the four raw dimension grades into the indices');
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
      // The default profile targets 15000 ms, which is well inside the interactive band.
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
      expect(component.secondOpinionModeHint).toContain('Select a second opinion assessor first');
      discardPeriodicTasks();
    }));

    it('should enable and describe the selected mode once an assessor is chosen', fakeAsync(() => {
      component.secondOpinionConfigId = 1;
      component.secondOpinionMode = 3;
      const select = modeSelect();

      expect(select!.disabled).toBeFalse();
      expect(component.secondOpinionModeHint).toContain('measures grader agreement');
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
      expect(scoreCardText('Unweighted Mean')).toContain('weighting +2');
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
      expect(component.agreementCoverageLabel).toBe('4/18');
      expect(component.agreementIsSelective).toBeTrue();
      const text = scoreCardText('Assessor Agreement');
      expect(text).toContain('4.3 pts');
      expect(text).toContain('4/18');
      expect(text).toContain('Flagged only');
    });

    it('should drop the selective caveat when every answer was graded twice', () => {
      component.selectedRunDetail = buildFinishedRun({
        secondOpinionModeUsed: 3,
        secondOpinionGradedAnswerCount: 18,
        secondOpinionMeanAbsDelta: 3.0
      });
      fixture.detectChanges();

      expect(component.agreementCoverageLabel).toBe('18/18');
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

      expect(body).toContain('A second-opinion assessor was selected but no answer met a trigger');
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
        expect(speedCardValueText()).toContain('2,500 ms');
        expect(speedIndexNoteText()).toContain('Speed Index');
        expect(speedIndexNoteText()).toContain('saturated: 2 of 4 at the ceiling');
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

    it('should record the scoring constants the run was actually scored with', () => {
      component.activeRunDetail = buildDiagnosticsRun();
      const text = component.runDiagnosticsText;

      expect(text).toContain('harness version: 7');
      expect(text).toContain('scoring method version: 6');
      expect(text).toContain('Speed: target 15000 ms, decay k 20');
      // The outlier delta is read only by the FlaggedAndOutliers trigger, so under All it governed
      // nothing and printing it read as a threshold this run applied.
      expect(text).toContain('Second opinion: mode All, threshold 50');
      expect(text).not.toContain('outlier delta');
    });

    it('should print the outlier delta only under the trigger that reads it', () => {
      component.activeRunDetail = buildDiagnosticsRun({ secondOpinionModeUsed: 2 });
      expect(component.runDiagnosticsText)
        .toContain('Second opinion: mode FlaggedAndOutliers, threshold 50, outlier delta 25');
    });

    it('should name the mode added after this capture was written', () => {
      component.activeRunDetail = buildDiagnosticsRun({ secondOpinionModeUsed: 4 });
      expect(component.runDiagnosticsText).toContain('Second opinion: mode FlaggedPlusSample');
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
      expect(text).toContain('contested verdicts: 1, unevidenced deductions: 0, refuted claims: 0, contested critical errors: 0, contested accuracy deductions: not recorded, re-assessed: 1');
      expect(text).toContain('unverified claims: 2');
      expect(text).toContain('4.3 mean abs delta');
      expect(text).toContain('over 2 of 2 answered, disagreements: 1');
      // Full coverage, so no conditioning caveat.
      expect(text).not.toContain('coverage selected by trigger');
    });

    it('should print the contested accuracy deduction count when recorded, zero included', () => {
      component.activeRunDetail = buildDiagnosticsRun({ contestedAccuracyDeductionAnswerCount: 2 });
      expect(component.runDiagnosticsText).toContain('contested critical errors: 0, contested accuracy deductions: 2, re-assessed: 1');

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
      expect(text).toContain('1 answer(s) met a trigger but the second-opinion call failed');
      expect(text).toContain('(question(s) 7)');
      expect(text).not.toContain('A second-opinion assessor was selected but no answer met a trigger');
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
      expect(text).toContain('A second-opinion assessor was selected but no answer met a trigger');
      expect(text).not.toContain('second-opinion call failed');
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
      expect(text).toContain('1 answer(s) met a trigger but the second-opinion call failed');
      expect(text).toContain('(question(s) 3)');
      expect(text).toContain('grader agreement is measured over the 3 answer(s) whose second opinion completed');
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
      expect(text).toContain('± 6 (95%)');
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
      expect(component.candidateResponseStyleHint).toContain('Default to 2–5 sentences per response');

      component.candidateVerboseMode = true;
      expect(component.candidateResponseStyleHint).toContain('detailed explanations');
      expect(component.candidateResponseStyleHint).toContain('NOT be comparable');
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
    // U3. Two decimals at or above $1 so the card reads the same as the report; four below it so a
    // sub-cent run still resolves to something other than $0.00.
    it('should format a cost at or above $1 with two decimals and below it with four', () => {
      expect(component.formatCostAmount(2.5312)).toBe('$2.53');
      expect(component.formatCostAmount(1)).toBe('$1.00');
      expect(component.formatCostAmount(0.9912)).toBe('$0.9912');
      expect(component.formatCostAmount(0.0004)).toBe('$0.0004');
    });

    it('should render a missing or non-finite cost as a dash rather than $0', () => {
      expect(component.formatCostAmount(null)).toBe('-');
      expect(component.formatCostAmount(undefined)).toBe('-');
      expect(component.formatCostAmount(Number.NaN)).toBe('-');
    });

    it('should render Estimated Cost card with the incomplete-pricing marker when pricingIncomplete is true', () => {
      component.activeSubTab = 'run';
      component.selectedRunDetail = {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Test',
        status: 2,
        estimatedCost: 1.2345,
        pricingSource: 'Anthropic API',
        pricingIncomplete: true,
        answers: []
      } as any;
      fixture.detectChanges();

      const cards = Array.from(fixture.nativeElement.querySelectorAll('.score-card')) as HTMLElement[];
      const card = cards.find(c => c.querySelector('.score-label')?.textContent?.trim() === 'Estimated Cost');
      expect(card).toBeTruthy();
      
      const content = card!.textContent?.replace(/\s+/g, ' ').trim() || '';
      expect(content).toContain('$1.23');
      expect(content).toContain('Anthropic API');
      
      const marker = card!.querySelector('.degraded-tag');
      expect(marker).toBeTruthy();
      expect(marker?.textContent?.trim()).toBe('*');
    });

    // H5. The summary row carries the same pair the run history Cost cell does: the model under
    // test first, the catalog total beside it.
    it('should render a Model Under Test card with the candidate figure and its share of the total', () => {
      component.activeSubTab = 'run';
      component.selectedRunDetail = {
        id: 1,
        benchmarkSuiteId: 1,
        suiteName: 'Test',
        status: 2,
        estimatedCost: 3.03,
        estimatedCandidateCost: 2.30,
        pricingSource: 'Anthropic API',
        answers: []
      } as any;
      fixture.detectChanges();

      const cards = Array.from(fixture.nativeElement.querySelectorAll('.score-card')) as HTMLElement[];
      const card = cards.find(c => c.querySelector('.score-label')?.textContent?.trim() === 'Model Under Test');
      expect(card).toBeTruthy();

      const content = card!.textContent?.replace(/\s+/g, ' ').trim() || '';
      expect(content).toContain('$2.30');
      expect(content).toContain('76 % of catalog total');
    });

    it('should omit the Model Under Test card when the candidate cost was never recorded', () => {
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
      expect(labels).not.toContain('Model Under Test');
      expect(labels).toContain('Estimated Cost');
    });

    it('should render Cost column in run history table with the incomplete-pricing marker when pricingIncomplete is true', () => {
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

      const headers = Array.from(fixture.nativeElement.querySelectorAll('.gh-table th')) as HTMLElement[];
      const costHeader = headers.find(th => th.textContent?.trim() === 'Cost');
      expect(costHeader).toBeTruthy();

      const row = fixture.nativeElement.querySelector('.gh-table tbody tr');
      expect(row).toBeTruthy();
      
      const cellText = row!.textContent || '';
      expect(cellText).toContain('$0.50');
      
      const marker = row!.querySelector('.degraded-tag');
      expect(marker).toBeTruthy();
      expect(marker?.textContent?.trim()).toBe('*');
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
      expect(stored.runCount).toBe(1);
      component.ngOnDestroy();
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

  describe('Run History table (data-table)', () => {
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

    it('should page and filter the view without touching historyRuns', () => {
      const runs = Array.from({ length: 3 }, (_, i) => buildHistoryRun({ id: i + 1, suiteName: `Suite ${i + 1}` }));
      component.historyRuns = runs;

      component.historyTable.setFilter('suiteName', 'Suite 2');

      expect(component.historyView.map(r => r.id)).toEqual([2]);
      expect(component.historyRuns).toBe(runs);
      expect(component.historyRuns.length).toBe(3);
    });

    it('should stack a labelled row per fingerprint, dashing a hash that was not recorded', () => {
      component.historyRuns = [buildHistoryRun({ id: 1, wikiHeadSha: null })];
      component.activeSubTab = 'history';
      fixture.detectChanges();

      const entries = Array.from(
        fixture.nativeElement.querySelectorAll('.instrument-cell .instrument-fingerprint')
      ) as HTMLElement[];

      // Five rows whatever the run recorded: the label carries the meaning, so the cell stays
      // legible in greyscale, and a missing hash is visible as a dash rather than absent.
      expect(entries.length).toBe(5);
      expect(entries.map(e => e.textContent?.trim())).toEqual([
        'PROMPT sha-a',
        'GUIDES guide-a',
        'KB kb-a',
        'WIKI -',
        'SRC src-a'
      ]);

      const cssClasses = ['fp-prompt', 'fp-guides', 'fp-kb', 'fp-wiki', 'fp-source'];
      cssClasses.forEach((cssClass, i) => expect(entries[i].classList.contains(cssClass)).toBeTrue());

      expect(entries[3].getAttribute('title')).toBe('GnollHack wiki Git HEAD SHA: not recorded');
      expect(entries[4].getAttribute('title')).toBe('GnollHack source Git HEAD SHA: src-a');
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

      expect(component.runAnswerDurationNote(run)).toBe('candidate answering only');
      expect(component.runWallClockNote(run)).toBe('start to finish, grading included');
    });

    it("should name the re-run's own span on the wall time note and flag re-executed answers on the answer duration note", () => {
      const run = buildRun({
        status: 'Canceled',
        rerunStartedAtUtc: '2026-09-09T10:00:00Z',
        rerunCompletedAtUtc: '2026-09-09T10:20:04Z'
      });

      expect(component.runAnswerDurationNote(run)).toBe('candidate answering only · includes re-executed answers');
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
      expect(panel.querySelector('.mc-launcher')).toBeTruthy();
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

      const launcher = fixture.nativeElement.querySelector('.mc-launcher');
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
      expect(actions[0].textContent.trim()).toBe('Open comparison wizard');
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
      fixture.detectChanges();

      const state = fixture.nativeElement.querySelector('.mc-launcher .mc-launcher-state');
      expect(state).toBeTruthy();
      const terms = Array.from(state.querySelectorAll('dt')).map((dt: any) => dt.textContent.trim());
      expect(terms).toEqual(['Suite', 'Pricing basis', 'Charted', 'Computed']);
      expect(state.textContent).toContain('2 of 3 entries');
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
            questionParallelism: '1', pricingSnapshot: '2026-09-01'
          },
          {
            key: 'run:2', sourceKind: 'Run', sourceId: 2, conditionOrdinal: 1,
            conditionLabel: 'Condition A', signature: 'sig-a', selfInconsistent: false,
            selfInconsistentKeys: [], differencesFromLargest: [],
            questionParallelism: '1', pricingSnapshot: '2026-09-01'
          },
          {
            key: 'run:3', sourceKind: 'Run', sourceId: 3, conditionOrdinal: 2,
            conditionLabel: 'Condition B', signature: 'sig-b', selfInconsistent: false,
            selfInconsistentKeys: [], differencesFromLargest: [],
            questionParallelism: '1', pricingSnapshot: '2026-09-01'
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
});
