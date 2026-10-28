import { toolLabel, wordsLabel } from '../../utils/display-labels.util';

// Readable labels for the coded values of the Security tab: event codes and details, the snapshot guard's
// channels, text kinds, subjects and scanner codes, and the codes of the related records. The stored values stay
// in the database, the API and the event's JSON. Pure: no Angular, no HTTP.

/** The kinds whose code is the provider's own vocabulary, shown as received. */
const PROVIDER_CODE_KINDS: ReadonlySet<string> = new Set(['ProviderRefusal', 'ProviderSafetyBlock', 'ProviderPolicyRejection']);

/**
 * Each kind's codes, as the code that records the event writes them (`docs/overseer/security-monitoring.md` § 5).
 * Updated with every new code; a code it does not know reads in words.
 */
export const SECURITY_CODE_LABELS: ReadonlyMap<string, ReadonlyMap<string, string>> = new Map<string, ReadonlyMap<string, string>>([
  // AuthController, SessionController.Create, the website's Login page; the game APIs write `game-api:<route>`.
  ['LoginFailed', new Map([
    ['login', 'Overseer sign-in'],
    ['session-create', 'GnollHack sign-in to Overseer'],
    ['website-login', 'Website sign-in']
  ])],
  ['LoginLockedOut', new Map([
    ['login', 'Overseer sign-in'],
    ['login-2fa', 'Overseer two-factor sign-in'],
    ['session-create', 'GnollHack sign-in to Overseer'],
    ['website-login', 'Website sign-in']
  ])],
  ['TwoFactorFailed', new Map([
    ['login-2fa', 'Overseer two-factor sign-in']
  ])],
  // RateLimitPolicies and GameApiRateLimits; the game sign-in throttle; HandoffGreetingStarter.
  ['RateLimitRejected', new Map([
    ['LoginRateLimit', 'Overseer sign-in, per IP address'],
    ['ChatRateLimit', 'Overseer chat, per user'],
    ['AttachmentRateLimit', 'Overseer attachments and exports, per user'],
    ['TunnelRateLimit', 'Overseer error reports, per user'],
    ['GameApiFlood', 'Game API requests, per IP address'],
    ['AccountPages', 'Website account pages, per IP address'],
    ['game-api-signin', 'Game sign-in throttle, game APIs'],
    ['session-create', 'Game sign-in throttle, GnollHack sign-in to Overseer'],
    ['session-create-turn', 'First chat turn from GnollHack, per user'],
    ['unknown', 'Rate limit without a policy name']
  ])],
  // SystemAiConfigService; AiUsageGuard.
  ['QuotaRefused', new Map([
    ['user', 'User\'s limit on the configuration'],
    ['group', 'Group limit on the configuration'],
    ['system', 'Configuration\'s own limit'],
    ['guard-cost', 'Daily cost guard'],
    ['guard-runs', 'Daily run guard']
  ])],
  ['AccessDenied', new Map([
    ['disabled', 'User\'s assignment switched off'],
    ['not-assigned', 'Not assigned to the user']
  ])],
  // ChatService attachment checks.
  ['AttachmentRejected', new Map([
    ['count', 'Too many attachments'],
    ['validation', 'File extension, type or size refused'],
    ['content', 'Contents refused: empty, too large or not the declared type'],
    ['malware', 'Malware found'],
    ['scan-failed', 'Malware scan failed']
  ])],
  // SnapshotGuardService.CodeOf.
  ['SnapshotRejected', new Map([
    ['injection-own', 'Injection in the player\'s own text'],
    ['injection-foreign', 'Injection in possibly another player\'s text'],
    ['suspicious-own', 'Suspicious content in the player\'s own text'],
    ['suspicious-foreign', 'Suspicious content in possibly another player\'s text']
  ])],
  // OverseerAccessService.SnapshotInjectionCode.
  ['AutoBlocked', new Map([
    ['snapshot-injection', 'Repeated injections in the player\'s own text']
  ])],
  // The website's deletion page, from AccountHoldEvaluator's verdict.
  ['AccountDeletionHeld', new Map([
    ['manual', 'Manual account hold'],
    ['automatic', 'Serious provider safety events']
  ])]
]);

/** The game API routes `GameApiAuthenticator` names in a `game-api:<route>` code. */
const GAME_API_ROUTE_LABELS: ReadonlyMap<string, string> = new Map([
  ['bones', 'bones upload'],
  ['xlogfile', 'finished-game record upload'],
  ['replay', 'replay upload'],
  ['savefiletracking', 'save file tracking']
]);

/** True for the provider kinds, whose code is the provider's own and is shown as received under *Provider code*. */
export function isProviderCodeKind(kind: string | null | undefined): boolean {
  return PROVIDER_CODE_KINDS.has(kind ?? '');
}

/**
 * An event code's label: `not-assigned` reads *Not assigned to the user*, `msg:812` *Message #812*. A provider kind's
 * code is returned as stored; an unknown code reads in words. Empty for no code.
 */
export function securityCodeLabel(kind: string | null | undefined, code: string | null | undefined): string {
  if (code == null || code.trim() === '') return '';
  if (isProviderCodeKind(kind)) return code;
  const text = code.trim();
  const label = SECURITY_CODE_LABELS.get(kind ?? '')?.get(text);
  if (label !== undefined) return label;
  if (kind === 'ChatReported') {
    const message = /^msg:(\d+)$/.exec(text);
    if (message) return `Message #${message[1]}`;
  }
  if (kind === 'LoginFailed' && text.startsWith('game-api:') && text.length > 'game-api:'.length) {
    const route = text.slice('game-api:'.length);
    return `Game API, ${GAME_API_ROUTE_LABELS.get(route) ?? wordsLabel(route).toLowerCase()}`;
  }
  return wordsLabel(text);
}

/**
 * A session's snapshot refusal code: the `SnapshotRejected` code's label, with the subject in parentheses when the
 * code names one (`injection-own:refresh`).
 */
export function snapshotRefusalCodeLabel(code: string | null | undefined): string {
  const text = (code ?? '').trim();
  if (!text) return '';
  const colon = text.indexOf(':');
  if (colon < 0) return securityCodeLabel('SnapshotRejected', text);
  const subject = text.slice(colon + 1);
  return `${securityCodeLabel('SnapshotRejected', text.slice(0, colon))} (${snapshotSubjectLabel(subject)})`;
}

/** The game sign-in throttle's limits (`GameApiSignInThrottle`). */
const THROTTLE_LIMIT_LABELS: ReadonlyMap<string, string> = new Map([
  ['pair', 'Per account and IP address'],
  ['account', 'Per account, across IP addresses'],
  ['ip', 'Per IP address, across accounts']
]);

/** `{type};{size}` of `ChatService`'s attachment checks; either side may be empty, not both. */
const ATTACHMENT_DETAIL = /^([A-Za-z0-9/.+-]{0,64});(\d{0,19})$/;

/** `req:<id>` of `ProviderRefusalEvents`. */
const PROVIDER_REQUEST_DETAIL = /^req:([A-Za-z0-9_.-]{1,64})$/;

/** `SnapshotGuardService` cuts a longer detail to this length, so its last group may be partial. */
const SNAPSHOT_DETAIL_MAX = 256;
const SNAPSHOT_OBSERVED_PREFIX = 'observed;';
const SNAPSHOT_DETAIL_GROUP = /^([A-Za-z0-9_-]+):([a-z0-9-]+(?:,[a-z0-9-]+)*)$/;

/**
 * An event detail of a fixed shape in words: `unknown-user` reads *No account matched the user name*, `pair` *Per
 * account and IP address*, `req:abc123` *Provider request id abc123*, `image/png;48213` *image/png, 48,213 bytes*, and
 * a snapshot guard detail as a sentence per channel. A reason, a route template or anything else that matches no
 * shape for its kind is returned as stored. Empty for no detail.
 */
export function securityDetailText(kind: string | null | undefined, detail: string | null | undefined): string {
  if (detail == null) return '';
  const text = detail.trim();
  switch (kind) {
    case 'LoginFailed':
      if (text === 'unknown-user') return 'No account matched the user name';
      break;
    case 'RateLimitRejected': {
      const limit = THROTTLE_LIMIT_LABELS.get(text);
      if (limit !== undefined) return limit;
      break;
    }
    case 'ProviderRefusal':
    case 'ProviderSafetyBlock':
    case 'ProviderPolicyRejection': {
      const request = PROVIDER_REQUEST_DETAIL.exec(text);
      if (request) return `Provider request id ${request[1]}`;
      break;
    }
    case 'AttachmentRejected': {
      const attachment = ATTACHMENT_DETAIL.exec(text);
      if (attachment && (attachment[1] !== '' || attachment[2] !== '')) {
        return attachmentDetailText(attachment[1], attachment[2]);
      }
      break;
    }
    case 'SnapshotRejected':
      return snapshotDetailText(text) ?? detail;
  }
  return detail;
}

function attachmentDetailText(type: string, size: string): string {
  if (size === '') return type;
  const n = Number(size);
  const bytes = `${n.toLocaleString('en-US')} ${n === 1 ? 'byte' : 'bytes'}`;
  return type === '' ? bytes : `${type}, ${bytes}`;
}

/** *Observed and delivered.* or *Withheld.*, then `Channel: Code, Code.` per group; null when a group has no shape. */
function snapshotDetailText(text: string): string | null {
  if (text === '' || text.length >= SNAPSHOT_DETAIL_MAX) return null;
  const observed = text.startsWith(SNAPSHOT_OBSERVED_PREFIX);
  const groups = (observed ? text.slice(SNAPSHOT_OBSERVED_PREFIX.length) : text).split(';');
  const sentences: string[] = [];
  for (const group of groups) {
    const match = SNAPSHOT_DETAIL_GROUP.exec(group);
    if (!match) return null;
    sentences.push(`${snapshotChannelLabel(match[1])}: ${match[2].split(',').map(snapshotCodeLabel).join(', ')}.`);
  }
  return [observed ? 'Observed and delivered.' : 'Withheld.', ...sentences].join(' ');
}

/** The channels a scanned text arrives on (`SessionController.Create`, `ChatController` attach). */
const SNAPSHOT_CHANNEL_LABELS: ReadonlyMap<string, string> = new Map([
  ['create', 'Snapshot sent at session start'],
  ['history', 'Message history sent at session start'],
  ['manifest', 'Directory manifest sent at session start'],
  ['attach', 'Snapshot attached in the chat']
]);

const TOOL_CHANNEL_PREFIX = 'tool-';

/** A capitalized first word kept as written inside a sentence. */
const PROPER_FIRST_WORDS: ReadonlySet<string> = new Set(['Oracle']);

/** A label inside a sentence: its first word in lower case unless it is a name (`GnollHack`, `Oracle`). */
function inSentence(label: string): string {
  const first = /^[A-Z][a-z]*(?=[\s-]|$)/.exec(label);
  if (!first || PROPER_FIRST_WORDS.has(first[0])) return label;
  return label.charAt(0).toLowerCase() + label.slice(1);
}

/** A snapshot guard channel: `create` reads *Snapshot sent at session start*, `tool-<name>` *Result of {tool}*. */
export function snapshotChannelLabel(channel: string | null | undefined): string {
  const text = (channel ?? '').trim();
  const label = SNAPSHOT_CHANNEL_LABELS.get(text);
  if (label !== undefined) return label;
  if (text.startsWith(TOOL_CHANNEL_PREFIX) && text.length > TOOL_CHANNEL_PREFIX.length) {
    return `Result of ${inSentence(toolLabel(text.slice(TOOL_CHANNEL_PREFIX.length)))}`;
  }
  return wordsLabel(text);
}

/** `SnapshotGuardTextKind` names. */
const SNAPSHOT_TEXT_KIND_LABELS: ReadonlyMap<string, string> = new Map([
  ['Snapshot', 'Game snapshot'],
  ['MessageHistory', 'Message history'],
  ['Manifest', 'Directory manifest'],
  ['ClientData', 'Client tool result']
]);

/** What a scanned text is: `MessageHistory` reads *Message history*. */
export function snapshotTextKindLabel(textKind: string | null | undefined): string {
  const text = (textKind ?? '').trim();
  return SNAPSHOT_TEXT_KIND_LABELS.get(text) ?? wordsLabel(text);
}

/** `SnapshotGuardSubject` names, keyed in lower case: a session's refusal code writes them so. */
const SNAPSHOT_SUBJECT_LABELS: ReadonlyMap<string, string> = new Map([
  ['snapshot', 'Snapshot'],
  ['refresh', 'Refreshed snapshot'],
  ['history', 'Message history'],
  ['manifest', 'Directory manifest'],
  ['client', 'Other client tool result']
]);

/** Which game text a scanned input is: `Refresh` reads *Refreshed snapshot*. */
export function snapshotSubjectLabel(subject: string | null | undefined): string {
  const text = (subject ?? '').trim();
  return SNAPSHOT_SUBJECT_LABELS.get(text.toLowerCase()) ?? wordsLabel(text);
}

/** `SnapshotGuardScanner`'s codes; their one-line meanings are `SNAPSHOT_GUARD_CODE_MEANINGS`. */
const SNAPSHOT_CODE_LABELS: ReadonlyMap<string, string> = new Map([
  ['address', 'Addresses the AI'],
  ['override', 'Overrides instructions'],
  ['authority', 'Claims authority'],
  ['tool', 'Steers a tool call'],
  ['exfil-image', 'Image link to another site'],
  ['exfil-url', 'Link to another site'],
  ['hidden-tag', 'Hidden tag characters'],
  ['hidden', 'Hidden characters'],
  ['directive', 'Standing order'],
  ['scan-timeout', 'Scan timed out']
]);

/** A scanner code's short label: `address` reads *Addresses the AI*. */
export function snapshotCodeLabel(code: string | null | undefined): string {
  const text = (code ?? '').trim();
  return SNAPSHOT_CODE_LABELS.get(text) ?? wordsLabel(text);
}

/** `LegalDocumentKeys` documents, keyed in lower case (the configured keys are case-insensitive), by their wiki page. */
const TERMS_DOCUMENT_LABELS: ReadonlyMap<string, string> = new Map([
  ['overseer-terms', 'Gnoll Overseer Terms of Service'],
  ['account-terms', 'GnollHack Account Terms of Service']
]);

/** A terms document: `overseer-terms` reads *Gnoll Overseer Terms of Service*. */
export function termsDocumentLabel(documentKey: string | null | undefined): string {
  const text = (documentKey ?? '').trim();
  return TERMS_DOCUMENT_LABELS.get(text.toLowerCase()) ?? wordsLabel(text);
}

/** `LegalDocumentKeys` sources: Overseer's `LegalController`, `SessionController`, the website's account pages. */
const TERMS_SOURCE_LABELS: ReadonlyMap<string, string> = new Map([
  ['web', 'Overseer website'],
  ['gnollhackapp', 'GnollHack app'],
  ['accountsite', 'GnollHack website']
]);

/** Where a terms acceptance was made: `GnollHackApp` reads *GnollHack app*. */
export function termsSourceLabel(source: string | null | undefined): string {
  const text = (source ?? '').trim();
  return TERMS_SOURCE_LABELS.get(text.toLowerCase()) ?? wordsLabel(text);
}

const MESSAGE_ROLE_LABELS: ReadonlyMap<string, string> = new Map([
  ['user', 'User'],
  ['assistant', 'Assistant'],
  ['system', 'System']
]);

/** A chat message's role: `assistant` reads *Assistant*. */
export function messageRoleLabel(role: string | null | undefined): string {
  const text = (role ?? '').trim();
  return MESSAGE_ROLE_LABELS.get(text.toLowerCase()) ?? wordsLabel(text);
}
