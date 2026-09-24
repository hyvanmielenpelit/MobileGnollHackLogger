namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

public class BenchmarkQuestionAssessmentTests
{
    [Fact]
    public void ApplySnapshot_PopulatesTheDifficultySnapshot_UsesDisplayNameWhenPresent()
    {
        var question = new BenchmarkQuestion
        {
            Id = 1,
            QuestionText = "Test question",
            ModifiedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var config = new SystemAiApiConfiguration
        {
            Id = 42,
            Provider = "Anthropic",
            ModelId = "claude-3-5-sonnet-20241022",
            DisplayName = "Claude 3.5 Sonnet",
            ThinkingLevel = "High",
            ReasoningMode = "Extended",
            ReasoningSummary = "Detailed",
            ServiceTier = "standard_only",
            MaxOutputTokens = 8192
        };

        var now = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
        var originalModified = question.ModifiedAtUtc;
        var snapshot = SystemAiConfigurationSnapshotStore.FromConfiguration(config);

        BenchmarkQuestionAssessment.ApplySnapshot(question, 75, config.Id, snapshot, now);

        Assert.Equal(75, question.AssessedDifficulty);
        Assert.Equal(now, question.AssessedDifficultyAtUtc);
        Assert.Equal(42L, question.AssessedDifficultyModelConfigurationId);
        Assert.Same(snapshot, question.AssessedDifficultyModelSnapshot);
        Assert.Equal("Claude 3.5 Sonnet", question.AssessedDifficultyModelSnapshot.Label());
        Assert.Equal("Anthropic", question.AssessedDifficultyModelSnapshot.Provider);
        Assert.Equal("claude-3-5-sonnet-20241022", question.AssessedDifficultyModelSnapshot.ModelId);
        Assert.Equal("High", question.AssessedDifficultyModelSnapshot.ThinkingLevel);
        Assert.Equal("Extended", question.AssessedDifficultyModelSnapshot.ReasoningMode);
        Assert.Equal("Detailed", question.AssessedDifficultyModelSnapshot.ReasoningSummary);
        Assert.Equal("standard_only", question.AssessedDifficultyModelSnapshot.ServiceTier);
        Assert.Equal(8192, question.AssessedDifficultyModelSnapshot.MaxOutputTokens);

        // Does not touch ModifiedAtUtc
        Assert.Equal(originalModified, question.ModifiedAtUtc);
    }

    [Fact]
    public void ApplySnapshot_UsesModelIdAsDisplayNameFallback_WhenDisplayNameIsNull()
    {
        var question = new BenchmarkQuestion { Id = 1 };
        var config = new SystemAiApiConfiguration
        {
            Id = 10,
            Provider = "OpenAI",
            ModelId = "gpt-4o",
            DisplayName = null!
        };
        var snapshot = SystemAiConfigurationSnapshotStore.FromConfiguration(config);

        BenchmarkQuestionAssessment.ApplySnapshot(question, 50, config.Id, snapshot, DateTime.UtcNow);

        Assert.Equal("gpt-4o", question.AssessedDifficultyModelSnapshot.Label());
    }

    [Fact]
    public void Clear_NullsTheDifficultySnapshot()
    {
        var question = new BenchmarkQuestion
        {
            AssessedDifficulty = 60,
            AssessedDifficultyAtUtc = DateTime.UtcNow,
            AssessedDifficultyModelConfigurationId = 10,
            AssessedDifficultyModelSnapshot = BenchmarkModelSnapshots.Model(
                provider: "OpenAI", modelId: "gpt-4o", displayName: "GPT-4o", thinkingLevel: "Default",
                reasoningMode: "Standard", reasoningSummary: "Auto", serviceTier: "Auto", maxOutputTokens: 4096),
            AssessedDifficultyModelSnapshotId = 99
        };

        BenchmarkQuestionAssessment.Clear(question);

        Assert.Null(question.AssessedDifficulty);
        Assert.Null(question.AssessedDifficultyAtUtc);
        Assert.Null(question.AssessedDifficultyModelConfigurationId);
        Assert.Null(question.AssessedDifficultyModelSnapshot);
        Assert.Null(question.AssessedDifficultyModelSnapshotId);
    }

    [Fact]
    public void Clear_BumpsTheItemRevision_BecauseAnEditedQuestionIsADifferentItem()
    {
        // The revision and the difficulty snapshot travel together because they express one
        // fact: what this question asks has changed. Item analysis groups by revision, so
        // without the bump an edited question's statistics would straddle the rewrite.
        var question = new BenchmarkQuestion { ItemRevision = 1, AssessedDifficulty = 40 };

        BenchmarkQuestionAssessment.Clear(question);
        Assert.Equal(2, question.ItemRevision);

        BenchmarkQuestionAssessment.Clear(question);
        Assert.Equal(3, question.ItemRevision);
    }

    [Fact]
    public void Clear_LandsOnRevisionTwoForARowThatPredatesTheColumn()
    {
        // A question whose row was written before ItemRevision existed reads 0 until the
        // migration's backfill lands. Treating that as "revision 1, now 2" keeps every stored
        // answer's null revision distinguishable from a real one.
        var question = new BenchmarkQuestion { ItemRevision = 0 };

        BenchmarkQuestionAssessment.Clear(question);

        Assert.Equal(2, question.ItemRevision);
    }

    [Fact]
    public void IsAssessed_ReflectsPresenceOfAssessedDifficulty()
    {
        var question = new BenchmarkQuestion();
        Assert.False(BenchmarkQuestionAssessment.IsAssessed(question));

        question.AssessedDifficulty = 45;
        Assert.True(BenchmarkQuestionAssessment.IsAssessed(question));

        BenchmarkQuestionAssessment.Clear(question);
        Assert.False(BenchmarkQuestionAssessment.IsAssessed(question));
    }

    [Fact]
    public void SuiteProgressRule_CalculatesCompletionCorrectly()
    {
        // Empty suite
        var emptyQuestions = new List<BenchmarkQuestion>();
        int emptyTotal = emptyQuestions.Count;
        int emptyAssessed = emptyQuestions.Count(q => q.AssessedDifficulty != null);
        bool emptyFullyAssessed = emptyTotal > 0 && emptyAssessed == emptyTotal;

        Assert.Equal(0, emptyAssessed);
        Assert.False(emptyFullyAssessed);

        // Partial suite: 2 of 3 assessed
        var partialQuestions = new List<BenchmarkQuestion>
        {
            new BenchmarkQuestion { Id = 1, AssessedDifficulty = 30 },
            new BenchmarkQuestion { Id = 2, AssessedDifficulty = null },
            new BenchmarkQuestion { Id = 3, AssessedDifficulty = 70 }
        };
        int partialTotal = partialQuestions.Count;
        int partialAssessed = partialQuestions.Count(q => q.AssessedDifficulty != null);
        bool partialFullyAssessed = partialTotal > 0 && partialAssessed == partialTotal;

        Assert.Equal(2, partialAssessed);
        Assert.False(partialFullyAssessed);

        // Complete suite: 3 of 3 assessed
        var completeQuestions = new List<BenchmarkQuestion>
        {
            new BenchmarkQuestion { Id = 1, AssessedDifficulty = 30 },
            new BenchmarkQuestion { Id = 2, AssessedDifficulty = 50 },
            new BenchmarkQuestion { Id = 3, AssessedDifficulty = 70 }
        };
        int completeTotal = completeQuestions.Count;
        int completeAssessed = completeQuestions.Count(q => q.AssessedDifficulty != null);
        bool completeFullyAssessed = completeTotal > 0 && completeAssessed == completeTotal;

        Assert.Equal(3, completeAssessed);
        Assert.True(completeFullyAssessed);
    }
}
