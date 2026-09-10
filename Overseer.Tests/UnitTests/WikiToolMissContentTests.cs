using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Covers the BuildMissContent payloads that WikiSearchTool, WikiViewTool, MonsterLookupTool and
/// ItemLookupTool return in place of a bare "not found" message. A synthetic corpus with one
/// article per category subdirectory (monster/Gnoll.md, item/Dagger.md) lets a category filter
/// be exercised both when it matches and when it excludes every hit.
/// </summary>
public class WikiToolMissContentTests : IDisposable
{
    private readonly string _tempDir;

    // Observed miss payloads run roughly 200-400 characters. 600 leaves headroom for normal
    // variation while still failing on a payload that dumps a full article or a runaway list.
    private const int MissContentMaxChars = 600;

    public WikiToolMissContentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WikiToolMissContentTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var monsterDir = Path.Combine(_tempDir, "monster");
        var itemDir = Path.Combine(_tempDir, "item");
        Directory.CreateDirectory(monsterDir);
        Directory.CreateDirectory(itemDir);

        File.WriteAllText(Path.Combine(monsterDir, "Gnoll.md"),
@"Gnoll is one of the most common early monsters encountered in GnollHack. Gnolls travel in packs and use rusty weapons.

## Strategy
Gnoll packs can overwhelm a low-level character. Retreat to a corridor and fight them one at a time.
");

        File.WriteAllText(Path.Combine(itemDir, "Dagger.md"),
@"A dagger is a basic bladed throwing weapon available from the start of a GnollHack game.

## Enchanting
Daggers can be enchanted at an altar like most other weapons.
");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch { }
        }
    }

    private IConfiguration BuildConfig(params (string Key, string? Value)[] extra)
    {
        var pairs = new List<KeyValuePair<string, string?>>
        {
            new("WikiPath", _tempDir)
        };
        pairs.AddRange(extra.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)));
        return new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
    }

    private static ToolExecutionContext Context() => new()
    {
        SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1),
        SpoilerFreeMode = false
    };

    // ----- wiki_search -----

    [Fact]
    public async Task WikiSearchTool_Hit_ReturnsArticleContent_NotMissPayload()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiSearchTool(service, BuildConfig());

        var jsonParams = JsonDocument.Parse("{\"query\": \"gnoll\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Contains("Gnoll.md", result.Content);
        Assert.Contains("Gnoll packs", result.Content);
        Assert.DoesNotContain("No GnollHack wiki article matched", result.Content);
    }

    [Fact]
    public async Task WikiSearchTool_Miss_NoCategory_NamesQueryAndNextAction()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiSearchTool(service, BuildConfig());

        var jsonParams = JsonDocument.Parse("{\"query\": \"qqzzxx1234\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotEmpty(result.Content);
        Assert.Contains("'qqzzxx1234'", result.Content);
        Assert.Contains("No individual term matched either.", result.Content);
        Assert.Contains("wiki_view", result.Content);
        Assert.Contains("nethack_wiki_search", result.Content);
        Assert.EndsWith("GnollHack inherits.", result.Content!.TrimEnd());
        Assert.True(result.Content.Length < MissContentMaxChars);
    }

    [Fact]
    public async Task WikiSearchTool_Miss_CategoryExcludesAllPaths_ReportsUnfilteredHit()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiSearchTool(service, BuildConfig());

        // "spellbook" appears in no indexed path, so the category-filtered search misses even
        // though the bare query "gnoll" matches monster/Gnoll.md without it.
        var jsonParams = JsonDocument.Parse("{\"query\": \"gnoll\", \"category\": \"spellbook\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Contains("'gnoll'", result.Content);
        Assert.Contains("category='spellbook'", result.Content);
        Assert.Contains("category matches the article's file path as a substring, not a tag", result.Content);
        Assert.Contains("Gnoll.md", result.Content);
        Assert.Contains("retry with no category", result.Content);
        Assert.True(result.Content!.Length < MissContentMaxChars);
    }

    [Fact]
    public async Task WikiSearchTool_Miss_CategoryAndQuery_BothMissEverywhere()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiSearchTool(service, BuildConfig());

        var jsonParams = JsonDocument.Parse("{\"query\": \"zzznonexistentqueryterm999\", \"category\": \"alsobogus\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Contains("category='alsobogus'", result.Content);
        Assert.Contains("the query matches nothing without it either", result.Content);
        Assert.True(result.Content!.Length < MissContentMaxChars);
    }

    [Theory]
    [InlineData("gnoll AND (")]
    [InlineData("*")]
    public async Task WikiSearchTool_Miss_LuceneSpecialCharacterQuery_NeverThrowsOrLeaksError(string query)
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiSearchTool(service, BuildConfig());

        var jsonParams = JsonDocument.Parse(JsonSerializer.Serialize(new { query })).RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Content);
        Assert.DoesNotContain("Error:", result.Content);
    }

    // ----- wiki_view -----

    [Fact]
    public async Task WikiViewTool_Hit_ReturnsArticleContent_NotMissPayload()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiViewTool(service);

        var jsonParams = JsonDocument.Parse("{\"article\": \"Gnoll\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Contains("Gnoll packs", result.Content);
        Assert.DoesNotContain("No wiki article matched", result.Content);
    }

    [Fact]
    public async Task WikiViewTool_Miss_ArticleNotFound_NamesArticleAndNextAction()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiViewTool(service);

        var jsonParams = JsonDocument.Parse("{\"article\": \"NonExistentArticleXYZ\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Contains("'NonExistentArticleXYZ'", result.Content);
        Assert.Contains("wiki_search", result.Content);
        Assert.Contains("nethack_wiki_view", result.Content);
        Assert.True(result.Content!.Length < MissContentMaxChars);
    }

    /// <summary>
    /// A section no heading matches is handled by WikiService, not by the miss path:
    /// ExtractMarkdownSection returns an explanatory "[Section '...' not found in article.
    /// Returning full text.]" line followed by the whole article, so the model is told what
    /// happened and still gets the content. BuildMissContent is never reached, which is why it
    /// carries no section-mismatch branch.
    /// </summary>
    [Fact]
    public async Task WikiViewTool_SectionNotFoundOnExistingArticle_ReturnsTheWholeArticleWithAnExplanatoryLine()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiViewTool(service);

        var jsonParams = JsonDocument.Parse("{\"article\": \"Gnoll\", \"section\": \"TotallyMissingSection\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Contains("TotallyMissingSection", result.Content);
        Assert.Contains("Returning full text", result.Content);
        // The article body, not a miss payload: this path is not a miss at all.
        Assert.DoesNotContain("No wiki article matched", result.Content);
    }

    /// <summary>
    /// A miss with a section requested still reports the *article* as what missed, so the model
    /// does not spend a round re-spelling a section that was never reached.
    /// </summary>
    [Fact]
    public async Task WikiViewTool_Miss_WithSectionRequested_SaysTheArticleMissedNotTheSection()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiViewTool(service);

        string content = tool.BuildMissContent("NonExistentArticleXYZ", "Strategy");

        Assert.Contains("'NonExistentArticleXYZ'", content);
        Assert.Contains("Strategy", content);
        Assert.Contains("never reached", content);
        Assert.True(content.Length < MissContentMaxChars);
    }

    [Theory]
    [InlineData("gnoll AND (")]
    [InlineData("*")]
    public async Task WikiViewTool_Miss_LuceneSpecialCharacterArticle_NeverThrowsOrLeaksError(string article)
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiViewTool(service);

        var jsonParams = JsonDocument.Parse(JsonSerializer.Serialize(new { article })).RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Content);
        Assert.DoesNotContain("Error:", result.Content);
    }

    // ----- monster_lookup -----

    [Fact]
    public async Task MonsterLookupTool_Hit_ReturnsArticleContent_NotMissPayload()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new MonsterLookupTool(service);

        var jsonParams = JsonDocument.Parse("{\"name\": \"gnoll\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Contains("Gnoll", result.Content);
        Assert.DoesNotContain("No GnollHack wiki article matched the monster", result.Content);
    }

    [Fact]
    public async Task MonsterLookupTool_Miss_BothFilteredAndUnfilteredMiss_NamesMonsterAndNextAction()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new MonsterLookupTool(service);

        var jsonParams = JsonDocument.Parse("{\"name\": \"zzznonexistentmonster999\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Contains("'zzznonexistentmonster999'", result.Content);
        Assert.Contains("Both the 'monster' path filter and an unfiltered search of the whole wiki missed", result.Content);
        Assert.Contains("get_monster_stats", result.Content);
        Assert.Contains("wiki_search", result.Content);
        Assert.True(result.Content!.Length < MissContentMaxChars);
    }

    [Theory]
    [InlineData("gnoll AND (")]
    [InlineData("*")]
    public async Task MonsterLookupTool_Miss_LuceneSpecialCharacterName_NeverThrowsOrLeaksError(string name)
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new MonsterLookupTool(service);

        var jsonParams = JsonDocument.Parse(JsonSerializer.Serialize(new { name })).RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Content);
        Assert.DoesNotContain("Error:", result.Content);
    }

    // ----- item_lookup -----

    [Fact]
    public async Task ItemLookupTool_Hit_ReturnsArticleContent_NotMissPayload()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new ItemLookupTool(service);

        var jsonParams = JsonDocument.Parse("{\"name\": \"dagger\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Contains("Dagger", result.Content);
        Assert.DoesNotContain("No GnollHack wiki article matched the item", result.Content);
    }

    [Fact]
    public async Task ItemLookupTool_Miss_BothFilteredAndUnfilteredMiss_NamesItemAndNextAction()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new ItemLookupTool(service);

        var jsonParams = JsonDocument.Parse("{\"name\": \"zzznonexistentitem999\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Contains("'zzznonexistentitem999'", result.Content);
        Assert.Contains("Both the 'item' path filter and an unfiltered search of the whole wiki missed", result.Content);
        Assert.Contains("get_item_stats", result.Content);
        Assert.Contains("wiki_search", result.Content);
        Assert.True(result.Content!.Length < MissContentMaxChars);
    }

    [Theory]
    [InlineData("gnoll AND (")]
    [InlineData("*")]
    public async Task ItemLookupTool_Miss_LuceneSpecialCharacterName_NeverThrowsOrLeaksError(string name)
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new ItemLookupTool(service);

        var jsonParams = JsonDocument.Parse(JsonSerializer.Serialize(new { name })).RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Content);
        Assert.DoesNotContain("Error:", result.Content);
    }
}
