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

    /// <summary>A verdict whose completeness evidence is whatever the test needs it to be.</summary>
    private static string VerdictWithCompletenessEvidence(string completenessEvidence)
    {
        return $$"""
        {
          "accuracyLevel": 5,
          "completenessLevel": 5,
          "concisenessLevel": 5,
          "readabilityLevel": 5,
          "criticalError": false,
          "criticalErrorQuote": null,
          "unverifiedClaims": [],
          "accuracyEvidence": "Matches rubric.",
          "completenessEvidence": "{{completenessEvidence}}",
          "comment": "A reasonable answer."
        }
        """;
    }

    [Fact]
    public void CompletenessOutOfScope_IsSetWhenTheMarkerIsPresent()
    {
        // Q12's shape on the 2026-09-06 run, as scoring method v8 asks the assessor to record it:
        // the rubric enumerates more than the question asked for, and that is not an omission.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithCompletenessEvidence(
                "OUT-OF-SCOPE: the rubric lists Celestial/Primordial/Infernal modifiers; the question asked only for Exceptional and Elite."),
            Answer);

        Assert.True(result.Success);
        Assert.True(result.Result!.CompletenessOutOfScope);
    }

    [Fact]
    public void CompletenessOutOfScope_IsSetWhenTheMarkerFollowsARealDeduction()
    {
        // One evidence string can carry both a deduction and an out-of-scope note, so the marker is
        // matched anywhere in the string rather than only at its start.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithCompletenessEvidence(
                "Rubric point 2: the answer omits the crowning penalty. OUT-OF-SCOPE: rubric point 5 covers Infernal armor, which the question did not ask about."),
            Answer);

        Assert.True(result.Result!.CompletenessOutOfScope);
    }

    [Fact]
    public void CompletenessOutOfScope_IsNotSetWhenTheMarkerIsAbsent()
    {
        // The normal case. A missing marker is never an error: most rubrics ask for nothing the
        // question did not.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithCompletenessEvidence("Rubric point 2: the answer omits the crowning penalty."),
            Answer);

        Assert.True(result.Success);
        Assert.False(result.Result!.CompletenessOutOfScope);
    }

    [Theory]
    // A mangled marker costs the measurement and nothing else — never the verdict, and never an
    // exception. The last case is the ordinary English phrase without the colon, which must not
    // count as the marker: it appears in evidence strings that are describing something else.
    [InlineData("OUT-OF-SCOPE")]
    [InlineData("out_of_scope:")]
    [InlineData("OUT-OF-SCOPE;")]
    [InlineData("The rubric point is arguably out of scope for this question.")]
    [InlineData("")]
    public void CompletenessOutOfScope_MalformedMarker_ParsesWithoutThrowingAndDoesNotSetTheFlag(string evidence)
    {
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithCompletenessEvidence(evidence),
            Answer);

        Assert.True(result.Success);
        Assert.False(result.Result!.CompletenessOutOfScope);
    }

    [Theory]
    // Casing and separator tolerance: models write the marker the way they feel like writing it.
    [InlineData("OUT-OF-SCOPE: rubric point 5.")]
    [InlineData("Out of scope: rubric point 5.")]
    [InlineData("out-of-scope : rubric point 5.")]
    [InlineData("OUTOFSCOPE: rubric point 5.")]
    public void CompletenessOutOfScope_ToleratesTheFormsAssessorsActuallyWrite(string evidence)
    {
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithCompletenessEvidence(evidence),
            Answer);

        Assert.True(result.Result!.CompletenessOutOfScope);
    }

    [Fact]
    public void CompletenessOutOfScope_Run38Q6Shape_AlsoCarriesTheUnevidencedDeductionFlag()
    {
        // The two readings of one verdict, and both belong on it. The marker was written, so the
        // measurement counts it; the level was docked to 5 with nothing in scope named, so the
        // advisory flag fires as well. Accuracy is held at 6 here so the flag can only have come
        // from the completeness side.
        const string verdict = """
        {
          "accuracyLevel": 6,
          "completenessLevel": 5,
          "concisenessLevel": 5,
          "readabilityLevel": 5,
          "criticalError": false,
          "criticalErrorQuote": null,
          "unverifiedClaims": [],
          "accuracyEvidence": "Matches rubric.",
          "completenessEvidence": "OUT-OF-SCOPE: the rubric enumerates the full material table; the question asked only about dragon scale mail.",
          "comment": "A reasonable answer."
        }
        """;

        var result = BenchmarkAssessmentParser.ParsePerQuestion(verdict, Answer);

        Assert.True(result.Success);
        Assert.True(result.Result!.CompletenessOutOfScope);
        Assert.True(result.Result!.UnevidencedDeduction);
    }

    /// <summary>A verdict whose readability evidence is whatever the test needs it to be.</summary>
    private static string VerdictWithReadabilityEvidence(string readabilityEvidence)
    {
        return $$"""
        {
          "accuracyLevel": 5,
          "completenessLevel": 5,
          "concisenessLevel": 5,
          "readabilityLevel": 5,
          "criticalError": false,
          "criticalErrorQuote": null,
          "unverifiedClaims": [],
          "accuracyEvidence": "Matches rubric.",
          "completenessEvidence": "Matches rubric.",
          "readabilityEvidence": "{{readabilityEvidence}}",
          "comment": "A reasonable answer."
        }
        """;
    }

    [Fact]
    public void ReadabilityFormOnly_IsSetWhenTheMarkerIsPresent()
    {
        // The shape scoring method v9 asks for: the rubric named a presentation, the answer used
        // another, and the assessor records that instead of docking Readability for it.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithReadabilityEvidence(
                "FORM: the rubric suggests a comparison table; the answer covers the same material as prose."),
            Answer);

        Assert.True(result.Success);
        Assert.True(result.Result!.ReadabilityFormOnly);
    }

    [Fact]
    public void ReadabilityFormOnly_IsNotSetWhenTheMarkerIsAbsent()
    {
        // The normal case, and the one the field is normally null in.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithReadabilityEvidence(""),
            Answer);

        Assert.True(result.Success);
        Assert.False(result.Result!.ReadabilityFormOnly);
    }

    [Theory]
    // Anchored, unlike the out-of-scope marker: 'form' is an ordinary word, and readabilityEvidence
    // carries the marker and nothing else, so a mid-sentence occurrence is prose about the answer
    // rather than a marker. A mangled marker costs the measurement and nothing else.
    [InlineData("The rubric's FORM: section asks for a table, which is worth noting.")]
    [InlineData("Deducted one level because the answer ignored the rubric FORM: guidance.")]
    [InlineData("FORM")]
    [InlineData("FORMAT: the rubric suggests a table.")]
    [InlineData("Clear headings and short paragraphs throughout.")]
    public void ReadabilityFormOnly_MarkerNotAtTheStart_DoesNotSetTheFlag(string evidence)
    {
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithReadabilityEvidence(evidence),
            Answer);

        Assert.True(result.Success);
        Assert.False(result.Result!.ReadabilityFormOnly);
    }

    [Theory]
    // Casing and spacing tolerance: models write the marker the way they feel like writing it.
    [InlineData("FORM: the rubric suggests a table.")]
    [InlineData("Form: the rubric suggests a table.")]
    [InlineData("form : the rubric suggests a table.")]
    [InlineData("  FORM: the rubric suggests a table.")]
    public void ReadabilityFormOnly_ToleratesTheFormsAssessorsActuallyWrite(string evidence)
    {
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithReadabilityEvidence(evidence),
            Answer);

        Assert.True(result.Result!.ReadabilityFormOnly);
    }

    [Fact]
    public void ReadabilityEvidence_IsAbsent_ParsesAndLeavesTheFlagClear()
    {
        // Every verdict graded before v9 has no such field at all, and re-parsing one must not fail.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithCompletenessEvidence("Matches rubric."),
            Answer);

        Assert.True(result.Success);
        Assert.Null(result.Result!.ReadabilityEvidence);
        Assert.False(result.Result!.ReadabilityFormOnly);
    }

    /// <summary>
    /// A verdict whose accuracy evidence is whatever the test needs it to be. Unlike its
    /// completeness and readability siblings, this accepts a null evidence string as well, so the
    /// same helper covers the "field absent/blank" fixtures below.
    /// </summary>
    private static string VerdictWithAccuracyEvidence(string? accuracyEvidence)
    {
        string evidenceJson = accuracyEvidence == null ? "null" : $"\"{accuracyEvidence}\"";
        return $$"""
        {
          "accuracyLevel": 5,
          "completenessLevel": 5,
          "concisenessLevel": 5,
          "readabilityLevel": 5,
          "criticalError": false,
          "criticalErrorQuote": null,
          "unverifiedClaims": [],
          "accuracyEvidence": {{evidenceJson}},
          "completenessEvidence": "Matches rubric.",
          "comment": "A reasonable answer."
        }
        """;
    }

    [Fact]
    public void AccuracyOutOfRubric_IsSetWhenTheMarkerIsPresent()
    {
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithAccuracyEvidence("Not in rubric: the answer omits the Yeenaghu wish condition."),
            Answer);

        Assert.True(result.Success);
        Assert.True(result.Result!.AccuracyOutOfRubric);
    }

    [Fact]
    public void AccuracyOutOfRubric_IsSetWhenTheMarkerFollowsARealDeduction()
    {
        // One evidence string can carry both a rubric deduction and an out-of-rubric note, so the
        // marker is matched anywhere in the string rather than only at its start — mirroring the
        // out-of-scope marker's own reasoning.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithAccuracyEvidence("Rubric point 2 unmet. Not in rubric: the AC figure is stated as a bonus."),
            Answer);

        Assert.True(result.Result!.AccuracyOutOfRubric);
    }

    [Theory]
    // Casing and spacing tolerance, plus the optional "the" the prompt allows: models write the
    // marker the way they feel like writing it.
    [InlineData("NOT IN RUBRIC: fabricated racial trait.")]
    [InlineData("not  in   rubric : fabricated racial trait.")]
    [InlineData("Not in the rubric: fabricated racial trait.")]
    public void AccuracyOutOfRubric_ToleratesTheFormsAssessorsActuallyWrite(string evidence)
    {
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithAccuracyEvidence(evidence),
            Answer);

        Assert.True(result.Result!.AccuracyOutOfRubric);
    }

    [Theory]
    // A mangled marker, or the ordinary English phrase without the colon, costs the measurement
    // and nothing else — never the verdict, and never an exception. The colon is the required
    // part: without it, this is prose describing something else.
    [InlineData("Matches rubric.")]
    [InlineData("The rubric does not mention this, but it is wrong.")]
    [InlineData("Not in rubric")]
    public void AccuracyOutOfRubric_MalformedOrAbsentMarker_DoesNotSetTheFlag(string evidence)
    {
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithAccuracyEvidence(evidence),
            Answer);

        Assert.True(result.Success);
        Assert.False(result.Result!.AccuracyOutOfRubric);
    }

    [Fact]
    public void AccuracyOutOfRubric_IsNotSetWhenAccuracyEvidenceIsNull()
    {
        // Total on its input, like HasOutOfScopeMarker: a null evidence string must not fail the
        // parse.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithAccuracyEvidence(null),
            Answer);

        Assert.True(result.Success);
        Assert.False(result.Result!.AccuracyOutOfRubric);
    }

    [Fact]
    public void AccuracyOutOfRubric_IsNotSetWhenAccuracyEvidenceIsEmpty()
    {
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithAccuracyEvidence(""),
            Answer);

        Assert.True(result.Success);
        Assert.False(result.Result!.AccuracyOutOfRubric);
    }

    [Fact]
    public void AccuracyOutOfRubric_IsNotSetWhenTheMarkerIsInCompletenessEvidenceOnly()
    {
        // Pins which dimension the property reads: the pre-existing out-of-scope marker reads
        // Completeness evidence, this one reads Accuracy evidence, and a marker sitting in the
        // wrong field must not cross over.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            """
            {
              "accuracyLevel": 5, "completenessLevel": 5, "concisenessLevel": 5, "readabilityLevel": 5,
              "criticalError": false,
              "accuracyEvidence": "Matches rubric.",
              "completenessEvidence": "Not in rubric: this belongs to the wrong dimension.",
              "comment": "A reasonable answer."
            }
            """,
            Answer);

        Assert.True(result.Success);
        Assert.False(result.Result!.AccuracyOutOfRubric);
    }

    [Fact]
    public void AccuracyOutOfRubric_DoesNotDisturbThePreExistingCompletenessOutOfScopeMarker()
    {
        // Regression: the two markers are read from different fields by different helpers, and
        // adding the accuracy one must not have changed what the completeness one does.
        var result = BenchmarkAssessmentParser.ParsePerQuestion(
            VerdictWithCompletenessEvidence(
                "OUT-OF-SCOPE: the rubric lists Celestial/Primordial/Infernal modifiers; the question asked only for Exceptional and Elite."),
            Answer);

        Assert.True(result.Success);
        Assert.True(result.Result!.CompletenessOutOfScope);
    }
}
