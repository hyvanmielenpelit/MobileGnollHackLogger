import type { Mock } from "vitest";
import { TestBed, fakeAsync, flush, tick } from '@angular/core/testing';

import { BenchmarkCompletionSoundService } from './benchmark-completion-sound.service';

/**
 * Stands in for the one `HTMLAudioElement` the service creates lazily. `appendChild` and `load`
 * are no-ops — the service calls them on construction, but nothing here needs a real media
 * pipeline — and `play` resolves or rejects however each spec configures it.
 */
class FakeAudioElement {
  preload = '';
  volume = 1;
  currentTime = 0;
  playCallCount = 0;
  pauseCallCount = 0;
  readonly appendedSources: { src: string; type: string }[] = [];
  playResult: 'resolve' | DOMException = 'resolve';

  appendChild(node: any): any {
    this.appendedSources.push({ src: node.src, type: node.type });
    return node;
  }

  load(): void { }

  pause(): void {
    this.pauseCallCount++;
  }

  play(): Promise<void> {
    this.playCallCount++;
    return this.playResult === 'resolve' ? Promise.resolve() : Promise.reject(this.playResult);
  }
}

describe('BenchmarkCompletionSoundService', () => {
  let service: BenchmarkCompletionSoundService;
  let fakeAudio: FakeAudioElement;
  let audioSpy: Mock;

  beforeEach(() => {
    fakeAudio = new FakeAudioElement();
    audioSpy = vi.spyOn(window as any, 'Audio').mockImplementation(function () { return fakeAudio; } as any) as unknown as Mock;
    TestBed.configureTestingModule({});
    service = TestBed.inject(BenchmarkCompletionSoundService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should create the audio element lazily, with the Opus source ahead of the AAC fallback', async () => {
    expect(audioSpy).not.toHaveBeenCalled();

    await service.play('run:1');

    expect(audioSpy).toHaveBeenCalledTimes(1);
    expect(fakeAudio.preload).toBe('auto');
    expect(fakeAudio.volume).toBe(0.6);
    expect(fakeAudio.appendedSources).toEqual([
      { src: expect.stringMatching(/AIBenchmarkingComplete\.opus$/), type: 'audio/ogg; codecs=opus' } as any,
      { src: expect.stringMatching(/AIBenchmarkingComplete\.m4a$/), type: 'audio/mp4; codecs="mp4a.40.2"' } as any
    ]);
  });

  it('should reuse the same audio element across calls', async () => {
    await service.play('run:1');
    await service.play('run:2');

    expect(audioSpy).toHaveBeenCalledTimes(1);
  });

  it('should reset currentTime before each play', async () => {
    fakeAudio.currentTime = 42;
    await service.play('run:1');

    expect(fakeAudio.currentTime).toBe(0);
  });

  it('should resolve "played" on a successful play, via the element on a visible tab', async () => {
    const outcome = await service.play('run:1');
    expect(outcome).toBe('played');
    expect(fakeAudio.playCallCount).toBe(1);
  });

  it('should resolve "blocked" on a NotAllowedError, and never reject', async () => {
    fakeAudio.playResult = new DOMException('autoplay refused', 'NotAllowedError');
    const outcome = await service.play('run:1');
    expect(outcome).toBe('blocked');
  });

  it('should resolve "unsupported" on any other playback error', async () => {
    fakeAudio.playResult = new DOMException('no decoder', 'NotSupportedError');
    const outcome = await service.play('run:1');
    expect(outcome).toBe('unsupported');
  });

  it('should resolve "unsupported" without touching the element when Audio does not exist', async () => {
    (window as any).Audio = undefined;
    const outcome = await service.play('run:1');
    expect(outcome).toBe('unsupported');
  });

  it('should resolve "duplicate" for a key already played this session, without a second play() call', async () => {
    await service.play('run:1');
    const outcome = await service.play('run:1');

    expect(outcome).toBe('duplicate');
    expect(fakeAudio.playCallCount).toBe(1);
  });

  it('should not deduplicate a key whose first attempt was blocked', async () => {
    fakeAudio.playResult = new DOMException('autoplay refused', 'NotAllowedError');
    const first = await service.play('run:1');
    expect(first).toBe('blocked');

    fakeAudio.playResult = 'resolve';
    const second = await service.play('run:1');
    expect(second).toBe('played');
    expect(fakeAudio.playCallCount).toBe(2);
  });

  it('should track duplicates per key, not globally', async () => {
    await service.play('run:1');
    const outcome = await service.play('run:2');

    expect(outcome).toBe('played');
  });

  describe('prime', () => {
    it('should play without going through the play() de-duplication', async () => {
      const first = await service.prime();
      const second = await service.prime();

      expect(first).toBe('played');
      expect(second).toBe('played');
      expect(fakeAudio.playCallCount).toBe(2);
    });

    it('should resolve "blocked" the same way play() does', async () => {
      fakeAudio.playResult = new DOMException('autoplay refused', 'NotAllowedError');
      const outcome = await service.prime();
      expect(outcome).toBe('blocked');
    });
  });

  describe('arm', () => {
    let fakeCtx: FakeAudioContext;
    let ctorSpy: Mock;
    let fetchSpy: Mock;

    beforeEach(() => {
      fakeCtx = new FakeAudioContext();
      ctorSpy = vi.spyOn(window as any, 'AudioContext').mockImplementation(function () { return fakeCtx; } as any) as unknown as Mock;
      fetchSpy = vi.spyOn(window, 'fetch').mockReturnValue(undefined as any);
    });

    function okResponse(): Response {
      return new Response(new ArrayBuffer(4), { status: 200 });
    }

    it('starts the silent buffer synchronously, before a delayed fetch resolves', async () => {
      let resolveFetch!: (value: Response) => void;
      fetchSpy.mockReturnValue(new Promise<Response>(resolve => { resolveFetch = resolve; }));

      const armPromise = service.arm();

      expect(fakeCtx.createdBufferSources.length).toBe(1);
      expect(fakeCtx.createdBufferSources[0].started).toBe(true);
      expect(fakeCtx.resumeCalls).toBe(1);

      resolveFetch(okResponse());
      await armPromise;
    });

    /** The fetched URLs, in order. */
    const fetchedUrls = (): string[] => vi.mocked(fetchSpy).mock.calls.map(call => String(call[0]));

    it('decodes the Opus source of each chime when its fetch succeeds', async () => {
      fetchSpy.mockImplementation(() => Promise.resolve(okResponse()));

      await service.arm();

      expect(fetchedUrls()).toEqual([
        expect.stringMatching(/AIBenchmarkingComplete\.opus$/),
        expect.stringMatching(/AIBenchmarkingFailed\.opus$/)
      ]);
      expect(fakeCtx.decodeAudioDataCalls.length).toBe(2);
      expect(service.diagnostics.armed).toBe(true);
      expect(service.diagnostics.armedKinds).toEqual(['complete', 'failed']);
    });

    it('falls back to the AAC source when the Opus fetch fails', async () => {
      fetchSpy.mockImplementation((url: string) => String(url).includes('.opus') ? Promise.reject(new Error('network down')) : Promise.resolve(okResponse()));

      await service.arm();

      expect(fetchedUrls()).toEqual([
        expect.stringMatching(/AIBenchmarkingComplete\.opus$/),
        expect.stringMatching(/AIBenchmarkingComplete\.m4a$/),
        expect.stringMatching(/AIBenchmarkingFailed\.opus$/),
        expect.stringMatching(/AIBenchmarkingFailed\.m4a$/)
      ]);
      expect(fakeCtx.decodeAudioDataCalls.length).toBe(2);
    });

    it('falls back to the AAC source when the Opus decode fails', async () => {
      fetchSpy.mockImplementation(() => Promise.resolve(okResponse()));
      let decodeCalls = 0;
      fakeCtx.decodeAudioData = (buf: ArrayBuffer) => {
        decodeCalls++;
        fakeCtx.decodeAudioDataCalls.push(buf);
        return decodeCalls === 1 ? Promise.reject(new Error('bad codec')) : Promise.resolve({} as AudioBuffer);
      };

      await service.arm();

      // The completion chime's Opus decode fails and its AAC is tried; the failure chime's Opus decodes.
      expect(fetchedUrls()).toEqual([
        expect.stringMatching(/AIBenchmarkingComplete\.opus$/),
        expect.stringMatching(/AIBenchmarkingComplete\.m4a$/),
        expect.stringMatching(/AIBenchmarkingFailed\.opus$/)
      ]);
      expect(decodeCalls).toBe(3);
    });

    it('shares one in-flight arm() across concurrent callers', async () => {
      fetchSpy.mockImplementation(() => Promise.resolve(okResponse()));

      await Promise.all([service.arm(), service.arm()]);

      expect(ctorSpy).toHaveBeenCalledTimes(1);
      expect(fetchSpy).toHaveBeenCalledTimes(2);
    });

    it('is a no-op on a second, later arm() once armed', async () => {
      fetchSpy.mockImplementation(() => Promise.resolve(okResponse()));

      await service.arm();
      await service.arm();

      expect(ctorSpy).toHaveBeenCalledTimes(1);
      expect(fetchSpy).toHaveBeenCalledTimes(2);
    });

    it('retries a fully failed arm on the next call', async () => {
      fetchSpy.mockRejectedValue(new Error('network down'));

      await service.arm();
      expect(fetchSpy).toHaveBeenCalledTimes(4);

      fetchSpy.mockClear();
      fetchSpy.mockImplementation(() => Promise.resolve(okResponse()));
      await service.arm();

      expect(fetchSpy).toHaveBeenCalledTimes(2);
    });

    it('stays armed with the completion chime alone when the failure chime is missing', async () => {
      fetchSpy.mockImplementation((url: string) => String(url).includes('Failed')
        ? Promise.resolve(new Response(null, { status: 404 }))
        : Promise.resolve(okResponse()));

      await service.arm();
      fetchSpy.mockClear();
      await service.arm();

      expect(service.diagnostics.armedKinds).toEqual(['complete']);
      expect(fetchSpy).not.toHaveBeenCalled();
    });

    it('never rejects, even when AudioContext throws on construction', async () => {
      ctorSpy.mockImplementation(function () {
        throw new Error('no audio hardware');
      });
      fetchSpy.mockImplementation(() => Promise.resolve(okResponse()));

      await expect(service.arm()).resolves.not.toThrow();
    });

    it('on a visible tab, tries the element first and never touches the armed buffer when the element succeeds', async () => {
      fetchSpy.mockImplementation(() => Promise.resolve(okResponse()));
      await service.arm();
      const buffersBeforePlay = fakeCtx.createdBufferSources.length;
      const resumesBeforePlay = fakeCtx.resumeCalls;

      const outcome = await service.play('run:1');

      expect(outcome).toBe('played');
      expect(fakeAudio.playCallCount).toBe(1);
      // Only the silent buffer arm() itself started; play() never created another.
      expect(fakeCtx.createdBufferSources.length).toBe(buffersBeforePlay);
      expect(fakeCtx.resumeCalls).toBe(resumesBeforePlay);
    });

    it('falls back to the armed buffer when the element is blocked on a visible tab', async () => {
      fetchSpy.mockImplementation(() => Promise.resolve(okResponse()));
      await service.arm();
      fakeAudio.playResult = new DOMException('autoplay refused', 'NotAllowedError');

      const outcome = await service.play('run:1');

      expect(outcome).toBe('played');
      expect(fakeCtx.createdBufferSources.some(s => s.started)).toBe(true);
    });

    it('plays through the decoded buffer first on a hidden tab, never touching the fallback element', async () => {
      vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);
      fetchSpy.mockImplementation(() => Promise.resolve(okResponse()));

      await service.arm();
      const outcome = await service.play('run:1');

      expect(outcome).toBe('played');
      expect(fakeCtx.createdBufferSources.some(s => s.started)).toBe(true);
      expect(fakeAudio.playCallCount).toBe(0);
    });

    it('resumes a suspended context before playing the buffer on a hidden tab', async () => {
      vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);
      fetchSpy.mockImplementation(() => Promise.resolve(okResponse()));
      await service.arm();
      fakeCtx.state = 'suspended';
      fakeCtx.resumeCalls = 0;

      await service.play('run:1');

      expect(fakeCtx.resumeCalls).toBe(1);
    });
  });

  describe('the resume timeout', () => {
    let fakeCtx: FakeAudioContext;

    beforeEach(() => {
      fakeCtx = new FakeAudioContext();
      fakeCtx.state = 'suspended';
      vi.spyOn(window as any, 'AudioContext').mockImplementation(function () { return fakeCtx; } as any);
    });

    it('falls through to the element within 1000 ms when ctx.resume() never settles', fakeAsync(() => {
      // A visible tab tries the element first; it is blocked here so the buffer path — the one
      // under test — actually runs. The completion chime's buffer is set directly, bypassing
      // arm()'s fetch/decode, since only the resume race matters to this spec.
      fakeAudio.playResult = new DOMException('autoplay refused', 'NotAllowedError');
      (service as any).audioContext = fakeCtx;
      (service as any).decodedBuffers.complete = {} as AudioBuffer;
      fakeCtx.resume = () => new Promise<void>(() => { });

      let outcome: string | undefined;
      service.play('run:1').then(o => { outcome = o; });
      tick(999);
      expect(outcome).toBeUndefined();
      tick(1);
      flush();

      // The buffer never got past the stuck resume(), so play() resolves with the element's own
      // (blocked) outcome rather than hanging.
      expect(outcome).toBe('blocked');
      expect(fakeCtx.createdBufferSources.length).toBe(0);
    }));
  });

  describe('the clock-liveness check', () => {
    let fakeCtx1: FakeAudioContext;
    let fakeCtx2: FakeAudioContext;
    let ctorSpy: Mock;

    beforeEach(() => {
      vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);
      fakeCtx1 = new FakeAudioContext();
      fakeCtx2 = new FakeAudioContext();
      // fakeCtx1 is wired in directly, bypassing construction, so the constructor spy is only
      // ever consulted for the rebuild — the single call every test here expects.
      ctorSpy = vi.spyOn(window as any, 'AudioContext').mockImplementation(function () { return fakeCtx2; } as any) as unknown as Mock;
      (service as any).audioContext = fakeCtx1;
      (service as any).decodedBuffers.complete = {} as AudioBuffer;
    });

    it('rebuilds the context once and plays on the rebuilt context when the first clock never advances', async () => {
      fakeCtx1.stallClock = true;

      const outcome = await service.play('run:1');

      expect(outcome).toBe('played');
      expect(fakeCtx1.closeCalls).toBe(1);
      expect(fakeCtx1.createdBufferSources[0].stopped).toBe(true);
      expect(ctorSpy).toHaveBeenCalledTimes(1);
      expect(fakeCtx2.createdBufferSources.some(s => s.started)).toBe(true);
      expect(fakeAudio.playCallCount).toBe(0);
    });

    it('stops the stalled source and falls back to the element when the rebuilt context also stalls', async () => {
      fakeCtx1.stallClock = true;
      fakeCtx2.stallClock = true;

      const outcome = await service.play('run:1');

      expect(outcome).toBe('played');
      expect(fakeCtx1.createdBufferSources[0].stopped).toBe(true);
      expect(fakeCtx2.createdBufferSources[0].stopped).toBe(true);
      expect(fakeAudio.playCallCount).toBe(1);
    });
  });

  describe('the hidden-tab deferred path', () => {
    it('resolves "deferred" after the timeout in a hidden tab, with no unhandled rejection, and marks the key played on a late fulfilment', async () => {
      vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);
      let rejectPlay!: (err: unknown) => void;
      const pending = new Promise<void>((_resolve, reject) => { rejectPlay = reject; });
      fakeAudio.play = () => { fakeAudio.playCallCount++; return pending; };

      vi.useFakeTimers();
      try {
        const outcomePromise = service.play('run:1');
        vi.advanceTimersByTime(2001);
        const outcome = await outcomePromise;
        expect(outcome).toBe('deferred');
      }
      finally {
        vi.useRealTimers();
      }

      // The original promise settling late must not throw an unhandled rejection, and a
      // rejection leaves the key retryable rather than marked played.
      rejectPlay(new DOMException('late refusal', 'NotAllowedError'));
      await Promise.resolve();
      await Promise.resolve();
      const retry = await service.play('run:1');
      expect(retry).not.toBe('duplicate');
    });

    it('marks the key played once a deferred play() later fulfils, so a retry cannot double-chime', async () => {
      vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);
      let resolvePlay!: () => void;
      const pending = new Promise<void>(resolve => { resolvePlay = resolve; });
      fakeAudio.play = () => { fakeAudio.playCallCount++; return pending; };

      vi.useFakeTimers();
      let outcome: string;
      try {
        const outcomePromise = service.play('run:1');
        vi.advanceTimersByTime(2001);
        outcome = await outcomePromise;
      }
      finally {
        vi.useRealTimers();
      }
      expect(outcome).toBe('deferred');

      resolvePlay!();
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();

      const retry = await service.play('run:1');
      expect(retry).toBe('duplicate');
      expect(fakeAudio.playCallCount).toBe(1);
    });

    it('does not defer on a visible tab: it awaits play() to its real outcome', async () => {
      const outcome = await service.play('run:1');
      expect(outcome).toBe('played');
    });
  });

  describe('the visible-tab play timeout', () => {
    let resolvePlay!: () => void;

    beforeEach(() => {
      const pending = new Promise<void>(resolve => { resolvePlay = resolve; });
      fakeAudio.play = () => { fakeAudio.playCallCount++; return pending; };
    });

    it('times out a play() that never settles, pauses the element and plays the armed buffer instead', async () => {
      const fakeCtx = new FakeAudioContext();
      (service as any).audioContext = fakeCtx;
      (service as any).decodedBuffers.complete = {} as AudioBuffer;

      vi.useFakeTimers();
      let outcome: string | undefined;
      try {
        const outcomePromise = service.play('run:1');
        await vi.advanceTimersByTimeAsync(2999);
        expect(fakeAudio.pauseCallCount).toBe(0);
        await vi.advanceTimersByTimeAsync(1001);
        outcome = await outcomePromise;
      }
      finally {
        vi.useRealTimers();
      }

      expect(outcome).toBe('played');
      expect(fakeAudio.pauseCallCount).toBe(1);
      expect(fakeCtx.createdBufferSources.some(s => s.started)).toBe(true);
      const attempts = service.diagnostics.attempts;
      expect(attempts.length).toBe(1);
      expect(attempts[0].path).toBe('buffer');
      expect(attempts[0].outcome).toBe('played');
    });

    it('records the attempt as "timeout" when no buffer is armed', async () => {
      vi.useFakeTimers();
      let outcome: string | undefined;
      try {
        const outcomePromise = service.play('run:1');
        await vi.advanceTimersByTimeAsync(3001);
        outcome = await outcomePromise;
      }
      finally {
        vi.useRealTimers();
      }

      expect(outcome).toBe('timeout');
      expect(fakeAudio.pauseCallCount).toBe(1);
      const attempts = service.diagnostics.attempts;
      expect(attempts.length).toBe(1);
      expect(attempts[0].path).toBe('element');
      expect(attempts[0].outcome).toBe('timeout');
    });

    it('marks no play and records no attempt when the timed-out play() fulfils late', async () => {
      vi.useFakeTimers();
      let outcome: string | undefined;
      try {
        const outcomePromise = service.play('run:1');
        await vi.advanceTimersByTimeAsync(3001);
        outcome = await outcomePromise;
      }
      finally {
        vi.useRealTimers();
      }
      expect(outcome).toBe('timeout');

      resolvePlay();
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();

      expect(service.diagnostics.attempts.length).toBe(1);
      const retry = await service.play('run:1');
      expect(retry).not.toBe('duplicate');
      expect(fakeAudio.playCallCount).toBe(2);
    });

    it('maps a timeout to "unsupported" for prime()', async () => {
      vi.useFakeTimers();
      let outcome: string | undefined;
      try {
        const outcomePromise = service.prime();
        await vi.advanceTimersByTimeAsync(3001);
        outcome = await outcomePromise;
      }
      finally {
        vi.useRealTimers();
      }

      expect(outcome).toBe('unsupported');
    });
  });

  describe('the failure chime', () => {
    /** One fake element per kind, in creation order. */
    let created: FakeAudioElement[];

    beforeEach(() => {
      created = [];
      audioSpy.mockImplementation(function () {
        const element = new FakeAudioElement();
        created.push(element);
        return element;
      } as any);
    });

    const elementOf = (pattern: RegExp): FakeAudioElement | undefined =>
      created.find(e => e.appendedSources.some(s => pattern.test(s.src)));

    it('plays the failure chime through its own element, with its Opus source ahead of the AAC fallback', async () => {
      const outcome = await service.play('battery:9', 'failed');

      expect(outcome).toBe('played');
      const failed = elementOf(/AIBenchmarkingFailed\.opus$/)!;
      expect(failed.appendedSources).toEqual([
        { src: expect.stringMatching(/AIBenchmarkingFailed\.opus$/), type: 'audio/ogg; codecs=opus' } as any,
        { src: expect.stringMatching(/AIBenchmarkingFailed\.m4a$/), type: 'audio/mp4; codecs="mp4a.40.2"' } as any
      ]);
      expect(failed.playCallCount).toBe(1);
      expect(elementOf(/AIBenchmarkingComplete\.opus$/)).toBeUndefined();
    });

    it('defaults to the completion chime', async () => {
      await service.play('run:1');
      await service.prime();

      const complete = elementOf(/AIBenchmarkingComplete\.opus$/)!;
      expect(complete.playCallCount).toBe(2);
      expect(elementOf(/AIBenchmarkingFailed\.opus$/)).toBeUndefined();
    });

    it('plays the same key once per kind: the two chimes deduplicate separately', async () => {
      expect(await service.play('run:1', 'complete')).toBe('played');
      expect(await service.play('run:1', 'failed')).toBe('played');
      expect(await service.play('run:1', 'failed')).toBe('duplicate');
      expect(await service.play('run:1')).toBe('duplicate');
    });

    it('primes the failure chime without deduplication', async () => {
      expect(await service.prime('failed')).toBe('played');
      expect(await service.prime('failed')).toBe('played');

      expect(elementOf(/AIBenchmarkingFailed\.opus$/)!.playCallCount).toBe(2);
    });

    it('records the kind of every attempt', async () => {
      await service.play('run:1', 'failed');
      await service.prime();

      expect(service.diagnostics.attempts.map(a => [a.key, a.kind])).toEqual([['run:1', 'failed'], ['test', 'complete']]);
    });

    it('plays the failure chime\'s own decoded buffer on a hidden tab', async () => {
      vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);
      const fakeCtx = new FakeAudioContext();
      const completeBuffer = { name: 'complete' } as unknown as AudioBuffer;
      const failedBuffer = { name: 'failed' } as unknown as AudioBuffer;
      (service as any).audioContext = fakeCtx;
      (service as any).decodedBuffers.complete = completeBuffer;
      (service as any).decodedBuffers.failed = failedBuffer;

      const outcome = await service.play('series:4', 'failed');

      expect(outcome).toBe('played');
      expect(fakeCtx.createdBufferSources.map(s => s.buffer)).toEqual([failedBuffer]);
      expect(created.length).toBe(0);
    });

    it('falls back to the failure chime\'s element when only the completion chime is decoded', async () => {
      vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);
      const fakeCtx = new FakeAudioContext();
      (service as any).audioContext = fakeCtx;
      (service as any).decodedBuffers.complete = {} as AudioBuffer;

      const outcome = await service.play('series:4', 'failed');

      expect(outcome).toBe('played');
      expect(fakeCtx.createdBufferSources.length).toBe(0);
      expect(elementOf(/AIBenchmarkingFailed\.opus$/)!.playCallCount).toBe(1);
    });
  });

  describe('diagnostics attempts', () => {
    it('records an attempt for play(), visible on diagnostics.attempts', async () => {
      await service.play('run:1');

      const attempts = service.diagnostics.attempts;
      expect(attempts.length).toBe(1);
      expect(attempts[0].key).toBe('run:1');
      expect(attempts[0].path).toBe('element');
      expect(attempts[0].outcome).toBe('played');
      expect(attempts[0].hidden).toBe(false);
    });

    it('records an attempt for prime(), keyed "test"', async () => {
      await service.prime();

      const attempts = service.diagnostics.attempts;
      expect(attempts.length).toBe(1);
      expect(attempts[0].key).toBe('test');
    });

    it('keeps only the last 10 attempts', async () => {
      for (let i = 0; i < 12; i++) {
        await service.play(`run:${i}`);
      }

      const attempts = service.diagnostics.attempts;
      expect(attempts.length).toBe(10);
      expect(attempts[0].key).toBe('run:2');
      expect(attempts[9].key).toBe('run:11');
    });
  });
});

/** Stands in for the Web Audio graph `arm()` and the buffer playback path build. */
class FakeAudioContext {
  state: 'running' | 'suspended' | 'closed' = 'running';
  sampleRate = 44100;
  currentTime = 0;
  resumeCalls = 0;
  closeCalls = 0;
  onstatechange: (() => void) | null = null;
  readonly createdBufferSources: FakeBufferSource[] = [];
  readonly decodeAudioDataCalls: ArrayBuffer[] = [];
  readonly destination = {} as AudioDestinationNode;

  /** When true, a buffer source created here never advances `currentTime` — simulates a stalled clock. */
  stallClock = false;

  resume(): Promise<void> {
    this.resumeCalls++;
    this.state = 'running';
    return Promise.resolve();
  }

  close(): Promise<void> {
    this.closeCalls++;
    this.state = 'closed';
    return Promise.resolve();
  }

  createBuffer(channels: number, length: number, sampleRate: number): AudioBuffer {
    return { numberOfChannels: channels, length, sampleRate } as unknown as AudioBuffer;
  }

  createBufferSource(): FakeBufferSource {
    const source = new FakeBufferSource(this);
    this.createdBufferSources.push(source);
    return source;
  }

  createGain(): FakeGainNode {
    return new FakeGainNode();
  }

  decodeAudioData(buffer: ArrayBuffer): Promise<AudioBuffer> {
    this.decodeAudioDataCalls.push(buffer);
    return Promise.resolve({} as AudioBuffer);
  }
}

class FakeBufferSource {
  buffer: AudioBuffer | null = null;
  started = false;
  stopped = false;

  constructor(private readonly ctx: FakeAudioContext) {}

  connect(node: any): any { return node; }

  start(): void {
    this.started = true;
    if (!this.ctx.stallClock) {
      this.ctx.currentTime += 1;
    }
  }

  stop(): void { this.stopped = true; }
}

class FakeGainNode {
  gain = { value: 1 };
  connect(node: any): any { return node; }
}
