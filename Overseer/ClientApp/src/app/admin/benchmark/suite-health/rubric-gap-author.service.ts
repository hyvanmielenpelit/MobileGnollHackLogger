import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

// The Rubric Gap Author's whole client contract, in one file for the same reason the server keeps
// its DTOs in BenchmarkRubricGapAuthorModels.cs rather than in BenchmarkAdminModels.cs: the boundary
// this feature enforces -- a model drafts, a human accepts, one draft at a time -- is only easy to
// keep intact while it is readable in one place. There is deliberately no bulk-accept call here, and
// none may be added; see AcceptRubricAdditionRequest on the server for the same statement.

export interface StartRubricGapAuthorRequest {
  suiteId: number;
  authorModelConfigurationId: number;

  /** Free-text operator instructions, passed to the drafting prompt verbatim. */
  instructions: string | null;

  /** Null means every eligible cluster in the suite. */
  clusterKeys: string[] | null;
}

export interface RubricGapAuthorDraftDto {
  /** `questionId:index`; the stable key an acceptance quotes back to the server. */
  clusterKey: string;
  questionId: number;
  questionOrderIndex: number;
  questionTextExcerpt: string;
  claims: string[];
  clusterVerdict: string;
  occurrences: number;
  modelFamilies: string[];
  status: string;
  proposedText: string | null;
  citation: string | null;
  justification: string | null;
  confidenceNote: string | null;
  errorMessage: string | null;
}

export interface RubricGapAuthorJobLogEntryDto {
  timestampUtc: string;
  message: string;
  severity: string;
  rawExcerpt: string | null;
}

export interface RubricGapAuthorJobDto {
  id: string;
  suiteId: number;
  suiteName: string;
  authorConfigId: number;
  authorDisplayName: string;
  instructions: string | null;
  startedByUserId: string | null;
  startedAtUtc: string;
  completedAtUtc: string | null;
  status: string;
  totalModelCalls: number;
  promptTokens: number;
  outputTokens: number;
  drafts: RubricGapAuthorDraftDto[];
  log: RubricGapAuthorJobLogEntryDto[];
}

export interface AcceptRubricAdditionRequest {
  /** What the operator submits, which may be the draft, an edit of it, or a replacement. */
  acceptedText: string;
  jobId: string | null;
  clusterKey: string | null;
}

export interface RubricAdditionAcceptanceDto {
  id: number;
  questionId: number;
  questionOrderIndex: number;
  itemRevisionAfter: number;
  acceptedVerbatim: boolean;
  citation: string | null;
  authorModelDisplayName: string | null;
  acceptedAtUtc: string;
  expectedPoints: string | null;
}

@Injectable({ providedIn: 'root' })
export class RubricGapAuthorService {
  private http = inject(HttpClient);

  startRubricGapAuthor(req: StartRubricGapAuthorRequest): Observable<{ jobId: string }> {
    return this.http.post<{ jobId: string }>('/api/admin/benchmark/rubric-gap-author', req);
  }

  getRubricGapAuthor(jobId: string): Observable<RubricGapAuthorJobDto> {
    return this.http.get<RubricGapAuthorJobDto>(`/api/admin/benchmark/rubric-gap-author/${jobId}`);
  }

  getActiveRubricGapAuthor(): Observable<RubricGapAuthorJobDto | null> {
    return this.http.get<RubricGapAuthorJobDto | null>('/api/admin/benchmark/rubric-gap-author/active');
  }

  cancelRubricGapAuthor(jobId: string): Observable<{ cancelled: boolean }> {
    return this.http.post<{ cancelled: boolean }>(`/api/admin/benchmark/rubric-gap-author/${jobId}/cancel`, {});
  }

  /**
   * Applies one operator-approved rubric addition to one question.
   *
   * One call, one draft, one item-revision bump. The request carries the final text the human
   * submitted rather than a draft id, so the server stores what was actually authored and can
   * record whether it was taken verbatim or edited.
   */
  acceptRubricAddition(questionId: number, req: AcceptRubricAdditionRequest): Observable<RubricAdditionAcceptanceDto> {
    return this.http.post<RubricAdditionAcceptanceDto>(
      `/api/admin/benchmark/questions/${questionId}/rubric-additions/accept`, req);
  }
}
