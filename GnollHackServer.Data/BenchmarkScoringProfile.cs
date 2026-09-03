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

    // Speed constants are calibrated so that the score floor is unreachable within
    // Benchmark:PerQuestionTimeoutSeconds at every difficulty, which is what keeps the metric
    // from saturating on agentic turns that legitimately make many tool calls.
    public int SpeedTargetMs { get; set; } = 15000;

    public double SpeedDecayK { get; set; } = 20.0;

    // Scales the speed target by assessed difficulty:
    //   effectiveTarget = SpeedTargetMs * (1 + SpeedDifficultyScaling * difficulty / 100)
    // Difficulty raises the expected time rather than the aggregate weight; weighting by
    // difficulty would penalise hard questions twice, once for being slow and once for
    // counting more.
    public double SpeedDifficultyScaling { get; set; } = 1.0;

    public int MaxParallelQuestions { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
}
