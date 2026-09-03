namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
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
    public void Finalizer_WithToolBudgetExhausted_CountsToolStarvedAndSetsCompletedWithLimits()
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

        // Reaching a configured cap is not an error: the answer is valid and the cap may simply
        // need raising. Reporting it as CompletedWithErrors made a healthy run look broken.
        Assert.Equal(BenchmarkRunStatus.CompletedWithLimits, run.Status);
        Assert.Equal(1, run.AnsweredQuestionCount);
        Assert.Equal(1, run.DegradedAnswerCount);
        Assert.Equal(1, run.ToolStarvedAnswerCount);
        Assert.Equal(0, run.TransportDefectAnswerCount);
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
    // --- Integrity partition (Phase C1) ---

    private static BenchmarkRunAnswer MakeAnswer(
        int orderIndex,
        BenchmarkAnswerFlags flags = BenchmarkAnswerFlags.None,
        bool budgetExhausted = false,
        BenchmarkAnswerStatus status = BenchmarkAnswerStatus.Ok)
    {
        return new BenchmarkRunAnswer
        {
            OrderIndex = orderIndex,
            Status = status,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            QualityScore = 80,
            SpeedScore = 70,
            DurationMs = 20000,
            Difficulty = BenchmarkDifficulty.Intermediate,
            AssessedDifficulty = 50,
            AnswerFlags = (int)flags,
            ToolBudgetExhausted = budgetExhausted
        };
    }

    [Fact]
    public void AdvisoryFlags_DoNotAffectRunStatusOrCleanCount()
    {
        // ReasoningBleed and RepeatedFragments describe text that was removed before grading,
        // so the graded answer is unaffected and the run is not degraded by them.
        var answers = new List<BenchmarkRunAnswer>
        {
            MakeAnswer(1, BenchmarkAnswerFlags.ReasoningBleed),
            MakeAnswer(2, BenchmarkAnswerFlags.RepeatedFragments)
        };

        Assert.Equal(BenchmarkRunStatus.Completed, BenchmarkRunFinalizer.ComputeStatus(answers));
        Assert.All(answers, a => Assert.Equal(BenchmarkAnswerIntegrity.Clean, BenchmarkRunFinalizer.Classify(a)));
        Assert.All(answers, a => Assert.True(BenchmarkRunFinalizer.HasAdvisoryFlag(a)));
        Assert.All(answers, a => Assert.False(BenchmarkRunFinalizer.HasTransportDefect(a)));
    }

    [Fact]
    public void TransportDefect_TakesPrecedenceOverHarnessLimit()
    {
        var answer = MakeAnswer(1, BenchmarkAnswerFlags.HarnessArtifacts, budgetExhausted: true);

        Assert.Equal(BenchmarkAnswerIntegrity.TransportDefect, BenchmarkRunFinalizer.Classify(answer));
        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors,
            BenchmarkRunFinalizer.ComputeStatus(new List<BenchmarkRunAnswer> { answer }));
    }

    [Fact]
    public void IntegrityBuckets_PartitionEveryAnswerExactlyOnce()
    {
        // The invariant the old report violated: clean + transport defects + harness limits must
        // equal the question count, whatever combination of causes is present.
        var answers = new List<BenchmarkRunAnswer>
        {
            MakeAnswer(1),
            MakeAnswer(2, BenchmarkAnswerFlags.HarnessArtifacts),
            MakeAnswer(3, BenchmarkAnswerFlags.Truncated),
            MakeAnswer(4, budgetExhausted: true),
            MakeAnswer(5, BenchmarkAnswerFlags.ReasoningBleed),
            MakeAnswer(6, BenchmarkAnswerFlags.ReasoningBleed, budgetExhausted: true),
            MakeAnswer(7, BenchmarkAnswerFlags.HarnessArtifacts, budgetExhausted: true),
            MakeAnswer(8, status: BenchmarkAnswerStatus.EmptyAnswer)
        };

        int clean = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.Clean);
        int defects = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.TransportDefect);
        int limits = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.HarnessLimit);

        Assert.Equal(answers.Count, clean + defects + limits);
        Assert.Equal(2, clean);    // 1 and 5 (advisory only)
        Assert.Equal(4, defects);  // 2, 3, 7, 8
        Assert.Equal(2, limits);   // 4 and 6
    }

    [Fact]
    public void Finalizer_SumsToolTimeIntoRunOverhead()
    {
        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 2 };
        var a1 = MakeAnswer(1);
        var a2 = MakeAnswer(2);
        a1.ToolTimeMs = 4000;
        a2.ToolTimeMs = 6000;
        run.Answers = new List<BenchmarkRunAnswer> { a1, a2 };

        BenchmarkRunFinalizer.Apply(run, run.Answers);

        Assert.Equal(10000, run.ToolOverheadMs);
        Assert.Equal(16000, a1.ModelTimeMs);
    }

    [Fact]
    public void Finalizer_LeavesToolOverheadNullWhenNotRecorded()
    {
        // Runs predating harness version 3 have no tool timings; the report must be able to say
        // so rather than reporting zero overhead, which would be a false claim.
        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 1 };
        run.Answers = new List<BenchmarkRunAnswer> { MakeAnswer(1) };

        BenchmarkRunFinalizer.Apply(run, run.Answers);

        Assert.Null(run.ToolOverheadMs);
    }
}
