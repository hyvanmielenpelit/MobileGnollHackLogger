import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { AiTelemetrySeriesGroupOptionDto } from '../../../../services/admin.service';
import { ChartGroupRow, TL_CHARTS_GROUPS_VIEW_STORAGE_KEY, groupRows } from './telemetry-chart-filters';
import { TelemetryChartGroupsComponent } from './telemetry-chart-groups.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function option(overrides: Partial<AiTelemetrySeriesGroupOptionDto> = {}): AiTelemetrySeriesGroupOptionDto {
  return {
    groupId: 1, groupName: 'Testers', memberCount: 12, requests: 40, estimatedCost: 1.25, pricedRequests: 40,
    lastRequestUtc: '2026-10-12T21:15:00Z',
    ...overrides
  };
}

const ROWS: ChartGroupRow[] = groupRows({
  groups: [
    option(),
    option({ groupId: 2, groupName: 'Admins', memberCount: 3, requests: 90 }),
    option({ groupId: null, groupName: 'No group', memberCount: 1, requests: 5 })
  ]
});

function clearStorage(): void {
  try {
    localStorage.removeItem(TL_CHARTS_GROUPS_VIEW_STORAGE_KEY);
  } catch {
    // Nothing stored.
  }
}

describe('TelemetryChartGroupsComponent', () => {
  let fixture: ComponentFixture<TelemetryChartGroupsComponent>;
  let component: TelemetryChartGroupsComponent;
  let el: HTMLElement;
  let emitted: string[][];

  beforeEach(async () => {
    clearStorage();
    await TestBed.configureTestingModule({ imports: [TelemetryChartGroupsComponent] }).compileComponents();
  });

  afterEach(() => {
    el?.querySelectorAll<HTMLElement>('[popover]').forEach(popover => {
      try { popover.hidePopover(); } catch { /* already closed */ }
    });
    fixture?.destroy();
    clearStorage();
  });

  function create(rows: ChartGroupRow[], options: { selected?: string[]; max?: number } = {}): void {
    fixture = TestBed.createComponent(TelemetryChartGroupsComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    emitted = [];
    fixture.componentRef.setInput('rows', rows);
    fixture.componentRef.setInput('selectedKeys', options.selected ?? []);
    if (options.max !== undefined) fixture.componentRef.setInput('max', options.max);
    component.selectionChange.subscribe(keys => {
      emitted.push(keys);
      fixture.componentRef.setInput('selectedKeys', keys);
    });
    fixture.detectChanges();
  }

  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => el.querySelector<T>(selector);
  const label = () => q('#tl-sel-groups-label')!;
  const checkbox = (i: number) => q<HTMLInputElement>(`#tl-sel-groups-opt-${i}`)!;
  const names = () => Array.from(el.querySelectorAll('.tl-sel-row-name')).map(textOf);
  const parts = (i: number) => Array.from(q(`#tl-sel-groups-opt-${i}-meta`)!
    .querySelectorAll('.tl-sel-parts > span:not(.tl-sel-dot):not(.visually-hidden)')).map(textOf);

  function click(target: HTMLElement): void {
    target.click();
    fixture.detectChanges();
  }

  it('lists the groups most requests first, each with its members, requests, cost and last active', () => {
    create(ROWS);
    expect(names()).toEqual(['Admins', 'Testers', 'No group']);
    expect(textOf(q('#tl-sel-groups-sort-trigger'))).toBe('Sort: Requests, highest first');
    expect(parts(1)).toEqual(['12 members', '40 requests', 'est. $1.25', 'last active 2026-10-12']);
    expect(Array.from(el.querySelectorAll('.tl-sort-fields .gh-facet-option-label')).map(textOf)).toEqual(
      ['Requests', 'Est. cost', 'Members', 'Last active', 'Group name']);
  });

  it('tags No group as the users in no group', () => {
    create(ROWS);
    expect(textOf(q('#tl-sel-groups-opt-2-meta .gh-tag'))).toBe('Users in no group');
    expect(q('#tl-sel-groups-opt-0-meta .gh-tag')).toBeNull();
    expect(parts(2)[0]).toBe('1 member');
  });

  it('says how a group counts', () => {
    create(ROWS);
    expect(textOf(q('.tl-charts-hint'))).toBe(
      'Choose the groups to chart. With none chosen — or all of them — every group counts. The figures cover the date range, keys and roles. '
      + 'A group counts its current members\' usage over the whole range; a member of two groups counts in both.');
    expect(textOf(el.querySelector('label[for="tl-sel-groups-search"]'))).toBe('Search groups by name');
  });

  it('chooses groups by key, No group as none, and reads the band in groups', () => {
    create(ROWS);
    click(checkbox(2));
    click(checkbox(0));
    expect(emitted[emitted.length - 1]).toEqual(['none', '2']);
    expect(textOf(label())).toBe('Your selection — 2 of 3 groups');
    expect(Array.from(el.querySelectorAll('.tl-sel-chip-name')).map(textOf)).toEqual(['No group', 'Admins']);
  });

  it('sorts by members and remembers the sort as version 1', () => {
    create(ROWS);
    click(q<HTMLInputElement>('#tl-sel-groups-sort-field-members')!);
    expect(names()).toEqual(['Testers', 'Admins', 'No group']);
    expect(JSON.parse(localStorage.getItem(TL_CHARTS_GROUPS_VIEW_STORAGE_KEY)!)).toEqual({
      version: 1, sort: { field: 'members', direction: 'desc' }
    });
  });

  it('keeps a chosen group that left the scope, named as last seen', () => {
    create(ROWS, { selected: ['1'] });
    fixture.componentRef.setInput('rows', ROWS.filter(r => r.key !== '1'));
    fixture.detectChanges();
    expect(names()[0]).toBe('Testers');
    expect(textOf(q('#tl-sel-groups-opt-0-meta'))).toBe('No usage in this scope');
    expect(checkbox(0).checked).toBe(true);
  });

  it('says when no group had usage', () => {
    create([]);
    expect(textOf(q('.tl-sel-empty'))).toBe('No group had AI usage in this date range with these keys and roles.');
  });
});
