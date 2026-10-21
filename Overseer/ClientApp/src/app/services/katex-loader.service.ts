import { Injectable } from '@angular/core';
import type katexDefault from 'katex';

export type Katex = typeof katexDefault;

/** The window members the prefetch uses; a spec passes a stub. */
export type PrefetchHost = Pick<Window, 'setTimeout' | 'clearTimeout'> &
  Partial<Pick<Window, 'scheduler' | 'requestIdleCallback' | 'cancelIdleCallback'>>;

/**
 * Loads KaTeX as a lazy chunk, so it stays out of the initial bundle. Nothing else may import
 * `katex` as a value.
 */
@Injectable({ providedIn: 'root' })
export class KatexLoaderService {
  private katex: Katex | null = null;
  private loading: Promise<Katex | null> | null = null;

  /** KaTeX if it has loaded, otherwise null. */
  get(): Katex | null {
    return this.katex;
  }

  /** Loads KaTeX once per page. Resolves null when the chunk cannot be fetched; that is not retried. */
  load(): Promise<Katex | null> {
    this.loading ??= import('katex').then(
      m => (this.katex = m.default),
      err => {
        console.warn('KaTeX could not be loaded; formulas are shown as TeX.', err);
        return null;
      });
    return this.loading;
  }

  /** Schedules load() at background priority. Returns a function that cancels an unstarted schedule. */
  prefetchWhenIdle(host: PrefetchHost = window): () => void {
    const run = () => { void this.load(); };

    if (host.scheduler?.postTask) {
      const controller = new AbortController();
      // Rejects with an AbortError when canceled.
      host.scheduler.postTask(run, { priority: 'background', signal: controller.signal }).catch(() => {});
      return () => controller.abort();
    }

    if (host.requestIdleCallback && host.cancelIdleCallback) {
      const handle = host.requestIdleCallback(run, { timeout: 5000 });
      return () => host.cancelIdleCallback!(handle);
    }

    const timer = host.setTimeout(run, 2000);
    return () => host.clearTimeout(timer);
  }
}
