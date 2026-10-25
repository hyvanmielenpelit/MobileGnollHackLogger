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
 * Change the email address of one account: the new address, whether to mark it confirmed, a required
 * reason, which the user is never shown, and a check that it is the right account. The change is sent
 * from here; the dialog stays open with the server's message when it is refused.
 */
@Component({
  selector: 'app-user-change-email-dialog',
  standalone: true,
  templateUrl: './user-change-email-dialog.component.html',
  styleUrl: './user-action-dialog.scss',
  changeDetection: ChangeDetectionStrategy.Eager   // matches every other component here
})
export class UserChangeEmailDialogComponent implements OnDestroy {
  private readonly usersService = inject(AdminUsersService);
  private readonly store = inject(AdminPageStore);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog', { static: true }) dialog!: ElementRef<HTMLDialogElement>;

  /** After the change succeeded; the dialog has closed. */
  @Output() readonly done = new EventEmitter<UserActionDone>();

  readonly reasonMax = ADMIN_REASON_MAX;

  user: AdminUser | null = null;
  email = '';
  markConfirmed = false;
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
    this.email = '';
    this.markConfirmed = false;
    this.reason = '';
    this.checked = false;
    this.busy = false;
    this.error = null;
    this.cdr.detectChanges();
    const dialog = this.dialog.nativeElement;
    if (!dialog.open) dialog.showModal();
    dialog.querySelector<HTMLElement>('#uad-email-title')?.focus();
  }

  close(): void {
    if (this.dialog.nativeElement.open) this.dialog.nativeElement.close();
  }

  onClose(): void {
    this.sub?.unsubscribe();
    this.user = null;
    this.busy = false;
  }

  /** What the change does, as the checkbox now stands. */
  get effectText(): string {
    const name = this.user?.userName ?? '';
    return this.markConfirmed
      ? `${name} signs in with the new address confirmed. Signed-in sessions end within 5 minutes. The user name does not change.`
      : 'The new address is emailed instructions for confirming it from the Resend email confirmation page of the account website. '
        + `${name} cannot sign in until it is confirmed. Signed-in sessions end within 5 minutes. The user name does not change.`;
  }

  get missingNote(): string | null {
    const missing: string[] = [];
    const email = this.email.trim();
    if (!email) missing.push('a new address');
    else if (email.toLowerCase() === (this.user?.email ?? '').trim().toLowerCase()) missing.push('an address other than the current one');
    if (!this.reason.trim()) missing.push('a reason');
    if (!this.checked) missing.push('the account check');
    return missingText(missing);
  }

  get canSubmit(): boolean {
    return this.missingNote === null && !this.busy;
  }

  onEmail(event: Event): void {
    this.email = (event.target as HTMLInputElement).value;
  }

  onMarkConfirmed(event: Event): void {
    this.markConfirmed = (event.target as HTMLInputElement).checked;
  }

  onReason(event: Event): void {
    this.reason = (event.target as HTMLTextAreaElement).value;
  }

  onChecked(event: Event): void {
    this.checked = (event.target as HTMLInputElement).checked;
  }

  /** Does nothing until the address, the reason and the check are in (the button is aria-disabled). */
  submit(): void {
    const user = this.user;
    if (!user || !this.canSubmit) return;
    this.busy = true;
    this.error = null;
    this.sub?.unsubscribe();
    this.sub = this.usersService.changeEmail(user.id, {
      email: this.email.trim(),
      markConfirmed: this.markConfirmed,
      reason: this.reason.trim()
    }).subscribe({
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
