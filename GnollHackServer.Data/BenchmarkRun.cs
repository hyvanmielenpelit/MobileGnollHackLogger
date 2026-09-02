namespace MobileGnollHackLogger.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public enum BenchmarkDifficulty
{
    Simple = 1,
    Intermediate = 2,
    Advanced = 3
}

public enum BenchmarkRunStatus
{
    Running = 1,
    Completed = 2,
    CompletedWithErrors = 3,
    Failed = 4,
    Canceled = 5
}

public enum BenchmarkAnswerStatus
{
    Ok = 1,
    ProviderError = 2,
    Failed = 3,
    Skipped = 4,
    EmptyAnswer = 5
}

[Flags]
public enum BenchmarkAnswerFlags
{
    None = 0,
    Empty = 1,
    HarnessArtifacts = 2,
    Truncated = 4
}

public class BenchmarkRun
{
    public long Id { get; set; }

    public long? BenchmarkSuiteId { get; set; }
    public BenchmarkSuite? BenchmarkSuite { get; set; }

    [MaxLength(128)]
    public string SuiteName { get; set; } = default!;

    // Tested Model Config snapshot
    public long? TestedModelConfigurationId { get; set; }
    public SystemAiApiConfiguration? TestedModelConfiguration { get; set; }

    [MaxLength(64)]
    public string TestedModelProviderUsed { get; set; } = default!;

    [MaxLength(128)]
    public string TestedModelIdUsed { get; set; } = default!;

    [MaxLength(256)]
    public string TestedModelDisplayNameUsed { get; set; } = default!;

    [MaxLength(32)]
    public string? TestedModelThinkingLevelUsed { get; set; }

    [MaxLength(32)]
    public string? TestedModelReasoningModeUsed { get; set; }

    [MaxLength(32)]
    public string? TestedModelReasoningSummaryUsed { get; set; }

    [MaxLength(64)]
    public string? TestedModelServiceTierUsed { get; set; }

    public int? TestedModelMaxOutputTokensUsed { get; set; }

    public ParallelExecutionMode TestedModelParallelExecutionModeUsed { get; set; } = ParallelExecutionMode.Enabled;

    // Assessor Model Config snapshot
    public long? AssessorModelConfigurationId { get; set; }
    public SystemAiApiConfiguration? AssessorModelConfiguration { get; set; }

    [MaxLength(64)]
    public string AssessorModelProviderUsed { get; set; } = default!;

    [MaxLength(128)]
    public string AssessorModelIdUsed { get; set; } = default!;

    [MaxLength(256)]
    public string AssessorModelDisplayNameUsed { get; set; } = default!;

    [MaxLength(32)]
    public string? AssessorModelThinkingLevelUsed { get; set; }

    [MaxLength(32)]
    public string? AssessorModelReasoningModeUsed { get; set; }

    [MaxLength(32)]
    public string? AssessorModelReasoningSummaryUsed { get; set; }

    [MaxLength(64)]
    public string? AssessorModelServiceTierUsed { get; set; }

    public int? AssessorModelMaxOutputTokensUsed { get; set; }

    public ParallelExecutionMode AssessorModelParallelExecutionModeUsed { get; set; } = ParallelExecutionMode.Enabled;

    // Run metadata
    [MaxLength(450)]
    public string? StartedByUserId { get; set; }
    public ApplicationUser? StartedByUser { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public BenchmarkRunStatus Status { get; set; } = BenchmarkRunStatus.Running;

    [MaxLength(2048)]
    public string? ErrorMessage { get; set; }

    public int? FinalScore { get; set; }

    // Superseded by QualityIndex
    public int? ComputedScore { get; set; }

    public int? QualityIndex { get; set; }

    public int? SpeedIndex { get; set; }

    public long TotalAnswerDurationMs { get; set; }

    public long? ScoringProfileId { get; set; }
    public BenchmarkScoringProfile? ScoringProfile { get; set; }

    public string? ScoringProfileSnapshotJson { get; set; }

    public int ScoringMethodVersion { get; set; }

    public bool DifficultyFallbackUsed { get; set; }

    public bool SpeedMeasurementDegraded { get; set; }

    public int MaxParallelQuestionsUsed { get; set; } = 1;

    public int AnsweredQuestionCount { get; set; }

    public int TotalQuestionCount { get; set; }

    public int DegradedAnswerCount { get; set; }

    public int ToolStarvedAnswerCount { get; set; }

    public int? MaxToolCallsPerQuestionUsed { get; set; }

    [MaxLength(32)]
    public string? HarnessVersion { get; set; }

    [MaxLength(2048)]
    public string? PurposeStatementUsed { get; set; }

    public bool SameProviderAcknowledged { get; set; }

    public string? AssessmentJson { get; set; }

    public string? AssessmentText { get; set; }

    public bool AssessmentParseFailed { get; set; }

    // Token totals and duration
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCacheReadTokens { get; set; }
    public long TotalCacheCreationTokens { get; set; }
    public long TotalDurationMs { get; set; }

    public List<BenchmarkRunAnswer> Answers { get; set; } = new();
}
