import { unzipSync } from 'fflate';

import { SecurityEventDetail, SecurityQuarantineItem, SecurityRelated } from '../../services/admin.service';
import {
  SECURITY_UNREADABLE_NOTICE,
  buildSecurityEventArchive,
  buildSecurityEventArchiveEntries,
  isUnreadableQuarantineText,
  quarantineCapText,
  quarantineLengthText,
  quarantineTags,
  quarantineTextDownloadName,
  securityEventArchiveName,
  securityEventFields,
  securityRelatedSections
} from './security-event-archive';

function item(id: number, overrides: Partial<SecurityQuarantineItem> = {}): SecurityQuarantineItem {
  return {
    id,
    occurredUtc: '2026-10-27T10:00:00Z',
    channel: 'tool-get_directory_listing',
    subject: 'Manifest',
    textKind: 'Manifest',
    level: 'Injection',
    codes: ['authority'],
    playerAuthored: true,
    withheld: true,
    sessionMode: 'normal',
    sessionRef: '42',
    plainLength: 120,
    storedLength: 120,
    truncated: false,
    sha256: 'ab'.repeat(32),
    text: '=== Game Directory Manifest ===\nsave\\game.sav\t12345\n',
    ...overrides
  };
}

function detail(overrides: Partial<SecurityEventDetail> = {}): SecurityEventDetail {
  return {
    id: 12,
    occurredUtc: '2026-10-27T10:00:00Z',
    lastOccurredUtc: '2026-10-27T10:04:00Z',
    kind: 'SnapshotRejected',
    count: 2,
    userId: 'u1',
    userName: 'Alice',
    userIsOverseerBlocked: false,
    ipAddress: null,
    systemAiApiConfigurationId: 3,
    provider: null,
    modelId: null,
    code: 'injection-own',
    detail: 'tool-get_directory_listing:authority',
    quarantineCount: 1,
    quarantine: [item(7)],
    quarantineTotalCount: 1,
    explanation: 'The snapshot guard refused game text addressed to the AI.',
    systemConfigurationName: 'Main',
    related: {
      termsAcceptances: [{ documentKey: 'overseer-terms', version: '3', acceptedUtc: '2026-09-01T08:00:00Z', source: 'GnollHackApp' }],
      adminActions: [],
      hold: null,
      deletionRequest: null,
      sessions: [{ id: 42, createdUtc: '2026-10-27T09:59:00Z', snapshotRefusedUtc: '2026-10-27T10:00:00Z', snapshotRefusalCode: 'injection-own', isConfidential: false }],
      reportedMessage: null,
      providerAttempts: [],
      usageRows: [],
      ipEvents: [],
      requestLogs: [],
      sameUserEvents: [{ id: 11, kind: 'SnapshotRejected', code: 'injection-own', detail: 'create:address|tool' }]
    },
    ...overrides
  };
}

const NOW = new Date(Date.UTC(2026, 9, 27, 11, 5, 30));

describe('security-event-archive', () => {
  it('names the JSON, the Markdown and one text file per refused text', () => {
    const entries = buildSecurityEventArchiveEntries(detail(), NOW);

    expect(entries.map(e => e.name)).toEqual([
      'security-event-12.json',
      'security-event-12.md',
      'texts/1-tool-get_directory_listing.txt'
    ]);
    expect(entries.every(e => e.mtime === NOW)).toBe(true);
    const text = entries[2];
    expect('text' in text && text.text).toBe(item(7).text);
  });

  it('replaces each text in the JSON with its file name and keeps every other field', () => {
    const entries = buildSecurityEventArchiveEntries(detail(), NOW);
    const json = JSON.parse((entries[0] as { text: string }).text);

    expect(json.quarantine[0].text).toBe('texts/1-tool-get_directory_listing.txt');
    expect(json.quarantine[0].sha256).toBe('ab'.repeat(32));
    expect(json.related.termsAcceptances[0].documentKey).toBe('overseer-terms');
    expect(json.explanation).toBe('The snapshot guard refused game text addressed to the AI.');
    expect(JSON.stringify(json)).not.toContain('Game Directory Manifest');
  });

  it('keeps every coded value in the JSON as stored, never as its label', () => {
    const entries = buildSecurityEventArchiveEntries(detail({ provider: 'openai', modelId: 'gpt-x', modelLabel: 'GPT X' }), NOW);
    const json = JSON.parse((entries[0] as { text: string }).text);

    expect(json.kind).toBe('SnapshotRejected');
    expect(json.code).toBe('injection-own');
    expect(json.detail).toBe('tool-get_directory_listing:authority');
    expect(json.provider).toBe('openai');
    expect(json.modelId).toBe('gpt-x');
    expect(json.quarantine[0].channel).toBe('tool-get_directory_listing');
    expect(json.quarantine[0].codes).toEqual(['authority']);
    expect(json.quarantine[0].subject).toBe('Manifest');
    expect(json.quarantine[0].textKind).toBe('Manifest');
    expect(json.related.termsAcceptances[0].source).toBe('GnollHackApp');
    expect(json.related.sessions[0].snapshotRefusalCode).toBe('injection-own');
    expect(json.related.sameUserEvents[0].code).toBe('injection-own');
    const raw = JSON.stringify(json);
    for (const label of ['Injection in the player', 'Claims authority', 'Result of', 'Directory manifest', 'Gnoll Overseer Terms', 'OpenAI']) {
      expect(raw).not.toContain(label);
    }
  });

  it('writes the header, the explanation, the fields, each related table and each text\'s metadata in the Markdown', () => {
    const md = (buildSecurityEventArchiveEntries(detail(), NOW)[1] as { text: string }).text;

    expect(md).toContain('# Security event #12: Snapshot refused · Alice');
    expect(md).toContain('The snapshot guard refused game text addressed to the AI.');
    expect(md).toContain('## Stored fields');
    expect(md).toContain('| Configuration | 3 · Main |');
    expect(md).toContain('| Kind | Snapshot refused |');
    expect(md).toContain('| Code | Injection in the player\'s own text |');
    expect(md).toContain('| Detail | Withheld. Result of game folder listing: Claims authority. |');
    expect(md).toContain('## Terms accepted');
    expect(md).toContain('| Gnoll Overseer Terms of Service | 3 | 2026-09-01 08:00 UTC | GnollHack app |');
    expect(md).toContain('## Sessions with a refused snapshot');
    expect(md).toContain('## Other events of this user');
    expect(md).toContain('create:address\\|tool');
    expect(md).toContain('## Refused game text');
    expect(md).toContain('### 1. Result of game folder listing');
    expect(md).toContain('| Codes | Claims authority |');
    expect(md).toContain('| Text kind | Directory manifest |');
    expect(md).toContain('| Subject | Directory manifest |');
    expect(md).toContain('| File | texts/1-tool-get_directory_listing.txt |');
    expect(md).toContain('| Verdict | Injection · Player\'s own text · Withheld · Normal chat |');
    expect(md).not.toContain('## Admin log');
    expect(md).not.toContain('Game Directory Manifest ===');
  });

  it('says when the texts are capped', () => {
    const capped = detail({ quarantineTotalCount: 60 });

    expect(quarantineCapText(capped)).toBe('Showing the newest 1 of 60.');
    expect((buildSecurityEventArchiveEntries(capped, NOW)[1] as { text: string }).text).toContain('Showing the newest 1 of 60.');
    expect(quarantineCapText(detail())).toBe('');
  });

  it('makes the names unique for two texts on the same channel', () => {
    const entries = buildSecurityEventArchiveEntries(detail({
      quarantine: [item(1, { channel: 'create' }), item(2, { channel: 'create' })],
      quarantineTotalCount: 2
    }), NOW);

    const names = entries.map(e => e.name);
    expect(names).toContain('texts/1-create.txt');
    expect(names).toContain('texts/2-create.txt');
    expect(new Set(names).size).toBe(names.length);
  });

  it('has no text files for an event without refused text', () => {
    const entries = buildSecurityEventArchiveEntries(detail({ quarantine: [], quarantineTotalCount: 0, quarantineCount: 0 }), NOW);

    expect(entries.map(e => e.name)).toEqual(['security-event-12.json', 'security-event-12.md']);
    expect((entries[1] as { text: string }).text).not.toContain('## Refused game text');
  });

  it('builds a ZIP holding every entry', async () => {
    const blob = await buildSecurityEventArchive(detail(), NOW);

    expect(blob.type).toBe('application/zip');
    const unzipped = unzipSync(new Uint8Array(await blob.arrayBuffer()));
    expect(Object.keys(unzipped).sort()).toEqual([
      'security-event-12.json', 'security-event-12.md', 'texts/1-tool-get_directory_listing.txt'
    ]);
    expect(new TextDecoder().decode(unzipped['texts/1-tool-get_directory_listing.txt'])).toBe(item(7).text);
  });

  it('names the ZIP and a single text download', () => {
    expect(securityEventArchiveName(12, NOW)).toBe('security-event-12-20261027-1105.zip');
    expect(quarantineTextDownloadName(12, 0, item(7))).toBe('security-event-12-1-tool-get_directory_listing.txt');
  });

  it('lists every stored field, a dash for an empty one', () => {
    const fields = securityEventFields(detail());

    expect(fields.map(f => f.label)).toEqual([
      'Event id', 'Kind', 'User', 'User id', 'Configuration', 'Provider', 'Model', 'IP address', 'Code', 'Detail',
      'First occurrence', 'Last occurrence', 'Count'
    ]);
    expect(fields.find(f => f.label === 'Kind')?.value).toBe('Snapshot refused');
    expect(fields.find(f => f.label === 'Code')).toEqual({ label: 'Code', value: 'Injection in the player\'s own text', mono: false });
    expect(fields.find(f => f.label === 'Detail')?.value).toBe('Withheld. Result of game folder listing: Claims authority.');
    expect(fields.find(f => f.label === 'IP address')?.value).toBe('—');
    expect(fields.find(f => f.label === 'Configuration')?.value).toBe('3 · Main');
    expect(securityEventFields(detail({ systemConfigurationName: null })).find(f => f.label === 'Configuration')?.value)
      .toBe('3 · no longer exists');
  });

  it('builds a section only for a related list that has rows, terms first', () => {
    const sections = securityRelatedSections(detail().related);

    expect(sections.map(s => s.id)).toEqual(['terms', 'sessions', 'user-events']);
    expect(sections[1].rows[0]).toEqual(['#42', '2026-10-27 09:59 UTC', '2026-10-27 10:00 UTC', 'Injection in the player\'s own text', 'No']);
    expect(sections[2].rows[0]).toEqual(['#11', 'Snapshot refused', 'Injection in the player\'s own text', 'create:address|tool']);
  });

  it('shows a provider kind\'s code as received under Provider code, and the provider and model by name', () => {
    const fields = securityEventFields(detail({
      kind: 'ProviderSafetyBlock', provider: 'google', modelId: 'gemini-x', modelLabel: 'Gemini X',
      code: 'PROHIBITED_CONTENT', detail: 'req:abc-123'
    }));
    const byLabel = (label: string) => fields.find(f => f.label === label);

    expect(fields.map(f => f.label)).toContain('Provider code');
    expect(fields.map(f => f.label)).not.toContain('Code');
    expect(byLabel('Provider code')).toEqual({ label: 'Provider code', value: 'PROHIBITED_CONTENT', mono: true });
    expect(byLabel('Provider')?.value).toBe('Google');
    expect(byLabel('Model')).toEqual({ label: 'Model', value: 'Gemini X', mono: false });
    expect(byLabel('Detail')?.value).toBe('Provider request id abc-123');
  });

  it('shows the model id in monospace when the catalog has no name for it', () => {
    const model = (overrides: Partial<SecurityEventDetail>) =>
      securityEventFields(detail({ provider: 'xai', modelId: 'grok-x', ...overrides })).find(f => f.label === 'Model');

    expect(model({ modelLabel: 'grok-x' })).toEqual({ label: 'Model', value: 'grok-x', mono: true });
    expect(model({ modelLabel: null })).toEqual({ label: 'Model', value: 'grok-x', mono: true });
    expect(model({ modelLabel: undefined, modelId: null })).toEqual({ label: 'Model', value: '—', mono: true });
  });

  it('labels the coded values of the related records and keeps identifiers and provider vocabulary', () => {
    const related: SecurityRelated = {
      ...detail().related,
      termsAcceptances: [{ documentKey: 'account-terms', version: '2026-10-18', acceptedUtc: '2026-09-01T08:00:00Z', source: 'AccountSite' }],
      adminActions: [{ id: 5, occurredUtc: '2026-10-27T09:00:00Z', kind: 'HoldPlaced', actorUserName: 'admin', reason: 'Review', detail: null, bulkOperationId: 3 }],
      sessions: [{ id: 43, createdUtc: '2026-10-27T09:59:00Z', snapshotRefusedUtc: null, snapshotRefusalCode: 'suspicious-foreign:refresh', isConfidential: true }],
      reportedMessage: { messageId: 812, sessionId: 42, timestampUtc: '2026-10-27T09:58:00Z', role: 'assistant', exists: true },
      providerAttempts: [{
        startedAtUtc: '2026-10-27T10:00:00Z', roleContext: 1, requestedModelId: 'gpt-x', requestedModelLabel: 'GPT X',
        httpStatus: 400, providerErrorCode: 'content_policy_violation', outcome: 'OurRequest', retried: false
      }],
      usageRows: [{ timestampUtc: '2026-10-27T10:00:00Z', modelId: 'gpt-x', modelLabel: 'GPT X', roleContext: 3, tokens: 1234, cost: 0.5 }],
      ipEvents: [{ id: 9, kind: 'RateLimitRejected', code: 'LoginRateLimit', count: 4 }],
      sameUserEvents: [
        { id: 10, kind: 'ProviderRefusal', code: 'refusal', detail: 'req:r1' },
        { id: 11, kind: 'AttachmentRejected', code: 'content', detail: 'image/png;48213' },
        { id: 12, kind: 'AdminBlocked', code: null, detail: 'Spam' }
      ]
    };
    const rows = (id: string) => securityRelatedSections(related).find(s => s.id === id)!.rows;

    expect(rows('terms')[0]).toEqual(['GnollHack Account Terms of Service', '2026-10-18', '2026-09-01 08:00 UTC', 'GnollHack website']);
    expect(rows('admin-actions')[0]).toEqual(['2026-10-27 09:00 UTC', 'Deletion hold placed', 'admin', 'Review', '—', '#3']);
    expect(rows('sessions')[0][3]).toBe('Suspicious content in possibly another player\'s text (Refreshed snapshot)');
    expect(rows('reported')[0]).toEqual(['#812', '#42', '2026-10-27 09:58 UTC', 'Assistant', 'Yes']);
    expect(rows('attempts')[0]).toEqual(['2026-10-27 10:00 UTC', 'Chat', 'GPT X', '400', 'content_policy_violation', 'Request rejected', 'No']);
    expect(rows('usage')[0]).toEqual(['2026-10-27 10:00 UTC', 'GPT X', 'Sub-agent', '1,234', '$0.5000']);
    expect(rows('ip-events')[0]).toEqual(['#9', 'Rate limit', 'Overseer sign-in, per IP address', '4']);
    expect(rows('user-events')).toEqual([
      ['#10', 'Provider refusal', 'refusal', 'Provider request id r1'],
      ['#11', 'Attachment rejected', 'Content check failed', 'image/png, 48,213 bytes'],
      ['#12', 'Blocked by admin', '—', 'Spam']
    ]);
  });

  it('falls back to the model id in the attempts and usage tables when no label came', () => {
    const related: SecurityRelated = {
      ...detail().related,
      providerAttempts: [{ startedAtUtc: '2026-10-27T10:00:00Z', roleContext: 1, requestedModelId: 'gpt-x', outcome: 'Ok', retried: false }],
      usageRows: [{ timestampUtc: '2026-10-27T10:00:00Z', modelId: 'gpt-x', roleContext: 1 }]
    };
    const sections = securityRelatedSections(related);

    expect(sections.find(s => s.id === 'attempts')!.rows[0][2]).toBe('gpt-x');
    expect(sections.find(s => s.id === 'attempts')!.rows[0][5]).toBe('Succeeded');
    expect(sections.find(s => s.id === 'usage')!.rows[0][1]).toBe('gpt-x');
  });

  it('reads the verdict tags, the length and the unreadable notice', () => {
    expect(quarantineTags(item(1, { level: 'Suspicious', playerAuthored: false, withheld: false, sessionMode: 'incognito' })))
      .toEqual(['Suspicious', 'Possibly another player\'s', 'Delivered (Observe)', 'Incognito chat']);
    expect(quarantineLengthText(item(1, { plainLength: 150000, storedLength: 100000, truncated: true })))
      .toBe('150,000 characters, truncated to 100,000');
    expect(isUnreadableQuarantineText(item(1, { text: SECURITY_UNREADABLE_NOTICE }))).toBe(true);
    expect(isUnreadableQuarantineText(item(1))).toBe(false);
  });
});
