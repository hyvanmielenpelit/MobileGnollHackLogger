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

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
}
