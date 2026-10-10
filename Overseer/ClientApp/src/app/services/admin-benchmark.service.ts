import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable, catchError, map, throwError } from 'rxjs';

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

// The battery statistics records are declared beside the views that render them; type-only, so
// erased at runtime.
import type {
  BenchmarkBatteryComparison,
  BenchmarkBatteryStatisticsResult
} from '../admin/benchmark/batteries/battery.models';

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
  /** Shown as a "Recommended" badge where the options are explained. */
  recommended?: boolean;
}

/** In coverage order, labelled for what they do rather than for their enum names. */
export const BENCHMARK_SECOND_OPINION_MODES: readonly BenchmarkSecondOpinionModeOption[] = [
  {
    value: BenchmarkSecondOpinionMode.Off,
    label: 'Never',
    hint: 'No second reading. Scores are unchanged; you get no DISPUTED marks and no agreement figure.'
  },
  {
    value: BenchmarkSecondOpinionMode.Flagged,
    label: 'Only flagged answers',
    hint: 'Only answers that raised a flag: a critical error, a refuted claim, a doubtful deduction, or a score below the profile threshold. Cheapest; agreement is measured on doubtful verdicts only.'
  },
  {
    value: BenchmarkSecondOpinionMode.FlaggedAndOutliers,
    label: 'Flagged answers and statistical outliers',
    hint: "Flagged answers, plus answers far below the run's median, found after scoring. Adds a final stage."
  },
  {
    value: BenchmarkSecondOpinionMode.FlaggedPlusSample,
    label: 'Flagged answers plus a sample',
    hint: "Flagged answers, topped up to the profile's minimum sample with the lowest scores. Always yields some "
      + "agreement figure; the Standard profile's default."
  },
  {
    value: BenchmarkSecondOpinionMode.All,
    label: 'Every answer (double grading)',
    hint: 'Every answer is read twice. The only unbiased measure of grading reliability, and the only mode that '
      + 'catches a confidently wrong verdict that raised no flag.',
    recommended: true
  }
];

/** Coverage of a panel run's reference reader, which reads every answer whatever mode the run records. */
export const BENCHMARK_REFERENCE_READER_COVERAGE = {
  label: 'Every answer, blind (reference reading)',
  hint: "A panel run's reference reader reads every answer, blind, as a third reading. It never scores."
} as const;

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
  /**
   * Lowest quality score for an answer graded not attempted with no critical error and Accuracy
   * level 5 or above, from scoring method 13. Null: no floor.
   */
  notAttemptedScore?: number | null;
  /** Quality score below which an answer is re-graded, when the run has a second-opinion assessor. 0 disables the score trigger. */
  secondOpinionQualityThreshold: number;
  /** Off (0), Flagged (1), FlaggedAndOutliers (2) or All (3). */
  secondOpinionMode: number;
  /** Quality points below the run's own median at which an answer is re-graded. FlaggedAndOutliers only. */
  secondOpinionOutlierDeltaPoints: number;
  secondOpinionBlind?: boolean;
  /** Answers FlaggedPlusSample tops the flagged set up to. */
  secondOpinionMinimumSample?: number;
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
  /**
   * Lowest quality score for an answer graded not attempted with no critical error and Accuracy
   * level 5 or above, from scoring method 13. Null: no floor.
   */
  notAttemptedScore: number | null;
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
  /**
   * Lowest quality score for an answer graded not attempted with no critical error and Accuracy
   * level 5 or above, from scoring method 13. Null: no floor. Always sent: an update that omits it
   * clears it.
   */
  notAttemptedScore: number | null;
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

/** One difficulty band as the rubric authoring guidance describes it. */
export interface RubricAuthoringBand {
  name: string;
  /** Inclusive assessed-difficulty range, e.g. "36–70". */
  range: string;
  description: string;
}

/** The server-owned rubric format and band definitions (`BenchmarkRubricAuthoringGuidance`). */
export interface RubricAuthoringGuidance {
  sectionRules: string;
  gradingSemantics: string;
  workedExample: string;
  formLabel: string;
  bands: RubricAuthoringBand[];
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
  /** The board's `Snapshot format: N` at capture. Null when the board text does not state one. */
  snapshotFormatVersion?: number | null;
  /** The board header's `snapshot at yyyy-MM-dd HH:mm:ss`, local game time with no zone, kept as text. */
  boardHeaderTimestamp?: string | null;
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

export interface UpdateBenchmarkGameSnapshotTextRequest {
  text: string;
  /* The SHA-256 the client loaded; the server refuses the save with 409 when the stored hash differs. */
  expectedSha256?: string | null;
}

/** What replacing a snapshot's text returns: the stored snapshot plus the check run against it. */
export interface UpdateBenchmarkSnapshotTextResponse {
  snapshot: BenchmarkGameSnapshotDto;
  boardFactsCheck: BoardFactsCheckDto | null;
}

export interface CaptureBenchmarkSnapshotResponse {
  board: BenchmarkGameSnapshotDto;
  suite: BenchmarkSuiteDto;
  boardFactsCheck?: BoardFactsCheckDto | null;
}

/** One BOARD FACTS bullet the checker read, kept for the "checked, not missing" count. */
export interface BoardFactIssueDto {
  questionId: number;
  orderIndex: number;
  /** Only present on a `missingLiterals` entry. */
  literal?: string | null;
  lineExcerpt: string;
}

/**
 * The deterministic BOARD FACTS quote check run after a snapshot write or an import. Only
 * `missingLiterals` are issues; `unquotedBullets` is coverage information, never a warning.
 */
export interface BoardFactsCheckDto {
  bulletCount: number;
  checkedLiteralCount: number;
  unquotedBulletCount: number;
  unquotedBullets: BoardFactIssueDto[];
  missingLiterals: BoardFactIssueDto[];
}

export interface StartQuestionGenerationRequest {
  suiteId: number;
  generatorModelConfigurationId: number;
  simpleCount: number;
  intermediateCount: number;
  advancedCount: number;
  instructions?: string | null;
}

/** `Band` generates a difficulty band; `RubricOnly` and `ReplaceQuestion` rewrite one existing question in place. */
export type QuestionGenerationItemKind = 'Band' | 'RubricOnly' | 'ReplaceQuestion';

export interface QuestionGenerationJobItemDto {
  kind: QuestionGenerationItemKind;
  difficulty: number;
  difficultyName: string;
  requestedCount: number;
  generatedCount: number;
  /** `Pending` | `Generating` | `Repairing` | `Completed` | `Failed` | `Skipped` | `Cancelled`. */
  status: string;
  errorMessage?: string | null;
  /** Set for the per-question kinds only. */
  targetQuestionId?: number | null;
  targetQuestionOrderIndex?: number | null;
  /** The first 120 characters of the target question's text. */
  targetQuestionExcerpt?: string | null;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  modelCalls: number;
  promptTokens: number;
  outputTokens: number;
  createdQuestionCount: number;
  updatedQuestionCount: number;
  discardedQuestionCount: number;
}

export interface QuestionGenerationJobLogEntryDto {
  timestampUtc: string;
  message: string;
  /** Lowercase: `info` | `warning` | `error`. */
  severity: string;
  /** The raw provider error or model response behind a failure line, truncated by the server. */
  rawExcerpt?: string | null;
}

export interface QuestionGenerationJobDto {
  id: string;
  suiteId: number;
  suiteName: string;
  generatorConfigId: number;
  generatorDisplayName: string;
  generatorProvider?: string | null;
  generatorModelId?: string | null;
  generatorThinkingLevel?: string | null;
  generatorReasoningMode?: string | null;
  generatorServiceTier?: string | null;
  gameSnapshotId?: number | null;
  gameSnapshotName?: string | null;
  /** `Generation` | `Retry` | `Regeneration`. */
  jobKind: string;
  retryOfJobId?: string | null;
  instructions: string;
  startedByUserId?: string | null;
  /** `Running` | `Completed` | `CompletedWithErrors` | `Cancelled` | `Failed`. */
  status: string;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  totalModelCalls: number;
  promptTokens: number;
  outputTokens: number;
  items: QuestionGenerationJobItemDto[];
  log: QuestionGenerationJobLogEntryDto[];
}

/**
 * Starts a new job over the chosen bands of a finished one. Generator and instructions left
 * null fall back to the previous job's.
 */
export interface RetryQuestionGenerationRequest {
  /** Band numbers: 1 Simple, 2 Intermediate, 3 Advanced. */
  difficulties: number[];
  /** True deletes the band's previously created questions and generates the full count again. */
  discardExisting: boolean;
  generatorModelConfigurationId?: number | null;
  instructions?: string | null;
}

/** `Rubric` rewrites only the rubric; `Question` replaces the question text and its rubric. */
export type RegenerateQuestionsScope = 'Rubric' | 'Question';

export interface RegenerateQuestionsRequest {
  suiteId: number;
  questionIds: number[];
  scope: RegenerateQuestionsScope;
  generatorModelConfigurationId: number;
  instructions?: string | null;
}

export interface GenerateSuiteDescriptionRequest {
  generatorModelConfigurationId: number;
  instructions?: string | null;
  includeSnapshot: boolean;
  /** When true the response also carries the full prompt and raw response text. */
  includeDebugText: boolean;
}

export interface SuiteDescriptionLogEntryDto {
  timestampUtc: string;
  message: string;
  /** Lowercase: `info` | `warning` | `error`. */
  severity: string;
  rawExcerpt?: string | null;
}

/**
 * One model call that drafts a suite description. Nothing is persisted; the operator edits and
 * saves the text through the ordinary suite update.
 */
export interface SuiteDescriptionGenerationResultDto {
  suiteId: number;
  suiteName: string;
  questionCount: number;
  snapshotIncluded: boolean;
  gameSnapshotName?: string | null;
  snapshotCharCount: number;
  promptCharCount: number;

  generatorConfigId: number;
  generatorDisplayName?: string | null;
  generatorProvider?: string | null;
  generatorModelId?: string | null;
  generatorThinkingLevel?: string | null;
  generatorReasoningMode?: string | null;
  generatorServiceTier?: string | null;
  actualServiceTier?: string | null;

  startedAtUtc: string;
  completedAtUtc: string;
  durationMs: number;
  timeToFirstTokenMs?: number | null;
  modelCalls: number;

  promptTokens: number;
  uncachedInputTokens: number;
  cacheReadTokens: number;
  cacheCreationTokens: number;
  outputTokens: number;
  reasoningTokens: number;
  /** True when the provider reported no usage and the client-side estimate was used instead. */
  tokensEstimated: boolean;
  costUsd?: number | null;
  /** `'catalog'` | `'custom'` | null. */
  pricingSource?: string | null;

  /** `Completed` | `Failed` | `Cancelled`. */
  status: string;
  description?: string | null;
  errorMessage?: string | null;
  log: SuiteDescriptionLogEntryDto[];

  /** Populated only when the request set `includeDebugText`. */
  promptText?: string | null;
  rawResponseText?: string | null;
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

/** One question of a YAML import: `questionId` replaces that question, null creates a new one. */
export interface ImportBenchmarkQuestionItem {
  questionId: number | null;
  /** Null keeps the current text on a replace; required on a create. */
  questionText: string | null;
  /** Null keeps the current difficulty on a replace; Simple on a create. */
  difficulty: number | null;
  /** Applied only when `replaceExpectedPoints` is true; empty clears the rubric. */
  expectedPoints: string | null;
  replaceExpectedPoints: boolean;
}

export interface ImportBenchmarkQuestionsRequest {
  items: ImportBenchmarkQuestionItem[];
}

export interface ImportBenchmarkQuestionsResultDto {
  createdCount: number;
  replacedCount: number;
  unchangedCount: number;
  questions: BenchmarkQuestionDto[];
  boardFactsCheck?: BoardFactsCheckDto | null;
}

/** The game snapshot a suite YAML carries; the import attaches it to the new suite. */
export interface ImportBenchmarkSuiteSnapshot {
  /** Blank uses the suite's name. */
  name: string | null;
  /** Flattened snapshot text, or a raw HTML dump. */
  text: string;
  sourceGnollHackVersion: string | null;
  capturedAtUtc: string | null;
  notes: string | null;
}

export interface ImportBenchmarkSuiteRequest {
  name: string;
  description: string | null;
  questions: ImportBenchmarkQuestionItem[];
  /** Null or absent imports the suite without a snapshot. */
  snapshot?: ImportBenchmarkSuiteSnapshot | null;
}

export interface MatchedSnapshotDto {
  id: number;
  name: string;
  suiteId?: number | null;
  suiteName?: string | null;
}

/** What a suite import would do with a snapshot text, checked before anything is written. */
export interface MatchSnapshotResult {
  sha256: string;
  charCount: number;
  truncated: boolean;
  isHtml: boolean;
  /** A stored snapshot with the same text; null when there is none. */
  match?: MatchedSnapshotDto | null;
}

export type SnapshotContentKind = 'Auto' | 'Text' | 'Html';

export interface UploadSuiteSnapshotRequest {
  name: string;
  /** The file's text: a viewer-downloaded .snapshot.txt or a raw HTML dump. */
  content: string;
  contentKind: SnapshotContentKind;
  notes?: string | null;
  sourceGnollHackVersion?: string | null;
  /** True only after the admin confirmed replacing the suite's current snapshot. */
  replaceExisting: boolean;
}

export interface StartBenchmarkRunRequest {
  suiteId: number;
  testedModelConfigurationId: number;
  assessorModelConfigurationId: number;
  /**
   * Optional. When set, the run is a two-member panel: this configuration (member B) grades every
   * answer beside the assessor (member A), and the published score is the mean of the two. It
   * must be a different provider from the assessor, and neither member may be the model under
   * test. Null means a single-assessor run.
   */
  coAssessorModelConfigurationId?: number | null;
  /**
   * Optional. When set, answers flagged with a critical error or scored below the profile's
   * threshold are re-graded once by this configuration. Null means no second opinion. In a panel
   * run it is the reference reader, and the server forces its mode to All, blind.
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
  /**
   * Optional. When set, this configuration writes the run's Executive Summary and Report for AI
   * Researchers and Developers once, after the run is scored. It may be neither the model under
   * test nor of its provider. Null means no AI-written reports. Not a comparability key.
   */
  reportWriterModelConfigurationId?: number | null;
  scoringProfileId?: number | null;
  acknowledgeSameProvider?: boolean;
  /** The operator acknowledged that the report writer shares the model under test's provider. */
  acknowledgeSameProviderReportWriter?: boolean;
  verboseMode?: boolean;
  /**
   * Whether the candidate may cite source files and lines. False, the production default, matches a
   * regular user who has not turned on Show source code references; omitted is the server's default.
   */
  allowSourceCodeReferences?: boolean | null;
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
  /** The verdict the calibration was compared against. Null on a row recorded before it existed, which means `Assessor`. */
  comparedAgainst?: BenchmarkCalibrationTarget | null;
}

/**
 * What a calibration compares its verdicts against: the assessor (member A), the co-assessor
 * (member B) or the panel score. The last two are accepted only on a panel run.
 */
export type BenchmarkCalibrationTarget = 'Assessor' | 'CoAssessor' | 'Panel';

/** Which panel member a re-assessment re-grades. The server treats an absent value as `Both`. */
export type BenchmarkPanelMember = 'A' | 'B' | 'Both';

/** Which choice shares the model under test's provider. Absent on older responses: an assessor. */
export type SameProviderWarningRole = 'assessor' | 'reportWriter';

export interface SameProviderWarningDto {
  sameProvider: boolean;
  provider: string;
  testedModelDisplayName: string;
  /** The assessor's display name, or the report writer's when `role` is `reportWriter`. */
  assessorModelDisplayName: string;
  message: string;
  role?: SameProviderWarningRole;
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

  /** The assessor marked the answer not attempted. Null for an answer graded before scoring method 13. */
  notAttempted?: boolean | null;

  /** How the answer's critical-error flags were resolved. Null before scoring method 13. */
  criticalErrorResolution?: BenchmarkCriticalErrorResolution | null;

  /** Null before scoring method 13, outside the quality index, and while the answer is not yet graded. */
  outcomeClass?: BenchmarkOutcomeClass | null;

  /**
   * The panel score: the mean of the two members' quality scores, set only when both scored.
   * Null on a single-assessor run, where qualityScore is the published score.
   */
  panelQualityScore?: number | null;
  /** The two members differ by more than 15 points or on criticalError. Null outside a panel run. */
  panelDisagreed?: boolean | null;

  /**
   * Panel member B's verdict. `coAssessmentJson` holds its levels, evidence, unverified claims and
   * flags (see `BenchmarkCoAssessmentRecord`). All null on a single-assessor run.
   */
  coAssessmentStatus?: string | number | null;
  coAssessmentError?: string | null;
  coAssessmentQualityScore?: number | null;
  coAssessmentRawQualityScore?: number | null;
  coAssessmentCriticalError?: boolean | null;
  coAssessmentNotAttempted?: boolean | null;
  coAssessmentJson?: string | null;
  coAssessedByModelDisplayNameUsed?: string | null;
  coAssessedAtUtc?: string | null;
  coAssessorBoardChars?: number | null;
  coAssessmentInputTokens?: number | null;
  coAssessmentOutputTokens?: number | null;
  coAssessmentDurationMs?: number | null;
  /**
   * Panel member B's integrity flags as enum names (`ContestedVerdict`, `RubricContradictedBySource`
   * and so on). Empty on a single-assessor run.
   */
  coAssessmentFlagNames?: string[];

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

  /** Board characters each grading role's prompt carried. Null when not recorded (before harness 30, or no board). */
  assessorBoardChars?: number | null;
  secondOpinionBoardChars?: number | null;
  verifierBoardChars?: number | null;

  /** The primary assessor's advisory re-grade with the verifier's findings in hand. Null when not re-graded. */
  evidenceInformedQualityScore?: number | null;
  evidenceInformedCriticalError?: boolean | null;
  evidenceInformedJson?: string | null;

  /** Claims the assessor could neither confirm nor refute. Null for a run graded before the field existed. */
  unverifiedClaimCount?: number | null;
  unverifiedClaimsJson?: string | null;

  /** Claim verification findings from the read-only tool verifier. Advisory: does not alter score. */
  claimVerificationJson?: string | null;
  claimsSupportedCount?: number | null;
  claimsRefutedCount?: number | null;
  claimsIndeterminateCount?: number | null;
  /** Per-sentence counts from a run integrity dispute's accused-sentence check. Null when the answer has no claim-verification record. */
  accusedSupportedCount?: number | null;
  accusedRefutedCount?: number | null;
  accusedIndeterminateCount?: number | null;
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

  /** When a re-run (failed-question or single-answer) replaced this attempt. Null for an attempt that was never re-run. */
  rerunAtUtc?: string | null;
  /** Enum name of the replaced attempt's status, e.g. `ProviderError`. Set only alongside rerunAtUtc. */
  rerunOfStatus?: string | null;
  /** The replaced attempt's error message, truncated to 512 characters. */
  rerunOfErrorMessage?: string | null;
}

/**
 * Panel member B's full verdict, as `BenchmarkRunAnswerDto.coAssessmentJson` stores it. The
 * levels are null only on a record written without a grader (a model-produced empty answer).
 */
export interface BenchmarkCoAssessmentRecord {
  accuracyLevel?: number | null;
  completenessLevel?: number | null;
  concisenessLevel?: number | null;
  readabilityLevel?: number | null;
  criticalError?: boolean;
  criticalErrorQuote?: string | null;
  criticalErrorDemoted?: boolean;
  qualityScore?: number | null;
  rawQualityScore?: number | null;
  comment?: string | null;
  accuracyEvidence?: string | null;
  completenessEvidence?: string | null;
  readabilityEvidence?: string | null;
  unverifiedClaims?: string[] | null;
  flags?: {
    contestedVerdict?: boolean;
    unevidencedDeduction?: boolean;
    omissionAsAccuracy?: boolean;
    outOfRubricAccuracy?: boolean;
    dimensionOutlier?: boolean;
    completenessOutOfScope?: boolean;
    readabilityFormOnly?: boolean;
    contestedCriticalError?: boolean;
    contestedAccuracyDeduction?: boolean;
    rubricContradictedBySource?: boolean;
  } | null;
}

/** One structured finding of a synthesis. */
export interface BenchmarkSynthesisFindingDto {
  /** `strength` or `weakness`. */
  kind: string;
  /** accuracy, completeness, conciseness, readability, critical_error, tool_use or other. */
  category: string;
  /** The question numbers the finding cites; empty for a run-wide finding. */
  questions: number[];
  text: string;
}

/**
 * One row of the computed agreement between the two panel members' synthesis findings, matched on
 * kind, category and overlapping questions.
 */
export interface BenchmarkSynthesisConvergenceRowDto {
  /** Member A's kind on a `Conflicting` row, where member B raised the opposite one. */
  kind: string;
  category: string;
  /** The questions the row's findings name, ascending; empty for a run-wide finding. */
  questions: number[];
  status: 'Convergent' | 'MemberAOnly' | 'MemberBOnly' | 'Conflicting';
  memberAText: string | null;
  memberBText: string | null;
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
  /** "official", or "custom (…; fingerprint …)". Never a hostname. */
  testedModelEndpoint?: string | null;

  assessorModelConfigurationId?: number | null;
  assessorModelDisplayNameUsed: string;
  assessorModelProviderUsed: string;
  assessorModelIdUsed: string;
  assessorModelThinkingLevelUsed?: string | null;
  assessorModelReasoningModeUsed?: string | null;
  assessorModelEndpoint?: string | null;
  assessorAvailable?: boolean;

  /**
   * True when a co-assessor (panel member B) graded beside the assessor (member A). The published
   * scores are then the panel's, and the second opinion is the advisory reference reader.
   */
  isPanelRun?: boolean;

  /** Panel member B. All null on a single-assessor run. */
  coAssessorModelConfigurationId?: number | null;
  coAssessorModelDisplayNameUsed?: string | null;
  coAssessorModelProviderUsed?: string | null;
  coAssessorModelIdUsed?: string | null;
  coAssessorModelThinkingLevelUsed?: string | null;
  coAssessorModelReasoningModeUsed?: string | null;
  coAssessorModelEndpoint?: string | null;

  /** Null when the run was started without a second-opinion assessor. */
  secondOpinionAssessorModelConfigurationId?: number | null;
  secondOpinionAssessorModelDisplayNameUsed?: string | null;
  secondOpinionAssessorModelProviderUsed?: string | null;
  secondOpinionAssessorModelIdUsed?: string | null;
  secondOpinionAssessorModelThinkingLevelUsed?: string | null;
  secondOpinionAssessorModelReasoningModeUsed?: string | null;
  secondOpinionAssessorModelEndpoint?: string | null;

  /** Null when the run was started without a claim verifier. */
  claimVerifierModelConfigurationId?: number | null;
  claimVerifierDisplayNameUsed?: string | null;
  claimVerifierProviderUsed?: string | null;
  claimVerifierModelIdUsed?: string | null;
  claimVerifierThinkingLevelUsed?: string | null;
  claimVerifierReasoningModeUsed?: string | null;
  claimVerifierModelEndpoint?: string | null;

  /** Null when the run was started without a report writer and none was chosen since. */
  reportWriterModelConfigurationId?: number | null;
  /** The report writer configuration's current display name; null when none or deleted. */
  reportWriterDisplayName?: string | null;
  /**
   * Where the run's two AI-written documents stand. It is not reset when the documents are
   * deleted, so whether they exist is read from the document list.
   */
  reportDocumentsStatus?: BenchmarkRunReportDocumentsStatus;
  /** Why the documents failed or were skipped; null otherwise. */
  reportDocumentsMessage?: string | null;
  /** The report writer configuration's current provider, model id and thinking level; null when none or deleted. */
  reportWriterProvider?: string | null;
  reportWriterModelId?: string | null;
  reportWriterThinkingLevel?: string | null;
  /** How many run-completion documents of this run are stored. */
  reportDocumentsWrittenCount?: number;
  /**
   * Sums over the run's stored run-completion documents; null when there is none. The cost is also
   * null when any document's cost is unknown. None of these is part of the run's own cost.
   */
  reportDocumentsDurationMs?: number | null;
  reportDocumentsCostUsd?: number | null;
  reportDocumentsInputTokens?: number | null;
  reportDocumentsOutputTokens?: number | null;

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
  /**
   * The run was graded under the scoring method this server grades under; every action that grades
   * part of the run is refused otherwise. An older server omits it.
   */
  isCurrentScoringMethod?: boolean;
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
   * Advisory before scoring method 13: the quality cap stands and no index moved. From scoring
   * method 13 a critical error only one panel member raised is overturned. Absent on a run before
   * harness 19.
   */
  contestedCriticalErrorAnswerCount?: number | null;
  /**
   * Answers flagged for either of two causes: an out-of-rubric Accuracy deduction whose
   * own-knowledge basis the claim verifier refuted, or a sentence the assessor quoted as false
   * that the verifier supported instead. Advisory: the deduction stands and no index moved. Null
   * on a run before harness 20, which never adjudicated it: "not recorded", never 0.
   */
  contestedAccuracyDeductionAnswerCount?: number | null;
  /**
   * Answers where a sentence a grader docked because it disagrees with the rubric's text was
   * supported by the claim verifier with a citation, for either panel member. Advisory: a suite
   * repair lead; the deduction stands and no index moved. 0 on a run before harness 46.
   */
  rubricContradictedAnswerCount?: number | null;
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

  /**
   * Agreement between the two panel members over the answers both scored, and each member's
   * index alone over those answers. All null on a single-assessor run.
   */
  panelGradedAnswerCount?: number | null;
  panelMeanAbsDelta?: number | null;
  /** B − A. Negative means member B graded lower. */
  panelMeanSignedDelta?: number | null;
  panelCriticalErrorSplitCount?: number | null;
  panelDisagreementCount?: number | null;
  panelIntraclassCorrelation?: number | null;
  assessorOnlyQualityIndex?: number | null;
  coAssessorOnlyQualityIndex?: number | null;

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
  /**
   * The suite asks at least one question a knowledge-base article answers
   * (BenchmarkChatTransfer.HasKnowledgeBaseRoutingQuestion), so answers without a knowledge-base
   * call are not by themselves prompt-compliant. Undefined on a run detail served before it existed.
   */
  hasKnowledgeBaseRoutingQuestion?: boolean;
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

  /** Panel member B's own synthesis, written from its own verdicts. Null on a single-assessor run. */
  coAssessorFinalScore?: number | null;
  coAssessorSynthesisJson?: string | null;
  coAssessorSynthesisText?: string | null;
  coAssessorSynthesisParseFailed?: boolean;

  /**
   * The structured findings of assessmentJson and coAssessorSynthesisJson, parsed on the server.
   * Empty when the synthesis carries none, including every synthesis written before findings existed.
   */
  synthesisFindings?: BenchmarkSynthesisFindingDto[];
  coAssessorSynthesisFindings?: BenchmarkSynthesisFindingDto[];

  /** The computed agreement between the two members' findings. Null unless both panel syntheses exist. */
  synthesisConvergence?: BenchmarkSynthesisConvergenceRowDto[] | null;

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

  /** Panel member B's per-question assessment usage. Zero on a single-assessor run. */
  totalCoAssessmentInputTokens?: number;
  totalCoAssessmentOutputTokens?: number;
  totalCoAssessmentCacheReadTokens?: number;
  totalCoAssessmentCacheCreationTokens?: number;
  totalCoAssessmentDurationMs?: number;

  /** Panel member B's own final-synthesis usage. Zero on a single-assessor run. */
  totalCoSynthesisInputTokens?: number;
  totalCoSynthesisOutputTokens?: number;
  totalCoSynthesisCacheReadTokens?: number;
  totalCoSynthesisCacheCreationTokens?: number;
  totalCoSynthesisDurationMs?: number;

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
   * Order indexes the run's most recent re-run (failed-question or single-answer) is scoped to. Reported while the
   * re-run executes and still after it finishes, until this process starts another run or
   * restarts; empty for a run it never re-ran.
   */
  rerunScopeOrderIndexes?: number[];

  /**
   * Order indexes that re-run has produced an answer or a score for. Same contract as
   * rerunScopeOrderIndexes.
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
   * The instrument the most recent re-run (failed-question or single-answer) executed under. Non-null only on a run
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
  /** Panel member B's assessments and its own synthesis. Null outside a panel run. */
  estimatedCoAssessorCost?: number | null;
  estimatedCoSynthesisCost?: number | null;
  /** Assessor, co-assessor, second opinion, claim verifier and both syntheses together — the whole grading side. */
  estimatedGradingCost?: number | null;
  pricingSource?: string | null;
  pricingIncomplete?: boolean;

  /** When the pre-run candidate delivery probe passed. Null when not recorded. */
  candidateDeliveryVerifiedAtUtc?: string | null;
  /** When the candidate delivery probe passed again before the run's most recent re-run. Null when not recorded. */
  rerunCandidateDeliveryVerifiedAtUtc?: string | null;
  /** Board delivery per grading role. Empty for a run with no board, or before harness 30. */
  boardDelivery?: BenchmarkBoardDeliveryDto[];

  /** The BOARD FACTS quote check stamped at launch. Null when the suite had no board. */
  boardFactsCheck?: BoardFactsCheckDto | null;
  /** The board's `Snapshot format: N` at launch. Null when the board text did not state one. */
  gameSnapshotFormatVersionUsed?: number | null;
  /** SHA-256 of the board text the run was launched with. Null when the suite had no board. */
  gameSnapshotSha256Used?: string | null;
  /** True when the run has a stored board record, which `getRunBoard` returns. */
  hasBoardRecord?: boolean;

  /** The run's answers by outcome class and its critical errors by resolution. Null before scoring method 13. */
  outcomeSummary?: BenchmarkRunOutcomeSummaryDto | null;

  /** The model batch this run is a member of, directly or through its series or battery run; null or absent for none. */
  modelBatchRunId?: number | null;

  answers: BenchmarkRunAnswerDto[];
}

/**
 * How an answer's critical-error flags were resolved (scoring method 13 on). Agreed,
 * UpheldByVerifier and SingleAssessor are confirmed and cap the score; Unresolved averages the two
 * panel members; OverturnedByVerifier lifts the cap.
 */
export type BenchmarkCriticalErrorResolution =
  'None' | 'Agreed' | 'UpheldByVerifier' | 'OverturnedByVerifier' | 'Unresolved' | 'SingleAssessor';

export type BenchmarkOutcomeClass = 'Correct' | 'Partial' | 'Incorrect' | 'NotAttempted' | 'NoAnswer';

/** A run's answers by outcome class and its critical errors by resolution. Question lists hold 1-based question numbers. */
export interface BenchmarkRunOutcomeSummaryDto {
  correctCount: number;
  partialCount: number;
  incorrectCount: number;
  notAttemptedCount: number;
  noAnswerCount: number;
  /** Correct, partial, incorrect and not attempted together. */
  classifiedCount: number;
  confirmedCriticalErrorCount: number;
  unresolvedCriticalErrorCount: number;
  overturnedCriticalErrorCount: number;
  /** Confirmed critical errors ÷ classified answers, a fraction, with its 95 % Wilson interval. Null when nothing is classified. */
  criticalErrorRate: number | null;
  criticalErrorRateLow: number | null;
  criticalErrorRateHigh: number | null;
  /** Correct ÷ (correct + partial + incorrect). Null when that denominator is 0. */
  correctWhenAttempted: number | null;
  /** Incorrect ÷ (incorrect + not attempted). Null when both are 0. */
  wrongInsteadOfAbstaining: number | null;
  confirmedCriticalErrorQuestions: number[];
  unresolvedCriticalErrorQuestions: number[];
  overturnedCriticalErrorQuestions: number[];
  notAttemptedQuestions: number[];
}

/** The board a run was made with, served from the run's own stored board record. */
export interface BenchmarkRunBoardDto {
  name: string | null;
  sanitizedText: string;
  digestText: string | null;
  charCount: number;
  sha256: string;
}

export interface BenchmarkBoardDeliveryDto {
  role: string;
  delivered: number;
  total: number;
  missingQuestions: number[];
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

/**
 * A sentence graders docked against the rubric that the claim verifier supported with a citation,
 * over the suite's runs at the question's current item revision: a suite repair lead.
 */
export interface BenchmarkRubricContradictionDto {
  questionId: number;
  questionOrderIndex: number;
  /** The answer sentence or table row the grader charged, verbatim. */
  chargedSentence: string;
  /** The words the grader quoted from it; empty when it charged a table row by its label. */
  chargedParts: string[];
  /** The rubric text the grader relied on; null when the harness found none. */
  rubricQuote?: string | null;
  citation?: string | null;
  basis?: string | null;
  /** The distinct runs whose verification raised it. */
  runCount: number;
}

export interface BenchmarkRubricGapReportDto {
  suiteId: number;
  runCount: number;
  claimCount: number;
  clusters: BenchmarkRubricGapClusterDto[];
  knowledgeBaseGaps?: BenchmarkKnowledgeBaseGapDto[];
  rubricContradictions?: BenchmarkRubricContradictionDto[];
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
  testedModelThinkingLevelUsed?: string | null;
  testedModelReasoningModeUsed?: string | null;
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
  /** The battery run this run is a member of (its newest non-superseded membership); null for none. */
  batteryRunId?: number | null;
  batteryName?: string | null;
  /** 1-based position of the run's suite in the battery definition. */
  batterySuitePosition?: number | null;
  /** K, the number of suites in the battery run. */
  batterySuiteCount?: number | null;
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
  /**
   * The most launches one battery run may plan (suites × runs per suite). Absent from a server
   * without batteries, which leaves Runs per Suite bounded by the server alone.
   */
  maxMembersPerBattery?: number;
  /**
   * The most models one model batch may run (`Benchmark:ModelBatch:MaxModels`). Absent from a server
   * without model batches, which leaves the Models Under Test picker bounded by the server alone.
   */
  maxModelsPerBatch?: number;
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

  /** The model batch this series is a member of; null or absent for none. */
  modelBatchRunId?: number | null;

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

// =========================================================================================
// Multi-suite batteries: definitions, battery runs, analyses and the leaderboard
// =========================================================================================

/** How a battery weighs its suites in the Overall Index. */
export type BenchmarkBatteryWeightingScheme = 'DifficultyMass' | 'ItemCount' | 'Equal' | 'Custom';

/** How a stopped battery run is resumed. */
export type BenchmarkBatteryResumeMode = 'Continue' | 'RerunUnderCurrentInstrument';

/** One suite of a battery, with its current exam size and difficulty readiness. */
export interface BenchmarkBatterySuiteDto {
  /** 0-based position in run order. */
  index: number;
  /** Null when the suite has been deleted. */
  suiteId?: number | null;
  suiteName: string;
  deleted: boolean;
  customWeight?: number | null;
  questionCount: number;
  assessedQuestionCount: number;
  /** Every question has an assessed difficulty; the launcher refuses the suite otherwise. */
  difficultyFullyAssessed: boolean;
  /** The sum of the current questions' difficulty weights, an unassessed question weighing 50. */
  difficultyMass: number;
}

/** The normalized suite weights one scheme would give, in suite order. */
export interface BenchmarkBatteryWeightPreviewDto {
  scheme: BenchmarkBatteryWeightingScheme;
  schemeLabel: string;
  /** The battery's own scheme; the others are sensitivity alternatives. */
  declared: boolean;
  /** One weight per suite, summing to 1; empty when the weights are undefined. */
  weights: number[];
}

export interface BenchmarkBatteryDto {
  id: number;
  name: string;
  description?: string | null;
  weightingScheme: BenchmarkBatteryWeightingScheme;
  weightingSchemeLabel: string;
  revision: number;
  definitionSha256: string;
  isArchived: boolean;
  /** The names of the suites that have been deleted; the battery cannot run until it is edited. */
  brokenSuiteNames: string[];
  /** Every problem that keeps the battery from running; empty when it can run. */
  validationErrors: string[];
  createdByUserName?: string | null;
  createdAtUtc: string;
  modifiedAtUtc: string;
  batteryRunCount: number;
  /** A battery run of this battery is Pending, Running or WaitingForCap; delete is refused. */
  hasActiveBatteryRun: boolean;
  /** Ranked rows on the current definition's leaderboard; absent from an older server. */
  rankedResultCount?: number;
  /** The newest analysis of any battery run of the current definition, ranked or not. */
  latestAnalysisAtUtc?: string | null;
  suites: BenchmarkBatterySuiteDto[];
  /** One preview per scheme, the declared one marked. */
  weightPreviews: BenchmarkBatteryWeightPreviewDto[];
}

/** Creates a battery. The suites are listed in run order. */
export interface CreateBenchmarkBatteryRequest {
  name: string;
  description?: string | null;
  weightingScheme: BenchmarkBatteryWeightingScheme;
  /** At least two, none twice. */
  suiteIds: number[];
  /** One weight per entry of `suiteIds`; read only under the Custom scheme. */
  customWeights?: (number | null)[] | null;
}

/** A change to the suites, their order, the weights or the scheme creates a new revision. */
export type UpdateBenchmarkBatteryRequest = CreateBenchmarkBatteryRequest;

/** An existing run placed in one (suite, round) slot of a battery run. */
export interface BenchmarkBatteryAttachDto {
  /** 0-based position of the suite in the battery definition. */
  suiteIndex: number;
  /** 1-based replicate round. */
  round: number;
  runId: number;
}

/** Starts a battery run: every suite of the battery, one after another, for one model configuration. */
export interface StartBenchmarkBatteryRunRequest {
  batteryId: number;
  /** Replicate rounds; every suite runs once per round, in round-robin order. */
  runsPerSuite: number;
  /** Pauses at the run cap rather than stopping; also admits a battery larger than the daily cap. */
  allowCapWait: boolean;
  /** The request every member is launched from. Its suite id and run count are ignored. */
  run: StartBenchmarkRunRequest;
  /** Earlier runs to place in slots; the server judges them again and refuses the start if one no longer qualifies. */
  attach?: BenchmarkBatteryAttachDto[] | null;
}

/** One slot of the reuse preview: the earlier run a start would attach, or why none qualifies. */
export interface BenchmarkBatteryReusePreviewSlotDto {
  /** 0-based position of the suite in the battery definition. */
  suiteIndex: number;
  suiteId: number;
  suiteName: string;
  /** 1-based replicate round. */
  round: number;
  /** The newest eligible run; null when none qualifies and the slot would be launched. */
  runId?: number | null;
  runStartedAtUtc?: string | null;
  qualityIndex?: number | null;
  /** When no run qualifies: why the newest candidate was refused, or that there is none. */
  reason?: string | null;
}

/** Which slots of a battery run not yet started earlier runs would fill. Creates and spends nothing. */
export interface BenchmarkBatteryReusePreviewDto {
  batteryId: number;
  suiteCount: number;
  runsPerSuite: number;
  /** Slots an earlier run would fill. */
  reusedCount: number;
  /** Slots the battery run would launch. */
  launchCount: number;
  /** The runs to send as the start's `attach`, in planner order. */
  attach: BenchmarkBatteryAttachDto[];
  /** Every slot in planner order: round 1 for every suite, then round 2. */
  slots: BenchmarkBatteryReusePreviewSlotDto[];
}

/** A run that may, or may not, be attached to one slot of a battery run. */
export interface BenchmarkBatteryAttachCandidateDto {
  runId: number;
  /** The run's status as text. */
  runStatus: string;
  qualityIndex?: number | null;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  testedModelLabel?: string | null;
  harnessVersion?: string | null;
  scoringMethodVersion: number;
  eligible: boolean;
  /** Why the run may not be attached; null when it may. */
  reason?: string | null;
}

/** One suite of a battery run's definition snapshot. */
export interface BenchmarkBatteryRunSuiteDto {
  index: number;
  suiteId: number;
  suiteName: string;
  customWeight?: number | null;
  /** The instrument hashes recorded for the suite at start; null means not recorded. */
  candidateSystemPromptSha256?: string | null;
  toolGuidesSha256?: string | null;
  knowledgeBaseHeadSha?: string | null;
  wikiHeadSha?: string | null;
  sourceCodeHeadSha?: string | null;
}

/** One run in one (suite, round) slot of a battery run. */
export interface BenchmarkBatteryMemberDto {
  memberId: number;
  /** 0-based position of the suite in the definition snapshot. */
  suiteIndex: number;
  /** 1-based replicate round. */
  round: number;
  runId: number;
  /** The run's status as text; `Deleted` when the run is gone. */
  runStatus: string;
  qualityIndex?: number | null;
  speedIndex?: number | null;
  /** `Launched` or `Attached`. */
  origin: string;
  superseded: boolean;
  /** The member may enter a statistic. */
  usable: boolean;
  /** Why the member is not usable, in a few words; null when it is. */
  unusableReason?: string | null;
  guardFailure?: string | null;
  addedAtUtc: string;
  runStartedAtUtc?: string | null;
  runCompletedAtUtc?: string | null;
  /** The live count for a running member, the run's own column for a finished one. */
  answeredQuestionCount: number;
  totalQuestionCount: number;
  /**
   * The running member's stage: `Answering`, `Verifying`, `SecondOpinion` or `Synthesizing`.
   * Null for a member that is not running, and before the run reports a stage.
   */
  stage?: string | null;
  /** Half-width of the member's Intelligence Index interval; null when not computed. */
  qualityIndexHalfWidth?: number | null;
  /** The finished member's wall-clock duration; null while it runs or when not recorded. */
  durationMs?: number | null;
  /** Claims the claim verifier refuted in the member's answers. */
  claimsRefutedCount?: number;
  /** Answers carrying at least one advisory integrity flag. */
  advisoryFlagAnswerCount?: number;
  /**
   * The member run's estimated cost in US dollars, live while it runs. Null on the battery-run list,
   * on a superseded member, and when the figure is unknown.
   */
  estimatedCost?: number | null;
  estimatedCandidateCost?: number | null;
  /**
   * Mean model time of the member run's Ok answers, in ms: the turn duration less tool time. Null on
   * the battery-run list, on a superseded member, and while no answer is Ok.
   */
  meanModelTimeMs?: number | null;
}

/**
 * A battery run's estimated cost so far, in US dollars: each role summed over the non-superseded
 * member runs that report a figure for it. A role is null when no member has one, or when any member
 * spent on it without a price card; Total is null when any member's pricing is incomplete.
 */
export interface BenchmarkBatteryLiveCostDto {
  total: number | null;
  candidate: number | null;
  assessor: number | null;
  /** Panel member B. */
  coAssessor: number | null;
  /** The second or reference reader. */
  secondOpinion: number | null;
  claimVerifier: number | null;
  synthesis: number | null;
  coSynthesis: number | null;
  /** Every grading role together. */
  grading: number | null;
  /** A member run's pricing was incomplete. */
  pricingIncomplete: boolean;
  /** The members' common pricing source; `mixed` when they differ; null when none resolved. */
  pricingSource: string | null;
  /** The member runs estimated. */
  pricedMemberCount: number;
  /** The battery-completion documents' report-writer cost, not part of `total`; null when none exists or any has no cost. */
  reportWriterCostUsd: number | null;
}

/** One (suite, round) cell of the K × R grid. */
export interface BenchmarkBatterySlotDto {
  suiteIndex: number;
  round: number;
  /** The non-superseded member in this slot; null while the slot is empty. */
  member?: BenchmarkBatteryMemberDto | null;
}

/** `BenchmarkBatteryRunDto.postRunWork`'s wire values. */
export type BenchmarkBatteryPostRunWork = 'None' | 'Repairing' | 'Analysing' | 'WritingReports';

export interface BenchmarkBatteryRunDto {
  id: number;
  /** Null when the battery has been deleted; the run keeps its own snapshot. */
  batteryId?: number | null;
  batteryName: string;
  definitionRevision: number;
  definitionSha256: string;
  weightingScheme: BenchmarkBatteryWeightingScheme;
  suites: BenchmarkBatteryRunSuiteDto[];
  /** K, the number of suites. */
  suiteCount: number;
  /** R, the number of replicate rounds. */
  runsPerSuite: number;
  /** K × R. */
  requestedMemberCount: number;
  /** Slots holding a usable member. */
  completedMemberCount: number;
  failedMemberCount: number;
  /** Suites with at least one usable member; the Overall Index needs all K. */
  completedSuiteCount: number;
  status: BenchmarkRunSeriesStatus;
  /** `MemberFailed`, `RunCapReached`, `SpendDenied` or `InstrumentChanged`; null unless stopped. */
  stopReason?: string | null;
  stopReasonText?: string | null;
  allowCapWait: boolean;
  /** Stopped, or Completed with errors, while no member run is being repaired. */
  resumable: boolean;
  /** This server process is driving the battery run now. */
  isDriving: boolean;
  /**
   * What the server is still doing for the battery run, the first that applies: `Repairing` while a
   * member run runs outside the drive loop, `Analysing` while the analysis is computed,
   * `WritingReports` while the battery-completion documents are Pending or Writing, else `None`.
   * `Analysing` is a wire value. An older server omits it; read that as `None`.
   */
  postRunWork: BenchmarkBatteryPostRunWork;
  /** The member runs being repaired: running while the battery run is not live. Empty otherwise. */
  repairingRunIds: number[];
  startedAtUtc: string;
  completedAtUtc?: string | null;
  lastProgressAtUtc?: string | null;
  errorMessage?: string | null;
  startedByUserName?: string | null;
  testedModelConfigurationId?: number | null;
  testedModelLabel?: string | null;
  /** The running member's slot, else the next one to launch while live; null when neither applies. */
  currentSuiteIndex?: number | null;
  /** 1-based, for "Suite s of K". */
  currentSuitePosition?: number | null;
  currentSuiteName?: string | null;
  currentRound?: number | null;
  /** The member run in flight, if any. */
  currentRunId?: number | null;
  /** The K × R slots in launch order: round 1 for every suite, then round 2. */
  slots: BenchmarkBatterySlotDto[];
  /** Every member row, superseded ones included. */
  members: BenchmarkBatteryMemberDto[];
  latestAnalysisId?: number | null;
  latestAnalysisAtUtc?: string | null;
  latestAnalysisComplete?: boolean | null;
  /** The latest analysis's comparability class; null while it is incomplete. */
  comparabilityClassSha256?: string | null;
  overallIndex?: number | null;
  overallIndexHalfWidth?: number | null;
  overallIndexLower?: number | null;
  overallIndexUpper?: number | null;
  overallSpeedIndex?: number | null;
  totalCost?: number | null;
  /** The usable members changed since the latest analysis. */
  analysisStale: boolean;
  /** The latest analysis lists an excluded member, so a recompute may change it. */
  analysisHasExcludedMembers: boolean;
  /** The model under test as the newest usable member ran it, else as the start request names it. */
  testedProvider?: string | null;
  testedModelId?: string | null;
  testedThinkingLevel?: string | null;
  testedReasoningMode?: string | null;
  testedServiceTier?: string | null;
  assessorLabel?: string | null;
  assessorProvider?: string | null;
  assessorThinkingLevel?: string | null;
  assessorReasoningMode?: string | null;
  /** Panel runs only. */
  coAssessorLabel?: string | null;
  coAssessorProvider?: string | null;
  coAssessorThinkingLevel?: string | null;
  coAssessorReasoningMode?: string | null;
  scoringProfileName?: string | null;
  verboseMode?: boolean;
  /** The battery-completion documents' writer; null when none was chosen. */
  reportWriterModelConfigurationId?: number | null;
  /**
   * The writer's configuration as the server holds it; null without a writer or when its
   * configuration is gone.
   */
  reportWriterDisplayName?: string | null;
  reportWriterProvider?: string | null;
  reportWriterModelId?: string | null;
  reportWriterThinkingLevel?: string | null;
  reportWriterReasoningMode?: string | null;
  reportWriterServiceTier?: string | null;
  reportDocumentsStatus?: BenchmarkRunReportDocumentsStatus;
  /** Why the documents failed or were skipped; null otherwise. */
  reportDocumentsMessage?: string | null;
  /** The battery documents written so far. */
  reportDocumentsWrittenCount?: number;
  /** The estimated cost so far; null on the battery-run list and when no estimator is available. */
  liveCost?: BenchmarkBatteryLiveCostDto | null;
  /**
   * Mean model time over the Ok answers of every non-superseded member run, in ms; null on the
   * battery-run list and while no answer is Ok.
   */
  meanModelTimeMs?: number | null;
  /** The Ok answers `meanModelTimeMs` is the mean of; 0 on the battery-run list. */
  modelTimedAnswerCount?: number;
  /** The model batch this battery run is a member of; null or absent for none. */
  modelBatchRunId?: number | null;
}

/** Computes a battery analysis, optionally paired against a baseline battery run. */
export interface BenchmarkBatteryCompareRequest {
  compareWithBatteryRunId?: number | null;
}

/** A member left out of an analysis, with its reason. */
export interface BenchmarkBatteryExcludedMemberDto {
  suiteIndex: number;
  round: number;
  runId: number;
  reason: string;
}

export interface BenchmarkBatteryAnalysisDto {
  id: number;
  batteryRunId: number;
  batteryName: string;
  computedAtUtc: string;
  /** The usable member run ids the result was computed over. */
  memberRunIds: number[];
  runCount: number;
  definitionSha256: string;
  /** Null when the battery run was incomplete. */
  comparabilityClassSha256?: string | null;
  complete: boolean;
  harnessVersion?: string | null;
  scoringMethodVersion: number;
  /** The usable member runs differ from the ones the result was computed over. */
  stale: boolean;
  comparedWithBatteryRunId?: number | null;
  comparedWithBatteryName?: string | null;
  result: BenchmarkBatteryStatisticsResult | null;
  comparison: BenchmarkBatteryComparison | null;
  excludedMembers: BenchmarkBatteryExcludedMemberDto[];
}

/** The latest analysis of one battery run, as a leaderboard row. */
export interface BenchmarkBatteryLeaderboardRowDto {
  batteryRunId: number;
  batteryId?: number | null;
  batteryName: string;
  definitionRevision: number;
  analysisId: number;
  computedAtUtc: string;
  testedModelConfigurationId?: number | null;
  testedModelLabel?: string | null;
  status: BenchmarkRunSeriesStatus;
  runsPerSuite: number;
  suiteCount: number;
  completedSuiteCount: number;
  complete: boolean;
  comparabilityClassSha256?: string | null;
  harnessVersion?: string | null;
  scoringMethodVersion: number;
  overallIndex?: number | null;
  overallIndexHalfWidth?: number | null;
  overallIndexLower?: number | null;
  overallIndexUpper?: number | null;
  overallSpeedIndex?: number | null;
  totalCost?: number | null;
  passCost?: number | null;
  testedProvider?: string | null;
  testedModelId?: string | null;
  testedThinkingLevel?: string | null;
  testedReasoningMode?: string | null;
  testedServiceTier?: string | null;
}

/** The battery runs whose results may stand in one ranked list. */
export interface BenchmarkBatteryLeaderboardClassDto {
  comparabilityClassSha256: string;
  /** What distinguishes this class, ready to render as its heading. */
  label: string;
  harnessVersion?: string | null;
  scoringMethodVersion: number;
  /** The must-match keys on which this class differs from another class; empty when it is the only one. */
  distinguishingKeys: string[];
  /** Sorted by Overall Index, highest first. */
  rows: BenchmarkBatteryLeaderboardRowDto[];
}

export interface BenchmarkBatteryLeaderboardDto {
  definitionSha256: string;
  /** The current battery carrying this hash, when there is one. */
  batteryId?: number | null;
  batteryName?: string | null;
  /** One ranked list per comparability class; results of different classes are never ranked together. */
  classes: BenchmarkBatteryLeaderboardClassDto[];
  /** Battery runs whose latest analysis is incomplete, unranked, newest first. */
  incomplete: BenchmarkBatteryLeaderboardRowDto[];
}

// =========================================================================================
// Model batches: several models under test, one after another, under one settings set
// =========================================================================================

/** What every member of a model batch runs: one suite (a run or a series) or a battery. */
export type BenchmarkModelBatchTargetKind = 'Suite' | 'Battery';

/** The members' run order: drawn at start from a stored seed, or as the request lists them. */
export type BenchmarkModelBatchOrder = 'Randomized' | 'AsListed';

export type BenchmarkModelBatchStatus =
  | 'Pending'
  | 'Running'
  | 'WaitingForCap'
  | 'Stopped'
  | 'Completed'
  | 'CompletedWithErrors'
  | 'Cancelled'
  | 'Failed';

/** Why a model batch stopped. */
export type BenchmarkModelBatchStopReason =
  | 'MemberStopped'
  | 'InstrumentChanged'
  | 'GraderConfigChanged'
  | 'RunCapReached'
  | 'SpendDenied'
  | 'RestartReconciled';

/** A member's state. A member's cancellation is `Canceled`; the batch's own is `Cancelled`. */
export type BenchmarkModelBatchMemberStatus =
  | 'Pending'
  | 'Running'
  | 'Completed'
  | 'CompletedWithErrors'
  | 'Stopped'
  | 'Failed'
  | 'Skipped'
  | 'Canceled';

/** How a stopped model batch is resumed. */
export type BenchmarkModelBatchResumeMode =
  | 'Continue'
  | 'SkipCurrent'
  | 'AcceptInstrumentChange'
  | 'RerunUnderCurrentInstrument';

/**
 * How a guardrail finding acts: a blocker holds Start back, a warning needs an acknowledgment, advice
 * never blocks.
 */
export type BenchmarkModelBatchFindingSeverity = 'Blocker' | 'Warning' | 'Advice';

/** Starts a model batch, or asks what starting it would meet (`preflight`). */
export interface StartBenchmarkModelBatchRequest {
  targetKind: BenchmarkModelBatchTargetKind;
  /** The suite of a Suite target; null for a battery. */
  suiteId?: number | null;
  /** The battery of a Battery target; null for a suite. */
  batteryId?: number | null;
  /** The models under test; under `AsListed` also their run order. */
  testedModelConfigurationIds: number[];
  /** R: runs per model on a suite (a series from 2), runs per suite on a battery. */
  runsPerModel: number;
  order: BenchmarkModelBatchOrder;
  /** Pauses at the run cap rather than stopping; also admits a batch larger than the daily cap. */
  allowCapWait: boolean;
  /** Every member's request; the server replaces the model under test per member. */
  run: StartBenchmarkRunRequest;
  /** The acknowledgment keys of the warnings the operator accepted. */
  acknowledgedFindingKeys: string[];
}

/** One guardrail finding, as the preflight, the start refusal and the stored batch report it. */
export interface BenchmarkModelBatchFindingDto {
  /** The stable guardrail code, such as `MB-W01`. */
  code: string;
  /** The code's name, such as `MixedFamiliesSingleAssessor`. */
  name: string;
  severity: BenchmarkModelBatchFindingSeverity;
  /**
   * The launcher field the finding is about: `models`, `assessor`, `coAssessor`, `reader` (the second
   * or reference reader), `verifier` (the claim verifier), `reportWriter`, `profile`, `responseStyle`,
   * `sourceReferences`, `runsPerModel`, `order`, `capWait` or `target`; null when it is about no one field.
   */
  field?: string | null;
  /** At most 60 characters. */
  title: string;
  /** One sentence of at most 140 characters. */
  detail: string;
  /** A rationale for the line's info tip. The server sends none, so the launcher's own text for the code applies. */
  rationale?: string | null;
  /** The candidates the finding is about; empty when it is about none in particular. */
  modelConfigurationIds: number[];
  /** A warning's acknowledgment key; it changes when the affected models do. Null for blockers and advice. */
  acknowledgmentKey?: string | null;
}

/** The body of a start a guardrail refuses: 400 for a blocker, 409 for an unacknowledged warning or a busy runner. */
export interface BenchmarkModelBatchRefusalDto {
  message: string;
  findings: BenchmarkModelBatchFindingDto[];
}

/** The 409 body of a resume refused because the instrument changed since the first member. */
export interface BenchmarkModelBatchInstrumentChangedDto {
  instrumentChanged: true;
  batchId: number;
  changedKeys: string[];
  message: string;
}

/**
 * The run caps and rolling windows, and the batch's place under them. Whether the plan exceeds the
 * daily cap, the daily headroom or the hourly rate is read from these numbers and `plannedRunCount`;
 * the findings MB-B08, MB-W11 and MB-W12 say it with authority. One cap wait inside a member is
 * bounded by 26 hours.
 */
export interface BenchmarkModelBatchLimitsDto {
  maxRunsPerDay: number;
  maxRunsPerHour: number;
  runsInLast24Hours: number;
  runsInLastHour: number;
  remainingDailyHeadroom: number;
  /** The rolling 24-hour windows the plan needs at the daily cap; null when it needs one. */
  daySpan?: number | null;
  /** (daySpan − 1) × 24 h, in ms; null when daySpan is. */
  minimumWallMs?: number | null;
  /** Launches per hour at the shortest recent mean run duration among the target's suites; null without a basis. */
  projectedRunsPerHour?: number | null;
  /** K × R, the launches each battery member plans; null for a suite target. */
  memberPlanRuns?: number | null;
  /** The most launches one battery run may plan. */
  maxBatteryMembers: number;
  /** The spend guard admits the first launch now. */
  spendAllowedNow: boolean;
  spendDenialReason?: string | null;
  /** The spend guard's denial is a cap that a wait can outlast. */
  spendDenialIsCap: boolean;
}

/** What a member's projection rests on: its own recent runs, partly its own, the target's mean, or nothing. */
export type BenchmarkModelBatchProjectionBasis = 'OwnRuns' | 'Mixed' | 'TargetMean' | 'None';

/** One model's share of the projection, with what it was projected from. */
export interface BenchmarkModelBatchProjectionMemberDto {
  modelConfigurationId: number;
  plannedRunCount: number;
  projectedCostUsd?: number | null;
  projectedWallMs?: number | null;
  basis: BenchmarkModelBatchProjectionBasis;
}

export interface BenchmarkModelBatchProjectionDto {
  /** L: models × R on a suite, models × K × R on a battery. */
  plannedRunCount: number;
  /** Null while any member has no priced basis. */
  projectedCostUsd?: number | null;
  /** Null while any member has no timed basis. */
  projectedWallMs?: number | null;
  limits: BenchmarkModelBatchLimitsDto;
  members: BenchmarkModelBatchProjectionMemberDto[];
}

/** What `preflight` answers: every finding starting the request now would meet, and the projection. */
export interface BenchmarkModelBatchPreflightResponse {
  findings: BenchmarkModelBatchFindingDto[];
  projection: BenchmarkModelBatchProjectionDto;
  /** `Benchmark:ModelBatch:MaxModels`. */
  maxModels: number;
}

/** The model a member runs, as the batch recorded it at start. */
export interface BenchmarkModelBatchMemberModelDto {
  /** The configuration's id; it may since have been deleted. */
  configurationId: number;
  displayName: string;
  provider: string;
  modelId: string;
  thinkingLevel?: string | null;
  reasoningMode?: string | null;
  serviceTier?: string | null;
  parallelExecutionMode?: number | null;
  /** "official", or a custom endpoint's description. Never a hostname. */
  endpoint: string;
  maxOutputTokens?: number | null;
}

/** A member's result, from the run summary or the battery run it produced. */
export interface BenchmarkModelBatchMemberResultDto {
  /** A suite target's Intelligence Index (a series' mean). */
  intelligenceIndex?: number | null;
  /** A battery target's Overall Index. */
  overallIndex?: number | null;
  /** The half-width of the index's interval; null for a single run. */
  indexHalfWidth?: number | null;
  /** Where the index comes from. */
  indexSource?: string | null;
  medianModelTimeMs?: number | null;
  ttftP50Ms?: number | null;
  candidateCostPerQuestionUsd?: number | null;
  candidateCostUsd?: number | null;
  totalCostUsd?: number | null;
  refutedClaims: number;
  confirmedCriticalErrors: number;
  /** The share of model time spent waiting on the provider; null without timing. */
  ownWaitShare?: number | null;
  failedAnswers: number;
  providerErrors: number;
  retries: number;
}

/** The instrument a member ran under. */
export interface BenchmarkModelBatchInstrumentDto {
  candidateSystemPromptSha256?: string | null;
  toolGuidesSha256?: string | null;
  knowledgeBaseHeadSha?: string | null;
  wikiHeadSha?: string | null;
  sourceCodeHeadSha?: string | null;
  harnessVersion?: string | null;
  scoringMethodVersion?: number | null;
  /** The corpus index fingerprints, as JSON. */
  corpusIndexFingerprintsJson?: string | null;
}

/** One model of a model batch, with the run, series or battery run it produced. */
export interface BenchmarkModelBatchMemberDto {
  id: number;
  /** 0-based position in the run order. */
  orderIndex: number;
  model: BenchmarkModelBatchMemberModelDto;
  status: BenchmarkModelBatchMemberStatus;
  /** A single run's id (Suite target, R = 1). */
  runId?: number | null;
  /** A replicate series' id (Suite target, R ≥ 2). */
  seriesId?: number | null;
  /** A battery run's id (Battery target). */
  batteryRunId?: number | null;
  /** Every run the member launched, oldest first. */
  runIds: number[];
  /** The member's run in flight, if any. */
  currentRunId?: number | null;
  /** The stage of the run in flight, as the server names it. */
  currentStage?: string | null;
  /** The step the member is on: a series' run or a battery's slot. */
  currentStepIndex?: number | null;
  /** The member's steps: R runs on a suite, K × R slots on a battery. */
  stepCount: number;
  /** The answers given so far in the run in flight, and the questions it asks. */
  answeredQuestionCount: number;
  totalQuestionCount: number;
  result?: BenchmarkModelBatchMemberResultDto | null;
  instrument?: BenchmarkModelBatchInstrumentDto | null;
  /** The instrument keys that moved since the first member; empty when none did. */
  instrumentDriftKeys: string[];
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  errorMessage?: string | null;
}

/** A resume mode valid now, with its button label and why it is offered. */
export interface BenchmarkModelBatchResumeOptionDto {
  mode: BenchmarkModelBatchResumeMode;
  label: string;
  reason?: string | null;
}

export interface BenchmarkModelBatchRunDto {
  id: number;
  status: BenchmarkModelBatchStatus;
  /** Null unless stopped. */
  stopReason?: BenchmarkModelBatchStopReason | null;
  stopReasonText?: string | null;
  /** What stopped it in detail, such as the keys that moved. */
  stopDetail?: string | null;
  targetKind: BenchmarkModelBatchTargetKind;
  suiteId?: number | null;
  batteryId?: number | null;
  targetName?: string | null;
  /** The battery's definition revision and hash at start; null for a suite. */
  batteryRevision?: number | null;
  batteryDefinitionSha256?: string | null;
  /** The target's suites in run order; one for a suite target. */
  suiteNames: string[];
  runsPerModel: number;
  order: BenchmarkModelBatchOrder;
  /** The seed a randomized order was drawn from; null under `AsListed`. */
  orderSeed?: number | null;
  allowCapWait: boolean;
  /** Every member's request template. */
  run?: StartBenchmarkRunRequest | null;
  createdAtUtc: string;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  createdByUserName?: string | null;
  /** In run order. */
  members: BenchmarkModelBatchMemberDto[];
  /** The member running, else the next to start, as a 0-based index into `members`; null when neither applies. */
  currentMemberIndex?: number | null;
  /** M, the models in the batch. */
  requestedMemberCount: number;
  completedMemberCount: number;
  failedMemberCount: number;
  skippedMemberCount: number;
  /** The estimated cost so far, in US dollars. */
  liveCandidateCostUsd?: number | null;
  liveTotalCostUsd?: number | null;
  /** The warnings acknowledged at start. */
  acknowledgedFindings: BenchmarkModelBatchFindingDto[];
  /** The advice shown at start. */
  adviceAtStart: BenchmarkModelBatchFindingDto[];
  firstMemberInstrument?: BenchmarkModelBatchInstrumentDto | null;
  /** The operator continued over an instrument change; the batch is not comparable across it. */
  instrumentChangeAcknowledged: boolean;
  /** This server process is driving the batch now. */
  isDriving: boolean;
  resumable: boolean;
  resumeOptions: BenchmarkModelBatchResumeOptionDto[];
  lastProgressAtUtc?: string | null;
  /** No progress for longer than `stallMinutes`; informational. */
  stalled: boolean;
  /** `Benchmark:ModelBatch:StallMinutes`. */
  stallMinutes: number;
  /** The members a re-run under the current instrument replaced, as JSON. */
  supersededMembersJson?: string | null;
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
  /** The newest member run's candidate, shared by every member; null for an empty group. */
  testedModelDisplayName?: string | null;
  testedModelProvider?: string | null;
  testedModelId?: string | null;
  testedModelThinkingLevel?: string | null;
  testedModelReasoningMode?: string | null;
  tier: BenchmarkComparabilityTier;
  tierLabel: string;
  comparabilityKeyHash?: string | null;
  /** The stored hash was computed under another comparability key definition; re-analyse to refresh it. */
  comparabilityKeyStale?: boolean;
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

// ---------------------------------------------------------------------------------------------
// Report packs and stored report documents. The enums travel as numbers, as the server has no
// string enum converter; the render endpoint alone takes lower-case names in its query string.
// ---------------------------------------------------------------------------------------------

/** The reader a report-pack document is written for. */
export enum BenchmarkReportAudience {
  ExecutiveSummary = 1,
  TechnicalReport = 2,
  InternalBrief = 3,
  /** A chat consistency finding written for the model's provider; chat consistency documents only. */
  ProviderIssueReport = 4
}

/** How much verbatim benchmark content a rendered document prints. */
export enum BenchmarkReportDisclosure {
  /** Questions described by topic; no question text, rubric, answer or grader evidence. */
  Summary = 1,
  /** Verbatim text and answer excerpts for every question; no rubric or grader evidence. */
  Detailed = 2,
  /** Everything, rubrics and grader evidence included. Internal only. */
  Full = 3
}

/** Whether peers are printed by name or as "Model A", "Model B"…. */
export enum BenchmarkReportPeerNaming {
  Named = 1,
  Anonymized = 2
}

/**
 * Where a stored document came from: Model Comparison's Report Pack, a run's or a battery run's
 * completion, or a saved chat consistency analysis.
 */
export enum BenchmarkReportDocumentOrigin {
  ReportPack = 1,
  RunCompletion = 2,
  BatteryCompletion = 3,
  ChatConsistencyReport = 4
}

/** Where a run's two AI-written (run-completion) documents stand. */
export enum BenchmarkRunReportDocumentsStatus {
  NotRequested = 0,
  Pending = 1,
  Writing = 2,
  Completed = 3,
  CompletedWithWarnings = 4,
  Failed = 5,
  Skipped = 6,
  /** An administrator canceled the job; documents written before the cancellation are kept. */
  Canceled = 7
}

/** Writes a finished run's missing AI-written documents now, with this writer. */
export interface WriteRunReportDocumentsRequest {
  writerModelConfigurationId: number;
  /** The documents to write; absent or empty writes every missing one. */
  audiences?: BenchmarkReportAudience[];
  /** The operator acknowledged that the writer shares the model under test's provider. */
  acknowledgeSameProvider?: boolean;
}

export interface WriteRunReportDocumentsResponse {
  runId: number;
  status: BenchmarkRunReportDocumentsStatus;
  /** The documents the job will write. */
  audiences?: BenchmarkReportAudience[];
}

/** `Queued`, `Preparing`, `Writing` or `Finished`. */
export type BenchmarkRunReportJobPhase = 'Queued' | 'Preparing' | 'Writing' | 'Finished';

/**
 * A run's report-writing job as this server process knows it: the run's persisted status plus the
 * job's phase, queue position, timestamps and progress. Kept in memory only, for 6 hours after the
 * job finishes; after a restart the endpoint answers 204.
 */
export interface BenchmarkRunReportJobDto {
  runId: number;
  status: BenchmarkRunReportDocumentsStatus;
  message: string | null;
  phase: BenchmarkRunReportJobPhase;
  queuedAtUtc: string;
  slotAcquiredAtUtc: string | null;
  finishedAtUtc: string | null;
  cancelRequestedAtUtc: string | null;
  /** Jobs ahead in the report writer's queue; only while queued. */
  jobsAhead: number | null;
  /** The running job that holds the slot, prefixed `Report Pack:` or `Run #N:`. */
  blockingJobLabel: string | null;
  audiences: BenchmarkReportAudience[];
  writerConfigId: number;
  writerDisplayName: string;
  writerProvider: string;
  writerModelId: string;
  writerThinkingLevel: string | null;
  job: BenchmarkReportPackJobDto;
  /** The server's clock when the response was built; elapsed times are measured against it. */
  serverTimeUtc: string;
}

export interface BenchmarkRunReportEstimateRequest {
  writerModelConfigurationId: number;
  /** Absent or empty estimates every missing document. */
  audiences?: BenchmarkReportAudience[];
}

export interface BenchmarkRunReportEstimateDto {
  estimates: BenchmarkReportPackAudienceEstimateDto[];
  /** Null when the writer has no resolvable price. */
  estimatedTotalCostUsd: number | null;
  /** Why the writer cannot write this run's reports, or null. */
  refusal: string | null;
  /** The same-provider warning writing would ask to acknowledge, or null. */
  sameProviderWarning: SameProviderWarningDto | null;
}

/** The comparison's pricing basis as the report-pack request body carries it: a number. */
export enum BenchmarkReportPackPricingBasis {
  AsRun = 0,
  Current = 1
}

/** The render endpoint's `disclosure` query value. */
export type BenchmarkReportDisclosureParam = 'summary' | 'detailed' | 'full';

/** The render endpoint's `peers` query value. */
export type BenchmarkReportPeerNamingParam = 'named' | 'anonymized';

export function reportDisclosureParam(disclosure: BenchmarkReportDisclosure): BenchmarkReportDisclosureParam {
  switch (disclosure) {
    case BenchmarkReportDisclosure.Detailed: return 'detailed';
    case BenchmarkReportDisclosure.Full: return 'full';
    default: return 'summary';
  }
}

export function reportPeerNamingParam(naming: BenchmarkReportPeerNaming): BenchmarkReportPeerNamingParam {
  return naming === BenchmarkReportPeerNaming.Named ? 'named' : 'anonymized';
}

/** Preview and start request. The first three fields mirror the model comparison's query. */
export interface BenchmarkReportPackRequest {
  runIds: number[];
  groupIds: number[];
  /** Battery results; never mixed with runs or groups. Absent reads as none. */
  batteryRunIds?: number[];
  pricingBasis: BenchmarkReportPackPricingBasis;
  /** The comparison entry key of the subject, `run:<id>`, `group:<id>` or `battery:<id>`. */
  subjectKey: string;
  audiences: BenchmarkReportAudience[];
  writerModelConfigurationId: number;
  /** The operator acknowledged that the writer shares the provider of the subject or of a covered model. */
  acknowledgeSameProvider: boolean;
  /** Per-model documents (the default) or comparison-scope documents over the covered models. */
  scope?: BenchmarkReportScope;
  /** Per-model scope: the subjects, each written in turn; empty or absent means `subjectKey` alone. */
  subjectKeys?: string[];
  /** Comparison scope: the covered entry keys; empty or absent means every entry that is not Excluded. */
  coveredEntryKeys?: string[];
  /**
   * Documents this job replaces, each deleted only once its replacement is stored. A requested
   * document already written and not named here is refused with 409.
   */
  replaceDocumentIds?: number[];
}

/** `POST report-packs/layout-preview`: a preview request for one document type, with its paper and chart layout. */
export interface BenchmarkReportPackLayoutPreviewRequest extends BenchmarkReportPackRequest {
  audience: BenchmarkReportAudience;
  paper: BenchmarkPdfPaper;
  /** Which copy's charts are sent; the preview renders that copy. */
  naming: BenchmarkReportPeerNamingParam;
  layout: ReportDocumentChartLayout | null;
}

/** One chart image of a layout preview; sent as the multipart file `<figureKey>.png`. */
export interface BenchmarkReportPackLayoutPreviewChart {
  figureKey: string;
  title: string;
  caption: string;
  altText: string;
  png: Blob;
}

export interface BenchmarkReportPackPeerDto {
  letter: string;
  entryKey: string;
  label: string;
  provider: string;
  state: string;
}

export interface BenchmarkReportPackAudienceEstimateDto {
  audience: BenchmarkReportAudience;
  promptChars: number;
  estimatedInputTokens: number;
  estimatedOutputTokens: number;
  /** Null when the writer has no resolvable price. */
  estimatedCostUsd: number | null;
  /** The subject the estimate is for: a model's entry key or the comparison-scope subject key. */
  subjectKey?: string | null;
  /** Estimated input tokens as a share of the writer's context window; null when the window is unknown. Above 0.9 is refused. */
  contextWindowShare?: number | null;
}

export interface BenchmarkReportPackPreviewDto {
  subjectKey: string;
  subjectLabel: string;
  subjectState: string;
  suiteName: string;
  peers: BenchmarkReportPackPeerDto[];
  estimates: BenchmarkReportPackAudienceEstimateDto[];
  /** Sum of the first call of each estimate; a repair turn can roughly double a document. */
  estimatedTotalCostUsd: number | null;
  writerDisplayName: string | null;
  /** The same-provider warning, or null. Starting requires acknowledging it. */
  sameProviderWarning: string | null;
  /** Why the pack cannot be generated as requested, or null when it can. */
  refusal: string | null;
  /**
   * The newest document already written for this comparison and subject, per audience; such an
   * audience cannot be started again. Absent from a server that predates it.
   */
  writtenDocuments?: BenchmarkReportPackWrittenDocumentDto[];
  /** The scope previewed, as requested. */
  scope?: BenchmarkReportScope;
  /** The numbered comparison, once identified; null before. */
  comparisonId?: number | null;
  comparisonName?: string | null;
  /** The comparison's entries that are not Excluded: the M of "2 of 5 models". */
  comparisonEntryCount?: number;
  /** Comparison scope: the covered set is every entry that is not Excluded. */
  coversAllEntries?: boolean;
  /** Comparison scope: the covered set's key; null for per-model scope. */
  coveredSetKey?: string | null;
  /** Comparison scope: the covered models in letter order, with letters. Per-model scope: the subjects, without. */
  coveredModels?: BenchmarkReportCoveredModelDto[];
  /** Comparison scope: every other covered set of this comparison that has documents. */
  otherModelSets?: BenchmarkReportPackCoveredSetDto[];
  /** The per-model documents of this comparison, per subject (or per covered model). */
  subjectDocuments?: BenchmarkReportPackSubjectDocumentsDto[];
  /** The writer configuration's context window in tokens; null when unknown. */
  writerContextWindowTokens?: number | null;
}

/** A Report Pack document already stored for the previewed comparison. */
export interface BenchmarkReportPackWrittenDocumentDto {
  audience: BenchmarkReportAudience;
  documentId: number;
  createdAtUtc: string;
  writerDisplayName: string | null;
  /** An entry key, `comparison:<id>` or `comparison:<id>/<16 hex>`. */
  subjectKey?: string;
  /** `Completed` or `CompletedWithWarnings`. */
  status?: string;
  writerProvider?: string | null;
  writerModelId?: string | null;
  writerThinkingLevel?: string | null;
  durationMs?: number;
  costUsd?: number | null;
}

/** The comparison-scope documents of one covered set of a comparison. */
export interface BenchmarkReportPackCoveredSetDto {
  coveredSetKey: string;
  subjectKey: string;
  coversAllEntries: boolean;
  coveredModels: BenchmarkReportCoveredModelDto[];
  /** The newest document per audience, in audience order. */
  documents: BenchmarkReportPackWrittenDocumentDto[];
}

/** The per-model documents of one subject of a comparison. */
export interface BenchmarkReportPackSubjectDocumentsDto {
  subjectKey: string;
  subjectLabel: string;
  /** The newest document per audience, in audience order. */
  documents: BenchmarkReportPackWrittenDocumentDto[];
}

export interface BenchmarkReportPackStartResponse {
  jobId: string;
}

/** `Pending`, `Writing`, `Repairing`, `Completed`, `CompletedWithWarnings`, `Failed` or `Canceled`. */
export type BenchmarkReportPackDocumentStatus = string;

export interface BenchmarkReportPackDocumentProgressDto {
  audience: BenchmarkReportAudience;
  status: BenchmarkReportPackDocumentStatus;
  documentId: number | null;
  errorMessage: string | null;
  modelCalls: number;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  inputTokens?: number;
  outputTokens?: number;
  /** Null when the writer has no resolvable price. */
  costUsd?: number | null;
  /** The subject the document is written for: a model's entry key or the comparison-scope subject key. */
  subjectKey?: string;
  subjectLabel?: string;
}

export interface BenchmarkReportPackJobLogEntryDto {
  timestampUtc: string;
  message: string;
  severity: string;
}

export interface BenchmarkReportPackJobDto {
  id: string;
  packId: string;
  subjectKey: string;
  subjectLabel: string;
  suiteId: number | null;
  suiteName: string;
  writerConfigId: number;
  writerDisplayName: string;
  startedByUserId: string | null;
  startedAtUtc: string;
  completedAtUtc: string | null;
  /** `Running`, `Completed`, `CompletedWithErrors`, `Canceled` or `Failed`. */
  status: string;
  totalModelCalls: number;
  inputTokens: number;
  outputTokens: number;
  costUsd: number | null;
  documents: BenchmarkReportPackDocumentProgressDto[];
  log: BenchmarkReportPackJobLogEntryDto[];
  /** The server's clock when it answered; absent from a server that predates it. */
  serverTimeUtc?: string;
  scope?: BenchmarkReportScope;
  comparisonId?: number | null;
}

/** A stored document in a list; never carries rendered text. */
export interface BenchmarkReportDocumentListItemDto {
  id: number;
  packId: string;
  audience: BenchmarkReportAudience;
  title: string;
  subjectKey: string;
  subjectLabel: string;
  subjectRunIds: number[];
  suiteId: number | null;
  suiteName: string;
  writerDisplayName: string;
  writerProvider: string;
  writerModelId: string;
  writerThinkingLevel: string | null;
  sameProviderAcknowledged: boolean;
  status: string;
  reportFormatVersion: number;
  createdAtUtc: string;
  inputTokens: number;
  outputTokens: number;
  durationMs: number;
  costUsd: number | null;
  /** A subject run was re-scored, re-run or deleted since the document was written. */
  runChangedSinceGeneration: boolean;
  /** Subject runs that no longer exist. */
  missingRunIds: number[];
  /** The disclosure levels this document renders at. */
  allowedDisclosures: BenchmarkReportDisclosure[];
  /** Report Pack or run completion. Undefined on a list served before it existed: a Report Pack document. */
  origin?: BenchmarkReportDocumentOrigin;
  /*
   * The comparison fields below are always sent by the server; they are optional here only so that
   * fixtures written before them still type-check, and a missing one reads as absent.
   */
  /** The comparison the document was written for, as the server stores it; null without one. */
  comparisonKey?: string | null;
  /** How many entries (runs and groups) that comparison had. */
  comparisonEntryCount?: number;
  /** How many of them were the subject's peers. */
  peerCount?: number;
  /** The pricing basis it was written on: `AsRun` or `Current`. */
  pricingBasis?: string;
  /** A peer's run was re-scored, re-run or deleted since the document was written. */
  peersChangedSinceGeneration?: boolean;
  /** How many chart images the document holds, both namings counted. */
  chartCount?: number;
  /** The figures it has charts for, in manifest order. */
  chartFigureKeys?: string[];
  /** The settings hash its charts were drawn with; null without charts. */
  chartSettingsHash?: string | null;
  /** Each peer's entry key → the letter the anonymized copy names it by (`run:69` → `A`). */
  peerLetters?: Record<string, string>;
  /** The numbered comparison (*Comparison #Id*); null for a document without one. */
  comparisonId?: number | null;
  /** The comparison's display name; null without a comparison. */
  comparisonName?: string | null;
  /** A per-model document or a comparison-scope one; absent reads as per-model. */
  scope?: BenchmarkReportScope;
  /** A comparison-scope document written over every non-excluded entry; false for per-model documents. */
  coversAllEntries?: boolean;
  /** The covered entry set's key. */
  coveredSetKey?: string | null;
  /** Comparison scope: the comparison's entries that were not Excluded when it was written, the M of "2 of 5 models". */
  comparisonModelCount?: number | null;
  /** The models the document covers: the subject for a per-model document, every covered entry otherwise. */
  coveredModels?: BenchmarkReportCoveredModelDto[];
  /** The saved chat consistency analysis a chat consistency document was written from; absent or null otherwise. */
  chatConsistencyAnalysisId?: number | null;
}

/** A per-model document (1), a comparison-scope document (2) or a chat consistency document (3), as the server sends it. */
export enum BenchmarkReportScope {
  Model = 1,
  Comparison = 2,
  ChatConsistency = 3,
}

/** One model (comparison entry) a document covers. */
export interface BenchmarkReportCoveredModelDto {
  /** A `run:`, `group:` or `battery:` entry key. */
  entryKey: string;
  label: string;
  provider: string | null;
  /** The model's letter in a comparison-scope document; null for a per-model document's subject. */
  letter?: string | null;
}

/** A numbered comparison: one selection of runs, groups or battery runs. */
export interface BenchmarkComparisonDto {
  id: number;
  /** The display name: the admin's rename, else the default name. */
  name: string;
  customName: string | null;
  defaultName: string;
  entryCount: number;
  subjectKind: 'Runs' | 'Batteries';
  entryKeys: string[];
  createdAtUtc: string;
  renamedAtUtc: string | null;
}

/** A comparison in `GET model-comparisons`, newest first. */
export interface BenchmarkComparisonListItemDto {
  id: number;
  name: string;
  customName: string | null;
  defaultName: string;
  entryCount: number;
  subjectKind: 'Runs' | 'Batteries';
  documentCount: number;
  lastDocumentAtUtc: string | null;
  createdAtUtc: string;
}

/** Which copy a chart image is for: the named copy or the anonymized one. */
export type ReportDocumentChartNaming = 'named' | 'anonymized';

/**
 * How the server places a document's charts: each figure's share of the text column, the row a
 * figure shares with the next one, and the tallest a figure may be as a share of the page. Stored in
 * the chart manifest; a manifest without it renders every figure full width, one per row, at 60 %.
 */
export interface ReportDocumentChartLayout {
  version: 1;
  /** One entry per figure key; a figure without one is full width on its own row. */
  figures: ReportDocumentChartLayoutFigure[];
  /** 0.4, 0.5 or 0.6. */
  maxHeightShare: number;
}

export interface ReportDocumentChartLayoutFigure {
  key: string;
  /** 1 (full column), 2/3 or 1/2. */
  widthShare: number;
  /** Consecutive figures with the same row group print side by side; null prints alone. */
  rowGroup: number | null;
}

/** One chart image for a report document, as `PUT report-documents/{id}/charts` takes it. */
export interface ReportDocumentChartUpload {
  /**
   * A Report Pack figure (`p1a-quality`, `p1b-speed`, `p1c-cost`, `p2-profile`, `s1-quality-speed`,
   * `s2-quality-cost` or `s3-speed-cost`) or a chat consistency one (`cc1-quality`, `cc2-speed`,
   * `cc3-work` or `cc4-timeline`).
   */
  figureKey: string;
  naming: ReportDocumentChartNaming;
  title: string;
  caption: string;
  altText: string;
  /** 64 hex characters: the hash of the settings the image was drawn with. */
  settingsHash: string;
  /** The PNG, base64 without a data-URL prefix. */
  pngBase64: string;
}

/** A document's chart set after a replace. */
export interface ReportDocumentChartsSummaryDto {
  documentId: number;
  chartCount: number;
  figureKeys: string[];
  settingsHash: string | null;
}

/** One validation problem, and whether the offending item was dropped. */
export interface BenchmarkReportValidationNote {
  /** The validator rule number, 1–12. */
  rule: number;
  /** Where: `headline`, `sections.abstract`, `weaknesses[1]`, …. */
  location: string;
  message: string;
  /** The item or paragraph was removed from the stored output. */
  dropped: boolean;
}

export interface BenchmarkReportDocumentDetailDto extends BenchmarkReportDocumentListItemDto {
  pricingSource: string | null;
  answerExcerptChars: number;
  writerPromptSha256: string;
  validationNotes: BenchmarkReportValidationNote[];
  factsJson: string;
}

/** Which stored documents to list; every field is optional. */
export interface BenchmarkReportDocumentQuery {
  suiteId?: number | null;
  /**
   * Every document whose subject includes this run, a group's, a battery's or a Report Pack's
   * included; a peer's run never matches. The run Download Center lists only the run's own
   * documents and uses `subject` (`run:<id>`) instead.
   */
  runId?: number | null;
  take?: number | null;
  /** A comparison's entry keys (`run:<id>`, `group:<id>`): the documents written for exactly that set. */
  comparison?: readonly string[] | null;
  origin?: BenchmarkReportDocumentOriginParam | null;
  /** Documents about exactly this subject: `run:<id>`, `group:<id>`, `battery:<id>` or `chat-consistency:<id>`. */
  subject?: string | null;
  /** Documents of this numbered comparison. */
  comparisonId?: number | null;
}

/** The list endpoint's `origin` query value. */
export type BenchmarkReportDocumentOriginParam = 'reportPack' | 'runCompletion' | 'batteryCompletion' | 'chatConsistencyReport';

/** A text file fetched from the server, with the name its `Content-Disposition` gave it. */
export interface BenchmarkTextFile {
  text: string;
  fileName: string;
}

/** The paper a PDF or Word document is laid out on. */
export type BenchmarkPdfPaper = 'a4' | 'letter';

/** A binary file fetched from the server, with the name its `Content-Disposition` gave it, if any. */
export interface BenchmarkBinaryFile {
  bytes: Uint8Array;
  fileName: string | null;
}

/**
 * An error of an `arraybuffer` request with its body decoded: JSON (`{ error }`) where it parses,
 * else the text. Anything else is returned unchanged.
 */
export function decodeBinaryErrorBody(error: unknown): unknown {
  if (!(error instanceof HttpErrorResponse) || !(error.error instanceof ArrayBuffer)) {
    return error;
  }
  let body: unknown = null;
  try {
    const text = new TextDecoder().decode(new Uint8Array(error.error));
    try {
      body = JSON.parse(text);
    } catch {
      body = text;
    }
  } catch {
    body = null;
  }
  return new HttpErrorResponse({
    error: body,
    headers: error.headers,
    status: error.status,
    statusText: error.statusText,
    url: error.url ?? undefined
  });
}

/**
 * The file name a `Content-Disposition` header carries: `filename*=UTF-8''…` first (RFC 5987,
 * percent-decoded), then `filename="…"` or a bare `filename=…`, else the fallback.
 */
export function fileNameFromContentDisposition(header: string | null | undefined, fallback: string): string {
  if (!header) {
    return fallback;
  }
  const extended = /filename\*\s*=\s*([^']*)'[^']*'([^;]+)/i.exec(header);
  if (extended) {
    try {
      const decoded = decodeURIComponent(extended[2].trim().replace(/^"(.*)"$/, '$1'));
      if (decoded.trim() !== '') {
        return decoded.trim();
      }
    } catch {
      // A malformed escape falls through to the plain parameter.
    }
  }
  const quoted = /filename\s*=\s*"((?:\\.|[^"\\])*)"/i.exec(header);
  if (quoted && quoted[1].trim() !== '') {
    return quoted[1].replace(/\\(.)/g, '$1').trim();
  }
  const bare = /filename\s*=\s*([^;"\s][^;]*)/i.exec(header);
  if (bare && bare[1].trim() !== '') {
    return bare[1].trim();
  }
  return fallback;
}

// Paired tests: the wizard's Paired tests view, the run report's and the battery report's Paired Test tabs.

/** Which pairs the wizard's Paired tests view tests. */
export type BenchmarkPairedComparisonMode = 'Reference' | 'AllPairs';

/** The wizard's paired tests over the same sources as the comparison, which the server compares again. */
export interface BenchmarkPairedComparisonRequest {
  runIds: number[];
  groupIds: number[];
  batteryRunIds: number[];
  pricingBasis: BenchmarkReportPackPricingBasis;
  /** Ignored with two comparable entries. */
  mode: BenchmarkPairedComparisonMode;
  /** The entry every other one is tested against in Reference mode; null takes the highest Intelligence Index. */
  referenceKey?: string | null;
  /** Bypasses the server's ten-minute response cache. */
  recompute?: boolean;
}

/** One suite of a battery Intelligence test (M7). */
export interface BenchmarkPairedSuiteDetailDto {
  suiteIndex: number;
  suiteName: string;
  pairedItems: number;
  weightedDifference?: number | null;
  wilcoxonPValue?: number | null;
  /** Holm-adjusted across the suites with a Wilcoxon p. */
  holmAdjustedPValue?: number | null;
  note?: string | null;
}

/** One paired test: treatment B against baseline A on the questions both answered. */
export interface BenchmarkPairedTestDto {
  baselineKey: string;
  treatmentKey: string;
  pairedItems: number;
  unpairedItems: number;
  revisionMismatched: number;
  /** B − A for a difference; B ÷ A, the geometric-mean ratio, for a ratio. */
  effect?: number | null;
  effectLower?: number | null;
  effectUpper?: number | null;
  effectKind: 'Difference' | 'Ratio';
  dz?: number | null;
  pValue?: number | null;
  adjustedPValue?: number | null;
  method: string;
  direction: 'Higher' | 'Lower' | 'None';
  /** The adjusted p-value is below 0.05. */
  established: boolean;
  verdict: string;
  notTestedReason?: string | null;
  note?: string | null;
  /** A battery Intelligence row only. */
  suites?: BenchmarkPairedSuiteDetailDto[] | null;
}

/** One measure's family of paired tests. */
export interface BenchmarkPairedMeasureDto {
  /** `Intelligence`, `Accuracy`, `Completeness`, `Conciseness`, `Readability`, `Speed` or `Cost`. */
  measure: string;
  label: string;
  category: 'Intelligence' | 'QualityDimension' | 'Speed' | 'Cost';
  primary: boolean;
  /** The tests actually made: pairs with a p-value. */
  familySize: number;
  adjustment: 'None' | 'Holm';
  /** Ready to render: "Single comparison — no adjustment needed", "Holm-adjusted across 3 tests". */
  adjustmentNote: string;
  notTestedReason?: string | null;
  caption?: string | null;
  pairs: BenchmarkPairedTestDto[];
}

/** The wizard's paired tests: one family per measure. */
export interface BenchmarkPairedComparisonDto {
  computedAtUtc: string;
  subjectKind: 'Runs' | 'Batteries';
  pricingBasis: string;
  mode: BenchmarkPairedComparisonMode;
  referenceKey?: string | null;
  /** The comparable entries that took part, in the comparison's order. */
  entryKeys: string[];
  /** The most comparable entries All pairs is offered for. */
  allPairsLimit: number;
  singleRunCaveat?: string | null;
  /** That each measure is its own family and the measures are not adjusted for each other. */
  measuresNote: string;
  measures: BenchmarkPairedMeasureDto[];
}

/** The kind of paired comparison two runs, or two battery results, make. */
export type BenchmarkPairKind = 'ModelComparison' | 'Verification' | 'Replicate';

/** One pair tested on every measure, unadjusted: two runs on one suite, or two battery results of one definition. */
export interface BenchmarkPairComparisonDto {
  computedAtUtc: string;
  subjectKind: 'Run' | 'Battery';
  treatmentId: number;
  baselineId: number;
  treatmentKey: string;
  baselineKey: string;
  treatmentLabel: string;
  baselineLabel: string;
  kind: BenchmarkPairKind;
  kindLabel: string;
  explanation: string;
  /** The model-axis keys of a model comparison, the instrument key of a verification. */
  changedKeys: string[];
  differences: BenchmarkComparabilityDifferenceDto[];
  speedDegraded: boolean;
  speedDegradingKeys: string[];
  costDegraded: boolean;
  costDegradingKeys: string[];
  singleRunCaveat?: string | null;
  pricingBasis: string;
  measures: BenchmarkPairedMeasureDto[];
}

/** The kind of paired comparison one candidate baseline would make with a run. */
export interface BenchmarkRunPairKindDto {
  runId: number;
  kind: BenchmarkPairKind | 'NotComparable';
  kindLabel: string;
  changedKeys: string[];
  /** For `NotComparable`, the reason. */
  explanation: string;
}
/** The root of the battery endpoints (`AdminBenchmarkBatteriesController`). */
const BATTERIES_ENDPOINT = '/api/admin/benchmark/batteries';

/** The root of the model batch endpoints (`AdminBenchmarkModelBatchesController`). */
const MODEL_BATCHES_ENDPOINT = '/api/admin/benchmark/model-batches';

@Injectable({
  providedIn: 'root'
})
export class AdminBenchmarkService {
  private http = inject(HttpClient);

  // Scoring Profiles
  getScoringProfiles(): Observable<BenchmarkScoringProfileDto[]> {
    return this.http.get<BenchmarkScoringProfileDto[]>('/api/admin/benchmark/scoring-profiles');
  }

  getRubricAuthoringGuidance(): Observable<RubricAuthoringGuidance> {
    return this.http.get<RubricAuthoringGuidance>('/api/admin/benchmark/rubric-authoring-guidance');
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

  /** Creates a new suite from an imported YAML document. An existing suite is never overwritten. */
  importSuite(req: ImportBenchmarkSuiteRequest): Observable<BenchmarkSuiteDto> {
    return this.http.post<BenchmarkSuiteDto>('/api/admin/benchmark/suites/import', req);
  }

  /** The read-only preflight behind the import review step's snapshot sentence. Writes nothing. */
  matchSnapshot(text: string): Observable<MatchSnapshotResult> {
    return this.http.post<MatchSnapshotResult>('/api/admin/benchmark/snapshots/match', { text });
  }

  /** Attaches a board built from an uploaded file to the suite; replacing a current snapshot needs `replaceExisting`. */
  uploadSuiteSnapshot(suiteId: number, req: UploadSuiteSnapshotRequest): Observable<CaptureBenchmarkSnapshotResponse> {
    return this.http.post<CaptureBenchmarkSnapshotResponse>(`/api/admin/benchmark/suites/${suiteId}/snapshot`, req);
  }

  /**
   * Drafts a Markdown suite description from the suite's name, questions and (optionally) its
   * game snapshot in a single synchronous model call. Nothing is persisted server-side; the
   * caller applies the text through the ordinary suite update.
   */
  generateSuiteDescription(suiteId: number, req: GenerateSuiteDescriptionRequest): Observable<SuiteDescriptionGenerationResultDto> {
    return this.http.post<SuiteDescriptionGenerationResultDto>(
      `/api/admin/benchmark/suites/${suiteId}/description-generation`, req);
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

  /** Replaces questions that carry an id and creates the rest; never deletes or reorders. */
  importQuestions(suiteId: number, req: ImportBenchmarkQuestionsRequest): Observable<ImportBenchmarkQuestionsResultDto> {
    return this.http.post<ImportBenchmarkQuestionsResultDto>(`/api/admin/benchmark/suites/${suiteId}/questions/import`, req);
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
   * The board the run was made with, from its own stored board record. 404 when the run had no
   * board; 409 with the refusal message as a plain string body when the board is unknown.
   */
  getRunBoard(runId: number): Observable<BenchmarkRunBoardDto> {
    return this.http.get<BenchmarkRunBoardDto>(`/api/admin/benchmark/runs/${runId}/board`);
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
   * Re-grades one answer of a panel run with the run's own members: A, B or both. No assessor
   * override is sent, since the server refuses one on a panel run.
   */
  reassessPanelAnswer(runId: number, answerId: number, member: BenchmarkPanelMember): Observable<{ runId: number }> {
    return this.http.post<{ runId: number }>(`/api/admin/benchmark/runs/${runId}/answers/${answerId}/reassess`, { member });
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

  /**
   * Grades every answer of a run with another model and records only the agreement statistics.
   * `compareAgainst` is sent only when given; the server reads its absence as `Assessor`.
   */
  calibrateAssessor(
    runId: number,
    assessorModelConfigurationId: number,
    compareAgainst?: BenchmarkCalibrationTarget | null
  ): Observable<BenchmarkAssessorCalibrationDto> {
    const body = compareAgainst
      ? { assessorModelConfigurationId, compareAgainst }
      : { assessorModelConfigurationId };
    return this.http.post<BenchmarkAssessorCalibrationDto>(
      `/api/admin/benchmark/runs/${runId}/calibrate`, body);
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

  /**
   * Replaces the snapshot text. The server normalizes it, recomputes the SHA-256 and character
   * count, rebuilds the digest, and returns the stored snapshot with its text alongside the
   * board-facts check run against the new text.
   */
  updateSnapshotText(id: number, req: UpdateBenchmarkGameSnapshotTextRequest): Observable<UpdateBenchmarkSnapshotTextResponse> {
    return this.http.put<UpdateBenchmarkSnapshotTextResponse>(`/api/admin/benchmark/snapshots/${id}/text`, req);
  }

  /** The BOARD FACTS quote check for a suite's current board, computed on demand. Null when the suite has no board. */
  getBoardFactsCheck(suiteId: number): Observable<BoardFactsCheckDto | null> {
    return this.http.get<BoardFactsCheckDto | null>(`/api/admin/benchmark/suites/${suiteId}/board-facts-check`);
  }

  /** Rebuilds the digest from the snapshot's own text; the snapshot text itself is untouched. */
  regenerateSnapshotDigest(id: number): Observable<BenchmarkGameSnapshotDto> {
    return this.http.post<BenchmarkGameSnapshotDto>(`/api/admin/benchmark/snapshots/${id}/regenerate-digest`, {});
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

  retryQuestionGeneration(jobId: string, req: RetryQuestionGenerationRequest): Observable<{ jobId: string }> {
    return this.http.post<{ jobId: string }>(`/api/admin/benchmark/question-generations/${jobId}/retry`, req);
  }

  regenerateQuestions(req: RegenerateQuestionsRequest): Observable<{ jobId: string }> {
    return this.http.post<{ jobId: string }>('/api/admin/benchmark/question-generations/regenerate', req);
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

  // Multi-suite batteries

  getBatteries(): Observable<BenchmarkBatteryDto[]> {
    return this.http.get<BenchmarkBatteryDto[]>(BATTERIES_ENDPOINT);
  }

  getBattery(id: number): Observable<BenchmarkBatteryDto> {
    return this.http.get<BenchmarkBatteryDto>(`${BATTERIES_ENDPOINT}/${id}`);
  }

  createBattery(req: CreateBenchmarkBatteryRequest): Observable<BenchmarkBatteryDto> {
    return this.http.post<BenchmarkBatteryDto>(BATTERIES_ENDPOINT, req);
  }

  updateBattery(id: number, req: UpdateBenchmarkBatteryRequest): Observable<BenchmarkBatteryDto> {
    return this.http.put<BenchmarkBatteryDto>(`${BATTERIES_ENDPOINT}/${id}`, req);
  }

  deleteBattery(id: number): Observable<void> {
    return this.http.delete<void>(`${BATTERIES_ENDPOINT}/${id}`);
  }

  /** Hides a battery from the launcher, or shows it again with `archived` false. */
  archiveBattery(id: number, archived = true): Observable<BenchmarkBatteryDto> {
    return this.http.post<BenchmarkBatteryDto>(`${BATTERIES_ENDPOINT}/${id}/archive`, { archived });
  }

  /**
   * Battery runs, newest first; only those of one battery when `batteryId` is given. `take` is
   * clamped by the server to 1000; without it the server returns 50.
   */
  getBatteryRuns(batteryId?: number, take?: number): Observable<BenchmarkBatteryRunDto[]> {
    let params = new HttpParams();
    if (batteryId != null) {
      params = params.set('batteryId', String(batteryId));
    }
    if (take != null) {
      params = params.set('take', String(take));
    }
    return this.http.get<BenchmarkBatteryRunDto[]>(`${BATTERIES_ENDPOINT}/runs`, { params });
  }

  /**
   * Deletes a battery run with its analyses. Its member runs stay as single runs unless
   * `deleteMembers`; a run that is also a member of another battery run is always kept. 409 while
   * the battery run is live.
   */
  deleteBatteryRun(id: number, deleteMembers = false): Observable<void> {
    const params = new HttpParams().set('deleteMembers', String(deleteMembers));
    return this.http.delete<void>(`${BATTERIES_ENDPOINT}/runs/${id}`, { params });
  }

  /**
   * Answers 202 with the new id. A refusal arrives as an error: 409 for a conflict, a same-provider
   * warning body or an `instrumentChanged` body; 429 when spend is denied; 400 for an invalid request.
   */
  startBatteryRun(req: StartBenchmarkBatteryRunRequest): Observable<{ batteryRunId: number }> {
    return this.http.post<{ batteryRunId: number }>(`${BATTERIES_ENDPOINT}/runs`, req);
  }

  /** The driven battery run, else the newest live or stopped one; a 204 arrives as null. */
  getActiveBatteryRun(): Observable<BenchmarkBatteryRunDto | null> {
    return this.http.get<BenchmarkBatteryRunDto | null>(`${BATTERIES_ENDPOINT}/runs/active`);
  }

  getBatteryRun(id: number): Observable<BenchmarkBatteryRunDto> {
    return this.http.get<BenchmarkBatteryRunDto>(`${BATTERIES_ENDPOINT}/runs/${id}`);
  }

  cancelBatteryRun(id: number): Observable<void> {
    return this.http.post<void>(`${BATTERIES_ENDPOINT}/runs/${id}/cancel`, {});
  }

  /** Refusals map as `startBatteryRun`'s do. */
  resumeBatteryRun(id: number, mode: BenchmarkBatteryResumeMode): Observable<{ batteryRunId: number }> {
    return this.http.post<{ batteryRunId: number }>(`${BATTERIES_ENDPOINT}/runs/${id}/resume`, { mode });
  }

  /**
   * Which slots earlier runs would fill for this start body, judged against the fingerprints a start
   * would record now. 404 for an unknown battery, 400 for a body that cannot be judged.
   */
  previewBatteryReuse(req: StartBenchmarkBatteryRunRequest): Observable<BenchmarkBatteryReusePreviewDto> {
    return this.http.post<BenchmarkBatteryReusePreviewDto>(`${BATTERIES_ENDPOINT}/runs/reuse-preview`, req);
  }

  /**
   * Attaches an earlier run to one slot and answers with the updated battery run. 409 while it is
   * running or in a state that takes no run; 400 with the reason when the run or the slot does not qualify.
   */
  attachBatteryMember(batteryRunId: number, body: BenchmarkBatteryAttachDto): Observable<BenchmarkBatteryRunDto> {
    return this.http.post<BenchmarkBatteryRunDto>(`${BATTERIES_ENDPOINT}/runs/${batteryRunId}/members`, body);
  }

  /** The runs of one slot's suite and tested configuration, newest first, each with whether it may be attached. */
  getBatteryAttachCandidates(
    batteryRunId: number, suiteIndex: number, round: number
  ): Observable<BenchmarkBatteryAttachCandidateDto[]> {
    const params = new HttpParams().set('suiteIndex', String(suiteIndex)).set('round', String(round));
    return this.http.get<BenchmarkBatteryAttachCandidateDto[]>(
      `${BATTERIES_ENDPOINT}/runs/${batteryRunId}/members/candidates`, { params });
  }

  /** Computes and stores an analysis, paired against a baseline battery run when one is given. */
  analyseBatteryRun(id: number, compareWithBatteryRunId?: number | null): Observable<BenchmarkBatteryAnalysisDto> {
    const req: BenchmarkBatteryCompareRequest = { compareWithBatteryRunId: compareWithBatteryRunId ?? null };
    return this.http.post<BenchmarkBatteryAnalysisDto>(`${BATTERIES_ENDPOINT}/runs/${id}/analysis`, req);
  }

  /** The latest stored analysis; a 204 arrives as null. */
  getBatteryAnalysis(id: number): Observable<BenchmarkBatteryAnalysisDto | null> {
    return this.http.get<BenchmarkBatteryAnalysisDto | null>(`${BATTERIES_ENDPOINT}/runs/${id}/analysis`);
  }

  /** The Markdown report, used like getRunReportUrl: window.open, not an XHR. */
  getBatteryReportUrl(id: number): string {
    return `${BATTERIES_ENDPOINT}/runs/${id}/report`;
  }

  /** The latest analysis of every battery run with this definition hash, grouped by comparability class. */
  getBatteryLeaderboard(definitionSha256: string): Observable<BenchmarkBatteryLeaderboardDto> {
    const params = new HttpParams().set('definitionSha256', definitionSha256);
    return this.http.get<BenchmarkBatteryLeaderboardDto>(`${BATTERIES_ENDPOINT}/leaderboard`, { params });
  }

  // Model batches

  /** Every guardrail finding starting this request now would meet, with the projection. Creates and spends nothing. */
  preflightModelBatch(req: StartBenchmarkModelBatchRequest): Observable<BenchmarkModelBatchPreflightResponse> {
    return this.http.post<BenchmarkModelBatchPreflightResponse>(`${MODEL_BATCHES_ENDPOINT}/preflight`, req);
  }

  /**
   * Starts a model batch; 201 with the batch. A guardrail refusal is a `BenchmarkModelBatchRefusalDto`:
   * 400 for a blocker, 409 for an unacknowledged warning or a busy runner (MB-B09). A 409 with a plain
   * string is a lost race for the runner; 404 an unknown target.
   */
  startModelBatch(req: StartBenchmarkModelBatchRequest): Observable<BenchmarkModelBatchRunDto> {
    return this.http.post<BenchmarkModelBatchRunDto>(`${MODEL_BATCHES_ENDPOINT}/runs`, req);
  }

  /** The live or resumable model batch; a 204 arrives as null. */
  getActiveModelBatch(): Observable<BenchmarkModelBatchRunDto | null> {
    return this.http.get<BenchmarkModelBatchRunDto | null>(`${MODEL_BATCHES_ENDPOINT}/runs/active`);
  }

  /** Model batches, newest first. */
  listModelBatches(skip?: number, take?: number): Observable<BenchmarkModelBatchRunDto[]> {
    let params = new HttpParams();
    if (skip != null) {
      params = params.set('skip', String(skip));
    }
    if (take != null) {
      params = params.set('take', String(take));
    }
    return this.http.get<BenchmarkModelBatchRunDto[]>(`${MODEL_BATCHES_ENDPOINT}/runs`, { params });
  }

  getModelBatch(id: number): Observable<BenchmarkModelBatchRunDto> {
    return this.http.get<BenchmarkModelBatchRunDto>(`${MODEL_BATCHES_ENDPOINT}/runs/${id}`);
  }

  cancelModelBatch(id: number): Observable<void> {
    return this.http.post<void>(`${MODEL_BATCHES_ENDPOINT}/runs/${id}/cancel`, {});
  }

  /**
   * Resumes a stopped model batch in one of the modes its `resumeOptions` offer. A 409
   * `BenchmarkModelBatchInstrumentChangedDto` names the keys that moved since the first member.
   */
  resumeModelBatch(id: number, mode: BenchmarkModelBatchResumeMode): Observable<BenchmarkModelBatchRunDto> {
    return this.http.post<BenchmarkModelBatchRunDto>(`${MODEL_BATCHES_ENDPOINT}/runs/${id}/resume`, { mode });
  }

  /** Marks a pending member Skipped. */
  skipModelBatchMember(id: number, memberId: number): Observable<BenchmarkModelBatchRunDto> {
    return this.http.post<BenchmarkModelBatchRunDto>(`${MODEL_BATCHES_ENDPOINT}/runs/${id}/members/${memberId}/skip`, {});
  }

  /** The batch's diagnostics as plain text. */
  getModelBatchDiagnostics(id: number): Observable<string> {
    return this.http.get(`${MODEL_BATCHES_ENDPOINT}/runs/${id}/diagnostics`, { responseType: 'text' });
  }

  /** Deletes a finished batch's record; its member runs, series and battery runs are kept. 204; 404 unknown; 409 while it is live. */
  deleteModelBatch(id: number): Observable<void> {
    return this.http.delete<void>(`${MODEL_BATCHES_ENDPOINT}/runs/${id}`);
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
   * The wizard's paired tests over the given sources. 400 `{ error }` or a string for a mixed
   * request, fewer than two comparable entries, All pairs above the limit, or an unknown reference.
   */
  getPairedComparison(request: BenchmarkPairedComparisonRequest): Observable<BenchmarkPairedComparisonDto> {
    return this.http.post<BenchmarkPairedComparisonDto>('/api/admin/benchmark/model-comparison/paired', request);
  }

  /** This run (the treatment) against `baselineRunId` on every measure. 400 when the pair is not comparable. */
  getRunPairedComparison(
    runId: number, baselineRunId: number, pricingBasis = BenchmarkReportPackPricingBasis.Current
  ): Observable<BenchmarkPairComparisonDto> {
    return this.http.post<BenchmarkPairComparisonDto>(
      `/api/admin/benchmark/runs/${runId}/paired-comparison`, { baselineRunId, pricingBasis });
  }

  /** The kind of paired comparison each candidate baseline would make with this run, in request order. */
  getRunPairKinds(runId: number, runIds: readonly number[]): Observable<BenchmarkRunPairKindDto[]> {
    return this.http.post<BenchmarkRunPairKindDto[]>(
      `/api/admin/benchmark/runs/${runId}/paired-comparison/kinds`, { runIds: [...runIds] });
  }

  /** A battery result (the treatment) against a baseline of the same definition on every measure. */
  getBatteryPairedComparison(
    batteryRunId: number, baselineBatteryRunId: number, pricingBasis = BenchmarkReportPackPricingBasis.Current
  ): Observable<BenchmarkPairComparisonDto> {
    return this.http.post<BenchmarkPairComparisonDto>(
      '/api/admin/benchmark/model-comparison/paired/battery', { batteryRunId, baselineBatteryRunId, pricingBasis });
  }

  /**
   * The comparability index over the named runs and analysis groups: which of them fall into the
   * same condition, ahead of a Compare that would otherwise silently exclude the smaller one.
   * Posted, because several hundred ids would not fit in a query string.
   */
  getComparabilityIndex(query: BenchmarkComparabilityIndexQuery): Observable<BenchmarkComparabilityIndexDto> {
    const body = { runIds: [...(query.runIds ?? [])], groupIds: [...(query.groupIds ?? [])] };
    return this.http.post<BenchmarkComparabilityIndexDto>(MODEL_COMPARABILITY_INDEX_ENDPOINT, body);
  }

  // Report packs. Preview makes no model call; start begins a background job.
  previewReportPack(request: BenchmarkReportPackRequest): Observable<BenchmarkReportPackPreviewDto> {
    return this.http.post<BenchmarkReportPackPreviewDto>('/api/admin/benchmark/report-packs/preview', request);
  }

  /**
   * 202 with the job id. Refusals: 400 `{ error }`, 429 a plain string, 409 a
   * `SameProviderWarningDto` (unacknowledged same provider) or a `BenchmarkReportPackJobDto`
   * (a job already running).
   */
  startReportPack(request: BenchmarkReportPackRequest): Observable<BenchmarkReportPackStartResponse> {
    return this.http.post<BenchmarkReportPackStartResponse>('/api/admin/benchmark/report-packs', request);
  }

  /**
   * A PDF laid out like the requested document, with placeholder text for the writer's sections and
   * the given charts placed by the given layout. Makes no model call and stores nothing.
   */
  reportPackLayoutPreview(request: BenchmarkReportPackLayoutPreviewRequest, charts: readonly BenchmarkReportPackLayoutPreviewChart[]):
    Observable<Blob> {
    const form = new FormData();
    form.append('request', JSON.stringify({
      ...request,
      charts: charts.map(c => ({ figureKey: c.figureKey, title: c.title, caption: c.caption, altText: c.altText })),
    }));
    for (const chart of charts) form.append('files', chart.png, `${chart.figureKey}.png`);
    return this.http.post('/api/admin/benchmark/report-packs/layout-preview', form, { responseType: 'blob' });
  }

  getReportPackJob(jobId: string): Observable<BenchmarkReportPackJobDto> {
    return this.http.get<BenchmarkReportPackJobDto>(`/api/admin/benchmark/report-packs/jobs/${encodeURIComponent(jobId)}`);
  }

  /** The running job, or null (204) when none is running. */
  getActiveReportPackJob(): Observable<BenchmarkReportPackJobDto | null> {
    return this.http.get<BenchmarkReportPackJobDto | null>('/api/admin/benchmark/report-packs/jobs/active');
  }

  cancelReportPackJob(jobId: string): Observable<{ cancelled: boolean }> {
    return this.http.post<{ cancelled: boolean }>(
      `/api/admin/benchmark/report-packs/jobs/${encodeURIComponent(jobId)}/cancel`, {});
  }

  // Stored report documents. None of these can reach a model.
  listReportDocuments(query: BenchmarkReportDocumentQuery = {}): Observable<BenchmarkReportDocumentListItemDto[]> {
    let params = new HttpParams();
    if (query.suiteId != null) params = params.set('suiteId', query.suiteId);
    if (query.runId != null) params = params.set('runId', query.runId);
    if (query.take != null) params = params.set('take', query.take);
    if (query.comparison != null) params = params.set('comparison', query.comparison.join(','));
    if (query.origin != null) params = params.set('origin', query.origin);
    if (query.subject != null) params = params.set('subject', query.subject);
    if (query.comparisonId != null) params = params.set('comparisonId', query.comparisonId);
    return this.http.get<BenchmarkReportDocumentListItemDto[]>('/api/admin/benchmark/report-documents', { params });
  }

  // Numbered comparisons. None of these can reach a model.
  /** Numbers the selection as a comparison, or returns the one it already has. */
  identifyComparison(selection: { runIds?: readonly number[]; groupIds?: readonly number[]; batteryRunIds?: readonly number[] }):
    Observable<BenchmarkComparisonDto> {
    return this.http.post<BenchmarkComparisonDto>('/api/admin/benchmark/model-comparisons/identify', {
      runIds: selection.runIds ?? [],
      groupIds: selection.groupIds ?? [],
      batteryRunIds: selection.batteryRunIds ?? [],
    });
  }

  /** Renames a comparison; a null or blank name restores its default name. */
  renameComparison(id: number, name: string | null): Observable<BenchmarkComparisonDto> {
    return this.http.patch<BenchmarkComparisonDto>(`/api/admin/benchmark/model-comparisons/${id}`, { name });
  }

  listComparisons(): Observable<BenchmarkComparisonListItemDto[]> {
    return this.http.get<BenchmarkComparisonListItemDto[]>('/api/admin/benchmark/model-comparisons');
  }

  getReportDocument(id: number): Observable<BenchmarkReportDocumentDetailDto> {
    return this.http.get<BenchmarkReportDocumentDetailDto>(`/api/admin/benchmark/report-documents/${id}`);
  }

  /** The document as Markdown at the given disclosure and peer naming; the same options give the same bytes. */
  renderReportDocument(
    id: number,
    disclosure: BenchmarkReportDisclosure,
    peers: BenchmarkReportPeerNaming
  ): Observable<string> {
    const params = new HttpParams()
      .set('disclosure', reportDisclosureParam(disclosure))
      .set('peers', reportPeerNamingParam(peers));
    return this.http.get(`/api/admin/benchmark/report-documents/${id}/render`, { params, responseType: 'text' });
  }

  deleteReportDocument(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/benchmark/report-documents/${id}`);
  }

  /** Replaces a report document's whole chart set, and the layout the server places it with. */
  putReportDocumentCharts(id: number, charts: ReportDocumentChartUpload[], layout: ReportDocumentChartLayout | null = null):
    Observable<ReportDocumentChartsSummaryDto> {
    return this.http.put<ReportDocumentChartsSummaryDto>(`/api/admin/benchmark/report-documents/${id}/charts`,
      layout ? { charts, layout } : { charts });
  }

  /** Removes every chart of a report document. */
  deleteReportDocumentCharts(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/benchmark/report-documents/${id}/charts`);
  }

  /**
   * Writes a finished run's missing AI-written documents (all of them, or the requested ones) with
   * the given writer, which becomes the run's report writer. 202 once queued. Refusals: 400
   * `{ error }` (no final synthesis, the writer refused, or an audience that is not a run
   * document), 404, 409 `{ error }` (the documents exist, or a job is pending or writing) or a
   * `SameProviderWarningDto` (a same-provider writer not acknowledged), 429 a plain string.
   */
  writeRunReportDocuments(runId: number, request: WriteRunReportDocumentsRequest): Observable<WriteRunReportDocumentsResponse> {
    return this.http.post<WriteRunReportDocumentsResponse>(`/api/admin/benchmark/runs/${runId}/report-documents`, request);
  }

  /** The run's report-writing job, or null (204) when this server process knows none. */
  getRunReportJob(runId: number): Observable<BenchmarkRunReportJobDto | null> {
    return this.http.get<BenchmarkRunReportJobDto>(`/api/admin/benchmark/runs/${runId}/report-documents/job`,
      { observe: 'response' }).pipe(
      map(response => response.status === 204 ? null : response.body ?? null)
    );
  }

  /** Asks the run's report-writing job to stop. 202 with the job view; 409 `{ error }` when none is in progress. */
  cancelRunReportJob(runId: number): Observable<BenchmarkRunReportJobDto> {
    return this.http.post<BenchmarkRunReportJobDto>(`/api/admin/benchmark/runs/${runId}/report-documents/cancel`, {});
  }

  /** What writing the run's documents with this writer would cost, and whether it is refused or warned. No model call. */
  estimateRunReports(runId: number, request: BenchmarkRunReportEstimateRequest): Observable<BenchmarkRunReportEstimateDto> {
    return this.http.post<BenchmarkRunReportEstimateDto>(`/api/admin/benchmark/runs/${runId}/report-documents/estimate`, request);
  }

  /** Deletes one of the run's AI-written documents. 204; 409 `{ error }` while the run's reports are being written. */
  deleteRunReportDocument(runId: number, documentId: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/benchmark/runs/${runId}/report-documents/${documentId}`);
  }

  /**
   * Writes a finished, analyzed battery run's missing battery-completion documents with the given
   * writer, which becomes the battery run's report writer. 202 once queued; refusals as
   * `writeRunReportDocuments`'s, with 400 also for a battery run without a complete, current analysis.
   */
  writeBatteryReportDocuments(batteryRunId: number, request: WriteRunReportDocumentsRequest): Observable<WriteRunReportDocumentsResponse> {
    return this.http.post<WriteRunReportDocumentsResponse>(`${BATTERIES_ENDPOINT}/runs/${batteryRunId}/report-documents`, request);
  }

  /** The battery run's report-writing job, or null (204) when this server process knows none. */
  getBatteryReportJob(batteryRunId: number): Observable<BenchmarkRunReportJobDto | null> {
    return this.http.get<BenchmarkRunReportJobDto>(`${BATTERIES_ENDPOINT}/runs/${batteryRunId}/report-documents/job`,
      { observe: 'response' }).pipe(
      map(response => response.status === 204 ? null : response.body ?? null)
    );
  }

  /** Asks the battery run's report-writing job to stop. 202 with the job view; 409 `{ error }` when none is in progress. */
  cancelBatteryReportJob(batteryRunId: number): Observable<BenchmarkRunReportJobDto> {
    return this.http.post<BenchmarkRunReportJobDto>(`${BATTERIES_ENDPOINT}/runs/${batteryRunId}/report-documents/cancel`, {});
  }

  /** What writing the battery run's documents with this writer would cost, and whether it is refused or warned. No model call. */
  estimateBatteryReports(batteryRunId: number, request: BenchmarkRunReportEstimateRequest): Observable<BenchmarkRunReportEstimateDto> {
    return this.http.post<BenchmarkRunReportEstimateDto>(`${BATTERIES_ENDPOINT}/runs/${batteryRunId}/report-documents/estimate`, request);
  }

  /** Deletes one of the battery run's AI-written documents. 204; 409 `{ error }` while they are being written. */
  deleteBatteryReportDocument(batteryRunId: number, documentId: number): Observable<void> {
    return this.http.delete<void>(`${BATTERIES_ENDPOINT}/runs/${batteryRunId}/report-documents/${documentId}`);
  }

  /**
   * The same-origin URL of a document's PDF, with the query `getReportDocumentPdf` sends. With
   * `inline`, the browser shows it in its own viewer and saves it under the server's file name.
   */
  reportDocumentPdfUrl(
    id: number,
    disclosure: BenchmarkReportDisclosure,
    peers: BenchmarkReportPeerNaming,
    paper: BenchmarkPdfPaper,
    inline = false
  ): string {
    let params = new HttpParams()
      .set('disclosure', reportDisclosureParam(disclosure))
      .set('peers', reportPeerNamingParam(peers))
      .set('paper', paper);
    if (inline) params = params.set('inline', 'true');
    return `/api/admin/benchmark/report-documents/${id}/render/pdf?${params.toString()}`;
  }

  /** The run's Markdown report as text, named as the server's `Content-Disposition` names it. */
  getRunReportText(runId: number): Observable<BenchmarkTextFile> {
    return this.getTextFile(this.getRunReportUrl(runId), `benchmark_run${runId}_report.md`);
  }

  /** The run's tool-call log as text, named as the server's `Content-Disposition` names it. */
  getToolCallLogText(runId: number): Observable<BenchmarkTextFile> {
    return this.getTextFile(this.getToolCallLogUrl(runId), `benchmark_run${runId}_tool_calls.md`);
  }

  private getTextFile(url: string, fallbackName: string): Observable<BenchmarkTextFile> {
    return this.http.get(url, { observe: 'response', responseType: 'text' }).pipe(
      map(response => ({
        text: response.body ?? '',
        fileName: fileNameFromContentDisposition(response.headers.get('Content-Disposition'), fallbackName)
      }))
    );
  }

  // PDFs, rendered by the server on a4 or letter paper. A 413 means the source is too large for one.

  /** The document as a PDF at the given disclosure and peer naming. */
  getReportDocumentPdf(
    id: number,
    disclosure: BenchmarkReportDisclosure,
    peers: BenchmarkReportPeerNaming,
    paper: BenchmarkPdfPaper
  ): Observable<BenchmarkBinaryFile> {
    const params = new HttpParams()
      .set('disclosure', reportDisclosureParam(disclosure))
      .set('peers', reportPeerNamingParam(peers))
      .set('paper', paper);
    return this.binaryFile(this.http.get(`/api/admin/benchmark/report-documents/${id}/render/pdf`,
      { params, observe: 'response', responseType: 'arraybuffer' }));
  }

  getRunReportPdf(runId: number, paper: BenchmarkPdfPaper): Observable<BenchmarkBinaryFile> {
    return this.binaryFile(this.http.get(`${this.getRunReportUrl(runId)}/pdf`,
      { params: new HttpParams().set('paper', paper), observe: 'response', responseType: 'arraybuffer' }));
  }

  getToolCallLogPdf(runId: number, paper: BenchmarkPdfPaper): Observable<BenchmarkBinaryFile> {
    return this.binaryFile(this.http.get(`${this.getToolCallLogUrl(runId)}/pdf`,
      { params: new HttpParams().set('paper', paper), observe: 'response', responseType: 'arraybuffer' }));
  }

  /** The diagnostics text the client captured, as a PDF; the server renders it and stores nothing. */
  renderDiagnosticsPdf(runId: number, text: string, capturedAtUtc: string, paper: BenchmarkPdfPaper): Observable<BenchmarkBinaryFile> {
    return this.binaryFile(this.http.post(`/api/admin/benchmark/runs/${runId}/diagnostics/pdf`, { text, capturedAtUtc },
      { params: new HttpParams().set('paper', paper), observe: 'response', responseType: 'arraybuffer' }));
  }

  // Word documents (.docx), rendered by the server on the same paper and with the same limits as the PDFs.

  /** The document as a Word document at the given disclosure and peer naming. */
  getReportDocumentDocx(
    id: number,
    disclosure: BenchmarkReportDisclosure,
    peers: BenchmarkReportPeerNaming,
    paper: BenchmarkPdfPaper
  ): Observable<BenchmarkBinaryFile> {
    const params = new HttpParams()
      .set('disclosure', reportDisclosureParam(disclosure))
      .set('peers', reportPeerNamingParam(peers))
      .set('paper', paper);
    return this.binaryFile(this.http.get(`/api/admin/benchmark/report-documents/${id}/render/docx`,
      { params, observe: 'response', responseType: 'arraybuffer' }));
  }

  getRunReportDocx(runId: number, paper: BenchmarkPdfPaper): Observable<BenchmarkBinaryFile> {
    return this.binaryFile(this.http.get(`${this.getRunReportUrl(runId)}/docx`,
      { params: new HttpParams().set('paper', paper), observe: 'response', responseType: 'arraybuffer' }));
  }

  getToolCallLogDocx(runId: number, paper: BenchmarkPdfPaper): Observable<BenchmarkBinaryFile> {
    return this.binaryFile(this.http.get(`${this.getToolCallLogUrl(runId)}/docx`,
      { params: new HttpParams().set('paper', paper), observe: 'response', responseType: 'arraybuffer' }));
  }

  /** The diagnostics text the client captured, as a Word document; the server renders it and stores nothing. */
  renderDiagnosticsDocx(runId: number, text: string, capturedAtUtc: string, paper: BenchmarkPdfPaper): Observable<BenchmarkBinaryFile> {
    return this.binaryFile(this.http.post(`/api/admin/benchmark/runs/${runId}/diagnostics/docx`, { text, capturedAtUtc },
      { params: new HttpParams().set('paper', paper), observe: 'response', responseType: 'arraybuffer' }));
  }

  private binaryFile(request: Observable<HttpResponse<ArrayBuffer>>): Observable<BenchmarkBinaryFile> {
    return request.pipe(
      map(response => ({
        bytes: new Uint8Array(response.body ?? new ArrayBuffer(0)),
        fileName: fileNameFromContentDisposition(response.headers.get('Content-Disposition'), '') || null
      })),
      catchError((error: unknown) => throwError(() => decodeBinaryErrorBody(error)))
    );
  }
}
