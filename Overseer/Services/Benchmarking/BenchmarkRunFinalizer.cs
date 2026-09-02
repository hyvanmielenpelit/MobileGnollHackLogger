namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;

public static class BenchmarkRunFinalizer
{
    public static int FallbackDifficulty(BenchmarkDifficulty difficulty) => difficulty switch
    {
        BenchmarkDifficulty.Simple => 25,
        BenchmarkDifficulty.Intermediate => 55,
        BenchmarkDifficulty.Advanced => 85,
        _ => 50
    };

    public static bool HasUnresolvedWork(BenchmarkRunAnswer answer)
    {
        return answer.Status is BenchmarkAnswerStatus.ProviderError or BenchmarkAnswerStatus.Failed or BenchmarkAnswerStatus.EmptyAnswer
            || answer.AssessmentStatus is BenchmarkAssessmentStatus.Failed or BenchmarkAssessmentStatus.Pending or BenchmarkAssessmentStatus.Assessing;
    }

    public static bool IsDegraded(BenchmarkRunAnswer answer)
    {
        return answer.Status == BenchmarkAnswerStatus.EmptyAnswer
            || answer.ToolBudgetExhausted
            || answer.AnswerFlags != 0;
    }

    public static BenchmarkRunStatus ComputeStatus(IReadOnlyCollection<BenchmarkRunAnswer> answers)
    {
        if (answers == null || answers.Count == 0)
        {
            return BenchmarkRunStatus.Failed;
        }

        return answers.Any(a => HasUnresolvedWork(a) || IsDegraded(a))
            ? BenchmarkRunStatus.CompletedWithErrors
            : BenchmarkRunStatus.Completed;
    }

    public static void Apply(BenchmarkRun run, IReadOnlyCollection<BenchmarkRunAnswer> answers)
    {
        run.TotalInputTokens = answers.Sum(a => (long)(a.InputTokens ?? 0));
        run.TotalOutputTokens = answers.Sum(a => (long)(a.OutputTokens ?? 0));
        run.TotalCacheReadTokens = answers.Sum(a => (long)(a.CacheReadInputTokens ?? 0));
        run.TotalCacheCreationTokens = answers.Sum(a => (long)(a.CacheCreationInputTokens ?? 0));
        run.TotalAnswerDurationMs = answers.Sum(a => a.DurationMs);
        run.AnsweredQuestionCount = answers.Count(a => a.Status == BenchmarkAnswerStatus.Ok);
        run.DegradedAnswerCount = answers.Count(IsDegraded);
        run.ToolStarvedAnswerCount = answers.Count(a => a.ToolBudgetExhausted);

        var scorableItems = answers
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok)
            .Select(a => (a.QualityScore, a.AssessedDifficulty ?? FallbackDifficulty(a.Difficulty)))
            .ToList();

        var speedItems = answers
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok)
            .Select(a => (a.SpeedScore, a.AssessedDifficulty ?? FallbackDifficulty(a.Difficulty)))
            .ToList();

        run.QualityIndex = BenchmarkScoring.QualityIndex(scorableItems);
        run.SpeedIndex = BenchmarkScoring.SpeedIndex(speedItems);

        run.CompletedAtUtc = DateTime.UtcNow;
        run.Status = ComputeStatus(answers);
    }
}
