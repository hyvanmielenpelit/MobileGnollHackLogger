import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { AdminUsersService } from '../../../services/admin-users.service';
import { AdminPageStore } from '../../admin-page.store';
import { configureAdminTestBed } from '../../admin.component.testing';
import { buildActionResult, buildAdminUser } from '../admin-users.testing';
import { UserActionDone } from './user-action-dialogs';
import { UserChangeEmailDialogComponent } from './user-change-email-dialog.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

describe('UserChangeEmailDialogComponent', () => {
  let fixture: ComponentFixture<UserChangeEmailDialogComponent>;
  let component: UserChangeEmailDialogComponent;
  let el: HTMLElement;
  let usersService: AdminUsersService;
  let store: AdminPageStore;
  let done: UserActionDone[];
  const user = buildAdminUser();

  beforeEach(async () => {
    ({ usersService, store } = await configureAdminTestBed(UserChangeEmailDialogComponent));
    vi.spyOn(store, 'showToast').mockReturnValue(undefined);
    fixture = TestBed.createComponent(UserChangeEmailDialogComponent);
    component = fixture.componentInstance;
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

  const confirm = () => el.querySelector<HTMLButtonElement>('.uad-confirm')!;
  const note = () => textOf(el.querySelector('#uad-email-note'));

  function type(selector: string, value: string): void {
    const field = el.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)!;
    field.value = value;
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  function click(selector: string): void {
    el.querySelector<HTMLInputElement>(selector)!.click();
    fixture.detectChanges();
  }

  function openDialog(): void {
    component.open(user);
    fixture.detectChanges();
  }

  it('opens on its heading with the current address shown and the address not marked confirmed', () => {
    openDialog();

    expect(el.querySelector<HTMLDialogElement>('dialog')!.open).toBe(true);
    expect(document.activeElement?.id).toBe('uad-email-title');
    expect(textOf(el.querySelector('#uad-email-title'))).toBe('Change the email of alice');
    expect(textOf(el.querySelector('#uad-email-current'))).toBe('Current address: alice@example.com');
    expect(el.querySelector<HTMLInputElement>('#uad-email-confirmed')!.checked).toBe(false);
    const address = el.querySelector<HTMLInputElement>('#uad-email-address')!;
    expect(address.type).toBe('email');
    expect(address.getAttribute('maxlength')).toBe('256');
    expect(textOf(confirm())).toBe('Change Email');
  });

  it('names what is still needed, and refuses the current address', () => {
    const changeEmail = vi.spyOn(usersService, 'changeEmail');
    openDialog();

    expect(confirm().getAttribute('aria-disabled')).toBe('true');
    expect(confirm().getAttribute('aria-describedby')).toBe('uad-email-note');
    expect(note()).toBe('Still needed: a new address, a reason and the account check.');

    type('#uad-email-address', ' ALICE@example.com ');
    expect(note()).toBe('Still needed: an address other than the current one, a reason and the account check.');
    confirm().click();
    expect(changeEmail).not.toHaveBeenCalled();

    type('#uad-email-address', 'alice2@example.com');
    expect(note()).toBe('Still needed: a reason and the account check.');

    type('#uad-email-reason', 'Lost mailbox');
    expect(note()).toBe('Still needed: the account check.');

    click('#uad-email-check');
    expect(el.querySelector('#uad-email-note')).toBeNull();
    expect(confirm().getAttribute('aria-disabled')).toBeNull();
  });

  it('states the effect of marking the address confirmed or not', () => {
    openDialog();
    expect(textOf(el.querySelector('#uad-email-effect'))).toBe(
      'The new address is emailed instructions for confirming it from the Resend email confirmation page of the account website. '
      + 'alice cannot sign in until it is confirmed. Signed-in sessions end within 5 minutes. The user name does not change.');

    click('#uad-email-confirmed');
    expect(textOf(el.querySelector('#uad-email-effect'))).toBe(
      'alice signs in with the new address confirmed. Signed-in sessions end within 5 minutes. The user name does not change.');
  });

  it('sends the trimmed address and reason with the confirmed choice and hands the result back', () => {
    const result = buildActionResult(buildAdminUser({ email: 'alice2@example.com' }), ['GoodStanding'], 'In good standing',
      ['GoodStanding'], 'In good standing', 'Changed the email of alice.');
    const changeEmail = vi.spyOn(usersService, 'changeEmail').mockReturnValue(of(result));
    openDialog();
    type('#uad-email-address', ' alice2@example.com ');
    click('#uad-email-confirmed');
    type('#uad-email-reason', ' Lost mailbox ');
    click('#uad-email-check');

    confirm().click();

    expect(changeEmail).toHaveBeenCalledWith('u-1', { email: 'alice2@example.com', markConfirmed: true, reason: 'Lost mailbox' });
    expect(el.querySelector<HTMLDialogElement>('dialog')!.open).toBe(false);
    expect(done).toEqual([{ user, result }]);
  });

  it('keeps the dialog open with the server message when the address is taken', () => {
    vi.spyOn(usersService, 'changeEmail').mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 409, error: { message: 'Another account already uses this email address.', code: 'email-taken' }
    })));
    openDialog();
    type('#uad-email-address', 'bob@example.com');
    type('#uad-email-reason', 'Lost mailbox');
    click('#uad-email-check');

    confirm().click();
    fixture.detectChanges();

    expect(el.querySelector<HTMLDialogElement>('dialog')!.open).toBe(true);
    expect(textOf(el.querySelector('.uad-error'))).toBe('Another account already uses this email address.');
    expect(store.showToast).toHaveBeenCalledWith('Another account already uses this email address.', 'error', 'Error');
    expect(done).toEqual([]);
  });
});
