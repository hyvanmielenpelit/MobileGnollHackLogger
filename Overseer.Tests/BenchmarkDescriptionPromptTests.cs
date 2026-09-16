namespace Overseer.Tests;

using System.Collections.Generic;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// Covers <see cref="BenchmarkDescriptionPrompt.BuildPrompt"/> and
/// <see cref="BenchmarkDescriptionPrompt.UnwrapMarkdown"/>.
/// </summary>
public class BenchmarkDescriptionPromptTests
{
    private static BenchmarkGameSnapshot SampleSnapshot() => new()
    {
        Name = "Test Snapshot",
        SanitizedText = "HP 12/60. A mind flayer is adjacent to the east.",
        Sha256 = "deadbeef",
        CaptureMethod = "Manual"
    };

    private static List<BenchmarkQuestion> SampleQuestions() => new()
    {
        new BenchmarkQuestion
        {
            OrderIndex = 3,
            Difficulty = BenchmarkDifficulty.Advanced,
            QuestionText = "Third question about runewords?",
            ExpectedPoints = "**REQUIRED**\n- SECRET RUBRIC THREE"
        },
        new BenchmarkQuestion
        {
            OrderIndex = 1,
            Difficulty = BenchmarkDifficulty.Simple,
            QuestionText = "First question about Gnolls?",
            ExpectedPoints = "**REQUIRED**\n- SECRET RUBRIC ONE"
        },
        new BenchmarkQuestion
        {
            OrderIndex = 2,
            Difficulty = BenchmarkDifficulty.Intermediate,
            QuestionText = "Second question about prayer?",
            ExpectedPoints = "**REQUIRED**\n- SECRET RUBRIC TWO"
        }
    };

    [Fact]
    public void BuildPrompt_WithSnapshot_ContainsTheBoardFence()
    {
        string prompt = BenchmarkDescriptionPrompt.BuildPrompt(
            "Suite", SampleQuestions(), SampleSnapshot(), "Operator instructions");

        Assert.Contains("--- BEGIN GAME CONTEXT BOARD (UNTRUSTED REFERENCE DATA) ---", prompt);
        Assert.Contains("HP 12/60. A mind flayer is adjacent to the east.", prompt);
        Assert.Contains("--- END GAME CONTEXT BOARD ---", prompt);
    }

    [Fact]
    public void BuildPrompt_WithoutSnapshot_OmitsTheBoardFence()
    {
        string prompt = BenchmarkDescriptionPrompt.BuildPrompt(
            "Suite", SampleQuestions(), null, "Operator instructions");

        Assert.DoesNotContain("GAME CONTEXT BOARD", prompt);
        Assert.Contains("UNTRUSTED REFERENCE DATA", prompt);
    }

    [Fact]
    public void BuildPrompt_ListsQuestionsInOrderIndexOrder_WithTheirBandNames()
    {
        string prompt = BenchmarkDescriptionPrompt.BuildPrompt(
            "Suite", SampleQuestions(), null, "Operator instructions");

        int first = prompt.IndexOf("1. [Simple] First question about Gnolls?", System.StringComparison.Ordinal);
        int second = prompt.IndexOf("2. [Intermediate] Second question about prayer?", System.StringComparison.Ordinal);
        int third = prompt.IndexOf("3. [Advanced] Third question about runewords?", System.StringComparison.Ordinal);

        Assert.True(first >= 0);
        Assert.True(second > first);
        Assert.True(third > second);
        Assert.Contains("Difficulty spread: Simple 1, Intermediate 1, Advanced 1 (3 questions total)", prompt);
    }

    [Fact]
    public void BuildPrompt_DoesNotIncludeRubricText()
    {
        string prompt = BenchmarkDescriptionPrompt.BuildPrompt(
            "Suite", SampleQuestions(), SampleSnapshot(), "Operator instructions");

        Assert.DoesNotContain("SECRET RUBRIC", prompt);
        Assert.DoesNotContain("**REQUIRED**", prompt);
    }

    [Fact]
    public void BuildPrompt_ContainsTheOperatorInstructionsVerbatim()
    {
        const string instructions = "Keep it under 150 words and mention **runewords** explicitly.";

        string prompt = BenchmarkDescriptionPrompt.BuildPrompt(
            "Suite", SampleQuestions(), null, instructions);

        Assert.Contains("--- BEGIN OPERATOR INSTRUCTIONS ---", prompt);
        Assert.Contains(instructions, prompt);
        Assert.DoesNotContain(BenchmarkDescriptionPrompt.DefaultInstructions, prompt);
    }

    [Fact]
    public void BuildPrompt_WithBlankInstructions_UsesTheDefaultInstructions()
    {
        string prompt = BenchmarkDescriptionPrompt.BuildPrompt(
            "Suite", SampleQuestions(), null, "   ");

        Assert.Contains(BenchmarkDescriptionPrompt.DefaultInstructions, prompt);
    }

    [Fact]
    public void BuildPrompt_ContainsTheSuiteName()
    {
        string prompt = BenchmarkDescriptionPrompt.BuildPrompt(
            "GnollHack Player Assistance", SampleQuestions(), null, "Operator instructions");

        Assert.Contains("Suite name: GnollHack Player Assistance", prompt);
    }

    [Theory]
    [InlineData("```\nA description.\n```")]
    [InlineData("```markdown\nA description.\n```")]
    [InlineData("  ```md\r\nA description.\r\n```  \n")]
    public void UnwrapMarkdown_StripsASurroundingFence(string raw)
    {
        Assert.Equal("A description.", BenchmarkDescriptionPrompt.UnwrapMarkdown(raw));
    }

    [Fact]
    public void UnwrapMarkdown_LeavesPlainMarkdownAlone()
    {
        const string text = "Lead paragraph.\n\n### Covered Domains\n- **Magic**: spells.";

        Assert.Equal(text, BenchmarkDescriptionPrompt.UnwrapMarkdown("\n  " + text + "  \n"));
    }

    [Fact]
    public void UnwrapMarkdown_LeavesAnInnerCodeBlockAlone()
    {
        const string text = "Lead paragraph.\n\n```\ncode\n```\n\nClosing line.";

        Assert.Equal(text, BenchmarkDescriptionPrompt.UnwrapMarkdown(text));
    }

    [Fact]
    public void UnwrapMarkdown_ReturnsEmptyForBlankInput()
    {
        Assert.Equal(string.Empty, BenchmarkDescriptionPrompt.UnwrapMarkdown("  \n "));
    }
}
