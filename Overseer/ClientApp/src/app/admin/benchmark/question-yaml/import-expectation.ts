import type { BenchmarkSuiteDto, MatchSnapshotResult } from '../../../services/admin-benchmark.service';
import { ImportPlanItem, ParseResult, RubricNoticeCode, lintRubric } from './question-yaml-format';
import { SuiteAgentPromptCounts, countsTotal } from './suite-agent-prompt';

/**
 * Whether a validated YAML document does what the Snapshot Suite Wizard's chosen route is for:
 * route A adds questions to an existing snapshot suite, route B creates a new snapshot suite.
 * Pure: the board identity is supplied by the caller from the server's snapshot preflight.
 */

export type ImportRoute = 'add-to-suite' | 'create-suite';

export interface ImportExpectation {
  route: ImportRoute;
  /** Route A: the suite the questions are added to. */
  targetSuite: BenchmarkSuiteDto | null;
  /** Route B: the name the prompt asked for, when it named one. */
  requestedSuiteName: string | null;
  /** The per-band counts the prompt asked for, when it set them. */
  requestedCounts: SuiteAgentPromptCounts | null;
  maxQuestionsPerSuite: number;
}

export interface BoardIdentity {
  state: 'unknown' | 'none-in-file' | 'matches-target' | 'other-suite' | 'not-stored';
  otherSuiteName?: string | null;
}

export interface ExpectationFinding {
  level: 'blocking' | 'warning' | 'confirmed';
  code: string;
  message: string;
}

/** The server's message for a document whose `questions` list is missing or empty. */
export const EMPTY_QUESTIONS_ERROR = '`questions` must be a non-empty list.';

export const DOWNLOADED_FILE_HINT = 'This looks like the file you downloaded for the agent (`overseer-suite-export-…`), not the file the agent wrote (`agent-new-questions-…`).';

/** The name the server gives an imported suite, following its "(Imported)" collision rule. */
export function resolveImportedSuiteName(name: string, existingNames: Iterable<string>): string {
  const names = new Set(existingNames);
  if (!names.has(name)) return name;
  let candidate = `${name} (Imported)`;
  let counter = 1;
  while (names.has(candidate)) {
    counter++;
    candidate = `${name} (Imported ${counter})`;
  }
  return candidate;
}

/** The board identity of a document against the target suite, from the preflight result. */
export function boardIdentityOf(
  result: ParseResult,
  targetSuite: BenchmarkSuiteDto | null,
  match: MatchSnapshotResult | null
): BoardIdentity {
  if (!result.suite?.snapshot) {
    return { state: 'none-in-file' };
  }
  if (!match) {
    return { state: 'unknown' };
  }
  if (!match.match) {
    return { state: 'not-stored' };
  }
  if (targetSuite && match.match.suiteId === targetSuite.id) {
    return { state: 'matches-target' };
  }
  return { state: 'other-suite', otherSuiteName: match.match.suiteName ?? null };
}

function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}

function bandCounts(plan: ImportPlanItem[]): SuiteAgentPromptCounts {
  const counts = { simple: 0, intermediate: 0, advanced: 0 };
  for (const item of plan) {
    // A question without a difficulty is created as Simple.
    const d = item.parsed.difficulty ?? 1;
    if (d === 1) counts.simple++;
    else if (d === 2) counts.intermediate++;
    else counts.advanced++;
  }
  return counts;
}

const split = (c: SuiteAgentPromptCounts) => `${c.simple} / ${c.intermediate} / ${c.advanced}`;

const LINT_MESSAGES: Record<RubricNoticeCode, (n: number) => string> = {
  'no-required': n => `${plural(n, 'rubric has', 'rubrics have')} no REQUIRED section, so the assessor has nothing to charge.`,
  'form-readability': n => `${plural(n, 'rubric ties', 'rubrics tie')} its FORM label to Readability.`,
  'bold-parenthetical': n => `${plural(n, 'rubric puts', 'rubrics put')} a heading's parenthetical inside the bold markers.`,
  'no-board-facts': n => `${plural(n, 'rubric has', 'rubrics have')} no BOARD FACTS section.`
};

export function checkImportExpectation(
  result: ParseResult,
  plan: ImportPlanItem[],
  expectation: ImportExpectation,
  board: BoardIdentity,
  existingSuiteNames: Iterable<string> = []
): ExpectationFinding[] {
  const findings: ExpectationFinding[] = [];
  const add = (level: ExpectationFinding['level'], code: string, message: string) => findings.push({ level, code, message });
  const withIds = plan.filter(p => p.parsed.id !== null).length;
  const fileName = result.suite?.name ?? null;
  const hasBoard = !!result.suite?.snapshot;

  if (expectation.route === 'add-to-suite') {
    const target = expectation.targetSuite;
    if (withIds > 0) {
      add('blocking', 'replaces-questions',
        `${plural(withIds, 'question in this file carries an id and would', 'questions in this file carry an id and would')} replace existing questions. `
        + 'This route only adds questions. Remove the ids, or use Import Questions from YAML in Manage Questions if replacing is what you want.');
    } else {
      add('confirmed', 'all-new', `All ${plural(plan.length, 'question is', 'questions are')} new; no existing question is replaced.`);
    }
    if (target && fileName && fileName !== target.name) {
      add('warning', 'other-suite-name', `The file names suite "${fileName}"; you are adding to "${target.name}".`);
    }
    switch (board.state) {
      case 'none-in-file':
        add('warning', 'board-none-in-file', 'The file carries no board, so it cannot be checked against this suite\'s snapshot.');
        break;
      case 'matches-target':
        add('confirmed', 'board-matches', 'The board in the file is the board stored on this suite.');
        break;
      case 'other-suite':
      case 'not-stored':
        add('warning', board.state === 'other-suite' ? 'board-other-suite' : 'board-not-stored',
          'The questions were written against a different board than the one stored on this suite, so their BOARD FACTS will fail the Snapshot facts check.');
        break;
      default:
        add('warning', 'board-unchecked', 'The board in the file could not be checked against this suite\'s snapshot.');
    }
    if (result.suite?.suggestedDescription) {
      add('confirmed', 'description-suggested',
        'The file suggests a new suite description. The import does not change it; you can read and apply it after the import.');
    } else {
      add('warning', 'description-not-suggested',
        'The file includes no suggested description (`suite.suggested_description`). You can still paste one from the agent\'s handoff after the import.');
    }
    if (target) {
      const total = target.questionCount + plan.length;
      if (total > expectation.maxQuestionsPerSuite) {
        add('warning', 'over-cap',
          `This suite would hold ${total} questions; the server's cap is ${expectation.maxQuestionsPerSuite} by default and it may refuse.`);
      }
    }
  } else {
    if (!hasBoard) {
      add('blocking', 'no-board', 'This route creates a snapshot suite, and the file carries no board.');
    }
    if (expectation.requestedSuiteName && fileName && expectation.requestedSuiteName.trim() !== fileName) {
      add('warning', 'name-differs', `You asked for a suite named "${expectation.requestedSuiteName.trim()}"; the file names it "${fileName}".`);
    }
    if (fileName) {
      const resolved = resolveImportedSuiteName(fileName, existingSuiteNames);
      if (resolved !== fileName) {
        add('warning', 'name-taken', `A suite named "${fileName}" already exists, so it will be created as "${resolved}".`);
      }
    }
    if (withIds > 0) {
      add('warning', 'ids-ignored', `${plural(withIds, 'question carries an id, which is', 'questions carry an id, which are')} ignored: every question is created as new.`);
    }
  }

  const requested = expectation.requestedCounts;
  if (requested) {
    const actual = bandCounts(plan);
    if (actual.simple === requested.simple && actual.intermediate === requested.intermediate && actual.advanced === requested.advanced) {
      add('confirmed', 'counts-match', `The file has the ${split(actual)} questions you asked for (${countsTotal(actual)} in total).`);
    } else {
      add('warning', 'counts-differ', `You asked for ${split(requested)}; the file has ${split(actual)}.`);
    }
  }

  const noDifficulty = plan.filter(p => p.parsed.difficulty === null).length;
  if (noDifficulty > 0) {
    add('warning', 'no-difficulty', `${plural(noDifficulty, 'question has', 'questions have')} no difficulty and will be created as Simple.`);
  }
  const noRubric = plan.filter(p => p.parsed.rubric === null || p.parsed.rubric.trim() === '').length;
  if (noRubric > 0) {
    add('warning', 'no-rubric', `${plural(noRubric, 'question has', 'questions have')} no rubric, so the assessor has nothing to charge.`);
  }

  const snapshotSuite = expectation.route === 'add-to-suite' ? !!expectation.targetSuite?.gameSnapshotId : hasBoard;
  const lintCounts = new Map<RubricNoticeCode, number>();
  for (const item of plan) {
    if (!item.parsed.rubric) continue;
    for (const notice of lintRubric(item.parsed.rubric, snapshotSuite)) {
      lintCounts.set(notice.code, (lintCounts.get(notice.code) ?? 0) + 1);
    }
  }
  for (const [code, count] of lintCounts) {
    add('warning', 'rubric-lint', LINT_MESSAGES[code](count));
  }

  return findings;
}
