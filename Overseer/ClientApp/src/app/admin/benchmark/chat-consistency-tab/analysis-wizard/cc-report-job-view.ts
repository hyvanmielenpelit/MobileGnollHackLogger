import {
  BenchmarkReportAudience,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkRunReportJobDto
} from '../../../../services/admin-benchmark.service';
import { audienceLabel, documentChipClass, formatCostUsd, formatElapsed, statusLabel } from '../../report-pack/report-document-format';
import {
  ClientPollState,
  NOT_RECORDED,
  formatUtcSeconds,
  runReportStatusWord,
  spanMs
} from '../../run-ai-reports/run-report-writing-diagnostics';
import { CC_REPORT_AUDIENCES } from '../chat-consistency.models';

/*
 * The Write step's view of a chat consistency report-writing job (`BenchmarkRunReportJobDto`): the
 * stage rail, the stat strip, one row per document, the one-line summary of a finished job, and the
 * plain-text diagnostics. Pure: every time is measured against the `nowUtc` the caller passes, which
 * is the server's clock run on since its last response.
 */

/** One stage of the job's rail: Queued, Preparing, one per document, Done. */
export interface CcReportJobStage {
  readonly key: string;
  readonly name: string;
  readonly state: 'done' | 'current' | 'pending';
}

/** The stat strip's values, each already in words. */
export interface CcReportJobStats {
  /** From the writer's slot (else the queueing) to the end, or to now while running; `—` when unknown. */
  readonly elapsed: string;
  readonly modelCalls: string;
  /** `42,000 in · 9,000 out`. */
  readonly tokens: string;
  /** Input and output tokens together. */
  readonly totalTokens: number;
  readonly cost: string;
  /** `Cost` once the job has finished, `Cost so far` before. */
  readonly costLabel: string;
}

/** One row of the document progress list. */
export interface CcReportJobRow {
  readonly key: string;
  readonly audience: BenchmarkReportAudience;
  readonly name: string;
  readonly status: string;
  readonly statusWord: string;
  readonly chipClass: string;
  readonly duration: string;
  readonly modelCalls: string;
  readonly errorMessage: string | null;
  readonly documentId: number | null;
}

/** What the diagnostics say beside the job: the analysis, the charts, the client's polling and the estimate. */
export interface CcReportDiagnosticsContext {
  readonly analysisId: number | null;
  readonly analysisName: string | null;
  readonly subject: {
    readonly displayName: string;
    readonly provider: string;
    readonly modelId: string;
    readonly thinkingLevel: string | null;
  } | null;
  /** `idle`, `attaching`, `done` or `failed`. */
  readonly chartState: string;
  readonly chartMessage: string;
  readonly client: ClientPollState;
  /** The estimate shown when the job was started here, or null. */
  readonly estimateUsd: number | null;
}

/** A document's statuses once it is written. */
const WRITTEN_DOCUMENT_STATUSES = new Set(['Completed', 'CompletedWithWarnings']);

/** A document's statuses while the writer works on it. */
const ACTIVE_DOCUMENT_STATUSES = new Set(['Writing', 'Repairing']);

const numberFormat = new Intl.NumberFormat('en-US');

function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}

/** A document type as the Write step names it (*Internal Brief*); the stored name for any other. */
export function ccReportAudienceLabel(audience: BenchmarkReportAudience): string {
  return CC_REPORT_AUDIENCES.find(entry => entry.audience === audience)?.label ?? audienceLabel(audience);
}

/** The job has finished: written, canceled or failed. */
export function ccReportJobFinished(job: BenchmarkRunReportJobDto): boolean {
  return job.phase === 'Finished';
}

function documentsOf(job: BenchmarkRunReportJobDto): BenchmarkReportPackDocumentProgressDto[] {
  return job.job?.documents ?? [];
}

/** Queued, Preparing, one stage per document, Done; the current one is where the job stands. */
export function ccReportJobStages(job: BenchmarkRunReportJobDto): CcReportJobStage[] {
  const documents = documentsOf(job);
  const stages = [
    { key: 'queued', name: 'Queued' },
    { key: 'preparing', name: 'Preparing' },
    ...documents.map(doc => ({ key: `doc-${doc.audience}`, name: ccReportAudienceLabel(doc.audience) })),
    { key: 'done', name: 'Done' }
  ];
  let current: number;
  switch (job.phase) {
    case 'Queued':
      current = 0;
      break;
    case 'Preparing':
      current = 1;
      break;
    case 'Finished':
      current = stages.length - 1;
      break;
    default: {
      if (documents.length === 0) {
        current = 1;
        break;
      }
      let index = documents.findIndex(doc => ACTIVE_DOCUMENT_STATUSES.has(doc.status));
      if (index < 0) {
        index = documents.findIndex(doc => doc.status === 'Pending');
      }
      if (index < 0) {
        index = documents.length - 1;
      }
      current = 2 + index;
    }
  }
  return stages.map((stage, i): CcReportJobStage => ({
    ...stage,
    state: i < current ? 'done' : i === current ? 'current' : 'pending'
  }));
}

/** Milliseconds from `start` to `end`, or to `nowUtc` while `end` is absent; null without a start. */
function spanToNow(start: string | null | undefined, end: string | null | undefined, nowUtc: Date): number | null {
  if (!start) {
    return null;
  }
  return spanMs(start, end ?? nowUtc.toISOString());
}

/** The elapsed time, model calls, tokens and cost of the job. */
export function ccReportJobStats(job: BenchmarkRunReportJobDto, nowUtc: Date): CcReportJobStats {
  const pack = job.job;
  const elapsedMs = spanToNow(job.slotAcquiredAtUtc ?? job.queuedAtUtc, job.finishedAtUtc, nowUtc);
  const input = pack?.inputTokens ?? 0;
  const output = pack?.outputTokens ?? 0;
  return {
    elapsed: elapsedMs === null ? '—' : formatElapsed(elapsedMs),
    modelCalls: numberFormat.format(pack?.totalModelCalls ?? 0),
    tokens: `${numberFormat.format(input)} in · ${numberFormat.format(output)} out`,
    totalTokens: input + output,
    cost: formatCostUsd(pack?.costUsd),
    costLabel: ccReportJobFinished(job) ? 'Cost' : 'Cost so far'
  };
}

/** One row per document: its chip, its duration (to now while it is written), its model calls and its error. */
export function ccReportJobRows(job: BenchmarkRunReportJobDto, nowUtc: Date): CcReportJobRow[] {
  const finished = ccReportJobFinished(job);
  const jobEnd = job.finishedAtUtc ?? job.job?.completedAtUtc ?? null;
  return documentsOf(job).map((doc): CcReportJobRow => {
    const duration = doc.startedAtUtc
      ? spanToNow(doc.startedAtUtc, doc.completedAtUtc ?? (finished ? jobEnd ?? nowUtc.toISOString() : null), nowUtc)
      : null;
    return {
      key: `doc-${doc.audience}`,
      audience: doc.audience,
      name: ccReportAudienceLabel(doc.audience),
      status: doc.status,
      statusWord: statusLabel(doc.status),
      chipClass: documentChipClass(doc.status),
      duration: duration === null ? '—' : formatElapsed(duration),
      modelCalls: numberFormat.format(doc.modelCalls),
      errorMessage: doc.errorMessage || null,
      documentId: doc.documentId
    };
  });
}

/** The finished job in one line: `3 documents written · $0.04 · 1 min 12 s`. */
export function ccReportJobSummary(job: BenchmarkRunReportJobDto, nowUtc: Date): string {
  const documents = documentsOf(job);
  const total = documents.length;
  const written = documents.filter(doc => WRITTEN_DOCUMENT_STATUSES.has(doc.status)).length;
  let head: string;
  if (total === 0) {
    const message = job.message?.trim();
    head = message ? `${runReportStatusWord(job.status)}: ${message}` : runReportStatusWord(job.status);
  } else {
    switch (job.job?.status) {
      case 'Completed':
        head = `${plural(written, 'document', 'documents')} written`;
        break;
      case 'CompletedWithErrors':
        head = `${written} of ${plural(total, 'document', 'documents')} written, with errors`;
        break;
      case 'Canceled':
        head = `Canceled: ${written} of ${plural(total, 'document', 'documents')} written`;
        break;
      default:
        head = `Failed: ${written} of ${plural(total, 'document', 'documents')} written`;
    }
  }
  const stats = ccReportJobStats(job, nowUtc);
  const parts = [head];
  if (job.job?.costUsd !== null && job.job?.costUsd !== undefined) {
    parts.push(stats.cost);
  }
  if (stats.elapsed !== '—') {
    parts.push(stats.elapsed);
  }
  return parts.join(' · ');
}

// --- Diagnostics ---

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

/** A span that ends at `end`, or runs on to `serverNow` with "so far" while `end` is absent. */
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

/** The chart state in words, for a document that was written; the others have no charts to attach. */
function chartWord(doc: BenchmarkReportPackDocumentProgressDto, context: CcReportDiagnosticsContext): string {
  if (doc.documentId === null) {
    return 'none (not written)';
  }
  switch (context.chartState) {
    case 'attaching': return 'attaching';
    case 'done': return 'attached';
    case 'failed': return `failed: ${orNotRecorded(context.chartMessage)}`;
    default: return NOT_RECORDED;
  }
}

/** `chat-consistency-reports_7_diagnostics_20261002-100130.txt`, the time in UTC. */
export function ccReportDiagnosticsFileName(analysisId: number | null, nowUtc: Date): string {
  const iso = nowUtc.toISOString();
  const stamp = `${iso.slice(0, 4)}${iso.slice(5, 7)}${iso.slice(8, 10)}-${iso.slice(11, 13)}${iso.slice(14, 16)}${iso.slice(17, 19)}`;
  return `chat-consistency-reports_${analysisId ?? 'unsaved'}_diagnostics_${stamp}.txt`;
}

/**
 * The plain-text diagnostics of an analysis's report-writing job: the analysis, the job's status,
 * phase, writer, timing and queue, one block per document with its chart state, the totals, the
 * client's polling and the job log. One fact per line, LF line endings, absent facts reading "not
 * recorded". The user who started the job is never included.
 */
export function ccReportJobDiagnostics(
  view: BenchmarkRunReportJobDto | null,
  nowUtc: Date,
  context: CcReportDiagnosticsContext
): string {
  const job = view?.job ?? null;
  const serverNow = view?.serverTimeUtc ?? job?.serverTimeUtc ?? null;
  const lines: string[] = [];

  lines.push('Overseer chat consistency report writing diagnostics');
  lines.push(`Captured: ${time(nowUtc)}`);

  lines.push('', '== Analysis ==');
  lines.push(`Analysis: ${context.analysisId === null ? NOT_RECORDED : `#${context.analysisId}`}`);
  lines.push(`Name: ${orNotRecorded(context.analysisName)}`);
  lines.push(`Model: ${orNotRecorded(context.subject?.displayName)}`);
  lines.push(`Provider / model: ${context.subject
    ? `${orNotRecorded(context.subject.provider)} / ${orNotRecorded(context.subject.modelId)}`
    : NOT_RECORDED}`);
  lines.push(`Thinking level: ${orNotRecorded(context.subject?.thinkingLevel)}`);

  lines.push('', '== Report status ==');
  lines.push(`Status: ${view ? runReportStatusWord(view.status) : NOT_RECORDED}`);
  lines.push(`Message: ${orNotRecorded(view?.message)}`);

  lines.push('', '== Job ==');
  lines.push(`Job id: ${orNotRecorded(job?.id)}`);
  lines.push(`Pack id: ${orNotRecorded(job?.packId)}`);
  lines.push(`Phase: ${orNotRecorded(view?.phase)}`);
  lines.push(`Job status: ${orNotRecorded(job?.status)}`);
  const requested = (view?.audiences ?? []).map(audience => ccReportAudienceLabel(audience));
  lines.push(`Requested documents: ${requested.length > 0 ? requested.join('; ') : NOT_RECORDED}`);

  lines.push('', '== Writer ==');
  lines.push(`Writer: ${orNotRecorded(view?.writerDisplayName)}`);
  lines.push(`Provider / model: ${view && (view.writerProvider || view.writerModelId)
    ? `${orNotRecorded(view.writerProvider)} / ${orNotRecorded(view.writerModelId)}`
    : NOT_RECORDED}`);
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

  lines.push('', '== Charts ==');
  lines.push(`State: ${orNotRecorded(context.chartState)}`);
  lines.push(`Message: ${orNotRecorded(context.chartMessage)}`);

  lines.push('', '== Documents ==');
  const documents = job?.documents ?? [];
  if (documents.length === 0) {
    lines.push(`Documents: ${NOT_RECORDED}`);
  } else {
    documents.forEach((doc, index) => lines.push(
      `Document ${index + 1}: ${ccReportAudienceLabel(doc.audience)}`,
      `  Status: ${orNotRecorded(doc.status)}`,
      `  Started: ${time(doc.startedAtUtc)}`,
      `  Completed: ${time(doc.completedAtUtc)}`,
      `  Duration: ${openSpan(doc.startedAtUtc, doc.completedAtUtc, serverNow)}`,
      `  Model calls: ${count(doc.modelCalls)}`,
      `  Input tokens: ${count(doc.inputTokens)}`,
      `  Output tokens: ${count(doc.outputTokens)}`,
      `  Cost: ${cost(doc.costUsd)}`,
      `  Document id: ${orNotRecorded(doc.documentId)}`,
      `  Error: ${orNotRecorded(doc.errorMessage)}`,
      `  Charts: ${chartWord(doc, context)}`
    ));
  }

  lines.push('', '== Totals ==');
  lines.push(`Model calls: ${count(job?.totalModelCalls)}`);
  lines.push(`Input tokens: ${count(job?.inputTokens)}`);
  lines.push(`Output tokens: ${count(job?.outputTokens)}`);
  lines.push(`Cost: ${cost(job?.costUsd)}`);
  if (context.estimateUsd !== null && Number.isFinite(context.estimateUsd)) {
    lines.push(`Estimate: ${formatCostUsd(context.estimateUsd)}`);
  }

  lines.push('', '== Client polling ==');
  lines.push(`Polls: ${count(context.client.pollCount)}`);
  lines.push(`Last success: ${time(context.client.lastSuccessUtc)}`);
  lines.push(`Consecutive failures: ${count(context.client.consecutiveFailures)}`);
  const lastError = context.client.lastError;
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
