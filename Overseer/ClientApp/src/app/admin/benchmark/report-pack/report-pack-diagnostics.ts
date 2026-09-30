import {
  BenchmarkReportAudience,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackJobDto
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
import { REPORT_CHART_FIGURES, ReportChartRowStatus, ReportChartSelection } from './report-charts';
import { audienceLabel, formatCostUsd, formatElapsed } from './report-document-format';

/** The clipboard and file side effects of the Reports panel, held in an object so a spec can observe them. */
export const reportPackIo = {
  copy: (text: string): Promise<boolean> => copyToClipboard(text),
  download: (fileName: string, text: string): void => downloadTextFile(fileName, text, 'text/plain;charset=utf-8')
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
  /** The estimate shown when the job was started here, or null. */
  readonly estimateUsd: number | null;
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

function figureTitles(selection: ReportChartSelection, audience: BenchmarkReportAudience): string {
  const keys = selection[audience] ?? [];
  if (keys.length === 0) {
    return 'none';
  }
  return keys.map(key => REPORT_CHART_FIGURES.find(figure => figure.key === key)?.title ?? key).join('; ');
}

/** A document's chart status in words. */
export function reportPackChartStatusWord(
  doc: BenchmarkReportPackDocumentProgressDto,
  input: Pick<ReportPackDiagnosticsInput, 'chartSelection' | 'chartStatus' | 'chartStorageMissing'>
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
    case 'done': return `${numberFormat.format(status.count)} attached`;
    case 'failed': return `failed: ${orNotRecorded(status.message)}`;
    default: return `none (${orNotRecorded(status.reason)})`;
  }
}

function documentLines(
  doc: BenchmarkReportPackDocumentProgressDto,
  index: number,
  serverNow: string | null | undefined,
  input: ReportPackDiagnosticsInput
): string[] {
  return [
    `Document ${index + 1}: ${audienceLabel(doc.audience)}`,
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
    `  Charts selected: ${figureTitles(input.chartSelection, doc.audience)}`,
    `  Charts: ${reportPackChartStatusWord(doc, input)}`
  ];
}

/** `report-pack_gemini-flash_diagnostics_20260929-120005.txt`, the time in UTC. */
export function reportPackDiagnosticsFileName(subjectLabel: string | null | undefined, nowUtc: Date): string {
  const iso = nowUtc.toISOString();
  const stamp = `${iso.slice(0, 4)}${iso.slice(5, 7)}${iso.slice(8, 10)}-${iso.slice(11, 13)}${iso.slice(14, 16)}${iso.slice(17, 19)}`;
  const subject = subjectLabel && subjectLabel.trim() ? safeFileName(subjectLabel) : 'no-subject';
  return `report-pack_${subject}_diagnostics_${stamp}.txt`;
}

/**
 * The plain-text diagnostics of the Reports panel: the comparison, the job, one block per document
 * with its chart status, the totals, the client's polling and the job log. One fact per line, LF
 * line endings, absent facts reading "not recorded". The user who started the job is never included.
 */
export function buildReportPackDiagnostics(input: ReportPackDiagnosticsInput): string {
  const { context, job, client } = input;
  const serverNow = job?.serverTimeUtc ?? null;
  const lines: string[] = [];

  lines.push('Overseer Report Pack diagnostics');
  lines.push(`Captured: ${time(input.nowUtc)}`);

  lines.push('', '== Comparison ==');
  lines.push(`Suite: ${orNotRecorded(context?.suiteName ?? job?.suiteName)}`);
  lines.push(`Suite id: ${orNotRecorded(context?.suiteId ?? job?.suiteId)}`);
  lines.push(`Entries: ${context ? `${count(context.entryKeys.length)} (${context.entryKeys.join(', ') || NOT_RECORDED})` : NOT_RECORDED}`);
  lines.push(`Runs: ${context && context.runIds.length > 0 ? context.runIds.join(', ') : NOT_RECORDED}`);
  lines.push(`Groups: ${context && context.groupIds.length > 0 ? context.groupIds.join(', ') : NOT_RECORDED}`);
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
  lines.push(`Started: ${time(job?.startedAtUtc)}`);
  lines.push(`Completed: ${time(job?.completedAtUtc)}`);
  lines.push(`Server time at the last response: ${time(serverNow)}`);
  lines.push(`Elapsed: ${job ? openSpan(job.startedAtUtc, job.completedAtUtc, serverNow) : NOT_RECORDED}`);

  lines.push('', '== Charts ==');
  lines.push(`Chart storage: ${input.chartStorageMissing ? 'not configured' : 'configured'}`);
  lines.push(`Advisory: ${orNotRecorded(input.chartAdvisory)}`);

  lines.push('', '== Documents ==');
  const documents = job?.documents ?? [];
  if (documents.length === 0) {
    lines.push(`Documents: ${NOT_RECORDED}`);
  } else {
    documents.forEach((doc, index) => lines.push(...documentLines(doc, index, serverNow, input)));
  }

  lines.push('', '== Totals ==');
  lines.push(`Model calls: ${count(job?.totalModelCalls)}`);
  lines.push(`Input tokens: ${count(job?.inputTokens)}`);
  lines.push(`Output tokens: ${count(job?.outputTokens)}`);
  lines.push(`Cost: ${cost(job?.costUsd)}`);
  if (input.estimateUsd !== null && Number.isFinite(input.estimateUsd)) {
    lines.push(`Estimate: ${formatCostUsd(input.estimateUsd)}`);
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
