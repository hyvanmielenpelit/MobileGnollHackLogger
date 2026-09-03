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

    /// <summary>
    /// Flags that compromise the validity of an answer. Advisory flags are deliberately absent:
    /// the text they describe is removed before grading, so the graded answer is unaffected.
    /// </summary>
    private const BenchmarkAnswerFlags TransportDefectFlags =
        BenchmarkAnswerFlags.Empty | BenchmarkAnswerFlags.HarnessArtifacts | BenchmarkAnswerFlags.Truncated;

    private const BenchmarkAnswerFlags AdvisoryFlags =
        BenchmarkAnswerFlags.ReasoningBleed | BenchmarkAnswerFlags.RepeatedFragments;

    /// <summary>A transport or provider defect corrupted this answer.</summary>
    public static bool HasTransportDefect(BenchmarkRunAnswer answer)
    {
        return answer.Status == BenchmarkAnswerStatus.EmptyAnswer
            || (((BenchmarkAnswerFlags)answer.AnswerFlags) & TransportDefectFlags) != 0;
    }

    /// <summary>
    /// An operator-configured cap was reached. The answer is valid; the cap may need raising.
    /// This is not an error and must not be reported as one.
    /// </summary>
    public static bool HasHarnessLimit(BenchmarkRunAnswer answer)
    {
        return answer.ToolBudgetExhausted;
    }

    /// <summary>
    /// Advisory only. May co-occur with either bucket above and never changes the run status or
    /// the clean count, so it is counted separately and never summed with them.
    /// </summary>
    public static bool HasAdvisoryFlag(BenchmarkRunAnswer answer)
    {
        return (((BenchmarkAnswerFlags)answer.AnswerFlags) & AdvisoryFlags) != 0;
    }

    /// <summary>
    /// Which of the three mutually exclusive integrity buckets this answer belongs to. Transport
    /// defects win when both apply, which is what keeps
    /// clean + transport defects + harness limits equal to the question count.
    /// </summary>
    public static BenchmarkAnswerIntegrity Classify(BenchmarkRunAnswer answer)
    {
        if (HasTransportDefect(answer)) return BenchmarkAnswerIntegrity.TransportDefect;
        if (HasHarnessLimit(answer)) return BenchmarkAnswerIntegrity.HarnessLimit;
        return BenchmarkAnswerIntegrity.Clean;
    }

    /// <summary>
    /// Superseded by <see cref="Classify"/>. Retained because the run-level
    /// <c>DegradedAnswerCount</c> column keeps its original meaning for older runs.
    /// </summary>
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

        if (answers.Any(a => HasUnresolvedWork(a) || HasTransportDefect(a)))
        {
            return BenchmarkRunStatus.CompletedWithErrors;
        }

        // Only a configured cap was reached, so the run is valid. Reporting this as
        // CompletedWithErrors made a healthy run look broken.
        if (answers.Any(HasHarnessLimit))
        {
            return BenchmarkRunStatus.CompletedWithLimits;
        }

        return BenchmarkRunStatus.Completed;
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
        run.ToolStarvedAnswerCount = answers.Count(HasHarnessLimit);
        run.TransportDefectAnswerCount = answers.Count(HasTransportDefect);
        run.AdvisoryFlagAnswerCount = answers.Count(HasAdvisoryFlag);
        run.ScrubbedArtifactAnswerCount = answers.Count(a => a.ScrubbedArtifactCount > 0);
        run.ToolOverheadMs = answers.Any(a => a.ToolTimeMs.HasValue)
            ? answers.Sum(a => a.ToolTimeMs ?? 0L)
            : null;

        var scorableItems = answers
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok)
            .Select(a => (a.QualityScore, a.AssessedDifficulty ?? FallbackDifficulty(a.Difficulty)))
            .ToList();

        run.QualityIndex = BenchmarkScoring.QualityIndex(scorableItems);

        // Equal weight: difficulty already scales each question's own speed target.
        run.SpeedIndex = BenchmarkScoring.SpeedIndex(
            answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok).Select(a => a.SpeedScore));

        run.CompletedAtUtc = DateTime.UtcNow;
        run.Status = ComputeStatus(answers);
    }
}
