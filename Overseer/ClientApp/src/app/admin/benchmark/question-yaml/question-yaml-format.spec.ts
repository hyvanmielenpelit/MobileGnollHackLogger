import type { BenchmarkQuestionDto, BenchmarkSuiteDto, RubricAuthoringGuidance } from '../../../services/admin-benchmark.service';
import {
  HUMAN_GUIDE_TABS,
  RUBRIC_GUIDANCE_UNAVAILABLE,
  SnapshotExport,
  YAML_EXAMPLES,
  buildAiInstructions,
  buildImportPlan,
  lintRubric,
  parseQuestionYaml,
  questionYamlFileName,
  serializeQuestionsYaml,
  serializeSuiteYaml,
  suiteYamlFileName,
  toImportItems,
  toSuiteSnapshot,
  validateForMode,
  yamlExampleFileName
} from './question-yaml-format';

function question(id: number, orderIndex: number, text: string, difficulty: number, rubric: string | null): BenchmarkQuestionDto {
  return {
    id, benchmarkSuiteId: 7, orderIndex, questionText: text, difficulty, expectedPoints: rubric,
    createdAtUtc: '2026-09-16T00:00:00Z'
  };
}

const SUITE: BenchmarkSuiteDto = {
  id: 7,
  name: 'GnollHack: Player "Assistance" Suite',
  description: 'Eighteen questions.\n\n## Tiers\nSix per band.',
  createdAtUtc: '2026-09-16T00:00:00Z',
  modifiedAtUtc: null,
  questionCount: 4,
  assessedQuestionCount: 0,
  difficultyFullyAssessed: false,
  gameSnapshotName: 'Valkyrie dlvl 12'
};

const TRICKY_RUBRIC = [
  '**REQUIRED** (accuracy + completeness)',
  '- The Gnoll is a playable race',
  '',
  '## Any Markdown heading',
  '```c',
  '/* a code fence */',
  '```',
  '---',
  '# a line starting with a hash',
  '  indented continuation: with a colon'
].join('\n');

const FIXTURE: BenchmarkQuestionDto[] = [
  question(42, 1, 'What is the Gnoll race?\nWhich roles can play it?', 1, TRICKY_RUBRIC),
  question(43, 2, 'A question: with a colon # and a hash', 2, null),
  question(44, 3, 'Third', 3, '  starts with two spaces\nthen flush'),
  question(45, 4, 'Fourth', 'Intermediate' as unknown as number, 'Short rubric')
];

describe('question-yaml-format', () => {
  describe('round trip', () => {
    it('parses what it serializes, for every fixture question', async () => {
      const yaml = serializeQuestionsYaml(FIXTURE, SUITE);
      const result = await parseQuestionYaml(yaml);

      expect(result.errors).toEqual([]);
      expect(result.questions.length).toBe(FIXTURE.length);
      result.questions.forEach((q, i) => {
        expect(q.id).toBe(FIXTURE[i].id);
        expect(q.questionText).toBe(FIXTURE[i].questionText);
        expect(q.rubric).toBe(FIXTURE[i].expectedPoints ? FIXTURE[i].expectedPoints : null);
      });
      expect(result.questions.map(q => q.difficulty)).toEqual([1, 2, 3, 2]);
      expect(result.suite?.name).toBe(SUITE.name);
      expect(result.suite?.snapshot).toBeNull();
    });

    it('writes the indentation indicator only when the first line starts with whitespace', () => {
      const yaml = serializeQuestionsYaml(FIXTURE, null);
      expect(yaml).toContain('    rubric: |2\n        starts with two spaces\n      then flush');
      expect(yaml).toContain('    question: |\n      What is the Gnoll race?');
      expect(yaml.endsWith('\n')).toBeTrue();
      expect(yaml.endsWith('\n\n')).toBeFalse();
      expect(yaml).not.toContain('suite:');
    });

    it('omits the rubric key for an empty rubric', () => {
      const yaml = serializeQuestionsYaml([FIXTURE[1]], null);
      expect(yaml).not.toContain('rubric:');
    });

    it('serializeSuiteYaml carries name and description back through the parser', async () => {
      const result = await parseQuestionYaml(serializeSuiteYaml(SUITE, FIXTURE));
      expect(result.errors).toEqual([]);
      expect(result.suite?.name).toBe(SUITE.name);
      expect(result.suite?.description).toBe(SUITE.description);
    });

    it('writes an empty question list as a flow sequence, which reads back as the empty-list error', async () => {
      const yaml = serializeSuiteYaml(SUITE, []);
      expect(yaml).toContain('\nquestions: []\n');
      const result = await parseQuestionYaml(yaml);
      expect(result.suite?.name).toBe(SUITE.name);
      expect(result.errors.map(e => e.message)).toEqual(['`questions` must be a non-empty list.']);
    });
  });

  describe('input handling', () => {
    const doc = 'format: overseer-benchmark-questions\nversion: 1\nquestions:\n  - id: 1\n    question: |\n      Hello\n      world\n    rubric: |\n';

    it('parses CRLF and a BOM identically to LF', async () => {
      const lf = await parseQuestionYaml(doc);
      const crlf = await parseQuestionYaml('﻿' + doc.replace(/\n/g, '\r\n'));
      expect(crlf).toEqual(lf);
      expect(lf.questions[0].questionText).toBe('Hello\nworld');
    });

    it('yields an empty rubric for a blank block and null for an absent key', async () => {
      const blank = await parseQuestionYaml(doc);
      expect(blank.questions[0].rubric).toBe('');
      const absent = await parseQuestionYaml(doc.replace('    rubric: |\n', ''));
      expect(absent.questions[0].rubric).toBeNull();
      expect(absent.questions[0].difficulty).toBeNull();
    });

    it('reports a syntax error with its line and column', async () => {
      const broken = 'format: overseer-benchmark-questions\nversion: 1\nquestions:\n  - question: |\n      one\n     two\n';
      const result = await parseQuestionYaml(broken);
      expect(result.errors.length).toBe(1);
      expect(result.errors[0].line).toBe(6);
      expect(result.errors[0].message).toMatch(/^Line 6, column \d+: /);
    });

    it('reports an empty document', async () => {
      const result = await parseQuestionYaml('  \n# only a comment\n');
      expect(result.errors[0].message).toBe('The document is empty.');
    });
  });

  describe('schema rules', () => {
    const header = 'format: overseer-benchmark-questions\nversion: 1\n';

    async function messages(text: string): Promise<string[]> {
      return (await parseQuestionYaml(text)).errors.map(e => e.message);
    }

    it('H1: requires the header', async () => {
      expect(await messages('questions: []\n')).toEqual([
        'The document must be a YAML mapping with `format: overseer-benchmark-questions` and `version: 1`.'
      ]);
      expect(await messages('- a\n- b\n')).toEqual([
        'The document must be a YAML mapping with `format: overseer-benchmark-questions` and `version: 1`.'
      ]);
    });

    it('H1: rejects another version and unknown top-level keys', async () => {
      expect(await messages('format: overseer-benchmark-questions\nversion: 2\n')).toEqual([
        'Unsupported `version: 2`; this importer understands version 1.'
      ]);
      expect(await messages(header + 'questoins: []\nquestions:\n  - question: x\n')).toContain(
        'Unknown top-level key `questoins`; allowed: format, version, suite, questions.'
      );
    });

    it('H2: checks the suite block', async () => {
      const long = 'x'.repeat(129);
      expect(await messages(header + `suite:\n  name: ${long}\nquestions:\n  - question: x\n`)).toContain(
        '`suite.name` must be 1–128 characters.'
      );
      expect(await messages(header + 'suite:\n  owner: me\nquestions:\n  - question: x\n')).toContain(
        'Unknown key `suite.owner`; allowed: name, description, snapshot.'
      );
    });

    it('Q1: requires a non-empty list of mappings', async () => {
      expect(await messages(header + 'questions: []\n')).toEqual(['`questions` must be a non-empty list.']);
      expect(await messages(header + 'questions:\n  - question: a\n  - question: b\n  - just text\n')).toEqual([
        'questions[2] must be a mapping with `question` and `rubric` keys.'
      ]);
    });

    it('Q2: rejects unknown question keys', async () => {
      expect(await messages(header + 'questions:\n  - question: a\n  - question: b\n    tier: hard\n')).toEqual([
        'questions[1]: unknown key `tier`; allowed: id, difficulty, question, rubric.'
      ]);
    });

    it('Q3: requires positive, unique ids', async () => {
      expect(await messages(header + 'questions:\n  - id: -3\n    question: a\n')).toEqual([
        'questions[0]: `id` must be a positive integer.'
      ]);
      expect(await messages(header + 'questions:\n  - id: 42\n    question: a\n  - id: 42\n    question: b\n')).toEqual([
        'questions[1]: id 42 is declared twice (first at questions[0]).'
      ]);
    });

    it('Q4: accepts band names case-insensitively and 1-3, rejects others', async () => {
      const ok = await parseQuestionYaml(header + 'questions:\n  - difficulty: advanced\n    question: a\n  - difficulty: 2\n    question: b\n');
      expect(ok.questions.map(q => q.difficulty)).toEqual([3, 2]);
      expect(await messages(header + 'questions:\n  - difficulty: Hard\n    question: a\n')).toEqual([
        'questions[0]: `difficulty` must be Simple, Intermediate or Advanced.'
      ]);
    });

    it('Q5: requires text values', async () => {
      expect(await messages(header + 'questions:\n  - question: [a, b]\n')).toEqual([
        'questions[0]: `question` must be text.'
      ]);
    });
  });

  describe('validateForMode', () => {
    const header = 'format: overseer-benchmark-questions\nversion: 1\n';
    const existing = [question(17, 4, 'Current text', 1, 'Current rubric'), question(18, 5, 'Other', 2, null)];

    async function validate(text: string, mode: 'single' | 'questions' | 'suite', target?: BenchmarkQuestionDto) {
      const parsed = await parseQuestionYaml(text);
      expect(parsed.errors).toEqual([]);
      return { parsed, ...validateForMode(parsed, mode, existing, target) };
    }

    it('M1: single mode accepts exactly one question with the target id', async () => {
      const three = await validate(header + 'questions:\n  - question: a\n  - question: b\n  - question: c\n', 'single', existing[0]);
      expect(three.errors.map(e => e.message)).toEqual([
        'The document contains 3 questions; this import replaces question #4 only, so it accepts exactly one.'
      ]);
      const wrongId = await validate(header + 'questions:\n  - id: 42\n    question: a\n', 'single', existing[0]);
      expect(wrongId.errors.map(e => e.message)).toEqual([
        "The document's question carries id 42, but this import targets question #4 (id 17)."
      ]);
      const noId = await validate(header + 'questions:\n  - question: a\n', 'single', existing[0]);
      expect(noId.errors).toEqual([]);
    });

    it('Q6: a blank question on a replace is an error', async () => {
      const blank = await validate(header + 'questions:\n  - id: 17\n    question: ""\n', 'single', existing[0]);
      expect(blank.errors.map(e => e.message)).toEqual([
        'questions[0] (id 17): `question` is blank; remove the key to keep the current text.'
      ]);
    });

    it('M2: questions mode requires ids of this suite, and text for creates', async () => {
      const result = await validate(
        header + 'suite:\n  name: x\nquestions:\n  - id: 999999\n    question: a\n  - rubric: only a rubric\n',
        'questions');
      expect(result.errors.map(e => e.message)).toEqual([
        'questions[0] (id 999999): id 999999 is not a question of this suite.',
        'questions[1] has no id, so it will be created, and a created question needs a non-blank `question`.'
      ]);
      expect(result.notices.length).toBe(1);
    });

    it('M2: questions mode notes a file that names another suite, when the open suite is given', async () => {
      const parsed = await parseQuestionYaml(header + 'suite:\n  name: Other\nquestions:\n  - question: a\n');
      expect(validateForMode(parsed, 'questions', existing, undefined, 'Core').notices)
        .toContain('This file names suite "Other", but you are importing into "Core".');
      expect(validateForMode(parsed, 'questions', existing, undefined, 'Other').notices.length).toBe(1);
      expect(validateForMode(parsed, 'questions', existing).notices.length).toBe(1);
    });

    it('M3: suite mode requires a suite name and ignores ids with a notice', async () => {
      const noName = await validate(header + 'questions:\n  - question: a\n', 'suite');
      expect(noName.errors.map(e => e.message)).toEqual(['`suite.name` is required when importing a suite.']);

      const withIds = await validate(header + 'suite:\n  name: S\nquestions:\n  - id: 17\n    question: a\n', 'suite');
      expect(withIds.errors).toEqual([]);
      expect(withIds.notices).toContain('Question ids in the file are ignored: a suite import always creates new questions.');
    });
  });

  describe('import plan', () => {
    const existing = [question(17, 4, 'Current text', 1, 'Current rubric')];

    it('marks an unchanged re-import and builds a request that keeps an absent rubric', async () => {
      const parsed = await parseQuestionYaml(serializeQuestionsYaml(existing, null));
      const plan = buildImportPlan(parsed, 'questions', existing);
      expect(plan[0].unchanged).toBeTrue();

      const noRubric = await parseQuestionYaml('format: overseer-benchmark-questions\nversion: 1\nquestions:\n  - id: 17\n    difficulty: Advanced\n');
      const items = toImportItems(buildImportPlan(noRubric, 'questions', existing), 'questions');
      expect(items).toEqual([{ questionId: 17, questionText: null, difficulty: 3, expectedPoints: null, replaceExpectedPoints: false }]);
    });

    it('detects a cleared rubric and drops ids in suite mode', async () => {
      const cleared = await parseQuestionYaml('format: overseer-benchmark-questions\nversion: 1\nquestions:\n  - id: 17\n    rubric: |\n');
      const plan = buildImportPlan(cleared, 'questions', existing);
      expect(plan[0].rubricCleared).toBeTrue();
      expect(toImportItems(plan, 'suite')[0].questionId).toBeNull();
    });

    it('carries every authored difficulty into the request items of a suite import', async () => {
      const doc = 'format: overseer-benchmark-questions\nversion: 1\n'
        + 'suite:\n  name: Bands\n'
        + 'questions:\n'
        + '  - id: 101\n    difficulty: Simple\n    question: a\n'
        + '  - id: 102\n    difficulty: Intermediate\n    question: b\n'
        + '  - id: 103\n    difficulty: Advanced\n    question: c\n';
      const parsed = await parseQuestionYaml(doc);
      expect(parsed.errors).toEqual([]);
      expect(validateForMode(parsed, 'suite', []).errors).toEqual([]);

      const items = toImportItems(buildImportPlan(parsed, 'suite', []), 'suite');
      expect(items.map(i => i.difficulty)).toEqual([1, 2, 3]);
      expect(items.map(i => i.questionId)).toEqual([null, null, null]);
    });
  });

  describe('file names', () => {
    it('builds slugs and names', () => {
      expect(questionYamlFileName(SUITE.name)).toBe('benchmark-questions-gnollhack-player-assistance-suite.yaml');
      expect(questionYamlFileName(SUITE.name, FIXTURE[1])).toBe('benchmark-question-2-id-43.yaml');
      expect(suiteYamlFileName('  ')).toBe('benchmark-suite-suite.yaml');
    });
  });

  it('every guide tab has a label and text', () => {
    expect(HUMAN_GUIDE_TABS.length).toBe(3);
    for (const tab of HUMAN_GUIDE_TABS) {
      expect(tab.label.trim()).not.toBe('');
      expect(tab.markdown.trim()).not.toBe('');
      expect(tab.ingress.trim()).withContext(tab.id).not.toBe('');
    }
  });

  describe('examples', () => {
    // The placeholder ids every example with ids uses.
    const existing = [question(42, 1, 'Current text', 1, 'Current rubric'), question(43, 2, 'Second', 2, null)];

    it('has six examples with unique ids', () => {
      expect(YAML_EXAMPLES.length).toBe(6);
      expect(new Set(YAML_EXAMPLES.map(e => e.id)).size).toBe(6);
    });

    for (const example of YAML_EXAMPLES) {
      it(`"${example.title}" parses and validates in ${example.mode} mode`, async () => {
        const result = await parseQuestionYaml(example.yaml);
        expect(result.errors).toEqual([]);
        const checked = validateForMode(result, example.mode, existing, existing[0]);
        expect(checked.errors).toEqual([]);
      });
    }

    it('names a downloaded example after its id', () => {
      expect(yamlExampleFileName(YAML_EXAMPLES[0])).toBe('benchmark-example-replace-one.yaml');
    });
  });

  describe('the snapshot mapping and question numbers', () => {
    const SNAPSHOT_SUITE: BenchmarkSuiteDto = { ...SUITE, gameSnapshotId: 3 };
    /* A hostile board: a column ruler, gutter-indented map rows, a blank run, a line that holds
       every character the serializer has to keep out of YAML's way, and a truncation marker. */
    const BOARD = [
      'GnollHack 4.2.0 Build 47',
      '',
      '     0         1',
      '     0123456789012',
      '  1  |....@....|',
      '  2  |.........|',
      '',
      'Status: HP:12(60) Pw:5(5)  # not a comment - a: b | c',
      '- a bullet that is not a list item',
      '',
      '[SNAPSHOT TRUNCATED at 60000 characters.]'
    ].join('\n');
    const SNAPSHOT: SnapshotExport = {
      name: 'Valkyrie dlvl 12',
      gnollhackVersion: '4.2.0 Build 47',
      capturedAtUtc: '2026-09-16T18:04:11Z',
      notes: 'Exported from the developer menu.',
      sha256: 'a'.repeat(64),
      text: BOARD
    };

    it('round-trips the whole board and every metadata key', async () => {
      const yaml = serializeSuiteYaml(SNAPSHOT_SUITE, FIXTURE, SNAPSHOT);
      expect(yaml).toContain('  snapshot:\n');
      expect(yaml).toContain('    text: |\n');
      expect(yaml).toContain('# suite.snapshot is the board the questions are written against. A suite import attaches it; a questions import ignores it.');

      const result = await parseQuestionYaml(yaml);
      expect(result.errors).toEqual([]);
      expect(result.suite?.snapshot?.text).toBe(BOARD);
      expect(result.suite?.snapshot?.name).toBe('Valkyrie dlvl 12');
      expect(result.suite?.snapshot?.gnollhackVersion).toBe('4.2.0 Build 47');
      expect(result.suite?.snapshot?.capturedAt).toBe('2026-09-16T18:04:11.000Z');
      expect(result.suite?.snapshot?.notes).toBe('Exported from the developer menu.');
      expect(result.suite?.snapshot?.sha256).toBe('a'.repeat(64));
      expect(result.questions.map(q => q.id)).toEqual(FIXTURE.map(q => q.id));
    });

    it('writes the indentation indicator for a board whose first line is indented', async () => {
      const indented: SnapshotExport = { ...SNAPSHOT, text: '   leading spaces\nthen flush' };
      const yaml = serializeSuiteYaml(SNAPSHOT_SUITE, FIXTURE, indented);
      expect(yaml).toContain('    text: |2\n');
      const result = await parseQuestionYaml(yaml);
      expect(result.suite?.snapshot?.text).toBe('   leading spaces\nthen flush');
    });

    it('writes no snapshot key at all when the text could not be fetched', async () => {
      expect(serializeSuiteYaml(SNAPSHOT_SUITE, FIXTURE)).not.toContain('snapshot');
      expect(serializeSuiteYaml(SNAPSHOT_SUITE, FIXTURE, null)).not.toContain('snapshot');
      const blank = serializeSuiteYaml(SNAPSHOT_SUITE, FIXTURE, { ...SNAPSHOT, text: '  ' });
      expect(blank).not.toContain('snapshot');
      const result = await parseQuestionYaml(blank);
      expect(result.errors).toEqual([]);
      expect(result.suite?.snapshot).toBeNull();
    });

    it('omits the metadata keys that are null', async () => {
      const bare: SnapshotExport = { name: null, gnollhackVersion: null, capturedAtUtc: null, notes: null, sha256: null, text: BOARD };
      const yaml = serializeSuiteYaml(SNAPSHOT_SUITE, FIXTURE, bare);
      expect(yaml).not.toContain('gnollhack_version');
      expect(yaml).not.toContain('sha256');
      const result = await parseQuestionYaml(yaml);
      expect(result.errors).toEqual([]);
      expect(result.suite?.snapshot?.text).toBe(BOARD);
      expect(result.suite?.snapshot?.name).toBeNull();
    });

    it('writes one question comment per question without changing the parsed result', async () => {
      const yaml = serializeQuestionsYaml(FIXTURE, null);
      expect(Array.from(yaml.match(/^ {2}# Question \d+$/gm) ?? [])).toEqual(FIXTURE.map(q => `  # Question ${q.orderIndex}`));
      const withComments = await parseQuestionYaml(yaml);
      const without = await parseQuestionYaml(yaml.replace(/^ {2}# Question \d+\n/gm, ''));
      expect(withComments).toEqual(without);
    });

    describe('H2 snapshot rules', () => {
      const header = 'format: overseer-benchmark-questions\nversion: 1\n';
      const tail = 'questions:\n  - question: x\n';

      async function messages(suiteBlock: string): Promise<string[]> {
        return (await parseQuestionYaml(header + suiteBlock + tail)).errors.map(e => e.message);
      }

      const oldShape = '`suite.snapshot` is now a mapping with `name` and `text`; `suite.snapshot_text` is no longer a key.';

      it('names the new shape for a string snapshot and for snapshot_text', async () => {
        expect(await messages('suite:\n  snapshot: "Valkyrie dlvl 12"\n')).toContain(oldShape);
        expect(await messages('suite:\n  snapshot_text: |\n    a board\n')).toContain(oldShape);
      });

      it('rejects an unknown key, a bad name, version, date and hash', async () => {
        expect(await messages('suite:\n  snapshot:\n    owner: me\n    text: a board\n')).toContain(
          'Unknown key `suite.snapshot.owner`; allowed: name, gnollhack_version, captured_at, notes, sha256, text.'
        );
        expect(await messages(`suite:\n  snapshot:\n    name: ${'x'.repeat(129)}\n    text: a board\n`)).toContain(
          '`suite.snapshot.name` must be 1–128 characters.'
        );
        expect(await messages(`suite:\n  snapshot:\n    gnollhack_version: ${'v'.repeat(65)}\n    text: a board\n`)).toContain(
          '`suite.snapshot.gnollhack_version` must be at most 64 characters.'
        );
        expect(await messages('suite:\n  snapshot:\n    captured_at: "not a date"\n    text: a board\n')).toContain(
          '`suite.snapshot.captured_at` must be a date, for example 2026-09-16T18:04:11Z.'
        );
        expect(await messages('suite:\n  snapshot:\n    sha256: "abc"\n    text: a board\n')).toContain(
          '`suite.snapshot.sha256` must be 64 hexadecimal characters.'
        );
      });

      it('requires a non-blank text', async () => {
        expect(await messages('suite:\n  snapshot:\n    name: A board\n')).toContain('`suite.snapshot.text` is required.');
        expect(await messages('suite:\n  snapshot:\n    text: [a]\n')).toContain('`suite.snapshot.text` must be text.');
        expect(await messages('suite:\n  snapshot:\n    text: "   "\n')).toContain(
          '`suite.snapshot.text` is blank; remove the whole `snapshot` mapping to import without a game snapshot.'
        );
      });

      it('accepts captured_at as an unquoted timestamp and as a string', async () => {
        const unquoted = await parseQuestionYaml(header + 'suite:\n  snapshot:\n    captured_at: 2026-09-16T18:04:11Z\n    text: a board\n' + tail);
        expect(unquoted.errors).toEqual([]);
        expect(unquoted.suite?.snapshot?.capturedAt).toBe('2026-09-16T18:04:11.000Z');

        const quoted = await parseQuestionYaml(header + 'suite:\n  snapshot:\n    captured_at: "2026-09-16T18:04:11Z"\n    text: a board\n' + tail);
        expect(quoted.errors).toEqual([]);
        expect(quoted.suite?.snapshot?.capturedAt).toBe('2026-09-16T18:04:11.000Z');
      });
    });

    it('builds the import request snapshot, and null without one', async () => {
      const withBoard = await parseQuestionYaml(serializeSuiteYaml(SNAPSHOT_SUITE, FIXTURE, SNAPSHOT));
      expect(toSuiteSnapshot(withBoard)).toEqual({
        name: 'Valkyrie dlvl 12',
        text: BOARD,
        sourceGnollHackVersion: '4.2.0 Build 47',
        capturedAtUtc: '2026-09-16T18:04:11.000Z',
        notes: 'Exported from the developer menu.'
      });
      expect(toSuiteSnapshot(await parseQuestionYaml(serializeSuiteYaml(SUITE, FIXTURE)))).toBeNull();
    });

    for (const mode of ['single', 'questions'] as const) {
      it(`validateForMode says the snapshot is ignored in ${mode} mode`, async () => {
        const target = question(42, 1, 'What is the Gnoll race?\nWhich roles can play it?', 1, TRICKY_RUBRIC);
        const parsed = await parseQuestionYaml(serializeSuiteYaml(SNAPSHOT_SUITE, [target], SNAPSHOT));
        const checked = validateForMode(parsed, mode, [target], target);
        expect(checked.errors).toEqual([]);
        expect(checked.notices).toContain('The game snapshot in the file is ignored: this import changes questions only.');
      });
    }

    it('says nothing about the snapshot in suite mode: the review step reports it', async () => {
      const parsed = await parseQuestionYaml(serializeSuiteYaml(SNAPSHOT_SUITE, FIXTURE, SNAPSHOT));
      const checked = validateForMode(parsed, 'suite', []);
      expect(checked.errors).toEqual([]);
      expect(checked.notices.some(n => n.includes('snapshot'))).toBeFalse();
    });
  });

  describe('lintRubric', () => {
    const HOUSE = [
      '**BOARD FACTS**',
      '- HP is 12/60.',
      '',
      '**REQUIRED**',
      '- Retreat.',
      '',
      '**CRITICAL ERROR**',
      '- Claims melee is safe.',
      '',
      '**SCOPE**',
      '- This turn.',
      '',
      '**FORM** (not graded — presentation note only)',
      '- Action first.',
      '',
      '**SOURCE** — board'
    ].join('\n');
    const codes = (rubric: string, snapshot = false) => lintRubric(rubric, snapshot).map(n => n.code);

    it('accepts a house-format rubric, and an empty one', () => {
      expect(codes(HOUSE, true)).toEqual([]);
      expect(codes('', true)).toEqual([]);
      expect(codes('   \n', true)).toEqual([]);
    });

    it('flags each trigger', () => {
      expect(codes('**SCOPE**\n- x')).toEqual(['no-required']);
      expect(codes('**REQUIRED**\n- x\n\n**FORM** (readability)\n- y')).toEqual(['form-readability']);
      expect(codes('**REQUIRED**\n- x\n\n**FORM (readability)**\n- y')).toEqual(['bold-parenthetical']);
      expect(codes('**REQUIRED**\n- x', true)).toEqual(['no-board-facts']);
      expect(codes('**REQUIRED**\r\n- x', false)).toEqual([]);
    });

    it('gives every notice a message', () => {
      for (const n of lintRubric('**FORM (readability)**\n**FORM** (readability)', true)) {
        expect(n.message.trim()).not.toBe('');
      }
    });
  });

  describe('AI instructions', () => {
    const GUIDANCE: RubricAuthoringGuidance = {
      sectionRules: '1. **BOARD FACTS**: fixture rule.\r\n2. **REQUIRED**: fixture rule.',
      gradingSemantics: 'Only REQUIRED and CRITICAL ERROR points are ever charged.',
      workedExample: '**REQUIRED**\r\n- A fixture point.',
      formLabel: '**FORM** (fixture label)',
      bands: [
        { name: 'Simple', range: '1–35', description: 'Fixture simple.' },
        { name: 'Intermediate', range: '36–70', description: 'Fixture intermediate.' },
        { name: 'Advanced', range: '71–100', description: 'Fixture advanced.' }
      ]
    };

    const completeExample = (text: string) => text.split('## Complete example')[1].split('```yaml\n')[1].split('```\n')[0];
    const skeleton = (text: string) => text.split('## Skeleton')[1].split('```yaml\n')[1].split('```\n')[0];

    it('assembles the guidance, with LF line endings, and a valid example', async () => {
      const text = buildAiInstructions(GUIDANCE);
      expect(text).not.toContain('\r');
      expect(text).toContain('## Writing a rubric');
      expect(text).toContain('1. **BOARD FACTS**: fixture rule.\n2. **REQUIRED**: fixture rule.');
      expect(text).toContain('## Difficulty bands');
      expect(text).toContain('- **Intermediate** (36–70): Fixture intermediate.');
      expect(text).toContain('snapshot:');
      expect(text).not.toContain('snapshot_text');
      expect(text).not.toContain(RUBRIC_GUIDANCE_UNAVAILABLE);

      const result = await parseQuestionYaml(completeExample(text));
      expect(result.errors).toEqual([]);
      expect(result.questions.length).toBe(2);
      expect(result.questions[0].rubric).toContain('**FORM** (fixture label)');
      expect(lintRubric(result.questions[0].rubric!, false)).toEqual([]);
      expect(validateForMode(result, 'questions', [question(17, 1, 'Q', 2, 'R')]).errors).toEqual([]);

      const skeletonResult = await parseQuestionYaml(skeleton(text));
      expect(skeletonResult.errors).toEqual([]);
      expect(lintRubric(skeletonResult.questions[0].rubric!, true)).toEqual([]);
    });

    it('falls back without guidance, and the example still parses', async () => {
      const text = buildAiInstructions(null);
      expect(text).toContain(RUBRIC_GUIDANCE_UNAVAILABLE);
      expect(text).not.toContain('## Writing a rubric');
      const result = await parseQuestionYaml(completeExample(text));
      expect(result.errors).toEqual([]);
      expect(result.questions.length).toBe(2);
    });
  });
});
