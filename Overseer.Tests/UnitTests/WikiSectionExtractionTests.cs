using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Pins how wiki_view resolves a <c>section</c> against an article's headings: the exact and
/// normalised passes that let a plain request find an emoji-prefixed heading while an exact heading
/// still wins, the unique-substring pass after them, the bound of the extracted section at the next
/// same-or-higher-level heading, and the heading list the marker line carries when nothing matches
/// — and that nethack_wiki_view resolves the same request to the same text. A synthetic corpus
/// supplies the emoji headings, the plain-and-decorated duplicate pair, enough headings to exercise
/// the list's character cap, and the substring cases.
/// </summary>
public class WikiSectionExtractionTests : IDisposable
{
    private readonly string _tempDir;

    // The bound BuildHeadingListFragment truncates the joined heading list at.
    private const int HeadingListMaxChars = 600;

    public WikiSectionExtractionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WikiSectionExtractionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        File.WriteAllText(Path.Combine(_tempDir, "Runewords.md"),
@"Runewords are combinations of runes that grant a property when engraved together.

## 🔮 Elbereth
The elberethrunemark scares most monsters off the square it is engraved on.

## ✨ Gilthoniel
The gilthonielrunemark is a lesser word that answers a prayer instead.
");

        // The decorated heading deliberately comes first, so a request for the plain name can only
        // land on the exact one if the exact pass runs before the normalised pass.
        File.WriteAllText(Path.Combine(_tempDir, "Engraving.md"),
@"Engraving writes a word into the dust, the ice or the stone of a square.

## 📝 Notes
The decoratednotesbody collects the asides nobody had a better home for.

## Notes
The plainnotesbody collects the caveats that actually matter.
");

        var compendium = new StringBuilder();
        compendium.AppendLine("Compendium collects every ritual note the other articles left out.");
        compendium.AppendLine();
        for (int i = 1; i <= 30; i++)
        {
            compendium.AppendLine($"## 🔮 Compendium Chapter Number {i:00}");
            compendium.AppendLine($"The compendiumbody{i:00} paragraph says nothing of consequence.");
            compendium.AppendLine();
        }
        File.WriteAllText(Path.Combine(_tempDir, "Compendium.md"), compendium.ToString());

        // The NetHack wiki's Spellcasting headings: "Spell failure" is contained in exactly one of
        // them, and "spell" in several.
        File.WriteAllText(Path.Combine(_tempDir, "Spellcasting.md"),
@"Spellcasting casts a known spell at the cost of energy.

## Basics
The basicsbody says what a spell is.

## Spellcasting costs
The costsbody lists the energy each level needs.

## Forgotten spells
The forgottenbody covers spells past their retention.

## Calculating spell success rate
The successbody walks through the formula.

## Minimum spell failure rates
The minfailbody tabulates the floor per role.

## Spell effects
The effectsbody points at the per-spell articles.
");

        // A heading that equals the request beside longer headings that contain it.
        File.WriteAllText(Path.Combine(_tempDir, "Armor.md"),
@"Armor protects the wearer.

## Strategy
The plainstrategybody is the general advice.

## Role and attribute strategy
The rolestrategybody is advice per role.

## Armor strategy
The armorstrategybody is advice per slot.
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

    private async Task<string?> ViewSectionAsync(string article, string section)
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiViewTool(service);

        var jsonParams = JsonDocument.Parse(JsonSerializer.Serialize(new { article, section })).RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        return result.Content;
    }

    /// <summary>
    /// 12.7% of the wiki's headings carry an emoji prefix, so the plain name a model asks for is
    /// never the heading as written. All three spellings reach the same section, and the section
    /// ends before the next heading.
    /// </summary>
    [Theory]
    [InlineData("Elbereth")]
    [InlineData("🔮 Elbereth")]
    [InlineData("elbereth")]
    public async Task WikiView_SectionOnAnEmojiPrefixedHeading_ReturnsThatSectionOnly(string section)
    {
        string? content = await ViewSectionAsync("Runewords", section);

        Assert.Contains("🔮 Elbereth", content);
        Assert.Contains("elberethrunemark", content);
        Assert.DoesNotContain("gilthonielrunemark", content);
        Assert.DoesNotContain("not found in article", content);
    }

    /// <summary>
    /// Stripping is leading only and the exact pass runs first, so a plain heading and a decorated
    /// heading of the same name stay distinct and the plain request gets the plain one.
    /// </summary>
    [Fact]
    public async Task WikiView_PlainAndDecoratedHeadingsOfTheSameName_PrefersTheExactMatch()
    {
        string? content = await ViewSectionAsync("Engraving", "Notes");

        Assert.Contains("plainnotesbody", content);
        Assert.DoesNotContain("decoratednotesbody", content);
    }

    [Fact]
    public async Task WikiView_SectionMatchesNoHeading_MarkerLineListsTheHeadingsAndTheArticleFollows()
    {
        string? content = await ViewSectionAsync("Runewords", "Nope");

        Assert.Contains("[Section 'Nope' not found in article. Headings: ", content);
        Assert.Contains("Headings: 🔮 Elbereth; ✨ Gilthoniel. Returning full text.]", content);

        // The whole article still follows the marker line.
        Assert.Contains("elberethrunemark", content);
        Assert.Contains("gilthonielrunemark", content);
        Assert.Contains("Runewords are combinations of runes", content);
    }

    [Fact]
    public async Task WikiView_SectionMatchesNoHeading_HeadingListIsCappedWithAnEllipsis()
    {
        string? content = await ViewSectionAsync("Compendium", "Nope");

        Assert.NotNull(content);
        Assert.Contains("[Section 'Nope' not found in article. Headings: ", content);
        Assert.Contains("…. Returning full text.]", content);

        const string prefix = "Headings: ";
        const string suffix = ". Returning full text.]";
        int start = content!.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        int end = content.IndexOf(suffix, start, StringComparison.Ordinal);
        string list = content.Substring(start, end - start);

        Assert.EndsWith("…", list);

        // The cap plus the ellipsis, or one character fewer where the cut would otherwise have
        // split a surrogate pair and left a lone surrogate in the line.
        Assert.InRange(list.Length, HeadingListMaxChars, HeadingListMaxChars + 1);

        // The article itself is long enough for the cap to have bitten.
        Assert.Contains("compendiumbody30", content);
    }

    /// <summary>
    /// A request contained in exactly one heading selects that heading.
    /// </summary>
    [Fact]
    public async Task WikiView_SectionIsASubstringOfExactlyOneHeading_ReturnsThatSection()
    {
        string? content = await ViewSectionAsync("Spellcasting", "Spell failure");

        Assert.Contains("## Minimum spell failure rates", content);
        Assert.Contains("minfailbody", content);
        Assert.DoesNotContain("successbody", content);
        Assert.DoesNotContain("effectsbody", content);
        Assert.DoesNotContain("not found in article", content);
    }

    /// <summary>
    /// The exact and normalised passes run before the substring pass, so a heading equal to the
    /// request wins over longer headings that contain it.
    /// </summary>
    [Fact]
    public async Task WikiView_SectionEqualsOneHeadingAndIsContainedInOthers_PrefersTheExactHeading()
    {
        string? content = await ViewSectionAsync("Armor", "strategy");

        Assert.Contains("plainstrategybody", content);
        Assert.DoesNotContain("rolestrategybody", content);
        Assert.DoesNotContain("armorstrategybody", content);
    }

    /// <summary>
    /// A request contained in several headings is ambiguous: the miss line lists the candidates.
    /// </summary>
    [Fact]
    public async Task WikiView_SectionIsASubstringOfSeveralHeadings_MarkerLineListsTheHeadings()
    {
        string? content = await ViewSectionAsync("Spellcasting", "spell");

        Assert.Contains(
            "[Section 'spell' not found in article. Headings: Basics; Spellcasting costs; Forgotten spells; " +
            "Calculating spell success rate; Minimum spell failure rates; Spell effects. Returning full text.]",
            content);
        Assert.Contains("basicsbody", content);
        Assert.Contains("effectsbody", content);
    }

    /// <summary>
    /// nethack_wiki_view and wiki_view share one section extractor: for the same article content
    /// and the same request, the text under the header line is identical.
    /// </summary>
    [Theory]
    [InlineData("Spellcasting", "Spell failure")]
    [InlineData("Spellcasting", "spell")]
    [InlineData("Armor", "strategy")]
    [InlineData("Runewords", "Elbereth")]
    [InlineData("Runewords", "Nope")]
    [InlineData("Engraving", "Notes")]
    public async Task NetHackWikiView_ReturnsTheSameSectionTextAsWikiView(string article, string section)
    {
        string? gnollHack = await ViewSectionAsync(article, section);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new List<KeyValuePair<string, string?>> { new("NetHackWikiPath", _tempDir) })
            .Build();
        using var netHackService = new NetHackWikiService(config);
        await netHackService.InitializationTask;
        string? netHack = netHackService.GetArticle(article, section);

        Assert.NotNull(gnollHack);
        Assert.NotNull(netHack);
        Assert.StartsWith($"--- {article} ---\n", netHack);
        Assert.Equal(BodyAfterHeader(gnollHack!), BodyAfterHeader(netHack!));
    }

    // The two services label an article differently (path form vs. title); the text below the
    // header line is what the extractor produced.
    private static string BodyAfterHeader(string result) => result.Substring(result.IndexOf('\n') + 1);
}
