import { TestBed } from '@angular/core/testing';

import { BenchmarkBackgroundActivityService } from './benchmark-background-activity.service';

describe('BenchmarkBackgroundActivityService', () => {
  let service: BenchmarkBackgroundActivityService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(BenchmarkBackgroundActivityService);
  });

  interface FakeRequest {
    name: string;
    /** Runs the lock callback, as the lock manager does once the lock is granted. */
    grant: () => Promise<void>;
    /** Settles when the lock is released; set once granted. */
    promise: Promise<void> | null;
  }

  /**
   * A fake lock manager. By default it grants each request immediately; with `deferred` the test
   * grants it later, which is how the real API behaves (the callback runs asynchronously).
   */
  function installFakeLocks(deferred = false): { requests: FakeRequest[] } {
    const requests: FakeRequest[] = [];
    const fake = {
      request: (name: string, callback: () => Promise<void>) => {
        const request: FakeRequest = {
          name,
          promise: null,
          grant: () => {
            request.promise = callback();
            return request.promise;
          }
        };
        requests.push(request);
        return deferred ? new Promise<void>(() => { /* settled by the test via grant() */ }) : request.grant();
      }
    };
    spyOn(service as any, 'lockManager').and.returnValue(fake);
    return { requests };
  }

  it('reports unsupported and never throws when the Locks API does not exist', () => {
    spyOn(service as any, 'lockManager').and.returnValue(undefined);
    expect(() => service.acquireForRun(54)).not.toThrow();
    expect(service.state).toBe('unsupported');
    expect(service.isSupported()).toBeFalse();
  });

  it('names the lock for a run and for a series distinctly', () => {
    const { requests } = installFakeLocks();
    service.acquireForRun(54);
    expect(requests[0].name).toBe('overseer-benchmark-live:run:54');
    expect(service.heldName).toBe('overseer-benchmark-live:run:54');

    service.acquireForSeries(9);
    expect(requests[1].name).toBe('overseer-benchmark-live:series:9');
    expect(service.heldName).toBe('overseer-benchmark-live:series:9');
  });

  it('holds the lock until release() is called', async () => {
    const { requests } = installFakeLocks();
    service.acquireForRun(54);
    expect(service.state).toBe('held');

    let settled = false;
    requests[0].promise!.then(() => { settled = true; });
    await Promise.resolve();
    expect(settled).toBeFalse();

    service.release();
    await requests[0].promise;
    expect(settled).toBeTrue();
    expect(service.state).toBe('idle');
    expect(service.heldName).toBeNull();
  });

  it('acquiring a new operation releases the previous lock first, so two tabs on different runs never queue', async () => {
    const { requests } = installFakeLocks();
    service.acquireForRun(1);
    let firstSettled = false;
    requests[0].promise!.then(() => { firstSettled = true; });

    service.acquireForRun(2);
    await Promise.resolve();

    expect(firstSettled).toBeTrue();
    expect(requests.length).toBe(2);
    expect(service.state).toBe('held');
    expect(service.heldName).toBe('overseer-benchmark-live:run:2');
  });

  it('never holds a lock whose grant arrives after it was released', async () => {
    const { requests } = installFakeLocks(true);
    service.acquireForRun(54);
    expect(service.state).toBe('requested');

    service.release();
    let settled = false;
    requests[0].grant().then(() => { settled = true; });
    await Promise.resolve();

    expect(settled).toBeTrue();
    expect(service.state).toBe('idle');
  });

  it('never holds a superseded lock whose grant arrives after the replacement was requested', async () => {
    const { requests } = installFakeLocks(true);
    service.acquireForRun(1);
    service.acquireForRun(2);

    let firstSettled = false;
    requests[0].grant().then(() => { firstSettled = true; });
    requests[1].grant();
    await Promise.resolve();

    expect(firstSettled).toBeTrue();
    expect(service.state).toBe('held');
    expect(service.heldName).toBe('overseer-benchmark-live:run:2');

    let secondSettled = false;
    requests[1].promise!.then(() => { secondSettled = true; });
    service.release();
    await Promise.resolve();
    expect(secondSettled).toBeTrue();
  });

  it('records a rejected acquisition as an error rather than throwing', async () => {
    spyOn(service as any, 'lockManager').and.returnValue({
      request: () => Promise.reject(new Error('lock manager unavailable'))
    });

    expect(() => service.acquireForRun(54)).not.toThrow();
    await Promise.resolve();
    await Promise.resolve();

    expect(service.state).toBe('error');
    expect(service.lastError).toContain('lock manager unavailable');
  });

  it('release() is a safe no-op when nothing was ever acquired', () => {
    expect(() => service.release()).not.toThrow();
    expect(service.state).toBe('unsupported');
  });
});
