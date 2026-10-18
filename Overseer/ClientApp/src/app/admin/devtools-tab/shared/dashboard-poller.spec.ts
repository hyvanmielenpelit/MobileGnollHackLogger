import { Observable, of, throwError } from 'rxjs';

import {
  ACTIVE_POLL_MS,
  DashboardPoller,
  ERROR_BACKOFF_MS,
  IDLE_POLL_MS,
  PollResult
} from './dashboard-poller';

interface Status {
  running: boolean;
}

describe('DashboardPoller', () => {
  let hidden = false;
  let answer: Status;
  let fail: boolean;
  let loads: number;
  let results: PollResult<Status>[];
  let poller: DashboardPoller<Status>;

  function load(): Observable<Status> {
    loads++;
    return fail ? throwError(() => new Error('down')) : of(answer);
  }

  beforeEach(() => {
    hidden = false;
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => hidden });
    answer = { running: false };
    fail = false;
    loads = 0;
    results = [];
    vi.useFakeTimers();
    poller = new DashboardPoller<Status>(load, s => s.running, r => results.push(r));
  });

  afterEach(() => {
    poller.stop();
    vi.useRealTimers();
    delete (document as unknown as { hidden?: boolean }).hidden;
  });

  it('has the documented cadences', () => {
    expect(ACTIVE_POLL_MS).toBe(1000);
    expect(IDLE_POLL_MS).toBe(15000);
    expect(ERROR_BACKOFF_MS).toEqual([5000, 10000, 30000]);
  });

  it('polls at once on start, then every 15 s while idle and every second while active', () => {
    poller.start();
    expect(poller.started).toBe(true);
    vi.advanceTimersByTime(0);
    expect(loads).toBe(1);
    expect(results).toEqual([{ ok: true, value: { running: false } }]);

    vi.advanceTimersByTime(IDLE_POLL_MS - 1);
    expect(loads).toBe(1);
    answer = { running: true };
    vi.advanceTimersByTime(1);
    expect(loads).toBe(2);

    vi.advanceTimersByTime(ACTIVE_POLL_MS - 1);
    expect(loads).toBe(2);
    vi.advanceTimersByTime(1);
    expect(loads).toBe(3);
  });

  it('backs off 5, 10 and 30 s after failures, reports the count and the delay, and resets on success', () => {
    fail = true;
    poller.start();
    vi.advanceTimersByTime(0);
    expect(loads).toBe(1);
    vi.advanceTimersByTime(4999);
    expect(loads).toBe(1);
    vi.advanceTimersByTime(1);
    expect(loads).toBe(2);
    vi.advanceTimersByTime(10000);
    expect(loads).toBe(3);
    vi.advanceTimersByTime(30000);
    expect(loads).toBe(4);
    vi.advanceTimersByTime(30000);
    expect(loads).toBe(5);

    const failures = results.map(r => r.ok ? null : [r.failures, r.retryInMs]);
    expect(failures).toEqual([[1, 5000], [2, 10000], [3, 30000], [4, 30000], [5, 30000]]);
    expect(poller.consecutiveFailures).toBe(5);
    const first = results[0];
    expect(first.ok).toBe(false);
    expect(first.ok ? null : (first.error as Error).message).toBe('down');

    fail = false;
    poller.retry();
    vi.advanceTimersByTime(0);
    expect(loads).toBe(6);
    expect(results[results.length - 1].ok).toBe(true);
    expect(poller.consecutiveFailures).toBe(0);
    vi.advanceTimersByTime(IDLE_POLL_MS);
    expect(loads).toBe(7);
  });

  it('polls now on request, replacing the pending poll and cancelling a request in flight', () => {
    let unsubscribed = 0;
    let pending = true;
    poller = new DashboardPoller<Status>(
      () => {
        loads++;
        return pending
          ? new Observable<Status>(() => () => unsubscribed++)
          : of(answer);
      },
      s => s.running,
      r => results.push(r));

    poller.start();
    vi.advanceTimersByTime(0);
    expect(loads).toBe(1);

    pending = false;
    poller.pollNow();
    vi.advanceTimersByTime(0);
    expect(unsubscribed).toBe(1);
    expect(loads).toBe(2);
    expect(results.length).toBe(1);

    vi.advanceTimersByTime(5000);
    poller.pollNow();
    vi.advanceTimersByTime(0);
    expect(loads).toBe(3);
    vi.advanceTimersByTime(IDLE_POLL_MS - 1);
    expect(loads).toBe(3);
    vi.advanceTimersByTime(1);
    expect(loads).toBe(4);
  });

  it('lets onResult ask for an immediate poll', () => {
    let asked = false;
    poller = new DashboardPoller<Status>(load, s => s.running, () => {
      if (!asked) {
        asked = true;
        poller.pollNow();
      }
    });
    poller.start();
    // A zero delay scheduled while fake timers tick runs 1 ms later.
    vi.advanceTimersByTime(1);
    expect(loads).toBe(2);
    vi.advanceTimersByTime(IDLE_POLL_MS - 2);
    expect(loads).toBe(2);
  });

  it('pauses while the page is hidden and polls at once when it is shown', () => {
    poller.start();
    vi.advanceTimersByTime(0);
    hidden = true;
    poller.onVisibilityChange(true);
    vi.advanceTimersByTime(IDLE_POLL_MS * 4);
    expect(loads).toBe(1);

    hidden = false;
    poller.onVisibilityChange(false);
    vi.advanceTimersByTime(0);
    expect(loads).toBe(2);
  });

  it('reads document.hidden when no value is passed, and does not poll on start while hidden', () => {
    hidden = true;
    poller.start();
    vi.advanceTimersByTime(IDLE_POLL_MS);
    expect(loads).toBe(0);

    hidden = false;
    poller.onVisibilityChange();
    vi.advanceTimersByTime(0);
    expect(loads).toBe(1);
  });

  it('keeps the backoff delay when the page is shown while the server was failing', () => {
    fail = true;
    poller.start();
    vi.advanceTimersByTime(0);
    hidden = true;
    poller.onVisibilityChange(true);
    vi.advanceTimersByTime(60000);
    expect(loads).toBe(1);

    hidden = false;
    poller.onVisibilityChange(false);
    vi.advanceTimersByTime(4999);
    expect(loads).toBe(1);
    vi.advanceTimersByTime(1);
    expect(loads).toBe(2);
  });

  it('ignores a visibility change that is not a return from hidden', () => {
    poller.start();
    vi.advanceTimersByTime(0);
    poller.onVisibilityChange(false);
    vi.advanceTimersByTime(IDLE_POLL_MS - 1);
    expect(loads).toBe(1);
  });

  it('stops polling, ignores requests while stopped, and starts again', () => {
    poller.start();
    vi.advanceTimersByTime(0);
    poller.stop();
    expect(poller.started).toBe(false);
    poller.pollNow();
    poller.onVisibilityChange(false);
    vi.advanceTimersByTime(IDLE_POLL_MS * 4);
    expect(loads).toBe(1);

    poller.start();
    vi.advanceTimersByTime(0);
    expect(loads).toBe(2);
  });
});
