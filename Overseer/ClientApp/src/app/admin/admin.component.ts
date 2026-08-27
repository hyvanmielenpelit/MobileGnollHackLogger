import { Component, OnInit, OnDestroy, inject, ViewChild, ElementRef, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AdminService, UserDto, GroupDto, SystemAiConfigDto, UserSystemAiConfigDto, GroupSystemAiConfigDto, DatabaseStorageMetrics, MaintenanceResult } from '../services/admin.service';
import { AiModelFormComponent, AiModelFormResult } from '../shared/ai-model-form/ai-model-form.component';
import { ConfigAnalyticsComponent } from './config-analytics/config-analytics.component';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';

@Component({
    selector: 'app-admin',
    imports: [CommonModule, FormsModule, RouterModule, AiModelFormComponent, ConfigAnalyticsComponent],
    templateUrl: './admin.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './admin.component.scss'
})
export class AdminComponent implements OnInit, OnDestroy {
  private adminService = inject(AdminService);
  
  activeTab: 'users' | 'groups' | 'configs' | 'database' | 'devtools' = 'users';
  loading = false;
  usersLoading = false;

  // Database Tab state
  storageMetrics: DatabaseStorageMetrics | null = null;
  storageLoading = false;
  maintenanceLoading = false;
  maintenanceDryRun = false;
  inactivityDays = 90;
  toolCallPruneDays = 30;
  lastMaintenanceResult: MaintenanceResult | null = null;

  users: UserDto[] = [];
  groups: GroupDto[] = [];
  configs: SystemAiConfigDto[] = [];
  
  get assignableConfigs(): SystemAiConfigDto[] {
    return this.configs.filter(c => !c.isSystemWide);
  }

  // Pagination & Sorting state
  page = 1;
  pageSize = 10;
  totalCount = 0;
  pageSizes = [10, 25, 50, 100];
  
  sortColumn: string = 'UserName';
  sortOrder: 'asc' | 'desc' = 'asc';

  usernameFilter: string = '';
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

  @ViewChild('createGroupDialog') createGroupDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('configDialog') configDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('confirmDialog') confirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('rateLimitsDialog') rateLimitsDialog!: ElementRef<HTMLDialogElement>;
  
  @ViewChild('manageUserConfigsDialog') manageUserConfigsDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('manageGroupConfigsDialog') manageGroupConfigsDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('editConfigOverrideDialog') editConfigOverrideDialog!: ElementRef<HTMLDialogElement>;
  
  @ViewChild('analyticsDialog') analyticsDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('adminToast') adminToast?: ElementRef<HTMLElement>;
  @ViewChild('maintenanceLoadingDialog') maintenanceLoadingDialog!: ElementRef<HTMLDialogElement>;
  analyticsConfigId: number = 0;
  analyticsConfigName: string = '';

  // Maintenance Loading Modal State
  maintenanceLoadingTitle = 'Executing Operation';
  maintenanceLoadingMessage = 'Please wait while the server processes your request...';

  // Generic Confirm State
  confirmTitle?: string;
  confirmMessage: string = '';
  confirmButtonText?: string;
  confirmButtonClass?: string;
  confirmAction: (() => void) | null = null;
  editingConfig: Partial<SystemAiConfigDto> | null = null;
  isNewConfig = false;
  adminProviders = ['Anthropic', 'Google', 'OpenAI'];
  savingConfig = false;

  chatLimitsMap = [
    { label: 'Daily Requests', countField: 'dailyChatRequestsCount', limitField: 'maxDailyChatRequests', backendCounterName: 'DailyChatRequestsCount' },
    { label: 'Monthly Requests', countField: 'monthlyChatRequestsCount', limitField: 'maxMonthlyChatRequests', backendCounterName: 'MonthlyChatRequestsCount' },
    { label: 'Total Requests', countField: 'totalChatRequestsCount', limitField: 'maxTotalChatRequests', backendCounterName: 'TotalChatRequestsCount' },
    { label: 'Daily Tokens', countField: 'dailyChatTokensCount', limitField: 'maxDailyChatTokens', backendCounterName: 'DailyChatTokensCount' },
    { label: 'Monthly Tokens', countField: 'monthlyChatTokensCount', limitField: 'maxMonthlyChatTokens', backendCounterName: 'MonthlyChatTokensCount' },
    { label: 'Total Tokens', countField: 'totalChatTokensCount', limitField: 'maxTotalChatTokens', backendCounterName: 'TotalChatTokensCount' },
  ];
  titleLimitsMap = [
    { label: 'Daily Requests', countField: 'dailyTitleRequestsCount', limitField: 'maxDailyTitleRequests', backendCounterName: 'DailyTitleRequestsCount' },
    { label: 'Monthly Requests', countField: 'monthlyTitleRequestsCount', limitField: 'maxMonthlyTitleRequests', backendCounterName: 'MonthlyTitleRequestsCount' },
    { label: 'Total Requests', countField: 'totalTitleRequestsCount', limitField: 'maxTotalTitleRequests', backendCounterName: 'TotalTitleRequestsCount' },
    { label: 'Daily Tokens', countField: 'dailyTitleTokensCount', limitField: 'maxDailyTitleTokens', backendCounterName: 'DailyTitleTokensCount' },
    { label: 'Monthly Tokens', countField: 'monthlyTitleTokensCount', limitField: 'maxMonthlyTitleTokens', backendCounterName: 'MonthlyTitleTokensCount' },
    { label: 'Total Tokens', countField: 'totalTitleTokensCount', limitField: 'maxTotalTitleTokens', backendCounterName: 'TotalTitleTokensCount' },
  ];

  newGroupName: string = '';
  createGroupError: string = '';

  // Config Assignment State
  selectedUserForConfigs: UserDto | null = null;
  selectedGroupForConfigs: GroupDto | null = null;
  userConfigAssignments: any[] = [];
  groupConfigAssignments: any[] = [];
  editingOverride: any = null;
  overrideContext: 'user' | 'group' = 'user';

  processingConfigs: number[] = [];

  ngOnInit() {
    this.filterSub = this.filterSubject.pipe(
      debounceTime(400)
    ).subscribe(val => {
      this.usernameFilter = val;
      this.page = 1;
      this.loadUsers();
    });

    this.loadData();
  }
  adminToastMessage = '';
  adminToastTitle = '';
  adminToastType: 'success' | 'error' | 'info' = 'success';
  private adminToastTimeout: any;

  showAdminToast(message: string, type: 'success' | 'error' | 'info' = 'success', title?: string, durationMs = 5000) {
    this.adminToastMessage = message;
    this.adminToastType = type;
    this.adminToastTitle = title || (type === 'success' ? 'Success' : type === 'error' ? 'Error' : 'Notification');

    const toast = this.adminToast?.nativeElement as any || document.getElementById('adminToast');
    if (toast && ('showPopover' in toast || 'show' in toast)) {
      try {
        if (!toast.matches(':popover-open')) {
          toast.showPopover();
        }
      } catch {
        try { toast.showPopover(); } catch {}
      }

      if (this.adminToastTimeout) {
        clearTimeout(this.adminToastTimeout);
      }

      this.adminToastTimeout = setTimeout(() => {
        this.hideAdminToast();
      }, durationMs);
    }
  }

  hideAdminToast() {
    if (this.adminToastTimeout) {
      clearTimeout(this.adminToastTimeout);
      this.adminToastTimeout = null;
    }
    const toast = this.adminToast?.nativeElement as any || document.getElementById('adminToast');
    if (toast && 'hidePopover' in toast) {
      try {
        if (toast.matches(':popover-open')) {
          toast.hidePopover();
        }
      } catch {
        try { toast.hidePopover(); } catch {}
      }
    }
  }

  testChangelogAnimation() {
    localStorage.setItem('overseer_last_seen_changelog', '0.0.0');
    window.dispatchEvent(new Event('changelog_badge_reset'));
    this.showAdminToast('Update badge reset successfully!', 'success', 'Badge Reset');
  }

  triggerFrontendSentryError() {
    this.showAdminToast('Triggering frontend exception...', 'info', 'Sentry Test');
    throw new Error('Sentry Frontend Crash Test triggered by Admin');
  }

  triggerBackendSentryError() {
    this.showAdminToast('Sending backend crash request...', 'info', 'Sentry Test');
    this.adminService.triggerBackendSentryError().subscribe({
      next: () => this.showAdminToast('Backend crash request completed unexpectedly successfully.', 'success', 'Sentry Test'),
      error: () => this.showAdminToast('Backend crash request completed (check Sentry!)', 'info', 'Sentry Test')
    });
  }

  ngOnDestroy() {
    this.filterSub?.unsubscribe();
  }

  openAnalytics(config: SystemAiConfigDto) {
    this.analyticsConfigId = config.id;
    this.analyticsConfigName = config.displayName || config.modelId;
    this.analyticsDialog.nativeElement.showModal();
  }

  closeAnalytics() {
    this.analyticsDialog.nativeElement.close();
    this.analyticsConfigId = 0;
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

  loadData() {
    this.loading = true;
    this.adminService.getUsers(this.page, this.pageSize, this.usernameFilter, this.sortColumn, this.sortOrder).subscribe({
      next: (res) => {
        this.users = res.rows;
        this.totalCount = res.totalCount;
        this.adminService.getGroups().subscribe({
          next: (g) => {
            this.groups = g;
            this.adminService.getSystemConfigs().subscribe({
              next: (c) => {
                this.configs = c;
                this.loading = false;
              },
              error: () => {
                this.loading = false;
              }
            });
          },
          error: () => {
            this.loading = false;
          }
        });
      },
      error: () => {
        this.loading = false;
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

  openCreateGroup() {
    this.newGroupName = '';
    this.createGroupError = '';
    this.createGroupDialog.nativeElement.showModal();
  }

  closeCreateGroup() {
    this.createGroupDialog.nativeElement.close();
  }

  saveCreateGroup() {
    this.newGroupName = this.newGroupName?.trim() || '';
    this.createGroupError = '';
    
    if (!this.newGroupName) {
      this.createGroupError = 'Group name cannot be empty.';
      return;
    }

    const validRegex = /^[a-zA-Z0-9 _\-]+$/;
    if (!validRegex.test(this.newGroupName)) {
      this.createGroupError = "Only letters, numbers, spaces, underscores, and dashes are allowed.";
      return;
    }

    this.adminService.createGroup(this.newGroupName).subscribe({
      next: (g) => {
        this.groups.push(g);
        this.closeCreateGroup();
      },
      error: (err) => {
        this.createGroupError = err.error || 'An error occurred while creating the group.';
      }
    });
  }

  deleteGroup(group: GroupDto) {
    this.openConfirmationModal(
      'Delete Group',
      `Are you sure you want to delete group ${group.displayName}?`,
      () => {
        this.adminService.deleteGroup(group.id).subscribe({
          next: () => {
            this.groups = this.groups.filter(g => g.id !== group.id);
            this.loadUsers(); // Reload users to update their groups correctly
          },
          error: () => {}
        });
      },
      'Delete',
      'btn-gh btn-gh-delete'
    );
  }

  // --- Configs ---
  openCreateConfig() {
    this.isNewConfig = true;
    this.editingConfig = {
      provider: this.adminProviders[0],
      isEnabled: true,
      isSystemWide: false,
      orderIndex: 0,
      modelRole: 3
    };
    this.configDialog.nativeElement.showModal();
  }

  openEditConfig(config: SystemAiConfigDto) {
    this.isNewConfig = false;
    this.editingConfig = { ...config }; 
    this.configDialog.nativeElement.showModal();
  }

  closeConfig() {
    this.configDialog.nativeElement.close();
    this.editingConfig = null;
  }

  onConfigSave(formData: AiModelFormResult) {
    this.savingConfig = true;
    
    // Merge form data with existing config (for things like orderIndex)
    const payload = {
      ...(this.editingConfig || {}),
      ...formData
    };

    if (this.isNewConfig) {
      this.adminService.createSystemConfig(payload).subscribe({
        next: (c) => {
          this.loadData();
          this.savingConfig = false;
          this.closeConfig();
        },
        error: (err) => {
          this.savingConfig = false;
          alert(err.error?.message || 'Error creating config');
        }
      });
    } else {
      this.adminService.updateSystemConfig(payload.id!, payload).subscribe({
        next: (c) => {
          this.loadData();
          this.savingConfig = false;
          this.closeConfig();
        },
        error: (err) => {
          this.savingConfig = false;
          alert(err.error?.message || 'Error updating config');
        }
      });
    }
  }

  deleteConfig(config: SystemAiConfigDto) {
    this.openConfirmationModal(
      'Delete Config',
      `Are you sure you want to delete config ${config.displayName}?`,
      () => {
        this.adminService.deleteSystemConfig(config.id).subscribe({
          next: () => {
            this.configs = this.configs.filter(c => c.id !== config.id);
          },
          error: () => {}
        });
      },
      'Delete',
      'btn-gh btn-gh-delete'
    );
  }

  selectedConfigForLimits: any = null;
  limitContext: 'system' | 'user' | 'group' = 'system';
  limitEntityId: number = 0;
  activeRateLimitTab: 'chat' | 'title' = 'chat';

  openRateLimitsDialog(context: 'system' | 'user' | 'group', entity: any) {
    this.limitContext = context;
    this.selectedConfigForLimits = entity;
    this.limitEntityId = entity.id;
    this.activeRateLimitTab = 'chat';
    this.rateLimitsDialog.nativeElement.showModal();
  }

  closeRateLimitsDialog() {
    this.rateLimitsDialog.nativeElement.close();
    this.selectedConfigForLimits = null;
    this.cancelEditLimit();
  }

  editingLimitField: string | null = null;
  editingLimitValue: number | null = null;

  startEditLimit(limitField: string, currentValue: number | null) {
    this.editingLimitField = limitField;
    this.editingLimitValue = currentValue;
  }

  cancelEditLimit() {
    this.editingLimitField = null;
    this.editingLimitValue = null;
  }

  saveEditLimit() {
    if (!this.editingLimitField || !this.selectedConfigForLimits) return;

    // Use full assignment for user/group, or find full config object for system
    const basePayload = this.limitContext === 'system' 
      ? this.configs.find(c => c.id === this.limitEntityId) 
      : this.selectedConfigForLimits;

    if (!basePayload) return;

    const payload = {
      ...basePayload,
      [this.editingLimitField]: this.editingLimitValue === null || this.editingLimitValue === undefined || (this.editingLimitValue as any) === '' ? null : Number(this.editingLimitValue)
    };

    let req: any;
    if (this.limitContext === 'system') {
      req = this.adminService.updateSystemConfig(this.limitEntityId, payload as any);
    } else if (this.limitContext === 'user') {
      req = this.adminService.updateUserSystemConfig(this.limitEntityId, payload as any);
    } else {
      req = this.adminService.updateGroupSystemConfig(this.limitEntityId, payload as any);
    }

    req.subscribe({
      next: () => {
        this.selectedConfigForLimits[this.editingLimitField!] = payload[this.editingLimitField!];
        
        // Also update the underlying local list if it's a system config
        if (this.limitContext === 'system') {
          const idx = this.configs.findIndex(c => c.id === this.limitEntityId);
          if (idx !== -1) {
            this.configs[idx] = { ...this.configs[idx], ...payload } as any;
          }
        }
        
        this.cancelEditLimit();
      },
      error: (err: any) => console.error("Failed to update limit", err)
    });
  }

  resetSingleCounter(counterName: string, backendCounterName: string) {
    this.openConfirmationModal(
      'Reset Counter',
      `Are you sure you want to reset the counter?`,
      () => {
        let req;
        if (this.limitContext === 'system') req = this.adminService.resetSystemConfig(this.limitEntityId, backendCounterName);
        else if (this.limitContext === 'user') req = this.adminService.resetUserSystemConfig(this.limitEntityId, backendCounterName);
        else req = this.adminService.resetGroupSystemConfig(this.limitEntityId, backendCounterName);

        req.subscribe({
          next: () => {
            if (this.selectedConfigForLimits) {
              this.selectedConfigForLimits[counterName] = 0;
            }
          },
          error: (err) => console.error("Failed to reset counter", err)
        });
      },
      'Reset',
      'btn-gh btn-gh-delete'
    );
  }

  // --- Confirm Dialog ---
  openConfirmationModal(title: string, message: string, action: () => void, btnText = 'Confirm', btnClass = 'btn-gh btn-gh-delete') {
    this.confirmTitle = title;
    this.confirmMessage = message;
    this.confirmAction = action;
    this.confirmButtonText = btnText;
    this.confirmButtonClass = btnClass;
    this.confirmDialog.nativeElement.showModal();
  }

  closeConfirmDialog() {
    this.confirmDialog.nativeElement.close();
    this.confirmAction = null;
    this.confirmTitle = undefined;
    this.confirmButtonText = undefined;
    this.confirmButtonClass = undefined;
  }

  executeConfirmAction() {
    if (this.confirmAction) {
      this.confirmAction();
    }
    this.closeConfirmDialog();
  }

  // --- Maintenance Loading Modal ---
  openLoadingModal(title: string, message: string) {
    this.maintenanceLoadingTitle = title;
    this.maintenanceLoadingMessage = message;
    if (this.maintenanceLoadingDialog?.nativeElement && !this.maintenanceLoadingDialog.nativeElement.open) {
      try {
        this.maintenanceLoadingDialog.nativeElement.showModal();
      } catch {}
    }
  }

  closeLoadingModal() {
    if (this.maintenanceLoadingDialog?.nativeElement?.open) {
      try {
        this.maintenanceLoadingDialog.nativeElement.close();
      } catch {}
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

  openManageGroupConfigs(group: GroupDto) {
    this.selectedGroupForConfigs = group;
    this.adminService.getGroupSystemConfigs(group.id).subscribe({
      next: (assignments) => {
        this.groupConfigAssignments = assignments;
        this.manageGroupConfigsDialog.nativeElement.showModal();
      },
      error: () => {}
    });
  }

  closeManageGroupConfigs() {
    this.manageGroupConfigsDialog.nativeElement.close();
    this.selectedGroupForConfigs = null;
    this.groupConfigAssignments = [];
  }

  isConfigAssignedToGroup(configId: number): boolean {
    return this.groupConfigAssignments.some(a => a.systemAiApiConfigurationId === configId);
  }

  getGroupAssignment(configId: number) {
    return this.groupConfigAssignments.find(a => a.systemAiApiConfigurationId === configId);
  }

  toggleGroupConfig(config: SystemAiConfigDto, event: any) {
    const isChecked = event.target.checked;
    const assignment = this.getGroupAssignment(config.id);
    
    if (this.processingConfigs.includes(config.id)) {
      // Revert the visual change because we are ignoring this click
      event.target.checked = !isChecked;
      return;
    }

    if (isChecked) {
      if (!assignment) {
        this.processingConfigs = [...this.processingConfigs, config.id];
        this.adminService.createGroupSystemConfig(this.selectedGroupForConfigs!.id, {
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
            this.groupConfigAssignments = [...this.groupConfigAssignments, res];
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
        this.adminService.deleteGroupSystemConfig(assignment.id).subscribe({
          next: () => {
            this.groupConfigAssignments = this.groupConfigAssignments.filter(a => a.id !== assignment.id);
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

  openEditUserOverride(assignment: any) {
    this.editingOverride = { ...assignment };
    this.overrideContext = 'user';
    this.editConfigOverrideDialog.nativeElement.showModal();
  }

  openEditGroupOverride(assignment: any) {
    this.overrideContext = 'group';
    this.editingOverride = { ...assignment };
    this.editConfigOverrideDialog.nativeElement.showModal();
  }

  formatModelRole(role: number): string {
    switch (role) {
      case 1: return 'Chat Only';
      case 2: return 'Title Generation Only';
      case 3: return 'Chat & Title';
      default: return 'Unknown';
    }
  }

  formatLevel(level: string | null | undefined): string {
    if (!level) return '';
    return level.charAt(0).toUpperCase() + level.slice(1);
  }

  formatReasoningMode(level: string | null | undefined): string {
    if (!level) return 'Default';
    return level.charAt(0).toUpperCase() + level.slice(1);
  }

  formatReasoningSummary(level: string | null | undefined, provider: string): string {
    if (!level) {
      if (provider === 'Anthropic') {
        return 'Default';
      }
      return 'None';
    }
    return level.charAt(0).toUpperCase() + level.slice(1);
  }

  formatServiceTier(tier: string | null | undefined): string {
    if (!tier) return 'None';
    if (tier.toLowerCase() === 'standard_only') return 'Standard Only';
    return tier.charAt(0).toUpperCase() + tier.slice(1);
  }

  getModelRoleClass(role: number): string {
    switch (role) {
      case 1: return 'badge-role-chat';
      case 2: return 'badge-role-title';
      case 3: return 'badge-role-all';
      default: return '';
    }
  }

  closeEditOverride() {
    this.editConfigOverrideDialog.nativeElement.close();
    this.editingOverride = null;
  }

  saveOverride() {
    if (!this.editingOverride) return;

    const payload = {
      ...this.editingOverride,
      isEnabled: this.editingOverride.isEnabled,
      modelRole: Number(this.editingOverride.modelRole)
    };

    if (this.overrideContext === 'user') {
      this.adminService.updateUserSystemConfig(this.editingOverride.id, payload).subscribe({
        next: () => {
          const idx = this.userConfigAssignments.findIndex(a => a.id === this.editingOverride.id);
          if (idx !== -1) {
            this.userConfigAssignments[idx] = { ...this.editingOverride, ...payload };
          }
          this.closeEditOverride();
        },
        error: () => {
          this.closeEditOverride();
        }
      });
    } else {
      this.adminService.updateGroupSystemConfig(this.editingOverride.id, payload).subscribe({
        next: () => {
          const idx = this.groupConfigAssignments.findIndex(a => a.id === this.editingOverride.id);
          if (idx !== -1) {
            this.groupConfigAssignments[idx] = { ...this.editingOverride, ...payload };
          }
          this.closeEditOverride();
        },
        error: () => {
          this.closeEditOverride();
        }
      });
    }
  }

  // --- Drag and Drop for System Configs ---
  onConfigDragStart(event: DragEvent, index: number) {
    if (event.dataTransfer) {
      event.dataTransfer.setData('text/plain', index.toString());
      event.dataTransfer.effectAllowed = 'move';
      const target = event.target as HTMLElement;
      setTimeout(() => target.classList.add('dragging'), 0);
    }
  }

  onConfigDragEnd(event: DragEvent) {
    const target = event.target as HTMLElement;
    target.classList.remove('dragging');
    const items = document.querySelectorAll('.model-item');
    items.forEach(item => item.classList.remove('drag-over-top', 'drag-over-bottom'));
  }

  onConfigDragOver(event: DragEvent) {
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    const targetItem = (event.target as HTMLElement).closest('.model-item');
    if (targetItem) {
      const rect = targetItem.getBoundingClientRect();
      const midY = rect.top + rect.height / 2;
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
      if (event.clientY < midY) {
        targetItem.classList.add('drag-over-top');
      } else {
        targetItem.classList.add('drag-over-bottom');
      }
    }
  }

  onConfigDragLeave(event: DragEvent) {
    const targetItem = (event.target as HTMLElement).closest('.model-item');
    if (targetItem) {
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
    }
  }

  onConfigDrop(event: DragEvent, dropIndex: number) {
    event.preventDefault();
    const targetItem = (event.target as HTMLElement).closest('.model-item');
    if (targetItem) {
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
    }
    
    if (event.dataTransfer) {
      const dragIndexStr = event.dataTransfer.getData('text/plain');
      if (dragIndexStr !== undefined && dragIndexStr !== '') {
        const dragIndex = parseInt(dragIndexStr, 10);
        if (dragIndex !== dropIndex) {
          const item = this.configs[dragIndex];
          this.configs.splice(dragIndex, 1);
          
          let insertIndex = dropIndex;
          if (targetItem) {
             const rect = targetItem.getBoundingClientRect();
             const midY = rect.top + rect.height / 2;
             if (event.clientY >= midY) {
               insertIndex++;
             }
             if (dragIndex < dropIndex && event.clientY < midY) {
                // Adjustment if dragging downwards but dropping on top half
             } else if (dragIndex < dropIndex) {
               insertIndex--;
             }
          }
          
          this.configs.splice(insertIndex, 0, item);
          this.saveConfigOrder();
        }
      }
    }
  }

  saveConfigOrder() {
    this.savingConfig = true;
    const orderedIds = this.configs.map(c => c.id);
    this.adminService.reorderSystemConfigs(orderedIds).subscribe({
      next: () => {
        this.savingConfig = false;
      },
      error: (err) => {
        console.error("Failed to save config order", err);
        this.savingConfig = false;
      }
    });
  }

  // --- Database Storage & Maintenance Tab ---

  selectTab(tab: 'users' | 'groups' | 'configs' | 'database' | 'devtools') {
    this.activeTab = tab;
    if (tab === 'database') {
      this.loadStorageMetrics();
    }
  }

  loadStorageMetrics(showFeedback = false) {
    this.storageLoading = true;
    this.adminService.getStorageMetrics().subscribe({
      next: (data) => {
        this.storageMetrics = data;
        this.storageLoading = false;
        if (showFeedback) {
          this.showAdminToast('Database storage metrics refreshed.', 'info', 'Metrics Refreshed');
        }
      },
      error: (err) => {
        console.error('Failed to load storage metrics', err);
        this.storageLoading = false;
        if (showFeedback) {
          this.showAdminToast('Failed to load storage metrics: ' + (err.error?.message || err.message), 'error', 'Error');
        }
      }
    });
  }

  runFullMaintenance() {
    const execute = () => {
      this.maintenanceLoading = true;
      this.lastMaintenanceResult = null;
      this.openLoadingModal(
        this.maintenanceDryRun ? 'Previewing Maintenance Pass' : 'Executing Maintenance Pass',
        this.maintenanceDryRun
          ? 'Computing dry-run maintenance metrics...'
          : 'Running full maintenance pass (purging expired trash, inactive sessions, and aged tool results)...'
      );

      this.adminService.runMaintenanceNow({
        dryRun: this.maintenanceDryRun,
        inactivityDays: this.inactivityDays,
        toolCallPruneDays: this.toolCallPruneDays
      }).subscribe({
        next: (res) => {
          this.closeLoadingModal();
          this.maintenanceLoading = false;
          this.lastMaintenanceResult = res;
          const mb = (res.reclaimedDiskBytes / 1024 / 1024).toFixed(2);
          const detail = res.isDryRun
            ? `Dry run identified ${res.softDeletedCount} inactive sessions, ${res.prunedToolResultCount} tool payloads.`
            : `Purged ${res.purgedSessionCount} sessions and ${res.deletedDiskFolderCount} disk folders (${mb} MB reclaimed in ${res.elapsedMilliseconds}ms).`;
          this.showAdminToast(
            detail,
            'success',
            res.isDryRun ? 'Dry Run Completed' : 'Full Maintenance Completed'
          );
          this.loadStorageMetrics();
        },
        error: (err) => {
          this.closeLoadingModal();
          this.maintenanceLoading = false;
          this.showAdminToast(
            'Failed to execute maintenance pass: ' + (err.error?.message || err.message),
            'error',
            'Maintenance Error'
          );
        }
      });
    };

    if (!this.maintenanceDryRun) {
      this.openConfirmationModal(
        'Run Scheduled Maintenance Pass',
        'Are you sure you want to run the full maintenance pass? Expired soft-deleted chats and aged tool calls will be permanently modified.',
        execute,
        'Execute Maintenance',
        'btn-gh btn-primary'
      );
    } else {
      execute();
    }
  }

  purgeAllTrash() {
    this.openConfirmationModal(
      'Purge All Trash Now',
      'Are you sure you want to immediately delete ALL soft-deleted sessions across all users? This action cannot be undone.',
      () => {
        this.maintenanceLoading = true;
        this.openLoadingModal(
          'Purging All Trash',
          'Permanently deleting all soft-deleted sessions and associated disk folders across all users...'
        );
        this.adminService.purgeTrashNow({ dryRun: false }).subscribe({
          next: (res) => {
            this.closeLoadingModal();
            this.maintenanceLoading = false;
            this.lastMaintenanceResult = res;
            const mb = (res.reclaimedDiskBytes / 1024 / 1024).toFixed(2);
            this.showAdminToast(
              `Purged ${res.purgedSessionCount} sessions and ${res.deletedDiskFolderCount} disk folders (${mb} MB reclaimed).`,
              'success',
              'Trash Purged'
            );
            this.loadStorageMetrics();
          },
          error: (err) => {
            this.closeLoadingModal();
            this.maintenanceLoading = false;
            this.showAdminToast(
              'Failed to purge trash: ' + (err.error?.message || err.message),
              'error',
              'Purge Error'
            );
          }
        });
      },
      'Purge All Trash',
      'btn-gh btn-gh-delete'
    );
  }

  purgeInactiveNow() {
    this.openConfirmationModal(
      'Purge Inactive Sessions',
      `Are you sure you want to soft-delete inactive sessions older than ${this.inactivityDays} days?`,
      () => {
        this.maintenanceLoading = true;
        this.openLoadingModal(
          'Purging Inactive Sessions',
          `Soft-deleting inactive sessions older than ${this.inactivityDays} days...`
        );
        this.adminService.purgeInactive({ inactivityDays: this.inactivityDays, dryRun: false }).subscribe({
          next: (res) => {
            this.closeLoadingModal();
            this.maintenanceLoading = false;
            this.showAdminToast(res.message, 'success', 'Inactive Sessions Purged');
            this.loadStorageMetrics();
          },
          error: (err) => {
            this.closeLoadingModal();
            this.maintenanceLoading = false;
            this.showAdminToast(
              'Failed to purge inactive sessions: ' + (err.error?.message || err.message),
              'error',
              'Purge Error'
            );
          }
        });
      },
      'Soft-Delete Inactive',
      'btn-gh btn-secondary'
    );
  }

  pruneToolResultsNow() {
    this.openConfirmationModal(
      'Prune Aged Tool Results',
      `Are you sure you want to prune tool call result payloads older than ${this.toolCallPruneDays} days? Message transcripts will be preserved.`,
      () => {
        this.maintenanceLoading = true;
        this.openLoadingModal(
          'Pruning Tool Call Results',
          `Truncating tool result payloads older than ${this.toolCallPruneDays} days...`
        );
        this.adminService.pruneToolResults({ toolCallPruneDays: this.toolCallPruneDays, dryRun: false }).subscribe({
          next: (res) => {
            this.closeLoadingModal();
            this.maintenanceLoading = false;
            this.showAdminToast(res.message, 'success', 'Tool Results Pruned');
            this.loadStorageMetrics();
          },
          error: (err) => {
            this.closeLoadingModal();
            this.maintenanceLoading = false;
            this.showAdminToast(
              'Failed to prune tool results: ' + (err.error?.message || err.message),
              'error',
              'Pruning Error'
            );
          }
        });
      },
      'Prune Tool Payloads',
      'btn-gh btn-secondary'
    );
  }

  sweepOrphanFoldersNow() {
    this.maintenanceLoading = true;
    this.openLoadingModal(
      'Sweeping Orphan Folders',
      'Scanning and removing unreferenced disk folders...'
    );
    this.adminService.sweepOrphans({ dryRun: false }).subscribe({
      next: (res) => {
        this.closeLoadingModal();
        this.maintenanceLoading = false;
        this.showAdminToast(res.message, 'success', 'Orphan Folders Swept');
        this.loadStorageMetrics();
      },
      error: (err) => {
        this.closeLoadingModal();
        this.maintenanceLoading = false;
        this.showAdminToast(
          'Failed to sweep orphan folders: ' + (err.error?.message || err.message),
          'error',
          'Sweep Error'
        );
      }
    });
  }

  sendDiagnosticEmail() {
    this.maintenanceLoading = true;
    this.openLoadingModal(
      'Sending Diagnostic Report',
      'Generating storage metrics and dispatching diagnostic email...'
    );
    this.adminService.sendReportEmail().subscribe({
      next: (res) => {
        this.closeLoadingModal();
        this.maintenanceLoading = false;
        this.showAdminToast(
          res.message,
          res.success ? 'success' : 'error',
          res.success ? 'Diagnostic Report Sent' : 'Failed to Send Report'
        );
      },
      error: (err) => {
        this.closeLoadingModal();
        this.maintenanceLoading = false;
        this.showAdminToast(
          'Failed to send report email: ' + (err.error?.message || err.message),
          'error',
          'Email Error'
        );
      }
    });
  }
}
