namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using UglyToad.PdfPig;
using Xunit;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

/// <summary>
/// Report-document charts through <see cref="AdminBenchmarkReportDocumentsController"/> and
/// <see cref="BenchmarkReportRenderService"/> over a chart store in a temp folder: the upload and its
/// refusals, replacement and deletion, the list fields read from the manifest, and the PDF and Word
/// renders that draw the charts of the requested naming.
/// </summary>
public class BenchmarkReportChartsTests
{
    private static readonly string SettingsHash = new('a', 64);

    private static readonly string[] FigureKeys = { "p1a-quality", "p1b-speed", "p2-profile", "s1-quality-speed", "s2-quality-cost" };

    private static readonly Regex FigureMarkerLine = new(@"^\[\[figure:[^\]]+\]\]$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    // --- Upload ------------------------------------------------------------------------------------

    [Fact]
    public async Task PutCharts_StoresTheSet_AndReturnsItsSummary()
    {
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        long id = await AddAsync(db, BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport));
        var controller = Controller(db, charts.Store);

        var ok = Assert.IsType<OkObjectResult>(await controller.PutCharts(id, Request(
            Upload("p1a-quality", BenchmarkReportChartStore.Named),
            Upload("p1a-quality", BenchmarkReportChartStore.Anonymized),
            Upload("s1-quality-speed", BenchmarkReportChartStore.Named),
            Upload("s1-quality-speed", BenchmarkReportChartStore.Anonymized)), ct));

        var summary = Assert.IsType<ReportDocumentChartsSummaryDto>(ok.Value);
        Assert.Equal(id, summary.DocumentId);
        Assert.Equal(4, summary.ChartCount);
        Assert.Equal(new[] { "p1a-quality", "s1-quality-speed" }, summary.FigureKeys);
        Assert.Equal(SettingsHash, summary.SettingsHash);
        Assert.True(File.Exists(Path.Combine(DocumentFolder(charts, id), "p1a-quality.named.png")));
        Assert.True(File.Exists(Path.Combine(DocumentFolder(charts, id), "s1-quality-speed.anonymized.png")));
    }

    [Fact]
    public async Task PutCharts_RefusesEveryInvalidUpload_WithTheStoresMessage_AndWritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        long id = await AddAsync(db, BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport));
        var controller = Controller(db, charts.Store);

        var named = BenchmarkReportChartStore.Named;
        var cases = new List<(string Name, ReportDocumentChartUpload[] Uploads)>
        {
            ("empty", Array.Empty<ReportDocumentChartUpload>()),
            ("too many", Enumerable.Range(0, BenchmarkReportChartStore.MaxCharts + 1).Select(_ => Upload("p1a-quality", named)).ToArray()),
            ("unknown figure key", new[] { Upload("x-unknown", named) }),
            ("unknown naming", new[] { Upload("p1a-quality", "pseudonymous") }),
            ("sent twice", new[] { Upload("p1a-quality", named), Upload("p1a-quality", named) }),
            ("no alt text", new[] { Upload("p1a-quality", named, alt: " ") }),
            ("title too long", new[] { Upload("p1a-quality", named, title: new string('t', BenchmarkReportChartStore.MaxTitleChars + 1)) }),
            ("bad settings hash", new[] { Upload("p1a-quality", named, settingsHash: "abc") }),
            ("mixed settings hashes", new[] { Upload("p1a-quality", named), Upload("p1b-speed", named, settingsHash: new string('b', 64)) }),
            ("not base64", new[] { Upload("p1a-quality", named, pngBase64: "!!!not-base64!!!") }),
            ("not a PNG", new[] { Upload("p1a-quality", named, pngBase64: Convert.ToBase64String(Encoding.UTF8.GetBytes("not a png at all, just text"))) }),
            ("too small", new[] { Upload("p1a-quality", named, width: 100, height: 100) })
        };

        foreach (var (name, uploads) in cases)
        {
            string expected = Assert.Throws<ChartStoreException>(() => BenchmarkReportChartStore.ValidateCharts(uploads)).Message;

            var result = await controller.PutCharts(id, Request(uploads), ct);

            Assert.True(result is BadRequestObjectResult, $"{name}: expected 400, got {result.GetType().Name}.");
            Assert.Equal(expected, ErrorOf(result));
            Assert.Null(charts.Store.ReadSummary(id));
            Assert.False(Directory.Exists(DocumentFolder(charts, id)), $"{name}: a chart folder was written.");
        }

        // A request with no body at all is refused as an empty upload.
        Assert.IsType<BadRequestObjectResult>(await controller.PutCharts(id, null, ct));
    }

    [Fact]
    public async Task PutCharts_RefusesAStandaloneDocument_AndUnconfiguredStorage_AndIsNotFoundForAnUnknownDocument()
    {
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        long compared = await AddAsync(db, BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport));
        long standalone = await AddAsync(db, BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary));
        var request = Request(Upload("p1a-quality", BenchmarkReportChartStore.Named));

        var standaloneResult = await Controller(db, charts.Store).PutCharts(standalone, request, ct);
        Assert.Equal(BenchmarkReportRenderService.StandaloneChartsRefusal, ErrorOf(standaloneResult));
        Assert.False(Directory.Exists(DocumentFolder(charts, standalone)));

        var unconfigured = await Controller(db, TestChartStores.Unconfigured()).PutCharts(compared, request, ct);
        Assert.Equal(BenchmarkReportChartStore.NotConfiguredMessage, ErrorOf(unconfigured));

        Assert.IsType<NotFoundObjectResult>(await Controller(db, charts.Store).PutCharts(compared + standalone + 1000, request, ct));
    }

    [Fact]
    public void TheChartEndpoints_AreRoutedUnderTheDocument_AndThePutAcceptsFortyMegabytes()
    {
        var type = typeof(AdminBenchmarkReportDocumentsController);

        var put = type.GetMethod(nameof(AdminBenchmarkReportDocumentsController.PutCharts))!;
        Assert.Equal("report-documents/{id:long}/charts", put.GetCustomAttribute<HttpPutAttribute>()!.Template);
        var limit = Assert.Single(put.CustomAttributes, a => a.AttributeType == typeof(RequestSizeLimitAttribute));
        Assert.Equal(40_000_000L, Assert.IsType<long>(limit.ConstructorArguments.Single().Value));
        Assert.NotNull(put.GetParameters().Single(p => p.ParameterType == typeof(PutReportDocumentChartsRequest)).GetCustomAttribute<FromBodyAttribute>());

        var delete = type.GetMethod(nameof(AdminBenchmarkReportDocumentsController.DeleteCharts))!;
        Assert.Equal("report-documents/{id:long}/charts", delete.GetCustomAttribute<HttpDeleteAttribute>()!.Template);
    }

    [Fact]
    public async Task PutCharts_ReplacesTheWholeSet()
    {
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        long id = await AddAsync(db, BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport));
        var controller = Controller(db, charts.Store);

        Assert.IsType<OkObjectResult>(await controller.PutCharts(id, Request(
            Upload("p1a-quality", BenchmarkReportChartStore.Named),
            Upload("p1a-quality", BenchmarkReportChartStore.Anonymized),
            Upload("p1b-speed", BenchmarkReportChartStore.Named)), ct));

        string newHash = new('c', 64);
        var ok = Assert.IsType<OkObjectResult>(await controller.PutCharts(id, Request(
            Upload("s2-quality-cost", BenchmarkReportChartStore.Anonymized, settingsHash: newHash)), ct));

        var summary = Assert.IsType<ReportDocumentChartsSummaryDto>(ok.Value);
        Assert.Equal(1, summary.ChartCount);
        Assert.Equal(new[] { "s2-quality-cost" }, summary.FigureKeys);
        Assert.Equal(newHash, charts.Store.ReadSummary(id)!.SettingsHash);
        Assert.Equal(
            new[] { "manifest.json", "s2-quality-cost.anonymized.png" },
            Directory.GetFiles(DocumentFolder(charts, id)).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Empty(await charts.Store.LoadAsync(id, BenchmarkReportChartStore.Named, ct));
    }

    // --- Deletion ----------------------------------------------------------------------------------

    [Fact]
    public async Task DeleteCharts_RemovesTheSet_AndIsNotFoundOnlyForAnUnknownDocument()
    {
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        long id = await AddAsync(db, BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport));
        var controller = Controller(db, charts.Store);
        Assert.IsType<OkObjectResult>(await controller.PutCharts(id, Request(Upload("p1a-quality", BenchmarkReportChartStore.Named)), ct));

        Assert.IsType<NoContentResult>(await controller.DeleteCharts(id, ct));
        Assert.Null(charts.Store.ReadSummary(id));
        Assert.False(Directory.Exists(DocumentFolder(charts, id)));

        // Nothing left to delete is still a success; the document stays.
        Assert.IsType<NoContentResult>(await controller.DeleteCharts(id, ct));
        Assert.True(db.BenchmarkReportDocuments.Any(d => d.Id == id));

        Assert.IsType<NotFoundResult>(await controller.DeleteCharts(id + 1000, ct));
        Assert.IsType<NoContentResult>(await Controller(db, TestChartStores.Unconfigured()).DeleteCharts(id, ct));
    }

    [Fact]
    public async Task DeletingAReportPackDocument_RemovesItsChartFolder_AndNoOther()
    {
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        long deleted = await AddAsync(db, BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport));
        long kept = await AddAsync(db, BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary));
        var controller = Controller(db, charts.Store);
        Assert.IsType<OkObjectResult>(await controller.PutCharts(deleted, Request(Upload("p1a-quality", BenchmarkReportChartStore.Named)), ct));
        Assert.IsType<OkObjectResult>(await controller.PutCharts(kept, Request(Upload("p1a-quality", BenchmarkReportChartStore.Named)), ct));

        Assert.IsType<NoContentResult>(await controller.Delete(deleted, ct));

        Assert.False(Directory.Exists(DocumentFolder(charts, deleted)));
        Assert.True(Directory.Exists(DocumentFolder(charts, kept)));
        Assert.NotNull(charts.Store.ReadSummary(kept));
    }

    // --- List and detail ---------------------------------------------------------------------------

    [Fact]
    public async Task TheListAndDetail_ReadTheChartManifest_AndThePeerLettersFromTheFactSheet()
    {
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        long withCharts = await AddAsync(db, BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport));
        long without = await AddAsync(db, BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary));
        long standalone = await AddAsync(db, BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary));
        var service = Service(db, charts.Store);
        Assert.Null((await service.SetChartsAsync(withCharts, Request(
            Upload("s1-quality-speed", BenchmarkReportChartStore.Named),
            Upload("s1-quality-speed", BenchmarkReportChartStore.Anonymized),
            Upload("p2-profile", BenchmarkReportChartStore.Named)), ct)).Refusal);

        var list = await service.ListAsync(null, null, null, ct);

        var charted = list.Single(d => d.Id == withCharts);
        Assert.Equal(3, charted.ChartCount);
        Assert.Equal(new[] { "s1-quality-speed", "p2-profile" }, charted.ChartFigureKeys);
        Assert.Equal(SettingsHash, charted.ChartSettingsHash);

        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport).FactsJson);
        var expectedLetters = sheet.Peers.ToDictionary(p => p.EntryKey, p => p.Letter);
        Assert.Equal(new Dictionary<string, string> { ["run:14"] = "A", ["run:13"] = "B" }, expectedLetters);
        Assert.Equal(expectedLetters, charted.PeerLetters);

        var plain = list.Single(d => d.Id == without);
        Assert.Equal(0, plain.ChartCount);
        Assert.Empty(plain.ChartFigureKeys);
        Assert.Null(plain.ChartSettingsHash);
        Assert.Equal(expectedLetters, plain.PeerLetters);

        Assert.Empty(list.Single(d => d.Id == standalone).PeerLetters);

        var detail = await service.GetAsync(withCharts, ct);
        Assert.NotNull(detail);
        Assert.Equal(3, detail!.ChartCount);
        Assert.Equal(new[] { "s1-quality-speed", "p2-profile" }, detail.ChartFigureKeys);
        Assert.Equal(SettingsHash, detail.ChartSettingsHash);
        Assert.Equal(expectedLetters, detail.PeerLetters);

        // Unconfigured storage lists no charts and still reads the letters.
        var bare = Assert.Single(await Service(db, TestChartStores.Unconfigured()).ListAsync(null, null, null, ct), d => d.Id == withCharts);
        Assert.Equal(0, bare.ChartCount);
        Assert.Equal(expectedLetters, bare.PeerLetters);
    }

    [Fact]
    public async Task AComparisonScopeDocument_TakesCharts_AndListsTheLetterOfEveryCoveredModel()
    {
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        long id = await AddAsync(db, BenchmarkReportPackFixture.ComparisonDocument(BenchmarkReportAudience.TechnicalReport, subset: true));

        Assert.IsType<OkObjectResult>(await Controller(db, charts.Store).PutCharts(id, Request(
            Upload("p1a-quality", BenchmarkReportChartStore.Named),
            Upload("p1a-quality", BenchmarkReportChartStore.Anonymized)), ct));

        var item = Assert.Single(await Service(db, charts.Store).ListAsync(
            new BenchmarkReportDocumentListFilter { ComparisonId = BenchmarkReportPackFixture.ComparisonNumber }, ct));
        Assert.Equal(BenchmarkReportScope.Comparison, item.Scope);
        Assert.Equal(new Dictionary<string, string> { ["run:31"] = "A", ["run:35"] = "B" }, item.PeerLetters);
        Assert.Equal(new[] { ("run:31", (string?)"A"), ("run:35", (string?)"B") }, item.CoveredModels.Select(m => (m.EntryKey, m.Letter)));
        Assert.False(item.CoversAllEntries);
        Assert.Equal(BenchmarkReportPackFixture.ComparisonName, item.ComparisonName);
        Assert.Equal(2, item.ChartCount);
    }

    [Fact]
    public async Task AComparisonScopeRender_PlacesItsChartsInThePdfAndWordCopies()
    {
        BenchmarkPdfTestSetup.Configure();
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        var controller = Controller(db, charts.Store);
        long id = await AddAsync(db, BenchmarkReportPackFixture.ComparisonDocument(BenchmarkReportAudience.ExecutiveSummary));

        Assert.IsType<OkObjectResult>(await controller.PutCharts(id, Request(
            Upload("p1a-quality", BenchmarkReportChartStore.Anonymized, alt: "Anonymized chart p1a-quality"),
            Upload("s2-quality-cost", BenchmarkReportChartStore.Anonymized, alt: "Anonymized chart s2-quality-cost")), ct));

        var docx = Assert.IsType<FileContentResult>(await controller.RenderDocx(id, "full", "anonymized", null, ct));
        Assert.Equal(new[] { "Anonymized chart p1a-quality", "Anonymized chart s2-quality-cost" }, FigureDescriptions(docx.FileContents));

        var pdf = Assert.IsType<FileContentResult>(await controller.RenderPdf(id, "full", "anonymized", null, ct));
        using var reader = PdfDocument.Open(pdf.FileContents);
        Assert.Equal(reader.NumberOfPages + 2, reader.GetPages().Sum(p => p.GetImages().Count()));
    }

    // --- Layout ------------------------------------------------------------------------------------

    private static BenchmarkReportChartLayout RowLayout(params string[] keys) => new()
    {
        Figures = keys.Select(k => new BenchmarkReportChartLayoutFigure { Key = k, WidthShare = 0.5, RowGroup = 1 }).ToList(),
        MaxHeightShare = 0.5
    };

    [Fact]
    public async Task PutCharts_RefusesAnInvalidLayout_WithTheStoresMessage_AndWritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        long id = await AddAsync(db, BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport));
        var controller = Controller(db, charts.Store);

        var layouts = new[]
        {
            new BenchmarkReportChartLayout { Figures = { new BenchmarkReportChartLayoutFigure { Key = "p1a-quality", WidthShare = 0 } } },
            new BenchmarkReportChartLayout { Figures = { new BenchmarkReportChartLayoutFigure { Key = "p1a-quality", WidthShare = 1.5 } } },
            new BenchmarkReportChartLayout { MaxHeightShare = 0.1 },
            new BenchmarkReportChartLayout { MaxHeightShare = 0.95 },
            new BenchmarkReportChartLayout { Version = 2 }
        };
        foreach (var layout in layouts)
        {
            string expected = Assert.Throws<ChartStoreException>(() => BenchmarkReportChartStore.ValidateLayout(layout)).Message;
            var request = Request(Upload("p1a-quality", BenchmarkReportChartStore.Named));
            request.Layout = layout;

            var result = await controller.PutCharts(id, request, ct);

            Assert.Equal(expected, ErrorOf(result));
            Assert.False(Directory.Exists(DocumentFolder(charts, id)), "A chart folder was written for an invalid layout.");
        }
    }

    [Fact]
    public async Task PutCharts_StoresTheLayout_TheRendersPlaceByIt_AndAPutWithoutOneClearsIt()
    {
        BenchmarkPdfTestSetup.Configure();
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        var controller = Controller(db, charts.Store);
        long id = await AddAsync(db, BenchmarkReportPackFixture.ComparisonDocument(BenchmarkReportAudience.ExecutiveSummary));

        var request = Request(
            Upload("p1a-quality", BenchmarkReportChartStore.Anonymized, alt: "Anonymized chart p1a-quality"),
            Upload("s2-quality-cost", BenchmarkReportChartStore.Anonymized, alt: "Anonymized chart s2-quality-cost"));
        request.Layout = RowLayout("p1a-quality", "s2-quality-cost");
        Assert.IsType<OkObjectResult>(await controller.PutCharts(id, request, ct));

        var stored = charts.Store.ReadLayout(id);
        Assert.NotNull(stored);
        Assert.Equal(0.5, stored!.MaxHeightShare);
        Assert.Equal(new[] { "p1a-quality", "s2-quality-cost" }, stored.Figures.Select(f => f.Key));

        // Word: the two figures are one borderless table row; PDF: two images side by side on one page.
        var docx = Assert.IsType<FileContentResult>(await controller.RenderDocx(id, "full", "anonymized", null, ct));
        Assert.Equal(2, FigureRowCells(docx.FileContents));
        var pdf = Assert.IsType<FileContentResult>(await controller.RenderPdf(id, "full", "anonymized", null, ct));
        var images = PdfChartImages(pdf.FileContents);
        Assert.Equal(2, images.Count);
        Assert.Equal(images[0].Page, images[1].Page);
        Assert.Equal(images[0].Top, images[1].Top, 1.0);

        // The client sends a layout whenever it has one; a PUT without one stores none.
        Assert.IsType<OkObjectResult>(await controller.PutCharts(id, Request(
            Upload("p1a-quality", BenchmarkReportChartStore.Anonymized),
            Upload("s2-quality-cost", BenchmarkReportChartStore.Anonymized)), ct));
        Assert.Null(charts.Store.ReadLayout(id));
        var plain = Assert.IsType<FileContentResult>(await controller.RenderDocx(id, "full", "anonymized", null, ct));
        Assert.Equal(0, FigureRowCells(plain.FileContents));
    }

    /// <summary>The cells of the Word document's figure row: a table holding pictures; 0 without one.</summary>
    private static int FigureRowCells(byte[] docx)
    {
        using var package = WordprocessingDocument.Open(new MemoryStream(docx), false);
        var table = package.MainDocumentPart!.Document!.Body!
            .Elements<DocumentFormat.OpenXml.Wordprocessing.Table>()
            .SingleOrDefault(t => t.Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>().Any());
        return table == null
            ? 0
            : table.Descendants<DocumentFormat.OpenXml.Wordprocessing.TableCell>().Count(c => c.Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>().Any());
    }

    /// <summary>The chart images of a PDF, larger than the frame's logo and emblem, left to right.</summary>
    private static List<(int Page, double Top, double Left)> PdfChartImages(byte[] pdf)
    {
        using var reader = PdfDocument.Open(pdf);
        return reader.GetPages()
            .SelectMany(p => p.GetImages().Where(i => i.BoundingBox.Width > 150).Select(i => (Page: p.Number, Top: i.BoundingBox.Top, Left: i.BoundingBox.Left)))
            .OrderBy(i => i.Left)
            .ToList();
    }

    // --- Renders -----------------------------------------------------------------------------------

    [Fact]
    public async Task ANativeRender_DrawsOnlyTheChartsOfItsNaming_AndTheMarkdownRenderDrawsNone()
    {
        BenchmarkPdfTestSetup.Configure();
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        var controller = Controller(db, charts.Store);

        var uploads = FigureKeys.SelectMany(key => new[]
        {
            Upload(key, BenchmarkReportChartStore.Named, shade: 30, alt: "Named chart " + key),
            Upload(key, BenchmarkReportChartStore.Anonymized, shade: 220, alt: "Anonymized chart " + key)
        }).ToArray();

        // The first stored document and disclosure whose anonymized render places a figure.
        (long Id, BenchmarkReportDisclosure Disclosure, int Markers)? found = null;
        foreach (var audience in new[] { BenchmarkReportAudience.TechnicalReport, BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.InternalBrief })
        {
            var document = BenchmarkReportPackFixture.Document(audience);
            long id = await AddAsync(db, document);
            Assert.IsType<OkObjectResult>(await controller.PutCharts(id, Request(uploads), ct));
            var anonymized = await charts.Store.LoadAsync(id, BenchmarkReportChartStore.Anonymized, ct);
            Assert.Equal(FigureKeys.Length, anonymized.Count);

            foreach (var disclosure in BenchmarkReportPackRenderer.AllowedDisclosures(audience))
            {
                var options = new BenchmarkReportRenderOptions
                {
                    Disclosure = disclosure,
                    PeerNaming = BenchmarkReportPeerNaming.Anonymized,
                    IncludeFrontMatter = false,
                    IncludeDocumentFooter = false,
                    IncludeFactSheet = false,
                    Charts = anonymized
                };
                if (found == null && BenchmarkReportPackRenderer.IsAllowed(audience, options))
                {
                    int markers = FigureMarkerLine.Matches(BenchmarkReportPackRenderer.Render(document, options)).Count;
                    if (markers > 0) found = (id, disclosure, markers);
                }
            }
        }
        Assert.True(found != null, "No stored document places a figure in its anonymized render.");
        var (documentId, level, expected) = found!.Value;
        string disclosureName = level.ToString().ToLowerInvariant();

        var anonymizedDocx = Assert.IsType<FileContentResult>(await controller.RenderDocx(documentId, disclosureName, "anonymized", null, ct));
        var anonymizedFigures = FigureDescriptions(anonymizedDocx.FileContents);
        Assert.Equal(expected, anonymizedFigures.Count);
        Assert.All(anonymizedFigures, d => Assert.StartsWith("Anonymized chart ", d, StringComparison.Ordinal));

        var namedDocx = Assert.IsType<FileContentResult>(await controller.RenderDocx(documentId, disclosureName, "named", null, ct));
        Assert.All(FigureDescriptions(namedDocx.FileContents), d => Assert.StartsWith("Named chart ", d, StringComparison.Ordinal));

        var anonymizedPdf = Assert.IsType<FileContentResult>(await controller.RenderPdf(documentId, disclosureName, "anonymized", null, ct));
        using (var reader = PdfDocument.Open(anonymizedPdf.FileContents))
        {
            // One frame image per page (the logo, then the emblem) and one per figure.
            Assert.Equal(reader.NumberOfPages + expected, reader.GetPages().Sum(p => p.GetImages().Count()));
            Assert.Contains("Figure1.", string.Concat(reader.GetPages().Select(p => p.Text)).Replace(" ", string.Empty, StringComparison.Ordinal));
        }

        var markdown = Assert.IsType<ContentResult>(await controller.Render(documentId, disclosureName, "anonymized", ct));
        Assert.DoesNotContain("[[figure:", markdown.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoChartForTheRequestedNaming_MeansNoFigure()
    {
        var ct = TestContext.Current.CancellationToken;
        using var charts = TestChartStores.InTempFolder();
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        var controller = Controller(db, charts.Store);
        long id = await AddAsync(db, BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport));
        Assert.IsType<OkObjectResult>(await controller.PutCharts(id, Request(
            FigureKeys.Select(key => Upload(key, BenchmarkReportChartStore.Anonymized)).ToArray()), ct));

        foreach (var disclosure in BenchmarkReportPackRenderer.AllowedDisclosures(BenchmarkReportAudience.TechnicalReport))
        {
            string name = disclosure.ToString().ToLowerInvariant();
            var docx = Assert.IsType<FileContentResult>(await controller.RenderDocx(id, name, "named", null, ct));

            Assert.Empty(FigureDescriptions(docx.FileContents));
            using var package = WordprocessingDocument.Open(new MemoryStream(docx.FileContents), false);
            Assert.Single(package.MainDocumentPart!.ImageParts);
            Assert.DoesNotContain("[[figure:", string.Concat(package.MainDocumentPart.Document!.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text)), StringComparison.Ordinal);
        }
    }

    // --- Helpers -----------------------------------------------------------------------------------

    private static BenchmarkReportRenderService Service(ApplicationDbContext db, BenchmarkReportChartStore store)
        => new(db, store, NullLogger<BenchmarkReportRenderService>.Instance);

    private static AdminBenchmarkReportDocumentsController Controller(ApplicationDbContext db, BenchmarkReportChartStore store)
        => new(Service(db, store));

    private static async Task<long> AddAsync(ApplicationDbContext db, BenchmarkReportDocument document)
    {
        document.Id = 0;
        db.BenchmarkReportDocuments.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return document.Id;
    }

    private static string DocumentFolder(TempChartStore charts, long id)
        => Path.Combine(charts.Root, id.ToString(CultureInfo.InvariantCulture));

    private static PutReportDocumentChartsRequest Request(params ReportDocumentChartUpload[] uploads) => new() { Charts = uploads.ToList() };

    private static ReportDocumentChartUpload Upload(
        string key, string naming, byte shade = 0, int width = 640, int height = 360,
        string? alt = null, string? title = null, string? settingsHash = null, string? pngBase64 = null) => new()
    {
        FigureKey = key,
        Naming = naming,
        Title = title ?? "Chart " + key,
        Caption = "Caption of " + key + ".",
        AltText = alt ?? "Alt text of " + key + ".",
        SettingsHash = settingsHash ?? SettingsHash,
        PngBase64 = pngBase64 ?? TestPngs.MakeBase64(width, height, shade)
    };

    /// <summary>The <c>{ error }</c> of a 400.</summary>
    private static string? ErrorOf(IActionResult result)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(bad.Value));
        return json.RootElement.GetProperty("error").GetString();
    }

    /// <summary>The alternative text of every figure picture in a Word document, in order.</summary>
    private static List<string> FigureDescriptions(byte[] docx)
    {
        using var package = WordprocessingDocument.Open(new MemoryStream(docx), false);
        return package.MainDocumentPart!.Document!.Body!.Descendants<DW.DocProperties>()
            .Where(p => p.Id!.Value >= Overseer.Services.Benchmarking.Word.BenchmarkWordMarkdownWriter.FirstFigureDrawingId)
            .Select(p => p.Description?.Value ?? string.Empty)
            .ToList();
    }
}
