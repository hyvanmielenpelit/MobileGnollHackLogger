import {
  BenchmarkReportPackCallDto,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackJobDto,
  BenchmarkReportPackValidationIssueDto
} from '../../../services/admin-benchmark.service';
import { parseServerUtcDate } from '../../../utils/date.util';
import {
  audienceLabel,
  formatCostUsd,
  formatElapsed,
  reportCallKindLabel,
  reportPhaseLabel
} from './report-document-format';
import { documentStatusQuestion, explainDocumentStatus, explanationLines, severityWord } from './report-validation-explanation';

/*
 * The diagnostics sections a report writing job's two views share: the Report Pack panel's
 * (`report-pack-diagnostics.ts`) and the run and battery run writing dialog's
 * (`run-report-writing-diagnostics.ts`). One fact per line, absent facts reading "not recorded".
 */

/** The value every absent or null fact reads as. */
export const NOT_RECORDED = 'not recorded';

/** `2026-09-29 12:00:05 UTC`, or null when the value is absent or unreadable. */
export function formatUtcSeconds(value: string | Date | null | undefined): string | null {
  if (value === null || value === undefined || value === '') {
    return null;
  }
  const date = parseServerUtcDate(value);
  if (Number.isNaN(date.getTime())) {
    return null;
  }
  return `${date.toISOString().slice(0, 19).replace('T', ' ')} UTC`;
}

/** Milliseconds between two server timestamps, or null when either is absent or the span is negative. */
export function spanMs(start: string | null | undefined, end: string | null | undefined): number | null {
  if (!start || !end) {
    return null;
  }
  const from = parseServerUtcDate(start).getTime();
  const to = parseServerUtcDate(end).getTime();
  if (Number.isNaN(from) || Number.isNaN(to) || to < from) {
    return null;
  }
  return to - from;
}

const numberFormat = new Intl.NumberFormat('en-US');

function oneLine(text: string): string {
  return text.replace(/\r\n|\r|\n/g, ' ').trim();
}

function orNotRecorded(value: string | number | null | undefined): string {
  if (value === null || value === undefined) {
    return NOT_RECORDED;
  }
  const text = typeof value === 'number' ? String(value) : oneLine(value);
  return text === '' ? NOT_RECORDED : text;
}

function count(value: number | null | undefined): string {
  return value === null || value === undefined || !Number.isFinite(value) ? NOT_RECORDED : numberFormat.format(value);
}

function time(value: string | Date | null | undefined): string {
  return formatUtcSeconds(value) ?? NOT_RECORDED;
}

function cost(value: number | null | undefined): string {
  return value === null || value === undefined || !Number.isFinite(value) ? NOT_RECORDED : formatCostUsd(value);
}

/** A span that ends at `end`, or runs on to the server's clock with "so far" while `end` is absent. */
function openSpan(start: string | null | undefined, end: string | null | undefined, serverNow: string | null | undefined): string {
  if (!start) {
    return NOT_RECORDED;
  }
  if (end) {
    const ms = spanMs(start, end);
    return ms === null ? NOT_RECORDED : formatElapsed(ms);
  }
  const soFar = spanMs(start, serverNow);
  return soFar === null ? NOT_RECORDED : `${formatElapsed(soFar)} so far`;
}

/**
 * The job's writer output limit and versions: `Writer output limit: …` to `Writer prompt SHA-256: …`,
 * without a heading.
 */
export function versionLines(job: BenchmarkReportPackJobDto | null | undefined): string[] {
  return [
    `Writer output limit: ${typeof job?.writerMaxOutputTokens === 'number'
      ? `${count(job.writerMaxOutputTokens)} tokens (reasoning included)`
      : NOT_RECORDED}`,
    `Report format version: ${orNotRecorded(job?.reportFormatVersion)}`,
    `Engine version: ${job?.engineVersion === null ? 'none (no Root Cause Analysis)' : orNotRecorded(job?.engineVersion)}`,
    `PDF layout version: ${orNotRecorded(job?.pdfLayoutVersion)}`,
    `Word layout version: ${orNotRecorded(job?.wordLayoutVersion)}`,
    `Writer prompt SHA-256: ${orNotRecorded(job?.writerPromptSha256)}`
  ];
}

/** `, reply 22,113 characters`, `, no reply`; empty from a server that sends no reply length. */
function replyPart(call: BenchmarkReportPackCallDto): string {
  if (call.replyChars === undefined) {
    return '';
  }
  return call.replyChars === null ? ', no reply' : `, reply ${count(call.replyChars)} characters`;
}

/** ` (14,950 reasoning)` when the call reported reasoning tokens; empty otherwise. */
function reasoningPart(call: BenchmarkReportPackCallDto): string {
  return (call.reasoningTokens ?? 0) > 0 ? ` (${count(call.reasoningTokens)} reasoning)` : '';
}

/** A call's output against the writer's output limit at or above which the diagnostics call it near the limit. */
const NEAR_OUTPUT_LIMIT = 0.8;

/** `, output 85% of the limit (near the limit)`; empty without a known limit or output count. */
function outputLimitPart(call: BenchmarkReportPackCallDto, limit: number | null | undefined): string {
  if (typeof limit !== 'number' || !(limit > 0) || !Number.isFinite(call.outputTokens)) {
    return '';
  }
  const share = call.outputTokens / limit;
  return `, output ${Math.round(share * 100)}% of the limit${share >= NEAR_OUTPUT_LIMIT ? ' (near the limit)' : ''}`;
}

/**
 * `Call 2: Revision, started 2026-09-28 10:00:50 UTC, 32 s, 6,000 in / 1,000 cached / 1,500 out tokens
 * (900 reasoning), output 5% of the limit, $0.05, finish stop, 4 messages, 20,000 characters, reply 5,100 characters`;
 * a shortening turn reads `Shortening turn`.
 */
export function callLine(call: BenchmarkReportPackCallDto, index: number, outputLimit: number | null | undefined): string {
  const duration = Number.isFinite(call.durationMs) ? formatElapsed(call.durationMs) : NOT_RECORDED;
  return `    Call ${index + 1}: ${orNotRecorded(reportCallKindLabel(call.kind))}, started ${time(call.startedAtUtc)}, ${duration}, `
    + `${count(call.inputTokens)} in / ${count(call.cachedInputTokens)} cached / ${count(call.outputTokens)} out tokens${reasoningPart(call)}`
    + `${outputLimitPart(call, outputLimit)}, `
    + `${cost(call.costUsd)}, finish ${orNotRecorded(call.finishReason)}, `
    + `${count(call.requestMessages)} messages, ${count(call.requestCharacters)} characters${replyPart(call)}`;
}

/**
 * `Writer replies: 2 (49,812 characters), downloadable separately as Markdown (…) while Overseer still
 * holds this job.`; null when no call has a reply. The replies themselves are never in the diagnostics.
 * Both views offer the download as *Download the writer's replies* under *Log and diagnostics*.
 */
export function writerRepliesLine(calls: readonly BenchmarkReportPackCallDto[]): string | null {
  const replies = calls.filter(call => (call.replyChars ?? 0) > 0);
  if (replies.length === 0) {
    return null;
  }
  const chars = replies.reduce((sum, call) => sum + (call.replyChars ?? 0), 0);
  return `  Writer replies: ${count(replies.length)} (${count(chars)} characters), downloadable separately as Markdown `
    + '(Log and diagnostics → Download the writer\'s replies) while Overseer still holds this job.';
}

function firstDraftCleanWord(value: boolean | null | undefined): string {
  return value === true ? 'yes' : value === false ? 'no' : 'not checked';
}

/**
 * One document's block without its chart lines: status, phase, first draft, timing, every writer call
 * against `outputLimit`, the writer-replies pointer, tokens, cost, document id and error.
 */
export function documentLines(
  doc: BenchmarkReportPackDocumentProgressDto,
  index: number,
  serverNow: string | null | undefined,
  outputLimit: number | null | undefined
): string[] {
  const lines = [
    `Document ${index + 1}: ${audienceLabel(doc.audience)}`,
    `  Status: ${orNotRecorded(doc.status)}`
  ];
  if (doc.phase !== undefined) {
    lines.push(`  Phase: ${doc.phase ? `${reportPhaseLabel(doc.phase)} (${doc.phase})` : 'waiting'}`);
    lines.push(`  Phase detail: ${orNotRecorded(doc.phaseDetail)}`);
  }
  lines.push(
    `  First draft clean: ${firstDraftCleanWord(doc.firstDraftClean)}`,
    `  Started: ${time(doc.startedAtUtc)}`,
    `  Completed: ${time(doc.completedAtUtc)}`,
    `  Duration: ${openSpan(doc.startedAtUtc, doc.completedAtUtc, serverNow)}`,
    `  Model calls: ${count(doc.modelCalls)}`
  );
  if (doc.calls && doc.calls.length > 0) {
    lines.push(...doc.calls.map((call, i) => callLine(call, i, outputLimit)));
    const replies = writerRepliesLine(doc.calls);
    if (replies) {
      lines.push(replies);
    }
  }
  lines.push(
    `  Input tokens: ${count(doc.inputTokens)}`,
    `  Output tokens: ${count(doc.outputTokens)}`,
    `  Cost: ${cost(doc.costUsd)}`,
    `  Document id: ${orNotRecorded(doc.documentId)}`,
    `  Error: ${orNotRecorded(doc.errorMessage)}`,
  );
  return lines;
}

/** `rule 2: 5, rule 16: 12`, in ascending rule order; empty for no issues. */
function ruleCounts(issues: readonly BenchmarkReportPackValidationIssueDto[]): string {
  const byRule = new Map<number, number>();
  for (const issue of issues) {
    byRule.set(issue.rule, (byRule.get(issue.rule) ?? 0) + 1);
  }
  return [...byRule.entries()].sort((a, b) => a[0] - b[0]).map(([rule, n]) => `rule ${rule}: ${numberFormat.format(n)}`).join(', ');
}

/** `17 issues (rule 2: 5, rule 16: 12)`, counts by rule in ascending rule order. */
function issueCounts(issues: readonly BenchmarkReportPackValidationIssueDto[]): string {
  const rules = ruleCounts(issues);
  return `${numberFormat.format(issues.length)} ${issues.length === 1 ? 'issue' : 'issues'}${rules ? ` (${rules})` : ''}`;
}

/**
 * `rule 16 (Ranking without the overlap caveat, warning) at Hypotheses: cost, paragraph 2: …`, with
 * `(dropped)` when the item was removed and the removed or flagged text indented below it. A server that
 * sends no rule name or location label gives `rule 16 at sections.abstract: …`.
 */
function issueLines(issue: BenchmarkReportPackValidationIssueDto, indent = '    '): string[] {
  const name = issue.ruleName?.trim();
  const severity = severityWord(issue.severity);
  const rule = name ? `rule ${issue.rule} (${severity ? `${name}, ${severity}` : name})` : `rule ${issue.rule}`;
  const location = issue.locationLabel?.trim() || orNotRecorded(issue.location);
  const lines = [`${indent}${rule} at ${location}: ${orNotRecorded(issue.message)}${issue.dropped ? ' (dropped)' : ''}`];
  if (issue.text) {
    const label = issue.addedByOverseer ? 'Added text' : issue.dropped ? 'Removed text' : 'Flagged text';
    lines.push(`${indent}  ${label}: ${oneLine(issue.text)}`);
  }
  return lines;
}

/** An issue's location in words, or its raw location from a server that sends no label. */
function issueLocation(issue: BenchmarkReportPackValidationIssueDto): string {
  return issue.locationLabel?.trim() || orNotRecorded(issue.location);
}

/** The issues of `issues` without a match in `other`, matched one to one by rule and location. */
function unmatched(
  issues: readonly BenchmarkReportPackValidationIssueDto[],
  other: readonly BenchmarkReportPackValidationIssueDto[]
): BenchmarkReportPackValidationIssueDto[] {
  const left = new Map<string, number>();
  for (const issue of other) {
    const key = `${issue.rule}|${issue.location}`;
    left.set(key, (left.get(key) ?? 0) + 1);
  }
  return issues.filter(issue => {
    const key = `${issue.rule}|${issue.location}`;
    const n = left.get(key) ?? 0;
    if (n > 0) {
      left.set(key, n - 1);
      return false;
    }
    return true;
  });
}

/** `2 (rule 7 at Conclusions; rule 31 at Key finding)`; `0` for none. */
function issuePlaces(issues: readonly BenchmarkReportPackValidationIssueDto[]): string {
  return issues.length === 0
    ? '0'
    : `${count(issues.length)} (${issues.map(issue => `rule ${issue.rule} at ${issueLocation(issue)}`).join('; ')})`;
}

/**
 * The revision against the first draft, matched by rule and location: what it fixed (counted by rule),
 * what is still there and what is new.
 */
function revisionDifferenceLines(
  first: readonly BenchmarkReportPackValidationIssueDto[] | null | undefined,
  revision: readonly BenchmarkReportPackValidationIssueDto[]
): string[] {
  const lines: string[] = [];
  const added = first ? unmatched(revision, first) : [...revision];
  if (first) {
    const fixed = unmatched(first, revision);
    const rules = ruleCounts(fixed);
    lines.push(`  Fixed by the revision: ${count(fixed.length)}${rules ? ` (${rules})` : ''}`);
    lines.push(`  Still there: ${issuePlaces(revision.filter(issue => !added.includes(issue)))}`);
  }
  lines.push(`  New in the revision: ${issuePlaces(added)}`);
  return lines;
}

/**
 * `== Word counts ==`: per document that carries them, each capped section's raw word count at every
 * turn (a token counts as one) and the final count per paragraph. Empty when no document carries any.
 */
export function wordCountLines(documents: readonly BenchmarkReportPackDocumentProgressDto[]): string[] {
  const lines: string[] = [];
  documents.forEach((doc, index) => {
    const counts = doc.wordCounts;
    if (!counts || counts.length === 0) {
      return;
    }
    lines.push(`Document ${index + 1}: ${audienceLabel(doc.audience)}`);
    for (const entry of counts) {
      const turns = ([['draft', entry.draft], ['revision', entry.revision], ['shortened', entry.shortened], ['final', entry.final]] as const)
        .filter(([, value]) => value !== null && value !== undefined)
        .map(([name, value]) => `${name} ${count(value)}`);
      const paragraphs = entry.finalParagraphs && entry.finalParagraphs.length > 0
        ? ` — paragraphs ${entry.finalParagraphs.map(n => count(n)).join(', ')}`
        : '';
      lines.push(`  ${entry.locationLabel?.trim() || orNotRecorded(entry.location)} — cap ${count(entry.cap)} — `
        + `${turns.length > 0 ? turns.join(', ') : NOT_RECORDED} (raw words; a token counts as one)${paragraphs}`);
    }
  });
  return lines.length > 0 ? ['', '== Word counts ==', ...lines] : [];
}

/** `== Preparation notes ==`: the notes of the fact sheet and the engine; absent from a server that sends none. */
export function preparationLines(job: BenchmarkReportPackJobDto | null | undefined): string[] {
  const notes = job?.preparationNotes;
  if (!notes) {
    return [];
  }
  const lines = ['', '== Preparation notes =='];
  if (notes.length === 0) {
    lines.push('Preparation notes: none');
  } else {
    lines.push(...notes.flatMap(note => issueLines(note, '')));
  }
  return lines;
}

/** `== Why document 2 (Root Cause Analysis) failed ==`: per document completed with warnings, failed or canceled. */
export function whyLines(documents: readonly BenchmarkReportPackDocumentProgressDto[]): string[] {
  const lines: string[] = [];
  documents.forEach((doc, index) => {
    if (doc.status !== 'CompletedWithWarnings' && doc.status !== 'Failed' && doc.status !== 'Canceled') {
      return;
    }
    lines.push('', `== ${documentStatusQuestion(`document ${index + 1} (${audienceLabel(doc.audience)})`, doc.status)} ==`);
    lines.push(...explanationLines(explainDocumentStatus(doc, 'job'), ''));
  });
  return lines;
}

/**
 * `== Validation ==`: per document that carries them, the first turn's issues counted by rule and
 * listed; the revision's own issues, and what it fixed, kept and brought in against the first turn; the
 * issues left after the last validation; then each paragraph Overseer added. Empty when no document
 * carries any.
 */
export function validationLines(documents: readonly BenchmarkReportPackDocumentProgressDto[]): string[] {
  const lines: string[] = [];
  documents.forEach((doc, index) => {
    const first = doc.firstTurnIssues;
    const revision = doc.revisionIssues;
    const final = doc.finalIssues;
    if (!first && !revision && !final) {
      return;
    }
    lines.push(`Document ${index + 1}: ${audienceLabel(doc.audience)}`);
    if (first) {
      lines.push(`  First turn: ${issueCounts(first)}`, ...first.flatMap(issue => issueLines(issue)));
    }
    if (revision) {
      lines.push(`  Revision: ${issueCounts(revision)}`, ...revision.flatMap(issue => issueLines(issue)));
      lines.push(...revisionDifferenceLines(first, revision));
    }
    if (final) {
      lines.push(`  Final: ${issueCounts(final)}`, ...final.flatMap(issue => issueLines(issue)));
      for (const issue of final.filter(note => note.addedByOverseer)) {
        lines.push(`  Added by Overseer: ${issueLocation(issue)} — ${issue.text ? oneLine(issue.text) : NOT_RECORDED}`);
      }
    }
  });
  return lines.length > 0 ? ['', '== Validation ==', ...lines] : [];
}

/** `== Failed writer replies ==`: each failed document's last raw reply. Empty when none failed with one. */
export function failedReplyLines(documents: readonly BenchmarkReportPackDocumentProgressDto[]): string[] {
  const lines: string[] = [];
  documents.forEach((doc, index) => {
    if (!doc.failedReply) {
      return;
    }
    lines.push(
      `Document ${index + 1}: ${audienceLabel(doc.audience)}`,
      '--- reply ---',
      ...doc.failedReply.split(/\r\n|\r|\n/),
      '--- end of reply ---');
  });
  return lines.length > 0 ? ['', '== Failed writer replies ==', ...lines] : [];
}
