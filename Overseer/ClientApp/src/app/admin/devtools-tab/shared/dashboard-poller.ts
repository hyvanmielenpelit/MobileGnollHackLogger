import { EMPTY, Observable, Subject, Subscription, catchError, map, of, switchMap, timer } from 'rxjs';

/** Poll cadence while the last answer reports work in progress. */
export const ACTIVE_POLL_MS = 1000;
/** Poll cadence while nothing runs. */
export const IDLE_POLL_MS = 15000;
/** Delays after the first, second and every later consecutive failed poll. */
export const ERROR_BACKOFF_MS: readonly number[] = [5000, 10000, 30000];

/**
 * One poll's outcome. A failure carries the consecutive failure count (1 for the first) and the
 * delay before the next attempt, so a host can say "Trying again in 5 s."
 */
export type PollResult<T> =
  | { readonly ok: true; readonly value: T }
  | { readonly ok: false; readonly error: unknown; readonly failures: number; readonly retryInMs: number };

type RawResult<T> = { ok: true; value: T } | { ok: false; error: unknown };

/**
 * The polling loop of a Services tab dashboard: `ACTIVE_POLL_MS` while `isActive` says work is in
 * progress, `IDLE_POLL_MS` otherwise, `ERROR_BACKOFF_MS` after failures, and paused while the page
 * is hidden.
 *
 * Every scheduling call replaces the pending poll and cancels a request in flight (`switchMap`), so
 * requests never stack. Not an Angular service: the host owns one per dashboard, calls `start()` in
 * `ngOnInit` and `stop()` in `ngOnDestroy`, forwards its `@HostListener('document:visibilitychange')`
 * to `onVisibilityChange`, and calls `markForCheck()` in `onResult`.
 *
 * `onResult` runs after the next poll is scheduled, so a `pollNow()` from inside it takes effect.
 */
export class DashboardPoller<T> {
  /** Each value replaces the pending poll: a delay in ms, or null to stop until rescheduled. */
  private readonly schedule$ = new Subject<number | null>();
  private subscription: Subscription | null = null;
  private failures = 0;
  private pendingDelayMs = 0;
  private paused = false;

  /**
   * @param load One status request; subscribed to once per poll.
   * @param isActive Whether an answer reports work in progress, which selects the fast cadence.
   * @param onResult Receives every answer and every failure.
   */
  constructor(
    private readonly load: () => Observable<T>,
    private readonly isActive: (value: T) => boolean,
    private readonly onResult: (result: PollResult<T>) => void
  ) {}

  /** True between `start()` and `stop()`. */
  get started(): boolean {
    return this.subscription !== null;
  }

  /** Failed polls since the last answer; 0 after a success. */
  get consecutiveFailures(): number {
    return this.failures;
  }

  /** Starts polling with an immediate poll. Does nothing while already started. */
  start(): void {
    if (this.subscription) {
      return;
    }
    this.failures = 0;
    this.paused = false;
    this.subscription = this.schedule$.pipe(
      switchMap(delay => delay === null
        ? EMPTY
        : timer(delay).pipe(
          switchMap(() => this.load().pipe(
            map((value): RawResult<T> => ({ ok: true, value })),
            catchError((error: unknown) => of<RawResult<T>>({ ok: false, error }))
          ))
        ))
    ).subscribe(result => this.handle(result));
    this.scheduleNext(0);
  }

  /** Polls now, cancelling a pending poll and any request in flight; for example after an action. */
  pollNow(): void {
    this.scheduleNext(0);
  }

  /** `pollNow()`, for a Retry button after a failed poll. */
  retry(): void {
    this.pollNow();
  }

  /** Stops polling and cancels a request in flight. `start()` begins again. */
  stop(): void {
    this.subscription?.unsubscribe();
    this.subscription = null;
    this.paused = false;
  }

  /**
   * Pauses while the page is hidden. When it is shown again, polls at once, unless the server was
   * failing, in which case the pending backoff delay starts over.
   */
  onVisibilityChange(hidden: boolean = document.hidden): void {
    if (!this.subscription) {
      return;
    }
    if (hidden) {
      this.paused = true;
      this.schedule$.next(null);
    } else if (this.paused) {
      this.paused = false;
      this.schedule$.next(this.failures > 0 ? this.pendingDelayMs : 0);
    }
  }

  private scheduleNext(delayMs: number): void {
    if (!this.subscription) {
      return;
    }
    this.pendingDelayMs = delayMs;
    if (document.hidden) {
      this.paused = true;
      this.schedule$.next(null);
      return;
    }
    this.schedule$.next(delayMs);
  }

  private handle(result: RawResult<T>): void {
    if (result.ok) {
      this.failures = 0;
      this.scheduleNext(this.isActive(result.value) ? ACTIVE_POLL_MS : IDLE_POLL_MS);
      this.onResult({ ok: true, value: result.value });
    } else {
      this.failures++;
      const delay = ERROR_BACKOFF_MS[Math.min(this.failures, ERROR_BACKOFF_MS.length) - 1];
      this.scheduleNext(delay);
      this.onResult({ ok: false, error: result.error, failures: this.failures, retryInMs: delay });
    }
  }
}
