namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;

public record BenchmarkScoringConstants
{
    public double WeightAccuracy { get; init; } = 0.55;
    public double WeightCompleteness { get; init; } = 0.25;
    public double WeightConciseness { get; init; } = 0.10;
    public double WeightReadability { get; init; } = 0.10;
    public IReadOnlyList<int> LevelScores { get; init; } = new[] { 1, 15, 35, 55, 72, 87, 100 };
    public int CriticalErrorCeiling { get; init; } = 25;
    public int SpeedTargetMs { get; init; } = 5000;
    public double SpeedDecayK { get; init; } = 25.0;

    public static BenchmarkScoringConstants Default { get; } = new();
}

public static class BenchmarkScoring
{
    public static int Score(int level, IReadOnlyList<int>? table = null)
    {
        if (table == null || table.Count == 0)
        {
            table = BenchmarkScoringConstants.Default.LevelScores;
        }

        int clampedLevel = Math.Clamp(level, 0, table.Count - 1);
        return table[clampedLevel];
    }

    public static (int Score, bool CapApplied) Quality(
        int accuracyLevel,
        int completenessLevel,
        int concisenessLevel,
        int readabilityLevel,
        bool criticalError,
        BenchmarkScoringConstants? constants = null)
    {
        var cfg = constants ?? BenchmarkScoringConstants.Default;

        double a = Math.Max(1.0, Score(accuracyLevel, cfg.LevelScores));
        double c = Math.Max(1.0, Score(completenessLevel, cfg.LevelScores));
        double cn = Math.Max(1.0, Score(concisenessLevel, cfg.LevelScores));
        double r = Math.Max(1.0, Score(readabilityLevel, cfg.LevelScores));

        double rawQuality = Math.Pow(a, cfg.WeightAccuracy) *
                            Math.Pow(c, cfg.WeightCompleteness) *
                            Math.Pow(cn, cfg.WeightConciseness) *
                            Math.Pow(r, cfg.WeightReadability);

        rawQuality = Math.Clamp(rawQuality, 1.0, 100.0);

        bool capApplied = false;
        if (criticalError)
        {
            if (rawQuality > cfg.CriticalErrorCeiling)
            {
                rawQuality = cfg.CriticalErrorCeiling;
                capApplied = true;
            }
        }

        int finalScore = (int)Math.Round(rawQuality, MidpointRounding.AwayFromZero);
        return (finalScore, capApplied);
    }

    public static int Speed(long durationMs, BenchmarkScoringConstants? constants = null)
    {
        var cfg = constants ?? BenchmarkScoringConstants.Default;

        if (durationMs <= 0 || cfg.SpeedTargetMs <= 0)
        {
            return 100;
        }

        double ratio = (double)durationMs / cfg.SpeedTargetMs;
        if (ratio <= 0.0)
        {
            return 100;
        }

        double rawSpeed = 100.0 - cfg.SpeedDecayK * Math.Log2(ratio);
        double clampedSpeed = Math.Clamp(rawSpeed, 1.0, 100.0);

        return (int)Math.Round(clampedSpeed, MidpointRounding.AwayFromZero);
    }

    public static int? QualityIndex(IEnumerable<(int? QualityScore, int? Difficulty)> items)
    {
        if (items == null) return null;

        double weightedSum = 0.0;
        double weightSum = 0.0;
        int count = 0;

        foreach (var (quality, difficulty) in items)
        {
            if (quality.HasValue)
            {
                double diffWeight = Math.Max(1.0, (double)(difficulty ?? 50));
                weightedSum += quality.Value * diffWeight;
                weightSum += diffWeight;
                count++;
            }
        }

        if (count == 0 || weightSum <= 0.0)
        {
            return null;
        }

        return (int)Math.Round(weightedSum / weightSum, MidpointRounding.AwayFromZero);
    }

    public static int? QualityIndex(IEnumerable<(int? QualityScore, int Difficulty)> items)
    {
        return QualityIndex(items?.Select(i => (i.QualityScore, (int?)i.Difficulty))!);
    }

    public static int? SpeedIndex(IEnumerable<(int? SpeedScore, int? Difficulty)> items)
    {
        if (items == null) return null;

        double weightedSum = 0.0;
        double weightSum = 0.0;
        int count = 0;

        foreach (var (speed, difficulty) in items)
        {
            if (speed.HasValue)
            {
                double diffWeight = Math.Max(1.0, (double)(difficulty ?? 50));
                weightedSum += speed.Value * diffWeight;
                weightSum += diffWeight;
                count++;
            }
        }

        if (count == 0 || weightSum <= 0.0)
        {
            return null;
        }

        return (int)Math.Round(weightedSum / weightSum, MidpointRounding.AwayFromZero);
    }

    public static int? SpeedIndex(IEnumerable<(int? SpeedScore, int Difficulty)> items)
    {
        return SpeedIndex(items?.Select(i => (i.SpeedScore, (int?)i.Difficulty))!);
    }
}
