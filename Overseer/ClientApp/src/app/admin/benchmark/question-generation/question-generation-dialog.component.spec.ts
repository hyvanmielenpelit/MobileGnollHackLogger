import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { QuestionGenerationDialogComponent } from './question-generation-dialog.component';
import {
  AdminBenchmarkService,
  BenchmarkQuestionDto,
  BenchmarkSuiteDto,
  QuestionGenerationJobDto,
  QuestionGenerationJobItemDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';

/**
 * The question generation workspace. State is driven through inputs and real clicks: the component
 * is OnPush, so a property set directly on the instance does not re-render the view.
 */
describe('QuestionGenerationDialogComponent', () => {
  let component: QuestionGenerationDialogComponent;
  let fixture: ComponentFixture<QuestionGenerationDialogComponent>;
  let serviceMock: jasmine.SpyObj<AdminBenchmarkService>;

  const MODEL_ID = 7;

  function buildSuite(overrides: Partial<BenchmarkSuiteDto> = {}): BenchmarkSuiteDto {
    return {
      id: 5,
      name: 'Board Suite',
      description: null,
      createdAtUtc: '2026-09-16T08:00:00Z',
      modifiedAtUtc: null,
      questionCount: 2,
      assessedQuestionCount: 0,
      difficultyFullyAssessed: false,
      gameSnapshotId: 3,
      gameSnapshotName: 'Gnomish Mines level 3',
      hasGeneratedQuestions: true,
      reviewedQuestionCount: 0,
      ...overrides
    };
  }

  function buildQuestion(overrides: Partial<BenchmarkQuestionDto> = {}): BenchmarkQuestionDto {
    return {
      id: 101,
      benchmarkSuiteId: 5,
      orderIndex: 1,
      itemRevision: 1,
      questionText: 'Should I pray now?',
      difficulty: 1,
      expectedPoints: 'BOARD FACTS\n- HP 3 of 40',
      isGenerated: true,
      isReviewed: false,
      createdAtUtc: '2026-09-16T08:00:00Z',
      ...overrides
    };
  }

  function buildConfig(id: number, displayName: string): SystemAiConfigDto {
    return {
      id,
      displayName,
      provider: 'OpenAI',
      modelId: `model-${id}`,
      thinkingLevel: 'high',
      reasoningMode: null,
      isEnabled: true,
      hasApiKey: true
    } as SystemAiConfigDto;
  }

  function buildItem(overrides: Partial<QuestionGenerationJobItemDto> = {}): QuestionGenerationJobItemDto {
    return {
      kind: 'Band',
      difficulty: 1,
      difficultyName: 'Simple',
      requestedCount: 6,
      generatedCount: 0,
      status: 'Pending',
      errorMessage: null,
      targetQuestionId: null,
      targetQuestionOrderIndex: null,
      targetQuestionExcerpt: null,
      startedAtUtc: null,
      completedAtUtc: null,
      modelCalls: 0,
      promptTokens: 0,
      outputTokens: 0,
      createdQuestionCount: 0,
      updatedQuestionCount: 0,
      discardedQuestionCount: 0,
      ...overrides
    };
  }

  function buildJob(overrides: Partial<QuestionGenerationJobDto> = {}): QuestionGenerationJobDto {
    return {
      id: 'job-1',
      suiteId: 5,
      suiteName: 'Board Suite',
      generatorConfigId: MODEL_ID,
      generatorDisplayName: 'GPT Generator',
      generatorProvider: 'OpenAI',
      generatorModelId: 'model-7',
      jobKind: 'Generation',
      instructions: 'Probe prayer timing.',
      status: 'Completed',
      startedAtUtc: '2026-09-16T08:00:00Z',
      completedAtUtc: '2026-09-16T08:02:00Z',
      totalModelCalls: 0,
      promptTokens: 0,
      outputTokens: 0,
      items: [],
      log: [],
      ...overrides
    };
  }

  /** Simple 6/6, Intermediate 4/6, Advanced failed. */
  function mixedOutcomeJob(): QuestionGenerationJobDto {
    return buildJob({
      status: 'CompletedWithErrors',
      totalModelCalls: 4,
      items: [
        buildItem({ difficulty: 1, difficultyName: 'Simple', status: 'Completed', generatedCount: 6, createdQuestionCount: 6 }),
        buildItem({ difficulty: 2, difficultyName: 'Intermediate', status: 'Completed', generatedCount: 4, createdQuestionCount: 4 }),
        buildItem({ difficulty: 3, difficultyName: 'Advanced', status: 'Failed', generatedCount: 0, errorMessage: 'Provider returned 401.' })
      ],
      log: [
        { timestampUtc: '2026-09-16T08:01:00Z', message: 'Intermediate band returned 4 of 6.', severity: 'warning' },
        { timestampUtc: '2026-09-16T08:02:00Z', message: 'Advanced band failed.', severity: 'error', rawExcerpt: 'HTTP 401 Unauthorized' }
      ]
    });
  }

  function open(): void {
    fixture.componentRef.setInput('benchmarkCapableConfigs', [buildConfig(MODEL_ID, 'GPT Generator'), buildConfig(9, 'Other')]);
    fixture.componentRef.setInput('defaultModelConfigId', MODEL_ID);
    fixture.componentRef.setInput('overseerBuildVersion', '2.3.4');
    fixture.componentRef.setInput('suite', buildSuite());
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
  }

  function query<T extends HTMLElement>(selector: string): T | null {
    return fixture.nativeElement.querySelector(selector) as T | null;
  }

  function queryAll<T extends HTMLElement>(selector: string): T[] {
    return Array.from(fixture.nativeElement.querySelectorAll(selector)) as T[];
  }

  function click(selector: string): void {
    const element = query<HTMLElement>(selector);
    expect(element).withContext(selector).toBeTruthy();
    element!.click();
    fixture.detectChanges();
  }

  /** Starts a generation whose first poll answers with `job`. */
  function startWith(job: QuestionGenerationJobDto): void {
    serviceMock.getQuestionGeneration.and.returnValue(of(job));
    click('.qg-start-btn');
  }

  beforeEach(async () => {
    serviceMock = jasmine.createSpyObj('AdminBenchmarkService', [
      'getQuestions', 'startQuestionGeneration', 'getQuestionGeneration', 'getActiveQuestionGeneration',
      'cancelQuestionGeneration', 'retryQuestionGeneration', 'regenerateQuestions', 'reviewQuestion'
    ]);
    serviceMock.getQuestions.and.returnValue(of([
      buildQuestion(),
      buildQuestion({ id: 102, orderIndex: 2, questionText: 'Which wand should I engrave-test first?', difficulty: 2 })
    ]));
    serviceMock.getActiveQuestionGeneration.and.returnValue(of(null));
    serviceMock.startQuestionGeneration.and.returnValue(of({ jobId: 'job-1' }));
    serviceMock.getQuestionGeneration.and.returnValue(of(buildJob()));
    serviceMock.cancelQuestionGeneration.and.returnValue(of({ cancelled: true }));
    serviceMock.retryQuestionGeneration.and.returnValue(of({ jobId: 'job-2' }));
    serviceMock.regenerateQuestions.and.returnValue(of({ jobId: 'job-3' }));
    serviceMock.reviewQuestion.and.callFake((id: number, reviewed: boolean) => of(buildQuestion({
      id, isReviewed: reviewed, reviewedAtRevision: reviewed ? 1 : null
    })));

    await TestBed.configureTestingModule({
      imports: [QuestionGenerationDialogComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: serviceMock }]
    }).compileComponents();

    fixture = TestBed.createComponent(QuestionGenerationDialogComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    // Polling runs on an interval; destroying the fixture clears it before the next spec.
    fixture.destroy();
  });

  it('should load the suite questions on open and render a rubric per card', () => {
    open();

    const dialog = query<HTMLDialogElement>('dialog.question-generation-dialog')!;
    expect(dialog.open).toBeTrue();
    expect(serviceMock.getQuestions).toHaveBeenCalledWith(5);
    expect(queryAll('.question-list-item').length).toBe(2);
    expect(queryAll('app-collapsible-markdown').length).toBe(2);
    expect(query('#qgDialogTitle')!.textContent).toContain('Generate Benchmark Questions');
  });

  it('should mark the authoring instructions textarea as the shared autosize class', () => {
    open();

    const textarea = query<HTMLTextAreaElement>('#qg-instructions');
    expect(textarea).toBeTruthy();
    expect(textarea!.classList.contains('gh-textarea-autosize')).toBeTrue();
  });

  it('should show band outcomes and retry the failed and partial bands with the current setup', () => {
    open();
    startWith(mixedOutcomeJob());

    const chips = queryAll('.job-status-chip');
    expect(chips.length).toBe(3);
    expect(chips[0].classList).toContain('status-completed');
    expect(chips[1].classList).toContain('status-partial');
    expect(chips[2].classList).toContain('status-failed');

    const progress = query<HTMLProgressElement>('#qgProgressBar')!;
    expect(progress.value).toBe(10);
    expect(progress.max).toBe(18);

    const fieldset = query<HTMLFieldSetElement>('fieldset.qg-setup')!;
    expect(fieldset.disabled).toBeFalse();

    click('.qg-retry-failed-btn');

    expect(serviceMock.retryQuestionGeneration).toHaveBeenCalledWith('job-1', {
      difficulties: [2, 3],
      discardExisting: false,
      generatorModelConfigurationId: MODEL_ID,
      instructions: component.instructions.trim()
    });
  });

  it('should lock the setup and offer no retry while the job is running', () => {
    open();
    startWith(buildJob({
      status: 'Running',
      completedAtUtc: null,
      items: [buildItem({ status: 'Generating' }), buildItem({ difficulty: 2, difficultyName: 'Intermediate' })]
    }));

    expect(query<HTMLFieldSetElement>('fieldset.qg-setup')!.disabled).toBeTrue();
    expect(query('.qg-retry-failed-btn')).toBeNull();
    expect(query('.progress-status')!.textContent).toContain('Generating Simple questions (item 1 of 2)');
    expect(query('#qgDialogTitle')!.textContent).toContain('Generating Benchmark Questions');
  });

  it('should render a warning log entry with the lowercase severity class', () => {
    open();
    startWith(mixedOutcomeJob());

    const warning = query('.log-entry.severity-warning');
    expect(warning).toBeTruthy();
    expect(warning!.textContent).toContain('Intermediate band returned 4 of 6.');
  });

  it('should copy diagnostics with the job id and the raw excerpt, and reset the status after two seconds', async () => {
    jasmine.clock().install();
    try {
      open();
      startWith(mixedOutcomeJob());
      const writeText = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());

      click('.diagnostics-toolbar .action-btn');
      await fixture.whenStable();
      fixture.detectChanges();

      expect(writeText).toHaveBeenCalledTimes(1);
      const copied = writeText.calls.mostRecent().args[0] as string;
      expect(copied).toContain('Job ID: job-1');
      expect(copied).toContain('Excerpt: HTTP 401 Unauthorized');
      expect(copied).toContain('--- INSTRUCTIONS ---');
      expect(query('.diagnostics-copy-status')!.textContent).toContain('copied');

      jasmine.clock().tick(2000);
      fixture.detectChanges();

      expect(query('.diagnostics-copy-status')!.textContent!.trim()).toBe('');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('should report a rejected clipboard write in the dialog error', async () => {
    open();
    startWith(mixedOutcomeJob());
    spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.reject(new Error('denied')));

    click('.diagnostics-toolbar .action-btn');
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.dialogError).toBe('Could not copy the question generation diagnostics to the clipboard.');
    expect(query('.qg-alert')!.textContent).toContain('Could not copy the question generation diagnostics');
  });

  it('should open the diagnostics when a poll reports the job failed', () => {
    open();
    startWith(buildJob({
      status: 'Failed',
      items: [buildItem({ status: 'Failed', errorMessage: 'Unexpected failure: bad key' })],
      log: [{ timestampUtc: '2026-09-16T08:00:01Z', message: 'Unexpected failure: bad key', severity: 'error' }]
    }));

    expect(query<HTMLDetailsElement>('details.job-diagnostics')!.open).toBeTrue();
    expect(query('#qgDialogTitle')!.textContent).toContain('Question Generation Failed');
    expect(query('.progress-status')!.textContent).toContain('Failed: Unexpected failure: bad key');
  });

  it('should regenerate one rubric directly from its card', () => {
    open();

    click('button[aria-label="Regenerate rubric for question 1"]');

    expect(serviceMock.regenerateQuestions).toHaveBeenCalledWith(jasmine.objectContaining({
      suiteId: 5,
      questionIds: [101],
      scope: 'Rubric',
      generatorModelConfigurationId: MODEL_ID
    }));
  });

  it('should confirm before regenerating the selected questions in place', () => {
    open();

    const checkboxes = queryAll<HTMLInputElement>('.question-list-item input[type="checkbox"]');
    checkboxes[0].click();
    fixture.detectChanges();
    checkboxes[1].click();
    fixture.detectChanges();

    expect(query('.qg-selection-count')!.textContent).toContain('2 selected');

    click('.qg-regenerate-questions-btn');

    expect(serviceMock.regenerateQuestions).not.toHaveBeenCalled();
    const confirm = query<HTMLDialogElement>('dialog.qg-confirm-dialog')!;
    expect(confirm.open).toBeTrue();
    expect(confirm.textContent).toContain('2 question(s) will be replaced in place');

    click('.qg-confirm-action');

    expect(confirm.open).toBeFalse();
    expect(serviceMock.regenerateQuestions).toHaveBeenCalledWith(jasmine.objectContaining({
      questionIds: [101, 102],
      scope: 'Question'
    }));
  });

  it('should reload questions, mark new ones and notify the host when an item completes', () => {
    serviceMock.getQuestions.and.returnValues(
      of([buildQuestion(), buildQuestion({ id: 102, orderIndex: 2 })]),
      of([buildQuestion(), buildQuestion({ id: 102, orderIndex: 2 }), buildQuestion({ id: 103, orderIndex: 3 })])
    );
    open();
    const notified = spyOn(component.questionsChanged, 'emit');

    startWith(buildJob({
      status: 'Running',
      completedAtUtc: null,
      items: [
        buildItem({ status: 'Completed', generatedCount: 1, requestedCount: 1, completedAtUtc: '2026-09-16T08:01:00Z', createdQuestionCount: 1 }),
        buildItem({ difficulty: 2, difficultyName: 'Intermediate', status: 'Generating' })
      ]
    }));

    expect(serviceMock.getQuestions).toHaveBeenCalledTimes(2);
    expect(notified).toHaveBeenCalledWith(5);
    const cards = queryAll('.question-list-item');
    expect(cards.length).toBe(3);
    expect(cards[2].classList).toContain('is-generation-new');
    expect(cards[0].classList).not.toContain('is-generation-new');
    expect(query('.qg-new-count')!.textContent).toContain('1 new or updated');
  });

  it('should report changed questions on close after a review toggle', () => {
    open();
    const closed = spyOn(component.closed, 'emit');

    click('.btn-review-toggle');
    expect(serviceMock.reviewQuestion).toHaveBeenCalledWith(101, true);

    click('.dialog-footer .btn-gh-cancel');

    expect(closed).toHaveBeenCalledWith({ questionsChanged: true });
    expect(query<HTMLDialogElement>('dialog.question-generation-dialog')!.open).toBeFalse();
  });
});
