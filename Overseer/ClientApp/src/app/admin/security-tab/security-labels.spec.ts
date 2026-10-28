import {
  SECURITY_CODE_LABELS,
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

/** Every code the server writes, by kind, with its label; the provider kinds are not here. */
const EXPECTED_CODE_LABELS: Readonly<Record<string, Readonly<Record<string, string>>>> = {
  LoginFailed: {
    login: 'Overseer sign-in',
    'session-create': 'GnollHack sign-in to Overseer',
    'website-login': 'Website sign-in'
  },
  LoginLockedOut: {
    login: 'Overseer sign-in',
    'login-2fa': 'Overseer two-factor sign-in',
    'session-create': 'GnollHack sign-in to Overseer',
    'website-login': 'Website sign-in'
  },
  TwoFactorFailed: {
    'login-2fa': 'Overseer two-factor sign-in'
  },
  RateLimitRejected: {
    LoginRateLimit: 'Overseer sign-in, per IP address',
    ChatRateLimit: 'Overseer chat, per user',
    AttachmentRateLimit: 'Overseer attachments and exports, per user',
    TunnelRateLimit: 'Overseer error reports, per user',
    GameApiFlood: 'Game API requests, per IP address',
    AccountPages: 'Website account pages, per IP address',
    'game-api-signin': 'Game sign-in throttle, game APIs',
    'session-create': 'Game sign-in throttle, GnollHack sign-in to Overseer',
    'session-create-turn': 'First chat turn from GnollHack, per user',
    unknown: 'Rate limit without a policy name'
  },
  QuotaRefused: {
    user: 'User\'s limit on the configuration',
    group: 'Group limit on the configuration',
    system: 'Configuration\'s own limit',
    'guard-cost': 'Daily cost guard',
    'guard-runs': 'Daily run guard'
  },
  AccessDenied: {
    disabled: 'User\'s assignment switched off',
    'not-assigned': 'Not assigned to the user'
  },
  AttachmentRejected: {
    count: 'Too many attachments',
    validation: 'File extension, type or size refused',
    content: 'Contents refused: empty, too large or not the declared type',
    malware: 'Malware found',
    'scan-failed': 'Malware scan failed'
  },
  SnapshotRejected: {
    'injection-own': 'Injection in the player\'s own text',
    'injection-foreign': 'Injection in possibly another player\'s text',
    'suspicious-own': 'Suspicious content in the player\'s own text',
    'suspicious-foreign': 'Suspicious content in possibly another player\'s text'
  },
  AutoBlocked: {
    'snapshot-injection': 'Repeated injections in the player\'s own text'
  },
  AccountDeletionHeld: {
    manual: 'Manual account hold',
    automatic: 'Serious provider safety events'
  }
};

describe('security-labels', () => {
  describe('securityCodeLabel', () => {
    it('labels every code the server writes, and the map holds no other', () => {
      for (const [kind, codes] of Object.entries(EXPECTED_CODE_LABELS)) {
        for (const [code, label] of Object.entries(codes)) {
          expect(securityCodeLabel(kind, code), `${kind} ${code}`).toBe(label);
        }
        expect([...SECURITY_CODE_LABELS.get(kind)!.keys()].sort()).toEqual(Object.keys(codes).sort());
      }
      expect([...SECURITY_CODE_LABELS.keys()].sort()).toEqual(Object.keys(EXPECTED_CODE_LABELS).sort());
    });

    it('reads a reported message\'s code as its id', () => {
      expect(securityCodeLabel('ChatReported', 'msg:812')).toBe('Message #812');
      expect(securityCodeLabel('ChatReported', 'msg:x')).toBe('Msg:x');
    });

    it('names the game API route of a game sign-in failure', () => {
      expect(securityCodeLabel('LoginFailed', 'game-api:bones')).toBe('Game API, bones upload');
      expect(securityCodeLabel('LoginFailed', 'game-api:xlogfile')).toBe('Game API, finished-game record upload');
      expect(securityCodeLabel('LoginFailed', 'game-api:replay')).toBe('Game API, replay upload');
      expect(securityCodeLabel('LoginFailed', 'game-api:savefiletracking')).toBe('Game API, save file tracking');
      expect(securityCodeLabel('LoginFailed', 'game-api:new_route')).toBe('Game API, new route');
    });

    it('returns a provider kind\'s code as the provider wrote it', () => {
      for (const kind of ['ProviderRefusal', 'ProviderSafetyBlock', 'ProviderPolicyRejection']) {
        expect(isProviderCodeKind(kind)).toBe(true);
        expect(securityCodeLabel(kind, 'PROHIBITED_CONTENT')).toBe('PROHIBITED_CONTENT');
        expect(securityCodeLabel(kind, 'content_filter')).toBe('content_filter');
        expect(securityCodeLabel(kind, 'guard-cost')).toBe('guard-cost');
      }
      expect(isProviderCodeKind('QuotaRefused')).toBe(false);
      expect(isProviderCodeKind(null)).toBe(false);
    });

    it('reads an unknown code in words, and no code as empty', () => {
      expect(securityCodeLabel('RateLimitRejected', 'SomeNewPolicy')).toBe('Some new policy');
      expect(securityCodeLabel('QuotaRefused', 'guard-tokens')).toBe('Guard tokens');
      expect(securityCodeLabel('NewKind', 'new-code')).toBe('New code');
      expect(securityCodeLabel('QuotaRefused', null)).toBe('');
      expect(securityCodeLabel('ProviderRefusal', '')).toBe('');
    });

    it('never reads an inherited property as a label', () => {
      expect(securityCodeLabel('QuotaRefused', 'constructor')).toBe('Constructor');
      expect(securityCodeLabel('constructor', 'toString')).toBe('To string');
      expect(securityCodeLabel('__proto__', 'user')).toBe('User');
      expect(securityCodeLabel('SnapshotRejected', '__proto__')).toBe('Proto');
    });
  });

  describe('snapshotRefusalCodeLabel', () => {
    it('reads a session\'s refusal code, with the subject when it names one', () => {
      expect(snapshotRefusalCodeLabel('injection-own')).toBe('Injection in the player\'s own text');
      expect(snapshotRefusalCodeLabel('injection-own:refresh')).toBe('Injection in the player\'s own text (Refreshed snapshot)');
      expect(snapshotRefusalCodeLabel('suspicious-foreign:client')).toBe('Suspicious content in possibly another player\'s text (Other client tool result)');
      expect(snapshotRefusalCodeLabel(null)).toBe('');
    });
  });

  describe('securityDetailText', () => {
    it('reads an unknown user name in words, only for a failed sign-in', () => {
      expect(securityDetailText('LoginFailed', 'unknown-user')).toBe('No account matched the user name');
      expect(securityDetailText('AdminBlocked', 'unknown-user')).toBe('unknown-user');
    });

    it('reads the game sign-in throttle\'s limit, and keeps a route template', () => {
      expect(securityDetailText('RateLimitRejected', 'pair')).toBe('Per account and IP address');
      expect(securityDetailText('RateLimitRejected', 'account')).toBe('Per account, across IP addresses');
      expect(securityDetailText('RateLimitRejected', 'ip')).toBe('Per IP address, across accounts');
      expect(securityDetailText('RateLimitRejected', 'api/auth/login')).toBe('api/auth/login');
    });

    it('reads a provider request id', () => {
      expect(securityDetailText('ProviderRefusal', 'req:abc123')).toBe('Provider request id abc123');
      expect(securityDetailText('ProviderSafetyBlock', 'req:a.b_c-1')).toBe('Provider request id a.b_c-1');
      expect(securityDetailText('ProviderPolicyRejection', 'req:has space')).toBe('req:has space');
      expect(securityDetailText('ProviderPolicyRejection', 'req:')).toBe('req:');
    });

    it('reads an attachment\'s type and size, with either side empty', () => {
      expect(securityDetailText('AttachmentRejected', 'image/png;48213')).toBe('image/png, 48,213 bytes');
      expect(securityDetailText('AttachmentRejected', 'image/png;')).toBe('image/png');
      expect(securityDetailText('AttachmentRejected', ';1')).toBe('1 byte');
      expect(securityDetailText('AttachmentRejected', ';')).toBe(';');
      expect(securityDetailText('AttachmentRejected', 'image/png;12;3')).toBe('image/png;12;3');
      expect(securityDetailText('AttachmentRejected', 'image/png')).toBe('image/png');
    });

    it('reads a snapshot guard detail as a sentence per channel, observed or withheld', () => {
      expect(securityDetailText('SnapshotRejected', 'observed;create:address,tool;history:directive')).toBe(
        'Observed and delivered. Snapshot sent at session start: Addresses the AI, Steers a tool call. '
        + 'Message history sent at session start: Standing order.');
      expect(securityDetailText('SnapshotRejected', 'tool-get_directory_listing:authority;attach:hidden-tag')).toBe(
        'Withheld. Result of game folder listing: Claims authority. Snapshot attached in the chat: Hidden tag characters.');
    });

    it('keeps a snapshot guard detail that has no shape, or that was cut, as stored', () => {
      expect(securityDetailText('SnapshotRejected', 'create:address|tool')).toBe('create:address|tool');
      expect(securityDetailText('SnapshotRejected', 'observed;')).toBe('observed;');
      expect(securityDetailText('SnapshotRejected', 'create:address;manif')).toBe('create:address;manif');
      const cut = ('create:address,override,authority,tool;').repeat(8).slice(0, 256);
      expect(securityDetailText('SnapshotRejected', cut)).toBe(cut);
    });

    it('keeps a reason as written, and reads no detail as empty', () => {
      expect(securityDetailText('AutoBlocked', 'Suspended automatically.')).toBe('Suspended automatically.');
      expect(securityDetailText('AccountHoldPlaced', 'pair')).toBe('pair');
      expect(securityDetailText('LoginFailed', null)).toBe('');
      expect(securityDetailText(undefined, 'x')).toBe('x');
    });
  });

  describe('snapshot guard labels', () => {
    it('names each channel, a tool\'s result by the tool', () => {
      expect(snapshotChannelLabel('create')).toBe('Snapshot sent at session start');
      expect(snapshotChannelLabel('history')).toBe('Message history sent at session start');
      expect(snapshotChannelLabel('manifest')).toBe('Directory manifest sent at session start');
      expect(snapshotChannelLabel('attach')).toBe('Snapshot attached in the chat');
      expect(snapshotChannelLabel('tool-refresh_snapshot')).toBe('Result of game status refresh');
      expect(snapshotChannelLabel('tool-get_full_message_history')).toBe('Result of message history');
      expect(snapshotChannelLabel('tool-get_directory_listing')).toBe('Result of game folder listing');
      expect(snapshotChannelLabel('tool-get_oracle_consultations')).toBe('Result of Oracle consultations');
      expect(snapshotChannelLabel('tool-wiki_search')).toBe('Result of GnollHack wiki search');
      expect(snapshotChannelLabel('tool-some_new_tool')).toBe('Result of some new tool');
      expect(snapshotChannelLabel('tool-')).toBe('Tool');
      expect(snapshotChannelLabel('mystery')).toBe('Mystery');
      expect(snapshotChannelLabel('constructor')).toBe('Constructor');
    });

    it('names each text kind', () => {
      expect(snapshotTextKindLabel('Snapshot')).toBe('Game snapshot');
      expect(snapshotTextKindLabel('MessageHistory')).toBe('Message history');
      expect(snapshotTextKindLabel('Manifest')).toBe('Directory manifest');
      expect(snapshotTextKindLabel('ClientData')).toBe('Client tool result');
      expect(snapshotTextKindLabel('NewKind')).toBe('New kind');
      expect(snapshotTextKindLabel('constructor')).toBe('Constructor');
    });

    it('names each subject, in either case', () => {
      expect(snapshotSubjectLabel('Snapshot')).toBe('Snapshot');
      expect(snapshotSubjectLabel('Refresh')).toBe('Refreshed snapshot');
      expect(snapshotSubjectLabel('History')).toBe('Message history');
      expect(snapshotSubjectLabel('Manifest')).toBe('Directory manifest');
      expect(snapshotSubjectLabel('Client')).toBe('Other client tool result');
      expect(snapshotSubjectLabel('refresh')).toBe('Refreshed snapshot');
      expect(snapshotSubjectLabel('hasOwnProperty')).toBe('Has own property');
    });

    it('names each scanner code', () => {
      expect(['address', 'override', 'authority', 'tool', 'exfil-image', 'exfil-url', 'hidden-tag', 'hidden', 'directive', 'scan-timeout']
        .map(snapshotCodeLabel)).toEqual([
        'Addresses the AI', 'Overrides instructions', 'Claims authority', 'Steers a tool call', 'Image link to another site',
        'Link to another site', 'Hidden tag characters', 'Hidden characters', 'Standing order', 'Scan timed out'
      ]);
      expect(snapshotCodeLabel('new-signal')).toBe('New signal');
      expect(snapshotCodeLabel('__proto__')).toBe('Proto');
    });
  });

  describe('related record labels', () => {
    it('names the terms documents and where they were accepted', () => {
      expect(termsDocumentLabel('overseer-terms')).toBe('Gnoll Overseer Terms of Service');
      expect(termsDocumentLabel('account-terms')).toBe('GnollHack Account Terms of Service');
      expect(termsDocumentLabel('Overseer-Terms')).toBe('Gnoll Overseer Terms of Service');
      expect(termsDocumentLabel('new-terms')).toBe('New terms');
      expect(termsDocumentLabel('constructor')).toBe('Constructor');
      expect(termsSourceLabel('Web')).toBe('Overseer website');
      expect(termsSourceLabel('GnollHackApp')).toBe('GnollHack app');
      expect(termsSourceLabel('AccountSite')).toBe('GnollHack website');
      expect(termsSourceLabel('NewSource')).toBe('New source');
      expect(termsSourceLabel('valueOf')).toBe('Value of');
    });

    it('names a message\'s role', () => {
      expect(messageRoleLabel('user')).toBe('User');
      expect(messageRoleLabel('assistant')).toBe('Assistant');
      expect(messageRoleLabel('System')).toBe('System');
      expect(messageRoleLabel('tool')).toBe('Tool');
      expect(messageRoleLabel('toString')).toBe('To string');
      expect(messageRoleLabel(null)).toBe('');
    });
  });
});
