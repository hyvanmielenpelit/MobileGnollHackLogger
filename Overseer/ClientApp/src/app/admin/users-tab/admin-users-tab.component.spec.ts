import type { Mock } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';
import { AdminUsersTabComponent } from './admin-users-tab.component';
import { AccountDeletionRequestsPage, AdminService } from '../../services/admin.service';
import { AdminUsersService } from '../../services/admin-users.service';
import { AdminPageStore } from '../admin-page.store';
import { configureAdminTestBed } from '../admin.component.testing';
import { AdminBulkWizardComponent } from './bulk-wizard/admin-bulk-wizard.component';
import { AdminUserLogComponent } from './admin-log/admin-user-log.component';

/** A deletion requests page with no rows and `pending` pending requests. */
function pendingPage(pending: number): AccountDeletionRequestsPage {
  return { totalCount: 0, pendingCount: pending, overdueCount: 0, erasurePathsConfigured: true, rows: [] };
}

describe('AdminUsersTabComponent', () => {
  let component: AdminUsersTabComponent;
  let fixture: ComponentFixture<AdminUsersTabComponent>;
  let adminService: AdminService;
  let usersService: AdminUsersService;
  let store: AdminPageStore;

  beforeEach(async () => {
    ({ adminService, usersService, store } = await configureAdminTestBed(AdminUsersTabComponent));
    vi.spyOn(adminService, 'getAccountDeletionRequests').mockReturnValue(of(pendingPage(2)));

    fixture = TestBed.createComponent(AdminUsersTabComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    (fixture.nativeElement as HTMLElement).querySelectorAll('dialog').forEach(d => { if (d.open) d.close(); });
  });

  const host = () => fixture.nativeElement as HTMLElement;
  const wizard = () => fixture.debugElement.query(By.directive(AdminBulkWizardComponent)).componentInstance as AdminBulkWizardComponent;

  describe('header', () => {
    it('shows the Users heading and Bulk Actions, a gold button that opens a dialog', () => {
      fixture.detectChanges();

      expect(host().querySelector('.tab-header-row h3')?.textContent?.trim()).toBe('Users');
      const button = host().querySelector<HTMLButtonElement>('#au-bulk-actions')!;
      expect(button.textContent?.trim()).toBe('Bulk Actions');
      expect(button.classList.contains('btn-gh')).toBe(true);
      expect(button.getAttribute('aria-haspopup')).toBe('dialog');
      expect(button.querySelector('svg.btn-icon')?.getAttribute('aria-hidden')).toBe('true');
    });

    it('opens the wizard seeded with the list criteria, as a copy', () => {
      store.usersQuery.criteria = { statuses: ['LockedOut'], emailConfirmed: 'no' };
      fixture.detectChanges();
      const open = vi.spyOn(wizard(), 'open');

      host().querySelector<HTMLButtonElement>('#au-bulk-actions')!.click();

      expect(open).toHaveBeenCalledWith({ statuses: ['LockedOut'], emailConfirmed: 'no' });
      expect(wizard().isOpen).toBe(true);
      expect(wizard().criteria).toEqual({ statuses: ['LockedOut'], emailConfirmed: 'no' });
      expect(wizard().criteria).not.toBe(store.usersQuery.criteria);
      expect(wizard().criteria.statuses).not.toBe(store.usersQuery.criteria.statuses);
    });

    it('opens the wizard a link asked for, on its results', () => {
      store.pendingBulkWizard = { operationId: 12 };
      fixture.detectChanges();

      expect(store.pendingBulkWizard).toBeNull();
      expect(wizard().isOpen).toBe(true);
      expect(wizard().operationId).toBe(12);
    });

    it("opens the Admin log the wizard's Show in admin log names", () => {
      fixture.detectChanges();

      wizard().showInLog.emit({ bulkOperationId: 12 });
      fixture.detectChanges();

      expect(store.usersSubTab).toBe('log');
      expect(store.usersLogFilter).toEqual(expect.objectContaining({ bulkOperationId: 12 }));
      const log = fixture.debugElement.query(By.directive(AdminUserLogComponent)).componentInstance as AdminUserLogComponent;
      expect(log.filter).toEqual(expect.objectContaining({ bulkOperationId: 12 }));
    });

    it('reloads the user list after an operation finished', () => {
      fixture.detectChanges();
      (usersService.list as Mock).mockClear();

      wizard().operationFinished.emit(12);

      expect(usersService.list).toHaveBeenCalledTimes(1);
    });
  });

  describe('sub-tabs', () => {
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
      expect(tab('log').textContent?.trim()).toBe('Admin log');
      expect(adminService.getAccountDeletionRequests).toHaveBeenCalledWith({ status: 'Pending', take: 1 });

      expect(tab('users').getAttribute('aria-selected')).toBe('true');
      expect(tab('users').getAttribute('tabindex')).toBe('0');
      expect(tab('users').getAttribute('aria-controls')).toBe('users-subpanel-users');
      expect(tab('deletions').getAttribute('aria-selected')).toBe('false');
      expect(tab('deletions').getAttribute('tabindex')).toBe('-1');
      expect(tab('log').getAttribute('tabindex')).toBe('-1');

      const panel = host().querySelector('#users-subpanel-users') as HTMLElement;
      expect(panel.getAttribute('role')).toBe('tabpanel');
      expect(panel.getAttribute('aria-labelledby')).toBe('users-subtab-users');
      expect(panel.querySelector('app-admin-user-list')).toBeTruthy();
      expect(host().querySelector('#users-subpanel-deletions')).toBeNull();
      expect(host().querySelector('#users-subpanel-log')).toBeNull();
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
      expect(store.usersSubTab).toBe('log');
      expect(host().querySelector('#users-subpanel-log app-admin-user-log')).toBeTruthy();
      expect(document.activeElement).toBe(tab('log'));

      press(tab('log'), 'ArrowRight');
      expect(store.usersSubTab).toBe('users');
      expect(document.activeElement).toBe(tab('users'));

      press(tab('users'), 'ArrowLeft');
      expect(store.usersSubTab).toBe('log');

      press(tab('log'), 'Home');
      expect(store.usersSubTab).toBe('users');
      expect(tab('users').getAttribute('aria-selected')).toBe('true');

      press(tab('users'), 'End');
      expect(store.usersSubTab).toBe('log');
      expect(document.activeElement).toBe(tab('log'));
    });

    it('loads the user list again when the Users sub-tab is selected again', () => {
      fixture.detectChanges();
      tab('deletions').click();
      fixture.detectChanges();
      (usersService.list as Mock).mockClear();

      tab('users').click();
      fixture.detectChanges();

      expect(usersService.list).toHaveBeenCalledTimes(1);
      expect(host().querySelector('#users-subpanel-users')).toBeTruthy();
    });

    it('opens on the sub-tab the page store asked for before the tab was created', () => {
      store.usersSubTab = 'log';
      fixture.detectChanges();

      expect(tab('log').getAttribute('aria-selected')).toBe('true');
      expect(host().querySelector('#users-subpanel-log app-admin-user-log')).toBeTruthy();
      expect(host().querySelector('#users-subpanel-users')).toBeNull();
      expect(usersService.list).not.toHaveBeenCalled();
    });

    it('keeps the label current with the count the deletion requests report', () => {
      fixture.detectChanges();

      component.onPendingCountChange(5);
      fixture.detectChanges();

      expect(tab('deletions').textContent?.trim()).toBe('Deletion requests (5 pending)');
    });
  });

  describe('manage request from AI Telemetry', () => {
    const dialog = () => host().querySelector('dialog.dialog-wide') as HTMLDialogElement;

    it('selects the Users sub-tab, whose list opens Manage User Configs for the requested user', () => {
      const configsSpy = vi.spyOn(adminService, 'getUserSystemConfigs').mockReturnValue(of([]));
      store.usersSubTab = 'deletions';
      store.pendingManageUser = { userId: 'u-9', userName: 'zed' };

      fixture.detectChanges();

      expect(store.usersSubTab).toBe('users');
      expect(store.pendingManageUser).toBeNull();
      expect(host().querySelector('#users-subtab-users')?.getAttribute('aria-selected')).toBe('true');
      expect(configsSpy).toHaveBeenCalledWith('u-9');
      expect(dialog().open).toBe(true);
      expect(dialog().querySelector('h3')?.textContent).toContain('Manage User Configs: zed');
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
