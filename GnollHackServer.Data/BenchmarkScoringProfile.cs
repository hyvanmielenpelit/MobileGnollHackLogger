namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public class BenchmarkScoringProfile
{
    public long Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = default!;

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

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
}
