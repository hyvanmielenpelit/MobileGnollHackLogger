import type { BenchmarkQuestionDto, BenchmarkSuiteDto, MatchSnapshotResult } from '../../../services/admin-benchmark.service';
import {
  BoardIdentity,
  ImportExpectation,
  boardIdentityOf,
  checkImportExpectation,
  resolveImportedSuiteName
} from './import-expectation';
import { ImportMode, buildImportPlan, parseQuestionYaml, validateForMode } from './question-yaml-format';

const HEADER = 'format: overseer-benchmark-questions\nversion: 1\n';
const BOARD = '  snapshot:\n    name: "Board"\n    text: |\n      GnollHack 4.2.0\n';
const RUBRIC = '    rubric: |\n      **BOARD FACTS**\n      - "GnollHack 4.2.0"\n\n      **REQUIRED**\n      - A point.\n';

const target: BenchmarkSuiteDto = {
  id: 7, name: 'Core', description: null, createdAtUtc: '', modifiedAtUtc: null,
  questionCount: 0, assessedQuestionCount: 0, difficultyFullyAssessed: false, gameSnapshotId: 3
};
const existing: BenchmarkQuestionDto[] = [
  { id: 17, benchmarkSuiteId: 7, orderIndex: 1, questionText: 'Old', difficulty: 1, expectedPoints: null, createdAtUtc: '' }
];

function routeA(overrides: Partial<ImportExpectation> = {}): ImportExpectation {
  return { route: 'add-to-suite', targetSuite: target, requestedSuiteName: null, requestedCounts: null, maxQuestionsPerSuite: 50, ...overrides };
}

function routeB(overrides: Partial<ImportExpectation> = {}): ImportExpectation {
  return { route: 'create-suite', targetSuite: null, requestedSuiteName: null, requestedCounts: null, maxQuestionsPerSuite: 50, ...overrides };
}

async function run(yaml: string, expectation: ImportExpectation, board: BoardIdentity, names: string[] = []) {
  const mode: ImportMode = expectation.route === 'add-to-suite' ? 'questions' : 'suite';
  const result = await parseQuestionYaml(yaml);
  expect(result.errors).toEqual([]);
  expect(validateForMode(result, mode, existing).errors).toEqual([]);
  const plan = buildImportPlan(result, mode, existing);
  return checkImportExpectation(result, plan, expectation, board, names);
}

const codes = (findings: { level: string; code: string }[], level: string) =>
  findings.filter(f => f.level === level).map(f => f.code);

describe('checkImportExpectation, route A', () => {
  it('confirms a clean file: all new, same board, a suggested description', async () => {
    const f = await run(HEADER + 'suite:\n  name: Core\n  suggested_description: |\n    A suite.\n' + BOARD + 'questions:\n  - difficulty: Simple\n    question: Q\n' + RUBRIC,
      routeA(), { state: 'matches-target' });
    expect(codes(f, 'blocking')).toEqual([]);
    expect(codes(f, 'warning')).toEqual([]);
    expect(codes(f, 'confirmed')).toEqual(['all-new', 'board-matches', 'description-suggested']);
  });

  it('warns, without blocking, when the file suggests no description', async () => {
    const f = await run(HEADER + 'suite:\n  name: Core\n' + BOARD + 'questions:\n  - difficulty: Simple\n    question: Q\n' + RUBRIC,
      routeA(), { state: 'matches-target' });
    expect(codes(f, 'blocking')).toEqual([]);
    expect(codes(f, 'warning')).toEqual(['description-not-suggested']);
    expect(codes(f, 'confirmed')).toEqual(['all-new', 'board-matches']);
    expect(f.find(x => x.code === 'description-not-suggested')!.message).toContain('`suite.suggested_description`');
  });

  it('blocks a file that would replace a question', async () => {
    const f = await run(HEADER + 'questions:\n  - id: 17\n    difficulty: Simple\n    question: Q\n' + RUBRIC,
      routeA(), { state: 'none-in-file' });
    expect(codes(f, 'blocking')).toEqual(['replaces-questions']);
    expect(f[0].message).toContain('1 question in this file carries an id');
    expect(codes(f, 'warning')).toContain('board-none-in-file');
  });

  it('warns about another suite name, another board, the cap and the counts', async () => {
    const f = await run(HEADER + 'suite:\n  name: Other\n' + BOARD + 'questions:\n  - question: Q\n',
      routeA({ targetSuite: { ...target, questionCount: 50 }, requestedCounts: { simple: 6, intermediate: 6, advanced: 6 } }),
      { state: 'other-suite', otherSuiteName: 'X' });
    expect(codes(f, 'warning')).toEqual(['other-suite-name', 'board-other-suite', 'description-not-suggested', 'over-cap', 'counts-differ', 'no-difficulty', 'no-rubric']);
    expect(f.find(x => x.code === 'counts-differ')!.message).toBe('You asked for 6 / 6 / 6; the file has 1 / 0 / 0.');
  });

  it('aggregates rubric lint by code', async () => {
    const plain = '    rubric: |\n      **REQUIRED**\n      - A point.\n';
    const f = await run(HEADER + 'questions:\n  - difficulty: Simple\n    question: A\n' + plain + '  - difficulty: Simple\n    question: B\n' + plain,
      routeA(), { state: 'none-in-file' });
    const lint = f.filter(x => x.code === 'rubric-lint');
    expect(lint.map(x => x.message)).toEqual(['2 rubrics have no BOARD FACTS section.']);
  });
});

describe('checkImportExpectation, route B', () => {
  it('blocks a file without a board', async () => {
    const f = await run(HEADER + 'suite:\n  name: New\nquestions:\n  - difficulty: Simple\n    question: Q\n    rubric: |\n      **REQUIRED**\n      - A.\n',
      routeB(), { state: 'none-in-file' });
    expect(codes(f, 'blocking')).toEqual(['no-board']);
  });

  it('warns about a different name, a taken name and ignored ids, and confirms matching counts', async () => {
    const f = await run(HEADER + 'suite:\n  name: Core\n' + BOARD + 'questions:\n  - id: 5\n    difficulty: Simple\n    question: Q\n' + RUBRIC,
      routeB({ requestedSuiteName: 'Wanted', requestedCounts: { simple: 1, intermediate: 0, advanced: 0 } }),
      { state: 'unknown' }, ['Core']);
    expect(codes(f, 'blocking')).toEqual([]);
    expect(codes(f, 'warning')).toEqual(['name-differs', 'name-taken', 'ids-ignored']);
    expect(f.find(x => x.code === 'name-taken')!.message).toContain('"Core (Imported)"');
    expect(codes(f, 'confirmed')).toEqual(['counts-match']);
  });

  it('reports nothing about a suggested description', async () => {
    const f = await run(HEADER + 'suite:\n  name: New\n  suggested_description: |\n    A suite.\n' + BOARD + 'questions:\n  - difficulty: Simple\n    question: Q\n' + RUBRIC,
      routeB(), { state: 'unknown' });
    expect(f.map(x => x.code)).not.toContain('description-suggested');
    expect(f.map(x => x.code)).not.toContain('description-not-suggested');
  });
});

describe('boardIdentityOf and resolveImportedSuiteName', () => {
  const match = (suiteId: number | null): MatchSnapshotResult =>
    ({ sha256: 'a', charCount: 1, truncated: false, isHtml: false, match: { id: 1, name: 'B', suiteId, suiteName: suiteId ? 'S' : null } });

  it('derives the identity from the preflight', async () => {
    const withBoard = await parseQuestionYaml(HEADER + 'suite:\n  name: Core\n' + BOARD + 'questions:\n  - question: Q\n');
    const without = await parseQuestionYaml(HEADER + 'questions:\n  - question: Q\n');
    expect(boardIdentityOf(without, target, null).state).toBe('none-in-file');
    expect(boardIdentityOf(withBoard, target, null).state).toBe('unknown');
    expect(boardIdentityOf(withBoard, target, { ...match(null), match: null }).state).toBe('not-stored');
    expect(boardIdentityOf(withBoard, target, match(7)).state).toBe('matches-target');
    expect(boardIdentityOf(withBoard, target, match(9))).toEqual({ state: 'other-suite', otherSuiteName: 'S' });
  });

  it('follows the server collision rule', () => {
    expect(resolveImportedSuiteName('A', ['B'])).toBe('A');
    expect(resolveImportedSuiteName('A', ['A'])).toBe('A (Imported)');
    expect(resolveImportedSuiteName('A', ['A', 'A (Imported)'])).toBe('A (Imported 2)');
  });
});
