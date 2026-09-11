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
    public void Finalizer_WithRecoveredArtifacts_SetsCompletedWithLimits()
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
                    AnswerText = "A graded answer, with the leaked payload already removed.",
                    AnswerFlags = (int)BenchmarkAnswerFlags.HarnessArtifacts
                }
            }
        };

        BenchmarkRunFinalizer.Apply(run, run.Answers);

        // The scrubber repaired this answer and it graded normally, so the run is valid. Calling
        // it CompletedWithErrors reported a healthy run as broken — on the 2026-09-03 Luna run,
        // five such answers scored 78 to 99 while the run was labelled errored and its
        // diagnostics said "ERRORS: none".
        Assert.Equal(BenchmarkRunStatus.CompletedWithLimits, run.Status);
        Assert.Equal(1, run.AnsweredQuestionCount);
        Assert.Equal(1, run.RecoveredAnswerCount);
        Assert.Equal(0, run.TransportDefectAnswerCount);
        Assert.Equal(1, run.DegradedAnswerCount);
        Assert.Equal(0, run.ToolStarvedAnswerCount);
    }

    [Fact]
    public void Finalizer_WithEmptyAnswer_StillSetsCompletedWithErrors()
    {
        var run = new BenchmarkRun
        {
            Id = 2,
            TotalQuestionCount = 1,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 1,
                    Status = BenchmarkAnswerStatus.EmptyAnswer,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    DurationMs = 3000,
                    Difficulty = BenchmarkDifficulty.Intermediate,
                    AssessedDifficulty = 50,
                    AnswerText = string.Empty,
                    AnswerFlags = (int)(BenchmarkAnswerFlags.HarnessArtifacts | BenchmarkAnswerFlags.Empty)
                }
            }
        };

        BenchmarkRunFinalizer.Apply(run, run.Answers);

        // An answer that did not survive the scrub is a genuine defect, not a recovery.
        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, run.Status);
        Assert.Equal(1, run.TransportDefectAnswerCount);
        Assert.Equal(0, run.RecoveredAnswerCount);
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
            // Non-empty: an answer with leaked artifacts counts as recovered only when text
            // survived the scrub, and an empty one is a transport defect instead.
            AnswerText = "Graded answer text.",
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
        var answer = MakeAnswer(1, BenchmarkAnswerFlags.Truncated, budgetExhausted: true);

        Assert.Equal(BenchmarkAnswerIntegrity.TransportDefect, BenchmarkRunFinalizer.Classify(answer));
        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors,
            BenchmarkRunFinalizer.ComputeStatus(new List<BenchmarkRunAnswer> { answer }));
    }

    [Fact]
    public void RecoveredArtifacts_TakePrecedenceOverHarnessLimit_AndDoNotFailTheRun()
    {
        var answer = MakeAnswer(1, BenchmarkAnswerFlags.HarnessArtifacts, budgetExhausted: true);

        Assert.Equal(BenchmarkAnswerIntegrity.Recovered, BenchmarkRunFinalizer.Classify(answer));
        Assert.True(BenchmarkRunFinalizer.WasRecovered(answer));
        Assert.False(BenchmarkRunFinalizer.HasTransportDefect(answer));
        Assert.Equal(BenchmarkRunStatus.CompletedWithLimits,
            BenchmarkRunFinalizer.ComputeStatus(new List<BenchmarkRunAnswer> { answer }));
    }

    [Fact]
    public void RecoveredArtifacts_WithNoSurvivingText_AreATransportDefect()
    {
        var answer = MakeAnswer(1, BenchmarkAnswerFlags.HarnessArtifacts | BenchmarkAnswerFlags.Empty);
        answer.AnswerText = string.Empty;

        Assert.Equal(BenchmarkAnswerIntegrity.TransportDefect, BenchmarkRunFinalizer.Classify(answer));
        Assert.False(BenchmarkRunFinalizer.WasRecovered(answer));
    }

    /// <summary>
    /// An answer the model itself ended without producing text: status EmptyAnswer plus a provider
    /// finish reason that means a normal stop. Both halves are required — an empty answer with no
    /// recorded reason is a transport defect.
    /// </summary>
    private static BenchmarkRunAnswer MakeUnansweredAnswer(int orderIndex, int assessedDifficulty = 50)
    {
        var answer = MakeAnswer(orderIndex, status: BenchmarkAnswerStatus.EmptyAnswer);
        answer.AnswerText = string.Empty;
        answer.ProviderFinishReason = "STOP";
        answer.AssessedDifficulty = assessedDifficulty;
        answer.QualityScore = 0;
        answer.RawQualityScore = 0;
        answer.Score = 0;
        answer.SpeedScore = null;
        answer.AnswerFlags = (int)BenchmarkAnswerFlags.Empty;
        return answer;
    }

    [Fact]
    public void IntegrityBuckets_PartitionEveryAnswerExactlyOnce()
    {
        // The invariant the old report violated: clean + transport defects + recovered +
        // harness limits + unanswered must equal the question count, whatever combination of
        // causes is present.
        var answers = new List<BenchmarkRunAnswer>
        {
            MakeAnswer(1),
            MakeAnswer(2, BenchmarkAnswerFlags.HarnessArtifacts),
            MakeAnswer(3, BenchmarkAnswerFlags.Truncated),
            MakeAnswer(4, budgetExhausted: true),
            MakeAnswer(5, BenchmarkAnswerFlags.ReasoningBleed),
            MakeAnswer(6, BenchmarkAnswerFlags.ReasoningBleed, budgetExhausted: true),
            MakeAnswer(7, BenchmarkAnswerFlags.HarnessArtifacts, budgetExhausted: true),
            MakeAnswer(8, status: BenchmarkAnswerStatus.EmptyAnswer),
            MakeUnansweredAnswer(9)
        };

        int clean = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.Clean);
        int defects = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.TransportDefect);
        int recovered = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.Recovered);
        int limits = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.HarnessLimit);
        int unanswered = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.Unanswered);

        Assert.Equal(answers.Count, clean + defects + recovered + limits + unanswered);
        Assert.Equal(2, clean);        // 1 and 5 (advisory only)
        Assert.Equal(2, defects);      // 3 (truncated) and 8 (empty, no finish reason recorded)
        Assert.Equal(2, recovered);    // 2 and 7 — repaired and graded
        Assert.Equal(2, limits);       // 4 and 6
        Assert.Equal(1, unanswered);   // 9 — the model stopped normally with no text
    }

    [Fact]
    public void Classify_UnansweredIsItsOwnBucket_AndNotATransportDefect()
    {
        var unanswered = MakeUnansweredAnswer(1);
        var emptyWithNoReason = MakeAnswer(2, status: BenchmarkAnswerStatus.EmptyAnswer);
        emptyWithNoReason.AnswerText = string.Empty;

        Assert.Equal(BenchmarkAnswerIntegrity.Unanswered, BenchmarkRunFinalizer.Classify(unanswered));
        Assert.False(BenchmarkRunFinalizer.HasTransportDefect(unanswered));

        // Attribution and severity are independent: the bucket says which failure it was, and
        // HasUnresolvedWork is what keeps both of them at CompletedWithErrors.
        Assert.True(BenchmarkRunFinalizer.HasUnresolvedWork(unanswered));

        Assert.Equal(BenchmarkAnswerIntegrity.TransportDefect, BenchmarkRunFinalizer.Classify(emptyWithNoReason));
        Assert.True(BenchmarkRunFinalizer.HasTransportDefect(emptyWithNoReason));
        Assert.True(BenchmarkRunFinalizer.HasUnresolvedWork(emptyWithNoReason));
    }

    [Fact]
    public void ComputeStatus_UnansweredStillProducesCompletedWithErrors()
    {
        // Item 6b: an empty answer is an error, not merely a low score, whatever produced it.
        var answers = new List<BenchmarkRunAnswer>
        {
            MakeAnswer(1),
            MakeUnansweredAnswer(2)
        };

        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, BenchmarkRunFinalizer.ComputeStatus(answers));
    }

    [Theory]
    [InlineData("STOP", true)]
    [InlineData("end_turn", true)]
    [InlineData("completed", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("MAX_TOKENS", false)]
    [InlineData("tool_use", false)]
    public void IsModelProducedEmptyAnswer_TrueOnNormalStop_FalseOnNullOrTruncated(string? finishReason, bool expected)
    {
        var answer = MakeAnswer(1, status: BenchmarkAnswerStatus.EmptyAnswer);
        answer.AnswerText = string.Empty;
        answer.ProviderFinishReason = finishReason;

        Assert.Equal(expected, BenchmarkRunFinalizer.IsModelProducedEmptyAnswer(answer));
    }

    [Fact]
    public void IsModelProducedEmptyAnswer_IsFalseForAnAnswerThatHasText()
    {
        var answered = MakeAnswer(1);
        answered.ProviderFinishReason = "STOP";

        Assert.False(BenchmarkRunFinalizer.IsModelProducedEmptyAnswer(answered));
        Assert.True(BenchmarkRunFinalizer.CountsTowardQualityIndex(answered));
    }

    [Fact]
    public void ApplyTotals_WritesMeasuredTotals_AndLeavesStatusAndScoresUntouched()
    {
        var completedAt = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var run = new BenchmarkRun
        {
            Id = 1,
            TotalQuestionCount = 18,
            Status = BenchmarkRunStatus.Canceled,
            CompletedAtUtc = completedAt,
            QualityIndex = null,
            SpeedIndex = null,
            TotalDurationMs = 0
        };

        var answers = new List<BenchmarkRunAnswer>
        {
            MakeAnswer(1),
            MakeAnswer(2),
            MakeAnswer(3),
            MakeUnansweredAnswer(4)
        };
        foreach (var a in answers)
        {
            a.InputTokens = 1000;
            a.OutputTokens = 100;
            a.DurationMs = 5000;
        }

        BenchmarkRunFinalizer.ApplyTotals(run, answers);

        // The totals sum over every answer that exists: an aborted run's cost is real whether or
        // not the answer that spent it graded.
        Assert.Equal(4000, run.TotalInputTokens);
        Assert.Equal(400, run.TotalOutputTokens);
        Assert.Equal(20000, run.TotalAnswerDurationMs);
        Assert.Equal(3, run.AnsweredQuestionCount);
        Assert.Equal(1, run.UnansweredQuestionCount);

        Assert.Equal(BenchmarkRunStatus.Canceled, run.Status);
        Assert.Equal(completedAt, run.CompletedAtUtc);
        Assert.Null(run.QualityIndex);
        Assert.Null(run.SpeedIndex);
        Assert.Equal(0, run.TotalDurationMs);
    }

    [Fact]
    public void ApplyTotals_OnNoAnswers_LeavesTotalsAtZero_AndToolOverheadNull()
    {
        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 18, Status = BenchmarkRunStatus.Canceled };

        BenchmarkRunFinalizer.ApplyTotals(run, new List<BenchmarkRunAnswer>());

        Assert.Equal(0, run.TotalInputTokens);
        Assert.Equal(0, run.TotalOutputTokens);
        Assert.Equal(0, run.TotalAnswerDurationMs);
        Assert.Equal(0, run.AnsweredQuestionCount);
        Assert.Equal(0, run.UnansweredQuestionCount);
        Assert.Null(run.ToolOverheadMs);
        Assert.Equal(BenchmarkRunStatus.Canceled, run.Status);
    }

    [Fact]
    public void Apply_StillComputesStatusAndIndices_AfterTotalsExtraction()
    {
        var a1 = MakeAnswer(1);
        a1.QualityScore = 90;
        a1.SpeedScore = 80;
        a1.AssessedDifficulty = 30;

        var a2 = MakeAnswer(2);
        a2.QualityScore = 70;
        a2.SpeedScore = 60;
        a2.AssessedDifficulty = 50;

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 2 };
        BenchmarkRunFinalizer.Apply(run, new[] { a1, a2 });

        Assert.Equal(BenchmarkRunStatus.Completed, run.Status);
        Assert.NotNull(run.CompletedAtUtc);
        Assert.Equal(2, run.AnsweredQuestionCount);
        Assert.Equal(78, run.QualityIndex);   // (30*90 + 50*70) / 80
        Assert.Equal(70, run.SpeedIndex);
        Assert.Equal(80, run.UnweightedQualityIndex);
    }

    [Fact]
    public void Apply_ScoresUnansweredAtZero_InQualityButNotSpeed()
    {
        var answered = MakeAnswer(1);
        answered.QualityScore = 80;
        answered.RawQualityScore = 80;
        answered.SpeedScore = 64;
        answered.AssessedDifficulty = 25;

        var unanswered = MakeUnansweredAnswer(2, assessedDifficulty: 25);

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 2 };
        BenchmarkRunFinalizer.Apply(run, new[] { answered, unanswered });

        Assert.Equal(40, run.QualityIndex);
        Assert.Equal(40, run.UnweightedQualityIndex);

        // The unanswered question has no SpeedScore, so the speed aggregate is the answered
        // question's alone. Counting one failure on two orthogonal axes would penalise it twice.
        Assert.Equal(64, run.SpeedIndex);
        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, run.Status);
        Assert.Equal(1, run.UnansweredQuestionCount);
        Assert.Equal(1, run.AnsweredQuestionCount);
    }

    [Fact]
    public void ScoringSites_AgreeOnAFixtureContainingAnUnansweredAnswer()
    {
        // Option B's central risk: six sites recompute a quality index, and one that filters on
        // Status == Ok alone would silently exclude the zeros the finaliser included.
        var a1 = MakeAnswer(1);
        a1.QualityScore = 80;
        a1.RawQualityScore = 80;
        a1.AssessedDifficulty = 25;

        var a2 = MakeAnswer(2);
        a2.QualityScore = 60;
        a2.RawQualityScore = 60;
        a2.AssessedDifficulty = 75;

        var unanswered = MakeUnansweredAnswer(3, assessedDifficulty: 50);
        var answers = new List<BenchmarkRunAnswer> { a1, a2, unanswered };

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 3, Answers = answers };
        BenchmarkRunFinalizer.Apply(run, answers);

        // 2: AdminBenchmarkController's RawQualityIndex expression.
        int? controllerRawIndex = BenchmarkScoring.QualityIndex(
            answers
                .Where(a => BenchmarkRunFinalizer.CountsTowardQualityIndex(a) && a.QualityScore.HasValue)
                .Select(a => (a.RawQualityScore ?? a.QualityScore, a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)))
                .ToList());

        // 3: BenchmarkReportBuilder's rawScorableItems selector.
        int? reportRawIndex = BenchmarkScoring.QualityIndex(
            answers
                .Where(a => BenchmarkRunFinalizer.CountsTowardQualityIndex(a) && a.QualityScore.HasValue)
                .Select(a => (a.RawQualityScore ?? a.QualityScore, a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)))
                .ToList());

        // 6: BenchmarkService.RescoreRunAsync's scorableItems selector.
        int? rescoreIndex = BenchmarkScoring.QualityIndex(
            answers
                .Where(a => BenchmarkRunFinalizer.CountsTowardQualityIndex(a) && a.QualityScore.HasValue)
                .Select(a => (a.QualityScore, a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)))
                .ToList());

        // (25*80 + 75*60 + 50*0) / 150 = 6500 / 150 = 43
        Assert.Equal(43, run.QualityIndex);
        Assert.Equal(run.QualityIndex, controllerRawIndex);
        Assert.Equal(run.QualityIndex, reportRawIndex);
        Assert.Equal(run.QualityIndex, rescoreIndex);
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
    [Fact]
    public void ContestedVerdict_IsAdvisory_NotATransportDefect()
    {
        var answer = MakeAnswer(1, BenchmarkAnswerFlags.ContestedVerdict);

        Assert.True(BenchmarkRunFinalizer.HasAdvisoryFlag(answer));
        Assert.False(BenchmarkRunFinalizer.HasTransportDefect(answer));
        Assert.Equal(BenchmarkAnswerIntegrity.Clean, BenchmarkRunFinalizer.Classify(answer));
    }

    [Fact]
    public void ContestedVerdict_AloneDoesNotDegradeTheRunStatus()
    {
        // Grouping it with the defect flags would flip every run carrying one to
        // CompletedWithErrors - the exact regression harness version 4 was written to undo. The
        // answer is intact and the verdict may well be right; what it is not is unambiguous.
        var answers = new[]
        {
            MakeAnswer(1, BenchmarkAnswerFlags.ContestedVerdict),
            MakeAnswer(2)
        };

        Assert.Equal(BenchmarkRunStatus.Completed, BenchmarkRunFinalizer.ComputeStatus(answers));
    }

    [Fact]
    public void Apply_CountsContestedVerdictsAndReassessments_AndTheUnweightedMean()
    {
        var a1 = MakeAnswer(1, BenchmarkAnswerFlags.ContestedVerdict);
        a1.QualityScore = 60;
        a1.AssessedDifficulty = 25;

        var a2 = MakeAnswer(2);
        a2.QualityScore = 100;
        a2.AssessedDifficulty = 90;
        a2.ReassessmentCount = 1;
        a2.PreviousQualityScore = 70;

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 2 };
        BenchmarkRunFinalizer.Apply(run, new[] { a1, a2 });

        Assert.Equal(1, run.ContestedVerdictAnswerCount);
        Assert.Equal(1, run.ReassessedAnswerCount);

        // Plain mean 80; difficulty-weighted (25*60 + 90*100) / 115 = 91. The gap is exactly the
        // effect the two figures exist to expose.
        Assert.Equal(80, run.UnweightedQualityIndex);
        Assert.Equal(91, run.QualityIndex);
    }

    [Fact]
    public void Apply_ComputesGraderAgreement_AndExcludesManualTrialVerdicts()
    {
        // Agreement measures the run's own two graders. A Manual verdict comes from a third
        // model an operator picked by hand for a trial, and folding it in would contaminate the
        // figure the assessor decision rests on.
        var graded = MakeAnswer(1);
        graded.QualityScore = 90;
        graded.SecondOpinionQualityScore = 80;
        graded.SecondOpinionTrigger = "All";

        var alsoGraded = MakeAnswer(2);
        alsoGraded.QualityScore = 60;
        alsoGraded.SecondOpinionQualityScore = 78;
        alsoGraded.SecondOpinionTrigger = "All";

        var trial = MakeAnswer(3);
        trial.QualityScore = 95;
        trial.SecondOpinionQualityScore = 20;
        trial.SecondOpinionTrigger = "Manual";

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 3 };
        BenchmarkRunFinalizer.Apply(run, new[] { graded, alsoGraded, trial });

        Assert.Equal(2, run.SecondOpinionGradedAnswerCount);
        Assert.Equal(14.0, run.SecondOpinionMeanAbsDelta);
        Assert.Equal(4.0, run.SecondOpinionMeanSignedDelta);
    }

    [Fact]
    public void Apply_ComputesSignedDeltaAndCriticalErrorSplits_MatchingHarness12Spec()
    {
        // 3 second opinions with deltas -45, -31, -72:
        // abs delta mean = (45 + 31 + 72)/3 = 148 / 3 ≈ 49.333
        // signed delta mean = (-45 + -31 + -72)/3 = -148 / 3 ≈ -49.333
        // 2 critical error flips
        var a1 = MakeAnswer(1);
        a1.QualityScore = 90;
        a1.SecondOpinionQualityScore = 45; // delta -45
        a1.SecondOpinionTrigger = "FlaggedAndOutliers";
        a1.CriticalError = false;
        a1.SecondOpinionCriticalError = true; // flip 1

        var a2 = MakeAnswer(2);
        a2.QualityScore = 85;
        a2.SecondOpinionQualityScore = 54; // delta -31
        a2.SecondOpinionTrigger = "FlaggedAndOutliers";
        a2.CriticalError = true;
        a2.SecondOpinionCriticalError = false; // flip 2

        var a3 = MakeAnswer(3);
        a3.QualityScore = 95;
        a3.SecondOpinionQualityScore = 23; // delta -72
        a3.SecondOpinionTrigger = "FlaggedAndOutliers";
        a3.CriticalError = false;
        a3.SecondOpinionCriticalError = false; // no flip

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 3 };
        BenchmarkRunFinalizer.Apply(run, new[] { a1, a2, a3 });

        Assert.Equal(3, run.SecondOpinionGradedAnswerCount);
        Assert.NotNull(run.SecondOpinionMeanAbsDelta);
        Assert.NotNull(run.SecondOpinionMeanSignedDelta);
        Assert.InRange(run.SecondOpinionMeanAbsDelta.Value, 49.3, 49.4);
        Assert.InRange(run.SecondOpinionMeanSignedDelta.Value, -49.4, -49.3);
        Assert.Equal(2, run.SecondOpinionCriticalErrorSplitCount);
    }

    [Fact]
    public void Apply_LeavesAgreementNull_WhenNoSecondVerdictWasProduced()
    {
        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 1 };
        BenchmarkRunFinalizer.Apply(run, new[] { MakeAnswer(1) });

        Assert.Equal(0, run.SecondOpinionGradedAnswerCount);
        Assert.Null(run.SecondOpinionMeanAbsDelta);
    }

    [Fact]
    public void Finalizer_CountsUnevidencedDeductionsAndAdvisoryFlags()
    {
        var a1 = MakeAnswer(1, BenchmarkAnswerFlags.UnevidencedDeduction);
        var a2 = MakeAnswer(2);
        var a3 = MakeAnswer(3);

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 3 };
        BenchmarkRunFinalizer.Apply(run, new[] { a1, a2, a3 });

        Assert.Equal(1, run.UnevidencedDeductionAnswerCount);
        Assert.Equal(1, run.AdvisoryFlagAnswerCount);
        Assert.True(BenchmarkRunFinalizer.HasAdvisoryFlag(a1));
        Assert.False(BenchmarkRunFinalizer.HasTransportDefect(a1));
        Assert.Equal(BenchmarkAnswerIntegrity.Clean, BenchmarkRunFinalizer.Classify(a1));
    }

    [Fact]
    public void Finalizer_CountsContestedCriticalErrors_AsAdvisoryOnly()
    {
        // The verifier supported the quote the critical error rested on. That is a statement about
        // the grading, not a regrade: the cap stays where it was, the answer stays Clean and the run
        // stays Completed.
        var a1 = MakeAnswer(1, BenchmarkAnswerFlags.ContestedCriticalError);
        a1.CriticalError = true;
        a1.CriticalErrorQuote = "Gnolls are immune to lycanthropy.";
        var a2 = MakeAnswer(2);
        var a3 = MakeAnswer(3);
        var answers = new[] { a1, a2, a3 };

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 3 };
        BenchmarkRunFinalizer.Apply(run, answers);

        Assert.Equal(1, run.ContestedCriticalErrorAnswerCount);
        Assert.Equal(1, run.AdvisoryFlagAnswerCount);
        Assert.True(BenchmarkRunFinalizer.HasAdvisoryFlag(a1));
        Assert.False(BenchmarkRunFinalizer.HasTransportDefect(a1));
        Assert.Equal(BenchmarkAnswerIntegrity.Clean, BenchmarkRunFinalizer.Classify(a1));
        Assert.All(answers, a => Assert.Equal(BenchmarkAnswerIntegrity.Clean, BenchmarkRunFinalizer.Classify(a)));
        Assert.Equal(BenchmarkRunStatus.Completed, BenchmarkRunFinalizer.ComputeStatus(answers));
        Assert.Equal(BenchmarkRunStatus.Completed, run.Status);
    }

    [Fact]
    public void Finalizer_LeavesContestedCriticalErrorCountAtZero_WhenNoAnswerCarriesTheFlag()
    {
        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 1 };
        BenchmarkRunFinalizer.Apply(run, new[] { MakeAnswer(1) });

        Assert.Equal(0, run.ContestedCriticalErrorAnswerCount);
    }

    [Fact]
    public void Finalizer_AggregatesOmissionAsAccuracy_AndBudgetSaturated_AndStandardError()
    {
        var a1 = MakeAnswer(1, BenchmarkAnswerFlags.OmissionAsAccuracy);
        a1.QualityScore = 70;
        a1.ToolCallCount = 20;
        a1.ToolCallBudgetUsed = 25;
        a1.ToolCallsBlocked = 0;

        // a2 is budget saturated: exactly 100% used with 0 calls blocked
        var a2 = MakeAnswer(2);
        a2.QualityScore = 85;
        a2.ToolCallCount = 35;
        a2.ToolCallBudgetUsed = 35;
        a2.ToolCallsBlocked = 0;

        var a3 = MakeAnswer(3);
        a3.QualityScore = 95;
        a3.ToolCallCount = 10;
        a3.ToolCallBudgetUsed = 25;
        a3.ToolCallsBlocked = 0;

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 3 };
        BenchmarkRunFinalizer.Apply(run, new[] { a1, a2, a3 });

        Assert.Equal(1, run.OmissionAsAccuracyAnswerCount);
        Assert.Equal(1, run.BudgetSaturatedAnswerCount);
        Assert.Equal(1, run.AdvisoryFlagAnswerCount);
        Assert.True(BenchmarkRunFinalizer.HasAdvisoryFlag(a1));
        Assert.NotNull(run.QualityIndexStandardError);
        Assert.True(run.QualityIndexStandardError > 0);
    }

    // --- Terminal failures as transport defects (run-29) ---

    [Theory]
    [InlineData(BenchmarkAnswerStatus.Failed)]
    [InlineData(BenchmarkAnswerStatus.ProviderError)]
    public void FailedOrProviderError_ClassifyAsTransportDefect_ThroughHasTerminalFailure(BenchmarkAnswerStatus status)
    {
        // Before HasTerminalFailure existed, neither status matched any bucket check and both
        // fell through to Clean, which let a run with a dead question report itself 100% clean.
        var answer = MakeAnswer(1, status: status);

        Assert.True(BenchmarkRunFinalizer.HasTerminalFailure(answer));
        Assert.True(BenchmarkRunFinalizer.HasTransportDefect(answer));
        Assert.Equal(BenchmarkAnswerIntegrity.TransportDefect, BenchmarkRunFinalizer.Classify(answer));
    }

    // --- Re-run selection (harness 21) ---

    [Theory]
    [InlineData(BenchmarkAnswerStatus.ProviderError, null)]
    [InlineData(BenchmarkAnswerStatus.Failed, null)]
    [InlineData(BenchmarkAnswerStatus.EmptyAnswer, null)]
    [InlineData(BenchmarkAnswerStatus.EmptyAnswer, "end_turn")]
    public void NeedsReExecution_IsTrueForProviderErrorFailedAndEmptyAnswer(
        BenchmarkAnswerStatus status, string? finishReason)
    {
        // The empty cases matter most: a model-produced empty answer carries a normal finish reason
        // and is scored 0, and a cancelled or dropped one carries none. Both are unanswered questions,
        // and the client's re-run scope has always listed them.
        var answer = MakeAnswer(1, status: status);
        answer.AnswerText = string.Empty;
        answer.ProviderFinishReason = finishReason;

        Assert.True(BenchmarkRunFinalizer.NeedsReExecution(answer));
    }

    [Fact]
    public void NeedsReExecution_IsFalseForOkAnswer()
    {
        Assert.False(BenchmarkRunFinalizer.NeedsReExecution(MakeAnswer(1)));
    }

    [Fact]
    public void IntegrityBuckets_WithTerminalFailures_StillSumToTheAnswerCount()
    {
        var failed = MakeAnswer(1, status: BenchmarkAnswerStatus.Failed);
        var providerError = MakeAnswer(2, status: BenchmarkAnswerStatus.ProviderError);
        var unanswered = MakeUnansweredAnswer(3);
        unanswered.ProviderFinishReason = "end_turn";
        var recovered = MakeAnswer(4, BenchmarkAnswerFlags.HarnessArtifacts);
        var budgetExhausted = MakeAnswer(5, budgetExhausted: true);
        var clean = MakeAnswer(6);

        var answers = new List<BenchmarkRunAnswer>
        {
            failed, providerError, unanswered, recovered, budgetExhausted, clean
        };

        Assert.Equal(BenchmarkAnswerIntegrity.TransportDefect, BenchmarkRunFinalizer.Classify(failed));
        Assert.Equal(BenchmarkAnswerIntegrity.TransportDefect, BenchmarkRunFinalizer.Classify(providerError));
        Assert.Equal(BenchmarkAnswerIntegrity.Unanswered, BenchmarkRunFinalizer.Classify(unanswered));
        Assert.Equal(BenchmarkAnswerIntegrity.Recovered, BenchmarkRunFinalizer.Classify(recovered));
        Assert.Equal(BenchmarkAnswerIntegrity.HarnessLimit, BenchmarkRunFinalizer.Classify(budgetExhausted));
        Assert.Equal(BenchmarkAnswerIntegrity.Clean, BenchmarkRunFinalizer.Classify(clean));

        int clean_ = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.Clean);
        int defects = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.TransportDefect);
        int recovered_ = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.Recovered);
        int limits = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.HarnessLimit);
        int unanswered_ = answers.Count(a => BenchmarkRunFinalizer.Classify(a) == BenchmarkAnswerIntegrity.Unanswered);

        Assert.Equal(answers.Count, clean_ + defects + recovered_ + limits + unanswered_);
        Assert.Equal(1, clean_);
        Assert.Equal(2, defects);      // failed + providerError
        Assert.Equal(1, recovered_);
        Assert.Equal(1, limits);
        Assert.Equal(1, unanswered_);
    }

    [Theory]
    [InlineData(BenchmarkAnswerStatus.Failed)]
    [InlineData(BenchmarkAnswerStatus.ProviderError)]
    public void ComputeStatus_FailedOrProviderErrorAnswer_StaysCompletedWithErrors(BenchmarkAnswerStatus status)
    {
        // Pinned because the bucket change above must not move the run status: it was already
        // CompletedWithErrors through HasUnresolvedWork, and still is.
        var answers = new List<BenchmarkRunAnswer> { MakeAnswer(1, status: status), MakeAnswer(2) };

        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, BenchmarkRunFinalizer.ComputeStatus(answers));
    }

    [Fact]
    public void ApplyTotals_SecondOpinionAggregates_ExcludeAnswersOutsideTheQualityIndex()
    {
        // Run 29: a dead question (ProviderError) still carried a second-opinion verdict from
        // before the transport failure consumed it, and must not enter the agreement figure.
        var a1 = MakeAnswer(1);
        a1.QualityScore = 90;
        a1.SecondOpinionQualityScore = 80;
        a1.SecondOpinionTrigger = "All";

        var a2 = MakeAnswer(2);
        a2.QualityScore = 60;
        a2.SecondOpinionQualityScore = 78;
        a2.SecondOpinionTrigger = "All";

        var a3 = MakeAnswer(3);
        a3.QualityScore = 95;
        a3.SecondOpinionQualityScore = 85;
        a3.SecondOpinionTrigger = "All";

        var deadQuestion = MakeAnswer(4, status: BenchmarkAnswerStatus.ProviderError);
        deadQuestion.QualityScore = 50;
        deadQuestion.SecondOpinionQualityScore = 10;
        deadQuestion.SecondOpinionTrigger = "All";

        // Still excluded on its own terms: a Manual trial verdict, even on an otherwise
        // gradeable answer, must not enter the figure either.
        var manualTrial = MakeAnswer(5);
        manualTrial.QualityScore = 95;
        manualTrial.SecondOpinionQualityScore = 20;
        manualTrial.SecondOpinionTrigger = "Manual";

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 5 };
        BenchmarkRunFinalizer.ApplyTotals(run, new[] { a1, a2, a3, deadQuestion, manualTrial });

        Assert.Equal(3, run.SecondOpinionGradedAnswerCount);
        // Deltas (SecondOpinion - Quality) over a1..a3 alone: -10, 18, -10 → 12.67 and -0.67,
        // stored at one decimal.
        Assert.Equal(12.7, run.SecondOpinionMeanAbsDelta);
        Assert.Equal(-0.7, run.SecondOpinionMeanSignedDelta);
    }

    [Fact]
    public void Apply_StoresAgreementDeltasAtOneDecimal_MidpointAwayFromZero()
    {
        // Run 33: deltas -30, +1, +12, +16. The signed mean is exactly -0.25, which toFixed(1)
        // and F1 would otherwise print differently; stored once, rounded away from zero.
        var answers = new[] { (1, 88, 58), (2, 83, 84), (3, 87, 99), (4, 60, 76) }
            .Select(t =>
            {
                var a = MakeAnswer(t.Item1);
                a.QualityScore = t.Item2;
                a.SecondOpinionQualityScore = t.Item3;
                a.SecondOpinionTrigger = "FlaggedPlusSample";
                return a;
            })
            .ToArray();

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 4 };
        BenchmarkRunFinalizer.Apply(run, answers);

        Assert.Equal(4, run.SecondOpinionGradedAnswerCount);
        Assert.Equal(-0.3, run.SecondOpinionMeanSignedDelta);
        Assert.Equal(14.8, run.SecondOpinionMeanAbsDelta);
    }

    [Fact]
    public void Apply_NullsSpeedScoreOnNonGradeableAnswer_ButLeavesSpeedIndexUnchanged()
    {
        var gradeable = MakeAnswer(1);
        gradeable.QualityScore = 80;
        gradeable.SpeedScore = 70;
        gradeable.AssessedDifficulty = 50;

        // Non-gradeable, but not a terminal failure: an EmptyAnswer with no recorded finish reason
        // is a transport defect (HasTransportDefect true) yet HasTerminalFailure is false, so this
        // case is decoupled from the index-withholding behaviour below.
        var nonGradeable = MakeAnswer(2, status: BenchmarkAnswerStatus.EmptyAnswer);
        nonGradeable.AnswerText = string.Empty;
        nonGradeable.SpeedScore = 999;

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 2 };
        BenchmarkRunFinalizer.Apply(run, new[] { gradeable, nonGradeable });

        Assert.Equal(70, gradeable.SpeedScore);
        Assert.Null(nonGradeable.SpeedScore);
        // SpeedIndex's own filter already excluded a non-Ok answer, so nulling the stored score
        // here moves no published index. That is why the change carries no scoring-version bump.
        Assert.Equal(70, run.SpeedIndex);
    }

    // --- Index withholding on a terminal failure (harness version 21) ---

    [Fact]
    public void ApplyTotals_SetsTerminalFailureAnswerCount()
    {
        var ok = MakeAnswer(1);
        var failed = MakeAnswer(2, status: BenchmarkAnswerStatus.Failed);
        var providerError = MakeAnswer(3, status: BenchmarkAnswerStatus.ProviderError);

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 3 };
        BenchmarkRunFinalizer.ApplyTotals(run, new[] { ok, failed, providerError });

        Assert.Equal(2, run.TerminalFailureAnswerCount);
    }

    [Fact]
    public void ApplyTotals_LeavesTerminalFailureAnswerCountAtZero_WhenNoAnswerHasOne()
    {
        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 1 };
        BenchmarkRunFinalizer.ApplyTotals(run, new[] { MakeAnswer(1) });

        Assert.Equal(0, run.TerminalFailureAnswerCount);
    }

    [Theory]
    [InlineData(BenchmarkAnswerStatus.Failed)]
    [InlineData(BenchmarkAnswerStatus.ProviderError)]
    public void Apply_WithholdsQualityAndSpeedIndices_WhenAnAnswerHasATerminalFailure(BenchmarkAnswerStatus status)
    {
        var ok = MakeAnswer(1);
        ok.QualityScore = 90;
        ok.SpeedScore = 80;
        ok.AssessedDifficulty = 50;

        var failed = MakeAnswer(2, status: status);

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 2 };
        BenchmarkRunFinalizer.Apply(run, new[] { ok, failed });

        Assert.Equal(1, run.TerminalFailureAnswerCount);
        Assert.Null(run.QualityIndex);
        Assert.Null(run.QualityIndexStandardError);
        Assert.Null(run.UnweightedQualityIndex);
        Assert.Null(run.SpeedIndex);
        // The index withholding is presentation only: the run still reports what it reports today
        // for a dead question, through the existing HasUnresolvedWork path.
        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, run.Status);
    }

    [Fact]
    public void Apply_KeepsIndices_WhenCompletedWithErrorsHasNoTerminalFailure()
    {
        // A run stopped only by a model-produced empty answer (scored 0 under scoring method 10)
        // is CompletedWithErrors through HasUnresolvedWork, but carries no terminal failure, so its
        // indexes must not be withheld.
        var ok = MakeAnswer(1);
        ok.QualityScore = 80;
        ok.SpeedScore = 70;
        ok.AssessedDifficulty = 25;

        var unanswered = MakeUnansweredAnswer(2, assessedDifficulty: 25);

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 2 };
        BenchmarkRunFinalizer.Apply(run, new[] { ok, unanswered });

        Assert.Equal(0, run.TerminalFailureAnswerCount);
        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, run.Status);
        Assert.NotNull(run.QualityIndex);
        Assert.NotNull(run.SpeedIndex);
    }

    [Fact]
    public void Apply_KeepsIndices_OnACleanRunWithNoTerminalFailure()
    {
        var a1 = MakeAnswer(1);
        a1.QualityScore = 90;
        a1.SpeedScore = 80;
        a1.AssessedDifficulty = 30;

        var a2 = MakeAnswer(2);
        a2.QualityScore = 70;
        a2.SpeedScore = 60;
        a2.AssessedDifficulty = 50;

        // Three scored items, because the standard error is defined only from three up.
        var a3 = MakeAnswer(3);
        a3.QualityScore = 85;
        a3.SpeedScore = 75;
        a3.AssessedDifficulty = 40;

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 3 };
        BenchmarkRunFinalizer.Apply(run, new[] { a1, a2, a3 });

        Assert.Equal(0, run.TerminalFailureAnswerCount);
        Assert.NotNull(run.QualityIndex);
        Assert.NotNull(run.QualityIndexStandardError);
        Assert.NotNull(run.UnweightedQualityIndex);
        Assert.NotNull(run.SpeedIndex);
    }

    [Fact]
    public void Apply_PreserveCompletedAtTrue_KeepsAnAlreadySetCompletedAtUtc()
    {
        var originalCompletedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 1, CompletedAtUtc = originalCompletedAt };

        BenchmarkRunFinalizer.Apply(run, new[] { MakeAnswer(1) }, preserveCompletedAt: true);

        Assert.Equal(originalCompletedAt, run.CompletedAtUtc);
    }

    [Fact]
    public void Apply_PreserveCompletedAtTrue_SetsItWhenPreviouslyNull()
    {
        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 1, CompletedAtUtc = null };

        BenchmarkRunFinalizer.Apply(run, new[] { MakeAnswer(1) }, preserveCompletedAt: true);

        Assert.NotNull(run.CompletedAtUtc);
    }

    [Fact]
    public void Apply_DefaultOverload_AlwaysOverwritesCompletedAtUtc()
    {
        // The optional parameter exists for the failed-question re-run path; the main run path
        // calls the default overload and must stay unconditional.
        var originalCompletedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 1, CompletedAtUtc = originalCompletedAt };

        BenchmarkRunFinalizer.Apply(run, new[] { MakeAnswer(1) });

        Assert.NotEqual(originalCompletedAt, run.CompletedAtUtc);
    }

    [Theory]
    [InlineData(BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction)]
    [InlineData(BenchmarkAnswerFlags.AnswerFramingOpener)]
    public void HasAdvisoryFlag_TrueForTheTwoNewFlags_AndDoesNotAffectStatusOrCleanCount(BenchmarkAnswerFlags flag)
    {
        var answer = MakeAnswer(1, flag);
        var answers = new[] { answer, MakeAnswer(2) };

        Assert.True(BenchmarkRunFinalizer.HasAdvisoryFlag(answer));
        Assert.Equal(BenchmarkAnswerIntegrity.Clean, BenchmarkRunFinalizer.Classify(answer));
        // The assertion that matters: a previous round's regression was exactly an advisory flag
        // flipping healthy runs to CompletedWithErrors.
        Assert.Equal(BenchmarkRunStatus.Completed, BenchmarkRunFinalizer.ComputeStatus(answers));
    }

    [Fact]
    public void ApplyTotals_CountsOutOfRubricAccuracyAndAnswerFramingOpenerFlags()
    {
        var a1 = MakeAnswer(1, BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction);
        var a2 = MakeAnswer(2, BenchmarkAnswerFlags.AnswerFramingOpener);
        var a3 = MakeAnswer(3);

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 3 };
        BenchmarkRunFinalizer.ApplyTotals(run, new[] { a1, a2, a3 });

        Assert.Equal(1, run.OutOfRubricAccuracyAnswerCount);
        Assert.Equal(1, run.AnswerFramingOpenerAnswerCount);
    }

    [Fact]
    public void Finalizer_CountsContestedAccuracyDeductions_AsAdvisoryOnly()
    {
        // The verifier refuted the statement an out-of-rubric Accuracy deduction rested on. A
        // statement about the grading, not a regrade: the answer stays Clean and the run Completed.
        var a1 = MakeAnswer(1, BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction | BenchmarkAnswerFlags.ContestedAccuracyDeduction);
        var a2 = MakeAnswer(2, BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction);
        var a3 = MakeAnswer(3);
        var answers = new[] { a1, a2, a3 };

        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 3, HarnessVersion = "20" };
        BenchmarkRunFinalizer.Apply(run, answers);

        Assert.Equal(1, run.ContestedAccuracyDeductionAnswerCount);
        Assert.Equal(2, run.OutOfRubricAccuracyAnswerCount);
        Assert.Equal(2, run.AdvisoryFlagAnswerCount);
        Assert.True(BenchmarkRunFinalizer.HasAdvisoryFlag(MakeAnswer(4, BenchmarkAnswerFlags.ContestedAccuracyDeduction)));
        Assert.All(answers, a => Assert.Equal(BenchmarkAnswerIntegrity.Clean, BenchmarkRunFinalizer.Classify(a)));
        Assert.Equal(BenchmarkRunStatus.Completed, BenchmarkRunFinalizer.ComputeStatus(answers));
        Assert.Equal(BenchmarkRunStatus.Completed, run.Status);
    }

    [Fact]
    public void Finalizer_RecordsZeroContestedAccuracyDeductions_OnAHarness20RunWithoutTheFlag()
    {
        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 1, HarnessVersion = "20" };
        BenchmarkRunFinalizer.Apply(run, new[] { MakeAnswer(1) });

        Assert.Equal(0, run.ContestedAccuracyDeductionAnswerCount);
    }

    [Fact]
    public void Finalizer_LeavesContestedAccuracyDeductionCountNull_OnARunBeforeHarness20()
    {
        // Harness 19 never adjudicated the basis, so re-finalizing one of its runs must not turn
        // "not recorded" into a zero.
        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 1, HarnessVersion = "19" };
        BenchmarkRunFinalizer.Apply(run, new[] { MakeAnswer(1, BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction) });

        Assert.Null(run.ContestedAccuracyDeductionAnswerCount);
    }

    // -----------------------------------------------------------------------
    // IsAbortedRun: "stopped before finishing its suite" is answer-row coverage, not the status
    // alone. A cancelled retry of a finished run, and a run cancelled during its grading stages,
    // both leave every answer row in place and are re-runnable.
    // -----------------------------------------------------------------------

    [Fact]
    public void IsAbortedRun_CanceledWithFullAnswerCoverage_IsNotAborted()
    {
        Assert.False(BenchmarkRunFinalizer.IsAbortedRun(BenchmarkRunStatus.Canceled, 18, 18));
    }

    [Fact]
    public void IsAbortedRun_CanceledWithPartialCoverage_IsAborted()
    {
        Assert.True(BenchmarkRunFinalizer.IsAbortedRun(BenchmarkRunStatus.Canceled, 18, 7));
    }

    [Fact]
    public void IsAbortedRun_FailedWithUnknownTotal_IsAborted()
    {
        // TotalQuestionCount 0 means the count was never recorded. "Not covered" is the safe
        // reading: such a row stays refused exactly as it was before the coverage test existed.
        Assert.True(BenchmarkRunFinalizer.IsAbortedRun(BenchmarkRunStatus.Failed, 0, 18));
    }

    [Fact]
    public void IsAbortedRun_CompletedWithErrors_IsNeverAborted()
    {
        Assert.False(BenchmarkRunFinalizer.IsAbortedRun(BenchmarkRunStatus.CompletedWithErrors, 18, 18));
        Assert.False(BenchmarkRunFinalizer.IsAbortedRun(BenchmarkRunStatus.CompletedWithErrors, 18, 3));
    }

    [Fact]
    public void IsAbortedRun_ReadsTheAnswerRowCount_NotTheAnsweredQuestionCount()
    {
        // All 18 rows exist; 17 of them failed at the provider. The run covers its suite.
        var run = new BenchmarkRun { Id = 1, TotalQuestionCount = 18, Status = BenchmarkRunStatus.Canceled };
        var answers = new List<BenchmarkRunAnswer>();
        for (int i = 1; i <= 18; i++)
        {
            answers.Add(new BenchmarkRunAnswer
            {
                OrderIndex = i,
                Status = i == 1 ? BenchmarkAnswerStatus.Ok : BenchmarkAnswerStatus.ProviderError
            });
        }

        Assert.False(BenchmarkRunFinalizer.IsAbortedRun(run, answers));
    }
}
