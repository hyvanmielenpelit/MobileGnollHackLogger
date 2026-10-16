import {
  BenchmarkReportPackCallDto,
  BenchmarkReportPackValidationIssueDto
} from '../../../services/admin-benchmark.service';

/**
 * What a document's validation outcome is explained from: a job's progress row (which is assignable
 * as it stands) or a stored document's validation issues.
 */
export interface DocumentStatusInput {
  /** `Pending`, `Writing`, `Repairing`, `Completed`, `CompletedWithWarnings`, `Failed` or `Canceled`. */
  readonly status: string;
  readonly errorMessage?: string | null;
  readonly failedReply?: string | null;
  /** The first draft's issues; null or absent when they are not known. */
  readonly firstTurnIssues?: readonly BenchmarkReportPackValidationIssueDto[] | null;
  /** The issues left after the last check; null or absent while the document is written. */
  readonly finalIssues?: readonly BenchmarkReportPackValidationIssueDto[] | null;
  readonly firstDraftClean?: boolean | null;
  readonly calls?: readonly BenchmarkReportPackCallDto[] | null;
}

/** Where the explanation is read: a job's progress row (with its diagnostics), or a stored document. */
export type DocumentStatusSource = 'job' | 'stored';

/** A run of plain text, or one `{{token}}` that the dialog shows as code. */
export interface TextSegment {
  readonly text: string;
  readonly token: boolean;
}

/** One validation issue in words. */
export interface ExplainedIssue {
  readonly rule: number;
  /** The rule's name, or `Rule 16` from a server that sends none. */
  readonly ruleName: string;
  /** `removesItem`, `warning`, `warningTextKept`, `addedByOverseer`, `failsDocument`, or null when not sent. */
  readonly severity: string | null;
  /** `Conclusions, paragraph 1`, or the raw location from a server that sends no label. */
  readonly location: string;
  /** What the check found, as the validator words it. */
  readonly meaning: string;
  /** The removed, flagged or added text, raw with its `{{tokens}}`; null when none was kept. */
  readonly text: string | null;
  /** Overseer wrote `text` in place of a removed one. */
  readonly addedByOverseer: boolean;
  /** Sentences were removed from the end of the paragraph, not the whole paragraph. */
  readonly shortened: boolean;
  /** `meaning` split around its tokens. */
  readonly meaningParts: readonly TextSegment[];
  /** `text` split around its tokens; null when there is no text. */
  readonly textParts: readonly TextSegment[] | null;
}

/** First-draft issues of one rule. */
export interface IssueGroup {
  readonly rule: number;
  readonly ruleName: string;
  readonly count: number;
  readonly issues: readonly ExplainedIssue[];
}

/** Why a document has its status, in the sections the dialog and the diagnostics both show. */
export interface DocumentStatusExplanation {
  /** One sentence: whether the document is usable, and what was shortened, removed, added or kept. */
  readonly summary: string;
  /** Shortened paragraphs and removed items. */
  readonly removed: readonly ExplainedIssue[];
  /** Paragraphs Overseer wrote in place of removed ones. */
  readonly added: readonly ExplainedIssue[];
  readonly kept: readonly ExplainedIssue[];
  /** Issues that stop the document from being stored. */
  readonly blocking: readonly ExplainedIssue[];
  /**
   * `First draft: 20 issues (…). One revision and one shortening turn. Final: 2 sentences removed, 1 paragraph
   * added by Overseer.`; empty when nothing is known.
   */
  readonly checks: string;
  readonly actions: readonly string[];
  /** A failed document's error message. */
  readonly error: string | null;
  /** The first characters of a failed document's last writer reply. */
  readonly failedReplyExcerpt: string | null;
}

/** How much of a failed document's last reply the explanation quotes. */
export const FAILED_REPLY_EXCERPT_CHARS = 500;

const FINISHED_STATUSES = new Set(['Completed', 'CompletedWithWarnings', 'Failed', 'Canceled']);
const NUMBER_WORDS = ['zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten'];
const numberFormat = new Intl.NumberFormat('en-US');

/** `one` to `ten`, then digits. */
export function numberWord(count: number): string {
  return NUMBER_WORDS[count] ?? numberFormat.format(count);
}

function capitalize(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

function plural(count: number, one: string, many: string): string {
  return `${numberFormat.format(count)} ${count === 1 ? one : many}`;
}

/** `once`, `twice`, `3 times`. */
export function timesWord(count: number): string {
  return count === 1 ? 'once' : count === 2 ? 'twice' : `${numberFormat.format(count)} times`;
}

/**
 * `warning`, `warning, text kept`, `removes the item`, `added by Overseer`, `fails the document`; the wire
 * value for an unknown one.
 */
export function severityWord(severity: string | null | undefined): string | null {
  switch (severity) {
    case 'removesItem': return 'removes the item';
    case 'warning': return 'warning';
    case 'warningTextKept': return 'warning, text kept';
    case 'addedByOverseer': return 'added by Overseer';
    case 'failsDocument': return 'fails the document';
    default: return severity ? severity : null;
  }
}

const TOKEN_PATTERN = /\{\{[^{}]*\}\}/g;

/** The text split into plain runs and `{{token}}`s, in order; empty for empty text. */
export function tokenSegments(text: string | null | undefined): TextSegment[] {
  const source = text ?? '';
  const segments: TextSegment[] = [];
  let last = 0;
  for (const match of source.matchAll(TOKEN_PATTERN)) {
    const start = match.index ?? 0;
    if (start > last) {
      segments.push({ text: source.slice(last, start), token: false });
    }
    segments.push({ text: match[0], token: true });
    last = start + match[0].length;
  }
  if (last < source.length) {
    segments.push({ text: source.slice(last), token: false });
  }
  return segments;
}

/** The word-cap rule, whose trimming removes sentences from the end of a section. */
const WORD_CAP_RULE = 7;

/**
 * A note that removed sentences rather than the paragraph: a word-cap note's trailing sentences (*Removed its last 2
 * sentences…*), or one faulty sentence of a paragraph otherwise kept (`sentenceOnly`).
 */
function isShortening(issue: BenchmarkReportPackValidationIssueDto): boolean {
  if (issue.sentenceOnly === true) {
    return true;
  }
  return issue.rule === WORD_CAP_RULE && issue.dropped && /^Removed its last\b/i.test(issue.message ?? '');
}

/** The sentences a shortening removed: the count its message names, else one. */
export function removedSentenceCount(issue: Pick<ExplainedIssue, 'meaning'>): number {
  const match = /^Removed its last (\d+) sentences\b/i.exec(issue.meaning ?? '');
  return match ? Number(match[1]) : 1;
}

/** A note of text Overseer wrote in place of a removed one. */
function isAddedByOverseer(issue: BenchmarkReportPackValidationIssueDto): boolean {
  return issue.addedByOverseer === true || issue.severity === 'addedByOverseer';
}

export function explainIssue(issue: BenchmarkReportPackValidationIssueDto): ExplainedIssue {
  const text = issue.text ?? null;
  return {
    rule: issue.rule,
    ruleName: issue.ruleName?.trim() || `Rule ${issue.rule}`,
    severity: issue.severity || null,
    location: issue.locationLabel?.trim() || issue.location || 'An unknown place',
    meaning: issue.message,
    text,
    addedByOverseer: isAddedByOverseer(issue),
    shortened: isShortening(issue),
    meaningParts: tokenSegments(issue.message),
    textParts: text === null ? null : tokenSegments(text)
  };
}

/** Issues grouped by rule, the most frequent first, ties in rule order. */
export function groupIssuesByRule(issues: readonly BenchmarkReportPackValidationIssueDto[]): IssueGroup[] {
  const groups = new Map<number, ExplainedIssue[]>();
  for (const issue of issues) {
    const list = groups.get(issue.rule) ?? [];
    list.push(explainIssue(issue));
    groups.set(issue.rule, list);
  }
  return [...groups.entries()]
    .map(([rule, list]) => ({ rule, ruleName: list[0].ruleName, count: list.length, issues: list }))
    .sort((a, b) => b.count - a.count || a.rule - b.rule);
}

/** `20 issues (Ranking without the overlap caveat: 13; Digit in prose: 7)`. */
export function issueCountSummary(issues: readonly BenchmarkReportPackValidationIssueDto[]): string {
  const groups = groupIssuesByRule(issues);
  const parts = groups.map(group => `${group.ruleName}: ${numberFormat.format(group.count)}`);
  return `${plural(issues.length, 'issue', 'issues')}${parts.length > 0 ? ` (${parts.join('; ')})` : ''}`;
}

/** The number of revision turns: counted from the calls when known, else one after a first draft with issues. */
export function revisionCount(input: DocumentStatusInput): number | null {
  if (input.calls && input.calls.length > 0) {
    return input.calls.filter(call => call.kind === 'Revision').length;
  }
  if ((input.firstTurnIssues?.length ?? 0) > 0 && FINISHED_STATUSES.has(input.status)) {
    return 1;
  }
  return null;
}

/** The number of final shortening turns, from the calls; 0 when they are not known. */
export function shorteningCount(input: Pick<DocumentStatusInput, 'calls'>): number {
  return input.calls?.filter(call => call.kind === 'Shorten').length ?? 0;
}

/** Whether the first draft passed every check; null when that is not known. */
export function firstDraftPassed(input: DocumentStatusInput): boolean | null {
  if (input.firstDraftClean === true || input.firstDraftClean === false) {
    return input.firstDraftClean;
  }
  if (input.firstTurnIssues) {
    return input.firstTurnIssues.length === 0;
  }
  return null;
}

/** `Why the Executive Summary was completed with warnings`, `Why … failed`, `Why … was canceled`. */
export function documentStatusQuestion(title: string, status: string): string {
  switch (status) {
    case 'CompletedWithWarnings': return `Why ${title} was completed with warnings`;
    case 'Failed': return `Why ${title} failed`;
    case 'Canceled': return `Why ${title} was canceled`;
    default: return `How ${title} was checked`;
  }
}

/** `warnings in the Executive Summary`, `failure of the Executive Summary`: the info button's subject. */
export function documentStatusSubject(title: string, status: string): string {
  switch (status) {
    case 'CompletedWithWarnings': return `warnings in ${title}`;
    case 'Failed': return `failure of ${title}`;
    case 'Canceled': return `cancellation of ${title}`;
    default: return `checks of ${title}`;
  }
}

function removedNoun(removed: readonly ExplainedIssue[], count: number): string {
  const paragraphs = removed.every(issue => /paragraph/i.test(issue.location));
  return paragraphs ? (count === 1 ? 'paragraph' : 'paragraphs') : (count === 1 ? 'item' : 'items');
}

/** `a`, `a and b`, `a, b, and c`. */
function listOf(parts: readonly string[]): string {
  if (parts.length <= 2) {
    return parts.join(' and ');
  }
  return `${parts.slice(0, -1).join(', ')}, and ${parts[parts.length - 1]}`;
}

/** `Overseer wrote one paragraph in place of a removed one`. */
function addedClause(count: number): string {
  return count === 1
    ? 'Overseer wrote one paragraph in place of a removed one'
    : `Overseer wrote ${numberWord(count)} paragraphs in place of removed ones`;
}

function summaryOf(
  input: DocumentStatusInput,
  removed: readonly ExplainedIssue[],
  added: readonly ExplainedIssue[],
  kept: readonly ExplainedIssue[]
): string {
  switch (input.status) {
    case 'Failed':
      return 'This document could not be stored.';
    case 'Canceled':
      return 'This document was canceled before it was stored.';
    case 'Completed':
    case 'CompletedWithWarnings': {
      const parts: string[] = [];
      const shortened = removed.filter(issue => issue.shortened);
      const whole = removed.filter(issue => !issue.shortened);
      // Each sentence a salvage removed has its own note, so a paragraph is counted once per location.
      const shortenedCount = new Set(shortened.map(issue => issue.location)).size;
      if (shortenedCount > 0) {
        parts.push(`${numberWord(shortenedCount)} ${removedNoun(shortened, shortenedCount)} ${shortenedCount === 1 ? 'was' : 'were'} shortened`);
      }
      if (whole.length > 0) {
        parts.push(`${numberWord(whole.length)} ${removedNoun(whole, whole.length)} ${whole.length === 1 ? 'was' : 'were'} removed`);
      }
      if (kept.length > 0) {
        parts.push(`${numberWord(kept.length)} ${kept.length === 1 ? 'warning was' : 'warnings were'} kept`);
      }
      if (parts.length > 0 || added.length > 0) {
        const passive = parts.length > 0 ? capitalize(listOf(parts)) : '';
        const overseer = added.length > 0 ? addedClause(added.length) : '';
        return `This document is usable. ${passive && overseer ? `${passive}, and ${overseer}` : passive || overseer}.`;
      }
      if (!input.finalIssues && input.status === 'CompletedWithWarnings') {
        return 'This document is usable. What was removed or kept with a warning is listed once the job finishes.';
      }
      return firstDraftPassed(input) === false
        ? 'This document is usable. Its revision cleared every issue the automatic check found.'
        : 'This document is usable. It passed every check.';
    }
    default:
      return 'This document is still being written.';
  }
}

function checksOf(
  input: DocumentStatusInput,
  source: DocumentStatusSource,
  removed: readonly ExplainedIssue[],
  added: readonly ExplainedIssue[],
  kept: readonly ExplainedIssue[],
  blocking: readonly ExplainedIssue[]
): string {
  const sentences: string[] = [];
  const first = input.firstTurnIssues ?? null;
  const passed = firstDraftPassed(input);
  if (first && first.length > 0) {
    sentences.push(`First draft: ${issueCountSummary(first)}.`);
  } else if (passed === true) {
    sentences.push('First draft passed every check.');
  } else if (passed === false) {
    sentences.push('First draft did not pass every check.');
  } else if (source === 'job' && !FINISHED_STATUSES.has(input.status)) {
    sentences.push('The first draft has not been checked yet.');
  }
  const revisions = revisionCount(input);
  const shortenings = shorteningCount(input);
  const shorteningPhrase = `${numberWord(shortenings)} ${shortenings === 1 ? 'shortening turn' : 'shortening turns'}`;
  if (revisions !== null && (revisions > 0 || passed === false || shortenings > 0)) {
    const revisionPhrase = revisions === 0 ? '' : `${numberWord(revisions)} ${revisions === 1 ? 'revision' : 'revisions'}`;
    if (shortenings > 0) {
      sentences.push(`${capitalize(revisionPhrase ? `${revisionPhrase} and ${shorteningPhrase}` : shorteningPhrase)}.`);
    } else {
      sentences.push(revisions === 0 ? 'No revision.' : `${capitalize(revisionPhrase)}.`);
    }
  }
  if (input.finalIssues) {
    const parts: string[] = [];
    const shortened = removed.filter(issue => issue.shortened);
    const whole = removed.filter(issue => !issue.shortened);
    if (shortened.length > 0) {
      const sentencesRemoved = shortened.reduce((sum, issue) => sum + removedSentenceCount(issue), 0);
      parts.push(plural(sentencesRemoved, 'sentence removed', 'sentences removed'));
    }
    if (whole.length > 0) {
      parts.push(plural(whole.length, 'removal', 'removals'));
    }
    if (added.length > 0) {
      parts.push(plural(added.length, 'paragraph added by Overseer', 'paragraphs added by Overseer'));
    }
    if (kept.length > 0) {
      parts.push(plural(kept.length, 'warning', 'warnings'));
    }
    if (blocking.length > 0) {
      parts.push(plural(blocking.length, 'issue that stops the document', 'issues that stop the document'));
    }
    sentences.push(parts.length > 0 ? `Final: ${parts.join(', ')}.` : 'Final: no issues.');
  }
  return sentences.join(' ');
}

function actionsOf(input: DocumentStatusInput, source: DocumentStatusSource, kept: readonly ExplainedIssue[]): string[] {
  const actions: string[] = [];
  const diagnostics = 'Copy the diagnostics from Log and diagnostics to report a problem with the writer or the check.';
  switch (input.status) {
    case 'CompletedWithWarnings':
      if (kept.length > 0) {
        actions.push('Read the passages kept with a warning before you share the document.');
      }
      actions.push('For a cleaner document, regenerate it: check Rewrite on its row under Documents of this comparison, then Generate.');
      break;
    case 'Completed':
      if (kept.length > 0) {
        actions.push('Read the passages kept with a warning before you share the document.');
      }
      break;
    case 'Failed':
      actions.push('Regenerate the document: check Write on its row under Documents of this comparison, then Generate.');
      break;
    case 'Canceled':
      actions.push('Write it again: check Write on its row under Documents of this comparison, then Generate.');
      break;
    default:
      return actions;
  }
  if (source === 'job' && input.status !== 'Completed') {
    actions.push(diagnostics);
  }
  return actions;
}

/**
 * Why a document has its status: one summary sentence, what was shortened or removed, what Overseer added
 * and what was kept with a warning (each with its location, rule and meaning, and the text), how the draft
 * was checked, and what can be done. Both the validation details dialog and the Report Pack diagnostics are
 * built from it.
 */
export function explainDocumentStatus(input: DocumentStatusInput, source: DocumentStatusSource = 'job'): DocumentStatusExplanation {
  const final = input.finalIssues ?? [];
  const added: ExplainedIssue[] = [];
  const removed: ExplainedIssue[] = [];
  const blocking: ExplainedIssue[] = [];
  const kept: ExplainedIssue[] = [];
  for (const issue of final) {
    const item = explainIssue(issue);
    if (item.addedByOverseer) {
      added.push(item);
    } else if (issue.dropped) {
      removed.push(item);
    } else if (issue.severity === 'failsDocument') {
      blocking.push(item);
    } else {
      kept.push(item);
    }
  }
  const failed = input.status === 'Failed';
  const reply = failed ? input.failedReply ?? '' : '';
  return {
    summary: summaryOf(input, removed, added, kept),
    removed,
    added,
    kept,
    blocking,
    checks: checksOf(input, source, removed, added, kept, blocking),
    actions: actionsOf(input, source, kept),
    error: failed && input.errorMessage ? input.errorMessage : null,
    failedReplyExcerpt: reply
      ? (reply.length > FAILED_REPLY_EXCERPT_CHARS ? `${reply.slice(0, FAILED_REPLY_EXCERPT_CHARS)}…` : reply)
      : null
  };
}

function oneLine(text: string): string {
  return text.replace(/\r\n|\r|\n/g, ' ').trim();
}

function issueLines(issue: ExplainedIssue, textLabel: string, indent: string): string[] {
  const lines = [`${indent}- ${issue.location}: ${issue.ruleName}. ${oneLine(issue.meaning)}`];
  if (issue.text) {
    lines.push(`${indent}    ${textLabel}: ${oneLine(issue.text)}`);
  }
  return lines;
}

/** The explanation as plain-text lines, each indented by `indent`, for the diagnostics. */
export function explanationLines(explanation: DocumentStatusExplanation, indent = '  '): string[] {
  const lines = [`${indent}${explanation.summary}`];
  if (explanation.blocking.length > 0) {
    lines.push(`${indent}What stopped the document:`, ...explanation.blocking.flatMap(issue => issueLines(issue, 'Text', indent)));
  }
  if (explanation.removed.length > 0) {
    lines.push(`${indent}What was removed:`, ...explanation.removed.flatMap(issue => issueLines(issue, 'Removed text', indent)));
  }
  if (explanation.added.length > 0) {
    lines.push(`${indent}What Overseer added:`, ...explanation.added.flatMap(issue => issueLines(issue, 'Added text', indent)));
  }
  if (explanation.kept.length > 0) {
    lines.push(`${indent}What was kept with a warning:`, ...explanation.kept.flatMap(issue => issueLines(issue, 'Flagged text', indent)));
  }
  if (explanation.checks) {
    lines.push(`${indent}How the draft was checked: ${explanation.checks}`);
  }
  if (explanation.error) {
    lines.push(`${indent}Error: ${oneLine(explanation.error)}`);
  }
  if (explanation.failedReplyExcerpt) {
    lines.push(`${indent}The writer's last reply began: ${oneLine(explanation.failedReplyExcerpt)}`);
  }
  if (explanation.actions.length > 0) {
    lines.push(`${indent}What you can do:`, ...explanation.actions.map(action => `${indent}- ${action}`));
  }
  return lines;
}
