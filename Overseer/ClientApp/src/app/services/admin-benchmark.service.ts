import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

// The comparison wire contract lives beside the view that renders it, so both consumers read one
// declaration. The reverse edge in that file is an `import type`, which TypeScript erases, so the
// two files carry no runtime cycle.
import {
  BenchmarkComparabilityIndexDto,
  BenchmarkComparabilityIndexQuery,
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonQuery,
  MODEL_COMPARABILITY_INDEX_ENDPOINT,
  MODEL_COMPARISON_ENDPOINT,
  comparabilityIndexQueryParams,
  modelComparisonQueryParams
} from '../admin/benchmark/model-comparison/model-comparison.models';

export type {
  BenchmarkComparabilityIndexDto,
  BenchmarkComparabilityIndexEntryDto,
  BenchmarkComparabilityConditionDto,
  BenchmarkComparabilityIndexQuery,
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonQuery,
  BenchmarkModelComparisonPricingBasis
} from '../admin/benchmark/model-comparison/model-comparison.models';

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
  All = 3,
  /**
   * Flagged, plus a deterministic top-up to the profile's minimum sample. Yields a
   * conditioned-plus-sample agreement rate, which is still not the unbiased rate All gives.
   */
  FlaggedPlusSample = 4
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
    value: BenchmarkSecondOpinionMode.FlaggedPlusSample,
    label: 'Flagged answers plus a sample',
    hint: "Flagged answers, topped up to the profile's minimum sample by taking the lowest-scoring "
      + 'answers first. Deterministic: the same data selects the same answers every run.'
  },
  {
    value: BenchmarkSecondOpinionMode.All,
    label: 'Every answer (double grading)',
    hint: 'Recommended. The only setting that measures grader agreement rather than sampling it.'
  }
];

export interface ModelPricingDto {
  inputPerMillion: number;
  outputPerMillion: number;
  cachedInputPerMillion?: number | null;
  cacheWritePerMillion?: number | null;
  asOf?: string | null;
}

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
  onlyUnassessed?: boolean;
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
  /** The default-suite catalog key this suite was imported from. Null for a custom suite, and for a suite imported before this field existed. */
  defaultSuiteKey?: string | null;
}

export interface CreateBenchmarkSuiteRequest {
  name: string;
  description?: string | null;
}

export interface UpdateBenchmarkSuiteRequest {
  name: string;
  description?: string | null;
}

/**
 * One `*.json` file found under the server's default-suite directory. `key` and `version` are
 * null only when `error` is set: an invalid file is listed with its error and cannot be
 * imported, never thrown. Invalid entries have no stable key, so the template tracks and
 * identifies them by `fileName` instead.
 */
export interface DefaultSuiteCatalogEntryDto {
  key: string | null;
  version: number | null;
  name: string;
  description: string | null;
  questionCount: number;
  /** Band name ("Simple" | "Intermediate" | "Advanced") to question count. */
  difficultyCounts: { [band: string]: number };
  fileName: string;
  error: string | null;
  alreadyImportedCount: number;
  alreadyImportedNames: string[];
  /**
   * Suites with no recorded `defaultSuiteKey` whose name equals this file's name — the
   * pre-key-column fallback for "this looks like it was already imported". Shown under
   * "possibly imported earlier (matched by name)".
   */
  nameMatchedSuiteNames: string[];
}

export interface ImportDefaultSuitesResultDto {
  imported: BenchmarkSuiteDto[];
  skipped: { key: string; reason: string }[];
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
  /**
   * How many times to execute this identical request, strictly one at a time. 1 (the default)
   * is the single-run path exactly as before multi-run existed: no series row, no auto-created
   * group. Bounded by the live configured MaxRunsPerDay, which the server re-checks; the field's
   * own max is a courtesy.
   */
  runCount?: number;
  /**
   * On a cap denial: true pauses the series in WaitingForCap and retries; false stops it with
   * StopReason = RunCapReached, keeping every completed member and leaving it resumable.
   */
  allowCapWait?: boolean;
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

/**
 * One tool call a candidate model attempted while producing an answer, ordered by
 * `sortOrder` across the whole turn. Unlike `toolCallSummary` and the per-tool usage tally,
 * which only ever list calls that succeeded, this is the full population: a call the tool
 * layer refused or that errored — most commonly a JSON result over the result cap — appears
 * here and nowhere else.
 */
export interface BenchmarkToolCallDto {
  id: number;
  /** Emission order across the whole turn, 0-based and dense. */
  sortOrder: number;
  /** The tool round this call belongs to. */
  iterationIndex: number | null;
  name: string | null;
  toolCallId: string | null;
  /** `'completed'` (case-insensitive) means the call succeeded; anything else did not. */
  status: string | null;
  argsText: string | null;
  result: string | null;
  error: string | null;
  queueWaitMs: number | null;
  executionMs: number | null;
  depth: number;
  agentName: string | null;
  /** Whether the stored `argsText` was cut short; the call's actual arguments may be longer. */
  argsTruncated: boolean;
  /**
   * Whether the stored `result` was cut short. The true size survived the cut and is in
   * `resultLengthChars`, so a truncated result is never a "call returned nothing".
   */
  resultTruncated: boolean;
  /**
   * The tool's true result length in characters, before any cap — 0 only when the call
   * genuinely produced no result. Server-side retention nulls `argsText` and `result` after
   * 90 days but never this figure, so a null `result` next to a non-zero `resultLengthChars`
   * means the payload was pruned by age, not that the call returned nothing: those are
   * opposite facts and must not be conflated.
   */
  resultLengthChars: number;
}

export interface BenchmarkRunAnswerDto {
  id: number;
  benchmarkRunId: number;
  /**
   * This answer's input tokens as a share of the run total, 0-100. Computed server-side from the
   * stored per-answer tokens and the run total, never a stored column. Input-token cost is driven
   * by model-call count rather than tool-call count, and this is what makes that visible.
   */
  inputTokenShare?: number | null;
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
  /** Null when the candidate failed terminally at the provider; the failure is carried by errorMessage / httpStatusCode / providerErrorDetail instead. */
  answerText: string | null;
  thoughtText?: string | null;
  status: string | number;
  assessmentStatus?: string | number;
  assessmentError?: string | null;
  errorMessage?: string | null;
  httpStatusCode?: number | null;
  /** The provider's own error payload, verbatim, when the call failed terminally. */
  providerErrorDetail?: string | null;
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
  /**
   * Per-call outcome tallies from the per-tool-call record. Null means "not recorded" — the
   * answer predates the per-call table — and never zero: a legacy answer's failed and refused
   * calls are unknown, not absent, so the UI must not print a "0" that asserts otherwise.
   */
  toolCallsSucceeded?: number | null;
  toolCallsFailed?: number | null;
  toolCallsRefused?: number | null;
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
  /**
   * The provider's own reason for ending the turn, verbatim — `STOP`, `MAX_TOKENS`, `end_turn` and so
   * on. Null where the provider reported none, which for an empty answer means the transport failed
   * rather than the model choosing to stop.
   */
  providerFinishReason?: string | null;
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
  /** The run stopped before finishing its suite. Decided by the server (BenchmarkRunFinalizer.IsAbortedRun). */
  isAborted?: boolean;
  benchmarkSuiteId?: number | null;
  suiteName: string;
  /** The default-suite catalog key the suite carried when this run was launched. Null for a custom suite, and for a run recorded before harness 24. */
  defaultSuiteKeyUsed?: string | null;
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
   * Answers that failed terminally at the provider (no text returned, carried by errorMessage /
   * httpStatusCode / providerErrorDetail on the answer). Null on a run finalised before this was
   * recorded. Any run with a nonzero count here has null qualityIndex, qualityIndexStandardError,
   * unweightedQualityIndex and speedIndex: the indexes are withheld rather than computed over the
   * surviving questions only.
   */
  terminalFailureAnswerCount?: number | null;
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
  /**
   * Answers whose critical-error quote the claim verifier supported against the source code/wiki.
   * Advisory: the quality cap stands and no index moved. Absent on a run before harness 19.
   */
  contestedCriticalErrorAnswerCount?: number | null;
  /**
   * Answers whose out-of-rubric Accuracy deduction rests on a statement the claim verifier refuted
   * against the source code/wiki. Advisory: the deduction stands and no index moved. Null on a run
   * before harness 20, which never adjudicated it: "not recorded", never 0.
   */
  contestedAccuracyDeductionAnswerCount?: number | null;
  /**
   * Answers where one grading dimension came back at level 1 or below while the other three were 3
   * or above, with no defect of that kind named. Advisory: no index moved, and the verdict is routed
   * to a second reader. Null on a run before harness 27, which never looked for it.
   */
  dimensionOutlierAnswerCount?: number | null;
  /** Answers whose assessor marked an Accuracy deduction 'Not in rubric:'. Advisory. */
  outOfRubricAccuracyAnswerCount?: number;
  /** Answers opening with a claim of sufficiency. Advisory, and the text was not removed. */
  answerFramingOpenerAnswerCount?: number;
  claimVerifiedAnswerCount?: number;
  claimsSupportedCount?: number;
  claimsRefutedCount?: number;
  claimsIndeterminateCount?: number;
  reassessedAnswerCount?: number;
  /**
   * How the second-opinion assessor was used: Off (0), Flagged (1), FlaggedAndOutliers (2),
   * All (3), FlaggedPlusSample (4).
   */
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
  wikiHeadSha?: string | null;
  sourceCodeHeadSha?: string | null;
  /**
   * H3. Run-wide tool calls by family, classified once on the server by BenchmarkChatTransfer.ClassifyTool.
   * Undefined on a run detail served before this existed; the diagnostics builder keeps its own loop only
   * as a fallback for those.
   */
  toolFamilyCounts?: { [family: string]: number } | null;
  zeroKnowledgeBaseAnswerCount?: number | null;
  secondOpinionDisagreementCount?: number;
  toolOverheadMs?: number | null;
  difficultyFallbackUsed: boolean;
  speedMeasurementDegraded: boolean;
  maxParallelQuestionsUsed: number;
  answeredQuestionCount: number;
  totalQuestionCount: number;
  /**
   * Questions the model ended its turn on without producing text. Each scores 0 under scoring
   * method 10 rather than being excluded, so this is a count of failures, not of missing data.
   */
  unansweredQuestionCount: number;
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

  /**
   * Per-question assessment usage, kept apart from the candidate totals above. Excludes the
   * final synthesis, which is a peer role with its own totals below.
   */
  totalAssessmentInputTokens?: number;
  totalAssessmentOutputTokens?: number;
  totalAssessmentCacheReadTokens?: number;
  totalAssessmentCacheCreationTokens?: number;
  totalAssessmentDurationMs?: number;

  /** Second-opinion assessor usage. */
  totalSecondOpinionInputTokens?: number;
  totalSecondOpinionOutputTokens?: number;
  totalSecondOpinionCacheReadTokens?: number;
  totalSecondOpinionCacheCreationTokens?: number;
  totalSecondOpinionDurationMs?: number;

  /** Claim-verifier usage. */
  totalClaimVerificationInputTokens?: number;
  totalClaimVerificationOutputTokens?: number;
  totalClaimVerificationCacheReadTokens?: number;
  totalClaimVerificationCacheCreationTokens?: number;
  totalClaimVerificationDurationMs?: number;

  /** Final synthesis usage — the single whole-run assessment that closes a run. */
  totalSynthesisInputTokens?: number;
  totalSynthesisOutputTokens?: number;
  totalSynthesisDurationMs?: number;

  /**
   * Completeness deductions the assessor itself placed outside the question's scope. This is
   * the instrument's measurable share of the Accuracy-to-Completeness gap: if Completeness rises
   * by more than this count can explain, the scope rule changed grader behaviour beyond its remit.
   */
  completenessOutOfScopeCount?: number;

  /**
   * The subset of the count above whose marked point sits beside a docked Completeness level with
   * no in-scope defect named — recorded as out of scope and seemingly deducted for anyway. Zero on
   * a run graded before the detector existed, which reads the same as an assessor that complied.
   */
  completenessOutOfScopeDeductedCount?: number;

  /**
   * Rubric format suggestions the assessor recorded rather than deducting Readability for. The
   * Readability counterpart of the count above: if Readability rises by more than this count can
   * explain, the FORM rule changed grader behaviour beyond its remit.
   */
  readabilityFormOnlyCount?: number;

  /** The Readability counterpart of `completenessOutOfScopeDeductedCount`. Counted, not flagged. */
  readabilityFormOnlyDeductedCount?: number;

  /**
   * Answers actually graded twice by the deterministic top-up under FlaggedPlusSample. May fall
   * short of the profile's target when fewer answers exist. Zero and meaningless under every
   * other mode.
   */
  secondOpinionSampleCountUsed?: number;

  /**
   * Claims the verifier actually checked. With claimsRefutedCount above and the verifier's own
   * cost, this is the yield line: what the verification spend bought.
   */
  claimsCheckedCount?: number;

  errorMessage?: string | null;

  /**
   * Order indexes whose request is in flight to the provider right now. Answer rows appear
   * only after the model replies, so this is what separates a question being answered from
   * one not yet dispatched. Empty for any run that is not currently executing.
   */
  inFlightOrderIndexes?: number[];

  /**
   * Order indexes the current failed-question re-run is scoped to. Same contract as
   * inFlightOrderIndexes: empty unless this process is executing a re-run right now.
   */
  rerunScopeOrderIndexes?: number[];

  /**
   * Order indexes the current re-run has produced an answer or a score for. Same contract as
   * inFlightOrderIndexes: empty unless this process is executing a re-run right now.
   */
  rerunAnsweredOrderIndexes?: number[];
  rerunScoredOrderIndexes?: number[];

  /**
   * Which run-level stage is executing — 'Answering', 'Verifying', 'SecondOpinion',
   * 'Synthesizing' or 'Terminal'. Absent for any run this process is not executing, including
   * a run whose process restarted mid-run; the client then derives a stage from the answer rows.
   */
  stage?: string | null;

  /**
   * Order indexes currently with the claim verifier and the second-opinion assessor. Same
   * contract as inFlightOrderIndexes; without these a row being re-graded reads as finished,
   * because it already carries a score.
   */
  inFlightVerificationOrderIndexes?: number[];
  inFlightSecondOpinionOrderIndexes?: number[];

  /**
   * The instrument the most recent failed-question re-run executed under. Non-null only on a run
   * that was re-run; when either differs from the run's own fingerprint, the run's answers were
   * not all produced under one instrument.
   */
  rerunCandidateSystemPromptSha256?: string | null;
  rerunToolGuidesSha256?: string | null;
  rerunHarnessVersion?: string | null;
  rerunStartedAtUtc?: string | null;
  rerunCompletedAtUtc?: string | null;

  estimatedCost?: number | null;
  estimatedCandidateCost?: number | null;
  /** The per-question assessments only. The final synthesis is a peer role, costed separately. */
  estimatedAssessorCost?: number | null;
  estimatedSecondOpinionCost?: number | null;
  estimatedVerifierCost?: number | null;
  estimatedSynthesisCost?: number | null;
  /** Assessor, second opinion, claim verifier and synthesis together — the whole grading side. */
  estimatedGradingCost?: number | null;
  pricingSource?: string | null;
  pricingIncomplete?: boolean;

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
  /** The run stopped before finishing its suite. Decided by the server (BenchmarkRunFinalizer.IsAbortedRun). */
  isAborted?: boolean;
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
  /**
   * Questions the model ended its turn on without producing text. Each scores 0 under scoring
   * method 10 rather than being excluded, so this is a count of failures, not of missing data.
   */
  unansweredQuestionCount: number;
  degradedAnswerCount?: number;
  toolStarvedAnswerCount?: number;
  budgetSaturatedAnswerCount?: number;
  /**
   * Answers that failed terminally at the provider. Null on a run finalised before this was
   * recorded. Any run with a nonzero count here has null qualityIndex, qualityIndexStandardError
   * and speedIndex, withheld rather than computed over the surviving questions only.
   */
  terminalFailureAnswerCount?: number | null;
  secondOpinionBlindUsed?: boolean;
  secondOpinionMeanSignedDelta?: number | null;
  secondOpinionCriticalErrorSplitCount?: number;
  candidatePromptOptionsJson?: string | null;
  candidatePromptSourceUsed?: string | null;
  harnessVersion?: string | null;
  totalDurationMs: number;
  /**
   * The five instrument fingerprints. Two runs form a reproduction only if the candidate prompt, the
   * tool guides and the knowledge base all match; the GnollHack wiki and source-code Git HEADs are
   * provenance rather than comparability keys. The run list uses them to badge a run whose instrument
   * moved since the previous run of the same suite. Null on runs recorded before each hash was
   * captured, which is "not recorded", never "unchanged".
   */
  candidateSystemPromptSha256?: string | null;
  toolGuidesSha256?: string | null;
  knowledgeBaseHeadSha?: string | null;
  wikiHeadSha?: string | null;
  sourceCodeHeadSha?: string | null;
  estimatedCost?: number | null;
  /** The candidate model's own share of estimatedCost — the figure the history table's primary cost line shows. */
  estimatedCandidateCost?: number | null;
  pricingIncomplete?: boolean;
}

// =========================================================================================
// Multi-run: limits, series, groups and group analysis
// =========================================================================================

/**
 * The run caps and the live rolling-window counts behind them.
 *
 * Both windows are **rolling** — the last 60 minutes and the last 24 hours — not calendar hours
 * or calendar days. The client must never re-derive them: the server's compliance guard owns the
 * arithmetic, and a field that bounded itself by its own idea of "today" would disagree with the
 * guard that actually refuses the run.
 */
export interface BenchmarkRunLimitsDto {
  maxRunsPerHour: number;
  maxRunsPerDay: number;
  runsInLastHour: number;
  runsInLast24Hours: number;
  /** Never negative, even after the cap is lowered below the current count. */
  remainingDailyHeadroom: number;
  /** The ceiling for the Number of runs field. Equals maxRunsPerDay, not the current headroom. */
  maxRunCountPerSeries: number;
}

export type BenchmarkRunSeriesStatus =
  | 'Pending'
  | 'Running'
  | 'WaitingForCap'
  | 'Stopped'
  | 'Completed'
  | 'CompletedWithErrors'
  | 'Cancelled'
  | 'Failed';

export type BenchmarkRunSeriesStopReason = 'MemberFailed' | 'RunCapReached' | 'SpendDenied';

export interface BenchmarkRunSeriesMemberDto {
  index: number;
  runId: number;
  status: string;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  qualityIndex?: number | null;
  speedIndex?: number | null;
  estimatedCost?: number | null;
  durationMs?: number | null;
  answeredQuestionCount: number;
  totalQuestionCount: number;
  shortFingerprint?: string | null;
}

export interface BenchmarkRunSeriesDto {
  id: number;
  benchmarkSuiteId?: number | null;
  suiteName: string;
  requestedRunCount: number;
  completedRunCount: number;
  failedRunCount: number;
  status: BenchmarkRunSeriesStatus;
  stopReason?: BenchmarkRunSeriesStopReason | null;
  /** The stop reason in words, for the Continue button's label. */
  stopReasonText?: string | null;
  allowCapWait: boolean;
  /** True when the Continue button should be offered. Cancelled, Completed and Failed are never resumable. */
  resumable: boolean;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  errorMessage?: string | null;

  /** The instrument as it was at member 1, and as it is now. A refused resume is self-explaining from these. */
  firstMemberCandidateSystemPromptSha256?: string | null;
  firstMemberToolGuidesSha256?: string | null;
  firstMemberKnowledgeBaseHeadSha?: string | null;
  firstMemberWikiHeadSha?: string | null;
  firstMemberSourceCodeHeadSha?: string | null;
  currentCandidateSystemPromptSha256?: string | null;
  currentToolGuidesSha256?: string | null;
  currentKnowledgeBaseHeadSha?: string | null;
  currentWikiHeadSha?: string | null;
  currentSourceCodeHeadSha?: string | null;
  /** Which of the five moved. Empty when the instrument has not changed. */
  changedInstrumentHashes: string[];
  instrumentChangeAcknowledged: boolean;

  autoCreatedGroupId?: number | null;
  autoCreatedGroupTier?: string | null;

  members: BenchmarkRunSeriesMemberDto[];
}

export interface ResumeBenchmarkRunSeriesRequest {
  /**
   * Continues over a changed instrument. The resulting group is then Tier C and can never be
   * pooled into one index — the dangerous case made impossible by construction rather than by
   * discipline.
   */
  acknowledgeInstrumentChange?: boolean;
}

/** The 409 body a refused resume returns, so the dialog can name the hash that moved. */
export interface BenchmarkInstrumentChangedDto {
  instrumentChanged: true;
  seriesId?: number | null;
  changedHashes: string[];
  message: string;
}

/**
 * How comparable a set of runs is, and therefore what may be computed over it. Higher is more
 * comparable; only `Replicate` may be pooled into one index.
 */
export type BenchmarkComparabilityTier =
  | 'NotComparable'
  | 'CrossCondition'
  | 'QualityComparable'
  | 'Replicate';

export interface BenchmarkComparabilityVariantDto {
  value: string;
  runIds: number[];
}

export interface BenchmarkComparabilityDifferenceDto {
  name: string;
  kind: string;
  description: string;
  variants: BenchmarkComparabilityVariantDto[];
}

export interface BenchmarkComparabilityResultDto {
  tier: BenchmarkComparabilityTier;
  tierLabel: string;
  poolingPermitted: boolean;
  speedAggregatesDegraded: boolean;
  costAggregatesDegraded: boolean;
  explanation: string;
  comparabilityKeyHash: string;
  matchedKeys: string[];
  differences: BenchmarkComparabilityDifferenceDto[];
  runIds: number[];
}

export interface BenchmarkRunGroupMemberDto {
  runId: number;
  startedAtUtc: string;
  status: string;
  qualityIndex?: number | null;
  speedIndex?: number | null;
  testedModelDisplayName?: string | null;
  shortFingerprint?: string | null;
  addedAtUtc: string;
}

export interface BenchmarkRunGroupDto {
  id: number;
  name: string;
  benchmarkSuiteId?: number | null;
  suiteName?: string | null;
  tier: BenchmarkComparabilityTier;
  tierLabel: string;
  comparabilityKeyHash?: string | null;
  crossCondition: boolean;
  notes?: string | null;
  createdFromSeriesId?: number | null;
  createdAtUtc: string;
  modifiedAtUtc: string;
  runCount: number;
  members: BenchmarkRunGroupMemberDto[];
  /** Null until an analysis has been computed. The report download stays disabled until then. */
  latestAnalysisId?: number | null;
  latestAnalysisAtUtc?: string | null;
  /** The membership changed after the last analysis. Badged, not discarded: a stale analysis is not wrong. */
  analysisStale: boolean;
}

export interface CreateBenchmarkRunGroupRequest {
  name: string;
  runIds: number[];
  notes?: string | null;
  /** Required to persist a Tier C group. Without it a cross-condition set is refused. */
  crossCondition?: boolean;
}

export interface UpdateBenchmarkRunGroupRequest {
  name?: string | null;
  runIds?: number[] | null;
  notes?: string | null;
  crossCondition?: boolean | null;
}

/**
 * The result of previewing, creating or editing a group. On refusal it carries the keys that
 * differ and the runs carrying them — a "no" with no reason is unusable in the group builder.
 */
export interface BenchmarkRunGroupTierPreviewDto {
  accepted: boolean;
  error?: string | null;
  comparability?: BenchmarkComparabilityResultDto | null;
  group?: BenchmarkRunGroupDto | null;
}

export interface BenchmarkGroupAnalysisRequest {
  /** Optional baseline group to pair this one against. Null computes the group alone. */
  compareWithGroupId?: number | null;
}

/** Per-item statistics across the group's R runs. */
export interface BenchmarkGroupItemStatisticsDto {
  questionId: number;
  orderIndex: number;
  questionText?: string | null;
  observationCount: number;
  /**
   * The item's per-run quality scores, positionally aligned with `runIds`. Without these an
   * unstable item can be seen to swing and never traced to the run that produced the low score.
   */
  scores: number[];
  /** The runs those scores came from, in ascending run-id order. */
  runIds: number[];
  mean: number;
  median: number;
  /** Sample SD (n-1). Null when fewer than two observations exist. */
  standardDeviation?: number | null;
  min: number;
  max: number;
  interquartileRange?: number | null;
  coefficientOfVariation?: number | null;
  confidenceIntervalHalfWidth?: number | null;
  /** k/R — how often this item produced a critical error. */
  criticalErrorRate: number;
  /** SD above the configured threshold: the items where one run's verdict is least trustworthy. */
  unstable: boolean;
}

export interface BenchmarkGroupIndexStatisticsDto {
  /** The mean of the R per-run indices. */
  point: number;
  runIndices: number[];
  /** SD(run indices) / sqrt(R). Answers "would a re-run move this?" and shrinks with R. */
  reproducibilityStandardError?: number | null;
  reproducibilityStandardDeviation?: number | null;
  /**
   * The chi-square 95 % interval on the reproducibility SD itself. At R = 3 the upper bound is over
   * six times the point estimate, so two groups' SDs are not comparable numbers and any surface
   * showing one must show the interval beside it. Null below three runs and above df 19.
   */
  reproducibilitySdLower?: number | null;
  reproducibilitySdUpper?: number | null;
  /**
   * Answers "would a different 18 questions move this?" and does **not** shrink with R, because
   * every run uses the same items. The UI must say so, or a reader will report it as a bug.
   */
  itemSamplingStandardError?: number | null;
  combinedIntervalHalfWidth?: number | null;
  /** R < 3 yields no reproducibility figure, mirroring the existing n < 3 convention. */
  reproducibilityReportable: boolean;
}

/**
 * A persisted multi-run analysis. `result` and `comparison` are the server's statistics records
 * passed through rather than mirrored field by field, so the two sides cannot drift.
 */
export interface BenchmarkGroupAnalysisDto {
  id: number;
  groupId: number;
  groupName: string;
  computedAtUtc: string;
  runCount: number;
  memberRunIds: number[];
  tier: BenchmarkComparabilityTier;
  tierLabel: string;
  harnessVersion?: string | null;
  scoringMethodVersion: number;
  stale: boolean;
  comparedWithGroupId?: number | null;
  comparedWithGroupName?: string | null;
  result?: any;
  comparison?: any;
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

  /** Every `*.json` file under the server's default-suite directory, valid or not. */
  getDefaultSuiteCatalog(): Observable<DefaultSuiteCatalogEntryDto[]> {
    return this.http.get<DefaultSuiteCatalogEntryDto[]>('/api/admin/benchmark/suites/default-catalog');
  }

  /** Imports the named catalog entries. Never overwrites an existing suite — a repeat import creates a numbered copy. */
  importDefaultSuites(keys: string[]): Observable<ImportDefaultSuitesResultDto> {
    return this.http.post<ImportDefaultSuitesResultDto>('/api/admin/benchmark/suites/import-default', { keys });
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

  /** The full per-call tool record for one answer, ordered by `sortOrder`. Loaded lazily — see the run detail dialog's tool-call disclosure. */
  getAnswerToolCalls(runId: number, answerId: number): Observable<BenchmarkToolCallDto[]> {
    return this.http.get<BenchmarkToolCallDto[]>(`/api/admin/benchmark/runs/${runId}/answers/${answerId}/tool-calls`);
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

  /** Beside getRunReportUrl, and used the same way: window.open, not an XHR. */
  getToolCallLogUrl(id: number): string {
    return `/api/admin/benchmark/runs/${id}/tool-call-log`;
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

  // Multi-run: limits, series, groups and group analysis

  /**
   * The caps and the live rolling-window counts. The Number of runs field binds its `max` to
   * `maxRunCountPerSeries` from here rather than to a literal, so raising the configured cap raises
   * the field with it.
   */
  getRunLimits(): Observable<BenchmarkRunLimitsDto> {
    return this.http.get<BenchmarkRunLimitsDto>('/api/admin/benchmark/runs/limits');
  }

  startRunSeries(req: StartBenchmarkRunRequest): Observable<{ seriesId: number }> {
    return this.http.post<{ seriesId: number }>('/api/admin/benchmark/runs/series', req);
  }

  getRunSeries(id: number): Observable<BenchmarkRunSeriesDto> {
    return this.http.get<BenchmarkRunSeriesDto>(`/api/admin/benchmark/runs/series/${id}`);
  }

  /** A 204 arrives as null, the same shape getActiveRun() relies on. */
  getActiveRunSeries(): Observable<BenchmarkRunSeriesDto | null> {
    return this.http.get<BenchmarkRunSeriesDto | null>('/api/admin/benchmark/runs/series/active');
  }

  cancelRunSeries(id: number): Observable<void> {
    return this.http.post<void>(`/api/admin/benchmark/runs/series/${id}/cancel`, {});
  }

  /**
   * Continues a stopped series from its next member. Answers 409 with a
   * {@link BenchmarkInstrumentChangedDto} body when an instrument hash moved since member 1; the
   * caller may retry with `acknowledgeInstrumentChange`, which marks the resulting group Tier C.
   */
  resumeRunSeries(id: number, req?: ResumeBenchmarkRunSeriesRequest): Observable<{ seriesId: number }> {
    return this.http.post<{ seriesId: number }>(
      `/api/admin/benchmark/runs/series/${id}/resume`, req ?? {});
  }

  getRunGroups(): Observable<BenchmarkRunGroupDto[]> {
    return this.http.get<BenchmarkRunGroupDto[]>('/api/admin/benchmark/runs/groups');
  }

  getRunGroup(id: number): Observable<BenchmarkRunGroupDto> {
    return this.http.get<BenchmarkRunGroupDto>(`/api/admin/benchmark/runs/groups/${id}`);
  }

  /**
   * The tier a set of runs would resolve to, without creating anything. This is what the group
   * builder shows while the operator is still selecting runs.
   */
  previewRunGroupTier(req: CreateBenchmarkRunGroupRequest): Observable<BenchmarkRunGroupTierPreviewDto> {
    return this.http.post<BenchmarkRunGroupTierPreviewDto>(
      '/api/admin/benchmark/runs/groups/preview', req);
  }

  createRunGroup(req: CreateBenchmarkRunGroupRequest): Observable<BenchmarkRunGroupTierPreviewDto> {
    return this.http.post<BenchmarkRunGroupTierPreviewDto>('/api/admin/benchmark/runs/groups', req);
  }

  updateRunGroup(id: number, req: UpdateBenchmarkRunGroupRequest): Observable<BenchmarkRunGroupTierPreviewDto> {
    return this.http.put<BenchmarkRunGroupTierPreviewDto>(
      `/api/admin/benchmark/runs/groups/${id}`, req);
  }

  deleteRunGroup(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/benchmark/runs/groups/${id}`);
  }

  analyseRunGroup(id: number, req?: BenchmarkGroupAnalysisRequest): Observable<BenchmarkGroupAnalysisDto> {
    return this.http.post<BenchmarkGroupAnalysisDto>(
      `/api/admin/benchmark/runs/groups/${id}/analysis`, req ?? {});
  }

  /** The most recent stored analysis, or null (204) when the group has never been analysed. */
  getRunGroupAnalysis(id: number): Observable<BenchmarkGroupAnalysisDto | null> {
    return this.http.get<BenchmarkGroupAnalysisDto | null>(
      `/api/admin/benchmark/runs/groups/${id}/analysis`);
  }

  /** Beside getRunReportUrl, and used the same way: window.open, not an XHR. */
  getGroupReportUrl(groupId: number): string {
    return `/api/admin/benchmark/runs/groups/${groupId}/report`;
  }

  /**
   * One cross-model comparison over the named runs and analysis groups.
   *
   * `runIds` and `groupIds` are repeated parameters rather than one comma-joined value, which is
   * the shape `[FromQuery] BenchmarkModelComparisonRequest` binds its two lists from, and
   * `pricingBasis` travels as the enum member name the query binder accepts.
   */
  compareModels(query: BenchmarkModelComparisonQuery): Observable<BenchmarkModelComparisonDto> {
    let params = new HttpParams();
    for (const [key, value] of modelComparisonQueryParams(query)) {
      params = params.append(key, value);
    }
    return this.http.get<BenchmarkModelComparisonDto>(MODEL_COMPARISON_ENDPOINT, { params });
  }

  /**
   * The comparability index over the named runs and analysis groups: which of them fall into the
   * same condition, ahead of a Compare that would otherwise silently exclude the smaller one.
   */
  getComparabilityIndex(query: BenchmarkComparabilityIndexQuery): Observable<BenchmarkComparabilityIndexDto> {
    let params = new HttpParams();
    for (const [key, value] of comparabilityIndexQueryParams(query)) {
      params = params.append(key, value);
    }
    return this.http.get<BenchmarkComparabilityIndexDto>(MODEL_COMPARABILITY_INDEX_ENDPOINT, { params });
  }
}
