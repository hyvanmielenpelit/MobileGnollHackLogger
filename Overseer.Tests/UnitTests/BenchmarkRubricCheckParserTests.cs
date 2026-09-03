namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkRubricCheckParserTests
{
    [Fact]
    public void Parse_VerifiableQuotes_Passes()
    {
        string rubric = "**REQUIRED** - HP 15/15.\n**BOARD FACTS**\n- HP: 15/15";
        string json = """
        {
          "verdict": "supported",
          "reasoning": "The HP matches board line.",
          "findings": [
            {
              "claim": "- HP: 15/15",
              "verdict": "supported",
              "boardQuote": "HP:15(15)",
              "reasoning": "Line matches status bar."
            }
          ]
        }
        """;

        var result = BenchmarkRubricCheckParser.Parse(json, rubric);
        Assert.True(result.Success);
        Assert.Equal("supported", result.Verdict);
        Assert.Single(result.Findings);
    }

    [Fact]
    public void Parse_HallucinatedQuote_NotStrictlyInRubric_GetsDropped()
    {
        string rubric = "**REQUIRED** - HP 15/15.\n**BOARD FACTS**\n- HP: 15/15";
        string json = """
        {
          "verdict": "contradicted",
          "reasoning": "Player has no wand.",
          "findings": [
            {
              "claim": "Player has a wand of death",
              "verdict": "contradicted",
              "boardQuote": "Inventory: none",
              "reasoning": "No wand in inventory."
            }
          ]
        }
        """;

        var result = BenchmarkRubricCheckParser.Parse(json, rubric);
        Assert.True(result.Success);
        Assert.Empty(result.Findings);
    }
}
