import {
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportScope
} from '../../../services/admin-benchmark.service';
import { parseServerUtcDate } from '../../../utils/date.util';
import { safeFileName } from '../../../utils/download.util';
import { firstDraftPassed, revisionCount, shorteningCount, timesWord } from './report-validation-explanation';

/** Which documents a document list holds: those of one comparison, or every Report Pack document. */
export type ReportDocumentLibraryScope =
  | {
      readonly kind: 'comparison';
      /** The numbered comparison (*Comparison #12*) its documents are listed by; absent or null before it is numbered. */
      readonly comparisonId?: number | null;
      /** The comparison's display name, for the list's heading; absent or null when unknown. */
      readonly name?: string | null;
      /** The comparison's entry keys, which list the documents written before comparisons were numbered. */
      readonly entryKeys: readonly string[];
    }
  | { readonly kind: 'all' };

/** What a Report Pack document covers: every model of its comparison, a subset of them, or one model. */
export type ReportDocumentScopeValue = 'comparison' | 'subset' | 'model';

/** The scopes in the order the Scope facet lists them, with their labels. */
export const REPORT_DOCUMENT_SCOPES: readonly { readonly value: ReportDocumentScopeValue; readonly label: string }[] = [
  { value: 'comparison', label: 'Whole comparison' },
  { value: 'subset', label: 'Model subset' },
  { value: 'model', label: 'One model' }
];

/** A document's scope: a comparison-scope document covers the whole comparison or a subset; any other covers one model. */
export function reportDocumentScope(doc: Pick<BenchmarkReportDocumentListItemDto, 'scope' | 'coversAllEntries'>): ReportDocumentScopeValue {
  if (doc.scope === BenchmarkReportScope.Comparison) {
    return doc.coversAllEntries ? 'comparison' : 'subset';
  }
  return 'model';
}

/** `Whole comparison`, `Model subset` or `One model`; the value itself for an unknown one. */
export function reportScopeLabel(value: string): string {
  return REPORT_DOCUMENT_SCOPES.find(scope => scope.value === value)?.label ?? value;
}

/**
 * A Report Pack document's kind in a file name, as the server's `BenchmarkPdfFileNames.KindSlug` spells
 * it: `executive-summary` or `root-cause-analysis`; `provider-issue-report` for the Provider Issue Report.
 */
export function reportKindSlug(audience: BenchmarkReportAudience): string {
  switch (audience) {
    case BenchmarkReportAudience.ExecutiveSummary: return 'executive-summary';
    case BenchmarkReportAudience.RootCauseAnalysis: return 'root-cause-analysis';
    case BenchmarkReportAudience.ProviderIssueReport: return 'provider-issue-report';
    default: return safeFileName(BenchmarkReportAudience[audience] ?? String(audience));
  }
}

/** The subject key prefix of a chat consistency document: `chat-consistency:12`. */
export const CHAT_CONSISTENCY_SUBJECT_PREFIX = 'chat-consistency:';

/** The subject key of a saved chat consistency analysis's documents. */
export function chatConsistencySubjectKey(analysisId: number): string {
  return `${CHAT_CONSISTENCY_SUBJECT_PREFIX}${analysisId}`;
}

/** The analysis id of a `chat-consistency:<id>` subject key; null for any other key. */
export function chatConsistencyAnalysisIdOf(subjectKey: string | null | undefined): number | null {
  const match = /^chat-consistency:([0-9]+)$/.exec(subjectKey ?? '');
  if (!match) {
    return null;
  }
  const id = Number(match[1]);
  return Number.isSafeInteger(id) && id > 0 ? id : null;
}

/** How many documents the `all` scope asks for: the list endpoint's maximum. */
export const REPORT_LIBRARY_ALL_TAKE = 500;

/** One document the Reports form offers. */
export interface ReportPackAudienceOption {
  readonly audience: BenchmarkReportAudience;
  readonly label: string;
  /** The label of a segment where the full label does not fit; a word of `label`. */
  readonly shortLabel: string;
  readonly description: string;
  readonly checkedByDefault: boolean;
}

export const EXECUTIVE_SUMMARY_OPTION: ReportPackAudienceOption = {
  audience: BenchmarkReportAudience.ExecutiveSummary,
  label: 'Executive Summary',
  shortLabel: 'Executive',
  description: 'For a non-specialist at the model’s provider, or a manager: plain language, short.',
  checkedByDefault: true
};

/** Why the models scored, ran and cost as they did; every scope but chat consistency. */
export const ROOT_CAUSE_ANALYSIS_OPTION: ReportPackAudienceOption = {
  audience: BenchmarkReportAudience.RootCauseAnalysis,
  label: 'Root Cause Analysis',
  shortLabel: 'Root Cause',
  description: 'For the Overseer team: why the Intelligence Index, speed and cost came out as they did, with hypotheses graded by evidence and the runs that would settle them. Internal only, at Full disclosure.',
  checkedByDefault: true
};

/** The document a chat consistency analysis writes for the model's provider; chat consistency only. */
export const PROVIDER_ISSUE_REPORT_OPTION: ReportPackAudienceOption = {
  audience: BenchmarkReportAudience.ProviderIssueReport,
  label: 'Provider Issue Report',
  shortLabel: 'Provider',
  description: 'For the model’s provider: a chat consistency finding, its evidence, and the request to the provider.',
  checkedByDefault: false
};

/**
 * Where documents are written: a comparison-scope or per-model Report Pack, a run's or a battery run's
 * AI reports tab, or a saved chat consistency analysis.
 */
export type ReportAudienceScope = 'comparison' | 'model' | 'run' | 'battery' | 'chatConsistency';

/** The documents a comparison-scope Report Pack writes. */
export const COMPARISON_REPORT_AUDIENCE_OPTIONS: readonly ReportPackAudienceOption[] = [EXECUTIVE_SUMMARY_OPTION, ROOT_CAUSE_ANALYSIS_OPTION];

/** The documents a per-model Report Pack writes. */
export const MODEL_REPORT_AUDIENCE_OPTIONS: readonly ReportPackAudienceOption[] = [EXECUTIVE_SUMMARY_OPTION, ROOT_CAUSE_ANALYSIS_OPTION];

/** The documents a run's or a battery run's Report Writer writes. */
export const RUN_REPORT_AUDIENCE_OPTIONS: readonly ReportPackAudienceOption[] = [EXECUTIVE_SUMMARY_OPTION, ROOT_CAUSE_ANALYSIS_OPTION];

/** The documents a saved chat consistency analysis writes. */
export const CHAT_CONSISTENCY_REPORT_AUDIENCE_OPTIONS: readonly ReportPackAudienceOption[] = [EXECUTIVE_SUMMARY_OPTION, PROVIDER_ISSUE_REPORT_OPTION];

/** The documents a scope offers, in audience order. */
export function reportAudiencesFor(scope: ReportAudienceScope): readonly ReportPackAudienceOption[] {
  switch (scope) {
    case 'comparison': return COMPARISON_REPORT_AUDIENCE_OPTIONS;
    case 'model': return MODEL_REPORT_AUDIENCE_OPTIONS;
    case 'chatConsistency': return CHAT_CONSISTENCY_REPORT_AUDIENCE_OPTIONS;
    default: return RUN_REPORT_AUDIENCE_OPTIONS;
  }
}

/** The audience scope of a Report Pack scope: comparison-scope or per-model. */
export function reportPackAudienceScope(scope: BenchmarkReportScope | null | undefined): ReportAudienceScope {
  return scope === BenchmarkReportScope.Comparison ? 'comparison' : 'model';
}

/** Every type a stored document can be, in list order: the Executive Summary, the Root Cause Analysis, then the Provider Issue Report. */
export const REPORT_DOCUMENT_AUDIENCE_OPTIONS: readonly ReportPackAudienceOption[] = [
  EXECUTIVE_SUMMARY_OPTION,
  ROOT_CAUSE_ANALYSIS_OPTION,
  PROVIDER_ISSUE_REPORT_OPTION
];

export function audienceLabel(audience: BenchmarkReportAudience): string {
  return REPORT_DOCUMENT_AUDIENCE_OPTIONS.find(option => option.audience === audience)?.label ?? 'Report document';
}

export function audienceShortLabel(audience: BenchmarkReportAudience): string {
  return REPORT_DOCUMENT_AUDIENCE_OPTIONS.find(option => option.audience === audience)?.shortLabel ?? 'Document';
}

export function disclosureLabel(disclosure: BenchmarkReportDisclosure): string {
  switch (disclosure) {
    case BenchmarkReportDisclosure.Detailed: return 'Detailed';
    case BenchmarkReportDisclosure.Full: return 'Full';
    default: return 'Summary';
  }
}

/** `CompletedWithWarnings` → `Completed with warnings`. */
export function statusLabel(status: string | null | undefined): string {
  if (!status) {
    return '';
  }
  const words = status.replace(/([a-z])([A-Z])/g, '$1 $2').split(' ');
  return words.map((word, i) => (i === 0 ? word : word.toLowerCase())).join(' ');
}

/**
 * A Report Pack job's document status on its progress row: `Writing` reads *Writing draft* and
 * `Repairing` *Revising*, the revision being a routine step rather than a fault; the rest as `statusLabel`.
 */
export function documentProgressLabel(status: string | null | undefined): string {
  switch (status) {
    case 'Writing': return 'Writing draft';
    case 'Repairing': return 'Revising';
    default: return statusLabel(status);
  }
}

/** The steps of a document's progress row in words; the server's wire value for an unknown one, empty while it waits. */
export function reportPhaseLabel(phase: string | null | undefined): string {
  switch (phase) {
    case 'Preparing': return 'Preparing';
    case 'Writing': return 'Writing draft';
    case 'Checking': return 'Checking the draft';
    case 'Revising': return 'Revising the draft';
    case 'CheckingRevision': return 'Checking the revision';
    case 'Storing': return 'Storing';
    case 'DrawingCharts': return 'Drawing charts';
    case 'Done': return 'Done';
    default: return phase ?? '';
  }
}

/**
 * A writer call's kind in words: *Draft*, *Revision*, *Shortening turn* (the final turn that shortens
 * sections over their word caps), *Headline retry*; the wire value for an unknown one.
 */
export function reportCallKindLabel(kind: string | null | undefined): string {
  switch (kind) {
    case 'Draft': return 'Draft';
    case 'Revision': return 'Revision';
    case 'Shorten': return 'Shortening turn';
    case 'Headline': return 'Headline retry';
    default: return kind ?? '';
  }
}

/** A document's statuses once it is written or given up. */
const FINISHED_DOCUMENT_STATUSES = new Set(['Completed', 'CompletedWithWarnings', 'Failed', 'Canceled']);

function issueCount(count: number): string {
  return `${count} ${count === 1 ? 'issue' : 'issues'}`;
}

/**
 * A document's phase line: while it is written, its step (*Revising the draft — 20 issues found by the
 * automatic check*); once finished, how its draft was checked (*First draft passed every check*,
 * *Revised once — 20 issues in the first draft*, *Revised once and shortened — …* after a shortening
 * turn). Null when there is nothing to say.
 */
export function documentPhaseLine(doc: BenchmarkReportPackDocumentProgressDto): string | null {
  const issues = doc.firstTurnIssues?.length ?? 0;
  if (FINISHED_DOCUMENT_STATUSES.has(doc.status)) {
    const passed = firstDraftPassed(doc);
    if (passed === true) {
      return 'First draft passed every check';
    }
    if (passed === false) {
      const revisions = revisionCount(doc) ?? 0;
      const shortened = shorteningCount(doc) > 0;
      const head = revisions > 0
        ? `Revised ${timesWord(revisions)}${shortened ? ' and shortened' : ''}`
        : shortened ? 'Shortened' : 'Not revised';
      return issues > 0 ? `${head} — ${issueCount(issues)} in the first draft` : head;
    }
    return null;
  }
  const phase = reportPhaseLabel(doc.phase);
  if (!phase || doc.phase === 'Done') {
    return null;
  }
  if ((doc.phase === 'Revising' || doc.phase === 'CheckingRevision') && issues > 0) {
    return `${phase} — ${issueCount(issues)} found by the automatic check`;
  }
  return phase;
}

/** A progress card's phase line: as documentPhaseLine, except none while it only repeats the chip. */
export function documentRowPhaseLine(doc: BenchmarkReportPackDocumentProgressDto): string | null {
  if ((doc.status === 'Writing' && doc.phase === 'Writing') || (doc.status === 'Repairing' && doc.phase === 'Revising')) {
    return null;
  }
  return documentPhaseLine(doc);
}

/** A stored document's status as the AI Reports tab words it: *Written*, *Written with warnings*. */
export function documentStatusLabel(status: string | null | undefined): string {
  switch (status) {
    case 'Completed': return 'Written';
    case 'CompletedWithWarnings': return 'Written with warnings';
    default: return statusLabel(status);
  }
}

/** The chip modifier of a document status; the word is always shown beside the color. */
export function documentChipClass(status: string): string {
  switch (status) {
    case 'Writing': return 'job-status-chip status-generating';
    case 'Repairing': return 'job-status-chip status-repairing';
    case 'Completed': return 'job-status-chip status-completed';
    case 'CompletedWithWarnings': return 'job-status-chip status-partial';
    case 'Failed': return 'job-status-chip status-failed';
    case 'Canceled': return 'job-status-chip status-canceled';
    default: return 'job-status-chip status-pending';
  }
}

export function formatCostUsd(cost: number | null | undefined): string {
  if (cost === null || cost === undefined || !Number.isFinite(cost)) {
    return 'Unknown';
  }
  if (cost === 0) {
    return '$0.00';
  }
  return cost < 0.01 ? `$${cost.toFixed(4)}` : `$${cost.toFixed(2)}`;
}

/** An estimate from the first calls alone (`low`) to one repair turn per document (`high`); `high` is null when unknown. */
export interface ReportCostRange {
  readonly low: number;
  readonly high: number | null;
}

/** `$1.04 to $2.10` when both ends are known and print differently; otherwise the first-call cost alone. */
export function formatCostRangeUsd(low: number | null | undefined, high: number | null | undefined): string {
  const from = formatCostUsd(low);
  if (high === null || high === undefined || !Number.isFinite(high) || from === 'Unknown') {
    return from;
  }
  const to = formatCostUsd(high);
  return to === from ? from : `${from} to ${to}`;
}

/** Whether a range prints as two ends: both are known and print differently. */
export function isCostRange(low: number | null | undefined, high: number | null | undefined): boolean {
  return formatCostRangeUsd(low, high) !== formatCostUsd(low);
}

/** `2026-09-21 16:00 UTC`, from a server timestamp; one without a zone is UTC. */
export function formatUtc(iso: string | null | undefined): string {
  if (!iso) {
    return '';
  }
  const date = parseServerUtcDate(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }
  return `${date.toISOString().slice(0, 16).replace('T', ' ')} UTC`;
}

/** `42 s`, `3 min 05 s`, `1 h 02 min`. */
export function formatElapsed(ms: number): string {
  const seconds = Math.max(0, Math.floor(ms / 1000));
  if (seconds < 60) {
    return `${seconds} s`;
  }
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return `${minutes} min ${String(seconds % 60).padStart(2, '0')} s`;
  }
  return `${Math.floor(minutes / 60)} h ${String(minutes % 60).padStart(2, '0')} min`;
}

/** `4 other models`, `1 other model`, `No other models`. */
export function peerCountLabel(count: number): string {
  if (count <= 0) {
    return 'No other models';
  }
  return `${count} other ${count === 1 ? 'model' : 'models'}`;
}
