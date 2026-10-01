import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkBatteryAttachCandidateDto,
  BenchmarkBatteryMemberDto,
  BenchmarkBatteryRunDto,
  BenchmarkBatterySlotDto
} from '../../../services/admin-benchmark.service';
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
    ...overrides
  });
}

describe('BatteryProgressDialogComponent', () => {
  let fixture: ComponentFixture<BatteryProgressDialogComponent>;
  let component: BatteryProgressDialogComponent;
  let service: jasmine.SpyObj<AdminBenchmarkService>;

  function open(run: BenchmarkBatteryRunDto): void {
    service.getBatteryRun.and.returnValue(of(run));
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
    service = jasmine.createSpyObj<AdminBenchmarkService>('AdminBenchmarkService', [
      'getBatteryRun', 'resumeBatteryRun', 'cancelBatteryRun', 'getBatteryAttachCandidates', 'attachBatteryMember'
    ]);
    service.getBatteryRun.and.returnValue(of(batteryRun()));
    service.resumeBatteryRun.and.returnValue(of({ batteryRunId: 7 }));
    service.cancelBatteryRun.and.returnValue(of(void 0));

    await TestBed.configureTestingModule({
      imports: [BatteryProgressDialogComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: service }]
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

    const resumed = jasmine.createSpy('resumed');
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

    button('.bp-continue')!.click();
    fixture.detectChanges();
    expect(service.resumeBatteryRun).toHaveBeenCalledWith(7, 'Continue');
  });

  it('offers the re-run after Continue is refused with 409', () => {
    const run = batteryRun({
      status: 'Stopped', stopReason: 'MemberFailed', resumable: true,
      runsPerSuite: 1, requestedMemberCount: 2,
      slots: [slot(0, 1, member()), slot(1, 1, null)], members: [member()]
    });
    open(run);
    service.resumeBatteryRun.and.returnValue(throwError(() => ({ status: 409, error: 'The harness version changed.' })));

    button('.bp-continue')!.click();
    fixture.detectChanges();

    expect(text('.bp-action-error')).toBe('The harness version changed.');
    expect(button('.bp-continue')).toBeNull();
    expect(button('.bp-rerun')).not.toBeNull();
  });

  it('cancels a running battery', () => {
    open(batteryRun());
    const canceled = jasmine.createSpy('canceled');
    component.batteryCanceled.subscribe(canceled);

    button('.bp-cancel-battery')!.click();
    fixture.detectChanges();

    expect(service.cancelBatteryRun).toHaveBeenCalledWith(7);
    expect(canceled).toHaveBeenCalledWith(7);
  });

  it('opens a member run progress and closes itself', () => {
    open(batteryRun());
    const runProgress = jasmine.createSpy('runProgress');
    const closed = jasmine.createSpy('closed');
    component.openRunProgress.subscribe(runProgress);
    component.closed.subscribe(closed);

    (cell(0, 2).querySelector('.bp-open-run') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(closed).toHaveBeenCalled();
    expect(runProgress).toHaveBeenCalledWith(103);
  });

  it('offers Open Analysis once finished', () => {
    const run = batteryRun({
      status: 'Completed', completedAtUtc: '2026-10-01T12:00:00Z', completedSuiteCount: 2,
      runsPerSuite: 1, requestedMemberCount: 2, completedMemberCount: 2,
      slots: [slot(0, 1, member()), slot(1, 1, member({ memberId: 9, suiteIndex: 1, runId: 109 }))],
      members: [member(), member({ memberId: 9, suiteIndex: 1, runId: 109 })]
    });
    open(run);
    const analysis = jasmine.createSpy('analysis');
    component.openAnalysis.subscribe(analysis);

    expect(button('.bp-cancel-battery')).toBeNull();
    button('.bp-open-analysis')!.click();
    fixture.detectChanges();

    expect(analysis).toHaveBeenCalledWith(7);
  });

  it('polls while the battery run is live and stops once it is not', fakeAsync(() => {
    open(batteryRun());
    expect(service.getBatteryRun).toHaveBeenCalledTimes(1);

    tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS);
    expect(service.getBatteryRun).toHaveBeenCalledTimes(2);

    service.getBatteryRun.and.returnValue(of(batteryRun({ status: 'Completed' })));
    tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS);
    expect(service.getBatteryRun).toHaveBeenCalledTimes(3);

    tick(BatteryProgressDialogComponent.POLL_INTERVAL_MS * 3);
    expect(service.getBatteryRun).toHaveBeenCalledTimes(3);

    close();
    discardPeriodicTasks();
  }));

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
      service.getBatteryAttachCandidates.and.returnValue(of([
        candidate(),
        candidate({
          runId: 202, eligible: false,
          reason: 'Run #202 ran under another instrument than the one recorded for \'Board Reading\': ToolGuidesSha256 differ.'
        })
      ]));
      const attached = member({ memberId: 10, suiteIndex: 1, round: 2, runId: 201, origin: 'Attached', qualityIndex: 68 });
      const base = stoppedRun();
      service.attachBatteryMember.and.returnValue(of(stoppedRun({
        completedMemberCount: 2,
        slots: [base.slots[0], base.slots[1], base.slots[2], slot(1, 2, attached)],
        members: [...base.members, attached]
      })));
      const attachedEvent = jasmine.createSpy('memberAttached');
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
      service.getBatteryAttachCandidates.and.returnValue(of([candidate()]));
      service.attachBatteryMember.and.returnValue(throwError(() => ({
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
      service.getBatteryAttachCandidates.and.returnValue(of([]));

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
