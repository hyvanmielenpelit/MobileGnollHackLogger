import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { Subscription } from 'rxjs';

import { AdminUser, AdminUsersService, adminApiError } from '../../../services/admin-users.service';
import { DateFieldComponent } from '../../../shared/date-field/date-field.component';
import { addDays, isDay, todayUtc } from '../../../shared/date-field/date-calendar';
import { AdminPageStore } from '../../admin-page.store';
import { formatUtcDateTime } from '../user-status/admin-user-status';
import { ADMIN_REASON_MAX, UserActionDone, missingText } from './user-action-dialogs';

export type LockoutDuration = '1h' | '24h' | '7d' | '30d' | 'date';

const HOUR_MS = 3_600_000;

/**
 * Lock out one account until a time: a choice of durations or a date at most `maxDays` ahead, a
 * required reason, and a check that it is the right account. The lockout is sent from here; the
 * dialog stays open with the server's message when it is refused.
 */
@Component({
  selector: 'app-user-lockout-dialog',
  standalone: true,
  imports: [DateFieldComponent],
  templateUrl: './user-lockout-dialog.component.html',
  styleUrl: './user-action-dialog.scss',
  changeDetection: ChangeDetectionStrategy.Eager   // matches every other component here
})
export class UserLockoutDialogComponent implements OnDestroy {
  private readonly usersService = inject(AdminUsersService);
  private readonly store = inject(AdminPageStore);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog', { static: true }) dialog!: ElementRef<HTMLDialogElement>;

  /** The furthest a lockout may reach (the server's `AdminUserManagement:LockoutMaxDays`). */
  @Input() maxDays = 365;

  /** After a lockout succeeded; the dialog has closed. */
  @Output() readonly done = new EventEmitter<UserActionDone>();

  readonly reasonMax = ADMIN_REASON_MAX;
  readonly durations: readonly { value: LockoutDuration; label: string }[] = [
    { value: '1h', label: '1 hour' },
    { value: '24h', label: '24 hours' },
    { value: '7d', label: '7 days' },
    { value: '30d', label: '30 days' },
    { value: 'date', label: 'Until a date…' }
  ];

  user: AdminUser | null = null;
  duration: LockoutDuration = '24h';
  dateValue = '';
  reason = '';
  checked = false;
  busy = false;
  error: string | null = null;

  /** The clock the durations count from. */
  now: () => Date = () => new Date();

  private sub: Subscription | null = null;

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    if (this.dialog.nativeElement.open) this.dialog.nativeElement.close();
  }

  open(user: AdminUser): void {
    this.user = user;
    this.duration = '24h';
    this.dateValue = '';
    this.reason = '';
    this.checked = false;
    this.busy = false;
    this.error = null;
    this.cdr.detectChanges();
    const dialog = this.dialog.nativeElement;
    if (!dialog.open) dialog.showModal();
    dialog.querySelector<HTMLElement>('#uad-lockout-title')?.focus();
  }

  close(): void {
    if (this.dialog.nativeElement.open) this.dialog.nativeElement.close();
  }

  onClose(): void {
    this.sub?.unsubscribe();
    this.user = null;
    this.busy = false;
  }

  get minDay(): string {
    return addDays(todayUtc(this.now()), 1);
  }

  get maxDay(): string {
    return addDays(todayUtc(this.now()), this.maxDays);
  }

  /** The lockout's end, or null while the chosen date is missing or out of range. */
  get until(): Date | null {
    const now = this.now().getTime();
    switch (this.duration) {
      case '1h': return new Date(now + HOUR_MS);
      case '24h': return new Date(now + 24 * HOUR_MS);
      case '7d': return new Date(now + 7 * 24 * HOUR_MS);
      case '30d': return new Date(now + 30 * 24 * HOUR_MS);
      default: {
        if (!isDay(this.dateValue) || this.dateValue < this.minDay || this.dateValue > this.maxDay) return null;
        return new Date(`${this.dateValue}T00:00:00Z`);
      }
    }
  }

  get dateError(): string | null {
    if (this.duration !== 'date' || this.dateValue === '' || this.until) return null;
    return `Choose a day from ${this.minDay} to ${this.maxDay}.`;
  }

  get effectText(): string {
    const until = this.until;
    const when = until ? formatUtcDateTime(until.toISOString()) : 'the chosen date';
    return `Cannot sign in to the website, Overseer or GnollHack online features until ${when}. Signed-in sessions end within 5 minutes.`;
  }

  get missingNote(): string | null {
    const missing: string[] = [];
    if (!this.until) missing.push('a valid date');
    if (!this.reason.trim()) missing.push('a reason');
    if (!this.checked) missing.push('the account check');
    return missingText(missing);
  }

  get canSubmit(): boolean {
    return this.missingNote === null && !this.busy;
  }

  setDuration(value: LockoutDuration): void {
    this.duration = value;
  }

  onDate(value: string): void {
    this.dateValue = value.trim();
  }

  onReason(event: Event): void {
    this.reason = (event.target as HTMLTextAreaElement).value;
  }

  onChecked(event: Event): void {
    this.checked = (event.target as HTMLInputElement).checked;
  }

  /** Does nothing until every check passes (the button is aria-disabled). */
  submit(): void {
    const user = this.user;
    const until = this.until;
    if (!user || !until || !this.canSubmit) return;
    this.busy = true;
    this.error = null;
    this.sub?.unsubscribe();
    this.sub = this.usersService.lockOut(user.id, { until: until.toISOString(), reason: this.reason.trim() }).subscribe({
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
