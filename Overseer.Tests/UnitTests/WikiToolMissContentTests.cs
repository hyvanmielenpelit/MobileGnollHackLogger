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

        // Grail of healing and Holy symbol sit under Items/, matching item_lookup's "item"
        // category; The Holy Grail sits under Artifacts/, matching its "artifact" category
        // instead, so together they exercise the tool's [item, artifact] category union.
        var itemsDir = Path.Combine(_tempDir, "Items");
        var artifactsDir = Path.Combine(_tempDir, "Artifacts");
        Directory.CreateDirectory(itemsDir);
        Directory.CreateDirectory(artifactsDir);

        File.WriteAllText(Path.Combine(itemsDir, "Grail of healing.md"),
@"The Grail of healing is a legendary healing item sought by many adventurers.

## Effects
Drinking from the grail restores health.
");

        File.WriteAllText(Path.Combine(artifactsDir, "The Holy Grail.md"),
@"The Holy Grail is a legendary artifact said to grant a wish to whoever finds it.

## History
The holy grail has appeared in many quests throughout history.
");

        File.WriteAllText(Path.Combine(itemsDir, "Holy symbol.md"),
@"A holy symbol is a religious item used for prayer and turning undead.
");

        // No test reads this article. It stays so the index's document count, and with it every
        // term's IDF and the rankings the search tests pin, does not change.
        File.WriteAllText(Path.Combine(itemsDir, "Torch.md"),
@"A torch is a simple light source that burns for a limited number of turns.

## Usage
Wielding a lit torch increases visibility in dark areas.
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
    public async Task WikiSearchTool_CategorisedHit_CarriesNoOutsideCategoryLine()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiSearchTool(service, BuildConfig());

        // Unfiltered, the root article Object Materials.md is the best match for "material";
        // category="Spells" keeps only the spell articles, and the result names nothing outside it.
        var jsonParams = JsonDocument.Parse("{\"query\": \"material\", \"category\": \"Spells\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("--- Spells/Spell", result.Content);
        Assert.DoesNotContain("Object Materials.md", result.Content);
        Assert.DoesNotContain("[Without category", result.Content);
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
        Assert.Contains("No GnollHack wiki article matched 'gnoll'", result.Content);
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
    [InlineData("wiki_search", "query", "gnoll AND (")]
    [InlineData("wiki_search", "query", "*")]
    [InlineData("wiki_view", "article", "gnoll AND (")]
    [InlineData("wiki_view", "article", "*")]
    [InlineData("monster_lookup", "name", "gnoll AND (")]
    [InlineData("monster_lookup", "name", "*")]
    [InlineData("item_lookup", "name", "gnoll AND (")]
    [InlineData("item_lookup", "name", "*")]
    public async Task WikiTools_Miss_LuceneSpecialCharacterInput_NeverThrowsOrLeaksError(string toolName, string parameter, string input)
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        IToolHandler tool = toolName switch
        {
            "wiki_search" => new WikiSearchTool(service, BuildConfig()),
            "wiki_view" => new WikiViewTool(service),
            "monster_lookup" => new MonsterLookupTool(service),
            "item_lookup" => new ItemLookupTool(service),
            _ => throw new ArgumentOutOfRangeException(nameof(toolName), toolName, null)
        };

        var jsonParams = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, string> { [parameter] = input })).RootElement;
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

    [Fact]
    public async Task WikiSearchTool_TwoTermQuery_BodyMatchOnBothOutranksTitleMatchOnOne_CountUnchanged()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiSearchTool(service, BuildConfig());

        // Spell1-7 carry both "material" and "components" in the body; Object Materials.md
        // carries only "material", in its boosted title. The count stays the eight articles that
        // match either word.
        var jsonParams = JsonDocument.Parse("{\"query\": \"material components\", \"max_results\": 3}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        var headers = System.Text.RegularExpressions.Regex.Matches(result.Content!, @"(?m)^--- (.+?) ---\r?$")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.Equal(3, headers.Count);
        Assert.All(headers, h => Assert.StartsWith("Spells/Spell", h));
        Assert.DoesNotContain("--- Object Materials.md ---", result.Content);
        Assert.Contains("[Showing 3 of 8 matching articles", result.Content);
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
    public async Task WikiViewTool_ArtifactWithoutArticle_ResolvesToTheArtifactByTitle()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiViewTool(service);

        var jsonParams = JsonDocument.Parse("{\"article\": \"Holy Grail\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        var firstHeader = System.Text.RegularExpressions.Regex.Match(result.Content!, @"^--- (.+?) ---", System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(firstHeader.Success);
        Assert.Equal("Artifacts/The Holy Grail.md", firstHeader.Groups[1].Value);
        Assert.DoesNotContain("Several wiki articles are titled", result.Content);
    }

    /// <summary>
    /// Over the result cap, the section-miss marker line keeps its heading list, says how much of
    /// the article follows, and points back at a heading; the header, the line, one newline and the
    /// article's opening characters land at exactly the cap, so ToolExecutor never appends its own
    /// "[Truncated: …]" suffix and cuts the article's tail with no pointer back into it. The
    /// under-cap case is <see cref="WikiSectionExtractionTests.WikiView_SectionMatchesNoHeading_MarkerLineListsTheHeadingsAndTheArticleFollows"/>.
    /// </summary>
    [Fact]
    public async Task WikiViewTool_SectionMissOnAnOverCapArticle_EndsAtTheCapWithoutTruncatedSuffix()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiViewTool(service);

        var ctx = Context();
        ctx.MaxResultLength = 300;
        var jsonParams = JsonDocument.Parse("{\"article\": \"Gnoll\", \"section\": \"TotallyMissingSection\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, ctx, CancellationToken.None);

        // The same request under the default cap returns the whole article after the marker line.
        var uncapped = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);
        const string fullTextEnding = " Returning full text.]\n\n";
        string uncappedContent = uncapped.Content!;
        int fullTextEnd = uncappedContent.IndexOf(fullTextEnding, StringComparison.Ordinal);
        Assert.True(fullTextEnd >= 0, uncappedContent);
        string fullArticle = uncappedContent.Substring(fullTextEnd + fullTextEnding.Length);

        Assert.True(result.Success);
        string content = result.Content!;
        Assert.Equal(ctx.MaxResultLength, content.Length);
        Assert.DoesNotContain("[Truncated:", content);
        Assert.DoesNotContain("[Article is", content);
        Assert.DoesNotContain("Returning full text", content);

        string[] lines = content.Split('\n');
        Assert.StartsWith("--- ", lines[0]);
        Assert.EndsWith("Gnoll.md ---", lines[0]);

        string marker = lines[1];
        Assert.StartsWith("[Section 'TotallyMissingSection' not found in article. Headings: Strategy. Showing the first ", marker);
        Assert.EndsWith(" characters; call wiki_view again with one of the headings above as section.]", marker);

        var counts = System.Text.RegularExpressions.Regex.Match(marker, @"Showing the first (\d+) of (\d+) characters");
        Assert.True(counts.Success, marker);
        int shown = int.Parse(counts.Groups[1].Value);
        int articleLength = int.Parse(counts.Groups[2].Value);
        Assert.True(shown > 0 && shown < articleLength, marker);
        // The total the marker names is the whole article's length.
        Assert.Equal(fullArticle.Length, articleLength);

        // Exactly the shown count follows the marker line, from the article's first character.
        string shownText = content.Substring(lines[0].Length + 1 + marker.Length + 1);
        Assert.Equal(shown, shownText.Length);
        Assert.StartsWith("Gnoll is one of the most common early monsters", shownText);
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

    // ----- monster_lookup -----

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

    // ----- item_lookup -----

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
        Assert.Contains("Both the 'item' and 'artifact' path filters and an unfiltered search of the whole wiki missed", result.Content);
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

    [Fact]
    public async Task ItemLookupTool_ArtifactExactTitle_ReturnsTheArtifactArticle()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new ItemLookupTool(service);

        var jsonParams = JsonDocument.Parse("{\"name\": \"The Holy Grail\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        var firstHeader = System.Text.RegularExpressions.Regex.Match(result.Content!, @"^--- (.+?) ---", System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(firstHeader.Success);
        Assert.Equal("Artifacts/The Holy Grail.md", firstHeader.Groups[1].Value);
        Assert.Contains("[Other matches:", result.Content);
    }

    /// <summary>
    /// Benchmark run 123 asked item_lookup for "Orb of Fate" and got the top-five join instead of
    /// the article titled "The Orb of Fate". A request without the leading "The" is an exact-title
    /// hit: that article alone, followed by the other matches line.
    /// </summary>
    [Fact]
    public async Task ItemLookupTool_ArtifactWithoutArticle_ReturnsTheArtifactArticleAlone()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new ItemLookupTool(service);

        var jsonParams = JsonDocument.Parse("{\"name\": \"Holy Grail\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        var firstHeader = System.Text.RegularExpressions.Regex.Match(result.Content!, @"^--- (.+?) ---", System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(firstHeader.Success);
        Assert.Equal("Artifacts/The Holy Grail.md", firstHeader.Groups[1].Value);
        var headerMatches = System.Text.RegularExpressions.Regex.Matches(result.Content, @"(?m)^--- .+ ---\r?$");
        Assert.Single(headerMatches);
        Assert.Contains("[Other matches:", result.Content);
    }

    [Fact]
    public async Task ItemLookupTool_ItemExactTitle_StillReturnsTheItem()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new ItemLookupTool(service);

        var jsonParams = JsonDocument.Parse("{\"name\": \"Grail of healing\"}").RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        var firstHeader = System.Text.RegularExpressions.Regex.Match(result.Content!, @"^--- (.+?) ---", System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(firstHeader.Success);
        // The header carries the repository-relative path, not the bare filename, as wiki_search's
        // snippet header and wiki_view's article header do.
        Assert.Equal("Items/Grail of healing.md", firstHeader.Groups[1].Value);
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

    /// <summary>
    /// A leading "The" is ignored in title equality, so a request for "Foo" equals both
    /// <i>The Foo</i> and <i>Foo</i>, and the caller gets the choice rather than either article.
    /// The pair is written by this test alone, so the shared corpus stays collision-free.
    /// </summary>
    [Fact]
    public async Task WikiViewTool_TitleWithAndWithoutLeadingThe_ReturnsBothPathsAsADisambiguation()
    {
        Write("Artifacts/The Foo.md", "The Foo is an artifact carrying zzthefooartifacttoken.\n");
        Write("Items/Foo.md", "A foo is an item carrying zzplainfooitemtoken.\n");

        string? content = await ViewAsync("Foo");

        Assert.NotNull(content);
        Assert.Contains("Several wiki articles are titled 'Foo'", content);
        Assert.Contains("Artifacts/The Foo", content);
        Assert.Contains("Items/Foo", content);
        Assert.DoesNotContain("zzthefooartifacttoken", content);
        Assert.DoesNotContain("zzplainfooitemtoken", content);
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

        Assert.Contains("No wiki article matched 'Zzqqxx Nonexistent Article 4711'", content);
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
}

/// <summary>
/// Covers the order GetRelevantSnippets returns articles in: those matching more of the query's
/// distinct analyzed terms first, then by the original title-boosted score, with the reported
/// total left as the whole query's match count. Its corpus is its own, so the counts the other
/// classes pin are untouched. GetRelevantContext runs the same parsed query in plain score order
/// and serves as the reference for the queries that keep that order.
/// </summary>
public class WikiSearchCoverageRankingTests : IDisposable
{
    private readonly string _tempDir;

    public WikiSearchCoverageRankingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WikiSearchCoverageRankingTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        // "brass lantern oil refill": three of the four words in Lighting's body, two in Oil
        // Flask's (one of them also its title) and one in Brass's, where it is also the title.
        Write("Guides/Lighting.md", "A lantern burns oil; refill it before it runs dry.\n");
        Write("Items/Oil Flask.md", "A flask of oil for a lantern.\n");
        Write("Items/Brass.md", "Brass is a yellowish alloy.\n");

        // "rune runes rune ward": Runes carries one distinct term, however often it is repeated.
        Write("Runes.md", "Runes are carved symbols. A rune glows faintly when read.\n");
        Write("Guides/Warding.md", "A ward is drawn around a single rune.\n");

        // "anvil forge": Anvil carries one term in both its title and its body.
        Write("Items/Anvil.md", "An anvil is a heavy block of iron.\n");
        Write("Guides/Smithy.md", "The forge stands beside the anvil.\n");

        // "honey smoke hive": a held-out topic.
        Write("Guides/Beekeeping.md", "Calm the hive with smoke before taking honey from it.\n");
        Write("Items/Honey.md", "Honey is sweet and never spoils.\n");
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

    private async Task<WikiService> CreateServiceAsync()
    {
        var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        return service;
    }

    /// <summary>The "--- X ---" header of each result, in order.</summary>
    private static List<string> Headers(IEnumerable<string> results)
    {
        return results
            .Select(r => System.Text.RegularExpressions.Regex.Match(r, @"^--- (.+?) ---"))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    private static string FileName(string header) => header.Substring(header.LastIndexOf('/') + 1);

    /// <summary>The same parsed query's plain score order, as file names.</summary>
    private static List<string> ScoreOrder(WikiService service, string query, string? category = null)
    {
        return Headers(service.GetRelevantContext(query, category, 5)).Select(FileName).ToList();
    }

    private static string Fillers(int count)
    {
        return string.Join(" ", Enumerable.Range(0, count).Select(i => "zq" + (char)('a' + i)));
    }

    [Fact]
    public async Task ThreeOfFourTermsInBody_OutrankATitleMatchOnOneTerm()
    {
        using var service = await CreateServiceAsync();

        var results = service.GetRelevantSnippets("brass lantern oil refill", null, 5, 2500, out int totalHits).ToList();

        Assert.Equal(new[] { "Guides/Lighting.md", "Items/Oil Flask.md", "Items/Brass.md" }, Headers(results));
        Assert.Equal(3, totalHits);

        // Score order alone, with the title boost, does not put Lighting first.
        Assert.NotEqual("Lighting.md", ScoreOrder(service, "brass lantern oil refill")[0]);
    }

    [Fact]
    public async Task TotalHits_StaysTheWholeQueryMatchCount_WhenFewerAreReturned()
    {
        using var service = await CreateServiceAsync();

        var results = service.GetRelevantSnippets("brass lantern oil refill", null, 1, 2500, out int totalHits).ToList();

        Assert.Equal(new[] { "Guides/Lighting.md" }, Headers(results));
        Assert.Equal(3, totalHits);
    }

    [Fact]
    public async Task RepeatedAndStemEquivalentWords_CountOnce()
    {
        using var service = await CreateServiceAsync();

        var results = service.GetRelevantSnippets("rune runes rune ward", null, 5, 2500, out int totalHits).ToList();

        Assert.Equal(new[] { "Guides/Warding.md", "Runes.md" }, Headers(results));
        Assert.Equal(2, totalHits);
    }

    [Fact]
    public async Task TermInTitleAndBody_CountsOnce()
    {
        using var service = await CreateServiceAsync();

        var results = service.GetRelevantSnippets("anvil forge", null, 5, 2500, out int totalHits).ToList();

        Assert.Equal(new[] { "Guides/Smithy.md", "Items/Anvil.md" }, Headers(results));
        Assert.Equal(2, totalHits);
    }

    [Fact]
    public async Task StopWordOnlyQuery_ReturnsNothing()
    {
        using var service = await CreateServiceAsync();

        var results = service.GetRelevantSnippets("the of and", null, 5, 2500, out int totalHits).ToList();

        Assert.Empty(results);
        Assert.Equal(0, totalHits);
    }

    [Fact]
    public async Task OneTermLeftAfterStopWords_KeepsScoreOrder()
    {
        using var service = await CreateServiceAsync();

        var results = service.GetRelevantSnippets("the lantern", null, 5, 2500, out _).ToList();

        Assert.Equal(ScoreOrder(service, "the lantern"), Headers(results).Select(FileName).ToList());
    }

    [Fact]
    public async Task SixteenDistinctTerms_AreRankedByCoverage()
    {
        using var service = await CreateServiceAsync();

        // Four matching terms and twelve that match nothing.
        string query = "brass lantern oil refill " + Fillers(12);
        var results = service.GetRelevantSnippets(query, null, 5, 2500, out int totalHits).ToList();

        Assert.Equal(new[] { "Guides/Lighting.md", "Items/Oil Flask.md", "Items/Brass.md" }, Headers(results));
        Assert.Equal(3, totalHits);
    }

    [Fact]
    public async Task SeventeenDistinctTerms_KeepScoreOrder()
    {
        using var service = await CreateServiceAsync();

        string query = "brass lantern oil refill " + Fillers(13);
        var results = service.GetRelevantSnippets(query, null, 5, 2500, out int totalHits).ToList();
        var scoreOrder = ScoreOrder(service, query);

        Assert.Equal(scoreOrder, Headers(results).Select(FileName).ToList());
        Assert.NotEqual("Lighting.md", scoreOrder[0]);
        Assert.Equal(3, totalHits);
    }

    [Fact]
    public async Task UppercaseNot_KeepsScoreOrder()
    {
        using var service = await CreateServiceAsync();

        string query = "brass lantern oil refill NOT soot";
        var results = service.GetRelevantSnippets(query, null, 5, 2500, out _).ToList();
        var scoreOrder = ScoreOrder(service, query);

        Assert.Equal(scoreOrder, Headers(results).Select(FileName).ToList());
        Assert.NotEqual("Lighting.md", scoreOrder[0]);
    }

    [Fact]
    public async Task Category_AppliesInEveryTier()
    {
        using var service = await CreateServiceAsync();

        // Lighting covers the most terms but sits outside Items/.
        var results = service.GetRelevantSnippets("brass lantern oil refill", "items", 5, 2500, out int totalHits).ToList();

        Assert.Equal(new[] { "Items/Oil Flask.md", "Items/Brass.md" }, Headers(results));
        Assert.Equal(2, totalHits);
    }

    [Fact]
    public async Task WikiViewMissProbe_ListsTheFullerMatchFirst()
    {
        using var service = await CreateServiceAsync();
        var tool = new WikiViewTool(service);

        string content = tool.BuildMissContent("brass lantern oil refill", null);

        int lighting = content.IndexOf("Guides/Lighting.md", StringComparison.Ordinal);
        int brass = content.IndexOf("Items/Brass.md", StringComparison.Ordinal);
        Assert.True(lighting >= 0, content);
        Assert.True(brass > lighting, content);
    }

    [Fact]
    public async Task HeldOutTopic_FullerBodyMatchOutranksTitleMatch()
    {
        using var service = await CreateServiceAsync();

        var results = service.GetRelevantSnippets("honey smoke hive", null, 5, 2500, out int totalHits).ToList();

        Assert.Equal(new[] { "Guides/Beekeeping.md", "Items/Honey.md" }, Headers(results));
        Assert.Equal(2, totalHits);
    }
}

/// <summary>
/// Covers the title comparison wiki_view and the lookup tools use to detect an exact-title hit:
/// case-insensitive, with one leading "The " ignored on either side.
/// </summary>
public class WikiTitleEqualsTests
{
    [Theory]
    [InlineData("The Orb of Fate", "Orb of Fate", true)]
    [InlineData("Orb of Fate", "The Orb of Fate", true)]
    [InlineData("the orb of fate", "ORB OF FATE", true)]
    [InlineData("The Orb of Fate", "The Orb of Fate", true)]
    [InlineData("Dagger", "dagger", true)]
    [InlineData("The Theater", "Theater", true)]
    [InlineData("Theater", "Theater", true)]
    [InlineData("Theater", "ater", false)]
    [InlineData("The", "", false)]
    [InlineData("The The Foo", "Foo", false)]
    [InlineData("The Orb of Fate", "Orb", false)]
    [InlineData(null, "Foo", false)]
    public void TitleEquals_IgnoresOneLeadingTheOnly(string? title, string request, bool expected)
    {
        Assert.Equal(expected, WikiService.TitleEquals(title, request));
    }
}
