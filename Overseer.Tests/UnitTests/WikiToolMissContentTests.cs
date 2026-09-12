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

        // Titled differently from Gnoll.md and Dagger.md but still matching an exact-title lookup
        // query through content, so a lookup's "Other matches" line has something to name.
        File.WriteAllText(Path.Combine(monsterDir, "Gnoll Whelp.md"),
@"A gnoll whelp is a young gnoll cub found in gnoll lairs alongside adult gnolls.
");

        File.WriteAllText(Path.Combine(itemDir, "Dagger Sheath.md"),
@"A dagger sheath holds a dagger safely at the hip when not in use.
");

        // Six griffin articles sharing a term no other fixture article uses, so a query for it
        // exercises max_results counting the actual returned hits rather than capping on a corpus
        // too small to tell a clamp from a coincidence.
        for (int i = 1; i <= 6; i++)
        {
            File.WriteAllText(Path.Combine(monsterDir, $"Griffin{i}.md"),
                $"Griffin{i} is a griffin, a griffin-type monster article describing griffin behavior.\n");
        }

        // "Object Materials" carries the plural in its title, where EnglishAnalyzer's Porter
        // stemming lets the singular query "material" match it; the title field's 5x boost ranks
        // it above the Spells articles below, which only match through the body.
        File.WriteAllText(Path.Combine(_tempDir, "Object Materials.md"),
@"Objects in GnollHack are made of different materials, which affect their weight, value, and resistance to damage or corrosion.

## Common materials
Materials include wood, iron, mithril, and dragonhide.
");

        // Seven spell articles whose only mention of "material" is the body heading, so the query
        // "material" matches eight articles in total - enough to exercise the tool's hit-count
        // line (three of eight returned) as well as its absence when nothing is truncated.
        var spellsDir = Path.Combine(_tempDir, "Spells");
        Directory.CreateDirectory(spellsDir);
        for (int i = 1; i <= 7; i++)
        {
            File.WriteAllText(Path.Combine(spellsDir, $"Spell{i}.md"),
$@"Spell{i} is a spell available to certain classes in GnollHack.

### Material components
Spell{i} requires no material components to cast.
");
        }
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

    [Fact]
    public async Task WikiSearchTool_MaxResultsAboveConfiguredCeiling_ClampsToTheConfiguredCount()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiSearchTool(service, BuildConfig());

        // Six articles match "griffin"; Tools:wiki_search:MaxResults defaults to 5, so max_results
        // 10 must still come back with no more than 5 "--- ... ---" result headers.
        var jsonParams = JsonDocument.Parse("{\"query\": \"griffin\", \"max_results\": 10}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        var headerMatches = System.Text.RegularExpressions.Regex.Matches(result.Content, @"(?m)^--- .+ ---\r?$");
        Assert.Equal(5, headerMatches.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task WikiSearchTool_MaxResultsZeroOrNegative_ClampsToOne(int maxResults)
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiSearchTool(service, BuildConfig());

        var jsonParams = JsonDocument.Parse($"{{\"query\": \"griffin\", \"max_results\": {maxResults}}}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        var headerMatches = System.Text.RegularExpressions.Regex.Matches(result.Content, @"(?m)^--- .+ ---\r?$");
        Assert.Single(headerMatches);
    }

    [Fact]
    public async Task WikiSearchTool_Query_Material_MatchesInflectedTitleAndRanksItFirst()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiSearchTool(service, BuildConfig());

        var jsonParams = JsonDocument.Parse("{\"query\": \"material\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Object Materials.md", result.Content);

        var firstHeader = System.Text.RegularExpressions.Regex.Match(result.Content!, @"^--- (.+?) ---", System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(firstHeader.Success);
        Assert.Equal("Object Materials.md", firstHeader.Groups[1].Value);
    }

    [Fact]
    public async Task WikiSearchTool_HitsExceedMaxResults_AppendsShowingLine()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiSearchTool(service, BuildConfig());

        // "material" matches all eight of Object Materials.md and Spell1-7.md; max_results 3
        // returns only three of them.
        var jsonParams = JsonDocument.Parse("{\"query\": \"material\", \"max_results\": 3}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("[Showing 3 of 8 matching articles", result.Content);
    }

    [Fact]
    public async Task WikiSearchTool_HitsDoNotExceedMaxResults_NoShowingLine()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiSearchTool(service, BuildConfig(("Tools:wiki_search:MaxResults", "8")));

        // The same eight-article match as above, this time with a ceiling wide enough to return
        // every hit, so nothing was left out and no line is appended.
        var jsonParams = JsonDocument.Parse("{\"query\": \"material\", \"max_results\": 8}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain("Showing", result.Content);
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

    [Fact]
    public async Task MonsterLookupTool_ExactTitle_ReturnsOneHeaderPlusOtherMatchesLine()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new MonsterLookupTool(service);

        var jsonParams = JsonDocument.Parse("{\"name\": \"Gnoll\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        var headerMatches = System.Text.RegularExpressions.Regex.Matches(result.Content, @"(?m)^--- .+ ---\r?$");
        Assert.Single(headerMatches);
        Assert.Contains("[Other matches:", result.Content);
        Assert.Contains("Gnoll Whelp", result.Content);
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

    [Fact]
    public async Task ItemLookupTool_ExactTitle_ReturnsOneHeaderPlusOtherMatchesLine()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new ItemLookupTool(service);

        var jsonParams = JsonDocument.Parse("{\"name\": \"Dagger\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        var headerMatches = System.Text.RegularExpressions.Regex.Matches(result.Content, @"(?m)^--- .+ ---\r?$");
        Assert.Single(headerMatches);
        Assert.Contains("[Other matches:", result.Content);
        Assert.Contains("Dagger Sheath", result.Content);
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

    private async Task<string?> SearchAsync(string query, string? category = null)
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiSearchTool(service, BuildConfig());

        var jsonParams = category == null
            ? JsonDocument.Parse(JsonSerializer.Serialize(new { query })).RootElement
            : JsonDocument.Parse(JsonSerializer.Serialize(new { query, category })).RootElement;
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

    [Fact]
    public async Task WikiSearch_CategoryMonster_MatchesCapitalizedMonstersDirectory()
    {
        // Both Monsters/Gnoll.md and Races/Gnoll.md match the bare query; category "monster"
        // must keep the former and exclude the latter even though the directory is capitalized.
        string? content = await SearchAsync("gnoll", "monster");

        Assert.Contains("--- Monsters/Gnoll.md ---", content);
        Assert.DoesNotContain("--- Races/Gnoll.md ---", content);
    }

    [Fact]
    public async Task WikiSearch_CategoryUppercase_StillMatchesLowercasePathSegment()
    {
        string? content = await SearchAsync("gnoll", "MONSTERS");

        Assert.Contains("--- Monsters/Gnoll.md ---", content);
        Assert.DoesNotContain("--- Races/Gnoll.md ---", content);
    }

    [Fact]
    public async Task WikiSearch_CategoryNamesNoDirectory_ReturnsMissPayload()
    {
        // The corpus has no Spells/ directory, so "spell" excludes every hit rather than
        // narrowing them, even though "sokoban" alone matches Guides/Sokoban.md.
        string? content = await SearchAsync("sokoban", "spell");

        Assert.Contains("No GnollHack wiki article matched", content);
    }
}
