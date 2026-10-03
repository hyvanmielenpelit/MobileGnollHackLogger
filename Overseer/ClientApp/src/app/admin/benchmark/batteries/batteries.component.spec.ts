import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkBatteryDto,
  BenchmarkBatteryLeaderboardDto,
  BenchmarkBatterySuiteDto,
  BenchmarkSuiteDto
} from '../../../services/admin-benchmark.service';
import { FilterFacetComponent } from '../../../shared/data-table/filter-facet.component';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { BenchmarkBatteriesComponent } from './batteries.component';
import { BatteryLeaderboardDialogComponent } from './battery-leaderboard-dialog.component';
import { BATTERY_LIST_VIEW_STORAGE_KEY, batteryWeightMix } from './battery.models';

function dto<T>(value: object): T {
  return value as T;
}

const HASH = 'abcdef0123456789abcdef0123456789';

function suite(index: number, name: string, overrides: Partial<BenchmarkBatterySuiteDto> = {}): BenchmarkBatterySuiteDto {
  return {
    index, suiteId: 10 + index, suiteName: name, deleted: false, customWeight: null, questionCount: 10,
    assessedQuestionCount: 10, difficultyFullyAssessed: true, difficultyMass: 500,
    ...overrides
  };
}

/** A battery; `suites` and `weights` replace the two-suite default together. */
function battery(overrides: Partial<BenchmarkBatteryDto> = {}, weights?: number[]): BenchmarkBatteryDto {
  const suites = overrides.suites ?? [
    suite(0, 'Gameplay Help', { questionCount: 10, difficultyMass: 400 }),
    suite(1, 'Board Reading', { questionCount: 8, difficultyMass: 600 })
  ];
  const declared = weights ?? (overrides.suites ? suites.map(() => 1 / suites.length) : [0.4, 0.6]);
  return dto<BenchmarkBatteryDto>({
    id: 3,
    name: 'Core Battery',
    description: null,
    weightingScheme: 'DifficultyMass',
    weightingSchemeLabel: 'Questions and difficulty',
    revision: 2,
    definitionSha256: HASH,
    isArchived: false,
    brokenSuiteNames: [],
    validationErrors: [],
    createdByUserName: 'admin',
    createdAtUtc: '2026-09-01T00:00:00Z',
    modifiedAtUtc: '2026-09-02T14:05:00Z',
    batteryRunCount: 2,
    hasActiveBatteryRun: false,
    rankedResultCount: 3,
    weightPreviews: [
      { scheme: 'DifficultyMass', schemeLabel: 'Questions and difficulty', declared: true, weights: declared },
      { scheme: 'Equal', schemeLabel: 'Equal per suite', declared: false, weights: suites.map(() => 1 / suites.length) }
    ],
    ...overrides,
    suites
  });
}

/** Four batteries: newest-modified order Bravo, Delta, Alpha, Charlie. */
function fourBatteries(): BenchmarkBatteryDto[] {
  return [
    battery({
      id: 1, name: 'Alpha', weightingScheme: 'Equal', weightingSchemeLabel: 'Equal per suite', batteryRunCount: 5,
      modifiedAtUtc: '2026-09-05T00:00:00Z', definitionSha256: 'a'.repeat(64)
    }),
    battery({
      id: 2, name: 'Bravo', batteryRunCount: 1, modifiedAtUtc: '2026-09-10T00:00:00Z', definitionSha256: 'b'.repeat(64),
      suites: [suite(0, 'Gameplay Help'), suite(1, 'Lore')]
    }),
    battery({
      id: 3, name: 'Charlie', weightingScheme: 'ItemCount', weightingSchemeLabel: 'Questions only', batteryRunCount: 0,
      modifiedAtUtc: '2026-09-01T00:00:00Z', definitionSha256: 'c'.repeat(64),
      suites: [suite(0, 'Gameplay Help'), suite(1, 'Board Reading'), suite(2, 'Lore')]
    }),
    battery({
      id: 4, name: 'Delta', description: 'Monster fights', batteryRunCount: 9, modifiedAtUtc: '2026-09-08T00:00:00Z',
      definitionSha256: 'd'.repeat(64), suites: [suite(0, 'Lore'), suite(1, 'Combat')]
    })
  ];
}

describe('BenchmarkBatteriesComponent', () => {
  let fixture: ComponentFixture<BenchmarkBatteriesComponent>;
  let component: BenchmarkBatteriesComponent;
  let service: MockedObject<AdminBenchmarkService>;

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(selector: string, root: ParentNode = el()): string {
    return (root.querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function click(selector: string): void {
    const target = el().querySelector(selector) as HTMLElement | null;
    expect(target, selector).not.toBeNull();
    target!.click();
    fixture.detectChanges();
  }

  function card(id: number): HTMLElement {
    const found = el().querySelector(`article.bb-card[data-battery-id="${id}"]`) as HTMLElement | null;
    expect(found, `card ${id}`).not.toBeNull();
    return found!;
  }

  function shownTitles(): string[] {
    return Array.from(el().querySelectorAll('.bb-card-title')).map(h => h.textContent!.trim());
  }

  function create(): void {
    fixture = TestBed.createComponent(BenchmarkBatteriesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function clearStoredView(): void {
    try {
      localStorage.removeItem(BATTERY_LIST_VIEW_STORAGE_KEY);
    } catch {
      // Storage unavailable: nothing to clear.
    }
  }

  beforeEach(async () => {
    clearStoredView();
    service = {
      getBatteries: vi.fn().mockName("AdminBenchmarkService.getBatteries"),
      getSuites: vi.fn().mockName("AdminBenchmarkService.getSuites"),
      getBatteryLeaderboard: vi.fn().mockName("AdminBenchmarkService.getBatteryLeaderboard"),
      archiveBattery: vi.fn().mockName("AdminBenchmarkService.archiveBattery"),
      deleteBattery: vi.fn().mockName("AdminBenchmarkService.deleteBattery"),
      createBattery: vi.fn().mockName("AdminBenchmarkService.createBattery"),
      updateBattery: vi.fn().mockName("AdminBenchmarkService.updateBattery"),
      getQuestions: vi.fn().mockName("AdminBenchmarkService.getQuestions")
    } as unknown as MockedObject<AdminBenchmarkService>;
    service.getBatteries.mockReturnValue(of([battery()]));
    service.getSuites.mockReturnValue(of([dto<BenchmarkSuiteDto>({ id: 11, name: 'Gameplay Help', questionCount: 10, assessedQuestionCount: 10, difficultyFullyAssessed: true })]));
    service.getBatteryLeaderboard.mockReturnValue(of(dto<BenchmarkBatteryLeaderboardDto>({
      definitionSha256: HASH, batteryId: 3, batteryName: 'Core Battery', classes: [], incomplete: []
    })));
    service.archiveBattery.mockImplementation((id: number, archived?: boolean) => of(battery({ id, isArchived: archived ?? true })));
    service.deleteBattery.mockReturnValue(of(void 0));
    service.getQuestions.mockReturnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [BenchmarkBatteriesComponent],
      providers: [
        { provide: AdminBenchmarkService, useValue: service },
        BenchmarkShellBridge
      ]
    }).compileComponents();
  });

  afterEach(() => {
    vi.useRealTimers();
    el().querySelectorAll('dialog').forEach(d => { if ((d as HTMLDialogElement).open) (d as HTMLDialogElement).close(); });
    clearStoredView();
  });

  it('loads the batteries and the suites, and states the count in the status line', () => {
    create();
    expect(service.getBatteries).toHaveBeenCalled();
    expect(service.getSuites).toHaveBeenCalled();
    expect(text('#bb-list-status')).toBe('One battery');
    expect(el().querySelector('#bb-list-status')?.getAttribute('role')).toBe('status');
  });

  it('shows the empty state', () => {
    service.getBatteries.mockReturnValue(of([]));
    create();
    expect(text('.bb-empty-batteries')).toContain('No batteries yet');
    expect(el().querySelector('.bb-cards')).toBeNull();
  });

  describe('a card', () => {
    it('lays out a two-suite battery: kicker, title, meta, metrics, weight bar and every suite row', () => {
      create();
      const c = card(3);
      expect(c.getAttribute('aria-labelledby')).toBe('bb-card-title-3');
      expect(text('.bb-card-kicker', c)).toBe('#3·, Revision 2·, Questions and difficulty');
      expect(c.querySelector('.bb-sep > [aria-hidden="true"]')?.textContent).toBe('·');
      expect(c.querySelector('.bb-sep > .visually-hidden')?.textContent).toBe(', ');
      const title = c.querySelector('h5.bb-card-title') as HTMLElement;
      expect(title.textContent!.trim()).toBe('Core Battery');
      expect(title.getAttribute('tabindex')).toBe('-1');
      expect(text('.bb-card-meta', c)).toContain('Modified 2026-09-02 14:05 UTC');
      expect(text('.bb-card-meta', c)).toContain('by admin');
      expect(text('.bb-card-meta .bb-hash', c)).toBe('abcdef01');
      expect(c.querySelector('.bb-card-meta time')?.getAttribute('datetime')).toBe('2026-09-02T14:05:00.000Z');

      const metric = (key: string) => text(`.bb-metric[data-metric="${key}"] dd`, c);
      expect(metric('suites')).toBe('2');
      expect(metric('questions')).toBe('18');
      expect(metric('runs')).toBe('2');
      expect(metric('ranked')).toBe('3');

      const segments = Array.from(c.querySelectorAll<HTMLElement>('.bb-weight-mix .bb-weight-segment'));
      expect(c.querySelector('.bb-weight-mix')?.getAttribute('aria-hidden')).toBe('true');
      expect(segments.map(s => s.style.flexGrow)).toEqual(['0.4', '0.6']);
      expect(segments.map(s => s.classList.contains('is-alt'))).toEqual([false, true]);

      expect(text('.bb-suite-table caption', c)).toBe('Suites of Core Battery in run order');
      const rows = c.querySelectorAll('.bb-suite-table tbody tr');
      expect(rows.length).toBe(2);
      expect(rows[1].textContent!.replace(/\s+/g, ' ')).toContain('Board Reading');
      expect(rows[1].textContent).toContain('60.0 %');
      expect(c.querySelector('.bb-suites-toggle')).toBeNull();
      expect(c.querySelectorAll('.bb-suite-table tbody').length).toBe(1);
    });

    it('shows "—" for Ranked results when an older server sends no count', () => {
      service.getBatteries.mockReturnValue(of([battery({ rankedResultCount: undefined })]));
      create();
      expect(text('.bb-metric[data-metric="ranked"] dd', card(3))).toBe('—');
    });

    it('shows four suite rows of seven, and Show all K suites reveals the rest with focus kept', () => {
      const names = ['S1', 'S2', 'S3', 'S4', 'S5', 'S6', 'S7'];
      service.getBatteries.mockReturnValue(of([battery({
        suites: names.map((name, i) => suite(i, name, i === 5 ? { deleted: true, questionCount: 0 } : {})),
        brokenSuiteNames: ['S6']
      }, [0.1, 0.1, 0.2, 0.2, 0.1, 0, 0.3])]));
      create();
      const c = card(3);

      const bodies = c.querySelectorAll<HTMLElement>('.bb-suite-table tbody');
      expect(bodies.length).toBe(2);
      expect(bodies[0].querySelectorAll('tr').length).toBe(4);
      expect(bodies[1].id).toBe('bb-suites-more-3');
      expect(bodies[1].hidden).toBe(true);
      expect(bodies[1].querySelectorAll('tr').length).toBe(3);

      const toggle = c.querySelector('.bb-suites-toggle') as HTMLButtonElement;
      expect(toggle.textContent!.trim()).toBe('Show all 7 suites');
      expect(toggle.getAttribute('aria-expanded')).toBe('false');
      expect(toggle.getAttribute('aria-controls')).toBe('bb-suites-more-3');

      toggle.focus();
      toggle.click();
      fixture.detectChanges();
      expect(bodies[1].hidden).toBe(false);
      expect(toggle.textContent!.trim()).toBe('Show fewer');
      expect(toggle.getAttribute('aria-expanded')).toBe('true');
      expect(document.activeElement).toBe(toggle);

      // The deleted suite: a hatched segment, a Deleted tag in its row and Broken in the kicker.
      const segments = Array.from(c.querySelectorAll<HTMLElement>('.bb-weight-segment'));
      expect(segments.length).toBe(7);
      expect(segments.map(s => s.classList.contains('is-deleted'))).toEqual([false, false, false, false, false, true, false]);
      expect(text('tr.is-deleted', c)).toContain('Deleted');
      expect(text('.bb-card-kicker', c)).toContain('Broken');
      expect(text('.bb-metric[data-metric="questions"] dd', c)).toBe('60');
    });

    it('names every state as a word in the kicker', () => {
      service.getBatteries.mockReturnValue(of([battery({
        isArchived: true,
        hasActiveBatteryRun: true,
        validationErrors: ['Suite S2 has been deleted.'],
        suites: [suite(0, 'Gameplay Help', { difficultyFullyAssessed: false }), suite(1, 'Board Reading')]
      })]));
      create();
      click('.bb-show-archived');
      const kicker = card(3).querySelector('.bb-card-kicker') as HTMLElement;
      expect(kicker.querySelector('.bb-tag-archived')?.textContent?.trim()).toBe('Archived');
      expect(kicker.querySelector('.bb-tag-active')?.textContent?.trim()).toBe('Running');
      expect(kicker.querySelector('.bb-tag-broken')?.textContent?.trim()).toBe('Broken');
      expect(kicker.querySelector('.bb-tag-difficulties')?.textContent?.trim()).toBe('Difficulties incomplete');
      expect(text('.bb-card-errors', card(3))).toContain('Suite S2 has been deleted.');
      expect(text('tbody tr', card(3))).toContain('Not assessed');
    });

    it('groups the actions with Leaderboard first', () => {
      create();
      const group = card(3).querySelector('.bb-card-actions') as HTMLElement;
      expect(group.getAttribute('role')).toBe('group');
      expect(group.getAttribute('aria-label')).toBe('Actions for battery Core Battery');
      const labels = Array.from(group.querySelectorAll('button')).map(b => b.textContent!.trim());
      expect(labels).toEqual(['Leaderboard', 'Edit', 'Archive', 'Delete']);
      expect(group.querySelector('.bb-leaderboard svg')?.getAttribute('aria-hidden')).toBe('true');
    });

    it('archives a battery and emits batteriesChanged', () => {
      create();
      const changed = vi.fn().mockName('changed');
      component.batteriesChanged.subscribe(changed);
      click('.bb-card .bb-archive');

      expect(service.archiveBattery).toHaveBeenCalledWith(3, true);
      expect(changed).toHaveBeenCalled();
      expect(el().querySelector('.bb-card')).toBeNull();
      expect(text('.bb-all-archived')).toContain('Every battery is archived');
    });

    it('offers Restore on an archived battery once Show archived is pressed', () => {
      service.getBatteries.mockReturnValue(of([battery(), battery({ id: 5, name: 'Old Battery', isArchived: true })]));
      create();
      const toggle = el().querySelector('.bb-show-archived') as HTMLButtonElement;
      expect(toggle.textContent!.trim()).toBe('Show archived (1)');
      expect(toggle.getAttribute('aria-pressed')).toBe('false');
      expect(el().querySelector('article[data-battery-id="5"]')).toBeNull();

      click('.bb-show-archived');
      expect(toggle.getAttribute('aria-pressed')).toBe('true');
      expect(text('.bb-archive', card(5))).toBe('Restore');
      expect(card(5).querySelector('.bb-archive')?.getAttribute('aria-label')).toBe('Restore battery Old Battery');

      card(5).querySelector<HTMLButtonElement>('.bb-archive')!.click();
      fixture.detectChanges();
      expect(service.archiveBattery).toHaveBeenCalledWith(5, false);
    });

    it('deletes a battery after confirmation and focuses the heading when no card is left', () => {
      create();
      const changed = vi.fn().mockName('changed');
      component.batteriesChanged.subscribe(changed);
      click('.bb-card .bb-delete');
      expect(text('#bbDeleteTitle')).toBe('Delete battery?');

      click('.bb-confirm-delete');
      expect(service.deleteBattery).toHaveBeenCalledWith(3);
      expect(changed).toHaveBeenCalled();
      expect(el().querySelector('.bb-card')).toBeNull();
      expect(document.activeElement?.id).toBe('bb-batteries-title');
    });

    it('keeps Delete aria-disabled and inert while a battery run is active', () => {
      service.getBatteries.mockReturnValue(of([battery({ hasActiveBatteryRun: true })]));
      create();
      const deleteButton = el().querySelector('.bb-card .bb-delete') as HTMLButtonElement;
      expect(deleteButton.getAttribute('aria-disabled')).toBe('true');
      expect(deleteButton.getAttribute('interestfor')).toBe('bb-tip-delete-3');
      deleteButton.click();
      fixture.detectChanges();
      expect((el().querySelector('.bb-delete-dialog') as HTMLDialogElement).open).toBe(false);
      expect(service.deleteBattery).not.toHaveBeenCalled();
    });
  });

  describe('the leaderboard', () => {
    it('opens the dialog with the battery\'s definition and returns focus to the button on close', () => {
      create();
      const dialog = fixture.debugElement.query(By.directive(BatteryLeaderboardDialogComponent)).componentInstance as BatteryLeaderboardDialogComponent;
      const open = vi.spyOn(dialog, 'open').mockImplementation(() => { });

      const button = card(3).querySelector('.bb-leaderboard') as HTMLButtonElement;
      expect(button.getAttribute('aria-label')).toBe('Leaderboard of battery Core Battery');
      button.click();
      expect(open).toHaveBeenCalledWith({
        definitionSha256: HASH,
        name: 'Core Battery',
        revision: 2,
        schemeLabel: 'Questions and difficulty',
        suiteCount: 2
      });

      (document.body as HTMLElement).focus();
      dialog.closed.emit();
      expect(document.activeElement).toBe(button);
    });
  });

  describe('the filter bar', () => {
    function useSearchClock(): void {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    }

    function search(value: string): void {
      const input = el().querySelector('#bb-search') as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(component.list.debounceMs);
      fixture.detectChanges();
    }

    function facet(label: string): FilterFacetComponent {
      const found = fixture.debugElement.queryAll(By.directive(FilterFacetComponent))
        .map(d => d.componentInstance as FilterFacetComponent)
        .find(f => f.label === label);
      expect(found, label).toBeTruthy();
      return found!;
    }

    it('is not rendered with three batteries or fewer', () => {
      service.getBatteries.mockReturnValue(of(fourBatteries().slice(0, 3)));
      create();
      expect(el().querySelector('.bb-filter-bar')).toBeNull();
      expect(el().querySelector('#bb-search')).toBeNull();
      expect(text('#bb-list-status')).toBe('Showing 3 of 3 batteries');
    });

    it('lists the cards most recently modified first, and searches name, description and suite names', () => {
      service.getBatteries.mockReturnValue(of(fourBatteries()));
      create();
      expect(el().querySelector('.bb-filter-bar')).not.toBeNull();
      expect(text('label[for="bb-search"]')).toBe('Search batteries');
      expect(shownTitles()).toEqual(['Bravo', 'Delta', 'Alpha', 'Charlie']);

      useSearchClock();
      search('monster');
      expect(shownTitles()).toEqual(['Delta']);
      expect(text('#bb-list-status')).toBe('One battery · filtered from 4');

      search('lore');
      expect(shownTitles()).toEqual(['Bravo', 'Delta', 'Charlie']);
      expect(text('.bb-filter-chips')).toContain('Search');
    });

    it('remembers the Sort by order', () => {
      service.getBatteries.mockReturnValue(of(fourBatteries()));
      create();
      const sort = el().querySelector('#bb-sort') as HTMLSelectElement;
      expect(Array.from(sort.options).map(o => o.text)).toEqual(['Recently modified', 'Name (A–Z)', 'Most runs', 'Most suites']);

      sort.value = 'runs';
      sort.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(shownTitles()).toEqual(['Delta', 'Alpha', 'Bravo', 'Charlie']);
      expect(JSON.parse(localStorage.getItem(BATTERY_LIST_VIEW_STORAGE_KEY)!)).toEqual({ version: 1, sort: 'runs' });

      fixture.destroy();
      create();
      expect((el().querySelector('#bb-sort') as HTMLSelectElement).value).toBe('runs');
      expect(shownTitles()).toEqual(['Delta', 'Alpha', 'Bravo', 'Charlie']);
    });

    it('filters by the Suite and Weighting facets, with a removable chip and Clear all', () => {
      service.getBatteries.mockReturnValue(of(fourBatteries()));
      create();
      expect(facet('Weighting').options.map(o => o.value)).toEqual(['Questions and difficulty', 'Questions only', 'Equal per suite']);
      expect(facet('Weighting').noun).toBe('batteries');

      facet('Weighting').selectedChange.emit(['Questions and difficulty']);
      fixture.detectChanges();
      expect(shownTitles()).toEqual(['Bravo', 'Delta']);

      facet('Suite').selectedChange.emit(['Combat']);
      fixture.detectChanges();
      expect(shownTitles()).toEqual(['Delta']);

      const chips = el().querySelectorAll<HTMLButtonElement>('.bb-filter-chips .gh-filter-chip');
      expect(Array.from(chips).map(c => c.getAttribute('aria-label'))).toEqual([
        'Remove filter Suite: Combat',
        'Remove filter Weighting: Questions and difficulty'
      ]);
      chips[0].click();
      fixture.detectChanges();
      expect(shownTitles()).toEqual(['Bravo', 'Delta']);
      // The chip that took the removed one's place.
      expect(document.activeElement?.getAttribute('aria-label')).toBe('Remove filter Weighting: Questions and difficulty');

      click('.bb-clear-filters');
      expect(shownTitles()).toEqual(['Bravo', 'Delta', 'Alpha', 'Charlie']);
      expect(document.activeElement?.id).toBe('bb-search');
    });

    it('shows ten cards, then Show more loads the rest and focuses the first new card', () => {
      const many = Array.from({ length: 12 }, (_, i) => battery({
        id: 100 + i,
        name: `Battery ${String(i + 1).padStart(2, '0')}`,
        modifiedAtUtc: `2026-09-${String(i + 1).padStart(2, '0')}T00:00:00Z`
      }));
      service.getBatteries.mockReturnValue(of(many));
      create();
      expect(el().querySelectorAll('.bb-card').length).toBe(10);
      expect(text('#bb-list-status')).toBe('Showing 10 of 12 batteries');
      expect(text('.bb-show-more')).toBe('Show 2 more');
      expect(el().querySelector('.bb-show-all')).toBeNull();

      click('.bb-show-more');
      expect(el().querySelectorAll('.bb-card').length).toBe(12);
      expect(document.activeElement?.id).toBe('bb-card-title-101');
      expect(el().querySelector('.bb-show-more')).toBeNull();
    });
  });

  describe('batteryWeightMix', () => {
    it('uses the declared weights, or equal shares when they are undefined', () => {
      const suites = [{ index: 0, deleted: false }, { index: 1, deleted: true }];
      expect(batteryWeightMix(suites, [0.25, 0.75]).map(s => s.share)).toEqual([0.25, 0.75]);
      expect(batteryWeightMix(suites, []).map(s => s.share)).toEqual([0.5, 0.5]);
      expect(batteryWeightMix(suites, []).map(s => s.deleted)).toEqual([false, true]);
    });
  });
});
