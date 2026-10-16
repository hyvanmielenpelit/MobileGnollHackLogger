import {
  BenchmarkReportAudience,
  BenchmarkReportPackCallDto,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackJobDto,
  BenchmarkReportPackValidationIssueDto,
  BenchmarkReportScope
} from '../../../services/admin-benchmark.service';
import { copyToClipboard } from '../../../utils/clipboard.util';
import { downloadTextFile, safeFileName } from '../../../utils/download.util';
import {
  ClientPollState,
  NOT_RECORDED,
  formatUtcSeconds,
  spanMs
} from '../run-ai-reports/run-report-writing-diagnostics';
import type { ReportPackContext } from './report-pack-panel.component';
import {
  REPORT_CHART_FIGURES,
  ReportChartFigureKey,
  ReportChartRowStatus,
  ReportChartScope,
  ReportChartSelection,
  reportChartDrawn
} from './report-charts';
import {
  ReportCostRange,
  audienceLabel,
  formatCostRangeUsd,
  formatCostUsd,
  formatElapsed,
  isCostRange,
  reportCallKindLabel,
  reportPhaseLabel
} from './report-document-format';
import { documentStatusQuestion, explainDocumentStatus, explanationLines, severityWord } from './report-validation-explanation';

/** The clipboard and file side effects of the Reports panel, held in an object so a spec can observe them. */
export const reportPackIo = {
  copy: (text: string): Promise<boolean> => copyToClipboard(text),
  download: (fileName: string, text: string): void => downloadTextFile(fileName, text, 'text/plain;charset=utf-8'),
  downloadMarkdown: (fileName: string, text: string): void => downloadTextFile(fileName, text, 'text/markdown;charset=utf-8')
};

/** What the diagnostics are built from. */
export interface ReportPackDiagnosticsInput {
  readonly context: ReportPackContext | null;
  readonly job: BenchmarkReportPackJobDto | null;
  /** The subject chosen in the form, for when no job is known. */
  readonly subjectKey: string | null;
  readonly chartSelection: ReportChartSelection;
  readonly chartStatus: Readonly<Record<number, ReportChartRowStatus>>;
  readonly chartStorageMissing: boolean;
  readonly chartAdvisory: string | null;
  readonly client: ClientPollState;
  /** The estimate shown when the job was started here, first calls alone to one repair turn per document, or null. */
  readonly estimateUsd: ReportCostRange | null;
  readonly nowUtc: Date;
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

/** `Comparison`, `Per model` or `Chat consistency`. */
function scopeWord(scope: BenchmarkReportScope | null | undefined): string {
  switch (scope) {
    case BenchmarkReportScope.Comparison: return 'Comparison';
    case BenchmarkReportScope.Model: return 'Per model';
    case BenchmarkReportScope.ChatConsistency: return 'Chat consistency';
    default: return scope === null || scope === undefined ? NOT_RECORDED : String(scope);
  }
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

/** The job's documents cover several entries of a comparison as equals, as `report-charts.ts` places their figures. */
function chartScopeOf(job: BenchmarkReportPackJobDto | null): ReportChartScope {
  return job?.scope === BenchmarkReportScope.Comparison ? 'comparison' : 'model';
}

function figureTitle(key: string): string {
  return REPORT_CHART_FIGURES.find(figure => figure.key === key)?.title ?? key;
}

/** The selected figures a document of `scope` draws, and those it does not. */
function selectedFigures(selection: ReportChartSelection, audience: BenchmarkReportAudience, scope: ReportChartScope)
  : { drawn: readonly ReportChartFigureKey[]; notDrawn: readonly ReportChartFigureKey[] } {
  const keys = selection[audience] ?? [];
  return {
    drawn: keys.filter(key => reportChartDrawn(audience, key, scope)),
    notDrawn: keys.filter(key => !reportChartDrawn(audience, key, scope))
  };
}

/**
 * A document's chart status in words. A Root Cause Analysis outside chat consistency also attaches the figures
 * Overseer draws itself, so its count is split into the selected ones and those: `8 attached: 4 selected, 4 drawn by
 * Overseer (16 images, named and anonymized)`.
 */
export function reportPackChartStatusWord(
  doc: BenchmarkReportPackDocumentProgressDto,
  input: Pick<ReportPackDiagnosticsInput, 'chartSelection' | 'chartStatus' | 'chartStorageMissing'>,
  scope: ReportChartScope = 'model'
): string {
  if (input.chartStorageMissing) {
    return 'none (chart storage is not configured)';
  }
  if ((input.chartSelection[doc.audience] ?? []).length === 0) {
    return 'none (no chart selected for this document type)';
  }
  const status = doc.documentId === null ? undefined : input.chartStatus[doc.documentId];
  if (!status) {
    return NOT_RECORDED;
  }
  switch (status.state) {
    case 'attaching': return 'attaching';
    case 'done': {
      const selected = selectedFigures(input.chartSelection, doc.audience, scope).drawn.length;
      const byOverseer = doc.audience === BenchmarkReportAudience.RootCauseAnalysis ? status.count - selected : 0;
      const head = byOverseer > 0
        ? `${numberFormat.format(status.count)} attached: ${numberFormat.format(selected)} selected, ${numberFormat.format(byOverseer)} drawn by Overseer`
        : `${numberFormat.format(status.count)} attached`;
      return status.images === status.count ? head : `${head} (${numberFormat.format(status.images)} images, named and anonymized)`;
    }
    case 'failed': return `failed: ${orNotRecorded(status.message)}`;
    default: return `none (${orNotRecorded(status.reason)})`;
  }
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
function callLine(call: BenchmarkReportPackCallDto, index: number, outputLimit: number | null | undefined): string {
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
 */
function writerRepliesLine(calls: readonly BenchmarkReportPackCallDto[]): string | null {
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

function documentLines(
  doc: BenchmarkReportPackDocumentProgressDto,
  index: number,
  serverNow: string | null | undefined,
  input: ReportPackDiagnosticsInput
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
    lines.push(...doc.calls.map((call, i) => callLine(call, i, input.job?.writerMaxOutputTokens)));
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
  const scope = chartScopeOf(input.job);
  const figures = selectedFigures(input.chartSelection, doc.audience, scope);
  lines.push(`  Charts selected: ${figures.drawn.length === 0 ? 'none' : figures.drawn.map(figureTitle).join('; ')}`);
  if (figures.notDrawn.length > 0) {
    lines.push(`  Selected but not drawn in this document type: ${figures.notDrawn.map(figureTitle).join('; ')}`);
  }
  lines.push(`  Charts: ${reportPackChartStatusWord(doc, input, scope)}`);
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
function wordCountLines(documents: readonly BenchmarkReportPackDocumentProgressDto[]): string[] {
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
function preparationLines(job: BenchmarkReportPackJobDto | null): string[] {
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
function whyLines(documents: readonly BenchmarkReportPackDocumentProgressDto[]): string[] {
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
function validationLines(documents: readonly BenchmarkReportPackDocumentProgressDto[]): string[] {
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
function failedReplyLines(documents: readonly BenchmarkReportPackDocumentProgressDto[]): string[] {
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

/** `report-pack_<subject>_<kind>_<yyyyMMdd-HHmmss>.<extension>`, the time in UTC. */
function reportPackFileName(subjectLabel: string | null | undefined, kind: string, extension: string, nowUtc: Date): string {
  const iso = nowUtc.toISOString();
  const stamp = `${iso.slice(0, 4)}${iso.slice(5, 7)}${iso.slice(8, 10)}-${iso.slice(11, 13)}${iso.slice(14, 16)}${iso.slice(17, 19)}`;
  const subject = subjectLabel && subjectLabel.trim() ? safeFileName(subjectLabel) : 'no-subject';
  return `report-pack_${subject}_${kind}_${stamp}.${extension}`;
}

/** `report-pack_gemini-flash_diagnostics_20260929-120005.txt`, the time in UTC. */
export function reportPackDiagnosticsFileName(subjectLabel: string | null | undefined, nowUtc: Date): string {
  return reportPackFileName(subjectLabel, 'diagnostics', 'txt', nowUtc);
}

/** `report-pack_comparison-4_writer-replies_20261016-113542.md`, the time in UTC. */
export function reportPackWriterRepliesFileName(subjectLabel: string | null | undefined, nowUtc: Date): string {
  return reportPackFileName(subjectLabel, 'writer-replies', 'md', nowUtc);
}

/**
 * The plain-text diagnostics of the Reports panel: the comparison, the job with its writer and
 * versions, the preparation notes, one block per document with its phase, writer calls (and a pointer
 * to the writer's replies, which are a separate download) and chart status, the word counts of the
 * capped sections, the validation issues (rule names, severities, removed and added text, and the
 * revision against the first draft) and failed writer replies when there are any, why each document
 * completed with warnings, failed or was canceled (the same text as the dialog), the totals, the
 * client's polling and the job log. One fact per line except a raw failed reply, LF line endings,
 * absent facts reading "not recorded". The user who started the job is never included.
 */
export function buildReportPackDiagnostics(input: ReportPackDiagnosticsInput): string {
  const { context, job, client } = input;
  const serverNow = job?.serverTimeUtc ?? null;
  const lines: string[] = [];

  lines.push('Overseer Report Pack diagnostics');
  lines.push(`Captured: ${time(input.nowUtc)}`);

  lines.push('', '== Comparison ==');
  lines.push(`Scope: ${scopeWord(job?.scope)}`);
  lines.push(`Comparison id: ${orNotRecorded(job?.comparisonId)}`);
  lines.push(`Reference model: ${job ? (job.referenceModel === undefined ? NOT_RECORDED : job.referenceModel ?? 'none') : NOT_RECORDED}`);
  lines.push(`Suite: ${orNotRecorded(context?.suiteName ?? job?.suiteName)}`);
  const entryKeys = job?.entries && job.entries.length > 0 ? job.entries : context?.entryKeys ?? null;
  const batteryRunIds = context?.batteryRunIds ?? [];
  const batteryComparison = batteryRunIds.length > 0
    || (entryKeys !== null && entryKeys.length > 0 && entryKeys.every(key => key.startsWith('battery:')));
  const suiteId = context?.suiteId ?? job?.suiteId;
  lines.push(`Suite id: ${(suiteId === null || suiteId === undefined) && batteryComparison
    ? 'not applicable (battery comparison)'
    : orNotRecorded(suiteId)}`);
  lines.push(`Entries: ${entryKeys ? `${count(entryKeys.length)} (${entryKeys.join(', ') || NOT_RECORDED})` : NOT_RECORDED}`);
  const noneInABattery = batteryComparison ? 'none (battery comparison)' : NOT_RECORDED;
  lines.push(`Runs: ${context && context.runIds.length > 0 ? context.runIds.join(', ') : noneInABattery}`);
  lines.push(`Groups: ${context && context.groupIds.length > 0 ? context.groupIds.join(', ') : noneInABattery}`);
  if (batteryRunIds.length > 0) {
    lines.push(`Battery runs: ${batteryRunIds.join(', ')}`);
  }
  lines.push(`Pricing basis: ${orNotRecorded(context?.pricingBasis)}`);
  const subjectKey = job?.subjectKey ?? input.subjectKey;
  const subjectEntry = context?.entries.find(entry => entry.key === subjectKey);
  lines.push(`Subject: ${orNotRecorded(job?.subjectLabel ?? subjectEntry?.label)} (${orNotRecorded(subjectKey)})`);

  lines.push('', '== Job ==');
  lines.push(`Job id: ${orNotRecorded(job?.id)}`);
  lines.push(`Pack id: ${orNotRecorded(job?.packId)}`);
  lines.push(`Status: ${orNotRecorded(job?.status)}`);
  lines.push(`Writer: ${orNotRecorded(job?.writerDisplayName)}`);
  lines.push(`Writer config id: ${orNotRecorded(job?.writerConfigId)}`);
  lines.push(`Writer provider: ${orNotRecorded(job?.writerProvider)}`);
  lines.push(`Writer model id: ${orNotRecorded(job?.writerModelId)}`);
  lines.push(`Writer thinking level: ${orNotRecorded(job?.writerThinkingLevel)}`);
  lines.push(`Writer output limit: ${typeof job?.writerMaxOutputTokens === 'number'
    ? `${count(job.writerMaxOutputTokens)} tokens (reasoning included)`
    : NOT_RECORDED}`);
  lines.push(`Report format version: ${orNotRecorded(job?.reportFormatVersion)}`);
  lines.push(`Engine version: ${job?.engineVersion === null ? 'none (no Root Cause Analysis)' : orNotRecorded(job?.engineVersion)}`);
  lines.push(`PDF layout version: ${orNotRecorded(job?.pdfLayoutVersion)}`);
  lines.push(`Word layout version: ${orNotRecorded(job?.wordLayoutVersion)}`);
  lines.push(`Writer prompt SHA-256: ${orNotRecorded(job?.writerPromptSha256)}`);
  lines.push(`Started: ${time(job?.startedAtUtc)}`);
  lines.push(`Completed: ${time(job?.completedAtUtc)}`);
  lines.push(`Server time at the last response: ${time(serverNow)}`);
  lines.push(`Elapsed: ${job ? openSpan(job.startedAtUtc, job.completedAtUtc, serverNow) : NOT_RECORDED}`);

  lines.push('', '== Charts ==');
  lines.push(`Chart storage: ${input.chartStorageMissing ? 'not configured' : 'configured'}`);
  lines.push(`Advisory: ${orNotRecorded(input.chartAdvisory)}`);

  lines.push(...preparationLines(job));

  lines.push('', '== Documents ==');
  const documents = job?.documents ?? [];
  if (documents.length === 0) {
    lines.push(`Documents: ${NOT_RECORDED}`);
  } else {
    documents.forEach((doc, index) => lines.push(...documentLines(doc, index, serverNow, input)));
  }
  lines.push(...wordCountLines(documents), ...validationLines(documents), ...failedReplyLines(documents), ...whyLines(documents));

  lines.push('', '== Totals ==');
  lines.push(`Model calls: ${count(job?.totalModelCalls)}`);
  lines.push(`Input tokens: ${count(job?.inputTokens)}`);
  lines.push(`Output tokens: ${count(job?.outputTokens)}`);
  lines.push(`Cost: ${cost(job?.costUsd)}`);
  const estimate = input.estimateUsd;
  if (estimate !== null && Number.isFinite(estimate.low)) {
    lines.push(isCostRange(estimate.low, estimate.high)
      ? `Estimate: ${formatCostRangeUsd(estimate.low, estimate.high)} (first call only, to one repair turn per document; a shortening turn or a second repair is not included)`
      : `Estimate: ${formatCostUsd(estimate.low)}`);
  }

  lines.push('', '== Client polling ==');
  lines.push(`Polls: ${count(client.pollCount)}`);
  lines.push(`Last success: ${time(client.lastSuccessUtc)}`);
  lines.push(`Consecutive failures: ${count(client.consecutiveFailures)}`);
  const lastError = client.lastError;
  lines.push(`Last error: ${lastError
    ? `HTTP ${lastError.httpStatus ?? 'none (network)'}: ${orNotRecorded(lastError.message)}`
    : NOT_RECORDED}`);

  lines.push('', '== Job log ==');
  const log = job?.log ?? [];
  if (log.length === 0) {
    lines.push(`Log: ${NOT_RECORDED}`);
  } else {
    for (const entry of log) {
      lines.push(`[${time(entry.timestampUtc)}] [${orNotRecorded(entry.severity)}] ${orNotRecorded(entry.message)}`);
    }
  }

  return lines.join('\n') + '\n';
}
