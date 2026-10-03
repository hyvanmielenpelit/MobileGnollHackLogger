import { elapsedMsBetween } from './date.util';

/** One elapsed-time step: progress dialogs show whole seconds. */
export const ELAPSED_TICK_MS = 1000;

/** How far past a whole-second boundary a tick lands, so a slightly early timer cannot repeat a second. */
export const ELAPSED_TICK_MARGIN_MS = 20;

/**
 * The delay until the elapsed time from `startedAtUtc` next crosses a whole second, plus
 * `ELAPSED_TICK_MARGIN_MS`; a plain `ELAPSED_TICK_MS` when no start is known.
 */
export function nextElapsedTickDelay(startedAtUtc: string | null | undefined): number {
  if (!startedAtUtc) return ELAPSED_TICK_MS;
  const elapsed = elapsedMsBetween(startedAtUtc, null);
  return ELAPSED_TICK_MS - (elapsed % ELAPSED_TICK_MS) + ELAPSED_TICK_MARGIN_MS;
}

function documentHidden(): boolean {
  return typeof document !== 'undefined' && document.hidden;
}

/**
 * Calls `onTick` once each time the elapsed time from `startedAtUtc()` crosses a whole second, through
 * a self-aligning `setTimeout` chain: each tick schedules the next for the next boundary, so a late
 * timer can neither skip nor repeat a second. Nothing is scheduled while the document is hidden; on
 * becoming visible again it ticks once and re-aligns. The start is read on every tick, so it may
 * change while the ticker runs. Returns the function that stops it.
 */
export function startElapsedTicker(startedAtUtc: () => string | null | undefined, onTick: () => void): () => void {
  let handle: ReturnType<typeof setTimeout> | null = null;
  let stopped = false;

  const clear = () => {
    if (handle !== null) {
      clearTimeout(handle);
      handle = null;
    }
  };

  const schedule = () => {
    clear();
    if (stopped || documentHidden()) return;
    handle = setTimeout(tick, nextElapsedTickDelay(startedAtUtc()));
  };

  const tick = () => {
    handle = null;
    if (stopped) return;
    if (!documentHidden()) onTick();
    schedule();
  };

  const onVisibilityChange = () => {
    if (stopped) return;
    if (documentHidden()) {
      clear();
      return;
    }
    onTick();
    schedule();
  };

  if (typeof document !== 'undefined') {
    document.addEventListener('visibilitychange', onVisibilityChange);
  }
  schedule();

  return () => {
    stopped = true;
    clear();
    if (typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', onVisibilityChange);
    }
  };
}
