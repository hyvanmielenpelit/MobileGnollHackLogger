namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The information conditions: their wire names, the candidate tool allowlist each resolves to, and
/// the allowlist a run with an instruction variant adds experimental tools to.
/// </summary>
public class BenchmarkInformationConditionTests
{
    private static readonly IReadOnlyList<string> Configured = new[]
    {
        "wiki_search",
        "source_code_search",
        "wiki_view",
        "get_item_stats",
        "get_knowledge_article",
        "nethack_wiki_search",
        "source_code_view",
        "nethack_wiki_view",
        "monster_lookup",
        "item_lookup",
        "get_monster_stats"
    };

    [Theory]
    [InlineData(null, BenchmarkInformationCondition.Production)]
    [InlineData("", BenchmarkInformationCondition.Production)]
    [InlineData("  ", BenchmarkInformationCondition.Production)]
    [InlineData("production", BenchmarkInformationCondition.Production)]
    [InlineData("sourceOnly", BenchmarkInformationCondition.SourceOnly)]
    [InlineData(" closedBook ", BenchmarkInformationCondition.ClosedBook)]
    public void TryParse_ReadsEveryWireName_AndNullAsProduction(string? wire, BenchmarkInformationCondition expected)
    {
        Assert.True(BenchmarkInformationConditions.TryParse(wire, out var condition));
        Assert.Equal(expected, condition);
    }

    [Theory]
    [InlineData("openBook")]
    [InlineData("SourceOnly")]
    [InlineData("closed-book")]
    public void TryParse_AnUnknownValue_IsFalse(string wire)
    {
        Assert.False(BenchmarkInformationConditions.TryParse(wire, out var condition));
        Assert.Equal(BenchmarkInformationCondition.Production, condition);
        Assert.Equal(BenchmarkInformationCondition.Production, BenchmarkInformationConditions.FromWire(wire));
    }

    [Theory]
    [InlineData(BenchmarkInformationCondition.Production, "production", null)]
    [InlineData(BenchmarkInformationCondition.SourceOnly, "sourceOnly", "sourceOnly")]
    [InlineData(BenchmarkInformationCondition.ClosedBook, "closedBook", "closedBook")]
    public void ToWire_RoundTrips_AndProductionIsStoredAsNull(
        BenchmarkInformationCondition condition, string wire, string? stored)
    {
        Assert.Equal(wire, condition.ToWire());
        Assert.Equal(stored, condition.ToStoredWire());
        Assert.Equal(condition, BenchmarkInformationConditions.FromWire(wire));
        Assert.Equal(condition, BenchmarkInformationConditions.FromWire(stored));
        Assert.False(string.IsNullOrWhiteSpace(condition.DisplayName()));
    }

    [Fact]
    public void ResolveAllowedTools_Production_IsTheConfiguredListUnchanged()
    {
        Assert.Equal(Configured, BenchmarkInformationCondition.Production.ResolveAllowedTools(Configured));
    }

    [Fact]
    public void ResolveAllowedTools_SourceOnly_DropsEveryExcludedTool_AndKeepsTheOrder()
    {
        var tools = BenchmarkInformationCondition.SourceOnly.ResolveAllowedTools(Configured);

        Assert.Equal(new[] { "source_code_search", "get_item_stats", "source_code_view", "get_monster_stats" }, tools);
        foreach (string excluded in BenchmarkInformationConditions.SourceOnlyExcludedTools)
        {
            Assert.DoesNotContain(excluded, tools);
        }
    }

    [Fact]
    public void ResolveAllowedTools_SourceOnly_MatchesExcludedNamesIgnoringCase()
    {
        var tools = BenchmarkInformationCondition.SourceOnly.ResolveAllowedTools(new[] { "Wiki_Search", "source_code_search" });

        Assert.Equal(new[] { "source_code_search" }, tools);
    }

    [Fact]
    public void ResolveAllowedTools_ClosedBook_IsEmpty()
    {
        Assert.Empty(BenchmarkInformationCondition.ClosedBook.ResolveAllowedTools(Configured));
    }

    [Fact]
    public void CandidateAllowedTools_AddsTheVariantsExperimentalTools_ExceptInClosedBook()
    {
        var variant = new BenchmarkVariantContext
        {
            Name = "probe",
            Hash = new string('a', 64),
            ExperimentalTools = new[] { "get_character_info", "source_code_search" }
        };

        var production = BenchmarkService.CandidateAllowedTools(BenchmarkInformationCondition.Production, Configured, variant);
        Assert.Equal(Configured.Count + 1, production.Count);
        Assert.Equal("get_character_info", production[^1]);

        var sourceOnly = BenchmarkService.CandidateAllowedTools(BenchmarkInformationCondition.SourceOnly, Configured, variant);
        Assert.Equal(new[] { "source_code_search", "get_item_stats", "source_code_view", "get_monster_stats", "get_character_info" }, sourceOnly);

        Assert.Empty(BenchmarkService.CandidateAllowedTools(BenchmarkInformationCondition.ClosedBook, Configured, variant));
        Assert.Equal(Configured, BenchmarkService.CandidateAllowedTools(BenchmarkInformationCondition.Production, Configured, null));
    }

    [Theory]
    [InlineData(BenchmarkInformationCondition.Production, true, null)]
    [InlineData(BenchmarkInformationCondition.SourceOnly, true, "sourceOnly")]
    [InlineData(BenchmarkInformationCondition.ClosedBook, false, "closedBook")]
    public void CandidatePromptOptionsFor_RecordsTheCondition_AndClosedBookDisablesTools(
        BenchmarkInformationCondition condition, bool enableToolUse, string? stored)
    {
        var options = BenchmarkService.CandidatePromptOptionsFor(false, true, false, condition);

        Assert.Equal(enableToolUse, options.EnableToolUse);
        Assert.Equal(stored, options.InformationCondition);
        Assert.Equal(condition, options.Condition);
        Assert.Equal(condition, BenchmarkCandidatePromptOptions.FromJson(options.ToCanonicalJson()).Condition);
    }
}
