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
    /// Flags that leave an answer unusable. Advisory flags are deliberately absent: they stay
    /// out of the run status because the text they describe does not *invalidate* the answer,
    /// not because its removal before grading is guaranteed. The 2026-09-03 GPT-5.6 Luna run
    /// showed both: the streaming layer's bug meant five graded answers still carried their own
    /// narration, and the report wrongly asserted it had been removed. The fix corrected the
    /// removal and the report's claim about it; this classification did not need to change,
    /// because narration that survives grading is a conciseness/readability quality problem the
    /// assessor already scores, not a reason to exclude the answer from the run.
    ///
    /// <see cref="BenchmarkAnswerFlags.HarnessArtifacts"/> is deliberately absent too. Leaked
    /// tool-call payloads are removed by the scrubber and the answer beneath them is graded
    /// normally — on the 2026-09-03 GPT-5.6 Luna run the five affected answers scored 78, 42,
    /// 99, 25 and 95 — so treating them as defects reported a healthy run as
    /// <c>CompletedWithErrors</c> and put a majority of its answers outside the clean count.
    /// They are classified as <see cref="BenchmarkAnswerIntegrity.Recovered"/> instead.
    /// </summary>
    private const BenchmarkAnswerFlags TransportDefectFlags =
        BenchmarkAnswerFlags.Empty | BenchmarkAnswerFlags.Truncated;

    /// <summary>
    /// Advisory, never defect. <see cref="BenchmarkAnswerFlags.ContestedVerdict"/> joins these
    /// deliberately: the answer is intact and the verdict may well be right — what it is not is
    /// unambiguous. Adding it to <see cref="TransportDefectFlags"/> would flip every run carrying
    /// one to <c>CompletedWithErrors</c>, which is precisely the regression harness version 4 was
    /// written to undo.
    /// </summary>
    private const BenchmarkAnswerFlags AdvisoryFlags =
        BenchmarkAnswerFlags.ReasoningBleed
        | BenchmarkAnswerFlags.RepeatedFragments
        | BenchmarkAnswerFlags.ContestedVerdict
        | BenchmarkAnswerFlags.UnevidencedDeduction
        | BenchmarkAnswerFlags.RefutedClaim
        | BenchmarkAnswerFlags.OmissionAsAccuracy;

    /// <summary>A transport or provider defect corrupted this answer beyond recovery.</summary>
    public static bool HasTransportDefect(BenchmarkRunAnswer answer)
    {
        return answer.Status == BenchmarkAnswerStatus.EmptyAnswer
            || (((BenchmarkAnswerFlags)answer.AnswerFlags) & TransportDefectFlags) != 0;
    }

    /// <summary>
    /// The provider leaked transport artifacts into the answer, the scrubber removed them, and
    /// what remained was authored prose that completed and graded normally. The event is real
    /// and worth reporting — it is a provider-path defect, and the report says so — but it did
    /// not damage the result, so it must not fail the run.
    ///
    /// An answer whose text did not survive the scrub carries <see cref="BenchmarkAnswerFlags.Empty"/>
    /// and is a transport defect instead; the checks below are ordered so that wins.
    /// </summary>
    public static bool WasRecovered(BenchmarkRunAnswer answer)
    {
        if (HasTransportDefect(answer)) return false;
        if (answer.Status != BenchmarkAnswerStatus.Ok) return false;
        if ((((BenchmarkAnswerFlags)answer.AnswerFlags) & BenchmarkAnswerFlags.HarnessArtifacts) == 0) return false;

        return !string.IsNullOrWhiteSpace(answer.AnswerText);
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
    /// Which of the four mutually exclusive integrity buckets this answer belongs to. The order
    /// is the precedence: the most severe applicable class wins, which is what keeps
    /// clean + transport defects + recovered + harness limits equal to the question count.
    /// </summary>
    public static BenchmarkAnswerIntegrity Classify(BenchmarkRunAnswer answer)
    {
        if (HasTransportDefect(answer)) return BenchmarkAnswerIntegrity.TransportDefect;
        if (WasRecovered(answer)) return BenchmarkAnswerIntegrity.Recovered;
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

        // Only a configured cap was reached, or the harness repaired a leaky answer and graded
        // it. Either way the run is valid. Reporting these as CompletedWithErrors made a
        // healthy run look broken.
        if (answers.Any(a => WasRecovered(a) || HasHarnessLimit(a)))
        {
            return BenchmarkRunStatus.CompletedWithLimits;
        }

        return BenchmarkRunStatus.Completed;
    }

    /// <summary>
    /// Computes candidate token and timing totals from a collection of answers.
    /// Shared with AdminBenchmarkController for live run-detail reporting while a run is pending or running.
    /// Note: BenchmarkRunFinalizer.Apply remains the only writer to the database entities.
    /// </summary>
    public static (long TotalInputTokens, long TotalOutputTokens, long TotalCacheReadTokens, long TotalCacheCreationTokens, long TotalAnswerDurationMs) ComputeCandidateTotals(IEnumerable<BenchmarkRunAnswer> answers)
    {
        return (
            answers.Sum(a => (long)(a.InputTokens ?? 0)),
            answers.Sum(a => (long)(a.OutputTokens ?? 0)),
            answers.Sum(a => (long)(a.CacheReadInputTokens ?? 0)),
            answers.Sum(a => (long)(a.CacheCreationInputTokens ?? 0)),
            answers.Sum(a => a.DurationMs)
        );
    }

    public static void Apply(BenchmarkRun run, IReadOnlyCollection<BenchmarkRunAnswer> answers)
    {
        var candidateTotals = ComputeCandidateTotals(answers);
        run.TotalInputTokens = candidateTotals.TotalInputTokens;
        run.TotalOutputTokens = candidateTotals.TotalOutputTokens;
        run.TotalCacheReadTokens = candidateTotals.TotalCacheReadTokens;
        run.TotalCacheCreationTokens = candidateTotals.TotalCacheCreationTokens;
        run.TotalAnswerDurationMs = candidateTotals.TotalAnswerDurationMs;

        // Assessor side, kept separate from the candidate totals above: the run's cost is the
        // two together, and the model under test must not be charged for its grader.
        run.TotalAssessmentInputTokens = answers.Sum(a => (long)(a.AssessmentInputTokens ?? 0));
        run.TotalAssessmentOutputTokens = answers.Sum(a => (long)(a.AssessmentOutputTokens ?? 0));
        run.TotalAssessmentDurationMs = answers.Sum(a => a.AssessmentDurationMs ?? 0L);

        // Claim verifier side, kept separate from candidate and assessor totals.
        run.TotalClaimVerificationInputTokens = answers.Sum(a => (long)(a.ClaimVerificationInputTokens ?? 0));
        run.TotalClaimVerificationOutputTokens = answers.Sum(a => (long)(a.ClaimVerificationOutputTokens ?? 0));
        run.TotalClaimVerificationDurationMs = answers.Sum(a => a.ClaimVerificationDurationMs ?? 0L);

        run.AnsweredQuestionCount = answers.Count(a => a.Status == BenchmarkAnswerStatus.Ok);
        run.DegradedAnswerCount = answers.Count(IsDegraded);
        run.ToolStarvedAnswerCount = answers.Count(HasHarnessLimit);
        run.TransportDefectAnswerCount = answers.Count(HasTransportDefect);
        run.RecoveredAnswerCount = answers.Count(WasRecovered);
        run.AdvisoryFlagAnswerCount = answers.Count(HasAdvisoryFlag);
        run.ScrubbedArtifactAnswerCount = answers.Count(a => a.ScrubbedArtifactCount > 0);
        run.ContestedVerdictAnswerCount = answers.Count(
            a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.ContestedVerdict) != 0);
        run.UnevidencedDeductionAnswerCount = answers.Count(
            a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.UnevidencedDeduction) != 0);
        run.OmissionAsAccuracyAnswerCount = answers.Count(
            a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.OmissionAsAccuracy) != 0);
        run.RefutedClaimAnswerCount = answers.Count(
            a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.RefutedClaim) != 0);
        run.ClaimVerifiedAnswerCount = answers.Count(
            a => (a.ClaimsSupportedCount ?? 0) + (a.ClaimsRefutedCount ?? 0) + (a.ClaimsIndeterminateCount ?? 0) > 0);
        run.ClaimsSupportedCount = answers.Sum(a => a.ClaimsSupportedCount ?? 0);
        run.ClaimsRefutedCount = answers.Sum(a => a.ClaimsRefutedCount ?? 0);
        run.ClaimsIndeterminateCount = answers.Sum(a => a.ClaimsIndeterminateCount ?? 0);
        run.BudgetSaturatedAnswerCount = answers.Count(
            a => a.ToolCallBudgetUsed.HasValue
                 && a.ToolCallCount.HasValue
                 && a.ToolCallCount.Value >= a.ToolCallBudgetUsed.Value
                 && (a.ToolCallsBlocked ?? 0) == 0
                 && !a.ToolBudgetExhausted);
        run.ReassessedAnswerCount = answers.Count(a => a.ReassessmentCount > 0);

        // Grader agreement. Manual verdicts are excluded: those come from a third model an
        // operator picked by hand for a trial, and this measures the run's own two graders.
        //
        // Coverage travels with the figure everywhere it is shown, because the two are not
        // separable. A mean delta over trigger-selected answers is conditioned on the first
        // assessor's own uncertainty and says nothing about the instrument; the same number over
        // every answer is an inter-rater agreement rate. Only the count distinguishes them.
        var secondOpinions = answers
            .Where(a => a.SecondOpinionQualityScore.HasValue
                        && a.QualityScore.HasValue
                        && !string.Equals(a.SecondOpinionTrigger, "Manual", StringComparison.Ordinal))
            .ToList();

        run.SecondOpinionGradedAnswerCount = secondOpinions.Count;
        run.SecondOpinionMeanAbsDelta = secondOpinions.Count > 0
            ? secondOpinions.Average(a => Math.Abs(a.SecondOpinionQualityScore!.Value - a.QualityScore!.Value))
            : null;
        run.SecondOpinionMeanSignedDelta = secondOpinions.Count > 0
            ? secondOpinions.Average(a => (double)(a.SecondOpinionQualityScore!.Value - a.QualityScore!.Value))
            : null;
        run.SecondOpinionCriticalErrorSplitCount = secondOpinions.Count(
            a => a.SecondOpinionCriticalError.HasValue && a.SecondOpinionCriticalError.Value != a.CriticalError);
        run.ToolOverheadMs = answers.Any(a => a.ToolTimeMs.HasValue)
            ? answers.Sum(a => a.ToolTimeMs ?? 0L)
            : null;

        var scorableItems = answers
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok)
            .Select(a => (a.QualityScore, a.AssessedDifficulty ?? FallbackDifficulty(a.Difficulty)))
            .ToList();

        run.QualityIndex = BenchmarkScoring.QualityIndex(scorableItems);
        run.QualityIndexStandardError = BenchmarkScoring.QualityIndexStandardError(scorableItems);

        // The plain mean of the same scores. Not a rival to the index above — a companion to it:
        // the gap between them is how much difficulty weighting moved the headline, which is
        // invisible from either number alone. On the 2026-09-03 run they were 94 and 92, because
        // the two weakest answers were also two of the easiest questions.
        run.UnweightedQualityIndex = BenchmarkScoring.UnweightedQualityMean(
            answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok).Select(a => a.QualityScore));

        // Equal weight: difficulty already scales each question's own speed target.
        run.SpeedIndex = BenchmarkScoring.SpeedIndex(
            answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok).Select(a => a.SpeedScore));

        run.CompletedAtUtc = DateTime.UtcNow;
        run.Status = ComputeStatus(answers);
    }
}
