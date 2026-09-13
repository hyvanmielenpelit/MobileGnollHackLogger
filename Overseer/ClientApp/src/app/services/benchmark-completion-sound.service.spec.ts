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
});
