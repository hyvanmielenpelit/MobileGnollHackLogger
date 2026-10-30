using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Overseer.Services.CorpusIndexing;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// wiki_overview over one small GnollHack wiki and one small NetHack wiki, and the clear failure
/// nethack_wiki_search gives a query with no searchable words. Each test builds its own services, so
/// a reindex or an extra file stays within it.
/// </summary>
public sealed class WikiOverviewToolTests : IDisposable
{
    private readonly string _gnollHackDir;
    private readonly string _netHackDir;

    public WikiOverviewToolTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "WikiOverviewToolTests_" + Guid.NewGuid().ToString("N"));
        _gnollHackDir = Path.Combine(root, "gnollhack");
        _netHackDir = Path.Combine(root, "nethack");

        WriteGnollHack("Monsters/Gnoll.md", "A gnoll is a monster.\n");
        WriteGnollHack("Monsters/Griffin.md", "A griffin is a monster.\n");
        WriteGnollHack("Items/Dagger.md", "A dagger is a weapon.\n");
        WriteGnollHack("Home.md", "The front page.\n");
        WriteGnollHack(".agents/SKILL.md", "Not an article.\n");

        Directory.CreateDirectory(_netHackDir);
        File.WriteAllText(Path.Combine(_netHackDir, "Cockatrice_file.md"), "---\ntitle: \"Cockatrice\"\nnamespace: article\n---\nA cockatrice is a monster.\n");
        File.WriteAllText(Path.Combine(_netHackDir, "Elbereth_file.md"), "---\ntitle: \"Elbereth\"\nnamespace: article\n---\nElbereth is an engraving.\n");
        File.WriteAllText(Path.Combine(_netHackDir, "Source_file.md"), "---\ntitle: \"Source:zap.c\"\nnamespace: source\n---\nCode.\n");
    }

    public void Dispose()
    {
        string root = Path.GetDirectoryName(_gnollHackDir)!;
        if (Directory.Exists(root))
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch { }
        }
    }

    private void WriteGnollHack(string relativePath, string content)
    {
        string fullPath = Path.Combine(_gnollHackDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static IConfiguration Config(string key, string path) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new[] { new KeyValuePair<string, string?>(key, path) })
            .Build();

    private async Task<(WikiService GnollHack, NetHackWikiService NetHack)> StartServicesAsync(string? gnollHackPath = null, string? netHackPath = null)
    {
        var gnollHack = new WikiService(Config("WikiPath", gnollHackPath ?? _gnollHackDir));
        var netHack = new NetHackWikiService(Config("NetHackWikiPath", netHackPath ?? _netHackDir));
        await gnollHack.InitializationTask;
        await netHack.InitializationTask;
        return (gnollHack, netHack);
    }

    private async Task<ToolResult> RunAsync(object arguments, bool spoilerFree = false)
    {
        var (gnollHack, netHack) = await StartServicesAsync();
        using (gnollHack)
        using (netHack)
        {
            return await RunAsync(new WikiOverviewTool(gnollHack, netHack), arguments, spoilerFree);
        }
    }

    private static Task<ToolResult> RunAsync(WikiOverviewTool tool, object arguments, bool spoilerFree = false)
    {
        var parameters = JsonDocument.Parse(JsonSerializer.Serialize(arguments)).RootElement;
        var context = new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1),
            SpoilerFreeMode = spoilerFree
        };
        return tool.ExecuteAsync(parameters, context, CancellationToken.None);
    }

    // ----- counts -----

    [Fact]
    public async Task NoArguments_ReportsTheGnollHackCountDirectoriesAndBuildTime()
    {
        var result = await RunAsync(new { });

        Assert.True(result.Success);
        Assert.StartsWith("GnollHack wiki: 4 articles in Overseer's index, built ", result.Content);
        Assert.Contains(" UTC. The live site can differ slightly.", result.Content);
        Assert.Contains("Monsters: 2", result.Content);
        Assert.Contains("Items: 1", result.Content);
        Assert.Contains("(root): 1", result.Content);
        Assert.DoesNotContain("Gnoll.md", result.Content);
    }

    [Fact]
    public async Task Result_NamesNoFilesystemPathOrByteCount()
    {
        var result = await RunAsync(new { list_titles = true });

        Assert.DoesNotContain(_gnollHackDir, result.Content);
        Assert.DoesNotContain("bytes", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SKILL", result.Content);
    }

    [Theory]
    [InlineData("monster", 2)]
    [InlineData("MONSTERS", 2)]
    [InlineData("griffin", 1)]
    [InlineData(".md", 4)]
    public async Task Category_CountsTheArticlesWhosePathContainsIt(string category, int expected)
    {
        var result = await RunAsync(new { category });

        Assert.True(result.Success);
        Assert.Contains($"{expected} article{(expected == 1 ? "" : "s")} with '{category}' in the path.", result.Content);
    }

    [Fact]
    public async Task Category_MatchingNothing_IsASuccessfulMissWithTheCounts()
    {
        var result = await RunAsync(new { category = "zzz", list_titles = true });

        Assert.True(result.Success);
        Assert.StartsWith("No GnollHack wiki article path contains 'zzz'.", result.Content);
        Assert.Contains("GnollHack wiki: 4 articles", result.Content);
    }

    [Fact]
    public async Task Category_Blank_IsIgnored()
    {
        var result = await RunAsync(new { category = "   ", list_titles = true });

        Assert.True(result.Success);
        Assert.Contains("Articles 1-4 of 4.", result.Content);
        Assert.DoesNotContain("in the path", result.Content);
    }

    // ----- listing -----

    [Fact]
    public async Task ListTitles_ListsSortedPaths()
    {
        var result = await RunAsync(new { list_titles = true });

        Assert.Contains("Articles 1-4 of 4.\nHome.md\nItems/Dagger.md\nMonsters/Gnoll.md\nMonsters/Griffin.md", result.Content);
        Assert.DoesNotContain("offset", result.Content);
    }

    [Fact]
    public async Task ListTitles_PagesWithOffsetAndLimit()
    {
        var result = await RunAsync(new { list_titles = true, offset = 1, limit = 2 });

        Assert.Contains("Articles 2-3 of 4. Call again with offset 3 for more.\nItems/Dagger.md\nMonsters/Gnoll.md", result.Content);
        Assert.DoesNotContain("Home.md", result.Content);
        Assert.DoesNotContain("Griffin", result.Content);
    }

    [Fact]
    public async Task ListTitles_WithCategory_PagesTheMatches()
    {
        var result = await RunAsync(new { list_titles = true, category = "monsters", limit = 1 });

        Assert.Contains("Articles 1-1 of 2. Call again with offset 1 for more.\nMonsters/Gnoll.md", result.Content);
    }

    [Fact]
    public async Task ListTitles_OffsetPastTheEnd_SaysSoAndSucceeds()
    {
        var result = await RunAsync(new { list_titles = true, offset = 99 });

        Assert.True(result.Success);
        Assert.Contains("Nothing to list at offset 99: 4 articles in all.", result.Content);
    }

    [Fact]
    public async Task ListTitles_LimitIsClampedTo200()
    {
        for (int i = 0; i < 200; i++)
        {
            WriteGnollHack($"Bulk/Article{i:D3}.md", "Filler.\n");
        }

        var result = await RunAsync(new { list_titles = true, limit = 1000 });

        Assert.Contains("Articles 1-200 of 204. Call again with offset 200 for more.", result.Content);
    }

    [Fact]
    public async Task ListTitles_SeesANewArticleAfterAReindex()
    {
        var (gnollHack, netHack) = await StartServicesAsync();
        using (gnollHack)
        using (netHack)
        {
            var tool = new WikiOverviewTool(gnollHack, netHack);
            WriteGnollHack("Items/Torch.md", "A torch.\n");

            Assert.Equal(CorpusIndexStartResult.Started, gnollHack.RequestReindex(CorpusIndexTrigger.Manual, out var completion));
            await completion!;

            var result = await RunAsync(tool, new { list_titles = true });
            Assert.StartsWith("GnollHack wiki: 5 articles", result.Content);
            Assert.Contains("Items/Torch.md", result.Content);
        }
    }

    // ----- spoiler-free mode -----

    [Fact]
    public async Task SpoilerFreeMode_RemindsOnlyWhenTitlesAreListed()
    {
        var listed = await RunAsync(new { list_titles = true }, spoilerFree: true);
        var counted = await RunAsync(new { }, spoilerFree: true);
        var netHackListed = await RunAsync(new { wiki = "nethack", list_titles = true }, spoilerFree: true);

        Assert.Contains("[SPOILER-FREE MODE ACTIVE:", listed.Content);
        Assert.DoesNotContain("SPOILER-FREE", counted.Content);
        Assert.Contains("[SPOILER-FREE MODE ACTIVE:", netHackListed.Content);
    }

    // ----- NetHack wiki -----

    [Fact]
    public async Task NetHack_ReportsTheCountAndNamespaces()
    {
        var result = await RunAsync(new { wiki = "nethack" });

        Assert.True(result.Success);
        Assert.StartsWith("NetHack wiki: 3 articles in Overseer's index, built ", result.Content);
        Assert.Contains("article: 2; source: 1", result.Content);
    }

    [Fact]
    public async Task NetHack_ListsSortedTitles_NotFileNames()
    {
        var result = await RunAsync(new { wiki = "NetHack", list_titles = true });

        Assert.Contains("Articles 1-3 of 3.\nCockatrice\nElbereth\nSource:zap.c", result.Content);
        Assert.DoesNotContain("_file", result.Content);
    }

    [Fact]
    public async Task NetHack_Category_IsRejected_ButABlankOneIsIgnored()
    {
        var rejected = await RunAsync(new { wiki = "nethack", category = "monster" });
        var blank = await RunAsync(new { wiki = "nethack", category = " " });

        Assert.False(rejected.Success);
        Assert.Contains("category applies to the GnollHack wiki only", rejected.ErrorMessage);
        Assert.True(blank.Success);
    }

    // ----- failures -----

    [Fact]
    public async Task UnknownWiki_IsInvalidInput()
    {
        var result = await RunAsync(new { wiki = "nethackwiki" });

        Assert.False(result.Success);
        Assert.Contains("'gnollhack' or 'nethack'", result.ErrorMessage);
    }

    [Fact]
    public async Task NetHack_WhileIndexing_ReturnsItsOwnGuardMessage()
    {
        var gnollHack = new WikiService(Config("WikiPath", _gnollHackDir));
        var netHack = new NetHackWikiService(Config("NetHackWikiPath", _netHackDir), null, null, startIndexing: false);
        using (gnollHack)
        using (netHack)
        {
            await gnollHack.InitializationTask;
            var result = await RunAsync(new WikiOverviewTool(gnollHack, netHack), new { wiki = "nethack" });

            Assert.False(result.Success);
            Assert.Equal(ToolGuardMessages.NetHackWikiIndexingInProgress, result.ErrorMessage);
        }
    }

    [Theory]
    [InlineData("gnollhack")]
    [InlineData("nethack")]
    public async Task FailedStartup_ReportsTheIndexUnavailable(string wiki)
    {
        string missing = Path.Combine(Path.GetDirectoryName(_gnollHackDir)!, "missing");
        var (gnollHack, netHack) = await StartServicesAsync(missing, missing);
        using (gnollHack)
        using (netHack)
        {
            var result = await RunAsync(new WikiOverviewTool(gnollHack, netHack), new { wiki });

            Assert.False(result.Success);
            Assert.Equal("The wiki index is not available.", result.ErrorMessage);
        }
    }

    // ----- searchable queries -----

    [Theory]
    [InlineData("*", false)]
    [InlineData("the", false)]
    [InlineData("the of and", false)]
    [InlineData("***?", false)]
    [InlineData("the gnoll", true)]
    [InlineData("nosuchwordanywhere", true)]
    public async Task IsSearchableQuery_IsFalseWhenTheAnalyzerLeavesNoTerm(string query, bool expected)
    {
        var (gnollHack, netHack) = await StartServicesAsync();
        using (gnollHack)
        using (netHack)
        {
            Assert.Equal(expected, gnollHack.IsSearchableQuery(query));
            Assert.Equal(expected, netHack.IsSearchableQuery(query));
        }
    }

    [Theory]
    [InlineData("*")]
    [InlineData("the")]
    public async Task NetHackWikiSearch_UnsearchableQuery_FailsAndPointsAtTheOverview(string query)
    {
        var (gnollHack, netHack) = await StartServicesAsync();
        using (gnollHack)
        using (netHack)
        {
            var tool = new NetHackWikiSearchTool(netHack, Config("NetHackWikiPath", _netHackDir));
            var parameters = JsonDocument.Parse(JsonSerializer.Serialize(new { query })).RootElement;
            var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1) };

            var result = await tool.ExecuteAsync(parameters, context, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(
                $"'{query}' contains no searchable words. nethack_wiki_search matches whole words, has no wildcards and ignores very common words such as \"the\". To count or list articles, use wiki_overview.",
                result.ErrorMessage);
        }
    }

    [Fact]
    public async Task NetHackWikiSearch_EmptyQuery_PointsAtTheOverview()
    {
        var (gnollHack, netHack) = await StartServicesAsync();
        using (gnollHack)
        using (netHack)
        {
            var tool = new NetHackWikiSearchTool(netHack, Config("NetHackWikiPath", _netHackDir));
            var parameters = JsonDocument.Parse("{}").RootElement;
            var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1) };

            var result = await tool.ExecuteAsync(parameters, context, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("Missing query parameter", result.ErrorMessage);
            Assert.Contains("wiki_overview", result.ErrorMessage);
        }
    }
}
