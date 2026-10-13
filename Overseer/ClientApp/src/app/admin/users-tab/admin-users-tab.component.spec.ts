import type { Mock } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminUsersTabComponent } from './admin-users-tab.component';
import { AccountDeletionRequestsPage, AdminService, UserDto, UsersResponse } from '../../services/admin.service';
import { AdminPageStore } from '../admin-page.store';
import { configureAdminTestBed } from '../admin.component.testing';

/** A deletion requests page with no rows and `pending` pending requests. */
function pendingPage(pending: number): AccountDeletionRequestsPage {
  return { totalCount: 0, pendingCount: pending, overdueCount: 0, erasurePathsConfigured: true, rows: [] };
}

describe('AdminUsersTabComponent', () => {
  let component: AdminUsersTabComponent;
  let fixture: ComponentFixture<AdminUsersTabComponent>;
  let adminService: AdminService;
  let store: AdminPageStore;

  beforeEach(async () => {
    ({ adminService, store } = await configureAdminTestBed(AdminUsersTabComponent));
    vi.spyOn(adminService, 'getAccountDeletionRequests').mockReturnValue(of(pendingPage(2)));

    fixture = TestBed.createComponent(AdminUsersTabComponent);
    component = fixture.componentInstance;
  });

  describe('loadUsers', () => {
    it('should populate users and totalCount on normal success', () => {
      const mockResponse: UsersResponse = {
        rows: [
          { id: '1', userName: 'admin', email: 'admin@test.com', groups: [] }
        ],
        totalCount: 1
      };
      (adminService.getUsers as Mock).mockReturnValue(of(mockResponse));

      component.loadUsers();

      expect(component.users.length).toBe(1);
      expect(component.totalCount).toBe(1);
      expect(component.usersLoading).toBe(false);
    });

    it('should catch TypeError: Failed to fetch on getUsers and reset usersLoading to false', () => {
      (adminService.getUsers as Mock).mockReturnValue(throwError(() => new TypeError('Failed to fetch')));

      expect(() => {
        component.loadUsers();
      }).not.toThrow();

      expect(component.usersLoading).toBe(false);
    });
  });

  describe('Overseer AI chat block', () => {
    const alice: UserDto = { id: 'u-1', userName: 'alice', email: 'alice@test.com', groups: [] };
    const blockedBob: UserDto = {
      id: 'u-2', userName: 'bob', email: 'bob@test.com', groups: [],
      isOverseerBlocked: true, overseerBlockReason: 'Abusive prompts'
    };

    const host = () => fixture.nativeElement as HTMLElement;
    const button = (label: string) => host().querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement;

    beforeEach(() => {
      (adminService.getUsers as Mock).mockReturnValue(of({ rows: [alice, blockedBob], totalCount: 2 }));
      fixture.detectChanges();
    });

    afterEach(() => {
      host().querySelectorAll('dialog').forEach(d => { if (d.open) d.close(); });
    });

    it("shows a Blocked badge only on the blocked user's row", () => {
      const rows = Array.from(host().querySelectorAll('tbody tr'));
      expect(rows[0].querySelector('.user-blocked-badge')).toBeNull();
      expect(rows[1].querySelector('.user-blocked-badge')?.textContent?.trim()).toBe('Blocked');
    });

    it('names the user in the Block and Unblock buttons', () => {
      expect(button('Block alice from the AI chat')).toBeTruthy();
      expect(button('Unblock bob')).toBeTruthy();
      expect(button('Block bob from the AI chat')).toBeNull();
    });

    it('Block opens the reason dialog and blocks with the typed reason, then reloads', () => {
      const blockSpy = vi.spyOn(adminService, 'blockUser').mockReturnValue(of(undefined));
      const toastSpy = vi.spyOn(store, 'showToast');
      (adminService.getUsers as Mock).mockClear();

      button('Block alice from the AI chat').click();
      fixture.detectChanges();

      const dialog = host().querySelector('app-admin-reason-dialog dialog') as HTMLDialogElement;
      expect(dialog.open).toBe(true);
      expect(dialog.querySelector('h3')?.textContent).toContain('Block from the Overseer AI chat');
      expect(dialog.querySelector('textarea')?.getAttribute('maxlength')).toBe('256');

      const field = dialog.querySelector('textarea') as HTMLTextAreaElement;
      field.value = ' Abusive prompts ';
      field.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      (dialog.querySelector('.reason-confirm') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(blockSpy).toHaveBeenCalledWith('u-1', 'Abusive prompts');
      expect(adminService.getUsers).toHaveBeenCalled();
      expect(toastSpy).toHaveBeenCalledWith(expect.stringContaining('alice'), 'success', expect.anything());
    });

    it("shows the server's message when the block is refused", () => {
      vi.spyOn(adminService, 'blockUser').mockReturnValue(
        throwError(() => ({ status: 400, error: { message: 'You cannot block yourself.' } })));
      const toastSpy = vi.spyOn(store, 'showToast');

      component.openBlockUser(alice);
      fixture.detectChanges();
      const dialog = host().querySelector('app-admin-reason-dialog dialog') as HTMLDialogElement;
      const field = dialog.querySelector('textarea') as HTMLTextAreaElement;
      field.value = 'Test';
      field.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      (dialog.querySelector('.reason-confirm') as HTMLButtonElement).click();

      expect(toastSpy).toHaveBeenCalledWith('You cannot block yourself.', 'error', 'Error');
    });

    it('Unblock asks for confirmation and unblocks, then reloads', () => {
      const unblockSpy = vi.spyOn(adminService, 'unblockUser').mockReturnValue(of(undefined));
      (adminService.getUsers as Mock).mockClear();

      button('Unblock bob').click();
      fixture.detectChanges();

      // The tab's own confirm dialog; the rate limits dialog embeds another one earlier in the DOM.
      const dialog = host().querySelector(':scope > app-admin-confirm-dialog dialog') as HTMLDialogElement;
      expect(dialog.open).toBe(true);
      expect(unblockSpy).not.toHaveBeenCalled();

      const confirm = Array.from(dialog.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Unblock') as HTMLButtonElement;
      confirm.click();
      fixture.detectChanges();

      expect(unblockSpy).toHaveBeenCalledWith('u-2');
      expect(adminService.getUsers).toHaveBeenCalled();
    });
  });

  describe('sub-tabs', () => {
    const host = () => fixture.nativeElement as HTMLElement;
    const tab = (id: string) => host().querySelector(`#users-subtab-${id}`) as HTMLButtonElement;
    const press = (target: HTMLElement, key: string) => {
      target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
      fixture.detectChanges();
    };

    it('renders the tab widget with the pending count in the Deletion requests label', () => {
      fixture.detectChanges();

      const list = host().querySelector('[role="tablist"]') as HTMLElement;
      expect(list.getAttribute('aria-label')).toBe('User sections');
      expect(tab('users').textContent?.trim()).toBe('Users');
      expect(tab('deletions').textContent?.trim()).toBe('Deletion requests (2 pending)');
      expect(adminService.getAccountDeletionRequests).toHaveBeenCalledWith({ status: 'Pending', take: 1 });

      expect(tab('users').getAttribute('aria-selected')).toBe('true');
      expect(tab('users').getAttribute('tabindex')).toBe('0');
      expect(tab('users').getAttribute('aria-controls')).toBe('users-subpanel-users');
      expect(tab('deletions').getAttribute('aria-selected')).toBe('false');
      expect(tab('deletions').getAttribute('tabindex')).toBe('-1');

      const panel = host().querySelector('#users-subpanel-users') as HTMLElement;
      expect(panel.getAttribute('role')).toBe('tabpanel');
      expect(panel.getAttribute('aria-labelledby')).toBe('users-subtab-users');
      expect(host().querySelector('#users-subpanel-deletions')).toBeNull();
    });

    it('drops the count from the label when nothing is pending', () => {
      (adminService.getAccountDeletionRequests as Mock).mockReturnValue(of(pendingPage(0)));
      fixture.detectChanges();

      expect(tab('deletions').textContent?.trim()).toBe('Deletion requests');
    });

    it('moves and selects with Right, Left, Home and End, focus following selection', () => {
      fixture.detectChanges();

      press(tab('users'), 'ArrowRight');
      expect(store.usersSubTab).toBe('deletions');
      expect(tab('deletions').getAttribute('aria-selected')).toBe('true');
      expect(tab('deletions').getAttribute('tabindex')).toBe('0');
      expect(tab('users').getAttribute('tabindex')).toBe('-1');
      expect(document.activeElement).toBe(tab('deletions'));
      const panel = host().querySelector('#users-subpanel-deletions') as HTMLElement;
      expect(panel.getAttribute('aria-labelledby')).toBe('users-subtab-deletions');
      expect(panel.querySelector('app-deletion-requests')).toBeTruthy();
      expect(host().querySelector('#users-subpanel-users')).toBeNull();

      press(tab('deletions'), 'ArrowRight');
      expect(store.usersSubTab).toBe('users');
      expect(document.activeElement).toBe(tab('users'));

      press(tab('users'), 'ArrowLeft');
      expect(store.usersSubTab).toBe('deletions');

      press(tab('deletions'), 'Home');
      expect(store.usersSubTab).toBe('users');
      expect(tab('users').getAttribute('aria-selected')).toBe('true');

      press(tab('users'), 'End');
      expect(store.usersSubTab).toBe('deletions');
      expect(document.activeElement).toBe(tab('deletions'));
    });

    it('reloads the user list when the Users sub-tab is selected again', () => {
      fixture.detectChanges();
      tab('deletions').click();
      fixture.detectChanges();
      (adminService.getUsers as Mock).mockClear();

      tab('users').click();
      fixture.detectChanges();

      expect(adminService.getUsers).toHaveBeenCalledTimes(1);
      expect(host().querySelector('#users-subpanel-users')).toBeTruthy();
    });

    it('opens on Deletion requests when the page store asked for it before the tab was created', () => {
      fixture.destroy();
      (adminService.getUsers as Mock).mockClear();
      store.usersSubTab = 'deletions';

      fixture = TestBed.createComponent(AdminUsersTabComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();

      expect(tab('deletions').getAttribute('aria-selected')).toBe('true');
      expect(host().querySelector('#users-subpanel-deletions app-deletion-requests')).toBeTruthy();
      expect(host().querySelector('#users-subpanel-users')).toBeNull();
      expect(adminService.getUsers).not.toHaveBeenCalled();
    });

    it('keeps the label current with the count the deletion requests report', () => {
      fixture.detectChanges();

      component.onPendingCountChange(5);
      fixture.detectChanges();

      expect(tab('deletions').textContent?.trim()).toBe('Deletion requests (5 pending)');
    });
  });

  describe('deletion hold', () => {
    const alice: UserDto = { id: 'u-1', userName: 'alice', email: 'alice@test.com', groups: [] };
    const heldCarol: UserDto = {
      id: 'u-3', userName: 'carol', email: 'carol@test.com', groups: [],
      hasAccountHold: true, accountHoldReason: 'Open fraud case', pendingDeletionRequestId: 7
    };

    const host = () => fixture.nativeElement as HTMLElement;
    const button = (label: string) => host().querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement;

    beforeEach(() => {
      (adminService.getUsers as Mock).mockReturnValue(of({ rows: [alice, heldCarol], totalCount: 2 }));
      fixture.detectChanges();
    });

    afterEach(() => {
      host().querySelectorAll('dialog').forEach(d => { if (d.open) d.close(); });
    });

    it("shows the Deletion hold and Pending deletion badges only on the held user's row", () => {
      const rows = Array.from(host().querySelectorAll('tbody tr'));
      expect(rows[0].querySelector('.user-hold-badge')).toBeNull();
      expect(rows[0].querySelector('.user-pending-deletion-badge')).toBeNull();
      expect(rows[1].querySelector('.user-hold-badge')?.textContent?.trim()).toBe('Deletion hold');
      expect(rows[1].querySelector('.user-pending-deletion-badge')?.textContent?.trim()).toBe('Pending deletion');
    });

    it('names the user in the Place and Release buttons', () => {
      expect(button('Place a deletion hold on alice')).toBeTruthy();
      expect(button('Release the deletion hold on carol')).toBeTruthy();
      expect(button('Place a deletion hold on carol')).toBeNull();
    });

    it('Place deletion hold needs a reason, then places the hold and reloads', () => {
      const placeSpy = vi.spyOn(adminService, 'placeAccountHold').mockReturnValue(of(undefined));
      const toastSpy = vi.spyOn(store, 'showToast');
      (adminService.getUsers as Mock).mockClear();

      button('Place a deletion hold on alice').click();
      fixture.detectChanges();

      const dialog = host().querySelector('app-admin-reason-dialog dialog') as HTMLDialogElement;
      expect(dialog.open).toBe(true);
      expect(dialog.querySelector('h3')?.textContent).toContain('Place a deletion hold');
      expect(dialog.querySelector('.reason-message')?.textContent).toContain('never shown to the user');
      expect(dialog.querySelector('textarea')?.getAttribute('maxlength')).toBe('512');

      const confirm = dialog.querySelector('.reason-confirm') as HTMLButtonElement;
      expect(confirm.getAttribute('aria-disabled')).toBe('true');
      confirm.click();
      fixture.detectChanges();
      expect(placeSpy).not.toHaveBeenCalled();
      expect(dialog.open).toBe(true);

      const field = dialog.querySelector('textarea') as HTMLTextAreaElement;
      field.value = ' Suspicious activity ';
      field.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      confirm.click();
      fixture.detectChanges();

      expect(placeSpy).toHaveBeenCalledWith('u-1', 'Suspicious activity');
      expect(adminService.getUsers).toHaveBeenCalled();
      expect(toastSpy).toHaveBeenCalledWith(expect.stringContaining('alice'), 'success', expect.anything());
    });

    it('Release deletion hold asks for confirmation, then releases and reloads', () => {
      const releaseSpy = vi.spyOn(adminService, 'releaseAccountHold').mockReturnValue(of(undefined));
      (adminService.getUsers as Mock).mockClear();

      button('Release the deletion hold on carol').click();
      fixture.detectChanges();

      // The tab's own confirm dialog; the rate limits dialog embeds another one earlier in the DOM.
      const dialog = host().querySelector(':scope > app-admin-confirm-dialog dialog') as HTMLDialogElement;
      expect(dialog.open).toBe(true);
      expect(dialog.textContent).toContain('does not approve a pending deletion request');
      expect(releaseSpy).not.toHaveBeenCalled();

      const confirm = Array.from(dialog.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Release Hold') as HTMLButtonElement;
      confirm.click();
      fixture.detectChanges();

      expect(releaseSpy).toHaveBeenCalledWith('u-3');
      expect(adminService.getUsers).toHaveBeenCalled();
    });
  });

  describe('manage request from AI Telemetry', () => {
    const host = () => fixture.nativeElement as HTMLElement;
    const dialog = () => host().querySelector('dialog.dialog-wide') as HTMLDialogElement;

    afterEach(() => {
      host().querySelectorAll('dialog').forEach(d => { if (d.open) d.close(); });
    });

    it('selects the Users sub-tab and opens Manage User Configs for the requested user without looking the user up', () => {
      const configsSpy = vi.spyOn(adminService, 'getUserSystemConfigs').mockReturnValue(of([]));
      store.usersSubTab = 'deletions';
      store.pendingManageUser = { userId: 'u-9', userName: 'zed' };

      fixture.detectChanges();

      expect(store.usersSubTab).toBe('users');
      expect(store.pendingManageUser).toBeNull();
      expect(host().querySelector('#users-subtab-users')?.getAttribute('aria-selected')).toBe('true');
      expect(configsSpy).toHaveBeenCalledWith('u-9');
      expect(component.selectedUserForConfigs?.id).toBe('u-9');
      expect(dialog().open).toBe(true);
      expect(dialog().querySelector('h3')?.textContent).toContain('Manage User Configs: zed');
      for (const call of (adminService.getUsers as Mock).mock.calls) {
        expect(call[2] ?? '').toBe('');
      }
    });

    it('handles a request that arrives while the tab is open', () => {
      const configsSpy = vi.spyOn(adminService, 'getUserSystemConfigs').mockReturnValue(of([]));
      fixture.detectChanges();
      component.selectSubTab('deletions');
      fixture.detectChanges();

      store.requestManageUser('u-7', 'yuri');
      fixture.detectChanges();

      expect(store.usersSubTab).toBe('users');
      expect(store.pendingManageUser).toBeNull();
      expect(configsSpy).toHaveBeenCalledWith('u-7');
      expect(dialog().open).toBe(true);
      expect(dialog().querySelector('h3')?.textContent).toContain('Manage User Configs: yuri');
    });
  });
});
