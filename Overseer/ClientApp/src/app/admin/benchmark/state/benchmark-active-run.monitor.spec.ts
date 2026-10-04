import type { Mock } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { NEVER, Observable, Subject, of, throwError } from 'rxjs';
import { AdminBenchmarkService } from '../../../services/admin-benchmark.service';
import { BenchmarkBackgroundActivityService } from '../../../services/benchmark-background-activity.service';
import { BenchmarkCompletionNotificationService } from '../../../services/benchmark-completion-notification.service';
import { BenchmarkCompletionSoundService } from '../../../services/benchmark-completion-sound.service';
import { BenchmarkPollTickerService } from '../../../services/benchmark-poll-ticker.service';
import { BATTERY_POST_RUN_GRACE_MS } from '../batteries/battery.models';
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
  let service: { getBatteryRun: Mock; getRunSeries: Mock; getRun: Mock; getRunReportJob: Mock };
  let background: { acquireForRun: Mock; acquireForSeries: Mock; acquireForBattery: Mock; release: Mock };
  let workspace: { loadHistory: Mock; loadRunLimits: Mock; loadRunGroups: Mock; loadAllFootprints: Mock };
  let launcher: { completionSound: boolean; completionNotification: boolean };
  let play: Mock;
  /** The status each run id answers with; a run missing here answers Running. */
  let runStatus: Map<number, string>;

  const runDetail = (id: number, status = 'Running'): unknown =>
    ({ id, status, benchmarkSuiteId: 1, suiteName: 'Suite', totalQuestionCount: 3, answers: [] });

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
      getRunReportJob: vi.fn(() => of(null))
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
        reportWriterModelConfigurationId: null, reportDocumentsStatus: 0,
        ...overrides
      };
    }

    const finished = (overrides: Record<string, unknown> = {}): unknown => battery({
      status: 'Completed', completedSuiteCount: 1, completedAtUtc: '2026-10-03T12:00:00Z', ...overrides
    });

    it('keeps polling a finished battery through its analysis and reports, then signals once and loads history', async () => {
      let answer: unknown = battery();
      service.getBatteryRun.mockImplementation(() => of(answer));
      monitor.startBatteryPolling(9);

      answer = finished({ reportWriterModelConfigurationId: 3, reportDocumentsStatus: 0 });
      vi.advanceTimersByTime(SERIES_POLL);
      answer = finished({ latestAnalysisId: 5, reportWriterModelConfigurationId: 3, reportDocumentsStatus: 1 });
      vi.advanceTimersByTime(SERIES_POLL);
      answer = finished({ latestAnalysisId: 5, reportWriterModelConfigurationId: 3, reportDocumentsStatus: 2 });
      vi.advanceTimersByTime(SERIES_POLL);
      await Promise.resolve();
      expect(play).not.toHaveBeenCalled();
      expect(workspace.loadHistory).not.toHaveBeenCalled();

      answer = finished({ latestAnalysisId: 5, reportWriterModelConfigurationId: 3, reportDocumentsStatus: 3 });
      vi.advanceTimersByTime(SERIES_POLL);
      await Promise.resolve();
      expect(play).toHaveBeenCalledTimes(1);
      expect(play).toHaveBeenCalledWith('battery:9');
      expect(workspace.loadHistory).toHaveBeenCalledTimes(1);

      const polls = service.getBatteryRun.mock.calls.length;
      vi.advanceTimersByTime(SERIES_POLL * 4);
      expect(service.getBatteryRun.mock.calls.length).toBe(polls);
    });

    it('stops waiting for the analysis once the grace has passed', () => {
      let answer: unknown = battery();
      service.getBatteryRun.mockImplementation(() => of(answer));
      monitor.startBatteryPolling(9);

      answer = finished();
      vi.advanceTimersByTime(SERIES_POLL);
      expect(workspace.loadHistory).not.toHaveBeenCalled();

      vi.advanceTimersByTime(BATTERY_POST_RUN_GRACE_MS);
      expect(workspace.loadHistory).toHaveBeenCalledTimes(1);
      const polls = service.getBatteryRun.mock.calls.length;
      vi.advanceTimersByTime(SERIES_POLL * 4);
      expect(service.getBatteryRun.mock.calls.length).toBe(polls);
    });

    it('lets no member signal while its battery awaits post-run work', async () => {
      service.getBatteryRun.mockReturnValue(of(battery({ currentRunId: 41 })));
      monitor.startBatteryPolling(9);
      expect(monitor.activeRunId).toBe(41);

      service.getBatteryRun.mockReturnValue(of(finished({ reportWriterModelConfigurationId: 3, reportDocumentsStatus: 1 })));
      vi.advanceTimersByTime(SERIES_POLL);
      runStatus.set(41, 'Completed');
      vi.advanceTimersByTime(RUN_POLL);
      await Promise.resolve();

      expect(play).not.toHaveBeenCalled();
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
