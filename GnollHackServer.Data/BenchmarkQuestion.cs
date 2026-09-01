namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public class BenchmarkQuestion
{
    public long Id { get; set; }

    public long BenchmarkSuiteId { get; set; }
    public BenchmarkSuite BenchmarkSuite { get; set; } = default!;

    public int OrderIndex { get; set; }

    public string QuestionText { get; set; } = default!;

    public BenchmarkDifficulty Difficulty { get; set; } = BenchmarkDifficulty.Simple;

    public string? ExpectedPoints { get; set; }

    public int? AssessedDifficulty { get; set; }

    [MaxLength(256)]
    public string? AssessedDifficultyModel { get; set; }

    public DateTime? AssessedDifficultyAtUtc { get; set; }

    // Difficulty assessor snapshot (the model that produced AssessedDifficulty).
    // AssessedDifficultyModel above holds the display name; these hold the rest of its settings.
    public long? AssessedDifficultyModelConfigurationId { get; set; }
    public SystemAiApiConfiguration? AssessedDifficultyModelConfiguration { get; set; }

    [MaxLength(64)]
    public string? AssessedDifficultyProviderUsed { get; set; }

    [MaxLength(128)]
    public string? AssessedDifficultyModelIdUsed { get; set; }

    [MaxLength(32)]
    public string? AssessedDifficultyThinkingLevelUsed { get; set; }

    [MaxLength(32)]
    public string? AssessedDifficultyReasoningModeUsed { get; set; }

    [MaxLength(32)]
    public string? AssessedDifficultyReasoningSummaryUsed { get; set; }

    [MaxLength(64)]
    public string? AssessedDifficultyServiceTierUsed { get; set; }

    public int? AssessedDifficultyMaxOutputTokensUsed { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
}
