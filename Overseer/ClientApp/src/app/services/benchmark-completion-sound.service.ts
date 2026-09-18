import { Injectable } from '@angular/core';

/** Every outcome `play` can resolve to. `prime` resolves to the first three; neither ever rejects. */
export type BenchmarkCompletionSoundOutcome = 'played' | 'blocked' | 'unsupported' | 'duplicate' | 'deferred';

const OPUS_URL = '/audio/AIBenchmarkingComplete.opus';
const M4A_URL = '/audio/AIBenchmarkingComplete.m4a';
const GAIN = 0.6;
/** How long a hidden tab's element playback may stay pending before it counts as 'deferred'. */
const DEFERRED_MS = 2000;

/**
 * The chime the AI Benchmark run tab plays when a run or series finishes. Two playback paths
 * exist side by side:
 *
 * - A Web Audio `AudioContext` with the chime pre-decoded into an `AudioBuffer`, armed by
 *   {@link arm} from a user gesture. This is the path that actually plays audibly from a
 *   background tab, because the buffer and context were both created while the tab still had
 *   focus.
 * - The original `HTMLAudioElement` with an Opus source ahead of an AAC fallback, used whenever
 *   the buffer path is unavailable or fails. A hidden tab can defer this element's first
 *   `play()` until the tab is shown again, which is what {@link BenchmarkCompletionSoundOutcome}
 *   `'deferred'` reports.
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

  /** Milliseconds from a deferred element play() to its late settle. Diagnostics only. */
  lastDeferredSettleMs: number | null = null;
  /** Which path the most recent play()/prime() actually used. Diagnostics only. */
  lastPlayPath: 'buffer' | 'element' | null = null;

  /** A snapshot for the run diagnostics capture; nothing here drives playback behaviour. */
  get diagnostics(): {
    arming: boolean;
    armed: boolean;
    audioContextState: AudioContextState | null;
    lastPlayPath: 'buffer' | 'element' | null;
    lastDeferredSettleMs: number | null;
  } {
    return {
      arming: this.arming,
      armed: this.decodedBuffer !== null,
      audioContextState: this.audioContext?.state ?? null,
      lastPlayPath: this.lastPlayPath,
      lastDeferredSettleMs: this.lastDeferredSettleMs
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
    const AudioContextCtor: (new () => AudioContext) | undefined =
      (window as unknown as { AudioContext?: new () => AudioContext }).AudioContext
      ?? (window as unknown as { webkitAudioContext?: new () => AudioContext }).webkitAudioContext;

    let ctx = this.audioContext;
    let resumePromise: Promise<void> | null = null;

    if (AudioContextCtor) {
      if (!ctx) {
        try {
          ctx = new AudioContextCtor();
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

  private async playViaBuffer(): Promise<'played' | 'unsupported'> {
    const ctx = this.audioContext;
    const buffer = this.decodedBuffer;
    if (!ctx || !buffer) return 'unsupported';
    try {
      if (ctx.state === 'suspended') {
        await ctx.resume();
      }
      const source = ctx.createBufferSource();
      source.buffer = buffer;
      const gain = ctx.createGain();
      gain.gain.value = GAIN;
      source.connect(gain);
      gain.connect(ctx.destination);
      source.start(0);
      return 'played';
    } catch {
      return 'unsupported';
    }
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

  /**
   * Plays the chime for `key` (`run:<id>` or `series:<id>`), unless that key already played
   * successfully once this session. Prefers the armed Web Audio buffer, which is what actually
   * plays audibly from a background tab; falls back to the `HTMLAudioElement` path when no
   * buffer was armed or the buffer path itself fails. A rejection is never thrown into the
   * caller: every outcome, including a deferred or blocked attempt, resolves rather than
   * rejecting, so a poller can await this without a try/catch. `'played'` means the browser
   * accepted the playback, not that anyone heard it.
   */
  async play(key: string): Promise<BenchmarkCompletionSoundOutcome> {
    if (this.playedKeys.has(key)) return 'duplicate';

    if (this.decodedBuffer && this.audioContext) {
      this.lastPlayPath = 'buffer';
      const bufferOutcome = await this.playViaBuffer();
      if (bufferOutcome === 'played') {
        this.playedKeys.add(key);
        return 'played';
      }
    }

    this.lastPlayPath = 'element';
    const outcome = await this.attemptPlayElement(key);
    if (outcome === 'played') {
      this.playedKeys.add(key);
    }
    return outcome;
  }

  /**
   * Plays under the user gesture that invoked it (the *Test sound* button), which also
   * unlocks later, gesture-less `play()` calls on browsers that require one interaction
   * before audio is allowed. Bypasses the per-key deduplication `play()` applies, since the
   * operator may press the button more than once. Prefers the armed buffer, same as `play()`.
   */
  async prime(): Promise<'played' | 'blocked' | 'unsupported'> {
    if (this.decodedBuffer && this.audioContext) {
      const bufferOutcome = await this.playViaBuffer();
      if (bufferOutcome === 'played') return 'played';
    }

    const audio = this.ensureAudio();
    if (!audio) return 'unsupported';
    audio.currentTime = 0;
    return this.awaitPlay(audio.play());
  }
}
