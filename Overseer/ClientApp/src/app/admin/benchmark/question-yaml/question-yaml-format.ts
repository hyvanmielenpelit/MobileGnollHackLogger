import type {
  BenchmarkQuestionDto,
  BenchmarkSuiteDto,
  ImportBenchmarkQuestionItem,
  ImportBenchmarkSuiteSnapshot,
  RubricAuthoringGuidance
} from '../../../services/admin-benchmark.service';
import { formatDifficulty } from '../../../utils/model-badge-format.util';

/**
 * The YAML interchange format for benchmark questions and suites.
 *
 * This file is the source of truth for the format; `docs/overseer/ai-benchmark.md` § YAML Import
 * and Export mirrors its rules. Parsing is `js-yaml` (loaded lazily, so the export path never
 * downloads it) followed by a schema pass with precise messages. Serializing is hand-written so
 * that every multi-line value is a `|` block scalar a human can edit.
 */

export type ParsedDifficulty = 1 | 2 | 3;

export interface ParsedQuestion {
  index: number;
  id: number | null;
  difficulty: ParsedDifficulty | null;
  /** Null when the key is absent. */
  questionText: string | null;
  /** Null when the key is absent; '' clears the rubric. */
  rubric: string | null;
}

/** The game snapshot a document carries: the board itself plus the metadata of the stored one. */
export interface ParsedSnapshot {
  name: string | null;
  gnollhackVersion: string | null;
  /** ISO 8601, normalized from a quoted string or an unquoted YAML timestamp. */
  capturedAt: string | null;
  notes: string | null;
  /** The hash of the board this file was exported from; a mismatch only warns. */
  sha256: string | null;
  /** The board the questions are written against. A suite import attaches it. */
  text: string;
}

export interface ParsedSuite {
  name: string | null;
  description: string | null;
  /** Read only by the Snapshot Suite Wizard's add-questions route; no export writes it. */
  suggestedDescription: string | null;
  snapshot: ParsedSnapshot | null;
}

export interface ParseIssue {
  line: number | null;
  message: string;
}

export interface ParseResult {
  suite: ParsedSuite | null;
  questions: ParsedQuestion[];
  errors: ParseIssue[];
  notices: string[];
}

export type ImportMode = 'single' | 'questions' | 'suite';

export const QUESTION_YAML_FORMAT = 'overseer-benchmark-questions';
export const QUESTION_YAML_VERSION = 1;

const TOP_LEVEL_KEYS = ['format', 'version', 'suite', 'questions'];
const SUITE_KEYS = ['name', 'description', 'suggested_description', 'snapshot'];
const SNAPSHOT_KEYS = ['name', 'gnollhack_version', 'captured_at', 'snapshot_format', 'notes', 'sha256', 'text'];
const QUESTION_KEYS = ['id', 'difficulty', 'question', 'rubric'];
export const MAX_SUITE_NAME_LENGTH = 128;
export const MAX_SNAPSHOT_NAME_LENGTH = 128;
export const MAX_GNOLLHACK_VERSION_LENGTH = 64;

/** The snapshot an export writes into `suite.snapshot`; every key but `text` may be null. */
export interface SnapshotExport {
  name: string | null;
  gnollhackVersion: string | null;
  capturedAtUtc: string | null;
  notes: string | null;
  sha256: string | null;
  text: string;
  /** The board's `Snapshot format: N` at capture; informational, ignored on import. */
  snapshotFormat: number | null;
}

// ---------------------------------------------------------------------------------------------
// Serializer
// ---------------------------------------------------------------------------------------------

/**
 * Serializes questions, with a `suite` block naming their suite when one is given. A snapshot,
 * when given with a suite, is written as the `suite.snapshot` mapping.
 */
export function serializeQuestionsYaml(questions: BenchmarkQuestionDto[], suite: BenchmarkSuiteDto | null, snapshot?: SnapshotExport | null): string {
  return serialize(questions, suite, false, snapshot);
}

/** Serializes a whole suite: name, description, the attached snapshot, and every question. */
export function serializeSuiteYaml(suite: BenchmarkSuiteDto, questions: BenchmarkQuestionDto[], snapshot?: SnapshotExport | null): string {
  return serialize(questions, suite, true, snapshot);
}

function serialize(
  questions: BenchmarkQuestionDto[],
  suite: BenchmarkSuiteDto | null,
  includeDescription: boolean,
  snapshot?: SnapshotExport | null
): string {
  // A snapshot whose text could not be fetched writes no `snapshot` key: the mapping needs a board.
  const hasSnapshot = !!suite && !!snapshot && !!snapshot.text && snapshot.text.trim() !== '';
  const out: string[] = [
    '# Overseer benchmark questions. Edit freely; keep every `id` you were given.',
    '# A question without `id` is created as new. Omit `rubric` to keep the current rubric.'
  ];
  if (hasSnapshot) {
    out.push('# suite.snapshot is the board the questions are written against. A suite import attaches it; a questions import ignores it.');
  }
  out.push(`format: ${QUESTION_YAML_FORMAT}`, `version: ${QUESTION_YAML_VERSION}`);

  if (suite) {
    out.push('', 'suite:');
    out.push(`  name: ${quoted(suite.name)}`);
    if (includeDescription && suite.description && suite.description.trim() !== '') {
      out.push(...blockScalar('description', suite.description, 2));
    }
    if (hasSnapshot) {
      const s = snapshot!;
      out.push('  snapshot:');
      if (s.name) {
        out.push(`    name: ${quoted(s.name)}`);
      }
      if (s.gnollhackVersion) {
        out.push(`    gnollhack_version: ${quoted(s.gnollhackVersion)}`);
      }
      if (s.capturedAtUtc) {
        out.push(`    captured_at: ${quoted(s.capturedAtUtc)}`);
      }
      if (s.snapshotFormat != null) {
        out.push(`    snapshot_format: ${s.snapshotFormat}`);
      }
      if (s.notes && s.notes.trim() !== '') {
        out.push(...blockScalar('notes', s.notes, 4));
      }
      if (s.sha256) {
        out.push(`    sha256: ${quoted(s.sha256)}`);
      }
      out.push(...blockScalar('text', s.text, 4));
    }
  }

  // An empty list is written as a flow sequence: a bare `questions:` would read back as null.
  out.push('', questions.length === 0 ? 'questions: []' : 'questions:');
  questions.forEach((q, i) => {
    if (i > 0) {
      out.push('');
    }
    out.push(`  # Question ${q.orderIndex}`);
    out.push(`  - id: ${q.id}`);
    out.push(`    difficulty: ${formatDifficulty(q.difficulty)}`);
    out.push(...blockScalar('question', q.questionText ?? '', 4));
    if (q.expectedPoints && q.expectedPoints.trim() !== '') {
      out.push(...blockScalar('rubric', q.expectedPoints, 4));
    }
  });

  return out.join('\n') + '\n';
}

/** A JSON string is a valid YAML double-quoted scalar, and quoting keeps `:` and `#` in names safe. */
function quoted(value: string): string {
  return JSON.stringify(value);
}

/**
 * Writes `key: |` followed by the value indented two spaces deeper than the key. The indentation
 * indicator is written when the first non-blank line starts with whitespace, because YAML would
 * otherwise take that whitespace as the block's indentation and drop it.
 */
function blockScalar(key: string, value: string, keyIndent: number): string[] {
  const pad = ' '.repeat(keyIndent);
  const normalized = value.replace(/\r\n?/g, '\n').replace(/\s+$/, '');
  if (normalized === '') {
    return [`${pad}${key}: ""`];
  }

  const lines = normalized.split('\n');
  const firstContent = lines.find(l => l.trim() !== '') ?? '';
  const indicator = /^[ \t]/.test(firstContent) ? '2' : '';
  const contentPad = ' '.repeat(keyIndent + 2);

  return [
    `${pad}${key}: |${indicator}`,
    ...lines.map(l => (l === '' ? '' : contentPad + l))
  ];
}

// ---------------------------------------------------------------------------------------------
// Parser
// ---------------------------------------------------------------------------------------------

/** Parses and schema-checks a document. Syntax errors carry a 1-based line number. */
export async function parseQuestionYaml(text: string): Promise<ParseResult> {
  const result: ParseResult = { suite: null, questions: [], errors: [], notices: [] };
  const source = (text ?? '').replace(/^﻿/, '');

  if (source.trim() === '' || source.split(/\r?\n/).every(l => l.trim() === '' || l.trim().startsWith('#'))) {
    result.errors.push({ line: null, message: 'The document is empty.' });
    return result;
  }

  let doc: unknown;
  try {
    const yaml = await import('js-yaml');
    doc = yaml.load(source);
  } catch (err) {
    result.errors.push(syntaxIssue(err));
    return result;
  }

  checkSchema(doc, result);
  return result;
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

function isMapping(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** Names the current shape for a document written against the flat keys this replaced. */
const OLD_SNAPSHOT_SHAPE_MESSAGE =
  '`suite.snapshot` is now a mapping with `name` and `text`; `suite.snapshot_text` is no longer a key.';

/** Checks the `suite.snapshot` mapping; returns null when it is unusable, with the errors pushed. */
function checkSnapshot(value: unknown, errors: ParseIssue[]): ParsedSnapshot | null {
  if (!isMapping(value)) {
    errors.push({ line: null, message: OLD_SNAPSHOT_SHAPE_MESSAGE });
    return null;
  }

  for (const key of Object.keys(value)) {
    if (!SNAPSHOT_KEYS.includes(key)) {
      errors.push({ line: null, message: `Unknown key \`suite.snapshot.${key}\`; allowed: ${SNAPSHOT_KEYS.join(', ')}.` });
    }
  }

  const parsed: ParsedSnapshot = { name: null, gnollhackVersion: null, capturedAt: null, notes: null, sha256: null, text: '' };

  if ('name' in value && value['name'] !== null) {
    const name = value['name'];
    const trimmed = typeof name === 'string' ? name.trim() : null;
    if (trimmed === null || trimmed.length < 1 || trimmed.length > MAX_SNAPSHOT_NAME_LENGTH) {
      errors.push({ line: null, message: `\`suite.snapshot.name\` must be 1–${MAX_SNAPSHOT_NAME_LENGTH} characters.` });
    } else {
      parsed.name = trimmed;
    }
  }

  if ('gnollhack_version' in value && value['gnollhack_version'] !== null) {
    const version = value['gnollhack_version'];
    const trimmed = typeof version === 'string' ? version.trim() : String(version).trim();
    if (trimmed.length > MAX_GNOLLHACK_VERSION_LENGTH) {
      errors.push({ line: null, message: `\`suite.snapshot.gnollhack_version\` must be at most ${MAX_GNOLLHACK_VERSION_LENGTH} characters.` });
    } else if (trimmed !== '') {
      parsed.gnollhackVersion = trimmed;
    }
  }

  if ('captured_at' in value && value['captured_at'] !== null) {
    // An unquoted YAML timestamp arrives as a Date; a quoted one as a string.
    const raw = value['captured_at'];
    const date = raw instanceof Date ? raw : new Date(String(raw));
    if (Number.isNaN(date.getTime())) {
      errors.push({ line: null, message: '`suite.snapshot.captured_at` must be a date, for example 2026-09-16T18:04:11Z.' });
    } else {
      parsed.capturedAt = date.toISOString();
    }
  }

  // Informational only: the server derives the format from the board text on import, so a valid
  // value is accepted and discarded rather than carried on `ParsedSnapshot`.
  if ('snapshot_format' in value && value['snapshot_format'] !== null) {
    const format = value['snapshot_format'];
    if (typeof format !== 'number' || !Number.isInteger(format) || format < 1) {
      errors.push({ line: null, message: '`suite.snapshot.snapshot_format` must be a positive integer.' });
    }
  }

  if ('notes' in value && value['notes'] !== null) {
    if (typeof value['notes'] !== 'string') {
      errors.push({ line: null, message: '`suite.snapshot.notes` must be text.' });
    } else {
      parsed.notes = cleanText(value['notes']) || null;
    }
  }

  if ('sha256' in value && value['sha256'] !== null) {
    const sha = typeof value['sha256'] === 'string' ? value['sha256'].trim().toLowerCase() : '';
    if (!/^[0-9a-f]{64}$/.test(sha)) {
      errors.push({ line: null, message: '`suite.snapshot.sha256` must be 64 hexadecimal characters.' });
    } else {
      parsed.sha256 = sha;
    }
  }

  if (!('text' in value) || value['text'] === null) {
    errors.push({ line: null, message: '`suite.snapshot.text` is required.' });
    return null;
  }
  if (typeof value['text'] !== 'string') {
    errors.push({ line: null, message: '`suite.snapshot.text` must be text.' });
    return null;
  }
  const text = cleanText(value['text']);
  if (text === '') {
    errors.push({ line: null, message: '`suite.snapshot.text` is blank; remove the whole `snapshot` mapping to import without a game snapshot.' });
    return null;
  }
  parsed.text = text;
  return parsed;
}

function checkSchema(doc: unknown, result: ParseResult): void {
  const errors = result.errors;
  const headerMessage = `The document must be a YAML mapping with \`format: ${QUESTION_YAML_FORMAT}\` and \`version: ${QUESTION_YAML_VERSION}\`.`;

  // H1
  if (!isMapping(doc) || doc['format'] !== QUESTION_YAML_FORMAT || !('version' in doc)) {
    errors.push({ line: null, message: headerMessage });
    return;
  }
  if (doc['version'] !== QUESTION_YAML_VERSION) {
    errors.push({
      line: null,
      message: `Unsupported \`version: ${String(doc['version'])}\`; this importer understands version ${QUESTION_YAML_VERSION}.`
    });
    return;
  }
  for (const key of Object.keys(doc)) {
    if (!TOP_LEVEL_KEYS.includes(key)) {
      errors.push({ line: null, message: `Unknown top-level key \`${key}\`; allowed: ${TOP_LEVEL_KEYS.join(', ')}.` });
    }
  }

  // H2
  if ('suite' in doc && doc['suite'] !== null) {
    const suite = doc['suite'];
    if (!isMapping(suite)) {
      errors.push({ line: null, message: '`suite` must be a mapping with optional `name`, `description`, `suggested_description` and `snapshot` keys.' });
    } else {
      for (const key of Object.keys(suite)) {
        if (key === 'snapshot_text') {
          errors.push({ line: null, message: OLD_SNAPSHOT_SHAPE_MESSAGE });
        } else if (!SUITE_KEYS.includes(key)) {
          errors.push({ line: null, message: `Unknown key \`suite.${key}\`; allowed: ${SUITE_KEYS.join(', ')}.` });
        }
      }
      const parsed: ParsedSuite = { name: null, description: null, suggestedDescription: null, snapshot: null };
      if ('name' in suite) {
        const name = suite['name'];
        const trimmed = typeof name === 'string' ? name.trim() : null;
        if (trimmed === null || trimmed.length < 1 || trimmed.length > MAX_SUITE_NAME_LENGTH) {
          errors.push({ line: null, message: `\`suite.name\` must be 1–${MAX_SUITE_NAME_LENGTH} characters.` });
        } else {
          parsed.name = trimmed;
        }
      }
      if ('description' in suite && suite['description'] !== null) {
        if (typeof suite['description'] !== 'string') {
          errors.push({ line: null, message: '`suite.description` must be text.' });
        } else {
          parsed.description = cleanText(suite['description']);
        }
      }
      if ('suggested_description' in suite && suite['suggested_description'] !== null) {
        if (typeof suite['suggested_description'] !== 'string') {
          errors.push({ line: null, message: '`suite.suggested_description` must be text.' });
        } else {
          parsed.suggestedDescription = cleanText(suite['suggested_description']) || null;
        }
      }
      if ('snapshot' in suite && suite['snapshot'] !== null) {
        parsed.snapshot = checkSnapshot(suite['snapshot'], errors);
      }
      result.suite = parsed;
    }
  }

  // Q1
  const questions = doc['questions'];
  if (!Array.isArray(questions) || questions.length === 0) {
    errors.push({ line: null, message: '`questions` must be a non-empty list.' });
    return;
  }

  const firstIndexById = new Map<number, number>();
  questions.forEach((raw, index) => {
    if (!isMapping(raw)) {
      errors.push({ line: null, message: `questions[${index}] must be a mapping with \`question\` and \`rubric\` keys.` });
      return;
    }

    const parsed: ParsedQuestion = { index, id: null, difficulty: null, questionText: null, rubric: null };
    let where = `questions[${index}]`;

    // Q3
    if ('id' in raw) {
      const id = raw['id'];
      if (typeof id !== 'number' || !Number.isInteger(id) || id <= 0) {
        errors.push({ line: null, message: `${where}: \`id\` must be a positive integer.` });
      } else {
        const first = firstIndexById.get(id);
        if (first !== undefined) {
          errors.push({ line: null, message: `${where}: id ${id} is declared twice (first at questions[${first}]).` });
        } else {
          firstIndexById.set(id, index);
        }
        parsed.id = id;
        where = `${where} (id ${id})`;
      }
    }

    // Q2
    for (const key of Object.keys(raw)) {
      if (!QUESTION_KEYS.includes(key)) {
        errors.push({ line: null, message: `${where}: unknown key \`${key}\`; allowed: ${QUESTION_KEYS.join(', ')}.` });
      }
    }

    // Q4
    if ('difficulty' in raw) {
      const difficulty = parseDifficulty(raw['difficulty']);
      if (difficulty === null) {
        errors.push({ line: null, message: `${where}: \`difficulty\` must be Simple, Intermediate or Advanced.` });
      } else {
        parsed.difficulty = difficulty;
      }
    }

    // Q5, Q6
    if ('question' in raw) {
      const value = raw['question'];
      if (value !== null && typeof value !== 'string') {
        errors.push({ line: null, message: `${where}: \`question\` must be text.` });
      } else {
        parsed.questionText = cleanText(value ?? '');
      }
    }
    if ('rubric' in raw) {
      const value = raw['rubric'];
      if (value !== null && typeof value !== 'string') {
        errors.push({ line: null, message: `${where}: \`rubric\` must be text.` });
      } else {
        parsed.rubric = cleanText(value ?? '');
      }
    }

    result.questions.push(parsed);
  });
}

/**
 * Drops the trailing newline a `|` block scalar keeps, and trailing whitespace. Leading whitespace
 * is kept so the serializer's indentation indicator round-trips; comparisons and the server trim.
 */
function cleanText(value: string): string {
  const text = value.replace(/\r\n?/g, '\n').replace(/\s+$/, '');
  return text.trim() === '' ? '' : text;
}

function parseDifficulty(value: unknown): ParsedDifficulty | null {
  if (value === 1 || value === 2 || value === 3) {
    return value;
  }
  if (typeof value === 'string') {
    switch (value.trim().toLowerCase()) {
      case 'simple': case '1': return 1;
      case 'intermediate': case '2': return 2;
      case 'advanced': case '3': return 3;
    }
  }
  return null;
}

/** A question DTO's difficulty, which arrives as its enum number or name, as 1–3. */
export function difficultyNumber(value: string | number): ParsedDifficulty {
  return parseDifficulty(value) ?? 1;
}

function locate(q: ParsedQuestion): string {
  return q.id !== null ? `questions[${q.index}] (id ${q.id})` : `questions[${q.index}]`;
}

/** Applies the rules that depend on where the import was opened (M1–M3) and on the current questions. */
export function validateForMode(
  result: ParseResult,
  mode: ImportMode,
  existing: BenchmarkQuestionDto[],
  target?: BenchmarkQuestionDto,
  openSuiteName?: string | null
): { errors: ParseIssue[]; notices: string[] } {
  const errors: ParseIssue[] = [];
  const notices: string[] = [];
  if (result.errors.length > 0) {
    return { errors, notices };
  }

  if (result.suite?.snapshot && mode !== 'suite') {
    notices.push('The game snapshot in the file is ignored: this import changes questions only.');
  }

  if (mode === 'single') {
    if (!target) {
      errors.push({ line: null, message: 'No question was chosen to replace.' });
      return { errors, notices };
    }
    if (result.questions.length !== 1) {
      errors.push({
        line: null,
        message: `The document contains ${result.questions.length} questions; this import replaces question #${target.orderIndex} only, so it accepts exactly one.`
      });
      return { errors, notices };
    }
    const q = result.questions[0];
    if (q.id !== null && q.id !== target.id) {
      errors.push({
        line: null,
        message: `The document's question carries id ${q.id}, but this import targets question #${target.orderIndex} (id ${target.id}).`
      });
    }
    if (q.questionText !== null && q.questionText.trim() === '') {
      errors.push({ line: null, message: `${locate(q)}: \`question\` is blank; remove the key to keep the current text.` });
    }
    if (result.suite) {
      notices.push('The `suite` block is ignored: this import changes one question only.');
    }
    return { errors, notices };
  }

  if (mode === 'questions') {
    const ids = new Set(existing.map(e => e.id));
    for (const q of result.questions) {
      if (q.id !== null) {
        if (!ids.has(q.id)) {
          errors.push({ line: null, message: `${locate(q)}: id ${q.id} is not a question of this suite.` });
        }
        if (q.questionText !== null && q.questionText.trim() === '') {
          errors.push({ line: null, message: `${locate(q)}: \`question\` is blank; remove the key to keep the current text.` });
        }
      } else if (q.questionText === null || q.questionText.trim() === '') {
        errors.push({
          line: null,
          message: `${locate(q)} has no id, so it will be created, and a created question needs a non-blank \`question\`.`
        });
      }
    }
    if (result.suite) {
      notices.push('The `suite` block is ignored: this import changes questions of the open suite only.');
      if (openSuiteName && result.suite.name && result.suite.name !== openSuiteName) {
        notices.push(`This file names suite "${result.suite.name}", but you are importing into "${openSuiteName}".`);
      }
    }
    return { errors, notices };
  }

  // suite
  if (!result.suite || !result.suite.name) {
    errors.push({ line: null, message: '`suite.name` is required when importing a suite.' });
  }
  for (const q of result.questions) {
    if (q.questionText === null || q.questionText.trim() === '') {
      errors.push({
        line: null,
        message: `${locate(q)}: a suite import creates every question, so each needs a non-blank \`question\`.`
      });
    }
  }
  if (result.questions.some(q => q.id !== null)) {
    notices.push('Question ids in the file are ignored: a suite import always creates new questions.');
  }
  if (result.suite?.suggestedDescription) {
    notices.push('`suite.suggested_description` is ignored: a suite import uses `suite.description`.');
  }
  // What happens to the snapshot is reported on the review step, from the server's preflight.
  return { errors, notices };
}

/** The snapshot a suite import sends, or null when the document carries none. */
export function toSuiteSnapshot(result: ParseResult): ImportBenchmarkSuiteSnapshot | null {
  const snapshot = result.suite?.snapshot;
  if (!snapshot) {
    return null;
  }
  return {
    name: snapshot.name,
    text: snapshot.text,
    sourceGnollHackVersion: snapshot.gnollhackVersion,
    capturedAtUtc: snapshot.capturedAt,
    notes: snapshot.notes
  };
}

// ---------------------------------------------------------------------------------------------
// Rubric lint
// ---------------------------------------------------------------------------------------------

export type RubricNoticeCode = 'no-required' | 'form-readability' | 'bold-parenthetical' | 'no-board-facts';

export interface RubricNotice {
  code: RubricNoticeCode;
  message: string;
}

/**
 * Advisory checks for the rubric house format that the assessor and the citation validator rely
 * on. Never blocks an import.
 */
export function lintRubric(rubric: string, suiteHasSnapshot: boolean): RubricNotice[] {
  const notices: RubricNotice[] = [];
  const text = (rubric ?? '').replace(/\r\n?/g, '\n');
  if (text.trim() === '') {
    return notices;
  }

  if (!/^\*\*REQUIRED\*\*/m.test(text)) {
    notices.push({ code: 'no-required', message: 'There is no **REQUIRED** section, so the assessor has nothing to charge.' });
  }
  if (/^\*\*FORM\*\*\s*\(readability\)/mi.test(text)) {
    notices.push({
      code: 'form-readability',
      message: 'The FORM label ties presentation to Readability; label it "**FORM** (not graded — presentation note only)".'
    });
  }
  if (/^\*\*[A-Z ]+\s*\(.*\)\*\*/m.test(text)) {
    notices.push({
      code: 'bold-parenthetical',
      message: 'A section heading has its parenthetical inside the bold markers; write it as **NAME** (note).'
    });
  }
  if (suiteHasSnapshot && !/^\*\*BOARD FACTS\*\*/m.test(text)) {
    notices.push({ code: 'no-board-facts', message: 'This is a snapshot suite, but the rubric has no **BOARD FACTS** section.' });
  }
  return notices;
}

// ---------------------------------------------------------------------------------------------
// Import plan
// ---------------------------------------------------------------------------------------------

export interface ImportPlanItem {
  parsed: ParsedQuestion;
  /** The question being replaced; null for a create. */
  current: BenchmarkQuestionDto | null;
  questionChanged: boolean;
  difficultyChanged: boolean;
  rubricChanged: boolean;
  rubricCleared: boolean;
  /** True for a replace that changes nothing. */
  unchanged: boolean;
}

/** Pairs every parsed question with what it replaces, and says what each one changes. Assumes a validated result. */
export function buildImportPlan(
  result: ParseResult,
  mode: ImportMode,
  existing: BenchmarkQuestionDto[],
  target?: BenchmarkQuestionDto
): ImportPlanItem[] {
  const byId = new Map(existing.map(e => [e.id, e]));
  return result.questions.map(parsed => {
    let current: BenchmarkQuestionDto | null = null;
    if (mode === 'single') {
      current = target ?? null;
    } else if (mode === 'questions' && parsed.id !== null) {
      current = byId.get(parsed.id) ?? null;
    }

    if (!current) {
      return {
        parsed, current: null,
        questionChanged: true, difficultyChanged: false,
        rubricChanged: !!parsed.rubric && parsed.rubric.trim() !== '',
        rubricCleared: false, unchanged: false
      };
    }

    const questionChanged = parsed.questionText !== null && parsed.questionText.trim() !== (current.questionText ?? '').trim();
    const difficultyChanged = parsed.difficulty !== null && parsed.difficulty !== difficultyNumber(current.difficulty);
    const currentRubric = (current.expectedPoints ?? '').trim();
    const importedRubric = parsed.rubric === null ? null : parsed.rubric.trim();
    const rubricChanged = importedRubric !== null && importedRubric !== currentRubric;
    const rubricCleared = rubricChanged && importedRubric === '';
    return {
      parsed, current,
      questionChanged, difficultyChanged, rubricChanged, rubricCleared,
      unchanged: !questionChanged && !difficultyChanged && !rubricChanged
    };
  });
}

/** The request items for a plan. Suite mode drops ids, so every item is a create. */
export function toImportItems(plan: ImportPlanItem[], mode: ImportMode): ImportBenchmarkQuestionItem[] {
  return plan.map(item => ({
    questionId: mode === 'suite' ? null : (item.current?.id ?? null),
    questionText: item.parsed.questionText,
    difficulty: item.parsed.difficulty,
    expectedPoints: item.parsed.rubric,
    replaceExpectedPoints: item.parsed.rubric !== null
  }));
}

// ---------------------------------------------------------------------------------------------
// File names
// ---------------------------------------------------------------------------------------------

/** The suite name lower-cased and reduced to `[a-z0-9-]`. */
export function suiteSlug(suiteName: string): string {
  const slug = (suiteName ?? '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  return slug === '' ? 'suite' : slug;
}

export function questionYamlFileName(suiteName: string, question?: BenchmarkQuestionDto): string {
  if (question) {
    return `overseer-question-export-${question.orderIndex}-id-${question.id}.yaml`;
  }
  return `overseer-questions-export-${suiteSlug(suiteName)}.yaml`;
}

export function suiteYamlFileName(suiteName: string): string {
  return `overseer-suite-export-${suiteSlug(suiteName)}.yaml`;
}

/** True for a name Overseer's own suite download produces, browser "(1)" suffixes included. */
export function isSuiteExportFileName(name: string): boolean {
  return /^overseer-suite-export-.*\.ya?ml$/i.test((name ?? '').trim());
}

export const AI_INSTRUCTIONS_FILE_NAME = 'overseer-benchmark-yaml-instructions.md';

// ---------------------------------------------------------------------------------------------
// Guides
// ---------------------------------------------------------------------------------------------

/** One tab of a help dialog guide: its stable id, its label, its ingress and its Markdown body. */
export interface GuideTab {
  /** Stable id, used in element ids. */
  id: string;
  label: string;
  /** One or two plain-text sentences shown above the body: what this tab is for. */
  ingress: string;
  markdown: string;
  /** A control rendered between the ingress and the body. */
  action?: 'wizard';
}

export type HumanGuideTab = GuideTab & { id: 'workflow' | 'rules' | 'format' };

const WORKFLOW_MARKDOWN = `## Export, edit, import

1. **Export** a question, all questions of the suite, or the whole suite. The export keeps every question's \`id\`.
2. **Edit** the YAML by hand, or hand it to an AI together with the instructions on the *AI Prompt* tab.
3. **Import** it back. **Validate** checks the whole document, **Review changes** shows every change before anything is written, and the final button writes all of it at once. If any question is invalid, nothing is written.

## Three ways to import

| Button | Where | What it does |
|---|---|---|
| **Import from YAML** | on a question | Replaces that one question. The document must hold exactly one question, and its \`id\`, if present, must be that question's. |
| **Import Questions from YAML** | Manage Questions toolbar | Replaces every question that carries an \`id\` and creates every question that has none. |
| **Import Suite from YAML** | Manage Suites toolbar | Always creates a **new** suite, even when one of that name exists; it is then named *Name (Imported)*. Ids in the file are ignored, and the game snapshot in the file is attached. The Manage Suites toolbar has its own help. |

## What an import never does

- Delete or reorder questions.
- Touch runs, assessments or reviews, or change an existing game snapshot.
- Write anything before you confirm on the review step.
`;

const RULES_MARKDOWN = `## Replace or create

- A question **with** \`id\` replaces the question with that id. The id must belong to the open suite.
- A question **without** \`id\` is created at the end of the suite, with difficulty *Simple* unless \`difficulty\` says otherwise.
- A key you leave out keeps the current value: no \`rubric\` key keeps the rubric, no \`difficulty\` key keeps the difficulty.
- \`rubric: |\` with nothing under it **clears** the rubric.

## The catch: a change resets the AI assessment

> **Any change to a question's text, difficulty or rubric, even whitespace inside the text, is a new revision**, exactly as when you edit the question by hand. The question's **AI-assessed difficulty is cleared**, a generated question loses its **Reviewed** mark, and **the suite cannot be run until every question is assessed again.**

Re-importing an unchanged export changes nothing: the review step marks those questions *no changes*, and they keep their assessment.
`;

const FORMAT_MARKDOWN = `## Format essentials

- The document starts with \`format: ${QUESTION_YAML_FORMAT}\` and \`version: ${QUESTION_YAML_VERSION}\`. Both are required.
- Top-level keys: \`format\`, \`version\`, \`suite\`, \`questions\`. Suite keys: \`name\`, \`description\`, \`suggested_description\`, \`snapshot\`; only the Snapshot Suite Wizard's add-questions route reads \`suggested_description\`. Question keys: \`id\`, \`difficulty\`, \`question\`, \`rubric\`. Any other key is an error.
- Write \`question\` and \`rubric\` as \`|\` block scalars, and indent every line of the block by the same number of spaces. Inside the block anything goes: Markdown headings, code fences, \`---\` lines. A \`#\` line inside a block is text, not a comment.
- Indent with spaces, never tabs.
- \`difficulty\` is Simple, Intermediate or Advanced.
- An uploaded file may be at most 2 MB.

## Reading a validation message

A syntax error is reported as *Line N, column M: reason*. A schema error names the question, as in *questions[3] (id 42): unknown key \`tier\`*. Fix every message: the import runs only when the whole document is valid.
`;

/** The admin guide, one entry per help dialog tab. The AI instructions below are a fourth tab. */
export const HUMAN_GUIDE_TABS: ReadonlyArray<HumanGuideTab> = [
  {
    id: 'workflow',
    label: 'Workflow',
    ingress: 'How questions travel out of Overseer as YAML and back in, and what each of the three import buttons does.',
    markdown: WORKFLOW_MARKDOWN
  },
  {
    id: 'rules',
    label: 'Replace or Create',
    ingress: 'How an import decides between replacing a question and creating one, and what a change costs you afterward.',
    markdown: RULES_MARKDOWN
  },
  {
    id: 'format',
    label: 'Format',
    ingress: 'The keys a question document may contain, and how to read a validation message.',
    markdown: FORMAT_MARKDOWN
  }
];

/** Shown above the example accordion on the help dialog's Examples tab. Plain text. */
export const EXAMPLES_INGRESS = 'Ready-to-edit documents, one for each import situation.';

/** Shown above the AI instructions on the help dialog's AI Prompt tab. Plain text. */
export const AI_INGRESS = 'Instructions to hand to an AI chat together with an exported document, so that what it returns imports cleanly.';

export interface YamlExample {
  /** Stable id: element ids, tooltip ids and the download file name derive from it. */
  id: string;
  title: string;
  /** The import mode the example is written for; the spec validates it in this mode. */
  mode: ImportMode;
  /** One or two sentences, Markdown: when to use it and which button imports it. */
  intro: string;
  yaml: string;
}

export function yamlExampleFileName(example: YamlExample): string {
  return `benchmark-example-${example.id}.yaml`;
}

/** Shown above the example accordion on the help dialog's Examples tab. */
export const EXAMPLES_INTRO_MARKDOWN = `Copy or download an example, put your own text in it, and import it with the button its description names. The ids **42** and **43** are placeholders: use the ids from your own export (**Download All as YAML** on the Manage Questions toolbar lists every question with its id). Everything else in these files is what the import expects, so edit the text and keep the shape.`;

/** The two header lines every example starts with. */
export const EXAMPLE_HEADER = `format: ${QUESTION_YAML_FORMAT}
version: ${QUESTION_YAML_VERSION}
`;

/** Ready-to-edit documents, one per import situation. Each one must parse and validate for its mode. */
export const YAML_EXAMPLES: ReadonlyArray<YamlExample> = [
  {
    id: 'replace-one',
    title: 'Replace one question',
    mode: 'single',
    intro: 'Every key is present, so the text, the difficulty and the rubric are all replaced. Use it with **Import from YAML** on that question, or with **Import Questions from YAML**.',
    yaml: EXAMPLE_HEADER + `
questions:
  - id: 42
    difficulty: Simple
    question: |
      What is the Gnoll race, and which roles can play it?
    rubric: |
      **REQUIRED**
      - The Gnoll is a GnollHack-original playable race.
      - Names at least one role a Gnoll can play.
`
  },
  {
    id: 'replace-many',
    title: 'Replace several questions',
    mode: 'questions',
    intro: 'One item per question, each with the id of the question it replaces. Use it with **Import Questions from YAML** on the Manage Questions toolbar.',
    yaml: EXAMPLE_HEADER + `
questions:
  - id: 42
    difficulty: Simple
    question: |
      What is the Gnoll race, and which roles can play it?
    rubric: |
      **REQUIRED**
      - The Gnoll is a GnollHack-original playable race.
      - Names at least one role a Gnoll can play.

  - id: 43
    difficulty: Intermediate
    question: |
      My character is Weak from hunger. What should I eat first, and what should I avoid?
    rubric: |
      **REQUIRED**
      - Eat a safe, filling food item from the inventory first.
      - Avoid cursed or rotten food while Weak.
`
  },
  {
    id: 'rubric-only',
    title: 'Replace only a rubric',
    mode: 'questions',
    intro: 'There is no `question` key, so the question text is kept as it is; only the rubric changes. To clear a rubric instead, write `rubric: ""`.',
    yaml: EXAMPLE_HEADER + `
questions:
  - id: 42
    rubric: |
      **REQUIRED**
      - The Gnoll is a GnollHack-original playable race.
      - Names at least one role a Gnoll can play.

      ## Notes for the assessor
      Do not reward an answer that calls Gnolls a monster only.
`
  },
  {
    id: 'question-only',
    title: 'Replace only the question text',
    mode: 'questions',
    intro: 'There is no `rubric` key, so the rubric is kept as it is; only the question text changes.',
    yaml: EXAMPLE_HEADER + `
questions:
  - id: 42
    question: |
      What is the Gnoll race in GnollHack, and which roles can a Gnoll play?
`
  },
  {
    id: 'create-new',
    title: 'Create new questions',
    mode: 'questions',
    intro: 'No item has an `id`, so every one is created at the end of the suite. The second item names no difficulty and becomes Simple. Use it with **Import Questions from YAML**.',
    yaml: EXAMPLE_HEADER + `
questions:
  - difficulty: Advanced
    question: |
      Which source file implements the hunger state transitions?
    rubric: |
      **REQUIRED**
      - Names the correct source file.

  - question: |
      What does the Weak hunger state do to a character?
    rubric: |
      **REQUIRED**
      - Describes the strength penalty and the risk of fainting next.
`
  },
  {
    id: 'whole-suite',
    title: 'A whole suite',
    mode: 'suite',
    intro: 'A `suite` block with the name and description, then the questions. Use it with **Import Suite from YAML** on the Manage Suites toolbar; it always creates a new suite.',
    yaml: EXAMPLE_HEADER + `
suite:
  name: "Hunger and Food"
  description: |
    Four questions on hunger states, food safety and prayer timing.

questions:
  - difficulty: Simple
    question: |
      What does the Weak hunger state do to a character?
    rubric: |
      **REQUIRED**
      - Describes the strength penalty and the risk of fainting next.

  - difficulty: Advanced
    question: |
      Which source file implements the hunger state transitions?
    rubric: |
      **REQUIRED**
      - Names the correct source file.
`
  }
];

/** Appended to the AI instructions when the rubric guidance could not be fetched from the server. */
export const RUBRIC_GUIDANCE_UNAVAILABLE =
  'The rubric format guidance could not be loaded from the server; ask for it before editing rubrics.';

/** Server text arrives with the server's line endings; the instructions use LF throughout. */
function lf(text: string): string {
  return (text ?? '').replace(/\r\n?/g, '\n').trim();
}

/** Indents every non-blank line, for text placed inside a YAML block scalar. */
function indent(text: string, spaces: number): string {
  const pad = ' '.repeat(spaces);
  return text.split('\n').map(l => (l === '' ? '' : pad + l)).join('\n');
}

/**
 * The instructions for an AI editing an exported document. The YAML shape is owned here; the
 * rubric format, grading semantics and difficulty bands come from the server
 * (`BenchmarkRubricAuthoringGuidance`), and are replaced by {@link RUBRIC_GUIDANCE_UNAVAILABLE}
 * when `guidance` is null.
 */
export function buildAiInstructions(guidance: RubricAuthoringGuidance | null): string {
  const formLabel = guidance ? lf(guidance.formLabel) : null;

  const skeletonRubric = formLabel
    ? `**BOARD FACTS**
- A fact quotable from the snapshot text.

**REQUIRED**
- A point the answer must make.

**CRITICAL ERROR**
- A false claim that fails the answer.

**SCOPE**
- What the question does not ask for.

${formLabel}
- How the answer is best laid out.

**SOURCE** — board`
    : `**REQUIRED**
- A point the answer must make.`;

  const skeletonSuite = formLabel
    ? `suite:
  name: "Suite name"
  description: |
    What the suite measures.
  snapshot:
    name: "Snapshot name"
    gnollhack_version: "4.2.0 Build 47"
    text: |
      The game board, exactly as exported.`
    : `suite:
  name: "Suite name"
  description: |
    What the suite measures.`;

  const exampleRubric1 = formLabel
    ? `**REQUIRED**
- Eat a safe, filling food item from the inventory first.
- Avoid cursed or rotten food while Weak.

**CRITICAL ERROR**
- Claims that eating a cockatrice corpse is safe.

**SCOPE**
- The next few turns; long-term food planning is not required.

${formLabel}
- The food to eat first, then what to avoid.

**SOURCE** — C source: src/eat.c (hunger states)`
    : `**REQUIRED**
- Eat a safe, filling food item from the inventory first.
- Avoid cursed or rotten food while Weak.`;

  const exampleRubric2 = formLabel
    ? `**REQUIRED**
- Names the correct source file.

**SOURCE** — C source: src/eat.c`
    : `**REQUIRED**
- Names the correct source file.`;

  let text = `# Editing Overseer benchmark questions in YAML

You are editing a YAML document that holds benchmark questions for the Overseer AI benchmark. Each question has the text asked of the model under test (\`question\`) and the grading rubric the assessor uses (\`rubric\`, Markdown). Return the whole document as YAML, and nothing else.

## Skeleton

\`\`\`yaml
format: ${QUESTION_YAML_FORMAT}
version: ${QUESTION_YAML_VERSION}

${skeletonSuite}

questions:
  - id: 42
    difficulty: Simple
    question: |
      The question text.
    rubric: |
${indent(skeletonRubric, 6)}
\`\`\`

## Rules

1. Keep the header exactly: \`format: ${QUESTION_YAML_FORMAT}\` and \`version: ${QUESTION_YAML_VERSION}\`.
2. Keep every \`id\` you were given, on the question it was given for. A question without \`id\` is created as a new question.
3. Write \`question\` and \`rubric\` as \`|\` block scalars, and indent every line of them by the same amount. Indent with spaces, never tabs.
4. Omit \`rubric\` to keep the current rubric; write \`rubric: |\` with no content to clear it.
5. Use only the keys id, difficulty, question, rubric inside a question, and only format, version, suite, questions at the top level. \`suite\` may hold name, description, suggested_description and snapshot; only the Snapshot Suite Wizard's add-questions route reads suggested_description. Return \`snapshot\` unchanged: its \`text\` is the game board the questions are written against, so use it to check every BOARD FACT.
6. Difficulty is Simple, Intermediate or Advanced.
7. Inside a block scalar any Markdown is allowed, including headings, code fences and \`---\` lines, as long as every line keeps the block's indentation.
8. Do not add comments inside a block scalar: a \`#\` line there is part of the text.
`;

  if (guidance) {
    const bands = guidance.bands.map(b => `- **${b.name}** (${b.range}): ${lf(b.description)}`).join('\n');
    text += `
## Writing a rubric

A rubric has these sections, in this order:

${lf(guidance.sectionRules)}

${lf(guidance.gradingSemantics)}

A worked example:

\`\`\`markdown
${lf(guidance.workedExample)}
\`\`\`

## Difficulty bands

${bands}
`;
  } else {
    text += `
${RUBRIC_GUIDANCE_UNAVAILABLE}
`;
  }

  text += `
## Complete example

\`\`\`yaml
format: ${QUESTION_YAML_FORMAT}
version: ${QUESTION_YAML_VERSION}

questions:
  - id: 17
    difficulty: Intermediate
    question: |
      My character is Weak from hunger. What should I eat first, and what should I avoid?
    rubric: |
${indent(exampleRubric1, 6)}

  - difficulty: Advanced
    question: |
      Which source file implements the hunger state transitions?
    rubric: |
${indent(exampleRubric2, 6)}
\`\`\`
`;
  return text;
}
