namespace MobileGnollHackLogger.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class BenchmarkSuite
{
    public long Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public long? GameSnapshotId { get; set; }
    public BenchmarkGameSnapshot? GameSnapshot { get; set; }

    /// <summary>True when any question in this suite came from the generator rather than a human.</summary>
    public bool HasGeneratedQuestions { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

    public List<BenchmarkQuestion> Questions { get; set; } = new();

    public List<BenchmarkRun> Runs { get; set; } = new();
}
