namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;

/// <summary>
/// A panel run's verification-cleared Accuracy sensitivity: the Intelligence Index recomputed with
/// the lifted members' Accuracy one level higher, and the question order indices lifted for each
/// member. <see cref="Index"/> is null when neither list has an entry.
/// </summary>
public sealed record PanelSensitivityResult(double? Index, IReadOnlyList<int> LiftedA, IReadOnlyList<int> LiftedB)
{
    public static PanelSensitivityResult Empty { get; } = new(null, Array.Empty<int>(), Array.Empty<int>());
}

/// <summary>
/// The panel counterpart of the single-assessor verification-cleared Accuracy sensitivity, computed
/// per member so that neither member gains a correction channel the other lacks. A member's Accuracy
/// is lifted one level, capped at 6, on an answer both members scored, neither flagged as a critical
/// error, and where that member's Accuracy is below 6 and either (i) every sentence it charged
/// (<c>accusedQuote</c>) was supported with a citation, or (ii) it flagged an unevidenced deduction and
/// every ordinary claim it raised was supported. The answer's panel score becomes the mean of the two
/// member scores, one or both lifted; every other answer keeps its stored panel score. Advisory and
/// pure: nothing is written, and a charge the verifier wrongly refuted is not lifted, so the figure
/// is a lower bound.
/// </summary>
public static class BenchmarkPanelSensitivity
{
    /// <summary>
    /// The harness version that first recorded which member charged or suspected each item. Before it,
    /// <see cref="BenchmarkService.AccusedByMembers"/> falls back to <c>raisedBy</c>, or to both members
    /// on an unlabeled item, so the figure is approximate.
    /// </summary>
    public const int MemberAttributionHarnessVersion = 44;

    // The top of the 0-6 assessment level scale.
    private const int MaxAssessmentLevel = 6;

    /// <summary>Whether <paramref name="run"/> predates <see cref="MemberAttributionHarnessVersion"/>; an unversioned run does.</summary>
    public static bool IsApproximate(BenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return !int.TryParse(run.HarnessVersion, out int version) || version < MemberAttributionHarnessVersion;
    }

    /// <summary>
    /// The sensitivity over the answers of <paramref name="indexAnswers"/> that count toward the
    /// quality index, weighted by the difficulty the published index uses. <see cref="PanelSensitivityResult.Empty"/>
    /// for a single-assessor run or when no answer qualifies.
    /// </summary>
    public static PanelSensitivityResult Compute(
        BenchmarkRun run,
        IReadOnlyList<BenchmarkRunAnswer> indexAnswers,
        BenchmarkScoringConstants constants)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(indexAnswers);
        if (!BenchmarkRunFinalizer.IsPanelRun(run)) return PanelSensitivityResult.Empty;

        var liftedA = new List<int>();
        var liftedB = new List<int>();
        var items = new List<(double? QualityScore, int Difficulty)>();
        foreach (var answer in indexAnswers.Where(BenchmarkRunFinalizer.CountsTowardQualityIndex))
        {
            double? score = BenchmarkScoring.IndexQuality(answer, isPanelRun: true);
            if (LiftedPanelScore(answer, constants) is { } lifted)
            {
                score = lifted.Score;
                if (lifted.LiftedA) liftedA.Add(answer.OrderIndex);
                if (lifted.LiftedB) liftedB.Add(answer.OrderIndex);
            }
            items.Add((score, answer.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(answer.Difficulty)));
        }

        if (liftedA.Count == 0 && liftedB.Count == 0) return PanelSensitivityResult.Empty;

        liftedA.Sort();
        liftedB.Sort();
        int? index = BenchmarkScoring.QualityIndex(items);
        return new PanelSensitivityResult(index, liftedA, liftedB);
    }

    /// <summary>
    /// The answer's panel score with each eligible member lifted, and which members were; null when
    /// neither member is eligible.
    /// </summary>
    private static (double Score, bool LiftedA, bool LiftedB)? LiftedPanelScore(
        BenchmarkRunAnswer answer,
        BenchmarkScoringConstants constants)
    {
        if (answer.QualityScore is not int scoreA || answer.CoAssessmentQualityScore is not int scoreB) return null;
        var viewA = BenchmarkVerdictView.FromPrimary(answer);
        var viewB = BenchmarkVerdictView.FromCoAssessment(answer);
        if (viewA == null || viewB == null) return null;

        // The critical-error resolver decides a flagged answer's panel score; this figure stays out of it.
        if (viewA.CriticalError || viewB.CriticalError || answer.CriticalError || answer.CoAssessmentCriticalError == true) return null;

        var verifications = BenchmarkReportContent.ReadVerifications(answer.ClaimVerificationJson)
            ?? new List<BenchmarkClaimVerification>();
        if (verifications.Count == 0) return null;

        bool liftA = IsEligible(answer, viewA, verifications);
        bool liftB = IsEligible(answer, viewB, verifications);
        if (!liftA && !liftB) return null;

        int memberA = liftA ? LiftedScore(viewA, constants) : scoreA;
        int memberB = liftB ? LiftedScore(viewB, constants) : scoreB;
        return ((memberA + memberB) / 2.0, liftA, liftB);
    }

    private static int LiftedScore(BenchmarkVerdictView view, BenchmarkScoringConstants constants)
        => BenchmarkScoring.Quality(
            Math.Min(MaxAssessmentLevel, view.AccuracyLevel + 1),
            view.CompletenessLevel,
            view.ConcisenessLevel,
            view.ReadabilityLevel,
            criticalError: false,
            constants,
            view.NotAttempted).Score;

    /// <summary>
    /// Whether <paramref name="view"/>'s member qualifies on this answer: Accuracy below 6, and every
    /// sentence it charged supported with a citation, or its unevidenced deduction verification-cleared.
    /// </summary>
    internal static bool IsEligible(
        BenchmarkRunAnswer answer,
        BenchmarkVerdictView view,
        IReadOnlyList<BenchmarkClaimVerification> verifications)
    {
        if (view.AccuracyLevel >= MaxAssessmentLevel) return false;
        return AccusationsAllSupported(view.Member, verifications)
            || IsVerificationClearedDeduction(answer, view, verifications);
    }

    /// <summary>The member charged at least one sentence, and the verifier supported every one with a citation.</summary>
    private static bool AccusationsAllSupported(
        BenchmarkPanelMember member,
        IReadOnlyList<BenchmarkClaimVerification> verifications)
    {
        var accused = BenchmarkService.AccusedByMembers(verifications, member)
            .Where(v => BenchmarkClaimRoles.HasRole(v, BenchmarkClaimRoles.AccusedQuote))
            .ToList();
        return accused.Count > 0
            && accused.All(v => v.EffectiveVerdict == BenchmarkClaimVerdict.Supported && !string.IsNullOrWhiteSpace(v.Citation));
    }

    /// <summary>
    /// The per-member twin of <see cref="BenchmarkService.IsVerificationClearedAccuracyDeduction"/>: the
    /// member flagged an unevidenced deduction at Accuracy at most
    /// <see cref="BenchmarkVerdictConsistency.UnevidencedDeductionMaxLevel"/>, raised at least one
    /// ordinary claim, and every claim it raised was supported.
    /// </summary>
    private static bool IsVerificationClearedDeduction(
        BenchmarkRunAnswer answer,
        BenchmarkVerdictView view,
        IReadOnlyList<BenchmarkClaimVerification> verifications)
    {
        bool unevidenced = view.Member == BenchmarkPanelMember.B
            ? BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)?.Flags?.UnevidencedDeduction == true
            : ((BenchmarkAnswerFlags)answer.AnswerFlags).HasFlag(BenchmarkAnswerFlags.UnevidencedDeduction);
        if (!unevidenced || view.AccuracyLevel > BenchmarkVerdictConsistency.UnevidencedDeductionMaxLevel) return false;

        var raised = BenchmarkService.RaisedByMembers(
            BenchmarkService.OrdinaryClaimVerifications(verifications, answer),
            view.Member);
        return raised.Count > 0 && raised.All(v => v.EffectiveVerdict == BenchmarkClaimVerdict.Supported);
    }
}
