import { Component, OnInit, OnDestroy, inject, ViewChild, ElementRef, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminService, UserDto, GroupDto, SystemAiConfigDto } from '../../services/admin.service';
import { AdminPageStore, AdminUsersSubTab } from '../admin-page.store';
import { AdminRateLimitsDialogComponent } from '../admin-dialogs/admin-rate-limits-dialog.component';
import { AdminConfigOverrideDialogComponent, formatModelRole } from '../admin-dialogs/admin-config-override-dialog.component';
import { AdminConfirmDialogComponent } from '../admin-dialogs/admin-confirm-dialog.component';
import { AdminReasonDialogComponent } from '../admin-dialogs/admin-reason-dialog.component';
import { DeletionRequestsComponent } from './deletion-requests/deletion-requests.component';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';

/** The longest deletion hold reason the server accepts. */
export const ACCOUNT_HOLD_REASON_MAX = 512;

/**
 * The Admin page's Users tab: two sub-tabs, the paged user list with each user's groups,
 * configuration assignments, AI chat block and deletion hold, and the deletion requests.
 */
@Component({
  selector: 'app-admin-users-tab',
  imports: [FormsModule, AdminRateLimitsDialogComponent, AdminConfigOverrideDialogComponent, AdminConfirmDialogComponent, AdminReasonDialogComponent, DeletionRequestsComponent],
  templateUrl: './admin-users-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-users-tab.component.scss'
})
export class AdminUsersTabComponent implements OnInit, OnDestroy {
  private adminService = inject(AdminService);
  private store = inject(AdminPageStore);

  @ViewChild(AdminRateLimitsDialogComponent, { static: true }) rateLimitsDialog!: AdminRateLimitsDialogComponent;
  @ViewChild(AdminConfigOverrideDialogComponent, { static: true }) overrideDialog!: AdminConfigOverrideDialogComponent;
  @ViewChild(AdminConfirmDialogComponent, { static: true }) confirmDialog!: AdminConfirmDialogComponent;
  @ViewChild(AdminReasonDialogComponent, { static: true }) reasonDialog!: AdminReasonDialogComponent;

  /** The sub-tabs, in display order. */
  readonly subTabs: readonly AdminUsersSubTab[] = ['users', 'deletions'];

  /** Pending deletion requests, for the Deletion requests label; null until counted. */
  pendingDeletionCount: number | null = null;

  usersLoading = false;

  users: UserDto[] = [];

  get groups(): GroupDto[] {
    return this.store.groups;
  }

  get configs(): SystemAiConfigDto[] {
    return this.store.configs;
  }

  readonly formatModelRole = formatModelRole;

  // Pagination & Sorting state, kept in the page store so it survives a tab switch
  get page(): number { return this.store.usersQuery.page; }
  set page(value: number) { this.store.usersQuery.page = value; }

  get pageSize(): number { return this.store.usersQuery.pageSize; }
  set pageSize(value: number) { this.store.usersQuery.pageSize = value; }

  totalCount = 0;
  pageSizes = [10, 25, 50, 100];

  get sortColumn(): string { return this.store.usersQuery.sortColumn; }
  set sortColumn(value: string) { this.store.usersQuery.sortColumn = value; }

  get sortOrder(): 'asc' | 'desc' { return this.store.usersQuery.sortOrder; }
  set sortOrder(value: 'asc' | 'desc') { this.store.usersQuery.sortOrder = value; }

  get usernameFilter(): string { return this.store.usersQuery.usernameFilter; }
  set usernameFilter(value: string) { this.store.usersQuery.usernameFilter = value; }

  private filterSubject = new Subject<string>();
  private filterSub?: Subscription;
  private pendingCountSub?: Subscription;
  private manageUserSub?: Subscription;

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  get pageNumbers(): (number | string)[] {
    const total = this.totalPages;
    const current = this.page;
    const sibling = 1;

    // If few enough pages, show them all (up to 7 pages fits in our 7 slots)
    if (total <= 7) {
      return Array.from({ length: total }, (_, i) => i + 1);
    }

    const left = Math.max(current - sibling, 1);
    const right = Math.min(current + sibling, total);
    const showLeftDots = left > 2;
    const showRightDots = right < total - 1;

    if (!showLeftDots && showRightDots) {
      const count = 3 + 2 * sibling;
      return [...Array.from({ length: count }, (_, i) => i + 1), '…', total];
    }
    if (showLeftDots && !showRightDots) {
      const count = 3 + 2 * sibling;
      return [1, '…', ...Array.from({ length: count }, (_, i) => total - count + 1 + i)];
    }
    // Both ellipses
    const mid = Array.from({ length: right - left + 1 }, (_, i) => left + i);
    return [1, '…', ...mid, '…', total];
  }

  onPageNumberClick(p: number | string) {
    if (typeof p === 'number') {
      this.onPageChange(p);
    }
  }

  onPageChange(newPage: number) {
    if (newPage >= 1 && newPage <= this.totalPages && newPage !== this.page) {
      this.page = newPage;
      this.loadUsers();
    }
  }

  onPageSizeChange() {
    this.page = 1;
    this.loadUsers();
  }

  onUsernameFilterChange(val: string) {
    this.filterSubject.next(val);
  }

  sortBy(column: string) {
    if (this.sortColumn === column) {
      this.sortOrder = this.sortOrder === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortColumn = column;
      this.sortOrder = 'asc';
    }
    this.page = 1;
    this.loadUsers();
  }

  @ViewChild('manageGroupsDialog') manageGroupsDialog!: ElementRef<HTMLDialogElement>;
  selectedUser: UserDto | null = null;

  @ViewChild('manageUserConfigsDialog', { static: true }) manageUserConfigsDialog!: ElementRef<HTMLDialogElement>;

  // Config Assignment State
  selectedUserForConfigs: UserDto | null = null;
  userConfigAssignments: any[] = [];

  processingConfigs: number[] = [];

  ngOnInit() {
    ensureOverlayPolyfills();

    this.filterSub = this.filterSubject.pipe(
      debounceTime(400)
    ).subscribe(val => {
      this.usernameFilter = val;
      this.page = 1;
      this.loadUsers();
    });

    if (this.subTab === 'users') {
      this.loadUsers();
    }
    this.loadPendingDeletionCount();

    this.takePendingManageUser();
    this.manageUserSub = this.store.manageUserRequested$.subscribe(() => this.takePendingManageUser());
  }

  ngOnDestroy() {
    this.filterSub?.unsubscribe();
    this.pendingCountSub?.unsubscribe();
    this.manageUserSub?.unsubscribe();
  }

  /**
   * Reads and clears the page store's pending manage request: selects the Users sub-tab and opens
   * Manage User Configs for that user, which needs only the id and the name.
   */
  private takePendingManageUser(): void {
    const pending = this.store.pendingManageUser;
    if (!pending) {
      return;
    }
    this.store.pendingManageUser = null;
    this.selectSubTab('users');
    this.openManageUserConfigs({ id: pending.userId, userName: pending.userName, email: '', groups: [] });
  }

  // --- Sub-tabs ---

  /** The selected sub-tab, kept in the page store so a link can choose it before the tab exists. */
  get subTab(): AdminUsersSubTab {
    return this.store.usersSubTab;
  }

  /** `Users`; `Deletion requests (2 pending)` while requests are pending. */
  subTabLabel(tab: AdminUsersSubTab): string {
    if (tab === 'users') {
      return 'Users';
    }
    const pending = this.pendingDeletionCount ?? 0;
    return pending > 0 ? `Deletion requests (${pending} pending)` : 'Deletion requests';
  }

  /** Selects a sub-tab; the user list reloads on its return, the deletion requests load as they appear. */
  selectSubTab(tab: AdminUsersSubTab): void {
    if (tab === this.subTab) {
      return;
    }
    this.store.usersSubTab = tab;
    if (tab === 'users') {
      this.loadUsers();
    }
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

  loadUsers() {
    this.usersLoading = true;
    this.adminService.getUsers(this.page, this.pageSize, this.usernameFilter, this.sortColumn, this.sortOrder).subscribe({
      next: (res) => {
        this.users = res.rows;
        this.totalCount = res.totalCount;
        this.usersLoading = false;
      },
      error: () => {
        this.usersLoading = false;
      }
    });
  }

  // --- Users & Groups ---
  openManageUserGroups(user: UserDto) {
    this.selectedUser = user;
    this.manageGroupsDialog.nativeElement.showModal();
  }

  closeManageGroups() {
    this.manageGroupsDialog.nativeElement.close();
    this.selectedUser = null;
  }

  isUserInGroup(user: UserDto, groupId: number): boolean {
    return user.groups?.some(g => g.id === groupId) || false;
  }

  toggleUserGroup(user: UserDto, group: GroupDto, event: any) {
    const checked = event.target.checked;
    if (checked) {
      this.adminService.addUserToGroup(user.id, group.id).subscribe({
        next: () => {
          if (!user.groups) user.groups = [];
          user.groups.push(group);
        },
        error: () => {
          event.target.checked = false; // revert on error
        }
      });
    } else {
      this.adminService.removeUserFromGroup(user.id, group.id).subscribe({
        next: () => {
          user.groups = user.groups.filter(g => g.id !== group.id);
        },
        error: () => {
          event.target.checked = true; // revert on error
        }
      });
    }
  }

  // --- Overseer AI chat block ---

  /** The block's tooltip: the action, and the stored reason while blocked. */
  blockTooltip(user: UserDto): string {
    if (!user.isOverseerBlocked) {
      return 'Block from the AI chat';
    }
    return user.overseerBlockReason ? `Unblock from the AI chat. Reason: ${user.overseerBlockReason}` : 'Unblock from the AI chat';
  }

  openBlockUser(user: UserDto) {
    this.reasonDialog.open({
      title: 'Block from the Overseer AI chat',
      message: `${user.userName} keeps signing in, reading their chats, exporting their data, deleting their account and changing their settings, but cannot start any AI work in Overseer. The reason is shown to them.`,
      label: 'Reason',
      maxLength: 256,
      confirmText: 'Block',
      confirmClass: 'btn-gh btn-gh-delete'
    }, reason => this.blockUser(user, reason));
  }

  private blockUser(user: UserDto, reason: string) {
    this.adminService.blockUser(user.id, reason).subscribe({
      next: () => {
        this.store.showToast(`${user.userName} is blocked from the Overseer AI chat.`, 'success', 'User Blocked');
        this.loadUsers();
      },
      error: (err) => {
        this.store.showToast(err?.error?.message || `Could not block ${user.userName}.`, 'error', 'Error');
      }
    });
  }

  openUnblockUser(user: UserDto) {
    this.confirmDialog.open({
      title: 'Unblock from the Overseer AI chat',
      message: `${user.userName} can start AI work in Overseer again.`,
      buttonText: 'Unblock',
      buttonClass: 'btn-gh'
    }, () => this.unblockUser(user));
  }

  private unblockUser(user: UserDto) {
    this.adminService.unblockUser(user.id).subscribe({
      next: () => {
        this.store.showToast(`${user.userName} can use the Overseer AI chat again.`, 'success', 'User Unblocked');
        this.loadUsers();
      },
      error: (err) => {
        this.store.showToast(err?.error?.message || `Could not unblock ${user.userName}.`, 'error', 'Error');
      }
    });
  }

  // --- Deletion hold ---

  /** The hold's tooltip: the action, and the stored reason while held. */
  holdTooltip(user: UserDto): string {
    if (!user.hasAccountHold) {
      return 'Place deletion hold';
    }
    return user.accountHoldReason ? `Release deletion hold. Reason: ${user.accountHoldReason}` : 'Release deletion hold';
  }

  openPlaceHold(user: UserDto) {
    this.reasonDialog.open({
      title: 'Place a deletion hold',
      message: `Deleting ${user.userName}'s account will be held for administrator review. The reason is never shown to the user.`,
      label: 'Reason',
      maxLength: ACCOUNT_HOLD_REASON_MAX,
      confirmText: 'Place Hold',
      confirmClass: 'btn-gh'
    }, reason => this.placeHold(user, reason));
  }

  private placeHold(user: UserDto, reason: string) {
    this.adminService.placeAccountHold(user.id, reason).subscribe({
      next: () => {
        this.store.showToast(`Deleting ${user.userName}'s account is now held for review.`, 'success', 'Deletion Hold Placed');
        this.loadUsers();
      },
      error: (err) => {
        this.store.showToast(err?.error?.message || `Could not place a deletion hold on ${user.userName}.`, 'error', 'Error');
      }
    });
  }

  openReleaseHold(user: UserDto) {
    this.confirmDialog.open({
      title: 'Release the deletion hold',
      message: `${user.userName}'s account is no longer held by an administrator. Releasing the hold does not approve a pending deletion request; decide it in Deletion requests.`,
      buttonText: 'Release Hold',
      buttonClass: 'btn-gh'
    }, () => this.releaseHold(user));
  }

  private releaseHold(user: UserDto) {
    this.adminService.releaseAccountHold(user.id).subscribe({
      next: () => {
        this.store.showToast(`The deletion hold on ${user.userName} is released.`, 'success', 'Deletion Hold Released');
        this.loadUsers();
      },
      error: (err) => {
        this.store.showToast(err?.error?.message || `Could not release the deletion hold on ${user.userName}.`, 'error', 'Error');
      }
    });
  }

  // --- Config Assignments ---

  openManageUserConfigs(user: UserDto) {
    this.selectedUserForConfigs = user;
    this.adminService.getUserSystemConfigs(user.id).subscribe({
      next: (assignments) => {
        this.userConfigAssignments = assignments;
        this.manageUserConfigsDialog.nativeElement.showModal();
      },
      error: () => {}
    });
  }

  closeManageUserConfigs() {
    this.manageUserConfigsDialog.nativeElement.close();
    this.selectedUserForConfigs = null;
    this.userConfigAssignments = [];
  }

  isConfigAssignedToUser(configId: number): boolean {
    return this.userConfigAssignments.some(a => a.systemAiApiConfigurationId === configId);
  }

  getUserAssignment(configId: number) {
    return this.userConfigAssignments.find(a => a.systemAiApiConfigurationId === configId);
  }

  toggleUserConfig(config: SystemAiConfigDto, event: any) {
    const isChecked = event.target.checked;
    const assignment = this.getUserAssignment(config.id);

    if (this.processingConfigs.includes(config.id)) {
      // Revert the visual change because we are ignoring this click
      event.target.checked = !isChecked;
      return;
    }

    if (isChecked) {
      if (!assignment) {
        this.processingConfigs = [...this.processingConfigs, config.id];
        this.adminService.createUserSystemConfig(this.selectedUserForConfigs!.id, {
          systemAiApiConfigurationId: config.id,
          isEnabled: true,
          modelRole: config.modelRole,
          maxResultLength: null,
          maxCallsPerSession: null,
          maxToolIterations: null,
          maxDailyChatRequests: null,
          maxMonthlyChatRequests: null,
          maxTotalChatRequests: null,
          maxDailyTitleRequests: null,
          maxMonthlyTitleRequests: null,
          maxTotalTitleRequests: null,
          maxDailyChatTokens: null,
          maxMonthlyChatTokens: null,
          maxTotalChatTokens: null,
          maxDailyTitleTokens: null,
          maxMonthlyTitleTokens: null,
          maxTotalTitleTokens: null
        }).subscribe({
          next: res => {
            this.userConfigAssignments = [...this.userConfigAssignments, res];
            this.processingConfigs = this.processingConfigs.filter(id => id !== config.id);
          },
          error: () => {
            this.processingConfigs = this.processingConfigs.filter(id => id !== config.id);
            event.target.checked = false; // revert
          }
        });
      }
    } else {
      if (assignment) {
        this.processingConfigs = [...this.processingConfigs, config.id];
        this.adminService.deleteUserSystemConfig(assignment.id).subscribe({
          next: () => {
            this.userConfigAssignments = this.userConfigAssignments.filter(a => a.id !== assignment.id);
            this.processingConfigs = this.processingConfigs.filter(id => id !== config.id);
          },
          error: () => {
            this.processingConfigs = this.processingConfigs.filter(id => id !== config.id);
            event.target.checked = true; // revert
          }
        });
      }
    }
  }

  openUserRateLimits(assignment: any) {
    this.rateLimitsDialog.open('user', assignment);
  }

  openEditUserOverride(assignment: any) {
    this.overrideDialog.open('user', assignment);
  }

  /** Replaces the saved assignment in the open dialog's list. */
  onOverrideSaved(saved: any) {
    const idx = this.userConfigAssignments.findIndex(a => a.id === saved.id);
    if (idx !== -1) {
      this.userConfigAssignments[idx] = saved;
    }
  }
}
