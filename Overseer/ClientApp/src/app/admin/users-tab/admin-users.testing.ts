import { of } from 'rxjs';
import {
  AccountDeletionImpact,
  AdminUser,
  AdminUserActionResult,
  AdminUserFacetCounts,
  AdminUserStatus,
  AdminUsersPage,
  AdminUsersService
} from '../../services/admin-users.service';

// Spec helper for the Users tab and its parts. Imported by specs only.

/** An account in good standing: confirmed, no lockout, ban, block, hold or request. */
export function buildAdminUser(overrides: Partial<AdminUser> = {}): AdminUser {
  return {
    id: 'u-1',
    userName: 'alice',
    email: 'alice@example.com',
    emailConfirmed: true,
    groups: [],
    statuses: ['GoodStanding'],
    statusText: 'In good standing',
    isAdministrator: false,
    isSelf: false,
    createdUtc: '2026-03-02T10:00:00Z',
    createdUtcIsEstimate: false,
    lastSignInUtc: '2026-10-10T12:00:00Z',
    lockoutEnd: null,
    lockoutIsAdminSet: false,
    adminLockoutReason: null,
    accessFailedCount: 0,
    twoFactorEnabled: false,
    isBanned: false,
    bannedUtc: null,
    banReason: null,
    isGameLogBanned: false,
    isBonesBanned: false,
    isOverseerBlocked: false,
    overseerBlockedUtc: null,
    overseerBlockReason: null,
    hasAccountHold: false,
    accountHoldReason: null,
    accountHoldPlacedUtc: null,
    accountHoldPlacedByUserName: null,
    pendingDeletionRequestId: null,
    gameSignInBlocks: [],
    gameLogCount: 0,
    bonesCount: 0,
    chatSessionCount: 0,
    junetHackUserName: null,
    ...overrides
  };
}

/** What an action returns: `user` after it, with the statuses before and after. */
export function buildActionResult(user: AdminUser | null, before: AdminUserStatus[], beforeText: string,
                                  after: AdminUserStatus[], afterText: string, summary = 'Done.'): AdminUserActionResult {
  return {
    user,
    before: { statuses: before, text: beforeText },
    after: { statuses: after, text: afterText },
    summary,
    deleted: user === null
  };
}

export function buildUsersPage(rows: AdminUser[], totalCount = rows.length): AdminUsersPage {
  return { totalCount, rows, nowUtc: '2026-10-13T12:00:00Z' };
}

/** Facet counts with every option at zero and `total` users. */
export function buildFacetCounts(total = 0, overrides: Partial<AdminUserFacetCounts> = {}): AdminUserFacetCounts {
  return {
    total,
    statuses: [],
    emailConfirmed: [],
    registered: [],
    lastSignIn: [],
    hasGameData: [],
    hasOverseerData: [],
    ...overrides
  };
}

export function buildDeletionImpact(overrides: Partial<AccountDeletionImpact> = {}): AccountDeletionImpact {
  return {
    userId: 'u-1',
    userName: 'alice',
    refusals: [],
    categories: [
      { key: 'game-logs', label: 'Game logs', count: 0, important: true },
      { key: 'bones', label: 'Bones files', count: 0, important: true },
      { key: 'save-files', label: 'Save-file tracking records', count: 0, important: true },
      { key: 'chats', label: 'Chats', count: 0, important: true },
      { key: 'ai-usage', label: 'AI usage records', count: 0, important: false },
      { key: 'benchmark-records', label: 'Benchmark records', count: 0, important: false, note: 'Kept; attribution removed.' }
    ],
    ...overrides
  };
}

/** The empty responses every Users tab spec starts from: no users and no counts. */
export function spyAdminUsersService(service: AdminUsersService): void {
  vi.spyOn(service, 'list').mockReturnValue(of(buildUsersPage([])));
  vi.spyOn(service, 'facets').mockReturnValue(of(buildFacetCounts()));
}
