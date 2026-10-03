import { formatElapsed } from '../admin/benchmark/benchmark-run-format';
import { elapsedMsBetween } from './date.util';
import { nextElapsedTickDelay, startElapsedTicker } from './elapsed-ticker';

describe('elapsed-ticker', () => {
  let hidden = false;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-04T12:00:00.000Z'));
    hidden = false;
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => hidden });
  });

  afterEach(() => {
    delete (document as unknown as { hidden?: boolean }).hidden;
    vi.useRealTimers();
  });

  /** A start `ms` before the fake now, as the server sends it: no zone designator. */
  function startedMsAgo(ms: number): string {
    return new Date(Date.now() - ms).toISOString().replace('Z', '');
  }

  function setHidden(value: boolean): void {
    hidden = value;
    document.dispatchEvent(new Event('visibilitychange'));
  }

  describe('formatElapsed', () => {
    it('shows whole seconds, floored, in the progress dialogs\' format', () => {
      expect(formatElapsed(0)).toBe('0s');
      expect(formatElapsed(999)).toBe('0s');
      expect(formatElapsed(1000)).toBe('1s');
      expect(formatElapsed(59_999)).toBe('59s');
      expect(formatElapsed(60_000)).toBe('1m 00s');
      expect(formatElapsed(3_725_000)).toBe('1h 02m 05s');
    });
  });

  describe('nextElapsedTickDelay', () => {
    it('waits until just past the next whole elapsed second', () => {
      expect(nextElapsedTickDelay(startedMsAgo(400))).toBe(620);
      expect(nextElapsedTickDelay(startedMsAgo(1020))).toBe(1000);
      expect(nextElapsedTickDelay(startedMsAgo(1990))).toBe(30);
      expect(nextElapsedTickDelay(startedMsAgo(2005))).toBe(1015);
    });

    it('falls back to a plain second when no start is known', () => {
      expect(nextElapsedTickDelay(null)).toBe(1000);
      expect(nextElapsedTickDelay(undefined)).toBe(1000);
    });
  });

  describe('startElapsedTicker', () => {
    it('advances the label by exactly one second per tick for ten ticks', () => {
      const start = startedMsAgo(400);
      const labels: string[] = [];
      const stop = startElapsedTicker(() => start, () => labels.push(formatElapsed(elapsedMsBetween(start, null))));

      vi.advanceTimersByTime(10_000);

      expect(labels).toEqual(['1s', '2s', '3s', '4s', '5s', '6s', '7s', '8s', '9s', '10s']);
      stop();
    });

    it('reads the start on every tick, so a changed start re-aligns the schedule', () => {
      let start: string | null = null;
      const onTick = vi.fn();
      const stop = startElapsedTicker(() => start, onTick);

      vi.advanceTimersByTime(1000);
      expect(onTick).toHaveBeenCalledTimes(1);

      start = startedMsAgo(700);
      // The tick scheduled before the start was known still lands after a plain second.
      vi.advanceTimersByTime(1000);
      expect(onTick).toHaveBeenCalledTimes(2);
      // From then on the ticks land just past each whole elapsed second: 1700 ms now, so 320 ms.
      vi.advanceTimersByTime(319);
      expect(onTick).toHaveBeenCalledTimes(2);
      vi.advanceTimersByTime(1);
      expect(onTick).toHaveBeenCalledTimes(3);
      stop();
    });

    it('does no work while the document is hidden, and ticks and re-aligns once it is visible', () => {
      const start = startedMsAgo(400);
      const onTick = vi.fn();
      hidden = true;
      const stop = startElapsedTicker(() => start, onTick);

      vi.advanceTimersByTime(5000);
      expect(onTick).not.toHaveBeenCalled();
      expect(vi.getTimerCount()).toBe(0);

      setHidden(false);
      expect(onTick).toHaveBeenCalledTimes(1);
      // 5400 ms elapsed, so the next boundary is 600 ms away, plus the margin.
      vi.advanceTimersByTime(619);
      expect(onTick).toHaveBeenCalledTimes(1);
      vi.advanceTimersByTime(1);
      expect(onTick).toHaveBeenCalledTimes(2);

      setHidden(true);
      vi.advanceTimersByTime(5000);
      expect(onTick).toHaveBeenCalledTimes(2);
      stop();
    });

    it('stops for good once the returned function is called', () => {
      const start = startedMsAgo(400);
      const onTick = vi.fn();
      const stop = startElapsedTicker(() => start, onTick);

      vi.advanceTimersByTime(620);
      expect(onTick).toHaveBeenCalledTimes(1);

      stop();
      vi.advanceTimersByTime(5000);
      setHidden(true);
      setHidden(false);
      expect(onTick).toHaveBeenCalledTimes(1);
      expect(vi.getTimerCount()).toBe(0);
    });
  });
});
