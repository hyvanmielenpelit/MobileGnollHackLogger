using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Overseer.Services.CorpusIndexing;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// <see cref="NetHackWikiService.GetStatus"/> and its manual reindex: namespace counts, a new file
/// picked up by a requested pass, the startup-and-manual refresh mode, the conversion report's
/// <c>TreeChangedSinceIndex</c>, oversize files, and the status of an unset or missing
/// <c>NetHackWikiPath</c>. Each test builds its corpus in its own temporary folder.
/// </summary>
public sealed class NetHackWikiServiceStatusTests : IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly string _tempDir;
    private readonly List<NetHackWikiService> _services = new();

    public NetHackWikiServiceStatusTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NetHackWikiServiceStatusTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        foreach (var service in _services)
        {
            service.Dispose();
        }

        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch { }
        }
    }

    // -- helpers -------------------------------------------------------------------------------------

    private static IConfiguration BuildConfig(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private NetHackWikiService CreateService(params (string Key, string? Value)[] settings)
    {
        var service = new NetHackWikiService(BuildConfig(settings));
        _services.Add(service);
        return service;
    }

    private NetHackWikiService CreateServiceForTempDir(params (string Key, string? Value)[] extraSettings) =>
        CreateService(new (string Key, string? Value)[] { ("NetHackWikiPath", _tempDir) }.Concat(extraSettings).ToArray());

    private void WriteArticle(string fileName, string content) =>
        File.WriteAllText(Path.Combine(_tempDir, fileName), content);

    private static string Article(string title, string? ns, string body) =>
        "---\ntitle: " + title + "\n" + (ns != null ? "namespace: " + ns + "\n" : "") + "---\n\n" + body + "\n";

    private string ReportPath => Path.Combine(_tempDir, NetHackWikiConversionReportReader.FileName);

    private static async Task AwaitStartup(NetHackWikiService service)
    {
        await service.InitializationTask.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
    }

    // -- namespaces ----------------------------------------------------------------------------------

    [Fact]
    public async Task GetStatus_Namespaces_CountsFrontMatterNamespacesLowercasedAndArticleByDefault()
    {
        WriteArticle("Foo.md", Article("Foo", "Variant", "A variant page."));
        WriteArticle("Bar.md", Article("Bar", "variant", "Another variant page."));
        WriteArticle("Baz.md", Article("Baz", "Source", "A source page."));
        WriteArticle("Cockatrice.md", Article("Cockatrice", null, "A monster page."));
        WriteArticle("Plain.md", "No front matter at all.\n");
        WriteArticle("Elbereth.md", Article("Elbereth", "article", "An engraving."));

        var service = CreateServiceForTempDir();
        await AwaitStartup(service);

        var status = service.GetStatus();
        Assert.Equal(
            new[] { "article:3", "variant:2", "source:1" },
            status.Namespaces.Select(n => n.Namespace + ":" + n.Articles).ToArray());
        Assert.Equal(6, status.Index!.FileCount);
        Assert.Single(service.GetRelevantContext("variant page", "variant", 5), r => r.Contains("--- Foo ---"));
    }

    // -- reindex -------------------------------------------------------------------------------------

    [Fact]
    public async Task TryStartReindex_NewFile_IsFoundAfterTheManualPass()
    {
        WriteArticle("Cockatrice.md", Article("Cockatrice", "article", "A cockatrice petrifies."));

        var service = CreateServiceForTempDir();
        await AwaitStartup(service);
        Assert.Empty(service.GetRelevantContext("snarfblat"));

        WriteArticle("Snarfblat.md", Article("Snarfblat", "article", "A snarfblat is a curious tool."));

        var task = service.TryStartReindex(CorpusIndexTrigger.Manual, out var result);
        Assert.Equal(CorpusIndexStartResult.Started, result);
        Assert.NotNull(task);
        await task!.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        Assert.Contains(service.GetRelevantContext("snarfblat"), r => r.Contains("--- Snarfblat ---"));

        var status = service.GetStatus();
        Assert.Equal(2L, status.Index!.Generation);
        Assert.Equal(2, status.Index.FileCount);
        Assert.Equal(service.LastStartedJobId, status.RecentJobs[0].Id);
        Assert.Equal(CorpusIndexTrigger.Manual, status.RecentJobs[0].Trigger);
        Assert.Equal(CorpusIndexOutcome.Succeeded, status.RecentJobs[0].Outcome);
        Assert.Equal("2 articles", status.RecentJobs[0].Message);
        Assert.Equal(CorpusIndexTrigger.Startup, status.RecentJobs[1].Trigger);
    }

    [Fact]
    public async Task RequestReindex_ConfiguredFolder_Starts()
    {
        WriteArticle("Cockatrice.md", Article("Cockatrice", "article", "A cockatrice petrifies."));

        var service = CreateServiceForTempDir();
        await AwaitStartup(service);

        string? startupJobId = service.LastStartedJobId;
        Assert.Equal(CorpusIndexStartResult.Started, service.RequestReindex());
        Assert.NotNull(service.LastStartedJobId);
        Assert.NotEqual(startupJobId, service.LastStartedJobId);
    }

    // -- refresh mode and conversion report ----------------------------------------------------------

    [Fact]
    public async Task GetStatus_RefreshMode_IsStartupAndManualWithNoTimer()
    {
        WriteArticle("Cockatrice.md", Article("Cockatrice", "article", "A cockatrice petrifies."));

        var service = CreateServiceForTempDir();
        await AwaitStartup(service);

        var status = service.GetStatus();
        Assert.Equal(CorpusKind.NetHackWiki, status.Kind);
        Assert.Equal("NetHackWikiPath", status.ConfigKey);
        Assert.True(status.Configured);
        Assert.True(status.PathExists);
        Assert.True(status.IsIndexingComplete);
        Assert.Equal(CorpusRefreshMode.StartupAndManual, status.RefreshMode);
        Assert.Null(status.NextCheckAtUtc);
        Assert.Null(status.CheckIntervalMinutes);
        Assert.True(status.UpToDate);
        Assert.Equal(500, status.Index!.MaxFileSizeKB);
        Assert.Null(status.ConversionReport);
        Assert.Null(status.ConversionReportError);
        Assert.False(status.TreeChangedSinceIndex);
    }

    [Fact]
    public async Task GetStatus_ReportWrittenAfterTheIndex_TreeChangedSinceIndex()
    {
        WriteArticle("Cockatrice.md", Article("Cockatrice", "article", "A cockatrice petrifies."));

        var service = CreateServiceForTempDir();
        await AwaitStartup(service);

        File.WriteAllText(ReportPath, "{ \"generated\": \"2026-10-18 12:00:00\" }");
        File.SetLastWriteTimeUtc(ReportPath, DateTime.UtcNow.AddMinutes(5));

        var status = service.GetStatus();
        Assert.NotNull(status.ConversionReport);
        Assert.Null(status.ConversionReportError);
        Assert.True(status.TreeChangedSinceIndex);
    }

    [Fact]
    public async Task GetStatus_ReportWrittenBeforeTheIndex_TreeNotChangedSinceIndex()
    {
        WriteArticle("Cockatrice.md", Article("Cockatrice", "article", "A cockatrice petrifies."));
        File.WriteAllText(ReportPath, "{ \"generated\": \"2026-10-18 12:00:00\" }");
        File.SetLastWriteTimeUtc(ReportPath, DateTime.UtcNow.AddMinutes(-5));

        var service = CreateServiceForTempDir();
        await AwaitStartup(service);

        var status = service.GetStatus();
        Assert.NotNull(status.ConversionReport);
        Assert.Equal("2026-10-18 12:00:00", status.ConversionReport!.GeneratedAt);
        Assert.False(status.TreeChangedSinceIndex);
        Assert.Equal(1, status.Index!.FileCount);
    }

    // -- files left out ------------------------------------------------------------------------------

    [Fact]
    public async Task GetStatus_OversizeFile_IsListedInSkippedBySize()
    {
        WriteArticle("Cockatrice.md", Article("Cockatrice", "article", "A cockatrice petrifies."));
        string bigContent = Article("Big", "article", new string('x', 1500));
        WriteArticle("Big.md", bigContent);

        var service = CreateServiceForTempDir(("MaxNetHackWikiFileSizeKB", "1"));
        await AwaitStartup(service);

        var status = service.GetStatus();
        var index = status.Index;
        Assert.NotNull(index);
        Assert.Equal(1, index!.MaxFileSizeKB);
        Assert.Equal(1, index.FileCount);
        Assert.Equal(1, index.SkippedBySizeCount);
        var skipped = Assert.Single(index.SkippedBySize);
        Assert.Equal("Big.md", skipped.Path);
        Assert.Equal((long)bigContent.Length, skipped.Bytes);
        Assert.Equal("1 article, 1 over the size limit", status.RecentJobs[0].Message);
    }

    // -- configuration -------------------------------------------------------------------------------

    [Fact]
    public async Task RequestReindex_UnsetPath_IsNotConfiguredAndTheStartupJobFailed()
    {
        var service = CreateService();
        await AwaitStartup(service);

        Assert.Equal(CorpusIndexStartResult.NotConfigured, service.RequestReindex());

        var status = service.GetStatus();
        Assert.False(status.Configured);
        Assert.Null(status.Path);
        Assert.Null(status.Index);
        var job = Assert.Single(status.RecentJobs);
        Assert.Equal(CorpusIndexTrigger.Startup, job.Trigger);
        Assert.Equal(CorpusIndexOutcome.Failed, job.Outcome);
        Assert.Equal("NetHackWikiPath is not configured.", job.Message);
    }

    [Fact]
    public async Task RequestReindex_MissingFolder_IsPathMissingAndTheStartupJobFailed()
    {
        var service = CreateService(("NetHackWikiPath", Path.Combine(_tempDir, "missing")));
        await AwaitStartup(service);

        Assert.Equal(CorpusIndexStartResult.PathMissing, service.RequestReindex());

        var status = service.GetStatus();
        Assert.True(status.Configured);
        Assert.False(status.PathExists);
        Assert.True(status.IsIndexingComplete);
        Assert.Null(status.Index);
        var job = Assert.Single(status.RecentJobs);
        Assert.Equal(CorpusIndexTrigger.Startup, job.Trigger);
        Assert.Equal(CorpusIndexOutcome.Failed, job.Outcome);
        Assert.Equal("The NetHackWikiPath folder does not exist.", job.Message);
    }

    [Fact]
    public void Constructor_WithoutIndexing_RecordsNoJob()
    {
        WriteArticle("Cockatrice.md", Article("Cockatrice", "article", "A cockatrice petrifies."));

        var service = new NetHackWikiService(BuildConfig(("NetHackWikiPath", _tempDir)), null, 1, false);
        _services.Add(service);

        Assert.True(service.InitializationTask.IsCompleted);
        Assert.False(service.IsIndexingComplete);
        Assert.Null(service.LastStartedJobId);

        var status = service.GetStatus();
        Assert.Empty(status.RecentJobs);
        Assert.Null(status.CurrentJob);
        Assert.Null(status.Index);
    }
}
