namespace Overseer.Tests.UnitTests;

using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkAssessmentParserTests
{
    private const string Answer =
        "Gnolls are a hyena-headed race. They are immune to lycanthropy. " +
        "Yeenaghu will be peaceful and grant a wish when met by a chaotic gnoll. " +
        "They can smell rotten food and underground roots.";

    private static string Verdict(
        string? unverifiedClaims = null,
        bool criticalError = false,
        string comment = "A reasonable answer.",
        string accuracyEvidence = "Matches rubric.")
    {
        string claims = unverifiedClaims ?? "[]";
        return $$"""
        {
          "accuracyLevel": 3,
          "completenessLevel": 4,
          "concisenessLevel": 5,
          "readabilityLevel": 5,
          "criticalError": {{(criticalError ? "true" : "false")}},
          "criticalErrorQuote": null,
          "unverifiedClaims": {{claims}},
          "accuracyEvidence": "{{accuracyEvidence}}",
          "completenessEvidence": "Matches rubric.",
          "comment": "{{comment}}"
        }
        """;
    }

    [Fact]
    public void UnverifiedClaims_AreParsedWhenQuotedFromTheAnswer()
    {
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            Verdict(unverifiedClaims: """["Yeenaghu will be peaceful and grant a wish when met by a chaotic gnoll."]"""),
            Answer);

        Assert.True(result.Success);
        Assert.Single(result.Result!.UnverifiedClaims);
        Assert.Equal(0, result.Result.UnverifiedClaimsDropped);
    }

    [Fact]
    public void UnverifiedClaims_AreDroppedWhenTheyDoNotAppearInTheAnswer()
    {
        // Same verification the critical-error quote gets, for the same reason: a claim that is
        // not in the answer is not a claim the answer made. Letting a paraphrase through would
        // let it accumulate across runs and be read later as cross-model corroboration of a
        // rubric gap nobody actually asserted.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            Verdict(unverifiedClaims: """["The candidate said gnolls get infravision, which I cannot confirm."]"""),
            Answer);

        Assert.True(result.Success);
        Assert.Empty(result.Result!.UnverifiedClaims);
        Assert.Equal(1, result.Result.UnverifiedClaimsDropped);
    }

    [Fact]
    public void UnverifiedClaims_AreTakenAsGivenWhenNoAnswerTextIsAvailable()
    {
        // Re-parsing a stored verdict has no answer to check against; the claims still parse.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            Verdict(unverifiedClaims: """["Some claim that is nowhere in any answer."]"""),
            gradedAnswerText: null);

        Assert.True(result.Success);
        Assert.Single(result.Result!.UnverifiedClaims);
    }

    [Fact]
    public void UnverifiedClaims_AcceptABareStringAsWellAsAnArray()
    {
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            Verdict(unverifiedClaims: "\"They are immune to lycanthropy.\""),
            Answer);

        Assert.True(result.Success);
        Assert.Single(result.Result!.UnverifiedClaims);
    }

    [Fact]
    public void UnverifiedClaims_DefaultToEmptyWhenAbsent()
    {
        const string verdictWithoutTheField = """
        {
          "accuracyLevel": 5, "completenessLevel": 5, "concisenessLevel": 5, "readabilityLevel": 5,
          "criticalError": false, "comment": "Fine."
        }
        """;

        var result = BenchmarkAssessmentParser.ParsePerQuestion(verdictWithoutTheField, Answer);

        Assert.True(result.Success);
        Assert.Empty(result.Result!.UnverifiedClaims);
    }

    [Fact]
    public void ContestedVerdict_IsSetWhenTheCommentDescribesAFabricationButTheFlagIsFalse()
    {
        // The Q10 shape from the 2026-09-03 run.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            Verdict(comment: "Accurate on orichalcum, but hallucinates 'adamantium' and omits bronze."),
            Answer);

        Assert.True(result.Success);
        Assert.False(result.Result!.CriticalError);
        Assert.True(result.Result.ContestedVerdict);
    }

    [Fact]
    public void ContestedVerdict_IsSetFromTheEvidenceStringsToo()
    {
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            Verdict(accuracyEvidence: "Candidate invents a racial intrinsic not in the rubric."),
            Answer);

        Assert.True(result.Result!.ContestedVerdict);
    }

    [Fact]
    public void ContestedVerdict_IsNotSetWhenTheAssessorActuallyFlaggedTheCriticalError()
    {
        // Nothing is contested when the two agree: the flag is for the divergence alone.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            """
            {
              "accuracyLevel": 1, "completenessLevel": 2, "concisenessLevel": 5, "readabilityLevel": 5,
              "criticalError": true,
              "criticalErrorQuote": "They are immune to lycanthropy.",
              "comment": "Hallucinates an intrinsic.",
              "accuracyEvidence": "Not in rubric.", "completenessEvidence": "Partial."
            }
            """,
            Answer);

        Assert.True(result.Result!.CriticalError);
        Assert.False(result.Result.ContestedVerdict);
    }

    [Fact]
    public void ContestedVerdict_IsNotSetForOrdinaryProse()
    {
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            Verdict(comment: "Correctly describes the inventory letters and the scent abilities."),
            Answer);

        Assert.False(result.Result!.ContestedVerdict);
    }

    [Fact]
    public void ContestedVerdict_IsSetWhenADemotedCriticalErrorLeavesFabricationLanguageBehind()
    {
        // The parser demotes an uncited critical error to false. That is exactly the state the
        // contested-verdict flag exists to catch, so the demotion must not hide it.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            """
            {
              "accuracyLevel": 2, "completenessLevel": 3, "concisenessLevel": 5, "readabilityLevel": 5,
              "criticalError": true,
              "criticalErrorQuote": "A sentence that is nowhere in the graded answer at all.",
              "comment": "The answer fabricates a material.",
              "accuracyEvidence": "Not in rubric.", "completenessEvidence": "Partial."
            }
            """,
            Answer);

        Assert.False(result.Result!.CriticalError);
        Assert.True(result.Result.CriticalErrorDemoted);
        Assert.True(result.Result.ContestedVerdict);
    }

    [Fact]
    public void OmissionAsAccuracy_IsSetWhenAccuracyEvidenceDescribesOmissionWithoutFalsehood()
    {
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            Verdict(
                accuracyEvidence: "Rubric REQUIRED: omits the racial attribute maxima.",
                comment: "Good answer but missing details."),
            Answer);

        Assert.True(result.Success);
        Assert.True(result.Result!.OmissionAsAccuracy);
    }

    [Fact]
    public void OmissionAsAccuracy_IsNotSetWhenAccuracyEvidenceHasFalsehood()
    {
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            Verdict(
                accuracyEvidence: "Rubric REQUIRED: omits bronze, and incorrectly claims iron gives reflection.",
                comment: "Has errors."),
            Answer);

        Assert.True(result.Success);
        Assert.False(result.Result!.OmissionAsAccuracy);
    }
}
