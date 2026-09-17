import { SNAPSHOT_CHAR_CAP, checkSnapshotFile, describeSnapshotFileCheck, looksLikeHtml } from './snapshot-file-check';

describe('snapshot-file-check', () => {
  it('applies the server rule for HTML', () => {
    expect(looksLikeHtml('﻿  <!DOCTYPE html><html><body>x')).toBeTrue();
    expect(looksLikeHtml('<pre>GnollHack</pre>')).toBeTrue();
    expect(looksLikeHtml('<div>GnollHack</div>')).toBeFalse();
    expect(looksLikeHtml('GnollHack 4.2.0 <html>')).toBeFalse();
  });

  it('recognises an exported HTML snapshot and reads its banner without markup', () => {
    const check = checkSnapshotFile('a.ai.html', '<html><body><pre><b>GnollHack 4.2.0 Build 47</b>\nDlvl:11</pre></body></html>');
    expect(check.format).toBe('html');
    expect(check.looksLikeSnapshot).toBeTrue();
    expect(check.banner).toBe('GnollHack 4.2.0 Build 47');
    expect(check.exceedsCap).toBeFalse();
  });

  it('recognises a text snapshot and flags a file over the cap', () => {
    const check = checkSnapshotFile('b.snapshot.txt', 'GnollHack 4.2.0\n' + 'x'.repeat(SNAPSHOT_CHAR_CAP));
    expect(check.format).toBe('text');
    expect(check.looksLikeSnapshot).toBeTrue();
    expect(check.exceedsCap).toBeTrue();
    expect(describeSnapshotFileCheck('b.snapshot.txt', check)).toContain('may be cut');
  });

  it('warns about a file without a banner, without refusing it', () => {
    const check = checkSnapshotFile('notes.txt', 'Dlvl:11 HP:14(58)\n');
    expect(check.looksLikeSnapshot).toBeFalse();
    expect(check.banner).toBeNull();
    expect(describeSnapshotFileCheck('notes.txt', check)).toContain('does not look like a GnollHack AI snapshot');
    expect(describeSnapshotFileCheck('notes.txt', check)).toContain('You can still use it.');
  });

  it('does not take a suite YAML carrying a board for a snapshot', () => {
    const check = checkSnapshotFile('s.yaml', 'format: overseer-benchmark-questions\nsuite:\n  snapshot:\n    text: |\n      GnollHack 4.2.0\n');
    expect(check.isSuiteYaml).toBeTrue();
    expect(check.looksLikeSnapshot).toBeFalse();
    expect(describeSnapshotFileCheck('s.yaml', check)).toContain('that is the other route');
  });
});
