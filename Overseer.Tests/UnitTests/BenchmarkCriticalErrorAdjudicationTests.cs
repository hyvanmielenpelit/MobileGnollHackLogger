namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The critical-error quote as a verifiable claim: which answers reach the claim verifier on that
/// ground, what claim list they are checked against, which verdict raises the advisory
/// <see cref="BenchmarkAnswerFlags.ContestedCriticalError"/> flag, and what the verifier prompt says
/// about it.
/// </summary>
public class BenchmarkCriticalErrorAdjudicationTests
{
    private const string Quote = "Praying on an unaligned altar at 1 HP is always safe.";

    private static BenchmarkRunAnswer Answer(
        bool criticalError = false,
        string? criticalErrorQuote = null,
        int? unverifiedClaimCount = null,
        string? unverifiedClaimsJson = null)
    {
        return new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "When is prayer safe?",
            AnswerText = "Graded answer text.",
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            Difficulty = BenchmarkDifficulty.Advanced,
            AssessedDifficulty = 78,
            CriticalError = criticalError,
            CriticalErrorQuote = criticalErrorQuote,
            UnverifiedClaimCount = unverifiedClaimCount,
            UnverifiedClaimsJson = unverifiedClaimsJson
        };
    }

    // --- Dispatch predicate ---

    [Fact]
    public void CriticalErrorWithAQuote_AndNoUnverifiedClaims_IsDispatchedToTheVerifier()
    {
        var answer = Answer(criticalError: true, criticalErrorQuote: Quote);

        Assert.True(BenchmarkService.IsCriticalErrorAdjudication(answer));
        Assert.True(BenchmarkService.NeedsClaimVerification(answer));
    }

    [Fact]
    public void CriticalErrorWithoutAQuote_IsNotDispatched()
    {
        // Nothing to look up: the assessor named no sentence, so the finding is unverifiable and the
        // verifier is not spent on it.
        Assert.False(BenchmarkService.NeedsClaimVerification(
            Answer(criticalError: true, criticalErrorQuote: null)));
        Assert.False(BenchmarkService.NeedsClaimVerification(
            Answer(criticalError: true, criticalErrorQuote: "   ")));
    }

    [Fact]
    public void AQuoteWithoutTheCriticalErrorFlag_IsNotDispatched()
    {
        // A quote left over from a demoted or withdrawn finding is not itself a critical error.
        Assert.False(BenchmarkService.NeedsClaimVerification(
            Answer(criticalError: false, criticalErrorQuote: Quote)));
    }

    [Fact]
    public void UnverifiedClaims_StillDispatch_WithNoCriticalErrorAtAll()
    {
        var answer = Answer(unverifiedClaimCount: 3);

        Assert.False(BenchmarkService.IsCriticalErrorAdjudication(answer));
        Assert.True(BenchmarkService.NeedsClaimVerification(answer));
    }

    // --- Claim list composition ---

    [Fact]
    public void TheQuoteIsSubmittedAtIndexZero_AheadOfTheUnadjudicableClaims()
    {
        var claims = new List<string> { "Gnolls have keen smell", "Master Kaen has AC -2" };

        var submitted = BenchmarkService.WithCriticalErrorQuoteFirst(claims, $"  {Quote}  ");

        Assert.Equal(3, submitted.Count);
        Assert.Equal(Quote, submitted[0]);
        Assert.Equal("Gnolls have keen smell", submitted[1]);
        Assert.Equal("Master Kaen has AC -2", submitted[2]);

        // The caller's own list is left alone.
        Assert.Equal(2, claims.Count);
    }

    [Fact]
    public void TheQuoteIsNotRepeated_WhenAnEntryAlreadyMatchesItVerbatim()
    {
        // ExtractDisputedClaims already places the quote first on a disputed answer, so this is the
        // ordinary shape for an answer that is both disputed and a critical-error adjudication.
        var claims = new List<string> { Quote, "Master Kaen has AC -2" };

        var submitted = BenchmarkService.WithCriticalErrorQuoteFirst(claims, Quote);

        Assert.Equal(2, submitted.Count);
        Assert.Equal(Quote, submitted[0]);
    }

    [Fact]
    public void AMissingQuoteAddsNothing_AndTheClaimListSurvivesUnchanged()
    {
        var claims = new List<string> { "Gnolls have keen smell" };

        Assert.Equal(claims, BenchmarkService.WithCriticalErrorQuoteFirst(claims, null));
        Assert.Empty(BenchmarkService.WithCriticalErrorQuoteFirst(null, null));
        Assert.Equal(new[] { Quote }, BenchmarkService.WithCriticalErrorQuoteFirst(null, Quote));
    }

    [Fact]
    public void TheQuoteNeverEntersTheUnadjudicableClaimColumns()
    {
        // UnverifiedClaimsJson means "claims the assessor could not adjudicate". A claim the
        // assessor called outright false is the opposite of one, so the quote is carried in the
        // prompt only and the two columns keep whatever the assessor put there.
        var answer = Answer(
            criticalError: true,
            criticalErrorQuote: Quote,
            unverifiedClaimCount: 1,
            unverifiedClaimsJson: JsonSerializer.Serialize(new[] { "Gnolls have keen smell" }));

        var stored = JsonSerializer.Deserialize<List<string>>(answer.UnverifiedClaimsJson!)!;
        var submitted = BenchmarkService.WithCriticalErrorQuoteFirst(stored, answer.CriticalErrorQuote);

        Assert.Equal(2, submitted.Count);
        Assert.Equal(1, answer.UnverifiedClaimCount);
        Assert.DoesNotContain(Quote, answer.UnverifiedClaimsJson);
    }

    // --- The advisory flag ---

    [Fact]
    public void ASupportedVerdictOnTheQuote_MarksTheCriticalErrorContested()
    {
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, Quote, BenchmarkClaimVerdict.Supported, "src/pray.c:812", "Found."),
            new BenchmarkClaimVerification(1, "Master Kaen has AC -2", BenchmarkClaimVerdict.Refuted, "src/monst.c:120", "Wrong.")
        };

        Assert.True(BenchmarkService.CriticalErrorQuoteWasSupported(verifications, Quote));
        Assert.True(BenchmarkService.CriticalErrorQuoteWasSupported(verifications, $" {Quote} "));
    }

    [Fact]
    public void ARefutedOrIndeterminateVerdictOnTheQuote_MarksNothing()
    {
        var refuted = new[]
        {
            new BenchmarkClaimVerification(0, Quote, BenchmarkClaimVerdict.Refuted, "src/pray.c:812", "The altar check fails.")
        };
        var indeterminate = new[]
        {
            new BenchmarkClaimVerification(0, Quote, BenchmarkClaimVerdict.Indeterminate, null, "Not located.")
        };

        Assert.False(BenchmarkService.CriticalErrorQuoteWasSupported(refuted, Quote));
        Assert.False(BenchmarkService.CriticalErrorQuoteWasSupported(indeterminate, Quote));
    }

    [Fact]
    public void ASupportedVerdictOnSomeOtherClaim_DoesNotMarkTheCriticalErrorContested()
    {
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, Quote, BenchmarkClaimVerdict.Indeterminate, null, "Not located."),
            new BenchmarkClaimVerification(1, "Gnolls have keen smell", BenchmarkClaimVerdict.Supported, "src/role.c:45", "Found.")
        };

        Assert.False(BenchmarkService.CriticalErrorQuoteWasSupported(verifications, Quote));
    }

    [Fact]
    public void NoVerifications_OrNoQuote_MarksNothing()
    {
        var supported = new[]
        {
            new BenchmarkClaimVerification(0, Quote, BenchmarkClaimVerdict.Supported, "src/pray.c:812", "Found.")
        };

        Assert.False(BenchmarkService.CriticalErrorQuoteWasSupported(null, Quote));
        Assert.False(BenchmarkService.CriticalErrorQuoteWasSupported(Array.Empty<BenchmarkClaimVerification>(), Quote));
        Assert.False(BenchmarkService.CriticalErrorQuoteWasSupported(supported, null));
        Assert.False(BenchmarkService.CriticalErrorQuoteWasSupported(supported, "  "));
    }

    // --- The verifier prompt preamble ---

    private static string BuildPrompt(bool disputed, bool criticalErrorAdjudication, string? assessorEvidence = null)
    {
        return BenchmarkClaimVerificationPrompt.BuildPrompt(
            "GnollHack Suite",
            1,
            "When is prayer safe?",
            "**REQUIRED** - prayer timeout must have elapsed.",
            new List<string> { Quote },
            new List<string> { "source_code_search" },
            15,
            isDisputedVerdict: disputed,
            isCriticalErrorAdjudication: criticalErrorAdjudication,
            assessorEvidence: assessorEvidence);
    }

    [Fact]
    public void ThePreambleAppearsOnlyUnderTheCriticalErrorFlag()
    {
        string with = BuildPrompt(disputed: false, criticalErrorAdjudication: true);
        string without = BuildPrompt(disputed: false, criticalErrorAdjudication: false);

        Assert.Contains("CRITICAL ERROR ADJUDICATION:", with);
        Assert.Contains("The first assessor marked the first claim below as a critical error", with);
        Assert.Contains("A claim absent from the rubric is not thereby false.", with);
        Assert.DoesNotContain("DISPUTED VERDICT ADJUDICATION:", with);

        Assert.DoesNotContain("CRITICAL ERROR ADJUDICATION:", without);
    }

    [Fact]
    public void BothPreamblesAreEmitted_WhenBothConditionsHold_DisputedFirst()
    {
        string prompt = BuildPrompt(disputed: true, criticalErrorAdjudication: true);

        int disputedAt = prompt.IndexOf("DISPUTED VERDICT ADJUDICATION:", StringComparison.Ordinal);
        int criticalAt = prompt.IndexOf("CRITICAL ERROR ADJUDICATION:", StringComparison.Ordinal);

        Assert.True(disputedAt >= 0 && criticalAt >= 0, "Both preambles must be present.");
        Assert.True(disputedAt < criticalAt, "The disputed-verdict preamble frames the task and comes first.");
    }

    [Fact]
    public void AssessorEvidenceIsCarried_UnderTheCriticalErrorFlagAlone()
    {
        const string evidence = "The rubric does not list unaligned altars at all.";

        string critical = BuildPrompt(disputed: false, criticalErrorAdjudication: true, assessorEvidence: evidence);
        Assert.Contains("Assessor Evidence / Counter-Claims:", critical);
        Assert.Contains(evidence, critical);

        string neither = BuildPrompt(disputed: false, criticalErrorAdjudication: false, assessorEvidence: evidence);
        Assert.DoesNotContain("Assessor Evidence / Counter-Claims:", neither);
    }
}
