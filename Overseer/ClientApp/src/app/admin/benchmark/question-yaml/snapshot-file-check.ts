/**
 * A local, advisory look at a file the admin picked in the Snapshot Suite Wizard: is it a GnollHack
 * AI snapshot, which form is it in, and could it exceed the board cap. Nothing is uploaded, and
 * nothing here ever blocks the wizard.
 */

/** The server's board cap, in characters of flattened text. */
export const SNAPSHOT_CHAR_CAP = 60000;

export interface SnapshotFileCheck {
  looksLikeSnapshot: boolean;
  /** The server's `LooksLikeHtml` rule: starts with `<` and holds an html, body or pre tag. */
  format: 'html' | 'text';
  charCount: number;
  /** Raw HTML is larger than the text it flattens to, so for HTML this only means "may be cut". */
  exceedsCap: boolean;
  /** The first line naming GnollHack, without markup; null when there is none. */
  banner: string | null;
  /** An Overseer benchmark YAML, whose board text would otherwise pass for a snapshot. */
  isSuiteYaml: boolean;
}

export function looksLikeHtml(text: string): boolean {
  const trimmed = (text ?? '').replace(/^﻿/, '').trimStart();
  if (!trimmed.startsWith('<')) {
    return false;
  }
  const lower = trimmed.toLowerCase();
  return lower.includes('<html') || lower.includes('<body') || lower.includes('<pre');
}

export function checkSnapshotFile(_name: string, text: string): SnapshotFileCheck {
  const content = text ?? '';
  const format = looksLikeHtml(content) ? 'html' : 'text';
  const isSuiteYaml = /^format:\s*["']?overseer-benchmark-questions/m.test(content);
  const bannerLine = content
    .split(/\r\n?|\n/)
    .map(l => l.replace(/<[^>]*>/g, '').trim())
    .find(l => l.includes('GnollHack'));
  return {
    looksLikeSnapshot: !isSuiteYaml && bannerLine !== undefined,
    format,
    charCount: content.length,
    exceedsCap: content.length > SNAPSHOT_CHAR_CAP,
    banner: bannerLine ? bannerLine.slice(0, 160) : null,
    isSuiteYaml
  };
}

/** One status sentence for the wizard. */
export function describeSnapshotFileCheck(fileName: string, check: SnapshotFileCheck): string {
  const size = `${check.charCount.toLocaleString('en-US')} characters`;
  if (check.isSuiteYaml) {
    return `${fileName} looks like an Overseer suite YAML, not a snapshot; that is the other route. You can still use it.`;
  }
  if (!check.looksLikeSnapshot) {
    return `${fileName} does not look like a GnollHack AI snapshot: no GnollHack banner was found. You can still use it.`;
  }
  const form = check.format === 'html' ? 'an exported .ai.html snapshot' : 'a snapshot text file';
  const cap = check.exceedsCap
    ? check.format === 'html'
      ? ` It is over ${SNAPSHOT_CHAR_CAP.toLocaleString('en-US')} characters as HTML, and the board may be cut once it is flattened.`
      : ` It is over ${SNAPSHOT_CHAR_CAP.toLocaleString('en-US')} characters, so the board may be cut.`
    : '';
  return `${fileName} looks like ${form} (${size}).${cap}`;
}
