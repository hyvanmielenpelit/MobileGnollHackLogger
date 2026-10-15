namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.Charts;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// Drawing a stored Root Cause Analysis' charts from its fact sheet: on store through the publisher, and
/// again through the redraw endpoint; a stand-alone document gets the named set only, and another
/// audience is refused.
/// </summary>
public class RootCauseChartRedrawTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public RootCauseChartRedrawTests() => BenchmarkPdfTestSetup.Configure();

    private static async Task<long> StoreAsync(ApplicationDbContext db, BenchmarkReportDocument document)
    {
        document.Id = 0;
        document.Comparison = null;
        document.ComparisonId = null;
        db.BenchmarkReportDocuments.Add(document);
        await db.SaveChangesAsync(Ct);
        return document.Id;
    }

    [Fact]
    public async Task TheRedrawEndpoint_DrawsBothNamingsOfAComparison_FromTheStoredFactSheet()
    {
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        using var charts = TestChartStores.InTempFolder();
        long id = await StoreAsync(db, RootCauseReportFixture.ComparisonDocument());
        var service = new BenchmarkReportRenderService(db, charts.Store, NullLogger<BenchmarkReportRenderService>.Instance, new RootCauseChartPublisher(charts.Store));
        var controller = new AdminBenchmarkReportDocumentsController(service);

        var ok = Assert.IsType<OkObjectResult>(await controller.RedrawCharts(id, CancellationToken.None));

        var summary = Assert.IsType<ReportDocumentChartsSummaryDto>(ok.Value);
        Assert.Equal(2 * BenchmarkReportChartPlacement.ServerFigureKeys.Count, summary.ChartCount);
        var named = await charts.Store.LoadAsync(id, BenchmarkReportChartStore.Named, Ct);
        var anonymized = await charts.Store.LoadAsync(id, BenchmarkReportChartStore.Anonymized, Ct);
        Assert.Equal(BenchmarkReportChartPlacement.ServerFigureKeys.OrderBy(k => k), named.Select(c => c.FigureKey).OrderBy(k => k));
        Assert.Equal(BenchmarkReportChartPlacement.ServerFigureKeys.OrderBy(k => k), anonymized.Select(c => c.FigureKey).OrderBy(k => k));
        Assert.All(anonymized, c => Assert.DoesNotContain("Orion", c.AltText));
    }

    [Fact]
    public async Task AStandaloneRootCauseAnalysis_GetsTheNamedSetOnly_AndRendersItsServerCharts()
    {
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        using var charts = TestChartStores.InTempFolder();
        var document = RootCauseReportFixture.ModelDocument(RootCauseReportFixture.RunSheet(controls: false), BenchmarkReportDocumentOrigin.RunCompletion);
        long id = await StoreAsync(db, document);
        var publisher = new RootCauseChartPublisher(charts.Store);

        int drawn = await publisher.DrawAndStoreAsync(id, BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson), Ct);

        Assert.Equal(BenchmarkReportChartPlacement.ServerFigureKeys.Count, drawn);
        Assert.Equal(drawn, (await charts.Store.LoadAsync(id, BenchmarkReportChartStore.Named, Ct)).Count);
        Assert.Empty(await charts.Store.LoadAsync(id, BenchmarkReportChartStore.Anonymized, Ct));

        var service = new BenchmarkReportRenderService(db, charts.Store, NullLogger<BenchmarkReportRenderService>.Instance, publisher);
        var loaded = await service.LoadRenderChartsAsync(id, BenchmarkReportPeerNaming.Named, Ct);
        Assert.Equal(drawn, loaded.Count);
    }

    [Fact]
    public async Task RedrawingAnExecutiveSummary_IsRefused_AndAnUnknownDocumentIsNotFound()
    {
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        using var charts = TestChartStores.InTempFolder();
        long id = await StoreAsync(db, BenchmarkReportPackFixture.ComparisonDocument(BenchmarkReportAudience.ExecutiveSummary));
        var service = new BenchmarkReportRenderService(db, charts.Store, NullLogger<BenchmarkReportRenderService>.Instance, new RootCauseChartPublisher(charts.Store));

        var refused = await service.RedrawServerChartsAsync(id, Ct);
        Assert.Null(refused.Summary);
        Assert.False(refused.NotFound);
        Assert.Equal(BenchmarkReportRenderService.RedrawNotRootCauseRefusal, refused.Refusal);

        var missing = await service.RedrawServerChartsAsync(id + 1000, Ct);
        Assert.True(missing.NotFound);
    }
}
