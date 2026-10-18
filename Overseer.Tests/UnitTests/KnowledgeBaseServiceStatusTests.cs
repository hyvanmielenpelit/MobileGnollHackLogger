using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;
using Overseer.Services.CorpusIndexing;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// <see cref="KnowledgeBaseService.GetStatus"/> and <see cref="KnowledgeBaseService.GetArticleSource"/>:
/// the per-article front-matter flags, a case-duplicate topic recorded as shadowed with today's
/// winner, the topic list's length, a reload after an edit, a missing <c>Content</c> folder, and the
/// raw article source. Each test builds its knowledge base in its own temporary folder.
/// </summary>
public sealed class KnowledgeBaseServiceStatusTests : IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly string _kbPath;
    private readonly string _contentPath;
    private readonly List<KnowledgeBaseService> _services = new();

    public KnowledgeBaseServiceStatusTests()
    {
        _kbPath = Path.Combine(Path.GetTempPath(), "KnowledgeBaseServiceStatusTests_" + Guid.NewGuid().ToString("N"));
        _contentPath = Path.Combine(_kbPath, "Content");
        Directory.CreateDirectory(_kbPath);
    }

    public void Dispose()
    {
        foreach (var service in _services)
        {
            service.Dispose();
        }

        if (Directory.Exists(_kbPath))
        {
            try
            {
                Directory.Delete(_kbPath, true);
            }
            catch { }
        }
    }

    // -- helpers -------------------------------------------------------------------------------------

    private static IConfiguration BuildConfig(string? kbPath)
    {
        var settings = new List<KeyValuePair<string, string?>>();
        if (kbPath != null) settings.Add(new KeyValuePair<string, string?>("KbPath", kbPath));
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private KnowledgeBaseService CreateService(string? kbPath, Func<string, IEnumerable<string>>? listArticleFiles = null)
    {
        var service = new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, BuildConfig(kbPath), listArticleFiles);
        _services.Add(service);
        return service;
    }

    private string WriteArticle(string relativePath, string content)
    {
        string path = Path.Combine(_contentPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Article(string? title, string? summary, string body)
    {
        string frontMatter = "---\n";
        if (title != null) frontMatter += "title: " + title + "\n";
        if (summary != null) frontMatter += "summary: " + summary + "\n";
        return frontMatter + "---\n\n" + body + "\n";
    }

    private static async Task AwaitStartup(KnowledgeBaseService service)
    {
        await service.InitializationTask.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
    }

    /// <summary>Waits, bounded, until the job <paramref name="jobId"/> is in the finished history.</summary>
    private static async Task<CorpusIndexJob> WaitForJob(KnowledgeBaseService service, string jobId)
    {
        var ct = TestContext.Current.CancellationToken;
        var deadline = DateTime.UtcNow + WaitLimit;
        while (DateTime.UtcNow < deadline)
        {
            var job = service.GetStatus().RecentJobs.FirstOrDefault(j => j.Id == jobId);
            if (job != null) return job;
            await Task.Delay(20, ct);
        }

        Assert.Fail($"Job {jobId} did not finish within {WaitLimit.TotalSeconds:0} s.");
        return null!;
    }

    // -- article info --------------------------------------------------------------------------------

    [Fact]
    public async Task GetStatus_Articles_CarryTheFrontMatterFlags()
    {
        string both = Article("Both Fields", "Has a title and a summary.", "Body of both.");
        string titleOnly = Article("Only Title", "", "Body of title only.");
        string blankTitle = Article("   ", "Has a summary only.", "Body of blank title.");
        string plain = "No front matter here.\n";
        WriteArticle("Both.md", both);
        WriteArticle("TitleOnly.md", titleOnly);
        WriteArticle("BlankTitle.md", blankTitle);
        WriteArticle("Guides/Plain.md", plain);

        var service = CreateService(_kbPath);
        await AwaitStartup(service);

        var status = service.GetStatus();
        Assert.Equal(new[] { "BlankTitle", "Both", "Guides/Plain", "TitleOnly" }, status.Articles.Select(a => a.Topic).ToArray());

        var bothInfo = status.Articles.Single(a => a.Topic == "Both");
        Assert.Equal("Both Fields", bothInfo.Title);
        Assert.True(bothInfo.HasTitle);
        Assert.True(bothInfo.HasSummary);
        Assert.Equal((long)both.Length, bothInfo.Bytes);

        var titleOnlyInfo = status.Articles.Single(a => a.Topic == "TitleOnly");
        Assert.True(titleOnlyInfo.HasTitle);
        Assert.False(titleOnlyInfo.HasSummary);

        var blankTitleInfo = status.Articles.Single(a => a.Topic == "BlankTitle");
        Assert.False(blankTitleInfo.HasTitle);
        Assert.True(blankTitleInfo.HasSummary);

        var plainInfo = status.Articles.Single(a => a.Topic == "Guides/Plain");
        Assert.Equal("Guides/Plain", plainInfo.Title);
        Assert.False(plainInfo.HasTitle);
        Assert.False(plainInfo.HasSummary);
        Assert.Equal((long)plain.Length, plainInfo.Bytes);

        Assert.Equal(4, status.Index!.FileCount);
        Assert.Null(status.Index.MaxFileSizeKB);
        Assert.Empty(status.ShadowedTopics);
        Assert.True(status.ContentPathExists);
    }

    [Fact]
    public async Task GetStatus_CaseDuplicateTopic_IsShadowedAndTheLastFileWins()
    {
        string upper = WriteArticle("Foo.md", Article("Foo Title", "The foo summary.", "Foo body."));
        string lower = Path.Combine(_contentPath, "foo.md");
        if (!File.Exists(lower))
        {
            File.Copy(upper, lower);
        }

        var service = CreateService(_kbPath, _ => new[] { upper, lower });
        await AwaitStartup(service);

        var status = service.GetStatus();
        var shadowed = Assert.Single(status.ShadowedTopics);
        Assert.Equal("Foo", shadowed.Topic);
        Assert.Equal("Content/Foo.md", shadowed.Path);
        Assert.Equal("Content/foo.md", shadowed.ShadowedByPath);

        var article = Assert.Single(status.Articles);
        Assert.Equal("foo", article.Topic);
        Assert.Equal(2, status.Index!.FileCount);

        Assert.Equal(new[] { "Foo" }, service.GetAvailableTopics().ToArray());
        Assert.Contains("**foo**", service.GetTopicList());
        Assert.DoesNotContain("**Foo**", service.GetTopicList());
        Assert.Contains("1 shadowed", status.RecentJobs[0].Message);
    }

    [Fact]
    public async Task GetStatus_TopicListChars_IsTheTopicListLength()
    {
        WriteArticle("Altars.md", Article("Altars", "Praying and sacrificing at altars.", "Altar body."));
        WriteArticle("Shops.md", Article("Shops", "Buying and selling.", "Shop body."));

        var service = CreateService(_kbPath);
        await AwaitStartup(service);

        var status = service.GetStatus();
        Assert.True(status.TopicListChars > 0);
        Assert.Equal(service.GetTopicList().Length, status.TopicListChars);
    }

    // -- reload --------------------------------------------------------------------------------------

    [Fact]
    public async Task RequestReindex_AfterAnEdit_PublishesTheNewContent()
    {
        string path = WriteArticle("Altars.md", Article("Altars", "Old summary.", "The old altar text."));

        var service = CreateService(_kbPath);
        await AwaitStartup(service);
        Assert.Contains("The old altar text.", service.GetArticle("Altars"));

        string edited = Article("Altars", "New summary.", "The new altar text.");
        File.WriteAllText(path, edited);

        Assert.Equal(CorpusIndexStartResult.Started, service.RequestReindex());
        string? jobId = service.LastStartedJobId;
        Assert.NotNull(jobId);
        var job = await WaitForJob(service, jobId!);

        Assert.Equal(CorpusIndexTrigger.Manual, job.Trigger);
        Assert.Equal(CorpusIndexOutcome.Succeeded, job.Outcome);
        Assert.Contains("The new altar text.", service.GetArticle("Altars"));
        Assert.DoesNotContain("The old altar text.", service.GetArticle("Altars"));
        Assert.Equal(edited, service.GetArticleSource("Altars"));
        Assert.Contains("New summary.", service.GetTopicList());

        var status = service.GetStatus();
        Assert.Equal(2L, status.Index!.Generation);
        Assert.Equal((long)edited.Length, status.Articles.Single().Bytes);
    }

    // -- folders and configuration -------------------------------------------------------------------

    [Fact]
    public async Task GetStatus_MissingContentFolder_HasNoArticles()
    {
        var service = CreateService(_kbPath);
        await AwaitStartup(service);

        var status = service.GetStatus();
        Assert.True(status.Configured);
        Assert.True(status.PathExists);
        Assert.False(status.ContentPathExists);
        Assert.Empty(status.Articles);
        Assert.Null(status.Index);
        Assert.Empty(service.GetAvailableTopics());
        Assert.Equal(0, status.TopicListChars);

        var job = Assert.Single(status.RecentJobs);
        Assert.Equal(CorpusIndexOutcome.Failed, job.Outcome);
        Assert.Equal("The Content folder under KbPath does not exist.", job.Message);
    }

    [Fact]
    public async Task RequestReindex_UnsetOrMissingKbPath_IsRefused()
    {
        var unset = CreateService(null);
        var missing = CreateService(Path.Combine(_kbPath, "missing"));
        await AwaitStartup(unset);
        await AwaitStartup(missing);

        Assert.Equal(CorpusIndexStartResult.NotConfigured, unset.RequestReindex());
        Assert.Equal(CorpusIndexStartResult.PathMissing, missing.RequestReindex());

        var unsetStatus = unset.GetStatus();
        Assert.Equal(CorpusKind.KnowledgeBase, unsetStatus.Kind);
        Assert.Equal("KbPath", unsetStatus.ConfigKey);
        Assert.False(unsetStatus.Configured);
        Assert.Null(unsetStatus.Path);
        Assert.Equal("KbPath is not configured.", Assert.Single(unsetStatus.RecentJobs).Message);

        var missingStatus = missing.GetStatus();
        Assert.True(missingStatus.Configured);
        Assert.False(missingStatus.PathExists);
        Assert.False(missingStatus.ContentPathExists);
        Assert.Equal("The KbPath folder does not exist.", Assert.Single(missingStatus.RecentJobs).Message);
    }

    // -- article source ------------------------------------------------------------------------------

    [Fact]
    public async Task GetArticleSource_ReturnsTheRawTextIgnoringCase()
    {
        string source = Article("Elbereth", "Engraving to scare monsters.", "Engrave Elbereth in the dust.");
        WriteArticle("Tactics/Elbereth.md", source);

        var service = CreateService(_kbPath);
        await AwaitStartup(service);

        Assert.Equal(source, service.GetArticleSource("Tactics/Elbereth"));
        Assert.Equal(source, service.GetArticleSource("tactics/ELBERETH"));
        Assert.StartsWith("---\ntitle: Elbereth\n", service.GetArticleSource("Tactics/Elbereth"));
        Assert.Null(service.GetArticleSource("Tactics/Unknown"));

        Assert.Equal("Engrave Elbereth in the dust.\n", service.GetArticle("Tactics/Elbereth"));
    }
}
