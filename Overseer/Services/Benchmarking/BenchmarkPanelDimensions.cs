namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;

/// <summary>One dimension's average: points on the 0–100 scale and the mean level.</summary>
public readonly record struct BenchmarkDimensionAverage(double Points, double Level);

/// <summary>
/// A panel's four dimension averages per reader, each row in Accuracy, Completeness, Conciseness,
/// Readability order.
/// </summary>
public sealed class BenchmarkPanelDimensionAverages
{
    /// <summary>Member A's stored scores and levels, a missing value counted as zero.</summary>
    public required IReadOnlyList<BenchmarkDimensionAverage> MemberA { get; init; }

    /// <summary>Member B's levels scored on the answer's run's level table; null when no record carries all four levels.</summary>
    public IReadOnlyList<BenchmarkDimensionAverage>? MemberB { get; init; }

    /// <summary>The mean of the two members' rows; null with <see cref="MemberB"/>.</summary>
    public IReadOnlyList<BenchmarkDimensionAverage>? Panel { get; init; }

    /// <summary>The answers member A's row is taken over.</summary>
    public int AnswerCount { get; init; }

    /// <summary>The answers whose member-B record carries all four levels, which member B's row is taken over.</summary>
    public int MemberBAnswerCount { get; init; }
}

/// <summary>
/// The dimension averages of a panel run: member A's, member B's and the panel row, the mean of the
/// two. The run report's Dimensional Score Averages table and the report pack's dimension facts both
/// read them, so the two print one set of figures.
/// </summary>
public static class BenchmarkPanelDimensions
{
    private static readonly (Func<BenchmarkRunAnswer, int?> ScoreA, Func<BenchmarkRunAnswer, int?> LevelA, Func<BenchmarkCoAssessmentRecord, int?> LevelB)[] Dimensions =
    {
        (a => a.AccuracyScore, a => a.AccuracyLevel, r => r.AccuracyLevel),
        (a => a.CompletenessScore, a => a.CompletenessLevel, r => r.CompletenessLevel),
        (a => a.ConcisenessScore, a => a.ConcisenessLevel, r => r.ConcisenessLevel),
        (a => a.ReadabilityScore, a => a.ReadabilityLevel, r => r.ReadabilityLevel)
    };

    /// <summary>The averages over one run's graded answers, member B's levels scored on <paramref name="levelScores"/>; null over no answer.</summary>
    public static BenchmarkPanelDimensionAverages? Averages(IReadOnlyList<BenchmarkRunAnswer> scoredAnswers, IReadOnlyList<int>? levelScores)
    {
        ArgumentNullException.ThrowIfNull(scoredAnswers);
        return Averages(scoredAnswers.Select(a => (a, levelScores)).ToList());
    }

    /// <summary>
    /// The averages over graded answers pooled across runs, each paired with its run's level table;
    /// null over no answer. Member B's row reads the answers whose co-assessment was scored and whose
    /// record carries all four levels.
    /// </summary>
    public static BenchmarkPanelDimensionAverages? Averages(IReadOnlyList<(BenchmarkRunAnswer Answer, IReadOnlyList<int>? LevelScores)> scoredAnswers)
    {
        ArgumentNullException.ThrowIfNull(scoredAnswers);
        if (scoredAnswers.Count == 0) return null;

        var answers = scoredAnswers.Select(s => s.Answer).ToList();
        var memberA = Dimensions
            .Select(d => new BenchmarkDimensionAverage(answers.Average(a => d.ScoreA(a) ?? 0), answers.Average(a => d.LevelA(a) ?? 0)))
            .ToList();

        var records = scoredAnswers
            .Where(s => s.Answer.CoAssessmentStatus == BenchmarkAssessmentStatus.Scored)
            .Select(s => (Record: BenchmarkCoAssessmentRecord.Parse(s.Answer.CoAssessmentJson), s.LevelScores))
            .Where(x => x.Record != null)
            .Select(x => (Record: x.Record!, x.LevelScores))
            .Where(x => Dimensions.All(d => d.LevelB(x.Record).HasValue))
            .ToList();

        if (records.Count == 0)
        {
            return new BenchmarkPanelDimensionAverages { MemberA = memberA, AnswerCount = answers.Count };
        }

        var memberB = Dimensions
            .Select(d => new BenchmarkDimensionAverage(
                records.Average(x => (double)BenchmarkScoring.Score(d.LevelB(x.Record)!.Value, x.LevelScores)),
                records.Average(x => (double)d.LevelB(x.Record)!.Value)))
            .ToList();
        var panel = memberA
            .Zip(memberB, (a, b) => new BenchmarkDimensionAverage((a.Points + b.Points) / 2.0, (a.Level + b.Level) / 2.0))
            .ToList();

        return new BenchmarkPanelDimensionAverages
        {
            MemberA = memberA,
            MemberB = memberB,
            Panel = panel,
            AnswerCount = answers.Count,
            MemberBAnswerCount = records.Count
        };
    }
}
