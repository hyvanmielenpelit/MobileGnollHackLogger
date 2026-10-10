import type { Mock, MockedObject } from 'vitest';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { NEVER, of, throwError } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkModelBatchMemberDto,
  BenchmarkModelBatchRunDto
} from '../../../services/admin-benchmark.service';
import { BenchmarkActiveRunMonitor } from '../state/benchmark-active-run.monitor';
import { BenchmarkShellBridge, BenchmarkConfirmOptions } from '../state/benchmark-shell-bridge.service';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import { ModelBatchProgressDialogComponent } from './model-batch-progress-dialog.component';
import {
  MODEL_BATCH_COMPARE_REASON,
  MODEL_BATCH_CORPUS_QUIET_TEXT,
  modelBatchComparisonPreset,
  modelBatchDiagnosticsFileName
} from './model-batch.models';

function member(orderIndex: number, overrides: Partial<BenchmarkModelBatchMemberDto> = {}): BenchmarkModelBatchMemberDto {
  return {
    id: 100 + orderIndex, orderIndex, status: 'Pending',
    model: {
      configurationId: 10 + orderIndex, displayName: `Model ${orderIndex + 1}`, provider: orderIndex % 2 === 0 ? 'OpenAI' : 'Google',
      modelId: `model-${orderIndex}`, thinkingLevel: 'medium', endpoint: 'official'
    },
    runIds: [], stepCount: 1, answeredQuestionCount: 0, totalQuestionCount: 15, instrumentDriftKeys: [],
    ...overrides
  };
}

function completed(orderIndex: number, runId: number, overrides: Partial<BenchmarkModelBatchMemberDto> = {}): BenchmarkModelBatchMemberDto {
  return member(orderIndex, {
    status: 'Completed', runId, runIds: [runId],
    result: {
      intelligenceIndex: 71.4, indexHalfWidth: null, medianModelTimeMs: 17200, ttftP50Ms: 1800,
      candidateCostPerQuestionUsd: 0.0042, totalCostUsd: 1.2, refutedClaims: 0, confirmedCriticalErrors: 0,
      failedAnswers: 0, providerErrors: 0, retries: 0
    },
    ...overrides
  });
}

function batch(overrides: Partial<BenchmarkModelBatchRunDto> = {}): BenchmarkModelBatchRunDto {
  return {
    id: 21, status: 'Running', targetKind: 'Suite', suiteId: 1, targetName: 'Default Suite', suiteNames: ['Default Suite'],
    runsPerModel: 1, order: 'Randomized', orderSeed: 4711, allowCapWait: false, createdAtUtc: '2026-10-10T08:00:00Z',
    startedAtUtc: '2026-10-10T08:00:00Z',
    run: { suiteId: 1, testedModelConfigurationId: 10, assessorModelConfigurationId: 5 },
    members: [
      member(0, { status: 'Running', runId: 61, runIds: [61], currentRunId: 61, currentStage: 'Answering', answeredQuestionCount: 4 }),
      member(1)
    ],
    currentMemberIndex: 0, requestedMemberCount: 2, completedMemberCount: 0, failedMemberCount: 0, skippedMemberCount: 0,
    liveCandidateCostUsd: 0.4321, liveTotalCostUsd: 1.5,
    acknowledgedFindings: [], adviceAtStart: [], instrumentChangeAcknowledged: false, isDriving: true,
    resumable: false, resumeOptions: [], stalled: false, stallMinutes: 15, lastProgressAtUtc: '2026-10-10T08:30:00Z',
    ...overrides
  };
}

/** Stopped after the first model's run failed, with the three ways on. */
function stopped(overrides: Partial<BenchmarkModelBatchRunDto> = {}): BenchmarkModelBatchRunDto {
  return batch({
    status: 'Stopped', stopReason: 'MemberStopped', stopReasonText: 'A model\'s run failed', isDriving: false, resumable: true,
    members: [completed(0, 61), member(1, { status: 'Failed', runId: 62, runIds: [62], errorMessage: 'The provider refused every request.' }), member(2)],
    currentMemberIndex: 1, requestedMemberCount: 3, completedMemberCount: 1, failedMemberCount: 1,
    resumeOptions: [
      { mode: 'Continue', label: 'Continue', reason: 'Runs Model 2 again.' },
      { mode: 'SkipCurrent', label: 'Skip this model', reason: 'Marks Model 2 skipped and goes on.' }
    ],
    ...overrides
  });
}

describe('ModelBatchProgressDialogComponent', () => {
  let fixture: ComponentFixture<ModelBatchProgressDialogComponent>;
  let component: ModelBatchProgressDialogComponent;
  let service: MockedObject<AdminBenchmarkService>;
  let monitor: { armCompletionSignalsFromGesture: Mock; activeModelBatch: null };
  let bridge: { openConfirmDialog: Mock };

  function open(dto: BenchmarkModelBatchRunDto): void {
    service.getModelBatch.mockReturnValue(of(dto));
    fixture.componentRef.setInput('modelBatchRunId', dto.id);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
  }

  function close(): void {
    fixture.componentRef.setInput('visible', false);
    fixture.detectChanges();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(selector: string, root: ParentNode = el()): string {
    return (root.querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function button(selector: string, root: ParentNode = el()): HTMLButtonElement | null {
    return root.querySelector(selector) as HTMLButtonElement | null;
  }

  function card(memberId: number): HTMLElement {
    return el().querySelector(`article.mb-member[data-member-id="${memberId}"]`) as HTMLElement;
  }

  function lastConfirm(): BenchmarkConfirmOptions {
    return bridge.openConfirmDialog.mock.lastCall![0] as BenchmarkConfirmOptions;
  }

  beforeEach(async () => {
    service = {
      getModelBatch: vi.fn().mockName('AdminBenchmarkService.getModelBatch'),
      cancelModelBatch: vi.fn().mockName('AdminBenchmarkService.cancelModelBatch'),
      resumeModelBatch: vi.fn().mockName('AdminBenchmarkService.resumeModelBatch'),
      skipModelBatchMember: vi.fn().mockName('AdminBenchmarkService.skipModelBatchMember'),
      getModelBatchDiagnostics: vi.fn().mockName('AdminBenchmarkService.getModelBatchDiagnostics'),
      getRunSeries: vi.fn().mockName('AdminBenchmarkService.getRunSeries')
    } as unknown as MockedObject<AdminBenchmarkService>;
    service.getModelBatch.mockReturnValue(of(batch()));
    service.cancelModelBatch.mockReturnValue(of(undefined));
    service.resumeModelBatch.mockReturnValue(of(batch()));
    service.getModelBatchDiagnostics.mockReturnValue(of('BATCH\n  id: 21'));
    monitor = {
      armCompletionSignalsFromGesture: vi.fn().mockName('BenchmarkActiveRunMonitor.armCompletionSignalsFromGesture'),
      activeModelBatch: null
    };
    bridge = { openConfirmDialog: vi.fn().mockName('BenchmarkShellBridge.openConfirmDialog') };

    await TestBed.configureTestingModule({
      imports: [ModelBatchProgressDialogComponent],
      providers: [
        { provide: AdminBenchmarkService, useValue: service },
        { provide: BenchmarkActiveRunMonitor, useValue: monitor },
        { provide: BenchmarkShellBridge, useValue: bridge },
        {
          provide: BenchmarkWorkspaceStore,
          useValue: {
            systemConfigs: [
              { id: 5, displayName: 'Grader Model', modelId: 'grader-1', provider: 'Anthropic', thinkingLevel: 'medium', reasoningMode: null },
              { id: 6, displayName: 'Second Grader', modelId: 'grader-2', provider: 'Google', thinkingLevel: null, reasoningMode: null }
            ]
          }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ModelBatchProgressDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    close();
  });

  it('loads nothing while hidden', () => {
    expect(service.getModelBatch).not.toHaveBeenCalled();
  });

  it('opens on the batch: title, plan, order and graders, focused on the title', () => {
    open(batch());

    expect(service.getModelBatch).toHaveBeenCalledWith(21);
    const dialog = el().querySelector('dialog.model-batch-progress-dialog') as HTMLDialogElement;
    expect(dialog.open).toBe(true);
    expect(dialog.getAttribute('aria-labelledby')).toBe('mbTitle');
    expect(text('#mbTitle')).toBe('Model Batch #21');
    expect(document.activeElement?.id).toBe('mbTitle');
    expect(text('.mb-subtitle')).toBe('Default Suite · 2 models × 1 run · Random order, seed 4711');
    expect(text('.mb-graders [data-fact="assessor"] dt')).toBe('Assessor');
    expect(text('.mb-graders [data-fact="assessor"] dd')).toContain('Grader Model');
    expect(button('.btn-icon-action')?.getAttribute('aria-label')).toBe('Close model batch progress');
    expect(button('.btn-icon-action')?.hasAttribute('title')).toBe(false);
  });

  it('names a panel of assessors', () => {
    open(batch({ run: { suiteId: 1, testedModelConfigurationId: 10, assessorModelConfigurationId: 5, coAssessorModelConfigurationId: 6 } }));
    expect(text('.mb-graders [data-fact="assessor"] dt')).toBe('Assessors');
    expect(text('.mb-graders [data-fact="assessor"] dd')).toContain('Second Grader');
  });

  it('has exactly one status region, which names the model running', () => {
    open(batch());
    expect(el().querySelectorAll('[role="status"]').length).toBe(1);
    expect(text('.mb-stage-line')).toBe('Running model 1 of 2: Model 1');
  });

  it('shows the stat strip, the shared instrument, the corpus line and the rail', () => {
    open(batch());
    const stats = Array.from(el().querySelectorAll('.mb-stats .run-stat')).map(stat => text('dt', stat));
    expect(stats).toEqual(['Status', 'Elapsed', 'Models done', 'Runs', 'Failed', 'Candidate cost', 'Total cost']);
    expect(text('.mb-stat-total-cost dd')).toBe('$1.50 so far');
    expect(text('.mb-instrument-shared')).toBe('All members share one instrument');
    expect(text('.mb-corpus-quiet')).toBe(MODEL_BATCH_CORPUS_QUIET_TEXT);
    const rail = Array.from(el().querySelectorAll('.mb-rail .run-stage'));
    expect(rail.map(item => text('.run-stage-name', item))).toEqual(['Model runs', 'Ready to compare']);
    expect(rail[0].classList).toContain('is-current');
  });

  it('shows a running member\'s run, stage and answered questions, and a pending member waiting with Skip model', () => {
    open(batch());

    const running = card(100);
    expect(text('.job-status-chip', running)).toBe('Running');
    expect(text('.mb-member-stage', running)).toBe('Run #61 · Stage 1 of 3 — Answering and grading');
    expect(running.textContent).toContain('4 of 15 questions answered');
    expect(running.querySelector('progress.mb-member-progress')).not.toBeNull();
    expect(button('.mb-open-run', running)?.getAttribute('aria-label')).toBe('Open run progress for run #61');
    expect(running.querySelector('[role="group"]')?.getAttribute('aria-label')).toBe('Actions for Model 1');

    const pending = card(101);
    expect(text('.mb-member-waiting', pending)).toBe('Waiting');
    expect(text('.mb-skip', pending)).toBe('Skip model');
  });

  it('skips a pending member and shows the batch the server returns', () => {
    const skipped = batch({ members: [batch().members[0], member(1, { status: 'Skipped' })], skippedMemberCount: 1 });
    service.skipModelBatchMember.mockReturnValue(of(skipped));
    open(batch());

    button('.mb-skip', card(101))!.click();
    fixture.detectChanges();

    expect(service.skipModelBatchMember).toHaveBeenCalledWith(21, 101);
    expect(text('.job-status-chip', card(101))).toBe('Skipped');
    expect(text('.mb-member-hint', card(101))).toBe('Skipped by the operator.');
  });

  it('hands a member\'s run to the host and closes', () => {
    open(batch());
    const closed = vi.fn();
    const openRun = vi.fn();
    component.closed.subscribe(closed);
    component.openRunProgress.subscribe(openRun);

    button('.mb-open-run', card(100))!.click();

    expect(closed).toHaveBeenCalledTimes(1);
    expect(openRun).toHaveBeenCalledWith({ runId: 61, modelBatchRunId: 21 });
  });

  it('offers one Open run progress per run of a series member', () => {
    open(batch({
      runsPerModel: 3,
      members: [member(0, { status: 'Running', seriesId: 8, runIds: [61, 62], currentRunId: 62, currentStepIndex: 2, stepCount: 3 }), member(1, { stepCount: 3 })]
    }));
    const buttons = Array.from(card(100).querySelectorAll('.mb-open-run')).map(b => b.getAttribute('aria-label'));
    expect(buttons).toEqual(['Open run progress for run #61', 'Open run progress for run #62']);
    expect(text('.mb-member-step', card(100))).toBe('Run 2 of 3');
    expect(text('.mb-member-ref', card(100))).toBe('Series #8');
  });

  it('shows a battery member\'s suite and round, and hands its battery run to the host', () => {
    open(batch({
      targetKind: 'Battery', targetName: 'Core Battery', batteryRevision: 3, suiteNames: ['Default Suite', 'Second Suite'],
      members: [member(0, { status: 'Running', batteryRunId: 9, runIds: [61, 62], currentRunId: 62, currentStepIndex: 2, stepCount: 2 }), member(1, { stepCount: 2 })]
    }));
    expect(text('.mb-subtitle')).toBe('Core Battery, revision 3 · 2 models × 2 suites × 1 run · Random order, seed 4711');
    expect(text('.mb-member-step', card(100))).toBe('Suite 2 of 2 · round 1: Second Suite');
    expect(card(100).querySelector('.mb-open-run')).toBeNull();

    const openBattery = vi.fn();
    component.openBatteryProgress.subscribe(openBattery);
    button('.mb-open-battery', card(100))!.click();
    expect(openBattery).toHaveBeenCalledWith({ batteryRunId: 9, modelBatchRunId: 21 });
  });

  it('shows a completed member\'s index and facts, and opens its report over the dialog', () => {
    open(batch({ status: 'Completed', isDriving: false, currentMemberIndex: null, members: [completed(0, 61), completed(1, 62)], completedMemberCount: 2 }));
    const done = card(100);
    expect(done.querySelector('app-index-badge')).not.toBeNull();
    expect(Array.from(done.querySelectorAll('.mb-member-facts dt')).map(dt => dt.textContent?.trim()))
      .toEqual(['Median model time', 'TTFT P50', 'Cost per question']);
    expect(text('.mb-member-facts', done)).toContain('$0.0042');
    expect(text('.mb-member-ref', done)).toBe('Run #61');

    const report = vi.fn();
    const closed = vi.fn();
    component.openRunReport.subscribe(report);
    component.closed.subscribe(closed);
    button('.mb-view-report', done)!.click();
    expect(report).toHaveBeenCalledWith(61);
    expect(closed).not.toHaveBeenCalled();
  });

  it('opens a battery member\'s Battery Run Report', () => {
    open(batch({
      status: 'Completed', targetKind: 'Battery', suiteNames: ['A', 'B'], currentMemberIndex: null, completedMemberCount: 1,
      members: [member(0, { status: 'Completed', batteryRunId: 9, runIds: [61, 62], result: { overallIndex: 80, refutedClaims: 0, confirmedCriticalErrors: 0, failedAnswers: 0, providerErrors: 0, retries: 0 } })]
    }));
    const report = vi.fn();
    component.openBatteryRunReport.subscribe(report);
    expect(button('.mb-view-report', card(100))?.getAttribute('aria-label')).toBe('View Battery Run Report of battery run #9');
    button('.mb-view-report', card(100))!.click();
    expect(report).toHaveBeenCalledWith(9);
  });

  describe('a stopped batch', () => {
    it('states the stop and its resume options, and labels the resume buttons', () => {
      open(stopped());

      expect(text('.mb-state-name')).toBe('Stopped');
      expect(text('.mb-state-detail')).toContain('A model\'s run failed');
      expect(text('.mb-resume-options')).toContain('Runs Model 2 again.');
      expect(text('.mb-stage-line')).toBe('Stopped: a model\'s run stopped');
      const labels = Array.from(el().querySelectorAll('.dialog-footer .mb-resume')).map(b => b.textContent?.replace(/\s+/g, ' ').trim());
      expect(labels).toEqual(['Skip this model', 'Continue — A model\'s run failed']);
      expect(text('.mb-close')).toBe('Close');
      expect(button('.mb-cancel-batch')).not.toBeNull();
      expect(text('.mb-member-hint', card(101))).toBe('The provider refused every request.');
    });

    it('continues under the click\'s gesture and tells the host', () => {
      open(stopped());
      const resumed = vi.fn();
      component.batchResumed.subscribe(resumed);

      button('.mb-resume[data-mode="Continue"]')!.click();

      expect(monitor.armCompletionSignalsFromGesture).toHaveBeenCalledTimes(1);
      expect(service.resumeModelBatch).toHaveBeenCalledWith(21, 'Continue');
      expect(resumed).toHaveBeenCalledWith(21);
    });

    it('keeps every action waiting, with the reason, while a request is in flight', () => {
      service.resumeModelBatch.mockReturnValue(NEVER);
      open(stopped());

      button('.mb-resume[data-mode="SkipCurrent"]')!.click();
      fixture.detectChanges();

      expect(text('#mbActionReason')).toBe('Waiting for the server to resume the batch.');
      for (const selector of ['.mb-resume[data-mode="Continue"]', '.mb-resume[data-mode="SkipCurrent"]', '.mb-cancel-batch']) {
        expect(button(selector)?.getAttribute('aria-disabled')).toBe('true');
        expect(button(selector)?.getAttribute('aria-describedby')).toBe('mbActionReason');
      }
      button('.mb-resume[data-mode="Continue"]')!.click();
      expect(service.resumeModelBatch).toHaveBeenCalledTimes(1);
    });

    it('asks before re-running every model under the current instrument, with the cost', () => {
      open(stopped({
        stopReason: 'InstrumentChanged',
        resumeOptions: [
          { mode: 'RerunUnderCurrentInstrument', label: 'Re-run under current instrument', reason: 'Re-runs every completed model.' },
          { mode: 'AcceptInstrumentChange', label: 'Continue — accept the change', reason: 'Marks the batch not comparable across the change.' }
        ]
      }));
      const labels = Array.from(el().querySelectorAll('.dialog-footer .mb-resume')).map(b => b.textContent?.replace(/\s+/g, ' ').trim());
      expect(labels).toEqual(['Re-run under current instrument', 'Continue — accept the change']);

      button('.mb-resume[data-mode="RerunUnderCurrentInstrument"]')!.click();
      expect(service.resumeModelBatch).not.toHaveBeenCalled();
      expect(lastConfirm().message).toContain('1 model, about $1.20 at their last cost');

      lastConfirm().action();
      expect(monitor.armCompletionSignalsFromGesture).toHaveBeenCalledTimes(1);
      expect(service.resumeModelBatch).toHaveBeenCalledWith(21, 'RerunUnderCurrentInstrument');
    });

    it('shows a refused resume\'s message', () => {
      service.resumeModelBatch.mockReturnValue(throwError(() => ({
        status: 409, error: { instrumentChanged: true, batchId: 21, changedKeys: ['ToolGuidesSha256'], message: 'The tool guides changed.' }
      })));
      open(stopped());
      button('.mb-resume[data-mode="Continue"]')!.click();
      fixture.detectChanges();

      expect(text('.mb-action-error')).toBe('The tool guides changed.');
    });
  });

  it('cancels the batch after the shell\'s confirmation', () => {
    open(batch());
    const canceled = vi.fn();
    component.batchCanceled.subscribe(canceled);

    button('.mb-cancel-batch')!.click();
    expect(service.cancelModelBatch).not.toHaveBeenCalled();
    expect(lastConfirm().title).toBe('Cancel model batch #21?');
    expect(lastConfirm().buttonText).toBe('Cancel Batch');

    lastConfirm().action();
    expect(service.cancelModelBatch).toHaveBeenCalledWith(21);
    expect(canceled).toHaveBeenCalledWith(21);
  });

  describe('Open in Model Comparison', () => {
    it('waits, with its reason, below two models with a result', () => {
      open(batch());
      const compare = button('.mb-open-comparison')!;
      expect(compare.getAttribute('aria-disabled')).toBe('true');
      expect(compare.getAttribute('aria-describedby')).toBe('mbCompareReason');
      expect(text('#mbCompareReason')).toBe(MODEL_BATCH_COMPARE_REASON);

      const preset = vi.fn();
      component.openComparison.subscribe(preset);
      compare.click();
      expect(preset).not.toHaveBeenCalled();
    });

    it('opens the runs of a one-run-per-model batch and closes', () => {
      open(batch({ status: 'Completed', currentMemberIndex: null, members: [completed(0, 61), completed(1, 62)], completedMemberCount: 2 }));
      const preset = vi.fn();
      const closed = vi.fn();
      component.openComparison.subscribe(preset);
      component.closed.subscribe(closed);

      button('.mb-open-comparison')!.click();

      expect(preset).toHaveBeenCalledWith({ batteryRunIds: [], runIds: [61, 62], groupIds: [] });
      expect(closed).toHaveBeenCalledTimes(1);
    });

    it('opens the analysis groups of a series batch, read from each series', () => {
      service.getRunSeries.mockImplementation((id: number) => of({ id, autoCreatedGroupId: id + 100 }) as never);
      open(batch({
        status: 'Completed', runsPerModel: 3, currentMemberIndex: null, completedMemberCount: 2,
        members: [completed(0, 61, { runId: null, seriesId: 7, runIds: [61, 62, 63] }), completed(1, 64, { runId: null, seriesId: 8, runIds: [64, 65, 66] })]
      }));
      const preset = vi.fn();
      component.openComparison.subscribe(preset);

      button('.mb-open-comparison')!.click();

      expect(service.getRunSeries).toHaveBeenCalledTimes(2);
      expect(preset).toHaveBeenCalledWith({ batteryRunIds: [], runIds: [], groupIds: [107, 108] });
    });
  });

  it('builds a battery batch\'s preset from its members\' battery runs', () => {
    const dto = batch({
      targetKind: 'Battery', status: 'Completed', currentMemberIndex: null,
      members: [completed(0, 61, { batteryRunId: 9 }), completed(1, 62, { batteryRunId: 10 }), member(2, { status: 'Failed', batteryRunId: 11 })]
    });
    expect(modelBatchComparisonPreset(dto)).toEqual({ batteryRunIds: [9, 10] });
  });

  it('warns of an instrument change before a member, and of a stall', () => {
    open(batch({
      stalled: true, stallMinutes: 15, lastProgressAtUtc: null, instrumentChangeAcknowledged: true,
      members: [batch().members[0], member(1, { instrumentDriftKeys: ['ToolGuidesSha256', 'HarnessVersion'] })]
    }));
    expect(text('.mb-instrument-changed')).toContain('Instrument changed before Model 2: ToolGuidesSha256, HarnessVersion');
    expect(text('.mb-not-comparable')).toBe('Not comparable across the change');
    expect(el().querySelector('.mb-instrument-shared')).toBeNull();
    expect(text('.mb-stall')).toBe('No progress for 15 min');
  });

  it('loads the diagnostics when opened, and copies them', async () => {
    open(batch());
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);

    const details = el().querySelector('details.mb-diagnostics') as HTMLDetailsElement;
    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();

    expect(service.getModelBatchDiagnostics).toHaveBeenCalledWith(21);
    expect(text('.mb-diagnostics-text')).toBe('BATCH id: 21');

    await component.copyDiagnostics();
    fixture.detectChanges();
    expect(writeText).toHaveBeenCalledWith('BATCH\n  id: 21');
    expect(text('.diagnostics-copy-status')).toBe('Copied the batch diagnostics.');
    expect(modelBatchDiagnosticsFileName(21)).toBe('model-batch-21-diagnostics.txt');
  });

  it('closes on Escape through the host', () => {
    open(batch());
    const closed = vi.fn();
    component.closed.subscribe(closed);

    const dialog = el().querySelector('dialog') as HTMLDialogElement;
    const cancel = new Event('cancel', { cancelable: true });
    dialog.dispatchEvent(cancel);

    expect(cancel.defaultPrevented).toBe(true);
    expect(closed).toHaveBeenCalledTimes(1);
    expect(dialog.open).toBe(false);
  });

  describe('polling', () => {
    it('polls every 2 s while live and stops once the batch is final', fakeAsync(() => {
      open(batch());
      expect(service.getModelBatch).toHaveBeenCalledTimes(1);

      tick(ModelBatchProgressDialogComponent.POLL_INTERVAL_MS);
      expect(service.getModelBatch).toHaveBeenCalledTimes(2);

      service.getModelBatch.mockReturnValue(of(batch({ status: 'Completed', isDriving: false, currentMemberIndex: null })));
      tick(ModelBatchProgressDialogComponent.POLL_INTERVAL_MS);
      fixture.detectChanges();
      expect(text('.mb-close')).toBe('Close');

      const polls = service.getModelBatch.mock.calls.length;
      tick(ModelBatchProgressDialogComponent.POLL_INTERVAL_MS * 3);
      expect(service.getModelBatch.mock.calls.length).toBe(polls);
      close();
      discardPeriodicTasks();
    }));

    it('backs off 4, 8 and 16 s after failures, and shows the error', fakeAsync(() => {
      vi.spyOn(console, 'error').mockReturnValue(undefined);
      service.getModelBatch.mockReturnValue(throwError(() => ({ status: 503 })));
      fixture.componentRef.setInput('modelBatchRunId', 21);
      fixture.componentRef.setInput('visible', true);
      fixture.detectChanges();
      expect(service.getModelBatch).toHaveBeenCalledTimes(1);
      expect(text('#mbLoadError')).toBe('Could not load the model batch.');

      tick(3999);
      expect(service.getModelBatch).toHaveBeenCalledTimes(1);
      tick(1);
      expect(service.getModelBatch).toHaveBeenCalledTimes(2);
      tick(8000);
      expect(service.getModelBatch).toHaveBeenCalledTimes(3);
      tick(16000);
      expect(service.getModelBatch).toHaveBeenCalledTimes(4);
      close();
      discardPeriodicTasks();
    }));

    it('stops polling when it closes', fakeAsync(() => {
      open(batch());
      close();
      const polls = service.getModelBatch.mock.calls.length;
      tick(ModelBatchProgressDialogComponent.POLL_INTERVAL_MS * 5);
      expect(service.getModelBatch.mock.calls.length).toBe(polls);
      discardPeriodicTasks();
    }));
  });
});
