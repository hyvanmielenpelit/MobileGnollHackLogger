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
}
