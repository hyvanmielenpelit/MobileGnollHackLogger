/**
 * The account actions a user card's Actions popover offers, in its three headed groups, each with
 * the reason it is unavailable. The rules mirror the server's guard, which stays authoritative.
 */

import { AdminUser } from '../../../services/admin-users.service';

/** An action the card asks its host to start. */
export type AdminUserActionId =
  | 'lockout'
  | 'unlock'
  | 'ban'
  | 'unban'
  | 'restrictions'
  | 'block'
  | 'unblock'
  | 'signout'
  | 'confirmEmail'
  | 'resetTwoFactor'
  | 'hold'
  | 'releaseHold'
  | 'delete';

export interface AdminUserActionItem {
  id: AdminUserActionId;
  label: string;
  /** Why the action is unavailable; null when it is available. */
  disabledReason: string | null;
  danger: boolean;
}

export interface AdminUserActionGroup {
  /** `access`, `account` or `deletion`; the heading's id derives from it. */
  key: string;
  heading: string;
  items: AdminUserActionItem[];
}

export const ADMIN_PROTECTED_REASON =
  'Administrators cannot be locked out, banned or deleted here. Remove them from the Admins setting first.';
export const ADMIN_UNCHANGED_REASON =
  'Administrators cannot be changed here. Remove them from the Admins setting first.';
export const HOLD_FIRST_REASON = 'Release the deletion hold first.';
export const PENDING_REQUEST_REASON = 'Decide the pending request in Deletion requests.';

/** The self and administrator refusal of the guard, or null. */
function guard(user: AdminUser, selfReason: string, adminReason: string): string | null {
  if (user.isSelf) return selfReason;
  if (user.isAdministrator) return adminReason;
  return null;
}

function item(id: AdminUserActionId, label: string, disabledReason: string | null, danger = false): AdminUserActionItem {
  return { id, label, disabledReason, danger };
}

/** The popover's groups for one account: Access, Account and Deletion. */
export function accountActionGroups(user: AdminUser): AdminUserActionGroup[] {
  const lockedOut = user.statuses.includes('LockedOut');
  const banned = user.isBanned || user.statuses.includes('Banned');
  const blocked = user.isOverseerBlocked || user.statuses.includes('AiChatBlocked');

  const access: AdminUserActionItem[] = [
    lockedOut
      ? item('unlock', 'Unlock', guard(user, 'You cannot unlock your own account.', ADMIN_UNCHANGED_REASON))
      : item('lockout', 'Lock out…', guard(user, 'You cannot lock out your own account.', ADMIN_PROTECTED_REASON)),
    banned
      ? item('unban', 'Unban', guard(user, 'You cannot unban your own account.', ADMIN_UNCHANGED_REASON))
      : item('ban', 'Ban…', guard(user, 'You cannot ban your own account.', ADMIN_PROTECTED_REASON)),
    item('restrictions', 'Game upload restrictions…', null),
    blocked
      ? item('unblock', 'Unblock from AI chat', null)
      : item('block', 'Block from AI chat…', guard(user, 'You cannot block your own account from the AI chat.',
        'Administrators cannot be blocked from the AI chat. Remove them from the Admins setting first.')),
    item('signout', 'Sign out everywhere', user.isSelf ? null : user.isAdministrator ? ADMIN_UNCHANGED_REASON : null)
  ];

  const account: AdminUserActionItem[] = [];
  if (!user.emailConfirmed) {
    account.push(item('confirmEmail', 'Mark email as confirmed', guard(user, 'You cannot change your own account here.', ADMIN_UNCHANGED_REASON)));
  }
  if (user.twoFactorEnabled) {
    account.push(item('resetTwoFactor', 'Reset two-factor authentication…',
      guard(user, 'You cannot reset your own two-factor authentication here.', ADMIN_UNCHANGED_REASON)));
  }

  const deleteReason = guard(user, 'You cannot delete your own account.', ADMIN_PROTECTED_REASON)
    ?? (user.hasAccountHold ? HOLD_FIRST_REASON : null)
    ?? (user.pendingDeletionRequestId != null ? PENDING_REQUEST_REASON : null);
  const deletion: AdminUserActionItem[] = [
    user.hasAccountHold
      ? item('releaseHold', 'Release deletion hold', null)
      : item('hold', 'Place deletion hold…', null),
    item('delete', 'Delete account…', deleteReason, true)
  ];

  const groups: AdminUserActionGroup[] = [{ key: 'access', heading: 'Access', items: access }];
  if (account.length > 0) {
    groups.push({ key: 'account', heading: 'Account', items: account });
  }
  groups.push({ key: 'deletion', heading: 'Deletion', items: deletion });
  return groups;
}
