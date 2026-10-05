import type { Mock } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { NEVER, Observable, Subject, of, throwError } from 'rxjs';
import { AdminBenchmarkService } from '../../../services/admin-benchmark.service';
import { BenchmarkBackgroundActivityService } from '../../../services/benchmark-background-activity.service';
import { BenchmarkCompletionNotificationService } from '../../../services/benchmark-completion-notification.service';
import { BenchmarkCompletionSoundService } from '../../../services/benchmark-completion-sound.service';
import { BenchmarkPollTickerService } from '../../../services/benchmark-poll-ticker.service';
import { BenchmarkActiveRunMonitor } from './benchmark-active-run.monitor';
import { BenchmarkLauncherState } from './benchmark-launcher.state';
import { BenchmarkShellBridge } from './benchmark-shell-bridge.service';
import { BenchmarkViewSync } from './benchmark-view-sync.service';
import { BenchmarkWorkspaceStore } from './benchmark-workspace.store';

const RUN_POLL = BenchmarkActiveRunMonitor.RUN_POLL_INTERVAL_MS;

/** The series and battery pollers' cadence, private to the monitor. */
const SERIES_POLL = 5000;

describe('BenchmarkActiveRunMonitor: lost contact', () => {
  let monitor: BenchmarkActiveRunMonitor;
  let service: { getBatteryRun: Mock; getRunSeries: Mock; getRun: Mock; getRunReportJob: Mock };
  let background: { acquireForRun: Mock; acquireForSeries: Mock; acquireForBattery: Mock; release: Mock };
  let consoleError: Mock;
  let startMs: number;

  const failure = (): Observable<never> => throwError(() => ({ status: 503 }));

  const batteryRun = (currentRunId: number | null = null): unknown =>
    ({ id: 9, status: 'Running', currentRunId, members: [] });

  /** Answers each call with `respond()` and records when it was made, in ms since the spec began. */
  function recordCalls(mock: Mock, respond: () => Observable<unknown>): number[] {
    const times: number[] = [];
    mock.mockImplementation(() => {
      times.push(Date.now() - startMs);
      return respond();
    });
    return times;
  }

  beforeEach(() => {
    vi.useFakeTimers();
    consoleError = vi.spyOn(console, 'error').mockReturnValue(undefined) as unknown as Mock;
    startMs = Date.now();
    service = { getBatteryRun: vi.fn(), getRunSeries: vi.fn(), getRun: vi.fn(), getRunReportJob: vi.fn() };
    background = { acquireForRun: vi.fn(), acquireForSeries: vi.fn(), acquireForBattery: vi.fn(), release: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        BenchmarkActiveRunMonitor,
        { provide: AdminBenchmarkService, useValue: service },
        { provide: BenchmarkBackgroundActivityService, useValue: background },
        {
          // The plain setInterval path, so the fake clock drives every tick.
          provide: BenchmarkPollTickerService,
          useValue: {
            start: (intervalMs: number, onTick: () => void) => {
              const id = setInterval(onTick, intervalMs);
              const handle = (() => clearInterval(id)) as any;
              Object.defineProperty(handle, 'mode', { value: 'timer', enumerable: true });
              return handle;
            }
          }
        },
        { provide: BenchmarkViewSync, useValue: { notify: vi.fn() } },
        { provide: BenchmarkShellBridge, useValue: { openRunProgressDialog: vi.fn() } },
        {
          provide: BenchmarkWorkspaceStore,
          useValue: { loadHistory: vi.fn(), loadRunLimits: vi.fn(), loadRunGroups: vi.fn(), loadAllFootprints: vi.fn() }
        },
        { provide: BenchmarkLauncherState, useValue: { completionSound: false, completionNotification: false } },
        { provide: BenchmarkCompletionSoundService, useValue: { play: vi.fn(), arm: vi.fn() } },
        {
          provide: BenchmarkCompletionNotificationService,
          useValue: { notify: vi.fn(), permission: vi.fn(), requestPermission: vi.fn() }
        }
      ]
    });
    monitor = TestBed.inject(BenchmarkActiveRunMonitor);
  });

  afterEach(() => {
    monitor.ngOnDestroy();
    consoleError.mockRestore();
    vi.useRealTimers();
  });

  it('backs a failing battery poller off 5, 10, 20 and 40 s, then keeps polling every 60 s', () => {
    const attempts = recordCalls(service.getBatteryRun, failure);

    monitor.startBatteryPolling(9);
    vi.advanceTimersByTime(260_000);

    expect(attempts).toEqual([0, 5_000, 15_000, 35_000, 75_000, 135_000, 195_000, 255_000]);
    expect(monitor.lostContact).toEqual({
      kind: 'battery', id: 9, failureCount: 8, sinceMs: startMs, retryIntervalMs: 60_000, gaveUp: false
    });
    expect(background.release).not.toHaveBeenCalled();
  });

  it('raises the Lost contact notice on the second failed poll in a row, not the first', () => {
    recordCalls(service.getBatteryRun, failure);

    monitor.startBatteryPolling(9);
    expect(monitor.lostContact).toBeNull();
    expect(monitor.lostContactText).toBeNull();

    vi.advanceTimersByTime(5_000);
    expect(monitor.lostContact).toMatchObject({ kind: 'battery', id: 9, failureCount: 2, retryIntervalMs: 10_000, gaveUp: false });
    expect(monitor.lostContactText).toBe(
      'Lost contact with the server: the last 2 polls for Battery Run #9 failed. Retrying every 10 s for up to 10 minutes.');
  });

  it('gives up only after ten minutes of failures, keeping the notice and releasing the lock', () => {
    const attempts = recordCalls(service.getBatteryRun, failure);

    monitor.startBatteryPolling(9);
    vi.advanceTimersByTime(614_999);
    expect(attempts[attempts.length - 1]).toBe(555_000);
    expect(monitor.lostContact?.gaveUp).toBe(false);
    expect(background.release).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(attempts[attempts.length - 1]).toBe(615_000);
    expect(monitor.lostContact).toMatchObject({ kind: 'battery', id: 9, retryIntervalMs: null, gaveUp: true });
    expect(monitor.lostContactText).toContain('stopped following Battery Run #9');
    expect(monitor.lostContactText).toContain('reload the page');
    expect(background.release).toHaveBeenCalledTimes(1);

    const count = attempts.length;
    vi.advanceTimersByTime(300_000);
    expect(attempts.length).toBe(count);
    expect(monitor.lostContact?.gaveUp).toBe(true);
  });

  it('returns to the 5 s cadence and clears the notice when the server answers again', () => {
    let answering = false;
    const attempts = recordCalls(service.getBatteryRun, () => (answering ? of(batteryRun()) : failure()));

    monitor.startBatteryPolling(9);
    vi.advanceTimersByTime(5_000);
    expect(monitor.lostContact).not.toBeNull();

    answering = true;
    vi.advanceTimersByTime(25_000);

    expect(attempts).toEqual([0, 5_000, 15_000, 20_000, 25_000, 30_000]);
    expect(monitor.lostContact).toBeNull();
    expect(monitor.lostContactText).toBeNull();
  });

  it('backs a failing series poller off the same way, and gives up after ten minutes', () => {
    const attempts = recordCalls(service.getRunSeries, failure);

    monitor.startSeriesPolling(11);
    vi.advanceTimersByTime(140_000);

    expect(attempts).toEqual([0, 5_000, 15_000, 35_000, 75_000, 135_000]);
    expect(monitor.lostContact).toMatchObject({ kind: 'series', id: 11, failureCount: 6, retryIntervalMs: 60_000, gaveUp: false });
    expect(monitor.lostContactText).toContain('Series #11');
    expect(background.release).not.toHaveBeenCalled();

    vi.advanceTimersByTime(475_000);
    expect(attempts[attempts.length - 1]).toBe(615_000);
    expect(monitor.lostContact).toMatchObject({ kind: 'series', gaveUp: true });
    expect(background.release).toHaveBeenCalledTimes(1);
  });

  it('keeps a single run to its five failed polls, with no Lost contact notice', () => {
    const attempts = recordCalls(service.getRun, failure);

    monitor.startPolling(42);
    vi.advanceTimersByTime(20_000);

    expect(attempts).toEqual([0, 2_000, 4_000, 6_000, 8_000]);
    expect(monitor.pollTickerHandle).toBeNull();
    expect(monitor.lostContact).toBeNull();
  });

  it('restarts a member run poller that gave up once the battery poller reaches the server again', () => {
    let batteryAnswering = true;
    const batteryAttempts = recordCalls(service.getBatteryRun, () => (batteryAnswering ? of(batteryRun(41)) : failure()));
    let runAnswering = false;
    const runAttempts = recordCalls(service.getRun, () => (runAnswering ? of({ id: 41, status: 'Running', answers: [] }) : failure()));

    monitor.startBatteryPolling(9);
    expect(runAttempts).toEqual([0]);

    batteryAnswering = false;
    vi.advanceTimersByTime(8_000);
    expect(runAttempts).toEqual([0, 2_000, 4_000, 6_000, 8_000]);
    expect(monitor.pollTickerHandle).toBeNull();

    batteryAnswering = true;
    runAnswering = true;
    vi.advanceTimersByTime(2_000);

    expect(batteryAttempts).toEqual([0, 5_000, 10_000]);
    expect(runAttempts).toEqual([0, 2_000, 4_000, 6_000, 8_000, 10_000]);
    expect(monitor.pollTickerHandle).not.toBeNull();
    expect(monitor.activeRunId).toBe(41);
  });
});

describe('BenchmarkActiveRunMonitor: the viewed run and battery post-run work', () => {
  let monitor: BenchmarkActiveRunMonitor;
  let service: {
    getBatteryRun: Mock; getRunSeries: Mock; getRun: Mock; getRunReportJob: Mock;
    resumeBatteryRun: Mock; rerunFailedQuestions: Mock;
  };
  let background: { acquireForRun: Mock; acquireForSeries: Mock; acquireForBattery: Mock; release: Mock };
  let workspace: { loadHistory: Mock; loadRunLimits: Mock; loadRunGroups: Mock; loadAllFootprints: Mock };
  let launcher: { completionSound: boolean; completionNotification: boolean };
  let play: Mock;
  /** The status each run id answers with; a run missing here answers Running. */
  let runStatus: Map<number, string>;

  const runDetail = (id: number, status = 'Running'): unknown =>
    ({
      id, status, benchmarkSuiteId: 1, suiteName: 'Suite', totalQuestionCount: 3, answers: [],
      completedAtUtc: status === 'Running' ? null : '2026-10-03T12:00:00Z'
    });

  const member = (runId: number): unknown =>
    ({ memberId: runId, suiteIndex: 0, round: 1, runId, runStatus: 'Completed', usable: true, superseded: false });

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-03T12:00:00Z'));
    runStatus = new Map<number, string>();
    service = {
      getBatteryRun: vi.fn(),
      getRunSeries: vi.fn(),
      getRun: vi.fn((id: number) => of(runDetail(id, runStatus.get(id) ?? 'Running'))),
      getRunReportJob: vi.fn(() => of(null)),
      resumeBatteryRun: vi.fn(),
      rerunFailedQuestions: vi.fn(() => of({ runId: 0 }))
    };
    background = { acquireForRun: vi.fn(), acquireForSeries: vi.fn(), acquireForBattery: vi.fn(), release: vi.fn() };
    workspace = { loadHistory: vi.fn(), loadRunLimits: vi.fn(), loadRunGroups: vi.fn(), loadAllFootprints: vi.fn() };
    launcher = { completionSound: true, completionNotification: false };
    play = vi.fn(() => Promise.resolve('played'));

    TestBed.configureTestingModule({
      providers: [
        BenchmarkActiveRunMonitor,
        { provide: AdminBenchmarkService, useValue: service },
        { provide: BenchmarkBackgroundActivityService, useValue: background },
        {
          provide: BenchmarkPollTickerService,
          useValue: {
            start: (intervalMs: number, onTick: () => void) => {
              const id = setInterval(onTick, intervalMs);
              const handle = (() => clearInterval(id)) as any;
              Object.defineProperty(handle, 'mode', { value: 'timer', enumerable: true });
              return handle;
            }
          }
        },
        { provide: BenchmarkViewSync, useValue: { notify: vi.fn() } },
        { provide: BenchmarkShellBridge, useValue: { openRunProgressDialog: vi.fn() } },
        { provide: BenchmarkWorkspaceStore, useValue: workspace },
        { provide: BenchmarkLauncherState, useValue: launcher },
        { provide: BenchmarkCompletionSoundService, useValue: { play, arm: vi.fn() } },
        {
          provide: BenchmarkCompletionNotificationService,
          useValue: { notify: vi.fn(), permission: vi.fn(), requestPermission: vi.fn() }
        }
      ]
    });
    monitor = TestBed.inject(BenchmarkActiveRunMonitor);
  });

  afterEach(() => {
    monitor.ngOnDestroy();
    vi.useRealTimers();
  });

  /** The ids getRun was asked for, in order. */
  const runRequests = (): number[] => service.getRun.mock.calls.map(call => call[0] as number);

  describe('the viewed run', () => {
    it('follows the live run while no run is viewed', () => {
      monitor.activeRunId = 77;
      monitor.startPolling(77);

      expect(monitor.dialogRunId).toBe(77);
      expect(monitor.dialogRunDetail?.id).toBe(77);
      expect(monitor.dialogFollowsLiveRun).toBe(true);
    });

    it('starts no viewed poller while the viewed run is the live run, and one when the live run moves on', () => {
      monitor.activeRunId = 76;
      monitor.startPolling(76);
      monitor.viewRun(76);
      service.getRun.mockClear();

      vi.advanceTimersByTime(RUN_POLL);
      expect(runRequests()).toEqual([76]);
      expect(monitor.dialogRunDetail?.id).toBe(76);

      // The battery switches to 77: the dialog stays on 76, which a viewed poller now follows.
      monitor.activeRunId = 77;
      monitor.startPolling(77);
      expect(monitor.dialogRunId).toBe(76);
      expect(monitor.dialogRunDetail?.id).toBe(76);
      expect(monitor.activeRunDetail?.id).toBe(77);

      service.getRun.mockClear();
      vi.advanceTimersByTime(RUN_POLL);
      expect(runRequests().sort()).toEqual([76, 77]);
    });

    it('shows the live poller\'s detail of the viewed run until its own first response', () => {
      service.getRun.mockImplementation((id: number) => (id === 76 ? NEVER : of(runDetail(id))));
      monitor.activeRunId = 76;
      monitor.activeRunDetail = runDetail(76) as any;
      monitor.viewRun(76);

      monitor.activeRunId = 77;

      expect(monitor.dialogRunDetail?.id).toBe(76);
    });

    it('stops the viewed poller after the first terminal response that awaits nothing', () => {
      monitor.activeRunId = 77;
      monitor.startPolling(77);
      runStatus.set(76, 'Completed');
      monitor.viewRun(76);
      expect(monitor.dialogRunDetail?.id).toBe(76);

      service.getRun.mockClear();
      vi.advanceTimersByTime(RUN_POLL * 3);
      expect(runRequests().filter(id => id === 76)).toEqual([]);

      // A later change of the live run does not restart it.
      monitor.activeRunId = 78;
      monitor.startPolling(78);
      expect(runRequests().filter(id => id === 76)).toEqual([]);
    });

    it('keeps polling a viewed run while it is Running, and never signals its completion', async () => {
      monitor.activeRunId = 77;
      monitor.startPolling(77);
      monitor.viewRun(76);
      service.getRun.mockClear();

      vi.advanceTimersByTime(RUN_POLL * 2);
      expect(runRequests().filter(id => id === 76)).toEqual([76, 76]);

      runStatus.set(76, 'Completed');
      vi.advanceTimersByTime(RUN_POLL);
      expect(monitor.dialogRunDetail?.status).toBe('Completed');
      await Promise.resolve();
      expect(play).not.toHaveBeenCalled();
      expect(background.acquireForRun).not.toHaveBeenCalledWith(76);
    });

    it('discards a late response for another run or from an earlier viewed poller', () => {
      const pending = new Map<number, Subject<unknown>>();
      service.getRun.mockImplementation((id: number) => {
        const subject = new Subject<unknown>();
        pending.set(id, subject);
        return subject;
      });
      monitor.activeRunId = 77;
      monitor.viewRun(76);
      const first = pending.get(76)!;

      monitor.clearViewedRun();
      monitor.viewRun(75);
      first.next(runDetail(76));
      expect(monitor.viewedRunDetail).toBeNull();
      expect(monitor.dialogRunDetail).toBeNull();

      pending.get(75)!.next(runDetail(74));
      expect(monitor.dialogRunDetail).toBeNull();

      pending.get(75)!.next(runDetail(75));
      expect(monitor.dialogRunDetail?.id).toBe(75);
    });

    it('discards a live poller response that arrives after the live run moved on', () => {
      const pending = new Map<number, Subject<unknown>>();
      service.getRun.mockImplementation((id: number) => {
        const subject = new Subject<unknown>();
        pending.set(id, subject);
        return subject;
      });
      monitor.activeRunId = 76;
      monitor.startPolling(76);
      const late = pending.get(76)!;

      monitor.activeRunId = 77;
      monitor.startPolling(77);
      pending.get(77)!.next(runDetail(77));
      late.next(runDetail(76));

      expect(monitor.activeRunDetail?.id).toBe(77);
    });

    it('stops the viewed poller and forgets the run on clearViewedRun', () => {
      monitor.activeRunId = 77;
      monitor.startPolling(77);
      monitor.viewRun(76);

      monitor.clearViewedRun();
      service.getRun.mockClear();
      vi.advanceTimersByTime(RUN_POLL * 3);

      expect(monitor.viewedRunId).toBeNull();
      expect(monitor.viewedRunDetail).toBeNull();
      expect(monitor.dialogRunId).toBe(77);
      expect(runRequests().filter(id => id === 76)).toEqual([]);
    });

    it('stops the viewed poller when the monitor is destroyed', () => {
      monitor.activeRunId = 77;
      monitor.viewRun(76);

      monitor.ngOnDestroy();
      service.getRun.mockClear();
      vi.advanceTimersByTime(RUN_POLL * 3);

      expect(runRequests()).toEqual([]);
    });

    it('polls the viewed run\'s report job while its reports are written', () => {
      service.getRun.mockImplementation((id: number) => of(id === 76
        ? { ...(runDetail(76, 'Completed') as object), reportWriterModelConfigurationId: 9, reportDocumentsStatus: 2 }
        : runDetail(id)));
      service.getRunReportJob.mockReturnValue(of({ runId: 76, phase: 'Writing', status: 2 }));
      monitor.activeRunId = 77;
      monitor.viewRun(76);

      expect(service.getRunReportJob).toHaveBeenCalledWith(76);
      expect(monitor.dialogRunReportJob?.runId).toBe(76);
      expect(monitor.activeRunReportJob).toBeNull();
    });
  });

  describe('battery post-run work', () => {
    function battery(overrides: Record<string, unknown> = {}): unknown {
      return {
        id: 9, status: 'Running', currentRunId: null, members: [member(41)], suiteCount: 1, completedSuiteCount: 0,
        batteryName: 'Core', latestAnalysisId: null, analysisStale: false, completedAtUtc: null,
        reportWriterModelConfigurationId: null, reportDocumentsStatus: 0, postRunWork: 'None', repairingRunIds: [],
        ...overrides
      };
    }

    const finished = (overrides: Record<string, unknown> = {}): unknown => battery({
      status: 'Completed', completedSuiteCount: 1, completedAtUtc: '2026-10-03T12:00:00Z', ...overrides
    });

    it('keeps polling a finished battery while postRunWork is not None, then signals once and loads history', async () => {
      let answer: unknown = battery();
      service.getBatteryRun.mockImplementation(() => of(answer));
      monitor.startBatteryPolling(9);

      answer = finished({ postRunWork: 'Analysing', reportWriterModelConfigurationId: 3, reportDocumentsStatus: 0 });
      vi.advanceTimersByTime(SERIES_POLL);
      answer = finished({ postRunWork: 'WritingReports', latestAnalysisId: 5, reportWriterModelConfigurationId: 3, reportDocumentsStatus: 1 });
      vi.advanceTimersByTime(SERIES_POLL);
      answer = finished({ postRunWork: 'WritingReports', latestAnalysisId: 5, reportWriterModelConfigurationId: 3, reportDocumentsStatus: 2 });
      vi.advanceTimersByTime(SERIES_POLL);
      await Promise.resolve();
      expect(play).not.toHaveBeenCalled();
      expect(workspace.loadHistory).not.toHaveBeenCalled();

      answer = finished({ postRunWork: 'None', latestAnalysisId: 5, reportWriterModelConfigurationId: 3, reportDocumentsStatus: 3 });
      vi.advanceTimersByTime(SERIES_POLL);
      await Promise.resolve();
      expect(play).toHaveBeenCalledTimes(1);
      expect(play).toHaveBeenCalledWith('battery:9:5:Completed');
      expect(workspace.loadHistory).toHaveBeenCalledTimes(1);

      const polls = service.getBatteryRun.mock.calls.length;
      vi.advanceTimersByTime(SERIES_POLL * 4);
      expect(service.getBatteryRun.mock.calls.length).toBe(polls);
    });

    it('stops polling a finished battery as soon as postRunWork is None, with no grace window', () => {
      let answer: unknown = battery();
      service.getBatteryRun.mockImplementation(() => of(answer));
      monitor.startBatteryPolling(9);

      // No analysis yet and a writer without a job: only postRunWork decides.
      answer = finished({ postRunWork: 'None', latestAnalysisId: null, reportWriterModelConfigurationId: 3 });
      vi.advanceTimersByTime(SERIES_POLL);
      expect(workspace.loadHistory).toHaveBeenCalledTimes(1);
      const polls = service.getBatteryRun.mock.calls.length;
      vi.advanceTimersByTime(SERIES_POLL * 4);
      expect(service.getBatteryRun.mock.calls.length).toBe(polls);
    });

    it('reads a battery run without postRunWork, from an older server, as having none', () => {
      const older = finished() as Record<string, unknown>;
      delete older['postRunWork'];
      service.getBatteryRun.mockReturnValue(of(older));
      monitor.startBatteryPolling(9);

      expect(workspace.loadHistory).toHaveBeenCalledTimes(1);
      expect(monitor.batteriesSeenLive.has(9)).toBe(false);
    });

    it('lets no member signal while its battery awaits post-run work', async () => {
      service.getBatteryRun.mockReturnValue(of(battery({ currentRunId: 41 })));
      monitor.startBatteryPolling(9);
      expect(monitor.activeRunId).toBe(41);

      service.getBatteryRun.mockReturnValue(of(finished({
        postRunWork: 'WritingReports', reportWriterModelConfigurationId: 3, reportDocumentsStatus: 1
      })));
      vi.advanceTimersByTime(SERIES_POLL);
      runStatus.set(41, 'Completed');
      vi.advanceTimersByTime(RUN_POLL);
      await Promise.resolve();

      expect(play).not.toHaveBeenCalled();
    });
  });

  describe('one user action, one chain of server work, one signal', () => {
    /** Battery run 8: two suites, run 91 its second member, finished with errors before the repair. */
    function battery8(overrides: Record<string, unknown> = {}): any {
      return {
        id: 8, status: 'CompletedWithErrors', currentRunId: null, members: [member(90), member(91)],
        suiteCount: 2, completedSuiteCount: 1, batteryName: 'Core', latestAnalysisId: 11, analysisStale: false,
        completedAtUtc: '2026-10-03T11:00:00Z', reportWriterModelConfigurationId: null, reportDocumentsStatus: 0,
        resumable: false, postRunWork: 'None', repairingRunIds: [],
        ...overrides
      };
    }

    /** What getBatteryRun answers with. */
    let answer: unknown;

    beforeEach(() => {
      answer = battery8();
      service.getBatteryRun.mockImplementation(() => of(answer));
      // The page shows battery run 8, finished; nothing polls it.
      monitor.activeBatteryRunId = 8;
      monitor.activeBatteryRun = battery8();
    });

    /** Re-runs a member's failed questions, as the run report does; the first poll sees it Running. */
    function repairMember(runId: number): void {
      runStatus.set(runId, 'Running');
      monitor.launchFailedQuestionRerun(runId, [0]);
    }

    const playedKeys = (): string[] => play.mock.calls.map(call => call[0] as string);

    it('signals the battery 8 sequence exactly once, battery:8:…, after postRunWork returns to None', async () => {
      repairMember(91);
      answer = battery8({ postRunWork: 'Repairing', repairingRunIds: [91] });

      // Continue is refused while the member is re-run.
      service.resumeBatteryRun.mockReturnValue(throwError(() => ({
        status: 409, error: { message: 'Run 91 is being re-run; the battery run follows it when the re-run finishes.' }
      })));
      monitor.resumeActiveBattery('Continue');
      await Promise.resolve();
      expect(monitor.batteryErrorMessage).toContain('Run 91 is being re-run');
      expect(monitor.batteriesSeenLive.has(8)).toBe(false);
      expect(playedKeys()).toEqual([]);

      // The re-run finishes; the server finishes the battery run by itself.
      answer = battery8({ status: 'Completed', completedSuiteCount: 2, postRunWork: 'Analysing' });
      runStatus.set(91, 'Completed');
      vi.advanceTimersByTime(RUN_POLL);
      await Promise.resolve();
      expect(playedKeys()).toEqual([]);
      expect(monitor.batteriesSeenLive.has(8)).toBe(true);

      answer = battery8({
        status: 'Completed', completedSuiteCount: 2, latestAnalysisId: 12, postRunWork: 'WritingReports', reportDocumentsStatus: 2
      });
      vi.advanceTimersByTime(SERIES_POLL);
      await Promise.resolve();
      expect(playedKeys()).toEqual([]);

      answer = battery8({
        status: 'Completed', completedSuiteCount: 2, latestAnalysisId: 12, postRunWork: 'None', reportDocumentsStatus: 3
      });
      vi.advanceTimersByTime(SERIES_POLL * 4);
      vi.advanceTimersByTime(RUN_POLL * 4);
      await Promise.resolve();
      expect(playedKeys()).toEqual(['battery:8:12:Completed']);
    });

    it('signals the battery once when the server settled it before the member\'s terminal poll', async () => {
      repairMember(91);

      // The battery run's status moved while run 91 ran, and its post-run work is already over.
      answer = battery8({ status: 'Completed', completedSuiteCount: 2, latestAnalysisId: 12 });
      runStatus.set(91, 'Completed');
      vi.advanceTimersByTime(RUN_POLL);
      await Promise.resolve();

      expect(playedKeys()).toEqual(['battery:8:12:NotRequested']);
    });

    it('signals nothing for a refused Continue alone', async () => {
      service.resumeBatteryRun.mockReturnValue(throwError(() => ({ status: 409, error: { message: 'The run slot is busy.' } })));
      answer = battery8({ postRunWork: 'Repairing', repairingRunIds: [91] });

      monitor.resumeActiveBattery('Continue');
      vi.advanceTimersByTime(SERIES_POLL * 4);
      await Promise.resolve();

      expect(playedKeys()).toEqual([]);
      expect(monitor.batteriesSeenLive.has(8)).toBe(false);
      // The banner's copy is read once, and nothing polls the battery run afterwards.
      expect(service.getBatteryRun).toHaveBeenCalledTimes(1);
      expect(monitor.activeBatteryRun?.postRunWork).toBe('Repairing');
    });

    it('makes the battery run one this page signals only when Continue succeeds', async () => {
      service.resumeBatteryRun.mockReturnValue(of({ batteryRunId: 8 }));
      answer = battery8({ status: 'Running' });

      monitor.resumeActiveBattery('Continue');
      expect(monitor.batteriesSeenLive.has(8)).toBe(true);

      answer = battery8({ status: 'Completed', completedSuiteCount: 2, latestAnalysisId: 12 });
      vi.advanceTimersByTime(SERIES_POLL);
      await Promise.resolve();
      expect(playedKeys()).toEqual(['battery:8:12:NotRequested']);
    });

    it('signals run:… once for a run in no battery, without reading any battery run', async () => {
      monitor.activeBatteryRunId = null;
      monitor.activeBatteryRun = null;
      monitor.activeRunId = 50;
      monitor.startPolling(50);

      runStatus.set(50, 'Completed');
      vi.advanceTimersByTime(RUN_POLL * 3);
      await Promise.resolve();

      expect(playedKeys()).toEqual(['run:50:2026-10-03T12:00:00Z']);
      expect(service.getBatteryRun).not.toHaveBeenCalled();
    });

    it('signals the member run when its battery run stays settled and unmoved', async () => {
      answer = battery8({ status: 'Completed', completedSuiteCount: 2 });
      monitor.activeBatteryRun = battery8({ status: 'Completed', completedSuiteCount: 2 });
      repairMember(91);

      runStatus.set(91, 'Completed');
      vi.advanceTimersByTime(RUN_POLL);
      await Promise.resolve();

      expect(playedKeys()).toEqual(['run:91:2026-10-03T12:00:00Z']);
      expect(monitor.batteriesSeenLive.has(8)).toBe(false);
    });

    it('signals a second repair chain on the same battery run again, under its own key', async () => {
      repairMember(91);
      answer = battery8({ status: 'Completed', completedSuiteCount: 2, postRunWork: 'Analysing' });
      runStatus.set(91, 'Completed');
      vi.advanceTimersByTime(RUN_POLL);
      answer = battery8({ status: 'Completed', completedSuiteCount: 2, latestAnalysisId: 12 });
      vi.advanceTimersByTime(SERIES_POLL);
      await Promise.resolve();
      expect(playedKeys()).toEqual(['battery:8:12:NotRequested']);

      // A later repair of run 90 ends in a new analysis.
      repairMember(90);
      answer = battery8({
        status: 'Completed', completedSuiteCount: 2, latestAnalysisId: 12, postRunWork: 'Repairing', repairingRunIds: [90]
      });
      vi.advanceTimersByTime(SERIES_POLL);
      answer = battery8({ status: 'Completed', completedSuiteCount: 2, latestAnalysisId: 12, postRunWork: 'Analysing' });
      runStatus.set(90, 'Completed');
      vi.advanceTimersByTime(RUN_POLL);
      answer = battery8({ status: 'Completed', completedSuiteCount: 2, latestAnalysisId: 13 });
      vi.advanceTimersByTime(SERIES_POLL * 3);
      await Promise.resolve();

      expect(playedKeys()).toEqual(['battery:8:12:NotRequested', 'battery:8:13:NotRequested']);
    });

    it('signals the same settled state once, however often it is polled', async () => {
      service.resumeBatteryRun.mockReturnValue(of({ batteryRunId: 8 }));
      answer = battery8({ status: 'Running' });
      monitor.resumeActiveBattery('Continue');

      const settled = battery8({ status: 'Completed', completedSuiteCount: 2, latestAnalysisId: 12, reportDocumentsStatus: 3 });
      answer = settled;
      vi.advanceTimersByTime(SERIES_POLL);
      monitor.pollBatteryRun(8);
      monitor.pollBatteryRun(8);
      await Promise.resolve();

      expect(playedKeys()).toEqual(['battery:8:12:Completed']);
      expect(BenchmarkActiveRunMonitor.batterySignalKey({ ...settled })).toBe('battery:8:12:Completed');
    });

    it('keys a series by its end status', async () => {
      service.getRunSeries.mockReturnValue(of({ id: 4, status: 'Running', members: [], completedRunCount: 0, requestedRunCount: 2 }));
      monitor.startSeriesPolling(4);
      service.getRunSeries.mockReturnValue(of({ id: 4, status: 'Completed', members: [], completedRunCount: 2, requestedRunCount: 2 }));
      vi.advanceTimersByTime(SERIES_POLL);
      await Promise.resolve();

      expect(playedKeys()).toEqual(['series:4:Completed']);
    });
  });

  describe('completion signals', () => {
    it('records "error" when play() rejects, and still fires the notification', async () => {
      const notifications = TestBed.inject(BenchmarkCompletionNotificationService) as unknown as { notify: Mock };
      launcher.completionNotification = true;
      play.mockImplementation(() => Promise.reject(new Error('audio pipeline gone')));
      monitor.activeRunDetail = runDetail(5, 'Completed') as any;

      (monitor as any).signalCompletion('run:5');
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();

      expect(play).toHaveBeenCalledWith('run:5');
      expect(monitor.lastCompletionSoundOutcome).toBe('error');
      expect(notifications.notify).toHaveBeenCalledTimes(1);
      expect(notifications.notify).toHaveBeenCalledWith('run:5', 'AI Benchmark', expect.stringContaining('Run #5'));
    });
  });
});
