namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public enum BenchmarkAssessmentStatus
{
    Pending = 1,
    Assessing = 2,
    Scored = 3,
    Failed = 4
}

public class BenchmarkRunAnswer
{
    public long Id { get; set; }

    public long BenchmarkRunId { get; set; }
    public BenchmarkRun BenchmarkRun { get; set; } = default!;

    public int OrderIndex { get; set; }

    public string QuestionText { get; set; } = default!;

    public BenchmarkDifficulty Difficulty { get; set; } = BenchmarkDifficulty.Simple;

    public string AnswerText { get; set; } = default!;

    public string? ThoughtText { get; set; }

    public BenchmarkAnswerStatus Status { get; set; } = BenchmarkAnswerStatus.Ok;

    [MaxLength(2048)]
    public string? ErrorMessage { get; set; }

    public int? HttpStatusCode { get; set; }

    // Superseded by QualityScore and dimensional level scoring
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
    public int? RawQualityScore { get; set; }
    public int? SpeedScore { get; set; }

    public int? AssessedDifficulty { get; set; }

    public BenchmarkAssessmentStatus AssessmentStatus { get; set; } = BenchmarkAssessmentStatus.Pending;

    [MaxLength(2048)]
    public string? AssessmentError { get; set; }

    // Assessor snapshot for THIS answer's score. Populated on every per-question
    // assessment; it differs from BenchmarkRun.Assessor*Used only when a manual retry
    // was run with a different assessor because the original was unavailable.
    public long? AssessedByModelConfigurationId { get; set; }
    public SystemAiApiConfiguration? AssessedByModelConfiguration { get; set; }

    [MaxLength(256)]
    public string? AssessedByModelDisplayNameUsed { get; set; }

    [MaxLength(64)]
    public string? AssessedByModelProviderUsed { get; set; }

    [MaxLength(128)]
    public string? AssessedByModelIdUsed { get; set; }

    public DateTime? AssessedAtUtc { get; set; }

    public string? ReviewComment { get; set; }

    public long DurationMs { get; set; }

    public long? TimeToFirstTokenMs { get; set; }

    [MaxLength(64)]
    public string? ActualServiceTierUsed { get; set; }

    public string? ToolCallSummary { get; set; }

    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? CacheReadInputTokens { get; set; }
    public int? CacheCreationInputTokens { get; set; }

    public int? ModelCallCount { get; set; }
    public int? ToolCallCount { get; set; }
    public bool ToolBudgetExhausted { get; set; }
    [MaxLength(32)]
    public string? TerminationReason { get; set; }
    public int AnswerFlags { get; set; }
}
