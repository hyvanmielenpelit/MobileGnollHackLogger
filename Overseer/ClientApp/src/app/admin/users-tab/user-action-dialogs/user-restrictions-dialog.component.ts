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
 * Set one account's game upload restrictions: game log uploads and bones uploads and downloads,
 * prefilled from the account. Sign-in stays allowed. A reason is required while anything is turned
 * on. The change is sent from here; the dialog stays open with the server's message when it is refused.
 */
@Component({
  selector: 'app-user-restrictions-dialog',
  standalone: true,
  templateUrl: './user-restrictions-dialog.component.html',
  styleUrl: './user-action-dialog.scss',
  changeDetection: ChangeDetectionStrategy.Eager   // matches every other component here
})
export class UserRestrictionsDialogComponent implements OnDestroy {
  private readonly usersService = inject(AdminUsersService);
  private readonly store = inject(AdminPageStore);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog', { static: true }) dialog!: ElementRef<HTMLDialogElement>;

  /** After the restrictions were saved; the dialog has closed. */
  @Output() readonly done = new EventEmitter<UserActionDone>();

  readonly reasonMax = ADMIN_REASON_MAX;

  user: AdminUser | null = null;
  gameLogBanned = false;
  bonesBanned = false;
  reason = '';
  busy = false;
  error: string | null = null;

  private sub: Subscription | null = null;

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    if (this.dialog.nativeElement.open) this.dialog.nativeElement.close();
  }

  open(user: AdminUser): void {
    this.user = user;
    this.gameLogBanned = user.isGameLogBanned;
    this.bonesBanned = user.isBonesBanned;
    this.reason = '';
    this.busy = false;
    this.error = null;
    this.cdr.detectChanges();
    const dialog = this.dialog.nativeElement;
    if (!dialog.open) dialog.showModal();
    dialog.querySelector<HTMLElement>('#uad-restrictions-title')?.focus();
  }

  close(): void {
    if (this.dialog.nativeElement.open) this.dialog.nativeElement.close();
  }

  onClose(): void {
    this.sub?.unsubscribe();
    this.user = null;
    this.busy = false;
  }

  get changed(): boolean {
    const user = this.user;
    return !!user && (user.isGameLogBanned !== this.gameLogBanned || user.isBonesBanned !== this.bonesBanned);
  }

  get reasonRequired(): boolean {
    return this.gameLogBanned || this.bonesBanned;
  }

  get missingNote(): string | null {
    if (!this.changed) return 'Nothing has changed yet.';
    return missingText(this.reasonRequired && !this.reason.trim() ? ['a reason'] : []);
  }

  get canSubmit(): boolean {
    return this.missingNote === null && !this.busy;
  }

  onGameLog(event: Event): void {
    this.gameLogBanned = (event.target as HTMLInputElement).checked;
  }

  onBones(event: Event): void {
    this.bonesBanned = (event.target as HTMLInputElement).checked;
  }

  onReason(event: Event): void {
    this.reason = (event.target as HTMLTextAreaElement).value;
  }

  /** Does nothing until something changed and, while anything is on, a reason is in. */
  submit(): void {
    const user = this.user;
    if (!user || !this.canSubmit) return;
    const reason = this.reason.trim();
    this.busy = true;
    this.error = null;
    this.sub?.unsubscribe();
    this.sub = this.usersService.setGameRestrictions(user.id, {
      gameLogBanned: this.gameLogBanned,
      bonesBanned: this.bonesBanned,
      ...(reason ? { reason } : {})
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
