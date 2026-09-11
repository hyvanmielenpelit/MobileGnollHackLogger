namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The basis of an out-of-rubric Accuracy deduction as a verifiable claim: what is extracted from
/// the assessor's "Not in rubric:" evidence, which answers reach the claim verifier on that ground,
/// where the basis sits in the claim list, which verdict raises the advisory
/// <see cref="BenchmarkAnswerFlags.ContestedAccuracyDeduction"/> flag, how the basis is kept out of
/// the answer's own claim figures, and what the verifier and synthesis prompts say about it.
/// </summary>
public class BenchmarkOutOfRubricAdjudicationTests
{
    private const string Basis = "The prayer timeout reset rnz(175) / rnz(350) is independent of player experience level.";
    private const string Quote = "Praying on an unaligned altar at 1 HP is always safe.";

    private static BenchmarkRunAnswer Answer(
        BenchmarkAnswerFlags flags = BenchmarkAnswerFlags.None,
        string? accuracyEvidence = null,
        bool criticalError = false,
        string? criticalErrorQuote = null,
        int? unverifiedClaimCount = null,
        string? unverifiedClaimsJson = null)
    {
        return new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "How long is the prayer timeout?",
            AnswerText = "Graded answer text.",
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            Difficulty = BenchmarkDifficulty.Advanced,
            AssessedDifficulty = 78,
            AnswerFlags = (int)flags,
            AssessmentEvidenceJson = accuracyEvidence == null
                ? null
                : JsonSerializer.Serialize(new { accuracy = accuracyEvidence }),
            CriticalError = criticalError,
            CriticalErrorQuote = criticalErrorQuote,
            UnverifiedClaimCount = unverifiedClaimCount,
            UnverifiedClaimsJson = unverifiedClaimsJson
        };
    }

    // --- Basis extraction ---

    [Fact]
    public void TheMarkersOwnSentenceIsExtracted_OnTheRun36Shape()
    {
        const string evidence =
            "Level 4: the timeout mechanics are otherwise right. Not in rubric: from my own knowledge, " +
            "the prayer timeout reset rnz(175) / rnz(350) is independent of player experience level. " +
            "Matches rubric otherwise.";

        Assert.Equal(
            "from my own knowledge, the prayer timeout reset rnz(175) / rnz(350) is independent of player experience level.",
            BenchmarkService.ExtractOutOfRubricBasis(evidence));
    }

    [Fact]
    public void TheMarkerIsMatchedAsTheParserMatchesIt()
    {
        Assert.Equal(
            "Gnolls are immune to lycanthropy.",
            BenchmarkService.ExtractOutOfRubricBasis("not in the rubric : Gnolls are immune to lycanthropy."));
        Assert.Equal(
            "Gnolls see invisible!",
            BenchmarkService.ExtractOutOfRubricBasis("NOT IN RUBRIC:Gnolls see invisible! Otherwise fine."));
    }

    [Fact]
    public void APeriodInsideAPathOrANumber_DoesNotEndTheBasis()
    {
        const string rest = "src/pray.c sets the timeout to 1.5 times rnz(350) on a major trouble";

        Assert.Equal(rest, BenchmarkService.ExtractOutOfRubricBasis($"Not in rubric: {rest}"));
    }

    [Fact]
    public void ALineBreakEndsTheBasis()
    {
        Assert.Equal(
            "Luck times out every 600 turns",
            BenchmarkService.ExtractOutOfRubricBasis("Not in rubric: Luck times out every 600 turns\nSecond line."));
    }

    [Fact]
    public void TheBasisIsCapped()
    {
        string basis = BenchmarkService.ExtractOutOfRubricBasis("Not in rubric: " + new string('a', 900))!;

        Assert.Equal(BenchmarkService.OutOfRubricBasisMaxLength, basis.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Matches rubric.")]
    [InlineData("The rubric does not mention it.")]
    [InlineData("Not in rubric:")]
    [InlineData("Level 3. Not in rubric:   ")]
    public void NoMarker_OrNothingAfterIt_YieldsNoBasis(string? evidence)
    {
        Assert.Null(BenchmarkService.ExtractOutOfRubricBasis(evidence));
    }

    // --- Dispatch predicate ---

    [Fact]
    public void AnOutOfRubricDeductionWithAMarker_IsDispatchedToTheVerifier()
    {
        var answer = Answer(BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction, $"Level 3. Not in rubric: {Basis} Otherwise matches.");

        Assert.Equal(Basis, BenchmarkService.OutOfRubricBasisOf(answer));
        Assert.True(BenchmarkService.IsOutOfRubricAdjudication(answer));
        Assert.False(BenchmarkService.IsCriticalErrorAdjudication(answer));
        Assert.True(BenchmarkService.NeedsClaimVerification(answer));
    }

    [Fact]
    public void TheFlagWithoutAMarkerInTheStoredEvidence_IsNotDispatched()
    {
        var answer = Answer(BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction, "Level 3. The damage figure is wrong.");

        Assert.False(BenchmarkService.IsOutOfRubricAdjudication(answer));
        Assert.False(BenchmarkService.NeedsClaimVerification(answer));
        Assert.False(BenchmarkService.NeedsClaimVerification(Answer(BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction)));
    }

    [Fact]
    public void AMarkerWithoutTheFlag_IsNotDispatched()
    {
        var answer = Answer(BenchmarkAnswerFlags.None, $"Level 3. Not in rubric: {Basis}");

        Assert.Null(BenchmarkService.OutOfRubricBasisOf(answer));
        Assert.False(BenchmarkService.NeedsClaimVerification(answer));
    }

    // --- Claim list composition ---

    [Fact]
    public void AloneTheBasisIsClaimZero_AheadOfTheUnadjudicableClaims()
    {
        var claims = new List<string> { "Gnolls have keen smell", "Master Kaen has AC -2" };

        var submitted = BenchmarkService.WithOutOfRubricBasis(claims, $"  {Basis}  ", 0);

        Assert.Equal(new[] { Basis, "Gnolls have keen smell", "Master Kaen has AC -2" }, submitted);
        Assert.Equal(2, claims.Count);
    }

    [Fact]
    public void WithACriticalErrorQuote_TheQuoteStaysFirstAndTheBasisIsSecond()
    {
        var withQuote = BenchmarkService.WithCriticalErrorQuoteFirst(new List<string> { "Gnolls have keen smell" }, Quote);

        var submitted = BenchmarkService.WithOutOfRubricBasis(withQuote, Basis, 1);

        Assert.Equal(new[] { Quote, Basis, "Gnolls have keen smell" }, submitted);
    }

    [Fact]
    public void TheBasisIsSubmittedOnce()
    {
        var claims = new List<string> { "Gnolls have keen smell", Basis };

        var submitted = BenchmarkService.WithOutOfRubricBasis(claims, Basis, 0);

        Assert.Equal(new[] { Basis, "Gnolls have keen smell" }, submitted);
    }

    [Fact]
    public void AMissingBasisAddsNothing_AndThePositionIsClamped()
    {
        var claims = new List<string> { "Gnolls have keen smell" };

        Assert.Equal(claims, BenchmarkService.WithOutOfRubricBasis(claims, null, 0));
        Assert.Equal(claims, BenchmarkService.WithOutOfRubricBasis(claims, "  ", 0));
        Assert.Empty(BenchmarkService.WithOutOfRubricBasis(null, null, 0));
        Assert.Equal(new[] { Basis }, BenchmarkService.WithOutOfRubricBasis(null, Basis, 1));
    }

    [Fact]
    public void TheBasisNeverEntersTheUnadjudicableClaimColumns()
    {
        // UnverifiedClaimsJson means "claims the assessor could not adjudicate", and the basis is the
        // assessor's own statement, so it is carried in the prompt only.
        var answer = Answer(
            BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction,
            $"Not in rubric: {Basis}",
            unverifiedClaimCount: 1,
            unverifiedClaimsJson: JsonSerializer.Serialize(new[] { "Gnolls have keen smell" }));

        var stored = JsonSerializer.Deserialize<List<string>>(answer.UnverifiedClaimsJson!)!;
        var submitted = BenchmarkService.WithOutOfRubricBasis(stored, BenchmarkService.OutOfRubricBasisOf(answer), 0);

        Assert.Equal(2, submitted.Count);
        Assert.Equal(1, answer.UnverifiedClaimCount);
        Assert.DoesNotContain("prayer timeout", answer.UnverifiedClaimsJson);
    }

    // --- The advisory flag ---

    [Fact]
    public void ARefutedVerdictOnTheBasis_ContestsTheDeduction()
    {
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, Quote, BenchmarkClaimVerdict.Supported, "src/pray.c:812", "Found."),
            new BenchmarkClaimVerification(1, Basis, BenchmarkClaimVerdict.Refuted, "src/pray.c:1020", "The reset scales with level."),
            new BenchmarkClaimVerification(2, "Gnolls have keen smell", BenchmarkClaimVerdict.Supported, "src/role.c:45", "Found.")
        };

        Assert.True(BenchmarkService.OutOfRubricBasisWasRefuted(verifications, Basis));
        Assert.True(BenchmarkService.OutOfRubricBasisWasRefuted(verifications, $" {Basis} "));
    }

    [Theory]
    [InlineData(BenchmarkClaimVerdict.Supported)]
    [InlineData(BenchmarkClaimVerdict.Indeterminate)]
    public void ASupportedOrIndeterminateVerdictOnTheBasis_ContestsNothing(BenchmarkClaimVerdict verdict)
    {
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, Basis, verdict, verdict == BenchmarkClaimVerdict.Supported ? "src/pray.c:1020" : null, "Checked.")
        };

        Assert.False(BenchmarkService.OutOfRubricBasisWasRefuted(verifications, Basis));
    }

    [Fact]
    public void ARefutationOfSomeOtherClaim_DoesNotContestTheDeduction()
    {
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, Basis, BenchmarkClaimVerdict.Indeterminate, null, "Not located."),
            new BenchmarkClaimVerification(1, "Master Kaen has AC -2", BenchmarkClaimVerdict.Refuted, "src/monst.c:120", "Wrong.")
        };

        Assert.False(BenchmarkService.OutOfRubricBasisWasRefuted(verifications, Basis));
    }

    [Fact]
    public void NoVerifications_OrNoBasis_ContestsNothing()
    {
        var refuted = new[]
        {
            new BenchmarkClaimVerification(0, Basis, BenchmarkClaimVerdict.Refuted, "src/pray.c:1020", "Wrong.")
        };

        Assert.False(BenchmarkService.OutOfRubricBasisWasRefuted(null, Basis));
        Assert.False(BenchmarkService.OutOfRubricBasisWasRefuted(Array.Empty<BenchmarkClaimVerification>(), Basis));
        Assert.False(BenchmarkService.OutOfRubricBasisWasRefuted(refuted, null));
        Assert.False(BenchmarkService.OutOfRubricBasisWasRefuted(refuted, "  "));
    }

    // --- Kept out of the answer's own claim figures ---

    [Fact]
    public void TheBasisIsExcludedFromTheAnswersOwnVerifications()
    {
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, Basis, BenchmarkClaimVerdict.Refuted, "src/pray.c:1020", "Wrong."),
            new BenchmarkClaimVerification(1, "Gnolls have keen smell", BenchmarkClaimVerdict.Supported, "src/role.c:45", "Found."),
            new BenchmarkClaimVerification(2, "Master Kaen has AC -2", BenchmarkClaimVerdict.Indeterminate, null, "Not located.")
        };

        var own = BenchmarkService.WithoutOutOfRubricBasis(verifications, Basis);

        Assert.Equal(2, own.Count);
        Assert.Equal(0, own.Count(v => v.Verdict == BenchmarkClaimVerdict.Refuted));
        Assert.Equal(1, own.Count(v => v.Verdict == BenchmarkClaimVerdict.Supported));
        Assert.Equal(1, own.Count(v => v.Verdict == BenchmarkClaimVerdict.Indeterminate));

        Assert.Equal(3, BenchmarkService.WithoutOutOfRubricBasis(verifications, null).Count);
        Assert.Empty(BenchmarkService.WithoutOutOfRubricBasis(null, Basis));
    }

    [Fact]
    public void AParsedVerifierResponse_KeepsTheBasisVerdictOnRecord_ButOutOfTheAnswersCounts()
    {
        // The parser maps each verdict back to its submitted claim by index, so the basis at claim 1
        // behind a critical-error quote is read at index 1 and nowhere else.
        var submitted = new List<string> { Quote, Basis, "Gnolls have keen smell" };
        string response = JsonSerializer.Serialize(new
        {
            verifications = new object[]
            {
                new { claimIndex = 0, claim = Quote, verdict = "Indeterminate", citation = (string?)null, basis = "Not located." },
                new { claimIndex = 1, claim = Basis, verdict = "Refuted", citation = "src/pray.c:1020", basis = "The reset scales with level." },
                new { claimIndex = 2, claim = "Gnolls have keen smell", verdict = "Supported", citation = "src/role.c:45", basis = "Found." }
            }
        });

        var parsed = BenchmarkClaimVerificationParser.Parse(response, submitted);

        Assert.True(parsed.Success);
        Assert.Equal(1, parsed.ClaimsRefutedCount);
        Assert.Equal(3, parsed.Verifications.Count);
        Assert.True(BenchmarkService.OutOfRubricBasisWasRefuted(parsed.Verifications, Basis));

        var own = BenchmarkService.WithoutOutOfRubricBasis(parsed.Verifications, Basis);
        Assert.Equal(0, own.Count(v => v.Verdict == BenchmarkClaimVerdict.Refuted));
        Assert.Equal(1, own.Count(v => v.Verdict == BenchmarkClaimVerdict.Supported));
        Assert.Equal(1, own.Count(v => v.Verdict == BenchmarkClaimVerdict.Indeterminate));
    }

    // --- The verifier prompt preamble ---

    private static string BuildPrompt(
        bool outOfRubricAdjudication,
        bool criticalErrorAdjudication = false,
        string? assessorEvidence = null)
    {
        var claims = new List<string>();
        if (criticalErrorAdjudication) claims.Add(Quote);
        if (outOfRubricAdjudication) claims.Add(Basis);
        claims.Add("Gnolls have keen smell");

        return BenchmarkClaimVerificationPrompt.BuildPrompt(
            "GnollHack Suite",
            1,
            "How long is the prayer timeout?",
            "**REQUIRED** - prayer timeout must have elapsed.",
            claims,
            new List<string> { "source_code_search" },
            15,
            isCriticalErrorAdjudication: criticalErrorAdjudication,
            isOutOfRubricAdjudication: outOfRubricAdjudication,
            assessorEvidence: assessorEvidence);
    }

    [Fact]
    public void ThePreambleAppearsOnlyUnderTheOutOfRubricFlag_NamingTheBasisAsClaimOne()
    {
        string with = BuildPrompt(outOfRubricAdjudication: true);
        string without = BuildPrompt(outOfRubricAdjudication: false);

        Assert.Contains("OUT-OF-RUBRIC DEDUCTION ADJUDICATION:", with);
        Assert.Contains(
            "The first assessor docked ACCURACY on a statement from its own knowledge rather than the rubric, quoted as claim 1 below (ClaimIndex 0).",
            with);
        Assert.Contains("Refuted means the assessor's statement is false.", with);
        Assert.DoesNotContain("CRITICAL ERROR ADJUDICATION:", with);
        Assert.DoesNotContain("DISPUTED VERDICT ADJUDICATION:", with);

        Assert.DoesNotContain("OUT-OF-RUBRIC DEDUCTION ADJUDICATION:", without);
    }

    [Fact]
    public void BehindACriticalErrorQuote_TheBasisIsClaimTwo_AndItsPreambleComesSecond()
    {
        string prompt = BuildPrompt(outOfRubricAdjudication: true, criticalErrorAdjudication: true);

        Assert.Contains("quoted as claim 2 below (ClaimIndex 1).", prompt);

        int criticalAt = prompt.IndexOf("CRITICAL ERROR ADJUDICATION:", StringComparison.Ordinal);
        int outOfRubricAt = prompt.IndexOf("OUT-OF-RUBRIC DEDUCTION ADJUDICATION:", StringComparison.Ordinal);
        Assert.True(criticalAt >= 0 && outOfRubricAt > criticalAt, "The critical-error preamble comes first, as its claim does.");

        // The preamble's claim number and the block the basis is actually rendered in agree.
        int claimOneStart = prompt.IndexOf("=== START CLAIM 1 ===", StringComparison.Ordinal);
        int claimOneEnd = prompt.IndexOf("=== END CLAIM 1 ===", StringComparison.Ordinal);
        int basisAt = prompt.IndexOf(Basis, StringComparison.Ordinal);
        Assert.True(claimOneStart < basisAt && basisAt < claimOneEnd, "The basis must be rendered as ClaimIndex 1.");
    }

    [Fact]
    public void AssessorEvidenceIsCarried_UnderTheOutOfRubricFlagAlone()
    {
        const string evidence = "Level 3. Not in rubric: the reset is level-independent.";

        string outOfRubric = BuildPrompt(outOfRubricAdjudication: true, assessorEvidence: evidence);
        Assert.Contains("Assessor Evidence / Counter-Claims:", outOfRubric);
        Assert.Contains(evidence, outOfRubric);

        string neither = BuildPrompt(outOfRubricAdjudication: false, assessorEvidence: evidence);
        Assert.DoesNotContain("Assessor Evidence / Counter-Claims:", neither);
    }

    // --- The synthesis prompt ---

    private static BenchmarkPerQuestionVerdictSummary Summary(IReadOnlyList<string> contestedBases)
    {
        return new BenchmarkPerQuestionVerdictSummary
        {
            OrderIndex = 3,
            QuestionText = "How long is the prayer timeout?",
            ExpectedPoints = "Rubric",
            AccuracyLevel = 4,
            CompletenessLevel = 5,
            ConcisenessLevel = 5,
            ReadabilityLevel = 5,
            QualityScore = 72,
            SpeedScore = 80,
            DurationMs = 2000,
            AssessedDifficulty = 60,
            CriticalError = false,
            AccuracyEvidence = $"Not in rubric: {Basis}",
            ReviewComment = "Mostly right.",
            Status = BenchmarkAnswerStatus.Ok,
            ContestedAccuracyDeductionBases = contestedBases
        };
    }

    [Fact]
    public void TheSynthesisIsTold_NotToDescribeAContestedDeductionAsAnErrorOfTheAnswer()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { Summary(new[] { Basis }) });

        Assert.Contains($"Accuracy deduction on Q3 rests on the assessor's own-knowledge statement \"{Basis}\"", prompt);
        Assert.Contains("do not describe this deduction as an error of the answer", prompt);
    }

    [Fact]
    public void TheSynthesisLineIsAbsent_WhenNoDeductionWasContested()
    {
        string prompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt("Suite", new[] { Summary(Array.Empty<string>()) });

        Assert.DoesNotContain("Accuracy deduction on Q3 rests on", prompt);
    }
}
