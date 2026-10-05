import { BenchmarkReportAudience, BenchmarkReportDisclosure } from '../../../services/admin-benchmark.service';
import { parseServerUtcDate } from '../../../utils/date.util';

/** Which documents a document list holds: those of one comparison, or every Report Pack document. */
export type ReportDocumentLibraryScope =
  | { readonly kind: 'comparison'; readonly entryKeys: readonly string[] }
  | { readonly kind: 'all' };

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

export const REPORT_PACK_AUDIENCES: readonly ReportPackAudienceOption[] = [
  {
    audience: BenchmarkReportAudience.ExecutiveSummary,
    label: 'Executive Summary',
    shortLabel: 'Executive',
    description: 'For a non-specialist at the model’s provider, or a manager: plain language, short.',
    checkedByDefault: true
  },
  {
    audience: BenchmarkReportAudience.TechnicalReport,
    label: 'Report for AI Researchers and Developers',
    shortLabel: 'Researchers',
    description: 'For AI researchers and model developers: figures against the peers, strengths, weaknesses and recommendations.',
    checkedByDefault: true
  },
  {
    audience: BenchmarkReportAudience.InternalBrief,
    label: 'Internal Improvement Brief',
    shortLabel: 'Internal',
    description: 'For the Overseer team: what to improve in the chat, the benchmark and the model. Internal only, at Full disclosure.',
    checkedByDefault: false
  }
];

export function audienceLabel(audience: BenchmarkReportAudience): string {
  return REPORT_PACK_AUDIENCES.find(option => option.audience === audience)?.label ?? 'Report document';
}

export function audienceShortLabel(audience: BenchmarkReportAudience): string {
  return REPORT_PACK_AUDIENCES.find(option => option.audience === audience)?.shortLabel ?? 'Document';
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
