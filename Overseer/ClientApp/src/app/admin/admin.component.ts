import { Component, OnInit, inject, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AdminService, UserDto, GroupDto, SystemAiConfigDto, UserSystemAiConfigDto, GroupSystemAiConfigDto } from '../services/admin.service';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
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
  newGroupName = '';

  @ViewChild('configDialog') configDialog!: ElementRef<HTMLDialogElement>;
  editingConfig: any = null;
  isNewConfig = false;

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
    this.createGroupDialog.nativeElement.showModal();
  }

  closeCreateGroup() {
    this.createGroupDialog.nativeElement.close();
  }

  saveCreateGroup() {
    if (!this.newGroupName) return;
    this.adminService.createGroup(this.newGroupName).subscribe(g => {
      this.groups.push(g);
      this.closeCreateGroup();
    });
  }

  deleteGroup(group: GroupDto) {
    if (confirm(`Are you sure you want to delete group ${group.name}?`)) {
      this.adminService.deleteGroup(group.id).subscribe(() => {
        this.groups = this.groups.filter(g => g.id !== group.id);
        this.users.forEach(u => {
          if (u.groups) u.groups = u.groups.filter(g => g.id !== group.id);
        });
      });
    }
  }

  // --- Configs ---
  openCreateConfig() {
    this.isNewConfig = true;
    this.editingConfig = {
      displayName: '',
      provider: 'Anthropic',
      modelId: '',
      isEnabled: true,
      isSystemWide: false,
      orderIndex: 0
    };
    this.configDialog.nativeElement.showModal();
  }

  openEditConfig(config: SystemAiConfigDto) {
    this.isNewConfig = false;
    this.editingConfig = { ...config, apiKey: '' }; // blank apiKey means don't update
    this.configDialog.nativeElement.showModal();
  }

  closeConfig() {
    this.configDialog.nativeElement.close();
    this.editingConfig = null;
  }

  saveConfig() {
    if (this.isNewConfig) {
      this.adminService.createSystemConfig(this.editingConfig).subscribe(c => {
        this.configs.push(c);
        this.closeConfig();
      });
    } else {
      this.adminService.updateSystemConfig(this.editingConfig.id, this.editingConfig).subscribe(c => {
        const idx = this.configs.findIndex(x => x.id === c.id);
        if (idx !== -1) this.configs[idx] = c;
        this.closeConfig();
      });
    }
  }

  deleteConfig(config: SystemAiConfigDto) {
    if (confirm(`Delete config ${config.displayName}?`)) {
      this.adminService.deleteSystemConfig(config.id).subscribe(() => {
        this.configs = this.configs.filter(c => c.id !== config.id);
      });
    }
  }
}
