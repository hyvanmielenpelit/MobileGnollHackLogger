import type { Mock, MockedObject } from "vitest";
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { of, throwError, Subject } from 'rxjs';
import { AdminBenchmarkComponent } from './benchmark.component';
import { AdminBenchmarkService } from '../../services/admin-benchmark.service';
import { BenchmarkCompletionSoundService } from '../../services/benchmark-completion-sound.service';
import { BenchmarkCompletionNotificationService } from '../../services/benchmark-completion-notification.service';
import { BenchmarkBackgroundActivityService } from '../../services/benchmark-background-activity.service';
import { clearStoredState, createAdminBenchmarkFixture, RUN_SETTINGS_KEY } from './benchmark.component.testing';

describe('AdminBenchmarkComponent', () => {
  let component: AdminBenchmarkComponent;
  let fixture: ComponentFixture<AdminBenchmarkComponent>;
  let benchmarkServiceMock: MockedObject<AdminBenchmarkService>;

  beforeEach(clearStoredState);

  afterEach(clearStoredState);

  beforeEach(async () => {
    ({ component, fixture, benchmarkServiceMock } = await createAdminBenchmarkFixture());
  });

  describe('run setting recall', () => {
    /**
     * The fixture's own configuration is id 1 with modelRole 7 (Chat + Title + Benchmark), so it is the
     * only benchmark-capable configuration unless a spec adds another.
     */
    const secondConfig = (id: number) => ({ ...component.systemConfigs[0], id, displayName: `Model ${id}` });

    it('should write the run settings to localStorage when a run is started', () => {
      benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 99 }));
      component.systemConfigs = [component.systemConfigs[0], secondConfig(2)];
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 2;
      component.secondOpinionConfigId = 2;
      component.claimVerifierConfigId = 1;
      component.selectedScoringProfileId = 1;
      component.candidateVerboseMode = true;

      component.startBenchmark();

      const stored = JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!);
      expect(stored.suiteId).toBe(1);
      expect(stored.testedConfigId).toBe(1);
      expect(stored.assessorConfigId).toBe(2);
      expect(stored.secondOpinionConfigId).toBe(2);
      expect(stored.claimVerifierConfigId).toBe(1);
      expect(stored.scoringProfileId).toBe(1);
      expect(stored.verboseMode).toBe(true);
      expect(stored.allowSourceCodeReferences).toBe(false);
      expect(stored.runCount).toBe(1);
      component.ngOnDestroy();
    });

    it('should remember Source Code References, and start a stored setup without it at Disallowed', () => {
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({ suiteId: 1, allowSourceCodeReferences: true }));
      const allowed = TestBed.createComponent(AdminBenchmarkComponent);
      allowed.componentInstance.systemConfigs = [component.systemConfigs[0]];
      allowed.detectChanges();
      expect(allowed.componentInstance.candidateAllowSourceCodeReferences).toBe(true);
      allowed.componentInstance.ngOnDestroy();

      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({ suiteId: 1, verboseMode: true }));
      const older = TestBed.createComponent(AdminBenchmarkComponent);
      older.componentInstance.systemConfigs = [component.systemConfigs[0]];
      older.detectChanges();
      expect(older.componentInstance.candidateAllowSourceCodeReferences).toBe(false);
      older.componentInstance.ngOnDestroy();
    });

    it('should restore every remembered selection on the next construction', () => {
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1,
        testedConfigId: 2,
        assessorConfigId: 1,
        secondOpinionConfigId: 2,
        claimVerifierConfigId: 1,
        secondOpinionMode: 3,
        scoringProfileId: 1,
        verboseMode: true,
        runCount: 5
      }));

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0], secondConfig(2)];
      restored.detectChanges();

      const c = restored.componentInstance;
      expect(c.selectedSuiteId).toBe(1);
      expect(c.testedConfigId).toBe(2);
      expect(c.assessorConfigId).toBe(1);
      expect(c.secondOpinionConfigId).toBe(2);
      expect(c.claimVerifierConfigId).toBe(1);
      expect(c.secondOpinionMode).toBe(3);
      expect(c.selectedScoringProfileId).toBe(1);
      expect(c.candidateVerboseMode).toBe(true);
      expect(c.runCount).toBe(5);
      c.ngOnDestroy();
    });

    it('should fall back to default runCount of 1 when stored runCount is invalid or non-positive', () => {
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1,
        testedConfigId: 1,
        assessorConfigId: 1,
        runCount: -3
      }));

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
      restored.detectChanges();

      expect(restored.componentInstance.runCount).toBe(1);
      restored.componentInstance.ngOnDestroy();
    });

    it('should clamp remembered runCount when loadRunLimits receives a lower maxRunCountPerSeries', () => {
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1,
        testedConfigId: 1,
        assessorConfigId: 1,
        runCount: 25
      }));

      benchmarkServiceMock.getRunLimits.mockReturnValue(of({
        maxRunsPerHour: 4,
        maxRunsPerDay: 20,
        runsInLastHour: 0,
        runsInLast24Hours: 0,
        remainingDailyHeadroom: 20,
        maxRunCountPerSeries: 10
      }));

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
      restored.detectChanges();

      expect(restored.componentInstance.runCount).toBe(10);
      restored.componentInstance.ngOnDestroy();
    });

    it('should fall back to the default when a remembered configuration is no longer benchmark-capable', () => {
      // Id 7 is not in systemConfigs at all, which is what a disabled configuration, one whose key was
      // removed, or one that lost its Benchmark role looks like to this screen.
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1,
        testedConfigId: 7,
        assessorConfigId: 7,
        secondOpinionConfigId: 7,
        claimVerifierConfigId: 7,
        secondOpinionMode: null,
        scoringProfileId: 1,
        verboseMode: false
      }));

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
      restored.detectChanges();

      const c = restored.componentInstance;
      expect(c.testedConfigId).toBe(1);
      expect(c.assessorConfigId).toBe(1);
      // The optional roles restore to "not selected" rather than to a dangling id.
      expect(c.secondOpinionConfigId).toBeNull();
      expect(c.claimVerifierConfigId).toBeNull();
      c.ngOnDestroy();
    });

    it('should fall back to the first suite when the remembered suite no longer exists', () => {
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 999, testedConfigId: null, assessorConfigId: null,
        secondOpinionConfigId: null, claimVerifierConfigId: null,
        secondOpinionMode: null, scoringProfileId: 999, verboseMode: null
      }));

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
      restored.detectChanges();

      expect(restored.componentInstance.selectedSuiteId).toBe(1);
      expect(restored.componentInstance.selectedScoringProfileId).toBe(1);
      restored.componentInstance.ngOnDestroy();
    });

    it('should leave every default untouched when localStorage throws', () => {
      vi.spyOn(localStorage, 'getItem').mockImplementation(() => {
        throw new Error('SecurityError');
      });

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];

      expect(() => restored.detectChanges()).not.toThrow();
      expect(restored.componentInstance.selectedSuiteId).toBe(1);
      expect(restored.componentInstance.testedConfigId).toBe(1);
      expect(restored.componentInstance.candidateVerboseMode).toBe(false);
      restored.componentInstance.ngOnDestroy();
    });

    it('should not remember the same-provider acknowledgement', () => {
      benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 99 }));
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 1;

      component.startBenchmark(true);

      const stored = JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!);
      // A per-run safety acknowledgement: remembering it would silently defeat the warning dialog.
      expect(stored.acknowledgeSameProvider).toBeUndefined();
      component.ngOnDestroy();
    });

    it('should send and remember the report writer, and restore it while it still qualifies', () => {
      benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 99 }));
      const writer = { ...secondConfig(2), provider: 'OpenAI', modelId: 'gpt-writer' };
      component.systemConfigs = [component.systemConfigs[0], writer];
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 1;
      component.reportWriterConfigId = 2;

      component.startBenchmark();

      expect(vi.mocked(benchmarkServiceMock.startRun).mock.lastCall![0].reportWriterModelConfigurationId).toBe(2);
      expect(JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!).reportWriterConfigId).toBe(2);
      component.ngOnDestroy();

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0], writer];
      restored.detectChanges();
      expect(restored.componentInstance.reportWriterConfigId).toBe(2);
      restored.componentInstance.ngOnDestroy();

      // A writer that no longer qualifies restores to "None".
      const dropped = TestBed.createComponent(AdminBenchmarkComponent);
      dropped.componentInstance.systemConfigs = [component.systemConfigs[0]];
      dropped.detectChanges();
      expect(dropped.componentInstance.reportWriterConfigId).toBeNull();
      dropped.componentInstance.ngOnDestroy();
    });

    it('should default completionSound to true before anything is remembered', () => {
      expect(component.completionSound).toBe(true);
    });

    it('should persist completionSound when a run is started', () => {
      benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 99 }));
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 1;
      component.completionSound = false;

      component.startBenchmark();

      const stored = JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!);
      expect(stored.completionSound).toBe(false);
      component.ngOnDestroy();
    });

    it('should restore a remembered completionSound value on the next construction', () => {
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1, testedConfigId: 1, assessorConfigId: 1,
        secondOpinionConfigId: null, claimVerifierConfigId: null,
        secondOpinionMode: null, scoringProfileId: 1, verboseMode: null,
        completionSound: false
      }));

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
      restored.detectChanges();

      expect(restored.componentInstance.completionSound).toBe(false);
      restored.componentInstance.ngOnDestroy();
    });

    it('should default completionSound to true when the stored blob predates the field', () => {
      localStorage.setItem(RUN_SETTINGS_KEY, JSON.stringify({
        suiteId: 1, testedConfigId: 1, assessorConfigId: 1,
        secondOpinionConfigId: null, claimVerifierConfigId: null,
        secondOpinionMode: null, scoringProfileId: 1, verboseMode: null
      }));

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
      restored.detectChanges();

      expect(restored.componentInstance.completionSound).toBe(true);
      restored.componentInstance.ngOnDestroy();
    });
  });

  describe('completion sound transition detection', () => {
    let playSpy: Mock;

    function makeRun(overrides: Record<string, unknown> = {}): any {
      return {
        id: 42,
        benchmarkSuiteId: 1,
        suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Test Model',
        testedModelProviderUsed: 'Anthropic',
        testedModelIdUsed: 'claude-3-5-sonnet',
        assessorModelDisplayNameUsed: 'Test Assessor',
        assessorModelProviderUsed: 'Anthropic',
        assessorModelIdUsed: 'claude-3-5-sonnet',
        startedByUserName: 'admin',
        status: 'Running',
        startedAtUtc: '2026-09-02T00:00:00Z',
        completedAtUtc: null,
        totalQuestionCount: 3,
        answers: [],
        ...overrides
      };
    }

    beforeEach(() => {
      const soundService = TestBed.inject(BenchmarkCompletionSoundService);
      playSpy = vi.spyOn(soundService, 'play').mockResolvedValue('played');
    });

    it('should chime once for a run seen Running and then reaching a terminal status', () => {
      benchmarkServiceMock.getRun.mockReturnValue(of(makeRun({ id: 42, status: 'Running' })));
      (component as any).pollRunDetail(42);
      expect(playSpy).not.toHaveBeenCalled();

      benchmarkServiceMock.getRun.mockReturnValue(of(makeRun({ id: 42, status: 'Completed' })));
      (component as any).pollRunDetail(42);
      expect(playSpy).toHaveBeenCalledWith('run:42');
      expect(playSpy).toHaveBeenCalledTimes(1);

      // A later poll of the same, already-terminal run must not chime a second time.
      (component as any).pollRunDetail(42);
      expect(playSpy).toHaveBeenCalledTimes(1);
    });

    it('should not chime for a run first observed already terminal', () => {
      benchmarkServiceMock.getRun.mockReturnValue(of(makeRun({ id: 42, status: 'Completed' })));
      (component as any).pollRunDetail(42);

      expect(playSpy).not.toHaveBeenCalled();
    });

    it('should not chime individually for a run that is a member of a still-live series', () => {
      component.activeSeries = { id: 7, status: 'Running', members: [] } as any;
      component.activeSeriesId = 7;

      benchmarkServiceMock.getRun.mockReturnValue(of(makeRun({ id: 42, status: 'Running' })));
      (component as any).pollRunDetail(42);

      benchmarkServiceMock.getRun.mockReturnValue(of(makeRun({ id: 42, status: 'Completed' })));
      (component as any).pollRunDetail(42);

      expect(playSpy).not.toHaveBeenCalledWith('run:42');
    });

    it('should chime once for a series seen live and then reaching a terminal status', () => {
      benchmarkServiceMock.getRunSeries.mockReturnValue(of({
        id: 8, status: 'Running', completedRunCount: 0, requestedRunCount: 2, members: []
      } as any));
      (component as any).pollSeries(8);
      expect(playSpy).not.toHaveBeenCalled();

      benchmarkServiceMock.getRunSeries.mockReturnValue(of({
        id: 8, status: 'Completed', completedRunCount: 2, requestedRunCount: 2, members: []
      } as any));
      (component as any).pollSeries(8);
      expect(playSpy).toHaveBeenCalledWith('series:8');
      expect(playSpy).toHaveBeenCalledTimes(1);

      // A later poll of the same, already-finished series must not chime a second time.
      (component as any).pollSeries(8);
      expect(playSpy).toHaveBeenCalledTimes(1);
    });

    it('should not chime for a series first observed already finished', () => {
      benchmarkServiceMock.getRunSeries.mockReturnValue(of({
        id: 8, status: 'Completed', completedRunCount: 2, requestedRunCount: 2, members: []
      } as any));
      (component as any).pollSeries(8);

      expect(playSpy).not.toHaveBeenCalled();
    });

    describe('cancellation', () => {
      let notifySpy: Mock;

      function series(status: string, members: { runId: number }[] = []): any {
        return { id: 8, status, completedRunCount: 1, requestedRunCount: 2, members };
      }

      function seeRunLive(id = 42): void {
        benchmarkServiceMock.getRun.mockReturnValue(of(makeRun({ id, status: 'Running' })));
        (component as any).pollRunDetail(id);
      }

      function pollRun(id: number, status: string | number): void {
        benchmarkServiceMock.getRun.mockReturnValue(of(makeRun({ id, status })));
        (component as any).pollRunDetail(id);
      }

      beforeEach(() => {
        const notificationService = TestBed.inject(BenchmarkCompletionNotificationService);
        notifySpy = vi.spyOn(notificationService, 'notify').mockReturnValue(undefined as any);
        vi.spyOn(notificationService, 'permission').mockReturnValue('granted');
        component.completionSound = true;
        component.completionNotification = true;
        vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);
        vi.spyOn(document, 'hasFocus').mockReturnValue(false);
      });

      it('does not chime or notify for a run that ends Canceled', () => {
        seeRunLive();
        pollRun(42, 'Canceled');

        expect(playSpy).not.toHaveBeenCalled();
        expect(notifySpy).not.toHaveBeenCalled();
      });

      it('does not chime for a run whose numeric status is Canceled', () => {
        seeRunLive();
        pollRun(42, 5);

        expect(playSpy).not.toHaveBeenCalled();
      });

      it('does not chime for a run cancelled here that the server returns to Completed', () => {
        seeRunLive();
        component.activeRunId = 42;
        benchmarkServiceMock.cancelRun.mockReturnValue(of({ success: true }));
        benchmarkServiceMock.getRun.mockReturnValue(of(makeRun({ id: 42, status: 'Completed' })));

        component.cancelActiveRun();

        expect(benchmarkServiceMock.cancelRun).toHaveBeenCalledWith(42);
        expect(playSpy).not.toHaveBeenCalled();
        expect(notifySpy).not.toHaveBeenCalled();
      });

      it('still chimes when the cancel request fails and the run then completes', () => {
        vi.spyOn(console, 'error').mockReturnValue(undefined);
        seeRunLive();
        component.activeRunId = 42;
        benchmarkServiceMock.cancelRun.mockReturnValue(throwError(() => ({ status: 500 })));

        component.cancelActiveRun();
        pollRun(42, 'Completed');

        expect(playSpy).toHaveBeenCalledTimes(1);

        expect(playSpy).toHaveBeenCalledWith('run:42');
      });

      it('does not chime for a run cancelled from the run detail view', () => {
        seeRunLive();
        benchmarkServiceMock.cancelRun.mockReturnValue(new Subject<{
          success: boolean;
        }>());

        component.cancelRunById(42);
        pollRun(42, 'Completed');

        expect(playSpy).not.toHaveBeenCalled();
      });

      it('chimes for a failed-question re-run launched after a cancel of the same run', () => {
        seeRunLive();
        benchmarkServiceMock.cancelRun.mockReturnValue(new Subject<{
          success: boolean;
        }>());
        component.cancelRunById(42);

        benchmarkServiceMock.rerunFailedQuestions.mockReturnValue(of({ runId: 42 }));
        benchmarkServiceMock.getRun.mockReturnValue(of(makeRun({ id: 42, status: 'Running' })));
        (component as any).launchFailedQuestionRerun(42, [0]);
        pollRun(42, 'Completed');

        expect(playSpy).toHaveBeenCalledTimes(1);

        expect(playSpy).toHaveBeenCalledWith('run:42');
        component.closeRunProgressDialog();
      });

      it('does not chime or notify for a series that ends Cancelled, then or later', () => {
        benchmarkServiceMock.getRunSeries.mockReturnValue(of(series('Running')));
        (component as any).pollSeries(8);
        benchmarkServiceMock.getRunSeries.mockReturnValue(of(series('Cancelled')));
        (component as any).pollSeries(8);
        (component as any).pollSeries(8);

        expect(playSpy).not.toHaveBeenCalled();
        expect(notifySpy).not.toHaveBeenCalled();
      });

      it('does not chime for a member that completes as its series is cancelled', () => {
        seeRunLive();
        component.activeSeries = series('Cancelled', [{ runId: 42 }]);
        component.activeSeriesId = 8;

        pollRun(42, 'Completed');

        expect(playSpy).not.toHaveBeenCalled();
      });

      it('still chimes for a run that ends Failed', () => {
        seeRunLive();
        pollRun(42, 'Failed');

        expect(playSpy).toHaveBeenCalledTimes(1);

        expect(playSpy).toHaveBeenCalledWith('run:42');
        expect(notifySpy).toHaveBeenCalledTimes(1);
      });

      for (const status of ['Stopped', 'Failed']) {
        it(`still chimes for a series that ends ${status}`, () => {
          benchmarkServiceMock.getRunSeries.mockReturnValue(of(series('Running')));
          (component as any).pollSeries(8);
          benchmarkServiceMock.getRunSeries.mockReturnValue(of(series(status)));
          (component as any).pollSeries(8);

          expect(playSpy).toHaveBeenCalledTimes(1);

          expect(playSpy).toHaveBeenCalledWith('series:8');
        });
      }
    });
  });

  describe('Part C: arming the completion signals from a user gesture', () => {
    let armSpy: Mock;

    function buildRun(overrides: Record<string, unknown> = {}): any {
      return {
        id: 37, benchmarkSuiteId: 1, suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Test Model', testedModelProviderUsed: 'Anthropic', testedModelIdUsed: 'x',
        assessorModelDisplayNameUsed: 'Test Assessor', assessorModelProviderUsed: 'Anthropic', assessorModelIdUsed: 'x',
        startedByUserName: 'admin', status: 'CompletedWithErrors', startedAtUtc: '2026-09-02T00:00:00Z',
        completedAtUtc: null, totalQuestionCount: 3, answers: [], ...overrides
      };
    }

    beforeEach(() => {
      const soundService = TestBed.inject(BenchmarkCompletionSoundService);
      armSpy = vi.spyOn(soundService, 'arm').mockResolvedValue();
    });

    it('arms from startBenchmark', () => {
      benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 99 }));
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 1;

      component.startBenchmark();

      expect(armSpy).toHaveBeenCalledTimes(1);
      component.ngOnDestroy();
    });

    it('arms from resumeActiveSeries', () => {
      component.activeSeriesId = 5;
      benchmarkServiceMock.resumeRunSeries.mockReturnValue(of({} as any));
      benchmarkServiceMock.getRunSeries.mockReturnValue(of({
        id: 5, status: 'Running', completedRunCount: 0, requestedRunCount: 2, members: []
      } as any));

      component.resumeActiveSeries();

      expect(armSpy).toHaveBeenCalledTimes(1);
      component.ngOnDestroy();
    });

    it('arms from rerunFailedFromProgress', () => {
      component.activeRunDetail = buildRun({ id: 37, answers: [] });
      benchmarkServiceMock.rerunFailedQuestions.mockReturnValue(of({ runId: 37 }));
      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun({ id: 37 })));

      component.rerunFailedFromProgress();

      expect(armSpy).toHaveBeenCalledTimes(1);
      component.closeRunProgressDialog();
    });

    it('arms from rerunFailedFromRunDetail', () => {
      component.selectedRunDetail = buildRun({ id: 37, answers: [] });
      benchmarkServiceMock.rerunFailedQuestions.mockReturnValue(of({ runId: 37 }));
      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun({ id: 37 })));

      component.rerunFailedFromRunDetail(37);

      expect(armSpy).toHaveBeenCalledTimes(1);
      component.closeRunProgressDialog();
    });

    it('arms from testCompletionSound, before priming', () => {
      const soundService = TestBed.inject(BenchmarkCompletionSoundService);
      const primeSpy = vi.spyOn(soundService, 'prime').mockResolvedValue('played');

      component.testCompletionSound();

      expect(armSpy).toHaveBeenCalledTimes(1);
      expect(primeSpy).toHaveBeenCalledTimes(1);
    });

    it('does nothing when neither signal is enabled', () => {
      component.completionSound = false;
      component.completionNotification = false;
      benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 99 }));
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 1;

      component.startBenchmark();

      expect(armSpy).not.toHaveBeenCalled();
      component.ngOnDestroy();
    });
  });

  describe('Part C: the desktop notification', () => {
    let notificationService: BenchmarkCompletionNotificationService;
    let notifySpy: Mock;
    let permissionSpy: Mock;

    function buildRun(overrides: Record<string, unknown> = {}): any {
      return {
        id: 42, benchmarkSuiteId: 1, suiteName: 'Default Suite',
        testedModelDisplayNameUsed: 'Test Model', testedModelProviderUsed: 'Anthropic', testedModelIdUsed: 'x',
        assessorModelDisplayNameUsed: 'Test Assessor', assessorModelProviderUsed: 'Anthropic', assessorModelIdUsed: 'x',
        startedByUserName: 'admin', status: 'Running', startedAtUtc: '2026-09-02T00:00:00Z',
        completedAtUtc: null, totalQuestionCount: 3, answers: [], ...overrides
      };
    }

    beforeEach(() => {
      notificationService = TestBed.inject(BenchmarkCompletionNotificationService);
      notifySpy = vi.spyOn(notificationService, 'notify').mockReturnValue(undefined as any);
      permissionSpy = vi.spyOn(notificationService, 'permission').mockReturnValue('granted');
      vi.spyOn(TestBed.inject(BenchmarkCompletionSoundService), 'play').mockResolvedValue('played');
      vi.spyOn(TestBed.inject(BenchmarkCompletionSoundService), 'arm').mockResolvedValue();
    });

    describe('when Start is pressed with the box ticked', () => {
      it('prompts under the gesture when this browser has not decided, and keeps the box on a grant', fakeAsync(() => {
        permissionSpy.mockReturnValue('default');
        const requestSpy = vi.spyOn(notificationService, 'requestPermission').mockResolvedValue('granted');
        component.completionNotification = true;

        (component as any).armCompletionSignalsFromGesture();
        expect(requestSpy).toHaveBeenCalledTimes(1);
        tick();

        expect(component.completionNotification).toBe(true);
        expect(component.completionNotificationStatus).toBeNull();
      }));

      it('unticks the box with the reason when the Start prompt is dismissed', fakeAsync(() => {
        permissionSpy.mockReturnValue('default');
        vi.spyOn(notificationService, 'requestPermission').mockResolvedValue('default');
        component.completionNotification = true;

        (component as any).armCompletionSignalsFromGesture();
        tick();

        expect(component.completionNotification).toBe(false);
        expect(component.completionNotificationStatus).toBe('The permission prompt was dismissed.');
      }));

      it('does not prompt again once permission was granted', () => {
        const requestSpy = vi.spyOn(notificationService, 'requestPermission').mockReturnValue(undefined as any);
        component.completionNotification = true;

        (component as any).armCompletionSignalsFromGesture();

        expect(requestSpy).not.toHaveBeenCalled();
        expect(component.completionNotification).toBe(true);
      });

      it('does not prompt when blocked, and unticks the box with the reason at once', () => {
        permissionSpy.mockReturnValue('denied');
        const requestSpy = vi.spyOn(notificationService, 'requestPermission').mockReturnValue(undefined as any);
        component.completionNotification = true;

        (component as any).armCompletionSignalsFromGesture();

        expect(requestSpy).not.toHaveBeenCalled();
        expect(component.completionNotification).toBe(false);
        expect(component.completionNotificationStatus).toBe("Notifications are blocked for this site in the browser's settings.");
      });

      it('never prompts when the box is unticked', () => {
        permissionSpy.mockReturnValue('default');
        const requestSpy = vi.spyOn(notificationService, 'requestPermission').mockReturnValue(undefined as any);
        component.completionSound = true;
        component.completionNotification = false;

        (component as any).armCompletionSignalsFromGesture();

        expect(requestSpy).not.toHaveBeenCalled();
      });
    });

    it('takes the empty status line out of the layout, and puts it back when it has a message', fakeAsync(() => {
      fixture.detectChanges();
      const status = fixture.nativeElement.querySelector('.completion-signals-status') as HTMLElement;
      expect(status.classList).toContain('is-empty');
      expect(status.getAttribute('role')).toBe('status');
      expect(getComputedStyle(status).position).toBe('absolute');
      expect(getComputedStyle(status).marginTop).toBe('0px');
      const toggle = fixture.nativeElement.querySelector('label[for="completionNotificationInput"]') as HTMLElement;
      const testSound = Array.from(fixture.nativeElement.querySelectorAll('.completion-signals button.btn-gh-small') as NodeListOf<HTMLElement>)
        .find(b => b.textContent?.trim() === 'Test sound') as HTMLElement;
      expect(toggle.getBoundingClientRect().height).toBeGreaterThan(0);
      expect(testSound.getBoundingClientRect().height).toBeGreaterThan(0);
      const gap = testSound.getBoundingClientRect().top - toggle.getBoundingClientRect().bottom;
      expect(gap).toBeLessThanOrEqual(12);

      vi.spyOn(notificationService, 'requestPermission').mockResolvedValue('default');
      component.onCompletionNotificationChange(true);
      tick();

      expect(status.classList).not.toContain('is-empty');
      expect(getComputedStyle(status).position).toBe('static');
      expect(status.textContent).toContain('The permission prompt was dismissed.');
    }));

    it('persists and restores completionNotification alongside completionSound', () => {
      benchmarkServiceMock.startRun.mockReturnValue(of({ runId: 99 }));
      component.selectedSuiteId = 1;
      component.testedConfigId = 1;
      component.assessorConfigId = 1;
      component.completionNotification = true;

      component.startBenchmark();

      const stored = JSON.parse(localStorage.getItem(RUN_SETTINGS_KEY)!);
      expect(stored.completionNotification).toBe(true);
      component.ngOnDestroy();

      const restored = TestBed.createComponent(AdminBenchmarkComponent);
      restored.componentInstance.systemConfigs = [component.systemConfigs[0]];
      restored.detectChanges();
      expect(restored.componentInstance.completionNotification).toBe(true);
      restored.componentInstance.ngOnDestroy();
    });

    it('turns on and clears the status once permission is granted', fakeAsync(() => {
      vi.spyOn(notificationService, 'requestPermission').mockResolvedValue('granted');

      component.onCompletionNotificationChange(true);
      tick();

      expect(component.completionNotification).toBe(true);
      expect(component.completionNotificationStatus).toBeNull();
    }));

    (['denied', 'default', 'unsupported'] as const).forEach(outcome => {
      it(`unticks the box and explains a "${outcome}" permission result`, fakeAsync(() => {
        vi.spyOn(notificationService, 'requestPermission').mockResolvedValue(outcome);

        component.onCompletionNotificationChange(true);
        tick();
        fixture.detectChanges();

        expect(component.completionNotification).toBe(false);
        expect(component.completionNotificationStatus).toBeTruthy();
        const status = fixture.nativeElement.querySelector('.completion-signals-status') as HTMLElement;
        expect(status.textContent).toContain(component.completionNotificationStatus);
      }));
    });

    it('unticking directly clears the status without requesting permission', () => {
      const requestSpy = vi.spyOn(notificationService, 'requestPermission').mockReturnValue(undefined as any);
      component.completionNotification = true;
      component.completionNotificationStatus = 'stale';

      component.onCompletionNotificationChange(false);

      expect(component.completionNotification).toBe(false);
      expect(component.completionNotificationStatus).toBeNull();
      expect(requestSpy).not.toHaveBeenCalled();
    });

    it('notifies once for a hidden completion with the sound off and the notification on', () => {
      component.completionSound = false;
      component.completionNotification = true;
      vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);
      vi.spyOn(document, 'hasFocus').mockReturnValue(false);

      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun({ id: 42, status: 'Running' })));
      (component as any).pollRunDetail(42);
      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun({ id: 42, status: 'Completed' })));
      (component as any).pollRunDetail(42);

      expect(notifySpy).toHaveBeenCalledTimes(1);
      expect(vi.mocked(notifySpy).mock.lastCall![0]).toBe('run:42');
    });

    it('notifies a visible, focused completion too — the ticked box no longer checks focus', () => {
      component.completionNotification = true;
      vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
      vi.spyOn(document, 'hasFocus').mockReturnValue(true);

      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun({ id: 42, status: 'Running' })));
      (component as any).pollRunDetail(42);
      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun({ id: 42, status: 'Completed' })));
      (component as any).pollRunDetail(42);

      expect(notifySpy).toHaveBeenCalledTimes(1);
      expect(vi.mocked(notifySpy).mock.lastCall![0]).toBe('run:42');
    });

    it('records a notification attempt with the tab focused, surfaced in diagnostics', () => {
      component.completionNotification = true;
      vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
      vi.spyOn(document, 'hasFocus').mockReturnValue(true);
      notifySpy.mockReturnValue('shown');

      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun({ id: 42, status: 'Running' })));
      (component as any).pollRunDetail(42);
      benchmarkServiceMock.getRun.mockReturnValue(of(buildRun({ id: 42, status: 'Completed' })));
      (component as any).pollRunDetail(42);

      const attempts = (component as any).notificationAttempts;
      expect(attempts.length).toBe(1);
      expect(attempts[0].key).toBe('run:42');
      expect(attempts[0].hidden).toBe(false);
      expect(attempts[0].focused).toBe(true);
      expect(attempts[0].outcome).toBe('shown');

      const diagnostics = component.runDiagnosticsText;
      expect(diagnostics).toContain('key=run:42 hidden=false focused=true outcome=shown');
    });
  });

  describe('Part C: the second series-resume path and the Web Lock', () => {
    it('restarts the series poll when the progress dialog resumes a stopped series', () => {
      benchmarkServiceMock.getRunSeries.mockReturnValue(of({
        id: 11, status: 'Running', completedRunCount: 1, requestedRunCount: 3, members: []
      } as any));

      component.onSeriesResumedFromDialog(11);

      expect(component.activeSeriesId).toBe(11);
      expect(benchmarkServiceMock.getRunSeries).toHaveBeenCalledWith(11);
      component.ngOnDestroy();
    });

    it('does not release the background lock on a single run poll error, and stays polling', () => {
      const consoleError = vi.spyOn(console, 'error').mockReturnValue(undefined);
      const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
      const releaseSpy = vi.spyOn(lockService, 'release').mockReturnValue(undefined);
      const acquireSpy = vi.spyOn(lockService, 'acquireForRun').mockReturnValue(undefined);

      // startPolling's own poll is the first failure; its stopPolling() of any previous poller
      // may release, so the spy is reset once polling has started.
      benchmarkServiceMock.getRun.mockReturnValue(throwError(() => ({ status: 500 })));
      (component as any).startPolling(42);
      expect(acquireSpy).toHaveBeenCalledWith(42);
      releaseSpy.mockClear();

      (component as any).pollRunDetail(42);

      expect(releaseSpy).not.toHaveBeenCalled();
      expect((component as any).pollTickerHandle).not.toBeNull();
      expect(consoleError).toHaveBeenCalledWith('Failed to poll run detail', expect.any(Object));
      (component as any).stopPolling();
      component.ngOnDestroy();
    });

    it('releases the background lock only on the 5th consecutive run poll error', () => {
      const consoleError = vi.spyOn(console, 'error').mockReturnValue(undefined);
      const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
      const releaseSpy = vi.spyOn(lockService, 'release').mockReturnValue(undefined);

      benchmarkServiceMock.getRun.mockReturnValue(throwError(() => ({ status: 500 })));
      (component as any).startPolling(42);
      releaseSpy.mockClear();

      // startPolling's own poll was the first failure; three more make four.
      for (let i = 0; i < 3; i++) {
        (component as any).pollRunDetail(42);
      }
      expect(releaseSpy).not.toHaveBeenCalled();
      expect((component as any).pollTickerHandle).not.toBeNull();

      (component as any).pollRunDetail(42);
      expect(releaseSpy).toHaveBeenCalled();
      expect((component as any).pollTickerHandle).toBeNull();
      expect(consoleError).toHaveBeenCalledWith('Failed to poll run detail', expect.any(Object));
      component.ngOnDestroy();
    });

    it('resets the consecutive run poll failure count on a successful poll', () => {
      const consoleError = vi.spyOn(console, 'error').mockReturnValue(undefined);
      const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
      const releaseSpy = vi.spyOn(lockService, 'release').mockReturnValue(undefined);

      benchmarkServiceMock.getRun.mockReturnValue(throwError(() => ({ status: 500 })));
      (component as any).startPolling(42);
      releaseSpy.mockClear();
      for (let i = 0; i < 3; i++) {
        (component as any).pollRunDetail(42);
      }

      benchmarkServiceMock.getRun.mockReturnValue(of({ id: 42, status: 'Running', answers: [] } as any));
      (component as any).pollRunDetail(42);

      benchmarkServiceMock.getRun.mockReturnValue(throwError(() => ({ status: 500 })));
      for (let i = 0; i < 4; i++) {
        (component as any).pollRunDetail(42);
      }

      expect(releaseSpy).not.toHaveBeenCalled();
      expect(consoleError).toHaveBeenCalledWith('Failed to poll run detail', expect.any(Object));
      (component as any).stopPolling();
      component.ngOnDestroy();
    });

    it('does not release the background lock on a single series poll error, and stays polling', () => {
      const consoleError = vi.spyOn(console, 'error').mockReturnValue(undefined);
      const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
      const releaseSpy = vi.spyOn(lockService, 'release').mockReturnValue(undefined);
      const acquireSpy = vi.spyOn(lockService, 'acquireForSeries').mockReturnValue(undefined);

      (component as any).startSeriesPolling(11);
      expect(acquireSpy).toHaveBeenCalledWith(11);
      releaseSpy.mockClear();

      benchmarkServiceMock.getRunSeries.mockReturnValue(throwError(() => ({ status: 500 })));
      (component as any).pollSeries(11);

      expect(releaseSpy).not.toHaveBeenCalled();
      expect((component as any).seriesPollTickerHandle).not.toBeNull();
      expect(consoleError).toHaveBeenCalledWith('Failed to poll benchmark run series', expect.any(Object));
      (component as any).stopSeriesPolling();
      component.ngOnDestroy();
    });

    it('releases the background lock only on the 5th consecutive series poll error', () => {
      const consoleError = vi.spyOn(console, 'error').mockReturnValue(undefined);
      const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
      const releaseSpy = vi.spyOn(lockService, 'release').mockReturnValue(undefined);

      (component as any).startSeriesPolling(11);
      releaseSpy.mockClear();
      benchmarkServiceMock.getRunSeries.mockReturnValue(throwError(() => ({ status: 500 })));

      for (let i = 0; i < 4; i++) {
        (component as any).pollSeries(11);
      }
      expect(releaseSpy).not.toHaveBeenCalled();

      (component as any).pollSeries(11);
      expect(releaseSpy).toHaveBeenCalled();
      expect((component as any).seriesPollTickerHandle).toBeNull();
      expect(consoleError).toHaveBeenCalledWith('Failed to poll benchmark run series', expect.any(Object));
      component.ngOnDestroy();
    });

    it('keeps the series lock while a member run is polled and when that run poller stops', () => {
      const lockService = TestBed.inject(BenchmarkBackgroundActivityService);
      benchmarkServiceMock.getRunSeries.mockReturnValue(of({
        id: 11, status: 'Running', completedRunCount: 0, requestedRunCount: 3, members: []
      } as any));
      const acquireSeriesSpy = vi.spyOn(lockService, 'acquireForSeries').mockReturnValue(undefined);
      const acquireRunSpy = vi.spyOn(lockService, 'acquireForRun').mockReturnValue(undefined);
      const releaseSpy = vi.spyOn(lockService, 'release').mockReturnValue(undefined);

      (component as any).startSeriesPolling(11);
      expect(acquireSeriesSpy).toHaveBeenCalledTimes(1);
      expect(acquireSeriesSpy).toHaveBeenCalledWith(11);

      (component as any).startPolling(42);
      (component as any).stopPolling();

      expect(acquireRunSpy).not.toHaveBeenCalled();
      expect(releaseSpy).not.toHaveBeenCalled();

      (component as any).stopSeriesPolling();
      expect(releaseSpy).toHaveBeenCalledTimes(1);
      component.ngOnDestroy();
    });
  });

  describe('series banner lifecycle', () => {
    function attachSeries(status: string): void {
      component.activeSeries = {
        id: 2,
        suiteName: 'GnollHack Player Assistance Benchmark Suite',
        status,
        requestedRunCount: 3,
        completedRunCount: 3,
        failedRunCount: 0,
        members: [],
        resumable: false,
        allowCapWait: true
      } as unknown as typeof component.activeSeries;
      component.activeSeriesId = 2;
      component.multiRunDialogVisible = false;
    }

    // A banner describing work that is still going to produce something.
    ['Pending', 'Running', 'WaitingForCap'].forEach(status => {
      it(`should show the series banner while the series is ${status}`, () => {
        attachSeries(status);

        expect(component.seriesIsFinished).toBe(false);
        expect(component.seriesBannerVisible).toBe(true);
      });
    });

    // Stopped is the one non-terminal end state, and the only one with a Continue button to offer.
    it('should keep the series banner for a Stopped series, which is resumable', () => {
      attachSeries('Stopped');

      expect(component.seriesIsFinished).toBe(false);
      expect(component.seriesBannerVisible).toBe(true);
    });

    ['Completed', 'Cancelled', 'Failed'].forEach(status => {
      it(`should hide the series banner once the series is ${status}`, () => {
        attachSeries(status);

        expect(component.seriesIsFinished).toBe(true);
        expect(component.seriesBannerVisible).toBe(false);
      });
    });

    it('should not render the banner element for a completed series', () => {
      attachSeries('Completed');
      component.activeSubTab = 'run';
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.series-banner')).toBeNull();
    });

    it('should keep activeSeries so the dialog and run labelling still resolve it', () => {
      attachSeries('Completed');

      // Gated rendering, not cleared state: seriesIdForRun and the progress dialog read this after
      // the series ends.
      expect(component.activeSeries).not.toBeNull();
      expect(component.activeSeriesId).toBe(2);
    });

    it('should hide the banner while the progress dialog is open', () => {
      attachSeries('Running');
      component.multiRunDialogVisible = true;

      expect(component.seriesBannerVisible).toBe(false);
    });
  });
});
