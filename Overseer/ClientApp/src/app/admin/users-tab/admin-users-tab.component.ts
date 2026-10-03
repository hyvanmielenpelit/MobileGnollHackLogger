import { Component, OnInit, OnDestroy, inject, ViewChild, ElementRef, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminService, UserDto, GroupDto, SystemAiConfigDto } from '../../services/admin.service';
import { AdminPageStore } from '../admin-page.store';
import { AdminRateLimitsDialogComponent } from '../admin-dialogs/admin-rate-limits-dialog.component';
import { AdminConfigOverrideDialogComponent, formatModelRole } from '../admin-dialogs/admin-config-override-dialog.component';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';

/** The Admin page's Users tab: the paged user list, and each user's groups and configuration assignments. */
@Component({
  selector: 'app-admin-users-tab',
  imports: [FormsModule, AdminRateLimitsDialogComponent, AdminConfigOverrideDialogComponent],
  templateUrl: './admin-users-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-users-tab.component.scss'
})
export class AdminUsersTabComponent implements OnInit, OnDestroy {
  private adminService = inject(AdminService);
  private store = inject(AdminPageStore);

  @ViewChild(AdminRateLimitsDialogComponent, { static: true }) rateLimitsDialog!: AdminRateLimitsDialogComponent;
  @ViewChild(AdminConfigOverrideDialogComponent, { static: true }) overrideDialog!: AdminConfigOverrideDialogComponent;

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

  @ViewChild('manageUserConfigsDialog') manageUserConfigsDialog!: ElementRef<HTMLDialogElement>;

  // Config Assignment State
  selectedUserForConfigs: UserDto | null = null;
  userConfigAssignments: any[] = [];

  processingConfigs: number[] = [];

  ngOnInit() {
    this.filterSub = this.filterSubject.pipe(
      debounceTime(400)
    ).subscribe(val => {
      this.usernameFilter = val;
      this.page = 1;
      this.loadUsers();
    });

    this.loadUsers();
  }

  ngOnDestroy() {
    this.filterSub?.unsubscribe();
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
