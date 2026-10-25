import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

/** The route prefix of `AdminUsersController`. */
export const ADMIN_USERS_ENDPOINT = '/api/admin/users';

// --- Statuses and criteria ---

/** A status badge of an account, as the server names it (`AdminUserStatusKind`). */
export type AdminUserStatus =
  | 'GoodStanding'
  | 'LockedOut'
  | 'Banned'
  | 'GameLogBanned'
  | 'BonesBanned'
  | 'AiChatBlocked'
  | 'DeletionHold'
  | 'DeletionPending'
  | 'EmailUnconfirmed'
  | 'Administrator';

/** Every status, in the order the server lists them and the badges show them. */
export const ADMIN_USER_STATUSES: readonly AdminUserStatus[] = [
  'GoodStanding',
  'LockedOut',
  'Banned',
  'GameLogBanned',
  'BonesBanned',
  'AiChatBlocked',
  'DeletionHold',
  'DeletionPending',
  'EmailUnconfirmed',
  'Administrator'
];

export type AdminYesNo = 'yes' | 'no';

/**
 * The criteria shared by the user list, the facet counts and the Bulk Actions wizard. Every field is
 * optional; a missing one means any.
 */
export interface AdminUserCriteria {
  /** User name or email, contains, case-insensitive. */
  search?: string;
  /** Any of these. */
  statuses?: AdminUserStatus[];
  emailConfirmed?: AdminYesNo;
  registeredOlderThanDays?: number;
  registeredWithinDays?: number;
  /** `never`, `olderThanDays:N` or `withinDays:N`. */
  lastSignIn?: string;
  /** Any game log, bones or save-file tracking row. */
  hasGameData?: AdminYesNo;
  /** Any chat, AI usage record or benchmark attribution. */
  hasOverseerData?: AdminYesNo;
}

/** The orders the user list offers; `newest` is the server's default. */
export type AdminUserSort = 'newest' | 'oldest' | 'name' | 'email' | 'signin-recent' | 'signin-oldest';

// --- Users ---

export interface AdminUserGroup {
  id: number;
  displayName: string;
  createdAtUtc?: string;
  userCount?: number;
}

/** The newest accepted version of one legal document. */
export interface AdminTermsAcceptance {
  /** `overseer-terms` or `account-terms`. */
  document: string;
  version: string;
  acceptedUtc: string;
  /** `Web`, `GnollHackApp` or `AccountSite`. */
  source: string;
}

/** One account as the Users tab shows it (`AdminUserDto`). Dates are UTC ISO strings. */
export interface AdminUser {
  id: string;
  userName: string;
  email: string;
  emailConfirmed: boolean;
  groups: AdminUserGroup[];
  /** The status badges, in display order. */
  statuses: AdminUserStatus[];
  statusText: string;
  isAdministrator: boolean;
  /** The account of the administrator viewing the list. */
  isSelf: boolean;
  /** When the account was registered; with `createdUtcIsEstimate` an upper bound ("on or before"). */
  createdUtc: string | null;
  createdUtcIsEstimate: boolean;
  lastSignInUtc: string | null;
  lockoutEnd: string | null;
  /** True while the lockout is the one an administrator set; false for an automatic lockout. */
  lockoutIsAdminSet: boolean;
  adminLockoutReason: string | null;
  accessFailedCount: number;
  twoFactorEnabled: boolean;
  isBanned: boolean;
  bannedUtc: string | null;
  banReason: string | null;
  isGameLogBanned: boolean;
  isBonesBanned: boolean;
  isOverseerBlocked: boolean;
  overseerBlockedUtc: string | null;
  /** Shown to the user while blocked. */
  overseerBlockReason: string | null;
  hasAccountHold: boolean;
  accountHoldReason: string | null;
  accountHoldPlacedUtc: string | null;
  accountHoldPlacedByUserName: string | null;
  pendingDeletionRequestId: number | null;
  gameLogCount: number;
  bonesCount: number;
  chatSessionCount: number;
  junetHackUserName: string | null;
  /** The newest accepted version per document; a document the user never accepted has no entry. */
  termsAcceptances?: AdminTermsAcceptance[];
}

export interface AdminUsersPage {
  totalCount: number;
  rows: AdminUser[];
  /** The server's clock when the page was read. */
  nowUtc: string;
}

/** One facet option's count: `key` is a status name, `yes` / `no`, or `withinDays:N` / `olderThanDays:N` / `never`. */
export interface AdminFacetCount {
  key: string;
  count: number;
}

/** A count per filter option, under the search only. */
export interface AdminUserFacetCounts {
  total: number;
  statuses: AdminFacetCount[];
  emailConfirmed: AdminFacetCount[];
  registered: AdminFacetCount[];
  lastSignIn: AdminFacetCount[];
  hasGameData: AdminFacetCount[];
  hasOverseerData: AdminFacetCount[];
}

export interface AdminUserStatusSet {
  statuses: AdminUserStatus[];
  /** `In good standing`, or the statuses joined, with the lockout's end. `Deleted` once deleted. */
  text: string;
}

/** What a per-user action did: the status before and after, and the refreshed account (null once deleted). */
export interface AdminUserActionResult {
  user: AdminUser | null;
  before: AdminUserStatusSet;
  after: AdminUserStatusSet;
  summary: string;
  deleted: boolean;
}

export interface AdminLockoutRequest {
  /** An ISO date-time in the future, at most `LockoutMaxDays` ahead. */
  until: string;
  reason: string;
}

export interface AdminGameRestrictionsRequest {
  gameLogBanned: boolean;
  bonesBanned: boolean;
  reason?: string;
}

export interface AdminChangeEmailRequest {
  email: string;
  markConfirmed: boolean;
  reason: string;
}

// --- Deletion ---

export interface AdminRefusal {
  /** `self`, `administrator`, `hold`, `pending-request` or `erasure-paths`. */
  code: string;
  message: string;
}

export interface AccountDeletionCategory {
  /** `game-logs`, `bones`, `save-files`, `chats`, `ai-usage`, `security-events`, `groups`, `configurations`, `benchmark-records`. */
  key: string;
  label: string;
  count: number;
  /** Must be acknowledged before deletion when the count is above zero. */
  important: boolean;
  /** What deletion does to these rows. */
  note?: string | null;
}

/** What deleting the account would erase, and why it is refused, if it is. */
export interface AccountDeletionImpact {
  userId: string;
  userName: string;
  refusals: AdminRefusal[];
  categories: AccountDeletionCategory[];
}

export interface AdminDeleteUserRequest {
  /** The account's user name, typed exactly. */
  confirmUserName: string;
  reason: string;
  /** The keys of exactly the important categories with a count above zero. */
  acknowledgedCategories: string[];
}

// --- Bulk operations ---

export type AdminBulkAction = 'Delete' | 'LockOut' | 'Unlock' | 'Ban' | 'Unban' | 'SignOutEverywhere';

export type AdminDangerLevel = 'safe' | 'caution' | 'danger';

export interface AdminBulkOptions {
  /** Lock out only: an ISO date-time. */
  until?: string;
  reason?: string;
}

export interface AdminBulkPreviewRequest {
  action: AdminBulkAction;
  options: AdminBulkOptions;
  criteria: AdminUserCriteria;
}

export interface AdminBulkPreviewItem {
  userId: string;
  userName: string;
  email: string;
  statuses: AdminUserStatus[];
  statusText: string;
  createdUtc: string | null;
  createdUtcIsEstimate: boolean;
  lastSignInUtc: string | null;
  isAdvanced: boolean;
}

/** Accounts an operation refuses for one reason: the count and up to 50 example user names. */
export interface AdminBulkRefusedGroup {
  /** `self`, `administrator`, `hold`, `pending-request`, `game-data`, `overseer-data`, `recent-sign-in`, `too-new`. */
  code: string;
  message: string;
  count: number;
  exampleUserNames: string[];
}

export interface AdminBulkPreview {
  operationId: number;
  action: AdminBulkAction;
  expiresUtc: string;
  dangerLevel: AdminDangerLevel;
  /** The acknowledgment keys the execute request must carry (plus `advanced` with the advanced accounts). */
  requiredAcknowledgments: string[];
  matchedCount: number;
  eligibleCount: number;
  advancedCount: number;
  refusedCount: number;
  noOpCount: number;
  /** Eligible and advanced accounts beyond the cap, which this operation does not take. */
  truncatedCount: number;
  maxUsers: number;
  eligible: AdminBulkPreviewItem[];
  advanced: AdminBulkPreviewItem[];
  refused: AdminBulkRefusedGroup[];
  /** Delete only: the data the eligible accounts hold (zero by construction). */
  dataTotals: AccountDeletionCategory[] | null;
  /** Delete only: the phrase with nothing excluded; recompute it with `bulkConfirmationPhrase`. */
  requiredPhrase: string | null;
}

export interface AdminBulkExecuteRequest {
  excludedUserIds: string[];
  includeAdvanced: boolean;
  /** `reviewed`, `irreversible`, `advanced`. */
  acknowledgments: string[];
  confirmationPhrase: string;
}

export type AdminBulkOperationStatus =
  | 'Previewed'
  | 'Queued'
  | 'Running'
  | 'Completed'
  | 'CancelRequested'
  | 'Canceled'
  | 'Interrupted'
  | 'Expired';

export type AdminBulkItemOutcome = 'Pending' | 'Changed' | 'Skipped' | 'Failed';

/** The `outcome` filter of `GET bulk/{opId}`. */
export type AdminBulkItemFilter = 'pending' | 'changed' | 'skipped' | 'failed' | 'excluded';

export interface AdminBulkItem {
  id: number;
  userId: string;
  userName: string;
  isAdvanced: boolean;
  excluded: boolean;
  outcome: AdminBulkItemOutcome;
  message: string | null;
  beforeStatus: string | null;
  afterStatus: string | null;
  processedUtc: string | null;
}

export interface AdminBulkOperation {
  id: number;
  action: AdminBulkAction;
  status: AdminBulkOperationStatus;
  actorUserName: string;
  dangerLevel: AdminDangerLevel;
  reason: string | null;
  until: string | null;
  /** Why the run stopped early, when it did. */
  stopMessage: string | null;
  createdUtc: string;
  expiresUtc: string;
  startedUtc: string | null;
  completedUtc: string | null;
  matchedCount: number;
  eligibleCount: number;
  refusedCount: number;
  changedCount: number;
  skippedCount: number;
  failedCount: number;
  /** Included accounts not yet processed. */
  pendingCount: number;
  excludedCount: number;
  includedAdvanced: boolean;
  refused: AdminBulkRefusedGroup[];
  noOpCount: number;
  truncatedCount: number;
  /** The items matching the request's outcome filter; `items` is one page of them. */
  itemsTotalCount: number;
  items: AdminBulkItem[];
}

export interface AdminBulkHistoryPage {
  totalCount: number;
  rows: AdminBulkOperation[];
}

export interface AdminBulkItemsQuery {
  outcome?: AdminBulkItemFilter;
  skip?: number;
  take?: number;
}

export interface AdminBulkHistoryQuery {
  skip?: number;
  take?: number;
  /** Previewed and expired operations are left out unless asked for. */
  includePreviews?: boolean;
}

// --- Admin log ---

/** An admin log entry's kind (`AdminUserActionKind`), by name. */
export type AdminUserActionKind =
  | 'LockedOut'
  | 'Unlocked'
  | 'Banned'
  | 'Unbanned'
  | 'GameRestrictionsChanged'
  | 'SignedOutEverywhere'
  | 'EmailConfirmed'
  | 'TwoFactorReset'
  | 'Deleted'
  | 'AiChatBlocked'
  | 'AiChatUnblocked'
  | 'HoldPlaced'
  | 'HoldReleased'
  | 'DeletionRequestApproved'
  | 'DeletionRequestDeclined'
  | 'EmailChanged';

/** Every kind, in the order of the stored enum. */
export const ADMIN_USER_ACTION_KINDS: readonly AdminUserActionKind[] = [
  'LockedOut',
  'Unlocked',
  'Banned',
  'Unbanned',
  'GameRestrictionsChanged',
  'SignedOutEverywhere',
  'EmailConfirmed',
  'TwoFactorReset',
  'Deleted',
  'AiChatBlocked',
  'AiChatUnblocked',
  'HoldPlaced',
  'HoldReleased',
  'DeletionRequestApproved',
  'DeletionRequestDeclined',
  'EmailChanged'
];

export interface AdminUserLogEntry {
  id: number;
  occurredUtc: string;
  actorUserId: string;
  actorUserName: string;
  subjectUserId: string;
  /** Kept after the account is deleted. */
  subjectUserName: string;
  kind: AdminUserActionKind;
  reason: string | null;
  /** For example `Before: In good standing. After: Locked out until 2026-10-20 15:00 UTC.` */
  detail: string | null;
  bulkOperationId: number | null;
}

export interface AdminUserLogPage {
  totalCount: number;
  rows: AdminUserLogEntry[];
}

/** The admin log's filters; newest first. */
export interface AdminUserLogQuery {
  /** Subject or actor name. */
  search?: string;
  kinds?: readonly AdminUserActionKind[];
  /** The actor's user name. */
  actor?: string;
  subjectUserId?: string;
  bulkOperationId?: number;
  skip?: number;
  take?: number;
}

// --- Errors ---

/** The server's error body, `{ message, code }`, with the HTTP status. */
export interface AdminApiError {
  status: number;
  message: string;
  /** A refusal code (`self`, `already-done`, `expired`, …), or null. */
  code: string | null;
}

/** Reads the server's `{ message, code }` from a failed request; `Unknown error` when it carries none. */
export function adminApiError(err: unknown): AdminApiError {
  const http = err as { status?: number; error?: { message?: unknown; code?: unknown } | string | null; message?: string } | null;
  const status = typeof http?.status === 'number' ? http.status : 0;
  const body = http?.error;
  if (body && typeof body === 'object') {
    const message = typeof body.message === 'string' && body.message.trim() ? body.message : null;
    const code = typeof body.code === 'string' && body.code ? body.code : null;
    if (message) return { status, message, code };
    if (code) return { status, message: http?.message || 'Unknown error', code };
  }
  if (typeof body === 'string' && body.trim()) return { status, message: body, code: null };
  return { status, message: http?.message || 'Unknown error', code: null };
}

/** `DELETE 37 ACCOUNTS`, or `DELETE 1 ACCOUNT`: the phrase a bulk delete of `count` accounts needs typed. */
export function bulkConfirmationPhrase(count: number): string {
  return count === 1 ? 'DELETE 1 ACCOUNT' : `DELETE ${count} ACCOUNTS`;
}

/** The criteria as query parameters: `statuses` repeats, missing and blank fields are left out. */
export function adminUserCriteriaParams(criteria: AdminUserCriteria, params: HttpParams = new HttpParams()): HttpParams {
  let p = params;
  const search = criteria.search?.trim();
  if (search) p = p.set('search', search);
  for (const status of criteria.statuses ?? []) p = p.append('statuses', status);
  if (criteria.emailConfirmed) p = p.set('emailConfirmed', criteria.emailConfirmed);
  if (criteria.registeredOlderThanDays != null) p = p.set('registeredOlderThanDays', String(criteria.registeredOlderThanDays));
  if (criteria.registeredWithinDays != null) p = p.set('registeredWithinDays', String(criteria.registeredWithinDays));
  if (criteria.lastSignIn) p = p.set('lastSignIn', criteria.lastSignIn);
  if (criteria.hasGameData) p = p.set('hasGameData', criteria.hasGameData);
  if (criteria.hasOverseerData) p = p.set('hasOverseerData', criteria.hasOverseerData);
  return p;
}

/**
 * The typed client of `AdminUsersController` (`api/admin/users`): the user list and its facet counts,
 * the per-user actions, the deletion impact and delete, the Bulk Actions operations and the admin log.
 * Every per-user action returns the action result: the status before and after and the refreshed account.
 */
@Injectable({
  providedIn: 'root'
})
export class AdminUsersService {
  private http = inject(HttpClient);

  private userUrl(id: string, suffix = ''): string {
    return `${ADMIN_USERS_ENDPOINT}/${encodeURIComponent(id)}${suffix}`;
  }

  // --- List ---

  /** One batch of the list; `take` is clamped to 1–100 by the server. 400 code `criteria` on a malformed criterion. */
  list(criteria: AdminUserCriteria, sort: AdminUserSort, skip: number, take: number): Observable<AdminUsersPage> {
    let params = adminUserCriteriaParams(criteria).set('sort', sort).set('take', String(take));
    if (skip > 0) params = params.set('skip', String(skip));
    return this.http.get<AdminUsersPage>(ADMIN_USERS_ENDPOINT, { params });
  }

  /** A count per facet option, under the search only. */
  facets(search?: string): Observable<AdminUserFacetCounts> {
    const term = search?.trim();
    const params = term ? new HttpParams().set('search', term) : new HttpParams();
    return this.http.get<AdminUserFacetCounts>(`${ADMIN_USERS_ENDPOINT}/facets`, { params });
  }

  get(id: string): Observable<AdminUser> {
    return this.http.get<AdminUser>(this.userUrl(id));
  }

  // --- Per-user actions; 409 codes self, administrator, already-done ---

  lockOut(id: string, request: AdminLockoutRequest): Observable<AdminUserActionResult> {
    return this.http.post<AdminUserActionResult>(this.userUrl(id, '/lockout'), request);
  }

  /** Ends any lockout, automatic ones included, and resets the failed sign-in count. */
  unlock(id: string): Observable<AdminUserActionResult> {
    return this.http.delete<AdminUserActionResult>(this.userUrl(id, '/lockout'));
  }

  /** `reason` (1–512 characters) is for administrators only. */
  ban(id: string, reason: string): Observable<AdminUserActionResult> {
    return this.http.post<AdminUserActionResult>(this.userUrl(id, '/ban'), { reason });
  }

  unban(id: string): Observable<AdminUserActionResult> {
    return this.http.delete<AdminUserActionResult>(this.userUrl(id, '/ban'));
  }

  setGameRestrictions(id: string, request: AdminGameRestrictionsRequest): Observable<AdminUserActionResult> {
    return this.http.put<AdminUserActionResult>(this.userUrl(id, '/game-restrictions'), request);
  }

  signOutEverywhere(id: string): Observable<AdminUserActionResult> {
    return this.http.post<AdminUserActionResult>(this.userUrl(id, '/sign-out-everywhere'), {});
  }

  confirmEmail(id: string): Observable<AdminUserActionResult> {
    return this.http.post<AdminUserActionResult>(this.userUrl(id, '/confirm-email'), {});
  }

  /** `reason` is optional (at most 512 characters). */
  resetTwoFactor(id: string, reason?: string): Observable<AdminUserActionResult> {
    const body = reason?.trim() ? { reason: reason.trim() } : {};
    return this.http.post<AdminUserActionResult>(this.userUrl(id, '/reset-two-factor'), body);
  }

  /** `reason` (1–512 characters) is for administrators only. Unless `markConfirmed`, the server emails the new address instructions. */
  changeEmail(id: string, request: AdminChangeEmailRequest): Observable<AdminUserActionResult> {
    return this.http.post<AdminUserActionResult>(this.userUrl(id, '/change-email'), request);
  }

  /** Blocks the user from starting AI work in Overseer; `reason` (1–256 characters) is shown to them. */
  blockFromAiChat(id: string, reason: string): Observable<AdminUserActionResult> {
    return this.http.post<AdminUserActionResult>(this.userUrl(id, '/overseer-block'), { reason });
  }

  unblockFromAiChat(id: string): Observable<AdminUserActionResult> {
    return this.http.delete<AdminUserActionResult>(this.userUrl(id, '/overseer-block'));
  }

  /** Holds any deletion of the account for review; `reason` (1–512 characters) is never shown to the user. */
  placeHold(id: string, reason: string): Observable<AdminUserActionResult> {
    return this.http.post<AdminUserActionResult>(this.userUrl(id, '/account-hold'), { reason });
  }

  /** Releases the hold; a pending deletion request still needs an explicit decision. */
  releaseHold(id: string): Observable<AdminUserActionResult> {
    return this.http.delete<AdminUserActionResult>(this.userUrl(id, '/account-hold'));
  }

  // --- Deletion ---

  deletionImpact(id: string): Observable<AccountDeletionImpact> {
    return this.http.get<AccountDeletionImpact>(this.userUrl(id, '/deletion-impact'));
  }

  /**
   * Deletes the account permanently. 409 with a refusal code; 400 codes `confirm-name`, `acknowledgments`,
   * `reason`; 500 code `erasure-failed`. Success has `deleted` true and `user` null.
   */
  deleteUser(id: string, request: AdminDeleteUserRequest): Observable<AdminUserActionResult> {
    return this.http.post<AdminUserActionResult>(this.userUrl(id, '/delete'), request);
  }

  // --- Bulk ---

  /** 400 codes `narrowing`, `reason`, `until`, `criteria`; 409 code `erasure-paths`. */
  bulkPreview(request: AdminBulkPreviewRequest): Observable<AdminBulkPreview> {
    return this.http.post<AdminBulkPreview>(`${ADMIN_USERS_ENDPOINT}/bulk/preview`, request);
  }

  /** The operation with one page of its items, filtered by outcome. */
  bulkGet(operationId: number, query: AdminBulkItemsQuery = {}): Observable<AdminBulkOperation> {
    let params = new HttpParams();
    if (query.outcome) params = params.set('outcome', query.outcome);
    if (query.skip) params = params.set('skip', String(query.skip));
    if (query.take) params = params.set('take', String(query.take));
    return this.http.get<AdminBulkOperation>(`${ADMIN_USERS_ENDPOINT}/bulk/${operationId}`, { params });
  }

  /**
   * Runs a preview. 403 code `actor`; 409 codes `expired`, `not-previewed`, `busy`; 400 codes `excluded`,
   * `acknowledgments`, `empty`, `phrase`.
   */
  bulkExecute(operationId: number, request: AdminBulkExecuteRequest): Observable<AdminBulkOperation> {
    return this.http.post<AdminBulkOperation>(`${ADMIN_USERS_ENDPOINT}/bulk/${operationId}/execute`, request);
  }

  /** Stops after the current account; discards a preview. 409 code `finished`. */
  bulkCancel(operationId: number): Observable<AdminBulkOperation> {
    return this.http.post<AdminBulkOperation>(`${ADMIN_USERS_ENDPOINT}/bulk/${operationId}/cancel`, {});
  }

  /** Newest first, with empty item lists. */
  bulkHistory(query: AdminBulkHistoryQuery = {}): Observable<AdminBulkHistoryPage> {
    let params = new HttpParams();
    if (query.skip) params = params.set('skip', String(query.skip));
    if (query.take) params = params.set('take', String(query.take));
    if (query.includePreviews) params = params.set('includePreviews', 'true');
    return this.http.get<AdminBulkHistoryPage>(`${ADMIN_USERS_ENDPOINT}/bulk`, { params });
  }

  /** The results report's address: CSV with a UTF-8 BOM, named `bulk-operation-{id}.csv`. */
  bulkReportUrl(operationId: number): string {
    return `${ADMIN_USERS_ENDPOINT}/bulk/${operationId}/report.csv`;
  }

  /** The results report as a file to save. */
  bulkReport(operationId: number): Observable<Blob> {
    return this.http.get(this.bulkReportUrl(operationId), { responseType: 'blob' });
  }

  // --- Admin log ---

  /** Newest first; `kinds` is sent comma-separated. */
  log(query: AdminUserLogQuery = {}): Observable<AdminUserLogPage> {
    let params = new HttpParams();
    const search = query.search?.trim();
    if (search) params = params.set('search', search);
    if (query.kinds?.length) params = params.set('kinds', query.kinds.join(','));
    if (query.actor?.trim()) params = params.set('actor', query.actor.trim());
    if (query.subjectUserId) params = params.set('subjectUserId', query.subjectUserId);
    if (query.bulkOperationId != null) params = params.set('bulkOperationId', String(query.bulkOperationId));
    if (query.skip) params = params.set('skip', String(query.skip));
    if (query.take) params = params.set('take', String(query.take));
    return this.http.get<AdminUserLogPage>(`${ADMIN_USERS_ENDPOINT}/log`, { params });
  }
}
