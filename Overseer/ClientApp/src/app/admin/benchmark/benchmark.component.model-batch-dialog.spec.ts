import type { MockedObject } from 'vitest';
import { ComponentFixture } from '@angular/core/testing';
import { of } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import {
  AdminBenchmarkService,
  BenchmarkModelBatchRunDto,
  BenchmarkRunSummaryDto
} from '../../services/admin-benchmark.service';
import { RUN_HISTORY_MEMBERS_STORAGE_KEY } from './benchmark.models';
import {
  AdminBenchmarkSpecContext, buildBatteryRun, buildModelBatchMember, buildModelBatchRun, clearStoredState,
  createAdminBenchmarkFixture
} from './benchmark.component.testing';

// The Model Batch Progress dialog in the shell: hosting and stacking with the run and battery
// progress dialogs (Back to Batch), the batch-owned notice of a member battery run, the comparison
// preset, Run History's batch card and member kickers, and Chat Consistency's batch setup.

function clearState(): void {
  clearStoredState();
  try {
    localStorage.removeItem(RUN_HISTORY_MEMBERS_STORAGE_KEY);
  } catch { /* private-browsing modes throw */ }
}

/** Batch 21, finished: two models of one run each, runs 61 and 62. */
function finishedBatch(overrides: Partial<BenchmarkModelBatchRunDto> = {}): BenchmarkModelBatchRunDto {
  return buildModelBatchRun({
    status: 'Completed', isDriving: false, currentMemberIndex: null, completedMemberCount: 2,
    startedAtUtc: '2026-09-05T00:00:00Z', completedAtUtc: '2026-09-05T02:00:00Z', liveTotalCostUsd: 2.5,
    members: [
      buildModelBatchMember(0, 2, { status: 'Completed', runId: 61, runIds: [61] }),
      buildModelBatchMember(1, 3, { status: 'Completed', runId: 62, runIds: [62] })
    ],
    ...overrides
  });
}

function historyRun(id: number, startedAtUtc: string): BenchmarkRunSummaryDto {
  return {
    id, benchmarkSuiteId: 1, suiteName: 'Default Suite', testedModelDisplayNameUsed: `Model ${id}`,
    testedModelProviderUsed: 'OpenAI', testedModelIdUsed: `m-${id}`, assessorModelDisplayNameUsed: 'Grader',
    status: 'Completed', startedAtUtc, totalAnswerDurationMs: 1000, totalDurationMs: 1000,
    speedMeasurementDegraded: false, answeredQuestionCount: 5, totalQuestionCount: 5
  } as BenchmarkRunSummaryDto;
}

describe('AdminBenchmarkComponent: model batch progress', () => {
  let ctx: AdminBenchmarkSpecContext;
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;

  beforeEach(clearState);

  beforeEach(async () => {
    ctx = await createAdminBenchmarkFixture();
    ({ component, fixture, benchmarkServiceMock } = ctx);
  });

  afterEach(() => {
    ctx.monitor.ngOnDestroy();
    for (const dialog of Array.from(document.querySelectorAll('dialog[open]')) as HTMLDialogElement[]) {
      dialog.close();
    }
    clearState();
  });

  function query<T extends Element = HTMLElement>(selector: string): T | null {
    return fixture.nativeElement.querySelector(selector) as T | null;
  }

  function text(element: Element | null): string {
    return (element?.textContent || '').replace(/\s+/g, ' ').trim();
  }

  function batchDialog(): HTMLDialogElement {
    return query<HTMLDialogElement>('dialog.model-batch-progress-dialog')!;
  }

  function openBatch(dto: BenchmarkModelBatchRunDto = buildModelBatchRun()): void {
    benchmarkServiceMock.getModelBatch.mockReturnValue(of(dto));
    ctx.monitor.openModelBatchDialog(dto.id);
    ctx.refresh();
  }

  it('reattaches a live model batch on load, without opening its dialog', () => {
    benchmarkServiceMock.getActiveModelBatch.mockReturnValue(of(buildModelBatchRun()));
    component.ngOnInit();
    ctx.refresh();

    expect(benchmarkServiceMock.getActiveModelBatch).toHaveBeenCalled();
    expect(ctx.monitor.activeModelBatch?.id).toBe(21);
    expect(ctx.monitor.modelBatchIsLive).toBe(true);
    expect(ctx.monitor.modelBatchDialogVisible).toBe(false);
    expect(batchDialog().open).toBe(false);
  });

  it('hosts the batch dialog the monitor opens', () => {
    openBatch();

    expect(batchDialog().open).toBe(true);
    expect(text(query('#mbTitle'))).toBe('Model Batch #21');
  });

  describe('Back to Batch', () => {
    it('replaces the batch dialog with a member\'s run progress, which leads back to it', () => {
      openBatch();
      (query<HTMLButtonElement>('article.mb-member[data-member-id="100"] .mb-open-run'))!.click();
      ctx.refresh();

      expect(batchDialog().open).toBe(false);
      expect(ctx.monitor.returnToModelBatchId).toBe(21);
      expect(component.runProgressDialog.nativeElement.open).toBe(true);
      expect(ctx.monitor.dialogRunId).toBe(61);
      const back = query<HTMLButtonElement>('.benchmark-run-progress-dialog .dialog-footer .btn-gh-cancel')!;
      expect(text(back)).toBe('Back to Batch');
      expect(query('.benchmark-run-progress-dialog .dialog-header .btn-icon-action')?.getAttribute('aria-label'))
        .toBe('Return to model batch progress');

      back.click();
      ctx.refresh();

      expect(component.runProgressDialog.nativeElement.open).toBe(false);
      expect(ctx.monitor.modelBatchDialogVisible).toBe(true);
      expect(ctx.monitor.returnToModelBatchId).toBeNull();
      expect(batchDialog().open).toBe(true);
    });

    it('takes the batch over the battery when both lead back', () => {
      ctx.monitor.returnToModelBatchId = 21;
      ctx.monitor.returnToBatteryRunId = 9;
      ctx.refresh();
      expect(component.runProgressCloseLabel('Close')).toBe('Back to Batch');

      component.closeRunProgressDialog();
      ctx.refresh();
      expect(ctx.monitor.modelBatchDialogVisible).toBe(true);
      expect(ctx.monitor.batteryDialogVisible).toBe(false);
    });

    it('replaces the batch dialog with a member\'s battery progress, which reopens it on close', async () => {
      const battery = buildBatteryRun({ id: 9, modelBatchRunId: 21 });
      benchmarkServiceMock.getBatteryRun.mockReturnValue(of(battery));
      openBatch(buildModelBatchRun({
        targetKind: 'Battery', suiteNames: ['Default Suite', 'Second Suite'],
        members: [buildModelBatchMember(0, 2, { status: 'Running', batteryRunId: 9, runIds: [61], currentRunId: 61, currentStepIndex: 1, stepCount: 2 })]
      }));
      (query<HTMLButtonElement>('.mb-open-battery'))!.click();
      ctx.refresh();

      expect(batchDialog().open).toBe(false);
      expect(ctx.monitor.batteryDialogVisible).toBe(true);
      const back = query<HTMLButtonElement>('.battery-progress-dialog .bp-back-to-batch')!;
      expect(text(back)).toBe('Back to Batch');

      back.click();
      ctx.refresh();
      await Promise.resolve();
      ctx.refresh();

      expect(ctx.monitor.batteryDialogVisible).toBe(false);
      expect(ctx.monitor.modelBatchDialogVisible).toBe(true);
      expect(batchDialog().open).toBe(true);
    });
  });

  describe('a member battery run\'s dialog', () => {
    it('says the batch owns it, keeps its own Continue and Cancel waiting, and opens the batch', () => {
      benchmarkServiceMock.getModelBatch.mockReturnValue(of(buildModelBatchRun({ status: 'Stopped', isDriving: false })));
      benchmarkServiceMock.getBatteryRun.mockReturnValue(of(buildBatteryRun({
        id: 9, status: 'Stopped', stopReason: 'MemberFailed', stopReasonText: 'A member run failed', resumable: true,
        isDriving: false, modelBatchRunId: 21
      })));
      ctx.monitor.openBatteryDialog(9);
      ctx.refresh();

      expect(text(query('.bp-batch-notice'))).toContain('Part of model batch #21.');
      const reason = query('#bpBatchOwnedReason');
      expect(text(reason)).toBe('Use the model batch\'s progress dialog.');
      for (const selector of ['.bp-continue', '.bp-cancel-battery']) {
        const button = query<HTMLButtonElement>(selector)!;
        expect(button.getAttribute('aria-disabled')).toBe('true');
        expect(button.getAttribute('aria-describedby')).toBe('bpBatchOwnedReason');
      }
      query<HTMLButtonElement>('.bp-continue')!.click();
      expect(benchmarkServiceMock.resumeBatteryRun).not.toHaveBeenCalled();

      query<HTMLButtonElement>('.bp-open-batch')!.click();
      ctx.refresh();
      expect(ctx.monitor.batteryDialogVisible).toBe(false);
      expect(ctx.monitor.modelBatchDialogVisible).toBe(true);
      expect(ctx.monitor.modelBatchDialogId).toBe(21);
    });

    it('lifts the gate once the batch is final', () => {
      benchmarkServiceMock.getModelBatch.mockReturnValue(of(finishedBatch()));
      benchmarkServiceMock.getBatteryRun.mockReturnValue(of(buildBatteryRun({
        id: 9, status: 'Stopped', resumable: true, isDriving: false, modelBatchRunId: 21
      })));
      ctx.monitor.openBatteryDialog(9);
      ctx.refresh();

      expect(query('.bp-batch-notice')).toBeNull();
      expect(query<HTMLButtonElement>('.bp-continue')?.getAttribute('aria-disabled')).toBeNull();
    });
  });

  it('opens the comparison wizard on the batch\'s runs from Open in Model Comparison', () => {
    openBatch(finishedBatch());
    query<HTMLButtonElement>('.mb-open-comparison')!.click();
    ctx.refresh();

    expect(batchDialog().open).toBe(false);
    expect(ctx.comparison.comparisonRunIds).toEqual([61, 62]);
    expect(ctx.comparison.comparisonGroupIds).toEqual([]);
    expect(ctx.comparison.comparisonBatteryRunIds).toEqual([]);
  });

  it('applies a preset\'s groups and runs, and a battery preset alone', () => {
    ctx.comparison.applyComparisonPreset({ batteryRunIds: [], runIds: [61, 61], groupIds: [7] });
    expect(ctx.comparison.comparisonRunIds).toEqual([61]);
    expect(ctx.comparison.comparisonGroupIds).toEqual([7]);

    ctx.comparison.applyComparisonPreset({ batteryRunIds: [9], runIds: [61], groupIds: [7] });
    expect(ctx.comparison.comparisonBatteryRunIds).toEqual([9]);
    expect(ctx.comparison.comparisonRunIds).toEqual([]);
    expect(ctx.comparison.comparisonGroupIds).toEqual([]);
  });

  describe('Run History', () => {
    function openHistory(batches: BenchmarkModelBatchRunDto[], runs: BenchmarkRunSummaryDto[] = []): void {
      benchmarkServiceMock.getRuns.mockReturnValue(of(runs));
      benchmarkServiceMock.listModelBatches.mockReturnValue(of(batches));
      query<HTMLButtonElement>('#bm-tab-history')!.click();
      fixture.detectChanges();
    }

    function batchCard(id = 21): HTMLElement {
      return query(`article.rh-card-batch[data-model-batch-run-id="${id}"]`)!;
    }

    it('lists a batch card among the runs, and badges its member runs', () => {
      openHistory([finishedBatch()], [
        historyRun(62, '2026-09-05T01:00:00Z'), historyRun(61, '2026-09-05T00:30:00Z'), historyRun(5, '2026-09-01T00:00:00Z')
      ]);

      const cards = Array.from(fixture.nativeElement.querySelectorAll('.rh-card-list article.rh-card')) as HTMLElement[];
      expect(cards.map(card => card.getAttribute('data-model-batch-run-id') ? 'batch' : card.getAttribute('data-run-id')))
        .toEqual(['62', '61', 'batch', '5']);

      const card = batchCard();
      expect(text(card.querySelector('.rh-batch-run-badge'))).toBe('Model batch #21');
      expect(text(card.querySelector('.rh-batch-progress'))).toBe('2 of 2 models');
      expect(Array.from(card.querySelectorAll('.rh-batch-model-name')).map(name => text(name))).toEqual(['Model 2', 'Model 3']);
      expect(Array.from(card.querySelectorAll('.rh-metric dt')).map(dt => text(dt))).toEqual(['Models', 'Runs', 'Duration', 'Cost']);
      expect(card.querySelector('[role="group"]')?.getAttribute('aria-label')).toBe('Actions for model batch 21');

      expect(text(query('article.rh-card[data-run-id="61"] .rh-batch-badge'))).toBe('Batch #21');
      expect(query('article.rh-card[data-run-id="5"] .rh-batch-badge')).toBeNull();
    });

    it('opens the batch\'s progress from View progress', () => {
      openHistory([finishedBatch()]);
      benchmarkServiceMock.getModelBatch.mockReturnValue(of(finishedBatch()));

      (batchCard().querySelector('.rh-batch-progress-btn') as HTMLButtonElement).click();
      ctx.refresh();

      expect(ctx.monitor.modelBatchDialogId).toBe(21);
      expect(batchDialog().open).toBe(true);
    });

    it('keeps Open in Model Comparison waiting below two results, and opens the wizard otherwise', () => {
      const oneDone = finishedBatch({
        completedMemberCount: 1,
        members: [buildModelBatchMember(0, 2, { status: 'Completed', runId: 61, runIds: [61] }), buildModelBatchMember(1, 3, { status: 'Failed' })]
      });
      openHistory([oneDone, finishedBatch({ id: 22 })]);

      const waiting = batchCard(21).querySelector('.rh-batch-compare') as HTMLButtonElement;
      expect(waiting.getAttribute('aria-disabled')).toBe('true');

      (batchCard(22).querySelector('.rh-batch-compare') as HTMLButtonElement).click();
      ctx.refresh();
      expect(ctx.comparison.comparisonRunIds).toEqual([61, 62]);
    });

    it('deletes a final batch after its confirmation, and keeps a live one', () => {
      openHistory([finishedBatch(), buildModelBatchRun({ id: 23 })]);

      const live = batchCard(23).querySelector('.rh-batch-delete') as HTMLButtonElement;
      expect(live.getAttribute('aria-disabled')).toBe('true');
      live.click();
      expect(component.confirmDialogTitle).not.toBe('Delete model batch #23?');

      (batchCard(21).querySelector('.rh-batch-delete') as HTMLButtonElement).click();
      expect(component.confirmDialogTitle).toBe('Delete model batch #21?');
      expect(component.confirmDialogMessage).toBe('Its member runs are kept.');
      component.executeConfirmAction();

      expect(benchmarkServiceMock.deleteModelBatch).toHaveBeenCalledWith(21);
    });

    it('filters batches by the Kind facet', () => {
      openHistory([finishedBatch()], [historyRun(5, '2026-09-01T00:00:00Z')]);
      ctx.historyTab().onHistoryFacetChange('kind', ['Model batch']);
      fixture.detectChanges();

      const cards = Array.from(fixture.nativeElement.querySelectorAll('.rh-card-list article.rh-card')) as HTMLElement[];
      expect(cards.length).toBe(1);
      expect(cards[0].getAttribute('data-model-batch-run-id')).toBe('21');
    });
  });

  describe('Chat Consistency\'s Set up as model batch', () => {
    it('opens Run Benchmark as a model batch on the target and model, asks for a control model, and focuses the picker', () => {
      ctx.bridge.prefillModelBatch({ targetKind: 'suite', suiteId: 1, batteryId: null, modelConfigurationId: 1, controlSuggested: true });
      ctx.refresh();

      expect(component.activeSubTab).toBe('run');
      expect(ctx.launcher.runMode).toBe('batch');
      expect(ctx.launcher.runTargetKind).toBe('suite');
      expect(ctx.launcher.selectedSuiteId).toBe(1);
      expect(ctx.launcher.batchModelKeys).toEqual([1]);
      expect(text(query('#bmBatchModelsHint'))).toBe('Add a control model from another provider.');
      expect(document.activeElement).toBe(query('.batch-models-picker .selector-trigger'));
      expect(ctx.launcher.batchModelsFocusPending).toBe(false);
      expect(benchmarkServiceMock.startModelBatch).not.toHaveBeenCalled();
    });

    it('does nothing while the active sub-tab refuses to be left', () => {
      const release = ctx.bridge.setLeaveGuard(() => 'Wait for the chart export.');
      ctx.bridge.prefillModelBatch({ targetKind: 'suite', suiteId: 1, batteryId: null, modelConfigurationId: 1, controlSuggested: true });
      release();

      expect(ctx.launcher.runMode).toBe('single');
    });
  });
});
