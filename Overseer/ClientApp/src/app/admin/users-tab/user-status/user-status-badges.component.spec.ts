import { ComponentFixture, TestBed } from '@angular/core/testing';

import { buildAdminUser } from '../admin-users.testing';
import { UserStatusBadgesComponent } from './user-status-badges.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

describe('UserStatusBadgesComponent', () => {
  let fixture: ComponentFixture<UserStatusBadgesComponent>;
  let el: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [UserStatusBadgesComponent] }).compileComponents();
    fixture = TestBed.createComponent(UserStatusBadgesComponent);
    el = fixture.nativeElement as HTMLElement;
  });

  function render(user = buildAdminUser(), highlight: string[] = []): void {
    fixture.componentRef.setInput('user', user);
    fixture.componentRef.setInput('idPrefix', 'au-u-1');
    fixture.componentRef.setInput('highlight', highlight);
    fixture.detectChanges();
  }

  it('lists the badges in a list named for the user', () => {
    render();

    const list = el.querySelector('ul.gh-account-badges')!;
    expect(list.getAttribute('role')).toBe('list');
    expect(list.getAttribute('aria-label')).toBe('Status of alice');
    const badge = list.querySelector('.gh-account-badge')!;
    expect(textOf(badge)).toBe('In good standing');
    expect(badge.getAttribute('data-tone')).toBe('ok');
    expect(badge.tagName).toBe('SPAN');
    expect(badge.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
  });

  it('renders a badge with details as a focusable button with a hint tooltip', () => {
    render(buildAdminUser({
      statuses: ['LockedOut', 'EmailUnconfirmed'], emailConfirmed: false,
      lockoutEnd: '2026-10-20T15:00:00Z', lockoutIsAdminSet: true, adminLockoutReason: 'Spam'
    }));

    const badges = Array.from(el.querySelectorAll<HTMLElement>('.gh-account-badge'));
    expect(badges.map(b => textOf(b))).toEqual(['Locked out', 'Email not confirmed']);
    expect(badges[0].tagName).toBe('BUTTON');
    expect(badges[0].getAttribute('type')).toBe('button');
    expect(badges[0].getAttribute('data-tone')).toBe('danger');
    expect(badges[0].getAttribute('interestfor')).toBe('au-u-1-badge-LockedOut-tip');
    expect(textOf(el.querySelector('#au-u-1-badge-LockedOut-tip'))).toBe('Locked out by an administrator until 2026-10-20 15:00 UTC. Reason: Spam');
    expect(badges[1].tagName).toBe('SPAN');
  });

  it('highlights the badges an action just added', () => {
    render(buildAdminUser({ statuses: ['Banned'], isBanned: true }), ['Banned']);

    expect(el.querySelector('.gh-account-badge')!.classList.contains('is-new')).toBe(true);
  });
});
