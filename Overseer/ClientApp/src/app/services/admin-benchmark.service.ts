import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/**
 * How the second-opinion assessor is used. Off is equivalent to selecting no second-opinion
 * assessor at all; All is the only setting that measures grader agreement rather than sampling
 * it, because under the trigger-based modes the disagreement rate is conditioned on the first
 * assessor's own uncertainty.
 */
export enum BenchmarkSecondOpinionMode {
  Off = 0,
  Flagged = 1,
  FlaggedAndOutliers = 2,
  All = 3
}

export interface BenchmarkSecondOpinionModeOption {
  value: BenchmarkSecondOpinionMode;
  label: string;
  hint: string;
}

/** In coverage order, labelled for what they do rather than for their enum names. */
export const BENCHMARK_SECOND_OPINION_MODES: readonly BenchmarkSecondOpinionModeOption[] = [
  {
    value: BenchmarkSecondOpinionMode.Off,
    label: 'Never',
    hint: 'No second verdict is produced.'
  },
  {
    value: BenchmarkSecondOpinionMode.Flagged,
    label: 'Only flagged answers',
    hint: 'Critical errors, contested verdicts, unverifiable claims, and scores below the profile threshold.'
  },
  {
    value: BenchmarkSecondOpinionMode.FlaggedAndOutliers,
    label: 'Flagged answers and statistical outliers',
    hint: "Adds answers far below the run's own median, found after scoring. Adds a stage to the run."
  },
  {
    value: BenchmarkSecondOpinionMode.All,
    label: 'Every answer (double grading)',
    hint: 'Recommended. The only setting that measures grader agreement rather than sampling it.'
  }
];

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
  /** Off (0), Flagged (1), FlaggedAndOutliers (2) or All (3). */
  secondOpinionMode: number;
  /** Quality points below the run's own median at which an answer is re-graded. FlaggedAndOutliers only. */
  secondOpinionOutlierDeltaPoints: number;
  secondOpinionBlind?: boolean;
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
  /** Off (0), Flagged (1), FlaggedAndOutliers (2) or All (3). */
  secondOpinionMode: number;
  /** Quality points below the run's own median at which an answer is re-graded. FlaggedAndOutliers only. */
  secondOpinionOutlierDeltaPoints: number;
  secondOpinionBlind?: boolean;
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
  /** Off (0), Flagged (1), FlaggedAndOutliers (2) or All (3). */
  secondOpinionMode: number;
  /** Quality points below the run's own median at which an answer is re-graded. FlaggedAndOutliers only. */
  secondOpinionOutlierDeltaPoints: number;
  secondOpinionBlind?: boolean;
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
  gameSnapshotId?: number | null;
  gameSnapshotName?: string | null;
  gameSnapshotCharCount?: number | null;
  hasGeneratedQuestions?: boolean;
  reviewedQuestionCount?: number;
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
  itemRevision?: number;
  questionText: string;
  difficulty: string | number;
  expectedPoints: string | null;
  isGenerated?: boolean;
  reviewedAtRevision?: number | null;
  reviewedAtUtc?: string | null;
  reviewedByUserId?: string | null;
  isReviewed?: boolean;
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

export interface BenchmarkGameSnapshotDto {
  id: number;
  name: string;
  sanitizedText?: string | null;
  digestText?: string | null;
  charCount: number;
  sha256: string;
  captureMethod: string;
  sourceGnollHackVersion?: string | null;
  notes?: string | null;
  sourceChatSessionId?: number | null;
  capturedAtUtc?: string | null;
  createdAtUtc: string;
  modifiedAtUtc?: string | null;
  suiteId?: number | null;
  suiteName?: string | null;
}

export interface CaptureBenchmarkSnapshotRequest {
  sessionId: string;
  name: string;
  notes?: string | null;
  sourceGnollHackVersion?: string | null;
}

export interface SaveAttachedSnapshotRequest {
  sessionId: string;
  name: string;
  notes?: string | null;
  sourceGnollHackVersion?: string | null;
}

export interface UploadBenchmarkSnapshotRequest {
  name: string;
  html: string;
  notes?: string | null;
  sourceGnollHackVersion?: string | null;
}

export interface UpdateBenchmarkGameSnapshotRequest {
  name?: string | null;
  notes?: string | null;
  digestText?: string | null;
  sourceGnollHackVersion?: string | null;
}

export interface CaptureBenchmarkSnapshotResponse {
  board: BenchmarkGameSnapshotDto;
  suite: BenchmarkSuiteDto;
}

export interface StartQuestionGenerationRequest {
  suiteId: number;
  generatorModelConfigurationId: number;
  simpleCount: number;
  intermediateCount: number;
  advancedCount: number;
  instructions?: string | null;
}

export interface QuestionGenerationJobItemDto {
  difficulty: number;
  requestedCount: number;
  generatedCount: number;
  status: string;
  errorMessage?: string | null;
}

export interface QuestionGenerationJobLogEntryDto {
  timestampUtc: string;
  message: string;
  severity: string;
}

export interface QuestionGenerationJobDto {
  id: string;
  suiteId: number;
  suiteName: string;
  generatorConfigId: number;
  generatorDisplayName: string;
  instructions: string;
  status: string;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  promptTokens: number;
  outputTokens: number;
  items: QuestionGenerationJobItemDto[];
  log: QuestionGenerationJobLogEntryDto[];
}

export interface StartRubricCheckRequest {
  suiteId: number;
  checkerModelConfigurationId: number;
  questionIds?: number[] | null;
}

export interface RubricCheckFindingDto {
  claim: string;
  assessment: string;
  boardQuote?: string | null;
  reasoning?: string | null;
}

export interface RubricCheckJobItemDto {
  questionId: number;
  orderIndex: number;
  questionTextExcerpt: string;
  status: string;
  verdict?: string | null;
  findings: RubricCheckFindingDto[];
  errorMessage?: string | null;
}

export interface RubricCheckLogEntryDto {
  timestampUtc: string;
  message: string;
  severity: string;
}

export interface RubricCheckJobDto {
  id: string;
  suiteId: number;
  suiteName: string;
  scope: string;
  checkerConfigId: number;
  checkerDisplayName: string;
  status: string;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  promptTokens: number;
  outputTokens: number;
  items: RubricCheckJobItemDto[];
  log: RubricCheckLogEntryDto[];
}

export interface ReviewBenchmarkQuestionRequest {
  reviewed: boolean;
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
  /**
   * Optional per-run override of the profile's mode. Omitted takes the profile default. Sending
   * Off (0) is an explicit "no second verdict for this run" and drops the assessor from the run,
   * because the mode is inert without an assessor and an assessor is inert under Off.
   */
  secondOpinionMode?: number | null;
  /**
   * Optional. When set, unverified factual claims are verified against the game source code
   * and wiki using read-only tools. Null means no claim verification.
   */
  claimVerifierModelConfigurationId?: number | null;
  scoringProfileId?: number | null;
  acknowledgeSameProvider?: boolean;
  verboseMode?: boolean;
}

/**
 * The assessor of a suite's most recent completed run. The start dialog warns when the selected
 * assessor differs from it: a suite's runs are comparable to each other only while the grader is
 * the same one.
 */
export interface BenchmarkLastAssessorDto {
  runId?: number | null;
  assessorModelConfigurationId?: number | null;
  assessorModelDisplayNameUsed?: string | null;
  assessorModelProviderUsed?: string | null;
  secondOpinionAssessorModelConfigurationId?: number | null;
  secondOpinionAssessorModelDisplayNameUsed?: string | null;
  completedAtUtc?: string | null;
  harnessVersion?: string | null;
  scoringMethodVersion?: number;
}

/**
 * One non-destructive re-grading of a run by an alternative assessor. Admin-only by design: a
 * calibration is an experiment about graders, not a property of the run, so it never reaches the
 * Markdown report where a calibration verdict could be mistaken for a result.
 */
export interface BenchmarkAssessorCalibrationDto {
  id: number;
  benchmarkRunId: number;
  assessorDisplayNameUsed?: string | null;
  assessorProviderUsed?: string | null;
  assessorModelIdUsed?: string | null;
  assessorThinkingLevelUsed?: string | null;
  createdAtUtc: string;
  createdByUserName?: string | null;
  answerCount: number;
  skippedAnswerCount: number;
  /** Mean |calibration - original| quality across graded answers. */
  meanAbsDelta?: number | null;
  /** A gap above 15 points, or a split on criticalError — the live run's own definition. */
  disagreementCount: number;
  inputTokens: number;
  outputTokens: number;
  durationMs: number;
  verdictsJson?: string | null;
  errorMessage?: string | null;
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
  /**
   * The suite question this answer was produced for. Null for a historical answer that could not
   * be matched unambiguously, and null once the question is deleted. Anything merging questions
   * with answers must prefer this over `orderIndex`, which a suite reorder rewrites.
   */
  benchmarkQuestionId?: number | null;
  itemRevisionUsed?: number | null;
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
  toolCallsBlocked?: number | null;
  /** The per-band tool call budget that actually applied to this question. */
  toolCallBudgetUsed?: number | null;
  /** Wall-clock time spent in tool batches during this turn. */
  toolTimeMs?: number | null;
  /** Turn duration with tool I/O removed. This is what speed is scored on. */
  modelTimeMs: number;
  /** Transport artifacts removed before grading, retained verbatim for audit. */
  scrubbedArtifactText?: string | null;
  scrubbedArtifactCount: number;
  // Null for runs before harness version 6, which did not record it: null means
  // "not recorded", not zero.
  narrationBlockCount?: number | null;
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

  /**
   * Why this answer was graded twice: CriticalError, ContestedVerdict, UnverifiedClaims,
   * BelowThreshold, Outlier, All, or Manual for an operator's trial. "Every answer" and "this one
   * looked wrong" are different facts about the same second verdict.
   */
  secondOpinionTrigger?: string | null;

  /** Failure details if the second-opinion call threw or returned no parseable verdict. */
  secondOpinionError?: string | null;

  /** Claims the assessor could neither confirm nor refute. Null for a run graded before the field existed. */
  unverifiedClaimCount?: number | null;
  unverifiedClaimsJson?: string | null;

  /** Claim verification findings from the read-only tool verifier. Advisory: does not alter score. */
  claimVerificationJson?: string | null;
  claimsSupportedCount?: number | null;
  claimsRefutedCount?: number | null;
  claimsIndeterminateCount?: number | null;
  claimVerificationByModelDisplayNameUsed?: string | null;
  claimVerificationInputTokens?: number | null;
  claimVerificationOutputTokens?: number | null;
  claimVerificationDurationMs?: number | null;
  claimVerificationToolCallCount?: number | null;
  claimVerificationError?: string | null;
  claimVerificationRawText?: string | null;

  /** Re-assessment provenance: a published index can move after publication. */
  reassessedAtUtc?: string | null;
  reassessedByModelDisplayNameUsed?: string | null;
  previousQualityScore?: number | null;
  reassessmentCount?: number;
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

  /** Null when the run was started without a claim verifier. */
  claimVerifierModelConfigurationId?: number | null;
  claimVerifierDisplayNameUsed?: string | null;
  claimVerifierProviderUsed?: string | null;
  claimVerifierModelIdUsed?: string | null;
  claimVerifierThinkingLevelUsed?: string | null;
  claimVerifierReasoningModeUsed?: string | null;

  startedByUserId?: string | null;
  startedByUserName?: string | null;
  status: string | number;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  finalScore?: number | null;
  computedScore?: number | null;
  qualityIndex?: number | null;
  rawQualityIndex?: number | null;
  /**
   * The equal-weight mean of the same per-question scores as qualityIndex. Null for runs before
   * harness version 7. The gap between the two is what difficulty weighting did to the headline.
   */
  unweightedQualityIndex?: number | null;
  qualityIndexStandardError?: number | null;
  speedIndex?: number | null;
  totalAnswerDurationMs: number;
  scoringProfileId?: number | null;
  scoringProfileName?: string | null;
  scoringProfileSnapshotJson?: string | null;
  /**
   * The constants this run was scored against, read out of the snapshot **server-side**. Never
   * parse scoringProfileSnapshotJson here: it is a storage format, and a second reader for it in
   * TypeScript is a second thing to keep in step with the profile shape.
   */
  scoringProfileSpeedTargetMs?: number | null;
  scoringProfileSpeedDecayK?: number | null;
  scoringProfileSecondOpinionQualityThreshold?: number | null;
  scoringProfileSecondOpinionOutlierDeltaPoints?: number | null;
  scoringMethodVersion: number;
  harnessVersion?: string | null;
  maxToolCallsPerQuestionUsed?: number | null;
  degradedAnswerCount?: number;
  toolStarvedAnswerCount?: number;
  budgetSaturatedAnswerCount?: number;
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
  /**
   * Answers whose assessor described a fabrication while leaving criticalError false, and answers
   * whose verdict was replaced after the run finished. Both advisory; both overlap the counts
   * above and are never summed with them.
   */
  contestedVerdictAnswerCount?: number;
  unevidencedDeductionAnswerCount?: number;
  omissionAsAccuracyAnswerCount?: number;
  refutedClaimAnswerCount?: number;
  claimVerifiedAnswerCount?: number;
  claimsSupportedCount?: number;
  claimsRefutedCount?: number;
  claimsIndeterminateCount?: number;
  reassessedAnswerCount?: number;
  /** How the second-opinion assessor was used: Off (0), Flagged (1), FlaggedAndOutliers (2), All (3). */
  secondOpinionModeUsed?: number;
  secondOpinionBlindUsed?: boolean;
  /**
   * Grader agreement, interpretable only together with its coverage: a mean delta over
   * trigger-selected answers is conditioned on the first assessor's own uncertainty, while the
   * same figure over every answer is an inter-rater agreement rate.
   */
  secondOpinionGradedAnswerCount?: number;
  secondOpinionMeanAbsDelta?: number | null;
  secondOpinionMeanSignedDelta?: number | null;
  secondOpinionCriticalErrorSplitCount?: number;
  candidatePromptOptionsJson?: string | null;
  candidatePromptSourceUsed?: string | null;
  candidateSystemPromptSha256?: string | null;
  toolGuidesSha256?: string | null;
  knowledgeBaseHeadSha?: string | null;
  secondOpinionDisagreementCount?: number;
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

  /** Claim-verifier usage. */
  totalClaimVerificationInputTokens?: number;
  totalClaimVerificationOutputTokens?: number;
  totalClaimVerificationDurationMs?: number;

  errorMessage?: string | null;

  /**
   * Order indexes whose request is in flight to the provider right now. Answer rows appear
   * only after the model replies, so this is what separates a question being answered from
   * one not yet dispatched. Empty for any run that is not currently executing.
   */
  inFlightOrderIndexes?: number[];

  answers: BenchmarkRunAnswerDto[];
}

// ---------------------------------------------------------------------------------------------
// Suite health. Four read-only reports; nothing here writes a question, a rubric, or a
// difficulty rating, and there is deliberately no endpoint that would.
// ---------------------------------------------------------------------------------------------

export interface BenchmarkItemStatisticsDto {
  questionId: number;
  orderIndex: number;
  questionText: string;
  authoredDifficulty: string | number;
  itemRevision: number;

  /** Sample size and its confounds. Shown together, always. */
  runCount: number;
  distinctModelCount: number;
  distinctAssessorCount: number;
  distinctScoringMethodVersionCount: number;
  /** Answers included whose revision was never recorded. */
  unknownRevisionCount: number;

  meanQuality: number;
  minQuality: number;
  maxQuality: number;
  stdDev: number;

  /**
   * 100 − meanQuality. Reported only: it is never written back into `assessedDifficulty`, which
   * weights the Intelligence Index — deriving the weight from the scores it weights is circular.
   */
  empiricalDifficulty: number;
  assessedDifficulty?: number | null;
  difficultyDelta?: number | null;

  /** Null below four runs, where a top-half/bottom-half split means nothing. */
  discrimination?: number | null;

  meanToolCalls: number;
  budgetBoundFraction: number;

  flags: number;
  flagNames: string[];
  /** A confound fired: every other figure on the row is a mixture, not a measurement. */
  confounded: boolean;
  insufficientData: boolean;
}

export interface BenchmarkSuiteItemAnalysisDto {
  suiteId: number;
  suiteName: string;
  questionCount: number;
  runCount: number;
  distinctModelCount: number;
  distinctAssessorCount: number;
  distinctScoringMethodVersionCount: number;
  linkedAnswerCount: number;
  unlinkedAnswerCount: number;
  minRunsForMeasurement: number;
  minRunsForDiscrimination: number;
  items: BenchmarkItemStatisticsDto[];
}

export interface BenchmarkRubricGapClusterDto {
  questionId: number;
  questionOrderIndex: number;
  /** Verbatim, so a human reads what was said rather than a paraphrase of it. */
  claims: string[];
  modelFamilies: string[];
  modelIds: string[];
  occurrences: number;
  /** 'VerifiedRubricGap', 'LikelyRubricGap' or 'LikelyHallucination'. */
  verdict: string;
}

export interface BenchmarkKnowledgeBaseGapDto {
  claim: string;
  citation?: string | null;
  basis?: string | null;
  questionOrderIndices: number[];
  recurrence: number;
}

export interface BenchmarkRubricGapReportDto {
  suiteId: number;
  runCount: number;
  claimCount: number;
  clusters: BenchmarkRubricGapClusterDto[];
  knowledgeBaseGaps?: BenchmarkKnowledgeBaseGapDto[];
}

export interface BenchmarkCitationDto {
  /** 'SourceFile', 'Symbol' or 'WikiArticle'. */
  kind: string;
  value: string;
  /** 'Resolved', 'Unresolved' or 'NotValidated' — the last meaning "we did not check". */
  status: string;
  /** Parsed and shown, never validated: line numbers drift with every commit. */
  lineNumber?: number | null;
}

export interface BenchmarkQuestionCitationsDto {
  questionId: number;
  orderIndex: number;
  citations: BenchmarkCitationDto[];
  unresolvedCount: number;
  notValidatedCount: number;
  hasNoCitations: boolean;
}

export interface BenchmarkCitationReportDto {
  suiteId: number;
  unresolvedCount: number;
  notValidatedCount: number;
  /** False while the source index is still building, which makes an unresolved result unreliable. */
  sourceIndexReady: boolean;
  questions: BenchmarkQuestionCitationsDto[];
}

export interface BenchmarkCoverageGapDto {
  subsystem: string;
  /** Required. A gap with no location is discarded before it reaches the client. */
  sourceLocation: string;
  rationale?: string | null;
  suggestedBand?: string | null;
}

/**
 * A read-only coverage report. Nothing is written into the suite, and no endpoint exists that
 * would: a generated draft is edited and approved by a human before it becomes a question.
 */
export interface BenchmarkCoverageReportDto {
  suiteId: number;
  suiteName: string;
  questionCount: number;
  analysisModelConfigurationId: number;
  analysisModelDisplayNameUsed?: string | null;
  analysisModelProviderUsed?: string | null;
  analysisModelIdUsed?: string | null;
  analysisModelThinkingLevelUsed?: string | null;
  analyzedAtUtc: string;
  inputTokens: number;
  outputTokens: number;
  durationMs: number;
  gaps: BenchmarkCoverageGapDto[];
  comment?: string | null;
  errorMessage?: string | null;
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
  qualityIndexStandardError?: number | null;
  rawQualityIndex?: number | null;
  speedIndex?: number | null;
  totalAnswerDurationMs: number;
  speedMeasurementDegraded: boolean;
  answeredQuestionCount: number;
  totalQuestionCount: number;
  degradedAnswerCount?: number;
  toolStarvedAnswerCount?: number;
  budgetSaturatedAnswerCount?: number;
  secondOpinionBlindUsed?: boolean;
  secondOpinionMeanSignedDelta?: number | null;
  secondOpinionCriticalErrorSplitCount?: number;
  candidatePromptOptionsJson?: string | null;
  candidatePromptSourceUsed?: string | null;
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

  /**
   * Replaces an answer's verdict and recomputes the run's indices. This changes a published
   * score, which is why the trial below is a separate call rather than a flag on this one at the
   * call site.
   */
  reassessAnswer(runId: number, answerId: number, assessorModelConfigurationId?: number | null): Observable<{ runId: number }> {
    return this.http.post<{ runId: number }>(`/api/admin/benchmark/runs/${runId}/answers/${answerId}/reassess`, { assessorModelConfigurationId });
  }

  /**
   * Records a prospective assessor's verdict in the second-opinion slot and changes no score,
   * level, flag or index. The mode for comparing a candidate assessor against the one in use.
   *
   * `replaceExistingSecondOpinion` is required to overwrite an automatic second opinion, which is
   * run evidence: an experiment must not erase evidence by accident.
   */
  trialReassessAnswer(
    runId: number,
    answerId: number,
    assessorModelConfigurationId?: number | null,
    replaceExistingSecondOpinion = false
  ): Observable<{ runId: number }> {
    return this.http.post<{ runId: number }>(
      `/api/admin/benchmark/runs/${runId}/answers/${answerId}/reassess`,
      { assessorModelConfigurationId, trial: true, replaceExistingSecondOpinion });
  }

  /** Grades every answer of a run with another model and records only the agreement statistics. */
  calibrateAssessor(runId: number, assessorModelConfigurationId: number): Observable<BenchmarkAssessorCalibrationDto> {
    return this.http.post<BenchmarkAssessorCalibrationDto>(
      `/api/admin/benchmark/runs/${runId}/calibrate`, { assessorModelConfigurationId });
  }

  getCalibrations(runId: number): Observable<BenchmarkAssessorCalibrationDto[]> {
    return this.http.get<BenchmarkAssessorCalibrationDto[]>(`/api/admin/benchmark/runs/${runId}/calibrations`);
  }

  getLastAssessor(suiteId: number): Observable<BenchmarkLastAssessorDto> {
    return this.http.get<BenchmarkLastAssessorDto>(`/api/admin/benchmark/suites/${suiteId}/last-assessor`);
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

  retryClaimVerification(runId: number, assessorModelConfigurationId?: number | null): Observable<{ runId: number }> {
    return this.http.post<{ runId: number }>(`/api/admin/benchmark/runs/${runId}/retry-claim-verification`, { assessorModelConfigurationId });
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

  // Suite health. All four are read-only reports; only the coverage analysis calls a model.
  getItemAnalysis(suiteId: number): Observable<BenchmarkSuiteItemAnalysisDto> {
    return this.http.get<BenchmarkSuiteItemAnalysisDto>(`/api/admin/benchmark/suites/${suiteId}/item-analysis`);
  }

  getRubricGaps(suiteId: number): Observable<BenchmarkRubricGapReportDto> {
    return this.http.get<BenchmarkRubricGapReportDto>(`/api/admin/benchmark/suites/${suiteId}/rubric-gaps`);
  }

  /** A POST because it walks the whole source index, which is work rather than a lookup. */
  validateCitations(suiteId: number): Observable<BenchmarkCitationReportDto> {
    return this.http.post<BenchmarkCitationReportDto>(`/api/admin/benchmark/suites/${suiteId}/validate-citations`, {});
  }

  analyzeCoverage(suiteId: number, analysisModelConfigurationId: number): Observable<BenchmarkCoverageReportDto> {
    return this.http.post<BenchmarkCoverageReportDto>(
      `/api/admin/benchmark/suites/${suiteId}/coverage-analysis`, { analysisModelConfigurationId });
  }

  getSuiteRunsFootprint(suiteId: number): Observable<BenchmarkFootprintDto> {
    return this.http.get<BenchmarkFootprintDto>(`/api/admin/benchmark/suites/${suiteId}/runs/footprint`);
  }

  deleteSuiteRuns(suiteId: number): Observable<{ deletedCount: number }> {
    return this.http.delete<{ deletedCount: number }>(`/api/admin/benchmark/suites/${suiteId}/runs`);
  }

  // Game Snapshots
  captureSnapshot(req: CaptureBenchmarkSnapshotRequest): Observable<CaptureBenchmarkSnapshotResponse> {
    return this.http.post<CaptureBenchmarkSnapshotResponse>('/api/admin/benchmark/snapshots/capture', req);
  }

  saveAttachedSnapshot(req: SaveAttachedSnapshotRequest): Observable<CaptureBenchmarkSnapshotResponse> {
    return this.http.post<CaptureBenchmarkSnapshotResponse>('/api/admin/benchmark/snapshots/from-session', req);
  }

  uploadSnapshot(req: UploadBenchmarkSnapshotRequest): Observable<CaptureBenchmarkSnapshotResponse> {
    return this.http.post<CaptureBenchmarkSnapshotResponse>('/api/admin/benchmark/snapshots', req);
  }

  getSnapshots(): Observable<BenchmarkGameSnapshotDto[]> {
    return this.http.get<BenchmarkGameSnapshotDto[]>('/api/admin/benchmark/snapshots');
  }

  getSnapshot(id: number, includeText: boolean = false): Observable<BenchmarkGameSnapshotDto> {
    return this.http.get<BenchmarkGameSnapshotDto>(`/api/admin/benchmark/snapshots/${id}`, {
      params: { includeText: includeText.toString() }
    });
  }

  downloadSnapshotText(id: number): Observable<Blob> {
    return this.http.get(`/api/admin/benchmark/snapshots/${id}/text`, { responseType: 'blob' });
  }

  getSnapshotTextUrl(id: number): string {
    return `/api/admin/benchmark/snapshots/${id}/text`;
  }

  updateSnapshot(id: number, req: UpdateBenchmarkGameSnapshotRequest): Observable<BenchmarkGameSnapshotDto> {
    return this.http.put<BenchmarkGameSnapshotDto>(`/api/admin/benchmark/snapshots/${id}`, req);
  }

  deleteSnapshot(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/benchmark/snapshots/${id}`);
  }

  // Question Generation
  startQuestionGeneration(req: StartQuestionGenerationRequest): Observable<{ jobId: string }> {
    return this.http.post<{ jobId: string }>('/api/admin/benchmark/question-generations', req);
  }

  getQuestionGeneration(jobId: string): Observable<QuestionGenerationJobDto> {
    return this.http.get<QuestionGenerationJobDto>(`/api/admin/benchmark/question-generations/${jobId}`);
  }

  getActiveQuestionGeneration(): Observable<QuestionGenerationJobDto | null> {
    return this.http.get<QuestionGenerationJobDto | null>('/api/admin/benchmark/question-generations/active');
  }

  cancelQuestionGeneration(jobId: string): Observable<{ cancelled: boolean }> {
    return this.http.post<{ cancelled: boolean }>(`/api/admin/benchmark/question-generations/${jobId}/cancel`, {});
  }

  // Rubric Checks
  startRubricCheck(req: StartRubricCheckRequest): Observable<{ jobId: string }> {
    return this.http.post<{ jobId: string }>('/api/admin/benchmark/rubric-checks', req);
  }

  getRubricCheck(jobId: string): Observable<RubricCheckJobDto> {
    return this.http.get<RubricCheckJobDto>(`/api/admin/benchmark/rubric-checks/${jobId}`);
  }

  getActiveRubricCheck(): Observable<RubricCheckJobDto | null> {
    return this.http.get<RubricCheckJobDto | null>('/api/admin/benchmark/rubric-checks/active');
  }

  cancelRubricCheck(jobId: string): Observable<{ cancelled: boolean }> {
    return this.http.post<{ cancelled: boolean }>(`/api/admin/benchmark/rubric-checks/${jobId}/cancel`, {});
  }

  // Question Review
  reviewQuestion(id: number, reviewed: boolean): Observable<BenchmarkQuestionDto> {
    return this.http.post<BenchmarkQuestionDto>(`/api/admin/benchmark/questions/${id}/review`, { reviewed });
  }

  reviewAllQuestions(suiteId: number): Observable<{ reviewedCount: number, suite: BenchmarkSuiteDto }> {
    return this.http.post<{ reviewedCount: number, suite: BenchmarkSuiteDto }>(`/api/admin/benchmark/suites/${suiteId}/review-all`, {});
  }
}
