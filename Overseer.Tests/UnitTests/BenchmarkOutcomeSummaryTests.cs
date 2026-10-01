namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The method-13 outcome classes, the run's outcome figures, and the Wilson interval they use.
/// </summary>
public class BenchmarkOutcomeSummaryTests
{
    private static BenchmarkRun Run(int method = 13, bool panel = false) => new()
    {
        Id = 60,
        ScoringMethodVersion = method,
        CoAssessorModelConfigurationId = panel ? 7 : null
    };

    /// <summary>A single-assessor answer, resolved as the finalizer resolves it.</summary>
    private static BenchmarkRunAnswer Single(
        int orderIndex,
        int accuracy,
        int completeness,
        bool criticalError = false,
        bool? notAttempted = false)
    {
        return new BenchmarkRunAnswer
        {
            Id = orderIndex,
            OrderIndex = orderIndex,
            QuestionText = "Question?",
            AnswerText = "Graded answer text.",
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            AccuracyLevel = accuracy,
            CompletenessLevel = completeness,
            ConcisenessLevel = 5,
            ReadabilityLevel = 5,
            QualityScore = 60,
            CriticalError = criticalError,
            NotAttempted = notAttempted,
            CriticalErrorResolution = criticalError
                ? BenchmarkCriticalErrorResolution.SingleAssessor
                : BenchmarkCriticalErrorResolution.None
        };
    }

    /// <summary>A panel answer: member A's levels in the primary columns, member B's in its record.</summary>
    private static BenchmarkRunAnswer Panel(
        int orderIndex,
        (int Accuracy, int Completeness) a,
        (int Accuracy, int Completeness) b,
        bool flagA = false,
        bool flagB = false,
        bool? notAttemptedA = false,
        bool? notAttemptedB = false,
        BenchmarkCriticalErrorResolution resolution = BenchmarkCriticalErrorResolution.None)
    {
        var answer = Single(orderIndex, a.Accuracy, a.Completeness, flagA, notAttemptedA);
        answer.CriticalErrorResolution = resolution;
        answer.CoAssessmentStatus = BenchmarkAssessmentStatus.Scored;
        answer.CoAssessmentQualityScore = 60;
        answer.CoAssessmentCriticalError = flagB;
        answer.CoAssessmentNotAttempted = notAttemptedB;
        answer.CoAssessmentJson = new BenchmarkCoAssessmentRecord
        {
            AccuracyLevel = b.Accuracy,
            CompletenessLevel = b.Completeness,
            ConcisenessLevel = 5,
            ReadabilityLevel = 5,
            CriticalError = flagB,
            QualityScore = 60
        }.Serialize();
        return answer;
    }

    private static BenchmarkRunAnswer NoAnswer(int orderIndex) => new()
    {
        Id = orderIndex,
        OrderIndex = orderIndex,
        QuestionText = "Question?",
        AnswerText = string.Empty,
        Status = BenchmarkAnswerStatus.EmptyAnswer,
        ProviderFinishReason = "stop",
        AssessmentStatus = BenchmarkAssessmentStatus.Scored,
        QualityScore = 0
    };

    // --- Gate ---

    [Fact]
    public void BeforeMethod13_ThereIsNoSummaryAndNoClass()
    {
        var run = Run(method: 12);
        var answers = new List<BenchmarkRunAnswer> { Single(1, 6, 6) };

        Assert.Null(BenchmarkOutcomeSummary.Compute(run, answers));
        Assert.Null(BenchmarkOutcomeSummary.Classify(run, answers[0], isPanelRun: false));
    }

    // --- Single-assessor classes ---

    [Theory]
    [InlineData(6, 6, false, false, BenchmarkOutcomeClass.Correct)]
    [InlineData(5, 4, false, false, BenchmarkOutcomeClass.Correct)]
    [InlineData(5, 3, false, false, BenchmarkOutcomeClass.Partial)]
    [InlineData(4, 6, false, false, BenchmarkOutcomeClass.Partial)]
    [InlineData(3, 3, false, false, BenchmarkOutcomeClass.Partial)]
    [InlineData(2, 6, false, false, BenchmarkOutcomeClass.Incorrect)]
    [InlineData(6, 6, true, false, BenchmarkOutcomeClass.Incorrect)]
    [InlineData(3, 2, false, true, BenchmarkOutcomeClass.NotAttempted)]
    [InlineData(5, 4, false, true, BenchmarkOutcomeClass.NotAttempted)]
    // Incorrect wins over not attempted.
    [InlineData(2, 2, false, true, BenchmarkOutcomeClass.Incorrect)]
    [InlineData(5, 4, true, true, BenchmarkOutcomeClass.Incorrect)]
    public void SingleAssessor_ClassifiesByTheAssessorsLevelsAndFlags(
        int accuracy, int completeness, bool criticalError, bool notAttempted, BenchmarkOutcomeClass expected)
    {
        var answer = Single(1, accuracy, completeness, criticalError, notAttempted);

        Assert.Equal(expected, BenchmarkOutcomeSummary.Classify(Run(), answer, isPanelRun: false));
    }

    [Fact]
    public void ANullNotAttempted_IsNotAttemptedFalse()
    {
        var answer = Single(1, 3, 3, notAttempted: null);

        Assert.Equal(BenchmarkOutcomeClass.Partial, BenchmarkOutcomeSummary.Classify(Run(), answer, isPanelRun: false));
    }

    [Fact]
    public void AModelProducedEmptyAnswer_IsNoAnswer()
    {
        Assert.Equal(BenchmarkOutcomeClass.NoAnswer, BenchmarkOutcomeSummary.Classify(Run(), NoAnswer(1), isPanelRun: false));
    }

    [Fact]
    public void AnAnswerOutsideTheIndex_OrNotYetGraded_HasNoClass()
    {
        var providerError = Single(1, 6, 6);
        providerError.Status = BenchmarkAnswerStatus.ProviderError;
        Assert.Null(BenchmarkOutcomeSummary.Classify(Run(), providerError, isPanelRun: false));

        var failedAssessment = Single(2, 6, 6);
        failedAssessment.AssessmentStatus = BenchmarkAssessmentStatus.Failed;
        Assert.Null(BenchmarkOutcomeSummary.Classify(Run(), failedAssessment, isPanelRun: false));
    }

    // --- Panel classes ---

    [Fact]
    public void Panel_UsesTheMeanOfBothMembersLevels()
    {
        var run = Run(panel: true);

        // Mean Accuracy 5, mean Completeness 4.
        Assert.Equal(BenchmarkOutcomeClass.Correct,
            BenchmarkOutcomeSummary.Classify(run, Panel(1, (6, 5), (4, 3)), isPanelRun: true));

        // Mean Accuracy 4.5.
        Assert.Equal(BenchmarkOutcomeClass.Partial,
            BenchmarkOutcomeSummary.Classify(run, Panel(2, (6, 6), (3, 6)), isPanelRun: true));

        // Mean Accuracy 2.
        Assert.Equal(BenchmarkOutcomeClass.Incorrect,
            BenchmarkOutcomeSummary.Classify(run, Panel(3, (3, 6), (1, 6)), isPanelRun: true));
    }

    [Fact]
    public void Panel_IsNotAttemptedOnlyWhenBothMembersSaySo()
    {
        var run = Run(panel: true);

        Assert.Equal(BenchmarkOutcomeClass.NotAttempted, BenchmarkOutcomeSummary.Classify(
            run, Panel(1, (3, 2), (4, 2), notAttemptedA: true, notAttemptedB: true), isPanelRun: true));
        Assert.Equal(BenchmarkOutcomeClass.Partial, BenchmarkOutcomeSummary.Classify(
            run, Panel(2, (3, 2), (4, 2), notAttemptedA: true, notAttemptedB: false), isPanelRun: true));
        Assert.Equal(BenchmarkOutcomeClass.Partial, BenchmarkOutcomeSummary.Classify(
            run, Panel(3, (3, 2), (4, 2), notAttemptedA: false, notAttemptedB: true), isPanelRun: true));
    }

    [Theory]
    [InlineData(BenchmarkCriticalErrorResolution.Agreed, BenchmarkOutcomeClass.Incorrect)]
    [InlineData(BenchmarkCriticalErrorResolution.UpheldByVerifier, BenchmarkOutcomeClass.Incorrect)]
    // Not confirmed, but a member still flagged, so never Correct.
    [InlineData(BenchmarkCriticalErrorResolution.OverturnedByVerifier, BenchmarkOutcomeClass.Partial)]
    [InlineData(BenchmarkCriticalErrorResolution.Unresolved, BenchmarkOutcomeClass.Partial)]
    public void Panel_AFlaggedAnswer_IsIncorrectOnlyWhenConfirmed(
        BenchmarkCriticalErrorResolution resolution, BenchmarkOutcomeClass expected)
    {
        var answer = Panel(1, (6, 6), (6, 6), flagB: true, resolution: resolution);

        Assert.Equal(expected, BenchmarkOutcomeSummary.Classify(Run(panel: true), answer, isPanelRun: true));
    }

    [Fact]
    public void Panel_AnAnswerMemberBHasNotScored_HasNoClass()
    {
        var answer = Panel(1, (6, 6), (6, 6));
        answer.CoAssessmentStatus = BenchmarkAssessmentStatus.Failed;

        Assert.Null(BenchmarkOutcomeSummary.Classify(Run(panel: true), answer, isPanelRun: true));
    }

    // --- Figures ---

    [Fact]
    public void Compute_CountsEveryClass_AndDerivesTheRates()
    {
        var failed = Single(7, 6, 6);
        failed.Status = BenchmarkAnswerStatus.ProviderError;
        var answers = new List<BenchmarkRunAnswer>
        {
            Single(1, 6, 6),
            Single(2, 5, 4),
            Single(3, 5, 3),
            Single(4, 6, 6, criticalError: true),
            Single(5, 3, 2, notAttempted: true),
            NoAnswer(6),
            failed
        };

        var summary = BenchmarkOutcomeSummary.Compute(Run(), answers);

        Assert.NotNull(summary);
        Assert.Equal(2, summary.CorrectCount);
        Assert.Equal(1, summary.PartialCount);
        Assert.Equal(1, summary.IncorrectCount);
        Assert.Equal(1, summary.NotAttemptedCount);
        Assert.Equal(1, summary.NoAnswerCount);
        Assert.Equal(5, summary.ClassifiedCount);

        Assert.Equal(1, summary.ConfirmedCriticalErrorCount);
        Assert.Equal(0, summary.UnresolvedCriticalErrorCount);
        Assert.Equal(0, summary.OverturnedCriticalErrorCount);
        Assert.Equal(new[] { 4 }, summary.ConfirmedCriticalErrorQuestions);
        Assert.Equal(new[] { 5 }, summary.NotAttemptedQuestions);

        Assert.Equal(0.2, summary.CriticalErrorRate!.Value, 10);
        var interval = BenchmarkProportionInterval.Wilson95(1, 5)!.Value;
        Assert.Equal(interval.Low, summary.CriticalErrorRateLow!.Value, 10);
        Assert.Equal(interval.High, summary.CriticalErrorRateHigh!.Value, 10);

        // Correct ÷ (correct + partial + incorrect) = 2 / 4; incorrect ÷ (incorrect + not attempted) = 1 / 2.
        Assert.Equal(0.5, summary.CorrectWhenAttempted!.Value, 10);
        Assert.Equal(0.5, summary.WrongInsteadOfAbstaining!.Value, 10);

        // The answer outside the index has no entry; the empty one does.
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, summary.Answers.Select(a => a.QuestionNumber));
        Assert.Equal(BenchmarkOutcomeClass.NoAnswer, summary.Answers[5].Class);
        Assert.Equal(BenchmarkCriticalErrorResolution.SingleAssessor, summary.Answers[3].Resolution);
    }

    [Fact]
    public void Compute_ListsUnresolvedAndOverturnedQuestions_InAPanelRun()
    {
        var answers = new List<BenchmarkRunAnswer>
        {
            Panel(1, (6, 6), (6, 6)),
            Panel(2, (6, 6), (6, 6), flagA: true, resolution: BenchmarkCriticalErrorResolution.Unresolved),
            Panel(3, (6, 6), (6, 6), flagB: true, resolution: BenchmarkCriticalErrorResolution.OverturnedByVerifier),
            Panel(4, (6, 6), (6, 6), flagA: true, flagB: true, resolution: BenchmarkCriticalErrorResolution.Agreed),
            Panel(5, (6, 6), (6, 6), flagA: true, resolution: BenchmarkCriticalErrorResolution.UpheldByVerifier)
        };

        var summary = BenchmarkOutcomeSummary.Compute(Run(panel: true), answers)!;

        Assert.Equal(new[] { 4, 5 }, summary.ConfirmedCriticalErrorQuestions);
        Assert.Equal(new[] { 2 }, summary.UnresolvedCriticalErrorQuestions);
        Assert.Equal(new[] { 3 }, summary.OverturnedCriticalErrorQuestions);
        Assert.Equal(1, summary.CorrectCount);
        Assert.Equal(2, summary.PartialCount);
        Assert.Equal(2, summary.IncorrectCount);
        Assert.Equal(0.4, summary.CriticalErrorRate!.Value, 10);
        Assert.Equal(1.0, summary.WrongInsteadOfAbstaining!.Value, 10);
    }

    [Fact]
    public void Compute_LeavesTheRatesNull_WhenTheirDenominatorsAreZero()
    {
        var summary = BenchmarkOutcomeSummary.Compute(Run(), new List<BenchmarkRunAnswer> { NoAnswer(1) })!;

        Assert.Equal(0, summary.ClassifiedCount);
        Assert.Equal(1, summary.NoAnswerCount);
        Assert.Null(summary.CriticalErrorRate);
        Assert.Null(summary.CriticalErrorRateLow);
        Assert.Null(summary.CriticalErrorRateHigh);
        Assert.Null(summary.CorrectWhenAttempted);
        Assert.Null(summary.WrongInsteadOfAbstaining);
    }

    [Fact]
    public void Compute_WrongInsteadOfAbstaining_IsZeroWhenOnlyAbstentions()
    {
        var summary = BenchmarkOutcomeSummary.Compute(
            Run(), new List<BenchmarkRunAnswer> { Single(1, 6, 6), Single(2, 3, 2, notAttempted: true) })!;

        Assert.Equal(0.0, summary.WrongInsteadOfAbstaining!.Value, 10);
        Assert.Equal(1.0, summary.CorrectWhenAttempted!.Value, 10);
    }

    // --- Wilson interval ---

    [Theory]
    [InlineData(0, 18, 0.0000, 0.1759)]
    [InlineData(1, 18, 0.0099, 0.2576)]
    public void Wilson95_MatchesReferenceValues(int k, int n, double low, double high)
    {
        var interval = BenchmarkProportionInterval.Wilson95(k, n)!.Value;

        Assert.InRange(interval.Low, low - 0.0005, low + 0.0005);
        Assert.InRange(interval.High, high - 0.0005, high + 0.0005);
    }

    [Fact]
    public void Wilson95_IsNullForNoTrials_AndSymmetricAtTheTop()
    {
        Assert.Null(BenchmarkProportionInterval.Wilson95(0, 0));

        var none = BenchmarkProportionInterval.Wilson95(0, 18)!.Value;
        var all = BenchmarkProportionInterval.Wilson95(18, 18)!.Value;
        Assert.Equal(0.0, none.Low);
        Assert.Equal(1.0, all.High);
        Assert.Equal(none.High, 1.0 - all.Low, 10);
    }

    [Fact]
    public void Wilson95_RejectsACountOutsideTheTrials()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkProportionInterval.Wilson95(3, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkProportionInterval.Wilson95(-1, 2));
    }
}
