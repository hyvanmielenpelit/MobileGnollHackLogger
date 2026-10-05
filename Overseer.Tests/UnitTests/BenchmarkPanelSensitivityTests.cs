namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The panel verification-cleared Accuracy sensitivity: which member is lifted on which answer, the
/// answers that never qualify, and the index it recomputes against the published one.
/// </summary>
public class BenchmarkPanelSensitivityTests
{
    private static readonly BenchmarkScoringConstants Constants = BenchmarkScoringConstants.Default;

    private static int Score(int accuracy) => BenchmarkScoring.Quality(accuracy, 5, 5, 5, false, Constants).Score;

    /// <summary>
    /// A panel answer both members scored, member A at <paramref name="accuracyA"/> and member B at
    /// <paramref name="accuracyB"/> Accuracy and every other level 5, each score the one its levels
    /// give and the panel score their mean.
    /// </summary>
    private static BenchmarkRunAnswer Answer(int orderIndex, int accuracyA = 4, int accuracyB = 4, int? assessedDifficulty = 50)
    {
        int scoreA = Score(accuracyA);
        int scoreB = Score(accuracyB);
        return new BenchmarkRunAnswer
        {
            OrderIndex = orderIndex,
            QuestionText = $"Q{orderIndex}",
            AnswerText = $"Answer {orderIndex}",
            Difficulty = BenchmarkDifficulty.Intermediate,
            AssessedDifficulty = assessedDifficulty,
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            AccuracyLevel = accuracyA,
            CompletenessLevel = 5,
            ConcisenessLevel = 5,
            ReadabilityLevel = 5,
            QualityScore = scoreA,
            RawQualityScore = scoreA,
            CoAssessmentStatus = BenchmarkAssessmentStatus.Scored,
            CoAssessmentQualityScore = scoreB,
            CoAssessmentRawQualityScore = scoreB,
            CoAssessmentCriticalError = false,
            CoAssessmentJson = new BenchmarkCoAssessmentRecord
            {
                AccuracyLevel = accuracyB,
                CompletenessLevel = 5,
                ConcisenessLevel = 5,
                ReadabilityLevel = 5,
                QualityScore = scoreB,
                RawQualityScore = scoreB,
                Flags = new BenchmarkCoAssessmentFlags()
            }.Serialize(),
            PanelQualityScore = (scoreA + scoreB) / 2.0
        };
    }

    /// <summary>A finalized panel run under the current scoring method; <paramref name="panel"/> false makes it single-assessor.</summary>
    private static BenchmarkRun Run(bool panel, params BenchmarkRunAnswer[] answers)
    {
        var run = new BenchmarkRun
        {
            Id = 1,
            HarnessVersion = "47",
            ScoringMethodVersion = BenchmarkCriticalErrorResolver.FirstScoringMethod,
            CoAssessorModelConfigurationId = panel ? 9 : null,
            TotalQuestionCount = answers.Length,
            Answers = new List<BenchmarkRunAnswer>(answers)
        };
        BenchmarkRunFinalizer.Apply(run, run.Answers);
        return run;
    }

    private static BenchmarkRun Run(params BenchmarkRunAnswer[] answers) => Run(true, answers);

    private static string Json(params BenchmarkClaimVerification[] items) => JsonSerializer.Serialize(items);

    /// <summary>A sentence <paramref name="member"/> charged as false.</summary>
    private static BenchmarkClaimVerification Accused(string member, BenchmarkClaimVerdict verdict, string? citation = "src/pray.c:120")
        => new(0, $"A sentence member {member} charged.", verdict, citation, "The source decides it.")
        {
            Roles = new[] { BenchmarkClaimRoles.AccusedQuote },
            RaisedBy = new[] { member },
            AccusedBy = new[] { member }
        };

    /// <summary>An out-of-rubric claim of the answer's own that <paramref name="member"/> raised.</summary>
    private static BenchmarkClaimVerification Claim(string member, BenchmarkClaimVerdict verdict)
        => new(1, $"A claim member {member} raised.", verdict, "src/pray.c:200", "The source decides it.")
        {
            Roles = new[] { BenchmarkClaimRoles.UnverifiedClaim },
            RaisedBy = new[] { member }
        };

    private static void FlagUnevidencedDeductionB(BenchmarkRunAnswer answer)
    {
        var record = BenchmarkCoAssessmentRecord.Parse(answer.CoAssessmentJson)!;
        record.Flags ??= new BenchmarkCoAssessmentFlags();
        record.Flags.UnevidencedDeduction = true;
        answer.CoAssessmentJson = record.Serialize();
    }

    private static int? ExpectedIndex(params (double Score, int Difficulty)[] items)
        => BenchmarkScoring.QualityIndex(items.Select(i => ((double?)i.Score, i.Difficulty)).ToList());

    [Fact]
    public void MemberA_IsLifted_WhenTheVerifierSupportedEverySentenceItCharged()
    {
        var q1 = Answer(1);
        q1.ClaimVerificationJson = Json(Accused("A", BenchmarkClaimVerdict.Supported));
        var q2 = Answer(2);
        var run = Run(q1, q2);

        var result = BenchmarkPanelSensitivity.Compute(run, run.Answers, Constants);

        Assert.Equal(new[] { 1 }, result.LiftedA);
        Assert.Empty(result.LiftedB);
        Assert.Equal(ExpectedIndex(((Score(5) + Score(4)) / 2.0, 50), (Score(4), 50)), (int?)result.Index);
    }

    [Fact]
    public void MemberB_IsLifted_WhenItsUnevidencedDeductionIsVerificationCleared()
    {
        var q1 = Answer(1);
        FlagUnevidencedDeductionB(q1);
        q1.ClaimVerificationJson = Json(Claim("B", BenchmarkClaimVerdict.Supported));
        var q2 = Answer(2);
        var run = Run(q1, q2);

        var result = BenchmarkPanelSensitivity.Compute(run, run.Answers, Constants);

        // Member A flagged nothing and charged nothing, so only B is lifted.
        Assert.Empty(result.LiftedA);
        Assert.Equal(new[] { 1 }, result.LiftedB);
        Assert.Equal(ExpectedIndex(((Score(4) + Score(5)) / 2.0, 50), (Score(4), 50)), (int?)result.Index);
    }

    [Fact]
    public void AVerificationClearedDeduction_NeedsTheMembersOwnFlagAndClaims()
    {
        // B's claim was supported, but B never flagged an unevidenced deduction; A flagged one but
        // raised no claim.
        var q1 = Answer(1);
        q1.AnswerFlags = (int)BenchmarkAnswerFlags.UnevidencedDeduction;
        q1.ClaimVerificationJson = Json(Claim("B", BenchmarkClaimVerdict.Supported));
        var run = Run(q1, Answer(2));

        var result = BenchmarkPanelSensitivity.Compute(run, run.Answers, Constants);

        Assert.Empty(result.LiftedA);
        Assert.Empty(result.LiftedB);
        Assert.Null(result.Index);
    }

    [Fact]
    public void OneRefutedAccusation_IsNotEligible()
    {
        var q1 = Answer(1);
        q1.ClaimVerificationJson = Json(
            Accused("A", BenchmarkClaimVerdict.Supported),
            Accused("A", BenchmarkClaimVerdict.Refuted) with { ClaimIndex = 1, Claim = "Another sentence member A charged." });
        var run = Run(q1, Answer(2));

        var result = BenchmarkPanelSensitivity.Compute(run, run.Answers, Constants);

        Assert.Empty(result.LiftedA);
        Assert.Null(result.Index);
    }

    /// <summary>
    /// Member A charged a sentence, flagged an unevidenced deduction at <paramref name="accuracyA"/>
    /// Accuracy and raised one ordinary claim, all with the given verdicts; the single-assessor
    /// counts are set as the stored verification would set them.
    /// </summary>
    private static BenchmarkRunAnswer ChargedUnevidencedDeduction(int orderIndex, int accuracyA, BenchmarkClaimVerdict chargeVerdict)
    {
        var answer = Answer(orderIndex, accuracyA: accuracyA);
        answer.AnswerFlags = (int)BenchmarkAnswerFlags.UnevidencedDeduction;
        answer.UnverifiedClaimCount = 1;
        answer.ClaimsSupportedCount = 1;
        answer.ClaimsRefutedCount = 0;
        answer.ClaimsIndeterminateCount = 0;
        answer.ClaimVerificationJson = Json(
            Accused("A", chargeVerdict),
            Claim("A", BenchmarkClaimVerdict.Supported));
        return answer;
    }

    [Theory]
    [InlineData(4)] // Run 93 Q3.
    [InlineData(5)] // Run 94 Q14.
    public void AnUpheldCharge_IsNeitherLiftedNorCountedAsVerificationCleared(int accuracyA)
    {
        var q1 = ChargedUnevidencedDeduction(1, accuracyA, BenchmarkClaimVerdict.Refuted);
        var run = Run(q1, Answer(2));

        var result = BenchmarkPanelSensitivity.Compute(run, run.Answers, Constants);

        Assert.Empty(result.LiftedA);
        Assert.Null(result.Index);
        Assert.False(BenchmarkService.IsVerificationClearedAccuracyDeduction(q1));
    }

    [Fact]
    public void ASupportedCharge_LeavesTheVerificationClearedDeductionLiftedAndCounted()
    {
        var q1 = ChargedUnevidencedDeduction(1, 4, BenchmarkClaimVerdict.Supported);
        var run = Run(q1, Answer(2));

        var result = BenchmarkPanelSensitivity.Compute(run, run.Answers, Constants);

        Assert.Equal(new[] { 1 }, result.LiftedA);
        Assert.True(BenchmarkService.IsVerificationClearedAccuracyDeduction(q1));
    }

    [Fact]
    public void AChargeUpheldForMemberB_DoesNotBlockMemberA()
    {
        var q1 = ChargedUnevidencedDeduction(1, 4, BenchmarkClaimVerdict.Supported);
        q1.ClaimVerificationJson = Json(
            Accused("A", BenchmarkClaimVerdict.Supported),
            Claim("A", BenchmarkClaimVerdict.Supported),
            Accused("B", BenchmarkClaimVerdict.Refuted) with { ClaimIndex = 2, Claim = "A sentence member B charged, upheld." });
        var run = Run(q1, Answer(2));

        var result = BenchmarkPanelSensitivity.Compute(run, run.Answers, Constants);

        Assert.Equal(new[] { 1 }, result.LiftedA);
        Assert.Empty(result.LiftedB);
        Assert.True(BenchmarkService.IsVerificationClearedAccuracyDeduction(q1));
    }

    [Fact]
    public void ASupportedAccusationWithoutACitation_IsNotEligible()
    {
        var q1 = Answer(1);
        q1.ClaimVerificationJson = Json(Accused("A", BenchmarkClaimVerdict.Supported, citation: null));
        var run = Run(q1, Answer(2));

        Assert.Empty(BenchmarkPanelSensitivity.Compute(run, run.Answers, Constants).LiftedA);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void ACriticalErrorOnEitherMember_IsNotEligible(string flaggingMember)
    {
        var q1 = Answer(1);
        q1.ClaimVerificationJson = Json(Accused("A", BenchmarkClaimVerdict.Supported));
        if (flaggingMember == "A")
        {
            q1.CriticalError = true;
        }
        else
        {
            q1.CoAssessmentCriticalError = true;
            var record = BenchmarkCoAssessmentRecord.Parse(q1.CoAssessmentJson)!;
            record.CriticalError = true;
            q1.CoAssessmentJson = record.Serialize();
        }
        var run = Run(q1, Answer(2));

        var result = BenchmarkPanelSensitivity.Compute(run, run.Answers, Constants);

        Assert.Empty(result.LiftedA);
        Assert.Empty(result.LiftedB);
        Assert.Null(result.Index);
    }

    [Fact]
    public void AMemberAlreadyAtAccuracy6_IsNotEligible()
    {
        var q1 = Answer(1, accuracyA: 6);
        q1.ClaimVerificationJson = Json(Accused("A", BenchmarkClaimVerdict.Supported));
        var run = Run(q1, Answer(2));

        Assert.Empty(BenchmarkPanelSensitivity.Compute(run, run.Answers, Constants).LiftedA);
    }

    [Fact]
    public void AnEmptyPopulation_GivesANullIndex()
    {
        var run = Run(Answer(1), Answer(2));

        var result = BenchmarkPanelSensitivity.Compute(run, run.Answers, Constants);

        Assert.Null(result.Index);
        Assert.Empty(result.LiftedA);
        Assert.Empty(result.LiftedB);
    }

    [Fact]
    public void ASingleAssessorRun_IsNeverComputed()
    {
        var q1 = Answer(1);
        q1.ClaimVerificationJson = Json(Accused("A", BenchmarkClaimVerdict.Supported));
        var run = Run(false, q1, Answer(2));

        Assert.Same(PanelSensitivityResult.Empty, BenchmarkPanelSensitivity.Compute(run, run.Answers, Constants));
    }

    [Fact]
    public void TheIndexEqualsThePublishedIndex_WhenTheLiftMovesNoScore()
    {
        // Member A's stored score on Q1 is already the one its lifted levels give, so the lift is a
        // no-op: what remains is the panel mean, the difficulty weighting and the fallback
        // difficulty, which must reproduce the published index exactly.
        var q1 = Answer(1, accuracyA: 4, accuracyB: 3, assessedDifficulty: 20);
        q1.QualityScore = Score(5);
        q1.RawQualityScore = Score(5);
        q1.ClaimVerificationJson = Json(Accused("A", BenchmarkClaimVerdict.Supported));
        var q2 = Answer(2, accuracyA: 5, accuracyB: 2, assessedDifficulty: 80);
        var q3 = Answer(3, accuracyA: 3, accuracyB: 5, assessedDifficulty: null);
        q3.Difficulty = BenchmarkDifficulty.Advanced;
        var run = Run(q1, q2, q3);

        var result = BenchmarkPanelSensitivity.Compute(run, run.Answers, Constants);

        Assert.Equal(new[] { 1 }, result.LiftedA);
        Assert.NotNull(run.QualityIndex);
        Assert.Equal(run.QualityIndex, (int?)result.Index);
    }

    [Fact]
    public void ALiftHiddenByRounding_StillShowsInTheUnroundedFigures()
    {
        // Twenty answers at one score; lifting member A on one of them moves the index by less than
        // half a point, so the rounded sensitivity equals the published index.
        const int count = 20;
        var answers = Enumerable.Range(1, count).Select(i => Answer(i)).ToArray();
        answers[0].ClaimVerificationJson = Json(Accused("A", BenchmarkClaimVerdict.Supported));
        var run = Run(answers);

        var result = BenchmarkPanelSensitivity.Compute(run, run.Answers, Constants);

        double lift = (Score(5) - Score(4)) / 2.0 / count;
        Assert.True(lift > 0.0 && lift < 0.5, $"The fixture's lift is {lift}.");
        Assert.Equal(new[] { 1 }, result.LiftedA);
        Assert.Equal(run.QualityIndex, (int?)result.Index);
        Assert.Equal(Score(4), result.UnroundedPublished!.Value, 9);
        Assert.Equal(Score(4) + lift, result.UnroundedIndex!.Value, 9);
        Assert.True(result.UnroundedIndex > result.UnroundedPublished);
    }

    [Fact]
    public void TheEmptyResult_HasNoUnroundedFigures()
    {
        Assert.Null(PanelSensitivityResult.Empty.UnroundedIndex);
        Assert.Null(PanelSensitivityResult.Empty.UnroundedPublished);
    }

    [Theory]
    [InlineData("43", true)]
    [InlineData("44", false)]
    [InlineData("47", false)]
    [InlineData(null, true)]
    public void IsApproximate_BeforeHarness44(string? harnessVersion, bool expected)
    {
        Assert.Equal(expected, BenchmarkPanelSensitivity.IsApproximate(new BenchmarkRun { HarnessVersion = harnessVersion }));
    }
}
