import type { ParseIssue } from '../../benchmark/question-yaml/question-yaml-format';
import type {
  FeedbackResolutionItem,
  FeedbackResolutionRequest,
  FeedbackResolutionStatus
} from '../../../services/feedback-transfer.service';
import type { FeedbackSource } from './feedback-view-storage';
import { findHiddenCharacters } from './feedback-hidden-characters';

/**
 * The resolution file an agent writes from a feedback export (`overseer-feedback-resolutions`, version 1), and the
 * client's parse and schema pass over it, rules R1–R13 of the format. Errors block the import; notices do not. The
 * server runs R1–R9 again on preview and apply. Parsing is `js-yaml`, loaded lazily.
 *
 * ```yaml
 * format: overseer-feedback-resolutions
 * version: 1
 * source: overseer
 * exportedAtUtc: "2026-10-23T12:34:56Z"
 * issues:
 *   - id: 42
 *     fingerprint: "3f2a…"
 *     status: resolved
 *     resolution: |
 *       What changed, where, and how it was verified.
 *     fixedInVersion: "1.4.1"
 *     note: |
 *       The triage note.
 * ```
 */

export type { ParseIssue };

export const FEEDBACK_RESOLUTION_FORMAT = 'overseer-feedback-resolutions';
export const FEEDBACK_RESOLUTION_VERSION = 1;
export const MAX_RESOLUTION_ITEMS = 500;
export const MAX_NOTE_LENGTH = 1000;
export const MAX_RESOLUTION_LENGTH = 4000;
export const MAX_FIXED_IN_VERSION_LENGTH = 64;

const FIXED_IN_VERSION_PATTERN = /^[0-9A-Za-z.+-]{1,64}$/;
const FINGERPRINT_PATTERN = /^[0-9a-f]{64}$/;
/** An ISO 8601 date and time with a UTC designator or an offset. */
const UTC_TIME_PATTERN = /^\d{4}-\d{2}-\d{2}[Tt ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?\s*([Zz]|[+-]\d{2}(:?\d{2})?)$/;

const TOP_LEVEL_KEYS = ['format', 'version', 'source', 'exportedAtUtc', 'issues'];
const ITEM_KEYS = ['id', 'fingerprint', 'status', 'resolution', 'fixedInVersion', 'note'];
const STATUSES: readonly FeedbackResolutionStatus[] = ['new', 'acknowledged', 'resolved', 'dismissed'];

/** The sub-tab name a source's file belongs to, as the R2 message says it. */
const SUB_TAB_NAMES: Readonly<Record<FeedbackSource, string>> = {
  overseer: 'Overseer',
  gnollbench: 'GnollBench'
};

export interface FeedbackResolutionParseResult {
  /** The request body for preview and apply; null while there is any error. */
  request: FeedbackResolutionRequest | null;
  errors: ParseIssue[];
  notices: string[];
}

/** Parses and schema-checks a resolution file for the sub-tab `expectedSource`. Syntax errors carry a 1-based line. */
export async function parseFeedbackResolutionYaml(text: string, expectedSource: FeedbackSource): Promise<FeedbackResolutionParseResult> {
  const source = (text ?? '').replace(/^﻿/, '');
  if (source.trim() === '' || source.split(/\r?\n/).every(l => l.trim() === '' || l.trim().startsWith('#'))) {
    return { request: null, errors: [{ line: null, message: 'The document is empty.' }], notices: [] };
  }

  let doc: unknown;
  try {
    const yaml = await import('js-yaml');
    doc = yaml.load(source);
  } catch (err) {
    return { request: null, errors: [syntaxIssue(err)], notices: [] };
  }
  return checkFeedbackResolutionDocument(doc, expectedSource);
}

/** R1–R13 over a document already parsed from YAML. */
export function checkFeedbackResolutionDocument(doc: unknown, expectedSource: FeedbackSource): FeedbackResolutionParseResult {
  const errors: ParseIssue[] = [];
  const notices: string[] = [];
  const fail = (): FeedbackResolutionParseResult => ({ request: null, errors, notices });
  const error = (message: string): void => { errors.push({ line: null, message }); };

  // R1
  const header = `The document must be a YAML mapping with \`format: ${FEEDBACK_RESOLUTION_FORMAT}\` and \`version: ${FEEDBACK_RESOLUTION_VERSION}\`.`;
  if (!isMapping(doc) || doc['format'] !== FEEDBACK_RESOLUTION_FORMAT || !('version' in doc)) {
    error(header);
    return fail();
  }
  if (doc['version'] !== FEEDBACK_RESOLUTION_VERSION) {
    error(`Unsupported \`version: ${String(doc['version'])}\`; this importer understands version ${FEEDBACK_RESOLUTION_VERSION}.`);
    return fail();
  }

  // R4, top level
  for (const key of Object.keys(doc)) {
    if (!TOP_LEVEL_KEYS.includes(key)) {
      error(`Unknown top-level key \`${key}\`; allowed: ${TOP_LEVEL_KEYS.join(', ')}.`);
    }
  }

  // R2
  const rawSource = doc['source'];
  let fileSource: FeedbackSource = expectedSource;
  if (rawSource !== 'overseer' && rawSource !== 'gnollbench') {
    error('`source` must be `overseer` or `gnollbench`.');
  } else {
    fileSource = rawSource;
    if (rawSource !== expectedSource) {
      error(`This file is for the ${SUB_TAB_NAMES[rawSource]} sub-tab.`);
    }
  }

  // R12, and the time's own shape
  let exportedAtUtc: string | undefined;
  const rawExported = doc['exportedAtUtc'];
  if (rawExported === undefined || rawExported === null || (typeof rawExported === 'string' && rawExported.trim() === '')) {
    notices.push('The file has no `exportedAtUtc`, so changes since the export cannot be detected.');
  } else if (rawExported instanceof Date && !Number.isNaN(rawExported.getTime())) {
    // An unquoted YAML timestamp arrives as a Date, read as UTC.
    exportedAtUtc = rawExported.toISOString();
  } else if (typeof rawExported === 'string' && UTC_TIME_PATTERN.test(rawExported.trim()) && !Number.isNaN(Date.parse(rawExported.trim()))) {
    exportedAtUtc = rawExported.trim();
  } else {
    error('`exportedAtUtc` must be a UTC time such as "2026-10-23T12:34:56Z", as the export wrote it.');
  }

  // R3
  const issues = doc['issues'];
  if (!Array.isArray(issues) || issues.length === 0) {
    error('`issues` must be a non-empty list.');
    return fail();
  }
  if (issues.length > MAX_RESOLUTION_ITEMS) {
    error(`\`issues\` can hold at most ${MAX_RESOLUTION_ITEMS} items; this file has ${issues.length}.`);
    return fail();
  }

  const overseer = fileSource === 'overseer';
  const firstById = new Map<number, number>();
  const firstByFingerprint = new Map<string, number>();
  const items: FeedbackResolutionItem[] = [];

  issues.forEach((raw: unknown, index: number) => {
    if (!isMapping(raw)) {
      error(`issues[${index}] must be a mapping with at least \`fingerprint\`.`);
      return;
    }
    const before = errors.length;

    const rawId = raw['id'];
    const idValid = typeof rawId === 'number' && Number.isInteger(rawId) && rawId > 0;
    const rawFingerprint = raw['fingerprint'];
    const fingerprintValid = typeof rawFingerprint === 'string' && FINGERPRINT_PATTERN.test(rawFingerprint);
    const label = idValid
      ? `issues[${index}] (id ${rawId})`
      : fingerprintValid ? `issues[${index}] (fingerprint ${(rawFingerprint as string).slice(0, 12)}…)` : `issues[${index}]`;

    // R4, item
    for (const key of Object.keys(raw)) {
      if (!ITEM_KEYS.includes(key)) {
        error(`${label}: unknown key \`${key}\`; allowed: ${ITEM_KEYS.join(', ')}.`);
      }
    }

    const item: FeedbackResolutionItem = { fingerprint: '' };

    // R5, R7 for ids
    if (overseer) {
      if (!('id' in raw) || rawId === null) {
        error(`${label}: \`id\` is required in an Overseer file.`);
      } else if (!idValid) {
        error(`${label}: \`id\` must be a positive integer.`);
      } else {
        const id = rawId as number;
        const first = firstById.get(id);
        if (first !== undefined) {
          error(`${label}: id ${id} appears more than once (first at issues[${first}]).`);
        } else {
          firstById.set(id, index);
        }
        item.id = id;
      }
    } else if ('id' in raw) {
      error(`${label}: \`id\` is not allowed in a GnollBench file; GnollBench issues are named by fingerprint.`);
    }

    // R6, R7 for fingerprints
    if (!('fingerprint' in raw) || rawFingerprint === null || rawFingerprint === '') {
      error(`${label}: \`fingerprint\` is required.`);
    } else if (!fingerprintValid) {
      error(`${label}: \`fingerprint\` must be 64 lowercase hexadecimal characters.`);
    } else {
      const fingerprint = rawFingerprint as string;
      const first = firstByFingerprint.get(fingerprint);
      if (first !== undefined) {
        error(`${label}: this fingerprint appears more than once (first at issues[${first}]).`);
      } else {
        firstByFingerprint.set(fingerprint, index);
      }
      item.fingerprint = fingerprint;
    }

    // R8
    if ('status' in raw) {
      const rawStatus = raw['status'];
      if (rawStatus === null) {
        notices.push(`${label}: \`status\` is empty, so the stored status is kept.`);
      } else {
        const status = typeof rawStatus === 'string' ? rawStatus.trim().toLowerCase() : null;
        if (status === 'regressed') {
          error(`${label}: \`status\` cannot be \`regressed\`: recurrence is recorded by the system.`);
        } else if (status === null || !STATUSES.includes(status as FeedbackResolutionStatus)) {
          error(`${label}: unknown status \`${String(rawStatus)}\`; use new, acknowledged, resolved or dismissed.`);
        } else {
          item.status = status as FeedbackResolutionStatus;
        }
      }
    }

    // R9
    const note = readText(raw, 'note', 'note', MAX_NOTE_LENGTH, label, errors, notices);
    if (note !== undefined) item.note = note;
    const resolution = readText(raw, 'resolution', 'resolution', MAX_RESOLUTION_LENGTH, label, errors, notices);
    if (resolution !== undefined) item.resolution = resolution;
    const fixedInVersion = readFixedInVersion(raw, label, errors, notices);
    if (fixedInVersion !== undefined) item.fixedInVersion = fixedInVersion;

    if (errors.length > before) return;

    // R10
    if (item.status === undefined && item.note === undefined && item.resolution === undefined && item.fixedInVersion === undefined) {
      notices.push(`${label}: this item changes nothing.`);
    }
    // R11: only a cleared resolution is known here; the preview also knows the stored one.
    if (item.status === 'resolved' && item.resolution === '') {
      notices.push(`${label}: a resolved issue should say what was done.`);
    }
    // R13
    for (const [noun, text] of [['note', item.note], ['resolution', item.resolution]] as const) {
      const hidden = findHiddenCharacters(text);
      if (hidden.length > 0) {
        notices.push(`${label}: the ${noun} holds hidden characters (${hidden.join(', ')}). Check the text before applying.`);
      }
    }
    items.push(item);
  });

  if (errors.length > 0) return fail();

  const request: FeedbackResolutionRequest = {
    format: FEEDBACK_RESOLUTION_FORMAT,
    version: FEEDBACK_RESOLUTION_VERSION,
    source: fileSource,
    items
  };
  if (exportedAtUtc !== undefined) request.exportedAtUtc = exportedAtUtc;
  return { request, errors, notices };
}

/** A text field: undefined keeps the stored value, `''` clears it. Pushes an error or notice when it must. */
function readText(raw: Record<string, unknown>, key: string, noun: string, maxLength: number, label: string,
  errors: ParseIssue[], notices: string[]): string | undefined {
  if (!(key in raw)) return undefined;
  const value = raw[key];
  if (value === null) {
    notices.push(`${label}: \`${key}\` is empty, so the stored ${noun} is kept; write \`${key}: ""\` to clear it.`);
    return undefined;
  }
  if (typeof value !== 'string') {
    errors.push({ line: null, message: `${label}: \`${key}\` must be text.` });
    return undefined;
  }
  const text = cleanText(value);
  const length = text.trim().length;
  if (length > maxLength) {
    errors.push({ line: null, message: `${label}: \`${key}\` can be at most ${maxLength} characters; it has ${length}.` });
    return undefined;
  }
  return text;
}

function readFixedInVersion(raw: Record<string, unknown>, label: string, errors: ParseIssue[], notices: string[]): string | undefined {
  if (!('fixedInVersion' in raw)) return undefined;
  const value = raw['fixedInVersion'];
  if (typeof value === 'number') {
    // An unquoted 1.4 reads as a number, and 1.10 as 1.1; only quoted text keeps the version as written.
    errors.push({ line: null, message: `${label}: \`fixedInVersion\` must be quoted text, for example "1.4.1".` });
    return undefined;
  }
  const text = readText(raw, 'fixedInVersion', 'fixed-in version', MAX_FIXED_IN_VERSION_LENGTH, label, errors, notices);
  if (text === undefined || text === '') return text;
  const trimmed = text.trim();
  if (!FIXED_IN_VERSION_PATTERN.test(trimmed)) {
    errors.push({ line: null, message: `${label}: \`fixedInVersion\` may contain only letters, digits, '.', '+' and '-'.` });
    return undefined;
  }
  return trimmed;
}

/** Normalizes line endings and drops trailing whitespace; a blank text becomes `''`. */
function cleanText(value: string): string {
  const text = value.replace(/\r\n?/g, '\n').replace(/\s+$/, '');
  return text.trim() === '' ? '' : text;
}

function isMapping(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value) && !(value instanceof Date);
}

function syntaxIssue(err: unknown): ParseIssue {
  const e = err as { reason?: string; message?: string; mark?: { line?: number; column?: number } };
  const reason = e?.reason ?? e?.message ?? String(err);
  const line = typeof e?.mark?.line === 'number' ? e.mark.line + 1 : null;
  const column = typeof e?.mark?.column === 'number' ? e.mark.column + 1 : null;
  if (line !== null && column !== null) {
    return { line, message: `Line ${line}, column ${column}: ${reason}` };
  }
  return { line, message: `YAML syntax error: ${reason}` };
}
