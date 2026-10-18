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
/// <see cref="WikiService.GetStatus"/> and <see cref="WikiService.RequestReindex(CorpusIndexTrigger)"/>:
/// what a pass records about the files it left out (oversize, under a dot-directory, unreadable),
/// the per-category counts and duplicate titles, a manual reindex of the working tree, and the
/// status of an unset, blank or missing <c>WikiPath</c>. Each test builds its corpus in its own
/// temporary folder.
/// </summary>
public sealed class WikiServiceStatusTests : IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly string _tempDir;
    private readonly List<WikiService> _services = new();

    public WikiServiceStatusTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WikiServiceStatusTests_" + Guid.NewGuid().ToString("N"));
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

    private WikiService CreateService(params (string Key, string? Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var service = new WikiService(config);
        _services.Add(service);
        return service;
    }

    private WikiService CreateServiceForTempDir(params (string Key, string? Value)[] extraSettings) =>
        CreateService(new (string Key, string? Value)[] { ("WikiPath", _tempDir) }.Concat(extraSettings).ToArray());

    private string WriteArticle(string relativePath, string content)
    {
        string path = Path.Combine(_tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task AwaitStartup(WikiService service)
    {
        await service.InitializationTask.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
    }

    // -- files left out ------------------------------------------------------------------------------

    [Fact]
    public async Task GetStatus_OversizeFile_IsListedInSkippedBySize()
    {
        WriteArticle("Small.md", "A small article about gnolls.\n");
        string bigContent = new string('x', 1500);
        WriteArticle("Big.md", bigContent);

        var service = CreateServiceForTempDir(("MaxWikiFileSizeKB", "1"));
        await AwaitStartup(service);

        var index = service.GetStatus().Index;
        Assert.NotNull(index);
        Assert.Equal(1, index!.MaxFileSizeKB);
        Assert.Equal(1, index.FileCount);
        Assert.Equal(1, index.SkippedBySizeCount);
        var skipped = Assert.Single(index.SkippedBySize);
        Assert.Equal("Big.md", skipped.Path);
        Assert.Equal((long)bigContent.Length, skipped.Bytes);
        Assert.Empty(index.FailedFiles);
    }

    [Fact]
    public async Task GetStatus_DefaultSizeLimit_Is100KB()
    {
        WriteArticle("Small.md", "A small article.\n");

        var service = CreateServiceForTempDir();
        await AwaitStartup(service);

        Assert.Equal(100, service.GetStatus().Index!.MaxFileSizeKB);
    }

    [Fact]
    public async Task GetStatus_FileUnderDotDirectory_IsCountedAndNotIndexed()
    {
        WriteArticle("Gnoll.md", "Gnolls are common early monsters.\n");
        WriteArticle(".agents/x.md", "Agent notes about zygomorphic planning.\n");

        var service = CreateServiceForTempDir();
        await AwaitStartup(service);

        var index = service.GetStatus().Index;
        Assert.NotNull(index);
        Assert.Equal(1, index!.SkippedDotDirectories);
        Assert.Equal(1, index.FileCount);
        Assert.Empty(service.GetRelevantContext("zygomorphic"));
    }

    [Fact]
    public async Task GetStatus_UnreadableFile_IsListedInFailedFilesAndThePassSucceeds()
    {
        WriteArticle("Readable.md", "The quokkaberry grows near the gnoll village.\n");
        string lockedPath = WriteArticle("Locked.md", "This article is held open while the wiki is indexed.\n");

        WikiService service;
        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            service = CreateServiceForTempDir();
            await AwaitStartup(service);
        }

        var status = service.GetStatus();
        var index = status.Index;
        Assert.NotNull(index);
        Assert.Equal(1, index!.FileCount);
        Assert.Equal(1, index.FailedFileCount);
        var failed = Assert.Single(index.FailedFiles);
        Assert.Equal("Locked.md", failed.Path);
        Assert.StartsWith("IOException: ", failed.Error);

        var job = Assert.Single(status.RecentJobs);
        Assert.Equal(CorpusIndexTrigger.Startup, job.Trigger);
        Assert.Equal(CorpusIndexOutcome.Succeeded, job.Outcome);
        Assert.Contains("1 unreadable", job.Message);

        var hits = service.GetRelevantContext("quokkaberry").ToList();
        var hit = Assert.Single(hits);
        Assert.Contains("Readable.md", hit);
    }

    // -- categories and duplicate titles -------------------------------------------------------------

    [Fact]
    public async Task GetStatus_Categories_ArePerTopLevelDirectoryWithRootSortedByCount()
    {
        WriteArticle("Monsters/Gnoll.md", "Gnoll monster.\n");
        WriteArticle("Monsters/Gnoll lord.md", "Gnoll lord monster.\n");
        WriteArticle("Monsters/Deep/Gnoll king.md", "Gnoll king monster.\n");
        WriteArticle("Races/Gnoll.md", "Gnoll race.\n");
        WriteArticle("Races/Human.md", "Human race.\n");
        WriteArticle("Welcome.md", "Welcome to the wiki.\n");

        var service = CreateServiceForTempDir();
        await AwaitStartup(service);

        var categories = service.GetStatus().Categories;
        Assert.Equal(new[] { "Monsters", "Races", "(root)" }, categories.Select(c => c.Category).ToArray());
        Assert.Equal(new[] { 3, 2, 1 }, categories.Select(c => c.Articles).ToArray());

        long monsterBytes = new[] { "Gnoll monster.\n", "Gnoll lord monster.\n", "Gnoll king monster.\n" }.Sum(s => (long)s.Length);
        Assert.Equal(monsterBytes, categories[0].Bytes);
        Assert.Equal((long)"Welcome to the wiki.\n".Length, categories[2].Bytes);
    }

    [Fact]
    public async Task GetStatus_DuplicateTitles_GroupThePathsSharingATitle()
    {
        WriteArticle("Races/Gnoll.md", "Gnoll race.\n");
        WriteArticle("Monsters/Gnoll.md", "Gnoll monster.\n");
        WriteArticle("Monsters/Dagger.md", "Not a duplicate.\n");

        var service = CreateServiceForTempDir();
        await AwaitStartup(service);

        var status = service.GetStatus();
        Assert.Equal(1, status.DuplicateTitleCount);
        var duplicate = Assert.Single(status.DuplicateTitles);
        Assert.Equal("Gnoll", duplicate.Title);
        Assert.Equal(new[] { "Monsters/Gnoll", "Races/Gnoll" }, duplicate.Paths.ToArray());
    }

    // -- reindex -------------------------------------------------------------------------------------

    [Fact]
    public async Task RequestReindex_FileAddedAfterStartup_IsFoundAfterTheManualPass()
    {
        WriteArticle("Gnoll.md", "Gnolls are common early monsters.\n");

        var service = CreateServiceForTempDir();
        await AwaitStartup(service);
        Assert.Empty(service.GetRelevantContext("snarfblat"));
        Assert.Equal(1L, service.GetStatus().Index!.Generation);

        WriteArticle("Items/Snarfblat.md", "A snarfblat is a curious tool.\n");

        var result = service.RequestReindex(CorpusIndexTrigger.Manual, out Task? completion);
        Assert.Equal(CorpusIndexStartResult.Started, result);
        Assert.NotNull(completion);
        await completion!.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

        var hit = Assert.Single(service.GetRelevantContext("snarfblat").ToList());
        Assert.Contains("Items/Snarfblat.md", hit);

        var status = service.GetStatus();
        Assert.Equal(2L, status.Index!.Generation);
        Assert.Equal(2, status.Index.FileCount);
        Assert.Equal(service.LastStartedJobId, status.RecentJobs[0].Id);
        Assert.Equal(CorpusIndexTrigger.Manual, status.RecentJobs[0].Trigger);
        Assert.Equal(CorpusIndexOutcome.Succeeded, status.RecentJobs[0].Outcome);
        Assert.Equal(CorpusIndexTrigger.Startup, status.RecentJobs[1].Trigger);
        Assert.Null(status.CurrentJob);
    }

    [Fact]
    public async Task GetStatus_ConfiguredFolder_ReportsGitPollRefresh()
    {
        WriteArticle("Gnoll.md", "Gnolls are common early monsters.\n");

        var service = CreateServiceForTempDir();
        await AwaitStartup(service);

        var status = service.GetStatus();
        Assert.Equal(CorpusKind.GnollHackWiki, status.Kind);
        Assert.Equal("WikiPath", status.ConfigKey);
        Assert.True(status.Configured);
        Assert.Equal(_tempDir, status.Path);
        Assert.True(status.PathExists);
        Assert.False(status.IsGitRepository);
        Assert.Null(status.CurrentHeadSha);
        Assert.True(status.IsIndexingComplete);
        Assert.Equal(CorpusRefreshMode.GitPoll, status.RefreshMode);
        Assert.Equal(10, status.CheckIntervalMinutes);
        Assert.NotNull(status.NextCheckAtUtc);
        Assert.True(status.UpToDate);
    }

    // -- configuration -------------------------------------------------------------------------------

    [Fact]
    public void GetStatus_UnsetWikiPath_IsNotConfiguredAndReportsTheBuiltInDefault()
    {
        // The startup pass reads c:\wiki when it exists on this machine; nothing here depends on it.
        var service = CreateService();

        var status = service.GetStatus();
        Assert.False(status.Configured);
        Assert.Equal("c:\\wiki", status.Path);
    }

    [Fact]
    public async Task RequestReindex_BlankWikiPath_IsNotConfigured()
    {
        var service = CreateService(("WikiPath", ""));
        await AwaitStartup(service);

        Assert.Equal(CorpusIndexStartResult.NotConfigured, service.RequestReindex());

        var status = service.GetStatus();
        Assert.False(status.Configured);
        Assert.Null(status.Path);
        Assert.Null(status.Index);
        var job = Assert.Single(status.RecentJobs);
        Assert.Equal(CorpusIndexOutcome.Failed, job.Outcome);
        Assert.Equal("WikiPath is not configured.", job.Message);
    }

    [Fact]
    public async Task RequestReindex_MissingFolder_IsPathMissingAndTheStartupJobFailed()
    {
        string missing = Path.Combine(_tempDir, "missing");
        var service = CreateService(("WikiPath", missing));
        await AwaitStartup(service);

        Assert.Equal(CorpusIndexStartResult.PathMissing, service.RequestReindex());

        var status = service.GetStatus();
        Assert.True(status.Configured);
        Assert.False(status.PathExists);
        Assert.True(status.IsIndexingComplete);
        Assert.Null(status.Index);
        Assert.False(status.UpToDate);
        var job = Assert.Single(status.RecentJobs);
        Assert.Equal(CorpusIndexTrigger.Startup, job.Trigger);
        Assert.Equal(CorpusIndexOutcome.Failed, job.Outcome);
        Assert.Equal("The WikiPath folder does not exist.", job.Message);
        Assert.NotNull(job.FinishedAtUtc);
    }
}
