import type {
  BenchmarkQuestionDto,
  BenchmarkSuiteDto,
  ImportBenchmarkQuestionItem
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

export interface ParsedSuite {
  name: string | null;
  description: string | null;
  snapshot: string | null;
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
const SUITE_KEYS = ['name', 'description', 'snapshot'];
const QUESTION_KEYS = ['id', 'difficulty', 'question', 'rubric'];
export const MAX_SUITE_NAME_LENGTH = 128;

// ---------------------------------------------------------------------------------------------
// Serializer
// ---------------------------------------------------------------------------------------------

/** Serializes questions, with a `suite` block naming their suite when one is given. */
export function serializeQuestionsYaml(questions: BenchmarkQuestionDto[], suite: BenchmarkSuiteDto | null): string {
  return serialize(questions, suite, false);
}

/** Serializes a whole suite: name, description, attached snapshot name, and every question. */
export function serializeSuiteYaml(suite: BenchmarkSuiteDto, questions: BenchmarkQuestionDto[]): string {
  return serialize(questions, suite, true);
}

function serialize(questions: BenchmarkQuestionDto[], suite: BenchmarkSuiteDto | null, includeDescription: boolean): string {
  const out: string[] = [
    '# Overseer benchmark questions. Edit freely; keep every `id` you were given.',
    '# A question without `id` is created as new. Omit `rubric` to keep the current rubric.',
    `format: ${QUESTION_YAML_FORMAT}`,
    `version: ${QUESTION_YAML_VERSION}`
  ];

  if (suite) {
    out.push('', 'suite:');
    out.push(`  name: ${quoted(suite.name)}`);
    if (includeDescription && suite.description && suite.description.trim() !== '') {
      out.push(...blockScalar('description', suite.description, 2));
    }
    if (suite.gameSnapshotName) {
      out.push(`  snapshot: ${quoted(suite.gameSnapshotName)}`);
    }
  }

  out.push('', 'questions:');
  questions.forEach((q, i) => {
    if (i > 0) {
      out.push('');
    }
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
      errors.push({ line: null, message: '`suite` must be a mapping with optional `name`, `description` and `snapshot` keys.' });
    } else {
      for (const key of Object.keys(suite)) {
        if (!SUITE_KEYS.includes(key)) {
          errors.push({ line: null, message: `Unknown key \`suite.${key}\`; allowed: ${SUITE_KEYS.join(', ')}.` });
        }
      }
      const parsed: ParsedSuite = { name: null, description: null, snapshot: null };
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
      if ('snapshot' in suite && suite['snapshot'] !== null) {
        parsed.snapshot = String(suite['snapshot']);
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
  target?: BenchmarkQuestionDto
): { errors: ParseIssue[]; notices: string[] } {
  const errors: ParseIssue[] = [];
  const notices: string[] = [];
  if (result.errors.length > 0) {
    return { errors, notices };
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
  if (result.suite?.snapshot) {
    notices.push('The `suite.snapshot` name is informational: a suite import attaches no snapshot.');
  }
  return { errors, notices };
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
    return `benchmark-question-${question.orderIndex}-id-${question.id}.yaml`;
  }
  return `benchmark-questions-${suiteSlug(suiteName)}.yaml`;
}

export function suiteYamlFileName(suiteName: string): string {
  return `benchmark-suite-${suiteSlug(suiteName)}.yaml`;
}

export const AI_INSTRUCTIONS_FILE_NAME = 'overseer-benchmark-yaml-instructions.md';

// ---------------------------------------------------------------------------------------------
// Guides
// ---------------------------------------------------------------------------------------------

export interface HumanGuideTab {
  /** Stable id, used in element ids. */
  id: 'workflow' | 'rules' | 'format';
  label: string;
  markdown: string;
}

const WORKFLOW_MARKDOWN = `## Export, edit, import

1. **Export** a question, all questions of the suite, or the whole suite. The export keeps every question's \`id\`.
2. **Edit** the YAML by hand, or hand it to an AI together with the instructions on the *For an AI* tab.
3. **Import** it back. **Validate** checks the whole document, **Review changes** shows every change before anything is written, and the final button writes all of it at once. If any question is invalid, nothing is written.

## Three ways to import

| Button | Where | What it does |
|---|---|---|
| **Import from YAML** | on a question | Replaces that one question. The document must hold exactly one question, and its \`id\`, if present, must be that question's. |
| **Import Questions from YAML** | Manage Questions toolbar | Replaces every question that carries an \`id\` and creates every question that has none. |
| **Import Suite from YAML** | Manage Suites toolbar | Always creates a **new** suite, even when one of that name exists; it is then named *Name (Imported)*. Ids in the file are ignored, and no game snapshot is attached. |

## What an import never does

- Delete or reorder questions.
- Touch runs, assessments, reviews or the game snapshot.
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
- Top-level keys: \`format\`, \`version\`, \`suite\`, \`questions\`. Question keys: \`id\`, \`difficulty\`, \`question\`, \`rubric\`. Any other key is an error.
- Write \`question\` and \`rubric\` as \`|\` block scalars, and indent every line of the block by the same number of spaces. Inside the block anything goes: Markdown headings, code fences, \`---\` lines. A \`#\` line inside a block is text, not a comment.
- Indent with spaces, never tabs.
- \`difficulty\` is Simple, Intermediate or Advanced.
- An uploaded file may be at most 2 MB.

## Reading a validation message

A syntax error is reported as *Line N, column M: reason*. A schema error names the question, as in *questions[3] (id 42): unknown key \`tier\`*. Fix every message: the import runs only when the whole document is valid.
`;

/** The admin guide, one entry per help dialog tab. The AI instructions below are a fourth tab. */
export const HUMAN_GUIDE_TABS: ReadonlyArray<HumanGuideTab> = [
  { id: 'workflow', label: 'Workflow', markdown: WORKFLOW_MARKDOWN },
  { id: 'rules', label: 'Replace or Create', markdown: RULES_MARKDOWN },
  { id: 'format', label: 'Format', markdown: FORMAT_MARKDOWN }
];

export const AI_INSTRUCTIONS_MARKDOWN = `# Editing Overseer benchmark questions in YAML

You are editing a YAML document that holds benchmark questions for the Overseer AI benchmark. Each question has the text asked of the model under test (\`question\`) and the grading rubric the assessor uses (\`rubric\`, Markdown). Return the whole document as YAML, and nothing else.

## Skeleton

\`\`\`yaml
format: ${QUESTION_YAML_FORMAT}
version: ${QUESTION_YAML_VERSION}

suite:
  name: "Suite name"
  description: |
    What the suite measures.

questions:
  - id: 42
    difficulty: Simple
    question: |
      The question text.
    rubric: |
      **REQUIRED**
      - A point the answer must make.
\`\`\`

## Rules

1. Keep the header exactly: \`format: ${QUESTION_YAML_FORMAT}\` and \`version: ${QUESTION_YAML_VERSION}\`.
2. Keep every \`id\` you were given, on the question it was given for. A question without \`id\` is created as a new question.
3. Write \`question\` and \`rubric\` as \`|\` block scalars, and indent every line of them by the same amount. Indent with spaces, never tabs.
4. Omit \`rubric\` to keep the current rubric; write \`rubric: |\` with no content to clear it.
5. Use only the keys id, difficulty, question, rubric inside a question, and only format, version, suite, questions at the top level. \`suite\` may hold name, description and snapshot.
6. Difficulty is Simple, Intermediate or Advanced.
7. Inside a block scalar any Markdown is allowed, including headings, code fences and \`---\` lines, as long as every line keeps the block's indentation.
8. Do not add comments inside a block scalar: a \`#\` line there is part of the text.

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
      **REQUIRED** (accuracy + completeness)
      - Eat a safe, filling food item from the inventory first.
      - Avoid cursed or rotten food while Weak.

      ## Notes for the assessor
      Do not reward advice to pray unless prayer timeout is addressed.

  - difficulty: Advanced
    question: |
      Which source file implements the hunger state transitions?
    rubric: |
      **REQUIRED**
      - Names the correct source file.
\`\`\`
`;
