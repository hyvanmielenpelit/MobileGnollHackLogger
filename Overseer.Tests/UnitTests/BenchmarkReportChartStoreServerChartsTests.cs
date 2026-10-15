namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// Server-drawn charts in the chart store: a browser upload keeps them, a server redraw keeps the
/// browser's, a browser upload may not carry a server key, and the chart limit counts both.
/// </summary>
public class BenchmarkReportChartStoreServerChartsTests
{
    private const long DocumentId = 4711;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string BrowserKey =
        BenchmarkReportChartPlacement.AllFigureKeys.First(k => !BenchmarkReportChartPlacement.IsServerDrawn(k));

    private static ValidatedChart Server(string key, string naming, byte shade = 10)
        => BenchmarkReportChartStore.ValidateServerChart(key, naming, "Title " + key, "Caption", "Alt " + key, TestPngs.Make(800, 400, shade));

    private static ValidatedChart Browser(string key, string naming, byte shade = 20)
        => BenchmarkReportChartStore.ValidateCharts(new[]
        {
            new ReportDocumentChartUpload
            {
                FigureKey = key,
                Naming = naming,
                Title = "Browser " + key,
                Caption = "Caption",
                AltText = "Alt",
                SettingsHash = new string('a', 64),
                PngBase64 = TestPngs.MakeBase64(800, 400, shade)
            }
        }).Single();

    [Fact]
    public void TheLimit_CountsEightBrowserKindsAndFourServerKinds_InTwoNamings()
    {
        Assert.Equal(24, BenchmarkReportChartStore.MaxCharts);
        Assert.Equal(4, BenchmarkReportChartPlacement.ServerFigureKeys.Count);
        Assert.Equal(17, BenchmarkReportChartPlacement.AllFigureKeys.Count);
        Assert.All(BenchmarkReportChartPlacement.ServerFigureKeys, k => Assert.True(BenchmarkReportChartPlacement.IsKnown(k)));
    }

    [Fact]
    public void ABrowserUpload_MayNotCarryAServerKey()
    {
        Assert.Throws<ChartStoreException>(() => Browser(BenchmarkReportChartPlacement.RcaSpeedKey, BenchmarkReportChartStore.Named));
        Assert.Throws<ChartStoreException>(() => Server(BrowserKey, BenchmarkReportChartStore.Named));
    }

    [Fact]
    public async Task ABrowserUpload_KeepsTheServerDrawnCharts()
    {
        using var temp = TestChartStores.InTempFolder();
        var store = temp.Store;
        await store.SetServerChartsAsync(DocumentId, BenchmarkReportChartPlacement.ServerFigureKeys
            .Select(k => Server(k, BenchmarkReportChartStore.Named)).ToList(), Ct);

        var summary = await store.SetChartsAsync(DocumentId, new[] { Browser(BrowserKey, BenchmarkReportChartStore.Named) }, Ct);

        Assert.Equal(5, summary.ChartCount);
        var loaded = await store.LoadAsync(DocumentId, BenchmarkReportChartStore.Named, Ct);
        Assert.Equal(5, loaded.Count);
        Assert.All(BenchmarkReportChartPlacement.ServerFigureKeys, k => Assert.Contains(loaded, c => c.FigureKey == k));
        Assert.Contains(loaded, c => c.FigureKey == BrowserKey);
    }

    [Fact]
    public async Task AServerRedraw_KeepsTheBrowserCharts_AndReplacesOnlyTheServerOnes()
    {
        using var temp = TestChartStores.InTempFolder();
        var store = temp.Store;
        await store.SetChartsAsync(DocumentId, new[] { Browser(BrowserKey, BenchmarkReportChartStore.Named) }, Ct);
        await store.SetServerChartsAsync(DocumentId, new[] { Server(BenchmarkReportChartPlacement.RcaSpeedKey, BenchmarkReportChartStore.Named, 30) }, Ct);

        await store.SetServerChartsAsync(DocumentId, new[] { Server(BenchmarkReportChartPlacement.RcaCostKey, BenchmarkReportChartStore.Named, 40) }, Ct);

        var loaded = await store.LoadAsync(DocumentId, BenchmarkReportChartStore.Named, Ct);
        Assert.Equal(new[] { BrowserKey, BenchmarkReportChartPlacement.RcaCostKey }.OrderBy(k => k), loaded.Select(c => c.FigureKey).OrderBy(k => k));
    }

    [Fact]
    public async Task AnEmptyServerSet_WithNoBrowserCharts_LeavesNoChart()
    {
        using var temp = TestChartStores.InTempFolder();
        var store = temp.Store;
        await store.SetServerChartsAsync(DocumentId, new[] { Server(BenchmarkReportChartPlacement.RcaSpeedKey, BenchmarkReportChartStore.Named) }, Ct);

        var summary = await store.SetServerChartsAsync(DocumentId, Array.Empty<ValidatedChart>(), Ct);

        Assert.Equal(0, summary.ChartCount);
        Assert.Empty(await store.LoadAsync(DocumentId, BenchmarkReportChartStore.Named, Ct));
    }
}
