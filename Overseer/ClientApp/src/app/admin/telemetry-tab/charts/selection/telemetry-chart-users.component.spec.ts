import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { AiTelemetrySeriesUserOptionDto } from '../../../../services/admin.service';
import { ChartUserRow, TL_CHARTS_USERS_VIEW_STORAGE_KEY, userRows } from './telemetry-chart-filters';
import { TelemetryChartUsersComponent } from './telemetry-chart-users.component';

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function option(index: number, overrides: Partial<AiTelemetrySeriesUserOptionDto> = {}): AiTelemetrySeriesUserOptionDto {
  return {
    userId: `u-${index}`, userName: `user${index}`, requests: 1000 - index, systemRequests: 1000 - index, userKeyRequests: 0,
    estimatedCost: 100 - index, pricedRequests: 1000 - index, lastRequestUtc: '2026-10-12T21:15:00Z',
    ...overrides
  };
}

/** Users 1 to n, highest cost first. */
function rowsOf(n: number, overrides: (i: number) => Partial<AiTelemetrySeriesUserOptionDto> = () => ({})): ChartUserRow[] {
  return userRows({ users: Array.from({ length: n }, (_, i) => option(i + 1, overrides(i + 1))) });
}

function clearStorage(): void {
  try {
    localStorage.removeItem(TL_CHARTS_USERS_VIEW_STORAGE_KEY);
  } catch {
    // Nothing stored.
  }
}

describe('TelemetryChartUsersComponent', () => {
  let fixture: ComponentFixture<TelemetryChartUsersComponent>;
  let component: TelemetryChartUsersComponent;
  let el: HTMLElement;
  let emitted: string[][];

  beforeEach(async () => {
    clearStorage();
    await TestBed.configureTestingModule({ imports: [TelemetryChartUsersComponent] }).compileComponents();
  });

  afterEach(() => {
    el?.querySelectorAll<HTMLElement>('[popover]').forEach(popover => {
      try { popover.hidePopover(); } catch { /* already closed */ }
    });
    fixture?.destroy();
    clearStorage();
  });

  function create(rows: ChartUserRow[], options: { selected?: string[]; max?: number; truncated?: boolean; total?: number } = {}): void {
    fixture = TestBed.createComponent(TelemetryChartUsersComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    emitted = [];
    fixture.componentRef.setInput('rows', rows);
    fixture.componentRef.setInput('selectedKeys', options.selected ?? []);
    fixture.componentRef.setInput('truncated', options.truncated ?? false);
    fixture.componentRef.setInput('total', options.total ?? rows.length);
    if (options.max !== undefined) fixture.componentRef.setInput('max', options.max);
    component.selectionChange.subscribe(keys => {
      emitted.push(keys);
      fixture.componentRef.setInput('selectedKeys', keys);
    });
    fixture.detectChanges();
  }

  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => el.querySelector<T>(selector);
  const label = () => q('#tl-sel-users-label')!;
  const checkbox = (i: number) => q<HTMLInputElement>(`#tl-sel-users-opt-${i}`)!;
  const names = () => Array.from(el.querySelectorAll('.tl-sel-row-name')).map(textOf);
  const status = () => textOf(q('#tl-sel-users-status'));

  function click(target: HTMLElement): void {
    target.click();
    fixture.detectChanges();
  }

  it('lists the users highest cost first, a gone account as Unknown user, with requests, cost and last active', () => {
    create(rowsOf(2, i => (i === 2 ? { userName: null } : {})));
    expect(names()).toEqual(['user1', 'Unknown user']);
    expect(textOf(q('#tl-sel-users-sort-trigger'))).toBe('Sort: Est. cost, highest first');
    const parts = Array.from(q('#tl-sel-users-opt-0-meta')!.querySelectorAll('.tl-sel-parts > span:not(.tl-sel-dot):not(.visually-hidden)')).map(textOf);
    expect(parts).toEqual(['999 requests', 'est. $99.00', 'last active 2026-10-12']);
  });

  it('says what a choice means, and labels its search by user name', () => {
    create(rowsOf(2));
    expect(textOf(q('.tl-charts-hint'))).toBe(
      'Choose the users to chart. With none chosen — or all of them — every user counts. The figures cover the date range, keys and roles.');
    expect(textOf(el.querySelector('label[for="tl-sel-users-search"]'))).toBe('Search users by user name');
    expect(q('.tl-sel-providers')).toBeNull();
  });

  it('reads the band in users', () => {
    create(rowsOf(4));
    expect(textOf(label())).toBe('Your selection — All users — no filter');
    click(checkbox(0));
    expect(textOf(label())).toBe('Your selection — 1 of 4 users');
    click(q('#tl-sel-users-select-all')!);
    expect(textOf(label())).toBe('Your selection — All 4 users chosen — the same as no filter');
  });

  it('notes the cap of a truncated list in the status line and the band, and offers no Select all', () => {
    create(rowsOf(60), { truncated: true, total: 1284 });
    expect(status()).toBe('Showing 50 of 60 users · the 60 users with the most requests of 1,284');
    expect(q('#tl-sel-users-select-all')).toBeNull();
    click(checkbox(0));
    expect(textOf(label())).toBe('Your selection — 1 of 1,284 users');
    expect(status()).toBe('Showing 50 of 60 users · 1 chosen · the 60 users with the most requests of 1,284');
  });

  it('caps a truncated list even when every listed user is chosen, without offering Select all', () => {
    create(rowsOf(3), { truncated: true, total: 10, max: 2, selected: ['u-1', 'u-2'] });
    expect(textOf(q('#tl-sel-users-cap'))).toBe('At most 2 users can be chosen.');
    expect(checkbox(2).getAttribute('aria-disabled')).toBe('true');
    click(checkbox(2));
    expect(emitted).toEqual([]);
    expect(checkbox(2).checked).toBe(false);
  });

  it('searches the user name, and offers Select all shown while narrowed', async () => {
    create(rowsOf(12));
    const input = q<HTMLInputElement>('#tl-sel-users-search')!;
    input.value = 'user1';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle(250);
    fixture.detectChanges();

    expect(names()).toEqual(['user1', 'user10', 'user11', 'user12']);
    expect(textOf(q('#tl-sel-users-select-shown'))).toBe('Select all shown (4)');
    expect(q('#tl-sel-users-clear-filters')).not.toBeNull();
  });

  it('remembers the sort as version 1', () => {
    create(rowsOf(3));
    click(q<HTMLInputElement>('#tl-sel-users-sort-field-name')!);
    click(q<HTMLInputElement>('#tl-sel-users-sort-direction-asc')!);
    expect(names()).toEqual(['user1', 'user2', 'user3']);
    expect(JSON.parse(localStorage.getItem(TL_CHARTS_USERS_VIEW_STORAGE_KEY)!)).toEqual({
      version: 1, sort: { field: 'name', direction: 'asc' }
    });
  });

  it('keeps a chosen user who left the scope, named as last seen', () => {
    create(rowsOf(2));
    click(checkbox(1));
    fixture.componentRef.setInput('rows', rowsOf(1));
    fixture.detectChanges();
    expect(names()).toEqual(['user2', 'user1']);
    expect(textOf(q('#tl-sel-users-opt-0-meta'))).toBe('No usage in this scope');
  });

  it('says when no user had usage', () => {
    create([]);
    expect(textOf(q('.tl-sel-empty'))).toBe('No user had AI usage in this date range with these keys and roles.');
  });
});
