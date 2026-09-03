namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkItemAnalysisTests
{
    private static BenchmarkSuite Suite() => new() { Id = 5, Name = "GnollHack Player Assistance Benchmark Suite" };

    private static BenchmarkQuestion Question(
        long id,
        int orderIndex,
        int? assessedDifficulty = 50,
        int itemRevision = 1) => new()
        {
            Id = id,
            BenchmarkSuiteId = 5,
            OrderIndex = orderIndex,
            QuestionText = $"Q{orderIndex}",
            Difficulty = BenchmarkDifficulty.Intermediate,
            AssessedDifficulty = assessedDifficulty,
            ItemRevision = itemRevision
        };

    /// <summary>
    /// One run with one answer. <paramref name="assessorModelId"/> and
    /// <paramref name="scoringMethodVersion"/> default to a single stable configuration, so a test
    /// that does not name them cannot accidentally trip a confound flag.
    /// </summary>
    private static BenchmarkRun Run(
        long runId,
        long questionId,
        int qualityScore,
        int? runQualityIndex = 90,
        string testedModelId = "gpt-5.6-luna",
        string assessorModelId = "gemini-3.7-flash",
        int scoringMethodVersion = 6,
        int? itemRevisionUsed = 1,
        int? toolCallCount = 4,
        int? toolCallBudgetUsed = 25,
        bool toolBudgetExhausted = false,
        BenchmarkAnswerStatus status = BenchmarkAnswerStatus.Ok)
    {
        return new BenchmarkRun
        {
            Id = runId,
            BenchmarkSuiteId = 5,
            SuiteName = "GnollHack Player Assistance Benchmark Suite",
            TestedModelIdUsed = testedModelId,
            AssessorModelIdUsed = assessorModelId,
            ScoringMethodVersion = scoringMethodVersion,
            QualityIndex = runQualityIndex,
            Answers = new List<BenchmarkRunAnswer>
            {
                new()
                {
                    Id = runId * 100,
                    BenchmarkRunId = runId,
                    BenchmarkQuestionId = questionId,
                    ItemRevisionUsed = itemRevisionUsed,
                    OrderIndex = 1,
                    QuestionText = "Q1",
                    Status = status,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = qualityScore,
                    ToolCallCount = toolCallCount,
                    ToolCallBudgetUsed = toolCallBudgetUsed,
                    ToolBudgetExhausted = toolBudgetExhausted
                }
            }
        };
    }

    [Fact]
    public void Compute_ProducesTheBasicStatisticsOverAHandBuiltFixture()
    {
        var question = Question(1, 1, assessedDifficulty: 30);
        var runs = new[]
        {
            Run(1, 1, 60),
            Run(2, 1, 80),
            Run(3, 1, 70)
        };

        var analysis = BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, runs);
        var item = Assert.Single(analysis.Items);

        Assert.Equal(3, item.RunCount);
        Assert.Equal(70.0, item.MeanQuality);
        Assert.Equal(60, item.MinQuality);
        Assert.Equal(80, item.MaxQuality);
        Assert.Equal(8.16, Math.Round(item.StdDev, 2));

        // 100 - 70. The a priori rating was 30, so the item is 0 points harder than advertised.
        Assert.Equal(30, item.EmpiricalDifficulty);
        Assert.Equal(0, item.DifficultyDelta);
    }

    [Fact]
    public void Compute_ExcludesAnswersWithNoQuestionLink()
    {
        var question = Question(1, 1);
        var run = Run(1, 1, 90);

        // The shape the backfill leaves behind when it cannot match unambiguously. Excluding it
        // is the point: a wrong link would corrupt every figure here, invisibly.
        run.Answers.First().BenchmarkQuestionId = null;

        var analysis = BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, new[] { run });
        var item = Assert.Single(analysis.Items);

        Assert.Equal(0, item.RunCount);
        Assert.True(item.InsufficientData);
        Assert.Equal(0, analysis.LinkedAnswerCount);
        Assert.Equal(1, analysis.UnlinkedAnswerCount);
    }

    [Fact]
    public void Compute_ExcludesAnswersAnsweredAgainstADifferentRevision()
    {
        var question = Question(1, 1, itemRevision: 2);
        var runs = new[]
        {
            Run(1, 1, 90, itemRevisionUsed: 1),   // the question as it was before the edit
            Run(2, 1, 40, itemRevisionUsed: 2)    // the question as it is now
        };

        var analysis = BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, runs);
        var item = Assert.Single(analysis.Items);

        Assert.Equal(1, item.RunCount);
        Assert.Equal(40.0, item.MeanQuality);
    }

    [Fact]
    public void Compute_IncludesAndCountsAnswersWhoseRevisionWasNeverRecorded()
    {
        // Null is "unknown", not "revision 1": dropping these would empty the table for every
        // suite that already has runs, and assuming they match would be a claim the data does
        // not support. So they are included and counted.
        var question = Question(1, 1, itemRevision: 3);
        var runs = new[] { Run(1, 1, 90, itemRevisionUsed: null) };

        var item = Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, runs).Items);

        Assert.Equal(1, item.RunCount);
        Assert.Equal(1, item.UnknownRevisionCount);
    }

    [Fact]
    public void Compute_ExcludesAnswersThatWereNeverScored()
    {
        var question = Question(1, 1);
        var runs = new[]
        {
            Run(1, 1, 90),
            Run(2, 1, 0, status: BenchmarkAnswerStatus.ProviderError)
        };

        var item = Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, runs).Items);
        Assert.Equal(1, item.RunCount);
    }

    [Fact]
    public void Discrimination_IsSuppressedBelowFourRuns()
    {
        var question = Question(1, 1);
        var threeRuns = new[] { Run(1, 1, 90, 95), Run(2, 1, 60, 80), Run(3, 1, 70, 85) };

        var item = Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, threeRuns).Items);

        Assert.Null(item.Discrimination);
        Assert.True(item.InsufficientData);
    }

    [Fact]
    public void Discrimination_SeparatesStrongRunsFromWeakOnesAtFourRuns()
    {
        var question = Question(1, 1);
        var runs = new[]
        {
            Run(1, 1, 95, runQualityIndex: 95),
            Run(2, 1, 90, runQualityIndex: 92),
            Run(3, 1, 60, runQualityIndex: 70),
            Run(4, 1, 50, runQualityIndex: 65)
        };

        var item = Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, runs).Items);

        // Top half (95, 90) minus bottom half (60, 50).
        Assert.Equal(37.5, item.Discrimination);
        Assert.False(item.InsufficientData);
    }

    [Fact]
    public void Discrimination_IsNullWhenTheRunsCarryNoIndexToRankBy()
    {
        var question = Question(1, 1);
        var runs = Enumerable.Range(1, 4)
            .Select(i => Run(i, 1, 80, runQualityIndex: null))
            .ToArray();

        var item = Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, runs).Items);
        Assert.Null(item.Discrimination);
    }

    [Fact]
    public void Saturated_FiresOnACeilingWithNoSpread_AndNotJustBelowIt()
    {
        var question = Question(1, 1);

        var saturated = new[] { Run(1, 1, 98), Run(2, 1, 100), Run(3, 1, 99) };
        var item = Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, saturated).Items);
        Assert.True(item.Flags.HasFlag(BenchmarkItemFlags.Saturated));

        // Mean 96.7 — just under the threshold, so the same tight spread is not saturation.
        var nearly = new[] { Run(1, 1, 96), Run(2, 1, 97), Run(3, 1, 97) };
        var nearlyItem = Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, nearly).Items);
        Assert.False(nearlyItem.Flags.HasFlag(BenchmarkItemFlags.Saturated));
    }

    [Fact]
    public void Miscalibrated_FiresOnItsBoundaryAndNotJustBelowIt()
    {
        // Empirical 40 against an a priori 15 is a 25-point gap: exactly the boundary.
        var atBoundary = Question(1, 1, assessedDifficulty: 15);
        var runs = new[] { Run(1, 1, 60), Run(2, 1, 60) };
        var item = Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { atBoundary }, runs).Items);
        Assert.Equal(25, item.DifficultyDelta);
        Assert.True(item.Flags.HasFlag(BenchmarkItemFlags.Miscalibrated));

        var justBelow = Question(1, 1, assessedDifficulty: 16);
        var belowItem = Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { justBelow }, runs).Items);
        Assert.Equal(24, belowItem.DifficultyDelta);
        Assert.False(belowItem.Flags.HasFlag(BenchmarkItemFlags.Miscalibrated));
    }

    [Fact]
    public void Unstable_FiresOnItsBoundaryAndNotJustBelowIt()
    {
        var question = Question(1, 1);

        var wide = new[] { Run(1, 1, 60), Run(2, 1, 90) };
        Assert.True(Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, wide).Items)
            .Flags.HasFlag(BenchmarkItemFlags.Unstable));

        var narrow = new[] { Run(1, 1, 61), Run(2, 1, 90) };
        Assert.False(Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, narrow).Items)
            .Flags.HasFlag(BenchmarkItemFlags.Unstable));
    }

    [Fact]
    public void BudgetBound_FiresAtHalfTheRunsAndNotBelowIt()
    {
        var question = Question(1, 1);

        // 23 of 25 is above 90% of the budget; 2 of 4 runs is exactly the threshold.
        var half = new[]
        {
            Run(1, 1, 60, toolCallCount: 23, toolCallBudgetUsed: 25),
            Run(2, 1, 60, toolCallCount: 25, toolCallBudgetUsed: 25, toolBudgetExhausted: true),
            Run(3, 1, 90, toolCallCount: 4, toolCallBudgetUsed: 25),
            Run(4, 1, 90, toolCallCount: 4, toolCallBudgetUsed: 25)
        };
        var item = Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, half).Items);
        Assert.Equal(0.5, item.BudgetBoundFraction);
        Assert.True(item.Flags.HasFlag(BenchmarkItemFlags.BudgetBound));

        var oneOfFour = new[]
        {
            Run(1, 1, 60, toolCallCount: 23, toolCallBudgetUsed: 25),
            Run(2, 1, 90, toolCallCount: 4, toolCallBudgetUsed: 25),
            Run(3, 1, 90, toolCallCount: 4, toolCallBudgetUsed: 25),
            Run(4, 1, 90, toolCallCount: 4, toolCallBudgetUsed: 25)
        };
        Assert.False(Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, oneOfFour).Items)
            .Flags.HasFlag(BenchmarkItemFlags.BudgetBound));
    }

    [Fact]
    public void AssessorConfounded_FiresAtTwoDistinctAssessors_AndConfoundsTheRow()
    {
        var question = Question(1, 1);
        var runs = new[]
        {
            Run(1, 1, 60, assessorModelId: "gemini-3.7-flash"),
            Run(2, 1, 90, assessorModelId: "claude-opus-5")
        };

        var item = Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, runs).Items);

        Assert.Equal(2, item.DistinctAssessorCount);
        Assert.True(item.Flags.HasFlag(BenchmarkItemFlags.AssessorConfounded));

        // The spread now mixes candidate ability with grader severity, so nothing else on the row
        // is a measurement.
        Assert.True(item.Confounded);
    }

    [Fact]
    public void ScoringMethodMixed_FiresAtTwoVersions_AndConfoundsTheRow()
    {
        // Method 5 permitted an Accuracy deduction for an unverifiable claim and method 6 forbids
        // it, so two runs across that boundary are not the same measurement.
        var question = Question(1, 1);
        var runs = new[]
        {
            Run(1, 1, 60, scoringMethodVersion: 5),
            Run(2, 1, 90, scoringMethodVersion: 6)
        };

        var item = Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, runs).Items);

        Assert.Equal(2, item.DistinctScoringMethodVersionCount);
        Assert.True(item.Flags.HasFlag(BenchmarkItemFlags.ScoringMethodMixed));
        Assert.True(item.Confounded);
    }

    [Fact]
    public void StableConfiguration_LeavesTheRowUnconfounded()
    {
        var question = Question(1, 1);
        var runs = new[] { Run(1, 1, 60), Run(2, 1, 90) };

        var item = Assert.Single(BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, runs).Items);
        Assert.False(item.Confounded);
    }

    [Fact]
    public void SuiteLevelCounts_ReportTheSampleAndBothMixes()
    {
        var questions = new[] { Question(1, 1), Question(2, 2) };
        var runs = new[]
        {
            Run(1, 1, 60, testedModelId: "gpt-5.6-luna", assessorModelId: "gemini-3.7-flash", scoringMethodVersion: 5),
            Run(2, 2, 90, testedModelId: "gemini-3.7-pro", assessorModelId: "claude-opus-5", scoringMethodVersion: 6)
        };

        var analysis = BenchmarkItemAnalysis.Compute(Suite(), questions, runs);

        Assert.Equal(2, analysis.RunCount);
        Assert.Equal(2, analysis.DistinctModelCount);
        Assert.Equal(2, analysis.DistinctAssessorCount);
        Assert.Equal(2, analysis.DistinctScoringMethodVersionCount);
        Assert.Equal(2, analysis.LinkedAnswerCount);
        Assert.Equal(0, analysis.UnlinkedAnswerCount);
    }

    [Fact]
    public void Items_AreOrderedByTheSizeOfTheDifficultyGap()
    {
        // The table exists to surface miscalibration, so the worst gap sorts first.
        var questions = new[]
        {
            Question(1, 1, assessedDifficulty: 40),   // empirical 40, delta 0
            Question(2, 2, assessedDifficulty: 10)    // empirical 40, delta 30
        };
        var runs = new[] { Run(1, 1, 60), Run(2, 2, 60) };
        runs[1].Answers.First().OrderIndex = 2;

        var analysis = BenchmarkItemAnalysis.Compute(Suite(), questions, runs);

        Assert.Equal(2, analysis.Items[0].OrderIndex);
        Assert.Equal(30, analysis.Items[0].DifficultyDelta);
    }

    [Fact]
    public void EmpiricalDifficulty_IsNeverWrittenBackIntoTheQuestion()
    {
        // The rule this asserts is the whole reason the delta is reported rather than applied:
        // AssessedDifficulty weights the Intelligence Index, so deriving it from the scores it
        // weights would let a model that did badly reduce that item's weight.
        var question = Question(1, 1, assessedDifficulty: 30);
        var runs = new[] { Run(1, 1, 10) };

        var analysis = BenchmarkItemAnalysis.Compute(Suite(), new[] { question }, runs);

        Assert.Equal(90, analysis.Items[0].EmpiricalDifficulty);
        Assert.Equal(30, question.AssessedDifficulty);
    }
}
