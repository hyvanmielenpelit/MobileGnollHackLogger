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

    /// <summary>
    /// An answer with no text stays here even when scoring method 10 has scored it 0: an unanswered
    /// question is an error, not merely a low score, so it keeps the run at CompletedWithErrors. The
    /// name predates that second meaning — nothing about a scored empty answer can be resolved by a
    /// retry.
    /// </summary>
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
        | BenchmarkAnswerFlags.ContestedCriticalError
        | BenchmarkAnswerFlags.ContestedAccuracyDeduction
        | BenchmarkAnswerFlags.OmissionAsAccuracy
        | BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction
        | BenchmarkAnswerFlags.AnswerFramingOpener;

    /// <summary>
    /// The harness version that first adjudicated an out-of-rubric Accuracy deduction, and so the
    /// first whose <see cref="BenchmarkRun.ContestedAccuracyDeductionAnswerCount"/> is a measurement.
    /// </summary>
    private const int OutOfRubricAdjudicationHarnessVersion = 20;

    private static bool PredatesOutOfRubricAdjudication(BenchmarkRun run)
        => int.TryParse(run.HarnessVersion, out int version) && version < OutOfRubricAdjudicationHarnessVersion;

    /// <summary>
    /// Provider finish reasons that mean "the model chose to stop here". `tool_use` is deliberately
    /// absent: a turn that ended asking for a tool and produced no text is a loop problem, not a
    /// refusal to answer.
    /// </summary>
    private static readonly string[] NormalStopReasons =
        { "stop", "end_turn", "completed", "complete", "stop_sequence" };

    /// <summary>
    /// The model finished normally and produced no answer text: a failure to answer, scored 0 by
    /// scoring method 10. Distinct from a transport defect, which is not the candidate's fault and
    /// stays unscored. A null or unrecognised reason keeps the old classification — "not recorded" is
    /// not evidence of a normal stop, and an unmatched reason must never silently reclassify an answer.
    /// </summary>
    public static bool IsModelProducedEmptyAnswer(BenchmarkRunAnswer answer)
    {
        if (answer.Status != BenchmarkAnswerStatus.EmptyAnswer) return false;
        if (string.IsNullOrWhiteSpace(answer.ProviderFinishReason)) return false;

        return NormalStopReasons.Contains(answer.ProviderFinishReason, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether this answer contributes to the quality indices. Ok answers, and answers the model failed
    /// to produce — the latter at 0, under scoring method 10. Every consumer that recomputes an index
    /// must use this: the run's stored index and any index computed elsewhere have to be over the same
    /// item set, or two numbers describing one run disagree with no way to tell which is right.
    /// </summary>
    public static bool CountsTowardQualityIndex(BenchmarkRunAnswer answer)
    {
        return answer.Status == BenchmarkAnswerStatus.Ok || IsModelProducedEmptyAnswer(answer);
    }

    /// <summary>
    /// The one rounding every assessor-agreement delta receives: one decimal, midpoints away from
    /// zero, so -0.25 is -0.3 on every surface.
    /// </summary>
    public static double RoundAgreementDelta(double value)
        => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The answer never completed: the provider failed the request, or the harness caught a throw.
    /// Neither leaves text to grade, so both belong outside the clean count. There is no
    /// <c>Ok</c>-status path into this predicate, which is why it is safe ahead of every other
    /// bucket check.
    /// </summary>
    public static bool HasTerminalFailure(BenchmarkRunAnswer answer)
        => answer.Status is BenchmarkAnswerStatus.ProviderError or BenchmarkAnswerStatus.Failed;

    /// <summary>A transport or provider defect corrupted this answer beyond recovery.</summary>
    public static bool HasTransportDefect(BenchmarkRunAnswer answer)
    {
        // A model that ended its turn normally and returned nothing has not suffered a transport
        // defect — it failed to answer, and scoring method 10 scores that 0 rather than excusing it.
        // The run still reports CompletedWithErrors: see HasUnresolvedWork.
        if (IsModelProducedEmptyAnswer(answer)) return false;

        return HasTerminalFailure(answer)
            || answer.Status == BenchmarkAnswerStatus.EmptyAnswer
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
    /// Which of the five mutually exclusive integrity buckets this answer belongs to. The order
    /// is the precedence: the most severe applicable class wins, which is what keeps
    /// clean + transport defects + recovered + harness limits + unanswered equal to the question
    /// count.
    ///
    /// A <see cref="HasTerminalFailure"/> answer — <c>ProviderError</c> or <c>Failed</c> — takes the
    /// transport-defect bucket ahead of every other class, through
    /// <see cref="HasTransportDefect"/>. No index, no dimensional score and no run status depends on
    /// the bucket, so this precedence carries no <c>ScoringMethodVersion</c> movement; what it
    /// changes is the clean count and the provider-error count, which is a
    /// <c>HarnessVersion</c> concern.
    /// </summary>
    public static BenchmarkAnswerIntegrity Classify(BenchmarkRunAnswer answer)
    {
        if (HasTransportDefect(answer)) return BenchmarkAnswerIntegrity.TransportDefect;
        if (IsModelProducedEmptyAnswer(answer)) return BenchmarkAnswerIntegrity.Unanswered;
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

    /// <summary>
    /// Run-level sums of the per-answer long-context buckets — a subset of the candidate totals above, not
    /// tokens in addition to them. All zero for a flat-rate model and for every answer recorded before
    /// tiered pricing existed, whose columns are null.
    /// </summary>
    public static (long TotalLongContextInputTokens, long TotalLongContextOutputTokens, long TotalLongContextCacheReadTokens, long TotalLongContextCacheCreationTokens) ComputeCandidateLongContextTotals(IEnumerable<BenchmarkRunAnswer> answers)
    {
        return (
            answers.Sum(a => (long)(a.LongContextInputTokens ?? 0)),
            answers.Sum(a => (long)(a.LongContextOutputTokens ?? 0)),
            answers.Sum(a => (long)(a.LongContextCacheReadTokens ?? 0)),
            answers.Sum(a => (long)(a.LongContextCacheCreationTokens ?? 0))
        );
    }

    /// <summary>
    /// The service tier the provider actually served for this run's candidate answers — the most common
    /// non-null ActualServiceTierUsed. Costing needs the served tier, not the requested one: OpenAI
    /// requests "auto"/"fast" and serves "default"/"priority", and a priority request that was served
    /// default must be billed as default. Null when no answer reported one, in which case costing falls
    /// back to the requested tier and then to a 1.0x multiplier.
    /// </summary>
    public static string? ResolveServedServiceTier(IEnumerable<BenchmarkRunAnswer> answers)
    {
        return answers
            .Select(a => a.ActualServiceTierUsed)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .GroupBy(t => t!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    /// <summary>
    /// Per-role token and duration sums over a run's answers: primary assessor, second opinion, and
    /// claim verifier. No synthesis members — the final synthesis has no per-answer row to sum from,
    /// and <see cref="BenchmarkService"/> assigns its totals onto <see cref="BenchmarkRun"/> directly.
    /// </summary>
    internal readonly record struct BenchmarkGradingTotals(
        long TotalAssessmentInputTokens,
        long TotalAssessmentOutputTokens,
        long TotalAssessmentCacheReadTokens,
        long TotalAssessmentCacheCreationTokens,
        long TotalAssessmentDurationMs,
        long TotalSecondOpinionInputTokens,
        long TotalSecondOpinionOutputTokens,
        long TotalSecondOpinionCacheReadTokens,
        long TotalSecondOpinionCacheCreationTokens,
        long TotalSecondOpinionDurationMs,
        long TotalClaimVerificationInputTokens,
        long TotalClaimVerificationOutputTokens,
        long TotalClaimVerificationCacheReadTokens,
        long TotalClaimVerificationCacheCreationTokens,
        long TotalClaimVerificationDurationMs);

    /// <summary>
    /// Sums the per-role grading totals over a run's answers. Shared with AdminBenchmarkController
    /// for live run-detail reporting while a run is pending or running, so the mid-run figure and the
    /// finalized one come from one copy of the arithmetic.
    /// </summary>
    internal static BenchmarkGradingTotals SumGradingTotals(IReadOnlyCollection<BenchmarkRunAnswer> answers)
    {
        return new BenchmarkGradingTotals(
            TotalAssessmentInputTokens: answers.Sum(a => (long)(a.AssessmentInputTokens ?? 0)),
            TotalAssessmentOutputTokens: answers.Sum(a => (long)(a.AssessmentOutputTokens ?? 0)),
            TotalAssessmentCacheReadTokens: answers.Sum(a => (long)(a.AssessmentCacheReadTokens ?? 0)),
            TotalAssessmentCacheCreationTokens: answers.Sum(a => (long)(a.AssessmentCacheCreationTokens ?? 0)),
            TotalAssessmentDurationMs: answers.Sum(a => a.AssessmentDurationMs ?? 0L),
            TotalSecondOpinionInputTokens: answers.Sum(a => (long)(a.SecondOpinionInputTokens ?? 0)),
            TotalSecondOpinionOutputTokens: answers.Sum(a => (long)(a.SecondOpinionOutputTokens ?? 0)),
            TotalSecondOpinionCacheReadTokens: answers.Sum(a => (long)(a.SecondOpinionCacheReadTokens ?? 0)),
            TotalSecondOpinionCacheCreationTokens: answers.Sum(a => (long)(a.SecondOpinionCacheCreationTokens ?? 0)),
            TotalSecondOpinionDurationMs: answers.Sum(a => a.SecondOpinionDurationMs ?? 0L),
            TotalClaimVerificationInputTokens: answers.Sum(a => (long)(a.ClaimVerificationInputTokens ?? 0)),
            TotalClaimVerificationOutputTokens: answers.Sum(a => (long)(a.ClaimVerificationOutputTokens ?? 0)),
            TotalClaimVerificationCacheReadTokens: answers.Sum(a => (long)(a.ClaimVerificationCacheReadTokens ?? 0)),
            TotalClaimVerificationCacheCreationTokens: answers.Sum(a => (long)(a.ClaimVerificationCacheCreationTokens ?? 0)),
            TotalClaimVerificationDurationMs: answers.Sum(a => a.ClaimVerificationDurationMs ?? 0L));
    }

    /// <summary>
    /// The measured totals: token sums, durations, integrity and advisory counts, and grader
    /// agreement. Nothing here is a score, and nothing here decides the run's status, which is what
    /// lets a run that stopped early use it on its own — such a run has a real cost and a real elapsed
    /// time, but a quality index over whichever questions happened to finish is not the suite's index.
    /// Synthesis totals are untouched here: they are assigned directly onto the run elsewhere and
    /// have no per-answer row to recompute from.
    /// </summary>
    public static void ApplyTotals(BenchmarkRun run, IReadOnlyCollection<BenchmarkRunAnswer> answers)
    {
        var candidateTotals = ComputeCandidateTotals(answers);
        run.TotalInputTokens = candidateTotals.TotalInputTokens;
        run.TotalOutputTokens = candidateTotals.TotalOutputTokens;
        run.TotalCacheReadTokens = candidateTotals.TotalCacheReadTokens;
        run.TotalCacheCreationTokens = candidateTotals.TotalCacheCreationTokens;
        run.TotalAnswerDurationMs = candidateTotals.TotalAnswerDurationMs;

        var longContextTotals = ComputeCandidateLongContextTotals(answers);
        run.TotalLongContextInputTokens = longContextTotals.TotalLongContextInputTokens;
        run.TotalLongContextOutputTokens = longContextTotals.TotalLongContextOutputTokens;
        run.TotalLongContextCacheReadTokens = longContextTotals.TotalLongContextCacheReadTokens;
        run.TotalLongContextCacheCreationTokens = longContextTotals.TotalLongContextCacheCreationTokens;

        // Assessor, second-opinion and claim-verifier sides, kept separate from the candidate totals
        // above: the run's cost is all of these together, and the model under test must not be
        // charged for its graders.
        var gradingTotals = SumGradingTotals(answers);
        run.TotalAssessmentInputTokens = gradingTotals.TotalAssessmentInputTokens;
        run.TotalAssessmentOutputTokens = gradingTotals.TotalAssessmentOutputTokens;
        run.TotalAssessmentCacheReadTokens = gradingTotals.TotalAssessmentCacheReadTokens;
        run.TotalAssessmentCacheCreationTokens = gradingTotals.TotalAssessmentCacheCreationTokens;
        run.TotalAssessmentDurationMs = gradingTotals.TotalAssessmentDurationMs;

        run.TotalSecondOpinionInputTokens = gradingTotals.TotalSecondOpinionInputTokens;
        run.TotalSecondOpinionOutputTokens = gradingTotals.TotalSecondOpinionOutputTokens;
        run.TotalSecondOpinionCacheReadTokens = gradingTotals.TotalSecondOpinionCacheReadTokens;
        run.TotalSecondOpinionCacheCreationTokens = gradingTotals.TotalSecondOpinionCacheCreationTokens;
        run.TotalSecondOpinionDurationMs = gradingTotals.TotalSecondOpinionDurationMs;

        run.TotalClaimVerificationInputTokens = gradingTotals.TotalClaimVerificationInputTokens;
        run.TotalClaimVerificationOutputTokens = gradingTotals.TotalClaimVerificationOutputTokens;
        run.TotalClaimVerificationCacheReadTokens = gradingTotals.TotalClaimVerificationCacheReadTokens;
        run.TotalClaimVerificationCacheCreationTokens = gradingTotals.TotalClaimVerificationCacheCreationTokens;
        run.TotalClaimVerificationDurationMs = gradingTotals.TotalClaimVerificationDurationMs;

        run.AnsweredQuestionCount = answers.Count(a => a.Status == BenchmarkAnswerStatus.Ok);
        run.UnansweredQuestionCount = answers.Count(IsModelProducedEmptyAnswer);

        // The index weights each item by its assessed difficulty, and an unanswered question has none —
        // no grader read it — so it is weighted by its authored band's fallback.
        //
        // Two guards make this false on every run that can exist, and it is kept as a defensive
        // assertion rather than a measurement: BenchmarkService coalesces the authored fallback into
        // BenchmarkRunAnswer.AssessedDifficulty when it stores the answer, so the column is never null;
        // and BenchmarkRunLauncher refuses to launch a suite carrying any question without an assessed
        // difficulty, so the fallback is unreachable upstream of that. FallbackDifficulty itself is still
        // live — it is the defensive default below and the report's assessed-difficulty label.
        run.DifficultyFallbackUsed = answers.Any(a => CountsTowardQualityIndex(a) && a.AssessedDifficulty == null);
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
        run.OutOfRubricAccuracyAnswerCount = answers.Count(
            a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction) != 0);
        run.AnswerFramingOpenerAnswerCount = answers.Count(
            a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.AnswerFramingOpener) != 0);
        run.RefutedClaimAnswerCount = answers.Count(
            a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.RefutedClaim) != 0);
        run.ContestedCriticalErrorAnswerCount = answers.Count(
            a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.ContestedCriticalError) != 0);
        // Left null, "not recorded", on a run stamped before harness 20 that carries no such flag:
        // that harness never adjudicated the basis, so re-finalizing it must not turn the absence
        // into a zero.
        int contestedAccuracyDeductions = answers.Count(
            a => (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.ContestedAccuracyDeduction) != 0);
        if (contestedAccuracyDeductions > 0 || !PredatesOutOfRubricAdjudication(run))
        {
            run.ContestedAccuracyDeductionAnswerCount = contestedAccuracyDeductions;
        }
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
        //
        // CountsTowardQualityIndex is the same population scorableItems uses in Apply below. An
        // answer the index excludes has no gradeable text, so a delta against it measures two
        // graders reading a transport failure, not their agreement about a candidate.
        var secondOpinions = answers
            .Where(CountsTowardQualityIndex)
            .Where(a => a.SecondOpinionQualityScore.HasValue
                        && a.QualityScore.HasValue
                        && !string.Equals(a.SecondOpinionTrigger, "Manual", StringComparison.Ordinal))
            .ToList();

        // Stored at one decimal, midpoints away from zero, so every surface that formats the
        // figure (UI toFixed(1), report F1, diagnostics) prints the same value.
        run.SecondOpinionGradedAnswerCount = secondOpinions.Count;
        run.SecondOpinionMeanAbsDelta = secondOpinions.Count > 0
            ? RoundAgreementDelta(secondOpinions.Average(a => Math.Abs(a.SecondOpinionQualityScore!.Value - a.QualityScore!.Value)))
            : null;
        run.SecondOpinionMeanSignedDelta = secondOpinions.Count > 0
            ? RoundAgreementDelta(secondOpinions.Average(a => (double)(a.SecondOpinionQualityScore!.Value - a.QualityScore!.Value)))
            : null;
        run.SecondOpinionCriticalErrorSplitCount = secondOpinions.Count(
            a => a.SecondOpinionCriticalError.HasValue && a.SecondOpinionCriticalError.Value != a.CriticalError);
        run.ToolOverheadMs = answers.Any(a => a.ToolTimeMs.HasValue)
            ? answers.Sum(a => a.ToolTimeMs ?? 0L)
            : null;
    }

    /// <summary>
    /// <paramref name="preserveCompletedAt"/> keeps an already-recorded <c>CompletedAtUtc</c>: a
    /// failed-question re-run finishes long after the run it repairs, and the run's elapsed wall time
    /// belongs to the original execution. The default is the first-run behaviour.
    /// </summary>
    public static void Apply(
        BenchmarkRun run,
        IReadOnlyCollection<BenchmarkRunAnswer> answers,
        bool preserveCompletedAt = false)
    {
        ApplyTotals(run, answers);

        // A non-gradeable answer has no measured latency to score. The SpeedIndex filter already
        // excluded it, so nulling the stored figure moves no published index — it stops persisting a
        // number that describes nothing, which the report then had to be trusted not to print.
        foreach (var answer in answers.Where(a => !CountsTowardQualityIndex(a)))
        {
            answer.SpeedScore = null;
        }

        var scorableItems = answers
            .Where(CountsTowardQualityIndex)
            .Select(a => (a.QualityScore, a.AssessedDifficulty ?? FallbackDifficulty(a.Difficulty)))
            .ToList();

        run.QualityIndex = BenchmarkScoring.QualityIndex(scorableItems);
        run.QualityIndexStandardError = BenchmarkScoring.QualityIndexStandardError(scorableItems);

        // The plain mean of the same scores. Not a rival to the index above — a companion to it:
        // the gap between them is how much difficulty weighting moved the headline, which is
        // invisible from either number alone. On the 2026-09-03 run they were 94 and 92, because
        // the two weakest answers were also two of the easiest questions.
        run.UnweightedQualityIndex = BenchmarkScoring.UnweightedQualityMean(
            answers.Where(CountsTowardQualityIndex).Select(a => a.QualityScore));

        // Equal weight: difficulty already scales each question's own speed target.
        run.SpeedIndex = BenchmarkScoring.SpeedIndex(
            answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok).Select(a => a.SpeedScore));

        if (!preserveCompletedAt || run.CompletedAtUtc == null)
        {
            run.CompletedAtUtc = DateTime.UtcNow;
        }

        run.Status = ComputeStatus(answers);
    }
}
