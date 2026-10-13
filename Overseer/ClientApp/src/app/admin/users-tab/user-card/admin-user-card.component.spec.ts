import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AdminUser } from '../../../services/admin-users.service';
import { buildAdminUser } from '../admin-users.testing';
import { AdminUserCardComponent } from './admin-user-card.component';
import {
  ADMIN_PROTECTED_REASON,
  AdminUserActionId,
  HOLD_FIRST_REASON,
  PENDING_REQUEST_REASON,
  accountActionGroups
} from './admin-user-card-actions';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

describe('AdminUserCardComponent', () => {
  let fixture: ComponentFixture<AdminUserCardComponent>;
  let component: AdminUserCardComponent;
  let el: HTMLElement;
  let actions: AdminUserActionId[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [AdminUserCardComponent] }).compileComponents();
    fixture = TestBed.createComponent(AdminUserCardComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    actions = [];
    component.action.subscribe(id => actions.push(id));
  });

  afterEach(() => fixture.destroy());

  function render(user: AdminUser = buildAdminUser(), changeNote: string | null = null): void {
    fixture.componentRef.setInput('user', user);
    fixture.componentRef.setInput('nowUtc', '2026-10-13T12:00:00Z');
    fixture.componentRef.setInput('changeNote', changeNote);
    fixture.detectChanges();
  }

  const item = (id: string): HTMLButtonElement => el.querySelector<HTMLButtonElement>(`.gh-action-popover-item[data-action="${id}"]`)!;

  it('shows the name, the email and the registration and last sign-in', () => {
    render(buildAdminUser({ createdUtc: '2026-10-14T00:00:00Z', createdUtcIsEstimate: true, lastSignInUtc: '2026-10-10T12:00:00Z' }));

    const card = el.querySelector('article.au-card')!;
    expect(card.getAttribute('aria-labelledby')).toBe('au-u-1-title');
    expect(textOf(el.querySelector('#au-u-1-title'))).toBe('alice');
    expect(el.querySelector('#au-u-1-title')!.getAttribute('tabindex')).toBe('-1');
    expect(textOf(el.querySelector('.au-card-email'))).toBe('alice@example.com');
    expect(textOf(el.querySelector('.au-registered'))).toBe('Registered on or before 2026-10-14');
    expect(textOf(el.querySelector('.au-last-sign-in'))).toBe('Last sign-in 3 days ago');
    expect(el.querySelector('.au-sep [aria-hidden="true"]')).toBeTruthy();
  });

  it('says when the dates are not recorded', () => {
    render(buildAdminUser({ createdUtc: null, lastSignInUtc: null }));

    expect(textOf(el.querySelector('.au-registered'))).toBe('Registration date not recorded');
    expect(textOf(el.querySelector('.au-last-sign-in'))).toBe('Last sign-in not recorded');
  });

  it('shows the badges and the facts', () => {
    render(buildAdminUser({
      statuses: ['LockedOut', 'EmailUnconfirmed'], emailConfirmed: false, accessFailedCount: 10,
      gameLogCount: 12, bonesCount: 3, chatSessionCount: 2, twoFactorEnabled: true,
      groups: [{ id: 1, displayName: 'Testers' }]
    }));

    expect(Array.from(el.querySelectorAll('.gh-account-badge')).map(b => textOf(b))).toEqual(['Locked out', 'Email not confirmed']);
    expect(textOf(el.querySelector('.au-fact-failed'))).toBe('10');
    expect(textOf(el.querySelector('.au-fact-2fa'))).toBe('On');
    expect(textOf(el.querySelector('.au-fact-groups'))).toBe('Testers');
    expect(textOf(el.querySelector('.au-fact-game'))).toBe('12 game logs · 3 bones');
    expect(textOf(el.querySelector('.au-fact-chats'))).toBe('2');
  });

  it('names the actions group, the icon buttons and the popover for the user', () => {
    render();

    expect(el.querySelector('.au-card-actions')!.getAttribute('role')).toBe('group');
    expect(el.querySelector('.au-card-actions')!.getAttribute('aria-label')).toBe('Actions for alice');
    expect(el.querySelector('.au-manage-groups')!.getAttribute('aria-label')).toBe('Manage the groups of alice');
    expect(el.querySelector('.au-manage-configs')!.getAttribute('aria-label')).toBe('Manage the AI configurations of alice');
    expect(el.querySelector('.au-security')!.getAttribute('aria-label')).toBe('Show the security events of alice');
    const trigger = el.querySelector('#au-u-1-actions-btn')!;
    expect(trigger.getAttribute('popovertarget')).toBe('au-u-1-actions');
    const popover = el.querySelector('#au-u-1-actions')!;
    expect(popover.getAttribute('role')).toBe('group');
    expect(popover.getAttribute('aria-label')).toBe('Account actions for alice');
    expect(Array.from(popover.querySelectorAll('.gh-action-popover-heading')).map(h => textOf(h))).toEqual(['Access', 'Deletion']);
  });

  it('offers the actions that fit the account state', () => {
    render();
    expect(Array.from(el.querySelectorAll('.gh-action-popover-item > span:first-child')).map(s => textOf(s))).toEqual([
      'Lock out…', 'Ban…', 'Game upload restrictions…', 'Block from AI chat…', 'Sign out everywhere',
      'Place deletion hold…', 'Delete account…'
    ]);
    expect(item('delete').classList.contains('is-danger')).toBe(true);

    render(buildAdminUser({
      statuses: ['LockedOut', 'Banned', 'AiChatBlocked', 'DeletionHold', 'EmailUnconfirmed'],
      isBanned: true, isOverseerBlocked: true, hasAccountHold: true, emailConfirmed: false, twoFactorEnabled: true
    }));
    expect(Array.from(el.querySelectorAll('.gh-action-popover-item > span:first-child')).map(s => textOf(s))).toEqual([
      'Unlock', 'Unban', 'Game upload restrictions…', 'Unblock from AI chat', 'Sign out everywhere',
      'Mark email as confirmed', 'Reset two-factor authentication…',
      'Release deletion hold', 'Delete account…'
    ]);
    expect(Array.from(el.querySelectorAll('.gh-action-popover-heading')).map(h => textOf(h))).toEqual(['Access', 'Account', 'Deletion']);
  });

  it('makes refused items aria-disabled with their reason on a second line', () => {
    render(buildAdminUser({ isSelf: true, isAdministrator: true, statuses: ['GoodStanding', 'Administrator'] }));
    expect(item('lockout').getAttribute('aria-disabled')).toBe('true');
    expect(textOf(item('lockout').querySelector('.gh-action-popover-item-reason'))).toBe('You cannot lock out your own account.');
    expect(item('signout').getAttribute('aria-disabled')).toBeNull();
    expect(item('restrictions').getAttribute('aria-disabled')).toBeNull();

    render(buildAdminUser({ isAdministrator: true, statuses: ['GoodStanding', 'Administrator'] }));
    expect(textOf(item('ban').querySelector('.gh-action-popover-item-reason'))).toBe(ADMIN_PROTECTED_REASON);
    expect(textOf(item('delete').querySelector('.gh-action-popover-item-reason'))).toBe(ADMIN_PROTECTED_REASON);

    render(buildAdminUser({ hasAccountHold: true, statuses: ['DeletionHold'] }));
    expect(textOf(item('delete').querySelector('.gh-action-popover-item-reason'))).toBe(HOLD_FIRST_REASON);

    render(buildAdminUser({ pendingDeletionRequestId: 7, statuses: ['DeletionPending'] }));
    expect(textOf(item('delete').querySelector('.gh-action-popover-item-reason'))).toBe(PENDING_REQUEST_REASON);
  });

  it('emits an available action and ignores a refused one', () => {
    render(buildAdminUser({ hasAccountHold: true, statuses: ['DeletionHold'] }));

    item('delete').click();
    expect(actions).toEqual([]);

    item('lockout').click();
    item('releaseHold').click();
    expect(actions).toEqual(['lockout', 'releaseHold']);
  });

  it('shows the change note with a Dismiss named for the user', () => {
    const dismissed = vi.fn();
    component.dismissNote.subscribe(dismissed);
    render(buildAdminUser(), 'In good standing → Locked out until 2026-10-20 15:00 UTC');

    expect(textOf(el.querySelector('.au-change-note .gh-change-note-text')))
      .toBe('Changed just now by you: In good standing → Locked out until 2026-10-20 15:00 UTC');
    const dismiss = el.querySelector<HTMLButtonElement>('.au-dismiss-note')!;
    expect(dismiss.getAttribute('aria-label')).toBe('Dismiss the change note of alice');
    dismiss.click();
    expect(dismissed).toHaveBeenCalledTimes(1);
  });

  it('emits the icon buttons', () => {
    const groups = vi.fn();
    const configs = vi.fn();
    const security = vi.fn();
    component.manageGroups.subscribe(groups);
    component.manageConfigs.subscribe(configs);
    component.securityEvents.subscribe(security);
    render();

    el.querySelector<HTMLButtonElement>('.au-manage-groups')!.click();
    el.querySelector<HTMLButtonElement>('.au-manage-configs')!.click();
    el.querySelector<HTMLButtonElement>('.au-security')!.click();

    expect([groups.mock.calls.length, configs.mock.calls.length, security.mock.calls.length]).toEqual([1, 1, 1]);
  });

  it('refuses sign-out everywhere on another administrator only', () => {
    const groups = accountActionGroups(buildAdminUser({ isAdministrator: true }));
    const signOut = groups[0].items.find(i => i.id === 'signout')!;
    expect(signOut.disabledReason).not.toBeNull();
  });
});
