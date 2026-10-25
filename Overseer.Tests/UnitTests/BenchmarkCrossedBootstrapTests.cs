namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The pigeonhole bootstrap over a crossed run × question array. Each test builds a small array by
/// hand whose answer follows from its construction: no spread, an identical pair, a constant shift.
/// </summary>
public class BenchmarkCrossedBootstrapTests
{
    private static readonly long[] BaselineRuns = { 1, 2, 3 };
    private static readonly long[] TreatmentRuns = { 4, 5, 6 };

    /// <summary>
    /// One <see cref="CrossedItem"/> per column of <paramref name="scoresByRun"/>, whose rows follow
    /// <paramref name="runIds"/>. A NaN cell is a question that run skipped.
    /// </summary>
    private static List<CrossedItem> Items(double[] weights, long[] runIds, double[][] scoresByRun)
        => weights.Select((w, q) => new CrossedItem(w, Column(runIds, scoresByRun, q))).ToList();

    private static List<CrossedPair> Pairs(double[] weights, double[][] baseline, double[][] treatment)
        => weights
            .Select((w, q) => new CrossedPair(w, Column(BaselineRuns, baseline, q), Column(TreatmentRuns, treatment, q)))
            .ToList();

    private static IReadOnlyDictionary<long, double> Column(long[] runIds, double[][] scoresByRun, int q)
    {
        var map = new Dictionary<long, double>();
        for (int r = 0; r < runIds.Length; r++)
        {
            if (!double.IsNaN(scoresByRun[r][q])) map[runIds[r]] = scoresByRun[r][q];
        }

        return map;
    }

    private static double[][] Shift(double[][] scores, double by)
        => scores.Select(row => row.Select(s => s + by).ToArray()).ToArray();

    private static readonly double[] Weights = { 20, 50, 80, 35 };

    private static readonly double[][] SpreadScores =
    {
        new double[] { 60, 70, 80, 40 },
        new double[] { 72, 65, 90, 55 },
        new double[] { 55, 85, 75, 50 }
    };

    [Fact]
    public void AFixedSeed_GivesIdenticalIntervalsTwice()
    {
        var items = Items(Weights, BaselineRuns, SpreadScores);

        var first = BenchmarkCrossedBootstrap.IndexDistribution(items, BaselineRuns, resampleRuns: true, replicates: 2000, seed: 7)!;
        var second = BenchmarkCrossedBootstrap.IndexDistribution(items, BaselineRuns, resampleRuns: true, replicates: 2000, seed: 7)!;

        Assert.Equal(first.Interval95, second.Interval95);
        Assert.Equal(first.Replicates, second.Replicates);
        Assert.Equal(first.Estimate, second.Estimate);
    }

    [Fact]
    public void TheDefaults_AreTheDocumentedReplicatesAndSeed()
    {
        var items = Items(Weights, BaselineRuns, SpreadScores);

        var defaults = BenchmarkCrossedBootstrap.IndexDistribution(items, BaselineRuns, resampleRuns: true)!;
        var explicitly = BenchmarkCrossedBootstrap.IndexDistribution(
            items, BaselineRuns, resampleRuns: true, BenchmarkCrossedBootstrap.DefaultReplicates, BenchmarkCrossedBootstrap.DefaultSeed)!;

        Assert.Equal(10_000, defaults.RequestedReplicates);
        Assert.Equal(explicitly.Interval95, defaults.Interval95);
    }

    [Fact]
    public void ZeroRunToRunSpread_GivesTheSameIntervalWithAndWithoutRunResampling()
    {
        // Every run scored every question identically, so whichever runs a replicate draws, their
        // mean index is the one index of the drawn questions. The question draws come from their own
        // stream, so the two settings see the same question draws.
        var identical = new[] { SpreadScores[0], SpreadScores[0], SpreadScores[0] };
        var items = Items(Weights, BaselineRuns, identical);

        var withRuns = BenchmarkCrossedBootstrap.IndexDistribution(items, BaselineRuns, resampleRuns: true, replicates: 2000, seed: 11)!;
        var questionsOnly = BenchmarkCrossedBootstrap.IndexDistribution(items, BaselineRuns, resampleRuns: false, replicates: 2000, seed: 11)!;

        Assert.Equal(questionsOnly.Interval95.Lower, withRuns.Interval95.Lower, 9);
        Assert.Equal(questionsOnly.Interval95.Upper, withRuns.Interval95.Upper, 9);
        Assert.True(withRuns.Interval95.Upper > withRuns.Interval95.Lower, "Question sampling alone still has spread.");
    }

    [Fact]
    public void TwoIdenticalGroups_GiveADifferenceIntervalCoveringZero_AndAPNearOne()
    {
        var pairs = Pairs(Weights, SpreadScores, SpreadScores);

        var distribution = BenchmarkCrossedBootstrap.DifferenceDistribution(pairs, BaselineRuns, TreatmentRuns, resampleRuns: true)!;

        Assert.Equal(0.0, distribution.Estimate, 12);
        var (lower, upper) = distribution.Interval95;
        Assert.True(lower <= 0.0 && upper >= 0.0, $"[{lower}, {upper}] should cover 0.");

        // The difference of two exchangeable groups is symmetric about 0, so each tail share is about
        // a half and the two-sided bootstrap p-value is close to 1.
        var replicates = distribution.Replicates;
        double atOrBelow = replicates.Count(v => v <= 0.0) / (double)replicates.Count;
        double atOrAbove = replicates.Count(v => v >= 0.0) / (double)replicates.Count;
        double p = Math.Min(1.0, 2.0 * Math.Min(atOrBelow, atOrAbove));
        Assert.True(p > 0.9, $"p = {p}");
    }

    [Fact]
    public void AConstantShiftOnEveryScore_GivesThatDifference_AndAnIntervalExcludingZero()
    {
        // Run offsets of 0, 2 and 4 points on every question: whatever questions are drawn, a run's
        // index is the drawn questions' index plus its offset, so the difference stays within 10 ± 4.
        double[][] baseline =
        {
            new double[] { 60, 70, 80, 40 },
            new double[] { 62, 72, 82, 42 },
            new double[] { 64, 74, 84, 44 }
        };
        var pairs = Pairs(Weights, baseline, Shift(baseline, 10.0));

        var distribution = BenchmarkCrossedBootstrap.DifferenceDistribution(pairs, BaselineRuns, TreatmentRuns, resampleRuns: true)!;

        Assert.Equal(10.0, distribution.Estimate, 9);
        var (lower, upper) = distribution.Interval95;
        Assert.True(lower > 0.0, $"Lower bound {lower} should exclude 0.");
        Assert.True(lower >= 6.0 - 1e-9 && upper <= 14.0 + 1e-9, $"[{lower}, {upper}] should lie within 10 ± 4.");
    }

    [Fact]
    public void ARaggedArray_YieldsADefinedInterval()
    {
        // Run 3 skipped the second question and run 2 the fourth.
        double[][] ragged =
        {
            new double[] { 60, 70, 80, 40 },
            new double[] { 72, 65, 90, double.NaN },
            new double[] { 55, double.NaN, 75, 50 }
        };
        var items = Items(Weights, BaselineRuns, ragged);

        var distribution = BenchmarkCrossedBootstrap.IndexDistribution(items, BaselineRuns, resampleRuns: true, replicates: 2000, seed: 3)!;

        // The point estimate is the mean of the three runs' indices over the questions each answered.
        double run1 = (20 * 60 + 50 * 70 + 80 * 80 + 35 * 40) / 185.0;
        double run2 = (20 * 72 + 50 * 65 + 80 * 90) / 150.0;
        double run3 = (20 * 55 + 80 * 75 + 35 * 50) / 135.0;
        Assert.Equal((run1 + run2 + run3) / 3.0, distribution.Estimate, 9);

        Assert.NotEmpty(distribution.Replicates);
        var (lower, upper) = distribution.Interval95;
        Assert.True(double.IsFinite(lower) && double.IsFinite(upper));
        Assert.True(lower <= upper);
    }

    [Fact]
    public void ARunWithNoScoreAtAll_IsNotACluster()
    {
        var items = Items(Weights, BaselineRuns, SpreadScores);

        var distribution = BenchmarkCrossedBootstrap.IndexDistribution(items, new long[] { 1, 2, 3, 99 }, resampleRuns: true, replicates: 500, seed: 5)!;

        Assert.Equal(new[] { 3 }, distribution.ClusterCounts);
    }

    [Fact]
    public void EveryBound_LiesInTheScoreRange()
    {
        double[][] extremes =
        {
            new double[] { 0, 100, 100, 0 },
            new double[] { 100, 0, 100, 100 },
            new double[] { 0, 0, 100, 0 }
        };
        var items = Items(Weights, BaselineRuns, extremes);

        foreach (bool resampleRuns in new[] { true, false })
        {
            var distribution = BenchmarkCrossedBootstrap.IndexDistribution(items, BaselineRuns, resampleRuns, replicates: 2000, seed: 13)!;

            Assert.All(distribution.Replicates, v => Assert.InRange(v, 0.0, 100.0));
            var (lower, upper) = distribution.Interval95;
            Assert.InRange(lower, 0.0, 100.0);
            Assert.InRange(upper, 0.0, 100.0);
        }
    }

    [Fact]
    public void ThePairedDraw_UsesOneQuestionDrawForBothGroups_ByConstruction()
    {
        // Treatment is baseline plus 10 in every cell, the questions differ widely, and runs are not
        // redrawn. With one question draw for both groups every replicate is exactly 10; independent
        // draws would spread it across tens of points.
        var pairs = Pairs(Weights, SpreadScores, Shift(SpreadScores, 10.0));

        var distribution = BenchmarkCrossedBootstrap.DifferenceDistribution(pairs, BaselineRuns, TreatmentRuns, resampleRuns: false, replicates: 2000, seed: 17)!;

        Assert.All(distribution.Replicates, v => Assert.Equal(10.0, v, 9));
    }

    [Fact]
    public void ThePairedDraw_UsesOneQuestionDrawForBothGroups_AsObserved()
    {
        var treatment = new[]
        {
            new double[] { 65, 80, 85, 45 },
            new double[] { 75, 82, 95, 60 },
            new double[] { 70, 88, 99, double.NaN }
        };
        var pairs = Pairs(Weights, SpreadScores, treatment);

        var recomputed = new List<double>();
        int calls = 0;
        var distribution = BenchmarkCrossedBootstrap.DifferenceDistribution(
            pairs, BaselineRuns, TreatmentRuns, resampleRuns: true, replicates: 1000, seed: 19,
            drawObserver: (questions, baselineRuns, treatmentRuns) =>
            {
                calls++;
                Assert.Equal(Weights.Length, questions.Length);
                Assert.All(baselineRuns, id => Assert.Contains(id, BaselineRuns));
                Assert.All(treatmentRuns, id => Assert.Contains(id, TreatmentRuns));

                // The one observed question draw, applied to both groups, reproduces the replicate.
                double t = Index(pairs.Select(p => (p.Weight, p.TreatmentScoreByRun)).ToList(), questions, treatmentRuns);
                double b = Index(pairs.Select(p => (p.Weight, p.BaselineScoreByRun)).ToList(), questions, baselineRuns);
                recomputed.Add(t - b);
            })!;

        Assert.Equal(1000, calls);
        var expected = recomputed.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToList();
        Assert.Equal(expected.Count, distribution.Replicates.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], distribution.Replicates[i], 9);
        }
    }

    [Fact]
    public void InvalidArguments_AreRefused()
    {
        var items = Items(Weights, BaselineRuns, SpreadScores);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BenchmarkCrossedBootstrap.IndexDistribution(items, BaselineRuns, resampleRuns: true, replicates: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BenchmarkCrossedBootstrap.IndexDistribution(
                new[] { new CrossedItem(0.0, new Dictionary<long, double> { [1] = 50 }) }, BaselineRuns, resampleRuns: true));
        Assert.Null(BenchmarkCrossedBootstrap.IndexDistribution(Array.Empty<CrossedItem>(), BaselineRuns, resampleRuns: true));
    }

    /// <summary>
    /// The statistic written out for a drawn question list and a drawn run list: the mean over the
    /// drawn runs of each run's weighted index over the drawn questions it answered.
    /// </summary>
    private static double Index(
        IReadOnlyList<(double Weight, IReadOnlyDictionary<long, double> ScoreByRun)> items,
        int[] questions,
        long[] runs)
    {
        double sum = 0.0;
        int count = 0;
        foreach (long run in runs)
        {
            double weighted = 0.0;
            double weights = 0.0;
            foreach (int q in questions)
            {
                if (!items[q].ScoreByRun.TryGetValue(run, out double score)) continue;
                weighted += items[q].Weight * score;
                weights += items[q].Weight;
            }

            if (weights <= 0.0) continue;
            sum += weighted / weights;
            count++;
        }

        return count > 0 ? sum / count : double.NaN;
    }
}
