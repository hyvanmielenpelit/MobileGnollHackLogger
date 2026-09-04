namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MobileGnollHackLogger.Data;

public record BenchmarkScoringConstants
{
    public double WeightAccuracy { get; init; } = 0.55;
    public double WeightCompleteness { get; init; } = 0.25;
    public double WeightConciseness { get; init; } = 0.10;
    public double WeightReadability { get; init; } = 0.10;
    public IReadOnlyList<int> LevelScores { get; init; } = new[] { 1, 15, 35, 55, 72, 87, 100 };
    public int CriticalErrorCeiling { get; init; } = 25;

    /// <summary>
    /// Quality score below which an answer is re-graded by the run's second-opinion assessor,
    /// when one was selected in the start dialog. 0 disables the score trigger; a critical
    /// error triggers a re-grade regardless. Carried on the profile, and therefore snapshotted
    /// into the run, so a report can say what threshold produced its second verdicts.
    /// </summary>
    public int SecondOpinionQualityThreshold { get; init; } = 50;

    /// <summary>
    /// How the second-opinion assessor is used. See <see cref="BenchmarkSecondOpinionMode"/>.
    /// Inert without a second-opinion assessor configured on the run.
    /// </summary>
    public BenchmarkSecondOpinionMode SecondOpinionMode { get; init; } = BenchmarkSecondOpinionMode.Flagged;

    /// <summary>
    /// Quality points below the run's own median at which an answer is re-graded, in
    /// <see cref="BenchmarkSecondOpinionMode.FlaggedAndOutliers"/> only. An absolute threshold
    /// cannot see an outlier in an otherwise strong run; this can.
    /// </summary>
    public int SecondOpinionOutlierDeltaPoints { get; init; } = 25;

    /// <summary>
    /// Whether the second-opinion assessor should be blinded to the first assessor's score,
    /// critical-error flag, and comment. Defaults to true.
    /// </summary>
    public bool SecondOpinionBlind { get; init; } = true;

    // Speed constants are pinned to two invariants rather than to a convention:
    //
    //  1. The score floor must be unreachable within Benchmark:QuestionTimeoutSeconds:{Band} at
    //     every difficulty, so any answer that did not time out gets a distinguishing score.
    //     The previous 5000 ms / k=25 pair violated this — it floored at ~78 s, which tied six
    //     of eighteen answers together on the 2026-09-03 run and made the index uninformative.
    //  2. A perfect score stays reserved for a genuinely fast turn, so the metric does not
    //     compress at the top instead of at the bottom.
    //
    // Invariant 1 is why the per-question timeout is banded rather than flat. The floor is
    // reached at ModelTime / Target(q) = 2^(99/20) ≈ 30.91, and Target(q) grows with difficulty,
    // so the binding case inside a band is its *lowest* difficulty — the smallest target, and
    // therefore the earliest floor:
    //
    //     Band          Lowest diff.  Target(q)   Floor at   Timeout   Margin
    //     Simple                   1   15,150 ms    ~468 s     420 s    ~48 s
    //     Intermediate            36   20,400 ms    ~631 s     600 s    ~31 s
    //     Advanced                71   25,650 ms    ~793 s     720 s    ~73 s
    //
    // A flat 720 s — the value an Advanced question needs to spend 45 tool calls over 22 rounds
    // — would let a Simple question run ~250 s past its own 468 s floor without timing out, so
    // every Simple answer slower than 468 s would score 1 and be indistinguishable from every
    // other slow one. That is the exact failure these constants exist to prevent, and it is what
    // the old 5000 ms / k=25 pair actually did. BenchmarkScoringTests asserts
    // every margin above, so editing either these constants or the timeout bands without
    // re-deriving the table fails the build rather than quietly degrading the metric.
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
    /// <summary>
    /// The constants a run was actually scored with, read back from the profile snapshot the run
    /// stored at start time. Falls back to the defaults for a run that carries no snapshot, and
    /// for any field the snapshot omits.
    ///
    /// One reader, deliberately: the snapshot is a storage format, and the report, the run-detail
    /// projection and the admin diagnostics all need the same fields out of it. A second parser
    /// — in another class here, or in TypeScript on the client — is a second thing that has to be
    /// kept in step with the profile shape.
    /// </summary>
    public static BenchmarkScoringConstants ConstantsFromSnapshot(string? snapshotJson)
    {
        var defaults = BenchmarkScoringConstants.Default;
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return defaults;
        }

        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            var root = doc.RootElement;

            int target = root.TryGetProperty("SpeedTargetMs", out var t) && t.TryGetInt32(out int tv)
                ? tv
                : defaults.SpeedTargetMs;
            double scaling = root.TryGetProperty("SpeedDifficultyScaling", out var s) && s.TryGetDouble(out double sv)
                ? sv
                : defaults.SpeedDifficultyScaling;
            double decay = root.TryGetProperty("SpeedDecayK", out var k) && k.TryGetDouble(out double kv)
                ? kv
                : defaults.SpeedDecayK;
            int secondOpinion = root.TryGetProperty("SecondOpinionQualityThreshold", out var o) && o.TryGetInt32(out int ov)
                ? ov
                : defaults.SecondOpinionQualityThreshold;

            // Serialized from the profile entity, where the mode is an int column.
            var mode = defaults.SecondOpinionMode;
            if (root.TryGetProperty("SecondOpinionMode", out var m) && m.TryGetInt32(out int mv) &&
                Enum.IsDefined(typeof(BenchmarkSecondOpinionMode), mv))
            {
                mode = (BenchmarkSecondOpinionMode)mv;
            }

            int outlierDelta = root.TryGetProperty("SecondOpinionOutlierDeltaPoints", out var d) && d.TryGetInt32(out int dv)
                ? dv
                : defaults.SecondOpinionOutlierDeltaPoints;

            bool secondOpinionBlind = root.TryGetProperty("SecondOpinionBlind", out var bl) && (bl.ValueKind == JsonValueKind.True || bl.ValueKind == JsonValueKind.False)
                ? bl.GetBoolean()
                : defaults.SecondOpinionBlind;

            return defaults with
            {
                SpeedTargetMs = target,
                SpeedDifficultyScaling = scaling,
                SpeedDecayK = decay,
                SecondOpinionQualityThreshold = secondOpinion,
                SecondOpinionMode = mode,
                SecondOpinionOutlierDeltaPoints = outlierDelta,
                SecondOpinionBlind = secondOpinionBlind
            };
        }
        catch (JsonException)
        {
            // A malformed snapshot must not take a report or a screen down with it: the defaults
            // describe the wrong run, but the caller can see the snapshot itself and say so.
            return defaults;
        }
    }

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
    /// Standard error of the difficulty-weighted Intelligence Index over the answered items, with the
    /// n/(n−1) correction. Null below 3 items. This is item-sampling error — how much the index would
    /// move if the suite had drawn a different 18 questions of the same difficulty profile — and NOT
    /// the model's run-to-run variance, which one sample per question cannot estimate.
    /// </summary>
    public static double? QualityIndexStandardError(IEnumerable<(int? QualityScore, int? Difficulty)> items)
    {
        if (items == null) return null;

        var index = QualityIndex(items);
        if (!index.HasValue) return null;

        double sumWeightedSqDev = 0.0;
        double weightSum = 0.0;
        int n = 0;

        foreach (var (quality, difficulty) in items)
        {
            if (quality.HasValue)
            {
                double w = Math.Max(1.0, (double)(difficulty ?? 50));
                double dev = quality.Value - index.Value;
                sumWeightedSqDev += w * w * dev * dev;
                weightSum += w;
                n++;
            }
        }

        if (n < 3 || weightSum <= 0.0)
        {
            return null;
        }

        double correction = (double)n / (n - 1);
        double variance = sumWeightedSqDev * correction;
        return Math.Sqrt(variance) / weightSum;
    }

    public static double? QualityIndexStandardError(IEnumerable<(int? QualityScore, int Difficulty)> items)
    {
        return QualityIndexStandardError(items?.Select(i => (i.QualityScore, (int?)i.Difficulty))!);
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
    /// The equal-weight mean of the per-question quality scores — the plain average the
    /// difficulty-weighted <see cref="QualityIndex(IEnumerable{ValueTuple{int?, int?}})"/> is not.
    ///
    /// Reported beside the index rather than instead of it. Difficulty weighting is a deliberate
    /// property of the Intelligence Index and is not being second-guessed here; what it needs is
    /// to be *visible*, because the direction it moves the headline depends on which questions a
    /// model got wrong. A run whose weak answers are its easy ones reads higher weighted than
    /// unweighted, which is worth a reader knowing rather than inferring.
    /// </summary>
    public static int? UnweightedQualityMean(IEnumerable<int?> qualityScores)
    {
        if (qualityScores == null) return null;

        double sum = 0.0;
        int count = 0;

        foreach (var quality in qualityScores)
        {
            if (quality.HasValue)
            {
                sum += quality.Value;
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
