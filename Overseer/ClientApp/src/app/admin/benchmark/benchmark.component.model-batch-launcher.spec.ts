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
  modelConfigurationIds: [4], acknowledgmentKey: 'MB-W10:4'
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

    // Configuration 1 (Anthropic) is the assessor; 2 to 4 are candidates of three providers.
    const base = component.systemConfigs[0];
    component.systemConfigs = [
      base,
      buildBenchmarkConfig(base, { id: 2, displayName: 'Second Model', provider: 'OpenAI', modelId: 'gpt-test' }),
      buildBenchmarkConfig(base, { id: 3, displayName: 'Third Model', provider: 'Google', modelId: 'gemini-test' }),
      buildBenchmarkConfig(base, { id: 4, displayName: 'Fourth Model', provider: 'Anthropic', modelId: 'claude-other' })
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

      const readiness = query('.model-batch-actions app-model-batch-readiness')!;
      expect(readiness).toBeTruthy();
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
      expect(options.find(o => o.key === 4)!.detail).toBe('Same provider as the assessor');
      expect(options.find(o => o.key === 2)!.detail).toBeUndefined();
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

  describe('Batch Projection', () => {
    it('shows the plan with its runs, wall time, cost, headroom and the per-model basis', fakeAsync(() => {
      preflightResponse = { findings: [], projection: projection({ plannedRunCount: 4 }), maxModels: 12 };
      chooseBatch();
      ctx.launcher.batchRunsPerModel = 2;
      chooseModels([2, 3]);

      const block = query('fieldset.batch-projection')!;
      expect(text(block.querySelector('legend'))).toBe('Batch Projection (2 models × 2 = 4 runs)');
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
      expect(text(query(`#${dialog.getAttribute('aria-labelledby')}`))).toBe('Start model batch?');
      expect(Array.from(dialog.querySelectorAll('.model-batch-plan-models li')).map(li => text(li)))
        .toEqual(['Second Model', 'Third Model']);
      const plan = text(dialog.querySelector('.model-batch-plan'));
      expect(plan).toContain('Random order');
      expect(plan).toContain('Suite: Default Suite');
      expect(plan).toContain('Assessor: Test Model');
      expect(text(dialog.querySelector('.model-batch-plan-acknowledged'))).toBe(WARNING.title);
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
  });

  describe('Completion Alerts', () => {
    it('offers Test failure sound beside Test sound, and says which ends play it', () => {
      const buttons = Array.from(card().querySelectorAll('.completion-signals .completion-signals-actions button.btn-gh-small'))
        .map(b => text(b));
      expect(buttons).toEqual(['Test sound', 'Test failure sound']);
      expect(text(query('#completionSignalsHint')))
        .toContain('A run, series, battery run or model batch that fails, stops or completes with errors plays a different sound.');
    });

    it('primes the failure chime from Test failure sound, arming first', () => {
      const soundService = fixture.debugElement.injector.get(BenchmarkCompletionSoundService);
      const armSpy = vi.spyOn(soundService, 'arm').mockResolvedValue(undefined);
      const primeSpy = vi.spyOn(soundService, 'prime').mockResolvedValue('played');
      ctx.launcher.completionSound = true;

      card().querySelector<HTMLButtonElement>('.completion-signals-actions .test-failure-sound')!.click();

      expect(armSpy).toHaveBeenCalled();
      expect(primeSpy).toHaveBeenCalledWith('failed');
    });
  });
});
