import { formatDate } from '@angular/common';

import {
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportDocumentOrigin,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportEstimateDto,
  BenchmarkRunReportJobDto,
  SameProviderWarningDto,
  reportDisclosureParam
} from '../../../services/admin-benchmark.service';
import { parseServerUtcDate } from '../../../utils/date.util';
import {
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

/** The three AI-written completion documents of a run or a battery run, in the order the panels list them. */
export const REPORT_DOCUMENT_AUDIENCES: readonly BenchmarkReportAudience[] = [
  BenchmarkReportAudience.ExecutiveSummary,
  BenchmarkReportAudience.TechnicalReport,
  BenchmarkReportAudience.InternalBrief
];

/** A battery-completion document's origin. */
export const BATTERY_COMPLETION_ORIGIN = BenchmarkReportDocumentOrigin.BatteryCompletion;

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
 * The completion documents of one subject (`run:<id>` or `battery:<id>`) of the given origin, the
 * newest one per audience, in audience order. Report Pack documents about the same subject are left
 * out.
 */
export function completionDocumentsOf(
  documents: readonly BenchmarkReportDocumentListItemDto[] | null | undefined,
  subjectKey: string,
  origin: number
): BenchmarkReportDocumentListItemDto[] {
  const newestFirst = (documents ?? [])
    .filter(doc => doc.origin === origin && doc.subjectKey === subjectKey)
    .sort((a, b) => (b.createdAtUtc ?? '').localeCompare(a.createdAtUtc ?? '') || b.id - a.id);
  return REPORT_DOCUMENT_AUDIENCES
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
