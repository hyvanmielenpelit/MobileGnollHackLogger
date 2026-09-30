using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// <see cref="BenchmarkReportChartStore"/> against a throwaway temp root: the on-disk layout, whole-set
/// replacement, upload validation, checksum-guarded loads, path containment and the maintenance clear.
/// </summary>
public class BenchmarkReportChartStoreTests
{
    private static readonly string HashA = new('a', 64);
    private static readonly string HashB = new('b', 64);

    private static ReportDocumentChartUpload Upload(
        string figureKey,
        string naming = BenchmarkReportChartStore.Named,
        int width = 640,
        int height = 400,
        string? hash = null,
        byte shade = 0)
        => new()
        {
            FigureKey = figureKey,
            Naming = naming,
            Title = $"Title of {figureKey}",
            Caption = $"Caption of {figureKey}",
            AltText = $"Alt text of {figureKey}",
            SettingsHash = hash ?? HashA,
            PngBase64 = TestPngs.MakeBase64(width, height, shade)
        };

    private static async Task<ReportDocumentChartsSummaryDto> SetAsync(
        BenchmarkReportChartStore store,
        long documentId,
        params ReportDocumentChartUpload[] uploads)
        => await store.SetChartsAsync(documentId, BenchmarkReportChartStore.ValidateCharts(uploads), TestContext.Current.CancellationToken);

    private static void AssertNoStagingLeft(string root)
    {
        string staging = Path.Combine(root, BenchmarkReportChartStore.StagingFolderName);
        Assert.True(!Directory.Exists(staging) || !Directory.EnumerateFileSystemEntries(staging).Any(),
            "A staging folder was left behind.");
    }

    // --- Layout ------------------------------------------------------------------------------------

    [Fact]
    public async Task SetCharts_WritesOneFolderWithImagesAndManifest()
    {
        using var charts = TestChartStores.InTempFolder();

        var summary = await SetAsync(charts.Store, 42,
            Upload("p1a-quality"),
            Upload("p1a-quality", BenchmarkReportChartStore.Anonymized, shade: 9),
            Upload("s1-quality-speed", width: 800, height: 800));

        string folder = Path.Combine(charts.Root, "42");
        Assert.True(File.Exists(Path.Combine(folder, "p1a-quality.named.png")));
        Assert.True(File.Exists(Path.Combine(folder, "p1a-quality.anonymized.png")));
        Assert.True(File.Exists(Path.Combine(folder, "s1-quality-speed.named.png")));
        Assert.Equal(4, Directory.GetFiles(folder).Length);
        AssertNoStagingLeft(charts.Root);

        byte[] manifestBytes = File.ReadAllBytes(Path.Combine(folder, BenchmarkReportChartStore.ManifestFileName));
        Assert.False(manifestBytes.Length >= 3 && manifestBytes[0] == 0xEF && manifestBytes[1] == 0xBB && manifestBytes[2] == 0xBF,
            "The manifest must not start with a byte order mark.");

        using var json = JsonDocument.Parse(manifestBytes);
        var rootElement = json.RootElement;
        Assert.Equal(1, rootElement.GetProperty("version").GetInt32());
        Assert.Equal(42, rootElement.GetProperty("documentId").GetInt64());
        Assert.Equal(HashA, rootElement.GetProperty("settingsHash").GetString());
        var entries = rootElement.GetProperty("charts").EnumerateArray().ToList();
        Assert.Equal(3, entries.Count);
        Assert.Equal("p1a-quality.named.png", entries[0].GetProperty("file").GetString());
        Assert.Equal("anonymized", entries[1].GetProperty("naming").GetString());
        Assert.Equal(800, entries[2].GetProperty("widthPx").GetInt32());
        Assert.Equal("Alt text of s1-quality-speed", entries[2].GetProperty("altText").GetString());
        Assert.Equal(
            BenchmarkReportChartStore.Sha256Hex(File.ReadAllBytes(Path.Combine(folder, "p1a-quality.named.png"))),
            entries[0].GetProperty("sha256").GetString());

        Assert.Equal(42, summary.DocumentId);
        Assert.Equal(3, summary.ChartCount);
        Assert.Equal(new[] { "p1a-quality", "s1-quality-speed" }, summary.FigureKeys);
        Assert.Equal(HashA, summary.SettingsHash);

        var read = charts.Store.ReadSummary(42);
        Assert.NotNull(read);
        Assert.Equal(summary.FigureKeys, read!.FigureKeys);
        Assert.Equal(3, read.ChartCount);
        Assert.Null(charts.Store.ReadSummary(43));
    }

    [Fact]
    public async Task LoadAsync_ReturnsOneNamingInManifestOrder()
    {
        using var charts = TestChartStores.InTempFolder();
        await SetAsync(charts.Store, 7,
            Upload("s2-quality-cost"),
            Upload("p1a-quality"),
            Upload("p1a-quality", BenchmarkReportChartStore.Anonymized, width: 1024, height: 512));

        var named = await charts.Store.LoadAsync(7, BenchmarkReportChartStore.Named, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "s2-quality-cost", "p1a-quality" }, named.Select(c => c.FigureKey));
        Assert.Equal("Title of s2-quality-cost", named[0].Title);
        Assert.Equal("Caption of s2-quality-cost", named[0].Caption);
        Assert.Equal(640, named[0].WidthPx);
        Assert.Equal(400, named[0].HeightPx);
        Assert.Equal(BenchmarkReportChartStore.Sha256Hex(named[0].Png), named[0].Sha256);

        var anonymized = await charts.Store.LoadAsync(7, BenchmarkReportChartStore.Anonymized, TestContext.Current.CancellationToken);
        var single = Assert.Single(anonymized);
        Assert.Equal(1024, single.WidthPx);
        Assert.Equal(512, single.HeightPx);

        Assert.Empty(await charts.Store.LoadAsync(8, BenchmarkReportChartStore.Named, TestContext.Current.CancellationToken));
        Assert.Empty(await charts.Store.LoadAsync(7, "other", TestContext.Current.CancellationToken));
    }

    // --- Replacement ---------------------------------------------------------------------------------

    [Fact]
    public async Task SetCharts_ReplacesTheWholeSet()
    {
        using var charts = TestChartStores.InTempFolder();
        await SetAsync(charts.Store, 3, Upload("p1a-quality"), Upload("p1b-speed"));

        var summary = await SetAsync(charts.Store, 3, Upload("p2-profile", hash: HashB));

        string folder = Path.Combine(charts.Root, "3");
        Assert.Equal(
            new[] { "manifest.json", "p2-profile.named.png" },
            Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(HashB, summary.SettingsHash);
        Assert.Equal(new[] { "p2-profile" }, charts.Store.ReadSummary(3)!.FigureKeys);
        AssertNoStagingLeft(charts.Root);
    }

    /// <summary>Fails the Nth file write once armed; zero writes normally.</summary>
    private sealed class FailingChartStore : BenchmarkReportChartStore
    {
        private int _writes;

        public FailingChartStore(IConfiguration configuration)
            : base(configuration)
        {
        }

        public int FailOnWrite { get; set; }

        protected override Task WriteFileAsync(string path, byte[] contents, CancellationToken cancellationToken)
        {
            if (FailOnWrite > 0 && ++_writes == FailOnWrite)
            {
                throw new IOException("Simulated disk failure.");
            }

            return base.WriteFileAsync(path, contents, cancellationToken);
        }
    }

    [Fact]
    public async Task SetCharts_FailureMidWrite_KeepsTheOldSetAndLeavesNoStagingFolder()
    {
        using var charts = TestChartStores.InTempFolder(configuration => new FailingChartStore(configuration));
        var store = (FailingChartStore)charts.Store;
        await SetAsync(store, 5, Upload("p1a-quality"), Upload("p1c-cost"));
        var before = await store.LoadAsync(5, BenchmarkReportChartStore.Named, TestContext.Current.CancellationToken);

        store.FailOnWrite = 2;
        await Assert.ThrowsAsync<IOException>(() => SetAsync(store, 5,
            Upload("s1-quality-speed", hash: HashB), Upload("s2-quality-cost", hash: HashB), Upload("s3-speed-cost", hash: HashB)));

        var after = await store.LoadAsync(5, BenchmarkReportChartStore.Named, TestContext.Current.CancellationToken);
        Assert.Equal(before.Select(c => c.FigureKey), after.Select(c => c.FigureKey));
        Assert.Equal(before.Select(c => c.Sha256), after.Select(c => c.Sha256));
        Assert.Equal(HashA, store.ReadSummary(5)!.SettingsHash);
        AssertNoStagingLeft(charts.Root);
    }

    [Fact]
    public async Task DeleteCharts_RemovesTheFolder_AndAMissingFolderIsFine()
    {
        using var charts = TestChartStores.InTempFolder();
        await SetAsync(charts.Store, 11, Upload("p1a-quality"));

        Assert.True(await charts.Store.DeleteChartsAsync(11, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.Combine(charts.Root, "11")));
        Assert.Null(charts.Store.ReadSummary(11));
        Assert.False(await charts.Store.DeleteChartsAsync(11, TestContext.Current.CancellationToken));
    }

    // --- Validation ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("empty", "No charts were sent")]
    [InlineData("too-many", "at most 16")]
    [InlineData("unknown-key", "unknown figure key")]
    [InlineData("crafted-key", "unknown figure key")]
    [InlineData("bad-naming", "must be \"named\" or \"anonymized\"")]
    [InlineData("duplicate", "more than once")]
    [InlineData("no-alt-text", "no alt text")]
    [InlineData("long-title", "a title of 201 characters")]
    [InlineData("long-caption", "a caption of 501 characters")]
    [InlineData("long-alt-text", "alt text of 1001 characters")]
    [InlineData("bad-hash", "not 64 hexadecimal characters")]
    [InlineData("mixed-hash", "different settings hash")]
    [InlineData("bad-base64", "not valid base64")]
    [InlineData("no-image", "no image data")]
    [InlineData("not-png", "not a PNG image")]
    [InlineData("too-narrow", "each side must be 320 to 4096 pixels")]
    [InlineData("too-tall", "each side must be 320 to 4096 pixels")]
    [InlineData("too-many-bytes", "bytes; the limit is")]
    public void ValidateCharts_Refuses(string problem, string expectedMessage)
    {
        var uploads = problem switch
        {
            "empty" => new List<ReportDocumentChartUpload>(),
            "too-many" => Enumerable.Range(0, 17).Select(_ => Upload("p1a-quality")).ToList(),
            "unknown-key" => new List<ReportDocumentChartUpload> { Upload("p9-unknown") },
            "crafted-key" => new List<ReportDocumentChartUpload> { Upload("..\\..\\p1a-quality") },
            "bad-naming" => new List<ReportDocumentChartUpload> { Upload("p1a-quality", "Named") },
            "duplicate" => new List<ReportDocumentChartUpload> { Upload("p1a-quality"), Upload("p1a-quality", shade: 3) },
            "no-alt-text" => new List<ReportDocumentChartUpload> { With(Upload("p1a-quality"), u => u.AltText = "   ") },
            "long-title" => new List<ReportDocumentChartUpload> { With(Upload("p1a-quality"), u => u.Title = new string('t', 201)) },
            "long-caption" => new List<ReportDocumentChartUpload> { With(Upload("p1a-quality"), u => u.Caption = new string('c', 501)) },
            "long-alt-text" => new List<ReportDocumentChartUpload> { With(Upload("p1a-quality"), u => u.AltText = new string('a', 1001)) },
            "bad-hash" => new List<ReportDocumentChartUpload> { Upload("p1a-quality", hash: "abc123") },
            "mixed-hash" => new List<ReportDocumentChartUpload> { Upload("p1a-quality"), Upload("p1b-speed", hash: HashB) },
            "bad-base64" => new List<ReportDocumentChartUpload> { With(Upload("p1a-quality"), u => u.PngBase64 = "not base64 at all!") },
            "no-image" => new List<ReportDocumentChartUpload> { With(Upload("p1a-quality"), u => u.PngBase64 = "") },
            "not-png" => new List<ReportDocumentChartUpload>
            {
                With(Upload("p1a-quality"), u => u.PngBase64 = Convert.ToBase64String(new byte[64]))
            },
            "too-narrow" => new List<ReportDocumentChartUpload> { Upload("p1a-quality", width: 319) },
            "too-tall" => new List<ReportDocumentChartUpload> { Upload("p1a-quality", height: 4097) },
            "too-many-bytes" => new List<ReportDocumentChartUpload>
            {
                With(Upload("p1a-quality"), u => u.PngBase64 = Convert.ToBase64String(Oversized()))
            },
            _ => throw new ArgumentOutOfRangeException(nameof(problem))
        };

        var ex = Assert.Throws<ChartStoreException>(() => BenchmarkReportChartStore.ValidateCharts(uploads));
        Assert.Contains(expectedMessage, ex.Message);
    }

    private static ReportDocumentChartUpload With(ReportDocumentChartUpload upload, Action<ReportDocumentChartUpload> change)
    {
        change(upload);
        return upload;
    }

    /// <summary>A valid PNG padded past the 4 MB limit.</summary>
    private static byte[] Oversized()
    {
        byte[] png = TestPngs.Make(400, 400);
        var padded = new byte[BenchmarkReportChartStore.MaxPngBytes + 1];
        Buffer.BlockCopy(png, 0, padded, 0, png.Length);
        return padded;
    }

    [Fact]
    public void ValidateCharts_AcceptsBoundarySizes_ADataUrlPrefix_AndLowercasesTheHash()
    {
        var uploads = new List<ReportDocumentChartUpload>
        {
            Upload("p1a-quality", width: 320, height: 4096, hash: new string('A', 64)),
            With(Upload("p1b-speed", width: 4096, height: 320, hash: new string('a', 64)),
                u => u.PngBase64 = "data:image/png;base64," + u.PngBase64)
        };

        var validated = BenchmarkReportChartStore.ValidateCharts(uploads);

        Assert.Equal(2, validated.Count);
        Assert.All(validated, v => Assert.Equal(new string('a', 64), v.SettingsHash));
        Assert.Equal((320, 4096), (validated[0].WidthPx, validated[0].HeightPx));
        Assert.Equal((4096, 320), (validated[1].WidthPx, validated[1].HeightPx));
        Assert.Equal(BenchmarkReportChartStore.Sha256Hex(validated[1].Png), validated[1].Sha256);
    }

    // --- Loads that skip -----------------------------------------------------------------------------

    [Fact]
    public async Task LoadAsync_SkipsAFileWhoseChecksumDiffers_AndAMissingFile()
    {
        using var charts = TestChartStores.InTempFolder();
        await SetAsync(charts.Store, 9, Upload("p1a-quality"), Upload("p1b-speed"), Upload("p1c-cost"));

        string folder = Path.Combine(charts.Root, "9");
        File.WriteAllBytes(Path.Combine(folder, "p1a-quality.named.png"), TestPngs.Make(640, 400, shade: 200));
        File.Delete(Path.Combine(folder, "p1c-cost.named.png"));

        var loaded = await charts.Store.LoadAsync(9, BenchmarkReportChartStore.Named, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "p1b-speed" }, loaded.Select(c => c.FigureKey));
    }

    [Fact]
    public async Task LoadAsync_UnreadableManifest_LoadsNothing()
    {
        using var charts = TestChartStores.InTempFolder();
        await SetAsync(charts.Store, 10, Upload("p1a-quality"));
        File.WriteAllText(Path.Combine(charts.Root, "10", BenchmarkReportChartStore.ManifestFileName), "{ not json");

        Assert.Empty(await charts.Store.LoadAsync(10, BenchmarkReportChartStore.Named, TestContext.Current.CancellationToken));
        Assert.Null(charts.Store.ReadSummary(10));
    }

    // --- Path containment ----------------------------------------------------------------------------

    [Theory]
    [InlineData("..\\..\\evil", "named")]
    [InlineData("../../evil", "named")]
    [InlineData("p1a-quality", "..\\..\\evil")]
    [InlineData("p1a-quality", "named\\..\\..\\..")]
    [InlineData("C:\\Windows\\evil", "named")]
    [InlineData("", "named")]
    public void GetChartFilePath_RefusesACraftedKeyOrNaming(string figureKey, string naming)
    {
        using var charts = TestChartStores.InTempFolder();

        Assert.Throws<ChartStoreException>(() => charts.Store.GetChartFilePath(1, figureKey, naming));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetDocumentFolder_RefusesANonPositiveId(long documentId)
    {
        using var charts = TestChartStores.InTempFolder();

        Assert.Throws<ChartStoreException>(() => charts.Store.GetDocumentFolder(documentId));
    }

    [Fact]
    public void GetChartFilePath_ForAKnownKey_IsInsideTheRoot()
    {
        using var charts = TestChartStores.InTempFolder();

        string path = charts.Store.GetChartFilePath(12, "s3-speed-cost", BenchmarkReportChartStore.Anonymized);

        Assert.Equal(Path.Combine(charts.Root, "12", "s3-speed-cost.anonymized.png"), path);
    }

    // --- Clear ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ClearAll_DryRunCountsEverything_AndARealRunDeletesOnlyChartFolders()
    {
        using var charts = TestChartStores.InTempFolder();
        await SetAsync(charts.Store, 1, Upload("p1a-quality"));
        await SetAsync(charts.Store, 2, Upload("p1a-quality"), Upload("p1b-speed"));

        string staging = Path.Combine(charts.Root, BenchmarkReportChartStore.StagingFolderName, "3-abandoned");
        Directory.CreateDirectory(staging);
        File.WriteAllBytes(Path.Combine(staging, "p1a-quality.named.png"), TestPngs.Make(320, 320));

        string foreignFile = Path.Combine(charts.Root, "readme.txt");
        string foreignFolder = Path.Combine(charts.Root, "keep");
        string paddedNumber = Path.Combine(charts.Root, "007");
        File.WriteAllText(foreignFile, "not a chart");
        Directory.CreateDirectory(foreignFolder);
        File.WriteAllText(Path.Combine(foreignFolder, "inside.txt"), "not a chart either");
        Directory.CreateDirectory(paddedNumber);

        long expectedBytes = new[] { Path.Combine(charts.Root, "1"), Path.Combine(charts.Root, "2"), staging }
            .SelectMany(d => Directory.GetFiles(d, "*", SearchOption.AllDirectories))
            .Sum(f => new FileInfo(f).Length);

        var dryRun = await charts.Store.ClearAllAsync(true, new long[] { 1 }, TestContext.Current.CancellationToken);

        Assert.Equal(3, dryRun.FolderCount);
        Assert.Equal(6, dryRun.FileCount);
        Assert.Equal(expectedBytes, dryRun.Bytes);
        Assert.Equal(1, dryRun.OrphanFolderCount);
        Assert.Equal(3, dryRun.LeftAlone.Count);
        Assert.Contains("readme.txt", dryRun.LeftAlone);
        Assert.Contains(dryRun.LeftAlone, e => e.StartsWith("keep", StringComparison.Ordinal));
        Assert.Contains(dryRun.LeftAlone, e => e.StartsWith("007", StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(charts.Root, "1")));
        Assert.True(Directory.Exists(Path.Combine(charts.Root, "2")));
        Assert.True(Directory.Exists(staging));
        Assert.Equal((3, 6, expectedBytes), charts.Store.GetDiskMetrics());

        var real = await charts.Store.ClearAllAsync(false, new long[] { 1 }, TestContext.Current.CancellationToken);

        Assert.Equal(dryRun.FolderCount, real.FolderCount);
        Assert.Equal(dryRun.FileCount, real.FileCount);
        Assert.Equal(dryRun.Bytes, real.Bytes);
        Assert.Equal(1, real.OrphanFolderCount);
        Assert.False(Directory.Exists(Path.Combine(charts.Root, "1")));
        Assert.False(Directory.Exists(Path.Combine(charts.Root, "2")));
        Assert.False(Directory.Exists(Path.Combine(charts.Root, BenchmarkReportChartStore.StagingFolderName)));
        Assert.True(Directory.Exists(charts.Root));
        Assert.True(File.Exists(foreignFile));
        Assert.True(File.Exists(Path.Combine(foreignFolder, "inside.txt")));
        Assert.True(Directory.Exists(paddedNumber));
        Assert.Equal((0, 0, 0L), charts.Store.GetDiskMetrics());
    }

    // --- Unconfigured --------------------------------------------------------------------------------

    public static TheoryData<string?> UnusableRoots => new() { null!, "", "   ", "relative\\charts", "charts" };

    [Theory]
    [MemberData(nameof(UnusableRoots))]
    public async Task UnconfiguredOrRelativeRoot_RefusesWritesAndLoadsNothing(string? root)
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new BenchmarkReportChartStore(TestChartStores.ConfigurationFor(root));
        var validated = BenchmarkReportChartStore.ValidateCharts(new[] { Upload("p1a-quality") });

        Assert.False(store.IsConfigured);
        Assert.Null(store.RootPath);

        var set = await Assert.ThrowsAsync<ChartStoreException>(() => store.SetChartsAsync(1, validated, ct));
        Assert.Equal(BenchmarkReportChartStore.NotConfiguredMessage, set.Message);
        await Assert.ThrowsAsync<ChartStoreException>(() => store.DeleteChartsAsync(1, ct));
        await Assert.ThrowsAsync<ChartStoreException>(() => store.ClearAllAsync(true, Array.Empty<long>(), ct));

        Assert.Empty(await store.LoadAsync(1, BenchmarkReportChartStore.Named, ct));
        Assert.Null(store.ReadSummary(1));
        Assert.Equal((0, 0, 0L), store.GetDiskMetrics());

        if (!string.IsNullOrWhiteSpace(root))
        {
            Assert.False(Directory.Exists(Path.GetFullPath(root)), "A relative root must never be created.");
        }
    }

    [Fact]
    public void Unconfigured_Helper_IsNotConfigured()
    {
        Assert.False(TestChartStores.Unconfigured().IsConfigured);
    }
}
