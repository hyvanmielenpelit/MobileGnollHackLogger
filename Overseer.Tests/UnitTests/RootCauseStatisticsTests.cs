namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Benchmarking.RootCause;
using Xunit;

/// <summary>
/// The root-cause statistics: Spearman's ρ with its Fisher-z interval, the Mann–Whitney test, the
/// fixed-seed bootstrap and the exact two- and three-factor LMDI splits.
/// </summary>
public class RootCauseStatisticsTests
{
    [Fact]
    public void Spearman_OfAPerfectlyMonotoneSeries_IsOne_AndItsIntervalExcludesZero()
    {
        var x = new double[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var y = new double[] { 10, 20, 25, 40, 41, 70, 90, 100 };

        var rho = RootCauseStatistics.Spearman(x, y)!;

        Assert.Equal(8, rho.Count);
        Assert.Equal(1.0, rho.Rho, 9);
        Assert.True(rho.ExcludesZero);
        Assert.True(rho.Lower > 0.9);
    }

    [Fact]
    public void Spearman_MatchesTheRankFormula_WithTiesAveraged()
    {
        var x = new double[] { 1, 2, 2, 4, 5 };
        var y = new double[] { 5, 3, 4, 1, 2 };

        var rho = RootCauseStatistics.Spearman(x, y)!;

        // Ranks x: 1, 2.5, 2.5, 4, 5; y: 5, 3, 4, 1, 2. Pearson of the ranks.
        var rx = new[] { 1, 2.5, 2.5, 4, 5 };
        var ry = new double[] { 5, 3, 4, 1, 2 };
        double mx = rx.Average(), my = ry.Average();
        double expected = rx.Zip(ry, (a, b) => (a - mx) * (b - my)).Sum()
            / Math.Sqrt(rx.Sum(a => (a - mx) * (a - mx)) * ry.Sum(b => (b - my) * (b - my)));
        Assert.Equal(expected, rho.Rho, 12);

        double half = 1.959963984540054 / Math.Sqrt(5 - 3);
        Assert.Equal(Math.Tanh(Math.Atanh(expected) - half), rho.Lower, 12);
        Assert.Equal(Math.Tanh(Math.Atanh(expected) + half), rho.Upper, 12);
    }

    [Fact]
    public void Spearman_NeedsFourPairs_AndSpreadOnBothSides()
    {
        Assert.Null(RootCauseStatistics.Spearman(new double[] { 1, 2, 3 }, new double[] { 1, 2, 3 }));
        Assert.Null(RootCauseStatistics.Spearman(new double[] { 1, 2, 3, 4 }, new double[] { 7, 7, 7, 7 }));
        Assert.Throws<ArgumentException>(() => RootCauseStatistics.Spearman(new double[] { 1, 2 }, new double[] { 1 }));
    }

    [Fact]
    public void MannWhitney_SeparatedGroups_AreSignificant_AndOverlappingOnesAreNot()
    {
        var low = new double[] { 10, 12, 11, 13, 9, 14, 12, 10 };
        var high = new double[] { 80, 85, 90, 82, 88, 79, 91, 86 };

        var separated = RootCauseStatistics.MannWhitney(low, high)!;
        Assert.Equal(0, separated.U);
        Assert.True(separated.PValue < 0.01);
        Assert.True(separated.MedianDifference < 0);

        var mixed = RootCauseStatistics.MannWhitney(new double[] { 1, 4, 5, 8 }, new double[] { 2, 3, 6, 7 })!;
        Assert.True(mixed.PValue > 0.5);

        Assert.Null(RootCauseStatistics.MannWhitney(Array.Empty<double>(), high));
        Assert.Null(RootCauseStatistics.MannWhitney(new double[] { 5, 5 }, new double[] { 5, 5 }));
    }

    [Fact]
    public void Bootstrap_IsReproducibleForTheFixedSeed()
    {
        var values = Enumerable.Range(1, 30).Select(i => (double)(i * i % 17)).ToList();

        var first = RootCauseStatistics.MeanWithInterval(values);
        var second = RootCauseStatistics.MeanWithInterval(values);

        Assert.Equal(20261015, RootCauseStatistics.Seed);
        Assert.Equal(2000, RootCauseStatistics.Resamples);
        Assert.NotNull(first.Interval);
        Assert.Equal(first.Interval, second.Interval);
        Assert.True(first.Interval!.Lower <= first.Mean && first.Mean <= first.Interval.Upper);
    }

    [Fact]
    public void Bootstrap_IsNullForNoValues_OrWhenMostResamplesHaveNoStatistic()
    {
        Assert.Null(RootCauseStatistics.Bootstrap(0, _ => 1.0));
        Assert.Null(RootCauseStatistics.Bootstrap(10, _ => null));
    }

    [Theory]
    [InlineData(4.0, 4.0, 4.0)]
    [InlineData(2.0, 8.0, 4.328085122666891)]
    public void LogMean_IsTheLogarithmicMean_AndAOnTheDiagonal(double a, double b, double expected)
    {
        Assert.Equal(expected, RootCauseStatistics.LogMean(a, b), 9);
        Assert.Equal(0, RootCauseStatistics.LogMean(0, 3));
    }

    [Theory]
    [InlineData(120.0, 80.0, 40.0, 20.0)]
    [InlineData(80.0, 120.0, 20.0, 40.0)]
    [InlineData(50.0, 50.0, 10.0, 25.0)]
    public void Lmdi_VolumeAndPrice_SumExactlyToTheDifference(double totalA, double totalB, double volumeA, double volumeB)
    {
        var (volume, price) = RootCauseStatistics.Lmdi(totalA, totalB, volumeA, volumeB);

        Assert.Equal(totalA - totalB, volume + price, 12);
        Assert.Equal(RootCauseStatistics.LogMean(totalA, totalB) * Math.Log(volumeA / volumeB), volume, 12);
    }

    [Fact]
    public void Lmdi_AZeroComponent_SendsTheWholeDifferenceToVolume()
    {
        Assert.Equal((30.0, 0.0), RootCauseStatistics.Lmdi(30, 0, 10, 0));
        Assert.Equal((-12.0, 0.0), RootCauseStatistics.Lmdi(0, 12, 0, 4));
    }

    [Theory]
    [InlineData(120.0, 80.0, 40.0, 20.0, 3.0, 2.0)]
    [InlineData(80.0, 120.0, 20.0, 40.0, 2.0, 3.0)]
    [InlineData(50.0, 50.0, 10.0, 25.0, 1.0, 1.0)]
    public void Lmdi3_VolumeListAndCaching_SumExactlyToTheDifference(double totalA, double totalB, double volumeA, double volumeB, double listA, double listB)
    {
        var (volume, list, caching) = RootCauseStatistics.Lmdi3(totalA, totalB, volumeA, volumeB, listA, listB);

        double l = RootCauseStatistics.LogMean(totalA, totalB);
        Assert.Equal(totalA - totalB, volume + list + caching, 12);
        Assert.Equal(l * Math.Log(volumeA / volumeB), volume, 12);
        Assert.Equal(l * Math.Log(listA / listB), list, 12);
        // The caching effect is what sets the effective price (total ÷ volume) apart from the list price.
        Assert.Equal(l * Math.Log(totalA / volumeA / listA / (totalB / volumeB / listB)), caching, 9);
    }

    [Fact]
    public void Lmdi3_ANonPositiveInput_FallsBackToTheTwoFactorSplit_WithoutCaching()
    {
        // A zero total or volume: the whole difference goes to volume, as Lmdi.
        Assert.Equal((30.0, 0.0, 0.0), RootCauseStatistics.Lmdi3(30, 0, 10, 0, 2, 2));
        // A missing list price: Lmdi's price effect becomes the list-price effect.
        var (volume, price) = RootCauseStatistics.Lmdi(120, 80, 40, 20);
        Assert.Equal((volume, price, 0.0), RootCauseStatistics.Lmdi3(120, 80, 40, 20, 0, 2));
    }

    [Fact]
    public void Ranks_AverageTies()
    {
        Assert.Equal(new[] { 2.5, 1, 2.5, 4 }, RootCauseStatistics.Ranks(new double[] { 3, 1, 3, 9 }));
    }
}
