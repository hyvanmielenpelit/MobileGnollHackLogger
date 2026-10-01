namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;

/// <summary>
/// Resolves an answer's critical-error flags under scoring method 13 and later
/// (<see cref="BenchmarkCriticalErrorResolution"/>). In a panel run a flag both members raise is
/// agreed; a flag one member raises is settled by the claim verifier's effective verdict on that
/// member's quote, which decides the two scores the panel score averages. A single-assessor run
/// records whether its assessor flagged, and its score is unchanged.
/// </summary>
public static class BenchmarkCriticalErrorResolver
{
    /// <summary>The first scoring method that resolves critical errors.</summary>
    public const int FirstScoringMethod = 13;

    /// <summary>Whether <paramref name="run"/> was scored under a method that resolves critical errors.</summary>
    public static bool Applies(BenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.ScoringMethodVersion >= FirstScoringMethod;
    }

    /// <summary>The critical-error ceiling of the run's recorded scoring profile.</summary>
    public static int CeilingOf(BenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return BenchmarkScoring.ConstantsFromSnapshot(run.ScoringProfileSnapshotJson).CriticalErrorCeiling;
    }

    /// <summary>
    /// The verifier's effective verdict on a critical-error quote, matched as
    /// <see cref="BenchmarkService.CriticalErrorQuoteWasSupported(IReadOnlyList{BenchmarkClaimVerification}, string)"/>
    /// matches it: the item at index 0 with the quote's exact text, then any item with that text, then
    /// the same item (<see cref="BenchmarkService.SameItem"/>). Null for no items, a blank quote, or no match.
    /// </summary>
    public static BenchmarkClaimVerdict? QuoteVerdict(
        IReadOnlyList<BenchmarkClaimVerification>? verifications,
        string? quote)
    {
        if (verifications == null || verifications.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(quote)) return null;

        string trimmed = quote.Trim();

        var match = verifications.FirstOrDefault(
            v => v.ClaimIndex == 0 && string.Equals(v.Claim?.Trim(), trimmed, StringComparison.Ordinal));
        match ??= verifications.FirstOrDefault(
            v => string.Equals(v.Claim?.Trim(), trimmed, StringComparison.Ordinal));
        match ??= verifications.FirstOrDefault(v => BenchmarkService.SameItem(v.Claim, trimmed));

        return match?.EffectiveVerdict;
    }

    /// <summary>
    /// <see cref="QuoteVerdict(IReadOnlyList{BenchmarkClaimVerification}, string)"/> for
    /// <paramref name="view"/>'s quote, over the items that member raised.
    /// </summary>
    public static BenchmarkClaimVerdict? QuoteVerdict(
        IReadOnlyList<BenchmarkClaimVerification>? verifications,
        BenchmarkVerdictView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return QuoteVerdict(verifications, view.Member, view.CriticalErrorQuote);
    }

    private static BenchmarkClaimVerdict? QuoteVerdict(
        IReadOnlyList<BenchmarkClaimVerification>? verifications,
        BenchmarkPanelMember member,
        string? quote)
        => QuoteVerdict(BenchmarkService.RaisedByMembers(verifications, member), quote);

    /// <summary>
    /// A panel answer's resolution and the two member scores its panel score averages. Null unless
    /// both members scored with a quality score.
    ///
    /// Neither flags: <c>None</c>, both as graded. Both flag: <c>Agreed</c>, both as graded. One flags
    /// and the verifier refuted its quote: <c>UpheldByVerifier</c>, the other member capped at
    /// <paramref name="ceiling"/>. Supported: <c>OverturnedByVerifier</c>, the flagging member's pre-cap
    /// score. Indeterminate, or no matching item: <c>Unresolved</c>, both as graded. Absent or invalid
    /// <see cref="BenchmarkRunAnswer.ClaimVerificationJson"/> reads as no items.
    /// </summary>
    public static (BenchmarkCriticalErrorResolution Resolution, int ScoreA, int ScoreB)? ResolvePanel(
        BenchmarkRunAnswer answer,
        int ceiling)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (answer.AssessmentStatus != BenchmarkAssessmentStatus.Scored
            || answer.QualityScore is not int scoreA
            || answer.CoAssessmentStatus != BenchmarkAssessmentStatus.Scored
            || answer.CoAssessmentQualityScore is not int scoreB)
        {
            return null;
        }

        bool flagA = answer.CriticalError;
        bool flagB = answer.CoAssessmentCriticalError ?? false;

        if (!flagA && !flagB) return (BenchmarkCriticalErrorResolution.None, scoreA, scoreB);
        if (flagA && flagB) return (BenchmarkCriticalErrorResolution.Agreed, scoreA, scoreB);

        var flagging = flagA ? BenchmarkPanelMember.A : BenchmarkPanelMember.B;
        string? quote = flagA
            ? answer.CriticalErrorQuote
            : BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)?.CriticalErrorQuote;
        var verifications = BenchmarkReportContent.ReadVerifications(answer.ClaimVerificationJson);

        return QuoteVerdict(verifications, flagging, quote) switch
        {
            BenchmarkClaimVerdict.Refuted => flagA
                ? (BenchmarkCriticalErrorResolution.UpheldByVerifier, scoreA, Math.Min(scoreB, ceiling))
                : (BenchmarkCriticalErrorResolution.UpheldByVerifier, Math.Min(scoreA, ceiling), scoreB),
            BenchmarkClaimVerdict.Supported => flagA
                ? (BenchmarkCriticalErrorResolution.OverturnedByVerifier, answer.RawQualityScore ?? scoreA, scoreB)
                : (BenchmarkCriticalErrorResolution.OverturnedByVerifier, scoreA, answer.CoAssessmentRawQualityScore ?? scoreB),
            _ => (BenchmarkCriticalErrorResolution.Unresolved, scoreA, scoreB)
        };
    }

    /// <summary>
    /// Writes the resolution onto <paramref name="answer"/>. In a panel run: the panel score from
    /// <see cref="ResolvePanel"/>, <see cref="BenchmarkRunAnswer.PanelDisagreed"/> from the members' own
    /// scores and flags, and the resolution, or all three null when <see cref="ResolvePanel"/> is null.
    /// Otherwise: <c>SingleAssessor</c> or <c>None</c> on a scored answer, null on an unscored one.
    /// </summary>
    public static void ApplyTo(BenchmarkRunAnswer answer, BenchmarkRun run, bool isPanelRun)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(run);

        if (isPanelRun)
        {
            var resolved = ResolvePanel(answer, CeilingOf(run));
            if (resolved is { } r)
            {
                answer.PanelQualityScore = (r.ScoreA + r.ScoreB) / 2.0;
                answer.PanelDisagreed = Math.Abs(answer.QualityScore!.Value - answer.CoAssessmentQualityScore!.Value)
                        > BenchmarkService.SecondOpinionDisagreementPoints
                    || answer.CriticalError != (answer.CoAssessmentCriticalError ?? false);
                answer.CriticalErrorResolution = r.Resolution;
            }
            else
            {
                answer.PanelQualityScore = null;
                answer.PanelDisagreed = null;
                answer.CriticalErrorResolution = null;
            }

            return;
        }

        answer.CriticalErrorResolution = answer.AssessmentStatus == BenchmarkAssessmentStatus.Scored
            ? (answer.CriticalError ? BenchmarkCriticalErrorResolution.SingleAssessor : BenchmarkCriticalErrorResolution.None)
            : null;
    }

    /// <summary>A confirmed critical error: resolved <c>Agreed</c>, <c>UpheldByVerifier</c> or <c>SingleAssessor</c>.</summary>
    public static bool IsConfirmed(BenchmarkRunAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return answer.CriticalErrorResolution is BenchmarkCriticalErrorResolution.Agreed
            or BenchmarkCriticalErrorResolution.UpheldByVerifier
            or BenchmarkCriticalErrorResolution.SingleAssessor;
    }
}
