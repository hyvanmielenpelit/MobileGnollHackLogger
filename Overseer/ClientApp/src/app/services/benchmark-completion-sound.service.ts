import { Injectable } from '@angular/core';

/** Every outcome `play` can resolve to. `prime` resolves to the first three; neither ever rejects. */
export type BenchmarkCompletionSoundOutcome = 'played' | 'blocked' | 'unsupported' | 'duplicate' | 'deferred';

/** Which playback path an attempt used. */
export type BenchmarkCompletionSoundPath = 'buffer' | 'element';

/** One `play()` or `prime()` call, kept for the run diagnostics capture. `key` is `'test'` for `prime`. */
export interface BenchmarkCompletionSoundAttempt {
  atUtc: string;
  key: string;
  hidden: boolean;
  focused: boolean;
  path: BenchmarkCompletionSoundPath;
  contextStateBefore: AudioContextState | null;
  contextStateAfter: AudioContextState | null;
  /** `null` when the buffer path was never reached; otherwise whether its liveness check passed. */
  clockAdvanced: boolean | null;
  /** Whether a stalled context was closed and replaced during this attempt. */
  rebuilt: boolean;
  outcome: BenchmarkCompletionSoundOutcome;
}

const OPUS_URL = '/audio/AIBenchmarkingComplete.opus';
const M4A_URL = '/audio/AIBenchmarkingComplete.m4a';
const GAIN = 0.6;
/** How long a hidden tab's element playback may stay pending before it counts as 'deferred'. */
const DEFERRED_MS = 2000;
/** How long `ctx.resume()` may stay pending before the buffer path gives up on this attempt. */
const RESUME_TIMEOUT_MS = 1000;
/** How long after `source.start(0)` the context clock is checked for having actually advanced. */
const CLOCK_CHECK_MS = 250;
/** How many attempt records `play()`/`prime()` keep for the diagnostics capture. */
const MAX_ATTEMPTS = 10;
/** How many context state transitions {@link diagnostics} keeps, oldest dropped first. */
const MAX_CONTEXT_STATE_EVENTS = 10;

/** One `AudioContext.onstatechange` firing, kept for the run diagnostics capture. */
export interface BenchmarkCompletionSoundContextStateEvent {
  atUtc: string;
  state: AudioContextState;
}

/**
 * The chime the AI Benchmark run tab plays when a run or series finishes. Two playback paths
 * exist side by side:
 *
 * - A Web Audio `AudioContext` with the chime pre-decoded into an `AudioBuffer`, armed by
 *   {@link arm} from a user gesture.
 * - The original `HTMLAudioElement` with an Opus source ahead of an AAC fallback.
 *
 * Which path is tried first depends on the tab's own visibility, not on which path "works
 * better" in the abstract: a visible, focused tab tries the element first, because that is the
 * path a normal `play()` call was always meant to take and a silent 'played' from the buffer path
 * on a visible tab (observed on run 59, cause never reproduced) is worse than a slightly less
 * robust element attempt the operator can actually hear fail. A hidden tab tries the buffer path
 * first, because the buffer and its context were armed while the tab still had focus and an
 * element's first `play()` can be deferred by the browser until the tab is shown again. Either
 * order falls through to the other path on a failure, so the chime still tries everything it can
 * before giving up. `prime()` — the *Test sound* button — follows the same order.
 *
 * The buffer path adds two defences a `source.start(0)` that never throws does not, on its own,
 * rule out: `ctx.resume()` can stay pending forever on some platforms, so it races against a
 * {@link RESUME_TIMEOUT_MS} timer and falls through rather than hang; and the context's own clock
 * can stay frozen even after a buffer source reports started, so every buffer attempt checks
 * `ctx.currentTime` {@link CLOCK_CHECK_MS} after `start(0)` and, if it has not moved, closes the
 * context, builds a new one (reusing the already-decoded `AudioBuffer`, which is not tied to the
 * context that decoded it) and tries once more before giving up on the buffer path entirely. A
 * stalled source is always stopped before another path plays, so one completion never chimes
 * twice. The last {@link MAX_ATTEMPTS} attempts, from both `play()` and `prime()`, are kept on
 * {@link diagnostics} for the run diagnostics capture.
 *
 * `play()` deduplicates per key within the page's lifetime: a run or series id that already
 * chimed once does not chime again for a second terminal poll of the same entity. `prime()`
 * is not deduplicated — it exists for the *Test sound* button, which the operator may press
 * more than once, and which doubles as the user gesture that unlocks autoplay on browsers
 * that require one before any later, gesture-less `play()` is allowed to produce sound.
 */
@Injectable({
  providedIn: 'root'
})
export class BenchmarkCompletionSoundService {
  private audio: HTMLAudioElement | null = null;
  private readonly playedKeys = new Set<string>();

  private audioContext: AudioContext | null = null;
  private decodedBuffer: AudioBuffer | null = null;
  private armPromise: Promise<void> | null = null;
  private arming = false;

  private readonly attempts: BenchmarkCompletionSoundAttempt[] = [];
  private readonly contextStateEvents: BenchmarkCompletionSoundContextStateEvent[] = [];

  /** Milliseconds from a deferred element play() to its late settle. Diagnostics only. */
  lastDeferredSettleMs: number | null = null;
  /** Which path the most recent play()/prime() actually used. Diagnostics only. */
  lastPlayPath: BenchmarkCompletionSoundPath | null = null;

  /** A snapshot for the run diagnostics capture; nothing here drives playback behaviour. */
  get diagnostics(): {
    arming: boolean;
    armed: boolean;
    audioContextState: AudioContextState | null;
    lastPlayPath: BenchmarkCompletionSoundPath | null;
    lastDeferredSettleMs: number | null;
    attempts: BenchmarkCompletionSoundAttempt[];
    contextStateEvents: BenchmarkCompletionSoundContextStateEvent[];
  } {
    return {
      arming: this.arming,
      armed: this.decodedBuffer !== null,
      audioContextState: this.audioContext?.state ?? null,
      lastPlayPath: this.lastPlayPath,
      lastDeferredSettleMs: this.lastDeferredSettleMs,
      attempts: this.attempts.slice(),
      contextStateEvents: this.contextStateEvents.slice()
    };
  }

  private ensureAudio(): HTMLAudioElement | null {
    if (this.audio) return this.audio;
    if (typeof Audio === 'undefined') return null;

    const audio = new Audio();
    audio.preload = 'auto';
    audio.volume = GAIN;

    const opusSource = document.createElement('source');
    opusSource.src = OPUS_URL;
    opusSource.type = 'audio/ogg; codecs=opus';
    audio.appendChild(opusSource);

    const aacSource = document.createElement('source');
    aacSource.src = M4A_URL;
    aacSource.type = 'audio/mp4; codecs="mp4a.40.2"';
    audio.appendChild(aacSource);

    audio.load();
    this.audio = audio;
    return audio;
  }

  private getAudioContextCtor(): (new () => AudioContext) | undefined {
    return (window as unknown as { AudioContext?: new () => AudioContext }).AudioContext
      ?? (window as unknown as { webkitAudioContext?: new () => AudioContext }).webkitAudioContext;
  }

  /**
   * Every context this service creates gets one, so a stall or a rebuild leaves a trail in
   * {@link diagnostics} even when nothing else about that attempt does. Purely observational —
   * nothing here gates or delays playback.
   */
  private attachStateChangeLogging(ctx: AudioContext): void {
    ctx.onstatechange = () => {
      this.contextStateEvents.push({ atUtc: new Date().toISOString(), state: ctx.state });
      if (this.contextStateEvents.length > MAX_CONTEXT_STATE_EVENTS) {
        this.contextStateEvents.shift();
      }
    };
  }

  /**
   * Idempotent: safe to call from any click handler on every arming action, and safe to call
   * concurrently. Never rejects. Must be called synchronously in the click handler's own call
   * stack — see the ordering note on {@link doArm} for why.
   */
  async arm(): Promise<void> {
    if (this.armPromise) return this.armPromise;
    this.arming = true;
    const attempt = this.doArm();
    this.armPromise = attempt;
    try {
      await attempt;
    } finally {
      this.arming = false;
      // A fully failed arm (no decoded buffer at all) is allowed to retry on the next gesture;
      // a successful one stays cached so a second arm() this session is a genuine no-op.
      if (!this.decodedBuffer) {
        this.armPromise = null;
      }
    }
  }

  /**
   * Order matters here and is dictated by user-activation rules, not by convenience: the silent
   * buffer must be scheduled before this function's first `await`, because that await is the
   * moment control returns to the event loop and the browser stops treating the call stack as
   * originating from the user's gesture. Everything after the buffer starts — resuming the
   * context, loading the fallback element, fetching and decoding the real chime — can freely
   * await, because by then the activation has already been spent on the buffer.
   */
  private async doArm(): Promise<void> {
    const AudioContextCtor = this.getAudioContextCtor();

    let ctx = this.audioContext;
    let resumePromise: Promise<void> | null = null;

    if (AudioContextCtor) {
      if (!ctx) {
        try {
          ctx = new AudioContextCtor();
          this.attachStateChangeLogging(ctx);
          this.audioContext = ctx;
        } catch {
          ctx = null;
        }
      }
      if (ctx) {
        // Fire and forget: awaiting resume() here would happen before the silent buffer starts.
        resumePromise = ctx.resume().catch(() => undefined);
        this.startSilentBuffer(ctx);
      }
    }

    // Loaded now, while the tab is visible, so a later gesture-less play() on the fallback
    // element is not starting cold in a hidden tab.
    this.ensureAudio();

    if (resumePromise) {
      await resumePromise;
    }

    if (ctx && !this.decodedBuffer) {
      this.decodedBuffer = await this.decodeChime(ctx).catch(() => null);
    }
  }

  private startSilentBuffer(ctx: AudioContext): void {
    try {
      const buffer = ctx.createBuffer(1, 1, ctx.sampleRate);
      const source = ctx.createBufferSource();
      source.buffer = buffer;
      source.connect(ctx.destination);
      source.start(0);
    } catch {
      // Best effort only; a failure here does not block the rest of arming.
    }
  }

  private async decodeChime(ctx: AudioContext): Promise<AudioBuffer | null> {
    const opus = await this.tryDecode(ctx, OPUS_URL);
    if (opus) return opus;
    return this.tryDecode(ctx, M4A_URL);
  }

  private async tryDecode(ctx: AudioContext, url: string): Promise<AudioBuffer | null> {
    try {
      const response = await fetch(url);
      if (!response.ok) return null;
      const arrayBuffer = await response.arrayBuffer();
      return await ctx.decodeAudioData(arrayBuffer);
    } catch {
      return null;
    }
  }

  private delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }

  /**
   * Races `ctx.resume()` against {@link RESUME_TIMEOUT_MS}. Returns `false` on timeout, so the
   * caller can fall through to the other path rather than hang; the abandoned `resume()` keeps a
   * no-op `catch` so a late rejection is never unhandled.
   */
  private async resumeWithTimeout(ctx: AudioContext): Promise<boolean> {
    const resumePromise = ctx.resume();
    resumePromise.catch(() => undefined);
    let timer: ReturnType<typeof setTimeout> | undefined;
    const timedOut = await Promise.race([
      resumePromise.then(() => false, () => false),
      new Promise<boolean>(resolve => { timer = setTimeout(() => resolve(true), RESUME_TIMEOUT_MS); })
    ]);
    clearTimeout(timer);
    return !timedOut;
  }

  /**
   * Starts `buffer` on `ctx` and waits {@link CLOCK_CHECK_MS} to confirm `ctx.currentTime` has
   * actually moved. A source whose clock never advances is stopped here rather than left running,
   * so a caller that falls back to the other path or rebuilds the context never leaves two sources
   * playing at once.
   */
  private async startBufferAndCheckClock(ctx: AudioContext, buffer: AudioBuffer): Promise<boolean> {
    try {
      const source = ctx.createBufferSource();
      source.buffer = buffer;
      const gain = ctx.createGain();
      gain.gain.value = GAIN;
      source.connect(gain);
      gain.connect(ctx.destination);

      const beforeTime = ctx.currentTime;
      source.start(0);
      await this.delay(CLOCK_CHECK_MS);
      const advanced = ctx.currentTime > beforeTime;
      if (!advanced) {
        try { source.stop(); } catch { /* already stopped, or never really started */ }
      }
      return advanced;
    } catch {
      return false;
    }
  }

  /** Closes `oldCtx` and replaces it with a freshly constructed context, or `null` if that fails too. */
  private rebuildAudioContext(oldCtx: AudioContext): AudioContext | null {
    try { oldCtx.close(); } catch { /* already closed, or unsupported */ }
    this.audioContext = null;

    const AudioContextCtor = this.getAudioContextCtor();
    if (!AudioContextCtor) return null;
    try {
      const ctx = new AudioContextCtor();
      this.attachStateChangeLogging(ctx);
      this.audioContext = ctx;
      return ctx;
    } catch {
      return null;
    }
  }

  /**
   * The buffer path in full: resume-with-timeout, start-and-check-clock, and on a stalled clock a
   * single rebuild-and-retry before giving up. Reports enough of what happened for the caller to
   * fill in an {@link BenchmarkCompletionSoundAttempt}.
   */
  private async playViaBufferChecked(): Promise<{
    outcome: 'played' | 'unsupported';
    contextStateAfter: AudioContextState | null;
    clockAdvanced: boolean | null;
    rebuilt: boolean;
  }> {
    let ctx = this.audioContext;
    const buffer = this.decodedBuffer;
    if (!ctx || !buffer) {
      return { outcome: 'unsupported', contextStateAfter: ctx?.state ?? null, clockAdvanced: null, rebuilt: false };
    }

    if (ctx.state === 'suspended' && !(await this.resumeWithTimeout(ctx))) {
      return { outcome: 'unsupported', contextStateAfter: ctx.state, clockAdvanced: null, rebuilt: false };
    }

    const firstAdvanced = await this.startBufferAndCheckClock(ctx, buffer);
    if (firstAdvanced) {
      return { outcome: 'played', contextStateAfter: ctx.state, clockAdvanced: true, rebuilt: false };
    }

    const rebuilt = this.rebuildAudioContext(ctx);
    if (!rebuilt) {
      return { outcome: 'unsupported', contextStateAfter: null, clockAdvanced: false, rebuilt: true };
    }
    ctx = rebuilt;

    if (ctx.state === 'suspended' && !(await this.resumeWithTimeout(ctx))) {
      return { outcome: 'unsupported', contextStateAfter: ctx.state, clockAdvanced: false, rebuilt: true };
    }
    const secondAdvanced = await this.startBufferAndCheckClock(ctx, buffer);
    return {
      outcome: secondAdvanced ? 'played' : 'unsupported',
      contextStateAfter: ctx.state,
      clockAdvanced: secondAdvanced,
      rebuilt: true
    };
  }

  /**
   * The element path. On a hidden tab, a `play()` that has not settled after `DEFERRED_MS`
   * resolves `'deferred'` instead of waiting further; handlers stay attached to the *original*
   * promise so a late fulfilment or rejection is never unhandled and, on a late fulfilment, the
   * key is marked played so a retry cannot double-chime.
   */
  private async attemptPlayElement(key: string): Promise<'played' | 'blocked' | 'unsupported' | 'deferred'> {
    const audio = this.ensureAudio();
    if (!audio) return 'unsupported';

    audio.currentTime = 0;
    const startedAt = Date.now();
    const playPromise = audio.play();

    if (!document.hidden) {
      return this.awaitPlay(playPromise);
    }

    let timer: ReturnType<typeof setTimeout> | undefined;
    const outcome = await Promise.race([
      playPromise.then(() => 'settled' as const, () => 'settled' as const),
      new Promise<'timeout'>(resolve => {
        timer = setTimeout(() => resolve('timeout'), DEFERRED_MS);
      })
    ]);
    clearTimeout(timer);

    if (outcome !== 'timeout') {
      return this.awaitPlay(playPromise);
    }

    playPromise.then(
      () => {
        this.lastDeferredSettleMs = Date.now() - startedAt;
        this.playedKeys.add(key);
      },
      () => { /* A late rejection needs no further handling; the key stays unplayed and retryable. */ }
    );
    return 'deferred';
  }

  private async awaitPlay(playPromise: Promise<void>): Promise<'played' | 'blocked' | 'unsupported'> {
    try {
      await playPromise;
      return 'played';
    } catch (err) {
      return (err as DOMException)?.name === 'NotAllowedError' ? 'blocked' : 'unsupported';
    }
  }

  private recordAttempt(attempt: BenchmarkCompletionSoundAttempt): void {
    this.attempts.push(attempt);
    if (this.attempts.length > MAX_ATTEMPTS) {
      this.attempts.shift();
    }
  }

  /**
   * Runs one play attempt for `key`, in the path order the tab's own visibility dictates, and
   * records it. Shared by `play()` and `prime()`; neither the per-key deduplication nor the
   * `playedKeys` bookkeeping happens here — that stays with each caller.
   */
  private async runAttempt(key: string): Promise<BenchmarkCompletionSoundOutcome> {
    const hidden = typeof document !== 'undefined' ? document.hidden : false;
    const focused = typeof document !== 'undefined' ? document.hasFocus() : true;
    const attempt: BenchmarkCompletionSoundAttempt = {
      atUtc: new Date().toISOString(),
      key,
      hidden,
      focused,
      path: hidden ? 'buffer' : 'element',
      contextStateBefore: this.audioContext?.state ?? null,
      contextStateAfter: null,
      clockAdvanced: null,
      rebuilt: false,
      outcome: 'unsupported'
    };

    let outcome: BenchmarkCompletionSoundOutcome;
    if (hidden) {
      outcome = await this.tryBufferThenElement(key, attempt);
    } else {
      outcome = await this.tryElementThenBuffer(key, attempt);
    }

    attempt.outcome = outcome;
    this.recordAttempt(attempt);
    return outcome;
  }

  /** Visible-tab order: the element first, the buffer only on `'blocked'` or `'unsupported'`. */
  private async tryElementThenBuffer(
    key: string,
    attempt: BenchmarkCompletionSoundAttempt
  ): Promise<BenchmarkCompletionSoundOutcome> {
    this.lastPlayPath = 'element';
    const elementOutcome = await this.attemptPlayElement(key);
    if (elementOutcome === 'played') return 'played';

    if ((elementOutcome === 'blocked' || elementOutcome === 'unsupported')
      && this.decodedBuffer && this.audioContext) {
      attempt.path = 'buffer';
      this.lastPlayPath = 'buffer';
      const result = await this.playViaBufferChecked();
      attempt.contextStateAfter = result.contextStateAfter;
      attempt.clockAdvanced = result.clockAdvanced;
      attempt.rebuilt = result.rebuilt;
      if (result.outcome === 'played') return 'played';
    }
    return elementOutcome;
  }

  /** Hidden-tab order: the armed buffer first, the element only when the buffer fails. */
  private async tryBufferThenElement(
    key: string,
    attempt: BenchmarkCompletionSoundAttempt
  ): Promise<BenchmarkCompletionSoundOutcome> {
    if (this.decodedBuffer && this.audioContext) {
      this.lastPlayPath = 'buffer';
      const result = await this.playViaBufferChecked();
      attempt.contextStateAfter = result.contextStateAfter;
      attempt.clockAdvanced = result.clockAdvanced;
      attempt.rebuilt = result.rebuilt;
      if (result.outcome === 'played') return 'played';
    }

    attempt.path = 'element';
    this.lastPlayPath = 'element';
    return this.attemptPlayElement(key);
  }

  /**
   * Plays the chime for `key` (`run:<id>` or `series:<id>`), unless that key already played
   * successfully once this session. A rejection is never thrown into the caller: every outcome,
   * including a deferred or blocked attempt, resolves rather than rejecting, so a poller can
   * await this without a try/catch. `'played'` means the browser accepted the playback, not that
   * anyone heard it.
   */
  async play(key: string): Promise<BenchmarkCompletionSoundOutcome> {
    if (this.playedKeys.has(key)) return 'duplicate';

    const outcome = await this.runAttempt(key);
    if (outcome === 'played') {
      this.playedKeys.add(key);
    }
    return outcome;
  }

  /**
   * Plays under the user gesture that invoked it (the *Test sound* button), which also
   * unlocks later, gesture-less `play()` calls on browsers that require one interaction
   * before audio is allowed. Bypasses the per-key deduplication `play()` applies, since the
   * operator may press the button more than once.
   */
  async prime(): Promise<'played' | 'blocked' | 'unsupported'> {
    const outcome = await this.runAttempt('test');
    return outcome === 'played' || outcome === 'blocked' ? outcome : 'unsupported';
  }
}
