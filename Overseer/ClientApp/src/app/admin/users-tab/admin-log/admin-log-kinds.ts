import type { AdminUserActionKind } from '../../../services/admin-users.service';
import { wordsLabel } from '../../../utils/display-labels.util';

// The words and tones of the admin log's action kinds, shared by the Users tab's Admin log and the Security
// tab's event download. Pure: no Angular, no HTTP.

/** The tone of an action's tag: the word carries the meaning, the outline reinforces it. */
export type AdminLogKindTone = 'ok' | 'info' | 'warning' | 'danger';

export const KIND_LABELS: Readonly<Record<AdminUserActionKind, string>> = {
  LockedOut: 'Locked out',
  Unlocked: 'Unlocked',
  Banned: 'Banned',
  Unbanned: 'Unbanned',
  GameRestrictionsChanged: 'Game restrictions changed',
  SignedOutEverywhere: 'Signed out everywhere',
  EmailConfirmed: 'Email confirmed',
  TwoFactorReset: 'Two-factor reset',
  Deleted: 'Deleted',
  AiChatBlocked: 'Blocked from AI chat',
  AiChatUnblocked: 'Unblocked from AI chat',
  HoldPlaced: 'Deletion hold placed',
  HoldReleased: 'Deletion hold released',
  DeletionRequestApproved: 'Deletion request approved',
  DeletionRequestDeclined: 'Deletion request declined',
  EmailChanged: 'Email changed',
  GameSignInBlockCleared: 'Game sign-in block cleared'
};

export const KIND_TONES: Readonly<Record<AdminUserActionKind, AdminLogKindTone>> = {
  LockedOut: 'danger',
  Unlocked: 'ok',
  Banned: 'danger',
  Unbanned: 'ok',
  GameRestrictionsChanged: 'warning',
  SignedOutEverywhere: 'info',
  EmailConfirmed: 'ok',
  TwoFactorReset: 'warning',
  Deleted: 'danger',
  AiChatBlocked: 'warning',
  AiChatUnblocked: 'ok',
  HoldPlaced: 'warning',
  HoldReleased: 'ok',
  DeletionRequestApproved: 'danger',
  DeletionRequestDeclined: 'info',
  EmailChanged: 'warning',
  GameSignInBlockCleared: 'ok'
};

/** The word of an admin log kind: `Locked out`, `Deletion hold placed`; an unknown kind reads in words. */
export function adminLogKindLabel(kind: string): string {
  return Object.hasOwn(KIND_LABELS, kind) ? KIND_LABELS[kind as AdminUserActionKind] : wordsLabel(kind);
}

/** The tone of an admin log kind; an unknown kind is `info`. */
export function adminLogKindTone(kind: string): AdminLogKindTone {
  return Object.hasOwn(KIND_TONES, kind) ? KIND_TONES[kind as AdminUserActionKind] : 'info';
}
