import { KatexLoaderService, type PrefetchHost } from './katex-loader.service';

describe('KatexLoaderService', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('loads KaTeX once and keeps it', async () => {
    const service = new KatexLoaderService();
    expect(service.get()).toBeNull();

    const first = service.load();
    expect(service.load()).toBe(first);

    const katex = await first;
    expect(katex).not.toBeNull();
    expect(service.get()).toBe(katex);
    expect(typeof service.get()!.renderToString).toBe('function');
  });

  it('prefetches through scheduler.postTask at background priority', () => {
    const service = new KatexLoaderService();
    const load = vi.spyOn(service, 'load').mockResolvedValue(null);

    let task: (() => void) | undefined;
    let options: SchedulerPostTaskOptions | undefined;
    const postTask = vi.fn((callback: () => void, opts?: SchedulerPostTaskOptions) => {
      task = callback;
      options = opts;
      return new Promise<void>((_resolve, reject) => {
        opts?.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')));
      });
    });
    const host = {
      setTimeout: vi.fn(),
      clearTimeout: vi.fn(),
      scheduler: { postTask } as unknown as Scheduler,
      requestIdleCallback: vi.fn(),
      cancelIdleCallback: vi.fn()
    } as unknown as PrefetchHost;

    const cancel = service.prefetchWhenIdle(host);

    expect(postTask).toHaveBeenCalledTimes(1);
    expect(options?.priority).toBe('background');
    expect(options?.signal).toBeInstanceOf(AbortSignal);
    expect(host.requestIdleCallback).not.toHaveBeenCalled();
    expect(host.setTimeout).not.toHaveBeenCalled();

    task!();
    expect(load).toHaveBeenCalledTimes(1);

    cancel();
    expect(options!.signal!.aborted).toBe(true);
  });

  it('falls back to requestIdleCallback', () => {
    const service = new KatexLoaderService();
    const load = vi.spyOn(service, 'load').mockResolvedValue(null);

    let idle: (() => void) | undefined;
    const requestIdleCallback = vi.fn((callback: () => void) => {
      idle = callback;
      return 42;
    });
    const host = {
      setTimeout: vi.fn(),
      clearTimeout: vi.fn(),
      requestIdleCallback,
      cancelIdleCallback: vi.fn()
    } as unknown as PrefetchHost;

    const cancel = service.prefetchWhenIdle(host);

    expect(requestIdleCallback).toHaveBeenCalledWith(expect.any(Function), { timeout: 5000 });
    expect(host.setTimeout).not.toHaveBeenCalled();

    idle!();
    expect(load).toHaveBeenCalledTimes(1);

    cancel();
    expect(host.cancelIdleCallback).toHaveBeenCalledWith(42);
  });

  it('falls back to a two-second timer', () => {
    vi.useFakeTimers();
    const service = new KatexLoaderService();
    const load = vi.spyOn(service, 'load').mockResolvedValue(null);
    const host: PrefetchHost = {
      setTimeout: ((handler: () => void, ms?: number) => setTimeout(handler, ms)) as Window['setTimeout'],
      clearTimeout: (id?: number) => clearTimeout(id)
    };

    service.prefetchWhenIdle(host);
    vi.advanceTimersByTime(1999);
    expect(load).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(load).toHaveBeenCalledTimes(1);

    const cancel = service.prefetchWhenIdle(host);
    cancel();
    vi.advanceTimersByTime(2000);
    expect(load).toHaveBeenCalledTimes(1);
  });
});
