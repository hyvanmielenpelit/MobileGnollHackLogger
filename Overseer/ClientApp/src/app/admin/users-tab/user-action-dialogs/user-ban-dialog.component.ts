import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  OnDestroy,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { Subscription } from 'rxjs';

import { AdminUser, AdminUsersService, adminApiError } from '../../../services/admin-users.service';
import { AdminPageStore } from '../../admin-page.store';
import { ADMIN_REASON_MAX, UserActionDone, missingText } from './user-action-dialogs';

/**
 * Ban one account indefinitely: a required reason, which the user is never shown, and a check that
 * it is the right account. The ban is sent from here; the dialog stays open with the server's
 * message when it is refused.
 */
@Component({
  selector: 'app-user-ban-dialog',
  standalone: true,
  templateUrl: './user-ban-dialog.component.html',
  styleUrl: './user-action-dialog.scss',
  changeDetection: ChangeDetectionStrategy.Eager   // matches every other component here
})
export class UserBanDialogComponent implements OnDestroy {
  private readonly usersService = inject(AdminUsersService);
  private readonly store = inject(AdminPageStore);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog', { static: true }) dialog!: ElementRef<HTMLDialogElement>;

  /** After a ban succeeded; the dialog has closed. */
  @Output() readonly done = new EventEmitter<UserActionDone>();

  readonly reasonMax = ADMIN_REASON_MAX;

  user: AdminUser | null = null;
  reason = '';
  checked = false;
  busy = false;
  error: string | null = null;

  private sub: Subscription | null = null;

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    if (this.dialog.nativeElement.open) this.dialog.nativeElement.close();
  }

  open(user: AdminUser): void {
    this.user = user;
    this.reason = '';
    this.checked = false;
    this.busy = false;
    this.error = null;
    this.cdr.detectChanges();
    const dialog = this.dialog.nativeElement;
    if (!dialog.open) dialog.showModal();
    dialog.querySelector<HTMLElement>('#uad-ban-title')?.focus();
  }

  close(): void {
    if (this.dialog.nativeElement.open) this.dialog.nativeElement.close();
  }

  onClose(): void {
    this.sub?.unsubscribe();
    this.user = null;
    this.busy = false;
  }

  get missingNote(): string | null {
    const missing: string[] = [];
    if (!this.reason.trim()) missing.push('a reason');
    if (!this.checked) missing.push('the account check');
    return missingText(missing);
  }

  get canSubmit(): boolean {
    return this.missingNote === null && !this.busy;
  }

  onReason(event: Event): void {
    this.reason = (event.target as HTMLTextAreaElement).value;
  }

  onChecked(event: Event): void {
    this.checked = (event.target as HTMLInputElement).checked;
  }

  /** Does nothing until the reason and the check are in (the button is aria-disabled). */
  submit(): void {
    const user = this.user;
    if (!user || !this.canSubmit) return;
    this.busy = true;
    this.error = null;
    this.sub?.unsubscribe();
    this.sub = this.usersService.ban(user.id, this.reason.trim()).subscribe({
      next: result => {
        this.busy = false;
        this.close();
        this.done.emit({ user, result });
      },
      error: err => {
        this.busy = false;
        this.error = adminApiError(err).message;
        this.store.showToast(this.error, 'error', 'Error');
        this.cdr.markForCheck();
      }
    });
  }
}
