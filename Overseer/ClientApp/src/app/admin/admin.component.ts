import { Component, OnInit, inject, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AdminService, UserDto, GroupDto, SystemAiConfigDto, UserSystemAiConfigDto, GroupSystemAiConfigDto } from '../services/admin.service';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  template: `
    <div class="settings-container gh-main-container">
      <div class="header-row">
        <h2>Admin Dashboard</h2>
        <a routerLink="/chat" queryParamsHandling="preserve" class="nav-back-link">&larr; Back to Chat</a>
      </div>
      
      <div class="settings-body">
        <div class="admin-tabs">
          <button class="admin-tab" [class.admin-tab-active]="activeTab === 'users'" (click)="activeTab = 'users'">Users</button>
          <button class="admin-tab" [class.admin-tab-active]="activeTab === 'groups'" (click)="activeTab = 'groups'">Groups</button>
          <button class="admin-tab" [class.admin-tab-active]="activeTab === 'configs'" (click)="activeTab = 'configs'">System Configs</button>
        </div>

        <div *ngIf="loading" class="loading-state">
          <span class="gh-spinner-small"></span> Loading...
        </div>

        <div *ngIf="!loading" class="tab-content mt-20">
          <!-- USERS TAB -->
          <div *ngIf="activeTab === 'users'">
            <div class="header-row" style="padding: 0 0 15px 0; border: none;">
              <h3 class="m-0">Manage Users</h3>
              <div class="pagination-controls" *ngIf="totalPages > 1">
                <button class="btn-gh btn-gh-small" [disabled]="currentPage === 1" (click)="currentPage = currentPage - 1">&larr; Prev</button>
                <span class="page-info">Page {{ currentPage }} of {{ totalPages }}</span>
                <button class="btn-gh btn-gh-small" [disabled]="currentPage === totalPages" (click)="currentPage = currentPage + 1">Next &rarr;</button>
              </div>
            </div>
            <div class="table-responsive">
              <table class="admin-table modern-table">
                <thead>
                  <tr>
                    <th style="width: 25%;">Username</th>
                    <th style="width: 35%;">Email</th>
                    <th style="width: 25%;">Groups</th>
                    <th style="width: 15%; text-align: right;">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  <tr *ngFor="let user of paginatedUsers">
                    <td>{{ user.userName }}</td>
                    <td class="text-muted">{{ user.email }}</td>
                    <td>
                      <span *ngFor="let g of user.groups" class="group-badge">{{ g.name }}</span>
                      <span *ngIf="!user.groups || user.groups.length === 0" class="text-muted italic">None</span>
                    </td>
                    <td style="text-align: right;">
                      <button class="btn-gh btn-gh-small" (click)="openManageUserGroups(user)">Manage Groups</button>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>

          <!-- GROUPS TAB -->
          <div *ngIf="activeTab === 'groups'">
            <div class="header-row" style="padding: 0 0 15px 0; border: none;">
              <h3 class="m-0">Manage Groups</h3>
              <button class="btn-gh btn-gh-small" (click)="openCreateGroup()">Create Group</button>
            </div>
            <div class="table-responsive">
              <table class="admin-table modern-table">
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>Name</th>
                    <th style="text-align: right;">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  <tr *ngFor="let group of groups">
                    <td>{{ group.id }}</td>
                    <td>{{ group.name }}</td>
                    <td style="text-align: right;">
                      <button class="btn-gh btn-gh-small btn-gh-delete" (click)="deleteGroup(group)">Delete</button>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>

          <!-- CONFIGS TAB -->
          <div *ngIf="activeTab === 'configs'">
            <div class="header-row" style="padding: 0 0 15px 0; border: none;">
              <h3 class="m-0">System AI Configurations</h3>
              <button class="btn-gh btn-gh-small" (click)="openCreateConfig()">Create Config</button>
            </div>
            
            <div class="models-list">
              <div *ngFor="let config of configs; let i = index" class="model-item">
                <div class="model-info">
                  <div class="model-title">
                    <h3>{{ config.displayName || config.modelId }}</h3>
                  </div>
                  <div class="model-details">
                    <div class="mb-4">
                      {{ config.modelId }} <span class="provider-badge ml-8">{{ config.provider }}</span>
                      <span *ngIf="config.isSystemWide" class="badge-system-wide ml-8">System Wide</span>
                      <span *ngIf="!config.isEnabled" class="badge-disabled ml-8">Disabled</span>
                    </div>
                  </div>
                </div>
                <div class="model-actions">
                   <button class="btn-gh btn-gh-small" (click)="openEditConfig(config)">Edit</button>
                   <button class="btn-gh btn-gh-small btn-gh-delete" (click)="deleteConfig(config)">Delete</button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- DIALOGS -->
    <dialog #manageGroupsDialog class="gh-dialog">
      <div class="dialog-content flex-col" *ngIf="selectedUser">
        <h3>Manage Groups for {{ selectedUser.userName }}</h3>
        <div class="mt-20">
          <div *ngFor="let group of groups" class="d-flex align-items-center mb-10">
            <input type="checkbox" [id]="'group_' + group.id" 
                   [checked]="isUserInGroup(selectedUser, group.id)"
                   (change)="toggleUserGroup(selectedUser, group, $event)">
            <label [for]="'group_' + group.id" class="ml-10">{{ group.name }}</label>
          </div>
        </div>
        <div class="dialog-actions mt-auto pt-20">
          <button class="btn-gh btn-gh-cancel" (click)="closeManageGroups()">Close</button>
        </div>
      </div>
    </dialog>

    <dialog #createGroupDialog class="gh-dialog">
      <div class="dialog-content flex-col">
        <h3>Create Group</h3>
        <div class="form-group mt-20">
          <label for="newGroupName">Group Name</label>
          <input type="text" id="newGroupName" [(ngModel)]="newGroupName" class="gh-input w-100">
        </div>
        <div class="dialog-actions mt-auto pt-20">
          <button class="btn-gh btn-gh-cancel" (click)="closeCreateGroup()">Cancel</button>
          <button class="btn-gh" (click)="saveCreateGroup()" [disabled]="!newGroupName">Create</button>
        </div>
      </div>
    </dialog>

    <dialog #configDialog class="gh-dialog">
      <div class="dialog-content flex-col" style="min-width: 500px;" *ngIf="editingConfig">
        <h3>{{ isNewConfig ? 'Create' : 'Edit' }} System AI Config</h3>
        <div class="form-group mt-10">
          <label>Display Name</label>
          <input type="text" [(ngModel)]="editingConfig.displayName" class="gh-input w-100">
        </div>
        <div class="form-group mt-10">
          <label>Provider</label>
          <input type="text" [(ngModel)]="editingConfig.provider" class="gh-input w-100">
        </div>
        <div class="form-group mt-10">
          <label>Model ID</label>
          <input type="text" [(ngModel)]="editingConfig.modelId" class="gh-input w-100">
        </div>
        <div class="form-group mt-10">
          <label>API Key (Base64 AES Encrypted, optional to update)</label>
          <input type="text" [(ngModel)]="editingConfig.apiKey" class="gh-input w-100" placeholder="Enter new API key to update">
        </div>
        <div class="form-group mt-10 d-flex flex-gap-15">
          <label class="d-flex align-items-center">
            <input type="checkbox" [(ngModel)]="editingConfig.isEnabled">
            <span class="ml-10">Is Enabled</span>
          </label>
          <label class="d-flex align-items-center">
            <input type="checkbox" [(ngModel)]="editingConfig.isSystemWide">
            <span class="ml-10">Is System Wide</span>
          </label>
        </div>
        <div class="dialog-actions mt-auto pt-20">
          <button class="btn-gh btn-gh-cancel" (click)="closeConfig()">Cancel</button>
          <button class="btn-gh" (click)="saveConfig()">Save</button>
        </div>
      </div>
    </dialog>
  `,
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
