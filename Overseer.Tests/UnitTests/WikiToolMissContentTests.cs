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

/// <summary>
/// Covers how wiki_view resolves an article request: a title two articles share, the
/// repository-relative path form that separates them, a filename carrying its extension, and the
/// dot-directories the indexer keeps out of the corpus. Its corpus is deliberately its own —
/// WikiToolMissContentTests depends on a bare title resolving straight to an article, which two
/// same-titled articles would turn into a disambiguation.
/// </summary>
public class WikiArticleResolutionTests : IDisposable
{
    private readonly string _tempDir;

    // The same bound the miss payloads are held to: a disambiguation list is re-sent to the model
    // on each subsequent round of the same question, so it has to stay a single short line.
    private const int PayloadMaxChars = 600;

    public WikiArticleResolutionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WikiArticleResolutionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        Write("Monsters/Gnoll.md",
@"A gnoll is a hyena-headed humanoid and one of the most common early monsters.

## Behaviour
Gnolls travel in packs, which is the monsterpackbehaviour that makes them dangerous early.
");

        Write("Races/Gnoll.md",
@"Gnolls are a playable race in GnollHack, starting with an unusually strong smell sense.

## Racial abilities
A gnoll character gains racialinfravision and keeps it for the whole game.
");

        Write("Runewords.md",
@"Runewords are combinations of runes that grant a property when engraved together.

## Engraving
A runewordengraving is consumed once the word takes effect.
");

        Write("Guides/Sokoban.md",
@"Sokoban is a branch whose levels are solved by pushing boulders onto holes.

## Solving
Every sokobanboulderpuzzle has exactly one solution that does not waste a boulder.
");

        // Agent and planning files, which the wiki repository also holds and the indexer skips.
        Write(".agents/skills/x/SKILL.md", "This file carries zzexcludedagenttoken and is not a wiki article.\n");
        Write(".plans/a/b.md", "This file carries zzexcludedplantoken and is not a wiki article.\n");
    }

    private void Write(string relativePath, string content)
    {
        string fullPath = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
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

    private IConfiguration BuildConfig()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new List<KeyValuePair<string, string?>> { new("WikiPath", _tempDir) })
            .Build();
    }

    private static ToolExecutionContext Context() => new()
    {
        SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1),
        SpoilerFreeMode = false
    };

    private async Task<string?> ViewAsync(string article)
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiViewTool(service);

        var jsonParams = JsonDocument.Parse(JsonSerializer.Serialize(new { article })).RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        return result.Content;
    }

    private async Task<string?> SearchAsync(string query)
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiSearchTool(service, BuildConfig());

        var jsonParams = JsonDocument.Parse(JsonSerializer.Serialize(new { query })).RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        return result.Content;
    }

    [Fact]
    public async Task WikiViewTool_CollidingTitle_ReturnsBothPathsAsOneShortLine()
    {
        string? content = await ViewAsync("Gnoll");

        Assert.NotNull(content);
        Assert.Contains("Several wiki articles are titled 'Gnoll'", content);
        Assert.Contains("Monsters/Gnoll", content);
        Assert.Contains("Races/Gnoll", content);
        Assert.Contains("Call wiki_view with the path form", content);

        // Neither article's body: a collision hands back a choice, not a guess.
        Assert.DoesNotContain("monsterpackbehaviour", content);
        Assert.DoesNotContain("racialinfravision", content);

        Assert.DoesNotContain("\n", content);
        Assert.DoesNotContain("\r", content);
        Assert.True(content!.Length < PayloadMaxChars, $"Disambiguation payload was {content.Length} characters.");
    }

    [Fact]
    public async Task WikiViewTool_PathForm_ReturnsThatArticleUnderItsRelativePathHeader()
    {
        string? content = await ViewAsync("Races/Gnoll");

        Assert.Contains("--- Races/Gnoll.md ---", content);
        Assert.Contains("racialinfravision", content);
        Assert.DoesNotContain("monsterpackbehaviour", content);
    }

    [Fact]
    public async Task WikiViewTool_PathFormWithExtension_ResolvesToTheSameArticle()
    {
        string? content = await ViewAsync("Races/Gnoll.md");

        Assert.Contains("--- Races/Gnoll.md ---", content);
        Assert.Contains("racialinfravision", content);
        Assert.DoesNotContain("monsterpackbehaviour", content);
    }

    [Fact]
    public async Task WikiViewTool_UniqueTitle_ResolvesDirectlyWithARelativePathHeader()
    {
        string? content = await ViewAsync("Sokoban");

        Assert.Contains("--- Guides/Sokoban.md ---", content);
        Assert.Contains("sokobanboulderpuzzle", content);
        Assert.DoesNotContain("Several wiki articles are titled", content);
    }

    /// <summary>
    /// Benchmark run 30 called wiki_view with the filename it had read out of a wiki_search
    /// header, `Runewords.md`, and got a miss while the bare title returned the article. Both
    /// spellings now reach the same article.
    /// </summary>
    [Theory]
    [InlineData("Runewords")]
    [InlineData("Runewords.md")]
    public async Task WikiViewTool_RootArticle_ResolvesWithAndWithoutItsExtension(string article)
    {
        string? content = await ViewAsync(article);

        Assert.Contains("--- Runewords.md ---", content);
        Assert.Contains("runewordengraving", content);
        Assert.DoesNotContain("No wiki article matched", content);
    }

    [Fact]
    public async Task WikiViewTool_NothingResembles_StillReturnsAMissPayloadWithANextAction()
    {
        string? content = await ViewAsync("Zzqqxx Nonexistent Article 4711");

        Assert.Contains("No wiki article matched '", content);
        Assert.Contains("wiki_search", content);
        Assert.Contains("nethack_wiki_view", content);
        Assert.True(content!.Length < PayloadMaxChars, $"Miss payload was {content.Length} characters.");
    }

    [Fact]
    public async Task WikiSearch_DotDirectoryFiles_AreNotIndexed()
    {
        Assert.Contains("No GnollHack wiki article matched", await SearchAsync("zzexcludedagenttoken"));
        Assert.Contains("No GnollHack wiki article matched", await SearchAsync("zzexcludedplantoken"));
    }

    [Fact]
    public async Task WikiView_DotDirectoryFiles_AreNotReachableByNameOrPath()
    {
        string? byName = await ViewAsync("SKILL");
        Assert.DoesNotContain("zzexcludedagenttoken", byName);
        Assert.DoesNotContain("SKILL.md", byName);

        string? byPath = await ViewAsync(".agents/skills/x/SKILL.md");
        Assert.DoesNotContain("zzexcludedagenttoken", byPath);

        string? planByPath = await ViewAsync(".plans/a/b.md");
        Assert.DoesNotContain("zzexcludedplantoken", planByPath);
    }

    [Fact]
    public async Task WikiSearch_SnippetHeader_CarriesTheRepositoryRelativePath()
    {
        string? content = await SearchAsync("gnoll");

        Assert.Contains("--- Monsters/Gnoll.md ---", content);
        Assert.Contains("--- Races/Gnoll.md ---", content);
    }
}
