import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface BenchmarkScoringProfileDto {
  id: number;
  name: string;
  isDefault: boolean;
  weightAccuracy: number;
  weightCompleteness: number;
  weightConciseness: number;
  weightReadability: number;
  levelScoresJson: string;
  criticalErrorCeiling: number;
  /** Quality score below which an answer is re-graded, when the run has a second-opinion assessor. 0 disables the score trigger. */
  secondOpinionQualityThreshold: number;
  speedTargetMs: number;
  speedDecayK: number;
  speedDifficultyScaling: number;
  maxParallelQuestions: number;
  createdAtUtc: string;
  modifiedAtUtc: string;
}

export interface CreateBenchmarkScoringProfileRequest {
  name: string;
  isDefault?: boolean;
  weightAccuracy: number;
  weightCompleteness: number;
  weightConciseness: number;
  weightReadability: number;
  levelScoresJson: string;
  criticalErrorCeiling: number;
  /** Quality score below which an answer is re-graded, when the run has a second-opinion assessor. 0 disables the score trigger. */
  secondOpinionQualityThreshold: number;
  speedTargetMs: number;
  speedDecayK: number;
  speedDifficultyScaling: number;
  maxParallelQuestions: number;
}

export interface UpdateBenchmarkScoringProfileRequest {
  name: string;
  isDefault: boolean;
  weightAccuracy: number;
  weightCompleteness: number;
  weightConciseness: number;
  weightReadability: number;
  levelScoresJson: string;
  criticalErrorCeiling: number;
  /** Quality score below which an answer is re-graded, when the run has a second-opinion assessor. 0 disables the score trigger. */
  secondOpinionQualityThreshold: number;
  speedTargetMs: number;
  speedDecayK: number;
  speedDifficultyScaling: number;
  maxParallelQuestions: number;
}

export interface StartDifficultyAssessmentRequest {
  suiteId: number;
  questionIds?: number[] | null;
  assessorModelConfigurationId: number;
}

export interface DifficultyAssessmentJobItemDto {
  questionId: number;
  orderIndex: number;
  questionTextExcerpt: string;
  status: string;
  difficulty: number | null;
  errorMessage: string | null;
}

export interface DifficultyAssessmentJobLogEntryDto {
  timestampUtc: string;
  message: string;
  severity: string;
  rawExcerpt: string | null;
}

export interface DifficultyAssessmentJobDto {
  id: string;
  suiteId: number;
  suiteName: string;
  scope: string;
  assessorConfigId: number;
  assessorDisplayName: string;
  startedAtUtc: string;
  completedAtUtc: string | null;
  status: string;
  ratedCount: number;
  failedCount: number;
  totalCount: number;
  totalModelCalls: number;
  promptTokens: number;
  outputTokens: number;
  items: DifficultyAssessmentJobItemDto[];
  log: DifficultyAssessmentJobLogEntryDto[];
}

export interface BenchmarkSuiteDto {
  id: number;
  name: string;
  description: string | null;
  createdAtUtc: string;
  modifiedAtUtc: string | null;
  questionCount: number;
  assessedQuestionCount: number;
  difficultyFullyAssessed: boolean;
}

export interface CreateBenchmarkSuiteRequest {
  name: string;
  description?: string | null;
}

export interface UpdateBenchmarkSuiteRequest {
  name: string;
  description?: string | null;
}

export interface BenchmarkQuestionDto {
  id: number;
  benchmarkSuiteId: number;
  orderIndex: number;
  questionText: string;
  difficulty: string | number;
  expectedPoints: string | null;
  assessedDifficulty?: number | null;
  assessedDifficultyModel?: string | null;
  assessedDifficultyAtUtc?: string | null;
  assessedDifficultyModelConfigurationId?: number | null;
  assessedDifficultyProviderUsed?: string | null;
  assessedDifficultyModelIdUsed?: string | null;
  assessedDifficultyThinkingLevelUsed?: string | null;
  assessedDifficultyReasoningModeUsed?: string | null;
  assessedDifficultyReasoningSummaryUsed?: string | null;
  assessedDifficultyServiceTierUsed?: string | null;
  assessedDifficultyMaxOutputTokensUsed?: number | null;
  createdAtUtc: string;
  modifiedAtUtc?: string | null;
}

export interface CreateBenchmarkQuestionRequest {
  questionText: string;
  difficulty: string | number;
  expectedPoints?: string | null;
}

export interface UpdateBenchmarkQuestionRequest {
  questionText: string;
  difficulty: string | number;
  expectedPoints?: string | null;
}

export interface StartBenchmarkRunRequest {
  suiteId: number;
  testedModelConfigurationId: number;
  assessorModelConfigurationId: number;
  /**
   * Optional. When set, answers flagged with a critical error or scored below the profile's
   * threshold are re-graded once by this configuration. Null means no second opinion.
   */
  secondOpinionAssessorModelConfigurationId?: number | null;
  scoringProfileId?: number | null;
  acknowledgeSameProvider?: boolean;
}

export interface SameProviderWarningDto {
  sameProvider: boolean;
  provider: string;
  testedModelDisplayName: string;
  assessorModelDisplayName: string;
  message: string;
}

export interface BenchmarkFootprintDto {
  runCount: number;
  totalAnswerCharacters: number;
}

export interface BenchmarkRunAnswerDto {
  id: number;
  benchmarkRunId: number;
  orderIndex: number;
  questionText: string;
  difficulty: string | number;
  assessedDifficulty?: number | null;
  answerText: string;
  thoughtText?: string | null;
  status: string | number;
  assessmentStatus?: string | number;
  assessmentError?: string | null;
  errorMessage?: string | null;
  httpStatusCode?: number | null;
  score?: number | null;
  accuracyLevel?: number | null;
  completenessLevel?: number | null;
  concisenessLevel?: number | null;
  readabilityLevel?: number | null;
  criticalError?: boolean;
  accuracyScore?: number | null;
  completenessScore?: number | null;
  concisenessScore?: number | null;
  readabilityScore?: number | null;
  qualityScore?: number | null;
  speedScore?: number | null;
  reviewComment?: string | null;
  durationMs: number;
  timeToFirstTokenMs?: number | null;
  actualServiceTierUsed?: string | null;
  toolCallSummary?: string | null;
  inputTokens?: number | null;
  outputTokens?: number | null;
  cacheReadInputTokens?: number | null;
  cacheCreationInputTokens?: number | null;
  assessedByModelConfigurationId?: number | null;
  assessedByModelDisplayNameUsed?: string | null;
  assessedByModelProviderUsed?: string | null;
  assessedByModelIdUsed?: string | null;
  assessedAtUtc?: string | null;
  rawQualityScore?: number | null;
  modelCallCount?: number | null;
  toolCallCount?: number | null;
  toolBudgetExhausted?: boolean;
  /** The per-band tool call budget that actually applied to this question. */
  toolCallBudgetUsed?: number | null;
  /** Wall-clock time spent in tool batches during this turn. */
  toolTimeMs?: number | null;
  /** Turn duration with tool I/O removed. This is what speed is scored on. */
  modelTimeMs: number;
  /** Transport artifacts removed before grading, retained verbatim for audit. */
  scrubbedArtifactText?: string | null;
  scrubbedArtifactCount: number;
  terminationReason?: string | null;
  answerFlags?: number;
  answerFlagNames?: string[];

  /** What the grading call itself consumed. Never part of the candidate's token counts. */
  assessmentInputTokens?: number | null;
  assessmentOutputTokens?: number | null;
  assessmentDurationMs?: number | null;

  /** The assessor's rubric citations, as stored JSON. */
  assessmentEvidenceJson?: string | null;

  /** The claim the assessor called a critical error, quoted from the graded answer. */
  criticalErrorQuote?: string | null;

  /** Second-opinion verdict, present only where one was triggered. Advisory: the first scored. */
  secondOpinionQualityScore?: number | null;
  secondOpinionCriticalError?: boolean | null;
  secondOpinionByModelDisplayNameUsed?: string | null;
  secondOpinionJson?: string | null;
  secondOpinionDisagreed?: boolean;
}

export interface BenchmarkRunDetailDto {
  id: number;
  benchmarkSuiteId?: number | null;
  suiteName: string;
  testedModelConfigurationId?: number | null;
  testedModelDisplayNameUsed: string;
  testedModelProviderUsed: string;
  testedModelIdUsed: string;
  testedModelThinkingLevelUsed?: string | null;
  testedModelReasoningModeUsed?: string | null;
  testedModelReasoningSummaryUsed?: string | null;
  testedModelServiceTierUsed?: string | null;
  testedModelMaxOutputTokensUsed?: number | null;
  testedModelParallelExecutionModeUsed: number;

  assessorModelConfigurationId?: number | null;
  assessorModelDisplayNameUsed: string;
  assessorModelProviderUsed: string;
  assessorModelIdUsed: string;
  assessorModelThinkingLevelUsed?: string | null;
  assessorModelReasoningModeUsed?: string | null;
  assessorAvailable?: boolean;

  /** Null when the run was started without a second-opinion assessor. */
  secondOpinionAssessorModelConfigurationId?: number | null;
  secondOpinionAssessorModelDisplayNameUsed?: string | null;
  secondOpinionAssessorModelProviderUsed?: string | null;
  secondOpinionAssessorModelIdUsed?: string | null;
  secondOpinionAssessorModelThinkingLevelUsed?: string | null;
  secondOpinionAssessorModelReasoningModeUsed?: string | null;

  startedByUserId?: string | null;
  startedByUserName?: string | null;
  status: string | number;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  finalScore?: number | null;
  computedScore?: number | null;
  qualityIndex?: number | null;
  rawQualityIndex?: number | null;
  speedIndex?: number | null;
  totalAnswerDurationMs: number;
  scoringProfileId?: number | null;
  scoringProfileName?: string | null;
  scoringProfileSnapshotJson?: string | null;
  scoringMethodVersion: number;
  harnessVersion?: string | null;
  maxToolCallsPerQuestionUsed?: number | null;
  degradedAnswerCount?: number;
  toolStarvedAnswerCount?: number;
  /**
   * Answers corrupted beyond recovery (empty or truncated). Disjoint from
   * recoveredAnswerCount and toolStarvedAnswerCount: together with the clean count these
   * partition the run, so the four always sum to the question count.
   */
  transportDefectAnswerCount: number;
  /**
   * Answers the harness repaired: leaked transport artifacts removed, the answer beneath
   * graded normally. A provider-path defect worth reporting, not a failed answer.
   */
  recoveredAnswerCount?: number;
  /**
   * Answers carrying an advisory flag. Overlaps the counts above and must never be summed
   * with them.
   */
  advisoryFlagAnswerCount: number;
  scrubbedArtifactAnswerCount: number;
  toolOverheadMs?: number | null;
  difficultyFallbackUsed: boolean;
  speedMeasurementDegraded: boolean;
  maxParallelQuestionsUsed: number;
  answeredQuestionCount: number;
  totalQuestionCount: number;
  purposeStatementUsed?: string | null;
  sameProviderAcknowledged?: boolean;
  assessmentJson?: string | null;
  assessmentText?: string | null;
  assessmentParseFailed: boolean;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalCacheReadTokens: number;
  totalCacheCreationTokens: number;
  totalDurationMs: number;

  /** Assessor-side usage, kept apart from the candidate totals above. */
  totalAssessmentInputTokens?: number;
  totalAssessmentOutputTokens?: number;
  totalAssessmentDurationMs?: number;

  errorMessage?: string | null;

  /**
   * Order indexes whose request is in flight to the provider right now. Answer rows appear
   * only after the model replies, so this is what separates a question being answered from
   * one not yet dispatched. Empty for any run that is not currently executing.
   */
  inFlightOrderIndexes?: number[];

  answers: BenchmarkRunAnswerDto[];
}

export interface BenchmarkRunSummaryDto {
  id: number;
  benchmarkSuiteId?: number | null;
  suiteName: string;
  testedModelConfigurationId?: number | null;
  testedModelDisplayNameUsed: string;
  testedModelProviderUsed: string;
  testedModelIdUsed: string;
  assessorModelConfigurationId?: number | null;
  assessorModelDisplayNameUsed: string;
  startedByUserName?: string | null;
  status: string | number;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  finalScore?: number | null;
  computedScore?: number | null;
  qualityIndex?: number | null;
  rawQualityIndex?: number | null;
  speedIndex?: number | null;
  totalAnswerDurationMs: number;
  speedMeasurementDegraded: boolean;
  answeredQuestionCount: number;
  totalQuestionCount: number;
  degradedAnswerCount?: number;
  toolStarvedAnswerCount?: number;
  harnessVersion?: string | null;
  totalDurationMs: number;
}

@Injectable({
  providedIn: 'root'
})
export class AdminBenchmarkService {
  private http = inject(HttpClient);

  // Scoring Profiles
  getScoringProfiles(): Observable<BenchmarkScoringProfileDto[]> {
    return this.http.get<BenchmarkScoringProfileDto[]>('/api/admin/benchmark/scoring-profiles');
  }

  createScoringProfile(req: CreateBenchmarkScoringProfileRequest): Observable<BenchmarkScoringProfileDto> {
    return this.http.post<BenchmarkScoringProfileDto>('/api/admin/benchmark/scoring-profiles', req);
  }

  updateScoringProfile(id: number, req: UpdateBenchmarkScoringProfileRequest): Observable<BenchmarkScoringProfileDto> {
    return this.http.put<BenchmarkScoringProfileDto>(`/api/admin/benchmark/scoring-profiles/${id}`, req);
  }

  setDefaultScoringProfile(id: number): Observable<void> {
    return this.http.post<void>(`/api/admin/benchmark/scoring-profiles/${id}/default`, {});
  }

  deleteScoringProfile(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/benchmark/scoring-profiles/${id}`);
  }

  // Difficulty Assessment
  startDifficultyAssessment(req: StartDifficultyAssessmentRequest): Observable<{ jobId: string }> {
    return this.http.post<{ jobId: string }>('/api/admin/benchmark/difficulty-assessments', req);
  }

  getDifficultyAssessment(jobId: string): Observable<DifficultyAssessmentJobDto> {
    return this.http.get<DifficultyAssessmentJobDto>(`/api/admin/benchmark/difficulty-assessments/${jobId}`);
  }

  getActiveDifficultyAssessment(): Observable<DifficultyAssessmentJobDto | null> {
    return this.http.get<DifficultyAssessmentJobDto | null>('/api/admin/benchmark/difficulty-assessments/active');
  }

  cancelDifficultyAssessment(jobId: string): Observable<{ cancelled: boolean }> {
    return this.http.post<{ cancelled: boolean }>(`/api/admin/benchmark/difficulty-assessments/${jobId}/cancel`, {});
  }

  // Suites
  getSuites(): Observable<BenchmarkSuiteDto[]> {
    return this.http.get<BenchmarkSuiteDto[]>('/api/admin/benchmark/suites');
  }

  createSuite(req: CreateBenchmarkSuiteRequest): Observable<BenchmarkSuiteDto> {
    return this.http.post<BenchmarkSuiteDto>('/api/admin/benchmark/suites', req);
  }

  updateSuite(id: number, req: UpdateBenchmarkSuiteRequest): Observable<void> {
    return this.http.put<void>(`/api/admin/benchmark/suites/${id}`, req);
  }

  deleteSuite(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/benchmark/suites/${id}`);
  }

  duplicateSuite(id: number): Observable<BenchmarkSuiteDto> {
    return this.http.post<BenchmarkSuiteDto>(`/api/admin/benchmark/suites/${id}/duplicate`, {});
  }

  importDefaultSuite(): Observable<BenchmarkSuiteDto> {
    return this.http.post<BenchmarkSuiteDto>('/api/admin/benchmark/suites/import-default', {});
  }

  // Questions
  getQuestions(suiteId: number): Observable<BenchmarkQuestionDto[]> {
    return this.http.get<BenchmarkQuestionDto[]>(`/api/admin/benchmark/suites/${suiteId}/questions`);
  }

  createQuestion(suiteId: number, req: CreateBenchmarkQuestionRequest): Observable<BenchmarkQuestionDto> {
    return this.http.post<BenchmarkQuestionDto>(`/api/admin/benchmark/suites/${suiteId}/questions`, req);
  }

  updateQuestion(id: number, req: UpdateBenchmarkQuestionRequest): Observable<BenchmarkQuestionDto> {
    return this.http.put<BenchmarkQuestionDto>(`/api/admin/benchmark/questions/${id}`, req);
  }

  deleteQuestion(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/benchmark/questions/${id}`);
  }

  reorderQuestions(suiteId: number, orderedIds: number[]): Observable<void> {
    return this.http.put<void>(`/api/admin/benchmark/suites/${suiteId}/questions/reorder`, { orderedIds });
  }

  // Runs
  startRun(req: StartBenchmarkRunRequest): Observable<{ runId: number }> {
    return this.http.post<{ runId: number }>('/api/admin/benchmark/runs', req);
  }

  getRun(id: number): Observable<BenchmarkRunDetailDto> {
    return this.http.get<BenchmarkRunDetailDto>(`/api/admin/benchmark/runs/${id}`);
  }

  /**
   * Returns the id of the run currently executing, or null when the server is idle.
   * A 204 arrives as null, the same shape getActiveDifficultyAssessment() relies on.
   */
  getActiveRun(): Observable<{ runId: number } | null> {
    return this.http.get<{ runId: number } | null>('/api/admin/benchmark/runs/active');
  }

  getRuns(suiteId?: number, take?: number): Observable<BenchmarkRunSummaryDto[]> {
    let params: any = {};
    if (suiteId != null) params.suiteId = suiteId;
    if (take != null) params.take = take;
    return this.http.get<BenchmarkRunSummaryDto[]>('/api/admin/benchmark/runs', { params });
  }

  rescoreRun(runId: number, scoringProfileId?: number | null): Observable<void> {
    return this.http.post<void>(`/api/admin/benchmark/runs/${runId}/rescore`, { scoringProfileId });
  }

  reassessAnswer(runId: number, answerId: number, assessorModelConfigurationId?: number | null): Observable<{ runId: number }> {
    return this.http.post<{ runId: number }>(`/api/admin/benchmark/runs/${runId}/answers/${answerId}/reassess`, { assessorModelConfigurationId });
  }

  rerunAnswer(runId: number, answerId: number, assessorModelConfigurationId?: number | null): Observable<{ runId: number }> {
    return this.http.post<{ runId: number }>(`/api/admin/benchmark/runs/${runId}/answers/${answerId}/rerun`, { assessorModelConfigurationId });
  }

  rerunFinalSynthesis(runId: number, assessorModelConfigurationId?: number | null): Observable<{ runId: number }> {
    return this.http.post<{ runId: number }>(`/api/admin/benchmark/runs/${runId}/rerun-synthesis`, { assessorModelConfigurationId });
  }

  retryFailedAssessments(runId: number, assessorModelConfigurationId?: number | null): Observable<{ runId: number }> {
    return this.http.post<{ runId: number }>(`/api/admin/benchmark/runs/${runId}/retry-failed-assessments`, { assessorModelConfigurationId });
  }

  cancelRun(id: number): Observable<{ success: boolean }> {
    return this.http.post<{ success: boolean }>(`/api/admin/benchmark/runs/${id}/cancel`, {});
  }

  rerunFailedQuestions(id: number): Observable<{ runId: number }> {
    return this.http.post<{ runId: number }>(`/api/admin/benchmark/runs/${id}/rerun-failed`, {});
  }

  getRunReportUrl(id: number): string {
    return `/api/admin/benchmark/runs/${id}/report`;
  }

  deleteRun(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/benchmark/runs/${id}`);
  }

  getSuiteRunsFootprint(suiteId: number): Observable<BenchmarkFootprintDto> {
    return this.http.get<BenchmarkFootprintDto>(`/api/admin/benchmark/suites/${suiteId}/runs/footprint`);
  }

  deleteSuiteRuns(suiteId: number): Observable<{ deletedCount: number }> {
    return this.http.delete<{ deletedCount: number }>(`/api/admin/benchmark/suites/${suiteId}/runs`);
  }
}
