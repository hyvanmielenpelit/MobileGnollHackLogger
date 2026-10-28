import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** The route prefix of `AdminFeedbackTransferController`. */
export const FEEDBACK_TRANSFER_ENDPOINT = '/api/admin/feedback';

/** Which sub-tab's issues a file holds. */
export type FeedbackTransferSource = 'overseer' | 'gnollbench';

/** An issue's shown status; `regressed` is derived and never set by a file. */
export type FeedbackTransferStatus = 'new' | 'acknowledged' | 'resolved' | 'regressed' | 'dismissed';

/** The statuses a resolution file may set. */
export type FeedbackResolutionStatus = Exclude<FeedbackTransferStatus, 'regressed'>;

/** The most issues one export holds. */
export const FEEDBACK_EXPORT_MAX_ISSUES = 200;

/** The file format of an export: YAML for an AI coding assistant, Markdown to read. */
export type FeedbackExportFormat = 'yaml' | 'markdown';

/**
 * Where an issue's fix went, in the canonical order (`FeedbackResolvedVia` on the server). The ids are what the API
 * and the files carry; the UI shows their labels (`feedback-labels.ts`).
 */
export const FEEDBACK_RESOLVED_VIA_IDS = [
  'wiki', 'knowledge_base', 'overseer_release', 'gnollhack_release', 'server_configuration', 'other'
] as const;

/** One *Resolved via* id. The wire types carry `string[]`, so an id a newer server adds still reads. */
export type FeedbackResolvedVia = typeof FEEDBACK_RESOLVED_VIA_IDS[number];

/** The body of `POST export`. Overseer sends `issueIds`, GnollBench `fingerprints`. */
export interface FeedbackExportRequest {
  source: FeedbackTransferSource;
  format: FeedbackExportFormat;
  issueIds?: number[];
  fingerprints?: string[];
  occurrencesPerIssue?: 5 | 10 | 25;
  /** GnollBench only. */
  includeQuestionText?: boolean;
}

/** The export file, and the requested issues that no longer exist. */
export interface FeedbackExportResult {
  fileName: string;
  /** The file's text, LF line endings. */
  content: string;
  /** `text/markdown;charset=utf-8` or `application/yaml;charset=utf-8`. */
  mediaType: string;
  issueCount: number;
  missingIds: number[];
  missingFingerprints: string[];
}

/** The 404 body of an export none of whose issues exist. */
export interface FeedbackExportNotFound {
  message: string;
  missingIds: number[];
  missingFingerprints: string[];
}

/**
 * One issue of a resolution file as JSON. For `status`, `resolution`, `fixedInVersion` and `note`, absent keeps the
 * stored value and `''` clears it; `resolvedVia` (version 2 only) absent keeps the stored list and `[]` clears it.
 * `expectedStatus` and `expectedTriagedUtc` are sent to apply only: the current values the preview showed.
 */
export interface FeedbackResolutionItem {
  id?: number;
  fingerprint: string;
  status?: FeedbackResolutionStatus;
  resolution?: string;
  fixedInVersion?: string;
  note?: string;
  resolvedVia?: string[];
  expectedStatus?: FeedbackTransferStatus;
  expectedTriagedUtc?: string | null;
}

/** The body of both `POST resolutions/preview` and `POST resolutions/apply`. */
export interface FeedbackResolutionRequest {
  format?: 'overseer-feedback-resolutions';
  version?: 1 | 2;
  source: FeedbackTransferSource;
  exportedAtUtc?: string;
  items: FeedbackResolutionItem[];
}

/** One validation error of a refused file; `index` is the item's zero-based position, null for the file. */
export interface FeedbackResolutionError {
  index: number | null;
  message: string;
}

/** The 400 body of a file that breaks the format's rules. */
export interface FeedbackResolutionInvalid {
  message: string;
  errors: FeedbackResolutionError[];
}

/** An issue's triage values as stored now. Dates are UTC ISO strings. */
export interface FeedbackResolutionState {
  status: FeedbackTransferStatus;
  note: string | null;
  resolution: string | null;
  fixedInVersion: string | null;
  resolvedVia: string[];
  triagedUtc: string | null;
  lastSeenUtc: string | null;
}

/** An issue's triage values once its item is applied. */
export interface FeedbackResolutionValues {
  status: FeedbackTransferStatus;
  note: string | null;
  resolution: string | null;
  fixedInVersion: string | null;
  resolvedVia: string[];
}

/** Which values an item changes. `resolvedVia` compares the lists as sets. */
export interface FeedbackResolutionChanges {
  status: boolean;
  note: boolean;
  resolution: boolean;
  fixedInVersion: boolean;
  resolvedVia: boolean;
}

/** One item of a preview. `subject` is model-written text, shown as plain text only. */
export interface FeedbackResolutionPreviewItem {
  index: number;
  id: number | null;
  fingerprint: string;
  exists: boolean;
  fingerprintMatches: boolean;
  category: string | null;
  subject: string | null;
  current: FeedbackResolutionState | null;
  after: FeedbackResolutionValues | null;
  changes: FeedbackResolutionChanges;
  hasChanges: boolean;
  /** Null when the file has no `exportedAtUtc`. */
  occurrencesSinceExport: number | null;
  triagedSinceExport: boolean;
  regressedSinceExport: boolean;
  /** An item with an error cannot be applied: the issue is gone, or the fingerprint is not the id's. */
  errors: string[];
  notices: string[];
}

/** The preview of a resolution file. */
export interface FeedbackResolutionPreview {
  source: FeedbackTransferSource;
  exportedAtUtc: string | null;
  errorCount: number;
  changeCount: number;
  triagedSinceExportCount: number;
  notices: string[];
  items: FeedbackResolutionPreviewItem[];
}

/** The result of an apply. */
export interface FeedbackResolutionApplyResult {
  changed: number;
  unchanged: number;
}

/** An issue that changed after the preview. */
export interface FeedbackResolutionConflictItem {
  index: number;
  id: number | null;
  fingerprint: string;
  reason: 'changed' | 'missing';
  expectedStatus: string | null;
  currentStatus: string | null;
  expectedTriagedUtc: string | null;
  currentTriagedUtc: string | null;
}

/** The 409 body of an apply that found issues changed since the preview; nothing was written. */
export interface FeedbackResolutionConflict {
  message: string;
  conflicts: FeedbackResolutionConflictItem[];
}

/** One example file of `GET format-examples`. */
export interface FeedbackFormatExample {
  fileName: string;
  content: string;
  mediaType: string;
}

/** Example files of the issues export and the resolution file, built on the server by the real builders. */
export interface FeedbackFormatExamples {
  issues: FeedbackFormatExample;
  resolutions: FeedbackFormatExample;
}

/**
 * The typed client of `AdminFeedbackTransferController` (`api/admin/feedback`): the YAML or Markdown export of
 * selected issues, the preview and apply of a resolution file, and the example files of both formats.
 */
@Injectable({ providedIn: 'root' })
export class FeedbackTransferService {
  private http = inject(HttpClient);

  exportIssues(request: FeedbackExportRequest): Observable<FeedbackExportResult> {
    return this.http.post<FeedbackExportResult>(`${FEEDBACK_TRANSFER_ENDPOINT}/export`, request);
  }

  previewResolutions(request: FeedbackResolutionRequest): Observable<FeedbackResolutionPreview> {
    return this.http.post<FeedbackResolutionPreview>(`${FEEDBACK_TRANSFER_ENDPOINT}/resolutions/preview`, request);
  }

  applyResolutions(request: FeedbackResolutionRequest): Observable<FeedbackResolutionApplyResult> {
    return this.http.post<FeedbackResolutionApplyResult>(`${FEEDBACK_TRANSFER_ENDPOINT}/resolutions/apply`, request);
  }

  formatExamples(source: FeedbackTransferSource): Observable<FeedbackFormatExamples> {
    return this.http.get<FeedbackFormatExamples>(`${FEEDBACK_TRANSFER_ENDPOINT}/format-examples`, { params: { source } });
  }
}

/** The HTTP status of a failed request; 0 when it carries none. */
export function feedbackTransferStatus(err: unknown): number {
  const status = (err as { status?: unknown } | null)?.status;
  return typeof status === 'number' ? status : 0;
}

/** The 400 body of a refused resolution file, or null when the failure was something else. */
export function feedbackResolutionInvalid(err: unknown): FeedbackResolutionInvalid | null {
  if (feedbackTransferStatus(err) !== 400) return null;
  const body = (err as { error?: unknown }).error as Partial<FeedbackResolutionInvalid> | null | undefined;
  if (!body || typeof body !== 'object') return null;
  const errors = Array.isArray(body.errors)
    ? body.errors
      .filter(e => e && typeof e.message === 'string')
      .map(e => ({ index: typeof e.index === 'number' ? e.index : null, message: e.message }))
    : [];
  const message = typeof body.message === 'string' ? body.message : '';
  if (!message && errors.length === 0) return null;
  return { message, errors };
}

/** The 409 body of an apply that met changed issues, or null when the failure was something else. */
export function feedbackResolutionConflict(err: unknown): FeedbackResolutionConflict | null {
  if (feedbackTransferStatus(err) !== 409) return null;
  const body = (err as { error?: unknown }).error as Partial<FeedbackResolutionConflict> | null | undefined;
  if (!body || typeof body !== 'object') return null;
  return {
    message: typeof body.message === 'string' ? body.message : '',
    conflicts: Array.isArray(body.conflicts) ? body.conflicts : []
  };
}

/** The 404 body of an export none of whose issues exist, or null when the failure was something else. */
export function feedbackExportNotFound(err: unknown): FeedbackExportNotFound | null {
  if (feedbackTransferStatus(err) !== 404) return null;
  const body = (err as { error?: unknown }).error as Partial<FeedbackExportNotFound> | null | undefined;
  if (!body || typeof body !== 'object') return null;
  return {
    message: typeof body.message === 'string' ? body.message : '',
    missingIds: Array.isArray(body.missingIds) ? body.missingIds : [],
    missingFingerprints: Array.isArray(body.missingFingerprints) ? body.missingFingerprints : []
  };
}
