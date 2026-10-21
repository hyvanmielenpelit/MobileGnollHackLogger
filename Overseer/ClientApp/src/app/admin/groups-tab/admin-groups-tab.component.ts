import { Component, ChangeDetectionStrategy, ElementRef, OnDestroy, OnInit, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { AdminService, GroupDto } from '../../services/admin.service';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';
import { isImeComposing } from '../../utils/ime.util';
import { AdminPageStore } from '../admin-page.store';
import { AdminConfirmDialogComponent } from '../admin-dialogs/admin-confirm-dialog.component';
import { ConfigAccessDialogComponent } from '../admin-dialogs/config-access-dialog/config-access-dialog.component';

/** The Users tab's Groups sub-panel: the group list, creating and deleting groups, and their configuration assignments. */
@Component({
  selector: 'app-admin-groups-tab',
  imports: [FormsModule, AdminConfirmDialogComponent, ConfigAccessDialogComponent],
  templateUrl: './admin-groups-tab.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-groups-tab.component.scss'
})
export class AdminGroupsTabComponent implements OnInit, OnDestroy {
  private adminService = inject(AdminService);
  readonly store = inject(AdminPageStore);

  @ViewChild('createGroupDialog') createGroupDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild(AdminConfirmDialogComponent, { static: true }) confirmDialog!: AdminConfirmDialogComponent;
  @ViewChild(ConfigAccessDialogComponent, { static: true }) configAccess!: ConfigAccessDialogComponent;

  newGroupName: string = '';
  createGroupError: string = '';

  private manageGroupSub: Subscription | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.takePendingManageGroup();
    this.manageGroupSub = this.store.manageGroupRequested$.subscribe(() => this.takePendingManageGroup());
  }

  ngOnDestroy(): void {
    this.manageGroupSub?.unsubscribe();
  }

  /**
   * Reads and clears the page store's pending manage request and opens the AI configuration access
   * dialog for that group, which needs only the id and the name.
   */
  private takePendingManageGroup(): void {
    const pending = this.store.pendingManageGroup;
    if (!pending) {
      return;
    }
    this.store.pendingManageGroup = null;
    this.configAccess.open({ kind: 'group', id: pending.groupId, name: pending.groupName });
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
        this.store.groups.push(g);
        this.closeCreateGroup();
      },
      error: (err) => {
        this.createGroupError = err.error || 'An error occurred while creating the group.';
      }
    });
  }

  onGroupNameEnter(event: Event) {
    if (isImeComposing(event)) return;
    this.saveCreateGroup();
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
    this.configAccess.open({ kind: 'group', id: group.id, name: group.displayName });
  }
}
