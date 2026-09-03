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

    /// <summary>
    /// Bumped whenever the question text or its rubric changes. An edited question is a
    /// *different item*: its statistics must not straddle the rewrite, so every stored answer
    /// records the revision it was answered against
    /// (<see cref="BenchmarkRunAnswer.ItemRevisionUsed"/>) and item analysis groups by it.
    ///
    /// Incremented at exactly the point that already clears the difficulty snapshot, because
    /// the two express the same fact: what this question asks has changed.
    /// </summary>
    public int ItemRevision { get; set; } = 1;

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
