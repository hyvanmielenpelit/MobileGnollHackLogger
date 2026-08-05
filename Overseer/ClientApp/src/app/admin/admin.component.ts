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

  // Generic Confirm State
  confirmMessage: string = '';
  confirmAction: (() => void) | null = null;
  editingConfig: Partial<SystemAiConfigDto> | null = null;
  isNewConfig = false;
  adminProviders = ['Anthropic', 'Google', 'OpenAI'];
  savingConfig = false;

  newGroupName: string = '';
  createGroupError: string = '';

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

  resetConfig(config: SystemAiConfigDto) {
    this.confirmMessage = `Are you sure you want to reset all rate limits (Daily, Monthly, Total) for the System Config '${config.displayName}'?`;
    this.confirmAction = () => {
      this.adminService.resetSystemConfig(config.id).subscribe({
        next: () => {
          config.dailyRequestsCount = 0;
          config.monthlyRequestsCount = 0;
          config.totalRequestsCount = 0;
        },
        error: (err) => {
          console.error("Failed to reset config rate limits", err);
        }
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
}
