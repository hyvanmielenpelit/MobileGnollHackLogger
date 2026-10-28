import {
  SecurityEvent,
  SecurityEventDetail,
  SecurityQuarantineItem,
  SecurityRelated
} from '../../services/admin.service';
import { elapsedMsBetween, parseServerUtcDate } from '../../utils/date.util';
import { providerLabel } from '../../utils/display-labels.util';
import { safeFileName } from '../../utils/download.util';
import { ArchiveEntry, buildTextArchive, uniqueFileNames } from '../benchmark/download-center/text-archive';
import { outcomeLabel } from '../benchmark/shared/provider-availability/provider-availability';
import { adminLogKindLabel } from '../users-tab/admin-log/admin-log-kinds';
import {
  isProviderCodeKind,
  messageRoleLabel,
  securityCodeLabel,
  securityDetailText,
  snapshotChannelLabel,
  snapshotCodeLabel,
  snapshotRefusalCodeLabel,
  snapshotSubjectLabel,
  snapshotTextKindLabel,
  termsDocumentLabel,
  termsSourceLabel
} from './security-labels';

// The words, tables and ZIP entries of one security event, shared by the Security tab's cards, the
// event dialog and the event's download. Pure: no Angular, no HTTP.

const KIND_LABELS: Readonly<Record<string, string>> = {
  LoginFailed: 'Sign-in failed',
  LoginLockedOut: 'Locked out',
  TwoFactorFailed: 'Two-factor failed',
  RateLimitRejected: 'Rate limit',
  QuotaRefused: 'Quota refused',
  AccessDenied: 'Access denied',
  ProviderRefusal: 'Provider refusal',
  ProviderSafetyBlock: 'Provider safety block',
  ProviderPolicyRejection: 'Provider policy rejection',
  AttachmentRejected: 'Attachment rejected',
  ChatReported: 'Chat reported',
  SnapshotRejected: 'Snapshot refused',
  AutoBlocked: 'Blocked automatically',
  AdminBlocked: 'Blocked by admin',
  AdminUnblocked: 'Unblocked by admin',
  AccountHoldPlaced: 'Deletion hold placed',
  AccountHoldReleased: 'Deletion hold released',
  AccountDeletionHeld: 'Deletion held for review',
  AdminLockedOut: 'Locked out by admin',
  AdminUnlocked: 'Unlocked by admin',
  AdminBanned: 'Banned by admin',
  AdminUnbanned: 'Unbanned by admin',
  AdminSignedOut: 'Signed out by admin',
  AdminEmailConfirmed: 'Email confirmed by admin',
  AdminTwoFactorReset: 'Two-factor reset by admin',
  AdminEmailChanged: 'Email changed by admin',
  AdminGameSignInBlockCleared: 'Game sign-in block cleared by admin'
};

/** What a security event kind reads as; an unknown kind reads as its name. */
export function securityKindLabel(kind: string): string {
  return Object.hasOwn(KIND_LABELS, kind) ? KIND_LABELS[kind] : kind;
}

/** The user name, else *Unknown user* for an id without one, else *No user*. */
export function securityUserLabel(event: Pick<SecurityEvent, 'userId' | 'userName'>): string {
  return event.userName?.trim() || (event.userId ? 'Unknown user' : 'No user');
}

/** `2026-10-13 09:15 UTC`; an unparseable value as it came. */
export function securityTimeText(utc: string | null | undefined): string {
  if (!utc) return '';
  const date = parseServerUtcDate(utc);
  if (isNaN(date.getTime())) return utc;
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())} `
    + `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())} UTC`;
}

/** How long a run of repeats lasted: `in 4 min`, `in under a minute`, `in 2 h 5 min`. */
export function securityRunSpanText(firstUtc: string, lastUtc: string): string {
  const minutes = Math.round(elapsedMsBetween(firstUtc, lastUtc) / 60000);
  if (minutes < 1) return 'in under a minute';
  if (minutes < 60) return `in ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest === 0 ? `in ${hours} h` : `in ${hours} h ${rest} min`;
}

/** The text the server sends for a quarantine row it cannot decrypt (`ContentProtectionService.UnreadableNotice`). */
export const SECURITY_UNREADABLE_NOTICE = '[This content could not be decrypted.]';

export function isUnreadableQuarantineText(item: Pick<SecurityQuarantineItem, 'text'>): boolean {
  return item.text === SECURITY_UNREADABLE_NOTICE;
}

const SESSION_MODE_LABELS: Readonly<Record<string, string>> = {
  normal: 'Normal chat',
  confidential: 'Confidential chat',
  incognito: 'Incognito chat'
};

/** The words of a refused text's verdict, author, delivery and chat mode, in that order. */
export function quarantineTags(item: SecurityQuarantineItem): string[] {
  return [
    item.level,
    item.playerAuthored ? 'Player\'s own text' : 'Possibly another player\'s',
    item.withheld ? 'Withheld' : 'Delivered (Observe)',
    SESSION_MODE_LABELS[item.sessionMode] ?? item.sessionMode
  ];
}

/** `12,345 characters`, with `, truncated to 100,000` when only part was kept. */
export function quarantineLengthText(item: SecurityQuarantineItem): string {
  const length = `${formatCount(item.plainLength)} ${item.plainLength === 1 ? 'character' : 'characters'}`;
  return item.truncated ? `${length}, truncated to ${formatCount(item.storedLength)}` : length;
}

/** `Showing the newest 50 of 60.`, or empty when every text is listed. */
export function quarantineCapText(detail: Pick<SecurityEventDetail, 'quarantine' | 'quarantineTotalCount'>): string {
  return detail.quarantineTotalCount > detail.quarantine.length
    ? `Showing the newest ${detail.quarantine.length} of ${detail.quarantineTotalCount}.`
    : '';
}

/** The archive paths of an event's texts, `texts/{n}-{channel}.txt`, in list order and unique. */
export function quarantineTextFileNames(items: readonly SecurityQuarantineItem[]): string[] {
  return uniqueFileNames(items.map((item, i) => `texts/${i + 1}-${safeFileName(item.channel)}.txt`));
}

/** The file one text downloads as on its own: `security-event-{id}-{n}-{channel}.txt`. */
export function quarantineTextDownloadName(eventId: number, index: number, item: SecurityQuarantineItem): string {
  return `security-event-${eventId}-${index + 1}-${safeFileName(item.channel)}.txt`;
}

/** `security-event-{id}-{yyyyMMdd-HHmm}.zip`, the time in UTC. */
export function securityEventArchiveName(eventId: number, now: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  const stamp = `${now.getUTCFullYear()}${pad(now.getUTCMonth() + 1)}${pad(now.getUTCDate())}-`
    + `${pad(now.getUTCHours())}${pad(now.getUTCMinutes())}`;
  return `security-event-${eventId}-${stamp}.zip`;
}

/** One stored field of an event, as the dialog's list and the Markdown summary show it. */
export interface SecurityEventField {
  label: string;
  value: string;
  /** Identifiers, model ids and provider codes are set in the monospace face. */
  mono: boolean;
}

/**
 * Every stored field of the event, ids included, coded values as labels; an empty one reads as a dash. A provider
 * kind's code is shown as received, under *Provider code*.
 */
export function securityEventFields(detail: SecurityEventDetail): SecurityEventField[] {
  const field = (label: string, value: string | number | null | undefined, mono = false): SecurityEventField =>
    ({ label, value: textOf(value), mono });
  const configuration = detail.systemAiApiConfigurationId == null
    ? null
    : `${detail.systemAiApiConfigurationId} · ${detail.systemConfigurationName?.trim() || 'no longer exists'}`;
  const modelLabel = detail.modelLabel?.trim();
  const providerCode = isProviderCodeKind(detail.kind);
  return [
    field('Event id', detail.id, true),
    field('Kind', securityKindLabel(detail.kind)),
    field('User', securityUserLabel(detail)),
    field('User id', detail.userId, true),
    field('Configuration', configuration),
    field('Provider', providerLabel(detail.provider)),
    field('Model', modelLabel || detail.modelId, !modelLabel || modelLabel === detail.modelId),
    field('IP address', detail.ipAddress, true),
    providerCode
      ? field('Provider code', detail.code, true)
      : field('Code', securityCodeLabel(detail.kind, detail.code)),
    field('Detail', securityDetailText(detail.kind, detail.detail)),
    field('First occurrence', securityTimeText(detail.occurredUtc)),
    field('Last occurrence', securityTimeText(detail.lastOccurredUtc)),
    field('Count', detail.count)
  ];
}

/** One list of related records as a table: a heading, column headers and cell texts. */
export interface SecurityRelatedSection {
  id: string;
  title: string;
  headers: string[];
  rows: string[][];
}

const ROLE_CONTEXT_LABELS: Readonly<Record<number, string>> = {
  1: 'Chat',
  2: 'Title generation',
  3: 'Sub-agent',
  4: 'GnollBench',
  5: 'Question generation',
  6: 'Rubric check',
  7: 'Suite description',
  8: 'Report pack'
};

function roleText(roleContext: number): string {
  return ROLE_CONTEXT_LABELS[roleContext] ?? String(roleContext);
}

/** The related records that exist, one section each, *Terms accepted* first, coded values as labels; empty lists have none. */
export function securityRelatedSections(related: SecurityRelated | null | undefined): SecurityRelatedSection[] {
  if (!related) return [];
  const sections: SecurityRelatedSection[] = [];
  const add = (id: string, title: string, headers: string[], rows: string[][]) => {
    if (rows.length > 0) sections.push({ id, title, headers, rows });
  };
  const cells = (...values: (string | number | boolean | null | undefined)[]): string[] => values.map(textOf);

  add('terms', 'Terms accepted', ['Document', 'Version', 'Accepted', 'Source'],
    (related.termsAcceptances ?? []).map(t => cells(termsDocumentLabel(t.documentKey), t.version, securityTimeText(t.acceptedUtc),
      termsSourceLabel(t.source))));
  add('admin-actions', 'Admin log', ['Time', 'Action', 'By', 'Reason', 'Detail', 'Bulk operation'],
    (related.adminActions ?? []).map(a => cells(securityTimeText(a.occurredUtc), adminLogKindLabel(a.kind), a.actorUserName, a.reason, a.detail,
      a.bulkOperationId == null ? null : `#${a.bulkOperationId}`)));
  const hold = related.hold;
  add('hold', 'Account hold', ['Hold', 'Reason', 'Placed by', 'Placed'],
    hold ? [cells(`#${hold.id}`, hold.reason, hold.placedByUserName, securityTimeText(hold.placedUtc))] : []);
  const deletion = related.deletionRequest;
  add('deletion', 'Deletion request', ['Request', 'Status', 'Requested', 'Hold reasons'],
    deletion ? [cells(`#${deletion.id}`, deletion.status, securityTimeText(deletion.requestedUtc), deletion.holdReasons)] : []);
  add('sessions', 'Sessions with a refused snapshot', ['Session', 'Created', 'Snapshot refused', 'Refusal code', 'Confidential'],
    (related.sessions ?? []).map(s => cells(`#${s.id}`, securityTimeText(s.createdUtc), securityTimeText(s.snapshotRefusedUtc),
      snapshotRefusalCodeLabel(s.snapshotRefusalCode), s.isConfidential)));
  const message = related.reportedMessage;
  add('reported', 'Reported message', ['Message', 'Session', 'Sent', 'Role', 'Still stored'],
    message ? [cells(`#${message.messageId}`, message.sessionId == null ? null : `#${message.sessionId}`,
      securityTimeText(message.timestampUtc), messageRoleLabel(message.role), message.exists)] : []);
  add('attempts', 'Provider attempts', ['Started', 'Role', 'Model', 'HTTP status', 'Error code', 'Outcome', 'Retried'],
    (related.providerAttempts ?? []).map(p => cells(securityTimeText(p.startedAtUtc), roleText(p.roleContext),
      p.requestedModelLabel?.trim() || p.requestedModelId, p.httpStatus, p.providerErrorCode, outcomeLabel(p.outcome), p.retried)));
  add('usage', 'AI usage', ['Time', 'Model', 'Role', 'Tokens', 'Cost'],
    (related.usageRows ?? []).map(u => cells(securityTimeText(u.timestampUtc), u.modelLabel?.trim() || u.modelId, roleText(u.roleContext),
      u.tokens == null ? null : formatCount(u.tokens), u.cost == null ? null : `$${u.cost.toFixed(4)}`)));
  add('ip-events', 'Events from this IP address', ['Event', 'Kind', 'Code', 'Count'],
    (related.ipEvents ?? []).map(e => cells(`#${e.id}`, securityKindLabel(e.kind), securityCodeLabel(e.kind, e.code), e.count)));
  add('request-logs', 'Website request log', ['Last request', 'Count', 'Method', 'Path', 'Response', 'User name'],
    (related.requestLogs ?? []).map(r => cells(securityTimeText(r.lastDate), r.count, r.requestMethod, r.requestPath,
      r.responseCode, r.requestUserName)));
  add('user-events', 'Other events of this user', ['Event', 'Kind', 'Code', 'Detail'],
    (related.sameUserEvents ?? []).map(e => cells(`#${e.id}`, securityKindLabel(e.kind), securityCodeLabel(e.kind, e.code),
      securityDetailText(e.kind, e.detail))));
  return sections;
}

/**
 * The files of one event's download: `security-event-{id}.json` (the detail with every stored value as
 * stored, each text replaced by its file name), `security-event-{id}.md` (a readable summary in labels)
 * and each refused text as received, every entry stamped `now`.
 */
export function buildSecurityEventArchiveEntries(detail: SecurityEventDetail, now: Date): ArchiveEntry[] {
  const items = detail.quarantine ?? [];
  const textNames = quarantineTextFileNames(items);
  const json = { ...detail, quarantine: items.map((item, i) => ({ ...item, text: textNames[i] })) };
  return [
    { name: `security-event-${detail.id}.json`, text: JSON.stringify(json, null, 2) + '\n', mtime: now },
    { name: `security-event-${detail.id}.md`, text: securityEventMarkdown(detail, textNames, now), mtime: now },
    ...items.map((item, i): ArchiveEntry => ({ name: textNames[i], text: item.text, mtime: now }))
  ];
}

/** The event's ZIP. */
export function buildSecurityEventArchive(detail: SecurityEventDetail, now: Date): Promise<Blob> {
  return buildTextArchive(buildSecurityEventArchiveEntries(detail, now));
}

function securityEventMarkdown(detail: SecurityEventDetail, textNames: readonly string[], now: Date): string {
  const lines: string[] = [];
  lines.push(`# Security event #${detail.id}: ${securityKindLabel(detail.kind)} · ${securityUserLabel(detail)}`, '');
  if (detail.explanation?.trim()) {
    lines.push(detail.explanation.trim(), '');
  }
  lines.push('## Stored fields', '');
  lines.push(...markdownTable(['Field', 'Value'], securityEventFields(detail).map(f => [f.label, f.value])), '');

  for (const section of securityRelatedSections(detail.related)) {
    lines.push(`## ${section.title}`, '');
    lines.push(...markdownTable(section.headers, section.rows), '');
  }

  const items = detail.quarantine ?? [];
  if (items.length > 0) {
    lines.push('## Refused game text', '');
    const cap = quarantineCapText(detail);
    if (cap) lines.push(cap, '');
    items.forEach((item, i) => {
      lines.push(`### ${i + 1}. ${snapshotChannelLabel(item.channel)}`, '');
      lines.push(...markdownTable(['Field', 'Value'], [
        ['Verdict', quarantineTags(item).join(' · ')],
        ['Codes', textOf(item.codes.map(snapshotCodeLabel).join(', '))],
        ['Text kind', textOf(snapshotTextKindLabel(item.textKind))],
        ['Subject', textOf(snapshotSubjectLabel(item.subject))],
        ['Received', securityTimeText(item.occurredUtc)],
        ['Session', textOf(item.sessionRef)],
        ['Length', quarantineLengthText(item)],
        ['SHA-256 of the full text', item.sha256],
        ['File', textNames[i]]
      ]), '');
    });
  }

  lines.push(`Packaged ${now.toISOString()}.`, '');
  return lines.join('\n');
}

function markdownTable(headers: readonly string[], rows: readonly (readonly string[])[]): string[] {
  const row = (cellsOf: readonly string[]) => `| ${cellsOf.map(markdownCell).join(' | ')} |`;
  return [row(headers), `|${headers.map(() => '---').join('|')}|`, ...rows.map(row)];
}

/** A table cell on one line, its pipes escaped. */
function markdownCell(value: string): string {
  return value.replace(/\r?\n/g, ' ').replace(/\|/g, '\\|');
}

/** A value as cell text: a dash when empty, Yes / No for a flag. */
function textOf(value: string | number | boolean | null | undefined): string {
  if (value === null || value === undefined) return '—';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  const text = String(value).trim();
  return text === '' ? '—' : text;
}

function formatCount(n: number): string {
  return n.toLocaleString('en-US');
}
