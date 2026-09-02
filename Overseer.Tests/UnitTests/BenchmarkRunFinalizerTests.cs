namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkRunFinalizerTests
{
    [Fact]
    public void Finalizer_CleanRun_ComputesIndicesAndCompletedStatus()
    {
        var run = new BenchmarkRun
        {
            Id = 1,
            TotalQuestionCount = 2,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 1,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = 90,
                    SpeedScore = 80,
                    DurationMs = 2000,
                    Difficulty = BenchmarkDifficulty.Simple,
                    AssessedDifficulty = 30
                },
                new BenchmarkRunAnswer
                {
                    OrderIndex = 2,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = 70,
                    SpeedScore = 60,
                    DurationMs = 4000,
                    Difficulty = BenchmarkDifficulty.Intermediate,
                    AssessedDifficulty = 50
                }
            }
        };

        BenchmarkRunFinalizer.Apply(run, run.Answers);

        Assert.Equal(BenchmarkRunStatus.Completed, run.Status);
        Assert.Equal(2, run.AnsweredQuestionCount);
        Assert.Equal(0, run.DegradedAnswerCount);
        Assert.Equal(0, run.ToolStarvedAnswerCount);
        Assert.NotNull(run.QualityIndex);
        Assert.NotNull(run.SpeedIndex);
    }

    [Fact]
    public void Finalizer_WithEmptyAnswer_ExcludesFromIndicesAndSetsCompletedWithErrors()
    {
        var run = new BenchmarkRun
        {
            Id = 1,
            TotalQuestionCount = 2,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 1,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = 85,
                    SpeedScore = 90,
                    DurationMs = 1500,
                    Difficulty = BenchmarkDifficulty.Simple,
                    AssessedDifficulty = 25
                },
                new BenchmarkRunAnswer
                {
                    OrderIndex = 2,
                    Status = BenchmarkAnswerStatus.EmptyAnswer,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = null,
                    SpeedScore = null,
                    DurationMs = 300,
                    Difficulty = BenchmarkDifficulty.Intermediate,
                    AssessedDifficulty = 50,
                    AnswerFlags = (int)BenchmarkAnswerFlags.Empty
                }
            }
        };

        BenchmarkRunFinalizer.Apply(run, run.Answers);

        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, run.Status);
        // Empty answer is excluded from AnsweredQuestionCount
        Assert.Equal(1, run.AnsweredQuestionCount);
        Assert.Equal(1, run.DegradedAnswerCount);
        Assert.Equal(0, run.ToolStarvedAnswerCount);
        // Quality index is based only on question 1
        Assert.Equal(85, run.QualityIndex);
    }

    [Fact]
    public void Finalizer_WithToolBudgetExhausted_CountsToolStarvedAndSetsCompletedWithErrors()
    {
        var run = new BenchmarkRun
        {
            Id = 1,
            TotalQuestionCount = 1,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 1,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = 60,
                    SpeedScore = 40,
                    DurationMs = 15000,
                    Difficulty = BenchmarkDifficulty.Advanced,
                    AssessedDifficulty = 80,
                    ToolBudgetExhausted = true
                }
            }
        };

        BenchmarkRunFinalizer.Apply(run, run.Answers);

        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, run.Status);
        Assert.Equal(1, run.AnsweredQuestionCount);
        Assert.Equal(1, run.DegradedAnswerCount);
        Assert.Equal(1, run.ToolStarvedAnswerCount);
    }

    [Fact]
    public void Finalizer_WithAnswerFlags_CountsDegradedAndSetsCompletedWithErrors()
    {
        var run = new BenchmarkRun
        {
            Id = 1,
            TotalQuestionCount = 1,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 1,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = 75,
                    SpeedScore = 70,
                    DurationMs = 3000,
                    Difficulty = BenchmarkDifficulty.Intermediate,
                    AssessedDifficulty = 50,
                    AnswerFlags = (int)BenchmarkAnswerFlags.HarnessArtifacts
                }
            }
        };

        BenchmarkRunFinalizer.Apply(run, run.Answers);

        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, run.Status);
        Assert.Equal(1, run.AnsweredQuestionCount);
        Assert.Equal(1, run.DegradedAnswerCount);
        Assert.Equal(0, run.ToolStarvedAnswerCount);
    }
}
