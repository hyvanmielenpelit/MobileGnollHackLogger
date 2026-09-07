namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The scientific core. Every figure here is arithmetic over a hand-built fixture, and the
/// expected values are either derived in the test's own comment or taken from a published example,
/// so a regression shows up as a wrong number rather than as a plausible one.
/// </summary>
public class BenchmarkGroupStatisticsTests
{
    private static BenchmarkSuite Suite() => new() { Id = 5, Name = "GnollHack Player Assistance Benchmark Suite" };

    private static BenchmarkQuestion[] Questions(params int[] assessedDifficulties)
    {
        return assessedDifficulties
            .Select((d, i) => new BenchmarkQuestion
            {
                Id = i + 1,
                BenchmarkSuiteId = 5,
                OrderIndex = i + 1,
                QuestionText = $"Q{i + 1}",
                Difficulty = BenchmarkDifficulty.Intermediate,
                AssessedDifficulty = d,
                ItemRevision = 1
            })
            .ToArray();
    }

    private static BenchmarkRun Run(
        long runId,
        BenchmarkQuestion[] questions,
        int[] scores,
        int? speedIndex = 80,
        bool[]? criticalErrors = null,
        long[]? modelTimesMs = null)
    {
        var run = new BenchmarkRun
        {
            Id = runId,
            BenchmarkSuiteId = 5,
            SuiteName = "GnollHack Player Assistance Benchmark Suite",
            TestedModelIdUsed = "gpt-5.6-luna",
            AssessorModelIdUsed = "gemini-3.7-pro",
            ScoringMethodVersion = 8,
            SpeedIndex = speedIndex
        };

        for (int i = 0; i < questions.Length; i++)
        {
            run.Answers.Add(new BenchmarkRunAnswer
            {
                Id = runId * 100 + i,
                BenchmarkRunId = runId,
                BenchmarkQuestionId = questions[i].Id,
                ItemRevisionUsed = 1,
                OrderIndex = i + 1,
                QuestionText = questions[i].QuestionText,
                Status = BenchmarkAnswerStatus.Ok,
                AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                QualityScore = scores[i],
                AssessedDifficulty = questions[i].AssessedDifficulty,
                CriticalError = criticalErrors?[i] ?? false,
                DurationMs = modelTimesMs?[i] ?? 30000,
                ToolTimeMs = 0
            });
        }

        return run;
    }

    // --- The identity the whole decomposition rests on -------------------------------------------

    [Fact]
    public void MultiRunIndex_EqualsBothTheMeanOfRunIndicesAndTheWeightedMeanOfItemMeans()
    {
        // Weights 20 / 50 / 80, total 150.
        //   run 1 = (20*60 + 50*70 + 80*80) / 150 = 11100 / 150 = 74
        //   run 2 = (20*70 + 50*80 + 80*90) / 150 = 12600 / 150 = 84
        //   run 3 = (20*80 + 50*90 + 80*100) / 150 = 14100 / 150 = 94
        //   mean of the three = 84
        // Item cross-run means are 70 / 80 / 90, and
        //   (20*70 + 50*80 + 80*90) / 150 = 12600 / 150 = 84
        // The two routes are the same number, which is what makes the two variance components
        // below components of one thing rather than two unrelated intervals.
        var questions = Questions(20, 50, 80);
        var runs = new[]
        {
            Run(1, questions, new[] { 60, 70, 80 }),
            Run(2, questions, new[] { 70, 80, 90 }),
            Run(3, questions, new[] { 80, 90, 100 })
        };

        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, runs);

        Assert.Equal(new[] { 74.0, 84.0, 94.0 }, result.Index.PerRunIndices.Select(v => Math.Round(v, 9)));
        Assert.Equal(84.0, result.Index.PointEstimate, 9);
        Assert.Equal(84.0, result.Index.WeightedMeanOfItemMeans, 9);
        Assert.True(result.Index.IdentityHolds);

        Assert.Equal(3, result.ItemCount);
        Assert.Equal(new[] { 70.0, 80.0, 90.0 }, result.Items.Select(i => i.Mean));
    }

    [Fact]
    public void ARaggedItemSet_BreaksTheIdentity_AndSaysSo()
    {
        // Run 3 never produced a scored answer to Q3. The two routes then weight the missing cell
        // differently, and the report has to say which number it is showing rather than imply both.
        var questions = Questions(20, 50, 80);
        var runs = new[]
        {
            Run(1, questions, new[] { 60, 70, 80 }),
            Run(2, questions, new[] { 70, 80, 90 }),
            Run(3, questions, new[] { 80, 90, 100 })
        };
        runs[2].Answers[2].Status = BenchmarkAnswerStatus.ProviderError;

        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, runs);

        Assert.False(result.Index.IdentityHolds);
        Assert.Equal(2, result.Items.Single(i => i.QuestionId == 3).RunCount);
    }

    // --- Variance decomposition -------------------------------------------------------------------

    [Fact]
    public void ReproducibilitySE_ShrinksAsRootR_WhileItemSamplingSE_DoesNot()
    {
        // Identical replication: the same three runs, then the same three runs four times over.
        // The underlying spread is unchanged by construction, so the population standard deviation
        // is identical and the standard error must fall exactly as sqrt(R).
        // The three score vectors give run indices of exactly 74, 84 and 94, and item means of
        // 70, 80 and 90 whichever multiple of the three is used.
        var questions = Questions(20, 50, 80);
        int[][] scoreSets = { new[] { 60, 70, 80 }, new[] { 70, 80, 90 }, new[] { 80, 90, 100 } };

        var three = scoreSets.Select((s, i) => Run(i + 1, questions, s)).ToArray();
        var twelve = Enumerable.Range(0, 12)
            .Select(i => Run(i + 1, questions, scoreSets[i % 3]))
            .ToArray();

        var small = BenchmarkGroupStatistics.Compute(Suite(), questions, three).Index;
        var large = BenchmarkGroupStatistics.Compute(Suite(), questions, twelve).Index;

        // The reported SE is SD / sqrt(R) in both.
        Assert.Equal(small.ReproducibilityStandardDeviation!.Value / Math.Sqrt(3), small.ReproducibilityStandardError!.Value, 9);
        Assert.Equal(large.ReproducibilityStandardDeviation!.Value / Math.Sqrt(12), large.ReproducibilityStandardError!.Value, 9);
        Assert.True(large.ReproducibilityStandardError!.Value < small.ReproducibilityStandardError!.Value);

        // Normalising out the n-1 correction, which is the only reason the raw ratio is not exactly
        // 2, quadrupling R halves the standard error.
        double population3 = small.ReproducibilityStandardDeviation!.Value * Math.Sqrt(2.0 / 3.0);
        double population12 = large.ReproducibilityStandardDeviation!.Value * Math.Sqrt(11.0 / 12.0);
        Assert.Equal(population3, population12, 9);
        Assert.Equal(2.0, (population3 / Math.Sqrt(3)) / (population12 / Math.Sqrt(12)), 9);

        // And the item-sampling component does not move at all: every run answered the same three
        // questions, so adding runs says nothing about which questions the suite drew.
        Assert.Equal(small.ItemSamplingStandardError!.Value, large.ItemSamplingStandardError!.Value, 12);
    }

    [Fact]
    public void ItemSamplingSE_IsUnchangedWhenOneRunIsReplicatedIdentically()
    {
        var questions = Questions(20, 50, 80);
        var one = new[] { Run(1, questions, new[] { 60, 80, 95 }) };
        var three = new[]
        {
            Run(1, questions, new[] { 60, 80, 95 }),
            Run(2, questions, new[] { 60, 80, 95 }),
            Run(3, questions, new[] { 60, 80, 95 })
        };

        var single = BenchmarkGroupStatistics.Compute(Suite(), questions, one).Index;
        var replicated = BenchmarkGroupStatistics.Compute(Suite(), questions, three).Index;

        Assert.Equal(single.ItemSamplingStandardError!.Value, replicated.ItemSamplingStandardError!.Value, 12);

        // Three identical runs have no reproducibility spread at all, which is the honest reading
        // of a replication that produced the same answer every time.
        Assert.Equal(0.0, replicated.ReproducibilityStandardDeviation!.Value, 12);
        Assert.Equal(replicated.ItemSamplingHalfWidth!.Value, replicated.CombinedHalfWidth!.Value, 9);
    }

    [Fact]
    public void ItemSamplingSE_IsTheExistingSingleRunEstimatorOverTheCrossRunMeans()
    {
        // Reuse, asserted: the same numbers put through BenchmarkScoring produce the same figure.
        var questions = Questions(20, 50, 80);
        var runs = new[]
        {
            Run(1, questions, new[] { 60, 70, 80 }),
            Run(2, questions, new[] { 70, 80, 90 }),
            Run(3, questions, new[] { 80, 90, 100 })
        };

        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, runs);

        var expected = BenchmarkScoring.QualityIndexStandardError(new (int?, int?)[]
        {
            (70, 20), (80, 50), (90, 80)
        });

        Assert.Equal(expected!.Value, result.Index.ItemSamplingStandardError!.Value, 12);
    }

    [Fact]
    public void TwoRuns_YieldNoReproducibilityFigure()
    {
        // Mirrors the n < 3 -> null convention in QualityIndexStandardError and
        // BenchmarkItemAnalysis.MinRunsForMeasurement. Two runs give a standard deviation that is
        // arithmetic rather than evidence.
        var questions = Questions(20, 50, 80);
        var runs = new[]
        {
            Run(1, questions, new[] { 60, 70, 80 }),
            Run(2, questions, new[] { 80, 90, 100 })
        };

        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, runs);

        Assert.Null(result.Index.ReproducibilityStandardDeviation);
        Assert.Null(result.Index.ReproducibilityStandardError);
        Assert.Null(result.Index.ReproducibilityHalfWidth);
        Assert.False(result.Index.ReproducibilityAvailable);
        Assert.False(result.PooledIndexReportable);

        // The interval is still reported — it just covers one source, and the flag above says so.
        Assert.Equal(result.Index.ItemSamplingHalfWidth!.Value, result.Index.CombinedHalfWidth!.Value, 9);
    }

    [Fact]
    public void CombinedInterval_AddsTheTwoComponentsInQuadrature()
    {
        var questions = Questions(20, 50, 80);
        var runs = new[]
        {
            Run(1, questions, new[] { 60, 70, 80 }),
            Run(2, questions, new[] { 70, 80, 90 }),
            Run(3, questions, new[] { 80, 90, 100 })
        };

        var index = BenchmarkGroupStatistics.Compute(Suite(), questions, runs).Index;

        double expected = Math.Sqrt(
            index.ReproducibilityHalfWidth!.Value * index.ReproducibilityHalfWidth!.Value +
            index.ItemSamplingHalfWidth!.Value * index.ItemSamplingHalfWidth!.Value);

        Assert.Equal(expected, index.CombinedHalfWidth!.Value, 9);

        // The half-width is the quantity in quadrature and is never clamped. The reported bounds
        // are, because a quality score cannot leave [0, 100] — three items give an interval wide
        // enough to reach both ends here, so both are pinned and the truncation is flagged.
        Assert.Equal(
            Math.Clamp(index.PointEstimate - expected, 0.0, 100.0), index.CombinedLower!.Value, 9);
        Assert.Equal(
            Math.Clamp(index.PointEstimate + expected, 0.0, 100.0), index.CombinedUpper!.Value, 9);
        Assert.True(index.CombinedIntervalTruncated);

        // SD of {74, 84, 94} is 10, so the reproducibility SE is 10 / sqrt(3) with t(2) = 4.3027.
        Assert.Equal(10.0, index.ReproducibilityStandardDeviation!.Value, 9);
        Assert.Equal(4.3027, index.ReproducibilityCriticalValue!.Value, 4);
    }

    // --- Per-item statistics ----------------------------------------------------------------------

    [Fact]
    public void PerItem_ReportsMeanMedianSampleSdIqrCvAndTheCriticalErrorRate()
    {
        // One item, scores 60 / 70 / 80 / 90.
        //   mean 75, median 75, sample SD = sqrt(500/3) = 12.9099, CV = 0.17213
        //   IQR by linear interpolation = 82.5 - 67.5 = 15
        //   critical errors in 1 of 4 runs = 0.25
        var questions = Questions(50);
        var runs = new[]
        {
            Run(1, questions, new[] { 60 }, criticalErrors: new[] { true }),
            Run(2, questions, new[] { 70 }),
            Run(3, questions, new[] { 80 }),
            Run(4, questions, new[] { 90 })
        };

        var item = Assert.Single(BenchmarkGroupStatistics.Compute(Suite(), questions, runs).Items);

        Assert.Equal(4, item.RunCount);
        Assert.Equal(75.0, item.Mean, 9);
        Assert.Equal(75.0, item.Median, 9);
        Assert.Equal(60, item.Min);
        Assert.Equal(90, item.Max);
        Assert.Equal(Math.Sqrt(500.0 / 3.0), item.StandardDeviation!.Value, 9);
        Assert.Equal(15.0, item.InterquartileRange!.Value, 9);
        Assert.Equal(item.StandardDeviation!.Value / 75.0, item.CoefficientOfVariation!.Value, 12);
        Assert.Equal(1, item.CriticalErrorCount);
        Assert.Equal(0.25, item.CriticalErrorRate, 9);

        // t(3) = 3.1824 on SD / sqrt(4).
        Assert.Equal(3.1824 * item.StandardDeviation!.Value / 2.0, item.MeanConfidenceHalfWidth!.Value, 3);

        // Reused rather than recomputed: the suite-health row travels with the group row.
        Assert.NotNull(item.Analysis);
        Assert.Equal(4, item.Analysis!.RunCount);
    }

    [Fact]
    public void UnstableItems_AreFlaggedAtTheConfiguredSpread()
    {
        var questions = Questions(50, 50);
        var runs = new[]
        {
            Run(1, questions, new[] { 30, 80 }),
            Run(2, questions, new[] { 60, 82 }),
            Run(3, questions, new[] { 95, 84 })
        };

        var options = new BenchmarkGroupStatisticsOptions { UnstableItemStandardDeviation = 15.0 };
        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, runs, options: options);

        Assert.Equal(new long[] { 1 }, result.UnstableQuestionIds);
        Assert.True(result.Items.Single(i => i.QuestionId == 1).Unstable);
        Assert.False(result.Items.Single(i => i.QuestionId == 2).Unstable);
    }

    [Fact]
    public void Speed_PoolsModelTimeAcrossEveryAnswerOfEveryRun()
    {
        var questions = Questions(50, 50);
        var runs = new[]
        {
            Run(1, questions, new[] { 80, 80 }, speedIndex: 70, modelTimesMs: new long[] { 10000, 20000 }),
            Run(2, questions, new[] { 80, 80 }, speedIndex: 90, modelTimesMs: new long[] { 30000, 40000 })
        };

        var speed = BenchmarkGroupStatistics.Compute(Suite(), questions, runs).Speed;

        Assert.Equal(80.0, speed.MeanSpeedIndex!.Value, 9);
        Assert.Equal(4, speed.PooledAnswerCount);

        // Pooled {10000, 20000, 30000, 40000}: P50 interpolates to 25000, P90 to 37000.
        Assert.Equal(25000.0, speed.ModelTimeP50Ms!.Value, 9);
        Assert.Equal(37000.0, speed.ModelTimeP90Ms!.Value, 9);
        Assert.Equal(40000.0, speed.ModelTimeMaxMs!.Value, 9);
    }

    [Fact]
    public void Cost_SplitsByRoleAndDerivesPerQuestionAndPerIndexPoint()
    {
        var questions = Questions(50, 50);
        var runs = new[]
        {
            Run(1, questions, new[] { 80, 80 }),
            Run(2, questions, new[] { 80, 80 }),
            Run(3, questions, new[] { 80, 80 })
        };

        var costs = new[] { 1L, 2L, 3L }.Select(id => new BenchmarkGroupRunCost
        {
            RunId = id,
            CostByRole = new Dictionary<string, double> { ["candidate"] = 0.5, ["assessor"] = 0.3, ["claimVerifier"] = 1.7 }
        }).ToList();

        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, runs, costs);
        var cost = result.Cost!;

        Assert.Equal(7.5, cost.TotalCost, 9);
        Assert.Equal(2.5, cost.MeanCostPerRun, 9);
        Assert.Equal(0.0, cost.CostStandardDeviation!.Value, 9);
        Assert.Equal(5.1, cost.TotalCostByRole["claimVerifier"], 9);
        Assert.Equal(1.7, cost.MeanCostByRole["claimVerifier"], 9);
        Assert.Equal(1.25, cost.CostPerQuestion!.Value, 9);
        Assert.Equal(2.5 / 80.0, cost.CostPerIndexPoint!.Value, 9);
    }

    [Fact]
    public void APricingMismatchDegradesCostAndLeavesQualityAlone()
    {
        var questions = Questions(50, 50);
        var runs = new[] { Run(1, questions, new[] { 80, 80 }), Run(2, questions, new[] { 80, 80 }) };
        var costs = new[]
        {
            new BenchmarkGroupRunCost { RunId = 1, CostByRole = new Dictionary<string, double> { ["candidate"] = 1.0 } },
            new BenchmarkGroupRunCost { RunId = 2, CostByRole = new Dictionary<string, double> { ["candidate"] = 2.0 } }
        };

        var options = new BenchmarkGroupStatisticsOptions
        {
            CostDegraded = true,
            CostDegradedReason = "PricingSnapshot differs"
        };

        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, runs, costs, options);

        Assert.True(result.Cost!.Degraded);
        Assert.False(result.Speed.Degraded);
        Assert.Equal(80.0, result.Index.PointEstimate, 9);
    }

    [Fact]
    public void DegradedFlags_AreDerivedFromTheComparabilityTier()
    {
        var comparability = new BenchmarkComparabilityResult
        {
            Tier = BenchmarkComparabilityTier.QualityComparable,
            SpeedAggregatesDegraded = true,
            CostAggregatesDegraded = true,
            Differences = new[]
            {
                new BenchmarkComparabilityKeyDifference
                {
                    Name = BenchmarkComparabilityKey.QuestionParallelismKey,
                    Kind = BenchmarkComparabilityKeyKind.SpeedAndCost,
                    Variants = new[]
                    {
                        new BenchmarkComparabilityKeyVariant { Value = "1", RunIds = new long[] { 1 } },
                        new BenchmarkComparabilityKeyVariant { Value = "3", RunIds = new long[] { 2 } }
                    }
                }
            }
        };

        var options = BenchmarkGroupStatisticsOptions.FromComparability(comparability);

        Assert.True(options.SpeedDegraded);
        Assert.True(options.CostDegraded);
        Assert.Contains(BenchmarkComparabilityKey.QuestionParallelismKey, options.SpeedDegradedReason);
    }

    [Fact]
    public void TheDecompositionCaveat_TravelsWithTheNumbers()
    {
        var questions = Questions(50);
        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[] { Run(1, questions, new[] { 80 }) });

        Assert.Contains("grader stochasticity", result.VarianceDecompositionCaveat);
        Assert.Contains("SecondOpinionMode = All", result.VarianceDecompositionCaveat);
    }

    // --- Wilcoxon signed-rank -----------------------------------------------------------------------

    [Fact]
    public void WilcoxonSignedRank_ReproducesAHandComputedExactCase()
    {
        // Differences 1, 2, 3, 4, 5, -6. The absolute values are 1..6, so the ranks are 1..6 with
        // no ties and rank 6 falls on the only negative value.
        //   W+ = 1+2+3+4+5 = 15, W- = 6, statistic = 6
        // Exact null distribution over the 2^6 = 64 sign assignments; subsets of {1..6} summing to
        // at most 6 number 14 (the empty set, {1}, {2}, {3}, {1,2}, {4}, {1,3}, {5}, {1,4}, {2,3},
        // {6}, {1,5}, {2,4}, {1,2,3}), so P(W+ <= 6) = 14/64 = 0.21875 and the two-sided p-value is
        // 0.4375.
        var result = BenchmarkGroupStatistics.WilcoxonSignedRank(new double[] { 1, 2, 3, 4, 5, -6 });

        Assert.Equal(6, result.SampleSize);
        Assert.Equal(0, result.ZeroDifferenceCount);
        Assert.Equal(15.0, result.PositiveRankSum, 9);
        Assert.Equal(6.0, result.NegativeRankSum, 9);
        Assert.Equal(6.0, result.Statistic, 9);
        Assert.Equal(0.4375, result.PValue!.Value, 9);
        Assert.StartsWith("exact", result.Method);
        Assert.False(result.TiesPresent);
    }

    [Fact]
    public void WilcoxonSignedRank_ReachesTheSmallestAttainableTwoSidedPValueForItsSampleSize()
    {
        // Every difference the same sign: W- = 0, and only one of the 2^5 = 32 sign assignments is
        // at least as extreme in each direction, so p = 2/32 = 0.0625. This is the textbook fact
        // that five pairs cannot reach 0.05 however large the effect.
        var result = BenchmarkGroupStatistics.WilcoxonSignedRank(new double[] { 3, 7, 11, 15, 19 });

        Assert.Equal(0.0, result.NegativeRankSum, 9);
        Assert.Equal(15.0, result.PositiveRankSum, 9);
        Assert.Equal(0.0625, result.PValue!.Value, 9);
    }

    [Fact]
    public void WilcoxonSignedRank_UsesTieCorrectedAverageRanks()
    {
        // |d| = 1, 1, 2, 2, 3 -> ranks 1.5, 1.5, 3.5, 3.5, 5.
        //   W+ = 1.5 (for +1) + 3.5 (for +2) + 5 (for +3) = 10
        //   W- = 1.5 + 3.5 = 5
        var result = BenchmarkGroupStatistics.WilcoxonSignedRank(new double[] { 1, -1, 2, -2, 3 });

        Assert.True(result.TiesPresent);
        Assert.Equal(10.0, result.PositiveRankSum, 9);
        Assert.Equal(5.0, result.NegativeRankSum, 9);
        Assert.Equal(5.0, result.Statistic, 9);
    }

    [Fact]
    public void WilcoxonSignedRank_DiscardsZeroDifferencesAndReportsHowMany()
    {
        var result = BenchmarkGroupStatistics.WilcoxonSignedRank(new double[] { 0, 0, 4, -1, 2 });

        Assert.Equal(3, result.SampleSize);
        Assert.Equal(2, result.ZeroDifferenceCount);

        var none = BenchmarkGroupStatistics.WilcoxonSignedRank(new double[] { 0, 0, 0 });
        Assert.Equal(0, none.SampleSize);
        Assert.Null(none.PValue);
        Assert.Equal("not computed", none.Method);
    }

    [Fact]
    public void WilcoxonSignedRank_FallsBackToTheNormalApproximationAboveTheExactThreshold()
    {
        var differences = Enumerable.Range(1, BenchmarkGroupStatistics.MaxExactWilcoxonSampleSize + 1)
            .Select(i => (double)i)
            .ToList();

        var result = BenchmarkGroupStatistics.WilcoxonSignedRank(differences);

        Assert.StartsWith("normal approximation", result.Method);
        Assert.True(result.PValue!.Value < 0.001);
    }

    // --- Benjamini-Hochberg ---------------------------------------------------------------------------

    [Fact]
    public void BenjaminiHochberg_ReproducesTheKnownRejectionSetOfTheOriginalPaper()
    {
        // The fifteen p-values Benjamini and Hochberg (1995) work through. At q = 0.05 the
        // procedure rejects the four smallest: 4/15 * 0.05 = 0.01333 clears p(4) = 0.0095, while
        // 5/15 * 0.05 = 0.01667 does not clear p(5) = 0.0201.
        var p = new[]
        {
            0.0001, 0.0004, 0.0019, 0.0095, 0.0201, 0.0278, 0.0298, 0.0344,
            0.0459, 0.3240, 0.4262, 0.5719, 0.6528, 0.7590, 1.0000
        };

        var results = BenchmarkGroupStatistics.BenjaminiHochberg(p, 0.05);

        Assert.Equal(4, results.Count(r => r.Rejected));
        Assert.True(results.Take(4).All(r => r.Rejected));
        Assert.True(results.Skip(4).All(r => !r.Rejected));

        // The adjusted p-value of the last rejection: 0.0095 * 15 / 4.
        Assert.Equal(0.0095 * 15.0 / 4.0, results[3].AdjustedPValue, 9);
        Assert.Equal(0.0201 * 15.0 / 5.0, results[4].AdjustedPValue, 9);

        // Adjusted values are monotone in the raw p-value and never exceed 1.
        var sorted = results.OrderBy(r => r.PValue).Select(r => r.AdjustedPValue).ToList();
        for (int i = 1; i < sorted.Count; i++)
        {
            Assert.True(sorted[i] >= sorted[i - 1] - 1e-12);
        }

        Assert.All(results, r => Assert.True(r.AdjustedPValue <= 1.0));
    }

    [Fact]
    public void BenjaminiHochberg_ReturnsResultsInInputOrderRegardlessOfSorting()
    {
        var p = new[] { 0.7590, 0.0001, 0.0095, 0.0004, 0.0019 };

        var results = BenchmarkGroupStatistics.BenjaminiHochberg(p, 0.05);

        Assert.Equal(p, results.Select(r => r.PValue));
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, results.Select(r => r.Index));
        Assert.False(results[0].Rejected);
        Assert.True(results[1].Rejected);
    }

    [Fact]
    public void BenjaminiHochberg_RejectsNothingWhenNothingClearsItsThreshold()
    {
        var results = BenchmarkGroupStatistics.BenjaminiHochberg(new[] { 0.2, 0.4, 0.6, 0.8 }, 0.05);
        Assert.DoesNotContain(results, r => r.Rejected);
    }

    // --- Effect size and the paired tests -----------------------------------------------------------

    [Fact]
    public void CohensDz_MatchesAHandComputedCase()
    {
        // d = {2, 4, 4, 4, 5, 5, 7, 9}: mean 5, squared deviations 9+1+1+1+0+0+4+16 = 32,
        // sample variance 32/7, SD = sqrt(32/7) = 2.1380899, dz = 5 / 2.1380899 = 2.3385358.
        var dz = BenchmarkGroupStatistics.CohensDz(new double[] { 2, 4, 4, 4, 5, 5, 7, 9 });

        Assert.Equal(5.0 / Math.Sqrt(32.0 / 7.0), dz!.Value, 12);
        Assert.Equal(2.3385358, dz!.Value, 6);
    }

    [Fact]
    public void CohensDz_IsNullWithoutSpreadOrWithoutAPair()
    {
        Assert.Null(BenchmarkGroupStatistics.CohensDz(new double[] { 4 }));
        Assert.Null(BenchmarkGroupStatistics.CohensDz(new double[] { 3, 3, 3 }));
    }

    [Fact]
    public void PairedTTest_MatchesAHandComputedCase()
    {
        // Same vector: t = 5 / (2.1380899 / sqrt(8)) = 6.6144 on 7 degrees of freedom.
        var result = BenchmarkGroupStatistics.PairedTTest(new double[] { 2, 4, 4, 4, 5, 5, 7, 9 });

        Assert.Equal(8, result.SampleSize);
        Assert.Equal(5.0, result.MeanDifference, 12);
        Assert.Equal(5.0 / (Math.Sqrt(32.0 / 7.0) / Math.Sqrt(8.0)), result.TStatistic!.Value, 9);
        Assert.Equal(7.0, result.DegreesOfFreedom!.Value, 9);
        Assert.Equal(0.000300, result.PValue!.Value, 5);
    }

    [Fact]
    public void StudentTCritical95_MatchesPublishedTables()
    {
        Assert.Equal(12.7062, BenchmarkGroupStatistics.StudentTCritical95(1), 4);
        Assert.Equal(4.3027, BenchmarkGroupStatistics.StudentTCritical95(2), 4);
        Assert.Equal(2.1009, BenchmarkGroupStatistics.StudentTCritical95(18), 4);

        // Above the table the Cornish-Fisher continuation takes over.
        Assert.Equal(2.0211, BenchmarkGroupStatistics.StudentTCritical95(40), 3);
        Assert.Equal(2.0003, BenchmarkGroupStatistics.StudentTCritical95(60), 3);
        Assert.Equal(1.9600, BenchmarkGroupStatistics.StudentTCritical95(1000000), 3);
    }

    [Fact]
    public void NormalCdf_MatchesItsOwnCriticalValue()
    {
        Assert.Equal(0.5, BenchmarkGroupStatistics.NormalCdf(0.0), 12);
        Assert.Equal(0.975, BenchmarkGroupStatistics.NormalCdf(BenchmarkGroupStatistics.NormalCritical95), 9);
    }

    [Fact]
    public void Percentile_InterpolatesBetweenOrderStatisticsAndAgreesWithTheMedian()
    {
        var values = new double[] { 1, 2, 3, 4 };

        Assert.Equal(2.5, BenchmarkGroupStatistics.Percentile(values, 50)!.Value, 12);
        Assert.Equal(BenchmarkGroupStatistics.Median(values)!.Value, BenchmarkGroupStatistics.Percentile(values, 50)!.Value, 12);
        Assert.Equal(1.75, BenchmarkGroupStatistics.Percentile(values, 25)!.Value, 12);
        Assert.Equal(3.25, BenchmarkGroupStatistics.Percentile(values, 75)!.Value, 12);
    }

    // --- Group comparison -------------------------------------------------------------------------

    [Fact]
    public void Compare_PairsByQuestionOnCrossRunMeansAndLabelsPerItemTestsExploratory()
    {
        var questions = Questions(50, 50, 50);

        var baseline = BenchmarkGroupStatistics.Compute(Suite(), questions, new[]
        {
            Run(1, questions, new[] { 60, 70, 80 }),
            Run(2, questions, new[] { 62, 72, 82 }),
            Run(3, questions, new[] { 64, 74, 84 })
        });

        // Every item ten points better, with the same within-group spread.
        var treatment = BenchmarkGroupStatistics.Compute(Suite(), questions, new[]
        {
            Run(4, questions, new[] { 70, 80, 90 }),
            Run(5, questions, new[] { 72, 82, 92 }),
            Run(6, questions, new[] { 74, 84, 94 })
        });

        var comparison = BenchmarkGroupStatistics.Compare(baseline, treatment);

        Assert.Equal(3, comparison.PairedItemCount);
        Assert.Equal(0, comparison.UnpairedItemCount);
        Assert.Equal(10.0, comparison.MeanDifference, 9);
        Assert.Equal(0.0, comparison.DifferenceStandardDeviation!.Value, 9);

        // Every difference is positive, so W- is zero; with three pairs the smallest attainable
        // two-sided p-value is 2/8 = 0.25, which is exactly what three items can say.
        Assert.Equal(0.0, comparison.Wilcoxon.NegativeRankSum, 9);
        Assert.Equal(0.25, comparison.Wilcoxon.PValue!.Value, 9);

        Assert.All(comparison.ItemComparisons, i =>
        {
            Assert.True(i.Exploratory);
            Assert.Equal(10.0, i.Difference, 9);
            Assert.NotNull(i.AdjustedPValue);
        });

        Assert.Contains("Benjamini-Hochberg", comparison.ExploratoryNote);
        Assert.Contains("grader stochasticity", comparison.VarianceDecompositionCaveat);
        Assert.Equal(new long[] { 1, 2, 3 }, comparison.BaselineRunIds);
        Assert.Equal(new long[] { 4, 5, 6 }, comparison.TreatmentRunIds);
    }

    [Fact]
    public void Compare_ExcludesItemsThatOnlyOneSideAnswered()
    {
        var baselineQuestions = Questions(50, 50, 50);
        var treatmentQuestions = Questions(50, 50);

        var baseline = BenchmarkGroupStatistics.Compute(Suite(), baselineQuestions, new[]
        {
            Run(1, baselineQuestions, new[] { 60, 70, 80 }),
            Run(2, baselineQuestions, new[] { 62, 72, 82 })
        });

        var treatment = BenchmarkGroupStatistics.Compute(Suite(), treatmentQuestions, new[]
        {
            Run(3, treatmentQuestions, new[] { 70, 80 }),
            Run(4, treatmentQuestions, new[] { 72, 82 })
        });

        var comparison = BenchmarkGroupStatistics.Compare(baseline, treatment);

        Assert.Equal(2, comparison.PairedItemCount);
        Assert.Equal(1, comparison.UnpairedItemCount);
    }

    [Fact]
    public void Compare_ReportsCohensDzAndAPairedTBesideWilcoxon()
    {
        var questions = Questions(50, 50, 50, 50);

        var baseline = BenchmarkGroupStatistics.Compute(Suite(), questions, new[]
        {
            Run(1, questions, new[] { 60, 70, 80, 90 }),
            Run(2, questions, new[] { 62, 72, 82, 92 }),
            Run(3, questions, new[] { 64, 74, 84, 94 })
        });

        var treatment = BenchmarkGroupStatistics.Compute(Suite(), questions, new[]
        {
            Run(4, questions, new[] { 70, 74, 86, 88 }),
            Run(5, questions, new[] { 72, 76, 88, 90 }),
            Run(6, questions, new[] { 74, 78, 90, 92 })
        });

        var comparison = BenchmarkGroupStatistics.Compare(baseline, treatment);

        // Item differences: +10, +4, +6, -2. Mean 4.5.
        Assert.Equal(4.5, comparison.MeanDifference, 9);
        Assert.NotNull(comparison.CohensDz);
        Assert.Equal(comparison.MeanDifference / comparison.DifferenceStandardDeviation!.Value, comparison.CohensDz!.Value, 9);
        Assert.Equal(4, comparison.PairedT.SampleSize);
        Assert.NotNull(comparison.PairedT.PValue);
        Assert.Equal(BenchmarkGroupStatistics.DefaultFalseDiscoveryRate, comparison.FalseDiscoveryRate);
    }

    // --- The speed caveat describes the group, not its tier -------------------------------------

    /// <summary>
    /// A group can sit at Tier B on a key that degrades cost alone. Saying its speed aggregates are
    /// degraded because it is Tier B is false, and it is the reason this text is built rather than
    /// printed from a constant.
    /// </summary>
    [Fact]
    public void SpeedCaveat_SaysNothingMoved_WhenSpeedIsNotDegraded()
    {
        var questions = Questions(50, 50);
        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[]
        {
            Run(1, questions, new[] { 80, 90 }),
            Run(2, questions, new[] { 82, 92 })
        });

        Assert.False(result.Speed.Degraded);
        Assert.Contains("No comparability key affecting timing differs", result.Speed.Caveat);
        Assert.DoesNotContain("Tier B", result.Speed.Caveat);
    }

    [Fact]
    public void SpeedCaveat_NamesTheReason_WhenSpeedIsDegraded()
    {
        var questions = Questions(50, 50);
        var options = new BenchmarkGroupStatisticsOptions
        {
            SpeedDegraded = true,
            SpeedDegradedReason = "QuestionParallelism: 1 (runs 1) vs 3 (runs 2)"
        };

        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[]
        {
            Run(1, questions, new[] { 80, 90 }),
            Run(2, questions, new[] { 82, 92 })
        }, costs: null, options: options);

        Assert.True(result.Speed.Degraded);
        Assert.Contains("mix timing conditions", result.Speed.Caveat);
        Assert.Contains("QuestionParallelism", result.Speed.Caveat);
    }

    // --- Intervals stay inside the score range ---------------------------------------------------

    /// <summary>
    /// Runs 16–18 scored Q1 at 45, 78 and 90: mean 71.0, SD 23.3, and a half-width of about 57.9,
    /// which put the reported upper bound at 128.9 on a scale that stops at 100. The half-width is
    /// the real quantity and is left alone; the bound is clamped and the truncation is flagged.
    /// </summary>
    [Fact]
    public void ItemInterval_IsClampedToTheScoreRange_AndFlaggedTruncated()
    {
        var questions = Questions(50);
        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[]
        {
            Run(1, questions, new[] { 45 }),
            Run(2, questions, new[] { 78 }),
            Run(3, questions, new[] { 90 })
        });

        var item = Assert.Single(result.Items);

        // SD = 23.302, SE = 13.454, t(2) = 4.3027 → half-width 57.887.
        Assert.Equal(71.0, item.Mean, 9);
        Assert.Equal(57.887, item.MeanConfidenceHalfWidth!.Value, 3);

        // Only the upper bound leaves the range: 71 − 57.887 is still above zero.
        Assert.Equal(13.113, item.MeanConfidenceLower!.Value, 3);
        Assert.Equal(100.0, item.MeanConfidenceUpper!.Value, 9);
        Assert.True(item.MeanConfidenceTruncated);
    }

    [Fact]
    public void ItemInterval_IsNotFlaggedTruncated_WhenItFitsInTheScoreRange()
    {
        var questions = Questions(50);
        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[]
        {
            Run(1, questions, new[] { 70 }),
            Run(2, questions, new[] { 71 }),
            Run(3, questions, new[] { 72 })
        });

        var item = Assert.Single(result.Items);

        Assert.False(item.MeanConfidenceTruncated);
        Assert.True(item.MeanConfidenceUpper < 100.0);
    }

    [Fact]
    public void CombinedIndexInterval_IsClampedAndFlagged_AtTheCeiling()
    {
        var questions = Questions(50, 50);
        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[]
        {
            Run(1, questions, new[] { 100, 100 }),
            Run(2, questions, new[] { 100, 100 }),
            Run(3, questions, new[] { 100, 99 })
        });

        Assert.Equal(100.0, result.Index.CombinedUpper!.Value, 9);
        Assert.True(result.Index.CombinedIntervalTruncated);
    }

    // --- Per-dimension statistics ----------------------------------------------------------------

    /// <summary>
    /// The measurement four consecutive single runs said was needed and no replicate set could
    /// supply: whether the Completeness gap survives across runs.
    /// </summary>
    [Fact]
    public void Dimensions_PoolPerRunMeansAndPerItemMeans()
    {
        var questions = Questions(50, 50);

        var a = Run(1, questions, new[] { 90, 80 });
        Score(a, accuracy: new[] { 96, 94 }, completeness: new[] { 84, 80 });
        var b = Run(2, questions, new[] { 92, 82 });
        Score(b, accuracy: new[] { 98, 96 }, completeness: new[] { 86, 82 });
        var c = Run(3, questions, new[] { 94, 84 });
        Score(c, accuracy: new[] { 97, 95 }, completeness: new[] { 88, 84 });

        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[] { a, b, c });

        Assert.Equal(4, result.Dimensions.Count);
        Assert.Equal(
            new[] { "Accuracy", "Completeness", "Conciseness", "Readability" },
            result.Dimensions.Select(d => d.Dimension));

        var accuracyStats = result.Dimensions.Single(d => d.Dimension == "Accuracy");
        // Per-run unweighted means: 95, 97, 96 → mean 96.
        Assert.Equal(new[] { 95.0, 97.0, 96.0 }, accuracyStats.PerRunMeans);
        Assert.Equal(96.0, accuracyStats.Mean!.Value, 9);
        Assert.Equal(1.0, accuracyStats.StandardDeviation!.Value, 9);
        Assert.Equal(95.0, accuracyStats.Min!.Value, 9);
        Assert.Equal(97.0, accuracyStats.Max!.Value, 9);
        Assert.NotNull(accuracyStats.ConfidenceHalfWidth);

        // Q1's accuracy across the three runs: 96, 98, 97 → 97.
        Assert.Equal(97.0, accuracyStats.ItemMeans[questions[0].Id], 9);

        var completenessStats = result.Dimensions.Single(d => d.Dimension == "Completeness");
        // 82, 84, 86 → 84. Completeness trails Accuracy by 12 points, which is the finding.
        Assert.Equal(84.0, completenessStats.Mean!.Value, 9);
        Assert.True(completenessStats.Mean < accuracyStats.Mean);
    }

    /// <summary>
    /// A dimension nothing scored is emitted empty rather than omitted, so a consumer never has to
    /// tell "absent" from "unscored".
    /// </summary>
    [Fact]
    public void Dimensions_AreAllPresent_EvenWhenNothingScoredThem()
    {
        var questions = Questions(50);
        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[]
        {
            Run(1, questions, new[] { 90 }),
            Run(2, questions, new[] { 92 })
        });

        Assert.Equal(4, result.Dimensions.Count);
        Assert.All(result.Dimensions, d =>
        {
            Assert.Empty(d.PerRunMeans);
            Assert.Null(d.Mean);
            Assert.Null(d.StandardDeviation);
            Assert.Null(d.ConfidenceHalfWidth);
        });
    }

    [Fact]
    public void Dimensions_ReportNoHalfWidth_BelowThreeRuns()
    {
        var questions = Questions(50);

        var a = Run(1, questions, new[] { 90 });
        Score(a, accuracy: new[] { 96 });
        var b = Run(2, questions, new[] { 92 });
        Score(b, accuracy: new[] { 98 });

        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[] { a, b });

        var accuracyStats = result.Dimensions.Single(d => d.Dimension == "Accuracy");
        Assert.Equal(97.0, accuracyStats.Mean!.Value, 9);
        Assert.NotNull(accuracyStats.StandardDeviation);
        Assert.Null(accuracyStats.ConfidenceHalfWidth);
    }

    // --- Token, tool and claim-verification usage ------------------------------------------------

    [Fact]
    public void Usage_PoolsTokensToolFamiliesAndClaimCounts()
    {
        var questions = Questions(50, 50);

        var a = Run(1, questions, new[] { 90, 80 });
        a.TotalInputTokens = 1_000_000;
        a.TotalOutputTokens = 40_000;
        a.TotalCacheReadTokens = 900_000;
        a.TotalAssessmentInputTokens = 50_000;
        a.TotalClaimVerificationInputTokens = 70_000;
        a.ClaimsSupportedCount = 3;
        a.ClaimsRefutedCount = 1;
        a.ClaimVerifiedAnswerCount = 2;
        // The stored format is name×count with no spaces, as BenchmarkService writes it.
        a.Answers[0].ToolCallSummary = "source_code_search×4, wiki_search×1";
        a.Answers[1].ToolCallSummary = "monster_lookup×1";

        var b = Run(2, questions, new[] { 92, 82 });
        b.TotalInputTokens = 1_200_000;
        b.TotalOutputTokens = 60_000;
        b.TotalCacheReadTokens = 1_100_000;
        b.ClaimsIndeterminateCount = 2;
        b.ClaimVerifiedAnswerCount = 1;
        b.Answers[0].ToolCallSummary = "wiki_search×3";
        b.Answers[1].ToolCallSummary = "get_knowledge_article×1";

        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[] { a, b });
        var usage = Assert.IsType<BenchmarkGroupUsageStatistics>(result.Usage);

        Assert.Equal(2_200_000, usage.TotalInputTokens);
        Assert.Equal(100_000, usage.TotalOutputTokens);
        Assert.Equal(2_000_000, usage.TotalCacheReadTokens);
        Assert.Equal(2_000_000 * 100.0 / 2_200_000, usage.CacheReadSharePercentage!.Value, 9);
        Assert.Equal(22.0, usage.InputOutputRatio!.Value, 9);

        // The grader totals stay out of the candidate's.
        Assert.Equal(50_000, usage.TotalAssessmentInputTokens);
        Assert.Equal(70_000, usage.TotalClaimVerificationInputTokens);

        // 4 source + 4 wiki + 1 structured + 1 knowledge base = 10.
        Assert.Equal(10, usage.TotalToolCalls);
        Assert.Equal(4, usage.ToolCallsByFamily["SourceCode"]);
        Assert.Equal(4, usage.ToolCallsByFamily["Wiki"]);
        Assert.Equal(1, usage.ToolCallsByFamily["StructuredLookup"]);
        Assert.Equal(1, usage.ToolCallsByFamily["KnowledgeBase"]);
        Assert.Equal(100.0, usage.ToolFamilyShares.Values.Sum(), 9);
        Assert.Equal(5.0, usage.MeanToolCallsPerRun!.Value, 9);

        Assert.Equal(3, usage.ClaimsSupported);
        Assert.Equal(1, usage.ClaimsRefuted);
        Assert.Equal(2, usage.ClaimsIndeterminate);
        Assert.Equal(6, usage.ClaimsChecked);
        Assert.Equal(3, usage.AnswersWithVerification);
    }

    /// <summary>An all-zero usage block reads as a measurement. A run that recorded none is null.</summary>
    [Fact]
    public void Usage_IsNull_WhenNoMemberRecordedAny()
    {
        var questions = Questions(50);
        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[]
        {
            Run(1, questions, new[] { 90 }),
            Run(2, questions, new[] { 92 })
        });

        Assert.Null(result.Usage);
    }

    // --- Per-role cost dispersion ----------------------------------------------------------------

    /// <summary>
    /// Runs 16–18 cost $3.46, $3.00 and $5.92 on a configuration whose comparability keys all
    /// matched, and the spread sat entirely in the claim verifier. A mean with one standard
    /// deviation on the total cannot show that; per-role dispersion can.
    /// </summary>
    [Fact]
    public void Cost_ReportsPerRoleDispersionAndPerRunTotals()
    {
        var questions = Questions(50);
        var costs = new[]
        {
            RunCost(1, candidate: 0.30, assessor: 0.50, claimVerifier: 2.00),
            RunCost(2, candidate: 0.30, assessor: 0.50, claimVerifier: 5.00)
        };

        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[]
        {
            Run(1, questions, new[] { 90 }),
            Run(2, questions, new[] { 92 })
        }, costs);

        var cost = Assert.IsType<BenchmarkGroupCostStatistics>(result.Cost);

        Assert.Equal(new[] { 2.80, 5.80 }, cost.PerRunTotals.Select(t => Math.Round(t, 9)));

        // The two fixed roles have no spread; the verifier carries all of it.
        Assert.Equal(0.0, cost.CostStandardDeviationByRole["candidate"]!.Value, 9);
        Assert.Equal(0.0, cost.CostStandardDeviationByRole["assessor"]!.Value, 9);
        Assert.True(cost.CostStandardDeviationByRole["claimVerifier"]!.Value > 2.0);

        Assert.Equal(2.00, cost.MinCostByRole["claimVerifier"], 9);
        Assert.Equal(5.00, cost.MaxCostByRole["claimVerifier"], 9);
    }

    /// <summary>A role one run never spent on contributes zero to the spread, not a skipped sample.</summary>
    [Fact]
    public void Cost_TreatsAnAbsentRoleAsZeroSpend()
    {
        var questions = Questions(50);
        var costs = new[]
        {
            RunCost(1, candidate: 0.30, assessor: 0.50, claimVerifier: 2.00),
            RunCost(2, candidate: 0.30, assessor: 0.50)
        };

        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[]
        {
            Run(1, questions, new[] { 90 }),
            Run(2, questions, new[] { 92 })
        }, costs);

        var cost = Assert.IsType<BenchmarkGroupCostStatistics>(result.Cost);

        Assert.Equal(0.0, cost.MinCostByRole["claimVerifier"], 9);
        Assert.Equal(2.00, cost.MaxCostByRole["claimVerifier"], 9);
        Assert.True(cost.CostStandardDeviationByRole["claimVerifier"]!.Value > 0.0);
    }

    // --- The prompt the group was graded under ---------------------------------------------------

    [Fact]
    public void PromptUnderTest_DecodesTheFirstMemberAndIsNotDivergent_ForIdenticalMembers()
    {
        var questions = Questions(50);
        const string Options = "{\"verboseMode\":true,\"overseerMode\":0,\"enableToolUse\":true,"
            + "\"allowSourceCodeReferences\":true,\"hasWikiContext\":false}";

        var a = Run(1, questions, new[] { 90 });
        a.CandidatePromptOptionsJson = Options;
        var b = Run(2, questions, new[] { 92 });
        b.CandidatePromptOptionsJson = Options;

        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[] { a, b });
        var prompt = Assert.IsType<BenchmarkGroupPromptUnderTest>(result.PromptUnderTest);

        Assert.True(prompt.Recorded);
        Assert.False(prompt.Divergent);
        Assert.True(prompt.VerboseMode);
        Assert.True(prompt.EnableToolUse);
        Assert.True(prompt.AllowSourceCodeReferences);

        // The permanent divergence from live chat, which every routing finding is measured under.
        Assert.False(prompt.HasWikiContext);
    }

    [Fact]
    public void PromptUnderTest_IsFlaggedDivergent_WhenMembersDisagree()
    {
        var questions = Questions(50);

        var a = Run(1, questions, new[] { 90 });
        a.CandidatePromptOptionsJson = "{\"verboseMode\":true}";
        var b = Run(2, questions, new[] { 92 });
        b.CandidatePromptOptionsJson = "{\"verboseMode\":false}";

        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[] { a, b });

        Assert.True(result.PromptUnderTest!.Divergent);
    }

    [Fact]
    public void PromptUnderTest_SaysSoWhenTheConfigurationWasNotRecorded()
    {
        var questions = Questions(50);
        var result = BenchmarkGroupStatistics.Compute(Suite(), questions, new[]
        {
            Run(1, questions, new[] { 90 })
        });

        Assert.False(result.PromptUnderTest!.Recorded);
    }

    // --- Fixture helpers -------------------------------------------------------------------------

    /// <summary>Adds dimension scores to a run built by <see cref="Run"/>, per answer in order.</summary>
    private static void Score(
        BenchmarkRun run,
        int[]? accuracy = null,
        int[]? completeness = null,
        int[]? conciseness = null,
        int[]? readability = null)
    {
        for (int i = 0; i < run.Answers.Count; i++)
        {
            if (accuracy != null) run.Answers[i].AccuracyScore = accuracy[i];
            if (completeness != null) run.Answers[i].CompletenessScore = completeness[i];
            if (conciseness != null) run.Answers[i].ConcisenessScore = conciseness[i];
            if (readability != null) run.Answers[i].ReadabilityScore = readability[i];
        }
    }

    private static BenchmarkGroupRunCost RunCost(
        long runId, double candidate, double assessor, double? claimVerifier = null)
    {
        var byRole = new Dictionary<string, double>
        {
            ["candidate"] = candidate,
            ["assessor"] = assessor
        };

        if (claimVerifier.HasValue) byRole["claimVerifier"] = claimVerifier.Value;

        return new BenchmarkGroupRunCost { RunId = runId, CostByRole = byRole };
    }
}
