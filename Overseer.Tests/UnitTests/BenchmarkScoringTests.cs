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
        var (perfectQuality, _) = BenchmarkScoring.Quality(6, 6, 6, 6, false, config);
        Assert.Equal(100, perfectQuality);

        // All level 0 -> 1
        var (lowestQuality, _) = BenchmarkScoring.Quality(0, 0, 0, 0, false, config);
        Assert.Equal(1, lowestQuality);

        // Accuracy=5 (87), Completeness=4 (72), Conciseness=6 (100), Readability=5 (87)
        // 87^0.55 * 72^0.25 * 100^0.10 * 87^0.10 ≈ 83.4 -> 83
        var (quality, _) = BenchmarkScoring.Quality(5, 4, 6, 5, false, config);
        Assert.InRange(quality, 80, 86);
    }

    [Fact]
    public void Quality_CapsAtCeiling_WhenCriticalErrorIsTrue()
    {
        var config = BenchmarkScoringConstants.Default;

        // Perfect answers with a critical hallucination/crash
        var (cappedQuality, capApplied) = BenchmarkScoring.Quality(6, 6, 6, 6, true, config);
        Assert.Equal(25, cappedQuality);
        Assert.True(capApplied);

        // If raw quality is lower than 25, it stays lower
        var (lowQualityWithCritical, lowCapApplied) = BenchmarkScoring.Quality(1, 1, 1, 1, true, config);
        Assert.Equal(15, lowQualityWithCritical);
        Assert.False(lowCapApplied);
    }

    [Fact]
    public void Speed_CalculatesLogarithmicDecayCorrectly()
    {
        var config = BenchmarkScoringConstants.Default;

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
    public void SpeedIndex_WeightsByDifficulty()
    {
        var items = new List<(int? SpeedScore, int Difficulty)>
        {
            (100, 50),
            (50, 50)
        };

        int? index = BenchmarkScoring.SpeedIndex(items);
        Assert.NotNull(index);
        Assert.Equal(75, index.Value);
    }
}
