namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;

public sealed record BenchmarkDifficultyTargetSelection(
    IReadOnlyList<BenchmarkQuestion> Questions, string Scope, string? Error);

/// <summary>
/// Chooses the questions a difficulty assessment job rates. Explicit question ids take
/// precedence over the unassessed filter; with neither, every question in the suite is
/// targeted. Results are ordered by <see cref="BenchmarkQuestion.OrderIndex"/>.
/// </summary>
public static class BenchmarkDifficultyTargetSelector
{
    public const string QuestionsScope = "questions";
    public const string UnassessedScope = "unassessed";
    public const string SuiteScope = "suite";

    public static BenchmarkDifficultyTargetSelection Select(
        long suiteId,
        IReadOnlyCollection<BenchmarkQuestion> suiteQuestions,
        IReadOnlyCollection<long>? questionIds,
        bool onlyUnassessed)
    {
        if (questionIds != null && questionIds.Count > 0)
        {
            var suiteQuestionIds = suiteQuestions.Select(q => q.Id).ToHashSet();
            foreach (var qId in questionIds)
            {
                if (!suiteQuestionIds.Contains(qId))
                {
                    return new BenchmarkDifficultyTargetSelection(
                        Array.Empty<BenchmarkQuestion>(),
                        QuestionsScope,
                        $"Question ID {qId} does not belong to suite {suiteId}.");
                }
            }

            var requestedIds = questionIds.ToHashSet();
            return new BenchmarkDifficultyTargetSelection(
                suiteQuestions.Where(q => requestedIds.Contains(q.Id)).OrderBy(q => q.OrderIndex).ToList(),
                QuestionsScope,
                null);
        }

        if (onlyUnassessed)
        {
            return new BenchmarkDifficultyTargetSelection(
                suiteQuestions.Where(q => q.AssessedDifficulty == null).OrderBy(q => q.OrderIndex).ToList(),
                UnassessedScope,
                null);
        }

        return new BenchmarkDifficultyTargetSelection(
            suiteQuestions.OrderBy(q => q.OrderIndex).ToList(),
            SuiteScope,
            null);
    }
}
