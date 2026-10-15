namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The startup step that deletes every stored document of the retired audiences 2 and 3, with its run
/// fingerprint rows and chart folder, and re-settles the runs and battery runs that lost one.
/// </summary>
public class BenchmarkReportAudienceRetirementTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly BenchmarkReportAudience Technical = (BenchmarkReportAudience)2;
    private static readonly BenchmarkReportAudience Internal = (BenchmarkReportAudience)3;

    [Fact]
    public void TheRetiredValues_AreTwoAndThree_AndNoLiveAudienceUsesThem()
    {
        Assert.Equal(new[] { 2, 3 }, BenchmarkReportAudienceRetirement.RetiredAudienceValues);
        Assert.DoesNotContain(Enum.GetValues<BenchmarkReportAudience>(), a => (int)a is 2 or 3);
    }

    [Fact]
    public async Task OnlyAudiencesTwoAndThree_AreDeleted_WithTheirFingerprintRowsAndChartFolders()
    {
        using var connection = SqliteSchemaTemplate.OpenCopy();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        using var charts = TestChartStores.InTempFolder();

        var ids = new Dictionary<int, long>();
        await using (var db = new ApplicationDbContext(options))
        {
            foreach (int value in new[] { 1, 2, 3, 4, 5 })
            {
                var document = Document((BenchmarkReportAudience)value, BenchmarkReportDocumentOrigin.ReportPack, "group:" + value);
                db.BenchmarkReportDocuments.Add(document);
                await db.SaveChangesAsync(Ct);
                ids[value] = document.Id;
            }
        }
        foreach (long id in ids.Values)
        {
            string folder = charts.Store.GetDocumentFolder(id);
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "manifest.json"), "{}", Ct);
        }

        using var provider = Services(options, charts.Store);
        var result = await BenchmarkReportAudienceRetirement.RunAsync(provider, NullLogger.Instance, Ct);

        Assert.Equal(1, result.TechnicalReports);
        Assert.Equal(1, result.InternalBriefs);
        Assert.Equal(2, result.ChartFoldersRemoved);
        Assert.Equal(0, result.Skipped);

        await using (var db = new ApplicationDbContext(options))
        {
            var remaining = await db.BenchmarkReportDocuments.AsNoTracking().IgnoreAutoIncludes()
                .Select(d => (int)d.Audience).OrderBy(a => a).ToListAsync(Ct);
            Assert.Equal(new[] { 1, 4, 5 }, remaining);

            var fingerprintDocuments = await db.Set<BenchmarkReportDocumentRun>().AsNoTracking()
                .Select(r => r.DocumentId).Distinct().ToListAsync(Ct);
            Assert.Equal(new[] { ids[1], ids[4], ids[5] }.OrderBy(i => i), fingerprintDocuments.OrderBy(i => i));
        }

        Assert.False(Directory.Exists(charts.Store.GetDocumentFolder(ids[2])));
        Assert.False(Directory.Exists(charts.Store.GetDocumentFolder(ids[3])));
        foreach (int kept in new[] { 1, 4, 5 })
        {
            Assert.True(Directory.Exists(charts.Store.GetDocumentFolder(ids[kept])));
        }
    }

    [Fact]
    public async Task ASecondCall_DeletesNothing()
    {
        var options = InMemory();
        await using (var db = new ApplicationDbContext(options))
        {
            db.BenchmarkReportDocuments.Add(Document(Technical, BenchmarkReportDocumentOrigin.ReportPack, "group:1"));
            db.BenchmarkReportDocuments.Add(Document(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDocumentOrigin.ReportPack, "group:1"));
            await db.SaveChangesAsync(Ct);
        }

        using var provider = Services(options, TestChartStores.Unconfigured());
        Assert.Equal(1, (await BenchmarkReportAudienceRetirement.RunAsync(provider, NullLogger.Instance, Ct)).Deleted);

        var second = await BenchmarkReportAudienceRetirement.RunAsync(provider, NullLogger.Instance, Ct);
        Assert.Equal(0, second.Deleted);
        Assert.Equal(0, second.RunsResettled);

        await using var check = new ApplicationDbContext(options);
        Assert.Equal(BenchmarkReportAudience.ExecutiveSummary, (await check.BenchmarkReportDocuments.SingleAsync(Ct)).Audience);
    }

    [Fact]
    public async Task ARunWithEveryAudienceLeft_SettlesCompleted_OrWithWarnings()
    {
        var options = InMemory();
        long runId = await SeedRunAsync(options, BenchmarkRunReportDocumentsStatus.Failed, "old message");
        await AddRunDocumentAsync(options, runId, BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDocumentStatus.Completed);
        await AddRunDocumentAsync(options, runId, BenchmarkReportAudience.RootCauseAnalysis, BenchmarkReportDocumentStatus.CompletedWithWarnings);
        await AddRunDocumentAsync(options, runId, Internal, BenchmarkReportDocumentStatus.Completed);

        using var provider = Services(options, TestChartStores.Unconfigured());
        var result = await BenchmarkReportAudienceRetirement.RunAsync(provider, NullLogger.Instance, Ct);

        Assert.Equal(1, result.RunsResettled);
        var run = await RunAsync(options, runId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.CompletedWithWarnings, run.ReportDocumentsStatus);
        Assert.Null(run.ReportDocumentsMessage);
    }

    [Fact]
    public async Task ARunWithOnlyItsExecutiveSummaryLeft_SettlesNotRequested_SayingTheRootCauseAnalysisIsMissing()
    {
        var options = InMemory();
        long runId = await SeedRunAsync(options, BenchmarkRunReportDocumentsStatus.Completed, null);
        await AddRunDocumentAsync(options, runId, BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDocumentStatus.Completed);
        await AddRunDocumentAsync(options, runId, Technical, BenchmarkReportDocumentStatus.Completed);
        await AddRunDocumentAsync(options, runId, Internal, BenchmarkReportDocumentStatus.CompletedWithWarnings);

        using var provider = Services(options, TestChartStores.Unconfigured());
        var result = await BenchmarkReportAudienceRetirement.RunAsync(provider, NullLogger.Instance, Ct);

        Assert.Equal(2, result.Deleted);
        var run = await RunAsync(options, runId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, run.ReportDocumentsStatus);
        Assert.Equal(BenchmarkRunReportDocumentService.RootCauseNotWrittenMessage, run.ReportDocumentsMessage);
    }

    [Fact]
    public async Task ARunWithNothingLeft_SettlesNotRequested_WithNoMessage()
    {
        var options = InMemory();
        long runId = await SeedRunAsync(options, BenchmarkRunReportDocumentsStatus.CompletedWithWarnings, null);
        await AddRunDocumentAsync(options, runId, Technical, BenchmarkReportDocumentStatus.Completed);

        using var provider = Services(options, TestChartStores.Unconfigured());
        await BenchmarkReportAudienceRetirement.RunAsync(provider, NullLogger.Instance, Ct);

        var run = await RunAsync(options, runId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, run.ReportDocumentsStatus);
        Assert.Null(run.ReportDocumentsMessage);
    }

    [Theory]
    [InlineData(BenchmarkRunReportDocumentsStatus.Pending)]
    [InlineData(BenchmarkRunReportDocumentsStatus.Writing)]
    public async Task ARunBeingWritten_KeepsItsDocument_AndItsStatus(BenchmarkRunReportDocumentsStatus status)
    {
        var options = InMemory();
        long runId = await SeedRunAsync(options, status, null);
        await AddRunDocumentAsync(options, runId, Technical, BenchmarkReportDocumentStatus.Completed);

        using var provider = Services(options, TestChartStores.Unconfigured());
        var result = await BenchmarkReportAudienceRetirement.RunAsync(provider, NullLogger.Instance, Ct);

        Assert.Equal(0, result.Deleted);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(status, (await RunAsync(options, runId)).ReportDocumentsStatus);
        await using var db = new ApplicationDbContext(options);
        Assert.Equal(1, await db.BenchmarkReportDocuments.CountAsync(Ct));
    }

    [Fact]
    public async Task ABatteryRunThatLostADocument_IsResettled()
    {
        var options = InMemory();
        long batteryRunId;
        await using (var db = new ApplicationDbContext(options))
        {
            batteryRunId = await BenchmarkBatteryTestData.SeedAsync(db, BenchmarkBatteryTestData.Definition(),
                (BenchmarkBatteryTestData.SuiteARun(1), 0, 1), (BenchmarkBatteryTestData.SuiteBRun(2), 1, 1));
            var batteryRun = await db.BenchmarkBatteryRuns.IgnoreAutoIncludes().SingleAsync(b => b.Id == batteryRunId, Ct);
            batteryRun.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Completed;
            db.BenchmarkReportDocuments.Add(BatteryReportHarness.BatteryDocument(batteryRunId, BenchmarkReportAudience.ExecutiveSummary));
            db.BenchmarkReportDocuments.Add(BatteryReportHarness.BatteryDocument(batteryRunId, Technical));
            await db.SaveChangesAsync(Ct);
        }

        using var provider = Services(options, TestChartStores.Unconfigured());
        var result = await BenchmarkReportAudienceRetirement.RunAsync(provider, NullLogger.Instance, Ct);

        Assert.Equal(1, result.BatteryRunsResettled);
        await using var check = new ApplicationDbContext(options);
        var settled = await check.BenchmarkBatteryRuns.AsNoTracking().IgnoreAutoIncludes().SingleAsync(b => b.Id == batteryRunId, Ct);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, settled.ReportDocumentsStatus);
        Assert.Equal(BenchmarkRunReportDocumentService.RootCauseNotWrittenMessage, settled.ReportDocumentsMessage);
    }

    [Fact]
    public void SettledStatus_CoversAllPresentPartlyPresentAndNonePresent()
    {
        var none = BenchmarkRunReportDocumentService.SettledStatus(Array.Empty<(BenchmarkReportAudience, BenchmarkReportDocumentStatus)>());
        Assert.Equal((BenchmarkRunReportDocumentsStatus.NotRequested, (string?)null), none);

        var onlyRca = BenchmarkRunReportDocumentService.SettledStatus(new[]
        {
            (BenchmarkReportAudience.RootCauseAnalysis, BenchmarkReportDocumentStatus.Completed)
        });
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, onlyRca.Status);
        Assert.Equal("Executive Summary not written yet.", onlyRca.Message);

        var all = BenchmarkRunReportDocumentService.SettledStatus(new[]
        {
            (BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDocumentStatus.Completed),
            (BenchmarkReportAudience.RootCauseAnalysis, BenchmarkReportDocumentStatus.Completed)
        });
        Assert.Equal((BenchmarkRunReportDocumentsStatus.Completed, (string?)null), all);
    }

    // --- Helpers -------------------------------------------------------------------------------------

    private static DbContextOptions<ApplicationDbContext> InMemory()
        => new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static ServiceProvider Services(DbContextOptions<ApplicationDbContext> options, BenchmarkReportChartStore charts)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(options));
        services.AddSingleton(charts);
        services.AddScoped(sp => new BenchmarkReportRenderService(
            sp.GetRequiredService<ApplicationDbContext>(), charts, NullLogger<BenchmarkReportRenderService>.Instance));
        return services.BuildServiceProvider();
    }

    private static BenchmarkReportDocument Document(BenchmarkReportAudience audience, BenchmarkReportDocumentOrigin origin, string subjectKey)
    {
        var document = BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary);
        document.Id = 0;
        document.Audience = audience;
        document.Origin = origin;
        document.SubjectKey = subjectKey;
        document.Runs = new List<BenchmarkReportDocumentRun>
        {
            new() { RunId = 12, ScoringMethodVersion = 9, SynthesisSha256 = "0123456789abcdef" }
        };
        return document;
    }

    private static async Task<long> SeedRunAsync(DbContextOptions<ApplicationDbContext> options, BenchmarkRunReportDocumentsStatus status, string? message)
    {
        await using var db = new ApplicationDbContext(options);
        var run = BenchmarkBatteryTestData.SuiteARun(500);
        run.ReportDocumentsStatus = status;
        run.ReportDocumentsMessage = message;
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(Ct);
        return run.Id;
    }

    private static async Task AddRunDocumentAsync(
        DbContextOptions<ApplicationDbContext> options, long runId, BenchmarkReportAudience audience, BenchmarkReportDocumentStatus status)
    {
        await using var db = new ApplicationDbContext(options);
        var document = Document(audience, BenchmarkReportDocumentOrigin.RunCompletion, BenchmarkRunReportDocumentService.SubjectKeyOf(runId));
        document.Status = status;
        db.BenchmarkReportDocuments.Add(document);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<BenchmarkRun> RunAsync(DbContextOptions<ApplicationDbContext> options, long runId)
    {
        await using var db = new ApplicationDbContext(options);
        return await db.BenchmarkRuns.AsNoTracking().IgnoreAutoIncludes().SingleAsync(r => r.Id == runId, Ct);
    }
}
