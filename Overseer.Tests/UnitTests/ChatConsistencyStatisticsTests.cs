namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;
using Xunit;

/// <summary>
/// Known-value checks for <see cref="ChatConsistencyStatistics"/>. Expected values are computed
/// by hand in each test, or are textbook values.
/// </summary>
public class ChatConsistencyStatisticsTests
{
    private static IReadOnlyList<IReadOnlyList<double>> Clusters(params double[][] clusters) => clusters;

    private static IReadOnlyDictionary<string, double> Run(params (string Key, double Value)[] items)
        => items.ToDictionary(i => i.Key, i => i.Value);

    // A period-4 pattern whose partial sums stay small, so no segmentation of it pays its penalty.
    private static double[] FlatPattern(int length)
    {
        double[] period = { 0.3, -0.1, -0.4, 0.2 };
        return Enumerable.Range(0, length).Select(i => period[i % period.Length]).ToArray();
    }

    // --- Hodges-Lehmann -------------------------------------------------------------------------

    [Fact]
    public void HodgesLehmannShift_OneSample_IsMedianOfWalshAverages()
    {
        // Walsh averages of {1, 2, 9}: 1, 1.5, 5, 2, 5.5, 9 -> sorted 1, 1.5, 2, 5, 5.5, 9 -> (2 + 5) / 2.
        double? shift = ChatConsistencyStatistics.HodgesLehmannShift(new[] { 1.0, 2.0, 9.0 });

        Assert.Equal(3.5, shift!.Value, 12);
    }

    [Fact]
    public void HodgesLehmannShift_OneSample_OddWalshCount_IsTheMiddleAverage()
    {
        // Walsh averages of {1, 3}: 1, 2, 3 -> 2.
        Assert.Equal(2.0, ChatConsistencyStatistics.HodgesLehmannShift(new[] { 1.0, 3.0 })!.Value, 12);
    }

    [Fact]
    public void HodgesLehmannShift_TwoSample_IsMedianOfPairwiseDifferences()
    {
        // y - x over x = {1, 2}, y = {3, 5}: 2, 4, 1, 3 -> (2 + 3) / 2.
        double? shift = ChatConsistencyStatistics.HodgesLehmannShift(new[] { 1.0, 2.0 }, new[] { 3.0, 5.0 });

        Assert.Equal(2.5, shift!.Value, 12);
    }

    [Fact]
    public void HodgesLehmannShift_EmptySample_IsNull()
    {
        Assert.Null(ChatConsistencyStatistics.HodgesLehmannShift(Array.Empty<double>()));
        Assert.Null(ChatConsistencyStatistics.HodgesLehmannShift(Array.Empty<double>(), new[] { 1.0 }));
    }

    [Fact]
    public void HodgesLehmannShift_NonFiniteValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => ChatConsistencyStatistics.HodgesLehmannShift(new[] { 1.0, double.NaN }));
    }

    // --- Cluster bootstrap ----------------------------------------------------------------------

    private static readonly double[][] FourRuns =
    {
        new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 },
        new[] { 2.0, 3.0, 4.0, 5.0, 6.0, 7.0 },
        new[] { 0.0, 1.0, 2.0, 3.0, 4.0, 5.0 },
        new[] { 1.5, 2.5, 3.5, 4.5, 5.5, 6.5 }
    };

    [Fact]
    public void ClusterBootstrap_FixedSeed_IsDeterministic()
    {
        var first = ChatConsistencyStatistics.ClusterBootstrap(Clusters(FourRuns), replicates: 2000, seed: 7)!;
        var second = ChatConsistencyStatistics.ClusterBootstrap(Clusters(FourRuns), replicates: 2000, seed: 7)!;
        var other = ChatConsistencyStatistics.ClusterBootstrap(Clusters(FourRuns), replicates: 2000, seed: 8)!;

        Assert.Equal(first.Replicates, second.Replicates);
        Assert.Equal(first.Interval95, second.Interval95);
        Assert.False(first.Replicates.SequenceEqual(other.Replicates));
    }

    [Fact]
    public void ClusterBootstrap_EstimateIsStatisticOfPooledValues_AndLiesInsideNestedIntervals()
    {
        var result = ChatConsistencyStatistics.ClusterBootstrap(Clusters(FourRuns))!;
        double pooled = ChatConsistencyStatistics.HodgesLehmannShift(FourRuns.SelectMany(r => r).ToArray())!.Value;

        Assert.Equal(pooled, result.Estimate, 12);
        Assert.Equal(ChatConsistencyStatistics.DefaultBootstrapReplicates, result.RequestedReplicates);
        Assert.Equal(new[] { 4 }, result.ClusterCounts);

        var (l95, u95) = result.Interval95;
        var (l90, u90) = result.Interval90;
        Assert.InRange(result.Estimate, l95, u95);
        Assert.InRange(result.Estimate, l90, u90);
        Assert.True(l95 <= l90 && l90 <= u90 && u90 <= u95);
    }

    [Fact]
    public void ClusterBootstrap_MeanStatistic_EstimateIsPooledMean()
    {
        var result = ChatConsistencyStatistics.ClusterBootstrap(Clusters(FourRuns), BootstrapStatistic.Mean, replicates: 500)!;

        Assert.Equal(FourRuns.SelectMany(r => r).Average(), result.Estimate, 12);
    }

    [Fact]
    public void ClusterBootstrap_RunsOnly_IdenticalRuns_GivesZeroWidthInterval()
    {
        var runs = Clusters(new[] { 1.0, 2.0, 3.0 }, new[] { 1.0, 2.0, 3.0 }, new[] { 1.0, 2.0, 3.0 });

        var result = ChatConsistencyStatistics.ClusterBootstrap(runs, resampleItems: false, replicates: 500)!;

        Assert.Equal(2.0, result.Estimate, 12);
        Assert.Equal(result.Estimate, result.Interval95.Lower);
        Assert.Equal(result.Estimate, result.Interval95.Upper);
    }

    [Fact]
    public void ClusterBootstrap_NoValues_IsNull()
    {
        Assert.Null(ChatConsistencyStatistics.ClusterBootstrap(Clusters(Array.Empty<double>())));
    }

    [Fact]
    public void ClusterBootstrap_NoReplicates_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChatConsistencyStatistics.ClusterBootstrap(Clusters(FourRuns), replicates: 0));
    }

    [Fact]
    public void ClusterBootstrapTwoSample_ConstantShift_EstimatesTheShift()
    {
        var baseline = Clusters(new[] { 1.0, 2.0, 3.0 }, new[] { 2.0, 3.0, 4.0 });
        var treatment = Clusters(new[] { 11.0, 12.0, 13.0 }, new[] { 12.0, 13.0, 14.0 });

        var result = ChatConsistencyStatistics.ClusterBootstrapTwoSample(baseline, treatment, replicates: 2000)!;

        Assert.Equal(10.0, result.Estimate, 12);
        Assert.Equal(new[] { 2, 2 }, result.ClusterCounts);
        Assert.InRange(10.0, result.Interval95.Lower, result.Interval95.Upper);
    }

    // --- Strata ---------------------------------------------------------------------------------

    [Fact]
    public void AssignStratum_FixtureDates_HaveTheExpectedWeekdays()
    {
        Assert.Equal(DayOfWeek.Wednesday, new DateTime(2026, 10, 7).DayOfWeek);
        Assert.Equal(DayOfWeek.Friday, new DateTime(2026, 10, 9).DayOfWeek);
        Assert.Equal(DayOfWeek.Saturday, new DateTime(2026, 10, 10).DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, new DateTime(2026, 10, 11).DayOfWeek);
        Assert.Equal(DayOfWeek.Monday, new DateTime(2026, 10, 12).DayOfWeek);
    }

    [Theory]
    [InlineData(7, 0, 0, 0, "Weekday 00–04 UTC")]
    [InlineData(7, 3, 59, 0, "Weekday 00–04 UTC")]
    [InlineData(7, 4, 0, 1, "Weekday 04–08 UTC")]
    [InlineData(7, 8, 0, 2, "Weekday 08–12 UTC")]
    [InlineData(7, 23, 59, 5, "Weekday 20–24 UTC")]
    [InlineData(9, 23, 59, 5, "Weekday 20–24 UTC")]
    [InlineData(10, 0, 0, 6, "Weekend 00–04 UTC")]
    [InlineData(10, 8, 0, 8, "Weekend 08–12 UTC")]
    [InlineData(11, 23, 59, 11, "Weekend 20–24 UTC")]
    [InlineData(12, 0, 0, 0, "Weekday 00–04 UTC")]
    public void AssignStratum_BlockEdgesAndWeekends(int day, int hour, int minute, int expectedIndex, string expectedLabel)
    {
        var stratum = ChatConsistencyStatistics.AssignStratum(new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Utc));

        Assert.Equal(expectedIndex, stratum.Index);
        Assert.Equal(expectedLabel, stratum.Label);
    }

    [Fact]
    public void AssignStratum_UnspecifiedKind_IsReadAsUtc()
    {
        var stratum = ChatConsistencyStatistics.AssignStratum(new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Unspecified));

        Assert.Equal(9, stratum.Index);
    }

    [Fact]
    public void AssignStratum_LocalKind_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ChatConsistencyStatistics.AssignStratum(new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void StratumLabel_OutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatConsistencyStatistics.StratumLabel(12));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatConsistencyStatistics.StratumLabel(-1));
    }

    [Fact]
    public void StratifiedShift_UsesCommonSupportOnly_AndReportsExcludedShare()
    {
        var baseline = new[]
        {
            new StratifiedObservation(0, 1.0), new StratifiedObservation(0, 2.0),
            new StratifiedObservation(1, 3.0)
        };
        var treatment = new[]
        {
            new StratifiedObservation(0, 2.0), new StratifiedObservation(0, 3.0),
            new StratifiedObservation(2, 5.0), new StratifiedObservation(2, 6.0)
        };

        var result = ChatConsistencyStatistics.StratifiedShift(baseline, treatment, BootstrapStatistic.Mean);

        // Stratum 1 (one baseline observation) and stratum 2 (two treatment observations) are excluded.
        Assert.Equal(new[] { 0 }, result.StrataUsed);
        Assert.Equal(1.0, result.Shift!.Value, 12);
        Assert.Equal(7, result.TotalObservationCount);
        Assert.Equal(3, result.ExcludedObservationCount);
        Assert.Equal(3.0 / 7.0, result.ExcludedShare, 12);
    }

    [Fact]
    public void StratifiedShift_WeightsStrataEqually_NotByObservationCount()
    {
        var baseline = new[]
        {
            new StratifiedObservation(0, 0.0), new StratifiedObservation(0, 0.0),
            new StratifiedObservation(0, 0.0), new StratifiedObservation(0, 0.0),
            new StratifiedObservation(1, 0.0)
        };
        var treatment = new[]
        {
            new StratifiedObservation(0, 1.0), new StratifiedObservation(0, 1.0),
            new StratifiedObservation(0, 1.0), new StratifiedObservation(0, 1.0),
            new StratifiedObservation(1, 3.0)
        };

        var result = ChatConsistencyStatistics.StratifiedShift(baseline, treatment, BootstrapStatistic.Mean);

        // (1 + 3) / 2, where observation weighting would give 1.4.
        Assert.Equal(2.0, result.Shift!.Value, 12);
        Assert.Equal(0.0, result.ExcludedShare, 12);
    }

    [Fact]
    public void StratifiedShift_NoCommonStratum_HasNullShift()
    {
        var result = ChatConsistencyStatistics.StratifiedShift(
            new[] { new StratifiedObservation(0, 1.0) },
            new[] { new StratifiedObservation(1, 2.0) });

        Assert.Null(result.Shift);
        Assert.Empty(result.StrataUsed);
        Assert.Equal(1.0, result.ExcludedShare, 12);
    }

    // --- Quantiles and robust slopes ------------------------------------------------------------

    [Fact]
    public void HarrellDavisQuantile_MedianOfOneToNine_IsFive()
    {
        var sample = Enumerable.Range(1, 9).Select(i => (double)i).ToArray();

        Assert.Equal(5.0, ChatConsistencyStatistics.HarrellDavisQuantile(sample, 0.5)!.Value, 9);
    }

    [Fact]
    public void HarrellDavisQuantile_SingleValue_IsThatValue()
    {
        Assert.Equal(4.2, ChatConsistencyStatistics.HarrellDavisQuantile(new[] { 4.2 }, 0.3)!.Value, 12);
    }

    [Fact]
    public void HarrellDavisQuantile_ProbabilityOutsideOpenInterval_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatConsistencyStatistics.HarrellDavisQuantile(new[] { 1.0, 2.0 }, 1.0));
    }

    [Fact]
    public void ShiftFunction_ConstantShift_GivesThatShiftAtEveryDecile()
    {
        var x = Enumerable.Range(1, 20).Select(i => (double)i).ToArray();
        var y = x.Select(v => v + 3.0).ToArray();

        var deciles = ChatConsistencyStatistics.ShiftFunction(x, y, replicates: 2000)!;

        Assert.Equal(9, deciles.Count);
        for (int d = 0; d < 9; d++)
        {
            Assert.Equal((d + 1) / 10.0, deciles[d].Probability, 12);
            Assert.Equal(3.0, deciles[d].Difference, 9);
            Assert.Equal(deciles[d].QuantileY - deciles[d].QuantileX, deciles[d].Difference, 12);
            Assert.InRange(3.0, deciles[d].Lower, deciles[d].Upper);
        }
    }

    [Fact]
    public void TheilSenSlope_PerfectLine_IsItsSlope()
    {
        var xs = new[] { 0.0, 1.0, 2.0, 3.0, 4.0 };
        var ys = xs.Select(x => 2.0 * x + 1.0).ToArray();

        Assert.Equal(2.0, ChatConsistencyStatistics.TheilSenSlope(xs, ys)!.Value, 12);
    }

    [Fact]
    public void TheilSenSlope_IgnoresOneOutlier()
    {
        // Six of the ten pairwise slopes are exactly 2, so the median is 2.
        var xs = new[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
        var ys = new[] { 3.0, 5.0, 7.0, 9.0, 100.0 };

        Assert.Equal(2.0, ChatConsistencyStatistics.TheilSenSlope(xs, ys)!.Value, 12);
    }

    [Fact]
    public void TheilSenSlope_NoDistinctX_IsNull()
    {
        Assert.Null(ChatConsistencyStatistics.TheilSenSlope(new[] { 1.0, 1.0 }, new[] { 2.0, 3.0 }));
    }

    // --- Equivalence and verdicts ---------------------------------------------------------------

    [Theory]
    [InlineData(-0.99, 0.99, true)]
    [InlineData(-1.0, 0.5, false)]
    [InlineData(-0.5, 1.0, false)]
    [InlineData(-2.0, 0.5, false)]
    public void Tost_IsEquivalentOnlyStrictlyInsideTheMargin(double low, double high, bool expected)
    {
        Assert.Equal(expected, ChatConsistencyStatistics.Tost(low, high, 1.0));
    }

    [Theory]
    // Significant and wholly beyond the margin: direction decides the label.
    [InlineData(0.01, 2.0, 4.0, 2.2, 3.8, true, ConsistencyVerdict.ChangedImproved)]
    [InlineData(0.01, 2.0, 4.0, 2.2, 3.8, false, ConsistencyVerdict.ChangedDegraded)]
    [InlineData(0.01, -4.0, -2.0, -3.8, -2.2, true, ConsistencyVerdict.ChangedDegraded)]
    [InlineData(0.01, -4.0, -2.0, -3.8, -2.2, false, ConsistencyVerdict.ChangedImproved)]
    // Beyond the margin but not significant after Holm.
    [InlineData(0.20, 2.0, 4.0, 2.2, 3.8, true, ConsistencyVerdict.Inconclusive)]
    // Excludes zero, inside the margin.
    [InlineData(0.01, 0.2, 0.8, 0.3, 0.7, true, ConsistencyVerdict.ChangedNegligible)]
    [InlineData(0.01, -0.8, -0.2, -0.7, -0.3, false, ConsistencyVerdict.ChangedNegligible)]
    // Includes zero; equivalence read from the 90 % interval.
    [InlineData(0.50, -0.9, 0.9, -0.7, 0.7, true, ConsistencyVerdict.Equivalent)]
    [InlineData(0.50, -1.5, 1.5, -0.9, 0.9, true, ConsistencyVerdict.Equivalent)]
    [InlineData(0.50, -2.0, 2.0, -1.5, 1.5, true, ConsistencyVerdict.Inconclusive)]
    // Excludes zero and straddles the margin.
    [InlineData(0.01, 0.5, 3.0, 0.7, 2.8, true, ConsistencyVerdict.Inconclusive)]
    public void Verdict_TruthTable(
        double p, double l95, double h95, double l90, double h90, bool higherIsBetter, ConsistencyVerdict expected)
    {
        Assert.Equal(expected, ChatConsistencyStatistics.Verdict(p, l95, h95, l90, h90, 1.0, higherIsBetter));
    }

    [Fact]
    public void Verdict_NonPositiveMargin_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChatConsistencyStatistics.Verdict(0.01, 1.0, 2.0, 1.1, 1.9, 0.0, true));
    }

    [Fact]
    public void Verdict_FromDistribution_ReadsBothIntervals()
    {
        var runs = Clusters(new[] { 1.0, 2.0, 3.0 }, new[] { 1.0, 2.0, 3.0 });
        var distribution = ChatConsistencyStatistics.ClusterBootstrap(runs, resampleItems: false, replicates: 200)!;

        // Every replicate is 2, so both intervals are [2, 2]: significant and beyond a margin of 1.
        Assert.Equal(ConsistencyVerdict.ChangedImproved, ChatConsistencyStatistics.Verdict(0.001, distribution, 1.0, true));
        Assert.Equal(ConsistencyVerdict.ChangedNegligible, ChatConsistencyStatistics.Verdict(0.001, distribution, 3.0, true));
    }

    // --- Proportions and tests ------------------------------------------------------------------

    [Fact]
    public void FisherExactTwoSided_TextbookTable()
    {
        // [[1, 9], [11, 3]]: the tables at least as extreme are x = 0, 1, 9, 10 with
        // hypergeometric weights 91, 3640, 3640, 91 out of C(24, 12) = 2704156.
        double p = ChatConsistencyStatistics.FisherExactTwoSided(1, 9, 11, 3);

        Assert.Equal(7462.0 / 2704156.0, p, 9);
        Assert.InRange(p, 0.00275, 0.00277);
    }

    [Fact]
    public void FisherExactTwoSided_MirroredTable_HasTheSamePValue()
    {
        Assert.Equal(
            ChatConsistencyStatistics.FisherExactTwoSided(1, 9, 11, 3),
            ChatConsistencyStatistics.FisherExactTwoSided(9, 1, 3, 11),
            12);
    }

    [Fact]
    public void FisherExactTwoSided_BalancedTable_IsOne()
    {
        Assert.Equal(1.0, ChatConsistencyStatistics.FisherExactTwoSided(5, 5, 5, 5), 9);
        Assert.Equal(1.0, ChatConsistencyStatistics.FisherExactTwoSided(0, 0, 0, 0), 12);
    }

    [Fact]
    public void FisherExactTwoSided_NegativeCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatConsistencyStatistics.FisherExactTwoSided(-1, 2, 3, 4));
    }

    [Fact]
    public void Wilson95_ReusesTheBenchmarkInterval()
    {
        Assert.Equal(BenchmarkProportionInterval.Wilson95(5, 10), ChatConsistencyStatistics.Wilson95(5, 10));
        Assert.Equal(BenchmarkProportionInterval.Wilson95(0, 7), ChatConsistencyStatistics.Wilson95(0, 7));
        Assert.Null(ChatConsistencyStatistics.Wilson95(0, 0));
    }

    [Fact]
    public void Holm_ReusesTheBatteryAdjustment()
    {
        // Sorted 0.01, 0.03, 0.04 -> 3 * 0.01, max(0.03, 2 * 0.03), max(0.06, 1 * 0.04).
        var adjusted = ChatConsistencyStatistics.Holm(new[] { 0.01, 0.04, 0.03 });

        Assert.Equal(0.03, adjusted[0], 12);
        Assert.Equal(0.06, adjusted[1], 12);
        Assert.Equal(0.06, adjusted[2], 12);
    }

    [Fact]
    public void BenjaminiHochberg_ReusesTheGroupProcedure()
    {
        var p = new[] { 0.001, 0.02, 0.04, 0.3 };

        Assert.Equal(
            BenchmarkGroupStatistics.BenjaminiHochberg(p).Select(r => r.AdjustedPValue),
            ChatConsistencyStatistics.BenjaminiHochberg(p).Select(r => r.AdjustedPValue));
    }

    // --- Difference in differences --------------------------------------------------------------

    private static DifferenceInDifferencesArm TargetArm() => new(
        Before: new[] { Run(("q1", 50), ("q2", 60), ("q3", 70)), Run(("q1", 52), ("q2", 58)) },
        After: new[] { Run(("q1", 70), ("q2", 80)), Run(("q1", 72), ("q2", 78)) });

    private static DifferenceInDifferencesArm ControlArm() => new(
        Before: new[] { Run(("q1", 50), ("q2", 60)), Run(("q1", 50), ("q2", 60)) },
        After: new[] { Run(("q1", 55), ("q2", 65)), Run(("q1", 55), ("q2", 65)) });

    [Fact]
    public void ItemDifferenceInDifferences_TargetGainsMoreThanControl_IsPositive()
    {
        // Target +20 on each item, control +5: 15 per item. q3 is missing from three cells.
        var result = ChatConsistencyStatistics.ItemDifferenceInDifferences(TargetArm(), ControlArm(), replicates: 2000)!;

        Assert.Equal(2, result.ItemCount);
        Assert.Equal(new[] { "q1", "q2" }, result.Items.Select(i => i.ItemKey));
        Assert.All(result.Items, i =>
        {
            Assert.Equal(20.0, i.TargetChange, 12);
            Assert.Equal(5.0, i.ControlChange, 12);
            Assert.Equal(15.0, i.Difference, 12);
        });
        Assert.Equal(15.0, result.Distribution.Estimate, 12);
        Assert.Equal(new[] { 2, 2, 2, 2 }, result.Distribution.ClusterCounts);
        Assert.True(result.Distribution.Interval95.Lower > 0.0);
        Assert.InRange(15.0, result.Distribution.Interval95.Lower, result.Distribution.Interval95.Upper);
    }

    [Fact]
    public void ItemDifferenceInDifferences_SwappedArms_FlipsTheSign()
    {
        var result = ChatConsistencyStatistics.ItemDifferenceInDifferences(ControlArm(), TargetArm(), replicates: 2000)!;

        Assert.Equal(-15.0, result.Distribution.Estimate, 12);
        Assert.True(result.Distribution.Interval95.Upper < 0.0);
    }

    [Fact]
    public void ItemDifferenceInDifferences_FixedSeed_IsDeterministic()
    {
        var first = ChatConsistencyStatistics.ItemDifferenceInDifferences(TargetArm(), ControlArm(), replicates: 1000, seed: 3)!;
        var second = ChatConsistencyStatistics.ItemDifferenceInDifferences(TargetArm(), ControlArm(), replicates: 1000, seed: 3)!;

        Assert.Equal(first.Distribution.Replicates, second.Distribution.Replicates);
    }

    [Fact]
    public void ItemDifferenceInDifferences_EmptyCell_IsNull()
    {
        var control = new DifferenceInDifferencesArm(
            Before: Array.Empty<IReadOnlyDictionary<string, double>>(),
            After: new[] { Run(("q1", 55)) });

        Assert.Null(ChatConsistencyStatistics.ItemDifferenceInDifferences(TargetArm(), control));
    }

    // --- Change points --------------------------------------------------------------------------

    [Fact]
    public void Pelt_ObviousStep_IsFound()
    {
        var series = FlatPattern(60).Select((v, i) => i >= 30 ? v + 5.0 : v).ToArray();

        Assert.Equal(new[] { 30 }, ChatConsistencyStatistics.Pelt(series));
    }

    [Fact]
    public void Pelt_FlatSeries_HasNoChangePoint()
    {
        Assert.Empty(ChatConsistencyStatistics.Pelt(FlatPattern(60)));
    }

    [Fact]
    public void Pelt_TwoSteps_AreBothFound()
    {
        var series = FlatPattern(90).Select((v, i) => v + (i >= 60 ? -4.0 : i >= 30 ? 5.0 : 0.0)).ToArray();

        Assert.Equal(new[] { 30, 60 }, ChatConsistencyStatistics.Pelt(series));
    }

    [Fact]
    public void Pelt_ShortOrConstantSeries_HasNoChangePoint()
    {
        Assert.Empty(ChatConsistencyStatistics.Pelt(new[] { 1.0, 9.0, 1.0 }));
        Assert.Empty(ChatConsistencyStatistics.Pelt(Enumerable.Repeat(3.0, 20).ToArray()));
    }

    // --- Power and design -----------------------------------------------------------------------

    [Fact]
    public void MinimumDetectableEffect_Formula()
    {
        // (1.959964 + 0.841621) * sqrt(2) * 10 / sqrt(4) = 19.8102.
        double mde = ChatConsistencyStatistics.MinimumDetectableEffect(10.0, 4, 4);

        Assert.Equal(19.8102, mde, 3);
    }

    [Fact]
    public void MinimumDetectableEffect_TwoRunsPerPeriod_UsesRunToRunNoise()
    {
        // Run means 2, 4 and 3, 5: each variance 2, pooled SD sqrt(2), two runs a side.
        var result = ChatConsistencyStatistics.MinimumDetectableEffect(
            Clusters(new[] { 1.0, 2.0, 3.0 }, new[] { 3.0, 4.0, 5.0 }),
            Clusters(new[] { 2.0, 3.0, 4.0 }, new[] { 4.0, 5.0, 6.0 }))!;

        Assert.False(result.CapNote);
        Assert.Null(result.Note);
        Assert.Equal(2, result.BaselineCount);
        Assert.Equal(2, result.TreatmentCount);
        Assert.Equal(Math.Sqrt(2.0), result.StandardDeviation, 12);
        Assert.Equal(3.9620, result.Effect, 3);
    }

    [Fact]
    public void MinimumDetectableEffect_OneRunPerPeriod_FallsBackToItemNoiseWithCapNote()
    {
        // Item variances 1 and 1, three items a side: 2.801585 * sqrt(2/3) = 2.287485.
        var result = ChatConsistencyStatistics.MinimumDetectableEffect(
            Clusters(new[] { 1.0, 2.0, 3.0 }),
            Clusters(new[] { 2.0, 3.0, 4.0 }))!;

        Assert.True(result.CapNote);
        Assert.Equal(MinimumDetectableEffectResult.OneRunPerPeriodNote, result.Note);
        Assert.Equal(3, result.BaselineCount);
        Assert.Equal(1.0, result.StandardDeviation, 12);
        Assert.Equal(2.28748, result.Effect, 4);
    }

    [Fact]
    public void DesignEffect_IsKishFormula()
    {
        Assert.Equal(1.9, ChatConsistencyStatistics.DesignEffect(10, 0.1), 12);
        Assert.Equal(1.0, ChatConsistencyStatistics.DesignEffect(1, 0.7), 12);
    }

    [Fact]
    public void FlipRate_IsShareOfChangedCorrectness()
    {
        var pairs = new[] { (true, true), (true, false), (false, true), (false, false) };

        Assert.Equal(0.5, ChatConsistencyStatistics.FlipRate(pairs)!.Value, 12);
        Assert.Equal(0.5, ChatConsistencyStatistics.NullFlipRate(pairs)!.Value, 12);
        Assert.Null(ChatConsistencyStatistics.FlipRate(Array.Empty<(bool, bool)>()));
    }

    // --- Distributions --------------------------------------------------------------------------

    [Fact]
    public void NormalQuantile_KnownValues()
    {
        Assert.Equal(0.0, ChatConsistencyStatistics.NormalQuantile(0.5), 12);
        Assert.Equal(1.959964, ChatConsistencyStatistics.NormalQuantile(0.975), 6);
        Assert.Equal(0.841621, ChatConsistencyStatistics.NormalQuantile(0.8), 6);
        Assert.Equal(-2.326348, ChatConsistencyStatistics.NormalQuantile(0.01), 6);
        Assert.Equal(-ChatConsistencyStatistics.NormalQuantile(0.99), ChatConsistencyStatistics.NormalQuantile(0.01), 9);
        Assert.Equal(double.NegativeInfinity, ChatConsistencyStatistics.NormalQuantile(0.0));
        Assert.Equal(double.PositiveInfinity, ChatConsistencyStatistics.NormalQuantile(1.0));
    }
}
