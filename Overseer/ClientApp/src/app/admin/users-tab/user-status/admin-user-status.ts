/**
 * How an account's statuses read in the Users tab, the Bulk Actions wizard and the admin log: each
 * status's word, tone, glyph and tooltip, the dates and the before → after note. Pure, no Angular.
 */

import {
  ADMIN_USER_STATUSES,
  AdminUser,
  AdminUserActionResult,
  AdminUserStatus
} from '../../../services/admin-users.service';
import { parseServerUtcDate } from '../../../utils/date.util';

/** A badge's tone: tokens plus a border, never a color alone (the glyph and the word carry it). */
export type AdminUserBadgeTone = 'ok' | 'info' | 'warning' | 'danger';

/** The 12 px glyph beside a badge's word. */
export type AdminUserBadgeGlyph =
  | 'check-circle'
  | 'lock'
  | 'slash-circle'
  | 'upload-cloud-off'
  | 'message-square-off'
  | 'shield'
  | 'trash'
  | 'mail'
  | 'star';

export interface AdminUserBadge {
  status: AdminUserStatus;
  label: string;
  tone: AdminUserBadgeTone;
  glyph: AdminUserBadgeGlyph;
  /** The facts behind the badge, for its hint tooltip; null when there are none to tell. */
  tooltip: string | null;
}

/**
 * What a badge needs: the statuses, and whatever facts the source has. The list's `AdminUser` has all
 * of them; a bulk preview item has the statuses only, so its badges carry no tooltip.
 */
export type AdminUserBadgeSource = Pick<AdminUser, 'statuses'> & Partial<Omit<AdminUser, 'statuses'>>;

const STATUS_LABELS: Readonly<Record<AdminUserStatus, string>> = {
  GoodStanding: 'In good standing',
  LockedOut: 'Locked out',
  Banned: 'Banned',
  GameLogBanned: 'Game log uploads banned',
  BonesBanned: 'Bones banned',
  AiChatBlocked: 'Blocked from AI chat',
  DeletionHold: 'Deletion hold',
  DeletionPending: 'Deletion pending',
  EmailUnconfirmed: 'Email not confirmed',
  Administrator: 'Administrator'
};

const STATUS_TONES: Readonly<Record<AdminUserStatus, AdminUserBadgeTone>> = {
  GoodStanding: 'ok',
  LockedOut: 'danger',
  Banned: 'danger',
  GameLogBanned: 'warning',
  BonesBanned: 'warning',
  AiChatBlocked: 'warning',
  DeletionHold: 'info',
  DeletionPending: 'warning',
  EmailUnconfirmed: 'info',
  Administrator: 'info'
};

const STATUS_GLYPHS: Readonly<Record<AdminUserStatus, AdminUserBadgeGlyph>> = {
  GoodStanding: 'check-circle',
  LockedOut: 'lock',
  Banned: 'slash-circle',
  GameLogBanned: 'upload-cloud-off',
  BonesBanned: 'upload-cloud-off',
  AiChatBlocked: 'message-square-off',
  DeletionHold: 'shield',
  DeletionPending: 'trash',
  EmailUnconfirmed: 'mail',
  Administrator: 'star'
};

/** The badge word of a status: `In good standing`, `Locked out`. */
export function statusLabel(status: AdminUserStatus): string {
  return STATUS_LABELS[status] ?? status;
}

export function statusTone(status: AdminUserStatus): AdminUserBadgeTone {
  return STATUS_TONES[status] ?? 'info';
}

/** `2026-10-20 15:00 UTC`; empty for a missing or unreadable value. */
export function formatUtcDateTime(utc: string | null | undefined): string {
  const date = parse(utc);
  if (!date) return '';
  return `${formatUtcDay(utc)} ${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())} UTC`;
}

/** `2026-10-20` (UTC); empty for a missing or unreadable value. */
export function formatUtcDay(utc: string | null | undefined): string {
  const date = parse(utc);
  if (!date) return '';
  return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
}

/** `just now`, `5 minutes ago`, `3 hours ago`, `3 days ago`, else `on 2026-03-02`; empty for a missing value. */
export function relativeTime(utc: string | null | undefined, nowUtc: string | null | undefined): string {
  const date = parse(utc);
  if (!date) return '';
  const now = parse(nowUtc) ?? new Date();
  const seconds = Math.max(0, Math.floor((now.getTime() - date.getTime()) / 1000));
  const unit = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'} ago`;
  if (seconds < 60) return 'just now';
  if (seconds < 3600) return unit(Math.floor(seconds / 60), 'minute');
  if (seconds < 86400) return unit(Math.floor(seconds / 3600), 'hour');
  const days = Math.floor(seconds / 86400);
  if (days < 60) return unit(days, 'day');
  return `on ${formatUtcDay(utc)}`;
}

/** The badges of an account, in the fixed status order, each with the facts behind it. */
export function badgesFor(user: AdminUserBadgeSource): AdminUserBadge[] {
  const present = new Set(user.statuses ?? []);
  return ADMIN_USER_STATUSES.filter(status => present.has(status)).map(status => ({
    status,
    label: statusLabel(status),
    tone: statusTone(status),
    glyph: STATUS_GLYPHS[status],
    tooltip: tooltipFor(status, user)
  }));
}

/** True when the account has none of the problems the other statuses name. */
export function goodStanding(user: AdminUserBadgeSource): boolean {
  return (user.statuses ?? []).includes('GoodStanding');
}

/** `In good standing → Locked out until 2026-10-20 15:00 UTC`: the before → after note of an action. */
export function changeNote(result: Pick<AdminUserActionResult, 'before' | 'after'>): string {
  return `${result.before.text} → ${result.after.text}`;
}

/** The statuses an action added: the badges to highlight. */
export function addedStatuses(result: Pick<AdminUserActionResult, 'before' | 'after'>): AdminUserStatus[] {
  const before = new Set(result.before.statuses);
  return result.after.statuses.filter(status => !before.has(status));
}

/** `with a reason` sentence part: ` Reason: …`, or nothing. */
function reasonPart(reason: string | null | undefined, prefix = 'Reason'): string {
  const text = reason?.trim();
  return text ? ` ${prefix}: ${text}` : '';
}

function tooltipFor(status: AdminUserStatus, user: AdminUserBadgeSource): string | null {
  switch (status) {
    case 'LockedOut': {
      if (user.lockoutEnd === undefined) return null;
      const until = formatUtcDateTime(user.lockoutEnd);
      const untilPart = until ? ` until ${until}` : '';
      if (user.lockoutIsAdminSet) {
        return `Locked out by an administrator${untilPart}.${reasonPart(user.adminLockoutReason)}`;
      }
      const failed = user.accessFailedCount ?? 0;
      return failed > 0
        ? `Locked out after ${failed} failed sign-in${failed === 1 ? '' : 's'}${untilPart}.`
        : `Locked out automatically${untilPart}.`;
    }
    case 'Banned': {
      if (user.bannedUtc === undefined && user.banReason === undefined) return null;
      const on = formatUtcDateTime(user.bannedUtc);
      const base = on ? `Banned on ${on}.` : 'Banned before ban dates were recorded.';
      return `${base} No sign-in is allowed anywhere.${reasonPart(user.banReason)}`;
    }
    case 'GameLogBanned':
      return 'GnollHack game log uploads from this account are refused. Sign-in is still allowed.';
    case 'BonesBanned':
      return 'Bones uploads and downloads for this account are refused. Sign-in is still allowed.';
    case 'AiChatBlocked': {
      if (user.overseerBlockedUtc === undefined && user.overseerBlockReason === undefined) return null;
      const on = formatUtcDateTime(user.overseerBlockedUtc);
      return `Blocked from starting AI work in Overseer${on ? ` since ${on}` : ''}.${reasonPart(user.overseerBlockReason, 'Reason shown to the user')}`;
    }
    case 'DeletionHold': {
      if (user.accountHoldPlacedUtc === undefined && user.accountHoldReason === undefined) return null;
      const by = user.accountHoldPlacedByUserName?.trim();
      const on = formatUtcDateTime(user.accountHoldPlacedUtc);
      return `Deletion hold${by ? ` placed by ${by}` : ''}${on ? ` on ${on}` : ''}.${reasonPart(user.accountHoldReason)}`;
    }
    case 'DeletionPending':
      return user.pendingDeletionRequestId != null
        ? 'A deletion request from this user is waiting in Deletion requests.'
        : null;
    case 'Administrator':
      return user.userName !== undefined ? 'Named in the Admins setting.' : null;
    default:
      return null;
  }
}

function parse(utc: string | null | undefined): Date | null {
  if (!utc) return null;
  const date = parseServerUtcDate(utc);
  return isNaN(date.getTime()) ? null : date;
}

function pad(n: number): string {
  return String(n).padStart(2, '0');
}
