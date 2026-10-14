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
/// Covers the notice line wiki_view prepends to a section-less article whose rendered text (header
/// included) overruns the result cap: that it names the article's true length and how much is
/// shown, that it lists every heading the article carries — including ones beyond the shown slice,
/// since the list is built from the whole article rather than the truncated text — that a
/// short article carries no such line, and that a very long heading list is itself cut down (with a
/// trailing "…") so the notice line never exceeds its own 600-character cap. Also covers
/// <see cref="MarkdownSectionExtractor.Headings"/> directly, since wiki_view's notice and the
/// section-miss marker line both build their heading lists from it.
/// </summary>
public class WikiViewToolHeadingsTests : IDisposable
{
    private readonly string _tempDir;

    // A single repeated block of filler text with no heading markers or embedded newlines, so it
    // can be cut to any exact length without disturbing heading parsing or line structure.
    private const string FillerUnit =
        "Gnolls and their kin fill these dungeon halls with routine, forgettable dialogue that pads the article out. ";

    public WikiViewToolHeadingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WikiViewToolHeadingsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        // LongArticle.md: rendered (header included) to exactly 17,840 characters, well past the
        // 10,000-character default cap. "Special Sacrifices" sits well past the article's midpoint,
        // after a large filler block, so the fixture also stands as "a heading placed late".
        File.WriteAllText(Path.Combine(_tempDir, "LongArticle.md"), BuildLongArticleBody(TargetLength: 17840, FileName: "LongArticle.md"));

        // ShortArticle.md: rendered to exactly 900 characters, comfortably under the cap.
        File.WriteAllText(Path.Combine(_tempDir, "ShortArticle.md"), BuildPlainBody(TargetLength: 900, FileName: "ShortArticle.md"));

        // ManyHeadings.md: rendered to exactly 15,000 characters (past the cap) with forty
        // long-titled headings, so their joined list alone exceeds the notice line's own
        // 600-character cap and has to be truncated with a trailing "…".
        File.WriteAllText(Path.Combine(_tempDir, "ManyHeadings.md"), BuildManyHeadingsBody(TargetLength: 15000, FileName: "ManyHeadings.md", ChapterCount: 40));
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

    /// <summary>Repeats <paramref name="unit"/> until it reaches exactly <paramref name="length"/> characters.</summary>
    private static string Repeat(string unit, int length)
    {
        var sb = new StringBuilder(length);
        while (sb.Length < length)
        {
            sb.Append(unit);
        }
        sb.Length = length;
        return sb.ToString();
    }

    /// <summary>
    /// A body with no headings at all, of the exact length needed so the rendered article (this
    /// body plus its <c>--- FileName ---</c> header) totals <paramref name="TargetLength"/>.
    /// </summary>
    private static string BuildPlainBody(int TargetLength, string FileName)
    {
        int headerLength = $"--- {FileName} ---\n".Length;
        return Repeat(FillerUnit, TargetLength - headerLength);
    }

    /// <summary>
    /// A body carrying three headings — Introduction, then Special Sacrifices after a large filler
    /// block, then Conclusion — padded so the rendered article totals exactly
    /// <paramref name="TargetLength"/> characters.
    /// </summary>
    private static string BuildLongArticleBody(int TargetLength, string FileName)
    {
        int headerLength = $"--- {FileName} ---\n".Length;
        int bodyLength = TargetLength - headerLength;

        var sb = new StringBuilder();
        sb.Append("LongArticle exists purely to exceed the wiki_view result cap so its notice line can be exercised.\n\n");
        sb.Append("## Introduction\n");
        sb.Append(Repeat(FillerUnit, 9500));
        sb.Append("\n\n## Special Sacrifices\n");
        sb.Append(Repeat(FillerUnit, 400));
        sb.Append("\n\n## Conclusion\n");

        int remaining = bodyLength - sb.Length;
        if (remaining > 0)
        {
            sb.Append(Repeat(FillerUnit, remaining));
        }

        return sb.ToString();
    }

    /// <summary>
    /// A body carrying <paramref name="ChapterCount"/> long-titled headings, padded so the rendered
    /// article totals exactly <paramref name="TargetLength"/> characters.
    /// </summary>
    private static string BuildManyHeadingsBody(int TargetLength, string FileName, int ChapterCount)
    {
        int headerLength = $"--- {FileName} ---\n".Length;
        int bodyLength = TargetLength - headerLength;

        var sb = new StringBuilder();
        sb.Append("ManyHeadings exists purely to force the wiki_view notice line's own 600-character cap.\n\n");
        for (int i = 1; i <= ChapterCount; i++)
        {
            sb.Append($"## Chapter {i:00}: A Rather Long Descriptive Heading Title\n");
            sb.Append("Body text for this chapter, kept short.\n\n");
        }

        int remaining = bodyLength - sb.Length;
        if (remaining > 0)
        {
            sb.Append(Repeat(FillerUnit, remaining));
        }

        return sb.ToString();
    }

    [Fact]
    public async Task WikiViewTool_ArticleLongerThanCap_PrependsNoticeNamingLateHeading()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiViewTool(service);

        var ctx = Context();
        var jsonParams = JsonDocument.Parse(JsonSerializer.Serialize(new { article = "LongArticle" })).RootElement;
        var result = await tool.ExecuteAsync(jsonParams, ctx, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Content);

        Assert.StartsWith("[Article is 17840 characters; the first ", result.Content);
        Assert.Contains("Headings: Introduction; Special Sacrifices; Conclusion.", result.Content);
        Assert.Contains("Call wiki_view again with section set to one of them to read the rest.]", result.Content);

        // The notice plus the shown slice lands at exactly the cap — never over it, so
        // ToolExecutor's own truncation never fires on top of this and cuts the notice line itself.
        Assert.Equal(ctx.MaxResultLength, result.Content!.Length);
    }

    [Fact]
    public async Task WikiViewTool_ArticleLongerThanCap_InSpoilerFreeMode_KeepsTheSuffixWithinTheCap()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiViewTool(service);

        var ctx = new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1),
            SpoilerFreeMode = true
        };
        var jsonParams = JsonDocument.Parse(JsonSerializer.Serialize(new { article = "LongArticle" })).RootElement;
        var result = await tool.ExecuteAsync(jsonParams, ctx, CancellationToken.None);

        Assert.True(result.Success);
        Assert.StartsWith("[Article is 17840 characters; the first ", result.Content);
        Assert.EndsWith("Only share mechanics, not unrevealed content.]", result.Content);
        Assert.Equal(ctx.MaxResultLength, result.Content!.Length);
    }

    [Fact]
    public async Task WikiViewTool_ArticleUnderCap_CarriesNoNoticeLine()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiViewTool(service);

        var jsonParams = JsonDocument.Parse(JsonSerializer.Serialize(new { article = "ShortArticle" })).RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(900, result.Content!.Length);
        Assert.DoesNotContain("[Article is", result.Content);
    }

    [Fact]
    public async Task WikiViewTool_SectionRequested_NeverPrependsTheTooLongNotice()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiViewTool(service);

        // "Special Sacrifices" resolves on the same over-cap article, but a section request is
        // never the too-long-article case: it either returns the section's own (short) text or the
        // section-miss marker, neither of which is this notice line.
        var jsonParams = JsonDocument.Parse(JsonSerializer.Serialize(new { article = "LongArticle", section = "Special Sacrifices" })).RootElement;
        var result = await tool.ExecuteAsync(jsonParams, Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain("are shown. Headings:", result.Content);
    }

    [Fact]
    public async Task WikiViewTool_HeadingListLongerThanNoticeCap_IsTruncatedWithAnEllipsis()
    {
        using var service = new WikiService(BuildConfig());
        await service.InitializationTask;
        var tool = new WikiViewTool(service);

        var ctx = Context();
        var jsonParams = JsonDocument.Parse(JsonSerializer.Serialize(new { article = "ManyHeadings" })).RootElement;
        var result = await tool.ExecuteAsync(jsonParams, ctx, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Content);

        string content = result.Content!;
        Assert.StartsWith("[Article is 15000 characters", content);

        int firstNewline = content.IndexOf('\n');
        Assert.True(firstNewline > 0, "Notice line was not followed by the article text.");
        string noticeLine = content.Substring(0, firstNewline);

        Assert.True(noticeLine.Length <= 600, $"Notice line was {noticeLine.Length} characters: {noticeLine}");
        Assert.Contains("…", noticeLine);
        Assert.Contains("Chapter 01", noticeLine);
        Assert.DoesNotContain("Chapter 40", noticeLine);

        // Still lands exactly at the cap, the same as the untruncated-heading-list case.
        Assert.Equal(ctx.MaxResultLength, content.Length);
    }

    [Fact]
    public void Headings_ReturnsEveryHeadingTitleInDocumentOrder_LeadingHashesStripped()
    {
        string content =
            "Intro paragraph with no heading yet.\n\n" +
            "## 🔮 Elbereth\nBody one.\n\n" +
            "### Nested Heading\nBody two.\n\n" +
            "# Top Level\nBody three.\n";

        var headings = MarkdownSectionExtractor.Headings(content);

        Assert.Equal(new[] { "🔮 Elbereth", "Nested Heading", "Top Level" }, headings);
    }

    [Fact]
    public void Headings_ArticleWithNoHeadings_ReturnsEmptyList()
    {
        var headings = MarkdownSectionExtractor.Headings("Just a paragraph of plain text, no headings at all.\n");

        Assert.Empty(headings);
    }
}
