import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { AdminUserCriteria, AdminUserSort } from '../../../services/admin-users.service';
import { FilterFacetComponent } from '../../../shared/data-table/filter-facet.component';
import { buildFacetCounts } from '../admin-users.testing';
import { USERS_VIEW_STORAGE_KEY, readStoredUserSort } from './user-criteria';
import { UserCriteriaBarComponent } from './user-criteria-bar.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function clearStorage(): void {
  try {
    localStorage.removeItem(USERS_VIEW_STORAGE_KEY);
  } catch {
    // Nothing stored.
  }
}

describe('UserCriteriaBarComponent', () => {
  let fixture: ComponentFixture<UserCriteriaBarComponent>;
  let component: UserCriteriaBarComponent;
  let el: HTMLElement;
  let emitted: AdminUserCriteria[];
  let sorts: AdminUserSort[];

  beforeEach(async () => {
    clearStorage();
    await TestBed.configureTestingModule({ imports: [UserCriteriaBarComponent] }).compileComponents();
    fixture = TestBed.createComponent(UserCriteriaBarComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    emitted = [];
    sorts = [];
    component.criteriaChange.subscribe(c => emitted.push(c));
    component.sortChange.subscribe(s => sorts.push(s));
  });

  afterEach(() => {
    vi.useRealTimers();
    fixture.destroy();
    clearStorage();
  });

  function render(criteria: AdminUserCriteria = {}, showSort = true): void {
    fixture.componentRef.setInput('idPrefix', 'au-criteria');
    fixture.componentRef.setInput('criteria', criteria);
    fixture.componentRef.setInput('showSort', showSort);
    fixture.componentRef.setInput('facetCounts', buildFacetCounts(100, {
      statuses: [{ key: 'LockedOut', count: 3 }],
      emailConfirmed: [{ key: 'yes', count: 90 }, { key: 'no', count: 10 }],
      registered: [{ key: 'withinDays:7', count: 4 }, { key: 'withinDays:30', count: 9 }, { key: 'olderThanDays:30', count: 91 }],
      lastSignIn: [{ key: 'never', count: 60 }],
      hasGameData: [{ key: 'yes', count: 20 }, { key: 'no', count: 80 }]
    }));
    fixture.detectChanges();
  }

  const facet = (column: string): FilterFacetComponent =>
    fixture.debugElement.queryAll(By.directive(FilterFacetComponent))
      .map(d => d.componentInstance as FilterFacetComponent)
      .find(f => f.facetId === `au-criteria-facet-${column}`)!;

  it('shows the search, Sort by and the five facets with the server counts', () => {
    render();

    expect(textOf(el.querySelector('label[for="au-criteria-search"]'))).toBe('Search users by user name or email');
    expect(el.querySelector<HTMLInputElement>('#au-criteria-search')!.type).toBe('search');
    expect(textOf(el.querySelector('label[for="au-criteria-sort"]'))).toBe('Sort by');
    expect(Array.from(el.querySelectorAll('#au-criteria-sort option')).map(o => textOf(o))).toEqual([
      'Newest first', 'Oldest first', 'User name (A–Z)', 'Email (A–Z)', 'Signed in recently', 'Longest without sign-in'
    ]);

    expect(['status', 'email', 'registered', 'signin', 'data'].map(c => facet(c).label))
      .toEqual(['Status', 'Email', 'Registered', 'Last sign-in', 'Data']);
    expect(facet('status').options.find(o => o.value === 'LockedOut')).toEqual({ value: 'LockedOut', label: 'Locked out', count: 3 });
    expect(facet('email').mode).toBe('single');
    expect(facet('email').options).toContainEqual({ value: 'no', label: 'Not confirmed', count: 10 });
    expect(facet('registered').options.map(o => [o.label, o.count])).toEqual([
      ['In the last 7 days', 4], ['7–30 days ago', 5], ['More than 30 days ago', 91], ['More than a year ago', 0]
    ]);
    expect(facet('signin').options.find(o => o.value === 'never')).toEqual({ value: 'never', label: 'Never recorded', count: 60 });
    expect(facet('data').options.find(o => o.value === 'game:no')).toEqual({ value: 'game:no', label: 'No game data', count: 80 });
    expect(facet('status').noun).toBe('users');
  });

  it('emits the criteria a facet choice makes', () => {
    render();

    facet('status').selectedChange.emit(['LockedOut', 'Banned']);
    expect(emitted.at(-1)).toEqual({ statuses: ['LockedOut', 'Banned'] });

    facet('email').selectedChange.emit(['no']);
    facet('registered').selectedChange.emit(['between:7:30']);
    facet('signin').selectedChange.emit(['olderThanDays:90']);
    expect(emitted.at(-1)).toEqual({
      statuses: ['LockedOut', 'Banned'], emailConfirmed: 'no',
      registeredOlderThanDays: 7, registeredWithinDays: 30, lastSignIn: 'olderThanDays:90'
    });
  });

  it('keeps one value per data pair, the one just chosen', () => {
    render();

    facet('data').selectedChange.emit(['game:yes']);
    fixture.detectChanges();
    facet('data').selectedChange.emit(['game:yes', 'game:no', 'overseer:no']);
    expect(emitted.at(-1)).toEqual({ hasGameData: 'no', hasOverseerData: 'no' });
  });

  it('shows the criteria it is given in the facets and the chips', () => {
    render({ statuses: ['Banned'], emailConfirmed: 'no', registeredOlderThanDays: 30, search: 'bob' });

    expect(facet('status').selected).toEqual(['Banned']);
    expect(facet('email').selected).toEqual(['no']);
    expect(facet('registered').selected).toEqual(['olderThanDays:30']);
    expect(el.querySelector<HTMLInputElement>('#au-criteria-search')!.value).toBe('bob');
    expect(Array.from(el.querySelectorAll('.ucb-chips .gh-filter-chip')).map(c => c.getAttribute('aria-label'))).toEqual([
      'Remove filter Status: Banned',
      'Remove filter Email: Not confirmed',
      'Remove filter Registered: More than 30 days ago',
      'Remove filter Search: “bob”'
    ]);
    expect(emitted).toEqual([]);
  });

  it('removes a chip and focuses the next one, and Clear all clears everything and focuses the search', () => {
    render({ statuses: ['Banned'], emailConfirmed: 'no' });

    el.querySelector<HTMLButtonElement>('.ucb-chips .gh-filter-chip')!.click();
    expect(emitted.at(-1)).toEqual({ emailConfirmed: 'no' });
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Remove filter Email: Not confirmed');

    el.querySelector<HTMLButtonElement>('.ucb-clear-all')!.click();
    expect(emitted.at(-1)).toEqual({});
    expect(document.activeElement?.id).toBe('au-criteria-search');
    expect(el.querySelector('.ucb-chips')).toBeNull();
  });

  it('applies the search once typing pauses', () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    render();
    const input = el.querySelector<HTMLInputElement>('#au-criteria-search')!;

    input.value = 'ali';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    expect(emitted).toEqual([]);
    vi.advanceTimersByTime(200);

    expect(emitted.at(-1)).toEqual({ search: 'ali' });
  });

  it('clears the search on Escape without letting the key reach a surrounding dialog', () => {
    render({ search: 'ali' });
    const input = el.querySelector<HTMLInputElement>('#au-criteria-search')!;
    const outer = vi.fn();
    el.addEventListener('keydown', outer);

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));

    expect(input.value).toBe('');
    expect(emitted.at(-1)).toEqual({});
    expect(outer).not.toHaveBeenCalled();
  });

  it('stores and emits a Sort by choice, and hides Sort by when asked', () => {
    render();
    const select = el.querySelector<HTMLSelectElement>('#au-criteria-sort')!;

    select.value = 'signin-oldest';
    select.dispatchEvent(new Event('change'));

    expect(sorts).toEqual(['signin-oldest']);
    expect(readStoredUserSort()).toBe('signin-oldest');

    fixture.componentRef.setInput('showSort', false);
    fixture.detectChanges();
    expect(el.querySelector('#au-criteria-sort')).toBeNull();
  });
});
