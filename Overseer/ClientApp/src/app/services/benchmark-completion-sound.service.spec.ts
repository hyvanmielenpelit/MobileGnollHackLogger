import { TestBed } from '@angular/core/testing';

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
  readonly appendedSources: { src: string; type: string }[] = [];
  playResult: 'resolve' | DOMException = 'resolve';

  appendChild(node: any): any {
    this.appendedSources.push({ src: node.src, type: node.type });
    return node;
  }

  load(): void { /* no-op: nothing here reads network state */ }

  play(): Promise<void> {
    this.playCallCount++;
    return this.playResult === 'resolve' ? Promise.resolve() : Promise.reject(this.playResult);
  }
}

describe('BenchmarkCompletionSoundService', () => {
  let service: BenchmarkCompletionSoundService;
  let fakeAudio: FakeAudioElement;
  let audioSpy: jasmine.Spy;

  beforeEach(() => {
    fakeAudio = new FakeAudioElement();
    audioSpy = spyOn(window as any, 'Audio').and.returnValue(fakeAudio);
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
      { src: jasmine.stringMatching(/AIBenchmarkingComplete\.opus$/), type: 'audio/ogg; codecs=opus' } as any,
      { src: jasmine.stringMatching(/AIBenchmarkingComplete\.m4a$/), type: 'audio/mp4; codecs="mp4a.40.2"' } as any
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

  it('should resolve "played" on a successful play', async () => {
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
    let ctorSpy: jasmine.Spy;
    let fetchSpy: jasmine.Spy;

    beforeEach(() => {
      fakeCtx = new FakeAudioContext();
      ctorSpy = spyOn(window as any, 'AudioContext').and.returnValue(fakeCtx);
      fetchSpy = spyOn(window, 'fetch');
    });

    function okResponse(): Response {
      return new Response(new ArrayBuffer(4), { status: 200 });
    }

    it('starts the silent buffer synchronously, before a delayed fetch resolves', async () => {
      let resolveFetch!: (value: Response) => void;
      fetchSpy.and.returnValue(new Promise<Response>(resolve => { resolveFetch = resolve; }));

      const armPromise = service.arm();

      expect(fakeCtx.createdBufferSources.length).toBe(1);
      expect(fakeCtx.createdBufferSources[0].started).toBeTrue();
      expect(fakeCtx.resumeCalls).toBe(1);

      resolveFetch(okResponse());
      await armPromise;
    });

    it('decodes the Opus source when its fetch succeeds', async () => {
      fetchSpy.and.returnValue(Promise.resolve(okResponse()));

      await service.arm();

      expect(fetchSpy).toHaveBeenCalledTimes(1);
      expect(fetchSpy.calls.argsFor(0)[0]).toMatch(/AIBenchmarkingComplete\.opus$/);
      expect(fakeCtx.decodeAudioDataCalls.length).toBe(1);
    });

    it('falls back to the AAC source when the Opus fetch fails', async () => {
      fetchSpy.and.callFake((url: string) =>
        String(url).includes('.opus') ? Promise.reject(new Error('network down')) : Promise.resolve(okResponse()));

      await service.arm();

      expect(fetchSpy).toHaveBeenCalledTimes(2);
      expect(fakeCtx.decodeAudioDataCalls.length).toBe(1);
    });

    it('falls back to the AAC source when the Opus decode fails', async () => {
      fetchSpy.and.callFake(() => Promise.resolve(okResponse()));
      let decodeCalls = 0;
      fakeCtx.decodeAudioData = (buf: ArrayBuffer) => {
        decodeCalls++;
        fakeCtx.decodeAudioDataCalls.push(buf);
        return decodeCalls === 1 ? Promise.reject(new Error('bad codec')) : Promise.resolve({} as AudioBuffer);
      };

      await service.arm();

      expect(fetchSpy).toHaveBeenCalledTimes(2);
      expect(decodeCalls).toBe(2);
    });

    it('shares one in-flight arm() across concurrent callers', async () => {
      fetchSpy.and.returnValue(Promise.resolve(okResponse()));

      await Promise.all([service.arm(), service.arm()]);

      expect(ctorSpy).toHaveBeenCalledTimes(1);
      expect(fetchSpy).toHaveBeenCalledTimes(1);
    });

    it('is a no-op on a second, later arm() once armed', async () => {
      fetchSpy.and.returnValue(Promise.resolve(okResponse()));

      await service.arm();
      await service.arm();

      expect(ctorSpy).toHaveBeenCalledTimes(1);
      expect(fetchSpy).toHaveBeenCalledTimes(1);
    });

    it('retries a fully failed arm on the next call', async () => {
      fetchSpy.and.returnValue(Promise.reject(new Error('network down')));

      await service.arm();
      expect(fetchSpy).toHaveBeenCalledTimes(2);

      fetchSpy.calls.reset();
      fetchSpy.and.returnValue(Promise.resolve(okResponse()));
      await service.arm();

      expect(fetchSpy).toHaveBeenCalledTimes(1);
    });

    it('never rejects, even when AudioContext throws on construction', async () => {
      ctorSpy.and.throwError('no audio hardware');
      fetchSpy.and.returnValue(Promise.resolve(okResponse()));

      await expectAsync(service.arm()).toBeResolved();
    });

    it('plays through the decoded buffer once armed, never touching the fallback element', async () => {
      fetchSpy.and.returnValue(Promise.resolve(okResponse()));

      await service.arm();
      const outcome = await service.play('run:1');

      expect(outcome).toBe('played');
      expect(fakeCtx.createdBufferSources.some(s => s.started)).toBeTrue();
      expect(fakeAudio.playCallCount).toBe(0);
    });

    it('resumes a suspended context before playing the buffer', async () => {
      fetchSpy.and.returnValue(Promise.resolve(okResponse()));
      await service.arm();
      fakeCtx.state = 'suspended';
      fakeCtx.resumeCalls = 0;

      await service.play('run:1');

      expect(fakeCtx.resumeCalls).toBe(1);
    });
  });

  describe('the hidden-tab deferred path', () => {
    it('resolves "deferred" after the timeout in a hidden tab, with no unhandled rejection, and marks the key played on a late fulfilment', async () => {
      spyOnProperty(document, 'hidden', 'get').and.returnValue(true);
      let rejectPlay!: (err: unknown) => void;
      const pending = new Promise<void>((_resolve, reject) => { rejectPlay = reject; });
      fakeAudio.play = () => { fakeAudio.playCallCount++; return pending; };

      jasmine.clock().install();
      try {
        const outcomePromise = service.play('run:1');
        jasmine.clock().tick(2001);
        const outcome = await outcomePromise;
        expect(outcome).toBe('deferred');
      } finally {
        jasmine.clock().uninstall();
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
      spyOnProperty(document, 'hidden', 'get').and.returnValue(true);
      let resolvePlay!: () => void;
      const pending = new Promise<void>(resolve => { resolvePlay = resolve; });
      fakeAudio.play = () => { fakeAudio.playCallCount++; return pending; };

      jasmine.clock().install();
      let outcome: string;
      try {
        const outcomePromise = service.play('run:1');
        jasmine.clock().tick(2001);
        outcome = await outcomePromise;
      } finally {
        jasmine.clock().uninstall();
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
});

/** Stands in for the Web Audio graph `arm()` and the buffer playback path build. */
class FakeAudioContext {
  state: 'running' | 'suspended' | 'closed' = 'running';
  sampleRate = 44100;
  resumeCalls = 0;
  readonly createdBufferSources: FakeBufferSource[] = [];
  readonly decodeAudioDataCalls: ArrayBuffer[] = [];
  readonly destination = {} as AudioDestinationNode;

  resume(): Promise<void> {
    this.resumeCalls++;
    this.state = 'running';
    return Promise.resolve();
  }

  createBuffer(channels: number, length: number, sampleRate: number): AudioBuffer {
    return { numberOfChannels: channels, length, sampleRate } as unknown as AudioBuffer;
  }

  createBufferSource(): FakeBufferSource {
    const source = new FakeBufferSource();
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
  connect(node: any): any { return node; }
  start(): void { this.started = true; }
}

class FakeGainNode {
  gain = { value: 1 };
  connect(node: any): any { return node; }
}
