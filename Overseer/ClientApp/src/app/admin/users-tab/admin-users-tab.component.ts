import { Component, OnInit, OnDestroy, inject, ViewChild, ChangeDetectionStrategy } from '@angular/core';
import { AdminService } from '../../services/admin.service';
import { AdminPageStore, AdminUsersLogFilter, AdminUsersSubTab } from '../admin-page.store';
import { DeletionRequestsComponent } from './deletion-requests/deletion-requests.component';
import { AdminUserListComponent } from './user-list/admin-user-list.component';
import { AdminUserLogComponent } from './admin-log/admin-user-log.component';
import { AdminBulkWizardComponent } from './bulk-wizard/admin-bulk-wizard.component';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';
import { Subscription } from 'rxjs';

/**
 * The Admin page's Users tab: the header with Bulk Actions, and three sub-tabs — the user list, the
 * deletion requests and the admin log. It hosts the Bulk Actions wizard, which opens seeded with the
 * list's current criteria.
 */
@Component({
  selector: 'app-admin-users-tab',
  imports: [AdminUserListComponent, DeletionRequestsComponent, AdminUserLogComponent, AdminBulkWizardComponent],
  templateUrl: './admin-users-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-users-tab.component.scss'
})
export class AdminUsersTabComponent implements OnInit, OnDestroy {
  private adminService = inject(AdminService);
  private store = inject(AdminPageStore);

  @ViewChild(AdminBulkWizardComponent, { static: true }) bulkWizard!: AdminBulkWizardComponent;
  @ViewChild(AdminUserListComponent) userList?: AdminUserListComponent;

  /** The sub-tabs, in display order. */
  readonly subTabs: readonly AdminUsersSubTab[] = ['users', 'deletions', 'log'];

  /** Pending deletion requests, for the Deletion requests label; null until counted. */
  pendingDeletionCount: number | null = null;

  private pendingCountSub?: Subscription;
  private manageUserSub?: Subscription;
  private bulkWizardSub?: Subscription;

  ngOnInit() {
    ensureOverlayPolyfills();
    this.loadPendingDeletionCount();

    this.takePendingManageUser();
    this.manageUserSub = this.store.manageUserRequested$.subscribe(() => this.takePendingManageUser());
    this.takePendingBulkWizard();
    this.bulkWizardSub = this.store.bulkWizardRequested$.subscribe(() => this.takePendingBulkWizard());
  }

  ngOnDestroy() {
    this.pendingCountSub?.unsubscribe();
    this.manageUserSub?.unsubscribe();
    this.bulkWizardSub?.unsubscribe();
  }

  /**
   * A pending manage request selects the Users sub-tab; the user list reads and clears it and opens
   * Manage User Configs for that user.
   */
  private takePendingManageUser(): void {
    if (this.store.pendingManageUser) {
      this.selectSubTab('users');
    }
  }

  /** Reads and clears the page store's pending wizard request and opens the wizard as it asks. */
  private takePendingBulkWizard(): void {
    const pending = this.store.pendingBulkWizard;
    if (!pending) {
      return;
    }
    this.store.pendingBulkWizard = null;
    if (pending.operationId != null) {
      this.bulkWizard.openResults(pending.operationId);
    } else {
      this.bulkWizard.open(pending.criteria ?? this.store.usersQuery.criteria);
    }
  }

  // --- Bulk Actions ---

  /** Opens the wizard on step 1, seeded with the user list's current criteria. */
  openBulkWizard(): void {
    this.bulkWizard.open(this.store.usersQuery.criteria);
  }

  /** Focus goes back to Bulk Actions after the wizard closes. */
  onBulkWizardClosed(): void {
    document.getElementById('au-bulk-actions')?.focus();
  }

  /** The wizard's *Show in admin log*: the Admin log, filtered to what the wizard names. */
  onShowInLog(filter: AdminUsersLogFilter): void {
    this.store.showUsersLog(filter);
  }

  /** An operation changed accounts: the list, when shown, reloads. */
  onOperationFinished(): void {
    this.userList?.reload();
  }

  // --- Sub-tabs ---

  /** The selected sub-tab, kept in the page store so a link can choose it before the tab exists. */
  get subTab(): AdminUsersSubTab {
    return this.store.usersSubTab;
  }

  /** `Users`; `Deletion requests (2 pending)` while requests are pending; `Admin log`. */
  subTabLabel(tab: AdminUsersSubTab): string {
    if (tab === 'users') {
      return 'Users';
    }
    if (tab === 'log') {
      return 'Admin log';
    }
    const pending = this.pendingDeletionCount ?? 0;
    return pending > 0 ? `Deletion requests (${pending} pending)` : 'Deletion requests';
  }

  /** Selects a sub-tab; each panel loads what it shows when it appears. */
  selectSubTab(tab: AdminUsersSubTab): void {
    if (tab === this.subTab) {
      return;
    }
    this.store.usersSubTab = tab;
  }

  /** Left/Right move and wrap, Home/End jump to the ends; focus follows selection. */
  onSubTabKeydown(event: KeyboardEvent, index: number): void {
    const targets: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: this.subTabs.length - 1
    };
    const requested = targets[event.key];
    if (requested === undefined) {
      return;
    }
    event.preventDefault();
    const next = (requested + this.subTabs.length) % this.subTabs.length;
    const tab = this.subTabs[next];
    this.selectSubTab(tab);
    document.getElementById(`users-subtab-${tab}`)?.focus();
  }

  onPendingCountChange(count: number): void {
    this.pendingDeletionCount = count;
  }

  private loadPendingDeletionCount(): void {
    this.pendingCountSub?.unsubscribe();
    this.pendingCountSub = this.adminService.getAccountDeletionRequests({ status: 'Pending', take: 1 }).subscribe({
      next: page => this.pendingDeletionCount = page.pendingCount,
      error: () => {}
    });
  }
}
