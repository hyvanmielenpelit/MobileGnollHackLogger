import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { AdminBenchmarkService } from '../../services/admin-benchmark.service';

describe('AdminBenchmarkComponent', () => {
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: jasmine.SpyObj<AdminBenchmarkService>;

  beforeEach(async () => {
    benchmarkServiceMock = jasmine.createSpyObj('AdminBenchmarkService', [
      'getSuites',
      'getRuns',
      'getQuestions',
      'getScoringProfiles',
      'startRun',
      'getRun',
      'cancelRun',
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
      'rescoreRun'
    ]);

    benchmarkServiceMock.getActiveDifficultyAssessment.and.returnValue(of(null));
    benchmarkServiceMock.getSuiteRunsFootprint.and.returnValue(of({ runCount: 0, totalAnswerCharacters: 0 }));
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
        speedTargetMs: 5000,
        speedDecayK: 25.0,
        maxParallelQuestions: 1,
        createdAtUtc: '2026-09-01T00:00:00Z',
        modifiedAtUtc: '2026-09-01T00:00:00Z'
      }
    ]));
    benchmarkServiceMock.getRuns.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [AdminBenchmarkComponent],
      providers: [
        { provide: AdminBenchmarkService, useValue: benchmarkServiceMock },
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
});
