import { Component, OnInit, inject, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AdminService, UserDto, GroupDto, SystemAiConfigDto, UserSystemAiConfigDto, GroupSystemAiConfigDto } from '../services/admin.service';
import { AiModelFormComponent, AiModelFormResult } from '../shared/ai-model-form/ai-model-form.component';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, AiModelFormComponent],
  templateUrl: './admin.component.html',
  styleUrl: './admin.component.scss'
})
export class AdminComponent implements OnInit {
  private adminService = inject(AdminService);
  
  activeTab: 'users' | 'groups' | 'configs' = 'users';
  loading = false;

  users: UserDto[] = [];
  groups: GroupDto[] = [];
  configs: SystemAiConfigDto[] = [];

  // Pagination state
  currentPage = 1;
  itemsPerPage = 10;

  get totalPages(): number {
    return Math.ceil(this.users.length / this.itemsPerPage);
  }

  get paginatedUsers(): UserDto[] {
    const startIndex = (this.currentPage - 1) * this.itemsPerPage;
    return this.users.slice(startIndex, startIndex + this.itemsPerPage);
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

  // Generic Confirm State
  confirmMessage: string = '';
  confirmAction: (() => void) | null = null;
  editingConfig: Partial<SystemAiConfigDto> | null = null;
  isNewConfig = false;
  adminProviders = ['Anthropic', 'Google', 'OpenAI'];
  savingConfig = false;

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
    this.loadData();
  }

  loadData() {
    this.loading = true;
    this.adminService.getUsers().subscribe(u => {
      this.users = u;
      this.adminService.getGroups().subscribe(g => {
        this.groups = g;
        this.adminService.getSystemConfigs().subscribe(c => {
          this.configs = c;
          this.loading = false;
        });
      });
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
      this.adminService.addUserToGroup(user.id, group.id).subscribe(() => {
        if (!user.groups) user.groups = [];
        user.groups.push(group);
      });
    } else {
      this.adminService.removeUserFromGroup(user.id, group.id).subscribe(() => {
        user.groups = user.groups.filter(g => g.id !== group.id);
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
    this.confirmMessage = `Are you sure you want to delete group ${group.displayName}?`;
    this.confirmAction = () => {
      this.adminService.deleteGroup(group.id).subscribe(() => {
        this.groups = this.groups.filter(g => g.id !== group.id);
        this.users.forEach(u => {
          if (u.groups) u.groups = u.groups.filter(g => g.id !== group.id);
        });
      });
    };
    this.confirmDialog.nativeElement.showModal();
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
    this.confirmMessage = `Are you sure you want to delete config ${config.displayName}?`;
    this.confirmAction = () => {
      this.adminService.deleteSystemConfig(config.id).subscribe(() => {
        this.configs = this.configs.filter(c => c.id !== config.id);
      });
    };
    this.confirmDialog.nativeElement.showModal();
  }

  selectedConfigForLimits: any = null;
  limitContext: 'system' | 'user' | 'group' = 'system';
  limitEntityId: number = 0;

  openRateLimitsDialog(context: 'system' | 'user' | 'group', entity: any) {
    this.limitContext = context;
    this.selectedConfigForLimits = entity;
    this.limitEntityId = entity.id;
    this.rateLimitsDialog.nativeElement.showModal();
  }

  closeRateLimitsDialog() {
    this.rateLimitsDialog.nativeElement.close();
    this.selectedConfigForLimits = null;
  }

  resetSingleCounter(counterName: string) {
    this.confirmMessage = `Are you sure you want to reset the counter?`;
    this.confirmAction = () => {
      let req;
      if (this.limitContext === 'system') req = this.adminService.resetSystemConfig(this.limitEntityId, counterName);
      else if (this.limitContext === 'user') req = this.adminService.resetUserSystemConfig(this.limitEntityId, counterName);
      else req = this.adminService.resetGroupSystemConfig(this.limitEntityId, counterName);

      req.subscribe({
        next: () => {
          if (this.selectedConfigForLimits) {
            this.selectedConfigForLimits[counterName] = 0;
          }
        },
        error: (err) => console.error("Failed to reset counter", err)
      });
    };
    this.confirmDialog.nativeElement.showModal();
  }

  // --- Confirm Dialog ---
  closeConfirmDialog() {
    this.confirmDialog.nativeElement.close();
    this.confirmAction = null;
  }

  executeConfirmAction() {
    if (this.confirmAction) {
      this.confirmAction();
    }
    this.closeConfirmDialog();
  }

  // --- Config Assignments ---
  
  openManageUserConfigs(user: UserDto) {
    this.selectedUserForConfigs = user;
    this.adminService.getUserSystemConfigs(user.id).subscribe(assignments => {
      this.userConfigAssignments = assignments;
      this.manageUserConfigsDialog.nativeElement.showModal();
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
    this.adminService.getGroupSystemConfigs(group.id).subscribe(assignments => {
      this.groupConfigAssignments = assignments;
      this.manageGroupConfigsDialog.nativeElement.showModal();
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
    this.editingOverride = {
      id: assignment.id,
      isEnabled: assignment.isEnabled,
      modelRole: assignment.modelRole || 3,
      maxResultLength: assignment.maxResultLength,
      maxCallsPerSession: assignment.maxCallsPerSession,
      maxToolIterations: assignment.maxToolIterations,
      maxDailyChatRequests: assignment.maxDailyChatRequests,
      maxMonthlyChatRequests: assignment.maxMonthlyChatRequests,
      maxTotalChatRequests: assignment.maxTotalChatRequests,
      maxDailyTitleRequests: assignment.maxDailyTitleRequests,
      maxMonthlyTitleRequests: assignment.maxMonthlyTitleRequests,
      maxTotalTitleRequests: assignment.maxTotalTitleRequests,
      maxDailyChatTokens: assignment.maxDailyChatTokens,
      maxMonthlyChatTokens: assignment.maxMonthlyChatTokens,
      maxTotalChatTokens: assignment.maxTotalChatTokens,
      maxDailyTitleTokens: assignment.maxDailyTitleTokens,
      maxMonthlyTitleTokens: assignment.maxMonthlyTitleTokens,
      maxTotalTitleTokens: assignment.maxTotalTitleTokens
    };
    this.editConfigOverrideDialog.nativeElement.showModal();
  }

  formatModelRole(role: number): string {
    switch (role) {
      case 1: return 'Chat Only';
      case 2: return 'Title Generation Only';
      case 3: return 'All (Chat & Title)';
      default: return 'Unknown';
    }
  }

  closeEditOverride() {
    this.editConfigOverrideDialog.nativeElement.close();
    this.editingOverride = null;
  }

  saveOverride() {
    if (!this.editingOverride) return;

    // Convert string limits to numbers or nulls
    const sanitize = (val: any) => {
      if (val === null || val === undefined || val === '') return null;
      return Number(val);
    };

    const payload = {
      isEnabled: this.editingOverride.isEnabled,
      modelRole: Number(this.editingOverride.modelRole),
      maxResultLength: sanitize(this.editingOverride.maxResultLength),
      maxCallsPerSession: sanitize(this.editingOverride.maxCallsPerSession),
      maxToolIterations: sanitize(this.editingOverride.maxToolIterations),
      maxDailyChatRequests: sanitize(this.editingOverride.maxDailyChatRequests),
      maxMonthlyChatRequests: sanitize(this.editingOverride.maxMonthlyChatRequests),
      maxTotalChatRequests: sanitize(this.editingOverride.maxTotalChatRequests),
      maxDailyTitleRequests: sanitize(this.editingOverride.maxDailyTitleRequests),
      maxMonthlyTitleRequests: sanitize(this.editingOverride.maxMonthlyTitleRequests),
      maxTotalTitleRequests: sanitize(this.editingOverride.maxTotalTitleRequests),
      maxDailyChatTokens: sanitize(this.editingOverride.maxDailyChatTokens),
      maxMonthlyChatTokens: sanitize(this.editingOverride.maxMonthlyChatTokens),
      maxTotalChatTokens: sanitize(this.editingOverride.maxTotalChatTokens),
      maxDailyTitleTokens: sanitize(this.editingOverride.maxDailyTitleTokens),
      maxMonthlyTitleTokens: sanitize(this.editingOverride.maxMonthlyTitleTokens),
      maxTotalTitleTokens: sanitize(this.editingOverride.maxTotalTitleTokens)
    };

    if (this.overrideContext === 'user') {
      this.adminService.updateUserSystemConfig(this.editingOverride.id, payload).subscribe(() => {
        const idx = this.userConfigAssignments.findIndex(a => a.id === this.editingOverride.id);
        if (idx !== -1) {
          this.userConfigAssignments[idx] = { ...this.editingOverride, ...payload };
        }
        this.closeEditOverride();
      });
    } else {
      this.adminService.updateGroupSystemConfig(this.editingOverride.id, payload).subscribe(() => {
        const idx = this.groupConfigAssignments.findIndex(a => a.id === this.editingOverride.id);
        if (idx !== -1) {
          this.groupConfigAssignments[idx] = { ...this.editingOverride, ...payload };
        }
        this.closeEditOverride();
      });
    }
  }
}
