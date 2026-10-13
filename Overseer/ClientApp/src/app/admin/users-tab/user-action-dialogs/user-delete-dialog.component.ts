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

import {
  AccountDeletionCategory,
  AccountDeletionImpact,
  AdminUser,
  AdminUsersService,
  adminApiError
} from '../../../services/admin-users.service';
import { AdminPageStore } from '../../admin-page.store';
import { ADMIN_REASON_MAX, UserActionDone, missingText } from './user-action-dialogs';

/** `I understand 12 game logs and their top-score entries are erased.` */
export function acknowledgmentText(category: AccountDeletionCategory): string {
  const n = category.count;
  const many = n !== 1;
  switch (category.key) {
    case 'game-logs':
      return `I understand ${n} game log${many ? 's' : ''} and ${many ? 'their' : 'its'} top-score entries are erased.`;
    case 'bones':
      return `I understand ${n} bones file${many ? 's are' : ' is'} deleted.`;
    case 'save-files':
      return `I understand ${n} save-file tracking record${many ? 's are' : ' is'} erased.`;
    case 'chats':
      return `I understand ${n} chat${many ? 's are' : ' is'} erased.`;
    default:
      return `I understand ${n} ${category.label.toLowerCase()} ${many ? 'are' : 'is'} erased.`;
  }
}

/** `erased 12 game logs, 2 chats`: the tombstone's summary of what a deletion removed. */
export function erasedText(impact: AccountDeletionImpact | null): string {
  const count = (key: string) => impact?.categories.find(c => c.key === key)?.count ?? 0;
  const logs = count('game-logs');
  const chats = count('chats');
  return `erased ${logs} game log${logs === 1 ? '' : 's'}, ${chats} chat${chats === 1 ? '' : 's'}`;
}

/**
 * Delete one account permanently. It opens on a spinner while the deletion impact loads. A refused
 * deletion shows only the reasons and Close. Otherwise it lists what is erased, asks for one
 * acknowledgment per important category with data, a reason and the user name typed exactly, and
 * offers *Ban instead*. The deletion is sent from here; a server error stays inline.
 */
@Component({
  selector: 'app-user-delete-dialog',
  standalone: true,
  templateUrl: './user-delete-dialog.component.html',
  styleUrl: './user-action-dialog.scss',
  changeDetection: ChangeDetectionStrategy.Eager   // matches every other component here
})
export class UserDeleteDialogComponent implements OnDestroy {
  private readonly usersService = inject(AdminUsersService);
  private readonly store = inject(AdminPageStore);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog', { static: true }) dialog!: ElementRef<HTMLDialogElement>;

  /** After the account was deleted; the dialog has closed. */
  @Output() readonly done = new EventEmitter<UserActionDone>();
  /** *Ban instead*: the dialog has closed; the host opens the ban dialog for this account. */
  @Output() readonly banInstead = new EventEmitter<AdminUser>();

  readonly reasonMax = ADMIN_REASON_MAX;

  user: AdminUser | null = null;
  impact: AccountDeletionImpact | null = null;
  loading = false;
  loadError: string | null = null;
  /** The important categories acknowledged so far, by key. */
  acknowledged = new Set<string>();
  reason = '';
  typedName = '';
  busy = false;
  error: string | null = null;

  private impactSub: Subscription | null = null;
  private deleteSub: Subscription | null = null;

  ngOnDestroy(): void {
    this.impactSub?.unsubscribe();
    this.deleteSub?.unsubscribe();
    if (this.dialog.nativeElement.open) this.dialog.nativeElement.close();
  }

  open(user: AdminUser): void {
    this.user = user;
    this.impact = null;
    this.loading = true;
    this.loadError = null;
    this.acknowledged = new Set<string>();
    this.reason = '';
    this.typedName = '';
    this.busy = false;
    this.error = null;
    this.cdr.detectChanges();
    const dialog = this.dialog.nativeElement;
    if (!dialog.open) dialog.showModal();
    dialog.querySelector<HTMLElement>('#uad-delete-title')?.focus();

    this.impactSub?.unsubscribe();
    this.impactSub = this.usersService.deletionImpact(user.id).subscribe({
      next: impact => {
        this.impact = impact;
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: err => {
        this.loading = false;
        this.loadError = `What deleting ${user.userName} would erase could not be loaded: ${adminApiError(err).message}`;
        this.cdr.markForCheck();
      }
    });
  }

  close(): void {
    if (this.dialog.nativeElement.open) this.dialog.nativeElement.close();
  }

  onClose(): void {
    this.impactSub?.unsubscribe();
    this.deleteSub?.unsubscribe();
    this.user = null;
    this.impact = null;
    this.busy = false;
  }

  get refused(): boolean {
    return (this.impact?.refusals.length ?? 0) > 0;
  }

  /** The categories that need an acknowledgment: important, with data. */
  get importantCategories(): AccountDeletionCategory[] {
    return (this.impact?.categories ?? []).filter(c => c.important && c.count > 0);
  }

  readonly acknowledgmentText = acknowledgmentText;

  get nameMatches(): boolean {
    const user = this.user;
    return !!user && this.typedName.trim() === user.userName.trim();
  }

  get missingNote(): string | null {
    if (!this.impact) return null;
    const missing: string[] = [];
    if (this.importantCategories.some(c => !this.acknowledged.has(c.key))) missing.push('every acknowledgment');
    if (!this.reason.trim()) missing.push('a reason');
    if (!this.nameMatches) missing.push('the user name typed exactly');
    return missingText(missing);
  }

  get canSubmit(): boolean {
    return !!this.impact && !this.refused && this.missingNote === null && !this.busy;
  }

  isAcknowledged(category: AccountDeletionCategory): boolean {
    return this.acknowledged.has(category.key);
  }

  onAcknowledge(category: AccountDeletionCategory, event: Event): void {
    const next = new Set(this.acknowledged);
    if ((event.target as HTMLInputElement).checked) {
      next.add(category.key);
    } else {
      next.delete(category.key);
    }
    this.acknowledged = next;
  }

  onReason(event: Event): void {
    this.reason = (event.target as HTMLTextAreaElement).value;
  }

  onName(event: Event): void {
    this.typedName = (event.target as HTMLInputElement).value;
  }

  onBanInstead(): void {
    const user = this.user;
    if (!user || this.busy) return;
    this.close();
    this.banInstead.emit(user);
  }

  /** Does nothing until every check passes (the button is aria-disabled). */
  submit(): void {
    const user = this.user;
    const impact = this.impact;
    if (!user || !impact || !this.canSubmit) return;
    this.busy = true;
    this.error = null;
    this.deleteSub?.unsubscribe();
    this.deleteSub = this.usersService.deleteUser(user.id, {
      confirmUserName: this.typedName.trim(),
      reason: this.reason.trim(),
      acknowledgedCategories: this.importantCategories.map(c => c.key)
    }).subscribe({
      next: result => {
        this.busy = false;
        this.close();
        this.done.emit({ user, result, erasedText: erasedText(impact) });
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
