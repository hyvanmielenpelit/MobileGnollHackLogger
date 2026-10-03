import type { Mock, MockedObject } from "vitest";
import { ChangeDetectorRef } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { MarkdownEditorComponent } from '../../shared/markdown-editor/markdown-editor.component';
import { MultiRunComponent } from './multi-run/multi-run.component';
import { AdminBenchmarkService } from '../../services/admin-benchmark.service';
import { clearStoredState, createAdminBenchmarkFixture } from './benchmark.component.testing';

describe('AdminBenchmarkComponent', () => {
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;

  beforeEach(clearStoredState);

  afterEach(clearStoredState);

  beforeEach(async () => {
    ({ component, fixture, benchmarkServiceMock } = await createAdminBenchmarkFixture());
  });

  it('should open a run requested through openRunId exactly once, and report it handled', () => {
    const viewRunDetail = vi.spyOn(component, 'viewRunDetail').mockReturnValue(undefined);
    const handled = vi.spyOn(component.openRunHandled, 'emit').mockReturnValue(undefined);

    component.openRunId = 123;
    component.openRunId = null;

    expect(viewRunDetail).toHaveBeenCalledTimes(1);

    expect(viewRunDetail).toHaveBeenCalledWith(123);
    expect(handled).toHaveBeenCalledTimes(1);
  });

  it('should open a run requested before its view existed once the view is initialised', async () => {
    const early = TestBed.createComponent(AdminBenchmarkComponent);
    const viewRunDetail = vi.spyOn(early.componentInstance, 'viewRunDetail').mockReturnValue(undefined);
    early.componentInstance.openRunId = 77;

    expect(viewRunDetail).not.toHaveBeenCalled();

    early.detectChanges();
    await Promise.resolve();

    expect(viewRunDetail).toHaveBeenCalledTimes(1);

    expect(viewRunDetail).toHaveBeenCalledWith(77);
    early.destroy();
  });

  describe('navigation requests', () => {
    const suite = (id: number) => ({
      id, name: `Suite ${id}`, description: 'Test', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null,
      questionCount: 15, assessedQuestionCount: 15, difficultyFullyAssessed: true
    });

    let handled: Mock;

    beforeEach(() => {
      benchmarkServiceMock.getSuites.mockReturnValue(of([suite(3), suite(7)]));
      handled = vi.spyOn(component.navigationHandled, 'emit').mockReturnValue(undefined) as unknown as Mock;
    });

    it('selects Manage Suites, outlines and focuses the requested suite, and reports it handled once', async () => {
      component.navigation = { subTab: 'suites', suiteId: 7 };
      await Promise.resolve();

      expect(component.activeSubTab).toBe('suites');
      expect(component.linkedSuiteId).toBe(7);
      const card: HTMLElement = fixture.nativeElement.querySelector('#bm-suite-7');
      expect(document.activeElement).toBe(card);
      expect(card.classList).toContain('suite-card-linked');
      expect(fixture.nativeElement.querySelector('#bm-suite-3').classList).not.toContain('suite-card-linked');
      expect(handled).toHaveBeenCalledTimes(1);
    });

    it('lands on Manage Suites for a suite id without a sub-tab', () => {
      component.navigation = { subTab: null, suiteId: 7 };

      expect(component.activeSubTab).toBe('suites');
      expect(component.linkedSuiteId).toBe(7);
    });

    it('selects the named sub-tab', () => {
      component.navigation = { subTab: 'history', suiteId: null };

      expect(component.activeSubTab).toBe('history');
    });

    it('stays on Run for an unknown sub-tab and still reports the request handled', async () => {
      component.navigation = { subTab: 'bogus', suiteId: null };
      await Promise.resolve();

      expect(component.activeSubTab).toBe('run');
      expect(handled).toHaveBeenCalledTimes(1);
    });

    it('says the linked suite no longer exists and links nothing when it is absent', () => {
      component.navigation = { subTab: 'suites', suiteId: 99 };

      expect(component.actionErrorMessage).toBe('The linked suite (id 99) no longer exists.');
      expect(component.linkedSuiteId).toBeNull();
      expect(fixture.nativeElement.querySelectorAll('.suite-card-linked').length).toBe(0);
    });

    it('clears the linked suite when another sub-tab is selected', () => {
      component.navigation = { subTab: 'suites', suiteId: 7 };
      expect(component.linkedSuiteId).toBe(7);

      component.selectSubTab('history');

      expect(component.linkedSuiteId).toBeNull();
    });

    it('applies a request set before ngOnInit once, after it', async () => {
      const early = TestBed.createComponent(AdminBenchmarkComponent);
      const earlyHandled = vi.spyOn(early.componentInstance.navigationHandled, 'emit').mockReturnValue(undefined);
      const select = vi.spyOn(early.componentInstance, 'selectSubTab');
      early.componentInstance.navigation = { subTab: 'suites', suiteId: 7 };

      expect(select).not.toHaveBeenCalled();

      early.detectChanges();
      await Promise.resolve();

      expect(select).toHaveBeenCalledTimes(1);
      expect(early.componentInstance.activeSubTab).toBe('suites');
      expect(early.componentInstance.linkedSuiteId).toBe(7);
      expect(earlyHandled).toHaveBeenCalledTimes(1);
      early.destroy();
    });
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
      expect(labels, label).toContain(label);
    }

    const primaryButtons = buttons.filter(b => b.classList.contains('btn-gh'));
    expect(primaryButtons.length).toBe(2); // One "Manage Questions" per suite card.
    const secondaryButtons = buttons.filter(b => !b.classList.contains('btn-gh'));
    expect(secondaryButtons.every(b => b.classList.contains('btn-ghost'))).toBe(true);

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
      expect(iconCharacters.test(element.textContent ?? ''), element.textContent ?? '').toBe(false);
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
    expect(generateBtn, 'Generate Questions button should render for a suite with a game snapshot').toBeTruthy();

    // The click sets component state synchronously through the (click) binding; fixture.detectChanges()
    // is deliberately not called again afterwards, so the newly visible child's own ngOnChanges (which
    // calls service methods this spec does not stub) never fires.
    generateBtn!.click();

    expect(component.generationDialogVisible).toBe(true);
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
    expect(dialog.classList.contains('benchmark-form-dialog')).toBe(true);
  });

  it('should mark the suite form dialog as the wide markdown-editor variant', () => {
    fixture.detectChanges();

    const dialog: HTMLDialogElement = fixture.nativeElement.querySelector('dialog.benchmark-suite-form-dialog');
    expect(dialog).toBeTruthy();
    expect(dialog.classList.contains('benchmark-form-dialog')).toBe(true);
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

    let suiteClose: Mock;
    let confirmShowModal: Mock;

    function generateButton(): HTMLButtonElement | undefined {
      const host = fixture.nativeElement as HTMLElement;
      return Array.from(host.querySelectorAll<HTMLButtonElement>('dialog.benchmark-suite-form-dialog .suite-desc-tools button'))
        .find(b => (b.textContent ?? '').trim() === 'Generate with AI');
    }

    beforeEach(() => {
      fixture.detectChanges();
      vi.spyOn(component.suiteDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      suiteClose = vi.spyOn(component.suiteDialog.nativeElement, 'close').mockReturnValue(undefined);
      confirmShowModal = vi.spyOn(component.confirmActionDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      vi.spyOn(component.confirmActionDialog.nativeElement, 'close').mockReturnValue(undefined);
    });

    it('disables Generate with AI in create mode and enables it for an existing suite', () => {
      component.openCreateSuite();
      fixture.detectChanges();
      expect(generateButton()).toBeTruthy();
      expect(generateButton()!.disabled).toBe(true);

      component.openEditSuite(suite);
      fixture.detectChanges();
      expect(generateButton()!.disabled).toBe(false);
    });

    it('sets descriptionGenerationVisible when Generate with AI is clicked', () => {
      component.openEditSuite(suite);
      fixture.detectChanges();

      // No detectChanges after the click, so the child's ngOnChanges never reaches unstubbed services.
      generateButton()!.click();

      expect(component.descriptionGenerationVisible).toBe(true);
      expect(component.descriptionGenerationSuite).toBe(suite);
    });

    it('writes a generated description into the suite form and marks it dirty', () => {
      component.openEditSuite(suite);
      expect(component.suiteFormDirty).toBe(false);

      component.onDescriptionGenerated('## Draft');

      expect(component.suiteForm.description).toBe('## Draft');
      expect(component.suiteFormDirty).toBe(true);
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

      expect(event.defaultPrevented).toBe(true);
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

      expect(component.canAutoRateAll).toBe(false);

      const button = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.questions-toolbar button'))
        .find(b => b.textContent?.trim() === 'AI Auto-Rate All Difficulties');
      expect(button).toBeTruthy();
      expect(button!.getAttribute('aria-disabled')).toBe('true');

      vi.spyOn(component, 'openDifficultyAssessorDialog').mockReturnValue(undefined);
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

      expect(component.canAutoRateAll).toBe(true);

      const button = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.questions-toolbar button'))
        .find(b => b.textContent?.trim() === 'AI Auto-Rate All Difficulties');
      expect(button!.getAttribute('aria-disabled')).toBeNull();

      vi.spyOn(component, 'openDifficultyAssessorDialog').mockReturnValue(undefined);
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
      vi.spyOn(component.importDefaultSuitesDialog.nativeElement, 'showModal').mockReturnValue(undefined);
      vi.spyOn(component.importDefaultSuitesDialog.nativeElement, 'close').mockReturnValue(undefined);
    });

    it('loads the catalog and renders one checkbox per entry, named for the suite', () => {
      benchmarkServiceMock.getDefaultSuiteCatalog.mockReturnValue(of([catalogEntry] as any));
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
      benchmarkServiceMock.getDefaultSuiteCatalog.mockReturnValue(of([catalogEntry] as any));
      fixture.detectChanges();
      component.openImportDefaultSuitesDialog();
      fixture.detectChanges();

      const dialogEl = component.importDefaultSuitesDialog.nativeElement;
      const importBtn = dialogEl.querySelector('.dialog-footer .btn-gh:not(.btn-gh-cancel)') as HTMLButtonElement;
      expect(importBtn.getAttribute('aria-disabled')).toBe('true');

      component.toggleDefaultSuite(catalogEntry.key);
      (component as unknown as { cdr: ChangeDetectorRef }).cdr.detectChanges();

      expect(component.canImportDefaultSuites).toBe(true);
      expect(importBtn.getAttribute('aria-disabled')).toBe('false');
    });

    it('imports the selected keys and reloads the suite list', () => {
      benchmarkServiceMock.getDefaultSuiteCatalog.mockReturnValue(of([catalogEntry] as any));
      benchmarkServiceMock.importDefaultSuites.mockReturnValue(of({
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
      benchmarkServiceMock.getDefaultSuiteCatalog.mockReturnValue(of([catalogEntry] as any));
      benchmarkServiceMock.importDefaultSuites.mockReturnValue(of({
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
      benchmarkServiceMock.getDefaultSuiteCatalog.mockReturnValue(of([invalidEntry] as any));
      fixture.detectChanges();
      component.openImportDefaultSuitesDialog();
      fixture.detectChanges();

      const row = component.importDefaultSuitesDialog.nativeElement.querySelector('.default-suite-row-invalid');
      expect(row).toBeTruthy();
      expect(row!.textContent).toContain('Missing "key" field.');
      expect(row!.querySelector('input[type="checkbox"]')).toBeNull();
    });

    it('renders the description as HTML, not Markdown source', () => {
      benchmarkServiceMock.getDefaultSuiteCatalog.mockReturnValue(of([catalogEntry] as any));
      fixture.detectChanges();
      component.openImportDefaultSuitesDialog();
      fixture.detectChanges();

      const dialogEl = component.importDefaultSuitesDialog.nativeElement;
      expect(dialogEl.querySelector('.default-suite-description .markdown-body strong')).toBeTruthy();
      expect(dialogEl.textContent).not.toContain('**');
    });

    it('renders one difficulty badge per band with its count', () => {
      benchmarkServiceMock.getDefaultSuiteCatalog.mockReturnValue(of([catalogEntry] as any));
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
      benchmarkServiceMock.getDefaultSuiteCatalog.mockReturnValue(of([catalogEntry] as any));
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
      benchmarkServiceMock.getDefaultSuiteCatalog.mockReturnValue(of([catalogEntry] as any));
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
    benchmarkServiceMock.getSuites.mockReturnValue(of([]));
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
    benchmarkServiceMock.getSuites.mockReturnValue(of([]));
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
    vi.spyOn(window, 'confirm').mockReturnValue(undefined as any);
    benchmarkServiceMock.deleteSuite.mockReturnValue(of(void 0));
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
    benchmarkServiceMock.deleteSuite.mockReturnValue(throwError(() => ({ status: 409, error: refusal })));
    const logged = vi.spyOn(console, 'error').mockReturnValue(undefined);
    component.activeSubTab = 'suites';
    component.suites = [
      { id: 42, name: 'Target Suite', description: 'Test', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null, questionCount: 1, assessedQuestionCount: 0, difficultyFullyAssessed: false }
    ];
    fixture.detectChanges();

    component.deleteSuite(42);
    component.executeConfirmAction();

    expect(component.actionErrorMessage).toBe(refusal);
    expect(logged).not.toHaveBeenCalledWith('Failed to delete suite', expect.anything());
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

    vi.spyOn(component.difficultyAssessorDialog.nativeElement, 'showModal').mockReturnValue(undefined);

    component.openDifficultyAssessorDialog(testSuite);

    expect(component.difficultyAssessorDialog.nativeElement.showModal).toHaveBeenCalled();
    expect(component.suiteForDifficultyAssessment).toBe(testSuite);
    expect(component.difficultyAssessmentScope).toBe('unassessed');
    expect(component.difficultyAssessmentTargetDescription).toBe('the 5 of 15 questions in Default Suite that do not yet have an assessed difficulty');
    expect(benchmarkServiceMock.startDifficultyAssessment).not.toHaveBeenCalled();

    (component as unknown as { cdr: ChangeDetectorRef }).cdr.detectChanges();
    const radios: HTMLInputElement[] = Array.from(fixture.nativeElement.querySelectorAll('.difficulty-scope-fieldset input[type="radio"]'));
    expect(radios.map(r => r.value)).toEqual(['unassessed', 'suite']);
    expect(radios[0].checked).toBe(true);
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
    vi.spyOn(component.difficultyAssessorDialog.nativeElement, 'showModal').mockReturnValue(undefined);

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
    vi.spyOn(component.difficultyAssessorDialog.nativeElement, 'showModal').mockReturnValue(undefined);

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

    benchmarkServiceMock.startDifficultyAssessment.mockReturnValue(of({ jobId: 'job-123' }));
    benchmarkServiceMock.getDifficultyAssessment.mockReturnValue(of(mockJob));

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
    expect(component.difficultyJobIsRunning).toBe(true);
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

    benchmarkServiceMock.startDifficultyAssessment.mockReturnValue(of({ jobId: 'job-unassessed' }));
    benchmarkServiceMock.getDifficultyAssessment.mockReturnValue(of({
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
    benchmarkServiceMock.startDifficultyAssessment.mockReturnValue(throwError(() => errorResponse));
    benchmarkServiceMock.getDifficultyAssessment.mockReturnValue(of(existingJob));

    component.confirmDifficultyAssessment();

    expect(component.difficultyDialogPhase).toBe('progress');
    expect(component.difficultyJob).toEqual(existingJob);
    expect(component.difficultyJobIsRunning).toBe(true);
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
    benchmarkServiceMock.cancelDifficultyAssessment.mockReturnValue(of({ cancelled: true }));
    benchmarkServiceMock.getDifficultyAssessment.mockReturnValue(of({ ...runningJob, status: 'Cancelled' }));

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
    benchmarkServiceMock.cancelDifficultyAssessment.mockReturnValue(of({ cancelled: true }));
    benchmarkServiceMock.getDifficultyAssessment.mockReturnValue(of(runningJob));

    component.terminateDifficultyAssessment();
    fixture.detectChanges();

    expect(component.terminatingDifficultyJob).toBe(true);
    expect(component.difficultyJobIsRunning).toBe(true);
    const terminatingButtons: HTMLButtonElement[] = Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button'))
      .filter(b => b.textContent?.includes('Terminating…'));
    expect(terminatingButtons.length).toBeGreaterThan(0);
    terminatingButtons.forEach(b => {
      expect(b.disabled).toBe(true);
      expect(b.getAttribute('aria-busy')).toBe('true');
    });

    const cancelledJob = {
      ...runningJob,
      status: 'Cancelled',
      completedAtUtc: '2026-09-02T00:01:00Z',
      items: runningJob.items.map(i => i.status === 'Rated' ? i : { ...i, status: 'Cancelled' })
    };
    benchmarkServiceMock.getDifficultyAssessment.mockReturnValue(of(cancelledJob));

    component.startDifficultyPolling('job-terminating');
    component.stopDifficultyPolling();
    fixture.detectChanges();

    expect(component.terminatingDifficultyJob).toBe(false);
    expect(component.difficultyJobIsTerminal).toBe(true);
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
    benchmarkServiceMock.cancelDifficultyAssessment.mockReturnValue(throwError(() => ({ status: 500, error: 'Boom' })));

    component.terminateDifficultyAssessment();

    expect(component.terminatingDifficultyJob).toBe(false);
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
    benchmarkServiceMock.startDifficultyAssessment.mockReturnValue(of({ jobId: 'retry-job-1' }));
    benchmarkServiceMock.getDifficultyAssessment.mockReturnValue(of({ ...failedJob, id: 'retry-job-1', status: 'Running' }));

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
      const writeTextSpy = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
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
      expect(component.copiedDiagnostics).toBe(true);

      const status = fixture.nativeElement.querySelector('.diagnostics-copy-status') as HTMLElement;
      expect(status.textContent?.trim()).toBe('Diagnostics copied to clipboard');

      tick(2000);
      fixture.detectChanges();

      expect(component.copiedDiagnostics).toBe(false);
      expect(status.textContent?.trim()).toBe('');
    }));

    it('should surface a clipboard failure in the inline dialog error rather than throwing', fakeAsync(() => {
      vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));

      const copyButton = fixture.nativeElement.querySelector(
        'button[aria-label="Copy difficulty assessment diagnostics"]'
      ) as HTMLButtonElement;

      copyButton.click();
      tick();
      fixture.detectChanges();

      expect(component.copiedDiagnostics).toBe(false);
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

    expect(component.canStartRun).toBe(false);

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
      expect(tabs().length).toBe(7);
    });

    it('should place Multi-Suite fourth, right after Multi-Run Analysis', () => {
      expect(tabs().map(t => t.id)).toEqual([
        'bm-tab-run', 'bm-tab-history', 'bm-tab-multirun', 'bm-tab-multisuite',
        'bm-tab-suites', 'bm-tab-profiles', 'bm-tab-modelcomparison'
      ]);
      const multiSuite = tabs()[3];
      expect((multiSuite.textContent || '').trim()).toBe('Multi-Suite');
      expect(multiSuite.getAttribute('aria-controls')).toBe('bm-panel-multisuite');
      expect(component.subTabs[3]).toBe('multisuite');
    });

    it('should reach Multi-Suite with the arrow keys from both of its neighbors', () => {
      tabs()[2].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }));
      fixture.detectChanges();
      expect(component.activeSubTab).toBe('multisuite');
      expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#bm-tab-multisuite'));

      tabs()[4].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true, cancelable: true }));
      fixture.detectChanges();
      expect(component.activeSubTab).toBe('multisuite');
    });

    it('should mark exactly one tab selected, matching activeSubTab', () => {
      const selected = tabs().filter(t => t.getAttribute('aria-selected') === 'true');
      expect(selected.length).toBe(1);
      expect(selected[0].id).toBe('bm-tab-' + component.activeSubTab);
    });

    it('should give exactly one tab tabindex="0" and the rest tabindex="-1"', () => {
      const all = tabs();
      expect(all.filter(t => t.getAttribute('tabindex') === '0').length).toBe(1);
      expect(all.filter(t => t.getAttribute('tabindex') === '-1').length).toBe(6);
    });

    it('should wrap forward from the last tab to the first with ArrowRight', () => {
      component.activeSubTab = 'modelcomparison';
      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'ArrowRight' }), 6);
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

      component.onTabKeydown(new KeyboardEvent('keydown', { key: 'Home' }), 6);
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

    it('should render the Multi-Suite panel, and nothing else, on the multisuite tab', () => {
      fixture.nativeElement.querySelector('#bm-tab-multisuite').click();
      fixture.detectChanges();

      const panel = fixture.nativeElement.querySelector('[role="tabpanel"]');
      expect(panel.id).toBe('bm-panel-multisuite');
      expect(panel.getAttribute('aria-labelledby')).toBe('bm-tab-multisuite');
      expect(panel.querySelector('app-benchmark-batteries')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('#bm-panel-run')).toBeNull();
      expect(component.batteriesPanel).toBeTruthy();
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
      benchmarkServiceMock.getRuns.mockClear();
      component.selectSubTab('history');
      expect(benchmarkServiceMock.getRuns).toHaveBeenCalled();
    });

    it('should load suites when the suites tab is selected', () => {
      benchmarkServiceMock.getSuites.mockClear();
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
      benchmarkServiceMock.getScoringProfiles.mockClear();
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
      expect(input.classList.contains('run-count-input')).toBe(true);
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
          expect(name, `unnamed button in "${where}" tab: ${btn.outerHTML.slice(0, 120)}`).toBeTruthy();
        }
      });
    });

    it('should give every button an explicit type="button"', () => {
      forEachSubTab(where => {
        const untyped = (
          Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[]
        ).filter(b => b.getAttribute('type') !== 'button');

        expect(untyped.map(b => b.outerHTML.slice(0, 100)), `buttons without type="button" in "${where}" tab`).toEqual([]);
      });
    });

    it('should not use the title attribute on any button', () => {
      forEachSubTab(where => {
        const titled = (
          Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[]
        ).filter(b => b.hasAttribute('title'));

        expect(titled.map(b => b.getAttribute('title')), `buttons still using title in "${where}" tab`).toEqual([]);
      });
    });

    it('should not use the invented btn-gh-primary or btn-gh-danger variants', () => {
      forEachSubTab(where => {
        const stale = fixture.nativeElement.querySelectorAll('.btn-gh-primary, .btn-gh-danger, .btn-gh-icon, .btn-danger-icon, .subnav-btn');
        expect(stale.length, `stale button classes in "${where}" tab`).toBe(0);
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
        expect(tooltip, `no tooltip element for interestfor="${id}"`).toBeTruthy();
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

  describe('editing a question from the question generation dialog', () => {
    const suiteA = { id: 1, name: 'Suite A', description: '', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null, questionCount: 1 } as any;
    const suiteB = { id: 2, name: 'Suite B', description: '', createdAtUtc: '2026-09-01T00:00:00Z', modifiedAtUtc: null, questionCount: 1, gameSnapshotId: 7 } as any;
    const questionA = { id: 10, benchmarkSuiteId: 1, orderIndex: 0, questionText: 'A?', difficulty: 1, expectedPoints: '' } as any;
    const questionB = { id: 20, benchmarkSuiteId: 2, orderIndex: 0, questionText: 'B?', difficulty: 2, expectedPoints: '- point' } as any;
    let refreshQuestions: Mock;

    beforeEach(() => {
      (benchmarkServiceMock as any).updateQuestion = vi.fn().mockReturnValue(of(questionB));
      (benchmarkServiceMock as any).createQuestion = vi.fn().mockReturnValue(of(questionA));
      component.openGenerationDialog(suiteB);
      fixture.detectChanges();
      // The child's own reload also calls getQuestions, which would blur what the host reloaded.
      refreshQuestions = vi.spyOn(component.generationDialog!, 'refreshQuestions').mockImplementation(() => {}) as unknown as Mock;
      benchmarkServiceMock.getQuestions.mockClear();
      benchmarkServiceMock.getSuites.mockClear();
    });

    it('saves the edit when Manage Questions was never opened', () => {
      expect(component.currentSuiteForQuestions).toBeNull();

      component.onGenerationEditQuestion(questionB);
      component.questionForm.questionText = 'B, edited?';
      component.saveQuestion();

      expect((benchmarkServiceMock as any).updateQuestion).toHaveBeenCalledWith(20, expect.objectContaining({ questionText: 'B, edited?' }));
      expect(refreshQuestions).toHaveBeenCalled();
      expect(benchmarkServiceMock.getSuites).toHaveBeenCalled();
      expect(benchmarkServiceMock.getQuestions).not.toHaveBeenCalled();
    });

    it('leaves another suite\'s Manage Questions list alone', () => {
      component.currentSuiteForQuestions = suiteA;
      component.questions = [questionA];

      component.onGenerationEditQuestion(questionB);
      component.saveQuestion();

      expect((benchmarkServiceMock as any).updateQuestion).toHaveBeenCalledWith(20, expect.anything());
      expect(benchmarkServiceMock.getQuestions).not.toHaveBeenCalledWith(1);
      expect(component.questions).toEqual([questionA]);
      expect(refreshQuestions).toHaveBeenCalled();
    });

    it('reloads the Manage Questions list when it shows the same suite', () => {
      component.currentSuiteForQuestions = suiteB;

      component.onGenerationEditQuestion(questionB);
      component.saveQuestion();

      expect(benchmarkServiceMock.getQuestions).toHaveBeenCalledWith(2);
      expect(refreshQuestions).toHaveBeenCalled();
    });

    it('still creates a new question in the Manage Questions suite', () => {
      component.currentSuiteForQuestions = suiteA;

      component.openCreateQuestion();
      component.questionForm.questionText = 'New?';
      component.saveQuestion();

      expect((benchmarkServiceMock as any).createQuestion).toHaveBeenCalledWith(1, expect.objectContaining({ questionText: 'New?' }));
      expect(benchmarkServiceMock.getQuestions).toHaveBeenCalledWith(1);
    });
  });
});
