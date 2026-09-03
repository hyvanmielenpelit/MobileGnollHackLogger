namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkScoringTests
{
    [Fact]
    public void Score_MapsAllLevelsCorrectly()
    {
        var config = BenchmarkScoringConstants.Default;

        Assert.Equal(1, BenchmarkScoring.Score(0, config.LevelScores));
        Assert.Equal(15, BenchmarkScoring.Score(1, config.LevelScores));
        Assert.Equal(35, BenchmarkScoring.Score(2, config.LevelScores));
        Assert.Equal(55, BenchmarkScoring.Score(3, config.LevelScores));
        Assert.Equal(72, BenchmarkScoring.Score(4, config.LevelScores));
        Assert.Equal(87, BenchmarkScoring.Score(5, config.LevelScores));
        Assert.Equal(100, BenchmarkScoring.Score(6, config.LevelScores));
    }

    [Fact]
    public void Score_ClampsOutOfBoundsLevels()
    {
        var config = BenchmarkScoringConstants.Default;

        Assert.Equal(1, BenchmarkScoring.Score(-5, config.LevelScores));
        Assert.Equal(100, BenchmarkScoring.Score(10, config.LevelScores));
    }

    [Fact]
    public void Quality_ComputesWeightedGeometricMean()
    {
        var config = BenchmarkScoringConstants.Default;

        // All level 6 -> 100
        var (perfectQuality, perfectRaw, _) = BenchmarkScoring.Quality(6, 6, 6, 6, false, config);
        Assert.Equal(100, perfectQuality);
        Assert.Equal(100, perfectRaw);

        // All level 0 -> 1
        var (lowestQuality, lowestRaw, _) = BenchmarkScoring.Quality(0, 0, 0, 0, false, config);
        Assert.Equal(1, lowestQuality);
        Assert.Equal(1, lowestRaw);

        // Accuracy=5 (87), Completeness=4 (72), Conciseness=6 (100), Readability=5 (87)
        // 87^0.55 * 72^0.25 * 100^0.10 * 87^0.10 ≈ 83.4 -> 83
        var (quality, rawQuality, _) = BenchmarkScoring.Quality(5, 4, 6, 5, false, config);
        Assert.InRange(quality, 80, 86);
        Assert.Equal(quality, rawQuality);
    }

    [Fact]
    public void Quality_CapsAtCeiling_WhenCriticalErrorIsTrue()
    {
        var config = BenchmarkScoringConstants.Default;

        // Perfect answers with a critical hallucination/crash: Raw is 100, capped at 25
        var (cappedQuality, rawScore, capApplied) = BenchmarkScoring.Quality(6, 6, 6, 6, true, config);
        Assert.Equal(25, cappedQuality);
        Assert.Equal(100, rawScore);
        Assert.True(capApplied);

        // If raw quality is lower than 25, it stays lower
        var (lowQualityWithCritical, lowRaw, lowCapApplied) = BenchmarkScoring.Quality(1, 1, 1, 1, true, config);
        Assert.Equal(15, lowQualityWithCritical);
        Assert.Equal(15, lowRaw);
        Assert.False(lowCapApplied);
    }

    [Fact]
    public void Speed_CalculatesLogarithmicDecayCorrectly()
    {
        // Pinned to explicit constants rather than the profile defaults, so recalibrating the
        // defaults cannot silently change what this test proves about the curve's shape.
        var config = new BenchmarkScoringConstants { SpeedTargetMs = 5000, SpeedDecayK = 25.0 };

        // <= target (5000 ms) -> 100
        Assert.Equal(100, BenchmarkScoring.Speed(0, config));
        Assert.Equal(100, BenchmarkScoring.Speed(2500, config));
        Assert.Equal(100, BenchmarkScoring.Speed(5000, config));

        // 10000 ms (2x target) -> 100 - 25 * log2(2) = 75
        Assert.Equal(75, BenchmarkScoring.Speed(10000, config));

        // 20000 ms (4x target) -> 100 - 25 * log2(4) = 50
        Assert.Equal(50, BenchmarkScoring.Speed(20000, config));

        // 40000 ms (8x target) -> 100 - 25 * log2(8) = 25
        Assert.Equal(25, BenchmarkScoring.Speed(40000, config));

        // 80000 ms (16x target) -> 100 - 25 * log2(16) = 0 -> clamped to 1
        Assert.Equal(1, BenchmarkScoring.Speed(80000, config));

        // Very long duration clamped to 1
        Assert.Equal(1, BenchmarkScoring.Speed(300000, config));
    }

    [Fact]
    public void DefaultSpeedConstants_AreTheRecalibratedValues()
    {
        var cfg = BenchmarkScoringConstants.Default;

        Assert.Equal(15000, cfg.SpeedTargetMs);
        Assert.Equal(20.0, cfg.SpeedDecayK);
        Assert.Equal(1.0, cfg.SpeedDifficultyScaling);
    }

    [Fact]
    public void QualityIndex_WeightsByDifficulty()
    {
        // 2 questions: Simple (diff 25) scored 100, Advanced (diff 85) scored 0
        // Weighted = (100 * 25 + 0 * 85) / (25 + 85) = 2500 / 110 ≈ 22.7 -> 23
        var items = new List<(int? QualityScore, int Difficulty)>
        {
            (100, 25),
            (0, 85)
        };

        int? index = BenchmarkScoring.QualityIndex(items);
        Assert.NotNull(index);
        Assert.Equal(23, index.Value);
    }

    [Fact]
    public void QualityIndex_IgnoresNullQualityScores()
    {
        var items = new List<(int? QualityScore, int? Difficulty)>
        {
            (80, 50),
            (null, 50)
        };

        int? index = BenchmarkScoring.QualityIndex(items);
        Assert.NotNull(index);
        Assert.Equal(80, index.Value);
    }

    [Fact]
    public void QualityIndex_ReturnsNullForEmptyList()
    {
        var items = new List<(int? QualityScore, int? Difficulty)>();
        Assert.Null(BenchmarkScoring.QualityIndex(items));
    }

    [Fact]
    public void SpeedIndex_IsEqualWeightMean()
    {
        int? index = BenchmarkScoring.SpeedIndex(new int?[] { 100, 50 });

        Assert.NotNull(index);
        Assert.Equal(75, index.Value);
    }

    [Fact]
    public void SpeedIndex_IsNotPulledDownByHardQuestions()
    {
        // Difficulty enters through the per-question target, not the aggregate weight. Under the
        // old difficulty-weighted index, the slow hard question dominated and the index came out
        // near it rather than midway.
        int? equalWeight = BenchmarkScoring.SpeedIndex(new int?[] { 95, 45 });
        int? oldWeighted = BenchmarkScoring.DifficultyWeightedSpeedIndex(
            new List<(int? SpeedScore, int? Difficulty)> { (95, 20), (45, 95) });

        Assert.Equal(70, equalWeight!.Value);
        Assert.True(oldWeighted!.Value < equalWeight.Value,
            "the difficulty-weighted index should sit below the equal-weight one here");
    }

    [Fact]
    public void SpeedIndex_ReturnsNullWhenNothingScored()
    {
        Assert.Null(BenchmarkScoring.SpeedIndex(new int?[] { null, null }));
        Assert.Null(BenchmarkScoring.SpeedIndex(System.Array.Empty<int?>()));
    }

    // --- Recalibrated speed model (Phase B3) ---

    [Fact]
    public void EffectiveSpeedTarget_ScalesWithDifficulty()
    {
        var cfg = BenchmarkScoringConstants.Default;

        // 15000 * (1 + 1.0 * d/100)
        Assert.Equal(15150.0, BenchmarkScoring.EffectiveSpeedTargetMs(1, cfg), 1);
        Assert.Equal(18750.0, BenchmarkScoring.EffectiveSpeedTargetMs(25, cfg), 1);
        Assert.Equal(26250.0, BenchmarkScoring.EffectiveSpeedTargetMs(75, cfg), 1);
        Assert.Equal(30000.0, BenchmarkScoring.EffectiveSpeedTargetMs(100, cfg), 1);
    }

    [Fact]
    public void Speed_UnderDifficultyScaledTarget_ScoresFull()
    {
        // A hard question is allowed more time before it starts losing points.
        Assert.Equal(100, BenchmarkScoring.Speed(26000, 75));
        Assert.True(BenchmarkScoring.Speed(26000, 25) < 100);
    }

    [Fact]
    public void Speed_ProducesAUsableSpreadOnTheReferenceRun()
    {
        // Model-attributable times from the 2026-09-03 GPT-5.6 Luna run (turn duration less an
        // estimate of its tool overhead). Under the previous 5000 ms / k=25 constants, the last
        // three of these all scored exactly 1 and were indistinguishable.
        int q1 = BenchmarkScoring.Speed(20000, 25);
        int q8 = BenchmarkScoring.Speed(38000, 54);
        int q2 = BenchmarkScoring.Speed(63000, 28);
        int q13 = BenchmarkScoring.Speed(131000, 75);
        int q15 = BenchmarkScoring.Speed(168000, 90);

        foreach (int score in new[] { q1, q8, q2, q13, q15 })
        {
            Assert.InRange(score, 2, 100);
        }

        // Strictly ordered: no ties, and a real spread across the range.
        Assert.True(q1 > q8 && q8 > q2 && q2 > q13 && q13 > q15,
            $"expected a strict ordering, got {q1}, {q8}, {q2}, {q13}, {q15}");
        Assert.True(q1 - q15 >= 30, $"expected a spread of at least 30 points, got {q1 - q15}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(70)]
    [InlineData(100)]
    public void Speed_FloorIsUnreachableWithinThePerQuestionTimeout(int difficulty)
    {
        // Invariant 1 of the calibration: an answer that did not time out must always receive a
        // distinguishing score. Violating this is what made the old constants useless — they
        // floored at ~78 s against a 300 s timeout.
        const long timeoutMs = 300_000;

        int score = BenchmarkScoring.Speed(timeoutMs, difficulty);

        Assert.True(score > 1,
            $"difficulty {difficulty} floored at the timeout ({score}); the metric would saturate");
    }
}
