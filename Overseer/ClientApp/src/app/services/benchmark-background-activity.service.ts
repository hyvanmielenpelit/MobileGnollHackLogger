import { Injectable } from '@angular/core';

export type BenchmarkLockState = 'unsupported' | 'idle' | 'requested' | 'held' | 'error';

interface LockRequest {
  name: string;
  release: (() => void) | null;
  cancelled: boolean;
}

/**
 * A best-effort Web Lock held for the duration of one watched live run or series, so this tab
 * can be recognised as doing background work while its poller keeps ticking in a hidden tab.
 * The lock name is unique per operation (`overseer-benchmark-live:run:<id>` or
 * `overseer-benchmark-live:series:<id>`), so two tabs watching different runs never queue
 * behind each other; two tabs watching the *same* run or series legitimately do.
 *
 * Scope, stated honestly: Chrome lists a held Web Lock among the conditions that exempt a tab
 * from Energy Saver tab freezing, but the Web Locks API is a cross-context coordination
 * primitive, not a power-management guarantee. Holding one is no protection against timer
 * throttling, tab suspension, tab discard, or the operating system itself sleeping, on any
 * browser.
 */
@Injectable({
  providedIn: 'root'
})
export class BenchmarkBackgroundActivityService {
  private current: LockRequest | null = null;

  state: BenchmarkLockState = 'unsupported';
  lastError: string | null = null;

  isSupported(): boolean {
    const locks = this.lockManager();
    return !!locks && typeof locks.request === 'function';
  }

  /** The name of the lock currently requested or held; null once released. */
  get heldName(): string | null {
    return this.current?.name ?? null;
  }

  /** Acquires the lock for a watched run, first releasing whatever this instance held before. */
  acquireForRun(runId: number): void {
    this.acquire(`overseer-benchmark-live:run:${runId}`);
  }

  /** Acquires the lock for a watched series, first releasing whatever this instance held before. */
  acquireForSeries(seriesId: number): void {
    this.acquire(`overseer-benchmark-live:series:${seriesId}`);
  }

  /** Releases the currently held or requested lock, if any. Safe to call when nothing is held. */
  release(): void {
    const lock = this.current;
    if (lock) {
      lock.cancelled = true;
      lock.release?.();
      lock.release = null;
    }
    this.current = null;
    if (this.state === 'held' || this.state === 'requested') {
      this.state = 'idle';
    }
  }

  /** The browser's lock manager; a seam for tests, since `navigator.locks` is read-only. */
  protected lockManager(): LockManager | undefined {
    return typeof navigator !== 'undefined' ? navigator.locks : undefined;
  }

  private acquire(name: string): void {
    this.release();
    const lock: LockRequest = { name, release: null, cancelled: false };
    this.current = lock;
    this.lastError = null;

    const locks = this.lockManager();
    if (!locks || typeof locks.request !== 'function') {
      this.state = 'unsupported';
      return;
    }

    this.state = 'requested';
    /*
     * Never awaited: the callback's promise is what keeps the lock held, so the request settles
     * only on release. The callback can run after this request was released or replaced; it
     * then returns at once, so a superseded lock is never held.
     */
    locks.request(name, () => {
      if (lock.cancelled) {
        return Promise.resolve();
      }
      return new Promise<void>(resolve => {
        lock.release = resolve;
        if (this.current === lock) {
          this.state = 'held';
        }
      });
    }).catch((err: unknown) => {
      if (this.current === lock) {
        this.state = 'error';
        this.lastError = err instanceof Error ? err.message : String(err);
      }
    });
  }
}
