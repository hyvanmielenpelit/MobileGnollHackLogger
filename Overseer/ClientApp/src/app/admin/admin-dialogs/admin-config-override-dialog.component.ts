import { Component, ChangeDetectionStrategy, ElementRef, EventEmitter, Output, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminService } from '../../services/admin.service';

/** "Chat & Title Generation": the roles a model role bit mask grants, as the assignment lists show them. */
export function formatModelRole(role: number): string {
  const roles: string[] = [];
  if ((role & 1) === 1) roles.push('Chat');
  if ((role & 2) === 2) roles.push('Title Generation');
  if ((role & 4) === 4) roles.push('Benchmark');
  return roles.length > 0 ? roles.join(' & ') : 'None';
}

/** Edits whether a user's or group's configuration assignment is enabled, and the roles it serves. */
@Component({
  selector: 'app-admin-config-override-dialog',
  imports: [FormsModule],
  templateUrl: './admin-config-override-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,   // matches every other component here
  styleUrl: './admin-config-override-dialog.component.scss'
})
export class AdminConfigOverrideDialogComponent {
  private adminService = inject(AdminService);

  @ViewChild('editConfigOverrideDialog', { static: true }) editConfigOverrideDialog!: ElementRef<HTMLDialogElement>;

  /** The assignment as saved, for the caller's list. */
  @Output() saved = new EventEmitter<any>();

  editingOverride: any = null;
  overrideContext: 'user' | 'group' = 'user';

  overrideRoleChat = true;
  overrideRoleTitle = true;
  overrideRoleBenchmark = false;

  get overrideModelRole(): number {
    return (this.overrideRoleChat ? 1 : 0) | (this.overrideRoleTitle ? 2 : 0) | (this.overrideRoleBenchmark ? 4 : 0);
  }

  open(context: 'user' | 'group', assignment: any) {
    this.overrideContext = context;
    this.editingOverride = { ...assignment };
    const role = assignment.modelRole ?? 3;
    this.overrideRoleChat = (role & 1) === 1;
    this.overrideRoleTitle = (role & 2) === 2;
    this.overrideRoleBenchmark = (role & 4) === 4;
    this.editConfigOverrideDialog.nativeElement.showModal();
  }

  closeEditOverride() {
    this.editConfigOverrideDialog.nativeElement.close();
    this.editingOverride = null;
  }

  saveOverride() {
    if (!this.editingOverride || this.overrideModelRole === 0) return;

    const payload = {
      ...this.editingOverride,
      isEnabled: this.editingOverride.isEnabled,
      modelRole: this.overrideModelRole
    };

    const request = this.overrideContext === 'user'
      ? this.adminService.updateUserSystemConfig(this.editingOverride.id, payload)
      : this.adminService.updateGroupSystemConfig(this.editingOverride.id, payload);

    request.subscribe({
      next: () => {
        this.saved.emit({ ...this.editingOverride, ...payload });
        this.closeEditOverride();
      },
      error: () => {
        this.closeEditOverride();
      }
    });
  }
}
