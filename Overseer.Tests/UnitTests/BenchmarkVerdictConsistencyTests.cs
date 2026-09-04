namespace Overseer.Tests.UnitTests;

using System.Linq;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkVerdictConsistencyTests
{
    [Theory]
    [InlineData("The response hallucinates 'adamantium'.")]
    [InlineData("Hallucinated a material that does not exist in GnollHack.")]
    [InlineData("The answer fabricates a spell school.")]
    [InlineData("It invents a racial intrinsic not present in the rubric.")]
    [InlineData("Cites a non-existent function.")]
    [InlineData("References a nonexistent constant.")]
    [InlineData("There is no such item in the game.")]
    [InlineData("The AC value appears to be made up.")]
    public void MentionsFabrication_MatchesTheVocabulary(string comment)
    {
        Assert.True(BenchmarkVerdictConsistency.MentionsFabrication(comment));
    }

    [Theory]
    // The reason "invent" carries a negative lookahead. This is a benchmark about a roguelike:
    // "inventory" appears in a large share of answers and assessor comments, and matching it
    // would flag most of the suite as contested.
    [InlineData("Correctly describes the inventory letter assignment.")]
    [InlineData("The inventory weight calculation is accurate.")]
    [InlineData("Accurate and thorough breakdown of Master Kaen's stats.")]
    [InlineData("Omits bronze armor mechanics.")]
    [InlineData("")]
    [InlineData(null)]
    public void MentionsFabrication_DoesNotMatchOrdinaryProse(string? comment)
    {
        Assert.False(BenchmarkVerdictConsistency.MentionsFabrication(comment));
    }

    [Fact]
    public void QuestionsNamedWithFabrication_FindsTheQuestionInTheSentenceThatNamesIt()
    {
        // Close to the 2026-09-03 run's actual synthesis, which reported Q10's hallucination as
        // the headline finding of a run whose per-question verdict had not flagged it.
        const string synthesis =
            "The benchmark demonstrates an extraordinarily high degree of competence. " +
            "The only notable flaw occurred in Question 10 regarding object materials, where the " +
            "model hallucinated non-existent materials ('adamantium'). " +
            "Overall the run represents an elite understanding of the game's systems.";

        var named = BenchmarkVerdictConsistency.QuestionsNamedWithFabrication(
            synthesis, Enumerable.Range(1, 18));

        Assert.Equal(new[] { 10 }, named);
    }

    [Fact]
    public void QuestionsNamedWithFabrication_DoesNotReachAcrossSentences()
    {
        // Sentence-scoped on purpose: a paragraph-wide match would attribute one question's
        // hallucination to every question mentioned near it.
        const string synthesis =
            "Question 4 was answered accurately and concisely. " +
            "Question 10 hallucinates a material that does not exist.";

        var named = BenchmarkVerdictConsistency.QuestionsNamedWithFabrication(
            synthesis, Enumerable.Range(1, 18));

        Assert.Equal(new[] { 10 }, named);
    }

    [Theory]
    [InlineData("Question 7 invents a spell.")]
    [InlineData("Q7 invents a spell.")]
    [InlineData("Q 7 invents a spell.")]
    [InlineData("Q#7 invents a spell.")]
    public void QuestionsNamedWithFabrication_AcceptsTheUsualReferenceForms(string sentence)
    {
        var named = BenchmarkVerdictConsistency.QuestionsNamedWithFabrication(
            sentence, Enumerable.Range(1, 18));

        Assert.Equal(new[] { 7 }, named);
    }

    [Fact]
    public void QuestionsNamedWithFabrication_IgnoresQuestionsOutsideTheRun()
    {
        var named = BenchmarkVerdictConsistency.QuestionsNamedWithFabrication(
            "Question 99 hallucinates a material.", Enumerable.Range(1, 18));

        Assert.Empty(named);
    }

    [Fact]
    public void QuestionsNamedWithFabrication_ReturnsNothingForCleanSynthesis()
    {
        const string synthesis =
            "Question 3 was accurate and complete. Question 10 covered the material list well.";

        Assert.Empty(BenchmarkVerdictConsistency.QuestionsNamedWithFabrication(
            synthesis, Enumerable.Range(1, 18)));
    }

    [Fact]
    public void QuestionsNamedWithFabrication_HandlesEmptyInput()
    {
        Assert.Empty(BenchmarkVerdictConsistency.QuestionsNamedWithFabrication(null, Enumerable.Range(1, 5)));
        Assert.Empty(BenchmarkVerdictConsistency.QuestionsNamedWithFabrication("Q1 invents a thing.", Enumerable.Empty<int>()));
    }

    [Theory]
    [InlineData("Matches rubric.", true)]
    [InlineData("matches the rubric", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("N/A", true)]
    [InlineData("Matches rubric points 1-3 but omits point 4.", false)]
    [InlineData("Rubric point 2: omits 350/175.", false)]
    public void IsNoFaultEvidence_IdentifiesNoFaultStrings(string? evidence, bool expected)
    {
        Assert.Equal(expected, BenchmarkVerdictConsistency.IsNoFaultEvidence(evidence));
    }

    [Fact]
    public void HasUnevidencedDeduction_Q1_AccuracyFourWithNoFaultEvidence_ReturnsTrue()
    {
        // Q1 shape from run 8: Accuracy = 4/6 with accuracyEvidence = "Matches rubric."
        Assert.True(BenchmarkVerdictConsistency.HasUnevidencedDeduction(
            accuracyLevel: 4,
            accuracyEvidence: "Matches rubric.",
            completenessLevel: 6,
            completenessEvidence: "Matches rubric."));
    }

    [Fact]
    public void HasUnevidencedDeduction_Q5_AccuracyFiveWithNoFaultEvidence_ReturnsFalse_PinsMaxLevel()
    {
        // Q5 shape from run 8: Accuracy = 5/6 with accuracyEvidence = "Matches rubric."
        // Must return false to pin UnevidencedDeductionMaxLevel == 4.
        Assert.Equal(4, BenchmarkVerdictConsistency.UnevidencedDeductionMaxLevel);
        Assert.False(BenchmarkVerdictConsistency.HasUnevidencedDeduction(
            accuracyLevel: 5,
            accuracyEvidence: "Matches rubric.",
            completenessLevel: 6,
            completenessEvidence: "Matches rubric."));
    }

    [Fact]
    public void HasUnevidencedDeduction_CrossDimension_AccuracyFourWithEvidence_CompletenessSixWithNoFault_ReturnsFalse()
    {
        // Accuracy 4 docked with real evidence; completeness 6 awarded with "Matches rubric."
        // Because completeness was not docked (level 6), its no-fault evidence is legitimate.
        Assert.False(BenchmarkVerdictConsistency.HasUnevidencedDeduction(
            accuracyLevel: 4,
            accuracyEvidence: "Misstated Master Kaen AC as -5 instead of -2.",
            completenessLevel: 6,
            completenessEvidence: "Matches rubric."));
    }

    [Fact]
    public void IsUnverifiabilityGroundedDeduction_Run9Q1Evidence_Flags()
    {
        const string evidence = "Docked to 4 because claims cannot be verified from the provided context.";
        Assert.True(BenchmarkVerdictConsistency.IsUnverifiabilityGroundedDeduction(
            accuracyLevel: 4,
            accuracyEvidence: evidence,
            unverifiedClaimCount: 3));
    }

    [Theory]
    [InlineData("Claims cannot be verified and the answer hallucinates.")]
    [InlineData("Cannot be verified; contains an incorrect statement about AC.")]
    [InlineData("Fabricates nonexistent monster stats.")]
    public void IsUnverifiabilityGroundedDeduction_EvidenceNamingADefect_DoesNotFlag(string evidence)
    {
        Assert.False(BenchmarkVerdictConsistency.IsUnverifiabilityGroundedDeduction(
            accuracyLevel: 4,
            accuracyEvidence: evidence,
            unverifiedClaimCount: 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IsUnverifiabilityGroundedDeduction_NoUnverifiedClaims_DoesNotFlag(int unverifiedClaimCount)
    {
        Assert.False(BenchmarkVerdictConsistency.IsUnverifiabilityGroundedDeduction(
            accuracyLevel: 4,
            accuracyEvidence: "Docked to 4 because claims cannot be verified from the provided context.",
            unverifiedClaimCount: unverifiedClaimCount));
    }

    [Fact]
    public void IsUnverifiabilityGroundedDeduction_AccuracyLevelFive_DoesNotFlag()
    {
        Assert.False(BenchmarkVerdictConsistency.IsUnverifiabilityGroundedDeduction(
            accuracyLevel: 5,
            accuracyEvidence: "Docked to 4 because claims cannot be verified from the provided context.",
            unverifiedClaimCount: 3));
    }

    [Fact]
    public void IsOmissionGroundedAccuracyDeduction_SubstitutionEvidence_DoesNotFlag()
    {
        // Q1's literal evidence string from run 11: describes a substitution ("provides ... rather than ..."),
        // which is a genuine accuracy defect and must not be flagged as an omission.
        const string substitutionEvidence =
            "The answer provides D&D-style relative attribute modifiers (+1 Dex, -2 Int, etc.) rather than GnollHack's racial attribute maxima (Str 18/100, Int 16, Wis 16, Dex 19, Con 19, Cha 16).";
        Assert.False(BenchmarkVerdictConsistency.IsOmissionGroundedAccuracyDeduction(
            accuracyLevel: 4,
            accuracyEvidence: substitutionEvidence));
    }

    [Fact]
    public void IsOmissionGroundedAccuracyDeduction_PureOmissionEvidence_Flags()
    {
        const string omissionEvidence = "The answer omits the racial attribute maxima.";
        Assert.True(BenchmarkVerdictConsistency.IsOmissionGroundedAccuracyDeduction(
            accuracyLevel: 4,
            accuracyEvidence: omissionEvidence));
    }

    [Fact]
    public void IsOmissionGroundedAccuracyDeduction_Run10Q10Shape_Flags()
    {
        // Q10 shape from run 10: fails to identify/mention
        const string evidence = "Rubric REQUIRED: hard crystal grants REFLECTION ..., orichalcum grants MAGIC RESISTANCE (+7 MC) ...; the answer fails to identify these crucial specific properties.";
        Assert.True(BenchmarkVerdictConsistency.IsOmissionGroundedAccuracyDeduction(
            accuracyLevel: 3,
            accuracyEvidence: evidence));
    }

    [Fact]
    public void IsOmissionGroundedAccuracyDeduction_WithFalsehood_DoesNotFlag()
    {
        // When the evidence names a genuine inaccuracy or falsehood along with an omission, it is not a pure omission
        const string evidence = "Incorrect statement that silver grants poison resistance, and fails to mention AC.";
        Assert.False(BenchmarkVerdictConsistency.IsOmissionGroundedAccuracyDeduction(
            accuracyLevel: 3,
            accuracyEvidence: evidence));
    }

    [Fact]
    public void IsOmissionGroundedAccuracyDeduction_AccuracyLevelFiveOrSix_DoesNotFlag()
    {
        Assert.False(BenchmarkVerdictConsistency.IsOmissionGroundedAccuracyDeduction(
            accuracyLevel: 5,
            accuracyEvidence: "Omits attribute maxima."));
        Assert.False(BenchmarkVerdictConsistency.IsOmissionGroundedAccuracyDeduction(
            accuracyLevel: 6,
            accuracyEvidence: "Omits attribute maxima."));
    }

    [Fact]
    public void IsOmissionGroundedAccuracyDeduction_EmptyOrNullEvidence_DoesNotFlag()
    {
        Assert.False(BenchmarkVerdictConsistency.IsOmissionGroundedAccuracyDeduction(3, null));
        Assert.False(BenchmarkVerdictConsistency.IsOmissionGroundedAccuracyDeduction(3, ""));
        Assert.False(BenchmarkVerdictConsistency.IsOmissionGroundedAccuracyDeduction(3, "   "));
    }
}
