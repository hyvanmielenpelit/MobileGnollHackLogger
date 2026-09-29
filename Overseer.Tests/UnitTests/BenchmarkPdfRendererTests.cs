namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.Pdf;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;
using Xunit;

/// <summary>
/// The benchmark PDFs read back with PdfPig: metadata and conformance, the page frame on every
/// page, tables across pages, raw HTML and typography, plain text, the size guard and
/// reproducibility; and the run-file PDF endpoints of <see cref="AdminBenchmarkController"/>.
/// </summary>
public class BenchmarkPdfRendererTests
{
    private static readonly DateTime CreatedAt = new(2026, 9, 28, 10, 42, 0, DateTimeKind.Utc);

    public BenchmarkPdfRendererTests()
    {
        BenchmarkPdfTestSetup.Configure();
    }

    // --- Metadata and conformance ------------------------------------------------------------------

    [Fact]
    public void AReportDocument_CarriesItsMetadata_AndItsStoredCreationDate()
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        var options = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Detailed,
            PeerNaming = BenchmarkReportPeerNaming.Anonymized
        };
        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, options, BenchmarkPdfPaper.A4);

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(
            BenchmarkReportPackRenderer.Render(document, options), info, TestContext.Current.CancellationToken);

        using var reader = PdfDocument.Open(pdf);
        Assert.Equal(document.Title, reader.Information.Title);
        Assert.Equal(BenchmarkPdfRenderer.Author, reader.Information.Author);
        Assert.Equal(info.SubjectLine, reader.Information.Subject);
        Assert.Contains("GPT-5.6 Luna", reader.Information.Keywords);
        Assert.Contains("GnollHack Core Suite", reader.Information.Keywords);
        Assert.Contains("Report for AI Researchers and Developers", reader.Information.Keywords);
        Assert.Contains("GnollBench", reader.Information.Keywords);
        Assert.Equal("Overseer " + BenchmarkPdfRenderer.OverseerVersion, reader.Information.Creator);
        Assert.Equal(BenchmarkReportPackFixture.CreatedAt, reader.Information.GetCreatedDateTimeOffset()!.Value.UtcDateTime);
        Assert.Equal(BenchmarkReportPackFixture.CreatedAt, reader.Information.GetModifiedDateTimeOffset()!.Value.UtcDateTime);
        Assert.Equal("en-US", CatalogString(reader, "Lang"));
        Assert.Equal(BenchmarkPdfClassification.ProviderConfidential, info.Classification);
    }

    [Fact]
    public void APdf_DeclaresPdfA3AndPdfUA1_AndCarriesAStructureTree()
    {
        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown("# Heading\n\nA paragraph.\n\n## Section\n\nMore text.\n", Info(), TestContext.Current.CancellationToken);

        using var reader = PdfDocument.Open(pdf);
        Assert.True(reader.TryGetXmpMetadata(out var xmp), "The PDF carries no XMP metadata.");
        var xml = xmp.GetXDocument();
        Assert.True(HasXmpValue(xml, "http://www.aiim.org/pdfa/ns/id/", "part", "3"), "pdfaid:part is not 3.");
        Assert.True(HasXmpValue(xml, "http://www.aiim.org/pdfua/ns/id/", "part", "1"), "pdfuaid:part is not 1.");
        Assert.True(reader.Structure.Catalog.CatalogDictionary.Data.ContainsKey("StructTreeRoot"), "The catalog has no structure tree.");
    }

    // --- Page frame --------------------------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkPdfClassification.ProviderConfidential, "CONFIDENTIAL — PROVIDER COPY")]
    [InlineData(BenchmarkPdfClassification.Internal, "INTERNAL")]
    public void EveryPage_CarriesTheClassification_AndPageXOfY(BenchmarkPdfClassification classification, string footer)
    {
        var markdown = new StringBuilder();
        for (int i = 1; i <= 120; i++)
        {
            markdown.Append("Paragraph ").Append(i).Append(" carries enough words to take up a line or two of the page body.\n\n");
        }

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown.ToString(), Info(classification), TestContext.Current.CancellationToken);

        using var reader = PdfDocument.Open(pdf);
        int pages = reader.NumberOfPages;
        Assert.True(pages >= 2, $"Expected several pages, got {pages}.");
        foreach (var page in reader.GetPages())
        {
            string text = Squash(page.Text);
            Assert.Contains(Squash(footer), text);
            Assert.Contains(Squash($"Page {page.Number} of {pages}"), text);
        }
    }

    [Fact]
    public void ATableLongerThanAPage_RepeatsItsHeaderRowOnEveryPage()
    {
        var markdown = new StringBuilder("| Alphaheader | Betaheader |\n|---|---:|\n");
        for (int i = 1; i <= 160; i++)
        {
            markdown.Append("| row ").Append(i).Append(" | ").Append(i * 3).Append(" |\n");
        }

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown.ToString(), Info(), TestContext.Current.CancellationToken);

        using var reader = PdfDocument.Open(pdf);
        Assert.True(reader.NumberOfPages >= 2, "The table should run past one page.");
        Assert.All(reader.GetPages(), page =>
        {
            string text = Squash(page.Text);
            Assert.Contains("Alphaheader", text);
            Assert.Contains("Betaheader", text);
        });
    }

    [Fact]
    public void ATableWithTallRows_CrossesPages_AndARowTallerThanAPageStillRenders()
    {
        var markdown = new StringBuilder("| Question | Note |\n|---|---|\n");
        for (int i = 1; i <= 40; i++)
        {
            markdown.Append("| Q").Append(i).Append(" | ")
                .Append(string.Join(" ", Enumerable.Repeat("a note long enough to wrap onto several lines of the cell", 4)))
                .Append(" |\n");
        }
        markdown.Append("| Qhuge | ").Append(string.Join(" ", Enumerable.Repeat("an oversized cell", 1200))).Append(" |\n");

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown.ToString(), Info(), TestContext.Current.CancellationToken);

        using var reader = PdfDocument.Open(pdf);
        Assert.True(reader.NumberOfPages >= 3, $"Expected the table to cross pages, got {reader.NumberOfPages}.");
        string text = AllText(pdf);
        Assert.Contains("Q40", text);
        Assert.Contains("Qhuge", text);
        Assert.All(reader.GetPages(), page => Assert.Contains("Question", Squash(page.Text)));
    }

    [Fact]
    public void ANarrowTable_IsSetAtItsPreferredWidths_AgainstTheLeftMargin()
    {
        const string narrow = "| Measure | Value |\n|---|---|\n| Median answer time | 12.3 s |\n| Cost per question | not available |\n";

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(narrow, Info(), TestContext.Current.CancellationToken);

        using var reader = PdfDocument.Open(pdf);
        var page = reader.GetPage(1);
        var number = page.GetWords().First(w => w.Text.Contains("12.3", StringComparison.Ordinal));
        Assert.True(number.BoundingBox.Right < page.Width / 2, $"The value column ends at {number.BoundingBox.Right} on a page {page.Width} wide.");

        var (_, _, constant) = Layout(narrow);
        Assert.True(constant);
        Assert.False(Layout("| A | B | C | D |\n|---|---|---|---|\n| 1 | 2 | 3 | 4 |\n").Constant);
        Assert.False(Layout("| Note | Value |\n|---|---|\n| " + new string('x', 20) + " " + new string('y', 20) + " " + new string('z', 20) + " | 1 |\n").Constant);
    }

    [Fact]
    public void ANumericColumn_StaysRightAligned_WhenACellIsNotAvailable()
    {
        var (aligns, _, _) = Layout("| Model | Score |\n|---|---|\n| One | 80 |\n| Two | not available |\n");

        Assert.Equal(BenchmarkPdfMarkdownComposer.CellAlign.Right, aligns[1]);
        Assert.True(BenchmarkPdfMarkdownComposer.IsNumericColumn(new[] { "80", "not available", "—" }));
    }

    [Fact]
    public void AReportDocumentsCover_ListsItsFacts_WithoutTheAudience()
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Detailed, PeerNaming = BenchmarkReportPeerNaming.Anonymized };

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, options, BenchmarkPdfPaper.A4);

        Assert.Equal(
            new[] { "Document ID", "Disclosure", "Peers", "Suite", "Questions", "Run", "Created (UTC)", "Report format", "Writer" },
            info.Facts.Select(f => f.Label));
        Assert.Equal("101", info.Facts.Single(f => f.Label == "Document ID").Value);
        Assert.Equal("2, anonymized", info.Facts.Single(f => f.Label == "Peers").Value);
        Assert.Equal("4", info.Facts.Single(f => f.Label == "Questions").Value);
        Assert.Equal("version " + BenchmarkReportPackRenderer.ReportFormatVersion, info.Facts.Single(f => f.Label == "Report format").Value);

        var standalone = BenchmarkPdfDocumentInfo.ForReportDocument(
            BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary),
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named },
            BenchmarkPdfPaper.A4);
        Assert.Equal("none (stand-alone report)", standalone.Facts.Single(f => f.Label == "Peers").Value);
        Assert.Equal("INTERNAL — unpublished benchmark results. Do not share outside the Overseer team.", standalone.ClassificationText);

        var older = BenchmarkReportPackFixture.StoredV2Document(BenchmarkReportAudience.TechnicalReport);
        Assert.Equal("version 2", BenchmarkPdfDocumentInfo.ForReportDocument(older, options, BenchmarkPdfPaper.A4)
            .Facts.Single(f => f.Label == "Report format").Value);
    }

    [Fact]
    public void AReportDocumentPdf_PrintsItsCoverFacts_AndTheLayoutVersion()
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        var options = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Full,
            PeerNaming = BenchmarkReportPeerNaming.Named,
            IncludeFrontMatter = false
        };
        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, options, BenchmarkPdfPaper.A4);

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(BenchmarkReportPackRenderer.Render(document, options), info, TestContext.Current.CancellationToken);

        string text = AllText(pdf);
        Assert.Contains(Squash("Document ID"), text);
        Assert.Contains(Squash("Questions"), text);
        Assert.Contains(Squash("PDF layout 2"), text);
        Assert.DoesNotContain(Squash("Audience"), text);
        // The stamp prints once, in the cover banner.
        Assert.Single(AllIndexesOf(text, Squash("INTERNAL — contains benchmark questions and rubrics.")));
    }

    [Fact]
    public void RawHtml_PrintsAsLiteralText()
    {
        const string markdown = "Before <b>bold</b> and <script>alert(1)</script> after.\n\n<div>A block of html</div>\n";

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown, Info(), TestContext.Current.CancellationToken);

        string text = AllText(pdf);
        Assert.Contains("<b>bold</b>", text);
        Assert.Contains("<script>alert(1)</script>", text);
        Assert.Contains(Squash("<div>A block of html</div>"), text);
    }

    [Fact]
    public void TypographicCharacters_ExtractIntact()
    {
        const string characters = "≥≤→±×—·✓−";
        string markdown = "Scores: a ≥ b, c ≤ d, e → f, 5 ± 1, 3 × 4 — fine · done ✓ and −2.\n";

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown, Info(), TestContext.Current.CancellationToken);

        string text = AllText(pdf);
        foreach (char c in characters)
        {
            Assert.True(text.Contains(c), $"U+{(int)c:X4} did not survive text extraction.");
        }
    }

    // --- Plain text --------------------------------------------------------------------------------

    [Fact]
    public void PlainText_KeepsItsLineBreaks_AndWrapsALongLine()
    {
        string longLine = new('x', 2000);
        string text = "FIRSTLINEMARKER\nSECONDLINEMARKER\n" + longLine + "\n";

        byte[] pdf = BenchmarkPdfRenderer.RenderPlainText(text, Info(BenchmarkPdfClassification.Internal), TestContext.Current.CancellationToken);

        using var reader = PdfDocument.Open(pdf);
        var page = reader.GetPage(1);
        var words = page.GetWords().ToList();
        var first = words.First(w => w.Text.Contains("FIRSTLINEMARKER", StringComparison.Ordinal));
        var second = words.First(w => w.Text.Contains("SECONDLINEMARKER", StringComparison.Ordinal));
        Assert.True(second.BoundingBox.Bottom < first.BoundingBox.Bottom, "The second line is not below the first.");
        Assert.Contains(longLine, Squash(page.Text));
    }

    // --- Guard, cancellation and reproducibility ---------------------------------------------------

    [Fact]
    public void ASourceOverTheGuard_IsRefusedBeforeRendering()
    {
        string huge = new('a', BenchmarkPdfRenderer.MaxSourceCharacters + 1);

        var markdown = Assert.Throws<BenchmarkPdfSourceTooLargeException>(() => BenchmarkPdfRenderer.RenderMarkdown(huge, Info(), TestContext.Current.CancellationToken));
        var plain = Assert.Throws<BenchmarkPdfSourceTooLargeException>(() => BenchmarkPdfRenderer.RenderPlainText(huge, Info(), TestContext.Current.CancellationToken));

        Assert.Equal(BenchmarkPdfRenderer.MaxSourceCharacters + 1, markdown.Characters);
        Assert.Equal(BenchmarkPdfRenderer.MaxSourceCharacters + 1, plain.Characters);
        Assert.True(BenchmarkPdfRenderer.IsTooLarge(huge));
        Assert.False(BenchmarkPdfRenderer.IsTooLarge(huge[..BenchmarkPdfRenderer.MaxSourceCharacters]));
    }

    [Fact]
    public void ACanceledRender_Stops()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => BenchmarkPdfRenderer.RenderMarkdown("# A\n\nText.\n", Info(), cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => BenchmarkPdfRenderer.RenderPlainText("Text.", Info(), cts.Token));
    }

    [Fact]
    public void TheSameInput_RendersTheSameContentTwice()
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        var options = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Full,
            PeerNaming = BenchmarkReportPeerNaming.Named
        };
        string markdown = BenchmarkReportPackRenderer.Render(document, options);
        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, options, BenchmarkPdfPaper.Letter);

        // PDFs are reproducible in content but not bytes: PDF/UA makes QuestPDF embed a fresh document id.
        byte[] first = BenchmarkPdfRenderer.RenderMarkdown(markdown, info, TestContext.Current.CancellationToken);
        byte[] second = BenchmarkPdfRenderer.RenderMarkdown(markdown, info, TestContext.Current.CancellationToken);

        using var a = PdfDocument.Open(first);
        using var b = PdfDocument.Open(second);
        Assert.Equal(a.NumberOfPages, b.NumberOfPages);
        Assert.Equal(a.GetPages().Select(p => p.Text).ToList(), b.GetPages().Select(p => p.Text).ToList());
        Assert.Equal(a.Information.Title, b.Information.Title);
        Assert.Equal(a.Information.Subject, b.Information.Subject);
        Assert.Equal(a.Information.Keywords, b.Information.Keywords);
        Assert.Equal(a.Information.CreationDate, b.Information.CreationDate);
        Assert.Equal(a.Information.ModifiedDate, b.Information.ModifiedDate);
    }

    // --- File names --------------------------------------------------------------------------------

    [Theory]
    [InlineData("GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Technical Report",
        "gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark-technical-report")]
    [InlineData("..a__b..", "a_b")]
    [InlineData("  ", "export")]
    [InlineData("Ünïcode Name", "ncode-name")]
    public void SafeFileName_MirrorsTheClient(string input, string expected)
    {
        Assert.Equal(expected, BenchmarkPdfFileNames.SafeFileName(input));
    }

    [Fact]
    public void AReportDocumentName_IsTheDownloadCentersWithPdf()
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);

        Assert.Equal(
            "run-12_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark_Researcher_Report_detailed_anonymized.pdf",
            BenchmarkPdfFileNames.ForReportDocument(document, new BenchmarkReportRenderOptions
            {
                Disclosure = BenchmarkReportDisclosure.Detailed,
                PeerNaming = BenchmarkReportPeerNaming.Anonymized
            }));
        Assert.Equal(
            "run-12_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark_Researcher_Report_full_named_INTERNAL.pdf",
            BenchmarkPdfFileNames.ForReportDocument(document, new BenchmarkReportRenderOptions
            {
                Disclosure = BenchmarkReportDisclosure.Full,
                PeerNaming = BenchmarkReportPeerNaming.Named
            }));
        Assert.Equal("Suite_Model_20260928_104200_INTERNAL.pdf", BenchmarkPdfFileNames.InternalPdfName("Suite_Model_20260928_104200.md"));
    }

    [Fact]
    public void ARunSubjectsName_StartsWithTheRunNumber()
    {
        var document = BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary);
        document.SubjectKey = "run:73";
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named };

        string name = BenchmarkPdfFileNames.ForReportDocument(document, options);

        Assert.StartsWith("run-73_", name, StringComparison.Ordinal);
        Assert.Equal("run-73_" + BenchmarkPdfFileNames.SafeFileName(document.Title) + "_full_named_INTERNAL.pdf", name);
        Assert.Equal(name[..^".pdf".Length] + ".docx", BenchmarkPdfFileNames.ForReportDocument(document, options, "docx"));
    }

    [Theory]
    [InlineData("group:5")]
    [InlineData("run:")]
    [InlineData("run:7a")]
    public void AGroupSubjectsName_HasNoRunPrefix(string subjectKey)
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        document.SubjectKey = subjectKey;

        Assert.Equal(
            "gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark-executive-summary_summary_named.pdf",
            BenchmarkPdfFileNames.ForReportDocument(document, new BenchmarkReportRenderOptions
            {
                Disclosure = BenchmarkReportDisclosure.Summary,
                PeerNaming = BenchmarkReportPeerNaming.Named
            }));
    }

    [Fact]
    public void AResearcherReportWrittenUnderTheLegacyName_IsNamedAndTitledUnderTheNewOne()
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        document.Title = "GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Technical Report";
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Summary, PeerNaming = BenchmarkReportPeerNaming.Named };

        Assert.Equal(
            "run-12_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark_Researcher_Report_summary_named.pdf",
            BenchmarkPdfFileNames.ForReportDocument(document, options));
        Assert.Equal(
            "GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Report for AI Researchers and Developers",
            BenchmarkPdfDocumentInfo.ForReportDocument(document, options, BenchmarkPdfPaper.A4).Title);

        // The other audiences keep the title-derived name.
        var executive = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        Assert.Equal(
            "run-12_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark-executive-summary_summary_named.pdf",
            BenchmarkPdfFileNames.ForReportDocument(executive, options));
    }

    // --- Run file endpoints ------------------------------------------------------------------------

    [Fact]
    public async Task RunPdfEndpoints_AreNotFoundForAnUnknownRun_AndRefuseAnUnknownPaper()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = BenchmarkRunExamTests.InMemoryOptions();
        await using var db = new ApplicationDbContext(options);
        var controller = RunController(db);
        var body = new BenchmarkRunDiagnosticsPdfRequest { Text = "Diagnostics", CapturedAtUtc = "2026-09-28T10:42:00Z" };

        Assert.IsType<NotFoundResult>(await controller.GetRunReportPdf(9999, null, ct));
        Assert.IsType<NotFoundResult>(await controller.GetRunToolCallLogPdf(9999, "a4", ct));
        Assert.IsType<NotFoundResult>(await controller.RenderRunDiagnosticsPdf(9999, "letter", body, ct));

        Assert.IsType<BadRequestObjectResult>(await controller.GetRunReportPdf(9999, "a3", ct));
        Assert.IsType<BadRequestObjectResult>(await controller.GetRunToolCallLogPdf(9999, "legal", ct));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderRunDiagnosticsPdf(9999, "tabloid", body, ct));
    }

    [Fact]
    public async Task DiagnosticsPdf_RefusesEmptyText_AndAnUnreadableCaptureTime()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = BenchmarkRunExamTests.InMemoryOptions();
        var seeded = await BenchmarkRunExamTests.SeedSuiteWithRunsAsync(options);
        await using var db = new ApplicationDbContext(options);
        var controller = RunController(db);
        long runId = seeded.RunIds[0];

        Assert.IsType<BadRequestObjectResult>(await controller.RenderRunDiagnosticsPdf(runId, null, null, ct));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderRunDiagnosticsPdf(runId, null,
            new BenchmarkRunDiagnosticsPdfRequest { Text = "  ", CapturedAtUtc = "2026-09-28T10:42:00Z" }, ct));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderRunDiagnosticsPdf(runId, null,
            new BenchmarkRunDiagnosticsPdfRequest { Text = "Diagnostics", CapturedAtUtc = "yesterday" }, ct));
    }

    [Fact]
    public async Task DiagnosticsPdf_IsAnInternalPdf_DatedAtItsCapture()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = BenchmarkRunExamTests.InMemoryOptions();
        var seeded = await BenchmarkRunExamTests.SeedSuiteWithRunsAsync(options);
        await using var db = new ApplicationDbContext(options);
        var controller = RunController(db);
        long runId = seeded.RunIds[0];

        var file = Assert.IsType<FileContentResult>(await controller.RenderRunDiagnosticsPdf(runId, null,
            new BenchmarkRunDiagnosticsPdfRequest { Text = "Overseer build 1.1.2\nTool calls: 12", CapturedAtUtc = "2026-09-28T10:42:00Z" }, ct));

        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal($"Isolation_Suite_gpt-5.6-luna_run{runId}_diagnostics_INTERNAL.pdf", file.FileDownloadName);
        using var reader = PdfDocument.Open(file.FileContents);
        Assert.Equal(CreatedAt, reader.Information.GetCreatedDateTimeOffset()!.Value.UtcDateTime);
        Assert.Contains("Toolcalls:12", AllText(file.FileContents));
    }

    [Fact]
    public async Task RunReportPdf_IsNamedAfterTheMarkdown_AndDatedAtTheRunsCompletion()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = BenchmarkRunExamTests.InMemoryOptions();
        var seeded = await BenchmarkRunExamTests.SeedSuiteWithRunsAsync(options);
        await using var db = new ApplicationDbContext(options);
        var controller = RunController(db);
        long runId = seeded.RunIds[0];
        var completed = (await db.BenchmarkRuns.AsNoTracking().SingleAsync(r => r.Id == runId, ct)).CompletedAtUtc!.Value;

        var markdown = Assert.IsType<FileContentResult>(await controller.GetRunReport(runId));
        var pdf = Assert.IsType<FileContentResult>(await controller.GetRunReportPdf(runId, "letter", ct));

        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.EndsWith(".md", markdown.FileDownloadName, StringComparison.Ordinal);
        Assert.Equal(markdown.FileDownloadName[..^3] + "_INTERNAL.pdf", pdf.FileDownloadName);
        using var reader = PdfDocument.Open(pdf.FileContents);
        Assert.Equal(completed, reader.Information.GetCreatedDateTimeOffset()!.Value.UtcDateTime);
        Assert.All(reader.GetPages(), page => Assert.Contains("INTERNAL", page.Text));
    }

    [Fact]
    public async Task ToolCallLogPdf_IsNamedAfterTheMarkdown()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = BenchmarkRunExamTests.InMemoryOptions();
        var seeded = await BenchmarkRunExamTests.SeedSuiteWithRunsAsync(options);
        await using var db = new ApplicationDbContext(options);
        var controller = RunController(db);
        long runId = seeded.RunIds[0];

        var markdown = Assert.IsType<FileContentResult>(await controller.GetRunToolCallLog(runId));
        var pdf = Assert.IsType<FileContentResult>(await controller.GetRunToolCallLogPdf(runId, null, ct));

        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.Equal(markdown.FileDownloadName[..^3] + "_INTERNAL.pdf", pdf.FileDownloadName);
        Assert.Contains(Squash(BenchmarkPdfDocumentInfo.TeamOnlyStamp), AllText(pdf.FileContents));
    }

    // --- Helpers -----------------------------------------------------------------------------------

    /// <summary>Only the DbContext is used by the run-file actions.</summary>
    private static AdminBenchmarkController RunController(ApplicationDbContext db) => new(
        db, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);

    private static BenchmarkPdfDocumentInfo Info(BenchmarkPdfClassification classification = BenchmarkPdfClassification.ProviderConfidential) => new()
    {
        DocumentKind = "Technical Report",
        Title = "PDF Fixture Title",
        SubjectLine = "Fixture Suite · run #12",
        Classification = classification,
        ClassificationText = classification == BenchmarkPdfClassification.Internal
            ? BenchmarkPdfDocumentInfo.TeamOnlyStamp
            : "Confidential. Prepared for the fixture's provider.",
        Facts = new[] { new BenchmarkPdfFact("Run", "#12"), new BenchmarkPdfFact("Suite", "Fixture Suite") },
        CreatedAtUtc = CreatedAt,
        Keywords = new[] { "Fixture Model", "Fixture Suite", "GnollBench" }
    };

    /// <summary>The PDF column layout of the first table in <paramref name="markdown"/>.</summary>
    private static (BenchmarkPdfMarkdownComposer.CellAlign[] Aligns, float[] Widths, bool Constant) Layout(string markdown)
    {
        var table = BenchmarkPdfMarkdownComposer.Parse(markdown).OfType<Markdig.Extensions.Tables.Table>().First();
        var rows = table.OfType<Markdig.Extensions.Tables.TableRow>().ToList();
        var header = rows.Where(r => r.IsHeader).Select(r => r.OfType<Markdig.Extensions.Tables.TableCell>().ToList()).ToList();
        var body = rows.Where(r => !r.IsHeader).Select(r => r.OfType<Markdig.Extensions.Tables.TableCell>().ToList()).ToList();
        int columns = rows.Max(r => r.Count);
        return BenchmarkPdfMarkdownComposer.PdfColumnLayout(table, header, body, columns, markdown);
    }

    private static List<int> AllIndexesOf(string text, string value)
    {
        var indexes = new List<int>();
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            indexes.Add(i);
        }
        return indexes;
    }

    /// <summary>The text with all whitespace removed: extraction does not reliably keep spaces.</summary>
    private static string Squash(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static string AllText(byte[] pdf)
    {
        using var reader = PdfDocument.Open(pdf);
        return string.Concat(reader.GetPages().Select(p => Squash(p.Text)));
    }

    private static string? CatalogString(PdfDocument reader, string key)
    {
        if (!reader.Structure.Catalog.CatalogDictionary.Data.TryGetValue(key, out var token)) return null;
        if (token is IndirectReferenceToken reference) token = reader.Structure.GetObject(reference.Data).Data;
        return token is StringToken text ? text.Data : token.ToString();
    }

    /// <summary>An XMP property, written either as an element or as an attribute.</summary>
    private static bool HasXmpValue(XDocument xml, string ns, string localName, string value)
    {
        XName name = XName.Get(localName, ns);
        return xml.Descendants(name).Any(e => e.Value.Trim() == value)
            || xml.Descendants().Attributes(name).Any(a => a.Value.Trim() == value);
    }
}

/// <summary>
/// The QuestPDF process settings <c>Program.cs</c> applies at startup, for tests that render a PDF
/// without the application running.
/// </summary>
internal static class BenchmarkPdfTestSetup
{
    public static void Configure()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = false;
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
        BenchmarkPdfResources.EnsureRegistered();
    }
}
