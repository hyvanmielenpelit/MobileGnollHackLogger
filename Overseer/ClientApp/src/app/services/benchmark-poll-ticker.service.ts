import { Injectable } from '@angular/core';

/** Which path actually delivers a started ticker's ticks. */
export type BenchmarkPollTickerMode = 'worker' | 'timer';

/**
 * A started ticker's disposer: call it to stop the ticker and release whatever it holds. `mode`
 * reports which path is currently serving it — 'worker' until and unless a post-start failure
 * falls back to 'timer', at which point it flips and stays flipped.
 */
export interface BenchmarkPollTickerHandle {
  (): void;
  readonly mode: BenchmarkPollTickerMode;
}

const WORKER_URL = '/workers/benchmark-poll-ticker.js';

/**
 * Drives a run or series poller's periodic tick from a dedicated Worker rather than the main
 * thread's `setInterval`, which Chrome, Firefox and Safari all throttle to about once a minute
 * once a tab is hidden, occluded or minimized. A worker's own timers are not subject to that
 * throttling, and its `message` events run the tick handler promptly; the tab's Web Lock
 * (`BenchmarkBackgroundActivityService`, held for the same run or series) already exempts it from
 * Chrome's Energy Saver freezing, which would stop a worker's timers too.
 *
 * Falls back to a plain `setInterval` when `Worker` does not exist, construction throws, or the
 * worker reports an error after starting — a same-origin script under `public/workers/` should
 * never fail to load, but a browser that blocks it still gets ticks, just throttled ones. A
 * post-start fallback swaps the delivery mechanism under the same handle rather than replacing it,
 * since `start()` has already returned its disposer to the caller by the time an error can fire.
 *
 * Each `start()` owns one worker or one interval; the run poller and the series poller call this
 * independently and never share state.
 */
@Injectable({
  providedIn: 'root'
})
export class BenchmarkPollTickerService {
  start(intervalMs: number, onTick: () => void): BenchmarkPollTickerHandle {
    const workerHandle = this.tryStartWorker(intervalMs, onTick);
    if (workerHandle) return workerHandle;
    return this.startTimer(intervalMs, onTick);
  }

  private tryStartWorker(intervalMs: number, onTick: () => void): BenchmarkPollTickerHandle | null {
    if (typeof Worker === 'undefined') return null;

    let worker: Worker;
    try {
      worker = new Worker(WORKER_URL);
    } catch {
      return null;
    }

    const modeRef: { mode: BenchmarkPollTickerMode } = { mode: 'worker' };
    let fallbackTimerId: ReturnType<typeof setInterval> | null = null;
    let stopped = false;

    const fallBackToTimer = () => {
      if (stopped || fallbackTimerId !== null) return;
      modeRef.mode = 'timer';
      fallbackTimerId = setInterval(onTick, intervalMs);
      try { worker.terminate(); } catch { /* already gone */ }
    };

    worker.onmessage = (ev: MessageEvent) => {
      if (!stopped && ev.data?.type === 'tick') onTick();
    };
    worker.onerror = () => fallBackToTimer();

    try {
      worker.postMessage({ type: 'start', intervalMs });
    } catch {
      fallBackToTimer();
    }

    const stop = () => {
      if (stopped) return;
      stopped = true;
      if (fallbackTimerId !== null) {
        clearInterval(fallbackTimerId);
        return;
      }
      try { worker.postMessage({ type: 'stop' }); } catch { /* already gone */ }
      try { worker.terminate(); } catch { /* already gone */ }
    };

    return this.asHandle(stop, modeRef);
  }

  private startTimer(intervalMs: number, onTick: () => void): BenchmarkPollTickerHandle {
    const id = setInterval(onTick, intervalMs);
    return this.asHandle(() => clearInterval(id), { mode: 'timer' });
  }

  private asHandle(stop: () => void, modeRef: { mode: BenchmarkPollTickerMode }): BenchmarkPollTickerHandle {
    const handle = stop as unknown as { (): void; mode?: BenchmarkPollTickerMode };
    Object.defineProperty(handle, 'mode', { get: () => modeRef.mode, enumerable: true });
    return handle as BenchmarkPollTickerHandle;
  }
}
