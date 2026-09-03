namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkGenerationParserTests
{
    [Fact]
    public void Parse_ValidJsonWithBoardFacts_Succeeds()
    {
        string json = """
        ```json
        {
          "questions": [
            {
              "questionText": "What is the player's current health and armor class?",
              "expectedPoints": "**REQUIRED** - HP is 15/15.\n**REQUIRED** - AC is 10.\n**BOARD FACTS**\n- HP: 15/15\n- AC: 10"
            }
          ]
        }
        ```
        """;

        var result = BenchmarkGenerationParser.Parse(json, 1);
        Assert.True(result.Success);
        Assert.Single(result.Questions);
        Assert.Equal("What is the player's current health and armor class?", result.Questions[0].QuestionText);
        Assert.Contains("**BOARD FACTS**", result.Questions[0].ExpectedPoints);
    }

    [Fact]
    public void Parse_MissingBoardFactsInRubric_FailsValidation()
    {
        string json = """
        {
          "questions": [
            {
              "questionText": "What is the player's current health?",
              "expectedPoints": "**REQUIRED** - HP is 15/15."
            }
          ]
        }
        """;

        var result = BenchmarkGenerationParser.Parse(json, 1);
        Assert.False(result.Success);
        Assert.Contains(result.ValidationErrors, e => e.Contains("**BOARD FACTS**"));
    }

    [Fact]
    public void Parse_Shortfall_FailsValidation()
    {
        string json = """
        {
          "questions": [
            {
              "questionText": "Question 1",
              "expectedPoints": "**REQUIRED** - R1.\n**BOARD FACTS**\n- F1"
            }
          ]
        }
        """;

        var result = BenchmarkGenerationParser.Parse(json, 3);
        Assert.False(result.Success);
        Assert.Contains(result.ValidationErrors, e => e.Contains("Shortfall") && e.Contains("3 requested"));
    }

    [Fact]
    public void Parse_Excess_TruncatesToRequestedCount()
    {
        string json = """
        {
          "questions": [
            {
              "questionText": "Question 1",
              "expectedPoints": "**REQUIRED** - R1.\n**BOARD FACTS**\n- F1"
            },
            {
              "questionText": "Question 2",
              "expectedPoints": "**REQUIRED** - R2.\n**BOARD FACTS**\n- F2"
            }
          ]
        }
        """;

        var result = BenchmarkGenerationParser.Parse(json, 1);
        Assert.True(result.Success);
        Assert.Single(result.Questions);
        Assert.Equal("Question 1", result.Questions[0].QuestionText);
    }

    [Fact]
    public void Parse_SalvagesTruncatedJson()
    {
        string json = """
        {
          "questions": [
            {
              "questionText": "Question 1",
              "expectedPoints": "**REQUIRED** - R1.\n**BOARD FACTS**\n- F1"
            },
            {
              "questionText": "Question 2",
              "expectedPoints": "**REQUIRED** - R2.\n**BOARD
        """;

        var result = BenchmarkGenerationParser.Parse(json, 1);
        Assert.True(result.Success);
        Assert.Single(result.Questions);
        Assert.Equal("Question 1", result.Questions[0].QuestionText);
    }
}
