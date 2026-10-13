import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';

import { AccountDeletionImpact, AdminUser, AdminUsersService } from '../../../services/admin-users.service';
import { AdminPageStore } from '../../admin-page.store';
import { configureAdminTestBed } from '../../admin.component.testing';
import { buildActionResult, buildAdminUser, buildDeletionImpact } from '../admin-users.testing';
import { UserActionDone } from './user-action-dialogs';
import { UserDeleteDialogComponent, acknowledgmentText, erasedText } from './user-delete-dialog.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

/** An impact with 12 game logs and 2 chats to acknowledge. */
function impactWithData(): AccountDeletionImpact {
  const impact = buildDeletionImpact();
  impact.categories = impact.categories.map(c =>
    c.key === 'game-logs' ? { ...c, count: 12 } : c.key === 'chats' ? { ...c, count: 2 } : c);
  return impact;
}

describe('UserDeleteDialogComponent', () => {
  let fixture: ComponentFixture<UserDeleteDialogComponent>;
  let component: UserDeleteDialogComponent;
  let el: HTMLElement;
  let usersService: AdminUsersService;
  let store: AdminPageStore;
  let done: UserActionDone[];
  const user = buildAdminUser();

  beforeEach(async () => {
    ({ usersService, store } = await configureAdminTestBed(UserDeleteDialogComponent));
    vi.spyOn(store, 'showToast').mockReturnValue(undefined);
    fixture = TestBed.createComponent(UserDeleteDialogComponent);
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

  const dialog = () => el.querySelector<HTMLDialogElement>('dialog.uad-delete-dialog')!;
  const confirm = () => el.querySelector<HTMLButtonElement>('.uad-confirm');

  function openWith(impact: AccountDeletionImpact): void {
    vi.spyOn(usersService, 'deletionImpact').mockReturnValue(of(impact));
    component.open(user);
    fixture.detectChanges();
  }

  function type(selector: string, value: string): void {
    const field = el.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)!;
    field.value = value;
    field.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  it('shows a spinner on its heading while the impact loads', () => {
    const pending = new Subject<AccountDeletionImpact>();
    vi.spyOn(usersService, 'deletionImpact').mockReturnValue(pending);
    component.open(user);
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(document.activeElement?.id).toBe('uad-delete-title');
    expect(dialog().getAttribute('aria-busy')).toBe('true');
    expect(el.querySelector('.uad-loading .dc-ring')).toBeTruthy();
    expect(confirm()).toBeNull();

    pending.next(buildDeletionImpact());
    fixture.detectChanges();
    expect(el.querySelector('.uad-loading')).toBeNull();
    expect(confirm()).toBeTruthy();
  });

  it('renders no delete control when the deletion is refused', () => {
    openWith(buildDeletionImpact({ refusals: [{ code: 'hold', message: 'Release the deletion hold first.' }] }));

    expect(textOf(el.querySelector('.uad-refused li'))).toBe('Release the deletion hold first.');
    expect(confirm()).toBeNull();
    expect(el.querySelector('#uad-delete-name')).toBeNull();
    expect(textOf(el.querySelector('.uad-cancel'))).toBe('Close');
  });

  it('asks for one acknowledgment per important category with data', () => {
    openWith(impactWithData());

    expect(textOf(el.querySelector('#uad-delete-zone-title'))).toBe('Permanently delete alice');
    const checks = Array.from(el.querySelectorAll('.uad-checks .checkbox-label')).map(l => textOf(l));
    expect(checks).toEqual([
      'I understand 12 game logs and their top-score entries are erased.',
      'I understand 2 chats are erased.'
    ]);
  });

  it('keeps Delete Account aria-disabled until every check passes, then deletes', () => {
    const result = buildActionResult(null, ['GoodStanding'], 'In good standing', [], 'Deleted', 'alice was deleted.');
    const del = vi.spyOn(usersService, 'deleteUser').mockReturnValue(of(result));
    openWith(impactWithData());

    expect(confirm()!.getAttribute('aria-disabled')).toBe('true');
    expect(textOf(el.querySelector('#uad-delete-note'))).toBe('Still needed: every acknowledgment, a reason and the user name typed exactly.');

    el.querySelector<HTMLInputElement>('#uad-delete-ack-game-logs')!.click();
    type('#uad-delete-reason', 'Spam account');
    type('#uad-delete-name', 'Alice');
    expect(confirm()!.getAttribute('aria-disabled')).toBe('true');
    expect(textOf(el.querySelector('#uad-delete-note'))).toBe('Still needed: every acknowledgment and the user name typed exactly.');
    confirm()!.click();
    expect(del).not.toHaveBeenCalled();

    el.querySelector<HTMLInputElement>('#uad-delete-ack-chats')!.click();
    type('#uad-delete-name', 'alice');
    expect(confirm()!.getAttribute('aria-disabled')).toBeNull();
    confirm()!.click();

    expect(del).toHaveBeenCalledWith('u-1', { confirmUserName: 'alice', reason: 'Spam account', acknowledgedCategories: ['game-logs', 'chats'] });
    expect(dialog().open).toBe(false);
    expect(done).toEqual([{ user, result, erasedText: 'erased 12 game logs, 2 chats' }]);
  });

  it('keeps the dialog open with the server error inline', () => {
    vi.spyOn(usersService, 'deleteUser').mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 500, error: { message: 'Deletion failed: disk error', code: 'erasure-failed' }
    })));
    openWith(buildDeletionImpact());
    type('#uad-delete-reason', 'Spam');
    type('#uad-delete-name', 'alice');
    confirm()!.click();
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(textOf(el.querySelector('.uad-error'))).toBe('Deletion failed: disk error');
    expect(done).toEqual([]);
  });

  it('Ban instead closes the dialog and hands the account over', () => {
    const banned: AdminUser[] = [];
    component.banInstead.subscribe(u => banned.push(u));
    openWith(buildDeletionImpact());

    el.querySelector<HTMLButtonElement>('.uad-ban-instead')!.click();

    expect(dialog().open).toBe(false);
    expect(banned).toEqual([user]);
  });

  it('words the acknowledgments and the tombstone summary in the singular too', () => {
    expect(acknowledgmentText({ key: 'game-logs', label: 'Game logs', count: 1, important: true }))
      .toBe('I understand 1 game log and its top-score entries are erased.');
    expect(acknowledgmentText({ key: 'bones', label: 'Bones files', count: 3, important: true }))
      .toBe('I understand 3 bones files are deleted.');
    expect(erasedText(buildDeletionImpact())).toBe('erased 0 game logs, 0 chats');
  });
});
