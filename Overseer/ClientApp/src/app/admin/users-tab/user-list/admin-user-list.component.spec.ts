import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { AdminService } from '../../../services/admin.service';
import { AdminUser, AdminUsersService } from '../../../services/admin-users.service';
import { AdminPageStore } from '../../admin-page.store';
import { configureAdminTestBed } from '../../admin.component.testing';
import { buildActionResult, buildAdminUser, buildFacetCounts, buildUsersPage } from '../admin-users.testing';
import { UserLockoutDialogComponent } from '../user-action-dialogs/user-lockout-dialog.component';
import { UserCriteriaBarComponent } from '../user-criteria/user-criteria-bar.component';
import { USERS_VIEW_STORAGE_KEY } from '../user-criteria/user-criteria';
import { AdminUserListComponent, USERS_BATCH } from './admin-user-list.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function clearStorage(): void {
  try {
    localStorage.removeItem(USERS_VIEW_STORAGE_KEY);
  } catch {
    // Nothing stored.
  }
}

/** Accounts u-{from} to u-{from + count - 1}, named user{n} (u-1 is alice). */
function userRange(from: number, count: number): AdminUser[] {
  return Array.from({ length: count }, (_, i) => {
    const n = from + i;
    return buildAdminUser({ id: `u-${n}`, userName: n === 1 ? 'alice' : `user${n}`, email: `user${n}@example.com` });
  });
}

describe('AdminUserListComponent', () => {
  let fixture: ComponentFixture<AdminUserListComponent>;
  let component: AdminUserListComponent;
  let el: HTMLElement;
  let usersService: AdminUsersService;
  let adminService: AdminService;
  let store: AdminPageStore;
  let listSpy: Mock;

  beforeEach(async () => {
    clearStorage();
    ({ usersService, adminService, store } = await configureAdminTestBed(AdminUserListComponent));
    listSpy = usersService.list as Mock;
    listSpy.mockReturnValue(of(buildUsersPage(userRange(1, 2))));
    (usersService.facets as Mock).mockReturnValue(of(buildFacetCounts(9102)));
    vi.spyOn(store, 'showToast').mockReturnValue(undefined);
  });

  afterEach(() => {
    if (fixture) {
      el.querySelectorAll('dialog').forEach(dialog => {
        if (dialog.open) dialog.close();
      });
      fixture.destroy();
    }
    clearStorage();
  });

  function create(): void {
    fixture = TestBed.createComponent(AdminUserListComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  }

  const card = (id: string): HTMLElement => el.querySelector<HTMLElement>(`article.au-card[data-user-id="${id}"]`)!;
  const actionItem = (id: string, action: string): HTMLButtonElement =>
    card(id).querySelector<HTMLButtonElement>(`.gh-action-popover-item[data-action="${action}"]`)!;
  const confirmDialog = (): HTMLDialogElement => el.querySelector<HTMLDialogElement>(':scope > app-admin-confirm-dialog dialog')!;
  const confirmButton = (label: string): HTMLButtonElement =>
    Array.from(confirmDialog().querySelectorAll('button')).find(b => textOf(b) === label) as HTMLButtonElement;

  describe('list', () => {
    it('fetches the first batch with the stored criteria and sort, and counts the facets', () => {
      store.usersQuery = { criteria: { statuses: ['LockedOut'] }, sort: 'oldest' };
      create();

      expect(listSpy).toHaveBeenCalledWith({ statuses: ['LockedOut'] }, 'oldest', 0, USERS_BATCH);
      expect(usersService.facets).toHaveBeenCalledWith(undefined);
      const list = el.querySelector('ul.au-cards')!;
      expect(list.getAttribute('role')).toBe('list');
      expect(list.getAttribute('aria-labelledby')).toBe('au-title');
      const heading = el.querySelector('#au-title')!;
      expect(textOf(heading)).toBe('Users');
      expect(heading.classList).toContain('visually-hidden');
      expect(heading.hasAttribute('tabindex')).toBe(false);
      expect(list.querySelectorAll(':scope > li > app-admin-user-card').length).toBe(2);
      expect(textOf(el.querySelector('#au-status'))).toBe('Showing 2 of 2 users · filtered from 9,102');
      expect(el.querySelector('#au-status')!.getAttribute('role')).toBe('status');
    });

    it('refetches with the criteria the bar emits and keeps them in the store', () => {
      create();
      listSpy.mockClear();

      const bar = fixture.debugElement.query(By.directive(UserCriteriaBarComponent)).componentInstance as UserCriteriaBarComponent;
      bar.criteriaChange.emit({ search: 'bob', emailConfirmed: 'no' });
      fixture.detectChanges();

      expect(store.usersQuery.criteria).toEqual({ search: 'bob', emailConfirmed: 'no' });
      expect(listSpy).toHaveBeenCalledWith({ search: 'bob', emailConfirmed: 'no' }, 'newest', 0, USERS_BATCH);
      expect(usersService.facets).toHaveBeenLastCalledWith('bob');

      bar.sortChange.emit('name');
      expect(store.usersQuery.sort).toBe('name');
      expect(listSpy).toHaveBeenLastCalledWith({ search: 'bob', emailConfirmed: 'no' }, 'name', 0, USERS_BATCH);
    });

    it('appends the next batch on Show more and focuses the first new card', () => {
      listSpy.mockReturnValue(of(buildUsersPage(userRange(1, 20), 25)));
      create();

      expect(textOf(el.querySelector('.au-show-more'))).toBe('Show 5 more');
      listSpy.mockReturnValue(of(buildUsersPage(userRange(21, 5), 25)));
      el.querySelector<HTMLButtonElement>('.au-show-more')!.click();
      fixture.detectChanges();

      expect(listSpy).toHaveBeenLastCalledWith({}, 'newest', 20, USERS_BATCH);
      expect(el.querySelectorAll('article.au-card').length).toBe(25);
      expect(document.activeElement?.id).toBe('au-u-21-title');
      expect(el.querySelector('.au-show-more')).toBeNull();
    });

    it('tells no users yet from no users matching, and Clear all filters refetches everything', () => {
      listSpy.mockReturnValue(of(buildUsersPage([])));
      create();
      expect(textOf(el.querySelector('.au-empty'))).toBe('No users yet.');

      store.usersQuery.criteria = { statuses: ['Banned'] };
      component.reload();
      fixture.detectChanges();
      expect(textOf(el.querySelector('.au-empty'))).toContain('No users match these filters.');

      listSpy.mockClear();
      el.querySelector<HTMLButtonElement>('.au-clear-filters')!.click();
      fixture.detectChanges();
      expect(store.usersQuery.criteria).toEqual({});
      expect(listSpy).toHaveBeenCalledWith({}, 'newest', 0, USERS_BATCH);
      expect(document.activeElement?.id).toBe('au-criteria-search');
    });

    it('shows a load error inline', () => {
      listSpy.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { message: 'lastSignIn must be never.', code: 'criteria' } })));
      create();

      expect(textOf(el.querySelector('.au-error'))).toBe('The users could not be loaded: lastSignIn must be never.');
    });
  });

  describe('actions', () => {
    it('Unlock asks for confirmation, then shows the change, announces it and returns focus to Actions', () => {
      const locked = buildAdminUser({ statuses: ['LockedOut'], lockoutEnd: '2026-10-20T15:00:00Z' });
      listSpy.mockReturnValue(of(buildUsersPage([locked])));
      const unlocked = buildAdminUser();
      const unlock = vi.spyOn(usersService, 'unlock').mockReturnValue(of(buildActionResult(unlocked,
        ['LockedOut'], 'Locked out until 2026-10-20 15:00 UTC', ['GoodStanding'], 'In good standing', 'alice can sign in again.')));
      create();

      actionItem('u-1', 'unlock').click();
      fixture.detectChanges();
      expect(confirmDialog().open).toBe(true);
      expect(confirmDialog().textContent).toContain('Failed sign-ins are reset.');
      expect(confirmDialog().textContent).toContain('is separate: clear it with Clear game sign-in block.');
      expect(unlock).not.toHaveBeenCalled();

      confirmButton('Unlock').click();
      fixture.detectChanges();

      expect(unlock).toHaveBeenCalledWith('u-1');
      expect(textOf(card('u-1').querySelector('.gh-change-note-text')))
        .toBe('Changed just now by you: Locked out until 2026-10-20 15:00 UTC → In good standing');
      expect(Array.from(card('u-1').querySelectorAll('.gh-account-badge')).map(b => textOf(b))).toEqual(['In good standing']);
      expect(card('u-1').querySelector('.gh-account-badge')!.classList.contains('is-new')).toBe(true);
      expect(store.showToast).toHaveBeenCalledWith('alice can sign in again.', 'success', 'Account Updated');
      expect(document.activeElement?.id).toBe('au-u-1-actions-btn');
    });

    it('Clear game sign-in block asks for confirmation, then clears it and shows the change', () => {
      const blocked = buildAdminUser({
        statuses: ['GameSignInBlocked'],
        gameSignInBlocks: [{ source: 'Website', kind: 'Pair', ipAddress: '203.0.113.7', blockedUntilUtc: '2026-10-27T19:20:00Z' }]
      });
      listSpy.mockReturnValue(of(buildUsersPage([blocked])));
      const clear = vi.spyOn(usersService, 'clearGameSignInBlock').mockReturnValue(of(buildActionResult(buildAdminUser(),
        ['GameSignInBlocked'], 'Game sign-in blocked until 2026-10-27 19:20 UTC', ['GoodStanding'], 'In good standing',
        'Game sign-in block cleared. GnollHack can sign in again within 15 seconds.')));
      create();

      actionItem('u-1', 'clearGameSignInBlock').click();
      fixture.detectChanges();
      expect(confirmDialog().open).toBe(true);
      expect(confirmDialog().textContent).toContain('Clear the game sign-in block of alice');
      expect(confirmDialog().textContent).toContain(
        'GnollHack can sign in as alice again within 15 seconds. If the stored password is still wrong, the block returns after 10 more failures.');
      expect(clear).not.toHaveBeenCalled();

      confirmButton('Clear Block').click();
      fixture.detectChanges();

      expect(clear).toHaveBeenCalledWith('u-1');
      expect(textOf(card('u-1').querySelector('.gh-change-note-text')))
        .toBe('Changed just now by you: Game sign-in blocked until 2026-10-27 19:20 UTC → In good standing');
      expect(Array.from(card('u-1').querySelectorAll('.gh-account-badge')).map(b => textOf(b))).toEqual(['In good standing']);
    });

    it('Block asks for a reason of at most 256 characters, shown to the user', () => {
      const block = vi.spyOn(usersService, 'blockFromAiChat').mockReturnValue(of(buildActionResult(
        buildAdminUser({ statuses: ['AiChatBlocked'], isOverseerBlocked: true }), ['GoodStanding'], 'In good standing', ['AiChatBlocked'], 'Blocked from AI chat')));
      create();

      actionItem('u-1', 'block').click();
      fixture.detectChanges();
      const dialog = el.querySelector<HTMLDialogElement>(':scope > app-admin-reason-dialog dialog')!;
      expect(dialog.open).toBe(true);
      expect(dialog.querySelector('textarea')!.getAttribute('maxlength')).toBe('256');
      const field = dialog.querySelector('textarea')!;
      field.value = ' Abusive prompts ';
      field.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      dialog.querySelector<HTMLButtonElement>('.reason-confirm')!.click();
      fixture.detectChanges();

      expect(block).toHaveBeenCalledWith('u-1', 'Abusive prompts');
      expect(textOf(card('u-1').querySelector('.gh-change-note-text'))).toContain('In good standing → Blocked from AI chat');
    });

    it('shows a refused action as an error toast', () => {
      vi.spyOn(usersService, 'signOutEverywhere').mockReturnValue(throwError(() => new HttpErrorResponse({
        status: 409, error: { message: 'Administrators cannot be signed out here.', code: 'administrator' }
      })));
      create();

      actionItem('u-1', 'signout').click();
      fixture.detectChanges();
      confirmButton('Sign Out Everywhere').click();
      fixture.detectChanges();

      expect(store.showToast).toHaveBeenCalledWith('Administrators cannot be signed out here.', 'error', 'Error');
      expect(card('u-1').querySelector('.gh-change-note')).toBeNull();
    });

    it('opens the lockout dialog and applies what it hands back', () => {
      create();

      actionItem('u-2', 'lockout').click();
      fixture.detectChanges();
      const lockout = fixture.debugElement.query(By.directive(UserLockoutDialogComponent)).componentInstance as UserLockoutDialogComponent;
      expect(lockout.user?.id).toBe('u-2');

      const user = component.rows[1];
      lockout.close();
      lockout.done.emit({
        user,
        result: buildActionResult({ ...user, statuses: ['LockedOut'] }, ['GoodStanding'], 'In good standing', ['LockedOut'], 'Locked out until 2026-10-20 15:00 UTC', 'user2 is locked out.')
      });
      fixture.detectChanges();

      expect(textOf(card('u-2').querySelector('.gh-change-note-text'))).toContain('In good standing → Locked out until 2026-10-20 15:00 UTC');
      expect(document.activeElement?.id).toBe('au-u-2-actions-btn');
    });

    it('turns a deleted account into a tombstone, focuses the next card, and Dismiss removes it', () => {
      listSpy.mockReturnValue(of(buildUsersPage(userRange(1, 3))));
      create();
      const user = component.rows[1];

      component.onDialogDone({ user, result: buildActionResult(null, ['GoodStanding'], 'In good standing', [], 'Deleted', 'user2 was deleted.'), erasedText: 'erased 0 game logs, 2 chats' });
      fixture.detectChanges();

      expect(card('u-2')).toBeNull();
      const tombstone = el.querySelector<HTMLElement>('.au-tombstone[data-user-id="u-2"]')!;
      expect(textOf(tombstone.querySelector('.au-tombstone-title'))).toBe('Deleted · user2');
      expect(textOf(tombstone.querySelector('.au-tombstone-text'))).toBe('— erased 0 game logs, 2 chats');
      expect(document.activeElement?.id).toBe('au-u-3-title');
      expect(store.showToast).toHaveBeenCalledWith('user2 was deleted.', 'success', 'Account Deleted');
      expect(textOf(el.querySelector('#au-status'))).toBe('Showing 2 of 2 users');

      const dismiss = tombstone.querySelector<HTMLButtonElement>('.au-dismiss-tombstone')!;
      expect(dismiss.getAttribute('aria-label')).toBe('Dismiss the deleted account user2');
      dismiss.click();
      fixture.detectChanges();
      expect(el.querySelector('.au-tombstone')).toBeNull();
      expect(document.activeElement?.id).toBe('au-u-3-title');
    });

    it('focuses the list section when a delete leaves no live card', () => {
      listSpy.mockReturnValue(of(buildUsersPage(userRange(1, 1))));
      create();
      const user = component.rows[0];

      component.onDialogDone({ user, result: buildActionResult(null, ['GoodStanding'], 'In good standing', [], 'Deleted', 'user1 was deleted.'), erasedText: 'erased 0 game logs, 2 chats' });
      fixture.detectChanges();

      expect(document.activeElement?.id).toBe('au-section');
      expect(el.querySelector('#au-title')!.classList).toContain('visually-hidden');
    });

    it('Security events asks the Security tab for the user', () => {
      const requestSecurityUser = vi.spyOn(store, 'requestSecurityUser').mockReturnValue(undefined);
      create();

      card('u-1').querySelector<HTMLButtonElement>('.au-security')!.click();

      expect(requestSecurityUser).toHaveBeenCalledWith('u-1', 'alice');
    });
  });

  describe('manage dialogs', () => {
    it('opens Manage Groups for a card and redraws its groups after a change', () => {
      store.groups = [{ id: 5, displayName: 'Testers' }];
      vi.spyOn(adminService, 'addUserToGroup').mockReturnValue(of(undefined));
      create();

      card('u-1').querySelector<HTMLButtonElement>('.au-manage-groups')!.click();
      fixture.detectChanges();
      const dialog = el.querySelector<HTMLDialogElement>('dialog:has(#group_5)')!;
      expect(dialog.open).toBe(true);
      dialog.querySelector<HTMLInputElement>('#group_5')!.click();
      fixture.detectChanges();

      expect(adminService.addUserToGroup).toHaveBeenCalledWith('u-1', 5);
      expect(textOf(card('u-1').querySelector('.au-fact-groups'))).toBe('Testers');
    });

    it('opens the AI configuration access dialog for a request from AI Telemetry without looking the user up', () => {
      const configsSpy = vi.spyOn(adminService, 'getUserSystemConfigs').mockReturnValue(of([]));
      store.pendingManageUser = { userId: 'u-9', userName: 'zed' };
      create();

      expect(store.pendingManageUser).toBeNull();
      expect(configsSpy).toHaveBeenCalledWith('u-9');
      const dialog = el.querySelector<HTMLDialogElement>('app-config-access-dialog dialog')!;
      expect(dialog.open).toBe(true);
      expect(textOf(dialog.querySelector('h3'))).toBe('AI configuration access for zed');
    });

    it('opens the AI configuration access dialog from a card', () => {
      const configsSpy = vi.spyOn(adminService, 'getUserSystemConfigs').mockReturnValue(of([]));
      create();

      card('u-1').querySelector<HTMLButtonElement>('.au-manage-configs')!.click();
      fixture.detectChanges();

      expect(configsSpy).toHaveBeenCalledWith('u-1');
      const dialog = el.querySelector<HTMLDialogElement>('app-config-access-dialog dialog')!;
      expect(dialog.open).toBe(true);
      expect(textOf(dialog.querySelector('h3'))).toBe('AI configuration access for alice');
    });
  });
});
