import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';

import {
  DEFAULT_SUITE_DESCRIPTION_INSTRUCTIONS,
  SuiteDescriptionGenerationDialogComponent
} from './suite-description-generation-dialog.component';
import {
  AdminBenchmarkService,
  SuiteDescriptionGenerationResultDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';

/**
 * The suite description generation dialog. State is driven through inputs and real clicks: the
 * component is OnPush, so a property set directly on the instance does not re-render the view.
 */
describe('SuiteDescriptionGenerationDialogComponent', () => {
  let component: SuiteDescriptionGenerationDialogComponent;
  let fixture: ComponentFixture<SuiteDescriptionGenerationDialogComponent>;
  let serviceMock: jasmine.SpyObj<AdminBenchmarkService>;

  const MODEL_ID = 7;

  function buildConfig(id: number, displayName: string, overrides: Partial<SystemAiConfigDto> = {}): SystemAiConfigDto {
    return {
      id,
      displayName,
      provider: 'OpenAI',
      modelId: `model-${id}`,
      thinkingLevel: 'high',
      reasoningMode: null,
      isEnabled: true,
      hasApiKey: true,
      ...overrides
    } as SystemAiConfigDto;
  }

  function buildResult(overrides: Partial<SuiteDescriptionGenerationResultDto> = {}): SuiteDescriptionGenerationResultDto {
    return {
      suiteId: 5,
      suiteName: 'Board Suite',
      questionCount: 18,
      snapshotIncluded: true,
      gameSnapshotName: 'Gnomish Mines level 3',
      snapshotCharCount: 4000,
      promptCharCount: 6500,
      generatorConfigId: MODEL_ID,
      generatorDisplayName: 'GPT Generator',
      generatorProvider: 'OpenAI',
      generatorModelId: 'model-7',
      generatorThinkingLevel: 'high',
      generatorReasoningMode: null,
      generatorServiceTier: null,
      actualServiceTier: null,
      startedAtUtc: '2026-09-16T08:00:00Z',
      completedAtUtc: '2026-09-16T08:01:00Z',
      durationMs: 60000,
      timeToFirstTokenMs: 850,
      modelCalls: 1,
      promptTokens: 1000,
      uncachedInputTokens: 1000,
      cacheReadTokens: 0,
      cacheCreationTokens: 0,
      outputTokens: 300,
      reasoningTokens: 0,
      tokensEstimated: false,
      costUsd: 0.0123,
      pricingSource: 'catalog',
      status: 'Completed',
      description: '## Draft description\n\nSome generated text.',
      errorMessage: null,
      log: [],
      ...overrides
    };
  }

  function open(
    configs: SystemAiConfigDto[] = [buildConfig(MODEL_ID, 'GPT Generator'), buildConfig(9, 'Other')],
    gameSnapshotName: string | null = 'Gnomish Mines level 3'
  ): void {
    fixture.componentRef.setInput('benchmarkCapableConfigs', configs);
    fixture.componentRef.setInput('defaultModelConfigId', MODEL_ID);
    fixture.componentRef.setInput('overseerBuildVersion', '2.3.4');
    fixture.componentRef.setInput('suiteId', 5);
    fixture.componentRef.setInput('suiteName', 'Board Suite');
    fixture.componentRef.setInput('questionCount', 18);
    fixture.componentRef.setInput('gameSnapshotName', gameSnapshotName);
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

  beforeEach(async () => {
    serviceMock = jasmine.createSpyObj('AdminBenchmarkService', ['generateSuiteDescription']);
    serviceMock.generateSuiteDescription.and.returnValue(of(buildResult()));

    await TestBed.configureTestingModule({
      imports: [SuiteDescriptionGenerationDialogComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: serviceMock }]
    }).compileComponents();

    fixture = TestBed.createComponent(SuiteDescriptionGenerationDialogComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    fixture.destroy();
  });

  it('should render the picker with the benchmark-capable configs and preselect the default', () => {
    open();

    const dialog = query<HTMLDialogElement>('dialog.sdg-dialog')!;
    expect(dialog.open).toBeTrue();
    expect(query('#sdgModelTrigger .model-name')!.textContent).toContain('GPT Generator');

    click('#sdgModelTrigger');
    expect(queryAll('.model-option').length).toBe(2);
  });

  it('should hide the snapshot checkbox when the suite has no snapshot', () => {
    open(undefined, null);

    expect(query('input[name="sdgIncludeSnapshot"]')).toBeNull();
  });

  it('should show the snapshot checkbox when the suite has a snapshot', () => {
    open();

    expect(query('input[name="sdgIncludeSnapshot"]')).toBeTruthy();
  });

  it('should call the service with the request body and show indeterminate progress while running', () => {
    const subject = new Subject<SuiteDescriptionGenerationResultDto>();
    serviceMock.generateSuiteDescription.and.returnValue(subject.asObservable());
    open();

    click('.sdg-start-btn');

    expect(serviceMock.generateSuiteDescription).toHaveBeenCalledWith(5, {
      generatorModelConfigurationId: MODEL_ID,
      instructions: component.instructions.trim(),
      includeSnapshot: true,
      includeDebugText: false
    });
    expect(query('progress.job-progress')).toBeTruthy();
    expect(query('.progress-status')!.textContent).toContain('Generating with GPT Generator');
    expect(query('dialog.sdg-dialog .dialog-footer .btn-gh-delete')).toBeTruthy();
  });

  it('should render a completed result and apply it through Use this description', () => {
    const subject = new Subject<SuiteDescriptionGenerationResultDto>();
    serviceMock.generateSuiteDescription.and.returnValue(subject.asObservable());
    open();
    click('.sdg-start-btn');

    const generated = spyOn(component.descriptionGenerated, 'emit');
    subject.next(buildResult());
    subject.complete();
    fixture.detectChanges();

    const resultBox = query<HTMLTextAreaElement>('#sdgResult')!;
    expect(resultBox.value).toBe('## Draft description\n\nSome generated text.');
    expect(query('.run-stat-strip')!.textContent).toContain('$0.0123');
    expect(query('.run-stat-strip')!.textContent).toContain('1m 00s');

    // Preview is the default: the rendered pane is visible, the source pane hidden.
    expect(query('#sdgResult-tab-preview')!.getAttribute('aria-selected')).toBe('true');
    const preview = query<HTMLElement>('.sdg-result-preview')!;
    expect(preview.hasAttribute('hidden')).toBeFalse();
    // Heading level is the pipe's business; the preview only has to render it as a heading.
    const headings = Array.from(preview.querySelectorAll('h1, h2, h3')).map(h => h.textContent ?? '');
    expect(headings.some(text => text.includes('Draft description'))).toBeTrue();
    expect(resultBox.hasAttribute('hidden')).toBeTrue();

    click('#sdgResult-tab-markdown');
    expect(query('#sdgResult-tab-markdown')!.getAttribute('aria-selected')).toBe('true');
    expect(preview.hasAttribute('hidden')).toBeTrue();
    expect(resultBox.hasAttribute('hidden')).toBeFalse();

    // Generate again is the plain gold image button, never a ghost button in a footer.
    const again = query<HTMLButtonElement>('.sdg-again-btn')!;
    expect(again.classList.contains('btn-gh')).toBeTrue();
    expect(again.classList.contains('btn-ghost')).toBeFalse();

    click('.sdg-use-btn');

    expect(generated).toHaveBeenCalledWith('## Draft description\n\nSome generated text.');
    expect(query<HTMLDialogElement>('dialog.sdg-dialog')!.open).toBeFalse();
  });

  it('should auto-open diagnostics on a failed result and copy the log excerpt', async () => {
    serviceMock.generateSuiteDescription.and.returnValue(of(buildResult({
      status: 'Failed',
      description: null,
      errorMessage: 'The model returned no usable text.',
      log: [
        { timestampUtc: '2026-09-16T08:01:00Z', message: 'Empty response.', severity: 'error', rawExcerpt: 'HTTP 200, empty body' }
      ]
    })));
    open();
    click('.sdg-start-btn');

    expect(query<HTMLDetailsElement>('details.job-diagnostics')!.open).toBeTrue();
    expect(query('.sdg-alert')!.textContent).toContain('The model returned no usable text.');

    const writeText = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
    click('.diagnostics-toolbar .action-btn');
    await fixture.whenStable();
    fixture.detectChanges();

    expect(writeText).toHaveBeenCalledTimes(1);
    expect(writeText.calls.mostRecent().args[0] as string).toContain('Excerpt: HTTP 200, empty body');
  });

  it('should unsubscribe on Cancel and read the status as Cancelled', () => {
    const subject = new Subject<SuiteDescriptionGenerationResultDto>();
    serviceMock.generateSuiteDescription.and.returnValue(subject.asObservable());
    open();
    click('.sdg-start-btn');

    click('dialog.sdg-dialog .dialog-footer .btn-gh-delete');

    expect(component.status).toBe('Cancelled');
    expect(query('.sdg-alert')!.textContent).toContain('cancelled');

    // Unsubscribed: a late emission from the aborted request must not resurrect the run.
    subject.next(buildResult());
    expect(component.status).toBe('Cancelled');
  });

  it('should abort the request and close without a prompt when closed while running', () => {
    const subject = new Subject<SuiteDescriptionGenerationResultDto>();
    serviceMock.generateSuiteDescription.and.returnValue(subject.asObservable());
    open();
    click('.sdg-start-btn');
    const closed = spyOn(component.closed, 'emit');

    click('.dialog-header .btn-icon-action');

    expect(query<HTMLDialogElement>('dialog.sdg-confirm-dialog')!.open).toBeFalse();
    expect(query<HTMLDialogElement>('dialog.sdg-dialog')!.open).toBeFalse();
    expect(closed).toHaveBeenCalled();
    expect(subject.observed).toBeFalse();
  });

  it('should move between the result tabs with the arrow keys and follow with focus', () => {
    open();
    click('.sdg-start-btn');

    const previewTab = query<HTMLButtonElement>('#sdgResult-tab-preview')!;
    previewTab.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    fixture.detectChanges();

    expect(component.resultMode).toBe('markdown');
    expect(query('#sdgResult-tab-markdown')!.getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(query('#sdgResult-tab-markdown'));
  });

  it('should keep the default instructions non-empty', () => {
    expect(DEFAULT_SUITE_DESCRIPTION_INSTRUCTIONS.length).toBeGreaterThan(0);
  });

  it('should confirm before closing an unapplied result, and Discard closes while Cancel keeps it open', () => {
    // The default mock (set in beforeEach) resolves synchronously, so the run is already
    // Completed and unapplied by the time the close button is clicked.
    open();
    click('.sdg-start-btn');

    // The header close button opens the guard rather than closing immediately.
    click('.dialog-header .btn-icon-action');

    const confirm = query<HTMLDialogElement>('dialog.sdg-confirm-dialog')!;
    expect(confirm.open).toBeTrue();
    expect(query<HTMLDialogElement>('dialog.sdg-dialog')!.open).toBeTrue();

    click('dialog.sdg-confirm-dialog .dialog-footer .btn-gh-cancel');
    expect(confirm.open).toBeFalse();
    expect(query<HTMLDialogElement>('dialog.sdg-dialog')!.open).toBeTrue();

    click('.dialog-header .btn-icon-action');
    click('dialog.sdg-confirm-dialog .dialog-footer .btn-gh-delete');

    expect(query<HTMLDialogElement>('dialog.sdg-dialog')!.open).toBeFalse();
  });
});
