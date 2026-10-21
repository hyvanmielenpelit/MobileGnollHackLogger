import { formatDate } from '@angular/common';

import {
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportDocumentOrigin,
  BenchmarkRootCauseReadinessDto,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportEstimateDto,
  BenchmarkRunReportJobDto,
  SameProviderWarningDto,
  reportDisclosureParam
} from '../../../services/admin-benchmark.service';
import { parseServerUtcDate } from '../../../utils/date.util';
import {
  CHAT_CONSISTENCY_REPORT_AUDIENCE_OPTIONS,
  RUN_REPORT_AUDIENCE_OPTIONS,
  audienceLabel,
  formatCostUsd,
  formatElapsed,
  statusLabel as reportDocumentStatusWord
} from '../report-pack/report-document-format';

/*
 * The document list both AI Reports panels share: the run report's (`app-run-ai-reports`) and the
 * battery run report's (`app-battery-ai-reports`). One row per completion document, written or not,
 * its tag and meta line, the job status line, and the cost estimate block.
 */

/**
 * The two AI-written completion documents of a run or a battery run, in the order the panels list them:
 * the order they are written in, the Root Cause Analysis first.
 */
export const REPORT_DOCUMENT_AUDIENCES: readonly BenchmarkReportAudience[] =
  RUN_REPORT_AUDIENCE_OPTIONS.map(option => option.audience);

/** A battery-completion document's origin. */
export const BATTERY_COMPLETION_ORIGIN = BenchmarkReportDocumentOrigin.BatteryCompletion;

/** A chat consistency document's origin. */
export const CHAT_CONSISTENCY_REPORT_ORIGIN = BenchmarkReportDocumentOrigin.ChatConsistencyReport;

/** The two documents of a saved chat consistency analysis: the Executive Summary, then the Provider Issue Report. */
export const CHAT_CONSISTENCY_DOCUMENT_AUDIENCES: readonly BenchmarkReportAudience[] =
  CHAT_CONSISTENCY_REPORT_AUDIENCE_OPTIONS.map(option => option.audience);

/** One row of the document list: an audience and its stored document, if any. */
export interface ReportDocumentRow {
  audience: BenchmarkReportAudience;
  label: string;
  doc: BenchmarkReportDocumentListItemDto | null;
}

/** How the status line is drawn. */
export type ReportDocumentsStatusKind = 'plain' | 'progress' | 'failed' | 'skipped';

/**
 * The cost estimate block: waiting for the estimate, failed, no price card for the writer, or the
 * total with, for two or more documents, the cost of each.
 */
export interface ReportEstimateView {
  state: 'loading' | 'failed' | 'noPrice' | 'ready';
  /** The total, formatted; null unless ready. */
  total: string | null;
  /** One entry per document, only when there are two or more. */
  parts: { name: string; cost: string }[];
}

/** A subject's report status, from its job while the server knows it, else from the subject itself. */
export interface ReportStatusSnapshot {
  status: BenchmarkRunReportDocumentsStatus;
  message: string | null;
  writerId: number | null;
  writerName: string | null;
}

/** A documents status, read from its number or, from an older server, its name. */
export function reportDocumentsStatusOf(raw: unknown): BenchmarkRunReportDocumentsStatus {
  if (typeof raw === 'number') {
    return raw as BenchmarkRunReportDocumentsStatus;
  }
  if (typeof raw === 'string') {
    const value = BenchmarkRunReportDocumentsStatus[raw as keyof typeof BenchmarkRunReportDocumentsStatus];
    if (typeof value === 'number') {
      return value;
    }
  }
  return BenchmarkRunReportDocumentsStatus.NotRequested;
}

/** A job for the documents is queued or writing. */
export function reportJobInProgress(status: BenchmarkRunReportDocumentsStatus): boolean {
  return status === BenchmarkRunReportDocumentsStatus.Pending || status === BenchmarkRunReportDocumentsStatus.Writing;
}

/** The status a job view reports, with its writer. */
export function reportStatusSnapshotOfJob(job: BenchmarkRunReportJobDto): ReportStatusSnapshot {
  return {
    status: reportDocumentsStatusOf(job.status),
    message: job.message ?? null,
    writerId: job.writerConfigId ?? null,
    writerName: job.writerDisplayName ?? null
  };
}

/**
 * The completion documents of one subject (`run:<id>`, `battery:<id>` or `chat-consistency:<id>`)
 * of the given origin, the newest one per audience, in audience order: a run's two, or for the chat
 * consistency origin the Executive Summary and the Provider Issue Report. Documents of any other origin about
 * the same subject are left out, so a chat consistency document is never a run's or a battery run's.
 */
export function completionDocumentsOf(
  documents: readonly BenchmarkReportDocumentListItemDto[] | null | undefined,
  subjectKey: string,
  origin: number
): BenchmarkReportDocumentListItemDto[] {
  const newestFirst = (documents ?? [])
    .filter(doc => doc.origin === origin && doc.subjectKey === subjectKey)
    .sort((a, b) => (b.createdAtUtc ?? '').localeCompare(a.createdAtUtc ?? '') || b.id - a.id);
  const audiences = origin === CHAT_CONSISTENCY_REPORT_ORIGIN ? CHAT_CONSISTENCY_DOCUMENT_AUDIENCES : REPORT_DOCUMENT_AUDIENCES;
  return audiences
    .map(audience => newestFirst.find(doc => doc.audience === audience))
    .filter((doc): doc is BenchmarkReportDocumentListItemDto => !!doc);
}

/** One row per completion audience, in list order, with its stored document if any. */
export function reportDocumentRows(documents: readonly BenchmarkReportDocumentListItemDto[]): ReportDocumentRow[] {
  return REPORT_DOCUMENT_AUDIENCES.map(audience => ({
    audience,
    label: audienceLabel(audience),
    doc: documents.find(doc => doc.audience === audience) ?? null
  }));
}

/** The audiences without a stored document, in list order. */
export function missingReportAudiences(documents: readonly BenchmarkReportDocumentListItemDto[]): BenchmarkReportAudience[] {
  return REPORT_DOCUMENT_AUDIENCES.filter(audience => !documents.some(doc => doc.audience === audience));
}

/** The names of the written documents, which the write panel lists as not writable. */
export function writtenReportLabels(documents: readonly BenchmarkReportDocumentListItemDto[]): string[] {
  return REPORT_DOCUMENT_AUDIENCES.filter(audience => documents.some(doc => doc.audience === audience)).map(audienceLabel);
}

/** Under the document checkboxes when both are checked. */
export const REPORT_WRITE_ORDER_BOTH_HINT =
  'Written in this order: the Root Cause Analysis first, then the Executive Summary, which is written from it.';

/**
 * The write order of the checked documents, under their checkboxes: both checked, the Root Cause Analysis
 * first and the Executive Summary from it; '' otherwise, where the Executive Summary's own reason line
 * says what it is written from.
 */
export function reportWriteOrderHint(audiences: readonly BenchmarkReportAudience[]): string {
  return audiences.includes(BenchmarkReportAudience.ExecutiveSummary) && audiences.includes(BenchmarkReportAudience.RootCauseAnalysis)
    ? REPORT_WRITE_ORDER_BOTH_HINT
    : '';
}

/**
 * A stored Executive Summary's Root Cause Analysis: *Written from Root Cause Analysis #12*; when
 * `documents` (the subject's list) holds a later Root Cause Analysis of the same subject, that it was
 * written from an earlier one; when it holds neither that one nor a later one, that it no longer
 * exists. '' for another audience and for an Executive Summary that records none.
 */
export function reportRootCauseSourceLine(
  doc: BenchmarkReportDocumentListItemDto,
  documents: readonly BenchmarkReportDocumentListItemDto[]
): string {
  const sourceId = doc.rootCauseSourceDocumentId;
  if (doc.audience !== BenchmarkReportAudience.ExecutiveSummary || sourceId === null || sourceId === undefined) {
    return '';
  }
  const analyses = documents.filter(other => other.audience === BenchmarkReportAudience.RootCauseAnalysis
    && other.subjectKey === doc.subjectKey);
  // Document ids grow with every document stored, so a larger one is a later Root Cause Analysis.
  if (analyses.some(other => other.id > sourceId)) {
    return `Written from an earlier Root Cause Analysis (#${sourceId}); rewrite it to include the newest`;
  }
  return analyses.some(other => other.id === sourceId)
    ? `Written from Root Cause Analysis #${sourceId}`
    : `Written from Root Cause Analysis #${sourceId}, which no longer exists`;
}

/**
 * What the tab knows of the stored Root Cause Analysis's readiness for an Executive Summary written from
 * it: the request is out, it failed, or the server answered.
 */
export type ReportRootCauseReadiness =
  | { readonly kind: 'loading' }
  | { readonly kind: 'failed' }
  | { readonly kind: 'answered'; readonly readiness: BenchmarkRootCauseReadinessDto };

/** What the inline error's fix button checks: the Root Cause Analysis, or its rewrite. */
export type ExecutiveSummaryFix = 'checkRootCause' | 'checkRootCauseRewrite';

/** The Executive Summary checkbox of a write panel: whether it may be checked, and the reason line under it. */
export interface ExecutiveSummaryAvailability {
  /** The Executive Summary may be checked; otherwise its checkbox is `aria-disabled` and unchecked. */
  readonly available: boolean;
  /** The visible reason line under the checkbox, its `aria-describedby`. */
  readonly reason: string;
  /** What the inline error's fix button checks; null when the panel has no fix to offer. */
  readonly fix: ExecutiveSummaryFix | null;
  /** The readiness could not be read: the reason line offers Try again. */
  readonly retry: boolean;
}

/** What `executiveSummaryAvailability` decides from. */
export interface ExecutiveSummaryAvailabilityInput {
  /** The Root Cause Analysis is checked in the panel: written, or rewritten in place of the stored one. */
  readonly rootCauseChecked: boolean;
  /** The stored Root Cause Analysis's id; null when none is stored. */
  readonly storedRootCauseId: number | null;
  /** The stored analysis's readiness; null while it has not been asked. */
  readonly readiness: ReportRootCauseReadiness | null;
  /** The subject's noun: *run*, *battery run*. */
  readonly subjectNoun: string;
}

/** The inline error under an Executive Summary checked while it is blocked. */
export const EXECUTIVE_SUMMARY_BLOCKED_ERROR =
  'The Executive Summary is written from the Root Cause Analysis, so it cannot be written alone here.';

/** The polite status line after unchecking the Root Cause Analysis unchecked the Executive Summary. */
export const EXECUTIVE_SUMMARY_UNCHECKED_ANNOUNCEMENT =
  'The Executive Summary was unchecked: it is written from the Root Cause Analysis.';

/** The fix button's label for each fix. */
export function executiveSummaryFixLabel(fix: ExecutiveSummaryFix): string {
  return fix === 'checkRootCauseRewrite' ? 'Check Root Cause Analysis — rewrite' : 'Check the Root Cause Analysis';
}

/**
 * Whether the stored Root Cause Analysis's readiness offers the **Root Cause Analysis — rewrite**
 * checkbox: the server answered that it was written from other data. A subject the server could not
 * prepare answers `outdated` with a message and no code, which a rewrite would not cure.
 */
export function rootCauseRewriteOffered(readiness: ReportRootCauseReadiness | null): boolean {
  if (readiness === null || readiness.kind !== 'answered') {
    return false;
  }
  const answer = readiness.readiness;
  return answer.state === 'outdated' && (answer.code === 'rootCauseOutdated' || !answer.message);
}

/**
 * The Executive Summary checkbox's state in a run's or a battery run's write panel. An Executive Summary
 * is written only from a Root Cause Analysis: the one checked beside it, or the stored one when it was
 * written from the subject's current data.
 */
export function executiveSummaryAvailability(input: ExecutiveSummaryAvailabilityInput): ExecutiveSummaryAvailability {
  const blocked = (reason: string, fix: ExecutiveSummaryFix | null = null, retry = false): ExecutiveSummaryAvailability =>
    ({ available: false, reason, fix, retry });
  if (input.rootCauseChecked) {
    return { available: true, reason: 'Written from the Root Cause Analysis above, after it.', fix: null, retry: false };
  }
  if (input.storedRootCauseId === null) {
    return blocked('Needs the Root Cause Analysis: check Root Cause Analysis to write both.', 'checkRootCause');
  }
  const readiness = input.readiness;
  if (readiness === null || readiness.kind === 'loading') {
    return blocked('Checking the stored Root Cause Analysis…');
  }
  if (readiness.kind === 'failed') {
    return blocked('The stored Root Cause Analysis could not be checked.', null, true);
  }
  const answer = readiness.readiness;
  const id = answer.rootCauseDocumentId ?? input.storedRootCauseId;
  switch (answer.state) {
    case 'usable':
      return {
        available: true,
        reason: `Written from Root Cause Analysis #${id}, which was written from this ${input.subjectNoun}'s current data.`,
        fix: null,
        retry: false
      };
    case 'outdated':
      return rootCauseRewriteOffered(readiness)
        ? blocked(`Root Cause Analysis #${id} was written from different data than this ${input.subjectNoun} has now. `
          + 'Check Root Cause Analysis — rewrite to write both.', 'checkRootCauseRewrite')
        : blocked(answer.message ?? 'The stored Root Cause Analysis could not be checked.');
    default:
      // The list holds an analysis the server no longer finds.
      return blocked(answer.message?.trim() || 'Needs the Root Cause Analysis: check Root Cause Analysis to write both.');
  }
}

/** The job state as the status line words it, or '' when the document rows already say everything. */
export function reportJobStatusText(status: BenchmarkRunReportDocumentsStatus, message: string | null | undefined): string {
  const trimmed = message?.trim();
  switch (status) {
    case BenchmarkRunReportDocumentsStatus.Pending:
      return 'Waiting for the report writer';
    case BenchmarkRunReportDocumentsStatus.Writing:
      return 'Writing…';
    case BenchmarkRunReportDocumentsStatus.Failed:
      return trimmed ? `Failed: ${trimmed}` : 'Failed';
    case BenchmarkRunReportDocumentsStatus.Skipped:
      return trimmed ? `Skipped: ${trimmed}` : 'Skipped';
    case BenchmarkRunReportDocumentsStatus.Canceled:
      // The server's cancellation messages begin with the word already.
      return !trimmed ? 'Canceled' : /^cancel/i.test(trimmed) ? trimmed : `Canceled: ${trimmed}`;
    default:
      return '';
  }
}

export function reportJobStatusKind(status: BenchmarkRunReportDocumentsStatus): ReportDocumentsStatusKind {
  switch (status) {
    case BenchmarkRunReportDocumentsStatus.Failed: return 'failed';
    case BenchmarkRunReportDocumentsStatus.Skipped: return 'skipped';
    case BenchmarkRunReportDocumentsStatus.Pending:
    case BenchmarkRunReportDocumentsStatus.Writing: return 'progress';
    default: return 'plain';
  }
}

/** A stored document's tag: *Written*, *Written with warnings*, or the document status word. */
export function reportDocumentTag(doc: BenchmarkReportDocumentListItemDto): string {
  switch (doc.status) {
    case 'Completed': return 'Written';
    case 'CompletedWithWarnings': return 'Written with warnings';
    default: return reportDocumentStatusWord(doc.status);
  }
}

/** A server time as `yyyy-MM-dd HH:mm UTC`, or *an unknown date*. */
export function reportDateUtc(value: string | null | undefined): string {
  if (!value) return 'an unknown date';
  const date = parseServerUtcDate(value);
  return Number.isNaN(date.getTime()) ? value : `${formatDate(date, 'yyyy-MM-dd HH:mm', 'en-US', 'UTC')} UTC`;
}

/** The document's writer, else the subject's writer, else *the report writer*. */
export function reportDocumentWriter(doc: BenchmarkReportDocumentListItemDto, fallbackWriter: string | null | undefined): string {
  return doc.writerDisplayName || fallbackWriter || 'the report writer';
}

/** A written document's writer and date. */
export function reportDocumentByline(doc: BenchmarkReportDocumentListItemDto, fallbackWriter: string | null | undefined): string {
  return `by ${reportDocumentWriter(doc, fallbackWriter)} on ${reportDateUtc(doc.createdAtUtc)}`;
}

/** The writer and date, then the stored duration and cost where known. */
export function reportDocumentMeta(doc: BenchmarkReportDocumentListItemDto, fallbackWriter: string | null | undefined): string {
  const parts = [reportDocumentByline(doc, fallbackWriter)];
  if (doc.durationMs > 0) parts.push(formatElapsed(doc.durationMs));
  if (doc.costUsd !== null && doc.costUsd !== undefined) parts.push(formatCostUsd(doc.costUsd));
  if (doc.sameProviderAcknowledged) parts.push('same provider, acknowledged');
  return parts.join(' · ');
}

/** The cost estimate block under the write row, or null when there is nothing to say. */
export function reportEstimateView(
  loading: boolean,
  failed: boolean,
  estimate: BenchmarkRunReportEstimateDto | null
): ReportEstimateView | null {
  if (loading) return { state: 'loading', total: null, parts: [] };
  if (failed) return { state: 'failed', total: null, parts: [] };
  if (!estimate || estimate.refusal) return null;
  const total = estimate.estimatedTotalCostUsd;
  if (total === null || total === undefined) {
    return { state: 'noPrice', total: null, parts: [] };
  }
  const parts = estimate.estimates.length > 1
    ? estimate.estimates.map(e => ({ name: audienceLabel(e.audience), cost: formatCostUsd(e.estimatedCostUsd) }))
    : [];
  return { state: 'ready', total: formatCostUsd(total), parts };
}

/**
 * The disclosures a stored document opens at in the PDF viewer: every allowed one, lowest first,
 * with the highest as the initial version, and the lookup from a viewer variant key back to one.
 */
export function reportDocumentDisclosures(doc: BenchmarkReportDocumentListItemDto): {
  disclosures: BenchmarkReportDisclosure[];
  highest: BenchmarkReportDisclosure;
  disclosureOf: (variant: string | null) => BenchmarkReportDisclosure;
} {
  const allowed = [...new Set(doc.allowedDisclosures ?? [])].sort((a, b) => a - b);
  const disclosures = allowed.length > 0 ? allowed : [BenchmarkReportDisclosure.Full];
  const highest = disclosures[disclosures.length - 1];
  const byKey = new Map(disclosures.map(disclosure => [reportDisclosureParam(disclosure), disclosure] as const));
  const disclosureOf = (variant: string | null): BenchmarkReportDisclosure =>
    (variant !== null ? byKey.get(variant as ReturnType<typeof reportDisclosureParam>) : undefined) ?? highest;
  return { disclosures, highest, disclosureOf };
}

/** The server's own message from an error body: a plain string, or `{ error }`. */
export function reportServerErrorText(err: any): string | null {
  const body = err?.error;
  if (typeof body === 'string' && body.trim()) return body.trim();
  if (body && typeof body.error === 'string' && body.error.trim()) return body.error.trim();
  return null;
}

/** A 409 body that asks for the same-provider acknowledgment rather than refusing. */
export function isReportWriterSameProviderWarning(body: unknown): body is SameProviderWarningDto {
  if (!body || typeof body !== 'object') return false;
  const warning = body as Partial<SameProviderWarningDto>;
  return typeof warning.provider === 'string' && (warning.sameProvider === true || warning.role === 'reportWriter');
}
