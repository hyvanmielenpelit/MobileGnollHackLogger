namespace Overseer.Tests.UnitTests;

using System;
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

    /// <summary>
    /// The 2026-09-06 run's synthesis, close to verbatim. It made two claims the same report's own
    /// verdicts contradicted: that the weaknesses were omissions rather than factual errors, and
    /// that the model avoided the single-turn weapon-swap error Q11's evidence says it made.
    /// </summary>
    private const string Run14Synthesis =
        "The model demonstrates an elite command of GnollHack's mechanics across the suite.\n" +
        "Identified weaknesses were confined to secondary omissions rather than factual errors or critical rubric failures.\n" +
        "It also avoided several common NetHack hallucinations, including single-turn weapon swapping costs.";

    /// <summary>
    /// The 2026-09-03 run's synthesis shape: strong prose that never makes the no-factual-errors
    /// claim. This is the string the detector must stay silent on, because a false positive here
    /// prints an accusation against a synthesis that said nothing wrong.
    /// </summary>
    private const string Run13Synthesis =
        "The model demonstrates an elite command of GnollHack's mechanics across the suite.\n" +
        "Weaknesses cluster in the Advanced band, where the answers thin out on implementation detail.\n" +
        "Question 4 understates the prayer timeout reset, which is the run's most consequential slip.";

    [Fact]
    public void SynthesisClaimsNoFactualErrors_FiresOnTheRun14Synthesis()
    {
        Assert.True(BenchmarkVerdictConsistency.SynthesisClaimsNoFactualErrors(Run14Synthesis));
    }

    [Fact]
    public void SynthesisClaimsNoFactualErrors_IsSilentOnACleanSynthesis()
    {
        Assert.False(BenchmarkVerdictConsistency.SynthesisClaimsNoFactualErrors(Run13Synthesis));
    }

    [Theory]
    [InlineData("The run was free of factual errors throughout.")]
    [InlineData("There were no factual errors in any answer.")]
    [InlineData("Weaknesses were omissions rather than factual errors.")]
    [InlineData("The answers were thorough and without factual errors.")]
    [InlineData("Identified weaknesses were confined to secondary omissions.")]
    public void SynthesisClaimsNoFactualErrors_CoversTheVocabularyFamily(string synthesis)
    {
        Assert.True(BenchmarkVerdictConsistency.SynthesisClaimsNoFactualErrors(synthesis));
    }

    [Theory]
    // A synthesis that admits factual errors is doing the opposite of the thing this detects.
    [InlineData("The run was not free of factual errors: Q14 misreports Master Kaen's level.")]
    [InlineData("Question 14 contains a factual error about monster difficulty.")]
    [InlineData("Two factual errors were identified, both in the Advanced band.")]
    [InlineData("")]
    [InlineData(null)]
    public void SynthesisClaimsNoFactualErrors_DoesNotFireWhenTheSynthesisAdmitsThem(string? synthesis)
    {
        Assert.False(BenchmarkVerdictConsistency.SynthesisClaimsNoFactualErrors(synthesis));
    }

    [Fact]
    public void SynthesisClaimsNoFactualErrors_DoesNotReachAcrossSentences()
    {
        // Sentence-scoped for the same reason QuestionsNamedWithFabrication is. This paragraph
        // names an omission in one sentence and a factual error in the next, which is what an
        // honest synthesis looks like; a paragraph-wide "confined to ... omissions" would match it.
        const string synthesis =
            "The weaknesses were confined to the Advanced band.\n" +
            "Two answers carried omissions, and Q14 carried a factual error about monster difficulty.";

        Assert.False(BenchmarkVerdictConsistency.SynthesisClaimsNoFactualErrors(synthesis));
    }

    /// <summary>
    /// The 2026-09-06 run's verdict shape: Q11 and Q14 docked to Accuracy 5 with evidence naming a
    /// concrete false assertion, beside answers whose evidence is the full-level boilerplate the
    /// prompt itself offers.
    /// </summary>
    private static (int OrderIndex, int? AccuracyLevel, string? AccuracyEvidence)[] Run14Verdicts()
    {
        return new (int, int?, string?)[]
        {
            (1, 5, "Matches rubric."),
            (2, 6, "Matches rubric."),
            (5, 5, "Aligns with rubric."),
            (9, 5, "Matches rubric"),
            (11, 5, "The answer covers the set mechanics but overlooks that weapon swapping between sets takes 0 turns in GnollHack, suggesting instead that switching weapons requires extra equipment management overhead."),
            (12, 6, null),
            (14, 5, "The answer lists Level as 40 and Hit dice as 25; in the monster definition LVL(25, 16, -10, 15, 10, -20), his level/HD is 25, while 40 is his monster difficulty."),
            (18, 5, "   ")
        };
    }

    [Fact]
    public void AnswersWithNamedAccuracyDefects_SelectsExactlyTheTwoQuestionsThatNameADefect()
    {
        var named = BenchmarkVerdictConsistency.AnswersWithNamedAccuracyDefects(Run14Verdicts());

        Assert.Equal(new[] { 11, 14 }, named);
    }

    [Fact]
    public void AnswersWithNamedAccuracyDefects_ExcludesFullLevelAndBoilerplateEvidence()
    {
        // The exclusion is what keeps this from firing on every level-5 answer of a strong run:
        // level 6 is faultless by definition, and "Matches rubric." beside level 5 names nothing.
        var verdicts = new (int, int?, string?)[]
        {
            (1, 6, "The answer is exact on every rubric point and adds the source line."),
            (2, 5, "Matches rubric."),
            (3, 5, "Matches rubric"),
            (4, 5, "Aligns with rubric."),
            (5, 5, null),
            (6, 5, ""),
            (7, null, "Excluded: Provider API error")
        };

        Assert.Empty(BenchmarkVerdictConsistency.AnswersWithNamedAccuracyDefects(verdicts));
    }

    [Fact]
    public void AnswersWithNamedAccuracyDefects_ToleratesAnEmptySet()
    {
        Assert.Empty(BenchmarkVerdictConsistency.AnswersWithNamedAccuracyDefects(
            Array.Empty<(int, int?, string?)>()));
    }

    [Fact]
    public void NamesAnAccuracyDefect_Run22Q3Q4Q17Shape_DoesNotFlag()
    {
        // Run 22's Q3/Q4/Q17 evidence: a sentence-form denial rather than the anchored
        // boilerplate IsNoFaultEvidence recognises. Before DefectDenialRegex this returned
        // true and stamped "Accuracy defect recorded: yes" onto an answer its own grader
        // recorded no defect for.
        const string evidence = "All stated stats and mechanics match the rubric with no factual errors.";
        Assert.False(BenchmarkVerdictConsistency.NamesAnAccuracyDefect(5, evidence));
    }

    [Fact]
    public void NamesAnAccuracyDefect_Run14Q11Shape_StillFlags()
    {
        const string evidence =
            "The answer covers the set mechanics but overlooks that weapon swapping between sets takes 0 turns in GnollHack, suggesting instead that switching weapons requires extra equipment management overhead.";
        Assert.True(BenchmarkVerdictConsistency.NamesAnAccuracyDefect(5, evidence));
    }

    [Fact]
    public void NamesAnAccuracyDefect_Run14Q14Shape_StillFlags()
    {
        const string evidence =
            "The answer lists Level as 40 and Hit dice as 25; in the monster definition LVL(25, 16, -10, 15, 10, -20), his level/HD is 25, while 40 is his monster difficulty.";
        Assert.True(BenchmarkVerdictConsistency.NamesAnAccuracyDefect(5, evidence));
    }

    [Fact]
    public void NamesAnAccuracyDefect_DenialAlongsideAFalsehood_StillFlags()
    {
        // FalsehoodRegex takes precedence over the denial, exactly as it does over
        // OmissionRegex in IsOmissionGroundedAccuracyDeduction: a denial paired with an
        // assertion that the answer stated something untrue still names a defect.
        const string evidence = "no contradictions, but states the level as 40 when it is 25";
        Assert.True(BenchmarkVerdictConsistency.NamesAnAccuracyDefect(5, evidence));
    }

    [Theory]
    [InlineData("no factual errors, but states the level as 40 when it is 25")]
    [InlineData("No inaccuracies, however the turn cost is given as 1 where the rubric has 0")]
    [InlineData("Without error, except that the armour class is quoted for the wrong form")]
    public void NamesAnAccuracyDefect_DenialConcededByALaterClause_StillFlags(string evidence)
    {
        // The defect these sentences go on to charge is neutral prose: it carries none of
        // FalsehoodRegex's assertion vocabulary and names no omission, so the concession marker is
        // the only thing separating them from a plain denial. Without it the run 14 guardrail holds
        // for "no contradictions" only, and only because that phrase happens to share a stem with
        // FalsehoodRegex.
        Assert.True(BenchmarkVerdictConsistency.NamesAnAccuracyDefect(5, evidence));
    }

    [Fact]
    public void NamesAnAccuracyDefect_DenialAlongsideAnOmission_StillFlags()
    {
        // OmissionRegex overrides the denial too: a bare omission (Q11's "overlooks that
        // weapon swapping ... takes 0 turns") already counts as a named defect above, so a
        // denial that sits beside an admitted omission cannot cancel it out either.
        const string evidence = "no factual errors, but omits the weapon-swap cost";
        Assert.True(BenchmarkVerdictConsistency.NamesAnAccuracyDefect(5, evidence));
    }

    [Theory]
    [InlineData("no factual error")]
    [InlineData("no factual errors")]
    [InlineData("zero factual errors")]
    [InlineData("no errors")]
    [InlineData("no inaccuracies")]
    [InlineData("free of factual errors")]
    [InlineData("devoid of factual errors")]
    [InlineData("without error")]
    [InlineData("without errors")]
    public void NamesAnAccuracyDefect_CoversTheDenialVocabularyFamily(string denial)
    {
        string evidence = $"Reviewed against the rubric point by point, {denial} were found in the answer.";
        Assert.False(BenchmarkVerdictConsistency.NamesAnAccuracyDefect(5, evidence));
    }

    [Theory]
    // These three DefectDenialRegex alternatives share a root with an existing FalsehoodRegex
    // alternative — "contradic" with "contradiction(s)"/"contradicted claims", "\bfalse" with
    // "false statements", "misstat" with "misstatements" — so evidence built from them always
    // also matches FalsehoodRegex, and the precedence rule then names a defect regardless of
    // whether a second, unrelated defect is actually present. DefectDenialRegex still recognises
    // the phrase; the override just never yields for it. No regression: this is exactly the
    // pre-existing behaviour for these three phrases before DefectDenialRegex existed.
    [InlineData("no contradiction")]
    [InlineData("no contradictions")]
    [InlineData("no contradicted claims")]
    [InlineData("no false statements")]
    [InlineData("no misstatements")]
    public void NamesAnAccuracyDefect_DenialSharingAFalsehoodRoot_StillFlags(string denial)
    {
        string evidence = $"Reviewed against the rubric point by point, {denial} were found in the answer.";
        Assert.True(BenchmarkVerdictConsistency.NamesAnAccuracyDefect(5, evidence));
    }

    [Fact]
    public void NamesAnAccuracyDefect_AccuracyLevelSixOrAbove_DoesNotFlag()
    {
        Assert.False(BenchmarkVerdictConsistency.NamesAnAccuracyDefect(6, "Overlooks a rubric point."));
        Assert.False(BenchmarkVerdictConsistency.NamesAnAccuracyDefect(6, null));
    }

    [Fact]
    public void NamesAnAccuracyDefect_TerseBoilerplateEvidence_DoesNotFlag()
    {
        Assert.False(BenchmarkVerdictConsistency.NamesAnAccuracyDefect(5, "Matches rubric"));
    }

    [Fact]
    public void NamesAnAccuracyDefect_NullEvidence_DoesNotFlag()
    {
        Assert.False(BenchmarkVerdictConsistency.NamesAnAccuracyDefect(5, null));
    }
}
