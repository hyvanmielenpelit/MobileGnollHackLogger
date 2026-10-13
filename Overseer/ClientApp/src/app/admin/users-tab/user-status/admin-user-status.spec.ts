import { ADMIN_USER_STATUSES, AdminUserStatus } from '../../../services/admin-users.service';
import { buildAdminUser } from '../admin-users.testing';
import {
  addedStatuses,
  badgesFor,
  changeNote,
  formatUtcDateTime,
  formatUtcDay,
  goodStanding,
  relativeTime,
  statusLabel
} from './admin-user-status';

describe('admin-user-status', () => {
  it('labels every status with the word the client shows', () => {
    const labels = ADMIN_USER_STATUSES.map(statusLabel);
    expect(labels).toEqual([
      'In good standing', 'Locked out', 'Banned', 'Game log uploads banned', 'Bones banned',
      'Blocked from AI chat', 'Deletion hold', 'Deletion pending', 'Email not confirmed', 'Administrator'
    ]);
  });

  it('gives each status a badge with a tone and a glyph, in the fixed order', () => {
    const reversed = [...ADMIN_USER_STATUSES].reverse() as AdminUserStatus[];
    const badges = badgesFor({ statuses: reversed });
    expect(badges.map(b => b.status)).toEqual([...ADMIN_USER_STATUSES]);
    expect(badges.find(b => b.status === 'GoodStanding')).toEqual(expect.objectContaining({ tone: 'ok', glyph: 'check-circle' }));
    expect(badges.find(b => b.status === 'LockedOut')).toEqual(expect.objectContaining({ tone: 'danger', glyph: 'lock' }));
    expect(badges.find(b => b.status === 'Banned')).toEqual(expect.objectContaining({ tone: 'danger', glyph: 'slash-circle' }));
    expect(badges.find(b => b.status === 'GameLogBanned')).toEqual(expect.objectContaining({ tone: 'warning', glyph: 'upload-cloud-off' }));
    expect(badges.find(b => b.status === 'BonesBanned')).toEqual(expect.objectContaining({ glyph: 'upload-cloud-off' }));
    expect(badges.find(b => b.status === 'AiChatBlocked')).toEqual(expect.objectContaining({ glyph: 'message-square-off' }));
    expect(badges.find(b => b.status === 'DeletionHold')).toEqual(expect.objectContaining({ glyph: 'shield' }));
    expect(badges.find(b => b.status === 'DeletionPending')).toEqual(expect.objectContaining({ glyph: 'trash' }));
    expect(badges.find(b => b.status === 'EmailUnconfirmed')).toEqual(expect.objectContaining({ glyph: 'mail' }));
    expect(badges.find(b => b.status === 'Administrator')).toEqual(expect.objectContaining({ glyph: 'star' }));
  });

  it('shows In good standing alone for an account with nothing wrong', () => {
    const user = buildAdminUser();
    expect(badgesFor(user).map(b => b.label)).toEqual(['In good standing']);
    expect(badgesFor(user)[0].tooltip).toBeNull();
    expect(goodStanding(user)).toBe(true);
    expect(goodStanding(buildAdminUser({ statuses: ['EmailUnconfirmed'] }))).toBe(false);
  });

  it('tells who locked the account out, until when, and why', () => {
    const admin = buildAdminUser({
      statuses: ['LockedOut'], lockoutEnd: '2026-10-20T15:00:00+00:00', lockoutIsAdminSet: true, adminLockoutReason: 'Spam'
    });
    expect(badgesFor(admin)[0].tooltip).toBe('Locked out by an administrator until 2026-10-20 15:00 UTC. Reason: Spam');

    const automatic = buildAdminUser({ statuses: ['LockedOut'], lockoutEnd: '2026-10-13T15:42:00Z', accessFailedCount: 10 });
    expect(badgesFor(automatic)[0].tooltip).toBe('Locked out after 10 failed sign-ins until 2026-10-13 15:42 UTC.');
  });

  it('tells a dated ban from a legacy one, and gives the hold and block facts', () => {
    expect(badgesFor(buildAdminUser({ statuses: ['Banned'], isBanned: true, bannedUtc: '2026-10-01T08:00:00Z', banReason: 'Cheating' }))[0].tooltip)
      .toBe('Banned on 2026-10-01 08:00 UTC. No sign-in is allowed anywhere. Reason: Cheating');
    expect(badgesFor(buildAdminUser({ statuses: ['Banned'], isBanned: true }))[0].tooltip)
      .toBe('Banned before ban dates were recorded. No sign-in is allowed anywhere.');
    expect(badgesFor(buildAdminUser({
      statuses: ['DeletionHold'], hasAccountHold: true, accountHoldReason: 'Fraud case',
      accountHoldPlacedByUserName: 'tommi', accountHoldPlacedUtc: '2026-10-02T09:30:00Z'
    }))[0].tooltip).toBe('Deletion hold placed by tommi on 2026-10-02 09:30 UTC. Reason: Fraud case');
    expect(badgesFor(buildAdminUser({
      statuses: ['AiChatBlocked'], isOverseerBlocked: true, overseerBlockedUtc: '2026-10-03T10:00:00Z', overseerBlockReason: 'Abuse'
    }))[0].tooltip).toBe('Blocked from starting AI work in Overseer since 2026-10-03 10:00 UTC. Reason shown to the user: Abuse');
  });

  it('gives a preview item, which has only statuses, badges without tooltips', () => {
    const badges = badgesFor({ statuses: ['LockedOut', 'Banned', 'EmailUnconfirmed'] });
    expect(badges.map(b => b.tooltip)).toEqual([null, null, null]);
  });

  it('writes the before → after note and the added statuses', () => {
    const result = {
      before: { statuses: ['GoodStanding'] as AdminUserStatus[], text: 'In good standing' },
      after: { statuses: ['LockedOut'] as AdminUserStatus[], text: 'Locked out until 2026-10-20 15:00 UTC' }
    };
    expect(changeNote(result)).toBe('In good standing → Locked out until 2026-10-20 15:00 UTC');
    expect(addedStatuses(result)).toEqual(['LockedOut']);
  });

  it('formats UTC dates and relative times', () => {
    expect(formatUtcDay('2026-03-02T23:30:00')).toBe('2026-03-02');
    expect(formatUtcDateTime('2026-03-02T23:30:00')).toBe('2026-03-02 23:30 UTC');
    expect(formatUtcDay(null)).toBe('');
    const now = '2026-10-13T12:00:00Z';
    expect(relativeTime('2026-10-13T11:59:30Z', now)).toBe('just now');
    expect(relativeTime('2026-10-13T11:55:00Z', now)).toBe('5 minutes ago');
    expect(relativeTime('2026-10-13T11:00:00Z', now)).toBe('1 hour ago');
    expect(relativeTime('2026-10-10T12:00:00Z', now)).toBe('3 days ago');
    expect(relativeTime('2026-03-02T12:00:00Z', now)).toBe('on 2026-03-02');
    expect(relativeTime(null, now)).toBe('');
  });
});
