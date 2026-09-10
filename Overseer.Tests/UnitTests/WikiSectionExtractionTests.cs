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
/// Pins how wiki_view resolves a <c>section</c> against an article's headings: the two-pass match
/// that lets a plain request find an emoji-prefixed heading while an exact heading still wins, the
/// bound of the extracted section at the next same-or-higher-level heading, and the heading list
/// the marker line carries when nothing matches. A synthetic corpus of three articles supplies the
/// emoji headings, the plain-and-decorated duplicate pair, and enough headings to exercise the
/// list's character cap.
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
}
