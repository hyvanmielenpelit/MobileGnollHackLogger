namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkQuestionAssessmentTests
{
    [Fact]
    public void ApplySnapshot_PopulatesAllElevenFields_UsesDisplayNameWhenPresent()
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

        BenchmarkQuestionAssessment.ApplySnapshot(question, 75, config, now);

        Assert.Equal(75, question.AssessedDifficulty);
        Assert.Equal("Claude 3.5 Sonnet", question.AssessedDifficultyModel);
        Assert.Equal(now, question.AssessedDifficultyAtUtc);
        Assert.Equal(42L, question.AssessedDifficultyModelConfigurationId);
        Assert.Equal("Anthropic", question.AssessedDifficultyProviderUsed);
        Assert.Equal("claude-3-5-sonnet-20241022", question.AssessedDifficultyModelIdUsed);
        Assert.Equal("High", question.AssessedDifficultyThinkingLevelUsed);
        Assert.Equal("Extended", question.AssessedDifficultyReasoningModeUsed);
        Assert.Equal("Detailed", question.AssessedDifficultyReasoningSummaryUsed);
        Assert.Equal("standard_only", question.AssessedDifficultyServiceTierUsed);
        Assert.Equal(8192, question.AssessedDifficultyMaxOutputTokensUsed);

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
            DisplayName = null
        };

        BenchmarkQuestionAssessment.ApplySnapshot(question, 50, config, DateTime.UtcNow);

        Assert.Equal("gpt-4o", question.AssessedDifficultyModel);
    }

    [Fact]
    public void Clear_NullsAllElevenFields()
    {
        var question = new BenchmarkQuestion
        {
            AssessedDifficulty = 60,
            AssessedDifficultyModel = "GPT-4o",
            AssessedDifficultyAtUtc = DateTime.UtcNow,
            AssessedDifficultyModelConfigurationId = 10,
            AssessedDifficultyProviderUsed = "OpenAI",
            AssessedDifficultyModelIdUsed = "gpt-4o",
            AssessedDifficultyThinkingLevelUsed = "Default",
            AssessedDifficultyReasoningModeUsed = "Standard",
            AssessedDifficultyReasoningSummaryUsed = "Auto",
            AssessedDifficultyServiceTierUsed = "Auto",
            AssessedDifficultyMaxOutputTokensUsed = 4096
        };

        BenchmarkQuestionAssessment.Clear(question);

        Assert.Null(question.AssessedDifficulty);
        Assert.Null(question.AssessedDifficultyModel);
        Assert.Null(question.AssessedDifficultyAtUtc);
        Assert.Null(question.AssessedDifficultyModelConfigurationId);
        Assert.Null(question.AssessedDifficultyProviderUsed);
        Assert.Null(question.AssessedDifficultyModelIdUsed);
        Assert.Null(question.AssessedDifficultyThinkingLevelUsed);
        Assert.Null(question.AssessedDifficultyReasoningModeUsed);
        Assert.Null(question.AssessedDifficultyReasoningSummaryUsed);
        Assert.Null(question.AssessedDifficultyServiceTierUsed);
        Assert.Null(question.AssessedDifficultyMaxOutputTokensUsed);
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
