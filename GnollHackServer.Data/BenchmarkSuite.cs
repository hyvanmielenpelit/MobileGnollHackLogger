namespace MobileGnollHackLogger.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class BenchmarkSuite
{
    public long Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(512)]
    public string? Description { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;

    public List<BenchmarkQuestion> Questions { get; set; } = new();

    public List<BenchmarkRun> Runs { get; set; } = new();
}
