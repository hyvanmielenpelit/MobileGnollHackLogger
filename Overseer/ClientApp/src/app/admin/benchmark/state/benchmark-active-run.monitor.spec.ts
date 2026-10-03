import type { Mock } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
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
