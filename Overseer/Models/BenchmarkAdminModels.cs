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
    public string QuestionText { get; set; } = string.Empty;
    public BenchmarkDifficulty Difficulty { get; set; }
    public string? ExpectedPoints { get; set; }
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
    public long? ScoringProfileId { get; set; }
    public bool AcknowledgeSameProvider { get; set; }
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
    public int SpeedTargetMs { get; set; }
    public double SpeedDecayK { get; set; }
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
    public int SpeedTargetMs { get; set; } = 5000;
    public double SpeedDecayK { get; set; } = 25.0;
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
    public int SpeedTargetMs { get; set; }
    public double SpeedDecayK { get; set; }
    public int MaxParallelQuestions { get; set; }
}

public class BenchmarkRunAnswerDto
{
    public long Id { get; set; }
    public long BenchmarkRunId { get; set; }
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
    public string? TerminationReason { get; set; }
    public int AnswerFlags { get; set; }
    public List<string> AnswerFlagNames { get; set; } = new();
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

    public string? StartedByUserId { get; set; }
    public string? StartedByUserName { get; set; }
    public BenchmarkRunStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? FinalScore { get; set; }
    public int? ComputedScore { get; set; }
    public int? QualityIndex { get; set; }
    public int? RawQualityIndex { get; set; }
    public int? SpeedIndex { get; set; }
    public long TotalAnswerDurationMs { get; set; }
    public long? ScoringProfileId { get; set; }
    public string? ScoringProfileName { get; set; }
    public string? ScoringProfileSnapshotJson { get; set; }
    public int ScoringMethodVersion { get; set; }
    public string? HarnessVersion { get; set; }
    public int? MaxToolCallsPerQuestionUsed { get; set; }
    public int DegradedAnswerCount { get; set; }
    public int ToolStarvedAnswerCount { get; set; }
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
    public string? ErrorMessage { get; set; }

    public List<BenchmarkRunAnswerDto> Answers { get; set; } = new();
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
    public int? RawQualityIndex { get; set; }
    public int? SpeedIndex { get; set; }
    public long TotalAnswerDurationMs { get; set; }
    public bool SpeedMeasurementDegraded { get; set; }
    public int AnsweredQuestionCount { get; set; }
    public int TotalQuestionCount { get; set; }
    public int DegradedAnswerCount { get; set; }
    public int ToolStarvedAnswerCount { get; set; }
    public string? HarnessVersion { get; set; }
    public long TotalDurationMs { get; set; }
}

