import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { AdminUsersService } from '../../../services/admin-users.service';
import { AdminPageStore } from '../../admin-page.store';
import { configureAdminTestBed } from '../../admin.component.testing';
import { buildActionResult, buildAdminUser } from '../admin-users.testing';
import { UserActionDone } from './user-action-dialogs';
import { UserRestrictionsDialogComponent } from './user-restrictions-dialog.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

describe('UserRestrictionsDialogComponent', () => {
  let fixture: ComponentFixture<UserRestrictionsDialogComponent>;
  let component: UserRestrictionsDialogComponent;
  let el: HTMLElement;
  let usersService: AdminUsersService;
  let store: AdminPageStore;
  let done: UserActionDone[];

  beforeEach(async () => {
    ({ usersService, store } = await configureAdminTestBed(UserRestrictionsDialogComponent));
    vi.spyOn(store, 'showToast').mockReturnValue(undefined);
    fixture = TestBed.createComponent(UserRestrictionsDialogComponent);
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
  const note = () => textOf(el.querySelector('#uad-restrictions-note'));

  it('prefills the two restrictions from the account', () => {
    component.open(buildAdminUser({ isGameLogBanned: true }));
    fixture.detectChanges();

    expect(el.querySelector<HTMLInputElement>('#uad-restrictions-gamelog')!.checked).toBe(true);
    expect(el.querySelector<HTMLInputElement>('#uad-restrictions-bones')!.checked).toBe(false);
    expect(textOf(confirm())).toBe('Save Restrictions');
    expect(confirm().classList.contains('btn-gh-delete')).toBe(false);
    expect(confirm().getAttribute('aria-disabled')).toBe('true');
    expect(note()).toBe('Nothing has changed yet.');
  });

  it('needs a reason while a restriction is turned on, then saves', () => {
    const user = buildAdminUser();
    const result = buildActionResult(buildAdminUser({ statuses: ['BonesBanned'] }), ['GoodStanding'], 'In good standing', ['BonesBanned'], 'Bones banned');
    const save = vi.spyOn(usersService, 'setGameRestrictions').mockReturnValue(of(result));
    component.open(user);
    fixture.detectChanges();

    el.querySelector<HTMLInputElement>('#uad-restrictions-bones')!.click();
    fixture.detectChanges();
    expect(note()).toBe('Still needed: a reason.');
    confirm().click();
    expect(save).not.toHaveBeenCalled();

    const field = el.querySelector<HTMLTextAreaElement>('#uad-restrictions-reason')!;
    field.value = 'Corrupt bones';
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(confirm().getAttribute('aria-disabled')).toBeNull();
    confirm().click();

    expect(save).toHaveBeenCalledWith('u-1', { gameLogBanned: false, bonesBanned: true, reason: 'Corrupt bones' });
    expect(done).toEqual([{ user, result }]);
  });

  it('lifts the restrictions without a reason', () => {
    const user = buildAdminUser({ isGameLogBanned: true, statuses: ['GameLogBanned'] });
    const save = vi.spyOn(usersService, 'setGameRestrictions').mockReturnValue(of(buildActionResult(buildAdminUser(), [], '', [], '')));
    component.open(user);
    fixture.detectChanges();

    el.querySelector<HTMLInputElement>('#uad-restrictions-gamelog')!.click();
    fixture.detectChanges();
    confirm().click();

    expect(save).toHaveBeenCalledWith('u-1', { gameLogBanned: false, bonesBanned: false });
  });
});
