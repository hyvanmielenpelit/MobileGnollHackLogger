import {
  FeedbackResolutionParseResult,
  checkFeedbackResolutionDocument,
  parseFeedbackResolutionYaml
} from './feedback-resolution-yaml';

const FP = '3f2a9c1b0d4e'.padEnd(64, 'a');
const FP2 = 'b'.repeat(64);

const overseerHeader = 'format: overseer-feedback-resolutions\nversion: 1\nsource: overseer\nexportedAtUtc: "2026-10-23T12:34:56Z"\n';
const gnollBenchHeader = 'format: overseer-feedback-resolutions\nversion: 1\nsource: gnollbench\nexportedAtUtc: "2026-10-23T12:34:56Z"\n';

const messages = (result: FeedbackResolutionParseResult): string[] => result.errors.map(e => e.message);

describe('feedback-resolution-yaml', () => {
  it('maps a valid Overseer file to the request, issues becoming items', async () => {
    const result = await parseFeedbackResolutionYaml(overseerHeader + [
      'issues:',
      '  - id: 42',
      `    fingerprint: "${FP}"`,
      '    status: Resolved',
      '    resolution: |',
      '      Fixed the tool.',
      '      Verified by hand.',
      '    fixedInVersion: "1.4.1"',
      '    note: ""',
      ''
    ].join('\n'), 'overseer');

    expect(result.errors).toEqual([]);
    expect(result.notices).toEqual([]);
    expect(result.request).toEqual({
      format: 'overseer-feedback-resolutions',
      version: 1,
      source: 'overseer',
      exportedAtUtc: '2026-10-23T12:34:56Z',
      items: [{ id: 42, fingerprint: FP, status: 'resolved', resolution: 'Fixed the tool.\nVerified by hand.', fixedInVersion: '1.4.1', note: '' }]
    });
  });

  it('maps a valid GnollBench file without ids', async () => {
    const result = await parseFeedbackResolutionYaml(gnollBenchHeader + `issues:\n  - fingerprint: "${FP}"\n    status: dismissed\n`, 'gnollbench');
    expect(result.errors).toEqual([]);
    expect(result.request?.source).toBe('gnollbench');
    expect(result.request?.items).toEqual([{ fingerprint: FP, status: 'dismissed' }]);
  });

  it('reports an empty document and a syntax error with its line', async () => {
    expect(messages(await parseFeedbackResolutionYaml('  \n# only a comment\n', 'overseer'))).toEqual(['The document is empty.']);

    const syntax = await parseFeedbackResolutionYaml(overseerHeader + 'issues:\n  - id: 1\n   bad: [\n', 'overseer');
    expect(syntax.request).toBeNull();
    expect(syntax.errors.length).toBe(1);
    expect(syntax.errors[0].line).not.toBeNull();
  });

  it('R1: requires the exact format and version', async () => {
    const wrongFormat = await parseFeedbackResolutionYaml('format: overseer-benchmark-questions\nversion: 1\n', 'overseer');
    expect(messages(wrongFormat)).toEqual(['The document must be a YAML mapping with `format: overseer-feedback-resolutions` and `version: 1`.']);

    const wrongVersion = await parseFeedbackResolutionYaml('format: overseer-feedback-resolutions\nversion: 2\n', 'overseer');
    expect(messages(wrongVersion)).toEqual(['Unsupported `version: 2`; this importer understands version 1.']);

    expect(messages(checkFeedbackResolutionDocument(['a list'], 'overseer'))[0]).toContain('must be a YAML mapping');
  });

  it('R2: requires a known source that matches the sub-tab', async () => {
    const body = `issues:\n  - fingerprint: "${FP}"\n    status: new\n`;
    expect(messages(await parseFeedbackResolutionYaml(gnollBenchHeader + body, 'overseer')))
      .toContain('This file is for the GnollBench sub-tab.');
    expect(messages(await parseFeedbackResolutionYaml(overseerHeader + 'issues:\n  - id: 1\n' + `    fingerprint: "${FP}"\n`, 'gnollbench')))
      .toContain('This file is for the Overseer sub-tab.');
    expect(messages(await parseFeedbackResolutionYaml('format: overseer-feedback-resolutions\nversion: 1\nsource: chat\n' + body, 'gnollbench')))
      .toContain('`source` must be `overseer` or `gnollbench`.');
  });

  it('R3: requires a non-empty list of at most 500', async () => {
    expect(messages(await parseFeedbackResolutionYaml(overseerHeader + 'issues: []\n', 'overseer'))).toEqual(['`issues` must be a non-empty list.']);
    expect(messages(await parseFeedbackResolutionYaml(overseerHeader, 'overseer'))).toEqual(['`issues` must be a non-empty list.']);

    const many = Array.from({ length: 501 }, (_, i) => ({ fingerprint: FP2, id: i + 1 }));
    const result = checkFeedbackResolutionDocument({ format: 'overseer-feedback-resolutions', version: 1, source: 'overseer', issues: many }, 'overseer');
    expect(messages(result)).toEqual(['`issues` can hold at most 500 items; this file has 501.']);
  });

  it('R4: refuses unknown keys at the top level and in an item, naming the item', async () => {
    const result = await parseFeedbackResolutionYaml(overseerHeader + [
      'comment: hello',
      'issues:',
      `  - id: 1`,
      `    fingerprint: "${FP2}"`,
      `  - id: 2`,
      `    fingerprint: "${FP2.replace(/^b/, 'c')}"`,
      `  - id: 3`,
      `    fingerprint: "${FP2.replace(/^b/, 'd')}"`,
      `  - id: 42`,
      `    fingerprint: "${FP}"`,
      '    state: resolved',
      ''
    ].join('\n'), 'overseer');
    expect(messages(result)).toContain('Unknown top-level key `comment`; allowed: format, version, source, exportedAtUtc, issues.');
    expect(messages(result).some(m => m.startsWith('issues[3] (id 42): unknown key `state`'))).toBe(true);
    expect(result.request).toBeNull();
  });

  it('R5: an Overseer item needs a positive integer id; a GnollBench item has none', async () => {
    const missing = await parseFeedbackResolutionYaml(overseerHeader + `issues:\n  - fingerprint: "${FP}"\n`, 'overseer');
    expect(messages(missing)).toEqual(['issues[0] (fingerprint 3f2a9c1b0d4e…): `id` is required in an Overseer file.']);

    const bad = await parseFeedbackResolutionYaml(overseerHeader + `issues:\n  - id: -3\n    fingerprint: "${FP}"\n  - id: "7"\n    fingerprint: "${FP2}"\n`, 'overseer');
    expect(messages(bad)).toEqual([
      'issues[0] (fingerprint 3f2a9c1b0d4e…): `id` must be a positive integer.',
      'issues[1] (fingerprint bbbbbbbbbbbb…): `id` must be a positive integer.'
    ]);

    const forbidden = await parseFeedbackResolutionYaml(gnollBenchHeader + `issues:\n  - id: 4\n    fingerprint: "${FP}"\n`, 'gnollbench');
    expect(messages(forbidden)).toEqual(['issues[0] (id 4): `id` is not allowed in a GnollBench file; GnollBench issues are named by fingerprint.']);
  });

  it('R6: requires a 64-character lowercase hex fingerprint', async () => {
    const result = await parseFeedbackResolutionYaml(overseerHeader + [
      'issues:',
      '  - id: 1',
      '  - id: 2',
      `    fingerprint: "${FP.toUpperCase()}"`,
      '  - id: 3',
      '    fingerprint: "abc"',
      ''
    ].join('\n'), 'overseer');
    expect(messages(result)).toEqual([
      'issues[0] (id 1): `fingerprint` is required.',
      'issues[1] (id 2): `fingerprint` must be 64 lowercase hexadecimal characters.',
      'issues[2] (id 3): `fingerprint` must be 64 lowercase hexadecimal characters.'
    ]);
  });

  it('R7: refuses a duplicate id or fingerprint', async () => {
    const ids = await parseFeedbackResolutionYaml(overseerHeader + `issues:\n  - id: 5\n    fingerprint: "${FP}"\n  - id: 5\n    fingerprint: "${FP2}"\n`, 'overseer');
    expect(messages(ids)).toEqual(['issues[1] (id 5): id 5 appears more than once (first at issues[0]).']);

    const fingerprints = await parseFeedbackResolutionYaml(gnollBenchHeader + `issues:\n  - fingerprint: "${FP}"\n    status: new\n  - fingerprint: "${FP}"\n    status: new\n`, 'gnollbench');
    expect(messages(fingerprints)).toEqual(['issues[1] (fingerprint 3f2a9c1b0d4e…): this fingerprint appears more than once (first at issues[0]).']);
  });

  it('R8: accepts the four statuses and gives regressed its own message', async () => {
    const result = await parseFeedbackResolutionYaml(gnollBenchHeader + [
      'issues:',
      `  - fingerprint: "${FP}"`,
      '    status: regressed',
      `  - fingerprint: "${FP2}"`,
      '    status: fixed',
      ''
    ].join('\n'), 'gnollbench');
    expect(messages(result)).toEqual([
      'issues[0] (fingerprint 3f2a9c1b0d4e…): `status` cannot be `regressed`: recurrence is recorded by the system.',
      'issues[1] (fingerprint bbbbbbbbbbbb…): unknown status `fixed`; use new, acknowledged, resolved or dismissed.'
    ]);

    for (const status of ['new', 'acknowledged', 'resolved', 'dismissed']) {
      const ok = await parseFeedbackResolutionYaml(gnollBenchHeader + `issues:\n  - fingerprint: "${FP}"\n    status: ${status}\n    resolution: Done.\n`, 'gnollbench');
      expect(ok.errors).toEqual([]);
      expect(ok.request?.items[0].status).toBe(status);
    }
  });

  it('R9: checks the lengths and the fixed-in version', async () => {
    const result = checkFeedbackResolutionDocument({
      format: 'overseer-feedback-resolutions', version: 1, source: 'gnollbench', exportedAtUtc: '2026-10-23T12:34:56Z',
      issues: [
        { fingerprint: FP, note: 'n'.repeat(1001) },
        { fingerprint: FP2, resolution: 'r'.repeat(4001) },
        { fingerprint: 'c'.repeat(64), fixedInVersion: '1.4 beta' },
        { fingerprint: 'd'.repeat(64), fixedInVersion: 1.4 },
        { fingerprint: 'e'.repeat(64), fixedInVersion: 'v'.repeat(65) },
        { fingerprint: 'f'.repeat(64), note: 12 }
      ]
    }, 'gnollbench');
    expect(messages(result)).toEqual([
      'issues[0] (fingerprint 3f2a9c1b0d4e…): `note` can be at most 1000 characters; it has 1001.',
      'issues[1] (fingerprint bbbbbbbbbbbb…): `resolution` can be at most 4000 characters; it has 4001.',
      'issues[2] (fingerprint cccccccccccc…): `fixedInVersion` may contain only letters, digits, \'.\', \'+\' and \'-\'.',
      'issues[3] (fingerprint dddddddddddd…): `fixedInVersion` must be quoted text, for example "1.4.1".',
      'issues[4] (fingerprint eeeeeeeeeeee…): `fixedInVersion` can be at most 64 characters; it has 65.',
      'issues[5] (fingerprint ffffffffffff…): `note` must be text.'
    ]);

    const atLimit = checkFeedbackResolutionDocument({
      format: 'overseer-feedback-resolutions', version: 1, source: 'gnollbench', exportedAtUtc: '2026-10-23T12:34:56Z',
      issues: [{ fingerprint: FP, note: 'n'.repeat(1000), resolution: 'r'.repeat(4000), fixedInVersion: '1.4.1+build-7' }]
    }, 'gnollbench');
    expect(atLimit.errors).toEqual([]);
  });

  it('keeps a value given as null and says how to clear it', async () => {
    const result = await parseFeedbackResolutionYaml(gnollBenchHeader + `issues:\n  - fingerprint: "${FP}"\n    status:\n    note:\n    resolution: Fixed.\n`, 'gnollbench');
    expect(result.errors).toEqual([]);
    expect(result.request?.items[0]).toEqual({ fingerprint: FP, resolution: 'Fixed.' });
    expect(result.notices).toEqual([
      'issues[0] (fingerprint 3f2a9c1b0d4e…): `status` is empty, so the stored status is kept.',
      'issues[0] (fingerprint 3f2a9c1b0d4e…): `note` is empty, so the stored note is kept; write `note: ""` to clear it.'
    ]);
  });

  it('R10, R11: notices for an item that changes nothing and a resolved issue without a resolution', async () => {
    const result = await parseFeedbackResolutionYaml(overseerHeader + [
      'issues:',
      '  - id: 1',
      `    fingerprint: "${FP}"`,
      '  - id: 2',
      `    fingerprint: "${FP2}"`,
      '    status: resolved',
      '    resolution: ""',
      ''
    ].join('\n'), 'overseer');
    expect(result.errors).toEqual([]);
    expect(result.request).not.toBeNull();
    expect(result.notices).toEqual([
      'issues[0] (id 1): this item changes nothing.',
      'issues[1] (id 2): a resolved issue should say what was done.'
    ]);
  });

  it('R12: a file without exportedAtUtc is a notice; a malformed one is an error', async () => {
    const without = await parseFeedbackResolutionYaml(
      `format: overseer-feedback-resolutions\nversion: 1\nsource: gnollbench\nissues:\n  - fingerprint: "${FP}"\n    status: new\n`, 'gnollbench');
    expect(without.errors).toEqual([]);
    expect(without.notices).toEqual(['The file has no `exportedAtUtc`, so changes since the export cannot be detected.']);
    expect(without.request?.exportedAtUtc).toBeUndefined();

    const unquoted = await parseFeedbackResolutionYaml(
      `format: overseer-feedback-resolutions\nversion: 1\nsource: gnollbench\nexportedAtUtc: 2026-10-23T12:34:56Z\nissues:\n  - fingerprint: "${FP}"\n    status: new\n`, 'gnollbench');
    expect(unquoted.errors).toEqual([]);
    expect(new Date(unquoted.request!.exportedAtUtc!).toISOString()).toBe('2026-10-23T12:34:56.000Z');

    const malformed = await parseFeedbackResolutionYaml(
      `format: overseer-feedback-resolutions\nversion: 1\nsource: gnollbench\nexportedAtUtc: "yesterday"\nissues:\n  - fingerprint: "${FP}"\n    status: new\n`, 'gnollbench');
    expect(messages(malformed)).toEqual(['`exportedAtUtc` must be a UTC time such as "2026-10-23T12:34:56Z", as the export wrote it.']);
  });

  it('reads the resolution template an export writes, once its comments are filled in', async () => {
    const template = [
      'format: overseer-feedback-resolutions',
      'version: 1',
      'source: overseer',
      'exportedAtUtc: "2026-10-23T12:34:56Z"',
      'issues:',
      '  - id: 42',
      `    fingerprint: "${FP}"`,
      '    status: resolved          # new | acknowledged | resolved | dismissed; now new',
      '    resolution: |',
      '      What changed, where, and how it was verified.',
      '    # fixedInVersion: "1.4.1"',
      ''
    ].join('\r\n');
    const result = await parseFeedbackResolutionYaml('﻿' + template, 'overseer');
    expect(result.errors).toEqual([]);
    expect(result.request?.items).toEqual([{ id: 42, fingerprint: FP, status: 'resolved', resolution: 'What changed, where, and how it was verified.' }]);
  });

  it('R13: a note or resolution with hidden characters is a notice; clean text is not', async () => {
    const rlo = String.fromCodePoint(0x202e);
    const zwsp = String.fromCodePoint(0x200b);
    const result = await parseFeedbackResolutionYaml(overseerHeader + [
      'issues:',
      '  - id: 42',
      `    fingerprint: "${FP}"`,
      '    status: resolved',
      `    resolution: "Fixed ${rlo}txet"`,
      `    note: "Looked${zwsp}into it"`,
      '  - id: 43',
      `    fingerprint: "${FP2}"`,
      '    status: resolved',
      '    resolution: "Fixed the tool."',
      '    note: "Clean."',
      ''
    ].join('\n'), 'overseer');

    expect(result.errors).toEqual([]);
    expect(result.notices).toEqual([
      'issues[0] (id 42): the note holds hidden characters (U+200B). Check the text before applying.',
      'issues[0] (id 42): the resolution holds hidden characters (U+202E). Check the text before applying.'
    ]);
    expect(result.request?.items.length).toBe(2);
  });

  it('reports a top-level __proto__ key as unknown and pollutes no prototype', async () => {
    const result = await parseFeedbackResolutionYaml(overseerHeader + [
      '__proto__:',
      '  polluted: true',
      'issues:',
      '  - id: 42',
      `    fingerprint: "${FP}"`,
      '    status: new',
      ''
    ].join('\n'), 'overseer');

    expect(result.request).toBeNull();
    expect(messages(result)).toContain('Unknown top-level key `__proto__`; allowed: format, version, source, exportedAtUtc, issues.');
    expect(({} as Record<string, unknown>)['polluted']).toBeUndefined();
  });

  it('completes on a document with deeply nested aliases', async () => {
    const lines = ['a0: &a0 [' + Array(10).fill('"x"').join(', ') + ']'];
    for (let level = 1; level < 10; level++) {
      lines.push(`a${level}: &a${level} [` + Array(10).fill(`*a${level - 1}`).join(', ') + ']');
    }
    const started = performance.now();
    const result = await parseFeedbackResolutionYaml(overseerHeader + lines.join('\n') + '\nissues: []\n', 'overseer');
    const elapsed = performance.now() - started;

    expect(result.request).toBeNull();
    expect(messages(result)).toContain('Unknown top-level key `a9`; allowed: format, version, source, exportedAtUtc, issues.');
    expect(messages(result)).toContain('`issues` must be a non-empty list.');
    expect(elapsed).toBeLessThan(2000);
    console.log(`feedback-resolution-yaml: deeply nested aliases parsed and checked in ${elapsed.toFixed(1)} ms`);
  });
});
