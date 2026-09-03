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

    // Speed constants are pinned to two invariants rather than to a convention:
    //
    //  1. The score floor must be unreachable within Benchmark:PerQuestionTimeoutSeconds (300 s)
    //     at every difficulty, so any answer that did not time out gets a distinguishing score.
    //     The previous 5000 ms / k=25 pair violated this — it floored at ~78 s, which tied six
    //     of eighteen answers together on the 2026-09-03 run and made the index uninformative.
    //  2. A perfect score stays reserved for a genuinely fast turn, so the metric does not
    //     compress at the top instead of at the bottom.
    public int SpeedTargetMs { get; init; } = 15000;
    public double SpeedDecayK { get; init; } = 20.0;

    /// <summary>
    /// Scales the speed target by assessed difficulty. Difficulty raises the expected time
    /// instead of the aggregate weight: weighting the index by difficulty penalised hard
    /// questions twice, once for being slow and again for counting more, which dragged the
    /// Speed Index toward the floor by construction.
    /// </summary>
    public double SpeedDifficultyScaling { get; init; } = 1.0;

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

    public static (int Score, int RawScore, bool CapApplied) Quality(
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
        int rawScore = (int)Math.Round(rawQuality, MidpointRounding.AwayFromZero);

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
        return (finalScore, rawScore, capApplied);
    }

    /// <summary>
    /// Scores a turn against a flat target. Retained so historical answers can be re-scored under
    /// the semantics that produced them; new scoring uses the difficulty-normalised overload.
    /// </summary>
    public static int Speed(long durationMs, BenchmarkScoringConstants? constants = null)
    {
        return SpeedAgainstTarget(durationMs, (constants ?? BenchmarkScoringConstants.Default).SpeedTargetMs, constants);
    }

    /// <summary>
    /// Scores model-attributable time against a target scaled by the question's assessed
    /// difficulty. Pass <paramref name="modelTimeMs"/>, not the raw turn duration: charging the
    /// model for tool execution latency made the metric a measure of harness I/O.
    /// </summary>
    public static int Speed(long modelTimeMs, int difficulty, BenchmarkScoringConstants? constants = null)
    {
        var cfg = constants ?? BenchmarkScoringConstants.Default;
        return SpeedAgainstTarget(modelTimeMs, EffectiveSpeedTargetMs(difficulty, cfg), cfg);
    }

    /// <summary>
    /// The speed target for a question of the given difficulty:
    /// <c>SpeedTargetMs * (1 + SpeedDifficultyScaling * difficulty / 100)</c>.
    /// </summary>
    public static double EffectiveSpeedTargetMs(int difficulty, BenchmarkScoringConstants? constants = null)
    {
        var cfg = constants ?? BenchmarkScoringConstants.Default;
        int clamped = Math.Clamp(difficulty, 1, 100);
        return cfg.SpeedTargetMs * (1.0 + cfg.SpeedDifficultyScaling * clamped / 100.0);
    }

    /// <summary>
    /// The shared curve. Named distinctly from the public overloads on purpose: as an overload it
    /// collided with <c>Speed(long, int, constants)</c> whenever the target was passed as an int,
    /// silently scoring against the target value as if it were a difficulty.
    /// </summary>
    private static int SpeedAgainstTarget(long durationMs, double targetMs, BenchmarkScoringConstants? constants)
    {
        var cfg = constants ?? BenchmarkScoringConstants.Default;

        if (durationMs <= 0 || targetMs <= 0.0)
        {
            return 100;
        }

        double ratio = durationMs / targetMs;
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

    /// <summary>
    /// Equal-weight mean of the per-question speed scores.
    ///
    /// Difficulty enters the speed metric through the per-question target
    /// (<see cref="EffectiveSpeedTargetMs"/>), not through the aggregate weight. Weighting here
    /// as well would count difficulty twice and pull the index toward the floor.
    /// </summary>
    public static int? SpeedIndex(IEnumerable<int?> speedScores)
    {
        if (speedScores == null) return null;

        double sum = 0.0;
        int count = 0;

        foreach (var speed in speedScores)
        {
            if (speed.HasValue)
            {
                sum += speed.Value;
                count++;
            }
        }

        if (count == 0)
        {
            return null;
        }

        return (int)Math.Round(sum / count, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Difficulty-weighted speed index. Superseded by <see cref="SpeedIndex(IEnumerable{int?})"/>
    /// and retained only to reproduce runs scored before scoring method version 4.
    /// </summary>
    public static int? DifficultyWeightedSpeedIndex(IEnumerable<(int? SpeedScore, int? Difficulty)> items)
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
}
