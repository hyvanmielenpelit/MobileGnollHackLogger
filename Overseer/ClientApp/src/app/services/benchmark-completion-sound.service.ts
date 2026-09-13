import { Injectable } from '@angular/core';

/** Every outcome `play` and `prime` can resolve to. Neither ever rejects. */
export type BenchmarkCompletionSoundOutcome = 'played' | 'blocked' | 'unsupported' | 'duplicate';

/**
 * The chime the AI Benchmark run tab plays when a run or series finishes. One
 * `HTMLAudioElement` is created lazily, on the first `prime()` or `play()`, with an Opus
 * source ahead of an AAC fallback so the browser picks whichever it can decode.
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

  private ensureAudio(): HTMLAudioElement | null {
    if (this.audio) return this.audio;
    if (typeof Audio === 'undefined') return null;

    const audio = new Audio();
    audio.preload = 'auto';
    audio.volume = 0.6;

    const opusSource = document.createElement('source');
    opusSource.src = '/audio/AIBenchmarkingComplete.opus';
    opusSource.type = 'audio/ogg; codecs=opus';
    audio.appendChild(opusSource);

    const aacSource = document.createElement('source');
    aacSource.src = '/audio/AIBenchmarkingComplete.m4a';
    aacSource.type = 'audio/mp4; codecs="mp4a.40.2"';
    audio.appendChild(aacSource);

    audio.load();
    this.audio = audio;
    return audio;
  }

  private async attemptPlay(): Promise<'played' | 'blocked' | 'unsupported'> {
    const audio = this.ensureAudio();
    if (!audio) return 'unsupported';

    try {
      audio.currentTime = 0;
      await audio.play();
      return 'played';
    } catch (err) {
      return (err as DOMException)?.name === 'NotAllowedError' ? 'blocked' : 'unsupported';
    }
  }

  /**
   * Plays the chime for `key` (`run:<id>` or `series:<id>`), unless that key already played
   * successfully once this session. A rejection is never thrown into the caller: a blocked or
   * unsupported attempt resolves rather than rejecting, so a poller can await this without a
   * try/catch.
   */
  async play(key: string): Promise<BenchmarkCompletionSoundOutcome> {
    if (this.playedKeys.has(key)) return 'duplicate';
    const outcome = await this.attemptPlay();
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
    return this.attemptPlay();
  }
}
