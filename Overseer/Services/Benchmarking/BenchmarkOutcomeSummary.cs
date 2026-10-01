namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;

/// <summary>
/// The outcome class of one answer under scoring method 13 and later. Reporting only: no index
/// reads it.
/// </summary>
public enum BenchmarkOutcomeClass
{
    Correct = 1,
    Partial = 2,
    Incorrect = 3,
    NotAttempted = 4,
    NoAnswer = 5
}

/// <summary>
/// One answer of <see cref="BenchmarkOutcomeSummary.Answers"/>. <paramref name="QuestionNumber"/> is
/// the answer's <see cref="BenchmarkRunAnswer.OrderIndex"/>, 1-based, as the run report prints it.
/// </summary>
public sealed record BenchmarkOutcomeAnswer(
    long AnswerId,
    int QuestionNumber,
    BenchmarkOutcomeClass Class,
    BenchmarkCriticalErrorResolution? Resolution);

/// <summary>
/// A run's answers by outcome class, and its critical errors by resolution, over the answers that
/// count toward the quality index (<see cref="BenchmarkRunFinalizer.CountsTowardQualityIndex"/>).
/// Null before scoring method 13.
///
/// Classes, first match wins: <c>Incorrect</c> (a confirmed critical error, or mean Accuracy at most 2);
/// <c>NotAttempted</c> (the assessor marked it so, or in a panel run both members did); <c>Correct</c>
/// (mean Accuracy at least 5, mean Completeness at least 4, and no member flagged a critical error);
/// <c>Partial</c> otherwise. A model-produced empty answer is <c>NoAnswer</c>. "Mean" is over the two
/// members in a panel run, otherwise the assessor's own level. An answer not yet graded is not classified.
/// </summary>
public sealed record BenchmarkOutcomeSummary
{
    public int CorrectCount { get; init; }
    public int PartialCount { get; init; }
    public int IncorrectCount { get; init; }
    public int NotAttemptedCount { get; init; }
    public int NoAnswerCount { get; init; }

    /// <summary>Correct, partial, incorrect and not attempted together; <c>NoAnswer</c> is not classified.</summary>
    public int ClassifiedCount { get; init; }

    /// <summary>Classified answers resolved <c>Agreed</c>, <c>UpheldByVerifier</c> or <c>SingleAssessor</c>.</summary>
    public int ConfirmedCriticalErrorCount { get; init; }

    /// <summary>Classified answers resolved <c>Unresolved</c>.</summary>
    public int UnresolvedCriticalErrorCount { get; init; }

    /// <summary>Classified answers resolved <c>OverturnedByVerifier</c>.</summary>
    public int OverturnedCriticalErrorCount { get; init; }

    /// <summary>Confirmed critical errors ÷ classified answers. Null when nothing is classified.</summary>
    public double? CriticalErrorRate { get; init; }

    /// <summary>The 95 % Wilson interval of <see cref="CriticalErrorRate"/>.</summary>
    public double? CriticalErrorRateLow { get; init; }

    public double? CriticalErrorRateHigh { get; init; }

    /// <summary>Correct ÷ (correct + partial + incorrect). Null when that denominator is 0.</summary>
    public double? CorrectWhenAttempted { get; init; }

    /// <summary>Incorrect ÷ (incorrect + not attempted). Null when both are 0.</summary>
    public double? WrongInsteadOfAbstaining { get; init; }

    /// <summary>Every answer with a class, in question order.</summary>
    public IReadOnlyList<BenchmarkOutcomeAnswer> Answers { get; init; } = Array.Empty<BenchmarkOutcomeAnswer>();

    public IReadOnlyList<int> ConfirmedCriticalErrorQuestions { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> UnresolvedCriticalErrorQuestions { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> OverturnedCriticalErrorQuestions { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> NotAttemptedQuestions { get; init; } = Array.Empty<int>();

    /// <summary>The summary of <paramref name="run"/>, or null before scoring method 13.</summary>
    public static BenchmarkOutcomeSummary? Compute(BenchmarkRun run, IReadOnlyCollection<BenchmarkRunAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(answers);
        if (!BenchmarkCriticalErrorResolver.Applies(run)) return null;

        bool isPanelRun = BenchmarkRunFinalizer.IsPanelRun(run);
        var classified = new List<(BenchmarkRunAnswer Answer, BenchmarkOutcomeAnswer Outcome)>();
        foreach (var answer in answers.OrderBy(a => a.OrderIndex).ThenBy(a => a.Id))
        {
            if (Classify(run, answer, isPanelRun) is not { } outcomeClass) continue;
            classified.Add((answer, new BenchmarkOutcomeAnswer(
                answer.Id, answer.OrderIndex, outcomeClass, answer.CriticalErrorResolution)));
        }

        int CountOf(BenchmarkOutcomeClass c) => classified.Count(x => x.Outcome.Class == c);
        int correct = CountOf(BenchmarkOutcomeClass.Correct);
        int partial = CountOf(BenchmarkOutcomeClass.Partial);
        int incorrect = CountOf(BenchmarkOutcomeClass.Incorrect);
        int notAttempted = CountOf(BenchmarkOutcomeClass.NotAttempted);
        int noAnswer = CountOf(BenchmarkOutcomeClass.NoAnswer);
        int classifiedCount = correct + partial + incorrect + notAttempted;

        var graded = classified.Where(x => x.Outcome.Class != BenchmarkOutcomeClass.NoAnswer).ToList();
        List<int> Questions(Func<BenchmarkRunAnswer, bool> predicate)
            => graded.Where(x => predicate(x.Answer)).Select(x => x.Outcome.QuestionNumber).ToList();

        var confirmed = Questions(BenchmarkCriticalErrorResolver.IsConfirmed);
        var unresolved = Questions(a => a.CriticalErrorResolution == BenchmarkCriticalErrorResolution.Unresolved);
        var overturned = Questions(a => a.CriticalErrorResolution == BenchmarkCriticalErrorResolution.OverturnedByVerifier);

        var interval = BenchmarkProportionInterval.Wilson95(confirmed.Count, classifiedCount);
        int attempted = correct + partial + incorrect;

        return new BenchmarkOutcomeSummary
        {
            CorrectCount = correct,
            PartialCount = partial,
            IncorrectCount = incorrect,
            NotAttemptedCount = notAttempted,
            NoAnswerCount = noAnswer,
            ClassifiedCount = classifiedCount,
            ConfirmedCriticalErrorCount = confirmed.Count,
            UnresolvedCriticalErrorCount = unresolved.Count,
            OverturnedCriticalErrorCount = overturned.Count,
            CriticalErrorRate = classifiedCount > 0 ? (double)confirmed.Count / classifiedCount : null,
            CriticalErrorRateLow = interval?.Low,
            CriticalErrorRateHigh = interval?.High,
            CorrectWhenAttempted = attempted > 0 ? (double)correct / attempted : null,
            WrongInsteadOfAbstaining = incorrect + notAttempted > 0 ? (double)incorrect / (incorrect + notAttempted) : null,
            Answers = classified.Select(x => x.Outcome).ToList(),
            ConfirmedCriticalErrorQuestions = confirmed,
            UnresolvedCriticalErrorQuestions = unresolved,
            OverturnedCriticalErrorQuestions = overturned,
            NotAttemptedQuestions = classified
                .Where(x => x.Outcome.Class == BenchmarkOutcomeClass.NotAttempted)
                .Select(x => x.Outcome.QuestionNumber)
                .ToList()
        };
    }

    /// <summary>
    /// The outcome class of <paramref name="answer"/>. Null before scoring method 13, for an answer
    /// outside the quality index, and for one not yet graded (in a panel run, by both members).
    /// </summary>
    public static BenchmarkOutcomeClass? Classify(BenchmarkRun run, BenchmarkRunAnswer answer, bool isPanelRun)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(answer);
        if (!BenchmarkCriticalErrorResolver.Applies(run)) return null;
        if (!BenchmarkRunFinalizer.CountsTowardQualityIndex(answer)) return null;
        if (BenchmarkRunFinalizer.IsModelProducedEmptyAnswer(answer)) return BenchmarkOutcomeClass.NoAnswer;

        var memberA = BenchmarkVerdictView.FromPrimary(answer);
        if (memberA == null) return null;

        BenchmarkVerdictView? memberB = null;
        if (isPanelRun)
        {
            memberB = BenchmarkVerdictView.FromCoAssessment(answer);
            if (memberB == null) return null;
        }

        double accuracy = memberB == null ? memberA.AccuracyLevel : (memberA.AccuracyLevel + memberB.AccuracyLevel) / 2.0;
        double completeness = memberB == null ? memberA.CompletenessLevel : (memberA.CompletenessLevel + memberB.CompletenessLevel) / 2.0;

        if (BenchmarkCriticalErrorResolver.IsConfirmed(answer) || accuracy <= 2.0)
        {
            return BenchmarkOutcomeClass.Incorrect;
        }

        bool notAttempted = isPanelRun
            ? answer.NotAttempted == true && answer.CoAssessmentNotAttempted == true
            : answer.NotAttempted == true;
        if (notAttempted) return BenchmarkOutcomeClass.NotAttempted;

        bool flagged = answer.CriticalError || (isPanelRun && (answer.CoAssessmentCriticalError ?? false));
        if (accuracy >= 5.0 && completeness >= 4.0 && !flagged) return BenchmarkOutcomeClass.Correct;

        return BenchmarkOutcomeClass.Partial;
    }
}
