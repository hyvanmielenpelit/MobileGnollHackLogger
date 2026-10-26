import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RelativeTimePipe } from './relative-time.pipe';
import { RelativeTimeTickerService } from '../services/relative-time-ticker.service';

describe('RelativeTimePipe', () => {
  const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
  let pipe: RelativeTimePipe;
  let cdr: { markForCheck: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    // The ticker's interval and the clock both run on fake time.
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    vi.setSystemTime(new Date('2026-10-26T12:00:00Z'));
    cdr = { markForCheck: vi.fn() };
    pipe = new RelativeTimePipe(cdr as unknown as ChangeDetectorRef, TestBed.inject(RelativeTimeTickerService));
  });

  afterEach(() => {
    pipe.ngOnDestroy();
    vi.useRealTimers();
  });

  it('returns the cached text for the same value without recomputing it', () => {
    const compute = vi.spyOn(pipe as any, 'calculateRelativeTime');
    const value = '2026-10-26T11:55:00';

    const first = pipe.transform(value);
    const second = pipe.transform(value);

    expect(first).toBe(rtf.format(-5, 'minute'));
    expect(second).toBe(first);
    expect(compute).toHaveBeenCalledTimes(1);
  });

  it('recomputes for a new value', () => {
    const compute = vi.spyOn(pipe as any, 'calculateRelativeTime');

    pipe.transform('2026-10-26T11:55:00');
    const other = pipe.transform('2026-10-26T09:00:00');

    expect(other).toBe(rtf.format(-3, 'hour'));
    expect(compute).toHaveBeenCalledTimes(2);
  });

  it('takes a label across a boundary on the ticker, and transform then returns it', () => {
    const value = '2026-10-26T11:59:10';
    expect(pipe.transform(value)).toBe('just now');
    const compute = vi.spyOn(pipe as any, 'calculateRelativeTime');

    vi.advanceTimersByTime(15000);

    expect(cdr.markForCheck).toHaveBeenCalledTimes(1);
    expect(compute).toHaveBeenCalledTimes(1);
    expect(pipe.transform(value)).toBe(rtf.format(-1, 'minute'));
    expect(compute).toHaveBeenCalledTimes(1);
  });
});
