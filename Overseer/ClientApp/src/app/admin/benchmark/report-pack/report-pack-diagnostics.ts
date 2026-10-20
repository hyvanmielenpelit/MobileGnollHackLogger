import {
  BenchmarkReportAudience,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackJobDto,
  BenchmarkReportScope
} from '../../../services/admin-benchmark.service';
import { copyToClipboard } from '../../../utils/clipboard.util';
import { downloadTextFile, safeFileName } from '../../../utils/download.util';
import type { ClientPollState } from '../run-ai-reports/run-report-writing-diagnostics';
import {
  NOT_RECORDED,
  documentLines as jobDocumentLines,
  failedReplyLines,
  formatUtcSeconds,
  preparationLines,
  spanMs,
  validationLines,
  versionLines,
  whyLines,
  wordCountLines
} from './report-job-diagnostics-sections';
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
  formatCostRangeUsd,
  formatCostUsd,
  formatElapsed,
  isCostRange
} from './report-document-format';

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

function documentLines(
  doc: BenchmarkReportPackDocumentProgressDto,
  index: number,
  serverNow: string | null | undefined,
  input: ReportPackDiagnosticsInput
): string[] {
  const lines = jobDocumentLines(doc, index, serverNow, input.job?.writerMaxOutputTokens);
  const scope = chartScopeOf(input.job);
  const figures = selectedFigures(input.chartSelection, doc.audience, scope);
  lines.push(`  Charts selected: ${figures.drawn.length === 0 ? 'none' : figures.drawn.map(figureTitle).join('; ')}`);
  if (figures.notDrawn.length > 0) {
    lines.push(`  Selected but not drawn in this document type: ${figures.notDrawn.map(figureTitle).join('; ')}`);
  }
  lines.push(`  Charts: ${reportPackChartStatusWord(doc, input, scope)}`);
  return lines;
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
  lines.push(...versionLines(job));
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
