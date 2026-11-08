using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Pins that a GnollHack wiki page's leading YAML front matter block is removed exactly as
/// Gollum removes it before rendering, and that WikiService indexes the stripped text for
/// <c>.md</c> files only, so the stored content and the corpus fingerprint hold what the wiki
/// shows. Every expected <see cref="WikiFrontMatter.Strip"/> value is gollum-lib's own result.
/// </summary>
public class WikiFrontMatterTests : IDisposable
{
    private const string DescriptionToken = "zqxdescriptiontoken";

    private const string DownloadBody =
        "## Android\r\nGet the zqxandroidbody build from Google Play.\r\n\r\n## Windows\r\nGet the Windows build from Steam.\r\n";

    private const string DownloadFrontMatter =
        "---\r\ndescription: \"Downloads for GnollHack: " + DescriptionToken + " for every platform.\"\r\n---\r\n\r\n";

    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch { }
            }
        }
    }

    [Theory]
    [InlineData("---\ndescription: \"x\"\n---\n\n## A\n", "## A\n")]
    [InlineData("---\r\ndescription: \"x\"\r\n---\r\n\r\n## A\r\n", "## A\r\n")]
    [InlineData("---\na: 1\n...\nBody", "Body")]
    [InlineData("---\n---\nBody", "Body")]
    [InlineData("---\na: [\n---\nBody", "Body")]
    [InlineData("---\na: 1\n---\n---\nb: 2\n---\n", "---\nb: 2\n---\n")]
    public void Strip_LeadingBlock_IsRemoved(string input, string expected)
    {
        Assert.Equal(expected, WikiFrontMatter.Strip(input));
    }

    [Theory]
    [InlineData("## A\nText")]
    [InlineData("Intro\n---\na: 1\n---\n")]
    [InlineData("---\na: 1\nBody")]
    [InlineData("## A\n\n---\n\nText")]
    public void Strip_NoLeadingBlock_LeavesTheTextUnchanged(string input)
    {
        Assert.Equal(input, WikiFrontMatter.Strip(input));
    }

    [Fact]
    public async Task Index_MarkdownPageWithFrontMatter_StoresTheBodyOnly()
    {
        string dir = CreateCorpus();
        File.WriteAllText(Path.Combine(dir, "Download.md"), DownloadFrontMatter + DownloadBody);

        using var service = new WikiService(BuildConfig(dir));
        await service.InitializationTask;

        string? article = service.GetArticle("Download");
        Assert.NotNull(article);
        Assert.Contains("zqxandroidbody", article);
        Assert.DoesNotContain("description:", article);
        Assert.DoesNotContain(DescriptionToken, article);

        Assert.Empty(service.GetRelevantSnippets(DescriptionToken, null, 5, 500));
        Assert.NotEmpty(service.GetRelevantSnippets("zqxandroidbody", null, 5, 500));
    }

    [Fact]
    public async Task Index_FrontMatter_DoesNotChangeTheContentFingerprint()
    {
        string withFrontMatter = CreateCorpus();
        File.WriteAllText(Path.Combine(withFrontMatter, "Download.md"), DownloadFrontMatter + DownloadBody);

        string withoutFrontMatter = CreateCorpus();
        File.WriteAllText(Path.Combine(withoutFrontMatter, "Download.md"), DownloadBody);

        using var first = new WikiService(BuildConfig(withFrontMatter));
        using var second = new WikiService(BuildConfig(withoutFrontMatter));
        await Task.WhenAll(first.InitializationTask, second.InitializationTask);

        Assert.NotNull(first.ContentFingerprint);
        Assert.NotNull(second.ContentFingerprint);
        Assert.Equal(second.ContentFingerprint!.Sha256, first.ContentFingerprint!.Sha256);
    }

    [Fact]
    public async Task Index_TextFileWithFrontMatter_KeepsIt()
    {
        string dir = CreateCorpus();
        File.WriteAllText(Path.Combine(dir, "Notes.txt"), DownloadFrontMatter + DownloadBody);

        using var service = new WikiService(BuildConfig(dir));
        await service.InitializationTask;

        string? article = service.GetArticle("Notes");
        Assert.NotNull(article);
        Assert.Contains("description:", article);
        Assert.Contains(DescriptionToken, article);
    }

    private string CreateCorpus()
    {
        string dir = Path.Combine(Path.GetTempPath(), "WikiFrontMatterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static IConfiguration BuildConfig(string wikiPath)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new List<KeyValuePair<string, string?>> { new("WikiPath", wikiPath) })
            .Build();
    }
}
