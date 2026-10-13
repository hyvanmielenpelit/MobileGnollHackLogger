import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { AdminUsersService } from '../../../services/admin-users.service';
import { AdminPageStore } from '../../admin-page.store';
import { configureAdminTestBed } from '../../admin.component.testing';
import { buildActionResult, buildAdminUser } from '../admin-users.testing';
import { UserActionDone } from './user-action-dialogs';
import { UserBanDialogComponent } from './user-ban-dialog.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

describe('UserBanDialogComponent', () => {
  let fixture: ComponentFixture<UserBanDialogComponent>;
  let component: UserBanDialogComponent;
  let el: HTMLElement;
  let usersService: AdminUsersService;
  let store: AdminPageStore;
  let done: UserActionDone[];
  const user = buildAdminUser();

  beforeEach(async () => {
    ({ usersService, store } = await configureAdminTestBed(UserBanDialogComponent));
    vi.spyOn(store, 'showToast').mockReturnValue(undefined);
    fixture = TestBed.createComponent(UserBanDialogComponent);
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

  function typeReason(reason: string): void {
    const field = el.querySelector<HTMLTextAreaElement>('#uad-ban-reason')!;
    field.value = reason;
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  it('opens on its heading, states the effect and needs a reason and the check', () => {
    const ban = vi.spyOn(usersService, 'ban');
    component.open(user);
    fixture.detectChanges();

    expect(el.querySelector<HTMLDialogElement>('dialog')!.open).toBe(true);
    expect(document.activeElement?.id).toBe('uad-ban-title');
    expect(textOf(el.querySelector('#uad-ban-effect'))).toBe('Cannot sign in anywhere until unbanned. The reason is not shown to the user.');
    expect(textOf(confirm())).toBe('Ban Account');
    expect(confirm().getAttribute('aria-disabled')).toBe('true');
    expect(textOf(el.querySelector('#uad-ban-note'))).toBe('Still needed: a reason and the account check.');

    typeReason('   ');
    confirm().click();
    expect(ban).not.toHaveBeenCalled();

    typeReason('Cheating');
    el.querySelector<HTMLInputElement>('#uad-ban-check')!.click();
    fixture.detectChanges();
    expect(confirm().getAttribute('aria-disabled')).toBeNull();
  });

  it('bans with the trimmed reason and hands the result back', () => {
    const result = buildActionResult(buildAdminUser({ statuses: ['Banned'] }), ['GoodStanding'], 'In good standing', ['Banned'], 'Banned');
    const ban = vi.spyOn(usersService, 'ban').mockReturnValue(of(result));
    component.open(user);
    fixture.detectChanges();
    typeReason(' Cheating ');
    el.querySelector<HTMLInputElement>('#uad-ban-check')!.click();
    fixture.detectChanges();

    confirm().click();

    expect(ban).toHaveBeenCalledWith('u-1', 'Cheating');
    expect(el.querySelector<HTMLDialogElement>('dialog')!.open).toBe(false);
    expect(done).toEqual([{ user, result }]);
  });

  it('keeps the dialog open with the server message when the ban is refused', () => {
    vi.spyOn(usersService, 'ban').mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 409, error: { message: 'The account is already banned.', code: 'already-done' }
    })));
    component.open(user);
    fixture.detectChanges();
    typeReason('Cheating');
    el.querySelector<HTMLInputElement>('#uad-ban-check')!.click();
    fixture.detectChanges();

    confirm().click();
    fixture.detectChanges();

    expect(el.querySelector<HTMLDialogElement>('dialog')!.open).toBe(true);
    expect(textOf(el.querySelector('.uad-error'))).toBe('The account is already banned.');
    expect(done).toEqual([]);
  });
});
