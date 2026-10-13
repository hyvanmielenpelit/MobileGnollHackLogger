import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output
} from '@angular/core';

import { AdminBulkAction, AdminBulkOptions, AdminDangerLevel } from '../../../services/admin-users.service';
import { DateFieldComponent } from '../../../shared/date-field/date-field.component';
import { addDays, isDay, todayUtc } from '../../../shared/date-field/date-calendar';
import { ADMIN_REASON_MAX } from '../user-action-dialogs/user-action-dialogs';
import { formatUtcDateTime } from '../user-status/admin-user-status';

/** How long a bulk lockout lasts: a fixed span from now, or until a chosen day. */
export type BulkLockoutDuration = '1h' | '24h' | '7d' | '30d' | 'date';

/** The options step 2 collects; which apply depends on the action. */
export interface BulkActionOptions {
  /** Lock out only. */
  duration: BulkLockoutDuration;
  /** Lock out with `date` only: `YYYY-MM-DD`. */
  dateValue: string;
  /** Lock out, ban and delete: required, admin-only. */
  reason: string;
}

export const DEFAULT_BULK_OPTIONS: BulkActionOptions = { duration: '24h', dateValue: '', reason: '' };

/** One action card of step 2, and the words the later steps use for it. */
export interface BulkActionInfo {
  action: AdminBulkAction;
  /** The card's title: `Delete accounts`. */
  title: string;
  danger: AdminDangerLevel;
  /** What happens and what is always protected, in one line. */
  line: string;
  /** Whether step 2 asks for a reason. */
  needsReason: boolean;
  /** The run button's verb: `Delete` in `Delete 37 Accounts`. */
  buttonVerb: string;
  /** `will be deleted`, for the review summary. */
  willBe: string;
  /** `Deleted`, for the results summary. */
  past: string;
  /** `Deleting`, for the progress line. */
  progressive: string;
}

/** The six actions, in the order step 2 shows them. */
export const BULK_ACTIONS: readonly BulkActionInfo[] = [
  {
    action: 'Delete', title: 'Delete accounts', danger: 'danger', needsReason: true,
    line: 'Permanently erases accounts that hold no data. Never touches administrators, your own account, '
      + 'accounts with a deletion hold or request, game or Overseer data, a sign-in within 90 days or a registration within 7 days.',
    buttonVerb: 'Delete', willBe: 'will be deleted', past: 'Deleted', progressive: 'Deleting'
  },
  {
    action: 'LockOut', title: 'Lock out', danger: 'caution', needsReason: true,
    line: 'Blocks sign-in on the website, in Overseer and in GnollHack until a date. Administrators and your own account are never included.',
    buttonVerb: 'Lock Out', willBe: 'will be locked out', past: 'Locked out', progressive: 'Locking out'
  },
  {
    action: 'Unlock', title: 'Unlock', danger: 'safe', needsReason: false,
    line: 'Ends lockouts, automatic ones included, and resets failed sign-ins. Accounts that are not locked out are left as they are.',
    buttonVerb: 'Unlock', willBe: 'will be unlocked', past: 'Unlocked', progressive: 'Unlocking'
  },
  {
    action: 'Ban', title: 'Ban', danger: 'caution', needsReason: true,
    line: 'Blocks sign-in everywhere until unbanned. Administrators and your own account are never included.',
    buttonVerb: 'Ban', willBe: 'will be banned', past: 'Banned', progressive: 'Banning'
  },
  {
    action: 'Unban', title: 'Unban', danger: 'safe', needsReason: false,
    line: 'Lets banned accounts sign in again. Accounts that are not banned are left as they are.',
    buttonVerb: 'Unban', willBe: 'will be unbanned', past: 'Unbanned', progressive: 'Unbanning'
  },
  {
    action: 'SignOutEverywhere', title: 'Sign out everywhere', danger: 'safe', needsReason: false,
    line: 'Ends every signed-in session within 5 minutes; the accounts can sign in again at once. Administrators and your own account are never included.',
    buttonVerb: 'Sign Out', willBe: 'will be signed out', past: 'Signed out', progressive: 'Signing out'
  }
];

export function bulkActionInfo(action: AdminBulkAction): BulkActionInfo {
  return BULK_ACTIONS.find(info => info.action === action) ?? BULK_ACTIONS[0];
}

/** `Safe`, `Caution` or `Danger — permanent`: a danger tag's word. */
export function dangerTagLabel(danger: AdminDangerLevel): string {
  return danger === 'danger' ? 'Danger — permanent' : danger === 'caution' ? 'Caution' : 'Safe';
}

/** A danger tag's tone, as `.gh-account-badge[data-tone]` takes it. */
export function dangerTone(danger: AdminDangerLevel): 'ok' | 'warning' | 'danger' {
  return danger === 'danger' ? 'danger' : danger === 'caution' ? 'warning' : 'ok';
}

/** `Delete 37 Accounts`, `Unlock 1 Account`: the run button's label. */
export function bulkRunLabel(action: AdminBulkAction, count: number): string {
  return `${bulkActionInfo(action).buttonVerb} ${count} Account${count === 1 ? '' : 's'}`;
}

const HOUR_MS = 3_600_000;

/** The first and last day a lockout may end on. */
export function bulkLockoutDayRange(now: Date, maxDays: number): { min: string; max: string } {
  const today = todayUtc(now);
  return { min: addDays(today, 1), max: addDays(today, maxDays) };
}

/** When a lockout with these options ends; null while the chosen day is missing or out of range. */
export function bulkLockoutUntil(options: BulkActionOptions, now: Date, maxDays: number): Date | null {
  const at = now.getTime();
  switch (options.duration) {
    case '1h': return new Date(at + HOUR_MS);
    case '24h': return new Date(at + 24 * HOUR_MS);
    case '7d': return new Date(at + 7 * 24 * HOUR_MS);
    case '30d': return new Date(at + 30 * 24 * HOUR_MS);
    default: {
      const range = bulkLockoutDayRange(now, maxDays);
      const day = options.dateValue.trim();
      if (!isDay(day) || day < range.min || day > range.max) return null;
      return new Date(`${day}T00:00:00Z`);
    }
  }
}

/** What step 2 still needs before the preview can run: `an action`, `a valid date`, `a reason`. */
export function bulkActionMissing(action: AdminBulkAction | null, options: BulkActionOptions, now: Date, maxDays: number): string[] {
  if (!action) return ['an action'];
  const missing: string[] = [];
  if (action === 'LockOut' && !bulkLockoutUntil(options, now, maxDays)) missing.push('a valid date');
  if (bulkActionInfo(action).needsReason && !options.reason.trim()) missing.push('a reason');
  return missing;
}

/** The preview request's options: the lockout's end and the reason, where the action takes them. */
export function bulkPreviewOptions(action: AdminBulkAction, options: BulkActionOptions, now: Date, maxDays: number): AdminBulkOptions {
  const result: AdminBulkOptions = {};
  if (action === 'LockOut') {
    const until = bulkLockoutUntil(options, now, maxDays);
    if (until) result.until = until.toISOString();
  }
  if (bulkActionInfo(action).needsReason) {
    const reason = options.reason.trim();
    if (reason) result.reason = reason;
  }
  return result;
}

/** What the action does to each account, for the review's summary. */
export function bulkEffectText(action: AdminBulkAction, until: string | null): string {
  switch (action) {
    case 'Delete':
      return 'Each account and everything it holds is erased.';
    case 'LockOut': {
      const when = formatUtcDateTime(until) || 'the chosen date';
      return `Cannot sign in to the website, Overseer or GnollHack online features until ${when}. Signed-in sessions end within 5 minutes.`;
    }
    case 'Ban':
      return 'Cannot sign in anywhere until unbanned. Signed-in sessions end within 5 minutes. The reason is not shown to the users.';
    case 'Unlock':
      return 'Can sign in again on the website, in Overseer and in GnollHack. Failed sign-ins are reset.';
    case 'Unban':
      return 'Can sign in again on the website, in Overseer and in GnollHack.';
    case 'SignOutEverywhere':
    default:
      return 'Every signed-in session ends within 5 minutes. The accounts can sign in again at once.';
  }
}

/**
 * Step 2 of the Bulk Actions wizard: one of six actions as radio cards, each with its danger tag and
 * one line on what happens, and under the chosen card its options: a duration and a reason for a
 * lockout, a reason for a ban or a delete. Presentational: the wizard owns the choice and the options,
 * runs the preview and passes its state back in.
 */
@Component({
  selector: 'app-bulk-action-step',
  standalone: true,
  imports: [DateFieldComponent],
  templateUrl: './bulk-action-step.component.html',
  styleUrl: './bulk-action-step.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager   // matches every other component here
})
export class BulkActionStepComponent {
  @Input() action: AdminBulkAction | null = null;
  @Input() options: BulkActionOptions = DEFAULT_BULK_OPTIONS;
  /** The accounts step 1 matched; null while not counted. */
  @Input() matchCount: number | null = null;
  /** The furthest a lockout may reach (the server's `AdminUserManagement:LockoutMaxDays`). */
  @Input() maxLockoutDays = 365;
  /** While the preview runs. */
  @Input() busy = false;
  /** Why the preview was refused, and the server's code. */
  @Input() error: string | null = null;
  @Input() errorCode: string | null = null;

  @Output() readonly actionChange = new EventEmitter<AdminBulkAction>();
  @Output() readonly optionsChange = new EventEmitter<BulkActionOptions>();
  /** *Back to Users*, after a delete was refused for want of a narrowing criterion. */
  @Output() readonly backToUsers = new EventEmitter<void>();

  readonly actions = BULK_ACTIONS;
  readonly reasonMax = ADMIN_REASON_MAX;
  readonly durations: readonly { value: BulkLockoutDuration; label: string }[] = [
    { value: '1h', label: '1 hour' },
    { value: '24h', label: '24 hours' },
    { value: '7d', label: '7 days' },
    { value: '30d', label: '30 days' },
    { value: 'date', label: 'Until a date…' }
  ];

  readonly dangerTagLabel = dangerTagLabel;
  readonly dangerTone = dangerTone;

  /** The clock the lockout's day range counts from. */
  now: () => Date = () => new Date();

  get dayRange(): { min: string; max: string } {
    return bulkLockoutDayRange(this.now(), this.maxLockoutDays);
  }

  get dateError(): string | null {
    if (this.action !== 'LockOut' || this.options.duration !== 'date' || this.options.dateValue === '') return null;
    if (bulkLockoutUntil(this.options, this.now(), this.maxLockoutDays)) return null;
    const range = this.dayRange;
    return `Choose a day from ${range.min} to ${range.max}.`;
  }

  /** `Checking 428 accounts…`. */
  get checkingText(): string {
    const n = this.matchCount;
    return n == null ? 'Checking the matching accounts…' : `Checking ${n.toLocaleString('en-US')} account${n === 1 ? '' : 's'}…`;
  }

  get legendText(): string {
    const n = this.matchCount;
    return n == null
      ? 'What happens to the matching accounts'
      : `What happens to the ${n.toLocaleString('en-US')} matching account${n === 1 ? '' : 's'}`;
  }

  cardId(info: BulkActionInfo): string {
    return `bw-action-${info.action}`;
  }

  choose(action: AdminBulkAction): void {
    if (action !== this.action) this.actionChange.emit(action);
  }

  setDuration(duration: BulkLockoutDuration): void {
    this.optionsChange.emit({ ...this.options, duration });
  }

  onDate(value: string): void {
    this.optionsChange.emit({ ...this.options, dateValue: value.trim() });
  }

  onReason(event: Event): void {
    this.optionsChange.emit({ ...this.options, reason: (event.target as HTMLTextAreaElement).value });
  }
}
