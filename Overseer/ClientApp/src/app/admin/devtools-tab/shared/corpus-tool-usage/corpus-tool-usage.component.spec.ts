import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';

import { AdminDevtoolsService, CorpusToolUsage, CorpusToolUsageDays } from '../../../../services/admin-devtools.service';
import { buildCorpusToolUsage } from '../../../admin.component.testing';
import { CorpusToolUsageComponent, TOOL_USAGE_DAYS_KEY } from './corpus-tool-usage.component';

const nf = new Intl.NumberFormat();
const pf = new Intl.NumberFormat(undefined, { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 });
const df = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' });

/** An IntersectionObserver the spec fires by hand. */
class FakeIntersectionObserver {
  static instances: FakeIntersectionObserver[] = [];
  readonly root = null;
  readonly rootMargin = '';
  readonly thresholds: number[] = [];
  observed: Element[] = [];
  disconnected = false;

  constructor(private readonly callback: IntersectionObserverCallback) {
    FakeIntersectionObserver.instances.push(this);
  }

  observe(target: Element): void { this.observed.push(target); }
  unobserve(): void { /* not used */ }
  takeRecords(): IntersectionObserverEntry[] { return []; }
  disconnect(): void { this.disconnected = true; }

  fire(isIntersecting: boolean): void {
    this.callback([{ isIntersecting } as IntersectionObserverEntry], this as unknown as IntersectionObserver);
  }
}

/** 30 days of usage with wiki_search busy and wiki_view idle. */
function usageFixture(days: CorpusToolUsageDays = 30): CorpusToolUsage {
  const base = buildCorpusToolUsage({ days });
  const daily = new Array<number>(days).fill(0);
  daily[2] = 96;
  daily[5] = 4;
  return {
    ...base,
    sinceUtc: '2026-10-01T00:00:00Z',
    generatedAtUtc: '2026-10-18T12:00:00Z',
    tools: base.tools.map(t => t.tool === 'wiki_search'
      ? { ...t, calls: 100, completed: 90, errors: 8, other: 2, guardRefusals: 3, p50ExecutionMs: 38, p95ExecutionMs: 1200, daily }
      : t),
    corpora: base.corpora.map(c => c.corpus === 'GnollHackWiki'
      ? { ...c, calls: 100, completed: 90, errors: 8, other: 2, guardRefusals: 3, p50ExecutionMs: 40, p95ExecutionMs: 1500, daily }
      : c)
  };
}

describe('CorpusToolUsageComponent', () => {
  let fixture: ComponentFixture<CorpusToolUsageComponent>;
  let el: HTMLElement;
  let usageSpy: Mock;
  let storedDays: string | null;

  const q = <T extends Element = HTMLElement>(selector: string) => el.querySelector<T>(selector);
  const text = (node: Element | null) => (node?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const observer = () => FakeIntersectionObserver.instances[FakeIntersectionObserver.instances.length - 1];

  beforeEach(async () => {
    storedDays = localStorage.getItem(TOOL_USAGE_DAYS_KEY);
    localStorage.removeItem(TOOL_USAGE_DAYS_KEY);
    FakeIntersectionObserver.instances = [];
    vi.stubGlobal('IntersectionObserver', FakeIntersectionObserver);

    await TestBed.configureTestingModule({
      imports: [CorpusToolUsageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    const devtools = TestBed.inject(AdminDevtoolsService);
    usageSpy = vi.spyOn(devtools, 'getCorpusToolUsage')
      .mockImplementation((days: CorpusToolUsageDays) => of(usageFixture(days))) as unknown as Mock;
  });

  afterEach(() => {
    fixture?.destroy();
    vi.unstubAllGlobals();
    if (storedDays === null) {
      localStorage.removeItem(TOOL_USAGE_DAYS_KEY);
    } else {
      localStorage.setItem(TOOL_USAGE_DAYS_KEY, storedDays);
    }
  });

  function create(): void {
    fixture = TestBed.createComponent(CorpusToolUsageComponent);
    el = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('corpus', 'GnollHackWiki');
    fixture.componentRef.setInput('idPrefix', 'gw');
    fixture.detectChanges();
  }

  function scrollIntoView(): void {
    observer().fire(true);
    fixture.detectChanges();
  }

  it('waits until it scrolls into view, then loads once and stops observing', () => {
    create();
    expect(observer().observed).toEqual([el]);
    expect(usageSpy).not.toHaveBeenCalled();
    observer().fire(false);
    expect(usageSpy).not.toHaveBeenCalled();

    scrollIntoView();
    expect(usageSpy).toHaveBeenCalledTimes(1);
    expect(usageSpy).toHaveBeenCalledWith(30);
    expect(observer().disconnected).toBe(true);
  });

  it('loads at once where IntersectionObserver is missing', () => {
    vi.stubGlobal('IntersectionObserver', undefined);
    create();
    expect(usageSpy).toHaveBeenCalledWith(30);
  });

  it('shows the corpus tiles from the server\'s per-corpus entry', () => {
    create();
    scrollIntoView();
    const tiles = Array.from(el.querySelectorAll('.ctu-tiles > .gh-health-tile'));
    expect(tiles.map(t => text(t.querySelector('dt')))).toEqual([
      'Calls', 'Error rate', 'Index-warming refusals', 'Median time', '95th percentile'
    ]);
    expect(text(tiles[0].querySelector('dd'))).toBe('100');
    expect(text(tiles[1].querySelector('dd'))).toBe(`${pf.format(0.08)} 8 errors`);
    expect(text(tiles[2].querySelector('dd'))).toBe('3');
    expect(text(tiles[3].querySelector('dd'))).toBe('40 ms');
    expect(text(tiles[4].querySelector('dd'))).toBe('1.5 s');
  });

  it('reads an error rate of a dash without calls', () => {
    usageSpy.mockImplementation((days: CorpusToolUsageDays) => of(buildCorpusToolUsage({ days })));
    create();
    scrollIntoView();
    const tile = Array.from(el.querySelectorAll('.ctu-tiles > .gh-health-tile'))[1];
    expect(text(tile.querySelector('dd'))).toBe('— 0 errors');
  });

  it('lists one row per tool of this corpus only', () => {
    create();
    scrollIntoView();
    const wrap = q('.gh-dash-table-wrap')!;
    expect(wrap.getAttribute('role')).toBe('region');
    expect(wrap.getAttribute('tabindex')).toBe('0');
    expect(wrap.getAttribute('aria-labelledby')).toBe('gw-usage-title');
    expect(Array.from(el.querySelectorAll('.ctu-table thead th')).map(th => text(th))).toEqual([
      'Tool', 'Calls', 'Completed', 'Errors', 'Other', 'Refusals', 'Median', 'p95', 'Trend'
    ]);
    const rows = Array.from(el.querySelectorAll('.ctu-table tbody tr'));
    expect(rows.map(r => text(r.querySelector('th[scope="row"]')))).toEqual(['wiki_search', 'wiki_view', 'monster_lookup', 'item_lookup']);
    const first = rows[0];
    const cell = (label: string) => text(first.querySelector(`td[data-label="${label}"]`));
    expect(cell('Calls')).toBe('100');
    expect(cell('Completed')).toBe('90');
    expect(cell('Errors')).toBe('8');
    expect(cell('Other')).toBe('2');
    expect(cell('Refusals')).toBe('3');
    expect(cell('Median')).toBe('38 ms');
    expect(cell('p95')).toBe('1.2 s');
  });

  it('draws each trend as a labeled sparkline, zero days as baseline ticks', () => {
    create();
    scrollIntoView();
    const rows = Array.from(el.querySelectorAll('.ctu-table tbody tr'));
    const busy = rows[0].querySelector('svg.ctu-spark')!;
    expect(busy.getAttribute('role')).toBe('img');
    expect(busy.getAttribute('aria-label')).toBe(
      `wiki_search daily calls, last 30 days: total ${nf.format(100)}, peak 96 on ${df.format(new Date(Date.UTC(2026, 9, 3)))}`);
    expect(busy.querySelectorAll('rect').length).toBe(30);
    expect(busy.querySelectorAll('rect.ctu-bar').length).toBe(2);
    expect(busy.querySelectorAll('rect.ctu-tick').length).toBe(28);
    const peakBar = busy.querySelectorAll('rect')[2];
    expect(peakBar.getAttribute('height')).toBe('22');
    expect(busy.querySelectorAll('rect')[0].getAttribute('height')).toBe('1');

    const idle = rows[1].querySelector('svg.ctu-spark')!;
    expect(idle.getAttribute('aria-label')).toBe('wiki_view daily calls, last 30 days: none');
  });

  it('states what the counts include and exclude', () => {
    create();
    scrollIntoView();
    const facts = Array.from(el.querySelectorAll('.ctu-facts li')).map(li => text(li));
    expect(facts).toContain('GnollBench runs are not included: their tool calls are stored with the run.');
    expect(facts).toContain('Ephemeral sessions are never stored, so their calls are not counted.');
    expect(facts).toContain('Calls made by sub-agents are included.');
    expect(facts).toContain('In confidential sessions a refusal counts as an error, not as a refusal, because its error text is encrypted.');
    expect(facts[facts.length - 1]).toMatch(/^Updated every 5 minutes; this view was computed at .+\.$/);
  });

  it('offers the period as a radio group, 30 days by default', () => {
    create();
    const fieldset = q('fieldset.gh-choice')!;
    expect(text(fieldset.querySelector('legend'))).toBe('Period');
    const radios = Array.from(fieldset.querySelectorAll<HTMLInputElement>('input[type="radio"]'));
    expect(radios.map(r => r.id)).toEqual(['gw-usage-days-7', 'gw-usage-days-30', 'gw-usage-days-90']);
    expect(radios.map(r => r.checked)).toEqual([false, true, false]);
    expect(new Set(radios.map(r => r.name)).size).toBe(1);
    expect(Array.from(fieldset.querySelectorAll('label.gh-radio')).map(l => text(l))).toEqual(['7 days', '30 days', '90 days']);
  });

  it('remembers a new period and reloads with it', () => {
    create();
    scrollIntoView();
    q<HTMLInputElement>('#gw-usage-days-90')!.click();
    fixture.detectChanges();
    expect(usageSpy).toHaveBeenLastCalledWith(90);
    expect(localStorage.getItem(TOOL_USAGE_DAYS_KEY)).toBe('90');
    expect(q('.ctu-table caption')!.textContent).toContain('last 90 days');
  });

  it('only remembers a period chosen before it was in view', () => {
    create();
    q<HTMLInputElement>('#gw-usage-days-7')!.click();
    fixture.detectChanges();
    expect(usageSpy).not.toHaveBeenCalled();
    expect(localStorage.getItem(TOOL_USAGE_DAYS_KEY)).toBe('7');
    scrollIntoView();
    expect(usageSpy).toHaveBeenCalledWith(7);
  });

  it('starts from a stored period, and from 30 days when the stored value is not a period', () => {
    localStorage.setItem(TOOL_USAGE_DAYS_KEY, '7');
    create();
    expect(q<HTMLInputElement>('#gw-usage-days-7')!.checked).toBe(true);
    fixture.destroy();

    localStorage.setItem(TOOL_USAGE_DAYS_KEY, '14');
    create();
    expect(q<HTMLInputElement>('#gw-usage-days-30')!.checked).toBe(true);
  });

  it('shows a load error with Try again, which loads again', () => {
    usageSpy.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Server Error' })));
    create();
    scrollIntoView();
    const error = q('#gw-usage-error')!;
    expect(error.classList).toContain('gh-field-error');
    expect(text(error)).toBe('The tool usage could not be loaded (HTTP 500).');
    const retry = q<HTMLButtonElement>('.ctu-retry')!;
    expect(retry.classList).toContain('btn-ghost');
    expect(retry.querySelector('svg')).toBeNull();
    expect(text(retry)).toBe('Try again');

    retry.click();
    fixture.detectChanges();
    expect(usageSpy).toHaveBeenCalledTimes(2);
    expect(q('#gw-usage-error')).toBeNull();
    expect(q('.ctu-table')).not.toBeNull();
  });

  it('disconnects the observer on destroy', () => {
    create();
    const o = observer();
    fixture.destroy();
    expect(o.disconnected).toBe(true);
  });
});
