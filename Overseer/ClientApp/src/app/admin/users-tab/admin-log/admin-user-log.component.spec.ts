import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';

import { AdminUserLogEntry, AdminUserLogPage, AdminUserLogQuery, AdminUsersService } from '../../../services/admin-users.service';
import { FilterFacetComponent } from '../../../shared/data-table/filter-facet.component';
import { AdminPageStore } from '../../admin-page.store';
import { configureAdminTestBed } from '../../admin.component.testing';
import { ADMIN_LOG_BATCH, ADMIN_LOG_VIEW_STORAGE_KEY, AdminUserLogComponent } from './admin-user-log.component';

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function clearStorage(): void {
  try {
    localStorage.removeItem(ADMIN_LOG_VIEW_STORAGE_KEY);
  } catch {
    // Nothing stored.
  }
}

function logEntry(id: number, overrides: Partial<AdminUserLogEntry> = {}): AdminUserLogEntry {
  return {
    id,
    occurredUtc: '2026-10-13T09:15:00Z',
    actorUserId: 'admin-1',
    actorUserName: 'tommi',
    subjectUserId: `u-${id}`,
    subjectUserName: `user${id}`,
    kind: 'LockedOut',
    reason: null,
    detail: null,
    bulkOperationId: null,
    ...overrides
  };
}

function page(rows: AdminUserLogEntry[], totalCount = rows.length): AdminUserLogPage {
  return { rows, totalCount };
}

/** Entries with ids `from` down to `from - count + 1`, newest first. */
function entryRange(from: number, count: number): AdminUserLogEntry[] {
  return Array.from({ length: count }, (_, i) => logEntry(from - i));
}

describe('AdminUserLogComponent', () => {
  let fixture: ComponentFixture<AdminUserLogComponent>;
  let component: AdminUserLogComponent;
  let el: HTMLElement;
  let usersService: AdminUsersService;
  let store: AdminPageStore;
  let logSpy: Mock;
  /** What a list request (take 20) returns. */
  let listPage: AdminUserLogPage;
  /** What a count request (take 1, one kind) reports per kind. */
  let kindCounts: Record<string, number>;
  /** Whether this test created a fixture, which afterEach then destroys. */
  let created: boolean;

  beforeEach(async () => {
    created = false;
    clearStorage();
    ({ usersService, store } = await configureAdminTestBed(AdminUserLogComponent));
    listPage = page([logEntry(1)]);
    kindCounts = { LockedOut: 4, Banned: 2, Deleted: 1 };
    logSpy = vi.spyOn(usersService, 'log').mockImplementation((query: AdminUserLogQuery = {}) =>
      of(query.take === 1 ? page([], kindCounts[query.kinds?.[0] ?? ''] ?? 0) : listPage));
    vi.spyOn(store, 'requestBulkWizard').mockReturnValue(undefined);
  });

  afterEach(() => {
    if (created) fixture.destroy();
    clearStorage();
  });

  function create(): void {
    fixture = TestBed.createComponent(AdminUserLogComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    created = true;
    fixture.detectChanges();
  }

  /** The list requests, without the facet's count requests. */
  const listQueries = (): AdminUserLogQuery[] =>
    logSpy.mock.calls.map(call => (call[0] ?? {}) as AdminUserLogQuery).filter(query => query.take === ADMIN_LOG_BATCH);
  const lastListQuery = (): AdminUserLogQuery => listQueries()[listQueries().length - 1];
  const countQueries = (): AdminUserLogQuery[] =>
    logSpy.mock.calls.map(call => (call[0] ?? {}) as AdminUserLogQuery).filter(query => query.take === 1);
  const card = (id: number): HTMLElement => el.querySelector<HTMLElement>(`article.aul-card[data-entry-id="${id}"]`)!;
  const chipLabels = (): string[] =>
    Array.from(el.querySelectorAll('.aul-chips .gh-filter-chip')).map(chip => chip.getAttribute('aria-label') ?? '');
  const facet = (): FilterFacetComponent =>
    fixture.debugElement.query(By.directive(FilterFacetComponent)).componentInstance as FilterFacetComponent;

  function typeInto(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }

  describe('list', () => {
    it('fetches the first batch with no filter and shows each entry as a card', () => {
      listPage = page([
        logEntry(2, {
          kind: 'LockedOut', reason: 'Spam uploads',
          detail: 'Before: In good standing. After: Locked out until 2026-10-20 15:00 UTC.'
        }),
        logEntry(1, { kind: 'Unlocked', actorUserName: '', actorUserId: 'admin-9' })
      ]);
      create();

      expect(lastListQuery()).toEqual({ skip: 0, take: ADMIN_LOG_BATCH });
      const list = el.querySelector('ul.aul-cards')!;
      expect(list.getAttribute('role')).toBe('list');
      expect(list.getAttribute('aria-labelledby')).toBe('aul-title');
      expect(textOf(el.querySelector('#aul-status'))).toBe('Showing 2 of 2 actions');
      expect(el.querySelector('#aul-status')!.getAttribute('role')).toBe('status');
      const heading = el.querySelector('#aul-title')!;
      expect(textOf(heading)).toBe('Admin log');
      expect(heading.classList).toContain('visually-hidden');
      expect(heading.hasAttribute('tabindex')).toBe(false);
      expect(el.querySelector('.aul-filter-bar')!.compareDocumentPosition(el.querySelector('#aul-status')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

      const first = card(2);
      expect(first.getAttribute('aria-labelledby')).toBe('aul-2-title');
      const tag = first.querySelector('.aul-kind-tag')!;
      expect(textOf(tag)).toBe('Locked out');
      expect(tag.getAttribute('data-tone')).toBe('danger');
      expect(textOf(first.querySelector('.aul-time'))).toBe('2026-10-13 09:15 UTC');
      expect(first.querySelector('.aul-time')!.getAttribute('datetime')).toBe('2026-10-13T09:15:00Z');
      expect(textOf(first.querySelector('h5.aul-card-title'))).toBe('user2');
      expect(textOf(first.querySelector('.aul-actor'))).toBe('by tommi');
      expect(textOf(first.querySelector('.aul-reason'))).toBe('Reason: Spam uploads');
      const note = first.querySelector('.gh-change-note.aul-detail')!;
      expect(textOf(note)).toBe('In good standing → changed to Locked out until 2026-10-20 15:00 UTC');
      expect(first.querySelector('.aul-bulk')).toBeNull();

      expect(textOf(card(1).querySelector('.aul-kind-tag'))).toBe('Unlocked');
      expect(card(1).querySelector('.aul-kind-tag')!.getAttribute('data-tone')).toBe('ok');
      expect(textOf(card(1).querySelector('.aul-actor'))).toBe('by admin-9');
      expect(card(1).querySelector('.aul-reason')).toBeNull();
      expect(card(1).querySelector('.aul-detail')).toBeNull();
    });

    it('shows a deletion detail as written, a failed deletion as such, and the user name of a deleted account', () => {
      listPage = page([
        logEntry(2, { kind: 'Deleted', subjectUserName: 'gone', detail: 'Deletion failed: disk error' }),
        logEntry(1, { kind: 'Deleted', subjectUserName: 'gone', detail: 'Deleted gone. Game logs: 0, Bones: 0.' })
      ]);
      create();

      expect(textOf(card(2).querySelector('.aul-kind-tag'))).toBe('Deletion failed');
      expect(textOf(card(2).querySelector('.aul-detail-text'))).toBe('Deletion failed: disk error');
      expect(textOf(card(1).querySelector('.aul-kind-tag'))).toBe('Deleted');
      expect(textOf(card(1).querySelector('h5.aul-card-title'))).toBe('gone');
      expect(textOf(card(1).querySelector('.aul-detail-text'))).toBe('Deleted gone. Game logs: 0, Bones: 0.');
      expect(card(1).querySelector('.gh-change-note')).toBeNull();
    });

    it('names the bulk operation of a bulk row, and Open results asks for the wizard on its results', () => {
      listPage = page([logEntry(1, { bulkOperationId: 12 })]);
      create();

      expect(textOf(card(1).querySelector('.aul-bulk-text'))).toBe('Part of bulk operation #12');
      const open = card(1).querySelector<HTMLButtonElement>('.aul-open-results')!;
      expect(textOf(open)).toBe('Open results of bulk operation #12');
      expect(open.classList.contains('btn-link')).toBe(true);
      open.click();

      expect(store.requestBulkWizard).toHaveBeenCalledWith({ operationId: 12 });
    });

    it('appends the next page on Show more and focuses the first new card', () => {
      listPage = page(entryRange(100, 20), 25);
      create();

      expect(el.querySelectorAll('article.aul-card').length).toBe(20);
      expect(textOf(el.querySelector('#aul-status'))).toBe('Showing 20 of 25 actions');
      const more = el.querySelector<HTMLButtonElement>('.aul-show-more')!;
      expect(textOf(more)).toBe('Show 5 more');

      listPage = page(entryRange(80, 5), 25);
      more.click();
      fixture.detectChanges();

      expect(lastListQuery()).toEqual(expect.objectContaining({ skip: 20, take: ADMIN_LOG_BATCH }));
      expect(el.querySelectorAll('article.aul-card').length).toBe(25);
      expect(document.activeElement?.id).toBe('aul-80-title');
      expect(el.querySelector('.aul-show-more')).toBeNull();
    });

    it('tells an empty log from no entries matching, and Clear all filters refetches everything', () => {
      listPage = page([]);
      create();
      expect(textOf(el.querySelector('.aul-empty'))).toBe('No admin actions recorded yet.');
      expect(el.querySelector('.aul-cards')).toBeNull();
      expect(textOf(el.querySelector('#aul-status'))).toBe('');

      facet().selectedChange.emit(['Banned']);
      fixture.detectChanges();
      expect(textOf(el.querySelector('.aul-empty'))).toContain('No admin actions match these filters.');

      el.querySelector<HTMLButtonElement>('.aul-empty-clear')!.click();
      fixture.detectChanges();
      expect(lastListQuery()).toEqual({ skip: 0, take: ADMIN_LOG_BATCH });
      expect(document.activeElement?.id).toBe('aul-search');
    });

    it('shows the server message when the log cannot be loaded', () => {
      logSpy.mockImplementation(() => throwError(() => ({ status: 500, error: { message: 'Database unavailable.' } })));
      create();

      expect(textOf(el.querySelector('.aul-error'))).toBe('The admin log could not be loaded: Database unavailable.');
      expect(el.querySelector('.aul-cards')).toBeNull();
    });
  });

  describe('filters', () => {
    it('lists the actions with entries in the Action facet, with their counts', () => {
      create();

      expect(countQueries().length).toBe(17);
      expect(countQueries()).toContainEqual(expect.objectContaining({ kinds: ['Banned'], take: 1 }));
      const f = facet();
      expect(f.facetId).toBe('aul-facet-kind');
      expect(f.label).toBe('Action');
      expect(f.mode).toBe('multiple');
      expect(f.options).toEqual([
        { value: 'LockedOut', label: 'Locked out', count: 4 },
        { value: 'Banned', label: 'Banned', count: 2 },
        { value: 'Deleted', label: 'Deleted', count: 1 }
      ]);
    });

    it('refetches page 1 for the Action facet, shows its chip, and keeps the counts', () => {
      create();
      const countsBefore = countQueries().length;

      facet().selectedChange.emit(['Banned', 'Deleted']);
      fixture.detectChanges();

      expect(lastListQuery()).toEqual(expect.objectContaining({ kinds: ['Banned', 'Deleted'], skip: 0, take: ADMIN_LOG_BATCH }));
      expect(countQueries().length).toBe(countsBefore);
      expect(chipLabels()).toEqual(['Remove filter Action: Banned', 'Remove filter Action: Deleted']);
      expect(store.usersLogFilter).toEqual({ kinds: ['Banned', 'Deleted'] });

      el.querySelector<HTMLButtonElement>('.aul-chips .gh-filter-chip')!.click();
      fixture.detectChanges();
      expect(lastListQuery().kinds).toEqual(['Deleted']);
      expect(document.activeElement?.getAttribute('aria-label')).toBe('Remove filter Action: Deleted');
    });

    it('refetches page 1 and recounts for the search once typing pauses, and Escape clears it at once', async () => {
      create();
      logSpy.mockClear();

      typeInto(el.querySelector<HTMLInputElement>('#aul-search')!, 'bob');
      expect(logSpy).not.toHaveBeenCalled();

      await settle(300);
      fixture.detectChanges();
      expect(lastListQuery()).toEqual(expect.objectContaining({ search: 'bob', skip: 0 }));
      expect(countQueries()).toContainEqual(expect.objectContaining({ search: 'bob', kinds: ['LockedOut'], take: 1 }));
      expect(chipLabels()).toContain('Remove filter Search: “bob”');

      const input = el.querySelector<HTMLInputElement>('#aul-search')!;
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      fixture.detectChanges();
      expect(input.value).toBe('');
      expect(lastListQuery().search).toBeUndefined();
    });

    it('opens with the filter a link preset in the page store, the operation and account chips first', () => {
      store.usersLogFilter = { bulkOperationId: 12, subjectUserId: 'u-2', subjectUserName: 'bob', kinds: ['Deleted'], search: 'spam' };
      create();

      expect(lastListQuery()).toEqual({
        search: 'spam', kinds: ['Deleted'], subjectUserId: 'u-2', bulkOperationId: 12, skip: 0, take: ADMIN_LOG_BATCH
      });
      expect(countQueries()).toContainEqual(expect.objectContaining({ search: 'spam', subjectUserId: 'u-2', bulkOperationId: 12, kinds: ['Banned'] }));
      expect(el.querySelector<HTMLInputElement>('#aul-search')!.value).toBe('spam');
      expect(chipLabels()).toEqual([
        'Remove filter Operation: #12',
        'Remove filter Account: bob',
        'Remove filter Action: Deleted',
        'Remove filter Search: “spam”'
      ]);
      expect(component.filter).toEqual({ search: 'spam', kinds: ['Deleted'], subjectUserId: 'u-2', subjectUserName: 'bob', bulkOperationId: 12 });
    });

    it('applies a filter the page store sets while the log is on screen', () => {
      create();

      store.showUsersLog({ bulkOperationId: 7 });
      fixture.detectChanges();

      expect(lastListQuery()).toEqual({ bulkOperationId: 7, skip: 0, take: ADMIN_LOG_BATCH });
      expect(component.filter).toEqual({ bulkOperationId: 7 });
      expect(chipLabels()).toEqual(['Remove filter Operation: #7']);
    });

    it('removing the operation chip refetches the whole log, keeps the store current and focuses the next chip', () => {
      store.usersLogFilter = { bulkOperationId: 12, subjectUserId: 'u-2', subjectUserName: 'bob' };
      create();

      el.querySelector<HTMLButtonElement>('.aul-chips .gh-filter-chip')!.click();
      fixture.detectChanges();

      expect(lastListQuery()).toEqual({ subjectUserId: 'u-2', skip: 0, take: ADMIN_LOG_BATCH });
      expect(store.usersLogFilter).toEqual({ subjectUserId: 'u-2', subjectUserName: 'bob' });
      expect(document.activeElement?.getAttribute('aria-label')).toBe('Remove filter Account: bob');

      (document.activeElement as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(lastListQuery()).toEqual({ skip: 0, take: ADMIN_LOG_BATCH });
      expect(chipLabels()).toEqual([]);
      expect(document.activeElement?.id).toBe('aul-search');
    });

    it('Clear all drops every filter, the preset ones included, and focuses the search', () => {
      store.usersLogFilter = { bulkOperationId: 12, kinds: ['Banned'] };
      create();

      el.querySelector<HTMLButtonElement>('.aul-clear-filters')!.click();
      fixture.detectChanges();

      expect(lastListQuery()).toEqual({ skip: 0, take: ADMIN_LOG_BATCH });
      expect(store.usersLogFilter).toEqual({});
      expect(el.querySelector('.aul-chips')).toBeNull();
      expect(document.activeElement?.id).toBe('aul-search');
    });

    it('ignores a kind the client does not know', () => {
      store.usersLogFilter = { kinds: ['Banned', 'Exploded' as never] };
      create();

      expect(lastListQuery().kinds).toEqual(['Banned']);
    });
  });
});
