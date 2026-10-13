import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { AdminUsersService } from '../../../services/admin-users.service';
import { DateFieldComponent } from '../../../shared/date-field/date-field.component';
import { AdminPageStore } from '../../admin-page.store';
import { configureAdminTestBed } from '../../admin.component.testing';
import { buildActionResult, buildAdminUser } from '../admin-users.testing';
import { UserActionDone } from './user-action-dialogs';
import { UserLockoutDialogComponent } from './user-lockout-dialog.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

describe('UserLockoutDialogComponent', () => {
  let fixture: ComponentFixture<UserLockoutDialogComponent>;
  let component: UserLockoutDialogComponent;
  let el: HTMLElement;
  let usersService: AdminUsersService;
  let store: AdminPageStore;
  let done: UserActionDone[];
  const user = buildAdminUser();

  beforeEach(async () => {
    ({ usersService, store } = await configureAdminTestBed(UserLockoutDialogComponent));
    vi.spyOn(store, 'showToast').mockReturnValue(undefined);
    fixture = TestBed.createComponent(UserLockoutDialogComponent);
    component = fixture.componentInstance;
    component.now = () => new Date('2026-10-13T12:00:00Z');
    el = fixture.nativeElement as HTMLElement;
    done = [];
    component.done.subscribe(d => done.push(d));
    fixture.detectChanges();
  });

  afterEach(() => {
    const dialog = el.querySelector('dialog');
    if (dialog?.open) dialog.close();
    fixture.destroy();
  });

  const dialog = () => el.querySelector<HTMLDialogElement>('dialog.uad-lockout-dialog')!;
  const confirm = () => el.querySelector<HTMLButtonElement>('.uad-confirm')!;

  function fill(reason: string, check = true): void {
    const field = el.querySelector<HTMLTextAreaElement>('#uad-lockout-reason')!;
    field.value = reason;
    field.dispatchEvent(new Event('input', { bubbles: true }));
    if (check) el.querySelector<HTMLInputElement>('#uad-lockout-check')!.click();
    fixture.detectChanges();
  }

  it('opens on its heading with the durations, the effect and the account check', () => {
    component.open(user);
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(document.activeElement?.id).toBe('uad-lockout-title');
    expect(textOf(el.querySelector('#uad-lockout-title'))).toBe('Lock out alice');
    expect(Array.from(el.querySelectorAll('.uad-durations .gh-radio')).map(l => textOf(l)))
      .toEqual(['1 hour', '24 hours', '7 days', '30 days', 'Until a date…']);
    expect(textOf(el.querySelector('#uad-lockout-effect'))).toBe(
      'Cannot sign in to the website, Overseer or GnollHack online features until 2026-10-14 12:00 UTC. Signed-in sessions end within 5 minutes.');
    expect(textOf(el.querySelector('.uad-check'))).toBe('I have checked this is the right account (alice@example.com).');
  });

  it('keeps Lock Out aria-disabled, with what is missing, until the reason and the check are in', () => {
    const lockOut = vi.spyOn(usersService, 'lockOut');
    component.open(user);
    fixture.detectChanges();

    expect(confirm().getAttribute('aria-disabled')).toBe('true');
    expect(confirm().getAttribute('aria-describedby')).toBe('uad-lockout-note');
    expect(textOf(el.querySelector('#uad-lockout-note'))).toBe('Still needed: a reason and the account check.');
    confirm().click();
    expect(lockOut).not.toHaveBeenCalled();

    fill('Spam', false);
    expect(textOf(el.querySelector('#uad-lockout-note'))).toBe('Still needed: the account check.');
    expect(confirm().getAttribute('aria-disabled')).toBe('true');

    el.querySelector<HTMLInputElement>('#uad-lockout-check')!.click();
    fixture.detectChanges();
    expect(confirm().getAttribute('aria-disabled')).toBeNull();
    expect(el.querySelector('#uad-lockout-note')).toBeNull();
  });

  it('locks out until the chosen duration and hands the result back', () => {
    const result = buildActionResult(buildAdminUser({ statuses: ['LockedOut'] }), ['GoodStanding'], 'In good standing', ['LockedOut'], 'Locked out until 2026-10-14 12:00 UTC');
    const lockOut = vi.spyOn(usersService, 'lockOut').mockReturnValue(of(result));
    component.open(user);
    fixture.detectChanges();

    el.querySelector<HTMLInputElement>('#uad-lockout-for-7d')!.click();
    fill('  Spam  ');
    confirm().click();
    fixture.detectChanges();

    expect(lockOut).toHaveBeenCalledWith('u-1', { until: '2026-10-20T12:00:00.000Z', reason: 'Spam' });
    expect(dialog().open).toBe(false);
    expect(done).toEqual([{ user, result }]);
  });

  it('locks out until a chosen date, and refuses one out of range', () => {
    const lockOut = vi.spyOn(usersService, 'lockOut').mockReturnValue(of(buildActionResult(user, [], '', [], '')));
    component.open(user);
    fixture.detectChanges();

    el.querySelector<HTMLInputElement>('#uad-lockout-for-date')!.click();
    fixture.detectChanges();
    const field = fixture.debugElement.query(By.directive(DateFieldComponent)).componentInstance as DateFieldComponent;
    expect(field.min).toBe('2026-10-14');
    expect(field.max).toBe('2027-10-13');

    field.valueChange.emit('2030-01-01');
    fill('Spam');
    expect(textOf(el.querySelector('#uad-lockout-date-error'))).toBe('Choose a day from 2026-10-14 to 2027-10-13.');
    expect(confirm().getAttribute('aria-disabled')).toBe('true');

    field.valueChange.emit('2026-10-20');
    fixture.detectChanges();
    expect(confirm().getAttribute('aria-disabled')).toBeNull();
    confirm().click();

    expect(lockOut).toHaveBeenCalledWith('u-1', { until: '2026-10-20T00:00:00.000Z', reason: 'Spam' });
  });

  it('keeps the dialog open with the server message when the lockout is refused', () => {
    vi.spyOn(usersService, 'lockOut').mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 409, error: { message: 'Administrators cannot be locked out.', code: 'administrator' }
    })));
    component.open(user);
    fixture.detectChanges();
    fill('Spam');
    confirm().click();
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(textOf(el.querySelector('.uad-error'))).toBe('Administrators cannot be locked out.');
    expect(store.showToast as Mock).toHaveBeenCalledWith('Administrators cannot be locked out.', 'error', 'Error');
    expect(done).toEqual([]);
  });
});
