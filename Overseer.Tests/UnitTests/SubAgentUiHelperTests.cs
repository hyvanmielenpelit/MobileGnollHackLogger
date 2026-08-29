using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services.Agents;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class SubAgentUiHelperTests
{
    private readonly SubAgentCatalogService _catalogService;

    public SubAgentUiHelperTests()
    {
        var config = new ConfigurationBuilder().Build();
        _catalogService = new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance);
    }

    [Theory]
    [InlineData("wiki_researcher", "Wiki Researcher")]
    [InlineData("source_investigator", "Source Investigator")]
    [InlineData("game_data_analyst", "Game Data Analyst")]
    [InlineData("custom-agent", "Custom Agent")]
    [InlineData("custom_sub_agent_tester", "Custom Sub Agent Tester")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void TitleCaseIdentifier_FormatsCorrectly(string? input, string expected)
    {
        var result = SubAgentUiHelper.TitleCaseIdentifier(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Wiki Researcher", "wiki researcher")]
    [InlineData("Source Investigator", "source investigator")]
    [InlineData("Game Data Analyst", "game data analyst")]
    [InlineData("NetHack Wiki Researcher", "NetHack wiki researcher")]
    [InlineData("GnollHack Data Specialist", "GnollHack data specialist")]
    [InlineData("AI Game Analyst", "AI game analyst")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void LowercaseTypePhrase_PreservesProperNounsAndLowersCapitalizedWords(string? input, string expected)
    {
        var result = SubAgentUiHelper.LowercaseTypePhrase(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeInstanceName_TrimsAndCollapsesWhitespace()
    {
        var result = SubAgentUiHelper.NormalizeInstanceName("  Multi\r\nline   title\twith spaces  ");
        Assert.Equal("Multi line title with spaces", result);
    }

    [Fact]
    public void NormalizeInstanceName_ClampsTo80Characters()
    {
        var longInput = new string('a', 200);
        var result = SubAgentUiHelper.NormalizeInstanceName(longInput);
        Assert.Equal(80, result.Length);
        Assert.Equal(new string('a', 80), result);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void NormalizeInstanceName_HandlesEmptyAndNull(string? input, string expected)
    {
        var result = SubAgentUiHelper.NormalizeInstanceName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildDisplayName_WithoutInstanceName_UsesCatalogDisplayName()
    {
        var result = SubAgentUiHelper.BuildDisplayName("wiki_researcher", null, _catalogService);
        Assert.Equal("Invoking subagent: Wiki Researcher", result);
    }

    [Fact]
    public void BuildDisplayName_WithInstanceName_BuildsFullLabel()
    {
        var result = SubAgentUiHelper.BuildDisplayName("wiki_researcher", "Rakshasa stats researcher", _catalogService);
        Assert.Equal("Invoking wiki researcher subagent: Rakshasa stats researcher", result);
    }

    [Fact]
    public void BuildDisplayName_WhenInstanceMatchesTypeNameOrIdentifier_SuppressesEcho()
    {
        var result1 = SubAgentUiHelper.BuildDisplayName("wiki_researcher", "Wiki Researcher", _catalogService);
        Assert.Equal("Invoking subagent: Wiki Researcher", result1);

        var result2 = SubAgentUiHelper.BuildDisplayName("wiki_researcher", "wiki_researcher", _catalogService);
        Assert.Equal("Invoking subagent: Wiki Researcher", result2);

        var result3 = SubAgentUiHelper.BuildDisplayName("wiki_researcher", "WIKI_RESEARCHER", _catalogService);
        Assert.Equal("Invoking subagent: Wiki Researcher", result3);
    }

    [Fact]
    public void BuildDisplayName_WithUnknownAgent_FallsBackToTitleCasedIdentifier()
    {
        var result = SubAgentUiHelper.BuildDisplayName("unknown_expert", null, _catalogService);
        Assert.Equal("Invoking subagent: Unknown Expert", result);

        var resultWithInstance = SubAgentUiHelper.BuildDisplayName("unknown_expert", "Dragon armor analyzer", _catalogService);
        Assert.Equal("Invoking unknown expert subagent: Dragon armor analyzer", resultWithInstance);
    }

    [Fact]
    public void BuildDisplayName_WithNullCatalog_DegradesGracefully()
    {
        var result = SubAgentUiHelper.BuildDisplayName("wiki_researcher", "Rakshasa stats researcher", null);
        Assert.Equal("Invoking wiki researcher subagent: Rakshasa stats researcher", result);

        var resultNoInstance = SubAgentUiHelper.BuildDisplayName("wiki_researcher", null, null);
        Assert.Equal("Invoking subagent: Wiki Researcher", resultNoInstance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildDisplayName_WithMissingAgentName_ReturnsGenericSubagent(string? agentName)
    {
        var result = SubAgentUiHelper.BuildDisplayName(agentName, "Some task", _catalogService);
        Assert.Equal("Invoking subagent", result);
    }

    [Fact]
    public void BuildDisplayName_ClampsTo256Characters()
    {
        var longInstance = new string('x', 300);
        var result = SubAgentUiHelper.BuildDisplayName("wiki_researcher", longInstance, _catalogService);
        Assert.True(result.Length <= 256);
    }
}
