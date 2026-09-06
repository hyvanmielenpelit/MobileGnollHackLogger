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
        Assert.Equal(index.PointEstimate - expected, index.CombinedLower!.Value, 9);
        Assert.Equal(index.PointEstimate + expected, index.CombinedUpper!.Value, 9);

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
}
