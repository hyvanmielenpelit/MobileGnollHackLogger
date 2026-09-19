namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// What a contested answer with no unverified claims submits to the claim verifier: the answer's own
/// sentences, persisted as its unverified claims, and the assessor's statements, submitted under
/// their own role so a verdict on them is never read as a verdict on the answer.
/// </summary>
public class BenchmarkDisputedClaimExtractionTests
{
    private static BenchmarkRunAnswer Answer(string answerText, string? criticalErrorQuote = null, string? reviewComment = null)
        => new()
        {
            OrderIndex = 1,
            AnswerText = answerText,
            CriticalErrorQuote = criticalErrorQuote,
            ReviewComment = reviewComment,
            AnswerFlags = (int)BenchmarkAnswerFlags.ContestedVerdict
        };

    // --- Answer claims ------------------------------------------------------------------------

    [Fact]
    public void AnswerClaims_DropAListLeadIn_AndFragmentsUnderFourWords()
    {
        const string text = "To use it:\n1. Apply the grail to restore 1000 hit points.\n2. Rest 5 turns.\n"
            + "The grail weighs 20 units and costs 300 zorkmids.";

        var claims = BenchmarkService.ExtractDisputedClaims(Answer(text), null);

        Assert.Equal(
            new[] { "Apply the grail to restore 1000 hit points.", "The grail weighs 20 units and costs 300 zorkmids." },
            claims.AnswerClaims);
        Assert.DoesNotContain(claims.AnswerClaims, c => c.Contains("To use it", StringComparison.Ordinal));
        Assert.DoesNotContain(claims.AnswerClaims, c => c.StartsWith("Rest", StringComparison.Ordinal));
        Assert.Empty(claims.AssessorStatements);
    }

    [Fact]
    public void AnswerClaims_AreNotSplitOnABareNewline()
    {
        const string text = "Damage bonus by level\n+2 at level 5 and +4 at level 10 for every weapon you wield.";

        var claims = BenchmarkService.ExtractDisputedClaims(Answer(text), null);

        Assert.Equal(text, Assert.Single(claims.AnswerClaims));
    }

    [Fact]
    public void AnswerClaims_KeepTheCriticalErrorQuoteFirst_AndCapTheDigitSentencesAtFour()
    {
        const string quote = "Praying at 1 HP is always safe.";
        string text = quote + " " + string.Join(" ", Enumerable.Range(1, 6).Select(i => $"Sentence number {i} carries a digit."));

        var claims = BenchmarkService.ExtractDisputedClaims(Answer(text, criticalErrorQuote: quote), null);

        Assert.Equal(quote, claims.AnswerClaims[0]);
        Assert.Equal(1 + BenchmarkService.MaxDisputedAnswerNumericClaims, claims.AnswerClaims.Count);
    }

    // --- Assessor statements --------------------------------------------------------------------

    [Fact]
    public void TheAssessorsSentences_ComeBackInTheSecondList()
    {
        const string evidence = "The grail heals 500 hit points, not 1000. Its weight in GnollHack is 40.";

        var claims = BenchmarkService.ExtractDisputedClaims(Answer("The grail heals 1000 hit points when applied."), evidence);

        Assert.Equal(new[] { "The grail heals 1000 hit points when applied." }, claims.AnswerClaims);
        Assert.Equal(new[] { "The grail heals 500 hit points, not 1000.", "Its weight in GnollHack is 40." }, claims.AssessorStatements);
    }

    [Theory]
    [InlineData("The answer states \"the grail heals 1000 hit points\".")]
    [InlineData("It claims 'the grail heals 1000 hit points' without support.")]
    [InlineData("The response gives “the grail heals 1000 hit points” as fact.")]
    public void ASentenceThatOnlyReportsTheAnswerWithAQuotation_IsNotSubmitted(string reportingSentence)
    {
        string evidence = reportingSentence + " In GnollHack the grail heals 500 hit points.";

        var claims = BenchmarkService.ExtractDisputedClaims(Answer("No digits here at all."), evidence);

        Assert.Equal(new[] { "In GnollHack the grail heals 500 hit points." }, claims.AssessorStatements);
    }

    [Fact]
    public void AReportingSentenceWithoutAQuotation_IsStillAnAssessorStatement()
    {
        const string evidence = "The answer says the grail is a tool, which is wrong in GnollHack.";

        var claims = BenchmarkService.ExtractDisputedClaims(Answer("No digits here at all."), evidence);

        Assert.Equal(new[] { evidence }, claims.AssessorStatements);
    }

    [Fact]
    public void TheReviewComment_IsAnAssessorStatement_WhenNothingElseWasFound()
    {
        var claims = BenchmarkService.ExtractDisputedClaims(
            Answer("No digits here at all.", reviewComment: "Completely hallucinates the mechanics."), null);

        Assert.Empty(claims.AnswerClaims);
        Assert.Equal(new[] { "Completely hallucinates the mechanics." }, claims.AssessorStatements);
    }

    // --- Roles through the manifest -----------------------------------------------------------

    private const string MonkAnswer =
        "As a vegan Monk you should eat fortune cookies and lembas wafers. Candy bars restore 100 nutrition each.";

    [Fact]
    public void ASuspectedFalseClaimAndAnAssessorStatement_KeepTheirOwnRoles()
    {
        const string suspected = "Suspected false: Candy bars restore 100 nutrition each. — the source gives 100 only for a fresh bar.";
        const string evidence = "Suspected false: Candy bars restore 100 nutrition each. Lembas wafers give 800 nutrition in GnollHack.";

        var disputed = BenchmarkService.ExtractDisputedClaims(Answer(MonkAnswer), evidence);

        // A suspected-false sentence is the answer's, so it never becomes an assessor statement.
        Assert.DoesNotContain(disputed.AssessorStatements, s => s.Contains("Suspected false", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Lembas wafers give 800 nutrition in GnollHack.", disputed.AssessorStatements);

        var manifest = BenchmarkService.BuildClaimManifest(
            new[] { suspected }, null, null, null, disputed.AssessorStatements, MonkAnswer);

        var answerItem = Assert.Single(manifest, m => m.Roles.Contains(BenchmarkClaimRoles.UnverifiedClaim));
        Assert.Equal("Candy bars restore 100 nutrition each.", answerItem.Text);
        Assert.True(answerItem.SuspectedFalse);
        Assert.Equal(suspected, answerItem.RecordedClaim);
        Assert.Equal("the source gives 100 only for a fresh bar.", answerItem.Suspicion);
        Assert.DoesNotContain(BenchmarkClaimRoles.AssessorStatement, answerItem.Roles);

        var assessorItem = Assert.Single(manifest, m => m.Roles.Contains(BenchmarkClaimRoles.AssessorStatement));
        Assert.Equal("Lembas wafers give 800 nutrition in GnollHack.", assessorItem.Text);
        Assert.Equal(new[] { BenchmarkClaimRoles.AssessorStatement }, assessorItem.Roles);
        Assert.False(assessorItem.SuspectedFalse);

        Assert.DoesNotContain(manifest, m => m.Text.StartsWith("Suspected false", StringComparison.OrdinalIgnoreCase));

        // The stamped record identifies each from ClaimVerificationJson alone.
        var stamped = BenchmarkService.StampRoles(new[]
        {
            new BenchmarkClaimVerification(0, answerItem.Text, BenchmarkClaimVerdict.Refuted, "src/eat.c:300", "Candy bars give 100."),
            new BenchmarkClaimVerification(1, assessorItem.Text, BenchmarkClaimVerdict.Supported, "src/objects.c:800", "800.")
        }, manifest);
        Assert.True(stamped[0].SuspectedFalse);
        Assert.Equal(suspected, stamped[0].RecordedClaim);
        Assert.Null(stamped[1].SuspectedFalse);
        Assert.Null(stamped[1].RecordedClaim);
    }

    [Fact]
    public void AnAssessorStatementEqualToAnAnswerItem_OrHoldingTheBasis_IsNotSubmitted()
    {
        const string claim = "Candy bars restore 100 nutrition each.";
        const string basis = "lembas wafers give 800 nutrition.";

        var manifest = BenchmarkService.BuildClaimManifest(
            new[] { claim }, null, basis, null,
            new[] { claim, "Not in rubric: lembas wafers give 800 nutrition.", "Fortune cookies give 40 nutrition." });

        Assert.Equal(new[] { basis, claim, "Fortune cookies give 40 nutrition." }, manifest.Select(m => m.Text));
        Assert.Equal(new[] { BenchmarkClaimRoles.OutOfRubricBasis }, manifest[0].Roles);
        Assert.Equal(new[] { BenchmarkClaimRoles.UnverifiedClaim }, manifest[1].Roles);
        Assert.Equal(new[] { BenchmarkClaimRoles.AssessorStatement }, manifest[2].Roles);
    }

    // --- Flags and counts ---------------------------------------------------------------------

    private static BenchmarkClaimVerification Verification(int index, string claim, BenchmarkClaimVerdict verdict, string role, string? citation = "src/eat.c:10")
        => new(index, claim, verdict, citation, "Basis.") { Roles = new[] { role } };

    [Fact]
    public void ARefutedAssessorStatement_StaysOutOfTheCounts_AndContestsTheAccuracyDeduction()
    {
        var verifications = new[]
        {
            Verification(0, "Candy bars restore 100 nutrition each.", BenchmarkClaimVerdict.Supported, BenchmarkClaimRoles.UnverifiedClaim),
            Verification(1, "Lembas wafers give 800 nutrition in GnollHack.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.AssessorStatement)
        };
        var answer = new BenchmarkRunAnswer();

        BenchmarkService.ApplyClaimVerificationOutcome(answer, verifications, isCriticalErrorAdjudication: false, outOfRubricBasis: null);

        Assert.Equal(1, answer.ClaimsSupportedCount);
        Assert.Equal(0, answer.ClaimsRefutedCount);
        Assert.Equal(0, answer.ClaimsIndeterminateCount);
        Assert.Equal(0, answer.AnswerFlags & (int)BenchmarkAnswerFlags.RefutedClaim);
        Assert.NotEqual(0, answer.AnswerFlags & (int)BenchmarkAnswerFlags.ContestedAccuracyDeduction);
        Assert.Contains("\"roles\":[\"assessorStatement\"]", answer.ClaimVerificationJson);

        // Nor does it reach the second reader's fact-check context or the synthesis's refuted claims.
        Assert.DoesNotContain(verifications, v => BenchmarkClaimRoles.IsOrdinaryClaim(v) && v.Roles!.Contains(BenchmarkClaimRoles.AssessorStatement));
        Assert.Single(BenchmarkService.OrdinaryClaimVerifications(verifications, answer));
        Assert.Single(BenchmarkService.RefutedAssessorStatements(verifications));
    }

    [Theory]
    [InlineData(BenchmarkClaimVerdict.Supported)]
    [InlineData(BenchmarkClaimVerdict.Indeterminate)]
    public void ASupportedOrIndeterminateAssessorStatement_RaisesNothing(BenchmarkClaimVerdict verdict)
    {
        var verifications = new[]
        {
            Verification(0, "Lembas wafers give 800 nutrition in GnollHack.", verdict, BenchmarkClaimRoles.AssessorStatement)
        };
        var answer = new BenchmarkRunAnswer { AnswerFlags = (int)BenchmarkAnswerFlags.ContestedAccuracyDeduction };

        BenchmarkService.ApplyClaimVerificationOutcome(answer, verifications, false, null);

        Assert.Equal(0, answer.AnswerFlags & (int)(BenchmarkAnswerFlags.ContestedAccuracyDeduction | BenchmarkAnswerFlags.RefutedClaim));
        Assert.Equal(0, answer.ClaimsSupportedCount + answer.ClaimsRefutedCount + answer.ClaimsIndeterminateCount);
    }

    [Fact]
    public void ARefutedSuspectedFalseClaim_IsARefutedClaimOfTheAnswer()
    {
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, "Candy bars restore 100 nutrition each.", BenchmarkClaimVerdict.Refuted, "src/eat.c:300", "They give 100 only fresh.")
            {
                Roles = new[] { BenchmarkClaimRoles.UnverifiedClaim },
                SuspectedFalse = true,
                RecordedClaim = "Suspected false: Candy bars restore 100 nutrition each. — only fresh."
            }
        };
        var answer = new BenchmarkRunAnswer();

        BenchmarkService.ApplyClaimVerificationOutcome(answer, verifications, false, null);

        Assert.Equal(1, answer.ClaimsRefutedCount);
        Assert.NotEqual(0, answer.AnswerFlags & (int)BenchmarkAnswerFlags.RefutedClaim);
        Assert.Equal(0, answer.AnswerFlags & (int)BenchmarkAnswerFlags.ContestedAccuracyDeduction);
        Assert.Contains("\"suspectedFalse\":true", answer.ClaimVerificationJson);
    }

    [Fact]
    public void ARefutedAssessorStatementWithACitation_IsAnAccuracyTargetOfTheReGrade()
    {
        var verifications = new[]
        {
            Verification(0, "Lembas wafers give 800 nutrition in GnollHack.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.AssessorStatement)
        };

        var target = Assert.Single(BenchmarkService.BuildEvidenceInformedTargets(new BenchmarkRunAnswer(), verifications));

        Assert.Equal(BenchmarkEvidenceInformedTarget.AccuracyKind, target.Kind);
        Assert.Equal(BenchmarkClaimRoles.AssessorStatement, target.Source);
        Assert.Equal(new[] { "F0" }, target.FindingIds);
    }

    // --- The verifier prompt ------------------------------------------------------------------

    [Fact]
    public void TheVerifierPrompt_MarksAnAssessorStatement_AndExplainsItsVerdict()
    {
        var manifest = BenchmarkService.BuildClaimManifest(
            new[] { "Candy bars restore 100 nutrition each." }, null, null, null,
            new[] { "Lembas wafers give 800 nutrition in GnollHack." });

        string prompt = BenchmarkClaimVerificationPrompt.BuildPrompt(
            "Suite", 1, "What should a vegan Monk eat?", null,
            manifest.Select(m => m.Text).ToList(),
            new List<string> { "source_code_search" }, 15,
            isDisputedVerdict: true,
            claimRoles: manifest.Select(m => m.Roles).ToList(),
            claimContexts: manifest.Select(m => m.Context).ToList());

        Assert.Contains("ASSESSOR STATEMENT ADJUDICATION: the items marked 'Stated by the first assessor' are the assessor's own statements about the game, not sentences of the answer. Supported means the assessor's statement is true.", prompt);

        string block0 = prompt.Substring(prompt.IndexOf("=== START CLAIM 0 ===", StringComparison.Ordinal));
        block0 = block0.Substring(0, block0.IndexOf("=== END CLAIM 0 ===", StringComparison.Ordinal));
        string block1 = prompt.Substring(prompt.IndexOf("=== START CLAIM 1 ===", StringComparison.Ordinal));
        block1 = block1.Substring(0, block1.IndexOf("=== END CLAIM 1 ===", StringComparison.Ordinal));

        Assert.DoesNotContain("Stated by the first assessor", block0);
        Assert.Contains("Stated by the first assessor (not part of the answer).", block1);
    }

    [Fact]
    public void TheVerifierPrompt_CarriesNoAssessorStatementPreamble_WithoutOne()
    {
        string prompt = BenchmarkClaimVerificationPrompt.BuildPrompt(
            "Suite", 1, "Question?", null,
            new List<string> { "Candy bars restore 100 nutrition each." },
            new List<string> { "source_code_search" }, 15,
            claimRoles: new List<IReadOnlyList<string>> { new[] { BenchmarkClaimRoles.UnverifiedClaim } });

        Assert.DoesNotContain("ASSESSOR STATEMENT ADJUDICATION", prompt);
        Assert.DoesNotContain("Stated by the first assessor", prompt);
    }
}
