/** What the account action dialogs share: the result they hand back and the reason limit. */

import { AdminUser, AdminUserActionResult } from '../../../services/admin-users.service';

/** The longest admin-only reason the server accepts for a lockout, a ban, a restriction or a delete. */
export const ADMIN_REASON_MAX = 512;

/** What a dialog emits once its action succeeded; the dialog has closed by then. */
export interface UserActionDone {
  /** The account the action was for, as it was when the dialog opened. */
  user: AdminUser;
  result: AdminUserActionResult;
  /** Delete only: `erased 12 game logs, 2 chats`. */
  erasedText?: string;
}

/** `Still needed: a reason and the account check.`, or null when nothing is missing. */
export function missingText(missing: readonly string[]): string | null {
  if (missing.length === 0) return null;
  const list = missing.length === 1
    ? missing[0]
    : `${missing.slice(0, -1).join(', ')} and ${missing[missing.length - 1]}`;
  return `Still needed: ${list}.`;
}
