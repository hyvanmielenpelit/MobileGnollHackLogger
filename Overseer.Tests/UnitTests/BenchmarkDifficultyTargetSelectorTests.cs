namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkDifficultyTargetSelectorTests
{
    private const long SuiteId = 7;

    // Deliberately out of order, so ordering by OrderIndex is observable.
    private static List<BenchmarkQuestion> SuiteQuestions() => new()
    {
        new() { Id = 13, OrderIndex = 3, QuestionText = "Q3", AssessedDifficulty = null },
        new() { Id = 11, OrderIndex = 1, QuestionText = "Q1", AssessedDifficulty = 40 },
        new() { Id = 12, OrderIndex = 2, QuestionText = "Q2", AssessedDifficulty = null },
        new() { Id = 14, OrderIndex = 4, QuestionText = "Q4", AssessedDifficulty = 70 }
    };

    [Fact]
    public void OnlyUnassessed_ReturnsQuestionsWithoutAssessedDifficulty_InOrder()
    {
        var selection = BenchmarkDifficultyTargetSelector.Select(SuiteId, SuiteQuestions(), null, onlyUnassessed: true);

        Assert.Null(selection.Error);
        Assert.Equal("unassessed", selection.Scope);
        Assert.Equal(new long[] { 12, 13 }, selection.Questions.Select(q => q.Id));
    }

    [Fact]
    public void ExplicitIds_TakePrecedenceOverOnlyUnassessed()
    {
        var selection = BenchmarkDifficultyTargetSelector.Select(SuiteId, SuiteQuestions(), new long[] { 14, 11 }, onlyUnassessed: true);

        Assert.Null(selection.Error);
        Assert.Equal("questions", selection.Scope);
        Assert.Equal(new long[] { 11, 14 }, selection.Questions.Select(q => q.Id));
    }

    [Fact]
    public void NeitherIdsNorOnlyUnassessed_ReturnsEveryQuestion_InOrder()
    {
        var selection = BenchmarkDifficultyTargetSelector.Select(SuiteId, SuiteQuestions(), new List<long>(), onlyUnassessed: false);

        Assert.Null(selection.Error);
        Assert.Equal("suite", selection.Scope);
        Assert.Equal(new long[] { 11, 12, 13, 14 }, selection.Questions.Select(q => q.Id));
    }

    [Fact]
    public void ForeignId_SetsError_AndReturnsNoQuestions()
    {
        var selection = BenchmarkDifficultyTargetSelector.Select(SuiteId, SuiteQuestions(), new long[] { 11, 99 }, onlyUnassessed: false);

        Assert.Equal("Question ID 99 does not belong to suite 7.", selection.Error);
        Assert.Empty(selection.Questions);
    }
}
