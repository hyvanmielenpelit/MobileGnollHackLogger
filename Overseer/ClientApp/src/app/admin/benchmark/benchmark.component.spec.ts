import { ComponentFixture, TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { AdminBenchmarkService } from '../../services/admin-benchmark.service';
import { SystemService } from '../../services/system.service';

describe('AdminBenchmarkComponent', () => {
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: jasmine.SpyObj<AdminBenchmarkService>;
  let systemServiceMock: jasmine.SpyObj<SystemService>;

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
      'retryClaimVerification'
    ]);

    benchmarkServiceMock.getActiveDifficultyAssessment.and.returnValue(of(null));
    benchmarkServiceMock.getActiveRun.and.returnValue(of(null));
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
      expect(tabs().length).toBe(3);
    });

    it('should mark exactly one tab selected, matching activeSubTab', () => {
      const selected = tabs().filter(t => t.getAttribute('aria-selected') === 'true');
      expect(selected.length).toBe(1);
      expect(selected[0].id).toBe('bm-tab-' + component.activeSubTab);
    });

    it('should give exactly one tab tabindex="0" and the rest tabindex="-1"', () => {
      const all = tabs();
      expect(all.filter(t => t.getAttribute('tabindex') === '0').length).toBe(1);
      expect(all.filter(t => t.getAttribute('tabindex') === '-1').length).toBe(2);
    });

    it('should wrap forward from the last tab to the first with ArrowRight', () => {
      component.activeSubTab = 'suites';
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 2);
      expect(component.activeSubTab).toBe('run');
    });

    it('should wrap backward from the first tab to the last with ArrowLeft', () => {
      component.activeSubTab = 'run';
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }), 0);
      expect(component.activeSubTab).toBe('suites');
    });

    it('should select the first and last tab with Home and End', () => {
      component.activeSubTab = 'history';
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'End' }), 1);
      expect(component.activeSubTab).toBe('suites');

      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'Home' }), 2);
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
  });

  // ---------------------------------------------------------------------------
  // Button harmonization guards. These assert the shared design-system
  // vocabulary is used and that no control is left without an accessible name.
  // ---------------------------------------------------------------------------
  describe('button harmonization', () => {
    /** Renders each sub-tab in turn so every button in the view is inspected. */
    function forEachSubTab(check: (where: string) => void): void {
      for (const tab of ['run', 'history', 'suites'] as const) {
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

    it('should name only the advisory flags as advisory', () => {
      expect(component.isAdvisoryFlagName('ReasoningBleed')).toBeTrue();
      expect(component.isAdvisoryFlagName('RepeatedFragments')).toBeTrue();
      expect(component.isAdvisoryFlagName('ContestedVerdict')).toBeTrue();
      expect(component.isAdvisoryFlagName('UnevidencedDeduction')).toBeTrue();
      expect(component.isAdvisoryFlagName('RefutedClaim')).toBeTrue();
      expect(component.isAdvisoryFlagName('HarnessArtifacts')).toBeFalse();
      expect(component.isAdvisoryFlagName('Truncated')).toBeFalse();
      expect(component.isAdvisoryFlagName('Empty')).toBeFalse();
    });

    it('should count and name the critical error answers alongside the advisory ones', () => {
      component.selectedRunDetail = buildCompletedRun({
        advisoryFlagAnswerCount: 2,
        answers: [
          buildScoredAnswer(1, { qualityScore: 25, criticalError: true }),
          buildScoredAnswer(2),
          buildScoredAnswer(3, { qualityScore: 25, criticalError: true, answerFlags: 8, answerFlagNames: ['ReasoningBleed'] }),
          buildScoredAnswer(4),
          buildScoredAnswer(5, { answerFlags: 16, answerFlagNames: ['RepeatedFragments'] })
        ]
      });
      fixture.detectChanges();

      expect(component.criticalErrorAnswerCount).toBe(2);
      expect(component.criticalErrorQuestionNumbers).toBe('1, 3');
      expect(component.advisoryFlagQuestionNumbers).toBe('3, 5');

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('2 answer(s) capped by a critical error (question(s) 1, 3).');
      expect(text).toContain('2 answer(s) carry advisory flags (question(s) 3, 5).');
    });

    it('should raise the integrity notice for a critical error that is the only cause', () => {
      component.selectedRunDetail = buildCompletedRun({
        answers: [buildScoredAnswer(1, { qualityScore: 25, criticalError: true })]
      });
      fixture.detectChanges();

      const text = integrityNoticeText().replace(/\s+/g, ' ').trim();
      expect(text).toContain('1 answer(s) capped by a critical error (question(s) 1).');
      expect(text).toContain('read this count, not the index, for this failure mode.');
      expect(text).not.toContain('advisory flags');
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

    it('should present the run as two stages, not three', () => {
      // BenchmarkService assesses each answer immediately after producing it, inside the same
      // loop, so "collecting" and "assessing" were never separate phases in wall-clock terms.
      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        answers: [buildScoredAnswer(1), buildScoredAnswer(2)]
      });

      expect(component.runStage).toBe('finalizing');
      expect(component.runStageLabel).toContain('Stage 2 of 2 — Synthesis and scoring');

      component.activeRunDetail = buildCompletedRun({
        status: 'Running',
        totalQuestionCount: 2,
        answers: [buildScoredAnswer(1, { assessmentStatus: 'Pending' }), buildScoredAnswer(2)]
      });

      // An answer still awaiting assessment keeps the run in stage 1: the stage covers both.
      expect(component.runStage).toBe('answering');
      expect(component.runStageLabel).toContain('Stage 1 of 2 — Collecting and assessing answers');
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

    it('should record in-flight questions and the two-stage number in the diagnostics text', () => {
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

      expect(diagnostics).toContain('Stage: 1');
      expect(diagnostics).toContain('In flight: Q2');
      expect(diagnostics).toContain('[Q2] status=Answering');
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
      expect(groups[1].querySelector('label')?.textContent?.trim()).toBe('Assessor Model (Evaluator)');
      expect(groups[2].querySelector('label')?.textContent?.trim()).toBe('Second Opinion Assessor (optional)');

      // The explanatory hint travels with the second-opinion selector, not loose in the row.
      // The trigger list moved to the mode dropdown's own hint when that control was added, so
      // this one describes what the second assessor is for rather than when it fires.
      const hint = groups[2].querySelector('.form-hint');
      expect(hint).toBeTruthy();
      expect(hint!.textContent).toContain('Produces a second, independent verdict');
    });

    it('should place Claim Verifier and Response Style in .form-row.claim-verifier-row in col 1 and col 2', () => {
      component.activeSubTab = 'run';
      fixture.detectChanges();

      const row = fixture.nativeElement.querySelector('.form-row.claim-verifier-row');
      expect(row).toBeTruthy();

      const groups = Array.from(row.querySelectorAll(':scope > .form-group')) as HTMLElement[];
      expect(groups.length).toBe(2);
      expect(groups[0].querySelector('label')?.textContent?.trim()).toBe('Claim Verifier (optional)');
      expect(groups[1].querySelector('label')?.textContent?.trim()).toBe('Response Style (candidate prompt)');
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
  });

  describe('scoring profile fit advisory', () => {
    /** Re-points the Model Under Test at a config carrying the given thinking level. */
    function selectTestedModelWithThinkingLevel(level: string | null): void {
      component.systemConfigs = [{ ...component.systemConfigs[0], thinkingLevel: level }];
      component.testedConfigId = component.systemConfigs[0].id;
    }

    /** The hint rendered inside the scoring profile's own .form-group, if any. */
    function profileFitHintText(): string {
      const group = (fixture.nativeElement.querySelector('#profileSelect') as HTMLElement)?.parentElement;
      const hint = group?.querySelector('.form-hint') as HTMLElement | null;
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

    it('should offer the four modes in coverage order', fakeAsync(() => {
      const select = modeSelect();
      expect(select).toBeTruthy();

      const labels = Array.from(select!.querySelectorAll('option')).map(o => (o.textContent || '').trim());
      expect(labels).toEqual([
        'Never',
        'Only flagged answers',
        'Flagged answers and statistical outliers',
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
      expect(text).toContain('Second opinion: mode All, threshold 50, outlier delta 25');
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
      expect(text).toContain('contested verdicts: 1, unevidenced deductions: 0, refuted claims: 0, re-assessed: 1');
      expect(text).toContain('unverified claims: 2');
      expect(text).toContain('4.3 mean abs delta');
      expect(text).toContain('over 2 of 2 answered, disagreements: 1');
      // Full coverage, so no conditioning caveat.
      expect(text).not.toContain('coverage selected by trigger');
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

  describe('Model Pricing Feature', () => {
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
      expect(content).toContain('$1.2345');
      expect(content).toContain('Anthropic API');
      
      const marker = card!.querySelector('.degraded-tag');
      expect(marker).toBeTruthy();
      expect(marker?.textContent?.trim()).toBe('*');
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
});
