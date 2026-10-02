import { TestBed } from '@angular/core/testing';

import { BenchmarkPollTickerService } from './benchmark-poll-ticker.service';

/** Stands in for the real Worker the service constructs against `/workers/benchmark-poll-ticker.js`. */
class FakeWorker {
  static instances: FakeWorker[] = [];

  onmessage: ((ev: MessageEvent) => void) | null = null;
  onerror: ((ev: any) => void) | null = null;
  readonly postedMessages: any[] = [];
  terminateCalls = 0;

  constructor(public url: string | URL) {
    FakeWorker.instances.push(this);
  }

  postMessage(data: any): void {
    this.postedMessages.push(data);
  }

  terminate(): void {
    this.terminateCalls++;
  }

  /** Test helper: simulates the worker's own script posting a tick back. */
  tick(): void {
    this.onmessage?.({ data: { type: 'tick' } } as MessageEvent);
  }
}

describe('BenchmarkPollTickerService', () => {
  let service: BenchmarkPollTickerService;
  let originalWorker: unknown;

  beforeEach(() => {
    FakeWorker.instances = [];
    originalWorker = (window as any).Worker;
    TestBed.configureTestingModule({});
    service = TestBed.inject(BenchmarkPollTickerService);
  });

  afterEach(() => {
    (window as any).Worker = originalWorker;
  });

  describe('when Worker is available', () => {
    beforeEach(() => {
      (window as any).Worker = FakeWorker;
    });

    it('starts a worker with the same-origin script and posts a start message with the interval', () => {
      const onTick = vi.fn().mockName('onTick');
      service.start(2000, onTick);

      expect(FakeWorker.instances.length).toBe(1);
      expect(String(FakeWorker.instances[0].url)).toContain('/workers/benchmark-poll-ticker.js');
      expect(FakeWorker.instances[0].postedMessages).toEqual([{ type: 'start', intervalMs: 2000 }]);
    });

    it('reports mode "worker" while the worker keeps delivering ticks', () => {
      const onTick = vi.fn().mockName('onTick');
      const handle = service.start(2000, onTick);

      expect(handle.mode).toBe('worker');
      FakeWorker.instances[0].tick();
      expect(onTick).toHaveBeenCalledTimes(1);
      expect(handle.mode).toBe('worker');
    });

    it('does not call onTick after the handle is disposed', () => {
      const onTick = vi.fn().mockName('onTick');
      const handle = service.start(2000, onTick);
      const worker = FakeWorker.instances[0];

      handle();

      expect(worker.postedMessages).toContainEqual({ type: 'stop' } as any);
      expect(worker.terminateCalls).toBe(1);

      worker.tick();
      expect(onTick).not.toHaveBeenCalled();
    });

    it('falls back to a timer and keeps ticking when the worker reports an error after starting', () => {
      vi.useFakeTimers();
      try {
        const onTick = vi.fn().mockName('onTick');
        const handle = service.start(1000, onTick);
        const worker = FakeWorker.instances[0];

        worker.onerror?.(new Event('error'));

        expect(handle.mode).toBe('timer');
        expect(worker.terminateCalls).toBe(1);

        vi.advanceTimersByTime(1000);
        expect(onTick).toHaveBeenCalledTimes(1);

        handle();
        vi.advanceTimersByTime(1000);
        expect(onTick).toHaveBeenCalledTimes(1);
      }
      finally {
        vi.useRealTimers();
      }
    });
  });

  describe('when Worker throws on construction', () => {
    beforeEach(() => {
      (window as any).Worker = class {
        constructor() {
          throw new Error('blocked by content security policy');
        }
      };
    });

    it('falls back to a timer and reports mode "timer"', () => {
      vi.useFakeTimers();
      try {
        const onTick = vi.fn().mockName('onTick');
        const handle = service.start(1500, onTick);

        expect(handle.mode).toBe('timer');
        vi.advanceTimersByTime(1500);
        expect(onTick).toHaveBeenCalledTimes(1);

        handle();
        vi.advanceTimersByTime(1500);
        expect(onTick).toHaveBeenCalledTimes(1);
      }
      finally {
        vi.useRealTimers();
      }
    });
  });

  describe('when Worker does not exist', () => {
    beforeEach(() => {
      delete (window as any).Worker;
    });

    it('uses a timer directly, without attempting to construct a worker', () => {
      vi.useFakeTimers();
      try {
        const onTick = vi.fn().mockName('onTick');
        const handle = service.start(2000, onTick);

        expect(handle.mode).toBe('timer');
        vi.advanceTimersByTime(2000);
        expect(onTick).toHaveBeenCalledTimes(1);

        handle();
        vi.advanceTimersByTime(2000);
        expect(onTick).toHaveBeenCalledTimes(1);
      }
      finally {
        vi.useRealTimers();
      }
    });
  });

  it('gives the run poller and the series poller independent handles', () => {
    (window as any).Worker = FakeWorker;
    const onTickA = vi.fn().mockName('onTickA');
    const onTickB = vi.fn().mockName('onTickB');

    const handleA = service.start(2000, onTickA);
    const handleB = service.start(5000, onTickB);

    expect(FakeWorker.instances.length).toBe(2);
    FakeWorker.instances[0].tick();
    expect(onTickA).toHaveBeenCalledTimes(1);
    expect(onTickB).not.toHaveBeenCalled();

    handleA();
    expect(FakeWorker.instances[1].terminateCalls).toBe(0);
    handleB();
  });
});
