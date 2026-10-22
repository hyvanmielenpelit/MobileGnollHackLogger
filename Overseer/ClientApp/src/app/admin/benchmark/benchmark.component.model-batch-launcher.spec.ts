import type { Mock, MockedObject } from 'vitest';
import { ComponentFixture, discardPeriodicTasks, fakeAsync, flush, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import {
  AdminBenchmarkService,
  BenchmarkModelBatchFindingDto,
  BenchmarkModelBatchLimitsDto,
  BenchmarkModelBatchPreflightResponse,
  BenchmarkModelBatchProjectionDto,
  BenchmarkModelBatchRunDto
} from '../../services/admin-benchmark.service';
import { BenchmarkCompletionSoundService } from '../../services/benchmark-completion-sound.service';
import { MODEL_BATCH_PREFLIGHT_DEBOUNCE_MS } from './state/benchmark-launcher.state';
import {
  AdminBenchmarkSpecContext, buildBenchmarkConfig, clearStoredState, createAdminBenchmarkFixture, RUN_SETTINGS_KEY
} from './benchmark.component.testing';

const BLOCKER: BenchmarkModelBatchFindingDto = {
  code: 'MB-B03', name: 'GraderIsCandidate', severity: 'Blocker', field: 'assessor', title: 'Test Model cannot grade itself',
  detail: 'Choose another assessor: a scoring grader must not be a model under test.', modelConfigurationIds: [1]
};

const WARNING: BenchmarkModelBatchFindingDto = {
  code: 'MB-W01', name: 'MixedFamiliesSingleAssessor', severity: 'Warning', field: 'coAssessor', title: 'Use a two-family panel for this batch',
  detail: 'A single assessor favors its own provider\'s models over the others.',
  modelConfigurationIds: [2, 3], acknowledgmentKey: 'MB-W01:2,3'
};

const SAME_PROVIDER: BenchmarkModelBatchFindingDto = {
  code: 'MB-W10', name: 'SameProviderAssessor', severity: 'Warning', field: 'assessor', title: 'Test Model grades its own provider\'s models',
  detail: 'Fourth Model is graded by a same-provider assessor.',
  modelConfigurationIds: [4], acknowledgmentKey: 'MB-W10:4',
  modelNotes: [{ modelConfigurationId: 4, text: 'Same provider as the assessor' }]
};

const SETTINGS_DIFFER: BenchmarkModelBatchFindingDto = {
  code: 'MB-W05', name: 'CandidateSettingsDiffer', severity: 'Warning', field: 'models', title: 'Service tier differs between models',
  detail: 'Fourth Model (priority); the other 2 use default.',
  modelConfigurationIds: [4], acknowledgmentKey: 'MB-W05/ServiceTier:4',
  modelNotes: [{ modelConfigurationId: 4, text: 'Service tier priority; the others use default' }]
};

const DUPLICATE: BenchmarkModelBatchFindingDto = {
  code: 'MB-W06', name: 'DuplicateConfiguration', severity: 'Warning', field: 'models', title: 'Second Model is selected twice',
  detail: 'For repeats, use Runs per model instead.',
  modelConfigurationIds: [2, 3], acknowledgmentKey: 'MB-W06:2,3',
  modelNotes: [
    { modelConfigurationId: 2, text: 'Same configuration as Third Model' },
    { modelConfigurationId: 3, text: 'Same configuration as Second Model' }
  ]
};

const REFUSED: BenchmarkModelBatchFindingDto = {
  code: 'MB-B06', name: 'MemberRefused', severity: 'Blocker', field: 'models', title: 'Fourth Model is refused',
  detail: 'Fourth Model: its custom endpoint is not allowed by the endpoint policy.', modelConfigurationIds: [4],
  modelNotes: [{ modelConfigurationId: 4, text: 'Refused: see Batch Readiness' }]
};

const TOO_FEW: BenchmarkModelBatchFindingDto = {
  code: 'MB-B01', name: 'TooFewModels', severity: 'Blocker', field: 'models', title: 'Choose at least two models',
  detail: 'A model batch compares two or more models.', modelConfigurationIds: []
};

const TOO_MANY: BenchmarkModelBatchFindingDto = {
  code: 'MB-B02', name: 'TooManyModels', severity: 'Blocker', field: 'models', title: 'At most 12 models in one batch',
  detail: 'Remove models until 12 or fewer remain.', modelConfigurationIds: []
};

const ADVICE: BenchmarkModelBatchFindingDto = {
  code: 'MB-A01', name: 'SingleRunPerModel', severity: 'Advice', field: 'runsPerModel', title: 'One run per model',
  detail: 'Differences under about 2 index points are noise; use 2–3 runs to rank.', modelConfigurationIds: []
};

function limits(overrides: Partial<BenchmarkModelBatchLimitsDto> = {}): BenchmarkModelBatchLimitsDto {
  return {
    maxRunsPerDay: 20, maxRunsPerHour: 4, runsInLast24Hours: 3, runsInLastHour: 1, remainingDailyHeadroom: 17,
    daySpan: null, minimumWallMs: null, projectedRunsPerHour: 1, memberPlanRuns: null, maxBatteryMembers: 120,
    spendAllowedNow: true, spendDenialReason: null, spendDenialIsCap: false,
    ...overrides
  };
}

function projection(overrides: Partial<BenchmarkModelBatchProjectionDto> = {}): BenchmarkModelBatchProjectionDto {
  return {
    plannedRunCount: 2, projectedCostUsd: 1.5, projectedWallMs: 2 * 3600 * 1000,
    limits: limits(),
    members: [
      { modelConfigurationId: 2, plannedRunCount: 1, projectedCostUsd: 0.5, projectedWallMs: 3600 * 1000, basis: 'OwnRuns' },
      { modelConfigurationId: 3, plannedRunCount: 1, projectedCostUsd: 1, projectedWallMs: 3600 * 1000, basis: 'TargetMean' }
    ],
    ...overrides
  };
}

function batchRun(overrides: Partial<BenchmarkModelBatchRunDto> = {}): BenchmarkModelBatchRunDto {
  return {
    id: 21, status: 'Running', targetKind: 'Suite', suiteId: 1, targetName: 'Default Suite', suiteNames: ['Default Suite'],
    runsPerModel: 1, order: 'Randomized', orderSeed: 4711, allowCapWait: false, createdAtUtc: '2026-10-10T08:00:00Z',
    members: [], currentMemberIndex: 0, requestedMemberCount: 2, completedMemberCount: 0, failedMemberCount: 0,
    skippedMemberCount: 0, acknowledgedFindings: [], adviceAtStart: [], instrumentChangeAcknowledged: false,
    isDriving: true, resumable: false, resumeOptions: [], stalled: false, stallMinutes: 15,
    ...overrides
  };
}

describe('AdminBenchmarkComponent: model batch launcher', () => {
  let ctx: AdminBenchmarkSpecContext;
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;
  let preflightResponse: BenchmarkModelBatchPreflightResponse;
  let preflight: Mock;
  let startModelBatch: Mock;

  const card = (): HTMLElement => fixture.nativeElement.querySelector('.setup-card') as HTMLElement;
  const query = <T extends Element = HTMLElement>(selector: string): T | null =>
    fixture.nativeElement.querySelector(selector) as T | null;
  const text = (node: Element | null): string => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();

  beforeEach(clearStoredState);

  afterEach(clearStoredState);

  beforeEach(async () => {
    ctx = await createAdminBenchmarkFixture();
    ({ component, fixture, benchmarkServiceMock } = ctx);

    preflightResponse = { findings: [], projection: projection(), maxModels: 12 };
    preflight = vi.fn(() => of(preflightResponse));
    startModelBatch = vi.fn(() => of(batchRun()));
    Object.assign(benchmarkServiceMock, { preflightModelBatch: preflight, startModelBatch });

    // Configuration 1 (Anthropic) is the assessor; 2 to 4 are candidates of three providers, each
    // with what the picker's badges draw.
    const base = component.systemConfigs[0];
    const badged = {
      thinkingLevel: 'high', effectiveInputPricePerMillion: 5, effectiveOutputPricePerMillion: 25, parallelExecutionMode: 0
    };
    component.systemConfigs = [
      base,
      buildBenchmarkConfig(base, { id: 2, displayName: 'Second Model', provider: 'OpenAI', modelId: 'gpt-test', ...badged }),
      buildBenchmarkConfig(base, { id: 3, displayName: 'Third Model', provider: 'Google', modelId: 'gemini-test', ...badged }),
      buildBenchmarkConfig(base, { id: 4, displayName: 'Fourth Model', provider: 'Anthropic', modelId: 'claude-other', ...badged })
    ];
    ctx.launcher.setDefaultModelSelections();
    component.activeSubTab = 'run';
    ctx.refresh();
  });

  afterEach(() => fixture.destroy());

  /** Clicks Model batch and lets the first check answer. */
  function chooseBatch(): void {
    query<HTMLInputElement>('#runModeBatch')!.click();
    fixture.detectChanges();
  }

  /** Chooses the candidates as the picker would, then lets the debounced check answer. */
  function chooseModels(keys: number[]): void {
    ctx.runTab().onBatchModelsChange(keys);
    settle();
  }

  function settle(): void {
    tick(MODEL_BATCH_PREFLIGHT_DEBOUNCE_MS);
    ctx.refresh();
  }

  function finish(): void {
    discardPeriodicTasks();
    flush();
  }

  const modelsStatus = '.batch-models-picker .gh-multi-picker-status';

  const startButton = (): HTMLButtonElement => query<HTMLButtonElement>('.model-batch-actions .start-model-batch')!;

  describe('Run Mode', () => {
    it('puts the Models radios first in the primary field, One model chosen', () => {
      const primary = card().querySelector('.setup-primary-field') as HTMLElement;
      const group = primary.querySelector('fieldset.run-mode-choice[role="radiogroup"]') as HTMLFieldSetElement;
      expect(group).toBeTruthy();
      expect(primary.firstElementChild).toBe(group);
      expect(text(query(`#${group.getAttribute('aria-labelledby')}`))).toBe('Models');
      const radios = Array.from(group.querySelectorAll('input[type="radio"]')) as HTMLInputElement[];
      expect(radios.map(r => text(r.closest('label')))).toEqual(['One model', 'Model batch']);
      expect(radios[0].checked).toBe(true);
      expect(query('.tested-model-selector')).toBeTruthy();
      expect(query('.batch-models-picker')).toBeNull();
      expect(text(card().querySelector('.form-actions .btn-gh'))).toBe('Start Benchmark');
    });

    it('swaps in the Models Under Test picker, Start Model Batch and a report writer of None', fakeAsync(() => {
      ctx.launcher.reportWriterConfigId = 3;

      chooseBatch();
      settle();

      expect(ctx.launcher.runMode).toBe('batch');
      expect(ctx.launcher.reportWriterConfigId).toBeNull();
      expect(query('.tested-model-selector')).toBeNull();
      const picker = query('.batch-models-picker')!;
      expect(picker).toBeTruthy();
      expect(text(query('#bmBatchModelsLabel'))).toBe('Models Under Test');
      expect(text(query('#bmBatchModelsHint'))).toBe('Choose two or more models.');
      expect(text(startButton())).toBe('Start Model Batch');
      expect(startButton().getAttribute('aria-disabled')).toBe('true');
      expect(text(query('#startModelBatchHint'))).toBe('Choose two or more models.');
      // Before a model is chosen the card waits.
      expect(query('app-model-batch-readiness')).toBeNull();
      expect(JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!).runMode).toBe('batch');
      finish();
    }));

    it('offers a scoring grader and the report writer as unavailable candidates, with the reason', fakeAsync(() => {
      chooseBatch();
      ctx.launcher.reportWriterConfigId = 3;
      ctx.refresh();

      const options = ctx.runTab().batchPickerOptions;
      expect(options.find(o => o.key === 1)!.disabledReason).toBe('Grades this batch');
      expect(options.find(o => o.key === 3)!.disabledReason).toBe('Writes its reports');
      expect(options.find(o => o.key === 2)!.disabledReason).toBeUndefined();
      // Memoized: the same array while nothing it depends on changes.
      expect(ctx.runTab().batchPickerOptions).toBe(options);
      finish();
    }));

    it('replaces Number of Runs with Runs per model and shows the Model order radios and Batch Options', fakeAsync(() => {
      chooseBatch();
      chooseModels([2, 3]);

      expect(query('#runCountInput')).toBeNull();
      expect(text(card().querySelector('label[for="batchRunsPerModelInput"]'))).toBe('Runs per model');
      const order = card().querySelector('fieldset.batch-order-choice') as HTMLElement;
      expect(Array.from(order.querySelectorAll('input[type="radio"]')).map(r => text(r.closest('label'))))
        .toEqual(['Randomized (recommended)', 'As listed']);
      expect(query<HTMLInputElement>('#batchOrderRandomized')!.checked).toBe(true);
      expect(query('app-reorderable-list')).toBeNull();
      expect(text(query('#execOptionsCaption'))).toBe('Batch Options');
      expect(query('#allowCapWaitInput')).toBeTruthy();

      query<HTMLInputElement>('#batchOrderAsListed')!.click();
      fixture.detectChanges();

      expect(ctx.launcher.batchOrder).toBe('asListed');
      const list = query('app-reorderable-list.batch-order-list')!;
      expect(list).toBeTruthy();
      expect(list.textContent).toContain('Second Model');
      expect(list.textContent).toContain('Third Model');
      finish();
    }));

    it('names Reuse earlier runs as unavailable for a battery batch', fakeAsync(() => {
      chooseBatch();
      ctx.launcher.runTargetKind = 'battery';
      ctx.refresh();

      expect(query('#reuseEarlierRunsInput')).toBeNull();
      expect(text(query('.batch-reuse-note'))).toBe('Reuse earlier runs: not available for a model batch.');
      finish();
    }));
  });

  describe('guardrails', () => {
    it('shows each field\'s blocker or warning as one line it is described by, and the card above Start', fakeAsync(() => {
      preflightResponse = { findings: [BLOCKER, WARNING, ADVICE], projection: projection(), maxModels: 12 };
      chooseBatch();
      chooseModels([2, 3]);

      const assessorLine = query('#mbLine-assessor')!;
      expect(assessorLine.classList).toContain('gh-field-error');
      expect(text(assessorLine)).toBe(BLOCKER.title);
      const assessorTrigger = query('.assessor-model-selector .selector-trigger')!;
      expect(assessorTrigger.getAttribute('aria-describedby')!.split(' ')).toContain('mbLine-assessor');

      const coAssessorLine = query('#mbLine-coAssessor')!;
      expect(coAssessorLine.classList).toContain('gh-field-warning');
      expect(text(coAssessorLine)).toBe(WARNING.title);

      // Advice is never inline.
      expect(query('#mbLine-runs')).toBeNull();

      const readiness = query('app-model-batch-readiness')!;
      expect(readiness).toBeTruthy();
      expect(readiness.closest('.model-batch-actions')).toBeNull();
      expect(readiness.nextElementSibling).toBe(query('.model-batch-actions'));
      expect(readiness.compareDocumentPosition(startButton()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(text(readiness.querySelector('.gh-readiness-status'))).toBe('1 blocking');
      expect(startButton().getAttribute('aria-disabled')).toBe('true');
      expect(text(query('#startModelBatchHint'))).toBe(`${BLOCKER.title}.`);
      finish();
    }));

    it('holds Start until the warning is acknowledged in the card', fakeAsync(() => {
      preflightResponse = { findings: [WARNING, ADVICE], projection: projection(), maxModels: 12 };
      chooseBatch();
      chooseModels([2, 3]);

      expect(startButton().getAttribute('aria-disabled')).toBe('true');
      expect(text(query('#startModelBatchHint'))).toBe('Acknowledge the warning in Batch Readiness.');

      query<HTMLInputElement>('app-model-batch-readiness .gh-readiness-item[data-severity="warning"] input[type="checkbox"]')!.click();
      fixture.detectChanges();

      expect(ctx.launcher.acknowledgedFindingKeys.has('MB-W01:2,3')).toBe(true);
      expect(startButton().getAttribute('aria-disabled')).toBe('false');
      expect(query('#startModelBatchHint')).toBeNull();
      finish();
    }));

    it('notes a warning on the chip of the candidate it names', fakeAsync(() => {
      preflightResponse = { findings: [SAME_PROVIDER], projection: projection(), maxModels: 12 };
      chooseBatch();
      chooseModels([2, 4]);

      const options = ctx.runTab().batchPickerOptions;
      expect(options.find(o => o.key === 4)!.notes).toEqual([{ text: 'Same provider as the assessor', tone: 'warning' }]);
      expect(options.find(o => o.key === 2)!.notes).toBeUndefined();
      expect(options.find(o => o.key === 2)!.detail).toBeUndefined();
      finish();
    }));

    it('notes a setting difference only on the model that deviates, in the server\'s words', fakeAsync(() => {
      preflightResponse = { findings: [SETTINGS_DIFFER], projection: projection(), maxModels: 12 };
      chooseBatch();
      chooseModels([2, 3, 4]);

      const cards = Array.from(fixture.nativeElement.querySelectorAll('.batch-models-picker .gh-multi-picker-chip')) as HTMLElement[];
      expect(cards.length).toBe(3);
      expect(cards[0].querySelector('.gh-multi-picker-chip-note')).toBeNull();
      expect(cards[1].querySelector('.gh-multi-picker-chip-note')).toBeNull();
      const notes = Array.from(cards[2].querySelectorAll('.gh-multi-picker-chip-notes > li.gh-multi-picker-chip-note.is-warning'));
      expect(notes.map(n => text(n))).toEqual(['Warning: Service tier priority; the others use default']);
      finish();
    }));

    it('notes a blocker on its model\'s card in the error tone, before its warnings', fakeAsync(() => {
      preflightResponse = { findings: [REFUSED, SAME_PROVIDER], projection: projection(), maxModels: 12 };
      chooseBatch();
      chooseModels([2, 4]);

      const cards = Array.from(fixture.nativeElement.querySelectorAll('.batch-models-picker .gh-multi-picker-chip')) as HTMLElement[];
      const items = Array.from(cards[1].querySelectorAll('.gh-multi-picker-chip-notes > li')) as HTMLElement[];
      expect(items.map(i => text(i))).toEqual(['Blocking: Refused: see Batch Readiness', 'Warning: Same provider as the assessor']);
      expect(items[0].classList).toContain('is-error');
      expect(items[0].querySelector('svg circle')).not.toBeNull();
      expect(items[1].classList).toContain('is-warning');
      finish();
    }));

    it('names the remove button by the model alone, and describes it by the card\'s notes', fakeAsync(() => {
      preflightResponse = { findings: [SAME_PROVIDER], projection: projection(), maxModels: 12 };
      chooseBatch();
      chooseModels([2, 4]);

      const cards = Array.from(fixture.nativeElement.querySelectorAll('.batch-models-picker .gh-multi-picker-chip')) as HTMLElement[];
      const remove = cards[1].querySelector('.action-btn')!;
      expect(remove.getAttribute('aria-label')).toBe('Remove Fourth Model');
      const description = query(`#${remove.getAttribute('aria-describedby')}`)!;
      expect(description.matches('ul.gh-multi-picker-chip-notes')).toBe(true);
      expect(cards[1].contains(description)).toBe(true);
      const plain = cards[0].querySelector('.action-btn')!;
      expect(plain.getAttribute('aria-label')).toBe('Remove Second Model');
      expect(plain.hasAttribute('aria-describedby')).toBe(false);
      finish();
    }));

    it('leaves the models line to the cards when they show the finding', fakeAsync(() => {
      preflightResponse = { findings: [SETTINGS_DIFFER, DUPLICATE], projection: projection(), maxModels: 12 };
      chooseBatch();
      chooseModels([2, 3, 4]);

      expect(query('#mbLine-models')).toBeNull();
      const status = query(modelsStatus)!;
      const describedBy = query('.batch-models-picker .selector-trigger')!.getAttribute('aria-describedby')!.split(' ');
      expect(describedBy).not.toContain('mbLine-models');
      expect(describedBy).toContain(status.id);
      expect(text(status)).toBe('Warnings on 3 models');
      finish();
    }));

    it('keeps the models line for a finding no card shows', fakeAsync(() => {
      preflightResponse = { findings: [TOO_MANY, SETTINGS_DIFFER], projection: projection(), maxModels: 12 };
      chooseBatch();
      chooseModels([2, 3, 4]);

      const line = query('#mbLine-models')!;
      expect(text(line)).toBe(TOO_MANY.title);
      // The card-covered warning is not counted.
      expect(line.querySelector('.mb-field-line-more')).toBeNull();
      // A models blocker never suppresses a warning count.
      expect(text(query(modelsStatus))).toBe('Warnings on 1 model');
      finish();
    }));

    it('names the reader and verifier role of a model not yet chosen, and follows a change of reader', fakeAsync(() => {
      chooseBatch();
      ctx.launcher.coAssessorConfigId = null;
      ctx.launcher.secondOpinionConfigId = 3;
      ctx.launcher.claimVerifierConfigId = 3;
      ctx.refresh();

      const options = ctx.runTab().batchPickerOptions;
      expect(options.find(o => o.key === 3)!.detail).toBe('Second reader · claim verifier');
      expect(options.find(o => o.key === 2)!.detail).toBeUndefined();

      ctx.launcher.secondOpinionConfigId = 2;
      ctx.refresh();

      const changed = ctx.runTab().batchPickerOptions;
      expect(changed).not.toBe(options);
      expect(changed.find(o => o.key === 3)!.detail).toBe('Claim verifier');
      expect(changed.find(o => o.key === 2)!.detail).toBe('Second reader');
      finish();
    }));

    it('draws the chosen models as glass cards with their badges, the warning on the one it names', fakeAsync(() => {
      preflightResponse = { findings: [SAME_PROVIDER], projection: projection(), maxModels: 12 };
      chooseBatch();
      chooseModels([2, 4]);

      expect(query('.batch-models-picker .gh-multi-picker')!.classList).toContain('gh-multi-picker--cards');
      const cards = Array.from(fixture.nativeElement.querySelectorAll('.batch-models-picker .gh-multi-picker-chip')) as HTMLElement[];
      expect(cards.length).toBe(2);
      for (const chip of cards) {
        for (const badge of ['.thinking-badge', '.provider-badge', '.price-badge', '.parallel-badge']) {
          expect(chip.querySelector(badge), badge).not.toBeNull();
        }
      }
      expect(text(cards[0].querySelector('.gh-multi-picker-chip-label'))).toBe('Second Model');
      expect(cards[0].querySelector('app-provider-badge')!.classList).toContain('provider-badge--openai');
      expect(cards[0].querySelector('.gh-multi-picker-chip-note')).toBeNull();
      const note = cards[1].querySelector('.gh-multi-picker-chip-note.is-warning');
      expect(text(note)).toBe('Warning: Same provider as the assessor');
      expect(cards[1].matches(':has(.gh-multi-picker-chip-note.is-warning)')).toBe(true);
      finish();
    }));

    it('focuses the field when Go to field is pressed', fakeAsync(() => {
      preflightResponse = { findings: [BLOCKER], projection: projection(), maxModels: 12 };
      chooseBatch();
      chooseModels([2, 3]);

      query<HTMLButtonElement>('app-model-batch-readiness .gh-readiness-go')!.click();

      expect(document.activeElement).toBe(query('.assessor-model-selector .selector-trigger'));
      finish();
    }));
  });

  describe('Models Under Test status', () => {
    const modelsTrigger = () => query('.batch-models-picker .selector-trigger')!;
    const cardList = () => query('.batch-models-picker ul.gh-multi-picker-chips')!;

    it('shows that the models are being checked, then what the check found', fakeAsync(() => {
      chooseBatch();
      preflightResponse = { findings: [SAME_PROVIDER], projection: projection(), maxModels: 12 };
      ctx.runTab().onBatchModelsChange([2, 4]);
      ctx.refresh();

      let status = query(modelsStatus)!;
      expect(status.getAttribute('data-tone')).toBe('busy');
      expect(text(status)).toBe('Checking the models for warnings…');
      expect(cardList().hasAttribute('data-checking')).toBe(true);

      settle();

      status = query(modelsStatus)!;
      expect(status.getAttribute('data-tone')).toBe('warning');
      expect(text(status)).toBe('Warnings on 1 model');
      expect(cardList().hasAttribute('data-checking')).toBe(false);
      expect(modelsTrigger().getAttribute('aria-describedby')).toBe(`bmBatchModelsHint ${status.id}`);
      finish();
    }));

    it('says when the chosen models have no warnings', fakeAsync(() => {
      chooseBatch();
      chooseModels([2, 3]);

      const status = query(modelsStatus)!;
      expect(status.getAttribute('data-tone')).toBe('ok');
      expect(text(status)).toBe('No warnings on these models');
      finish();
    }));

    it('counts blocked and warned models separately', fakeAsync(() => {
      preflightResponse = { findings: [REFUSED, SETTINGS_DIFFER, DUPLICATE], projection: projection(), maxModels: 12 };
      chooseBatch();
      chooseModels([2, 3, 4]);

      const status = query(modelsStatus)!;
      expect(status.getAttribute('data-tone')).toBe('error');
      expect(text(status)).toBe('1 model blocked · warnings on 2 models');
      finish();
    }));

    it('says when the check failed', fakeAsync(() => {
      preflight.mockReturnValueOnce(throwError(() => new Error('x')));
      chooseBatch();
      chooseModels([2, 3]);

      const status = query(modelsStatus)!;
      expect(status.getAttribute('data-tone')).toBe('error');
      expect(text(status)).toBe('Could not check the models; trying again');
      // Runs the retry: it asks again and the default answer settles.
      finish();
    }));

    it('shows nothing for one chosen model, whose blocker is the line under the picker', fakeAsync(() => {
      preflightResponse = { findings: [TOO_FEW], projection: projection(), maxModels: 12 };
      chooseBatch();
      chooseModels([2]);

      expect(query(modelsStatus)).toBeNull();
      expect(text(query('#mbLine-models'))).toBe(TOO_FEW.title);
      expect(modelsTrigger().getAttribute('aria-describedby')).toBe('bmBatchModelsHint mbLine-models');
      finish();
    }));
  });

  describe('Batch Projection', () => {
    it('shows the plan with its runs, wall time, cost, headroom and the per-model basis', fakeAsync(() => {
      preflightResponse = { findings: [], projection: projection({ plannedRunCount: 4 }), maxModels: 12 };
      chooseBatch();
      ctx.launcher.batchRunsPerModel = 2;
      chooseModels([2, 3]);

      const block = query('fieldset.batch-projection')!;
      expect(text(block.querySelector('legend'))).toBe('Batch Projection');
      const planned = block.querySelector('.projection-grid > .batch-planned-runs-row')!;
      expect(planned).toBe(block.querySelector('.projection-grid')!.firstElementChild);
      expect(text(planned.querySelector('.projection-label'))).toBe('Planned runs:');
      expect(text(planned.querySelector('.projection-value'))).toBe('4');
      expect(text(planned.querySelector('.projection-note'))).toBe('(2 models × 2 runs per model)');
      const rows = text(block.querySelector('.projection-grid'));
      expect(rows).toContain('Projected wall time:');
      expect(rows).toContain('$1.5000');
      expect(rows).toContain('17 of 20');
      expect(rows).toContain('both rolling windows, not calendar days');
      expect(block.querySelector('.batch-day-span-row')).toBeNull();
      const basis = block.querySelector('details.batch-projection-basis') as HTMLDetailsElement;
      expect(basis.open).toBe(false);
      expect(text(basis)).toContain('Second Model:');
      expect(text(basis)).toContain('its own recent runs on this target');
      expect(text(basis)).toContain("the target's mean over other models");
      expect(query('fieldset.battery-projection')).toBeNull();
      expect(block.parentElement!.classList).toContain('launch-summary');
      expect(query('.setup-group-exec .series-projection-fieldset')).toBeNull();
      finish();
    }));

    it('words a plan above the daily cap as the battery does, with the day span', fakeAsync(() => {
      preflightResponse = {
        findings: [],
        projection: projection({
          plannedRunCount: 30,
          limits: limits({ runsInLast24Hours: 0, runsInLastHour: 0, remainingDailyHeadroom: 20, daySpan: 2, minimumWallMs: 24 * 3600 * 1000 })
        }),
        maxModels: 12
      };
      chooseBatch();
      chooseModels([2, 3]);

      const warning = query('.batch-cap-warning')!;
      expect(text(warning)).toContain('30 runs exceed the daily cap of 20, so this batch spans at least 2 days.');
      expect(text(warning)).toContain('Select Wait when the run cap blocks the next run to start it.');
      expect(query('.batch-headroom-warning')).toBeNull();
      expect(text(query('.batch-day-span-row'))).toContain('at least 2 rolling 24-hour windows');
      finish();
    }));
  });

  describe('Start', () => {
    beforeEach(() => {
      // Confirming arms both chimes under the gesture; the decoding itself is the sound service's spec.
      vi.spyOn(fixture.debugElement.injector.get(BenchmarkCompletionSoundService), 'arm').mockResolvedValue(undefined);
    });

    function readyToStart(findings: BenchmarkModelBatchFindingDto[] = []): void {
      preflightResponse = { findings, projection: projection(), maxModels: 12 };
      chooseBatch();
      chooseModels([2, 3]);
    }

    it('confirms the plan before anything is sent', fakeAsync(() => {
      readyToStart([WARNING]);
      ctx.launcher.setFindingAcknowledged('MB-W01:2,3', true);
      ctx.refresh();

      startButton().click();
      fixture.detectChanges();

      const dialog = query<HTMLDialogElement>('dialog.model-batch-confirm-dialog')!;
      expect(dialog.open).toBe(true);
      expect(text(query(`#${dialog.getAttribute('aria-labelledby')}`))).toBe('Start a batch of 2 models?');
      expect(text(dialog.querySelector('.mbc-subtitle')))
        .toBe('2 runs on the suite Default Suite. Nothing runs until you start it.');
      expect(Array.from(dialog.querySelectorAll('.mbc-models .mbc-model-name')).map(name => text(name)))
        .toEqual(['Second Model', 'Third Model']);
      expect(dialog.querySelector('ul.mbc-model-list')).toBeTruthy();
      expect(dialog.querySelector('ol.mbc-model-list')).toBeNull();
      expect(text(dialog.querySelector('.mbc-models .mbc-section-note'))).toBe('Order drawn at random when the batch starts');
      expect(text(dialog.querySelector('.mbc-figure-runs dd'))).toBe('2 2 models × 1 run per model');
      expect(text(dialog.querySelector('.mbc-target-name'))).toBe('Suite Default Suite');
      const assessor = dialog.querySelector('.mbc-graders > div')!;
      expect(text(assessor.querySelector('dt'))).toBe('Assessor');
      expect(text(assessor.querySelector('dd'))).toContain('Test Model');
      const settings = Array.from(dialog.querySelectorAll('.mbc-settings > div'));
      expect(settings.map(row => text(row.querySelector('dt')))).toEqual(['Scoring profile', 'Response style']);
      expect(text(settings[1].querySelector('dd'))).toBe('Concise — production default');
      expect(text(dialog.querySelector('.mbc-acknowledged-list'))).toBe(WARNING.title);
      expect(dialog.querySelector('table')).toBeNull();
      expect(startModelBatch).not.toHaveBeenCalled();

      dialog.querySelector<HTMLButtonElement>('.dialog-footer .btn-gh-cancel')!.click();
      expect(dialog.open).toBe(false);
      expect(startModelBatch).not.toHaveBeenCalled();
      finish();
    }));

    it('starts the batch on confirmation and opens its progress dialog', fakeAsync(() => {
      const monitor = ctx.monitor as any;
      monitor.followModelBatch = vi.fn();
      monitor.openModelBatchDialog = vi.fn();
      readyToStart([WARNING]);
      ctx.launcher.setFindingAcknowledged('MB-W01:2,3', true);
      ctx.refresh();
      startButton().click();
      fixture.detectChanges();

      query<HTMLButtonElement>('dialog.model-batch-confirm-dialog .confirm-model-batch')!.click();
      fixture.detectChanges();

      expect(startModelBatch).toHaveBeenCalledTimes(1);
      expect(startModelBatch.mock.calls[0][0]).toEqual(expect.objectContaining({
        targetKind: 'Suite', suiteId: 1, testedModelConfigurationIds: [2, 3], runsPerModel: 1,
        order: 'Randomized', acknowledgedFindingKeys: ['MB-W01:2,3']
      }));
      expect(monitor.followModelBatch).toHaveBeenCalledWith(expect.objectContaining({ id: 21 }));
      expect(monitor.openModelBatchDialog).toHaveBeenCalledWith(21);
      expect(query<HTMLDialogElement>('dialog.model-batch-confirm-dialog')!.open).toBe(false);
      expect(ctx.launcher.acknowledgedFindingKeys.size).toBe(0);
      finish();
    }));

    it('refreshes the card from a refusal that carries findings, and keeps the dialog closed', fakeAsync(() => {
      readyToStart();
      startModelBatch.mockReturnValue(throwError(() => ({ status: 409, error: { message: 'Acknowledge every warning before starting.', findings: [SAME_PROVIDER] } })));
      startButton().click();
      fixture.detectChanges();

      query<HTMLButtonElement>('dialog.model-batch-confirm-dialog .confirm-model-batch')!.click();
      fixture.detectChanges();

      expect(query<HTMLDialogElement>('dialog.model-batch-confirm-dialog')!.open).toBe(false);
      expect(ctx.launcher.findings).toEqual([SAME_PROVIDER]);
      expect(text(query('app-model-batch-readiness .gh-readiness-status'))).toBe('1 to review');
      expect(startButton().getAttribute('aria-disabled')).toBe('true');
      finish();
    }));

    it('does not open the confirmation while Start is held back', fakeAsync(() => {
      readyToStart([BLOCKER]);

      startButton().click();
      fixture.detectChanges();

      expect(query<HTMLDialogElement>('dialog.model-batch-confirm-dialog')!.open).toBe(false);
      finish();
    }));

    /** Opens the confirmation for a batch that may start, and returns it. */
    function openConfirm(): HTMLDialogElement {
      startButton().click();
      fixture.detectChanges();
      const dialog = query<HTMLDialogElement>('dialog.model-batch-confirm-dialog')!;
      expect(dialog.open).toBe(true);
      return dialog;
    }

    it('shows each model with its badges', fakeAsync(() => {
      readyToStart();
      const dialog = openConfirm();

      const items = Array.from(dialog.querySelectorAll('.mbc-model-list > li'));
      expect(items.length).toBe(2);
      for (const item of items) {
        expect(text(item.querySelector('.thinking-badge'))).toContain('High');
        expect(item.querySelector('app-provider-badge')).toBeTruthy();
        expect(item.querySelector('.mbc-model-id')).toBeNull();
      }
      finish();
    }));

    it('numbers the models only when they run as listed', fakeAsync(() => {
      readyToStart();
      query<HTMLInputElement>('#batchOrderAsListed')!.click();
      settle();
      const dialog = openConfirm();

      expect(dialog.querySelector('ol.mbc-model-list')).toBeTruthy();
      expect(dialog.querySelector('ul.mbc-model-list')).toBeNull();
      expect(text(dialog.querySelector('.mbc-models .mbc-section-note'))).toBe('Run order');
      finish();
    }));

    it('puts each grader on its own row, with the reader\'s coverage', fakeAsync(() => {
      ctx.launcher.claimVerifierConfigId = 4;
      ctx.launcher.secondOpinionConfigId = 4;
      readyToStart();
      const dialog = openConfirm();

      const rows = Array.from(dialog.querySelectorAll('.mbc-graders > div'));
      expect(rows.map(row => text(row.querySelector('dt')))).toEqual(['Assessor', 'Second reader', 'Claim verifier']);
      expect(text(rows[2].querySelector('.mbc-grader-model'))).toContain('Fourth Model');
      expect(rows[2].querySelector('app-provider-badge')).toBeTruthy();
      const coverage = ctx.launcher.secondOpinionModeOptions.find(o => o.value === ctx.launcher.secondOpinionMode)!.label;
      expect(text(rows[1].querySelector('.mbc-grader-note'))).toBe(coverage);
      expect(rows[2].querySelector('.mbc-grader-note')).toBeNull();
      finish();
    }));

    it('tells identical entries apart by their configuration id', fakeAsync(() => {
      component.systemConfigs = [
        ...component.systemConfigs,
        buildBenchmarkConfig(component.systemConfigs[1], { id: 5 })
      ];
      ctx.refresh();
      preflightResponse = { findings: [], projection: projection(), maxModels: 12 };
      chooseBatch();
      chooseModels([2, 5, 3]);
      const dialog = openConfirm();

      expect(Array.from(dialog.querySelectorAll('.mbc-model-list > li')).map(li => text(li.querySelector('.mbc-model-id'))))
        .toEqual(['#2', '#5', '']);
      finish();
    }));

    it('focuses the title on open, so the close button\'s hint stays shut', fakeAsync(() => {
      readyToStart();
      const dialog = openConfirm();

      expect(document.activeElement).toBe(dialog.querySelector('#modelBatchConfirmTitle'));
      expect(dialog.querySelector('#modelBatchConfirmCloseTip')!.matches(':popover-open')).toBe(false);
      finish();
    }));

    it('shows the day span only when the batch needs more than one window', fakeAsync(() => {
      readyToStart();
      let dialog = openConfirm();
      expect(dialog.querySelector('.mbc-figure-day-span')).toBeNull();
      dialog.querySelector<HTMLButtonElement>('.dialog-footer .btn-gh-cancel')!.click();
      fixture.detectChanges();

      preflightResponse = {
        findings: [],
        projection: projection({ limits: limits({ daySpan: 2, minimumWallMs: 24 * 3600 * 1000 }) }),
        maxModels: 12
      };
      chooseModels([2, 4]);
      dialog = openConfirm();
      expect(text(dialog.querySelector('.mbc-figure-day-span dd'))).toBe('≥ 2 rolling 24-hour windows');
      finish();
    }));
  });

  describe('Completion Alerts', () => {
    it('offers Test failure sound beside Test sound, and says which ends play it', () => {
      const buttons = Array.from(card().querySelectorAll<HTMLButtonElement>('.completion-signals .completion-sound-tests button.action-btn'));
      expect(buttons.map(b => b.getAttribute('aria-label'))).toEqual(['Test sound', 'Test failure sound']);
      for (const button of buttons) {
        expect(button.hasAttribute('title')).toBe(false);
        const tip = card().querySelector(`#${button.getAttribute('interestfor')}`) as HTMLElement;
        expect(tip.getAttribute('popover')).toBe('hint');
        expect(text(tip)).toBe(button.getAttribute('aria-label'));
      }
      expect(text(query('#completionSignalsHint')))
        .toContain('A run, series, battery run or model batch that fails, stops or completes with errors plays a different sound.');
    });

    it('primes the failure chime from Test failure sound, arming first', () => {
      const soundService = fixture.debugElement.injector.get(BenchmarkCompletionSoundService);
      const armSpy = vi.spyOn(soundService, 'arm').mockResolvedValue(undefined);
      const primeSpy = vi.spyOn(soundService, 'prime').mockResolvedValue('played');
      ctx.launcher.completionSound = true;

      card().querySelector<HTMLButtonElement>('.completion-sound-tests .test-failure-sound')!.click();

      expect(armSpy).toHaveBeenCalled();
      expect(primeSpy).toHaveBeenCalledWith('failed');
    });
  });
});
