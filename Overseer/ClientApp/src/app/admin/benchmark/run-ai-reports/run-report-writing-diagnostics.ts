import {
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportJobDto
} from '../../../services/admin-benchmark.service';
import { parseServerUtcDate } from '../../../utils/date.util';
import { audienceLabel, formatCostUsd, formatElapsed } from '../report-pack/report-document-format';

/** The value every absent or null fact reads as. */
export const NOT_RECORDED = 'not recorded';

/** The run a report writing job belongs to, as far as the client knows it. */
export interface RunIdentity {
  runId: number;
  suiteName?: string | null;
  candidateLabel?: string | null;
  provider?: string | null;
  modelId?: string | null;
  /** The run's persisted report status, read when no job view is known. */
  reportDocumentsStatus?: BenchmarkRunReportDocumentsStatus | null;
  /** The run's persisted report message, read when no job view is known. */
  reportDocumentsMessage?: string | null;
}

/** The last failed poll: the HTTP status (null for a network failure) and the message. */
export interface ClientPollError {
  httpStatus: number | null;
  message: string;
}

/** How the client's polling of the job has gone. */
export interface ClientPollState {
  pollCount: number;
  lastSuccessUtc: Date | null;
  consecutiveFailures: number;
  lastError: ClientPollError | null;
}

const numberFormat = new Intl.NumberFormat('en-US');

/** A run report status as a word: `CompletedWithWarnings` → `Completed with warnings`. */
export function runReportStatusWord(status: BenchmarkRunReportDocumentsStatus | null | undefined): string {
  switch (status) {
    case BenchmarkRunReportDocumentsStatus.NotRequested: return 'Not requested';
    case BenchmarkRunReportDocumentsStatus.Pending: return 'Pending';
    case BenchmarkRunReportDocumentsStatus.Writing: return 'Writing';
    case BenchmarkRunReportDocumentsStatus.Completed: return 'Completed';
    case BenchmarkRunReportDocumentsStatus.CompletedWithWarnings: return 'Completed with warnings';
    case BenchmarkRunReportDocumentsStatus.Failed: return 'Failed';
    case BenchmarkRunReportDocumentsStatus.Skipped: return 'Skipped';
    case BenchmarkRunReportDocumentsStatus.Canceled: return 'Canceled';
    default: return status === null || status === undefined ? NOT_RECORDED : `Unknown (${status})`;
  }
}

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

/** `run-42_ai-report-writing-diagnostics_20260929-120005.txt`, the time in UTC. */
export function runReportWritingDiagnosticsFileName(runId: number, nowUtc: Date): string {
  const iso = nowUtc.toISOString();
  const stamp = `${iso.slice(0, 4)}${iso.slice(5, 7)}${iso.slice(8, 10)}-${iso.slice(11, 13)}${iso.slice(14, 16)}${iso.slice(17, 19)}`;
  return `run-${runId}_ai-report-writing-diagnostics_${stamp}.txt`;
}

function orNotRecorded(value: string | number | null | undefined): string {
  if (value === null || value === undefined) {
    return NOT_RECORDED;
  }
  const text = typeof value === 'number' ? String(value) : oneLine(value);
  return text === '' ? NOT_RECORDED : text;
}

function oneLine(text: string): string {
  return text.replace(/\r\n|\r|\n/g, ' ').trim();
}

function count(value: number | null | undefined): string {
  return value === null || value === undefined || !Number.isFinite(value) ? NOT_RECORDED : numberFormat.format(value);
}

function time(value: string | Date | null | undefined): string {
  return formatUtcSeconds(value) ?? NOT_RECORDED;
}

function span(ms: number | null): string {
  return ms === null ? NOT_RECORDED : formatElapsed(ms);
}

/** A span that ends at `end`, or runs on to the server's clock with "so far" while `end` is absent. */
function openSpan(start: string | null | undefined, end: string | null | undefined, serverNow: string | null | undefined): string {
  if (!start) {
    return NOT_RECORDED;
  }
  if (end) {
    return span(spanMs(start, end));
  }
  const soFar = spanMs(start, serverNow);
  return soFar === null ? NOT_RECORDED : `${formatElapsed(soFar)} so far`;
}

function providerAndModel(provider: string | null | undefined, modelId: string | null | undefined): string {
  if (!provider && !modelId) {
    return NOT_RECORDED;
  }
  return `${orNotRecorded(provider)} / ${orNotRecorded(modelId)}`;
}

function documentLines(doc: BenchmarkReportPackDocumentProgressDto, index: number, serverNow: string | null | undefined): string[] {
  return [
    `Document ${index + 1}: ${audienceLabel(doc.audience)}`,
    `  Status: ${orNotRecorded(doc.status)}`,
    `  Started: ${time(doc.startedAtUtc)}`,
    `  Completed: ${time(doc.completedAtUtc)}`,
    `  Duration: ${openSpan(doc.startedAtUtc, doc.completedAtUtc, serverNow)}`,
    `  Model calls: ${count(doc.modelCalls)}`,
    `  Input tokens: ${count(doc.inputTokens)}`,
    `  Output tokens: ${count(doc.outputTokens)}`,
    `  Cost: ${doc.costUsd === undefined || doc.costUsd === null ? NOT_RECORDED : formatCostUsd(doc.costUsd)}`,
    `  Document id: ${orNotRecorded(doc.documentId)}`,
    `  Error: ${orNotRecorded(doc.errorMessage)}`
  ];
}

/**
 * The plain-text diagnostics of a run's AI report writing job: one fact per line, LF line endings,
 * absent facts reading "not recorded". The user who started the job is never included.
 */
export function buildRunReportWritingDiagnostics(
  view: BenchmarkRunReportJobDto | null,
  run: RunIdentity,
  client: ClientPollState,
  nowUtc: Date,
  estimateUsd?: number | null
): string {
  const job = view?.job ?? null;
  const serverNow = view?.serverTimeUtc ?? null;
  const lines: string[] = [];

  lines.push('Overseer AI report writing diagnostics');
  lines.push(`Captured: ${time(nowUtc)}`);

  lines.push('', '== Run ==');
  lines.push(`Run: #${run.runId}`);
  lines.push(`Suite: ${orNotRecorded(run.suiteName)}`);
  lines.push(`Candidate: ${orNotRecorded(run.candidateLabel)}`);
  lines.push(`Provider / model: ${providerAndModel(run.provider, run.modelId)}`);

  lines.push('', '== Report status ==');
  const status = view ? view.status : run.reportDocumentsStatus;
  const message = view ? view.message : run.reportDocumentsMessage;
  lines.push(`Status: ${runReportStatusWord(status)}`);
  lines.push(`Message: ${orNotRecorded(message)}`);
  lines.push(`Source: ${view ? 'the job known to this server process' : 'the stored run (no job known to this server process)'}`);

  lines.push('', '== Job ==');
  lines.push(`Job id: ${orNotRecorded(job?.id)}`);
  lines.push(`Pack id: ${orNotRecorded(job?.packId)}`);
  lines.push(`Phase: ${orNotRecorded(view?.phase)}`);
  lines.push(`Job status: ${orNotRecorded(job?.status)}`);
  const requested = (view?.audiences ?? []).map(audience => audienceLabel(audience));
  lines.push(`Requested documents: ${requested.length > 0 ? requested.join('; ') : NOT_RECORDED}`);

  lines.push('', '== Writer ==');
  lines.push(`Writer: ${orNotRecorded(view?.writerDisplayName)}`);
  lines.push(`Provider / model: ${providerAndModel(view?.writerProvider, view?.writerModelId)}`);
  lines.push(`Thinking level: ${orNotRecorded(view?.writerThinkingLevel)}`);
  lines.push(`Config id: ${orNotRecorded(view?.writerConfigId)}`);

  lines.push('', '== Timing ==');
  lines.push(`Queued: ${time(view?.queuedAtUtc)}`);
  lines.push(`Slot acquired: ${time(view?.slotAcquiredAtUtc)}`);
  lines.push(`Finished: ${time(view?.finishedAtUtc)}`);
  lines.push(`Cancel requested: ${time(view?.cancelRequestedAtUtc)}`);
  lines.push(`Server time at the last response: ${time(serverNow)}`);
  const queueEnd = view?.slotAcquiredAtUtc ?? view?.finishedAtUtc ?? null;
  lines.push(`Queue wait: ${view ? openSpan(view.queuedAtUtc, queueEnd, serverNow) : NOT_RECORDED}`);
  lines.push(`Writing time: ${view?.slotAcquiredAtUtc ? openSpan(view.slotAcquiredAtUtc, view.finishedAtUtc, serverNow) : NOT_RECORDED}`);
  lines.push(`Total: ${view ? openSpan(view.queuedAtUtc, view.finishedAtUtc, serverNow) : NOT_RECORDED}`);

  lines.push('', '== Queue ==');
  lines.push(`Jobs ahead: ${count(view?.jobsAhead)}`);
  lines.push(`Blocking job: ${orNotRecorded(view?.blockingJobLabel)}`);

  lines.push('', '== Documents ==');
  const documents = job?.documents ?? [];
  if (documents.length === 0) {
    lines.push(`Documents: ${NOT_RECORDED}`);
  } else {
    documents.forEach((doc, index) => lines.push(...documentLines(doc, index, serverNow)));
  }

  lines.push('', '== Totals ==');
  lines.push(`Model calls: ${count(job?.totalModelCalls)}`);
  lines.push(`Input tokens: ${count(job?.inputTokens)}`);
  lines.push(`Output tokens: ${count(job?.outputTokens)}`);
  lines.push(`Cost: ${job && job.costUsd !== null && job.costUsd !== undefined ? formatCostUsd(job.costUsd) : NOT_RECORDED}`);
  if (estimateUsd !== null && estimateUsd !== undefined && Number.isFinite(estimateUsd)) {
    lines.push(`Estimate: ${formatCostUsd(estimateUsd)}`);
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
