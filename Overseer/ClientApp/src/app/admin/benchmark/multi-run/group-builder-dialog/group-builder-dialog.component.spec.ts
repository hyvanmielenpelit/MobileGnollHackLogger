import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkComparabilityResultDto,
  BenchmarkRunGroupDto,
  BenchmarkRunGroupTierPreviewDto,
  BenchmarkRunSummaryDto,
  CreateBenchmarkRunGroupRequest
} from '../../../../services/admin-benchmark.service';
import { RUN_HISTORY_LIMIT } from '../../benchmark.models';
import { GROUP_BUILDER_RUNS_VIEW_STORAGE_KEY, GroupBuilderDialogComponent } from './group-builder-dialog.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

function buildRun(overrides: Partial<BenchmarkRunSummaryDto> = {}): BenchmarkRunSummaryDto {
  return {
    id: 41,
    benchmarkSuiteId: 5,
    suiteName: 'GnollHack Player Assistance Benchmark Suite',
    testedModelConfigurationId: 1,
    testedModelDisplayNameUsed: 'GPT-5.6 Luna',
    testedModelProviderUsed: 'OpenAI',
    testedModelIdUsed: 'gpt-5.6-luna',
    assessorModelConfigurationId: 2,
    assessorModelDisplayNameUsed: 'Claude Opus 5',
    status: 'Completed',
    startedAtUtc: '2026-09-06T08:00:00Z',
    qualityIndex: 71,
    speedIndex: 55,
    totalAnswerDurationMs: 1000,
    speedMeasurementDegraded: false,
    answeredQuestionCount: 18,
    totalQuestionCount: 18,
    unansweredQuestionCount: 0,
    totalDurationMs: 1000,
    candidateSystemPromptSha256: 'bb19dc24aaaa',
    toolGuidesSha256: 'f59d8b30aaaa',
    knowledgeBaseHeadSha: 'cccc2222aaaa',
    wikiHeadSha: 'dddd3333aaaa',
    sourceCodeHeadSha: 'eeee4444aaaa',
    ...overrides
  } as BenchmarkRunSummaryDto;
}

function buildComparability(overrides: Partial<BenchmarkComparabilityResultDto> = {}): BenchmarkComparabilityResultDto {
  return {
    tier: 'Replicate',
    tierLabel: 'Tier A — Replicate',
    poolingPermitted: true,
    speedAggregatesDegraded: false,
    costAggregatesDegraded: false,
    explanation: 'The runs repeat one condition exactly.',
    comparabilityKeyHash: 'a1b2c3d4',
    matchedKeys: ['HarnessVersion', 'ScoringMethodVersion'],
    differences: [],
    runIds: [41, 42],
    ...overrides
  };
}

function acceptedPreview(): BenchmarkRunGroupTierPreviewDto {
  return { accepted: true, error: null, comparability: buildComparability(), group: null, duplicateOfGroupId: null };
}

/** A refusal: the tool guides differ, so the set is below Tier B and cannot be persisted at all. */
function refusedPreview(): BenchmarkRunGroupTierPreviewDto {
  return {
    accepted: false,
    error: 'Refused: the set is below Tier B.',
    group: null,
    comparability: buildComparability({
      tier: 'NotComparable',
      tierLabel: 'Not comparable',
      poolingPermitted: false,
      explanation: 'These runs do not form a comparable set.',
      differences: [{
        name: 'ToolGuidesSha256',
        kind: 'Instrument',
        description: 'The tool guides differ between the runs.',
        variants: [
          { value: 'f59d8b30', runIds: [41, 42] },
          { value: '9c1de220', runIds: [43] }
        ]
      }],
      runIds: [41, 42, 43]
    })
  };
}

/** Tier C as the server previews it without the acknowledgment: refused until crossCondition is set. */
function crossConditionPreview(accepted: boolean): BenchmarkRunGroupTierPreviewDto {
  const refused = refusedPreview();
  return {
    accepted,
    error: accepted ? null : 'These runs differ on exactly one instrument key.',
    group: null,
    comparability: { ...refused.comparability!, tier: 'CrossCondition', tierLabel: 'Tier C — Cross-condition' }
  };
}

function duplicatePreview(): BenchmarkRunGroupTierPreviewDto {
  return {
    accepted: false,
    error: 'Group #12 already holds exactly these runs.',
    duplicateOfGroupId: 12,
    comparability: buildComparability(),
    group: null
  };
}

function buildGroup(id: number): BenchmarkRunGroupDto {
  return {
    id,
    name: 'GnollHack Player Assistance Benchmark Suite · GPT-5.6 Luna · 2026-09-06 · R=2',
    benchmarkSuiteId: 5,
    tier: 'Replicate',
    tierLabel: 'Tier A — Replicate',
    crossCondition: false,
    createdAtUtc: '2026-09-06T10:00:00Z',
    modifiedAtUtc: '2026-09-06T10:00:00Z',
    runCount: 2,
    members: [],
    analysisStale: false,
    memberRunIds: [41, 42],
    headlineIndex: null,
    headlineHalfWidth: null,
    headlineSpeedIndex: null,
    headlineCostPerRun: null,
    nameIsGenerated: true
  } as BenchmarkRunGroupDto;
}

describe('GroupBuilderDialogComponent', () => {
  let fixture: ComponentFixture<GroupBuilderDialogComponent>;
  let component: GroupBuilderDialogComponent;
  let el: HTMLElement;
  let serviceMock: MockedObject<AdminBenchmarkService>;

  beforeEach(async () => {
    try {
      localStorage.removeItem(GROUP_BUILDER_RUNS_VIEW_STORAGE_KEY);
    } catch {
      // Storage unavailable: the default sort applies anyway.
    }

    serviceMock = {
      getRuns: vi.fn().mockName('AdminBenchmarkService.getRuns'),
      previewRunGroupTier: vi.fn().mockName('AdminBenchmarkService.previewRunGroupTier'),
      createRunGroup: vi.fn().mockName('AdminBenchmarkService.createRunGroup')
    } as unknown as MockedObject<AdminBenchmarkService>;
    serviceMock.getRuns.mockReturnValue(of([
      buildRun({ id: 41, startedAtUtc: '2026-09-06T08:00:00Z' }),
      buildRun({ id: 42, startedAtUtc: '2026-09-06T09:00:00Z' }),
      buildRun({ id: 43, startedAtUtc: '2026-09-06T10:00:00Z' })
    ]));
    serviceMock.previewRunGroupTier.mockReturnValue(of(acceptedPreview()));
    serviceMock.createRunGroup.mockReturnValue(of({ accepted: true, error: null, comparability: buildComparability(), group: buildGroup(50) }));

    await TestBed.configureTestingModule({
      imports: [GroupBuilderDialogComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: serviceMock }]
    }).compileComponents();

    fixture = TestBed.createComponent(GroupBuilderDialogComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('suiteId', 5);
    fixture.detectChanges();
  });

  afterEach(() => {
    el.querySelectorAll('dialog').forEach(dialog => {
      if (dialog.open) dialog.close();
    });
    fixture.destroy();
  });

  function open(): void {
    component.open();
    fixture.detectChanges();
  }

  const dialog = (): HTMLDialogElement => el.querySelector<HTMLDialogElement>('dialog.mr-builder-dialog')!;
  const checkbox = (id: number): HTMLInputElement => el.querySelector<HTMLInputElement>(`#mr-run-${id}`)!;
  const createButton = (): HTMLButtonElement => el.querySelector<HTMLButtonElement>('.mr-builder-create')!;
  const footerNote = (): string => textOf(el.querySelector('#mr-builder-footer-note'));
  const cardIds = (): number[] => Array.from(el.querySelectorAll('article.mr-run')).map(card => Number(card.getAttribute('data-run-id')));
  const removeButton = (id: number): HTMLButtonElement =>
    el.querySelector<HTMLButtonElement>(`button[aria-label="Remove run #${id} from the selection"]`)!;

  function select(...ids: number[]): void {
    for (const id of ids) {
      checkbox(id).click();
      fixture.detectChanges();
    }
  }

  function lastCreateRequest(): CreateBenchmarkRunGroupRequest {
    const calls = serviceMock.createRunGroup.mock.calls;
    return calls[calls.length - 1][0] as CreateBenchmarkRunGroupRequest;
  }

  // --- Opening ---

  it('opens with its title focused, loads up to the run history limit of 1000 runs and has no light dismiss', () => {
    open();

    expect(dialog().open).toBe(true);
    const title = el.querySelector('h3#mr-builder-title')!;
    expect(textOf(title)).toBe('New analysis group');
    expect(document.activeElement).toBe(title);
    expect(dialog().getAttribute('aria-labelledby')).toBe('mr-builder-title');
    expect(dialog().hasAttribute('closedby')).toBe(false);
    expect(RUN_HISTORY_LIMIT).toBe(1000);
    expect(serviceMock.getRuns).toHaveBeenCalledWith(5, RUN_HISTORY_LIMIT);
    expect(cardIds()).toEqual([43, 42, 41]);
  });

  it('names each checkbox for the run it selects and makes the card title its label', () => {
    open();

    expect(checkbox(41).getAttribute('aria-label')).toBe('Include run 41 in this group');
    const label = el.querySelector('h5#mr-run-41-title label[for="mr-run-41"]');
    expect(label).not.toBeNull();
    expect(textOf(el.querySelector('article[data-run-id="41"] .mr-run-meta'))).toContain('#41');
    expect(el.querySelector('article[data-run-id="41"]')!.getAttribute('aria-labelledby')).toBe('mr-run-41-title');
  });

  // --- The tier preview ---

  it('does not preview below two runs, and says why Create is unavailable', () => {
    open();
    expect(textOf(el.querySelector('.mr-sel-empty'))).toContain('Nothing is selected yet.');
    expect(footerNote()).toBe('Select at least two runs.');

    select(41);

    expect(serviceMock.previewRunGroupTier).not.toHaveBeenCalled();
    expect(component.preview).toBeNull();
    expect(el.querySelector('.mr-preview')).toBeNull();
    expect(footerNote()).toBe('Select at least two runs.');
    expect(createButton().getAttribute('aria-disabled')).toBe('true');
    expect(createButton().getAttribute('aria-describedby')).toBe('mr-builder-footer-note');

    createButton().click();
    expect(serviceMock.createRunGroup).not.toHaveBeenCalled();
  });

  it('renders which keys differ and on which runs when a set is refused, and names the reason in the footer', () => {
    serviceMock.previewRunGroupTier.mockReturnValue(of(refusedPreview()));
    open();

    select(41, 43);

    expect(serviceMock.previewRunGroupTier).toHaveBeenCalledWith({ runIds: [41, 43], crossCondition: false });
    expect(textOf(el.querySelector('.mr-preview .mr-tier'))).toBe('Not comparable');
    expect(textOf(el.querySelector('.mr-preview-head'))).toContain('Never pooled into one index');
    const differences = textOf(el.querySelector('.mr-differences'));
    expect(differences).toContain('Keys that differ');
    expect(differences).toContain('ToolGuidesSha256');
    expect(differences).toContain('The tool guides differ between the runs.');
    expect(differences).toContain('runs #41, #42');
    expect(differences).toContain('runs #43');
    const matched = el.querySelector<HTMLDetailsElement>('details.mr-matched-keys')!;
    expect(matched.open).toBe(false);
    expect(textOf(matched.querySelector('summary'))).toBe('2 keys matched');
    expect(footerNote()).toBe('These runs cannot form a group: the keys above differ.');
    expect(createButton().getAttribute('aria-disabled')).toBe('true');
  });

  it('offers the cross-condition checkbox only at Tier C, and asks for it before Create', () => {
    serviceMock.previewRunGroupTier.mockReturnValue(of(refusedPreview()));
    open();
    select(41, 43);
    expect(el.querySelector('#mr-cross-condition')).toBeNull();

    serviceMock.previewRunGroupTier.mockReturnValue(of(crossConditionPreview(false)));
    select(42);
    const acknowledge = el.querySelector<HTMLInputElement>('#mr-cross-condition')!;
    expect(acknowledge).not.toBeNull();
    expect(acknowledge.checked).toBe(false);
    expect(footerNote()).toBe('Acknowledge the cross-condition set to create it.');
    expect(createButton().getAttribute('aria-disabled')).toBe('true');

    serviceMock.previewRunGroupTier.mockReturnValue(of(crossConditionPreview(true)));
    acknowledge.click();
    fixture.detectChanges();

    expect(serviceMock.previewRunGroupTier).toHaveBeenLastCalledWith({ runIds: [41, 43, 42], crossCondition: true });
    expect(el.querySelector('#mr-builder-footer-note')).toBeNull();
    expect(createButton().getAttribute('aria-disabled')).toBeNull();
    expect(createButton().hasAttribute('aria-describedby')).toBe(false);

    createButton().click();
    expect(lastCreateRequest().crossCondition).toBe(true);
  });

  it('warns about a set an existing group already holds, and its link emits openGroup', () => {
    serviceMock.previewRunGroupTier.mockReturnValue(of(duplicatePreview()));
    const opened: number[] = [];
    component.openGroup.subscribe(id => opened.push(id));
    open();

    select(41, 42);

    const warning = el.querySelector('.mr-preview .gh-field-warning.mr-duplicate')!;
    expect(textOf(warning)).toContain('Group #12 already holds exactly these runs.');
    const link = warning.querySelector<HTMLButtonElement>('button.btn-link.mr-open-duplicate')!;
    expect(textOf(link)).toBe('Open group #12');
    expect(footerNote()).toBe('Group #12 already holds exactly these runs.');
    expect(createButton().getAttribute('aria-disabled')).toBe('true');
    expect(el.querySelector('.mr-preview-refusal')).toBeNull();

    link.click();
    expect(opened).toEqual([12]);
  });

  it('ignores a preview response that arrives after a newer selection', () => {
    const first = new Subject<BenchmarkRunGroupTierPreviewDto>();
    const second = new Subject<BenchmarkRunGroupTierPreviewDto>();
    serviceMock.previewRunGroupTier.mockReturnValueOnce(first).mockReturnValueOnce(second);
    open();

    select(41, 42, 43);
    second.next(acceptedPreview());
    first.next(refusedPreview());
    fixture.detectChanges();

    expect(textOf(el.querySelector('.mr-preview .mr-tier'))).toBe('Tier A — Replicate');
  });

  // --- Creating ---

  it('creates the group without a name and without a note, then closes and emits it', () => {
    const created: BenchmarkRunGroupDto[] = [];
    component.created.subscribe(group => created.push(group));
    open();

    select(41, 42);
    expect(el.querySelector('#mr-builder-footer-note')).toBeNull();
    createButton().click();

    const request = lastCreateRequest();
    expect(request).toEqual({ runIds: [41, 42], crossCondition: false });
    expect('name' in request).toBe(false);
    expect('notes' in request).toBe(false);
    expect(dialog().open).toBe(false);
    expect(created.map(group => group.id)).toEqual([50]);
  });

  it('sends the note only when one is typed, from a disclosure that starts closed', () => {
    open();
    const disclosure = el.querySelector<HTMLDetailsElement>('details.mr-note-disclosure')!;
    expect(disclosure.open).toBe(false);
    expect(textOf(disclosure.querySelector('summary'))).toBe('Note (optional)');
    expect(textOf(el.querySelector('label[for="mr-builder-note"]'))).toBe('Note');

    disclosure.open = true;
    const note = el.querySelector<HTMLTextAreaElement>('#mr-builder-note')!;
    expect(note.classList).toContain('gh-textarea-autosize');
    note.value = '  Rerun after the knowledge base fix  ';
    note.dispatchEvent(new Event('input'));
    select(41, 42);
    createButton().click();

    expect(lastCreateRequest()).toEqual({ runIds: [41, 42], crossCondition: false, notes: 'Rerun after the knowledge base fix' });
  });

  it('starts a new group on every open: no selection, no note and the note disclosure closed', () => {
    open();
    select(41, 42);
    el.querySelector<HTMLDetailsElement>('details.mr-note-disclosure')!.open = true;
    component.close();

    open();

    expect(component.selectedRunIds).toEqual([]);
    expect(component.notes).toBe('');
    expect(el.querySelector<HTMLDetailsElement>('details.mr-note-disclosure')!.open).toBe(false);
    expect(el.querySelector('.mr-preview')).toBeNull();
  });

  it('shows a refusal that arrives as a 400 body with the duplicate warning, and keeps the dialog open', () => {
    const created: BenchmarkRunGroupDto[] = [];
    component.created.subscribe(group => created.push(group));
    serviceMock.createRunGroup.mockReturnValue(throwError(() => ({ status: 400, error: duplicatePreview() })));
    open();

    select(41, 42);
    createButton().click();
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(created).toEqual([]);
    expect(textOf(el.querySelector('.mr-duplicate'))).toContain('Group #12 already holds exactly these runs.');
    expect(el.querySelector('.mr-create-error')).toBeNull();
    expect(footerNote()).toBe('Group #12 already holds exactly these runs.');
  });

  it('shows a refusal that arrives as data with its error and the keys that differ', () => {
    serviceMock.createRunGroup.mockReturnValue(of(refusedPreview()));
    open();

    select(41, 43);
    createButton().click();
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(textOf(el.querySelector('.mr-create-error'))).toBe('Refused: the set is below Tier B.');
    expect(textOf(el.querySelector('.mr-differences'))).toContain('ToolGuidesSha256');
    expect(footerNote()).toBe('These runs cannot form a group: the keys above differ.');
  });

  // --- Selection ---

  it('keeps the selection by id across batches and filters, and passes it to create', () => {
    const many = Array.from({ length: 25 }, (_, i) => buildRun({
      id: i + 1,
      testedModelDisplayNameUsed: (i + 1) % 2 === 0 ? 'Model B' : 'Model A',
      startedAtUtc: `2026-09-${String(i + 1).padStart(2, '0')}T08:00:00Z`
    }));
    serviceMock.getRuns.mockReturnValue(of(many));
    open();

    // Newest first, twenty at a time: run 25 leads and run 5 is beyond the batch.
    expect(cardIds().length).toBe(20);
    expect(cardIds()[0]).toBe(25);
    select(25);

    el.querySelector<HTMLButtonElement>('.mr-runs-show-more')!.click();
    fixture.detectChanges();
    expect(cardIds().length).toBe(25);
    expect(document.activeElement?.id).toBe('mr-run-5-title');
    select(3);

    component.onFacetChange('model', ['Model B']);
    fixture.detectChanges();
    expect(cardIds().every(id => id % 2 === 0)).toBe(true);
    expect(component.selectedRunIds).toEqual([25, 3]);
    expect(textOf(el.querySelector('#mr-runs-status'))).toContain('2 selected, 2 not shown');
    expect(Array.from(el.querySelectorAll('.mr-sel-chip-id')).map(chip => textOf(chip))).toEqual(['#3', '#25']);

    createButton().click();
    expect(lastCreateRequest().runIds).toEqual([25, 3]);
  });

  it('shows Show selected only as an aria-pressed toggle that filters to the selection', () => {
    open();
    expect(el.querySelector('.mr-runs-selected-only')).toBeNull();

    select(41);
    const toggle = el.querySelector<HTMLButtonElement>('button.gh-filter-toggle.mr-runs-selected-only')!;
    expect(textOf(toggle)).toBe('Show selected only');
    expect(toggle.getAttribute('aria-pressed')).toBe('false');

    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    expect(cardIds()).toEqual([41]);
    expect(el.querySelector('.mr-runs-chips')).toBeNull();

    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-pressed')).toBe('false');
    expect(cardIds()).toEqual([43, 42, 41]);
  });

  it('labels the band "3 runs selected" with a hidden prefix, and Clear selection moves focus to that label', () => {
    open();
    select(41, 42, 43);

    const label = el.querySelector<HTMLElement>('#mr-builder-selection-label')!;
    expect(textOf(label)).toBe('Your selection — 3 runs selected');
    expect(label.getAttribute('role')).toBe('status');
    expect(label.getAttribute('tabindex')).toBe('-1');
    expect(el.querySelector('.mr-sel-band')!.getAttribute('aria-labelledby')).toBe('mr-builder-selection-label');
    const clear = el.querySelector<HTMLButtonElement>('#mr-builder-clear-selection')!;
    expect(textOf(clear)).toBe('Clear selection (3)');

    clear.click();
    fixture.detectChanges();

    expect(component.selectedRunIds).toEqual([]);
    expect(document.activeElement).toBe(label);
    expect(textOf(label)).toBe('Your selection — nothing selected yet');
    expect(el.querySelector('#mr-builder-clear-selection')).toBeNull();
  });

  it('moves focus after a chip is removed to the next chip, else Clear selection, else the label', () => {
    open();
    select(41, 42, 43);

    removeButton(41).click();
    fixture.detectChanges();
    expect(document.activeElement).toBe(removeButton(42));

    removeButton(43).click();
    fixture.detectChanges();
    expect(document.activeElement?.id).toBe('mr-builder-clear-selection');

    removeButton(42).click();
    fixture.detectChanges();
    expect(document.activeElement?.id).toBe('mr-builder-selection-label');
    expect(component.selectedRunIds).toEqual([]);
  });

  // --- The cards ---

  it('refuses a run that has not settled: an aria-disabled checkbox, a refused click and a visible reason', () => {
    serviceMock.getRuns.mockReturnValue(of([
      buildRun({ id: 44, status: 'Running', startedAtUtc: '2026-09-07T08:00:00Z' }),
      buildRun({ id: 45, status: 4, startedAtUtc: '2026-09-06T08:00:00Z' }),
      buildRun({ id: 46, status: 'CompletedWithLimits', startedAtUtc: '2026-09-05T08:00:00Z' })
    ]));
    open();

    const running = checkbox(44);
    expect(running.getAttribute('aria-disabled')).toBe('true');
    expect(running.getAttribute('aria-describedby')).toBe('mr-run-44-reason');
    expect(running.disabled).toBe(false);
    expect(textOf(el.querySelector('#mr-run-44-reason'))).toBe('Run #44 is still in progress. Only a finished run can join a group.');
    expect(textOf(el.querySelector('#mr-run-45-reason'))).toBe('Run #45 ended Failed and produced no measurement, so it cannot join a group.');
    expect(el.querySelector('article[data-run-id="44"]')!.classList).toContain('is-unsettled');

    running.click();
    fixture.detectChanges();
    expect(running.checked).toBe(false);
    expect(component.selectedRunIds).toEqual([]);

    expect(checkbox(46).hasAttribute('aria-disabled')).toBe(false);
    expect(el.querySelector('#mr-run-46-reason')).toBeNull();
  });

  it('shows the five fingerprints as buttons with interest tooltips, Advisory timing as a word, and no title attribute', () => {
    serviceMock.getRuns.mockReturnValue(of([buildRun({ id: 41, speedMeasurementDegraded: true })]));
    open();

    const buttons = Array.from(el.querySelectorAll<HTMLButtonElement>('article[data-run-id="41"] button.mr-fp'));
    expect(buttons.map(button => textOf(button))).toEqual([
      'PROMPT (candidate system prompt) bb19dc24',
      'GUIDES (tool guides) f59d8b30',
      'KB (knowledge base) cccc2222',
      'WIKI (wiki) dddd3333',
      'SRC (source code) eeee4444'
    ]);
    const tipId = buttons[0].getAttribute('interestfor')!;
    const tip = el.querySelector(`#${tipId}`)!;
    expect(tip.getAttribute('popover')).toBe('hint');
    expect(textOf(tip)).toBe('Candidate system prompt SHA-256: bb19dc24aaaa');
    expect(textOf(el.querySelector('article[data-run-id="41"] [data-metric="speed"] .mr-run-metric-note'))).toBe('Advisory timing');
    expect(el.querySelectorAll('[title]').length).toBe(0);
  });

  // --- Closing ---

  it('closes on Escape, from the close button and from Cancel', async () => {
    open();

    // requestClose() is the path Escape takes: cancel, then close. A synthetic keydown would not close it.
    const native = dialog() as HTMLDialogElement & { requestClose?: () => void };
    const closed = new Promise<void>(resolve => native.addEventListener('close', () => resolve(), { once: true }));
    if (typeof native.requestClose === 'function') native.requestClose();
    else native.close();
    await closed;
    expect(dialog().open).toBe(false);

    open();
    const closeButton = el.querySelector<HTMLButtonElement>('.mr-builder-close')!;
    expect(closeButton.classList).toContain('btn-icon-action');
    expect(closeButton.getAttribute('aria-label')).toBe('Close');
    closeButton.click();
    await settle();
    expect(dialog().open).toBe(false);

    open();
    const cancel = el.querySelector<HTMLButtonElement>('.mr-builder-cancel')!;
    expect(cancel.classList).toContain('btn-gh-cancel');
    cancel.click();
    await settle();
    expect(dialog().open).toBe(false);
  });

  it('clears the search on Escape with text and keeps the dialog open', async () => {
    open();
    const search = el.querySelector<HTMLInputElement>('#mr-runs-search')!;
    search.value = '#42';
    search.dispatchEvent(new Event('input'));
    await settle(250);
    fixture.detectChanges();
    expect(cardIds()).toEqual([42]);

    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    search.dispatchEvent(escape);
    fixture.detectChanges();

    expect(escape.defaultPrevented).toBe(true);
    expect(cardIds()).toEqual([43, 42, 41]);
    expect(dialog().open).toBe(true);
  });
});
