import { Component, ChangeDetectionStrategy, ElementRef, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminService, GroupDto, SystemAiConfigDto } from '../../services/admin.service';
import { AdminPageStore } from '../admin-page.store';
import { AdminConfirmDialogComponent } from '../admin-dialogs/admin-confirm-dialog.component';
import { AdminRateLimitsDialogComponent } from '../admin-dialogs/admin-rate-limits-dialog.component';
import { AdminConfigOverrideDialogComponent, formatModelRole } from '../admin-dialogs/admin-config-override-dialog.component';

/** The Admin page's Groups tab: the group list, creating and deleting groups, and their configuration assignments. */
@Component({
  selector: 'app-admin-groups-tab',
  imports: [FormsModule, AdminConfirmDialogComponent, AdminRateLimitsDialogComponent, AdminConfigOverrideDialogComponent],
  templateUrl: './admin-groups-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-groups-tab.component.scss'
})
export class AdminGroupsTabComponent {
  private adminService = inject(AdminService);
  readonly store = inject(AdminPageStore);

  @ViewChild('createGroupDialog') createGroupDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('manageGroupConfigsDialog') manageGroupConfigsDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild(AdminConfirmDialogComponent, { static: true }) confirmDialog!: AdminConfirmDialogComponent;
  @ViewChild(AdminRateLimitsDialogComponent, { static: true }) rateLimitsDialog!: AdminRateLimitsDialogComponent;
  @ViewChild(AdminConfigOverrideDialogComponent, { static: true }) overrideDialog!: AdminConfigOverrideDialogComponent;

  readonly formatModelRole = formatModelRole;

  newGroupName: string = '';
  createGroupError: string = '';

  // Config Assignment State
  selectedGroupForConfigs: GroupDto | null = null;
  groupConfigAssignments: any[] = [];

  processingConfigs: number[] = [];

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
        this.store.groups.push(g);
        this.closeCreateGroup();
      },
      error: (err) => {
        this.createGroupError = err.error || 'An error occurred while creating the group.';
      }
    });
  }

  deleteGroup(group: GroupDto) {
    this.confirmDialog.open(
      {
        title: 'Delete Group',
        message: `Are you sure you want to delete group ${group.displayName}?`,
        buttonText: 'Delete',
        buttonClass: 'btn-gh btn-gh-delete'
      },
      () => {
        this.adminService.deleteGroup(group.id).subscribe({
          next: () => {
            this.store.groups = this.store.groups.filter(g => g.id !== group.id);
          },
          error: () => {}
        });
      }
    );
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

  openEditGroupOverride(assignment: any) {
    this.overrideDialog.open('group', assignment);
  }

  onGroupOverrideSaved(saved: any) {
    const idx = this.groupConfigAssignments.findIndex(a => a.id === saved.id);
    if (idx !== -1) {
      this.groupConfigAssignments[idx] = saved;
    }
  }
}
