import type { Mock, MockedObject } from "vitest";
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkBatteryAttachCandidateDto,
  BenchmarkBatteryLiveCostDto,
  BenchmarkBatteryMemberDto,
  BenchmarkBatteryRunDto,
  BenchmarkBatterySlotDto
} from '../../../services/admin-benchmark.service';
import { BenchmarkCostPanelComponent } from '../cost-panel/benchmark-cost-panel.component';
import { BenchmarkActiveRunMonitor } from '../state/benchmark-active-run.monitor';
import {
  BatteryProgressDialogComponent,
  batterySlotState
} from './battery-progress-dialog.component';
import { INDEX_WITHHELD_HINT } from './battery.models';

function dto<T>(value: object): T {
  return value as T;
}

function member(overrides: Partial<BenchmarkBatteryMemberDto> = {}): BenchmarkBatteryMemberDto {
  return dto<BenchmarkBatteryMemberDto>({
    memberId: 1,
    suiteIndex: 0,
    round: 1,
    runId: 101,
    runStatus: 'Completed',
    qualityIndex: 70,
    speedIndex: 55,
    origin: 'Launched',
    superseded: false,
    usable: true,
    unusableReason: null,
    guardFailure: null,
    addedAtUtc: '2026-10-01T10:00:00Z',
    runStartedAtUtc: '2026-10-01T10:00:00Z',
    runCompletedAtUtc: '2026-10-01T10:20:00Z',
    answeredQuestionCount: 10,
    totalQuestionCount: 10,
    ...overrides
  });
}

function slot(suiteIndex: number, round: number, occupant: BenchmarkBatteryMemberDto | null): BenchmarkBatterySlotDto {
  return dto<BenchmarkBatterySlotDto>({ suiteIndex, round, member: occupant });
}

function batteryRun(overrides: Partial<BenchmarkBatteryRunDto> = {}): BenchmarkBatteryRunDto {
  const completed = member({ memberId: 1, suiteIndex: 0, round: 1, runId: 101 });
  const withheld = member({
    memberId: 2, suiteIndex: 1, round: 1, runId: 102, runStatus: 'CompletedWithErrors',
    qualityIndex: null, usable: false, unusableReason: 'index withheld'
  });
  const running = member({
    memberId: 3, suiteIndex: 0, round: 2, runId: 103, runStatus: 'Running',
    qualityIndex: null, usable: false, unusableReason: 'run not finished', answeredQuestionCount: 4
  });
  return dto<BenchmarkBatteryRunDto>({
    id: 7,
    batteryId: 3,
    batteryName: 'Core Battery',
    definitionRevision: 2,
    definitionSha256: 'abcdef0123456789',
    weightingScheme: 'DifficultyMass',
    suites: [
      { index: 0, suiteId: 11, suiteName: 'Gameplay Help', customWeight: null },
      { index: 1, suiteId: 12, suiteName: 'Board Reading', customWeight: null }
    ],
    suiteCount: 2,
    runsPerSuite: 2,
    requestedMemberCount: 4,
    completedMemberCount: 1,
    failedMemberCount: 0,
    completedSuiteCount: 1,
    status: 'Running',
    stopReason: null,
    stopReasonText: null,
    allowCapWait: false,
    resumable: false,
    isDriving: true,
    startedAtUtc: '2026-10-01T10:00:00Z',
    completedAtUtc: null,
    lastProgressAtUtc: null,
    errorMessage: null,
    startedByUserName: 'admin',
    testedModelConfigurationId: 5,
    testedModelLabel: 'Model X',
    currentSuiteIndex: 0,
    currentSuitePosition: 1,
    currentSuiteName: 'Gameplay Help',
    currentRound: 2,
    currentRunId: 103,
    slots: [slot(0, 1, completed), slot(1, 1, withheld), slot(0, 2, running), slot(1, 2, null)],
    members: [completed, withheld, running],
    latestAnalysisId: null,
    latestAnalysisAtUtc: null,
    latestAnalysisComplete: null,
    overallIndex: null,
    overallIndexHalfWidth: null,
    overallIndexLower: null,
    overallIndexUpper: null,
    overallSpeedIndex: null,
    totalCost: null,
    analysisStale: false,
    analysisHasExcludedMembers: false,
    postRunWork: 'None',
    repairingRunIds: [],
    ...overrides
  });
}

/** A priced battery run's cost so far: the candidate and the grading roles add up to the total. */
function liveCost(overrides: Partial<BenchmarkBatteryLiveCostDto> = {}): BenchmarkBatteryLiveCostDto {
  return {
    total: 1.5,
    candidate: 0.4321,
    assessor: 0.6,
    coAssessor: null,
    secondOpinion: 0.2,
    claimVerifier: 0.1,
    synthesis: 0.1679,
    coSynthesis: null,
    grading: 1.0679,
    pricingIncomplete: false,
    pricingSource: 'Model catalog',
    pricedMemberCount: 3,
    reportWriterCostUsd: null,
    ...overrides
  };
}

describe('BatteryProgressDialogComponent', () => {
  let fixture: ComponentFixture<BatteryProgressDialogComponent>;
  let component: BatteryProgressDialogComponent;
  let service: MockedObject<AdminBenchmarkService>;
  let monitor: { armCompletionSignalsFromGesture: Mock };

  function open(run: BenchmarkBatteryRunDto): void {
    service.getBatteryRun.mockReturnValue(of(run));
    fixture.componentRef.setInput('batteryRunId', run.id);
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

  function text(selector: string): string {
    return (el().querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function button(selector: string): HTMLButtonElement | null {
    return el().querySelector(selector) as HTMLButtonElement | null;
  }

  function cell(suiteIndex: number, round: number): HTMLElement {
    const rows = el().querySelectorAll('.bp-grid tbody tr');
    return rows[suiteIndex].querySelectorAll('td.bp-cell')[round - 1] as HTMLElement;
  }

  beforeEach(async () => {
    service = {
      getBatteryRun: vi.fn().mockName("AdminBenchmarkService.getBatteryRun"),
      resumeBatteryRun: vi.fn().mockName("AdminBenchmarkService.resumeBatteryRun"),
      cancelBatteryRun: vi.fn().mockName("AdminBenchmarkService.cancelBatteryRun"),
      getBatteryAttachCandidates: vi.fn().mockName("AdminBenchmarkService.getBatteryAttachCandidates"),
      attachBatteryMember: vi.fn().mockName("AdminBenchmarkService.attachBatteryMember")
    } as unknown as MockedObject<AdminBenchmarkService>;
    service.getBatteryRun.mockReturnValue(of(batteryRun()));
    service.resumeBatteryRun.mockReturnValue(of({ batteryRunId: 7 }));
    service.cancelBatteryRun.mockReturnValue(of(void 0));
    monitor = {
      armCompletionSignalsFromGesture: vi.fn().mockName('BenchmarkActiveRunMonitor.armCompletionSignalsFromGesture')
    };

    await TestBed.configureTestingModule({
      imports: [BatteryProgressDialogComponent],
      providers: [
        { provide: AdminBenchmarkService, useValue: service },
        { provide: BenchmarkActiveRunMonitor, useValue: monitor }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(BatteryProgressDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    close();
  });

  it('creates without loading anything while hidden', () => {
    expect(component).toBeTruthy();
    expect(service.getBatteryRun).not.toHaveBeenCalled();
  });

  it('loads the battery run on open and shows the stage line', () => {
    open(batteryRun());
    expect(service.getBatteryRun).toHaveBeenCalledWith(7);
    expect(text('#bpDialogTitle')).toBe('Battery Run #7');
    expect(text('.bp-stage-line')).toBe('Running suite 1 of 2 · round 2 of 2: Gameplay Help');
  });

  it('shows one chip per suite and round, with the Index withheld hint', () => {
    open(batteryRun());
    expect(text('.bp-grid caption')).toBe('Members by suite and round');
    expect(cell(0, 1).querySelector('.job-status-chip')?.textContent?.trim()).toBe('Completed with index');
    expect(cell(1, 1).querySelector('.job-status-chip')?.textContent?.trim()).toBe('Index withheld');
    expect(cell(1, 1).textContent).toContain(INDEX_WITHHELD_HINT);
    expect(cell(0, 2).querySelector('.job-status-chip')?.textContent?.trim()).toBe('Running');
    expect(cell(0, 2).textContent).toContain('4 of 10 questions answered');
    expect(cell(1, 2).querySelector('.job-status-chip')?.textContent?.trim()).toBe('Pending');
  });

  it('has exactly one live region', () => {
    open(batteryRun());
    const live = el().querySelectorAll('[aria-live], [role="status"]');
    expect(live.length).toBe(1);
  });

  it('shows Instrument changed and offers only the re-run on that stop', () => {
    const guarded = member({
      memberId: 4, suiteIndex: 1, round: 1, runId: 104, usable: false,
      guardFailure: 'Harness version differs from the other members.'
    });
    const run = batteryRun({
      status: 'Stopped', stopReason: 'InstrumentChanged',
      stopReasonText: 'A member is not comparable with the others', resumable: true,
      runsPerSuite: 1, requestedMemberCount: 2,
      slots: [slot(0, 1, member()), slot(1, 1, guarded)],
      members: [member(), guarded]
    });
    open(run);

    expect(cell(1, 1).querySelector('.job-status-chip')?.textContent?.trim()).toBe('Instrument changed');
    expect(cell(1, 1).textContent).toContain('Harness version differs');
    expect(button('.bp-continue')).toBeNull();
    expect(button('.bp-cancel-battery')).not.toBeNull();
    expect(text('.bp-rerun')).toBe('Re-run under current instrument');

    const resumed = vi.fn().mockName('resumed');
    component.batteryResumed.subscribe(resumed);
    button('.bp-rerun')!.click();
    fixture.detectChanges();

    expect(service.resumeBatteryRun).toHaveBeenCalledWith(7, 'RerunUnderCurrentInstrument');
    expect(resumed).toHaveBeenCalledWith(7);
  });

  it('shows a superseded failed member as Failed and continues a stopped battery', () => {
    const failed = member({
      memberId: 5, suiteIndex: 1, round: 1, runId: 105, runStatus: 'Failed',
      superseded: true, usable: false, unusableReason: 'superseded', qualityIndex: null
    });
    const run = batteryRun({
      status: 'Stopped', stopReason: 'MemberFailed', stopReasonText: 'A member run failed.', resumable: true,
      runsPerSuite: 1, requestedMemberCount: 2,
      slots: [slot(0, 1, member()), slot(1, 1, null)],
      members: [member(), failed]
    });
    open(run);

    expect(cell(1, 1).querySelector('.job-status-chip')?.textContent?.trim()).toBe('Failed');
    expect(cell(1, 1).textContent).toContain('#105');
    expect(button('.bp-rerun')).toBeNull();
    expect(text('.bp-continue')).toBe('Continue — A member run failed.');
    expect(button('.bp-continue')!.querySelector('svg.btn-icon polygon')?.getAttribute('points')).toBe('5 3 19 12 5 21 5 3');

    button('.bp-continue')!.click();
    fixture.detectChanges();
    expect(service.resumeBatteryRun).toHaveBeenCalledWith(7, 'Continue');
  });

  describe('a member of a model batch', () => {
    const owned = (): BenchmarkBatteryRunDto => batteryRun({
      status: 'Stopped', stopReason: 'MemberFailed', stopReasonText: 'A member run failed.', resumable: true, isDriving: false,
      runsPerSuite: 1, requestedMemberCount: 2, slots: [slot(0, 1, member()), slot(1, 1, null)], members: [member()],
      modelBatchRunId: 21
    });

    let getModelBatch: Mock;

    beforeEach(() => {
      getModelBatch = vi.fn(() => of({ id: 21, status: 'Stopped' }));
      (service as unknown as { getModelBatch: Mock }).getModelBatch = getModelBatch;
    });

    it('says the batch owns it and keeps Continue and Cancel Battery waiting with the reason', () => {
      open(owned());

      expect(getModelBatch).toHaveBeenCalledWith(21);
      expect(text('.bp-batch-notice')).toBe('Part of model batch #21. Open model batch #21');
      expect(text('#bpBatchOwnedReason')).toBe('Use the model batch\'s progress dialog.');
      for (const selector of ['.bp-continue', '.bp-cancel-battery']) {
        expect(button(selector)!.getAttribute('aria-disabled')).toBe('true');
        expect(button(selector)!.getAttribute('aria-describedby')).toBe('bpBatchOwnedReason');
      }

      button('.bp-continue')!.click();
      button('.bp-cancel-battery')!.click();
      expect(service.resumeBatteryRun).not.toHaveBeenCalled();
      expect(service.cancelBatteryRun).not.toHaveBeenCalled();
      expect(monitor.armCompletionSignalsFromGesture).not.toHaveBeenCalled();
    });

    it('opens the batch from the notice, closing first', () => {
      open(owned());
      const closed = vi.fn();
      const openBatch = vi.fn();
      component.closed.subscribe(closed);
      component.openModelBatch.subscribe(openBatch);

      button('.bp-open-batch')!.click();

      expect(closed).toHaveBeenCalledTimes(1);
      expect(openBatch).toHaveBeenCalledWith(21);
    });

    it('lifts the gate when the batch is final', () => {
      getModelBatch.mockReturnValue(of({ id: 21, status: 'Cancelled' }));
      open(owned());

      expect(el().querySelector('.bp-batch-notice')).toBeNull();
      expect(button('.bp-continue')!.getAttribute('aria-disabled')).toBeNull();
    });

    it('reads Back to Batch when opened from the batch\'s dialog', () => {
      fixture.componentRef.setInput('returnsToModelBatch', true);
      open(batteryRun());

      expect(text('.bp-back-to-batch')).toBe('Back to Batch');
      expect(el().querySelector('.dialog-footer')?.textContent).not.toContain('Run in Background');
    });
  });

  it('offers the re-run after Continue is refused with 409 for a moved instrument', () => {
    const run = batteryRun({
      status: 'Stopped', stopReason: 'MemberFailed', resumable: true,
      runsPerSuite: 1, requestedMemberCount: 2,
      slots: [slot(0, 1, member()), slot(1, 1, null)], members: [member()]
    });
    open(run);
    service.resumeBatteryRun.mockReturnValue(throwError(() => ({
      status: 409,
      error: { message: 'The harness version changed.', instrumentChanged: true, changedInstrumentHashes: ['ScoringMethodVersion'] }
    })));

    button('.bp-continue')!.click();
    fixture.detectChanges();

    expect(text('.bp-action-error')).toBe('The harness version changed.');
    expect(button('.bp-continue')).toBeNull();
    expect(button('.bp-rerun')).not.toBeNull();
  });

  it('shows a plain 409 on Continue as its message alone, offering no re-run and emitting nothing', () => {
    const run = batteryRun({
      status: 'Stopped', stopReason: 'MemberFailed', stopReasonText: 'A member run failed.', resumable: true,
      runsPerSuite: 1, requestedMemberCount: 2,
      slots: [slot(0, 1, member()), slot(1, 1, null)], members: [member()]
    });
    open(run);
    const resumed = vi.fn().mockName('resumed');
    component.batteryResumed.subscribe(resumed);
    service.resumeBatteryRun.mockReturnValue(throwError(() => ({
      status: 409, error: { message: 'Another run is using the run slot.' }
    })));

    button('.bp-continue')!.click();
    fixture.detectChanges();

    expect(text('.bp-action-error')).toBe('Another run is using the run slot.');
    expect(component.resumeRefusedForInstrument).toBe(false);
    expect(button('.bp-rerun')).toBeNull();
    expect(text('.bp-continue')).toBe('Continue — A member run failed.');
    expect(button('.bp-continue')!.getAttribute('aria-disabled')).toBeNull();
    // batteryResumed means a 2xx: the host counts the battery run as one this page signals only then.
    expect(resumed).not.toHaveBeenCalled();
  });

  it('arms the completion signals inside the Continue click, before the request and the batteryResumed emit', () => {
    const run = batteryRun({
      status: 'Stopped', stopReason: 'MemberFailed', resumable: true,
      runsPerSuite: 1, requestedMemberCount: 2,
      slots: [slot(0, 1, member()), slot(1, 1, null)], members: [member()]
    });
    open(run);
    const resumed = vi.fn().mockName('resumed');
    component.batteryResumed.subscribe(resumed);

    button('.bp-continue')!.click();

    expect(monitor.armCompletionSignalsFromGesture).toHaveBeenCalledTimes(1);
    expect(resumed).toHaveBeenCalledWith(7);
    const armedAt = monitor.armCompletionSignalsFromGesture.mock.invocationCallOrder[0];
    expect(armedAt).toBeLessThan(service.resumeBatteryRun.mock.invocationCallOrder[0]);
    expect(armedAt).toBeLessThan(resumed.mock.invocationCallOrder[0]);
  });

  it('cancels a running battery', () => {
    open(batteryRun());
    const canceled = vi.fn().mockName('canceled');
    component.batteryCanceled.subscribe(canceled);

    button('.bp-cancel-battery')!.click();
    fixture.detectChanges();

    expect(service.cancelBatteryRun).toHaveBeenCalledWith(7);
    expect(canceled).toHaveBeenCalledWith(7);
  });

  it('opens a member run progress and closes itself', () => {
    open(batteryRun());
    const runProgress = vi.fn().mockName('runProgress');
    const closed = vi.fn().mockName('closed');
    component.openRunProgress.subscribe(runProgress);
    component.closed.subscribe(closed);

    (cell(0, 2).querySelector('.bp-open-run') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(closed).toHaveBeenCalled();
    expect(runProgress).toHaveBeenCalledWith({ runId: 103, batteryRunId: 7 });
  });

  it('names the battery run in the hand-off even when the host drops it as the dialog closes', () => {
    open(batteryRun());
    const runProgress = vi.fn().mockName('runProgress');
    component.openRunProgress.subscribe(runProgress);
    component.closed.subscribe(() => fixture.componentRef.setInput('batteryRunId', null));

    (cell(0, 1).querySelector('.bp-open-run') as HTMLButtonElement).click();

    expect(runProgress).toHaveBeenCalledWith({ runId: 101, batteryRunId: 7 });
  });

  describe('the stage rail and post-run work', () => {
    const railItems = (): HTMLElement[] => Array.from(el().querySelectorAll('.bp-rail .run-stage'));
    const stageNote = (item: HTMLElement): string => (item.querySelector('.run-stage-note')?.textContent ?? '').trim();

    /** A battery run that finished just now, both suites usable. */
    function finishedRun(overrides: Partial<BenchmarkBatteryRunDto> = {}): BenchmarkBatteryRunDto {
      const first = member({ memberId: 1, suiteIndex: 0, round: 1, runId: 101 });
      const second = member({ memberId: 2, suiteIndex: 1, round: 1, runId: 102 });
      return batteryRun({
        status: 'Completed', completedAtUtc: new Date().toISOString(), completedSuiteCount: 2,
        runsPerSuite: 1, requestedMemberCount: 2, completedMemberCount: 2, isDriving: false,
        currentSuiteIndex: null, currentSuitePosition: null, currentSuiteName: null, currentRound: null, currentRunId: null,
        slots: [slot(0, 1, first), slot(1, 1, second)], members: [first, second],
        latestAnalysisId: 31, overallIndex: 84.94, testedProvider: 'OpenAI', testedThinkingLevel: 'high',
        ...overrides
      });
    }

    /** A finished battery run with a report writer; Pending (1) and Writing (2) are the server's WritingReports. */
    const withWriter = (status: number, overrides: Partial<BenchmarkBatteryRunDto> = {}): BenchmarkBatteryRunDto =>
      finishedRun({
        reportWriterModelConfigurationId: 3, reportDocumentsStatus: status,
        postRunWork: status === 1 || status === 2 ? 'WritingReports' : 'None',
        ...overrides
      });

    beforeEach(() => {
      service.getBatteryReportJob = vi.fn().mockName('AdminBenchmarkService.getBatteryReportJob').mockReturnValue(of(null)) as any;
    });

    it('has two stages without a report writer, the suite runs current while the battery runs', () => {
      open(batteryRun());

      const items = railItems();
      expect(items.length).toBe(2);
      expect(items.map(item => item.querySelector('.run-stage-name')?.textContent?.trim()))
        .toEqual(['Suite runs', 'Battery analysis']);
      expect(items[0].classList).toContain('is-current');
      expect(items[0].getAttribute('aria-current')).toBe('step');
      // Runs 101 and 102 have finished; 103 is still running.
      expect(stageNote(items[0])).toBe('2 of 4 runs');
      expect(items[1].classList).not.toContain('is-current');
      expect(el().querySelector('.bp-report-writer')).toBeNull();
    });

    it('has three stages with a report writer, and names the writer and the model in the header', () => {
      open(withWriter(3));

      const items = railItems();
      expect(items.length).toBe(3);
      expect(items[2].querySelector('.run-stage-name')?.textContent?.trim()).toBe('AI-written reports');
      expect(text('.bp-report-writer .model-name')).toBe('Configuration #3');
      expect(text('.bp-model-line .model-name')).toBe('Model X');
      expect(el().querySelector('.bp-model-line app-provider-badge')).not.toBeNull();
      expect(el().querySelector('.bp-model-line .thinking-badge')).not.toBeNull();
    });

    it('badges the report writer exactly as the model under test, from the battery run\'s writer fields', () => {
      open(withWriter(3, {
        reportWriterDisplayName: 'Writer GPT', reportWriterProvider: 'OpenAI', reportWriterModelId: 'gpt-writer',
        reportWriterThinkingLevel: 'high', reportWriterReasoningMode: null, reportWriterServiceTier: null
      }));

      const writer = el().querySelector('.bp-report-writer') as HTMLElement;
      expect(text('.bp-report-writer .model-name')).toBe('Writer GPT');
      expect(writer.querySelector('app-provider-badge')).not.toBeNull();
      const thinking = writer.querySelector('.thinking-badge') as HTMLElement;
      expect(thinking).not.toBeNull();
      expect(thinking.querySelector('.visually-hidden')?.textContent).toBe('thinking level ');
      // The writer's badges follow runFactBadges order, as the model under test's do.
      const kinds = Array.from(writer.querySelectorAll('dd > *')).map(node => node.tagName === 'APP-PROVIDER-BADGE' ? 'provider' : node.className);
      expect(kinds).toEqual(['model-name', 'thinking-badge', 'provider']);
    });

    it('badges the model under test with its requested service tier', () => {
      open(finishedRun({ testedServiceTier: 'flex' }));
      const tier = el().querySelector('.bp-model-line .config-badge') as HTMLElement;
      expect(tier).not.toBeNull();
      expect(tier.querySelector('.visually-hidden')?.textContent).toBe('service tier ');
    });

    it('counts the written documents from the battery run, not the report job', () => {
      (service.getBatteryReportJob as any).mockReturnValue(of({ runId: 7, writerConfigId: 3, job: { documents: [] } }));
      open(withWriter(3, { reportDocumentsWrittenCount: 2 }));
      expect(stageNote(railItems()[2])).toBe('2 documents written');

      close();
      open(withWriter(3, { id: 8, reportDocumentsWrittenCount: 1 }));
      expect(stageNote(railItems()[2])).toBe('1 document written');

      close();
      open(withWriter(3, { id: 9, reportDocumentsWrittenCount: 0 }));
      expect(stageNote(railItems()[2])).toBe('Written');
    });

    it('shows the Overall Index tile once the analysis is done, and not before', () => {
      open(finishedRun({ overallIndexHalfWidth: 3.6 }));
      const tile = el().querySelector('.run-stat-strip .bp-stat-index') as HTMLElement;
      expect(tile).not.toBeNull();
      expect((tile.querySelector('dt')?.textContent ?? '').trim()).toBe('Overall Index');
      const badge = tile.querySelector('.index-badge') as HTMLElement;
      expect(badge.classList).toContain('index-badge-md');
      expect((badge.textContent ?? '').replace(/\s+/g, ' ').trim()).toBe('Intelligence Index 85 ± 4, out of 100');
      // The rail's note stays text.
      expect(stageNote(railItems()[1])).toBe('Overall Index 84.9');

      close();
      open(finishedRun({ id: 8, latestAnalysisId: null, overallIndex: null }));
      expect(el().querySelector('.bp-stat-index')).toBeNull();
    });

    it('marks the analysis done with the Overall Index', () => {
      open(finishedRun());

      const items = railItems();
      expect(items[0].classList).toContain('is-done');
      expect(stageNote(items[0])).toBe('2 of 2 runs');
      expect(items[1].classList).toContain('is-done');
      expect(stageNote(items[1])).toBe('Overall Index 84.9');
      expect(items[1].querySelector('.visually-hidden')?.textContent?.trim()).toBe('(done)');
    });

    it('shows the analysis current while postRunWork is Analysing, and ended once it is None without one', fakeAsync(() => {
      open(finishedRun({ latestAnalysisId: null, overallIndex: null, postRunWork: 'Analysing' }));

      expect(railItems()[1].classList).toContain('is-current');
      expect(stageNote(railItems()[1])).toBe('Computing the Overall Index…');
      expect(text('.bp-stage-line')).toBe('Computing the battery analysis…');

      service.getBatteryRun.mockReturnValue(of(finishedRun({ latestAnalysisId: null, overallIndex: null, postRunWork: 'None' })));
      tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS);
      fixture.detectChanges();
      expect(railItems()[1].classList).toContain('is-ended');
      expect(stageNote(railItems()[1])).toBe('Not computed: use Recompute in the Battery Run Report');

      const polls = service.getBatteryRun.mock.calls.length;
      tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS * 3);
      expect(service.getBatteryRun.mock.calls.length).toBe(polls);
      close();
      discardPeriodicTasks();
    }));

    it('reads a missing postRunWork, from an older server, as none: no grace window keeps it polling', fakeAsync(() => {
      const older = finishedRun({ latestAnalysisId: null, overallIndex: null }) as unknown as Record<string, unknown>;
      delete older['postRunWork'];
      open(older as unknown as BenchmarkBatteryRunDto);

      expect(railItems()[1].classList).toContain('is-ended');
      tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS * 3);
      expect(service.getBatteryRun).toHaveBeenCalledTimes(1);
      close();
      discardPeriodicTasks();
    }));

    it('keeps the reports stage current only while postRunWork is WritingReports', () => {
      open(withWriter(0, { postRunWork: 'None' }));
      expect(railItems()[2].classList).toContain('is-ended');
      expect(stageNote(railItems()[2])).toBe('Not started');
      expect(service.getBatteryReportJob).not.toHaveBeenCalled();
    });

    it('marks the runs stage current and the later stages pending while a member is re-run', () => {
      open(withWriter(3, { status: 'CompletedWithErrors', postRunWork: 'Repairing', repairingRunIds: [102] }));

      const items = railItems();
      expect(items[0].classList).toContain('is-current');
      expect(stageNote(items[0])).toBe('Re-run in progress · 2 of 2 runs');
      expect(items[1].classList).not.toContain('is-current');
      expect(stageNote(items[1])).toBe('Follows the re-run');
      expect(stageNote(items[2])).toBe('Follows the analysis');
      expect(text('.bp-stage-line')).toBe('Re-run in progress: run #102');
    });

    it('shows the reports current while they are written, polls the job, and keeps polling the battery run', fakeAsync(() => {
      open(withWriter(2));

      const items = railItems();
      expect(items[2].classList).toContain('is-current');
      expect(items[2].getAttribute('aria-current')).toBe('step');
      expect(stageNote(items[2])).toBe('Writing the AI-written reports');
      expect(text('.bp-stage-line')).toBe('Writing the AI reports…');
      expect(service.getBatteryReportJob).toHaveBeenCalledWith(7);

      tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS);
      expect(service.getBatteryRun).toHaveBeenCalledTimes(2);
      tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS);
      expect(service.getBatteryRun).toHaveBeenCalledTimes(3);
      close();
      discardPeriodicTasks();
    }));

    it('names the queue position while the job waits for the writer', () => {
      (service.getBatteryReportJob as any).mockReturnValue(of({ runId: 7, jobsAhead: 2, writerConfigId: 3, writerDisplayName: 'Writer Model' }));
      open(withWriter(1));

      expect(stageNote(railItems()[2])).toBe('Waiting for the report writer (2 jobs ahead)');
      expect(text('.bp-report-writer .model-name')).toBe('Writer Model');
    });

    it('stops polling once the reports are written, and says so in the stage line', fakeAsync(() => {
      open(withWriter(3));

      expect(railItems()[2].classList).toContain('is-done');
      expect(text('.bp-stage-line')).toBe('Completed: 2 of 2 suites · reports written');
      tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS * 3);
      expect(service.getBatteryRun).toHaveBeenCalledTimes(1);
      close();
      discardPeriodicTasks();
    }));

    it('shows a failed reports stage with its message', () => {
      open(withWriter(5, { reportDocumentsMessage: 'The writer refused.' }));

      const item = railItems()[2];
      expect(item.classList).toContain('is-ended');
      expect(stageNote(item)).toBe('Failed: The writer refused.');
      expect(item.querySelector('.visually-hidden')?.textContent?.trim()).toBe('(ended)');
      expect(text('.bp-stage-line')).toBe('Completed: 2 of 2 suites · reports failed');
    });

    it('keeps exactly one live region through the post-run stages', () => {
      open(withWriter(2));
      expect(el().querySelectorAll('[aria-live], [role="status"]').length).toBe(1);
    });

    it('marks the suite runs ended and the later stages not reached for a canceled battery run', () => {
      open(finishedRun({ status: 'Cancelled', completedAtUtc: '2026-10-01T12:00:00Z', reportWriterModelConfigurationId: 3 }));

      const items = railItems();
      expect(items[0].classList).toContain('is-ended');
      expect(stageNote(items[0])).toBe('Canceled at 2 of 2 runs');
      expect(stageNote(items[1])).toBe('Not reached');
      expect(stageNote(items[2])).toBe('Not reached');
    });
  });

  it('offers Open Analysis once finished, closing and emitting the battery run id for the Battery Run Report', () => {
    const run = batteryRun({
      status: 'Completed', completedAtUtc: '2026-10-01T12:00:00Z', completedSuiteCount: 2,
      runsPerSuite: 1, requestedMemberCount: 2, completedMemberCount: 2,
      slots: [slot(0, 1, member()), slot(1, 1, member({ memberId: 9, suiteIndex: 1, runId: 109 }))],
      members: [member(), member({ memberId: 9, suiteIndex: 1, runId: 109 })]
    });
    open(run);
    const analysis = vi.fn().mockName('analysis');
    const closed = vi.fn().mockName('closed');
    component.openAnalysis.subscribe(analysis);
    component.closed.subscribe(closed);

    expect(button('.bp-cancel-battery')).toBeNull();
    button('.bp-open-analysis')!.click();
    fixture.detectChanges();

    expect(closed).toHaveBeenCalled();
    expect(analysis).toHaveBeenCalledTimes(1);
    expect(analysis).toHaveBeenCalledWith(7);
  });

  it('keeps polling a finished battery run through Repairing, and stops once postRunWork is None', fakeAsync(() => {
    const repairing = (postRunWork: 'Repairing' | 'None') => batteryRun({
      status: 'CompletedWithErrors', isDriving: false, currentRunId: null, currentSuiteIndex: null, currentRound: null,
      postRunWork, repairingRunIds: postRunWork === 'Repairing' ? [103] : []
    });
    open(repairing('Repairing'));
    expect(service.getBatteryRun).toHaveBeenCalledTimes(1);

    tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS);
    expect(service.getBatteryRun).toHaveBeenCalledTimes(2);

    service.getBatteryRun.mockReturnValue(of(repairing('None')));
    tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS);
    expect(service.getBatteryRun).toHaveBeenCalledTimes(3);

    tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS * 3);
    expect(service.getBatteryRun).toHaveBeenCalledTimes(3);

    close();
    discardPeriodicTasks();
  }));

  it('polls while the battery run is live and stops once it is not', fakeAsync(() => {
    open(batteryRun());
    expect(service.getBatteryRun).toHaveBeenCalledTimes(1);

    tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS);
    expect(service.getBatteryRun).toHaveBeenCalledTimes(2);

    service.getBatteryRun.mockReturnValue(of(batteryRun({ status: 'Completed' })));
    tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS);
    expect(service.getBatteryRun).toHaveBeenCalledTimes(3);

    tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS * 3);
    expect(service.getBatteryRun).toHaveBeenCalledTimes(3);

    close();
    discardPeriodicTasks();
  }));

  describe('member cells', () => {
    it('shows a running member\'s run, stage, progress and elapsed time', () => {
      const running = member({
        memberId: 3, suiteIndex: 0, round: 2, runId: 103, runStatus: 'Running', qualityIndex: null,
        usable: false, stage: 'Verifying', answeredQuestionCount: 13, totalQuestionCount: 18,
        runStartedAtUtc: new Date(Date.now() - 372_000).toISOString(), runCompletedAtUtc: null
      });
      const base = batteryRun();
      open(batteryRun({ slots: [base.slots[0], base.slots[1], slot(0, 2, running), base.slots[3]] }));

      const runningCell = cell(0, 2);
      expect(text('.bp-grid tbody tr:first-child td:nth-of-type(2) .bp-member-stage'))
        .toBe('Run #103 · Stage 2 of 3 — Follow-up grading passes');
      const progress = runningCell.querySelector('progress.bp-member-progress') as HTMLProgressElement;
      expect(progress.max).toBe(18);
      expect(progress.value).toBe(13);
      const label = el().querySelector('#' + progress.getAttribute('aria-labelledby')) as HTMLElement;
      expect(label.textContent?.trim()).toBe('13 of 18 questions answered');
      expect((runningCell.querySelector('.bp-member-elapsed')?.textContent ?? '').trim()).toBe('Elapsed 6m 12s');
    });

    it('says a running member is starting until its first answer, then answering', () => {
      const starting = member({
        memberId: 3, suiteIndex: 0, round: 2, runId: 103, runStatus: 'Running', qualityIndex: null,
        usable: false, stage: null, answeredQuestionCount: 0
      });
      const base = batteryRun();
      open(batteryRun({ slots: [base.slots[0], base.slots[1], slot(0, 2, starting), base.slots[3]] }));
      expect((cell(0, 2).querySelector('.bp-member-stage')?.textContent ?? '').replace(/\s+/g, ' ').trim())
        .toBe('Run #103 · Starting');

      // The default fixture's running member has answered 4 questions without a stage.
      close();
      open(batteryRun({ id: 8 }));
      expect((cell(0, 2).querySelector('.bp-member-stage')?.textContent ?? '').replace(/\s+/g, ' ').trim())
        .toBe('Run #103 · Stage 1 of 3 — Answering and grading');
    });

    it('shows a finished member\'s index badge, its facts line and its run', () => {
      const finished = member({
        memberId: 1, suiteIndex: 0, round: 1, runId: 101, qualityIndex: 85, qualityIndexHalfWidth: 4.2,
        speedIndex: 71, durationMs: 820_000, claimsRefutedCount: 0, advisoryFlagAnswerCount: 3
      });
      const base = batteryRun();
      open(batteryRun({ slots: [slot(0, 1, finished), base.slots[1], base.slots[2], base.slots[3]] }));

      const finishedCell = cell(0, 1);
      const badge = finishedCell.querySelector('app-index-badge .index-badge') as HTMLElement;
      expect((badge.textContent ?? '').replace(/\s+/g, ' ').trim()).toBe('Intelligence Index 85 ± 4, out of 100');
      expect(badge.classList).toContain('badge-score-high');
      expect((finishedCell.querySelector('.bp-member-facts')?.textContent ?? '').trim())
        .toBe('Speed 71 · 13m 40s · 0 refuted claims · 3 flagged answers');
      expect(finishedCell.textContent).toContain('Run #101');
    });

    it('leaves the flagged answers out of the facts line when there are none', () => {
      const finished = member({ speedIndex: 55, durationMs: 9_400, claimsRefutedCount: 1, advisoryFlagAnswerCount: 0 });
      const base = batteryRun();
      open(batteryRun({ slots: [slot(0, 1, finished), base.slots[1], base.slots[2], base.slots[3]] }));
      expect((cell(0, 1).querySelector('.bp-member-facts')?.textContent ?? '').trim())
        .toBe('Speed 55 · 9s · 1 refuted claim');
    });

    it('ends the facts line with the member\'s estimated cost', () => {
      const cheap = member({ speedIndex: 55, durationMs: 9_400, claimsRefutedCount: 1, advisoryFlagAnswerCount: 0, estimatedCost: 0.421 });
      const base = batteryRun();
      open(batteryRun({ slots: [slot(0, 1, cheap), base.slots[1], base.slots[2], base.slots[3]] }));
      expect((cell(0, 1).querySelector('.bp-member-facts')?.textContent ?? '').trim())
        .toBe('Speed 55 · 9s · 1 refuted claim · est. $0.4210');

      const dear = member({ speedIndex: 55, durationMs: 9_400, claimsRefutedCount: 1, advisoryFlagAnswerCount: 0, estimatedCost: 2.5 });
      close();
      open(batteryRun({ id: 8, slots: [slot(0, 1, dear), base.slots[1], base.slots[2], base.slots[3]] }));
      expect((cell(0, 1).querySelector('.bp-member-facts')?.textContent ?? '').trim())
        .toBe('Speed 55 · 9s · 1 refuted claim · est. $2.50');
    });

    it('draws a running member\'s bar as a job progress bar across the whole cell', () => {
      open(batteryRun());
      const progress = cell(0, 2).querySelector('progress.bp-member-progress') as HTMLProgressElement;
      expect(progress.classList).toContain('job-progress');
      const style = getComputedStyle(progress);
      // `display: block` proves the component's style sheet applies, so `none` is not a default.
      expect(style.display).toBe('block');
      expect(style.maxInlineSize).toBe('none');
      expect(style.maxWidth).toBe('none');
    });

    it('shows Re-run in progress on a member cell whose run is being re-run', () => {
      open(batteryRun({
        status: 'CompletedWithErrors', isDriving: false, currentRunId: null, currentSuiteIndex: null, currentRound: null,
        postRunWork: 'Repairing', repairingRunIds: [103]
      }));

      const repaired = cell(0, 2);
      expect(repaired.querySelector('.job-status-chip')?.textContent?.trim()).toBe('Re-run in progress');
      expect(repaired.getAttribute('data-repairing')).toBe('true');
      expect(repaired.classList).toContain('is-current');
      expect(cell(0, 1).querySelector('.job-status-chip')?.textContent?.trim()).toBe('Completed with index');
      expect(cell(0, 1).getAttribute('data-repairing')).toBeNull();
    });

    it('says what a pending slot of a live battery run waits for', () => {
      open(batteryRun());
      expect((cell(1, 2).querySelector('.bp-cell-waiting')?.textContent ?? '').trim()).toBe('Waiting for suite 1');
    });

    it('keeps a single live region with running and finished members shown', () => {
      open(batteryRun());
      expect(el().querySelectorAll('[aria-live], [role="status"]').length).toBe(1);
    });
  });

  describe('mean answer and cost', () => {
    const tile = (label: string): HTMLElement => {
      const stat = Array.from(el().querySelectorAll<HTMLElement>('.run-stat-strip .run-stat'))
        .find(node => (node.querySelector('dt')?.textContent ?? '').trim() === label);
      expect(stat, label).toBeTruthy();
      return stat!;
    };
    /** The tile's value without its sub-note. */
    const tileValue = (label: string): string => {
      const dd = tile(label).querySelector('dd')!.cloneNode(true) as HTMLElement;
      dd.querySelectorAll('.bp-stat-note').forEach(node => node.remove());
      return (dd.textContent ?? '').replace(/\s+/g, ' ').trim();
    };
    const tileNote = (label: string): string | null =>
      tile(label).querySelector('.bp-stat-note')?.textContent?.trim() ?? null;
    const expectNotAvailable = (label: string): void => {
      const dd = tile(label).querySelector('dd')!;
      expect(dd.querySelector('[aria-hidden="true"]')?.textContent, label).toBe('—');
      expect(dd.querySelector('.visually-hidden')?.textContent?.trim(), label).toBe('not available yet');
    };
    const costPanel = (): BenchmarkCostPanelComponent =>
      fixture.debugElement.query(By.css('.bp-cost-details app-benchmark-cost-panel')).componentInstance as BenchmarkCostPanelComponent;

    it('places the three tiles after Failed and before the Overall Index', () => {
      open(batteryRun({ liveCost: liveCost(), meanModelTimeMs: 23_400, modelTimedAnswerCount: 14 }));
      const labels = Array.from(el().querySelectorAll('.run-stat-strip .run-stat dt')).map(dt => dt.textContent?.trim());
      expect(labels).toEqual(['Status', 'Elapsed', 'Suites complete', 'Usable slots', 'Failed', 'Mean answer', 'Candidate cost', 'Total cost']);
    });

    it('shows the mean model time, the candidate cost and the total so far while the battery runs', () => {
      open(batteryRun({ liveCost: liveCost(), meanModelTimeMs: 23_400, modelTimedAnswerCount: 14 }));

      expect(tileValue('Mean answer')).toBe('23.4 s');
      expect(tileNote('Mean answer')).toBe('model time, tools excluded');
      expect(tileValue('Candidate cost')).toBe('$0.4321');
      expect(tileNote('Candidate cost')).toBeNull();
      expect(tileValue('Total cost')).toBe('$1.50');
      expect(tileNote('Total cost')).toBe('so far');
    });

    it('shows a dash with a hidden "not available yet" for every value it does not have', () => {
      open(batteryRun());

      for (const label of ['Mean answer', 'Candidate cost', 'Total cost']) {
        expectNotAvailable(label);
      }
      expect(tileNote('Mean answer')).toBe('model time, tools excluded');
      expect(tileNote('Total cost')).toBeNull();
    });

    it('shows no total while pricing is incomplete, but still the candidate cost', () => {
      open(batteryRun({ liveCost: liveCost({ total: null, pricingIncomplete: true }) }));

      expect(tileValue('Candidate cost')).toBe('$0.4321');
      expectNotAvailable('Total cost');
      expect(tileNote('Total cost')).toBeNull();
    });

    it('adds the stored report writer cost to the total of a finished battery run', () => {
      open(batteryRun({
        status: 'Completed', completedAtUtc: '2026-10-01T12:00:00Z', isDriving: false,
        reportWriterModelConfigurationId: 3, reportDocumentsStatus: 3,
        liveCost: liveCost({ reportWriterCostUsd: 0.25 })
      }));

      expect(tileValue('Total cost')).toBe('$1.75');
      expect(tileNote('Total cost')).toBe('incl. report writer');
    });

    it('adds the report job\'s running cost while the reports are written', () => {
      service.getBatteryReportJob = vi.fn().mockName('AdminBenchmarkService.getBatteryReportJob')
        .mockReturnValue(of({ runId: 7, writerConfigId: 3, job: { documents: [], costUsd: 0.1 } })) as any;
      open(batteryRun({
        status: 'Completed', completedAtUtc: new Date().toISOString(), isDriving: false,
        reportWriterModelConfigurationId: 3, reportDocumentsStatus: 2, postRunWork: 'WritingReports',
        liveCost: liveCost({ reportWriterCostUsd: null })
      }));
      fixture.detectChanges();

      expect(service.getBatteryReportJob).toHaveBeenCalledWith(7);
      expect(tileValue('Total cost')).toBe('$1.60');
      expect(tileNote('Total cost')).toBe('incl. report writer');
      expect(costPanel().reportWriter).toBe(0.1);
    });

    it('puts the cost by role in a closed disclosure holding the live cost panel', () => {
      open(batteryRun({ liveCost: liveCost() }));

      const details = el().querySelector('details.gh-disclosure.bp-cost-details') as HTMLDetailsElement;
      expect(details).not.toBeNull();
      expect(details.open).toBe(false);
      expect(details.hasAttribute('open')).toBe(false);
      expect((details.querySelector('summary')?.textContent ?? '').trim()).toBe('Cost by role');
      expect(details.querySelector('.gh-disclosure-body app-benchmark-cost-panel')).not.toBeNull();

      const panel = costPanel();
      expect(panel.variant).toBe('live');
      expect(panel.total).toBe(1.5);
      expect(panel.candidate).toBe(0.4321);
      expect(panel.grading).toBe(1.0679);
      expect(panel.pricingSource).toBe('Model catalog');
      expect(panel.pricingIncomplete).toBe(false);
      expect(panel.panel).toBe(false);
      expect(panel.reportWriter).toBeNull();
    });

    it('marks the cost panel a panel run when the battery run has a co-assessor', () => {
      open(batteryRun({ liveCost: liveCost({ coAssessor: 0.3, coSynthesis: 0.05 }), coAssessorLabel: 'Assessor B' }));
      expect(costPanel().panel).toBe(true);
      expect(costPanel().coAssessor).toBe(0.3);
    });

    it('leaves the disclosure out while there is no live cost', () => {
      open(batteryRun());
      expect(el().querySelector('.bp-cost-details')).toBeNull();
      expect(el().querySelector('app-benchmark-cost-panel')).toBeNull();
    });
  });

  describe('elapsed time', () => {
    const elapsedText = (): string => {
      const stat = Array.from(el().querySelectorAll('.run-stat'))
        .find(node => (node.querySelector('dt')?.textContent ?? '').trim() === 'Elapsed');
      return (stat?.querySelector('dd')?.textContent ?? '').trim();
    };

    it('reads in the other progress dialogs\' format, never in milliseconds or with a decimal', () => {
      const finished = (id: number, completedAtUtc: string) => batteryRun({
        id, status: 'Completed', startedAtUtc: '2026-10-01T10:00:00Z', completedAtUtc,
        currentSuiteIndex: null, currentSuitePosition: null, currentRound: null, currentRunId: null
      });

      open(finished(7, '2026-10-01T10:00:09.400Z'));
      expect(elapsedText()).toBe('9s');

      close();
      open(finished(8, '2026-10-01T10:17:15.000Z'));
      expect(elapsedText()).toBe('17m 15s');

      close();
      open(finished(9, '2026-10-01T11:02:05.000Z'));
      expect(elapsedText()).toBe('1h 02m 05s');

      close();
      open(finished(10, '2026-10-01T10:00:00.850Z'));
      expect(elapsedText()).toBe('0s');
      expect(elapsedText()).not.toMatch(/ms|\.|min/);
    });

    it('advances by one second per tick while the battery runs', fakeAsync(() => {
      Object.defineProperty(document, 'hidden', { configurable: true, get: () => false });
      try {
        open(batteryRun({ startedAtUtc: new Date(Date.now() - 400).toISOString() }));
        expect(elapsedText()).toBe('0s');

        // The first tick lands just past the first whole second, before the first poll at 2 s.
        tick(620);
        fixture.detectChanges();
        expect(elapsedText()).toBe('1s');

        for (let second = 2; second <= 10; second++) {
          tick(1000);
          fixture.detectChanges();
          expect(elapsedText()).toBe(`${second}s`);
        }
        close();
      } finally {
        delete (document as unknown as { hidden?: boolean }).hidden;
      }
    }));
  });

  it('derives an empty slot with no history as Pending', () => {
    expect(batterySlotState(slot(0, 1, null), [])).toBe('pending');
  });

  describe('attaching an existing run', () => {
    /**
     * A stopped battery run: (0, 1) usable, (1, 1) index withheld, (0, 2) empty after a superseded
     * member, (1, 2) never filled.
     */
    function stoppedRun(overrides: Partial<BenchmarkBatteryRunDto> = {}): BenchmarkBatteryRunDto {
      const completed = member({ memberId: 1, suiteIndex: 0, round: 1, runId: 101 });
      const withheld = member({
        memberId: 2, suiteIndex: 1, round: 1, runId: 102, runStatus: 'CompletedWithErrors',
        qualityIndex: null, usable: false, unusableReason: 'index withheld'
      });
      const replaced = member({
        memberId: 3, suiteIndex: 0, round: 2, runId: 103, runStatus: 'CompletedWithErrors',
        qualityIndex: null, superseded: true, usable: false, unusableReason: 'superseded'
      });
      return batteryRun({
        status: 'Stopped', stopReason: 'MemberFailed', stopReasonText: 'A member run failed', resumable: true,
        isDriving: false, currentSuiteIndex: null, currentSuitePosition: null, currentSuiteName: null,
        currentRound: null, currentRunId: null,
        slots: [slot(0, 1, completed), slot(1, 1, withheld), slot(0, 2, null), slot(1, 2, null)],
        members: [completed, withheld, replaced],
        ...overrides
      });
    }

    function candidate(overrides: Partial<BenchmarkBatteryAttachCandidateDto> = {}): BenchmarkBatteryAttachCandidateDto {
      return dto<BenchmarkBatteryAttachCandidateDto>({
        runId: 201, runStatus: 'Completed', qualityIndex: 68, startedAtUtc: '2026-09-30T09:00:00Z',
        completedAtUtc: '2026-09-30T09:20:00Z', testedModelLabel: 'Model X', harnessVersion: '45',
        scoringMethodVersion: 13, eligible: true, reason: null,
        ...overrides
      });
    }

    const attachButton = (suiteIndex: number, round: number): HTMLButtonElement | null =>
      cell(suiteIndex, round).querySelector('.bp-attach') as HTMLButtonElement | null;

    it('offers the action on empty, superseded and index-withheld cells of a stopped battery run only', () => {
      open(stoppedRun());

      expect(attachButton(0, 1)).toBeNull();
      expect(attachButton(1, 1)).not.toBeNull();
      expect(attachButton(0, 2)).not.toBeNull();
      expect(attachButton(1, 2)).not.toBeNull();
      expect(cell(0, 2).querySelector('.job-status-chip')?.textContent?.trim()).toBe('Superseded');
      expect(attachButton(1, 2)!.textContent!.trim()).toBe('Attach existing run');
      expect(attachButton(1, 2)!.getAttribute('aria-label')).toBe('Attach existing run to Board Reading, round 2');
      expect(attachButton(1, 2)!.getAttribute('aria-expanded')).toBe('false');
    });

    it('offers no attach while the battery run is running or being driven', () => {
      open(batteryRun());
      expect(el().querySelectorAll('.bp-attach').length).toBe(0);

      close();
      open(stoppedRun({ id: 8, isDriving: true }));
      expect(el().querySelectorAll('.bp-attach').length).toBe(0);
    });

    it('lists the slot\'s candidates with their eligibility, attaches the chosen one and reloads the grid', () => {
      open(stoppedRun());
      service.getBatteryAttachCandidates.mockReturnValue(of([
        candidate(),
        candidate({
          runId: 202, eligible: false,
          reason: 'Run #202 ran under another instrument than the one recorded for \'Board Reading\': ToolGuidesSha256 differ.'
        })
      ]));
      const attached = member({ memberId: 10, suiteIndex: 1, round: 2, runId: 201, origin: 'Attached', qualityIndex: 68 });
      const base = stoppedRun();
      service.attachBatteryMember.mockReturnValue(of(stoppedRun({
        completedMemberCount: 2,
        slots: [base.slots[0], base.slots[1], base.slots[2], slot(1, 2, attached)],
        members: [...base.members, attached]
      })));
      const attachedEvent = vi.fn().mockName('memberAttached');
      component.memberAttached.subscribe(attachedEvent);

      attachButton(1, 2)!.click();
      fixture.detectChanges();

      expect(service.getBatteryAttachCandidates).toHaveBeenCalledWith(7, 1, 2);
      expect(text('#bpAttachTitle')).toBe('Attach an existing run: Board Reading, round 2');
      expect(attachButton(1, 2)!.getAttribute('aria-expanded')).toBe('true');
      const rows = el().querySelectorAll('.bp-attach-table tbody tr');
      expect(rows.length).toBe(2);
      expect(rows[0].textContent).toContain('#201');
      expect(rows[0].textContent).toContain('Eligible');
      expect(rows[1].textContent).toContain('ToolGuidesSha256 differ.');
      expect(rows[1].querySelector('.bp-attach-choose')).toBeNull();

      (rows[0].querySelector('.bp-attach-choose') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(service.attachBatteryMember).toHaveBeenCalledWith(7, { suiteIndex: 1, round: 2, runId: 201 });
      expect(el().querySelector('.bp-attach-panel')).toBeNull();
      expect(cell(1, 2).textContent).toContain('Run #201');
      expect(cell(1, 2).textContent).toContain('attached');
      expect(attachButton(1, 2)).toBeNull();
      expect(attachedEvent).toHaveBeenCalledWith(7);
    });

    it('shows why the server refused the attach and keeps the list open', () => {
      open(stoppedRun());
      service.getBatteryAttachCandidates.mockReturnValue(of([candidate()]));
      service.attachBatteryMember.mockReturnValue(throwError(() => ({
        status: 400, error: 'Suite 2, round 1 already holds a usable member (run #300).'
      })));

      attachButton(1, 1)!.click();
      fixture.detectChanges();
      (el().querySelector('.bp-attach-choose') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(text('.bp-attach-error')).toBe('Suite 2, round 1 already holds a usable member (run #300).');
      expect(el().querySelector('.bp-attach-panel')).not.toBeNull();
    });

    it('says when no run of the suite tested the configuration, keeps one live region, and closes', () => {
      open(stoppedRun());
      service.getBatteryAttachCandidates.mockReturnValue(of([]));

      attachButton(0, 2)!.click();
      fixture.detectChanges();

      expect(text('.bp-attach-status')).toContain('No run of this suite');
      expect(el().querySelectorAll('[aria-live], [role="status"]').length).toBe(1);

      (el().querySelector('.bp-attach-panel .btn-icon-action') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(el().querySelector('.bp-attach-panel')).toBeNull();
    });
  });
});
