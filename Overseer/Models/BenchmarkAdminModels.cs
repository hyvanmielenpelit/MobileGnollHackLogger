namespace Overseer.Models;

using System;
using System.Collections.Generic;
using MobileGnollHackLogger.Data;

public class BenchmarkSuiteDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public int QuestionCount { get; set; }
    public int AssessedQuestionCount { get; set; }
    public bool DifficultyFullyAssessed { get; set; }
    public long? GameSnapshotId { get; set; }
    public string? GameSnapshotName { get; set; }
    public int? GameSnapshotCharCount { get; set; }
    public bool HasGeneratedQuestions { get; set; }
    public int ReviewedQuestionCount { get; set; }
}

public class CreateBenchmarkSuiteRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateBenchmarkSuiteRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class BenchmarkQuestionDto
{
    public long Id { get; set; }
    public long BenchmarkSuiteId { get; set; }
    public int OrderIndex { get; set; }
    public int ItemRevision { get; set; } = 1;
    public string QuestionText { get; set; } = string.Empty;
    public BenchmarkDifficulty Difficulty { get; set; }
    public string? ExpectedPoints { get; set; }
    public bool IsGenerated { get; set; }
    public int? ReviewedAtRevision { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewedByUserId { get; set; }
    public bool IsReviewed { get; set; }
    public int? AssessedDifficulty { get; set; }
    public string? AssessedDifficultyModel { get; set; }
    public DateTime? AssessedDifficultyAtUtc { get; set; }
    public long? AssessedDifficultyModelConfigurationId { get; set; }
    public string? AssessedDifficultyProviderUsed { get; set; }
    public string? AssessedDifficultyModelIdUsed { get; set; }
    public string? AssessedDifficultyThinkingLevelUsed { get; set; }
    public string? AssessedDifficultyReasoningModeUsed { get; set; }
    public string? AssessedDifficultyReasoningSummaryUsed { get; set; }
    public string? AssessedDifficultyServiceTierUsed { get; set; }
    public int? AssessedDifficultyMaxOutputTokensUsed { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ModifiedAtUtc { get; set; }
}

public class CreateBenchmarkQuestionRequest
{
    public string QuestionText { get; set; } = string.Empty;
    public BenchmarkDifficulty Difficulty { get; set; } = BenchmarkDifficulty.Simple;
    public string? ExpectedPoints { get; set; }
}

public class UpdateBenchmarkQuestionRequest
{
    public string QuestionText { get; set; } = string.Empty;
    public BenchmarkDifficulty Difficulty { get; set; }
    public string? ExpectedPoints { get; set; }
}

public class StartDifficultyAssessmentRequest
{
    public long SuiteId { get; set; }
    public List<long>? QuestionIds { get; set; }
    public long AssessorModelConfigurationId { get; set; }
}

public class DifficultyAssessmentJobItemDto
{
    public long QuestionId { get; set; }
    public int OrderIndex { get; set; }
    public string QuestionTextExcerpt { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? Difficulty { get; set; }
    public string? ErrorMessage { get; set; }
}

public class DifficultyAssessmentJobLogEntryDto
{
    public DateTime TimestampUtc { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string? RawExcerpt { get; set; }
}

public class DifficultyAssessmentJobDto
{
    public string Id { get; set; } = string.Empty;
    public long SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public string Scope { get; set; } = "suite";
    public long AssessorConfigId { get; set; }
    public string AssessorDisplayName { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public int RatedCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalCount { get; set; }
    public int TotalModelCalls { get; set; }
    public int PromptTokens { get; set; }
    public int OutputTokens { get; set; }
    public List<DifficultyAssessmentJobItemDto> Items { get; set; } = new();
    public List<DifficultyAssessmentJobLogEntryDto> Log { get; set; } = new();
}

public class StartBenchmarkRunRequest
{
    public long SuiteId { get; set; }
    public long TestedModelConfigurationId { get; set; }
    public long AssessorModelConfigurationId { get; set; }

    /// <summary>
    /// Optional. When set, answers the assessor flags with a critical error or scores below the
    /// profile's threshold are re-graded once by this configuration. Null means no second
    /// opinion for this run — there is no fallback to the assessor above, because a model
    /// checking its own verdict produces agreement rather than a second reading.
    /// </summary>
    public long? SecondOpinionAssessorModelConfigurationId { get; set; }

    /// <summary>
    /// Optional per-run override of the scoring profile's <c>SecondOpinionMode</c>. Null takes
    /// the profile's default. <c>Off</c> (0) is honoured as an explicit choice: it drops the
    /// second-opinion assessor from the run, which is what the enum's own documentation says the
    /// two mean — the mode is inert without an assessor, and an assessor is inert under Off.
    /// </summary>
    public int? SecondOpinionMode { get; set; }
    public long? ClaimVerifierModelConfigurationId { get; set; }
    public long? ScoringProfileId { get; set; }
    public bool AcknowledgeSameProvider { get; set; }

    /// <summary>
    /// Optional. The candidate answers under the production chat system prompt
    /// (ChatService.BuildSystemPrompt); this selects its Response Style section. Null and false both mean
    /// the concise style — "Default to 2–5 sentences per response" — which is what every run through 11
    /// used and therefore what they are comparable against.
    ///
    /// True selects the verbose style. Run 11 scored Completeness at 83.0 against Accuracy 97.7 while
    /// under the concise instruction, so a verbose run is the experiment that separates a prompt effect
    /// from a model limitation. It is not comparable with concise runs on Completeness, Conciseness or
    /// Readability, which is why the value is snapshotted onto the run and printed in the report manifest
    /// rather than merely accepted.
    /// </summary>
    public bool? VerboseMode { get; set; }
}

/// <summary>
/// The assessor of the most recent completed run of a suite, for the start dialog's
/// assessor-change advisory. Comparability across a suite's runs rests on the grader being the
/// same one, so a change of assessor is worth surfacing *before* the run rather than in the
/// report afterwards.
/// </summary>
public class BenchmarkLastAssessorDto
{
    public long? RunId { get; set; }
    public long? AssessorModelConfigurationId { get; set; }
    public string? AssessorModelDisplayNameUsed { get; set; }
    public string? AssessorModelProviderUsed { get; set; }
    public long? SecondOpinionAssessorModelConfigurationId { get; set; }
    public string? SecondOpinionAssessorModelDisplayNameUsed { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? HarnessVersion { get; set; }
    public int ScoringMethodVersion { get; set; }
}

public class SameProviderWarningDto
{
    public bool SameProvider { get; set; } = true;
    public string Provider { get; set; } = string.Empty;
    public string TestedModelDisplayName { get; set; } = string.Empty;
    public string AssessorModelDisplayName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class BenchmarkFootprintDto
{
    public int RunCount { get; set; }
    public long TotalAnswerCharacters { get; set; }
}

public class RescoreRunRequest
{
    public long? ScoringProfileId { get; set; }
}

public class ReassessAnswerRequest
{
    public long? AssessorModelConfigurationId { get; set; }

    /// <summary>
    /// Record a verdict without applying it. False (the default) replaces the answer's verdict and
    /// recomputes the run's indices, which is what settling a disputed score needs. True records
    /// the verdict in the second-opinion slot and changes no score, level, flag or index — the
    /// mode for comparing a prospective assessor against the one in use.
    /// </summary>
    public bool Trial { get; set; }

    /// <summary>
    /// Required to let a trial overwrite an existing second opinion. Without it such a trial is
    /// refused: an automatic second opinion is run evidence, and an experiment must not erase
    /// evidence by accident.
    /// </summary>
    public bool ReplaceExistingSecondOpinion { get; set; }
}

public class CalibrateAssessorRequest
{
    public long AssessorModelConfigurationId { get; set; }
}

/// <summary>
/// One non-destructive re-grading of a run by an alternative assessor. Deliberately reaches the
/// admin UI only and never the Markdown report: a calibration is an experiment about graders, not
/// a property of the run, and printing it beside the run's own figures would invite reading a
/// calibration verdict as a result.
/// </summary>
public class BenchmarkAssessorCalibrationDto
{
    public long Id { get; set; }
    public long BenchmarkRunId { get; set; }
    public string? AssessorDisplayNameUsed { get; set; }
    public string? AssessorProviderUsed { get; set; }
    public string? AssessorModelIdUsed { get; set; }
    public string? AssessorThinkingLevelUsed { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedByUserName { get; set; }
    public int AnswerCount { get; set; }
    public int SkippedAnswerCount { get; set; }

    /// <summary>Mean |calibration - original| quality across graded answers.</summary>
    public double? MeanAbsDelta { get; set; }

    /// <summary>Same definition as a live run's: a gap above 15 points, or a split on criticalError.</summary>
    public int DisagreementCount { get; set; }

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public long DurationMs { get; set; }
    public string? VerdictsJson { get; set; }
    public string? ErrorMessage { get; set; }
}

public class BenchmarkRetryRequest
{
    public long? AssessorModelConfigurationId { get; set; }
}

public class BenchmarkScoringProfileDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public double WeightAccuracy { get; set; }
    public double WeightCompleteness { get; set; }
    public double WeightConciseness { get; set; }
    public double WeightReadability { get; set; }
    public string LevelScoresJson { get; set; } = string.Empty;
    public int CriticalErrorCeiling { get; set; }
    public int SecondOpinionQualityThreshold { get; set; }

    /// <summary>Off (0), Flagged (1), FlaggedAndOutliers (2) or All (3).</summary>
    public int SecondOpinionMode { get; set; }

    /// <summary>Meaningful under FlaggedAndOutliers only; validated &gt; 0 there.</summary>
    public int SecondOpinionOutlierDeltaPoints { get; set; }

    public bool SecondOpinionBlind { get; set; }

    public int SpeedTargetMs { get; set; }
    public double SpeedDecayK { get; set; }
    public double SpeedDifficultyScaling { get; set; }
    public int MaxParallelQuestions { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ModifiedAtUtc { get; set; }
}

public class CreateBenchmarkScoringProfileRequest
{
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public double WeightAccuracy { get; set; } = 0.55;
    public double WeightCompleteness { get; set; } = 0.25;
    public double WeightConciseness { get; set; } = 0.10;
    public double WeightReadability { get; set; } = 0.10;
    public string LevelScoresJson { get; set; } = "[1, 15, 35, 55, 72, 87, 100]";
    public int CriticalErrorCeiling { get; set; } = 25;
    public int SecondOpinionQualityThreshold { get; set; } = 50;
    public int SecondOpinionMode { get; set; } = (int)BenchmarkSecondOpinionMode.Flagged;
    public int SecondOpinionOutlierDeltaPoints { get; set; } = 25;
    public bool SecondOpinionBlind { get; set; } = true;
    public int SpeedTargetMs { get; set; } = 15000;
    public double SpeedDecayK { get; set; } = 20.0;
    public double SpeedDifficultyScaling { get; set; } = 1.0;
    public int MaxParallelQuestions { get; set; } = 1;
}

public class UpdateBenchmarkScoringProfileRequest
{
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public double WeightAccuracy { get; set; }
    public double WeightCompleteness { get; set; }
    public double WeightConciseness { get; set; }
    public double WeightReadability { get; set; }
    public string LevelScoresJson { get; set; } = string.Empty;
    public int CriticalErrorCeiling { get; set; }
    public int SecondOpinionQualityThreshold { get; set; }
    public int SecondOpinionMode { get; set; }
    public int SecondOpinionOutlierDeltaPoints { get; set; }
    public bool SecondOpinionBlind { get; set; }
    public int SpeedTargetMs { get; set; }
    public double SpeedDecayK { get; set; }
    public double SpeedDifficultyScaling { get; set; }
    public int MaxParallelQuestions { get; set; }
}

public class BenchmarkRunAnswerDto
{
    public long Id { get; set; }
    public long BenchmarkRunId { get; set; }

    /// <summary>
    /// The suite question this answer was produced for. Null for a historical answer that could
    /// not be matched unambiguously, and null once the question is deleted. Anything merging
    /// questions with answers must prefer this over <see cref="OrderIndex"/>, which a suite
    /// reorder rewrites.
    /// </summary>
    public long? BenchmarkQuestionId { get; set; }
    public int? ItemRevisionUsed { get; set; }

    public int OrderIndex { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public BenchmarkDifficulty Difficulty { get; set; }
    public int? AssessedDifficulty { get; set; }
    public string AnswerText { get; set; } = string.Empty;
    public string? ThoughtText { get; set; }
    public BenchmarkAnswerStatus Status { get; set; }
    public BenchmarkAssessmentStatus AssessmentStatus { get; set; }
    public string? AssessmentError { get; set; }
    public string? ErrorMessage { get; set; }
    public int? HttpStatusCode { get; set; }
    public int? Score { get; set; }
    public int? AccuracyLevel { get; set; }
    public int? CompletenessLevel { get; set; }
    public int? ConcisenessLevel { get; set; }
    public int? ReadabilityLevel { get; set; }
    public bool CriticalError { get; set; }
    public int? AccuracyScore { get; set; }
    public int? CompletenessScore { get; set; }
    public int? ConcisenessScore { get; set; }
    public int? ReadabilityScore { get; set; }
    public int? QualityScore { get; set; }
    public int? SpeedScore { get; set; }
    public string? ReviewComment { get; set; }
    public long DurationMs { get; set; }
    public long? TimeToFirstTokenMs { get; set; }
    public string? ActualServiceTierUsed { get; set; }
    public string? ToolCallSummary { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? CacheReadInputTokens { get; set; }
    public int? CacheCreationInputTokens { get; set; }
    public long? AssessedByModelConfigurationId { get; set; }
    public string? AssessedByModelDisplayNameUsed { get; set; }
    public string? AssessedByModelProviderUsed { get; set; }
    public string? AssessedByModelIdUsed { get; set; }
    public DateTime? AssessedAtUtc { get; set; }
    public int? RawQualityScore { get; set; }
    public int? ModelCallCount { get; set; }
    public int? ToolCallCount { get; set; }
    public bool ToolBudgetExhausted { get; set; }
    public int? ToolCallsBlocked { get; set; }

    /// <summary>The per-band budget that actually applied to this question.</summary>
    public int? ToolCallBudgetUsed { get; set; }

    /// <summary>Wall-clock time spent in tool batches during this turn.</summary>
    public long? ToolTimeMs { get; set; }

    /// <summary>Turn duration with tool I/O removed. This is what speed is scored on.</summary>
    public long ModelTimeMs { get; set; }

    /// <summary>Transport artifacts removed before grading, retained verbatim for audit.</summary>
    public string? ScrubbedArtifactText { get; set; }

    public int ScrubbedArtifactCount { get; set; }

    /// <summary>
    /// Reasoning-narration blocks removed before grading. Null for runs before harness
    /// version 6, which did not record it — null means "not recorded", not zero.
    /// </summary>
    public int? NarrationBlockCount { get; set; }

    public string? TerminationReason { get; set; }
    public int AnswerFlags { get; set; }
    public List<string> AnswerFlagNames { get; set; } = new();

    /// <summary>What the grading call consumed. Never folded into the candidate's tokens.</summary>
    public int? AssessmentInputTokens { get; set; }
    public int? AssessmentOutputTokens { get; set; }
    public long? AssessmentDurationMs { get; set; }

    /// <summary>The assessor's rubric citations, as stored JSON.</summary>
    public string? AssessmentEvidenceJson { get; set; }

    /// <summary>The claim the assessor called a critical error, quoted from the answer.</summary>
    public string? CriticalErrorQuote { get; set; }

    /// <summary>
    /// Claims the assessor could neither confirm nor refute. Null for a run graded before the
    /// field existed — null is "never asked", zero is "asked and found none".
    /// </summary>
    public int? UnverifiedClaimCount { get; set; }
    public string? UnverifiedClaimsJson { get; set; }

    /// <summary>Second-opinion verdict, present only when one was triggered. Advisory.</summary>
    public int? SecondOpinionQualityScore { get; set; }
    public bool? SecondOpinionCriticalError { get; set; }
    public string? SecondOpinionByModelDisplayNameUsed { get; set; }
    public string? SecondOpinionJson { get; set; }
    public bool SecondOpinionDisagreed { get; set; }

    /// <summary>
    /// Why this answer was graded twice: CriticalError, ContestedVerdict, UnverifiedClaims,
    /// BelowThreshold, Outlier, All, or Manual for an operator's trial. "Every answer" and "this
    /// one looked wrong" are different facts about the same second verdict.
    /// </summary>
    public string? SecondOpinionTrigger { get; set; }
    public string? SecondOpinionError { get; set; }

    /// <summary>
    /// Re-assessment provenance. A published index can move after publication, and these are how
    /// the screen says that it did, and what it moved from.
    /// </summary>
    public DateTime? ReassessedAtUtc { get; set; }
    public string? ReassessedByModelDisplayNameUsed { get; set; }
    public int? PreviousQualityScore { get; set; }
    public int ReassessmentCount { get; set; }

    /// <summary>
    /// Per-claim verdicts from the claim verifier, as a JSON array of
    /// { claim, verdict, citation, basis }. Advisory: nothing here is read by any scoring path.
    /// </summary>
    public string? ClaimVerificationJson { get; set; }
    public int? ClaimsSupportedCount { get; set; }
    public int? ClaimsRefutedCount { get; set; }
    public int? ClaimsIndeterminateCount { get; set; }
    public string? ClaimVerificationByModelDisplayNameUsed { get; set; }
    public int? ClaimVerificationInputTokens { get; set; }
    public int? ClaimVerificationOutputTokens { get; set; }
    public long? ClaimVerificationDurationMs { get; set; }
    public int? ClaimVerificationToolCallCount { get; set; }
    public string? ClaimVerificationError { get; set; }
    public string? ClaimVerificationRawText { get; set; }
}

public class BenchmarkRunDetailDto
{
    public long Id { get; set; }
    public long? BenchmarkSuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public long? TestedModelConfigurationId { get; set; }
    public string TestedModelDisplayNameUsed { get; set; } = string.Empty;
    public string TestedModelProviderUsed { get; set; } = string.Empty;
    public string TestedModelIdUsed { get; set; } = string.Empty;
    public string? TestedModelThinkingLevelUsed { get; set; }
    public string? TestedModelReasoningModeUsed { get; set; }
    public string? TestedModelReasoningSummaryUsed { get; set; }
    public string? TestedModelServiceTierUsed { get; set; }
    public int? TestedModelMaxOutputTokensUsed { get; set; }
    public ParallelExecutionMode TestedModelParallelExecutionModeUsed { get; set; }

    public long? AssessorModelConfigurationId { get; set; }
    public string AssessorModelDisplayNameUsed { get; set; } = string.Empty;
    public string AssessorModelProviderUsed { get; set; } = string.Empty;
    public string AssessorModelIdUsed { get; set; } = string.Empty;
    public string? AssessorModelThinkingLevelUsed { get; set; }
    public string? AssessorModelReasoningModeUsed { get; set; }
    public bool AssessorAvailable { get; set; }

    /// <summary>Null when the run was started without a second-opinion assessor.</summary>
    public long? SecondOpinionAssessorModelConfigurationId { get; set; }
    public string? SecondOpinionAssessorModelDisplayNameUsed { get; set; }
    public string? SecondOpinionAssessorModelProviderUsed { get; set; }
    public string? SecondOpinionAssessorModelIdUsed { get; set; }
    public string? SecondOpinionAssessorModelThinkingLevelUsed { get; set; }
    public string? SecondOpinionAssessorModelReasoningModeUsed { get; set; }

    /// <summary>Null when the run was started without a claim verifier.</summary>
    public long? ClaimVerifierModelConfigurationId { get; set; }
    public string? ClaimVerifierDisplayNameUsed { get; set; }
    public string? ClaimVerifierProviderUsed { get; set; }
    public string? ClaimVerifierModelIdUsed { get; set; }
    public string? ClaimVerifierThinkingLevelUsed { get; set; }
    public string? ClaimVerifierReasoningModeUsed { get; set; }

    public string? StartedByUserId { get; set; }
    public string? StartedByUserName { get; set; }
    public BenchmarkRunStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? FinalScore { get; set; }
    public int? ComputedScore { get; set; }
    public int? QualityIndex { get; set; }
    public int? RawQualityIndex { get; set; }

    /// <summary>
    /// The equal-weight mean of the same per-question scores as <see cref="QualityIndex"/>. Null
    /// for runs before harness version 7, which never recorded it. Shown only when it differs
    /// from the weighted index, where the gap is what the weighting did to the headline.
    /// </summary>
    public int? UnweightedQualityIndex { get; set; }
    public double? QualityIndexStandardError { get; set; }

    public int? SpeedIndex { get; set; }
    public long TotalAnswerDurationMs { get; set; }
    public long? ScoringProfileId { get; set; }
    public string? ScoringProfileName { get; set; }
    public string? ScoringProfileSnapshotJson { get; set; }

    /// <summary>
    /// The speed constants this run was scored against, read from
    /// <see cref="ScoringProfileSnapshotJson"/> <b>server-side</b>. The client needs the target
    /// to mark the Speed Index advisory for a deliberating candidate on an interactive-latency
    /// profile, and parsing the snapshot in TypeScript would be a second reader of a storage
    /// format that has to be kept in step with the profile shape.
    /// </summary>
    public int? ScoringProfileSpeedTargetMs { get; set; }

    public double? ScoringProfileSpeedDecayK { get; set; }
    public int? ScoringProfileSecondOpinionQualityThreshold { get; set; }
    public int? ScoringProfileSecondOpinionOutlierDeltaPoints { get; set; }
    public int ScoringMethodVersion { get; set; }
    public string? HarnessVersion { get; set; }
    public int? MaxToolCallsPerQuestionUsed { get; set; }
    public int DegradedAnswerCount { get; set; }
    public int ToolStarvedAnswerCount { get; set; }
    public int BudgetSaturatedAnswerCount { get; set; }

    /// <summary>
    /// Answers compromised by a transport or provider defect. Disjoint from
    /// <see cref="ToolStarvedAnswerCount"/>: together with the clean count these partition the
    /// run, so the three always sum to the question count.
    /// </summary>
    public int TransportDefectAnswerCount { get; set; }

    /// <summary>
    /// Answers the harness repaired: leaked transport artifacts removed, the answer beneath
    /// graded normally. Its own bucket, disjoint from the three others.
    /// </summary>
    public int RecoveredAnswerCount { get; set; }

    /// <summary>
    /// Answers carrying an advisory flag. Overlaps the counts above and must never be summed
    /// with them.
    /// </summary>
    public int AdvisoryFlagAnswerCount { get; set; }

    public int ScrubbedArtifactAnswerCount { get; set; }

    /// <summary>
    /// Answers whose assessor described a fabrication while leaving criticalError false, and
    /// answers whose verdict was replaced after the run finished. Both advisory; both overlap the
    /// counts above and are never summed with them.
    /// </summary>
    public int ContestedVerdictAnswerCount { get; set; }
    public int UnevidencedDeductionAnswerCount { get; set; }
    public int OmissionAsAccuracyAnswerCount { get; set; }
    public int RefutedClaimAnswerCount { get; set; }
    public int ClaimVerifiedAnswerCount { get; set; }
    public int ClaimsSupportedCount { get; set; }
    public int ClaimsRefutedCount { get; set; }
    public int ClaimsIndeterminateCount { get; set; }
    public int ReassessedAnswerCount { get; set; }

    /// <summary>
    /// How the second-opinion assessor was used on this run: Off (0), Flagged (1),
    /// FlaggedAndOutliers (2) or All (3), as stamped at run start.
    /// </summary>
    public int SecondOpinionModeUsed { get; set; }
    public bool SecondOpinionBlindUsed { get; set; }

    /// <summary>
    /// Grader agreement, which is only interpretable together with its coverage: a mean delta
    /// over trigger-selected answers is conditioned on the first assessor's own uncertainty,
    /// while the same figure over every answer is an inter-rater agreement rate.
    /// </summary>
    public int SecondOpinionGradedAnswerCount { get; set; }
    public double? SecondOpinionMeanAbsDelta { get; set; }
    public double? SecondOpinionMeanSignedDelta { get; set; }
    public int SecondOpinionCriticalErrorSplitCount { get; set; }
    public string? CandidatePromptOptionsJson { get; set; }
    public string? CandidatePromptSourceUsed { get; set; }
    public string? CandidateSystemPromptSha256 { get; set; }
    public string? ToolGuidesSha256 { get; set; }
    public string? KnowledgeBaseHeadSha { get; set; }

    /// <summary>Answers whose two verdicts disagreed, among those graded twice.</summary>
    public int SecondOpinionDisagreementCount { get; set; }

    public long? ToolOverheadMs { get; set; }

    public bool DifficultyFallbackUsed { get; set; }
    public bool SpeedMeasurementDegraded { get; set; }
    public int MaxParallelQuestionsUsed { get; set; }
    public int AnsweredQuestionCount { get; set; }
    public int TotalQuestionCount { get; set; }
    public string? PurposeStatementUsed { get; set; }
    public bool SameProviderAcknowledged { get; set; }
    public string? AssessmentJson { get; set; }
    public string? AssessmentText { get; set; }
    public bool AssessmentParseFailed { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCacheReadTokens { get; set; }
    public long TotalCacheCreationTokens { get; set; }
    public long TotalDurationMs { get; set; }

    /// <summary>Assessor-side usage, kept apart from the candidate totals above.</summary>
    public long TotalAssessmentInputTokens { get; set; }
    public long TotalAssessmentOutputTokens { get; set; }
    public long TotalAssessmentDurationMs { get; set; }

    /// <summary>Claim-verifier-side usage, kept apart from candidate and assessor totals.</summary>
    public long TotalClaimVerificationInputTokens { get; set; }
    public long TotalClaimVerificationOutputTokens { get; set; }
    public long TotalClaimVerificationDurationMs { get; set; }

    public decimal? EstimatedCost { get; set; }
    public decimal? EstimatedCandidateCost { get; set; }
    public decimal? EstimatedAssessorCost { get; set; }
    public decimal? EstimatedVerifierCost { get; set; }
    public string? PricingSource { get; set; }
    public bool PricingIncomplete { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Order indexes whose request is in flight to the provider right now. An answer row does
    /// not exist until the model replies, so this is the only way the client can tell a
    /// question that is being answered from one that has not been dispatched. Always empty for
    /// a run that is not the current, still-running one.
    /// </summary>
    public List<int> InFlightOrderIndexes { get; set; } = new();

    public List<BenchmarkRunAnswerDto> Answers { get; set; } = new();
}

// ---------------------------------------------------------------------------------------------
// Suite health. Every DTO below carries a read-only finding: the panel that shows them has no
// write action, and no endpoint here writes a question, a rubric, or a difficulty rating.
// ---------------------------------------------------------------------------------------------

public class BenchmarkItemStatisticsDto
{
    public long QuestionId { get; set; }
    public int OrderIndex { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public BenchmarkDifficulty AuthoredDifficulty { get; set; }
    public int ItemRevision { get; set; }

    /// <summary>Sample size and its confounds. Shown together, always.</summary>
    public int RunCount { get; set; }
    public int DistinctModelCount { get; set; }
    public int DistinctAssessorCount { get; set; }
    public int DistinctScoringMethodVersionCount { get; set; }

    /// <summary>Answers included whose revision was never recorded.</summary>
    public int UnknownRevisionCount { get; set; }

    public double MeanQuality { get; set; }
    public int MinQuality { get; set; }
    public int MaxQuality { get; set; }
    public double StdDev { get; set; }

    /// <summary>
    /// 100 − MeanQuality. Reported only. It is <b>never</b> written back into
    /// <c>AssessedDifficulty</c>, which weights the Intelligence Index: deriving the weight from
    /// the scores it weights is circular.
    /// </summary>
    public int EmpiricalDifficulty { get; set; }

    public int? AssessedDifficulty { get; set; }
    public int? DifficultyDelta { get; set; }

    /// <summary>Null below four runs, where a top-half/bottom-half split means nothing.</summary>
    public double? Discrimination { get; set; }

    public double MeanToolCalls { get; set; }
    public double BudgetBoundFraction { get; set; }

    public int Flags { get; set; }
    public List<string> FlagNames { get; set; } = new();

    /// <summary>A confound fired: every other figure on this row is a mixture, not a measurement.</summary>
    public bool Confounded { get; set; }
    public bool InsufficientData { get; set; }
}

public class BenchmarkSuiteItemAnalysisDto
{
    public long SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public int QuestionCount { get; set; }

    public int RunCount { get; set; }
    public int DistinctModelCount { get; set; }
    public int DistinctAssessorCount { get; set; }
    public int DistinctScoringMethodVersionCount { get; set; }

    /// <summary>
    /// Answers excluded because they could not be tied to a question — a historical answer the
    /// backfill could not match unambiguously, or one whose question was deleted.
    /// </summary>
    public int LinkedAnswerCount { get; set; }
    public int UnlinkedAnswerCount { get; set; }

    /// <summary>Below this many runs, nothing in the table is a measurement.</summary>
    public int MinRunsForMeasurement { get; set; }
    public int MinRunsForDiscrimination { get; set; }

    public List<BenchmarkItemStatisticsDto> Items { get; set; } = new();
}

public class BenchmarkRubricGapClusterDto
{
    public long QuestionId { get; set; }
    public int QuestionOrderIndex { get; set; }

    /// <summary>Verbatim, so a human reads what was said rather than a paraphrase of it.</summary>
    public List<string> Claims { get; set; } = new();

    public List<string> ModelFamilies { get; set; } = new();
    public List<string> ModelIds { get; set; } = new();
    public int Occurrences { get; set; }

    /// <summary>LikelyRubricGap or LikelyHallucination.</summary>
    public string Verdict { get; set; } = string.Empty;
}

public class BenchmarkRubricGapReportDto
{
    public long SuiteId { get; set; }
    public int RunCount { get; set; }

    /// <summary>Unverified claims read, before clustering. Zero for a suite with no v7 runs.</summary>
    public int ClaimCount { get; set; }

    public List<BenchmarkRubricGapClusterDto> Clusters { get; set; } = new();
    public List<BenchmarkKnowledgeBaseGapDto> KnowledgeBaseGaps { get; set; } = new();
}

public class BenchmarkKnowledgeBaseGapDto
{
    public string Claim { get; set; } = string.Empty;
    public string? Citation { get; set; }
    public string? Basis { get; set; }
    public List<int> QuestionOrderIndices { get; set; } = new();
    public int Recurrence { get; set; }
}

public class BenchmarkCitationDto
{
    /// <summary>SourceFile, Symbol or WikiArticle.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <summary>Resolved, Unresolved or NotValidated — the last meaning "we did not check".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Parsed and shown, never validated: line numbers drift with every commit.</summary>
    public int? LineNumber { get; set; }
}

public class BenchmarkQuestionCitationsDto
{
    public long QuestionId { get; set; }
    public int OrderIndex { get; set; }
    public List<BenchmarkCitationDto> Citations { get; set; } = new();
    public int UnresolvedCount { get; set; }
    public int NotValidatedCount { get; set; }
    public bool HasNoCitations { get; set; }
}

public class BenchmarkCitationReportDto
{
    public long SuiteId { get; set; }
    public int UnresolvedCount { get; set; }
    public int NotValidatedCount { get; set; }

    /// <summary>
    /// False while the source index is still building, which makes an unresolved citation
    /// unreliable. Stated rather than left to be inferred from a suspiciously bad result.
    /// </summary>
    public bool SourceIndexReady { get; set; }

    public List<BenchmarkQuestionCitationsDto> Questions { get; set; } = new();
}

public class CoverageAnalysisRequest
{
    public long AnalysisModelConfigurationId { get; set; }
}

public class BenchmarkCoverageGapDto
{
    public string Subsystem { get; set; } = string.Empty;

    /// <summary>Required. A gap with no location is discarded before it reaches here.</summary>
    public string SourceLocation { get; set; } = string.Empty;

    public string? Rationale { get; set; }
    public string? SuggestedBand { get; set; }
}

/// <summary>
/// A read-only coverage report. Nothing here is written into the suite, and no endpoint exists
/// that would write one: a generated draft is edited and approved by a human before it becomes a
/// question, and a draft rubric without a source location is not usable.
///
/// The analysing model is disclosed on the report itself rather than snapshotted onto the suite,
/// because the report is not persisted either.
/// </summary>
public class BenchmarkCoverageReportDto
{
    public long SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public int QuestionCount { get; set; }

    public long AnalysisModelConfigurationId { get; set; }
    public string? AnalysisModelDisplayNameUsed { get; set; }
    public string? AnalysisModelProviderUsed { get; set; }
    public string? AnalysisModelIdUsed { get; set; }
    public string? AnalysisModelThinkingLevelUsed { get; set; }
    public DateTime AnalyzedAtUtc { get; set; }

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public long DurationMs { get; set; }

    public List<BenchmarkCoverageGapDto> Gaps { get; set; } = new();
    public string? Comment { get; set; }
    public string? ErrorMessage { get; set; }
}

public class BenchmarkRunSummaryDto
{
    public long Id { get; set; }
    public long? BenchmarkSuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public long? TestedModelConfigurationId { get; set; }
    public string TestedModelDisplayNameUsed { get; set; } = string.Empty;
    public string TestedModelProviderUsed { get; set; } = string.Empty;
    public string TestedModelIdUsed { get; set; } = string.Empty;
    public long? AssessorModelConfigurationId { get; set; }
    public string AssessorModelDisplayNameUsed { get; set; } = string.Empty;
    public string? StartedByUserName { get; set; }
    public BenchmarkRunStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? FinalScore { get; set; }
    public int? ComputedScore { get; set; }
    public int? QualityIndex { get; set; }
    public double? QualityIndexStandardError { get; set; }
    public int? RawQualityIndex { get; set; }
    public int? SpeedIndex { get; set; }
    public long TotalAnswerDurationMs { get; set; }
    public bool SpeedMeasurementDegraded { get; set; }
    public int AnsweredQuestionCount { get; set; }
    public int TotalQuestionCount { get; set; }
    public int DegradedAnswerCount { get; set; }
    public int ToolStarvedAnswerCount { get; set; }
    public int BudgetSaturatedAnswerCount { get; set; }
    public bool SecondOpinionBlindUsed { get; set; }
    public double? SecondOpinionMeanSignedDelta { get; set; }
    public int SecondOpinionCriticalErrorSplitCount { get; set; }
    public string? CandidatePromptOptionsJson { get; set; }
    public string? CandidatePromptSourceUsed { get; set; }
    public string? HarnessVersion { get; set; }
    public long TotalDurationMs { get; set; }

    public decimal? EstimatedCost { get; set; }
    public bool PricingIncomplete { get; set; }
}

// --- Benchmark Game Snapshot Models ---

public class BenchmarkGameSnapshotDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SanitizedText { get; set; }
    public string? DigestText { get; set; }
    public int CharCount { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string CaptureMethod { get; set; } = string.Empty;
    public string? SourceGnollHackVersion { get; set; }
    public string? Notes { get; set; }
    public long? SourceChatSessionId { get; set; }
    public DateTime? CapturedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ModifiedAtUtc { get; set; }
    public long? SuiteId { get; set; }
    public string? SuiteName { get; set; }
}

public class CaptureBenchmarkSnapshotRequest
{
    public long SessionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? SourceGnollHackVersion { get; set; }
}

public class SaveAttachedSnapshotRequest
{
    public long SessionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? SourceGnollHackVersion { get; set; }
}

public class UploadBenchmarkSnapshotRequest
{
    public string Name { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? SourceGnollHackVersion { get; set; }
}

public class UpdateBenchmarkGameSnapshotRequest
{
    public string? Name { get; set; }
    public string? Notes { get; set; }
    public string? DigestText { get; set; }
    public string? SourceGnollHackVersion { get; set; }
}

public class CaptureBenchmarkSnapshotResponse
{
    public BenchmarkGameSnapshotDto Board { get; set; } = default!;
    public BenchmarkSuiteDto Suite { get; set; } = default!;
}

// --- Question Generation Job Models ---

public class StartQuestionGenerationRequest
{
    public long SuiteId { get; set; }
    public long GeneratorModelConfigurationId { get; set; }
    public string? Instructions { get; set; }
    public int SimpleCount { get; set; }
    public int IntermediateCount { get; set; }
    public int AdvancedCount { get; set; }
}

public class QuestionGenerationJobItemDto
{
    public int Difficulty { get; set; }
    public string DifficultyName { get; set; } = string.Empty;
    public int RequestedCount { get; set; }
    public int GeneratedCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public class QuestionGenerationJobLogEntryDto
{
    public DateTime TimestampUtc { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string? RawExcerpt { get; set; }
}

public class QuestionGenerationJobDto
{
    public string Id { get; set; } = string.Empty;
    public long SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public long GeneratorConfigId { get; set; }
    public string GeneratorDisplayName { get; set; } = string.Empty;
    public string? StartedByUserId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalModelCalls { get; set; }
    public int PromptTokens { get; set; }
    public int OutputTokens { get; set; }
    public List<QuestionGenerationJobItemDto> Items { get; set; } = new();
    public List<QuestionGenerationJobLogEntryDto> Log { get; set; } = new();
}

// --- Rubric Verification Job Models ---

public class StartRubricCheckRequest
{
    public long SuiteId { get; set; }
    public List<long>? QuestionIds { get; set; }
    public long CheckerModelConfigurationId { get; set; }
}

public class RubricCheckFindingDto
{
    public string Claim { get; set; } = string.Empty;
    public string Assessment { get; set; } = string.Empty;
    public string? BoardQuote { get; set; }
}

public class RubricCheckJobItemDto
{
    public long QuestionId { get; set; }
    public int OrderIndex { get; set; }
    public string QuestionTextExcerpt { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Verdict { get; set; }
    public List<RubricCheckFindingDto> Findings { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class RubricCheckJobLogEntryDto
{
    public DateTime TimestampUtc { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string? RawExcerpt { get; set; }
}

public class RubricCheckJobDto
{
    public string Id { get; set; } = string.Empty;
    public long SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public string Scope { get; set; } = "suite";
    public long CheckerConfigId { get; set; }
    public string CheckerDisplayName { get; set; } = string.Empty;
    public string? StartedByUserId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalModelCalls { get; set; }
    public int PromptTokens { get; set; }
    public int OutputTokens { get; set; }
    public List<RubricCheckJobItemDto> Items { get; set; } = new();
    public List<RubricCheckJobLogEntryDto> Log { get; set; } = new();
}

// --- Question Review Request ---

public class ReviewBenchmarkQuestionRequest
{
    public bool? Reviewed { get; set; }
}

