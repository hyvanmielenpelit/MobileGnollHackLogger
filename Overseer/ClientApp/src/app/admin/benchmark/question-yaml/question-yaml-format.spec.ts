import type { BenchmarkQuestionDto, BenchmarkSuiteDto, RubricAuthoringGuidance } from '../../../services/admin-benchmark.service';
import {
  HUMAN_GUIDE_TABS,
  RUBRIC_GUIDANCE_UNAVAILABLE,
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
      expect(result.suite?.snapshot).toBe('Valkyrie dlvl 12');
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
        'Unknown key `suite.owner`; allowed: name, description, snapshot, snapshot_text.'
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

  describe('snapshot text and question numbers', () => {
    const SNAPSHOT_SUITE: BenchmarkSuiteDto = { ...SUITE, gameSnapshotId: 3 };
    const BOARD = '    0         1\n    0123456789012\n 1  |....@....|\n   \n\nHP:12(60) Pw:5(5)';

    it('round-trips a snapshot text with an indented first line and blank lines', async () => {
      const yaml = serializeQuestionsYaml(FIXTURE, SNAPSHOT_SUITE, BOARD);
      expect(yaml).toContain('  snapshot_text: |2\n');
      expect(yaml).toContain('# snapshot_text is the board the questions are written against; it is ignored on import.');

      const result = await parseQuestionYaml(yaml);
      expect(result.errors).toEqual([]);
      expect(result.suite?.snapshotText).toBe(BOARD);
      expect(result.questions.map(q => q.id)).toEqual(FIXTURE.map(q => q.id));
    });

    it('writes no snapshot_text without a text, and serializeSuiteYaml carries it', async () => {
      expect(serializeQuestionsYaml(FIXTURE, SNAPSHOT_SUITE)).not.toContain('snapshot_text');
      expect(serializeQuestionsYaml(FIXTURE, SNAPSHOT_SUITE, '  ')).not.toContain('snapshot_text');
      const suiteResult = await parseQuestionYaml(serializeSuiteYaml(SNAPSHOT_SUITE, FIXTURE, BOARD));
      expect(suiteResult.suite?.snapshotText).toBe(BOARD);
    });

    it('writes one question comment per question without changing the parsed result', async () => {
      const yaml = serializeQuestionsYaml(FIXTURE, null);
      expect(Array.from(yaml.match(/^ {2}# Question \d+$/gm) ?? [])).toEqual(FIXTURE.map(q => `  # Question ${q.orderIndex}`));
      const withComments = await parseQuestionYaml(yaml);
      const without = await parseQuestionYaml(yaml.replace(/^ {2}# Question \d+\n/gm, ''));
      expect(withComments).toEqual(without);
    });

    it('rejects a non-text snapshot_text', async () => {
      const result = await parseQuestionYaml('format: overseer-benchmark-questions\nversion: 1\nsuite:\n  snapshot_text: [a]\nquestions:\n  - question: x\n');
      expect(result.errors.map(e => e.message)).toEqual(['`suite.snapshot_text` must be text.']);
    });

    for (const mode of ['single', 'questions', 'suite'] as const) {
      it(`validateForMode emits the snapshot_text notice in ${mode} mode`, async () => {
        const target = question(42, 1, 'What is the Gnoll race?\nWhich roles can play it?', 1, TRICKY_RUBRIC);
        const parsed = await parseQuestionYaml(serializeQuestionsYaml([target], SNAPSHOT_SUITE, BOARD));
        const checked = validateForMode(parsed, mode, [target], target);
        expect(checked.errors).toEqual([]);
        expect(checked.notices).toContain('`suite.snapshot_text` is ignored: an import never changes the game snapshot.');
      });
    }
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
      expect(text).toContain('snapshot_text');
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
