import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';

import { BenchmarkRunReportJobDto, WriteRunReportDocumentsResponse } from './admin-benchmark.service';
import {
  CcAnalysisFreshness,
  CcAnalysisRequest,
  CcAnalysisResult,
  CcAnalysisSummary,
  CcAnchorResponse,
  CcAnnotation,
  CcAnnotationRequest,
  CcBatteryRunRow,
  CcComparisonSets,
  CcModelAxis,
  CcRegradeEstimate,
  CcRegradeJob,
  CcReportEstimate,
  CcReportEstimateRequest,
  CcRunRow,
  CcTimeline,
  CcWriteReportsRequest
} from '../admin/benchmark/chat-consistency-tab/chat-consistency.models';

/** The route prefix of `AdminChatConsistencyController`. */
export const CHAT_CONSISTENCY_ENDPOINT = '/api/admin/benchmark/chat-consistency';

/**
 * The refusal text of a failed request: the `{ error }` body the API answers a refusal with, a plain
 * string body, or a short description of the status.
 */
export function ccErrorText(err: unknown, fallback = 'The request failed.'): string {
  if (err instanceof HttpErrorResponse || (err && typeof err === 'object' && 'status' in err)) {
    const response = err as { status?: number; error?: unknown };
    const body = response.error;
    if (typeof body === 'string' && body.trim()) {
      return body.trim();
    }
    if (body && typeof body === 'object') {
      const message = (body as { error?: unknown }).error;
      if (typeof message === 'string' && message.trim()) {
        return message.trim();
      }
    }
    if (response.status === 0) return 'The server could not be reached.';
    if (response.status === 404) return 'It no longer exists.';
    if (typeof response.status === 'number' && response.status > 0) return `${fallback} (HTTP ${response.status})`;
  }
  return fallback;
}

/** `modelKey`, `from` and `to` as the timeline and run table take them; a missing bound is left out. */
function rangeParams(modelKey: string, fromUtc: string | null | undefined, toUtc: string | null | undefined): HttpParams {
  let params = new HttpParams().set('modelKey', modelKey);
  if (fromUtc) params = params.set('from', fromUtc);
  if (toUtc) params = params.set('to', toUtc);
  return params;
}

/** The GnollBench chat consistency API. Admin only. */
@Injectable({ providedIn: 'root' })
export class AdminChatConsistencyService {
  private readonly http = inject(HttpClient);

  // --- Model axes, timeline and run table ---

  /** Every model axis with usable runs. */
  listModels(): Observable<CcModelAxis[]> {
    return this.http.get<CcModelAxis[]>(`${CHAT_CONSISTENCY_ENDPOINT}/models`);
  }

  /** One point per usable run of the subject between the UTC bounds, either optional. */
  getTimeline(modelKey: string, fromUtc?: string | null, toUtc?: string | null): Observable<CcTimeline> {
    return this.http.get<CcTimeline>(`${CHAT_CONSISTENCY_ENDPOINT}/timeline`, { params: rangeParams(modelKey, fromUtc, toUtc) });
  }

  /** The subject's run table over the range. */
  getRuns(modelKey: string, fromUtc?: string | null, toUtc?: string | null): Observable<CcRunRow[]> {
    return this.http.get<CcRunRow[]>(`${CHAT_CONSISTENCY_ENDPOINT}/runs`, { params: rangeParams(modelKey, fromUtc, toUtc) });
  }

  /** The batteries and suites the subject can be compared within over the range, and the default one. */
  getComparisonSets(modelKey: string, fromUtc?: string | null, toUtc?: string | null): Observable<CcComparisonSets> {
    return this.http.get<CcComparisonSets>(`${CHAT_CONSISTENCY_ENDPOINT}/comparison-sets`,
      { params: rangeParams(modelKey, fromUtc, toUtc) });
  }

  /** The subject's battery runs over the range, each with its usable members. */
  getBatteryRuns(modelKey: string, fromUtc?: string | null, toUtc?: string | null): Observable<CcBatteryRunRow[]> {
    return this.http.get<CcBatteryRunRow[]>(`${CHAT_CONSISTENCY_ENDPOINT}/battery-runs`,
      { params: rangeParams(modelKey, fromUtc, toUtc) });
  }

  // --- Analyses ---

  /** Runs and saves an analysis; 400 `{ error }` for a refused request. */
  analyze(request: CcAnalysisRequest): Observable<CcAnalysisResult> {
    return this.http.post<CcAnalysisResult>(`${CHAT_CONSISTENCY_ENDPOINT}/analyses`, request);
  }

  /** Every saved analysis, newest first, without the full results. */
  listAnalyses(): Observable<CcAnalysisSummary[]> {
    return this.http.get<CcAnalysisSummary[]>(`${CHAT_CONSISTENCY_ENDPOINT}/analyses`);
  }

  getAnalysis(id: number): Observable<CcAnalysisResult> {
    return this.http.get<CcAnalysisResult>(`${CHAT_CONSISTENCY_ENDPOINT}/analyses/${id}`);
  }

  /** Whether a saved analysis is out of date, and why; 404 for an unknown id. */
  getAnalysisFreshness(id: number): Observable<CcAnalysisFreshness> {
    return this.http.get<CcAnalysisFreshness>(`${CHAT_CONSISTENCY_ENDPOINT}/analyses/${id}/freshness`);
  }

  /** 204; 409 `{ error }` while report documents written from the analysis exist. */
  deleteAnalysis(id: number): Observable<void> {
    return this.http.delete<void>(`${CHAT_CONSISTENCY_ENDPOINT}/analyses/${id}`);
  }

  // --- Common-grader re-grade ---

  /** What re-grading the runs with the assessor would cost. Makes no model call. */
  estimateRegrade(runIds: readonly number[], assessorConfigId: number): Observable<CcRegradeEstimate> {
    return this.http.post<CcRegradeEstimate>(`${CHAT_CONSISTENCY_ENDPOINT}/regrade/estimate`,
      { runIds: [...runIds], assessorConfigId });
  }

  /** Starts a re-grade the operator confirmed after seeing the estimate: 202 with the job; 400 `{ error }`. */
  startRegrade(runIds: readonly number[], assessorConfigId: number): Observable<CcRegradeJob> {
    return this.http.post<CcRegradeJob>(`${CHAT_CONSISTENCY_ENDPOINT}/regrade`,
      { runIds: [...runIds], assessorConfigId, confirmed: true });
  }

  /** The current or last re-grade job, or null (204) when none has run since start-up. */
  getRegradeJob(): Observable<CcRegradeJob | null> {
    return this.http.get<CcRegradeJob>(`${CHAT_CONSISTENCY_ENDPOINT}/regrade/job`, { observe: 'response' }).pipe(
      map(response => response.status === 204 ? null : response.body ?? null)
    );
  }

  /** 202 with the job; 409 `{ error }` when none runs. */
  cancelRegrade(): Observable<CcRegradeJob> {
    return this.http.post<CcRegradeJob>(`${CHAT_CONSISTENCY_ENDPOINT}/regrade/cancel`, {});
  }

  // --- Grader anchors ---

  setAnchor(runId: number, isAnchor: boolean): Observable<CcAnchorResponse> {
    return this.http.put<CcAnchorResponse>(`${CHAT_CONSISTENCY_ENDPOINT}/runs/${runId}/anchor`, { isAnchor });
  }

  // --- Annotations ---

  /** Annotations, oldest first; those applying to the provider and model when a provider is given. */
  listAnnotations(provider?: string | null, modelId?: string | null): Observable<CcAnnotation[]> {
    let params = new HttpParams();
    if (provider) params = params.set('provider', provider);
    if (modelId) params = params.set('modelId', modelId);
    return this.http.get<CcAnnotation[]>(`${CHAT_CONSISTENCY_ENDPOINT}/annotations`, { params });
  }

  /** 200 with the annotation; 400 `{ error }` for a refused field. */
  addAnnotation(request: CcAnnotationRequest): Observable<CcAnnotation> {
    return this.http.post<CcAnnotation>(`${CHAT_CONSISTENCY_ENDPOINT}/annotations`, request);
  }

  deleteAnnotation(id: number): Observable<void> {
    return this.http.delete<void>(`${CHAT_CONSISTENCY_ENDPOINT}/annotations/${id}`);
  }

  // --- AI-written reports of an analysis ---

  /** What writing the analysis's documents with the writer would cost, and whether the Provider Issue Report is available. */
  estimateReports(analysisId: number, request: CcReportEstimateRequest): Observable<CcReportEstimate> {
    return this.http.post<CcReportEstimate>(`${CHAT_CONSISTENCY_ENDPOINT}/analyses/${analysisId}/report-documents/estimate`, request);
  }

  /**
   * Starts writing the analysis's documents. 202 once queued; 400 / 409 `{ error }`, 409 with a
   * same-provider warning while it is not acknowledged, or 409 `{ error, outOfDate: true }` for an
   * out-of-date analysis without `acknowledgeOutOfDate`.
   */
  writeReports(analysisId: number, request: CcWriteReportsRequest): Observable<WriteRunReportDocumentsResponse> {
    return this.http.post<WriteRunReportDocumentsResponse>(`${CHAT_CONSISTENCY_ENDPOINT}/analyses/${analysisId}/report-documents`, request);
  }

  /** The analysis's report-writing job, or null (204) when this server process knows none. */
  getReportJob(analysisId: number): Observable<BenchmarkRunReportJobDto | null> {
    return this.http.get<BenchmarkRunReportJobDto>(`${CHAT_CONSISTENCY_ENDPOINT}/analyses/${analysisId}/report-documents/job`,
      { observe: 'response' }).pipe(
      map(response => response.status === 204 ? null : response.body ?? null)
    );
  }

  /** 202 with the job view; 409 `{ error }` when none is in progress. */
  cancelReportJob(analysisId: number): Observable<BenchmarkRunReportJobDto> {
    return this.http.post<BenchmarkRunReportJobDto>(`${CHAT_CONSISTENCY_ENDPOINT}/analyses/${analysisId}/report-documents/cancel`, {});
  }
}
